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

namespace QuickshotRing {
    // The Volley skill (Two-Shot, Three-Shot, Burst): 5, then up to 15 spectral ghosts.
    //
    //  - The game's own extra arrows (full networked objects) are never spawned.
    //  - The Volley skill (VolleySkill.cs) is charged by the game's own input: that is the authority for the first
    //    five. The timeline (Release.cs, VolleyTimeline) adds the Full Volley ratchet and Burst's rack from the time
    //    since the 68% crossing.
    //  - Server: the skill's input state per bow + the owner's build + the draw clock; on release it fires the
    //    ghosts, echoes and pellets as "virtual" arrows on one flight model (the real arrow's arc, ArcModel, from a
    //    layout built from the real arrow alone) and deals their damage through the real arrow.
    //  - Clients: the same from the status the game already broadcasts (the shooter uses its own clock, a spectator
    //    the time since the status said charging started), drawn round the nocked arrow; on release the same
    //    virtual arrows run purely as visuals (landing effects in VolleyFx.cs, pellet streaks in Burst.cs).
    // Nothing here is a networked object and no new message is sent.
    internal static class Volley {
        class State {
            public bool volleyCharged;      // the Volley skill's input is active (server) / its Charged status was seen (client)
            public bool chargeCharged;      // client: Slowdraw's Charged status was seen
            public float startedAt = -1f;   // client: when the Volley skill's ChargingStarted was seen (the 68% crossing); -1 = not seen
            public float chargedAt;         // client: when its Charged status was seen
            public Player? serverOwner;     // server: who holds the string
            public string owner = "";       // client: the shooter's name
            public bool local;              // client: the shooter is the local player
            public bool holyCharging;
            public float holyAt;
            public Ghost?[] ghosts = new Ghost?[VolleyMath.Total];
            public float[] seenAt = new float[VolleyMath.Total];
            public int lastShown, lastLogged, lastHeavyLogged;
            public bool offPathLogged;      // the "no ghosts" line for an off-path archer was logged for this draw
            public bool heavySlidLogged;    // the "heavy arrows ... slid out" line was logged for this draw
        }

        static readonly Dictionary<BowString, State> states = new Dictionary<BowString, State>();
        static readonly List<BowString> dead = new List<BowString>();

        static MelonPreferences_Entry<float> innerShare = null!, outerShare = null!, outerInterval = null!, innerRadius = null!, outerRadius = null!,
            visibleFraction = null!, arriveSeconds = null!, outerScale = null!, outerBrightness = null!, shaftWidth = null!, gatherSeconds = null!,
            lifeSeconds = null!, hitFlash = null!, relaxGrace = null!, needleDegrees = null!, nockBack = null!, clientCastRange = null!, otherArcherBrightness = null!;
        static MelonPreferences_Entry<int> liveCap = null!;
        static MelonPreferences_Entry<string> colour = null!, chargedColour = null!, ghostMaterial = null!;
        static MelonPreferences_Entry<bool> rampTest = null!;
        static readonly Color DefaultColour = new Color(0f, 0.89f, 1f);
        static readonly Color DefaultChargedColour = new Color(1f, 0.78f, 0.2f);
        const float MinNockBack = 0.05f; // a nock point on the tip would aim every spectral arrow sideways
        static Material? headMaterial;
        static string headMaterialKind = "";
        static bool loggedHeadTextures;
        static Material? rampMaterial, rampMaterialFor; // ghost 0's ramp-test copy, and the head material it was made from
        static bool rampMaterialTried;
        static string lastRampLog = "";
        static string lastFxError = "";

        /// <summary>[Volley] InnerShare / OuterShare (shared), the defaults until Init ran.</summary>
        internal static float InnerShare => innerShare != null ? innerShare.Value : VolleyMath.InnerShare;
        internal static float OuterShare => outerShare != null ? outerShare.Value : VolleyMath.OuterShare;
        internal static Color Colour => Cfg.Colour(colour, DefaultColour);
        /// <summary>Ghosts of a charged shot (Slowdraw charged) or a Multi main's gold ghosts: orange-to-gold always means they stagger.</summary>
        internal static Color ChargedColour => Cfg.Colour(chargedColour, DefaultChargedColour);

        /// <summary>G3: a Multi main's ghosts are gold exactly from hold stage 1 (VolleyHold: each stage earned by holding a full volley
        /// adds stagger per ghost and damage). Such an archer never has a Slowdraw charge (the path gate), so this replaces the charged
        /// switch for them. G4: the look is staged ring by ring (GoldHold); this one switch says only "some gold" (the release's
        /// flare, shed and landing light, Ricochet's arcs) and is the whole look when [GoldHold] is off or could not be built.</summary>
        internal static bool GoldGhosts(int holdStage) => holdStage >= 1;

        /// <summary>G3: the hold stage this bow's volley has reached right now as this machine sees it (VolleyHold.Stage, the one
        /// function), and G4: how far it has come toward the next one (VolleyHold.Toward, the same clock), for DrawHeld's gold.</summary>
        static void HoldNow(BowString bow, State s, Build b, out int stage, out float toward) {
            stage = 0; toward = 0f;
            if (VolleyHold.Orbs(b) == 0) return;
            bool charged = Inputs(bow, s, null, out int tier, out float t, out VolleyTimes tm);
            if (!charged || tier <= 0) return;
            float per = Paths.Values.VolleyStageSeconds;
            stage = VolleyHold.Stage(b, t, tm, per);
            toward = VolleyHold.Toward(b, t, tm, per);
        }

        /// <summary>G3, clients: seconds since this bow's 68% crossing as its volley state knows them (a spectator: since the Volley
        /// skill's ChargingStarted status), or null with no state. Draws.Release reads it for a spectator's hold stage with no relay.</summary>
        internal static float? HeldSeconds(BowString bow) {
            if (bow == null || !states.TryGetValue(bow, out State s)) return null;
            Inputs(bow, s, null, out _, out float t, out _);
            return t;
        }
        /// <summary>The colour of an archer's ghosts: <paramref name="gold"/> is the ghosts' own switch (a charged shot, or
        /// <see cref="GoldGhosts"/>; DrawHeld while the bow is held, Launch at the release). Burst's fifteen (held pellet arrows,
        /// streaks, shed sparks, landing sparks, puffs) take it from the same switch, so they never differ from the ghosts.</summary>
        internal static Color GhostColour(bool gold) => gold ? ChargedColour : Colour;
        /// <summary>G4: a ghost on its way from its own colour (0) to gold (1) as its ring's hold stage sweeps round (GoldHold).</summary>
        internal static Color GhostColour(float gold) => gold <= 0f ? Colour : gold >= 1f ? ChargedColour : Color.Lerp(Colour, ChargedColour, gold);
        /// <summary>H1: the same, for an arrow of a locked (homing) release: [SeekingVolley] Colour (SeekLook) in place of its own colour and
        /// of the hold stages' gold (the owner: "turn all the ghost arrows green"). The one colour function for ghosts and pellet streaks.</summary>
        internal static Color GhostColour(float gold, bool homing) => homing ? SeekLook.Colour : GhostColour(gold);
        /// <summary>H2: a ghost on the bow on its way to the homing green (<paramref name="green"/> 0 to 1: the green sweeps round the
        /// rings when a release would home, SeekLook.BowGreen); 1 in a locked release's flight.</summary>
        internal static Color GhostColour(float gold, float green) =>
            green <= 0f ? GhostColour(gold) : green >= 1f ? SeekLook.Colour : Color.Lerp(GhostColour(gold), SeekLook.Colour, green);

        /// <summary>H1: metres from the real arrow's tip to each core arrow's (a tight ring on the arrow's own line).</summary>
        const float CoreRadius = 0.012f;
        /// <summary>Brightness of the one landing light per volley; 0 = none.</summary>
        internal static float HitFlash => hitFlash != null ? hitFlash.Value : 0f;
        internal static float ShaftWidth => shaftWidth != null ? shaftWidth.Value : 0.012f;
        internal static float OtherArcherBrightness => otherArcherBrightness != null ? otherArcherBrightness.Value : 1f;
        /// <summary>[Volley] OuterBrightness: the outer ring's ghosts' brightness (Burst's held pellet arrows on that ring match it).</summary>
        internal static float OuterBrightness => outerBrightness != null ? outerBrightness.Value : 0.55f;
        /// <summary>[Volley] GatherSeconds: the fold into the arrow before Holy Arrow is ready (the Fill's squeeze uses it too).</summary>
        internal static float GatherSeconds => gatherSeconds != null ? gatherSeconds.Value : DrawTimeline.GatherSeconds;
        /// <summary>[Volley] NeedleDegrees (shared): how far each needle leaves the arrow's line.</summary>
        internal static float NeedleDegrees => needleDegrees != null ? needleDegrees.Value : NeedlePattern.Degrees;

        internal static void Init() {
            var cat = Cfg.Category("Volley");
            innerShare = Cfg.Shared(cat.CreateEntry("InnerShare", VolleyMath.InnerShare, description: "Damage of each of the five inner extras, as a share of the charged main arrow. 5 x 0.15 = +75%."));
            outerShare = Cfg.Shared(cat.CreateEntry("OuterShare", VolleyMath.OuterShare, description: "Damage of each of the ten outer extras. 10 x 0.06 = +60%."));
            outerInterval = Cfg.Shared(cat.CreateEntry("OuterInterval", VolleyTimes.Default.Interval, description: "Seconds between each of the ten outer extras appearing (the Full Volley ratchet)."));
            innerRadius = Cfg.Shared(cat.CreateEntry("InnerRadius", VolleyMath.InnerRadius, description: "Metres from the real arrow's tip to the five inner tips."));
            outerRadius = Cfg.Shared(cat.CreateEntry("OuterRadius", VolleyMath.OuterRadius, description: "Metres from the real arrow's tip to the ten outer tips."));
            relaxGrace = Cfg.Shared(cat.CreateEntry("RelaxGraceSeconds", 0.35f, description: "From the 68% crossing on, the string must stay relaxed this long before any bow skill is lost (Quickdraw included, so a missed snap re-arms only after this). 0 = the game's instant cancel."));
            visibleFraction = cat.CreateEntry("VisibleFraction", 0.4f, description: "How much of each extra is drawn while held, from the tip back. The rest fades out so nothing sits next to your eye.");
            arriveSeconds = cat.CreateEntry("ArriveSeconds", 0.167f);
            outerScale = cat.CreateEntry("OuterScale", 0.7f, description: "Thickness of the outer six relative to the inner three.");
            outerBrightness = cat.CreateEntry("OuterBrightness", 0.55f);
            shaftWidth = cat.CreateEntry("ShaftWidth", 0.012f);
            gatherSeconds = Cfg.Shared(cat.CreateEntry("GatherSeconds", DrawTimeline.GatherSeconds, description: "How long the fifteen take to fold into the arrow, ending the moment Holy Arrow is ready. Before that they sit fanned out."));
            lifeSeconds = Cfg.Shared(cat.CreateEntry("LifeSeconds", 3f, description: "Longest an extra arrow can fly before it vanishes."));
            hitFlash = cat.CreateEntry("HitFlash", 1.5f, description: "Brightness of the one light per volley, at its densest landing. 0 = none.");
            colour = cat.CreateEntry("Colour", "#00E3FF", description: "Shafts of uncharged ghosts, needles and pellets, and their landing sparks.");
            chargedColour = cat.CreateEntry("ChargedColour", "#FFC733", description: "Shafts of a charged shot's ghosts and pellets (Slowdraw charged) and of a multi-arrow main's gold ghosts (with Charge Shot orbs), and their landing sparks. Burst's fifteen pellets always have the colour of the same archer's ghosts.");
            // [Volley] EchoShare is retired (Task 5): needles take their share from [Complements] NeedleShare.
            needleDegrees = Cfg.Shared(cat.CreateEntry("NeedleDegrees", NeedlePattern.Degrees, description: "Quickdraw main's needles: degrees each one leaves the arrow's line, spread evenly round it (a quick shot and Quickdraw 4's line alike)."));
            nockBack = Cfg.Shared(cat.CreateEntry("NockBack", 0.75f, description: "Metres behind the real arrow's tip, along the arrow, that every ghost, echo and pellet aims from at release."));
            liveCap = cat.CreateEntry("LiveCap", 200, description: "Server: above this many live spectral arrows, each one looks for hits every other physics tick (over twice the distance).");
            clientCastRange = cat.CreateEntry("ClientCastRange", 40f, description: "Clients: other archers' spectral arrows look for what they hit only this close to the camera (metres); farther ones fly on until their life ends. Your own always look.");
            otherArcherBrightness = cat.CreateEntry("OtherArcherBrightness", 0.6f, description: "Brightness of other archers' ghosts, needles and pellets (1 = as bright as your own).");
            ghostMaterial = cat.CreateEntry("HeadMaterial", "skill", description: "'skill' = the game's skill-glow material on the extra arrowheads; 'flat' = plain colour (use if the heads are invisible).");
            rampTest = cat.CreateEntry("RampTest", true, description: "Test: ghost 0 (the top inner ghost) gets its own copy of the head material with every colour-ramp texture set to the first one (the log shows which). The other ghosts keep the original.");
            VolleyFx.Init(cat);
            GoldHold.Init();
            Burst.Init();
            NeedleLook.Init();
            HeavyLook.Init();
            Cfg.Reloaded += () => { headMaterial = null; rampMaterial = null; rampMaterialFor = null; rampMaterialTried = false; };
        }

        static State Get(BowString bow) {
            if (!states.TryGetValue(bow, out State s)) states[bow] = s = new State();
            return s;
        }

        /// <summary>The volley's beats from the live settings (shared) and Slowdraw's own charge time.</summary>
        internal static VolleyTimes Times => outerInterval == null ? VolleyTimes.Default
            : new VolleyTimes(Tree.TwoShotSeconds, Tree.ThreeShotSeconds, outerInterval.Value, Tree.RippleSeconds, Tree.Stages.S2, Tree.RackSeconds);

        // The Volley tier, whether the skill is charged, and the seconds since the 68% crossing, as this machine knows them:
        // the server from the owner's build and its draw clock; the shooter from its own build and clock; a spectator from
        // the build the server sent and the time since the Volley skill's status said charging started (or since Charged,
        // counted from the Volley time, when the start was not seen). `at` (server only): the time the release is decided with
        // (Draws.ServerTime, the echo fix) instead of the server's own clock.
        // `tm`: this archer's own volley timeline (Paths.VolleyTimesFor: a Multi main's Quick orbs shorten it). The game's
        // Volley timer is shared and set to the fastest build's; the server holds the Charged status back until the archer's
        // own VolleyAt (OwnTime), and VolleyTimeline.Ghosts waits for that time too.
        static bool Inputs(BowString bow, State s, float? at, out int tier, out float t, out VolleyTimes tm) {
            Build b = BuildOf(s);
            tm = Paths.VolleyTimesFor(b);
            tier = b.Volley;
            // Only a Multi main (or no main path) has the volley: anyone else's held ghosts, rack and beats are tier 0.
            if (tier > 0 && !Resolver.OnPath(b.Main, MainPath.Multi)) {
                tier = 0;
                if (!s.offPathLogged) { s.offPathLogged = true; if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley: no ghosts on '{bow.name}' (main path {b.Main})"); }
            }
            if (NetworkSceneManager.IsServer && at.HasValue) t = at.Value;
            else if (NetworkSceneManager.IsServer || s.local) t = Draws.Elapsed(bow) ?? 0f;
            else t = s.startedAt >= 0f ? Time.time - s.startedAt : tm.VolleyAt + (Time.time - s.chargedAt); // Charged comes at the archer's own VolleyAt
            return s.volleyCharged;
        }

        /// <summary>The shooter's build as this machine knows it: the server's own, the local player's, or the one the server sent.</summary>
        static Build BuildOf(State s) =>
            NetworkSceneManager.IsServer ? Builds.ServerBuild(s.serverOwner) : s.local ? Builds.LocalBuild() : Builds.ClientBuild(s.owner);

        /// <summary>The shooter's bow hand (as BowString.OnShoot finds it), or null.</summary>
        internal static Controller? BowHand(BowString bow) {
            Pickup p = bow.Bow != null ? bow.Bow.Pickup : null!;
            if (p == null || p.Interactors.Count == 0 || p.Interactors[0] == null) return null;
            Controller c = p.Interactors[0].Controller;
            return c != null ? c : null;
        }

        /// <summary>The bow's draw is over: forget its beats and remove Burst's held pellet arrows.</summary>
        static void DropFx(BowString bow) {
            VolleyFx.Beats.Forget(bow);
            GoldHold.Forget(bow); // its hold stages start again with the next volley (G4)
            Burst.Held.Drop(bow); // where they were is kept for this frame's release (Burst.Held.Hand)
        }

        /// <summary>How many ghosts are showing, and would fire, on this bow right now (0 to 15).</summary>
        internal static int Ghosts(BowString bow) => Ghosts(bow, null);

        /// <summary>As <see cref="Ghosts(BowString)"/>; on the server at <paramref name="atSeconds"/> after the crossing when given
        /// (null = this machine's own time).</summary>
        internal static int Ghosts(BowString bow, float? atSeconds) {
            if (bow == null || !states.TryGetValue(bow, out State s)) return 0;
            bool charged = Inputs(bow, s, atSeconds, out int tier, out float t, out VolleyTimes tm);
            int n = VolleyTimeline.Ghosts(tier, charged, t, tm);
            if (n != s.lastLogged) {
                s.lastLogged = n;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley: {(NetworkSceneManager.IsServer ? "armed" : "showing")} {n} on '{bow.name}' (tier {tier}, t {Mathf.RoundToInt(t * 1000f)} ms)");
            }
            return n;
        }

        /// <summary>Burst's rack has happened on this bow.</summary>
        internal static bool Racked(BowString bow) => Racked(bow, null);

        /// <summary>As <see cref="Racked(BowString)"/>, at <paramref name="atSeconds"/> on the server when given.</summary>
        internal static bool Racked(BowString bow, float? atSeconds) {
            if (bow == null || !states.TryGetValue(bow, out State s)) return false;
            bool charged = Inputs(bow, s, atSeconds, out int tier, out float t, out VolleyTimes tm);
            return VolleyTimeline.Racked(tier, charged, t, tm);
        }

        /// <summary>Client: this bow's shot is charged (Slowdraw), as far as the status broadcasts tell.</summary>
        internal static bool ChargedOnClient(BowString bow) => bow != null && states.TryGetValue(bow, out State s) && s.chargeCharged;

        /// <summary>Seconds a dip below the draw threshold may last without losing anything (shared setting).</summary>
        internal static float RelaxGrace => relaxGrace != null ? relaxGrace.Value : 0f;

        /// <summary>The volley has been used up (fired, or absorbed by Holy Arrow).</summary>
        internal static void Consume(BowString bow) {
            if (bow == null || !states.TryGetValue(bow, out State s)) return;
            foreach (Ghost? g in s.ghosts) if (g != null) UnityEngine.Object.Destroy(g.gameObject);
            states.Remove(bow);
            DropFx(bow);
        }

        /// <summary>Client: this bow's shooter has Holy Arrow charging, so a full ring should fold into the arrow.</summary>
        internal static void SetHolyCharging(BowString bow, bool charging) {
            if (bow == null) return;
            if (!charging && !states.ContainsKey(bow)) return;
            State s = Get(bow);
            if (charging && !s.holyCharging) s.holyAt = Time.time;
            s.holyCharging = charging;
        }

        /// <summary>Server: the Volley skill's input on this bow became active (charged) or not (VolleySkill.ServerState).</summary>
        static readonly int T_ServerVolleyChanged = Timing.Section("Volley.ServerVolleyChanged");
        internal static void ServerVolleyChanged(BowString bow, bool charged, Player? owner) {
            using var _t = Timing.Begin(T_ServerVolleyChanged);
            if (bow == null) return;
            if (!charged && !states.ContainsKey(bow)) return; // already fired or never charged: nothing to clear
            State s = Get(bow);
            s.volleyCharged = charged;
            if (owner != null) s.serverOwner = owner;
            Ghosts(bow); // logs the count when it changed
        }

        /// <summary>Client: a status of the Volley skill for the arrow on this bow (VolleySkill.ClientStatus).</summary>
        static readonly int T_ClientVolleyStatus = Timing.Section("Volley.ClientVolleyStatus");
        internal static void ClientVolleyStatus(BowString bow, ToolBasedSkillStatus status, string owner, bool local) {
            using var _t = Timing.Begin(T_ClientVolleyStatus, owner);
            if (bow == null) return;
            bool clears = status == ToolBasedSkillStatus.Cancelled || status == ToolBasedSkillStatus.Consumed;
            if (clears && !states.ContainsKey(bow)) return;
            State s = Get(bow);
            s.owner = owner;
            s.local = local;
            switch (status) {
                case ToolBasedSkillStatus.ChargingStarted: s.startedAt = Time.time; s.volleyCharged = false; break;
                case ToolBasedSkillStatus.Charged: s.volleyCharged = true; s.chargedAt = Time.time; break;
                default: s.volleyCharged = false; s.startedAt = -1f; break;
            }
            Ghosts(bow); // logs the count when it changed
        }

        /// <summary>Client: a status of Slowdraw (the charge) for the arrow on this bow (VolleySkill.ClientStatus). The archer is
        /// kept too: a Charge main's heavy arrows show from this state alone (no Volley status may ever come for them).</summary>
        static readonly int T_ClientChargeStatus = Timing.Section("Volley.ClientChargeStatus");
        internal static void ClientChargeStatus(BowString bow, ToolBasedSkillStatus status, string owner, bool local) {
            using var _t = Timing.Begin(T_ClientChargeStatus, owner);
            if (bow == null) return;
            if (status == ToolBasedSkillStatus.Charged) {
                State c = Get(bow);
                c.chargeCharged = true;
                c.owner = owner;
                c.local = local;
            }
            else if ((status == ToolBasedSkillStatus.Cancelled || status == ToolBasedSkillStatus.Consumed) && states.TryGetValue(bow, out State s)) s.chargeCharged = false;
        }

        /// <summary>Client: a Charge main's heavy arrows showing on this bow (0 to 6): two per multi-arrow orb, from the moment
        /// this archer's own stage 1 completes (Draws.ChargeReady, as the Fill and the release decide it).</summary>
        static int HeavyShowing(BowString bow, State s, Build b) {
            int n = b.Main == MainPath.Charge && Draws.ChargeReady(s.chargeCharged, b, Draws.Elapsed(bow))
                ? Math.Min(ReleasePlan.MaxHeavy, Complements.HeavyArrows(b, Paths.Values)) : 0;
            if (n != s.lastHeavyLogged) {
                s.lastHeavyLogged = n;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley: showing {n} heavy arrows on '{bow.name}' (main path {b.Main}, multi-arrow {b.Volley})");
            }
            return n;
        }

        // dock -> its bow string, or null when the dock is known not to be a string dock (b3-review I-1: misses are remembered too,
        // so a quiver, rack or chest replayed at a chunk load costs one scene search, not one per arrow stack)
        static readonly Dictionary<PickupDock, BowString?> bowByDock = new Dictionary<PickupDock, BowString?>();
        static readonly List<PickupDock> deadDocks = new List<PickupDock>();
        const int DockCacheCap = 1024;
        static readonly Dictionary<Shooter, BowString?> bowByShooter = new Dictionary<Shooter, BowString?>();
        static readonly int TBowForDock = Timing.Section("scene Volley.BowForDock search (FindObjectsOfType<BowString>)"),
            TBowForShooter = Timing.Section("scene Volley.BowForShooter search (FindObjectsOfType<BowString>)"),
            TBowOf = Timing.Section("scene Volley.BowOf search (FindObjectsOfType<BowString>)");

        /// <summary>The bow string this dock is the nocking point of, or null if it is some other dock. Remembered either way;
        /// a destroyed dock's entry goes when the cache is full (then, if still full, the whole cache).</summary>
        internal static BowString? BowForDock(PickupDock? dock) {
            if (dock == null) return null;
            if (bowByDock.TryGetValue(dock, out BowString? known)) {
                if (ReferenceEquals(known, null)) return null;                  // a remembered miss
                if (known != null && known.StringDock == dock) return known;    // a live hit
                bowByDock.Remove(dock);                                          // its bow is gone: ask again
            }
            BowString found = dock.GetComponentInParent<BowString>();
            if (found == null || found.StringDock != dock) {
                found = null!;
                // H3 section 3: the registry of bow strings (BowRegistry), never a scene-wide search (it cost 11 to 37 ms on clients)
                using (Timing.Begin(TBowForDock))
                foreach (BowString bs in BowRegistry.All())
                    if (bs.gameObject.activeInHierarchy && bs.StringDock == dock) { found = bs; break; }
            }
            if (bowByDock.Count >= DockCacheCap) {
                deadDocks.Clear();
                foreach (PickupDock d in bowByDock.Keys) if (d == null) deadDocks.Add(d!); // destroyed: Unity says null, the reference is not
                foreach (PickupDock d in deadDocks) bowByDock.Remove(d);
                if (bowByDock.Count >= DockCacheCap) bowByDock.Clear();
            }
            // A miss is remembered for good, so never record one asked about while the dock was inactive (b4b-review M-7:
            // GetComponentInParent and FindObjectsOfType both skip inactive objects).
            if (found == null && !dock.gameObject.activeInHierarchy) return null;
            bowByDock[dock] = found != null ? found : null;
            return found;
        }

        /// <summary>The bow string a Shooter component belongs to.</summary>
        internal static BowString? BowForShooter(Shooter? shooter) {
            if (shooter == null) return null;
            if (bowByShooter.TryGetValue(shooter, out BowString? known)) {
                if (ReferenceEquals(known, null)) return null;   // G2: a remembered miss (a Shooter that is no bow's, e.g. ItemShooter)
                if (known != null) return known;
            }
            BowString? found = null;
            using (Timing.Begin(TBowForShooter))
            foreach (BowString bs in BowRegistry.All())
                if (bs.gameObject.activeInHierarchy && BowRegistry.ShooterOf(bs) == shooter) { found = bs; break; }
            // A miss is remembered as BowForDock remembers one, but only for an active shooter with no bow string anywhere in its
            // hierarchy, inactive ones included (FindObjectsOfType skips inactive objects; a bow's 'shooter' is a serialized
            // reference inside the bow's own prefab, so such a shooter can never become a bow's).
            if (found == null && (!shooter.gameObject.activeInHierarchy || shooter.transform.root.GetComponentInChildren<BowString>(true) != null)) return null;
            if (bowByShooter.Count > 256) bowByShooter.Clear();
            bowByShooter[shooter] = found;
            return found;
        }

        /// <summary>The bow string this entity is (a bow string itself) or is nocked on (an arrow).</summary>
        internal static BowString? BowOf(NetworkEntity entity) {
            if (entity == null) return null;
            BowString direct = entity.GetComponent<BowString>();
            if (direct != null) return direct;
            Pickup p = entity.CommonPickup;
            if (p == null) return null;
            if (p.IsDocked) {
                BowString? viaDock = BowForDock(p.Dock);
                if (viaDock != null) { BowRegistry.Nocked(entity.Identifier, viaDock); return viaDock; }
            }
            // Last resort: whichever bow string reports this arrow as the one on it. H3 section 3: this ran (11 to 37 ms, a scene-wide
            // search) about 70 ms after almost every release, for the skill status that names the arrow just shot: it has left the
            // string, so no string holds it and the search always came back empty. Now: the string it was last seen on, then the
            // registry of bow strings; the answer is the same (a string whose dock holds this arrow), without the scene search.
            using var _t = Timing.Begin(TBowOf);
            // H4 (H23-review-opus minor 7): active strings only, as the scene search it replaced (FindObjectsOfType) found
            BowString? last = BowRegistry.LastNockedOn(entity.Identifier);
            if (last != null && last.gameObject.activeInHierarchy && last.StringDock != null && last.StringDock.HasDockedPickup && last.StringDock.DockedPickup == p) return last;
            foreach (BowString bs in BowRegistry.All())
                if (bs.gameObject.activeInHierarchy && bs.StringDock != null && bs.StringDock.HasDockedPickup && bs.StringDock.DockedPickup == p) return bs;
            return null;
        }

        /// <summary>H3 section 3: every bow string that exists, added when it wakes (BowString.Awake) and dropped when Unity has destroyed
        /// it; seeded once with one scene search the first time it is asked (bows woken before the mod patched). Plus the string each
        /// arrow was last seen nocked on (bounded).</summary>
        internal static class BowRegistry {
            static readonly List<BowString> bows = new List<BowString>();
            static readonly Dictionary<BowString, Shooter?> shooters = new Dictionary<BowString, Shooter?>();
            static readonly Dictionary<uint, BowString> nocked = new Dictionary<uint, BowString>();
            static bool seeded;
            static readonly int TSeed = Timing.Section("scene Volley.BowRegistry seed (once, FindObjectsOfType<BowString>)");

            internal static void Add(BowString bs) { if (bs != null && !bows.Contains(bs)) bows.Add(bs); }

            internal static List<BowString> All() {
                if (!seeded) {
                    seeded = true;
                    using (Timing.Begin(TSeed))
                    foreach (BowString bs in Resources.FindObjectsOfTypeAll<BowString>())
                        if (bs != null && bs.gameObject.scene.IsValid()) Add(bs);   // scene objects only, inactive ones too (not prefab assets)
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley: bow string registry seeded with {bows.Count} string(s)");
                }
                bows.RemoveAll(b => b == null);
                return bows;
            }

            internal static Shooter? ShooterOf(BowString bs) {
                if (shooters.TryGetValue(bs, out Shooter? s) && s != null) return s;
                Shooter? found = Traverse.Create(bs).Field("shooter").GetValue<Shooter>();
                if (shooters.Count > 512) shooters.Clear();
                shooters[bs] = found;
                return found;
            }

            internal static void Nocked(uint arrow, BowString bs) {
                if (nocked.Count >= 256) nocked.Clear();
                nocked[arrow] = bs;
            }

            internal static BowString? LastNockedOn(uint arrow) => nocked.TryGetValue(arrow, out BowString bs) && bs != null ? bs : null;
        }

        [HarmonyPatch(typeof(BowString), "Awake")]
        static class BowWoke {
            static readonly int T = Timing.Section("patch BowString.Awake / Volley.BowWoke (registry)");
            static void Postfix(BowString __instance) {
                using var _t = Timing.Begin(T);
                try { BowRegistry.Add(__instance); } catch { }
            }
        }

        // ---------------------------------------------------------------- the game's own extras

        // The vanilla extras are real networked arrows. Never make them. (Two-Shot and Three-Shot run on the Volley
        // skill now; this stays for the case where the copy could not be made.)
        [HarmonyPatch(typeof(BowMultiArrowShot), "CreateExtraArrows")]
        static class NoVanillaExtras {
            static bool Prefix() => false;
        }

        // The Volley skill's state hooks (server input, client statuses) live in VolleySkill.cs; a short dip at full
        // draw (DrawGrace) lives in Draws.cs.

        // ---------------------------------------------------------------- firing (server, shooter and every other client)

        // The game calls this on every machine the moment an arrow leaves a bow. It subscribes to OnShootingEntity before
        // any skill does (ExtraArrowUpdater.Initialize), so the release is decided here before any skill resets its input.
        [HarmonyPatch(typeof(ExtraArrowUpdater), "ShootExtraArrows")]
        static class Fire {
            static readonly int T = Timing.Section("patch ExtraArrowUpdater.ShootExtraArrows / Volley.Fire (the whole release)");
            // G5: the release's stages, each its own section (nested in the one above)
            static readonly int TResolve = Timing.Section("release 1 resolve / Draws.Release"),
                TFillRelease = Timing.Section("release 2 look / ChargeFx.Released"),
                TSnapRelease = Timing.Section("release 3 look / SnapWeight.Released"),
                TDropFx = Timing.Section("release 4 held looks dropped / Volley.DropFx"),
                TLaunch = Timing.Section("release 5 launch / Volley.Launch"),
                TSkyfall = Timing.Section("release 5 Skyfall / Skyfall.Fire (held, not launched)"),
                TGhostsGone = Timing.Section("release 6 held ghosts destroyed / Volley.Fire");
            static void Prefix(BowString __0, NetworkEntity __1, Vector3 __2) {
                using var _t = Timing.Begin(T, Timing.On && __1 != null ? __1.Identifier : 0u);
                if (__0 == null) return;
                Resolution r;
                try { using (Timing.Begin(TResolve)) r = Draws.Release(__0, __1); }
                catch (Exception e) {
                    MelonLogger.Error($"[Archery] Release: deciding the release failed, firing what is showing: {e}");
                    r = new Resolution(ShotKind.Held, UltimateUse.None, new ReleasePlan(Ghosts(__0), 0, 0), 0);
                }
                try { using (Timing.Begin(TFillRelease)) ChargeFx.Released(__0, __1, r); } // the Fill goes with the arrow (reads the armed flag)
                catch (Exception e) { MelonLogger.Error($"[Archery] Fill: release handling failed: {e}"); }
                try { using (Timing.Begin(TSnapRelease)) SnapWeight.Released(__0, __1, r); } // a Quick main's weighted quick shot: its ember goes, its trail starts
                catch (Exception e) { MelonLogger.Error($"[Archery] Snap weight: release handling failed: {e.Message}"); }
                HolyArrow.ClearClientArmed(__0); // the release has read it; the next arrow starts unarmed
                try {
                    // A spectator may have no state of its own: it draws what the server's relayed decision says.
                    Ghost?[] ghosts = states.TryGetValue(__0, out State s) ? s.ghosts : new Ghost?[VolleyMath.Total];
                    states.Remove(__0);
                    using (Timing.Begin(TDropFx)) DropFx(__0);
                    if (__1 != null) {
                        // Skyfall (admin skill): a cast or fizzle is held and never launched; every other release exactly as before
                        if (Skyfall.IsCast(__1.Identifier)) using (Timing.Begin(TSkyfall)) Skyfall.Fire(__0, __1);
                        else using (Timing.Begin(TLaunch)) Launch(__0, __1, __2, r, ghosts);
                    }
                    using (Timing.Begin(TGhostsGone))
                    for (int i = 0; i < ghosts.Length; i++) {
                        if (ghosts[i] != null) UnityEngine.Object.Destroy(ghosts[i]!.gameObject);
                        ghosts[i] = null;
                    }
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Volley: firing failed: {e}"); }
            }
        }

        /// <summary>Every spectral part of the release flies on the real arrow's arc model: ghosts, echoes and pellets.</summary>
        internal static void Launch(BowString bow, NetworkEntity arrowEntity, Vector3 velocity, Resolution r, Ghost?[] held) {
            Arrow arrow = arrowEntity.GetComponent<Arrow>();
            if (arrow == null || velocity.sqrMagnitude < 0.01f) return;
            bool server = NetworkSceneManager.IsServer, visuals = !ApplicationManager.IsHeadless;
            PlayerController pc = bow.Pickup != null ? bow.Pickup.LastPlayerInteractor : null!;
            if (pc == null && arrowEntity.CommonPickup != null) pc = arrowEntity.CommonPickup.LastPlayerInteractor;
            bool local = pc != null && pc.IsLocalPlayer;
            Player? shooter = pc != null ? pc.NetworkPlayer : null;
            // The shooter's build as this machine knows it (as Draws.Release finds it): the spread and the gold look follow it.
            Build build = server ? Builds.ServerBuild(shooter) : local ? Builds.LocalBuild() : Builds.ClientBuild(Builds.NameOf(shooter));
            float spread = Complements.SpreadFactor(build, Paths.Values);
            // Built from the real arrow alone (its pose travels in the shot message), so every machine aims the same way.
            ReleaseLayout layout = new ReleaseLayout(arrow, nockBack.Value, spread);
            float speed = velocity.magnitude;
            int ghosts = Math.Min(r.Plan.Ghosts, Math.Min(VolleyMath.Total, held.Length));
            // A Line's needles are lines, cast and drawn by Quickdraw4; only a flying snap launches them here.
            int needles = r.Ultimate == UltimateUse.None ? Math.Min(r.Plan.Echoes, ReleasePlan.MaxNeedles) : 0;
            float needleShare = Paths.Values.NeedleShare;
            int pellets = Math.Min(r.Plan.Pellets, BurstPattern.Count);
            bool chargedShot = r.Kind == ShotKind.Charged || r.ChargedTier > 0;
            // The gold look (shafts, pellets, landing sparks): a charged shot, or a Multi main's volley held to stage 1 or more (G3).
            // Looks only: damage follows the charge tier and the hold stage.
            bool charged = chargedShot || GoldGhosts(r.HoldStage);
            // G4: a Multi main's held volley carries its stage ring by ring (GoldStages.Level: the inner ring from stage 1, the outer
            // from stage 2, everything brighter at stage 3; fewer orbs: the last stage owned is all gold). 0 orbs here = the one
            // switch above for every arrow (a charged shot, every other build, [GoldHold] off). Looks only.
            int holdOrbs = !chargedShot && GoldHold.On ? VolleyHold.Orbs(build) : 0;
            int holdRings = GoldStages.Rings(ghosts);
            // Ghosts and pellets of an uncharged shot deal their plain share (x1.00); a charged shot's take its Charge tier.
            float spectral = r.ChargedTier > 0 ? Tree.ChargeMultiplier(r.ChargedTier) : 1f;
            // A Charge main's charged shot: the plan's ghosts are its heavy arrows (Complements.HeavyArrows, at most six), each a
            // flat HeavyShare of the arrow's power (never the charge multiplier) and the charged carry's per-share ghost stagger.
            bool heavy = chargedShot && r.Ultimate == UltimateUse.None && build.Main == MainPath.Charge;
            float heavyShare = Paths.Values.HeavyShare;
            if (heavy) ghosts = Math.Min(ghosts, ReleasePlan.MaxHeavy);
            // The heavy look (HeavyLook), looks only (the count and the flight's share and charge follow `heavy`): the shooter's
            // from the build as above; a spectator's from the relay (HeavyLookRule: a Charged release of 1..6 ghosts and no
            // pellets) unless the build it holds for the shooter is known and not a Charge main (a None main's charged volley).
            bool heavyLook = visuals && (local || server
                ? heavy && r.Kind == ShotKind.Charged
                : HeavyLookRule.Spectator(r.Kind, r.Ultimate, ghosts, pellets, Builds.ClientBuildKnown(Builds.NameOf(shooter)), build.Main));

            Transform? shooterRoot = pc != null ? pc.transform : null;
            Transform? bowRoot = bow.Bow != null ? bow.Bow.transform : null;
            uint release = arrowEntity.Identifier;
            float now = Time.time;
            float brightness = server || local ? 1f : OtherArcherBrightness;

            // Ricochet (batch 3): this release's record and target list on every machine; its ghosts and pellets may skip once.
            // Never a heavy arrow or a needle, and nothing at all for a build without the orb.
            // Shatter (batch 4b, a Charge side orb): the same record says whether its real arrow shatters: a Charge main's charged
            // release that is not a Holy Arrow beam.
            Targets.Release rec = Targets.Open(release, build, shooter, local, charged, Claims.TargetsOf(release, local),
                                               r.ChargedTier > 0 && r.Ultimate != UltimateUse.Beam, r.Ultimate == UltimateUse.Beam);
            if (visuals) rec.Bow = bow; // looks only: the shooter's bow hand, for Shatter's thump
            // Ricochet's ground skip: on the Multi main its ghosts and pellets (never heavy arrows or needles); off it (fix batch D) the
            // build's own spectral arrows, a Quick main's needles and a Charge main's heavy arrows (RicochetRule.CanBounce)
            bool ricochet = rec.Ricochet && r.Ultimate == UltimateUse.None && (!rec.RicochetOnPath || (!heavy && !heavyLook));
            // the side orbs' try-out practice: each orb's own action for this main path (SideOrbPractice, fix batch D). On their own
            // line as before: Ricochet a full volley; Shatter the charged release itself (fix 5, b4b-review I-1: the try-out has no
            // target a charged arrow can damage). Practise checks the held orb and marks once.
            if (server && rec.Ricochet && SideOrbPractice.Done(Orb.Ricochet, build.Main, r.Kind, r.Ultimate, ghosts, rec.ShatterReal && rec.Shatter == ShatterHalf.Charge))
                Shrine.Practise(shooter, Orb.Ricochet, rec.RicochetOnPath ? "releasing a full volley" : "its main path's own shot");
            if (server && rec.Shatter != ShatterHalf.None && SideOrbPractice.Done(Orb.Shatter, build.Main, r.Kind, r.Ultimate, ghosts, rec.ShatterReal && rec.Shatter == ShatterHalf.Charge))
                Shrine.Practise(shooter, Orb.Shatter, rec.Shatter == ShatterHalf.Charge ? "a charged shot while it was held" : "its main path's own shot");

            // fix batch D (the user: "Have it give it a damage and stagger buff"): a Multi main's Charge Shot orbs raise its ghosts' and
            // pellets' damage; G3: earned by holding, +GhostDamagePerOrb per hold stage reached (Complements.GhostDamageAt, the release's
            // HoldStage), 1 at stage 0 and for every other main path. The real arrow is not changed, and Ricochet's jumps follow (a share
            // of each arrow's own damage).
            float ghostDamage = Complements.GhostDamageAt(build, Paths.Values, r.HoldStage);

            // H1, Seeking Volley: a release whose claim is steered (the shooter's own, the server's kept one, a spectator's relay) homes:
            // every ghost and pellet flies to a lock (dealt round the locks in arrow order), CoreArrows core arrows take the real
            // arrow's place on its own line to the main lock, and the real arrow does not fly (Seek.HoldReal). Anything else: as before.
            List<uint> locks = Claims.LocksOf(release, local);
            bool homing = locks.Count > 0 && LockRule.Steerable(r) && !heavy && !heavyLook;
            HomingValues hv = Seek.Values;
            Seek.Flight? flight = null;
            int cores = 0;
            if (homing) {
                try { flight = Seek.Open(release, locks, shooter, local, arrow); cores = HomingValues.CoreCount(hv.CoreArrows); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Seek: opening arrow {release}'s homing failed; an ordinary volley: {e}"); homing = false; flight = null; }
            }
            float seeker = homing ? Math.Max(0f, Paths.OrbNumbers.SeekerShare) : 1f;
            // the core's share: the real arrow's damage at the multiplier this release gives it (x1 for a multi-arrow main's volley)
            float realMultiplier = homing ? Resolver.RealArrowMultiplier(r, build, 1f, Tree.QuickMultiplier, Tree.ChargeMultiplier, Tree.ChargeDamageAt) ?? 1f : 1f;
            float coreShare = hv.CoreShare * realMultiplier;
            // the try-out: a multi-arrow main's volley released while Seeking Volley is held practises it (PracticeRule: OwnAction)
            if (server && ghosts > 0 && build.Main == MainPath.Multi && Tree.OrbOn(Orb.SeekingVolley))
                Shrine.Practise(shooter, Orb.SeekingVolley, "releasing a volley while it was held");

            int total = ghosts + needles + pellets + cores;
            if (total <= 0) return;
            if (server) Hits.OpenRelease(release, total, shooter, arrow.ImpactTool);
            ImpactTool? tool = server ? arrow.ImpactTool : null;
            if (homing && flight != null) {
                flight.Cores = cores;
                flight.CoreMultiplier = cores * coreShare;
            }

            // Heavy arrows have their own layout (HeavyPattern: pairs close beside the real arrow, chosen by the count, as the
            // held look's; [HeavyArrows] Radius and SpreadDegrees, both shared), never the volley's ghost slots. A spectator that draws the heavy look uses it too,
            // so what it sees flies where the server's heavy arrows fly.
            bool heavyLayout = heavy || heavyLook;
            int heavyLooks = 0;
            for (int i = 0; i < ghosts; i++) {
                Vector3 tip, dir;
                if (heavyLayout) layout.HeavyTip(i, ghosts, out tip, out dir);
                else layout.Tip(i, out tip, out dir);
                Ghost? ghost = null;
                // this ghost's gold: its ring's level at the stage reached (G4), else the release's one switch
                int level = holdOrbs > 0 ? GoldStages.Level(GoldStages.GhostRing(i), r.HoldStage, holdOrbs, holdRings) : charged ? 1 : 0;
                if (visuals) {
                    ghost = held[i] != null ? held[i] : Ghost.Create(arrow, i, rampTestOk: !heavyLook);
                    // a heavy arrow whose look cannot be built keeps the gold ghost look
                    if (heavyLook && ghost!.MakeHeavy()) heavyLooks++;
                    if (holdOrbs > 0 && !heavyLook) ghost!.Style(GoldHold.Flight(level), brightness);
                    else ghost!.Style(charged || heavyLook, brightness);
                    ghost.Homing = homing; // H1: green on a locked release (GhostColour), in place of its own colour and the hold stages' gold
                    if (homing) SeekLook.Dress(ghost, false, brightness); // H2: bigger, with a longer tapering trail (after its style, before it flies)
                    ghost.Fly(tip, dir, layout.Length);
                }
                held[i] = null;
                SpectralKind kind = heavyLayout || i < VolleyMath.Inner ? SpectralKind.Inner : SpectralKind.Outer;
                VolleySim.Shot shot = VolleySim.Make(tip, dir, layout.Forward, speed, now, kind, i,
                    (heavy ? heavyShare : VolleyMath.ExtraShare(i, innerShare.Value, outerShare.Value) * ghostDamage) * seeker, heavy ? 1f : spectral, level >= 1, release, shooter, tool, shooterRoot, bowRoot, arrow.transform, ghost, null);
                shot.bounceable = ricochet && RicochetRule.CanBounce(kind, false, heavyLayout, rec.RicochetOnPath);
                if (homing && flight != null)
                    VolleySim.Home(ref shot, flight, dir, layout.Side(VolleyMath.Azimuth(i)), HomingAssign.Lock(HomingAssign.Order(kind, i, ghosts), locks.Count),
                                   HomingAssign.Ring(kind, false), false, speed, lifeSeconds.Value, hv);
                VolleySim.Add(shot, local);
            }
            if (visuals && ghosts > 0) {
                try { VolleyFx.Beats.Release(release, layout.RealTip, ghosts); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Volley FX: release flare failed: {e}"); }
                if (homing) SeekLook.Released(release, layout.RealTip, brightness, pellets > 0); // H2: the flare of a locked volley is green
                if (heavyLook)
                    { if (Logs.Debug) MelonLogger.Msg($"[Archery] Heavy arrows: arrow {release} {ghosts} flying ({(local ? "own" : "seen")})" +
                                    (heavyLooks < ghosts ? $"; {ghosts - heavyLooks} with the gold ghost look" : "")); }
            }
            // Needles ride a Quick main's snaps (the plan's echo slot and the echo flight): needle i leaves the real tip at
            // NeedleDegrees off the arrow's line, turned i x 360 / n round it, uncharged, at NeedleShare. Their count comes from
            // the resolution alone (a spectator's from the relayed Snap's stage), never from the build held for the shooter.
            // Look: NeedleLook (a gold streak); the old echo look (an inner ghost) only for a needle whose look cannot be built.
            int needleLooks = 0;
            for (int i = 0; i < needles; i++) {
                Vector3 tip = layout.RealTip;
                Vector3 dir = layout.PelletDirection(NeedleDegrees, NeedlePattern.Azimuth(i, needles));
                Ghost? ghost = null;
                NeedleLook? look = null;
                if (visuals) {
                    look = NeedleLook.Create(tip, dir, brightness);
                    if (look != null) needleLooks++;
                    else {
                        ghost = Ghost.Create(arrow, 0, rampTestOk: false); // the old echo look: an inner ghost
                        ghost.Style(false, brightness);
                        ghost.Fly(tip, dir, layout.Length);
                    }
                }
                VolleySim.Shot shot = VolleySim.Make(tip, dir, layout.Forward, speed, now, SpectralKind.Echo, i,
                    needleShare, 1f, false, release, shooter, tool, shooterRoot, bowRoot, arrow.transform, ghost, null);
                shot.needle = look;
                shot.bounceable = ricochet && RicochetRule.CanBounce(SpectralKind.Echo, false, false, rec.RicochetOnPath); // fix batch D: off the Multi main
                VolleySim.Add(shot, local);
            }
            if (visuals && needles > 0) {
                try { NeedleLook.Released(release, needles, needleLooks, bowRoot != null ? bowRoot.position : layout.RealTip, local); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Needles: release sounds failed: {e}"); }
            }
            // Burst's pellets (fix E): pellet p starts on its ghosts' ring, midway between ghost p and the next (where its held
            // arrow showed), and flies on the fixed pattern's direction from there (BurstPattern; ReleaseLayout.PelletTip). The
            // same on every machine. (Until fix E it started at inner ghost p % 5's tip, on that ghost's own turn round the arrow.)
            for (int p = 0; p < pellets; p++) {
                layout.PelletTip(p, out Vector3 tip, out Vector3 dir);
                // Looks: a pellet has the colour of the release's ghosts (`charged`, the ghosts' own switch above): streak, puff,
                // landing sparks and shed alike; G4: of the ghosts of its own ring at the hold stage reached (`level`).
                // Looks only: a pellet still carries no stagger, and its damage takes `spectral`.
                int level = holdOrbs > 0 ? GoldStages.Level(GoldStages.PelletRing(p), r.HoldStage, holdOrbs, holdRings) : charged ? 1 : 0;
                PelletStreak? streak = visuals ? PelletStreak.Create(homing ? GhostColour(1f, true) : holdOrbs > 0 ? GoldHold.FlightColour(level) : GhostColour(charged), brightness) : null;
                if (homing) SeekLook.Dress(streak); // H2: a homing pellet's streak is wider and longer, and as bright all the way
                // looks only: the streak starts where this pellet's held arrow was on the bow and joins the path below
                if (streak != null) {
                    try { Burst.Held.Hand(bow, p, streak, tip, dir); }
                    catch (Exception e) { FxError("pellet hand-over", e); }
                }
                // the gold flag here is the landing sparks' colour only (VolleySim.Shot.charged)
                VolleySim.Shot pellet = VolleySim.Make(tip, dir, layout.Forward, speed, now, SpectralKind.Pellet, p,
                    Burst.PelletShare * ghostDamage * seeker, spectral, level >= 1, release, shooter, tool, shooterRoot, bowRoot, arrow.transform, null, streak);
                pellet.maxRange = HomingAssign.PelletRange(homing, Burst.Range, hv); // H1: with a lock they follow it out to the lock's range
                pellet.bounceable = ricochet && RicochetRule.CanBounce(SpectralKind.Pellet, false, false, rec.RicochetOnPath);
                if (homing && flight != null) {
                    BurstPattern.Pellet(p, 0f, 0f, out _, out float pelletTurn);
                    VolleySim.Home(ref pellet, flight, dir, layout.Side(pelletTurn), HomingAssign.Lock(HomingAssign.Order(SpectralKind.Pellet, p, ghosts), locks.Count),
                                   HomingAssign.Ring(SpectralKind.Pellet, false), false, speed, lifeSeconds.Value, hv);
                }
                VolleySim.Add(pellet, local);
            }
            // H1: the core cluster, the real arrow's stand-in: CoreArrows spectral arrows in a tight ring on the arrow's own line, each
            // CoreShare of the real arrow's damage, all to the main lock, flying straight in; no jump of their own (their landing is the
            // real arrow's, Seek.CoreEnded) and no ground skip
            if (homing && flight != null && cores > 0) {
                for (int c = 0; c < cores; c++) {
                    double turn = 90.0 + c * 360.0 / cores;
                    Vector3 side = layout.Side(turn);
                    Vector3 tip = layout.RealTip + side * CoreRadius;
                    Vector3 dir = layout.Forward;
                    Ghost? ghost = null;
                    if (visuals) {
                        ghost = Ghost.Create(arrow, 0, rampTestOk: false);
                        ghost.Style(false, brightness);
                        ghost.Homing = true;
                        SeekLook.Dress(ghost, true, brightness); // H2: the core arrows a little larger still and the brightest
                        ghost.Fly(tip, dir, layout.Length);
                    }
                    VolleySim.Shot core = VolleySim.Make(tip, dir, layout.Forward, speed, now, SpectralKind.Inner, c, coreShare, 1f, false, release, shooter, tool,
                                                         shooterRoot, bowRoot, arrow.transform, ghost, null);
                    VolleySim.Home(ref core, flight, dir, side, 0, HomingAssign.Ring(SpectralKind.Inner, true), true, speed, lifeSeconds.Value, hv);
                    VolleySim.Add(core, local);
                }
            }
            // H4 (H23-review Fable I-2): the real arrow is held only now, after every ghost, pellet and core arrow above copied its
            // head (Ghost.Create reads the arrow's drawn renderers, which the hold hides on clients: the core cluster had no heads)
            if (homing && flight != null) {
                try { Seek.HoldReal(arrow, release); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Seek: holding arrow {release}'s real arrow failed: {e}"); }
            }
            if (homing && Logs.Debug)
                MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Seek: arrow {0} homes on {1} lock(s) [{2}] ({3}): {4} ghosts, {5} pellets x{6:0.##} (seekers), {7} core arrows x{8:0.###} each; the real arrow is held",
                    release, locks.Count, string.Join(",", locks.ConvertAll(l => l.ToString(CultureInfo.InvariantCulture)).ToArray()), server ? "server" : local ? "own" : "seen",
                    ghosts, pellets, seeker, cores, coreShare));
            if (visuals && pellets > 0) {
                try {
                    // each inner ghost sheds sparks from where it starts at release
                    var tips = new Vector3[VolleyMath.Inner];
                    var dirs = new Vector3[VolleyMath.Inner];
                    for (int i = 0; i < VolleyMath.Inner; i++) layout.Tip(i, out tips[i], out dirs[i]);
                    // H2: a locked release sheds in the homing green (sparks and light), whatever its hold stage
                    Burst.Shed(release, tips, dirs, charged, brightness, layout.RealTip, bowRoot != null ? bowRoot.position : layout.RealTip, pellets, homing ? SeekLook.Colour : (Color?)null);
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Burst: shed failed: {e}"); }
            }
            if (heavy) {
                if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture,
                    "[Archery] Volley: {0} arrow {1}: {2} heavy arrows x{3:0.00} (flat), {4} needles, {5} pellets at {6:0.0} m/s, charge tier {7} (main path Charge, multi-arrow {8}); heavy layout radius {9:0.000} m, spread {10:0.0#} degrees",
                    server ? "fired" : local ? "launched (own)" : "launched (seen)", release, ghosts, heavyShare, needles, pellets, speed, r.ChargedTier, build.Volley,
                    HeavyLook.Radius, HeavyLook.SpreadDegrees));
                return;
            }
            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture,
                "[Archery] Volley: {0} arrow {1}: {2} ghosts (inner x{3:0.00}, outer x{4:0.00}), {5} needles x{6:0.00}, {7} pellets x{8:0.00} (spectral x{9:0.00}) at {10:0.0} m/s, spread x{11:0.00}{12}{13}",
                server ? "fired" : local ? "launched (own)" : "launched (seen)", release, ghosts, innerShare.Value * spectral * ghostDamage, outerShare.Value * spectral * ghostDamage,
                needles, needleShare, pellets, Burst.PelletShare * spectral * ghostDamage, spectral, speed, spread,
                chargedShot ? ", charged" : charged ? (holdOrbs > 0 ? $", gold: hold stage {r.HoldStage} of {holdOrbs}" : ", gold ghosts") : "",
                ghostDamage > 1.0001f ? string.Format(CultureInfo.InvariantCulture, ", ghost damage x{0:0.00} (Charge Shot orbs)", ghostDamage) : ""));
        }

        // Where each spectral arrow starts and which way it points at release, from the real arrow alone (spec Part 12
        // item 4): the nock point is NockBack behind the tip along the arrow, "up" is the arrow's own up. Ghost i's tip
        // sits round the real tip (VolleyMath.TipOffset) and aims away from the nock point.
        // `spread`: Complements.SpreadFactor (a Multi main's Quick orbs narrow the fan). It scales every ghost's tip offset, so
        // the angle off the arrow's line scales with it (tan, near-linear at these few degrees).
        readonly struct ReleaseLayout {
            readonly Vector3 tip, nock, right, up;
            readonly float spread;
            public readonly Vector3 Forward;
            public readonly float Length;

            public ReleaseLayout(Arrow arrow, float nockBack, float spread) {
                this.spread = spread;
                Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
                Transform at = arrow.transform;
                tip = tipT.position;
                Forward = at.forward;
                Vector3 u = at.up - Forward * Vector3.Dot(at.up, Forward);
                if (u.sqrMagnitude < 1e-6f) u = Vector3.up - Forward * Vector3.Dot(Vector3.up, Forward);
                if (u.sqrMagnitude < 1e-6f) u = Vector3.forward - Forward * Vector3.Dot(Vector3.forward, Forward);
                up = u.normalized;
                right = Vector3.Cross(up, Forward);
                Length = Mathf.Max(MinNockBack, nockBack);
                nock = tip - Forward * Length;
            }

            public Vector3 RealTip => tip;

            /// <summary>H1: the way round the arrow at <paramref name="azimuthDegrees"/> (0 = right, 90 = up), across the aim: the side
            /// a homing arrow on that turn comes in from.</summary>
            public Vector3 Side(double azimuthDegrees) {
                float phi = (float)azimuthDegrees * Mathf.Deg2Rad;
                return Mathf.Cos(phi) * right + Mathf.Sin(phi) * up;
            }

            public void Tip(int index, out Vector3 tipPos, out Vector3 direction) {
                VolleyMath.TipOffset(index, out float x, out float y, innerRadius.Value * spread, outerRadius.Value * spread);
                tipPos = tip + right * x + up * y;
                direction = (tipPos - nock).normalized;
            }

            /// <summary>Heavy arrow <paramref name="index"/> of <paramref name="count"/> (HeavyPattern: the layout follows the
            /// count): its tip [HeavyArrows] Radius from the real tip, leaning SpreadDegrees off the arrow's line, straight away
            /// from it. The same on every machine.</summary>
            public void HeavyTip(int index, int count, out Vector3 tipPos, out Vector3 direction) {
                HeavyPattern.TipOffset(index, count, HeavyLook.Radius, out float x, out float y);
                tipPos = tip + right * x + up * y;
                direction = PelletDirection(HeavyLook.SpreadDegrees, HeavyPattern.Azimuth(index, count));
            }

            /// <summary>cos(angle) forward + sin(angle) (cos(azimuth) right + sin(azimuth) up), both in degrees.</summary>
            public Vector3 PelletDirection(float angleDegrees, float azimuthDegrees) {
                float a = angleDegrees * Mathf.Deg2Rad, phi = azimuthDegrees * Mathf.Deg2Rad;
                return (Mathf.Cos(a) * Forward + Mathf.Sin(a) * (Mathf.Cos(phi) * right + Mathf.Sin(phi) * up)).normalized;
            }

            /// <summary>Burst's pellet <paramref name="index"/> (fix E): it starts on its ghosts' own ring (the same radius,
            /// narrowed with the fan), midway between two ghosts (BurstPattern.TipOffset), and leaves the arrow's line by its
            /// own angle ([Burst] InnerDegrees / OuterDegrees), straight away from it. The same on every machine.</summary>
            public void PelletTip(int index, out Vector3 tipPos, out Vector3 direction) {
                BurstPattern.TipOffset(index, innerRadius.Value * spread, outerRadius.Value * spread, out float x, out float y);
                tipPos = tip + right * x + up * y;
                BurstPattern.Pellet(index, Burst.InnerDegrees, Burst.OuterDegrees, out float angle, out float azimuth);
                direction = PelletDirection(angle, azimuth);
            }
        }

        // While the bow is held: where each extra's tip sits and which way it points. All extras aim as if from one spot
        // at the string hand, with their tips spread round the real arrow's tip. Visual only (the release uses ReleaseLayout).
        readonly struct HeldLayout {
            readonly Vector3 tipPos, nock, right, up, forward;
            readonly float spread; // as ReleaseLayout's
            public readonly float length;

            public HeldLayout(BowString bow, Arrow arrow, Vector3 forward, float spread) {
                this.spread = spread;
                this.forward = forward;
                Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
                tipPos = tipT.position;
                nock = bow.transform.position; // the string's grab point: where the hand is
                Vector3 bowUp = bow.Bow != null ? bow.Bow.transform.up : Vector3.up;
                right = Vector3.Cross(bowUp, forward).normalized;
                if (right.sqrMagnitude < 0.5f) right = Vector3.Cross(Vector3.up, forward).normalized;
                up = Vector3.Cross(forward, right).normalized;
                length = Mathf.Max(0.3f, (tipPos - nock).magnitude);
            }

            public Vector3 RealTip => tipPos;

            public void Tip(int index, out Vector3 tip, out Vector3 direction) {
                VolleyMath.TipOffset(index, out float x, out float y, innerRadius.Value * spread, outerRadius.Value * spread);
                tip = tipPos + right * x + up * y;
                direction = (tip - nock).normalized;
            }

            /// <summary>A held heavy arrow (HeavyPattern), as ReleaseLayout.HeavyTip lays it out at release: close beside
            /// the real arrow and all but parallel to it, in the layout for <paramref name="count"/> arrows.</summary>
            public void HeavyTip(int index, int count, out Vector3 tip, out Vector3 direction) {
                HeavyPattern.TipOffset(index, count, HeavyLook.Radius, out float x, out float y);
                tip = tipPos + right * x + up * y;
                float a = HeavyLook.SpreadDegrees * Mathf.Deg2Rad, phi = HeavyPattern.Azimuth(index, count) * Mathf.Deg2Rad;
                Vector3 d = Mathf.Cos(a) * forward + Mathf.Sin(a) * (Mathf.Cos(phi) * right + Mathf.Sin(phi) * up);
                direction = d.sqrMagnitude > 1e-8f ? d.normalized : (tip - nock).normalized;
            }

            /// <summary>A held Burst pellet arrow (Burst.Held, looks only), as ReleaseLayout.PelletTip lays its pellet out at
            /// release: its tip on its ghosts' own ring, midway between two ghosts (BurstPattern.TipOffset with the ghosts'
            /// radii), pointing along its pellet's own line (its angle off the arrow, straight away from it). With the
            /// ghosts' tips (Tip) that is two evenly spaced rings round the real arrow: 10 inside, 20 outside.</summary>
            public void PelletTip(int index, out Vector3 tip, out Vector3 direction) {
                BurstPattern.TipOffset(index, innerRadius.Value * spread, outerRadius.Value * spread, out float x, out float y);
                tip = tipPos + right * x + up * y;
                BurstPattern.Pellet(index, Burst.InnerDegrees, Burst.OuterDegrees, out float angle, out float azimuth);
                float a = angle * Mathf.Deg2Rad, phi = azimuth * Mathf.Deg2Rad;
                Vector3 d = Mathf.Cos(a) * forward + Mathf.Sin(a) * (Mathf.Cos(phi) * right + Mathf.Sin(phi) * up);
                direction = d.sqrMagnitude > 1e-8f ? d.normalized : (tip - nock).normalized;
            }
        }

        // ---------------------------------------------------------------- virtual arrows

        // One flight model for every spectral arrow: the real arrow's arc (ArcModel) from its start point, direction and
        // the real arrow's launch speed. Hits are looked for once per physics tick along the arc; visuals are placed
        // every rendered frame from the same arc. On the server a hit deals damage; everywhere it ends the arrow.
        // Spectral arrows never test against each other.
        internal class VolleySim : MonoBehaviour {
            static bool shotFailedLogged;
            internal struct Shot {
                public Vector3 p0, flat;    // start point; horizontal unit direction of travel
                public float elevation, speed, born, lastCast, maxRange;
                public SpectralKind kind;
                public float share, charge; // the arrow's damage share, and the charge multiplier of the shot
                public bool charged;        // the shot is charged (gold landing sparks)
                public bool own;            // clients: the local player's own release, which always looks for hits (no cast range)
                public int index;
                public uint release;        // the real arrow's id: one release, one experience payment
                public Player? shooter;
                public Ghost? ghost;
                public PelletStreak? streak;
                public NeedleLook? needle;  // a needle's streak (Echo kind); null for the others, or when it fell back to a ghost
                public ImpactTool? tool;
                public Transform? ignoreA, ignoreB, ignoreC;
                public bool bounceable;     // Ricochet: this ghost or pellet may bounce once (Launch); false for everyone without the orb
                public bool bounced;        // it has skipped: p0 is the skip point and born the skip time
                public float lifeUsed;      // seconds of its life spent before the skip (its life counts from the release)
                // H1, a homing arrow (Seeking Volley): stepped every physics tick from its own state (HomingFlight), not the arc
                public bool homing;         // it flies the homing flight (false again after a Ricochet skip once it has let go)
                public bool core;           // one of the core cluster (the real arrow's stand-in): no jump of its own, its end places the real arrow
                public HomingArrow h;       // its state
                public int lockIndex;       // which of the release's locks it flies to
                public Seek.Flight? flight; // the release's homing record (the locks' middles, refreshed once per tick)
                public Vector3 castFrom;    // where its last cast ended
                public float lastStepAt;    // when it was last stepped (looks extrapolate from there)
                public float life;          // its own life (0 = [Volley] LifeSeconds)
                // H2, looks only: it flies (or flew) in a locked release, so everything it does is green (it stays so after it lets
                // go or skips), and its last steps, newest first, for the smooth curve it and its trail are drawn on (TrailCurve)
                public bool green;
                public HVec[]? path;
                public int pathCount;
            }

            /// <summary>H1: makes <paramref name="s"/> a homing arrow toward lock <paramref name="lockIndex"/> of <paramref name="flight"/>, leaving
            /// along <paramref name="dir"/> from its own side <paramref name="side"/> (the same on every machine from the release pose). A lock this
            /// machine cannot see gives it a target straight ahead at the lock range, and it lets go at once (it flies straight on).</summary>
            internal static void Home(ref Shot s, Seek.Flight flight, Vector3 dir, Vector3 side, int lockIndex, int ring, bool core, float speed, float baseLife, HomingValues v) {
                Vector3 target = Seek.LaunchTarget(flight, lockIndex, s.p0 + dir.normalized * Mathf.Max(1f, v.LockRangeMetres));
                s.homing = true;
                s.core = core;
                s.lockIndex = lockIndex;
                s.flight = flight;
                s.h = HomingFlight.Launch(Seek.H(s.p0), Seek.H(dir), Seek.H(side), Seek.H(target), speed, ring, core, baseLife, v, Seek.RoomOf(flight, lockIndex), lockIndex);
                // H5: the ghosts (and with ChainLocks all the pellets) chain through the release's locks; never a core arrow
                s.h.Chain = HomingChain.Chains(v.ChainLocks, s.kind, core);
                if (s.h.Chain) Seek.ChainLaunched(flight);
                s.life = (float)s.h.Life;
                s.castFrom = s.p0;
                s.lastStepAt = s.born;
                if (core) s.bounceable = false;
                s.green = true;
                if (!ApplicationManager.IsHeadless) {
                    // H2: the start point twice: it is drawn at the bow until its first step, then leaves along the curve
                    s.path = new HVec[TrailCurve.History];
                    s.path[0] = s.path[1] = s.h.Pos;
                    s.pathCount = 2;
                }
            }

            static Vector3 V(HVec h) => new Vector3((float)h.X, (float)h.Y, (float)h.Z);

            const int CastRangeLogLimit = 4;

            static VolleySim? instance;
            static readonly Vector3[] trailPoints = new Vector3[SeekLook.MaxTrailPoints]; // H2: every homing ghost's trail, in turn (main thread only)
            static int tick;
            static string castFrom = "";
            static int castFromLogs;
            readonly List<Shot> live = new List<Shot>();
            readonly System.Diagnostics.Stopwatch tickTimer = new System.Diagnostics.Stopwatch();

            static VolleySim Instance() {
                if (instance == null) {
                    var go = new GameObject("ArcheryVolleySim");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<VolleySim>();
                }
                return instance;
            }

            /// <summary><paramref name="own"/>: the local player shot it, so a client casts it at any range.</summary>
            internal static void Add(Shot shot, bool own) { shot.own = own; Instance().live.Add(shot); }

            /// <summary>A spectral arrow starting at <paramref name="start"/> on <paramref name="dir"/> at the real arrow's speed.</summary>
            internal static Shot Make(Vector3 start, Vector3 dir, Vector3 forward, float speed, float born, SpectralKind kind, int index,
                                      float share, float charge, bool charged, uint release, Player? shooter, ImpactTool? tool,
                                      Transform? ignoreA, Transform? ignoreB, Transform? ignoreC, Ghost? ghost, PelletStreak? streak) {
                Aim(dir, forward, out float elevation, out Vector3 flat);
                return new Shot {
                    p0 = start, flat = flat, elevation = elevation, speed = speed, born = born, lastCast = 0f, maxRange = float.MaxValue,
                    kind = kind, index = index, share = share, charge = charge, charged = charged, release = release, shooter = shooter,
                    tool = tool, ignoreA = ignoreA, ignoreB = ignoreB, ignoreC = ignoreC, ghost = ghost, streak = streak,
                };
            }

            /// <summary>Elevation (radians, from asin of the direction's height) and the horizontal unit direction; a vertical
            /// shot takes the arrow's horizontal forward (or world forward when that is vertical too).</summary>
            static void Aim(Vector3 dir, Vector3 forward, out float elevation, out Vector3 flat) {
                Vector3 d = dir.normalized;
                elevation = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f));
                flat = new Vector3(d.x, 0f, d.z);
                if (flat.sqrMagnitude < 1e-8f) flat = new Vector3(forward.x, 0f, forward.z);
                if (flat.sqrMagnitude < 1e-8f) flat = Vector3.forward;
                flat.Normalize();
            }

            static float Gravity => -Physics.gravity.y;

            static Vector3 Point(Vector3 p0, Vector3 flat, float speed, float elevation, float t) {
                ArcModel.Point(speed, elevation, t, Gravity, out double along, out double up);
                return p0 + flat * (float)along + Vector3.up * (float)up;
            }

            static Vector3 Direction(Vector3 flat, float speed, float elevation, float t) {
                ArcModel.Velocity(speed, elevation, t, Gravity, out double along, out double up);
                return flat * (float)along + Vector3.up * (float)up;
            }

            static Vector3 Position(in Shot s, float t) => Point(s.p0, s.flat, s.speed, s.elevation, t);

            /// <summary>Clients: where cast range is measured from (the main camera, else the local player's head, else nowhere: cast everything).</summary>
            static Transform? Eye() {
                Camera cam = Camera.main;
                Transform? eye = null;
                string from;
                if (cam != null) { eye = cam.transform; from = cam.name; }
                else {
                    Player? me = Player.Current as Player;
                    PlayerController pc = me != null ? me.PlayerController : null!;
                    Transform head = pc != null ? pc.Head : null!;
                    if (head != null) { eye = head; from = "head"; }
                    else from = "nothing";
                }
                if (from != castFrom && castFromLogs < CastRangeLogLimit) {
                    castFromLogs++;
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Volley: client cast range measured from {from}");
                }
                castFrom = from;
                return eye;
            }

            static readonly int TFixedUpdate = Timing.Section("FixedUpdate Volley.VolleySim");
            static readonly int THomingStep = Timing.Section("FixedUpdate Volley.VolleySim / homing step (H1, per homing arrow)");
            static readonly int THomingDraw = Timing.Section("LateUpdate Volley.VolleySim / homing curve and trail (H2, per homing arrow)");
            static readonly int TChainStrike = Timing.Section("FixedUpdate Volley.VolleySim / chain strike (H5, per lock struck by a chaining arrow)");
            void FixedUpdate() {
                using var _t = Timing.Begin(TFixedUpdate);
                tick++;
                if (live.Count == 0) return;

                tickTimer.Reset(); tickTimer.Start();
                float now = Time.time;
                bool server = NetworkSceneManager.IsServer, visuals = !ApplicationManager.IsHeadless;
                Transform? eye = server ? null : Eye();
                Vector3 eyePos = eye != null ? eye.position : Vector3.zero;
                float range = clientCastRange.Value, range2 = range * range;
                int count = live.Count, cap = liveCap.Value;
                float life = lifeSeconds.Value;
                HomingValues hv = Seek.Values;
                float dt = Time.fixedDeltaTime;
                for (int i = live.Count - 1; i >= 0; i--) {
                    Shot s = live[i];
                    bool removed = false;
                    // H4 (H23-review minor 7): one shot that throws is dropped and logged once; it never stops the shots before it or
                    // the server's flush of everyone's spectral hits below
                    try {
                    float age = Mathf.Max(0f, now - s.born);
                    Vector3 pos, from;
                    if (s.homing) {
                        // H1: one step of the homing flight toward its lock as this machine sees it (the lock's middle is refreshed
                        // once per release per tick); it casts from where its last cast ended
                        using (Timing.Begin(THomingStep)) {
                            // H3: every lock of the release (its arrows turn to another living lock when theirs dies), led by its velocity
                            LetGo before = s.h.Let;
                            if (s.flight != null) HomingFlight.Step(ref s.h, Seek.TargetsNow(s.flight, tick), dt, hv);
                            else HomingFlight.Step(ref s.h, s.h.Pos + s.h.Dir, false, dt, hv);
                            s.lockIndex = s.h.Lock;
                            if (s.h.Chain) s.life = (float)s.h.Life; // H5 review I-1: a leg the step started (its lock died) has its own life
                            s.lastStepAt = now;
                            pos = V(s.h.Pos);
                            if (s.path != null) s.pathCount = TrailCurve.Push(s.path, s.pathCount, s.h.Pos); // H2, looks: this step joins the ones it is drawn through
                            if (before == LetGo.None && s.h.Let != LetGo.None) Seek.LetGoSeen(s.flight, s.kind, s.core, s.index, s.h.Let, pos);
                        }
                        from = s.castFrom;
                    }
                    else {
                        pos = Position(s, age);
                        from = Position(s, s.lastCast);
                    }
                    float travelled = (pos - s.p0).magnitude;
                    bool ended = SpectralLife.Ended(s.kind, travelled, age + s.lifeUsed, s.maxRange, s.life > 0f ? s.life : life)
                                 // H3: a let-go arrow ends LetGoSeconds after it let go; on a client whose server refused the lock, at once
                                 || (s.homing && (HomingFlight.Done(s.h, hv) || (s.flight != null && s.flight.Refused)));
                    bool cast, bouncedNow = false, chainedNow = false;
                    // Another archer's arrow too far away to matter: no hits. The local player's own always look (at most 30 per release).
                    if (eye != null && !s.own && (pos - eyePos).sqrMagnitude > range2) { cast = false; s.lastCast = age; s.castFrom = pos; }
                    else cast = ended || !server || SpectralBudget.CastThisTick(count, cap, s.index, tick);
                    bool hitSomething = false;
                    RaycastHit landHit = default;
                    if (cast && (s.homing ? (pos - from).sqrMagnitude > 1e-10f : age > s.lastCast)) {
                        Vector3 step = pos - from;
                        // H5: a chaining arrow's cast also passes through every lock it has already struck (it starts inside the last one)
                        bool landed = s.homing ? Hits.CastPastShells(from, step, step.magnitude, out RaycastHit hit, s.ignoreA, s.ignoreB, s.ignoreC, // H4 (Opus minor 1)
                                                                     s.flight != null && s.h.Chain ? Seek.StruckRoots(s.flight, s.h.Struck) : null)
                                               : Hits.Cast(from, step, step.magnitude, out hit, s.ignoreA, s.ignoreB, s.ignoreC);
                        // Ricochet: a ghost or pellet off ground, rock or wood skips once and flies on along its own direction,
                        // slower, on the same arc model from the skip point (no extra cast this tick). Too steep: it lands as before.
                        // H1: a homing arrow never skips while it homes (once it has let go it may, and then flies the arc).
                        if (landed && s.bounceable && !s.bounced && (!s.homing || s.h.Let != LetGo.None)
                            && Ricochet.TrySkip(s.release, s.index, hit, step, out Vector3 bStart, out Vector3 bDir)) {
                            Vector3 inDir = step;
                            if (s.homing) { s.homing = false; s.speed = (float)s.h.Speed; }
                            Aim(bDir, bDir, out s.elevation, out s.flat);
                            s.speed = RicochetRule.SkipSpeed(s.speed, Ricochet.SpeedKeep);
                            s.share *= Ricochet.SkipShare;
                            s.lifeUsed += age; // its life goes on counting from the release
                            s.p0 = bStart; s.born = now; s.lastCast = 0f; s.bounced = true;
                            bouncedNow = true; landed = false;
                            if (visuals) Ricochet.SkipLook(s.release, hit.point, inDir, bDir);
                        }
                        if (landed) {
                            hitSomething = true;
                            landHit = hit;
                            // H5: a chaining arrow that still homes and lands on one of the release's locks (its own or another on the way)
                            int struckLock = s.homing && s.h.Chain && s.h.Let == LetGo.None && s.flight != null && !s.flight.Refused ? Seek.LockOf(s.flight, hit.collider) : -1;
                            // H1: a core arrow is no ghost (no gold stagger) and queues no jump of its own
                            if (server && s.tool != null)
                                Hits.Spectral(s.tool, hit, s.share, s.charge, s.release, s.shooter, !s.core && (s.kind == SpectralKind.Inner || s.kind == SpectralKind.Outer), !s.core,
                                              TelemetryRules.SourceOf(s.kind, s.core));
                            if (visuals) {
                                try {
                                    // a heavy arrow lands in the charged hit effect (gold sparks if that cannot be had)
                                    if (s.ghost != null && s.ghost.Heavy != null) HeavyLook.Land(s.release, hit.point, hit.normal);
                                    else VolleyFx.LandFx.Land(s.release, hit.point, s.charged, s.needle != null, true, s.green); // H2: a homing arrow's sparks and light are green
                                    // H2: the first arrows to land on a locked creature show its arrival burst (once per lock)
                                    if (s.homing && !s.core && s.flight != null && s.h.Let == LetGo.None) Seek.Arrived(s.flight, struckLock >= 0 ? struckLock : s.lockIndex, hit);
                                }
                                catch (Exception e) { MelonLogger.Error($"[Archery] Volley: landing effect failed: {e}"); }
                            }
                            if (visuals && !server && s.own) {
                                // Balance Ring: the local player's own spectral hit on a creature (look only)
                                try {
                                    ImpactDetector det = hit.collider.GetComponentInParent<ImpactDetector>();
                                    HealthObject h = det != null ? det.HealthTarget : null!;
                                    if (h != null && !(h is PlayerHealth)) Balance.Hit(h, Draws.ReleaseOf(s.release)?.ChargedTier ?? 0);
                                }
                                catch (Exception e) { MelonLogger.Error($"[Archery] Balance: spectral hit failed: {e.Message}"); }
                            }
                            if (visuals && !server && !s.core && Targets.Of(s.release)?.Ricochet == true) {
                                // Ricochet: the jump this client sees from a creature this release struck (looks only; a core arrow's is
                                // the real arrow's, drawn once from Seek.CoreEnded)
                                ImpactDetector jd = hit.collider.GetComponentInParent<ImpactDetector>();
                                HealthObject jh = jd != null ? jd.HealthTarget : null!;
                                if (jh != null) Ricochet.ClientStruck(s.release, jh, false, hit.point); // its arc starts where it hit
                            }
                            // H5: everything above is any landing's (the server's hit, which queues the creature's Ricochet jump, the
                            // landing sparks, the arrival burst once per lock, the client's jump arc). A chaining arrow that struck a lock
                            // then flies on from the hit point to the next lock it has not struck (HomingChain.Strike); with none left it
                            // ends here as before.
                            if (struckLock >= 0) {
                                using (Timing.Begin(TChainStrike, s.release)) {
                                    chainedNow = HomingChain.Strike(ref s.h, struckLock, Seek.H(hit.point), Seek.TargetsNow(s.flight!, tick), hv);
                                    Seek.ChainStruck(s.flight!, s.kind, s.index, struckLock, s.h.Hits, chainedNow ? s.h.Lock : -1);
                                    if (chainedNow) {
                                        s.lockIndex = s.h.Lock;
                                        s.life = (float)s.h.Life;
                                        if (s.path != null && s.pathCount > 0) s.path[0] = s.h.Pos; // looks: the curve runs through the strike
                                    }
                                }
                            }
                        }
                        if (!bouncedNow) { s.lastCast = age; s.castFrom = chainedNow ? V(s.h.Pos) : pos; }
                    }
                    // H5: a chaining arrow that flies on is not over this tick (its life was just set for its next leg)
                    if (chainedNow) { hitSomething = false; ended = false; }
                    if (!bouncedNow && (hitSomething || ended)) {
                        Vector3 endDir = s.homing ? V(s.h.Dir) * (float)s.h.Speed : Direction(s.flat, s.speed, s.elevation, age);
                        if (s.ghost != null) s.ghost.End(); // a heavy arrow's embers are let go to fade first
                        if (s.needle != null) s.needle.End();
                        // a pellet that reached its range puffs out; one that hit something ends in the landing sparks
                        if (s.streak != null) s.streak.End(!hitSomething && s.kind == SpectralKind.Pellet && travelled >= s.maxRange, pos, endDir);
                        // H1: the first core arrow to end places the real arrow and stands in for its first creature hit (once per release)
                        if (s.core) {
                            try { Seek.CoreEnded(s.release, hitSomething, landHit, hitSomething ? landHit.point : pos, endDir); }
                            catch (Exception e) { MelonLogger.Error($"[Archery] Seek: arrow {s.release}'s core landing failed: {e}"); }
                        }
                        live.RemoveAt(i);
                        removed = true;
                        if (server) Hits.ArrowDone(s.release);
                        if (s.h.Chain) Seek.ChainEnded(s.flight, s.h); // H5: the release's chain line once its last chaining arrow is over
                    }
                    else live[i] = s;
                    }
                    catch (Exception e) {
                        if (!removed && i < live.Count) live.RemoveAt(i);
                        try { if (s.ghost != null) s.ghost.End(); if (s.needle != null) s.needle.End(); if (s.streak != null) s.streak.End(false, s.p0, Vector3.forward); } catch { }
                        if (server && !removed) { try { Hits.ArrowDone(s.release); } catch { } }
                        if (!removed && s.h.Chain) { try { Seek.ChainEnded(s.flight, s.h); } catch { } }
                        if (!shotFailedLogged) { shotFailedLogged = true; MelonLogger.Error($"[Archery] Volley: arrow {s.release}'s spectral {s.kind} {s.index} failed and was dropped (said once a session): {e}"); }
                    }
                }
                if (server) {
                    // one game hit per target per release for this tick; then pay and log any release that just ended
                    try { Hits.FlushSpectral(); }
                    catch (Exception e) { MelonLogger.Error($"[Archery] Volley: flushing spectral hits failed: {e}"); }
                    tickTimer.Stop();
                    try { Hits.SimTick(tickTimer.Elapsed.TotalMilliseconds); }
                    catch (Exception e) { MelonLogger.Error($"[Archery] Volley: closing releases failed: {e}"); }
                }
            }

            // Visuals follow the arc from the time since launch every rendered frame, so they are smooth in the headset.
            static readonly int TLateUpdate = Timing.Section("LateUpdate Volley.VolleySim");
            void LateUpdate() {
                using var _t = Timing.Begin(TLateUpdate);
                try { VolleyFx.LandFx.Pump(); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Volley: landing light failed: {e}"); }
                if (live.Count == 0 || ApplicationManager.IsHeadless) return;
                float now = Time.time, fixedDt = Mathf.Max(1e-4f, Time.fixedDeltaTime);
                for (int i = 0; i < live.Count; i++) {
                    Shot s = live[i];
                    if (s.ghost == null && s.streak == null && s.needle == null) continue;
                    Vector3 pos, dir;
                    if (s.homing && s.path != null && SeekLook.Smooth) {
                        // H2: on the smooth curve through its last steps, one step behind the flight (TrailCurve: no corners in a hard
                        // turn and no sideways jump at a step); its trail is the same curve behind it
                        try {
                            using var _h = Timing.Begin(THomingDraw);
                            double back = TrailCurve.Back(now - s.lastStepAt, fixedDt);
                            HVec next = s.h.Pos + s.h.Dir * (s.h.Speed * fixedDt);
                            pos = V(TrailCurve.At(s.path, s.pathCount, next, back));
                            HVec heading = TrailCurve.Heading(s.path, s.pathCount, next, back);
                            dir = (heading.Length > 0.5 ? V(heading) : V(s.h.Dir)) * (float)s.h.Speed;
                            if (s.ghost != null && s.ghost.HasTrail) {
                                int n = SeekLook.TrailPoints;
                                double span = TrailCurve.Steps(s.ghost.TrailMetres, s.h.Speed, fixedDt) / (n - 1);
                                trailPoints[0] = pos;
                                for (int k = 1; k < n; k++) trailPoints[k] = V(TrailCurve.At(s.path, s.pathCount, next, back + span * k));
                                s.ghost.MoveAlong(trailPoints, n, dir);
                                if (s.streak != null) s.streak.Move(pos, dir, (pos - s.p0).magnitude);
                                continue;
                            }
                        }
                        catch (Exception e) {
                            SeekLook.FlightFailed(e); // from here on: straight from each step, as below
                            dir = V(s.h.Dir) * (float)s.h.Speed;
                            pos = V(s.h.Pos) + dir * Mathf.Clamp(now - s.lastStepAt, 0f, 0.05f);
                        }
                    }
                    else if (s.homing) {
                        // H1: from its last physics step, carried on along its heading to this frame (at most a few ticks ahead)
                        dir = V(s.h.Dir) * (float)s.h.Speed;
                        pos = V(s.h.Pos) + dir * Mathf.Clamp(now - s.lastStepAt, 0f, 0.05f);
                    }
                    else {
                        float t = Mathf.Max(0f, now - s.born);
                        pos = Position(s, t);
                        dir = Direction(s.flat, s.speed, s.elevation, t);
                    }
                    if (s.ghost != null) s.ghost.MoveTo(pos, dir); // needles start on the real tip: no slide
                    if (s.needle != null) s.needle.MoveTo(pos, dir);
                    if (s.streak != null) s.streak.Move(pos, dir, (pos - s.p0).magnitude);
                }
            }
        }

        // ---------------------------------------------------------------- client: what you see

        /// <summary>Called once per frame from the mod. Draws the extras round each drawn bow.</summary>
        internal static void DrawHeld() {
            if (states.Count == 0 || ApplicationManager.IsHeadless) return;
            dead.Clear();
            foreach (KeyValuePair<BowString, State> pair in states) {
                BowString bow = pair.Key; State s = pair.Value;
                if (bow == null || bow.StringDock == null || !bow.StringDock.HasDockedPickup) { dead.Add(bow!); continue; }
                Arrow arrow = bow.StringDock.DockedPickup.GetComponent<Arrow>();
                if (arrow == null) continue;
                Build build = BuildOf(s);
                int n = Ghosts(bow);
                // A Charge main has no volley (Inputs gates it); its heavy arrows show from stage 1 in their own layout
                // (HeavyPattern: pairs close beside the real arrow), with the heavy look (HeavyLook), sliding out one pair per
                // multi-arrow orb PairSeconds apart.
                bool heavyHeld = false;
                if (n == 0) { n = HeavyShowing(bow, s, build); heavyHeld = n > 0; }
                if (n != s.lastShown) {
                    for (int i = s.lastShown; i < n; i++) s.seenAt[i] = Time.time + (heavyHeld ? HeavyLook.SlideDelay(i) : 0f);
                    s.lastShown = n;
                }
                if (heavyHeld && !s.heavySlidLogged && Time.time >= s.seenAt[n - 1]) {
                    s.heavySlidLogged = true;
                    if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Heavy arrows: {0} on '{1}' (pair {2} slid out; {3:0.000} m beside the arrow, spread {4:0.0#} degrees)",
                        n, bow.name, (n + 1) / 2, HeavyLook.Radius, HeavyLook.SpreadDegrees));
                }
                float heavyGlow = heavyHeld ? ChargeFx.StageGlow(bow) : 1f; // they brighten with the main arrow's stages
                // Holy Arrow: whatever is showing sits fanned out, then folds into the arrow over the last GatherSeconds.
                float gather = s.holyCharging && n > 0
                    ? DrawTimeline.GatherProgress(Time.time - s.holyAt, Paths.HolySecondsFor(build), gatherSeconds.Value) : 0f;

                HeldLayout layout = new HeldLayout(bow, arrow, arrow.transform.forward, Complements.SpreadFactor(build, Paths.Values));
                float slot = 1f / Mathf.Max(1, n);
                // Gold once the shot is charged. Slowdraw's Charged status also comes for a Multi main that owns Charge Shot orbs (the
                // game's skill still runs), but such an archer has no charged shot (the path gate): it never turns their ghosts gold
                // (G4; with it they were gold from Slowdraw's own time, before any hold stage). Other archers' ghosts dimmer.
                bool charged = heavyHeld || (s.chargeCharged && Resolver.OnPath(build.Main, MainPath.Charge));
                float brightness = s.local ? 1f : OtherArcherBrightness;
                Controller? hand = null;
                // A Multi main's Charge Shot orbs, earned by holding (G3): the stage this machine sees, and how near the next one is.
                int holdOrbs = charged || n <= 0 ? 0 : VolleyHold.Orbs(build), holdStage = 0;
                float holdToward = 0f;
                // The beats (sounds for every bow drawn here, pulses only in the shooter's own bow hand) and Burst's rack.
                float ripple = 0f;
                bool racked = false;
                try {
                    bool volleyCharged = Inputs(bow, s, null, out int tier, out float t, out VolleyTimes tm);
                    racked = VolleyTimeline.Racked(tier, volleyCharged, t, tm);
                    hand = s.local ? BowHand(bow) : null;
                    // G4: an archer who holds for stages has no charge thump for the full-volley ripple to fold into (it came at
                    // Charge Shot's stage-2 time, between the rack and the first hold stage): theirs comes at the ripple's own time
                    ripple = VolleyFx.Beats.Frame(bow, s.local, n, t, tier, holdOrbs > 0 ? 0 : build.Charge, racked, layout.RealTip, hand, tm, out bool rack, heavyHeld);
                    if (rack) Burst.Rack(bow, layout.RealTip, hand);
                }
                catch (Exception e) { FxError("beats", e); }
                // G4: the hold stages' look (GoldHold): the thump at a stage reached, then each arrow's own look below. If it is off
                // or cannot be built, the look before it: every ghost gold from stage 1.
                bool staged = false;
                GoldHold.Look hold = default;
                if (holdOrbs > 0) {
                    HoldNow(bow, s, build, out holdStage, out holdToward);
                    if (GoldHold.On) {
                        try { hold = GoldHold.Frame(bow, s.local, holdStage, holdToward, holdOrbs, GoldStages.Rings(n), layout.RealTip, hand, brightness); staged = true; }
                        catch (Exception e) { GoldHold.Failed(e.Message); }
                    }
                    if (!staged) charged = GoldGhosts(holdStage);
                }
                if (!staged) GoldHold.Forget(bow);
                // H2, the shooter only ("the bow tells you too"): while a release now would home (a lock held inside the release
                // cone, Seek.HeldLocks), the ghosts and pellet arrows turn the homing green in a sweep round their rings and each
                // leans toward the creature it will fly to (dealt as Launch deals them: HomingAssign). No lock: exactly as before.
                List<Vector3>? seekAt = null;
                bool seekBow = false;
                if (s.local && !heavyHeld && n > 0) {
                    try { seekAt = Seek.HeldLocks(bow); SeekLook.Bow(seekAt != null); seekBow = true; }
                    catch (Exception e) { SeekLook.BowFailed(e); seekAt = null; }
                }
                float headProgress = VolleyFx.Beats.HeadProgress(ripple), shaftBoost = VolleyFx.Beats.ShaftBoost(ripple);
                for (int i = 0; i < VolleyMath.Total; i++) {
                    if (i >= n) {
                        if (s.ghosts[i] != null) { UnityEngine.Object.Destroy(s.ghosts[i]!.gameObject); s.ghosts[i] = null; }
                        continue;
                    }
                    // a ghost dressed as a heavy arrow is not reused as a plain ghost (the build changed mid-draw)
                    if (!heavyHeld && s.ghosts[i] != null && s.ghosts[i]!.Heavy != null) { UnityEngine.Object.Destroy(s.ghosts[i]!.gameObject); s.ghosts[i] = null; }
                    Ghost g = s.ghosts[i] ??= Ghost.Create(arrow, i, rampTestOk: !heavyHeld);
                    if (heavyHeld && g.MakeHeavy()) g.Heavy!.Glow = heavyGlow; // else the gold ghost look
                    if (staged) {
                        // its ring's level at this stage, its turn in the sweep, the shimmer toward the next (looks only)
                        GoldHold.Tone tone = hold.For(GoldStages.GhostRing(i), VolleyMath.Azimuth(i), i);
                        g.Style(tone, brightness);
                        if (tone.Flare > ripple) g.Ripple(VolleyFx.Beats.HeadProgress(tone.Flare), VolleyFx.Beats.ShaftBoost(tone.Flare));
                        else g.Ripple(headProgress, shaftBoost);
                    }
                    else {
                        g.Style(charged, brightness);
                        g.Ripple(headProgress, shaftBoost);
                    }
                    Vector3 tip, dir;
                    if (heavyHeld) layout.HeavyTip(i, n, out tip, out dir);
                    else layout.Tip(i, out tip, out dir);
                    g.Green = seekBow ? SeekLook.BowGreen(VolleyMath.Azimuth(i)) : 0f;
                    if (seekAt != null)
                        dir = SeekLook.Lean(tip, dir, seekAt[HomingAssign.Lock(HomingAssign.Order(i < VolleyMath.Inner ? SpectralKind.Inner : SpectralKind.Outer, i, n), seekAt.Count)]);
                    float arrive = Mathf.Clamp01((Time.time - s.seenAt[i]) / Mathf.Max(0.01f, arriveSeconds.Value));
                    // last out, first in: the outer ring folds first, then the inner one
                    float folded = gather > 0f ? Mathf.Clamp01((gather - (n - 1 - i) * slot) / slot) : 0f;
                    float outness = arrive * (1f - folded);
                    g.Hold(Vector3.Lerp(layout.RealTip, tip, outness), dir, layout.length * visibleFraction.Value, outness);
                }
                // Burst's fifteen pellets show as their own small arrows from the rack on (Burst.Held: 30 arrows on the bow)
                bool burstShows = racked && n >= VolleyMath.Inner;
                try {
                    if (Burst.Held.Begin(bow, arrow, burstShows, out Burst.Held.Set? set) && set != null) {
                        // last out, first in: they fold back into the arrow with the first ghost that folds (Holy Arrow's gather)
                        float fold = gather > 0f ? Mathf.Clamp01(gather / slot) : 0f;
                        float drawn = layout.length * visibleFraction.Value;
                        bool placed = true;
                        for (int p = 0; p < BurstPattern.Count && placed; p++) {
                            layout.PelletTip(p, out Vector3 pTip, out Vector3 pDir);
                            BurstPattern.Pellet(p, 0f, 0f, out _, out float turn);
                            // the gold of the ghosts of its own ring: the one switch, or its ring's turn in the hold stages (G4)
                            GoldHold.Tone tone = staged ? hold.For(GoldStages.PelletRing(p), turn, VolleyMath.Total + p) : GoldHold.Tone.Plain(charged);
                            // H2: green and leaning with the ghosts (the pellets are dealt round the locks after the ghosts)
                            if (seekAt != null) pDir = SeekLook.Lean(pTip, pDir, seekAt[HomingAssign.Lock(HomingAssign.Order(SpectralKind.Pellet, p, n), seekAt.Count)]);
                            placed = Burst.Held.Place(set, p, layout.RealTip, pTip, pDir, drawn, brightness, fold, tone, seekBow ? SeekLook.BowGreen(turn) : 0f);
                        }
                    }
                }
                catch (Exception e) { FxError("pellet arrows", e); }
            }
            foreach (BowString b in dead) {
                if (!ReferenceEquals(b, null) && states.TryGetValue(b, out State s)) {
                    foreach (Ghost? g in s.ghosts) if (g != null) UnityEngine.Object.Destroy(g.gameObject);
                    states.Remove(b);
                }
                if (!ReferenceEquals(b, null)) DropFx(b);
            }
        }

        // An effect that throws every frame logs once per distinct message, not every frame.
        static void FxError(string what, Exception e) {
            string line = $"[Archery] Volley FX: {what} failed: {e}";
            if (line == lastFxError) return;
            lastFxError = line;
            MelonLogger.Error(line);
        }

        internal static Material? ResolveHeadMaterial() => HeadMaterial();

        static readonly int T_HeadMaterial = Timing.Section("asset Volley.HeadMaterial");
        static Material? HeadMaterial() {
            using var _t = Timing.Begin(T_HeadMaterial);
            string kind = ghostMaterial.Value.Trim().ToLowerInvariant();
            if (headMaterial != null && headMaterialKind == kind) return headMaterial;
            headMaterialKind = kind;
            headMaterial = null;
            if (kind == "skill") {
                try {
                    PooledObject prefab = GlobalSettings<ProgressionSettings>.Instance.ToolBasedEffect.Prefab;
                    ToolBasedEffectBase effect = prefab != null ? prefab.GetComponent<ToolBasedEffectBase>() : null!;
                    Material baseMat = effect != null ? Traverse.Create(effect).Field("baseMaterial").GetValue<Material>() : null!;
                    if (baseMat != null) {
                        headMaterial = new Material(baseMat);
                        if (headMaterial.HasProperty("_Progress")) headMaterial.SetFloat("_Progress", 1f);
                        if (Logs.Summary) MelonLogger.Msg($"[Archery] Volley: extra arrowheads use the game's skill material '{baseMat.name}' ({baseMat.shader.name})");
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Volley: skill material not available ({e.Message})."); }
            }
            if (headMaterial == null) {
                Material? flat = Fx.LineMaterial();
                if (flat != null) { headMaterial = new Material(flat) { color = Colour }; if (Logs.Summary) MelonLogger.Msg("[Archery] Volley: extra arrowheads use flat colour."); }
            }
            if (headMaterial != null && !loggedHeadTextures) {
                loggedHeadTextures = true;
                try { if (Logs.Summary) MelonLogger.Msg($"[Archery] Volley FX: head material '{headMaterial.name}' ({Fx.ShaderName(headMaterial)}) textures: {Fx.TextureProps(headMaterial)}"); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Volley FX: head material textures not readable ({e.Message})"); }
            }
            return headMaterial;
        }

        // The ramp test (spec Step 3; Part 12 item 13): ghost 0 gets its own copy of the head material in which every texture
        // property whose texture name contains "ramp" is set to the first such texture. Ghosts 1 to 14 keep the original.
        static readonly int T_Ghost0Material = Timing.Section("asset Volley.Ghost0Material");
        static Material? Ghost0Material() {
            using var _t = Timing.Begin(T_Ghost0Material);
            Material? baseMat = HeadMaterial();
            if (baseMat == null) return null;
            if (!rampTest.Value) { RampLog("not run (RampTest is off)"); return baseMat; }
            if (rampMaterialTried && ReferenceEquals(rampMaterialFor, baseMat)) return rampMaterial != null ? rampMaterial : baseMat;
            rampMaterialTried = true;
            rampMaterialFor = baseMat;
            rampMaterial = null;
            try {
                Texture? first = null;
                var props = new List<string>();
                foreach (string p in baseMat.GetTexturePropertyNames()) {
                    Texture tex = baseMat.GetTexture(p);
                    if (tex == null || tex.name.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (first == null) first = tex;
                    props.Add(p);
                }
                if (first == null) { RampLog($"not run (no texture property of '{baseMat.name}' holds a texture named '*ramp*')"); return baseMat; }
                var m = new Material(baseMat) { name = baseMat.name + " (ramp test)" };
                foreach (string p in props) m.SetTexture(p, first);
                rampMaterial = m;
                RampLog(string.Join(", ", props.ConvertAll(p => $"{p}<-'{first.name}'")));
                return m;
            }
            catch (Exception e) { RampLog($"not run ({e.Message})"); return baseMat; }
        }

        static void RampLog(string what) {
            string line = $"[Archery] Volley FX: ramp test on ghost 0: {what}";
            if (line == lastRampLog) return;
            lastRampLog = line;
            if (Logs.Summary) MelonLogger.Msg(line);
        }

        // One spectral arrow: a copy of the nocked arrow's head, plus a shaft drawn as a line that fades toward
        // the hand. No fletching. Exists only on the machine that draws it.
        internal class Ghost : MonoBehaviour {
            LineRenderer shaft = null!;
            Transform head = null!;
            bool outer;
            readonly List<MeshRenderer> headParts = new List<MeshRenderer>();
            static MaterialPropertyBlock? block;
            static readonly int ProgressId = Shader.PropertyToID("_Progress"); // the property the game animates (ToolBasedEffectBase.cs:8)
            float headProgress = 1f, shaftBoost = 1f;

            /// <summary>Ghost 0 (the top inner ghost) carries the ramp test's material unless <paramref name="rampTestOk"/> is false (echoes).</summary>
            static readonly int T_Create = Timing.Section("look Volley.Create (ghost / needle)");
            internal static Ghost Create(Arrow arrow, int index, bool rampTestOk = true) {
                using var _t = Timing.Begin(T_Create);
                var go = new GameObject("ArcheryGhostArrow");
                Ghost g = go.AddComponent<Ghost>();
                g.outer = index >= VolleyMath.Inner;
                var shaftGo = new GameObject("shaft");
                shaftGo.transform.SetParent(go.transform, false);
                g.baseWidth = g.shownWidth = shaftWidth.Value * (g.outer ? outerScale.Value : 1f);
                g.shaft = Fx.NewLine(shaftGo, 2, g.baseWidth, false);
                if (g.outer) g.glintScale = Mathf.Max(0f, outerScale.Value);

                var headGo = new GameObject("head");
                headGo.transform.SetParent(go.transform, false);
                g.head = headGo.transform;
                // Copy the real arrowhead's meshes (the arrow's tip is its own part), LOD0 only. An arrow with no head part has just the shaft.
                if (arrow.ArrowTip != null && arrow.ArrowTip != arrow.Entity) {
                    Transform tip = arrow.ArrowTip.transform;
                    Material? mat = index == 0 && rampTestOk ? Ghost0Material() : HeadMaterial();
                    g.ownHead = mat;
                    foreach (MeshFilter mf in HeadMeshes(arrow, tip)) {
                        if (mf.sharedMesh == null || mat == null) continue;
                        var part = new GameObject(mf.name);
                        part.transform.SetParent(headGo.transform, false);
                        part.transform.localPosition = tip.InverseTransformPoint(mf.transform.position);
                        part.transform.localRotation = Quaternion.Inverse(tip.rotation) * mf.transform.rotation;
                        part.transform.localScale = mf.transform.lossyScale * (g.outer ? outerScale.Value : 1f);
                        part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                        var mr = part.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = mat;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                        g.headParts.Add(mr);
                    }
                    // the head keeps the real head's orientation relative to the arrow's forward
                    g.headOffset = Quaternion.Inverse(Quaternion.LookRotation(arrow.transform.forward)) * tip.rotation;
                    g.headBack = HeadRear(g.headParts, g.headOffset);
                }
                return g;
            }

            const float MaxHeadBack = 0.3f;

            static Vector3 Corner(Bounds b, int i) =>
                b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));

            /// <summary>G4: metres from this ghost's tip back to the rear of its head, along the arrow (0 with no head): the
            /// farthest-back corner of its head parts' mesh bounds. The arrow's 'Arrow Tip' is not its point: on the plain
            /// Arrow it sits 4 cm behind the point, inside a head 14 cm long and up to 7.6 cm wide, so anything small put at
            /// the tip is inside the head, and from behind (where the archer looks from) the head hides whatever is ahead of
            /// its widest part. What the archer must see of a head goes at its rear (the glint).</summary>
            static float HeadRear(List<MeshRenderer> parts, Quaternion offset) {
                float back = 0f;
                foreach (MeshRenderer r in parts) {
                    if (r == null) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Bounds b = mf.sharedMesh.bounds;
                    Transform t = r.transform;
                    for (int i = 0; i < 8; i++) {
                        Vector3 inHead = t.localPosition + t.localRotation * Vector3.Scale(Corner(b, i), t.localScale);
                        back = Mathf.Max(back, -(offset * inHead).z);
                    }
                }
                return Mathf.Clamp(back, 0f, MaxHeadBack);
            }

            /// <summary>G4: metres from a real arrow's 'Arrow Tip' back to the rear of its arrowhead, along the arrow, from its
            /// head's meshes as they are now (the plain Arrow's spike, a crafted arrow's own head); 0 when it has no head mesh.
            /// SnapWeight's ember sits there, round the shaft, where it is seen from behind. Not kept: a crafted arrow of the
            /// same kind can carry another head (asked once per draw).</summary>
            internal static float HeadBackOf(Arrow arrow) {
                if (arrow == null || arrow.ArrowTip == null || arrow.ArrowTip == arrow.Entity) return 0f;
                Transform tip = arrow.ArrowTip.transform;
                Vector3 origin = tip.position, fwd = arrow.transform.forward;
                float back = 0f;
                foreach (MeshFilter mf in HeadMeshes(arrow, tip)) {
                    if (mf == null || mf.sharedMesh == null) continue;
                    Bounds b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++) back = Mathf.Max(back, -Vector3.Dot(mf.transform.TransformPoint(Corner(b, i)) - origin, fwd));
                }
                return Mathf.Clamp(back, 0f, MaxHeadBack);
            }

            /// <summary>A smaller ghost of its own kind (Burst.Held: a pellet's arrow on the bow): an inner ghost with no ramp
            /// test, sized every frame by <see cref="Resize"/>.</summary>
            static readonly int T_CreateSmall = Timing.Section("look Volley.Ghost.CreateSmall");
            internal static Ghost CreateSmall(Arrow arrow, string name) {
                using var _t = Timing.Begin(T_CreateSmall, name);
                Ghost g = Create(arrow, 0, rampTestOk: false);
                g.gameObject.name = name;
                return g;
            }

            /// <summary>Head size (times the real arrowhead's) and shaft width in metres. The head's parts keep their places
            /// inside the head; nothing is set when nothing changed.</summary>
            internal void Resize(float headScale, float shaftMetres) {
                if (head.localScale.x != headScale) head.localScale = Vector3.one * headScale;
                baseWidth = shaftMetres; // put on the line by Paint, with the tone's factor (every frame it is drawn)
                glintScale = headScale;
            }

            static readonly HashSet<string> loggedHeads = new HashSet<string>();

            // The head's meshes (fixF: in play-test 7 every ghost head had 0 meshes, 98 of 98). How the game's arrows are built
            // (exported prefabs): the plain Arrow's 'Arrow Tip' is an empty child entity, and its head is Mesh/Spike/
            // metal_detail_spike_fancy_LOD0..2 beside it, in the arrow's own LODGroup; a crafted 'Arrow Shaft Wooden' has an empty
            // 'Tip' child entity too, and the spike crafted onto it is parented under its 'Slot Deco' (JointSlot.AlignAndChild) with
            // its own LODGroup. So, in order:
            //   1. LOD0's renderers under the tip (the tip's own LODGroup, or the arrow's);
            //   2. the highest-detail meshes under the tip itself (LOD0 of whatever LODGroup lists them, or any mesh no LOD lists);
            //   3. the arrow's own highest-detail meshes nearest the tip (the plain arrow's spike, a crafted arrow's spike; the
            //      shaft's middle and the feathers are too far back);
            //   4. none (only the shaft line).
            // Only drawn, enabled renderers count (a crafted part's worn or damaged copy, an empty slot's glow are left out). The
            // meshes and materials are only read and shared, never changed. Which path was taken is logged once per arrow kind.
            static readonly int T_HeadMeshes = Timing.Section("Volley.Ghost.HeadMeshes");
            static List<MeshFilter> HeadMeshes(Arrow arrow, Transform tip) {
                using var _t = Timing.Begin(T_HeadMeshes);
                var list = new List<MeshFilter>();
                string kind = arrow.Entity != null && !string.IsNullOrEmpty(arrow.Entity.OriginalName) ? arrow.Entity.OriginalName : arrow.name;
                LODGroup group = tip.GetComponentInChildren<LODGroup>(true);
                if (group == null) group = tip.GetComponentInParent<LODGroup>();
                LOD[] lods = group != null ? group.GetLODs() : null!;
                if (lods != null && lods.Length > 0 && lods[0].renderers != null) {
                    foreach (Renderer r in lods[0].renderers) {
                        if (!Drawn(r) || !r.transform.IsChildOf(tip)) continue;
                        Add(list, r);
                    }
                }
                string how = "LOD0 under the tip";
                if (list.Count == 0) {
                    how = "the highest-detail meshes under the tip";
                    foreach (Renderer r in HighestDetail(tip)) Add(list, r);
                }
                if (list.Count == 0) {
                    how = "the arrow's own highest-detail meshes nearest the tip";
                    NearestTip(HighestDetail(arrow.transform), tip.position, list);
                }
                if (list.Count == 0) how = "none: only the shaft line is drawn";
                if (loggedHeads.Add(kind)) {
                    var names = new List<string>();
                    foreach (MeshFilter mf in list) names.Add(mf.name);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Volley: ghost heads of '{kind}' (tip '{tip.name}') use {list.Count} mesh(es) from {how}{(names.Count > 0 ? ": " + string.Join(", ", names.ToArray()) : "")} (said once per arrow kind)");
                }
                return list;
            }

            // a pooled game effect parented to the tip (the Fill's knot flash) is not the arrow's own mesh
            static bool Drawn(Renderer? r) => r != null && r.enabled && r.gameObject.activeInHierarchy && r is MeshRenderer
                                              && r.GetComponentInParent<PooledObject>() == null;

            static void Add(List<MeshFilter> list, Renderer r) {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && !list.Contains(mf)) list.Add(mf);
            }

            /// <summary>Every drawn renderer under <paramref name="root"/> at its highest detail: those in LOD0 of a LODGroup that
            /// lists them, and those no LODGroup lists; lower LODs are left out.</summary>
            static List<Renderer> HighestDetail(Transform root) {
                var lod0 = new HashSet<Renderer>();
                var lower = new HashSet<Renderer>();
                foreach (LODGroup g in root.GetComponentsInChildren<LODGroup>(false)) CollectLods(g, lod0, lower);
                foreach (LODGroup g in root.GetComponentsInParent<LODGroup>(false)) CollectLods(g, lod0, lower);
                var result = new List<Renderer>();
                foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false)) {
                    if (!Drawn(r)) continue;
                    if (lod0.Contains(r) || !lower.Contains(r)) result.Add(r);
                }
                return result;
            }

            static void CollectLods(LODGroup g, HashSet<Renderer> lod0, HashSet<Renderer> lower) {
                if (g == null) return;
                LOD[] lods = g.GetLODs();
                if (lods == null) return;
                for (int i = 0; i < lods.Length; i++) {
                    if (lods[i].renderers == null) continue;
                    foreach (Renderer r in lods[i].renderers) if (r != null) (i == 0 ? lod0 : lower).Add(r);
                }
            }

            const float HeadReach = 0.25f, HeadBand = 0.04f;

            /// <summary>The renderers nearest the tip: the nearest one's centre within HeadReach of the tip, and every other one
            /// within HeadBand farther than it (a head made of two meshes).</summary>
            static void NearestTip(List<Renderer> candidates, Vector3 tip, List<MeshFilter> list) {
                float best = float.PositiveInfinity;
                foreach (Renderer r in candidates) best = Mathf.Min(best, (r.bounds.center - tip).magnitude);
                if (!(best <= HeadReach)) return;
                foreach (Renderer r in candidates)
                    if ((r.bounds.center - tip).magnitude <= Mathf.Min(HeadReach, best + HeadBand)) Add(list, r);
            }

            Quaternion headOffset = Quaternion.identity;
            GoldHold.Tone tone = GoldHold.Tone.Plain(false);
            float brightness = 1f;
            float baseWidth, shownWidth;    // the shaft's own width, and the width now on its line
            float headBack;                 // metres from the tip back to the rear of the head (HeadRear), at the head's own size
            bool manyPoints;                // the shaft is drawn with GoldStages.ShaftPoints points (the shimmer); else 2
            LineRenderer? glint;            // the small gold glint on the head (G4), on a child of its own, made when first shown
            float glintScale = 1f;          // smaller with a smaller head (an outer ghost, a pellet arrow)
            static bool glintFailed;
            HeavyLook? heavy;
            bool heavyTried;
            /// <summary>H2: how far this ghost has turned to the homing green (0 to 1): on the bow as the green sweeps round the rings
            /// while a release would home (SeekLook.BowGreen); 1 in a locked release's flight. The shaft is painted through
            /// GhostColour; the head takes the green copy of its material from half-way on (SeekLook.HeadMaterial: the ghosts' own
            /// head material with its colours set, as a heavy arrow's head is made; its own material again below half).</summary>
            internal float Green {
                get => green;
                set {
                    green = value;
                    bool on = value >= 0.5f;
                    if (on != headGreen) HeadGreen(on);
                }
            }
            float green;
            bool headGreen;
            Material? ownHead;

            void HeadGreen(bool on) {
                headGreen = on;
                if (heavy != null || headParts.Count == 0) return;
                Material? m = on ? SeekLook.HeadMaterial() : ownHead; // null: no green copy can be had, the head keeps its own
                if (m == null) return;
                foreach (MeshRenderer r in headParts) if (r != null) r.sharedMaterial = m;
            }
            /// <summary>H1: this ghost flies in a locked (homing) release: painted in the homing colour.</summary>
            internal bool Homing { get => Green >= 1f; set => Green = value ? 1f : 0f; }

            // H2, a homing arrow in flight (SeekLook.Dress): bigger, and its shaft is a longer, tapering trail that fades to nothing,
            // drawn on the flight's own curve (MoveAlong; VolleySim gives the points)
            float size = 1f;
            bool trail;
            float trailLength = 1f;
            static AnimationCurve? taperCurve;
            static float taperFor = -1f;

            /// <summary>This ghost at <paramref name="k"/> times its size: head and shaft width (and its glint with the head).</summary>
            internal void Bigger(float k) {
                if (!(k > 0f) || k == size) return;
                float by = k / size;
                size = k;
                head.localScale = head.localScale * by;
                baseWidth *= by; // put on the line by Paint
                glintScale *= by;
            }

            /// <summary>In flight this ghost's shaft is a trail <paramref name="lengthFactor"/> times as long, narrowing to
            /// <paramref name="taper"/> of its width at its end and fading to nothing.</summary>
            internal void Trail(float lengthFactor, float taper) {
                trail = true;
                trailLength = Mathf.Max(0.1f, lengthFactor);
                if (taperCurve == null || taperFor != taper) { taperCurve = AnimationCurve.Linear(0f, 1f, 1f, taper); taperFor = taper; } // one curve for every trail
                shaft.widthCurve = taperCurve;
                shaft.numCornerVertices = 2;
            }

            internal bool HasTrail => trail;
            /// <summary>Metres of trail behind this ghost in flight.</summary>
            internal float TrailMetres => flightLength * trailLength;

            /// <summary>Gold once the shot is charged; other archers' ghosts at OtherArcherBrightness.</summary>
            internal void Style(bool isCharged, float bright) { tone = GoldHold.Tone.Plain(isCharged); brightness = bright; }

            /// <summary>G4: one arrow of a multi-arrow main's hold stages (GoldHold): how gold, how bright and wide, the shimmer
            /// toward the next stage, the glint on its head.</summary>
            internal void Style(in GoldHold.Tone look, float bright) { tone = look; brightness = bright; }

            /// <summary>The heavy look this ghost wears (a Charge main's heavy arrow), or null.</summary>
            internal HeavyLook? Heavy => heavy;

            /// <summary>Dresses this ghost as a heavy arrow (HeavyLook), once; false when the look cannot be built, and the
            /// ghost then keeps its own (gold) look. True again on later calls once dressed.</summary>
            static readonly int T_MakeHeavy = Timing.Section("look Volley.Ghost.MakeHeavy");
            internal bool MakeHeavy() {
                using var _t = Timing.Begin(T_MakeHeavy);
                if (heavy != null) return true;
                if (heavyTried) return false;
                heavyTried = true;
                heavy = HeavyLook.Attach(this, shaft, headParts, outer);
                if (heavy == null) return false;
                outer = false; // every heavy arrow, the sixth included, has the same look
                return true;
            }

            void Paint(Vector3 tip, Vector3 direction, float drawnLength, float tipAlpha, float tailAlpha, bool held) {
                Color c;
                float k;
                if (heavy != null) { c = heavy.ShaftColour; k = brightness * HeavyLook.Alpha; }
                else {
                    c = GhostColour(tone.Gold, Green);
                    k = (outer ? outerBrightness.Value : 1f) * brightness * shaftBoost;
                    if (shaftBoost != 1f) c = new Color(Mathf.Min(1f, c.r * shaftBoost), Mathf.Min(1f, c.g * shaftBoost), Mathf.Min(1f, c.b * shaftBoost));
                    if (tone.Glow > 1f) { c = GoldHold.Brighter(c, tone.Glow); k *= tone.Glow; } // the brighter level (G4)
                    float w = baseWidth * tone.Width;
                    if (w != shownWidth) { shaft.widthMultiplier = w; shownWidth = w; }
                }
                // Paint runs for every ghost every frame from Volley.DrawHeld, which nothing catches: the new parts never throw out of it
                bool shimmers = heavy == null && held && tone.Shimmer > 0f && tone.ShimmerTint > 0f && tipAlpha > 0.01f;
                if (shimmers) {
                    try { PaintShimmer(tip, direction, drawnLength, tipAlpha, tailAlpha, c, k); }
                    catch (Exception e) { shimmers = false; GoldHold.Failed("the shimmer: " + e.Message); } // the one switch from the next frame on
                }
                if (!shimmers) {
                    if (manyPoints) { shaft.positionCount = 2; shaft.colorGradient = plainGradient; manyPoints = false; } // two keys again
                    shaft.SetPosition(0, tip);
                    shaft.SetPosition(1, tip - direction * drawnLength);
                    shaft.startColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, tipAlpha * k));
                    shaft.endColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, tailAlpha * k));
                }
                head.SetPositionAndRotation(tip, Quaternion.LookRotation(direction) * headOffset);
                head.gameObject.SetActive(tipAlpha > 0.5f);
                if (heavy != null) heavy.Painted(tip, direction, drawnLength, tipAlpha * brightness, held);
                else if (glint != null || tone.Glint > 0.01f) PaintGlint(tip, direction, tipAlpha);
            }

            // Reused by every shimmering shaft, every frame (the main thread only): a LineRenderer copies its positions and
            // gradient when they are set, so nothing is allocated.
            static readonly Vector3[] shimmerPoints = new Vector3[GoldStages.ShaftPoints];
            static readonly Gradient shimmerGradient = new Gradient();
            static readonly GradientColorKey[] shimmerColours = new GradientColorKey[GoldStages.ShaftPoints];
            static readonly GradientAlphaKey[] shimmerAlphas = new GradientAlphaKey[GoldStages.ShaftPoints];
            static readonly Gradient plainGradient = new Gradient(); // a new Gradient is two keys: start and end colour

            /// <summary>G4, held only: the shaft with the shimmer creeping from its back end toward the tip (GoldStages.Shimmer):
            /// where it has reached, the shaft is tinted toward the next stage's colour and a little more opaque. A line's
            /// colours are set at its points, so the shaft is drawn with ShaftPoints evenly spaced ones while it shimmers
            /// (point 0 = the tip, as always).</summary>
            void PaintShimmer(Vector3 tip, Vector3 direction, float drawnLength, float tipAlpha, float tailAlpha, Color c, float k) {
                int n = GoldStages.ShaftPoints;
                if (!manyPoints) { shaft.positionCount = n; manyPoints = true; }
                Color next = GoldHold.Brighter(GhostColour(tone.NextGold, Green), tone.NextGlow); // a green ghost shimmers toward brighter green
                float soft = GoldHold.ShimmerSoft;
                for (int i = 0; i < n; i++) {
                    float time = i / (float)(n - 1); // along the line: 0 = the tip, 1 = its back end
                    shimmerPoints[i] = tip - direction * (drawnLength * time);
                    float reached = GoldStages.Shimmer(1f - time, tone.Shimmer, soft);
                    shimmerColours[i] = new GradientColorKey(Color.Lerp(c, next, Mathf.Clamp01(reached * tone.ShimmerTint)), time);
                    float a = Mathf.Lerp(tipAlpha, tailAlpha, time) + reached * tone.ShimmerGlow * tipAlpha;
                    shimmerAlphas[i] = new GradientAlphaKey(Mathf.Clamp01(a * k), time);
                }
                shaft.SetPositions(shimmerPoints);
                shimmerGradient.SetKeys(shimmerColours, shimmerAlphas);
                shaft.colorGradient = shimmerGradient;
            }

            /// <summary>G4, the brighter level: a small bead at the rear of the head, facing along the arrow (the archer looks
            /// down the shaft from behind: there it is seen face-on, in front of the head, and not hidden inside or behind
            /// it), in [GoldHold] GlintColour. Made when first shown; if it cannot be made, one log line and no ghost shows
            /// a glint (everything else stays).</summary>
            void PaintGlint(Vector3 tip, Vector3 direction, float tipAlpha) {
                float a = tone.Glint * brightness;
                if (a <= 0.01f || tipAlpha <= 0.5f) {
                    if (glint != null && glint.enabled) glint.enabled = false;
                    return;
                }
                if (glintFailed) return;
                try {
                    if (glint == null) {
                        var go = new GameObject("glint");
                        go.transform.SetParent(transform, false);
                        LineRenderer lr = Fx.NewLine(go, 2, GoldHold.GlintWidth, false);
                        Fx.BuildCircle(lr);
                        glint = lr;
                    }
                    Transform t = glint.transform;
                    t.SetPositionAndRotation(tip - direction * (headBack * head.localScale.x + GoldHold.GlintBehind), Quaternion.LookRotation(direction));
                    float r = GoldHold.GlintRadius * glintScale;
                    if (t.localScale.x != r) t.localScale = Vector3.one * r;
                    glint.widthMultiplier = GoldHold.GlintWidth * glintScale;
                    Color gc = GoldHold.GlintColour;
                    gc.a = Mathf.Clamp01(a);
                    glint.startColor = gc;
                    glint.endColor = gc;
                    if (!glint.enabled) glint.enabled = true;
                }
                catch (Exception e) {
                    glintFailed = true;
                    if (glint != null) glint.enabled = false;
                    MelonLogger.Warning($"[Archery] Gold hold: the glint on the ghosts' heads could not be shown ({e.Message}); the third stage shows without it");
                }
            }

            /// <summary>The arrow landed or its life ended: a heavy arrow's embers are let go to fade where they are, then the
            /// ghost goes.</summary>
            internal void End() {
                if (heavy != null) heavy.LetGo();
                Destroy(gameObject);
            }

            /// <summary>The full-volley ripple (VolleyFx.Beats): the heads' _Progress through a property block (1 = the material's
            /// own value, so the block is cleared), the shafts' brightness factor.</summary>
            internal void Ripple(float progress, float boost) {
                shaftBoost = boost;
                if (Mathf.Abs(progress - headProgress) < 1e-4f) return;
                headProgress = progress;
                bool clear = progress >= 1f - 1e-4f;
                if (!clear) { block ??= new MaterialPropertyBlock(); block.Clear(); block.SetFloat(ProgressId, progress); }
                foreach (MeshRenderer r in headParts) if (r != null) r.SetPropertyBlock(clear ? null : block);
            }

            /// <summary>While the bow is held: only the front of the arrow, fading to nothing toward the hand.</summary>
            internal void Hold(Vector3 tip, Vector3 direction, float drawnLength, float alpha) => Paint(tip, direction, drawnLength, alpha, 0f, true);

            /// <summary>Released: the whole arrow, glowing (a heavy arrow drops its strand and starts its embers).</summary>
            internal void Fly(Vector3 tip, Vector3 direction, float length) {
                Ripple(1f, 1f);
                flightLength = length;
                if (heavy != null) heavy.Glow = 1f;
                Paint(tip, direction, trail ? TrailMetres : length, 1f, trail ? 0f : 0.6f, false);
                if (heavy != null) heavy.Flown(head);
            }

            float flightLength = 0.7f;

            internal void MoveTo(Vector3 tip, Vector3 velocity) {
                if (velocity.sqrMagnitude > 0.01f) Paint(tip, velocity.normalized, trail ? TrailMetres : flightLength, 1f, trail ? 0f : 0.6f, false);
            }

            /// <summary>H2, every rendered frame of a homing ghost with a trail (VolleySim.LateUpdate): its head at
            /// <paramref name="points"/>[0] flying along <paramref name="velocity"/>, its shaft and trail through the first
            /// <paramref name="count"/> points (the flight's own curve behind it), full at the head, narrowing and fading to nothing
            /// at the end. No allocation: the line copies the points.</summary>
            internal void MoveAlong(Vector3[] points, int count, Vector3 velocity) {
                if (count < 2 || velocity.sqrMagnitude <= 0.01f) { MoveTo(points[0], velocity); return; }
                Vector3 direction = velocity.normalized;
                Color c = GhostColour(tone.Gold, Green);
                float k = (outer ? outerBrightness.Value : 1f) * brightness;
                if (tone.Glow > 1f) { c = GoldHold.Brighter(c, tone.Glow); k *= tone.Glow; }
                float w = baseWidth * tone.Width;
                if (w != shownWidth) { shaft.widthMultiplier = w; shownWidth = w; }
                if (shaft.positionCount != count) shaft.positionCount = count;
                manyPoints = true; // a straight paint after this (it let go and skipped) goes back to two points
                shaft.SetPositions(points);
                shaft.startColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, k));
                shaft.endColor = new Color(c.r, c.g, c.b, 0f);
                head.SetPositionAndRotation(points[0], Quaternion.LookRotation(direction) * headOffset);
                if (!head.gameObject.activeSelf) head.gameObject.SetActive(true);
                if (glint != null || tone.Glint > 0.01f) PaintGlint(points[0], direction, 1f);
            }

            /// <summary>H2: the whole arrow at <paramref name="alpha"/> (SeekReveal: a green ghost fading out over the real arrow).</summary>
            internal void Fade(Vector3 tip, Vector3 direction, float length, float alpha) => Paint(tip, direction, length, alpha, alpha * 0.6f, false);
        }

        // One flying needle (a Quick main's snap, one per multi-arrow orb): a thin short bright streak, not an arrow. One
        // line Length long, Width at the head tapering to 0 at the tail, in the needle colour, with a hairline trail behind
        // it; no head or shaft mesh. Shown at release only. Settings in [Needles] (looks, not shared). If it cannot be built
        // (no line material), Volley.Launch gives that needle the old echo look (a Ghost) instead.
        internal class NeedleLook : MonoBehaviour {
            static MelonPreferences_Entry<float> length = null!, width = null!, trailSeconds = null!, trailWidth = null!, soundGap = null!, landSparkScale = null!;
            static MelonPreferences_Entry<string> colour = null!, sound = null!;
            static readonly Color DefaultColour = new Color(1f, 0.914f, 0.659f); // #FFE9A8
            static bool loggedFailure;

            LineRenderer streak = null!;
            float len;

            /// <summary>Creates the [Needles] settings (called from Volley.Init, before Cfg.Ready).</summary>
            internal static void Init() {
                var cat = Cfg.Category("Needles");
                length = cat.CreateEntry("Length", 0.35f, description: "Length in metres of each needle's streak (a Quickdraw main's quick shot with multi-arrow orbs).");
                width = cat.CreateEntry("Width", 0.008f, description: "Width of a needle's streak at its head; it tapers to nothing at the tail.");
                colour = cat.CreateEntry("Colour", "#FFE9A8", description: "Needles' streaks, their trails and landing sparks, and Quickdraw 4's needle lines (the Quickdraw ring's gold, lighter).");
                trailSeconds = cat.CreateEntry("TrailSeconds", 0.12f, description: "How long the hairline trail behind each needle lasts. 0 = no trail.");
                trailWidth = cat.CreateEntry("TrailWidth", 0.003f, description: "Width of the hairline trail behind each needle.");
                sound = cat.CreateEntry("Sound", "Click", description: "One sharp sound per needle at the bow when they fly (names separated by ';', the first one found is used; empty = none).");
                soundGap = cat.CreateEntry("SoundGap", 0.02f, description: "Seconds between the needles' sounds, so three needles are heard as three.");
                landSparkScale = cat.CreateEntry("LandSparkScale", 0.6f, description: "Size of a needle's landing sparks, relative to the other spectral arrows' sparks.");
            }

            internal static Color Colour => colour != null ? Cfg.Colour(colour, DefaultColour) : DefaultColour;
            internal static float LandSparkScale => landSparkScale != null ? landSparkScale.Value : 0.6f;

            /// <summary>A needle starting at <paramref name="tip"/> pointing along <paramref name="dir"/>; other archers' at
            /// <paramref name="brightness"/>. Null when it cannot be built (the caller then uses the echo look).</summary>
            static readonly int T_Create2 = Timing.Section("look Volley.Create (ghost / needle) #2");
            internal static NeedleLook? Create(Vector3 tip, Vector3 dir, float brightness) {
                using var _t = Timing.Begin(T_Create2);
                GameObject? go = null;
                try {
                    Material? mat = Fx.LineMaterial();
                    if (mat == null) { Failed("no line material"); return null; }
                    go = new GameObject("ArcheryNeedle");
                    go.transform.position = tip; // before the trail exists, so it does not start from the world origin
                    NeedleLook n = go.AddComponent<NeedleLook>();
                    if (n == null) { Destroy(go); Failed("no NeedleLook component"); return null; }
                    Color c = Colour;
                    float a = Mathf.Clamp01(brightness);
                    n.len = Mathf.Max(0.01f, length.Value);
                    // The streak is the look itself: if its LineRenderer cannot be made, the catch below falls back to the echo look.
                    n.streak = Fx.NewLine(go, 2, Mathf.Max(0f, width.Value), false);
                    if (n.streak == null) { Destroy(go); Failed("no LineRenderer"); return null; }
                    n.streak.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f); // full at the head, 0 at the tail
                    n.streak.numCapVertices = 0;
                    n.streak.startColor = new Color(c.r, c.g, c.b, a);
                    n.streak.endColor = new Color(c.r, c.g, c.b, 0.35f * a);
                    if (trailSeconds.Value > 0f && trailWidth.Value > 0f) n.AddTrail(mat, c, a);
                    n.MoveTo(tip, dir);
                    return n;
                }
                catch (Exception e) {
                    if (go != null) Destroy(go);
                    Failed(e.Message);
                    return null;
                }
            }

            static void Failed(string why) {
                if (loggedFailure) return;
                loggedFailure = true;
                MelonLogger.Warning($"[Archery] Needles: the needle look could not be built ({why}); needles keep the old echo look");
            }

            static bool loggedTrailFailure;
            GameObject? trailGo;
            float trailLife;

            // The hairline trail lives on a child object: a GameObject holds one Renderer, and the streak's LineRenderer is on
            // the root. A trail that cannot be made only loses the trail (logged once); the streak stays.
            void AddTrail(Material mat, Color c, float a) {
                try {
                    var child = new GameObject("trail");
                    child.transform.SetParent(transform, false); // at the head; follows it as the root moves
                    TrailRenderer t = child.AddComponent<TrailRenderer>();
                    if (t == null) { Destroy(child); TrailFailed("no TrailRenderer component"); return; }
                    t.sharedMaterial = mat;
                    t.time = trailSeconds.Value;
                    t.widthMultiplier = trailWidth.Value;
                    t.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                    t.minVertexDistance = 0.05f;
                    t.numCapVertices = 0;
                    t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    t.receiveShadows = false;
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                              new[] { new GradientAlphaKey(0.6f * a, 0f), new GradientAlphaKey(0f, 1f) });
                    t.colorGradient = g;
                    trailGo = child;
                    trailLife = t.time;
                }
                catch (Exception e) { TrailFailed(e.Message); }
            }

            static void TrailFailed(string why) {
                if (loggedTrailFailure) return;
                loggedTrailFailure = true;
                MelonLogger.Warning($"[Archery] Needles: the needle trail could not be built ({why}); needles fly without it");
            }

            /// <summary>Every rendered frame (VolleySim.LateUpdate): the streak's head at <paramref name="tip"/>. No allocation.</summary>
            internal void MoveTo(Vector3 tip, Vector3 velocity) {
                if (velocity.sqrMagnitude < 1e-6f) return;
                transform.position = tip; // the trail child follows the head
                streak.SetPosition(0, tip);
                streak.SetPosition(1, tip - velocity.normalized * len);
            }

            /// <summary>The needle landed or its life ended: the streak goes now; the trail is let go where it is and fades out
            /// over its own TrailSeconds before it is destroyed.</summary>
            internal void End() {
                if (trailGo != null) {
                    trailGo.transform.SetParent(null, true);
                    TrailRenderer t = trailGo.GetComponent<TrailRenderer>();
                    if (t != null) t.emitting = false;
                    Destroy(trailGo, Mathf.Max(0.01f, trailLife));
                    trailGo = null;
                }
                Destroy(gameObject);
            }

            /// <summary>A release launched <paramref name="needles"/> flying needles (<paramref name="looks"/> of them with this
            /// look): one Sound per needle at the bow, SoundGap apart, and one log line.</summary>
            static readonly int T_Released = Timing.Section("look start Volley.NeedleLook.Released");
            internal static void Released(uint release, int needles, int looks, Vector3 bowAt, bool own) {
                using var _t = Timing.Begin(T_Released, release);
                string names = sound != null ? sound.Value : "";
                float gap = soundGap != null ? Mathf.Max(0f, soundGap.Value) : 0.02f;
                for (int i = 0; i < needles; i++) VolleyFx.Play(names, bowAt, i * gap);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Needles: arrow {release} {needles} needle(s) drawn ({(own ? "own" : "seen")})" +
                                (looks < needles ? $"; {needles - looks} with the old echo look" : ""));
            }
        }

        // One heavy arrow (a Charge main's charged shot with multi-arrow orbs, two per orb): a ghost's head and shaft dressed
        // as a full-size arrow in the charge's orange-gold: the head meshes at Scale in HeadColour, the shaft Thickness times a
        // ghost's width in Colour at Alpha, the same for all six. Held: one turn of the main arrow's charge strand (its width,
        // gap, taper and converge, stage 1's colour) round the drawn front, brightening with the main arrow's stages
        // (ChargeFx.StageGlow). In flight: the charged arrow's ember trail at TrailWidthScale. Landing: the Fill's HitEffect at
        // HitEffectScale. Lives on the Ghost's root (which has no renderer); the strand and the trail are on children of their
        // own. Settings in [HeavyArrows]: looks (not shared), and the layout's Radius and SpreadDegrees (shared: they decide
        // where the server's heavy arrows fly). If it cannot be built, Ghost.MakeHeavy leaves the gold ghost look.
        internal class HeavyLook : MonoBehaviour {
            static MelonPreferences_Entry<float> scale = null!, thickness = null!, alpha = null!, pairSeconds = null!, strandTurns = null!,
                slideVolume = null!, trailWidthScale = null!, hitEffectScale = null!, layoutRadius = null!, layoutSpread = null!;
            const float MaxSpreadDegrees = 45f;
            static MelonPreferences_Entry<string> colour = null!, headColour = null!, slideSound = null!, headMaterialKind = null!;
            static readonly Color DefaultColour = new Color(1f, 0.604f, 0.18f);   // #FF9A2E
            static readonly Color DefaultHeadColour = new Color(1f, 0.78f, 0.2f); // #FFC733
            const int StrandPoints = 16;
            static readonly Vector3[] points = new Vector3[StrandPoints]; // reused by every strand, every frame (main thread only)
            static Material? headMat;
            static bool headMatTried, loggedFailure, loggedStrandFailure, loggedTrailFailure, loggedSlide, loggedHit, loggedHitFailure;

            LineRenderer? strand;
            AnimationCurve? strandCurve;
            ChargeFx.EmberTrail? trail;
            float shaftHalf = 0.008f;
            /// <summary>Held: the main arrow's charge brightness (ChargeFx.StageGlow), set every frame by DrawHeld; 1 in flight.</summary>
            internal float Glow = 1f;

            /// <summary>Creates the [HeavyArrows] settings (called from Volley.Init, before Cfg.Ready).</summary>
            internal static void Init() {
                var cat = Cfg.Category("HeavyArrows");
                layoutRadius = Cfg.Shared(cat.CreateEntry("Radius", HeavyPattern.Radius, description: "Metres from the real arrow's tip to each heavy arrow's tip (a Charge Shot main's extra arrows): two sit left and right of the arrow, four make a plus, six a hexagon round it, on the string and at release."));
                layoutSpread = Cfg.Shared(cat.CreateEntry("SpreadDegrees", HeavyPattern.SpreadDegrees, description: "Degrees each heavy arrow leaves the real arrow's line in flight, straight away from it."));
                scale = cat.CreateEntry("Scale", 1.15f, description: "Size of each heavy arrow's head (a Charge Shot main's extra arrows from multi-arrow orbs), relative to the real arrowhead.");
                thickness = cat.CreateEntry("Thickness", 1.4f, description: "Width of a heavy arrow's shaft, as a multiple of a ghost's ([Volley] ShaftWidth).");
                alpha = cat.CreateEntry("Alpha", 0.85f, description: "Opacity of a heavy arrow's shaft (other archers' also times [Volley] OtherArcherBrightness).");
                colour = cat.CreateEntry("Colour", "#FF9A2E", description: "Heavy arrows' shafts: the charge's orange-gold.");
                headColour = cat.CreateEntry("HeadColour", "#FFC733", description: "Heavy arrows' heads.");
                headMaterialKind = cat.CreateEntry("HeadMaterial", "skill", description: "'skill' = a copy of the ghosts' head material with its colours set to HeadColour (flat if it has none); 'flat' = plain HeadColour.");
                pairSeconds = cat.CreateEntry("PairSeconds", 0.08f, description: "Seconds between each pair of heavy arrows sliding out of the tip as Charge Shot's first stage completes.");
                strandTurns = cat.CreateEntry("StrandTurns", 1f, description: "Turns of the charge strand round each held heavy arrow (width, gap, taper and converge from [Fill]). 0 = no strand.");
                slideSound = cat.CreateEntry("SlideSound", "Power Strike Start Sounds", description: "Sound when the heavy arrows slide out: the charge's own thump sound (names separated by ';', the first one found is used). Empty = the multi-arrow slide-out ([Volley] SlideSound).");
                slideVolume = cat.CreateEntry("SlideVolume", 0.6f, description: "Volume (0 to 1) of SlideSound.");
                trailWidthScale = cat.CreateEntry("TrailWidthScale", 0.5f, description: "Width of a flying heavy arrow's ember trail, relative to a charged arrow's ([Fill] TrailWidth). 0 = no trail.");
                hitEffectScale = cat.CreateEntry("HitEffectScale", 0.5f, description: "Size of the charged hit effect ([Fill] HitEffect) where a heavy arrow lands, relative to a charged arrow's. 0 = gold sparks instead.");
                Cfg.Reloaded += () => { headMat = null; headMatTried = false; };
            }

            internal static float Alpha => alpha != null ? Mathf.Clamp01(alpha.Value) : 0.85f;
            /// <summary>[HeavyArrows] Radius (shared): metres from the real tip to each heavy arrow's tip.</summary>
            internal static float Radius => layoutRadius != null ? Mathf.Max(0f, layoutRadius.Value) : HeavyPattern.Radius;
            /// <summary>[HeavyArrows] SpreadDegrees (shared): how far each heavy arrow leaves the arrow's line.</summary>
            internal static float SpreadDegrees => layoutSpread != null ? Mathf.Clamp(layoutSpread.Value, 0f, MaxSpreadDegrees) : HeavyPattern.SpreadDegrees;

            /// <summary>Seconds after the heavy arrows start showing that heavy arrow <paramref name="index"/> slides out (in pairs).</summary>
            internal static float SlideDelay(int index) => (index / 2) * (pairSeconds != null ? Mathf.Max(0f, pairSeconds.Value) : 0.08f);

            /// <summary>The shaft's colour this frame: Colour, toward white with the charge's brightness.</summary>
            internal Color ShaftColour => Brighter(colour != null ? Cfg.Colour(colour, DefaultColour) : DefaultColour, Glow);

            static Color Brighter(Color c, float k) => k <= 1f ? c : Color.Lerp(c, Color.white, 1f - 1f / k);

            /// <summary>Dresses ghost <paramref name="g"/> (Ghost.MakeHeavy); null when the look cannot be built.</summary>
            static readonly int T_Attach = Timing.Section("look Volley.HeavyLook.Attach");
            internal static HeavyLook? Attach(Ghost g, LineRenderer shaft, List<MeshRenderer> headParts, bool wasOuter) {
                using var _t = Timing.Begin(T_Attach);
                try {
                    if (shaft == null) { Failed("no shaft"); return null; }
                    Material? mat = headParts.Count > 0 ? HeadMaterial() : null;
                    if (headParts.Count > 0 && mat == null) { Failed("no head material"); return null; }
                    // every value first, then the component; the ghost itself is changed only after every check has passed, so a
                    // throw before that leaves it a clean gold ghost
                    float w = ShaftWidth * Mathf.Max(0f, thickness.Value);
                    // Ghost.Create scaled an outer ghost's head by OuterScale: undo it, so the sixth looks like the other five
                    float k = Mathf.Max(0f, scale.Value) / (wasOuter ? Mathf.Max(0.01f, outerScale.Value) : 1f);
                    bool strandWanted = strandTurns.Value > 0f;
                    HeavyLook h = g.gameObject.AddComponent<HeavyLook>();
                    if (h == null) { Failed("no HeavyLook component"); return null; }
                    h.shaftHalf = w * 0.5f;
                    if (strandWanted) h.AddStrand(); // a strand that cannot be made only loses the strand (caught inside)
                    shaft.widthMultiplier = w;
                    foreach (MeshRenderer r in headParts) {
                        if (r == null) continue;
                        r.transform.localScale *= k;
                        r.sharedMaterial = mat;
                    }
                    return h;
                }
                catch (Exception e) {
                    // a component added before the throw goes again (its strand child with it)
                    HeavyLook left = g != null ? g.GetComponent<HeavyLook>() : null!;
                    if (left != null) { if (left.strand != null) Destroy(left.strand.gameObject); Destroy(left); }
                    Failed(e.Message);
                    return null;
                }
            }

            static void Failed(string why) {
                if (loggedFailure) return;
                loggedFailure = true;
                MelonLogger.Warning($"[Archery] Heavy arrows: the heavy look could not be built ({why}); those heavy arrows keep the gold ghost look");
            }

            // The heads: a copy of the ghosts' head material with every colour property set to HeadColour, else the flat line
            // material in HeadColour. Made once (again after a settings reload) and shared by every heavy arrow.
            static readonly int T_HeadMaterial2 = Timing.Section("asset Volley.HeadMaterial #2");
            static Material? HeadMaterial() {
                using var _t = Timing.Begin(T_HeadMaterial2);
                if (headMat != null || headMatTried) return headMat;
                headMatTried = true;
                Color hc = headColour != null ? Cfg.Colour(headColour, DefaultHeadColour) : DefaultHeadColour;
                string note = "";
                if (headMaterialKind.Value.Trim().Equals("skill", StringComparison.OrdinalIgnoreCase)) {
                    Material? baseMat = Volley.HeadMaterial();
                    if (baseMat != null) {
                        var m = new Material(baseMat) { name = baseMat.name + " (Archery heavy arrow)" };
                        string props = Tint(m, hc);
                        if (props.Length > 0) { headMat = m; note = $"a copy of '{baseMat.name}' ({Fx.ShaderName(baseMat)}), colours set: {props}"; }
                        else { Destroy(m); note = $"'{baseMat.name}' has no colour property, so "; }
                    }
                    else note = "no ghost head material, so ";
                }
                if (headMat == null) {
                    Material? flat = Fx.LineMaterial();
                    if (flat != null) { headMat = new Material(flat) { color = hc, name = "Archery heavy arrow head (flat)" }; note += "flat HeadColour"; }
                }
                if (headMat != null) { if (Logs.Summary) MelonLogger.Msg($"[Archery] Heavy arrows: heads use {note}"); }
                return headMat;
            }

            /// <summary>Sets every colour property of the material's shader to <paramref name="c"/>; returns their names ("" = none).</summary>
            internal static string Tint(Material m, Color c) {
                if (m.shader == null) return "";
                var parts = new List<string>();
                int n = m.shader.GetPropertyCount();
                for (int i = 0; i < n; i++) {
                    if (m.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                    string p = m.shader.GetPropertyName(i);
                    m.SetColor(p, c);
                    parts.Add(p);
                }
                return string.Join(", ", parts);
            }

            // The strand lives on a child object: a GameObject holds one Renderer. One that cannot be made loses only the strand.
            void AddStrand() {
                try {
                    if (Fx.LineMaterial() == null) { StrandFailed("no line material"); return; }
                    var child = new GameObject("strand");
                    child.transform.SetParent(transform, false);
                    LineRenderer lr = Fx.NewLine(child, StrandPoints, ChargeFx.StrandWidth, false);
                    if (lr == null) { Destroy(child); StrandFailed("no LineRenderer"); return; }
                    lr.numCapVertices = 0;
                    lr.enabled = false;
                    strand = lr;
                }
                catch (Exception e) { StrandFailed(e.Message); }
            }

            static void StrandFailed(string why) {
                if (loggedStrandFailure) return;
                loggedStrandFailure = true;
                MelonLogger.Warning($"[Archery] Heavy arrows: the strand could not be built ({why}); held heavy arrows show without it");
            }

            /// <summary>Every frame the ghost is drawn (Ghost.Paint): held, one turn of strand round the drawn front from its
            /// back end to the head, closing onto the head (StrandShape), at <paramref name="a"/> (the ghost's alpha times its
            /// brightness); in flight, none. No allocation.</summary>
            internal void Painted(Vector3 tip, Vector3 dir, float len, float a, bool held) {
                if (strand == null) return;
                float turns = strandTurns.Value;
                if (!held || a <= 0.01f || turns <= 0f || len <= 0f) {
                    if (strand.enabled) strand.enabled = false;
                    return;
                }
                Vector3 u = Vector3.Cross(dir, Vector3.up);
                if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(dir, Vector3.right);
                u.Normalize();
                Vector3 v = Vector3.Cross(dir, u);
                float gap = ChargeFx.StrandGap, converge = ChargeFx.StrandConverge, spin = Time.time * ChargeFx.StrandSpin;
                Vector3 back = tip - dir * len;
                for (int i = 0; i < StrandPoints; i++) {
                    float s = i / (float)(StrandPoints - 1);
                    float angle = 2f * Mathf.PI * (turns * s + spin);
                    float r = StrandShape.Radius(shaftHalf, gap, s, converge);
                    points[i] = back + dir * (len * s) + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * r;
                }
                strand.SetPositions(points);
                AnimationCurve curve = ChargeFx.StrandTaperCurve;
                if (!ReferenceEquals(curve, strandCurve)) { strand.widthCurve = curve; strandCurve = curve; }
                strand.widthMultiplier = ChargeFx.StrandWidth;
                Color c = Brighter(ChargeFx.StrandColour1, Glow);
                float k = a * Mathf.Max(1f, Glow) * ChargeFx.GlowScale; // [Fill] GlowScale: the one knob on the charge's glow
                strand.startColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(0.5f * k)); // the back end, fainter, as the main strands
                strand.endColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(k));
                if (!strand.enabled) strand.enabled = true;
            }

            /// <summary>Released (Ghost.Fly): the strand goes, the ember trail starts on the head.</summary>
            internal void Flown(Transform head) {
                if (strand != null) strand.enabled = false;
                if (trail != null || head == null) return;
                try { trail = ChargeFx.EmberTrail.AttachTo(head, trailWidthScale.Value); }
                catch (Exception e) {
                    if (!loggedTrailFailure) { loggedTrailFailure = true; MelonLogger.Warning($"[Archery] Heavy arrows: the ember trail could not be built ({e.Message}); heavy arrows fly without it"); }
                }
            }

            /// <summary>The heavy arrow is about to go (Ghost.End): its embers fade out where they are.</summary>
            internal void LetGo() {
                if (trail != null) trail.LetGo();
                trail = null;
            }

            /// <summary>Played instead of the volley's slide-out when heavy arrows slide out (VolleyFx.Beats); false when
            /// SlideSound is empty, so the volley's plays.</summary>
            internal static bool PlaySlide(Vector3 at) {
                string names = slideSound != null ? slideSound.Value : "";
                if (string.IsNullOrWhiteSpace(names)) return false;
                float v = Mathf.Clamp01(slideVolume.Value);
                VolleyFx.Play(names, at, 0f, v);
                if (!loggedSlide) { loggedSlide = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] Heavy arrows: slide-out sound '{names}' at volume {v.ToString("0.##", CultureInfo.InvariantCulture)}"); }
                return true;
            }

            /// <summary>A heavy arrow landed: the Fill's HitEffect at HitEffectScale, with the release's landing sound and light
            /// (VolleyFx.LandFx); gold sparks instead when the effect cannot be had.</summary>
            static readonly int T_Land = Timing.Section("look start Volley.Land");
            internal static void Land(uint release, Vector3 point, Vector3 normal) {
                using var _t = Timing.Begin(T_Land, release);
                bool shown = false;
                float k = hitEffectScale != null ? hitEffectScale.Value : 0.5f;
                if (k > 0f) {
                    try { shown = SpawnHit(point, normal, k); }
                    catch (Exception e) {
                        if (!loggedHitFailure) { loggedHitFailure = true; MelonLogger.Warning($"[Archery] Heavy arrows: the hit effect failed ({e.Message}); gold sparks instead"); }
                    }
                }
                VolleyFx.LandFx.Land(release, point, true, false, !shown);
            }

            // The pooled effect is scaled down while it plays and put back to its own scale when it returns to its pool, so the
            // game's own use of it is never smaller.
            static bool SpawnHit(Vector3 point, Vector3 normal, float k) {
                string names = ChargeFx.HitEffect;
                if (string.IsNullOrWhiteSpace(names)) return false;
                PooledObjectDefinition? def = Fx.FindEffect(names); // a miss is logged by Fx
                if (def == null) return false;
                Quaternion rot = normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(normal) : Quaternion.identity;
                if (!Fx.GetScaled(def, point, rot, k)) return false;
                if (!loggedHit) { loggedHit = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] Heavy arrows: landings show '{def.name}' at {k.ToString("0.##", CultureInfo.InvariantCulture)} size"); }
                return true;
            }
        }
    }
}
