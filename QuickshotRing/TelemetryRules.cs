using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace QuickshotRing {
    // Balance telemetry, the Unity-free part (tested in TelemetryTests.cs): the shot and hit rows and their CSV, the rule that names a
    // hit's source, the split of a batched spectral hit, the index from an arrow to its shot, and the scope the mod's own damage sites
    // open so the hit hook (Telemetry.cs) knows who dealt the damage. The owner (2026-11-19): "Have it keep logs of all arrows shot,
    // skills used and damage delt, so after a day of playtest we can take all that information and graph it".

    /// <summary>What dealt a hit. Ghost, Pellet, Needle and Core are the spectral arrows of a volley (Core: Seeking Volley's core
    /// cluster); Mod is a hand-made hit with no scope, a marker for a damage site that forgot to open one.</summary>
    internal enum DealtBy : byte { Arrow, Ghost, Pellet, Needle, Core, Line, Ricochet, Shatter, HolyBeam, HolyBlast, Weight, Skyfall, Mod }

    /// <summary>What took a hit: a creature (a combatant), a player, or any other thing with health.</summary>
    internal enum Victim : byte { Creature, Player, Thing }

    /// <summary>One bow release as the server decided it.</summary>
    internal struct ShotRow {
        public long Utc;
        public int Shot;
        public uint Arrow;
        public string Player;
        public int Account;
        public ShotKind Kind;
        public UltimateUse Ult;
        public int Ghosts, Pellets, Needles, Tier, Hold, Ms, Locks;
        public SkyCode Sky;
        public Build Build;
        public bool Test;
        /// <summary>The mod version and the shared-settings checksum (Cfg.SharedChecksum): the tuning a shot was made under, so the report
        /// never mixes two tunings (a public server's days).</summary>
        public string Version;
        public uint Settings;

        /// <summary><paramref name="ms"/> null (unknown) is written as an empty field; the plan's echo slot carries a Quick main's needles.</summary>
        public static ShotRow Of(long utc, int shot, uint arrow, string player, int account, Resolution r, int? ms, int locks, SkyCode sky, Build build, bool test,
                                 string version, uint settings) =>
            new ShotRow {
                Utc = utc, Shot = shot, Arrow = arrow, Player = player, Account = account, Kind = r.Kind, Ult = r.Ultimate,
                Ghosts = r.Plan.Ghosts, Pellets = r.Plan.Pellets, Needles = r.Plan.Echoes, Tier = r.ChargedTier, Hold = r.HoldStage,
                Ms = ms ?? -1, Locks = locks, Sky = sky, Build = build, Test = test, Version = version, Settings = settings,
            };
    }

    /// <summary>One hit of archery damage: what the target's health really lost (dealt), what was asked (raw), its cripple, a kill.</summary>
    internal struct HitRow {
        public long Utc;
        public int Shot;
        public string Player;
        public DealtBy Source;
        public string Target;
        public uint TargetId;
        public Victim Victim;
        public float Raw, Dealt, Cripple;
        public bool Killed;
        public float MaxHealth;
    }

    /// <summary>The two files' rows. Read by tools/telemetry_report.py by column name; a change of columns changes the "v1" in the file names.</summary>
    internal static class TelemetryCsv {
        internal const string ShotHeader =
            "time,session,shot,arrow,player,account,kind,ult,ghosts,pellets,needles,tier,hold,ms,locks,sky,main,quick,volley,charge,ults,ricochet,shatter,gatherer,test,version,settings";
        internal const string HitHeader = "time,session,shot,player,source,target,target_id,victim,raw,dealt,cripple,killed,max_health";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>"hits-v1-2026-11-19.csv": one file of each kind per server-local day.</summary>
        internal static string FileName(string kind, DateTime localDay) => kind + "-v1-" + localDay.ToString("yyyy-MM-dd", Inv) + ".csv";

        internal static DateTime LocalDay(long utc) => new DateTime(utc, DateTimeKind.Utc).ToLocalTime().Date;

        internal static void Shot(StringBuilder sb, in ShotRow r, string session) {
            Time(sb, r.Utc); sb.Append(',').Append(session);
            Int(sb, r.Shot); Int(sb, r.Arrow); sb.Append(','); Field(sb, r.Player); Int(sb, r.Account);
            sb.Append(',').Append(r.Kind.ToString()).Append(',').Append(r.Ult.ToString());
            Int(sb, r.Ghosts); Int(sb, r.Pellets); Int(sb, r.Needles); Int(sb, r.Tier); Int(sb, r.Hold);
            sb.Append(','); if (r.Ms >= 0) sb.Append(r.Ms.ToString(Inv));
            Int(sb, r.Locks); sb.Append(',').Append(r.Sky.ToString());
            Build b = r.Build;
            sb.Append(',').Append(b.Main.ToString()); Int(sb, b.Quick); Int(sb, b.Volley); Int(sb, b.Charge);
            sb.Append(','); Ults(sb, b.Ultimates);
            Bit(sb, (b.Sides & SideOrbs.Ricochet) != 0); Bit(sb, (b.Bridges & Bridges.Shatter) != 0); Bit(sb, b.QuiverGather); Bit(sb, r.Test);
            sb.Append(','); Field(sb, r.Version); sb.Append(',').Append(r.Settings.ToString("x8", Inv));
            sb.Append('\n');
        }

        internal static void Hit(StringBuilder sb, in HitRow r, string session) {
            Time(sb, r.Utc); sb.Append(',').Append(session);
            Int(sb, r.Shot); sb.Append(','); Field(sb, r.Player); sb.Append(',').Append(Name(r.Source));
            sb.Append(','); Field(sb, r.Target); Int(sb, r.TargetId); sb.Append(',').Append(Name(r.Victim));
            Num(sb, r.Raw); Num(sb, r.Dealt); Num(sb, r.Cripple); Bit(sb, r.Killed); Num(sb, r.MaxHealth);
            sb.Append('\n');
        }

        internal static string Name(DealtBy s) => s switch {
            DealtBy.Arrow => "arrow", DealtBy.Ghost => "ghost", DealtBy.Pellet => "pellet", DealtBy.Needle => "needle",
            DealtBy.Core => "core", DealtBy.Line => "line", DealtBy.Ricochet => "ricochet", DealtBy.Shatter => "shatter",
            DealtBy.HolyBeam => "holybeam", DealtBy.HolyBlast => "holyblast", DealtBy.Weight => "weight", DealtBy.Skyfall => "skyfall",
            _ => "mod",
        };

        static string Name(Victim v) => v == Victim.Player ? "player" : v == Victim.Creature ? "creature" : "thing";

        static void Time(StringBuilder sb, long utc) =>
            sb.Append(new DateTime(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", Inv));

        static void Int(StringBuilder sb, long v) => sb.Append(',').Append(v.ToString(Inv));

        static void Bit(StringBuilder sb, bool v) => sb.Append(v ? ",1" : ",0");

        // ponytail: a broken number (NaN, infinity) is written as 0, so a sum in the report never turns into NaN
        static void Num(StringBuilder sb, float v) => sb.Append(',').Append(float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("0.###", Inv));

        static void Ults(StringBuilder sb, Ultimates u) {
            if (u == Ultimates.None) { sb.Append('-'); return; }
            bool any = false;
            void Add(Ultimates flag, string name) { if ((u & flag) == 0) return; if (any) sb.Append('+'); sb.Append(name); any = true; }
            Add(Ultimates.Quickdraw4, "Q4"); Add(Ultimates.SeekingVolley, "Seek"); Add(Ultimates.HolyArrow, "Holy");
        }

        /// <summary>A text field: quoted, with its quotes doubled, when it holds a comma, a quote or a line break (player names are free text).</summary>
        static void Field(StringBuilder sb, string? s) {
            if (string.IsNullOrEmpty(s)) return;
            if (s!.IndexOfAny(Special) < 0) { sb.Append(s); return; }
            sb.Append('"').Append(s.Replace("\"", "\"\"")).Append('"');
        }
        static readonly char[] Special = { ',', '"', '\n', '\r' };
    }

    internal static class TelemetryRules {
        /// <summary>The source of a spectral arrow's hit: a core arrow whatever its kind, a pellet, a needle (the Echo kind carries a Quick
        /// main's needles), else a ghost (inner, outer, and a Charge main's heavy arrows, which fly in the ghosts' slots).</summary>
        internal static DealtBy SourceOf(SpectralKind kind, bool core) =>
            core ? DealtBy.Core : kind == SpectralKind.Pellet ? DealtBy.Pellet : kind == SpectralKind.Echo ? DealtBy.Needle : DealtBy.Ghost;

        /// <summary>The hit hook's decision, first match wins: an open scope names it; a tool on an arrow is the real arrow (the game's own
        /// hit) unless the mod is dealing a hand-made hit with no scope (Mod); a tool on a known Holy beam is the beam; any other tool is
        /// not archery; a toolless hit whose wielder is a known beam is the Holy blast; a hand-made toolless hit with no scope is Mod; the
        /// rest (bites, falls) is not archery. False = not recorded.</summary>
        internal static bool TryDecide(bool scoped, DealtBy scope, bool dealing, bool hasTool, bool toolOnArrow, bool knownBeam, out DealtBy source) {
            source = scope;
            if (scoped) return true;
            if (hasTool) {
                if (toolOnArrow) { source = dealing ? DealtBy.Mod : DealtBy.Arrow; return true; }
                if (knownBeam) { source = DealtBy.HolyBeam; return true; }
                return false;
            }
            if (knownBeam) { source = DealtBy.HolyBlast; return true; }
            if (dealing) { source = DealtBy.Mod; return true; }
            return false;
        }

        /// <summary>Health the hit really took: never more than was there, never negative, 0 for anything not a number.</summary>
        internal static float Dealt(float before, float after) {
            if (!(before > 0f) || float.IsNaN(after)) return 0f;
            float d = before - Math.Max(after, 0f);
            return d > 0f ? d : 0f;
        }

        /// <summary>The hit took the last of the target's health.</summary>
        internal static bool Killed(float before, float after) => before > 0f && !float.IsNaN(after) && !(after > 0f);

        /// <summary>A Unity object's name without its "(Clone)"; "?" for none.</summary>
        internal static string CleanName(string? name) {
            if (string.IsNullOrEmpty(name)) return "?";
            string n = name!.Trim();
            while (n.EndsWith("(Clone)", StringComparison.Ordinal)) n = n.Substring(0, n.Length - "(Clone)".Length).TrimEnd();
            return n.Length > 0 ? n : "?";
        }
    }

    /// <summary>How the shares of one batched spectral hit (Hits.FlushSpectral: one game hit per target per tick per release) were made
    /// up, so the hook can split the damage back to each kind in proportion. Anything but a pellet, needle or core counts as a ghost.</summary>
    internal struct SpectralMix {
        public float Ghost, Pellet, Needle, Core;

        public void Add(DealtBy kind, float share) {
            if (!(share > 0f) || float.IsInfinity(share)) return;
            switch (kind) {
                case DealtBy.Pellet: Pellet += share; break;
                case DealtBy.Needle: Needle += share; break;
                case DealtBy.Core: Core += share; break;
                default: Ghost += share; break;
            }
        }

        public float Total => Ghost + Pellet + Needle + Core;

        public float Of(DealtBy kind) => kind switch {
            DealtBy.Ghost => Ghost, DealtBy.Pellet => Pellet, DealtBy.Needle => Needle, DealtBy.Core => Core, _ => 0f,
        };

        /// <summary>The kind with the largest share (ties in the order ghost, pellet, needle, core): the row a kill is written on, so a
        /// split hit counts its kill once.</summary>
        public DealtBy Largest {
            get {
                DealtBy best = DealtBy.Ghost; float v = Ghost;
                if (Pellet > v) { best = DealtBy.Pellet; v = Pellet; }
                if (Needle > v) { best = DealtBy.Needle; v = Needle; }
                if (Core > v) best = DealtBy.Core;
                return best;
            }
        }
    }

    internal readonly struct ShotRef {
        public readonly int Shot;
        public readonly string Player;
        public readonly int Account;
        public readonly double At;
        public ShotRef(int shot, string player, int account, double at) { Shot = shot; Player = player; Account = account; At = at; }
    }

    /// <summary>Server: from an arrow (or a Holy beam, or a shooter) to the shot it belongs to. Arrow ids are reused by the game, so an
    /// entry is trusted only within a window: a real arrow's own hit within RealArrowSeconds of its release, anything dealt later for
    /// the shot (Skyfall's storm, Ricochet's bounces, a homing chain) within LateSeconds; a reused id is the newest shot.</summary>
    internal sealed class ShotIndex {
        internal const double RealArrowSeconds = 15, LateSeconds = 120, PlayerSeconds = 15;
        const int Cap = 4096, BeamCap = 64;

        readonly Dictionary<uint, ShotRef> arrows = new Dictionary<uint, ShotRef>();
        readonly Dictionary<int, ShotRef> players = new Dictionary<int, ShotRef>();
        readonly Dictionary<uint, uint> beams = new Dictionary<uint, uint>();
        readonly List<uint> stale = new List<uint>();
        double nextPrune = double.MinValue;
        int last;

        /// <summary>The session's next shot number (1, 2, ...).</summary>
        internal int Next() => ++last;

        internal void Put(uint arrow, int account, string player, int shot, double now) {
            var r = new ShotRef(shot, player, account, now);
            if (arrow != 0u) {
                // past the cap, entries older than LateSeconds go (no lookup accepts them); at most one scan a second, so a burst of young
                // entries just grows the table until they age out
                if (arrows.Count >= Cap && now >= nextPrune && !arrows.ContainsKey(arrow)) { Prune(now); nextPrune = now + 1.0; }
                arrows[arrow] = r;
            }
            if (account != 0) players[account] = r; // ponytail: one entry per account that ever shot, never pruned (a server's players)
        }

        void Prune(double now) {
            stale.Clear();
            foreach (KeyValuePair<uint, ShotRef> kv in arrows) if (now - kv.Value.At > LateSeconds) stale.Add(kv.Key);
            foreach (uint k in stale) arrows.Remove(k);
            stale.Clear();
        }

        internal bool TryArrow(uint arrow, double now, double maxAge, out ShotRef s) {
            s = default;
            return arrow != 0u && arrows.TryGetValue(arrow, out s) && now - s.At <= maxAge;
        }

        internal bool TryPlayer(int account, double now, out ShotRef s) {
            s = default;
            return account != 0 && players.TryGetValue(account, out s) && now - s.At <= PlayerSeconds;
        }

        /// <summary>A Holy beam (made when the skill arms, before the release) and the arrow it sacrifices; its shot is looked up when it hits.</summary>
        internal void Beam(uint beam, uint arrow) {
            if (beam == 0u) return;
            if (beams.Count >= BeamCap && !beams.ContainsKey(beam)) beams.Clear(); // ponytail: beams live seconds; a full table is old ones
            beams[beam] = arrow;
        }

        internal bool IsBeam(uint beam) => beam != 0u && beams.ContainsKey(beam);

        /// <summary>Any Holy beam is known (until one is, the hit hook does not look for one).</summary>
        internal bool HasBeams => beams.Count > 0;

        internal bool TryBeam(uint beam, double now, out ShotRef s) {
            s = default;
            return beam != 0u && beams.TryGetValue(beam, out uint arrow) && TryArrow(arrow, now, LateSeconds, out s);
        }
    }

    /// <summary>The scope a damage site opens around the hits it deals (<c>using var _tel = HitScope.As(DealtBy.Shatter, arrowId);</c>),
    /// read by the hit hook. Scopes nest: the innermost names the hit, and closing it gives back the one outside. Main thread only.</summary>
    internal readonly struct HitScope : IDisposable {
        internal static bool Open;
        internal static DealtBy Source;
        internal static uint Arrow;
        internal static SpectralMix Mix;

        readonly bool active, wasOpen;
        readonly DealtBy wasSource;
        readonly uint wasArrow;
        readonly SpectralMix wasMix;

        HitScope(bool _) { active = true; wasOpen = Open; wasSource = Source; wasArrow = Arrow; wasMix = Mix; }

        internal static HitScope As(DealtBy source, uint arrow = 0u) {
            var s = new HitScope(true);
            Open = true; Source = source; Arrow = arrow; Mix = default;
            return s;
        }

        /// <summary>A batched spectral hit of <paramref name="release"/>: the hook splits it by <paramref name="mix"/>.</summary>
        internal static HitScope Spectral(uint release, in SpectralMix mix) {
            var s = new HitScope(true);
            Open = true; Source = DealtBy.Ghost; Arrow = release; Mix = mix;
            return s;
        }

        public void Dispose() {
            if (!active) return;
            Open = wasOpen; Source = wasSource; Arrow = wasArrow; Mix = wasMix;
        }
    }

    /// <summary>The day files and the hand-over to one background worker (the owner's condition: "make sure it dosent cause preformance
    /// issues"). The game's thread only appends rows to a list (Add) and every few seconds swaps the lists for the worker's empty ones
    /// (Flush); the worker formats and appends, so the game never waits on the disk. While a write runs the next hand-over is refused and
    /// the rows wait. Text that cannot be appended (a file held open by OneDrive or Excel) is kept and tried again on every later write; a
    /// file that reaches maxFileBytes stops growing; at most maxRows wait. Nothing here throws out of Add, Flush or the worker.</summary>
    internal sealed class TelemetryLog {
        /// <summary>Tests only: the worker waits this long before it writes.</summary>
        internal static int TestDelayMs { get; set; }

        const int MaxCarryChars = 32 << 20; // about 64 MB of text kept for files that cannot be written yet

        sealed class Carry { public readonly StringBuilder Text = new StringBuilder(); public int Rows; }

        static readonly UTF8Encoding Bom = new UTF8Encoding(true), NoBom = new UTF8Encoding(false);

        readonly string folder, session;
        readonly long maxFileBytes;
        readonly int maxRows;
        List<ShotRow> shots = new List<ShotRow>(), outShots = new List<ShotRow>();
        List<HitRow> hits = new List<HitRow>(), outHits = new List<HitRow>();
        // the writer's own: touched only while `writing` is set (by the worker, or by a synchronous Flush on the caller's thread)
        readonly Dictionary<string, Carry> carry = new Dictionary<string, Carry>();
        readonly HashSet<string> full = new HashSet<string>();
        readonly List<string> paths = new List<string>();
        readonly StringBuilder chunk = new StringBuilder(1 << 16);
        int carryChars;
        volatile bool writing;
        volatile int carried;
        long dropped;
        string? problem;

        internal TelemetryLog(string folder, string session, long maxFileBytes = 256L << 20, int maxRows = 100000) {
            this.folder = folder; this.session = session; this.maxFileBytes = maxFileBytes; this.maxRows = maxRows;
        }

        internal int Pending => shots.Count + hits.Count;
        internal long Dropped => Interlocked.Read(ref dropped);
        internal bool Writing => writing;

        internal void Add(in ShotRow r) { if (Pending >= maxRows) Interlocked.Increment(ref dropped); else shots.Add(r); }
        internal void Add(in HitRow r) { if (Pending >= maxRows) Interlocked.Increment(ref dropped); else hits.Add(r); }

        /// <summary>Hands the waiting rows over: to the worker (<paramref name="background"/>), or written here (tests, quitting). False when
        /// a write is still running: the rows wait for the next call.</summary>
        internal bool Flush(bool background) {
            if (writing) return false;
            if (shots.Count == 0 && hits.Count == 0 && carried == 0) return true;
            List<ShotRow> s = shots; shots = outShots; outShots = s;
            List<HitRow> h = hits; hits = outHits; outHits = h;
            writing = true;
            if (background) {
                try { ThreadPool.QueueUserWorkItem(_ => Write()); return true; }
                catch (Exception e) { Say("the background writer could not start (" + e.Message + "); writing here"); }
            }
            Write();
            return true;
        }

        /// <summary>Waits up to <paramref name="milliseconds"/> for a running write; true when none runs.</summary>
        internal bool WaitIdle(int milliseconds) {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (writing && clock.ElapsedMilliseconds < milliseconds) Thread.Sleep(5);
            return !writing;
        }

        /// <summary>The newest problem the writer met, once (then null).</summary>
        internal string? TakeProblem() => Interlocked.Exchange(ref problem, null);

        void Say(string p) => Interlocked.Exchange(ref problem, p);

        void Write() {
            try {
                if (TestDelayMs > 0) Thread.Sleep(TestDelayMs);
                Collect(true);
                Collect(false);
            }
            catch (Exception e) { Say("formatting rows failed (" + e.Message + ")"); }
            finally { outShots.Clear(); outHits.Clear(); }
            try {
                if (carry.Count > 0) Directory.CreateDirectory(folder);
                paths.Clear(); paths.AddRange(carry.Keys);
                foreach (string path in paths) Emit(path);
            }
            catch (Exception e) { Say("writing failed (" + e.Message + "); the rows are kept for the next try"); }
            finally { carried = carry.Count; writing = false; }
        }

        // Formats the handed-over rows of one kind into text per file (one file per local day; rows are in time order, so a day's rows
        // are one run) and adds it to what that file still has to take.
        void Collect(bool shotRows) {
            int n = shotRows ? outShots.Count : outHits.Count;
            string? path = null;
            DateTime day = default;
            int rows = 0;
            chunk.Clear();
            for (int i = 0; i < n; i++) {
                long utc = shotRows ? outShots[i].Utc : outHits[i].Utc;
                DateTime d = TelemetryCsv.LocalDay(utc);
                if (path == null || d != day) {
                    if (path != null) Keep(path, rows);
                    day = d; rows = 0; chunk.Clear();
                    path = Path.Combine(folder, TelemetryCsv.FileName(shotRows ? "shots" : "hits", d));
                }
                if (shotRows) { ShotRow r = outShots[i]; TelemetryCsv.Shot(chunk, in r, session); }
                else { HitRow r = outHits[i]; TelemetryCsv.Hit(chunk, in r, session); }
                rows++;
            }
            if (path != null) Keep(path, rows);
        }

        void Keep(string path, int rows) {
            if (carryChars + chunk.Length > MaxCarryChars) {
                Interlocked.Add(ref dropped, rows);
                Say("too much is waiting to be written (" + (carryChars >> 19) + " MB); " + rows + " rows dropped");
                return;
            }
            if (!carry.TryGetValue(path, out Carry c)) carry[path] = c = new Carry();
            c.Text.Append(chunk);
            c.Rows += rows;
            carryChars += chunk.Length;
        }

        // A day's file that reaches maxFileBytes goes on in "<name>-2.csv", "-3", ... (a busy public server); past MaxParts the rows are dropped.
        const int MaxParts = 99;

        void Emit(string path) {
            Carry c = carry[path];
            try {
                string target = path;
                long length = File.Exists(target) ? new FileInfo(target).Length : 0L;
                for (int part = 2; length >= maxFileBytes && part <= MaxParts; part++) {
                    target = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-" + part.ToString(CultureInfo.InvariantCulture) + ".csv");
                    length = File.Exists(target) ? new FileInfo(target).Length : 0L;
                }
                if (length >= maxFileBytes) {
                    Interlocked.Add(ref dropped, c.Rows);
                    Forget(path, c);
                    if (full.Add(path)) Say(Path.GetFileName(path) + " and its " + MaxParts + " parts are full: later rows for that day are dropped");
                    return;
                }
                if (length == 0L) File.WriteAllText(target, (Path.GetFileName(path).StartsWith("shots", StringComparison.Ordinal) ? TelemetryCsv.ShotHeader : TelemetryCsv.HitHeader) + "\n", Bom);
                File.AppendAllText(target, c.Text.ToString(), NoBom);
                Forget(path, c);
            }
            catch (Exception e) { Say("writing " + Path.GetFileName(path) + " failed (" + e.Message + "); its " + c.Rows + " rows are kept for the next try"); }
        }

        void Forget(string path, Carry c) { carryChars -= c.Text.Length; carry.Remove(path); }
    }
}
