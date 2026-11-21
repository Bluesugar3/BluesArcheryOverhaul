using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Impact;
using Alta.Networking.Scripts.Player;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Skyfall, the server's storm (Task 9; the kill rule Task 12c's). Everything a landing decides happens here, on the server only:
    // when each landing is due (one queue of every storm's landings, the World-Nail queued first so a full queue drops rain, never the
    // finale), the ground under it (Skyfall.GroundAt: loaded ground only, else the landing is skipped), and in a normal storm the kill
    // of everything inside its kill zone (the owner: "all attacks are ment to kill everything it hits"; the hemisphere of BlastRadius,
    // or NailDomeRadius for the World-Nail, on the ground): each creature by one batched lethal Explosive hit per tick (no force, no
    // experience), each player by a lethal hit through the game's own death, whatever the server's PvP setting; never the caster,
    // never a player inside the caster's safe circle, no player when the caster is unknown. Festival care and cripple (dormant since
    // Task 12b) are Task 9's. The rules are SkyfallStrike's (SkyfallRules.cs, tested). It plays no sound and no effect (the headless
    // server), and it never touches the held real arrow (Skyfall.Held destroys it).
    internal static class SkyfallServer {
        sealed class Running {
            public SkyStorm S = null!; public string Caster = ""; public PlayerController? Wielder;
            public int Total, Left, Dealt, Skipped, Dropped, Struck, CreaturesKilled, Killed, Spared, Healed, Fed;
        }
        struct Due { public Running R; public int Index; }
        struct Blast { public float Cripple; public bool Kill; public Vector3 Point; public Running R; }   // Kill: one lethal hit; else festival's cripple

        static readonly BounceQueue<Due> landings = new BounceQueue<Due>();
        static readonly List<Due> due = new List<Due>();
        static Collider[] buf = new Collider[2048];
        static bool overlapWarned, queueWarned, landWarned, playerWarned;
        const int ColliderCap = 8192;

        // reused per landing (bodies, and beside each the creature's health or the player) and per tick (the batch, players acted on)
        static readonly List<SkyBody> bodies = new List<SkyBody>();
        static readonly List<HealthObject?> healths = new List<HealthObject?>();
        static readonly List<Player?> players = new List<Player?>();
        static readonly Dictionary<HealthObject, int> creatureAt = new Dictionary<HealthObject, int>();   // a creature -> its index in bodies
        static bool creatureWarned;
        static readonly List<SkyEffect> effects = new List<SkyEffect>();
        static readonly Dictionary<HealthObject, Blast> batch = new Dictionary<HealthObject, Blast>();
        static readonly HashSet<Player> actedOn = new HashSet<Player>();

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- the start (Skyfall.Begin, server, Skyfall and Festival only)

        static readonly int TStart = Timing.Section("SkyfallServer.Start");
        /// <summary>Server: queues the storm's landings, the World-Nail first and then the rain in plan order (SkyfallStrike.QueueOrder),
        /// each due at StartTime + its LandAt. A full queue drops what does not fit (one warning a session).</summary>
        internal static void Start(SkyStorm s) {
            using var _t = Timing.Begin(TStart, s != null ? s.ArrowId : 0u);
            if (!NetworkSceneManager.IsServer || s == null || s.Impacts.Length == 0) return;   // a fizzle's arrow is Skyfall.Held's
            Player? caster = Online(s.CasterAccount);
            var r = new Running {
                S = s,
                Caster = caster != null ? Builds.NameOf(caster) : GrantName(s.CasterAccount) ?? s.CasterAccount.ToString(CultureInfo.InvariantCulture),
                Wielder = caster != null && caster.PlayerController != null ? caster.PlayerController : null,
                Total = s.Impacts.Length,
            };
            foreach (int k in SkyfallStrike.QueueOrder(s.Impacts)) {
                if (landings.Add(new Due { R = r, Index = k }, s.StartTime + s.Impacts[k].LandAt)) r.Left++;
                else r.Dropped++;
            }
            if (r.Dropped > 0 && !queueWarned) {
                queueWarned = true;
                MelonLogger.Warning($"[Archery] Skyfall: {BounceQueue<Due>.Cap} landings are waiting; arrow {s.ArrowId}'s storm lost {r.Dropped} rain landing(s) (said once a session)");
            }
            if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: storm of arrow {s.ArrowId} by '{r.Caster}' started on the server{(s.Festival ? " (festival)" : "")}: {r.Left} landing(s) over {SkyfallPattern.EndsAt(Skyfall.Values):0.0} s round ({s.CX:0.0}, {s.CZ:0.0})"));
            if (r.Left == 0) Ended(r);   // nothing fitted: its one line now, as no landing will end it
        }

        // The online player of this account. A loop of our own, not Player.SafeGetPlayer: that reads every player's UserInfo.Identifier
        // and throws while one has none yet (as SkyfallLink.OnlinePlayer).
        static Player? Online(int account) {
            if (account == 0) return null;
            foreach (IPlayer ip in Player.AllPlayers) {
                try { if (ip is Player p && p != null && p.UserInfo != null && p.UserInfo.Identifier == account) return p; }
                catch { }   // a player still joining: not this one
            }
            return null;
        }

        static string? GrantName(int account) {
            foreach (KeyValuePair<int, string> g in Skyfall.Grants.All) if (g.Key == account) return g.Value;
            return null;
        }

        // ---------------------------------------------------------------- every frame: the landings due

        /// <summary>Main.OnUpdate, before the Quickdraw-ring return: every landing due now (each on its own, so one throw never loses the
        /// rest), then one hit per creature, then each landing counted off its storm, whatever happened above. One count test while
        /// none waits (clients never queue).</summary>
        internal static void Tick() {
            if (landings.Count == 0) return;
            float now = Time.time;
            due.Clear();
            if (landings.TakeDue(now, due) == 0) return;
            try {
                SkyfallValues v = Skyfall.Values;
                actedOn.Clear();
                foreach (Due d in due) {
                    try { Landing(d, v); }
                    catch (Exception e) {
                        if (!landWarned) { landWarned = true; MelonLogger.Warning($"[Archery] Skyfall: a landing failed (said once): {e.Message}"); }
                    }
                }
                try { Deal(); } finally { batch.Clear(); }
            }
            finally {
                foreach (Due d in due) {
                    d.R.Left--;
                    if (d.R.Left == 0) Ended(d.R);
                }
                due.Clear();
                actedOn.Clear();
            }
        }

        static readonly int TLanding = Timing.Section("hit SkyfallServer.Impact (one landing)");
        static void Landing(Due d, SkyfallValues v) {
            using var _t = Timing.Begin(TLanding, d.R.S.ArrowId);
            Running r = d.R;
            SkyStorm s = r.S;
            SkyImpact i = s.Impacts[d.Index];
            if (!Skyfall.GroundAt(i.X, i.Z, s.CY + 0.5, s.Release.Y - Skyfall.GroundBelowMetres, out float gy)) {
                r.Skipped++;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {s.ArrowId} landing {i.Index} skipped: no loaded ground under it");
                return;
            }
            r.Dealt++;
            var point = new Vector3((float)i.X, gy, (float)i.Z);
            bodies.Clear(); healths.Clear(); players.Clear(); creatureAt.Clear();

            // creatures: their point nearest the landing (the sphere holds the whole kill zone, SkyfallStrike.Reach). Final review I-1:
            // the PositionHandler point is the HealthObject's origin for most creatures, so a big creature whose near side is in the
            // zone lived; each of its body colliders the overlap found offers its closest point (SkyfallCaught.Nearer), never a shell
            // ('Block Players', 'Target Collector'), and a collider Collider.ClosestPoint does not take (a non-convex mesh) its bounds'.
            // Each collider is read on its own: one that throws is left out (said once), never the landing.
            HVec ground = Seek.H(point);
            int n = Targets.Overlap(point, (float)SkyfallStrike.Reach(i.Nail, v), ref buf, ColliderCap, ref overlapWarned, "Skyfall");
            for (int k = 0; k < n; k++) {
                Collider c = buf[k];
                if (c == null) continue;
                try {
                    ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
                    HealthObject? h = det != null ? det.HealthTarget : null;
                    if (h == null || !Targets.IsCreature(h)) continue;
                    if (!creatureAt.TryGetValue(h, out int bi)) {
                        bi = bodies.Count;
                        bodies.Add(new SkyBody { At = Seek.H(Origin(h, point)), Player = false, Alive = h.Health > 0f });
                        healths.Add(h); players.Add(null);
                        creatureAt[h] = bi;
                    }
                    if (Seek.ShellLayer(c.gameObject.layer)) continue;
                    Vector3 near = ClosestTakes(c) ? c.ClosestPoint(point) : c.bounds.ClosestPoint(point);
                    SkyBody b = bodies[bi];
                    b.At = SkyfallCaught.Nearer(ground, b.At, Seek.H(near));
                    bodies[bi] = b;
                }
                catch (Exception e) {
                    if (!creatureWarned) { creatureWarned = true; MelonLogger.Warning($"[Archery] Skyfall: a creature's collider '{(c != null ? c.name : "?")}' could not be read at a landing; left out (said once): {e.Message}"); }
                }
            }

            // players: their heads (as Ricochet's candidates; SkyfallStrike tests their feet, and skips the caster and the safe circle)
            foreach (IPlayer ip in Player.AllPlayers) {
                if (!(ip is Player p) || p == null) continue;
                try {
                    PlayerController pc = p.PlayerController;
                    if (pc == null || !(pc.Health is PlayerHealth ph) || ph == null) continue;
                    Vector3? head = Targets.HeadOf(p);
                    if (!head.HasValue) continue;
                    var body = new SkyBody { At = Seek.H(head.Value), Player = true, Alive = ph.Health > 0f && pc.IsAlive, Account = p.UserInfo?.Identifier ?? 0 };
                    bodies.Add(body); healths.Add(null); players.Add(p);
                }
                catch (Exception e) {   // e.g. a player still joining (no account yet): left out, never struck, never cared for
                    if (!playerWarned) { playerWarned = true; MelonLogger.Warning($"[Archery] Skyfall: '{Builds.NameOf(p)}' could not be read at a landing; left out (said once): {e.Message}"); }
                }
            }

            SkyfallStrike.Decide(i, gy, s.Festival, s.CasterAccount, s.Release, bodies, v, effects);
            int creatures = 0, kills = 0, cared = 0;
            foreach (SkyEffect e in effects) {
                if (!bodies[e.Body].Player) {
                    HealthObject? h = healths[e.Body];
                    if (h == null) continue;
                    if (batch.TryGetValue(h, out Blast b)) { b.Cripple += e.Cripple; b.Kill |= e.Kill; batch[h] = b; }
                    else batch[h] = new Blast { Cripple = e.Cripple, Kill = e.Kill, Point = point, R = r };
                    creatures++;
                    continue;
                }
                Player? p = players[e.Body];
                if (p == null || !actedOn.Add(p)) continue;   // once per player per tick
                if (e.Kill) { if (Kill(p, r, i, point)) kills++; }
                else if (e.Care) { Care(p, r); cared++; }
            }
            if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: arrow {s.ArrowId} landing {i.Index} ({(i.Nail ? "the World-Nail" : "wave " + (i.Wave + 1))}) at {point}, kill radius {SkyfallStrike.KillRadius(i.Nail, v):0.#} m: {creatures} creature(s) in it (hit at the end of this tick), {kills} player(s) killed{(s.Festival ? $", {cared} cared for" : "")}"));
        }

        // Collider.ClosestPoint takes only a box, sphere, capsule or convex mesh; any other (a non-convex mesh, a CharacterController)
        // is measured by its bounds, which errs toward the kill by at most the bounds' corners
        static bool ClosestTakes(Collider c) =>
            c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex);

        // the creature's PositionHandler point (a ColliderPositionHelper's closest point, else its origin); its transform if that throws
        static Vector3 Origin(HealthObject h, Vector3 point) {
            try { if (h.PositionHandler != null) return h.PositionHandler.GetPosition(point); }
            catch { }
            return h.transform.position;
        }

        static readonly int TDeal = Timing.Section("hit SkyfallServer.Deal (one hit per creature)");
        // One plain Explosive ImpactHit per creature (the Holy blast's recipe), the caster's controller as the wielder, no force, no
        // experience. Normal mode: a creature any landing of this tick caught takes SkyfallStrike.Lethal(its maximum health), no
        // cripple: the player kill's rule. Festival (dormant): the summed cripple of every landing that reached it, no damage.
        static void Deal() {
            using var _t = Timing.Begin(TDeal);
            foreach (KeyValuePair<HealthObject, Blast> kv in batch) {
                HealthObject h = kv.Key;
                Blast b = kv.Value;
                if (h == null || !(h.Health > 0f) || (!b.Kill && !(b.Cripple > 0f))) continue;
                string name = h.name;
                float before = h.Health;
                float damage = b.Kill ? SkyfallStrike.Lethal(h.MaxHealth) : 0f, cripple = b.Kill ? 0f : b.Cripple;
                try {
                    var hit = new ImpactHit(damage, cripple, 1f, 1f, null, ImpactType.Explosive, b.Point, Vector3.zero, Vector3.zero, null, null,
                                            b.R.Wielder != null ? b.R.Wielder : null);
                    Hits.Dealing = true;
                    using (HitScope.As(DealtBy.Skyfall, b.R.S.ArrowId)) h.DetectImpact(hit); // balance telemetry
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: a hit on '{name}' failed: {e.Message}"); continue; }
                finally { Hits.Dealing = false; }
                b.R.Struck++;
                float after = h != null ? h.Health : 0f;
                if (b.Kill && !(after > 0f)) b.R.CreaturesKilled++;
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: arrow {b.R.S.ArrowId} '{name}' {(b.Kill ? (after > 0f ? "struck to kill but not killed" : "killed") : "crippled")}: damage {damage:0.#}, cripple {cripple:0.#}; health {before:0.#} -> {after:0.#}"));
            }
        }

        // ---------------------------------------------------------------- a player in a kill zone, festival care, the storm's line

        static readonly int TKill = Timing.Section("hit SkyfallServer.Kill (a player in a kill zone)");
        // A plain ImpactHit of Lethal(maxHealth) health damage, no cripple, no wielder (so no PvP rule: the game's PvP gate is only for
        // ImpactHitWithTool), Explosive, through the player's DetectImpact (the Holy recipe): the game's own Downed death follows. Spawn
        // and cave invincibility, the immortals switch, IsIgnoringDamage, locked controls, DamageX or PlayerDamageTakenMultiplier at 0, a
        // 0 Explosive multiplier and damage protection all still stop it, by design: the line then says "but was not killed". True only
        // when the player was killed.
        static bool Kill(Player p, Running r, SkyImpact i, Vector3 point) {
            using var _t = Timing.Begin(TKill, r.S.ArrowId);
            PlayerController pc = p.PlayerController;
            if (pc == null || !(pc.Health is PlayerHealth ph) || ph == null) return false;
            if (r.Wielder != null && pc == r.Wielder) {   // the caster's own body, whatever its account reads now (SkyfallStrike skips it by account)
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: '{Builds.NameOf(p)}' is the caster of arrow {r.S.ArrowId}'s storm: never killed");
                return false;
            }
            float before = ph.Health;
            try {
                var hit = new ImpactHit(SkyfallStrike.Lethal(ph.MaxHealth), 0f, 1f, 1f, null, ImpactType.Explosive, point, Vector3.zero, Vector3.zero, null, null, null);
                Hits.Dealing = true;
                using (HitScope.As(DealtBy.Skyfall, r.S.ArrowId)) ph.DetectImpact(hit); // balance telemetry
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: the kill on '{Builds.NameOf(p)}' failed: {e.Message}"); }   // the game's death path can throw on the headless server
            finally { Hits.Dealing = false; }
            float after = ph != null ? ph.Health : 0f;
            bool killed = !(after > 0f);
            if (killed) r.Killed++; else r.Spared++;
            if (!Logs.Summary) return killed;
            string where = i.Nail ? "the World-Nail" : $"a wave {i.Wave + 1} arrow";
            Vector3? head = Targets.HeadOf(p);
            string off = head.HasValue ? Inv($", {Vector2.Distance(new Vector2(head.Value.x, head.Value.z), new Vector2(point.x, point.z)):0.0} m from it") : "";
            MelonLogger.Msg(killed
                ? Inv($"[Archery] Skyfall: '{Builds.NameOf(p)}' was in the kill zone of {where} of '{r.Caster}''s storm (arrow {r.S.ArrowId}{off}) and was killed (health {before:0.#} -> {after:0.#})")
                : Inv($"[Archery] Skyfall: '{Builds.NameOf(p)}' was in the kill zone of {where} of '{r.Caster}''s storm (arrow {r.S.ArrowId}{off}) but was not killed: health {before:0.#} -> {after:0.#} (spawn invincibility, the immortals switch, locked controls or the server's damage settings)"));
            return killed;
        }

        static readonly int TCare = Timing.Section("SkyfallServer.Care (festival heal and feed)");
        static void Care(Player p, Running r) {
            using var _t = Timing.Begin(TCare, r.S.ArrowId);
            try {
                if (PlayerCare.Heal(p)) r.Healed++;
                if (PlayerCare.Feed(p)) r.Fed++;
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: festival care for '{Builds.NameOf(p)}' failed: {e.Message}"); }
        }

        static void Ended(Running r) {
            if (Logs.Summary) MelonLogger.Msg(r.S.Festival
                ? $"[Archery] Skyfall: storm of arrow {r.S.ArrowId} by '{r.Caster}' ended (festival): {r.Dealt} of {r.Total} landing(s) dealt, {r.Skipped} over unloaded or missing ground, {r.Dropped} dropped (queue full); {r.Struck} creature(s) crippled; {r.Healed} healed, {r.Fed} fed"
                : $"[Archery] Skyfall: storm of arrow {r.S.ArrowId} by '{r.Caster}' ended: {r.Dealt} of {r.Total} landing(s) dealt, {r.Skipped} over unloaded or missing ground, {r.Dropped} dropped (queue full); {r.CreaturesKilled} creature(s) killed, {r.Struck - r.CreaturesKilled} struck but not killed; {r.Killed} player(s) killed, {r.Spared} in a kill zone but not killed");
        }

        /// <summary>Server quit: every waiting landing, the batch and the storms (each Running lives only in the queue) are dropped. It
        /// destroys nothing: the held real arrow is Skyfall.Held's.</summary>
        internal static void Reset() {
            landings.Clear();
            due.Clear();
            batch.Clear();
            actedOn.Clear();
            bodies.Clear(); healths.Clear(); players.Clear(); creatureAt.Clear(); effects.Clear();
        }
    }
}
