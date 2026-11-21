using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Skyfall (the admin sky-rain skill; design docs/superpowers/specs/2026-11-19-skyfall-admin-skill-design.md): every rule that can be
// pure. Compiled by BluesArcheryOverhaul.Tests (the fourteenth file), so only System.* in here: no UnityEngine, MelonLoader or game types,
// no System.Linq. The pattern decides damage spots, so it uses Skyfall's own hash (SkyfallRules.Unit), never LookSeed (looks only).
namespace BluesArcheryOverhaul {
    /// <summary>Every [Skyfall] number; the defaults are the design's. Read through <see cref="Clamped"/> before use.</summary>
    internal sealed class SkyfallValues {
        // the trigger and the aim
        public float MinStrength = 0.9f, MinElevationDegrees = 25f, ArmSeconds = 1.0f;
        public float SigilHeight = 30f, MinDistance = 15f, MaxDistance = 60f, SigilRadius = 17.5f, RainRadius = 10f;
        // the storm
        public float BuildSeconds = 1.75f; public int Waves = 3, ArrowsPerWave = 8; public float WaveGapSeconds = 1.0f;
        public float DomeRadius = 2.5f, DomeWarningSeconds = 1.5f, MinImpactSpacing = 1.5f, CasterClearance = 3f, ArrowLength = 5f, FallSeconds = 0.6f;
        public float ImpactDamage = 80f, ImpactCripple = 80f, BlastRadius = 4f;
        // the World-Nail (NailStandSeconds was 20 until 0.18.1; the owner, play-testing 0.18.0: "the big arrow lingers for too long")
        public float NailLength = 46f, NailGapSeconds = 1.0f, NailSlideSeconds = 2.5f, NailDomeRadius = 10f, NailBlastRadius = 12f, NailDamage = 400f, NailStandSeconds = 6f;
        public int MaxLiveExplosions = 12;
        // the sky
        public float SkyDimMetres = 150f, SkySun = 0.5f, SkyFade = 0.6f, SkyFadeSeconds = 2f, FestivalCareRadius = 4f;

        public static SkyfallValues Default => new SkyfallValues();

        /// <summary>The fields only a client's looks read (per client, never [shared]): the sigil's and the arrows' drawn sizes, the explosion
        /// budget and the sky dial. Every other field decides the trigger, the centre, the pattern, its timing or the server's damage, and is
        /// [shared] (Skyfall.Init). Tested: none of these changes the pattern, the trigger or the centre.</summary>
        public static readonly string[] LooksOnly = {
            nameof(SigilRadius), nameof(ArrowLength), nameof(NailLength), nameof(MaxLiveExplosions),
            nameof(SkyDimMetres), nameof(SkySun), nameof(SkyFade), nameof(SkyFadeSeconds),
        };

        // The ceilings: 10 waves of 32 arrows, 64 live explosions; the sky never fades past 0.95; the arm time stays under the
        // claim's 8190 ms time cap; every sigil-centre offset (height and distance) stays inside the marks' +-64 m (MarkRange).
        public const int MaxWaves = 10, MaxArrowsPerWave = 32, MaxExplosionsCap = 64;
        public const float MaxSkyFade = 0.95f, MaxArmSeconds = 8f, MaxReachMetres = 60f;

        /// <summary>A copy held to safe ranges. A NaN setting falls back to its default, an infinite one to its bound. Then:
        /// MaxDistance is raised to MinDistance, BuildSeconds to DomeWarningSeconds (the first wave's warning fits), and
        /// FallSeconds lowered to BuildSeconds (no arrow starts falling before the release).</summary>
        public SkyfallValues Clamped() {
            var d = Default;
            var c = (SkyfallValues)MemberwiseClone();   // a field added later is copied too
            c.MinStrength = C(MinStrength, 0.5f, 1f, d.MinStrength);
            c.MinElevationDegrees = C(MinElevationDegrees, 10f, 89f, d.MinElevationDegrees);
            c.ArmSeconds = C(ArmSeconds, 0.25f, MaxArmSeconds, d.ArmSeconds);
            c.SigilHeight = C(SigilHeight, 5f, MaxReachMetres, d.SigilHeight);
            c.MinDistance = C(MinDistance, 0f, MaxReachMetres, d.MinDistance);
            c.MaxDistance = C(MaxDistance, 0f, MaxReachMetres, d.MaxDistance);
            if (c.MaxDistance < c.MinDistance) c.MaxDistance = c.MinDistance;
            c.SigilRadius = C(SigilRadius, 1f, 100f, d.SigilRadius);
            c.RainRadius = C(RainRadius, 1f, 40f, d.RainRadius);
            c.DomeWarningSeconds = C(DomeWarningSeconds, 0.5f, 10f, d.DomeWarningSeconds);
            c.BuildSeconds = C(BuildSeconds, 0f, 10f, d.BuildSeconds);
            if (c.BuildSeconds < c.DomeWarningSeconds) c.BuildSeconds = c.DomeWarningSeconds;
            c.Waves = I(Waves, 1, MaxWaves);
            c.ArrowsPerWave = I(ArrowsPerWave, 1, MaxArrowsPerWave);
            c.WaveGapSeconds = C(WaveGapSeconds, 0.1f, 10f, d.WaveGapSeconds);
            c.DomeRadius = C(DomeRadius, 0.5f, 10f, d.DomeRadius);
            c.MinImpactSpacing = C(MinImpactSpacing, 0f, 10f, d.MinImpactSpacing);
            c.CasterClearance = C(CasterClearance, 0f, 20f, d.CasterClearance);
            c.ArrowLength = C(ArrowLength, 0.5f, 50f, d.ArrowLength);
            c.FallSeconds = C(FallSeconds, 0.05f, 5f, d.FallSeconds);
            if (c.FallSeconds > c.BuildSeconds) c.FallSeconds = c.BuildSeconds;
            c.ImpactDamage = C(ImpactDamage, 0f, 100000f, d.ImpactDamage);
            c.ImpactCripple = C(ImpactCripple, 0f, 100000f, d.ImpactCripple);
            c.BlastRadius = C(BlastRadius, 0.5f, 20f, d.BlastRadius);
            c.NailLength = C(NailLength, 1f, 200f, d.NailLength);
            c.NailGapSeconds = C(NailGapSeconds, 0f, 10f, d.NailGapSeconds);
            c.NailSlideSeconds = C(NailSlideSeconds, 0.1f, 10f, d.NailSlideSeconds);
            c.NailDomeRadius = C(NailDomeRadius, 0.5f, 30f, d.NailDomeRadius);
            c.NailBlastRadius = C(NailBlastRadius, 0.5f, 40f, d.NailBlastRadius);
            c.NailDamage = C(NailDamage, 0f, 100000f, d.NailDamage);
            c.NailStandSeconds = C(NailStandSeconds, 0f, 120f, d.NailStandSeconds);
            c.MaxLiveExplosions = I(MaxLiveExplosions, 1, MaxExplosionsCap);
            c.SkyDimMetres = C(SkyDimMetres, 0f, 1000f, d.SkyDimMetres);
            c.SkySun = C(SkySun, 0f, 1f, d.SkySun);
            c.SkyFade = C(SkyFade, 0f, MaxSkyFade, d.SkyFade);
            c.SkyFadeSeconds = C(SkyFadeSeconds, 0f, 10f, d.SkyFadeSeconds);
            c.FestivalCareRadius = C(FestivalCareRadius, 0f, 20f, d.FestivalCareRadius);
            return c;
        }

        // NaN -> the field's default; -Infinity and +Infinity fall to lo and hi by the comparisons themselves
        static float C(float x, float lo, float hi, float fallback) => float.IsNaN(x) ? fallback : x < lo ? lo : x > hi ? hi : x;
        static int I(int x, int lo, int hi) => x < lo ? lo : x > hi ? hi : x;
    }

    /// <summary>The claim's window bits when the claim is unsteered (Task 5). A shooter only ever requests Skyfall; the server relays
    /// Skyfall, Festival (accepted while festival mode is on) or Fizzled (refused). The values are on the wire: never renumber.</summary>
    internal enum SkyCode { None = 0, Skyfall = 1, Festival = 2, Fizzled = 3 }

    // Filled in by Skyfall.PoseOf (from Claims.Bits) and by the tests.
    internal struct ShotPose { public HVec Pos; public HVec Dir; public float Strength; }   // the release as it went on the wire

    internal static class SkyfallRules {
        /// <summary>The trigger's slacks: Atan2's last-bit error at exactly MinElevationDegrees, the wire's 1/1000 strength grid
        /// (Shooter's SerializeFloat(0, 1, 0.001)) at exactly MinStrength, and a float hold at exactly ArmSeconds.</summary>
        public const double AngleSlackDegrees = 1e-6;
        public const float StrengthSlack = 1e-4f;
        public const double HeldSlackSeconds = 1e-4;

        /// <summary>The sigil centre's offset from the release, per axis, rides the claim's 17-bit mark slots in 1/1024 m steps
        /// from -64 m (mark 0); zero is mark 65536, the top mark 131071 is +63.9990234375 m (+64 m itself does not fit).</summary>
        public const int MarkBits = 17; public const double MarkRange = 64.0;
        public const int MarkSteps = 1024;
        public const uint MarkMax = 131071u;   // 2^17 - 1

        /// <summary>[0, 1): Skyfall's own repeatable number from a seed (the release's arrow id), an index and a salt. It COUNTS: the
        /// pattern's damage spots come from it, so it must be bit-identical on every machine (integer arithmetic, one exact scale).</summary>
        public static float Unit(uint seed, int index, int salt) {
            unchecked {
                uint h = seed * 0xCC9E2D51u + (uint)index * 0x1B873593u + (uint)salt * 0xE6546B64u + 0x5F1E7A11u;
                h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>Degrees above level (negative below); any length. NaN for a broken or zero direction.</summary>
        public static double ElevationDegrees(HVec dir) {
            if (!dir.Finite) return double.NaN;
            double h = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            if (h == 0 && dir.Y == 0) return double.NaN;
            return Math.Atan2(dir.Y, h) * (180.0 / Math.PI);
        }

        /// <summary>A full draw at the sky: strength at least MinStrength and elevation at least MinElevationDegrees (NaN fails both).</summary>
        public static bool Aimed(ShotPose pose, SkyfallValues v) =>
            pose.Strength >= v.MinStrength - StrengthSlack && ElevationDegrees(pose.Dir) >= v.MinElevationDegrees - AngleSlackDegrees;

        /// <summary>Aimed and held at least ArmSeconds (a NaN hold is not armed).</summary>
        public static bool Armed(ShotPose pose, double heldSeconds, SkyfallValues v) =>
            Aimed(pose, v) && heldSeconds >= v.ArmSeconds - HeldSlackSeconds;

        /// <summary>The server's decision on a shooter's Skyfall request: a granted caster with no storm running, an aimed release
        /// pose, and BOTH the claimed hold and the time the server decides with (<paramref name="effectiveMs"/>, ClaimTrust's pick
        /// under [Claim] TrustMs) at least ArmSeconds. So a claim saturated at the 8190 ms cap or behind the server's clock still
        /// casts on the server's own time, and a claim far ahead never casts on the client's time alone.</summary>
        public static bool ServerAccepts(bool granted, bool casterBusy, ShotPose pose, int? claimedMs, int? effectiveMs, SkyfallValues v) {
            if (!granted || casterBusy || !Aimed(pose, v) || !claimedMs.HasValue || !effectiveMs.HasValue) return false;
            double arm = v.ArmSeconds - HeldSlackSeconds;
            return claimedMs.Value / 1000.0 >= arm && effectiveMs.Value / 1000.0 >= arm;   // both clocks; effectiveMs is ClaimTrust's pick
        }

        /// <summary>The server's relayed code: only a shooter's Skyfall request counts; refused = Fizzled.</summary>
        public static SkyCode Relay(SkyCode requested, bool accepted, bool festival) =>
            requested != SkyCode.Skyfall ? SkyCode.None : !accepted ? SkyCode.Fizzled : festival ? SkyCode.Festival : SkyCode.Skyfall;

        /// <summary>Where the aim meets the plane SigilHeight above the release, its distance along the ground held to
        /// [MinDistance, MaxDistance]. Straight up (or a broken direction) goes along +Z; level or downward goes MaxDistance out.</summary>
        public static HVec SigilCentre(HVec release, HVec dir, SkyfallValues v) {
            double h = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            double gx = 0, gz = 1;
            if (dir.Finite && h >= 1e-9) { gx = dir.X / h; gz = dir.Z / h; }
            double d = dir.Finite && dir.Y > 0 ? v.SigilHeight * h / dir.Y : v.MaxDistance;
            if (d < v.MinDistance) d = v.MinDistance;
            if (d > v.MaxDistance) d = v.MaxDistance;
            return new HVec(release.X + gx * d, release.Y + v.SigilHeight, release.Z + gz * d);
        }

        /// <summary>An offset in metres to its mark: nearest 1/1024 m step, held to [0, MarkMax]; NaN counts as 0 m.</summary>
        public static uint QuantizeOffset(double metres) {
            if (double.IsNaN(metres)) metres = 0;
            double s = Math.Floor((metres + MarkRange) * MarkSteps + 0.5);
            if (!(s > 0)) return 0;
            if (s > MarkMax) return MarkMax;
            return (uint)s;
        }

        /// <summary>A mark back to metres; bits above the 17th are ignored.</summary>
        public static double OffsetOf(uint mark) => (mark & MarkMax) / (double)MarkSteps - MarkRange;
    }

    /// <summary>One impact of a storm: a rain arrow or the World-Nail. Times are seconds after the release.</summary>
    internal struct SkyImpact { public int Index; public int Wave; public double X, Z; public float DomeAt, LandAt; public bool Nail; }

    // Made by SkyfallSeam.Storm (every machine's decision, Task 8) and by the tests.
    /// <summary>One running storm: the release's arrow id and relayed code, the release point, the sigil centre, when it started
    /// (on the local clock) and its planned impacts.</summary>
    internal sealed class SkyStorm {
        public uint ArrowId; public SkyCode Code; public HVec Release; public double CX, CY, CZ; public float StartTime;
        public SkyImpact[] Impacts = new SkyImpact[0]; public int CasterAccount;
        public bool Festival => Code == SkyCode.Festival;
    }

    /// <summary>The storm's pattern and clock. Every machine plans the same storm from the arrow id, the relayed centre and the
    /// [shared] values, so these spots decide damage. The pattern depends only on positions relative to the centre: moving the
    /// centre and the caster together moves every impact with them. Only integer hashing, IEEE + - * / and comparisons in
    /// doubles (no trig), so it is bit-identical everywhere; any change to it changes the storm and needs the new DLL on every
    /// machine. The one-storm-per-caster rule is SkyfallSeam.BusyUntil (Task 8), not here.</summary>
    internal static class SkyfallPattern {
        /// <summary>Candidates per rain spot, and the salts of their two coordinates in <see cref="SkyfallRules.Unit"/>.</summary>
        public const int TriesPerSpot = 64;
        public const int SaltX = 1, SaltZ = 2;

        /// <summary>When the World-Nail lands, in seconds after the release: NailGapSeconds and NailSlideSeconds after the last
        /// wave. <paramref name="v"/> must already be clamped.</summary>
        public static float NailLandAt(SkyfallValues v) => v.BuildSeconds + (v.Waves - 1) * v.WaveGapSeconds + v.NailGapSeconds + v.NailSlideSeconds;

        /// <summary>Seconds after the release until the Nail has stood and crumbled. Clamps (one small allocation): call once per
        /// storm, never per frame.</summary>
        public static float EndsAt(SkyfallValues v) { v = v.Clamped(); return NailLandAt(v) + v.NailStandSeconds; }

        /// <summary>The storm's impacts: Waves * ArrowsPerWave rain spots (wave-major, Index = place in the array), then the Nail at
        /// the centre, last. Deterministic from the arrow id, the [shared] values and the caster's offset from the centre. Every rain
        /// spot that can be is at least BlastRadius + CasterClearance from the caster, so no rain kill zone reaches the caster's safe
        /// circle; with no room left, a spot goes to the disc's far edge from the caster.</summary>
        public static SkyImpact[] Plan(uint arrowId, double cx, double cz, double casterX, double casterZ, SkyfallValues v) {
            v = v.Clamped();
            int rain = v.Waves * v.ArrowsPerWave;
            var result = new SkyImpact[rain + 1];
            double r = v.RainRadius, keep = v.BlastRadius + v.CasterClearance, gap = v.MinImpactSpacing;   // the safe circle (Task 12c)
            double ox = casterX - cx, oz = casterZ - cz;                       // the caster, seen from the centre
            var px = new List<double>(rain + 1) { 0 };                          // placed offsets; the Nail's spot first
            var pz = new List<double>(rain + 1) { 0 };
            float lastRain = v.BuildSeconds + (v.Waves - 1) * v.WaveGapSeconds;
            float burn = Math.Max(0f, Math.Min(v.BuildSeconds - v.DomeWarningSeconds, v.WaveGapSeconds));
            bool crowded = false;
            for (int i = 0; i < rain; i++) {
                bool found = false, any = false; double bx = 0, bz = 0, bestGap2 = -1;
                for (int t = 0; t < TriesPerSpot; t++) {
                    double dx = (2.0 * SkyfallRules.Unit(arrowId, i * TriesPerSpot + t, SaltX) - 1.0) * r;
                    double dz = (2.0 * SkyfallRules.Unit(arrowId, i * TriesPerSpot + t, SaltZ) - 1.0) * r;
                    if (dx * dx + dz * dz > r * r) continue;                    // outside the rain disc
                    double ex = dx - ox, ez = dz - oz;
                    if (ex * ex + ez * ez < keep * keep) continue;              // its kill zone would reach the caster's safe circle
                    if (crowded) { bx = dx; bz = dz; found = true; break; }     // no room for spacing any more: first legal spot
                    double g2 = double.MaxValue;
                    for (int k = 0; k < px.Count; k++) { double ax = dx - px[k], az = dz - pz[k]; double d2 = ax * ax + az * az; if (d2 < g2) g2 = d2; }
                    if (g2 >= gap * gap) { bx = dx; bz = dz; found = true; break; }
                    if (g2 > bestGap2) { bestGap2 = g2; bx = dx; bz = dz; any = true; }
                }
                if (!found) crowded = true;
                if (!found && !any) {                                           // nothing legal: the disc's far edge from the caster
                    double l = Math.Sqrt(ox * ox + oz * oz);
                    if (l < 1e-9) { bx = 0; bz = r; } else { bx = -ox / l * r; bz = -oz / l * r; }
                }
                px.Add(bx); pz.Add(bz);
                int w = i / v.ArrowsPerWave;
                result[i] = new SkyImpact { Index = i, Wave = w, X = cx + bx, Z = cz + bz,
                                            LandAt = v.BuildSeconds + w * v.WaveGapSeconds, DomeAt = burn * i / rain, Nail = false };
            }
            float nail = NailLandAt(v);
            result[rain] = new SkyImpact { Index = rain, Wave = v.Waves, X = cx, Z = cz, LandAt = nail,
                                           DomeAt = Math.Min(lastRain, nail - v.DomeWarningSeconds), Nail = true };
            return result;
        }
    }

    /// <summary>The client's cap on live explosions: one expiry time per slot, no allocation per call. A slot expires exactly at
    /// start + lifetime.</summary>
    internal sealed class ExplosionBudget {
        readonly float[] until;

        public ExplosionBudget(int cap) {
            until = new float[cap < 0 ? 0 : cap];
            for (int i = 0; i < until.Length; i++) until[i] = float.NegativeInfinity;
        }

        /// <summary>Takes the first free slot until now + lifetime; false when none is free. A lifetime that is not above 0 (NaN
        /// included) counts as 0: it still needs a free slot, and frees it at once.</summary>
        public bool TryTake(float now, float lifetime) {
            if (!(lifetime > 0f)) lifetime = 0f;
            for (int i = 0; i < until.Length; i++)
                if (!(until[i] > now)) { until[i] = now + lifetime; return true; }
            return false;
        }

        /// <summary>The slots still alive at <paramref name="now"/>.</summary>
        public int Live(float now) {
            int n = 0;
            for (int i = 0; i < until.Length; i++) if (until[i] > now) n++;
            return n;
        }
    }

    /// <summary>The storm's sky dim: 0 = no dim, 1 = full dim.</summary>
    internal static class SkyDial {
        /// <summary>0 with no storm, in a cave, before the start, after the end or on a broken time; 1 with no fade; otherwise a
        /// smoothstep in over the first and out over the last <paramref name="fadeSeconds"/>.</summary>
        public static float Amount(bool active, bool inCave, float sinceStart, float untilEnd, float fadeSeconds) {
            if (!active || inCave) return 0f;
            if (float.IsNaN(sinceStart) || sinceStart < 0f || !(untilEnd > 0f)) return 0f;
            if (!(fadeSeconds > 0f)) return 1f;
            float a = Math.Min(1f, Math.Min(sinceStart, untilEnd) / fadeSeconds);
            return a * a * (3f - 2f * a);
        }
    }

    /// <summary>Who holds Skyfall: account -> name, the server's file UserData\BluesArcheryOverhaul\skyfall-grants-v2.txt
    /// (SkyfallConsole.GrantFile; plain <c>account=name</c> lines, accounts ascending, under <see cref="Header"/>). Hand edits are read
    /// only at start-up; the next give or take rewrites the file. Accounts are positive; names are trimmed, non-empty and on one line.</summary>
    internal sealed class SkyfallGrants {
        public const string Header = "# Skyfall grants (QuickshotRing): account=name, one per line. Edit only while the server is stopped.";

        readonly Dictionary<int, string> map = new Dictionary<int, string>();

        /// <summary>Adds or renames a grant; true when the table changed. False for an account not above 0, a blank or multi-line
        /// name, or the same name again.</summary>
        public bool Give(int account, string name) {
            if (account <= 0 || name == null) return false;
            string n = name.Trim();
            if (n.Length == 0 || n.IndexOf('\n') >= 0 || n.IndexOf('\r') >= 0) return false;
            if (map.TryGetValue(account, out string? old) && old == n) return false;
            map[account] = n;
            return true;
        }

        /// <summary>True when the account held a grant.</summary>
        public bool Take(int account) => map.Remove(account);

        /// <summary>The one account granted under this name (trimmed, any case); null for none, a blank name or two or more.</summary>
        public int? FindByName(string name) {
            string n = (name ?? "").Trim();
            if (n.Length == 0) return null;
            int? found = null;
            foreach (var kv in map) {
                if (!string.Equals(kv.Value, n, StringComparison.OrdinalIgnoreCase)) continue;
                if (found.HasValue) return null;   // two grants with one name: never guessed between
                found = kv.Key;
            }
            return found;
        }

        /// <summary>Every grant, sorted by account; a new list each call.</summary>
        public IReadOnlyList<KeyValuePair<int, string>> All {
            get {
                var l = new List<KeyValuePair<int, string>>(map);
                l.Sort((a, b) => a.Key.CompareTo(b.Key));
                return l;
            }
        }

        public string Serialize() {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (var kv in All) sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append('=').Append(kv.Value).Append('\n');
            return sb.ToString();
        }

        /// <summary>Reads the file; null or empty is no grants. Blank and <c>#</c> lines and bad lines are skipped; the line splits
        /// at its first '=' (so a name may hold '='); a repeated account keeps its last line.</summary>
        public static SkyfallGrants Parse(string? text) {
            var g = new SkyfallGrants();
            if (string.IsNullOrEmpty(text)) return g;
            foreach (string raw in text!.Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!int.TryParse(line.Substring(0, eq).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int a)) continue;
                g.Give(a, line.Substring(eq + 1));   // the name rules, and a later line wins
            }
            return g;
        }
    }

    /// <summary>What a look at the grant file found (SkyfallGrantStore.Table; Skyfall.Grants logs it). Cached: nothing was read.</summary>
    internal enum GrantLoad { Cached = 0, NoFile, Loaded, Migrated, Unreadable, StillUnreadable, Recovered }

    /// <summary>The server's grant file as Skyfall.Grants keeps it (final review: I-1 and the Task 7 minor). Read at the first use. A
    /// file that exists but cannot be read (an offline OneDrive placeholder) is never taken for "nobody holds Skyfall" for good: the
    /// table is empty for now (the safe direction), every save refuses so the unread file is never overwritten, and the read is tried
    /// again at most every <see cref="RetrySeconds"/>; once it can be read, the file's grants and the ones given meanwhile are both kept
    /// (the latter win for the same account) and written. With no file of this build's name, a file of the old name (0.18.0's) is
    /// carried over; either way a file of the old name is then moved aside, so no earlier build ever reads a grant again.</summary>
    internal sealed class SkyfallGrantStore {
        public const float RetrySeconds = 10f;

        /// <summary>The server's files (Skyfall.cs: UserData\BluesArcheryOverhaul; the tests: memory), by file name.</summary>
        internal interface IFiles {
            /// <summary>The file's text; null when there is no such file. Throws when it exists but cannot be read.</summary>
            string? Read(string name);
            /// <summary>Writes the file whole; throws when it cannot.</summary>
            void Write(string name, string text);
            /// <summary>Moves the file aside (out of every build's way); false when there is none. Throws when it cannot.</summary>
            bool Retire(string name);
        }

        readonly IFiles files;
        SkyfallGrants table = new SkyfallGrants();
        bool loaded;
        float retryAt;

        public SkyfallGrantStore(IFiles files) { this.files = files; }

        /// <summary>Why the file could not be read, while it cannot (no save is allowed then); null once it has been read.</summary>
        public string? ReadError { get; private set; }
        public bool Unreadable => ReadError != null;
        /// <summary>The last load's trouble besides reading (a carry-over or a recovery that could not be written, an old file that
        /// could not be moved aside); null when there was none. The table is right either way.</summary>
        public string? Problem { get; private set; }
        /// <summary>The last load moved a file of the old name aside.</summary>
        public bool RetiredOld { get; private set; }

        /// <summary>The grants: read at the first call, then kept (Cached); while the file cannot be read, the grants given since (none
        /// at first), read again from <paramref name="now"/> RetrySeconds after the last try (a broken time never retries).</summary>
        public SkyfallGrants Table(float now, out GrantLoad what) {
            what = GrantLoad.Cached;
            if (loaded && !Unreadable) return table;
            if (Unreadable && !(now >= retryAt)) return table;
            bool recovering = Unreadable;
            Problem = null;
            RetiredOld = false;
            string? text;
            bool fromOld = false;
            try {
                text = files.Read(SkyfallConsole.GrantFile);
                if (text == null) { text = files.Read(SkyfallConsole.OldGrantFile); fromOld = text != null; }
            }
            catch (Exception e) {
                ReadError = e.Message.Length > 0 ? e.Message : e.GetType().Name;
                loaded = true;
                retryAt = now + RetrySeconds;
                what = recovering ? GrantLoad.StillUnreadable : GrantLoad.Unreadable;
                return table;
            }
            SkyfallGrants read = SkyfallGrants.Parse(text);
            int meanwhile = 0;
            if (recovering) foreach (KeyValuePair<int, string> kv in table.All) { read.Give(kv.Key, kv.Value); meanwhile++; }
            table = read;
            loaded = true;
            ReadError = null;
            what = recovering ? GrantLoad.Recovered : fromOld ? GrantLoad.Migrated : text == null ? GrantLoad.NoFile : GrantLoad.Loaded;
            if (fromOld || meanwhile > 0) {
                try { files.Write(SkyfallConsole.GrantFile, table.Serialize()); }
                catch (Exception e) {   // the grants stand in memory; the old file stays until a later start-up carries it over
                    Problem = SkyfallConsole.GrantFile + " could not be written (" + e.Message + ")";
                    return table;
                }
            }
            try { RetiredOld = files.Retire(SkyfallConsole.OldGrantFile); }
            catch (Exception e) { Problem = SkyfallConsole.OldGrantFile + " could not be moved aside (" + e.Message + ")"; }
            return table;
        }

        /// <summary>Writes the table whole: null when written, else why not. Refused, writing nothing, while the file cannot be read
        /// (the unread grants are never overwritten) or before it was first read.</summary>
        public string? Save() {
            if (Unreadable) return "it could not be read (" + ReadError + "); it is left as it is and read again every "
                                   + RetrySeconds.ToString("0", CultureInfo.InvariantCulture) + " s";
            if (!loaded) return "it has not been read yet";
            try { files.Write(SkyfallConsole.GrantFile, table.Serialize()); return null; }
            catch (Exception e) { return e.Message; }
        }
    }

    /// <summary>The one invisible message (the owner-approved exception to "no new traffic"): the server tells a holder's game, in the
    /// game's own player text message, <c>QSR_SKYFALL_V1|holds 0/1|festival 0/1|check as 2 hex digits</c>. 21 ASCII characters, no
    /// player name (PlayerTextMessage is serialised as ASCII).</summary>
    internal static class SkyfallSignal {
        public const string Tag = "QSR_SKYFALL_V1";
        const string Prefix = Tag + "|";

        public static string Format(bool holds, bool festival, byte check) =>
            Prefix + (holds ? "1" : "0") + "|" + (festival ? "1" : "0") + "|" + check.ToString("x2", CultureInfo.InvariantCulture);

        /// <summary>Anything that starts <c>QSR_SKYFALL_V1|</c>, well-formed or not.</summary>
        public static bool IsOurs(string? text) => text != null && text.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Exactly three fields after the tag: "0"/"1", "0"/"1" and two hex digits (either case). A refusal leaves the outputs
        /// false, false, 0.</summary>
        public static bool TryParse(string? text, out bool holds, out bool festival, out byte check) {
            holds = false; festival = false; check = 0;
            if (!IsOurs(text)) return false;
            string[] p = text!.Substring(Prefix.Length).Split('|');
            if (p.Length != 3 || !Bit(p[0], out bool h) || !Bit(p[1], out bool f) || p[2].Length != 2) return false;
            int hi = Hex(p[2][0]), lo = Hex(p[2][1]);   // by hand: byte.TryParse with HexNumber would take spaces
            if (hi < 0 || lo < 0) return false;
            holds = h; festival = f; check = (byte)(hi * 16 + lo);
            return true;
        }

        /// <summary>Any text that starts <c>QSR_SKYFALL_</c> (ordinal, case for case), whatever its version: the client never shows such a
        /// message (SkyfallLink.Swallow); TryParse reads only this version's.</summary>
        public static bool Hidden(string? text) => text != null && text.StartsWith("QSR_SKYFALL_", StringComparison.Ordinal);

        /// <summary>The server's shared-settings check differs from this game's: the whole byte, not the build block's 6 bits.</summary>
        public static bool Differs(byte serverCheck, byte localCheck) => serverCheck != localCheck;

        static bool Bit(string s, out bool b) { b = s == "1"; return s == "0" || s == "1"; }
        static int Hex(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
    }

    /// <summary>The console's player lookup for <c>skyfall give/take &lt;player&gt;</c>.</summary>
    internal static class SkyfallNames {
        /// <summary>The one online name equal to the trimmed query in any case (never a prefix), as spelled online, with
        /// <paramref name="refusal"/> "". Otherwise null and the refusal text, which ends with who is online (sorted in any case, ties
        /// ordinal; "nobody"). Blank online entries are skipped; two names differing only in case are refused, never guessed
        /// between. The texts are pinned by the tests: a caller may prefix them, never reword them.</summary>
        public static string? Match(IEnumerable<string> online, string query, out string refusal) {
            var names = new List<string>();
            if (online != null)
                foreach (string? n in online) if (!string.IsNullOrWhiteSpace(n)) names.Add(n!);
            names.Sort(Order);
            string list = names.Count == 0 ? "nobody" : string.Join(", ", names.ToArray());
            string q = (query ?? "").Trim();
            if (q.Length == 0) { refusal = "name a player (online: " + list + ")"; return null; }
            var hits = new List<string>();
            foreach (string n in names) if (string.Equals(n.Trim(), q, StringComparison.OrdinalIgnoreCase)) hits.Add(n);
            if (hits.Count == 1) { refusal = ""; return hits[0]; }
            refusal = hits.Count == 0
                ? "no player named '" + q + "' is online (online: " + list + ")"
                : "'" + q + "' matches more than one online player: " + string.Join(", ", hits.ToArray()) + " (online: " + list + ")";
            return null;
        }

        static int Order(string a, string b) {
            int c = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }
    }

    /// <summary>Who a landing catches. Every kill (Task 12c) is <see cref="InDome"/>: the hemisphere of the kill radius standing on
    /// the ground at the impact, the shape the World-Nail's dome is drawn in, with <see cref="Slack"/> over its surface and under the
    /// ground. A creature is tested at its point nearest the landing (<see cref="Nearer"/> over its body colliders' closest points and
    /// its PositionHandler point), a player at their <see cref="Feet"/>. <see cref="Inside"/> is Task 9's cylinder, kept for festival
    /// care (dormant); no kill uses it.</summary>
    internal static class SkyfallCaught {
        /// <summary>Metres over a dome's surface, and under the ground, that still count as inside it.</summary>
        public const double Slack = 1.0;
        /// <summary>A player's feet are this far under their head.</summary>
        public const double EyeHeight = 1.7;

        /// <summary>A cylinder round the impact, out to the radius along the ground, and from 1 m under the ground to radius + 3 m over
        /// it (both ends in). A player on a ledge high over it or in a cave under it is not caught.</summary>
        public static bool Inside(HVec head, double cx, double groundY, double cz, double radius) {
            if (!head.Finite || !(radius > 0) || !Finite(cx) || !Finite(groundY) || !Finite(cz)) return false;
            double dx = head.X - cx, dz = head.Z - cz;
            return dx * dx + dz * dz <= radius * radius && head.Y >= groundY - 1.0 && head.Y <= groundY + radius + 3.0;
        }

        /// <summary>Inside the hemisphere of <paramref name="radius"/> standing on the ground at (cx, groundY, cz): along the ground d
        /// at most the radius, and the height over the ground from -Slack up to sqrt(r * r - d * d) + Slack (every bound in). A broken
        /// point, centre or ground, or a radius not above 0, catches nothing.</summary>
        public static bool InDome(HVec point, double cx, double groundY, double cz, double radius) {
            if (!point.Finite || !(radius > 0) || !Finite(radius) || !Finite(cx) || !Finite(groundY) || !Finite(cz)) return false;
            double dx = point.X - cx, dz = point.Z - cz, d2 = dx * dx + dz * dz, r2 = radius * radius;
            if (!(d2 <= r2)) return false;
            double up = point.Y - groundY;
            return up >= -Slack && up <= Math.Sqrt(r2 - d2) + Slack;
        }

        /// <summary>Where a player is tested: EyeHeight under the head, but never under the dome's floor (groundY - Slack) while the head
        /// itself is at or above it, so a player lying down is tested at the floor and one in a cave under the landing keeps their real
        /// feet (and is not caught).</summary>
        public static HVec Feet(HVec head, double groundY) {
            double y = head.Y - EyeHeight, floor = groundY - Slack;
            if (y < floor && head.Y >= floor) y = floor;
            return new HVec(head.X, y, head.Z);
        }

        /// <summary>Where a creature is tested (final review I-1): of its points, the one nearer to the landing's ground point
        /// <paramref name="to"/>. SkyfallServer starts from its PositionHandler point (the HealthObject's origin for most creatures) and
        /// offers the closest point of each of its body colliders the landing's overlap found, so a big creature whose near side is in
        /// the kill zone is caught though its origin is not. A broken candidate never wins over a finite one; a tie keeps the first.</summary>
        public static HVec Nearer(HVec to, HVec best, HVec candidate) {
            if (!candidate.Finite) return best;
            if (!best.Finite) return candidate;
            HVec a = best - to, b = candidate - to;
            return HVec.Dot(b, b) < HVec.Dot(a, a) ? candidate : best;
        }

        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }

    /// <summary>Why the server's verdict on a release came out as it did (Task 8, the seam): the first check that failed, in
    /// <see cref="SkyfallSeam.Server"/>'s order, or Cast. Plain is the default (no verdict at all). Log text only.</summary>
    internal enum SkyWhy { Plain = 0, Cast, NotRequested, NotGranted, Busy, NoPose, NotAimed, NotArmed, NoCentre, CentreFar }

    /// <summary>Task 12d: what the server does after one try at destroying a held real arrow (<see cref="SkyfallSeam.DestroyNext"/>):
    /// Done (it is going: forget the hold), Retry (try again next tick, still held), GiveUp (let it go as a normal arrow).</summary>
    internal enum SkyDestroy { Done = 0, Retry, GiveUp }

    /// <summary>The server's verdict on one release: the code it relays, why, and the centre marks it relays (none for a plain shot).</summary>
    internal sealed class SkyVerdict { public SkyCode Code; public SkyWhy Why; public List<uint> Marks = new List<uint>(); }

    /// <summary>The seam between a bow shot and a Skyfall storm (Task 8): every decision the game-facing code makes about a release,
    /// pure. The shooter asks (ShooterAsks) and turns its claim into a request from the WIRE pose (Finalise); the server decides
    /// (Server) and relays (Relay); a spectator reads the relay (Seen); every machine plans the same storm from the arrow id, the
    /// relayed marks and its own copy of the release point (Storm), which is why the marks are offsets from the release point and
    /// never absolute positions. A release that is not a Skyfall release never reaches anything here but the one lookup that says so.</summary>
    internal static class SkyfallSeam {
        public const int CentreMarks = 3;                 // x, y, z offsets of the sigil centre from the release point (ShotClaimWire.MaxMarks)
        public const double CentreSlackMetres = 2.0;      // server: the shooter's centre is kept within this of its own
        public const float BusySlackSeconds = 0.5f;       // the caster's game stays busy this much longer, the server this much shorter
        public const float DestroySeconds = 1.0f;         // server: the held real arrow is destroyed this long after the release
        public const float ClientHoldSeconds = 5f;        // clients: a held arrow still there this long after the release is let go
        public const int DestroyTries = 4;                // server: the destroy at DestroySeconds, then one on each of the next 3 ticks
        /// <summary>How long a per-arrow seam record (the shooter's candidate and request, every machine's decision) is read: as a
        /// claim (ClaimStore.Lifetime). Every reader runs in the release's own frame; an arrow id is reused once its entity is gone
        /// (EntityManager's fixed id pool), so an older record must never meet a later shot of the same id (final review, Task 8 minor).</summary>
        public const float RecordSeconds = ClaimStore.Lifetime;

        /// <summary>A record made at <paramref name="at"/> may be read at <paramref name="now"/>: from 0 to RecordSeconds old. A broken
        /// or backward time is stale.</summary>
        public static bool Fresh(float at, float now) {
            float age = now - at;
            return age >= 0f && age <= RecordSeconds;
        }

        /// <summary>Task 12d, server: after the <paramref name="triesSoFar"/>-th try at destroying a held real arrow (counting this
        /// one), whether it is going (<paramref name="destroyed"/>: Done), worth another try next tick (Retry, while fewer than
        /// DestroyTries were made) or to be let go as a normal arrow (GiveUp). Another mod can swallow the game's destroy without a
        /// word (RevampedSkills' Holy Arrow keep, docs/superpowers/specs/skyfall-research/revampedskills-compat.md).</summary>
        public static SkyDestroy DestroyNext(int triesSoFar, bool destroyed) =>
            destroyed ? SkyDestroy.Done : triesSoFar < DestroyTries ? SkyDestroy.Retry : SkyDestroy.GiveUp;

        /// <summary>(NaN, NaN, NaN): no centre, or no release point.</summary>
        public static readonly HVec Unknown = new HVec(double.NaN, double.NaN, double.NaN);

        /// <summary>The release every machine decides for a Skyfall release (cast or fizzle): the resolver's own empty release
        /// (Release.cs, its fall-through), so everything downstream of Draws.Release sees a plain Early shot with nothing to fire.</summary>
        public static readonly Resolution Neutral = new Resolution(ShotKind.Early, UltimateUse.None, new ReleasePlan(0, 0, 0), 0);

        /// <summary>The sigil centre as three marks: its offset from the release point per axis (SkyfallRules.QuantizeOffset).</summary>
        public static List<uint> MarksOf(HVec release, HVec centre) => new List<uint> {
            SkyfallRules.QuantizeOffset(centre.X - release.X),
            SkyfallRules.QuantizeOffset(centre.Y - release.Y),
            SkyfallRules.QuantizeOffset(centre.Z - release.Z),
        };

        /// <summary>MarksOf's inverse over this machine's release point. False with <see cref="Unknown"/> unless there are exactly
        /// CentreMarks marks and the release point is finite.</summary>
        public static bool TryCentre(HVec release, IList<uint>? marks, out HVec centre) {
            centre = Unknown;
            if (marks == null || marks.Count != CentreMarks || !release.Finite) return false;
            centre = new HVec(release.X + SkyfallRules.OffsetOf(marks[0]), release.Y + SkyfallRules.OffsetOf(marks[1]),
                              release.Z + SkyfallRules.OffsetOf(marks[2]));
            return true;
        }

        /// <summary>Shooter, at Shoot (before the wire pose exists): this release may be a cast. It holds Skyfall, no storm of its own
        /// is running, and the draw has lasted ArmSeconds since the 68% crossing. The wire pose decides later (Finalise).</summary>
        public static bool ShooterAsks(bool holds, bool busy, int? elapsedMs, SkyfallValues v) =>
            holds && !busy && elapsedMs.HasValue && elapsedMs.Value / 1000.0 >= v.ArmSeconds;

        /// <summary>Shooter, as the claim goes on the wire: with the release as it went on the wire armed (aimed and held), the claim
        /// becomes a request (<see cref="AsSky"/> with Skyfall and the sigil centre's marks) and true; otherwise false and the claim is
        /// left exactly as it was built.</summary>
        public static bool Finalise(ShotClaim claim, ShotPose wire, int? elapsedMs, SkyfallValues v) {
            if (claim == null || !elapsedMs.HasValue || !SkyfallRules.Armed(wire, elapsedMs.Value / 1000.0, v)) return false;
            AsSky(claim, SkyCode.Skyfall, MarksOf(wire.Pos, SkyfallRules.SigilCentre(wire.Pos, wire.Dir, v)));
            return true;
        }

        /// <summary>In place, and returns <paramref name="c"/>: an unsteered Early claim at stage 0 with no targets or picks (so its
        /// code rides the window bits), carrying this code and a copy of these marks. Its time is left alone.</summary>
        public static ShotClaim AsSky(ShotClaim c, SkyCode code, IList<uint> marks) {
            c.Kind = ShotKind.Early;
            c.Stage = 0;
            c.Steered = false;
            c.Locks = 0;
            c.Targets.Clear();
            c.Picks.Clear();
            c.Sky = code;
            c.Marks = marks != null ? new List<uint>(marks) : new List<uint>();
            return c;
        }

        /// <summary>Server: its verdict on a release. The first failing check names the reason: no request (a plain shot, no marks),
        /// not granted, the caster's storm still running, no wire pose, not aimed, not armed (ServerAccepts: both the claimed time and
        /// the time it decides with), no centre (not exactly three marks), the shooter's centre more than CentreSlackMetres from the
        /// server's own over its own wire pose. Otherwise Cast: Skyfall, or Festival in festival mode, with the shooter's marks. Every
        /// refusal is Fizzled (SkyfallRules.Relay), relaying the shooter's centre when it sent one, else its own, else none.</summary>
        public static SkyVerdict Server(bool requested, bool granted, bool busy, ShotPose? pose, int? claimedMs, int? effectiveMs,
                                        IList<uint>? marks, bool festival, SkyfallValues v) {
            if (!requested) return new SkyVerdict { Code = SkyfallRules.Relay(SkyCode.None, false, festival), Why = SkyWhy.NotRequested };
            HVec own = pose.HasValue ? SkyfallRules.SigilCentre(pose.Value.Pos, pose.Value.Dir, v) : Unknown;
            SkyWhy why;
            if (!granted) why = SkyWhy.NotGranted;
            else if (busy) why = SkyWhy.Busy;
            else if (!pose.HasValue) why = SkyWhy.NoPose;
            else if (!SkyfallRules.Aimed(pose.Value, v)) why = SkyWhy.NotAimed;
            else if (!SkyfallRules.ServerAccepts(granted, busy, pose.Value, claimedMs, effectiveMs, v)) why = SkyWhy.NotArmed;
            else if (!TryCentre(pose.Value.Pos, marks, out HVec sc)) why = SkyWhy.NoCentre;
            else if (!((sc - own).Length <= CentreSlackMetres)) why = SkyWhy.CentreFar;   // NaN counts as far
            else return new SkyVerdict { Code = SkyfallRules.Relay(SkyCode.Skyfall, true, festival), Why = SkyWhy.Cast, Marks = new List<uint>(marks!) };
            var fizzle = new SkyVerdict { Code = SkyfallRules.Relay(SkyCode.Skyfall, false, festival), Why = why };
            if (marks != null && marks.Count == CentreMarks) fizzle.Marks = new List<uint>(marks);
            else if (pose.HasValue) fizzle.Marks = MarksOf(pose.Value.Pos, own);
            return fizzle;
        }

        /// <summary>Server: the claim it relays for a Skyfall release: a copy of the claim it checked (a new one when the shooter sent
        /// none) as <see cref="AsSky"/> with the verdict's code and marks, at the time it decided with. The input is never changed.</summary>
        public static ShotClaim Relay(ShotClaim? checkedClaim, SkyVerdict verdict, int? effectiveMs) {
            ShotClaim c = AsSky(checkedClaim?.Copy() ?? new ShotClaim(), verdict.Code, verdict.Marks);
            c.TimeMs = effectiveMs;
            return c;
        }

        /// <summary>Spectator: what a relay says. None (centre Unknown) for no relay or no code; otherwise the centre over this
        /// machine's own release point, and Fizzled when the relay fizzled or carries no centre (or there is no release point), else
        /// the relayed code. A fizzle keeps its centre when it has one (the puff).</summary>
        public static SkyCode Seen(ShotClaim? relay, HVec release, out HVec centre) {
            centre = Unknown;
            if (relay == null || relay.Sky == SkyCode.None) return SkyCode.None;
            bool has = TryCentre(release, relay.Marks, out centre);
            return relay.Sky == SkyCode.Fizzled || !has ? SkyCode.Fizzled : relay.Sky;
        }

        /// <summary>One storm as this machine plans it: the pattern over the centre and the release point (the same release point the
        /// centre was built from, so every machine's impacts agree), only for Skyfall or Festival with both finite; otherwise no
        /// impact. The impacts are planned once here: a running storm keeps them whatever the settings do later.</summary>
        public static SkyStorm Storm(uint arrowId, SkyCode code, HVec release, HVec centre, int casterAccount, float startTime, SkyfallValues v) {
            bool plans = (code == SkyCode.Skyfall || code == SkyCode.Festival) && centre.Finite && release.Finite;
            return new SkyStorm {
                ArrowId = arrowId, Code = code, Release = release, CX = centre.X, CY = centre.Y, CZ = centre.Z, StartTime = startTime,
                CasterAccount = casterAccount,
                Impacts = plans ? SkyfallPattern.Plan(arrowId, centre.X, centre.Z, release.X, release.Z, v) : new SkyImpact[0],
            };
        }

        /// <summary>The one-storm-per-caster rule: until when a storm started at <paramref name="startTime"/> keeps its caster busy.
        /// The server lets go BusySlackSeconds before the storm ends, the caster's game BusySlackSeconds after, so a sky shot the
        /// caster's game asks for is never one the server still refuses as busy. Clamps (EndsAt): once per storm, never per frame.</summary>
        public static float BusyUntil(float startTime, SkyfallValues v, bool server) =>
            startTime + SkyfallPattern.EndsAt(v) + (server ? -BusySlackSeconds : BusySlackSeconds);

        /// <summary>The same code and impacts, index for index: Index, Wave and Nail equal, X and Z within <paramref name="metres"/>,
        /// DomeAt and LandAt within 1e-6 s. (SpectatorTest, and the tests.)</summary>
        public static bool SamePattern(SkyStorm a, SkyStorm b, double metres) {
            if (a == null || b == null || a.Code != b.Code || a.Impacts.Length != b.Impacts.Length) return false;
            for (int i = 0; i < a.Impacts.Length; i++) {
                SkyImpact x = a.Impacts[i], y = b.Impacts[i];
                if (x.Index != y.Index || x.Wave != y.Wave || x.Nail != y.Nail) return false;
                if (!(Math.Abs(x.X - y.X) <= metres) || !(Math.Abs(x.Z - y.Z) <= metres)) return false;
                if (!(Math.Abs(x.DomeAt - y.DomeAt) <= 1e-6) || !(Math.Abs(x.LandAt - y.LandAt) <= 1e-6)) return false;
            }
            return true;
        }

        /// <summary>One line for the log: the code, the impact count, the centre, the first impact and the last (InvariantCulture).</summary>
        public static string Describe(SkyStorm s) {
            CultureInfo ci = CultureInfo.InvariantCulture;
            int n = s.Impacts.Length;
            string head = s.Code + " " + n.ToString(ci) + " impacts, centre " + new HVec(s.CX, s.CY, s.CZ);
            if (n == 0) return head;
            SkyImpact f = s.Impacts[0], l = s.Impacts[n - 1];
            return head + string.Format(ci, ", first ({0:0.00}, {1:0.00}) at {2:0.00} s, last ({3:0.00}, {4:0.00}){5}",
                                        f.X, f.Z, f.LandAt, l.X, l.Z, l.Nail ? " nail" : "");
        }
    }

    /// <summary>The server's send queue for the quiet signal (SkyfallLink): at most one entry per account, in the order first wanted.
    /// An entry only says "this account's game must be told"; the text is built when it is sent, so it is never stale. An entry that has
    /// waited more than <see cref="GiveUpSeconds"/> is <see cref="Expired"/> (the caller warns once) but is never dropped while its player
    /// is online.</summary>
    internal sealed class SkyfallOutbox {
        public const float GiveUpSeconds = 60f;   // as CustomIngots' ServerConfigSync

        readonly List<int> order = new List<int>();
        readonly Dictionary<int, float> since = new Dictionary<int, float>();

        /// <summary>The accounts waiting, in the order first wanted (the live list: copy it before calling Done while walking it).</summary>
        public IReadOnlyList<int> Waiting => order;

        /// <summary>Account 0 is ignored. A new account goes to the back; one already waiting keeps its place, and its give-up clock
        /// restarts at <paramref name="now"/>.</summary>
        public void Want(int account, float now) {
            if (account == 0) return;
            if (!since.ContainsKey(account)) order.Add(account);
            since[account] = now;
        }

        public void Done(int account) { if (since.Remove(account)) order.Remove(account); }

        /// <summary>Waiting, and for more than GiveUpSeconds since it was last wanted.</summary>
        public bool Expired(int account, float now) => since.TryGetValue(account, out float t) && now - t > GiveUpSeconds;
    }

    /// <summary>The server's rejoin poll: which accounts are new since the last poll, from the online players as (account, the
    /// <c>Player</c> entity's network id). A new entity id for an account is a rejoin.</summary>
    internal sealed class SkyfallSeen {
        Dictionary<int, uint> last = new Dictionary<int, uint>(), next = new Dictionary<int, uint>();

        /// <summary>In the given order, each account that is unseen, seen with another id, or back after a poll without it. Account 0 is
        /// never returned; an account listed twice counts once, with its first id. Then exactly this call's list is remembered (anyone
        /// absent is forgotten).</summary>
        public List<int> Changed(IEnumerable<KeyValuePair<int, uint>> online) {
            var changed = new List<int>();
            next.Clear();
            if (online != null)
                foreach (KeyValuePair<int, uint> kv in online) {
                    if (kv.Key == 0 || next.ContainsKey(kv.Key)) continue;
                    next[kv.Key] = kv.Value;
                    if (!last.TryGetValue(kv.Key, out uint id) || id != kv.Value) changed.Add(kv.Key);
                }
            Dictionary<int, uint> t = last; last = next; next = t;
            return changed;
        }
    }

    /// <summary>The <c>skyfall</c> console module's words and its take rule (SkyfallCommands reads the game; this only decides the text).
    /// Every reply is pinned by the tests.</summary>
    internal static class SkyfallConsole {
        /// <summary>The grant file this build reads and writes, and the one 0.17.16 to 0.18.0 read and wrote. A build that changes what
        /// a grant does must change this name (final review I-1): 0.17.16 and 0.17.17 hold Skyfall's kills but none of its looks, so a
        /// rollback to them that read a later build's grants would make invisible kill zones. Rolling back below this build drops every
        /// grant, by design; SkyfallGrantStore carries a grant file of the old name over once and moves it aside.</summary>
        public const string GrantFile = "skyfall-grants-v2.txt", OldGrantFile = "skyfall-grants.txt";
        public const string
            GiveUsage = "Use: skyfall give <player>, or skyfall give all (players online now).",
            TakeUsage = "Use: skyfall take <player> (offline holders too; skyfall lists them).";

        /// <summary>The bare <c>skyfall</c>: the holders by name (with account, online or offline).</summary>
        public static string Status(IReadOnlyList<KeyValuePair<int, string>> grants, ICollection<int> online) {
            if (grants == null || grants.Count == 0) return "Skyfall: nobody holds it. " + GiveUsage;
            var sorted = new List<KeyValuePair<int, string>>(grants);
            sorted.Sort((a, b) => { int c = Order(a.Value, b.Value); return c != 0 ? c : a.Key.CompareTo(b.Key); });
            var parts = new string[sorted.Count];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = sorted[i].Value + " (" + Num(sorted[i].Key) + ", " + (online != null && online.Contains(sorted[i].Key) ? "online" : "offline") + ")";
            return "Skyfall: " + Num(sorted.Count) + " holder(s): " + string.Join(", ", parts) + ".";
        }

        /// <summary><c>skyfall give &lt;player&gt;</c> and <c>give all</c> while the build is not ready (SkyfallCommands.Ready false):
        /// the refusal, and nothing is given; null when it is ready.</summary>
        public static string? NotFinished(bool ready) => ready ? null : "Skyfall is not finished in this build: nothing was given.";

        public static string Given(string name, int account, bool alreadyHeld, string? saveError) {
            if (alreadyHeld)
                return Who(name, account) + " already holds Skyfall; their game is told again." + (saveError != null ? " " + GrantFile + " could not be written (" + saveError + ")." : "");
            if (saveError != null)
                return "Skyfall given to " + Who(name, account) + ", but " + GrantFile + " could not be written (" + saveError + "): the grant lasts until the server restarts.";
            return "Skyfall given to " + Who(name, account) + ". Their game is told quietly; nothing shows on anyone's screen.";
        }

        /// <summary><c>skyfall give all</c>: the names given to and already holding, each in the order passed.</summary>
        public static string GivenAll(IList<string> given, IList<string> already, string? saveError) {
            int g = given != null ? given.Count : 0, a = already != null ? already.Count : 0;
            if (g == 0 && a == 0) return "Nobody is online. skyfall give all gives Skyfall to the players online now.";
            if (g == 0) return "Everyone online already holds Skyfall: " + string.Join(", ", already!) + ".";
            string s = "Skyfall given to " + Num(g) + " player(s): " + string.Join(", ", given!) + ".";
            if (a > 0) s += " Already held: " + string.Join(", ", already!) + ".";
            if (saveError != null) s += " But " + GrantFile + " could not be written (" + saveError + "): these grants last until the server restarts.";
            return s;
        }

        public static string NoAccount(string name) => "Not given: " + name + " has no account id yet (still joining?). Try again in a moment.";

        public static string NotGiven(string refusal) => "Not given. " + refusal;

        /// <summary>Whose grant <c>skyfall take &lt;query&gt;</c> means, comparing names itself (trimmed, any case):
        /// 1. a grant saved under that name (the lowest account if two are); 2. else an online player of that name: their account if it
        /// holds a grant, else the refusal "{name} does not hold Skyfall."; 3. else "Nobody holds Skyfall." or "Nobody called '{query}'
        /// holds Skyfall. Holders: {names}.". <paramref name="refusal"/> is "" when an account is returned.</summary>
        public static int? TakeTarget(SkyfallGrants grants, string query, IEnumerable<KeyValuePair<int, string>> online, out string refusal) {
            string q = (query ?? "").Trim();
            IReadOnlyList<KeyValuePair<int, string>> all = grants != null ? grants.All : new List<KeyValuePair<int, string>>();
            foreach (KeyValuePair<int, string> g in all)
                if (Is(g.Value, q)) { refusal = ""; return g.Key; }
            if (online != null)
                foreach (KeyValuePair<int, string> p in online) {
                    if (string.IsNullOrWhiteSpace(p.Value) || !Is(p.Value.Trim(), q)) continue;
                    foreach (KeyValuePair<int, string> g in all)
                        if (g.Key == p.Key) { refusal = ""; return g.Key; }
                    refusal = p.Value + " does not hold Skyfall.";
                    return null;
                }
            if (all.Count == 0) { refusal = "Nobody holds Skyfall."; return null; }
            var names = new List<string>(all.Count);
            foreach (KeyValuePair<int, string> g in all) names.Add(g.Value);
            names.Sort(Order);
            refusal = "Nobody called '" + q + "' holds Skyfall. Holders: " + string.Join(", ", names.ToArray()) + ".";
            return null;
        }

        public static string Taken(string name, int account, bool online, string? saveError) =>
            "Skyfall taken from " + Who(name, account) + "." + (online ? " Their game is told quietly." : " They are offline; nothing is sent.")
            + (saveError != null ? " But " + GrantFile + " could not be written (" + saveError + "): the grant comes back when the server restarts." : "");

        public static string Failed(string message) => "Skyfall: the command failed: " + message + ". See the server's MelonLoader log.";

        static string Who(string name, int account) => name + " (" + Num(account) + ")";
        static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        // names sorted in any case, ties ordinal (as SkyfallNames)
        static int Order(string a, string b) {
            int c = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }
    }

    // Filled in by SkyfallServer.Landing (Task 9) and by the tests.
    /// <summary>One body a landing may strike: a creature (<see cref="At"/> = its point nearest the landing: SkyfallCaught.Nearer over
    /// its PositionHandler point and the closest point of each of its body colliders) or a player (<see cref="At"/> = their head;
    /// <see cref="Account"/> = their account id, 0 when unknown).</summary>
    internal struct SkyBody { public HVec At; public bool Player, Alive; public int Account; }

    /// <summary>What one landing does to one body (<see cref="Body"/> = its index in the list Decide was given): a kill (normal mode,
    /// a creature or a player; the server deals SkyfallStrike.Lethal), a creature's cripple (festival mode, no damage) or a player's
    /// care (festival mode: heal and feed). No effect carries damage: nothing is hurt without being killed.</summary>
    internal struct SkyEffect { public int Body; public float Cripple; public bool Kill, Care; }

    /// <summary>The server's rules for one landing (SkyfallServer; Task 9, the kill rule Task 12c's): who it strikes, the kill's size
    /// and the order the storm's landings are queued in. The owner: "all attacks are ment to kill everything it hits". In normal mode
    /// every living creature and player inside the landing's kill zone (SkyfallCaught.InDome of <see cref="KillRadius"/>) is killed,
    /// whatever the server's PvP setting; nothing outside it is touched (no falloff). Never the caster, never a player inside the
    /// caster's safe circle (<see cref="Safe"/>), and no player at all when the caster is unknown (account 0); creatures are killed
    /// in the circle too. Festival mode (dormant: it can no longer be switched on, Task 12b) is Task 9's: creatures crippled only, at
    /// <see cref="Share"/>, everyone within FestivalCareRadius cared for, the caster too.</summary>
    internal static class SkyfallStrike {
        /// <summary>Festival's cripple share at its reach (Task 9's falloff; normal mode has none). A constant: SkyfallValues has no
        /// field for it.</summary>
        public const float EdgeShare = 0.5f;

        /// <summary>A landing's kill radius: BlastRadius for a rain arrow, NailDomeRadius for the World-Nail.</summary>
        public static double KillRadius(bool nail, SkyfallValues v) => nail ? v.NailDomeRadius : v.BlastRadius;

        /// <summary>The storm's kill area round its centre: the rain disc and a rain arrow's kill radius (RainRadius + BlastRadius,
        /// 14 m). The one size of the marks on the ground under the sigil (the ground twin and the hold's target glyph).</summary>
        public static float KillArea(SkyfallValues v) => v.RainRadius + v.BlastRadius;

        /// <summary>Festival's (dormant) cripple reach, Task 9's: the larger of the dome and the blast radius.</summary>
        public static double FestivalReach(bool nail, SkyfallValues v) =>
            nail ? Math.Max((double)v.NailDomeRadius, v.NailBlastRadius) : Math.Max((double)v.DomeRadius, v.BlastRadius);

        /// <summary>How far round a landing's ground point the server looks for creatures: the kill radius plus the dome's Slack (that
        /// sphere holds every point InDome catches), or festival's reach when that is larger.</summary>
        public static double Reach(bool nail, SkyfallValues v) => Math.Max(KillRadius(nail, v) + SkyfallCaught.Slack, FestivalReach(nail, v));

        /// <summary>Festival's cripple share at this distance from a landing: 1 inside the dome, EdgeShare at FestivalReach, 0 beyond
        /// (and for NaN).</summary>
        public static float Share(double distance, bool nail, SkyfallValues v) =>
            BlastMath.Fraction((float)distance, nail ? v.NailDomeRadius : v.DomeRadius, (float)FestivalReach(nail, v), EdgeShare);

        /// <summary>The caster's safe circle: the head within <paramref name="clearance"/> of the release point along the ground (the
        /// edge in). A broken head or release point is outside it.</summary>
        public static bool Safe(HVec head, HVec release, double clearance) {
            if (!head.Finite || !release.Finite || !(clearance >= 0)) return false;
            double dx = head.X - release.X, dz = head.Z - release.Z;
            return dx * dx + dz * dz <= clearance * clearance;
        }

        /// <summary>The kill's health damage, for a player and a creature alike: 1000 x the maximum health, held to [1e5, 1e9] (no
        /// maximum, or a broken one: 1e5). Never float.MaxValue: the server's DamageX overflows that to Infinity, and Infinity x a 0
        /// multiplier is NaN, which the game's stat clamp keeps.</summary>
        public static float Lethal(float maxHealth) {
            if (!(maxHealth > 0f)) return 1e5f;
            float l = maxHealth * 1000f;
            return l < 1e5f ? 1e5f : l > 1e9f ? 1e9f : l;
        }

        /// <summary>Clears <paramref name="into"/>, then adds at most one effect per body, in body order: nothing for a dead body. Normal
        /// mode: a Kill for a creature whose point is InDome of the kill radius, and for a player whose Feet are, unless they are the
        /// caster, inside the caster's safe circle round <paramref name="release"/> (CasterClearance), or the caster is unknown.
        /// Festival mode: a creature within FestivalReach gets Cripple (ImpactCripple at its Share, no damage), a player caught within
        /// FestivalCareRadius is cared for. A non-finite <paramref name="groundY"/> (no ground found) adds nothing.</summary>
        public static void Decide(SkyImpact impact, double groundY, bool festival, int casterAccount, HVec release, IList<SkyBody> bodies,
                                  SkyfallValues v, List<SkyEffect> into) {
            into.Clear();
            if (bodies == null || v == null || double.IsNaN(groundY) || double.IsInfinity(groundY)) return;
            var point = new HVec(impact.X, groundY, impact.Z);
            double r = KillRadius(impact.Nail, v);
            for (int i = 0; i < bodies.Count; i++) {
                SkyBody b = bodies[i];
                if (!b.Alive) continue;
                if (festival) {
                    if (!b.Player) {
                        float s = Share((b.At - point).Length, impact.Nail, v);
                        if (s > 0f) into.Add(new SkyEffect { Body = i, Cripple = v.ImpactCripple * s });
                    }
                    else if (SkyfallCaught.Inside(b.At, impact.X, groundY, impact.Z, v.FestivalCareRadius)) into.Add(new SkyEffect { Body = i, Care = true });
                    continue;
                }
                bool caught = b.Player
                    ? casterAccount != 0 && b.Account != casterAccount && !Safe(b.At, release, v.CasterClearance)
                      && SkyfallCaught.InDome(SkyfallCaught.Feet(b.At, groundY), impact.X, groundY, impact.Z, r)
                    : SkyfallCaught.InDome(b.At, impact.X, groundY, impact.Z, r);
                if (caught) into.Add(new SkyEffect { Body = i, Kill = true });
            }
        }

        /// <summary>The order a storm's landings are queued in: every World-Nail index first, then every other index in plan order. A
        /// full queue then drops rain, never the finale; each landing is still dealt at its own time.</summary>
        public static int[] QueueOrder(SkyImpact[] impacts) {
            if (impacts == null) return new int[0];
            var order = new int[impacts.Length];
            int n = 0;
            for (int i = 0; i < impacts.Length; i++) if (impacts[i].Nail) order[n++] = i;
            for (int i = 0; i < impacts.Length; i++) if (!impacts[i].Nail) order[n++] = i;
            return order;
        }
    }

    /// <summary>One hit of a ground cast (Skyfall.GroundAt fills these): its height, whether its collider belongs to a creature's or a
    /// player's body (an ImpactDetector above it whose HealthTarget is one, kinematic rigidbody or not) and whether it is a loose item
    /// (a non-kinematic rigidbody).</summary>
    internal struct GroundHit { public float Height; public bool Body, Loose; }

    /// <summary>The ground under a spot, from one ground cast's hits (Skyfall.GroundAt: the server's landings; Tasks 10-12, the
    /// clients' domes): the highest hit that is neither a body nor a loose item. A creature's colliders are solid, on the Default
    /// layer and under KINEMATIC rigidbodies (the exported Turabada: 10, its detector on the root; the Gotera's legs and weak points),
    /// so a loose-item test alone took the top of its body for the ground and lifted a dome over the players beside it.</summary>
    internal static class SkyGround {
        /// <summary>A hit counts as ground: not a body, not loose, and a finite height.</summary>
        public static bool Counts(GroundHit h) => !h.Body && !h.Loose && !float.IsNaN(h.Height) && !float.IsInfinity(h.Height);

        /// <summary>The highest of the first <paramref name="n"/> hits that counts as ground; NaN when none does.</summary>
        public static float Highest(GroundHit[] hits, int n) {
            float best = float.NaN;
            if (hits == null) return best;
            if (n > hits.Length) n = hits.Length;
            for (int i = 0; i < n; i++)
                if (Counts(hits[i]) && (float.IsNaN(best) || hits[i].Height > best)) best = hits[i].Height;
            return best;
        }
    }

    /// <summary>Looks only: the timing curves the Skyfall looks draw with (tested). Nothing here decides damage.</summary>
    internal static class SkyfallCurves {
        /// <summary>The share of the open time each band of a sigil takes to open (the bands start one after another, core first).</summary>
        public const float BandShare = 0.4f;

        /// <summary>A smoothstep: 0 for a broken time or at or before 0, 1 at or after 1, else t*t*(3-2t).</summary>
        public static float Ease(float t) {
            if (!(t > 0f)) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>A fade in and out over a life: 0 before 0, at or after the life, for no life or a broken time; otherwise the
        /// lower of the linear fade-in (1 with none) and the linear fade-out toward the end (1 with none).</summary>
        public static float Envelope(float t, float life, float fadeIn, float fadeOut) {
            if (!(life > 0f) || !(t >= 0f) || t >= life) return 0f;
            float a = fadeIn > 0f ? Math.Min(1f, t / fadeIn) : 1f;
            float b = fadeOut > 0f ? Math.Min(1f, (life - t) / fadeOut) : 1f;
            return Math.Min(a, b);
        }

        /// <summary>How open band <paramref name="band"/> of <paramref name="bands"/> is <paramref name="t"/> seconds into an unfold of
        /// <paramref name="seconds"/>: the core (band 0) starts at once, each band after it a step later, each takes BandShare of the
        /// time, and the last ends exactly at <paramref name="seconds"/>. No time: open at once (from t = 0).</summary>
        public static float Unfold(float t, int band, int bands, float seconds) {
            if (!(seconds > 0f)) return t >= 0f ? 1f : 0f;
            if (bands < 1) bands = 1;
            band = band < 0 ? 0 : band > bands - 1 ? bands - 1 : band;
            if (bands == 1) return Ease(t / seconds);
            float start = band * seconds * (1f - BandShare) / (bands - 1);
            return Ease((t - start) / (seconds * BandShare));
        }

        static readonly float[] RingShare = { 0.30f, 0.62f, 1.0f };
        static readonly float[] RingSize = { 0.55f, 0.8f, 1.0f };
        static int Ring(int ring) => ring < 0 ? 0 : ring > 2 ? 2 : ring;
        static float Progress(float p) => float.IsNaN(p) || p < 0f ? 0f : p > 1f ? 1f : p;

        /// <summary>The hold's ring <paramref name="ring"/> (0 nearest, 2 farthest; held to 0..2): how far ahead of the arrow tip it has
        /// climbed at hold <paramref name="progress"/> (held to 0..1, NaN = 0) of a climb of <paramref name="span"/> metres.</summary>
        public static float HoldRingDistance(int ring, float progress, float span) => span * RingShare[Ring(ring)] * Ease(Progress(progress));

        /// <summary>The hold's ring <paramref name="ring"/>'s size as a share of the full ring radius: its own size, from 0.6 of it at
        /// the start of the hold to all of it when armed.</summary>
        public static float HoldRingScale(int ring, float progress) => RingSize[Ring(ring)] * (0.6f + 0.4f * Ease(Progress(progress)));

        /// <summary>A dial moved from <paramref name="shown"/> toward <paramref name="want"/> by at most dt / seconds (all of the way
        /// with no time), never past it, held to 0..1. A broken shown or want counts as 0; a broken or backward step moves nothing.</summary>
        public static float Toward(float shown, float want, float dt, float seconds) {
            if (float.IsNaN(shown)) shown = 0f;
            if (float.IsNaN(want)) want = 0f;
            if (!(dt >= 0f)) return shown < 0f ? 0f : shown > 1f ? 1f : shown;
            float step = seconds > 0f ? dt / seconds : 1f;
            if (want > shown) shown = Math.Min(want, shown + step);
            else if (want < shown) shown = Math.Max(want, shown - step);
            return shown < 0f ? 0f : shown > 1f ? 1f : shown;
        }

        // ---- Task 11: the storm's rain and scorches

        /// <summary>Where a scorch's colour is FireHot, Fire, FireDeep and Char, as shares of its cooling time (SkyfallLook maps the
        /// stops to the colours).</summary>
        public static readonly float[] CoolStops = { 0f, 0.15f, 0.4f, 1f };

        /// <summary>How far a giant arrow has fallen at <paramref name="u"/> of its fall time: 0 for a broken time or at or before 0, 1
        /// at or after 1, else u * u (it speeds up into the ground).</summary>
        public static float Fall(float u) {
            if (!(u > 0f)) return 0f;
            if (u >= 1f) return 1f;
            return u * u;
        }

        /// <summary>A scorch's fire glow at <paramref name="cool"/> of its cooling: full when fresh, gone by the third stop (0.4).</summary>
        public static float Glow(float cool) => 1f - Ease(cool / 0.4f);

        /// <summary>The colour stop a scorch is past at <paramref name="cool"/> of its cooling (held to 0..1, NaN = 0): the last index
        /// i with CoolStops[i] at or below it, at most 2, and in <paramref name="blend"/> how far it is from stop i to stop i + 1.</summary>
        public static int RampStop(float cool, out float blend) {
            if (float.IsNaN(cool) || cool < 0f) cool = 0f;
            else if (cool > 1f) cool = 1f;
            int i = 0;
            while (i < CoolStops.Length - 2 && CoolStops[i + 1] <= cool) i++;
            blend = (cool - CoolStops[i]) / (CoolStops[i + 1] - CoolStops[i]);
            return i;
        }

        // ---- Task 12: the World-Nail's slide, its seven bands and their roll over the ground

        /// <summary>How far the World-Nail has slid at <paramref name="u"/> of its slide time: 0 for a broken time or at or before 0, 1
        /// at or after 1, else u * u * u (slow out of the sigil, fastest as it is driven into the ground).</summary>
        public static float Slide(float u) {
            if (!(u > 0f)) return 0f;
            if (u >= 1f) return 1f;
            return u * u * u;
        }

        static int Bands(int bands) => bands < 1 ? 1 : bands;
        static int Band(int band, int bands) => band < 0 ? 0 : band > bands - 1 ? bands - 1 : band;

        /// <summary>Band <paramref name="band"/> of <paramref name="bands"/> (0 = lowest; held to the bands, at least one): its height as a
        /// share of the way from the ground up to the sigil, evenly spaced with a gap at each end: (band + 1) / (bands + 1).</summary>
        public static float BandHeight(int band, int bands) {
            bands = Bands(bands);
            band = Band(band, bands);
            return (band + 1f) / (bands + 1f);
        }

        /// <summary>Band <paramref name="band"/> of <paramref name="bands"/> (held as <see cref="BandHeight"/>): its radius as a share of the
        /// lowest band's, from 1 at the bottom down to 0.45 at the top; a single band is full size.</summary>
        public static float BandRadius(int band, int bands) {
            bands = Bands(bands);
            band = Band(band, bands);
            return bands > 1 ? 1f - 0.55f * band / (bands - 1) : 1f;
        }

        // t / seconds held to 0..1, a broken time counting as 0 (seconds is above 0 here)
        static float Share(float t, float seconds) {
            float u = t / seconds;
            return float.IsNaN(u) || u < 0f ? 0f : u > 1f ? 1f : u;
        }

        /// <summary>How far a fallen band has rolled out over the ground <paramref name="t"/> seconds into a roll of
        /// <paramref name="seconds"/>: 1 - (1 - u)^2 (fast at first, easing to a stop), held at 1 after; no roll time rolls out at once.</summary>
        public static float RollOut(float t, float seconds) {
            if (!(seconds > 0f)) return 1f;
            float u = Share(t, seconds);
            return 1f - (1f - u) * (1f - u);
        }

        /// <summary>Which of the game's eight hand-drawn ring frames (SpriteSheet_Combat row 2, thick to broken) a rolling band shows
        /// <paramref name="t"/> seconds into a roll of <paramref name="seconds"/>: eight even steps, never past 7; a broken or early time
        /// shows 0, no roll time the broken ring (7).</summary>
        public static int RollFrame(float t, float seconds) {
            if (!(seconds > 0f)) return 7;
            return Math.Min(7, (int)(8f * Share(t, seconds)));
        }
    }

    /// <summary>Looks only (SkyfallHold, the caster's own hold; the cast itself is judged on the wire pose, strictly, and the arm thump
    /// by SkyfallRules.Armed): when the hold's looks count as aimed, and when they start over (final review I-1). The farthest storms are
    /// cast 0-2 degrees above MinElevationDegrees, where hand tremor crosses the threshold several times a second; a restart on every
    /// return of the aim blanked the three rings at the eyes and re-opened them, a 3-6 Hz strobe.</summary>
    internal static class SkyfallHoldLook {
        /// <summary>Once aimed, the looks stay aimed until the pull is this far under MinStrength or the aim this many degrees under
        /// MinElevationDegrees (both as the trigger's own slacks).</summary>
        public const float StrengthGrace = 0.03f;
        public const double AngleGraceDegrees = 2.0;

        /// <summary>The trigger's own test (SkyfallRules.Aimed), with the grace above while <paramref name="wasAimed"/>. NaN is never aimed.</summary>
        public static bool Aimed(ShotPose pose, bool wasAimed, SkyfallValues v) {
            if (SkyfallRules.Aimed(pose, v)) return true;
            if (!wasAimed) return false;
            return pose.Strength >= v.MinStrength - SkyfallRules.StrengthSlack - StrengthGrace
                   && SkyfallRules.ElevationDegrees(pose.Dir) >= v.MinElevationDegrees - SkyfallRules.AngleSlackDegrees - AngleGraceDegrees;
        }

        /// <summary>A new aimed stretch (its flare from closed, its hum, its sky dim's ramp) starts when the aim comes back and the looks
        /// are no longer <paramref name="shown"/>: a short break only dims them (their presence), it never blanks and re-opens them.</summary>
        public static bool Restarts(bool aimed, bool wasAimed, bool shown) => aimed && !wasAimed && !shown;
    }

    /// <summary>Looks only, one per machine (SkyfallLook.Bright, the one gate every Skyfall look's bright moment goes through: an
    /// explosion moment, an Fx.Flash light, the World-Nail's finale and its appear flash): the owner's VR rule "no more than 3 bright
    /// flashes a second", across every storm (final review I-3). A bright moment within <see cref="MergeSeconds"/> after the last one
    /// taken is that same moment (a wave's explosions, started a frame or two apart). A new moment is refused when
    /// <see cref="MaxPerWindow"/> moments started in the <see cref="WindowSeconds"/> before it; the caller then shows it dimmed (sparks
    /// and smoke: no light, no explosion).</summary>
    internal sealed class SkyfallFlashGate {
        public const int MaxPerWindow = 3;
        public const float WindowSeconds = 1f, MergeSeconds = 0.15f;

        readonly float[] starts = new float[MaxPerWindow];
        int next;
        float last = float.NegativeInfinity;

        public SkyfallFlashGate() { for (int i = 0; i < starts.Length; i++) starts[i] = float.NegativeInfinity; }

        /// <summary>A bright moment now would be shown (nothing is recorded). Never for a broken time.</summary>
        public bool Allows(float now) {
            if (float.IsNaN(now)) return false;
            float since = now - last;
            if (since >= 0f && since <= MergeSeconds) return true;
            int n = 0;
            for (int i = 0; i < starts.Length; i++) if (now - starts[i] < WindowSeconds) n++;
            return n < MaxPerWindow;
        }

        /// <summary><see cref="Allows"/>, and a new moment recorded when it starts one.</summary>
        public bool TryTake(float now) {
            if (!Allows(now)) return false;
            float since = now - last;
            if (since >= 0f && since <= MergeSeconds) return true;   // the same moment
            starts[next] = now;
            next = (next + 1) % starts.Length;
            last = now;
            return true;
        }
    }

    /// <summary>Looks only: what a look starts at most a few a frame, in the order queued (final review I-2: a wave's explosions and
    /// scorches; the game makes a new 'Explosion' when its pool is empty, an object of eleven parts and eight particle systems). One
    /// waiting longer than <see cref="MaxWaitSeconds"/> (a hitch) is dropped, never shown late. At most <see cref="Cap"/> wait.</summary>
    internal sealed class SkyfallPacer<T> {
        public const int Cap = 256;
        public const float MaxWaitSeconds = 0.5f;

        readonly Queue<KeyValuePair<T, float>> waiting = new Queue<KeyValuePair<T, float>>();

        public int Count => waiting.Count;

        /// <summary>Queued at <paramref name="now"/>; false when Cap are waiting.</summary>
        public bool Add(T item, float now) {
            if (waiting.Count >= Cap) return false;
            waiting.Enqueue(new KeyValuePair<T, float>(item, now));
            return true;
        }

        /// <summary>At most <paramref name="perFrame"/> items into <paramref name="into"/>, oldest first; the ones waiting longer than
        /// MaxWaitSeconds are dropped on the way (a broken time drops none). Returns how many were taken.</summary>
        public int Take(float now, int perFrame, List<T> into) {
            int n = 0;
            while (n < perFrame && waiting.Count > 0) {
                KeyValuePair<T, float> e = waiting.Dequeue();
                if (now - e.Value > MaxWaitSeconds) continue;
                into.Add(e.Key);
                n++;
            }
            return n;
        }

        public void Clear() => waiting.Clear();
    }

    /// <summary>Looks only: the clients' pool sizes and what the idle warm-up builds ahead (SkyfallSigil, SkyfallLook, SkyfallNail). The
    /// pools hold <see cref="Storms"/> storms at once (final review I-4: with `skyfall give all` every holder can cast; a World-Nail
    /// lived about 23.5 s from its slide to its last crumble with the first 20 s stand (9.5 s since 0.18.1's 6 s), and a pool of 2 left
    /// the third storm's finale without its Nail). The warm-up builds what one storm and its caster's hold take in their first frames
    /// (final review I-2), one item per idle frame (loose pieces <see cref="LoosePerFrame"/> at a time), so no first storm builds a
    /// hundred objects in the frame it shows them.</summary>
    internal static class SkyfallPools {
        public const int Storms = 6;
        public const int NailBands = 7;                                     // the World-Nail's bands, per storm
        public const int Nails = Storms, Domes = Storms, NailBandSigils = Storms * NailBands;
        public const int SkySigils = 6, GroundSigils = 6, HoldSigils = 8, Loose = 400, GiantArrows = 64;
        // a caster's hold keeps its sky sigil and ground glyph while they fade, so its storm needs a second of each
        public const int WarmSky = 2, WarmGround = 2, WarmHold = 3, WarmNailBands = NailBands, WarmArrows = 9, WarmNails = 1, WarmDomes = 1;
        // a storm's own loose pieces: 24 rain scorches of 5 (alive 14 s), the Nail's scorch, 14 rolling rings, the sky sigil's 20 runes
        // and 2 rim rings
        public const int WarmLoose = 170, LoosePerFrame = 10;
    }
}
