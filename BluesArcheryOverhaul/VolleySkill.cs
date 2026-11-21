using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Alta.Impact;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // The Volley skill: a copy of the game's Slowdraw skill with its own id (47011), its own pull settings (68%,
    // [Archery] TwoShotSeconds, no timeout, no sounds), no events, no achievement and a flat x1 damage table.
    // Two-Shot and Three-Shot (and Burst) point at it, so their owners get a Volley level and the game's own input
    // machinery says when the volley is charged. Made once per process, before the tree is edited; the game never
    // looks it up by type, and nothing in this mod finds Slowdraw by type once it exists.
    internal static class VolleySkill {
        internal static BowSlowdraw? Skill;
        internal static BowStringPullbackStructure? Pull;
        internal static bool Registered;

        const int MinSkills = 10;
        static bool failed, errorLogged;
        static string lastNotYet = "";

        static readonly string[] SoundFields = { "chargedDefaultEffect", "executedDefaultEffect", "canceledDefaultEffect" };
        static readonly string[] EventFields = {
            "SkillChargedTransform", "SkillExecutedTransform", "SkillCanceledTransform", "SkillChargingStartedTransform",
            "hitPositionEffect", "hitPositionAndBladeAlignedEffect", "localPlayerChargedTransform", "localPlayerExecutedTransform",
            "localPlayerCanceledTransform", "localPlayerHitPositionEffect", "localPlayerHitPositionAndBladeAlignedEffect",
        };

        static void NotYet(string when, string reason) {
            string key = when + "|" + reason;
            if (key == lastNotYet) return;
            lastNotYet = key;
            MelonLogger.Warning($"[Archery] Volley skill: not registered at {when}: {reason}");
        }

        /// <summary>The error line, once: Two-Shot and Three-Shot keep the game's skills.</summary>
        internal static void LogCouldNotBeMade() {
            if (errorLogged) return;
            errorLogged = true;
            MelonLogger.Error("[Archery] Volley skill: could not be made; Two-Shot and Three-Shot keep the game's skills and Burst is off");
        }

        static void Fail(string when, string reason) {
            failed = true;
            NotYet(when, reason);
            LogCouldNotBeMade();
        }

        static bool IsVolleyId(object key) {
            try { return Convert.ToInt64(key) == SlotIds.VolleySkill; }
            catch { return false; }
        }

        /// <summary>Make and register the copy (idempotent). Attempts: start, every scene until done, and the tree load.</summary>
        static readonly int T_TryRegister = Timing.Section("VolleySkill.TryRegister");
        internal static bool TryRegister(string when) {
            using var _t = Timing.Begin(T_TryRegister, when);
            if (Registered) return true;
            if (failed) return false;
            // Made after the tree was edited it would be in no slot: give up for this process.
            if (Tree.Ready) { Fail(when, "the skill tree was already edited without it"); return false; }
            object? items;
            try {
                // The game never warms the skill table itself. CheckItems (Alta.Values.dll) fills it from
                // Resources.LoadAll<ProfessionSkill>("ProfessionSkill/"), the whole set whenever it is called, and keeps it only
                // when non-empty (an empty or failed load is retried), so calling it early cannot cache a partial table.
                Alta.HashedGeneralValue<ProfessionSkill>.CheckItems();
                items = Traverse.Create(typeof(Alta.HashedGeneralValue<ProfessionSkill>)).Field("items").GetValue();
            }
            catch (Exception e) { NotYet(when, $"the skill table could not be loaded yet ({e.Message})"); return false; } // may be too early: retried
            try {
                if (!(items is IDictionary table)) { NotYet(when, "the skill table is not readable"); return false; }
                int before = table.Count;
                if (before < MinSkills) { NotYet(when, $"the skill table holds {before} skills"); return false; }

                // The one type test: made before any copy exists. Tree.Slowdraw wins if the tree already found it.
                BowSlowdraw? slow = Tree.Slowdraw as BowSlowdraw;
                ProfessionSkill? taken = null;
                foreach (DictionaryEntry e in table) {
                    if (IsVolleyId(e.Key)) taken = e.Value as ProfessionSkill;
                    if (slow == null && e.Value is BowSlowdraw b && b != null && b.Hash != SlotIds.VolleySkill) slow = b;
                }
                if (slow == null) { NotYet(when, "no Slowdraw skill in the skill table yet"); return false; }
                if (taken != null) { Fail(when, $"skill id {SlotIds.VolleySkill} is already used by '{taken.name}'"); return false; }
                if (!(slow.InputStructure is BowStringPullbackStructure slowPull) || slowPull == null) {
                    Fail(when, "Slowdraw has no bow-string pull settings to copy");
                    return false;
                }

                // ---- the pull settings: own object, 68%, the Volley timer, no timeout, silent
                BowStringPullbackStructure pull = UnityEngine.Object.Instantiate(slowPull);
                pull.name = "Bow Volley Pullback";
                Traverse tp = Traverse.Create(pull);
                tp.Field("pullbackValue").SetValue(DrawClock.CrossingPull);
                tp.Field("minTimeToPullback").SetValue(Mathf.Max(0f, Tree.SharedVolleySeconds)); // the fastest build's; Tree.ApplyTimers keeps it so
                tp.Field("isTimingOut").SetValue(false);
                int sounds = 0;
                foreach (string f in SoundFields) {
                    FieldInfo? fi = AccessTools.Field(typeof(SkillInputStructure), f);
                    if (fi == null) { MelonLogger.Warning($"[Archery] Volley skill: no sound field '{f}' on the pull settings"); continue; }
                    fi.SetValue(pull, Activator.CreateInstance(fi.FieldType));
                    sounds++;
                }

                // ---- the skill: Slowdraw's copy with its own id, no practice link, no achievement, no events, flat x1
                BowSlowdraw skill = UnityEngine.Object.Instantiate(slow);
                skill.name = "Bow Volley";
                FieldInfo? hash = AccessTools.Field(typeof(ProfessionSkill), "hash");
                if (hash == null) { Fail(when, "the skill id field 'hash' was not found"); UnityEngine.Object.Destroy(skill); UnityEngine.Object.Destroy(pull); return false; }
                hash.SetValue(skill, Convert.ChangeType(SlotIds.VolleySkill, hash.FieldType)); // protected int hash: never pass the uint itself
                AccessTools.Field(typeof(ProfessionSkill), "skillPracticeTrigger").SetValue(skill, 0u);
                FieldInfo achievement = AccessTools.Field(typeof(ProfessionSkill), "targetAchievement");
                achievement.SetValue(skill, null);
                int events = 0;
                foreach (string f in EventFields) {
                    FieldInfo? fi = AccessTools.Field(typeof(PickupBasedSkill), f);
                    if (fi == null) { MelonLogger.Warning($"[Archery] Volley skill: no event field '{f}' on the skill"); continue; }
                    fi.SetValue(skill, Activator.CreateInstance(fi.FieldType));
                    events++;
                }
                AccessTools.Field(typeof(PickupBasedSkill), "inputStructure").SetValue(skill, pull);
                var flat = new FloatValuesPerLevel();
                AccessTools.Field(typeof(ValuePerLevel<float>), "valuePerLevel").SetValue(flat, new[] { 1f, 1f, 1f, 1f });
                AccessTools.Field(typeof(BowDrawSkill), "damageMultiplierPerLevel").SetValue(skill, flat);
                skill.hideFlags = HideFlags.DontUnloadUnusedAsset;
                pull.hideFlags = HideFlags.DontUnloadUnusedAsset;

                if (items is Dictionary<uint, ProfessionSkill> byUint) byUint[SlotIds.VolleySkill] = skill;
                else if (items is Dictionary<int, ProfessionSkill> byInt) byInt[(int)SlotIds.VolleySkill] = skill;
                else { Fail(when, $"the skill table is a {items.GetType().Name}, not keyed by id"); UnityEngine.Object.Destroy(skill); UnityEngine.Object.Destroy(pull); return false; }

                Skill = skill;
                Pull = pull;
                Registered = true;
                MelonLogger.Msg($"[Archery] Volley skill: registered '{skill.name}' id {skill.Hash} at {when} (skill table {before} -> {table.Count})");

                object? ownInputs = Traverse.Create(skill).Field("inputs").GetValue(), slowInputs = Traverse.Create(slow).Field("inputs").GetValue();
                bool ownMaps = ownInputs != null && !ReferenceEquals(ownInputs, slowInputs);
                bool separate = !ReferenceEquals(pull, slowPull) && ReferenceEquals(skill.InputStructure, pull) && !ReferenceEquals(skill.InputStructure, slow.InputStructure);
                bool noAchievement = achievement.GetValue(skill) == null;
                MelonLogger.Msg($"[Archery] Volley skill: own pull settings: threshold {pull.PullbackValue}, {pull.MinTimeToPullback} s, timeout {pull.IsTimingOut}; " +
                                $"Slowdraw keeps threshold {slowPull.PullbackValue}, {slowPull.MinTimeToPullback} s; separate settings objects: {(separate ? "yes" : "NO")}; " +
                                $"own input maps: {(ownMaps ? "yes" : "NO")}; achievement cleared: {(noAchievement ? "yes" : "NO")}; sounds cleared: {sounds}; events cleared: {events}");
                return true;
            }
            catch (Exception e) {
                Fail(when, e.ToString());
                return false;
            }
        }

        // ---------------------------------------------------------------- state hooks

        static readonly HashSet<string> livePatches = new HashSet<string>();

        // G2: one flag per patch (no string hashed per frame); the set still says each patch once.
        static bool live0;
        static void Live(string patch) { if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Volley skill: patch on {patch} is live"); } // names built at the call
        static void Live(ref bool said, string patch) {
            if (said) return;
            said = true;
            if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Volley skill: patch on {patch} is live");
        }

        // Server: the Volley skill's input on a bow string became active (its timer ran out above 68%) or not.
        [HarmonyPatch(typeof(BowDrawSkill), nameof(BowDrawSkill.InputChangedState))]
        static class ServerState {
            static readonly int T = Timing.Section("patch BowDrawSkill.InputChangedState / VolleySkill.ServerState");
            static void Postfix(BowDrawSkill __instance, bool isEnabled, ISkillInputMethod input, Dictionary<ISkillInputMethod, BowString> ___bowStrings) {
                using var _t = Timing.Begin(T);
                try {
                    if (Skill == null || !ReferenceEquals(__instance, Skill) || !NetworkSceneManager.IsServer || input == null || ___bowStrings == null) return;
                    Live(ref live0, "BowDrawSkill.InputChangedState");
                    if (!___bowStrings.TryGetValue(input, out BowString bow) || bow == null) return;
                    HeldPickupHandler handler = input.Handler;
                    Player? owner = handler != null && handler.PlayerHelper != null && handler.PlayerHelper.Player != null ? handler.PlayerHelper.Player : null;
                    Volley.ServerVolleyChanged(bow, isEnabled, owner);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Volley skill: state tracking failed: {e.Message}"); }
            }
        }

        // Clients: the game broadcasts every status of the Volley skill (ChargingStarted at the 68% crossing, Charged,
        // Cancelled, Consumed) and of Slowdraw (the charge), for the arrow on the string.
        [HarmonyPatch(typeof(PickupBasedSkill), nameof(PickupBasedSkill.SendSerializedMessage))]
        static class ClientStatus {
            static readonly int T = Timing.Section("patch PickupBasedSkill.SendSerializedMessage / VolleySkill.ClientStatus");
            static void Postfix(PickupBasedSkill __instance, IPlayer __0, Alta.Serialization.Stream __1, ToolBasedSkillStatus ___status, uint ___pickupIdentifier) {
                using var _t = Timing.Begin(T);
                if (__1 == null || !__1.IsReading || ApplicationManager.IsHeadless || NetworkSceneManager.IsServer || __instance == null) return;
                bool volley = Skill != null && ReferenceEquals(__instance, Skill);
                bool charge = !volley && ReferenceEquals(__instance, Tree.Slowdraw);
                bool quick = !volley && !charge && ReferenceEquals(__instance, Tree.Quickdraw); // to note the archer (Charge2Sound) and for SnapWeight's ember
                if (!volley && !charge && !quick) return;
                ToolBasedSkillStatus status = ___status;
                bool local = __0 != null && __0.IsLocalPlayer;
                string owner = Builds.NameOf(__0);
                try {
                    Live(volley ? "PickupBasedSkill.SendSerializedMessage (Volley)" : charge ? "PickupBasedSkill.SendSerializedMessage (Slowdraw)"
                         : "PickupBasedSkill.SendSerializedMessage (Quickdraw)");
                    NetworkSceneManager.Current.Spawner.WaitForSpawn(___pickupIdentifier, entity => {
                        try {
                            BowString? bow = Volley.BowOf(entity);
                            if (bow == null) {
                                // Normal once the arrow has left the bow; for Charged it means nothing will show: say why.
                                if (status == ToolBasedSkillStatus.Charged && volley)
                                    MelonLogger.Warning($"[Archery] Volley skill: charged on '{(entity != null ? entity.name : "null")}' for '{owner}' but its bow was not found");
                                return;
                            }
                            ChargeFx.NoteArcher(bow, owner, local); // the archer for the next status's glow and sound (NoGlow, Charge2Sound)
                            if (quick) {
                                // another Quick main's Charge Shot orbs: the ember on their arrowhead (looks only)
                                try { SnapWeight.Status(bow, status, owner, local, entity != null ? entity.Identifier : 0u); }
                                catch (Exception e) { MelonLogger.Warning($"[Archery] Snap weight: status handling failed: {e.Message}"); }
                                return;
                            }
                            if (volley) Volley.ClientVolleyStatus(bow, status, owner, local);
                            else { Volley.ClientChargeStatus(bow, status, owner, local); ChargeFx.Status(bow, status, owner, local); }
                        }
                        catch (Exception e) { MelonLogger.Warning($"[Archery] Volley skill: status handling failed: {e.Message}"); }
                    });
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Volley skill: status lookup failed: {e.Message}"); }
            }
        }

        // ---------------------------------------------------------------- the game's whole-arrow glow

        static string glowLogged = "";
        static readonly HashSet<uint> glowArrows = new HashSet<uint>(); // arrows whose Slowdraw glow decision was logged
        static bool slowdrawHidden;

        // Clients, on a Charged status (PickupBasedSkill.SendSerializedMessage). Slowdraw's glow goes only on an arrow where
        // the Fill is actually drawn (spec Part 9 item 15: never remove feedback before its replacement shows); Volley's
        // whenever HideGameGlow (the ghosts show it); Holy Arrow's only with HideHolyGlow (until Step 8's armed core).
        // ReleaseToolEffect is left alone: releasing a glow that was never requested is a no-op, and blocking it
        // would strand a glow if the setting is switched on while one is showing.
        [HarmonyPatch(typeof(ProfessionSkill), "RequestToolEffect")]
        static class NoGlow {
            static readonly int T = Timing.Section("patch ProfessionSkill.RequestToolEffect / VolleySkill.NoGlow");
            static bool Prefix(ProfessionSkill __instance, NetworkEntity entity) {
                using var _t = Timing.Begin(T);
                try {
                    if (__instance == null) return true;
                    bool slow = ReferenceEquals(__instance, Tree.Slowdraw);
                    bool volley = !slow && Skill != null && ReferenceEquals(__instance, Skill);
                    bool holy = !slow && !volley && ReferenceEquals(__instance, Tree.Holy);
                    if (!slow && !volley && !holy) return true;
                    // Only a Charge main (or no main path) has the charge and Holy Arrow: an off-path archer's glow never shows.
                    if ((slow || holy) && ChargeFx.OffPath(entity, MainPath.Charge, out MainPath main, out string archer)) {
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Glow: {(slow ? "Slowdraw" : "Holy Arrow")} skipped for '{archer}' (main path {main}, arrow {(entity != null ? entity.Identifier : 0u)})");
                        return false;
                    }
                    bool hide;
                    if (slow) {
                        hide = slowdrawHidden = Tree.HideSlowdrawGlow && ChargeFx.FillShownFor(entity);
                        if (hide) ChargeFx.GlowHidden(entity); // given back if the Fill goes before Cancelled/Consumed
                    }
                    else if (volley) hide = Tree.HideGameGlow;
                    else hide = Tree.HideHolyGlow;
                    if (!Logs.Debug) return !hide; // G2: the glow line and its log-only bookkeeping (glowArrows, glowLogged) are Debug
                    string line = $"[Archery] Glow: Slowdraw {(slowdrawHidden ? "hidden (the Fill shows the charge)" : "shown (no Fill on this arrow)")}, " +
                                  $"Volley {(Tree.HideGameGlow ? "hidden" : "shown")}, Holy Arrow {(Tree.HideHolyGlow ? "hidden" : "shown")}";
                    // Once per arrow for Slowdraw's decision (so every draw can be checked), and on any change otherwise.
                    // Only the first request per arrow is logged: Drop's re-request at release is not a new decision.
                    bool firstForArrow = false;
                    if (slow && entity != null) {
                        if (glowArrows.Count >= 64 && !glowArrows.Contains(entity.Identifier)) glowArrows.Clear();
                        firstForArrow = glowArrows.Add(entity.Identifier);
                    }
                    if (firstForArrow || line != glowLogged) {
                        glowLogged = line;
                        MelonLogger.Msg($"{line} (arrow {(entity != null ? entity.Identifier : 0u)})");
                    }
                    return !hide;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Glow: check failed: {e.Message}"); return true; }
            }
        }

        // ---------------------------------------------------------------- the real arrow's one-off multiplier

        // BowDrawSkill.OnArrowShot assigns the arrow's one-off multiplier from the skill's table, and handlers run in
        // subscription order: the copy's flat x1 would overwrite Slowdraw's charge if it ran last. The copy still
        // consumes (practice, the consumed status); the arrow keeps whatever it had before the copy's handler.
        [HarmonyPatch(typeof(BowDrawSkill), "OnArrowShot")]
        static class KeepMultiplier {
            static readonly int TPrefix = Timing.Section("patch BowDrawSkill.OnArrowShot / VolleySkill.KeepMultiplier prefix");
            static readonly int TPostfix = Timing.Section("patch BowDrawSkill.OnArrowShot / VolleySkill.KeepMultiplier postfix");
            static void Prefix(BowDrawSkill __instance, BowString bowString, NetworkEntity shotEntity, out KeyValuePair<ImpactTool, float>[]? __state) {
                using var _t = Timing.Begin(TPrefix);
                __state = null;
                try {
                    if (Skill == null || !ReferenceEquals(__instance, Skill) || !NetworkSceneManager.IsServer || shotEntity == null) return;
                    if (!Draws.InputActive(Skill, bowString)) return; // the original does nothing then
                    ImpactTool tool = shotEntity.GetComponent<ImpactTool>();
                    if (tool == null) return;
                    ImpactTool[] tools = Hits.Tools(tool);
                    __state = new KeyValuePair<ImpactTool, float>[tools.Length];
                    for (int i = 0; i < tools.Length; i++) __state[i] = new KeyValuePair<ImpactTool, float>(tools[i], tools[i].OneOffMultiplier);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Volley skill: could not record the arrow's multiplier: {e.Message}"); }
            }

            static void Postfix(NetworkEntity shotEntity, KeyValuePair<ImpactTool, float>[]? __state, bool __runOriginal) {
                using var _t = Timing.Begin(TPostfix);
                if (__state == null || !__runOriginal) return; // skipped (OwnTime.NotYet: before the archer's own volley time): nothing changed
                try {
                    foreach (KeyValuePair<ImpactTool, float> p in __state) if (p.Key != null) p.Key.OneOffMultiplier = p.Value;
                    ImpactTool own = shotEntity != null ? shotEntity.GetComponent<ImpactTool>() : null!;
                    float m = own != null ? own.OneOffMultiplier : 1f;
                    uint id = shotEntity != null ? shotEntity.Identifier : 0u;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley skill: consumed on arrow {id}; real arrow multiplier kept at x{m.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Volley skill: could not restore the arrow's multiplier: {e.Message}"); }
            }
        }
    }
}
