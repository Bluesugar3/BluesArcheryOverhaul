using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Batch H1: Seeking Volley, the homing volley (the multi-arrow line's legendary). The rules are Unity-free in Homing.cs; this is
    // where they meet the game. No new message and no networked object: the lock rides the shot claim (Claims.cs, Wire.cs).
    //   - Shooter's client: while the local player holds a volley past the 68% crossing with the orb working (owned, multi-arrow
    //     main), a narrow sweep along the arrow's line (three sphere casts, SearchHz times a second) finds the creature under the aim;
    //     LockSet runs the dwell and loss rules every held frame from cached positions; at release the locks within the release cone
    //     lead the claim's target list (ClaimLocks). One lock per draw: the set is cleared by the shot and by a new draw.
    //   - Server: checks each claimed lock with its slack (Check, LockCheck), relays what it kept, and its homing arrows are the only
    //     ones that deal damage.
    //   - Every machine (Volley.Launch): a release whose claim is steered opens a Flight here: the locks resolved on this machine,
    //     each lock's middle refreshed once per physics tick for all of that release's arrows; the real arrow does not fly (HoldReal)
    //     and is placed where the core cluster lands (CoreEnded), which also stands in once for the real arrow's first creature hit.
    // Looks (H2): SeekLook.cs is the seam (the lock's cage SeekCage.cs, the bow's green and lean, the bigger arrows and their trails,
    // the arrival SeekFx.cs); the shape maths is CageShape / HomingLean / TrailCurve in Spectral.cs.
    internal static class Seek {
        static MelonPreferences_Entry<float> lockMin = null!, lockRange = null!, dwellNear = null!, dwellNearMetres = null!, dwellFar = null!, dwellFarMetres = null!,
            dwellFaster = null!, lockCone = null!, lockConeMetres = null!, still = null!, drain = null!, takeover = null!, searchHz = null!, breakOff = null!,
            breakOffSeconds = null!, sightLost = null!, breakMetres = null!, releaseCone = null!, serverTolerance = null!, serverRange = null!, serverSlack = null!,
            bloom = null!, inner = null!, outer = null!, pellet = null!, ringGap = null!, turn = null!, letGoDrift = null!, maxLife = null!, coreShare = null!,
            bloomMax = null!, sideMax = null!, letGoSeconds = null!, placeMax = null!, leadMax = null!, chainMaxLife = null!;
        static MelonPreferences_Entry<int> maxLocks = null!, coreArrows = null!;
        static MelonPreferences_Entry<string> chainLocks = null!;
        static MelonPreferences_Entry<bool> lockPlayers = null!;
        static string chainWarned = "";
        static HomingValues? cache;

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        internal static void Init() {
            HomingValues d = HomingValues.Default;
            var cat = Cfg.Category("SeekingVolley");
            MelonPreferences_Entry<float> F(string key, float value, string text, bool shared) {
                var e = cat.CreateEntry(key, value, description: text);
                return shared ? Cfg.Shared(e) : e;
            }
            lockMin = F("LockMinMetres", d.LockMinMetres, "The lock: a creature can be locked from this many metres away...", true);
            lockRange = F("LockRangeMetres", d.LockRangeMetres, "... out to this many metres (the server allows ServerRangeMetres more). With a lock, Burst's pellets fly this far (plus that slack) instead of Burst's Range.", true);
            dwellNear = F("DwellNearSeconds", d.DwellNearSeconds, "Seconds the aim must rest on a creature at DwellNearMetres or nearer to lock it (rising in a straight line to DwellFarSeconds at DwellFarMetres). The server checks the shot's time against the same dwell.", true);
            dwellNearMetres = F("DwellNearMetres", d.DwellNearMetres, "See DwellNearSeconds.", true);
            dwellFar = F("DwellFarSeconds", d.DwellFarSeconds, "Seconds to lock a creature at DwellFarMetres or farther.", true);
            dwellFarMetres = F("DwellFarMetres", d.DwellFarMetres, "See DwellFarSeconds.", true);
            dwellFaster = F("DwellFasterPerOrb", d.DwellFasterPerOrb, "Each Quickdraw orb owned (at most three) makes every dwell this much shorter (0.15 = 15%).", true);
            maxLocks = Cfg.Shared(cat.CreateEntry("MaxLocks", d.MaxLocks, description: "How many creatures one draw can lock (1 to 4, the shot message's room). The first is the main lock: the core arrows go to it and the real arrow ends in it; every other arrow is dealt round the locks in turn."));
            lockCone = F("LockConeDegrees", d.LockConeDegrees, "Shooter: a creature is under the aim when its middle is within this many degrees of the arrow's line, plus the angle LockConeMetres makes at its distance.", false);
            lockConeMetres = F("LockConeMetres", d.LockConeMetres, "Shooter: see LockConeDegrees (a near creature is wide in the view, a far one is not).", false);
            still = F("StillDegreesPerSecond", d.StillDegreesPerSecond, "Shooter: the lock fills only while the bow turns slower than this (swinging across a pack never locks anything).", false);
            drain = F("DrainFactor", d.DrainFactor, "Shooter: looking away empties a lock that is filling this many times as fast as it filled.", false);
            takeover = F("TakeoverSeconds", d.TakeoverSeconds, "Shooter: another creature takes the filling over once it has been under the aim this long.", false);
            searchHz = F("SearchHz", d.SearchHz, "Shooter: how many times a second the narrow sweep along the arrow's line looks for the creature under the aim (and checks sight to every lock). Between sweeps only angles are worked out.", false);
            breakOff = F("BreakOffDegrees", d.BreakOffDegrees, "Shooter: every lock is lost when the aim stays more than this many degrees off all of them for BreakOffSeconds (not while it rests on another creature).", false);
            breakOffSeconds = F("BreakOffSeconds", d.BreakOffSeconds, "Shooter: see BreakOffDegrees.", false);
            sightLost = F("SightLostSeconds", d.SightLostSeconds, "Shooter: a lock is lost when no sight ray reaches it for this long (it does not see through walls).", false);
            breakMetres = F("BreakMetres", d.BreakMetres, "Shooter: a lock is lost when its creature is farther than this.", false);
            releaseCone = F("ReleaseConeDegrees", d.ReleaseConeDegrees, "The release: the volley homes on the locks within this many degrees of where the bow points (the server allows ServerToleranceDegrees more); with none, it is an ordinary volley.", true);
            serverTolerance = F("ServerToleranceDegrees", d.ServerToleranceDegrees, "Server: extra degrees on ReleaseConeDegrees when it checks a claimed lock (it sees creatures 100 to 200 ms late).", false);
            // H3 (H1-review M-7): shared, every machine reads it for the pellets' range with a lock (HomingAssign.PelletRange)
            serverRange = F("ServerRangeMetres", d.ServerRangeMetres, "Extra metres on LockRangeMetres when the server checks a claimed lock; with a lock, Burst's pellets fly LockRangeMetres plus this on every machine.", true);
            serverSlack = F("ServerDwellSlackSeconds", d.ServerDwellSlackSeconds, "Server: each claimed lock may have had this much less than its dwell (the server's clock lags the shooter's).", false);
            bloom = F("BloomShare", d.BloomShare, "Flight: each homing arrow flies its normal spread for this share of the way to its creature, then turns toward it.", true);
            inner = F("InnerDegrees", d.InnerDegrees, "Flight: the inner ghosts come in this many degrees off the line from the bow, from their own side (the last few metres straighten out).", true);
            outer = F("OuterDegrees", d.OuterDegrees, "Flight: the outer ghosts' approach angle.", true);
            pellet = F("PelletDegrees", d.PelletDegrees, "Flight: Burst's pellets' approach angle.", true);
            ringGap = F("RingGapSeconds", d.RingGapSeconds, "Flight: the inner ring (and the core) lands first, the outer ring and the pellets this many seconds apart behind it.", true);
            turn = F("TurnDegreesPerSecond", d.TurnDegreesPerSecond, "Flight: the most a homing arrow turns, in degrees a second.", true);
            letGoDrift = F("LetGoDriftMetres", d.LetGoDriftMetres, "Flight: an arrow lets go (flies straight on) when its creature has moved this far from where it was at the release; also when it gets behind the arrow, or when it dies and no other lock of the release lives ahead of the arrow (then it turns to that one).", true);
            maxLife = F("MaxLifeSeconds", d.MaxLifeSeconds, "Flight: a homing arrow lives the longer of [Volley] LifeSeconds and its arrival plus 0.3 s, never longer than this.", true);
            // H3
            bloomMax = F("BloomMaxMetres", d.BloomMaxMetres, "Flight: the straight first part (BloomShare of the way) is never longer than this, so a release a little low at long range turns before it reaches the ground.", true);
            sideMax = F("SideMaxMetres", d.SideMaxMetres, "Flight: how far off the line from the bow an arrow's curve may swing (its shaping point at most this far to the side or above).", true);
            letGoSeconds = F("LetGoSeconds", d.LetGoSeconds, "Flight: an arrow that let go of its creature flies straight on for this long and then ends.", true);
            placeMax = F("PlaceMaxMetres", d.PlaceMaxMetres, "Server: the real arrow is put where the core arrows ended only within this many metres of the archer (and in loaded world); further, it is put along the way at that reach, as a Quickdraw 4 line that hit nothing.", false);
            leadMax = F("LeadMaxSeconds", d.LeadMaxSeconds, "Flight: a moving creature is led by its velocity over the time the arrow still needs, at most this many seconds.", true);
            coreArrows = Cfg.Shared(cat.CreateEntry("CoreArrows", d.CoreArrows, description: "On a locked release the real arrow does not fly (the owner: \"only the multi-shot arrows\"): this many spectral core arrows fly in a tight cluster on the arrow's own line to the main lock instead, and the real arrow is put in the creature where they land. 1 to 12."));
            coreShare = F("CoreShare", d.CoreShare, "Each core arrow's share of the real arrow's damage at the release's multiplier (5 x 0.2 = the real arrow's own).", true);
            // H5 (the owner: "Have homing bounce each ghost arrow to each target that is locked onto")
            chainLocks = Cfg.Shared(cat.CreateEntry("ChainLocks", HomingChain.Name(d.ChainLocks), description: "Which homing arrows chain through the locks: a chaining arrow that lands on one of the release's locks while it still homes deals its hit and flies on to the nearest lock it has not struck yet, until it has struck every living lock or its life is over. ghosts = the inner and outer ghosts; all = Burst's pellets too; off = as before (every arrow ends on its first creature). The core arrows never chain."));
            chainMaxLife = F("ChainMaxLifeSeconds", d.ChainMaxLifeSeconds, "Flight: a chaining arrow's whole flight, every leg included, never lasts longer than this (0.5 to 10 s). Each leg gets the time to reach its lock plus 0.3 s.", true);
            // 0.19.11 (the owner: "homing doesn't work on players")
            lockPlayers = Cfg.Shared(cat.CreateEntry("LockPlayers", d.LockPlayers, description: "The lock also takes other players (never yourself). The server keeps a player's lock only while its PvP is on and [PvP] ModHitsHurtPlayers is on; otherwise that release is an ordinary volley. A server with PvP off can switch this off so no player is locked at all."));
            SeekLook.Init(cat);
            Cfg.Reloaded += () => cache = null;
        }

        /// <summary>The live [SeekingVolley] numbers, built once per settings load (a leaf: it reads only its own entries; Paths.OrbNumbers
        /// reads it, never the other way round).</summary>
        internal static HomingValues Values {
            get {
                if (lockMin == null) return HomingValues.Default;
                if (cache != null) return cache;
                var v = new HomingValues {
                    LockMinMetres = Mathf.Max(0f, lockMin.Value), LockRangeMetres = Mathf.Max(0f, lockRange.Value),
                    DwellNearSeconds = Mathf.Max(0f, dwellNear.Value), DwellNearMetres = dwellNearMetres.Value, DwellFarSeconds = Mathf.Max(0f, dwellFar.Value),
                    DwellFarMetres = dwellFarMetres.Value, DwellFasterPerOrb = dwellFaster.Value, MaxLocks = Mathf.Clamp(maxLocks.Value, 1, ShotClaimWire.MaxLocks),
                    LockConeDegrees = Mathf.Max(0f, lockCone.Value), LockConeMetres = Mathf.Max(0f, lockConeMetres.Value), StillDegreesPerSecond = still.Value,
                    DrainFactor = Mathf.Max(0f, drain.Value), TakeoverSeconds = Mathf.Max(0f, takeover.Value), SearchHz = Mathf.Clamp(searchHz.Value, 0.5f, 30f),
                    BreakOffDegrees = breakOff.Value, BreakOffSeconds = Mathf.Max(0f, breakOffSeconds.Value), SightLostSeconds = Mathf.Max(0f, sightLost.Value),
                    BreakMetres = breakMetres.Value, ReleaseConeDegrees = Mathf.Clamp(releaseCone.Value, 0f, 90f), ServerToleranceDegrees = Mathf.Max(0f, serverTolerance.Value),
                    ServerRangeMetres = Mathf.Max(0f, serverRange.Value), ServerDwellSlackSeconds = Mathf.Max(0f, serverSlack.Value), BloomShare = Mathf.Clamp01(bloom.Value),
                    InnerDegrees = inner.Value, OuterDegrees = outer.Value, PelletDegrees = pellet.Value, RingGapSeconds = Mathf.Max(0f, ringGap.Value),
                    TurnDegreesPerSecond = Mathf.Max(1f, turn.Value), LetGoDriftMetres = Mathf.Max(0f, letGoDrift.Value), MaxLifeSeconds = Mathf.Clamp(maxLife.Value, 0.5f, 10f),
                    CoreArrows = HomingValues.CoreCount(coreArrows.Value), // H4 (Opus minor 2): 1 to 12
                    CoreShare = Mathf.Max(0f, coreShare.Value),
                    BloomMaxMetres = Mathf.Max(0f, bloomMax.Value), SideMaxMetres = Mathf.Max(0f, sideMax.Value), LetGoSeconds = Mathf.Clamp(letGoSeconds.Value, 0f, 10f),
                    PlaceMaxMetres = Mathf.Max(1f, placeMax.Value), LeadMaxSeconds = Mathf.Clamp(leadMax.Value, 0f, 3f),
                    // H5: at most 10 s, so every per-release record the chain's hits use (Targets 15 s, the homing record 12 s, the
                    // landing looks 10 s) outlives the flight
                    ChainLocks = HomingChain.Parse(chainLocks.Value, out bool known), ChainMaxLifeSeconds = Mathf.Clamp(chainMaxLife.Value, 0.5f, 10f),
                    LockPlayers = lockPlayers.Value,
                };
                if (!known && chainWarned != chainLocks.Value) {
                    chainWarned = chainLocks.Value;
                    MelonLogger.Warning($"[Archery] Seek: [SeekingVolley] ChainLocks '{chainLocks.Value}' is not off, ghosts or all; ghosts is used");
                }
                return cache = v;
            }
        }

        /// <summary>The orb is in the shrine ([Tree] SeekingVolleyOrb) and works for this build (owned, multi-arrow main).</summary>
        internal static bool Applies(Build b) => Tree.OrbOn(Orb.SeekingVolley) && LockRule.Applies(b);

        static Vector3 V(HVec h) => new Vector3((float)h.X, (float)h.Y, (float)h.Z);
        internal static HVec H(Vector3 v) => new HVec(v.x, v.y, v.z);

        // ================================================================ the shooter's lock

        sealed class Tracked {
            public HealthObject? Health; public Transform? Root; public Vector3 Centre; public float Radius = 0.5f, Distance; public bool Visible = true;
            public Vector3 Extents = new Vector3(0.5f, 0.5f, 0.5f);   // H2: its bounds' half-sizes, for the cage's size (SeekLook)
            public Vector3 Offset;                                    // H2: its middle in its root's own space when it was measured (the cage follows the root)
        }

        sealed class Aim {
            public readonly LockSet Set = new LockSet();
            public BowString? Bow;
            public uint Arrow;
            public float LastUpdate = -1f, NextSearch;
            public Vector3 LastForward;
            public uint Candidate;
            public readonly Tracked Cand = new Tracked();
            public readonly Dictionary<uint, Tracked> Locked = new Dictionary<uint, Tracked>();
            // H3 (H1-review I-2): LastUpdate too, or the first frame of the next draw counted every second since this one
            public void Clear() { Set.Reset(); Candidate = 0u; Locked.Clear(); Arrow = 0u; LastForward = Vector3.zero; NextSearch = 0f; LastUpdate = -1f; }
        }

        // ponytail: one aim, one bow (the local player draws one string at a time), as the Quickdraw ring
        static readonly Aim aim = new Aim();
        static readonly List<LockView> views = new List<LockView>();
        static RaycastHit[] sweep = new RaycastHit[64];
        static bool sweepWarned;
        static readonly Dictionary<uint, Tracked> seen = new Dictionary<uint, Tracked>();
        static int searches;
        static double searchMs, searchMaxMs;
        static bool costLogged;

        [HarmonyPatch(typeof(BowString), "HeldUpdate")]
        static class Held {
            static readonly int T = Timing.Section("patch BowString.HeldUpdate / Seek.Held (the shooter's lock)");
#if QUEST
            static void Postfix(BowString __instance) {
                Interactor ___interactorHoldingBowString = __instance.interactorHoldingBowString;
                using var _t = Timing.Begin(T);
                if (NetworkSceneManager.IsServer || ApplicationManager.IsHeadless) return;
#else
            static void Postfix(BowString __instance, Interactor ___interactorHoldingBowString) {
                using var _t = Timing.Begin(T);
                if (NetworkSceneManager.IsServer || ApplicationManager.IsHeadless) return;
#endif
                if (___interactorHoldingBowString == null || !___interactorHoldingBowString.IsLocal) return;
                try { Frame(__instance); }
                catch (Exception e) {
                    aim.Clear();
                    // H3 (H1-review M-5): said once with the whole exception, then counted (one line per 200 more)
                    frameFailures++;
                    if (frameFailures == 1) MelonLogger.Warning($"[Archery] Seek: the lock failed this frame; it starts again (said once, then counted): {e}");
                    else if (frameFailures % 200 == 0) MelonLogger.Warning($"[Archery] Seek: the lock has failed {frameFailures} times this session (last: {e.Message})");
                }
            }
        }

        static int frameFailures;

        static readonly int T_Frame = Timing.Section("Seek.Frame (the shooter's lock, per held frame)"), T_Search = Timing.Section("Seek.LockSearch (shooter's sweep)");

        static void Frame(BowString bow) {
            using var _t = Timing.Begin(T_Frame);
            if (!Tree.OrbOn(Orb.SeekingVolley)) return;
            Build b = Builds.LocalBuild();
            // H3 (H1-review M-10): a build the orb does not work for stops here, before the dock and arrow look-ups
            if (!LockRule.Applies(b) && aim.Set.Locks.Count == 0 && aim.Set.Filling == 0u && aim.Arrow == 0u) return;
            PickupDock dock = bow.StringDock;
            Pickup? docked = dock != null && dock.HasDockedPickup ? dock.DockedPickup : null;
            Arrow? arrow = docked != null ? docked.GetComponent<Arrow>() : null;
            uint crossed = Draws.ArrowAtCrossing(bow);
            float? t = Draws.Elapsed(bow);
            float now = Time.time;
            if (!LockRule.Applies(b) || arrow == null || docked!.Entity == null || !t.HasValue || crossed == 0u || crossed != docked.Entity.Identifier) {
                if (aim.Set.Locks.Count > 0 || aim.Set.Filling != 0u) {
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek: the draw ended without a shot; {aim.Set.Locks.Count} lock(s) dropped");
                    foreach (KeyValuePair<uint, Tracked> kv in aim.Locked) { SeekLook.Lost(kv.Value.Centre); break; } // one sound for them all
                    SeekLook.DrawEnded(); // H2: every cage of this draw springs open now (those of a release in flight stay)
                }
                aim.Clear();
                return;
            }
#if QUEST
            if (!Il.Same(aim.Bow, bow) || aim.Arrow != crossed) { aim.Clear(); aim.Bow = bow; aim.Arrow = crossed; }
#else
            if (!ReferenceEquals(aim.Bow, bow) || aim.Arrow != crossed) { aim.Clear(); aim.Bow = bow; aim.Arrow = crossed; }
#endif
            HomingValues v = Values;
            Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
            Vector3 origin = tipT.position, fwd = docked.transform.forward;
#if QUEST
            Vector3 eye = Targets.HeadOf(Player.Current?.TryCast<Player>()) ?? origin;
#else
            Vector3 eye = Targets.HeadOf(Player.Current as Player) ?? origin;
#endif
            float dt = aim.LastUpdate >= 0f ? Mathf.Max(0f, now - aim.LastUpdate) : 0f;
            bool isStill = aim.LastForward == Vector3.zero || dt <= 0f || Vector3.Angle(aim.LastForward, fwd) / dt < v.StillDegreesPerSecond;
            aim.LastForward = fwd;
            aim.LastUpdate = now;

            if (now >= aim.NextSearch) {
                aim.NextSearch = now + 1f / Mathf.Max(0.5f, v.SearchHz);
                Search(origin, fwd, eye, bow, arrow, v);
            }
            // per frame: only angles from the cached middles
            uint candidate = 0u;
            if (aim.Candidate != 0u && aim.Cand.Health != null && Vector3.Angle(fwd, aim.Cand.Centre - origin) <= LockRule.AimCone(aim.Cand.Distance, v)) candidate = aim.Candidate;
            views.Clear();
            foreach (uint id in aim.Set.Locks) {
                if (!aim.Locked.TryGetValue(id, out Tracked k) || k.Health == null) continue;
                views.Add(new LockView(id, Vector3.Angle(fwd, k.Centre - origin), k.Visible, k.Distance, k.Health.Health > 0f));
            }
            aim.Set.Update(now, dt, candidate, aim.Cand.Distance, isStill, views, b.Quick, v);
            foreach (KeyValuePair<uint, LockLoss> lost in aim.Set.LostNow) {
                if (aim.Locked.TryGetValue(lost.Key, out Tracked k)) { SeekLook.Lost(k.Centre); aim.Locked.Remove(lost.Key); }
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek: lock on {Targets.NameOf(lost.Key)} lost ({lost.Value})");
            }
            if (aim.Set.GainedNow != 0u) {
                uint id = aim.Set.GainedNow;
                aim.Locked[id] = new Tracked { Health = aim.Cand.Health, Root = aim.Cand.Root, Centre = aim.Cand.Centre, Radius = aim.Cand.Radius, Distance = aim.Cand.Distance, Visible = true, Extents = aim.Cand.Extents, Offset = aim.Cand.Offset };
                SeekLook.Locked(aim.Cand.Centre, Volley.BowHand(bow), aim.Set.Locks.Count);
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Seek: locked {Targets.NameOf(id)} at {aim.Cand.Distance:0.0} m ({aim.Set.Locks.Count} of {v.MaxLocks}), {t.Value * 1000f:0} ms after the crossing"));
            }
            // the looks (H2): a cage round each lock in lock order (the first is the main lock), a closing one round the creature
            // being locked; each with its root, so the look follows the creature every frame between the sweeps
            marks.Clear();
            foreach (uint id in aim.Set.Locks)
                if (aim.Locked.TryGetValue(id, out Tracked k)) marks.Add(new SeekLook.Mark { Id = id, Root = k.Root, Centre = k.Centre, Extents = k.Extents, Offset = k.Offset });
            bool filling = aim.Set.Filling != 0u && aim.Set.Filling == aim.Candidate && aim.Cand.Health != null;
            SeekLook.Mark fill = filling ? new SeekLook.Mark { Id = aim.Candidate, Root = aim.Cand.Root, Centre = aim.Cand.Centre, Extents = aim.Cand.Extents, Offset = aim.Cand.Offset } : default;
            SeekLook.Frame(marks, filling, fill, aim.Set.Fill);
            BowNow(bow, origin, fwd, v);
        }

        static readonly List<SeekLook.Mark> marks = new List<SeekLook.Mark>();

        // H2, looks only ("the bow tells you too"): the locks a release right now would home on, in claim order, with where each
        // is: the ghosts on the bow turn green and lean toward them (Volley.DrawHeld). The rule is ClaimLocks' own
        // (LockClaim.ShooterKeep, with the same angles), asked again only when its answer may have changed (a lock gained or lost,
        // or one crossing the release cone), so nothing is allocated on an ordinary held frame.
        static readonly List<float> heldAngles = new List<float>();
        static readonly List<Vector3> heldCentres = new List<Vector3>();
        static List<uint> heldKept = new List<uint>();
        static BowString? heldBow;
        static int heldFrame = -1, heldMask = -1, heldCount = -1;
        static uint heldFirst;

        static readonly int T_BowNow = Timing.Section("Seek.BowNow (what a release now would home on, per held frame)");
        static void BowNow(BowString bow, Vector3 origin, Vector3 fwd, HomingValues v) {
            using var _t = Timing.Begin(T_BowNow);
            heldCentres.Clear();
            heldBow = bow;
            heldFrame = Time.frameCount;
            int n = aim.Set.Locks.Count;
            if (n == 0) { heldMask = -1; heldCount = 0; return; }
            heldAngles.Clear();
            int mask = 0;
            for (int i = 0; i < n; i++) {
                float a = aim.Locked.TryGetValue(aim.Set.Locks[i], out Tracked k) ? Vector3.Angle(fwd, k.Centre - origin) : float.NaN;
                heldAngles.Add(a);
                if (a <= v.ReleaseConeDegrees) mask |= 1 << i;
            }
            if (mask != heldMask || n != heldCount || aim.Set.Locks[0] != heldFirst || aim.Set.GainedNow != 0u || aim.Set.LostNow.Count > 0) {
                heldKept = LockClaim.ShooterKeep(aim.Set.Locks, heldAngles, v);
                heldMask = mask; heldCount = n; heldFirst = aim.Set.Locks[0];
            }
            foreach (uint id in heldKept) if (aim.Locked.TryGetValue(id, out Tracked k)) heldCentres.Add(k.Centre);
        }

        /// <summary>H2, the shooter's client, looks only: where the locks are that a release of <paramref name="bow"/> this frame
        /// would home on, in claim order (the first is the main lock); null with none (no lock, or every lock outside the release
        /// cone: the release would be an ordinary volley, and the bow shows that).</summary>
        internal static List<Vector3>? HeldLocks(BowString bow) =>
#if QUEST
            Il.Same(heldBow, bow) && Time.frameCount - heldFrame <= 1 && heldCentres.Count > 0 ? heldCentres : null;
#else
            ReferenceEquals(heldBow, bow) && Time.frameCount - heldFrame <= 1 && heldCentres.Count > 0 ? heldCentres : null;
#endif

        /// <summary>H2: this release homes on this machine (it has a homing record: Volley.Launch opened one). H4 (H23-review minor 4 =
        /// Opus minor 9): only while it can still be in flight (the arrow id comes back through Quiver Gather and Auto Dock, and its
        /// record is kept up to FlightForget).</summary>
        internal static bool Homes(uint release) => flights.TryGetValue(release, out Flight f) && Time.time - f.At <= Values.FlightSeconds + 1f; // H5: a chaining arrow flies up to ChainMaxLifeSeconds

        // The narrow sweep: three sphere casts along the arrow's line out to the lock range, each as wide as the aim's cone at its far
        // end (and started that far back, so the segments overlap), instead of the 60 m cone search of the target list (4.29 ms once in
        // play-test 8). Every creature it touches is scored by how far off the aim it is for its distance; the best one in range, in
        // the cone and in sight is the candidate. Then each lock's middle, distance and sight are refreshed.
        static void Search(Vector3 origin, Vector3 fwd, Vector3 eye, BowString bow, Arrow arrow, HomingValues v) {
            using var _t = Timing.Begin(T_Search);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Transform? self = PlayerController.Current != null ? PlayerController.Current.transform : null;
            PlayerController? me = PlayerController.Current;
            Transform? bowT = bow.Bow != null ? bow.Bow.transform : null;
            seen.Clear();
            scored.Clear(); // H4 (H23-review minor 3): a sweep that threw part-way never leaves ids behind for the next one
            uint best = 0u; float bestScore = float.MaxValue;
            float range = v.LockRangeMetres;
            const int Segments = 3;
            for (int s = 0; s < Segments; s++) {
                float a = range * s / Segments, z = range * (s + 1) / Segments;
                float r = Mathf.Tan(LockRule.AimCone(Mathf.Max(1f, z), v) * Mathf.Deg2Rad) * z;
                float start = Mathf.Max(0f, a - r);
                int n;
#if QUEST
                while ((n = Il.SphereCastNonAlloc(origin + fwd * start, r, fwd, sweep, z - start, ~(1 << 2), QueryTriggerInteraction.Ignore)) == sweep.Length && sweep.Length < 1024)
#else
                while ((n = Physics.SphereCastNonAlloc(origin + fwd * start, r, fwd, sweep, z - start, ~(1 << 2), QueryTriggerInteraction.Ignore)) == sweep.Length && sweep.Length < 1024)
#endif
                    sweep = new RaycastHit[sweep.Length * 2];
#if QUEST
                QuestWatch.Mark("sweep: cast done");
#endif
                if (n == sweep.Length && !sweepWarned) { sweepWarned = true; MelonLogger.Warning($"[Archery] Seek: the lock's sweep met more than {n} colliders; some may be missed (said once)"); }
                for (int i = 0; i < n; i++) {
                    Collider c = sweep[i].collider;
                    if (c == null) continue;
                    ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
                    HealthObject h = det != null ? det.HealthTarget : null!;
#if QUEST
                    QuestWatch.Mark("sweep: health");
#endif
                    if (h == null || !(h.Health > 0f)) continue;
#if QUEST
                    if (Targets.IsPlayer(h) ? !v.LockPlayers || (h.Is(out PlayerHealth own) && own.Player == me) : !Targets.IsCreature(h)) continue;   // 0.19.11: other players too
#else
                    if (Targets.IsPlayer(h) ? !v.LockPlayers || (h is PlayerHealth own && own.Player == me) : !Targets.IsCreature(h)) continue;   // 0.19.11: other players too
#endif
#if QUEST
                    QuestWatch.Mark("sweep: creature test passed");
#endif
                    uint id = Targets.IdOf(h);
                    if (id == 0u || seen.ContainsKey(id)) continue;
#if QUEST
                    QuestWatch.Mark("sweep: measure");
#endif
                    var k = Measure(h, eye);
                    seen[id] = k;
                    if (!LockRule.InRange(k.Distance, v)) continue;
#if QUEST
                    QuestWatch.Mark("sweep: in range");
#endif
                    float cone = LockRule.AimCone(k.Distance, v), off = Vector3.Angle(fwd, k.Centre - origin);
                    if (off > cone) continue;
                    float score = off / Mathf.Max(0.01f, cone);
                    scored.Add(new KeyValuePair<float, uint>(score, id));
                }
            }
            // H3 (H1-review M-2): the best-scoring creature in sight, trying at most three in score order (was: the best or none)
#if QUEST
            QuestWatch.Mark("sweep: sort");
#endif
            scored.Sort((x, y) => x.Key.CompareTo(y.Key));
            for (int i = 0; i < scored.Count && i < 3 && best == 0u; i++)
                if (Sight(eye, seen[scored[i].Value], self, bowT, arrow.transform)) { best = scored[i].Value; bestScore = scored[i].Key; }
            scored.Clear();
#if QUEST
            QuestWatch.Mark("sweep: sight done");
#endif
            aim.Candidate = best;
            if (best != 0u) { Tracked k = seen[best]; aim.Cand.Health = k.Health; aim.Cand.Root = k.Root; aim.Cand.Centre = k.Centre; aim.Cand.Radius = k.Radius; aim.Cand.Distance = k.Distance; aim.Cand.Extents = k.Extents; aim.Cand.Offset = k.Offset; }
            else aim.Cand.Health = null;
            foreach (KeyValuePair<uint, Tracked> kv in aim.Locked) {
                Tracked k = kv.Value;
                if (k.Health == null) continue;
                Tracked m = seen.TryGetValue(kv.Key, out Tracked fresh) ? fresh : Measure(k.Health, eye);
                k.Centre = m.Centre; k.Radius = m.Radius; k.Distance = m.Distance; k.Root = m.Root; k.Extents = m.Extents; k.Offset = m.Offset;
                k.Visible = Sight(eye, k, self, bowT, arrow.transform);
            }
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds;
            searches++; searchMs += ms; if (ms > searchMaxMs) searchMaxMs = ms;
            if (!costLogged && searches >= 50) {
                costLogged = true;
                if (Logs.Summary) MelonLogger.Msg(Inv($"[Archery] Seek: the lock's sweep costs {searchMs / searches:0.000} ms on average, {searchMaxMs:0.000} ms at most, over {searches} sweeps ({Values.SearchHz:0.#} a second while a volley is held; logged once a session)"));
            }
        }

        static Tracked Measure(HealthObject h, Vector3 eye) {
            Transform root = Targets.RootOf(h);
#if QUEST
            QuestWatch.Mark("measure: root");
#endif
            var k = new Tracked { Health = h, Root = root };
#if QUEST
            if (h.Is<PlayerHealth>()) { k.Centre = Targets.Centre(h, out _); k.Extents = new Vector3(0.3f, 0.6f, 0.3f); k.Radius = 0.6f; }   // 0.19.11: a player's chest (Targets.Centre)
#else
            if (h is PlayerHealth) { k.Centre = Targets.Centre(h, out _); k.Extents = new Vector3(0.3f, 0.6f, 0.3f); k.Radius = 0.6f; }   // 0.19.11: a player's chest (Targets.Centre)
#endif
            else if (Targets.BoundsOf(root, out Bounds bb)) { k.Centre = bb.center; k.Extents = bb.extents; k.Radius = Mathf.Clamp(Mathf.Max(bb.extents.x, Mathf.Max(bb.extents.y, bb.extents.z)), 0.3f, 3f); }
            else { k.Centre = root.position + Vector3.up * 0.5f; k.Radius = 0.5f; }
#if QUEST
            QuestWatch.Mark("measure: bounds");
#endif
            k.Offset = root.InverseTransformPoint(k.Centre);
            k.Distance = (k.Centre - eye).magnitude;
            return k;
        }

        /// <summary>A sight ray from the archer's head to the creature's middle reaches it (or nothing solid stands between).</summary>
        /// H3 (H1-review M-2): only the world blocks sight. Another creature (or a player) in front is looked through, so a pack does
        /// not make locks flicker and the creature behind the first of the test tool's row can still be locked.
        static bool Sight(Vector3 eye, Tracked k, Transform? self, Transform? bow, Transform? arrow) {
            Vector3 to = k.Centre - eye;
            float d = to.magnitude;
            if (d < 0.05f) return true;
#if QUEST
            QuestWatch.Mark("sight: cast");
#endif
            Hits.CastAll(eye, to, d, sightHits, self, bow, arrow);
            foreach (RaycastHit hit in sightHits) {
                if (hit.transform == null) continue;
                if (k.Root != null && hit.transform.IsChildOf(k.Root)) return true;
                ImpactDetector det = hit.collider != null ? hit.collider.GetComponentInParent<ImpactDetector>() : null!;
                HealthObject h = det != null ? det.HealthTarget : null!;
                if (h != null && (Targets.IsCreature(h) || Targets.IsPlayer(h))) continue;   // a body in the way: looked through
                return false;
            }
            return true;
        }

        static readonly List<RaycastHit> sightHits = new List<RaycastHit>();
        static readonly List<KeyValuePair<float, uint>> scored = new List<KeyValuePair<float, uint>>();

        /// <summary>Shooter's client, in the claim (Claims.Shot, before the shot is sent): the locks this release homes on, within the
        /// release cone, in lock order; empty when the orb does not work for the local build, the release is no volley, or there is no
        /// lock on this draw. One lock per draw: the set is cleared here whatever the outcome.</summary>
        internal static List<uint> ClaimLocks(BowString? bow, Pickup pickup, Resolution r) {
            var none = new List<uint>();
            List<uint>? claimed = null;
            try {
#if QUEST
                if (bow == null || !Il.Same(aim.Bow, bow) || aim.Set.Locks.Count == 0 || Time.time - aim.LastUpdate > 0.3f) return none;
#else
                if (bow == null || !ReferenceEquals(aim.Bow, bow) || aim.Set.Locks.Count == 0 || Time.time - aim.LastUpdate > 0.3f) return none;
#endif
                if (!Applies(Builds.LocalBuild()) || !LockRule.Steerable(r)) {
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek: {aim.Set.Locks.Count} lock(s) not used: {(LockRule.Steerable(r) ? "the orb does not work for this build" : "the release is no volley (" + r + ")")}");
                    return none;
                }
                Arrow arrow = pickup.GetComponent<Arrow>();
                Vector3 tip = arrow != null && arrow.ArrowTip != null ? arrow.ArrowTip.transform.position : pickup.transform.position;
                Vector3 fwd = pickup.transform.forward;
                var angles = new List<float>();
                foreach (uint id in aim.Set.Locks) angles.Add(aim.Locked.TryGetValue(id, out Tracked k) ? Vector3.Angle(fwd, k.Centre - tip) : float.NaN);
                List<uint> kept = LockClaim.ShooterKeep(aim.Set.Locks, angles, Values);
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Seek: release with {aim.Set.Locks.Count} lock(s) at [{string.Join(", ", angles.ConvertAll(a => a.ToString("0", CultureInfo.InvariantCulture) + " deg").ToArray())}] off the bow; claimed {kept.Count}"));
                claimed = kept;
                return kept;
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek: claiming the locks failed; an ordinary volley: {e.Message}"); return none; }
            finally {
                aim.Clear();
                // H2, looks: the cages of the locks this release homes on stay on their creatures until its arrows land there
                // (or their life is over); every other cage of this draw springs open
                SeekLook.Claimed(claimed, Values.FlightSeconds + 0.5f); // H5: a chain's later arrivals come up to ChainMaxLifeSeconds after
            }
        }

        // ================================================================ the server's check

        /// <summary>Server, Claims.Check: the relayed claim of a steered shot when the orb works for the shooter's server build (the
        /// kept locks first, then Ricochet's and Shatter's names for a build that wants them), else null: the caller's own check (the
        /// lock dropped). One Summary line per steered shot: how many locks were claimed and kept, and why any was refused.</summary>
        static readonly int T_Check = Timing.Section("Seek.Check (server)");
        internal static ShotClaim? Check(ShotClaim got, BowString? bow, uint id, Pickup? pickup, int? effectiveMs) {
            using var _t = Timing.Begin(T_Check, id);
            PlayerController? pc = bow != null && bow.Pickup != null ? bow.Pickup.LastPlayerInteractor : null;
            Player? shooter = pc != null ? pc.NetworkPlayer : null;
            Build build = Builds.ServerBuild(shooter);
            List<uint> claimed = LockClaim.Of(got);
            string name = Builds.NameOf(shooter);
            if (!Applies(build)) {
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek: arrow {id} '{name}' claimed {claimed.Count} lock(s), kept 0: Seeking Volley does not work for this build ({build})");
                return null;
            }
            Vector3? head = Targets.HeadOf(shooter);
            Arrow? arrow = pickup != null ? pickup.GetComponent<Arrow>() : null;
            if (!head.HasValue || pickup == null) {
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek: arrow {id} '{name}' claimed {claimed.Count} lock(s), kept 0: {(head.HasValue ? "no arrow" : "the archer is not found")}");
                return null;
            }
            Vector3 tip = arrow != null && arrow.ArrowTip != null ? arrow.ArrowTip.transform.position : pickup.transform.position;
            Vector3 fwd = pickup.transform.forward; // the release pose the shot message carried (Shooter.SyncShoot set it before Shoot)
            HomingValues v = Values;
            LockFacts Facts(uint t) {
                HealthObject? h = Targets.ResolveOwn(t);
#if QUEST
                if (h == null || (h.Is(out PlayerHealth own) && pc != null && own.Player == pc)) return default;   // never the archer
#else
                if (h == null || (h is PlayerHealth own && pc != null && own.Player == pc)) return default;   // never the archer
#endif
                Vector3 c = Targets.Centre(h, out _);
                return new LockFacts(true, Targets.IsCreature(h), Targets.IsPlayer(h), h.Health > 0f, (c - head.Value).magnitude, Vector3.Angle(fwd, c - tip));
            }
            var refused = new List<KeyValuePair<uint, LockRefusal>>();
            float? seconds = effectiveMs.HasValue ? effectiveMs.Value / 1000f : (float?)null;
            List<uint> kept = LockCheck.Keep(claimed, Facts, seconds, build.Quick, v, refused, v.LockPlayers && Pvp.ModHitsLand);
            ShotClaim c2 = LockClaim.Server(got, kept, Targets.Wanted(build), t => Targets.Facts(t, head.Value), Targets.Range);
            if (Logs.Summary) {
                var why = new List<string>();
                foreach (var kv in refused) why.Add($"{Targets.NameOf(kv.Key)} {LockCheck.Why(kv.Value)}");
                string t = seconds.HasValue ? Inv($"{seconds.Value * 1000f:0} ms") : "no time";
                MelonLogger.Msg($"[Archery] Seek: arrow {id} '{name}' claimed {claimed.Count} lock(s), kept {kept.Count} at {t} after the crossing" +
                                (why.Count > 0 ? "; refused: " + string.Join("; ", why.ToArray()) : "") + (kept.Count == 0 ? " (an ordinary volley)" : ""));
            }
            return c2;
        }

        // ================================================================ every machine: one homing release

        internal sealed class Flight {
            public uint Release;
            public float At;
            public bool Server, Local;
            public Player? Shooter;
            public uint[] Ids = new uint[0];
            public HealthObject?[] Healths = new HealthObject?[0];
            public Vector3[] Centres = new Vector3[0];
            public bool[] Alive = new bool[0];
            public int Tick = int.MinValue;
            public bool CoreEnded, LetLogged;
            public int Cores;
            public float CoreMultiplier = 1f;   // the core cluster's whole damage on the arrow's power (the real arrow's at the release's multiplier)
            public ImpactTool? Tool;
            public Arrow? Arrow;
            // H2, looks only (clients): the locks whose arrival burst has shown, and how many arrival sounds have played
            public bool[] Arrived = new bool[0];
            public int ArriveSounds;
            // H3: every lock as this machine sees it this tick (body middle, velocity, alive, the room round it measured at the
            // release), the body colliders each middle is taken from, when they were last refreshed
            public HomingTarget[] Targets = new HomingTarget[0];
            public List<Collider>?[] Body = new List<Collider>?[0];
            public float RefreshedAt;
            /// <summary>H3, clients: the server flew an ordinary volley for this release (it refused the lock): its homing arrows end here.</summary>
            public bool Refused;
            public int LetGoes;
            // H5, the chain: each lock's root (a strike is a landing under it; a chaining arrow's casts pass the roots it has struck),
            // and the release's chaining arrows: launched, still flying, their lock hits, how many struck every lock
            public Transform?[] Roots = new Transform?[0];
            public int ChainArrows, ChainLive, ChainHits, ChainAll;
            public bool ChainLogged;
        }

        const int FlightCap = 64;
        const float FlightForget = 12f;
        static readonly Dictionary<uint, Flight> flights = new Dictionary<uint, Flight>();
        static readonly List<uint> flightOrder = new List<uint>();

        /// <summary>Every machine, at Volley.Launch: the record of a homing release, its locks resolved here (an id this machine does not
        /// know stays unresolved: its arrows turn to another lock, or let go). Replaces an older record of the same arrow. H3: each lock's
        /// body colliders and the room round it (Room) are found here, once.</summary>
        static readonly int T_Open = Timing.Section("Seek.Open (a homing release)");
        internal static Flight Open(uint release, List<uint> locks, Player? shooter, bool local, Arrow arrow) {
            using var _t = Timing.Begin(T_Open, release);
            float now = Time.time;
            if (flights.ContainsKey(release)) flightOrder.Remove(release);
            while (flightOrder.Count > 0 && (flights.Count >= FlightCap || (flights.TryGetValue(flightOrder[0], out Flight old) && now - old.At > FlightForget))) {
                flights.Remove(flightOrder[0]); flightOrder.RemoveAt(0);
            }
            int n = locks.Count;
            var f = new Flight {
                Release = release, At = now, Server = NetworkSceneManager.IsServer, Local = local, Shooter = shooter, Arrow = arrow, Tool = arrow.ImpactTool,
                Ids = locks.ToArray(), Healths = new HealthObject?[n], Centres = new Vector3[n], Alive = new bool[n], Arrived = new bool[n],
                Targets = new HomingTarget[n], Body = new List<Collider>?[n], RefreshedAt = now, Roots = new Transform?[n],
            };
            Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
            Vector3 eye = Targets.HeadOf(shooter) ?? tipT.position;
            Transform? shooterRoot = shooter != null && shooter.PlayerController != null ? shooter.PlayerController.transform : null;
            for (int i = 0; i < n; i++) {
                HealthObject? h = Targets.ResolveOwn(locks[i]);
                f.Healths[i] = h;
                f.Roots[i] = h != null ? Targets.RootOf(h) : null;
                f.Alive[i] = h != null && h.Health > 0f;
                f.Body[i] = h != null ? BodyOf(h) : null;
                f.Centres[i] = h != null ? BodyCentre(h, f.Body[i]) : Vector3.zero;
                HomingRoom room = h != null ? Room(eye, f.Centres[i], Targets.RootOf(h), shooterRoot, arrow.transform) : HomingRoom.Open;
                f.Targets[i] = new HomingTarget { Pos = H(f.Centres[i]), Alive = f.Alive[i], Room = room };
            }
            flights[release] = f;
            flightOrder.Add(release);
            if (Logs.Debug && n > 0) {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) sb.Append(i > 0 ? "; " : "").Append(Targets.NameOf(locks[i])).Append(": ").Append(f.Targets[i].Room.ToString()).Append(f.Body[i] != null ? Inv($", {f.Body[i]!.Count} body collider(s)") : ", whole bounds");
                MelonLogger.Msg($"[Archery] Seek: arrow {release} room round each lock: {sb}");
            }
            return f;
        }

        internal static Flight? Of(uint release) => flights.TryGetValue(release, out Flight f) ? f : null;

        // ---- H3: the creature's body (H1-review M-3: the core landed on a Turabada's 'Block Players' shell; arrows aimed at the middle
        // of every collider passed a hopping Gotera): the colliders the mod's hits can damage, those under a detector whose target is
        // this creature, never a trigger, never on Ignore Raycast, BlockTeleport ('Block Players') or AI ('Target Collector')

        static int blockTeleportLayer = -2, aiLayer = -2;
        static readonly List<Collider> bodyScratch = new List<Collider>();

        internal static bool ShellLayer(int layer) {   // also SkyfallServer: a shell is not a creature's body
            if (blockTeleportLayer == -2) { blockTeleportLayer = LayerMask.NameToLayer("BlockTeleport"); aiLayer = LayerMask.NameToLayer("AI"); }
            return layer == 2 || layer == blockTeleportLayer || layer == aiLayer;
        }

        // H4 (H23-review-opus minor 1): a collider on a shell layer that belongs to a creature (its prefab root holds a creature's
        // health) is a shell the homing arrows pass through (Hits.CastPastShells); a world collider on the same layer is not. Kept per
        // collider (instance ids are never reused in a session), at most 4096.
        static readonly Dictionary<int, bool> shells = new Dictionary<int, bool>();
        static readonly int T_Shell = Timing.Section("scene Seek.CreatureShell (a shell layer collider's owner, once per collider)");
        internal static bool CreatureShell(Collider c) {
            if (c == null || !ShellLayer(c.gameObject.layer)) return false;
            int id = c.GetInstanceID();
            if (shells.TryGetValue(id, out bool known)) return known;
            using var _t = Timing.Begin(T_Shell);
            bool shell = false;
            try {
                NetworkEntity e = c.GetComponentInParent<NetworkEntity>();
                NetworkEntity root = e != null && e.PrefabRoot != null ? e.PrefabRoot : e!;
                HealthObject h = root != null ? root.GetComponentInChildren<HealthObject>() : null!;
                shell = h != null && Targets.IsCreature(h);
            }
            catch { }
            if (shells.Count >= 4096) shells.Clear();
            shells[id] = shell;
            return shell;
        }

        static readonly int T_Body = Timing.Section("scene Seek.BodyOf (a lock's body colliders, at the release)");
        static List<Collider>? BodyOf(HealthObject h) {
            using var _t = Timing.Begin(T_Body);
            Transform root = Targets.RootOf(h);
#if QUEST
            if (root == null || h.Is<PlayerHealth>()) return null;   // 0.19.11: a player's middle is their chest (Targets.Centre), the same on every machine
#else
            if (root == null || h is PlayerHealth) return null;   // 0.19.11: a player's middle is their chest (Targets.Centre), the same on every machine
#endif
            root.GetComponentsInChildren(false, bodyScratch);
            var body = new List<Collider>();
            foreach (Collider c in bodyScratch) {
                if (c == null || c.isTrigger || !c.enabled || ShellLayer(c.gameObject.layer)) continue;
                ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
                if (det == null || det.HealthTarget != h) continue;
                body.Add(c);
            }
            bodyScratch.Clear();
            return body.Count > 0 ? body : null;
        }

        /// <summary>H3: the middle of a creature's body (its damageable colliders' bounds), else the middle of all its colliders.</summary>
        static Vector3 BodyCentre(HealthObject h, List<Collider>? body) {
            if (body != null) {
                bool any = false; Bounds b = default;
                foreach (Collider c in body) {
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    Bounds cb = c.bounds;
                    if (cb.size.sqrMagnitude < 1e-6f) continue;
                    if (!any) { b = cb; any = true; } else b.Encapsulate(cb);
                }
                if (any) return b.center;
            }
            return Targets.Centre(h, out _);
        }

        /// <summary>H3, the caves (the owner: "homing needs to be able to work in caves and other locations"): the free space round a lock,
        /// four casts against the world at the release on every machine that flies the volley: up from the body's middle, to each side
        /// across the line from the shooter, and up from the middle of that line; the creature, the shooter and the arrow are not in the
        /// way. Each reaches RoomCastMetres at most (more than the side offset can use).</summary>
        static readonly int T_Room = Timing.Section("scene Seek.Room (four casts per lock, at the release)");
        const float RoomCastMetres = 8f;
        static HomingRoom Room(Vector3 eye, Vector3 centre, Transform? root, Transform? shooter, Transform? arrow) {
            using var _t = Timing.Begin(T_Room);
            try {
                HomingFan.Basis(H(centre - eye), out HVec hr, out _);
                Vector3 right = V(hr);
                float Reach(Vector3 from, Vector3 dir) => Hits.Cast(from, dir, RoomCastMetres, out RaycastHit hit, root, shooter, arrow) ? hit.distance : RoomCastMetres;
                Vector3 mid = (eye + centre) * 0.5f;
                return new HomingRoom(Reach(centre, Vector3.up), Reach(centre, -right), Reach(centre, right), Reach(mid, Vector3.up));
            }
            catch (Exception e) {
                if (!roomWarned) { roomWarned = true; MelonLogger.Warning($"[Archery] Seek: measuring the room round a lock failed; open ground assumed (said once): {e.Message}"); }
                return HomingRoom.Open;
            }
        }
        static bool roomWarned;

        /// <summary>The room round lock <paramref name="index"/> (open ground for an unknown one).</summary>
        internal static HomingRoom RoomOf(Flight f, int index) => index >= 0 && index < f.Targets.Length ? f.Targets[index].Room : HomingRoom.Open;

        /// <summary>Every machine, VolleySim each physics tick: every lock of the release as this machine sees it now (body middle, alive,
        /// velocity from its last two middles, smoothed). Refreshed once per release per tick, shared by all of that release's arrows.</summary>
        static readonly int T_Refresh = Timing.Section("Seek.Targets refresh (body bounds per lock per tick)");
        internal static HomingTarget[] TargetsNow(Flight f, int tick) {
            if (f.Tick == tick) return f.Targets;
            using var _t = Timing.Begin(T_Refresh, f.Release);
            f.Tick = tick;
            float now = Time.time, since = now - f.RefreshedAt;
            f.RefreshedAt = now;
            for (int i = 0; i < f.Ids.Length; i++) {
                HealthObject? h = f.Healths[i];
                bool alive = h != null && h.Health > 0f;
                bool wasAlive = f.Alive[i];
                f.Alive[i] = alive;
                ref HomingTarget t = ref f.Targets[i];
                t.Alive = alive;
                if (!alive) { t.Vel = new HVec(0, 0, 0); continue; }
                Vector3 c = BodyCentre(h!, f.Body[i]);
                if (wasAlive && since > 1e-4f) {
                    HVec raw = H((c - f.Centres[i]) / since);
                    // a jump far faster than any creature runs is a teleport or a body that changed shape: no lead from it
                    if (raw.Finite && raw.Length < 30.0) t.Vel = t.Vel * (1.0 - VelocityBlend) + raw * VelocityBlend;
                    else t.Vel = new HVec(0, 0, 0);
                }
                f.Centres[i] = c;
                t.Pos = H(c);
            }
            return f.Targets;
        }

        const double VelocityBlend = 0.5;

        /// <summary>Where lock <paramref name="index"/> was at the release (an unresolved lock: <paramref name="fallback"/>).</summary>
        internal static Vector3 LaunchTarget(Flight f, int index, Vector3 fallback) =>
            index >= 0 && index < f.Ids.Length && f.Healths[index] != null ? f.Centres[index] : fallback;

        /// <summary>H2, clients, looks only (VolleySim): a homing ghost or pellet landed while it still homed on lock
        /// <paramref name="index"/>. The first one of each lock that landed on that lock's own creature shows its arrival burst
        /// (SeekLook.Arrived); a landing on anything else (cover) shows none. The main lock's burst is the core cluster's
        /// (CoreEnded), so with core arrows flying the others' landings on it do not show one.</summary>
        internal static void Arrived(Flight f, int index, RaycastHit hit) {
            if (index < 0 || index >= f.Ids.Length || index >= f.Arrived.Length || f.Arrived[index] || hit.collider == null) return;
            if (index == 0 && f.Cores > 0) return;
            HealthObject? h = f.Healths[index];
            if (h == null) return;
            Transform root = Targets.RootOf(h);
            if (root == null || !hit.collider.transform.IsChildOf(root)) return;
            f.Arrived[index] = true;
            SeekLook.Arrived(f, index, root, hit.point);
        }

        // ---- H5: the chain (HomingChain in Homing.cs; VolleySim drives it)

        /// <summary>The lock (index) whose creature <paramref name="c"/> belongs to, or -1: a collider under that lock's root, as Arrived
        /// tells a landing on the lock's own creature.</summary>
        internal static int LockOf(Flight f, Collider? c) {
            if (c == null) return -1;
            Transform t = c.transform;
            for (int i = 0; i < f.Roots.Length && i < HomingChain.MaxBits; i++) {
                Transform? r = f.Roots[i];
                if (r != null && t.IsChildOf(r)) return i;
            }
            return -1;
        }

        static readonly Transform?[] struckRoots = new Transform?[HomingChain.MaxBits];

        /// <summary>The roots of the locks in <paramref name="struck"/> (a chaining arrow's casts pass through them), in a shared array
        /// (main thread, filled again on every call; unused entries null); null when it has struck none.</summary>
        internal static Transform?[]? StruckRoots(Flight f, int struck) {
            if (struck == 0) return null;
            for (int i = 0; i < struckRoots.Length; i++) struckRoots[i] = HomingChain.IsStruck(struck, i) && i < f.Roots.Length ? f.Roots[i] : null;
            return struckRoots;
        }

        /// <summary>Every machine, Volley.Launch: one more arrow of this release chains (ChainLocks).</summary>
        internal static void ChainLaunched(Flight f) { f.ChainArrows++; f.ChainLive++; }

        /// <summary>A chaining arrow struck lock <paramref name="index"/>; <paramref name="next"/> is the lock it flies on to (-1: it ends
        /// there). Debug only.</summary>
        internal static void ChainStruck(Flight f, SpectralKind kind, int arrow, int index, int hits, int next) {
            if (!Logs.Debug) return;
            MelonLogger.Msg($"[Archery] Seek: arrow {f.Release} {kind} {arrow} struck lock {index} {Targets.NameOf(index < f.Ids.Length ? f.Ids[index] : 0u)} ({hits} of {f.Ids.Length}){(next >= 0 ? $", on to lock {next}" : ", ends there")}");
        }

        /// <summary>A chaining arrow of the release ended (any way). When the last one has, one line: Summary on the server and the
        /// shooter's own client, Debug for a watching client.</summary>
        internal static void ChainEnded(Flight? f, in HomingArrow h) {
            if (f == null) return;
            f.ChainLive--;
            f.ChainHits += h.Hits;
            if (HomingChain.Count(h.Struck) >= f.Ids.Length && f.Ids.Length > 0) f.ChainAll++;
            if (f.ChainLive > 0 || f.ChainLogged) return;
            f.ChainLogged = true;
            if (f.Ids.Length < 2) return;   // H5 review M-1: one lock cannot chain
            if (!(f.Server || f.Local ? Logs.Summary : Logs.Debug)) return;
            MelonLogger.Msg($"[Archery] Seek: arrow {f.Release} chain: {f.ChainArrows} {(Values.ChainLocks == ChainMode.All ? "ghosts and pellets" : "ghosts")}, {f.Ids.Length} lock(s), {f.ChainHits} chain hits ({f.ChainAll} struck all {f.Ids.Length}){(f.Server ? "" : f.Local ? " (own)" : " (seen)")}");
        }

        /// <summary>A homing arrow let go of its creature: every let-go is counted, the first of a release logged at Debug (H3, M-9: a
        /// core arrow is called one).</summary>
        internal static void LetGoSeen(Flight? f, SpectralKind kind, bool core, int index, LetGo why, Vector3 at) {
            if (f == null) return;
            f.LetGoes++;
            if (f.LetLogged || !Logs.Debug) return;
            f.LetLogged = true;
            MelonLogger.Msg($"[Archery] Seek: arrow {f.Release} {(core ? "core" : kind.ToString())} {index} let go ({why}) at {Targets.P(at)}; it flies straight on for {Values.LetGoSeconds:0.##} s and ends (first of this release)");
        }

        // ================================================================ the real arrow: it does not fly on a locked release

        // H3 (H1-review I-4): while held the arrow is out of the world on every machine: its colliders off (it cannot push the bow or
        // the hand, and no hand can close on it), kinematic (Discrete first: the game sets that itself only after a delay, and a
        // kinematic body with continuous detection logged Unity's warning at every locked release), hidden on clients. Every way out
        // (Free: placed, refused, timed out, the shooter gone, an exception) turns all of that back. What a hold changes and how it is
        // turned back is HeldArrow's, shared with Skyfall's hold.
        sealed class Hold : HeldArrow {
            public Vector3 At; public float CoreEndedAt = -1f;
            protected override string Who => "Seek";
        }
#if QUEST
        static readonly Dictionary<Arrow, Hold> holds = new Dictionary<Arrow, Hold>(Il.ByPointer<Arrow>());
#else
        static readonly Dictionary<Arrow, Hold> holds = new Dictionary<Arrow, Hold>();
#endif
        static readonly List<Arrow> holdScratch = new List<Arrow>();
        static readonly MethodInfo? embedMethod = AccessTools.Method(typeof(Embeddable), "Embed");
        /// <summary>H3, clients: seconds after their own core cluster ended that they wait for the server to place the arrow before they
        /// give it back its normal state where it is.</summary>
        const float PlaceWaitSeconds = 1.5f;

        /// <summary>Every machine, at a locked release (Volley.Launch, after Arrow.Shoot started its flight): the real arrow stops where
        /// it left the bow and is held out of the world (Hold). The server's own flight sync, which reaches the shooter after this, is
        /// refused for a held arrow (HeldFlight). The server places it where the core cluster lands (CoreEnded); clients show it where the
        /// server's sync puts it (Tick); it is let go MaxLifeSeconds + 1 s after the release at the latest.</summary>
        static readonly int T_HoldReal = Timing.Section("Seek.HoldReal (the real arrow held)");
        internal static void HoldReal(Arrow arrow, uint release) {
            using var _t = Timing.Begin(T_HoldReal, release);
            if (arrow == null) return;
            // H4: recorded before anything is changed, a throw part-way frees what was changed (HeldArrow.Take; an older hold of the
            // same arrow is freed first, which never moves it, so At is its place either way)
            HeldArrow.Take(holds, arrow, new Hold { Release = release, Until = Time.time + Values.MaxLifeSeconds + 1f, At = arrow.transform.position });
        }

        /// <summary>H4 (H23-review-opus I-1), clients: the server's embed of a held arrow (its placement where the core cluster landed)
        /// arrives while the arrow's colliders are still off for the hold; the game then calls Physics.IgnoreCollision between them and
        /// the creature's (Embeddable.DisableCollisionsWithOther), which Unity refuses for a disabled collider (one error per pair) and
        /// which is never set, so the colliders come back live inside the creature when the hold ends. The held arrow's colliders are
        /// put back on first (it stays hidden until Tick shows it); the game's own code then runs as for any embed.</summary>
        [HarmonyPatch(typeof(Embeddable), "SyncEmbed")]
        static class HeldEmbed {
            static readonly int T = Timing.Section("patch Embeddable.SyncEmbed / Seek.HeldEmbed");
#if QUEST
            static void Prefix(Embeddable __instance, Il2CppAlta.Serialization.Stream stream) {
#else
            static void Prefix(Embeddable __instance, Alta.Serialization.Stream stream) {
#endif
                using var _t = Timing.Begin(T);
                if (holds.Count == 0 || NetworkSceneManager.IsServer || __instance == null || stream == null || stream.IsWriting) return;
                try {
                    Arrow arrow = __instance.Entity != null ? __instance.Entity.GetComponent<Arrow>() : null!;
                    if (arrow == null || !holds.TryGetValue(arrow, out Hold h) || h.Off.Count == 0) return;
                    int n = 0;
                    foreach (Collider c in h.Off) { try { if (c != null) { c.enabled = true; n++; } } catch { } }
                    h.Off.Clear();
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek: arrow {h.Release}'s embed from the server: its {n} collider(s) back on before the game sets their ignores");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Seek: held-arrow embed check failed: {e.Message}"); }
            }
        }

        /// <summary>Clients: the server's flight sync for a held arrow (sent from the server's own Arrow.Shoot, it reaches the shooter after
        /// its release) would set it flying again: refused while it is held. Quickdraw 4's marker check on the same method is its own.</summary>
        [HarmonyPatch(typeof(Arrow), nameof(Arrow.StartHandlingArrowDeviance))]
        static class HeldFlight {
            static readonly int T = Timing.Section("patch Arrow.StartHandlingArrowDeviance / Seek.HeldFlight");
            static bool Prefix(Arrow __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (NetworkSceneManager.IsServer || holds.Count == 0 || __instance == null || !holds.TryGetValue(__instance, out Hold h) || Time.time > h.Until) return true;
                    HeldArrow.Stop(__instance);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek: arrow {h.Release}'s flight sync refused: the real arrow is held on a locked release");
                    return false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Seek: held-arrow check failed: {e.Message}"); return true; }
            }
        }

        /// <summary>Every machine, VolleySim: the first core arrow of a homing release ended (it landed, or its life ran out in the air).
        /// Once per release: on the server the real arrow is put there (embedded in what it struck, as the Quickdraw 4 line does, within
        /// PlaceMaxMetres and loaded world), and a landing on a creature stands in for the real arrow's first creature hit: Ricochet's
        /// real-arrow jump, off-path Shatter's blast and Weight's hook on the server; their looks on clients. Clients never move the real
        /// arrow (H3, H1-review I-3): it shows where the server's sync puts it (Tick).</summary>
        static readonly int T_CoreEnded = Timing.Section("Seek.CoreEnded (the real arrow's stand-in)");
        internal static void CoreEnded(uint release, bool landed, RaycastHit hit, Vector3 end, Vector3 dir) {
            using var _t = Timing.Begin(T_CoreEnded, release);
            Flight? f = Of(release);
            if (f == null || f.CoreEnded) return;
            f.CoreEnded = true;
            bool server = NetworkSceneManager.IsServer;
            Arrow? arrow = f.Arrow;
            Vector3 d = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
            HealthObject? h = null; ImpactDetector? det = null;
            if (landed && hit.collider != null) { det = hit.collider.GetComponentInParent<ImpactDetector>(); h = det != null ? det.HealthTarget : null; }
            bool creature = h != null && Targets.IsCreature(h);
            // H4 (H23-review M-4 second half = Opus minor 11): the stand-in hooks fire only for a creature the core could really damage
            // (the filters the core's own hit passes: channel, hardness, immunity), as the Quickdraw 4 line's need a dealt hit
            bool damaged = creature && Hits.CouldDamage(f.Tool, det);
            if (creature && !damaged && Logs.Debug) MelonLogger.Msg($"[Archery] Seek: arrow {release}'s core cluster met '{h!.name}' but cannot damage it: no real-arrow hit stands in");
            try {
                if (damaged) {
                    if (server) {
                        PlayerController? pc = f.Shooter != null ? f.Shooter.PlayerController : null;
                        Targets.RealHit(release, h!);
                        if (f.Tool != null) {
                            Ricochet.ServerLineHit(release, h!, f.Tool, f.CoreMultiplier);
                            Shatter.ServerLineHit(release, h!, hit.point, d, f.Tool, float.NaN, pc);
                        }
                        Weight.OnLineHit(release, h, f.Shooter);
                    }
                    else if (!ApplicationManager.IsHeadless) {
                        Targets.RealHit(release, h!);
                        Ricochet.ClientStruck(release, h!, true, hit.point);
                        Shatter.ClientRealHit(release, arrow, det, h, hit.point, d, null);
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek: arrow {release}'s core landing hooks failed: {e.Message}"); }
            // H2, looks (clients): the heavier burst where the core cluster lands, the thump in the shooter's bow hand, the main
            // lock's cage thrown outward when that is what it struck (never throws: SeekLook catches its own)
            if (!ApplicationManager.IsHeadless) SeekLook.CoreLanded(f, landed, creature ? h : null, landed ? hit.point : end, d);
            if (server) PlaceReal(f, landed, hit, landed ? hit.point : end, d);
            else if (arrow != null && holds.TryGetValue(arrow, out Hold hold) && hold.Release == release) hold.CoreEndedAt = Time.time;
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek: arrow {release} core cluster {(landed ? "landed on '" + (hit.collider != null ? hit.collider.name : "?") + "'" + (creature ? " (a creature: the real arrow's hit)" : "") : "ended in the air")} at {Targets.P(landed ? hit.point : end)}{(server ? "" : " (looks; the server places the real arrow)")}; {f.LetGoes} arrow(s) of the release let go so far");
        }

        /// <summary>Server: the real arrow placed where the core ended (H3: within PlaceMaxMetres of the archer and in loaded world, else
        /// along the way at that reach, in the air, as a Quickdraw 4 line that hit nothing), and given its body back in every case.</summary>
        static void PlaceReal(Flight f, bool landed, RaycastHit hit, Vector3 point, Vector3 dir) {
            Arrow? arrow = f.Arrow;
            if (arrow == null || !holds.TryGetValue(arrow, out Hold hold) || hold.Release != f.Release) return;   // already let go, or held for a later shot
            holds.Remove(arrow);
            bool freed = false;
            try {
                Pickup? p = arrow.Entity != null ? arrow.Entity.CommonPickup : null;
                if (p != null && (p.IsInteractedWith || p.IsDocked)) { hold.Free(arrow, true); freed = true; return; }   // someone took it: theirs
                Vector3? head = Targets.HeadOf(f.Shooter);
                if (head.HasValue) {
                    HomingValues v = Values;
                    float wanted = (point - head.Value).magnitude;
                    // H4 (Opus minor 3): an end point in loaded world within PlaceMaxMetres is within, whatever the 10 m steps of
                    // LoadedReach say about the way there (an end within 10 m of the loaded edge was put short, in the air)
                    float reach = wanted <= v.PlaceMaxMetres && Hits.Loaded(point) ? wanted + 0.01f
                                : wanted <= v.PlaceMaxMetres ? Hits.LoadedReach(head.Value, point - head.Value, Mathf.Max(10f, wanted + 10f)) : Hits.LoadedReach(head.Value, point - head.Value, v.PlaceMaxMetres);
                    if (!HomingPlace.Within(H(head.Value), H(point), v, reach)) {
                        Vector3 to = V(HomingPlace.Fallback(H(head.Value), H(point), v, reach));
                        if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Seek: arrow {f.Release}'s core ended {wanted:0} m out (loaded reach {reach:0} m, at most {v.PlaceMaxMetres:0} m): the real arrow is put {(to - head.Value).magnitude:0} m out along the way, in the air"));
                        point = to; landed = false;
                        if ((point - head.Value).sqrMagnitude > 1e-4f) dir = (point - head.Value).normalized;
                    }
                }
                hold.Free(arrow, false); freed = true;
                Transform tip = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
                Rigidbody rb = arrow.Rigidbody;
                arrow.StopUpdatingArrowFlight();
                arrow.transform.rotation = Quaternion.LookRotation(dir);
                Vector3 pos = point + dir * (landed ? 0.03f : 0f) - (tip.position - arrow.transform.position);
                arrow.transform.position = pos;
                if (rb != null) { rb.position = pos; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                if (landed) {
                    Embeddable embed = arrow.Entity != null ? arrow.Entity.GetComponent<Embeddable>() : null!;
                    if (embed != null && embedMethod != null) embedMethod.Invoke(embed, new object?[] { hit.rigidbody, arrow.transform.position, arrow.transform.rotation });
                }
                if (arrow.Entity != null) arrow.Entity.ForcePositionSync();
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek: placing arrow {f.Release}'s real arrow failed; it is let go where it is: {e.Message}"); }
            finally {
                if (!freed) { hold.Free(arrow, false); try { if (arrow.Entity != null) arrow.Entity.ForcePositionSync(); } catch { } }
            }
        }

        /// <summary>Every frame (Main.OnUpdate, the headless server too). Clients: a held arrow that the server has placed (embedded, or
        /// moved from where it was held) after this client's core ended is shown there (H3, I-3); one still unplaced PlaceWaitSeconds
        /// after that gets its normal state back where it is; one the server sets flying before the core ended means the server flew an
        /// ordinary volley (it refused the lock: the known gap): the homing arrows of that release end and the arrow is shown flying.
        /// Everyone: a hold past its time is let go where it is.</summary>
        internal static void Tick() {
            if (holds.Count == 0) return;
            float now = Time.time;
            bool server = NetworkSceneManager.IsServer;
            holdScratch.Clear();
            foreach (KeyValuePair<Arrow, Hold> kv in holds) holdScratch.Add(kv.Key);
            foreach (Arrow a in holdScratch) {
                if (!holds.TryGetValue(a, out Hold h)) continue;
                try {
                    // H4 (H23-review minor 1): an arrow destroyed while held still gives back what its hold turned off
                    if (a == null) { holds.Remove(a!); h.Free(null, true); continue; }
                    if (!server) {
                        Embeddable embed = a.Entity != null ? a.Entity.GetComponent<Embeddable>() : null!;
                        bool embedded = embed != null && embed.IsEmbedded;
                        float moved = (a.transform.position - h.At).magnitude;
                        Rigidbody rb = a.Rigidbody;
                        bool flying = rb != null && !rb.isKinematic && rb.velocity.magnitude > RefusedSpeed;
                        if (h.CoreEndedAt < 0f && flying && !embedded && moved > 1f) {
                            holds.Remove(a);
                            h.Free(a, true);
                            Flight? f = Of(h.Release);
                            if (f != null) {
                                f.Refused = true;
                                // H4 (H23-review minor 5): every cage kept for this release springs open now, not only the main lock's
                                if (!ApplicationManager.IsHeadless) SeekLook.Refused(f);
                            }
                            if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek: arrow {h.Release}: the server flies the real arrow (it refused the lock and fired an ordinary volley); the homing picture is dropped here");
                            continue;
                        }
                        if (h.CoreEndedAt >= 0f && (embedded || moved > 0.25f)) {
                            holds.Remove(a);
                            h.Free(a, true);
                            if (!ApplicationManager.IsHeadless) SeekLook.Reveal(a, Of(h.Release)?.Local ?? false);
                            if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Seek: arrow {h.Release}'s real arrow shown where the server put it ({(embedded ? "embedded" : "moved " + moved.ToString("0.0", CultureInfo.InvariantCulture) + " m")}, {(now - h.CoreEndedAt) * 1000f:0} ms after this client's core ended)"));
                            continue;
                        }
                        if (h.CoreEndedAt >= 0f && now - h.CoreEndedAt > PlaceWaitSeconds) {
                            holds.Remove(a);
                            h.Free(a, false);
                            if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Seek: arrow {h.Release}: no placement from the server {PlaceWaitSeconds:0.#} s after this client's core ended; the real arrow is shown where it is"));
                            continue;
                        }
                    }
                    if (now > h.Until) {
                        holds.Remove(a);
                        h.Free(a, false);
                        if (server && a.Entity != null) a.Entity.ForcePositionSync();
                        MelonLogger.Warning($"[Archery] Seek: arrow {h.Release}'s real arrow was still held {Values.MaxLifeSeconds + 1f:0.#} s after the release (no core arrow ended); let go where it is");
                    }
                }
                catch (Exception e) { holds.Remove(a); h.Free(a, false); MelonLogger.Warning($"[Archery] Seek: arrow {h.Release}'s hold failed; let go: {e.Message}"); }
            }
        }

        /// <summary>A held arrow moving faster than this (m/s) on a client was set flying by the server (a fall from rest stays slower for 2 s).</summary>
        const float RefusedSpeed = 20f;
    }
}