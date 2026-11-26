// Unity-free spectral-arrow rules (arc model, Burst pattern, hit batching, experience ledger; Ricochet's ground skip and creature jump).
using System;
using System.Collections.Generic;

namespace BluesArcheryOverhaul {
    /// <summary>The game's flight in closed form: speed constant, gravity turns the direction (dTheta/dt = -(g/s) cos Theta).</summary>
    internal static class ArcModel {
        internal const double MaxElevation = 89.9 * Math.PI / 180.0;

        static double Clamp(double e) => e > MaxElevation ? MaxElevation : e < -MaxElevation ? -MaxElevation : e;
        static double Asinh(double x) => Math.Log(x + Math.Sqrt(x * x + 1));
        static double Gd(double u) => Math.Atan(Math.Sinh(u));
        static double GdInv(double w) => Asinh(Math.Tan(w));
        // ln(cosh x) without overflow
        static double LnCosh(double x) { x = Math.Abs(x); return x + Math.Log(1 + Math.Exp(-2 * x)) - Math.Log(2); }

        internal static void Point(double speed, double elevation, double t, double g, out double along, out double up) {
            double th = Clamp(elevation);
            if (g <= 0 || speed <= 0) { along = speed * Math.Cos(th) * t; up = speed * Math.Sin(th) * t; return; }
            double k = g / speed, u0 = GdInv(th), u = u0 - k * t;
            along = speed / k * (Gd(u0) - Gd(u));
            up = speed / k * (LnCosh(u0) - LnCosh(u));
        }

        internal static void Velocity(double speed, double elevation, double t, double g, out double along, out double up) {
            double th = Clamp(elevation);
            if (g <= 0 || speed <= 0) { along = speed * Math.Cos(th); up = speed * Math.Sin(th); return; }
            double u = GdInv(th) - g / speed * t;
            along = speed / Math.Cosh(u);
            up = speed * Math.Tanh(u);
        }

        /// <summary>Seconds until the arrow has travelled <paramref name="along"/> metres horizontally; NaN if it never gets there.</summary>
        internal static double TimeToAlong(double speed, double elevation, double along, double g) {
            double th = Clamp(elevation);
            if (g <= 0 || speed <= 0) {
                double vx = speed * Math.Cos(th);
                return vx > 0 ? along / vx : double.NaN;
            }
            double k = g / speed, u0 = GdInv(th);
            double w = Gd(u0) - along * k / speed;
            if (w <= -Math.PI / 2 + 1e-9) return double.NaN;
            return (u0 - GdInv(w)) / k;
        }
    }

    internal enum SpectralKind { Inner, Outer, Echo, Pellet }

    /// <summary>A Quick main's needles: needle i of n leaves the arrow's line at <see cref="Degrees"/>, turned i x 360 / n
    /// degrees round it (a cross for four). The flying snap and Quickdraw 4's line use the same pattern.</summary>
    internal static class NeedlePattern {
        internal const float Degrees = 1.5f;

        internal static float Azimuth(int index, int count) => count <= 0 ? 0f : (index * 360f / count) % 360f;
    }

    internal static class SpectralLife {
        internal static bool Ended(SpectralKind kind, float distanceFromStart, float age, float range, float life) =>
            age >= life || (kind == SpectralKind.Pellet && distanceFromStart >= range);
    }

    internal static class BurstPattern {
        internal const int Count = 15, Inner = 5;
        // Play-test 6: the cone at 70% of the first design's 2.5 and 5 degrees.
        internal const float InnerDegrees = 1.75f, OuterDegrees = 3.5f, PelletShare = 0.04f, Range = 30f;

        /// <summary>Pellet <paramref name="index"/> of the fifteen, on every machine: how far it leaves the arrow's line
        /// (<paramref name="angleDegrees"/>: the inner five at the inner angle, the outer ten at the outer) and which way round
        /// the arrow (<paramref name="azimuthDegrees"/>: 0 = right, 90 = up). Pellet p belongs to ghost p's ring
        /// (VolleyMath: the five inner ghosts, the ten outer) and is turned half a ring step from it, midway between ghost p
        /// and the next: with the ghosts the inner ring is 10 evenly round the arrow, the outer ring 20 (fix E after play-test
        /// 7, the user: "not in a correct circle"; before it the pellets left on the ghosts' own turns: 90 + 72 p and 36 p).
        /// The ghosts' turns are read from VolleyMath.Azimuth, the one definition.</summary>
        internal static void Pellet(int index, float innerDegrees, float outerDegrees, out float angleDegrees, out float azimuthDegrees) {
            angleDegrees = index < Inner ? innerDegrees : outerDegrees;
            double turn = (VolleyMath.Azimuth(index) + VolleyMath.Step(index) * 0.5) % 360.0;
            azimuthDegrees = (float)(turn < 0.0 ? turn + 360.0 : turn);
        }

        /// <summary>Where pellet <paramref name="index"/> sits on the bow and leaves from, relative to the real arrow's tip in
        /// the plane across the aim (x = right, y = up): on its ghosts' own ring (the radius the caller gives for that ring,
        /// the same as VolleyMath.TipOffset's), at its own turn round the arrow (<see cref="Pellet"/>), so between two ghosts.
        /// The held arrow (looks) and the flying pellet (every machine) both start here: what shows is what leaves. A radius
        /// that is not a positive number gives the tip itself.</summary>
        internal static void TipOffset(int index, float innerRadius, float outerRadius, out float x, out float y) {
            float r = index < Inner ? innerRadius : outerRadius;
            if (!(r > 0f) || float.IsInfinity(r)) { x = 0f; y = 0f; return; }
            Pellet(index, 0f, 0f, out _, out float azimuth);
            double phi = azimuth * Math.PI / 180.0;
            x = (float)(Math.Cos(phi) * r);
            y = (float)(Math.Sin(phi) * r);
        }

        // ---- looks only (fix batch C, play-test 7: "make it show all arrows"): the fifteen pellets as their own arrows on the bow

        /// <summary>The rack's ripple outward: seconds after the rack at which pellet <paramref name="index"/>'s arrow starts to
        /// slide out. The five inner ones first (one after another over the first quarter), then the ten outer ones in turn, the
        /// last as <paramref name="rippleSeconds"/> ends.</summary>
        internal static float HeldDelay(int index, float rippleSeconds) {
            if (!(rippleSeconds > 0f) || float.IsInfinity(rippleSeconds) || index <= 0) return 0f;
            if (index < Inner) return rippleSeconds * 0.3f * index / Inner;
            int k = Math.Min(index, Count - 1) - Inner;
            return rippleSeconds * (0.4f + 0.6f * k / (Count - Inner - 1));
        }

        /// <summary>The release: how much of the gap between a pellet's held arrow and its real path is left
        /// <paramref name="age"/> seconds on (1 at the release, easing to 0 at <paramref name="seconds"/>), so its streak starts
        /// where the held arrow was and joins the path without a jump.</summary>
        internal static float Join(float age, float seconds) {
            if (!(seconds > 0f) || !(age < seconds)) return 0f;
            if (age <= 0f) return 1f;
            float t = age / seconds;
            return 1f - t * t * (3f - 2f * t);
        }
    }

    /// <summary>Looks only: a repeatable number from a seed (the release's arrow id), an index and a salt, so a look that wants
    /// variety (Shatter's fan, Ricochet's arcs) is the same every time it is asked and on every machine. Never used for anything
    /// that counts.</summary>
    internal static class LookSeed {
        /// <summary>In [0, 1).</summary>
        internal static float Unit(uint seed, int index, int salt) {
            unchecked {
                uint h = seed * 0x9E3779B1u ^ (uint)(index + 1) * 0x85EBCA6Bu ^ (uint)(salt + 1) * 0xC2B2AE35u;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }
    }

    /// <summary>Looks only (G4, play-test 8: "the ghosts turn gold ring by ring with a thump"): what a multi-arrow main's hold stages
    /// show on the bow and in flight. The stage itself is VolleyHold's (Release.cs); this only says which ring an arrow is on, the
    /// level each ring shows at a stage (0 = its own colour, 1 = gold, 2 and up = brighter gold with a glint on the head), when each
    /// arrow of a ring takes its turn in the sweep round it, and the shimmer that creeps along a shaft toward the next stage.
    /// With N Charge Shot orbs the archer sees N steps: the inner ring, the outer ring, then everything brighter; with fewer orbs
    /// the last stage owned is all gold (1 orb: all gold at stage 1; 2 orbs: inner, then outer).</summary>
    internal static class GoldStages {
        internal const int Inner = 0, Outer = 1;
        /// <summary>Points a shimmering shaft is drawn with (a line's colours are set at its points only).</summary>
        internal const int ShaftPoints = 6;

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>The ring ghost <paramref name="ghost"/> sits on: the five inner ghosts, the ten outer ones (VolleyMath).</summary>
        internal static int GhostRing(int ghost) => ghost < VolleyMath.Inner ? Inner : Outer;

        /// <summary>The ring Burst's pellet <paramref name="pellet"/> sits on and leaves from: its ghosts' own (BurstPattern).</summary>
        internal static int PelletRing(int pellet) => pellet < BurstPattern.Inner ? Inner : Outer;

        /// <summary>Rings showing with this many ghosts: one with the five, two once an outer ghost shows.</summary>
        internal static int Rings(int ghosts) => ghosts > VolleyMath.Inner ? 2 : 1;

        /// <summary>The stage at which every ring showing is gold: 2 with two rings and two or more stages to hold for, else 1.</summary>
        internal static int AllGoldAt(int orbs, int rings) => orbs >= 2 && rings >= 2 ? 2 : 1;

        /// <summary>What ring <paramref name="ring"/> shows at hold stage <paramref name="stage"/> for an archer with
        /// <paramref name="orbs"/> stages to hold for (at most three) and <paramref name="rings"/> rings showing: 0 its own colour,
        /// 1 gold, 2 and up brighter gold (one more for each stage past all gold). It never falls as the stage rises.</summary>
        internal static int Level(int ring, int stage, int orbs, int rings) {
            int n = orbs > Complements.MaxOrbs ? Complements.MaxOrbs : orbs;
            if (n <= 0 || stage <= 0) return 0;
            int s = stage > n ? n : stage;
            int all = AllGoldAt(n, rings);
            if (s > all) return 1 + (s - all);
            int goldAt = all == 2 && ring != Inner ? 2 : 1;
            return s >= goldAt ? 1 : 0;
        }

        /// <summary>The head's small gold glint shows from the brighter level on.</summary>
        internal static bool Glints(int level) => level >= 2;

        /// <summary>The sweep round a ring: seconds after its stage at which the arrow at <paramref name="azimuthDegrees"/> (0 = right,
        /// 90 = up, as VolleyMath.Azimuth and BurstPattern.Pellet give them) starts to turn. The top arrow first, then clockwise as
        /// the archer sees it, the last just before <paramref name="sweepSeconds"/> ends. No sweep time: all at once.</summary>
        internal static float SweepDelay(double azimuthDegrees, float sweepSeconds) {
            if (!(sweepSeconds > 0f) || float.IsInfinity(sweepSeconds) || double.IsNaN(azimuthDegrees) || double.IsInfinity(azimuthDegrees)) return 0f;
            double turn = (90.0 - azimuthDegrees) % 360.0;
            if (turn < 0.0) turn += 360.0;
            if (turn >= 360.0 - 1e-9) turn = 0.0;
            return (float)(turn / 360.0) * sweepSeconds;
        }

        /// <summary>How far one arrow has turned to its new level (0 to 1, eased) <paramref name="sinceStage"/> seconds after the
        /// stage: nothing until its <paramref name="delay"/>, done <paramref name="turnSeconds"/> later (a switch with no turn
        /// time). A broken age shows the finished colour.</summary>
        internal static float Turned(float sinceStage, float delay, float turnSeconds) {
            if (float.IsNaN(sinceStage)) return 1f;
            float late = sinceStage - (delay > 0f ? delay : 0f);
            if (late < 0f) return 0f;
            if (!(turnSeconds > 0f)) return 1f;
            float t = Clamp01(late / turnSeconds);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The level an arrow shows on its way from <paramref name="before"/> to <paramref name="now"/>
        /// (<paramref name="turned"/> 0 to 1; not a number shows the new one).</summary>
        internal static float Shown(int before, int now, float turned) => before + (now - before) * (float.IsNaN(turned) ? 1f : Clamp01(turned));

        /// <summary>The shimmer toward the next stage: how strongly the shaft is tinted at <paramref name="u"/> (0 = the back end of
        /// what is drawn, 1 = the tip) when the hold is <paramref name="toward"/> (0 to 1, VolleyHold.Toward) of the way there. It
        /// creeps from the back toward the tip behind a soft front <paramref name="soft"/> wide: nothing at 0, the whole shaft at 1.</summary>
        internal static float Shimmer(float u, float toward, float soft) {
            if (float.IsNaN(u) || float.IsNaN(toward) || !(toward > 0f)) return 0f;
            float t = toward > 1f ? 1f : toward;
            if (!(soft > 0f) || float.IsInfinity(soft)) return u < t || t >= 1f ? 1f : 0f;
            return Clamp01((t * (1f + soft) - u) / soft);
        }
    }

    /// <summary>A Charge main's heavy arrows (two per multi-arrow orb): their own layout round the real arrow, on every
    /// machine (the held look, the server's flight and the clients' visuals), chosen by how many there are: two sit left and
    /// right of the real arrow, four make a plus (left, right, top, bottom), six a hexagon that includes left and right.
    /// Every layout is mirror-symmetric left/right, centred on the real arrow, every tip the same <see cref="Radius"/> from
    /// the real tip, and every arrow leaves the real arrow's line by <see cref="SpreadDegrees"/>, straight away from it.
    /// An odd count (a spectator's look only) takes the first ones of the next even layout. (Before play-test 6 they rode
    /// the volley's ghost slots; until the final review four sat left, right, upper right and lower left.)</summary>
    internal static class HeavyPattern {
        internal const int Max = 6;
        internal const float Radius = 0.045f, SpreadDegrees = 1.2f;

        // degrees round the real arrow in the plane across the aim: 0 = right, 90 = up; each pair is two opposite points
        static readonly float[] pair = { 180f, 0f };
        static readonly float[] plus = { 180f, 0f, 90f, 270f };
        static readonly float[] hexagon = { 180f, 0f, 60f, 240f, 120f, 300f };

        static float[] LayoutFor(int count) => count <= 2 ? pair : count <= 4 ? plus : hexagon;

        /// <summary>Where heavy arrow <paramref name="index"/> of <paramref name="count"/> sits round the real arrow, in
        /// degrees (0 = right, 90 = up): also the way it leans off the arrow's line in flight.</summary>
        internal static float Azimuth(int index, int count) {
            float[] a = LayoutFor(count);
            return a[((index % a.Length) + a.Length) % a.Length];
        }

        /// <summary>Heavy arrow <paramref name="index"/> of <paramref name="count"/>: its tip relative to the real arrow's tip,
        /// in the plane across the aim (x = right, y = up). A radius that is not a positive number gives the real tip itself.</summary>
        internal static void TipOffset(int index, int count, float radius, out float x, out float y) {
            if (!(radius > 0f) || float.IsInfinity(radius)) { x = 0f; y = 0f; return; }
            double rad = Azimuth(index, count) * Math.PI / 180.0;
            x = (float)(Math.Cos(rad) * radius);
            y = (float)(Math.Sin(rad) * radius);
        }
    }

    internal static class SpectralMath {
        /// <summary>A share becomes an effectiveness (at most 1) and a damage multiplier for what is left over.</summary>
        internal static void Split(float shares, out float effectiveness, out float extraMultiplier) {
            if (!(shares > 0f)) { effectiveness = 0f; extraMultiplier = 0f; return; }
            effectiveness = Math.Min(1f, shares);
            extraMultiplier = shares / effectiveness;
        }

        /// <summary>Index of the point with the most other points within the radius (first on ties), -1 when empty.</summary>
        internal static int Densest(IList<float> xs, IList<float> ys, IList<float> zs, float radius) {
            int n = xs.Count, best = -1, bestCount = -1;
            float r2 = radius * radius;
            for (int i = 0; i < n; i++) {
                int c = 0;
                for (int j = 0; j < n; j++) {
                    if (j == i) continue;
                    float dx = xs[i] - xs[j], dy = ys[i] - ys[j], dz = zs[i] - zs[j];
                    if (dx * dx + dy * dy + dz * dz <= r2) c++;
                }
                if (c > bestCount) { bestCount = c; best = i; }
            }
            return best;
        }
    }

    /// <summary>Shares and hit counts per creature for one tick.</summary>
    internal sealed class HitBatch {
        readonly Dictionary<int, float> sums = new Dictionary<int, float>();
        readonly Dictionary<int, int> hits = new Dictionary<int, int>();
        readonly Dictionary<int, List<float>> each = new Dictionary<int, List<float>>();
        static readonly List<float> none = new List<float>();

        internal void Add(int key, float share) => Add(key, share, true);

        /// <summary>H1: <paramref name="jumps"/> false (a homing volley's core arrow) adds the share to the hit but not to the arrows
        /// that jump (<see cref="Shares"/>): the core cluster's landing stands in for the real arrow's one jump instead.</summary>
        internal void Add(int key, float share, bool jumps) {
            sums.TryGetValue(key, out float s); sums[key] = s + share;
            hits.TryGetValue(key, out int h); hits[key] = h + 1;
            if (!each.TryGetValue(key, out List<float> l)) each[key] = l = new List<float>();
            if (jumps) l.Add(share);
        }
        /// <summary>Each landed arrow's own share, in the order they landed (play-test 7: every arrow's Ricochet jump).</summary>
        internal IList<float> Shares(int key) => each.TryGetValue(key, out List<float> l) ? l : none;
        internal int Count => sums.Count;
        internal IEnumerable<int> Keys => sums.Keys;
        internal float Sum(int key) => sums.TryGetValue(key, out float s) ? s : 0f;
        internal int Hits(int key) => hits.TryGetValue(key, out int h) ? h : 0;
        internal void Clear() { sums.Clear(); hits.Clear(); each.Clear(); }
    }

    /// <summary>Per release: how many spectral arrows are still flying and the share each creature took, so experience is paid once.</summary>
    internal sealed class ReleaseLedger {
        internal const int Cap = 64;
        sealed class Entry { internal int Open; internal readonly Dictionary<int, float> Shares = new Dictionary<int, float>(); }
        readonly Dictionary<uint, Entry> entries = new Dictionary<uint, Entry>();
        readonly List<uint> order = new List<uint>();

        internal int OpenCount => entries.Count;

        internal void Open(uint release, int arrows) {
            if (entries.ContainsKey(release)) order.Remove(release);
            while (entries.Count >= Cap && order.Count > 0) { entries.Remove(order[0]); order.RemoveAt(0); }
            entries[release] = new Entry { Open = arrows };
            order.Add(release);
        }

        internal void Add(uint release, int creature, float share) {
            if (!entries.TryGetValue(release, out Entry e)) return;
            e.Shares.TryGetValue(creature, out float s); e.Shares[creature] = s + share;
        }

        /// <summary>True exactly when the last open arrow of the release ends.</summary>
        internal bool ArrowDone(uint release) {
            if (!entries.TryGetValue(release, out Entry e) || e.Open <= 0) return false;
            e.Open--;
            return e.Open == 0;
        }

        internal Dictionary<int, float> Take(uint release) {
            if (!entries.TryGetValue(release, out Entry e)) return new Dictionary<int, float>();
            entries.Remove(release); order.Remove(release);
            return e.Shares;
        }
    }

    internal static class SpectralBudget {
        internal static bool CastThisTick(int live, int cap, int index, int tick) => live <= cap || (index + tick) % 2 == 1;
    }

    // ---------------------------------------------------------------- batch 3: Ricochet (spec 2026-11-16, redefined by the user)

    /// <summary>Ricochet (Multi side orb off Burst, Multi main only). The user's words: "if it hits the ground have it bounce
    /// following its direction, then if it hits a enemy have it bounce to a different creature".
    /// Ground skip: a ghost or pellet that hits a world surface skips once and flies on along its own direction: the incoming
    /// direction's part along the surface, raised SkipDegrees away from it, at SpeedKeep of its speed. Coming in steeper than
    /// MaxHitDegrees (from the surface) it ends as before. No creature is aimed at, so every machine skips the same way from the
    /// same hit point, normal and incoming direction.</summary>
    internal static class RicochetRule {
        /// <summary>Fix batch D: any owner (off the Multi main at [Complements] SideOrbOffPathShare, SideOrbShare).</summary>
        internal static bool Applies(Build b) => (b.Sides & SideOrbs.Ricochet) != 0;

        /// <summary>The owner's main path is Ricochet's own line (its ghosts and pellets skip; full strength).</summary>
        internal static bool OnPath(Build b) => b.Main == MainPath.Multi;

        /// <summary>A ghost (inner or outer) or a pellet that has not skipped; never a needle (Echo kind) or a heavy arrow.</summary>
        internal static bool CanBounce(SpectralKind kind, bool bounced, bool heavy) =>
            !bounced && !heavy && (kind == SpectralKind.Inner || kind == SpectralKind.Outer || kind == SpectralKind.Pellet);

        /// <summary>Fix batch D: off the Multi main (<paramref name="onPath"/> false) the ground skip is for the build's own spectral
        /// arrows: a Quick main's needles (Echo kind) and a Charge main's heavy arrows; once each.</summary>
        internal static bool CanBounce(SpectralKind kind, bool bounced, bool heavy, bool onPath) =>
            onPath ? CanBounce(kind, bounced, heavy) : !bounced && (heavy || kind == SpectralKind.Echo);

        static bool Listed(string name, string list) {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(list)) return false;
            foreach (string raw in list.Split(';'))
                if (string.Equals(raw.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The research's rule (research-targets.md section 4), its lists from the settings: never off a creature or a
        /// player; off anything whose impact channels include one of <paramref name="bounceChannels"/> (wood, rock); off anything on
        /// one of <paramref name="bounceLayers"/> (the ground); off a static world object (no network entity) when
        /// <paramref name="bounceStatic"/>. Everything else (items, structures) stops it as before.</summary>
        internal static bool Surface(bool playerHealth, bool creatureHealth, string layer, bool networkEntity, IList<string> channels,
                                     string bounceLayers, string bounceChannels, bool bounceStatic) {
            if (playerHealth || creatureHealth) return false;
            if (channels != null) foreach (string c in channels) if (Listed(c, bounceChannels)) return true;
            if (Listed(layer, bounceLayers)) return true;
            return bounceStatic && !networkEntity;
        }

        /// <summary>Degrees between the incoming direction and the surface (0 = grazing, 90 = straight in); NaN when it is not
        /// coming into the surface at all.</summary>
        internal static double HitDegrees(double ix, double iy, double iz, double nx, double ny, double nz) {
            double il = Math.Sqrt(ix * ix + iy * iy + iz * iz), nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (il < 1e-9 || nl < 1e-9) return double.NaN;
            double into = -(ix * nx + iy * ny + iz * nz) / (il * nl);
            if (into <= 0) return double.NaN;
            return Math.Asin(Math.Min(1, into)) * 180.0 / Math.PI;
        }

        /// <summary>The skip: false (it ends as before) when it is not coming into the surface or came in steeper than
        /// <paramref name="maxHitDegrees"/>; else the new unit direction: the incoming direction's part along the surface,
        /// normalised, raised <paramref name="skipDegrees"/> away from the surface.</summary>
        internal static bool Skip(double ix, double iy, double iz, double nx, double ny, double nz, double skipDegrees, double maxHitDegrees,
                                  out double dx, out double dy, out double dz) {
            dx = dy = dz = 0;
            double hit = HitDegrees(ix, iy, iz, nx, ny, nz);
            if (double.IsNaN(hit) || hit > maxHitDegrees) return false;
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= nl; ny /= nl; nz /= nl;
            double along = ix * nx + iy * ny + iz * nz;
            double tx = ix - nx * along, ty = iy - ny * along, tz = iz - nz * along, tl = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (tl < 1e-9) return false;
            tx /= tl; ty /= tl; tz /= tl;
            double a = Math.Max(0, Math.Min(89, skipDegrees)) * Math.PI / 180.0;
            dx = Math.Cos(a) * tx + Math.Sin(a) * nx; dy = Math.Cos(a) * ty + Math.Sin(a) * ny; dz = Math.Cos(a) * tz + Math.Sin(a) * nz;
            return true;
        }

        /// <summary>The speed after a skip: <paramref name="keep"/> (0..1) of the speed before.</summary>
        internal static float SkipSpeed(float speed, float keep) => speed * (float.IsNaN(keep) ? 1f : keep < 0f ? 0f : keep > 1f ? 1f : keep);
    }

    /// <summary>A creature a Ricochet jump may land on: its id, where this machine sees it, whether this release already struck it,
    /// and whether it is a player (never a candidate).</summary>
    internal readonly struct JumpCandidate {
        public readonly uint Id;
        public readonly float X, Y, Z;
        public readonly bool Struck, Player;
        public JumpCandidate(uint id, float x, float y, float z, bool struck, bool player) { Id = id; X = x; Y = y; Z = z; Struck = struck; Player = player; }
    }

    /// <summary>Ricochet's creature jump (the former Chain). Play-test 7 (the user: "Anything that lands should ricochet to the next
    /// enemy", "every arrow that hits"): every arrow of the release that lands on a creature jumps, for [Ricochet] JumpShare of
    /// that arrow's own damage, to one of the nearest other creatures within JumpMetres; successive jumps from one struck creature
    /// go round the nearest JumpSpread in turn. A jump never lands on the creature it came from. H5b (the owner, 2026-11-18: "Have
    /// Ricochet be able to bounce to 2 enemies max, not just 1 more"): a jump that lands on a creature jumps on from it while its
    /// chain is shorter than [Ricochet] Bounces (<see cref="MayBounce"/>), never to a creature already in its chain
    /// (<see cref="JumpChain"/>), for JumpShare of what it dealt there (<see cref="ShareAt"/>); <see cref="JumpWalk"/> walks it.</summary>
    internal static class JumpRule {
        /// <summary>H5b: [Ricochet] Bounces is held to 1 (one jump, as before H5) .. this.</summary>
        internal const int MaxBounces = 4;

        /// <summary>H5b: the [Ricochet] Bounces setting held to 1 .. <see cref="MaxBounces"/>.</summary>
        internal static int Bounces(int setting) => setting < 1 ? 1 : setting > MaxBounces ? MaxBounces : setting;

        /// <summary>The jump targets from creature <paramref name="self"/> at (sx, sy, sz), in turn order: other candidates within
        /// <paramref name="range"/>, creatures nearest first (a tie to the lower id), then players (the server lists one only when
        /// PvP lets the mod's hits land on them) nearest first; at most <paramref name="spread"/> (at least 1); each id once.
        /// Indexes into <paramref name="candidates"/>. A pure function of its inputs.</summary>
        internal static List<int> Targets(uint self, float sx, float sy, float sz, IList<JumpCandidate> candidates, float range, int spread) =>
            Targets(self, sx, sy, sz, candidates, range, spread, null);

        /// <summary>H5b: as above, leaving out every creature already in <paramref name="chain"/> (never back to where the chain
        /// came from; null or a chain of <paramref name="self"/> alone = the plain form, bit for bit).</summary>
        internal static List<int> Targets(uint self, float sx, float sy, float sz, IList<JumpCandidate> candidates, float range, int spread, JumpChain? chain) {
            var picked = new List<int>();
            if (candidates == null) return picked;
            var dist = new Dictionary<int, double>();
            for (int i = 0; i < candidates.Count; i++) {
                JumpCandidate c = candidates[i];
                if (c.Id == self || (chain != null && chain.Contains(c.Id))) continue;
                double dx = c.X - sx, dy = c.Y - sy, dz = c.Z - sz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (double.IsNaN(d) || d > range) continue;
                int same = picked.FindIndex(k => candidates[k].Id == c.Id);
                if (same >= 0) {
                    if (d >= dist[picked[same]]) continue;
                    picked.RemoveAt(same);
                }
                picked.Add(i); dist[i] = d;
            }
            picked.Sort((x, y) => {
                JumpCandidate a = candidates[x], b = candidates[y];
                if (a.Player != b.Player) return a.Player ? 1 : -1;
                int c = dist[x].CompareTo(dist[y]);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            int max = Math.Max(1, spread);
            if (picked.Count > max) picked.RemoveRange(max, picked.Count - max);
            return picked;
        }

        /// <summary>The landed arrows' jump damages spread over <paramref name="targets"/> targets in turn: arrow i goes to target
        /// (first + i) mod targets, where <paramref name="first"/> is how many jumps this creature has passed on already. The sum
        /// per target (a broken or negative value adds nothing).</summary>
        internal static float[] Spread(int first, IList<float> arrows, int targets) {
            var sums = new float[Math.Max(0, targets)];
            if (targets <= 0 || arrows == null) return sums;
            int start = ((first % targets) + targets) % targets;
            for (int i = 0; i < arrows.Count; i++) {
                float v = arrows[i];
                if (v > 0f && !float.IsInfinity(v)) sums[(start + i) % targets] += v;
            }
            return sums;
        }

        /// <summary>H5b: as <see cref="Spread"/>, keeping the arrows themselves: the arrows (their values, in their order) each
        /// target takes, so each target's jump can bounce on with its own arrows. Each list's sum, added up in order from 0, is
        /// Spread's sum bit for bit.</summary>
        internal static List<float>[] Split(int first, IList<float> arrows, int targets) {
            var lists = new List<float>[Math.Max(0, targets)];
            for (int k = 0; k < lists.Length; k++) lists[k] = new List<float>();
            if (targets <= 0 || arrows == null) return lists;
            int start = ((first % targets) + targets) % targets;
            for (int i = 0; i < arrows.Count; i++) {
                float v = arrows[i];
                if (v > 0f && !float.IsInfinity(v)) lists[(start + i) % targets].Add(v);
            }
            return lists;
        }

        /// <summary>A jump's damage as a share of the arrow's: <paramref name="share"/> of what the arrow dealt
        /// (<paramref name="taken"/>, in the same units); never negative.</summary>
        internal static float Damage(float taken, float share) {
            float d = taken * share;
            return d > 0f && !float.IsInfinity(d) ? d : 0f;
        }

        /// <summary>H5b: the share of an arrow's own damage that the jump at <paramref name="depth"/> deals (1 = the first jump,
        /// from the creature the arrow struck): <paramref name="jumpShare"/> x <paramref name="sideShare"/> (the side orb's share
        /// off its main path, Targets.Release.RicochetShare) for the first, and JumpShare of what the jump before dealt for each
        /// one after (the side orb's share is in the first already, so it is not taken twice). 0 below depth 1 or for a share that
        /// is not positive. Depth 1 is exactly the jump before H5 (jumpShare x sideShare).</summary>
        internal static float ShareAt(int depth, float jumpShare, float sideShare) {
            if (depth < 1 || !(jumpShare > 0f) || !(sideShare > 0f) || float.IsInfinity(jumpShare) || float.IsInfinity(sideShare)) return 0f;
            float f = jumpShare * sideShare;
            for (int d = 1; d < depth && d < MaxBounces; d++) f *= jumpShare;
            return f;
        }

        /// <summary>Only a real arrow's or a spectral hit queues a jump where it lands: a jump's own hit never queues another (it
        /// bounces on through <see cref="MayBounce"/> instead) and a Shatter blast never jumps (batch 4: neither orb feeds the
        /// other). Checked where a jump is queued (Ricochet.ServerStruck).</summary>
        internal static bool MayJump(HitSource source) => source == HitSource.RealArrow || source == HitSource.Spectral;

        /// <summary>H5b: a jump that landed on a creature at chain depth <paramref name="depth"/> (1 = the first jump) jumps on
        /// from it while depth is below <paramref name="bounces"/> (held by <see cref="Bounces"/>). Only a jump bounces on: a
        /// Shatter blast never feeds one, and an arrow's own hit is <see cref="MayJump"/>'s.</summary>
        internal static bool MayBounce(HitSource source, int depth, int bounces) => source == HitSource.Jump && depth >= 1 && depth < Bounces(bounces);

        /// <summary>H5c: [Ricochet] BounceDelaySeconds is held to 0 .. this (a guard: a bounce never waits long on the server).</summary>
        internal const float MaxBounceDelaySeconds = 2f;

        /// <summary>H5c: the pause a bounce makes on the creature it leaves ([Ricochet] BounceDelaySeconds) held to 0 ..
        /// <see cref="MaxBounceDelaySeconds"/>; a broken value is 0.</summary>
        internal static float BounceDelay(float setting) =>
            float.IsNaN(setting) || setting < 0f ? 0f : setting > MaxBounceDelaySeconds ? MaxBounceDelaySeconds : setting;

        /// <summary>H5c (the owner, 2026-11-18, after play-testing 0.17.12: "Have the bounce a bit after from the one that it hit,
        /// so it looks like it went from one to another"): seconds after a chain's first jump (depth 1) that its jump at
        /// <paramref name="depth"/> comes: each bounce waits for the arc before it to land on its creature
        /// (<paramref name="drawSeconds"/>, [Ricochet] JumpDrawSeconds) and pauses there <paramref name="delaySeconds"/>
        /// (<see cref="BounceDelay"/>) before it leaves, so (depth - 1) x (pause + draw). The server deals the bounce this long after
        /// the first jump's hit (the moment the first arc lands); a client starts drawing its arc this long after the first arc
        /// started, so it lands this long after the first arc landed. 0 for depth 1 and below; at most MaxBounces deep; a negative
        /// or broken draw time counts as none.</summary>
        internal static float BounceAfter(int depth, float delaySeconds, float drawSeconds) {
            if (depth <= 1) return 0f;
            int steps = (depth > MaxBounces ? MaxBounces : depth) - 1;
            float draw = float.IsNaN(drawSeconds) || float.IsInfinity(drawSeconds) || drawSeconds < 0f ? 0f : drawSeconds;
            return steps * (BounceDelay(delaySeconds) + draw);
        }
    }

    /// <summary>H5c, the server: the bounces waiting for their time (<see cref="JumpRule.BounceAfter"/>). Taken once due, in the
    /// order they were queued (a walk is breadth first, so a chain's bounces come in depth order); at most <see cref="Cap"/> wait.
    /// A broken due time is due at once, so nothing waits for ever.</summary>
    internal sealed class BounceQueue<T> {
        /// <summary>At most this many bounces wait (a full volley's walk queues at most 15 x 3 x MaxBounces; several releases fit).</summary>
        public const int Cap = 512;
        readonly List<T> items = new List<T>();
        readonly List<float> due = new List<float>();

        public int Count => items.Count;

        /// <summary>Queues <paramref name="item"/> to be taken at <paramref name="dueAt"/>; false when <see cref="Cap"/> wait already
        /// (the caller drops it).</summary>
        public bool Add(T item, float dueAt) {
            if (items.Count >= Cap) return false;
            items.Add(item); due.Add(dueAt);
            return true;
        }

        /// <summary>Moves every item due at <paramref name="now"/> (its time reached, or a broken time) into <paramref name="into"/>,
        /// in the order queued; the rest keep waiting. Returns how many were taken.</summary>
        public int TakeDue(float now, List<T> into) {
            int taken = 0, kept = 0;
            for (int i = 0; i < items.Count; i++) {
                float d = due[i];
                if (!(d > now) || float.IsInfinity(d)) { into.Add(items[i]); taken++; continue; }
                items[kept] = items[i]; due[kept] = d; kept++;
            }
            if (kept < items.Count) { items.RemoveRange(kept, items.Count - kept); due.RemoveRange(kept, due.Count - kept); }
            return taken;
        }

        public void Clear() { items.Clear(); due.Clear(); }
    }

    /// <summary>H5b: the creatures one jump chain has passed through, in order: the creature the arrow struck, then each creature a
    /// jump of it landed on. A jump never lands on a creature already in its chain (never back, never itself).</summary>
    internal sealed class JumpChain {
        readonly uint[] ids;
        public JumpChain(uint struck) { ids = new[] { struck }; }
        JumpChain(uint[] ids) { this.ids = ids; }

        public int Count => ids.Length;
        /// <summary>The jumps made so far (0: only the creature the arrow struck).</summary>
        public int Depth => ids.Length - 1;
        /// <summary>Where the chain is now: the creature its next jump leaves from.</summary>
        public uint Last => ids[ids.Length - 1];
        public uint this[int i] => ids[i];

        public bool Contains(uint id) {
            foreach (uint x in ids) if (x == id) return true;
            return false;
        }

        /// <summary>This chain with <paramref name="id"/> at its end (a new chain; this one is unchanged).</summary>
        public JumpChain Then(uint id) {
            var next = new uint[ids.Length + 1];
            Array.Copy(ids, next, ids.Length);
            next[ids.Length] = id;
            return new JumpChain(next);
        }
    }

    /// <summary>H5b: the numbers one jump walk uses: JumpMetres, JumpSpread, JumpShare, the side orb's share for this release and
    /// Bounces.</summary>
    internal readonly struct JumpValues {
        public readonly float Range, JumpShare, SideShare;
        public readonly int Spread, Bounces;
        public JumpValues(float range, int spread, float jumpShare, float sideShare, int bounces) {
            Range = range; Spread = spread; JumpShare = jumpShare; SideShare = sideShare; Bounces = bounces;
        }
    }

    /// <summary>H5b: what a jump walk needs from the machine that walks it. The server deals the hits (Ricochet), a client draws the
    /// arcs, the tests keep a list.</summary>
    internal interface IJumpWorld {
        /// <summary>The jump candidates round creature <paramref name="from"/> on this machine, into <paramref name="into"/> (cleared
        /// first), and where <paramref name="from"/> is. False: stop this chain here (the creature is gone; a client's arc cap).</summary>
        bool Candidates(uint from, List<JumpCandidate> into, out float x, out float y, out float z);

        /// <summary>No target within reach of <paramref name="from"/> for <paramref name="arrows"/> jumps at <paramref name="depth"/>.</summary>
        void Nowhere(uint from, int depth, int arrows);

        /// <summary>One jump from <paramref name="from"/> onto candidate <paramref name="to"/> (an index into the list the last
        /// Candidates call filled): <paramref name="multiplier"/> = the summed values of the <paramref name="arrows"/> arrows it
        /// carries x <see cref="JumpRule.ShareAt"/>. True when it landed (the server: the hit was dealt; a client: drawn).</summary>
        bool Land(uint from, int to, float multiplier, int depth, int arrows);
    }

    /// <summary>H5b: one release's jumps from one struck creature, walked to the end of every chain: the arrows that landed on the
    /// struck creature jump round its targets in turn (JumpRule.Targets, the ledger, JumpRule.Split), and each target a jump lands on
    /// passes that jump's arrows on from itself while <see cref="JumpRule.MayBounce"/> allows, never to a creature already in that
    /// chain, never from a player. Breadth first: every first jump before any second one. Every machine walks it alike from the same
    /// candidates; with Bounces 1 it is exactly the jump before H5 (one level: Targets, Take, Spread, Damage).</summary>
    internal static class JumpWalk {
        /// <summary>A guard: at most this many jumps one walk tries (a walk tries at most arrows x Bounces).</summary>
        public const int MaxHops = 256;

        readonly struct Step {
            public readonly JumpChain Chain;
            public readonly IList<float> Arrows;
            public Step(JumpChain chain, IList<float> arrows) { Chain = chain; Arrows = arrows; }
        }

        /// <summary>The jumps of <paramref name="arrows"/> (each landed arrow's own value: its share of the arrow's damage, or 1 for a
        /// copy of the real arrow's hit) that landed on <paramref name="struck"/>. Returns how many jumps landed.</summary>
        internal static int Run(IJumpWorld world, JumpLedger ledger, uint struck, IList<float> arrows, JumpValues v) {
            if (world == null || ledger == null || arrows == null || arrows.Count == 0) return 0;
            var queue = new List<Step> { new Step(new JumpChain(struck), arrows) };
            var candidates = new List<JumpCandidate>();
            int landed = 0, tried = 0;
            for (int q = 0; q < queue.Count; q++) {
                JumpChain chain = queue[q].Chain;
                IList<float> mine = queue[q].Arrows;
                uint from = chain.Last;
                int depth = chain.Depth + 1;
                if (!world.Candidates(from, candidates, out float x, out float y, out float z)) continue;
                List<int> to = JumpRule.Targets(from, x, y, z, candidates, v.Range, v.Spread, chain);
                if (to.Count == 0) { world.Nowhere(from, depth, mine.Count); continue; }
                int first = ledger.Take(from, mine.Count);
                List<float>[] split = JumpRule.Split(first, mine, to.Count);
                float share = JumpRule.ShareAt(depth, v.JumpShare, v.SideShare);
                for (int k = 0; k < to.Count; k++) {
                    float sum = 0f;
                    foreach (float a in split[k]) sum += a;
                    float multiplier = JumpRule.Damage(sum, share);
                    if (!(multiplier > 0f)) continue;
                    if (tried >= MaxHops) return landed;
                    tried++;
                    JumpCandidate c = candidates[to[k]];
                    if (!world.Land(from, to[k], multiplier, depth, split[k].Count)) continue;
                    landed++;
                    if (!c.Player && JumpRule.MayBounce(HitSource.Jump, depth, v.Bounces)) queue.Add(new Step(chain.Then(c.Id), split[k]));
                }
            }
            return landed;
        }
    }

    /// <summary>The jumps each struck creature of one release has passed on, so successive jumps go round its targets in turn.</summary>
    internal sealed class JumpLedger {
        /// <summary>A release strikes at most this many creatures that jump (the oldest is forgotten past it).</summary>
        public const int Cap = 64;
        readonly Dictionary<uint, int> made = new Dictionary<uint, int>();
        readonly List<uint> order = new List<uint>();

        public int Count => made.Count;

        /// <summary>How many jumps <paramref name="from"/> had passed on before these <paramref name="n"/>, which are counted now.</summary>
        public int Take(uint from, int n) {
            if (!made.TryGetValue(from, out int before)) {
                while (order.Count >= Cap) { made.Remove(order[0]); order.RemoveAt(0); }
                order.Add(from);
                before = 0;
            }
            made[from] = before + Math.Max(0, n);
            return before;
        }
    }

    // ---------------------------------------------------------------- batch 4: Shatter (spec 2026-11-16), a Charge side orb since batch 4b

    /// <summary>What dealt a hit, for the rule that Ricochet's jump and Shatter's splinters never feed each other. Jumps and
    /// splinters are dealt by the mod under Hits.Dealing, so neither reaches Hits.FlushSpectral or the real-arrow hook.</summary>
    internal enum HitSource { RealArrow, Spectral, Jump, Splinter }

    /// <summary>Whether a release can shatter (batch 4b: the Charge half only; the Multi half was removed).</summary>
    internal enum ShatterHalf { None, Charge, OffPath }

    /// <summary>Fix batch D (the user: "make autodock, shatter and ricoshy work even though its not your main path"; "Half
    /// strength"): the share of its damage a side orb gives a build. 0 when the build does not own it; 1 when the owner's main path
    /// is the orb's own line (Shatter: Charge, Ricochet: Multi); else [Complements] SideOrbOffPathShare (0 to 1), an owner with no
    /// main path included.</summary>
    internal static class SideOrbShare {
        public const float DefaultOffPath = 0.5f;
        public static float Of(bool owned, MainPath main, MainPath line, float offPath) {
            if (!owned) return 0f;
            if (main == line) return 1f;
            return float.IsNaN(offPath) || offPath < 0f ? 0f : offPath > 1f ? 1f : offPath;
        }
        public static float Ricochet(Build b, float offPath) => Of((b.Sides & SideOrbs.Ricochet) != 0, b.Main, MainPath.Multi, offPath);
        public static float Shatter(Build b, float offPath) => Of((b.Bridges & Bridges.Shatter) != 0, b.Main, MainPath.Charge, offPath);
    }

    /// <summary>Fix batch D: the try-out action that practises Ricochet or Shatter, for every main path (the orb's own action):
    /// on its own line as before (Ricochet: a full volley of fifteen ghosts; Shatter: a charged release); off it, a release of the
    /// main path's own shot (Quick: a quick shot or a line; Multi: a volley; Charge: a charged shot that is not a beam); with no
    /// main path, any release.</summary>
    internal static class SideOrbPractice {
        public static bool Done(Orb orb, MainPath main, ShotKind kind, UltimateUse ultimate, int ghosts, bool chargedReal) {
            if (orb != Orb.Ricochet && orb != Orb.Shatter) return false;
            switch (main) {
                case MainPath.None: return true;
                case MainPath.Quick: return kind == ShotKind.Snap;
                case MainPath.Multi: return orb == Orb.Ricochet ? ghosts >= VolleyMath.Total : ghosts > 0;
                case MainPath.Charge: return orb == Orb.Shatter ? chargedReal : kind == ShotKind.Charged && ultimate != UltimateUse.Beam;
                default: return false;
            }
        }
    }

    /// <summary>Shatter (batch 4b, the user: "a Charge side orb off Charge Shot 3 ... only works for a Charge main, like every side
    /// orb"; never counts toward a line). A Charge main's charged real arrow shatters on its first creature hit. One shatter per
    /// release. Fix batch D: any other owner (no main path included) gets the off-path half: the real arrow of any release on its
    /// first creature hit, at [Complements] SideOrbOffPathShare. Its wire bit stays where it was (Bridges.Shatter): a bit is only a bit.</summary>
    internal static class ShatterRule {
        internal const int MaxPellets = 64;

        /// <summary>Fix batch D: an owner whose main path is not Charge (none included) gets the off-path half.</summary>
        internal static ShatterHalf Half(Build b) =>
            (b.Bridges & Bridges.Shatter) == 0 ? ShatterHalf.None : b.Main == MainPath.Charge ? ShatterHalf.Charge : ShatterHalf.OffPath;

        /// <summary>Play-test 7: how many pellets the blast's look draws ([Shatter] Pellets), 0 when the build gets no half; never
        /// negative, at most <see cref="MaxPellets"/>. Looks only: the blast's damage is <see cref="ShatterBlast"/>.</summary>
        internal static int Pellets(Build b, int pellets) {
            if (Half(b) == ShatterHalf.None) return 0;
            return pellets < 0 ? 0 : pellets > MaxPellets ? MaxPellets : pellets;
        }

        /// <summary>The Charge half: the real arrow of a charged release (never a Holy Arrow beam, never a plain arrow). Off the
        /// main path (fix batch D: the owner has no charged shot): the real arrow of any release, never a beam.</summary>
        internal static bool RealArrowShatters(ShatterHalf half, bool charged, bool beam) =>
            !beam && (half == ShatterHalf.Charge ? charged : half == ShatterHalf.OffPath);

        /// <summary>What a shattering hit may land on: a creature; while the orb is held for try-out also anything else that is
        /// not a player (a networked thing in the try-out area; the shrine has none a charged arrow can damage, so practice is the
        /// charged release, Volley.Launch). Never a player.</summary>
        internal static bool Struck(bool creature, bool player, bool tryingOut) => !player && (creature || tryingOut);

        /// <summary>Only the real arrow's hit shatters: ghosts and pellets never (batch 4b), a splinter never shatters again, a
        /// Ricochet jump never shatters. Checked where a blast starts (Shatter.ServerShatter).</summary>
        internal static bool MayShatter(HitSource source) => source == HitSource.RealArrow;

        /// <summary>Fix batch D: off the main path the blast skips creatures the same release already damaged (the line it pierced,
        /// its needles' or heavy arrows' targets); on the Charge main nothing changes.</summary>
        internal static bool SkipsDamaged(ShatterHalf half, bool damaged) => half == ShatterHalf.OffPath && damaged;

        /// <summary>The multi-arrow orbs that raise the blast's share: a Charge main's (its complement); none off the main path.</summary>
        internal static int MultiOrbs(Build b) => b.Main == MainPath.Charge ? b.Volley : 0;
    }

    /// <summary>The splinters' directions: a sunflower (Vogel) spiral filling a cone round the hit's direction. Splinter i of n
    /// leaves the axis by half the cone x sqrt((i + 0.5) / n), turned i x 137.5 degrees round it. The same on every machine.</summary>
    internal static class SplinterPattern {
        const double Golden = 137.50776405003785;

        internal static void Angles(int index, int count, float coneDegrees, out double offDegrees, out double azimuthDegrees) {
            double half = Math.Max(0.0, Math.Min(170.0, coneDegrees)) * 0.5;
            offDegrees = count <= 0 ? 0 : half * Math.Sqrt((index + 0.5) / count);
            azimuthDegrees = (index * Golden) % 360.0;
        }

        /// <summary>Splinter <paramref name="index"/>'s unit direction round the axis (ax, ay, az), whose length does not matter.
        /// The cross-axis frame: u = axis x (world up, or world x when the axis is near vertical), w = axis x u.</summary>
        internal static void Direction(double ax, double ay, double az, int index, int count, float coneDegrees, out double dx, out double dy, out double dz) {
            Angles(index, count, coneDegrees, out double off, out double az2);
            Around(ax, ay, az, off, az2, out dx, out dy, out dz);
        }

        // ---- looks only (fix batch C, play-test 7: the blast's fan of pellets)

        /// <summary>The shortest a fan pellet flies, as a share of the blast's reach (the [Shatter] PelletMinReach default).</summary>
        internal const float MinReach = 0.75f;
        const double FanAzimuthJitter = 20.0;

        /// <summary>Pellet <paramref name="index"/> of the blast's fan for the release <paramref name="seed"/> (its arrow id): the
        /// spiral of <see cref="Angles"/> turned as a whole by an angle of that release's own, each pellet moved inside its own
        /// ring of the cone (never past the cone's edge) and a little round it, and reaching between <see cref="MinReach"/> and
        /// all of the blast's reach (<paramref name="minReach"/>, 0 to 1, in its place when given). The same on every machine for
        /// the same arrow; another arrow draws another fan.</summary>
        internal static void Fan(uint seed, int index, int count, float coneDegrees, out double offDegrees, out double azimuthDegrees, out float reachShare, float minReach = MinReach) {
            if (count <= 0) { offDegrees = 0; azimuthDegrees = 0; reachShare = 1f; return; }
            double half = Math.Max(0.0, Math.Min(170.0, coneDegrees)) * 0.5;
            int i = index < 0 ? 0 : index >= count ? count - 1 : index;
            offDegrees = half * Math.Sqrt((i + LookSeed.Unit(seed, i, 1)) / count);
            double turned = i * Golden + 360.0 * LookSeed.Unit(seed, 0, 2) + FanAzimuthJitter * (LookSeed.Unit(seed, i, 3) - 0.5);
            azimuthDegrees = ((turned % 360.0) + 360.0) % 360.0;
            float least = float.IsNaN(minReach) ? MinReach : minReach < 0f ? 0f : minReach > 1f ? 1f : minReach;
            reachShare = least + (1f - least) * LookSeed.Unit(seed, i, 4);
        }

        /// <summary>That pellet's unit direction round the axis (ax, ay, az), in the frame <see cref="Direction"/> uses.</summary>
        internal static void FanDirection(double ax, double ay, double az, uint seed, int index, int count, float coneDegrees,
                                          out double dx, out double dy, out double dz, out float reachShare, float minReach = MinReach) {
            Fan(seed, index, count, coneDegrees, out double off, out double az2, out reachShare, minReach);
            Around(ax, ay, az, off, az2, out dx, out dy, out dz);
        }

        /// <summary>The unit direction <paramref name="off"/> degrees from the axis, turned <paramref name="az2"/> degrees round it.</summary>
        static void Around(double ax, double ay, double az, double off, double az2, out double dx, out double dy, out double dz) {
            double l = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (l < 1e-12 || double.IsNaN(l)) { dx = dy = dz = 0; return; }
            ax /= l; ay /= l; az /= l;
            double rx, ry, rz;
            if (Math.Abs(ay) < 0.9) { rx = 0; ry = 1; rz = 0; } else { rx = 1; ry = 0; rz = 0; }
            double ux = ay * rz - az * ry, uy = az * rx - ax * rz, uz = ax * ry - ay * rx;
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            ux /= ul; uy /= ul; uz /= ul;
            double wx = ay * uz - az * uy, wy = az * ux - ax * uz, wz = ax * uy - ay * ux;
            double a = off * Math.PI / 180.0, p = az2 * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a), cp = Math.Cos(p), sp = Math.Sin(p);
            dx = ca * ax + sa * (cp * ux + sp * wx);
            dy = ca * ay + sa * (cp * uy + sp * wy);
            dz = ca * az + sa * (cp * uz + sp * wz);
            double dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= dl; dy /= dl; dz /= dl;
        }
    }

    /// <summary>A creature (or, on the server with PvP on, a player) the blast may reach: its id, the centre of its body as this
    /// machine sees it, a radius for that body, and whether it is a player.</summary>
    internal readonly struct SplinterCandidate {
        public readonly uint Id;
        public readonly float X, Y, Z, Radius;
        public readonly bool Player;
        public SplinterCandidate(uint id, float x, float y, float z, float radius, bool player) { Id = id; X = x; Y = y; Z = z; Radius = radius; Player = player; }
    }

    /// <summary>Shatter's blast numbers ([Shatter], shared): reach, the cone's full width, the share of the charged arrow's hit on the
    /// axis close behind, where the distance falloff starts, the share left at the reach and at the cone's edge, and the extra share per
    /// multi-arrow orb owned.</summary>
    internal readonly struct BlastValues {
        public readonly float Metres, ConeDegrees, Share, FullMetres, EdgeShare, PerMultiOrb;
        public BlastValues(float metres, float coneDegrees, float share, float fullMetres, float edgeShare, float perMultiOrb) {
            Metres = metres; ConeDegrees = coneDegrees; Share = share; FullMetres = fullMetres; EdgeShare = edgeShare; PerMultiOrb = perMultiOrb;
        }
        // G3 (play-test 8, the user: "10 m"): reach 6 -> 10 m, full within 2 -> 3 m; cone, share, edge share and per-orb bonus unchanged. 0.19.17 balance: share x2 -> x1, full within 3 -> 2 m, per-orb 0.15 -> 0.05
        public static BlastValues Default => new BlastValues(10f, 60f, 1f, 2f, 0.25f, 0.05f);
    }

    /// <summary>Shatter becomes a blast (play-test 7, the user: "a giant shotgun is coming out behind ... a good chunk of pellets and
    /// ... a really good chunk of damage"; chosen: about double the arrow's hit on a creature right behind, 6 m, a wide cone). Every
    /// creature in the cone behind the struck one, along the arrow's direction, takes a share of the charged arrow's hit.</summary>
    internal static class ShatterBlast {
        static bool Ok(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Fix batch D (fixA-review I-4, the controller's ruling): [Shatter] AssumedEffectiveness, the share of the arrow's
        /// power x stage a hit is taken to deal when the real hit's damage is not known; [Shatter] MaxBodyMetres, the widest a body
        /// counts (radius) for the blast's reach and cone.</summary>
        public const float DefaultAssumedEffectiveness = 0.65f, DefaultMaxBody = 0.75f;

        /// <summary>The damage the shattering hit dealt: <paramref name="dealt"/> when it is known (finite and above 0), else the
        /// arrow's <paramref name="power"/> x the release's <paramref name="stage"/> multiplier x <paramref name="assumed"/>.</summary>
        internal static float HitDamage(float dealt, float power, float stage, float assumed) {
            if (Ok(dealt) && dealt > 0f) return dealt;
            float d = power * stage * assumed;
            return Ok(d) && d > 0f ? d : 0f;
        }

        /// <summary>What a creature in the blast takes, in health: its <paramref name="share"/> (Share: BlastShare with the falloffs)
        /// x the shattering hit's damage x the owner's <paramref name="pathShare"/> (1 on the Charge main). Never negative.</summary>
        internal static float Damage(float share, float hitDamage, float pathShare) {
            float d = share * hitDamage * pathShare;
            return Ok(d) && d > 0f ? d : 0f;
        }

        /// <summary>A body's radius for the blast: its largest half-extent, at most <paramref name="cap"/>.</summary>
        internal static float Radius(float ex, float ey, float ez, float cap) {
            float r = Math.Max(ex, Math.Max(ey, ez));
            if (!Ok(r) || r < 0f) r = 0f;
            return Ok(cap) && cap >= 0f && r > cap ? cap : r;
        }

        /// <summary>The share on the axis close behind: Share plus PerMultiOrb per multi-arrow orb (at most three); 0 when broken.</summary>
        internal static float Peak(BlastValues v, int multiOrbs) {
            int n = Math.Max(0, Math.Min(Complements.MaxOrbs, multiOrbs));
            float p = v.Share + (Ok(v.PerMultiOrb) ? v.PerMultiOrb : 0f) * n;
            return Ok(p) && p > 0f ? p : 0f;
        }

        /// <summary>The share of the charged arrow's hit <paramref name="c"/> takes from a blast at (px, py, pz) along (ax, ay, az):
        /// 0 for the struck creature, for a centre behind the hit (the shooter's side), beyond Metres or outside the cone (both
        /// measured to the nearest part of its body); else Peak x a distance falloff (1 within FullMetres, EdgeShare at Metres) x an
        /// angle falloff (1 on the axis, EdgeShare at the cone's edge). Players are not told apart here (PvpRule decides).</summary>
        internal static float Share(uint struck, double px, double py, double pz, double ax, double ay, double az, SplinterCandidate c, BlastValues v, int multiOrbs) {
            if (c.Id == struck || !Ok(v.Metres) || !Ok(v.ConeDegrees) || !Ok(v.FullMetres) || !Ok(v.EdgeShare) || v.Metres <= 0f) return 0f;
            double al = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (al < 1e-12 || double.IsNaN(al)) return 0f;
            double vx = c.X - px, vy = c.Y - py, vz = c.Z - pz, d = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (double.IsNaN(d)) return 0f;
            double along = (vx * ax + vy * ay + vz * az) / al;
            if (along <= 0) return 0f;                                       // behind the hit: the shooter's side
            double r = c.Radius > 0f && Ok(c.Radius) ? c.Radius : 0.0;
            double reach = Math.Max(0, d - r);
            double angle = d > 1e-9 ? Math.Acos(Math.Max(-1, Math.Min(1, along / d))) * 180.0 / Math.PI : 0;
            if (d <= r) angle = 0;
            else if (r > 0) angle = Math.Max(0, angle - Math.Asin(Math.Min(1, r / d)) * 180.0 / Math.PI);
            float half = Math.Max(0f, Math.Min(85f, v.ConeDegrees * 0.5f));
            if (reach > v.Metres + 1e-4 || angle > half + 1e-3) return 0f;
            reach = Math.Min(reach, v.Metres); angle = Math.Min(angle, half);
            float byDistance = BlastMath.Fraction((float)reach, v.FullMetres, v.Metres, v.EdgeShare);
            float byAngle = half > 0f ? BlastMath.Fraction((float)angle, 0f, half, v.EdgeShare) : 1f;
            float share = Peak(v, multiOrbs) * byDistance * byAngle;
            return share > 0f && Ok(share) ? share : 0f;
        }

        /// <summary>Looks only (fix batch C): where the blast comes out of the struck creature's back. Metres from the hit (px, py,
        /// pz) along the arrow's direction (ax, ay, az) to the far side of its body, a ball of <paramref name="radius"/> round
        /// (cx, cy, cz); level with the body's centre when the line misses the ball. Never behind the hit, never farther than the
        /// body is wide; 0 for no body, no direction or broken numbers.</summary>
        internal static float ExitDistance(double px, double py, double pz, double ax, double ay, double az, double cx, double cy, double cz, float radius) {
            if (!(radius > 0f) || !Ok(radius)) return 0f;
            double al = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (!(al > 1e-12)) return 0f;
            double vx = cx - px, vy = cy - py, vz = cz - pz;
            double along = (vx * ax + vy * ay + vz * az) / al;
            double off2 = vx * vx + vy * vy + vz * vz - along * along, r2 = (double)radius * radius;
            if (double.IsNaN(along) || double.IsNaN(off2)) return 0f;
            double t = off2 <= r2 ? along + Math.Sqrt(Math.Max(0.0, r2 - off2)) : along;
            return (float)Math.Max(0.0, Math.Min(2.0 * radius, t));
        }
    }

    /// <summary>Looks only (fix batch C, play-test 7: one arc per landed arrow): the shape of a Ricochet jump's arc. Each arc is the
    /// straight line from where its arrow hit to the creature it jumps to, lifted and bowed sideways by a hump that is highest
    /// half-way; how high and which way is the arc's own (<see cref="Shape"/>), so thirty arcs fan out instead of drawing one
    /// line. It is drawn from the hit to the target over a draw time, then fades.</summary>
    internal static class JumpArc {
        /// <summary>Points along one arc.</summary>
        internal const int Points = 14;
        const float ReferenceMetres = 4f, MinScale = 0.5f, MaxScale = 2f;

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>Arc number <paramref name="arc"/> of the release <paramref name="seed"/>: its lift as a share of the usual
        /// height (1 give or take <paramref name="variety"/>, 0 to 1) and how far it bows sideways (-1 to 1: left or right of the
        /// jump, as a share of the sideways setting).</summary>
        internal static void Shape(uint seed, int arc, float variety, out float lift, out float side) {
            float v = float.IsNaN(variety) ? 0f : Clamp01(variety);
            lift = v > 0f ? 1f + v * (2f * LookSeed.Unit(seed, arc, 11) - 1f) : 1f;
            side = 2f * LookSeed.Unit(seed, arc, 12) - 1f;
        }

        /// <summary>The hump of a jump over <paramref name="distance"/> metres: <paramref name="height"/> at 4 m, growing with the
        /// square root of the distance, never under half of it or over twice it. 0 for no height or broken numbers.</summary>
        internal static float Height(float distance, float height) {
            if (!(height > 0f) || float.IsInfinity(height) || float.IsNaN(distance)) return 0f;
            float k = distance > 0f ? (float)Math.Sqrt(distance / ReferenceMetres) : MinScale;
            return height * (k < MinScale ? MinScale : k > MaxScale ? MaxScale : k);
        }

        /// <summary>The arc's point at <paramref name="t"/> (0 = the hit, 1 = the target; clamped): the straight line plus
        /// 4 t (1 - t) x (<paramref name="lift"/> metres up + <paramref name="sideways"/> metres across the jump). "Across" is
        /// level and square to the jump (world x for a jump straight up or down).</summary>
        internal static void Point(double ax, double ay, double az, double bx, double by, double bz, float lift, float sideways, float t,
                                   out float x, out float y, out float z) {
            double u = float.IsNaN(t) ? 1.0 : t < 0f ? 0.0 : t > 1f ? 1.0 : t;
            double hump = 4.0 * u * (1.0 - u);
            double dx = bx - ax, dz = bz - az, flat = Math.Sqrt(dx * dx + dz * dz);
            double sx = 1.0, sz = 0.0;
            if (flat > 1e-6) { sx = dz / flat; sz = -dx / flat; }
            x = (float)(ax + (bx - ax) * u + sx * sideways * hump);
            y = (float)(ay + (by - ay) * u + lift * hump);
            z = (float)(az + (bz - az) * u + sz * sideways * hump);
        }

        /// <summary>How much of the arc is drawn <paramref name="age"/> seconds after the hit (0 to 1; at once with no draw time).</summary>
        internal static float Drawn(float age, float drawSeconds) =>
            !(drawSeconds > 0f) ? 1f : float.IsNaN(age) ? 1f : Clamp01(age / drawSeconds);

        /// <summary>The arc's brightness: full while it is drawn, then falling to nothing over <paramref name="fadeSeconds"/>.</summary>
        internal static float Alpha(float age, float drawSeconds, float fadeSeconds) {
            float draw = drawSeconds > 0f ? drawSeconds : 0f;
            if (float.IsNaN(age)) return 0f;
            if (age <= draw) return 1f;
            if (!(fadeSeconds > 0f)) return 0f;
            return Clamp01(1f - (age - draw) / fadeSeconds);
        }

        /// <summary>The arc is done (drawn and faded; a broken age is done too).</summary>
        internal static bool Over(float age, float drawSeconds, float fadeSeconds) =>
            !(age < (drawSeconds > 0f ? drawSeconds : 0f) + (fadeSeconds > 0f ? fadeSeconds : 0f));
    }
    // ---------------------------------------------------------------- H2: the homing volley's looks (shape maths only)

    /// <summary>What the lock's cage on a creature is doing: closing as the dwell fills, locked, sprung open (lost), or thrown
    /// outward as its arrows land.</summary>
    internal enum CageState { Forming, Locked, Lost, Burst }

    /// <summary>The cage's numbers ([SeekLook], looks only): how wide and how open it starts, how near it is when the dwell is full,
    /// the snap shut, the flash, the spring open.</summary>
    internal struct CageLook {
        /// <summary>Its size (times the locked size) and the degrees of each arc drawn when the dwell starts, and when it is full.</summary>
        public float OpenScale, NearScale, OpenDegrees, NearDegrees;
        /// <summary>How bright it is when the dwell starts (1 when it is full).</summary>
        public float FormAlpha;
        public float SnapSeconds, FlashSeconds, LostSeconds, LostSpring, BurstSeconds;

        public static CageLook Default => new CageLook {
            OpenScale = 2.2f, NearScale = 1.25f, OpenDegrees = 70f, NearDegrees = 300f, FormAlpha = 0.45f,
            SnapSeconds = 0.12f, FlashSeconds = 0.3f, LostSeconds = 0.25f, LostSpring = 1.6f, BurstSeconds = 0.3f,
        };
    }

    /// <summary>The cage this frame: its size (times the locked size), the degrees of each arc drawn (360 = a closed ring), its
    /// brightness and its flash (0 to 1).</summary>
    internal struct CagePose { public float Scale, Sweep, Alpha, Flash; }

    /// <summary>Looks only (H2; the owner after playing 0.17.0 chose "Rings around the body" over the flat ring that always faced
    /// the camera): the lock's cage. Three arcs on three planes that are square to one another and each lean the same 54.7 degrees
    /// from level (a gyroscope), so from any side at least one is seen well open and none ever lies flat or stands as a bare line.
    /// While the aim rests on a creature the arcs are wide and mostly open; as the dwell fills they tighten and close, in step with
    /// it (half-way is half-way); at the lock they snap shut a little past the body and settle; lost, they spring open and fade.
    /// Sized from the creature's own bounds between a floor and a ceiling, and never thinner on screen than a set angle.</summary>
    internal static class CageShape {
        internal const int Rings = 3;
        const double Deg = Math.PI / 180.0;
        static readonly float Up = (float)(1.0 / Math.Sqrt(3.0)), Flat = (float)Math.Sqrt(2.0 / 3.0);

        static float Clamp01(float v) => float.IsNaN(v) || v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>Ring <paramref name="ring"/>'s plane: two unit directions in it (u level, v up the slope) and its normal n = u x v.
        /// The three normals are square to one another, 120 degrees apart round straight up, each 54.7 degrees from it.</summary>
        internal static void Plane(int ring, out float ux, out float uy, out float uz, out float vx, out float vy, out float vz, out float nx, out float ny, out float nz) {
            int k = ((ring % Rings) + Rings) % Rings;
            double phi = (90.0 + 120.0 * k) * Deg;
            float c = (float)Math.Cos(phi), s = (float)Math.Sin(phi);
            nx = Flat * c; ny = Up; nz = Flat * s;
            ux = -s; uy = 0f; uz = c;
            // v = n x u, so that u x v = n
            vx = ny * uz - nz * uy; vy = nz * ux - nx * uz; vz = nx * uy - ny * ux;
        }

        /// <summary>The point of ring <paramref name="ring"/>'s unit circle at <paramref name="angleDegrees"/> round it, with the whole
        /// cage turned <paramref name="turnDegrees"/> round straight up.</summary>
        internal static void Point(int ring, float angleDegrees, float turnDegrees, out float x, out float y, out float z) {
            Plane(ring, out float ux, out float uy, out float uz, out float vx, out float vy, out float vz, out _, out _, out _);
            double a = (float.IsNaN(angleDegrees) || float.IsInfinity(angleDegrees) ? 0.0 : angleDegrees) * Deg;
            double t = (float.IsNaN(turnDegrees) || float.IsInfinity(turnDegrees) ? 0.0 : turnDegrees) * Deg;
            double ca = Math.Cos(a), sa = Math.Sin(a), ct = Math.Cos(t), st = Math.Sin(t);
            double px = ca * ux + sa * vx, py = ca * uy + sa * vy, pz = ca * uz + sa * vz;
            x = (float)(px * ct + pz * st);
            y = (float)py;
            z = (float)(-px * st + pz * ct);
        }

        /// <summary>The locked cage's radius for a creature whose bounds reach <paramref name="ex"/>, <paramref name="ey"/>,
        /// <paramref name="ez"/> from their middle: the largest of them times <paramref name="scale"/>, never under
        /// <paramref name="floor"/> or over <paramref name="ceiling"/> (a small creature and a huge one both read).</summary>
        internal static float Radius(float ex, float ey, float ez, float scale, float floor, float ceiling) {
            float lo = float.IsNaN(floor) || floor < 0f ? 0f : floor;
            float hi = float.IsNaN(ceiling) || ceiling < lo ? lo : ceiling;
            float m = 0f;
            if (!float.IsNaN(ex)) m = Math.Max(m, Math.Abs(ex));
            if (!float.IsNaN(ey)) m = Math.Max(m, Math.Abs(ey));
            if (!float.IsNaN(ez)) m = Math.Max(m, Math.Abs(ez));
            float r = m * (float.IsNaN(scale) || scale <= 0f ? 1f : scale);
            return float.IsNaN(r) || r < lo ? lo : r > hi ? hi : r;
        }

        /// <summary>How wide (metres) an arc is drawn <paramref name="distance"/> metres from the eye: its own width, but never
        /// thinner on screen than <paramref name="minRadians"/> (so the cage reads at 60 m).</summary>
        internal static float Width(float metres, float distance, float minRadians) {
            float w = float.IsNaN(metres) || metres < 0f ? 0f : metres;
            if (!(distance > 0f) || float.IsInfinity(distance) || !(minRadians > 0f) || float.IsInfinity(minRadians)) return w;
            float far = distance * minRadians;
            return far > w ? far : w;
        }

        /// <summary>While the dwell fills (<paramref name="progress"/> 0 to 1, LockSet.Fill): from wide and mostly open to near and
        /// nearly shut, in a straight line, so how far the lock has come is read from how closed the cage is.</summary>
        internal static CagePose Forming(float progress, in CageLook k) {
            float p = Clamp01(progress);
            return new CagePose {
                Scale = k.OpenScale + (k.NearScale - k.OpenScale) * p,
                Sweep = k.OpenDegrees + (k.NearDegrees - k.OpenDegrees) * p,
                Alpha = k.FormAlpha + (1f - k.FormAlpha) * p,
            };
        }

        /// <summary>From the lock on (<paramref name="age"/> seconds): over SnapSeconds the arcs close to full rings and the cage
        /// snaps from where the forming one was to a little past the body, then settles on it; a flash that fades over FlashSeconds.</summary>
        internal static CagePose Locked(float age, in CageLook k) {
            if (float.IsNaN(age)) return new CagePose { Scale = 1f, Sweep = 360f, Alpha = 1f };
            float t = k.SnapSeconds > 0f ? Clamp01(age / k.SnapSeconds) : 1f;
            // ease out with a small overshoot (to about a tenth past the end), so the cage is seen to snap
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f, back = 1f + c3 * u * u * u + c1 * u * u, ease = 1f - u * u;
            float sweep = k.NearDegrees + (360f - k.NearDegrees) * ease;
            float flash = k.FlashSeconds > 0f && age < k.FlashSeconds ? 1f - Clamp01(age / k.FlashSeconds) : 0f;
            return new CagePose { Scale = k.NearScale + (1f - k.NearScale) * back, Sweep = sweep > 360f ? 360f : sweep, Alpha = 1f, Flash = flash * flash };
        }

        /// <summary>Lost (<paramref name="age"/> seconds ago), from the size and opening it had then: it springs open to LostSpring
        /// times that size, its arcs opening, and fades to nothing over LostSeconds.</summary>
        internal static CagePose Lost(float age, float fromScale, float fromSweep, in CageLook k) {
            float t = float.IsNaN(age) || !(k.LostSeconds > 0f) ? 1f : Clamp01(age / k.LostSeconds);
            float u = 1f - t, ease = 1f - u * u;
            float spring = float.IsNaN(k.LostSpring) || k.LostSpring <= 0f ? 1f : k.LostSpring;
            return new CagePose { Scale = fromScale * (1f + (spring - 1f) * ease), Sweep = fromSweep * (1f - 0.45f * ease), Alpha = u };
        }

        /// <summary>Its arrows landed (<paramref name="age"/> seconds ago): a shut cage that flashes, is thrown outward to
        /// <paramref name="toScale"/> times its size and fades over <paramref name="seconds"/>.</summary>
        internal static CagePose Burst(float age, float toScale, float seconds) {
            float t = float.IsNaN(age) || !(seconds > 0f) ? 1f : Clamp01(age / seconds);
            float u = 1f - t;
            return new CagePose { Scale = 1f + (toScale - 1f) * (1f - u * u), Sweep = 360f, Alpha = u, Flash = u * u };
        }

        /// <summary>A lost or burst cage has faded out (a broken age is over); a forming or locked one never is.</summary>
        internal static bool Over(CageState state, float age, in CageLook k) =>
            state == CageState.Lost ? !(age < k.LostSeconds) : state == CageState.Burst && !(age < k.BurstSeconds);

        /// <summary>Degrees a second the arcs turn while the dwell fills: <paramref name="slow"/> when it starts, quickening to
        /// <paramref name="fast"/> when it is full.</summary>
        internal static float Spin(float progress, float slow, float fast) {
            float p = Clamp01(progress);
            return slow + (fast - slow) * p * p;
        }
    }

    /// <summary>Looks only (H2): a ghost on the bow leans toward the creature it will fly to (HomingAssign gives which), so the
    /// volley is seen to point at its locks.</summary>
    internal static class HomingLean {
        /// <summary>The unit direction (dx, dy, dz) turned toward (tx, ty, tz) by at most <paramref name="maxDegrees"/> times
        /// <paramref name="share"/> (0 to 1, the ease-in), never past it. Unchanged (as a unit direction) with no target, no angle,
        /// broken numbers, or a target more than a quarter turn away; a direction of no length stays as it is.</summary>
        internal static void Toward(float dx, float dy, float dz, float tx, float ty, float tz, float maxDegrees, float share, out float x, out float y, out float z) {
            double dl = Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz);
            if (!(dl > 1e-9) || double.IsInfinity(dl)) { x = dx; y = dy; z = dz; return; }
            double ux = dx / dl, uy = dy / dl, uz = dz / dl;
            x = (float)ux; y = (float)uy; z = (float)uz;
            double tl = Math.Sqrt((double)tx * tx + (double)ty * ty + (double)tz * tz);
            if (!(tl > 1e-9) || double.IsInfinity(tl) || !(maxDegrees > 0f) || float.IsInfinity(maxDegrees) || !(share > 0f)) return;
            double wx = tx / tl, wy = ty / tl, wz = tz / tl;
            double cos = ux * wx + uy * wy + uz * wz;
            if (double.IsNaN(cos) || cos <= 0.0) return;
            double angle = Math.Acos(cos > 1.0 ? 1.0 : cos);
            double lean = Math.Min(angle, maxDegrees * Math.PI / 180.0 * (share > 1f ? 1f : share));
            double px = wx - ux * cos, py = wy - uy * cos, pz = wz - uz * cos;
            double pl = Math.Sqrt(px * px + py * py + pz * pz);
            if (!(pl > 1e-9) || !(lean > 0.0)) return;
            double c = Math.Cos(lean), s = Math.Sin(lean) / pl;
            x = (float)(ux * c + px * s); y = (float)(uy * c + py * s); z = (float)(uz * c + pz * s);
        }
    }

    /// <summary>Looks only (H2; the owner: "have a trail be a little bit longer"; the curve must not show corners): the smooth curve
    /// a homing arrow and its trail are drawn on. The homing flight is stepped once per physics tick and may turn 21.6 degrees in
    /// one (HomingFlight), so drawn from step to step it has corners, and drawn ahead of its last step it jumps sideways at every
    /// turn. Instead the arrow is drawn on the curve through its last steps (a Catmull-Rom curve: through every step, its heading
    /// continuous), one step behind the flight, and its trail is that same curve behind it. Steps are counted back from the
    /// newest: 0 = the last step, 1 = the one before.</summary>
    internal static class TrailCurve {
        /// <summary>How many of its last steps a homing arrow keeps.</summary>
        internal const int History = 8;

        static HVec Spline(HVec p0, HVec p1, HVec p2, HVec p3, double f) =>
            (p1 * 2.0 + (p2 - p0) * f + (p0 * 2.0 - p1 * 5.0 + p2 * 4.0 - p3) * (f * f) + (p1 * 3.0 - p0 - p2 * 3.0 + p3) * (f * f * f)) * 0.5;

        static HVec Slope(HVec p0, HVec p1, HVec p2, HVec p3, double f) =>
            ((p2 - p0) + (p0 * 2.0 - p1 * 5.0 + p2 * 4.0 - p3) * (2.0 * f) + (p1 * 3.0 - p0 - p2 * 3.0 + p3) * (3.0 * f * f)) * 0.5;

        // the four points round the piece that holds `back`: from hist[j] (f = 0) to hist[j + 1] (f = 1)
        static bool Piece(HVec[] hist, int n, HVec next, double back, out HVec p0, out HVec p1, out HVec p2, out HVec p3, out double f) {
            int j = (int)Math.Floor(back);
            f = back - j;
            p1 = hist[j]; p2 = hist[j + 1];
            p0 = j > 0 ? hist[j - 1] : next.Finite ? next : p1 + (p1 - p2);
            p3 = j + 2 < n ? hist[j + 2] : p2 + (p2 - p1);
            return (p2 - p1).Length > 1e-9;
        }

        /// <summary>The point <paramref name="back"/> steps behind the newest of the <paramref name="count"/> steps in
        /// <paramref name="hist"/> (newest first); <paramref name="next"/> is where the flight goes next (it shapes the newest
        /// piece). A piece of no length (the arrow has not left the bow yet) is its point; past the oldest step the curve runs
        /// straight on; with one step it runs straight back from where it is going. Broken input: where the arrow is.</summary>
        internal static HVec At(HVec[] hist, int count, HVec next, double back) {
            int n = hist == null ? 0 : Math.Min(count, hist.Length);
            if (n <= 0) return next.Finite ? next : default;
            if (double.IsNaN(back) || back <= 0.0) return hist![0];
            if (n == 1) return next.Finite ? hist![0] - (next - hist[0]) * back : hist![0];
            if (back >= n - 1) { HVec a = hist![n - 1]; return a + (a - hist[n - 2]) * (back - (n - 1)); }
            return Piece(hist!, n, next, back, out HVec p0, out HVec p1, out HVec p2, out HVec p3, out double f) ? Spline(p0, p1, p2, p3, f) : p1;
        }

        /// <summary>The way the arrow flies at that point of the curve (a unit direction; none when it cannot be told). Standing
        /// still, it is the way the arrow goes next.</summary>
        internal static HVec Heading(HVec[] hist, int count, HVec next, double back) {
            int n = hist == null ? 0 : Math.Min(count, hist.Length);
            if (n <= 0) return default;
            HVec ahead = next.Finite ? (next - hist![0]).Normalized : default;
            if (n == 1) return ahead;
            if (double.IsNaN(back) || back < 0.0) back = 0.0;
            if (back >= n - 1) { HVec d = (hist![n - 2] - hist[n - 1]).Normalized; return d.Length > 0.5 ? d : ahead; }
            if (!Piece(hist!, n, next, back, out HVec p0, out HVec p1, out HVec p2, out HVec p3, out double f)) return ahead;
            HVec h = (Slope(p0, p1, p2, p3, f) * -1.0).Normalized;
            return h.Length > 0.5 ? h : ahead;
        }

        /// <summary>A new step <paramref name="p"/> joins the <paramref name="count"/> steps kept in <paramref name="hist"/>, newest
        /// first; the oldest falls out when it is full. Returns how many are kept now.</summary>
        internal static int Push(HVec[] hist, int count, HVec p) {
            if (hist == null || hist.Length == 0) return 0;
            int kept = Math.Min(Math.Max(count, 0), hist.Length - 1);
            for (int k = kept; k > 0; k--) hist[k] = hist[k - 1];
            hist[0] = p;
            return kept + 1;
        }

        /// <summary>How many steps behind its newest step the arrow is drawn, <paramref name="sinceStep"/> seconds after that step
        /// with a step every <paramref name="dt"/> seconds: one step behind at the step itself, on it when the next one is due
        /// (so it moves on at the flight's own speed and never waits or jumps).</summary>
        internal static double Back(double sinceStep, double dt) {
            if (!(dt > 0.0) || double.IsNaN(sinceStep)) return 0.0;
            double u = sinceStep / dt;
            return 1.0 - (u < 0.0 ? 0.0 : u > 1.0 ? 1.0 : u);
        }

        /// <summary>How many steps back a trail <paramref name="metres"/> long reaches for an arrow at <paramref name="speed"/> m/s
        /// stepped every <paramref name="dt"/> seconds; never more than the steps kept.</summary>
        internal static double Steps(double metres, double speed, double dt) {
            double step = speed * dt;
            if (!(metres > 0.0) || !(step > 1e-6) || double.IsInfinity(step)) return 0.0;
            return Math.Min(metres / step, History - 2);
        }
    }
}
