using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    /// <summary>One per bow string: its draw clock and what its last release was.</summary>
    internal sealed class DrawRecord {
        public readonly DrawClock Clock = new DrawClock();
        public Resolution? Last;
        public uint LastArrow;
        public float LastShotAt = -1f;
        public int GameResetsLogged;
        // fix batch D (diagnosis-holy.md), server: the last pull seen and when the snap detector last logged this string
        public float LastPull = float.NaN, SnapLoggedAt = float.NaN;
    }

    // The draw record per bow string and the release. Every release is decided once, here, by the resolver
    // (Release.cs): on the server from the game's own input state, on the shooting client from what it knows, on a
    // spectator from the server's relayed claim. The draw clock, the grace on a short dip, and resetting every
    // input when the arrow leaves (an input still in range would otherwise keep timing into the next arrow).
    internal static class Draws {
        const int RecordCap = 64, ReleaseCap = 64, GameLinesPerShot = 4;
        const float GameLogSeconds = 10f;

        static readonly Func<BowString, float, float> GetStrength =
            AccessTools.MethodDelegate<Func<BowString, float, float>>(AccessTools.Method(typeof(BowString), "GetStrength", new[] { typeof(float) }));

#if QUEST
        static readonly Dictionary<BowString, DrawRecord> records = new Dictionary<BowString, DrawRecord>(Il.ByPointer<BowString>());
#else
        static readonly Dictionary<BowString, DrawRecord> records = new Dictionary<BowString, DrawRecord>();
#endif
        static readonly List<BowString> dead = new List<BowString>();
        static readonly Dictionary<uint, Resolution> releases = new Dictionary<uint, Resolution>();
        static readonly Dictionary<uint, Build> releaseBuilds = new Dictionary<uint, Build>(); // the build each release was decided with
        static readonly HashSet<string> livePatches = new HashSet<string>();
#if QUEST
        // Quest: BowDrawSkill.inputs and BowStringPullbackInputMethod.isInPullbackRange are proxy properties, read directly below
#else
        static AccessTools.FieldRef<BowDrawSkill, Dictionary<BowString, ISkillInputMethod>>? inputsRef;
        static AccessTools.FieldRef<BowStringPullbackInputMethod, bool>? inRangeRef;
#endif
        static bool warnedClock, warnedInputs;
        static bool ours; // set while this mod resets the inputs on a shot, so the game's own resets can be told apart

        static int Ms(float seconds) => (int)Math.Round(seconds * 1000);

        static MelonPreferences_Entry<float>? serverDipGrace;

        internal static void Init() {
            var cat = Cfg.Category("Draw");
            serverDipGrace = cat.CreateEntry("ServerDipGraceSeconds", DipGraceRule.DefaultServerSeconds, description: "Server. How long the server's copy of a held string may read at or below the draw threshold before a bow skill (Charge Shot, Holy Arrow, Quickdraw, the volley) is lost; the server's draw clock and Holy Arrow's arming use the same time. Raise it (about 1.5) if long holds lose their charge while the archer's own view shows no let-down; the cost is that a deliberate let-down without releasing cancels later, and re-arming a quick shot by relaxing and drawing again takes this long on the server while the archer's ring re-arms after [Volley] RelaxGraceSeconds (0.35): a re-drawn snap inside that gap shows ready but is not a quick shot. Re-read live. Clients keep [Volley] RelaxGraceSeconds for their ring.");
        }

        /// <summary>The dip a held string may make before a bow skill is lost on this machine: on the server [Draw]
        /// ServerDipGraceSeconds (fix batch D, used by DrawGrace, the draw clock and Holy Arrow's arming alike), on a client the
        /// ring's [Volley] RelaxGraceSeconds.</summary>
        internal static float DipGrace => NetworkSceneManager.IsServer
            ? DipGraceRule.Server(serverDipGrace != null ? serverDipGrace.Value : DipGraceRule.DefaultServerSeconds)
            : Volley.RelaxGrace;

        // G2: one flag per patch (no string hashed per frame); the set still says each patch once.
        static bool live0, live1, live2, live3, live4, live5, live6;
        static void Live(ref bool said, string patch) {
            if (said) return;
            said = true;
            if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Draw: patch on {patch} is live");
        }

        internal static DrawRecord Get(BowString bow) {
            if (!records.TryGetValue(bow, out DrawRecord r)) {
                if (records.Count > RecordCap) {
                    foreach (BowString key in records.Keys) if (key == null) dead.Add(key!); // destroyed, not a null reference
                    foreach (BowString key in dead) records.Remove(key);
                    dead.Clear();
                }
                records[bow] = r = new DrawRecord();
            }
            return r;
        }

        /// <summary>Seconds since this bow string's 68% crossing, or null when there was none (or it ended over Grace ago).</summary>
        internal static float? Elapsed(BowString? bow) =>
            bow != null && records.TryGetValue(bow, out DrawRecord r) && r.Clock.TryElapsed(Time.time, DrawClock.Grace, out float s) ? s : (float?)null;

        /// <summary>The arrow that was on this bow string at its current draw's 68% crossing (0 when none or unknown).</summary>
        internal static uint ArrowAtCrossing(BowString? bow) =>
            bow != null && records.TryGetValue(bow, out DrawRecord r) ? r.Clock.ArrowAtCrossing : 0u;

        internal static int? ElapsedMs(BowString? bow) {
            float? s = Elapsed(bow);
            return s.HasValue ? Ms(s.Value) : (int?)null;
        }

        /// <summary>The release decided for this arrow on this machine, if it is still remembered.</summary>
        internal static Resolution? ReleaseOf(uint arrowId) => releases.TryGetValue(arrowId, out Resolution r) ? r : (Resolution?)null;

#if QUEST
        static Il2CppSystem.Collections.Generic.Dictionary<BowString, ISkillInputMethod>? InputsOf(BowDrawSkill skill) {
            try {
                return skill.inputs;
            }
#else
        static Dictionary<BowString, ISkillInputMethod>? InputsOf(BowDrawSkill skill) {
            try {
                inputsRef ??= AccessTools.FieldRefAccess<BowDrawSkill, Dictionary<BowString, ISkillInputMethod>>("inputs");
                return inputsRef(skill);
            }
#endif
            catch (Exception e) {
                if (!warnedInputs) { warnedInputs = true; MelonLogger.Error($"[Archery] Draw: cannot read the bow skills' inputs; releases read as uncharged: {e.Message}"); }
                return null;
            }
        }

        static bool InRange(BowStringPullbackInputMethod input) {
            try {
#if QUEST
                return input.isInPullbackRange;
#else
                inRangeRef ??= AccessTools.FieldRefAccess<BowStringPullbackInputMethod, bool>("isInPullbackRange");
                return inRangeRef(input);
#endif
            }
            catch { return false; }
        }

        /// <summary>Server: this skill's input on this bow string is active (charged, or inside Quickdraw's window).</summary>
        internal static bool InputActive(BowDrawSkill? skill, BowString bow) {
            if (skill == null || bow == null) return false;
            Dictionary<BowString, ISkillInputMethod>? map = InputsOf(skill);
            return map != null && map.TryGetValue(bow, out ISkillInputMethod input) && input != null && input.IsActive;
        }

        // ---------------------------------------------------------------- the release

        /// <summary>Quickdraw's window in milliseconds (its pull settings' timeout), or 1 000 000 when it does not time out
        /// or its settings are missing.</summary>
        internal static int QuickdrawWindowMs {
            get {
                var settings = Tree.Quickdraw != null ? Tree.Quickdraw.InputStructure as BowStringPullbackStructure : null;
                return settings != null && settings.IsTimingOut ? Ms(settings.TimeoutDuration) : 1000000;
            }
        }

        /// <summary>Server, the echo fix: the milliseconds since the crossing this arrow's release is decided with (the server's own
        /// draw clock, or the shooter's claimed time when it is ahead by at most [Claim] TrustMs), or null when neither is known.</summary>
        internal static int? ServerTime(BowString bow, uint arrowId, out int? serverMs, out int? claimedMs) {
            serverMs = ElapsedMs(bow);
            claimedMs = Claims.ClaimedMs(arrowId);
            return ClaimTrust.EffectiveMs(serverMs, claimedMs, Claims.TrustMs);
        }

        /// <summary>The game says charged (its timer is shared by every player and set to the fastest build's), and this player's
        /// own stage 1 has passed on the mod's clock. An unknown time trusts the game (a spectator has no clock: the server holds
        /// the Charged status back until the shooter's own time, OwnTime).</summary>
        internal static bool ChargeReady(bool gameCharged, Build build, float? t) =>
            gameCharged && (!t.HasValue || Complements.Reached(t, Paths.StagesFor(build).S1));

        static readonly int T_ServerFacts = Timing.Section("Draws.ServerFacts");
        static ReleaseFacts ServerFacts(BowString bow, Player? owner, uint arrowId) {
            using var _t = Timing.Begin(T_ServerFacts, arrowId);
            int? effective = ServerTime(bow, arrowId, out _, out int? claimedMs);
            float? t = effective.HasValue ? effective.Value / 1000f : (float?)null;
            Build build = Builds.ServerBuild(owner);
            bool charge = ChargeReady(InputActive(Tree.Slowdraw, bow), build, t);
            ShotKind? claimedKind = Claims.ClaimedKind(arrowId);
            return new ReleaseFacts {
                SnapReady = ClaimTrust.SnapReady(InputActive(Tree.Quickdraw, bow), claimedKind, claimedMs, effective, QuickdrawWindowMs),
                ChargeActive = charge,
                HolyArmed = HolyArrow.IsArmed(bow),
                Ghosts = Volley.Ghosts(bow, t),
                Needles = Complements.Needles(build, Paths.Values),
                Heavy = Complements.HeavyArrows(build, Paths.Values),
                Racked = Volley.Racked(bow, t),
                Crossed = t != null,
                Seconds = t, Stages = Paths.StagesFor(build),
                Build = build,
            };
        }

        // `mine`: the local player is the shooter. A spectator never takes the local player's snap or build.
        static readonly int T_ClientFacts = Timing.Section("Draws.ClientFacts");
        static ReleaseFacts ClientFacts(BowString bow, bool mine, string? shooterName) {
            using var _t = Timing.Begin(T_ClientFacts, shooterName);
            Build build = mine ? Builds.LocalBuild() : Builds.ClientBuild(shooterName);
            float? elapsed = Elapsed(bow);
            bool charge = ChargeReady(Volley.ChargedOnClient(bow), build, elapsed);
            return new ReleaseFacts {
                SnapReady = mine && Main.QuickshotReady,
                ChargeActive = charge,
                HolyArmed = mine && HolyArrow.ArmedOnClient(bow), // a spectator follows the server's relayed claim
                Ghosts = Volley.Ghosts(bow),
                Needles = Complements.Needles(build, Paths.Values),
                Heavy = Complements.HeavyArrows(build, Paths.Values),
                Racked = Volley.Racked(bow),
                Crossed = elapsed != null,
                Seconds = elapsed, Stages = Paths.StagesFor(build),
                Build = build,
            };
        }

        internal static Resolution ResolveServer(BowString bow, Player? owner, uint arrowId) => Resolver.Resolve(ServerFacts(bow, owner, arrowId));

        /// <summary>Shooting client: the release as this machine sees it (the shot claim carries it to the server).</summary>
        internal static Resolution ResolveLocal(BowString bow) => Resolver.Resolve(ClientFacts(bow, true, null));

        static PlayerController? ShooterOf(BowString bow, NetworkEntity? arrow) {
            PlayerController pc = null!;
            Pickup p = arrow != null ? arrow.CommonPickup : null!;
            if (p != null) pc = p.LastPlayerInteractor;
            if (pc == null && bow.Pickup != null) pc = bow.Pickup.LastPlayerInteractor;
            return pc != null ? pc : null;
        }

        /// <summary>Every machine, the moment an arrow leaves the bow (Volley.Fire): decide the release once, remember it,
        /// hand the server's decision to the relay, and log it.</summary>
        internal static Resolution Release(BowString bow, NetworkEntity? arrow) {
            uint id = arrow != null ? arrow.Identifier : 0u;
            bool server = NetworkSceneManager.IsServer;
            int? ms = ElapsedMs(bow);
            bool test = false, local = false;
            Player? owner = null;
            Resolution r;
            Build build;
            string side, name;
            SkyCode sky = SkyCode.None;   // Skyfall (admin skill): decided first; a cast or fizzle is the neutral release to everything below

            if (server) {
                // The string's last interactor is the archer (brief); the arrow's is the same player, set by Shooter.Shoot.
                PlayerController? pc = bow.Pickup != null && bow.Pickup.LastPlayerInteractor != null ? bow.Pickup.LastPlayerInteractor : ShooterOf(bow, arrow);
                owner = pc != null ? pc.NetworkPlayer : null;
                name = Builds.NameOf(owner);
                ms = ServerTime(bow, id, out _, out int? claimedMs); // the time the server decides with (the echo fix)
                if (arrow != null) sky = Skyfall.DecideServer(arrow, owner, ms, claimedMs);
                r = sky != SkyCode.None ? SkyfallSeam.Neutral : ResolveServer(bow, owner, id);
                build = Builds.ServerBuild(owner);
                test = Builds.TestBuildFor(name);
                side = sky != SkyCode.None ? "Release (Skyfall " + sky + ")" : "Release";
            }
            else {
                PlayerController? pc = ShooterOf(bow, arrow);
                Player? who = pc != null ? pc.NetworkPlayer : null;
                name = Builds.NameOf(who);
                if (pc != null && pc.IsLocalPlayer) {
                    local = true;
                    if (arrow != null) sky = Skyfall.DecideShooter(arrow);
                    r = sky != SkyCode.None ? SkyfallSeam.Neutral : ResolveLocal(bow);
                    build = Builds.LocalBuild();
                    side = sky != SkyCode.None ? "Release (local, Skyfall " + sky + ")" : "Release (local)";
                }
                else {
                    build = Builds.ClientBuild(name);
                    side = "Release (seen)";
                    bool relayed = Claims.TryRelay(id, out ShotClaim? c) && c != null;
                    if (arrow != null) sky = Skyfall.DecideSeen(arrow, relayed ? c : null, who);
                    if (sky != SkyCode.None) { r = SkyfallSeam.Neutral; ms = c!.TimeMs; side = "Release (seen, Skyfall " + sky + ")"; }
                    else if (relayed && c != null) {
                        // The relayed time is the one the server decided with. A Line is relayed as Snap (SeenUltimate);
                        // the charge tier comes from the shooter's build.
                        bool charged = c.Kind == ShotKind.Charged;
                        int? relayedMs = c.TimeMs;
                        r = new Resolution(c.Kind, ClaimRules.SeenUltimate(c.Kind, build), ReleasePlan.From(c.Kind, c.Stage),
                            charged ? Paths.StagesFor(build).Tier(build.Charge, relayedMs / 1000f) : 0);
                        ms = relayedMs;
                    }
                    else r = Resolver.Resolve(ClientFacts(bow, false, name));
                }
            }

            // G3: a Multi main's hold stage (VolleyHold), decided from the time this release is decided with: the server's (its clock or
            // the shooter's trusted claim, the one it relays), the shooter's own clock, a spectator's relayed time (else the time since
            // the Volley skill's ChargingStarted status). A spectator thus gets the server's stage; the shooter's own may differ by
            // its clock's lag near a stage boundary (looks only: the server's decides the damage and stagger).
            float? held = ms.HasValue ? ms.Value / 1000f : server ? (float?)null : Volley.HeldSeconds(bow);
            r = r.WithHold(VolleyHold.ForRelease(r, build, held, Paths.VolleyTimesFor(build), Paths.Values.VolleyStageSeconds));

            DrawRecord rec = Get(bow);
            rec.Last = r;
            rec.LastArrow = id;
            rec.LastShotAt = Time.time;
            rec.GameResetsLogged = 0;
            if (id != 0u) {
                if (releases.Count >= ReleaseCap && !releases.ContainsKey(id)) { releases.Clear(); releaseBuilds.Clear(); }
                releases[id] = r;
                releaseBuilds[id] = build;
                if (server && sky == SkyCode.None) Claims.PutResolved(id, r, ms, build);   // a Skyfall release's relay was put by Skyfall.DecideServer
                if (server) Telemetry.Shot(id, arrow, owner, r, build, ms, sky, test);   // balance telemetry: one row per release (never throws)
                // Weight: what this arrow carries into its first creature hit (server: stagger, interrupt; clients: the looks).
                try {
                    if (server) Weight.Arm(id, r, build, owner, bow);
                    else Weight.ArmClient(id, r, build, local);
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Weight: arming arrow {id} failed: {e}"); }
            }

            float spectral = r.ChargedTier > 0 ? Tree.ChargeMultiplier(r.ChargedTier) : 1f;
            string t = ms.HasValue ? ms.Value.ToString(CultureInfo.InvariantCulture) : "?";
            string ult = r.Ultimate == UltimateUse.None ? "-" : r.Ultimate.ToString();
            if (Logs.Summary) MelonLogger.Msg($"[Archery] {side}: arrow {id} '{name}' {r.Kind} t{t} ult:{ult} {r.Plan} spectral x{spectral.ToString("0.00", CultureInfo.InvariantCulture)} " +
                            $"charge tier {r.ChargedTier} " + (r.HoldStage > 0 ? $"hold stage {r.HoldStage} " : "") +
                            $"| {Paths.TimingOf(build)} | build {build}{(test ? " | TEST BUILD" : "")}");
            if (server && sky == SkyCode.None) Shrine.PractiseOnShot(owner);   // a later orb held in the try-out area (does nothing otherwise); never a Skyfall release
            return r;
        }

        // ---------------------------------------------------------------- the draw clock

        // Client: every frame the local player holds a string. Server: every update of a held string.
        [HarmonyPatch(typeof(BowString), "HeldUpdate")]
        static class Clock {
            static readonly int T = Timing.Section("patch BowString.HeldUpdate / Draws.Clock");
            static void Postfix(BowString __instance, Interactor ___interactorHoldingBowString, float ___currentZ) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer && (___interactorHoldingBowString == null || !___interactorHoldingBowString.IsLocal)) return;
                    Live(ref live0, "BowString.HeldUpdate");
                    // The pull exactly as OnUpdatedStringPull receives it (BowString.HeldUpdate: GetStrength(currentZ)),
                    // and the arrow as the game's pullback check sees it (StringDock.QuantityStored > 0).
                    uint arrow = 0u;
                    PickupDock dock = __instance.StringDock;
                    if (dock != null && dock.QuantityStored > 0) {
                        Pickup docked = dock.DockedPickup;
                        if (docked != null && docked.Entity != null) arrow = docked.Entity.Identifier;
                    }
                    float pull = GetStrength(__instance, ___currentZ);
                    DrawRecord rec = Get(__instance);
                    float grace = DipGrace;
                    rec.Clock.Update(pull, arrow, Time.time, grace);
                    if (!NetworkSceneManager.IsServer && Skyfall.LocalHolds) SkyfallHold.Frame(__instance, SkyfallHold.AimOf(__instance, pull), Elapsed(__instance) ?? 0f); // Skyfall: the caster's hold looks (the arm is this draw clock, Task 8's ShooterAsks); one bool test for everyone else
                    if (NetworkSceneManager.IsServer) {
                        // fix batch D: the snap detector (a held string's pull dropping from full draw to nearly none in one update)
                        if (arrow != 0u && PullSnap.Fell(rec.LastPull, pull, DrawClock.CrossingPull) && PullSnap.Due(rec.SnapLoggedAt, Time.time)) {
                            rec.SnapLoggedAt = Time.time;
                            LogSnap(__instance, ___interactorHoldingBowString, rec.LastPull, pull);
                        }
                        rec.LastPull = pull;
                        // each player's own charge time: the held-back Charged statuses and Holy Arrow's arming
                        OwnTime.Tick(__instance);
                        HolyArrow.Tick(__instance);
                    }
                }
                catch (Exception e) {
                    if (!warnedClock) { warnedClock = true; MelonLogger.Warning($"[Archery] Draw: draw clock failed: {e}"); }
                }
            }
        }

        // Server, play-test 7: the last holder of each bow string, so a string drawn by someone else starts fresh for them.
#if QUEST
        static readonly Dictionary<BowString, string> holders = new Dictionary<BowString, string>(Il.ByPointer<BowString>());
#else
        static readonly Dictionary<BowString, string> holders = new Dictionary<BowString, string>();
#endif

        static readonly int T_NewHolder = Timing.Section("Draws.NewHolder");
        static void NewHolder(BowString bow, Interactor? interactor) {
            using var _t = Timing.Begin(T_NewHolder);
            PlayerController pc = interactor != null && interactor.Controller != null ? interactor.Controller.PlayerController : null!;
            Player? p = pc != null ? pc.NetworkPlayer : null;
            if (p == null) return;
            string name = Builds.NameOf(p);
            holders.TryGetValue(bow, out string? last);
            if (holders.Count > RecordCap && last == null) {
                foreach (BowString key in holders.Keys) if (key == null) dead.Add(key!);
                foreach (BowString key in dead) holders.Remove(key);
                dead.Clear();
            }
            holders[bow] = name;
            if (!StaleInputRule.FreshBow(last, name)) return;
            // nothing decided, held back or armed for the previous holder carries over (their own-time holds, Holy Arrow, the volley)
            int holds = OwnTime.ForgetBow(bow);
            bool holy = HolyArrow.ForgetServer(bow);
            Volley.Consume(bow);
            int swept = Sweep(bow, p);
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Draw: '{name}' took '{bow.name}' from '{last}': a fresh bow ({holds} held-back charge(s){(holy ? " and an armed Holy Arrow" : "")} of the last holder dropped; " +
                            $"{swept} input(s) of other holders or of skills '{name}' does not own removed from Quickdraw, Charge Shot, the Volley and Holy Arrow)");
        }

#if QUEST
        // Quest: BowDrawSkill.bowStrings is a proxy property, read directly below
#else
        static AccessTools.FieldRef<BowDrawSkill, Dictionary<ISkillInputMethod, BowString>>? bowStringsRef;
#endif

        // Fix batch D (fixA-review I-3): on a handover, every input on this string that is not the new holder's goes, in all four
        // bow skills (not only the ones the new holder owns: BowDrawSkill.Register, and so StaleInputs, runs only for those).
        // The same removal as StaleInputs: the game's own switch-off while active, unhooked, cleared.
        // G3 (diagnosis 3): on every grab, also the holder's own inputs of a skill the holder no longer owns (level 0: sold, the test
        // tool's sale, a refund); StaleInputRule.Wanted decides. (An input the game unhooked is cleared at DeregisterSkill, below.)
        static readonly int T_Sweep = Timing.Section("Draws.Sweep");
        static int Sweep(BowString bow, Player holder) {
            using var _t = Timing.Begin(T_Sweep);
            int n = 0;
            PlayerProgressionManager? pm = holder != null ? holder.ProgressionManager : null;
            foreach (BowDrawSkill? skill in new[] { Tree.Quickdraw, Tree.Slowdraw, VolleySkill.Skill, Tree.Holy }) {
                if (skill == null) continue;
                try {
#if QUEST
                    var byInput = skill.bowStrings;
                    var inputs = InputsOf(skill);
#else
                    bowStringsRef ??= AccessTools.FieldRefAccess<BowDrawSkill, Dictionary<ISkillInputMethod, BowString>>("bowStrings");
                    Dictionary<ISkillInputMethod, BowString> byInput = bowStringsRef(skill);
                    Dictionary<BowString, ISkillInputMethod>? inputs = InputsOf(skill);
#endif
                    if (byInput == null) continue;
                    bool owned = pm == null || pm.GetLevel(skill) > 0; // a manager that cannot be read keeps the holder's own inputs
                    var drop = new List<ISkillInputMethod>();
                    foreach (KeyValuePair<ISkillInputMethod, BowString> kv in byInput) {
#if QUEST
                        if (kv.Key == null || !Il.Same(kv.Value, bow)) continue;
#else
                        if (kv.Key == null || !ReferenceEquals(kv.Value, bow)) continue;
#endif
                        HeldPickupHandler h = kv.Key.Handler;
                        Player? owner = h != null && h.PlayerHelper != null ? h.PlayerHelper.Player : null;
                        if (!StaleInputRule.Wanted(!StaleInputRule.Foreign(owner, holder!), true, owned)) drop.Add(kv.Key);
                    }
                    if (inputs != null && inputs.TryGetValue(bow, out ISkillInputMethod cur) && cur != null && !drop.Contains(cur)) {
                        HeldPickupHandler h = cur.Handler;
                        Player? owner = h != null && h.PlayerHelper != null ? h.PlayerHelper.Player : null;
                        if (!StaleInputRule.Wanted(!StaleInputRule.Foreign(owner, holder!), true, owned)) drop.Add(cur);
                    }
                    foreach (ISkillInputMethod old in drop) {
                        try { if (old.IsActive) skill.InputChangedState(false, old); }
                        catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: switching off another holder's {Label(skill)} input failed: {e.Message}"); }
                        byInput.Remove(old);
                        try { old.ChangedActive -= skill.InputChangedState; } catch { }
                        OwnTime.Forget(old);
                        DrawGrace.Forget(old);
                        try { old.Clear(); } catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: clearing another holder's input failed: {e.Message}"); }
#if QUEST
                        if (inputs != null && inputs.TryGetValue(bow, out ISkillInputMethod now) && Il.Same(now, old)) inputs.Remove(bow);
#else
                        if (inputs != null && inputs.TryGetValue(bow, out ISkillInputMethod now) && ReferenceEquals(now, old)) inputs.Remove(bow);
#endif
                        n++;
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: handover sweep of {Label(skill)} failed: {e.Message}"); }
            }
            return n;
        }

        /// <summary>G3, server, every grab (after NewHolder): the holder's inputs of skills they no longer own go too; one Summary line
        /// when any went.</summary>
        static readonly int T_SweepUnwanted = Timing.Section("Draws.SweepUnwanted");
        static void SweepUnwanted(BowString bow, Interactor? interactor) {
            using var _t = Timing.Begin(T_SweepUnwanted);
            try {
                PlayerController pc = interactor != null && interactor.Controller != null ? interactor.Controller.PlayerController : null!;
                Player? p = pc != null ? pc.NetworkPlayer : null;
                if (p == null) return;
                int n = Sweep(bow, p);
                if (n > 0 && Logs.Summary) MelonLogger.Msg($"[Archery] Draw: {n} orphaned input(s) removed from '{bow.name}' at '{Builds.NameOf(p)}''s grab (another holder's, or of a skill they no longer own)");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: the grab's input sweep failed: {e.Message}"); }
        }

        // G3 (play-test 8, diagnosis 3): the game's HeldPickupHandler.DeregisterSkill (_decomp HeldPickupHandler.cs:99-135, reached from
        // PickupBasedSkill.Lock -> PlayerToolBasedSkillHelper.DeregisterSkill for every pickup held: a sale at the shrine, the test
        // tool's sale, a sale that removes child orbs, EndPractice) unhooks the skill and drops the input from Inputs, but clears it
        // (BowStringPullbackInputMethod.Clear: unsubscribes from the string) only when it is active. On the headless server a
        // released string stays "held", so an idle input was left subscribed to the string with no skill or map that knows it, and
        // kept sending ChargingStarted and Cancelled for a skill the player no longer owned. Now, when the game's own call left a bow
        // input that no other skill on that pickup uses (it returns early itself when one does) and did not clear it, it is cleared
        // here (StaleInputRule.ClearAfterDeregister). Server only, bow inputs only: nothing else changes.
#if !QUEST // server-only: the game's bow skills run on the server (CLAUDE.md, skill logic is server-side)
        [HarmonyPatch(typeof(HeldPickupHandler), nameof(HeldPickupHandler.DeregisterSkill))]
        static class Orphans {
            static readonly int TPrefix = Timing.Section("patch HeldPickupHandler.DeregisterSkill / Draws.Orphans prefix");
            static readonly int TPostfix = Timing.Section("patch HeldPickupHandler.DeregisterSkill / Draws.Orphans postfix");
            static void Prefix(HeldPickupHandler __instance, IInputBasedSkill skill, out ISkillInputMethod? __state) {
                using var _t = Timing.Begin(TPrefix);
                __state = null;
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || skill == null || skill.InputStructure == null) return;
                    if (!__instance.SkillsToInputs.ContainsKey(skill)) return; // the game returns at once too
                    if (__instance.Inputs.TryGetValue(skill.InputStructure, out ISkillInputMethod v) && v is BowStringPullbackInputMethod) __state = v;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: orphan check before a skill was dropped failed: {e.Message}"); }
            }

            static void Postfix(HeldPickupHandler __instance, IInputBasedSkill skill, ISkillInputMethod? __state) {
                using var _t = Timing.Begin(TPostfix);
                if (__state == null) return;
                try {
                    Live(ref live6, "HeldPickupHandler.DeregisterSkill");
                    bool stillUsed = false;
#if QUEST
                    foreach (KeyValuePair<IInputBasedSkill, ISkillInputMethod> kv in __instance.SkillsToInputs) if (Il.Same(kv.Value, __state)) { stillUsed = true; break; }
#else
                    foreach (KeyValuePair<IInputBasedSkill, ISkillInputMethod> kv in __instance.SkillsToInputs) if (ReferenceEquals(kv.Value, __state)) { stillUsed = true; break; }
#endif
                    // the game cleared it itself when it was active (the Clear is deferred, so it still reads active here)
                    if (!StaleInputRule.ClearAfterDeregister(stillUsed, __state.IsActive)) return;
                    OwnTime.Forget(__state);
                    DrawGrace.Forget(__state);
                    __state.Clear();
                    Pickup p = __instance.Pickup;
                    string who = __instance.PlayerHelper != null && __instance.PlayerHelper.Player != null ? Builds.NameOf(__instance.PlayerHelper.Player) : "?";
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Draw: '{who}' lost {Label(skill.Skill)}: its idle input on '{(p != null ? p.name : "?")}' was cleared (the game clears only an active one; no other skill used it)");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: clearing a dropped skill's input failed: {e.Message}"); }
            }
        }
#endif

        // A fresh grab starts a fresh draw: the last draw's span never ended (HeldUpdate stops with the release), so
        // without this the next shot would be timed from the previous crossing.
        [HarmonyPatch(typeof(BowString), "StringGrabbed")]
        static class Grabbed {
            static readonly int T = Timing.Section("patch BowString.StringGrabbed / Draws.Grabbed");
            static void Postfix(BowString __instance, Interactor interactor) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer && (interactor == null || !interactor.IsLocal)) return;
                    Live(ref live1, "BowString.StringGrabbed");
                    if (__instance == null) return;
                    if (NetworkSceneManager.IsServer) NewHolder(__instance, interactor);
                    if (NetworkSceneManager.IsServer) SweepUnwanted(__instance, interactor); // G3: inputs of sold skills, after the handover's own sweep
                    if (!records.TryGetValue(__instance, out DrawRecord rec)) return;
                    if (rec.Clock.TryElapsed(Time.time, DrawClock.Grace, out float s))
                        { if (Logs.Debug) MelonLogger.Msg($"[Archery] Draw: string grabbed on '{__instance.name}'; the last draw's clock ({Ms(s)} ms) was cleared"); }
                    rec.Clock.Reset();
                    rec.LastPull = float.NaN; // G3 (diagnosis 8): the last draw's pull is not this draw's first; the snap detector starts fresh
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: re-grab reset failed: {e.Message}"); }
            }
        }

        // ---------------------------------------------------------------- stale skill inputs on a string (play-test 7, 2a)

        // BowDrawSkill.Register throws "same key" when the string still has an earlier input (its removal is deferred, and on the
        // headless server the release's audio NullReferenceException leaves the hand "holding", so the next grab clears and
        // registers in the same call). The throw leaves the new input registered (bowStrings) and subscribed to the string with no
        // handler that will ever clear it, and aborts the rest of the grab's skills: inputs pile up per skill per string, keep
        // being driven (Charged sent for an earlier holder, a burst of "lost" lines), and the new holder's own Slowdraw, Quickdraw
        // and Lightbeam never register. Before the game adds the new input, every other registration for the same string in this
        // skill goes (StaleInputRule, tested), whoever it belonged to: switched off through the game's own InputChangedState while
        // it is still known, unhooked from the skill, and cleared (unsubscribed from the string). Every BowDrawSkill (Quickdraw,
        // Slowdraw, the mod's Volley copy, Lightbeam) runs this method. The StringReleased exception itself is not touched (the
        // server gets InteractEnd before the shot message; a clean release would reset the inputs before OnArrowShot).
#if !QUEST // server-only: the game's bow skills run on the server (CLAUDE.md, skill logic is server-side)
        [HarmonyPatch(typeof(BowDrawSkill), nameof(BowDrawSkill.Register), new[] { typeof(ISkillInputMethod) })]
        static class StaleInputs {
            static readonly int T = Timing.Section("patch BowDrawSkill.Register / Draws.StaleInputs");
            const int LogCap = 200;
            static int logged;

            static void Prefix(BowDrawSkill __instance, ISkillInputMethod input, Dictionary<ISkillInputMethod, BowString> ___bowStrings,
                               Dictionary<BowString, ISkillInputMethod> ___inputs) {
                using var _t = Timing.Begin(T);
                try {
                    if (__instance == null || input == null || ___bowStrings == null || ___inputs == null) return;
                    Pickup p = input.Handler != null ? input.Handler.Pickup : null!;
                    BowString s = p != null ? p.GetComponent<BowString>() : null!;
                    if (s == null) return;
                    List<ISkillInputMethod> stale = StaleInputRule.Stale(___bowStrings, input, s);
                    if (___inputs.TryGetValue(s, out ISkillInputMethod current) && StaleInputRule.Replaces(current, input) && !stale.Contains(current)) stale.Add(current);
                    if (stale.Count == 0) return;
                    var whose = new Dictionary<string, int>();
                    int active = 0;
                    foreach (ISkillInputMethod old in stale) {
                        if (old == null) continue;
                        string owner = OwnerName(old);
                        whose.TryGetValue(owner, out int n); whose[owner] = n + 1;
                        try {
                            if (old.IsActive) { active++; __instance.InputChangedState(false, old); } // the game's own off: effects, OnArrowShot hook
                        }
                        catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: switching off a stale {Label(__instance)} input failed: {e.Message}"); }
                        ___bowStrings.Remove(old);
                        try { old.ChangedActive -= __instance.InputChangedState; } catch { }
                        OwnTime.Forget(old);
                        DrawGrace.Forget(old);
                        try { old.Clear(); } catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: clearing a stale input failed: {e.Message}"); }
                    }
#if QUEST
                    if (___inputs.TryGetValue(s, out ISkillInputMethod cur) && !Il.Same(cur, input)) ___inputs.Remove(s);
#else
                    if (___inputs.TryGetValue(s, out ISkillInputMethod cur) && !ReferenceEquals(cur, input)) ___inputs.Remove(s);
#endif
                    if (logged >= LogCap || !Logs.Summary) return; // G2: Summary (a stale input is an open bug's trace); nothing below acts
                    var parts = new List<string>();
                    foreach (KeyValuePair<string, int> kv in whose) parts.Add($"'{kv.Key}' x{kv.Value}");
                    string line = $"[Archery] Draw: dropped {stale.Count} stale {Label(__instance)} input(s) on '{s.name}' ({string.Join(", ", parts.ToArray())}; {active} still active) " +
                                  $"before the new one of '{OwnerName(input)}'";
                    if (++logged == LogCap) line += " (no more of these lines this session)";
                    MelonLogger.Msg(line);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: stale input check failed, the game registers as before: {e.Message}"); }
            }
        }
#endif

        static string OwnerName(ISkillInputMethod? input) {
            HeldPickupHandler h = input != null ? input.Handler : null!;
            return h != null && h.PlayerHelper != null && h.PlayerHelper.Player != null ? Builds.NameOf(h.PlayerHelper.Player) : "?";
        }

        // ---------------------------------------------------------------- server: a short dip loses nothing

        static string Label(object? skill) {
            if (skill == null) return "?";
#if QUEST
            if (Il.Same(skill, Tree.Slowdraw)) return "Slowdraw";
#else
            if (ReferenceEquals(skill, Tree.Slowdraw)) return "Slowdraw";
#endif
#if QUEST
            if (VolleySkill.Skill != null && Il.Same(skill, VolleySkill.Skill)) return "Volley";
#else
            if (VolleySkill.Skill != null && ReferenceEquals(skill, VolleySkill.Skill)) return "Volley";
#endif
#if QUEST
            if (Il.Same(skill, Tree.Holy)) return "Holy Arrow";
#else
            if (ReferenceEquals(skill, Tree.Holy)) return "Holy Arrow";
#endif
#if QUEST
            if (Il.Same(skill, Tree.Quickdraw)) return "Quickdraw";
#else
            if (ReferenceEquals(skill, Tree.Quickdraw)) return "Quickdraw";
#endif
            return SkillName(skill);
        }

        static string SkillName(object? skill) => skill is ProfessionSkill ps && ps != null ? ps.name : "?";

        // The game cancels every bow skill the instant the string reads at or below the draw threshold, even for a
        // single update. From the 68% crossing on, a dip only counts once it has lasted RelaxGraceSeconds, for every
        // input in range (Quickdraw's included). Low priority: after RevampedSkills' pull rewrite.
        [HarmonyPatch(typeof(BowStringPullbackInputMethod), "CheckForPullback")]
        static class DrawGrace {
            static readonly int T = Timing.Section("patch BowStringPullbackInputMethod.CheckForPullback / Draws.DrawGrace");
#if QUEST
            static readonly Dictionary<BowStringPullbackInputMethod, float> belowSince = new Dictionary<BowStringPullbackInputMethod, float>(Il.ByPointer<BowStringPullbackInputMethod>());
#else
            static readonly Dictionary<BowStringPullbackInputMethod, float> belowSince = new Dictionary<BowStringPullbackInputMethod, float>();
#endif

            internal static void Forget(ISkillInputMethod input) { if (input is BowStringPullbackInputMethod b) belowSince.Remove(b); }

            [HarmonyPriority(Priority.Low)]
            static bool Prefix(BowStringPullbackInputMethod __instance, float currentPull, bool ___isInPullbackRange,
                               BowStringPullbackStructure ___pullbackStructure, BowString ___bowString, object ___skill) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || ___pullbackStructure == null || ___bowString == null) return true;
                    Live(ref live2, "BowStringPullbackInputMethod.CheckForPullback");
                    if (!___isInPullbackRange || currentPull > ___pullbackStructure.PullbackValue) { belowSince.Remove(__instance); return true; }
                    if (___bowString.StringDock == null || ___bowString.StringDock.QuantityStored < 1) return true; // the game ignores this update anyway
                    float grace = DipGrace; // fix batch D: [Draw] ServerDipGraceSeconds
                    if (!belowSince.TryGetValue(__instance, out float since)) {
                        if (belowSince.Count > 256) belowSince.Clear();
                        belowSince[__instance] = since = Time.time;
                    }
                    if (Time.time - since < grace) return false; // a brief dip: carry on as if it never happened
                    belowSince.Remove(__instance);
                    // fix batch D (diagnosis-holy.md): which string, whose input (live or stale), how far into the draw, which arrow
                    // G2: Summary for a STALE input (it traces an open bug), Debug for a live one; nothing here acts, it only reads
                    if (!Logs.Summary) return true;
                    var map = ___skill is BowDrawSkill b ? InputsOf(b) : null;
#if QUEST
                    bool live = map != null && map.TryGetValue(___bowString, out ISkillInputMethod cur) && Il.Same(cur, __instance);
#else
                    bool live = map != null && map.TryGetValue(___bowString, out ISkillInputMethod cur) && ReferenceEquals(cur, __instance);
#endif
                    if (live && !Logs.Debug) return true;
                    HeldPickupHandler h = ((ISkillInputMethod)__instance).Handler;
                    string who = h != null && h.PlayerHelper != null && h.PlayerHelper.Player != null ? Builds.NameOf(h.PlayerHelper.Player) : "?";
                    int? ms = ElapsedMs(___bowString);
                    MelonLogger.Msg($"[Archery] Draw: {Label(___skill)} lost on '{___bowString.name}' of '{who}' ({(live ? "live" : "STALE")} input), the string stayed relaxed at {currentPull:0.00} " +
                                    $"(needs more than {___pullbackStructure.PullbackValue:0.00}) for {grace} s, {(ms.HasValue ? ms.Value + " ms" : "no crossing")} into the draw; arrow {ArrowName(___bowString)}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: grace check failed: {e.Message}"); }
                return true;
            }
        }

        /// <summary>For the log: the arrow docked on a string, "id 'prefab name'" (or "-").</summary>
        static string ArrowName(BowString bow) {
            Pickup a = bow != null && bow.StringDock != null && bow.StringDock.QuantityStored > 0 ? bow.StringDock.DockedPickup : null!;
            if (a == null) return "-";
            NetworkEntity e = a.Entity;
            string prefab = e != null && e.Prefab != null ? e.Prefab.name : a.name;
            return $"{(e != null ? e.Identifier : 0u)} '{prefab}'";
        }

        // Fix batch D (diagnosis-holy.md): the server's snap detector. A held string's pull that fell from above 68% to under 0.3 in
        // one update; logged with the docked arrow and how many of its solid colliders are enabled (after the nock-sound finalizer
        // the game's dock should leave none). At most one line per string per minute (PullSnap).
        static void LogSnap(BowString bow, Interactor? holder, float before, float now) {
            if (!Logs.Summary) return; // G2: Summary (at most one line per string a minute, an open bug's trace); only reads
            try {
                Pickup a = bow.StringDock != null && bow.StringDock.QuantityStored > 0 ? bow.StringDock.DockedPickup : null!;
                int enabled = 0, solid = 0;
                var layers = new HashSet<string>();
                if (a != null)
                    foreach (Collider c in a.GetComponentsInChildren<Collider>(true)) {
                        if (c == null || c.isTrigger) continue;
                        solid++;
                        if (!c.enabled || !c.gameObject.activeInHierarchy) continue;
                        enabled++;
                        layers.Add(LayerMask.LayerToName(c.gameObject.layer));
                    }
                PlayerController pc = holder != null && holder.Controller != null ? holder.Controller.PlayerController : null!;
                string who = pc != null ? Builds.NameOf(pc.NetworkPlayer) : "?";
                MelonLogger.Msg($"[Archery] Draw: snap on '{bow.name}' of '{who}': pull {before:0.00} -> {now:0.00} in one update ({Time.deltaTime * 1000f:0} ms); " +
                                $"arrow {ArrowName(bow)}: {enabled} of {solid} solid collider(s) enabled{(layers.Count > 0 ? " on " + string.Join("/", new List<string>(layers).ToArray()) : "")} (at most one line per string a minute)");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: snap log failed: {e.Message}"); }
        }

        // ---------------------------------------------------------------- server: every input resets when the arrow leaves

        // The bow string's skill handler, through whichever bow skill knows this string.
        static HeldPickupHandler? HandlerOf(BowString bow) {
            foreach (BowDrawSkill? skill in new[] { Tree.Quickdraw, Tree.Slowdraw, VolleySkill.Skill, Tree.Holy }) { // a Volley-only archer too
                if (skill == null) continue;
                Dictionary<BowString, ISkillInputMethod>? map = InputsOf(skill);
                if (map != null && map.TryGetValue(bow, out ISkillInputMethod input) && input != null && input.Handler != null) return input.Handler;
            }
            return null;
        }

        static float OneOff(Pickup? arrow) {
            ImpactTool tool = arrow != null ? arrow.GetComponent<ImpactTool>() : null!;
            if (tool == null) return 1f;
            float best = 0f;
            foreach (ImpactTool t in Hits.Tools(tool, oneOffTools)) best = Mathf.Max(best, t.OneOffMultiplier);
            return best;
        }
        static readonly List<ImpactTool> oneOffTools = new List<ImpactTool>(); // G2: OneOff's own list (no array per shot)

        // OnShoot raises OnShootingEntity for every handler, so this runs after every skill's OnArrowShot (and after
        // Volley.Fire). CheckForPullback returns early with no arrow docked, so an input still in range would otherwise
        // keep timing and charge the next arrow.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(BowString), "OnShoot")]
        static class ShotReset {
            static readonly int T = Timing.Section("patch BowString.OnShoot / Draws.ShotReset");
            [HarmonyPriority(Priority.Last)]
            static void Postfix(BowString __instance, Pickup __1) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null) return;
                    Live(ref live3, "BowString.OnShoot");
                    uint id = __1 != null && __1.Entity != null ? __1.Entity.Identifier : 0u;
                    Pvp.Shot(id); // fix batch D (fixA-review I-1): a new shot of this arrow, which a player hit by it may report once
                    var charging = new List<string>();
                    int n = 0;
                    HeldPickupHandler? handler = HandlerOf(__instance);
                    if (handler != null) {
                        var all = new List<ISkillInputMethod>(handler.Inputs.Values);
                        foreach (ISkillInputMethod input in all) {
                            if (input == null) continue;
                            bool live = input.IsActive || (input is BowStringPullbackInputMethod pull && InRange(pull));
                            if (!live) continue;
                            foreach (KeyValuePair<IInputBasedSkill, ISkillInputMethod> kv in handler.SkillsToInputs)
#if QUEST
                                if (Il.Same(kv.Value, input) && kv.Key != null) charging.Add(SkillName(kv.Key.Skill));
#else
                                if (ReferenceEquals(kv.Value, input) && kv.Key != null) charging.Add(SkillName(kv.Key.Skill));
#endif
                        }
                        ours = true;
                        try {
                            foreach (ISkillInputMethod input in all) {
                                if (input == null) continue;
                                try { input.Reset(true); n++; }
                                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: resetting an input failed: {e.Message}"); }
                            }
                        }
                        finally { ours = false; }
                    }
                    float oneOff = OneOff(__1);
                    // Only the main path's shot exists: the real arrow carries the multiplier of the release as decided (a snap
                    // the Quick tier, a charged shot the stage it reached, anything else x1), whatever the game's skills set.
                    // Set on the root tool only, the one BowDrawSkill.OnArrowShot sets (arg2.Entity's ImpactTool); the parts keep
                    // what the game left. A Quickdraw 4 line and a Holy beam keep what their own code sets. No main path: as 0.9.2,
                    // only a staged charge is lowered, on every tool above it.
                    Resolution? rel = ReleaseOf(id);
                    NetworkEntity? entity = __1 != null ? __1.Entity : null;
                    ImpactTool root = entity != null ? entity.GetComponent<ImpactTool>() : null!;
                    if (rel.HasValue && root != null && releaseBuilds.TryGetValue(id, out Build b)) {
                        float? set = Resolver.RealArrowMultiplier(rel.Value, b, oneOff, Tree.QuickMultiplier, Tree.ChargeMultiplier, Tree.ChargeDamageAt);
                        if (set.HasValue) {
                            if (b.Main == MainPath.None) { foreach (ImpactTool t in Hits.Tools(root)) if (t.OneOffMultiplier > set.Value) t.OneOffMultiplier = set.Value; }
                            else root.OneOffMultiplier = set.Value;
                            if (Logs.Debug) MelonLogger.Msg($"[Archery] Release: arrow {id} real arrow x{oneOff.ToString("0.###", CultureInfo.InvariantCulture)} -> " +
                                            $"x{set.Value.ToString("0.###", CultureInfo.InvariantCulture)} ({rel.Value.Kind}, main {b.Main})");
                            oneOff = set.Value;
                        }
                    }
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Release: arrow {id} game set x{oneOff.ToString("0.###", CultureInfo.InvariantCulture)} on the real arrow; " +
                                    $"reset {n} inputs (still charging: {(charging.Count > 0 ? string.Join(", ", charging.ToArray()) : "none")})");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: input reset on release failed: {e}"); }
            }
        }
#endif

        // The game's own clears after a shot, timed against it (a few lines per shot, for the log only).
        static bool TakeGameLine(DrawRecord rec, out int ms) {
            ms = 0;
            if (ours || rec.LastShotAt < 0f || Time.time - rec.LastShotAt > GameLogSeconds || rec.GameResetsLogged >= GameLinesPerShot) return false;
            rec.GameResetsLogged++;
            ms = Ms(Time.time - rec.LastShotAt);
            return true;
        }

#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(BowStringPullbackInputMethod), nameof(BowStringPullbackInputMethod.Reset))]
        static class GameReset {
            static readonly int T = Timing.Section("patch BowStringPullbackInputMethod.Reset / Draws.GameReset");
            [HarmonyPriority(Priority.First)]
            static void Prefix(BowString ___bowString, object ___skill) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer) return;
                    Live(ref live4, "BowStringPullbackInputMethod.Reset");
                    if (ours || ___bowString == null || !records.TryGetValue(___bowString, out DrawRecord rec) || !TakeGameLine(rec, out int ms)) return;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Draw: the game reset {SkillName(___skill)} on '{___bowString.name}' {ms} ms after arrow {rec.LastArrow} was shot");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: reset log failed: {e.Message}"); }
            }
        }
#endif

        // Runs when the held pickup's interaction ends (PlayerToolBasedSkillHelper.InteractionEnded): the bow or the
        // string, which the log settles. Each input's own Reset follows one frame later.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(HeldPickupHandler), nameof(HeldPickupHandler.Clear))]
        static class GameClear {
            static readonly int T = Timing.Section("patch HeldPickupHandler.Clear / Draws.GameClear");
            static void Postfix(HeldPickupHandler __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null) return;
                    Live(ref live5, "HeldPickupHandler.Clear");
                    Pickup p = __instance.Pickup;
                    if (ours || p == null || records.Count == 0) return;
                    foreach (KeyValuePair<BowString, DrawRecord> kv in records) {
                        BowString bow = kv.Key;
                        if (bow == null) continue;
                        bool match = bow.Pickup == p || (bow.Bow != null && bow.Bow.IsPickup && bow.Bow.Pickup == p);
                        if (!match) continue;
                        if (TakeGameLine(kv.Value, out int ms))
                            { if (Logs.Debug) MelonLogger.Msg($"[Archery] Draw: the game cleared the inputs on '{bow.name}' {ms} ms after arrow {kv.Value.LastArrow} was shot"); }
                        break;
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: clear log failed: {e.Message}"); }
            }
        }
#endif
    }

    // Server: each player's own charge time. The game's Slowdraw and Volley timers are shared by every player and set to
    // the fastest build's (Tree.ApplyTimers). Their Charged status, which the clients turn into the game's charged sound,
    // the glow, the Fill's first thump and the ghosts, is held back here until this player's own time on the draw clock
    // (Paths.StagesFor(build).S1, Paths.VolleyTimesFor(build).VolleyAt), then sent by running the game's own
    // HandleInputChanged (BowDrawSkill: SetEffectActive -> Charged). A release before that gets nothing from the game's skill:
    // OnArrowShot is skipped (no multiplier, no Consumed status, no practice), as the resolver says it was not charged.
    // Holy Arrow's arming is held the same way in HolyArrow.Arm/Tick.
    internal static class OwnTime {
        sealed class Wait {
            public BowDrawSkill skill = null!;
            public ISkillInputMethod input = null!;
            public BowString bow = null!;
            public float since;
        }

        const int Cap = 128;
        const float StaleSeconds = 60f;
        static readonly List<Wait> waits = new List<Wait>();
        static readonly MethodInfo? handle = AccessTools.Method(typeof(BowDrawSkill), "HandleInputChanged");
        // G2: a direct call instead of MethodInfo.Invoke with a new object[] per delivery. Non-virtual on purpose: the waits hold
        // Slowdraw and the Volley skill (BowSlowdraw, which does not override it; only LightBeamSkill does, and it is never queued
        // here), so this is the method Invoke dispatched to. Harmony's patches sit on the method itself, so they still run.
        static Action<BowDrawSkill, bool, ISkillInputMethod, BowString>? handleCall = Bind<BowDrawSkill>(handle);

        internal static Action<T, bool, ISkillInputMethod, BowString>? Bind<T>(MethodInfo? m) {
            if (m == null) return null;
            try { return AccessTools.MethodDelegate<Action<T, bool, ISkillInputMethod, BowString>>(m, null, false); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: no direct call to {m.DeclaringType?.Name}.{m.Name} ({e.Message}); reflection is used"); return null; }
        }

        /// <summary>HandleInputChanged(true, input, bow) through the bound call. G5 (G2 review M-1): until the bound call has once
        /// returned normally, ANY exception from it drops it for good and reflection delivers, as before G2 (the runtime may refuse a
        /// dynamic method in more ways than three exception types). After that first success its exceptions pass to the caller as
        /// the reflection call's did.</summary>
        internal static void Deliver<T>(ref Action<T, bool, ISkillInputMethod, BowString>? call, ref bool proven, MethodInfo m, T skill, ISkillInputMethod input, BowString bow) {
            if (call != null) {
                if (proven) { call(skill, true, input, bow); return; }
                try { call(skill, true, input, bow); proven = true; return; }
                catch (Exception e) {
                    call = null;
                    MelonLogger.Warning($"[Archery] Draw: the direct call to {m.DeclaringType?.Name}.{m.Name} failed at its first use ({e.GetType().Name}: {e.Message}); reflection is used");
                }
            }
            m.Invoke(skill, new object[] { true, input, bow });
        }
        static bool handleProven;
        static bool sending, warned;

#if QUEST
        static string Label(BowDrawSkill skill) => Il.Same(skill, Tree.Slowdraw) ? "Slowdraw" : "Volley";
#else
        static string Label(BowDrawSkill skill) => ReferenceEquals(skill, Tree.Slowdraw) ? "Slowdraw" : "Volley";
#endif

        static Player? OwnerOf(ISkillInputMethod input) {
            HeldPickupHandler handler = input != null ? input.Handler : null!;
            return handler != null && handler.PlayerHelper != null && handler.PlayerHelper.Player != null ? handler.PlayerHelper.Player : null;
        }

        /// <summary>Seconds after the crossing at which this skill is charged for this build; null for a skill the mod does not time.</summary>
        static float? OwnSeconds(BowDrawSkill skill, Build b) {
            if (skill == null) return null;
#if QUEST
            if (Il.Same(skill, Tree.Slowdraw)) return Paths.StagesFor(b).S1;
#else
            if (ReferenceEquals(skill, Tree.Slowdraw)) return Paths.StagesFor(b).S1;
#endif
#if QUEST
            if (VolleySkill.Skill != null && Il.Same(skill, VolleySkill.Skill)) return Paths.VolleyTimesFor(b).VolleyAt;
#else
            if (VolleySkill.Skill != null && ReferenceEquals(skill, VolleySkill.Skill)) return Paths.VolleyTimesFor(b).VolleyAt;
#endif
            return null;
        }

        static int Find(BowDrawSkill skill, BowString bow) {
#if QUEST
            for (int i = 0; i < waits.Count; i++) if (Il.Same(waits[i].skill, skill) && Il.Same(waits[i].bow, bow)) return i;
#else
            for (int i = 0; i < waits.Count; i++) if (ReferenceEquals(waits[i].skill, skill) && ReferenceEquals(waits[i].bow, bow)) return i;
#endif
            return -1;
        }

        static int Ms(float? s) => s.HasValue ? Mathf.RoundToInt(s.Value * 1000f) : -1;

        /// <summary>Server, play-test 7: a stale input (Draws.StaleInputs) holds nothing back any more.</summary>
#if QUEST
        internal static void Forget(ISkillInputMethod input) => waits.RemoveAll(w => Il.Same(w.input, input));
#else
        internal static void Forget(ISkillInputMethod input) => waits.RemoveAll(w => ReferenceEquals(w.input, input));
#endif

        /// <summary>Server, play-test 7: someone else took this string; every charge held back on it goes. How many went.</summary>
#if QUEST
        internal static int ForgetBow(BowString bow) => waits.RemoveAll(w => Il.Same(w.bow, bow));
#else
        internal static int ForgetBow(BowString bow) => waits.RemoveAll(w => ReferenceEquals(w.bow, bow));
#endif

        static bool warnedDeliver;

        static void WarnCannotDeliver(string missing) {
            if (warnedDeliver) return;
            warnedDeliver = true;
            MelonLogger.Warning($"[Archery] Draw: cannot deliver held charges ({missing}); per-player charge timing is off");
        }

        /// <summary>Server, every update of a held string (Draws.Clock): send the held-back Charged statuses whose time has come.</summary>
        static readonly int T_Tick = Timing.Section("Draws.OwnTime.Tick (held Charged statuses)");
        internal static void Tick(BowString bow) {
            using var _t = Timing.Begin(T_Tick);
            if (waits.Count == 0 || bow == null) return;
            try {
                for (int i = waits.Count - 1; i >= 0; i--) {
                    if (i >= waits.Count) continue; // G5: a delivery below may change the list
                    Wait w = waits[i];
                    if (w.bow == null || w.input == null || Time.time - w.since > StaleSeconds) { waits.RemoveAt(i); continue; }
#if QUEST
                    if (!Il.Same(w.bow, bow)) continue;
#else
                    if (!ReferenceEquals(w.bow, bow)) continue;
#endif
                    if (!w.input.IsActive) { waits.RemoveAt(i); continue; } // cancelled or shot meanwhile: nothing to send
                    Player? owner = OwnerOf(w.input);
                    float? own = OwnSeconds(w.skill, Builds.ServerBuild(owner));
                    float? t = Draws.Elapsed(bow);
                    if (own.HasValue && t.HasValue && !Complements.Reached(t, own.Value)) continue;
                    if (handle == null) { waits.RemoveAt(i); WarnCannotDeliver("BowDrawSkill.HandleInputChanged not found"); continue; } // Hold never queues then
                    sending = true;
                    // G5 (M-1): delivered first, then removed (by reference: the delivery may have changed the list)
                    try { Deliver(ref handleCall, ref handleProven, handle, w.skill, w.input, bow); }
                    finally { sending = false; int k = waits.IndexOf(w); if (k >= 0) waits.RemoveAt(k); }
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Draw: {Label(w.skill)} Charged sent for '{Builds.NameOf(owner)}' at {Ms(t)} ms (own time {Ms(own)} ms)");
                }
            }
            catch (Exception e) {
                sending = false;
                if (!warned) { warned = true; MelonLogger.Warning($"[Archery] Draw: sending a held-back Charged status failed: {e}"); }
            }
        }

        // The Charged status of Slowdraw and the Volley skill (BowDrawSkill.HandleInputChanged -> SetEffectActive). Holy Arrow
        // (a LightBeamSkill, which calls this as its base) is held by its own patch, HolyArrow.Arm.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(BowDrawSkill), "HandleInputChanged")]
        static class Hold {
            static readonly int T = Timing.Section("patch BowDrawSkill.HandleInputChanged / Draws.Hold");
            static bool live;

            static bool Prefix(BowDrawSkill __instance, bool isEnabled, ISkillInputMethod input, BowString bowString) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || bowString == null || input == null || __instance is LightBeamSkill) return true;
                    if (!isEnabled) { int i = Find(__instance, bowString); if (i >= 0) waits.RemoveAt(i); return true; }
                    if (sending) return true;
                    if (handle == null) { WarnCannotDeliver("BowDrawSkill.HandleInputChanged not found"); return true; } // never hold what cannot be sent
                    Player? owner = OwnerOf(input);
                    float? own = OwnSeconds(__instance, Builds.ServerBuild(owner));
                    if (!own.HasValue) return true;
                    float? t = Draws.Elapsed(bowString);
                    if (!t.HasValue || Complements.Reached(t, own.Value)) return true; // no clock: as the game would; on time: nothing to hold
                    if (!live) { live = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Draw: patch on BowDrawSkill.HandleInputChanged is live"); }
                    int at = Find(__instance, bowString);
                    if (at >= 0) waits.RemoveAt(at);
                    if (waits.Count >= Cap) waits.RemoveAt(0);
                    waits.Add(new Wait { skill = __instance, input = input, bow = bowString, since = Time.time });
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Draw: {Label(__instance)} charged by the shared timer at {Ms(t)} ms; its Charged status waits for '{Builds.NameOf(owner)}'s own {Ms(own)} ms");
                    return false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: own-time check failed, the game's timer stands: {e.Message}"); return true; }
            }
        }
#endif

        // An arrow shot while its Charged status is still held back: not charged for this player (the resolver says the same),
        // so the game's skill does nothing. With the shooter's trusted claim the release may already be on time (the echo fix):
        // then the game's skill runs as usual.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(BowDrawSkill), "OnArrowShot")]
        static class NotYet {
            static readonly int T = Timing.Section("patch BowDrawSkill.OnArrowShot / Draws.NotYet");
            static bool Prefix(BowDrawSkill __instance, BowString bowString, NetworkEntity shotEntity) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || bowString == null) return true;
                    int i = Find(__instance, bowString);
                    if (i < 0) return true;
                    Wait w = waits[i];
                    waits.RemoveAt(i);
                    uint id = shotEntity != null ? shotEntity.Identifier : 0u;
                    int? ms = Draws.ServerTime(bowString, id, out _, out _);
                    Player? owner = OwnerOf(w.input);
                    float? own = OwnSeconds(__instance, Builds.ServerBuild(owner));
                    if (ms.HasValue && own.HasValue && Complements.Reached(ms.Value / 1000f, own.Value)) return true;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Draw: arrow {id} left before '{Builds.NameOf(owner)}'s own {Label(__instance)} time ({(ms.HasValue ? ms.Value : -1)} of {Ms(own)} ms); the game's skill is skipped");
                    return false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Draw: own-time shot check failed: {e.Message}"); return true; }
            }
        }
#endif
    }
}
