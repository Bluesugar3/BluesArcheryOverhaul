using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Alta.Global;
using Alta.Networking.Scripts.Player;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;

namespace QuickshotRing {
    // The shrine's rules for the archery tree, on the server:
    //   - only one ultimate (Quickdraw 4, Seeking Volley, Holy Arrow) can be bought; the others can still be tried.
    //     Loading a save never checks this, so a save with two keeps both.
    //   - after a sale, the game's own counts are rebuilt: selling a parent also removes its children, but the game
    //     only un-counts the orb that was sold, so the slot cap and the children's skills stay as if still owned.
    // And, on both sides, one log of what the tree costs and how much each tier multiplies damage.
    internal static class Shrine {
        static readonly HashSet<string> livePatches = new HashSet<string>();
        static readonly Dictionary<string, SkillStatus> blockLogged = new Dictionary<string, SkillStatus>(); // "player|orb" -> status last logged
        static bool warnedBlock, warnedFields;
        static AccessTools.FieldRef<PlayerProgressionManager, Dictionary<uint, int>>? skillLevel;
        static AccessTools.FieldRef<PlayerProgressionManager, int>? ownedCount;
        static AccessTools.FieldRef<PlayerProgressionManager, bool>? canBuyMore;
        const int LogTrimAt = 256;

        // G2: one flag per patch (no string hashed per frame); the set still says each patch once.
        static bool live0, live1;
        static void Live(string patch) { if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: patch on {patch} is live"); } // names built at the call
        static void Live(ref bool said, string patch) {
            if (said) return;
            said = true;
            if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: patch on {patch} is live");
        }

        static string Label(ProgressionSlot? slot) =>
            slot == null ? "?" : Tree.OrbBySlot.TryGetValue(slot.Hash, out Orb o) ? o.ToString() : (slot.Name ?? slot.Hash.ToString());

        static string Label(ProfessionSkillTree? tree, uint hash) =>
            tree != null && tree.GetSlot(hash, out ProgressionSlot s) ? Label(s)
            : Tree.OrbBySlot.TryGetValue(hash, out Orb o) ? o.ToString() : hash.ToString();

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- one buyable ultimate

        /// <summary>The ultimate orb behind this slot that the player may not buy, or None.</summary>
        static Orb BlockedUltimate(PlayerProgressionManager? pm, ProgressionSlot? slot, out Ultimates owned) {
            owned = Ultimates.None;
            if (pm == null || slot == null || !Tree.Ready || slot.Path != Tree.RangedPath) return Orb.None;
            if (!Tree.OrbBySlot.TryGetValue(slot.Hash, out Orb orb) || !UltimateRule.IsUltimate(orb)) return Orb.None;
            owned = Builds.ServerOwnedUltimates(pm);
            return UltimateRule.Blocks(orb, owned) ? orb : Orb.None;
        }

        static void Downgrade(PlayerProgressionManager pm, ProgressionSlot slot, ref SkillStatus result, uint freePoints, string patch) {
            if (!NetworkSceneManager.IsServer) return;
            Live(patch);
            string name = Builds.NameOf(pm != null ? pm.Player : null);
            Orb orb = BlockedUltimate(pm, slot, out Ultimates owned);
            if (orb == Orb.None) {
                if (blockLogged.Count > 0 && slot != null && Tree.OrbBySlot.TryGetValue(slot.Hash, out Orb any)) blockLogged.Remove(name + "|" + any);
                return;
            }
            SkillStatus was = result;
            var now = (SkillStatus)UltimateRule.Downgrade((int)was);
            if (now == was) return; // not buyable anyway (or already downgraded by the path's own check)
            result = now;
            string key = name + "|" + orb;
            if (blockLogged.TryGetValue(key, out SkillStatus last) && last == now) return;
            if (blockLogged.Count > LogTrimAt) blockLogged.Clear();
            blockLogged[key] = now;
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: '{name}' sees {orb} as {now} (was {was}; free points {freePoints}, cost {slot!.Cost}): they already own {owned}; only one ultimate can be bought (it can still be tried)");
        }

        [HarmonyPatch(typeof(PathExperience), nameof(PathExperience.IsSlotAvailable))]
        static class PathAvailable {
            static readonly int T = Timing.Section("patch PathExperience.IsSlotAvailable / Shrine.PathAvailable");
            [HarmonyPriority(Priority.Last)]
            static void Postfix(PathExperience __instance, ProgressionSlot slot, ref SkillStatus __result, PlayerProgressionManager ___targetPlayer) {
                using var _t = Timing.Begin(T);
                try { Downgrade(___targetPlayer, slot, ref __result, __instance.FreePoints, "PathExperience.IsSlotAvailable"); }
                catch (Exception e) { if (!warnedBlock) { warnedBlock = true; MelonLogger.Warning($"[Archery] Shrine: ultimate check failed: {e}"); } }
            }
        }

        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.IsSlotAvailable))]
        static class ManagerAvailable {
            static readonly int T = Timing.Section("patch PlayerProgressionManager.IsSlotAvailable / Shrine.ManagerAvailable");
            [HarmonyPriority(Priority.Last)]
            static void Postfix(PlayerProgressionManager __instance, ProgressionSlot slot, ref SkillStatus __result) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || slot == null) return;
                    Downgrade(__instance, slot, ref __result, (uint)__instance.GetFreePoints(slot.Path), "PlayerProgressionManager.IsSlotAvailable");
                    if (__result == SkillStatus.TestingLockedOwned) SaleLocked(__instance, slot);
                }
                catch (Exception e) { if (!warnedBlock) { warnedBlock = true; MelonLogger.Warning($"[Archery] Shrine: ultimate check failed: {e}"); } }
            }
        }

        // An owned orb in the try-out area that reads TestingLockedOwned cannot be sold: the game's rule while the server's
        // CanSellSkillsWithChildren is off (PathExperience.IsSlotAvailable), and the attempt does nothing and says nothing. One line per
        // player and orb (the set is trimmed like blockLogged).
        static readonly HashSet<string> sellLogged = new HashSet<string>();
        static void SaleLocked(PlayerProgressionManager pm, ProgressionSlot slot) {
            if (!Tree.Ready || slot.Path != Tree.RangedPath || !Tree.OrbBySlot.TryGetValue(slot.Hash, out Orb orb)) return;
            string name = Builds.NameOf(pm.Player);
            if (sellLogged.Count > LogTrimAt) sellLogged.Clear();
            if (!sellLogged.Add(name + "|" + orb)) return;
            var above = new List<string>();
            if (pm.PathExperiences.TryGetValue(slot.Path, out PathExperience pe) && pe != null)
                foreach (ProgressionSlot child in slot.InheritSlots) if (pe.UnlockedSlots.Contains(child.Hash)) above.Add(Label(child));
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: '{name}' holds {orb} to sell it, but it is locked: they own {(above.Count > 0 ? string.Join(", ", above.ToArray()) : "an orb")} above it (the server's CanSellSkillsWithChildren is off: sell from the top down)");
        }

        // Coexists with Tree.GathererRefund (load only). The game would refuse a downgraded orb too, but with a
        // misleading "not all dependant skills were unlocked" error; this says why.
        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.UnlockSlot))]
        static class RefuseSecondUltimate {
            static readonly int T = Timing.Section("patch PlayerProgressionManager.UnlockSlot / Shrine.RefuseSecondUltimate");
            [HarmonyPriority(Priority.First)]
            static bool Prefix(PlayerProgressionManager __instance, ProgressionSlot slot, bool isFromLoad) {
                using var _t = Timing.Begin(T);
                try {
                    if (isFromLoad || !NetworkSceneManager.IsServer || __instance == null) return true;
                    Live(ref live0, "PlayerProgressionManager.UnlockSlot (one ultimate)");
                    Orb orb = BlockedUltimate(__instance, slot, out Ultimates owned);
                    if (orb == Orb.None) return true;
                    MelonLogger.Warning($"[Archery] Shrine: refused to unlock {orb} for '{Builds.NameOf(__instance.Player)}': they already own {owned}");
                    return false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: unlock check failed, the game decides: {e.Message}"); return true; }
            }
        }

        // ---------------------------------------------------------------- sale fixes

        internal sealed class SaleState {
            public readonly Dictionary<uint, int> Levels;
            public readonly HashSet<uint> Slots;
            public SaleState(Dictionary<uint, int> levels, HashSet<uint> slots) { Levels = levels; Slots = slots; }
        }

        static bool Fields() {
            try {
                skillLevel ??= AccessTools.FieldRefAccess<PlayerProgressionManager, Dictionary<uint, int>>("skillLevel");
                ownedCount ??= AccessTools.FieldRefAccess<PlayerProgressionManager, int>("currentlyOwnedSkillsCount");
                canBuyMore ??= AccessTools.FieldRefAccess<PlayerProgressionManager, bool>("canBuyMoreSkills");
                return true;
            }
            catch (Exception e) {
                if (!warnedFields) { warnedFields = true; MelonLogger.Error($"[Archery] Sale: cannot reach the game's skill counts; sales are left to the game: {e.Message}"); }
                return false;
            }
        }

        // Selling a parent removes its children too (PathExperience.RecursiveLockSlot), and Lock runs on each child
        // while the manager still counts it, so the child's skill stays registered and the slot cap keeps counting
        // it. RemoveFreePoints/RemoveSlots reach this with no manager call at all. Runs before Builds.OnPathLockSlot
        // (ruling 3), so the build refresh sees the corrected counts.
        [HarmonyPatch(typeof(PathExperience), nameof(PathExperience.LockSlot))]
        static class Sale {
            static readonly int TPrefix = Timing.Section("patch PathExperience.LockSlot / Shrine.Sale prefix");
            static readonly int TPostfix = Timing.Section("patch PathExperience.LockSlot / Shrine.Sale postfix");
            [HarmonyPriority(Priority.First)]
            static void Prefix(PathExperience __instance, PlayerProgressionManager ___targetPlayer, out SaleState? __state) {
                using var _t = Timing.Begin(TPrefix);
                __state = null;
                try {
                    if (!NetworkSceneManager.IsServer || ___targetPlayer == null || !Fields()) return;
                    __state = new SaleState(new Dictionary<uint, int>(skillLevel!(___targetPlayer)), new HashSet<uint>(__instance.UnlockedSlots));
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Sale: could not record the counts before the sale: {e.Message}"); }
            }

            [HarmonyPriority(Priority.First)]
            static void Postfix(PathExperience __instance, PlayerProgressionManager ___targetPlayer, ProgressionSlot slot, SaleState? __state) {
                using var _t = Timing.Begin(TPostfix);
                if (__state == null) return;
                try {
                    Live(ref live1, "PathExperience.LockSlot (sale fix)");
                    PlayerProgressionManager pm = ___targetPlayer;
                    string name = Builds.NameOf(pm.Player);
                    ProfessionSkillTree tree = ProfessionSkillTree.GetProfessionTree(__instance.Path);
                    var left = new HashSet<uint>(__instance.UnlockedSlots);
                    List<uint> removed = __state.Slots.Where(h => !left.Contains(h)).ToList();
                    string sold = Label(slot);
                    if (pm.PracticingOrb != null) {
                        MelonLogger.Warning($"[Archery] Sale: '{name}' sold {sold} while trying an orb; counts left to the game");
                        return;
                    }
                    List<string> also = removed.Where(h => slot == null || h != slot.Hash).Select(h => Label(tree, h)).ToList();

                    int counted = ownedCount!(pm);
                    // Builds.OnUpdateCounts puts the test build's granted levels back on top (the play-test kit's TestBuild.Reapply, through KitHooks.Recounted) and
                    // refreshes; calling Reapply here as well would count them twice.
                    pm.UpdateCounts();
                    int owned = ownedCount!(pm);
                    uint cap = GlobalSettings<ProgressionSettings>.Instance.MaxBoughtSkills;
                    bool canBuy = cap < 1 || cap > owned;
                    canBuyMore!(pm) = canBuy;

                    // The skills behind the sold and removed slots that no longer have a level: switch them off.
                    var candidates = new List<ProgressionSlot>();
                    if (slot != null) candidates.Add(slot);
                    foreach (uint h in removed) if (tree != null && tree.GetSlot(h, out ProgressionSlot s) && !candidates.Contains(s)) candidates.Add(s);
                    var off = new List<string>();
                    foreach (uint skillHash in SkillLevels.Dropped(__state.Levels, skillLevel!(pm))) {
                        ProgressionSlot? match = candidates.FirstOrDefault(c => c.Skill != null && c.Skill.Hash == skillHash);
                        if (match == null || pm.GetLevel(match.Skill) != 0) continue;
                        match.Skill.Lock(pm);
                        off.Add(match.Skill.name);
                    }
                    Builds.Refresh(pm);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Sale: '{name}' sold {sold}; also removed: {(also.Count > 0 ? string.Join(", ", also.ToArray()) : "none")}; owned orbs {owned} (the game had counted {counted}), can buy more: {(canBuy ? "yes" : "no")}; skills switched off: {(off.Count > 0 ? string.Join(", ", off.ToArray()) : "none")}");
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Sale: fixing the counts after a sale failed: {e}"); }
            }
        }

        // ---------------------------------------------------------------- practice by use (server)

        /// <summary>Server: when the owner is trying out this orb in the shrine, using it once (`how`) counts as its practice.
        /// (Restored from before the Brace/Overcharge removal.) Callers: <see cref="PractiseOnShot"/>; each later orb's batch
        /// calls it when the orb's "Practised by" action happens.</summary>
        static readonly int T_Practise = Timing.Section("Shrine.Practise");
        internal static void Practise(Player? owner, Orb orb, string how) {
            using var _t = Timing.Begin(T_Practise, how);
            try {
                if (!NetworkSceneManager.IsServer || owner == null) return;
                PlayerProgressionManager pm = owner.ProgressionManager;
                ProgressionSlot? slot = Tree.SlotOf(orb);
                SkillOrb? trying = pm != null ? pm.PracticingOrb : null;
                if (pm == null || slot == null || trying == null || trying.Slot == null || trying.Slot.Hash != slot.Hash) return;
                if (pm.IsSlotPracticed(slot)) return; // once per player and orb: no repeated mark, message or log line
                bool first = pm.MarkSlotPracticed(slot.Path, slot.Hash);
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: '{Builds.NameOf(owner)}' practised {orb} by {how} (first time: {(first ? "yes" : "no")})");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: could not mark {orb} as practised: {e.Message}"); }
        }

        /// <summary>Server: the owner has this orb in the shrine's try-out area right now (Shatter: a networked thing there counts as struck; it is practised on the charged release, Volley.Launch).</summary>
        internal static bool Trying(Player? owner, Orb orb) {
            try {
                if (!NetworkSceneManager.IsServer || owner == null) return false;
                PlayerProgressionManager pm = owner.ProgressionManager;
                SkillOrb? trying = pm != null ? pm.PracticingOrb : null;
                ProgressionSlot? slot = Tree.SlotOf(orb);
                return trying != null && trying.Slot != null && slot != null && trying.Slot.Hash == slot.Hash;
            }
            catch { return false; }
        }

        /// <summary>Server, every release (Draws.Release): the held orb is practised by the shot when PracticeRule (Preview.cs)
        /// says any shot does for this player's main path: an orb that does nothing for it, or one whose own action is not
        /// built yet. Otherwise its own action practises it (AutoDock, Ricochet and Shatter call <see cref="Practise"/>).</summary>
        static readonly int T_PractiseOnShot = Timing.Section("Shrine.PractiseOnShot");
        internal static void PractiseOnShot(Player? owner) {
            using var _t = Timing.Begin(T_PractiseOnShot);
            try {
                if (!NetworkSceneManager.IsServer || owner == null) return;
                PlayerProgressionManager pm = owner.ProgressionManager;
                SkillOrb? trying = pm != null ? pm.PracticingOrb : null;
                if (trying == null || trying.Slot == null || !Tree.OrbBySlot.TryGetValue(trying.Slot.Hash, out Orb orb)) return;
                MainPath main = Builds.ServerBuild(owner).Main;
                if (PracticeRule.For(orb, main) != PracticeBy.AnyShot) return;
                Practise(owner, orb, $"a shot while it was held (it does nothing for a {main} main path, or its own practice is not built yet)");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: shot practice check failed: {e.Message}"); }
        }

        // ---------------------------------------------------------------- costs and multipliers, at load (both sides)

        static readonly int T_LogTree = Timing.Section("Shrine.LogTree");
        internal static void LogTree(ProfessionSkillTree tree) {
            using var _t = Timing.Begin(T_LogTree);
            try {
                var costs = new List<string>();
                foreach (KeyValuePair<uint, Orb> p in Tree.OrbBySlot.OrderBy(p => p.Value))
                    costs.Add(tree.GetSlot(p.Key, out ProgressionSlot s) ? $"{p.Value}={s.Cost}" : $"{p.Value}=?");
                MelonLogger.Msg($"[Archery] Tree: orb costs {string.Join(" ", costs.ToArray())}");
                string Levels(Func<int, float> at) => string.Join(" ", Enumerable.Range(1, 4).Select(l => F(at(l))).ToArray());
                MelonLogger.Msg($"[Archery] Probe: damage by level 1-4: Quickdraw {Levels(Tree.QuickMultiplier)}, Slowdraw {Levels(Tree.ChargeMultiplier)}, " +
                                $"Volley {(VolleySkill.Registered ? Levels(Tree.VolleyMultiplier) : "none (no Volley skill)")}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Tree: cost and multiplier log failed: {e.Message}"); }
        }
    }
}
