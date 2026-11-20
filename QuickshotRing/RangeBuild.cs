using System;
using System.Collections.Generic;
using System.Globalization;

namespace QuickshotRing {
    // R2: the range built from the game's own pieces, on the ground (TestRangeBuild.cs places what this decides). Unity-free, compiled
    // into QuickshotRing.Tests (RangeBuildTests.cs); keep Unity types out. The world is asked through IRangeProbe (raycasts and
    // OverlapBox on the server; a heightmap in the tests and in the plan-image harness), so the whole layout is decided here.
    //
    // Directions as in Range.cs: yaw in degrees clockwise from above, 0 = +Z, 90 = +X. A piece's own space: x right, y up, z forward;
    // a piece placed at pivot P with yaw Y has its local (x, z) at P + R(Y) (x, z), R(Y) (x, z) = (x cos Y + z sin Y, -x sin Y + z cos Y).
    //
    // The owner's rules (2026-11-18): follow the terrain (real ground first; a pad only where the ground is uneven, its top at the
    // ground's height; a stack of frames only over a drop, as a last resort, said in the log); nothing clips into anything (every
    // piece's own box tested against the world, only its base may touch the ground; a piece that does not fit slides by small steps
    // or is skipped, naming what it hit); build toward the way out (AutoFace scores usable ground first, pillars only as a tie-break).

    /// <summary>A piece's shape in its own space, metres: its solid box and the walkable top a creature stands on (NaN: none).</summary>
    internal struct PieceShape {
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ, Top;
        public bool Known;

        public static PieceShape Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ, float top = float.NaN) =>
            new PieceShape { MinX = minX, MinY = minY, MinZ = minZ, MaxX = maxX, MaxY = maxY, MaxZ = maxZ, Top = top, Known = true };

        public float CentreX => (MinX + MaxX) * 0.5f;
        public float CentreZ => (MinZ + MaxZ) * 0.5f;
        public float HalfX => (MaxX - MinX) * 0.5f;
        public float HalfZ => (MaxZ - MinZ) * 0.5f;
        public float Height => MaxY;

        public override string ToString() => Known
            ? $"{F(MaxX - MinX)} x {F(MaxY - MinY)} x {F(MaxZ - MinZ)} m (x {F(MinX)}..{F(MaxX)}, y {F(MinY)}..{F(MaxY)}, z {F(MinZ)}..{F(MaxZ)}){(float.IsNaN(Top) ? "" : $", top {F(Top)}")}"
            : "unknown";

        internal static string F(float v) => float.IsNaN(v) ? "?" : v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    internal enum PieceRole { Shelf, Pad, Tall, Frame, Wall, Lamp, Creature }

    /// <summary>[TestRange] Blueprint: which of the game's prefabs plays each role, "role: prefab name [turn N]" separated by '|'.
    /// A role written as "none" (or left out) is not built. Roles: pad (a level patch where the ground is uneven), tall (the top of a
    /// stack over a drop), frame (a stack's lower levels), wall (backstops), lamp (on the walls).</summary>
    internal sealed class Blueprint {
        public const string Default = "pad: woodenPlatform_1x1 | tall: Tall Support | frame: Tall Support Beams | wall: Temp Dynamic Wall | lamp: Outdoor Light Fixture Dim";
        static readonly PieceRole[] Roles = { PieceRole.Pad, PieceRole.Tall, PieceRole.Frame, PieceRole.Wall, PieceRole.Lamp };
        readonly Dictionary<PieceRole, string> names = new Dictionary<PieceRole, string>();
        readonly Dictionary<PieceRole, float> turns = new Dictionary<PieceRole, float>();

        public string NameOf(PieceRole r) => names.TryGetValue(r, out string n) ? n : "";
        public bool Has(PieceRole r) => NameOf(r).Length > 0;
        /// <summary>Extra turn in degrees for this role ("turn 180" after its name), in case its front is its back.</summary>
        public float TurnOf(PieceRole r) => turns.TryGetValue(r, out float t) ? t : 0f;

        public static Blueprint Parse(string? text, out List<string> errors) {
            errors = new List<string>();
            var b = new Blueprint();
            if (string.IsNullOrEmpty(text)) return b;
            foreach (string raw in text!.Split('|')) {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                int colon = t.IndexOf(':');
                if (colon <= 0) { errors.Add($"'{t}': no 'role:' before the prefab name"); continue; }
                string roleText = t.Substring(0, colon).Trim();
                PieceRole role = PieceRole.Creature;
                foreach (PieceRole r in Roles) if (string.Equals(r.ToString(), roleText, StringComparison.OrdinalIgnoreCase)) role = r;
                if (role == PieceRole.Creature) { errors.Add($"'{roleText}' is not a role (pad, tall, frame, wall, lamp)"); continue; }
                string name = t.Substring(colon + 1).Trim();
                float turn = 0f;
                int ti = name.LastIndexOf(" turn ", StringComparison.OrdinalIgnoreCase);
                if (ti >= 0) {
                    string num = name.Substring(ti + 6).Trim();
                    if (!float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out turn) || float.IsNaN(turn) || float.IsInfinity(turn)) {
                        errors.Add($"'{t}': '{num}' after 'turn' is not degrees");
                        continue;
                    }
                    name = name.Substring(0, ti).Trim();
                }
                if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase)) { b.names.Remove(role); b.turns.Remove(role); continue; }
                b.names[role] = name;
                b.turns[role] = turn;
            }
            return b;
        }

        public override string ToString() {
            var parts = new List<string>();
            foreach (PieceRole r in Roles)
                parts.Add($"{r.ToString().ToLowerInvariant()}: {(Has(r) ? NameOf(r) + (TurnOf(r) != 0f ? $" turn {TurnOf(r).ToString("0.#", CultureInfo.InvariantCulture)}" : "") : "none")}");
            return string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>The numbers of a build (from [TestRange] settings; the defaults are what the range builds).</summary>
    internal sealed class BuildValues {
        public float MaxSlope = 35f;          // ground steeper than this is not stood on
        public float FlatSpread = 0.3f;       // ground varying less than this under a pad's footprint is level enough to stand on
        public float CreatureSpread = 0.35f;  // a creature stands on ground varying up to this under its own feet (0.9 m: about 21 degrees; no pad)
        public float PadSpread = 0.9f;        // up to this a pad levels it (its legs reach the low side); more: slide or skip
        public float QuickFlat = 12f;         // AutoFace's one-cast read: up to this slope ground, up to MaxSlope a pad
        public float QuickClear = 0.6f;       // and its look for obstacles at a spot starts this far above the cast point
        public float Window = 12f;            // a spot's ground within this of the shooter's ground (above or below)
        public int StackLevels = 2;           // a stack over a drop: at most this many levels (last resort)
        public float ShelfWindow = 3f;        // the shelves' ground within this of the shooter's
        public float ShelfSpread = 0.4f;      // a shelf's ground may vary this much
        public float WallSpread = 1.2f;       // a wall's ground may vary this much along it (its base sinks into the high side)
        public float Inset = 0.12f;           // the base of a piece may touch the ground this far above its highest point under it
        public float Lift = 0.03f;            // a pad's top and a creature's feet this far above the ground they level to
        public float Margin = 0.05f;          // boxes are shrunk this much on every side before the overlap test (touching is fine)
        public float SlideStep = 1f;          // a piece that does not fit slides by this many metres (shelves by half)...
        public int SlideTries = 9;            // ...in at most this many places (the first is where it was planned); walls and shelves
        public int SpotTries = 25;            // a creature spot: this many places, back and forward along its lane and sideways (3 rings)
        public float WallBehind = 3f;         // a backstop stands this far behind its lane's farthest spot
        public float WallMargin = 1.5f;       // and reaches this far past the lane's outermost spots on each side
        public int WallsMax = 4;              // segments per lane at most
        public float LampHeight = 2.0f;       // lamps on the walls' fronts at this height above the wall's base
        public float LampOut = 0.14f;         // the wall's front face from its middle (Temp Dynamic Wall's bricks: 0.135)
        public int LampEvery = 2;             // a lamp on the middle wall of a backstop, then every this-many-th wall outward
        public float SightHalfWidth = 0.75f;  // a wall that would stand in the line from the shooter to another lane's spot is left out
        public float CreatureHalf = 0.45f, CreatureHeight = 1.5f; // the space a creature spawns into
        public float SpotClear = 1f;          // a creature spot (or its pad) keeps this far out of the shrine's keep-clear zone
        public float ShelfSide = 1.6f;        // shelves this far right (arrows) and left (gear) of the shooter
        public float KeepTree = 3.5f, KeepTryout = 8f; // the shrine's keep-clear zone: round the tree and round the try-out area
        public int MaxPieces = 80;            // pieces of construction per build at most (shelves and creatures not counted)
        public float SearchUp = 3f, SearchDown = 60f; // a spot's ground is looked for from Window + SearchUp above to SearchDown below
        // AutoFace
        public float FaceStep = 15f;          // facings tried round the shooter (and the fan's rays)
        public float FanReach = 60f, FanStep = 2f, FanHalf = 30f; // the ground read along rays out to this reach, a sample every FanStep m
        public float RiseStep = 1.2f;         // a rise or fall steeper than this per FanStep is not level ground (a bank, a ledge)
        public float FanWindow = 4f;          // a ray's reach ends where the ground leaves the shooter's height by more than this
        public float FanGap = 6f;             // or after a steep stretch longer than this (a ditch's bank is crossed, a hillside is not)
        public float SpotMetres = 6f;         // each lane spot on usable ground counts like this many metres of walkable ground
        public float PadMetres = 4f;          // ...on a pad
        public float BlockedMetres = 10f;     // ...a spot that cannot be used costs this many
        public float TieMetres = 8f;          // facings this close are tied: fewer stacks, fewer pads, then the smaller turn wins
    }

    /// <summary>How the world answers the planner: the server's raycasts and OverlapBox, or a heightmap in the tests.</summary>
    internal interface IRangeProbe {
        /// <summary>The ground under (x, z) between heights <paramref name="top"/> and <paramref name="bottom"/> (the highest solid
        /// surface there; creatures, players and loose items are not ground): its height and slope in degrees.</summary>
        bool Ground(float x, float z, float top, float bottom, out float y, out float slopeDegrees);

        /// <summary>The first solid thing inside the box (world centre, half extents, turned by <paramref name="yaw"/> degrees about
        /// up): "name (layer N)", or null when it is clear.</summary>
        string? Blocked(float cx, float cy, float cz, float hx, float hy, float hz, float yaw);
    }

    /// <summary>The ground under a footprint: its lowest and highest point, the height at its centre and the steepest slope seen.</summary>
    internal struct GroundPatch {
        public float Min, Max, Centre, Slope;
        public int Found, Total;
        public float Spread => Max - Min;
        public bool Complete => Total > 0 && Found == Total;

        /// <summary>From the heights (NaN = no ground there) and slopes sampled at a footprint's points; the centre is the first point.</summary>
        public static GroundPatch From(IList<float> heights, IList<float> slopes) {
            var g = new GroundPatch { Min = float.NaN, Max = float.NaN, Centre = heights.Count > 0 ? heights[0] : float.NaN, Total = heights.Count };
            for (int i = 0; i < heights.Count; i++) {
                float h = heights[i];
                if (float.IsNaN(h)) continue;
                g.Found++;
                if (float.IsNaN(g.Min) || h < g.Min) g.Min = h;
                if (float.IsNaN(g.Max) || h > g.Max) g.Max = h;
                float s = i < slopes.Count ? slopes[i] : 0f;
                if (!float.IsNaN(s) && s > g.Slope) g.Slope = s;
            }
            return g;
        }
    }

    internal enum SpotKind { Ground, Pad, Stack, Blocked }

    internal struct SpotFitResult {
        public SpotKind Kind;
        public float BaseY;   // the pivot height of what stands there (the pad's or the stack's; for Ground the lowest ground)
        public float StandY;  // where a creature's feet go
        public float Inset;   // how far above BaseY its box may touch the ground
        public int Levels;    // a stack's levels
        public string Why;    // Blocked: why; otherwise what it is ("on the ground", "on a pad (the ground varies 0.42 m)")
    }

    /// <summary>The 5 points read under a rectangle: its centre first, then its corners pulled in by <paramref name="pullIn"/>.</summary>
    internal static class Footprint {
        public static void Points(float cx, float cz, float halfX, float halfZ, float yaw, float pullIn, float[] xs, float[] zs) {
            float hx = Math.Max(0f, halfX - pullIn), hz = Math.Max(0f, halfZ - pullIn);
            xs[0] = cx; zs[0] = cz;
            int i = 1;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f }) {
                    ToWorld(cx, cz, yaw, sx * hx, sz * hz, out xs[i], out zs[i]);
                    i++;
                }
        }

        /// <summary>World (x, z) of a point of a piece's own space, the piece's pivot at (px, pz) turned by yaw.</summary>
        public static void ToWorld(float px, float pz, float yaw, float lx, float lz, out float x, out float z) {
            double a = yaw * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            x = px + (float)(lx * c + lz * s);
            z = pz + (float)(-lx * s + lz * c);
        }
    }

    /// <summary>How a creature spot is used on the ground there (the owner: "follow the terrain").</summary>
    internal static class SpotFit {
        static string M(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string M1(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>The height a stack of <paramref name="levels"/> stands a creature at above its base: frames under one tall piece.</summary>
        public static float StackTop(int levels, PieceShape tall, PieceShape frame) => (levels - 1) * frame.MaxY + tall.Top;

        /// <summary>Level enough: on the ground. Uneven up to PadSpread: a pad whose top is at the HIGH side's ground (its legs reach
        /// the low side). Ground more than Window below the shooter's: the last resort, a stack of a tall piece on frames, only while
        /// StackLevels reach it. Anything else: Blocked with the reason (the caller slides the spot and tries again).</summary>
        public static SpotFitResult Decide(GroundPatch g, float refY, BuildValues v, PieceShape pad, PieceShape tall, PieceShape frame) {
            var r = new SpotFitResult { Kind = SpotKind.Blocked, Why = "" };
            if (g.Found == 0 || float.IsNaN(g.Min)) { r.Why = $"no ground within {M1(v.Window)} m above or {M1(v.SearchDown)} m below yours"; return r; }
            if (!g.Complete) { r.Why = "the ground ends under part of it (an edge)"; return r; }
            float spread = g.Spread;
            if (g.Min - refY > v.Window) { r.Why = $"the ground is {M1(g.Min - refY)} m above yours (a hillside; at most {M1(v.Window)})"; return r; }
            if (refY - g.Max > v.Window) {
                float drop = refY - g.Min;
                if (!tall.Known || float.IsNaN(tall.Top) || v.StackLevels < 1) { r.Why = $"the ground is {M1(drop)} m below yours (at most {M1(v.Window)}; no stack piece)"; return r; }
                int n = 1;
                float need = refY - v.Window - g.Min;
                if (need > tall.Top) {
                    if (!frame.Known || frame.MaxY <= 0.5f) { r.Why = $"the ground is {M1(drop)} m below yours (one level does not reach; no frame piece)"; return r; }
                    n = 1 + (int)Math.Ceiling((need - tall.Top) / frame.MaxY - 1e-4);
                }
                if (n > v.StackLevels) { r.Why = $"the ground is {M1(drop)} m below yours: deeper than a stack of {v.StackLevels} reaches"; return r; }
                if (spread > v.PadSpread) { r.Why = $"the ground {M1(drop)} m below varies {M(spread)} m (too uneven for a stack)"; return r; }
                r.Kind = SpotKind.Stack;
                r.Levels = n;
                r.BaseY = g.Min;
                r.Inset = spread + v.Inset;
                r.StandY = g.Min + StackTop(n, tall, frame);
                r.Why = $"a stack of {n} over a drop of {M1(drop)} m (last resort)";
                return r;
            }
            if (spread <= v.FlatSpread && g.Slope <= v.MaxSlope) {
                r.Kind = SpotKind.Ground;
                r.BaseY = g.Min;
                r.StandY = g.Max + v.Lift;
                r.Inset = spread + v.Inset;
                r.Why = "on the ground";
                return r;
            }
            if (pad.Known && !float.IsNaN(pad.Top)) {
                float reach = pad.Top - pad.MinY - v.Lift + 0.1f; // the pad's legs reach this far below its top
                if (spread <= Math.Min(v.PadSpread, reach)) {
                    r.Kind = SpotKind.Pad;
                    r.StandY = g.Max + v.Lift;
                    r.BaseY = r.StandY - pad.Top;
                    r.Inset = g.Max - r.BaseY + v.Inset;
                    r.Why = $"on a pad (the ground varies {M(spread)} m{(g.Slope > v.MaxSlope ? $", {M1(g.Slope)} degrees" : "")})";
                    return r;
                }
                r.Why = $"the ground varies {M(spread)} m under it (more than a pad levels: {M(Math.Min(v.PadSpread, reach))})";
                return r;
            }
            r.Why = $"the ground is uneven ({M(spread)} m, {M1(g.Slope)} degrees) and there is no pad piece";
            return r;
        }

        /// <summary>A creature can stand on this ground as it is (read under its own footprint, CreatureHalf): complete, within the
        /// window, varying at most CreatureSpread and not steeper than MaxSlope. Creatures stand on uneven ground all over the game; a
        /// pad is only for ground they cannot stand on.</summary>
        public static bool CreatureStands(GroundPatch g, float refY, BuildValues v) =>
            g.Complete && !float.IsNaN(g.Min) && Math.Abs(g.Min - refY) <= v.Window && Math.Abs(g.Max - refY) <= v.Window && g.Spread <= v.CreatureSpread && g.Slope <= v.MaxSlope;

        /// <summary>AutoFace's quick read of one spot from one cast at its centre: what it would most likely be.</summary>
        public static SpotKind Quick(bool found, float y, float slope, float refY, BuildValues v, bool stacks) {
            if (!found || float.IsNaN(y)) return SpotKind.Blocked;
            if (y - refY > v.Window) return SpotKind.Blocked;
            if (refY - y > v.Window) return stacks && refY - y <= v.Window + 3.3f * v.StackLevels ? SpotKind.Stack : SpotKind.Blocked;
            if (slope <= v.QuickFlat) return SpotKind.Ground;
            return slope <= v.MaxSlope ? SpotKind.Pad : SpotKind.Blocked;
        }
    }

    /// <summary>Where a piece that does not fit is tried next: offsets (along its lane, to its side) in metres, the planned place first.</summary>
    internal static class SlideRule {
        /// <summary>Ring by ring: sideways both ways, then back, then back and sideways; forward (toward the shooter) only when
        /// <paramref name="forward"/>. At most <paramref name="tries"/> places, all different.</summary>
        public static List<KeyValuePair<float, float>> Offsets(float step, int tries, bool forward, bool backFirst = false) {
            var list = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(0f, 0f) };
            for (int k = 1; list.Count < tries && k <= 50; k++) {
                // backFirst (a backstop's segments: sideways they would run into their neighbours): back, then sideways
                var ring = backFirst
                    ? new List<int[]> { new[] { k, 0 }, new[] { 0, k }, new[] { 0, -k }, new[] { k, k }, new[] { k, -k } }
                    : new List<int[]> { new[] { 0, k }, new[] { 0, -k }, new[] { k, 0 }, new[] { k, k }, new[] { k, -k } };
                if (forward) { ring.Add(new[] { -k, 0 }); ring.Add(new[] { -k, k }); ring.Add(new[] { -k, -k }); }
                foreach (int[] o in ring) {
                    if (list.Count >= tries) break;
                    list.Add(new KeyValuePair<float, float>(o[0] * step, o[1] * step));
                }
            }
            return list;
        }
    }

    /// <summary>A box in the world as the planner keeps it: plan-view rectangle (centre, half sizes, yaw) and a height range.</summary>
    internal struct PlanBox {
        public float Cx, Cz, Hx, Hz, Yaw, MinY, MaxY;
        public string What;

        /// <summary>The box of a piece of this shape placed with its pivot at (px, py, pz) turned by yaw; its bottom raised by
        /// <paramref name="inset"/> (the base may touch the ground) and every side pulled in by <paramref name="margin"/>.</summary>
        public static PlanBox Of(PieceShape s, float px, float py, float pz, float yaw, float inset, float margin, string what) {
            Footprint.ToWorld(px, pz, yaw, s.CentreX, s.CentreZ, out float cx, out float cz);
            float minY = py + s.MinY + inset, maxY = py + s.MaxY - margin;
            if (maxY < minY + 0.01f) maxY = minY + 0.01f;
            return new PlanBox { Cx = cx, Cz = cz, Hx = Math.Max(0.01f, s.HalfX - margin), Hz = Math.Max(0.01f, s.HalfZ - margin), Yaw = yaw, MinY = minY, MaxY = maxY, What = what };
        }

        public float CentreY => (MinY + MaxY) * 0.5f;
        public float HalfY => (MaxY - MinY) * 0.5f;

        static void Axes(float yaw, out double ux, out double uz, out double wx, out double wz) {
            double a = yaw * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            ux = c; uz = -s;   // the box's own x in the world
            wx = s; wz = c;    // its own z
        }

        static bool Apart(PlanBox a, PlanBox b, double lx, double lz) {
            Axes(a.Yaw, out double aux, out double auz, out double awx, out double awz);
            Axes(b.Yaw, out double bux, out double buz, out double bwx, out double bwz);
            double ra = a.Hx * Math.Abs(aux * lx + auz * lz) + a.Hz * Math.Abs(awx * lx + awz * lz);
            double rb = b.Hx * Math.Abs(bux * lx + buz * lz) + b.Hz * Math.Abs(bwx * lx + bwz * lz);
            double d = Math.Abs((b.Cx - a.Cx) * lx + (b.Cz - a.Cz) * lz);
            return d >= ra + rb;
        }

        static bool PlanOverlap(PlanBox a, PlanBox b) {
            Axes(a.Yaw, out double aux, out double auz, out double awx, out double awz);
            Axes(b.Yaw, out double bux, out double buz, out double bwx, out double bwz);
            return !(Apart(a, b, aux, auz) || Apart(a, b, awx, awz) || Apart(a, b, bux, buz) || Apart(a, b, bwx, bwz));
        }

        /// <summary>Two boxes overlap (plan-view rectangles by the separating-axis test, and their height ranges).</summary>
        public static bool Overlap(PlanBox a, PlanBox b) {
            if (a.MaxY <= b.MinY || b.MaxY <= a.MinY) return false;
            return PlanOverlap(a, b);
        }

        /// <summary>The box crosses the line from (ax, az) to (bx, bz) widened by <paramref name="halfWidth"/> on each side (plan view).</summary>
        public static bool Crosses(PlanBox a, float ax, float az, float bx, float bz, float halfWidth) {
            float dx = bx - ax, dz = bz - az;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f) return false;
            var line = new PlanBox { Cx = (ax + bx) * 0.5f, Cz = (az + bz) * 0.5f, Hx = halfWidth, Hz = len * 0.5f, Yaw = RangeLayout.Toward(ax, az, bx, bz) };
            return PlanOverlap(a, line);
        }
    }

    /// <summary>The shrine's keep-clear zone: circles round the tree and the try-out area.</summary>
    internal struct KeepCircle {
        public float X, Z, R;
        public string What;
    }

    internal static class KeepClear {
        /// <summary>What a circle of <paramref name="radius"/> at (x, z) would enter, or null.</summary>
        public static string? Inside(float x, float z, float radius, IList<KeepCircle>? zone) {
            if (zone == null) return null;
            foreach (KeepCircle c in zone) {
                float dx = x - c.X, dz = z - c.Z;
                if (dx * dx + dz * dz < (c.R + radius) * (c.R + radius)) return c.What;
            }
            return null;
        }
    }

    /// <summary>One ray's walk out from the shooter, a sample at a time (FanRule).</summary>
    internal struct FanWalk {
        public float Prev, Walk, Run;
        public bool Done;

        public static FanWalk Start(float refY) => new FanWalk { Prev = refY };

        /// <summary>Takes the next sample; false once the walk has ended (it then ignores every later sample).</summary>
        public bool Add(float y, float slope, bool kept, float refY, BuildValues v) {
            if (Done) return false;
            if (kept || float.IsNaN(y) || Math.Abs(y - refY) > v.FanWindow) { Done = true; return false; }   // a drop, a hillside, the shrine
            bool level = !(slope > v.MaxSlope) && Math.Abs(y - Prev) <= v.RiseStep;
            Prev = y;
            if (level) { Walk += v.FanStep; Run = 0f; return true; }
            Run += v.FanStep;                                     // a steep bank or a step: crossed while short, never counted
            if (Run > v.FanGap) { Done = true; return false; }
            return true;
        }
    }

    /// <summary>AutoFace's view of the ground: rays out from the shooter, a sample every FanStep metres.</summary>
    internal static class FanRule {
        /// <summary>Walkable metres in reach along one ray: the samples with level-enough ground (not steeper than MaxSlope, not a
        /// step of more than RiseStep from the sample before), counted up to the reach's end: no ground (a drop), ground more than
        /// FanWindow above or below the shooter's (a hillside, a gully), the shrine's keep-clear zone, or a steep stretch longer than
        /// FanGap (a short bank, a ditch, is crossed but not counted). heights[i] is the ground at (i + 1) x FanStep.</summary>
        public static float Walkable(IList<float> heights, IList<float> slopes, IList<bool> kept, float refY, BuildValues v) {
            FanWalk w = FanWalk.Start(refY);
            for (int i = 0; i < heights.Count; i++)
                if (!w.Add(heights[i], i < slopes.Count ? slopes[i] : 0f, i < kept.Count && kept[i], refY, v)) break;
            return w.Walk;
        }

        /// <summary>A facing's walkable ground: the rays (one per FaceStep, world bearings i x FaceStep) within FanHalf of it.</summary>
        public static float Within(IList<float> rays, float yaw, BuildValues v) {
            if (rays.Count == 0) return 0f;
            float step = 360f / rays.Count, sum = 0f;
            for (int i = 0; i < rays.Count; i++)
                if (Math.Abs(FaceRule.Norm(i * step - yaw)) <= v.FanHalf + 1e-3f) sum += rays[i];
            return sum;
        }
    }

    internal struct FaceCandidate {
        public float Yaw, Walkable, Turn;   // Turn: degrees from where the shooter faces
        public int Ground, Pads, Stacks, Blocked;
    }

    /// <summary>AutoFace: usable ground first (walkable metres and lane spots on the ground), pillars only as a tie-break.</summary>
    internal static class FaceRule {
        public static float Usable(FaceCandidate c, BuildValues v) => c.Walkable + v.SpotMetres * c.Ground + v.PadMetres * c.Pads - v.BlockedMetres * c.Blocked;

        static int Choose(IList<FaceCandidate> cs, BuildValues v, int skip) {
            float best = float.NegativeInfinity;
            for (int i = 0; i < cs.Count; i++) if (i != skip) best = Math.Max(best, Usable(cs[i], v));
            int pick = -1;
            for (int i = 0; i < cs.Count; i++) {
                if (i == skip || Usable(cs[i], v) < best - v.TieMetres) continue;
                if (pick < 0 || Better(cs[i], cs[pick], v)) pick = i;
            }
            return pick;
        }

        // within the tie: fewer stacks, then fewer pads, then the smaller turn, then the higher score
        static bool Better(FaceCandidate a, FaceCandidate b, BuildValues v) {
            if (a.Stacks != b.Stacks) return a.Stacks < b.Stacks;
            if (a.Pads != b.Pads) return a.Pads < b.Pads;
            if (Math.Abs(a.Turn - b.Turn) > 1e-3f) return a.Turn < b.Turn;
            return Usable(a, v) > Usable(b, v);
        }

        /// <summary>The index of the best facing (and the runner-up, -1 when there is none).</summary>
        public static int Pick(IList<FaceCandidate> cs, BuildValues v, out int runnerUp) {
            runnerUp = -1;
            if (cs == null || cs.Count == 0) return -1;
            int best = Choose(cs, v, -1);
            runnerUp = cs.Count > 1 ? Choose(cs, v, best) : -1;
            return best;
        }

        public static string Describe(FaceCandidate c, BuildValues v) =>
            $"{Norm(c.Yaw).ToString("0", CultureInfo.InvariantCulture)} degrees: score {Usable(c, v).ToString("0", CultureInfo.InvariantCulture)} (walkable {c.Walkable.ToString("0", CultureInfo.InvariantCulture)} m; spots {c.Ground} on the ground, {c.Pads} on pads, {c.Stacks} stacks, {c.Blocked} unusable)";

        public static float Norm(float deg) { float d = deg % 360f; if (d > 180f) d -= 360f; if (d <= -180f) d += 360f; return d; }
    }

    /// <summary>One wall segment of a backstop: its centre (pivot, bottom centre) and facing (its front toward the shooter).</summary>
    internal struct WallSpot {
        public float X, Z, Facing;
        public int Index, Of;
    }

    /// <summary>Backstops: walls behind each lane so missed arrows stop near the range.</summary>
    internal static class BackstopRule {
        /// <summary>Segments of <paramref name="segment"/> m across the lane, WallBehind behind its farthest spot, covering its spots'
        /// sideways spread plus WallMargin each side (at most WallsMax), centred on the lane's line, facing the shooter.</summary>
        public static List<WallSpot> Walls(Lane lane, float originX, float originZ, float yaw, float segment, BuildValues v) {
            var list = new List<WallSpot>();
            if (lane == null || lane.NoWall || lane.Spots.Count == 0 || segment <= 0.1f) return list;
            float far = 0f, minSide = float.PositiveInfinity, maxSide = float.NegativeInfinity;
            foreach (LaneSpot s in lane.Spots) {
                far = Math.Max(far, s.Distance);
                if (s.RowGap > 0f) {
                    int k = lane.Kinds.Count;
                    float half = (k - 1) * 0.5f * s.RowGap;
                    minSide = Math.Min(minSide, -half); maxSide = Math.Max(maxSide, half);
                } else { minSide = Math.Min(minSide, s.Side); maxSide = Math.Max(maxSide, s.Side); }
            }
            float width = maxSide - minSide + 2f * v.WallMargin;
            int count = Math.Max(1, Math.Min(Math.Max(1, v.WallsMax), (int)Math.Ceiling(width / segment - 1e-3)));
            float mid = (minSide + maxSide) * 0.5f, dist = far + v.WallBehind;
            double a = (yaw + lane.Bearing) * Math.PI / 180.0;
            float fx = (float)Math.Sin(a), fz = (float)Math.Cos(a), rx = (float)Math.Cos(a), rz = -(float)Math.Sin(a);
            for (int i = 0; i < count; i++) {
                float side = mid + (i - (count - 1) * 0.5f) * segment;
                list.Add(new WallSpot { X = originX + fx * dist + rx * side, Z = originZ + fz * dist + rz * side, Facing = yaw + lane.Bearing + 180f, Index = i, Of = count });
            }
            return list;
        }

        /// <summary>Which of a lane's <paramref name="walls"/> segments carry a lamp: the middle one, then every LampEvery-th outward.</summary>
        public static List<int> Lamps(int walls, int every) {
            var list = new List<int>();
            if (walls <= 0) return list;
            if (every < 1) every = 1;
            int mid = (walls - 1) / 2;
            list.Add(mid);
            for (int d = 1; d < walls; d++) {
                if (d % every != 0) continue;
                if (mid - d >= 0) list.Add(mid - d);
                if (mid + d < walls) list.Add(mid + d);
            }
            return list;
        }
    }

    /// <summary>A piece the planner decided on (TestRangeBuild spawns these in order).</summary>
    internal sealed class Placed {
        public PieceRole Role;
        public string Lane = "", Label = "", Kind = "", Note = "";
        public int Index, Host = -1;   // Host: the placed piece it hangs on or stands on (a lamp's wall, a creature's pad)
        public float X, Y, Z, Yaw, Inset;
        public bool Free, Dummy, LastResort;
        public PlanBox Box;
    }

    internal sealed class PlanInput {
        public float X, Y, Z, PlayerYaw;
        public bool AutoFace = true;
        public List<Lane> Lanes = new List<Lane>();
        public Blueprint Pieces = new Blueprint();
        public readonly Dictionary<PieceRole, PieceShape> Shapes = new Dictionary<PieceRole, PieceShape>();
        public PieceShape ArrowShelf, GearShelf;
        public bool GearShelfWanted = true;
        public List<KeepCircle> Keep = new List<KeepCircle>();
        public BuildValues Values = new BuildValues();
    }

    internal sealed class LaneReport {
        public string Lane = "";
        public int Spots, OnGround, OnPads, Stacks, Skipped, Walls, WallsWanted, Lamps, Moved;
    }

    internal sealed class PlanResult {
        public float Yaw;
        public string? Refusal;                       // the arrow shelf has nowhere to stand
        public float[] Rays = new float[0];           // walkable metres per world bearing i x FaceStep
        public List<FaceCandidate> Faces = new List<FaceCandidate>();
        public int Best = -1, Runner = -1;
        public List<Placed> Pieces = new List<Placed>();
        public List<string> Skipped = new List<string>();
        public List<string> Notes = new List<string>();
        public List<LaneReport> Lanes = new List<LaneReport>();
        public int Construction, CapHit;              // construction pieces placed; how many were left out by MaxPieces
    }

    /// <summary>The whole layout, decided through the probe: the facing, the shelves, every lane spot (ground, pad or stack), the
    /// backstops and their lamps, each fitted to the ground, tested for overlap against the world and the pieces already planned, and
    /// slid or skipped when it does not fit.</summary>
    internal static class RangePlanner {
        static int RayCount(BuildValues v) => Math.Max(4, (int)Math.Round(360f / Math.Max(1f, v.FaceStep)));

        /// <summary>Walkable metres along a ray every FaceStep degrees all round (world bearings).</summary>
        public static float[] Fan(IRangeProbe probe, float ox, float oy, float oz, IList<KeepCircle> keep, BuildValues v) {
            int n = RayCount(v), samples = Math.Max(1, (int)(v.FanReach / v.FanStep + 1e-3f));
            var rays = new float[n];
            float top = oy + v.FanWindow + v.RiseStep + 1f, bottom = oy - v.FanWindow - 2f;
            for (int i = 0; i < n; i++) {
                double a = i * (360.0 / n) * Math.PI / 180.0;
                float sx = (float)Math.Sin(a), sz = (float)Math.Cos(a);
                FanWalk walk = FanWalk.Start(oy);
                for (int k = 1; k <= samples; k++) {
                    float d = k * v.FanStep, x = ox + sx * d, z = oz + sz * d;
                    bool kept = KeepClear.Inside(x, z, 0f, keep) != null;
                    float y = float.NaN, slope = 0f;
                    if (!kept && !probe.Ground(x, z, top, bottom, out y, out slope)) y = float.NaN;
                    if (!walk.Add(y, slope, kept, oy, v)) break;
                }
                rays[i] = walk.Walk;
            }
            return rays;
        }

        sealed class Ctx {
            public IRangeProbe Probe = null!;
            public PlanInput In = null!;
            public BuildValues V = null!;
            public PlanResult R = null!;
            public readonly List<Placed> Creatures = new List<Placed>();
            public float[] Xs = new float[5], Zs = new float[5], Ys = new float[5], Ss = new float[5];
            public bool CapReached;
        }

        static PieceShape ShapeOf(PlanInput i, PieceRole role) =>
            i.Pieces.Has(role) && i.Shapes.TryGetValue(role, out PieceShape s) && s.Known ? s : default;

        static string M(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>The ground under a rectangle (centre, half sizes, yaw) between the given heights.</summary>
        static GroundPatch Patch(Ctx c, float cx, float cz, float hx, float hz, float yaw, float top, float bottom) {
            Footprint.Points(cx, cz, hx, hz, yaw, 0.1f, c.Xs, c.Zs);
            for (int i = 0; i < 5; i++) {
                if (!c.Probe.Ground(c.Xs[i], c.Zs[i], top, bottom, out float y, out float s)) { y = float.NaN; s = 0f; }
                c.Ys[i] = y; c.Ss[i] = s;
            }
            return GroundPatch.From(c.Ys, c.Ss);
        }

        /// <summary>Why this box cannot be placed (a planned piece in the way, or something in the world), or null. Pieces in
        /// <paramref name="ignore"/> (its own host chain) are not counted.</summary>
        static string? Clash(Ctx c, PlanBox b, ICollection<int>? ignore, bool creatures = true) {
            for (int i = 0; i < c.R.Pieces.Count; i++) {
                if (ignore != null && ignore.Contains(i)) continue;
                if (PlanBox.Overlap(b, c.R.Pieces[i].Box)) return $"it would stand in {c.R.Pieces[i].Label}";
            }
            if (creatures)
                foreach (Placed p in c.Creatures)
                    if (PlanBox.Overlap(b, p.Box)) return $"it would stand in {p.Label}";
            string? hit = c.Probe.Blocked(b.Cx, b.CentreY, b.Cz, b.Hx, b.HalfY, b.Hz, b.Yaw);
            return hit != null ? $"it would hit {hit}" : null;
        }

        static Placed Add(Ctx c, PieceRole role, string lane, string label, float x, float y, float z, float yaw, float inset, PieceShape shape, string note, int host = -1) {
            var p = new Placed { Role = role, Lane = lane, Label = label, X = x, Y = y, Z = z, Yaw = yaw, Inset = inset, Note = note, Host = host,
                Box = PlanBox.Of(shape, x, y, z, yaw, inset, c.V.Margin, label) };
            c.R.Pieces.Add(p);
            if (role != PieceRole.Shelf && role != PieceRole.Creature) c.R.Construction++;
            return p;
        }

        static bool Cap(Ctx c, int more) {
            if (c.R.Construction + more <= c.V.MaxPieces) return false;
            c.R.CapHit += more;
            c.CapReached = true;
            return true;
        }

        static string Moved(float along, float side) {
            if (along == 0f && side == 0f) return "";
            var parts = new List<string>();
            if (side != 0f) parts.Add($"{M(Math.Abs(side))} m {(side > 0f ? "right" : "left")}");
            if (along != 0f) parts.Add($"{M(Math.Abs(along))} m {(along > 0f ? "back" : "forward")}");
            return "moved " + string.Join(" and ", parts.ToArray());
        }

        /// <summary>Both phases at once (the tests and the plan harness; the server runs them in two frames).</summary>
        public static PlanResult Plan(IRangeProbe probe, PlanInput input) {
            PlanResult r = Face(probe, input);
            Layout(probe, input, r);
            return r;
        }

        /// <summary>Phase 1, the way out: the ground read in a fan, then every facing scored (AutoFace); sets Yaw, Rays, Faces.</summary>
        public static PlanResult Face(IRangeProbe probe, PlanInput input) {
            BuildValues v = input.Values;
            var r = new PlanResult();
            float ox = input.X, oy = input.Y, oz = input.Z;
            PieceShape tall = ShapeOf(input, PieceRole.Tall);
            bool stacks = tall.Known && !float.IsNaN(tall.Top);
            r.Rays = Fan(probe, ox, oy, oz, input.Keep, v);
            float yaw = input.PlayerYaw;
            if (input.AutoFace) {
                int n = RayCount(v);
                float top = oy + v.Window + v.SearchUp, bottom = oy - v.SearchDown;
                for (int k = 0; k < n; k++) {
                    float turn = k * (360f / n), y = input.PlayerYaw + turn;
                    var f = new FaceCandidate { Yaw = y, Turn = Math.Abs(FaceRule.Norm(turn)), Walkable = FanRule.Within(r.Rays, y, v) };
                    foreach (RangePiece s in RangeLayout.Spots(input.Lanes, ox, oz, y)) {
                        SpotKind kind;
                        if (KeepClear.Inside(s.X, s.Z, v.SpotClear, input.Keep) != null) kind = SpotKind.Blocked;
                        else {
                            bool found = probe.Ground(s.X, s.Z, top, bottom, out float gy, out float gs);
                            kind = SpotFit.Quick(found, gy, gs, oy, v, stacks);
                            // one cast cannot tell a slope under the creature's feet from an obstacle: the quick box starts QuickClear
                            // above the cast point (rocks, trees, walls and the shrine still reach into it)
                            if (kind != SpotKind.Blocked && kind != SpotKind.Stack && probe.Blocked(s.X, gy + v.QuickClear + v.CreatureHeight * 0.5f, s.Z, v.CreatureHalf, v.CreatureHeight * 0.5f, v.CreatureHalf, s.FacingDegrees) != null)
                                kind = SpotKind.Blocked;
                        }
                        switch (kind) {
                            case SpotKind.Ground: f.Ground++; break;
                            case SpotKind.Pad: f.Pads++; break;
                            case SpotKind.Stack: f.Stacks++; break;
                            default: f.Blocked++; break;
                        }
                    }
                    r.Faces.Add(f);
                }
                r.Best = FaceRule.Pick(r.Faces, v, out r.Runner);
                if (r.Best >= 0) yaw = r.Faces[r.Best].Yaw;
            }
            r.Yaw = yaw;
            return r;
        }

        /// <summary>Phase 2, the layout at the chosen facing (r.Yaw): the shelves, the creature spots, the backstops, the lamps.</summary>
        public static void Layout(IRangeProbe probe, PlanInput input, PlanResult r) {
            BuildValues v = input.Values;
            var c = new Ctx { Probe = probe, In = input, V = v, R = r };
            float ox = input.X, oy = input.Y, oz = input.Z, yaw = r.Yaw;
            PieceShape pad = ShapeOf(input, PieceRole.Pad), tall = ShapeOf(input, PieceRole.Tall), frame = ShapeOf(input, PieceRole.Frame),
                wall = ShapeOf(input, PieceRole.Wall), lamp = ShapeOf(input, PieceRole.Lamp);

            // ---- the shelves: one step right (arrows: without it there is no range) and left (gear)
            foreach (bool right in new[] { true, false }) {
                if (!right && !input.GearShelfWanted) continue;
                PieceShape shape = right ? input.ArrowShelf : input.GearShelf;
                if (!shape.Known) shape = PieceShape.Box(-0.75f, 0f, -0.25f, 0.75f, 1.9f, 0.25f);
                string label = right ? "the arrow shelf" : "the gear shelf";
                RangeLayout.Shelf(ox, oz, yaw, v.ShelfSide, right, out float sx, out float sz, out float facing);
                double ya = yaw * Math.PI / 180.0;
                float fx = (float)Math.Sin(ya), fz = (float)Math.Cos(ya), rx = (float)Math.Cos(ya) * (right ? 1f : -1f), rz = -(float)Math.Sin(ya) * (right ? 1f : -1f);
                string? first = null, last = null;
                bool placed = false;
                // for a shelf, "back" is outward (away from the shooter) and "sideways" is along the shooter's facing
                foreach (KeyValuePair<float, float> o in SlideRule.Offsets(v.SlideStep * 0.5f, v.SlideTries, false)) {
                    float px = sx + rx * o.Key + fx * o.Value, pz = sz + rz * o.Key + fz * o.Value;
                    string? why = KeepClear.Inside(px, pz, Math.Max(shape.HalfX, shape.HalfZ), input.Keep) is string k ? $"it would stand in {k}'s keep-clear zone" : null;
                    GroundPatch g = default;
                    if (why == null) {
                        Footprint.ToWorld(px, pz, facing, shape.CentreX, shape.CentreZ, out float gx, out float gz);
                        g = Patch(c, gx, gz, shape.HalfX, shape.HalfZ, facing, oy + v.ShelfWindow + 1f, oy - v.ShelfWindow - 1f);
                        if (g.Found == 0) why = $"no ground within {M(v.ShelfWindow)} m of yours";
                        else if (!g.Complete) why = "the ground ends under it (an edge)";
                        else if (Math.Abs(g.Min - oy) > v.ShelfWindow || Math.Abs(g.Max - oy) > v.ShelfWindow) why = $"its ground is more than {M(v.ShelfWindow)} m from yours";
                        else if (g.Spread > v.ShelfSpread) why = $"the ground varies {M(g.Spread)} m under it";
                        else if (g.Slope > v.MaxSlope) why = $"the ground is too steep ({M(g.Slope)} degrees)";
                    }
                    if (why == null) {
                        float inset = g.Spread + v.Inset;
                        why = Clash(c, PlanBox.Of(shape, px, g.Min, pz, facing, inset, v.Margin, label), null);
                        if (why == null) {
                            Add(c, PieceRole.Shelf, "", label, px, g.Min, pz, facing, inset, shape, o.Key == 0f && o.Value == 0f ? "" : $"{Moved(o.Key, o.Value)} ({first})");
                            placed = true;
                            break;
                        }
                    }
                    first ??= why; last = why;
                }
                if (!placed) {
                    if (right) { r.Refusal = $"the arrow shelf has nowhere to stand one step to your right ({first}{(last != first ? $"; last try: {last}" : "")}). Stand on open, level ground and press Build again"; return; }
                    r.Skipped.Add($"the gear shelf: left out ({first})");
                }
            }

            // ---- every creature spot: on the ground, on a pad, or (last resort) on a stack; slid by small steps when it does not fit
            var reports = new Dictionary<string, LaneReport>();
            foreach (Lane l in input.Lanes) if (!reports.ContainsKey(l.Name)) { var rep = new LaneReport { Lane = l.Name }; reports[l.Name] = rep; r.Lanes.Add(rep); }
            float spotTop = oy + v.Window + v.SearchUp, spotBottom = oy - v.SearchDown;
            PieceShape footprint = pad.Known ? pad : PieceShape.Box(-1f, 0f, -1f, 1f, 1f, 1f);
            foreach (RangePiece s in RangeLayout.Spots(input.Lanes, ox, oz, yaw)) {
                LaneReport rep = reports[s.Lane];
                rep.Spots++;
                string label = $"{s.Lane} {s.Index + 1} '{s.Kind}'";
                double la = s.Bearing * Math.PI / 180.0;
                float fx = (float)Math.Sin(la), fz = (float)Math.Cos(la), rx = (float)Math.Cos(la), rz = -(float)Math.Sin(la);
                string? first = null, last = null;
                bool placed = false;
                foreach (KeyValuePair<float, float> o in SlideRule.Offsets(v.SlideStep, v.SpotTries, true)) {
                    float x = s.X + fx * o.Key + rx * o.Value, z = s.Z + fz * o.Key + rz * o.Value;
                    float facing = RangeLayout.Toward(x, z, ox, oz);
                    string? why = KeepClear.Inside(x, z, v.SpotClear, input.Keep) is string k ? $"it would stand in {k}'s keep-clear zone" : null;
                    SpotFitResult fit = default;
                    if (why == null) {
                        // the creature's own footprint first: real ground wherever a creature can stand; a pad's only where it cannot
                        GroundPatch own = Patch(c, x, z, v.CreatureHalf + 0.1f, v.CreatureHalf + 0.1f, facing, spotTop, spotBottom);
                        if (SpotFit.CreatureStands(own, oy, v))
                            fit = new SpotFitResult { Kind = SpotKind.Ground, BaseY = own.Min, StandY = own.Max + v.Lift, Inset = own.Spread + v.Inset, Why = "on the ground" };
                        else {
                            GroundPatch g = Patch(c, x, z, footprint.HalfX, footprint.HalfZ, facing, spotTop, spotBottom);
                            fit = SpotFit.Decide(g, oy, v, pad, tall, frame);
                            if (fit.Kind == SpotKind.Blocked) why = fit.Why;
                        }
                    }
                    if (why == null && fit.Kind != SpotKind.Ground && Cap(c, fit.Kind == SpotKind.Pad ? 1 : fit.Levels)) { why = $"the piece cap ([TestRange] MaxPieces {v.MaxPieces}) is reached"; first ??= why; last = why; break; }
                    var chain = new List<PlanBox>();
                    var chainShapes = new List<KeyValuePair<PieceRole, PieceShape>>();
                    float px = 0f, pz = 0f;
                    if (why == null && fit.Kind != SpotKind.Ground) {
                        PieceShape under = fit.Kind == SpotKind.Pad ? pad : tall;
                        Footprint.ToWorld(x, z, facing, -under.CentreX, -under.CentreZ, out px, out pz);
                        if (fit.Kind == SpotKind.Pad) {
                            chain.Add(PlanBox.Of(pad, px, fit.BaseY, pz, facing, fit.Inset, v.Margin, label));
                            chainShapes.Add(new KeyValuePair<PieceRole, PieceShape>(PieceRole.Pad, pad));
                        } else {
                            for (int lv = 0; lv < fit.Levels; lv++) {
                                bool topLevel = lv == fit.Levels - 1;
                                PieceShape ps = topLevel ? tall : frame;
                                float baseY = fit.BaseY + lv * frame.MaxY, inset = lv == 0 ? fit.Inset : -ps.MinY + v.Margin;
                                chain.Add(PlanBox.Of(ps, px, baseY, pz, facing, inset, v.Margin, label));
                                chainShapes.Add(new KeyValuePair<PieceRole, PieceShape>(topLevel ? PieceRole.Tall : PieceRole.Frame, ps));
                            }
                        }
                        foreach (PlanBox b in chain) { why = Clash(c, b, null); if (why != null) break; }
                    }
                    PlanBox body = default;
                    if (why == null) {
                        body = new PlanBox { Cx = x, Cz = z, Hx = v.CreatureHalf, Hz = v.CreatureHalf, Yaw = facing, MinY = fit.StandY + 0.1f, MaxY = fit.StandY + 0.1f + v.CreatureHeight, What = label };
                        foreach (PlanBox b in chain) if (PlanBox.Overlap(body, b)) { body.MinY = Math.Max(body.MinY, b.MaxY + 0.01f); body.MaxY = body.MinY + v.CreatureHeight; } // its own pad's posts
                        why = Clash(c, body, null);
                    }
                    if (why != null) { first ??= why; last = why; continue; }
                    // it fits: the pieces under it, then the creature (spawned after every piece)
                    string moved = o.Key == 0f && o.Value == 0f ? "" : $"; {Moved(o.Key, o.Value)} ({first})";
                    int host = -1;
                    bool lastResort = fit.Kind == SpotKind.Stack;
                    for (int lv = 0; lv < chainShapes.Count; lv++) {
                        float baseY = fit.BaseY + (fit.Kind == SpotKind.Stack ? lv * frame.MaxY : 0f);
                        float inset = lv == 0 ? fit.Inset : -chainShapes[lv].Value.MinY + v.Margin;
                        Placed p = Add(c, chainShapes[lv].Key, s.Lane, $"{label}'s {(chainShapes[lv].Key == PieceRole.Pad ? "pad" : $"stack level {lv + 1}")}", px, baseY, pz, facing, inset, chainShapes[lv].Value, fit.Why + moved, host);
                        p.Index = s.Index; p.LastResort = lastResort;
                        host = c.R.Pieces.Count - 1;
                    }
                    if (lastResort) r.Notes.Add($"{label}: LAST RESORT: {fit.Why}: {fit.Levels} level(s) of '{input.Pieces.NameOf(fit.Levels > 1 ? PieceRole.Frame : PieceRole.Tall)}'{(fit.Levels > 1 ? $" under '{input.Pieces.NameOf(PieceRole.Tall)}'" : "")}");
                    c.Creatures.Add(new Placed { Role = PieceRole.Creature, Lane = s.Lane, Label = label, Kind = s.Kind, Index = s.Index, X = x, Y = fit.StandY, Z = z, Yaw = facing,
                        Host = host, Free = s.Free, Dummy = RangeKinds.IsDummy(s.Kind), LastResort = lastResort, Note = fit.Why + moved, Box = body });
                    switch (fit.Kind) { case SpotKind.Pad: rep.OnPads++; break; case SpotKind.Stack: rep.Stacks++; break; default: rep.OnGround++; break; }
                    if (moved.Length > 0) rep.Moved++;
                    placed = true;
                    break;
                }
                if (!placed) { rep.Skipped++; r.Skipped.Add($"{label}: skipped ({first}{(last != first && last != null ? $"; last try: {last}" : "")})"); }
            }

            // ---- the backstops: walls behind each lane, on the ground, never in the line to another lane's target
            var lampHosts = new Dictionary<string, List<KeyValuePair<int, float>>>();
            if (wall.Known) {
                float segment = wall.MaxX - wall.MinX, turn = input.Pieces.TurnOf(PieceRole.Wall);
                foreach (Lane lane in input.Lanes) {
                    List<WallSpot> walls = BackstopRule.Walls(lane, ox, oz, yaw, segment, v);
                    LaneReport rep = reports[lane.Name];
                    rep.WallsWanted += walls.Count;
                    var hosts = new List<KeyValuePair<int, float>>();
                    lampHosts[lane.Name] = hosts;
                    double la = (yaw + lane.Bearing) * Math.PI / 180.0;
                    float fx = (float)Math.Sin(la), fz = (float)Math.Cos(la), rx = (float)Math.Cos(la), rz = -(float)Math.Sin(la);
                    foreach (WallSpot w in walls) {
                        string label = $"{lane.Name}'s backstop wall {w.Index + 1} of {w.Of}";
                        string? first = null;
                        bool placed = false;
                        if (Cap(c, 1)) { r.Skipped.Add($"{label}: left out (the piece cap, [TestRange] MaxPieces {v.MaxPieces}, is reached)"); continue; }
                        foreach (KeyValuePair<float, float> o in SlideRule.Offsets(v.SlideStep, v.SlideTries, false, true)) {
                            float px = w.X + fx * o.Key + rx * o.Value, pz = w.Z + fz * o.Key + rz * o.Value;
                            string? why = KeepClear.Inside(px, pz, segment * 0.5f, input.Keep) is string k ? $"it would stand in {k}'s keep-clear zone" : null;
                            GroundPatch g = default;
                            if (why == null) {
                                Footprint.ToWorld(px, pz, w.Facing, wall.CentreX, wall.CentreZ, out float gx, out float gz);
                                g = Patch(c, gx, gz, wall.HalfX, Math.Min(wall.HalfZ, 0.3f), w.Facing, spotTop, spotBottom);
                                if (g.Found == 0) why = "no ground there";
                                else if (!g.Complete) why = "the ground ends under it (an edge)";
                                else if (Math.Abs(g.Min - oy) > v.Window) why = $"its ground is {M(Math.Abs(g.Min - oy))} m from yours";
                                else if (g.Spread > v.WallSpread) why = $"the ground varies {M(g.Spread)} m along it";
                            }
                            PlanBox box = default;
                            if (why == null) {
                                box = PlanBox.Of(wall, px, g.Min, pz, w.Facing + turn, g.Spread + v.Inset, v.Margin, label);
                                foreach (Placed t in c.Creatures)
                                    if (t.Lane != lane.Name && PlanBox.Crosses(box, ox, oz, t.X, t.Z, v.SightHalfWidth)) { why = $"it would stand in the line to {t.Label}"; break; }
                            }
                            if (why == null) why = Clash(c, box, null);
                            if (why != null) { first ??= why; continue; }
                            Add(c, PieceRole.Wall, lane.Name, label, px, g.Min, pz, w.Facing + turn, g.Spread + v.Inset, wall, o.Key == 0f && o.Value == 0f ? "" : $"{Moved(o.Key, o.Value)} ({first})");
                            hosts.Add(new KeyValuePair<int, float>(c.R.Pieces.Count - 1, w.Facing));
                            rep.Walls++;
                            if (o.Key != 0f || o.Value != 0f) rep.Moved++;
                            placed = true;
                            break;
                        }
                        if (!placed) r.Skipped.Add($"{label}: left out ({first})");
                    }
                }
            }

            // ---- lamps on the walls' fronts
            if (lamp.Known)
                foreach (Lane lane in input.Lanes) {
                    if (!lampHosts.TryGetValue(lane.Name, out List<KeyValuePair<int, float>> hosts)) continue;
                    foreach (int wi in BackstopRule.Lamps(hosts.Count, v.LampEvery)) {
                        int hi = hosts[wi].Key;
                        Placed w = c.R.Pieces[hi];
                        float facing = hosts[wi].Value;
                        string label = $"{lane.Name}'s lamp on wall {wi + 1}";
                        if (Cap(c, 1)) { r.Skipped.Add($"{label}: left out (the piece cap is reached)"); continue; }
                        Footprint.ToWorld(w.X, w.Z, facing, 0f, v.LampOut, out float lx, out float lz);
                        float ly = w.Y + v.LampHeight, lyaw = facing + input.Pieces.TurnOf(PieceRole.Lamp);
                        string? why = Clash(c, PlanBox.Of(lamp, lx, ly, lz, lyaw, 0f, v.Margin * 0.2f, label), new[] { hi });
                        if (why != null) { r.Skipped.Add($"{label}: left out ({why})"); continue; }
                        Add(c, PieceRole.Lamp, lane.Name, label, lx, ly, lz, lyaw, 0f, lamp, "", hi);
                        reports[lane.Name].Lamps++;
                    }
                }

            // ---- the creatures last: their pads stand first
            foreach (Placed t in c.Creatures) r.Pieces.Add(t);
        }
    }
}
