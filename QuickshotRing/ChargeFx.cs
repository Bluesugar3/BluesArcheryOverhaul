using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Global;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;

namespace QuickshotRing {
    // The Charge line's own feedback on the held arrow (clients only; nothing here runs on a headless server):
    //   Fill   per bow string, from Slowdraw's ChargingStarted: an orange-to-gold line growing along the front of the shaft,
    //          reaching the tip at Slowdraw's charge time, offset off the shaft toward the camera so the opaque shaft does not
    //          hide it; a halo on a white copy of 'Orb Connection Line'; a flickering ember at its front; Charge 3's shell.
    //   Strands  one per owned Charge orb wound around the shaft (DrawStrands): tapered, closing onto the head, a faint halo
    //          line under each, a pulse along a strand at its stage's thump, brighter at every stage; StrandKnots leaves one
    //          knot at the head per completed stage and makes the head glow at stage 3.
    //   Thump  on Slowdraw's Charged status (the server's own charge): flare, the game's dissipate flash on the arrow, a light,
    //          the bow-hand haptic, and at Charge 2+ a sound that replaces Slowdraw's own.
    //   Hit    (every client, a charged release that is not a beam) the first hit's looks: the hit effects, Charge 2's dust
    //          ring, the ground puff; and the EmberTrail in flight (Charge Shot 3's pierce streak went with its pierce, batch 4b).
    //          Fed by Weight's hit hook, or its embed fallback when no hit came.
    // The game's whole-arrow glow on Slowdraw is hidden only where FillShownFor says the Fill is drawn (VolleySkill.NoGlow).
    internal static class ChargeFx {
        static MelonPreferences_Entry<bool> enabled = null!, holySqueeze = null!;
        static MelonPreferences_Entry<float> glowScale = null!;
        static MelonPreferences_Entry<float> strandGap = null!, strandWidth = null!, strandTurns = null!, strandSpin = null!,
            strandTaper = null!, strandConverge = null!, strandHaloWidth = null!, strandHaloAlpha = null!, strandFlicker = null!,
            strandPulseSeconds = null!, strandPulseWidth = null!, strandPulseBoost = null!, strandStageBrightness = null!, strandStageWidth = null!;
        static MelonPreferences_Entry<float> fraction = null!, headGap = null!, coreWidth = null!, haloWidth = null!, haloAlpha = null!,
            emberSize = null!, charge2EmberSize = null!, flareBoost = null!, flareSeconds = null!, thumpHaptic = null!, charge2ThumpHaptic = null!,
            shellWidth = null!, shellAlpha = null!, trailSeconds = null!, trailWidth = null!, dustRingFrom = null!, dustRingTo = null!, dustRingSeconds = null!,
            holyCoreFraction = null!, holySqueezeBoost = null!, holyPulseHz = null!;
        static MelonPreferences_Entry<string> backColour = null!, frontColour = null!, haloMaterial = null!, charge2ThumpSound = null!,
            hitEffect = null!, charge2HitEffect = null!, groundEffect = null!;
        static readonly Color DefaultBack = new Color(1f, 0.416f, 0f), DefaultFront = new Color(1f, 0.78f, 0.2f);

        internal const float ShaftRadiusFallback = 0.006f, CameraClearance = 0.002f, ThumpHapticSeconds = 0.08f;
        const float StrandMinSpan = 0.02f;                             // metres of strand always left behind the head gap, however hard the squeeze
        const float EmberWidthFactor = 1.5f, FlashRange = 1f;          // Ruling 9: internal, not settings
        const int Cap = 64;

        internal static bool Enabled => enabled != null && enabled.Value;
        internal static Color BackColour => Cfg.Colour(backColour, DefaultBack);
        internal static Color FrontColour => Cfg.Colour(frontColour, DefaultFront);
        internal static string HitEffect => hitEffect.Value;
        internal static string Charge2HitEffect => charge2HitEffect.Value;
        internal static string GroundEffect => groundEffect.Value;
        internal static float DustRingFrom => dustRingFrom.Value;
        internal static float DustRingTo => dustRingTo.Value;
        internal static float DustRingSeconds => dustRingSeconds.Value;
        // The main arrow's strand, for Volley.HeavyLook's one-turn wrap round each held heavy arrow.
        internal static float StrandGap => strandGap != null ? Mathf.Max(0f, strandGap.Value) : 0.004f;
        internal static float StrandWidth => strandWidth != null ? Mathf.Max(0f, strandWidth.Value) : 0.007f;
        internal static float StrandSpin => strandSpin != null ? strandSpin.Value : 0f;
        internal static float StrandConverge => strandConverge != null ? strandConverge.Value : 0f;
        /// <summary>[Fill] GlowScale: one factor on the brightness of the strands, the halos, the knots and the stage-3 head
        /// light (and the heavy arrows' strand). The game's own glow on a charged arrow is the game's and is not touched.</summary>
        internal static float GlowScale => glowScale != null ? StrandShape.Glow(glowScale.Value) : 1f;
        /// <summary>Strand 1's colour (stage 1's): DrawStrands' blend for k = 1, the back colour.</summary>
        internal static Color StrandColour1 => BackColour;
        /// <summary>The strands' width curve (StrandShape.Taper), shared; a new instance only when StrandTaper changes.</summary>
        internal static AnimationCurve StrandTaperCurve => TaperCurve(strandTaper != null ? strandTaper.Value : 0f);

        /// <summary>The Charge Shot's own stage thump, for a multi-arrow main's hold stages (GoldHold, G4): its sound
        /// ([Fill] Charge2ThumpSound) and its strength in the bow hand at a stage (ThumpHaptic, Charge2ThumpHaptic, full at 3).</summary>
        internal static string ThumpSound => charge2ThumpSound != null ? charge2ThumpSound.Value : "";
        internal static float ThumpStrength(int stage) =>
            thumpHaptic == null ? 0f : stage >= 3 ? 1f : stage == 2 ? charge2ThumpHaptic.Value : thumpHaptic.Value;

        /// <summary>How bright the held arrow's charge is on this bow right now: the strands' stage step (1 at stage 1) times
        /// the thump's flare; 1 with no Fill or before stage 1. Volley.HeavyLook brightens with it.</summary>
        internal static float StageGlow(BowString bow) {
            if (bow == null || !fills.TryGetValue(bow, out Fill f) || f.stage <= 0) return 1f;
            return Mathf.Max(0f, StrandShape.StageBrightness(f.stage, strandStageBrightness.Value)) * Flare(f);
        }

        internal static void Init() {
            var cat = Cfg.Category("Fill");
            enabled = cat.CreateEntry("Enabled", true, description: "Off = no Fill, and the game's glow comes back.");
            fraction = cat.CreateEntry("Fraction", 0.4f, description: "Share of the shaft, from the tip back, that the Fill lights.");
            headGap = cat.CreateEntry("HeadGap", 0.03f, description: "Metres left clear behind the arrowhead.");
            backColour = cat.CreateEntry("BackColour", "#FF6A00", description: "Colour of the Fill's back end.");
            frontColour = cat.CreateEntry("FrontColour", "#FFC733", description: "Colour of the Fill's front end (also the thump's light and Charge 3's shell).");
            coreWidth = cat.CreateEntry("CoreWidth", 0.006f);
            haloWidth = cat.CreateEntry("HaloWidth", 0.02f);
            haloAlpha = cat.CreateEntry("HaloAlpha", 0.35f);
            haloMaterial = cat.CreateEntry("HaloMaterial", "orb-line", description: "'orb-line' = a white copy of the game's 'Orb Connection Line' material; 'flat' = the plain line material.");
            emberSize = cat.CreateEntry("EmberSize", 0.012f, description: "Length in metres of the bright flickering ember at the Fill's front.");
            charge2EmberSize = cat.CreateEntry("Charge2EmberSize", 0.02f, description: "The ember's length at Charge 2 and 3.");
            glowScale = cat.CreateEntry("GlowScale", 0.7f, description: "One knob for how bright the charge looks: multiplies the brightness of the charge strands, the halos (the Fill's and each strand's), the knots at the head and the stage-3 head light. 1 = as bright as before 0.13.0. The game's own glow on a charged arrow is not changed by this.");
            flareBoost = cat.CreateEntry("FlareBoost", 1.8f, description: "At the thump the Fill flares to this many times its width (and the halo's brightness), fading back over FlareSeconds.");
            flareSeconds = cat.CreateEntry("FlareSeconds", 0.15f);
            strandGap = cat.CreateEntry("StrandGap", 0.004f, description: "Metres the charge strands sit outside the shaft's surface.");
            strandWidth = cat.CreateEntry("StrandWidth", 0.007f, description: "Width in metres of each charge strand.");
            strandTurns = cat.CreateEntry("StrandTurns", 5f, description: "How many times a charge strand winds around the shaft between the hand and the head.");
            strandSpin = cat.CreateEntry("StrandSpin", 0.6f, description: "Turns per second the strands rotate around the shaft.");
            strandTaper = cat.CreateEntry("StrandTaper", 0.12f, description: "Share of a charge strand's length (0 to 0.5) over which it thins to nothing at each end.");
            strandConverge = cat.CreateEntry("StrandConverge", 0.15f, description: "Share of a charge strand's length, at the head end, over which it closes in onto the arrowhead. 0 = it keeps its distance to the end.");
            strandHaloWidth = cat.CreateEntry("StrandHaloWidth", 0.018f, description: "Width in metres of the faint wider line under each charge strand.");
            strandHaloAlpha = cat.CreateEntry("StrandHaloAlpha", 0.25f, description: "Opacity of the faint wider line under each charge strand.");
            strandFlicker = cat.CreateEntry("StrandFlicker", 0.15f, description: "How much (0 to 1) each charge strand's width flickers, frame to frame.");
            strandPulseSeconds = cat.CreateEntry("StrandPulseSeconds", 0.25f, description: "Seconds a stage's pulse of light takes to run along its strand from the hand to the head. 0 = no pulse.");
            strandPulseWidth = cat.CreateEntry("StrandPulseWidth", 0.12f, description: "Half-length of the pulse, as a share of the strand.");
            strandPulseBoost = cat.CreateEntry("StrandPulseBoost", 1.5f, description: "Extra brightness at the pulse's peak (0 = no pulse).");
            strandStageBrightness = cat.CreateEntry("StrandStageBrightness", 0.15f, description: "Every charge strand gets this much brighter at each stage after the first (stage 3 = 1 + 2 x this).");
            strandStageWidth = cat.CreateEntry("StrandStageWidth", 0.15f, description: "Every charge strand (and its halo and the knots) gets this much wider at each stage after the first (stage 3 = 1 + 2 x this).");
            charge2ThumpSound = cat.CreateEntry("Charge2ThumpSound", "Power Strike Start Sounds", description: "Charge 2 and 3: this replaces Slowdraw's own charge sound. Empty = keep the game's.");
            thumpHaptic = cat.CreateEntry("ThumpHaptic", 0.7f, description: "Strength (0 to 1) of the thump in your bow hand.");
            charge2ThumpHaptic = cat.CreateEntry("Charge2ThumpHaptic", 0.9f, description: "The thump's strength at Charge 2 and 3.");
            shellWidth = cat.CreateEntry("ShellWidth", 0.035f, description: "Charge 3: width of the shell over the lit stretch, from the thump on.");
            shellAlpha = cat.CreateEntry("ShellAlpha", 0.2f);
            trailSeconds = cat.CreateEntry("TrailSeconds", 0.25f, description: "Ember trail behind a charged arrow in flight.");
            trailWidth = cat.CreateEntry("TrailWidth", 0.01f);
            hitEffect = cat.CreateEntry("HitEffect", "Charge Hit Use", description: "Effect where a charged arrow hits a creature.");
            charge2HitEffect = cat.CreateEntry("Charge2HitEffect", "Power Strike Use", description: "Added at Charge 2 and 3.");
            groundEffect = cat.CreateEntry("GroundEffect", "GroundHitPuff", description: "Effect where a charged arrow hits the ground.");
            dustRingFrom = cat.CreateEntry("DustRingFrom", 0.3f, description: "Charge 2: the dust ring's starting radius in metres.");
            dustRingTo = cat.CreateEntry("DustRingTo", 1.2f);
            dustRingSeconds = cat.CreateEntry("DustRingSeconds", 0.3f);
            StrandKnots.Init(cat);
            holySqueeze = cat.CreateEntry("HolySqueeze", true, description: "Holy Arrow: over the last [Volley] GatherSeconds before it is ready, the lit stretch squeezes forward toward the tip and brightens; once ready, the short core left pulses until release. Off = the Fill keeps its shape.");
            holyCoreFraction = cat.CreateEntry("HolyCoreFraction", 0.08f, description: "Holy Arrow: share of the shaft, from the tip back, still lit when the squeeze is complete.");
            holySqueezeBoost = cat.CreateEntry("HolySqueezeBoost", 1.8f, description: "Holy Arrow: the core and halo are this many times as bright when the squeeze is complete (1 = no change).");
            holyPulseHz = cat.CreateEntry("HolyPulseHz", 2f, description: "Holy Arrow: pulses per second of the armed core, from Holy Arrow ready until release. 0 = steady.");
            HolyWindUp.Init();
            Cfg.Reloaded += () => { haloResolved = false; HolyWindUp.Reloaded(); };
        }

        // ---------------------------------------------------------------- the Fill

        internal sealed class Fill {
            public BowString bow = null!;
            public uint arrow;
            public string owner = "";
            public bool local;
            public float startedAt;
            public float thumpAt = -1f;     // Time.time of the thump; -1 = not yet
            public bool notDrawnLogged;
            public NetworkEntity? glowHidden; // VolleySkill.NoGlow skipped Slowdraw's glow request for this arrow (until Cancelled/Consumed)
            public float radius = ShaftRadiusFallback;
            public GameObject root = null!;
            public LineRenderer? core, halo, ember, shell;
            public int stage;               // Charge stages completed on this draw (0 before the first thump)
            public bool gameCharged;        // Slowdraw's Charged status arrived (the server sends it at this archer's own stage 1, OwnTime)
            public readonly LineRenderer?[] strands = new LineRenderer?[3]; // one per Charge orb, wound around the outside of the shaft
            public readonly LineRenderer?[] strandHalos = new LineRenderer?[3]; // the faint wider line under each strand
            public readonly float[] strandThumpAt = { -1f, -1f, -1f };      // Time.time of stage k's thump (its strand's pulse); -1 = not yet
            public float strandTaperShare = float.NaN;                       // the taper the strands' width curve was built for
            public StrandKnots? knots;                                       // the knots at the head, one per completed stage; the stage-3 head glow
            public Vector3 back, lit;
            public HolyWindUp? windUp;      // Holy Arrow's wind-up (a Charge main with Holy Arrow), from stage 3
            public bool windUpDone;         // the wind-up ran and stopped (or failed to start): not again on this draw
            public Helix helix;             // this frame's strand geometry (DrawStrands), which the wind-up's motes ride on

            internal Build Build => local ? Builds.LocalBuild() : Builds.ClientBuild(owner);
            internal int Tier => Build.Charge;
        }

        static readonly Dictionary<BowString, Fill> fills = new Dictionary<BowString, Fill>();
        static readonly List<BowString> drop = new List<BowString>();
        static readonly Dictionary<BowString, string> dropWhy = new Dictionary<BowString, string>();
        static readonly Dictionary<string, float> radii = new Dictionary<string, float>();
        static string lastFrameError = "";

        internal static Fill? FillOf(BowString? bow) => bow != null && fills.TryGetValue(bow, out Fill f) ? f : null;

        /// <summary>Clients: an archer's main path, the local player's own or the one the server sent.</summary>
        internal static MainPath MainOf(string owner, bool local) => (local ? Builds.LocalBuild() : Builds.ClientBuild(owner)).Main;

        // Clients: who is drawing each bow, from the skill statuses the server sends (Slowdraw's, Holy Arrow's). The game plays
        // a status's glow and sound before the mod's status handling runs, so they read the archer noted by an earlier status
        // of the same draw (ChargingStarted comes at the crossing, Charged later).
        static readonly Dictionary<BowString, KeyValuePair<string, bool>> archers = new Dictionary<BowString, KeyValuePair<string, bool>>();

        internal static void NoteArcher(BowString bow, string owner, bool local) {
            if (bow == null) return;
            if (archers.Count >= Cap && !archers.ContainsKey(bow)) archers.Clear();
            archers[bow] = new KeyValuePair<string, bool>(owner, local);
        }

        /// <summary>Clients: true only when the archer drawing this arrow is known and their main path is neither
        /// <paramref name="line"/> nor None; anything unknown keeps the game's behaviour.</summary>
        internal static bool OffPath(NetworkEntity? entity, MainPath line, out MainPath main, out string archer) {
            main = MainPath.None; archer = "?";
            BowString? bow = entity != null ? Volley.BowOf(entity) : null;
            if (bow == null || !archers.TryGetValue(bow, out KeyValuePair<string, bool> a)) return false;
            archer = a.Key;
            main = MainOf(a.Key, a.Value);
            return !Resolver.OnPath(main, line);
        }

        static uint ArrowOn(BowString bow) {
            PickupDock dock = bow.StringDock;
            if (dock == null || !dock.HasDockedPickup) return 0u;
            Pickup p = dock.DockedPickup;
            return p != null && p.Entity != null ? p.Entity.Identifier : 0u;
        }

        static Arrow? ArrowObj(BowString bow) {
            PickupDock dock = bow.StringDock;
            if (dock == null || !dock.HasDockedPickup || dock.DockedPickup == null) return null;
            Arrow a = dock.DockedPickup.GetComponent<Arrow>();
            return a != null ? a : null;
        }

        /// <summary>Clients: every Slowdraw status for the arrow on this bow (VolleySkill.ClientStatus, after the game's own handling).</summary>
        static readonly int T_Status = Timing.Section("ChargeFx.Status");
        internal static void Status(BowString bow, ToolBasedSkillStatus status, string owner, bool local) {
            using var _t = Timing.Begin(T_Status, owner);
            if (bow == null || ApplicationManager.IsHeadless) return;
            switch (status) {
                case ToolBasedSkillStatus.ChargingStarted:
                    Remove(bow);
                    if (!Enabled) return;
                    // Only a Charge main (or no main path) has the charge: anyone else gets no Fill (and no glow, VolleySkill.NoGlow).
                    MainPath main = MainOf(owner, local);
                    if (!Resolver.OnPath(main, MainPath.Charge)) {
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: none for '{owner}' on '{bow.name}' (main path {main})");
                        return;
                    }
                    Create(bow, owner, local);
                    break;
                case ToolBasedSkillStatus.Charged:
                    // Stage 1's thump is on the same clock as stages 2 and 3 (Draw): once this status has arrived and the
                    // archer's own stage 1 has passed, whichever is later.
                    if (fills.TryGetValue(bow, out Fill f)) {
                        f.gameCharged = true;
                        Arrow? on = ArrowObj(bow);
                        if (on != null && ArrowOn(bow) == f.arrow) {
                            try { Draw(f, on); } catch (Exception e) { FrameError(e); }
                        }
                    }
                    break;
                case ToolBasedSkillStatus.Cancelled:
                case ToolBasedSkillStatus.Consumed:
                    // The game has released Slowdraw's glow (a no-op when it was never requested): nothing to give back.
                    if (fills.TryGetValue(bow, out Fill c)) c.glowHidden = null;
                    Remove(bow, status == ToolBasedSkillStatus.Cancelled ? "the charge was cancelled" : "the charge was used");
                    break;
            }
        }

        /// <summary>Every machine, from Volley.Fire (after Draws.Release armed the arrow): the arrow left this bow. Its Fill
        /// goes; a charged arrow gets its ember trail and its hit looks are remembered (Hit).</summary>
        internal static void Released(BowString bow, NetworkEntity? arrowEntity, Resolution r) {
            if (bow == null || ApplicationManager.IsHeadless) return;
            uint arrowId = arrowEntity != null ? arrowEntity.Identifier : 0u;
            // A spectator's relayed resolution cannot tell a beam from a charged shot, so the armed flag decides too.
            bool beam = r.Ultimate == UltimateUse.Beam || HolyArrow.ArmedFlagFor(bow, arrowId);
            try { Flown(bow, arrowEntity, r, beam); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: charged flight looks failed: {e.Message}"); }
            holies.Remove(bow);
            // The game keeps Slowdraw's glow on the flying arrow until Consumed; a hidden one comes back for that stretch.
            Remove(bow, "the arrow was released");
        }

        static readonly int T_Create = Timing.Section("look ChargeFx.Create (Fill)");
        static void Create(BowString bow, string owner, bool local) {
            using var _t = Timing.Begin(T_Create, owner);
            uint id = ArrowOn(bow);
            Arrow? arrow = ArrowObj(bow);
            if (id == 0u || arrow == null) return;
            if (fills.Count >= Cap) { foreach (Fill old in fills.Values) Drop(old, "too many Fills at once"); fills.Clear(); }
            var f = new Fill { bow = bow, arrow = id, owner = owner, local = local, startedAt = Time.time, radius = ShaftRadius(arrow) };
            f.root = new GameObject("ArcheryFill");
            f.core = NewLine(f.root, "Core", Fx.LineMaterial(), coreWidth.Value);
            f.halo = NewLine(f.root, "Halo", HaloMaterial(), haloWidth.Value);
            f.ember = NewLine(f.root, "Ember", Fx.LineMaterial(), coreWidth.Value * EmberWidthFactor);
            fills[bow] = f;
            float gap = Mathf.Max(0f, strandGap.Value);
            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Fill: strands on '{0}' radius {1:0.0000} m (shaft {2:0.0000} m + gap {3:0.0000}), {4} owned stage(s)",
                bow.name, f.radius + gap, f.radius, gap, Mathf.Clamp(f.Tier, 1, 3)));
            try { Draw(f, arrow); }
            catch (Exception e) {
                FrameError(e);
                if (f.core != null) f.core.enabled = false; // never laid out: FillShownFor says no, the game's glow stays
            }
        }

        static LineRenderer NewLine(GameObject root, string name, Material? material, float width) {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            LineRenderer lr = Fx.NewLine(go, 2, width, false);
            lr.material = material;
            return lr;
        }

        static void Remove(BowString bow, string why = "a new draw") {
            if (fills.TryGetValue(bow, out Fill f)) { Drop(f, why); fills.Remove(bow); }
        }

        /// <summary>The Fill goes. If the game's glow was hidden for its arrow and the game has not released Slowdraw's glow
        /// since (GlowReleasedByGame clears the record on any release of it), the glow is requested again, so the arrow is never left with neither.</summary>
        static void Drop(Fill f, string why) {
            NetworkEntity? entity = f.glowHidden;
            f.glowHidden = null;
            Destroy(f, why);
            if (entity == null || entity.Identifier != f.arrow) return;
            try {
                ProfessionSkill? slow = Tree.Slowdraw;
                if (slow == null) return;
                // as ProfessionSkill.RequestToolEffect does it; the mod's NoGlow prefix is on that method, not on RequestEffect
                ProfessionSkill.RequestEffect(entity, slow, out ToolBasedEffectBase effect);
                if (effect != null) effect.InitializeEffect(entity);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: gone from '{(f.bow != null ? f.bow.name : "?")}' ({why}) while the game's glow was hidden; the glow is requested again for arrow {f.arrow}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: could not give the game's glow back to arrow {f.arrow}: {e.Message}"); }
        }

        /// <summary>VolleySkill.NoGlow: Slowdraw's glow request for this arrow was skipped because its Fill is drawn.</summary>
        internal static void GlowHidden(NetworkEntity entity) {
            if (entity == null) return;
            Fill? f = FillOf(Volley.BowOf(entity));
            if (f != null && f.arrow == entity.Identifier) f.glowHidden = entity;
        }

        // The game's own release of Slowdraw's glow for an arrow, keyed by arrow id (not by bow: once the arrow is off the
        // string Volley.BowOf finds nothing and Status never runs). Every release path ends here: ReleaseToolEffect and the
        // NetworkEntity overload (ProfessionSkill.cs:186-205), BowDrawSkill.OnArrowShot (:75), the input's OnCancelled
        // (BowStringPullbackInputMethod.cs:136) and the forced release when the arrow is destroyed (skill null, isForced).
        // After it, a Drop must not ask for the glow again: nothing would ever release it.
        [HarmonyPatch(typeof(ProfessionSkill), nameof(ProfessionSkill.ReleaseEffect), new[] { typeof(uint), typeof(ProfessionSkill), typeof(bool) })]
        static class GlowReleasedByGame {
            static readonly int T = Timing.Section("patch ProfessionSkill.ReleaseEffect / ChargeFx.GlowReleasedByGame");
            static bool live;

            static void Postfix(uint identifier, ProfessionSkill skill, bool isForced) {
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || fills.Count == 0) return;
                    if (!isForced && (skill == null || !ReferenceEquals(skill, Tree.Slowdraw))) return;
                    if (!live) { live = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Fill: patch on ProfessionSkill.ReleaseEffect is live"); }
                    foreach (Fill f in fills.Values) {
                        if (f.arrow != identifier || f.glowHidden == null) continue;
                        f.glowHidden = null;
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: the game released Slowdraw's glow on arrow {identifier}{(isForced ? " (forced)" : "")}; it will not be requested again");
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: glow release tracking failed: {e.Message}"); }
            }
        }

        static void Destroy(Fill f, string why) {
            StopWindUp(f, why); // the hum stops before its arrow goes; the motes and ring go with the root below anyway
            try { f.knots?.Stop(); } // a pooled knot effect goes back to the pool, never down with the arrow
            catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: releasing the knots failed: {e.Message}"); }
            f.knots = null;
            if (f.root != null) UnityEngine.Object.Destroy(f.root); // the strands, knot rings and head glow are children of the root
        }

        /// <summary>Every frame (Main.OnLateUpdate, after Volley.DrawHeld): each Fill follows its arrow.</summary>
        internal static void Frame() {
            if (fills.Count == 0 || ApplicationManager.IsHeadless) return;
            drop.Clear();
            bool on = Enabled;
            foreach (KeyValuePair<BowString, Fill> kv in fills) {
                BowString bow = kv.Key; Fill f = kv.Value;
                if (!on || bow == null || f.root == null) { drop.Add(bow!); dropWhy[bow!] = !on ? "the Fill was switched off" : "its bow or lines are gone"; continue; }
                Arrow? arrow = ArrowObj(bow);
                if (arrow == null || ArrowOn(bow) != f.arrow) { drop.Add(bow); dropWhy[bow] = "the arrow left the string"; continue; }
                try { Draw(f, arrow); }
                catch (Exception e) { FrameError(e); }
            }
            foreach (BowString b in drop) {
                if (fills.TryGetValue(b, out Fill f)) Drop(f, dropWhy.TryGetValue(b, out string why) ? why : "dropped");
                fills.Remove(b);
            }
            dropWhy.Clear();
        }

        static void FrameError(Exception e) {
            string line = $"[Archery] Fill: drawing failed: {e}";
            if (line == lastFrameError) return;
            lastFrameError = line;
            MelonLogger.Error(line);
        }

        static float Seconds(Fill f) {
            float since = Time.time - f.startedAt;
            return f.local ? (Draws.Elapsed(f.bow) ?? since) : since;
        }

        static float Flare(Fill f) {
            if (f.thumpAt < 0f) return 1f;
            float len = flareSeconds.Value, age = Time.time - f.thumpAt;
            if (len <= 0f || age >= len) return 1f;
            return 1f + (Mathf.Max(1f, flareBoost.Value) - 1f) * (1f - age / len);
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        static readonly Gradient coreGradient = new Gradient();

        /// <summary>The core's colours: the Fill's own back-to-lit blend at one alpha.</summary>
        static Gradient CoreGradient(Color back, Color lit, float alpha) {
            float a = Mathf.Clamp01(alpha);
            coreGradient.SetKeys(new[] { new GradientColorKey(back, 0f), new GradientColorKey(lit, 1f) },
                                 new[] { new GradientAlphaKey(a, 0f), new GradientAlphaKey(a, 1f) });
            return coreGradient; // LineRenderer.colorGradient copies it
        }

        static readonly int T_Draw = Timing.Section("look ChargeFx.Draw (per Fill per frame)");
        static void Draw(Fill f, Arrow arrow) {
            using var _t = Timing.Begin(T_Draw);
            Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
            Vector3 tip = tipT.position, fwd = arrow.transform.forward;
            float L = Mathf.Max(0.3f, (tip - f.bow.transform.position).magnitude); // the string's grab point, as Volley's HeldLayout
            float t = Seconds(f);
            // This archer's own stage times (a Charge main's Quick orbs make them sooner; the game's timer is the fastest build's).
            ChargeStages st = Paths.StagesFor(f.Build);
            float grown = FillTimeline.Grown(t, st.S1);
            // One thump per owned orb's stage, all on this clock: stage 1 (Thump) also needs the game's Charged status.
            int owned = Mathf.Clamp(f.Tier, 1, 3);
            while (f.gameCharged && f.stage < owned && t + 1e-4f >= st.At(f.stage + 1)) {
                f.stage++;
                f.strandThumpAt[f.stage - 1] = Time.time; // that strand's pulse
                if (f.stage == 1) Thump(f, arrow, t, st.S1);
                else StageThump(f, tip, f.stage, t);
            }
            // Holy Arrow (Lightbeam charging on this bow): the lit stretch squeezes toward the tip and brightens; armed, it pulses
            float share = Mathf.Clamp01(fraction.Value), bright = 1f, pulse = 1f;
            HolyGather? record = HolyRecord(f);
            HolyGather? holy = record != null && holySqueeze.Value ? record : null;
            if (holy != null) share = HolyShape(f, holy, L, share, out bright, out pulse);
            // the wind-up (a Charge main with Holy Arrow, from stage 3): it takes over the strands' squeeze and pulse
            HolyWindUp? wind = WindUpFor(f, record, t, st, tipT);
            Vector3 back = tip - fwd * (share * L);
            Vector3 front = tip - fwd * Mathf.Max(0f, headGap.Value);
            Vector3 lit = Vector3.Lerp(back, front, grown);

            // off the axis, toward the viewer: the flat material keeps the depth test, so a line inside the shaft would not show
            Vector3 offset = Vector3.zero;
            Camera cam = Camera.main;
            if (cam != null) {
                Vector3 toCam = cam.transform.position - Vector3.Lerp(back, lit, 0.5f);
                Vector3 side = toCam - fwd * Vector3.Dot(toCam, fwd);
                if (side.sqrMagnitude > 1e-8f) offset = side.normalized * (f.radius + CameraClearance);
            }
            f.back = back + offset; f.lit = lit + offset;
            float baseShare = Mathf.Clamp01(fraction.Value);
            // With the wind-up the strands' span shortens toward the head over the gather ([HolyWindUp] StrandSqueeze: the whole
            // spiral packs into the front of the arrow, in step with the core's squeeze), the braid tightens on top of that, and
            // the cord and head take the armed pulse; without it, the plain squeeze as before.
            float strandSqueeze = wind != null ? wind.Span : baseShare > 0.001f ? share / baseShare : 1f;
            float headPulse = wind != null ? wind.Pulse : 1f;
            try { DrawStrands(f, tip, fwd, L, strandSqueeze, st, owned, t, Flare(f) * (wind != null ? wind.Pulse : pulse), wind); }
            catch (Exception e) { FrameError(e); }
            if (f.stage > 0) {
                try {
                    if (f.knots == null) f.knots = new StrandKnots();
                    f.knots.Frame(f, arrow, tip, fwd, front, Flare(f) * headPulse, headPulse);
                }
                catch (Exception e) { FrameError(e); }
            }
            if (wind != null) {
                try { wind.Frame(f, tip, fwd); }
                catch (Exception e) { FrameError(e); StopWindUp(f, "drawing failed"); } // the strands go back to the squeeze
            }

            float b = f.local ? 1f : Volley.OtherArcherBrightness;
            float thump = Flare(f), flare = thump * pulse;
            Color back0 = BackColour, front0 = FrontColour, litColour = Color.Lerp(back0, front0, grown);
            // brighter = toward white (vertex colours clamp at 1) and more opaque; the halo's alpha takes the same factor
            Color coreBack = Brighter(back0, bright), coreLit = Brighter(litColour, bright);

            if (f.core != null) {
                f.core.SetPosition(0, f.back);
                f.core.SetPosition(1, f.lit);
                f.core.widthMultiplier = coreWidth.Value * flare;
                f.core.colorGradient = CoreGradient(coreBack, coreLit, b * bright);
            }
            if (f.halo != null) {
                f.halo.SetPosition(0, f.back);
                f.halo.SetPosition(1, f.lit);
                f.halo.widthMultiplier = haloWidth.Value * flare;
                float a = haloAlpha.Value * b * thump * bright * GlowScale; // bright already carries the armed pulse: once, not twice
                f.halo.startColor = WithAlpha(back0, a);
                f.halo.endColor = WithAlpha(litColour, a);
            }
            if (f.ember != null) {
                int tier = f.stage;
                float size = tier >= 2 ? charge2EmberSize.Value : emberSize.Value;
                float along = Mathf.Min(Mathf.Max(0f, size), (f.lit - f.back).magnitude);
                f.ember.SetPosition(0, f.lit - fwd * along);
                f.ember.SetPosition(1, f.lit);
                f.ember.widthMultiplier = coreWidth.Value * EmberWidthFactor * flare;
                Color hot = Color.Lerp(front0, Color.white, 0.5f);
                float flicker = UnityEngine.Random.Range(0.7f, 1f);
                f.ember.startColor = WithAlpha(front0, flicker * b);
                f.ember.endColor = WithAlpha(hot, flicker * b);
            }
            if (f.shell != null) {
                f.shell.SetPosition(0, f.back);
                f.shell.SetPosition(1, f.lit);
                f.shell.widthMultiplier = shellWidth.Value;
                Color s = WithAlpha(front0, shellAlpha.Value * b);
                f.shell.startColor = s;
                f.shell.endColor = s;
            }
        }

        const int StrandPoints = 28, StrandKeys = 7; // a Gradient takes at most 8 keys
        const float DoneWhiteness = 0.3f;            // a completed strand's lean toward white before the stage brightness

        // Reused every frame by every strand (the main thread only): no allocation in the draw loop. A LineRenderer copies
        // its positions, gradient and width curve when they are set.
        static readonly Vector3[] strandPoints = new Vector3[StrandPoints];
        static readonly Gradient strandGradient = new Gradient();
        static readonly GradientColorKey[] strandColourKeys = new GradientColorKey[StrandKeys];
        static readonly GradientAlphaKey[] strandAlphaKeys = new GradientAlphaKey[StrandKeys];
        static readonly float[] strandKeyAt = new float[StrandKeys];
        static AnimationCurve? taperCurve;
        static float taperCurveShare = float.NaN;

        /// <summary>The strands' width along the drawn line: StrandShape.Taper as a curve (flat tangents make each rise a
        /// smoothstep, as Taper is). Rebuilt only when StrandTaper changes.</summary>
        static AnimationCurve TaperCurve(float share) {
            share = Mathf.Clamp(share, 0f, 0.5f);
            if (taperCurve != null && share == taperCurveShare) return taperCurve;
            taperCurveShare = share;
            if (share <= 0f) taperCurve = AnimationCurve.Constant(0f, 1f, 1f);
            else if (share >= 0.5f) taperCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            else taperCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(share, 1f), new Keyframe(1f - share, 1f), new Keyframe(1f, 0f));
            return taperCurve;
        }

        /// <summary>One strand per owned Charge orb, wound around the outside of the shaft from the hand to the head. Strand k
        /// winds on over stage k's stretch of the timeline and brightens at its thump; Holy Arrow's gather squeezes them forward
        /// (<paramref name="squeeze"/>: the share of the span still covered; the turns stay, so the helix gets denser).
        /// Each strand tapers at both ends, closes in onto the head, flickers in width, has a faint halo line under it, carries
        /// a pulse from hand to head at its thump, and every strand steps up in brightness at each stage.</summary>
        static void DrawStrands(Fill f, Vector3 tip, Vector3 fwd, float L, float squeeze, ChargeStages st, int owned, float t, float flare,
                                HolyWindUp? wind) {
            // the back end never comes up to the head gap, so the helix always runs forward (no reversed or zero-length line)
            Vector3 hb = tip - fwd * StrandShape.Back(L, squeeze, headGap.Value, StrandMinSpan), hf = tip - fwd * Mathf.Max(0f, headGap.Value);
            Vector3 u = Vector3.Cross(fwd, Vector3.up);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(fwd, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(fwd, u);
            float gap = Mathf.Max(0f, strandGap.Value), turns = strandTurns.Value, spin = Time.time * strandSpin.Value;
            // Holy Arrow's wind-up: the colour shift toward the beam's, and the braid over the front of the strands
            float shift = wind != null ? wind.Shift : 0f, braid = wind != null ? wind.BraidNow : 0f;
            float cordShare = HolyWindUp.CordShare, cordBright = wind != null ? HolyWindUp.CordBrightness * braid : 0f;
            Color beam = HolyWindUp.BeamColour;
            f.helix = new Helix {
                back = hb, front = hf, u = u, v = v, shaft = f.radius, gap = gap, turns = turns, spin = spin, converge = strandConverge.Value,
                braid = braid, cordShare = cordShare, cordDepth = HolyWindUp.CordDepth, valid = true,
            };
            float glow = GlowScale; // the one knob: the strands' and their halos' alpha, and a done strand's lean toward white
            float alpha = (f.local ? 1f : Volley.OtherArcherBrightness) * glow;
            float stageBright = Mathf.Max(0f, StrandShape.StageBrightness(f.stage, strandStageBrightness.Value));
            float stageWide = Mathf.Max(0f, StrandShape.StageBrightness(f.stage, strandStageWidth.Value)); // the same step, on width
            float flick = Mathf.Clamp01(strandFlicker.Value);
            float pulseSeconds = strandPulseSeconds.Value, pulseWidth = strandPulseWidth.Value, pulseBoost = Mathf.Max(0f, strandPulseBoost.Value);
            float haloW = Mathf.Max(0f, strandHaloWidth.Value), haloA = Mathf.Clamp01(strandHaloAlpha.Value);
            float taper = strandTaper.Value;
            bool newCurve = !(f.strandTaperShare == Mathf.Clamp(taper, 0f, 0.5f));
            AnimationCurve curve = TaperCurve(taper);
            if (newCurve) f.strandTaperShare = taperCurveShare;
            Color back0 = BackColour, front0 = FrontColour;
            for (int k = 1; k <= 3; k++) {
                LineRenderer? lr = f.strands[k - 1], halo = f.strandHalos[k - 1];
                bool done = k <= f.stage;
                // the next strand winds on only while charging: strand 1 from the crossing, later ones once the charge is live
                float p = k > owned ? 0f : done ? 1f : k == f.stage + 1 && (k == 1 || f.thumpAt >= 0f) ? Mathf.Min(st.Progress(k, t), 0.97f) : 0f;
                if (p <= 0.001f) {
                    if (lr != null) lr.enabled = false;
                    if (halo != null) halo.enabled = false;
                    continue;
                }
                if (lr == null) {
                    f.strands[k - 1] = lr = NewLine(f.root, "Strand" + k, Fx.LineMaterial(), strandWidth.Value);
                    lr.positionCount = StrandPoints;
                    lr.widthCurve = curve;
                }
                if (halo == null) {
                    f.strandHalos[k - 1] = halo = NewLine(f.root, "StrandHalo" + k, HaloMaterial(), haloW);
                    halo.positionCount = StrandPoints;
                    halo.sortingOrder = -1; // under its strand
                    halo.widthCurve = curve;
                }
                if (newCurve) { lr.widthCurve = curve; halo.widthCurve = curve; }
                lr.enabled = true;
                halo.enabled = haloW > 0f && haloA > 0f;

                // the shape: s runs hand (0) to head (1) along the whole strand; the drawn part ends at p while it winds on.
                // The gap closes onto the shaft's surface at the head (and over the front with the wind-up's braid).
                for (int i = 0; i < StrandPoints; i++) strandPoints[i] = f.helix.Point(k, p * i / (StrandPoints - 1), 0f);
                lr.SetPositions(strandPoints);
                if (halo.enabled) halo.SetPositions(strandPoints);

                // width: the taper is the curve; the flare at a done strand, and one flicker factor per strand per frame
                float wobble = flick > 0f ? UnityEngine.Random.Range(1f - flick, 1f) : 1f;
                float widthK = (done ? flare : 0.7f) * wobble * stageWide;
                lr.widthMultiplier = strandWidth.Value * widthK;

                // colour: the strand's own blend, toward white when done; the stage brightness on whiteness and alpha; the pulse
                Color c = Color.Lerp(Color.Lerp(back0, front0, (k - 1) / 2f), beam, shift); // the wind-up turns it to the beam's colour
                float white = done ? Mathf.Clamp01(DoneWhiteness * stageBright * glow) : 0f;
                float since = f.strandThumpAt[k - 1] >= 0f ? Time.time - f.strandThumpAt[k - 1] : -1f;
                bool pulsing = done && pulseBoost > 0f && pulseSeconds > 0f && pulseWidth > 0f && since >= 0f && since / pulseSeconds <= 1f + pulseWidth;
                StrandKeyTimes(p, pulsing ? since / pulseSeconds : -1f, pulseWidth);
                for (int j = 0; j < StrandKeys; j++) {
                    float at = strandKeyAt[j];
                    float boost = pulsing ? StrandShape.Pulse(at * p, since, pulseSeconds, pulseWidth) * pulseBoost : 0f;
                    // the braided cord at the head is brighter (0 without the wind-up)
                    float cord = cordBright > 0f ? cordBright * (1f - WindUp.CordGap(at * p, 1f, cordShare, 1f)) : 0f;
                    float w = Mathf.Clamp01(white + (1f - 1f / (1f + boost)) + cord);
                    strandColourKeys[j] = new GradientColorKey(Color.Lerp(c, Color.white, w), at);
                    strandAlphaKeys[j] = new GradientAlphaKey(Mathf.Clamp01(alpha * Mathf.Lerp(0.5f, 1f, at) * stageBright * (1f + boost)), at);
                }
                strandGradient.SetKeys(strandColourKeys, strandAlphaKeys);
                lr.colorGradient = strandGradient;

                if (halo.enabled) {
                    halo.widthMultiplier = haloW * widthK;
                    float ha = alpha * haloA * stageBright;
                    halo.startColor = WithAlpha(c, ha * 0.5f);
                    halo.endColor = WithAlpha(c, ha);
                }
            }
        }

        /// <summary>The gradient's key times along the drawn line (0..1): spread evenly, or gathered around the pulse when one
        /// is on its way (centre at <paramref name="centre"/> on the whole strand; the drawn part is <paramref name="p"/> of it).</summary>
        static void StrandKeyTimes(float p, float centre, float width) {
            if (centre < 0f || p <= 0f) {
                for (int j = 0; j < StrandKeys; j++) strandKeyAt[j] = j / (float)(StrandKeys - 1);
                return;
            }
            strandKeyAt[0] = 0f;
            strandKeyAt[1] = centre - width;
            strandKeyAt[2] = centre - width * 0.5f;
            strandKeyAt[3] = centre;
            strandKeyAt[4] = centre + width * 0.5f;
            strandKeyAt[5] = centre + width;
            strandKeyAt[6] = p;
            for (int j = 0; j < StrandKeys; j++) strandKeyAt[j] = Mathf.Clamp01(strandKeyAt[j] / p); // still in order: clamping keeps it
        }

        /// <summary>Stages 2 and 3: the flare again, a heavier sound and thump in the bow hand; stage 3 adds the shell.</summary>
        static readonly int T_StageThump = Timing.Section("look start ChargeFx.StageThump");
        static void StageThump(Fill f, Vector3 tip, int stage, float t) {
            using var _t = Timing.Begin(T_StageThump);
            f.thumpAt = Time.time;
            Fx.Flash(tip, FrontColour, Mathf.Max(1f, flareBoost.Value), FlashRange, Mathf.Max(0.01f, flareSeconds.Value));
            VolleyFx.Play(charge2ThumpSound.Value, tip);
            if (f.local) {
                Controller? hand = Volley.BowHand(f.bow);
                float s = stage >= 3 ? 1f : charge2ThumpHaptic.Value;
                if (hand != null && s > 0f) hand.TriggerHaptic(Mathf.Clamp01(s), ThumpHapticSeconds);
            }
            if (stage >= 3 && f.shell == null) f.shell = NewLine(f.root, "Shell", Fx.LineMaterial(), shellWidth.Value);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: stage {stage} thump on '{f.bow.name}'{(f.local ? " (local)" : "")} at {Mathf.RoundToInt(t * 1000f).ToString(CultureInfo.InvariantCulture)} ms");
        }

        static Color Brighter(Color c, float k) => k <= 1f ? c : Color.Lerp(c, Color.white, 1f - 1f / k);

        // ---------------------------------------------------------------- Holy Arrow's gather on the Fill (Task 12)

        /// <summary>One bow's Lightbeam charge as the clients see it (HolyArrow.Status): kept apart from the Fill, because
        /// Lightbeam's and Slowdraw's ChargingStarted arrive in no fixed order and a new Fill must not lose it.</summary>
        internal sealed class HolyGather {
            public uint arrow;
            public float startedAt;
            public float armedAt = -1f;     // Time.time of Lightbeam's Charged; -1 = not yet
            public bool gatherLogged, armedLogged;
        }

        static readonly Dictionary<BowString, HolyGather> holies = new Dictionary<BowString, HolyGather>();
        const float PulseDepth = 0.35f; // the armed core's swing in width and brightness (internal, not a setting)

        /// <summary>Clients, from HolyArrow.Status: Lightbeam's ChargingStarted (the crossing), Charged (armed) and Cancelled on
        /// the arrow on this bow. The Fill reads it every frame (squeeze, brighten, pulse); Cancelled returns it to normal.</summary>
        static readonly int T_Holy = Timing.Section("ChargeFx.Holy (status)");
        internal static void Holy(BowString bow, ToolBasedSkillStatus status, uint arrowId) {
            using var _t = Timing.Begin(T_Holy, arrowId);
            if (bow == null || ApplicationManager.IsHeadless) return;
            uint id = arrowId; // the status's own arrow, not whatever is on the string now
            bool onString = id != 0u && ArrowOn(bow) == id;
            switch (status) {
                case ToolBasedSkillStatus.ChargingStarted:
                    if (!onString) return; // a late status for an arrow that has left the string
                    // a repeat while gathering keeps the start; after armed, a fresh ChargingStarted is a restart
                    if (holies.TryGetValue(bow, out HolyGather same) && same.arrow == id && same.armedAt < 0f) return;
                    if (holies.Count >= Cap) holies.Clear();
                    holies[bow] = new HolyGather { arrow = id, startedAt = Time.time };
                    break;
                case ToolBasedSkillStatus.Charged:
                    if (!onString) return;
                    if (!holies.TryGetValue(bow, out HolyGather h) || h.arrow != id) {
                        // ChargingStarted was missed: armed means the gather is complete
                        if (holies.Count >= Cap) holies.Clear();
                        Fill? known = FillOf(bow);
                        h = new HolyGather { arrow = id, startedAt = Time.time - (known != null ? Paths.HolySecondsFor(known.Build) : Tree.HolySeconds) };
                        holies[bow] = h;
                    }
                    if (h.armedAt < 0f) h.armedAt = Time.time;
                    break;
                case ToolBasedSkillStatus.Cancelled:
                    if (holies.TryGetValue(bow, out HolyGather gone) && gone.arrow == id) holies.Remove(bow);
                    break;
            }
        }

        /// <summary>This Fill's arrow's Lightbeam charge, whatever HolySqueeze says (the squeeze reads it only when on; the
        /// wind-up always). A record for another arrow is dropped.</summary>
        static HolyGather? HolyRecord(Fill f) {
            if (!holies.TryGetValue(f.bow, out HolyGather h)) return null;
            if (h.arrow != f.arrow) { holies.Remove(f.bow); return null; } // another arrow is on the string now
            return h;
        }

        /// <summary>How far Holy Arrow's gather has gone (0..1; 1 once armed), on the clock the core's squeeze has always used.</summary>
        static float GatherNow(Fill f, HolyGather h) =>
            h.armedAt >= 0f ? 1f : DrawTimeline.GatherProgress(Time.time - h.startedAt, Paths.HolySecondsFor(f.Build), Volley.GatherSeconds);

        /// <summary>The armed pulse (1 +- PulseDepth at HolyPulseHz from the Charged status); 1 before Holy Arrow is armed.</summary>
        static float ArmedPulse(HolyGather h) {
            if (h.armedAt < 0f) return 1f;
            float hz = holyPulseHz.Value;
            return 1f + PulseDepth * (hz > 0f ? Mathf.Sin(2f * Mathf.PI * hz * (Time.time - h.armedAt)) : 0f);
        }

        /// <summary>The lit stretch's share of the shaft behind the tip while Lightbeam charges, and its brightness and pulse.</summary>
        static float HolyShape(Fill f, HolyGather h, float L, float share, out float bright, out float pulse) {
            float gather = GatherNow(f, h);
            pulse = 1f;
            bright = 1f + (Mathf.Max(1f, holySqueezeBoost.Value) - 1f) * gather;
            if (gather > 0f && !h.gatherLogged) { h.gatherLogged = true; if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: Holy gather on '{f.bow.name}'"); }
            if (h.armedAt >= 0f) {
                if (!h.armedLogged) { h.armedLogged = true; if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: Holy armed core on '{f.bow.name}'"); }
                pulse = ArmedPulse(h);
                bright *= pulse;
            }
            float squeezed = FillTimeline.Squeeze(gather, share, Mathf.Clamp01(holyCoreFraction.Value));
            // never behind the Fill's own back end, never up to the head gap (that would be a reversed line)
            float minShare = L > 0f ? Mathf.Clamp01((Mathf.Max(0f, headGap.Value) + 0.005f) / L) : 0f;
            return Mathf.Clamp(squeezed, Mathf.Min(minShare, share), share);
        }

        // ---------------------------------------------------------------- Holy Arrow's wind-up

        /// <summary>One frame's strand geometry (DrawStrands): strand k's point at s (0 = hand, 1 = head), the gap closing
        /// onto the shaft's surface at the head and, with the wind-up's braid, over the front of the strand. A struct on the
        /// Fill, rebuilt every frame without allocating.</summary>
        internal struct Helix {
            public Vector3 back, front, u, v;
            public float shaft, gap, turns, spin, converge, braid, cordShare, cordDepth;
            public bool valid;

            public float Length => (front - back).magnitude;

            public Vector3 Point(int k, float s, float lift) {
                float a = 2f * Mathf.PI * ((k - 1) / 3f + spin + turns * s);
                float r = StrandShape.Radius(shaft, gap * WindUp.CordGap(s, braid, cordShare, cordDepth), s, converge) + lift;
                return Vector3.Lerp(back, front, s) + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
            }
        }

        /// <summary>Every frame of a Fill: starts the wind-up once a Charge main with Holy Arrow has reached stage 3 while
        /// Lightbeam charges on this arrow, stops it (once per draw) when any of that stops being true, and prepares this
        /// frame's shift, braid and pulse for the strands. Null when there is none: the strands keep the old squeeze.</summary>
        static HolyWindUp? WindUpFor(Fill f, HolyGather? record, float t, ChargeStages st, Transform tipT) {
            if (f.windUpDone) return null;
            Build build = f.Build;
            bool holyOrb = (build.Ultimates & Ultimates.HolyArrow) != 0;
            // why the wind-up cannot run on this frame (null = it can); the reasons are only built when it has to stop
            bool can = HolyWindUp.On && record != null && build.Main == MainPath.Charge && holyOrb && f.stage >= 3;
            if (f.windUp != null) {
                if (!can) {
                    StopWindUp(f, !HolyWindUp.On ? "switched off"
                                : record == null ? "Holy Arrow's charge was cancelled"
                                : build.Main != MainPath.Charge ? $"the main path is now {build.Main}"
                                : "the Holy Arrow orb is gone");
                    return null;
                }
            }
            else {
                if (!can) return null;
                try { f.windUp = HolyWindUp.Start(f, tipT, st.S3, Paths.HolySecondsFor(build)); }
                catch (Exception e) {
                    f.windUpDone = true; // the old squeeze stays for this arrow
                    MelonLogger.Warning($"[Archery] Holy wind-up: could not start on '{f.bow.name}': {e.Message}; the strands keep the plain squeeze");
                    return null;
                }
            }
            f.windUp!.Prepare(f, record!, t, st.S3, Paths.HolySecondsFor(build));
            return f.windUp;
        }

        static void StopWindUp(Fill f, string why) {
            HolyWindUp? w = f.windUp;
            if (w == null) return;
            f.windUp = null;
            f.windUpDone = true;
            try { w.Stop(f, why); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Holy wind-up: stopping failed: {e.Message}"); }
        }

        /// <summary>Holy Arrow's wind-up on a Charge main's held arrow, from stage 3 to the release (spec Looks, "Holy Arrow
        /// wind-up"): the strands turn to the beam's pale gold (DrawStrands, by Shift), motes run along them from the hand
        /// end to the head ever faster, over the gather the strands' span shortens toward the head (StrandSqueeze: the spiral
        /// packs into the front of the arrow, keeping its turns) while they braid into one bright cord at the head (the gap
        /// closes over the front of the strands), and once armed the cord and head pulse and a ring
        /// breathes around the head. A looping hum on the tip (HumSound) and a faint tick in the bow hand at each mote's
        /// arrival late in the wait. Its lines are children of the Fill's root; the hum is stopped in Stop.</summary>
        internal sealed class HolyWindUp {
            static MelonPreferences_Entry<bool> enabledEntry = null!, strandSqueeze = null!;
            static MelonPreferences_Entry<float> strandSqueezeTo = null!;
            static MelonPreferences_Entry<string> beamColour = null!, humSound = null!, humRtpc = null!;
            static MelonPreferences_Entry<int> moteCount = null!;
            static MelonPreferences_Entry<float> moteSlow = null!, moteFast = null!, moteFadeIn = null!, moteLength = null!, moteWidth = null!,
                cordShare = null!, cordDepth = null!, cordBrightness = null!, ringRadius = null!, ringWidth = null!, ringAlphaLow = null!,
                ringAlphaHigh = null!, ringBreathe = null!, humRtpcFrom = null!, humRtpcTo = null!, moteHaptic = null!, moteHapticFrom = null!;
            static readonly Color DefaultBeam = new Color(1f, 0.945f, 0.761f);
            const int MaxMotes = 32;
            const float MoteWhiteness = 0.5f, MoteEndTaper = 0.08f, MoteTickSeconds = 0.02f, RtpcStep = 0.5f; // internal, not settings
            static Color beam = DefaultBeam;
            static string beamFor = "";
            static Material? ringMat;
            static bool ringMatFlat;

            internal static bool On => enabledEntry != null && enabledEntry.Value;
            internal static float CordShare => cordShare != null ? cordShare.Value : 1f / 3f;
            internal static float CordDepth => cordDepth != null ? cordDepth.Value : 0.85f;
            internal static float CordBrightness => cordBrightness != null ? Mathf.Max(0f, cordBrightness.Value) : 0f;

            /// <summary>The beam's pale gold, parsed again only when the setting's text changes (no per-frame parse).</summary>
            internal static Color BeamColour {
                get {
                    if (beamColour == null) return DefaultBeam;
                    string s = beamColour.Value;
                    if (!ReferenceEquals(s, beamFor)) { beamFor = s; beam = Cfg.Colour(beamColour, DefaultBeam); }
                    return beam;
                }
            }

            internal static void Init() {
                var cat = Cfg.Category("HolyWindUp");
                enabledEntry = cat.CreateEntry("Enabled", true, description: "Holy Arrow's wind-up on a Charge Shot main's arrow from stage 3 (colour shift, motes, the squeeze toward the head, the braid into one cord, the armed ring, the hum). Off = only the plain squeeze toward the tip.");
                strandSqueeze = cat.CreateEntry("StrandSqueeze", true, description: "Over the gather (the last [Volley] GatherSeconds before Holy Arrow is ready) the strands' span along the shaft shortens toward the head, in step with the core line, so the whole spiral packs into the front of the arrow (it keeps its turns and gets denser). The braid tightens on top of it. Off = the strands keep their full length and only braid.");
                strandSqueezeTo = cat.CreateEntry("StrandSqueezeTo", 0.3f, description: "Share of the shaft (hand to head) the strands still cover when Holy Arrow is ready (0 to 1; 1 = no squeeze).");
                beamColour = cat.CreateEntry("BeamColour", "#FFF1C2", description: "The pale gold the three strands (and their halos) turn to between stage 3 and Holy Arrow ready; also the motes' and the ring's colour.");
                moteCount = cat.CreateEntry("MoteCount", 6, description: "Small bright motes running along the strands from the hand end to the head (at most 32).");
                moteSlow = cat.CreateEntry("MoteSlow", 0.25f, description: "The motes' speed at stage 3, in shaft-lengths per second.");
                moteFast = cat.CreateEntry("MoteFast", 1.6f, description: "The motes' speed when Holy Arrow is ready, in shaft-lengths per second (they speed up with the square of the wait).");
                moteFadeIn = cat.CreateEntry("MoteFadeIn", 0.3f, description: "Seconds the motes take to fade in after stage 3.");
                moteLength = cat.CreateEntry("MoteLength", 0.012f, description: "Length in metres of each mote's short streak.");
                moteWidth = cat.CreateEntry("MoteWidth", 0.004f, description: "Width in metres of each mote.");
                cordShare = cat.CreateEntry("CordShare", 1f / 3f, description: "Share of the strands, from the head back, that braid into one cord over the gather (the last [Volley] GatherSeconds before Holy Arrow is ready).");
                cordDepth = cat.CreateEntry("CordDepth", 0.85f, description: "How far (0 to 1) the strands' gap off the shaft closes over the cord when the braid is complete.");
                cordBrightness = cat.CreateEntry("CordBrightness", 0.5f, description: "How much brighter (toward white) the braided cord is when the braid is complete. 0 = no change.");
                ringRadius = cat.CreateEntry("RingRadius", 0.05f, description: "Holy Arrow armed: radius in metres of the soft ring breathing around the head.");
                ringWidth = cat.CreateEntry("RingWidth", 0.004f, description: "Holy Arrow armed: the ring's line width in metres.");
                ringAlphaLow = cat.CreateEntry("RingAlphaLow", 0.25f, description: "Holy Arrow armed: the ring's opacity at the bottom of each breath ([Fill] HolyPulseHz breaths per second).");
                ringAlphaHigh = cat.CreateEntry("RingAlphaHigh", 0.6f, description: "Holy Arrow armed: the ring's opacity at the top of each breath.");
                ringBreathe = cat.CreateEntry("RingBreathe", 0.1f, description: "Holy Arrow armed: the ring grows by this share of its radius at the top of each breath. 0 = a fixed size.");
                humSound = cat.CreateEntry("HumSound", "", description: "A looping sound (names from loaded_assets.txt, 'A;B' = first found) played on the arrow tip from stage 3, stopped on release, cancel or another arrow. Empty = no hum. A sound that is not a loop plays once.");
                humRtpc = cat.CreateEntry("HumRtpc", "", description: "The Wwise game parameter (RTPC) that raises the hum's pitch, set from HumRtpcFrom at stage 3 to HumRtpcTo at Holy Arrow ready. Empty = the hum keeps its pitch (the game's sounds have no general pitch control).");
                humRtpcFrom = cat.CreateEntry("HumRtpcFrom", 0f, description: "HumRtpc's value at stage 3.");
                humRtpcTo = cat.CreateEntry("HumRtpcTo", 100f, description: "HumRtpc's value at Holy Arrow ready.");
                moteHaptic = cat.CreateEntry("MoteHaptic", 0.15f, description: "Strength (0 to 1) of the faint tick in your bow hand as each mote reaches the head, late in the wait. 0 = none.");
                moteHapticFrom = cat.CreateEntry("MoteHapticFrom", 0.5f, description: "How far through the wait (0 = stage 3, 1 = Holy Arrow ready) the mote ticks start.");
            }

            /// <summary>Settings reloaded: the ring's material and the beam colour are resolved again.</summary>
            internal static void Reloaded() { ringMat = null; ringMatFlat = false; beamFor = ""; }

            GameObject root = null!;
            LineRenderer?[] motes = new LineRenderer?[0];
            LineRenderer? ring;
            AudioControl? hum;
            uint rtpcId;
            float rtpcLast = float.NaN;
            float travel, lastTime = float.NaN, t, stage3At;
            bool braidLogged, squeezeLogged, ringLogged, humEndLogged;
            HolyGather? h;

            /// <summary>This frame's colour shift toward the beam (0 at stage 3, 1 at Holy Arrow ready or armed).</summary>
            internal float Shift { get; private set; }
            /// <summary>This frame's braid (0 = three strands, 1 = one cord at the head).</summary>
            internal float BraidNow { get; private set; }
            /// <summary>This frame's share of their full span the strands cover (1 before the gather, StrandSqueezeTo once
            /// Holy Arrow is ready; 1 always with StrandSqueeze off).</summary>
            internal float Span { get; private set; } = 1f;
            /// <summary>This frame's armed pulse for the cord and head (1 before armed).</summary>
            internal float Pulse { get; private set; } = 1f;

            internal static HolyWindUp Start(Fill f, Transform tipT, float stage3At, float holyAt) {
                var w = new HolyWindUp { stage3At = stage3At };
                w.root = new GameObject("HolyWindUp");
                w.root.transform.SetParent(f.root.transform, false);
                string humNote = w.StartHum(tipT);
                if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture,
                    "[Archery] Holy wind-up: started on '{0}' (stage 3 at {1:0.00} s, ready at {2:0.00} s){3}; {4} mote(s), hum {5}",
                    f.bow.name, stage3At, holyAt, f.local ? " (local)" : "", Mathf.Clamp(moteCount.Value, 0, MaxMotes), humNote));
                return w;
            }

            /// <summary>Before the strands are drawn: the shift, braid and pulse of this frame.</summary>
            internal void Prepare(Fill f, HolyGather record, float now, float s3, float holyAt) {
                h = record;
                t = now;
                stage3At = s3;
                bool armed = record.armedAt >= 0f;
                Shift = armed ? 1f : WindUp.Shift(now, s3, holyAt);
                float gather = GatherNow(f, record);
                BraidNow = WindUp.Braid(gather);
                Span = strandSqueeze.Value ? WindUp.Span(gather, strandSqueezeTo.Value) : 1f;
                Pulse = ArmedPulse(record);
                if (BraidNow > 0f && !braidLogged) { braidLogged = true; if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy wind-up: braid on '{f.bow.name}'"); }
                if (Span < 1f && !squeezeLogged) {
                    squeezeLogged = true;
                    if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Holy wind-up: strands squeeze toward the head on '{0}' (to {1:0.##} of their span)",
                        f.bow.name, WindUp.Span(1f, strandSqueezeTo.Value)));
                }
            }

            /// <summary>After the strands: the motes ride this frame's helix, the armed ring, the hum's pitch, the mote ticks.</summary>
            internal void Frame(Fill f, Vector3 tip, Vector3 fwd) {
                float now = Time.time, dt = float.IsNaN(lastTime) ? 0f : Mathf.Max(0f, now - lastTime);
                lastTime = now;
                float bright = f.local ? 1f : Volley.OtherArcherBrightness;
                Color c = BeamColour;

                // the motes: one 2-point line each, made once (again only when MoteCount changes)
                int count = Mathf.Clamp(moteCount.Value, 0, MaxMotes);
                if (motes.Length != count) Remake(count);
                float before = travel;
                travel += WindUp.MoteSpeed(Shift, moteSlow.Value, moteFast.Value) * dt;
                if (travel > 1000f) { travel -= 1000f; before -= 1000f; } // keep float precision on a very long hold
                float fade = WindUp.FadeIn(t, stage3At, moteFadeIn.Value);
                Helix hx = f.helix;
                float len = hx.valid ? hx.Length : 0f;
                float ds = len > 1e-4f ? Mathf.Max(0f, moteLength.Value) / len : 0f, width = Mathf.Max(0f, moteWidth.Value);
                Color moteColour = Color.Lerp(Color.Lerp(FrontColour, c, Shift), Color.white, MoteWhiteness);
                for (int i = 0; i < count; i++) {
                    LineRenderer? lr = motes[i];
                    if (lr == null) continue;
                    float a = fade * bright;
                    if (!hx.valid || a <= 0.001f || width <= 0f) { lr.enabled = false; continue; }
                    float s = WindUp.MotePhase(travel, i, count);
                    int k = 1 + i % 3; // each mote rides one of the three strands
                    float lift = width * 0.5f;
                    lr.SetPosition(0, hx.Point(k, Mathf.Max(0f, s - ds), lift));
                    lr.SetPosition(1, hx.Point(k, s, lift));
                    lr.widthMultiplier = width;
                    a *= StrandShape.Taper(s, MoteEndTaper); // in at the hand end, out into the head
                    lr.startColor = WithAlpha(moteColour, 0f);
                    lr.endColor = WithAlpha(moteColour, a);
                    lr.enabled = true;
                }
                // a faint tick in the shooter's bow hand as motes reach the head, late in the wait
                if (f.local && fade > 0f && count > 0 && Shift > moteHapticFrom.Value && moteHaptic.Value > 0f
                    && WindUp.Arrivals(before, travel, count) > 0) {
                    Controller? hand = Volley.BowHand(f.bow);
                    if (hand != null) hand.TriggerHaptic(Mathf.Clamp01(moteHaptic.Value), MoteTickSeconds);
                }

                // armed: the ring breathing around the head, in step with the core's pulse
                bool armed = h != null && h.armedAt >= 0f;
                if (armed && ring == null) MakeRing(f);
                if (ring != null) {
                    ring.enabled = armed && fwd.sqrMagnitude > 1e-8f;
                    if (ring.enabled) {
                        float since = now - h!.armedAt, hz = holyPulseHz.Value;
                        float breath = WindUp.Breathe(since, hz, 0f, 1f);
                        float alpha = WindUp.Breathe(since, hz, Mathf.Clamp01(ringAlphaLow.Value), Mathf.Clamp01(ringAlphaHigh.Value)) * bright;
                        Transform tr = ring.transform;
                        tr.position = tip - fwd * (Mathf.Max(0f, headGap.Value) * 0.5f); // the middle of the arrowhead
                        tr.rotation = Quaternion.LookRotation(fwd);
                        tr.localScale = Vector3.one * (Mathf.Max(0f, ringRadius.Value) * (1f + Mathf.Max(0f, ringBreathe.Value) * breath));
                        ring.widthMultiplier = Mathf.Max(0f, ringWidth.Value);
                        Color rc = WithAlpha(c, alpha);
                        ring.startColor = rc;
                        ring.endColor = rc;
                    }
                }

                // the hum's pitch climbs with the shift (only through a named RTPC); one log if it ends by itself
                if (hum != null) {
                    if (hum.IsExhausted) {
                        if (!humEndLogged) { humEndLogged = true; if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy wind-up: the hum '{humSound.Value}' ended by itself on '{f.bow.name}' (not a looping sound); it is not restarted"); }
                    }
                    else if (rtpcId != 0u) {
                        float value = Mathf.Lerp(humRtpcFrom.Value, humRtpcTo.Value, Shift);
                        if (float.IsNaN(rtpcLast) || Mathf.Abs(value - rtpcLast) >= RtpcStep) {
                            rtpcLast = value;
                            AkGameObj ak = hum.AkGameObj;
                            if (ak != null) AkSoundEngine.SetRTPCValue(rtpcId, value, ak.gameObject);
                        }
                    }
                }
            }

            void Remake(int count) {
                foreach (LineRenderer? old in motes) if (old != null) UnityEngine.Object.Destroy(old.gameObject);
                motes = new LineRenderer?[count];
                Material? mat = Fx.LineMaterial();
                if (mat == null) return; // logged by Fx; no motes
                for (int i = 0; i < count; i++) {
                    var go = new GameObject("Mote" + (i + 1));
                    go.transform.SetParent(root.transform, false);
                    LineRenderer lr = Fx.NewLine(go, 2, Mathf.Max(0f, moteWidth.Value), false);
                    lr.enabled = false;
                    motes[i] = lr;
                }
            }

            void MakeRing(Fill f) {
                var go = new GameObject("ArmedRing");
                go.transform.SetParent(root.transform, false);
                LineRenderer lr = Fx.NewLine(go, 2, Mathf.Max(0f, ringWidth.Value), false);
                Fx.BuildCircle(lr);
                string note = RingMaterial(out Material? mat);
                lr.material = mat;
                lr.enabled = false;
                ring = lr;
                if (!ringLogged) { ringLogged = true; if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy wind-up: armed ring on '{f.bow.name}' ({note})"); }
            }

            /// <summary>A white copy of the game's 'Orb Connection Line' (one for every ring); the flat line material, logged,
            /// while it cannot be had (tried again at the next ring).</summary>
            static string RingMaterial(out Material? mat) {
                if (ringMat != null && !ringMatFlat) { mat = ringMat; return $"material '{ringMat.name}'"; }
                (Material? m, string failed) = Fx.OrbLineMaterial();
                if (m == null) {
                    ringMat = Fx.LineMaterial();
                    ringMatFlat = true;
                    mat = ringMat;
                    return $"the flat material: 'Orb Connection Line' not found ({failed})";
                }
                var copy = new Material(m) { name = m.name + " (Archery Holy ring)" };
                string props = Fx.ResetColours(copy);
                ringMat = copy;
                ringMatFlat = false;
                mat = copy;
                return $"material '{m.name}' ({Fx.ShaderName(m)}), colour properties reset: {props}";
            }

            /// <summary>The hum on the tip, through the game's AudioManager with a handle that can stop it; a note for the log.</summary>
            string StartHum(Transform tipT) {
                string names = humSound.Value;
                if (string.IsNullOrWhiteSpace(names)) return "none";
                SoundEffectSet? set = Fx.FindSound(names); // a miss is logged by Fx
                if (set == null) return $"'{names}' not found";
                AudioManager am = Alta.Utilities.SingletonBehaviour<AudioManager>.Instance;
                if (am == null) return "none (no audio manager)";
                am.PlaySoundEffectOnObject(set, tipT.gameObject, null!, out AudioControl control);
                if (control == null) return $"'{set.name}' could not play";
                hum = control;
                string rtpc = humRtpc.Value.Trim();
                if (rtpc.Length > 0) rtpcId = AkSoundEngine.GetIDFromString(rtpc);
                return $"'{set.name}'{(rtpcId != 0u ? $", pitch through '{rtpc}' {humRtpcFrom.Value:0.##} to {humRtpcTo.Value:0.##}" : ", steady pitch")}";
            }

            /// <summary>On release, cancel, a lost charge, another arrow, the Fill going or any wind-up condition failing:
            /// the hum stops and the motes and ring go (the strands go back to the plain look on the next frame).</summary>
            internal void Stop(Fill f, string why) {
                AudioControl? a = hum;
                hum = null;
                if (a != null) {
                    try { a.Stop(); } // a no-op once the sound has ended (AudioControl.IsExhausted)
                    catch (Exception e) { MelonLogger.Warning($"[Archery] Holy wind-up: stopping the hum failed: {e.Message}"); }
                }
                motes = new LineRenderer?[0];
                ring = null;
                if (root != null) UnityEngine.Object.Destroy(root);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy wind-up: stopped ({why}) on '{(f.bow != null ? f.bow.name : "?")}'");
            }
        }

        const float ShaftRadiusMax = 0.02f, RoundCross = 0.5f; // internal, not settings

        /// <summary>The arrow's shaft radius as the Fill measures it (once per arrow prefab), for SnapWeight's ember round the shaft.</summary>
        internal static float ShaftRadiusOf(Arrow arrow) => ShaftRadius(arrow);

        // The shaft itself, once per arrow prefab: every mesh's two cross-axis extents (the two smaller of its scaled
        // bounds), and StrandShape.ShaftRadius takes the smallest, halved (the head and fletching are wider than the shaft).
        static float ShaftRadius(Arrow arrow) {
            string prefab = arrow.gameObject.name.Replace("(Clone)", "").Trim();
            if (radii.TryGetValue(prefab, out float known)) return known;
            float r = ShaftRadiusFallback;
            var sizes = new List<float>();
            var seen = new List<string>();
            try {
                foreach (MeshFilter mf in arrow.GetComponentsInChildren<MeshFilter>(true)) {
                    if (mf == null || mf.sharedMesh == null) continue;
                    Vector3 sc = mf.transform.lossyScale;
                    Vector3 size = Vector3.Scale(mf.sharedMesh.bounds.size, new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z)));
                    float[] e = { size.x, size.y, size.z };
                    Array.Sort(e); // e[2] is the long axis; e[0] and e[1] are across it
                    // a flat mesh (a single fletching vane, a decal) is not a shaft: its thickness would put the lines inside it
                    bool round = e[0] >= RoundCross * e[1];
                    if (round) { sizes.Add(e[0]); sizes.Add(e[1]); }
                    seen.Add(string.Format(CultureInfo.InvariantCulture, "'{0}' {1:0.0000} x {2:0.0000}{3}", mf.name, e[0], e[1], round ? "" : " (flat, skipped)"));
                }
                r = StrandShape.ShaftRadius(sizes.ToArray(), ShaftRadiusFallback, ShaftRadiusMax); // only flat meshes: the fallback
            }
            catch (Exception ex) { MelonLogger.Warning($"[Archery] Fill: shaft radius of '{prefab}' could not be measured: {ex.Message}"); r = ShaftRadiusFallback; }
            if (radii.Count >= Cap) radii.Clear();
            radii[prefab] = r;
            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Fill: shaft radius {0:0.0000} m, core offset {1:0.000} m ('{2}'; mesh cross-sizes: {3})",
                r, r + CameraClearance, prefab, seen.Count > 0 ? string.Join(", ", seen.ToArray()) : "none"));
            return r;
        }

        // ---------------------------------------------------------------- the halo material

        static Material? haloMat;
        static bool haloResolved;
        static string lastHaloLog = "";

        static readonly int T_HaloMaterial = Timing.Section("asset ChargeFx.HaloMaterial");
        static Material? HaloMaterial() {
            using var _t = Timing.Begin(T_HaloMaterial);
            if (haloResolved) return haloMat;
            haloResolved = true;
            string line;
            if (string.Equals(haloMaterial.Value.Trim(), "flat", StringComparison.OrdinalIgnoreCase)) {
                haloMat = Fx.LineMaterial();
                line = "[Archery] Fill: halo uses the flat material (HaloMaterial = flat)";
            }
            else {
                (Material? m, string failed) = Fx.OrbLineMaterial();
                if (m == null) {
                    haloMat = Fx.LineMaterial();
                    line = $"[Archery] Fill: halo uses the flat material ('Orb Connection Line' not found: {failed})";
                }
                else {
                    var copy = new Material(m) { name = m.name + " (Archery Fill halo)" };
                    string props = Fx.ResetColours(copy);
                    haloMat = copy;
                    line = $"[Archery] Fill: halo material '{m.name}' ({Fx.ShaderName(m)}); colour properties reset: {props}";
                }
            }
            if (line != lastHaloLog) { lastHaloLog = line; if (Logs.Summary) MelonLogger.Msg(line); }
            return haloMat;
        }


        // ---------------------------------------------------------------- the thump

        /// <summary>Stage 1, from Draw's stage loop (the arrow is the Fill's own, checked by the caller).</summary>
        static void Thump(Fill f, Arrow arrow, float t, float ownS1) {
            f.thumpAt = Time.time;
            if (f.stage < 1) f.stage = 1;
            Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
            Vector3 tip = tipT.position;
            int tier = f.Tier;

            HeadFlash(arrow);
            Fx.Flash(tip, FrontColour, Mathf.Max(1f, flareBoost.Value), FlashRange, Mathf.Max(0.01f, flareSeconds.Value));
            if (f.local) {
                Controller? hand = Volley.BowHand(f.bow);
                float s = thumpHaptic.Value;
                if (hand != null && s > 0f) hand.TriggerHaptic(Mathf.Clamp01(s), ThumpHapticSeconds);
            }
            string sound = "the game's";
            int ms = Mathf.RoundToInt(t * 1000f);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: thump on '{f.bow.name}' (stage 1 of {Mathf.Clamp(tier, 1, 3)}{(f.local ? ", local" : "")}) {ms} ms after the crossing " +
                            $"(own stage 1 at {Mathf.RoundToInt(ownS1 * 1000f)} ms); sound '{sound}'");
        }

        static bool headMissLogged;

        /// <summary>The game's own dissipating skill flash on the arrowhead's meshes (the stage-1 thump, and Charge 3's head
        /// glow). False when the arrow has no head mesh or the effect could not be had.</summary>
        static bool HeadFlash(Arrow arrow) {
            try {
                // Only the head's own meshes: InitializeEffect(Component) would light every renderer under the arrow
                // (shaft, fletching and nock, next to the archer's face; ToolBasedEffectBase.cs:56-83).
                List<MeshRenderer> head = HeadRenderers(arrow);
                PooledObjectDefinition? def = head.Count > 0 ? GlobalSettings<ProgressionSettings>.Instance.DissipateToolBasedEffect : null;
                PooledObject? got = def != null ? def.Get() : null;
                if (got is ToolBasedEffectBase effect) { effect.InitializeEffect(head); return true; }
                if (got != null) got.Release();
                if (head.Count == 0 && !headMissLogged) { headMissLogged = true; MelonLogger.Warning($"[Archery] Fill: no arrowhead mesh found on '{arrow.name}'; the head flash is skipped"); } // G5 (M-5): every level
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: head flash failed: {e.Message}"); }
            return false;
        }

        /// <summary>The enabled mesh renderers (with a mesh) of the arrowhead entity (Arrow.ArrowTip), never the shaft's.</summary>
        static readonly int T_HeadRenderers = Timing.Section("ChargeFx.HeadRenderers");
        static List<MeshRenderer> HeadRenderers(Arrow arrow) {
            using var _t = Timing.Begin(T_HeadRenderers);
            var list = new List<MeshRenderer>();
            if (arrow.ArrowTip == null) return list;
            foreach (MeshRenderer r in arrow.ArrowTip.GetComponentsInChildren<MeshRenderer>(false)) {
                if (r == null || !r.enabled) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) list.Add(r);
            }
            return list;
        }

        // Only a Charge main (or no main path) hears Slowdraw's own Charged sound, only a Multi main the Volley skill's, only a
        // Quick main Quickdraw's: an off-path archer's shot does not exist.
        // The game plays it in the same status callback that runs before the mod's (PickupBasedSkill.SendSerializedMessage),
        // so the archer comes from this draw's earlier statuses (NoteArcher). An unknown archer keeps the game's sound.
        // Stage 1 keeps the game's own sound for a Charge main; the heavier sound is stages 2 and 3 (StageThump).
        [HarmonyPatch(typeof(SkillInputStructure), nameof(SkillInputStructure.PlaySoundAt))]
        static class Charge2Sound {
            static readonly int T = Timing.Section("patch SkillInputStructure.PlaySoundAt / ChargeFx.Charge2Sound");
            static bool live, listenersLogged;

            static bool Prefix(SkillInputStructure __instance, Transform target, ToolBasedSkillStatus status) {
                using var _t = Timing.Begin(T);
                try {
                    if (status != ToolBasedSkillStatus.Charged || ApplicationManager.IsHeadless || __instance == null || target == null) return true;
                    // Each line's own skill: Slowdraw (Charge), the Volley skill (Multi), Quickdraw (Quick).
                    BowDrawSkill? slow = Tree.Slowdraw, volley = VolleySkill.Skill, quick = Tree.Quickdraw;
                    bool isSlow = slow != null && ReferenceEquals(__instance, slow.InputStructure);
                    MainPath line; string skill;
                    if (isSlow) { line = MainPath.Charge; skill = "Slowdraw"; }
                    else if (volley != null && ReferenceEquals(__instance, volley.InputStructure)) { line = MainPath.Multi; skill = "Volley"; }
                    else if (quick != null && ReferenceEquals(__instance, quick.InputStructure)) { line = MainPath.Quick; skill = "Quickdraw"; }
                    else return true;
                    if (!live) { live = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Fill: patch on SkillInputStructure.PlaySoundAt is live"); }
                    NetworkEntity entity = target.GetComponentInParent<NetworkEntity>();
                    if (!OffPath(entity, line, out MainPath main, out string archer)) return true;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Sound: {skill}'s charged sound skipped for '{archer}' (main path {main})");
                    if (isSlow && !listenersLogged) { listenersLogged = true; LogListeners(__instance); }
                    return false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: charge sound check failed: {e.Message}"); return true; }
            }

            static void LogListeners(SkillInputStructure structure) {
                try {
                    var names = new List<string>();
                    object? ev = Traverse.Create(structure).Field("chargedDefaultEffect").GetValue();
                    if (ev != null) {
                        if (Traverse.Create(ev).Field("triggered").GetValue() is UnityEventBase unity) {
                            int n = unity.GetPersistentEventCount();
                            for (int i = 0; i < n; i++) {
                                UnityEngine.Object t = unity.GetPersistentTarget(i);
                                names.Add($"{(t != null ? t.name : "null")}.{unity.GetPersistentMethodName(i)}");
                            }
                        }
                        if (Traverse.Create(ev).Field("Invoked").GetValue() is Delegate d)
                            foreach (Delegate x in d.GetInvocationList())
                                names.Add($"{(x.Target is UnityEngine.Object o && o != null ? o.name : x.Method.DeclaringType?.Name ?? "?")}.{x.Method.Name}");
                    }
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Fill: Slowdraw's charged default effect has {names.Count} listener(s): {(names.Count > 0 ? string.Join(", ", names) : "none")}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: could not list Slowdraw's charged default effect: {e.Message}"); }
            }
        }

        // ---------------------------------------------------------------- the knots at the head, Charge 3's head glow

        /// <summary>Where the strands finish: each completed stage leaves one small bright knot at the head end (a ring around
        /// the shaft, or the game's effect named in KnotEffect), KnotSpacing apart back from the head gap; from stage 3 the
        /// arrowhead glows (a steady light at the tip until the Fill goes, after the game's dissipating flash on the head). One per
        /// Fill; its lines and light are children of the Fill's root, its pooled effects go back to the pool in Stop.</summary>
        internal sealed class StrandKnots {
            static MelonPreferences_Entry<string> knotEffect = null!;
            static MelonPreferences_Entry<float> knotSpacing = null!, knotWidth = null!, headGlowBrightness = null!, headGlowRange = null!;
            static MelonPreferences_Entry<bool> headGlow = null!;
            const float KnotWhiteness = 0.5f; // internal, not a setting

            internal static void Init(MelonPreferences_Category cat) {
                knotEffect = cat.CreateEntry("KnotEffect", "", description: "The game's effect left at the head for each completed charge stage (names from loaded_assets.txt, 'A;B' = first found). Empty = a small bright ring around the shaft.");
                knotSpacing = cat.CreateEntry("KnotSpacing", 0.012f, description: "Metres between the knots at the head end (one per completed charge stage), back from the head gap.");
                knotWidth = cat.CreateEntry("KnotWidth", 0.004f, description: "Thickness in metres of each knot ring.");
                headGlow = cat.CreateEntry("HeadGlow", true, description: "Charge 3: the arrowhead glows, a steady light at the tip from stage 3 until the arrow leaves the string (with the game's own dissipating flash on the head at the moment it starts).");
                headGlowBrightness = cat.CreateEntry("HeadGlowBrightness", 0.9f, description: "Charge 3: brightness of the steady light at the tip.");
                headGlowRange = cat.CreateEntry("HeadGlowRange", 0.18f, description: "Charge 3: range in metres of the steady light at the tip.");
            }

            readonly LineRenderer?[] rings = new LineRenderer?[3];
            readonly PooledObject?[] effects = new PooledObject?[3];
            readonly PoolChangedHandler?[] onReleased = new PoolChangedHandler?[3];
            int placed;          // knots made so far (rings or effects)
            bool headDone;
            Light? lamp;

            /// <summary>Every frame of the Fill from its first thump: new knots for newly completed stages, the rings placed and
            /// lit, Charge 3's head glow once.</summary>
            internal void Frame(Fill f, Arrow arrow, Vector3 tip, Vector3 fwd, Vector3 headGapPoint, float flare, float glow) {
                int stages = Mathf.Clamp(f.stage, 0, 3);
                float spacing = Mathf.Max(0f, knotSpacing.Value), width = Mathf.Max(0f, knotWidth.Value);
                Quaternion rot = fwd.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(fwd) : Quaternion.identity;
                while (placed < stages) {
                    int j = placed++;
                    Vector3 at = headGapPoint - fwd * (spacing * (j + 1));
                    if (TryEffect(j, arrow, at, rot, f.bow.name)) continue;
                    var go = new GameObject("Knot" + (j + 1));
                    go.transform.SetParent(f.root.transform, false);
                    LineRenderer lr = Fx.NewLine(go, 2, width, false);
                    Fx.BuildCircle(lr);
                    rings[j] = lr;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: knot {j + 1} on '{f.bow.name}' (ring)");
                }
                float alpha = (f.local ? 1f : Volley.OtherArcherBrightness)
                              * Mathf.Max(0f, StrandShape.StageBrightness(f.stage, strandStageBrightness.Value))
                              * GlowScale; // the knots' brightness, and through it the head light's below
                Color c = Color.Lerp(FrontColour, Color.white, KnotWhiteness);
                Color shown = WithAlpha(c, alpha);
                float stageWide = Mathf.Max(0f, StrandShape.StageBrightness(f.stage, strandStageWidth.Value)); // wider at each stage, as the strands
                float radius = f.radius + width * stageWide * 0.5f; // sits on the shaft's surface
                for (int j = 0; j < 3; j++) {
                    LineRenderer? lr = rings[j];
                    if (lr == null) continue;
                    Transform tr = lr.transform;
                    tr.position = headGapPoint - fwd * (spacing * (j + 1));
                    tr.rotation = rot;
                    tr.localScale = Vector3.one * radius;
                    lr.widthMultiplier = width * flare * stageWide;
                    lr.startColor = shown;
                    lr.endColor = shown;
                }
                if (stages >= 3 && !headDone) {
                    headDone = true;
                    HeadGlowOn(f, arrow);
                }
                if (lamp != null) {
                    lamp.transform.position = tip;
                    lamp.intensity = Mathf.Max(0f, headGlowBrightness.Value) * alpha * Mathf.Max(0f, glow); // glow: the wind-up's armed pulse, else 1
                    lamp.range = Mathf.Max(0f, headGlowRange.Value);
                }
            }

            bool TryEffect(int j, Arrow arrow, Vector3 at, Quaternion rot, string bowName) {
                string names = knotEffect.Value;
                if (string.IsNullOrWhiteSpace(names)) return false;
                PooledObjectDefinition? def = Fx.FindEffect(names); // a miss is logged by Fx; the ring stands in
                if (def == null) return false;
                PooledObject got = def.Get(at, rot);
                if (got == null) return false;
                Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
                got.transform.SetParent(tipT, true);
                effects[j] = got;
                int slot = j;
                PoolChangedHandler handler = p => { if (ReferenceEquals(p, effects[slot])) effects[slot] = null; };
                onReleased[j] = handler;
                got.Released += handler; // released by its own timer: never touch it again
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: knot {j + 1} on '{bowName}' (effect '{def.name}')");
                return true;
            }

            void HeadGlowOn(Fill f, Arrow arrow) {
                if (!headGlow.Value) return;
                // the game's flash on the head marks the moment; the steady light holds the glow until the Fill goes
                bool flashed = HeadFlash(arrow);
                var go = new GameObject("HeadGlow");
                go.transform.SetParent(f.root.transform, false);
                lamp = go.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = FrontColour;
                lamp.shadows = LightShadows.None;
                lamp.intensity = 0f;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: Charge 3 head glow on '{f.bow.name}' (a steady light at the tip{(flashed ? ", after the game's head flash" : "; no head flash")})");
            }

            /// <summary>With the Fill: pooled knot effects are unparented and go back to the pool.</summary>
            internal void Stop() {
                for (int j = 0; j < 3; j++) {
                    PooledObject? e = effects[j];
                    PoolChangedHandler? handler = onReleased[j];
                    effects[j] = null;
                    onReleased[j] = null;
                    if (e == null) continue;
                    if (handler != null) e.Released -= handler;
                    if (e.IsReleased) continue;
                    e.transform.SetParent(null, true);
                    e.Release();
                }
            }
        }

        // ---------------------------------------------------------------- charged hits you can see (clients)

        sealed class Flight {
            public int Tier;
            public float At;
            public PlayerController? Shooter;
            public Transform? Bow;
        }

        static readonly Dictionary<uint, Flight> flights = new Dictionary<uint, Flight>();
        const float FlightSeconds = 10f, GroundBelow = 1.5f; // internal, not settings

        /// <summary>From Released: a charged arrow (never a Holy Arrow beam) gets its ember trail, and its first hit's looks
        /// are remembered; the embed fallback is armed for it (Weight.WatchEmbed).</summary>
        static readonly int T_Flown = Timing.Section("look start ChargeFx.Flown");
        static void Flown(BowString bow, NetworkEntity? arrowEntity, Resolution r, bool beam) {
            using var _t = Timing.Begin(T_Flown);
            if (arrowEntity == null || arrowEntity.Identifier == 0u) return;
            uint id = arrowEntity.Identifier;
            flights.Remove(id);
            try { Weight.WatchEmbed(arrowEntity); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Hit (embed fallback): watching arrow {id} failed: {e.Message}"); }
            if (r.ChargedTier <= 0 || beam) return;
            if (flights.Count >= Cap) {
                var old = new List<uint>();
                foreach (KeyValuePair<uint, Flight> kv in flights) if (Time.time - kv.Value.At > FlightSeconds) old.Add(kv.Key);
                foreach (uint k in old) flights.Remove(k);
                if (flights.Count >= Cap) flights.Clear();
            }
            PlayerController pc = arrowEntity.CommonPickup != null ? arrowEntity.CommonPickup.LastPlayerInteractor : null!;
            if (pc == null && bow.Pickup != null) pc = bow.Pickup.LastPlayerInteractor;
            flights[id] = new Flight {
                Tier = r.ChargedTier, At = Time.time,
                Shooter = pc != null ? pc : null, Bow = bow.Bow != null ? bow.Bow.transform : bow.transform,
            };
            Arrow arrow = arrowEntity.GetComponent<Arrow>();
            if (arrow != null) EmberTrail.Attach(arrow);
        }

        /// <summary>Every client, from Weight's hit hook (the arrow's first creature or terrain hit) or the embed fallback
        /// (<paramref name="hit"/> null): the charged arrow's hit looks. Nothing for an arrow that was not a charged release.</summary>
        static readonly int T_Hit = Timing.Section("look start ChargeFx.Hit");
        internal static void Hit(uint arrowId, ImpactHitWithTool? hit, Vector3 point, Vector3 normal, HealthObject? creature) {
            using var _t = Timing.Begin(T_Hit, arrowId);
            if (ApplicationManager.IsHeadless) return;
            if (!flights.TryGetValue(arrowId, out Flight f)) return;
            flights.Remove(arrowId); // the first hit only
            if (Time.time - f.At > FlightSeconds) return;
            string? prop = creature != null && !creature.HasCrippleStat ? creature.name : null;
            if (prop != null) creature = null; // a prop with health gets the plain hit look, not a creature's
            try {
                Quaternion rot = normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(normal) : Quaternion.identity;
                var played = new List<string>();
                string where;
                if (creature == null) {
                    Spawn(GroundEffect, point, rot, played);
                    where = prop != null ? $"'{prop}' (not a creature)" : "the ground";
                }
                else {
                    where = $"'{creature.name}'";
                    Spawn(HitEffect, point, rot, played);
                    if (f.Tier >= 2) {
                        Spawn(Charge2HitEffect, point, rot, played);
                        if (GroundUnder(point, creature, out Vector3 ground)) { DustRing.Play(ground); played.Add("dust ring"); }
                    }
                }
                Vector3 p = point;
                if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture,
                    "[Archery] Hit looks: arrow {0} on {1} (charge tier {2}{3}) at ({4:0.00}, {5:0.00}, {6:0.00}): {7}",
                    arrowId, where, f.Tier, hit == null ? ", embed fallback" : "",
                    p.x, p.y, p.z, played.Count > 0 ? string.Join(", ", played.ToArray()) : "none"));
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Hit looks: arrow {arrowId} failed: {e.Message}"); }
        }

        static readonly int T_Spawn = Timing.Section("look ChargeFx.Spawn (game effect)");
        static void Spawn(string names, Vector3 at, Quaternion rot, List<string> played) {
            using var _t = Timing.Begin(T_Spawn, names);
            if (string.IsNullOrWhiteSpace(names)) return;
            PooledObjectDefinition? def = Fx.FindEffect(names);
            if (def == null) { played.Add($"'{names}' (not found)"); return; }
            def.Get(at, rot);
            played.Add($"'{def.name}'");
        }

        /// <summary>The ground within GroundBelow metres under the hit point (anything that is not the struck creature).</summary>
        static readonly int T_GroundUnder = Timing.Section("ChargeFx.GroundUnder (ray)");
        static bool GroundUnder(Vector3 point, HealthObject creature, out Vector3 ground) {
            using var _t = Timing.Begin(T_GroundUnder);
            ground = default;
            Transform? root = Targets.RootOf(creature);
            const float lift = 0.1f;

            if (!Hits.Cast(point + Vector3.up * lift, Vector3.down, GroundBelow + lift, out RaycastHit h, root)) return false;
            ground = h.point;
            return true;
        }

        /// <summary>A charged arrow's ember trail in flight: back colour to transparent, from release until the arrow stops
        /// or 3 s; it then fades out where it is.</summary>
        internal sealed class EmberTrail : MonoBehaviour {
            const float MaxSeconds = 3f, StoppedSpeed = 0.5f, StoppedSeconds = 0.1f, VertexDistance = 0.05f;
            TrailRenderer trail = null!;
            float born, still;
            Vector3 last;

            internal static void Attach(Arrow arrow) => Attach(arrow, 1f, 1f);

            /// <summary>The real arrow's trail at <paramref name="widthScale"/> of a charged arrow's width,
            /// <paramref name="alpha"/> of its opacity and <paramref name="lengthScale"/> of its length in time (SnapWeight: a
            /// Quick main's weighted quick shot, wider and longer per Charge Shot orb); it replaces any trail the arrow still
            /// carries.</summary>
            internal static EmberTrail? Attach(Arrow arrow, float widthScale, float alpha, float lengthScale = 1f) {
                foreach (EmberTrail old in arrow.GetComponentsInChildren<EmberTrail>(true)) Destroy(old.gameObject);
                Transform tip = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
                return AttachTo(tip, widthScale, alpha, lengthScale);
            }

            /// <summary>An ember trail on its own child of <paramref name="parent"/> at <paramref name="widthScale"/> of the
            /// charged arrow's width (Volley.HeavyLook: half); null when trails are off or there is no line material.</summary>
            internal static EmberTrail? AttachTo(Transform parent, float widthScale, float alpha = 1f, float lengthScale = 1f) {
                float seconds = trailSeconds.Value * Mathf.Max(0f, lengthScale), width = trailWidth.Value * Mathf.Max(0f, widthScale);
                if (seconds <= 0f || width <= 0f || Fx.LineMaterial() == null) return null;
                var go = new GameObject("ArcheryEmberTrail");
                go.transform.SetParent(parent, false);
                TrailRenderer tr = go.AddComponent<TrailRenderer>();
                tr.material = Fx.LineMaterial();
                tr.time = seconds;
                tr.widthMultiplier = width;
                tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                tr.minVertexDistance = VertexDistance;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
                Color c = BackColour;
                tr.startColor = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(alpha));
                tr.endColor = new Color(c.r, c.g, c.b, 0f);
                tr.Clear();
                EmberTrail t = go.AddComponent<EmberTrail>();
                t.trail = tr;
                t.born = Time.time;
                t.last = go.transform.position;
                return t;
            }

            static readonly int TUpdate = Timing.Section("Update ChargeFx.EmberTrail");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float dt = Time.deltaTime;
                Vector3 p = transform.position;
                float speed = dt > 0f ? (p - last).magnitude / dt : 0f;
                last = p;
                still = speed < StoppedSpeed ? still + dt : 0f;
                if (Time.time - born < MaxSeconds && still < StoppedSeconds) return;
                LetGo();
            }

            /// <summary>Stopped, out of time, or its carrier is about to go (a heavy arrow landing): what is drawn fades
            /// where it is and the trail destroys itself.</summary>
            internal void LetGo() {
                if (!enabled) return;
                // stopped (or out of time): let what is drawn fade where it is, never ride along on a stuck or picked-up arrow
                enabled = false;
                if (trail != null) trail.emitting = false;
                transform.SetParent(null, true);
                Destroy(gameObject, trail != null ? trail.time + 0.05f : 0f);
            }
        }

        /// <summary>Charge 2 and 3: a dust-coloured circle on the ground under the hit, growing from DustRingFrom to
        /// DustRingTo over DustRingSeconds while it fades.</summary>
        internal sealed class DustRing : MonoBehaviour {
            const float Width = 0.03f, Lift = 0.03f; // internal, not settings
            static readonly Color Dust = new Color(0.76f, 0.66f, 0.5f, 0.8f);
            LineRenderer lr = null!;
            float born, from, to, seconds;

            internal static void Play(Vector3 ground) {
                if (Fx.LineMaterial() == null) return;
                var go = new GameObject("ArcheryDustRing");
                go.transform.position = ground + Vector3.up * Lift;
                go.transform.rotation = Quaternion.LookRotation(Vector3.up); // the circle's local XY plane lies flat
                DustRing d = go.AddComponent<DustRing>();
                d.lr = Fx.NewLine(go, 2, Width, false);
                Fx.BuildCircle(d.lr);
                d.born = Time.time;
                d.from = Mathf.Max(0f, DustRingFrom);
                d.to = Mathf.Max(d.from, DustRingTo);
                d.seconds = Mathf.Max(0.01f, DustRingSeconds);
                d.Frame(0f);
            }

            void Frame(float k) {
                transform.localScale = Vector3.one * Mathf.Lerp(from, to, k);
                Color c = WithAlpha(Dust, Dust.a * (1f - k));
                lr.startColor = c;
                lr.endColor = c;
            }

            static readonly int TUpdate = Timing.Section("Update ChargeFx.DustRing");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float k = (Time.time - born) / seconds;
                if (k >= 1f) { Destroy(gameObject); return; }
                Frame(k);
            }
        }

        // ---------------------------------------------------------------- the game's glow goes only where the Fill is

        /// <summary>True only when the Fill is actually drawn on this arrow; anything else keeps the game's glow (spec Part 9 item 15).</summary>
        internal static bool FillShownFor(NetworkEntity entity) {
            if (ApplicationManager.IsHeadless || entity == null) return false;
            BowString? bow = Volley.BowOf(entity);
            Fill? f = FillOf(bow);
            if (f == null) return false;
            string? why = null;
            if (!Enabled) why = "the Fill is switched off";
            else if (f.arrow != entity.Identifier) why = "the string's arrow changed";
            else if (f.core == null) why = "no core line";
            else if (f.core.sharedMaterial == null) why = "the flat line material was not found";
            else if (!f.core.enabled) why = "the core line is hidden";
            else if (coreWidth.Value <= 0f) why = "the core width is 0";
            else if (!f.local && Volley.OtherArcherBrightness <= 0f) why = "other archers' brightness is 0";
            if (why == null) return true;
            if (!f.notDrawnLogged) {
                f.notDrawnLogged = true;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Fill: not drawn on '{f.bow.name}' ({why}); the game's glow stays");
            }
            return false;
        }
    }
}
