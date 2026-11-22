using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Alta.Networking.Scripts.Player;
using Alta.Impact;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Ricochet (Multi side orb, Multi main only, needs Burst since batch 4b (the user's sketch; it needed Three-Shot before);
    // spec 2026-11-16, redefined by the user in batch 3: "combine
    // Ricochet Chain, if it hits the ground have it bounce following its direction, then if it hits a enemy have it bounce to a
    // different creature"). Rules in Spectral.cs (RicochetRule, JumpRule, tested).
    //   A. Ground skip. A ghost or pellet whose cast hits a world surface (RicochetRule.Surface: setting lists BounceLayers,
    //      BounceChannels, BounceStatic) skips once: the incoming direction's
    //      part along the surface, raised SkipDegrees off it, at SpeedKeep of its speed, then on under the normal arc model
    //      (VolleySim re-seeds the shot: no extra cast this tick). Steeper than MaxHitDegrees it ends as before. Nothing is aimed
    //      at, so every machine skips alike from the same hit point, normal and incoming direction. A skipped ghost deals
    //      SkipShare of its share. Looks: a white spark, a white kink, a click (at most ClicksMax per volley).
    //   B. Creature jump (the former Chain; play-test 7, the user: "Anything that lands should ricochet to the next enemy",
    //      "every arrow that hits", "the ricochet range should be larger"). Every arrow of the release that lands on a creature
    //      (each ghost, each pellet, the real arrow) jumps for JumpShare of that arrow's own damage to one of the nearest other
    //      creatures within JumpMetres (15 m); successive jumps from one struck creature go round the nearest JumpSpread (3) in
    //      turn (JumpRule.Targets / Spread, JumpLedger: tested). With the server's PvP on, players (never the shooter) come after
    //      every creature in that order. The ghosts' and pellets' jumps are collected for JumpBatchSeconds (or until the release
    //      closes) and dealt as one hit per target with no cripple (Hits.Apply, under Hits.Dealing, so no jump ever jumps again).
    //      The real arrow's first creature hit jumps at once, as a share of that hit (Hits.Copy), within RealArrowSeconds.
    //      Candidates: the release's target list (Targets) and the creatures it struck. Only the server deals; every client draws
    //      one arc per landed arrow it sees with its own view (Arcs, fix batch C: a thin pooled arc of its own curve from where
    //      the arrow hit, in the ghosts' colour, drawn then faded, a spark where it lands; the real arrow's brighter and wider;
    //      at most ArcsMax arcs and JumpSoundsMax sounds per release).
    //   - Practice: releasing a full volley (fifteen ghosts) while the orb is held for try-out (Volley.Launch, server).
    //   Fix batch D (the user: "make ... ricoshy work even though its not your main path", "Half strength"):
    //   - Any owner. Off the Multi main every jump deals [Complements] SideOrbOffPathShare (0.5) of what it would (Targets.Release
    //     .RicochetShare, SideOrbShare in Spectral.cs): every arrow of the release that hits a creature jumps (the real arrow, a
    //     Quick main's needles, a Charge main's heavy arrows, a Quickdraw 4 line's first creature, ServerLineHit), and the ground
    //     skip is for those spectral arrows (RicochetRule.CanBounce with onPath false). Practice off the Multi main: the main
    //     path's own shot while the orb is held (SideOrbPractice).
    //   - Jumps pick players only with [PvP] JumpsHitPlayers (server, default off; fixA-review I-5).
    //   - The server's candidates also include the living creatures within JumpMetres of the struck one (Targets.Nearby, one
    //     bounded overlap per struck creature per release; I-6). Clients keep the relayed list and what they saw struck, so a
    //     client's arc can point at another creature than the server's jump when the target was outside the aim cone.
    //   - The real arrow (or the line) jumps once per release (M-4); a client checks its arc cap before searching (M-5).
    //   H5b (the owner, 2026-11-18: "Have Ricochet be able to bounce to 2 enemies max, not just 1 more"): [Ricochet] Bounces
    //   (shared, 2; 1 = before H5). A jump that lands on creature B jumps once more from B, for JumpShare of what it dealt B, to
    //   one of the nearest other creatures within JumpMetres that is not already in its chain (never back to A, never B itself),
    //   by the same rules (creatures first, players as before, JumpSpread, the ledger); a jump that lands on a player stops there.
    //   Every jump source bounces alike (a ghost's or pellet's batch, a needle, the real arrow's copy, the Quickdraw 4 line, a
    //   homing core): all of them walk JumpWalk (Spectral.cs, tested) through one world (ServerWorld). Every jump is dealt under
    //   Hits.Dealing (so no jump feeds Shatter, Weight or a fresh jump); a client draws each further arc from where the arc before
    //   it landed, within ArcsMax.
    //   H5c (the owner, after play-testing 0.17.12: "Have the bounce a bit after from the one that it hit, so it looks like it went
    //   from one to another"; until then the server dealt the first jump and its bounce 1-2 ms apart): [Ricochet]
    //   BounceDelaySeconds (shared, 0.25) is the pause a bounce makes on the creature it leaves. A client starts a bounce's arc that
    //   long after the arc before it landed (JumpRule.BounceAfter: (depth - 1) x (pause + JumpDrawSeconds) after the first arc
    //   started), and the creature it leaves sparks again as it goes (Arc: the landing's spark, and the jump sound within
    //   JumpSoundsMax). The server still walks the whole chain at once (every target decided as before, from the creature the bounce
    //   leaves), deals each first jump then, and queues each bounce (BounceQueue, at most 512) to be dealt when its arc lands:
    //   BounceAfter after the first jump, with the server's own JumpDrawSeconds. BounceTick deals them from Main.OnUpdate (the spectral
    //   tick stops once no spectral arrow flies) in the order decided, at the creature's middle as it is then; a bounce whose creature
    //   died or went in the delay is dropped (a Debug line). A release's close does not hurry them.
    internal static class Ricochet {
        const int SurfaceCap = 2048, LogsPerRelease = 3, JumpCap = 256;
        static MelonPreferences_Entry<float> skipDegrees = null!, speedKeep = null!, maxHitDegrees = null!, skipShare = null!, jumpBatch = null!, realArrowSeconds = null!,
            kinkLength = null!, kinkSeconds = null!, kinkWidth = null!, jumpWidth = null!, jumpSeconds = null!, jumpHeight = null!, jumpVolume = null!,
            jumpDrawSeconds = null!, jumpSideways = null!, jumpVariety = null!, jumpLandSpread = null!, realArcWidth = null!, realArcWhiten = null!, bounceDelay = null!;
        static MelonPreferences_Entry<string> bounceLayers = null!, bounceChannels = null!, colour = null!, clickSound = null!, jumpSound = null!;
        static MelonPreferences_Entry<bool> bounceStatic = null!, realArrowJump = null!, looks = null!;
        static MelonPreferences_Entry<int> sparks = null!, clicksMax = null!, jumpSparks = null!, jumpSoundsMax = null!, arcsMax = null!, bounces = null!;
        static readonly Dictionary<int, bool> surfaces = new Dictionary<int, bool>();   // collider instance id -> skips (a local cache)
        static readonly List<string> channelNames = new List<string>();
        // the candidates the last Candidates call listed (ids, health objects, middles), read by the world's Land before the next call
        static readonly List<uint> ids = new List<uint>();
        static readonly List<HealthObject> healths = new List<HealthObject>();
        static readonly List<Vector3> centres = new List<Vector3>();
        static bool lookFailed;

        internal static void Init() {
            var cat = Cfg.Category("Ricochet"); // JumpMetres and JumpShare are created by Paths (the tablet's numbers)
            skipDegrees = Cfg.Shared(cat.CreateEntry("SkipDegrees", 10f, description: "Ricochet: a ghost or pellet that hits the ground skips off it at this many degrees and flies on along its own direction."));
            speedKeep = Cfg.Shared(cat.CreateEntry("SpeedKeep", 0.7f, description: "Ricochet: a skipping ghost keeps this share of its speed (0-1)."));
            maxHitDegrees = Cfg.Shared(cat.CreateEntry("MaxHitDegrees", 75f, description: "Ricochet: a ghost that comes into the ground steeper than this (degrees from the surface) does not skip; it lands as before."));
            skipShare = Cfg.Shared(cat.CreateEntry("SkipShare", 1f, description: "Ricochet: a skipped ghost's damage, as a share of its usual share."));
            realArrowJump = Cfg.Shared(cat.CreateEntry("RealArrowJump", true, description: "Ricochet: the real arrow's hit on its first creature jumps too, at JumpShare of that hit (a copy of the hit: its cripple is copied at the same share). The ghosts' jump carries no cripple, as the ghosts themselves."));
            realArrowSeconds = Cfg.Shared(cat.CreateEntry("RealArrowSeconds", 10f, description: "Ricochet: the real arrow's hit jumps only within this many seconds of the release (its flight; an arrow picked up and stabbed into a creature later does not jump)."));
            jumpBatch = Cfg.Shared(cat.CreateEntry("JumpBatchSeconds", 0.2f, description: "Ricochet: a creature's jump collects what the release's ghosts and pellets dealt it for this long after the first strike (or until the volley is over), then deals its share once."));
            bounceLayers = Cfg.Shared(cat.CreateEntry("BounceLayers", "Terrain", description: "Ricochet: ghosts skip off anything on these layers (names separated by ';')."));
            bounceChannels = Cfg.Shared(cat.CreateEntry("BounceChannels", "Wood Cutting;Mining Impact", description: "Ricochet: ghosts skip off anything whose impact channels include one of these (wood and rock; names separated by ';')."));
            bounceStatic = Cfg.Shared(cat.CreateEntry("BounceStatic", true, description: "Ricochet: ghosts skip off static world objects (no network entity behind them). Never off a creature or a player."));
            looks = cat.CreateEntry("Looks", true, description: "Looks of a skip (white spark, kink, click) and a jump (arc, spark, soft sound). Off changes nothing they do.");
            colour = cat.CreateEntry("Colour", "#FFFFFF", description: "Colour of a skip's spark and kink.");
            sparks = cat.CreateEntry("Sparks", 8, description: "Sparks thrown at a skip. 0 = none.");
            kinkLength = cat.CreateEntry("KinkLength", 0.5f, description: "Metres of path drawn before and after the skip point (the kink).");
            kinkSeconds = cat.CreateEntry("KinkSeconds", 0.35f, description: "Seconds the kink takes to fade.");
            kinkWidth = cat.CreateEntry("KinkWidth", 0.01f, description: "Width of the kink in metres.");
            clickSound = cat.CreateEntry("ClickSound", "Click", description: "Sound at a skip (names separated by ';', the first one found is used; empty = none).");
            clicksMax = cat.CreateEntry("ClicksMax", 3, description: "At most this many skip clicks per volley.");
            jumpWidth = cat.CreateEntry("JumpWidth", 0.01f, description: "Width in metres of a jump's arc.");
            jumpDrawSeconds = cat.CreateEntry("JumpDrawSeconds", 0.12f, description: "Seconds a jump's arc takes to draw itself from where the arrow hit to the creature it jumps to. 0 = at once. The server reads its own value too: it deals a bounce this plus BounceDelaySeconds after the jump before it, when the bounce's arc lands.");
            jumpSeconds = cat.CreateEntry("JumpSeconds", 0.2f, description: "Seconds a jump's arc takes to fade once it is drawn.");
            jumpHeight = cat.CreateEntry("JumpHeight", 0.4f, description: "Metres a jump's arc rises above the straight line, for a jump of 4 m (a longer jump rises more, a shorter one less: between half and twice this).");
            jumpSideways = cat.CreateEntry("JumpSideways", 0.35f, description: "Metres a jump's arc may bow to the left or right of the straight line, for a jump of 4 m; each arc bows its own amount, so the arcs of one volley fan out instead of drawing one line. 0 = none.");
            jumpVariety = cat.CreateEntry("JumpVariety", 0.4f, description: "How much each arc's rise differs from JumpHeight (0.4 = up to 40% more or less). 0 = every arc the same height.");
            jumpLandSpread = cat.CreateEntry("JumpLandSpread", 0.15f, description: "Metres round the middle of the creature jumped to within which each arc ends, so thirty arcs do not end on one point. 0 = all on its middle.");
            realArcWidth = cat.CreateEntry("RealArcWidth", 1.8f, description: "The real arrow's jump: its arc's width, times JumpWidth.");
            realArcWhiten = cat.CreateEntry("RealArcWhiten", 0.45f, description: "The real arrow's jump: how far its arc's colour goes from the ghosts' colour toward white (0 to 1), so it is the brightest arc.");
            jumpSparks = cat.CreateEntry("JumpSparks", 6, description: "Sparks where a jump lands. 0 = none.");
            jumpSound = cat.CreateEntry("JumpSound", "Skill Orb Interact Sounds", description: "Sound where a jump lands (names separated by ';', the first one found is used; empty = none).");
            jumpVolume = cat.CreateEntry("JumpVolume", 0.6f, description: "Volume of the jump's sound (0 to 1).");
            jumpSoundsMax = cat.CreateEntry("JumpSoundsMax", 3, description: "At most this many jump sounds per release.");
            arcsMax = cat.CreateEntry("ArcsMax", 60, description: "At most this many jump arcs drawn per release (one per jump, a bounce included, up to this; H5b: was 30 before jumps bounced on).");
            bounces = Cfg.Shared(cat.CreateEntry("Bounces", 2, description: $"Ricochet: how many creatures one jump chain reaches. 2 = a jump that lands on a creature jumps once more from it, for JumpShare of what it dealt there, to the nearest other creature within JumpMetres that the chain has not passed through; 1 = one jump, as before H5 (1 to {JumpRule.MaxBounces})."));
            bounceDelay = Cfg.Shared(cat.CreateEntry("BounceDelaySeconds", 0.25f, description: $"Ricochet: how long a bounce waits on the creature the jump before it landed on before it leaves for the next one, so it looks like it went from one to the other. A bounce's arc starts this long after the arc before it has landed (that creature sparks again as it leaves), and the server deals the bounce's hit when its arc lands: this plus JumpDrawSeconds after the hit before it. 0 = it leaves as the arc before it lands (0 to {JumpRule.MaxBounceDelaySeconds:0})."));
            Cfg.Reloaded += () => { surfaces.Clear(); Arcs.Retry(); };
        }

        /// <summary>H5b: [Ricochet] Bounces, held to 1 .. JumpRule.MaxBounces (a leaf: reads its own entry only).</summary>
        internal static int Bounces => JumpRule.Bounces(bounces != null ? bounces.Value : 2);

        /// <summary>H5c: [Ricochet] BounceDelaySeconds held by JumpRule.BounceDelay (a leaf: reads its own entry only).</summary>
        internal static float BounceDelay => JumpRule.BounceDelay(bounceDelay != null ? bounceDelay.Value : 0.25f);

        /// <summary>H5c: [Ricochet] JumpDrawSeconds, never negative (a leaf). The server reads its own value for a bounce's time.</summary>
        static float JumpDraw => jumpDrawSeconds != null ? Mathf.Max(0f, jumpDrawSeconds.Value) : 0.12f;

        /// <summary>The orb is on ([Tree] RicochetOrb) and this build is a Multi main that owns it.</summary>
        internal static bool Applies(Build b) => Tree.OrbOn(Orb.Ricochet) && RicochetRule.Applies(b);

        internal static float SpeedKeep => speedKeep != null ? speedKeep.Value : 0.7f;
        internal static float SkipShare => skipShare != null ? Mathf.Max(0f, skipShare.Value) : 1f;
        internal static float JumpMetres => Mathf.Max(0f, Paths.OrbNumbers.RicochetMetres);
        internal static float JumpShare => Mathf.Max(0f, Paths.OrbNumbers.RicochetJumpShare);

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- A. the ground skip (every machine that casts the shot)

        static void Describe(Collider c, out bool player, out bool creature, out string layer, out bool entity) {
            ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
            HealthObject h = det != null ? det.HealthTarget : null!;
#if QUEST
            player = h.Is<PlayerHealth>();
#else
            player = h is PlayerHealth;
#endif
            creature = h != null && !player && h.HasCrippleStat;
            layer = LayerMask.LayerToName(c.gameObject.layer) ?? "";
            entity = c.GetComponentInParent<NetworkEntity>() != null;
            channelNames.Clear();
            if (det != null && det.Channels != null) foreach (ImpactChannel ch in det.Channels) if (ch != null) channelNames.Add(ch.name);
        }

        static bool Skips(Collider c) {
            int key = c.GetInstanceID();
            if (surfaces.TryGetValue(key, out bool known)) return known;
            Describe(c, out bool player, out bool creature, out string layer, out bool entity);

            bool b = RicochetRule.Surface(player, creature, layer, entity, channelNames, bounceLayers.Value, bounceChannels.Value, bounceStatic.Value);
            if (surfaces.Count >= SurfaceCap) surfaces.Clear();
            surfaces[key] = b;
            return b;
        }

        /// <summary>VolleySim, a shot that may skip (RicochetRule.CanBounce) of a release whose record has Ricochet: whether this hit
        /// skips it, and if so where it starts again and which way it goes.</summary>
        static readonly int T_TrySkip = Timing.Section("Ricochet.TrySkip");
        internal static bool TrySkip(uint release, int index, RaycastHit hit, Vector3 step, out Vector3 start, out Vector3 dir) {
            using var _t = Timing.Begin(T_TrySkip, release);
            start = hit.point; dir = step;
            Targets.Release? rec = Targets.Of(release);
            if (rec == null || !rec.Ricochet || hit.collider == null || step.sqrMagnitude < 1e-8f || !Skips(hit.collider)) return false;
            if (!RicochetRule.Skip(step.x, step.y, step.z, hit.normal.x, hit.normal.y, hit.normal.z, skipDegrees.Value, maxHitDegrees.Value,
                                   out double dx, out double dy, out double dz)) {
                if (rec.SkipLogs++ < LogsPerRelease)
                    { if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Ricochet: arrow {release} spectral {index} hit '{hit.collider.name}' at {RicochetRule.HitDegrees(step.x, step.y, step.z, hit.normal.x, hit.normal.y, hit.normal.z):0} degrees (over {maxHitDegrees.Value:0}): no skip")); }
                return false;
            }
            start = hit.point + hit.normal * 0.02f;
            dir = new Vector3((float)dx, (float)dy, (float)dz);
            if (rec.SkipLogs++ < LogsPerRelease)
                { if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Ricochet: arrow {release} spectral {index} skipped off '{hit.collider.name}' at {Targets.P(hit.point)}, came in at {RicochetRule.HitDegrees(step.x, step.y, step.z, hit.normal.x, hit.normal.y, hit.normal.z):0} degrees{(NetworkSceneManager.IsServer ? "" : " (looks)")}")); }
            return true;
        }

        /// <summary>Clients: a skip's looks at <paramref name="at"/>: a white spark, a kink and a click, at most ClicksMax per release.</summary>
        static readonly int T_SkipLook = Timing.Section("look start Ricochet.SkipLook");
        internal static void SkipLook(uint release, Vector3 at, Vector3 inDir, Vector3 outDir) {
            using var _t = Timing.Begin(T_SkipLook, release);
            if (ApplicationManager.IsHeadless || lookFailed || looks == null || !looks.Value) return;   // b3-review M-6: a failed look stays off
            try {
                Color c = Cfg.Colour(colour, Color.white);
                VolleyFx.Sparks.Emit(at, outDir, Mathf.Max(0, sparks.Value), c, 50f, 3f);
                Line.Create("ArcheryRicochetKink", c, Mathf.Max(0.001f, kinkWidth.Value), Mathf.Max(0.05f, kinkSeconds.Value), 3,
                    (i, n) => i == 0 ? at - Unit(inDir) * Mathf.Max(0.05f, kinkLength.Value) : i == 1 ? at : at + Unit(outDir) * Mathf.Max(0.05f, kinkLength.Value), 0.25f);
                Targets.Release? rec = Targets.Of(release);
                if (rec != null && rec.Clicks < Mathf.Max(0, clicksMax.Value)) { rec.Clicks++; VolleyFx.Play(clickSound.Value, at); }
            }
            catch (Exception e) { LookFailed(e.Message); }
        }

        static Vector3 Unit(Vector3 v) => v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero;

        static void LookFailed(string why) {
            if (lookFailed) return;
            lookFailed = true;
            MelonLogger.Warning($"[Archery] Ricochet: a look could not be built; skips and jumps show nothing more ({why})");
        }

        // ---------------------------------------------------------------- B. the creature jump (play-test 7: every landed arrow)

        internal static int Spread => Math.Max(1, Paths.OrbNumbers.RicochetSpread);

        /// <summary>The jump candidates round creature <paramref name="fromId"/> on this machine, into <paramref name="into"/> (JumpWalk
        /// picks among them with JumpRule.Targets): the release's target list and the creatures it struck; with
        /// <paramref name="players"/> (server, PvP on) also every living player but the shooter, after the creatures. Indexes into
        /// <paramref name="into"/> are indexes into ids/healths/centres until the next call.</summary>
        static readonly int T_Candidates = Timing.Section("Ricochet.Candidates");
        static void Candidates(Targets.Release rec, uint fromId, HealthObject from, bool players, PlayerController? shooter, List<JumpCandidate> into, out Vector3 fromC) {
            using var _t = Timing.Begin(T_Candidates, fromId);
            fromC = Targets.Centre(from, out _);
            // fix batch D (fixA-review I-6): the server also looks round the struck creature itself, within JumpMetres (cached per
            // release); a client draws from the relayed list and the creatures it saw struck, so its arc can point elsewhere
            List<uint>? near = NetworkSceneManager.IsServer ? Targets.Nearby(rec, fromId, fromC, JumpMetres) : null;
            Targets.Candidates(rec, ids, healths, centres, near);
            into.Clear();
            for (int i = 0; i < ids.Count; i++) into.Add(new JumpCandidate(ids[i], centres[i].x, centres[i].y, centres[i].z, rec.Struck.Contains(ids[i]), false));
            if (players) {
                foreach (IPlayer ip in Player.AllPlayers) {
#if QUEST
                    if (!ip.Is(out Player p)) continue;
#else
                    if (!(ip is Player p) || p == null) continue;
#endif
                    PlayerController pc = p.PlayerController;
#if QUEST
                    if (pc == null || pc == shooter || !(pc.Health.Is(out PlayerHealth ph)) || ph == null || !(ph.Health > 0f)) continue;
#else
                    if (pc == null || pc == shooter || !(pc.Health is PlayerHealth ph) || ph == null || !(ph.Health > 0f)) continue;
#endif
                    Vector3? head = Targets.HeadOf(p);
                    if (!head.HasValue) continue;
                    uint pid = (uint)ph.GetInstanceID();
                    ids.Add(pid); healths.Add(ph); centres.Add(head.Value);
                    into.Add(new JumpCandidate(pid, head.Value.x, head.Value.y, head.Value.z, false, true));
                }
            }
        }

        /// <summary>H5b: the numbers a walk of this release's jumps uses.</summary>
        static JumpValues ValuesFor(Targets.Release rec) => new JumpValues(JumpMetres, Spread, JumpShare, rec.RicochetShare, Bounces);

        static readonly float[] OneHit = { 1f };

        /// <summary>H5b, server: deals one struck creature's jumps and their bounces (JumpWalk). <c>copyOf</c> set: each jump is a
        /// copy of the real arrow's hit at its share (Hits.Copy, its cripple copied at the same share, as the real arrow's jump always
        /// was); else a hand-made hit of <c>tool</c> at its multiplier with no cripple (Hits.Apply, as the ghosts' jumps, the line's
        /// and the needles'). Every jump is dealt under Hits.Dealing.</summary>
        sealed class ServerWorld : IJumpWorld {
            readonly Targets.Release rec;
            readonly uint release;
            readonly ImpactTool? tool;
            readonly ImpactHitWithTool? copyOf;
            readonly bool players;
            readonly PlayerController? shooter;
            readonly string what;
            readonly Dictionary<uint, HealthObject> known = new Dictionary<uint, HealthObject>();   // the creatures a chain may leave from
            Vector3 fromC;

            internal ServerWorld(Targets.Release rec, uint release, HealthObject struck, uint struckId, ImpactTool? tool, ImpactHitWithTool? copyOf, string what) {
                this.rec = rec; this.release = release; this.tool = tool; this.copyOf = copyOf; this.what = what;
                players = Pvp.JumpsLandOnPlayers;
                shooter = rec.Shooter != null ? rec.Shooter.PlayerController : null;
                known[struckId] = struck;
            }

            public bool Candidates(uint from, List<JumpCandidate> into, out float x, out float y, out float z) {
                x = y = z = 0f;
                if (!known.TryGetValue(from, out HealthObject h) || h == null) return false;
                Ricochet.Candidates(rec, from, h, players, shooter, into, out fromC);
                x = fromC.x; y = fromC.y; z = fromC.z;
                return true;
            }

            public void Nowhere(uint from, int depth, int arrows) {
                if (rec.JumpLogs++ < LogsPerRelease && Logs.Debug)
                    MelonLogger.Msg(Inv($"[Archery] Ricochet: arrow {release} {what}{(depth > 1 ? $", bounce {depth}" : "")}: {arrows} jump(s) from {NameOf(known.TryGetValue(from, out HealthObject h) ? h : null, from)}, no other living creature within {JumpMetres:0.#} m{(depth > 1 ? " that the chain has not passed" : "")} to jump to"));
            }

            public bool Land(uint from, int to, float multiplier, int depth, int arrows) {
                HealthObject target = healths[to];
                Vector3 at = centres[to];
                uint toId = ids[to];
                known.TryGetValue(from, out HealthObject fromH);
                // H5c: a bounce (depth 2 on) is dealt when its arc lands on its creature, JumpRule.BounceAfter after the first jump:
                // decided now (its target from the creature it leaves, as before), queued, dealt by BounceTick. The walk goes on from it
                // now, as every client's arcs do.
                float after = JumpRule.BounceAfter(depth, BounceDelay, JumpDraw);
                if (after > 0f) {
                    float now = Time.time;
                    var b = new Bounce {
                        Rec = rec, Release = release, FromId = from, FromHealth = fromH, Target = target, ToId = toId, FromC = fromC,
                        Tool = tool, CopyOf = copyOf, Multiplier = multiplier, Depth = depth, Arrows = arrows, What = what, Queued = now,
                    };
                    if (!waiting.Add(b, now + after)) { WaitingFull(release); return false; }
                    if (Targets.IsCreature(target)) known[toId] = target;   // a creature a jump lands on may pass it on (JumpWalk)
                    return true;
                }
                bool dealt = Hit(release, target, at, fromC, tool, copyOf, multiplier, out string how);
                if (dealt && Targets.IsCreature(target)) known[toId] = target;   // a creature a jump landed on may pass it on (JumpWalk)
                JumpLine(rec, release, what, depth, fromH, from, target, toId, (at - fromC).magnitude, arrows, multiplier, copyOf != null, dealt, how, 0f);
                return dealt;
            }
        }

        /// <summary>Server: one jump's hit on <paramref name="target"/> at <paramref name="at"/>, from <paramref name="fromC"/>.
        /// <paramref name="copyOf"/> set: a copy of the real arrow's hit at <paramref name="multiplier"/> (Hits.Copy, its cripple copied
        /// at the same share); else a hand-made hit of <paramref name="tool"/> with no cripple (Hits.Apply). Under Hits.Dealing.</summary>
        static bool Hit(uint release, HealthObject target, Vector3 at, Vector3 fromC, ImpactTool? tool, ImpactHitWithTool? copyOf, float multiplier, out string how) {
            using var _tel = HitScope.As(DealtBy.Ricochet, release); // balance telemetry
            Vector3 normal = (fromC - at).sqrMagnitude > 1e-6f ? (fromC - at).normalized : Vector3.up;
            bool dealt = false; how = "no detector";
            foreach (object o in target.ImpactDetectors) {
#if QUEST
                if (!(o as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).Is(out ImpactDetector det)) continue;
#else
                if (!(o is ImpactDetector det) || det == null) continue;
#endif
                // cripple 0: the ghosts carry no native stagger (FlushSpectral), so neither do their jumps (b3-review I-3)
                if (copyOf != null ? Hits.Copy(copyOf, det, at, normal, out how, multiplier) : tool != null && Hits.Apply(tool, det, at, normal, multiplier, out how, 0f)) { dealt = true; break; }
            }
            if (dealt) {
                Targets.Damaged(release, target);
                if (copyOf != null) how += Inv($" ({Hits.LastCopyDamage:0.###} damage)");
            }
            return dealt;
        }

        /// <summary>Server, Debug: one jump's line (at most LogsPerRelease x 4 a release). <paramref name="later"/>: seconds a bounce
        /// waited after it was decided (0 = dealt as decided).</summary>
        static void JumpLine(Targets.Release rec, uint release, string what, int depth, HealthObject? fromH, uint fromId, HealthObject target, uint toId,
                             float metres, int arrows, float multiplier, bool copy, bool dealt, string how, float later) {
            if (rec.JumpLogs++ < LogsPerRelease * 4 && Logs.Debug) // G2: Debug only, the string built behind the guard
                MelonLogger.Msg(Inv($"[Archery] Ricochet: arrow {release} {what} jump{(depth > 1 ? " (bounce " + depth.ToString(CultureInfo.InvariantCulture) + (later > 0f ? Inv($", {later:0.00} s after the first jump") : "") + ")" : "")} {NameOf(fromH, fromId)} -> '{target.name}' ({toId}) {metres:0.0} m, {arrows} arrow(s), x{multiplier:0.###} of {(copy ? "its hit" : "the arrow")}{(rec.RicochetOnPath ? "" : " (off the main path)")}{(copy ? "" : ", no cripple")}: {(dealt ? how : "not dealt (" + how + ")")}"));
        }

        static string NameOf(HealthObject? h, uint id) => h != null ? $"'{h.name}' ({id})" : id.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- H5c: the bounces waiting for their arcs to land (server)

        /// <summary>H5c, server: one bounce decided by a walk and waiting for its arc to land on <see cref="Target"/>.</summary>
        sealed class Bounce {
            public Targets.Release Rec = null!;
            public uint Release, FromId, ToId;
            public HealthObject? FromHealth;
            public HealthObject Target = null!;
            public Vector3 FromC;
            public ImpactTool? Tool;
            public ImpactHitWithTool? CopyOf;
            public float Multiplier, Queued;
            public int Depth, Arrows;
            public string What = "";
        }

        static readonly BounceQueue<Bounce> waiting = new BounceQueue<Bounce>();
        static readonly List<Bounce> dueNow = new List<Bounce>();
        static bool waitingFullWarned;

        static void WaitingFull(uint release) {
            if (waitingFullWarned) return;
            waitingFullWarned = true;
            MelonLogger.Warning($"[Archery] Ricochet: {BounceQueue<Bounce>.Cap} bounces are waiting; arrow {release}'s next bounce and any more past the cap are dropped (said once a session)");
        }

        /// <summary>H5c, server, every frame (Main.OnUpdate, before the headless server's early return): the waiting bounces whose
        /// time has come are dealt, in the order they were decided. A release's close does not hurry them (Ricochet.JumpClosed): it
        /// usually comes in the same tick as the volley's hits, so the bounce would be back in the same call as the first jump.</summary>
        internal static void BounceTick() {
            if (waiting.Count == 0) return;
            dueNow.Clear();
            if (waiting.TakeDue(Time.time, dueNow) > 0)
                foreach (Bounce b in dueNow) DealBounce(b);
            dueNow.Clear();
        }

        static readonly int T_DealBounce = Timing.Section("hit Ricochet.DealBounce (a bounce whose arc landed)");
        static void DealBounce(Bounce b) {
            using var _t = Timing.Begin(T_DealBounce, b.Release);
            try {
                HealthObject target = b.Target;
                // the creature it goes to died or went in the delay: dropped quietly (a deeper bounce from it still comes: every client
                // drew the whole chain when the arrow landed)
                if (target == null || !(target.Health > 0f)) {
                    if (b.Rec.JumpLogs++ < LogsPerRelease * 4 && Logs.Debug)
                        MelonLogger.Msg(Inv($"[Archery] Ricochet: arrow {b.Release} {b.What} bounce {b.Depth} {NameOf(b.FromHealth, b.FromId)} -> {(target != null ? "'" + target.name + "' " : "")}({b.ToId}) dropped: it {(target == null ? "is gone" : "died")} in the delay"));
                    return;
                }
                Vector3 at = Targets.Centre(target, out _);   // where it is now: it may have moved in the delay
                bool dealt = Hit(b.Release, target, at, b.FromC, b.Tool, b.CopyOf, b.Multiplier, out string how);
                JumpLine(b.Rec, b.Release, b.What, b.Depth, b.FromHealth, b.FromId, target, b.ToId, (at - b.FromC).magnitude, b.Arrows, b.Multiplier, b.CopyOf != null, dealt, how, Time.time - b.Queued);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Ricochet: arrow {b.Release} bounce {b.Depth} failed: {e.Message}"); }
        }

        /// <summary>H5b, server: walks the jumps of <paramref name="arrows"/> that landed on <paramref name="struck"/>, and their bounces.</summary>
        static readonly int T_Walk = Timing.Section("hit Ricochet.Walk (jumps and bounces)");
        static void Walk(Targets.Release rec, uint release, HealthObject struck, uint struckId, IList<float> arrows, ImpactTool? tool, ImpactHitWithTool? copyOf, string what) {
            using var _t = Timing.Begin(T_Walk, release);
            var world = new ServerWorld(rec, release, struck, struckId, tool, copyOf, what);
            JumpWalk.Run(world, rec.Jumps, struckId, arrows, ValuesFor(rec));
        }

        sealed class Jump {
            public uint Release, FromId;
            public HealthObject From = null!;
            public ImpactTool Tool = null!;
            public readonly List<float> Arrows = new List<float>();   // each landed arrow's own damage, as a share of the arrow's
            public float Due;
        }

        static readonly List<Jump> jumps = new List<Jump>();

        /// <summary>Server, from Hits.FlushSpectral after a dealt spectral hit on a creature: <paramref name="shares"/> are the shares of
        /// the arrows that landed on it this tick, one each, times <paramref name="charge"/>. Every one of them jumps; their damage is
        /// collected for JumpBatchSeconds (or until the release closes) and dealt as one hit per target. <paramref name="source"/>:
        /// what dealt the hit; only the hits JumpRule.MayJump allows queue a jump (never a jump's or a blast's own).</summary>
        static readonly int T_ServerStruck = Timing.Section("Ricochet.ServerStruck");
        internal static void ServerStruck(uint release, HealthObject target, IList<float> shares, float charge, ImpactTool tool, HitSource source) {
            using var _t = Timing.Begin(T_ServerStruck, release);
            try {
                if (!JumpRule.MayJump(source)) return;
                Targets.Release? rec = Targets.Of(release);

                if (rec == null || !rec.Ricochet || !Targets.IsCreature(target) || tool == null || shares == null || shares.Count == 0) return;
                uint id = Targets.IdOf(target);
                if (id == 0u) return;
                rec.Struck.Add(id);
                Jump? j = null;
                foreach (Jump w in jumps) if (w.Release == release && w.FromId == id) { j = w; break; }
                if (j == null) {
                    if (jumps.Count >= JumpCap) { MelonLogger.Warning($"[Archery] Ricochet: {jumps.Count} jumps waiting; arrow {release}'s jumps from '{target.name}' are dropped"); return; }
                    j = new Jump { Release = release, FromId = id, From = target, Tool = tool, Due = Time.time + Mathf.Max(0f, jumpBatch.Value) };
                    jumps.Add(j);
                }
                foreach (float s in shares) j.Arrows.Add(s * charge);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Ricochet: arrow {release} jump failed: {e.Message}"); }
        }

        /// <summary>Server, every spectral tick (Hits.SimTick): jumps whose batch time is over are dealt.</summary>
        static readonly int T_JumpTick = Timing.Section("Ricochet.JumpTick");
        internal static void JumpTick() {
            using var _t = Timing.Begin(T_JumpTick);
            if (jumps.Count == 0) return;
            float now = Time.time;
            for (int i = jumps.Count - 1; i >= 0; i--) if (jumps[i].Due <= now) { Jump j = jumps[i]; jumps.RemoveAt(i); Deal(j); }
        }

        /// <summary>Server, when a release's last spectral arrow has ended (Hits.Close): its waiting jumps are dealt now. H5c: their
        /// bounces (and any already waiting) keep their own time (BounceTick): the close mostly comes in the very tick the volley
        /// struck, and dealing them here would bring the bounce back into the same call as the first jump.</summary>
        internal static void JumpClosed(uint release) {
            for (int i = jumps.Count - 1; i >= 0; i--) if (jumps[i].Release == release) { Jump j = jumps[i]; jumps.RemoveAt(i); Deal(j); }
        }

        static readonly int T_Deal = Timing.Section("hit Ricochet.Deal (jump)");
        static void Deal(Jump j) {
            using var _t = Timing.Begin(T_Deal);
            try {
                Targets.Release? rec = Targets.Of(j.Release);
                if (j.Tool == null) { if (Logs.Debug) MelonLogger.Msg($"[Archery] Ricochet: arrow {j.Release} jumps from {j.FromId} dropped (the arrow's tool is gone)"); return; }
                if (rec == null || j.From == null) { if (Logs.Debug) MelonLogger.Msg($"[Archery] Ricochet: arrow {j.Release} jumps from {j.FromId} dropped (the release or the struck creature is gone)"); return; }
                // fix batch D: x SideOrbOffPathShare off the Multi main (JumpRule.ShareAt); H5b: each jump bounces on (JumpWalk)
                Walk(rec, j.Release, j.From, j.FromId, j.Arrows, j.Tool, null, "spectral");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Ricochet: arrow {j.Release} dealing a jump failed: {e.Message}"); }
        }

        /// <summary>Server, from Weight's real-arrow hook after Targets.RealHit: the real arrow's creature hit jumps at once, at
        /// JumpShare of that hit (a copy of it, cripple included, under Hits.Dealing), to the struck creature's next target in turn.
        /// Only the arrow's first creature and only within RealArrowSeconds of the release (b3-review M-5).</summary>
        static readonly int T_ServerRealHit = Timing.Section("Ricochet.ServerRealHit");
        internal static void ServerRealHit(uint arrowId, HealthObject h, ImpactHitWithTool hit) {
            using var _t = Timing.Begin(T_ServerRealHit, arrowId);
            try {
                Targets.Release? rec = Targets.Of(arrowId);
                if (rec == null || !rec.Ricochet || !realArrowJump.Value || hit == null || !Targets.IsCreature(h)) return;
                uint id = Targets.IdOf(h);
                if (id == 0u || rec.RealHit != id || rec.RealJumped || Time.time - rec.At > Mathf.Max(0f, realArrowSeconds.Value)) return;
                rec.RealJumped = true; // fixA-review M-4: the real arrow's hit jumps once per release (its parts each report a hit)
                // one jump at JumpShare x SideOrbOffPathShare of its hit (a copy: JumpRule.ShareAt of 1), to the struck creature's next
                // target in turn; H5b: it bounces on as a copy at JumpShare of that (JumpWalk)
                Walk(rec, arrowId, h, id, OneHit, hit.Tool, hit, "real arrow");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Ricochet: arrow {arrowId} real-arrow jump failed: {e.Message}"); }
        }

        /// <summary>Server, fix batch D (off the main path): a Quickdraw 4 line's first creature jumps once, at JumpShare x the off-path
        /// share of what the line dealt it (<paramref name="lineMultiplier"/>: the line's own multiplier on the arrow's power), no
        /// cripple of its own (dealt as Hits.Apply under Hits.Dealing). Nothing without the orb.</summary>
        /// <param name="needle">A needle line of the same release (fix batch D: every arrow of the release that hits jumps): each one
        /// jumps, the line's main hit only once.</param>
        static readonly int T_ServerLineHit = Timing.Section("Ricochet.ServerLineHit");
        internal static void ServerLineHit(uint arrowId, HealthObject h, ImpactTool tool, float lineMultiplier, bool needle = false) {
            using var _t = Timing.Begin(T_ServerLineHit, arrowId);
            try {
                Targets.Release? rec = Targets.Of(arrowId);
                if (rec == null || !rec.Ricochet || tool == null || !Targets.IsCreature(h)) return;
                if (!needle && (!realArrowJump.Value || rec.RealJumped)) return;
                if (!needle) Targets.RealHit(arrowId, h);
                uint id = Targets.IdOf(h);
                if (id == 0u) return;
                if (needle) rec.Struck.Add(id); else rec.RealJumped = true;
                // one jump at JumpShare x SideOrbOffPathShare of the line's multiplier, no cripple, to the struck creature's next target
                // in turn; H5b: it bounces on (JumpWalk)
                Walk(rec, arrowId, h, id, new[] { lineMultiplier }, tool, null, needle ? "needle line" : "line");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Ricochet: arrow {arrowId} line jump failed: {e.Message}"); }
        }

        /// <summary>Clients, an arrow of the release landing on a creature (a spectral hit this client cast, VolleySim, or the real
        /// arrow's, <paramref name="real"/>, Weight's hook): one arc to the struck creature's next target in turn on this machine's
        /// view, at most ArcsMax arcs and JumpSoundsMax sounds per release. Looks only (players are never drawn as targets: a client
        /// does not know the server's PvP setting).</summary>
        /// <param name="hitPoint">Where the arrow hit the creature (its arc starts there); null = the creature's middle.</param>
        static readonly int T_ClientStruck = Timing.Section("look start Ricochet.ClientStruck");
        internal static void ClientStruck(uint release, HealthObject h, bool real = false, Vector3? hitPoint = null) {
            using var _t = Timing.Begin(T_ClientStruck, release);
            if (ApplicationManager.IsHeadless || NetworkSceneManager.IsServer || lookFailed) return;   // b3-review M-6
            try {
                Targets.Release? rec = Targets.Of(release);
                if (rec == null || !rec.Ricochet || !Targets.IsCreature(h) || (real && !realArrowJump.Value)) return;
                uint id = Targets.IdOf(h);
                if (id == 0u) return;
                rec.Struck.Add(id);
                if (real) { if (rec.RealJumped) return; rec.RealJumped = true; } // fixA-review M-4: the real arrow's arc once per release
                // fixA-review M-5: the cap before the candidate search (thirty landed arrows in one tick, in VR)
                if (looks == null || !looks.Value || rec.Arcs >= Mathf.Max(0, arcsMax.Value)) return;
                // H5b: the arc to the struck creature's next target in turn, and each bounce's arc after it (JumpWalk, as the server)
                var world = new ClientWorld(rec, release, h, id, hitPoint, real);
                JumpWalk.Run(world, rec.Jumps, id, OneHit, ValuesFor(rec));
            }
            catch (Exception e) { LookFailed(e.Message); }
        }

        /// <summary>H5b, clients: draws one landed arrow's jump and each bounce of it (looks only). Each arc starts where the arc
        /// before it landed (the first where the arrow hit) and waits for that arc to land before it draws itself (JumpDrawSeconds
        /// per bounce). A chain stops at the release's ArcsMax.</summary>
        sealed class ClientWorld : IJumpWorld {
            readonly Targets.Release rec;
            readonly uint release;
            readonly bool real;
            readonly Color colour;
            readonly float alpha;
            readonly Dictionary<uint, HealthObject> known = new Dictionary<uint, HealthObject>();
            readonly Dictionary<uint, Vector3> landedAt = new Dictionary<uint, Vector3>();   // where the arc into each creature ended
            Vector3 fromC;

            internal ClientWorld(Targets.Release rec, uint release, HealthObject struck, uint struckId, Vector3? hitPoint, bool real) {
                this.rec = rec; this.release = release; this.real = real;
                known[struckId] = struck;
                if (hitPoint.HasValue) landedAt[struckId] = hitPoint.Value;
                // H2: the jumps of a locked (homing) release are the homing green, as everything its arrows do
                colour = Seek.Homes(release) ? SeekLook.Colour : rec.Gold ? Volley.ChargedColour : Volley.Colour;
                alpha = rec.Local ? 1f : Mathf.Clamp01(Volley.OtherArcherBrightness);
            }

            public bool Candidates(uint from, List<JumpCandidate> into, out float x, out float y, out float z) {
                x = y = z = 0f;
                // fixA-review M-5: the cap before the candidate search, for every bounce too
                if (looks == null || !looks.Value || rec.Arcs >= Mathf.Max(0, arcsMax.Value)) return false;
                if (!known.TryGetValue(from, out HealthObject h) || h == null) return false;
                Ricochet.Candidates(rec, from, h, false, null, into, out fromC);
                x = fromC.x; y = fromC.y; z = fromC.z;
                return true;
            }

            public void Nowhere(uint from, int depth, int arrows) { }

            public bool Land(uint from, int to, float multiplier, int depth, int arrows) {
                HealthObject target = healths[to];
                Vector3 toC = centres[to];
                uint toId = ids[to];
                Vector3 start = landedAt.TryGetValue(from, out Vector3 p) ? p : fromC;
                if (rec.JumpLogs++ < LogsPerRelease) { if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Ricochet: arrow {release} jump seen{(depth > 1 ? $" (bounce {depth})" : "")} {from} -> '{target.name}' ({toId}) {(toC - fromC).magnitude:0.0} m{(real ? " (real arrow)" : "")}")); }
                int arc = rec.Arcs++;
                int soundsMax = Mathf.Max(0, jumpSoundsMax.Value);
                // H5c: a bounce's arc starts BounceDelaySeconds after the arc before it has landed (JumpRule.BounceAfter, the same time
                // the server deals its hit), and the creature it leaves sparks again as it goes (its sound first in JumpSoundsMax: it
                // comes before the landing). With no pause the arc before it lands as it leaves: nothing more.
                float pause = BounceDelay;
                float delay = JumpRule.BounceAfter(depth, pause, JumpDraw);
                bool leaves = depth > 1 && pause > 0f;
                bool leaveSound = leaves && rec.JumpSounds < soundsMax;
                if (leaveSound) rec.JumpSounds++;
                bool sound = rec.JumpSounds < soundsMax;
                if (sound) rec.JumpSounds++;
                // fix batch C: one arc of its own for this arrow, from where it hit (pooled); the plain arc when that cannot be built
                if (!Arcs.Draw(release, arc, start, toC, colour, alpha, real, sound, delay, leaves, leaveSound, out Vector3 end)) {
                    end = toC;
                    Color c = colour;
                    Vector3 lift = Vector3.up * jumpHeight.Value, a0 = fromC; // the plain arc runs middle to middle, as before
                    Line.Create("ArcheryRicochetJump", new Color(c.r, c.g, c.b, alpha), Mathf.Max(0.001f, jumpWidth.Value), PlainJumpSeconds, 12,
                        (i, n) => { float t = i / (float)(n - 1); return Vector3.Lerp(a0, toC, t) + lift * (4f * t * (1f - t)); }, 1f);
                    VolleyFx.Sparks.Emit(toC, Vector3.up, Mathf.Max(0, jumpSparks.Value), c, 60f, 2f);
                    if (sound) VolleyFx.Play(jumpSound.Value, toC, 0f, Mathf.Clamp01(jumpVolume.Value));
                }
                landedAt[toId] = end;
                if (Targets.IsCreature(target)) known[toId] = target;
                return true;
            }
        }

        const float PlainJumpSeconds = 0.35f; // the plain arc's fade, as it shipped before fix batch C

        // Fix batch C (play-test 7: every landed arrow jumps, so every landed arrow shows its own arc; "it only does like one or
        // two"). One thin arc per landed arrow, from where that arrow hit to the creature it jumps to, in the ghosts' colour. Each
        // has a curve of its own (JumpArc.Shape, seeded from the release's arrow id and the arc's number: its rise and how far it
        // bows left or right), and ends within JumpLandSpread of the target's middle, so thirty arcs fan out as a sheaf and never
        // draw one line. It draws itself from the hit to the target over JumpDrawSeconds, lands with a small spark (and the jump's
        // sound for the first JumpSoundsMax of the release), then fades over JumpSeconds. The real arrow's arc is RealArcWidth
        // times as wide, whitened by RealArcWhiten, and is the plain middle curve (no bow, the set height).
        // Cost: the arcs are pooled (one GameObject with one LineRenderer of JumpArc.Points points each, made once and reused, at
        // most PoolCap alive); a release draws at most ArcsMax; per frame an arc sets its points while it draws and two colours
        // while it fades (no allocation). When the pool is busy the arc is skipped and only its landing spark shows.
        static class Arcs {
            const int PoolCap = 64;
            const float HitEndAlpha = 0.35f; // the arc is fainter where it left the struck creature than where it lands
            static readonly List<Arc> pool = new List<Arc>();
            static bool failed, logged;

            /// <summary>Settings reloaded: a failed look is tried again.</summary>
            internal static void Retry() => failed = false;

            /// <summary>False when the arcs cannot be built (said once): the caller draws the plain arc instead.</summary>
            /// <param name="delay">H5b: seconds before the arc starts to draw itself (a bounce waits for the arc before it to land;
            /// H5c: and then BounceDelaySeconds more).</param>
            /// <param name="leaves">H5c: when it starts, the creature it leaves sparks as an arc's landing does (a bounce after a pause).</param>
            /// <param name="leaveSound">H5c: and the jump's sound plays there.</param>
            /// <param name="end">Where the arc ends (its target's middle give or take JumpLandSpread): the next bounce's arc starts there.</param>
            static readonly int T_Draw = Timing.Section("look start Ricochet.Draw (arc)");
            internal static bool Draw(uint release, int arc, Vector3 from, Vector3 to, Color c, float alpha, bool real, bool sound, float delay, bool leaves, bool leaveSound, out Vector3 end) {
                using var _t = Timing.Begin(T_Draw, release);
                end = to;
                if (failed) return false;
                try {
                    float distance = (to - from).magnitude;
                    float lift = 1f, side = 0f;
                    if (!real) {
                        JumpArc.Shape(release, arc, jumpVariety.Value, out lift, out side);
                        float spread = Mathf.Max(0f, jumpLandSpread.Value);
                        if (spread > 0f)
                            to += new Vector3(LookSeed.Unit(release, arc, 13) * 2f - 1f, LookSeed.Unit(release, arc, 14) - 0.5f, LookSeed.Unit(release, arc, 15) * 2f - 1f) * spread;
                    }
                    lift *= JumpArc.Height(distance, jumpHeight.Value);
                    side *= JumpArc.Height(distance, jumpSideways.Value);
                    Color colour = real ? Color.Lerp(c, Color.white, Mathf.Clamp01(realArcWhiten.Value)) : c;
                    colour.a = alpha;
                    float width = Mathf.Max(0.001f, jumpWidth.Value) * (real ? Mathf.Max(0.1f, realArcWidth.Value) : 1f);
                    int sparks = Mathf.Max(0, jumpSparks.Value);
                    float volume = sound ? Mathf.Clamp01(jumpVolume.Value) : 0f;
                    end = to;
                    Arc? a = Take();
                    if (a == null) { Land(to, colour, sparks, volume); return true; } // every pooled arc is busy
                    a.Go(from, to, lift, side, colour, width, Mathf.Max(0f, jumpDrawSeconds.Value), Mathf.Max(0f, jumpSeconds.Value), sparks, volume, delay,
                         leaves ? sparks : 0, leaveSound ? Mathf.Clamp01(jumpVolume.Value) : 0f);
                    if (!logged) { logged = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] Ricochet: jump arcs: one per landed arrow from where it hit, at most {Mathf.Max(0, arcsMax.Value)} a release ({PoolCap} pooled); first drawn for arrow {release}"); }
                    return true;
                }
                catch (Exception e) { Fail(e.Message); return false; }
            }

            static void Fail(string why) {
                if (failed) return;
                failed = true;
                MelonLogger.Warning($"[Archery] Ricochet: the jump arcs could not be built ({why}); jumps show the plain arc instead");
            }

            static Arc? Take() {
                foreach (Arc p in pool) if (p != null && !p.Busy) return p;
                if (pool.Count >= PoolCap) { pool.RemoveAll(p => p == null); if (pool.Count >= PoolCap) return null; }
                if (Fx.LineMaterial() == null) throw new InvalidOperationException("no line material");
                var go = new GameObject("ArcheryRicochetArc");
                UnityEngine.Object.DontDestroyOnLoad(go);
                Arc a = go.AddComponent<Arc>();
                a.Build();
                pool.Add(a);
                return a;
            }

            /// <summary>Where a jump lands: a small spark, and the jump's sound when this arc carries one.</summary>
            static void Land(Vector3 at, Color c, int sparks, float volume) {
                try {
                    VolleyFx.Sparks.Emit(at, Vector3.up, sparks, c, 60f, 2f);
                    if (volume > 0f) VolleyFx.Play(jumpSound.Value, at, 0f, volume);
                }
                catch (Exception e) { Fail(e.Message); }
            }

            sealed class Arc : MonoBehaviour {
#if QUEST
                public Arc(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
                LineRenderer line = null!;
                Vector3 from, to;
                Color colour;
                float lift, side, born, draw, fade, volume, shown, leaveVolume;
                int sparks, leaveSparks;
                bool landed;
                internal bool Busy => gameObject.activeSelf;

                internal void Build() {
                    line = Fx.NewLine(gameObject, JumpArc.Points, 0.01f, false);
                    gameObject.SetActive(false);
                }

                internal void Go(Vector3 a, Vector3 b, float liftMetres, float sideMetres, Color c, float width, float drawSeconds, float fadeSeconds, int landSparks, float landVolume, float delaySeconds,
                                 int departSparks, float departVolume) {
                    from = a; to = b; lift = liftMetres; side = sideMetres; colour = c; draw = drawSeconds; fade = fadeSeconds;
                    sparks = landSparks; volume = landVolume; landed = false; shown = -1f;
                    // H5b: a bounce's arc is busy (taken from the pool) but hidden until its turn
                    float wait = delaySeconds > 0f && !float.IsInfinity(delaySeconds) ? delaySeconds : 0f;
                    // H5c: what its start shows when it leaves after its wait (an arc that starts at once leaves from the arrow's own hit)
                    leaveSparks = wait > 0f ? departSparks : 0; leaveVolume = wait > 0f ? departVolume : 0f;
                    born = Time.time + wait;
                    line.widthMultiplier = width;
                    line.enabled = wait <= 0f;
                    gameObject.SetActive(true);
                    if (line.enabled) Frame(0f);
                }

                // the points are spread over the part drawn so far, so the line grows from the hit toward its target
                void Frame(float age) {
                    float drawn = JumpArc.Drawn(age, draw);
                    if (drawn != shown) {
                        shown = drawn;
                        for (int i = 0; i < JumpArc.Points; i++) {
                            JumpArc.Point(from.x, from.y, from.z, to.x, to.y, to.z, lift, side, drawn * i / (JumpArc.Points - 1), out float x, out float y, out float z);
                            line.SetPosition(i, new Vector3(x, y, z));
                        }
                    }
                    float k = JumpArc.Alpha(age, draw, fade);
                    line.startColor = new Color(colour.r, colour.g, colour.b, colour.a * k * HitEndAlpha);
                    line.endColor = new Color(colour.r, colour.g, colour.b, colour.a * k);
                    if (!landed && drawn >= 1f) { landed = true; Land(to, colour, sparks, volume); }
                }

                static readonly int TUpdate = Timing.Section("Update Ricochet.Arc");
                void Update() {
                    using var _t = Timing.Begin(TUpdate);
                    float age = Time.time - born;
                    if (age < 0f) return;                    // H5b: a bounce waits for the arc before it to land (H5c: and its pause)
                    if (!line.enabled) {                     // its turn: the points are set below before it is drawn
                        line.enabled = true;
                        // H5c: it leaves the creature the arc before it landed on: that creature sparks (and sounds) again as it goes
                        if (leaveSparks > 0 || leaveVolume > 0f) Land(from, colour, leaveSparks, leaveVolume);
                    }
                    if (JumpArc.Over(age, draw, fade)) {
                        if (!landed) { landed = true; Land(to, colour, sparks, volume); }
                        gameObject.SetActive(false);
                        return;
                    }
                    Frame(age);
                }
            }
        }

        // One fading line on its own object (one Renderer): the skip's kink and the jump's arc. Points are set once; each frame only
        // the colours change (no allocation).
        sealed class Line : MonoBehaviour {
#if QUEST
            public Line(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            LineRenderer line = null!;
            Color colour;
            float born, life, tailAlpha;

            internal static void Create(string name, Color c, float width, float seconds, int points, Func<int, int, Vector3> at, float tail) {
                if (Fx.LineMaterial() == null) { LookFailed("no line material"); return; }
                var go = new GameObject(name);
                Line l = go.AddComponent<Line>();
                l.line = Fx.NewLine(go, points, width, false);
                for (int i = 0; i < points; i++) l.line.SetPosition(i, at(i, points));
                l.colour = c; l.born = Time.time; l.life = seconds; l.tailAlpha = tail;
                l.Paint(1f);
            }

            void Paint(float k) {
                line.startColor = new Color(colour.r, colour.g, colour.b, colour.a * tailAlpha * k);
                line.endColor = new Color(colour.r, colour.g, colour.b, colour.a * k);
            }

            static readonly int TUpdate = Timing.Section("Update Ricochet.Line");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float t = (Time.time - born) / life;
                if (t >= 1f) { Destroy(gameObject); return; }
                Paint(1f - t);
            }
        }
    }
}
