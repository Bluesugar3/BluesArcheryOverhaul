using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Alta.Impact;
using Alta.Networking;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Balance telemetry, server only (the owner, 2026-11-19: "Have it keep logs of all arrows shot, skills used and damage delt, so after
    // a day of playtest we can take all that information and graph it to visualize if some skills are to op"; design
    // docs/superpowers/specs/2026-11-19-balance-telemetry-design.md). Every release the server decides (Draws.Release -> Shot) and every
    // hit of archery damage go to UserData\QuickshotRing\telemetry\ as two CSV files a day; tools/telemetry_report.py draws them.
    //
    // Every hit on the server passes HealthObject.ReceiveDamage (PlayerHealth reaches it through base, after the PvP gate, invincibility
    // and its multipliers), so one prefix/postfix sees them all and reads what really came off the health bar. The mod's own damage
    // sites open a HitScope (TelemetryRules.cs) naming their source; without one a tool on an arrow is the real arrow, a tool or wielder
    // that is a known Holy beam is the beam or its blast, and anything else is not archery and is left alone. The hooks only append a
    // struct; TelemetryLog writes on a background thread every FlushSeconds (the owner's condition: no performance issues).
    internal static class Telemetry {
        const float FlushSeconds = 5f, WarnSeconds = 600f;

        static MelonPreferences_Entry<bool>? enabled;
        static TelemetryLog? log;
        static readonly ShotIndex index = new ShotIndex();
        static readonly string session = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        static float nextFlush, lastProblemWarn = float.MinValue, lastDropWarn = float.MinValue;
        static long droppedSaid;
        static bool saidStart, warnedHit, warnedShot;

        internal static void Init() {
            var cat = Cfg.Category("Telemetry");
            enabled = cat.CreateEntry("Enabled", true, description: "Server (not shared). Records every bow release and every hit of archery damage (what really came off the target's health, kills, damage to players) to UserData\\QuickshotRing\\telemetry\\ as shots-v1-<day>.csv and hits-v1-<day>.csv, one pair per server-local day, for the balance graphs of tools/telemetry_report.py (python -I tools/telemetry_report.py). The game's thread only queues a small record; a background thread writes every 5 s. Re-read live. Off: nothing is recorded (one check per hit).");
        }

        static bool On => enabled != null && enabled.Value && NetworkSceneManager.IsServer;

        static double Now => Time.realtimeSinceStartup;

        static readonly string Version = typeof(Main).Assembly.GetCustomAttribute<MelonInfoAttribute>()?.Version ?? "?";

        // created on first use, on the server, so a client never makes the folder
        static TelemetryLog Log => log ??= new TelemetryLog(Path.Combine(Cfg.Folder, "telemetry"), session);

        static void Once(ref bool said, string what) {
            if (said) return;
            said = true;
            MelonLogger.Warning("[Archery] Telemetry: " + what + " (said once)");
        }

        /// <summary>Server, from Draws.Release: one release as the server decided it (Skyfall casts and fizzles included).</summary>
        static readonly int T_Shot = Timing.Section("Telemetry.Shot");
        internal static void Shot(uint arrow, Player? owner, Resolution r, Build build, int? ms, SkyCode sky, bool test) {
            if (!On) return;
            using var _t = Timing.Begin(T_Shot, arrow);
            try {
                int account = owner?.UserInfo?.Identifier ?? 0;
                string name = Builds.NameOf(owner);
                int locks = Claims.LocksOf(arrow, false).Count; // the server's kept Seeking Volley locks (none: 0)
                int shot = index.Next();
                index.Put(arrow, account, name, shot, Now);
                Log.Add(ShotRow.Of(DateTime.UtcNow.Ticks, shot, arrow, name, account, r, ms, locks, sky, build, test, Version, Cfg.SharedChecksum));
            }
            catch (Exception e) { Once(ref warnedShot, "recording a shot failed: " + e.Message); }
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

        /// <summary>Server, every frame from Main.OnUpdate (before the headless return): every FlushSeconds the queued rows go to the
        /// writer's thread; the writer's problems and dropped rows are said at most once every WarnSeconds.</summary>
        internal static void Tick() {
            if (log == null && !On) return;
            float now = Time.unscaledTime;
            if (now < nextFlush) return;
            nextFlush = now + FlushSeconds;
            if (!saidStart && On) {
                saidStart = true;
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Telemetry: recording every shot and archery hit to {Path.Combine(Cfg.Folder, "telemetry")} (session {session}); graphs: python -I tools/telemetry_report.py");
            }
            if (log == null) return;
            log.Flush(true);
            string? problem = log.TakeProblem();
            if (problem != null && now - lastProblemWarn >= WarnSeconds) { lastProblemWarn = now; MelonLogger.Warning("[Archery] Telemetry: " + problem); }
            long dropped = log.Dropped;
            if (dropped > droppedSaid && now - lastDropWarn >= WarnSeconds) {
                lastDropWarn = now; droppedSaid = dropped;
                MelonLogger.Warning($"[Archery] Telemetry: {dropped} rows dropped so far (the queue was full or a day's file reached its size limit)");
            }
        }

        /// <summary>Server quit: the last rows are written here, after any running write (at most 2 s).</summary>
        internal static void Shutdown() {
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
                    if (!(total > 0f)) { Log.Add(row); return; }
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
                Log.Add(row);
            }
        }
    }
}
