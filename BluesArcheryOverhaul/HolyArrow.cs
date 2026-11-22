using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Holy Arrow: the end of the Slowdraw line. The game's own skill is kept (the beam, sacrificing the arrow,
    // damage from the arrow's rating) and changed around the edges:
    //   - it comes after Charge 3 and arms only on a live draw (string held, the same arrow as at the 68%
    //     crossing), no volley needed; it absorbs whatever ghosts are showing
    //   - the beam flies 5x faster, dead straight
    //   - direct hit 4x the charged arrow; where it lands it blasts everything within 5 m, with falloff,
    //     knock-back and "cripple" damage (the game's own stun: it knocks creatures down and slows players)
    //   - clients draw a jagged gold trail, a ground ring that shows the falloff, and a taser-like marker on
    //     everything that was stunned
    internal static class HolyArrow {
        static readonly Color DefaultGold = new Color(1f, 0.86f, 0.45f);

        static MelonPreferences_Entry<float> direct = null!, blast = null!, radius = null!, fullRadius = null!, edgeFraction = null!, cripple = null!,
            blastForce = null!, playerShare = null!, speed = null!, trailWidth = null!, trailKink = null!, trailLinger = null!, forkEvery = null!,
            flash = null!, ringSeconds = null!, ringWidth = null!, stunSeconds = null!, arcWidth = null!, readyFlash = null!,
            flashSeconds = null!, ringGap = null!, crackleSeconds = null!, crackleLength = null!, seenTrailSeconds = null!;
        static MelonPreferences_Entry<int> wisps = null!, ringCount = null!, crackleArcs = null!;
        static MelonPreferences_Entry<string> colour = null!, impactEffects = null!, impactSounds = null!;
        static MelonPreferences_Entry<bool> keepsArrow = null!;

        internal static Color Gold => Cfg.Colour(colour, DefaultGold);

        /// <summary>The cripple at the centre of the blast ([HolyArrow] BlastCripple); Weight's unit of stagger.</summary>
        internal static float BlastCripple => cripple != null ? cripple.Value : 0.5f;

        class BeamInfo { public PlayerController? shooter; public float centreDamage; public Transform? bow; public HashSet<ImpactChannel>? channels; }
        static readonly Dictionary<uint, BeamInfo> beams = new Dictionary<uint, BeamInfo>();          // server
#if QUEST
        static readonly Dictionary<Projectile, Vector3> boosted = new Dictionary<Projectile, Vector3>(Il.ByPointer<Projectile>()); // every machine
#else
        static readonly Dictionary<Projectile, Vector3> boosted = new Dictionary<Projectile, Vector3>(); // every machine
#endif
#if QUEST
        static readonly HashSet<BowString> armed = new HashSet<BowString>(Il.ByPointer<BowString>());                           // server
#else
        static readonly HashSet<BowString> armed = new HashSet<BowString>();                           // server
#endif
#if QUEST
        static readonly Dictionary<BowString, uint> clientArmed = new Dictionary<BowString, uint>(Il.ByPointer<BowString>());   // clients: bow -> arrow armed, from the status broadcasts
#else
        static readonly Dictionary<BowString, uint> clientArmed = new Dictionary<BowString, uint>();   // clients: bow -> arrow armed, from the status broadcasts
#endif
        static NetworkPrefab? beamPrefab;
        static readonly MethodInfo? onCollision = AccessTools.Method(typeof(Projectile), "OnCollision");

        internal static void Init() {
            var cat = Cfg.Category("HolyArrow");
            direct = Cfg.Shared(cat.CreateEntry("DirectMultiplier", 4f, description: "Direct hit, as a multiple of the charged main arrow."));
            blast = Cfg.Shared(cat.CreateEntry("BlastMultiplier", 2f, description: "Blast damage at the centre, as a multiple of the charged main arrow."));
            radius = Cfg.Shared(cat.CreateEntry("Radius", 7f, description: "How far the blast reaches, in metres. Damage and the visuals both use it."));
            fullRadius = Cfg.Shared(cat.CreateEntry("FullRadius", 2f, description: "Inside this distance the blast does full damage."));
            edgeFraction = Cfg.Shared(cat.CreateEntry("EdgeFraction", 0.5f, description: "Share of the centre damage dealt at the very edge."));
            cripple = Cfg.Shared(cat.CreateEntry("BlastCripple", 0.5f, description: "Stun strength at the centre (the game's cripple damage). Tune from the log."));
            blastForce = Cfg.Shared(cat.CreateEntry("BlastForce", 300f, description: "Shove given to loose objects."));
            playerShare = Cfg.Shared(cat.CreateEntry("PlayerDamageShare", 0.5f, description: "Other players take this share of the blast damage, only with the server's PvP on (its PvP multipliers apply on top); the archer is never hurt by their own blast."));
            speed = Cfg.Shared(cat.CreateEntry("SpeedMultiplier", 5f));
            keepsArrow = cat.CreateEntry("KeepsArrow", false, description: "[server] On: Holy Arrow no longer uses up the arrow; it goes back into the archer's quiver right after the shot (dropped at the bow if no quiver has room). Off: the game's sacrifice.");
            colour = cat.CreateEntry("Colour", "#FFDB73");
            trailWidth = cat.CreateEntry("TrailWidth", 0.03f);
            trailKink = cat.CreateEntry("TrailKink", 0.12f, description: "How far the trail's kinks stray from the true path, in metres.");
            trailLinger = cat.CreateEntry("TrailLingerSeconds", 0.8f);
            seenTrailSeconds = cat.CreateEntry("SeenTrailSeconds", 2f, description: "Another archer's beam: how long to wait for its landing before dropping its trail (it is drawn only once the landing is known).");
            forkEvery = cat.CreateEntry("ForkEveryMetres", 4f, description: "A side fork on the trail every this many metres. 0 = none.");
            flash = cat.CreateEntry("ImpactFlash", 10f);
            flashSeconds = cat.CreateEntry("ImpactFlashSeconds", 0.5f);
            ringSeconds = cat.CreateEntry("RingSeconds", 0.9f, description: "How long each ground ring takes to reach the edge.");
            ringWidth = cat.CreateEntry("RingWidth", 0.07f);
            ringCount = cat.CreateEntry("Rings", 3, description: "Ground rings racing outward one after another (1 to 6).");
            ringGap = cat.CreateEntry("RingGapSeconds", 0.18f);
            wisps = cat.CreateEntry("Wisps", 16, description: "Bolts thrown up from the ground at the moment of impact.");
            crackleSeconds = cat.CreateEntry("CrackleSeconds", 2.5f, description: "How long the ground keeps crackling after the impact. 0 = none.");
            crackleArcs = cat.CreateEntry("CrackleArcs", 3, description: "Small arcs per crackle burst at the start; they thin out as it dies down.");
            crackleLength = cat.CreateEntry("CrackleLength", 0.9f, description: "Length of a crackle arc near the centre, in metres. Shorter toward the edge.");
            stunSeconds = cat.CreateEntry("StunMarkerSeconds", 4f, description: "How long the taser marker lasts on something at the centre of the blast.");
            arcWidth = cat.CreateEntry("StunArcWidth", 0.008f);
            readyFlash = cat.CreateEntry("ReadyFlash", 4f);
            impactEffects = cat.CreateEntry("ImpactEffects", "Shockwave Impact;Power Shockwave;Stun Explosion;Small Explosion", description: "Game effects to try at the impact, first one found wins. See BluesArcheryOverhaul\\loaded_assets.txt.");
            impactSounds = cat.CreateEntry("ImpactSounds", "Huge Explosion;Shockwave Sounds;Lightbeam Shot Impact Sounds", description: "Game sounds to try at the impact, first one found wins.");
            Cfg.Reloaded += Fx.ClearAssetCache;
        }

        /// <summary>Server: Holy Arrow is charged and waiting on this bow.</summary>
        internal static bool IsArmed(BowString bow) => bow != null && armed.Contains(bow);

        /// <summary>Server, play-test 7: someone else took this bow string (Draws.Grabbed): nothing armed or waiting carries over.</summary>
        internal static bool ForgetServer(BowString bow) => bow != null && (armed.Remove(bow) | waiting.Remove(bow));

        /// <summary>Client: the server said Holy Arrow is charged for the arrow of this draw (its Charged status names the
        /// arrow on the string), and it has not been cancelled or fired since. Arming removes the bow's volley state here,
        /// so without this the release would read as an Early shot and fly echoes the server never fires. Bound to the
        /// arrow at this draw's 68% crossing, so a flag left by an arrow that was taken off the string never arms the next.</summary>
        internal static bool ArmedOnClient(BowString? bow) =>
            bow != null && clientArmed.TryGetValue(bow, out uint arrow) && arrow != 0u && arrow == Draws.ArrowAtCrossing(bow);

        /// <summary>Client, spectators included (no draw clock needed): the last Lightbeam Charged status on this bow named this
        /// arrow, so its release is the beam. For the looks only (ChargeFx).</summary>
        internal static bool ArmedFlagFor(BowString? bow, uint arrow) =>
            bow != null && arrow != 0u && clientArmed.TryGetValue(bow, out uint armedArrow) && armedArrow == arrow;

        /// <summary>Client: the arrow left this bow (Volley.Fire, after the release was decided), so Holy Arrow is no longer armed.</summary>
        internal static void ClearClientArmed(BowString? bow) { if (bow != null) clientArmed.Remove(bow); }

        // Client: arrows whose beam this client saw fire (BeamFiredSync), for WatcherSkipsKeptArrow; kept 2 s, at most 32.
        static readonly Dictionary<uint, float> beamFired = new Dictionary<uint, float>();
        static void NoteBeamFired(uint arrow) {
            if (arrow == 0u) return;
            if (beamFired.Count >= 32) beamFired.Clear();
            beamFired[arrow] = Time.time;
        }
        static bool TakeBeamFired(uint arrow) =>
            beamFired.TryGetValue(arrow, out float at) & beamFired.Remove(arrow) && Time.time - at <= 2f;

        static void SetClientArmed(BowString bow, uint arrow) {
            if (arrow == 0u) { clientArmed.Remove(bow); return; }
            if (clientArmed.Count > 64) clientArmed.Clear(); // a cap; a live draw re-arms on its next Charged status at worst
            clientArmed[bow] = arrow;
        }

        static bool IsBeam(NetworkEntity entity) {
            if (entity == null || Tree.Holy == null) return false;
            if (beamPrefab == null) beamPrefab = Traverse.Create(Tree.Holy).Field("beamPrefab").GetValue<NetworkPrefab>();
            return beamPrefab != null && entity.Prefab != null && entity.Prefab.Hash == beamPrefab.Hash;
        }

        // ---------------------------------------------------------------- server: arming

        /// <summary>The arrow on the string, as the game's pullback check sees it (0 when none).</summary>
        static uint ArrowOn(BowString bow) {
            var dock = bow.StringDock;
            if (dock == null || dock.QuantityStored < 1) return 0u;
            Pickup docked = dock.DockedPickup;
            return docked != null && docked.Entity != null ? docked.Entity.Identifier : 0u;
        }

        // Server: Lightbeam's input charged by the shared timer (set to the fastest build's, Tree.ApplyTimers) before this
        // archer's own wait (Paths.HolySecondsFor): armed from Tick when the draw clock reaches it, if the input is still live.
        struct Deferred { public LightBeamSkill skill; public ISkillInputMethod input; }
#if QUEST
        static readonly Dictionary<BowString, Deferred> waiting = new Dictionary<BowString, Deferred>(Il.ByPointer<BowString>());
#else
        static readonly Dictionary<BowString, Deferred> waiting = new Dictionary<BowString, Deferred>();
#endif
        static readonly MethodInfo? handleInput = AccessTools.Method(typeof(LightBeamSkill), "HandleInputChanged");
        // G2: a direct, non-virtual call to LightBeamSkill's own override (nothing derives from LightBeamSkill), as Invoke dispatched.
        static Action<LightBeamSkill, bool, ISkillInputMethod, BowString>? handleInputCall = OwnTime.Bind<LightBeamSkill>(handleInput);
        static bool warnedTick, warnedDefer, handleInputProven;

        /// <summary>What deferring the arming would need and is missing; null when it can be deferred.</summary>
        static string? CannotDefer(LightBeamSkill? skill, ISkillInputMethod? input) =>
            handleInput == null ? "LightBeamSkill.HandleInputChanged not found"
            : skill == null ? "no skill instance"
            : input == null ? "no input"
            : null;

        static void WarnCannotDefer(string missing) {
            if (warnedDefer) return;
            warnedDefer = true;
            MelonLogger.Warning($"[Archery] Holy: cannot arm on the archer's own time ({missing}); arming on the shared timer as the game would");
        }

        static Player? OwnerOf(BowString bow) {
            PlayerController? pc = bow.Pickup != null && bow.Pickup.LastPlayerInteractor != null ? bow.Pickup.LastPlayerInteractor : null;
            return pc != null ? pc.NetworkPlayer : null;
        }

        /// <summary>Server, every update of a held string (Draws.Clock): arm a Holy Arrow whose own wait is over.</summary>
        static readonly int T_Tick = Timing.Section("HolyArrow.Tick (own-time arming)");
        internal static void Tick(BowString bow) {
            using var _t = Timing.Begin(T_Tick);
            if (waiting.Count == 0 || bow == null || !waiting.TryGetValue(bow, out Deferred d)) return;
            try {
                ISkillInputMethod input = d.input;
                if (input == null || !input.IsActive) { waiting.Remove(bow); return; }
                float? t = Draws.Elapsed(bow);
                if (t.HasValue && !Complements.Reached(t, Paths.HolySecondsFor(Builds.ServerBuild(OwnerOf(bow))))) return;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: '{Builds.NameOf(OwnerOf(bow))}'s own wait is over at {(t.HasValue ? Mathf.RoundToInt(t.Value * 1000f) : -1)} ms; arming");
                string? missing = CannotDefer(d.skill, input);
                if (missing != null) { waiting.Remove(bow); WarnCannotDefer(missing); return; } // Arm never defers then
                // G5 (M-1): delivered first, then removed. Arm (run by the delivery) finds the wait over and arms; it never re-defers.
                try { OwnTime.Deliver(ref handleInputCall, ref handleInputProven, handleInput!, d.skill, input, bow); } // Arm decides again, now on time
                finally { waiting.Remove(bow); }
            }
            catch (Exception e) {
                waiting.Remove(bow);
                if (!warnedTick) { warnedTick = true; MelonLogger.Warning($"[Archery] Holy: arming on the archer's own time failed: {e}"); }
            }
        }

        // Live-draw gate: the string is held, it crossed 68% in this draw (a short dip keeps the span), and the arrow
        // on it is the one that was there at the crossing. No volley is needed.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(LightBeamSkill), "HandleInputChanged")]
        static class Arm {
            static readonly int TPrefix = Timing.Section("patch LightBeamSkill.HandleInputChanged / HolyArrow.Arm prefix");
            static readonly int TPostfix = Timing.Section("patch LightBeamSkill.HandleInputChanged / HolyArrow.Arm postfix");
#if QUEST
            static readonly Dictionary<BowString, uint> refusedLogged = new Dictionary<BowString, uint>(Il.ByPointer<BowString>()); // the arrow at the crossing of the draw last logged
#else
            static readonly Dictionary<BowString, uint> refusedLogged = new Dictionary<BowString, uint>(); // the arrow at the crossing of the draw last logged
#endif

            static bool Prefix(LightBeamSkill __instance, bool isEnabled, ISkillInputMethod input, BowString bowString) {
                using var _t = Timing.Begin(TPrefix);
                if (!NetworkSceneManager.IsServer || bowString == null) return true;
                if (!isEnabled) { armed.Remove(bowString); waiting.Remove(bowString); return true; }
                try {
                    DrawRecord record = Draws.Get(bowString);
                    // Only a Charge main (or no main path) has Holy Arrow.
                    Player? owner = OwnerOf(bowString);
                    Build build = Builds.ServerBuild(owner);
                    MainPath main = build.Main;
                    if (!Resolver.OnPath(main, MainPath.Charge)) {
                        uint at = record.Clock.ArrowAtCrossing;
                        if (!refusedLogged.TryGetValue(bowString, out uint was) || was != at) {
                            if (refusedLogged.Count >= 64) refusedLogged.Clear();
                            refusedLogged[bowString] = at;
                            if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: not armed for '{Builds.NameOf(owner)}': main path {main} (arrow {at})");
                        }
                        return false;
                    }
                    bool held = bowString.Pickup != null && bowString.Pickup.IsInteractedWith;
                    bool crossed = record.Clock.Active(Time.time, Draws.DipGrace); // fix batch D: [Draw] ServerDipGraceSeconds, as DrawGrace
                    uint arrowNow = ArrowOn(bowString), atCrossing = record.Clock.ArrowAtCrossing;
                    if (HolyGate.MayArm(held, crossed, arrowNow, atCrossing)) {
                        // The game's timer is the fastest build's: this archer waits for their own (Tick arms it then).
                        float own = Paths.HolySecondsFor(build);
                        float? t = Draws.Elapsed(bowString);
                        if (!t.HasValue || Complements.Reached(t, own)) return true;
                        string? missing = CannotDefer(__instance, input);
                        if (missing != null) { WarnCannotDefer(missing); return true; } // never defer what cannot be armed later
                        if (waiting.Count >= 64 && !waiting.ContainsKey(bowString)) waiting.Clear();
                        waiting[bowString] = new Deferred { skill = __instance, input = input };
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: charged by the shared timer at {Mathf.RoundToInt(t.Value * 1000f)} ms; '{Builds.NameOf(owner)}' arms at {Mathf.RoundToInt(own * 1000f)} ms");
                        return false;
                    }
                    string why = !held ? "string not held"
                               : arrowNow == 0u ? "no arrow on the string"
                               : !crossed ? "no crossing"
                               : $"the arrow changed since the crossing (was {atCrossing}, now {arrowNow})";
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: not armed: {why}");
                    return false;
                }
                catch (Exception e) {
                    MelonLogger.Warning($"[Archery] Holy: live-draw check failed, arming as the game would: {e.Message}");
                    return true;
                }
            }

            static void Postfix(bool isEnabled, BowString bowString, bool __runOriginal) {
                using var _t = Timing.Begin(TPostfix);
                if (!NetworkSceneManager.IsServer || !isEnabled || !__runOriginal || bowString == null) return;
                armed.Add(bowString);
                try {
                    int ghosts = Volley.Ghosts(bowString);
                    Volley.Consume(bowString); // absorbed: no extras fire with the beam
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: armed on a live draw (arrow {ArrowOn(bowString)}); {ghosts} ghost(s) absorbed");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: absorbing the volley on arming failed: {e.Message}"); }
            }
        }
#endif

        // A Holy Arrow that never armed must not give its damage bonus to the ordinary arrow either.
#if !QUEST   // a server's work: not patched on Quest
        [HarmonyPatch(typeof(BowDrawSkill), "OnArrowShot")]
#endif
        static class UnarmedShot {
            static readonly int T = Timing.Section("patch BowDrawSkill.OnArrowShot / HolyArrow.UnarmedShot");
            static bool Prefix(BowDrawSkill __instance, BowString bowString) {
                using var _t = Timing.Begin(T);
#if QUEST
                if (!(__instance.Is<LightBeamSkill>()) || !NetworkSceneManager.IsServer || bowString == null) return true;
#else
                if (!(__instance is LightBeamSkill) || !NetworkSceneManager.IsServer || bowString == null) return true;
#endif
                return armed.Remove(bowString);
            }
        }

        // Skyfall (admin skill): Skyfall holds a cast's arrow and the server destroys it after the relay. Holy Arrow's sacrifice (subscribed
        // when it armed, D LightBeamSkill.cs:26-31) would destroy it first, and every spectator's relayed shot would find no arrow (D Shooter.cs:50-61).
        // Everything the game's method does but the destroy is done here, the unsubscribe included, so it never sacrifices the next arrow.
#if !QUEST   // a server's work: not patched on Quest
        [HarmonyPatch(typeof(LightBeamSkill), "DestroyShotArrow")]
#endif
        static class SkyfallKeepsArrow {
            static readonly int T = Timing.Section("patch LightBeamSkill.DestroyShotArrow / HolyArrow.SkyfallKeepsArrow");
            static readonly MethodInfo? method = AccessTools.Method(typeof(LightBeamSkill), "DestroyShotArrow");
            internal static MethodInfo? Method => method;
            static bool Prefix(LightBeamSkill __instance, BowString bowString, NetworkEntity shotEntity) {
                using var _t = Timing.Begin(T);
                bool cast = false, keeping = false;
                try {
                    cast = shotEntity != null && bowString != null && Skyfall.IsCast(shotEntity.Identifier);
                    keeping = !cast;
                    if (!cast) return !(keepsArrow.Value && KeepArrow(__instance, bowString!, shotEntity!));
                    bowString!.IsIgnoringCollisionOnShoot = true;   // cast says it is not null (the test above it)
                    if (method != null)
                        bowString.OnShootingEntity -= (Action<BowString, NetworkEntity, Vector3>)Delegate.CreateDelegate(typeof(Action<BowString, NetworkEntity, Vector3>), __instance, method);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {shotEntity!.Identifier} kept from Holy Arrow's sacrifice (Skyfall holds it; the server destroys it after the relay)");
                    return false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] {(keeping ? "Holy: keeping the arrow" : "Skyfall: Holy Arrow's sacrifice check")} failed: {e.Message}"); return !cast; }
            }
        }

        // [HolyArrow] KeepsArrow (server): instead of the game's sacrifice the arrow is stopped where it is now (inert, so it hits
        // nothing) and put back into its archer's quiver on the next frame: after the server has relayed the shot (Shooter.SyncShoot
        // relays when Shoot returns), so every client sees the shot first and the dock second. No quiver with room: it drops at the bow.
        static bool KeepArrow(LightBeamSkill skill, BowString bowString, NetworkEntity shotEntity) {
            Pickup arrow = shotEntity.CommonPickup;
            if (arrow == null) return false;
            bowString.IsIgnoringCollisionOnShoot = true;
            MethodInfo? m = SkyfallKeepsArrow.Method;
            if (m != null) bowString.OnShootingEntity -= (Action<BowString, NetworkEntity, Vector3>)Delegate.CreateDelegate(typeof(Action<BowString, NetworkEntity, Vector3>), skill, m);
            Arrow a = arrow.GetComponent<Arrow>();
            if (a != null) a.StopUpdatingArrowFlight();
            Rigidbody rb = arrow.GetComponent<Rigidbody>();
            if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
            PlayerController? owner = bowString.Pickup != null
                ? (bowString.Pickup.Interactors.Count > 0 && bowString.Pickup.Interactors[0].Controller != null ? bowString.Pickup.Interactors[0].Controller.PlayerController : bowString.Pickup.LastPlayerInteractor)
                : null;
            MelonCoroutines.Start(ReturnArrow(arrow, rb, owner));
            return true;
        }

        static readonly int T_Return = Timing.Section("tick HolyArrow.ReturnArrow");
        static System.Collections.IEnumerator ReturnArrow(Pickup arrow, Rigidbody? rb, PlayerController? owner) {
            yield return null;
            using var _t = Timing.Begin(T_Return);   // after the yield, so the frame wait is not timed
            try {
                if (arrow == null || arrow.Entity == null || arrow.Entity.IsBeingDestroyed) yield break;
                if (rb != null) rb.isKinematic = false;
                PickupDock? quiver = owner != null ? AutoDock.ReturnQuiver(owner) : null;
                PickupDock.AllowedResult result = quiver != null ? arrow.DockInto(null, quiver, 1, true) : PickupDock.AllowedResult.Ok;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: arrow {arrow.Entity.Identifier} kept: {(quiver == null ? "no quiver with room, dropped at the bow" : result == PickupDock.AllowedResult.Ok ? "back in the quiver" : "quiver refused (" + result + "), dropped at the bow")}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: returning the kept arrow failed: {e.Message}"); }
        }

        // ---------------------------------------------------------------- beam damage and speed

#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(ExtraArrowUpdater), nameof(ExtraArrowUpdater.CreateExtraArrow))]
        static class BeamCreated {
            static readonly int T = Timing.Section("patch ExtraArrowUpdater.CreateExtraArrow / HolyArrow.BeamCreated");
            static void Postfix(ExtraArrowUpdater __instance, IShootable __result) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __result == null) return;
                    NetworkEntity entity = __result.transform.GetComponent<NetworkEntity>();
                    if (!IsBeam(entity)) return;
                    Traverse self = Traverse.Create(__instance);
                    BowString bow = self.Field("bowString").GetValue<BowString>();
                    ImpactTool arrowTool = self.Field("arrowImpactTool").GetValue<ImpactTool>();
                    PlayerController? shooter = bow != null && bow.Pickup != null
                        ? (bow.Pickup.Interactors.Count > 0 && bow.Pickup.Interactors[0].Controller != null ? bow.Pickup.Interactors[0].Controller.PlayerController : bow.Pickup.LastPlayerInteractor)
                        : null;
                    // The rating can sit on the arrow's head part rather than on the arrow itself, so look at every part.
                    // The multiplier of the shooter's Charge tier (Slowdraw's own table); Charge 1 alone gives x1.25.
                    int tier = Builds.ServerBuild(shooter != null ? shooter.NetworkPlayer : null).Charge;
                    float slow = Tree.ChargeMultiplier(tier);
                    float charged = slow * (arrowTool != null ? Hits.Power(arrowTool) : 1f);
                    ImpactTool beamTool = __result.transform.GetComponent<ImpactTool>();
                    if (beamTool != null) beamTool.OneOffMultiplier = direct.Value * charged;
                    if (beams.Count > 32) beams.Clear();
                    beams[entity.Identifier] = new BeamInfo {
                        shooter = shooter, centreDamage = blast.Value * charged, bow = bow != null && bow.Bow != null ? bow.Bow.transform : null,
                        channels = Channels(arrowTool), // the blast hurts what the sacrificed arrow could hurt
                    };
                    Telemetry.Beam(entity.Identifier, arrowTool); // balance telemetry: the beam's hit and blast find this arrow's shot (never throws)
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: beam ready, direct {direct.Value * charged:0.###}, blast centre {blast.Value * charged:0.###} (charge tier {tier} x{slow:0.##}); arrow: {Hits.Describe(arrowTool)}");
                    if (charged <= 0f) MelonLogger.Warning("[Archery] Holy: the sacrificed arrow has no damage rating (a shrine training arrow?), so this Holy Arrow will do no damage.");
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Holy: beam setup failed: {e}"); }
            }
        }
#endif

        static HashSet<ImpactChannel>? Channels(ImpactTool? arrowTool) {
            if (arrowTool == null) return null;
#if QUEST
            var set = new HashSet<ImpactChannel>(Il.ByPointer<ImpactChannel>());
#else
            var set = new HashSet<ImpactChannel>();
#endif
            foreach (ImpactTool t in Hits.Tools(arrowTool)) foreach (ImpactChannel c in t.Channels) if (c != null) set.Add(c);
            return set;
        }

        // ---------------------------------------------------------------- full strength on release

        const int StrengthCap = 32;
        static readonly Dictionary<uint, float> shotStrength = new Dictionary<uint, float>(); // arrow id -> strength as shot
        static uint shooting; // the arrow inside the Shooter.Shoot call running now (0 outside it), for the beam backstop

        /// <summary>Inside Shooter.Shoot only (the beam is launched by the same call): the strength the arrow being shot
        /// went out with, as this machine saw it.</summary>
        static bool StrengthOfShot(out float strength) {
            strength = 0f;
            return shooting != 0u && shotStrength.TryGetValue(shooting, out strength);
        }

        // Watching clients: whenever the server KEEPS a Holy Arrow's arrow ([HolyArrow] KeepsArrow, or RevampedSkills' arrow recovery,
        // which skips the game's destroy whatever KeepsArrow says), its relayed Shooter.Shoot finds the arrow, and the watcher shoots
        // the real arrow locally and resolves the release itself (Volley: "Charged ult:-"). A relayed shot of an arrow whose beam this
        // client just saw fire (BeamFiredSync: the server sends SyncFired inside its own Shoot, 3-12 ms before it relays the shot, and
        // clears the armed flag there, so the beam-fired record is what is checked) or that is still armed is shot with no arrow,
        // exactly the vanilla sacrificed path (which logs the game's own "Failed shooting arrow" NullReferenceException each time); the
        // beam is drawn from SyncFired, and the server moves the arrow. A sky shot (Skyfall, Festival, Fizzled) keeps its arrow and is
        // left alone. The record is taken when used, so the same arrow's next plain shot is untouched.
        [HarmonyPatch(typeof(Shooter), nameof(Shooter.Shoot))]
        static class WatcherSkipsKeptArrow {
            static readonly int T = Timing.Section("patch Shooter.Shoot / HolyArrow.WatcherSkipsKeptArrow");
            [HarmonyPriority(Priority.First)]
            static void Prefix(Shooter __instance, ref Pickup pickup, bool isFromNetwork) {
                using var _t = Timing.Begin(T);
                try {
                    if (!isFromNetwork || NetworkSceneManager.IsServer || pickup == null || pickup.Entity == null) return;
                    uint id = pickup.Entity.Identifier;
                    // a sky shot (Skyfall, Festival, Fizzled) keeps its arrow: its storm is drawn from OnShoot. Read from the relayed
                    // claim (filled during SyncShoot's args.Serialize); Skyfall.IsCast is decided only later, in Draws.Release.
                    if (Claims.TryRelay(id, out ShotClaim? c) && c != null && c.Sky != SkyCode.None) return;
                    bool fired = TakeBeamFired(id);
                    if (!fired && !ArmedFlagFor(Volley.BowForShooter(__instance), id)) return;
                    pickup = null!;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: relayed beam shot of kept arrow {id}: shot without the arrow, as a sacrificed one");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: kept-arrow check on a relayed shot failed: {e.Message}"); }
            }
        }

        // Every machine. With Holy Arrow armed the shooter raises the strength to full before the game builds the shot message
        // from it (Shooter.Shoot), so the server and spectators get the raised one; everyone records what the shot carried,
        // for the beam's speed (BeamShot).
        [HarmonyPatch(typeof(Shooter), nameof(Shooter.Shoot))]
        static class ReleaseStrength {
            static readonly int TPrefix = Timing.Section("patch Shooter.Shoot / HolyArrow.ReleaseStrength prefix");
            static void Prefix(Shooter __instance, Pickup pickup, ref float strength, bool isFromNetwork) {
                using var _t = Timing.Begin(TPrefix);
                shooting = 0u;
                try {
                    if (pickup == null || pickup.Entity == null) return;
                    uint id = pickup.Entity.Identifier;
                    if (!isFromNetwork && !NetworkSceneManager.IsServer) {
                        BowString? bow = Volley.BowForShooter(__instance);
                        if (bow != null && ArmedOnClient(bow)) {
                            float before = strength;
                            strength = HolyGate.ReleaseStrength(strength, true);
                            if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: arrow {id} released at pull {before.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}: full strength (Holy Arrow armed)");
                        }
                    }
                    if (shotStrength.Count >= StrengthCap && !shotStrength.ContainsKey(id)) shotStrength.Clear();
                    shotStrength[id] = strength;
                    shooting = id;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: release strength failed: {e.Message}"); }
            }

            static void Postfix() => shooting = 0u;
        }

        // Runs on the shooter, the server and other clients. The game clamps this velocity to 100 m/s on the way
        // in, so the real speed is set after it returns.
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Shoot), new[] { typeof(Vector3), typeof(ColliderGroup) })]
        static class BeamShot {
            static readonly int TPostfix = Timing.Section("patch Projectile.Shoot / HolyArrow.BeamShot postfix");
            static void Prefix(Vector3 velocity, out Vector3 __state) => __state = velocity;

            static void Postfix(Projectile __instance, Vector3 __state) {
                using var _t = Timing.Begin(TPostfix);
                try {
                    if (__instance == null || boosted.ContainsKey(__instance) || !IsBeam(__instance.Entity)) return;
                    // The launch speed is linear in the shot's strength (Shooter.Shoot), so a light release is worked back
                    // to the full-draw speed: an armed beam always flies at full draw (the shooter's ReleaseStrength prefix
                    // raises the strength too; this covers a shooter whose client missed or lagged the arming).
                    bool full = StrengthOfShot(out float s) && s >= 0.05f && s <= 1f;
                    Vector3 v = full ? __state.normalized * (__state.magnitude / s) * speed.Value : __state * speed.Value;
                    if (boosted.Count > 32) boosted.Clear();
                    boosted[__instance] = v;
                    __instance.Rigidbody.useGravity = false; // flies dead straight and lands where it was aimed
                    __instance.Rigidbody.velocity = v;
                    if (NetworkSceneManager.IsServer) {
                        beams.TryGetValue(__instance.Entity.Identifier, out BeamInfo info);
                        var guard = __instance.gameObject.AddComponent<TunnelGuard>();
                        guard.projectile = __instance; guard.info = info;
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: fired, speed {v.magnitude:0} ({(full ? $"full draw from strength {s:0.00}" : "as shot")})");
                    }
                    if (!ApplicationManager.IsHeadless) {
                        Trail.Begin(__instance);
                        if (Logs.Debug) MelonLogger.Msg("[Archery] Holy: trail");
                    }
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Holy: shot failed: {e}"); }
            }
        }

        // The game's "it was fired" message resets the beam's velocity on clients; put ours back.
        // On every other client this message is the only sign the beam was fired: the server destroys the sacrificed arrow
        // (LightBeamSkill.DestroyShotArrow) before it relays the shot, so the relayed Shooter.Shoot finds no arrow, stops,
        // and never reaches ExtraArrowUpdater.ShootExtraArrows or Projectile.Shoot (BeamShot) there. Start the trail here.
        [HarmonyPatch(typeof(Projectile), "SyncFired")]
        static class BeamFiredSync {
#if QUEST
            static readonly int T = Timing.Section("patch Projectile.SyncFired / HolyArrow.BeamFiredSync");
            static void Postfix(Projectile __instance, Il2CppAlta.Serialization.Stream stream) {
#else
            static readonly int T = Timing.Section("patch Projectile.SyncFired / HolyArrow.BeamFiredSync");
            static void Postfix(Projectile __instance, Alta.Serialization.Stream stream) {
#endif
                using var _t = Timing.Begin(T);
                if (stream == null || !stream.IsReading || NetworkSceneManager.IsServer || __instance == null) return;
                if (boosted.TryGetValue(__instance, out Vector3 v)) { __instance.Rigidbody.useGravity = false; __instance.Rigidbody.velocity = v; return; }
                try {
                    if (!SeenBeamRule.DrawsFired(false, ApplicationManager.IsHeadless, IsBeam(__instance.Entity), false, seen.ContainsKey(__instance))) return;
                    BowString? bow = BowHolding(__instance, out string name, out bool local);
                    if (bow != null) {
                        if (clientArmed.TryGetValue(bow, out uint armedArrow)) NoteBeamFired(armedArrow);   // for WatcherSkipsKeptArrow
                        ClearClientArmed(bow); // Volley.Fire never runs here for this shot
                    }
                    float brightness = SeenBeamRule.TrailBrightness(local, Volley.OtherArcherBrightness);
                    if (seen.Count > 32) seen.Clear();
                    seen[__instance] = new SeenBeam { trail = Trail.Begin(__instance, brightness, follow: false), name = name };
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: drawing '{name}'s beam (seen) from {__instance.transform.position}, brightness {brightness:0.##}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: drawing another archer's beam failed: {e.Message}"); }
            }
        }

        // Clients: beams fired by another archer (their trail and name), from the beam's "fired" message to its landing.
        class SeenBeam { public Trail? trail; public string name = "?"; }
#if QUEST
        static readonly Dictionary<Projectile, SeenBeam> seen = new Dictionary<Projectile, SeenBeam>(Il.ByPointer<Projectile>());
#else
        static readonly Dictionary<Projectile, SeenBeam> seen = new Dictionary<Projectile, SeenBeam>();
#endif

        /// <summary>Clients: the bow string a synced beam hangs on (ExtraArrowUpdater.SyncExtraArrows parents it to the bow),
        /// with its archer's name and whether that is the local player.</summary>
        static readonly int T_BowHolding = Timing.Section("HolyArrow.BowHolding");
        static BowString? BowHolding(Projectile beam, out string name, out bool local) {
            using var _t = Timing.Begin(T_BowHolding);
            name = "?"; local = false;
            Transform parent = beam.transform.parent;
            BowString? bow = parent != null ? parent.GetComponentInChildren<BowString>() : null;
            if (bow == null) {
                // H4 (H23-review-opus minor 8): the registry of bow strings (Volley.BowRegistry), no scene search; active ones only, as before
                foreach (BowString bs in Volley.BowRegistry.All())
                    if (bs != null && bs.gameObject.activeInHierarchy && bs.Bow != null && beam.transform.IsChildOf(bs.Bow.transform)) { bow = bs; break; }
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: a seen beam's bow found by looking through every bow string ({(bow != null ? bow.name : "none found")}; parent '{(parent != null ? parent.name : "none")}')");
            }
            PlayerController? pc = bow != null && bow.Pickup != null ? bow.Pickup.LastPlayerInteractor : null;
            if (pc != null) { name = Builds.NameOf(pc.NetworkPlayer); local = pc.IsLocalPlayer; }
            return bow;
        }

        // Server: a beam this fast can step straight through a thin target between physics ticks. Check the gap.
        class TunnelGuard : MonoBehaviour {
#if QUEST
            public TunnelGuard(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            internal Projectile projectile = null!;
            internal BeamInfo? info;
            Vector3 previous;
            void Start() => previous = transform.position;
            static readonly int TFixedUpdate = Timing.Section("FixedUpdate HolyArrow.TunnelGuard");
            void FixedUpdate() {
                using var _t = Timing.Begin(TFixedUpdate);
                if (projectile == null || projectile.HasCollided) { Destroy(this); return; }
                Vector3 now = transform.position, step = now - previous;
                Transform? shooter = info != null && info.shooter != null ? info.shooter.transform : null;
                if (step.sqrMagnitude > 1e-6f && Hits.Cast(previous, step, step.magnitude, out RaycastHit hit, shooter, info?.bow, transform)) {
                    transform.position = hit.point;
                    ImpactTool tool = GetComponent<ImpactTool>();
                    if (tool != null) Hits.Apply(tool, hit, tool.OneOffMultiplier, out _, players: false); // a player takes the blast (its own PvP rule), not the beam
                    onCollision?.Invoke(projectile, new object[] { hit.point, hit.normal, hit.collider });
                    Destroy(this);
                    return;
                }
                previous = now;
            }
        }

        // ---------------------------------------------------------------- server: the blast

#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(Projectile), "OnCollision")]
        static class Landed {
            static readonly int T = Timing.Section("patch Projectile.OnCollision / HolyArrow.Landed");
            static void Prefix(Projectile __instance, Vector3 point, Collider collider) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || __instance.HasCollided || __instance.Entity == null) return;
                    if (!beams.TryGetValue(__instance.Entity.Identifier, out BeamInfo info)) return;
                    beams.Remove(__instance.Entity.Identifier);
                    Blast(point, collider, info, __instance.Entity);
                    if (!ApplicationManager.IsHeadless) Impact.Play(point, -__instance.transform.forward);
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Holy: blast failed: {e}"); }
            }
        }
#endif

#if QUEST
        static readonly HashSet<HealthObject> healths = new HashSet<HealthObject>(Il.ByPointer<HealthObject>());
#else
        static readonly HashSet<HealthObject> healths = new HashSet<HealthObject>();
#endif

        // Same recipe as the game's own explosives: who is in range, damage scaled by distance, a shove.
        static readonly int T_Blast = Timing.Section("hit HolyArrow.Blast");
        static void Blast(Vector3 point, Collider? directHit, BeamInfo info, NetworkEntity beam) {
            using var _t = Timing.Begin(T_Blast);
            ImpactDetector? directDetector = directHit != null ? directHit.GetComponentInParent<ImpactDetector>() : null;
            HealthObject? directHealth = directDetector != null ? directDetector.HealthTarget : null;
            healths.Clear(); rejected.Clear();
            foreach (Collider c in Physics.OverlapSphere(point, radius.Value)) {
                if (c == null || c.isTrigger) continue;
                // Only things with health (creatures, players), and only if the arrow could hurt them: the same
                // filter the game's explosives use. Trees, fuses and crafting nails are left alone.
                ImpactDetector d = c.GetComponentInParent<ImpactDetector>();
                if (d != null && d.HealthTarget != null) {
                    if (Accepts(d, info.channels)) healths.Add(d.HealthTarget); else rejected.Add(d.HealthTarget);
                }
                if (blastForce.Value > 0f && c.attachedRigidbody != null) c.attachedRigidbody.AddExplosionForce(blastForce.Value, point, radius.Value);
            }
            // The directly hit target already took the direct hit; it does not take the blast on top.
            // Players are the exception: the beam's direct hit never deals to a player, so they get the blast (with PvP only).
#if QUEST
            if (directHealth != null && !(directHealth.Is<PlayerHealth>())) healths.Remove(directHealth);
#else
            if (directHealth != null && !(directHealth is PlayerHealth)) healths.Remove(directHealth);
#endif

            int count = 0, pvpSpared = 0; string stunLog = "";
            foreach (HealthObject h in healths) {
                if (h == null) continue;
#if QUEST
                Vector3 at = h.PositionHandler != null ? h.PositionHandler.GetPosition.Invoke(point) : h.transform.position;
#else
                Vector3 at = h.PositionHandler != null ? h.PositionHandler.GetPosition(point) : h.transform.position;
#endif
                float f = BlastMath.Fraction(Vector3.Distance(at, point), fullRadius.Value, radius.Value, edgeFraction.Value);
                if (f <= 0f) continue;
#if QUEST
                bool player = h.Is<PlayerHealth>();
#else
                bool player = h is PlayerHealth;
#endif
                // play-test 7: a plain ImpactHit never meets the game's PvP gate, so the blast applies it itself (PvpRule.HolyBlast):
                // nothing with the server's PvP off or on the archer, else the server's PVPMultiplier / PVPCrippleMultiplier
                float healthK = 1f, crippleK = 1f;
                if (player) {
                    if (!Pvp.HolyBlast(info.shooter, (PlayerHealth)h, out healthK, out crippleK)) { pvpSpared++; continue; }
                    healthK *= playerShare.Value;
                }
                float before = h.HasCrippleStat ? h.CrippleStat.Value : 0f;
                var hit = new ImpactHit(info.centreDamage * healthK, cripple.Value * crippleK, 1f, f, null, ImpactType.Spiritual,
                                        point, Vector3.zero, Vector3.zero, null, beam, info.shooter);
                h.DetectImpact(hit);
                count++;
                if (player && stunLog.Length == 0 && h.HasCrippleStat) stunLog = $", a player's cripple stat {before:0.00} -> {h.CrippleStat.Value:0.00}";
            }
            rejected.ExceptWith(healths);
            string skipped = "";
            foreach (HealthObject h in rejected) { if (h != null && skipped.Length < 120) skipped += (skipped.Length > 0 ? ", " : "") + h.name; }
            string directName = directHealth != null ? directHealth.name : directHit != null ? directHit.name : "nothing";
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: blast at {point}, direct hit on '{directName}', {count} of {healths.Count} thing(s) with health in range hurt, " +
                            $"{rejected.Count} left alone as not arrow targets{(skipped.Length > 0 ? " (" + skipped + ")" : "")}, centre {info.centreDamage:0.###}, cripple {cripple.Value}{stunLog}" +
                            (pvpSpared > 0 ? $", {pvpSpared} player(s) spared (the server's PvP is off, or the archer)" : ""));
            healths.Clear(); rejected.Clear();
        }
#if QUEST
        static readonly HashSet<HealthObject> rejected = new HashSet<HealthObject>(Il.ByPointer<HealthObject>());
#else
        static readonly HashSet<HealthObject> rejected = new HashSet<HealthObject>();
#endif

        static bool Accepts(ImpactDetector d, HashSet<ImpactChannel>? channels) {
            if (channels == null || channels.Count == 0 || d.Channels == null) return true; // nothing to compare: do not silently hit nothing
            foreach (ImpactChannel c in d.Channels) if (c != null && channels.Contains(c)) return true;
            return false;
        }

        // ---------------------------------------------------------------- clients: status, trail, impact

        // The game broadcasts when someone's Holy Arrow starts charging, is charged, is cancelled.
        [HarmonyPatch(typeof(PickupBasedSkill), nameof(PickupBasedSkill.SendSerializedMessage))]
        static class Status {
            static readonly int T = Timing.Section("patch PickupBasedSkill.SendSerializedMessage / HolyArrow.Status");
#if QUEST
            static void Postfix(PickupBasedSkill __instance, IPlayer __0, Il2CppAlta.Serialization.Stream __1) {
                ToolBasedSkillStatus ___status = __instance.status;
                uint ___pickupIdentifier = __instance.pickupIdentifier;
#else
            static void Postfix(PickupBasedSkill __instance, IPlayer __0, Alta.Serialization.Stream __1, ToolBasedSkillStatus ___status, uint ___pickupIdentifier) {
#endif
                using var _t = Timing.Begin(T);
#if QUEST
                if (!__instance.Is<LightBeamSkill>() || __1 == null || !__1.IsReading || ApplicationManager.IsHeadless || NetworkSceneManager.IsServer) return;
#else
                if (!(__instance is LightBeamSkill) || __1 == null || !__1.IsReading || ApplicationManager.IsHeadless || NetworkSceneManager.IsServer) return;
#endif
                ToolBasedSkillStatus status = ___status;
                bool local = __0 != null && __0.IsLocalPlayer;
                try {
#if QUEST
                    NetworkSceneManager.Current.Spawner.WaitForSpawn(___pickupIdentifier, Il.Keep(DelegateSupport.ConvertDelegate<EntitySpawnedHandler>(new Action<NetworkEntity>(entity => {
                        try {
                            BowString? bow = Volley.BowOf(entity);
                            if (bow == null) return;
                            ChargeFx.NoteArcher(bow, Builds.NameOf(__0), local);
#else
                    NetworkSceneManager.Current.Spawner.WaitForSpawn(___pickupIdentifier, entity => {
                        try {
                            BowString? bow = Volley.BowOf(entity);
                            if (bow == null) return;
                            ChargeFx.NoteArcher(bow, Builds.NameOf(__0), local);
#endif
                            try { ChargeFx.Holy(bow, status, entity.Identifier); } // the Fill's squeeze and armed core
                            catch (Exception e) { MelonLogger.Warning($"[Archery] Fill: Holy status failed: {e.Message}"); }
                            if (status == ToolBasedSkillStatus.ChargingStarted) Volley.SetHolyCharging(bow, true);
                            else if (status == ToolBasedSkillStatus.Cancelled) { Volley.SetHolyCharging(bow, false); SetClientArmed(bow, 0u); }
                            else if (status == ToolBasedSkillStatus.Charged) {
                                SetClientArmed(bow, entity.Identifier); // the arrow on the string (BowDrawSkill: StringDock.DockedPickup)
                                Volley.Consume(bow);
                                Arrow arrow = entity.GetComponent<Arrow>();
                                Vector3 at = arrow != null && arrow.ArrowTip != null ? arrow.ArrowTip.transform.position : entity.transform.position;
                                if (readyFlash.Value > 0f) Fx.Flash(at, Gold, readyFlash.Value, 3f, 0.25f);
#if QUEST
                                if (local && Player.Current.Is(out Player me) && me.PlayerController != null) {
#else
                                if (local && Player.Current is Player me && me.PlayerController != null) {
#endif
                                    me.PlayerController.LeftController?.TriggerHaptic(1f, 0.2f);
                                    me.PlayerController.RightController?.TriggerHaptic(1f, 0.2f);
                                }
                            }
                        }
#if QUEST
                        catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: status handling failed: {e.Message}"); }
                    }))));
#else
                        catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: status handling failed: {e.Message}"); }
                    });
#endif
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: status lookup failed: {e.Message}"); }
            }
        }

        [HarmonyPatch(typeof(Projectile), "SyncCollided")]
        static class LandedOnClient {
#if QUEST
            static readonly int T = Timing.Section("patch Projectile.SyncCollided / HolyArrow.LandedOnClient");
            static void Postfix(Projectile __instance, Il2CppAlta.Serialization.Stream stream) {
#else
            static readonly int T = Timing.Section("patch Projectile.SyncCollided / HolyArrow.LandedOnClient");
            static void Postfix(Projectile __instance, Alta.Serialization.Stream stream) {
#endif
                using var _t = Timing.Begin(T);
                try {
                    if (stream == null || !stream.IsReading || NetworkSceneManager.IsServer || ApplicationManager.IsHeadless || __instance == null) return;
                    bool shotHere = boosted.Remove(__instance);
                    bool seenFired = seen.TryGetValue(__instance, out SeenBeam s);
                    seen.Remove(__instance);
                    switch (SeenBeamRule.Landing(false, false, shotHere, seenFired, !shotHere && !seenFired && IsBeam(__instance.Entity))) {
                        case BeamLanding.Own:
                            Impact.Play(__instance.SurfacePoint, __instance.SurfaceNormal);
                            break;
                        case BeamLanding.Seen:
                            // The trail ends exactly where the server says the beam landed, however the beam moved here.
                            if (s != null && s.trail != null) s.trail.EndAt(__instance.SurfacePoint);
                            string name = s != null ? s.name : "?";
                            string missed = "";
                            if (!seenFired) {
                                // The fired message was missed (or came after this): no trail exists for this beam. Draw the
                                // bolt now, from the bow string holding the beam (else the beam copy), ended at the landing at
                                // once, so the blast never shows without its bolt. One landing per beam (collidesOnlyOnce), so
                                // this is said once per beam.
                                BowString? bow = BowHolding(__instance, out name, out bool local);
                                Vector3 from = bow != null ? bow.transform.position : __instance.transform.position;
                                Trail? late = Trail.Begin(__instance, SeenBeamRule.TrailBrightness(local, Volley.OtherArcherBrightness), follow: false, from: from);
                                if (late != null) late.EndAt(__instance.SurfacePoint);
                                missed = $", its fired message was missed: bolt drawn from {(bow != null ? "the bow string" : "the beam copy")} at {from}{(late == null ? " (no line material: no bolt)" : "")}";
                            }
                            if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: drawing '{name}'s beam landing (seen) at {__instance.SurfacePoint}{missed}");
                            Impact.Play(__instance.SurfacePoint, __instance.SurfaceNormal);
                            break;
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Holy: impact visual failed: {e.Message}"); }
            }
        }

        // The arrow flies dead straight; the trail it leaves is a jagged, flickering bolt along that path.
        class Trail : MonoBehaviour {
#if QUEST
            public Trail(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            Projectile projectile = null!;
            LineRenderer line = null!;
            readonly List<Vector3> path = new List<Vector3>();
            readonly List<Vector3> jitter = new List<Vector3>();
            float sinceFork, endedAt = -1f, nextRoll, brightness = 1f, born;
            // false for another archer's beam: the spectator's copy never really flies (the fired message carries velocity 0,
            // it stays on the bow), so the trail does not follow it; it is drawn from the start point to EndAt's landing.
            bool follow = true;
            Projectile? seenKey; // the key in `seen`, removed when this trail ends

            /// <summary>brightness: 1 for the local archer's beam (BeamShot), [Volley] OtherArcherBrightness for another's.
            /// follow: false for another archer's beam (see `follow`). from: where the bolt starts (default: the beam's position).</summary>
            static readonly int T_Begin = Timing.Section("look start HolyArrow.Trail.Begin");
            internal static Trail? Begin(Projectile p, float brightness = 1f, bool follow = true, Vector3? from = null) {
                using var _t = Timing.Begin(T_Begin);
                if (Fx.LineMaterial() == null) return null;
                var t = new GameObject("HolyArrowTrail").AddComponent<Trail>();
                t.projectile = p;
                t.brightness = brightness;
                t.follow = follow;
                t.born = Time.time;
                if (!follow) t.seenKey = p;
                t.line = Fx.NewLine(t.gameObject, 0, trailWidth.Value, false);
                t.line.numCornerVertices = 0; // sharp kinks
                t.path.Add(from ?? p.transform.position);
                t.jitter.Add(Vector3.zero);
                return t;
            }

            /// <summary>A seen beam: close the bolt at the landing point the server sent (the end point is never jittered).</summary>
            /// A seen trail gets in-between points every 0.6 m (as the shooter's trail samples its beam) so it kinks the same way.
#if QUEST
            [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
#endif
            internal void EndAt(Vector3 point) {
                if (endedAt >= 0f || path.Count == 0) return;
                Vector3 last = path[path.Count - 1];
                float d = Vector3.Distance(point, last);
                if (d > 0.05f) {
                    int steps = follow ? 1 : Mathf.Clamp(Mathf.CeilToInt(d / 0.6f), 1, 200);
                    for (int i = 1; i <= steps; i++) { path.Add(Vector3.Lerp(last, point, (float)i / steps)); jitter.Add(Vector3.zero); }
                }
                endedAt = Time.time;
            }

            void OnDestroy() {
                if (seenKey is null) return;
                if (seen.TryGetValue(seenKey, out SeenBeam s) && s.trail == this) seen.Remove(seenKey);
            }

            static readonly int TLateUpdate = Timing.Section("LateUpdate HolyArrow.Trail");
            void LateUpdate() {
                using var _t = Timing.Begin(TLateUpdate);
                Color gold = Gold;
                if (!follow && endedAt < 0f) {
                    // Another archer's beam: nothing to draw until the landing is known; no landing in time (it hit nothing,
                    // or landed outside this client's chunks) or the beam is gone: drop the trail.
                    float limit = Mathf.Max(0.1f, seenTrailSeconds.Value);
                    if (projectile == null || Time.time - born >= limit) {
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: another archer's beam trail dropped, {(projectile == null ? "the beam was removed" : $"no landing within {limit:0.#} s")}");
                        Destroy(gameObject);
                    }
                    return;
                }
                if (endedAt < 0f) {
                    if (projectile == null || projectile.HasCollided) endedAt = Time.time;
                    else {
                        Vector3 p = projectile.transform.position, last = path[path.Count - 1];
                        float d = Vector3.Distance(p, last);
                        if (d >= 0.6f) {
                            path.Add(p); jitter.Add(Vector3.zero);
                            float every = Mathf.Max(0.5f, forkEvery.Value); // 0 or less = no forks
                            if (forkEvery.Value > 0f) for (sinceFork += d; sinceFork >= every; sinceFork -= every)
                                Fx.Bolt(p, p + UnityEngine.Random.onUnitSphere * 0.5f, 0.08f, 0.2f, trailWidth.Value * 0.6f,
                                        brightness < 1f ? new Color(1f, 1f, 1f, brightness) : Color.white,
                                        brightness < 1f ? new Color(gold.r, gold.g, gold.b, gold.a * brightness) : gold, 3);
                        }
                    }
                }
                float fade = 0f;
                if (endedAt >= 0f) {
                    fade = (Time.time - endedAt) / Mathf.Max(0.05f, trailLinger.Value);
                    if (fade >= 1f) { Destroy(gameObject); return; }
                }
                if (path.Count < 2) return;
                if (Time.time >= nextRoll) {
                    nextRoll = Time.time + 0.05f;
                    Fx.Basis((path[path.Count - 1] - path[0]).normalized, out Vector3 u, out Vector3 w);
                    for (int i = 1; i < path.Count - 1; i++)
                        jitter[i] = (u * UnityEngine.Random.Range(-1f, 1f) + w * UnityEngine.Random.Range(-1f, 1f)) * trailKink.Value;
                }
                line.positionCount = path.Count;
                for (int i = 0; i < path.Count; i++) line.SetPosition(i, path[i] + jitter[i]);
                float a = (1f - fade) * brightness;
                line.startColor = new Color(gold.r, gold.g, gold.b, a);
                line.endColor = new Color(1f, 1f, 1f, a);
            }
        }

        // What everyone sees where it lands. Brightness means damage: a bright core, rings racing outward along
        // the ground one after another and dimming until they die at the edge, bolts thrown up that are long and
        // bright near the centre and short and faint near the edge, then the ground keeps crackling for a while,
        // thinning out. No dome. Every part has its own setting.
        class Impact : MonoBehaviour {
#if QUEST
            public Impact(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            LineRenderer[] rings = new LineRenderer[0];
            Vector3 point, normal, u, w;
            float born, nextCrackle;

            static readonly int T_Play = Timing.Section("look start HolyArrow.Impact.Play");
            internal static void Play(Vector3 point, Vector3 normal) {
                using var _t = Timing.Begin(T_Play);
                if (Fx.LineMaterial() == null) return;
                if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
                Color gold = Gold;
                if (Physics.Raycast(point + normal * 0.5f, -normal, out RaycastHit ground, 2f, ~0, QueryTriggerInteraction.Ignore)) normal = ground.normal;

                var go = new GameObject("HolyArrowImpact");
                go.transform.SetPositionAndRotation(point + normal * 0.03f, Quaternion.LookRotation(normal));
                Impact fx = go.AddComponent<Impact>();
                fx.point = point; fx.normal = normal; fx.born = Time.time;
                Fx.Basis(normal, out fx.u, out fx.w);
                fx.rings = new LineRenderer[Mathf.Clamp(ringCount.Value, 1, 6)];
                for (int i = 0; i < fx.rings.Length; i++) {
                    var ringGo = new GameObject("ring");
                    ringGo.transform.SetParent(go.transform, false);
                    LineRenderer r = ringGo.AddComponent<LineRenderer>();
                    r.material = Fx.LineMaterial();
                    Fx.BuildCircle(r);
                    r.widthMultiplier = ringWidth.Value;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = false;
                    fx.rings[i] = r;
                }

                if (flash.Value > 0f) Fx.Flash(point + normal * 0.3f, gold, flash.Value, radius.Value + 3f, Mathf.Max(0.05f, flashSeconds.Value));

                int n = Mathf.Max(0, wisps.Value);
                for (int i = 0; i < n; i++) {
                    float ang = (i + UnityEngine.Random.value) * Mathf.PI * 2f / n;
                    Vector3 dir = fx.u * Mathf.Cos(ang) + fx.w * Mathf.Sin(ang);
                    float start = UnityEngine.Random.Range(0.2f, radius.Value * 0.85f);
                    float f = BlastMath.Fraction(start, fullRadius.Value, radius.Value, edgeFraction.Value);
                    float len = 0.4f + 1.8f * f * f;
                    Vector3 a = point + dir * start + normal * 0.05f;
                    Fx.Bolt(a, a + (dir + normal * UnityEngine.Random.Range(0.3f, 1.2f)).normalized * len, 0.1f * len, 0.3f + 0.3f * f, 0.025f,
                            new Color(1f, 1f, 1f, f), new Color(gold.r, gold.g, gold.b, f), 3, UnityEngine.Random.Range(0f, 0.15f));
                }

                string effect = "none", sound = "none";
                try { PooledObjectDefinition? def = Fx.FindEffect(impactEffects.Value); if (def != null) { def.Get(point, Quaternion.LookRotation(normal)); effect = def.name; } }
                catch (Exception e) { effect = "failed: " + e.Message; }
                try { SoundEffectSet? s = Fx.FindSound(impactSounds.Value); if (s != null) { s.PlayAtPosition(point); sound = s.name; } }
                catch (Exception e) { sound = "failed: " + e.Message; }

                int marked = StunMarker.MarkAll(point);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: impact using '{effect}' / '{sound}', stun marker on {marked}");
            }

            static readonly int TUpdate = Timing.Section("Update HolyArrow.Impact");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float age = Time.time - born;
                float ringLife = Mathf.Max(0.05f, ringSeconds.Value), gap = Mathf.Max(0f, ringGap.Value);
                if (age >= Mathf.Max(ringLife + gap * (rings.Length - 1), crackleSeconds.Value)) { Destroy(gameObject); return; }
                Color gold = Gold;

                for (int i = 0; i < rings.Length; i++) {
                    LineRenderer ring = rings[i];
                    float t = (age - i * gap) / ringLife;
                    if (t < 0f || t >= 1f) { ring.enabled = false; continue; }
                    float r = Mathf.Lerp(0.3f, radius.Value, 1f - (1f - t) * (1f - t)); // fast out, slowing
                    ring.transform.localScale = Vector3.one * r;
                    float f = BlastMath.Fraction(r, fullRadius.Value, radius.Value, edgeFraction.Value);
                    float a = Mathf.Clamp01(f * f * (1f - t * 0.6f)) / (1f + 0.6f * i);     // each following ring a little fainter
                    ring.startColor = ring.endColor = new Color(gold.r, gold.g, gold.b, a);
                    ring.enabled = true;
                }

                // The surface keeps crackling: small arcs skittering along it, in bursts that thin out.
                float crackle = crackleSeconds.Value;
                if (age < crackle && Time.time >= nextCrackle) {
                    float k = age / crackle; // 0 -> 1 as it dies down
                    nextCrackle = Time.time + Mathf.Lerp(0.05f, 0.3f, k);
                    int arcs = Mathf.Max(1, Mathf.RoundToInt(crackleArcs.Value * (1f - 0.6f * k)));
                    for (int i = 0; i < arcs; i++) {
                        float ang = UnityEngine.Random.value * Mathf.PI * 2f;
                        float d = UnityEngine.Random.Range(0.1f, radius.Value * 0.9f);
                        float f = BlastMath.Fraction(d, fullRadius.Value, radius.Value, edgeFraction.Value);
                        Vector3 a = point + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * d + normal * 0.04f;
                        float turn = UnityEngine.Random.value * Mathf.PI * 2f;
                        float len = crackleLength.Value * (0.4f + 0.6f * f) * (1f - 0.5f * k);
                        Vector3 b = a + (u * Mathf.Cos(turn) + w * Mathf.Sin(turn)) * len + normal * UnityEngine.Random.Range(0f, 0.15f);
                        float alpha = f * (1f - 0.5f * k);
                        Fx.Bolt(a, b, 0.14f * len, 0.1f, 0.012f, new Color(1f, 1f, 1f, alpha), new Color(gold.r, gold.g, gold.b, alpha), 4);
                    }
                }
            }
        }

        // Like a taser: a few thin, fast arcs snapping across whatever was stunned, in bursts that thin out.
        // Few at once, each very brief, sharp kinks, clinging to the body: that is what keeps it from being clutter.
        class StunMarker : MonoBehaviour {
#if QUEST
            public StunMarker(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            Collider target = null!;
            float born, life, nextBurst;

            static readonly int T_MarkAll = Timing.Section("look HolyArrow.StunMarker.MarkAll (overlap)");
            internal static int MarkAll(Vector3 point) {
                using var _t = Timing.Begin(T_MarkAll);
#if QUEST
                var seen = new HashSet<HealthObject>(Il.ByPointer<HealthObject>());
#else
                var seen = new HashSet<HealthObject>();
#endif
                int count = 0;
                foreach (Collider c in Physics.OverlapSphere(point, radius.Value)) {
                    if (c == null || c.isTrigger) continue;
                    ImpactDetector d = c.GetComponentInParent<ImpactDetector>();
                    HealthObject h = d != null ? d.HealthTarget : null!;
                    // Only things that can actually be stunned get the marker (not ore, plants and the like).
                    if (h == null || !h.HasCrippleStat || !seen.Add(h)) continue;
                    float f = BlastMath.Fraction(Vector3.Distance(c.bounds.center, point), fullRadius.Value, radius.Value, edgeFraction.Value);
                    if (f <= 0f) continue;
                    var m = new GameObject("HolyArrowStun").AddComponent<StunMarker>();
                    m.target = c; m.born = Time.time; m.life = stunSeconds.Value * f;
                    count++;
                }
                return count;
            }

            static readonly int TUpdate = Timing.Section("Update HolyArrow.StunMarker");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float t = Time.time - born;
                if (target == null || t >= life) { Destroy(gameObject); return; }
                if (Time.time < nextBurst) return;
                float k = t / life;                       // 0 -> 1 as the stun wears off
                nextBurst = Time.time + Mathf.Lerp(0.15f, 0.6f, k);
                Bounds b = target.bounds;
                Color gold = Gold;
                int arcs = k < 0.5f ? 3 : 2;
                for (int i = 0; i < arcs; i++) {
                    Vector3 a = b.center + Vector3.Scale(UnityEngine.Random.insideUnitSphere, b.extents);
                    Vector3 c = b.center + Vector3.Scale(UnityEngine.Random.insideUnitSphere, b.extents);
                    Fx.Bolt(a, c, 0.06f, 0.06f, arcWidth.Value, Color.white, gold, UnityEngine.Random.Range(3, 5), i * 0.03f);
                }
            }
        }
    }
}
