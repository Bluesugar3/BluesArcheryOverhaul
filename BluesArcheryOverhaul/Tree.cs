using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta.Global;
using Alta.Inventory;

using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {

    /// <summary>The passive skill behind each side orb (one instance per orb, its own fixed id in
    /// <see cref="SlotIds.Skills"/>). No behaviour of its own: each orb's code asks whether a player owns the orb.</summary>
    public class PassiveOrbSkill : ProfessionSkill { }

    // Edits to the Ranged skill tree, made at load on BOTH server and client so they agree:
    //   - a fourth Quickdraw orb ("Quickdraw 4")
    //   - the one gatherer orb (Arrow Gatherer: feathers, arrows, and since H3 Quiver Gather's return to the quiver)
    //   - Two-Shot and Three-Shot re-pointed at the Volley skill (VolleySkill.cs); Two-Shot needs nothing first
    //   - a Burst orb (the Volley skill's third level) above Three-Shot
    //   - Holy Arrow (Lightbeam) re-linked to sit after Charge 3, at the top of the Slowdraw column
    //   - draw timers for the Volley skill and Holy Arrow
    //   - the mod's own icons on those orbs
    // The shrine builds its orbs and connecting lines from this list, and a skill's level is how many of its
    // orbs a player owns. Saves store orb ids and skip unknown ones, so removing the mod is safe.
    internal static class Tree {
        const uint Quickdraw4SlotHash = SlotIds.Quickdraw4;   // unchanged since 0.3.0 so an already-unlocked orb survives


        /// <summary>Slowdraw is found once, in Inject's first pass (before the Volley copy, also a BowSlowdraw, is in the
        /// tree); everything afterwards compares by reference.</summary>
        internal static BowDrawSkill? Quickdraw, Slowdraw;
        /// <summary>The game's own Two-Shot and Three-Shot skills (their slots point at the Volley skill once re-pointed).</summary>
        internal static BowMultiArrowShot? TwoShot, ThreeShot;
        internal static ProgressionSlot? TwoShotSlot, ThreeShotSlot, BurstSlot;
        /// <summary>Slowdraw's own slots (Charge 1-3), by depth, recorded before any re-point.</summary>
        static readonly List<ProgressionSlot> chargeSlots = new List<ProgressionSlot>();
        internal static LightBeamSkill? Holy;
        internal static int Quickdraw4Level = int.MaxValue;
        internal static bool Ready;

        static readonly Dictionary<uint, Orb> orbBySlot = new Dictionary<uint, Orb>();
        /// <summary>The archery orb behind each Ranged slot hash (the build bits are made from it). Empty until the tree is injected.</summary>
        internal static IReadOnlyDictionary<uint, Orb> OrbBySlot => orbBySlot;
        static readonly Dictionary<Orb, ProgressionSlot> slotByOrb = new Dictionary<Orb, ProgressionSlot>();
        /// <summary>The Ranged tree's path. Means nothing until <see cref="Ready"/>.</summary>
        internal static ProgressionPath RangedPath;

        static MelonPreferences_Entry<float> twoShotSeconds = null!, threeShotSeconds = null!, holySeconds = null!, rippleSeconds = null!, rackSeconds = null!;
        static MelonPreferences_Entry<bool> hideGameGlow = null!, hideHolyGlow = null!, hideSlowdrawGlow = null!;
        static MelonPreferences_Entry<float> stage1Seconds = null!, stage2Seconds = null!, stage3Seconds = null!, stage1Damage = null!, stage2Damage = null!, stage3Damage = null!;
        static MelonPreferences_Entry<string> posHoly = null!, posArrow = null!, posBurst = null!;
        static MelonPreferences_Entry<int> burstCost = null!;
        // The later orbs (spec 2026-11-16, NewOrbs in Preview.cs): [Tree] <Orb>Orb, <Orb>Cost and [TreeLayout] <Orb>.
        static readonly Dictionary<Orb, MelonPreferences_Entry<bool>> orbOn = new Dictionary<Orb, MelonPreferences_Entry<bool>>();
        static readonly Dictionary<Orb, MelonPreferences_Entry<int>> orbCost = new Dictionary<Orb, MelonPreferences_Entry<int>>();
        static readonly Dictionary<Orb, MelonPreferences_Entry<string>> orbPlace = new Dictionary<Orb, MelonPreferences_Entry<string>>();
        /// <summary>H3 section 5: [TreeLayout] entries of the game's own slots (Quickdraw 1-3, Charge Shot 1-3, Two-Shot, Three-Shot) and Quickdraw 4.</summary>
        static readonly Dictionary<Orb, MelonPreferences_Entry<string>> gamePlace = new Dictionary<Orb, MelonPreferences_Entry<string>>();
        static readonly Orb[] GameSlotOrbs = { Orb.Quick1, Orb.Quick2, Orb.Quick3, Orb.Quickdraw4, Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Volley, Orb.FullVolley };
        static readonly Dictionary<Orb, ProgressionSlot> newSlots = new Dictionary<Orb, ProgressionSlot>();
        /// <summary>Icon name -> the parent slot whose icon an orb shows while its own PNG is missing.</summary>
        static readonly Dictionary<string, ProgressionSlot> iconParent = new Dictionary<string, ProgressionSlot>();
        static readonly HashSet<string> iconFallbackLogged = new HashSet<string>();
        static ProgressionSlot? mergedGatherer;
        static ProfessionSkillTree? rangedTree;
        static readonly List<KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>>> placed = new List<KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>>>();
        /// <summary>The position "auto" means for a placed slot, worked out once in Inject.</summary>
        static readonly Dictionary<ProgressionSlot, Vector3> autoPosition = new Dictionary<ProgressionSlot, Vector3>();

        /// <summary>Seconds at full draw until Holy Arrow is ready.</summary>
        internal static float HolySeconds => holySeconds != null ? holySeconds.Value : DrawTimeline.HolyTotal;
        /// <summary>Seconds after the 68% crossing until the five Volley ghosts (the Volley skill's own timer).</summary>
        internal static float TwoShotSeconds => twoShotSeconds != null ? twoShotSeconds.Value : VolleyTimes.Default.VolleyAt;
        /// <summary>Seconds after the 68% crossing until the Full Volley ratchet starts.</summary>
        internal static float ThreeShotSeconds => threeShotSeconds != null ? threeShotSeconds.Value : VolleyTimes.Default.FullAt;
        internal static float RippleSeconds => rippleSeconds != null ? rippleSeconds.Value : VolleyTimes.Default.RippleAt;
        internal static float RackSeconds => rackSeconds != null ? rackSeconds.Value : VolleyTimes.Default.RackAt;
        internal static bool HideGameGlow => hideGameGlow == null || hideGameGlow.Value;
        internal static bool HideHolyGlow => hideHolyGlow != null && hideHolyGlow.Value;
        internal static bool HideSlowdrawGlow => hideSlowdrawGlow != null && hideSlowdrawGlow.Value;
        /// <summary>When each Charge orb's stage completes (shared settings; stage 1 is also Slowdraw's own timer).</summary>
        internal static ChargeStages Stages => stage1Seconds == null ? ChargeStages.Default
            : new ChargeStages(stage1Seconds.Value, stage2Seconds.Value, stage3Seconds.Value);

        // (Slowdraw's own timer is now the fastest build's, shared by every player: read an archer's stage 1 from
        // Paths.StagesFor(build), never from the game's pull settings.)
        static bool injected;
        static DateTime iconStamp;
        static readonly Dictionary<string, ProgressionSlot> iconSlots = new Dictionary<string, ProgressionSlot>();
        static AccessTools.FieldRef<BowDrawSkill, FloatValuesPerLevel>? damageTable;
        static bool warnedMultiplier;

        internal static void Init() {
            var cat = Cfg.Category("Archery");
            twoShotSeconds = Cfg.Shared(cat.CreateEntry("TwoShotSeconds", VolleyTimes.Default.VolleyAt, description: "Seconds after the 68% crossing until the five Volley ghosts appear, for a multi-arrow main (Quickdraw orbs shorten it, [Complements] VolleyFasterPerOrb). The Volley skill's own timer is set to the fastest build's time and each archer waits for their own."));
            threeShotSeconds = Cfg.Shared(cat.CreateEntry("ThreeShotSeconds", VolleyTimes.Default.FullAt, description: "Seconds after the 68% crossing until the Full Volley ratchet starts."));
            rippleSeconds = Cfg.Shared(cat.CreateEntry("RippleSeconds", VolleyTimes.Default.RippleAt, description: "Seconds after the 68% crossing until the full-volley ripple when no Charge orb is owned (with one, the ripple comes at Charge Shot's own charge time)."));
            rackSeconds = Cfg.Shared(cat.CreateEntry("RackSeconds", VolleyTimes.Default.RackAt, description: "Seconds after the 68% crossing until Burst's rack."));
            hideGameGlow = cat.CreateEntry("HideGameGlow", true, description: "Hide the game's whole-arrow glow on Charge Shot while the Fill is drawn on that arrow, and on Volley (the ghosts show it). Holy Arrow's glow: see HideHolyGlow.");
            hideSlowdrawGlow = cat.CreateEntry("HideSlowdrawGlow", false, description: "Hide the game's whole-arrow glow on a charged arrow while the mod's charge strands are drawn on it. Off: the glow and the strands both show.");
            var stages = Cfg.Category("Charge");
            ChargeStages sd = ChargeStages.Default;
            stage1Seconds = Cfg.Shared(stages.CreateEntry("Stage1Seconds", sd.S1, description: "Seconds after the 68% crossing until Charge 1 completes, for a Charge Shot main without Quickdraw orbs (the game's own charge timer is set to the fastest build's time and each archer waits for their own)."));
            stage2Seconds = Cfg.Shared(stages.CreateEntry("Stage2Seconds", sd.S2, description: "Seconds until Charge 2 completes, for owners of the second Charge orb. A release before it is a Charge 1 shot."));
            stage3Seconds = Cfg.Shared(stages.CreateEntry("Stage3Seconds", sd.S3, description: "Seconds until Charge 3 completes, for owners of the third Charge orb."));
            // play-test 7 balance (the user: a full Charge Shot 3 must hit "much harder per arrow" than a Quickdraw 4 line)
            ChargeDamage dd = ChargeDamage.Default;
            stage1Damage = Cfg.Shared(stages.CreateEntry("Stage1Damage", dd.S1, description: "Damage multiplier on the real arrow of a charged shot that reached stage 1 (a Charge Shot main; replaces the game's own Charge Shot x1.25). Shatter's blast is a share of that hit."));
            stage2Damage = Cfg.Shared(stages.CreateEntry("Stage2Damage", dd.S2, description: "... at stage 2 (the game's own Charge Shot table said x1.5)."));
            stage3Damage = Cfg.Shared(stages.CreateEntry("Stage3Damage", dd.S3, description: "... at stage 3 (the game's own Charge Shot table said x2)."));
            hideHolyGlow = cat.CreateEntry("HideHolyGlow", false, description: "Hide the game's glow when Holy Arrow is ready. Off until the armed core replaces it (Step 8).");
            holySeconds = Cfg.Shared(cat.CreateEntry("HolySeconds", DrawTimeline.HolyTotal, description: "Seconds at full draw until Holy Arrow is ready. The volley folds in over the last [Volley] GatherSeconds of this."));
            // Where every orb sits in the shrine: "depth, sideways, height" in metres (H3 section 5: the owner's even grid, one table in
            // OrbPlaces.Default; every orb a setting). The server decides; restart not needed, reopen the shrine.
            var layout = Cfg.Category("TreeLayout");
            posHoly = Cfg.Shared(layout.CreateEntry("HolyArrow", OrbPlaces.Default(Orb.HolyArrow), description: "Where the Holy Arrow orb sits: depth, sideways, height; auto = continue the Charge Shot column above Charge 3."));
            posBurst = Cfg.Shared(layout.CreateEntry("Burst", OrbPlaces.Default(Orb.Burst), description: "Where the Burst orb sits: depth, sideways, height; auto = above Three-Shot."));
            posArrow = Cfg.Shared(layout.CreateEntry("ArrowGatherer", OrbPlaces.Default(Orb.Gatherer), description: "Where the one gatherer orb sits (feathers, arrows, and arrows back into the quiver): depth, sideways, height."));
            // the game's own slots and Quickdraw 4 (H3): "auto" puts each back where the game (or for Quickdraw 4 the mod's old pick) had it
            foreach (Orb o in GameSlotOrbs)
                gamePlace[o] = Cfg.Shared(layout.CreateEntry(OrbPlaces.SettingName(o), OrbPlaces.Default(o), description: $"Where the {Lines.Name(o)} orb sits: depth, sideways, height; auto = the game's own spot."));
            var costs = Cfg.Category("Tree");
            burstCost = Cfg.Shared(costs.CreateEntry("BurstCost", 1, description: "Skill points for the Burst orb. Changing it after someone bought the orb refunds the wrong amount when they sell it."));
            foreach (Orb o in NewOrbs.All) {
                string name = Lines.Name(o);
                bool built = NewOrbs.OnByDefault(o);
                orbOn[o] = Cfg.Shared(costs.CreateEntry(o + "Orb", built, description: $"Put the {name} orb in the archery shrine (server and every client alike; restart both to change). Off: it is not in the shrine and none of its code runs (the play-test kit's [TestBuild] cannot lend it either); a player who bought it gets its skill points back the next time their save loads, and loses the orb." + (built ? "" : " Off by default: parked, or not built yet.")));
                orbCost[o] = Cfg.Shared(costs.CreateEntry(o + "Cost", 1, description: $"Skill points for the {name} orb. Changing it after someone bought the orb refunds the wrong amount when they sell it."));
                orbPlace[o] = Cfg.Shared(layout.CreateEntry(o.ToString(), NewOrbs.DefaultPlace(o), description: $"Where the {name} orb sits: depth, sideways, height."));
            }
            Cfg.Reloaded += () => { if (Ready) { ApplyTimers(); ApplyCosts(); ApplyLayout(); } };
        }

        /// <summary>[Tree] &lt;Orb&gt;Orb: the orb is in the shrine and its code runs (false for an orb that is not one of the later orbs).</summary>
        internal static bool OrbOn(Orb o) => orbOn.TryGetValue(o, out MelonPreferences_Entry<bool> e) && e.Value;

        /// <summary>The Ranged slot behind an archery orb (reverse of <see cref="OrbBySlot"/>); null when the orb has no slot.</summary>
        internal static ProgressionSlot? SlotOf(Orb orb) => slotByOrb.TryGetValue(orb, out ProgressionSlot s) ? s : null;

        /// <summary>H4: what the one gatherer orb (the game's Arrow Gatherer) costs; 0 when the tree is not read yet.</summary>
        static uint GathererCost() {
            try { ProgressionSlot? s = SlotOf(Orb.Gatherer); return s != null ? s.Cost : 0u; }
            catch { return 0u; }
        }

        /// <summary>H4: the game's banked-points cap (0 = no cap, or not readable).</summary>
        static uint BankedCap() {
            try { return Alta.Global.GlobalSettings<ProgressionSettings>.Instance.MaxFreePoints; }
            catch { return 0u; }
        }

        /// <summary>The damage multiplier a charged shot gets at this Charge tier: 1 at tier 0, else Slowdraw's own
        /// table at that level (the table clamps past its end).</summary>
        internal static float ChargeMultiplier(int tier) => MultiplierAt(Slowdraw, tier, "Slowdraw");

        /// <summary>The mod's own charge table ([Charge] StageNDamage, shared): the real arrow of a main path's charged release.</summary>
        internal static ChargeDamage ChargeDamageTable => stage1Damage == null ? ChargeDamage.Default
            : new ChargeDamage(stage1Damage.Value, stage2Damage.Value, stage3Damage.Value);

        /// <summary>The real arrow's damage multiplier at a charge stage (1 at 0).</summary>
        internal static float ChargeDamageAt(int tier) => ChargeDamageTable.At(tier);

        /// <summary>The damage multiplier of a Quickdraw shot at this Quick tier: 1 at tier 0, else Quickdraw's own table at that level.</summary>
        internal static float QuickMultiplier(int tier) => MultiplierAt(Quickdraw, tier, "Quickdraw");

        /// <summary>The Volley skill's own table at this tier (a flat x1: the copy never changes the real arrow's damage).</summary>
        internal static float VolleyMultiplier(int tier) => MultiplierAt(VolleySkill.Skill, tier, "Volley");

        static float MultiplierAt(BowDrawSkill? skill, int tier, string what) {
            if (tier <= 0 || skill == null) return 1f;
            try {
                damageTable ??= AccessTools.FieldRefAccess<BowDrawSkill, FloatValuesPerLevel>("damageMultiplierPerLevel");
                FloatValuesPerLevel table = damageTable(skill);
                if (table != null) return table.GetForLevel(tier);
            }
            catch (Exception e) {
                if (!warnedMultiplier) { warnedMultiplier = true; MelonLogger.Warning($"[Archery] Could not read the {what} damage table: {e.Message}"); }
            }
            return 1f;
        }

        // ---------------------------------------------------------------- G3: the client loads the tree itself (diagnosis 2)

        // On a client nothing asked for a skill tree until a shrine loaded, so until then Tree.Slowdraw (and Quickdraw) were null and
        // VolleySkill.ClientStatus dropped every Charge Shot and Quickdraw status: no strands or thumps, and the shooter resolved
        // its charged shots as Early while the server dealt the full charge. The game's own GetProfessionTree (_decomp
        // ProfessionSkillTree.cs:67) loads every tree from Resources on its first call and the postfix below injects; a client asks
        // for the Ranged tree once a second from Main.OnUpdate, only once it has joined a world (Player.Current: the game's own data
        // and the skill table are loaded by then), until the tree is injected. Nothing is asked after that.
        static float nextClientLoad;
        static string? trigger;
        static bool warnedClientLoad;

        internal static void ClientLoad() {
            if (Ready || injected || ApplicationManager.IsHeadless || NetworkSceneManager.IsServer || Time.unscaledTime < nextClientLoad) return;
            nextClientLoad = Time.unscaledTime + 1f;
            if (global::Player.Current == null) return; // not in a world yet
            trigger = "this client, after joining (no shrine needed)";
            try { ProfessionSkillTree.GetProfessionTree(ProgressionPath.Ranged); }
            catch (Exception e) { if (!warnedClientLoad) { warnedClientLoad = true; MelonLogger.Warning($"[Archery] Tree: asking the game for the Ranged tree failed (tried again each second): {e.Message}"); } }
            finally { trigger = null; }
        }

        [HarmonyPatch(typeof(ProfessionSkillTree), nameof(ProfessionSkillTree.GetProfessionTree))]
        static class OnTreeLoaded {
            static readonly int TPrefix = Timing.Section("patch ProfessionSkillTree.GetProfessionTree / Tree.OnTreeLoaded prefix");
            static readonly int TPostfix = Timing.Section("patch ProfessionSkillTree.GetProfessionTree / Tree.OnTreeLoaded postfix");
            // Last chance to make the Volley skill before the tree is edited. This runs on every experience gain, so it
            // returns at once once there is nothing left to do.
            static void Prefix() {
                using var _t = Timing.Begin(TPrefix);
                if (VolleySkill.Registered || Ready || injected) return;
                VolleySkill.TryRegister("tree load");
            }

            static void Postfix() {
                using var _t = Timing.Begin(TPostfix);
                if (injected) return;
                injected = true; // set first: the code below calls back into the tree
                try { Inject(); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Tree: edits failed, archery changes are off: {e}"); }
            }
        }

        static readonly int T_Inject = Timing.Section("Tree.Inject");
        static void Inject() {
            using var _t = Timing.Begin(T_Inject);
            var trees = Traverse.Create(typeof(ProfessionSkillTree)).Field("treeMap").GetValue<Dictionary<ProgressionPath, ProfessionSkillTree>>();
            if (trees == null || trees.Count == 0) { injected = false; return; } // not loaded yet: try again next call

            ProfessionSkillTree? tree = trees.Values.FirstOrDefault(t => t.Slots.Any(s => s.Skill is BowQuickdraw));
            if (tree == null) { MelonLogger.Warning("[Archery] Tree: no Quickdraw orb found in any skill tree; archery changes are off."); return; }

            ProgressionSlot? twoSlot = null, threeSlot = null, holySlot = null;
            var multi = new List<ProgressionSlot>();
            chargeSlots.Clear();
            // The first pass, before any re-point: the one place Slowdraw is found by type. The Volley copy is a
            // BowSlowdraw too, so it is excluded by reference, and every later lookup compares by reference or slot id.
            foreach (ProgressionSlot s in tree.Slots) {
                switch (s.Skill) {
                    case BowQuickdraw q: Quickdraw = q; break;
                    case BowSlowdraw sd when !ReferenceEquals(sd, VolleySkill.Skill):
                        if (Slowdraw == null) Slowdraw = sd;
                        if (ReferenceEquals(sd, Slowdraw)) chargeSlots.Add(s);
                        break;
                    case LightBeamSkill h: Holy = h; holySlot = s; break;
                    case BowMultiArrowShot _: multi.Add(s); break;
                }
            }
            List<ProgressionSlot> byDepth = chargeSlots.OrderBy(s => s.GetIndentLevel(tree)).ToList(); // stable, as before
            chargeSlots.Clear();
            chargeSlots.AddRange(byDepth);
            // Two-Shot and Three-Shot are two separate skills that each add ONE arrow in the plain game, so they
            // cannot be told apart by how many arrows they add. Three-Shot is the one that requires the other.
            foreach (ProgressionSlot s in multi) {
                bool three = (s.Name ?? "").IndexOf("Three", StringComparison.OrdinalIgnoreCase) >= 0
                             || multi.Any(o => o != s && s.Dependencies != null && s.Dependencies.Contains(o.Hash));
                if (three) { ThreeShot = (BowMultiArrowShot)s.Skill; threeSlot = s; }
                else { TwoShot = (BowMultiArrowShot)s.Skill; twoSlot = s; }
            }
            MelonLogger.Msg($"[Archery] Tree: Two-Shot = '{twoSlot?.Name ?? "NOT FOUND"}', Three-Shot = '{threeSlot?.Name ?? "NOT FOUND"}'");
            TwoShotSlot = twoSlot;
            ThreeShotSlot = threeSlot;
            PickupBasedSkill? modified = TwoShot != null ? TwoShot.ModifiedSkill : null;
            MelonLogger.Msg($"[Archery] Tree: Two-Shot ({(twoSlot != null ? twoSlot.Hash.ToString() : "NOT FOUND")}) modified '{(modified != null ? modified.name : "-")}'; " +
                            $"Three-Shot ({(threeSlot != null ? threeSlot.Hash.ToString() : "NOT FOUND")})");

            // ---- Two-Shot and Three-Shot now run on the Volley skill; Two-Shot is a root, Three-Shot still needs it
            bool volleyIn = false;
            if (!VolleySkill.Registered) VolleySkill.LogCouldNotBeMade();
            else if (twoSlot == null || threeSlot == null || TwoShot == null)
                MelonLogger.Error("[Archery] Tree: Two-Shot or Three-Shot not found; the Volley skill is not in the tree.");
            else { RepointVolley(tree, twoSlot, threeSlot, TwoShot); volleyIn = true; }

            // ---- Burst: the Volley skill's third orb, after Three-Shot (only with the Volley skill in the tree)
            autoPosition.Clear();
            ProgressionSlot? burstSlot = null;
            if (volleyIn && SlotIdFree(tree, SlotIds.Burst, Orb.Burst)) {
                Vector3 auto = PickPosition(tree, threeSlot!.Position, threeSlot.Position - twoSlot!.Position);
                burstSlot = AddSlot(tree, "Burst", VolleySkill.Skill!, threeSlot, SlotIds.Burst, auto);
                autoPosition[burstSlot] = auto;
                iconSlots["Burst"] = burstSlot;
            }
            BurstSlot = burstSlot;
            ApplyCosts();

            // ---- Quickdraw 4
            List<ProgressionSlot> quick = tree.Slots.Where(s => s.Skill is BowQuickdraw).OrderBy(s => s.GetIndentLevel(tree)).ToList();
            if (!tree.GetSlot(Quickdraw4SlotHash, out ProgressionSlot q4)) {
                ProgressionSlot last = quick[quick.Count - 1];
                ProgressionSlot prev = quick.Count > 1 ? quick[quick.Count - 2] : last;
                q4 = AddSlot(tree, "Quickdraw 4", Quickdraw!, last, Quickdraw4SlotHash, PickPosition(tree, last.Position, last.Position - prev.Position));
                quick.Add(q4);
            }
            Quickdraw4Level = quick.Count;
            iconSlots["Quickdraw4"] = q4;
            MelonLogger.Msg($"[Archery] Tree: Quickdraw 4 at {q4.Position} (level {Quickdraw4Level})");

            // ---- The one gatherer orb (H3 section 6, the owner: "Combine gather and quiver plus the quiver gather into one skill"):
            // the game's Arrow Gatherer slot (its hash and skill stay, so saves are safe with the mod removed) also gives what Quiver
            // Gather did (Build.From: the Gatherer sets the QuiverGather flag). The mod's Quiver Gather slot is gone: its slot and
            // skill ids are reserved, and a save that holds the slot gets 1 point back at load (SlotIds.DeletedSlots, OffOrbRefund).
            // It shows the Quiver Gather icon (drawn from the game's own pouch glyph, with the quiver).
            ProgressionSlot? gatherer = tree.Slots.FirstOrDefault(s => s.Name == "Arrow Gatherer");
            ProgressionSlot? feather = tree.Slots.FirstOrDefault(s => s.Name == "Feather Gatherer");
            if (gatherer == null) MelonLogger.Error("[Archery] Tree: no 'Arrow Gatherer' orb found; nothing gives Quiver Gather's return to the quiver.");
            else iconSlots["QuiverGather"] = gatherer;

            // ---- Which slot is which archery orb (the build bits on bow status messages are made from this)
            orbBySlot.Clear();
            slotByOrb.Clear();
            void Map(ProgressionSlot? s, Orb orb) { if (s != null) { orbBySlot[s.Hash] = orb; slotByOrb[orb] = s; } }
            List<ProgressionSlot> plainQuick = quick.Where(s => s.Hash != Quickdraw4SlotHash).ToList();
            for (int i = 0; i < plainQuick.Count && i < 3; i++) Map(plainQuick[i], Orb.Quick1 + i);
            Map(q4, Orb.Quickdraw4);
            for (int i = 0; i < chargeSlots.Count && i < 3; i++) Map(chargeSlots[i], Orb.Charge1 + i);
            Map(twoSlot, Orb.Volley);
            Map(threeSlot, Orb.FullVolley);
            Map(burstSlot, Orb.Burst);
            Map(holySlot, Orb.HolyArrow);
            Map(gatherer, Orb.Gatherer);
            // H3 section 5: the spot the game (or the mod's old pick, for Quickdraw 4) gave each of these, for "auto", before anything moves
            foreach (Orb o in GameSlotOrbs) { ProgressionSlot? s = SlotOf(o); if (s != null && !autoPosition.ContainsKey(s)) autoPosition[s] = s.Position; }
            AddNewOrbs(tree, volleyIn);
            ApplyCosts(); // again: the later orbs' [Tree] <Orb>Cost (review I-1; the call above ran before they existed)
            RangedPath = tree.Path;
            MelonLogger.Msg($"[Archery] Build: orb map {string.Join(" ", orbBySlot.Select(p => $"{p.Value}={p.Key}").ToArray())} ({orbBySlot.Count} archery orbs mapped)");

            // ---- Holy Arrow comes after Charge 3: Slowdraw's own slots, never Two-Shot's or Three-Shot's (also
            // BowSlowdraw now). "auto" continues the Slowdraw column above Charge 3. Without the Volley skill it keeps
            // its 0.7.0 link after Three-Shot, and "auto" continues the Three-Shot column instead.
            if (holySlot == null) MelonLogger.Error("[Archery] Tree: Lightbeam orb not found; Holy Arrow is not in the tree.");
            else if (!volleyIn) {
                if (threeSlot != null) {
                    Relink(tree, holySlot, threeSlot);
                    if (twoSlot != null) autoPosition[holySlot] = PickPosition(tree, threeSlot.Position, threeSlot.Position - twoSlot.Position, holySlot);
                    MelonLogger.Error($"[Archery] Tree: the Volley skill is not in the tree; Burst is off and Lightbeam keeps its old link after '{threeSlot.Name}'");
                }
                else MelonLogger.Error("[Archery] Tree: the Volley skill is not in the tree and Three-Shot was not found; Burst is off and Lightbeam keeps the game's link");
            }
            else if (chargeSlots.Count == 0) MelonLogger.Error("[Archery] Tree: no Slowdraw (Charge) orbs found; Lightbeam keeps the game's link.");
            else {
                ProgressionSlot charge3 = chargeSlots[Math.Min(2, chargeSlots.Count - 1)];
                ProgressionSlot last = chargeSlots[chargeSlots.Count - 1];
                ProgressionSlot before = chargeSlots.Count > 1 ? chargeSlots[chargeSlots.Count - 2] : last;
                autoPosition[holySlot] = PickPosition(tree, last.Position, last.Position - before.Position, holySlot);
                Relink(tree, holySlot, charge3);
                MelonLogger.Msg($"[Archery] Tree: Lightbeam now requires '{charge3.Name}'");
            }

            // ---- The gatherers get a side path of their own, linked to nothing in the bow tree: one pouch-gatherer
            // orb (feathers and arrows together; since H3 also Quiver Gather's return to the quiver).
            if (gatherer != null && feather != null) MergeGatherers(tree, gatherer, feather);
            placed.Clear();
            if (holySlot != null) placed.Add(new KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>>(holySlot, posHoly));
            if (gatherer != null) placed.Add(new KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>>(gatherer, posArrow));
            if (burstSlot != null) placed.Add(new KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>>(burstSlot, posBurst));
            foreach (KeyValuePair<Orb, ProgressionSlot> p in newSlots) placed.Add(new KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>>(p.Value, orbPlace[p.Key]));
            foreach (Orb o in GameSlotOrbs) { ProgressionSlot? s = SlotOf(o); if (s != null) placed.Add(new KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>>(s, gamePlace[o])); }
            rangedTree = tree;
            ApplyLayout();
            if (burstSlot != null)
                MelonLogger.Msg($"[Archery] Tree: Burst at {burstSlot.Position} (slot {burstSlot.Hash}, needs '{ThreeShotSlot?.Name}', cost {burstSlot.Cost})");
            // Depth in the tree is worked out from the links and cached; the links just changed.
            foreach (ProgressionSlot s in tree.Slots) Traverse.Create(s).Field("indentLevel").SetValue(-1);
            if (twoSlot != null) iconSlots["TwoShot"] = twoSlot;
            if (threeSlot != null) iconSlots["ThreeShot"] = threeSlot;
            if (holySlot != null) iconSlots["HolyArrow"] = holySlot;

            Ready = true;
            ApplyTimers();
            ApplyIcons();
            LogProgression();
            Shrine.LogTree(tree);
            firstPrintDone = true; // later layout, slot and timer lines: Debug only (G2)
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Tree: ready on the {(NetworkSceneManager.IsServer ? "server" : "client")} (asked for by {trigger ?? "the game (a save, a shrine or experience)"})");
        }

        // The game's progression caps, once at the tree load (play-test 8's diagnosis read the banked points cap from it).
        static void LogProgression() {
            try {
                ProgressionSettings s = GlobalSettings<ProgressionSettings>.Instance;
                string sell;
                try { sell = Alta.Utilities.SettingsManager.GameConfig.Settings.CanSellSkillsWithChildren.ToString(); }
                catch (Exception e) { sell = $"unknown ({e.GetType().Name})"; }
                MelonLogger.Msg($"[Archery] Progression: slot cap {s.MaxBoughtSkills}, banked points cap {s.MaxFreePoints} (0 = no cap), sell refund {s.SellingSkillPointsRefundPercentage:0.###} | RevampedSkills {(BluesArcheryOverhaul.QuiverGather.RevampedSkillsLoaded ? "loaded" : "not loaded")}, can sell with children: {sell}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Progression probe failed: {e}"); }
        }

        // (Tree.QuiverPractice, which marked the Quiver Gather orb practised when it was tried, went with that orb in H3 section 6: the
        // one gatherer orb is the game's own, practised the game's way.)

        // ---------------------------------------------------------------- the later orbs (spec 2026-11-16)

        // Each orb with [Tree] <Orb>Orb on goes in after its parents (the game's dependencies are "any of"), on a passive skill
        // of its own (Seeking Volley: the Volley skill's fourth level), with its parent's try-out.
        // Anything wrong (setting off, a parent missing, an id taken or reserved, the shrine's link cap) leaves that orb out.
        static void AddNewOrbs(ProfessionSkillTree tree, bool volleyIn) {
            newSlots.Clear();
            foreach (Orb orb in NewOrbs.All) {
                try { AddNewOrb(tree, orb, volleyIn); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Tree: adding {orb} failed, it is off: {e}"); }
            }
            Lines.SeekingVolleyBuilt = newSlots.ContainsKey(Orb.SeekingVolley);
            MelonLogger.Msg($"[Archery] Tree: later orbs in the shrine: {(newSlots.Count > 0 ? string.Join(", ", newSlots.Keys.Select(o => o.ToString()).ToArray()) : "none")}; multi-arrow line has {Lines.Size(MainPath.Multi)} orbs");
        }

        static void AddNewOrb(ProfessionSkillTree tree, Orb orb, bool volleyIn) {
            string name = Lines.Name(orb);
            if (!orbOn[orb].Value) { MelonLogger.Msg($"[Archery] Tree: {name} is off ([Tree] {orb}Orb = false); it is not in the shrine"); return; }
            var parents = new List<ProgressionSlot>();
            foreach (Orb p in NewOrbs.Parents(orb)) {
                ProgressionSlot? s = SlotOf(p);
                if (s == null) { MelonLogger.Error($"[Archery] Tree: {name} needs {Lines.Name(p)}, which is not in the tree; {name} is off"); return; }
                parents.Add(s);
            }
            if (!SlotIds.New.TryGetValue(orb, out uint slotId) || !SlotIdFree(tree, slotId, orb)) return;
            int links = ShrineLinks.Entries(tree.Slots.Select(s => s.Dependencies != null ? s.Dependencies.Count : 0));
            int more = ShrineLinks.Entries(new[] { parents.Count });
            if (links + more > ShrineLinks.Cap) {
                MelonLogger.Error($"[Archery] Tree: {name} would take the shrine's connection list to {links + more} entries (the game's cap is {ShrineLinks.Cap}); {name} is off");
                return;
            }
            if (!OrbPlaces.TryParse(orbPlace[orb].Value, out float d, out float sw, out float h)) {
                OrbPlaces.TryParse(NewOrbs.DefaultPlace(orb), out d, out sw, out h);   // ApplyLayout warns about the bad setting
            }
            ProfessionSkill? skill;
            if (orb == Orb.SeekingVolley) {
                if (!volleyIn || VolleySkill.Skill == null) { MelonLogger.Error("[Archery] Tree: the Volley skill is not in the tree; Seeking Volley is off"); return; }
                skill = VolleySkill.Skill;   // the Volley skill's fourth level; its try-out is already Two-Shot's (RepointVolley), as Burst's
            }
            else {
                if (!SlotIds.Skills.TryGetValue(orb, out int skillId)) { MelonLogger.Error($"[Archery] Tree: {name} has no skill id; it is off"); return; }
                skill = MakePassiveSkill(ScriptableObject.CreateInstance<PassiveOrbSkill>(), name, skillId, orb.ToString());
                if (skill == null) return;
                CopyTryOut(parents[0].Skill, skill);   // the first parent's bow, arrows, target and glyphs
            }
            ProgressionSlot slot = AddSlot(tree, name, skill, parents, slotId, new Vector3(d, sw, h));
            newSlots[orb] = slot;
            orbBySlot[slot.Hash] = orb;
            slotByOrb[orb] = slot;
            iconSlots[orb.ToString()] = slot;
            iconParent[orb.ToString()] = parents[0];
            MelonLogger.Msg($"[Archery] Tree: {name} added (slot {slotId}, skill {(skill != null ? skill.Hash.ToString() : "?")}, needs {string.Join(" or ", parents.Select(p => "'" + p.Name + "'").ToArray())})");
        }

        // Feather Gatherer only collects feathers and Arrow Gatherer only arrows, so they are one orb: the Arrow
        // Gatherer orb, which now collects both. The Feather orb is taken out of the shrine. Nobody loses what
        // they paid for: the old Feather orb's id now leads to the combined orb, and someone who had bought both
        // gets the second point back the next time their skills load (GathererRefund).
        static void MergeGatherers(ProfessionSkillTree tree, ProgressionSlot keep, ProgressionSlot drop) {
            if (keep.Skill is Gatherer into && drop.Skill is Gatherer from) {
                Item[] mine = Traverse.Create(into).Field("validItems").GetValue<Item[]>() ?? new Item[0];
                Item[] theirs = Traverse.Create(from).Field("validItems").GetValue<Item[]>() ?? new Item[0];
                Item[] both = mine.Union(theirs).Where(i => i != null).ToArray();
                Traverse.Create(into).Field("validItems").SetValue(both);
                MelonLogger.Msg($"[Archery] Tree: '{keep.Name}' now gathers {both.Length} kind(s) of item: {string.Join(", ", both.Select(i => i.name).ToArray())}");
            }
            else MelonLogger.Warning("[Archery] Tree: the gatherer orbs are not the expected kind of skill; the combined orb only does what Arrow Gatherer did.");
            tree.Slots.Remove(drop);
            foreach (ProgressionSlot s in tree.Slots) InheritList(s)?.Remove(keep); // it used to hang off Quickdraw Plus too
            Traverse.Create(keep).Field("dependencies").SetValue(new List<uint>());
            Traverse.Create(tree).Field("slotMap").GetValue<Dictionary<uint, ProgressionSlot>>()[drop.Hash] = keep;
            mergedGatherer = keep;
            MelonLogger.Msg($"[Archery] Tree: '{drop.Name}' orb removed; '{keep.Name}' is the one gatherer orb and needs nothing first");
        }

        // Skills are loaded one saved orb at a time. If the combined gatherer orb comes up twice, the player had
        // bought both old orbs: count it once and hand the other point back.
        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.UnlockSlot))]
        static class GathererRefund {
            static readonly int T = Timing.Section("patch PlayerProgressionManager.UnlockSlot / Tree.GathererRefund");
            static bool Prefix(PlayerProgressionManager __instance, ProgressionSlot slot, bool isFromLoad) {
                using var _t = Timing.Begin(T);
                try {
                    if (!isFromLoad || mergedGatherer == null || slot != mergedGatherer) return true;
                    var paths = Traverse.Create(__instance).Property("PathExperiences").GetValue<Dictionary<ProgressionPath, PathExperience>>()
                                ?? Traverse.Create(__instance).Field("PathExperiences").GetValue<Dictionary<ProgressionPath, PathExperience>>();
                    if (paths == null || !paths.TryGetValue(slot.Path, out PathExperience pe) || !pe.UnlockedSlots.Contains(slot.Hash)) return true;
                    Traverse points = Traverse.Create(pe).Field("freePoints");
                    points.SetValue(points.GetValue<uint>() + slot.Cost);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Tree: a player owned both old gatherer orbs; {slot.Cost} skill point handed back.");
                    return false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Tree: gatherer refund check failed: {e.Message}"); return true; }
            }
        }

        // Review I-2 (batch 1): a later orb switched off ([Tree] <Orb>Orb = false) is not in the tree, so a save that holds it
        // has the game drop the slot with a warning in its own log only (PathExperience.Serialize: GetSlot fails), and the
        // point would be lost for good. While a save's slots are read, a failed lookup of one of the later orbs' slot ids hands
        // its cost back to that path's free points (server only, the way GathererRefund hands back a point) and says so.
        // Seeking Volley rides the Volley skill but has its own slot like the others, so it is refunded the same way.
        // The six parked orbs deleted in 0.16.0 (SlotIds.DeletedSlots) are never in the tree: 1 point each (their cost was 1).
        static PathExperience? reading;
        static readonly HashSet<string> refundLogged = new HashSet<string>();

        [HarmonyPatch(typeof(PathExperience), nameof(PathExperience.Serialize))]
        static class OffOrbRead {
            static readonly int TPrefix = Timing.Section("patch PathExperience.Serialize / Tree.OffOrbRead prefix");
            static void Prefix(PathExperience __instance, Alta.Serialization.Stream stream) {
                using var _t = Timing.Begin(TPrefix);
                reading = stream != null && stream.IsReading && NetworkSceneManager.IsServer ? __instance : null;
            }
            static void Finalizer() => reading = null;
        }

        [HarmonyPatch(typeof(ProfessionSkillTree), nameof(ProfessionSkillTree.GetSlot))]
        static class OffOrbRefund {
            static readonly int T = Timing.Section("patch ProfessionSkillTree.GetSlot / Tree.OffOrbRefund");
            static void Postfix(uint hash, bool __result) {
                using var _t = Timing.Begin(T);
                if (__result || reading == null) return;
                try {
                    Orb orb = Orb.None;
                    foreach (KeyValuePair<Orb, uint> p in SlotIds.New) if (p.Value == hash) { orb = p.Key; break; }
                    bool deleted = Array.IndexOf(SlotIds.DeletedSlots, hash) >= 0;
                    uint back;
                    // H4 (Opus minor 6): the Quiver Gather slot gives back what it cost (the Arrow Gatherer's cost), the others 1
                    if (deleted) back = SlotIds.DeletedRefund(hash, GathererCost());
                    // b2-review M-6: the condition is "not in the tree" (switched off, or refused at injection: a parent missing, an id clash, the link cap)
                    else if (orb == Orb.None || !orbCost.TryGetValue(orb, out MelonPreferences_Entry<int> cost) || (Ready ? newSlots.ContainsKey(orb) : OrbOn(orb))) return;
                    else back = (uint)Math.Max(0, cost.Value);
                    PathExperience pe = reading;
                    Traverse points = Traverse.Create(pe).Field("freePoints");
                    uint after = points.GetValue<uint>() + back;
                    points.SetValue(after); // the player's point, paid even at or above the banked cap (said below)
                    PlayerProgressionManager pm = Traverse.Create(pe).Field("targetPlayer").GetValue<PlayerProgressionManager>();
                    string who = Builds.NameOf(pm != null ? pm.Player : null);
                    if (refundLogged.Count > 512) refundLogged.Clear();
                    if (refundLogged.Add(who + "|" + (deleted ? hash.ToString(CultureInfo.InvariantCulture) : orb.ToString())))
                        MelonLogger.Warning((hash == SlotIds.QuiverGather
                            ? $"[Archery] Tree: '{who}' owned the Quiver Gather orb, merged into the Arrow Gatherer in 0.17.2: the orb is dropped and {back} skill point(s) handed back"
                            : deleted
                            ? $"[Archery] Tree: '{who}' owned slot {hash}, an orb deleted in 0.16.0: the orb is dropped and {back} skill point handed back"
                            : $"[Archery] Tree: '{who}' owned {Lines.Name(orb)}, which is not in the shrine ({(OrbOn(orb) ? "it could not be added; see the tree log" : $"[Tree] {orb}Orb = false")}): the orb is dropped and {back} skill point(s) handed back")
                            + (SlotIds.AtBankedCap(after, BankedCap()) ? $"; they now hold {after} unspent point(s), at or above the game's banked cap of {BankedCap()}: no Ranged experience until they spend some" : ""));
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Tree: refunding a switched-off orb failed; its point is lost: {e.Message}"); }
            }
        }

        // Two-Shot and Three-Shot point at the Volley skill, so their owners' levels count toward it when a save loads
        // (the tree is injected inside GetProfessionTree, before PathExperience.Serialize reads any slot). Trying a
        // Volley orb shows Two-Shot's try-out props and glyphs, not Slowdraw's. Two-Shot needs nothing first.
        /// <summary>What the shrine sets out and shows when an orb of this skill is tried: props, items and action glyphs.
        /// (Restored from before the Brace/Overcharge removal.)</summary>
        static void CopyTryOut(ProfessionSkill? source, ProfessionSkill target) {
            if (source == null) return;
            Traverse from = Traverse.Create(source), to = Traverse.Create(target);
            List<PositionedStaticPrefab>? props = from.Field("staticContentPrefabs").GetValue<List<PositionedStaticPrefab>>();
            List<PositionedNetworkPrefab>? items = from.Field("dynamicContentPrefabs").GetValue<List<PositionedNetworkPrefab>>();
            Sprite[]? glyphs = from.Field("practiceGlyphSprites").GetValue<Sprite[]>();
            if (props != null) to.Field("staticContentPrefabs").SetValue(new List<PositionedStaticPrefab>(props));
            if (items != null) to.Field("dynamicContentPrefabs").SetValue(new List<PositionedNetworkPrefab>(items));
            if (glyphs != null) to.Field("practiceGlyphSprites").SetValue((Sprite[])glyphs.Clone());
            MelonLogger.Msg($"[Archery] Tree: '{target.name}' try-out copied from '{source.name}': {props?.Count ?? 0} prop(s), {items?.Count ?? 0} item(s), {glyphs?.Length ?? 0} glyph(s)");
        }

        static void RepointVolley(ProfessionSkillTree tree, ProgressionSlot twoSlot, ProgressionSlot threeSlot, BowMultiArrowShot twoShot) {
            BowSlowdraw volley = VolleySkill.Skill!;
            Traverse from = Traverse.Create(twoShot), to = Traverse.Create(volley);
            List<PositionedStaticPrefab>? props = from.Field("staticContentPrefabs").GetValue<List<PositionedStaticPrefab>>();
            List<PositionedNetworkPrefab>? items = from.Field("dynamicContentPrefabs").GetValue<List<PositionedNetworkPrefab>>();
            Sprite[]? glyphs = from.Field("practiceGlyphSprites").GetValue<Sprite[]>();
            // The shrine reads these when an orb is tried out; they must not be null.
            to.Field("staticContentPrefabs").SetValue(props != null ? new List<PositionedStaticPrefab>(props) : new List<PositionedStaticPrefab>());
            to.Field("dynamicContentPrefabs").SetValue(items != null ? new List<PositionedNetworkPrefab>(items) : new List<PositionedNetworkPrefab>());
            to.Field("practiceGlyphSprites").SetValue(glyphs != null ? (Sprite[])glyphs.Clone() : new Sprite[0]);
            Traverse.Create(twoSlot).Field("skill").SetValue(volley);
            Traverse.Create(threeSlot).Field("skill").SetValue(volley);
            foreach (ProgressionSlot s in tree.Slots) InheritList(s)?.Remove(twoSlot);
            Traverse.Create(twoSlot).Field("dependencies").SetValue(new List<uint>());
            MelonLogger.Msg("[Archery] Tree: Two-Shot and Three-Shot now use the Volley skill; Two-Shot needs nothing first");
        }

        // Make `slot` depend on `parent` alone, and keep the game's reverse links in step.
        static void Relink(ProfessionSkillTree tree, ProgressionSlot slot, ProgressionSlot parent) {
            foreach (ProgressionSlot s in tree.Slots) InheritList(s)?.Remove(slot);
            Traverse.Create(slot).Field("dependencies").SetValue(new List<uint> { parent.Hash });
            slot.AddInherit(tree);
        }

        // A new slot id must not already be in the tree (every game id read so far is below 65536, ours above).
        static bool SlotIdFree(ProfessionSkillTree tree, uint id, Orb orb) {
            if (Array.IndexOf(SlotIds.ReservedSlots, id) >= 0) {
                MelonLogger.Error($"[Archery] Tree: slot id {id} for {orb} is reserved (a removed orb's; saves may hold it); {orb} is off");
                return false;
            }
            ProgressionSlot? used = tree.Slots.FirstOrDefault(s => s.Hash == id);
            if (used == null && tree.GetSlot(id, out ProgressionSlot mapped)) used = mapped;
            if (used == null) return true;
            MelonLogger.Error($"[Archery] Tree: slot id {id} for {orb} is already used by '{used.Name}'; {orb} is off");
            return false;
        }

        static readonly int T_ApplyCosts = Timing.Section("Tree.ApplyCosts");
        static void ApplyCosts() {
            using var _t = Timing.Begin(T_ApplyCosts);
            ApplyCost(BurstSlot, burstCost, "Burst");
            foreach (KeyValuePair<Orb, ProgressionSlot> p in newSlots) ApplyCost(p.Value, orbCost[p.Key], Lines.Name(p.Key));
        }

        static void ApplyCost(ProgressionSlot? slot, MelonPreferences_Entry<int> entry, string orb) {
            if (slot == null) return;
            uint cost = (uint)Math.Max(0, entry.Value);
            if (slot.Cost == cost) return;
            Traverse.Create(slot).Field("cost").SetValue(cost);
            if (Ready && Logs.Summary) MelonLogger.Msg($"[Archery] Tree: {orb} now costs {cost}");
        }

        static readonly int T_ApplyLayout = Timing.Section("Tree.ApplyLayout");
        static void ApplyLayout() {
            using var _t = Timing.Begin(T_ApplyLayout);
            foreach (KeyValuePair<ProgressionSlot, MelonPreferences_Entry<string>> p in placed) {
                if (string.Equals((p.Value.Value ?? "").Trim(), "auto", StringComparison.OrdinalIgnoreCase)) {
                    if (autoPosition.TryGetValue(p.Key, out Vector3 auto)) {
                        Traverse.Create(p.Key).Field("position").SetValue(auto);
                        if (Detail) MelonLogger.Msg($"[Archery] Tree: '{p.Key.Name}' placed at ({auto.x}, {auto.y}, {auto.z}) (auto)");
                    }
                    else MelonLogger.Warning($"[Archery] Tree: [TreeLayout] {p.Value.Identifier} = 'auto' but '{p.Key.Name}' has no automatic position; it stays where it was.");
                    continue;
                }
                string[] parts = (p.Value.Value ?? "").Split(',');
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (parts.Length == 3 && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, inv, out float x)
                    && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, inv, out float y)
                    && float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, inv, out float z)) {
                    Traverse.Create(p.Key).Field("position").SetValue(new Vector3(x, y, z));
                    if (Detail) MelonLogger.Msg($"[Archery] Tree: '{p.Key.Name}' placed at ({x}, {y}, {z})");
                }
                else MelonLogger.Warning($"[Archery] Tree: [TreeLayout] {p.Value.Identifier} = '{p.Value.Value}' is not 'depth, sideways, height' or 'auto'; '{p.Key.Name}' stays where it was.");
            }
            LogSlots();
        }

        // Each archery slot's shrine position and what it needs, after every layout change.
        // G2: the layout, slot and timer lines are printed in full once, when the tree is first set up; after that (every settings
        // reload) only at [Log] Level Debug. Warnings and errors from the same code always print.
        static bool firstPrintDone;
        static bool Detail => !firstPrintDone || Logs.Debug;

        static readonly int T_LogSlots = Timing.Section("Tree.LogSlots");
        static void LogSlots() {
            using var _t = Timing.Begin(T_LogSlots);
            if (rangedTree == null) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (Detail) foreach (KeyValuePair<uint, Orb> p in orbBySlot.OrderBy(p => p.Value)) {
                if (!slotByOrb.TryGetValue(p.Value, out ProgressionSlot s) || s == null) continue;
                var needs = new List<string>();
                foreach (uint dep in s.Dependencies ?? new List<uint>()) {
                    if (orbBySlot.TryGetValue(dep, out Orb o)) needs.Add(o.ToString());
                    else needs.Add(rangedTree.GetSlot(dep, out ProgressionSlot other) && other != null ? $"'{other.Name}'" : dep.ToString(inv));
                }
                Vector3 at = s.Position;
                MelonLogger.Msg(string.Format(inv, "[Archery] Tree: slot {0} '{1}' at ({2:0.00}, {3:0.00}, {4:0.00}) needs {5}",
                                              p.Value, s.Name, at.x, at.y, at.z, needs.Count > 0 ? string.Join("+", needs.ToArray()) : "nothing"));
            }
            // Orbs that sit too close (a [TreeLayout] setting), and the shrine's connection list against the game's cap.
            List<ProgressionSlot> all = rangedTree.Slots.ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++) {
                    float gap = (all[i].Position - all[j].Position).magnitude;
                    if (gap < OrbPlaces.MinGap)
                        MelonLogger.Warning(string.Format(inv, "[Archery] Tree: '{0}' and '{1}' are {2:0.000} m apart (less than {3}); check [TreeLayout]",
                                                          all[i].Name, all[j].Name, gap, OrbPlaces.MinGap));
                }
            int links = ShrineLinks.Entries(all.Select(s => s.Dependencies != null ? s.Dependencies.Count : 0));
            if (links > ShrineLinks.Cap) MelonLogger.Error($"[Archery] Tree: the shrine's connection list has {links} entries, over the game's cap of {ShrineLinks.Cap}");
            else if (Detail) MelonLogger.Msg($"[Archery] Tree: {all.Count} orbs, connection list {links} of {ShrineLinks.Cap} entries");
        }

        static List<ProgressionSlot>? InheritList(ProgressionSlot s) => Traverse.Create(s).Field("inheritSlots").GetValue<List<ProgressionSlot>>();

        static ProgressionSlot AddSlot(ProfessionSkillTree tree, string name, ProfessionSkill skill, ProgressionSlot after, uint hash, Vector3 position) =>
            AddSlot(tree, name, skill, new List<ProgressionSlot> { after }, hash, position);

        /// <summary>A new slot needing any one of <paramref name="after"/> (the game's dependencies are "any of"); cost and
        /// icon from the first.</summary>
        static ProgressionSlot AddSlot(ProfessionSkillTree tree, string name, ProfessionSkill skill, List<ProgressionSlot> after, uint hash, Vector3 position) {
            var slot = new ProgressionSlot();
            Traverse t = Traverse.Create(slot);
            t.Field("name").SetValue(name);
            t.Field("skill").SetValue(skill);
            t.Field("cost").SetValue(after[0].Cost);
            t.Field("dependencies").SetValue(after.Select(a => a.Hash).ToList());
            t.Field("hash").SetValue(hash);
            t.Field("position").SetValue(position);
            t.Field("slotIcon").SetValue(after[0].SlotIcon); // replaced by ApplyIcons when the mod's icon is available
            slot.Path = tree.Path;
            tree.Slots.Add(slot);
            Traverse.Create(tree).Field("slotMap").GetValue<Dictionary<uint, ProgressionSlot>>()[hash] = slot;
            slot.AddInherit(tree);
            return slot;
        }

        // A skill object with no behaviour of its own behind a mod orb (the side orbs). Null (the orb is
        // off) when the game's skill table already holds the id.
        static ProfessionSkill? MakePassiveSkill(ProfessionSkill skill, string name, int id, string orb) {
            if (Array.IndexOf(SlotIds.ReservedSkills, id) >= 0) {
                MelonLogger.Error($"[Archery] Tree: skill id {id} for {orb} is reserved (a removed orb's); {orb} is off");
                UnityEngine.Object.Destroy(skill);
                return null;
            }
            Alta.HashedGeneralValue<ProfessionSkill>.CheckItems(); // loads the game's own skills first; ours is only ever added to a full table
            object? items = Traverse.Create(typeof(Alta.HashedGeneralValue<ProfessionSkill>)).Field("items").GetValue();
            ProfessionSkill? used = null;
            if (items is Dictionary<uint, ProfessionSkill> byUintUsed) byUintUsed.TryGetValue((uint)id, out used);
            else if (items is Dictionary<int, ProfessionSkill> byIntUsed) byIntUsed.TryGetValue(id, out used);
            if (used != null) {
                MelonLogger.Error($"[Archery] Tree: skill id {id} for {orb} is already used by '{used.name}'; {orb} is off");
                UnityEngine.Object.Destroy(skill);
                return null;
            }
            skill.name = name;
            // The id must be the same on server and client, and the game must be able to look the skill up by it.
            FieldInfo? hash = AccessTools.Field(typeof(ProfessionSkill), "hash");
            if (hash != null) hash.SetValue(skill, Convert.ChangeType(id, hash.FieldType));
            else MelonLogger.Error($"[Archery] Tree: could not set the {name} skill id.");
            if (items is Dictionary<uint, ProfessionSkill> byUint) byUint[(uint)id] = skill;
            else if (items is Dictionary<int, ProfessionSkill> byInt) byInt[id] = skill;
            else MelonLogger.Warning($"[Archery] Tree: could not register the {name} skill for lookup by id.");
            // The shrine reads these when an orb is tried out; they must not be null.
            Traverse s = Traverse.Create(skill);
            s.Field("staticContentPrefabs").SetValue(new List<PositionedStaticPrefab>());
            s.Field("dynamicContentPrefabs").SetValue(new List<PositionedNetworkPrefab>());
            s.Field("practiceGlyphSprites").SetValue(new Sprite[0]);
            return skill;
        }

        // Continue in the given direction; if another orb already sits there, step sideways. `self` (a slot being
        // moved) does not count as being in the way.
        static Vector3 PickPosition(ProfessionSkillTree tree, Vector3 from, Vector3 step, ProgressionSlot? self = null) {
            if (step.sqrMagnitude < 1e-6f) step = Vector3.forward * 0.24f;
            Vector3 side = Vector3.Cross(step, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(step, Vector3.right);
            side = side.normalized * step.magnitude;
            Vector3 best = from + step; float bestGap = -1f;
            foreach (Vector3 c in new[] { from + step, from + step * 0.6f + side * 0.8f, from + step * 0.6f - side * 0.8f, from + side, from - side }) {
                float gap = tree.Slots.Where(s => s != self).Select(s => (s.Position - c).magnitude).DefaultIfEmpty(float.MaxValue).Min();
                if (gap > 0.6f * step.magnitude) return c;
                if (gap > bestGap) { bestGap = gap; best = c; }
            }
            return best;
        }

        // ---------------------------------------------------------------- draw timers

        // The Volley skill's own pull settings carry the ghosts' timer; the Full Volley ratchet, the ripple and the rack
        // are read from the timeline (Volley.Times), not from any game timer.
        // The game has one timer per skill, shared by every player, so each is set to the fastest any build can have (three
        // off-path Quick orbs) and every player is held to their own time on the mod's draw clock: Draws (the release and the
        // deferred Charged statuses), Volley's ghosts and HolyArrow's arming.
        static readonly int T_ApplyTimers = Timing.Section("Tree.ApplyTimers");
        static void ApplyTimers() {
            using var _t = Timing.Begin(T_ApplyTimers);
            float charge = Paths.SharedStageFactor, volley = Paths.SharedVolleyFactor;
            if (VolleySkill.Registered) SetTimer(VolleySkill.Pull, SharedVolleySeconds);
            SetTimer(Holy != null ? Holy.InputStructure : null, holySeconds.Value * charge);
            if (Slowdraw != null) SetTimer(Slowdraw.InputStructure, Stages.S1 * charge); // stage 1; stages 2 and 3 are the mod's own (ChargeStages)
            if (Detail) MelonLogger.Msg($"[Archery] Tree: timers volley={twoShotSeconds.Value}{(VolleySkill.Registered ? "" : " (no Volley skill)")} full volley={threeShotSeconds.Value} " +
                            $"ripple={rippleSeconds.Value} rack={rackSeconds.Value} charge stages={Stages.S1}/{Stages.S2}/{Stages.S3} holy={holySeconds.Value} " +
                            $"(shared timers at the fastest build: charge x{charge.ToString("0.##", CultureInfo.InvariantCulture)}, volley x{volley.ToString("0.##", CultureInfo.InvariantCulture)})");
        }

        /// <summary>The Volley skill's own (shared) timer: Two-Shot's time at the fastest build's factor.</summary>
        internal static float SharedVolleySeconds => TwoShotSeconds * Paths.SharedVolleyFactor;

        static void SetTimer(SkillInputStructure? structure, float seconds) {
            if (structure is BowStringPullbackStructure pull) Traverse.Create(pull).Field("minTimeToPullback").SetValue(Mathf.Max(0f, seconds));
            else MelonLogger.Warning("[Archery] Tree: a bow skill has no draw timer to change.");
        }

        // ---------------------------------------------------------------- icons

        // Each orb's icon is UserData\BluesArcheryOverhaul\Icons\<Name>.png if that file exists, otherwise the copy
        // built into the mod. Edit or replace a PNG while the game runs and it is picked up within two seconds
        // (reopen the shrine to see it).
        static string IconFolder => Path.Combine(Cfg.Folder, "Icons");

        internal static void PollIcons() {
            if (!Ready || ApplicationManager.IsHeadless) return;
            try {
                if (!Directory.Exists(IconFolder)) return;
                DateTime newest = Directory.GetFiles(IconFolder, "*.png").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
                if (newest == iconStamp) return;
                iconStamp = newest;
                ApplyIcons();
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Icon check failed: {e.Message}"); }
        }

        static bool mipLogged;
        static readonly int T_ApplyIcons = Timing.Section("asset Tree.ApplyIcons (PNG decode)");
        static void ApplyIcons() {
            using var _t = Timing.Begin(T_ApplyIcons);
            if (ApplicationManager.IsHeadless) return;
            try { Directory.CreateDirectory(IconFolder); } catch { }
            foreach (KeyValuePair<string, ProgressionSlot> pair in iconSlots) {
                try {
                    byte[]? png = null; string source = "built-in";
                    string file = Path.Combine(IconFolder, pair.Key + ".png");
                    if (File.Exists(file)) { png = File.ReadAllBytes(file); source = file; }
                    else {
                        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"BluesArcheryOverhaul.Resources.{pair.Key}.png");
                        if (s != null) { png = new byte[s.Length]; s.Read(png, 0, png.Length); }
                    }
                    if (png == null) {
                        if (iconParent.ContainsKey(pair.Key)) continue;   // a later orb: its parent's icon, after this loop
                        MelonLogger.Warning($"[Archery] Icon '{pair.Key}': none found, the orb keeps the game's icon."); continue;
                    }
                    Texture2D? old = pair.Value.SlotIcon;
                    // H3 section 4: no mip maps, as the game's own glyph textures (with them the lines looked fuller at every distance)
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(tex, png)) { MelonLogger.Warning($"[Archery] Icon '{pair.Key}': {source} is not a readable PNG."); continue; }
                    tex.name = "Archery_" + pair.Key;
                    if (old != null) { tex.filterMode = old.filterMode; tex.wrapMode = old.wrapMode; tex.anisoLevel = old.anisoLevel; }
                    if (!mipLogged) { mipLogged = true; if (Logs.Debug) MelonLogger.Msg($"[Archery] Icon '{pair.Key}': {tex.width}x{tex.height}, mipmapCount {tex.mipmapCount} (1 = none, as the game's glyphs; said once a session)"); }
                    Traverse.Create(pair.Value).Field("slotIcon").SetValue(tex);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Icon '{pair.Key}' loaded from {source}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Icon '{pair.Key}' failed: {e.Message}"); }
            }
            // A later orb with no PNG (the art is still being drafted) shows its first parent's icon, as loaded above.
            foreach (KeyValuePair<string, ProgressionSlot> pair in iconParent) {
                try {
                    if (!iconSlots.TryGetValue(pair.Key, out ProgressionSlot slot) || slot == null) continue;
                    Texture2D? mine = slot.SlotIcon;
                    if (mine != null && mine.name == "Archery_" + pair.Key) continue;   // its own PNG loaded
                    Traverse.Create(slot).Field("slotIcon").SetValue(pair.Value.SlotIcon);
                    if (iconFallbackLogged.Add(pair.Key))
                        { if (Logs.Summary) MelonLogger.Msg($"[Archery] Icon '{pair.Key}': no {pair.Key}.png built in or in {IconFolder}; showing the icon of '{pair.Value.Name}'"); }
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Icon '{pair.Key}' fallback failed: {e.Message}"); }
            }
        }
    }
}
