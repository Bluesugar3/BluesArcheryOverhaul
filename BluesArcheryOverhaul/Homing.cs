using System;
using System.Collections.Generic;
using System.Globalization;

// Unity-free. Batch H1 (spec docs/superpowers/specs/2026-11-18-homing-arrow-design.md; its last sections are the owner's decisions and
// win): Seeking Volley, the homing volley, the multi-arrow line's legendary. Compiled by BluesArcheryOverhaul.Tests, so only System.* in here.
//   - the lock (LockRule, LockSet): the shooter's own view, while it holds a volley past the 68% crossing; up to MaxLocks creatures,
//     each locked by keeping the aim on it for its dwell, the first the main lock; one lock per draw.
//   - the claim (LockClaim): the locks lead the shot claim's target list, their number rides the reserved window bits with the
//     Steered bit (Wire.cs); the server checks each one (LockCheck) and relays what it kept.
//   - which arrow homes on which lock (HomingAssign), and the flight itself (HomingFlight): straight on its own line for the bloom,
//     then turning at a capped rate toward a point that makes it come in from its own side, the same on every machine.
namespace BluesArcheryOverhaul {
    /// <summary>[SeekingVolley] numbers (Seek.cs reads them from the settings; the defaults are the design's). A plain class: the
    /// game side builds one per settings load and never changes it after.</summary>
    internal sealed class HomingValues {
        // the lock (shooter; the server re-checks with its slack)
        public float LockMinMetres = 3f, LockRangeMetres = 60f;
        // H2: 0.45 s to 0.9 s (were 0.6 and 1.2; the owner after playing 0.17.0: "have locking on be a little quicker")
        public float DwellNearSeconds = 0.45f, DwellNearMetres = 10f, DwellFarSeconds = 0.9f, DwellFarMetres = 50f, DwellFasterPerOrb = 0.15f;
        public int MaxLocks = 4;
        public float LockConeDegrees = 5f, LockConeMetres = 0.6f, StillDegreesPerSecond = 30f, DrainFactor = 2f, TakeoverSeconds = 0.2f, SearchHz = 5f;
        public float BreakOffDegrees = 15f, BreakOffSeconds = 0.3f, SightLostSeconds = 0.5f, BreakMetres = 75f;
        // the release and the server's check
        public float ReleaseConeDegrees = 25f, ServerToleranceDegrees = 10f, ServerRangeMetres = 5f, ServerDwellSlackSeconds = 0.15f;
        // the flight
        public float BloomShare = 0.3f, InnerDegrees = 20f, OuterDegrees = 35f, PelletDegrees = 50f, RingGapSeconds = 0.04f;
        // H3: drift 25 m (was 10; the owner: "can it kind of follow it if it moves?")
        public float TurnDegreesPerSecond = 1080f, LetGoDriftMetres = 25f, MaxLifeSeconds = 4f;
        // H3 (diagnosis-misses.md 4.2, 4.4; H1-review M-1): the straight bloom at most this long; the side offset of the shaping
        // point at most this far off the line; a let-go arrow ends this long after it let go; the real arrow is never placed
        // further than PlaceMaxMetres from the shooter; a moving creature is led by its velocity over at most LeadMaxSeconds
        public float BloomMaxMetres = 3f, SideMaxMetres = 4f, LetGoSeconds = 1f, PlaceMaxMetres = 80f, LeadMaxSeconds = 0.8f;
        // the core cluster that takes the real arrow's place
        public int CoreArrows = 5;
        /// <summary>H4 (H23-review-opus minor 2): the core cluster's size from [SeekingVolley] CoreArrows, 1 to 12: with none no core
        /// arrow ever ends, so the real arrow would be held to its time-out and dropped at the bow, with no stand-in hit.</summary>
        public static int CoreCount(int setting) => setting < 1 ? 1 : setting > 12 ? 12 : setting;
        public float CoreShare = 0.2f;
        // H5 (the owner, 2026-11-18: "each ghost arrow will try to hit every single target that gets locked onto"): which arrows chain
        // through the release's locks ([SeekingVolley] ChainLocks), and the longest a chaining arrow flies in all
        public ChainMode ChainLocks = ChainMode.Ghosts;
        public float ChainMaxLifeSeconds = 8f;
        // 0.19.11 (the owner: "homing doesn't work on players"): other players can be locked ([SeekingVolley] LockPlayers)
        public bool LockPlayers = true;

        /// <summary>H5: the longest any homing arrow of a release can fly: MaxLifeSeconds, or ChainMaxLifeSeconds when arrows chain.</summary>
        public float FlightSeconds => ChainLocks == ChainMode.Off ? MaxLifeSeconds : Math.Max(MaxLifeSeconds, ChainMaxLifeSeconds);

        public static HomingValues Default => new HomingValues();
    }

    /// <summary>H5: which homing arrows chain through the locks: none (as before H5), the ghosts (inner and outer rings), or the ghosts
    /// and Burst's pellets. The core arrows never chain (they stand in for the real arrow and end at the main lock).</summary>
    internal enum ChainMode { Off, Ghosts, All }

    /// <summary>H5, Seeking Volley's chain (the owner: "Have homing bounce each ghost arrow to each target that is locked onto"): a chaining
    /// arrow that lands on one of the release's locks while it still homes deals its hit as any landing and flies on to the nearest living
    /// lock it has not struck yet, homing; it ends once it has struck every living lock, has none left, or its life is over. The same on
    /// every machine from the relayed lock order, the alive flags and where each machine sees the locks.</summary>
    internal static class HomingChain {
        /// <summary>The struck set is a bitmask: at most this many locks (MaxLocks is at most 4).</summary>
        public const int MaxBits = 8;
        /// <summary>Seconds of life a chain leg gets beyond the flight's own estimate of its way to the lock (as Launch's).</summary>
        public const double LegSlackSeconds = 0.3;

        /// <summary>[SeekingVolley] ChainLocks: "off", "ghosts" or "all" (any case, spaces ignored); anything else is the default, ghosts,
        /// with <paramref name="known"/> false.</summary>
        public static ChainMode Parse(string? text, out bool known) {
            string s = (text ?? "").Trim().ToLowerInvariant();
            known = true;
            switch (s) {
                case "off": case "none": return ChainMode.Off;
                case "ghosts": case "ghost": return ChainMode.Ghosts;
                case "all": return ChainMode.All;
                default: known = false; return ChainMode.Ghosts;
            }
        }

        public static string Name(ChainMode m) => m == ChainMode.Off ? "off" : m == ChainMode.All ? "all" : "ghosts";

        /// <summary>This arrow chains: never a core arrow; the inner and outer ghosts unless ChainLocks is off; Burst's pellets only with
        /// "all" (needles and echoes never home).</summary>
        public static bool Chains(ChainMode mode, SpectralKind kind, bool core) {
            if (core || mode == ChainMode.Off) return false;
            if (kind == SpectralKind.Inner || kind == SpectralKind.Outer) return true;
            return mode == ChainMode.All && kind == SpectralKind.Pellet;
        }

        public static bool IsStruck(int mask, int index) => index >= 0 && index < MaxBits && (mask & (1 << index)) != 0;
        public static int Mark(int mask, int index) => index >= 0 && index < MaxBits ? mask | (1 << index) : mask;

        /// <summary>The number of locks in <paramref name="mask"/>.</summary>
        public static int Count(int mask) { int n = 0; for (int i = 0; i < MaxBits; i++) if ((mask & (1 << i)) != 0) n++; return n; }

        /// <summary>The lock a chaining arrow at <paramref name="pos"/> flies to next: the nearest living lock (alive, a finite middle) it
        /// has not struck (<paramref name="struck"/>), ties to the lower index; -1 with none. Unlike HomingAssign.Retarget a lock behind
        /// it or close by is allowed: the arrow turns round at its normal rate.</summary>
        public static int Next(HVec pos, HomingTarget[] targets, int struck) {
            if (targets == null || !pos.Finite) return -1;
            // H5 review I-2: two passes, so the pick is the lowest index within TieMetres of the nearest whatever the order: every machine
            // sees the creatures a little differently, and a near tie must still go the same way on all of them
            double nearest = double.MaxValue;
            for (int i = 0; i < targets.Length && i < MaxBits; i++) {
                if (IsStruck(struck, i) || !targets[i].Alive || !targets[i].Pos.Finite) continue;
                double d = (targets[i].Pos - pos).Length;
                if (d < nearest) nearest = d;
            }
            if (nearest == double.MaxValue) return -1;
            for (int i = 0; i < targets.Length && i < MaxBits; i++) {
                if (IsStruck(struck, i) || !targets[i].Alive || !targets[i].Pos.Finite) continue;
                if ((targets[i].Pos - pos).Length <= nearest + TieMetres) return i;
            }
            return -1;
        }

        /// <summary>H5 review I-2: locks this close in distance count as a tie (the lower index wins).</summary>
        public const double TieMetres = 0.25;

        /// <summary>H5 review I-2: where the next lock is measured from: lock <paramref name="index"/>'s middle (a relayed fact, about the
        /// same on every machine) when it is known here, else the arrow's own place.</summary>
        public static HVec From(HomingTarget[] targets, int index, HVec arrow) =>
            targets != null && index >= 0 && index < targets.Length && targets[index].Pos.Finite ? targets[index].Pos : arrow;

        /// <summary>How far outside the circle an arrow at <paramref name="pos"/> heading <paramref name="dir"/> (unit) turns on toward
        /// <paramref name="aim"/> the point is, in turning radii (<paramref name="radius"/> = speed / turn rate) from that circle's
        /// centre: below 1 it is inside and turning toward it only circles it; dead ahead is as far out as can be.</summary>
        public static double Clearance(HVec pos, HVec dir, HVec aim, double radius) {
            HVec to = aim - pos;
            double along = HVec.Dot(to, dir);
            HVec perp = to - dir * along;
            double pl = perp.Length, r = Math.Max(1e-9, radius);
            if (pl < 1e-9) return along > 0 ? double.MaxValue : Math.Sqrt(1 + along * along / (r * r));
            return (aim - (pos + perp * (r / pl))).Length / r;
        }

        /// <summary>The point is outside the circle the arrow turns on toward it (Clearance at least 1), or dead ahead.</summary>
        public static bool Reachable(HVec pos, HVec dir, HVec aim, double radius) => Clearance(pos, dir, aim, radius) >= 1 - 1e-9;

        /// <summary>The steepest a chain leg dives and climbs, degrees off level: a chain leg flies about level from creature to creature
        /// (its first strike can be anywhere on a body, often low on a small one, and a steep dive there ends in the ground).</summary>
        public const double DiveDegrees = 12.0, ClimbDegrees = 25.0;
        /// <summary>A chain leg going round to its lock starts round once the lock is this many turning radii from the circle's centre,
        /// and goes on straight again only if it falls inside this many (the gap keeps a lock near the circle's edge from flipping it
        /// between the two every step: a step is a turn and then a straight move, not an arc).</summary>
        public const double RoundFrom = 1.25, StraightBelow = 0.9;
        /// <summary>A chain leg has turned onto its lock (the ordinary homing flight and its Behind rule take over) once it can reach it
        /// and heads within 45 degrees of it (a leg that could reach a lock 85 degrees off spent its turn levelling out and then had it
        /// behind it).</summary>
        public static readonly double FacedCos = Math.Cos(45.0 * Math.PI / 180.0);
        const double Deg = Math.PI / 180.0;

        /// <summary>Where a chain leg heads this step while it is still turning onto its lock (HomingArrow.Turning). It starts inside the
        /// creature it struck with its next lock beside or behind it:
        ///   - the lock is in front of it and outside the circle it turns on: straight for it (<paramref name="onto"/>; from here the leg
        ///     is an ordinary homing flight, still about level: Level);
        ///   - the lock is inside or near that circle (turning toward it would only circle it): on along its own heading until the lock
        ///     is RoundFrom turning radii out;
        ///   - then round toward the lock's bearing (a turn in the plane of a diving arrow and a lock behind it would go down into the
        ///     ground) until it faces it, unless the lock falls inside StraightBelow again.
        /// Both of the last two level out toward the lock's height (Level).</summary>
        public static HVec TurnRound(ref HomingArrow a, HVec aim, double radius, out bool onto) {
            HVec to = aim - a.Pos, dir = a.Dir;
            double d = to.Length;
            onto = false;
            if (!(d > 1e-6)) return dir;
            double clear = Clearance(a.Pos, dir, aim, radius);
            if (clear >= 1 - 1e-9 && HVec.Dot(dir, to) > 0) { onto = true; return to * (1.0 / d); }
            a.Round = a.Round ? clear >= StraightBelow : clear >= RoundFrom;
            HVec flat = a.Round ? new HVec(to.X, 0, to.Z) : new HVec(dir.X, 0, dir.Z);
            if (flat.Length < 1e-6) flat = new HVec(dir.X, 0, dir.Z);
            if (flat.Length < 1e-6) flat = new HVec(to.X, 0, to.Z);
            if (flat.Length < 1e-6) return to * (1.0 / d);   // straight above or below it, heading straight up or down: for it
            double across = Math.Max(Math.Max(0.0, radius), new HVec(to.X, 0, to.Z).Length);
            HVec want = Level(flat.Normalized * across + new HVec(0, to.Y, 0));
            if (HVec.Dot(want, dir) < -0.9999) want = (want + new HVec(0, 0.05, 0)).Normalized;   // never exactly against it (no plane to turn in)
            return want;
        }

        /// <summary>A chain leg's turn of at most <paramref name="max"/> radians from <paramref name="from"/> toward <paramref name="want"/>
        /// (both unit): its climb or dive first (toward want's, never steeper than Level allows), then its heading round with what is left.
        /// A plain turn along the great circle from a diving arrow to a level heading half round deepens the dive half-way (the H5 tests'
        /// arrows struck low on a small creature went into the ground right after).</summary>
        public static HVec LevelTurn(HVec from, HVec want, double max) {
            if (!(max > 0)) return from;
            double f0 = Math.Sqrt(from.X * from.X + from.Z * from.Z), f1 = Math.Sqrt(want.X * want.X + want.Z * want.Z);
            if (f0 < 1e-9 || f1 < 1e-9) return Level(want);
            double e0 = Math.Atan2(from.Y, f0), e1 = Math.Atan2(want.Y, f1);
            double held = Math.Max(-DiveDegrees * Deg, Math.Min(ClimbDegrees * Deg, e0));
            double de = Math.Max(-max, Math.Min(max, e1 - e0));
            if (Math.Abs(held - e0) > Math.Abs(de)) de = held - e0;   // steeper than allowed (a dive into a strike): out of it at once
            double rest = Math.Max(0.0, max - Math.Abs(de));
            double h0 = Math.Atan2(from.X, from.Z), h1 = Math.Atan2(want.X, want.Z);
            double dh = h1 - h0;
            while (dh > Math.PI) dh -= 2 * Math.PI;
            while (dh < -Math.PI) dh += 2 * Math.PI;
            double h = h0 + Math.Max(-rest, Math.Min(rest, dh)), e = e0 + de;
            return new HVec(Math.Sin(h) * Math.Cos(e), Math.Sin(e), Math.Cos(h) * Math.Cos(e));
        }

        /// <summary>A chain leg's heading held within DiveDegrees below and ClimbDegrees above level (unit; straight up or down is left
        /// as it is: nothing to level).</summary>
        public static HVec Level(HVec want) {
            HVec w = want.Normalized;
            double flat = Math.Sqrt(w.X * w.X + w.Z * w.Z);
            if (flat < 1e-9) return w;
            double e = Math.Atan2(w.Y, flat), held = Math.Max(-DiveDegrees * Deg, Math.Min(ClimbDegrees * Deg, e));
            if (held == e) return w;
            return new HVec(w.X / flat * Math.Cos(held), Math.Sin(held), w.Z / flat * Math.Cos(held));
        }

        /// <summary>Every living lock has been struck (true with none living).</summary>
        public static bool AllStruck(int mask, HomingTarget[] targets) {
            if (targets == null) return true;
            for (int i = 0; i < targets.Length && i < MaxBits; i++) if (targets[i].Alive && !IsStruck(mask, i)) return false;
            return true;
        }

        /// <summary>A chaining arrow that still homes landed on lock <paramref name="index"/> at <paramref name="at"/>: that lock is struck
        /// and it starts its next leg from there (true: it flies on, through the creature), or it has none left (false: it ends there, as
        /// any landing). An arrow that does not chain, or has let go, only has the strike counted.</summary>
        public static bool Strike(ref HomingArrow a, int index, HVec at, HomingTarget[] targets, HomingValues v) {
            if (index >= 0 && index < MaxBits && !IsStruck(a.Struck, index)) { a.Struck = Mark(a.Struck, index); a.Hits++; }
            if (!a.Chain || a.Let != LetGo.None) return false;
            if (at.Finite) a.Pos = at;
            int next = Next(From(targets, index, a.Pos), targets, a.Struck);
            if (next < 0) return false;
            Leg(ref a, targets, next, v);
            return true;
        }

        /// <summary>A new chain leg toward lock <paramref name="next"/>: a fresh frame and the room round that lock, no bloom (it turns at
        /// once), its speed and its side kept, the drift measured from where the lock is now, and the life to get there (Launch's
        /// estimate plus LegSlackSeconds), never past ChainMaxLifeSeconds from the release. The Behind rule waits until it has turned to
        /// face the lock (Turning).</summary>
        public static void Leg(ref HomingArrow a, HomingTarget[] targets, int next, HomingValues v) {
            HomingTarget t = targets[next];
            a.Lock = next;
            a.TargetAtLaunch = t.Pos;
            a.Room = t.Room;
            HVec line = t.Pos - a.Pos;
            double dist = line.Finite ? line.Length : 0;
            HomingFlight.Reframe(ref a, dist > 1e-6 ? line * (1.0 / dist) : a.Dir);
            a.Bloom = a.Travelled;
            a.Turning = true;
            a.Legged = true;
            a.Round = false;
            a.ApproachRad = 0;   // a chain leg comes straight in: the side approach is the volley's fan round its first lock
            a.Life = Math.Min(Math.Max(0.0, v.ChainMaxLifeSeconds), a.Age + LegSeconds(a.Pos, a.Dir, t.Pos, a.Speed, v));
        }

        /// <summary>The life a chain leg from <paramref name="pos"/> heading <paramref name="dir"/> gets to reach a lock at
        /// <paramref name="target"/>: Launch's estimate of its way (straight in) plus LegSlackSeconds. Its way is the distance when it can
        /// turn onto the lock now (in front, outside the circle it turns on); else one whole turning circle more (TurnRound: on along its
        /// own heading, then round): a leg that had only the distance ran out of life half-way round a lock beside it.</summary>
        public static double LegSeconds(HVec pos, HVec dir, HVec target, double speed, HomingValues v) {
            HVec line = target - pos;
            double dist = line.Finite ? line.Length : 0;
            double radius = speed / Math.Max(1e-6, Math.Max(0.0, v.TurnDegreesPerSecond) * Deg);
            double way = HVec.Dot(dir, line) > 0 && Reachable(pos, dir, target, radius) ? dist : dist + 2 * Math.PI * radius;
            return HomingFlight.Estimate(way, 0, speed, v) + LegSlackSeconds;
        }
    }

    internal static class LockRule {
        /// <summary>Seconds the aim must rest on a creature <paramref name="distance"/> metres away to lock it: DwellNearSeconds at
        /// DwellNearMetres or nearer, rising in a straight line to DwellFarSeconds at DwellFarMetres and beyond, shorter by
        /// DwellFasterPerOrb per Quickdraw orb (at most three count; never below a quarter). A broken distance is the far dwell.</summary>
        public static float Dwell(float distance, int quickOrbs, HomingValues v) {
            float d;
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance >= v.DwellFarMetres) d = v.DwellFarSeconds;
            else if (distance <= v.DwellNearMetres || !(v.DwellFarMetres > v.DwellNearMetres)) d = v.DwellNearSeconds;
            else d = v.DwellNearSeconds + (v.DwellFarSeconds - v.DwellNearSeconds) * (distance - v.DwellNearMetres) / (v.DwellFarMetres - v.DwellNearMetres);
            int orbs = Math.Max(0, Math.Min(Complements.MaxOrbs, quickOrbs));
            float k = Math.Max(Complements.FloorFactor, 1f - (float.IsNaN(v.DwellFasterPerOrb) ? 0f : v.DwellFasterPerOrb) * orbs);
            return Math.Max(0f, d * k);
        }

        /// <summary>A creature may be locked from LockMinMetres to LockRangeMetres away.</summary>
        public static bool InRange(float d, HomingValues v) => !float.IsNaN(d) && d >= v.LockMinMetres && d <= v.LockRangeMetres;

        /// <summary>Half-width of the aim's cone at <paramref name="distance"/>, degrees: LockConeDegrees plus the angle LockConeMetres
        /// makes there (a near creature is wide in the view, a far one is not).</summary>
        public static float AimCone(float distance, HomingValues v) {
            double extra = distance > 0.01f && !float.IsInfinity(distance) ? Math.Atan(v.LockConeMetres / distance) * 180.0 / Math.PI : 90.0;
            return (float)(v.LockConeDegrees + extra);
        }

        /// <summary>The orb works: owned, and the multi-arrow line is the main path (off it the legendary does nothing).</summary>
        public static bool Applies(Build b) => (b.Ultimates & Ultimates.SeekingVolley) != 0 && b.Main == MainPath.Multi;

        /// <summary>A release that can home: a held (or charged) volley with ghosts, never an ultimate, a snap or an early release.</summary>
        public static bool Steerable(Resolution r) =>
            r.Ultimate == UltimateUse.None && r.Plan.Ghosts > 0 && (r.Kind == ShotKind.Held || r.Kind == ShotKind.Charged);

        /// <summary>This release homes on this machine: its claim (the shooter's own, the server's kept one, a spectator's relay) is
        /// steered with at least one lock, and the release is a volley.</summary>
        public static bool Homes(ShotClaim? c, Resolution r) => LockClaim.Of(c).Count > 0 && Steerable(r);
    }

    internal enum LockLoss { None, AimOff, Sight, Range, Dead, Gone }

    /// <summary>What the shooter's machine sees of one lock this update: how far its aim is off it (degrees), whether a sight ray
    /// reaches it, how far it is, whether it lives.</summary>
    internal readonly struct LockView {
        public readonly uint Id;
        public readonly float Angle, Distance;
        public readonly bool Visible, Alive;
        public LockView(uint id, float angle, bool visible, float distance, bool alive) { Id = id; Angle = angle; Visible = visible; Distance = distance; Alive = alive; }
    }

    /// <summary>The shooter's locks for one draw. Each creature the aim rests on (the search's candidate, already inside the aim's
    /// cone) fills while the bow is still; full after its dwell, it becomes a lock (the first is the main lock); looking away drains
    /// the fill DrainFactor times as fast; a different creature takes the fill over only once it has been the candidate for
    /// TakeoverSeconds. A lock is lost when it dies or goes (Dead, Gone), is past BreakMetres (Range), is out of sight for
    /// SightLostSeconds (Sight), or when the aim is more than BreakOffDegrees off every lock for BreakOffSeconds (AimOff, all of
    /// them). Ruling: that last clock does not run while the aim rests on another creature (it is being locked, or is a lock).</summary>
    internal sealed class LockSet {
        public readonly List<uint> Locks = new List<uint>();
        /// <summary>The locks lost in the last update, with why.</summary>
        public readonly List<KeyValuePair<uint, LockLoss>> LostNow = new List<KeyValuePair<uint, LockLoss>>();
        readonly Dictionary<uint, float> hiddenSince = new Dictionary<uint, float>();
        float offSince = float.NaN, pendingSince = float.NaN, fillDwell = 1f;
        uint pending;

        /// <summary>The creature being locked (0 = none) and how far (0 to 1).</summary>
        public uint Filling { get; private set; }
        public float Fill { get; private set; }
        /// <summary>The creature locked in the last update (0 = none).</summary>
        public uint GainedNow { get; private set; }

        public void Reset() {
            Locks.Clear(); LostNow.Clear(); hiddenSince.Clear();
            offSince = pendingSince = float.NaN; pending = 0u;
            Filling = 0u; Fill = 0f; GainedNow = 0u; fillDwell = 1f;
        }

        void Lose(int i, LockLoss why) {
            LostNow.Add(new KeyValuePair<uint, LockLoss>(Locks[i], why));
            hiddenSince.Remove(Locks[i]);
            Locks.RemoveAt(i);
        }

        /// <param name="candidate">The creature under the aim (0 = none), <paramref name="distance"/> metres away.</param>
        /// <param name="still">The bow turns slower than StillDegreesPerSecond.</param>
        /// <param name="views">This update's view of each lock (a lock missing here is gone on this machine).</param>
        /// <summary>H3 (H1-review I-2): one update counts at most this many seconds of the dwell (a hitch, or the time since the last
        /// draw on the first frame of a new one, can never fill a lock at once).</summary>
        public const float MaxStepSeconds = 0.1f;

        public void Update(float now, float dt, uint candidate, float distance, bool still, IList<LockView> views, int quickOrbs, HomingValues v) {
            GainedNow = 0u;
            LostNow.Clear();
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            if (dt > MaxStepSeconds) dt = MaxStepSeconds;

            // 1. each lock on its own: dead or gone, too far, out of sight too long
            for (int i = Locks.Count - 1; i >= 0; i--) {
                uint id = Locks[i];
                int k = -1;
                for (int j = 0; j < views.Count; j++) if (views[j].Id == id) { k = j; break; }
                if (k < 0) { Lose(i, LockLoss.Gone); continue; }
                LockView w = views[k];
                if (!w.Alive) { Lose(i, LockLoss.Dead); continue; }
                if (float.IsNaN(w.Distance) || w.Distance > v.BreakMetres) { Lose(i, LockLoss.Range); continue; }
                if (w.Visible) hiddenSince.Remove(id);
                else {
                    if (!hiddenSince.TryGetValue(id, out float since)) hiddenSince[id] = since = now;
                    if (now - since >= v.SightLostSeconds - 1e-5f) { Lose(i, LockLoss.Sight); continue; }
                }
            }

            // 2. the aim far off every lock: all of them go (never while the aim rests on a creature)
            bool inRange = candidate != 0u && LockRule.InRange(distance, v);
            if (Locks.Count == 0) offSince = float.NaN;
            else {
                bool near = candidate != 0u;
                for (int j = 0; j < views.Count; j++) { LockView w = views[j]; if (Locks.Contains(w.Id) && !(w.Angle > v.BreakOffDegrees)) { near = true; break; } } // H3 (M-6): no boxed enumerator
                if (near) offSince = float.NaN;
                else {
                    if (float.IsNaN(offSince)) offSince = now;
                    if (now - offSince >= v.BreakOffSeconds - 1e-5f) {
                        for (int i = Locks.Count - 1; i >= 0; i--) Lose(i, LockLoss.AimOff);
                        offSince = float.NaN;
                    }
                }
            }

            // 3. the fill
            bool full = Locks.Count >= Math.Max(0, v.MaxLocks);
            bool fresh = inRange && !full && !Locks.Contains(candidate);
            if (!fresh) { pending = 0u; pendingSince = float.NaN; Drain(dt, v); return; }
            if (Filling == 0u || Fill <= 0f) { Filling = candidate; Fill = 0f; pending = 0u; pendingSince = float.NaN; }
            if (candidate != Filling) {
                if (pending != candidate) { pending = candidate; pendingSince = now; }
                if (now - pendingSince >= v.TakeoverSeconds - 1e-5f) { Filling = candidate; Fill = 0f; pending = 0u; pendingSince = float.NaN; }
                else { Drain(dt, v); return; }
            }
            else { pending = 0u; pendingSince = float.NaN; }
            fillDwell = LockRule.Dwell(distance, quickOrbs, v);
            if (!still) return;
            Fill += fillDwell > 0f ? dt / fillDwell : 1f;
            if (Fill < 1f - 1e-5f) return;
            Locks.Add(Filling);
            GainedNow = Filling;
            Filling = 0u; Fill = 0f; offSince = float.NaN;
        }

        void Drain(float dt, HomingValues v) {
            if (Filling == 0u) return;
            Fill -= (fillDwell > 0f ? dt / fillDwell : 1f) * Math.Max(0f, v.DrainFactor);
            if (Fill <= 1e-6f) { Fill = 0f; Filling = 0u; }
        }
    }

    /// <summary>The server's view of one claimed lock: found (its own root id), a creature by the mod's test (a combat dummy too),
    /// a player, alive, metres from the shooter's head, and degrees between the release direction and the line to it.</summary>
    internal readonly struct LockFacts {
        public readonly bool Exists, Creature, Player, Alive;
        public readonly float Distance, Angle;
        public LockFacts(bool exists, bool creature, bool player, bool alive, float distance, float angle) {
            Exists = exists; Creature = creature; Player = player; Alive = alive; Distance = distance; Angle = angle;
        }
    }

    internal enum LockRefusal { None, Gone, NotCreature, Player, Dead, Range, Cone, Dwell, Duplicate, Cap }

    /// <summary>The server's check of a claim's locks, with the design's slack (it sees creatures 100 to 200 ms late).</summary>
    internal static class LockCheck {
        /// <summary>The claimed locks the server keeps, in the shooter's order, each once, at most MaxLocks: found, a creature (a player
        /// only with <paramref name="players"/>, 0.19.11), alive, within LockRangeMetres + ServerRangeMetres, within ReleaseConeDegrees + ServerToleranceDegrees of the
        /// release, and the shot's time since the 68% crossing (<paramref name="seconds"/>, the time the server decided with) at least
        /// the dwells of every lock up to this one less ServerDwellSlackSeconds each (locks fill one after another). No time: none.
        /// <paramref name="refused"/> (may be null) gets each refused id and why.</summary>
        public static List<uint> Keep(IList<uint> claimed, Func<uint, LockFacts> facts, float? seconds, int quickOrbs, HomingValues v,
                                      List<KeyValuePair<uint, LockRefusal>>? refused, bool players = false) {
            var kept = new List<uint>();
            var seen = new List<uint>();
            if (claimed == null) return kept;
            double needed = 0;
            int counted = 0;
            foreach (uint id in claimed) {
                LockRefusal why = LockRefusal.None;
                if (seen.Contains(id)) why = LockRefusal.Duplicate;
                LockFacts f = why == LockRefusal.None ? facts(id) : default;
                if (why == LockRefusal.None) {
                    seen.Add(id);
                    counted++;
                    needed += LockRule.Dwell(f.Exists ? f.Distance : v.DwellNearMetres, quickOrbs, v);
                    double slack = (double)v.ServerDwellSlackSeconds * counted;
                    if (!f.Exists) why = LockRefusal.Gone;
                    else if (f.Player ? !players : !f.Creature) why = f.Player ? LockRefusal.Player : LockRefusal.NotCreature;
                    else if (!f.Alive) why = LockRefusal.Dead;
                    else if (float.IsNaN(f.Distance) || f.Distance > v.LockRangeMetres + v.ServerRangeMetres + 1e-4f) why = LockRefusal.Range;
                    // play-test of 0.17.12: with chaining on only the first lock kept must be within the cone (the shooter puts the
                    // locks it aimed at first, LockClaim.ShooterKeep); the others are kept wherever they are
                    else if ((v.ChainLocks == ChainMode.Off || kept.Count == 0)
                             && (float.IsNaN(f.Angle) || f.Angle > v.ReleaseConeDegrees + v.ServerToleranceDegrees + 1e-4f)) why = LockRefusal.Cone;
                    else if (!seconds.HasValue || float.IsNaN(seconds.Value) || seconds.Value + 1e-4 < needed - slack) why = LockRefusal.Dwell;
                    else if (kept.Count >= Math.Max(0, v.MaxLocks)) why = LockRefusal.Cap;
                }
                if (why == LockRefusal.None) kept.Add(id);
                else refused?.Add(new KeyValuePair<uint, LockRefusal>(id, why));
            }
            return kept;
        }

        public static string Why(LockRefusal r) {
            switch (r) {
                case LockRefusal.Gone: return "not found";
                case LockRefusal.NotCreature: return "not a creature";
                case LockRefusal.Player: return "a player (LockPlayers off, or the server's PvP off)";
                case LockRefusal.Dead: return "dead";
                case LockRefusal.Range: return "out of range";
                case LockRefusal.Cone: return "outside the release cone";
                case LockRefusal.Dwell: return "the dwell had not passed";
                case LockRefusal.Duplicate: return "named twice";
                case LockRefusal.Cap: return "more than MaxLocks";
                default: return "kept";
            }
        }
    }

    /// <summary>The locks in the shot claim: Steered, the number of locks (Locks, carried in the reserved window bits as locks - 1)
    /// and the locks first in the target list, in lock order; Ricochet's and Shatter's names follow. A claim that is not steered
    /// has no locks, whatever else it carries.</summary>
    internal static class LockClaim {
        /// <summary>The locks this claim carries (the first Locks of its targets when steered), else an empty list.</summary>
        public static List<uint> Of(ShotClaim? c) {
            var l = new List<uint>();
            if (c == null || !c.Steered) return l;
            int n = Math.Min(c.LockCount, c.Targets.Count);
            for (int i = 0; i < n; i++) l.Add(c.Targets[i]);
            return l;
        }

        /// <summary>Shooter: the locks lead the claim's target list (set first, before any other name); steered when there is one.</summary>
        public static void Put(ShotClaim c, IList<uint> locks) {
            var l = new List<uint>();
            if (locks != null) foreach (uint id in locks) if (id != 0u && !l.Contains(id) && l.Count < ShotClaimWire.MaxLocks) l.Add(id);
            c.Targets.InsertRange(0, l);
            c.Steered = l.Count > 0;
            c.Locks = l.Count;
        }

        /// <summary>Adds <paramref name="others"/> after what is already there, each id once, until <paramref name="max"/>. With an
        /// empty list to start from this is exactly the list given (the claim without a lock is unchanged).</summary>
        public static void Merge(List<uint> into, IList<uint> others, int max) {
            if (others == null) return;
            foreach (uint id in others) {
                if (into.Count >= max) break;
                if (!into.Contains(id)) into.Add(id);
            }
        }

        /// <summary>Shooter, at release: the locks whose line is within ReleaseConeDegrees of the release direction
        /// (<paramref name="angles"/>, one per lock), in lock order, at most MaxLocks. The server checks the same with its slack.</summary>
        public static List<uint> ShooterKeep(IList<uint> locks, IList<float> angles, HomingValues v) {
            var l = new List<uint>();
            int max = Math.Min(ShotClaimWire.MaxLocks, Math.Max(0, v.MaxLocks));
            for (int i = 0; i < locks.Count && i < angles.Count; i++) {
                if (l.Count >= max) break;
                if (!float.IsNaN(angles[i]) && angles[i] <= v.ReleaseConeDegrees && !l.Contains(locks[i])) l.Add(locks[i]);
            }
            // play-test of 0.17.12 (the owner: "the homing stopped at one enemy"): with chaining on, a release aimed at one of its
            // locks keeps the others too, after the ones aimed at, so each ghost can strike every lock; aimed at none, it does not home
            if (v.ChainLocks != ChainMode.Off && l.Count > 0)
                for (int i = 0; i < locks.Count && i < angles.Count && l.Count < max; i++)
                    if (!l.Contains(locks[i])) l.Add(locks[i]);
            return l;
        }

        /// <summary>Server: the relayed claim of a steered shot from a build the orb works for: the kept locks first, then (only for a
        /// build that wants a target list, Ricochet or Shatter) the claim's other names that pass TargetRule.Keep, at most six in all;
        /// steered with Locks = the kept count, or not steered with none. Picks cleared, the claim read untouched.</summary>
        public static ShotClaim Server(ShotClaim got, IList<uint> kept, bool othersWanted, Func<uint, TargetFacts> facts, float range) {
            ShotClaim c = ClaimRules.ServerCheck(got);
            foreach (uint id in kept) if (c.Targets.Count < ShotClaimWire.MaxTargets && !c.Targets.Contains(id)) c.Targets.Add(id);
            c.Steered = c.Targets.Count > 0;
            c.Locks = c.Targets.Count;
            if (othersWanted) {
                int skip = Of(got).Count;
                for (int i = skip; i < got.Targets.Count; i++) {
                    uint id = got.Targets[i];
                    if (c.Targets.Count >= ShotClaimWire.MaxTargets) break;
                    if (c.Targets.Contains(id) || !TargetRule.Keep(facts(id), range)) continue;
                    c.Targets.Add(id);
                }
            }
            return c;
        }

        /// <summary>The release is not one that homes (no ghosts, a snap, a beam): the claim loses its lock; its lock ids stay as
        /// Ricochet's or Shatter's candidates only for a build that wants them.</summary>
        public static void Unsteer(ShotClaim c, bool othersWanted) {
            int n = Of(c).Count;
            c.Steered = false;
            c.Locks = 0;
            if (!othersWanted && n > 0) c.Targets.RemoveRange(0, n);
        }
    }

    /// <summary>Which lock each homing arrow flies to, the same on every machine from the relayed lock list alone: the core cluster
    /// to the main (first) lock; the ghosts and then the pellets dealt round the locks in turn, in arrow order.</summary>
    internal static class HomingAssign {
        public const float CoreApproachDegrees = 0f;

        /// <summary>The lock (index into the claim's locks) of the arrow at <paramref name="order"/>; -1 with no locks.</summary>
        public static int Lock(int order, int locks) => locks <= 0 ? -1 : ((order % locks) + locks) % locks;

        /// <summary>An arrow's place in the dealing order: ghosts by their index, the pellets after all the ghosts.</summary>
        public static int Order(SpectralKind kind, int index, int ghosts) => kind == SpectralKind.Pellet ? Math.Max(0, ghosts) + index : index;

        /// <summary>The ring an arrow lands with: 0 the inner ghosts and the core, 1 the outer ghosts, 2 the pellets.</summary>
        public static int Ring(SpectralKind kind, bool core) => core || kind == SpectralKind.Inner ? 0 : kind == SpectralKind.Outer ? 1 : 2;

        /// <summary>The angle a ring comes in at, off the line from the bow (InnerDegrees, OuterDegrees, PelletDegrees); the core
        /// flies straight in.</summary>
        public static float ApproachDegrees(int ring, bool core, HomingValues v) =>
            core ? CoreApproachDegrees : ring <= 0 ? v.InnerDegrees : ring == 1 ? v.OuterDegrees : v.PelletDegrees;

        /// <summary>How far a pellet may fly: Burst's range, or with a lock the lock's range (with the server's slack) (the owner:
        /// "Follow the lock to 60 m").</summary>
        public static float PelletRange(bool homing, float burstRange, HomingValues v) => homing ? v.LockRangeMetres + v.ServerRangeMetres : burstRange;

        /// <summary>H3 (diagnosis 4.3): the lock an arrow at <paramref name="pos"/> heading <paramref name="dir"/> turns to when its own
        /// (<paramref name="current"/>) has died or is lost: the nearest other living lock of the same release that is still ahead of it
        /// and within its turn ability (less than 90 degrees off its heading, and far enough to turn onto: the turn's arc fits in the
        /// distance). -1 with none (it lets go). Ties go to the lower index, so every machine fed the same alive flags agrees.</summary>
        public static int Retarget(HVec pos, HVec dir, double speed, HomingValues v, HomingTarget[] targets, int current) {
            if (targets == null) return -1;
            double turn = Math.Max(1e-6, Math.Max(0.0, v.TurnDegreesPerSecond) * Math.PI / 180.0);
            double radius = (speed > 0 && !double.IsInfinity(speed) ? speed : 0.1) / turn;
            HVec d = dir.Normalized;
            int best = -1; double bestDist = double.MaxValue;
            for (int i = 0; i < targets.Length; i++) {
                if (i == current || !targets[i].Alive || !targets[i].Pos.Finite) continue;
                HVec to = targets[i].Pos - pos;
                double dist = to.Length;
                if (dist < 1e-6) continue;
                double cos = HVec.Dot(d, to * (1.0 / dist));
                if (cos <= 0) continue;                                  // behind it
                double angle = Math.Acos(Math.Min(1.0, cos));
                if (dist < radius * angle) continue;                     // too close to turn onto
                if (dist < bestDist - 1e-9) { bestDist = dist; best = i; }
            }
            return best;
        }
    }

    /// <summary>H3: the free space round one lock, measured once at the release on every machine that flies the volley (Seek.Room: a
    /// few casts against the world): metres up from the creature's body, to its left and right (across the line from the bow), and up
    /// from the middle of the line from the shooter to it. Open ground is "plenty" everywhere.</summary>
    internal readonly struct HomingRoom {
        public readonly double Up, Left, Right, MidUp;
        public HomingRoom(double up, double left, double right, double midUp) { Up = up; Left = left; Right = right; MidUp = midUp; }
        public static HomingRoom Open => new HomingRoom(1e6, 1e6, 1e6, 1e6);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "up {0:0.0} m, left {1:0.0} m, right {2:0.0} m, mid-line up {3:0.0} m", Up, Left, Right, MidUp);
    }

    /// <summary>H3: one lock as a machine sees it this physics tick: its body's middle, its velocity (for the lead), whether it lives
    /// (or is known here at all), and the room round it measured at the release.</summary>
    internal struct HomingTarget {
        public HVec Pos, Vel;
        public bool Alive;
        public HomingRoom Room;
    }

    /// <summary>H3 (I-1, diagnosis 4.1, the caves): where each arrow's shaping point sits round its creature. Every arrow's side (its
    /// turn round the arrow) is folded into the upper half round the line from the bow (the angle from the horizontal right, phi, is
    /// halved: the ring keeps its order and stays evenly fanned over the upper half-circle), so on open ground no arrow aims below the
    /// creature. The offset is then held inside the measured room: its rise to the head-room (the smaller of up from the body and up
    /// from the middle of the line) less Margin, each side to that side's room less Margin; with no head-room the fan lies flat
    /// (sideways, as in a low tunnel), with no room at all every arrow flies the plain line.</summary>
    internal static class HomingFan {
        /// <summary>Metres kept clear of a ceiling or a wall.</summary>
        public const double Margin = 0.4;

        /// <summary>The horizontal right <paramref name="h"/> across <paramref name="line"/> (unit) and the up <paramref name="up"/>
        /// square to both (its upward side). A vertical line takes world x as its right.</summary>
        public static void Basis(HVec line, out HVec h, out HVec up) {
            HVec l = line.Normalized;
            h = HVec.Cross(new HVec(0, 1, 0), l);
            if (h.Length < 1e-6) h = new HVec(1, 0, 0);
            h = new HVec(h.X, 0, h.Z).Normalized;
            if (h.Length < 0.5) h = new HVec(1, 0, 0);
            up = HVec.Cross(l, h).Normalized;
            if (up.Y < 0) up = up * -1;
        }

        /// <summary>The folded angle (0 to pi, from the right through straight up) of a side round <paramref name="line"/>.</summary>
        public static double Fold(HVec side, HVec line) {
            Basis(line, out HVec h, out HVec up);
            double x = HVec.Dot(side, h), y = HVec.Dot(side, up);
            if (Math.Abs(x) < 1e-12 && Math.Abs(y) < 1e-12) return Math.PI / 2;
            double phi = Math.Atan2(y, x);
            if (phi < 0) phi += 2 * Math.PI;
            return phi / 2;
        }

        /// <summary>The shaping point's offset across the line for folded angle <paramref name="phi"/> and wanted size
        /// <paramref name="size"/> (metres): <paramref name="oh"/> along the right (negative = left), <paramref name="ov"/> up.</summary>
        public static void Offset(double phi, double size, HomingRoom room, out double oh, out double ov) {
            oh = ov = 0;
            if (!(size > 0) || double.IsInfinity(size) || double.IsNaN(phi)) return;
            double c = Math.Cos(phi), s = Math.Max(0.0, Math.Sin(phi));
            double up = Math.Max(0.0, Math.Min(room.Up, room.MidUp) - Margin);
            double side = Math.Max(0.0, (c >= 0 ? room.Right : room.Left) - Margin);
            // the fan as wanted, then held inside the room; where the head-room is short, the rise it cannot have goes sideways
            // (the fan lies flatter) as far as that side has room
            double wantUp = s * size, wantSide = Math.Abs(c) * size;
            double gotUp = Math.Min(wantUp, up);
            double lost = wantUp - gotUp;
            double gotSide = Math.Min(Math.Sqrt(wantSide * wantSide + lost * lost), side);
            ov = gotUp;
            oh = (c >= 0 ? 1 : -1) * gotSide;
        }
    }

    /// <summary>H3 (H1-review M-1(a)): where the real arrow goes when the core cluster ended. Within PlaceMaxMetres of the shooter and
    /// inside the loaded world: where the core ended. Otherwise as a Quickdraw 4 line that hit nothing: along the way from the shooter,
    /// at the loaded reach (at most PlaceMaxMetres), in the air (it drops from there).</summary>
    internal static class HomingPlace {
        public static bool Within(HVec shooter, HVec end, HomingValues v, double loadedReach) {
            if (!shooter.Finite || !end.Finite) return false;
            double d = (end - shooter).Length;
            return d <= Math.Min(Math.Max(0.0, v.PlaceMaxMetres), loadedReach) + 1e-6;
        }

        public static HVec Fallback(HVec shooter, HVec end, HomingValues v, double loadedReach) {
            if (!shooter.Finite || !end.Finite) return shooter;
            HVec to = end - shooter;
            double d = to.Length;
            if (d < 1e-6) return shooter;
            double reach = Math.Min(d, Math.Min(Math.Max(0.0, v.PlaceMaxMetres), double.IsNaN(loadedReach) ? 0 : Math.Max(0.0, loadedReach)));
            return shooter + to * (reach / d);
        }
    }

    /// <summary>A vector in doubles, for the flight's arithmetic.</summary>
    internal struct HVec {
        public double X, Y, Z;
        public HVec(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static HVec operator +(HVec a, HVec b) => new HVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static HVec operator -(HVec a, HVec b) => new HVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static HVec operator *(HVec a, double k) => new HVec(a.X * k, a.Y * k, a.Z * k);
        public static double Dot(HVec a, HVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static HVec Cross(HVec a, HVec b) => new HVec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(Dot(this, this));
        public HVec Normalized { get { double l = Length; return l > 1e-12 ? this * (1.0 / l) : new HVec(0, 0, 0); } }
        public bool Finite => !double.IsNaN(X) && !double.IsNaN(Y) && !double.IsNaN(Z) && !double.IsInfinity(X) && !double.IsInfinity(Y) && !double.IsInfinity(Z);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", X, Y, Z);
    }


    /// <summary>Why a homing arrow stopped homing: its creature died and no other lock of the release lives ahead of it (Dead), this
    /// machine cannot see it (Lost), it ran more than LetGoDriftMetres from where it was at the release (Drift), or it got behind the
    /// arrow (Behind). It then flies straight on for LetGoSeconds and ends (H3).</summary>
    internal enum LetGo { None, Dead, Drift, Behind, Lost }

    /// <summary>One homing arrow's state. Kept per arrow and stepped every physics tick (it is not a closed form).</summary>
    internal struct HomingArrow {
        public HVec Pos, Dir, TargetAtLaunch;
        /// <summary>The line from the arrow to its lock when it was launched (or re-targeted), and the right and up across it: the
        /// frame its shaping point is placed in, relative to the creature (so the point moves with it).</summary>
        public HVec Line, Right, Up;
        public double Speed, Bloom, Travelled, Life, ApproachRad;
        /// <summary>H3: its side folded into the upper half round the line (0 = right, pi/2 = up, pi = left).</summary>
        public double Phi;
        /// <summary>H3: seconds it has flown, and when it let go (-1 while it homes).</summary>
        public double Age, LetAge;
        /// <summary>H3: the lock it flies to now (it changes when its own dies and another lives ahead), and the room round that lock.</summary>
        public int Lock;
        public HomingRoom Room;
        public LetGo Let;
        /// <summary>H5: it chains through the release's locks (HomingChain); the locks it has struck (bit i = lock i) and how many lock
        /// hits it made; whether it is still turning onto a chain leg's lock (the Behind rule waits until it has faced that lock).</summary>
        public bool Chain, Turning;
        public int Struck, Hits;
        /// <summary>H5: a chain leg going round toward its lock (HomingChain.TurnRound), not on along its own heading.</summary>
        public bool Round;
        /// <summary>H5 review M-2: it has flown a chain leg (struck a lock, or its lock died and it turned to another): it flies about level.</summary>
        public bool Legged;
    }

    /// <summary>The homing flight. Launch: from where the arrow leaves the bow, along its own line (the volley's normal spread),
    /// with no drop. For BloomShare of the straight distance to its creature (at most BloomMaxMetres, H3) it flies straight; then every
    /// step it turns, at most TurnDegreesPerSecond, toward a shaping point: ShapeK x the remaining distance short of the creature (led
    /// by its velocity), out to its own side by its ring's approach angle, the side folded into the upper half and held inside the room
    /// measured round the lock (HomingFan) and at most SideMaxMetres off the line. Within FadeTurns turning circles the point closes on
    /// the creature itself, and it keeps re-aiming at the moving body every tick to the end. When its creature dies it turns to the
    /// nearest other living lock ahead (HomingAssign.Retarget); with none, when the creature ran LetGoDriftMetres away, or once the
    /// creature is behind it, it lets go, flies straight on for LetGoSeconds and ends. Its speed is set per ring at launch so the rings
    /// land RingGapSeconds apart.</summary>
    internal static class HomingFlight {
        /// <summary>How far short of the creature, as a share of the remaining distance, the arrow aims along its approach direction.</summary>
        public const double ShapeK = 0.5;
        /// <summary>Within this many turning radii (speed / turn rate) of the creature the side approach fades into a straight one.</summary>
        public const double FadeTurns = 3.0;
        /// <summary>H3: a creature's velocity is believed up to this (m/s); a faster one (a teleport, a bad measurement) is cut to it.</summary>
        public const double LeadMaxSpeed = 15.0;
        const double Deg = Math.PI / 180.0;

        /// <summary>The H1 entry: open ground, the first lock.</summary>
        public static HomingArrow Launch(HVec start, HVec dir, HVec side, HVec target, double speed, int ring, bool core, double baseLife, HomingValues v) =>
            Launch(start, dir, side, target, speed, ring, core, baseLife, v, HomingRoom.Open, 0);

        /// <summary>A new homing arrow. <paramref name="side"/>: the way it leaves the arrow's line (its ring's turn round the arrow);
        /// <paramref name="baseLife"/>: [Volley] LifeSeconds; <paramref name="room"/>: the room round its lock; <paramref name="lockIndex"/>:
        /// which of the release's locks it flies to.</summary>
        public static HomingArrow Launch(HVec start, HVec dir, HVec side, HVec target, double speed, int ring, bool core, double baseLife, HomingValues v,
                                         HomingRoom room, int lockIndex) {
            var a = new HomingArrow { Pos = start, Dir = dir.Normalized, TargetAtLaunch = target, Let = LetGo.None, Lock = lockIndex, Room = room, LetAge = -1 };
            if (a.Dir.Length < 0.5) a.Dir = new HVec(0, 0, 1);
            HVec line = target - start;
            double dist = line.Finite ? line.Length : 0;
            HVec l0 = dist > 1e-6 ? line * (1.0 / dist) : a.Dir;
            a.Bloom = Math.Min(Math.Max(0.0, v.BloomShare) * dist, Math.Max(0.0, v.BloomMaxMetres)) * BloomOff(a.Dir, l0);
            a.ApproachRad = Math.Max(0.0, Math.Min(80.0, HomingAssign.ApproachDegrees(ring, core, v))) * Deg;
            Frame(ref a, l0);
            HVec sp = side - l0 * HVec.Dot(side, l0);
            if (!sp.Finite || sp.Length < 1e-6) { a.ApproachRad = 0; a.Phi = Math.PI / 2; }
            else a.Phi = HomingFan.Fold(sp.Normalized, l0);
            double s = speed > 0.1 && !double.IsInfinity(speed) ? speed : 0.1;
            double t0 = Estimate(dist, HomingAssign.ApproachDegrees(0, false, v) * Deg, s, v), tk = Estimate(dist, a.ApproachRad, s, v);
            double want = t0 + Math.Max(0, ring) * Math.Max(0.0, v.RingGapSeconds);
            a.Speed = want > 1e-6 && tk > 1e-6 ? s * tk / want : s;
            // its life: the longer of [Volley] LifeSeconds and its arrival plus 0.3 s, never past MaxLifeSeconds
            a.Life = Math.Min(Math.Max(0.0, v.MaxLifeSeconds), Math.Max(baseLife, want + 0.3));
            return a;
        }

        /// <summary>H3: a release far off its lock starts turning sooner: the bloom is full while the arrow leaves within a few degrees
        /// of the line to its creature (the volley's own spread) and shrinks in a straight line to a quarter at BloomOffDegrees off
        /// (the release cone). Why: a release 20 degrees wide and 10 low on a slope rising toward that side flew its straight bloom
        /// into the ground (the tests' worst cases, 16 of 35).</summary>
        public static double BloomOff(HVec dir, HVec line) {
            double cos = Math.Max(-1.0, Math.Min(1.0, HVec.Dot(dir.Normalized, line.Normalized)));
            double off = Math.Acos(cos) / Deg;
            if (double.IsNaN(off)) return 1.0;
            return Math.Max(0.25, Math.Min(1.0, 1.0 - Math.Max(0.0, off - BloomFreeDegrees) / (BloomOffDegrees - BloomFreeDegrees)));
        }

        /// <summary>H3: the bloom is full up to this many degrees off the line, a quarter at BloomOffDegrees.</summary>
        public const double BloomFreeDegrees = 5.0, BloomOffDegrees = 25.0;

        static void Frame(ref HomingArrow a, HVec line) {
            a.Line = line.Normalized;
            if (a.Line.Length < 0.5) a.Line = a.Dir;
            HomingFan.Basis(a.Line, out a.Right, out a.Up);
        }

        /// <summary>H5: a chain leg's fresh frame (HomingChain.Leg).</summary>
        internal static void Reframe(ref HomingArrow a, HVec line) => Frame(ref a, line);

        /// <summary>Seconds a still creature <paramref name="dist"/> away is reached at <paramref name="speed"/> with this approach
        /// (a fit of the flight's own paths, measured by the tests: the curve is longer the wider it comes in, about 13% of
        /// 1 - cos(approach) longer than the straight line).</summary>
        public static double Estimate(double dist, double approachRad, double speed, HomingValues v) {
            if (!(speed > 0) || !(dist > 0)) return 0;
            return dist * (1.0 + PathFit * (1.0 - Math.Cos(approachRad))) / speed;
        }

        const double PathFit = 0.13;

        /// <summary>H3: the point a moving creature will be at when an arrow <paramref name="dist"/> metres away at
        /// <paramref name="speed"/> gets there: its velocity (at most LeadMaxSpeed) over the time left, at most LeadMaxSeconds.</summary>
        public static HVec LeadPoint(HVec pos, HVec vel, double dist, double speed, HomingValues v) {
            if (!vel.Finite || !pos.Finite || !(speed > 0.1) || !(dist > 0)) return pos;
            double vl = vel.Length;
            if (vl < 1e-6) return pos;
            if (vl > LeadMaxSpeed) vel = vel * (LeadMaxSpeed / vl);
            double t = Math.Min(dist / speed, Math.Max(0.0, v.LeadMaxSeconds));
            return pos + vel * t;
        }

        /// <summary>H3: it let go LetGoSeconds ago: it ends now (H1 flew it on for 3 to 4 s, straight, with no drop).</summary>
        public static bool Done(in HomingArrow a, HomingValues v) => a.Let != LetGo.None && a.LetAge >= 0 && a.Age - a.LetAge >= Math.Max(0.0, v.LetGoSeconds) - 1e-9;

        /// <summary>The H1 entry: one lock, still, open ground.</summary>
        public static void Step(ref HomingArrow a, HVec target, bool alive, double dt, HomingValues v) {
            var one = new[] { new HomingTarget { Pos = target, Alive = alive, Room = a.Room } };
            int keep = a.Lock;
            a.Lock = 0;
            Step(ref a, one, dt, v);
            if (a.Lock == 0) a.Lock = keep;
        }

        /// <summary>One physics step of <paramref name="dt"/> seconds. <paramref name="targets"/>: every lock of the release as this
        /// machine sees it now (the same array for all of the release's arrows). A step that is not a positive number does nothing.</summary>
        public static void Step(ref HomingArrow a, HomingTarget[] targets, double dt, HomingValues v) {
            if (!(dt > 0) || double.IsInfinity(dt)) return;
            a.Age += dt;
            if (a.Let == LetGo.None) {
                bool inRange = targets != null && a.Lock >= 0 && a.Lock < targets.Length;
                HomingTarget t = inRange ? targets![a.Lock] : default;
                if (!t.Alive || !t.Pos.Finite) {
                    // H5: a chaining arrow turns to the nearest living lock it has not struck (behind it too), on a fresh leg; the
                    // others to the nearest living lock ahead within their turn (H3)
                    int next = a.Chain ? HomingChain.Next(HomingChain.From(targets!, a.Lock, a.Pos), targets!, a.Struck) : HomingAssign.Retarget(a.Pos, a.Dir, a.Speed, v, targets!, a.Lock);
                    if (next >= 0 && a.Chain) HomingChain.Leg(ref a, targets!, next, v);
                    else if (next >= 0) {
                        a.Lock = next;
                        a.TargetAtLaunch = targets![next].Pos;
                        a.Room = targets[next].Room;
                        Frame(ref a, targets[next].Pos - a.Pos);
                    }
                    else a.Let = t.Alive ? LetGo.Lost : LetGo.Dead;
                }
                else if ((t.Pos - a.TargetAtLaunch).Length > v.LetGoDriftMetres) a.Let = LetGo.Drift;
                if (a.Let != LetGo.None) a.LetAge = a.Age;
            }
            if (a.Let == LetGo.None && a.Travelled >= a.Bloom) {
                HomingTarget t = targets![a.Lock];
                double d0 = (t.Pos - a.Pos).Length;
                HVec target = LeadPoint(t.Pos, t.Vel, d0, a.Speed, v);
                HVec to = target - a.Pos;
                double d = to.Length;
                if (d > 1e-6) {
                    // the shaping point closes on the creature, and within FadeTurns turning circles of it the arrow aims
                    // straight at it (the cap could not make the side approach that close: it would overshoot)
                    double turn = Math.Max(1e-6, Math.Max(0.0, v.TurnDegreesPerSecond) * Deg);
                    double fade = Math.Min(1.0, d / (FadeTurns * a.Speed / turn));
                    HVec aim = target;
                    if (a.ApproachRad > 1e-9) {
                        double along = ShapeK * fade * d;
                        double size = Math.Min(along * Math.Sin(a.ApproachRad), Math.Max(0.0, v.SideMaxMetres));
                        HomingFan.Offset(a.Phi, size, a.Room, out double oh, out double ov);
                        aim = target - a.Line * (along * Math.Cos(a.ApproachRad)) + a.Right * oh + a.Up * ov;
                    }
                    HVec los = to * (1.0 / d);
                    double facing = HVec.Dot(a.Dir, los);
                    // H5: a chain leg starts in the creature it struck with its next lock beside or behind it: until it can turn onto
                    // that lock in front of it, it comes round (HomingChain.TurnRound: never round and round the lock, never a turn down
                    // into the ground); from then on it homes as any arrow (and Behind applies again). Every chain leg flies about level.
                    HVec want;
                    if (a.Turning) {
                        want = HomingChain.TurnRound(ref a, aim, a.Speed / turn, out bool onto);
                        if (onto && facing > HomingChain.FacedCos) a.Turning = false;
                    }
                    else want = (aim - a.Pos).Normalized;
                    bool level = a.Chain && (a.Hits > 0 || a.Legged);   // H5 review M-2: a leg after its lock died flies level too
                    if (level) want = HomingChain.Level(want);
                    if (facing < 0 && !a.Turning) { a.Let = LetGo.Behind; a.LetAge = a.Age; }   // more than 90 degrees: never turned round to circle it
                    else if (want.Length > 0.5) {
                        double max = Math.Max(0.0, v.TurnDegreesPerSecond) * Deg * dt;
                        a.Dir = level ? HomingChain.LevelTurn(a.Dir, want, max) : Turn(a.Dir, want, max);
                    }
                }
            }
            double move = a.Speed * dt;
            a.Pos = a.Pos + a.Dir * move;
            a.Travelled += move;
        }

        /// <summary><paramref name="from"/> turned toward <paramref name="to"/> by at most <paramref name="max"/> radians (both unit).</summary>
        static HVec Turn(HVec from, HVec to, double max) {
            double cos = Math.Max(-1.0, Math.Min(1.0, HVec.Dot(from, to)));
            double angle = Math.Acos(cos);
            if (angle <= max) return to;
            HVec perp = to - from * cos;
            double pl = perp.Length;
            if (pl < 1e-9) return from;
            perp = perp * (1.0 / pl);
            return (from * Math.Cos(max) + perp * Math.Sin(max)).Normalized;
        }
    }
}
