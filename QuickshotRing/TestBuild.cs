using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Alta.Networking.Scripts.Player;
using MelonLoader;

namespace QuickshotRing {
    // The server-only test build, for play-tests: [TestBuild] Player is treated as also owning [TestBuild] Orbs.
    // Nothing is saved: the orbs only enter the build (Builds.ServerBuild) and, for orbs that have a slot in the
    // tree, the game's own skill level, so the game's skills (Slowdraw, Two-Shot, ...) run for them too.
    // [TestBuild] Without takes orbs out of the archery build only (never the game's skill levels).
    // Clients ignore these settings.
    internal static class TestBuild {
        static MelonPreferences_Entry<string> player = null!, orbs = null!, without = null!;
        static MelonPreferences_Entry<int> freePoints = null!;
        static MelonPreferences_Entry<string> menuOnStart = null!;
        static bool menuTried;
        static AccessTools.FieldRef<PathExperience, uint>? pointsField, levelField;
        static bool loggedPoints, loggedClamp;
        static List<Orb> parsed = new List<Orb>(), parsedWithout = new List<Orb>();
        static string configured = "";
        static bool wasActive, warnedLevels;
        static readonly Orb[] none = new Orb[0];
        // The slots whose skill level this switch added, per manager; taken back when no longer wanted.
        static readonly Dictionary<PlayerProgressionManager, List<ProgressionSlot>> granted = new Dictionary<PlayerProgressionManager, List<ProgressionSlot>>();
        static AccessTools.FieldRef<PlayerProgressionManager, Dictionary<uint, int>>? skillLevel;

        // Init runs before the scene says whether this is the server; a dedicated server is headless.
        static bool ServerSide => NetworkSceneManager.IsServer || ApplicationManager.IsHeadless;

        internal static void Init() {
            var cat = Cfg.Category("TestBuild");
            player = cat.CreateEntry("Player", "", description: "Server only, for play-tests. The username that gets the extra orbs below. Empty = off.");
            orbs = cat.CreateEntry("Orbs", "", description: "Server only, for play-tests. Orb names separated by ';' (Quick1-3, Quickdraw4, Volley, FullVolley, Burst, Charge1-3, HolyArrow, Gatherer (QuiverGather is another name for it), AutoDock, Ricochet, SeekingVolley, Shatter) the player is treated as owning. Never saved; empty = off.");
            without = cat.CreateEntry("Without", "", description: "Server only. Orb names separated by ';' that the archery build treats as NOT owned (the game's own skill levels are untouched). For trying snaps while Quickdraw 4 is owned. Empty = off.");
            freePoints = cat.CreateEntry("FreePoints", 0, description: "Server only, for play-tests. Keeps the player's unspent Ranged skill points topped up to this number, so orbs can be bought freely (the slot cap still applies). Always below the game's banked-points cap (the progression line in the log names it; 3 on this install): at or above the cap the game pays that path no experience, so a number at or above it tops up to one below the cap (2 here). A balance already higher is never lowered. This DOES change the save. 0 = off.");
            menuOnStart = cat.CreateEntry("MenuOnStart", "", description: "Client only, play-tests only. The full path of a .ps1 (tools\\TestBuildMenu.ps1) to open when this game starts. Empty = off. Never used on a server.");
            Cfg.Reloaded += Reload;
            Parse();
        }

        /// <summary>Client, once per game start (after Cfg.Ready; never on reload): opens [TestBuild] MenuOnStart's .ps1 window.
        /// Any failure is one log line.</summary>
        internal static void OpenMenu() {
            if (menuTried) return;
            menuTried = true;
            try {
                if (ApplicationManager.IsHeadless || NetworkSceneManager.IsServer) return;
                string path = (menuOnStart.Value ?? "").Trim();
                if (path.Length == 0) return;
                if (!string.Equals(System.IO.Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase)) { MelonLogger.Warning($"[Archery] TEST BUILD: MenuOnStart '{path}' is not a .ps1; not opened"); return; }
                if (!System.IO.File.Exists(path)) { MelonLogger.Warning($"[Archery] TEST BUILD: MenuOnStart '{path}' does not exist; not opened"); return; }
                StartMenu(path);
                MelonLogger.Msg($"[Archery] TEST BUILD: opened the test menu '{path}' (MenuOnStart)");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: MenuOnStart could not open the menu: {e.GetType().Name}: {e.Message}"); }
        }

        // Its own method so a missing Process type fails here, inside OpenMenu's catch.
        static void StartMenu(string path) {
            var info = new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{path}\"") {
                UseShellExecute = false, CreateNoWindow = true,
            };
            System.Diagnostics.Process.Start(info)?.Dispose(); // not waited for
        }

        static void Parse() {
            configured = (player.Value ?? "").Trim();
            parsed = TestBuildRule.Parse(orbs.Value, out List<string> unknown);
            parsedWithout = TestBuildRule.Parse(without.Value, out List<string> unknownWithout);
            unknown.AddRange(unknownWithout);
            // An orb whose [Tree] <Orb>Orb is off is off everywhere: the test build cannot lend it either.
            List<Orb> off = parsed.Where(o => NewOrbs.All.Contains(o) && !Tree.OrbOn(o)).ToList();
            parsed.RemoveAll(off.Contains);
            if (!ServerSide) return;
            if (off.Count > 0)
                MelonLogger.Warning($"[Archery] TEST BUILD: {Names(off)} not lent: switched off ([Tree] <Orb>Orb = false); turn it on on the server and every client first");
            if (unknown.Count > 0)
                MelonLogger.Warning($"[Archery] TEST BUILD: unknown orb name(s) {string.Join(", ", unknown.ToArray())}; known: {string.Join(", ", Enum.GetNames(typeof(Orb)).Where(n => n != nameof(Orb.None)).ToArray())}");
            bool active = configured.Length > 0 && (parsed.Count > 0 || parsedWithout.Count > 0);
            if (active)
                MelonLogger.Warning($"[Archery] TEST BUILD ACTIVE: '{configured}' is treated as also owning {Names(parsed)} and as not owning {Names(parsedWithout)}. Nothing is saved. Clear [TestBuild] Orbs and Without in the server's QuickshotRing.cfg to stop.");
            else if (wasActive) { if (Logs.Summary) MelonLogger.Msg("[Archery] TEST BUILD: off"); }
            wasActive = active;
        }

        static readonly int T_Reload = Timing.Section("TestBuild.Reload");
        static void Reload() {
            using var _t = Timing.Begin(T_Reload);
            try {
                Parse();
                Builds.ReloadAll(new List<PlayerProgressionManager>(granted.Keys));
                if (NetworkSceneManager.IsServer && freePoints.Value > 0)
                    // G2: the players' own managers instead of a scene search. TopUp needs pm.Player (only a player's manager is ever
                    // topped up), and FindObjectsOfType returned only managers on active objects, hence the activeInHierarchy test.
                    foreach (IPlayer ip in Player.AllPlayers)
                        if (ip is Player pl && pl.ProgressionManager != null && pl.ProgressionManager.gameObject.activeInHierarchy) TopUp(pl.ProgressionManager);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: reload failed: {e}"); }
        }

        /// <summary>Server: the orbs the test build adds for this player (empty when it does not apply).</summary>
        internal static IReadOnlyList<Orb> ExtraFor(string? playerName) =>
            NetworkSceneManager.IsServer && parsed.Count > 0 && TestBuildRule.AppliesTo(configured, playerName) ? (IReadOnlyList<Orb>)parsed : none;

        /// <summary>Server: the orbs the test build takes out of this player's archery build (empty when it does not apply).
        /// Only the build: the game's skill levels, the one-ultimate rule (ServerOwnedUltimates) and Sync ignore it.</summary>
        internal static IReadOnlyList<Orb> WithoutFor(string? playerName) =>
            NetworkSceneManager.IsServer && parsedWithout.Count > 0 && TestBuildRule.AppliesTo(configured, playerName) ? (IReadOnlyList<Orb>)parsedWithout : none;

        /// <summary>[TestBuild] Player, trimmed (empty = off). The test tools (TestTools.cs) act only for this player.</summary>
        internal static string Configured => configured;

        internal static bool ActiveFor(string? playerName) => ExtraFor(playerName).Count > 0 || WithoutFor(playerName).Count > 0;

        static string Names(List<Orb> list) => list.Count > 0 ? string.Join("+", list.Select(o => o.ToString()).ToArray()) : "nothing";

        static Dictionary<uint, int>? Levels(PlayerProgressionManager pm) {
            try {
                skillLevel ??= AccessTools.FieldRefAccess<PlayerProgressionManager, Dictionary<uint, int>>("skillLevel");
                return skillLevel(pm);
            }
            catch (Exception e) {
                if (!warnedLevels) { warnedLevels = true; MelonLogger.Error($"[Archery] TEST BUILD: cannot reach the game's skill levels; the test build only changes the archery build: {e.Message}"); }
                return null;
            }
        }

        /// <summary>Server: keep the test player's unspent Ranged points at [TestBuild] FreePoints, so every orb can be
        /// bought in a play-test. Runs at each build refresh (load, buy, sell, try) and on a settings reload. The points
        /// are written to the game's own counter (past its banked-points cap) and the game's own "points changed" callback
        /// tells the shrine, so this one does change the save.</summary>
        static void TopUp(PlayerProgressionManager pm) {
            try {
                int asked = freePoints.Value;
                if (asked <= 0 || !NetworkSceneManager.IsServer || pm == null || configured.Length == 0) return;
                // G3 (diagnosis 9), fix1 (final-G34 M-1): always below the game's banked-points cap (at or above it the game pays this
                // path no experience at all); a balance already higher is left alone (below)
                uint cap = Alta.Global.GlobalSettings<ProgressionSettings>.Instance.MaxFreePoints;
                int want = TestBuildRule.TopUpTo(asked, cap);
                if (want < asked && !loggedClamp) { loggedClamp = true; MelonLogger.Warning($"[Archery] TEST BUILD: FreePoints {asked} is at or above the game's banked-points cap of {cap}; topping up to {want} (at or above the cap the game pays no Ranged experience)"); }                string name = Builds.NameOf(pm.Player);
                if (!TestBuildRule.AppliesTo(configured, name) || !Tree.Ready) return;
                if (pm.PathExperiences == null || !pm.PathExperiences.TryGetValue(Tree.RangedPath, out PathExperience exp) || exp == null) return;
                pointsField ??= AccessTools.FieldRefAccess<PathExperience, uint>("freePoints");
                levelField ??= AccessTools.FieldRefAccess<PathExperience, uint>("currentLevel");
                uint had = pointsField(exp);
                if (had >= (uint)want) return;
                pointsField(exp) = (uint)want;
                // H4 (the range review's I-1, the same path here): the points come with the levels they stand for, so a later sale at
                // the shrine (the game lowers the unsigned level by the orb's cost) can never wrap the level
                uint level = levelField(exp), newLevel = PointsRule.Level(level, TestTools.OwnedCost(exp), (uint)want);
                if (newLevel != level) levelField(exp) = newLevel;
                Traverse.Create(exp).Field("UpdatedPoints").GetValue<Action<ProgressionPath>>()?.Invoke(exp.Path);
                if (!loggedPoints) { loggedPoints = true; MelonLogger.Warning($"[Archery] TEST BUILD: FreePoints is on: '{name}' keeps {want} unspent Ranged skill point(s). This changes the save. Set [TestBuild] FreePoints = 0 to stop."); }
                if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST BUILD: topped up '{name}' from {had} to {want} unspent Ranged point(s)");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: could not top up skill points: {e.Message}"); }
        }

        internal static int Holding(PlayerProgressionManager pm) {
            PlayerToolBasedSkillHelper helper = pm.GetComponent<PlayerToolBasedSkillHelper>();
            return helper != null ? helper.HeldInteractables.Count : 0;
        }

        /// <summary>Server: give the player's game skills the levels the test build wants, beyond what they really own,
        /// and take back levels it no longer wants. The practising orb is deliberately not subtracted: the build is
        /// set-based, and re-granting at EndPractice would re-run Unlock on a bow in hand (each input fires twice).</summary>
        static readonly int T_Sync = Timing.Section("TestBuild.Sync");
        internal static void Sync(PlayerProgressionManager pm, IEnumerable<Orb> realOwned) {
            using var _t = Timing.Begin(T_Sync);
            if (!NetworkSceneManager.IsServer || pm == null) return;
            TopUp(pm);
            string name = Builds.NameOf(pm.Player);
            var owned = new HashSet<Orb>(realOwned);
            var wanted = new List<ProgressionSlot>();
            foreach (Orb o in ExtraFor(name)) {
                if (owned.Contains(o)) continue;
                ProgressionSlot? slot = Tree.SlotOf(o); // orbs with no slot yet only change the build
                if (slot != null && slot.Skill != null && !wanted.Contains(slot)) wanted.Add(slot);
            }
            granted.TryGetValue(pm, out List<ProgressionSlot>? have);
            if ((have == null || have.Count == 0) && wanted.Count == 0) return;
            Dictionary<uint, int>? levels = Levels(pm);
            if (levels == null) return;
            have ??= new List<ProgressionSlot>();

            foreach (ProgressionSlot slot in wanted) {
                if (have.Contains(slot)) continue;
                uint hash = slot.Skill.Hash;
                levels.TryGetValue(hash, out int n);
                levels[hash] = ++n;
                if (n == 1) slot.Skill.Unlock(pm);
                have.Add(slot);
                if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST BUILD: granted the game's '{slot.Skill.name}' to '{name}' (level {n}; holding {Holding(pm)} item(s))");
            }
            foreach (ProgressionSlot slot in have.ToList()) {
                if (wanted.Contains(slot)) continue;
                uint hash = slot.Skill.Hash;
                levels.TryGetValue(hash, out int n);
                n--;
                if (n <= 0) { n = 0; levels.Remove(hash); slot.Skill.Lock(pm); }
                else levels[hash] = n;
                have.Remove(slot);
                if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST BUILD: took back the game's '{slot.Skill.name}' from '{name}' (level {n}; holding {Holding(pm)} item(s))");
            }
            if (have.Count == 0) granted.Remove(pm);
            else granted[pm] = have;
            // Destroyed managers (players who left) drop out here.
            foreach (PlayerProgressionManager gone in granted.Keys.Where(k => k == null).ToList()) granted.Remove(gone);
        }

        /// <summary>Server: the game rebuilt its skill levels from the owned slots alone; add the granted levels back.
        /// Unlock is not called again (the skill's registration survived; only the count was cleared).</summary>
        static readonly int T_Reapply = Timing.Section("TestBuild.Reapply");
        internal static void Reapply(PlayerProgressionManager pm) {
            using var _t = Timing.Begin(T_Reapply);
            if (!NetworkSceneManager.IsServer || pm == null || !granted.TryGetValue(pm, out List<ProgressionSlot> have) || have.Count == 0) return;
            Dictionary<uint, int>? levels = Levels(pm);
            if (levels == null) return;
            foreach (ProgressionSlot slot in have) {
                levels.TryGetValue(slot.Skill.Hash, out int n);
                levels[slot.Skill.Hash] = n + 1;
            }
            if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST BUILD: re-added {have.Count} granted level(s) for '{Builds.NameOf(pm.Player)}' after the game recounted its skills");
        }
    }
}
