using System;
using System.Collections.Generic;
using System.Linq;
using ATT.Saving;
using Alta.Chunks;
using Alta.Global;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.PlayerStates;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Server-only play-test tools, driven by tools\TestBuildMenu.ps1 through the server's QuickshotRing.cfg:
    //   [TestBuild] Buy + BuyRequest      really sell and buy archery orbs so [TestBuild] Player owns exactly the list (saved)
    //   [TestBuild] Spawn + SpawnRequest  spawn creatures in a line ahead of that player, frozen after FreezeSeconds (never saved)
    //   [TestBuild] ClearRequest          remove every creature this tool spawned
    // Safety: nothing runs on a client, nothing runs unless a request number changed after the server started (the numbers
    // the server read at start-up are remembered and never acted on), and nothing runs unless [TestBuild] Player names a
    // player who is online. The rules (what to sell and buy, the spawn line, the request check) are in Build.cs, tested.
    // Fix batch D (fixB-review):
    //   C-1  A buy acts only on a player who is online AND fully loaded at the moment the request is seen and again when it runs
    //        (BuyGate, Build.cs, tested: found by name, the game has read their save (Player.StartUp, private isStarted), a body,
    //        a loaded state that is not character customization, the tree in the game, Ranged experience and owned-slot data).
    //        Anything uncertain refuses it; a refused request is logged and dropped. Nothing ever waits for a player.
    //   I-1  The points / level / experience snapshot is put back in a finally, and each sale and each purchase is caught on its
    //        own, so an exception cannot leave the level raised or the batch half-restored.
    //   I-2  The player name and the orb list are captured when the request is seen; a press while this process is not (yet) the
    //        server is recorded and ignored, so it never fires later.
    //   I-3  No wait for empty hands. After the batch, every skill on every pickup the player holds keeps exactly one subscription
    //        to its input (a newly unlocked skill re-subscribes the skills already there: their inputs fired twice).
    //   Minors: an unknown orb name refuses the whole request; the level is raised to cover the orbs owned after a batch (so a
    //   later sale by hand cannot wrap the unsigned level); the level is put back right after the sales; the save line says when
    //   the game declined; a second spawn press is refused while this tool's creatures stand within 2 m of a planned spot (no
    //   shift sideways); the spawn distances are clamped; a prefab with no creature brain is refused before it is spawned.
    internal static class TestTools {
        static MelonPreferences_Entry<string> buy = null!, buyRequest = null!, spawn = null!, spawnRequest = null!, clearRequest = null!;
        static MelonPreferences_Entry<float> freezeSeconds = null!, aheadMetres = null!, gapMetres = null!;
        static MelonPreferences_Entry<int> spawnMax = null!;
        static string lastBuy = "", lastSpawn = "", lastClear = "";
        static bool started, spawnPending, clearPending;
        const float SpawnClearMetres = 2f;

        // a buy request as it was seen: who and what, captured at that moment (I-2)
        sealed class BuyRequest { public string Name = ""; public string Text = ""; public string Number = ""; }
        static BuyRequest? pendingBuy;

        static AccessTools.FieldRef<PathExperience, uint>? expField, levelField, pointsField;
        static AccessTools.FieldRef<PlayerProgressionManager, bool>? canBuyMore;
        static AccessTools.FieldRef<Player, bool>? startedField;
        static bool startedFieldFailed;

        sealed class Spawned {
            public NetworkEntity? Entity;
            public Alta.Intelligence.Agent? Agent;
            public string Kind = "";
            public string Owner = "";
            public float FreezeAt;
            public bool Frozen;
        }
        static readonly List<Spawned> spawned = new List<Spawned>();

        static bool Server => NetworkSceneManager.IsServer;

        internal static void Init() {
            var cat = Cfg.Category("TestBuild"); // after TestBuild.Init: [TestBuild] Player is its entry (TestBuild.Configured)
            buy = cat.CreateEntry("Buy", "", description: "Server only, for play-tests. Orb names separated by ';' that [TestBuild] Player really owns after the next BuyRequest: every other archery orb is sold (the Gatherer is kept unless listed; QuiverGather is another name for it), these are bought in order up to the game's slot cap, and the save is written. Points and experience are put back as they were; the Ranged level too, raised if needed to cover the orbs owned. Empty list = sell every archery orb. A name that is not an orb refuses the whole request.");
            buyRequest = cat.CreateEntry("BuyRequest", "", description: "Server only. The button window writes a new number here on every press; the server applies Buy once per new number while it runs (the number it started with is never applied). The player must be online and fully loaded at the press, or the request is refused and dropped.");
            spawn = cat.CreateEntry("Spawn", "", description: $"Server only, for play-tests. \"Kind xN\" ({SpawnRule.Known}) spawned in a line ahead of [TestBuild] Player on the next SpawnRequest. Never saved; their drops are ordinary loot.");
            spawnRequest = cat.CreateEntry("SpawnRequest", "", description: "Server only. A new number spawns Spawn once (same rule as BuyRequest).");
            clearRequest = cat.CreateEntry("ClearRequest", "", description: "Server only. A new number removes every creature the test tool spawned.");
            freezeSeconds = cat.CreateEntry("FreezeSeconds", 2f, description: "Server only. Seconds after spawning until a test creature is frozen (the game's own switch for a held creature: no moving or attacking, still takes hits, staggers and dies).");
            spawnMax = cat.CreateEntry("SpawnMax", 9, description: "Server only. At most this many test creatures alive at once; a request that would pass it is refused.");
            aheadMetres = cat.CreateEntry("SpawnAheadMetres", 6f, description: "Server only. The first test creature stands this far ahead of the player's head, flattened to the ground (at least 2).");
            gapMetres = cat.CreateEntry("SpawnGapMetres", 3f, description: "Server only. The others stand this far apart behind it, on the same line (at least 1).");
            Cfg.Reloaded += Reload;
        }

        /// <summary>After Cfg.Ready: the request numbers in the file now are the ones the server started with; they are never acted on.</summary>
        internal static void Started() {
            lastBuy = (buyRequest.Value ?? "").Trim();
            lastSpawn = (spawnRequest.Value ?? "").Trim();
            lastClear = (clearRequest.Value ?? "").Trim();
            started = true;
        }

        // Every new number is recorded at once, whether or not it is acted on (I-2): a press while this process is not yet the
        // server (a dedicated server starting up) is ignored for good, never fired by a later reload.
        static readonly int T_Reload = Timing.Section("TestTools.Reload");
        static void Reload() {
            using var _t = Timing.Begin(T_Reload);
            if (!started) return;
            try {
                bool server = Server;
                if (TestRequest.IsNew(lastBuy, buyRequest.Value)) {
                    lastBuy = buyRequest.Value.Trim();
                    if (!server) Ignored("buy", lastBuy);
                    else {
                        var req = new BuyRequest { Name = Who, Text = buy.Value ?? "", Number = lastBuy };
                        string? why = Gate(req, out _, out _, out _);
                        if (why != null) MelonLogger.Warning($"[Archery] TEST BUILD: buy request {req.Number} for '{req.Name}' refused and dropped: {why}");
                        else { pendingBuy = req; MelonLogger.Warning($"[Archery] TEST BUILD: buy request {req.Number} for '{req.Name}': '{req.Text}'"); }
                    }
                }
                if (TestRequest.IsNew(lastSpawn, spawnRequest.Value)) { lastSpawn = spawnRequest.Value.Trim(); if (server) spawnPending = true; else Ignored("spawn", lastSpawn); }
                if (TestRequest.IsNew(lastClear, clearRequest.Value)) { lastClear = clearRequest.Value.Trim(); if (server) clearPending = true; else Ignored("clear", lastClear); }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: reading the requests failed: {e.Message}"); }
        }

        static void Ignored(string what, string number) {
            if (ApplicationManager.IsHeadless) // a dedicated server still starting; on a client these settings mean nothing
                MelonLogger.Warning($"[Archery] TEST BUILD: {what} request {number} ignored: the server is not running yet (press again once it is)");
        }

        static string Who => TestBuild.Configured;

        /// <summary>The online player [TestBuild] Player names, or null (empty name, or not on the server).</summary>
        static Player? Online() {
            string name = Who;
            if (name.Length == 0) return null;
            Player p = Player.GetPlayer(name);
            return p != null && p.ProgressionManager != null ? p : null;
        }

        /// <summary>Server, every frame (Main.OnUpdate, before the headless early return).</summary>
        internal static void Poll() {
            if (!Server) return;
            if (clearPending) { clearPending = false; Clear("a clear request"); }
            if (spawnPending) { spawnPending = false; Spawn(); }
            if (pendingBuy != null) { BuyRequest req = pendingBuy; pendingBuy = null; TryBuy(req); }
            Tend();
        }

        /// <summary>Server, on quit: remove the test creatures (they are never saved either way).</summary>
        internal static void Shutdown() {
            try { if (Server && spawned.Count > 0) Clear("server shutdown"); }
            catch { }
        }

        // ================================================================ 1. really buying a preset

        /// <summary>The game has read this player's save (Player.StartUp sets the private isStarted just before LoadFrom); null when
        /// that cannot be read, which refuses.</summary>
        static bool? SaveRead(Player p) {
            if (startedFieldFailed) return null;
            try {
                startedField ??= AccessTools.FieldRefAccess<Player, bool>("isStarted");
                return startedField(p);
            }
            catch (Exception e) {
                startedFieldFailed = true;
                MelonLogger.Error($"[Archery] TEST BUILD: cannot tell whether a player's save was read (Player.isStarted): {e.Message}; every buy is refused");
                return null;
            }
        }

        /// <summary>C-1: why this request must not run now (null = it may), from the player as the server sees them right now.</summary>
        static string? Gate(BuyRequest req, out Player? p, out PathExperience? exp, out List<Orb> wanted) {
            p = null; exp = null;
            wanted = TestBuildRule.Parse(req.Text, out List<string> unknown);
            var f = new BuyGateFacts { Named = req.Name.Length > 0, UnknownNames = unknown.Count };
            try {
                if (f.Named) {
                    Player found = Player.GetPlayer(req.Name);
                    if (found != null && found.ProgressionManager != null) { p = found; f.Found = true; }
                }
                if (p != null) {
                    f.Started = SaveRead(p);
                    f.HasController = p.PlayerController != null;
                    PlayerStateManager sm = p.StateManager;
                    f.StateLoaded = sm == null ? (bool?)null : sm.CurrentStateType != PlayerStateType.None && sm.CurrentStateType != PlayerStateType.Customization;
                    f.TreeReady = Tree.Ready && Tree.OrbBySlot.Count > 0;
                    PlayerProgressionManager pm = p.ProgressionManager;
                    f.RangedExperience = pm.PathExperiences != null && pm.PathExperiences.TryGetValue(Tree.RangedPath, out exp) && exp != null;
                    f.SlotData = exp != null && exp.UnlockedSlots != null;
                }
            }
            catch (Exception e) { return $"reading the player's state failed ({e.Message})"; }
            string? why = BuyGate.Refusal(f);
            if (why != null && unknown.Count > 0) why += $": {string.Join(", ", unknown.ToArray())}";
            return why;
        }

        static readonly int T_TryBuy = Timing.Section("TestTools.TryBuy");
        static void TryBuy(BuyRequest req) {
            using var _t = Timing.Begin(T_TryBuy);
            string? why = Gate(req, out Player? p, out PathExperience? exp, out List<Orb> wanted);
            if (why != null || p == null || exp == null) { MelonLogger.Warning($"[Archery] TEST BUILD: buy request {req.Number} for '{req.Name}' refused and dropped: {why ?? "the player changed"}"); return; }
            try { Buy(p, p.ProgressionManager, exp, wanted, req); }
            catch (Exception e) { MelonLogger.Error($"[Archery] TEST BUILD: buy failed: {e}"); }
        }

        static bool Fields() {
            try {
                expField ??= AccessTools.FieldRefAccess<PathExperience, uint>("currentExperience");
                levelField ??= AccessTools.FieldRefAccess<PathExperience, uint>("currentLevel");
                pointsField ??= AccessTools.FieldRefAccess<PathExperience, uint>("freePoints");
                canBuyMore ??= AccessTools.FieldRefAccess<PlayerProgressionManager, bool>("canBuyMoreSkills");
                return true;
            }
            catch (Exception e) { MelonLogger.Error($"[Archery] TEST BUILD: cannot reach the game's skill points; buy refused: {e.Message}"); return false; }
        }

        static string Names(IEnumerable<Orb> list) {
            string s = string.Join("+", list.Select(o => o.ToString()).ToArray());
            return s.Length > 0 ? s : "nothing";
        }

        static readonly int T_Buy = Timing.Section("TestTools.Buy");
        static void Buy(Player p, PlayerProgressionManager pm, PathExperience exp, List<Orb> wanted, BuyRequest req) {
            using var _t = Timing.Begin(T_Buy);
            string name = Builds.NameOf(p);
            if (!Fields()) return;

            // switched-off names are refused by name (unknown names refused the whole request in the gate)
            var refused = new List<string>();
            foreach (Orb o in wanted.Where(o => NewOrbs.All.Contains(o) && !Tree.OrbOn(o)).ToList()) {
                wanted.Remove(o);
                refused.Add($"{o} (switched off: [Tree] {o}Orb = false)");
            }

            // the tree as the rule sees it: each archery orb and its parents
            var deps = new Dictionary<Orb, Orb[]>();
            foreach (Orb o in Tree.OrbBySlot.Values.Distinct()) {
                ProgressionSlot? s = Tree.SlotOf(o);
                if (s == null) continue;
                deps[o] = (s.Dependencies ?? new List<uint>()).Select(h => Tree.OrbBySlot.TryGetValue(h, out Orb d) ? d : Orb.None).ToArray();
            }

            var owned = new List<Orb>();
            foreach (uint h in exp.UnlockedSlots) if (Tree.OrbBySlot.TryGetValue(h, out Orb o)) owned.Add(o);
            int total = 0;
            foreach (KeyValuePair<ProgressionPath, PathExperience> pe in pm.PathExperiences) {
                ProfessionSkillTree t = ProfessionSkillTree.GetProfessionTree(pe.Key);
                if (t == null || pe.Value == null) continue;
                foreach (uint h in pe.Value.UnlockedSlots) if (t.GetSlot(h, out ProgressionSlot _)) total++;
            }
            int cap = (int)GlobalSettings<ProgressionSettings>.Instance.MaxBoughtSkills;
            BuyPlan plan = BuyRule.Plan(owned, wanted, deps, cap, Math.Max(0, total - owned.Count));

            var sold = new List<Orb>();
            var bought = new List<Orb>();
            var failed = new List<string>();
            uint xp = expField!(exp), level = levelField!(exp), points = pointsField!(exp), levelAfter = level;
            // G3 (play-test 8: Ranged level 4294967258 in the save, wrapped by earlier hand sales): a level above BuyRule.SaneLevelMax
            // is repaired to the cost of the orbs owned now plus the points still unspent (fix1, final-G34 M-7; raised below to cover
            // what the batch leaves); a sane level is never lowered
            uint wrappedLevel = level;
            level = BuyRule.RepairLevel(level, OwnedCost(exp), points, out bool repaired);
            if (repaired) MelonLogger.Warning($"[Archery] TEST BUILD: '{name}''s Ranged level {wrappedLevel} is not real (an unsigned level wrapped by earlier sales); repaired to {level}, the cost of the orbs owned plus {points} unspent point(s)");
            bool cut = false;
            Builds.BeginBatch(pm);
            try {
                try { pm.EndPractice(); } // a try-out in progress would leave the counts to the game (Shrine.Sale)
                catch (Exception e) { failed.Add($"ending the try-out ({e.Message})"); }

                // sell, leaves first, with no experience refund (the game's own sale path below the shrine's); each on its own (I-1)
                foreach (Orb o in plan.Sell) {
                    try {
                        ProgressionSlot? slot = Tree.SlotOf(o);
                        if (slot == null) continue;
                        if (!exp.UnlockedSlots.Contains(slot.Hash)) { sold.Add(o); continue; } // the game removed it with its parent
                        levelField!(exp) = level + 1000u; // the game lowers the level by each sold orb's cost (unsigned); put back below
                        exp.LockSlot(slot, false);
                        if (exp.UnlockedSlots.Contains(slot.Hash)) failed.Add($"{o} (still owned after the sale)");
                        else sold.Add(o);
                    }
                    catch (Exception e) { cut = true; failed.Add($"{o} (the sale threw: {e.Message})"); }
                }
                levelField!(exp) = level; // M-5: back right after the sales, so the purchases' stat lines carry the real level
                try { pm.UpdateCounts(); } catch (Exception e) { cut = true; failed.Add($"recounting after the sales ({e.Message})"); }

                // buy in the plan's order, each marked practised (keeps a later hand purchase possible); each on its own (I-1)
                foreach (Orb o in plan.Buy) {
                    try {
                        ProgressionSlot? slot = Tree.SlotOf(o);
                        if (slot == null) { failed.Add($"{o} (no slot)"); continue; }
                        pm.MarkSlotPracticed(slot.Path, slot.Hash);
                        if (pointsField!(exp) < slot.Cost) pointsField!(exp) = slot.Cost;
                        pm.UnlockSlot(slot);
                        if (exp.UnlockedSlots.Contains(slot.Hash)) bought.Add(o);
                        else failed.Add($"{o} (refused by the game or the mod's one-ultimate rule; see the lines above)");
                    }
                    catch (Exception e) { cut = true; failed.Add($"{o} (the purchase threw: {e.Message})"); }
                }
            }
            finally {
                // I-1: the swap costs and refunds nothing, whatever happened above; the level covers the orbs now owned (M-4)
                try {
                    levelAfter = BuyRule.LevelToCover(level, OwnedCost(exp));
                    expField!(exp) = xp; levelField!(exp) = levelAfter; pointsField!(exp) = points;
                    if (levelAfter != level) MelonLogger.Warning($"[Archery] TEST BUILD: '{name}''s Ranged level raised from {level} to {levelAfter} to cover the orbs now owned (a later sale by hand lowers it by each orb's cost)");
                    Traverse.Create(exp).Field("UpdatedPoints").GetValue<Action<ProgressionPath>>()?.Invoke(exp.Path); // an open shrine refreshes
                }
                catch (Exception e) { cut = true; MelonLogger.Error($"[Archery] TEST BUILD: putting back '{name}''s points, level and experience failed: {e}"); }
                try {
                    pm.UpdateCounts();
                    int ownedNow = pm.GetSlots().Count();
                    canBuyMore!(pm) = cap < 1 || cap > ownedNow;
                }
                catch (Exception e) { cut = true; MelonLogger.Error($"[Archery] TEST BUILD: recounting '{name}''s skills failed: {e.Message}"); }
                Builds.EndBatch(pm); // the one refresh: the build and the main path, decided on the final set
            }
            int dupes = OneSubscriptionEach(pm); // I-3: no doubled inputs on what the player holds

            Build build = Builds.ServerBuild(p);
            string leftOut = string.Join(", ", plan.LeftOut.Select(k => $"{k.Key} ({k.Value})").Concat(refused).Concat(failed).ToArray());
            MelonLogger.Warning($"[Archery] TEST BUILD: request {req.Number}{(cut ? " CUT SHORT" : "")}: bought {Names(bought)} sold {Names(sold)} left out {(leftOut.Length > 0 ? leftOut : "nothing")} for '{name}'"
                                + $" (also removed by the game with a sold parent: {Names(plan.RemovedWith.Where(o => !bought.Contains(o)))}; kept {Names(plan.Kept)}; room {(plan.Room == int.MaxValue ? "no cap" : plan.Room.ToString())}; {dupes} doubled input subscription(s) removed); now {build}");
            try {
                bool savable = p.IsInSavableState();
                p.SaveControllerAndState();
                p.WriteSave(false);
                MelonLogger.Warning(savable
                    ? $"[Archery] TEST BUILD: '{name}''s save was changed; the game was asked to write it now (Ranged level {levelAfter}, {points} free point(s), {xp} experience kept)"
                    : $"[Archery] TEST BUILD: '{name}''s save was changed; the game was asked to write it now, but '{name}' is dead or in character customization, so that file may still hold the earlier progression until the game's next save");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: the purchase is done but writing '{name}''s save now failed (the game saves it later): {e.Message}"); }
        }

        /// <summary>The skill points the orbs this path owns cost.</summary>
        internal static uint OwnedCost(PathExperience exp) {
            uint ownedCost = 0u;
            ProfessionSkillTree? tree = ProfessionSkillTree.GetProfessionTree(exp.Path);
            if (tree == null) return 0u;
            foreach (uint h in exp.UnlockedSlots) if (tree.GetSlot(h, out ProgressionSlot s) && s != null) ownedCost += s.Cost;
            return ownedCost;
        }

        /// <summary>I-3: on every pickup the player's skill helper holds, each skill keeps exactly one subscription to its input. A
        /// skill whose level went from 0 to 1 re-validates the handler (PlayerToolBasedSkillHelper.NewSkillOnInteractor), which adds
        /// every registered skill's InputChangedState to its input once more. How many extra subscriptions were removed.</summary>
        static int OneSubscriptionEach(PlayerProgressionManager pm) {
            int removed = 0;
            try {
                PlayerToolBasedSkillHelper helper = pm.GetComponent<PlayerToolBasedSkillHelper>();
                if (helper == null) return 0;
                foreach (KeyValuePair<Interactable, HeldPickupHandler> held in helper.HeldInteractables) {
                    HeldPickupHandler h = held.Value;
                    if (h == null) continue;
                    foreach (KeyValuePair<IInputBasedSkill, ISkillInputMethod> kv in h.SkillsToInputs) {
                        if (kv.Key == null || kv.Value == null) continue;
                        Action<bool, ISkillInputMethod> handler = kv.Key.InputChangedState;
                        int n = 0;
                        while (n < 16) { kv.Value.ChangedActive -= handler; n++; if (!Subscribed(kv.Value, handler)) break; }
                        kv.Value.ChangedActive += handler;
                        removed += Math.Max(0, n - 1);
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: de-duplicating input subscriptions failed: {e.Message}"); }
            return removed;
        }

        /// <summary>Whether the input's ChangedActive event still holds this handler (read from its backing field; false when it cannot be read).</summary>
        static bool Subscribed(ISkillInputMethod input, Action<bool, ISkillInputMethod> handler) {
            try {
                var d = Traverse.Create(input).Field("ChangedActive").GetValue() as Delegate;
                if (d == null) return false;
                foreach (Delegate x in d.GetInvocationList()) if (x.Equals(handler)) return true;
                return false;
            }
            catch { return false; }
        }

        // ================================================================ 2. creatures that stand still

        static readonly int T_Spawn = Timing.Section("TestTools.Spawn");
        static void Spawn() {
            using var _t = Timing.Begin(T_Spawn);
            Player? p = Online();
            if (p == null) { MelonLogger.Warning($"[Archery] TEST BUILD: spawn refused: {(Who.Length == 0 ? "[TestBuild] Player is empty" : $"'{Who}' is not online")}"); return; }
            if (!SpawnRule.Parse(spawn.Value, out string kind, out int count, out bool moving)) {
                MelonLogger.Warning($"[Archery] TEST BUILD: spawn refused: '{spawn.Value}' is not \"Kind xN\" (or \"Kind xN moving\") with a kind of {SpawnRule.Known}");
                return;
            }
            Prune();
            int alive = Alive();
            if (!SpawnRule.Allowed(alive, count, spawnMax.Value)) {
                MelonLogger.Warning($"[Archery] TEST BUILD: spawn refused: {alive} test creature(s) alive + {count} would pass [TestBuild] SpawnMax {spawnMax.Value}; remove them first");
                return;
            }
            uint hash = SpawnRule.Hash(kind);
            NetworkPrefab prefab = PrefabManager.GetPrefab(hash);
            if (prefab == null) { MelonLogger.Warning($"[Archery] TEST BUILD: spawn refused: no prefab {hash} for {kind} in this game"); return; }
            // fixB-review M-9: a prefab with no creature brain cannot be frozen; refused before anything is spawned
            if (prefab.GetComponentInChildren<Alta.Intelligence.Agent>(true) == null) { MelonLogger.Warning($"[Archery] TEST BUILD: spawn refused: prefab {hash} ('{prefab.name}') has no creature brain to freeze"); return; }
            Transform? head = p.PlayerController != null ? p.PlayerController.Head : null;
            if (head == null) { MelonLogger.Warning($"[Archery] TEST BUILD: spawn refused: '{Who}' has no body right now"); return; }

            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) { forward = head.up; forward.y = 0f; } // looking straight down: the top of the head points ahead
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            Quaternion facing = Quaternion.LookRotation(-forward, Vector3.up);
            // H3: four or more stand side by side in a shallow arc (several locks, none behind another); three or fewer in the line
            List<SpawnRule.Spot> spots = SpawnRule.Spots(count, SpawnRule.ClampAhead(aheadMetres.Value), SpawnRule.ClampGap(gapMetres.Value)); // fixB-review M-8
            Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
            // fixB-review M-1: refused while this tool's live creatures stand within 2 m of a planned spot (a second press would
            // stack the new ones on their heads); the line is not shifted sideways
            var xs = new List<float>(); var zs = new List<float>();
            foreach (Spawned s in spawned) if (!Gone(s)) { Vector3 at = s.Entity!.transform.position; xs.Add(at.x); zs.Add(at.z); }
            foreach (SpawnRule.Spot sp in spots) {
                float d = sp.Ahead;
                Vector3 spot = head.position + forward * sp.Ahead + across * sp.Side;
                if (SpawnRule.Blocked(spot.x, spot.z, xs, zs, SpawnClearMetres)) {
                    MelonLogger.Warning($"[Archery] TEST BUILD: spawn refused: this tool's creatures already stand within {SpawnClearMetres:0} m of the spot {d:0} m ahead; remove them first (Clear) or turn to face somewhere else");
                    return;
                }
            }
            var placed = new List<string>();
            int made = 0;
            foreach (SpawnRule.Spot sp in spots) {
                float d = sp.Ahead;
                Vector3 above = head.position + forward * sp.Ahead + across * sp.Side + Vector3.up * 5f;
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) { placed.Add($"{d:0}m: no ground"); continue; }
                LocationChunk chunk = LocationChunkHelper.GetBestContaining(hit.point);
                if (chunk == null) { placed.Add($"{d:0}m: no chunk"); continue; }
                try {
                    NetworkEntity e = SpawnHelper.Spawn(prefab, SpawnData.Default, chunk, hit.point, facing,
                        x => { if (x != null && x.Prefab != null) x.Prefab.SaveMode = SaveMode.Ignore; }); // never in the world save
                    if (e == null) { placed.Add($"{d:0}m: spawn failed"); continue; }
                    var s = new Spawned { Entity = e, Agent = e.GetComponentInChildren<Alta.Intelligence.Agent>(), Kind = kind, Owner = Builds.NameOf(p), FreezeAt = moving ? float.PositiveInfinity : Time.time + Mathf.Max(0f, freezeSeconds.Value) };
                    spawned.Add(s);
                    made++;
                    placed.Add($"{d:0}m {Targets.P(hit.point)}{(s.Agent == null ? " (no agent: cannot be frozen)" : "")}");
                }
                catch (Exception ex) { placed.Add($"{d:0}m: {ex.Message}"); }
            }
            MelonLogger.Warning($"[Archery] TEST BUILD: spawned {made} of {count} '{prefab.name}' ({hash}) ahead of '{Builds.NameOf(p)}': {string.Join("; ", placed.ToArray())}; {(moving ? "moving (never frozen)" : $"frozen in {freezeSeconds.Value:0.#} s")}; {Alive()} test creature(s) alive");
        }

        static bool Gone(Spawned s) => s.Entity == null || s.Entity.IsBeingDestroyed;

        static int Alive() => spawned.Count(s => !Gone(s) && (s.Agent == null || s.Agent.IsAlive));

        static readonly Predicate<Spawned> GoneTest = Gone;   // G2: one delegate, not one per frame
        static readonly List<string> owners = new List<string>();

        static void Prune() => spawned.RemoveAll(GoneTest);

        // Freeze the due ones; remove them all when their player has left.
        static readonly int T_Tend = Timing.Section("TestTools.Tend");
        static void Tend() {
            using var _t = Timing.Begin(T_Tend);
            if (spawned.Count == 0) return;
            float now = Time.time;
            foreach (Spawned s in spawned) {
                if (s.Frozen || now < s.FreezeAt || Gone(s)) continue;
                s.Frozen = true;
                try {
                    if (s.Agent == null || !s.Agent.IsAlive) continue;
                    s.Agent.IsEnabled = false; // the game's own freeze for a held creature (PickupAgent): no new actions, walker off
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST BUILD: froze test {s.Kind} {s.Entity!.name} (walker {(s.Agent.HasWalker ? "off" : "none")})");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: could not freeze test {s.Kind}: {e.Message}"); }
            }
            Prune();
            // G2: the distinct owners in first-seen order, as Select + Distinct + ToList gave, without LINQ per frame
            owners.Clear();
            foreach (Spawned s in spawned) if (!owners.Contains(s.Owner)) owners.Add(s.Owner);
            foreach (string owner in owners)
                if (Player.GetPlayer(owner) == null) Clear($"'{owner}' left", s => s.Owner == owner);
        }

        static readonly int T_Clear = Timing.Section("TestTools.Clear");
        static void Clear(string why, Predicate<Spawned>? which = null) {
            using var _t = Timing.Begin(T_Clear, why);
            int removed = 0;
            foreach (Spawned s in spawned) {
                if (Gone(s) || (which != null && !which(s))) continue;
                try { s.Entity!.SceneDestroy(); removed++; }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST BUILD: could not remove test {s.Kind}: {e.Message}"); }
            }
            if (which == null) spawned.Clear();
            else spawned.RemoveAll(which);
            MelonLogger.Warning($"[Archery] TEST BUILD: removed {removed} test creature(s) ({why})");
        }

        // ================================================================ hooks for the play-test range (TestRange*.cs): the same gate and buy

        /// <summary>TestRange: null when this player is online and fully loaded right now, exactly as a buy request is gated (C-1);
        /// else why not.</summary>
        internal static string? ReadyFor(string name, out Player? p, out PathExperience? exp) => Gate(new BuyRequest { Name = name ?? "" }, out p, out exp, out _);

        /// <summary>TestRange (buy for everyone): one player's buy of this orb list, gated as a pressed request; null when it ran,
        /// else why not (nothing done).</summary>
        internal static string? BuyNow(string name, string text, string number) {
            var req = new BuyRequest { Name = name ?? "", Text = text ?? "", Number = number ?? "" };
            string? why = Gate(req, out Player? p, out PathExperience? exp, out List<Orb> wanted);
            if (why != null || p == null || exp == null) return why ?? "the player changed";
            try { Buy(p, p.ProgressionManager, exp, wanted, req); return null; }
            catch (Exception e) { MelonLogger.Error($"[Archery] TEST BUILD: buy failed: {e}"); return $"the buy threw: {e.Message}"; }
        }

        /// <summary>TestRange (restore): the same de-duplication of input subscriptions a buy ends with (I-3).</summary>
        internal static int OneSubscriptionEachFor(PlayerProgressionManager pm) => OneSubscriptionEach(pm);
    }
}
