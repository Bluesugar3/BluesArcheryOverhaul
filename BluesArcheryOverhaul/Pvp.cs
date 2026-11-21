using System;
using Alta.Impact;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Play-test 7, PvP (the user: "plain arrows do nothing to other players"). The rules are PvpRule (Rules.cs, tested).
    //   a. The game never lets a shot or thrown item hurt a player on a dedicated server: the server leaves a flying arrow's hit on
    //      a player to the victim's own client (ImpactDetector.OnCollisionEnter / IsForcedClientSideImpact), then refuses that
    //      client's report (ImpactDetector.SyncImpact -> ShouldRefuseSyncedHit: the arrow's last interactor is the shooter, not
    //      the sender). The server now also accepts a player's report of THEIR OWN body being hit by a flying Arrow that someone
    //      else shot ([PvP] ArrowsHurtPlayers, server only). Nothing else is accepted: a report about another body is still
    //      refused by the game before this, a held tool, a non-arrow or an arrow nobody shot stays refused. The game rebuilds the
    //      hit (GenerateImpactOnDetector(...).Hit()) as an ImpactHitWithTool by the shooter, so IsPVPEnabled, PVPMultiplier,
    //      PVPCrippleMultiplier, PlayerDamageTakenMultiplier and spawn invincibility all still apply. The client supplies the
    //      hit's effectiveness: it can only lower its own damage.
    //   b. The mod's own hits (Hits.Apply, Hits.Copy, Hits.Spectral: ghosts, pellets, needles, heavy arrows, the Quickdraw 4
    //      line, Ricochet's jumps, Shatter's blast) land on a player only when the server's PvP is on, at [PvP] ModHitShare of a
    //      creature's, never on the shooter ([PvP] ModHitsHurtPlayers, shared).
    //   c. Holy Arrow's blast (HolyArrow.Blast) follows the server's PvP setting and multipliers.
    //   Fix batch D (fixA-review I-1): a report is accepted only for an Arrow with a damage tool that no hand and no dock holds,
    //   shot from a bow (BowString.OnShoot on the server, Draws.ShotReset) within [PvP] ReportSeconds, with the server's PvP on,
    //   and once per arrow per shot (ShotReportLedger, Rules.cs, tested). After the accepted hit the arrow's one-off multiplier is
    //   spent on every one of its tools, as the game does after a hit it processes itself (ImpactTool.GenerateHit), so a charged
    //   arrow cannot land its multiplier twice. Ricochet's jumps pick players only with [PvP] JumpsHitPlayers (I-5, default off).
    internal static class Pvp {
        static MelonPreferences_Entry<bool> arrowsHurt = null!, modHits = null!, jumpsHitPlayers = null!, blastHitsPlayers = null!;
        static MelonPreferences_Entry<float> modShare = null!, reportSeconds = null!;
        static bool acceptedLogged, readFailed;
        static int refusedLogged;
        const int RefusedLogCap = 8;
        static readonly ShotReportLedger shots = new ShotReportLedger();

        internal static void Init() {
            var cat = Cfg.Category("PvP");
            arrowsHurt = cat.CreateEntry("ArrowsHurtPlayers", true, description: "Server. A player's report that another player's arrow hit their own body is accepted (the game refuses it, so no arrow ever hurt a player), only for a loose arrow shot from a bow within ReportSeconds, with the server's PvP on, once per arrow per shot. The server's IsPVPEnabled, PVPMultiplier and spawn invincibility still apply. Off: as before.");
            reportSeconds = cat.CreateEntry("ReportSeconds", PvpRule.DefaultReportSeconds, description: "Server. A report of an arrow hitting the reporter counts only within this many seconds of that arrow being shot from a bow.");
            jumpsHitPlayers = cat.CreateEntry("JumpsHitPlayers", PvpRule.DefaultJumpsHitPlayers, description: "Server. Ricochet's jumps may land on players (with the server's PvP on and ModHitsHurtPlayers). Off: jumps pick creatures only (they are not aimed).");
            blastHitsPlayers = cat.CreateEntry("BlastHitsPlayers", PvpRule.DefaultBlastHitsPlayers, description: "Server. Shatter's blast may land on players (with the server's PvP on and ModHitsHurtPlayers). Off: the blast catches creatures only (it fires on the first creature hit of every release, and its cone is where a friend fighting that creature stands).");
            modHits = Cfg.Shared(cat.CreateEntry("ModHitsHurtPlayers", true, description: "The mod's own hits (ghost arrows, pellets, needles, heavy arrows, the Quickdraw 4 line, Ricochet's jumps, Shatter's blast, Holy Arrow's blast) land on other players, only when the server's PvP is enabled. Off: they never do."));
            modShare = Cfg.Shared(cat.CreateEntry("ModHitShare", PvpRule.DefaultModHitShare, description: "The share of what a creature would take that the mod's own hits deal to a player (the game's PVPMultiplier applies on top)."));
        }

        /// <summary>Server, every arrow shot from a bow (Draws.ShotReset): a new shot that a player may report once.</summary>
        internal static void Shot(uint arrowId) { if (NetworkSceneManager.IsServer && arrowId != 0u) shots.Shot(arrowId, Time.time); }

        /// <summary>Server: Ricochet's jumps may pick players right now (fixA-review I-5).</summary>
        internal static bool JumpsLandOnPlayers => PvpRule.JumpsTargetPlayers(ModHitsLand, jumpsHitPlayers != null && jumpsHitPlayers.Value);

        /// <summary>Server: Shatter's blast may catch players right now (fixF, final review I-2).</summary>
        internal static bool BlastLandsOnPlayers => PvpRule.BlastTargetsPlayers(ModHitsLand, blastHitsPlayers != null && blastHitsPlayers.Value);

        /// <summary>Server: the server's own PvP setting (read live, as the game does on every hit).</summary>
        internal static bool ServerPvp {
            get {
                try {
                    ServerHandler h = ServerHandler.Current;
                    return h != null && h.ServerConfig != null && h.ServerConfig.Settings != null && h.ServerConfig.Settings.IsPVPEnabled;
                }
                catch (Exception e) {
                    if (!readFailed) { readFailed = true; MelonLogger.Warning($"[Archery] PvP: cannot read the server's PvP setting ({e.Message}); the mod's hits skip players"); }
                    return false;
                }
            }
        }

        static void Multipliers(out float health, out float cripple) {
            health = cripple = 0f;
            try {
                ServerHandler h = ServerHandler.Current;
                if (h == null || h.ServerConfig == null || h.ServerConfig.Settings == null) return;
                health = h.ServerConfig.Settings.PVPMultiplier;
                cripple = h.ServerConfig.Settings.PVPCrippleMultiplier;
            }
            catch { }
        }

        /// <summary>The archer behind a hand-made hit's tool: the arrow's last interactor.</summary>
        internal static PlayerController? ShooterOf(ImpactTool? tool) {
            if (tool == null) return null;
            Pickup p = tool.HasPickup ? tool.Pickup : tool.GetComponentInParent<Pickup>();
            if (p == null) { Arrow a = tool.GetComponentInParent<Arrow>(); if (a != null) p = a.GetComponent<Pickup>(); }
            return p != null && p.LastPlayerInteractor != null ? p.LastPlayerInteractor : null;
        }

        static bool IsShooter(PlayerController? shooter, PlayerHealth ph) => shooter != null && ph != null && ph.Player != null && ph.Player == shooter;

        /// <summary>Server: the share of a creature's hit the mod's own hit from <paramref name="tool"/> deals to this player (0 = none).</summary>
        internal static float PlayerShare(ImpactTool? tool, PlayerHealth ph) => PlayerShare(ShooterOf(tool), ph);

        /// <summary>As above with the shooter already known (fixA-review M-6: Hits.Spectral passes its own shooter).</summary>
        internal static float PlayerShare(PlayerController? shooter, PlayerHealth ph) {
            if (!NetworkSceneManager.IsServer || ph == null) return 0f;
            return PvpRule.ModHitShare(ServerPvp, modHits == null || modHits.Value, IsShooter(shooter, ph), modShare != null ? modShare.Value : PvpRule.DefaultModHitShare);
        }

        /// <summary>Server: whether players count at all for the mod's own hits right now (lets callers skip collecting them).</summary>
        internal static bool ModHitsLand => NetworkSceneManager.IsServer && ServerPvp && (modHits == null || modHits.Value) && (modShare == null || modShare.Value > 0f);

        /// <summary>Server, Holy Arrow's blast on a player: false = nothing; else its health and cripple factors.</summary>
        internal static bool HolyBlast(PlayerController? shooter, PlayerHealth ph, out float health, out float cripple) {
            Multipliers(out float pm, out float pc);
            return PvpRule.HolyBlast(ServerPvp, modHits == null || modHits.Value, IsShooter(shooter, ph), pm, pc, out health, out cripple);
        }

        // ---------------------------------------------------------------- a. the victim's own report of a flying arrow

        [HarmonyPatch(typeof(ImpactDetector), nameof(ImpactDetector.SyncImpact))]
        static class SelfReport {
            static readonly int TPrefix = Timing.Section("patch ImpactDetector.SyncImpact / Pvp.SelfReport prefix");
            static readonly int TFinalizer = Timing.Section("patch ImpactDetector.SyncImpact / Pvp.SelfReport finalizer");
            /// <summary>Set for the one SyncImpact call being read on the server: the sender is the body's own player.</summary>
            [ThreadStatic] internal static bool OwnBody;
            [ThreadStatic] internal static string? Who;
            [ThreadStatic] internal static Arrow? Accepted;   // the arrow whose report this call accepted: its multiplier is spent after the hit

            static void Prefix(ImpactDetector __instance, IPlayer player) {
                using var _t = Timing.Begin(TPrefix);
                OwnBody = false; Who = null; Accepted = null;
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || player == null) return;
                    if (!(__instance.HealthTarget is PlayerHealth ph) || ph.Player == null || ph.Player.NetworkPlayer == null) return;
                    OwnBody = ReferenceEquals(ph.Player.NetworkPlayer, player);
                    Who = Builds.NameOf(player);
                }
                catch { OwnBody = false; }
            }

            // After the game dealt the accepted hit (or threw): the arrow's one-off multiplier is spent on every tool it has, as the
            // game does after a hit it processes itself, so a charged arrow cannot land it twice (two body parts, two reports).
            static void Finalizer() {
                using var _t = Timing.Begin(TFinalizer);
                Arrow? a = Accepted;
                OwnBody = false; Who = null; Accepted = null;
                if (a == null) return;
                try {
                    ImpactTool root = a.ImpactTool;
                    if (root != null) foreach (ImpactTool t in Hits.Tools(root)) t.OneOffMultiplier = 1f;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] PvP: spending the arrow's multiplier after an accepted report failed: {e.Message}"); }
            }
        }

        [HarmonyPatch(typeof(ImpactDetector), "ShouldRefuseSyncedHit")]
        static class AllowShotAtMe {
            static readonly int T = Timing.Section("patch ImpactDetector.ShouldRefuseSyncedHit / Pvp.AllowShotAtMe");
            static void Postfix(NetworkEntity toolEntity, IPlayer fromPlayer, ref bool __result) {
                using var _t = Timing.Begin(T);
                if (!__result || !NetworkSceneManager.IsServer) return;
                try {
                    if (!SelfReport.OwnBody || toolEntity == null) return; // not a report about the sender's own body: the game decides
                    Arrow arrow = toolEntity.GetComponentInParent<Arrow>();
                    NetworkEntity? root = arrow != null ? arrow.Entity : null;
                    Pickup p = root != null && root.CommonPickup != null ? root.CommonPickup : toolEntity.CommonPickup;
                    PlayerController? shooter = p != null ? p.LastPlayerInteractor : null;
                    uint id = root != null ? root.Identifier : 0u;
                    var facts = new OwnReport {
                        Enabled = arrowsHurt == null || arrowsHurt.Value, GameRefused = __result, OwnBody = true,
                        IsArrow = arrow != null, HasTool = toolEntity.CommonImpactTool != null,
                        Held = p != null && p.IsInteractedWith, Docked = p != null && p.IsDocked,
                        ShotBySomeoneElse = shooter != null, ServerPvp = ServerPvp,
                        SinceShot = id != 0u ? shots.Since(id, Time.time) : null, AcceptedThisShot = id != 0u && shots.Accepted(id),
                    };
                    string? refusal = PvpRule.ReportRefusal(facts, reportSeconds != null ? reportSeconds.Value : PvpRule.DefaultReportSeconds);
                    string what = $"'{SelfReport.Who ?? "?"}' reported '{toolEntity.name}' (arrow {id}, shot by '{(shooter != null ? Builds.NameOf(shooter.NetworkPlayer) : "nobody")}') hitting their own body";
                    if (refusal == null) {
                        __result = false;
                        shots.Accept(id);
                        SelfReport.Accepted = arrow;
                        if (!acceptedLogged) { acceptedLogged = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] PvP: accepted: {what}; the game's PvP setting and multipliers decide the damage, the arrow's multiplier is spent after it (said once a session)"); }
                    }
                    else if (refusedLogged < RefusedLogCap) {
                        refusedLogged++;
                        if (Logs.Summary) MelonLogger.Msg($"[Archery] PvP: still refused: {what}: {refusal}{(refusedLogged == RefusedLogCap ? " (no more of these lines this session)" : "")}");
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] PvP: own-report check failed, the game's refusal stands: {e.Message}"); }
            }
        }
    }
}
