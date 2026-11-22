using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Networking.Scripts.Player;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;

namespace BluesArcheryOverhaul {
    // Who owns which archery orbs. The server keeps one build per player (owned orbs + the orb being tried out +
    // the server-only test build), recomputed whenever the game unlocks, sells, tries out or loads an orb.
    // The build bits carry it to clients: 32 bits at the very end of every bow status message (BowDrawSkill and
    // its subclasses: Quickdraw, Slowdraw, Lightbeam). The block and its layout are in Build.cs.
    internal static class Builds {
        // The player whose skill message is being written or read; only set inside PickupBasedSkill.SendSerializedMessage.
        static IPlayer? owner;
        static bool loggedWrite, loggedRead, warnedNoTree, warnedUnknown, loggedMatch, warnedMismatch, loggedFired;
        static readonly Dictionary<string, string> serverLogged = new Dictionary<string, string>(); // player name -> build text last logged
        static readonly Dictionary<string, Build> clientCache = new Dictionary<string, Build>();    // player name -> build from the server
#if QUEST
        static readonly Dictionary<PlayerProgressionManager, Build> serverCache = new Dictionary<PlayerProgressionManager, Build>(Il.ByPointer<PlayerProgressionManager>());
#else
        static readonly Dictionary<PlayerProgressionManager, Build> serverCache = new Dictionary<PlayerProgressionManager, Build>();
#endif
        // Managers whose save is being loaded: their orbs arrive one at a time, so refreshes wait for the end of LoadFrom.
#if QUEST
        static readonly HashSet<PlayerProgressionManager> loading = new HashSet<PlayerProgressionManager>(Il.ByPointer<PlayerProgressionManager>());
#else
        static readonly HashSet<PlayerProgressionManager> loading = new HashSet<PlayerProgressionManager>();
#endif
        static readonly HashSet<string> livePatches = new HashSet<string>();
        const int CacheTrimAt = 64;

        static Builds() {
            Cfg.Reloaded += () => warnedMismatch = false;
        }

        /// <summary>Start-up (where the test build's own settings reload was subscribed): every settings reload forgets the cached
        /// builds and refreshes the known players.</summary>
        internal static void Init() => Cfg.Reloaded += Reload;

        static readonly int T_Reload = Timing.Section("Builds.Reload (settings)");
        static void Reload() {
            using var _t = Timing.Begin(T_Reload);
            try { ReloadAll(noManagers); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Build: reload failed: {e}"); }
        }
        static readonly PlayerProgressionManager[] noManagers = new PlayerProgressionManager[0];

        // The play-test kit's lent and hidden orbs for this player (KitHooks; none without the kit).
        static readonly Orb[] noOrbs = new Orb[0];
        static IReadOnlyList<Orb> Lent(string? name) => KitHooks.Lent?.Invoke(name) ?? noOrbs;
        static IReadOnlyList<Orb> Hidden(string? name) => KitHooks.Hidden?.Invoke(name) ?? noOrbs;

        /// <summary>Server: the play-test kit lends or hides orbs for this player (telemetry marks the shot as a test build's).</summary>
        internal static bool TestBuildFor(string? name) => Lent(name).Count > 0 || Hidden(name).Count > 0;

#if QUEST
        internal static string NameOf(IPlayer? p) => p?.UserInfo?.Username ?? "?";
        // Quest: a Player is not an IPlayer to C# (an interface is a class in IL2CPP); these take the Player itself.
        internal static string NameOf(Player? p) => p?.UserInfo?.Username ?? "?";
        internal static string KeyOf(Player? p, string name) {
            int id = p?.UserInfo?.Identifier ?? 0;
            return id != 0 ? id.ToString() : name;
        }
#else
        internal static string NameOf(IPlayer? p) => p?.UserInfo?.Username ?? "?";
#endif

        /// <summary>The key a player's main path is remembered under: the account id, else the username.</summary>
        internal static string KeyOf(IPlayer? p, string name) { // internal for the play-test kit (reset, snapshots)
            int id = p?.UserInfo?.Identifier ?? 0;
            return id != 0 ? id.ToString() : name;
        }

        static bool TreeMapped => Tree.Ready && Tree.OrbBySlot.Count > 0;

        // The archery orbs this manager really owns on the Ranged path (no practice, no test build).
        static List<Orb> RealOwned(PlayerProgressionManager? pm) {
            var owned = new List<Orb>();
            // TryGetValue, never the indexer: a player who never earned Ranged experience has no entry yet.
#if QUEST
            // Quest: never the game's PathExperiences (keyed by an enum: reading it from the mod aborts the game natively); the
            // game's own list of the unlocked slots of every path
            if (pm != null && TreeMapped)
                foreach (ProgressionSlot slot in pm.GetSlots().Each())
                    if (slot != null && slot.Path == Tree.RangedPath && Tree.OrbBySlot.TryGetValue(slot.Hash, out Orb o)) owned.Add(o);
#else
            if (pm != null && TreeMapped && pm.PathExperiences.TryGetValue(Tree.RangedPath, out PathExperience exp) && exp != null)
                foreach (uint hash in exp.UnlockedSlots)
                    if (Tree.OrbBySlot.TryGetValue(hash, out Orb o)) owned.Add(o);
#endif
            return owned;
        }

        static Orb Practising(PlayerProgressionManager? pm) {
            SkillOrb? orb = pm != null ? pm.PracticingOrb : null;
            return orb != null && orb.Slot != null && Tree.OrbBySlot.TryGetValue(orb.Slot.Hash, out Orb po) ? po : Orb.None;
        }

        // Owned + practising + test build, minus the test build's Without, logged when it changes.
        static readonly int T_Compute = Timing.Section("Builds.Compute");
        static Build Compute(PlayerProgressionManager? pm, string name, out List<Orb> owned) {
            using var _t = Timing.Begin(T_Compute, name);
            owned = RealOwned(pm);
            Orb practising = Practising(pm);
            IReadOnlyList<Orb> extra = Lent(name);
            IReadOnlyList<Orb> without = Hidden(name);
            Build build = Build.From(TestBuildRule.Combine(owned, practising, extra, without));
            // The main path: remembered per account, decided only on final builds (a save still loading or an
            // unmapped tree gives partial counts that must never overwrite the remembered path). An orb being tried
            // out is not a purchase: like a test build, it is decided in memory only, so trying an orb never moves
            // the remembered path through the tie rule.
            string key = KeyOf(pm != null ? pm.Player : null, name);
            bool tryingOut = practising != Orb.None && !owned.Contains(practising);
            build = pm != null && TreeMapped && !loading.Contains(pm)
                ? Paths.Stamp(build, key, extra.Count > 0 || without.Count > 0, name, tryingOut)
                : Paths.Peek(build, key);

            string extraText = extra.Count > 0 || without.Count > 0
                ? ", TEST BUILD" + (extra.Count > 0 ? $" +{string.Join("+", extra.Select(o => o.ToString()).ToArray())}" : "")
                                 + (without.Count > 0 ? $" -{string.Join("-", without.Select(o => o.ToString()).ToArray())}" : "")
                : "";
            string text = build + extraText;
            if (!serverLogged.TryGetValue(name, out string last) || last != text) {
                serverLogged[name] = text;
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Build: '{name}' = {build} (owned {owned.Count} archery orbs{(practising != Orb.None ? $", practising {practising}" : "")}){extraText}");
            }
            return build;
        }

        static void Store(PlayerProgressionManager pm, Build build) {
            serverCache[pm] = build;
            if (serverCache.Count <= CacheTrimAt) return;
            foreach (PlayerProgressionManager gone in serverCache.Keys.Where(k => k == null).ToList()) serverCache.Remove(gone);
        }

        /// <summary>Server: the player's archery build (owned orbs + the orb being tried out + the test build),
        /// cached per progression manager. <c>default(Build)</c> for null.</summary>
        internal static Build ServerBuild(Player? p) {
            if (p == null) return default;
            PlayerProgressionManager pm = p.ProgressionManager;
            if (pm != null && serverCache.TryGetValue(pm, out Build cached)) return cached;
            Build build = Compute(pm, NameOf(p), out _);
            if (pm != null && TreeMapped && !loading.Contains(pm)) Store(pm, build);
            return build;
        }

        /// <summary>Server only, never on the wire: whether the player has this orb in the same set the build is made from (owned +
        /// the orb being tried out + the test build, minus its Without). For orbs only the server acts on (Auto Dock has no build
        /// bit; the side-orb bits 16 and 32 stay reserved).</summary>
        internal static bool ServerHas(Player? p, Orb orb) {
            if (p == null || !NetworkSceneManager.IsServer) return false;
            if (NewOrbs.All.Contains(orb) && !Tree.OrbOn(orb)) return false; // switched off: off everywhere, the test build included
            PlayerProgressionManager pm = p.ProgressionManager;
            string name = NameOf(p);
            return TestBuildRule.Combine(RealOwned(pm), Practising(pm), Lent(name), Hidden(name)).Contains(orb);
        }

        /// <summary>Server: the ultimates this manager owns (owned orbs + the test build, no practice).</summary>
        internal static Ultimates ServerOwnedUltimates(PlayerProgressionManager? pm) {
            if (pm == null) return Ultimates.None;
            var all = RealOwned(pm);
            all.AddRange(Lent(NameOf(pm.Player)));
            return Build.From(all).Ultimates;
        }

        /// <summary>Server: recompute this manager's build, bring the test build's game-skill grants in line, log on change.</summary>
        static readonly int T_Refresh = Timing.Section("Builds.Refresh");
        internal static void Refresh(PlayerProgressionManager pm) {
            using var _t = Timing.Begin(T_Refresh);
            if (!NetworkSceneManager.IsServer || pm == null || loading.Contains(pm)) return;
            Build build = Compute(pm, NameOf(pm.Player), out List<Orb> owned);
            if (TreeMapped) Store(pm, build);
            else serverCache.Remove(pm);
            KitHooks.Refreshed?.Invoke(pm, owned); // the play-test kit: its granted game-skill levels and FreePoints
        }

        /// <summary>Server, the test build's real purchase (the play-test kit's TestTools): hold every refresh of this manager, as while a save loads,
        /// so the many sales and purchases end in one refresh (<see cref="EndBatch"/>) and the main path is decided once.</summary>
        internal static void BeginBatch(PlayerProgressionManager pm) {
            if (pm == null) return;
            loading.Add(pm);
            serverCache.Remove(pm);
        }

        /// <summary>Ends <see cref="BeginBatch"/> with the one refresh (always call it, in a finally).</summary>
        internal static void EndBatch(PlayerProgressionManager pm) {
            if (pm == null) return;
            loading.Remove(pm);
            Refresh(pm);
        }

        /// <summary>Settings reloaded: forget every cached build and refresh the known players (and any with test-build grants).</summary>
        static readonly int T_ReloadAll = Timing.Section("Builds.ReloadAll");
        internal static void ReloadAll(IEnumerable<PlayerProgressionManager> alsoThese) {
            using var _t = Timing.Begin(T_ReloadAll);
            var known = new List<PlayerProgressionManager>(serverCache.Keys);
            foreach (PlayerProgressionManager pm in alsoThese) if (!known.Contains(pm)) known.Add(pm);
            serverCache.Clear();
            foreach (PlayerProgressionManager pm in known) if (pm != null) Refresh(pm);
        }

        /// <summary>Client: the build the server last sent for this player (<c>default</c> until one arrives).</summary>
        internal static Build ClientBuild(string? playerName) =>
            playerName != null && clientCache.TryGetValue(playerName, out Build b) ? b : default;

        /// <summary>Client: the server has sent a build for this player (ClientBuild is not just the default).</summary>
        internal static bool ClientBuildKnown(string? playerName) => playerName != null && clientCache.ContainsKey(playerName);

        /// <summary>Client: the local player's build, as the server last sent it. Before any bits arrived: the local
        /// player's own owned orbs (no practice, no test build) with the local file's main path.</summary>
        internal static Build LocalBuild() {
            IPlayer? me = Player.Current;
            string? name = me?.UserInfo?.Username;
            if (name != null && clientCache.TryGetValue(name, out Build b)) return b;
            if (me == null || !TreeMapped) return default;
            Build own = Build.From(RealOwned(me.ProgressionManager));
            return own.With(Paths.LocalIncumbent(own));
        }

        /// <summary>Client: the server has sent this client the local player's own build.</summary>
        internal static bool LocalBuildKnown {
            get {
                string? me = Player.Current?.UserInfo?.Username;
                return me != null && clientCache.ContainsKey(me);
            }
        }

        // ---------------------------------------------------------------- server: refresh points

        // G5: the five one-line slot patches below (UnlockSlot, LockSlot, PracticeSlot, EndPractice, PathExperience.LockSlot) all
        // run through here; the spike line names which one.
        static readonly int TRefreshed = Timing.Section("patch PlayerProgressionManager slot changes (5 patches) / Builds.Refreshed");
        static void Refreshed(PlayerProgressionManager? pm, string patch) {
            using var _t = Timing.Begin(TRefreshed, patch);
            try {
                if (!NetworkSceneManager.IsServer || pm == null) return;
                if (livePatches.Add(patch)) { if (Logs.Summary) MelonLogger.Msg($"[Archery] Build: refresh patch on {patch} is live"); }
                Refresh(pm);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Build: refresh after {patch} failed: {e}"); }
        }

        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.UnlockSlot))]
        static class OnUnlockSlot {
            [HarmonyPriority(Priority.Last)]
            static void Postfix(PlayerProgressionManager __instance) => Refreshed(__instance, "PlayerProgressionManager.UnlockSlot");
        }

        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.LockSlot))]
        static class OnLockSlot {
            static void Postfix(PlayerProgressionManager __instance) => Refreshed(__instance, "PlayerProgressionManager.LockSlot");
        }

        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.PracticeSlot))]
        static class OnPracticeSlot {
            static void Postfix(PlayerProgressionManager __instance) => Refreshed(__instance, "PlayerProgressionManager.PracticeSlot");
        }

        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.EndPractice))]
        static class OnEndPractice {
            static void Postfix(PlayerProgressionManager __instance) => Refreshed(__instance, "PlayerProgressionManager.EndPractice");
        }

        // Selling reaches PathExperience.LockSlot also from RemoveFreePoints/RemoveSlots, with no manager call.
        [HarmonyPatch(typeof(PathExperience), nameof(PathExperience.LockSlot))]
        static class OnPathLockSlot {
#if QUEST
            static void Postfix(PathExperience __instance) => Refreshed(__instance.targetPlayer, "PathExperience.LockSlot");
#else
            static void Postfix(PlayerProgressionManager ___targetPlayer) => Refreshed(___targetPlayer, "PathExperience.LockSlot");
#endif
        }

        // UpdateCounts clears the game's skill levels and recounts them from owned slots only: the test build's
        // granted levels go back on top.
        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.UpdateCounts))]
        static class OnUpdateCounts {
            static readonly int T = Timing.Section("patch PlayerProgressionManager.UpdateCounts / Builds.OnUpdateCounts");
            static void Postfix(PlayerProgressionManager __instance) {
                using var _t = Timing.Begin(T);
                try { if (NetworkSceneManager.IsServer && __instance != null) KitHooks.Recounted?.Invoke(__instance); } // the play-test kit re-adds its granted levels
                catch (Exception e) { MelonLogger.Warning($"[Archery] Build: test build re-apply failed: {e}"); }
                Refreshed(__instance, "PlayerProgressionManager.UpdateCounts");
            }
        }

        [HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.LoadFrom))]
        static class OnLoadFrom {
            static readonly int TPrefix = Timing.Section("patch PlayerProgressionManager.LoadFrom / Builds.OnLoadFrom prefix");
            static readonly int TPostfix = Timing.Section("patch PlayerProgressionManager.LoadFrom / Builds.OnLoadFrom postfix");
            static readonly int TFinalizer = Timing.Section("patch PlayerProgressionManager.LoadFrom / Builds.OnLoadFrom finalizer");
            static void Prefix(PlayerProgressionManager __instance) {
                using var _t = Timing.Begin(TPrefix);
                if (NetworkSceneManager.IsServer && __instance != null) { loading.Add(__instance); serverCache.Remove(__instance); }
            }

            static void Postfix(PlayerProgressionManager __instance) {
                using var _t = Timing.Begin(TPostfix);
                loading.Remove(__instance);
                Refreshed(__instance, "PlayerProgressionManager.LoadFrom");
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null) return;
                    Ultimates real = Build.From(RealOwned(__instance)).Ultimates; // test build excluded for this line
                    int count = 0;
                    for (int bits = (int)real; bits != 0; bits &= bits - 1) count++;
                    if (count >= 2)
                        MelonLogger.Error($"[Archery] Build: '{NameOf(__instance.Player)}' OWNS TWO ULTIMATES in the save ({real}); both stay active and the moment of release decides");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Build: ultimate check after load failed: {e.Message}"); }
                try { if (NetworkSceneManager.IsServer && __instance != null) LogVolleyLevel(__instance); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Volley skill: level check after load failed: {e.Message}"); }
            }

            // Owners of Two-Shot / Three-Shot keep their ghosts only if those slots already pointed at the Volley skill
            // while the save's slots were read (the tree is edited inside GetProfessionTree, before that).
            static void LogVolleyLevel(PlayerProgressionManager pm) {
                string name = NameOf(pm.Player);
                int level = VolleySkill.Skill != null ? pm.GetLevel(VolleySkill.Skill) : 0;
                if (level > 0) { if (Logs.Summary) MelonLogger.Msg($"[Archery] Volley skill: level {level} for '{name}' after loading"); }
                if (!TreeMapped || !pm.PathExperiences.TryGetValue(Tree.RangedPath, out PathExperience exp) || exp == null) return;
                bool ownsVolleySlot = (Tree.TwoShotSlot != null && exp.UnlockedSlots.Contains(Tree.TwoShotSlot.Hash))
                                      || (Tree.ThreeShotSlot != null && exp.UnlockedSlots.Contains(Tree.ThreeShotSlot.Hash));
                if (ownsVolleySlot && level == 0)
                    MelonLogger.Error($"[Archery] Volley skill: '{name}' owns Two-Shot/Three-Shot slots but the Volley level is 0 (the tree was not re-pointed before the save loaded)");
            }

            // Runs even when the load throws, so a manager is never left marked as loading.
            static Exception? Finalizer(Exception? __exception, PlayerProgressionManager __instance) {
                using var _t = Timing.Begin(TFinalizer);
                loading.Remove(__instance);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(PickupBasedSkill), nameof(PickupBasedSkill.SendSerializedMessage))]
        static class Owner {
            static void Prefix(IPlayer __0) => owner = __0;
#if QUEST
            // Quest: the build bits are read here, not in a patch on SerializeInternal (see Bits). SerializeInternal is the last
            // thing the game's SendSerializedMessage reads, so the stream is where that patch would find it. First: the other
            // postfixes on this method use the build this reads.
            [HarmonyPriority(Priority.First)]
            static void Postfix(PickupBasedSkill __instance, Il2CppAlta.Serialization.Stream __1) {
                try { Bits.Postfix(__instance, __1); } finally { owner = null; }
            }
#else
            static void Postfix() => owner = null;
#endif
        }

        // SerializeInternal is the last field of the status message (PickupBasedSkill.SendSerializedMessage), so
        // our block goes right after the game's own fields.
        // Quest: never patched there. The game's SerializeInternal is an empty method: a few bytes of native code, smaller than the
        // patch's jump, so patching it overwrote the code beside it and the game crashed (signal 11) on the first bow-skill status.
#if !QUEST
        [HarmonyPatch(typeof(PickupBasedSkill), "SerializeInternal")]
#endif
        static class Bits {
            static readonly int T = Timing.Section("patch PickupBasedSkill.SerializeInternal / Builds.Bits");
#if QUEST
            internal static void Postfix(PickupBasedSkill __instance, Il2CppAlta.Serialization.Stream stream) {
                uint ___pickupIdentifier = __instance.pickupIdentifier;
                ToolBasedSkillStatus ___status = __instance.status;
#else
            static void Postfix(PickupBasedSkill __instance, Alta.Serialization.Stream stream, uint ___pickupIdentifier, ToolBasedSkillStatus ___status) {
#endif
                using var _t = Timing.Begin(T);
#if QUEST
                if (!__instance.Is<BowDrawSkill>() || stream == null) return; // both sides know the skill, so writer and reader agree
#else
                if (!(__instance is BowDrawSkill) || stream == null) return; // both sides know the skill, so writer and reader agree
#endif
                try {
                    if (NetworkSceneManager.IsServer) {
                        if (stream.IsWriting && !loggedFired) { loggedFired = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Build bits: status-message postfix fired on the server (writing)"); }
                        if (stream.IsWriting) Write(stream);
                    }
                    else if (stream.IsReading) Read(__instance, stream, ___pickupIdentifier, ___status);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Build bits: failed: {e}"); }
            }
        }

        static readonly int T_Write = Timing.Section("Builds.Write (status bits)");
#if QUEST
        static void Write(Il2CppAlta.Serialization.Stream stream) {
#else
        static void Write(Alta.Serialization.Stream stream) {
#endif
            using var _t = Timing.Begin(T_Write);
#if QUEST
            if (!owner.Is(out Player p)) return;
#else
            if (!(owner is Player p)) return;
#endif
            if (!Tree.Ready || Tree.OrbBySlot.Count == 0) {
                if (!warnedNoTree) { warnedNoTree = true; MelonLogger.Warning("[Archery] Build bits: archery tree not injected; sending none"); }
                return;
            }
            Build build = ServerBuild(p);
            BuildBits.Write(new StreamBits(stream), build, Cfg.SharedShort);
            if (!loggedWrite) { loggedWrite = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Build bits: status-message patch is live (writing)"); }
        }

        static readonly int T_Read = Timing.Section("Builds.Read (status bits)");
#if QUEST
        static void Read(PickupBasedSkill skill, Il2CppAlta.Serialization.Stream stream, uint arrow, ToolBasedSkillStatus status) {
#else
        static void Read(PickupBasedSkill skill, Alta.Serialization.Stream stream, uint arrow, ToolBasedSkillStatus status) {
#endif
            using var _t = Timing.Begin(T_Read, arrow);
            var bits = new StreamBits(stream);
            WireRead result = BuildBits.TryRead(bits, out Build build, out byte check);
            if (!loggedRead) { loggedRead = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Build bits: status-message patch is live (reading)"); }
            string name = NameOf(owner);
            switch (result) {
                case WireRead.Ok:
                    int left = bits.BitsLeft;
                    if (left >= 8)
                        MelonLogger.Warning($"[Archery] Build bits: {left} unread bits after the build bits on {skill.name}; the layouts disagree");
                    if (!clientCache.TryGetValue(name, out Build old) || old != build) {
                        clientCache[name] = build;
                        if (Logs.Summary) MelonLogger.Msg($"[Archery] Build: '{name}' is now {build} (from the server)");
                    }
                    // The local file follows the server, except while the local player tries an orb out at the shrine:
                    // that build counts the tried orb, and its path must not be remembered. Checked on every status
                    // (not only when the build changes): after a purchase the build is the one already seen during the
                    // try-out, and the first status after it must store the real path. Only the file write is held
                    // back; the build above is used as before.
                    if (name == Player.Current?.UserInfo?.Username && !(ShrineView.Open && ShrineView.Held != null)) Paths.FromServer(build.Main);
                    if ((check & BuildBits.CheckMask) == (Cfg.SharedShort & BuildBits.CheckMask)) {
                        if (!loggedMatch) { loggedMatch = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] Settings: shared settings match the server (cfg {check:x2})"); }
                    }
                    else if (!warnedMismatch) {
                        warnedMismatch = true;
                        loggedMatch = false; // once fixed, the next match logs its line again
                        MelonLogger.Error($"[Archery] Settings: SHARED SETTINGS DIFFER FROM THE SERVER (server {check & BuildBits.CheckMask:x2}, this client {Cfg.SharedShort & BuildBits.CheckMask:x2}). This game could not take the server's settings: read the '[Archery] Settings:' and '[Archery] Join:' lines above.");
                    }
                    break;
                case WireRead.Absent:
                    break;

                default:
                    if (!warnedUnknown) {
                        warnedUnknown = true;
                        MelonLogger.Warning($"[Archery] Build bits: '{name}' {skill.name}/{status} carries bits this version does not understand ({result}); ignored");
                    }
                    break;
            }
        }
    }
}
