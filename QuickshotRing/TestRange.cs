using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ATT.Saving;
using Alta.Chunks;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // The play-test range (design docs/superpowers/specs/2026-11-18-test-range-design.md; the rules are in Range.cs, tested).
    // Server only, driven like TestTools: the button window (tools\TestBuildMenu.ps1) writes a request number into the server's
    // [TestRange] settings and the server acts once per new number (TestRequest.IsNew; the numbers it started with are never acted
    // on; a press before this process is a server is recorded and ignored). Every result, refusals included, is written to
    // UserData\QuickshotRing\range-status.txt, which the window shows.
    //
    //   TestRange.cs         settings, requests, the status file, building and removing the range, the creature spots
    //   TestRangeShelf.cs    the shelves: plain and crafted arrows, quivers, refilling, the never-saved trap, loose arrows
    //   TestRangeArchery.cs  points for everyone, buy for everyone, reset / snapshot / restore, heal, noon, teleport
    //   TestRangeBuild.cs    R2: the construction (AutoFace, pads, stacks, backstops, lamps) from the planner in RangeBuild.cs
    //
    // Master switch [TestRange] Enabled (server, default false): off, every request is refused (and said so) and nothing is built,
    // spawned or touched; Poll is one bool test. Nothing of the range is ever saved into the world: furniture, creatures and every
    // item it spawns are SaveMode.Ignore, and items also Pickup.BaseSaveMode = Ignore (a dock sets an undocked item back to its
    // BaseSaveMode, PickupDock.ChangePickupsSaveMode); an item taken from a range dock is set to Ignore again in that dock's
    // ItemUndocked (the game makes the next one on the shelf from a save copy, CreateNewImage). The exception, by the owner's
    // decision: an arrow a player docks into their own quiver or bag becomes part of the player's equipment and is theirs.
    // The range is built only within [TestRange] ShrineMetres of the loaded archery (Ranged) shrine (owner's decision); no second
    // shrine is ever spawned.
    internal static partial class TestRange {
        static MelonPreferences_Entry<bool> enabled = null!, frozen = null!, headless = null!;
        const string HeadlessOwner = "(no player)";
        const float HeadlessStandMetres = 3f; // ponytail: fixed spot in front of the tree; a setting if anyone tests headless from elsewhere
        static MelonPreferences_Entry<float> respawnSeconds = null!, leashMetres = null!, freezeSeconds = null!, shrineMetres = null!, shelfMetres = null!,
            shelfYawOffset = null!, refillSeconds = null!, laneGap = null!, nearMetres = null!, tickSeconds = null!, maxSlope = null!, maxStep = null!, creatureStep = null!, playerClear = null!;
        static MelonPreferences_Entry<int> arrowShelfPrefab = null!, gearShelfPrefab = null!, shaftPrefab = null!;
        static MelonPreferences_Entry<string> arrowShelf = null!, gearShelf = null!, lanes = null!;

        static bool Server => NetworkSceneManager.IsServer;
        static bool started;
        static bool work; // Poll's one test: something is queued, a range stands, points are kept up or loose arrows are tracked

        // ---------------------------------------------------------------- requests

        /// <summary>A request as it was seen: its number and what it acts on, captured at that moment.</summary>
        sealed class Captured {
            public Request Of = null!;
            public string Number = "", Name = "", Text = "";
        }

        sealed class Request {
            public MelonPreferences_Entry<string> Entry = null!;
            public string Last = "", Label = "";
            public Action<Captured> Act = null!;
            public Func<string>? Name;   // who it acts on, captured at the press (null: nobody)
            public Func<string>? Text;   // what it acts with, captured at the press
        }

        static readonly List<Request> requests = new List<Request>();
        static readonly List<Captured> pending = new List<Captured>();

        static Request Add(MelonPreferences_Category cat, string key, string label, string description, Action<Captured> act, Func<string>? name = null, Func<string>? text = null) {
            var r = new Request { Entry = cat.CreateEntry(key, "", description: description), Label = label, Act = act, Name = name, Text = text };
            requests.Add(r);
            return r;
        }

        internal static void Init() {
            var cat = Cfg.Category("TestRange");
            enabled = cat.CreateEntry("Enabled", false, description: "Server only, for play-test servers. The play-test range's master switch. Off (the default): every [TestRange] request is refused (the button window says so) and nothing is built, spawned or touched.");
            string req = "Server only. The button window writes a new number here on every press; the server acts once per new number while it runs (the number it started with is never acted on). ";
            Add(cat, "BuildRequest", "build", req + "Builds the range around [TestBuild] Player, facing where their head faces: only within ShrineMetres of the archery shrine; refused while one stands.", Build, () => TestBuild.Configured);
            Add(cat, "RemoveRequest", "remove", req + "Removes the range: the shelves and the range's items still on them, the dummies, every test creature. Arrows players took are theirs; a range shelf or quiver takes only the range's own items, and anything else found on a shelf is put down beside it.", c => Remove("the Remove button", c));
            Add(cat, "RefillRequest", "refill", req + "Tops every shelf slot back to full now (also retries a slot that failed).", Refill);
            Add(cat, "KillRequest", "kill all", req + "Kills every live range creature (health to 0: they die normally and respawn if RespawnSeconds is above 0). Dummies stay.", Kill);
            InitShelf(cat);
            InitBuild(cat); // R2: the construction settings (TestRangeBuild.cs)
            frozen =cat.CreateEntry("Frozen", true, description: "Server only, live. The range's creatures stand still (true) or move and attack (false), at once and for later respawns. A free one farther than LeashMetres from its spot is put back.");
            respawnSeconds = cat.CreateEntry("RespawnSeconds", 5f, description: "Server only, live. A dead range creature comes back at its own spot after this many seconds (its body removed first). 0 = they stay dead until the range is rebuilt.");
            leashMetres = cat.CreateEntry("LeashMetres", 8f, description: "Server only, live. A free (not frozen) range creature farther than this from its spot is removed and spawned again there. 0 = no leash.");
            freezeSeconds = cat.CreateEntry("FreezeSeconds", 2f, description: "Server only. Seconds after a range creature is spawned until Frozen applies to it (it settles on the ground first).");
            lanes = cat.CreateEntry("Lanes", RangeDefaults.Lanes, description: "Server only. The creature lanes, '|' between lanes: 'Name: kind[,kind...] @bearing spot,spot,...'. Bearing in degrees from where you face (right positive). A spot is metres from you; 'L'/'R' after it = 2 m to that side of the lane ('L3.5' = 3.5 m); 'rowN' = the kinds stand across the lane, N m apart. Kinds: one for all spots or one per spot; a prefab name (Turabada, Turabada Iron Variant, Gotera Birch Variant, combat_dummy, ...). Combat dummies never die. After the spots: 'free' = this lane's creatures are never frozen (a moving lane, leashed), 'nowall' = no backstop behind it. With AutoFace the bearings are from the facing it picks.");
            laneGap = cat.CreateEntry("LaneGapMetres", 15f, description: "Server only. Build warns (and builds anyway) when a piece of one lane stands closer than this to a piece of another lane: Ricochet's jump (15 m), a Shatter blast or a lock in one lane could reach the other.");
            shrineMetres = cat.CreateEntry("ShrineMetres", 60f, description: "Server only. Build is refused unless the player stands within this many metres of the loaded archery shrine (the owner's decision: only near the archery shrine).");
            shelfMetres = cat.CreateEntry("ShelfMetres", 1.6f, description: "Server only. The two shelves stand this far to your right (arrows) and left (bows and quivers), turned toward you.");
            shelfYawOffset = cat.CreateEntry("ShelfYawOffset", 0f, description: "Server only. Extra turn of the shelves in degrees, in case a shelf's front is not its forward side (not known before a play-test).");
            maxSlope = cat.CreateEntry("MaxSlopeDegrees", 35f, description: "Server only. A piece is not placed on ground steeper than this.");
            maxStep = cat.CreateEntry("MaxStepMetres", 3f, description: "Server only. A shelf is not placed where the ground is more than this above or below the ground you stand on (a roof, a cliff). If the arrow shelf cannot be placed the build is refused.");
            creatureStep = cat.CreateEntry("CreatureStepMetres", 12f, description: "Server only. A creature or dummy is not placed where the ground is more than this above or below the ground you stand on.");
            playerClear = cat.CreateEntry("PlayerClearMetres", 2f, description: "Server only. A piece is not placed within this many metres of another player's head (the builder is not counted).");
            nearMetres = cat.CreateEntry("NearMetres", 120f, description: "Server only. A range creature that is gone without dying (its area unloaded, or it was removed) is spawned again only while a player is within this many metres of its spot.");
            tickSeconds = cat.CreateEntry("TickSeconds", 0.5f, description: "Server only. How often the range's creature spots are checked (respawn, freeze, leash).");
            arrowShelfPrefab = cat.CreateEntry("ArrowShelfPrefab", 23630, description: "Server only. The arrow shelf's prefab hash (the game's 20-slot 'Shelf').");
            gearShelfPrefab = cat.CreateEntry("GearShelfPrefab", 20452, description: "Server only. The bow and quiver shelf's prefab hash (the game's 10-slot 'Shelf Short').");
            headless = cat.CreateEntry("HeadlessBuild", false, description: "Server only, for checking the range with nobody online. On, and [TestBuild] Player not online: Build stands a virtual archer 3 m in front of the archery tree, facing away from it, and builds there; the range then stays until Remove (nobody to leave).");
            InitArchery(cat);
            Cfg.Reloaded += Reload;
        }

        /// <summary>After Cfg.Ready: the request numbers in the file now are the ones the server started with; never acted on.</summary>
        internal static void Started() {
            foreach (Request r in requests) r.Last = (r.Entry.Value ?? "").Trim();
            RememberLive();
            started = true;
            UpdateWork();
        }

        static readonly int T_Reload = Timing.Section("TestRange.Reload");
        static void Reload() {
            using var _t = Timing.Begin(T_Reload);
            if (!started) return;
            try {
                bool server = Server;
                foreach (Request r in requests) {
                    if (!TestRequest.IsNew(r.Last, r.Entry.Value)) continue;
                    r.Last = r.Entry.Value.Trim();
                    if (!server) {
                        if (ApplicationManager.IsHeadless) MelonLogger.Warning($"[Archery] TEST RANGE: {r.Label} request {r.Last} ignored: the server is not running yet (press again once it is)");
                        continue;
                    }
                    var c = new Captured { Of = r, Number = r.Last, Name = r.Name != null ? r.Name() : "", Text = r.Text != null ? r.Text() : "" };
                    if (!enabled.Value) { Refuse(c, "the range is switched off on this server ([TestRange] Enabled = false in the server's QuickshotRing.cfg)"); continue; }
                    pending.Add(c);
                }
                if (server) LiveChanged();
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: reading the requests failed: {e.Message}"); }
            UpdateWork();
        }

        // H4 (range minor 3): nothing runs once the switch is off (the range is removed then, the loose arrows forgotten); while it is on,
        // Poll also keeps the online list (C-1) and a teleport's arrival check
        static void UpdateWork() => work = pending.Count > 0 || range != null || job != null || (enabled != null && enabled.Value) || teleport != null;

        static readonly int T_Poll = Timing.Section("TestRange.Poll");
        /// <summary>Server, every frame (Main.OnUpdate, before the headless early return). One bool test while nothing is going on.</summary>
        internal static void Poll() {
            if (!work) return;
            using var _t = Timing.Begin(T_Poll);
            if (!Server) return;
            while (pending.Count > 0) {
                Captured c = pending[0];
                pending.RemoveAt(0);
                try { c.Of.Act(c); }
                catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: {c.Of.Label} request {c.Number} failed: {e}"); Status(c, false, $"failed inside the server: {e.Message}"); }
            }
            float now = Time.time;
            if (job != null) TickJob(now); // R2: a build in progress (its own try and timing section)
            if (range != null) {
                try { Tick(now); } catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: tick failed: {e.Message}"); }
            }
            try { TickShelf(now); } catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: shelf tick failed: {e.Message}"); }
            try { TickArchery(now); } catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: points tick failed: {e.Message}"); }
            try { TickOnline(now); } catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: online list failed: {e.Message}"); }
            try { TickTeleport(now); } catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: teleport check failed: {e.Message}"); }
            UpdateWork();
        }

        /// <summary>Server, on quit: everything the range spawned goes (it is never saved either way).</summary>
        internal static void Shutdown() {
            try { if (Server && range != null) Remove("server shutdown", null); }
            catch { }
            try { if (Server && lastOnline.Length > 0) WriteOnline("server stopped", new List<KeyValuePair<string, string?>>()); }
            catch { }
        }

        // ---------------------------------------------------------------- the status the window shows

        static string StatusFile => Path.Combine(Cfg.Folder, "range-status.txt");
        static string OnlineFile => Path.Combine(Cfg.Folder, "range-online.txt");

        /// <summary>H4 (range minor 9): a whole file at once: written beside it, then moved over it (File.Replace), so the window never
        /// reads half a file and its open read (FileShare.Delete) never blocks the write; a few tries, then a plain write.</summary>
        internal static void WriteWhole(string file, string text) { // also Skyfall's grant file
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, text);
            for (int i = 0; ; i++) {
                try {
                    if (File.Exists(file)) File.Replace(tmp, file, null);
                    else File.Move(tmp, file);
                    return;
                }
                catch (Exception e) when (i < 3 && (e is IOException || e is UnauthorizedAccessException)) { System.Threading.Thread.Sleep(15); }
                catch (Exception) {
                    File.WriteAllText(file, text);
                    try { File.Delete(tmp); } catch { }
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- H4 (range C-1): who is online, for the window's question

        static float nextOnline;
        static string lastOnline = "";

        static readonly int T_Online = Timing.Section("TestRange.TickOnline (online list)");
        /// <summary>While the range is switched on: every 2 s the players online and whether each is fully loaded (TestTools.ReadyFor),
        /// written to range-online.txt when it changed. "Buy for all" names how many players it touches from this.</summary>
        static void TickOnline(float now) {
            if (enabled == null || !enabled.Value || now < nextOnline) return;
            using var _t = Timing.Begin(T_Online);
            nextOnline = now + 2f;
            var players = new List<KeyValuePair<string, string?>>();
            foreach (IPlayer ip in Player.AllPlayers.ToList()) {
                if (!(ip is Player pl)) continue;
                string name = Builds.NameOf(pl);
                players.Add(new KeyValuePair<string, string?>(name, TestTools.ReadyFor(name, out _, out _)));
            }
            WriteOnline("on", players);
        }

        static void WriteOnline(string state, List<KeyValuePair<string, string?>> players) {
            string body = RangeOnline.Format(players, state, "");
            if (body == lastOnline) return;
            lastOnline = body;
            try { WriteWhole(OnlineFile, RangeOnline.Format(players, state, DateTime.Now.ToString("HH:mm:ss"))); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: cannot write {OnlineFile}: {e.Message}"); }
        }

        static void Refuse(Captured c, string why) => Status(c, false, why);

        /// <summary>One line in the log (warning: refusals and actions are rare and asked for) and the status file the window reads.
        /// <paramref name="partly"/>: it ran, but not all of it worked (the text says what did not).</summary>
        static void Status(Captured? c, bool ok, string text, bool partly = false) {
            string label = c != null ? $"{c.Of.Label} request {c.Number}" : "range";
            string result = !ok ? "refused" : partly ? "partly" : "done";
            MelonLogger.Warning($"[Archery] TEST RANGE: {label} {result}: {text}");
            WriteStatus(c != null ? c.Number : "", result, text);
        }

        static void WriteStatus(string number, string result, string text) {
            try { WriteWhole(StatusFile, RangeStatus.Format(number, result, text, StateLine(), DateTime.Now.ToString("HH:mm:ss"))); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: cannot write {StatusFile}: {e.Message}"); }
        }

        static string StateLine() {
            string s = range == null ? (job != null ? "a range is being built" : "no range") : $"range by '{range.Owner}': {range.Posts.Count(p => p.Exists)} of {range.Posts.Count} creatures/dummies up, {range.Pieces.Count(p => p != null && !p.IsBeingDestroyed)} pieces, {range.Slots.Count} shelf slots";
            return $"{s}; enemies {(frozen.Value ? "frozen" : "free")}; respawn {(respawnSeconds.Value > 0f ? respawnSeconds.Value.ToString("0.#") + " s" : "off")}{PointsState()}";
        }

        // ---------------------------------------------------------------- the range

        sealed class Post {
            public string Lane = "", Kind = "";
            public uint Hash;
            public Vector3 At;
            public Quaternion Facing;
            public bool Dummy;
            public int Stood; // times StandDummy had to stand this spot's dummy up again (logged the first three)
            public Quaternion Rot = Quaternion.identity; // the spawn rotation: Facing turned by the prefab's own root rotation
            public bool Free;          // R2: a moving lane's creature: never frozen (leashed as every free one)
            public string Stand = "";  // R2: what it stands on ("on the ground", "on a pad (the ground varies 0.42 m)")
            public NetworkEntity? Entity;
            public Alta.Intelligence.Agent? Agent;
            public HealthObject? Health;
            public float DownSince = -1f, FreezeAt, SpawnedAt;
            public bool FreezeApplied, FrozenNow;
            public int Spawns;
            public bool Exists => Entity != null && !Entity.IsBeingDestroyed;
        }

        sealed class RangeState {
            public string Owner = "";
            public Vector3 Origin;
            public float Yaw, Built;
            public readonly List<NetworkEntity> Furniture = new List<NetworkEntity>();
            public readonly List<NetworkEntity> Pieces = new List<NetworkEntity>(); // R2: pads, stacks, walls, lamps
            public readonly List<Slot> Slots = new List<Slot>();
            public readonly List<Post> Posts = new List<Post>();
        }

        static RangeState? range;
        static float nextTick;
        static readonly HashSet<uint> namesChecked = new HashSet<uint>();
        static readonly Dictionary<string, uint> resolved = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase); // H4 (range minor 5)

        /// <summary>The first time a hash is used: its prefab's name, so a wrong hash shows at once. Null when the game has no such prefab.</summary>
        static NetworkPrefab? PrefabOf(uint hash, string meant) {
            NetworkPrefab prefab = hash != 0u ? PrefabManager.GetPrefab(hash) : null!;
            if (namesChecked.Add(hash) && Logs.Summary)
                MelonLogger.Msg($"[Archery] TEST RANGE: prefab {hash} for '{meant}' is {(prefab != null ? $"'{prefab.name}'" : "MISSING in this game")}");
            return prefab;
        }

        static readonly int T_Resolve = Timing.Section("asset TestRange.Resolve (PrefabManager.GetPrefab by name)");
        /// <summary>A creature or item kind by name: the range's table first, then the game's own prefab list by name (a scan of every
        /// prefab, so each name's answer is kept, a miss too: H4, range minor 5).</summary>
        static uint Resolve(string name, uint fromTable) {
            if (fromTable != 0u) return fromTable;
            string key = (name ?? "").Trim();
            if (resolved.TryGetValue(key, out uint known)) return known;
            using var _t = Timing.Begin(T_Resolve, key);
            uint hash = 0u;
            try {
                NetworkPrefab p = PrefabManager.GetPrefab(key.ToLowerInvariant());
                hash = p != null ? p.Hash : 0u;
            }
            catch { }
            resolved[key] = hash;
            return hash;
        }

        static readonly int T_FindShrine = Timing.Section("scene TestRange.FindShrine (FindObjectsOfType SkillShrine)");
        /// <summary>The loaded archery (Ranged) shrine nearest to <paramref name="at"/>, or null.</summary>
        static SkillShrine? FindShrine(Vector3 at, out float distance) {
            using var _t = Timing.Begin(T_FindShrine);
            distance = float.NaN;
            SkillShrine? best = null;
            if (!Tree.Ready) return null;
            foreach (SkillShrine s in UnityEngine.Object.FindObjectsOfType<SkillShrine>()) {
                if (s == null || s.CurrentPath != Tree.RangedPath) continue;
                float d = Flat(s.transform.position, at);
                if (best == null || d < distance) { best = s; distance = d; }
            }
            return best;
        }

        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        // R2: the ground and everything in the way are read by TestRangeBuild.WorldProbe for the planner (RangeBuild.cs), which
        // replaced the one-cast ground check per piece (PlacementRule) of the first build.

        static float NearestOtherPlayer(Vector3 at, string skip) {
            float best = float.PositiveInfinity;
            foreach (IPlayer ip in Player.AllPlayers) {
                if (!(ip is Player p) || p.PlayerController == null || p.PlayerController.Head == null) continue;
                if (string.Equals(Builds.NameOf(p), skip, StringComparison.OrdinalIgnoreCase)) continue;
                best = Mathf.Min(best, Vector3.Distance(p.PlayerController.Head.position, at));
            }
            return best;
        }

        static readonly int T_Build = Timing.Section("TestRange.Build");
        static void Build(Captured c) {
            using var _t = Timing.Begin(T_Build);
            if (range != null) { Refuse(c, $"a test range already stands (built by '{range.Owner}' {Time.time - range.Built:0} s ago); press Remove first"); return; }
            if (job != null) { Refuse(c, $"a test range is being built right now (for '{job.Owner}', {Time.time - job.Started:0.0} s ago); wait for it"); return; }
            if (c.Name.Length == 0) { Refuse(c, "[TestBuild] Player is empty (type your name in the window)"); return; }
            string? notReady = TestTools.ReadyFor(c.Name, out Player? p, out _);
            Vector3 stand, forward;
            float shrineDistance;
            string owner;
            SkillShrine? shrine;
            if ((notReady != null || p == null) && headless.Value) {
                // nobody online: a virtual archer HeadlessStandMetres in front of the tree (the player's side is the tree frame's -x,
                // SkillShrine.OpenOnServer), facing away from it
                // the server loads the Ranged tree only when a player joins or a shrine asks for it: ask for it here
                if (!Tree.Ready) {
                    try { ProfessionSkillTree.GetProfessionTree(ProgressionPath.Ranged); }
                    catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: HeadlessBuild: asking for the Ranged tree failed: {e.Message}"); }
                }
                SkillShrine? any = FindShrine(Vector3.zero, out _);
                if (any == null || any.SkillTreeTarget == null) {
                    Refuse(c, $"HeadlessBuild: no archery shrine is loaded on the server (Ranged tree ready: {Tree.Ready}; shrines of any profession loaded: {UnityEngine.Object.FindObjectsOfType<SkillShrine>().Length}; with nobody online the server may not load the shrine's area)");
                    return;
                }
                Transform tree = any.SkillTreeTarget;
                forward = tree.rotation * Vector3.left;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-4f) { Refuse(c, "HeadlessBuild: the tree's front is not level"); return; }
                forward.Normalize();
                stand = tree.position + forward * HeadlessStandMetres;
                shrine = FindShrine(stand, out shrineDistance);
                owner = HeadlessOwner;
                MelonLogger.Warning($"[Archery] TEST RANGE: HeadlessBuild: nobody online, a virtual archer stands at {Targets.P(stand)} (tree at {Targets.P(tree.position)}, shrine root at {Targets.P(any.transform.position)}) facing {Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg:0} degrees");
            } else {
                if (notReady != null || p == null) { Refuse(c, $"'{c.Name}': {notReady ?? "not online"}"); return; }
                Transform? head = p.PlayerController != null ? p.PlayerController.Head : null;
                if (head == null) { Refuse(c, $"'{c.Name}' has no body right now"); return; }
                shrine = FindShrine(head.position, out shrineDistance);
                stand = head.position;
                forward = head.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-4f) { forward = head.up; forward.y = 0f; }
                if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
                forward.Normalize();
                owner = Builds.NameOf(p);
            }
            // a fixed spot and facing (BuildSpot "x, z, yaw"): the same range every time, wherever the player stands; AutoFace is skipped
            bool fixedSpot = NoonRule.Spot(buildSpot.Value, out float fx, out float fz, out float fyaw);
            if (fixedSpot) {
                stand = new Vector3(fx, stand.y + 2f, fz);
                forward = new Vector3(Mathf.Sin(fyaw * Mathf.Deg2Rad), 0f, Mathf.Cos(fyaw * Mathf.Deg2Rad));
                shrine = FindShrine(stand, out shrineDistance);
                MelonLogger.Msg($"[Archery] TEST RANGE: BuildSpot: building at ({fx:0.00}, {fz:0.00}) facing {fyaw:0} degrees (AutoFace skipped)");
            }
            string? far = ShrineRule.Refusal(!float.IsNaN(shrineDistance), shrineDistance, shrineMetres.Value);
            if (far != null) { Refuse(c, far); return; }
            Vector3 rayFrom = owner == HeadlessOwner || fixedSpot ? stand + Vector3.up * 2f : stand; // a virtual archer stands at the tree's height, maybe below the ground
            if (!Physics.Raycast(rayFrom, Vector3.down, out RaycastHit under, owner == HeadlessOwner || fixedSpot ? 12f : 4f, ~0, QueryTriggerInteraction.Ignore)) { Refuse(c, "no ground under you"); return; }
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            Vector3 origin = new Vector3(stand.x, under.point.y, stand.z);
            // R2: the rest is a job over the next frames (TestRangeBuild.cs): AutoFace, the layout from the planner (RangeBuild.cs), the
            // shelves, the pieces within BuildFrameMs per frame, the creatures; it answers the window when it is done or refused
            StartBuild(c, owner, origin, yaw, shrineDistance, shrine);
        }

        static readonly int T_SpawnPost = Timing.Section("TestRange.SpawnPost");
        /// <summary>Spawns the creature or dummy of a spot (never saved). False when the game gave nothing.</summary>
        static bool SpawnPost(Post post, float now) {
            using var _t = Timing.Begin(T_SpawnPost, post.Kind);
            post.Entity = null; post.Agent = null; post.Health = null;
            NetworkPrefab? prefab = PrefabOf(post.Hash, post.Kind);
            if (prefab == null) return false;
            LocationChunk chunk = LocationChunkHelper.GetBestContaining(post.At);
            if (chunk == null) return false;
            // the prefab's own root rotation is kept under the facing: a combat dummy's root is turned -90 degrees about x so its body stands
            // (spawned with the facing alone it lay flat, 0.17.6 to 0.17.9)
            post.Rot = post.Facing * prefab.transform.localRotation;
            NetworkEntity e = SpawnHelper.Spawn(prefab, SpawnData.Default, chunk, post.At, post.Rot,
                x => { if (x != null && x.Prefab != null) x.Prefab.SaveMode = SaveMode.Ignore; }); // never in the world save
            if (e == null) return false;
            post.Entity = e;
            post.Agent = e.GetComponentInChildren<Alta.Intelligence.Agent>();
            post.Health = e.GetComponentInChildren<HealthObject>();
            post.DownSince = -1f;
            post.FreezeAt = now + Mathf.Max(0f, freezeSeconds.Value);
            post.SpawnedAt = now;
            post.FreezeApplied = false;
            post.Spawns++;
            if (post.Dummy) StandDummy(post);
            return true;
        }

        /// <summary>A combat dummy is a ragdoll hung from its root body, which the prefab makes kinematic; spawned by the range it came
        /// up lying flat (0.17.6 photos). Its root is held kinematic and upright at its spot, at the spawn and on every tick; each time
        /// it had to be stood up again is logged (the first three per spot) with what was wrong, so the cause shows in the log.</summary>
        static void StandDummy(Post p) {
            NetworkEntity? e = p.Entity;
            if (e == null) return;
            Rigidbody rb = e.GetComponent<Rigidbody>() ?? e.GetComponentInChildren<Rigidbody>();
            if (rb == null) return;
            Transform t = rb.transform;
            float tilt = Quaternion.Angle(t.rotation, p.Rot), moved = Vector3.Distance(t.position, p.At);
            if (rb.isKinematic && tilt < 10f && moved < 0.3f) return;
            if (p.Stood++ < 3) MelonLogger.Msg($"[Archery] TEST RANGE: {p.Lane} dummy stood up again (it was {(rb.isKinematic ? "kinematic" : "NOT kinematic")}, turned {tilt:0} degrees off its pose, {moved:0.0} m from its spot; body '{t.name}')");
            rb.isKinematic = true;
            rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            t.SetPositionAndRotation(p.At, p.Rot);
        }

        static bool Alive(Post p) => p.Exists && (p.Dummy || ((p.Agent == null || p.Agent.IsAlive) && (p.Health == null || p.Health.Health > 0f)));

        static Vector3 Where(Post p) => p.Agent != null ? p.Agent.transform.position : p.Entity!.transform.position;

        static readonly int T_Tick = Timing.Section("TestRange.Tick (creature spots)");
        static void Tick(float now) {
            if (range == null || now < nextTick) return;
            using var _t = Timing.Begin(T_Tick);
            nextTick = now + Mathf.Max(0.1f, tickSeconds.Value);
            RangeState r = range;
            if (!enabled.Value) { Remove("[TestRange] Enabled was switched off", null); return; }
            if (r.Owner != HeadlessOwner && Player.GetPlayer(r.Owner) == null) { Remove($"'{r.Owner}' left", null); return; }
            if (r.Furniture.Count > 0 && r.Furniture.All(f => f == null || f.IsBeingDestroyed)) { Remove("its shelves are gone (the area unloaded, or they were destroyed)", null); return; }
            bool isFrozen = frozen.Value;
            float respawn = respawnSeconds.Value, leash = leashMetres.Value;
            foreach (Post p in r.Posts) {
                bool exists = p.Exists, alive = Alive(p) || (exists && now - p.SpawnedAt < 1f); // a new creature's health may read 0 until it is set up
                if (alive) p.DownSince = -1f;
                else if (p.DownSince < 0f) {
                    p.DownSince = now;
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: {p.Lane} '{p.Kind}' {(exists ? "died" : "is gone")}{(respawn > 0f ? $"; back in {respawn:0.#} s" : "; respawn is off")}");
                }
                if (p.Dummy && exists) StandDummy(p);
                bool wantFrozen = isFrozen && !p.Free; // R2: a moving lane's creatures are never frozen
                if (alive && !p.Dummy && p.Agent != null && now >= p.FreezeAt && (!p.FreezeApplied || p.FrozenNow != wantFrozen)) {
                    try { p.Agent.IsEnabled = !wantFrozen; p.FrozenNow = wantFrozen; p.FreezeApplied = true; }
                    catch (Exception e) { p.FreezeApplied = true; MelonLogger.Warning($"[Archery] TEST RANGE: could not {(wantFrozen ? "freeze" : "free")} '{p.Kind}': {e.Message}"); }
                }
                float distance = exists ? Flat(Where(p), p.At) : 0f;
                bool near = NearestOtherPlayer(p.At, "") < nearMetres.Value;
                PostAction act = RespawnRule.Next(exists, alive, p.DownSince < 0f ? 0f : now - p.DownSince, respawn, leash, distance, wantFrozen || !p.FreezeApplied, near, p.Dummy);
                if (act == PostAction.Wait) continue;
                if (act == PostAction.PutBack || act == PostAction.Respawn) {
                    try { if (p.Exists) p.Entity!.SceneDestroy(); }
                    catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: removing '{p.Kind}' failed: {e.Message}"); }
                }
                // H4 (range minor 4): one spot that throws never stops the others (it is tried again after its wait)
                bool ok;
                try { ok = SpawnPost(p, now); }
                catch (Exception e) { ok = false; MelonLogger.Warning($"[Archery] TEST RANGE: {p.Lane} '{p.Kind}': the spawn threw {e.GetType().Name}: {e.Message}"); }
                if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: {p.Lane} '{p.Kind}' {(act == PostAction.PutBack ? $"strayed {distance:0.#} m and was put back" : "respawned")} at its spot{(ok ? "" : ": the spawn FAILED (tried again next tick)")} (spawn {p.Spawns})");
                if (!ok) p.DownSince = now;
            }
        }

        static readonly int T_Kill = Timing.Section("TestRange.Kill");
        /// <summary>Kill all: every live range creature's health to 0 (HealthObject.Die; they die as from a hit and respawn when
        /// respawn is on). Dummies have no health and are left standing.</summary>
        static void Kill(Captured c) {
            using var _t = Timing.Begin(T_Kill);
            if (range == null) { Refuse(c, "no test range stands"); return; }
            int killed = 0, failed = 0;
            foreach (Post p in range.Posts) {
                if (p.Dummy || !Alive(p) || p.Health == null) continue;
                try { p.Health.Die(); killed++; }
                catch (Exception e) { failed++; MelonLogger.Warning($"[Archery] TEST RANGE: could not kill '{p.Kind}': {e.Message}"); }
            }
            Status(c, true, $"{killed} test creature(s) killed{(failed > 0 ? $", {failed} could not be" : "")}{(respawnSeconds.Value > 0f ? $"; back in {respawnSeconds.Value:0.#} s" : "; respawn is off")}", failed > 0);
        }

        // live settings: what the server last applied, so a change is said once (log and window)
        static bool lastFrozen, lastEnabled;
        static float lastRespawn;

        static void RememberLive() {
            lastEnabled = enabled.Value;
            lastFrozen = frozen.Value;
            lastRespawn = respawnSeconds.Value;
            RememberPoints();
        }

        static void LiveChanged() {
            var said = new List<string>();
            if (enabled.Value != lastEnabled) {
                said.Add(enabled.Value ? "the range is switched on" : "the range is switched off");
                if (enabled.Value) { nextOnline = 0f; lastOnline = ""; }
                else { loose.Clear(); try { WriteOnline("switched off", new List<KeyValuePair<string, string?>>()); } catch { } } // H4 (range minor 3)
            }
            if (frozen.Value != lastFrozen) said.Add(frozen.Value ? "enemies are frozen" : $"enemies are free (put back past {leashMetres.Value:0.#} m)");
            if (!Mathf.Approximately(respawnSeconds.Value, lastRespawn)) said.Add(respawnSeconds.Value > 0f ? $"dead enemies come back after {respawnSeconds.Value:0.#} s" : "respawn is off");
            PointsChanged(said);
            RememberLive();
            if (said.Count == 0) return;
            string text = string.Join("; ", said.ToArray());
            MelonLogger.Warning($"[Archery] TEST RANGE: {text}{(enabled.Value ? "" : " (the range is switched off: [TestRange] Enabled = false)")}");
            WriteStatus("", enabled.Value ? "done" : "refused", enabled.Value ? text : "the range is switched off on this server ([TestRange] Enabled = false in the server's QuickshotRing.cfg)");
            if (range != null) nextTick = 0f; // frozen or free at once
            if (range != null && !enabled.Value) Remove("[TestRange] Enabled was switched off", null);
        }

        /// <summary>SelfCheck: the range's settings-derived values (its shelf and lane texts; its own entries only, a leaf). Never throws
        /// for a bad text (that is named when Build runs).</summary>
        internal static int Check() {
            if (arrowShelf == null) return 0;
            int n = ShelfEntry.ParseList(arrowShelf.Value, out _).Count + ShelfEntry.ParseList(gearShelf.Value, out _).Count;
            n += RangeLayout.Spots(RangeLayout.Parse(lanes.Value, out _), 0f, 0f, 0f).Count;
            NoonRule.Spot(shrineSpot.Value, out _, out _, out _);
            if (blueprint != null) n += Blueprint.Parse(blueprint.Value, out _).ToString().Length > 0 ? 1 : 0; // R2: the construction's roles
            return n;
        }

        /// <summary>Server: removes the whole range (the shelves and what is still on them, every creature and dummy); arrows players
        /// took are left alone. One log line, and the status file.</summary>
        static void Remove(string why, Captured? c) {
            if (range == null) {
                // R2: a build still planning (before its shelves stand) is stopped too
                if (job != null) {
                    BuildJob stopped = job;
                    job = null;
                    Status(stopped.C, false, $"the build was stopped before anything was placed ({why})");
                    if (c != null) Status(c, true, "the build in progress was stopped before anything was placed"); // last: the window shows the latest press
                    UpdateWork();
                    return;
                }
                if (c != null) Refuse(c, "no test range stands");
                return;
            }
            RangeState r = range;
            range = null;
            int n = RemoveAll(r, why);
            if (c != null) Status(c, true, $"range removed ({n} object(s))");
            else { MelonLogger.Warning($"[Archery] TEST RANGE: range removed ({why}): {n} object(s)"); WriteStatus("", "removed", $"range removed: {why}"); }
            UpdateWork();
        }

        static readonly int T_Remove = Timing.Section("TestRange.RemoveAll");
        static int RemoveAll(RangeState r, string why) {
            using var _t = Timing.Begin(T_Remove, why);
            int n = 0;
            n += EmptySlots(r);
            if (job != null && job.R == r) job = null; // R2: a build still running for this range stops
            foreach (Post p in r.Posts) {
                try { if (p.Exists) { p.Entity!.SceneDestroy(); n++; } }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: could not remove '{p.Kind}': {e.Message}"); }
            }
            // R2: the construction (lamps and upper stack levels first: the order does not matter to the game, only to the eye)
            for (int i = r.Pieces.Count - 1; i >= 0; i--) {
                NetworkEntity f = r.Pieces[i];
                try { if (f != null && !f.IsBeingDestroyed) { f.SceneDestroy(); n++; } }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: could not remove a piece: {e.Message}"); }
            }
            foreach (NetworkEntity f in r.Furniture) {
                try { if (f != null && !f.IsBeingDestroyed) { f.SceneDestroy(); n++; } }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: could not remove a shelf: {e.Message}"); }
            }
            r.Posts.Clear(); r.Pieces.Clear(); r.Furniture.Clear(); r.Slots.Clear();
            return n;
        }
    }
}
