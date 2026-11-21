using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace QuickshotRing {
    /// <summary>G5: one timed section in flight. Made by <see cref="Timing.Begin(int)"/> and ended by Dispose, normally through
    /// <c>using var _t = Timing.Begin(Id);</c> as the first statement of the timed body, so the section ends on every path,
    /// exceptions included, and nothing else in the body changes. A struct: no allocation; <c>using</c> calls Dispose on it
    /// without boxing. Start 0 = not recording (timing was off when it began).</summary>
    internal readonly struct TimingScope : IDisposable {
        internal readonly long Start;
        internal readonly int Id;
        internal TimingScope(int id, long start) { Id = id; Start = start; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() { if (Start != 0) Timing.End(Id, Start, Stopwatch.GetTimestamp()); }
    }

    /// <summary>G5: in-memory timing of everything the mod runs. Unity-free (tested in TimingTests.cs); the settings, the console
    /// lines and the report file's place are TimingHost.cs. Sections are registered once by name (<c>static readonly int</c>) and
    /// addressed by index. Off: <see cref="Begin(int)"/> is one bool test and Dispose one zero test. On: two timestamp reads and a
    /// few array updates per section, no allocation. Main thread only (every caller is: Unity callbacks and Harmony patches on
    /// the game's main-thread methods).</summary>
    internal static class Timing {
        /// <summary>[Timing] Enabled. Read at every Begin; flipping it mid-session is safe (a section that began while on ends and
        /// is counted even if it is off by then; one that began while off records nothing).</summary>
        internal static bool On;

        internal static readonly long Frequency = Stopwatch.Frequency;
        internal const int BucketCount = 5;
        internal const int MaxDepth = 64;
        internal const long NoNumber = -1;

        static readonly object gate = new object();
        static readonly Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.Ordinal);
        static string[] names = new string[256];
        static long[] calls = new long[256], total = new long[256], longest = new long[256], frameSec = new long[256];
        static long[] buckets = new long[256 * BucketCount];
        static int[] frameMark = new int[256], touched = new int[256];
        static int count, touchedCount, frameNo = 1;

        static int depth;
        static readonly string?[] ctxText = new string?[MaxDepth];
        static readonly long[] ctxNum = new long[MaxDepth];
        // fix1 (final-G5 M-2, M-3): per open depth, the section's id, the time its nested sections took and whether one of them
        // (at any depth below) already queued a spike line
        static readonly int[] ctxId = new int[MaxDepth];
        static readonly long[] ctxChild = new long[MaxDepth];
        static readonly bool[] ctxChildSpiked = new bool[MaxDepth];
        static int leakDepth, leakId = -1;

        static readonly long b01 = Ticks(0.1), b1 = Ticks(1), b5 = Ticks(5), b20 = Ticks(20);
        static long spikeTicks = Ticks(5), frameSpikeTicks = Ticks(60);

        internal static long Ticks(double ms) => (long)(ms * Frequency / 1000.0);
        internal static double Ms(long ticks) => ticks * 1000.0 / Frequency;

        /// <summary>Spikes: a single section call or a whole frame (Configure's thresholds).</summary>
        internal static void Configure(double spikeMs, double frameSpikeMs) {
            spikeTicks = spikeMs > 0 ? Ticks(spikeMs) : long.MaxValue;
            frameSpikeTicks = frameSpikeMs > 0 ? Ticks(frameSpikeMs) : long.MaxValue;
        }

        // ---------------------------------------------------------------- sections

        /// <summary>The index of the section of this name (registered on first use). Never throws.</summary>
        internal static int Section(string name) {
            lock (gate) {
                if (byName.TryGetValue(name, out int id)) return id;
                if (count == names.Length) Grow(names.Length * 2);
                id = count++;
                names[id] = name;
                byName[name] = id;
                return id;
            }
        }

        static void Grow(int n) {
            Array.Resize(ref names, n); Array.Resize(ref calls, n); Array.Resize(ref total, n); Array.Resize(ref longest, n);
            Array.Resize(ref frameSec, n); Array.Resize(ref frameMark, n); Array.Resize(ref touched, n);
            Array.Resize(ref buckets, n * BucketCount);
        }

        internal static int SectionCount => count;
        internal static string NameOf(int id) => (uint)id < (uint)count ? names[id] : "?";
        internal static int Depth => depth;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimingScope Begin(int id) => On ? BeginAt(id, Stopwatch.GetTimestamp(), null, NoNumber) : default;

        /// <summary>With a short context for the spike line (a player, an asset name). The string must already exist.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimingScope Begin(int id, string? context) => On ? BeginAt(id, Stopwatch.GetTimestamp(), context, NoNumber) : default;

        /// <summary>With a number for the spike line (an arrow or creature id).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimingScope Begin(int id, uint context) => On ? BeginAt(id, Stopwatch.GetTimestamp(), null, context) : default;

        internal static TimingScope BeginAt(int id, long now, string? text, long number) {
            int d = depth++;
            if (d < MaxDepth) { ctxText[d] = text; ctxNum[d] = number; ctxId[d] = id; ctxChild[d] = 0; ctxChildSpiked[d] = false; }
            return new TimingScope(id, now <= 0 ? 1 : now);
        }

        internal static void End(int id, long start, long now) {
            long t = now - start;
            if (t < 0) t = 0;
            int d = depth > 0 ? --depth : 0;
            string? text = null;
            long number = NoNumber;
            long self = t;
            bool childSpiked = false;
            if (d < MaxDepth) {
                text = ctxText[d]; number = ctxNum[d]; ctxText[d] = null;
                self = t - ctxChild[d]; if (self < 0) self = 0;
                childSpiked = ctxChildSpiked[d];
            }
            bool spiked = false;
            if ((uint)id < (uint)count) {
                calls[id]++;
                total[id] += t;
                if (t > longest[id]) longest[id] = t;
                buckets[id * BucketCount + Bucket(t)]++;
                if (d == 0) frameMod += t; // outermost only: nested time is already inside its parent
                if (frameMark[id] != frameNo) { frameMark[id] = frameNo; frameSec[id] = t; touched[touchedCount++] = id; }
                else frameSec[id] += t;
                // fix1 (final-G5 M-2): one line per outlier, not one per nesting level. A section prints when its own time (outside
                // its nested sections) passes the threshold, or when its whole time does and no section inside it printed
                if (self >= spikeTicks || (t >= spikeTicks && !childSpiked)) {
                    Queue(new Spike { Id = id, Ticks = t, Self = self, Text = text, Number = number });
                    spiked = true;
                }
            }
            if (d > 0 && d - 1 < MaxDepth) { ctxChild[d - 1] += t; if (spiked || childSpiked) ctxChildSpiked[d - 1] = true; }
        }

        /// <summary>fix1 (final-G5 M-3): the sections left open at the last frame tick (depth should be 0 there): how deep, and
        /// the outermost one's name. True once per leak, then cleared.</summary>
        internal static bool TakeLeak(out int leakedDepth, out string section) {
            leakedDepth = leakDepth; section = leakId >= 0 ? NameOf(leakId) : "?";
            if (leakDepth == 0) return false;
            leakDepth = 0; leakId = -1;
            return true;
        }

        internal static int Bucket(long t) => t < b01 ? 0 : t < b1 ? 1 : t < b5 ? 2 : t < b20 ? 3 : 4;

        // ---------------------------------------------------------------- frames

        /// <summary>One frame of the game: the time between two calls (Main.OnUpdate) and the mod's share in it.</summary>
        internal struct FrameRecord {
            public long Ticks, Mod, At;
            public int Top0, Top1, Top2;
            public long T0, T1, T2;
        }

        static long lastFrame, frameMod;
        static long frames, frameSum, frameMax, frameModSum, frameModMax, over25, over50;
        internal const double BinMs = 0.25;
        internal const int Bins = 1000; // 0 to 250 ms; slower frames go to the last bin (the worst frame is kept exactly)
        static readonly int[] frameBins = new int[Bins];
        internal const int WorstKept = 10;
        static readonly FrameRecord[] worst = new FrameRecord[WorstKept];
        static int worstCount;
        static readonly long over25Ticks = Ticks(25), over50Ticks = Ticks(50);

        /// <summary>Start of a frame (first thing in Main.OnUpdate). Closes the previous frame; the first call only starts.</summary>
        internal static void FrameTick(long now) {
            // fix1 (final-G5 M-3): no section is open between frames; a leaked one would make every later section count as
            // nested (a mod share of 0 for the rest of the session). Put it right and keep what was open for one warning.
            if (depth != 0) {
                if (leakDepth == 0) { leakDepth = depth; leakId = depth > 0 ? ctxId[0] : -1; }
                Array.Clear(ctxText, 0, ctxText.Length);
                depth = 0;
            }
            if (lastFrame != 0 && On) CloseFrame(now - lastFrame, now);
            lastFrame = now;
            frameMod = 0;
            touchedCount = 0;
            frameNo++;
            if (frameNo == int.MaxValue) { frameNo = 1; Array.Clear(frameMark, 0, frameMark.Length); }
        }

        /// <summary>Timing switched on: the next FrameTick only starts a frame (no stale gap from before).</summary>
        internal static void RestartFrames() { lastFrame = 0; frameMod = 0; touchedCount = 0; frameNo++; }

        static void CloseFrame(long t, long at) {
            frames++;
            frameSum += t;
            if (t > frameMax) frameMax = t;
            frameModSum += frameMod;
            if (frameMod > frameModMax) frameModMax = frameMod;
            if (t > over25Ticks) over25++;
            if (t > over50Ticks) over50++;
            int bin = (int)(Ms(t) / BinMs);
            frameBins[bin < 0 ? 0 : bin >= Bins ? Bins - 1 : bin]++;
            bool spike = t >= frameSpikeTicks;
            if (!spike && worstCount == WorstKept && t <= worst[WorstKept - 1].Ticks) return;
            var r = new FrameRecord { Ticks = t, Mod = frameMod, At = at, Top0 = -1, Top1 = -1, Top2 = -1 };
            for (int i = 0; i < touchedCount; i++) {
                int id = touched[i];
                long s = frameSec[id];
                if (s > r.T0) { r.Top2 = r.Top1; r.T2 = r.T1; r.Top1 = r.Top0; r.T1 = r.T0; r.Top0 = id; r.T0 = s; }
                else if (s > r.T1) { r.Top2 = r.Top1; r.T2 = r.T1; r.Top1 = id; r.T1 = s; }
                else if (s > r.T2) { r.Top2 = id; r.T2 = s; }
            }
            KeepWorst(r);
            if (spike) Queue(new Spike { Id = -1, Ticks = t, Frame = r });
        }

        static void KeepWorst(FrameRecord r) {
            int at;
            if (worstCount < WorstKept) at = worstCount++;
            else if (r.Ticks <= worst[WorstKept - 1].Ticks) return;
            else at = WorstKept - 1;
            worst[at] = r;
            for (int i = at; i > 0 && worst[i].Ticks > worst[i - 1].Ticks; i--) { var x = worst[i]; worst[i] = worst[i - 1]; worst[i - 1] = x; }
        }

        internal static long Frames => frames;
        internal static long FrameMax => frameMax;
        internal static long FrameModMax => frameModMax;
        internal static long CurrentFrameMod => frameMod;
        internal static FrameRecord Worst(int i) => worst[i];
        internal static int WorstCount => worstCount;
        internal static long Calls(int id) => calls[id];
        internal static long Total(int id) => total[id];
        internal static long Longest(int id) => longest[id];
        internal static long BucketOf(int id, int b) => buckets[id * BucketCount + b];

        /// <summary>The frame time below which 99% of the interval's frames fell (upper edge of its 0.25 ms bin, the worst frame
        /// itself when it lies in that bin or beyond 250 ms).</summary>
        internal static double P99Ms() {
            if (frames == 0) return 0;
            long need = (long)Math.Ceiling(frames * 0.99);
            long seen = 0;
            for (int i = 0; i < Bins; i++) {
                seen += frameBins[i];
                if (seen >= need) return i == Bins - 1 ? Ms(frameMax) : Math.Min((i + 1) * BinMs, Ms(frameMax));
            }
            return Ms(frameMax);
        }

        // ---------------------------------------------------------------- spikes (logged by the host between frames)

        internal struct Spike {
            public int Id;            // -1: a whole frame
            public long Ticks;
            public long Self;         // a section's own time, outside its nested sections (fix1)
            public string? Text;
            public long Number;
            public FrameRecord Frame;
        }

        const int QueueSize = 64;
        static readonly Spike[] queue = new Spike[QueueSize];
        static int queued, queueLost;

        // fix1 (final-G5 M-2): the last place is kept for the frame's own line (queued once per frame tick, which drains at once)
        static void Queue(Spike s) {
            if (queued < (s.Id < 0 ? QueueSize : QueueSize - 1)) queue[queued++] = s;
            else queueLost++;
        }

        /// <summary>Hands every queued spike to <paramref name="each"/> and empties the queue; returns how many did not fit.</summary>
        internal static int Drain(Action<Spike> each) {
            int n = queued, lost = queueLost;
            queued = 0; queueLost = 0;
            for (int i = 0; i < n; i++) { Spike s = queue[i]; queue[i] = default; each(s); }
            return lost;
        }

        internal static int Queued => queued;

        internal static string SpikeLine(Spike s) {
            var inv = CultureInfo.InvariantCulture;
            if (s.Id < 0) {
                FrameRecord f = s.Frame;
                string top = f.Top0 >= 0 ? string.Format(inv, "; top '{0}' {1:0.00} ms", NameOf(f.Top0), Ms(f.T0)) : "; no mod section ran";
                return string.Format(inv, "[Archery] Timing: frame {0:0.0} ms, mod share {1:0.00} ms{2}", Ms(s.Ticks), Ms(f.Mod), top);
            }
            string ctx = s.Text != null ? " (" + s.Text + ")" : s.Number != NoNumber ? " (" + s.Number.ToString(inv) + ")" : "";
            string own = s.Self < s.Ticks ? string.Format(inv, "; {0:0.00} ms of it in no nested section", Ms(s.Self)) : "";
            return string.Format(inv, "[Archery] Timing: spike {0:0.00} ms in '{1}'{2}{3}", Ms(s.Ticks), NameOf(s.Id), ctx, own);
        }

        // ---------------------------------------------------------------- report

        internal const string Columns = "section = what ran (a patch is named 'patch Type.Method / Owner.Class'); calls = times it ran this interval; " +
            "total ms = time inside it (a nested section's time is also in its parent's); avg us = total / calls in microseconds; " +
            "max ms = its longest single call; then how many calls took <0.1 ms, 0.1-1 ms, 1-5 ms, 5-20 ms, >20 ms.";

        /// <summary>The interval's report (sections sorted by total time, then the ten worst frames). Does not reset.</summary>
        internal static string Report(string side, DateTime now, long nowTicks, double seconds, string overhead) {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(4096);
            sb.Append("=== QuickshotRing timing: ").Append(side).Append(", ").Append(now.ToString("yyyy-MM-dd HH:mm:ss", inv))
              .Append(string.Format(inv, ", interval {0:0.0} s ===\n", seconds));
            sb.Append("overhead: ").Append(overhead).Append('\n');
            double avg = frames > 0 ? Ms(frameSum) / frames : 0, modAvg = frames > 0 ? Ms(frameModSum) / frames : 0;
            sb.Append(string.Format(inv, "frames {0}  avg {1:0.00} ms  p99 {2:0.00} ms  worst {3:0.00} ms  over 25 ms {4}  over 50 ms {5}  | mod share: total {6:0.00} ms  avg {7:0.000} ms/frame  worst {8:0.00} ms\n",
                frames, avg, P99Ms(), Ms(frameMax), over25, over50, Ms(frameModSum), modAvg, Ms(frameModMax)));
            sb.Append("columns: ").Append(Columns).Append('\n');
            var ids = new List<int>();
            for (int i = 0; i < count; i++) if (calls[i] > 0) ids.Add(i);
            ids.Sort((a, b) => total[b] != total[a] ? total[b].CompareTo(total[a]) : string.CompareOrdinal(names[a], names[b]));
            int w = 7;
            foreach (int i in ids) w = Math.Max(w, names[i].Length);
            sb.Append("section".PadRight(w)).Append(string.Format(inv, " {0,9} {1,10} {2,9} {3,9} {4,8} {5,8} {6,8} {7,8} {8,8}\n",
                "calls", "total ms", "avg us", "max ms", "<0.1", "0.1-1", "1-5", "5-20", ">20"));
            foreach (int i in ids) {
                sb.Append(names[i].PadRight(w)).Append(string.Format(inv, " {0,9} {1,10:0.000} {2,9:0.00} {3,9:0.000} {4,8} {5,8} {6,8} {7,8} {8,8}\n",
                    calls[i], Ms(total[i]), Ms(total[i]) * 1000.0 / calls[i], Ms(longest[i]),
                    buckets[i * BucketCount], buckets[i * BucketCount + 1], buckets[i * BucketCount + 2], buckets[i * BucketCount + 3], buckets[i * BucketCount + 4]));
            }
            if (ids.Count == 0) sb.Append("(no section ran)\n");
            sb.Append("worst frames: time, frame ms, mod ms, the mod's three largest sections in that frame (ms)\n");
            for (int i = 0; i < worstCount; i++) {
                FrameRecord f = worst[i];
                DateTime at = now.AddSeconds(-(nowTicks - f.At) / (double)Frequency);
                sb.Append(string.Format(inv, "  {0}  {1,8:0.00}  {2,7:0.00}  ", at.ToString("HH:mm:ss.fff", inv), Ms(f.Ticks), Ms(f.Mod)));
                AppendTop(sb, f.Top0, f.T0, true); AppendTop(sb, f.Top1, f.T1, false); AppendTop(sb, f.Top2, f.T2, false);
                if (f.Top0 < 0) sb.Append("-");
                sb.Append('\n');
            }
            if (worstCount == 0) sb.Append("  (no frames)\n");
            sb.Append('\n');
            return sb.ToString();
        }

        static void AppendTop(StringBuilder sb, int id, long t, bool first) {
            if (id < 0) return;
            if (!first) sb.Append("; ");
            sb.Append(NameOf(id)).Append(' ').Append(Ms(t).ToString("0.00", CultureInfo.InvariantCulture));
        }

        /// <summary>Starts a new interval: every counter and the frame figures (the sections stay registered).</summary>
        internal static void ResetInterval() {
            Array.Clear(calls, 0, calls.Length); Array.Clear(total, 0, total.Length); Array.Clear(longest, 0, longest.Length);
            Array.Clear(buckets, 0, buckets.Length); Array.Clear(frameBins, 0, frameBins.Length);
            frames = frameSum = frameMax = frameModSum = frameModMax = over25 = over50 = 0;
            worstCount = 0;
        }

        /// <summary>Tests only: forget everything, sections included.</summary>
        internal static void ResetAll() {
            lock (gate) { byName.Clear(); count = 0; }
            ResetInterval();
            depth = 0; touchedCount = 0; frameMod = 0; lastFrame = 0; queued = 0; queueLost = 0; leakDepth = 0; leakId = -1;
            Array.Clear(frameMark, 0, frameMark.Length);
            Array.Clear(ctxText, 0, ctxText.Length);
            frameNo = 1;
        }

        // ---------------------------------------------------------------- its own cost

        /// <summary>Times <paramref name="n"/> empty sections with timing on and with it off; nanoseconds per section. Leaves the
        /// self-test section's counters in the interval (the caller resets it).</summary>
        internal static void Calibrate(int n, int selfId, out double onNs, out double offNs) {
            bool was = On;
            long savedMod = frameMod;
            try {
                On = true;
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < n; i++) { using var s = Begin(selfId); }
                long t1 = Stopwatch.GetTimestamp();
                On = false;
                for (int i = 0; i < n; i++) { using var s = Begin(selfId); }
                long t2 = Stopwatch.GetTimestamp();
                onNs = (t1 - t0) * 1e9 / Frequency / Math.Max(1, n);
                offNs = (t2 - t1) * 1e9 / Frequency / Math.Max(1, n);
            }
            finally { On = was; frameMod = savedMod; }
        }
    }

    /// <summary>G5: at most <c>limit</c> spike lines per minute; the rest are counted and reported when the minute is over.</summary>
    internal sealed class SpikeLimiter {
        double windowStart = double.NegativeInfinity;
        int shown, suppressed;

        /// <summary>True when this spike may be logged now. <paramref name="rolledOver"/> is how many were held back in the minute
        /// that just ended (report it before this one), 0 otherwise.</summary>
        internal bool Allow(double nowSeconds, int limit, out int rolledOver) {
            rolledOver = 0;
            if (nowSeconds - windowStart >= 60.0) {
                rolledOver = suppressed;
                windowStart = nowSeconds; shown = 0; suppressed = 0;
            }
            if (shown < limit) { shown++; return true; }
            suppressed++;
            return false;
        }

        /// <summary>Held back in a minute that has ended by <paramref name="nowSeconds"/> (once; 0 otherwise).</summary>
        internal int TakeExpired(double nowSeconds) {
            if (suppressed == 0 || nowSeconds - windowStart < 60.0) return 0;
            int n = suppressed; suppressed = 0; shown = 0; windowStart = nowSeconds;
            return n;
        }

        internal int Suppressed => suppressed;

        /// <summary>Counts spikes that were never offered (the queue was full) as not shown.</summary>
        internal void Suppress(int n) { if (n > 0) suppressed += n; }
    }

    /// <summary>G5: the report file. Appended to; at MaxFileKb it is renamed to .old (one old file kept) and started again.</summary>
    internal static class TimingFile {
        internal static bool ShouldRotate(long currentBytes, long addBytes, int maxKb) =>
            maxKb > 0 && currentBytes > 0 && currentBytes + addBytes > (long)maxKb * 1024;

        internal static void Append(string path, string text, int maxKb) {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            long size = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (ShouldRotate(size, Encoding.UTF8.GetByteCount(text), maxKb)) {
                string old = path + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            File.AppendAllText(path, text, new UTF8Encoding(false));
        }
    }
}
