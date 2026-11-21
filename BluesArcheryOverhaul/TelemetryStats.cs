using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BluesArcheryOverhaul {
    /// <summary>Balance telemetry compiled as it happens (design docs/superpowers/specs/2026-11-20-compiled-telemetry-design.md; the owner:
    /// "It should all be compiled immediately so it stays relatively small", and it must "not actually fill the memory or RAM"). Every shot
    /// and hit the server records goes into running statistics for its day and is forgotten: one line per day, player and kind of shot
    /// (S), a tally of single shots' damage per kind of shot in steps of Step (D), damage per hour (T) and the hit counts (M). Only a shot's
    /// own damage sum waits, until CloseSeconds after its release (hits name their shot and arrive up to ShotIndex.LateSeconds late).
    ///
    /// The numbers mirror analyze in tools/telemetry_report.py line for line, which reads these files (read_stats) as it reads the row
    /// files. One file per server run per local day, rewritten whole (written beside, then moved over: a reader, or a server killed
    /// mid-write, never sees half a file). The game's thread only adds numbers (Add, Tick) and copies them (Flush); one background worker
    /// formats and writes. A write that fails (a file held open by OneDrive or Excel, a low drive) keeps the day dirty for a later flush.
    /// A day is dropped from memory once a later day exists, none of its shots is open and its last copy was written. Nothing here throws
    /// out of Add, Tick, Flush, CloseAll or the worker; a problem is kept for TakeProblem. Unity-free (compiled by the tests).</summary>
    internal sealed class TelemetryStats {
        internal const double GapCapSeconds = 15, CloseSeconds = 125, Step = 0.5;

        /// <summary>Tests only: the worker waits this long before it writes.</summary>
        internal static int TestDelayMs { get; set; }

        const string Header = "#,Blue's Archery Overhaul balance statistics,v1,gap_cap_s,15,step,0.5,session,";
        const string SHeader = "#S,day,player,account,kind,ult,ghosts,pellets,needles,tier,hold,locks,sky,main,quick,volley,charge,ults,ricochet,shatter,gatherer,test,version,settings,arrow_kind,"
                             + "shots,cost_s,damage,kills,pvp_damage,pvp_kills,held_ms,held_n,arrow_power,"
                             + "by_arrow,by_ghost,by_pellet,by_needle,by_core,by_line,by_ricochet,by_shatter,by_holybeam,by_holyblast,by_weight,by_skyfall,by_mod";
        const string DHeader = "#D,day,kind,ult,ghosts,pellets,locks,tier,sky,main,quick,test,version,settings,shots,zero,steps";
        const string THeader = "#T,day,hour,version,damage";
        const string MHeader = "#M,day,version,hits,unattributed_hits,unattributed_damage,things,mods";
        const int Sources = (int)DealtBy.Mod + 1;   // one by_ column per DealtBy, in its order
        static readonly long GapCapTicks = (long)(GapCapSeconds * TimeSpan.TicksPerSecond), CloseTicks = (long)(CloseSeconds * TimeSpan.TicksPerSecond);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly UTF8Encoding Bom = new UTF8Encoding(true);

        // ---------------------------------------------------------------- the keys: the columns written, exactly as TelemetryCsv.Shot writes them

        struct SKey : IEquatable<SKey> {
            public string Player, Version, Arrow;   // Arrow: what was shot (ShotRow.ArrowKind)
            public int Account, Ghosts, Pellets, Needles, Tier, Hold, Locks, Quick, Volley, Charge;
            public ShotKind Kind; public UltimateUse Ult; public SkyCode Sky; public MainPath Main; public Ultimates Ults;
            public bool Ricochet, Shatter, Gatherer, Test;
            public uint Settings;

            public static SKey Of(in ShotRow r) {
                Build b = r.Build;
                return new SKey {
                    Player = r.Player ?? "", Version = r.Version ?? "", Arrow = r.ArrowKind ?? "", Account = r.Account, Ghosts = r.Ghosts, Pellets = r.Pellets, Needles = r.Needles,
                    Tier = r.Tier, Hold = r.Hold, Locks = r.Locks, Quick = b.Quick, Volley = b.Volley, Charge = b.Charge,
                    Kind = r.Kind, Ult = r.Ult, Sky = r.Sky, Main = b.Main, Ults = b.Ultimates,
                    Ricochet = (b.Sides & SideOrbs.Ricochet) != 0, Shatter = (b.Bridges & Bridges.Shatter) != 0, Gatherer = b.QuiverGather, Test = r.Test,
                    Settings = r.Settings,
                };
            }

            public bool Equals(SKey o) =>
                Account == o.Account && Ghosts == o.Ghosts && Pellets == o.Pellets && Needles == o.Needles && Tier == o.Tier && Hold == o.Hold
                && Locks == o.Locks && Quick == o.Quick && Volley == o.Volley && Charge == o.Charge && Kind == o.Kind && Ult == o.Ult && Sky == o.Sky
                && Main == o.Main && Ults == o.Ults && Ricochet == o.Ricochet && Shatter == o.Shatter && Gatherer == o.Gatherer && Test == o.Test
                && Settings == o.Settings && string.Equals(Player, o.Player, StringComparison.Ordinal) && string.Equals(Version, o.Version, StringComparison.Ordinal)
                && string.Equals(Arrow, o.Arrow, StringComparison.Ordinal);

            public override bool Equals(object? o) => o is SKey k && Equals(k);

            public override int GetHashCode() {
                unchecked {
                    int h = StringComparer.Ordinal.GetHashCode(Player ?? "");
                    h = h * 31 + Account; h = h * 31 + (int)Kind; h = h * 31 + (int)Ult; h = h * 31 + Ghosts; h = h * 31 + Pellets; h = h * 31 + Needles;
                    h = h * 31 + Tier; h = h * 31 + Hold; h = h * 31 + Locks; h = h * 31 + (int)Sky; h = h * 31 + (int)Main;
                    h = h * 31 + Quick * 16 + Volley * 4 + Charge; h = h * 31 + (int)Ults;
                    h = h * 31 + (Ricochet ? 1 : 0) + (Shatter ? 2 : 0) + (Gatherer ? 4 : 0) + (Test ? 8 : 0);
                    h = h * 31 + StringComparer.Ordinal.GetHashCode(Arrow ?? "");
                    return h * 31 + (int)Settings;
                }
            }

            /// <summary>player .. settings, in TelemetryCsv.ShotHeader's order without time, session, shot, arrow and ms; each field after a comma.</summary>
            public void Write(StringBuilder sb) {
                sb.Append(','); TelemetryCsv.Field(sb, Player); TelemetryCsv.Int(sb, Account);
                sb.Append(',').Append(Kind.ToString()).Append(',').Append(Ult.ToString());
                TelemetryCsv.Int(sb, Ghosts); TelemetryCsv.Int(sb, Pellets); TelemetryCsv.Int(sb, Needles); TelemetryCsv.Int(sb, Tier); TelemetryCsv.Int(sb, Hold);
                TelemetryCsv.Int(sb, Locks); sb.Append(',').Append(Sky.ToString());
                sb.Append(',').Append(Main.ToString()); TelemetryCsv.Int(sb, Quick); TelemetryCsv.Int(sb, Volley); TelemetryCsv.Int(sb, Charge);
                sb.Append(','); TelemetryCsv.Ults(sb, Ults);
                TelemetryCsv.Bit(sb, Ricochet); TelemetryCsv.Bit(sb, Shatter); TelemetryCsv.Bit(sb, Gatherer); TelemetryCsv.Bit(sb, Test);
                sb.Append(','); TelemetryCsv.Field(sb, Version); sb.Append(',').Append(Settings.ToString("x8", Inv));
                sb.Append(','); TelemetryCsv.Field(sb, Arrow);
            }
        }

        /// <summary>The columns the report's skill_of reads (no player), and what the report filters by.</summary>
        struct DKey : IEquatable<DKey> {
            public ShotKind Kind; public UltimateUse Ult; public SkyCode Sky; public MainPath Main;
            public int Ghosts, Pellets, Locks, Tier, Quick;
            public bool Test; public string Version; public uint Settings;

            public static DKey Of(in SKey s) => new DKey {
                Kind = s.Kind, Ult = s.Ult, Sky = s.Sky, Main = s.Main, Ghosts = s.Ghosts, Pellets = s.Pellets, Locks = s.Locks, Tier = s.Tier,
                Quick = s.Quick, Test = s.Test, Version = s.Version, Settings = s.Settings,
            };

            public bool Equals(DKey o) =>
                Kind == o.Kind && Ult == o.Ult && Sky == o.Sky && Main == o.Main && Ghosts == o.Ghosts && Pellets == o.Pellets && Locks == o.Locks
                && Tier == o.Tier && Quick == o.Quick && Test == o.Test && Settings == o.Settings && string.Equals(Version, o.Version, StringComparison.Ordinal);

            public override bool Equals(object? o) => o is DKey k && Equals(k);

            public override int GetHashCode() {
                unchecked {
                    int h = (int)Kind * 7 + (int)Ult; h = h * 31 + (int)Sky; h = h * 31 + (int)Main; h = h * 31 + Ghosts; h = h * 31 + Pellets;
                    h = h * 31 + Locks; h = h * 31 + Tier; h = h * 31 + Quick; h = h * 31 + (Test ? 1 : 0);
                    return h * 31 + (int)Settings;
                }
            }

            public void Write(StringBuilder sb) {
                sb.Append(',').Append(Kind.ToString()).Append(',').Append(Ult.ToString());
                TelemetryCsv.Int(sb, Ghosts); TelemetryCsv.Int(sb, Pellets); TelemetryCsv.Int(sb, Locks); TelemetryCsv.Int(sb, Tier);
                sb.Append(',').Append(Sky.ToString()).Append(',').Append(Main.ToString()); TelemetryCsv.Int(sb, Quick); TelemetryCsv.Bit(sb, Test);
                sb.Append(','); TelemetryCsv.Field(sb, Version); sb.Append(',').Append(Settings.ToString("x8", Inv));
            }
        }

        struct TKey : IEquatable<TKey> {
            public int Hour; public string Version;
            public bool Equals(TKey o) => Hour == o.Hour && string.Equals(Version, o.Version, StringComparison.Ordinal);
            public override bool Equals(object? o) => o is TKey k && Equals(k);
            public override int GetHashCode() => Hour * 31 + StringComparer.Ordinal.GetHashCode(Version ?? "");
        }

        // ---------------------------------------------------------------- the numbers

        sealed class SLine {
            public SKey Key;
            public long Shots, Kills, PvpKills, HeldMs, HeldN;
            public double Cost, Damage, Pvp, Power;   // Power: the shots' arrows' own damage ratings, added up
            public double[] By = new double[Sources];
            public SLine Clone() { var c = (SLine)MemberwiseClone(); c.By = (double[])By.Clone(); return c; }
        }

        sealed class DLine {
            public DKey Key;
            public long Shots, Zero;
            public Dictionary<int, long> Steps = new Dictionary<int, long>();
            public DLine Clone() { var c = (DLine)MemberwiseClone(); c.Steps = new Dictionary<int, long>(Steps); return c; }
        }

        sealed class MLine {
            public string Version = "";
            public long Hits, Unattributed, Things, Mods;
            public double UnattributedDamage;
            public MLine Clone() => (MLine)MemberwiseClone();
        }

        sealed class Day {
            public DateTime Date;
            public string Path = "";
            public readonly Dictionary<SKey, SLine> S = new Dictionary<SKey, SLine>();
            public readonly Dictionary<DKey, DLine> D = new Dictionary<DKey, DLine>();
            public readonly Dictionary<TKey, double> T = new Dictionary<TKey, double>();
            public readonly Dictionary<string, MLine> M = new Dictionary<string, MLine>(StringComparer.Ordinal);
            public int Open;      // its shots still waiting for late hits
            public long Changes;  // game thread: bumped on every change
            public long Written;  // the Changes of the last copy written (set by the worker)
            public int Count => S.Count + D.Count + T.Count + M.Count;
        }

        /// <summary>A shot waiting for late hits: where its numbers go and its own creature damage so far.</summary>
        sealed class OpenShot {
            public long Utc;
            public Day Day = null!;
            public SLine S = null!;
            public DKey D;
            public double Own;
            public bool Sky, Test;
            public string Version = "";
        }

        /// <summary>A day's numbers as copied for the worker.</summary>
        sealed class Copy {
            public Day Day = null!;
            public long Changes;
            public readonly List<SLine> S = new List<SLine>();
            public readonly List<DLine> D = new List<DLine>();
            public readonly List<KeyValuePair<TKey, double>> T = new List<KeyValuePair<TKey, double>>();
            public readonly List<MLine> M = new List<MLine>();
        }

        // ---------------------------------------------------------------- state

        readonly string folder, session, version;
        readonly Dictionary<DateTime, Day> days = new Dictionary<DateTime, Day>();
        readonly HashSet<DateTime> dropped = new HashSet<DateTime>();
        readonly Dictionary<int, OpenShot> open = new Dictionary<int, OpenShot>();
        readonly Queue<KeyValuePair<int, OpenShot>> byTime = new Queue<KeyValuePair<int, OpenShot>>();
        readonly Dictionary<string, long> lastShot = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly List<DateTime> scratch = new List<DateTime>();
        readonly LowDiskStop disk = new LowDiskStop();
        readonly StringBuilder text = new StringBuilder(1 << 16);   // the worker's
        List<Copy>? pending;
        volatile bool writing;
        string? problem;
        Day? newest;

        internal TelemetryStats(string folder, string session, string version) {
            this.folder = folder ?? ""; this.session = session ?? ""; this.version = version ?? "";
        }

        /// <summary>The low-disk stop ([Telemetry] MinFreeMb): while the drive is under it no file is written and the days stay in memory.</summary>
        internal long MinFreeBytes { get => disk.MinFreeBytes; set => disk.MinFreeBytes = value; }
        internal Func<long>? FreeProbe { get => disk.FreeProbe; set => disk.FreeProbe = value; }
        internal int FreeAskMs { get => disk.FreeAskMs; set => disk.FreeAskMs = value; }

        internal int OpenShots => open.Count;

        internal int Lines {
            get { int n = 0; foreach (Day d in days.Values) n += d.Count; return n; }
        }

        internal bool Writing => writing;

        /// <summary>"stats-v1-2026-11-19-20261119-180000.csv": one file per server-local day per server run.</summary>
        internal static string FileName(DateTime localDay, string session) => "stats-v1-" + localDay.ToString("yyyy-MM-dd", Inv) + "-" + session + ".csv";

        internal string? TakeProblem() => Interlocked.Exchange(ref problem, null);

        void Say(string p) => Interlocked.Exchange(ref problem, p);

        // ---------------------------------------------------------------- the game's thread

        // The local time of a UTC time, as TelemetryCsv.LocalDay finds it, with the offset kept per 15-minute UTC slot (a time zone's
        // offset, and when it changes, are whole quarter hours): ToLocalTime costs about 0.2 microseconds, and a hit asks twice.
        const long Slot = 15 * TimeSpan.TicksPerMinute;
        long offsetSlot = long.MinValue, offsetTicks;

        DateTime Local(long utc) {
            long slot = utc / Slot;
            if (slot != offsetSlot) {
                offsetTicks = new DateTime(utc, DateTimeKind.Utc).ToLocalTime().Ticks - utc;
                offsetSlot = slot;
            }
            return new DateTime(utc + offsetTicks);
        }

        DateTime LocalDay(long utc) {
            try { return Local(utc).Date; }
            catch { offsetSlot = long.MinValue; return DateTime.MinValue.Date; } // a broken time: one day of its own, never a throw
        }

        int LocalHour(long utc) {
            try { return Local(utc).Hour; }
            catch { offsetSlot = long.MinValue; return 0; }
        }

        static double Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0.0 : v;

        /// <summary>The day's table; a day already dropped (a clock that went back) is the newest one instead, so a file is never rewritten
        /// with less than it holds.</summary>
        Day DayFor(DateTime date) {
            if (days.TryGetValue(date, out Day d)) return d;
            if (dropped.Contains(date) && newest != null) return newest;
            d = new Day { Date = date, Path = System.IO.Path.Combine(folder, FileName(date, session)) };
            days[date] = d;
            if (newest == null || date > newest.Date) newest = d;
            return d;
        }

        MLine MOf(Day d, string v) {
            if (!d.M.TryGetValue(v, out MLine m)) d.M[v] = m = new MLine { Version = v };
            return m;
        }

        /// <summary>One release as the server decided it.</summary>
        internal void Add(in ShotRow r) {
            try {
                SKey key = SKey.Of(in r);
                Day day = DayFor(LocalDay(r.Utc));
                if (!day.S.TryGetValue(key, out SLine s)) day.S[key] = s = new SLine { Key = key };
                s.Shots++;
                // the time it took: since that archer's previous shot in this run (test-build shots their own lane, as the report leaves
                // them out of the gaps), at most the gap cap; an archer's first shot costs the cap; one out of order costs 0
                string lane = (r.Test ? "t" : "") + (r.Account != 0 ? "#" + r.Account.ToString(Inv) : "n" + key.Player);
                if (lastShot.TryGetValue(lane, out long prev)) {
                    long gap = r.Utc - prev;
                    s.Cost += gap <= 0 ? 0.0 : gap >= GapCapTicks ? GapCapSeconds : gap / (double)TimeSpan.TicksPerSecond;
                    if (r.Utc > prev) lastShot[lane] = r.Utc;
                }
                else { s.Cost += GapCapSeconds; lastShot[lane] = r.Utc; }
                if (r.Ms >= 0) { s.HeldMs += r.Ms; s.HeldN++; }
                s.Power += Math.Max(0.0, Finite(r.Power));
                day.Changes++;
                var o = new OpenShot {
                    Utc = r.Utc, Day = day, S = s, D = DKey.Of(in key), Version = key.Version, Test = r.Test,
                    Sky = r.Sky == SkyCode.Skyfall || r.Sky == SkyCode.Fizzled,   // the report's two Skyfall skills (skill_of)
                };
                day.Open++;
                if (r.Shot <= 0) { Close(o); return; }   // nothing can name it: a miss now
                if (open.TryGetValue(r.Shot, out OpenShot was)) { open.Remove(r.Shot); Close(was); } // a number used again: the old one is done
                open[r.Shot] = o;
                byTime.Enqueue(new KeyValuePair<int, OpenShot>(r.Shot, o));
            }
            catch (Exception e) { Say("adding a shot failed (" + e.Message + ")"); }
        }

        /// <summary>One hit of archery damage; <c>h.Shot</c> names its shot (0 = none). Mirrors analyze's hit loop.</summary>
        internal void Add(in HitRow h) {
            try {
                Day day = DayFor(LocalDay(h.Utc));
                OpenShot? o = h.Shot > 0 && open.TryGetValue(h.Shot, out OpenShot found) ? found : null;
                double dealt = Finite(h.Dealt);
                int src = (int)h.Source < Sources ? (int)h.Source : (int)DealtBy.Mod;   // TelemetryCsv.Name writes an unknown source as "mod"
                MLine m = MOf(day, o != null ? o.Version : version);
                m.Hits++;
                day.Changes++;
                if (o != null && o.Test) {   // a test build's borrowed orbs: only its own line (the report leaves the shot and its hits out)
                    Credit(o, h, dealt, src);
                    return;
                }
                if (src == (int)DealtBy.Mod) m.Mods++;
                if (h.Victim == Victim.Thing) { m.Things++; return; }
                if (h.Victim == Victim.Player) { if (o != null) Credit(o, h, dealt, src); return; }
                if (o == null) { m.Unattributed++; m.UnattributedDamage += dealt; }
                else Credit(o, h, dealt, src);
                if (o == null || !o.Sky) {
                    var tk = new TKey { Hour = LocalHour(h.Utc), Version = o != null ? o.Version : version };
                    day.T.TryGetValue(tk, out double v);
                    day.T[tk] = v + dealt;
                }
            }
            catch (Exception e) { Say("adding a hit failed (" + e.Message + ")"); }
        }

        static void Credit(OpenShot o, in HitRow h, double dealt, int src) {
            SLine s = o.S;
            if (h.Victim == Victim.Player) { s.Pvp += dealt; if (h.Killed) s.PvpKills++; }
            else if (h.Victim == Victim.Creature) { s.Damage += dealt; if (h.Killed) s.Kills++; s.By[src] += dealt; o.Own += dealt; }
            o.Day.Changes++;
        }

        /// <summary>A shot's own damage goes into its kind's tally: "zero" when exactly 0, else step floor(damage / Step).</summary>
        void Close(OpenShot o) {
            Day day = o.Day;
            day.Open--;
            if (!day.D.TryGetValue(o.D, out DLine d)) day.D[o.D] = d = new DLine { Key = o.D };
            d.Shots++;
            if (o.Own == 0.0) d.Zero++;
            else {
                double q = Math.Floor(o.Own / Step);
                int step = q >= int.MaxValue ? int.MaxValue : q <= int.MinValue ? int.MinValue : (int)q;
                d.Steps.TryGetValue(step, out long n);
                d.Steps[step] = n + 1;
            }
            day.Changes++;
        }

        /// <summary>Closes the shots released more than CloseSeconds before <paramref name="utcNow"/>; drops finished days.</summary>
        internal void Tick(long utcNow) {
            try {
                while (byTime.Count > 0) {
                    KeyValuePair<int, OpenShot> e = byTime.Peek();
                    if (utcNow - e.Value.Utc <= CloseTicks) break;
                    byTime.Dequeue();
                    if (open.TryGetValue(e.Key, out OpenShot o) && ReferenceEquals(o, e.Value)) { open.Remove(e.Key); Close(o); }
                }
                Prune();
            }
            catch (Exception e) { Say("closing shots failed (" + e.Message + ")"); }
        }

        /// <summary>Quitting: every open shot closes now.</summary>
        internal void CloseAll() {
            try {
                foreach (OpenShot o in open.Values) Close(o);
                open.Clear();
                byTime.Clear();
            }
            catch (Exception e) { Say("closing shots failed (" + e.Message + ")"); }
        }

        // A day goes once a later day exists, none of its shots is open and its last copy was written.
        void Prune() {
            if (newest == null || days.Count < 2) return;
            scratch.Clear();
            foreach (Day d in days.Values)
                if (d != newest && d.Open <= 0 && Interlocked.Read(ref d.Written) == d.Changes) scratch.Add(d.Date);
            foreach (DateTime date in scratch) { days.Remove(date); dropped.Add(date); }
            scratch.Clear();
        }

        /// <summary>Copies the changed days' numbers here and writes them on the worker (<paramref name="background"/>) or here (tests,
        /// quitting). False while a write is running: nothing is lost, the days stay changed for the next call.</summary>
        internal bool Flush(bool background) {
            if (writing) return false;
            try {
                Prune();
                List<Copy>? batch = null;
                foreach (Day d in days.Values) {
                    if (Interlocked.Read(ref d.Written) == d.Changes) continue;
                    var c = new Copy { Day = d, Changes = d.Changes };
                    foreach (SLine s in d.S.Values) c.S.Add(s.Clone());
                    foreach (DLine l in d.D.Values) c.D.Add(l.Clone());
                    foreach (KeyValuePair<TKey, double> t in d.T) c.T.Add(t);
                    foreach (MLine m in d.M.Values) c.M.Add(m.Clone());
                    (batch ??= new List<Copy>()).Add(c);
                }
                if (batch == null) return true;
                pending = batch;
                writing = true;
                if (background) {
                    try { ThreadPool.QueueUserWorkItem(_ => Write()); return true; }
                    catch (Exception e) { Say("the background writer could not start (" + e.Message + "); writing here"); }
                }
                Write();
                Prune();
                return true;
            }
            catch (Exception e) { Say("copying the statistics failed (" + e.Message + ")"); writing = false; return true; }
        }

        /// <summary>Waits up to <paramref name="milliseconds"/> for a running write; true when none runs.</summary>
        internal bool WaitIdle(int milliseconds) {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (writing && clock.ElapsedMilliseconds < milliseconds) Thread.Sleep(5);
            return !writing;
        }

        // ---------------------------------------------------------------- the worker

        void Write() {
            try {
                if (TestDelayMs > 0) Thread.Sleep(TestDelayMs);
                List<Copy>? batch = pending;
                if (batch == null) return;
                if (disk.Low(folder, Say, out long free)) {
                    Say("the drive has " + (free >> 20) + " MB free, under the " + (MinFreeBytes >> 20) + " MB of [Telemetry] MinFreeMb: the statistics file is not written until there is more room (nothing is lost: they stay in memory)");
                    return;
                }
                Directory.CreateDirectory(folder);
                foreach (Copy c in batch) {
                    try {
                        Format(c);
                        WriteWhole(c.Day.Path, text.ToString());
                        Interlocked.Exchange(ref c.Day.Written, c.Changes);
                    }
                    catch (Exception e) { Say("writing " + System.IO.Path.GetFileName(c.Day.Path) + " failed (" + e.Message + "); the day is kept for the next try"); }
                }
            }
            catch (Exception e) { Say("writing the statistics failed (" + e.Message + "); they are kept for the next try"); }
            finally { pending = null; text.Clear(); writing = false; }
        }

        // Written beside, then moved over: the old file stays whole until the new one is complete. A failed move leaves no .tmp behind.
        static void WriteWhole(string path, string content) {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Bom);
            try {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch {
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }

        void Format(Copy c) {
            StringBuilder sb = text;
            sb.Clear();
            string day = c.Day.Date.ToString("yyyy-MM-dd", Inv);
            sb.Append(Header); TelemetryCsv.Field(sb, session); sb.Append('\n');
            sb.Append(SHeader).Append('\n');
            foreach (SLine s in c.S) {
                sb.Append('S').Append(',').Append(day);
                s.Key.Write(sb);
                TelemetryCsv.Int(sb, s.Shots); Num(sb, s.Cost); Num(sb, s.Damage); TelemetryCsv.Int(sb, s.Kills); Num(sb, s.Pvp); TelemetryCsv.Int(sb, s.PvpKills);
                TelemetryCsv.Int(sb, s.HeldMs); TelemetryCsv.Int(sb, s.HeldN); Num(sb, s.Power);
                for (int i = 0; i < Sources; i++) Num(sb, s.By[i]);
                sb.Append('\n');
            }
            sb.Append(DHeader).Append('\n');
            var steps = new List<int>();
            foreach (DLine d in c.D) {
                sb.Append('D').Append(',').Append(day);
                d.Key.Write(sb);
                TelemetryCsv.Int(sb, d.Shots); TelemetryCsv.Int(sb, d.Zero); sb.Append(',');
                steps.Clear(); steps.AddRange(d.Steps.Keys); steps.Sort();
                for (int i = 0; i < steps.Count; i++) {
                    if (i > 0) sb.Append(';');
                    sb.Append(steps[i].ToString(Inv)).Append(':').Append(d.Steps[steps[i]].ToString(Inv));
                }
                sb.Append('\n');
            }
            sb.Append(THeader).Append('\n');
            c.T.Sort((a, b) => a.Key.Hour != b.Key.Hour ? a.Key.Hour.CompareTo(b.Key.Hour) : string.CompareOrdinal(a.Key.Version, b.Key.Version));
            foreach (KeyValuePair<TKey, double> t in c.T) {
                sb.Append('T').Append(',').Append(day); TelemetryCsv.Int(sb, t.Key.Hour); sb.Append(','); TelemetryCsv.Field(sb, t.Key.Version); Num(sb, t.Value);
                sb.Append('\n');
            }
            sb.Append(MHeader).Append('\n');
            foreach (MLine m in c.M) {
                sb.Append('M').Append(',').Append(day); sb.Append(','); TelemetryCsv.Field(sb, m.Version);
                TelemetryCsv.Int(sb, m.Hits); TelemetryCsv.Int(sb, m.Unattributed); Num(sb, m.UnattributedDamage); TelemetryCsv.Int(sb, m.Things); TelemetryCsv.Int(sb, m.Mods);
                sb.Append('\n');
            }
        }

        // up to 4 decimals, invariant; a broken number is written as 0
        static void Num(StringBuilder sb, double v) => sb.Append(',').Append(double.IsNaN(v) || double.IsInfinity(v) ? "0" : v.ToString("0.####", Inv));
    }
}
