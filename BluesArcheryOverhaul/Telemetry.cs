using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Alta.Impact;
using Alta.Networking;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Balance telemetry, server only (the owner, 2026-11-19: "Have it keep logs of all arrows shot, skills used and damage delt, so after
    // a day of playtest we can take all that information and graph it to visualize if some skills are to op"; design
    // docs/superpowers/specs/2026-11-19-balance-telemetry-design.md). Every release the server decides (Draws.Release -> Shot) and every
    // hit of archery damage are compiled into per-day statistics as they happen (TelemetryStats; since 0.19.5, the owner: "It should all
    // be compiled immediately so it stays relatively small"), one small file per server run per day in UserData\BluesArcheryOverhaul\
    // telemetry\; [Telemetry] KeepRows also writes the old row files (shots-v1-<day>.csv, hits-v1-<day>.csv) for troubleshooting.
    // tools/telemetry_report.py draws either.
    //
    // Every hit on the server passes HealthObject.ReceiveDamage (PlayerHealth reaches it through base, after the PvP gate, invincibility
    // and its multipliers), so one prefix/postfix sees them all and reads what really came off the health bar. The mod's own damage
    // sites open a HitScope (TelemetryRules.cs) naming their source; without one a tool on an arrow is the real arrow, a tool or wielder
    // that is a known Holy beam is the beam or its blast, and anything else is not archery and is left alone. The hooks only append a
    // struct or add to numbers; the statistics file is written on a background thread every StatsSeconds and the rows (KeepRows) every
    // FlushSeconds (the owner's condition: no performance issues).
    internal static class Telemetry {
        const float FlushSeconds = 5f, StatsSeconds = 60f, WarnSeconds = 600f;

        static MelonPreferences_Entry<bool>? enabled, keepRows;
        static MelonPreferences_Entry<int>? minFreeMb;
        static TelemetryLog? log;
        static TelemetryStats? stats;
        static readonly ShotIndex index = new ShotIndex();
        static readonly string session = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        static float nextFlush, nextStats, lastProblemWarn = float.MinValue, lastStatsWarn = float.MinValue, lastDropWarn = float.MinValue;
        static long droppedSaid;
        static bool saidStart, warnedHit, warnedShot;

        internal static void Init() {
            var cat = Cfg.Category("Telemetry");
            enabled = cat.CreateEntry("Enabled", true, description: "Server (not shared). Compiles every bow release and every hit of archery damage (what really came off the target's health, kills, damage to players) into small per-day statistics as they happen: one file per server run per server-local day, stats-v1-<day>-<session>.csv in UserData\\BluesArcheryOverhaul\\telemetry\\, rewritten whole about once a minute, for the balance graphs of tools/telemetry_report.py (python -I tools/telemetry_report.py). No per-shot file unless KeepRows is on. The game's thread only adds to numbers; a background thread writes. Re-read live. Off: nothing is recorded (one check per hit).");
            keepRows = cat.CreateEntry("KeepRows", false, description: "Server (not shared). For troubleshooting: also writes one row per shot and per hit (shots-v1-<day>.csv and hits-v1-<day>.csv, as before 0.19.5), which grow without end on a busy server (about 1 GB a month); the report can read them with --rows and check them against the statistics with --compare. Re-read live.");
            minFreeMb = cat.CreateEntry("MinFreeMb", 2048, description: "Server (not shared). The low-disk stop: while the drive that holds the telemetry folder has less than this many MB free, no telemetry file is written (the statistics wait in memory and are written once there is room; KeepRows' rows are dropped), and the log says so at most every 10 minutes, so weeks of logs can never fill the server's disk. Old days are never deleted: the balance report compiles all of them. 0 = no check. Re-read live.");
            // 0.19.7 (play-test of 0.19.6: the server was stopped by closing its window and the last statistics were not written):
            // that way of stopping skips every mod's OnApplicationQuit but still raises OnApplicationDefiniteQuit before the process
            // ends (MelonLoader 0.7.3; the notes at the top of ServerSettings.cs), so the last write also hangs there. A second
            // Shutdown finds nothing changed and writes nothing.
            try { MelonEvents.OnApplicationDefiniteQuit.Subscribe(ShutdownAtDefiniteQuit); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Telemetry: cannot see the server's stop ({e.Message}); the last minute of statistics is only written at a clean quit"); }
        }

        static void ShutdownAtDefiniteQuit() {
            try { Shutdown(); }
            catch (Exception e) { MelonLogger.Warning("[Archery] Telemetry: shutdown failed: " + e.Message); }
        }

        // [Telemetry] MinFreeMb as bytes for the writer's low-disk stop (0 = no check)
        static long MinFreeBytes => minFreeMb != null && minFreeMb.Value > 0 ? (long)minFreeMb.Value << 20 : 0L;

        static bool On => enabled != null && enabled.Value && NetworkSceneManager.IsServer;

        static bool KeepRows => keepRows != null && keepRows.Value;

        static double Now => Time.realtimeSinceStartup;

        static readonly string Version = typeof(Main).Assembly.GetCustomAttribute<MelonInfoAttribute>()?.Version ?? "?";

        static string Folder => Path.Combine(Cfg.Folder, "telemetry");

        // created on first use, on the server, so a client never makes the folder
        static TelemetryLog Log => log ??= new TelemetryLog(Folder, session);
        static TelemetryStats Stats => stats ??= new TelemetryStats(Folder, session, Version);

        // every shot and hit row: into the statistics, and to the row writer only with KeepRows
        static void Record(in ShotRow r) { Stats.Add(in r); if (KeepRows) Log.Add(in r); }
        static void Record(in HitRow r) { Stats.Add(in r); if (KeepRows) Log.Add(in r); }

        static void Once(ref bool said, string what) {
            if (said) return;
            said = true;
            MelonLogger.Warning("[Archery] Telemetry: " + what + " (said once)");
        }

        /// <summary>Server, from Draws.Release: one release as the server decided it (Skyfall casts and fizzles included).</summary>
        static readonly int T_Shot = Timing.Section("Telemetry.Shot");
        internal static void Shot(uint arrow, NetworkEntity? entity, Player? owner, Resolution r, Build build, int? ms, SkyCode sky, bool test) {
            if (!On) return;
            using var _t = Timing.Begin(T_Shot, arrow);
            try {
                int account = owner?.UserInfo?.Identifier ?? 0;
                string name = Builds.NameOf(owner);
                int locks = Claims.LocksOf(arrow, false).Count; // the server's kept Seeking Volley locks (none: 0)
                int shot = index.Next();
                index.Put(arrow, account, name, shot, Now);
                ShotRow row = ShotRow.Of(DateTime.UtcNow.Ticks, shot, arrow, name, account, r, ms, locks, sky, build, test, Version, Cfg.SharedChecksum);
                Describe(entity, out row.ArrowKind, out row.Power);
                Record(row);
            }
            catch (Exception e) { Once(ref warnedShot, "recording a shot failed: " + e.Message); }
        }

        static readonly List<ImpactTool> arrowTools = new List<ImpactTool>();
        static bool warnedArrow;

        /// <summary>What was shot (0.19.8; the owner: "Does the statistics keep track of what arrow is shot?"): the arrow's kind (its own
        /// name, then each attached part that deals damage: a crafted arrow's head) and its damage rating with no skill multiplier
        /// (Hits.Power; the game keeps a shot's multiplier apart, in ImpactTool.OneOffMultiplier). "?" and 0 when it cannot be read.</summary>
        static void Describe(NetworkEntity? entity, out string kind, out float power) {
            kind = "?"; power = 0f;
            try {
                if (entity == null) return;
                kind = TelemetryRules.KindName(entity.name);
                ImpactTool? root = entity.GetComponentInChildren<ImpactTool>(true);
                if (root == null) return;
                power = Hits.Power(root);
                foreach (ImpactTool t in Hits.Tools(root, arrowTools)) {
                    if (ReferenceEquals(t, root)) continue;
                    string part = TelemetryRules.KindName(t.name);
                    if (part != "?" && kind.Length + part.Length < 80 && kind.IndexOf(part, StringComparison.Ordinal) < 0) kind += " + " + part;
                }
            }
            catch (Exception e) { Once(ref warnedArrow, "reading what was shot failed: " + e.Message); }
        }

        /// <summary>Server, from HolyArrow.BeamCreated (the beam is made when the skill arms, before the release): which arrow the beam
        /// sacrifices, so its hit and its blast find that arrow's shot.</summary>
        internal static void Beam(uint beam, ImpactTool? arrowTool) {
            if (!On) return;
            try {
                Arrow? a = arrowTool != null ? Weight.ArrowOf(arrowTool) : null;
                NetworkEntity? e = a != null ? a.Entity : null;
                if (e != null) index.Beam(beam, e.Identifier);
            }
            catch (Exception ex) { Once(ref warnedShot, "linking a Holy beam to its arrow failed: " + ex.Message); }
        }

        /// <summary>Server, every frame from Main.OnUpdate (before the headless return): every FlushSeconds the shots older than
        /// TelemetryStats.CloseSeconds close and the rows (KeepRows) go to the writer's thread; every StatsSeconds the statistics are
        /// copied and written on the worker; the writers' problems and dropped rows are said at most once every WarnSeconds.</summary>
        internal static void Tick() {
            if (log == null && stats == null && !On) return;
            float now = Time.unscaledTime;
            if (now < nextFlush) return;
            nextFlush = now + FlushSeconds;
            if (!saidStart && On) {
                saidStart = true;
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Telemetry: compiling every shot and archery hit into statistics in {Folder} (session {session}, written about once a minute; [Telemetry] KeepRows {(KeepRows ? "on: the per-shot row files too" : "off: no per-shot files")}); graphs: python -I tools/telemetry_report.py");
            }
            if (stats != null) {
                stats.MinFreeBytes = MinFreeBytes; // re-read live
                stats.Tick(DateTime.UtcNow.Ticks);
                if (now >= nextStats && stats.Flush(true)) nextStats = now + StatsSeconds;
                string? said = stats.TakeProblem();
                if (said != null && now - lastStatsWarn >= WarnSeconds) { lastStatsWarn = now; MelonLogger.Warning("[Archery] Telemetry: statistics: " + said); }
            }
            if (log == null) return;
            log.MinFreeBytes = MinFreeBytes; // re-read live
            log.Flush(true);
            string? problem = log.TakeProblem();
            if (problem != null && now - lastProblemWarn >= WarnSeconds) { lastProblemWarn = now; MelonLogger.Warning("[Archery] Telemetry: " + problem); }
            long dropped = log.Dropped;
            if (dropped > droppedSaid && now - lastDropWarn >= WarnSeconds) {
                lastDropWarn = now; droppedSaid = dropped;
                MelonLogger.Warning($"[Archery] Telemetry: {dropped} rows dropped so far (the queue was full, a day's file reached its size limit, or the drive was low on space)");
            }
        }

        /// <summary>Server quit: every open shot closes and the statistics, then the last rows, are written here, each after any running
        /// write (at most 2 s).</summary>
        internal static void Shutdown() {
            if (stats != null) {
                try {
                    stats.CloseAll();
                    if (!stats.WaitIdle(2000) || !stats.Flush(false)) MelonLogger.Warning("[Archery] Telemetry: the statistics writer was still busy at quit; the last minute is lost");
                    string? problem = stats.TakeProblem();
                    if (problem != null) MelonLogger.Warning("[Archery] Telemetry: statistics: " + problem);
                }
                catch (Exception e) { MelonLogger.Warning("[Archery] Telemetry: writing the last statistics failed: " + e.Message); }
            }
            if (log == null) return;
            try {
                if (!log.WaitIdle(2000) || !log.Flush(false)) MelonLogger.Warning("[Archery] Telemetry: the writer was still busy at quit; the last rows are lost");
                string? problem = log.TakeProblem();
                if (problem != null) MelonLogger.Warning("[Archery] Telemetry: " + problem);
            }
            catch (Exception e) { MelonLogger.Warning("[Archery] Telemetry: writing the last rows failed: " + e.Message); }
        }

        // ---------------------------------------------------------------- the hit hook

        struct Seen {
            public bool On;
            public float Before;
            public DealtBy Source;
            public uint Arrow, Beam;
            public SpectralMix Mix;
            public int Account;
            public string? Shooter;
        }

        static uint WielderId(ImpactTool tool) {
            try { NetworkEntity e = tool.Wielder; return e != null ? e.Identifier : 0u; }
            catch { return 0u; } // a tool with no entity: not a beam
        }

        [HarmonyPatch(typeof(HealthObject), nameof(HealthObject.ReceiveDamage), new[] { typeof(float), typeof(float), typeof(DamageData) })]
        static class Damage {
            static readonly int TPre = Timing.Section("patch HealthObject.ReceiveDamage / Telemetry.Damage (prefix)");
            static readonly int TPost = Timing.Section("patch HealthObject.ReceiveDamage / Telemetry.Damage (postfix)");

            static void Prefix(HealthObject __instance, DamageData impact, out Seen __state) {
                __state = default;
                if (!On) return;
                using var _t = Timing.Begin(TPre);
                try {
                    float before = __instance.Health;
                    if (!(before > 0f)) return; // already dead: the game deals nothing
                    ImpactHit? hit = impact as ImpactHit;
                    ImpactHitWithTool? withTool = impact as ImpactHitWithTool;
                    ImpactTool? tool = withTool != null ? withTool.Tool : null;
                    Arrow? arrow = tool != null ? Weight.ArrowOf(tool) : null;
                    uint beam = 0u;
                    if (index.HasBeams) {
                        if (tool != null) { if (arrow == null) beam = WielderId(tool); }   // a beam has no pickup: its wielder is its own root
                        else if (hit != null && hit.Wielder != null) beam = hit.Wielder.Identifier; // the blast's ImpactHit carries the beam
                    }
                    bool knownBeam = index.IsBeam(beam);
                    if (!TelemetryRules.TryDecide(HitScope.Open, HitScope.Source, Hits.Dealing, tool != null, arrow != null, knownBeam, out DealtBy source)) return;
                    PlayerController? pc = hit != null ? hit.PlayerWielder : null;
                    Player? who = pc != null ? pc.NetworkPlayer : withTool != null ? withTool.HittingPlayer : null;
                    NetworkEntity? arrowEntity = arrow != null ? arrow.Entity : null;
                    __state = new Seen {
                        On = true, Before = before, Source = source,
                        Arrow = HitScope.Open && HitScope.Arrow != 0u ? HitScope.Arrow : arrowEntity != null ? arrowEntity.Identifier : 0u,
                        Beam = knownBeam ? beam : 0u,
                        Mix = HitScope.Open ? HitScope.Mix : default,
                        Account = who?.UserInfo?.Identifier ?? 0,
                        Shooter = who != null ? Builds.NameOf(who) : null,
                    };
                }
                catch (Exception e) { __state = default; Once(ref warnedHit, "reading a hit failed: " + e.Message); }
            }

            static void Postfix(HealthObject __instance, float healthDamage, float crippleDamage, Seen __state) {
                if (!__state.On) return;
                using var _t = Timing.Begin(TPost);
                try {
                    float after = __instance.Health;
                    float dealt = TelemetryRules.Dealt(__state.Before, after);
                    bool killed = TelemetryRules.Killed(__state.Before, after);
                    double now = Now;
                    // the shot: by its arrow (a real arrow's own hit only soon after its release, anything dealt for it later within
                    // LateSeconds), else by the Holy beam, else by the shooter's latest shot, else none (0)
                    ShotRef shot = default;
                    bool found = __state.Arrow != 0u && index.TryArrow(__state.Arrow, now, __state.Source == DealtBy.Arrow ? ShotIndex.RealArrowSeconds : ShotIndex.LateSeconds, out shot);
                    if (!found && __state.Beam != 0u) found = index.TryBeam(__state.Beam, now, out shot);
                    if (!found && __state.Account != 0) found = index.TryPlayer(__state.Account, now, out shot);
                    var row = new HitRow {
                        Utc = DateTime.UtcNow.Ticks, Shot = found ? shot.Shot : 0, Player = (found ? shot.Player : __state.Shooter) ?? "?",
                        Source = __state.Source, TargetId = Targets.IdOf(__instance),
                        Raw = healthDamage, Dealt = dealt, Cripple = crippleDamage, Killed = killed, MaxHealth = __instance.MaxHealth,
                    };
                    if (__instance is PlayerHealth ph) {
                        PlayerController p = ph.Player;
                        row.Target = Builds.NameOf(p != null ? p.NetworkPlayer : null);
                        row.Victim = Victim.Player;
                    }
                    else {
                        row.Target = TelemetryRules.CleanName(Targets.RootOf(__instance).name);
                        row.Victim = __instance.IsCombatant ? Victim.Creature : Victim.Thing;
                    }
                    SpectralMix mix = __state.Mix;
                    float total = mix.Total;
                    if (!(total > 0f)) { Record(row); return; }
                    // a batched spectral hit: one row per kind of arrow in it, each its share; the kill on the largest share only
                    DealtBy largest = mix.Largest;
                    Part(row, DealtBy.Ghost, mix.Ghost / total, killed && largest == DealtBy.Ghost);
                    Part(row, DealtBy.Pellet, mix.Pellet / total, killed && largest == DealtBy.Pellet);
                    Part(row, DealtBy.Needle, mix.Needle / total, killed && largest == DealtBy.Needle);
                    Part(row, DealtBy.Core, mix.Core / total, killed && largest == DealtBy.Core);
                }
                catch (Exception e) { Once(ref warnedHit, "recording a hit failed: " + e.Message); }
            }

            static void Part(HitRow row, DealtBy kind, float share, bool killed) {
                if (!(share > 0f)) return;
                row.Source = kind; row.Raw *= share; row.Dealt *= share; row.Cripple *= share; row.Killed = killed;
                Record(row);
            }
        }
    }
}
