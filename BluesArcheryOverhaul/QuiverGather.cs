using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Global;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Quiver Gather: arrows a player drew from their quiver and shot come back to that quiver. Since H3 section 6 it is part of the one
    // gatherer orb (the game's Arrow Gatherer): Build.QuiverGather is set by owning that orb.
    // All decisions are made on the server (QuiverRules.Decide); the client only draws the return.
    internal static class QuiverGather {
        class Tracked {
            public Pickup pickup = null!;
            public PickupDock quiver = null!;
            public PlayerController owner = null!;
            public bool shot;
            public float due;
            public long seq;      // 0.19.11: the order it was tracked in (Trim forgets the oldest, unshot ones first)
            public int refusals;  // 0.19.11: the quiver refused the dock this many times
            public OutOfReach reach;   // 0.19.13: nobody can get it back, so it returns at once (QuiverRules.Reach)
            public bool clipChecked;   // 0.19.13: stuck in the world and already looked at (it does not move while stuck)
        }

        static long nextSeq;
        const int RefusalsMax = 20;
        // 0.19.11 (the owner: "make sure the arrow quiver return is more consistent"): what became of the arrows due back, one Summary
        // line a minute when any did not come back, so a play-test's log says why
        static int returnedCount;
        static readonly Dictionary<string, int> lostWhy = new Dictionary<string, int>();
        static float reportAt;

        static MelonPreferences_Entry<bool> enabled = null!;
        static MelonPreferences_Entry<float> returnSeconds = null!, retrySeconds = null!, ghostMinDistance = null!;
        static MelonPreferences_Entry<int> maxTracked = null!;

        static readonly Dictionary<uint, Tracked> tracked = new Dictionary<uint, Tracked>();
        static readonly List<uint> scratch = new List<uint>(), dead = new List<uint>();
        static readonly HashSet<uint> shotOnThisClient = new HashSet<uint>();
        static float nextPoll;

        internal static void Init() {
            var cat = Cfg.Category("QuiverGather");
            enabled = Cfg.Shared(cat.CreateEntry("Enabled", true));
            returnSeconds = Cfg.Shared(cat.CreateEntry("ReturnSeconds", 15f, description: "Seconds after the shot before an arrow returns to its quiver."));
            retrySeconds = Cfg.Shared(cat.CreateEntry("RetrySeconds", 1f, description: "How often a waiting arrow (held, or quiver taken off) is checked again."));
            maxTracked = Cfg.Shared(cat.CreateEntry("MaxTracked", 40, description: "Most arrows remembered per player; the oldest is forgotten first."));
            ghostMinDistance = cat.CreateEntry("GhostMinDistance", 1.5f, description: "Returns from closer than this show no flying ghost.");
        }

        /// <summary>Client: remember that this arrow was shot here, so only real returns get the flying ghost
        /// (not every arrow that docks into a quiver when a world loads).</summary>
        internal static void NoteShot(uint arrowId) {
            if (shotOnThisClient.Count > 256) shotOnThisClient.Clear();
            shotOnThisClient.Add(arrowId);
        }

        static bool IsBowStringDock(PickupDock dock) => Volley.BowForDock(dock) != null;

        // ---------------------------------------------------------------- server: tracking

        /// <summary>Only for the progression log line: Quiver Gather runs whether or not RevampedSkills is loaded (the owner, 2026-11-20).</summary>
        internal static bool RevampedSkillsLoaded {
            get {
                try {
                    foreach (MelonBase m in MelonBase.RegisteredMelons)
                        if (m is MelonMod && m.Info != null && m.Info.Name == "Revamped Skills") return true;
                    return false;
                }
                catch { return false; }
            }
        }

#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(Pickup), "OnUndocked")]
        static class TakenFromDock {
            static readonly int T = Timing.Section("patch Pickup.OnUndocked / QuiverGather.TakenFromDock");
            static void Postfix(Interactor interactor, Pickup pickup, PickupDock dock) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || pickup == null || dock == null || interactor == null) return;
                    if (dock.GetComponent<Quiver>() == null || pickup.GetComponent<Arrow>() == null) return;
                    PlayerController owner = interactor.Controller != null ? interactor.Controller.PlayerController : null!;
                    AutoDock.NoteQuiver(owner, dock); // the quiver this player last drew from by hand (whether or not Quiver Gather is on)
                    if (!enabled.Value) return;
                    if (owner == null || !Builds.ServerBuild(owner.NetworkPlayer).QuiverGather) return;

                    uint id = pickup.Entity.Identifier;
                    tracked[id] = new Tracked { pickup = pickup, quiver = dock, owner = owner, seq = ++nextSeq };
                    Trim(owner);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Quiver: tracking arrow {id} for {Name(owner)}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Quiver: tracking failed: {e.Message}"); }
            }
        }
#endif

        static void Trim(PlayerController owner) {
            int mine = 0;
            foreach (Tracked t in tracked.Values) if (t.owner == owner) mine++;
            if (mine <= maxTracked.Value) return;
            // 0.19.11: the oldest arrow drawn and never shot goes first, else the oldest shot one (dictionary order is not age: a freed
            // slot is reused, so a fresh shot could be the one forgotten)
            uint victim = 0u; bool victimShot = true; long victimSeq = long.MaxValue;
            foreach (KeyValuePair<uint, Tracked> p in tracked) {
                if (p.Value.owner != owner) continue;
                bool better = (victimShot && !p.Value.shot) || (victimShot == p.Value.shot && p.Value.seq < victimSeq);
                if (better) { victim = p.Key; victimShot = p.Value.shot; victimSeq = p.Value.seq; }
            }
            tracked.Remove(victim);
            if (victimShot) Lost("more than MaxTracked arrows out");
        }

        static string Name(PlayerController pc) {
            try { return pc.NetworkPlayer != null && pc.NetworkPlayer.UserInfo != null ? pc.NetworkPlayer.UserInfo.Username : "?"; }
            catch { return "?"; }
        }

#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(Pickup), "OnDocked")]
        static class PutIntoDock {
            static readonly int T = Timing.Section("patch Pickup.OnDocked / QuiverGather.PutIntoDock");
            static void Postfix(Pickup pickup, PickupDock dock) {
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || pickup == null || dock == null || pickup.Entity == null) return;
                    uint id = pickup.Entity.Identifier;
#if QUEST
                    if (!tracked.TryGetValue(id, out Tracked t) || !Il.Same(t.pickup, pickup)) return; // G2: ids are reused; the entry must be this arrow's
#else
                    if (!tracked.TryGetValue(id, out Tracked t) || !ReferenceEquals(t.pickup, pickup)) return; // G2: ids are reused; the entry must be this arrow's
#endif
                    if (dock == t.quiver) { tracked.Remove(id); return; }           // back home by hand
                    if (IsBowStringDock(dock)) { t.shot = false; return; }          // nocked again: countdown restarts on the next shot
                    tracked.Remove(id);                                              // stored somewhere else: an ordinary arrow now
                    if (t.shot) Lost("put away before its return");
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Quiver: arrow {id} -> GiveUp (stored elsewhere)");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Quiver: dock check failed: {e.Message}"); }
            }
        }
#endif

#if !QUEST // server-only: every patch method returns at once on a client
        [HarmonyPatch(typeof(BowString), "OnShoot")]
        static class Shot {
            static readonly int T = Timing.Section("patch BowString.OnShoot / QuiverGather.Shot");
            static void Postfix(Pickup arg2) {
                using var _t = Timing.Begin(T);
                if (!NetworkSceneManager.IsServer || arg2 == null || arg2.Entity == null) return;
#if QUEST
                if (!tracked.TryGetValue(arg2.Entity.Identifier, out Tracked t) || !Il.Same(t.pickup, arg2)) return; // G2: by reference
#else
                if (!tracked.TryGetValue(arg2.Entity.Identifier, out Tracked t) || !ReferenceEquals(t.pickup, arg2)) return; // G2: by reference
#endif
                try { if (Skyfall.IsCast(arg2.Entity.Identifier)) { tracked.Remove(arg2.Entity.Identifier); return; } } catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: quiver check failed: {e.Message}"); } // Skyfall: the arrow is used up, never returned
                t.shot = true;
                t.due = Time.time + returnSeconds.Value;
            }
        }
#endif

        // ---------------------------------------------------------------- server: the return

        /// <summary>Called from the mod's update. Does nothing most frames.</summary>
        internal static void Poll() {
            if (tracked.Count == 0 || Time.time < nextPoll || !NetworkSceneManager.IsServer) return;
            nextPoll = Time.time + 0.25f;
            scratch.Clear(); dead.Clear();
            foreach (KeyValuePair<uint, Tracked> p in tracked) {
                if (p.Value.shot) {
                    if (Time.time >= p.Value.due) scratch.Add(p.Key);
                    else if (p.Value.reach == OutOfReach.None && (p.Value.reach = Reach(p.Value)) != OutOfReach.None) { p.Value.due = Time.time; scratch.Add(p.Key); }
                }
                // G2 (leak): drawn by hand and never shot, and the arrow or its archer is gone: nothing can ever act on it again (a
                // shot needs the arrow; the id may be reused by another arrow, which no longer matches by reference)
                else if (p.Value.pickup == null || p.Value.owner == null) dead.Add(p.Key);
            }
            foreach (uint id in dead) tracked.Remove(id);
            if (dead.Count > 0 && Logs.Debug) MelonLogger.Msg($"[Archery] Quiver: forgot {dead.Count} unshot arrow(s) whose arrow or archer is gone");
            foreach (uint id in scratch) Tick(id);
            if (lostWhy.Count > 0 && Time.time >= reportAt) {
                reportAt = Time.time + 60f;
                int n = lostWhy.Values.Sum();
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Quiver: {returnedCount} arrow(s) returned, {n} did not ({string.Join(", ", lostWhy.Select(kv => kv.Value + " " + kv.Key).ToArray())}) since the last such line");
                lostWhy.Clear(); returnedCount = 0;
            }
        }

        static void Lost(string why) { lostWhy.TryGetValue(why, out int n); lostWhy[why] = n + 1; }

        // ---- 0.19.13: arrows nobody can get back come back at once (QuiverRules.Reach)

        static readonly int T_Reach = Timing.Section("scene QuiverGather.Reach (ground or wall cast)");
        static readonly List<RaycastHit> reachHits = new List<RaycastHit>();
        static bool clippedSaid, voidSaid;
        const float BackMetres = 0.75f;

        /// <summary>Server, each poll of a shot arrow still flying or lying out its time: stuck in the world with its middle inside
        /// the surface it went into (looked at once per time it sticks), or falling fast through the void.</summary>
        static OutOfReach Reach(Tracked t) {
            Pickup p = t.pickup;
            if (p == null || p.Entity == null || p.Entity.IsBeingDestroyed || p.IsDocked || p.IsInteractedWith) return OutOfReach.None;
            Embeddable embed = p.GetComponent<Embeddable>();
            if (embed != null && embed.IsEmbedded) {
                if (t.clipChecked) return OutOfReach.None;
                t.clipChecked = true;
                if (!InWorld(embed)) return OutOfReach.None;   // in a creature or a player: it comes back at its time
                using var _t = Timing.Begin(T_Reach);
                return QuiverRules.Reach(true, WallBeforeMiddle(p), 0f, true, true, 0f);
            }
            t.clipChecked = false;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            float vy = rb != null && !rb.isKinematic ? rb.velocity.y : 0f;
            if (!(vy < -QuiverRules.VoidFallSpeed)) return OutOfReach.None;   // most polls end here: no cast
            using var _t2 = Timing.Begin(T_Reach);
            Vector3 at = p.transform.position;
            bool ground = Skyfall.GroundAt(at.x, at.z, Mathf.Max(1200f, at.y + 10f), at.y - 1500f, out float gy);
            return QuiverRules.Reach(false, false, vy, Hits.Loaded(at), ground, ground ? gy - at.y : float.NaN);
        }

        /// <summary>Stuck in the world itself or in something that is not a creature or a player (a building).</summary>
        static bool InWorld(Embeddable embed) {
            NetworkEntity? e = embed.EmbeddedEntity;
            if (e == null) return true;
            NetworkEntity root = e.PrefabRoot != null ? e.PrefabRoot : e;
            HealthObject h = root.GetComponentInChildren<HealthObject>();
            return h == null || !(Targets.IsCreature(h) || Targets.IsPlayer(h));
        }

        /// <summary>A cast back along the arrow's own way (it flew in along its forward, so the way behind it was open) from
        /// BackMetres behind its middle meets the world before the middle: half the arrow or more is inside what it hit.</summary>
        static bool WallBeforeMiddle(Pickup p) {
            Vector3 mid = Targets.BoundsOf(p.transform, out Bounds b) ? b.center : p.transform.position;
            Vector3 fwd = p.transform.forward;
            Hits.CastAll(mid - fwd * BackMetres, fwd, BackMetres - 0.01f, reachHits, p.transform);
            bool wall = false;
            foreach (RaycastHit h in reachHits) {
                Collider c = h.collider;
                if (c == null || c.GetComponentInParent<Arrow>() != null) continue;               // another arrow
                Rigidbody rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;                                       // a loose thing in the way
                ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
                HealthObject? hh = det != null ? det.HealthTarget : null;
                if (hh != null && (Targets.IsCreature(hh) || Targets.IsPlayer(hh))) continue;      // someone standing there
                wall = true; break;
            }
            reachHits.Clear();
            return wall;
        }

        static readonly int T_Tick = Timing.Section("QuiverGather.Tick");
        static void Tick(uint id) {
            using var _t = Timing.Begin(T_Tick, id);
            if (!tracked.TryGetValue(id, out Tracked t)) return;
            try {
                Pickup p = t.pickup;
                bool exists = p != null && p.Entity != null && !p.Entity.IsBeingDestroyed && t.owner != null && t.quiver != null;
                bool loaded = false, held = false, storedElsewhere = false, carried = false, room = false;
                if (exists) {
                    if (p!.IsDocked && p.Dock == t.quiver) { tracked.Remove(id); return; }
                    // 0.19.13: an arrow falling through the void has left every chunk; it still comes home
                    loaded = t.reach != OutOfReach.None || (p.Entity!.Chunk != null && p.Entity.Chunk.IsLoaded);
                    bool onBow = p.IsDocked && IsBowStringDock(p.Dock);
                    held = p.IsInteractedWith || onBow;
                    storedElsewhere = p.IsDocked && !onBow;
                    carried = Carried(t.quiver!.GetComponentInParent<Pickup>(), t.owner!);
                    room = t.quiver!.CanDockAmount(1);
                }
                ReturnAction action = QuiverRules.Decide(exists, loaded, held, storedElsewhere, carried, room);
                string why = !exists ? "arrow gone" : !loaded ? "left behind" : storedElsewhere ? "stored elsewhere" : held ? "being held" : !carried ? "quiver not worn" : !room ? "quiver full" : "ok";

                if (action == ReturnAction.Wait) { t.due = Time.time + retrySeconds.Value; return; }
                if (action == ReturnAction.Return) {
                    Arrow arrow = p!.GetComponent<Arrow>();
                    Embeddable embed = p.GetComponent<Embeddable>();
                    if (embed != null && embed.IsEmbedded) embed.UnEmbed();
                    if (arrow != null) {
                        arrow.StopUpdatingArrowFlight();
                        if (arrow.Rigidbody != null) { arrow.Rigidbody.velocity = Vector3.zero; arrow.Rigidbody.angularVelocity = Vector3.zero; }
                    }
                    PickupDock.AllowedResult result = p.DockInto(null, t.quiver, 1, true);
                    if (result != PickupDock.AllowedResult.Ok) {
                        // 0.19.11: a refusal can pass (the quiver being moved); try again each RetrySeconds, RefusalsMax times
                        if (++t.refusals < RefusalsMax) { t.due = Time.time + retrySeconds.Value; return; }
                        action = ReturnAction.GiveUp; why = "quiver refused: " + result;
                    }
                }
                tracked.Remove(id);
                if (action == ReturnAction.Return) returnedCount++; else Lost(why);
                if (action == ReturnAction.Return && t.reach != OutOfReach.None) {
                    string how = t.reach == OutOfReach.Void ? "fell out of the world" : "clipped into a wall";
                    ref bool said = ref (t.reach == OutOfReach.Void ? ref voidSaid : ref clippedSaid);
                    if (!said && Logs.Summary) { said = true; MelonLogger.Msg($"[Archery] Quiver: an arrow that {how} came straight back to its quiver (said once a session; each one is a Debug line)"); }
                    else if (Logs.Debug) MelonLogger.Msg($"[Archery] Quiver: arrow {id} {how}: straight back");
                }
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Quiver: arrow {id} -> {action} ({why})");
            }
            catch (Exception e) {
                tracked.Remove(id);
                Lost("an error");
                MelonLogger.Warning($"[Archery] Quiver: arrow {id} dropped after an error: {e.Message}");
            }
        }

        /// <summary>Server, Auto Dock: an arrow taken from <paramref name="quiver"/> with no hand (interactor null, which the undock
        /// patch above skips) is tracked like a hand-drawn one, so it returns. False when Quiver Gather does not apply.</summary>
        internal static bool TrackAutoDocked(Pickup pickup, PickupDock quiver, PlayerController owner) {
            if (!NetworkSceneManager.IsServer || !enabled.Value || pickup == null || pickup.Entity == null || quiver == null || owner == null) return false;
            if (!Builds.ServerBuild(owner.NetworkPlayer).QuiverGather) return false;

            uint id = pickup.Entity.Identifier;
            tracked[id] = new Tracked { pickup = pickup, quiver = quiver, owner = owner, seq = ++nextSeq };
            Trim(owner);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Quiver: tracking auto-docked arrow {id} for {Name(owner)}");
            return true;
        }

        /// <summary>Server, Auto Dock: the dock was refused, so the arrow stayed in its quiver: forget it.</summary>
        internal static void Untrack(uint arrowId) => tracked.Remove(arrowId);

        // Is this item on the owner's body or in their hands, directly or inside something they carry?
        internal static bool Carried(Pickup? item, PlayerController owner) {
            for (int depth = 0; item != null && depth < 4; depth++) {
                foreach (Interactor i in item.Interactors)
                    if (i != null && i.Controller != null && i.Controller.PlayerController == owner) return true;
                if (!item.IsDocked) return false;
                PickupDock dock = item.Dock;
                if (dock.transform.IsChildOf(owner.transform)) return true;
                item = dock.GetComponentInParent<Pickup>();
            }
            return false;
        }

        // ---------------------------------------------------------------- client: the look of the return

        // Runs just before a pickup is put into a dock, while it is still where it was lying.
        // Reuses the game's own "item flies into a pouch" ghost, exactly as the pouch gatherer does.
        [HarmonyPatch(typeof(PickupDocker), "OnPreDocked")]
        static class ReturnGhost {
            static readonly int T = Timing.Section("patch PickupDocker.OnPreDocked / QuiverGather.ReturnGhost");
#if QUEST
            static void Prefix(PickupDocker __instance, Interactor interactor, PickupDock dock) {
                Pickup ___pickup = __instance.pickup; QuestWatch.Once("PickupDocker.OnPreDocked (QuiverGather)");
#else
            static void Prefix(Pickup ___pickup, Interactor interactor, PickupDock dock) {
#endif
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless || interactor != null || ___pickup == null || dock == null || ___pickup.Entity == null) return;
                    if (!shotOnThisClient.Remove(___pickup.Entity.Identifier)) return;
                    if (dock.GetComponent<Quiver>() == null) return;
                    if ((___pickup.transform.position - dock.transform.position).magnitude < ghostMinDistance.Value) return;

#if QUEST
                    var ghost = GlobalSettings<PickupTypeSettings>.Instance.PickupMovingGhost.Get(___pickup.transform).As<MovingObjectsBetweenDocks>();
#else
                    var ghost = GlobalSettings<PickupTypeSettings>.Instance.PickupMovingGhost.Get(___pickup.transform) as MovingObjectsBetweenDocks;
#endif
                    if (ghost == null) return;
                    ghost.SetupRenderers(___pickup.GetComponentsInChildren<MeshRenderer>(true));
                    ghost.transform.parent = dock.SnapPoint;
                    ghost.ReceiveDirections(dock, ghost.transform, ___pickup.Item);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Quiver: return ghost for arrow {___pickup.Entity.Identifier}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Quiver: return ghost failed: {e.Message}"); }
            }
        }
    }
}
