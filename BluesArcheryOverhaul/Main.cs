using System;
using Alta.Inventory;
using Alta.Networking.Scripts.Player;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BluesArcheryOverhaul.Main), "Blue's Archery Overhaul", BluesArcheryOverhaul.Main.Version, "Blue")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace BluesArcheryOverhaul {
    // Archery mod for A Township Tale. One DLL, loaded by the server and by every client.
    //   Main.cs          start-up, and the gold ring that closes on the arrow tip as Quickdraw charges
    //   Tree.cs          skill-tree edits (orbs, links, draw timers, icons)
    //   QuiverGather.cs  shot arrows return to the quiver they came from
    //   Quickdraw4.cs    top Quickdraw orb: an instant straight-line shot that pierces creatures (batch 4b)
    //   VolleySkill.cs   the Volley skill (a copy of Slowdraw) that Two-Shot / Three-Shot run on; the game's glow rule
    //   Volley.cs        the volley's ghosts, echoes and pellets; Fire, where every release starts
    //   VolleyFx.cs      the volley's draw beats and landing effects
    //   Burst.cs         Burst, 30 spectral arrows (rack, the 15 pellet arrows on the bow, pellet flower)
    //   GoldHold.cs      client: a multi-arrow main's Charge Shot orbs, the gold hold stages ring by ring (thump, sweep, shimmer, glint)
    //   HolyArrow.cs     the long-charge beam with a blast, armed on a live draw
    //   Draws.cs         per-bow draw record and clock, DrawGrace, and the release decision (Draws.Release)
    //   Weight.cs        charged hits: stagger, interrupt, knock-down immunity (Charge Shot 3 no longer pierces, batch 4b)
    //   Steady.cs        Quickdraw 1-3: less wobble, faster snaps
    //   ChargeFx.cs      client looks for the Charge line (the Fill, strands, thump, ember trail)
    //   Balance.cs       client: the Balance Ring on creatures the local player hits
    //   SnapWeight.cs    client: a Quickdraw main's Charge Shot orbs shown on the quick shot (ember, trail, hit)
    //   AutoDock.cs      the Quick side orb: self-nocking
    //   Targets.cs, Ricochet.cs   the shot's checked target list and per-release record; ghosts bouncing off terrain
    //   Seek.cs, Homing.cs   Seeking Volley (H1): the lock, the server's check, the homing flight; Unity-free rules
    //   SeekLook.cs, SeekCage.cs, SeekFx.cs   client (H2): the homing looks: the lock's cage, the green bow, bigger arrows and trails, the arrival
    //   Shrine.cs        one buyable ultimate, sale fixes
    //   ShrineView.cs, TreeColours.cs   client: the shrine the local player has open, rings and links on its tree
    //   Tablet.cs        client: the floating stone tablet at the shrine that says what the held orb does to the build
    //   Lectern.cs, Preview.cs   Unity-free, unit-tested: the tablet's stone blocks, layout and motion; its wording
    //   KitHooks.cs      the few hooks the separate play-test kit (BluesArcheryTestKit: test build, test buttons, the range) sets
    //   Skyfall*.cs      the admin sky-rain skill, given only by console command: settings hub, commands, the quiet signal, the server's storm, the looks; Unity-free rules in SkyfallRules.cs
    //   Builds.cs, Claims.cs, StreamBits.cs   build bits and shot claims on the game's own messages
    //   Hits.cs, Fx.cs, Cfg.cs   raycast + damage, line drawing and game-asset lookup, settings
    //   Rules.cs, Build.cs, Wire.cs, Release.cs, Spectral.cs, Charge.cs   Unity-free, unit-tested rules and bit layouts
    //
    // Skill logic runs on the SERVER. A client only knows (a) its own bow string, every frame it is held
    // (BowString.HeldUpdate) and (b) the status messages the game broadcasts for each skill.
    public class Main : MelonMod {
        /// <summary>This build's version (tools/stage-build.sh sets it). The play-test kit is compiled against it and refuses to run
        /// beside any other version.</summary>
        internal const string Version = "0.19.9";
        internal const float TightRadius = 0.012f; // metres, ring size once "absorbed" into the arrow
        static readonly Color DefaultGold = new Color(1f, 0.78f, 0.2f);

        static MelonPreferences_Entry<float> startRadius = null!, lineWidth = null!, alpha = null!;
        static MelonPreferences_Entry<string> ringColour = null!;

        enum State { Drawing, Charging, Ready }

        // ponytail: one ring, one bow. Dual-wielding bows would need per-bow state. Client visual only.
        static GameObject? ring;
        static LineRenderer? line;
        static BowString? bow;
        static State state;
        static float lastSeen, stateSince;
        static bool expired, ownsQuickdraw, loggedTimings;
        static readonly RelaxTimer relax = new RelaxTimer(); // the server's DrawGrace rule, so a twitch it ignores does not re-arm the ring
        static BowQuickdraw? quickdraw;
        static readonly Func<BowString, float> GetStrength =
            AccessTools.MethodDelegate<Func<BowString, float>>(AccessTools.Method(typeof(BowString), "GetStrength", Type.EmptyTypes));

        /// <summary>True while the local player's quickshot is charged and can be fired.</summary>
        internal static bool QuickshotReady => state == State.Ready && !expired;

        // ---------------------------------------------------------------- the old name

        /// <summary>This mod's name up to 0.18.3 (its DLL, settings file and folder were QuickshotRing).</summary>
        internal const string OldName = "QuickshotRing";
        static string? oldCopy;          // the old DLL's path when it is loaded too: this copy then does nothing at all
        static bool patchesBlocked;      // MelonLoader was told not to patch this assembly (else every patch is taken off again)

        /// <summary>The old QuickshotRing.dll is loaded too, so this copy refused to start (no patches, no settings, no callbacks).
        /// Also read by the play-test kit, which then does not start either.</summary>
        internal static bool Refused => oldCopy != null;

        /// <summary>Before MelonLoader applies this mod's Harmony patches: with the old DLL loaded as well every patch would run twice,
        /// so this copy stands aside and the old one runs alone until its file is deleted. Every mod's assembly is loaded before any
        /// is registered, so the old one is found whichever registers first.</summary>
        public override void OnEarlyInitializeMelon() {
            oldCopy = FindOldCopy();
            if (oldCopy == null) return;
            try { AccessTools.Property(typeof(MelonAssembly), nameof(MelonAssembly.HarmonyDontPatchAll)).SetValue(MelonAssembly, true, null); patchesBlocked = MelonAssembly.HarmonyDontPatchAll; }
            catch (Exception) { patchesBlocked = false; }
        }

        static string? FindOldCopy() {
            try {
                foreach (MelonAssembly ma in MelonAssembly.LoadedAssemblies)
                    if (ma?.Assembly != null && ma.Assembly.GetName().Name == OldName) return string.IsNullOrEmpty(ma.Location) ? OldPath() : ma.Location;
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == OldName) return string.IsNullOrEmpty(a.Location) ? OldPath() : a.Location;
                foreach (MelonBase m in MelonBase.RegisteredMelons)
                    if (m.Info?.Name == OldName) return OldPath();
            }
            catch (Exception) { }
            return null;
        }

        static string OldPath() => System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, OldName + ".dll");

        public override void OnLateInitializeMelon() {
            if (Refused) return;
            Weight.LogPatches();
        }

        public override void OnInitializeMelon() {
            if (Refused) {
                if (!patchesBlocked) HarmonyInstance.UnpatchSelf();
                LoggerInstance.Error($"[Archery] Blue's Archery Overhaul v{Info.Version} DID NOT START: the old {OldName}.dll is installed too ({oldCopy}). Delete that file (this mod replaces it) and restart. Until then only the old {OldName} runs; nothing of this one was patched or loaded.");
                return;
            }
            LoggerInstance.Msg($"[Archery] Blue's Archery Overhaul v{Info.Version} ({(ApplicationManager.IsHeadless ? "server" : "client")})");
            Cfg.CarryOverOldName(); // before anything reads a setting: UserData\QuickshotRing.cfg and UserData\QuickshotRing\ copied to the new names when those do not exist yet

            Logs.Init(); // first: every later line reads [Log] Level
            TimingHost.Init(); // G5: [Timing], switched on after Cfg.Ready
            var cat = Cfg.Category("QuickshotRing");
            startRadius = cat.CreateEntry("StartRadius", 0.07f);
            lineWidth = cat.CreateEntry("LineWidth", 0.004f);
            alpha = cat.CreateEntry("Alpha", 0.6f);
            ringColour = cat.CreateEntry("Colour", "#FFC733");
            Tree.Init();
            QuiverGather.Init();
            Quickdraw4.Init();
            Volley.Init();
            Draws.Init();
            HolyArrow.Init();
            Weight.Init();
            Steady.Init();
            ChargeFx.Init();
            Balance.Init();
            SnapWeight.Init();
            AutoDock.Init();
            Targets.Init();
            Ricochet.Init();
            Shatter.Init();
            Seek.Init(); // H1: Seeking Volley's [SeekingVolley] settings; H2: its looks' [SeekLook] (SeekerShare is Paths')
            Pvp.Init();
            Builds.Init(); // a settings reload refreshes every known build (where the test build's reload was; the test tools are the BluesArcheryTestKit mod)
            Paths.Init();
            Claims.Init();
            Tutorial.Init();
            ShrineView.Init();
            TreeColours.Init();
            Tablet.Init();
            Skyfall.Init(); // Skyfall, the admin skill ([Skyfall]); before Cfg.Ready, which records, migrates and checksums only the categories that exist then
            Telemetry.Init(); // balance telemetry ([Telemetry], server, not shared)
            JoinGate.Init(); // the join gate ([Join], server, not shared)
            ServerSettings.Init(); // server-set settings: sees MelonLoader's file loads and its quit (no entries of its own)
            Cfg.Ready();
            TimingHost.Refresh(); // G5: after any reset or migration; measures its own overhead once when [Timing] Enabled
            if (ApplicationManager.IsHeadless) SkyfallCommands.Register(); // Skyfall: the skyfall console module, on the dedicated server only (its own try/catch)
            VolleySkill.TryRegister("start"); // retried on every scene and at the tree load until it works
            SelfCheck.Run("start-up"); // fixF: every settings-derived value and the tablet's text, once, so a cycle shows here
            Cfg.Reloaded += () => SelfCheck.Run("settings reload"); // subscribed last: runs after every other reload handler

            if (Progress(0f, .7f, false, 0f, 2f) != 0f || Progress(.7f, .7f, false, 0f, 2f) != .5f ||
                Progress(1f, .7f, true, 2f, 2f) != 1f || Progress(.35f, .7f, false, 0f, 0f) != .5f ||
                VolleyTimeline.Ghosts(3, true, 60f, VolleyTimes.Default) != VolleyMath.Total || BlastMath.Fraction(0f, 1.5f, 5f, 0.5f) != 1f)
                LoggerInstance.Error("[Archery] Start-up self-check failed.");
        }

        public override void OnApplicationQuit() {
            if (Refused) return;
            ServerSettings.Quit(); // first: a client holding a server's settings puts the player's own back before MelonLoader's quit save (never throws)
            using (Timing.Begin(TQuit)) {
                try { AutoDock.Shutdown(); } // fix batch D: the try-out quiver and its arrows never outlive the server
                catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: shutdown clean-up failed: {e.Message}"); }
                try { Skyfall.Held.Shutdown(); } // Skyfall: a held real arrow never outlives the server
                catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: held arrows at shutdown: {e.Message}"); }
                try { SkyfallServer.Reset(); } // Skyfall: landings still waiting are dropped
                catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: shutdown clean-up failed: {e.Message}"); }
            }
            try { Telemetry.Shutdown(); } // balance telemetry: the last rows are written (waits at most 2 s for a running write)
            catch (Exception e) { MelonLogger.Warning($"[Archery] Telemetry: shutdown failed: {e.Message}"); }
            TimingHost.Shutdown(); // G5: the last interval's timing report (never throws)
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) {
            if (Refused) return;
            using var _t = Timing.Begin(TScene, sceneName);
            if (!VolleySkill.Registered) VolleySkill.TryRegister($"scene {sceneName}");
        }

        // G5: one section per routine Main's callbacks run (Timing.cs; TimingHost.Frame closes each frame first)
        static readonly int TScene = Timing.Section("Main.OnSceneWasInitialized / VolleySkill.TryRegister"),
            TQuiver = Timing.Section("Main.OnUpdate / QuiverGather.Poll"),
            TAutoPoll = Timing.Section("Main.OnUpdate / AutoDock.Poll"),
            TWeight = Timing.Section("Main.OnUpdate / Weight.Tick"),
            TBounce = Timing.Section("Main.OnUpdate / Ricochet.BounceTick (waiting bounces)"),
            TPump = Timing.Section("Main.OnUpdate / VolleyFx.Pump"),
            TBalance = Timing.Section("Main.OnUpdate / Balance.Frame"),
            TWatch = Timing.Section("Main.OnUpdate / Hits.WatchTick"),
            TSeek = Timing.Section("Main.OnUpdate / Seek.Tick (held real arrows)"),
            TSkyfall = Timing.Section("Main.OnUpdate / Skyfall.Tick (held real arrows, storm records)"),
            TSkyLink = Timing.Section("Main.OnUpdate / SkyfallLink.Tick (signal queue, rejoins, latch)"),
            TSkyServer = Timing.Section("Main.OnUpdate / SkyfallServer.Tick (landings due)"),
            TTelemetry = Timing.Section("Main.OnUpdate / Telemetry.Tick (hand rows to the writer)"),
            TJoin = Timing.Section("Main.OnUpdate / JoinGate.Tick (add the join step once)"),
            TClientLoad = Timing.Section("Main.OnUpdate / Tree.ClientLoad"),
            TCfgPoll = Timing.Section("Main.OnUpdate / Cfg.Poll (file stamp, reload)"),
            TIcons = Timing.Section("Main.OnUpdate / Tree.PollIcons"),
            TRing = Timing.Section("Main.OnUpdate / ring check"),
            TDrawHeld = Timing.Section("Main.OnLateUpdate / Volley.DrawHeld"),
            TFill = Timing.Section("Main.OnLateUpdate / ChargeFx.Frame"),
            TSnap = Timing.Section("Main.OnLateUpdate / SnapWeight.Frame"),
            TAutoFrame = Timing.Section("Main.OnLateUpdate / AutoDock.Frame"),
            TShrine = Timing.Section("Main.OnLateUpdate / ShrineView.Frame"),
            TColours = Timing.Section("Main.OnLateUpdate / TreeColours.Frame"),
            TTablet = Timing.Section("Main.OnLateUpdate / Tablet.Frame"),
            TSkyHold = Timing.Section("Main.OnLateUpdate / SkyfallHold.Tick"),
            TSkySky = Timing.Section("Main.OnLateUpdate / SkyfallSky.Frame"),
            TSkyLook = Timing.Section("Main.OnLateUpdate / SkyfallLook.Tick"),
            TSkyNail = Timing.Section("Main.OnLateUpdate / SkyfallNail.Tick"),
            TQuit = Timing.Section("Main.OnApplicationQuit / AutoDock + Skyfall shutdown"),
            TSkillMessage = Timing.Section("patch PickupBasedSkill.SendSerializedMessage / Main.SkillMessage"),
            TDraw = Timing.Section("patch BowString.HeldUpdate / Main.Draw (the ring)");

        // 0 = ring fully open, 1 = closed on the arrow. First the draw distance closes it, then the hold timer.
        // If the skill has no hold timer, draw distance alone closes it.
        internal static float Progress(float pull, float pullNeeded, bool charging, float heldFor, float holdNeeded) {
            float drawShare = holdNeeded > 0f ? 0.5f : 1f;
            if (!charging) return pullNeeded > 0f ? drawShare * Mathf.Clamp01(pull / pullNeeded) : drawShare;
            return holdNeeded > 0f ? drawShare + (1f - drawShare) * Mathf.Clamp01(heldFor / holdNeeded) : 1f;
        }

        float nextSlowPoll;

        public override void OnUpdate() {
            if (Refused) return;
            TimingHost.Frame(); // G5: first: closes the last frame (its time and the mod's share), spike lines, the report when due
            using (Timing.Begin(TQuiver)) QuiverGather.Poll();
            try { using (Timing.Begin(TAutoPoll)) AutoDock.Poll(); } // server: queued docks (before the headless server's early return below)
            catch (Exception e) { MelonLogger.Error($"[Archery] Auto Dock: poll failed: {e.Message}"); }
            try { using (Timing.Begin(TWeight)) Weight.Tick(); } // here, not below: the headless server returns early further down (no ring)
            catch (Exception e) { MelonLogger.Error($"[Archery] Weight: tick failed: {e.Message}"); }
            // H5c, server: Ricochet's bounces whose arc has landed (here, not in the spectral tick: that stops once no spectral arrow
            // flies, and a bounce usually comes after the volley's last one; one count test while none waits)
            try { using (Timing.Begin(TBounce)) Ricochet.BounceTick(); }
            catch (Exception e) { MelonLogger.Error($"[Archery] Ricochet: bounce tick failed: {e.Message}"); }
            try { using (Timing.Begin(TPump)) VolleyFx.Pump(); } // the volley's queued haptic pulses and delayed sounds
            catch (Exception e) { MelonLogger.Error($"[Archery] Volley FX: pulse pump failed: {e.Message}"); }
            try { using (Timing.Begin(TBalance)) Balance.Frame(); } // the Balance Ring (clients; returns at once on the server)
            catch (Exception e) { MelonLogger.Error($"[Archery] Balance: frame failed: {e.Message}"); }
            try { using (Timing.Begin(TWatch)) Hits.WatchTick(); } // G3, server, [Log] Level Debug only: the hand-made hits' 0.5 s re-check (returns at once otherwise)
            catch (Exception e) { MelonLogger.Error($"[Archery] Hits: watch failed: {e.Message}"); }
            try { using (Timing.Begin(TSeek)) Seek.Tick(); } // H1, every machine: a held real arrow whose core cluster never ended is let go
            catch (Exception e) { MelonLogger.Error($"[Archery] Seek: tick failed: {e.Message}"); }
            try { using (Timing.Begin(TSkyfall)) Skyfall.Tick(); } // Skyfall, every machine: held real arrows (the server destroys them 1 s after the release) and storm records
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: tick failed: {e.Message}"); }
            try { using (Timing.Begin(TSkyLink)) SkyfallLink.Tick(); } // Skyfall: server: the quiet signal to holders (queued sends, rejoins); client: the latch is dropped on leaving
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: signal tick failed: {e.Message}"); }
            try { using (Timing.Begin(TSkyServer)) SkyfallServer.Tick(); } // Skyfall, server: landings due (blasts, dome kills, festival care; one count test while none waits)
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: server tick failed: {e.Message}"); }
            try { using (Timing.Begin(TJoin)) JoinGate.Tick(); } // the join gate, server: adds its step to the game's join sequence once (one reference test otherwise)
            catch (Exception e) { MelonLogger.Error($"[Archery] Join: tick failed: {e.Message}"); }
            try { ServerSettings.Tick(); } // server-set settings: server: parts to send; client: a whole send to apply, leaving the server (one flag test otherwise; its own timing section)
            catch (Exception e) { MelonLogger.Error($"[Archery] Settings: tick failed: {e.Message}"); }
            try { using (Timing.Begin(TTelemetry)) Telemetry.Tick(); } // balance telemetry, server: every 5 s the queued rows go to the writer's thread
            catch (Exception e) { MelonLogger.Error($"[Archery] Telemetry: tick failed: {e.Message}"); }
            using (Timing.Begin(TClientLoad)) Tree.ClientLoad(); // G3: a client loads the archery tree once it has joined (once a second until it is in; nothing after)
            if (Time.unscaledTime >= nextSlowPoll) {
                nextSlowPoll = Time.unscaledTime + 2f;
                using (Timing.Begin(TCfgPoll)) Cfg.Poll();
                using (Timing.Begin(TIcons)) Tree.PollIcons();
            }
            if (ring == null || !ring.activeSelf) return;
            using var _t = Timing.Begin(TRing);
            // HeldUpdate runs every frame the string is held; if it stopped, or the arrow left, we're done.
            if (Time.time - lastSeen > 0.2f || bow == null || !bow.StringDock.HasDockedPickup || !bow.Pickup.IsInteractedWith)
                Hide();
        }

        public override void OnLateUpdate() {
            if (Refused) return;
            using (Timing.Begin(TDrawHeld)) Volley.DrawHeld();
            try { using (Timing.Begin(TFill)) ChargeFx.Frame(); } // after the ghosts: the Fill follows the same arrow pose
            catch (Exception e) { MelonLogger.Error($"[Archery] Fill: frame failed: {e.Message}"); }
            if (ApplicationManager.IsHeadless) return;
            try { using (Timing.Begin(TSnap)) SnapWeight.Frame(); } // the ember on a Quick main's arrowhead (clients), after the arrow's pose is final
            catch (Exception e) { MelonLogger.Error($"[Archery] Snap weight: frame failed: {e.Message}"); }
            try { using (Timing.Begin(TAutoFrame)) AutoDock.Frame(); } // the quiver-to-string ghost (clients)
            catch (Exception e) { MelonLogger.Error($"[Archery] Auto Dock: frame failed: {e.Message}"); }
            try { using (Timing.Begin(TShrine)) ShrineView.Frame(); using (Timing.Begin(TColours)) TreeColours.Frame(); } // the archery shrine's tree colours (clients)
            catch (Exception e) { MelonLogger.Error($"[Archery] Tree colours: frame failed: {e.Message}"); }
            try { using (Timing.Begin(TTablet)) Tablet.Frame(); } // the shrine's floating test tablet (clients)
            catch (Exception e) { MelonLogger.Error($"[Archery] Tablet: frame failed: {e.Message}"); }
            try { using (Timing.Begin(TSkyLook)) SkyfallLook.Tick(); } // Skyfall, every storm's look on this machine (sigils, rain, explosions, scorches)
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: storm look failed: {e.Message}"); }
            try { using (Timing.Begin(TSkyNail)) SkyfallNail.Tick(); } // Skyfall, the World-Nail and the sigil's shatter (after the storm look, which hands it the sigil)
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: World-Nail look failed: {e.Message}"); }
            try { using (Timing.Begin(TSkyHold)) SkyfallHold.Tick(); } // Skyfall, the caster's hold looks, drawn from the arrow's pose this frame
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: hold looks failed: {e.Message}"); }
            try { using (Timing.Begin(TSkySky)) SkyfallSky.Frame(); } // Skyfall, the sky dial: last, after every look has asked for its dim
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: sky dial failed: {e.Message}"); }
        }

        static void Hide() {
            if (ring == null) return;
            SnapWeight.RingHidden(); // the ember on the arrowhead shows only with the ring
            ring.transform.SetParent(null, false); // never leave it on a (pooled/destroyed) arrow
            ring.SetActive(false);
        }

        static void SetState(State s) { state = s; stateSince = Time.time; }

        static readonly int T_FindQuickdraw = Timing.Section("asset Main.FindQuickdraw search (FindObjectsOfTypeAll)");
        static BowQuickdraw? FindQuickdraw() {
            if (quickdraw == null) {
                using var _t = Timing.Begin(T_FindQuickdraw); // G5: the search only, not the cached return every held frame
                var all = Resources.FindObjectsOfTypeAll<BowQuickdraw>();
                if (all.Length > 0) quickdraw = all[0];
            }
            return quickdraw;
        }

        static readonly int T_EnsureRing = Timing.Section("look Main.EnsureRing");
        static bool EnsureRing() {
            using var _t = Timing.Begin(T_EnsureRing);
            if (ring != null) return true;
            Material? mat = Fx.LineMaterial();
            if (mat == null) return false;
            ring = new GameObject("QuickshotRing");
            line = ring.AddComponent<LineRenderer>();
            Fx.BuildCircle(line);
            line.material = mat;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return true;
        }

        // Server -> everyone: "this player's Quickdraw is now charging / charged / cancelled / used".
        [HarmonyPatch(typeof(PickupBasedSkill), nameof(PickupBasedSkill.SendSerializedMessage))]
        static class SkillMessage {
            static void Postfix(PickupBasedSkill __instance, IPlayer __0, Alta.Serialization.Stream __1, ToolBasedSkillStatus ___status) {
                using var _t = Timing.Begin(TSkillMessage);
                if (!__1.IsReading || !(__instance is BowQuickdraw) || __0 == null || !__0.IsLocalPlayer) return;
                ownsQuickdraw = true;
                switch (___status) {
                    case ToolBasedSkillStatus.ChargingStarted: expired = false; SetState(State.Charging); break;
                    case ToolBasedSkillStatus.Charged: expired = false; SetState(State.Ready); break;
                    default:
                        // Cancelled (string relaxed, or the window ran out) or Consumed (shot).
                        // If the string is still fully drawn the window was missed: stay hidden until it is relaxed.
                        expired = true;
                        SetState(State.Drawing);
                        break;
                }
            }
        }

        // Runs on the client every frame the local player holds a bow string.
        [HarmonyPatch(typeof(BowString), "HeldUpdate")]
        static class Draw {
            static void Postfix(BowString __instance, Interactor ___interactorHoldingBowString) {
                using var _t = Timing.Begin(TDraw);
                if (___interactorHoldingBowString == null || !___interactorHoldingBowString.IsLocal) return;

                BowQuickdraw? skill = FindQuickdraw();
                var settings = skill?.InputStructure as BowStringPullbackStructure;
                if (skill == null || settings == null) return;

                if (!ownsQuickdraw && Player.Current is Player me && me.ProgressionManager != null)
                    ownsQuickdraw = me.ProgressionManager.SkillUnlocked(skill);
                if (!ownsQuickdraw) return;

                PickupDock dock = __instance.StringDock;
                if (dock == null || !dock.HasDockedPickup) { Hide(); SetState(State.Drawing); expired = false; relax.Reset(); return; }

                if (!loggedTimings) {
                    loggedTimings = true;
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Quickdraw: pull>{settings.PullbackValue}, hold {settings.MinTimeToPullback}s, " +
                                    $"window {(settings.IsTimingOut ? settings.TimeoutDuration + "s" : "unlimited")}");
                }

                float pull = GetStrength(__instance);
                float now = Time.time;
                if (relax.Relaxed(pull <= settings.PullbackValue, now, Volley.RelaxGrace)) {
                    // Relaxed below the threshold for longer than the server's grace: the server has cancelled (or will);
                    // a new attempt may start. A shorter dip changes nothing, on the server or here.
                    expired = false;
                    if (state != State.Drawing) SetState(State.Drawing);
                }
                // Only a Quick main (or no main path) has the quick shot, so only they see its ring.
                Build mine = Builds.LocalBuild();
                if (expired || !Resolver.OnPath(mine.Main, MainPath.Quick) || !EnsureRing()) {
                    // The quick window was missed with the arrow still drawn (every frame until the string relaxes): nothing charges
                    // for a quick archer, so the Charge Shot orbs' ember is seen to let go (G4; nothing without those orbs).
                    if (expired) {
                        try { SnapWeight.LetGo(__instance); }
                        catch (Exception e) { MelonLogger.Error($"[Archery] Snap weight: letting go failed: {e.Message}"); }
                    }
                    Hide();
                    return;
                }

                Pickup arrowPickup = dock.DockedPickup;
                Arrow arrow = arrowPickup.GetComponent<Arrow>();
                // ponytail: arrows without an ArrowTip get the ring at the arrow's origin, not its front.
                Transform tip = arrow != null && arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrowPickup.transform;

                float radius = TightRadius, a = 1f, closed = 1f; // closed: how far the ring has closed, for the ember (SnapWeight)
                bool ready = state == State.Ready && !expired;
                if (state == State.Ready) {
                    // Absorbed: solid on the arrow, draining as the quickshot window runs out.
                    if (settings.IsTimingOut && settings.TimeoutDuration > 0f)
                        a = Mathf.Lerp(1f, 0.25f, (now - stateSince) / settings.TimeoutDuration);
                    closed = a; // charged: full, draining with the window as the ring does
                }
                else {
                    float p = Progress(pull, settings.PullbackValue, state == State.Charging, now - stateSince, settings.MinTimeToPullback);
                    radius = Mathf.Lerp(startRadius.Value, TightRadius, p);
                    a = alpha.Value;
                    closed = p;
                }

                Transform t = ring!.transform;
                if (t.parent != tip) t.SetParent(tip, false); // parented so it can't lag behind the hand in VR
                t.localPosition = Vector3.zero;
                t.rotation = Quaternion.LookRotation(arrowPickup.transform.forward); // docked arrows point +Z at the bow hand
                float parentScale = tip.lossyScale.x;
                t.localScale = Vector3.one * (radius / (parentScale > 0f ? parentScale : 1f));

                line!.widthMultiplier = lineWidth.Value;
                // Charged with Quickdraw 4 owned: the ring takes that shot's colour to say "this one is a line shot".
                Color c = ready && Quickdraw4.LocalPlayerHasIt() ? Quickdraw4.Colour : Cfg.Colour(ringColour, DefaultGold);
                line.startColor = line.endColor = new Color(c.r, c.g, c.b, a);
                ring.SetActive(true);
                bow = __instance;
                lastSeen = now;
                // A Quick main's Charge Shot orbs: the orange ember on the arrowhead while the ring shows (looks only).
                try { SnapWeight.Ring(__instance, mine, closed, radius, ready); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Snap weight: ember failed: {e.Message}"); }
            }
        }
    }
}
