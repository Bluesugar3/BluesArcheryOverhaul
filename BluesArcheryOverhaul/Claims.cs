using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // The shot claim: what the shooter's client says about its shot (kind, time since its 68% crossing, stage),
    // as a block at the very end of the shot message (after ShootArguments.Serialize). The shooter's client writes
    // it from its own resolution of the release, the server reads it, times it against its own draw clock, and relays
    // its own decision (kind and stage from Draws.Release, and as the time the one the server decided with: its own, or
    // the client's when trusted by ClaimTrust, never simply the client's) even when the shooter sent none; spectators
    // draw what the relay says. The block and its layout are in Wire.cs; the clock (Draws.cs) and store and trust
    // rules in Rules.cs.
    internal static class Claims {
        static MelonPreferences_Entry<int>? trustMs;

        internal static void Init() {
            var cat = Cfg.Category("Claim");
            trustMs = cat.CreateEntry("TrustMs", ClaimTrust.DefaultTrustMs, description:
                "Server: a shot's time since the 68% crossing is taken from the shooter's claim when the claim is ahead of the server's own time by at most this many milliseconds, " +
                "or when the server saw no crossing and the claim is at most this old (the server sees the pull late; play-test 2 measured 58 to 378 ms). " +
                "It decides echoes, the ghost ratchet, Burst's rack, the charge stages and whether a claimed snap counts. 0 = the server's own time only.");
        }

        /// <summary>Server: [Claim] TrustMs.</summary>
        internal static int TrustMs => trustMs != null ? trustMs.Value : ClaimTrust.DefaultTrustMs;

        /// <summary>Server: the time since the 68% crossing in this arrow's incoming claim (null when there is no claim or no time).</summary>
        internal static int? ClaimedMs(uint arrowId) =>
            NetworkSceneManager.IsServer && incoming.TryGet(arrowId, Time.time, out ShotClaim? c) && c != null ? c.TimeMs : null;

        /// <summary>Server: the kind in this arrow's incoming claim (null when there is no claim).</summary>
        internal static ShotKind? ClaimedKind(uint arrowId) =>
            NetworkSceneManager.IsServer && incoming.TryGet(arrowId, Time.time, out ShotClaim? c) && c != null ? c.Kind : (ShotKind?)null;

        /// <summary>Server, Skyfall (Skyfall.DecideServer): the Skyfall code and marks in this arrow's incoming (raw) claim, before the
        /// check cleared them; None with no marks otherwise. A shooter only ever asks Skyfall; any non-zero code counts as asking, so the
        /// server always holds an arrow its shooter may be holding.</summary>
        internal static SkyCode ClaimedSky(uint arrowId, out List<uint>? marks) {
            marks = null;
            if (!NetworkSceneManager.IsServer || !incoming.TryGet(arrowId, Time.time, out ShotClaim? c) || c == null) return SkyCode.None;
            marks = c.Marks;
            return c.Sky;
        }

        static readonly ClaimStore outgoing = new ClaimStore(); // arrow id -> claim this machine writes on its next shot message
        static readonly ClaimStore incoming = new ClaimStore(); // arrow id -> claim read from the last shot message
        static readonly Dictionary<uint, string> senders = new Dictionary<uint, string>();       // server: arrow id -> who sent its shot message
        static readonly HashSet<uint> networkShots = new HashSet<uint>(); // server: arrow ids whose shot came from a client, awaiting the server's decision
        static bool loggedLive, warnedPose;

        /// <summary>Server: the release decided for a client's shot (marked by the Shoot prefix) replaces the kind and stage
        /// of the claim it relays; with no claim from the shooter, one is made with an unknown time, so spectators always
        /// get the server's decision. The relayed time is <paramref name="effectiveMs"/>, the time the server decided with.
        /// Runs inside Shoot, before the relay is serialised (Shooter.SyncShoot).</summary>
        internal static void PutResolved(uint arrowId, Resolution r, int? effectiveMs, Build build) {
            if (!NetworkSceneManager.IsServer || !networkShots.Remove(arrowId)) return;
            float now = Time.time;
            ShotClaim claim = outgoing.TryGet(arrowId, now, out ShotClaim? had) && had != null ? had.Copy() : new ShotClaim { TimeMs = null };
            claim.Kind = r.Kind;
            claim.Stage = r.Plan.Stage;
            claim.TimeMs = effectiveMs;
            // H1: a lock the server kept homes only a volley as the server decided it (ghosts, no ultimate)
            if (claim.Steered && !LockRule.Steerable(r)) {
                LockClaim.Unsteer(claim, Targets.Wanted(build));
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek: arrow {arrowId}'s locks dropped: the server's release is no volley ({r}); an ordinary shot");
            }
            outgoing.Put(arrowId, claim, now);
        }

        /// <summary>Server, Skyfall (Skyfall.DecideServer, the last thing it does): the relay for a Skyfall release, cast or fizzle
        /// (SkyfallSeam.Relay over the claim it checked, or a new one: unsteered, Early, stage 0, no targets, the verdict's code and
        /// marks, the time it decided with), then the id is taken out of the shots awaiting PutResolved, so Draws.Release's own relay
        /// never overwrites it. In that order: a throw in the relay leaves the id for PutResolved. Unlike PutResolved it does not need
        /// the id among the client shots: an incoming request already proves a client sent it.</summary>
        internal static void PutSkyfall(uint arrowId, SkyVerdict verdict, int? effectiveMs) {
            if (!NetworkSceneManager.IsServer) return;
            float now = Time.time;
            ShotClaim? had = outgoing.TryGet(arrowId, now, out ShotClaim? c) && c != null ? c : null;
            outgoing.Put(arrowId, SkyfallSeam.Relay(had, verdict, effectiveMs), now);
            networkShots.Remove(arrowId);
        }

        /// <summary>H1: the locks this release homes on as this machine has them (the server's kept claim, the shooter's own, a
        /// spectator's relay), in lock order; empty when it is not steered.</summary>
        internal static List<uint> LocksOf(uint arrowId, bool local) {
            float now = Time.time;
            ShotClaim? c;
            bool got = NetworkSceneManager.IsServer || local ? outgoing.TryGet(arrowId, now, out c) : incoming.TryGet(arrowId, now, out c);
            return LockClaim.Of(got ? c : null);
        }

        /// <summary>Client: the server's relayed claim for another player's arrow.</summary>
        internal static bool TryRelay(uint arrowId, out ShotClaim? claim) {
            claim = null;
            return !NetworkSceneManager.IsServer && incoming.TryGet(arrowId, Time.time, out claim) && claim != null;
        }

        // A prefix on Shoot runs where a PreShoot handler would (Shooter.Shoot invokes PreShoot first).
        [HarmonyPatch(typeof(Shooter), nameof(Shooter.Shoot))]
        static class Shot {
            static readonly int T = Timing.Section("patch Shooter.Shoot / Claims.Shot");
            static void Prefix(Shooter __instance, Pickup pickup, float strength, bool isFromNetwork) {
                using var _t = Timing.Begin(T);
                try {
                    if (pickup == null || pickup.Entity == null || NetworkSceneManager.IsLocalTest) return;
                    uint id = pickup.Entity.Identifier;
                    outgoing.Remove(id); // first, so a throw below can never leave an older claim to ride this send
                    BowString? bow = Volley.BowForShooter(__instance);
                    float now = Time.time;

                    if (!NetworkSceneManager.IsServer) {
                        if (isFromNetwork) return;
                        Resolution r = bow != null ? Draws.ResolveLocal(bow) : new Resolution(ShotKind.Early, UltimateUse.None, default, 0);
                        var claim = new ShotClaim {
                            Kind = r.Kind,
                            Stage = r.Plan.Stage,
                            TimeMs = Draws.ElapsedMs(bow) // since the 68% crossing
                        };
                        // H1: Seeking Volley's locks lead the target list (steered, their count in the window bits); none without a lock
                        LockClaim.Put(claim, Seek.ClaimLocks(bow, pickup, r));
                        PutTargets(claim, pickup); // Ricochet's and Shatter's candidates (batches 3, 4) after them; nothing for anyone else
                        outgoing.Put(id, claim, now);
                        Skyfall.Ask(id, claim.TimeMs); // Skyfall (admin skill): a holder's candidate; the claim above is built exactly as for any shot
                        return;
                    }

                    if (!isFromNetwork) return; // the server's own shot never carries a client's claim
                    if (networkShots.Count > 256) networkShots.Clear();
                    networkShots.Add(id);

                    // The same inputs Draws.Release decides with later in this Shoot (Draws.ServerTime): the effective time.
                    int? serverMs = Draws.ElapsedMs(bow);
                    int? claimedMs = ClaimedMs(id);
                    int? effective = ClaimTrust.EffectiveMs(serverMs, claimedMs, TrustMs);
                    // G2: the timing line is Debug; Volley.Ghosts still runs at every level (it keeps the bow's "armed N" record)
                    int showing = bow != null ? Volley.Ghosts(bow, effective.HasValue ? effective.Value / 1000f : (float?)null) : 0;
                    bool got1 = incoming.TryGet(id, now, out ShotClaim? got) && got != null;
                    if (Logs.Debug) {
                        string deciding = $" -> deciding with {(effective.HasValue ? effective.Value.ToString(CultureInfo.InvariantCulture) : "none")} ms: " +
                                          ClaimTrust.Why(serverMs, claimedMs, TrustMs);
                        string name = senders.TryGetValue(id, out string n) ? n : "?";
                        string server = serverMs.HasValue ? serverMs.Value.ToString(CultureInfo.InvariantCulture) : "no crossing";
                        if (got1) {
                            int? high = got!.TimeMs;
                            int? diff = ClaimRules.Diff(high, serverMs);
                            string client = high.HasValue ? high.Value.ToString(CultureInfo.InvariantCulture) : "?";
                            string d = diff.HasValue ? diff.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture) : "?";
                            MelonLogger.Msg($"[Archery] Claim: timing arrow {id} from '{name}': client {client} ms, server {server} ms at receipt, " +
                                            $"diff {d} ms, within {ClaimRules.ToleranceMs} ms: {(ClaimRules.Agrees(high, serverMs) ? "yes" : "no")} | " +
                                            $"stage client {got.Stage}, server {showing}{deciding}");
                        }
                        else MelonLogger.Msg($"[Archery] Claim: timing arrow {id} from '{name}': no claim, server {server} ms at receipt{deciding}");
                    }
                    if (got1) outgoing.Put(id, Check(got!, bow, id, pickup, effective), now);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Claim: failed: {e}"); }
            }
        }

        // ---------------------------------------------------------------- the target list (batch 3, Ricochet; batch 4, Shatter)

        /// <summary>Shooter's client: a Multi main with Ricochet, or a Charge main with Shatter (Targets.Wanted), lists the creatures in the cone round the aim at release (Targets.ForShot);
        /// anyone else's claim carries no targets, exactly as before.</summary>
        static readonly int T_PutTargets = Timing.Section("Claims.PutTargets");
        static void PutTargets(ShotClaim claim, Pickup pickup) {
            using var _t = Timing.Begin(T_PutTargets);
            try {
                if (!Targets.Wanted(Builds.LocalBuild())) return;
#if QUEST
                Vector3? head = Targets.HeadOf(Player.Current?.TryCast<Player>());
#else
                Vector3? head = Targets.HeadOf(Player.Current as Player);
#endif
                if (!head.HasValue) return;
                // after any lock (H1), each id once, at most the wire's six; with no lock exactly the list ForShot gives (LockClaim.Merge)
                LockClaim.Merge(claim.Targets, Targets.ForShot(pickup.transform.position, pickup.transform.forward, head.Value), ShotClaimWire.MaxTargets);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Targets: listing the shot's creatures failed; none sent: {e.Message}"); }
        }

        /// <summary>Server: the checked copy of a client's claim that the relay carries. Targets survive only for a shooter whose
        /// build uses them (Ricochet, Shatter: Targets.Wanted), each checked here (TargetRule.Keep, from the shooter's head); everything else as before.</summary>
        static readonly int T_Check = Timing.Section("Claims.Check (server)");
        static ShotClaim Check(ShotClaim got, BowString? bow, uint id, Pickup? pickup, int? effectiveMs) {
            using var _t = Timing.Begin(T_Check, id);
            // H1: a steered claim (Seeking Volley's locks) from a build the orb works for: its locks checked with the server's slack,
            // the kept ones first; any other steered claim falls through to the checks below, which clear the steered bit as before
            if (got.Steered) {
                ShotClaim? homing = null;
                try { homing = Seek.Check(got, bow, id, pickup, effectiveMs); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Seek: checking arrow {id}'s locks failed; an ordinary volley: {e.Message}"); }
                if (homing != null) return homing;
            }
            if (got.Targets.Count == 0) return ClaimRules.ServerCheck(got);
            PlayerController? pc = bow != null && bow.Pickup != null ? bow.Pickup.LastPlayerInteractor : null;
            Player? shooter = pc != null ? pc.NetworkPlayer : null;
            Vector3? head = Targets.HeadOf(shooter);
            string Ids(List<uint> l) => string.Join(",", l.ConvertAll(t => t.ToString(CultureInfo.InvariantCulture)).ToArray());
            if (!Targets.Wanted(Builds.ServerBuild(shooter)) || !head.HasValue) {
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Targets: arrow {id} '{Builds.NameOf(shooter)}' named [{Ids(got.Targets)}]; dropped ({(head.HasValue ? "no Ricochet or Shatter for this build" : "the archer is not found")})");
                return ClaimRules.ServerCheck(got);
            }
            ShotClaim c = ClaimRules.ServerCheck(got, t => Targets.Facts(t, head.Value), Targets.Range);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Targets: arrow {id} '{Builds.NameOf(shooter)}' named [{Ids(got.Targets)}], the server keeps [{Ids(c.Targets)}] (within {Targets.Range.ToString("0.#", CultureInfo.InvariantCulture)} m, alive creatures)");
            return c;
        }

        /// <summary>The target list of this release as this machine has it: the server's kept list, the shooter's own list (it never
        /// receives its own relay), or a spectator's relay. Null when there is none.</summary>
        internal static List<uint>? TargetsOf(uint arrowId, bool local) {
            float now = Time.time;
            ShotClaim? c;
            bool got = NetworkSceneManager.IsServer || local ? outgoing.TryGet(arrowId, now, out c) : incoming.TryGet(arrowId, now, out c);
            return got && c != null ? c.Targets : null;
        }

        // ShootArguments.Serialize is the first and only field of the shot message (Shooter.SyncShoot), and strength is its
        // last field, so the claim goes right after the game's own fields. Writer writes the whole block or nothing; the
        // reader checks Finished before reading any bit (ShotClaimWire.TryRead).
        [HarmonyPatch(typeof(ShootArguments), nameof(ShootArguments.Serialize))]
        static class Bits {
            static readonly int T = Timing.Section("patch ShootArguments.Serialize / Claims.Bits");
#if QUEST
            static void Postfix(ref ShootArguments __instance, IPlayer player, Il2CppAlta.Serialization.Stream stream) {
#else
            static void Postfix(ref ShootArguments __instance, IPlayer player, Alta.Serialization.Stream stream) {
#endif
                using var _t = Timing.Begin(T);
                if (stream == null) return;
                try {
                    uint id = __instance.target;
                    // Skyfall (admin skill): the release as it went on the wire, before the claim is written or read; its own try, so a
                    // throw here never skips any shot's claim
                    try { Skyfall.RecordPose(id, Skyfall.PoseOf(in __instance)); }
                    catch (Exception e) { if (!warnedPose) { warnedPose = true; MelonLogger.Warning($"[Archery] Skyfall: recording a release pose failed (said once): {e.Message}"); } }
                    float now = Time.time;
                    if (stream.IsWriting) Write(id, stream, now);
                    else if (stream.IsReading) Read(id, player, stream, now);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Claim: failed: {e}"); }
            }
        }

        static void Live(string use) {
            if (loggedLive) return;
            loggedLive = true;
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Claim: shot-message patch is live (first use: {use})");
        }

        // Either side. Never removes the claim: the client may serialise twice (to the server, then to players) and the
        // server's relay is serialised once for all recipients; every pass must write the same bits.
        static readonly int T_Write = Timing.Section("Claims.Write (shot claim)");
#if QUEST
        static void Write(uint id, Il2CppAlta.Serialization.Stream stream, float now) {
#else
        static void Write(uint id, Alta.Serialization.Stream stream, float now) {
#endif
            using var _t = Timing.Begin(T_Write, id);
            Live("writing");
            if (!outgoing.TryGet(id, now, out ShotClaim? claim) || claim == null) return;
            // Skyfall (admin skill): a holder's candidate becomes a request from the wire pose, in place, or the claim goes out as built
            try { Skyfall.Finalise(id, claim); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: arrow {id}'s request could not be finalised; the claim goes out as built: {e.Message}"); }
            ShotClaimWire.Write(new StreamBits(stream), claim);
        }

        // Either side: the server reads the shooter's claim, clients read the server's relay.
        static readonly int T_Read = Timing.Section("Claims.Read (shot claim)");
#if QUEST
        static void Read(uint id, IPlayer? player, Il2CppAlta.Serialization.Stream stream, float now) {
#else
        static void Read(uint id, IPlayer? player, Alta.Serialization.Stream stream, float now) {
#endif
            using var _t = Timing.Begin(T_Read, id);
            Live("reading");
            bool server = NetworkSceneManager.IsServer;
            string who = server ? player?.UserInfo?.Username ?? "?" : "server";
            if (server) {
                // If Shoot is then skipped (arrow entity missing), the relay still runs: it must not carry an older claim.
                outgoing.Remove(id);
                if (senders.Count > 256) senders.Clear();
                senders[id] = who;
            }
            var bits = new StreamBits(stream);
            WireRead result = ShotClaimWire.TryRead(bits, out ShotClaim? claim);
            if (result == WireRead.Ok && claim != null) {
                incoming.Put(id, claim, now);
                int left = bits.BitsLeft;
                if (left >= 8)
                    MelonLogger.Warning($"[Archery] Claim: {left} unread bits after the claim on arrow {id}; the layouts disagree");
                return;
            }
            incoming.Remove(id);
            if (result != WireRead.Absent) MelonLogger.Warning($"[Archery] Claim: arrow {id} from '{who}' carries a claim this version does not understand ({result}); treated as absent");
        }
    }
}
