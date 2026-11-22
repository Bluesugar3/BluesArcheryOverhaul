using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    /// <summary>G5: the Unity side of Timing.cs. [Timing] settings (each machine its own, re-read live), the frame tick (first thing
    /// in Main.OnUpdate), spike lines between frames, and the report file every [Timing] ReportSeconds and at shutdown:
    /// UserData\BluesArcheryOverhaul\timing-server.txt or timing-client.txt (never the console; one Summary line points at it).</summary>
    internal static class TimingHost {
        static MelonPreferences_Entry<bool> enabled = null!;
        static MelonPreferences_Entry<float> reportSeconds = null!, spikeMs = null!, frameSpikeMs = null!;
        static MelonPreferences_Entry<int> maxFileKb = null!, spikesPerMinute = null!;

        static readonly int TSelf = Timing.Section("Timing self-test (empty sections, start-up only)");
        static readonly int TWrite = Timing.Section("Timing report write");
        static readonly SpikeLimiter limiter = new SpikeLimiter();
        static readonly Action<Timing.Spike> show = Show;
        static string overhead = "not measured";
        static bool calibrated, warnedWrite, ready;
        static float intervalStart;

#if QUEST
        static string Side => "client";   // a headset is never the server
#else
        static string Side => global::ApplicationManager.IsHeadless ? "server" : "client";
#endif
        static string FilePath => Path.Combine(Cfg.Folder, $"timing-{Side}.txt");

        internal static void Init() {
            var cat = Cfg.Category("Timing");
            enabled = cat.CreateEntry("Enabled", false, description: "Not shared. Times everything the mod runs, in memory (well under a microsecond per timed section; the start-up line says how much), and writes a report every ReportSeconds to UserData\\BluesArcheryOverhaul\\timing-server.txt or timing-client.txt. Off: one bool test per section. Switch on for a play-test whose speed will be looked at; safe to switch on or off while the game runs.");
            reportSeconds = cat.CreateEntry("ReportSeconds", 60f, description: "Seconds between two reports (and one at shutdown or when timing is switched off).");
            maxFileKb = cat.CreateEntry("MaxFileKb", 2048, description: "When the report file would pass this size it is renamed to .old (one kept) and started again.");
            spikeMs = cat.CreateEntry("SpikeMs", 5f, description: "A single timed call longer than this logs one line at once (Summary level). 0: never.");
            spikesPerMinute = cat.CreateEntry("SpikesPerMinute", 20, description: "At most this many spike lines a minute, and separately this many slow-frame lines; the rest are counted in one line when the minute is over. A slow call inside nested sections prints one line, for the innermost section that was slow.");
            frameSpikeMs = cat.CreateEntry("FrameSpikeMs", 60f, description: "A whole frame longer than this logs one line with the mod's share and its largest section. 0: never.");
            Cfg.Reloaded += Refresh;
        }

        /// <summary>Start-up (after Cfg.Ready) and every settings reload.</summary>
        internal static void Refresh() {
            ready = true;
            Timing.Configure(spikeMs.Value, frameSpikeMs.Value);
            if (enabled.Value && !Timing.On) Start();
            else if (!enabled.Value && Timing.On) Stop();
        }

        static void Start() {
            if (!calibrated) {
                calibrated = true;
                Timing.Calibrate(100_000, TSelf, out double onNs, out double offNs);
                overhead = string.Format(CultureInfo.InvariantCulture, "{0:0} ns per timed section, {1:0.0} ns per section while timing is off (100000 empty sections at {2}, on this machine)",
                    onNs, offNs, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                MelonLogger.Msg($"[Archery] Timing: on; {overhead}; {Timing.SectionCount} sections registered so far; reports to {FilePath}");
                if (onNs >= 1000) MelonLogger.Warning($"[Archery] Timing: a timed section costs {onNs:0} ns here, not well under a microsecond: the numbers in the report carry that much per call");
            }
            else if (Logs.Summary) MelonLogger.Msg($"[Archery] Timing: on; reports to {FilePath}");
            Timing.ResetInterval();
            Timing.RestartFrames();
            intervalStart = Time.realtimeSinceStartup;
            Timing.On = true;
        }

        static void Stop() {
            Write("timing switched off");
            Timing.On = false;
            if (Logs.Summary) MelonLogger.Msg("[Archery] Timing: off");
        }

        /// <summary>First thing in Main.OnUpdate: closes the last frame, prints the spikes queued since, writes a report when due.</summary>
        internal static void Frame() {
            if (!Timing.On) return;
            try { Tick(); }
            catch (Exception e) { if (!warnedFrame) { warnedFrame = true; MelonLogger.Warning($"[Archery] Timing: frame tick failed: {e.Message}"); } }
        }
        static bool warnedFrame;

        static void Tick() {
            Timing.FrameTick(Stopwatch.GetTimestamp());
            // fix1 (final-G5 M-3): FrameTick put a leaked depth back to 0; say so once per session
            if (Timing.TakeLeak(out int leaked, out string open) && !warnedLeak) {
                warnedLeak = true;
                MelonLogger.Warning($"[Archery] Timing: {leaked} section(s) were still open at the start of a frame (outermost '{open}'); the nesting was reset to 0. Mod shares before this line may read low");
            }
            float now = Time.realtimeSinceStartup;
            if (Timing.Queued > 0) {
                int lost = Timing.Drain(show);
                if (lost > 0) Held(lost);
            }
            int expired = limiter.TakeExpired(now);
            if (expired > 0 && Logs.Summary) MelonLogger.Msg($"[Archery] Timing: {expired} more spike(s) in the last minute not shown (SpikesPerMinute {spikesPerMinute.Value}); they are in the report's buckets");
            int expiredFrames = frameLimiter.TakeExpired(now);
            if (expiredFrames > 0 && Logs.Summary) MelonLogger.Msg($"[Archery] Timing: {expiredFrames} more slow frame(s) in the last minute not shown (SpikesPerMinute {spikesPerMinute.Value}); they are in the report");
            if (now - intervalStart >= Mathf.Max(5f, reportSeconds.Value)) Write(null);
        }
        static bool warnedLeak;

        // fix1 (final-G5 M-2): frame lines have their own limiter (the same SpikesPerMinute), so section spikes cannot use up their place
        static readonly SpikeLimiter frameLimiter = new SpikeLimiter();

        static void Show(Timing.Spike s) {
            if (!Logs.Summary) return;
            bool frame = s.Id < 0;
            if ((frame ? frameLimiter : limiter).Allow(Time.realtimeSinceStartup, spikesPerMinute.Value, out int rolled)) {
                if (rolled > 0) MelonLogger.Msg($"[Archery] Timing: {rolled} more {(frame ? "slow frame(s)" : "spike(s)")} in the minute before not shown");
                MelonLogger.Msg(Timing.SpikeLine(s));
            }
        }

        static void Held(int lost) => limiter.Suppress(lost); // more than the queue held between two frames: counted as not shown

        static void Write(string? why) {
            using var _t = Timing.Begin(TWrite);
            float now = Time.realtimeSinceStartup;
            double seconds = now - intervalStart;
            intervalStart = now;
            try {
                double worst = Timing.Ms(Timing.FrameMax), worstMod = Timing.WorstCount > 0 ? Timing.Ms(Timing.Worst(0).Mod) : 0;
                string head = why != null ? $"({why}) " : "";
                string text = Timing.Report(Side, DateTime.Now, Stopwatch.GetTimestamp(), seconds,
                    overhead + $"; [Log] Level {(Logs.Debug ? "Debug" : Logs.Summary ? "Summary" : "Quiet")} (a log line costs 0.2-0.35 ms, inside whatever section prints it) " + head);
                TimingFile.Append(FilePath, text, maxFileKb.Value);
                if (Logs.Summary) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Timing: report written to {0}, worst frame {1:0.0} ms, mod share {2:0.00} ms", Path.GetFileName(FilePath), worst, worstMod));
            }
            catch (Exception e) {
                if (!warnedWrite) { warnedWrite = true; MelonLogger.Warning($"[Archery] Timing: could not write {FilePath}: {e.Message}"); }
            }
            finally { Timing.ResetInterval(); }
        }

        /// <summary>Main.OnApplicationQuit: the last interval's report.</summary>
        internal static void Shutdown() {
            if (!ready || !Timing.On) return;
            try { Write("shutdown"); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Timing: shutdown report failed: {e.Message}"); }
        }
    }
}
