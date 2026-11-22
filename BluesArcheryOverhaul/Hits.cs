using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Chunks;
using Alta.Global;
using Alta.Impact;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Shots that never physically fly are resolved here: the Quickdraw 4 line and Holy Arrow's beam hit at once
    // (Apply); the volley's spectral arrows are batched per target per tick and paid experience per release (Spectral).
    internal static class Hits {
        static RaycastHit[] buffer = new RaycastHit[64];
        const int Mask = ~(1 << 2); // everything except Unity's "Ignore Raycast" layer
        // G2: a cached field accessor instead of FieldInfo.GetValue on every hand-made hit (null when the field is missing, as before).
#if QUEST
        // Quest: ImpactDetector.hitCooldowns is a proxy property
        static Il2CppSystem.Collections.Generic.Dictionary<ImpactTool, float>? Cooldowns(ImpactDetector detector) => detector.hitCooldowns;
#else
        static readonly AccessTools.FieldRef<ImpactDetector, Dictionary<ImpactTool, float>>? cooldownRef = CooldownRef();

        static AccessTools.FieldRef<ImpactDetector, Dictionary<ImpactTool, float>>? CooldownRef() {
            try { return AccessTools.Field(typeof(ImpactDetector), "hitCooldowns") != null ? AccessTools.FieldRefAccess<ImpactDetector, Dictionary<ImpactTool, float>>("hitCooldowns") : null; }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Hits: no access to ImpactDetector.hitCooldowns ({e.Message})"); return null; }
        }

        static Dictionary<ImpactTool, float>? Cooldowns(ImpactDetector detector) => cooldownRef != null ? cooldownRef(detector) : null;
#endif

        /// <summary>True while the mod deals a hit it built by hand (Apply, FlushSpectral, Copy). The real-arrow hook
        /// (Weight) ignores hits made while it is set: they share the real arrow's tool but are not its hit.</summary>
        internal static bool Dealing;

        static readonly Comparison<RaycastHit> ByDistance = (x, y) => x.distance.CompareTo(y.distance);

        /// <summary>Every solid thing along the ray (triggers and Ignore Raycast left out) that is not part of any transform in
        /// <paramref name="ignore"/>, nearest first, into <paramref name="into"/> (cleared first). Grows the shared buffer as
        /// <see cref="Cast"/> does, so a crowded ray never drops hits (Quickdraw 4's pierce, batch 4b).</summary>
        internal static void CastAll(Vector3 from, Vector3 direction, float maxDistance, List<RaycastHit> into, params Transform?[] ignore) =>
            CastAllCore(from, direction, maxDistance, into, null, null, null, ignore);

        /// <summary>As the params form, without an array per call (G2): up to three transforms to ignore.</summary>
        internal static void CastAll(Vector3 from, Vector3 direction, float maxDistance, List<RaycastHit> into, Transform? a = null, Transform? b = null, Transform? c = null) =>
            CastAllCore(from, direction, maxDistance, into, a, b, c, null);

        // True when the hit belongs to one of the ignored transforms (the three, then the array): the old loop over one array.
        static bool Ignored(Transform hit, Transform? a, Transform? b, Transform? c, Transform?[]? rest) {
            if (a != null && hit.IsChildOf(a)) return true;
            if (b != null && hit.IsChildOf(b)) return true;
            if (c != null && hit.IsChildOf(c)) return true;
            if (rest != null) foreach (Transform? t in rest) if (t != null && hit.IsChildOf(t)) return true;
            return false;
        }

        static readonly int T_CastAllCore = Timing.Section("Hits.CastAll (raycast all)");
        static void CastAllCore(Vector3 from, Vector3 direction, float maxDistance, List<RaycastHit> into, Transform? a, Transform? b, Transform? c, Transform?[]? rest) {
            using var _t = Timing.Begin(T_CastAllCore);
            into.Clear();
            if (maxDistance <= 0f || direction.sqrMagnitude < 1e-8f) return;
            int n;
#if QUEST
            while ((n = Il.RaycastNonAlloc(from, direction.normalized, buffer, maxDistance, Mask, QueryTriggerInteraction.Ignore)) == buffer.Length && buffer.Length < 2048)
#else
            while ((n = Physics.RaycastNonAlloc(from, direction.normalized, buffer, maxDistance, Mask, QueryTriggerInteraction.Ignore)) == buffer.Length && buffer.Length < 2048)
#endif
                buffer = new RaycastHit[buffer.Length * 2];
            for (int i = 0; i < n; i++) {
                RaycastHit h = buffer[i];
                if (h.collider == null) continue;
                if (!Ignored(h.transform, a, b, c, rest)) into.Add(h);
            }
            into.Sort(ByDistance);
        }

        /// <summary>Nearest solid thing along the ray that is not part of any transform in <paramref name="ignore"/>
        /// (the shooter, their bow, the arrow itself).</summary>
        internal static bool Cast(Vector3 from, Vector3 direction, float maxDistance, out RaycastHit hit, params Transform?[] ignore) =>
            CastCore(from, direction, maxDistance, out hit, null, null, null, ignore);

        /// <summary>As the params form, without an array per call (G2: every spectral arrow casts each physics tick): up to three
        /// transforms to ignore. Chosen over the params form for one to three transforms (a normal form beats an expanded one).</summary>
        internal static bool Cast(Vector3 from, Vector3 direction, float maxDistance, out RaycastHit hit, Transform? a = null, Transform? b = null, Transform? c = null) =>
            CastCore(from, direction, maxDistance, out hit, a, b, c, null);

        /// <summary>H4 (H23-review-opus minor 1): a homing arrow's cast passes through a creature's shells (the non-body colliders
        /// Seek.BodyOf leaves out: a Turabada's 'Block Players' pillar), but only those that belong to a creature (Seek.CreatureShell):
        /// a world collider on the same layer still stops it. H5: also past every collider under the transforms in
        /// <paramref name="past"/> (null entries are skipped): a chaining homing arrow's cast passes through the creatures it has
        /// already struck (Seek.StruckRoots), never a world collider.</summary>
        internal static bool CastPastShells(Vector3 from, Vector3 direction, float maxDistance, out RaycastHit hit, Transform? a, Transform? b, Transform? c, Transform?[]? past) =>
            CastCore(from, direction, maxDistance, out hit, a, b, c, past, true);

        static readonly int T_CastCore = Timing.Section("Hits.Cast (raycast)");
        static bool CastCore(Vector3 from, Vector3 direction, float maxDistance, out RaycastHit hit, Transform? a, Transform? b, Transform? c, Transform?[]? rest, bool pastShells = false) {
            using var _t = Timing.Begin(T_CastCore);
            hit = default;
            if (maxDistance <= 0f || direction.sqrMagnitude < 1e-8f) return false;
            // A full buffer means hits were dropped in no particular order, and the nearest may be among them: grow and ask again.
            int n;
#if QUEST
            while ((n = Il.RaycastNonAlloc(from, direction.normalized, buffer, maxDistance, Mask, QueryTriggerInteraction.Ignore)) == buffer.Length && buffer.Length < 2048)
#else
            while ((n = Physics.RaycastNonAlloc(from, direction.normalized, buffer, maxDistance, Mask, QueryTriggerInteraction.Ignore)) == buffer.Length && buffer.Length < 2048)
#endif
                buffer = new RaycastHit[buffer.Length * 2];
            float best = float.MaxValue;
            for (int i = 0; i < n; i++) {
                RaycastHit h = buffer[i];
                if (h.collider == null || h.distance >= best) continue;
                if (Ignored(h.transform, a, b, c, rest)) continue;
                if (pastShells && Seek.CreatureShell(h.collider)) continue;
                best = h.distance;
                hit = h;
            }
            return best < float.MaxValue;
        }

        /// <summary>H4 (H23-review-opus minor 3): the point's own chunk is loaded.</summary>
        internal static bool Loaded(Vector3 point) {
            try {
                LocationChunk chunk = LocationChunkHelper.GetBestContaining(point);
                return chunk != null && chunk.IsLoaded;
            }
            catch { return false; }
        }

        /// <summary>How far along the ray the world is still loaded, capped at maxDistance. Keeps a missed shot
        /// from landing somewhere that does not exist (and so could never be recovered).</summary>
        static readonly int T_LoadedReach = Timing.Section("Hits.LoadedReach");
        internal static float LoadedReach(Vector3 from, Vector3 direction, float maxDistance) {
            using var _t = Timing.Begin(T_LoadedReach);
            const float step = 10f;
            Vector3 dir = direction.normalized;
            float reach = 0f;
            try {
                for (float d = step; d <= maxDistance + 0.01f; d += step) {
                    LocationChunk chunk = LocationChunkHelper.GetBestContaining(from + dir * d);
                    if (chunk == null || !chunk.IsLoaded) break;
                    reach = d;
                }
            }
            catch (Exception e) {
                MelonLogger.Warning($"[Archery] Loaded-world check failed ({e.Message}); using {Mathf.Min(maxDistance, 50f)} m.");
                return Mathf.Min(maxDistance, 50f);
            }
            return Mathf.Clamp(reach, step, maxDistance);
        }

        /// <summary>Every damage tool that is part of this arrow: its own, and any on attached parts (the head).</summary>
        internal static ImpactTool[] Tools(ImpactTool tool) => tool.GetComponentsInChildren<ImpactTool>(true);

        /// <summary>G2: <see cref="Tools(ImpactTool)"/> into a list the caller owns (no array per call; the same components in the same
        /// order: Unity fills the list as it fills the array). Each caller keeps its own static list and nothing it calls while it walks
        /// the list fills that list again.</summary>
#if QUEST
        internal static List<ImpactTool> Tools(ImpactTool tool, List<ImpactTool> into) { into.Clear(); foreach (ImpactTool t in tool.GetComponentsInChildren<ImpactTool>(true)) into.Add(t); return into; }
#else
        internal static List<ImpactTool> Tools(ImpactTool tool, List<ImpactTool> into) { tool.GetComponentsInChildren(true, into); return into; }
#endif

        static readonly List<ImpactTool> powerTools = new List<ImpactTool>(), partTools = new List<ImpactTool>();

        /// <summary>The arrow's damage rating: the strongest hit any of its parts can deal.</summary>
        internal static float Power(ImpactTool? tool) {
            float best = 0f;
            if (tool != null) foreach (ImpactTool t in Tools(tool, powerTools)) best = Mathf.Max(best, t.MaxDamage);
            return best;
        }

        /// <summary>For the log: what damage parts an arrow has. Lets a "zero damage" report be explained from facts.</summary>
        internal static string Describe(ImpactTool? tool) {
            if (tool == null) return "no tool";
            var sb = new System.Text.StringBuilder();
            foreach (ImpactTool t in Tools(tool)) {
                int impactors = 0; float top = 0f, cripple = 0f;
#if QUEST
                foreach (var pair in t.ImpactorsPerCollider)
                    foreach (ImpactGenerator g in pair.Value) {
                        if (g == null) continue;
                        // the best part is the one with the strongest health damage; its cripple is the arrowhead's own
#else
                foreach (KeyValuePair<Collider, HashSet<ImpactGenerator>> pair in t.ImpactorsPerCollider)
                    foreach (ImpactGenerator g in pair.Value) {
                        if (g == null) continue;
                        // the best part is the one with the strongest health damage; its cripple is the arrowhead's own
#endif
                        if (impactors == 0 || g.HealthDamageModifier > top) { top = g.HealthDamageModifier; cripple = g.CrippleDamageModifier; }
                        impactors++;
                    }
                var names = new List<string>();
                foreach (ImpactChannel c in t.Channels) if (c != null) names.Add(c.name);
                sb.Append($"['{t.name}' damage {t.Damage:0.###} x best part {top:0.###} = {t.MaxDamage:0.###}, cripple {cripple:0.###}, {impactors} part(s), hits: {string.Join("/", names.ToArray())}] ");
            }
            return sb.ToString();
        }

        /// <summary>Server only. Deals the arrow's damage, scaled by <paramref name="multiplier"/>, to whatever was
        /// hit, the way the game delivers a hit a client reported: armour, hit reactions and experience behave
        /// as for a real arrow. Returns true if damage was dealt.
        ///
        /// The hit is built directly instead of through the tool's own "I hit something" event, because that event
        /// also stops the real arrow's flight, wears it, and makes a Holy Arrow beam explode.</summary>
        static readonly int T_Apply = Timing.Section("hit Hits.Apply");
        internal static bool Apply(ImpactTool tool, RaycastHit hit, float multiplier, out string what, bool players = true) {
            using var _t = Timing.Begin(T_Apply);
            what = hit.collider != null ? hit.collider.name : "nothing";
            if (tool == null || hit.collider == null) return false;
            ImpactDetector detector = hit.collider.GetComponentInParent<ImpactDetector>();
            if (detector == null) return false;
            return Apply(tool, detector, hit.point, hit.normal, multiplier, out what, 1f, players);
        }

        /// <summary>As <see cref="Apply(ImpactTool, RaycastHit, float, out string)"/>, on a known detector at a point (Ricochet's
        /// creature jump, Shatter's splinters, a pierced creature's part). <paramref name="cripple"/> scales the hit's own cripple
        /// (the game's CripplingMultiplier): 1 for the Quickdraw 4 line, as a real arrow; 0 for hits whose stagger, if any, is
        /// Weight's ledger's (Ricochet's ghost jump and Shatter's splinters, b3-review I-3).</summary>
        static readonly int T_Apply2 = Timing.Section("hit Hits.Apply #2");
        internal static bool Apply(ImpactTool tool, ImpactDetector detector, Vector3 point, Vector3 normal, float multiplier, out string what, float cripple = 1f, bool players = true) {
            using var _t = Timing.Begin(T_Apply2);
            what = detector != null ? detector.name : "nothing";
            if (tool == null || detector == null) return false;
            // Play-test 7 (PvP, Pvp.cs): a player takes [PvP] ModHitShare of it, only with the server's PvP on, never the shooter.
#if QUEST
            if (detector.HealthTarget.Is(out PlayerHealth ph)) {
#else
            if (detector.HealthTarget is PlayerHealth ph) {
#endif
                float pvp = players ? Pvp.PlayerShare(tool, ph) : 0f;
                if (!(pvp > 0f)) { what += players ? " (player: PvP is off for the mod's hits)" : " (player: not by this hit)"; return false; }
                multiplier *= pvp;
                what += $" (player, PvP x{pvp:0.##})";
            }

            if (!BestPart(tool, detector, out ImpactGenerator? best, out ImpactChannel? channel, out ImpactTool? bestTool) || best == null || bestTool == null) {
                what += " (not something this arrow can damage)"; return false;
            }

            // G3 (diagnosis 11): never a zero-length or not-a-number normal or point into the game's hit (Shatter's fallback normal is
            // -axis, which is never zero there; a broken point would put the hit nowhere)
            Sane(detector, ref point, ref normal);
#if QUEST
            Il2CppSystem.Collections.Generic.Dictionary<ImpactTool, float>? cooldowns = null;
#else
            Dictionary<ImpactTool, float>? cooldowns = null;
#endif
            try {
                // The game ignores a second hit from the same arrow on the same target for a short time. The real
                // arrow and its extras share one tool, so clear that before (so this lands) and after (so the real
                // arrow's own hit still lands).
                cooldowns = Cooldowns(detector);
                cooldowns?.Remove(bestTool);
                var dealt = new ImpactHitWithTool(bestTool, best, detector, bestTool.Damage * multiplier, 1f, channel, point, normal);
                if (cripple != 1f) dealt.CripplingMultiplier = Mathf.Max(0f, cripple);
                Dealing = true;
                dealt.Hit();
                LastApplyDamage = dealt.HealthDamage;
                what += $" ({dealt.HealthDamage:0.###} damage)";
                Watch(detector.HealthTarget, "hand-made hit");
                return true;
            }
            catch (Exception e) {
                // Read from the game's code, not proven in play: make any failure loud.
                MelonLogger.Error($"[Archery] Applying damage to '{detector.name}' failed: {e}");
                return false;
            }
            finally {
                Dealing = false;
                cooldowns?.Remove(bestTool);
            }
        }

        static void Sane(ImpactDetector detector, ref Vector3 point, ref Vector3 normal) {
            if (!HitWatchRule.Finite(point.x, point.y, point.z)) point = detector.transform.position;
            if (!HitWatchRule.Finite(normal.x, normal.y, normal.z) || normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
        }

        // ---------------------------------------------------------------- G3: the hit watch (diagnosis 11, [Log] Level Debug only)

        // NaN colliders on wild Goteras started 26 ms after a Shatter blast in play-test 8, mechanism unknown. At Debug each hand-made
        // hit on a creature (Apply: the line, a blast, a pierced creature; Copy: the real arrow's jump; FlushSpectral: ghosts, pellets,
        // needles, heavy arrows; Ricochet's batched jumps go through Apply) logs the creature's rigidbody position and velocity, and
        // HitWatchRule.LaterSeconds later logs them again if either is not finite or it moved more than HitWatchRule.MovedMetres.
        // At most WatchLines lines a session; nothing at all below Debug.
        sealed class Watched { public Rigidbody Body = null!; public string Name = ""; public Vector3 At; public float Due; }
        static readonly List<Watched> watched = new List<Watched>();
        const int WatchCap = 32, WatchLines = 200;
        static int watchLines;

        static void Watch(HealthObject? h, string what) {
#if QUEST
            if (!Logs.Debug || h == null || h.Is<PlayerHealth>() || watchLines >= WatchLines || !NetworkSceneManager.IsServer) return;
#else
            if (!Logs.Debug || h == null || h is PlayerHealth || watchLines >= WatchLines || !NetworkSceneManager.IsServer) return;
#endif
            try {
                Transform root = Targets.RootOf(h);
                Rigidbody rb = root.GetComponentInChildren<Rigidbody>();
                if (rb == null) return;
                Vector3 p = rb.position, v = rb.velocity;
                watchLines++;
                MelonLogger.Msg(Inv($"[Archery] Hits: watch {what} on '{root.name}': rigidbody at {Targets.P(p)} moving {Targets.P(v)}{(HitWatchRule.Finite(p.x, p.y, p.z) && HitWatchRule.Finite(v.x, v.y, v.z) ? "" : " NOT FINITE")}"));
                foreach (Watched w in watched) if (w.Body == rb) return; // already due for a re-check
                if (watched.Count >= WatchCap) return;
                watched.Add(new Watched { Body = rb, Name = root.name, At = p, Due = Time.time + HitWatchRule.LaterSeconds });
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Hits: watch failed: {e.Message}"); }
        }

        /// <summary>Server, every frame (Main.OnUpdate): the watched creatures whose re-check is due.</summary>
        internal static void WatchTick() {
            if (watched.Count == 0) return;
            float now = Time.time;
            for (int i = watched.Count - 1; i >= 0; i--) {
                Watched w = watched[i];
                if (w.Body == null) { watched.RemoveAt(i); continue; }
                if (now < w.Due) continue;
                watched.RemoveAt(i);
                Vector3 p = w.Body.position, v = w.Body.velocity;
                bool finite = HitWatchRule.Finite(p.x, p.y, p.z) && HitWatchRule.Finite(v.x, v.y, v.z);
                float moved = finite ? (p - w.At).magnitude : float.NaN;
                if (!HitWatchRule.ReportLater(finite, moved, HitWatchRule.MovedMetres) || watchLines >= WatchLines) continue;
                watchLines++;
                MelonLogger.Msg(Inv($"[Archery] Hits: watch '{w.Name}' {HitWatchRule.LaterSeconds:0.#} s after the hit: rigidbody at {Targets.P(p)} moving {Targets.P(v)}, {(finite ? Inv($"moved {moved:0.0} m") : "NOT FINITE")}"));
            }
        }

        /// <summary>Health damage the last successful <see cref="Apply(ImpactTool, ImpactDetector, Vector3, Vector3, float, out string, float, bool)"/> dealt (fix batch D:
        /// a Quickdraw 4 line's first creature, the hit that an off-path Shatter or Ricochet works from).</summary>
        internal static float LastApplyDamage;

        /// <summary>Health damage the last successful <see cref="Copy"/> dealt (for Ricochet's real-arrow jump log).</summary>
        internal static float LastCopyDamage;

        /// <summary>Server only. Ricochet's real-arrow jump: <paramref name="next"/> takes a copy of the real arrow's hit with the
        /// same base damage (the hit's Multipliers and Effectiveness; the tool's one-off multiplier is already spent by then),
        /// scaled by <paramref name="share"/>; its cripple scales the same way (a copy of the hit). Same filters as
        /// <see cref="Apply"/>: players skipped, the impactor's hardness and a shared channel. Dealt under <see cref="Dealing"/>,
        /// with the tool's cooldown on the target cleared before and after.</summary>
        static readonly int T_Copy = Timing.Section("hit Hits.Copy");
        internal static bool Copy(ImpactHitWithTool first, ImpactDetector next, Vector3 point, Vector3 normal, out string what, float share = 1f) {
            using var _t = Timing.Begin(T_Copy);
            LastCopyDamage = 0f;
            what = next != null ? $"on '{next.name}'" : "on nothing";
            if (first == null || next == null) return false;
            ImpactTool tool = first.Tool;
            ImpactGenerator impactor = first.Impactor;
            if (tool == null || impactor == null) { what += " (the first hit has no tool part)"; return false; }
            float pvp = 1f;
#if QUEST
            if (next.HealthTarget.Is(out PlayerHealth ph)) {   // play-test 7: a jump lands on a player only with PvP on (Pvp.cs)
#else
            if (next.HealthTarget is PlayerHealth ph) {   // play-test 7: a jump lands on a player only with PvP on (Pvp.cs)
#endif
                pvp = Pvp.PlayerShare(tool, ph);
                if (!(pvp > 0f)) { what += " (player: PvP is off for the mod's hits)"; return false; }
            }
            share *= pvp;
            if (next.HardnessLevel > impactor.HardnessLevel) { what += " (too hard for this arrow)"; return false; }
            ImpactChannel? channel = Shared(impactor.Channels, next.Channels);
            if (channel == null) { what += " (not something this arrow can damage)"; return false; }

            Sane(next, ref point, ref normal); // G3, as Apply
#if QUEST
            Il2CppSystem.Collections.Generic.Dictionary<ImpactTool, float>? cooldowns = null;
#else
            Dictionary<ImpactTool, float>? cooldowns = null;
#endif
            try {
                cooldowns = Cooldowns(next);
                cooldowns?.Remove(tool);
                var dealt = new ImpactHitWithTool(tool, impactor, next, share == 1f ? first.Multipliers : first.Multipliers * share, first.Effectiveness, channel, point, normal);
                Dealing = true;
                dealt.Hit();
                LastCopyDamage = dealt.HealthDamage;
                Watch(next.HealthTarget, "jump (copy of the real arrow's hit)");
                return true;
            }
            catch (Exception e) {
                MelonLogger.Error($"[Archery] Hits: copy of the real arrow's hit onto '{next.name}' failed: {e}");
                what += " (failed)";
                return false;
            }
            finally {
                Dealing = false;
                cooldowns?.Remove(tool);
            }
        }

        /// <summary>The arrow's strongest part that this target accepts (the head, not the shaft). The same two filters
        /// the game applies on a real collision; without them arrows would chop trees and light fuses.</summary>
        /// <summary>Whether this arrow can damage <paramref name="h"/> at all: one of its detectors accepts one of the arrow's parts
        /// (hardness and a shared channel, as <see cref="Apply(ImpactTool, ImpactDetector, Vector3, Vector3, float, out string, float)"/>
        /// checks). Reads data only, so clients can ask too (Quickdraw 4's line, b4b-review M-3).</summary>
        internal static bool CanDamage(ImpactTool tool, HealthObject h) {
#if QUEST
            if (tool == null || h == null || h.Is<PlayerHealth>()) return false;
#else
            if (tool == null || h == null || h is PlayerHealth) return false;
#endif
            foreach (object o in h.ImpactDetectors)
#if QUEST
                if ((o as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).Is(out ImpactDetector d) && BestPart(tool, d, out _, out _, out _)) return true;
#else
                if (o is ImpactDetector d && d != null && BestPart(tool, d, out _, out _, out _)) return true;
#endif
            return false;
        }

        static readonly int T_BestPart = Timing.Section("Hits.BestPart");
        static bool BestPart(ImpactTool tool, ImpactDetector detector, out ImpactGenerator? best, out ImpactChannel? channel, out ImpactTool? bestTool) {
            using var _t = Timing.Begin(T_BestPart);
            best = null; channel = null; bestTool = null; float bestPower = -1f;
            foreach (ImpactTool t in Tools(tool, partTools)) {
#if QUEST
                foreach (var pair in t.ImpactorsPerCollider) {
#else
                foreach (KeyValuePair<Collider, HashSet<ImpactGenerator>> pair in t.ImpactorsPerCollider) {
#endif
                    foreach (ImpactGenerator g in pair.Value) {
                        if (g == null) continue;
                        float power = t.Damage * g.HealthDamageModifier;
                        if (power <= bestPower || detector.HardnessLevel > g.HardnessLevel) continue;
                        ImpactChannel? shared = Shared(g.Channels, detector.Channels);
                        if (shared == null) continue;
                        best = g; channel = shared; bestTool = t; bestPower = power;
                    }
                }
            }
            return best != null && bestTool != null;
        }

        /// <summary>H4 (H23-review M-4 second half): a hit of this arrow on this detector would deal damage: the filters every hand-made and
        /// spectral hit passes (a part of the arrow that shares a channel with the target and is hard enough, BestPart) and the immunity
        /// a batched spectral hit is scaled by (FlushSpectral skips it at 0). Players are not asked here (callers pass creatures).</summary>
        internal static bool CouldDamage(ImpactTool? tool, ImpactDetector? detector) {
            if (tool == null || detector == null) return false;
            try { return BestPart(tool, detector, out ImpactGenerator? best, out _, out ImpactTool? bestTool) && best != null && bestTool != null && Immunity(best, detector) > 0f; }
            catch { return false; }
        }

        // ---------------------------------------------------------------- spectral arrows (server)
        //
        // A spectral arrow's hit is queued, not dealt: every share that lands on one target in one physics tick
        // becomes a single game hit at the end of the tick (FlushSpectral), and the experience for a whole release is
        // paid once, per creature, when its last spectral arrow has ended (the ledger). The game's own per-hit
        // experience is switched off for these hits (NoSpectralXp), so nothing is paid twice.

        sealed class Payload {
            public ImpactTool tool = null!; public ImpactGenerator generator = null!; public ImpactDetector detector = null!;
            public ImpactChannel? channel; public Vector3 point, normal; public float charge;
        }

        // One tick's queued shares of one release. Kept per release so two archers hitting one creature in the
        // same tick stay two hits, each with its own shooter and charge.
        sealed class Pending {
            public readonly HitBatch batch = new HitBatch();
            public readonly Dictionary<int, Payload> first = new Dictionary<int, Payload>();
            public readonly Dictionary<int, int> ghosts = new Dictionary<int, int>(); // ghost (inner/outer) arrows among the hits, per target
            public readonly Dictionary<int, SpectralMix> mix = new Dictionary<int, SpectralMix>(); // balance telemetry: each kind's share, per target
        }

        // What one release did, for its closing log line.
        sealed class ReleaseInfo {
            public Player? shooter;
            public int total, landed, events, healthChanges;
            public float damage; // sum of share x charge over every landed spectral arrow
            public double simMaxMs;
            public readonly Dictionary<int, string> names = new Dictionary<int, string>();
            public readonly Dictionary<int, int> hits = new Dictionary<int, int>();
        }

        static readonly Dictionary<uint, Pending> pending = new Dictionary<uint, Pending>();
        static readonly ReleaseLedger ledger = new ReleaseLedger();
        static readonly Dictionary<uint, ReleaseInfo> releases = new Dictionary<uint, ReleaseInfo>();
        static readonly List<uint> releaseOrder = new List<uint>();
        static readonly List<uint> finishing = new List<uint>();
        static readonly HashSet<string> livePatches = new HashSet<string>();
        static bool inSpectral;
        static ReleaseInfo? current;

        static string Inv(FormattableString s) => s.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // G2: one flag per patch (no string hashed per frame); the set still says each patch once.
        static bool live0, live1;
        static void Live(ref bool said, string patch) {
            if (said) return;
            said = true;
            if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Hits: patch on {patch} is live");
        }

        /// <summary>Server: the release still has spectral arrows out (it has not closed).</summary>
        internal static bool IsOpen(uint release) => releases.ContainsKey(release);

        /// <summary>Server, at launch: this release fires <paramref name="arrows"/> spectral arrows.</summary>
        static readonly int T_OpenRelease = Timing.Section("Hits.OpenRelease");
        internal static void OpenRelease(uint release, int arrows, Player? shooter, ImpactTool? tool) {
            using var _t = Timing.Begin(T_OpenRelease, release);
            if (arrows <= 0) return;
            ledger.Open(release, arrows);
            if (releases.ContainsKey(release)) releaseOrder.Remove(release);
            while (releases.Count >= ReleaseLedger.Cap && releaseOrder.Count > 0) { releases.Remove(releaseOrder[0]); releaseOrder.RemoveAt(0); }
            releases[release] = new ReleaseInfo { shooter = shooter, total = arrows };
            releaseOrder.Add(release);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley: arrow {release} tool {Describe(tool)}");
        }

        /// <summary>Server: one spectral arrow of the release has ended (hit, life or range). The release closes at the end of the tick.</summary>
        internal static void ArrowDone(uint release) {
            if (ledger.ArrowDone(release)) finishing.Add(release);
        }

        /// <summary>Server: queue a spectral arrow's hit. Same filters as <see cref="Apply"/> (players only with PvP, hardness,
        /// channels). Returns true if it was queued, i.e. the arrow landed on something it can damage. <paramref name="ghost"/>:
        /// the arrow is one of the volley's ghosts (a Multi main's gold ghosts add stagger per ghost, Weight.AddSpectral).</summary>
        static readonly int T_Spectral = Timing.Section("hit Hits.Spectral (queue)");
        /// <param name="jumps">H1: false for a homing volley's core arrow: it is dealt with the rest but queues no Ricochet jump of its own
        /// (the core cluster's landing stands in once for the real arrow's jump, Seek.CoreEnded).</param>
        /// <param name="kind">Balance telemetry: which kind of spectral arrow this is (TelemetryRules.SourceOf), so the batched hit's damage
        /// can be split back by kind.</param>
        internal static bool Spectral(ImpactTool tool, RaycastHit hit, float share, float chargeMultiplier, uint release, Player? shooter, bool ghost, bool jumps = true,
                                      DealtBy kind = DealtBy.Ghost) {
            using var _t = Timing.Begin(T_Spectral, release);
            if (tool == null || hit.collider == null || !(share > 0f)) return false;
            ImpactDetector detector = hit.collider.GetComponentInParent<ImpactDetector>();
            if (detector == null) return false;
#if QUEST
            if (detector.HealthTarget.Is(out PlayerHealth ph)) {   // play-test 7: only with PvP on, at [PvP] ModHitShare (Pvp.cs)
#else
            if (detector.HealthTarget is PlayerHealth ph) {   // play-test 7: only with PvP on, at [PvP] ModHitShare (Pvp.cs)
#endif
                float pvp = Pvp.PlayerShare(shooter != null ? shooter.PlayerController : Pvp.ShooterOf(tool), ph); // fixA-review M-6: the release's own shooter
                if (!(pvp > 0f)) return false;
                share *= pvp;
            }
            if (!BestPart(tool, detector, out ImpactGenerator? best, out ImpactChannel? channel, out ImpactTool? bestTool) || best == null || bestTool == null) return false;
            if (!pending.TryGetValue(release, out Pending p)) pending[release] = p = new Pending();
            int key = detector.GetInstanceID();
            if (!p.first.ContainsKey(key))
                p.first[key] = new Payload { tool = bestTool, generator = best, detector = detector, channel = channel, point = hit.point, normal = hit.normal, charge = chargeMultiplier };
            p.batch.Add(key, share, jumps);
            p.mix.TryGetValue(key, out SpectralMix m); m.Add(kind, share); p.mix[key] = m;
            if (ghost) { p.ghosts.TryGetValue(key, out int g); p.ghosts[key] = g + 1; }
            if (releases.TryGetValue(release, out ReleaseInfo info)) {
                info.landed++;
                info.damage += share * chargeMultiplier;
                if (info.shooter == null) info.shooter = shooter;
            }
            return true;
        }

        /// <summary>Product of the immunity multipliers the part and the target share (each 0..1), as the game applies it.</summary>
        static float Immunity(ImpactGenerator generator, ImpactDetector detector) {
            float k = 1f;
            if (generator.ImmunityGroups == null || detector.ImmunityGroups == null) return k;
#if QUEST
            var theirs = new HashSet<ImpactImmunityGroup>(Il.ByPointer<ImpactImmunityGroup>());
#else
            var theirs = new HashSet<ImpactImmunityGroup>();
#endif
#if QUEST
            foreach (ImpactImmunityGroup g in detector.ImmunityGroups.Each()) if (g != null) theirs.Add(g);
#else
            foreach (ImpactImmunityGroup g in detector.ImmunityGroups) if (g != null) theirs.Add(g);
#endif
#if QUEST
            var seen = new HashSet<ImpactImmunityGroup>(Il.ByPointer<ImpactImmunityGroup>());
#else
            var seen = new HashSet<ImpactImmunityGroup>();
#endif
#if QUEST
            foreach (ImpactImmunityGroup g in generator.ImmunityGroups.Each())
#else
            foreach (ImpactImmunityGroup g in generator.ImmunityGroups)
#endif
                if (g != null && theirs.Contains(g) && seen.Add(g)) k *= g.Multiplier;
            return k;
        }

        /// <summary>Server, end of each physics tick: every target hit this tick takes one hit per release, carrying the
        /// sum of the shares that landed on it.</summary>
        static readonly int T_FlushSpectral = Timing.Section("hit Hits.FlushSpectral (batched spectral hits)");
        internal static void FlushSpectral() {
            using var _t = Timing.Begin(T_FlushSpectral);
            if (pending.Count == 0) return;
            // Cleared in a finally: if anything below throws, the same batch must not be dealt again on every tick.
            try {
                foreach (KeyValuePair<uint, Pending> rp in pending) {
                    releases.TryGetValue(rp.Key, out ReleaseInfo? info);
                    HitBatch batch = rp.Value.batch;
                    foreach (int key in batch.Keys) {
                        Payload pl = rp.Value.first[key];
                        if (pl.detector == null || pl.tool == null || pl.generator == null) continue; // destroyed during the tick
                        float sum = batch.Sum(key);
                        int n = batch.Hits(key);
                        SpectralMath.Split(sum, out float e, out float x);
                        e *= Immunity(pl.generator, pl.detector);
                        if (!(e > 0f)) continue;
                        HealthObject target = pl.detector.HealthTarget;
#if QUEST
                        Il2CppSystem.Collections.Generic.Dictionary<ImpactTool, float>? cooldowns = null;
#else
                        Dictionary<ImpactTool, float>? cooldowns = null;
#endif
                        bool dealt = false;
                        try {
                            // The real arrow and its spectral arrows share one tool, and the game ignores a second hit from
                            // the same tool on the same target for a short time: clear it before (so this lands) and after
                            // (so the real arrow's own hit still lands).
                            cooldowns = Cooldowns(pl.detector);
                            cooldowns?.Remove(pl.tool);
                            // No cripple from spectral hits: all stagger goes through Weight later (spec Part 10).
                            var hitNow = new ImpactHitWithTool(pl.tool, pl.generator, pl.detector, pl.tool.Damage * pl.charge * x, e, pl.channel, pl.point, pl.normal) {
                                CripplingMultiplier = 0f,
                            };
                            inSpectral = true; current = info; Dealing = true;
                            rp.Value.mix.TryGetValue(key, out SpectralMix mix);
                            using (HitScope.Spectral(rp.Key, mix)) hitNow.Hit(); // balance telemetry: split back by kind
                            dealt = true;
                        }
                        catch (Exception ex) { MelonLogger.Error($"[Archery] Spectral hit on '{pl.detector.name}' failed: {ex}"); }
                        finally {
                            inSpectral = false; current = null; Dealing = false;
                            cooldowns?.Remove(pl.tool);
                        }
                        if (dealt) Watch(target, "spectral hit");
                        if (!dealt || info == null) continue;
                        info.events++;
                        int creature = target != null ? target.GetInstanceID() : key;
                        info.names[creature] = target != null ? target.name : pl.detector.name;
                        info.hits.TryGetValue(creature, out int h); info.hits[creature] = h + n;
                        // The ghosts' stagger joins the release's one cripple hit per creature (Weight; players never).
#if QUEST
                        if (target != null && !(target.Is<PlayerHealth>())) {
#else
                        if (target != null && !(target is PlayerHealth)) {
#endif
                            rp.Value.ghosts.TryGetValue(key, out int ghostHits);
                            try { Weight.AddSpectral(rp.Key, target, sum, ghostHits); }
                            catch (Exception ex) { MelonLogger.Error($"[Archery] Weight: spectral stagger on '{target.name}' failed: {ex.Message}"); }
                            // Shatter off the main path skips creatures this release already damaged (fix batch D)
                            Targets.Damaged(rp.Key, target);
                            // Ricochet: the creature's jump collects what this release dealt it (nothing without the orb; a homing
                            // volley's core arrows are not among the shares, H1)
                            Ricochet.ServerStruck(rp.Key, target, batch.Shares(key), pl.charge, pl.tool, HitSource.Spectral);
                        }
                        // Experience as the game would pay it for this hit (ImpactDetector.RunPlayerEvents and
                        // CombatImpactProgressionFilter.ApplyImpact), but once per creature per release.
#if QUEST
                        if (target != null && !(target.Is<PlayerHealth>()) && target.IsCombatant && pl.detector.Settings != null && pl.detector.Settings.ProvideExperienceWhenHit && pl.tool.ProvidesExperience)
#else
                        if (target != null && !(target is PlayerHealth) && target.IsCombatant && pl.detector.Settings != null && pl.detector.Settings.ProvideExperienceWhenHit && pl.tool.ProvidesExperience)
#endif
                            ledger.Add(rp.Key, creature, sum);
                    }
                }
            }
            finally { pending.Clear(); }
        }

        /// <summary>Server, after <see cref="FlushSpectral"/>: how long this tick of the spectral simulation took, and the
        /// releases whose last spectral arrow ended in it are paid and logged.</summary>
        static readonly int T_SimTick = Timing.Section("Hits.SimTick");
        internal static void SimTick(double ms) {
            using var _t = Timing.Begin(T_SimTick);
            foreach (ReleaseInfo info in releases.Values) if (ms > info.simMaxMs) info.simMaxMs = ms;
            try { Ricochet.JumpTick(); } // jumps whose batch time is over
            catch (Exception e) { MelonLogger.Error($"[Archery] Ricochet: jump tick failed: {e.Message}"); }
            if (finishing.Count == 0) return;
            foreach (uint id in finishing) Close(id);
            finishing.Clear();
        }

        static readonly int T_Close = Timing.Section("Hits.Close (release closed)");
        static void Close(uint release) {
            using var _t = Timing.Begin(T_Close, release);
            Dictionary<int, float> shares = ledger.Take(release);
            if (!releases.TryGetValue(release, out ReleaseInfo info)) return;
            releases.Remove(release); releaseOrder.Remove(release);
            try { Weight.ReleaseClosed(release); } // the release's stagger is dealt now, one cripple hit per creature
            catch (Exception e) { MelonLogger.Error($"[Archery] Weight: closing arrow {release}'s stagger failed: {e}"); }
            try { Ricochet.JumpClosed(release); } // its waiting jumps are dealt now
            catch (Exception e) { MelonLogger.Error($"[Archery] Ricochet: closing arrow {release}'s jumps failed: {e.Message}"); }
            if (shares.Count > 0) {
                ProgressionSettings settings = GlobalSettings<ProgressionSettings>.Instance;
                if (info.shooter == null)
                    MelonLogger.Warning($"[Archery] Xp: arrow {release} has no shooter; {shares.Count} creature(s) pay no experience");
                foreach (KeyValuePair<int, float> c in shares) {
                    float xp = settings != null ? settings.GetDamageExperience(c.Value, ProgressionPath.Ranged) : 0f;
                    if (info.shooter != null) {
#if QUEST
                        try { PlayerProgressionManager.GainExperience(info.shooter.Cast<IPlayer>(), ProgressionPath.Ranged, xp); }
#else
                        try { PlayerProgressionManager.GainExperience(info.shooter, ProgressionPath.Ranged, xp); }
#endif
                        catch (Exception e) { MelonLogger.Error($"[Archery] Xp: paying arrow {release} failed: {e}"); }
                    }
                    info.names.TryGetValue(c.Key, out string? name);
                    info.hits.TryGetValue(c.Key, out int hits);
                    if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Xp: arrow {release} '{name ?? "?"}' shares {c.Value:0.000} -> {xp:0.###} Ranged xp ({hits} spectral hits, {(hits > 0 ? xp / hits : 0f):0.###} each)"));
                }
            }
            if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Volley: arrow {release} {info.landed}/{info.total} spectral arrows landed; {info.names.Count} creature(s) hit in {info.events} damage events ({info.healthChanges} health changes); damage x{info.damage:0.00} of the arrow; sim max {info.simMaxMs:0.00} ms/tick"));
        }

        // Spectral hits pay their experience from the ledger, once per creature per release. Only the payment is
        // stopped: the filter still sets the Ranged path, so the player's path events see a Ranged hit.
        [HarmonyPatch(typeof(BaseImpactChannelProgressionFilter), "GainExperience")]
        static class NoSpectralXp {
            static readonly int T = Timing.Section("patch BaseImpactChannelProgressionFilter.GainExperience / Hits.NoSpectralXp");
            static bool Prefix() {
                using var _t = Timing.Begin(T);
                if (!inSpectral) return true;
                Live(ref live0, "BaseImpactChannelProgressionFilter.GainExperience");
                return false;
            }
        }

        // Counts the health changes a release's batched hits caused (the log's "health changes").
        [HarmonyPatch(typeof(HealthObject), nameof(HealthObject.ReceiveDamage), new[] { typeof(float), typeof(float), typeof(DamageData) })]
        static class CountHealthChanges {
            static readonly int T = Timing.Section("patch HealthObject.ReceiveDamage / Hits.CountHealthChanges");
            static void Postfix() {
                using var _t = Timing.Begin(T);
                if (!inSpectral) return;
                Live(ref live1, "HealthObject.ReceiveDamage");
                if (current != null) current.healthChanges++;
            }
        }

#if QUEST
        static ImpactChannel? Shared(Il2CppSystem.Collections.Generic.List<ImpactChannel> a, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<ImpactChannel> b) {
#else
        static ImpactChannel? Shared(List<ImpactChannel> a, ImpactChannel[] b) {
#endif
            if (a == null || b == null) return null;
            foreach (ImpactChannel x in a)
                foreach (ImpactChannel y in b)
                    if (x != null && x == y) return x;
            return null;
        }
    }
}
