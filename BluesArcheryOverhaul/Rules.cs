using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Unity-free arithmetic and decision rules. Rules.cs, Build.cs, Wire.cs, Release.cs, Spectral.cs, Charge.cs, Path.cs, Preview.cs and Lectern.cs are the files BluesArcheryOverhaul.Tests compiles,
// so nothing in here may touch UnityEngine, MelonLoader or the game.
namespace BluesArcheryOverhaul {
    /// <summary>Holy Arrow's long hold. The volley's own beats are in Release.cs (VolleyTimeline); the live values
    /// come from settings and are passed in where they matter.</summary>
    internal static class DrawTimeline {
        public const float HolyTotal = 5.5f;                // was 6.5 (user 2026-11-19: "a bit quicker")
        public const float GatherSeconds = 1.25f;           // 4.25 s -> 5.5 s; before that the fifteen sit fanned out

        /// <summary>How far the volley has folded into the arrow for Holy Arrow: 0 while it is fanned out,
        /// rising to 1 over the last <paramref name="gatherSeconds"/> before Holy Arrow is ready.</summary>
        public static float GatherProgress(float sinceHolyStarted, float holySeconds, float gatherSeconds) {
            if (gatherSeconds <= 0f) return sinceHolyStarted >= holySeconds ? 1f : 0f;
            float t = (sinceHolyStarted - (holySeconds - gatherSeconds)) / gatherSeconds;
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }
    }

    internal static class VolleyMath {
        public const int Inner = 5, Total = 15, Outer = Total - Inner;
        // Metres, at the real arrow's tip. Play-test 6: 70% of the first design's 0.04 and 0.1 ("tighten up the spread on all
        // the arrows"): each ghost aims from the nock point through its tip, so the radius is its spread angle too.
        public const float InnerRadius = 0.028f, OuterRadius = 0.07f;
        // Five at 15% and ten at 6%: the same totals as the first design's 3 x 25% and 6 x 10% (175% and 235%).
        public const float InnerShare = 0.15f, OuterShare = 0.06f;

        /// <summary>Share of the charged main arrow's damage one extra deals.</summary>
        public static float ExtraShare(int index, float inner, float outer) => index < Inner ? inner : outer;

        /// <summary>Largest possible total, main arrow included, if every extra lands.</summary>
        public static float MaxTotal(int extras, float inner, float outer) {
            float sum = 1f;
            for (int i = 0; i < extras; i++) sum += ExtraShare(i, inner, outer);
            return sum;
        }

        /// <summary>Where extra <paramref name="index"/>'s tip sits relative to the real arrow's tip, in the plane
        /// across the aim (x = right, y = up). Inner ones: an even ring with one point straight up.
        /// Outer ones: a wider even ring starting at "right".</summary>
        public static void TipOffset(int index, out float x, out float y, float innerRadius = InnerRadius, float outerRadius = OuterRadius) {
            float r = index < Inner ? innerRadius : outerRadius;
            double rad = Azimuth(index) * Math.PI / 180.0;
            x = (float)(Math.Cos(rad) * r);
            y = (float)(Math.Sin(rad) * r);
        }

        /// <summary>Degrees round the real arrow (0 = right, 90 = up) at which extra <paramref name="index"/>'s tip sits: the
        /// inner ring from straight up, the outer ring from the right, each evenly spaced (<see cref="Step"/>). The one
        /// definition of the ghosts' rings; Burst's pellets are placed from it (BurstPattern.Pellet).</summary>
        public static double Azimuth(int index) => index < Inner ? 90 + index * (360.0 / Inner) : (index - Inner) * (360.0 / Outer);

        /// <summary>Degrees between two neighbouring extras of <paramref name="index"/>'s ring (72 inner, 36 outer).</summary>
        public static double Step(int index) => 360.0 / (index < Inner ? Inner : Outer);
    }

    internal static class BlastMath {
        /// <summary>Fraction of the centre damage dealt at <paramref name="distance"/>: 1 inside the full-damage
        /// radius, falling in a straight line to <paramref name="edgeFraction"/> at the edge, 0 beyond it.</summary>
        public static float Fraction(float distance, float fullRadius, float radius, float edgeFraction) {
            if (float.IsNaN(distance) || distance > radius) return 0f;
            if (distance <= fullRadius || radius <= fullRadius) return 1f;
            float t = (distance - fullRadius) / (radius - fullRadius);
            return 1f + (edgeFraction - 1f) * t;
        }
    }

    internal enum ReturnAction {
        Wait,    // not now; look again shortly
        Return,  // dock it into its quiver
        GiveUp,  // it is an ordinary arrow from now on
        Forget,  // the arrow no longer exists
    }

    internal static class QuiverRules {
        /// <summary>What to do with a tracked arrow whose time has come. First matching rule wins.</summary>
        public static ReturnAction Decide(bool arrowExists, bool areaLoaded, bool held, bool storedElsewhere, bool quiverCarried, bool quiverHasRoom) {
            if (!arrowExists) return ReturnAction.Forget;
            if (!areaLoaded) return ReturnAction.GiveUp;        // left behind: the intended way to lose arrows
            if (storedElsewhere) return ReturnAction.GiveUp;    // someone put it away
            if (held) return ReturnAction.Wait;                 // comes back once it is let go
            if (!quiverCarried) return ReturnAction.Wait;       // comes back once the quiver is worn again
            if (!quiverHasRoom) return ReturnAction.Wait;       // full quiver: comes back once there is room (0.19.11; gave up for good before)
            return ReturnAction.Return;
        }

        // 0.19.13 (the owner: "IF arrows clip into the wall or fall out into the void, have it snap back to the quiver instantly")
        public const float VoidFallSpeed = 15f, VoidDepthMetres = 3f;

        /// <summary>A shot arrow nobody can get back, which returns at once instead of at its time. Void: falling faster than
        /// VoidFallSpeed in loaded world with no ground at all under it (off the map) or ground more than VoidDepthMetres above it
        /// (through the ground). Clipped: stuck in the world (not in a creature or player) with its middle behind the surface it
        /// went into (<paramref name="wallBeforeMiddle"/>: a cast back along its own way met the world before its middle).</summary>
        public static OutOfReach Reach(bool stuckInWorld, bool wallBeforeMiddle, float verticalSpeed, bool loaded, bool groundFound, float groundAboveMetres) {
            if (stuckInWorld) return wallBeforeMiddle ? OutOfReach.Clipped : OutOfReach.None;
            if (!(verticalSpeed < -VoidFallSpeed) || !loaded) return OutOfReach.None;
            return !groundFound || groundAboveMetres > VoidDepthMetres ? OutOfReach.Void : OutOfReach.None;
        }
    }

    internal enum OutOfReach { None, Clipped, Void }

    /// <summary>What one poll of a queued Auto Dock does.</summary>
    internal enum DockStep { Wait, Dock, Drop }

    /// <summary>What one poll does with an auto-docked arrow that has not been shot.</summary>
    internal enum UnshotStep { Wait, Return, Forget }

    /// <summary>Auto Dock (spec 2026-11-16): after a shot, the quiver's next arrow nocks itself on the string, unless the archer
    /// has done anything in the meantime. A dock the game refuses waits for the next grab of the string, for a few seconds.</summary>
    internal static class AutoDockRule {
        /// <summary>One poll of a queued dock. First matching rule wins: anything the archer did drops it; a refused dock
        /// (<paramref name="fallback"/>) waits for the grab (DockOnGrab) until it expires.</summary>
        public static DockStep Poll(bool alive, bool stringHasArrow, bool ownerHoldsBow, bool stringGrabbed, bool handHoldsArrow,
                                    bool due, bool fallback, bool fallbackExpired) {
            if (!alive || stringHasArrow || !ownerHoldsBow || handHoldsArrow) return DockStep.Drop;
            if (fallback) return fallbackExpired ? DockStep.Drop : DockStep.Wait;
            if (stringGrabbed) return DockStep.Drop;
            return due ? DockStep.Dock : DockStep.Wait;
        }

        /// <summary>The archer grabbed the string: a refused dock that is still waiting docks now.</summary>
        public static bool DockOnGrab(bool fallback, bool fallbackExpired, bool stringHasArrow, bool ownerHoldsBow, bool handHoldsArrow) =>
            fallback && !fallbackExpired && !stringHasArrow && ownerHoldsBow && !handHoldsArrow;

        public static bool FallbackExpired(float now, float refusedAt, float fallbackSeconds) => now - refusedAt >= fallbackSeconds;

        /// <summary>b2-review I-1: an arrow Auto Dock put on the string that has not been shot. The game undocks the string whenever
        /// the bow itself is docked (holstered), so it can fall out. First matching rule wins: gone, or taken by a hand (the
        /// archer's now): forget; still on the string: wait; put in some dock (its quiver included): forget; its quiver gone or
        /// full, or no longer carried by the archer who owned it (b3-review M-1): forget, the arrow stays where it is; loose for less
        /// than <paramref name="waitSeconds"/>: wait (a shot whose OnShoot postfix never ran is not pulled out of the air at once);
        /// otherwise it goes back into its quiver.</summary>
        public static UnshotStep Unshot(bool exists, bool onString, bool handHolds, bool docked, bool quiverExists, bool quiverRoom,
                                        bool quiverCarried, float looseSeconds, float waitSeconds) {
            if (!exists || handHolds) return UnshotStep.Forget;
            if (onString) return UnshotStep.Wait;
            if (docked || !quiverExists || !quiverRoom || !quiverCarried) return UnshotStep.Forget;
            if (!(looseSeconds >= waitSeconds)) return UnshotStep.Wait;
            return UnshotStep.Return;
        }

        /// <summary>For the log: why a dock was dropped (the first matching reason of Poll).</summary>
        public static string Why(bool alive, bool stringHasArrow, bool ownerHoldsBow, bool stringGrabbed, bool handHoldsArrow) =>
            !alive ? "the bow or the archer is gone" : stringHasArrow ? "the string already holds an arrow" : !ownerHoldsBow ? "the bow is no longer held"
            : handHoldsArrow ? "an arrow is in the archer's hand" : stringGrabbed ? "the string was grabbed" : "ok";
    }

    /// <summary>Seconds since a bow string crossed 68% pull with an arrow nocked, kept per bow string on every machine.
    /// A span ends when the pull drops to 68% or below (or no arrow); it stays readable for <c>grace</c> seconds
    /// so a shot message that arrives just after the string relaxes can still be timed.</summary>
    internal sealed class DrawClock {
        public const float CrossingPull = 0.68f;
        public const float Grace = 1f;
        float crossedAt = float.NaN, endedAt = float.NaN;

        /// <summary>The arrow that was nocked when the current span began (0 when none).</summary>
        public uint ArrowAtCrossing { get; private set; }

        public void Update(float pull, bool nocked, float now) => Update(pull, nocked ? 1u : 0u, now, 0f);

        /// <summary>A dip below the crossing shorter than <paramref name="dipGrace"/> resumes the same span.</summary>
        public void Update(float pull, uint arrowId, float now, float dipGrace) {
            if (arrowId != 0u && pull > CrossingPull) {
                if (float.IsNaN(crossedAt)) Start(arrowId, now);
                else if (!float.IsNaN(endedAt)) {
                    if (dipGrace > 0f && now - endedAt <= dipGrace) endedAt = float.NaN;
                    else Start(arrowId, now);
                }
            } else if (!float.IsNaN(crossedAt) && float.IsNaN(endedAt)) {
                endedAt = now;   // only the first low update ends the span; later ones must not extend the grace
            }
        }

        void Start(uint arrowId, float now) { crossedAt = now; endedAt = float.NaN; ArrowAtCrossing = arrowId; }

        public void Reset() { crossedAt = float.NaN; endedAt = float.NaN; ArrowAtCrossing = 0u; }

        /// <summary>True while a span is running, or ended less than <paramref name="dipGrace"/> ago.</summary>
        public bool Active(float now, float dipGrace) =>
            !float.IsNaN(crossedAt) && (float.IsNaN(endedAt) || now - endedAt <= dipGrace);

        public bool TryElapsed(float now, float grace, out float seconds) {
            seconds = 0f;
            if (float.IsNaN(crossedAt)) return false;
            if (!float.IsNaN(endedAt) && now - endedAt > grace) return false;
            seconds = now - crossedAt;
            return true;
        }
    }

    /// <summary>The server's DrawGrace rule for one string: a dip at or below the draw threshold counts only once it has
    /// lasted the grace. The client's Quickdraw ring uses it so a twitch the server ignores does not re-arm the ring.</summary>
    internal sealed class RelaxTimer {
        float belowSince = float.NaN;

        /// <summary>True once the pull has stayed at or below the threshold for at least <paramref name="grace"/> seconds
        /// (at once when the grace is 0). Coming back above clears it.</summary>
        public bool Relaxed(bool below, float now, float grace) {
            if (!below) { belowSince = float.NaN; return false; }
            if (float.IsNaN(belowSince)) belowSince = now;
            return now - belowSince >= grace;
        }

        public void Reset() => belowSince = float.NaN;
    }

    internal static class ClaimRules {
        public const int ToleranceMs = 60;

        /// <summary>Server minus client, or null when either side does not know its time.</summary>
        public static int? Diff(int? clientMs, int? serverMs) => clientMs.HasValue && serverMs.HasValue ? serverMs.Value - clientMs.Value : (int?)null;

        /// <summary>A spectator's reading of a relayed kind: a Line is relayed as Snap, so a Snap with Quickdraw 4 in the shooter's build is a Line.</summary>
        public static UltimateUse SeenUltimate(ShotKind kind, Build build) =>
            kind == ShotKind.Snap && (build.Ultimates & Ultimates.Quickdraw4) != 0 ? UltimateUse.Line : UltimateUse.None;

        public static bool Agrees(int? clientMs, int? serverMs, int toleranceMs = ToleranceMs) {
            int? d = Diff(clientMs, serverMs);
            return d.HasValue && Math.Abs(d.Value) <= toleranceMs;
        }

        /// <summary>The server's checked copy of a claim with nothing in its lists: a lock (steered) and picks are never
        /// trusted yet, and targets are cleared too (a shooter whose build wants no target list), and a Skyfall code or marks,
        /// which only the server's own decision may relay (Skyfall's seam, Claims.PutSkyfall).</summary>
        public static ShotClaim ServerCheck(ShotClaim claim) {
            var c = claim.Copy();
            c.Steered = false;
            c.Targets.Clear(); c.Picks.Clear();
            c.Sky = SkyCode.None; c.Marks.Clear();
            return c;
        }

        /// <summary>Batch 3: the server's checked copy of a claim whose targets it relays. Each target is kept, in the order the
        /// shooter wrote it and once, only when <paramref name="facts"/> says it passes <see cref="TargetRule.Keep"/> within
        /// <paramref name="range"/> of the shooter; at most the wire's six. Steered and picks are cleared as before.</summary>
        public static ShotClaim ServerCheck(ShotClaim claim, Func<uint, TargetFacts> facts, float range) {
            ShotClaim c = ServerCheck(claim);
            foreach (uint id in claim.Targets) {
                if (c.Targets.Count >= ShotClaimWire.MaxTargets) break;
                if (c.Targets.Contains(id) || !TargetRule.Keep(facts(id), range)) continue;
                c.Targets.Add(id);
            }
            return c;
        }
    }

    /// <summary>What a machine knows about one creature id of a shot's target list (the server checks; the shooter picks).</summary>
    internal readonly struct TargetFacts {
        public readonly bool Exists, Creature, Player, Alive;
        public readonly float Distance; // metres from the shooter

        public TargetFacts(bool exists, bool creature, bool player, bool alive, float distance) {
            Exists = exists; Creature = creature; Player = player; Alive = alive; Distance = distance;
        }
    }

    /// <summary>A creature the shooter could list: its id, how far off the line of aim it is, and its facts.</summary>
    internal readonly struct TargetCandidate {
        public readonly uint Id;
        public readonly float AngleDegrees;
        public readonly TargetFacts Facts;

        public TargetCandidate(uint id, float angleDegrees, TargetFacts facts) { Id = id; AngleDegrees = angleDegrees; Facts = facts; }
    }

    /// <summary>The shot claim's target list (batch 3, spec 2026-11-16): the shooter lists creatures, the server keeps the ones that
    /// pass and relays them, so Ricochet chooses from the same ids on every machine.</summary>
    internal static class TargetRule {
        /// <summary>A target counts: it exists, is a creature (never a player), is alive and within <paramref name="range"/>.</summary>
        public static bool Keep(TargetFacts f, float range) =>
            f.Exists && f.Creature && !f.Player && f.Alive && !float.IsNaN(f.Distance) && f.Distance <= range;

        /// <summary>The shooter's list: creatures inside the cone round the aim (<paramref name="coneDegrees"/> is the cone's full
        /// width, so half of it either side of the line) that pass <see cref="Keep"/>, nearest the shooter first (a tie: the lower
        /// id first), each id once, at most <paramref name="max"/>.</summary>
        public static List<uint> Pick(IList<TargetCandidate> candidates, float coneDegrees, float range, int max) {
            var kept = new List<TargetCandidate>();
            float half = Math.Max(0f, coneDegrees) * 0.5f;
            foreach (TargetCandidate c in candidates) {
                if (!(c.AngleDegrees <= half) || !Keep(c.Facts, range)) continue;
                bool seen = false;
                foreach (TargetCandidate k in kept) if (k.Id == c.Id) { seen = true; break; }
                if (!seen) kept.Add(c);
            }
            kept.Sort((a, b) => a.Facts.Distance != b.Facts.Distance ? a.Facts.Distance.CompareTo(b.Facts.Distance) : a.Id.CompareTo(b.Id));
            var ids = new List<uint>();
            for (int i = 0; i < kept.Count && i < max; i++) ids.Add(kept[i].Id);
            return ids;
        }

        /// <summary>The shooter writes a target list (and the server keeps one) only for a build that uses it: a Multi main with
        /// Ricochet, a Charge main with Shatter (batch 4b; later Seeking Volley). Anyone else's claim is exactly as before.</summary>
        public static bool Wanted(Build b) => RicochetRule.Applies(b) || ShatterRule.Half(b) != ShatterHalf.None;
    }

    /// <summary>G2: the shooter's target search overlaps the sphere that encloses its cone instead of the whole ball of the range.
    /// The sphere passes through the cone's apex with its centre <c>ahead</c> metres along the aim, <c>ahead = reach / (2 cos h)</c>:
    /// a point at distance r from the apex and angle t off the aim is inside it when r² - 2 r ahead cos t ≤ 0, i.e. r ≤ 2 ahead cos t,
    /// and every point with r ≤ reach and t ≤ h meets that (reach = 2 ahead cos h ≤ 2 ahead cos t). <c>margin</c> is added to the
    /// radius on top. For h of 60 degrees or more the sphere is no smaller than the ball, so the ball stays (false).</summary>
    internal static class TargetSearch {
        public const float MarginMetres = 1f;

        public static bool Enclose(float reach, float halfDegrees, float margin, out float ahead, out float radius) {
            ahead = 0f; radius = reach;
            if (!(reach > 0f) || !(halfDegrees >= 0f) || !(halfDegrees < 60f)) return false;
            double c = reach / (2.0 * Math.Cos(halfDegrees * Math.PI / 180.0));
            ahead = (float)c;
            radius = (float)c + Math.Max(0f, margin);
            return radius < reach;   // only worth it when it is smaller than the ball it replaces
        }
    }

    // ---------------------------------------------------------------- batch 4b: Quickdraw 4's line pierces (user, 2026-11-16)

    /// <summary>What the line met. <c>PassedOver</c> (fix 5, b4b-review M-1/M-3): a creature the line cannot hurt, i.e. one whose
    /// detectors all refuse the arrow (hardness or channel), or, on the server, one already dead. The line passes through it: it
    /// takes no place in the share order, no cap slot, and never stops the line.</summary>
    internal enum LineHitKind { Creature, Player, Solid, PassedOver }

    /// <summary>One thing the Quickdraw 4 line's cast met: the creature's id (any number for a player or a solid), its distance along
    /// the line, and what it is. A creature has several colliders: each is one candidate with the same id.</summary>
    internal readonly struct LineCandidate {
        public readonly uint Id;
        public readonly float Distance;
        public readonly LineHitKind Kind;
        public LineCandidate(uint id, float distance, LineHitKind kind) { Id = id; Distance = distance; Kind = kind; }
    }

    /// <summary>A creature the line passes through: the candidate's index, its place in the line's order (0 = the first creature)
    /// and the share of the line's damage it takes.</summary>
    internal readonly struct LineStrike {
        public readonly int Index, Order;
        public readonly float Share;
        public LineStrike(int index, int order, float share) { Index = index; Order = order; Share = share; }
    }

    /// <summary>Quickdraw 4's pierce: the line passes through every creature on its path, nearest first, each creature once (at its
    /// nearest collider), and stops at the first world surface or player (a player is never damaged, as before) or in the
    /// <c>max</c>-th creature. The first creature takes the line's full damage; later ones a share from [Quickdraw4] PierceShares
    /// (second creature the first value, third the second, ...; the last value repeats).</summary>
    internal static class LinePierce {
        public static readonly float[] DefaultShares = { 0.7f, 0.5f };
        public const string DefaultSharesText = "0.7, 0.5";
        public const int DefaultMax = 8;
        /// <summary>[Quickdraw4] Damage, shipped (play-test 7 balance: was 3; a full Charge Shot 3 must hit much harder per arrow).</summary>
        public const float DefaultDamage = 2.5f;

        /// <summary>The share of the line's damage the creature at <paramref name="order"/> takes (0 = the first: all of it).</summary>
        public static float Share(int order, IList<float> shares) {
            if (order <= 0 || shares == null || shares.Count == 0) return 1f;
            return shares[Math.Min(order - 1, shares.Count - 1)];
        }

        /// <summary>The creatures struck, in order, and in <paramref name="end"/> the index of the candidate the line stops at
        /// (a surface, a player, or the last creature when the cap is reached); -1 when nothing stops it before its range.</summary>
        public static List<LineStrike> Walk(IList<LineCandidate> hits, int max, IList<float> shares, out int end) {
            end = -1;
            var strikes = new List<LineStrike>();
            if (hits == null || hits.Count == 0) return strikes;
            if (max < 1) max = 1;
            var order = new List<int>(hits.Count);
            for (int i = 0; i < hits.Count; i++) order.Add(i);
            // a stable sort by distance (List.Sort is not stable): ties keep the cast's order
            order.Sort((a, b) => { int c = hits[a].Distance.CompareTo(hits[b].Distance); return c != 0 ? c : a.CompareTo(b); });
            var seen = new HashSet<uint>();
            foreach (int i in order) {
                LineCandidate h = hits[i];
                if (h.Kind == LineHitKind.PassedOver) continue;   // cannot be hurt (or dead, server): passed through, no place
                if (h.Kind != LineHitKind.Creature) { end = i; break; }
                if (!seen.Add(h.Id)) continue;
                strikes.Add(new LineStrike(i, strikes.Count, Share(strikes.Count, shares)));
                if (strikes.Count >= max) { end = i; break; }
            }
            return strikes;
        }

        /// <summary>"0.7, 0.5" (commas or semicolons): each value kept between 0 and 1. False for empty or unreadable text.</summary>
        public static bool TryParseShares(string? text, out float[] shares) {
            shares = new float[0];
            if (string.IsNullOrEmpty(text) || text!.Trim().Length == 0) return false;
            var list = new List<float>();
            foreach (string raw in text.Split(',', ';')) {
                if (!float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || float.IsNaN(v)) return false;
                list.Add(v < 0f ? 0f : v > 1f ? 1f : v);
            }
            shares = list.ToArray();
            return shares.Length > 0;
        }

        public static float[] ParseShares(string? text) => TryParseShares(text, out float[] s) ? s : (float[])DefaultShares.Clone();
    }

    /// <summary>Play-test 7, PvP. (a) The game leaves a flying arrow's hit on a player to the victim's own client and then refuses
    /// that client's report, because the arrow's last interactor is the shooter (ImpactDetector.ShouldRefuseSyncedHit): no shot
    /// or thrown item ever hurt a player on a dedicated server. The server now accepts exactly one more kind of report: a
    /// player's report of their own body being hit by a flying arrow someone else shot. A client can only ever hurt itself that
    /// way (and it supplies the hit's effectiveness, so it can only lower it). (b) The mod's own hits land on players only
    /// with the server's PvP on, at a share, never on the shooter. (c) Holy Arrow's blast follows the server's PvP setting.</summary>
    /// <summary>What the server knows about one player's report of an arrow hitting their own body (fix batch D, fixA-review I-1).</summary>
    internal struct OwnReport {
        public bool Enabled;            // [PvP] ArrowsHurtPlayers
        public bool GameRefused;        // the game's own check refused it (the arrow's last interactor is not the reporter)
        public bool OwnBody;            // the report is about the reporter's own body
        public bool IsArrow, HasTool;   // an Arrow, and the reported entity has a damage tool
        public bool Held, Docked;       // a hand holds it; it sits in a dock (a quiver, a string, a bag)
        public bool ShotBySomeoneElse;  // it has a last interactor (someone shot it)
        public bool ServerPvp;          // the server's IsPVPEnabled
        public float? SinceShot;        // seconds since the server saw this arrow shot from a bow (null: never, or forgotten)
        public bool AcceptedThisShot;   // a report of this arrow was already accepted since that shot
    }

    /// <summary>Server: the arrows shot from bows and whether a player's report of one hitting them was accepted since (fix batch D,
    /// fixA-review I-1: one accepted report per arrow per shot). Bounded: the oldest shot is forgotten first.</summary>
    internal sealed class ShotReportLedger {
        public const int Cap = 256;
        readonly Dictionary<uint, float> shotAt = new Dictionary<uint, float>();
        readonly HashSet<uint> accepted = new HashSet<uint>();
        readonly List<uint> order = new List<uint>();

        public int Count => shotAt.Count;

        /// <summary>The arrow left a bow now: a new shot, nothing accepted for it yet.</summary>
        public void Shot(uint id, float now) {
            if (id == 0u) return;
            if (shotAt.ContainsKey(id)) order.Remove(id);
            while (order.Count >= Cap) { shotAt.Remove(order[0]); accepted.Remove(order[0]); order.RemoveAt(0); }
            shotAt[id] = now;
            accepted.Remove(id);
            order.Add(id);
        }

        public float? Since(uint id, float now) => shotAt.TryGetValue(id, out float at) ? now - at : (float?)null;
        public bool Accepted(uint id) => accepted.Contains(id);
        public void Accept(uint id) { if (shotAt.ContainsKey(id)) accepted.Add(id); }
    }

    internal static class PvpRule {
        public const float DefaultModHitShare = 0.5f;
        /// <summary>[PvP] ReportSeconds: a report counts only within this many seconds of the arrow's shot.</summary>
        public const float DefaultReportSeconds = 10f;
        /// <summary>[PvP] JumpsHitPlayers ships on since 0.19.11 (the owner: "Turn on all pvp settings with archery as default"; off
        /// before: Ricochet's jumps are not aimed). The server's PvP and ModHitsHurtPlayers still gate it.</summary>
        public const bool DefaultJumpsHitPlayers = true;
        /// <summary>[PvP] BlastHitsPlayers ships on since 0.19.11 (the owner's PvP defaults; off from fixF, final review I-2: Shatter's
        /// blast fires on the first creature hit of every release, and its cone opens where a friend fighting that creature stands).</summary>
        public const bool DefaultBlastHitsPlayers = true;

        /// <summary>Why the server keeps refusing a player's report of an arrow hitting their own body, or null to accept it:
        /// [PvP] ArrowsHurtPlayers on, the game refused it, the reporter's own body, an Arrow with a damage tool that no hand holds
        /// and no dock holds, shot from a bow by someone within <paramref name="reportSeconds"/>, no report of it accepted since
        /// that shot, and the server's PvP on.</summary>
        public static string? ReportRefusal(OwnReport r, float reportSeconds) {
            if (!r.GameRefused) return "the game accepts it";
            if (!r.Enabled) return "[PvP] ArrowsHurtPlayers is off";
            if (!r.OwnBody) return "not the reporter's own body";
            if (!r.IsArrow) return "not an arrow";
            if (!r.HasTool) return "no damage tool";
            if (r.Held) return "held in a hand";
            if (r.Docked) return "in a quiver or on a string";
            if (!r.ShotBySomeoneElse) return "nobody shot it";
            if (!r.ServerPvp) return "the server's PvP is off";
            if (!r.SinceShot.HasValue) return "not shot from a bow";
            float s = r.SinceShot.Value;
            if (float.IsNaN(reportSeconds) || reportSeconds < 0f || float.IsNaN(s) || s < 0f || s > reportSeconds) return "not shot recently";
            if (r.AcceptedThisShot) return "already accepted for this shot";
            return null;
        }

        /// <summary>Ricochet's jumps may pick players: the mod's hits land on players right now AND [PvP] JumpsHitPlayers.</summary>
        public static bool JumpsTargetPlayers(bool modHitsLand, bool jumpsHitPlayers) => modHitsLand && jumpsHitPlayers;

        /// <summary>Shatter's blast may catch players: the mod's hits land on players right now AND [PvP] BlastHitsPlayers.</summary>
        public static bool BlastTargetsPlayers(bool modHitsLand, bool blastHitsPlayers) => modHitsLand && blastHitsPlayers;

        /// <summary>The share of a creature's hit a mod-made hit (ghost, pellet, needle, heavy arrow, the line, a jump, Shatter's
        /// blast) deals to a player: 0 (no hit, as before) unless the server's PvP is on and [PvP] ModHitsHurtPlayers, never on
        /// the shooter. The game's PVPMultiplier applies on top (the hit is an ImpactHitWithTool by a player).</summary>
        public static float ModHitShare(bool serverPvp, bool modHitsOn, bool isShooter, float share) =>
            serverPvp && modHitsOn && !isShooter && share > 0f && !float.IsInfinity(share) ? share : 0f;

        /// <summary>Holy Arrow's blast (a plain ImpactHit the game's PvP gate never sees) on a player: false = nothing; else the
        /// health and cripple factors are the server's PVPMultiplier and PVPCrippleMultiplier (never negative).</summary>
        public static bool HolyBlast(bool serverPvp, bool modHitsOn, bool isShooter, float pvpMultiplier, float pvpCrippleMultiplier, out float health, out float cripple) {
            health = cripple = 0f;
            if (!serverPvp || !modHitsOn || isShooter) return false;
            health = pvpMultiplier > 0f && !float.IsInfinity(pvpMultiplier) ? pvpMultiplier : 0f;
            cripple = pvpCrippleMultiplier > 0f && !float.IsInfinity(pvpCrippleMultiplier) ? pvpCrippleMultiplier : 0f;
            return true;
        }
    }

    /// <summary>Play-test 7 (2a, widened): stale skill inputs on a bow string. A grab whose BowDrawSkill.Register threw "same key"
    /// leaves its new input registered (bowStrings) and subscribed to the string with no handler that will ever clear it, and the
    /// string's entry (inputs) still names an earlier holder's input. They pile up and keep being driven on later draws. When a
    /// new input registers for a string, every other registration for that string goes first, whoever it belonged to.</summary>
    internal static class StaleInputRule {
        /// <summary>The registered inputs (input -> string) that are stale once <paramref name="input"/> registers for <paramref name="str"/>.</summary>
        public static List<TInput> Stale<TInput, TString>(IEnumerable<KeyValuePair<TInput, TString>> byInput, TInput input, TString str)
            where TInput : class where TString : class {
            var stale = new List<TInput>();
            if (byInput == null) return stale;
            foreach (KeyValuePair<TInput, TString> kv in byInput)
                if (ReferenceEquals(kv.Value, str) && !ReferenceEquals(kv.Key, input) && kv.Key != null) stale.Add(kv.Key);
            return stale;
        }

        /// <summary>The string's current entry is replaced: there is one and it is not the new input.</summary>
        public static bool Replaces<TInput>(TInput? current, TInput input) where TInput : class => current != null && !ReferenceEquals(current, input);

        /// <summary>A grab of a string by someone other than its last holder: the bow starts fresh for the new holder.</summary>
        public static bool FreshBow(string? lastHolder, string holder) => lastHolder != null && !string.Equals(lastHolder, holder, StringComparison.Ordinal);

        /// <summary>Fix batch D (fixA-review I-3): on a handover, an input on the string goes unless it is the new holder's own
        /// (an input whose holder cannot be told goes too).</summary>
        public static bool Foreign<TPlayer>(TPlayer? owner, TPlayer holder) where TPlayer : class => owner == null || !ReferenceEquals(owner, holder);

        /// <summary>G3 (play-test 8, diagnosis 3): an input on a string is still wanted only when it is the holder's own, some skill
        /// still uses it, and the holder still owns that skill (a level above 0, a tried orb included). Anything else is left over
        /// from a sale or a handover and goes.</summary>
        public static bool Wanted(bool holderIsOwner, bool usedBySkill, bool skillOwned) => holderIsOwner && usedBySkill && skillOwned;

        /// <summary>G3: after the game's HeldPickupHandler.DeregisterSkill, the input it unhooked is cleared (unsubscribed from the
        /// string) when no other skill on that pickup still uses it and the game did not clear it itself (it does only for an active
        /// one).</summary>
        public static bool ClearAfterDeregister(bool stillUsed, bool gameCleared) => !stillUsed && !gameCleared;
    }

    /// <summary>G3 (play-test 8, diagnosis 5): the combat dummy ('Fake Player combat_dummy') has health but no stagger pool, so the
    /// mod's targeting never saw it as a creature. With [Targets] DummiesCount on, a thing with health whose prefab name contains one
    /// of [Targets] DummyNames counts as a creature for the mod's own targeting (never for stagger or interrupt).</summary>
    internal static class DummyRule {
        public const string DefaultNames = "combat_dummy";
        static readonly char[] Separators = { ';', ',' };

        /// <summary>The prefab name contains one of the names (';' or ',' between them, blanks ignored), ignoring case.</summary>
        public static bool Matches(string? prefabName, string? names) {
            if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(names)) return false;
            foreach (string raw in names!.Split(Separators)) {
                string n = raw.Trim();
                if (n.Length > 0 && prefabName!.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>A dummy counts: the setting is on, it is not a player, it has no stagger pool (one with a pool is a creature
        /// anyway) and its name matches.</summary>
        public static bool Counts(bool enabled, bool player, bool hasPool, bool nameMatches) => enabled && !player && !hasPool && nameMatches;
    }

    /// <summary>G3 (play-test 8, diagnosis 11, Debug only): each hand-made hit on a creature logs its rigidbody, and again
    /// <see cref="LaterSeconds"/> later when it is not finite or has moved more than <see cref="MovedMetres"/>.</summary>
    internal static class HitWatchRule {
        public const float LaterSeconds = 0.5f, MovedMetres = 20f;
        public static bool Finite(float x, float y, float z) =>
            !float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z) && !float.IsInfinity(x) && !float.IsInfinity(y) && !float.IsInfinity(z);
        public static bool ReportLater(bool finiteNow, float moved, float threshold) => !finiteNow || float.IsNaN(moved) || moved > threshold;
    }

    /// <summary>Fix batch D (diagnosis-holy.md): how long the server's copy of a held string may read at or below the draw
    /// threshold before a bow skill is lost ([Draw] ServerDipGraceSeconds, server only). The client's ring keeps its own rule.</summary>
    internal static class DipGraceRule {
        public const float DefaultServerSeconds = 0.35f;

        /// <summary>The setting as used: a broken value is the shipped one, a negative one 0 (the game's instant cancel).</summary>
        public static float Server(float seconds) => float.IsNaN(seconds) || float.IsInfinity(seconds) ? DefaultServerSeconds : seconds < 0f ? 0f : seconds;
    }

    /// <summary>Fix batch D: the server's snap detector. A held string's pull that falls from above the draw threshold to under
    /// <see cref="Floor"/> within one update is logged, at most once per string per <paramref name="every"/> seconds.</summary>
    internal static class PullSnap {
        public const float Floor = 0.3f;
        public static bool Fell(float before, float now, float threshold, float floor = Floor) => before > threshold && now < floor;
        public static bool Due(float lastLogged, float now, float every = 60f) => float.IsNaN(lastLogged) || now - lastLogged >= every;
    }

    /// <summary>The echo fix: the server times a shot from the shooter's claimed milliseconds when the claim is ahead of its own time
    /// by at most the trust limit (the server sees the pull late), never from a saturated or far-ahead claim.</summary>
    internal static class ClaimTrust {
        public const int DefaultTrustMs = 1000;

        public static int? EffectiveMs(int? serverMs, int? claimedMs, int trustMs) {
            if (trustMs <= 0 || !claimedMs.HasValue || claimedMs.Value < 0) return serverMs;
            int c = claimedMs.Value;
            if (c >= ShotClaimWire.TimeMax) return serverMs;
            if (!serverMs.HasValue) return c <= trustMs ? c : (int?)null;
            if (c < serverMs.Value) return serverMs;
            return c - serverMs.Value <= trustMs ? c : serverMs;
        }

        public static string Why(int? serverMs, int? claimedMs, int trustMs) {
            if (trustMs <= 0) return "trust is off";
            if (!claimedMs.HasValue || claimedMs.Value < 0) return "no claimed time";
            int c = claimedMs.Value;
            if (c >= ShotClaimWire.TimeMax) return $"the client's time is saturated (at least {ShotClaimWire.TimeMax} ms); the server's own";
            if (!serverMs.HasValue)
                return c <= trustMs ? "the server saw no crossing; the client's claim is trusted"
                                    : $"the server saw no crossing; the client's {c} ms is over {trustMs} ms";
            int s = serverMs.Value;
            if (c < s) return $"the server's own (the client is {s - c} ms behind)";
            if (c - s <= trustMs) return $"the client's time (ahead {c - s} ms)";
            return $"the server's own (the client is {c - s} ms ahead, over {trustMs} ms)";
        }

        /// <summary>The server's own Quickdraw input lags the shooter's, so it also accepts a claimed Snap whose time was the one
        /// trusted and lies inside Quickdraw's window (give or take the claim tolerance).</summary>
        public static bool SnapReady(bool serverSnap, ShotKind? claimedKind, int? claimedMs, int? effectiveMs, int windowMs) =>
            serverSnap || (claimedKind == ShotKind.Snap && claimedMs.HasValue && effectiveMs == claimedMs
                           && claimedMs.Value <= windowMs + ClaimRules.ToleranceMs);
    }

    /// <summary>Claims by arrow id, kept for <see cref="Lifetime"/> seconds. Reading never removes one: the relay and every
    /// repeat serialisation must see the same claim.</summary>
    internal sealed class ClaimStore {
        public const float Lifetime = 2f;
        public const int Cap = 256;
        readonly Dictionary<uint, KeyValuePair<ShotClaim, float>> map = new Dictionary<uint, KeyValuePair<ShotClaim, float>>();

        public int Count => map.Count;

        public void Put(uint arrowId, ShotClaim claim, float now) {
            if (map.Count >= Cap && !map.ContainsKey(arrowId)) {
                var old = new List<uint>();
                foreach (var kv in map) if (now - kv.Value.Value > Lifetime) old.Add(kv.Key);
                foreach (uint k in old) map.Remove(k);
                if (map.Count >= Cap) map.Clear();
            }
            map[arrowId] = new KeyValuePair<ShotClaim, float>(claim, now);
        }

        public bool TryGet(uint arrowId, float now, out ShotClaim? claim) {
            claim = null;
            if (!map.TryGetValue(arrowId, out var e) || now - e.Value > Lifetime) return false;
            claim = e.Key;
            return true;
        }

        public void Remove(uint arrowId) => map.Remove(arrowId);
    }

    internal static class SettingsChecksum {
        public static string Format(object? value) {
            if (value == null) return "";
#if QUEST
            // Quest (.NET 8): "R" is the shortest text that reads back, where the PC game and the server (Mono) write 7 digits and
            // 9 only when 7 do not read back. Only [HolyWindUp] CordShare (1/3) came out differently ("0.33333334" against
            // "0.333333343"), but that made every Quest game's settings check differ from the server's (2026-11-25). Mono's rule:
            if (value is float f) { string s = f.ToString("G7", CultureInfo.InvariantCulture); return float.Parse(s, CultureInfo.InvariantCulture) == f ? s : f.ToString("G9", CultureInfo.InvariantCulture); }
            if (value is double d) { string s = d.ToString("G15", CultureInfo.InvariantCulture); return double.Parse(s, CultureInfo.InvariantCulture) == d ? s : d.ToString("G17", CultureInfo.InvariantCulture); }
#else
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString("R", CultureInfo.InvariantCulture);
#endif
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        /// <summary>FNV-1a 32 over the UTF-8 bytes of "key=value\n", pairs sorted by key (ordinal).</summary>
        public static uint Compute(IEnumerable<KeyValuePair<string, string>> settings) {
            var list = new List<KeyValuePair<string, string>>(settings);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            uint h = 2166136261u;
            foreach (var kv in list)
                foreach (byte b in Encoding.UTF8.GetBytes(kv.Key + "=" + kv.Value + "\n")) { h ^= b; h *= 16777619u; }
            return h;
        }

        public static byte Short(uint c) => (byte)(c ^ (c >> 8) ^ (c >> 16) ^ (c >> 24));
    }

    /// <summary>The arrow flight-speed field as the game sends it: SerializeFloat(0, 1000, 0.01).</summary>
    internal static class SpeedWire {
        public static float RoundTrip(float speed) {
            float range = 1000f; uint steps = (uint)(range / 0.01f) + 1;           // 100001, 17 bits
            uint q = 0; if (speed > 0f) { q = (uint)(speed / range * (float)steps + 0.5f); if (q > steps) q = steps; }
            return (float)q / (float)steps * range;
        }

        public static int Hundredths(float speed) => (int)Math.Round(speed * 100f);

        public static bool IsMarker(float received, float marker) => Hundredths(received) == Hundredths(RoundTrip(marker));
    }

    /// <summary>Per-key settings migration (0.8.0): a setting still at its old shipped default moves to the new one,
    /// a hand-tuned value is kept. The shipped defaults of the last run are recorded in a file next to the settings.</summary>
    internal static class SettingsMigration {
        // H4 (H23-review Fable I-1): texts this mod once shipped as a key's default and no longer does, matched EXACTLY (an entry equal to
        // one of them is the mod's old text, never a user's). Read from every older Tutorial.cs under .superpowers\sdd (snap-0 .. snap-4
        // and the batches before); the real install holds five of them (Burst, AutoDock, Ricochet, SeekingVolley, Shatter), while its
        // shipped_defaults.txt recorded only the first line of the CURRENT defaults (the pre-H3 format), so no rule on that file can
        // see them. H3's first-line rule is gone: it reset nothing there and could reset a user's edited second line.
        static readonly Dictionary<string, string[]> retired = new Dictionary<string, string[]>(StringComparer.Ordinal) {
            { "Tutorial.AutoDock", new[] { "Wear a quiver with arrows in it\nand shoot once." } },
            { "Tutorial.Burst", new[] { "Pull fully and hold 2 seconds\nuntil the click, then shoot." } },
            { "Tutorial.Ricochet", new[] { "Pull fully and hold until fifteen\nghost arrows show, then shoot." } },
            { "Tutorial.SeekingVolley", new[] {
                "Pull fully and keep your aim on the target\nuntil it locks, then shoot.",
                "Hold a volley and shoot to try it. To lock: rest your aim\non a creature until its green ring closes, then shoot near it." } },
            { "Tutorial.Shatter", new[] {
                "Hit the target with a charged shot\nor with ghost arrows.",
                "Pull fully and hold until the thump,\nthen hit the target.",
                "Hold a Charge Shot until the thump,\nthen shoot.",
                "Hold a Charge Shot until the thump, then shoot:\nthe hit blasts out behind the creature." } },
        };

        /// <summary>H4: the value is a text this mod shipped for this key and no longer does (exact, Windows line ends allowed).</summary>
        public static bool IsRetired(string key, string current) {
            if (!retired.TryGetValue(key ?? "", out string[] texts)) return false;
            string c = (current ?? "").Replace("\r\n", "\n");
            foreach (string t in texts) if (string.Equals(c, t, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>A setting moves to its new default when it still holds the default recorded last run (shipped_defaults.txt) or, H4,
        /// a text this mod shipped for it before (<see cref="IsRetired"/>); anything else is the user's and stays.</summary>
        public static bool ShouldReset(string key, string current, string? oldDefault, string newDefault) {
            if (string.Equals(current, newDefault, StringComparison.Ordinal)) return false;
            if (oldDefault != null && string.Equals(current, oldDefault, StringComparison.Ordinal)) return true;
            return IsRetired(key, current);
        }

        public static bool ShouldReset(string current, string? oldDefault, string newDefault) => ShouldReset("", current, oldDefault, newDefault);

        /// <summary>H3: a value on one line of the defaults file: backslashes doubled, line breaks as \n (\r as \r).</summary>
        public static string Escape(string v) => (v ?? "").Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");

        public static string Unescape(string v) {
            if (v.IndexOf('\\') < 0) return v;
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++) {
                char c = v[i];
                if (c == '\\' && i + 1 < v.Length) {
                    char n = v[++i];
                    sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Format(IDictionary<string, string> defaults) {
            var keys = new List<string>(defaults.Keys);
            keys.Sort(string.CompareOrdinal);
            var sb = new StringBuilder();
            foreach (string k in keys) sb.Append(k).Append('=').Append(Escape(defaults[k])).Append('\n');
            return sb.ToString();
        }

        public static Dictionary<string, string> Parse(string text) {
            var map = new Dictionary<string, string>();
            foreach (string raw in text.Split('\n')) {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq)] = Unescape(line.Substring(eq + 1));
            }
            return map;
        }

        /// <summary>The shipped default this key had last run (shipped_defaults.txt); null when none was recorded.</summary>
        public static string? OldDefault(string key, IDictionary<string, string>? recorded) =>
            recorded != null && recorded.TryGetValue(key, out string? v) ? v : null;

    }
}
