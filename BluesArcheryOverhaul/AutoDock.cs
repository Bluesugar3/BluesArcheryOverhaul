using System;
using System.Collections.Generic;
using System.Globalization;
using ATT.Saving;
using Alta.Global;
using Alta.Networking;
using Alta.Inventory;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Auto Dock (Quick side orb, any main path: a convenience; spec 2026-11-16 and research-autodock.md). After the owner
    // shoots, [AutoDock] Seconds later the server takes the next arrow out of a quiver the owner carries and docks it on that
    // bow's string with no hand (PickupDock docking is networked by the game: no new messages). The poll decision is
    // AutoDockRule (Rules.cs, tested).
    //   - Server: a postfix on BowString.OnShoot queues the bow for its owner (Builds.ServerHas: owned, tried out or lent;
    //     never when [Tree] AutoDockOrb is off). Poll (Main.OnUpdate, before the headless return) drops the entry if the
    //     string holds an arrow, the owner let go of the bow, grabbed the string or holds an arrow; at its time it docks the
    //     quiver's next arrow (dock.DockedPickup, the one a hand would get) with the owner as its last interactor, so the
    //     game ignores the archer's own body and bow at the shot. Quiver Gather tracks it as if drawn by hand. Never creates
    //     an arrow, never docks with a hand as interactor.
    //   - A refused dock waits for the owner's next grab of the string ([AutoDock] FallbackSeconds) and docks then.
    //   - Clients: the dock arrives as the game's own interactor-less dock right after the quiver's undock. The look: the
    //     game slides the arrow when the string dock does not snap; when it snaps, the game's pouch-gatherer ghost travels
    //     from the quiver to the string over GhostSeconds while the real arrow is hidden. The game's own knock sound plays
    //     (BowString.ArrowKnocked); a light tick in the shooter's bow hand. Every client sets the arrow's last interactor
    //     to the bow's holder.
    //   - Practice: the dock itself, while the orb is held for try-out (the try-out docks as a demonstration).
    //   - b2-review I-1: the game undocks the string whenever the bow itself is docked (BowString.PreBowDocked -> UndockAll), so
    //     a self-nocked arrow that was never shot falls out at every holster. The server remembers such arrows (unshot) and, once
    //     one is off the string and not in a hand, puts it back into the quiver it came from (AutoDockRule.Unshot, tested);
    //     with that quiver gone, full or no longer carried by its archer it stays where it is; it waits until the arrow has been
    //     loose PutBackSeconds (b3-review M-1).
    internal static class AutoDock {
        const float PollSeconds = 0.05f, RecentUndockSeconds = 0.5f, HapticSeconds = 0.03f;
        const int Cap = 64;

        static MelonPreferences_Entry<float> seconds = null!, fallbackSeconds = null!, putBackSeconds = null!, ghostSeconds = null!, haptic = null!, tryOutReach = null!;
        static MelonPreferences_Entry<bool> looks = null!, tryOutQuiver = null!;
        static MelonPreferences_Entry<int> tryOutQuiverHash = null!, tryOutArrowHash = null!, tryOutArrows = null!;
        static MelonPreferences_Entry<string> tryOutOffset = null!;

        internal static void Init() {
            var cat = Cfg.Category("AutoDock");
            // Server only (b2-review M-8): not [shared]; a client never reads them.
            seconds = cat.CreateEntry("Seconds", 0.35f, description: "Server. Auto Dock: seconds after a shot before the quiver's next arrow nocks itself.");
            fallbackSeconds = cat.CreateEntry("FallbackSeconds", 5f, description: "Server. Auto Dock: when the game refuses the dock, it is tried again the next time you grab the string, for at most this many seconds.");
            putBackSeconds = cat.CreateEntry("PutBackSeconds", 0.25f, description: "Server. Auto Dock: a self-nocked arrow that comes off the string unshot (the bow put away) goes back into its quiver once it has been loose this long, and only while the archer still carries that quiver.");
            looks = cat.CreateEntry("Looks", true, description: "Looks: the arrow is seen travelling from the quiver to the string, and a light tick in your bow hand. Off changes nothing the dock does.");
            ghostSeconds = cat.CreateEntry("GhostSeconds", 0.2f, description: "Seconds the arrow's ghost takes from the quiver to the string (when the string's dock snaps; otherwise the game slides the arrow itself).");
            haptic = cat.CreateEntry("Haptic", 0.15f, description: "Strength (0 to 1) of the tick in your bow hand when an arrow nocks itself. 0 = none.");
            // play-test 7 (the user: "I need the quiver to be able to test auto-docking"); server only
            tryOutQuiver = cat.CreateEntry("TryOutQuiver", true, description: "Server. Trying Auto Dock out at the shrine gives a quiver of arrows beside the try-out bow; Auto Dock takes its arrows while you try the orb out, worn or not. It goes when the try-out ends.");
            tryOutQuiverHash = cat.CreateEntry("TryOutQuiverPrefab", 64640, description: "Server. The quiver's prefab hash (the game's 'Quiver').");
            tryOutArrowHash = cat.CreateEntry("TryOutArrowPrefab", 36190, description: "Server. The arrows put in it (the game's 'Arrow Training', the try-out's own arrow).");
            tryOutArrows = cat.CreateEntry("TryOutArrows", 10, description: "Server. How many arrows the try-out quiver holds.");
            tryOutOffset = cat.CreateEntry("TryOutQuiverOffset", "0.6, 0.3, 0", description: "Server. Where the quiver appears: right, up, forward in metres from the try-out area's spawn point.");
            tryOutReach = cat.CreateEntry("TryOutReachMetres", 20f, description: "Server. Auto Dock uses a try-out quiver within this many metres of the archer trying the orb out.");
        }

        static bool On => Tree.OrbOn(Orb.AutoDock);
        static float PutBackSeconds => putBackSeconds != null ? Mathf.Max(0f, putBackSeconds.Value) : 0.25f;

        // ---------------------------------------------------------------- server: the quiver a player last drew from

#if QUEST
        static readonly Dictionary<PlayerController, PickupDock> lastQuiver = new Dictionary<PlayerController, PickupDock>(Il.ByPointer<PlayerController>());
#else
        static readonly Dictionary<PlayerController, PickupDock> lastQuiver = new Dictionary<PlayerController, PickupDock>();
#endif
        static readonly HashSet<string> quiversLogged = new HashSet<string>();
        static bool snapLogged;

        /// <summary>Server, from Quiver Gather's undock patch: <paramref name="owner"/> drew an arrow from this quiver by hand.</summary>
        internal static void NoteQuiver(PlayerController? owner, PickupDock dock) {
            if (owner == null || dock == null) return;
            if (lastQuiver.Count >= Cap && !lastQuiver.ContainsKey(owner)) lastQuiver.Clear();
            lastQuiver[owner] = dock;
        }

        static bool Usable(PickupDock? dock, PlayerController owner) {
            if (dock == null || !dock.HasDockedPickup || dock.QuantityStored < 1) return false;
            Pickup next = dock.DockedPickup;
            if (next == null || next.Entity == null || next.Entity.IsBeingDestroyed || next.IsInteractedWith || next.GetComponent<Arrow>() == null) return false;
            return QuiverGather.Carried(dock.GetComponentInParent<Pickup>(), owner);
        }

        static Pickup? Held(Controller? hand) {
            Interactor i = hand != null ? hand.Interactor : null!;
#if QUEST
            return i != null ? i.InteractingWith.As<Pickup>() : null;
#else
            return i != null ? i.InteractingWith as Pickup : null;
#endif
        }

        /// <summary>Server, Holy Arrow's KeepsArrow: where a kept arrow goes back: the quiver last drawn from by hand if still
        /// carried, else any worn one, with room for one arrow. Null: none.</summary>
        internal static PickupDock? ReturnQuiver(PlayerController pc) {
            lastQuiver.TryGetValue(pc, out PickupDock last);
            if (last != null && last.CanDockAmount(1) && QuiverGather.Carried(last.GetComponentInParent<Pickup>(), pc)) return last;
            foreach (Quiver q in pc.GetComponentsInChildren<Quiver>(true)) {
                PickupDock d = q != null ? q.GetComponent<PickupDock>() : null!;
                if (d != null && d.CanDockAmount(1)) return d;
            }
            return null;
        }

        /// <summary>The quiver whose next arrow is used: the one last drawn from by hand, else any worn (in the player's
        /// hierarchy, also inside a worn bag) or held one, with an arrow in it. Logged once per player.</summary>
        static readonly int T_FindQuiver = Timing.Section("AutoDock.FindQuiver");
        static PickupDock? FindQuiver(PlayerController pc, string name) {
            using var _t = Timing.Begin(T_FindQuiver, name);
            lastQuiver.TryGetValue(pc, out PickupDock last);
            var found = new List<PickupDock>();
            int worn = 0, held = 0;
            foreach (Quiver q in pc.GetComponentsInChildren<Quiver>(true)) {
                PickupDock d = q != null ? q.GetComponent<PickupDock>() : null!;
                if (d != null && !found.Contains(d)) { found.Add(d); worn++; }
            }
            foreach (Controller? hand in new[] { pc.LeftController, pc.RightController }) {
                Pickup? p = Held(hand);
                Quiver q = p != null ? p.GetComponentInChildren<Quiver>(true) : null!;
                PickupDock d = q != null ? q.GetComponent<PickupDock>() : null!;
                if (d != null && !found.Contains(d)) { found.Add(d); held++; }
            }
            PickupDock? pick = Usable(last, pc) ? last : null;
            if (pick == null) foreach (PickupDock d in found) if (Usable(d, pc)) { pick = d; break; }
            if (pick == null) pick = TryOutQuiver(pc);   // play-test 7: the shrine's try-out quiver, carried or not
            if (quiversLogged.Add(name))
                { if (Logs.Debug) MelonLogger.Msg($"[Archery] Auto Dock: quivers found for '{name}': {worn} worn, {held} held, last drawn from by hand: {(last != null ? "known" : "none")}; " +
                                $"using {(pick != null ? $"'{pick.name}' ({pick.QuantityStored} arrows)" : "none")}"); }
            return pick;
        }

        // ---------------------------------------------------------------- server: queued docks

        sealed class Entry {
            public BowString Bow = null!;
            public PlayerController Owner = null!;
            public string Name = "?";
            public float Due, RefusedAt;
            public bool Grabbed, Fallback;
        }

#if QUEST
        static readonly Dictionary<BowString, Entry> pending = new Dictionary<BowString, Entry>(Il.ByPointer<BowString>());
#else
        static readonly Dictionary<BowString, Entry> pending = new Dictionary<BowString, Entry>();
#endif
        static readonly List<BowString> scratch = new List<BowString>();
        static float nextPoll;

        // Every shot (a normal arrow, a Quickdraw 4 line, a Holy Arrow beam): Shooter.Shoot has undocked the arrow by now.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(BowString), "OnShoot")]
        static class Shot {
            static readonly int T = Timing.Section("patch BowString.OnShoot / AutoDock.Shot");
            [HarmonyPriority(Priority.Last)]
            static void Postfix(BowString __instance, Pickup __1) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null) return;
                    if (__1 != null && __1.Entity != null) unshot.Remove(__1.Entity.Identifier); // a self-nocked arrow was shot
                    if (!On) return;
                    PlayerController pc = __instance.Pickup != null ? __instance.Pickup.LastPlayerInteractor : null!;
                    if (pc == null && __1 != null) pc = __1.LastPlayerInteractor;
                    if (pc == null || pc.NetworkPlayer == null || !Builds.ServerHas(pc.NetworkPlayer, Orb.AutoDock)) return;
                    if (pending.Count >= Cap && !pending.ContainsKey(__instance)) pending.Clear();
                    string name = Builds.NameOf(pc.NetworkPlayer);
                    pending[__instance] = new Entry { Bow = __instance, Owner = pc, Name = name, Due = Time.time + Mathf.Max(0f, seconds.Value) };
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Auto Dock: '{name}' shot from '{__instance.name}'; the next arrow nocks itself in {seconds.Value.ToString("0.##", CultureInfo.InvariantCulture)} s");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: queueing failed: {e.Message}"); }
            }
        }
#endif

        // The owner's (or anyone's) grab of the string: a queued dock is dropped, a refused one docks now.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(BowString), "StringGrabbed")]
        static class Grab {
            static readonly int T = Timing.Section("patch BowString.StringGrabbed / AutoDock.Grab");
            static void Postfix(BowString __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || pending.Count == 0 || !pending.TryGetValue(__instance, out Entry e)) return;
                    State(e, out bool alive, out bool hasArrow, out bool holds, out bool handArrow);
                    if (AutoDockRule.DockOnGrab(e.Fallback, AutoDockRule.FallbackExpired(Time.time, e.RefusedAt, fallbackSeconds.Value), hasArrow, holds && alive, handArrow)) {
                        pending.Remove(__instance);
                        Dock(e, "on the grab after a refused dock");
                        return;
                    }
                    e.Grabbed = true;
                }
                catch (Exception ex) { MelonLogger.Warning($"[Archery] Auto Dock: grab check failed: {ex.Message}"); }
            }
        }
#endif

        static void State(Entry e, out bool alive, out bool stringHasArrow, out bool ownerHoldsBow, out bool handHoldsArrow) {
            BowString bow = e.Bow;
            PlayerController pc = e.Owner;
            alive = bow != null && pc != null && bow.StringDock != null;
            stringHasArrow = alive && bow!.StringDock!.HasDockedPickup;
            ownerHoldsBow = false;
            if (alive && bow!.Bow != null && bow.Bow.IsPickup && bow.Bow.Pickup != null)
                foreach (Interactor i in bow.Bow.Pickup.Interactors)
                    if (i != null && i.Controller != null && i.Controller.PlayerController == pc) { ownerHoldsBow = true; break; }
            handHoldsArrow = false;
            if (alive) {
                Pickup? l = Held(pc!.LeftController), r = Held(pc.RightController);
                handHoldsArrow = (l != null && l.GetComponent<Arrow>() != null) || (r != null && r.GetComponent<Arrow>() != null);
            }
        }

        /// <summary>Server, every frame (Main.OnUpdate, the headless server too): due docks happen, stale ones go.</summary>
        internal static void Poll() {
            if (tryOuts.Count > 0 && NetworkSceneManager.IsServer) TendTryOuts(); // fix batch D: a try-out whose player left ends
            if ((pending.Count == 0 && unshot.Count == 0) || Time.time < nextPoll || !NetworkSceneManager.IsServer) return;
            nextPoll = Time.time + PollSeconds;
            float now = Time.time;
            PollUnshot();
            scratch.Clear();
            scratch.AddRange(pending.Keys);
            foreach (BowString key in scratch) {
                if (!pending.TryGetValue(key, out Entry e)) continue;
                try {
                    State(e, out bool alive, out bool hasArrow, out bool holds, out bool handArrow);
                    bool grabbed = e.Grabbed || (alive && e.Bow.Pickup != null && e.Bow.Pickup.IsInteractedWith && now >= e.Due);
                    bool expired = e.Fallback && AutoDockRule.FallbackExpired(now, e.RefusedAt, fallbackSeconds.Value);
                    DockStep step = AutoDockRule.Poll(alive, hasArrow, holds, grabbed, handArrow, now >= e.Due, e.Fallback, expired);
                    if (step == DockStep.Wait) continue;
                    pending.Remove(key);
                    if (step == DockStep.Drop) {
                        string why = expired ? "the refused dock was not retried in time" : AutoDockRule.Why(alive, hasArrow, holds, grabbed, handArrow);
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Auto Dock: '{e.Name}' nothing docked ({why})");
                        continue;
                    }
                    Dock(e, "after the shot");
                }
                catch (Exception ex) { pending.Remove(key); MelonLogger.Warning($"[Archery] Auto Dock: '{e.Name}' dropped after an error: {ex.Message}"); }
            }
        }

        static readonly int T_Dock = Timing.Section("Auto Dock: dock an arrow (AutoDock.Dock)");
        static void Dock(Entry e, string when) {
            using var _t = Timing.Begin(T_Dock);
            BowString bow = e.Bow;
            PlayerController pc = e.Owner;
            PickupDock? quiver = FindQuiver(pc, e.Name);
            Pickup next;
            if (quiver != null) next = quiver.DockedPickup;
            else {
                // fix batch D (the ruling): a try-out whose quiver refused the training arrow nocks the try-out's own training arrow
                Pickup? training = TrainingArrow(pc, out bool wanted);
                if (training == null) {
                    if (wanted && !e.Fallback) {
                        e.Fallback = true; e.RefusedAt = Time.time; e.Grabbed = false; pending[bow] = e;
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Auto Dock: '{e.Name}' no training arrow in the try-out area yet; it docks on the next grab of the string");
                        return;
                    }
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Auto Dock: '{e.Name}' nothing docked (no carried quiver with an arrow)");
                    return;
                }
                next = training;
            }
            uint id = next.Entity.Identifier;
            if (!snapLogged) {
                snapLogged = true;
                PickupDockSettings s = bow.StringDock.Settings;
                if (Logs.Summary) MelonLogger.Msg(s != null
                    ? $"[Archery] Auto Dock: the string dock on '{bow.name}' {(s.IsSnapping ? "snaps" : "slides")} (scale duration {s.ScaleDuration.ToString("0.###", CultureInfo.InvariantCulture)} s)"
                    : $"[Archery] Auto Dock: the string dock on '{bow.name}' has no settings");
            }
            next.LastPlayerInteractor = pc; // so the game ignores the archer's own bow and body at the shot (BowString.IgnoreArrowCollisions)
            bool gathered = quiver != null && QuiverGather.TrackAutoDocked(next, quiver, pc);
            PickupDock.AllowedResult result;
            try { result = next.DockInto(null, bow.StringDock, 1, false); }
            catch (Exception ex) {
                // play-test 7: the knock sound threw after the dock (headless server); HeadlessKnock swallows that now, this is a fallback
                bool on = next.IsDocked && next.Dock == bow.StringDock;
                MelonLogger.Warning($"[Archery] Auto Dock: '{e.Name}' the dock threw ({ex.Message}); the arrow is {(on ? "on the string: counted as docked" : "not on the string")}");
                if (!on) throw;
                result = PickupDock.AllowedResult.Ok;
            }
            if (result == PickupDock.AllowedResult.Ok && !(next.IsDocked && next.Dock == bow.StringDock)) {
                MelonLogger.Warning($"[Archery] Auto Dock: '{e.Name}' the game said the dock was fine, but arrow {id} is not on the string; nothing docked"); // G5 (M-5): every level
                if (gathered) QuiverGather.Untrack(id);
                return;
            }
            if (result == PickupDock.AllowedResult.Ok) {
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Auto Dock: '{e.Name}' arrow {id} from {(quiver != null ? $"quiver '{quiver.name}'" : "the try-out area (its training arrow)")} onto '{bow.name}' {when}{(gathered ? " (Quiver Gather returns it)" : "")}");
                if (unshot.Count >= Cap) unshot.Clear();
                if (quiver != null) unshot[id] = new Unshot { Arrow = next, Quiver = quiver, Owner = pc, Name = e.Name };
                Shrine.Practise(pc.NetworkPlayer, Orb.AutoDock, "an arrow nocking itself after a shot");
                return;
            }
            if (gathered) QuiverGather.Untrack(id);
            if (e.Fallback) { if (Logs.Summary) MelonLogger.Msg($"[Archery] Auto Dock: '{e.Name}' the dock was refused again ({result}); nothing docked"); return; }
            e.Fallback = true;
            e.RefusedAt = Time.time;
            e.Grabbed = false;
            pending[bow] = e;
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Auto Dock: '{e.Name}' the game refused the dock ({result}); it docks on the next grab of the string, for {fallbackSeconds.Value.ToString("0.#", CultureInfo.InvariantCulture)} s");
        }

        // ---------------------------------------------------------------- server: the game's knock sound (play-test 7, 2b)

        // BowString.ArrowKnocked plays the knock sound as its last statement; on the headless server AudioManager.Resolve throws a
        // NullReferenceException there (the sound mapping is never loaded). The exception leaves PickupDock.OnDocked, so the other
        // ItemDocked handlers never run (the skill input's SetArrowEntity: Charged statuses named a stale arrow, and
        // SendChargingStarted threw "Pre IK" errors on a null one) and the rest of the game's own server dock (weight, colliders) is
        // skipped; Auto Dock's success was never recorded. Server only, exactly that exception (headless, from the audio code).
#if !QUEST   // a server's work: not patched on Quest
        [HarmonyPatch(typeof(BowString), "ArrowKnocked")]
#endif
        static class HeadlessKnock {
            static readonly int T = Timing.Section("patch BowString.ArrowKnocked / AutoDock.HeadlessKnock");
            static bool logged;
            static Exception? Finalizer(Exception? __exception) {
                using var _t = Timing.Begin(T);
                if (__exception == null) return null;
                if (!(__exception is NullReferenceException) || !ApplicationManager.IsHeadless || !NetworkSceneManager.IsServer) return __exception;
                string trace = __exception.StackTrace ?? "";
                if (trace.IndexOf("AudioManager", StringComparison.Ordinal) < 0 && trace.IndexOf("SoundEffect", StringComparison.Ordinal) < 0) return __exception;
                if (!logged) { logged = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Auto Dock: the knock sound's NullReferenceException on the headless server is swallowed (BowString.ArrowKnocked; said once)"); }
                return null;
            }
        }

        // ---------------------------------------------------------------- server: the shrine's try-out quiver (play-test 7, 2c)

        // Fix batch D (fixA-review I-2, the controller's ruling): the try-out must not be a way to obtain arrows.
        //   - The quiver stacks (PickupDock.DestroyPickupIfStackedInDock), and every arrow taken out of it is a new entity made by the
        //     game (CreateNewImage), set back to normal saving (OnUndocked). So every arrow that leaves it is tracked from the dock's
        //     own ItemUndocked and set to SaveMode.Ignore, and while the try-out lasts a tracked arrow may only go onto a bow string
        //     or back into this quiver (PickupDock.CheckAllowed refuses every other dock: the player's own quiver, a bag).
        //   - When the try-out ends (another orb opened, the shrine closed, the player gone, the server quitting) the quiver is
        //     emptied with the game's own DestroyAmountToEmpty, destroyed, and every tracked arrow is destroyed wherever it is (in a
        //     hand, on a string, lying anywhere).
        //   - No plain-arrow fallback: when the quiver refuses the training arrow, there is no quiver at all and Auto Dock nocks the
        //     try-out's own training arrow (the game respawns it after each shot) on the string instead (TrainingArrow).
        sealed class TryOut {
            public SkillShrineTestingArea Area = null!;
            public NetworkEntity? Quiver;
            public PickupDock? Dock;
            public PickupDockInteractionHandler? OnUndock;
            public readonly List<NetworkEntity> Arrows = new List<NetworkEntity>();
            // fixF I-1: id -> the entity tracked under it, kept per try-out so End can forget every one, alive or not
            public readonly Dictionary<uint, NetworkEntity> Ids = new Dictionary<uint, NetworkEntity>();
            public bool NockTraining;    // the quiver refused the training arrow: nock the try-out's own one instead
            public string Owner = "";    // the player whose try-out it is ("" when it could not be read)
        }
        static readonly List<TryOut> tryOuts = new List<TryOut>();
        // Ids of arrows that came out of a try-out quiver, with the entity each id was tracked for. The game reuses entity ids (a
        // fixed list of 100,000, wrapping), so a match needs the same entity object, not the id alone (fixF I-1); an entry goes
        // when its arrow is destroyed (DestroyedByScene) and, at the latest, when its try-out ends.
        static readonly Dictionary<uint, NetworkEntity> tracked = new Dictionary<uint, NetworkEntity>();
        static float nextTend;

        static ProgressionSlot? AutoDockSlot => Tree.SlotOf(Orb.AutoDock);

        static Vector3 Offset() {
            string[] p = (tryOutOffset.Value ?? "").Split(',');
            float F(int i) => p.Length > i && float.TryParse(p[i].Trim(), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
            return new Vector3(F(0), F(1), F(2));
        }

        /// <summary>Server: the try-out quiver for an archer who is trying Auto Dock out, within reach and with an arrow in it.</summary>
        static PickupDock? TryOutQuiver(PlayerController pc) {
            if (tryOuts.Count == 0 || pc == null || !Shrine.Trying(pc.NetworkPlayer, Orb.AutoDock)) return null;
            Vector3 at = pc.Head != null ? pc.Head.position : pc.transform.position;
            float reach = Mathf.Max(1f, tryOutReach.Value);
            foreach (TryOut t in tryOuts) {
                if (t.Quiver == null || t.Quiver.IsBeingDestroyed || t.Dock == null) continue;
                if ((t.Quiver.transform.position - at).sqrMagnitude > reach * reach) continue;
                PickupDock dock = t.Dock;
                if (!dock.HasDockedPickup || dock.QuantityStored < 1) continue;
                Pickup next = dock.DockedPickup;
                if (next == null || next.IsInteractedWith || next.GetComponent<Arrow>() == null) continue;
                return dock;
            }
            return null;
        }

        static readonly Collider[] trainingHits = new Collider[256];

        /// <summary>Server, the nock fallback: the try-out's own training arrow lying in the try-out area of an archer trying Auto Dock
        /// out whose try-out has no quiver (it refused the training arrow); null when none lies there (yet: the game respawns it
        /// once the shot one has flown off).</summary>
        static readonly int T_TrainingArrow = Timing.Section("scene AutoDock.TrainingArrow (overlap 10 m)");
        static Pickup? TrainingArrow(PlayerController pc, out bool wanted) {
            using var _t = Timing.Begin(T_TrainingArrow);
            wanted = false;
            if (tryOuts.Count == 0 || pc == null || !Shrine.Trying(pc.NetworkPlayer, Orb.AutoDock)) return null;
            Vector3 at = pc.Head != null ? pc.Head.position : pc.transform.position;
            float reach = Mathf.Max(1f, tryOutReach.Value);
            uint hash = (uint)Mathf.Max(0, tryOutArrowHash.Value);
            foreach (TryOut t in tryOuts) {
                if (!t.NockTraining || t.Area == null) continue;
                Transform folder = t.Area.SpawnFolder != null ? t.Area.SpawnFolder : t.Area.transform;
                if ((folder.position - at).sqrMagnitude > reach * reach) continue;
                wanted = true;
#if QUEST
                int n = Il.OverlapSphereNonAlloc(folder.position, 10f, trainingHits, ~0, QueryTriggerInteraction.Collide);
#else
                int n = Physics.OverlapSphereNonAlloc(folder.position, 10f, trainingHits, ~0, QueryTriggerInteraction.Collide);
#endif
                for (int i = 0; i < n; i++) {
                    Arrow a = trainingHits[i] != null ? trainingHits[i].GetComponentInParent<Arrow>() : null!;
                    Pickup p = a != null ? a.GetComponent<Pickup>() : null!;
                    if (p == null || p.Entity == null || p.Entity.IsBeingDestroyed || p.IsInteractedWith || p.IsDocked) continue;
                    if (p.Entity.Prefab == null || p.Entity.Prefab.Hash != hash) continue;
                    return p;
                }
            }
            return null;
        }

        static NetworkEntity? Spawn(uint hash, SkillShrineTestingArea area, Vector3 at, Quaternion rot) {
            if (hash == 0u || PrefabManager.GetPrefab(hash) == null) return null;
#if QUEST
            return SpawnHelper.Spawn(hash, SpawnData.Default, area.Entity != null ? area.Entity.Chunk : null!, at, rot, Il.Keep(DelegateSupport.ConvertDelegate<SpawnHelper.SpawningCallback>(new Action<NetworkEntity>(e => { if (e != null && e.Prefab != null) e.Prefab.SaveMode = SaveMode.Ignore; }))));
#else
            return SpawnHelper.Spawn(hash, SpawnData.Default, area.Entity != null ? area.Entity.Chunk : null!, at, rot, e => { if (e != null && e.Prefab != null) e.Prefab.SaveMode = SaveMode.Ignore; });
#endif
        }

        static readonly int T_Track = Timing.Section("event AutoDock.Track (try-out quiver undock)");
        static void Track(TryOut t, Pickup? p) {
            using var _t = Timing.Begin(T_Track);
            if (p == null || p.Entity == null || p.GetComponent<Arrow>() == null) return;
            NetworkEntity e = p.Entity;
            if (!t.Arrows.Contains(e)) {
                t.Arrows.Add(e);
#if QUEST
                e.add_DestroyedByScene(UntrackGame);
#else
                e.DestroyedByScene += Untrack;
#endif
            }
            t.Ids[e.Identifier] = e;
            tracked[e.Identifier] = e;
            if (p.Entity.Prefab != null) p.Entity.Prefab.SaveMode = SaveMode.Ignore; // the game set it back to normal saving on the undock
        }

        /// <summary>A tracked arrow was destroyed (stacked back into a quiver, shot and gone, ...): its id is free for the game to reuse.</summary>
        static readonly int T_Untrack = Timing.Section("event AutoDock.Untrack (entity destroyed)");
#if QUEST
        static readonly EntityDespawnedHandler UntrackGame = Il.Keep(DelegateSupport.ConvertDelegate<EntityDespawnedHandler>(new Action<NetworkEntity, bool>(Untrack)));
        static void Untrack(NetworkEntity entity, bool isUnload) {
#else
        static void Untrack(NetworkEntity entity, bool isUnload) {
#endif
            using var _t = Timing.Begin(T_Untrack);
            try { Forget(entity != null ? entity.Identifier : 0u, entity); }
            catch { }
        }

        static void Forget(uint id, NetworkEntity? entity) {
#if QUEST
            if (tracked.TryGetValue(id, out NetworkEntity e) && Il.Same(e, entity)) tracked.Remove(id);
#else
            if (tracked.TryGetValue(id, out NetworkEntity e) && ReferenceEquals(e, entity)) tracked.Remove(id);
#endif
        }

        static string OwnerOf(SkillShrineTestingArea area) {
            try {
                SkillShrine shrine = Traverse.Create(area).Field("shrine").GetValue<SkillShrine>();
                PlayerProgressionManager pm = shrine != null ? shrine.CurrentManager : null!;
                return pm != null && pm.Player != null ? Builds.NameOf(pm.Player) : "";
            }
            catch { return ""; }
        }

#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(SkillShrineTestingArea), nameof(SkillShrineTestingArea.Open), new[] { typeof(SkillOrb) })]
        static class TryOutOpened {
            static readonly int T = Timing.Section("patch SkillShrineTestingArea.Open / AutoDock.TryOutOpened");
            static void Postfix(SkillShrineTestingArea __instance, SkillOrb orb) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null || orb == null || orb.Slot == null || !On || !tryOutQuiver.Value) return;
                    ProgressionSlot? slot = AutoDockSlot;
                    if (slot == null || orb.Slot.Hash != slot.Hash) return;
                    foreach (TryOut open in tryOuts) if (open.Area == __instance && (open.NockTraining || (open.Quiver != null && !open.Quiver.IsBeingDestroyed))) return; // reopened: it is still there
                    Transform folder = __instance.SpawnFolder != null ? __instance.SpawnFolder : __instance.transform;
                    Vector3 off = Offset();
                    Vector3 at = folder.position + folder.right * off.x + Vector3.up * off.y + folder.forward * off.z;
                    NetworkEntity? quiver = Spawn((uint)Mathf.Max(0, tryOutQuiverHash.Value), __instance, at, folder.rotation);
                    PickupDock dock = quiver != null ? quiver.GetComponentInChildren<PickupDock>(true) : null!;
                    if (quiver == null || dock == null) {
                        MelonLogger.Warning($"[Archery] Auto Dock: try-out quiver (prefab {tryOutQuiverHash.Value}) {(quiver == null ? "could not be spawned" : "has no dock")}; the try-out needs a worn quiver");
                        if (quiver != null) quiver.SceneDestroy();
                        return;
                    }
                    var t = new TryOut { Area = __instance, Quiver = quiver, Dock = dock, Owner = OwnerOf(__instance) };
                    int want = Mathf.Clamp(tryOutArrows.Value, 1, 50), ok = 0;
                    uint used = (uint)Mathf.Max(0, tryOutArrowHash.Value);
                    string why = "";
                    for (int i = 0; i < want; i++) {
                        NetworkEntity? a = Spawn(used, __instance, at + Vector3.up * 0.3f, folder.rotation);
                        Pickup p = a != null ? a.GetComponent<Pickup>() : null!;
                        PickupDock.AllowedResult r = p != null ? p.DockInto(null, dock, 1, true) : PickupDock.AllowedResult.Ok;
                        if (a != null && p != null && r == PickupDock.AllowedResult.Ok) { if (ok == 0) Track(t, p); ok++; continue; } // later ones stack into the first
                        if (a != null && !a.IsBeingDestroyed) a.SceneDestroy();
                        why = a == null ? $"prefab {used} could not be spawned" : $"the quiver refused prefab {used} ({r})";
                        break;
                    }
                    if (ok == 0) {
                        // the ruling: no plain arrows; Auto Dock nocks the try-out's own training arrow instead
                        dock.DestroyAmountToEmpty();
                        quiver.SceneDestroy();
                        t.Quiver = null; t.Dock = null; t.NockTraining = true;
                        tryOuts.Add(t);
                        MelonLogger.Warning($"[Archery] Auto Dock: try-out quiver removed ({why}); Auto Dock nocks the try-out's own training arrow on the string after each shot instead");
                        return;
                    }
                    t.OnUndock = (interactor, pickup, from) => { try { Track(t, pickup); } catch { } };
                    dock.ItemUndocked += t.OnUndock;
                    tryOuts.Add(t);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Auto Dock: try-out quiver '{quiver.name}' at {Targets.P(at)} with {dock.QuantityStored} '{(PrefabManager.GetPrefab(used) != null ? PrefabManager.GetPrefab(used).name : used.ToString())}' arrows for '{t.Owner}'{(why.Length > 0 ? " (" + why + ")" : "")}; every arrow taken out is tracked and goes when the try-out ends");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: try-out quiver failed: {e.Message}"); }
            }
        }
#endif

        // Every way a try-out ends (another orb opened, the shrine closed, the player walked off) goes through the private Clear.
#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(SkillShrineTestingArea), "Clear")]
        static class TryOutCleared {
            static readonly int T = Timing.Section("patch SkillShrineTestingArea.Clear / AutoDock.TryOutCleared");
            static void Prefix(SkillShrineTestingArea __instance) {
                using var _t = Timing.Begin(T);
                if (!NetworkSceneManager.IsServer || tryOuts.Count == 0) return;
                for (int i = tryOuts.Count - 1; i >= 0; i--) {
                    TryOut t = tryOuts[i];
                    if (t.Area != __instance && t.Area != null) continue;
                    tryOuts.RemoveAt(i);
                    End(t, "the try-out ended");
                }
            }
        }
#endif

        /// <summary>Server: one try-out's quiver and every arrow that came out of it go, wherever they are.</summary>
        static void End(TryOut t, string why) {
            int gone = 0;
            try {
                if (t.Dock != null && t.OnUndock != null) t.Dock.ItemUndocked -= t.OnUndock;
                if (t.Dock != null && t.Quiver != null && !t.Quiver.IsBeingDestroyed) t.Dock.DestroyAmountToEmpty(); // the game's own: undock, then destroy
                if (t.Quiver != null && !t.Quiver.IsBeingDestroyed) t.Quiver.SceneDestroy();
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: removing the try-out quiver failed: {e.Message}"); }
            foreach (NetworkEntity a in t.Arrows) {
                try {
                    if (a == null || a.IsBeingDestroyed) continue;
#if QUEST
                    a.remove_DestroyedByScene(UntrackGame);
#else
                    a.DestroyedByScene -= Untrack;
#endif
                    a.SceneDestroy();
                    gone++;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: removing a try-out arrow failed: {e.Message}"); }
            }
            // fixF I-1: every id this try-out tracked goes, whether its arrow is still alive or was destroyed long ago (but never an
            // entry another try-out has since tracked for a different entity under a reused id)
            foreach (KeyValuePair<uint, NetworkEntity> kv in t.Ids) Forget(kv.Key, kv.Value);
            t.Ids.Clear();
            t.Arrows.Clear();
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Auto Dock: {why}; {(t.NockTraining ? "no quiver was used" : "its quiver was emptied and removed")}, {gone} try-out arrow(s) removed wherever they were");
        }

        /// <summary>Server, from Poll: a try-out whose player is gone ends; its quiver and arrows are kept out of the world save (a
        /// grab and let-go makes the game save an object normally again).</summary>
        static readonly int T_TendTryOuts = Timing.Section("AutoDock.TendTryOuts");
        static void TendTryOuts() {
            using var _t = Timing.Begin(T_TendTryOuts);
            if (tryOuts.Count == 0 || Time.time < nextTend) return;
            nextTend = Time.time + 1f;
            for (int i = tryOuts.Count - 1; i >= 0; i--) {
                TryOut t = tryOuts[i];
                try {
                    if (t.Area == null || (t.Owner.Length > 0 && Player.GetPlayer(t.Owner) == null)) {
                        tryOuts.RemoveAt(i);
                        End(t, t.Area == null ? "the try-out area is gone" : $"'{t.Owner}' left");
                        continue;
                    }
                    if (t.Quiver != null && !t.Quiver.IsBeingDestroyed && t.Quiver.Prefab != null) t.Quiver.Prefab.SaveMode = SaveMode.Ignore;
                    foreach (NetworkEntity a in t.Arrows) if (a != null && !a.IsBeingDestroyed && a.Prefab != null) a.Prefab.SaveMode = SaveMode.Ignore;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: tending the try-out failed: {e.Message}"); }
            }
        }

        /// <summary>Server, on quit: every try-out's quiver and arrows go.</summary>
        static readonly int T_Shutdown = Timing.Section("AutoDock.Shutdown");
        internal static void Shutdown() {
            using var _t = Timing.Begin(T_Shutdown);
            if (!NetworkSceneManager.IsServer) return;
            for (int i = tryOuts.Count - 1; i >= 0; i--) { TryOut t = tryOuts[i]; tryOuts.RemoveAt(i); End(t, "server shutdown"); }
        }

        // While the try-out lasts, an arrow out of its quiver docks only onto a bow string or back into that quiver: never into the
        // player's own quiver or bag, where it would stack into an arrow the game saves (fixA-review I-2).
#if !QUEST   // a server's work: not patched on Quest
        [HarmonyPatch(typeof(PickupDock), nameof(PickupDock.CheckAllowed), new[] { typeof(Interactor), typeof(Pickup), typeof(int), typeof(PickupDock) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
#endif
        static class KeepTryOutArrows {
            static readonly int T = Timing.Section("patch PickupDock.CheckAllowed / AutoDock.KeepTryOutArrows");
            static bool logged;
            static void Postfix(PickupDock __instance, Pickup pickup, ref PickupDock.AllowedResult __result) {
                using var _t = Timing.Begin(T);
                if (tracked.Count == 0 || __result != PickupDock.AllowedResult.Ok || !NetworkSceneManager.IsServer) return;
                try {
                    if (pickup == null || pickup.Entity == null || __instance == null) return;
#if QUEST
                    if (!tracked.TryGetValue(pickup.Entity.Identifier, out NetworkEntity e) || !Il.Same(e, pickup.Entity)) return; // fixF I-1: the same entity, not a reused id
#else
                    if (!tracked.TryGetValue(pickup.Entity.Identifier, out NetworkEntity e) || !ReferenceEquals(e, pickup.Entity)) return; // fixF I-1: the same entity, not a reused id
#endif
                    foreach (TryOut t in tryOuts) if (t.Dock == __instance) return;   // back into its try-out quiver
                    if (__instance.GetComponent<Quiver>() == null && Volley.BowForDock(__instance) != null) return; // onto a bow string
                    __result = PickupDock.AllowedResult.CustomFail;
                    if (!logged) { logged = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] Auto Dock: a try-out arrow cannot be put into '{__instance.name}' (only onto a bow string or back into the try-out quiver; said once)"); }
                }
                catch { }
            }
        }
        // ---------------------------------------------------------------- server: self-nocked arrows that were never shot (b2-review I-1)

        sealed class Unshot { public Pickup Arrow = null!; public PickupDock Quiver = null!; public PlayerController? Owner; public string Name = "?"; public float LooseSince = float.NaN; }
        static readonly Dictionary<uint, Unshot> unshot = new Dictionary<uint, Unshot>();
        static readonly List<uint> unshotScratch = new List<uint>();

        static readonly int T_PollUnshot = Timing.Section("AutoDock.PollUnshot");
        static void PollUnshot() {
            using var _t = Timing.Begin(T_PollUnshot);
            if (unshot.Count == 0) return;
            unshotScratch.Clear();
            unshotScratch.AddRange(unshot.Keys);
            foreach (uint id in unshotScratch) {
                if (!unshot.TryGetValue(id, out Unshot u)) continue;
                try {
                    Pickup a = u.Arrow;
                    bool exists = a != null && a.Entity != null && !a.Entity.IsBeingDestroyed;
                    bool docked = exists && a!.IsDocked;
                    bool onString = docked && Volley.BowForDock(a!.Dock) != null;
                    bool hand = exists && a!.IsInteractedWith;
                    bool quiverExists = u.Quiver != null;
                    bool room = quiverExists && u.Quiver!.CanDockAmount(1);
                    // b3-review M-1: only into a quiver its archer still carries, and only once the arrow has been loose a moment
                    bool carried = quiverExists && u.Owner != null && QuiverGather.Carried(u.Quiver!.GetComponentInParent<Pickup>(), u.Owner);
                    bool loose = exists && !onString && !hand && !docked;
                    float now = Time.time;
                    if (!loose) u.LooseSince = float.NaN;
                    else if (float.IsNaN(u.LooseSince)) u.LooseSince = now;
                    float looseFor = float.IsNaN(u.LooseSince) ? 0f : now - u.LooseSince;
                    UnshotStep step = AutoDockRule.Unshot(exists, onString, hand, docked, quiverExists, room, carried, looseFor, PutBackSeconds);
                    if (step == UnshotStep.Wait) continue;
                    unshot.Remove(id);
                    if (step == UnshotStep.Forget) {
                        if (loose) { if (Logs.Summary) MelonLogger.Msg($"[Archery] Auto Dock: '{u.Name}' self-nocked arrow {id} came off the string unshot; its quiver is {(!quiverExists ? "gone" : !room ? "full" : "no longer carried by the archer")}, so it stays where it is"); }
                        continue;
                    }
                    Embeddable embed = a!.GetComponent<Embeddable>();
                    if (embed != null && embed.IsEmbedded) embed.UnEmbed();
                    Arrow arrow = a.GetComponent<Arrow>();
                    if (arrow != null) {
                        arrow.StopUpdatingArrowFlight();
                        if (arrow.Rigidbody != null) { arrow.Rigidbody.velocity = Vector3.zero; arrow.Rigidbody.angularVelocity = Vector3.zero; }
                    }
                    PickupDock.AllowedResult result = a.DockInto(null, u.Quiver, 1, true); // as Quiver Gather's return
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Auto Dock: '{u.Name}' self-nocked arrow {id} came off the string unshot (the bow was put away?): back into '{u.Quiver!.name}': {result}");
                }
                catch (Exception ex) { unshot.Remove(id); MelonLogger.Warning($"[Archery] Auto Dock: unshot arrow {id} dropped after an error: {ex.Message}"); }
            }
        }

        // ---------------------------------------------------------------- clients: the look

        static readonly Dictionary<uint, float> quiverUndocks = new Dictionary<uint, float>(); // arrow id -> when it left a quiver with no hand
        static bool slideLogged, failedLogged;

        [HarmonyPatch(typeof(Pickup), "OnUndocked")]
        static class LeftQuiver {
            static readonly int T = Timing.Section("patch Pickup.OnUndocked / AutoDock.LeftQuiver");
            static void Postfix(Interactor interactor, Pickup pickup, PickupDock dock) {
                using var _t = Timing.Begin(T);
                try {
                    if (NetworkSceneManager.IsServer || interactor != null || pickup == null || dock == null || pickup.Entity == null) return;
                    if (dock.GetComponent<Quiver>() == null || pickup.GetComponent<Arrow>() == null) return;
                    if (quiverUndocks.Count >= Cap) quiverUndocks.Clear();
                    quiverUndocks[pickup.Entity.Identifier] = Time.time;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: undock note failed: {e.Message}"); }
            }
        }

        // Just before the server's dock puts the arrow on the string, while it is still where the quiver left it.
        [HarmonyPatch(typeof(PickupDocker), "OnPreDocked")]
        static class Arriving {
            static readonly int T = Timing.Section("patch PickupDocker.OnPreDocked / AutoDock.Arriving");
#if QUEST
            static void Prefix(PickupDocker __instance, Interactor interactor, PickupDock dock) {
                Pickup ___pickup = __instance.pickup;
                using var _t = Timing.Begin(T);
                try {
                    if (NetworkSceneManager.IsServer || interactor != null || ___pickup == null || dock == null || ___pickup.Entity == null) return;
#else
            static void Prefix(Pickup ___pickup, Interactor interactor, PickupDock dock) {
                using var _t = Timing.Begin(T);
                try {
                    if (NetworkSceneManager.IsServer || interactor != null || ___pickup == null || dock == null || ___pickup.Entity == null) return;
#endif
                    uint id = ___pickup.Entity.Identifier;
                    if (___pickup.GetComponent<Arrow>() == null) return;
                    if (dock.GetComponent<Quiver>() != null) return; // cheap first: a quiver is never a bow string (b3-review I-1)
                    BowString? bow = Volley.BowForDock(dock);
                    if (bow == null) return;
                    // b2-review M-4: any hand-less dock onto a held bow's string takes the holder as the arrow's last interactor, so
                    // the game ignores the archer's own body and bow at the shot, whether or not the look below runs.
                    Interactor? holder = null;
                    if (bow.Bow != null && bow.Bow.IsPickup && bow.Bow.Pickup != null)
                        foreach (Interactor i in bow.Bow.Pickup.Interactors) if (i != null && i.Controller != null && i.Controller.PlayerController != null) { holder = i; break; }
                    if (holder != null) ___pickup.LastPlayerInteractor = holder.Controller.PlayerController;
                    if (!quiverUndocks.TryGetValue(id, out float at) || Time.time - at > RecentUndockSeconds) return;
                    quiverUndocks.Remove(id);
                    bool local = holder != null && holder.IsLocal;
                    string look = Look(___pickup, dock);
                    if (local && looks.Value && haptic.Value > 0f) holder!.Controller.TriggerHaptic(Mathf.Clamp01(haptic.Value), HapticSeconds);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Auto Dock: arrow {id} nocks itself on '{bow.name}' ({(local ? "own" : "seen")}): {look}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Auto Dock: dock look failed: {e.Message}"); }
            }
        }

        sealed class Ghost {
            public MovingObjectsBetweenDocks Pooled = null!;
            public Vector3 FromPos;
            public Quaternion FromRot;
            public Transform To = null!;
            public float Start, Seconds;
            public readonly List<Renderer> Hidden = new List<Renderer>();
        }

        static readonly List<Ghost> ghosts = new List<Ghost>();

        static string Look(Pickup pickup, PickupDock dock) {
            if (ApplicationManager.IsHeadless || !looks.Value) return "no look";
            PickupDockSettings s = dock.Settings;
            if (s == null || !s.IsSnapping) {
                if (!slideLogged) { slideLogged = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Auto Dock: the string dock does not snap; the game slides the arrow there itself"); }
                return "the game slides it";
            }
            try {
                float secs = Mathf.Max(0f, ghostSeconds.Value);
                if (secs <= 0f || ghosts.Count >= Cap) return "no ghost";
#if QUEST
                var g = GlobalSettings<PickupTypeSettings>.Instance.PickupMovingGhost.Get(pickup.transform).As<MovingObjectsBetweenDocks>();
#else
                var g = GlobalSettings<PickupTypeSettings>.Instance.PickupMovingGhost.Get(pickup.transform) as MovingObjectsBetweenDocks;
#endif
                if (g == null) return "no ghost (the game's ghost is missing)";
                MeshRenderer[] renderers = pickup.GetComponentsInChildren<MeshRenderer>(false);
                g.SetupRenderers(renderers);
                var ghost = new Ghost { Pooled = g, FromPos = g.transform.position, FromRot = g.transform.rotation, To = dock.SnapPoint != null ? dock.SnapPoint : dock.transform,
                                        Start = Time.time, Seconds = secs };
                foreach (MeshRenderer r in renderers) if (r != null && r.enabled) { r.enabled = false; ghost.Hidden.Add(r); } // the real one docks at once
                ghosts.Add(ghost);
                return "a ghost travels from the quiver";
            }
            catch (Exception e) {
                if (!failedLogged) { failedLogged = true; MelonLogger.Warning($"[Archery] Auto Dock: the ghost could not be shown ({e.Message}); arrows nock without it"); }
                return "no ghost (failed)";
            }
        }

        static void Finish(Ghost g) {
            foreach (Renderer r in g.Hidden) if (r != null) r.enabled = true;
            g.Hidden.Clear();
            MovingObjectsBetweenDocks p = g.Pooled;
            if (p != null && !p.IsReleased) { p.ReleaseActive(); p.Release(); }
        }

        /// <summary>Clients, every frame (Main.OnLateUpdate): each ghost moves on toward the string; at the end it goes back to
        /// its pool and the real arrow shows again.</summary>
        internal static void Frame() {
            if (ghosts.Count == 0) return;
            float now = Time.time;
            for (int i = ghosts.Count - 1; i >= 0; i--) {
                Ghost g = ghosts[i];
                try {
                    float t = g.Seconds > 0f ? (now - g.Start) / g.Seconds : 1f;
                    if (t >= 1f || g.To == null || g.Pooled == null || g.Pooled.IsReleased) { ghosts.RemoveAt(i); Finish(g); continue; }
                    float k = t * t * (3f - 2f * t);
                    g.Pooled.transform.SetPositionAndRotation(Vector3.Lerp(g.FromPos, g.To.position, k), Quaternion.Slerp(g.FromRot, g.To.rotation, k));
                }
                catch (Exception e) {
                    ghosts.RemoveAt(i);
                    try { Finish(g); } catch { }
                    if (!failedLogged) { failedLogged = true; MelonLogger.Warning($"[Archery] Auto Dock: the ghost failed ({e.Message})"); }
                }
            }
        }
    }
}
