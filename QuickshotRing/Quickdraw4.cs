using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Impact;
using Alta.Networking;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Quickdraw 4: the top Quickdraw orb. A charged quickshot does not fly: it is an instant, perfectly
    // straight line. The server resolves the hit and puts the real arrow where the line ends; every client
    // draws the line.
    // Batch 4b (user, 2026-11-16): the line pierces. It passes through every creature on its path (LinePierce in Rules.cs,
    // tested: nearest first, each creature once, at most [Quickdraw4] PierceMax) and stops at the first surface or player, or in
    // the last creature it may strike; the real arrow is embedded there. The first creature takes the line's damage and
    // everything that rides it (snap weight's stagger and interrupt); each later one its PierceShares share of the damage
    // and of the stagger, never an interrupt. Clients run the same path from their own view and draw a small hit at
    // every creature passed (no new message).
    //
    // How clients learn about it with no new network message: the server sends the game's ordinary arrow
    // flight sync with an impossible speed (Marker). Clients treat that as "this shot was a Quickdraw 4 line".
    internal static class Quickdraw4 {
        internal const float Marker = 999f;        // the flight sync carries speeds up to 1000
        static readonly Color DefaultColour = new Color(0.72f, 0.9f, 1f);

        static MelonPreferences_Entry<float> damage = null!, maxReach = null!, lineWidth = null!, lineHold = null!, lineFade = null!, hitFlash = null!,
            spiralRadius = null!, spiralTurn = null!, spiralWidth = null!, spiralSpin = null!, spiralMax = null!, spiralFlare = null!,
            spikeLength = null!, spikeSpread = null!, spikeSeconds = null!, needleWidth = null!, needleFlash = null!, pierceFlash = null!;
        static MelonPreferences_Entry<int> spiralStrands = null!, spikes = null!, pierceMax = null!, pierceSpikes = null!;
        static MelonPreferences_Entry<string> colour = null!, pierceShares = null!;

        struct ShotInfo { public Vector3 origin, direction; public float time; public Transform? shooter, bow, bowString; }
        struct Result {
            public Vector3 origin, end, direction, normal; public bool struck, needlesDrawn; public float time;
            public Transform? shooter, bow, bowString; // what the casts ignore (the needles are cast later for a spectator)
        }
        static readonly Dictionary<Arrow, ShotInfo> shots = new Dictionary<Arrow, ShotInfo>();
        static readonly Dictionary<Arrow, Result> results = new Dictionary<Arrow, Result>(); // client: where each line shot ended
        static Arrow? lastLocalArrow;
        static bool localHasIt;
        static readonly MethodInfo? embedMethod = AccessTools.Method(typeof(Embeddable), "Embed");

        internal static Color Colour => Cfg.Colour(colour, DefaultColour);

        internal static void Init() {
            var cat = Cfg.Category("Quickdraw4");
            damage = Cfg.Shared(cat.CreateEntry("Damage", LinePierce.DefaultDamage, description: "Damage multiplier on the arrow. Replaces the normal Quickdraw bonus. (Play-test 7: 3 -> 2.5, so a full Charge Shot 3 hits much harder per arrow.)"));
            maxReach = Cfg.Shared(cat.CreateEntry("MaxReach", 150f, description: "Longest the line can be, in metres."));
            // batch 4b (user, 2026-11-16): "Quickdraw 4 gains pierce. The line shot passes through every creature on its path"
            pierceShares = Cfg.Shared(cat.CreateEntry("PierceShares", LinePierce.DefaultSharesText, description: "The line passes through every creature on its path: the first takes the line's full damage, the second the first value here, the third the second, and so on; the last value repeats. Values 0 to 1, separated by ',' (unreadable text: the default 0.7, 0.5)."));
            pierceMax = Cfg.Shared(cat.CreateEntry("PierceMax", LinePierce.DefaultMax, description: "The line strikes at most this many creatures; it ends in the last one."));
            colour = cat.CreateEntry("Colour", "#B8E6FF");
            lineWidth = cat.CreateEntry("LineWidth", 0.035f, description: "Width of the straight centre line.");
            lineHold = cat.CreateEntry("LineHoldSeconds", 0.08f, description: "How long the line stays at full brightness.");
            lineFade = cat.CreateEntry("LineFadeSeconds", 0.5f);
            spiralStrands = cat.CreateEntry("SpiralStrands", 2, description: "Spirals winding round the centre line (0 to 4). 0 = just the line.");
            spiralRadius = cat.CreateEntry("SpiralRadius", 0.1f, description: "How far the spirals stand off the centre line, in metres.");
            spiralTurn = cat.CreateEntry("SpiralTurnMetres", 0.9f, description: "Length of one full turn of a spiral.");
            spiralWidth = cat.CreateEntry("SpiralWidth", 0.01f);
            spiralSpin = cat.CreateEntry("SpiralSpin", 2.5f, description: "Turns per second the spirals rotate while they fade.");
            spiralFlare = cat.CreateEntry("SpiralFlare", 0.8f, description: "How much the spirals open outward as they fade (0 = stay the same size).");
            spiralMax = cat.CreateEntry("SpiralMaxMetres", 60f, description: "Spirals are only drawn along this much of a long shot; the centre line always goes the whole way.");
            hitFlash = cat.CreateEntry("HitFlash", 6f, description: "Brightness of the snap of light where the line ends. 0 = none.");
            spikes = cat.CreateEntry("ImpactSpikes", 12, description: "Sharp straight spikes snapping out of the hit point.");
            spikeLength = cat.CreateEntry("ImpactSpikeLength", 0.7f);
            spikeSpread = cat.CreateEntry("ImpactSpikeSpread", 0.8f, description: "0 = all spikes straight back out of the surface; higher = wider burst.");
            spikeSeconds = cat.CreateEntry("ImpactSpikeSeconds", 0.16f);
            needleWidth = cat.CreateEntry("NeedleLineWidth", 0.012f, description: "Width of each needle's line (a Quickdraw main's multi-arrow orbs), beside the centre line.");
            needleFlash = cat.CreateEntry("NeedleHitFlash", 0.25f, description: "Where a needle's line lands: its flash as a share of HitFlash. 0 = none.");
            pierceFlash = cat.CreateEntry("PierceHitFlash", 0.5f, description: "Where the line passes through a creature: a flash this share of HitFlash. 0 = none.");
            pierceSpikes = cat.CreateEntry("PierceSpikes", 4, description: "Short spikes where the line passes through a creature (ImpactSpikeLength at half length). 0 = none.");
        }

        static string sharesText = "";
        static float[] shares = LinePierce.DefaultShares;

        /// <summary>[Quickdraw4] PierceShares, parsed once per change (an unreadable value logs once and uses the default).</summary>
        internal static float[] PierceShares {
            get {
                string t = pierceShares != null ? pierceShares.Value ?? "" : LinePierce.DefaultSharesText;
                if (t == sharesText) return shares;
                sharesText = t;
                if (!LinePierce.TryParseShares(t, out shares)) {
                    shares = (float[])LinePierce.DefaultShares.Clone();
                    MelonLogger.Warning($"[Archery] Q4: [Quickdraw4] PierceShares '{t}' is not a list of numbers; using {LinePierce.DefaultSharesText}");
                }
                return shares;
            }
        }

        static int PierceMax => pierceMax != null ? Math.Max(1, pierceMax.Value) : LinePierce.DefaultMax;

        /// <summary>Best knowledge this client has of whether the local player's next quickshot is a line shot.
        /// Once the server has sent this client its own build, the build decides (so a test build without Quickdraw 4 is never
        /// overruled by the session's old latch); before that, true from the first line shot the server confirms.</summary>
        internal static bool LocalPlayerHasIt() =>
            Builds.LocalBuildKnown ? (Builds.LocalBuild().Ultimates & Ultimates.Quickdraw4) != 0 : localHasIt;

        static Transform Tip(Arrow arrow) => arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;

        // Where the line ends, and what it hit. Server and clients run this with the same things ignored
        // (the shooter, their bow and string, the arrow) so they agree.
        static bool Resolve(Vector3 origin, Vector3 direction, out Vector3 end, out RaycastHit hit, params Transform?[] ignore) {
            float reach = Hits.LoadedReach(origin, direction, maxReach.Value);
            bool struck = Hits.Cast(origin, direction, reach, out hit, ignore);
            end = struck ? hit.point : origin + direction.normalized * reach;
            return struck;
        }

        // ---------------------------------------------------------------- the pierce (batch 4b): every creature on the path

        /// <summary>One creature the line passes through: the hit to deal and draw at (its nearest collider that has a damage
        /// detector, else its nearest), the creature, that detector, its place in the line's order and its share.</summary>
        struct Pass { public RaycastHit hit; public HealthObject h; public ImpactDetector? det; public int order; public float share; }

        static readonly List<RaycastHit> lineHits = new List<RaycastHit>();
        static readonly List<LineCandidate> lineCands = new List<LineCandidate>();
        static readonly List<HealthObject?> lineHealth = new List<HealthObject?>();
        static readonly List<ImpactDetector?> lineDets = new List<ImpactDetector?>();
        static readonly List<Pass> serverPasses = new List<Pass>(), clientPasses = new List<Pass>();

        /// <summary>What one collider on the line is: a creature (named by its prefab root, so its several colliders count once; a
        /// part with no detector of its own counts as its creature), a creature the line passes over (fix 5: one this arrow cannot
        /// damage at all, M-3; on the server one already dead, M-1), a player, or anything else (a surface: it stops the line).
        /// <paramref name="tool"/> null skips the damage test.</summary>
        static LineHitKind Classify(RaycastHit rh, ImpactTool? tool, bool server, out uint id, out HealthObject? health, out ImpactDetector? det) {
            id = 0u; health = null;
            det = rh.collider.GetComponentInParent<ImpactDetector>();
            HealthObject? h = det != null ? det.HealthTarget : null;
            if (h == null) {
                NetworkEntity ne = rh.collider.GetComponentInParent<NetworkEntity>();
                NetworkEntity root = ne != null ? ne.PrefabRoot : null!;
                if (root != null) h = root.GetComponentInChildren<HealthObject>(true);
                // only the creature's own root (b4b-review M-4): a networked container holding creatures is not one of them
                if (h != null && (h.Entity == null || h.Entity.PrefabRoot != root)) h = null;
                if (h != null && !(h is PlayerHealth) && !h.HasCrippleStat && !Targets.IsDummy(h)) h = null; // not a creature's part (nor a combat dummy, G3): a plain surface
                det = null;
            }
            if (h is PlayerHealth || rh.collider.GetComponentInParent<PlayerController>() != null) { health = h; return LineHitKind.Player; }
            if (!Targets.IsCreature(h)) { health = h; return LineHitKind.Solid; }
            health = h;
            id = Targets.IdOf(h!);
            if (id == 0u) id = (uint)h!.GetInstanceID();
            if (server && !(h!.Health > 0f)) return LineHitKind.PassedOver;   // a corpse (server only: clients still flash it)
            if (tool != null && !Hits.CanDamage(tool, h!)) return LineHitKind.PassedOver;
            return LineHitKind.Creature;
        }

        /// <summary>The line's path, the same on the server and every client from the same ray (LinePierce.Walk, tested): the creatures
        /// it passes through into <paramref name="passes"/>, and true with <paramref name="endHit"/> when something stops it (a
        /// surface, a player, or the PierceMax-th creature); otherwise it ends at its loaded reach.</summary>
        static readonly int T_Trace = Timing.Section("Quickdraw4.Trace (line cast)");
        static bool Trace(Vector3 origin, Vector3 direction, ImpactTool? tool, bool server, List<Pass> passes, out Vector3 end, out RaycastHit endHit, params Transform?[] ignore) {
            using var _t = Timing.Begin(T_Trace);
            passes.Clear();
            float reach = Hits.LoadedReach(origin, direction, maxReach.Value);
            Hits.CastAll(origin, direction, reach, lineHits, ignore);
            lineCands.Clear(); lineHealth.Clear(); lineDets.Clear();
            // lineHits is nearest first: classification stops at the first surface or player, which ends the line (b4b-review M-2)
            foreach (RaycastHit rh in lineHits) {
                LineHitKind kind = Classify(rh, tool, server, out uint id, out HealthObject? h, out ImpactDetector? det);
                lineCands.Add(new LineCandidate(id, rh.distance, kind));
                lineHealth.Add(h);
                lineDets.Add(det);
                if (kind == LineHitKind.Solid || kind == LineHitKind.Player) break;
            }
            List<LineStrike> strikes = LinePierce.Walk(lineCands, PierceMax, PierceShares, out int stop);
            foreach (LineStrike st in strikes) {
                int use = st.Index;
                if (lineDets[use] == null) // the nearest part with a detector of the same creature (lineHits is nearest first)
                    for (int k = 0; k < lineCands.Count; k++)
                        if (lineCands[k].Kind == LineHitKind.Creature && lineCands[k].Id == lineCands[st.Index].Id && lineDets[k] != null) { use = k; break; }
                passes.Add(new Pass { hit = lineHits[use], h = lineHealth[st.Index]!, det = lineDets[use], order = st.Order, share = st.Share });
            }
            if (stop >= 0) { endHit = lineHits[stop]; end = endHit.point; return true; }
            endHit = default;
            end = origin + direction.normalized * reach;
            return false;
        }

        /// <summary>Server: one pierced creature's hit, through Hits.Apply (players never, channel and hardness, cooldowns, Hits.Dealing).</summary>
        static readonly int T_Deal = Timing.Section("hit Quickdraw4.Deal");
        static bool Deal(ImpactTool tool, Pass p, float multiplier, out string what) {
            using var _t = Timing.Begin(T_Deal);
            what = p.h != null ? p.h.name : "nothing";
            if (p.det != null && Hits.Apply(tool, p.det, p.hit.point, p.hit.normal, multiplier, out what)) return true;
            if (p.h == null) return false;
            // the part it met refused (or had no detector): the creature's other detectors (b4b-review M-3; Trace counts only a
            // creature one of them accepts)
            foreach (object o in p.h.ImpactDetectors)
                if (o is ImpactDetector d && d != null && d != p.det && Hits.Apply(tool, d, p.hit.point, p.hit.normal, multiplier, out what)) return true;
            return false;
        }

        // Put the arrow so its tip is at `point`, pointing along `direction`, with no speed.
        static readonly int T_Place = Timing.Section("Quickdraw4.Place (embed)");
        static void Place(Arrow arrow, Vector3 point, Vector3 direction) {
            using var _t = Timing.Begin(T_Place);
            arrow.StopUpdatingArrowFlight();
            Rigidbody rb = arrow.Rigidbody;
            if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            arrow.transform.rotation = Quaternion.LookRotation(direction);
            Vector3 toTip = Tip(arrow).position - arrow.transform.position;
            Vector3 pos = point - toTip;
            arrow.transform.position = pos;
            if (rb != null) rb.position = pos;
        }

        // ---------------------------------------------------------------- server

        // BowDrawSkill.OnArrowShot only runs where skills are judged (the server), and only matters when charged.
        // The line fires when this arrow's release (Draws.Release, decided earlier in the same OnShootingEntity) said Line.
        [HarmonyPatch(typeof(BowDrawSkill), "OnArrowShot")]
        static class ServerShot {
            static readonly int TPrefix = Timing.Section("patch BowDrawSkill.OnArrowShot / Quickdraw4.ServerShot prefix");
            static readonly int TPostfix = Timing.Section("patch BowDrawSkill.OnArrowShot / Quickdraw4.ServerShot postfix");
            static void Prefix(BowDrawSkill __instance, BowString bowString, NetworkEntity shotEntity, Dictionary<BowString, ISkillInputMethod> ___inputs, out Player? __state) {
                using var _t = Timing.Begin(TPrefix);
                __state = null;
                if (!(__instance is BowQuickdraw) || bowString == null || shotEntity == null) return;
                DrawRecord rec = Draws.Get(bowString);
                if (rec.LastArrow != shotEntity.Identifier || rec.Last?.Ultimate != UltimateUse.Line) return;
                Player? player = null;
                if (___inputs.TryGetValue(bowString, out ISkillInputMethod input) && input != null) player = input.Handler?.PlayerHelper?.Player;
                if (player == null) {
                    PlayerController pc = bowString.Pickup != null ? bowString.Pickup.LastPlayerInteractor : null!;
                    if (pc != null) player = pc.NetworkPlayer;
                }
                __state = player;
            }

            static void Postfix(BowString bowString, NetworkEntity shotEntity, Player? __state) {
                using var _t = Timing.Begin(TPostfix);
                if (__state == null || shotEntity == null || shotEntity.IsBeingDestroyed) return;
                Fire(bowString, shotEntity, __state);
            }
        }

        // server: arrows whose line was fired by the game's own Quickdraw path (ServerShot) in the shot now being made. The mark
        // lives from ServerShot to this shot's OnShoot postfix (FromRelease removes it), so a re-shot arrow is never skipped.
        static readonly HashSet<uint> fired = new HashSet<uint>();

        // A release the server resolved as a Line while the game's Quickdraw input was not active (a snap the server accepted
        // from the shooter's trusted claim after its own input had lapsed) never reaches BowDrawSkill.OnArrowShot. OnShoot's
        // postfix runs after every OnShootingEntity handler: such a Line is fired here (b2-review I-3: it flew at x1 in 0.13.0).
        [HarmonyPatch(typeof(BowString), "OnShoot")]
        static class FromRelease {
            static readonly int T = Timing.Section("patch BowString.OnShoot / Quickdraw4.FromRelease");
            [HarmonyPriority(Priority.High)]
            static void Postfix(BowString __instance, Pickup __1) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || __1 == null) return;
                    NetworkEntity entity = __1.Entity;
                    if (entity == null) return;
                    if (fired.Remove(entity.Identifier) || entity.IsBeingDestroyed) return; // fired by the game's path for this shot
                    DrawRecord rec = Draws.Get(__instance);
                    if (rec.LastArrow != entity.Identifier || rec.Last?.Ultimate != UltimateUse.Line) return;
                    PlayerController pc = __instance.Pickup != null ? __instance.Pickup.LastPlayerInteractor : null!;
                    if (pc == null) pc = __1.LastPlayerInteractor;
                    Player? player = pc != null ? pc.NetworkPlayer : null;
                    if (player == null) return;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Q4: arrow {entity.Identifier} line fired from the release (the game's Quickdraw was not active; the server resolved a Line from the shooter's claim)");
                    Fire(__instance, entity, player);
                    fired.Remove(entity.Identifier); // the mark is per shot
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Q4: line from the release failed: {e}"); }
            }
        }

        static readonly int T_Fire = Timing.Section("Quickdraw4.Fire (server line)");
        static void Fire(BowString bowString, NetworkEntity shotEntity, Player __state) {
            using var _t = Timing.Begin(T_Fire);
                if (fired.Count > 64) fired.Clear();
                fired.Add(shotEntity.Identifier);
                try {
                    Arrow arrow = shotEntity.GetComponent<Arrow>();
                    if (arrow == null) return;
                    Vector3 origin = Tip(arrow).position, direction = arrow.transform.forward;
                    Transform? shooter = __state.PlayerController != null ? __state.PlayerController.transform : null;
                    Transform? bow = bowString.Bow != null ? bowString.Bow.transform : null;

                    // batch 4b: the line passes through every creature on its path and stops at the first surface (LinePierce)
                    List<Pass> passes = serverPasses;
                    bool struck = Trace(origin, direction, arrow.ImpactTool, true, passes, out Vector3 end, out RaycastHit hit, shooter, bow, arrow.transform, bowString.transform);

                    arrow.StartHandlingArrowDeviance(Marker);       // tells every client; restarts flight, which Place stops again
                    Place(arrow, struck ? end + direction * 0.03f : end, direction);

                    string what = "nothing";
                    ImpactTool tool = arrow.ImpactTool;
                    // Needles first (they never move or embed the arrow): one ray each at NeedleShare, no Line bonus, no weight.
                    int needles = Math.Min(Draws.ReleaseOf(shotEntity.Identifier)?.Plan.Echoes ?? 0, ReleasePlan.MaxNeedles);
                    if (needles > 0) {
                        int hits = 0;
                        float share = Paths.Values.NeedleShare;
                        var names = new List<string>();
                        for (int i = 0; i < needles; i++) {
                            Vector3 d = NeedleDirection(direction, i, needles);
                            if (!Resolve(origin, d, out _, out RaycastHit nh, shooter, bow, arrow.transform, bowString.transform)) continue;
                            if (tool != null && Hits.Apply(tool, nh, share, out string nwhat)) {
                                hits++; names.Add(nwhat);
                                // fix batch D: a needle line's creature is damaged by this release, and with Ricochet it jumps too
                                ImpactDetector nd = nh.collider != null ? nh.collider.GetComponentInParent<ImpactDetector>() : null!;
                                HealthObject nhealth = nd != null ? nd.HealthTarget : null!;
                                if (nhealth != null) {
                                    Targets.Damaged(shotEntity.Identifier, nhealth);
                                    Ricochet.ServerLineHit(shotEntity.Identifier, nhealth, tool, share, true);
                                }
                            }
                        }
                        if (Logs.Debug) MelonLogger.Msg(System.FormattableString.Invariant(
                            $"[Archery] Line: arrow {shotEntity.Identifier} {needles} needle line(s), {hits} hit(s) x{share:0.00}{(names.Count > 0 ? " on " + string.Join(", ", names.ToArray()) : "")}"));
                    }
                    System.Text.StringBuilder? pierced = Logs.Debug ? new System.Text.StringBuilder() : null; // G2: log text, Debug only
                    if (tool != null) {
                        bool firstDealt = false; Pass first = default; float firstMultiplier = 0f, firstDamage = float.NaN;
                        foreach (Pass p in passes) {
                            float multiplier = damage.Value * p.share;
                            bool dealtPass = Deal(tool, p, multiplier, out string w);
                            if (dealtPass) Targets.Damaged(shotEntity.Identifier, p.h); // fix batch D: an off-path Shatter skips them
                            if (p.order == 0) {
                                what = w;
                                if (dealtPass) {
                                    firstDealt = true; first = p; firstMultiplier = multiplier; firstDamage = Hits.LastApplyDamage;
                                    Weight.OnLineHit(shotEntity.Identifier, p.h, __state); // the snap's weight (a Quick main's Charge Shot orbs)
                                }
                                continue;
                            }
                            // a later creature: its share of the damage, its share of the snap's stagger, never an interrupt
                            if (dealtPass) Weight.OnLineHit(shotEntity.Identifier, p.h, __state, p.share, false);
                            pierced?.Append(System.FormattableString.Invariant($"{(pierced.Length > 0 ? ", " : "")}'{w}' x{multiplier:0.##}{(dealtPass ? "" : " (not dealt)")}"));
                        }
                        // fix batch D (off the main path): the line's first creature is the release's hit for Ricochet (one jump) and
                        // Shatter (a blast along the line that skips every creature the line pierced); nothing without the orbs
                        if (firstDealt && first.h != null) {
                            Ricochet.ServerLineHit(shotEntity.Identifier, first.h, tool, firstMultiplier);
                            Shatter.ServerLineHit(shotEntity.Identifier, first.h, first.hit.point, direction, tool, firstDamage, __state.PlayerController);
                        }
                        // play-test 7 (PvP ruling): a line that stops at a player after passing creatures deals that player its place's
                        // share (Hits.Apply: only with the server's PvP on, at [PvP] ModHitShare, never the shooter); it still stops there
                        if (passes.Count > 0 && struck && hit.collider != null && hit.collider.GetComponentInParent<ImpactDetector>() is ImpactDetector pd
                            && pd != null && pd.HealthTarget is PlayerHealth) {
                            float share = LinePierce.Share(passes.Count, PierceShares);
                            bool dealtPlayer = Hits.Apply(tool, hit, damage.Value * share, out string pw);
                            pierced?.Append(System.FormattableString.Invariant($"{(pierced.Length > 0 ? ", " : "")}player '{pw}' x{damage.Value * share:0.##}{(dealtPlayer ? "" : " (not dealt)")}"));
                        }
                        if (passes.Count == 0 && struck) {
                            // no creature on the path: the first thing the line met takes the hit, as before
                            bool dealt = Hits.Apply(tool, hit, damage.Value, out what);   // a player only with PvP on (Pvp.cs)
                            if (dealt) {
                                ImpactDetector endDet = hit.collider != null ? hit.collider.GetComponentInParent<ImpactDetector>() : null!;
                                Weight.OnLineHit(shotEntity.Identifier, endDet != null ? endDet.HealthTarget : null, __state);
                            }
                        }
                        if (struck || passes.Count > 0) tool.OneOffMultiplier = 1f; // the normal Quickdraw bonus must not also land later
                    }
                    if (struck) {
                        Embeddable embed = shotEntity.GetComponent<Embeddable>();
                        if (embed != null && embedMethod != null)
                            embedMethod.Invoke(embed, new object?[] { hit.rigidbody, arrow.transform.position, arrow.transform.rotation });
                    }
                    shotEntity.ForcePositionSync();
                    if (pierced != null) {
                        string stopAt = !struck ? "its reach" : hit.collider != null ? $"'{hit.collider.name}'" : "?";
                        MelonLogger.Msg($"[Archery] Q4: line {(end - origin).magnitude:0.0}m, hit '{what}', damage x{damage.Value}" +
                                        (pierced.Length > 0 ? $"; pierced {passes.Count - 1} more: {pierced}" : "") + $"; ends at {stopAt}");
                    }
                }
                catch (Exception e) { MelonLogger.Error($"[Archery] Q4: server shot failed: {e}"); }
        }

        // ---------------------------------------------------------------- clients

        // Runs on every client for every bow shot: remember where each arrow started.
        [HarmonyPatch(typeof(Shooter), nameof(Shooter.Shoot))]
        static class ClientShot {
            static readonly int T = Timing.Section("patch Shooter.Shoot / Quickdraw4.ClientShot");
            static void Postfix(Shooter __instance, Pickup pickup, bool isFromNetwork) {
                using var _t = Timing.Begin(T);
                try {
                    if (NetworkSceneManager.IsServer || pickup == null) return;
                    Arrow arrow = pickup.GetComponent<Arrow>();
                    if (arrow == null) return;
                    if (pickup.Entity != null) QuiverGather.NoteShot(pickup.Entity.Identifier);
                    // For another player's shot the server's "this was a line" can arrive before this relay of the
                    // shot itself, which has just put the arrow back on the bow and launched it. Undo that.
                    if (isFromNetwork && results.TryGetValue(arrow, out Result done) && Time.time - done.time < 1f) {
                        Settle(arrow, done);
                        // The relayed claim (read with this shot message) carries the needle count the line could not know yet.
                        if (!done.needlesDrawn && DrawNeedles(arrow, ref done, false) > 0) results[arrow] = done;
                        return;
                    }
                    if (shots.Count > 64) { shots.Clear(); results.Clear(); }
                    results.Remove(arrow); // the same arrow can be recovered and shot again
                    BowString? bs = Volley.BowForShooter(__instance);
                    PlayerController who = pickup.LastPlayerInteractor;
                    shots[arrow] = new ShotInfo {
                        origin = Tip(arrow).position, direction = pickup.transform.forward, time = Time.time,
                        shooter = who != null ? who.transform : null,
                        bow = bs != null && bs.Bow != null ? bs.Bow.transform : null,
                        bowString = bs != null ? bs.transform : null,
                    };
                    if (isFromNetwork) return;
                    lastLocalArrow = arrow;
                    // Predict the local player's own shot so they never see the arrow fly.
                    // (Skyfall: a cast's arrow is held, never predicted as a line; the check runs only when the prediction would)
                    if (Main.QuickshotReady && LocalPlayerHasIt() && !(pickup.Entity != null && Skyfall.IsCast(pickup.Entity.Identifier))) ResolveOnClient(arrow, true);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Q4: client shot failed: {e.Message}"); }
            }
        }

        // The server's flight sync. A speed at the marker value is not a flight at all.
        [HarmonyPatch(typeof(Arrow), nameof(Arrow.StartHandlingArrowDeviance))]
        static class Detect {
            static readonly int T = Timing.Section("patch Arrow.StartHandlingArrowDeviance / Quickdraw4.Detect");
            static bool Prefix(Arrow __instance, float speed, int randomSeed) {
                using var _t = Timing.Begin(T);
                if (NetworkSceneManager.IsServer || speed < Marker - 1f) return true;
                try {
                    bool mine = ReferenceEquals(__instance, lastLocalArrow);
                    if (mine && !localHasIt) {
                        localHasIt = true;
                        if (Logs.Summary) MelonLogger.Msg("[Archery] Q4: the server confirmed a line shot; later ones are predicted locally.");
                    }
                    ResolveOnClient(__instance, mine);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Q4: line draw failed: {e.Message}"); }
                return false; // never start a flight for this arrow
            }
        }

        static readonly int T_ResolveOnClient = Timing.Section("Quickdraw4.ResolveOnClient");
        static void ResolveOnClient(Arrow arrow, bool local) {
            using var _t = Timing.Begin(T_ResolveOnClient);
            if (arrow == null) return;
            // Already drawn for this shot (predicted locally, now confirmed). The game's ordinary flight message
            // may have set the arrow flying again in between, so put it back.
            if (results.TryGetValue(arrow, out Result done) && Time.time - done.time < 2f) {
                Settle(arrow, done);
                if (!done.needlesDrawn && DrawNeedles(arrow, ref done, local) > 0) results[arrow] = done;
                return;
            }
            Vector3 origin, direction;
            Transform? shooter = local && PlayerController.Current != null ? PlayerController.Current.transform : null, bow = null, bowString = null;
            if (shots.TryGetValue(arrow, out ShotInfo info) && Time.time - info.time < 2f) {
                origin = info.origin; direction = info.direction;
                if (info.shooter != null) shooter = info.shooter;
                bow = info.bow; bowString = info.bowString;
            }
            else { origin = Tip(arrow).position; direction = arrow.transform.forward; }

            // batch 4b: the same path as the server's from this machine's view (the shooter's own ray, a spectator's from the relayed
            // shot or the arrow), drawn to its real end with a small hit at every creature it passed through. Looks only.
            List<Pass> passes = clientPasses;
            bool struck = Trace(origin, direction, arrow.ImpactTool, false, passes, out Vector3 end, out RaycastHit hit, shooter, bow, bowString, arrow.transform);
            var result = new Result {
                origin = origin, end = struck ? end + direction * 0.03f : end, direction = direction, normal = struck ? hit.normal : -direction,
                struck = struck, time = Time.time, shooter = shooter, bow = bow, bowString = bowString,
            };
            Settle(arrow, result);
            // what the line's own hit was: its first creature, else the surface it stopped at
            bool touched = passes.Count > 0 || struck;
            RaycastHit first = passes.Count > 0 ? passes[0].hit : hit;
            // A Quick main's Charge Shot orbs: the line's last third in the charge's orange, the hit look on a creature (looks only).
            SnapWeight.LineWeight weight = ApplicationManager.IsHeadless ? default : SnapWeight.Line(arrow, local, touched, first);
            // fix batch D (off the main path, looks only): the line's first creature shows Ricochet's arc and Shatter's blast, as a
            // real arrow's first creature hit does (nothing without the orbs or without this release's record on this machine)
            if (passes.Count > 0 && passes[0].h != null && arrow.Entity != null && !ApplicationManager.IsHeadless) {
                uint lineId = arrow.Entity.Identifier;
                try {
                    Targets.RealHit(lineId, passes[0].h);
                    Ricochet.ClientStruck(lineId, passes[0].h, true, passes[0].hit.point);
                    Shatter.ClientRealHit(lineId, arrow, passes[0].det, passes[0].h, passes[0].hit.point, direction, null);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Q4: the line's side-orb looks failed: {e.Message}"); }
            }
            if (!ApplicationManager.IsHeadless) {
                Line.Draw(origin, end, struck, result.normal, false, weight.Tint, weight.Orbs, weight.WidthPerOrb);
                foreach (Pass p in passes) {
                    if (!struck || p.hit.point != hit.point) Line.PassedThrough(p.hit.point, -direction);
                    // G4: a weighted line shows its weight at each creature it passes through after the first (looks only)
                    if (p.order > 0 && weight.Orbs > 0) SnapWeight.Passed(weight, arrow.Entity != null ? arrow.Entity.Identifier : 0u, p.hit, p.share);
                }
            }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Q4: line drawn (local={local}){(passes.Count > 1 ? $", through {passes.Count} creatures" : "")}");
            DrawNeedles(arrow, ref result, local);
            results[arrow] = result;
        }

        /// <summary>Needle i of n: <see cref="Volley.NeedleDegrees"/> off the line, turned i x 360 / n round it. The basis comes
        /// from the direction alone, so the server and every client turn the same way.</summary>
        static Vector3 NeedleDirection(Vector3 direction, int index, int count) {
            Vector3 d = direction.normalized;
            Fx.Basis(d, out Vector3 u, out Vector3 w);
            float a = Volley.NeedleDegrees * Mathf.Deg2Rad, phi = NeedlePattern.Azimuth(index, count) * Mathf.Deg2Rad;
            return (Mathf.Cos(a) * d + Mathf.Sin(a) * (Mathf.Cos(phi) * u + Mathf.Sin(phi) * w)).normalized;
        }

        /// <summary>Clients: how many needles this line shot carries. The shooter: its own release (or its build, if the line is
        /// predicted before the release is decided); a spectator: the server's relayed claim (a Line is relayed as Snap, its
        /// stage the needle count), or 0 while it has not arrived.</summary>
        static int NeedlesSeen(Arrow arrow, bool local) {
            uint id = arrow.Entity != null ? arrow.Entity.Identifier : 0u;
            if (!local && id != 0u && Claims.TryRelay(id, out ShotClaim? c) && c != null)
                return c.Kind == ShotKind.Snap ? ReleasePlan.From(ShotKind.Snap, c.Stage).Echoes : 0;
            Resolution? r = id != 0u ? Draws.ReleaseOf(id) : null;
            if (r.HasValue) return r.Value.Ultimate == UltimateUse.Line ? Math.Min(r.Value.Plan.Echoes, ReleasePlan.MaxNeedles) : 0;
            return local ? Math.Min(Complements.Needles(Builds.LocalBuild(), Paths.Values), ReleasePlan.MaxNeedles) : 0;
        }

        /// <summary>Clients: cast and draw this line shot's needles once (looks only; the server deals their damage).</summary>
        static int DrawNeedles(Arrow arrow, ref Result r, bool local) {
            if (r.needlesDrawn) return 0;
            int n = NeedlesSeen(arrow, local);
            if (n <= 0) return 0;
            r.needlesDrawn = true;
            int hits = 0;
            for (int i = 0; i < n; i++) {
                Vector3 d = NeedleDirection(r.direction, i, n);
                bool struck = Resolve(r.origin, d, out Vector3 end, out RaycastHit hit, r.shooter, r.bow, r.bowString, arrow.transform);
                if (struck) hits++;
                if (!ApplicationManager.IsHeadless) Line.Draw(r.origin, end, struck, struck ? hit.normal : -d, true);
            }
            uint id = arrow.Entity != null ? arrow.Entity.Identifier : 0u;
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Line: arrow {id} {n} needle line(s), {hits} hit(s) drawn (local={local})");
            return n;
        }

        static readonly int T_Settle = Timing.Section("Quickdraw4.Settle");
        static void Settle(Arrow arrow, Result r) {
            using var _t = Timing.Begin(T_Settle);
            Place(arrow, r.end, r.direction); // also stops any flight the game started
            if (r.struck && arrow.Rigidbody != null) arrow.Rigidbody.isKinematic = true; // hold still until the server's embed arrives
        }

        // ---------------------------------------------------------------- the look

        // A ruler-straight centre line with spirals winding round it like a column. It is all there at once, holds
        // for a blink, then the spirals turn and open outward as everything fades. Where it lands: a tight, sharp
        // burst of straight spikes and a snap of light. Nothing spreads along the ground and nothing lingers.
        class Line : MonoBehaviour {
            LineRenderer core = null!;
            LineRenderer[] strands = new LineRenderer[0];
            Vector3[] buffer = new Vector3[0];
            Vector3 from, dir, u, w;
            float born, length, spiralLength, weight;
            Color c;
            const int WeightPoints = 13; // a line's colours and width are set at its points: enough of them for the last third to show
            // Reused by every weighted line, every frame (the main thread only): a LineRenderer copies its gradient when set.
            static readonly Gradient tintGradient = new Gradient();
            static readonly GradientColorKey[] tintColours = new GradientColorKey[4];
            static readonly GradientAlphaKey[] tintAlphas = new GradientAlphaKey[2];
            const float TintRise = 0.08f; // share of the line over which the orange comes in, from the start of the last third

            /// <summary><paramref name="needle"/>: one of a Quick main's needles, a thinner bare line with a small flash where it lands.
            /// <paramref name="weight"/> (0..1, the centre line only): a Quick main's Charge Shot orbs turn the core to the
            /// charge's orange over its last third (SnapWeight.Line); 0 = the plain line. G4: with <paramref name="orbs"/> of
            /// them the core is also wider there, by <paramref name="widthPerOrb"/> each (SnapWeightLook.LineWidth).</summary>
            static readonly int T_Draw = Timing.Section("look start Quickdraw4.Draw (line)");
            internal static void Draw(Vector3 from, Vector3 to, bool struck, Vector3 normal, bool needle, float weight = 0f, int orbs = 0, float widthPerOrb = 0f) {
                using var _t = Timing.Begin(T_Draw);
                if (Fx.LineMaterial() == null) return;
                var go = new GameObject(needle ? "Quickdraw4Needle" : "Quickdraw4Line");
                Line l = go.AddComponent<Line>();
                l.c = needle ? Volley.NeedleLook.Colour : Colour; l.from = from; l.born = Time.time; // needles: [Needles] Colour
                l.weight = needle ? 0f : Mathf.Clamp01(weight);
                Vector3 d = to - from;
                l.length = d.magnitude;
                l.dir = l.length > 1e-4f ? d / l.length : Vector3.forward;
                Fx.Basis(l.dir, out l.u, out l.w);
                l.core = Fx.NewLine(go, 2, needle ? needleWidth.Value : lineWidth.Value, false);
                l.core.SetPosition(0, from);
                l.core.SetPosition(1, to);
                if (l.weight > 0f || (!needle && orbs > 0 && widthPerOrb > 0f)) {
                    // G4: the weighted core is drawn with evenly spaced points, so its gradient (the orange last third) and its
                    // width (wider there per Charge Shot orb) show where they should; a two-point line has only its two ends
                    try {
                        l.core.positionCount = WeightPoints;
                        for (int i = 0; i < WeightPoints; i++) l.core.SetPosition(i, Vector3.Lerp(from, to, i / (float)(WeightPoints - 1)));
                        if (orbs > 0 && widthPerOrb > 0f) {
                            var keys = new Keyframe[WeightPoints];
                            for (int i = 0; i < WeightPoints; i++) {
                                float u = i / (float)(WeightPoints - 1);
                                keys[i] = new Keyframe(u, SnapWeightLook.LineWidth(u, orbs, widthPerOrb), 0f, 0f);
                            }
                            l.core.widthCurve = new AnimationCurve(keys);
                        }
                    }
                    catch (Exception e) {
                        // the plain two-point core stands in (its tint then only blends from end to end)
                        l.core.positionCount = 2;
                        l.core.SetPosition(0, from);
                        l.core.SetPosition(1, to);
                        if (!weightFailed) { weightFailed = true; MelonLogger.Warning($"[Archery] Q4: the weighted line could not be built ({e.Message}); it is drawn as the plain line"); }
                    }
                }
                if (needle) {
                    l.Paint(0f);
                    if (struck && hitFlash.Value > 0f) Fx.Flash(to + normal * 0.05f, l.c, hitFlash.Value * needleFlash.Value, 1.5f, 0.1f);
                    return;
                }

                int count = Mathf.Clamp(spiralStrands.Value, 0, 4);
                l.spiralLength = Mathf.Min(l.length, Mathf.Max(0f, spiralMax.Value));
                if (count > 0 && spiralRadius.Value > 0f && l.spiralLength > 0.5f) {
                    float turn = Mathf.Max(0.2f, spiralTurn.Value);
                    int points = Mathf.Clamp(Mathf.CeilToInt(l.spiralLength / turn * 12f), 8, 480); // 12 per turn, capped for long shots
                    l.buffer = new Vector3[points];
                    l.strands = new LineRenderer[count];
                    for (int i = 0; i < count; i++) {
                        var strand = new GameObject("spiral");
                        strand.transform.SetParent(go.transform, false);
                        l.strands[i] = Fx.NewLine(strand, points, spiralWidth.Value, false);
                    }
                }
                l.Paint(0f);
                if (struck) Strike(to, normal, l.c);
                else if (hitFlash.Value > 0f) Fx.Flash(to, l.c, hitFlash.Value * 0.5f, 3f, 0.15f);
            }

            static bool passFailed, weightFailed;

            /// <summary>Batch 4b: where the line passed through a creature: a smaller snap of light and a few short spikes back
            /// along the line. A failure logs once and shows nothing more.</summary>
            internal static void PassedThrough(Vector3 point, Vector3 back) {
                if (passFailed) return;
                try {
                    Color c = Colour;
                    if (hitFlash.Value > 0f && pierceFlash.Value > 0f) Fx.Flash(point, c, hitFlash.Value * pierceFlash.Value, 1.5f, 0.1f);
                    int n = Mathf.Max(0, pierceSpikes.Value);
                    float life = Mathf.Max(0.03f, spikeSeconds.Value);
                    for (int i = 0; i < n; i++) {
                        Vector3 dir = (back + UnityEngine.Random.insideUnitSphere * spikeSpread.Value).normalized;
                        float len = spikeLength.Value * 0.5f * UnityEngine.Random.Range(0.35f, 1f);
                        Fx.Bolt(point, point + dir * len, 0f, life * UnityEngine.Random.Range(0.6f, 1f), 0.012f, Color.white, c, 0);
                    }
                }
                catch (Exception e) {
                    passFailed = true;
                    MelonLogger.Warning($"[Archery] Q4: the pierce look could not be built; passes show nothing more ({e.Message})");
                }
            }

            // Pinpoint and aggressive: everything starts at the exact hit point and snaps straight back out of it.
            static void Strike(Vector3 point, Vector3 normal, Color c) {
                if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
                if (hitFlash.Value > 0f) Fx.Flash(point + normal * 0.05f, c, hitFlash.Value, 2.5f, 0.12f);
                int n = Mathf.Max(0, spikes.Value);
                float life = Mathf.Max(0.03f, spikeSeconds.Value);
                for (int i = 0; i < n; i++) {
                    Vector3 dir = (normal + UnityEngine.Random.insideUnitSphere * spikeSpread.Value).normalized;
                    if (Vector3.Dot(dir, normal) < 0.05f) dir = Vector3.Reflect(dir, normal); // never into the surface
                    float len = spikeLength.Value * UnityEngine.Random.Range(0.35f, 1f);
                    Fx.Bolt(point, point + dir * len, 0f, life * UnityEngine.Random.Range(0.6f, 1f), 0.018f, Color.white, c, 0);
                }
                Fx.Bolt(point, point + normal * spikeLength.Value * 1.4f, 0f, life, 0.03f, Color.white, c, 0); // the long one, straight out
            }

            void Paint(float t) {
                float fade = Mathf.Max(0.01f, lineFade.Value);
                float k = Mathf.Clamp01((t - lineHold.Value) / fade); // 0 while held, 1 when gone
                float a = 1f - k;
                if (weight > 0f) {
                    // the plain line runs white to its colour; from the last third on it turns to the charge's orange, then
                    // a little toward its gold at the very end (the Fill's own back-to-front blend)
                    float from = SnapWeightLook.LineTintFrom, rise = Mathf.Min(1f, from + TintRise);
                    Color orange = ChargeFx.BackColour, gold = Color.Lerp(orange, ChargeFx.FrontColour, 0.5f);
                    tintColours[0] = new GradientColorKey(Color.white, 0f);
                    tintColours[1] = new GradientColorKey(Color.Lerp(Color.white, c, from), from);
                    tintColours[2] = new GradientColorKey(Color.Lerp(Color.Lerp(Color.white, c, rise), orange, weight), rise);
                    tintColours[3] = new GradientColorKey(Color.Lerp(c, gold, weight), 1f);
                    tintAlphas[0] = new GradientAlphaKey(a, 0f);
                    tintAlphas[1] = new GradientAlphaKey(a, 1f);
                    tintGradient.SetKeys(tintColours, tintAlphas);
                    core.colorGradient = tintGradient;
                }
                else {
                    core.startColor = new Color(1f, 1f, 1f, a);
                    core.endColor = new Color(c.r, c.g, c.b, a);
                }
                if (strands.Length == 0) return;

                float turn = Mathf.Max(0.2f, spiralTurn.Value);
                float radius = spiralRadius.Value * (1f + spiralFlare.Value * k);
                float spin = t * spiralSpin.Value * Mathf.PI * 2f;
                int points = buffer.Length;
                for (int s = 0; s < strands.Length; s++) {
                    float offset = s * Mathf.PI * 2f / strands.Length;
                    for (int i = 0; i < points; i++) {
                        float d = spiralLength * i / (points - 1);
                        float ang = d / turn * Mathf.PI * 2f + spin + offset;
                        float r = radius * Mathf.Clamp01(d / 0.8f); // grows out of the arrow tip instead of starting wide
                        buffer[i] = from + dir * d + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * r;
                    }
                    strands[s].SetPositions(buffer);
                    strands[s].startColor = new Color(c.r, c.g, c.b, a * 0.9f);
                    // if the spirals stop short of a very long shot, they fade out instead of ending abruptly
                    strands[s].endColor = new Color(c.r, c.g, c.b, spiralLength < length - 0.01f ? 0f : a * 0.9f);
                }
            }

            static readonly int TUpdate = Timing.Section("Update Quickdraw4.Line");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float t = Time.time - born;
                if (t >= lineHold.Value + Mathf.Max(0.01f, lineFade.Value)) { Destroy(gameObject); return; }
                Paint(t);
            }
        }
    }
}
