using System;
using System.Collections.Generic;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Client: the archery (Ranged) skill shrine the LOCAL player has open, its orbs as they arrive, what the local
    // player owns on it, and the orb in the try-out area. Read-only: nothing of the game's is changed here.
    //
    // How the game opens a shrine on a client (_decomp\ProgressionShrine.cs SyncCurrentPlayer): it sets CurrentManager
    // (which fires UpdatedPlayer, and orbs that synced early run SkillOrb.SyncFull's Add -> InitializeSlot ->
    // InitializeSkill then), sets IsOpen and calls OpenOnClient. Orbs that sync later initialise one by one afterwards.
    // Every orb of a shrine opened by someone else reads SkillStatus.OtherPlayer here (SkillOrb.SyncFull / SyncAllowed);
    // the same test is made on the shrine's manager, so such a shrine is never Open.
    //
    // Besides that, for the tablet (which every client shows): the archery shrine this client has loaded, whoever uses
    // it (Site), and whether anyone has its tree open and who (SiteOpen, Opener). A client knows a shrine's profession
    // without the tree being opened: the server sends SkillShrine.SyncPath to every player the shrine is loaded for
    // (_decomp\SkillShrine.cs:196-198). It knows who has the tree open from ProgressionShrine.SyncCurrentPlayer, which
    // goes to every client in range and runs OpenOnClient or Close there (_decomp\ProgressionShrine.cs:62-83, :133).
    internal static class ShrineView {
        static SkillShrine? shrine;
        static readonly List<SkillOrb> orbs = new List<SkillOrb>();
        static bool loggedOther;

        /// <summary>True while a Ranged shrine is open for the local player.</summary>
        internal static bool Open => shrine != null;
        /// <summary>The open shrine (null when none).</summary>
        internal static SkillShrine? Shrine => shrine;
        /// <summary>The open shrine's orbs that have arrived (never one reading OtherPlayer).</summary>
        internal static IReadOnlyList<SkillOrb> Orbs => orbs;
        /// <summary>The archery orbs the local player owns, read from the orbs' statuses.</summary>
        internal static HashSet<Orb> Owned { get; } = new HashSet<Orb>();
        /// <summary>The local player's main path as the shrine shows it: the server's when its build is of the owned orbs
        /// alone, else decided from the owned orbs and the last trusted path (never from an orb that is only tried out).</summary>
        internal static MainPath Incumbent { get; private set; }
        // The last main path that was trusted: seeded when the shrine opens, moved on by the server's word or by a real
        // purchase or sale (an orb's status changing), never by orbs still arriving.
        static MainPath trusted;
        /// <summary>The orb in the try-out area, or null.</summary>
        internal static SkillOrb? Held { get; private set; }

        // Every skill shrine (any profession) this client has loaded: added when the game initialises it or opens it,
        // dropped when it is destroyed. The archery one among them is the Site.
        static readonly List<SkillShrine> known = new List<SkillShrine>();
        static SkillShrine? site;
        static bool siteOpen, openerLocal;
        static string opener = "";
        static PlayerProgressionManager? openerManager;
        static float nextSurvey;
        const float SurveySeconds = 0.5f;

        /// <summary>The archery (Ranged) shrine this client has loaded, open or not; null when there is none.</summary>
        internal static SkillShrine? Site => site != null ? site : null;
        /// <summary>True while someone (anyone) has the tree open at <see cref="Site"/>.</summary>
        internal static bool SiteOpen => siteOpen && site != null;
        /// <summary>True while it is the local player who has it open.</summary>
        internal static bool SiteMine => SiteOpen && openerLocal;
        /// <summary>The name of the player who has the tree open at the Site ("" when nobody has).</summary>
        internal static string Opener => SiteOpen ? opener : "";

        /// <summary>Where the player who has the tree open is: their head, else their body (the controller); false when nobody
        /// has it open or that player has no body here (the caller then uses its own reader spot). The `Player` network
        /// entity itself sits in the scene's Players folder and never moves (_decomp\Player.cs:257), so it is never used;
        /// the body is Player.PlayerController (_decomp\Player.cs:127), linked on every client for every player
        /// (_decomp\PlayerController.cs:270), with its head at PlayerController.Head (_decomp\PlayerController.cs:129).</summary>
        internal static bool OpenerPosition(out Vector3 at) {
            at = default;
            try {
                if (!SiteOpen || openerManager == null) return false;
                Player player = openerManager.Player;
                if (player == null) return false;
                PlayerController controller = player.PlayerController;
                if (controller == null) return false;   // Unity's null: also a destroyed controller
                Transform head = controller.Head;
                at = head != null ? head.position : controller.transform.position;
                return true;
            }
            catch { return false; }   // a player object torn down this frame
        }

        /// <summary>The shrine opened or closed (for anyone), an orb arrived, left or changed status, the held orb changed,
        /// the archery shrine appeared or went, or the settings reloaded.</summary>
        internal static event Action? Changed;

        internal static void Init() {
            if (ApplicationManager.IsHeadless) return;
            Cfg.Reloaded += () => { if (Open || !(site is null)) Raise(); };
        }

        static void Know(SkillShrine s) { if (s != null && !known.Contains(s)) known.Add(s); }

        /// <summary>Looks at the loaded shrines again: which one is the archery shrine, is its tree open, for whom.
        /// True when any of that changed. <paramref name="closing"/>: a shrine the game is closing right now (its IsOpen
        /// still reads true inside CloseInternal). Allocates only when the opener changes (the name, the log line).</summary>
        static bool Survey(SkillShrine? closing = null) {
            try { return Look(closing); }
            catch (Exception e) {
                // a shrine or player object torn down mid-read: nobody has it open as far as this client can tell
                if (!warnedSurvey) { warnedSurvey = true; MelonLogger.Warning($"[Archery] Shrine: could not read the loaded shrines: {e.Message}"); }
                bool was = siteOpen;
                siteOpen = false; openerLocal = false; opener = ""; openerManager = null;
                return was;
            }
        }
        static bool warnedSurvey;

        static readonly int T_Look = Timing.Section("ShrineView.Look");
        static bool Look(SkillShrine? closing) {
            using var _t = Timing.Begin(T_Look);
            SkillShrine? found = null, open = null;
            for (int i = known.Count - 1; i >= 0; i--) {
                SkillShrine s = known[i];
                if (s == null) { known.RemoveAt(i); continue; }
                if (!IsRanged(s)) continue;
#if QUEST
                if (found == null || Il.Same(s, site)) found = s;   // the one already shown stays, should there be two
#else
                if (found == null || ReferenceEquals(s, site)) found = s;   // the one already shown stays, should there be two
#endif
#if QUEST
                if (s.IsOpen && !Il.Same(s, closing) && s.CurrentManager != null) open = s;
#else
                if (s.IsOpen && !ReferenceEquals(s, closing) && s.CurrentManager != null) open = s;
#endif
            }
            if (open != null) found = open;   // a tree someone has open wins

            bool nowOpen = false, nowLocal = false;
            string nowOpener = "";
            PlayerProgressionManager? manager = null;
            if (open != null) {
                manager = open.CurrentManager;
                nowOpen = manager != null;
                if (nowOpen) {
                    nowLocal = IsLocal(open);
                    // The name is read once per opening: the player object can go before the shrine closes. A name not
                    // known yet ("?": the player's info had not arrived when the tree opened) is read again on each survey
                    // (twice a second, never per frame) until it resolves; the change then raises Changed and the tablet
                    // writes the name.
#if QUEST
                    bool sameOpening = siteOpen && Il.Same(manager, openerManager) && Il.Same(found, site);
#else
                    bool sameOpening = siteOpen && ReferenceEquals(manager, openerManager) && ReferenceEquals(found, site);
#endif
                    nowOpener = sameOpening && !Unresolved(opener) ? opener : NameOfOpener(manager!);
                    if (sameOpening && Unresolved(opener) && !Unresolved(nowOpener))
                        { if (Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: the opener's name is now known: '{nowOpener}'"); }
                }
            }
#if QUEST
            if (Il.Same(found, site) && nowOpen == siteOpen && nowLocal == openerLocal && nowOpener == opener) return false;
#else
            if (ReferenceEquals(found, site) && nowOpen == siteOpen && nowLocal == openerLocal && nowOpener == opener) return false;
#endif
            bool wasOpen = siteOpen;
            string was = opener;
            site = found; siteOpen = nowOpen; openerLocal = nowLocal; opener = nowOpener; openerManager = nowOpen ? manager : null;
            if (nowOpen && (!wasOpen || was != nowOpener)) { if (Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: the archery tree is open for {(nowLocal ? "you" : "'" + nowOpener + "'")}"); }
            else if (wasOpen && !nowOpen) { if (Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: the archery tree is closed ({(was.Length > 0 ? "'" + was + "'" : "its player")} left it)"); }
            return true;
        }

        static bool Unresolved(string name) => name.Length == 0 || name == "?";

        static string NameOfOpener(PlayerProgressionManager manager) {
            try { return manager.Player != null ? Builds.NameOf(manager.Player) : "?"; }
            catch { return "?"; }
        }

        internal static bool IsOwned(SkillStatus s) =>
            s == SkillStatus.TestingLockedOwned || s == SkillStatus.OwnedLocked || s == SkillStatus.TestingOwned || s == SkillStatus.Owned;

        /// <param name="settled">An orb's status changed (a purchase or a sale): the decision becomes the trusted path.
        /// False while orbs arrive one by one, when a tie on a partial set must not move it.</param>
        static readonly int T_Refresh = Timing.Section("ShrineView.Refresh");
        static void Refresh(bool settled) {
            using var _t = Timing.Begin(T_Refresh);
            Owned.Clear();
            for (int i = orbs.Count - 1; i >= 0; i--) {
                SkillOrb o = orbs[i];
                if (o == null) { orbs.RemoveAt(i); continue; }
                if (o.Slot != null && IsOwned(o.Status) && Tree.OrbBySlot.TryGetValue(o.Slot.Hash, out Orb orb)) Owned.Add(orb);
            }
            // The server's build counts an orb that is only being tried out, so its main path is used only when its line
            // counts are those of the owned orbs (ShrineIncumbent). Nothing is stored here.
            bool known = Builds.LocalBuildKnown;
            Build server = known ? Builds.LocalBuild() : default;
            Incumbent = ShrineIncumbent.Decide(known, server, Build.From(Owned), trusted, out bool fromServer);
            if ((fromServer || settled) && Incumbent != MainPath.None) trusted = Incumbent;
        }

        static void Raise(bool settled = false) {
            try { Refresh(settled); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: refresh failed: {e.Message}"); }
            try { Changed?.Invoke(); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: change handler failed: {e.Message}"); }
        }

        static void Reset() {
            shrine = null;
            orbs.Clear();
            Owned.Clear();
            Held = null;
        }

        /// <summary>Called each frame (clients): a shrine unloaded without closing is let go at once; twice a second the
        /// loaded shrines are looked at again (a profession that arrived after the shrine did, an opening whose hook was
        /// missed). Nothing is allocated unless something changed.</summary>
        internal static void Frame() {
            bool changed = false;
            if (!(shrine is null) && shrine == null) {   // the local player's open shrine was unloaded
                Reset();
                if (Logs.Summary) MelonLogger.Msg("[Archery] Shrine: closed (the shrine was unloaded)");
                changed = true;
            }
            bool siteGone = !(site is null) && site == null;   // the archery shrine was destroyed: the tablet goes this frame
            if (siteGone || Time.unscaledTime >= nextSurvey) {
                nextSurvey = Time.unscaledTime + SurveySeconds;
                bool had = !(site is null);
                if (siteGone) { site = null; siteOpen = false; openerLocal = false; opener = ""; openerManager = null; changed = true; }
                if (Survey()) changed = true;
                if (had != !(site is null)) { if (Logs.Summary) MelonLogger.Msg(site is null ? "[Archery] Shrine: the archery shrine is gone" : "[Archery] Shrine: the archery shrine is loaded here"); }
            }
            if (changed) Raise();
        }

        static bool IsLocal(SkillShrine s) {
            IPlayer? me = Player.Current;
            PlayerProgressionManager? pm = s.CurrentManager;
#if QUEST
            return me != null && pm != null && pm.Player != null && Il.Same(pm.Player, me);
#else
            return me != null && pm != null && pm.Player != null && pm.Player.Identifier == me.Identifier;
#endif
        }

        static bool IsRanged(SkillShrine s) => Tree.Ready && s.CurrentPath == Tree.RangedPath;

        static bool Accept(SkillOrb orb) =>
            orb != null && orb.Slot != null && orb.Status != SkillStatus.OtherPlayer && !orbs.Contains(orb);

        // ---------------------------------------------------------------- G3: what this client knows about buying (section 4)

        // The shrine's bought-orb ring (BoughtSkillVisualizer, one of SkillShrine.Visualizers) holds one crystal per bought skill of
        // every profession, up to the slot cap, and is sent to every client with the opener's unspent points of the shrine's path
        // (_decomp BoughtSkillVisualizer.cs:274-314 Serialize -> UpdateHolders(freePoints); re-sent on every points change,
        // ProgressionShrine.UpdateVisuals). The ring's filled holders are the skills held over all professions; UpdateHolders' argument
        // is the free points. Neither is a new message.
#if QUEST
        static readonly Dictionary<BoughtSkillVisualizer, int> freePoints = new Dictionary<BoughtSkillVisualizer, int>(Il.ByPointer<BoughtSkillVisualizer>());
#else
        static readonly Dictionary<BoughtSkillVisualizer, int> freePoints = new Dictionary<BoughtSkillVisualizer, int>();
#endif
        static bool warnedRing;

        [HarmonyPatch(typeof(BoughtSkillVisualizer), "UpdateHolders")]
        static class RingPoints {
            static readonly int T = Timing.Section("patch BoughtSkillVisualizer.UpdateHolders / ShrineView.RingPoints");
            static void Prefix(BoughtSkillVisualizer __instance, int freePoints) {
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || __instance == null) return;
                    if (ShrineView.freePoints.Count > 16) ShrineView.freePoints.Clear();
                    bool changed = !ShrineView.freePoints.TryGetValue(__instance, out int was) || was != freePoints;
                    ShrineView.freePoints[__instance] = freePoints;
                    if (changed && Held != null) Raise(); // the tablet's reason line may change
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: points hook failed: {e.Message}"); }
            }
        }

        // The local player practised an orb (PlayerProgressionManager.OnSlotPracticed -> SkillShrineTestingArea.Practiced on clients,
        // _decomp SkillShrine.cs:286): the tablet's "Try it first" line goes.
        [HarmonyPatch(typeof(SkillShrineTestingArea), nameof(SkillShrineTestingArea.Practiced))]
        static class PractisedHook {
            static readonly int T = Timing.Section("patch SkillShrineTestingArea.Practiced / ShrineView.PractisedHook");
            static void Postfix() {
                using var _t = Timing.Begin(T);
                try { if (!ApplicationManager.IsHeadless && shrine != null && Held != null) Raise(); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: practice hook failed: {e.Message}"); }
            }
        }

        static BoughtSkillVisualizer? Ring() {
            if (shrine == null) return null;
#if QUEST
            foreach (SkillShrineVisualizer v in shrine.Visualizers.Each()) if (v.Is(out BoughtSkillVisualizer b)) return b;
#else
            foreach (SkillShrineVisualizer v in shrine.Visualizers) if (v is BoughtSkillVisualizer b && b != null) return b;
#endif
            return null;
        }

        /// <summary>The opener's unspent points on the open shrine's path, as its bought-orb ring was last told; null when not known.</summary>
        internal static int? FreePoints() {
            BoughtSkillVisualizer? ring = Ring();
#if QUEST
            QuestWatch.Mark("ShrineView.FreePoints");
#endif
            return ring != null && freePoints.TryGetValue(ring, out int p) ? p : (int?)null;
        }

        /// <summary>Skills the local player holds over every profession: the open shrine's bought-orb ring's filled holders; null when
        /// it cannot be read.</summary>
        internal static int? SlotsHeld() {
            try {
                BoughtSkillVisualizer? ring = Ring();
                if (ring == null) return null;
#if QUEST
                QuestWatch.Mark("ShrineView.SlotsHeld: ring read");
#endif
                if (!(Traverse.Create(ring).Field("placedHolders").GetValue() is Array holders)) return null;
                int n = 0;
                foreach (object h in holders) if (h != null && Traverse.Create(h).Property("SlotHash").GetValue<uint>() != 0u) n++;
                return n;
            }
            catch (Exception e) {
                if (!warnedRing) { warnedRing = true; MelonLogger.Warning($"[Archery] Shrine: cannot read the bought-orb ring ({e.Message}); the tablet cannot tell a full slot cap from short points"); }
                return null;
            }
        }

        /// <summary>The local player has practised this slot (the game sends the practised slots to every client: Player.OnConnected
        /// -> SyncPracticedToPlayer, and each new one to everyone in range); null when this client has no record for its path.</summary>
        internal static bool? Practised(ProgressionSlot slot) {
#if QUEST
            Player? me = Player.Current?.TryCast<Player>();
#else
            Player? me = Player.Current as Player;
#endif
            PlayerProgressionManager? pm = me != null ? me.ProgressionManager : null;
#if QUEST
            // Quest: reading the game's PathExperiences (a dictionary keyed by an enum) from the mod aborts the game natively (no
            // AOT code for it); the game's own question, which its orbs ask on clients (SkillOrb.UpdateAllowed). It also says true
            // when the server does not require practice, as the game's orbs then do.
            if (pm == null || slot == null) return null;
            try { return pm.IsSlotPracticed(slot); } catch { return null; }
#else
            if (pm == null || slot == null || !pm.PathExperiences.TryGetValue(slot.Path, out PathExperience pe) || pe == null) return null;
            return pe.IsSlotPracticed(slot.Hash);
#endif
        }

        /// <summary>The held orb's slot: whether one of its parents is owned (any of; none needed for a root), and their names.</summary>
        internal static bool ParentOwned(ProgressionSlot slot, out string names) {
            var list = new List<string>();
            bool owned = slot.Dependencies == null || slot.Dependencies.Count == 0;
            ProfessionSkillTree? tree = Tree.Ready ? ProfessionSkillTree.GetProfessionTree(slot.Path) : null;
            if (slot.Dependencies != null)
                foreach (uint dep in slot.Dependencies) {
                    list.Add(Tree.OrbBySlot.TryGetValue(dep, out Orb o) ? Lines.Name(o) : tree != null && tree.GetSlot(dep, out ProgressionSlot s) && s != null ? s.Name : dep.ToString());
                    foreach (SkillOrb so in orbs) if (so != null && so.Slot != null && so.Slot.Hash == dep && IsOwned(so.Status)) { owned = true; break; }
                }
            names = string.Join(" or ", list.ToArray());
            return owned;
        }

        /// <summary>The owned orbs directly above this slot, by name ("A, B"): what the game makes the player sell first while the
        /// server's CanSellSkillsWithChildren is off (PathExperience.IsSlotAvailable; the game also lets one go that has another owned
        /// parent, which this list does not leave out).</summary>
        internal static string OwnedAbove(ProgressionSlot slot) {
            var names = new List<string>();
#if QUEST
            foreach (ProgressionSlot child in slot.InheritSlots.Each())
#else
            foreach (ProgressionSlot child in slot.InheritSlots)
#endif
                foreach (SkillOrb so in orbs)
                    if (so != null && so.Slot != null && so.Slot.Hash == child.Hash && IsOwned(so.Status)) {
                        names.Add(Tree.OrbBySlot.TryGetValue(child.Hash, out Orb o) ? Lines.Name(o) : child.Name);
                        break;
                    }
            return string.Join(", ", names.ToArray());
        }

        internal static ShrineSays Says(SkillStatus s) {
            switch (s) {
                case SkillStatus.Allowed: case SkillStatus.TestingAllowed: return ShrineSays.Buyable;
                case SkillStatus.CantBuy: case SkillStatus.TestingCantBuy: return ShrineSays.CantBuy;
                case SkillStatus.NotAllowed: return ShrineSays.NotAllowed;
                case SkillStatus.OtherPlayer: return ShrineSays.Unknown;
                case SkillStatus.OwnedLocked: case SkillStatus.TestingLockedOwned: return ShrineSays.OwnedLocked;
                default: return IsOwned(s) ? ShrineSays.Owned : ShrineSays.Unknown;
            }
        }

        // ---------------------------------------------------------------- patches (all return at once on the server)

        // ProgressionShrine.SyncCurrentPlayer -> OpenOnClient, only on clients (_decomp\ProgressionShrine.cs:70-76,
        // SkillShrine.cs:282). The server never calls it (ProgressionShrine.Open calls OpenOnServer).
        [HarmonyPatch(typeof(SkillShrine), "OpenOnClient")]
        static class Opened {
            static readonly int T = Timing.Section("patch SkillShrine.OpenOnClient / ShrineView.Opened");
            static void Postfix(SkillShrine __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || __instance == null) return;
                    Know(__instance);
                    if (!IsLocal(__instance) || !IsRanged(__instance)) {
                        bool wasMine = shrine == __instance;
                        if (wasMine) { Reset(); if (Logs.Summary) MelonLogger.Msg("[Archery] Shrine: closed"); }
                        if (!loggedOther) { loggedOther = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Shrine: a shrine opened for another player or another tree; its orbs are not tracked"); }
                        if (Survey() || wasMine) Raise();   // another player's archery tree: the tablet rises on this client too
                        return;
                    }
                    Reset();
                    shrine = __instance;
                    foreach (SkillOrb o in __instance.SpawnedOrbs) if (o != null && o.Shrine == __instance && Accept(o)) orbs.Add(o);
                    // The seed: the path the local file remembers (the server's word outside try-outs, Paths.FromServer);
                    // without one, whatever the server last said.
                    trusted = Paths.LocalRemembered;
                    if (trusted == MainPath.None && Builds.LocalBuildKnown) trusted = Builds.LocalBuild().Main;
                    Refresh(false);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Shrine: open for the local player ({orbs.Count} orbs, main path {Incumbent})");
                    Survey();
                    Raise();
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: open hook failed: {e.Message}"); }
            }
        }

        // NetworkEntity.InitializeBehaviours -> SkillShrine.Initialize, on every machine, when the shrine is loaded
        // (_decomp\SkillShrine.cs:169). Its profession arrives a little later (SyncPath); the survey in Frame picks it up.
        [HarmonyPatch(typeof(SkillShrine), nameof(SkillShrine.Initialize))]
        static class Loaded {
            static readonly int T = Timing.Section("patch SkillShrine.Initialize / ShrineView.Loaded");
            static void Postfix(SkillShrine __instance) {
                using var _t = Timing.Begin(T);
                try { if (!ApplicationManager.IsHeadless) Know(__instance); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: load hook failed: {e.Message}"); }
            }
        }

        // ProgressionShrine.Close -> CloseInternal (_decomp\SkillShrine.cs:290), on clients from SyncCurrentPlayer with no
        // player (ProgressionShrine.cs:78-81) or when the shrine is destroyed (DestroyDisable, :44-50).
        [HarmonyPatch(typeof(SkillShrine), "CloseInternal")]
        static class Closed {
            static readonly int T = Timing.Section("patch SkillShrine.CloseInternal / ShrineView.Closed");
            static void Postfix(SkillShrine __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || __instance == null) return;
                    bool mine = shrine != null && __instance == shrine;
                    if (mine) { Reset(); if (Logs.Summary) MelonLogger.Msg("[Archery] Shrine: closed"); }
                    // Inside CloseInternal the shrine still reads open (ProgressionShrine.Close clears IsOpen after it).
                    if (Survey(__instance) || mine) Raise();
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: close hook failed: {e.Message}"); }
            }
        }

        // SkillOrb.SyncFull's Add -> InitializeSlot -> InitializeSkill on clients (_decomp\SkillOrb.cs:312-335, :406-446):
        // the orb has its slot, shrine and status here.
        [HarmonyPatch(typeof(SkillOrb), "InitializeSkill")]
        static class OrbArrived {
            static readonly int T = Timing.Section("patch SkillOrb.InitializeSkill / ShrineView.OrbArrived");
            static void Postfix(SkillOrb __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || shrine == null || __instance == null || __instance.Shrine != shrine || !Accept(__instance)) return;
                    orbs.Add(__instance);
                    Raise();
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: orb hook failed: {e.Message}"); }
            }
        }

        // The status setter behind AlowedUpdated (_decomp\SkillOrb.cs:362-379); on clients it runs from SyncAllowed (:338-350).
        [HarmonyPatch(typeof(SkillOrb), "UpdateAllowed", new[] { typeof(SkillStatus), typeof(bool) })]
        static class OrbStatus {
            static readonly int T = Timing.Section("patch SkillOrb.UpdateAllowed / ShrineView.OrbStatus");
            static void Postfix(SkillOrb __instance, bool __result) {
                using var _t = Timing.Begin(T);
                try {
                    if (!__result || ApplicationManager.IsHeadless || shrine == null || !orbs.Contains(__instance)) return;
                    if (__instance.Status == SkillStatus.OtherPlayer) orbs.Remove(__instance);
                    Raise(true);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: status hook failed: {e.Message}"); }
            }
        }

        // An orb going away (destroyed, or disabled for a pool): _decomp\SkillOrb.cs:456.
        [HarmonyPatch(typeof(SkillOrb), "OnDisable")]
        static class OrbGone {
            static readonly int T = Timing.Section("patch SkillOrb.OnDisable / ShrineView.OrbGone");
            static void Postfix(SkillOrb __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless) return;
#if QUEST
                    bool held = Il.Same(Held, __instance);   // before the list test: a held orb that was never listed must not stay held
#else
                    bool held = ReferenceEquals(Held, __instance);   // before the list test: a held orb that was never listed must not stay held
#endif
                    if (held) Held = null;
                    if (!orbs.Remove(__instance) && !held) return;
                    Raise();
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: orb removal hook failed: {e.Message}"); }
            }
        }

        // The try-out area: SyncSkill -> Open(SkillOrb) -> Open() on clients (_decomp\SkillShrineTestingArea.cs:201-246),
        // Disable() (:312). Tutorial.cs patches the same two.
        [HarmonyPatch(typeof(SkillShrineTestingArea), "Open", new Type[0])]
        static class TryOutOpened {
            static readonly int T = Timing.Section("patch SkillShrineTestingArea.Open / ShrineView.TryOutOpened");
#if QUEST
            static void Postfix(SkillShrineTestingArea __instance) {
                SkillOrb ___openSkillOrb = __instance.openSkillOrb;
                SkillShrine ___shrine = __instance.shrine;
#else
            static void Postfix(SkillOrb ___openSkillOrb, SkillShrine ___shrine) {
#endif
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || shrine == null || ___shrine != shrine || Held == ___openSkillOrb) return;
                    Held = ___openSkillOrb;
                    Raise();
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: try-out hook failed: {e.Message}"); }
            }
        }

        [HarmonyPatch(typeof(SkillShrineTestingArea), nameof(SkillShrineTestingArea.Disable))]
        static class TryOutClosed {
            static readonly int T = Timing.Section("patch SkillShrineTestingArea.Disable / ShrineView.TryOutClosed");
#if QUEST
            static void Postfix(SkillShrineTestingArea __instance) {
                SkillShrine ___shrine = __instance.shrine;
#else
            static void Postfix(SkillShrine ___shrine) {
#endif
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || shrine == null || ___shrine != shrine || Held is null) return;
                    Held = null;
                    Raise();
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Shrine: try-out end hook failed: {e.Message}"); }
            }
        }
    }
}
