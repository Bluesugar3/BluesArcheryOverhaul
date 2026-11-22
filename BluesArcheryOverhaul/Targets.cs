using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Impact;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Batch 3 (spec 2026-11-16): the shot claim's target list, and what each machine remembers about one release for the Multi
    // side orbs (Ricochet's jump candidates, the creatures the release struck, the looks' counters). The list is for Ricochet's
    // creature jump now and for the later Shatter and Seeking Volley batches.
    //   - The shooter lists creatures (ForShot): inside [Targets] ConeDegrees round the aim at release, within Range, nearest first, at most the wire's six, named by HealthObject.Entity.PrefabRoot.Identifier
    //     (Targets.IdOf). Only a build that uses the list writes one (TargetRule.Wanted): anyone else's claim is as before.
    //   - The server keeps a target only when it resolves (NetworkSceneManager.Current.GetEntity), is a creature (never a
    //     player), is alive and within Range of the shooter (TargetRule, Rules.cs, tested), and relays what it kept.
    //   - The shooter never receives its own relay: it uses its own list, filtered by the same rule with its own view.
    //   - Every machine opens a Release at Volley.Launch with the list it has (Claims.TargetsOf) and adds the creatures the
    //     release strikes as it sees them.
    internal static class Targets {
        const int Cap = 64, ColliderCap = 8192;   // b3-review I-4: a 60 m sphere in forest or town holds far more than 1024
        static bool fullWarned, costLogged;
        const float ForgetSeconds = 15f;
        static MelonPreferences_Entry<float> range = null!, cone = null!;
        static Collider[] overlap = new Collider[2048]; // G2: was 256; play-test 8's first search grew it three times (1859 colliders)
        static readonly List<TargetCandidate> candidates = new List<TargetCandidate>();
        static readonly HashSet<uint> seen = new HashSet<uint>();

        internal static void Init() {
            var cat = Cfg.Category("Targets");
            range = Cfg.Shared(cat.CreateEntry("Range", 60f, description: "Multi-arrow main with Ricochet, or Charge Shot main with Shatter: the shot names creatures within this many metres of the archer (the server keeps only those); Ricochet's creature jumps choose among them and the creatures the release struck, Shatter's splinters among them and the creature behind the struck one."));
            cone = Cfg.Shared(cat.CreateEntry("ConeDegrees", 60f, description: "Ricochet or Shatter: the shot names the creatures inside a cone this wide (degrees, its full width) round the aim at release, nearest first, at most six."));
            dummiesCount = Cfg.Shared(cat.CreateEntry("DummiesCount", true, description: "A combat dummy (health, no stagger pool, a name in DummyNames) counts as a creature for the mod's own targeting: Shatter's trigger and blast, Ricochet's jumps, the Quickdraw 4 line passing through, the shot's target list. Never for stagger or interrupt (it has none)."));
            dummyNames = Cfg.Shared(cat.CreateEntry("DummyNames", DummyRule.DefaultNames, description: "Names (part of the prefab's name, any case, ';' between them) of the things DummiesCount treats as creatures."));
            Cfg.Reloaded += () => dummyMemo.Clear();
        }

        internal static float Range => range != null ? Mathf.Max(0f, range.Value) : 60f;
        static float Cone => cone != null ? Mathf.Clamp(cone.Value, 0f, 180f) : 60f;

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>The creature behind a network id on this machine, or null (not loaded here, despawned, or not a thing with health).</summary>
        internal static HealthObject? Resolve(uint id) {
            if (id == 0u) return null;
            try {
                INetworkScene scene = NetworkSceneManager.Current;
                NetworkEntity e = scene != null ? scene.GetEntity(id) : null!;
                NetworkEntity root = e != null ? e.PrefabRoot : null!;
                if (root == null) return null;
                // 0.19.11: a player's own health, never another health object under their body (Seeking Volley locks players)
                PlayerController pc = root.GetComponent<PlayerController>();
                if (pc != null && pc.Health != null) return pc.Health;
                HealthObject h = root.GetComponentInChildren<HealthObject>(true);
                return h != null ? h : null;
            }
            catch { return null; }
        }

        /// <summary>A creature's id on every machine: its entity's prefab root's network identifier (0 when it has none).</summary>
        internal static uint IdOf(HealthObject h) {
            NetworkEntity e = h.Entity;
            NetworkEntity root = e != null ? e.PrefabRoot : null!;
            return root != null ? root.Identifier : 0u;
        }

        /// <summary>A health target's root: its entity's prefab root, else its own transform.</summary>
        internal static Transform RootOf(HealthObject h) {
            NetworkEntity e = h.Entity;
            return e != null && e.PrefabRoot != null ? e.PrefabRoot.transform : h.transform;
        }

        static readonly List<Collider> boundsColliders = new List<Collider>();

        /// <summary>The bounds of a creature's solid colliders under its root (a disabled one reports no size and is skipped);
        /// false when none has a size.</summary>
        internal static bool BoundsOf(Transform root, out Bounds b, bool includeInactive = false) {
            bool any = false;
            b = default;
            root.GetComponentsInChildren(includeInactive, boundsColliders); // G2: one list for every call (same colliders, same order)
            foreach (Collider c in boundsColliders) {
                if (c == null || c.isTrigger) continue;
                Bounds cb = c.bounds;
                if (cb.size.sqrMagnitude < 1e-6f) continue;
                if (!any) { b = cb; any = true; } else b.Encapsulate(cb);
            }
            return any;
        }

        /// <summary>The middle of a creature: its colliders' bounds (its root plus half a metre when none has a size).</summary>
        static readonly int T_Centre = Timing.Section("Targets.Centre (bounds walk)");
        internal static Vector3 Centre(HealthObject h, out Transform root) {
            using var _t = Timing.Begin(T_Centre);
            root = RootOf(h);
            // 0.19.11: a player's chest, from the head every machine knows (the body's colliders ride the avatar's bones, posed
            // differently on each machine; the bounds of everything under a player's root are not their body)
#if QUEST
            if (h.Is(out PlayerHealth ph) && ph.Player != null && ph.Player.Head != null) return ph.Player.Head.position + Vector3.down * PlayerChestBelowHead;
#else
            if (h is PlayerHealth ph && ph.Player != null && ph.Player.Head != null) return ph.Player.Head.position + Vector3.down * PlayerChestBelowHead;
#endif
            return BoundsOf(root, out Bounds b) ? b.center : root.position + Vector3.up * 0.5f;
        }

        internal const float PlayerChestBelowHead = 0.35f;

        /// <summary>The colliders within <paramref name="radius"/> of <paramref name="centre"/> (not layer 2, no triggers), in
        /// <paramref name="buffer"/>, which is doubled while full up to <paramref name="cap"/>; still full at the cap, one warning
        /// per <paramref name="warned"/> flag naming <paramref name="what"/>. Returns how many were found.</summary>
        static readonly int T_Overlap = Timing.Section("scene Targets.Overlap (overlap sphere)");
        internal static int Overlap(Vector3 centre, float radius, ref Collider[] buffer, int cap, ref bool warned, string what) {
            using var _t = Timing.Begin(T_Overlap, what);
            int n;
#if QUEST
            while ((n = Il.OverlapSphereNonAlloc(centre, radius, buffer, ~(1 << 2), QueryTriggerInteraction.Ignore)) == buffer.Length && buffer.Length < cap)
#else
            while ((n = Physics.OverlapSphereNonAlloc(centre, radius, buffer, ~(1 << 2), QueryTriggerInteraction.Ignore)) == buffer.Length && buffer.Length < cap)
#endif
                buffer = new Collider[buffer.Length * 2];
            if (n == buffer.Length && !warned) { warned = true; MelonLogger.Warning(Inv($"[Archery] {what}: more than {n} colliders within {radius:0.#} m; some may be missed (said once)")); }
            return n;
        }

        /// <summary>A creature for the mod's own targeting (target lists, Ricochet's jumps, Shatter's trigger and blast, the Quickdraw 4
        /// line passing through, Seeking Volley's locks): a thing with health and a stagger pool that is not a player; 0.19.11: or one
        /// with a creature's AI (a Spriggull has no stagger pool); G3: or a combat dummy (IsDummy). Stagger and interrupt test the pool
        /// themselves (Weight), so a Spriggull or a dummy is never staggered.</summary>
#if QUEST
        internal static bool IsCreature(HealthObject? h) => h != null && !(h.Is<PlayerHealth>()) && (h.HasCrippleStat || HasAgent(h) || IsDummy(h));
#else
        internal static bool IsCreature(HealthObject? h) => h != null && !(h is PlayerHealth) && (h.HasCrippleStat || HasAgent(h) || IsDummy(h));
#endif

        static readonly Dictionary<int, bool> agentMemo = new Dictionary<int, bool>();

        /// <summary>An Alta.Intelligence.Agent under the health object's prefab root (every creature's AI). Remembered per health
        /// object, at most 1024.</summary>
        static bool HasAgent(HealthObject h) {
            int key = h.GetInstanceID();
            if (agentMemo.TryGetValue(key, out bool known)) return known;
            NetworkEntity e = h.Entity;
            NetworkEntity root = e != null ? e.PrefabRoot : null!;
#if QUEST
            bool yes = root != null && root.GetComponent<PlayerController>() == null && root.GetComponentInChildren<Il2CppAlta.Intelligence.Agent>(true) != null;
#else
            bool yes = root != null && root.GetComponent<PlayerController>() == null && root.GetComponentInChildren<Alta.Intelligence.Agent>(true) != null;
#endif
            if (agentMemo.Count >= DummyMemoCap) agentMemo.Clear();
            agentMemo[key] = yes;
            return yes;
        }

        // G3 (play-test 8, diagnosis 5): [Targets] DummiesCount and DummyNames (shared: the server's targets depend on them)
        static MelonPreferences_Entry<bool>? dummiesCount;
        static MelonPreferences_Entry<string>? dummyNames;
        static readonly Dictionary<int, bool> dummyMemo = new Dictionary<int, bool>();
        const int DummyMemoCap = 1024;

        /// <summary>[Targets] DummyNames as set (a leaf: reads its own entry only).</summary>
        internal static string DummyNames => dummyNames != null ? dummyNames.Value ?? "" : DummyRule.DefaultNames;

        /// <summary>A combat dummy (DummyRule): [Targets] DummiesCount on, health but no stagger pool, not a player (no PlayerController on
        /// its root), and its prefab root's name matches [Targets] DummyNames. Remembered per health object (names never change; the
        /// memo is dropped on a settings reload).</summary>
        internal static bool IsDummy(HealthObject? h) {
#if QUEST
            if (h == null || h.Is<PlayerHealth>() || h.HasCrippleStat || dummiesCount == null || !dummiesCount.Value) return false;
#else
            if (h == null || h is PlayerHealth || h.HasCrippleStat || dummiesCount == null || !dummiesCount.Value) return false;
#endif
            int key = h.GetInstanceID();
            if (dummyMemo.TryGetValue(key, out bool known)) return known;
            NetworkEntity e = h.Entity;
            NetworkEntity root = e != null ? e.PrefabRoot : null!;
            bool player = root != null && root.GetComponent<PlayerController>() != null;
            string name = root != null ? root.name : h.name;
            bool yes = DummyRule.Counts(true, player, false, DummyRule.Matches(name, DummyNames));
            if (dummyMemo.Count >= DummyMemoCap) dummyMemo.Clear();
            dummyMemo[key] = yes;
            if (yes && Logs.Debug) MelonLogger.Msg($"[Archery] Targets: '{name}' counts as a creature (a combat dummy: [Targets] DummiesCount)");
            return yes;
        }
#if QUEST
        internal static bool IsPlayer(HealthObject? h) => h.Is<PlayerHealth>() || (h != null && h.Entity != null && h.Entity.PrefabRoot != null && h.Entity.PrefabRoot.GetComponent<PlayerController>() != null);
#else
        internal static bool IsPlayer(HealthObject? h) => h is PlayerHealth || (h != null && h.Entity != null && h.Entity.PrefabRoot != null && h.Entity.PrefabRoot.GetComponent<PlayerController>() != null);
#endif

        /// <summary>A player's position: their head (a Player entity's own transform never moves).</summary>
        internal static Vector3? HeadOf(Player? p) {
            PlayerController pc = p != null ? p.PlayerController : null!;
            Transform head = pc != null ? pc.Head : null!;
            return head != null ? head.position : pc != null ? pc.transform.position : (Vector3?)null;
        }

        static TargetFacts FactsOf(HealthObject? h, Vector3 from) => h == null ? default : FactsAt(h, Targets.Centre(h, out _), from);

        static TargetFacts FactsAt(HealthObject h, Vector3 centre, Vector3 from) =>
            new TargetFacts(true, IsCreature(h), IsPlayer(h), h.Health > 0f, (centre - from).magnitude);

        /// <summary>The creature a claimed id names, only when the id is that creature's own (its prefab root's) id. A child entity's
        /// id would resolve to the same creature under another id, so it could be its own jump target (b3-review M-4).</summary>
        internal static HealthObject? ResolveOwn(uint id) {
            HealthObject? h = Resolve(id);
            return h != null && Targets.IdOf(h) == id ? h : null;
        }

        /// <summary>Server: the facts of one claimed id as this server sees it, measured from the shooter's head.</summary>
        internal static TargetFacts Facts(uint id, Vector3 shooter) => FactsOf(ResolveOwn(id), shooter);

        /// <summary>Shooter's client, in the claim (Claims.Shot): the creatures in the cone round the aim at release, nearest first.</summary>
        static readonly int T_ForShot = Timing.Section("release targets Targets.ForShot (shooter's overlap)");
        internal static List<uint> ForShot(Vector3 aimFrom, Vector3 aim, Vector3 shooter) {
            using var _t = Timing.Begin(T_ForShot);
            candidates.Clear(); seen.Clear();
            float reach = Range;
            if (reach <= 0f || aim.sqrMagnitude < 1e-8f) return new List<uint>();
            Vector3 dir = aim.normalized;
            var watch = costLogged || !Logs.Summary ? null : System.Diagnostics.Stopwatch.StartNew();
            float half = Cone * 0.5f;
            // G2: only the sphere that encloses the cone the loop below keeps (half + 15 degrees round the aim from aimFrom, out to the
            // range measured from the shooter, so to range + |shooter - aimFrom| from aimFrom); TargetSearch, tested. Every collider
            // that reaches into that cone within that reach is still found; the ones left out could never pass the cone test or
            // TargetRule.Pick's range, and Pick sorts by distance then id, so the list does not depend on the overlap's order.
            float apexReach = reach + (shooter - aimFrom).magnitude;
            int n = TargetSearch.Enclose(apexReach, half + 15f, TargetSearch.MarginMetres, out float ahead, out float radius)
                ? Overlap(aimFrom + dir * ahead, radius, ref overlap, ColliderCap, ref fullWarned, "Targets: the shot's list")
                : Overlap(shooter, reach, ref overlap, ColliderCap, ref fullWarned, "Targets: the shot's list");
            for (int i = 0; i < n; i++) {
                Collider c = overlap[i];
                if (c == null) continue;
                // cheap first: a collider well outside the cone is skipped before any component is looked for
                Vector3 to = c.bounds.center - aimFrom;
                if (to.sqrMagnitude > 1e-6f && Vector3.Angle(dir, to) > half + 15f) continue;
                ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
                HealthObject h = det != null ? det.HealthTarget : null!;
                if (h == null || !IsCreature(h)) continue;
                uint id = Targets.IdOf(h);
                if (id == 0u || !seen.Add(id)) continue;
                Vector3 centre = Targets.Centre(h, out _);
                Vector3 toCentre = centre - aimFrom;
                float angle = toCentre.sqrMagnitude > 1e-6f ? Vector3.Angle(dir, toCentre) : 0f;
                candidates.Add(new TargetCandidate(id, angle, FactsAt(h, centre, shooter))); // G2: the centre just measured, not a second walk
            }
            List<uint> picked = TargetRule.Pick(candidates, Cone, reach, ShotClaimWire.MaxTargets);
            if (watch != null) {
                costLogged = true;
                MelonLogger.Msg(Inv($"[Archery] Targets: the shooter's search found {n} colliders for a {reach:0} m range, {candidates.Count} creature(s), in {watch.Elapsed.TotalMilliseconds:0.00} ms (logged once a session)"));
            }
            return picked;
        }

        // ---------------------------------------------------------------- one release, as this machine knows it

        internal sealed class Release {
            public uint Id;
            public float At;
            public Player? Shooter;
            public bool Local, Gold, Ricochet;
            public readonly List<uint> Ids = new List<uint>();             // the relayed (server: kept; shooter: its own) target list
            public uint RealHit;                                            // the creature the real arrow struck (0 = none yet)
            public readonly HashSet<uint> Struck = new HashSet<uint>();     // creatures this release struck (spectral, real or a jump)
            public readonly JumpLedger Jumps = new JumpLedger();            // every machine: the jumps each struck creature has passed on (play-test 7: every landed arrow jumps)
            public int Clicks, SkipLogs, JumpSounds, JumpLogs, Arcs;
            public BowString? Bow;                                          // clients, looks only: the bow it left (the shooter's bow-hand thump at a shatter)
            // Shatter (batch 4, a Charge side orb since 4b; a blast since play-test 7): whether this release can shatter (None:
            // nothing), whether its real arrow shatters (a charged arrow), the pellets its look draws, the multi-arrow orbs that
            // raise its share, and whether its one shatter has happened on this machine.
            public ShatterHalf Shatter;
            public bool ShatterReal, Shattered;
            public int Splinters, MultiOrbs;
            // Fix batch D: each orb's share of its damage for this owner (1 on its own main path, [Complements] SideOrbOffPathShare
            // off it; 0 without it), the creatures this release already damaged (an off-path blast skips them), whether the real
            // arrow (or the line) has jumped (once per release, fixA-review M-4), and the server's creatures round each struck one.
            public float RicochetShare, ShatterShare;
            public bool RicochetOnPath, RealJumped;
            public readonly HashSet<uint> Damaged = new HashSet<uint>();
            public readonly Dictionary<uint, List<uint>> Near = new Dictionary<uint, List<uint>>();
        }

        /// <summary>The shooter writes a target list (and the server keeps one) only for a build that uses it (TargetRule.Wanted,
        /// with each orb's [Tree] switch): Ricochet's jump, Shatter's splinters.</summary>
        internal static bool Wanted(Build b) => BluesArcheryOverhaul.Ricochet.Applies(b) || BluesArcheryOverhaul.Shatter.Applies(b);

        static readonly Dictionary<uint, Release> releases = new Dictionary<uint, Release>();
        static readonly List<uint> order = new List<uint>();

        /// <summary>Every machine, at Volley.Launch: this release's record (replacing any older one with the same id).</summary>
        /// <param name="chargedReal">The release's real arrow is a charged shot that is not a Holy Arrow beam (Shatter's Charge half).</param>
        /// <param name="beam">The release is a Holy Arrow beam (never shatters, off the main path either).</param>
        static readonly int T_Open = Timing.Section("release targets Targets.Open");
        internal static Release Open(uint id, Build build, Player? shooter, bool local, bool gold, List<uint>? ids, bool chargedReal = false, bool beam = false) {
            using var _t = Timing.Begin(T_Open, id);
            if (releases.ContainsKey(id)) order.Remove(id);
            float now = Time.time;
            while (order.Count > 0 && (releases.Count >= Cap || (releases.TryGetValue(order[0], out Release old) && now - old.At > ForgetSeconds))) {
                releases.Remove(order[0]); order.RemoveAt(0);
            }
            var r = new Release {
                Id = id, At = now, Shooter = shooter, Local = local, Gold = gold,
                Ricochet = BluesArcheryOverhaul.Ricochet.Applies(build),
                Shatter = BluesArcheryOverhaul.Shatter.HalfFor(build),
            };
            r.ShatterReal = ShatterRule.RealArrowShatters(r.Shatter, chargedReal, beam);
            r.Splinters = r.Shatter != ShatterHalf.None ? BluesArcheryOverhaul.Shatter.PelletsFor(build) : 0;
            r.MultiOrbs = ShatterRule.MultiOrbs(build);   // fix batch D: a Charge main's multi-arrow orbs (unchanged); none off the main path
            float off = Paths.OrbNumbers.SideOrbOffPathShare;
            r.RicochetShare = r.Ricochet ? SideOrbShare.Ricochet(build, off) : 0f;
            r.ShatterShare = r.Shatter != ShatterHalf.None ? SideOrbShare.Shatter(build, off) : 0f;
            r.RicochetOnPath = RicochetRule.OnPath(build);
            if (!(r.RicochetShare > 0f)) r.Ricochet = false;   // an off-path share of 0: the orb does nothing off its main path
            if (!(r.ShatterShare > 0f)) { r.Shatter = ShatterHalf.None; r.ShatterReal = false; r.Splinters = 0; }
            bool listed = r.Ricochet || r.Shatter != ShatterHalf.None;
            if (ids != null && listed) foreach (uint t in ids) if (!r.Ids.Contains(t)) r.Ids.Add(t);
            releases[id] = r;
            order.Add(id);
            if (listed && Logs.Debug) { // G2: the list line is Debug
                string uses = r.Ricochet && r.Shatter != ShatterHalf.None ? "Ricochet's jump and Shatter" : r.Ricochet ? "Ricochet's jump" : "Shatter";
                if (r.Ricochet && !r.RicochetOnPath) uses += Inv($" (Ricochet off the main path, x{r.RicochetShare:0.##})");
                string shatter = r.Shatter == ShatterHalf.None ? "" : r.Shatter == ShatterHalf.OffPath
                    ? Inv($"; Shatter off the main path (x{r.ShatterShare:0.##}), {r.Splinters} pellets{(r.ShatterReal ? ", the real arrow's first creature hit shatters" : ", a beam: no shatter")}")
                    : $"; Shatter's blast, {r.Splinters} pellets{(r.ShatterReal ? ", the charged arrow shatters" : ", not charged: no shatter")}";
                MelonLogger.Msg($"[Archery] Targets: arrow {id} '{Builds.NameOf(shooter)}' {(NetworkSceneManager.IsServer ? "kept" : local ? "own list" : "relayed")} " +
                                $"[{string.Join(",", r.Ids.ConvertAll(t => t.ToString(CultureInfo.InvariantCulture)).ToArray())}] for {uses}{shatter}");
            }
            return r;
        }

        internal static Release? Of(uint id) => releases.TryGetValue(id, out Release r) ? r : null;

        /// <summary>The real arrow of a Ricochet release struck this creature (Weight's real-arrow hook, every machine that gets it):
        /// it joins the bounce candidates. The first creature only.</summary>
        internal static void RealHit(uint release, HealthObject h) {
            Release? r = Of(release);
            if (r == null || !r.Ricochet || r.RealHit != 0u || !IsCreature(h)) return;
            uint id = Targets.IdOf(h);
            if (id == 0u) return;
            r.RealHit = id;
            r.Struck.Add(id);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Targets: arrow {release}'s real arrow struck '{h.name}' ({id})");
        }

        /// <summary>Fix batch D: the release dealt damage to this creature (a spectral hit, the line, a jump). An off-path Shatter
        /// blast skips it (ShatterRule.SkipsDamaged). Creatures only; nothing without a record.</summary>
        internal static void Damaged(uint release, HealthObject? h) {
            Release? r = Of(release);
            if (r == null || !IsCreature(h)) return;
            uint id = Targets.IdOf(h!);
            if (id != 0u && r.Damaged.Count < 512) r.Damaged.Add(id);
        }

        const int NearCap = 4096, NearPerRelease = 64;
        static Collider[] near = new Collider[128];
        static bool nearWarned;

        /// <summary>Server, fix batch D (fixA-review I-6): the living creatures within <paramref name="metres"/> of a struck one, found
        /// by a bounded overlap sphere round it (buffer grown as ForShot's, at most NearCap colliders) and kept per release and
        /// struck creature, so every jump from it reuses one search. Ids as Targets.IdOf.</summary>
        static readonly int T_Nearby = Timing.Section("Targets.Nearby (server overlap)");
        internal static List<uint> Nearby(Release r, uint fromId, Vector3 centre, float metres) {
            using var _t = Timing.Begin(T_Nearby, fromId);
            if (r.Near.TryGetValue(fromId, out List<uint> known)) return known;
            var found = new List<uint>();
            if (r.Near.Count < NearPerRelease) r.Near[fromId] = found;
            if (!(metres > 0f)) return found;
            int n = Overlap(centre, metres, ref near, NearCap, ref nearWarned, "Targets: round a struck creature (jump targets)");
            for (int i = 0; i < n; i++) {
                Collider c = near[i];
                if (c == null) continue;
                ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
                HealthObject h = det != null ? det.HealthTarget : null!;
                if (h == null || !IsCreature(h) || !(h.Health > 0f)) continue;
                uint id = Targets.IdOf(h);
                if (id == 0u || id == fromId || found.Contains(id)) continue;
                found.Add(id);
            }
            return found;
        }

        /// <summary>The candidates a jump chooses among: the list, the real arrow's creature and the creatures this release
        /// struck, each with where this machine sees it; ids that do not resolve here (or are dead) are left out. On the server
        /// (fix batch D, I-6) also the creatures round the struck one, <paramref name="near"/>; clients keep the relayed list.</summary>
        static readonly int T_Candidates = Timing.Section("Targets.Candidates");
        internal static void Candidates(Release r, List<uint> ids, List<HealthObject> healths, List<Vector3> centres, List<uint>? near = null) {
            using var _t = Timing.Begin(T_Candidates);
            ids.Clear(); healths.Clear(); centres.Clear();
            void Add(uint id) {
                if (id == 0u || ids.Contains(id)) return;
                HealthObject? h = ResolveOwn(id);
                if (h == null || !IsCreature(h) || !(h.Health > 0f)) return;
                ids.Add(id); healths.Add(h); centres.Add(Targets.Centre(h, out _));
            }
            foreach (uint id in r.Ids) Add(id);
            Add(r.RealHit);
            foreach (uint id in r.Struck) Add(id);
            if (near != null) foreach (uint id in near) Add(id);
        }

        internal static string NameOf(uint id) {
            HealthObject? h = Resolve(id);
            return h != null ? $"'{h.name}' ({id})" : id.ToString(CultureInfo.InvariantCulture);
        }

        internal static string P(Vector3 v) => Inv($"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})");
    }
}
