using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ATT.Saving;
using Alta.Chunks;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // R2: the range built from the game's own pieces, on the ground (the owner, 2026-11-18: "testing your capacity on building in
    // the game"; "follow the terrain and don't spawn anything clipping in walls"; the shrine "is up on a hill in a corner with a lot
    // of workspace on the way out"). Research: .superpowers/sdd/2026-11-18-homing/R2-research.md. The layout is decided by the
    // Unity-free planner (RangeBuild.cs, tested; the same code draws docs/range-r2-plan.png through tools/range_r2_plan.py); this file
    // asks the world for it (WorldProbe: raycasts for the ground, OverlapBox for anything in the way) and spawns what it decided.
    //
    // A build is a job run by Poll over several frames: (1) AutoFace reads the ground in a fan and scores every facing, (2) the
    // layout at the chosen facing (shelves, creature spots on the ground / on pads / on stacks, backstop walls, lamps), the shelves
    // placed and stocked, (3) the pieces spawned within [TestRange] BuildFrameMs per frame, each box tested again just before it
    // spawns, (4) the creatures, (5) one Summary line per lane and a total. Every piece: network prefabs only (the game's "Proto"
    // kit is not one: R2-research section 1), SaveMode.Ignore, kept in RangeState.Pieces, destroyed by Remove, Enabled off, the
    // builder leaving and shutdown. Nothing here sends a message of its own: the game spawns the entities as it always does.
    internal static partial class TestRange {
        static MelonPreferences_Entry<bool> autoFace = null!;
        static MelonPreferences_Entry<string> blueprint = null!;
        static MelonPreferences_Entry<int> maxPieces = null!, stackLevels = null!, spotTries = null!, lampEvery = null!;
        static MelonPreferences_Entry<float> buildFrameMs = null!, creatureSpread = null!, padSpread = null!, wallBehind = null!, wallSpread = null!,
            lampHeight = null!, keepTree = null!, keepTryout = null!, fanReach = null!, slideStep = null!;

        static void InitBuild(MelonPreferences_Category cat) {
            var d = new BuildValues();
            autoFace = cat.CreateEntry("AutoFace", true, description: "Server only. On: Build tries facings round you every 15 degrees and builds toward the one with the most usable ground in reach (level, walkable ground at about your height; lane spots on real ground count most; pads count less; pillars only break ties); the log names the choice, its score and the runner-up, and the walkable metres in every direction. Off: the range faces where your head faces.");
            blueprint = cat.CreateEntry("Blueprint", Blueprint.Default, description: "Server only. Which of the game's network prefabs plays each role, 'role: prefab name' separated by '|', 'none' = not built; 'turn N' after a name turns that piece N degrees. pad: a level patch under a creature where the ground is uneven (its top at the high side's ground). tall: the top of a stack, the last resort over a drop. frame: a stack's lower levels. wall: backstops behind each lane. lamp: lights on the walls' fronts.");
            maxPieces = cat.CreateEntry("MaxPieces", d.MaxPieces, description: "Server only. At most this many pieces of construction (pads, stack pieces, walls, lamps) per build; the shelves and creatures are not counted. The build log names what the cap left out.");
            buildFrameMs = cat.CreateEntry("BuildFrameMs", 8f, description: "Server only. The build spawns its pieces over several frames, at most this many milliseconds of spawning per frame (at least one piece per frame).");
            creatureSpread = cat.CreateEntry("CreatureSpreadMetres", d.CreatureSpread, description: "Server only. A creature stands on the ground itself (no pad) where the ground under its feet (0.9 m) varies at most this much and is not steeper than MaxSlopeDegrees.");
            padSpread = cat.CreateEntry("PadSpreadMetres", d.PadSpread, description: "Server only. Where a creature cannot stand, a pad levels ground varying up to this much under it (its top at the high side's ground, its legs reaching the low side). More: the spot slides along its lane, or is skipped.");
            stackLevels = cat.CreateEntry("StackLevels", d.StackLevels, description: "Server only. The last resort for a spot over a drop (its ground more than CreatureStepMetres below yours): a stack of at most this many levels (frames under a tall piece), said in the log. 0 = never.");
            spotTries = cat.CreateEntry("SlideTries", d.SpotTries, description: "Server only. A creature spot that does not fit (uneven ground, a rock, a tree, the shrine) is tried in up to this many places, SlideStepMetres apart, back and forward along its lane and sideways (walls and shelves: 9 places).");
            slideStep = cat.CreateEntry("SlideStepMetres", d.SlideStep, description: "Server only. The step of those slides (shelves slide half as far).");
            wallBehind = cat.CreateEntry("WallBehindMetres", d.WallBehind, description: "Server only. A backstop wall stands this far behind its lane's farthest spot (a lane with 'nowall' has none). A wall that would stand in the line from you to another lane's target is left out.");
            wallSpread = cat.CreateEntry("WallSpreadMetres", d.WallSpread, description: "Server only. A wall's ground may vary this much along it (its base sinks into the high side); more: it slides back or sideways, or is left out.");
            lampHeight = cat.CreateEntry("LampHeightMetres", d.LampHeight, description: "Server only. Lamps hang on the walls' fronts this high above the wall's base.");
            lampEvery = cat.CreateEntry("LampEvery", d.LampEvery, description: "Server only. A lamp on the middle wall of each backstop, then on every this-many-th wall outward (1 = every wall).");
            keepTree = cat.CreateEntry("KeepTreeMetres", d.KeepTree, description: "Server only. Nothing of the range stands within this many metres of the archery tree (the orbs, the purchase pedestal, the tablet's cradle).");
            keepTryout = cat.CreateEntry("KeepTryoutMetres", d.KeepTryout, description: "Server only. Nothing of the range stands within this many metres of the shrine's try-out area.");
            fanReach = cat.CreateEntry("FanReachMetres", d.FanReach, description: "Server only. AutoFace reads the ground this far out in every direction (a cast every 2 m).");
        }

        static BuildValues Values() {
            var v = new BuildValues {
                MaxSlope = maxSlope.Value, Window = creatureStep.Value, ShelfWindow = maxStep.Value, ShelfSide = shelfMetres.Value,
                MaxPieces = Math.Max(0, maxPieces.Value), CreatureSpread = Mathf.Max(0f, creatureSpread.Value), PadSpread = Mathf.Max(0f, padSpread.Value),
                StackLevels = Math.Max(0, stackLevels.Value), SpotTries = Mathf.Clamp(spotTries.Value, 1, 100), SlideStep = Mathf.Clamp(slideStep.Value, 0.1f, 10f),
                WallBehind = wallBehind.Value, WallSpread = Mathf.Max(0f, wallSpread.Value), LampHeight = lampHeight.Value, LampEvery = Math.Max(1, lampEvery.Value),
                KeepTree = Mathf.Max(0f, keepTree.Value), KeepTryout = Mathf.Max(0f, keepTryout.Value), FanReach = Mathf.Clamp(fanReach.Value, 4f, 120f),
            };
            return v;
        }

        // ---------------------------------------------------------------- pieces: the prefab and its shape, measured once

        sealed class PieceKind {
            public string Name = "";
            public uint Hash;
            public NetworkPrefab Prefab = null!;
            public PieceShape Shape;
            public string From = "";
        }

        static readonly Dictionary<string, PieceKind?> pieceKinds = new Dictionary<string, PieceKind?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The prefab of this name and its shape (once per name; a miss is kept too).</summary>
        static PieceKind? KindOf(string name, string role) {
            if (string.IsNullOrEmpty(name)) return null;
            if (pieceKinds.TryGetValue(name, out PieceKind? k)) return k;
            uint hash = Resolve(name, 0u);
            NetworkPrefab? prefab = hash != 0u ? PrefabOf(hash, name) : null;
            if (prefab == null) {
                pieceKinds[name] = null;
                MelonLogger.Warning($"[Archery] TEST RANGE: [TestRange] Blueprint names '{name}' for the {role}, but this game has no network prefab of that name: no {role} is built");
                return null;
            }
            k = new PieceKind { Name = prefab.name, Hash = hash, Prefab = prefab };
            k.Shape = Measure(prefab.gameObject, out k.From);
            pieceKinds[name] = k;
            if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: piece '{prefab.name}' ({hash}) as the {role}: {k.Shape} ({k.From})");
            return k;
        }

        static bool SolidLayer(int layer) => layer == 0 || layer == 8 || layer == 10 || layer == 11 || layer == 13 || layer == 16 || layer == 19;

        static bool ActiveUnder(Transform t, Transform root) {
            for (Transform? x = t; x != null; x = x.parent) {
                if (!x.gameObject.activeSelf) return false;
                if (x == root) return true;
            }
            return true;
        }

        static bool LocalBox(Collider c, out Vector3 centre, out Vector3 extents) {
            centre = Vector3.zero; extents = Vector3.zero;
            switch (c) {
                case BoxCollider b: centre = b.center; extents = b.size * 0.5f; return true;
                case SphereCollider s: centre = s.center; extents = Vector3.one * s.radius; return true;
                case CapsuleCollider k: {
                    centre = k.center; extents = Vector3.one * k.radius;
                    float h = Mathf.Max(k.radius, k.height * 0.5f);
                    if (k.direction == 0) extents.x = h; else if (k.direction == 1) extents.y = h; else extents.z = h;
                    return true;
                }
                case MeshCollider m:
                    if (m.sharedMesh == null) return false;
                    centre = m.sharedMesh.bounds.center; extents = m.sharedMesh.bounds.extents;
                    return true;
                default: return false;
            }
        }

        static readonly int T_Measure = Timing.Section("TestRange.Measure (a piece's shape from its colliders)");
        /// <summary>A piece's solid box in its own space, from its enabled, active, non-trigger colliders' own definitions (never
        /// Collider.bounds: a prefab asset is not in the physics scene, and a just-spawned entity's bounds are stale until the physics
        /// transforms sync, which is what gave the first build's "footprint 330 x 202 m"). Solid layers first; with none, any
        /// non-trigger collider (a shelf has only its docks on the server). The walkable top: the highest Ground-layer (10) collider.</summary>
        static PieceShape Measure(GameObject root, out string from) {
            using var _t = Timing.Begin(T_Measure, root != null ? root.name : "?");
            from = "no solid collider";
            if (root == null) return default;
            Transform rt = root.transform;
            Matrix4x4 toRoot = rt.worldToLocalMatrix;
            Vector3 scale = rt.localScale;
            Collider[] cs = root.GetComponentsInChildren<Collider>(true);
            for (int pass = 0; pass < 2; pass++) {
                Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                float top = float.NaN;
                int n = 0;
                foreach (Collider c in cs) {
                    if (c == null || c.isTrigger || !c.enabled || !ActiveUnder(c.transform, rt)) continue;
                    int layer = c.gameObject.layer;
                    if (pass == 0 && !SolidLayer(layer)) continue;
                    if (!LocalBox(c, out Vector3 cen, out Vector3 ext)) continue;
                    Matrix4x4 m = toRoot * c.transform.localToWorldMatrix;
                    float hi = float.NegativeInfinity;
                    for (int i = 0; i < 8; i++) {
                        var corner = new Vector3((i & 1) == 0 ? -ext.x : ext.x, (i & 2) == 0 ? -ext.y : ext.y, (i & 4) == 0 ? -ext.z : ext.z);
                        Vector3 p = Vector3.Scale(m.MultiplyPoint3x4(cen + corner), scale);
                        mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                        hi = Mathf.Max(hi, p.y);
                    }
                    if (layer == 10) top = float.IsNaN(top) ? hi : Mathf.Max(top, hi);
                    n++;
                }
                if (n == 0) continue;
                from = pass == 0 ? $"measured from {n} solid collider(s)" : $"measured from {n} non-solid collider(s): {(cs.Any(c => c != null && c is SphereCollider && c.gameObject.layer == 15) ? "its docks" : "no solid one")}";
                return PieceShape.Box(mn.x, mn.y, mn.z, mx.x, mx.y, mx.z, top);
            }
            return default;
        }

        // ---------------------------------------------------------------- the world as the planner asks it

        /// <summary>The server's answers to the planner: the ground by raycast (the highest surface, never a loose item: a dynamic
        /// Rigidbody), anything in a box by OverlapBox (loose items roll aside; players always count, and another player's head
        /// within PlayerClearMetres). Counts its casts for the cost line.</summary>
        sealed class WorldProbe : IRangeProbe {
            // Default, Terrain, Ground, GroundOverlay, Wall, TerrainWallOnly
            internal const int GroundMask = (1 << 0) | (1 << 8) | (1 << 10) | (1 << 11) | (1 << 13) | (1 << 19);
            // and Player, BlockTeleport (creature shells), CharacterPart, PlayerPersonalSpace
            internal const int BlockMask = GroundMask | (1 << 9) | (1 << 16) | (1 << 24) | (1 << 30);
            public int Casts, Boxes;
            public float PlayerClear;
            public string Builder = "";
            public Vector3? BuilderAt;         // where the builder stood at the press (their own body never blocks a piece)
            public NetworkEntity? Ignore;      // the host of the piece being checked at spawn (its wall, its pad, the level under it)
            readonly RaycastHit[] hits = new RaycastHit[48];
            readonly Collider[] cols = new Collider[96];
            bool warnedFull;

            public bool Ground(float x, float z, float top, float bottom, out float y, out float slope) {
                Casts++;
                y = float.NaN; slope = 0f;
                int n = Physics.RaycastNonAlloc(new Vector3(x, top + 0.5f, z), Vector3.down, hits, top - bottom + 0.5f, GroundMask, QueryTriggerInteraction.Ignore);
                if (n == hits.Length && !warnedFull) { warnedFull = true; MelonLogger.Warning($"[Archery] TEST RANGE: a ground cast at ({x:0.0}, {z:0.0}) filled its {hits.Length} hits (said once); the highest of those is taken"); }
                for (int i = 0; i < n; i++) {
                    Collider col = hits[i].collider;
                    if (col == null) continue;
                    Rigidbody rb = col.attachedRigidbody;
                    if (rb != null && !rb.isKinematic) continue;   // a loose item, a dummy's swinging body: not ground
                    if (float.IsNaN(y) || hits[i].point.y > y) { y = hits[i].point.y; slope = Vector3.Angle(hits[i].normal, Vector3.up); }
                }
                return !float.IsNaN(y) && y <= top && y >= bottom;
            }

            public string? Blocked(float cx, float cy, float cz, float hx, float hy, float hz, float yaw) {
                Boxes++;
                var centre = new Vector3(cx, cy, cz);
                int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(hx, hy, hz), cols, Quaternion.Euler(0f, yaw, 0f), BlockMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) {
                    Collider c = cols[i];
                    if (c == null) continue;
                    int layer = c.gameObject.layer;
                    bool player = layer == 9 || layer == 24 || layer == 30;
                    Rigidbody rb = c.attachedRigidbody;
                    if (!player && rb != null && !rb.isKinematic) continue;      // a loose item rolls out of the way
                    // the builder's own body: the shelves stand a step beside them by design (they step aside); never refused for that
                    if (player && BuilderAt.HasValue) {
                        Vector3 b = c.bounds.center;
                        float dx = b.x - BuilderAt.Value.x, dz = b.z - BuilderAt.Value.z;
                        if (dx * dx + dz * dz < 1.5f * 1.5f) continue;
                    }
                    if (Ignore != null) {
                        NetworkEntity e = c.GetComponentInParent<NetworkEntity>();
                        if (e != null && (e == Ignore || e.PrefabRoot == Ignore)) continue;
                    }
                    return $"'{c.name}' (layer {layer})";
                }
                if (PlayerClear > 0f)
                    foreach (IPlayer ip in Player.AllPlayers) {
                        if (!(ip is Player p) || p.PlayerController == null || p.PlayerController.Head == null) continue;
                        if (string.Equals(Builds.NameOf(p), Builder, StringComparison.OrdinalIgnoreCase)) continue;
                        Vector3 h = p.PlayerController.Head.position;
                        float dx = h.x - cx, dz = h.z - cz;
                        if (Mathf.Sqrt(dx * dx + dz * dz) < Mathf.Max(hx, hz) + PlayerClear && h.y > cy - hy - 2.5f && h.y < cy + hy + 2.5f) return $"player '{Builds.NameOf(p)}' standing there";
                    }
                return null;
            }
        }

        // ---------------------------------------------------------------- the build job

        sealed class BuildJob {
            public Captured C = null!;
            public string Owner = "";
            public Vector3 Origin;
            public float PlayerYaw, ShrineDistance;
            public PlanInput Input = null!;
            public PlanResult? Plan;
            public WorldProbe Probe = null!;
            public RangeState? R;
            public int Stage, Next, Built, SkippedAtSpawn, Creatures;
            public readonly Dictionary<int, NetworkEntity> Spawned = new Dictionary<int, NetworkEntity>();
            public readonly List<string> Log = new List<string>();
            public readonly Dictionary<PieceRole, PieceKind> Kinds = new Dictionary<PieceRole, PieceKind>();
            public double FaceMs, LayoutMs, ShelvesMs, WorstFrameMs;
            public int Frames;
            public float Started;
        }

        static BuildJob? job;

        static string Bearing(float yaw) {
            string[] names = { "north", "NNE", "NE", "ENE", "east", "ESE", "SE", "SSE", "south", "SSW", "SW", "WSW", "west", "WNW", "NW", "NNW" };
            float d = yaw % 360f; if (d < 0f) d += 360f;
            return names[(int)Math.Round(d / 22.5f) % 16];
        }

        static string Deg(float yaw) => FaceRule.Norm(yaw).ToString("0", CultureInfo.InvariantCulture);

        /// <summary>Build, after its gates (TestRange.Build): the job that plans and builds over the next frames.</summary>
        static void StartBuild(Captured c, string owner, Vector3 origin, float playerYaw, float shrineDistance, SkillShrine? shrine) {
            var input = new PlanInput {
                X = origin.x, Y = origin.y, Z = origin.z, PlayerYaw = playerYaw, AutoFace = autoFace.Value && !NoonRule.Spot(buildSpot.Value, out _, out _, out _), // BuildSpot fixes the facing
                Lanes = RangeLayout.Parse(lanes.Value, out List<string> laneErrors),
                Pieces = Blueprint.Parse(blueprint.Value, out List<string> bpErrors), Values = Values(),
            };
            var j = new BuildJob { C = c, Owner = owner, Origin = origin, PlayerYaw = playerYaw, ShrineDistance = shrineDistance, Input = input, Started = Time.time,
                Probe = new WorldProbe { PlayerClear = playerClear.Value, Builder = owner, BuilderAt = origin } };
            foreach (string e in laneErrors) j.Log.Add($"skipped {e}");
            foreach (string e in bpErrors) j.Log.Add($"[TestRange] Blueprint: {e}");
            // the pieces and their shapes (measured from the prefabs once)
            foreach (PieceRole role in new[] { PieceRole.Pad, PieceRole.Tall, PieceRole.Frame, PieceRole.Wall, PieceRole.Lamp }) {
                PieceKind? k = KindOf(input.Pieces.NameOf(role), role.ToString().ToLowerInvariant());
                if (k == null) continue;
                PieceShape s = k.Shape;
                if (!s.Known && role == PieceRole.Lamp) { s = PieceShape.Box(-0.15f, -0.3f, 0f, 0.15f, 0.05f, 0.3f); k.From = "assumed: its visual is client-side static content"; k.Shape = s; }
                if (!s.Known) { j.Log.Add($"the {role.ToString().ToLowerInvariant()} '{k.Name}' has no collider to measure: none is built"); continue; }
                if ((role == PieceRole.Pad || role == PieceRole.Tall) && float.IsNaN(s.Top)) { j.Log.Add($"the {role.ToString().ToLowerInvariant()} '{k.Name}' has no walkable top (no Ground-layer collider): none is built"); continue; }
                input.Shapes[role] = s;
                j.Kinds[role] = k;
            }
            NetworkPrefab? arrow = PrefabOf((uint)Mathf.Max(0, arrowShelfPrefab.Value), "arrow shelf"), gear = PrefabOf((uint)Mathf.Max(0, gearShelfPrefab.Value), "gear shelf");
            if (arrow != null) input.ArrowShelf = Measure(arrow.gameObject, out _);
            if (gear != null) input.GearShelf = Measure(gear.gameObject, out _);
            input.GearShelfWanted = ShelfEntry.ParseList(gearShelf.Value, out _).Count > 0;
            // the shrine's keep-clear zone, from the live shrine
            if (shrine != null) {
                if (shrine.SkillTreeTarget != null && input.Values.KeepTree > 0f)
                    input.Keep.Add(new KeepCircle { X = shrine.SkillTreeTarget.position.x, Z = shrine.SkillTreeTarget.position.z, R = input.Values.KeepTree, What = "the archery tree" });
                if (shrine.TestingArea != null && input.Values.KeepTryout > 0f)
                    input.Keep.Add(new KeepCircle { X = shrine.TestingArea.transform.position.x, Z = shrine.TestingArea.transform.position.z, R = input.Values.KeepTryout, What = "the shrine's try-out area" });
                if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: keep-clear: {string.Join("; ", input.Keep.Select(k => $"{k.What} at ({k.X:0.0}, {k.Z:0.0}), {k.R:0.#} m").ToArray())}");
            }
            job = j;
            UpdateWork();
        }

        static readonly int T_Job = Timing.Section("TestRange.TickJob (building)");
        static readonly int T_Face = Timing.Section("TestRange.TickJob AutoFace (fan and facings)");
        static readonly int T_Layout = Timing.Section("TestRange.TickJob layout");
        static readonly int T_Piece = Timing.Section("TestRange.SpawnPiece");

        /// <summary>Poll, every frame while a build runs.</summary>
        static void TickJob(float now) {
            BuildJob? j = job;
            if (j == null) return;
            using var _t = Timing.Begin(T_Job);
            var sw = Stopwatch.StartNew();
            try {
                if (j.Stage >= 2 && (j.R == null || range != j.R)) { MelonLogger.Warning($"[Archery] TEST RANGE: the build stopped: the range was removed while it was being built ({j.Built} piece(s) were up)"); job = null; return; }
                switch (j.Stage) {
                    case 0: FacePhase(j); break;
                    case 1: LayoutPhase(j, now); break;
                    case 2: PiecesPhase(j, now); break;
                    case 3: CreaturesPhase(j, now); break;
                    default: Finish(j); break;
                }
            }
            catch (Exception e) {
                MelonLogger.Error($"[Archery] TEST RANGE: the build failed in stage {j.Stage}: {e}");
                if (j.R != null && range == j.R) Remove("the build failed", null);
                Status(j.C, false, $"the build failed inside the server ({e.GetType().Name}: {e.Message}); everything it had placed was removed");
                job = null;
            }
            finally {
                double ms = sw.Elapsed.TotalMilliseconds;
                j.Frames++;
                if (ms > j.WorstFrameMs) j.WorstFrameMs = ms;
                UpdateWork();
            }
        }

        static void FacePhase(BuildJob j) {
            using var _t = Timing.Begin(T_Face);
            var sw = Stopwatch.StartNew();
            j.Plan = RangePlanner.Face(j.Probe, j.Input);
            j.FaceMs = sw.Elapsed.TotalMilliseconds;
            PlanResult p = j.Plan;
            BuildValues v = j.Input.Values;
            if (Logs.Summary) {
                var fan = new List<string>();
                int best = 0;
                for (int i = 0; i < p.Rays.Length; i++) { fan.Add($"{(i * 360 / p.Rays.Length)}:{p.Rays[i]:0}"); if (p.Rays[i] > p.Rays[best]) best = i; }
                MelonLogger.Msg($"[Archery] TEST RANGE: the ground round you, walkable metres in reach by direction (degrees clockwise from north): {string.Join(" ", fan.ToArray())}; the most open way: {best * 360 / Math.Max(1, p.Rays.Length)} degrees ({Bearing(best * 360f / Math.Max(1, p.Rays.Length))}), {p.Rays[best]:0} m");
            }
            if (j.Input.AutoFace && p.Best >= 0) {
                if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: AutoFace: facing {Deg(p.Yaw)} degrees ({Bearing(p.Yaw)}; you faced {Deg(j.PlayerYaw)}): {FaceRule.Describe(p.Faces[p.Best], v)}{(p.Runner >= 0 ? $"; runner-up {FaceRule.Describe(p.Faces[p.Runner], v)}" : "")}");
                if (Logs.Debug) foreach (FaceCandidate f in p.Faces) MelonLogger.Msg($"[Archery] TEST RANGE:   facing {FaceRule.Describe(f, v)}");
            }
            else if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: AutoFace is off: facing where you face, {Deg(p.Yaw)} degrees ({Bearing(p.Yaw)})");
            j.Stage = 1;
        }

        static void LayoutPhase(BuildJob j, float now) {
            PlanResult p = j.Plan!;
            using (Timing.Begin(T_Layout)) {
                var sw = Stopwatch.StartNew();
                RangePlanner.Layout(j.Probe, j.Input, p);
                j.LayoutMs = sw.Elapsed.TotalMilliseconds;
            }
            if (p.Refusal != null) { job = null; Refuse(j.C, p.Refusal); return; }
            if (range != null) { job = null; Refuse(j.C, $"a test range already stands (built by '{range.Owner}'); press Remove first"); return; }
            // the shelves: the range is recorded first (H4, range I-4), a throw while placing removes everything and refuses
            var r = new RangeState { Owner = j.Owner, Origin = j.Origin, Yaw = p.Yaw, Built = now };
            range = r;
            j.R = r;
            var sw2 = Stopwatch.StartNew();
            string? shelfWhy;
            try { shelfWhy = PlaceShelves(r, p.Pieces.Where(x => x.Role == PieceRole.Shelf).ToList(), j.Log); }
            catch (Exception e) {
                MelonLogger.Error($"[Archery] TEST RANGE: placing the shelves threw: {e}");
                shelfWhy = $"placing the shelves failed inside the server ({e.GetType().Name}: {e.Message}); everything already placed was removed";
            }
            j.ShelvesMs = sw2.Elapsed.TotalMilliseconds;
            if (shelfWhy != null) {
                RemoveAll(r, "the build was refused");
                range = null;
                job = null;
                Refuse(j.C, shelfWhy);
                return;
            }
            j.Stage = 2;
            j.Next = 0;
        }

        static bool OverBudget(Stopwatch sw, int done) => done > 0 && sw.Elapsed.TotalMilliseconds >= Mathf.Max(0.5f, buildFrameMs.Value);

        static void PiecesPhase(BuildJob j, float now) {
            PlanResult p = j.Plan!;
            RangeState r = j.R!;
            var sw = Stopwatch.StartNew();
            int done = 0;
            bool synced = false;
            for (; j.Next < p.Pieces.Count && !OverBudget(sw, done); j.Next++) {
                Placed piece = p.Pieces[j.Next];
                if (piece.Role == PieceRole.Shelf || piece.Role == PieceRole.Creature) continue;
                if (!synced) { Physics.SyncTransforms(); synced = true; } // the pieces of earlier frames where they are, for the box tests
                SpawnPiece(j, r, piece, j.Next);
                done++;
            }
            if (j.Next >= p.Pieces.Count) { j.Stage = 3; j.Next = 0; }
        }

        /// <summary>One piece of construction: its box tested again (something may have moved there since the plan; its host is not
        /// counted), then spawned never-saved and kept in the range record.</summary>
        static void SpawnPiece(BuildJob j, RangeState r, Placed piece, int index) {
            using var _t = Timing.Begin(T_Piece, piece.Label);
            if (!j.Kinds.TryGetValue(piece.Role, out PieceKind k)) return;
            NetworkEntity? host = null;
            if (piece.Host >= 0 && (!j.Spawned.TryGetValue(piece.Host, out host) || host == null || host.IsBeingDestroyed)) {
                j.SkippedAtSpawn++;
                j.Log.Add($"{piece.Label}: not built (what it stands on or hangs from was not built)");
                return;
            }
            j.Probe.Ignore = host;
            string? hit;
            try { hit = j.Probe.Blocked(piece.Box.Cx, piece.Box.CentreY, piece.Box.Cz, piece.Box.Hx, piece.Box.HalfY, piece.Box.Hz, piece.Box.Yaw); }
            finally { j.Probe.Ignore = null; }
            if (hit != null) { j.SkippedAtSpawn++; j.Log.Add($"{piece.Label}: not built: it would now hit {hit} (it was clear when planned)"); return; }
            var pos = new Vector3(piece.X, piece.Y, piece.Z);
            LocationChunk chunk = LocationChunkHelper.GetBestContaining(pos);
            if (chunk == null) { j.SkippedAtSpawn++; j.Log.Add($"{piece.Label}: not built (no loaded chunk at {Targets.P(pos)})"); return; }
            NetworkEntity e;
            try {
                e = SpawnHelper.Spawn(k.Prefab, SpawnData.DontGenerate, chunk, pos, Quaternion.Euler(0f, piece.Yaw, 0f),
                    x => { if (x != null && x.Prefab != null) x.Prefab.SaveMode = SaveMode.Ignore; }); // never in the world save
            }
            catch (Exception ex) { j.SkippedAtSpawn++; j.Log.Add($"{piece.Label}: the spawn threw {ex.GetType().Name}: {ex.Message}"); return; }
            if (e == null) { j.SkippedAtSpawn++; j.Log.Add($"{piece.Label}: the game spawned nothing for '{k.Name}'"); return; }
            r.Pieces.Add(e);
            j.Spawned[index] = e;
            j.Built++;
            if (Logs.Debug) MelonLogger.Msg($"[Archery] TEST RANGE: built {piece.Label}: '{k.Name}' at {Targets.P(pos)} turned {Deg(piece.Yaw)}{(piece.Note.Length > 0 ? $" ({piece.Note})" : "")}");
        }

        static void CreaturesPhase(BuildJob j, float now) {
            PlanResult p = j.Plan!;
            RangeState r = j.R!;
            var sw = Stopwatch.StartNew();
            int done = 0;
            bool synced = false;
            for (; j.Next < p.Pieces.Count && !OverBudget(sw, done); j.Next++) {
                Placed t = p.Pieces[j.Next];
                if (t.Role != PieceRole.Creature) continue;
                if (!synced) { Physics.SyncTransforms(); synced = true; }
                PlaceCreature(j, r, t, now);
                done++;
            }
            if (j.Next >= p.Pieces.Count) j.Stage = 4;
        }

        static void PlaceCreature(BuildJob j, RangeState r, Placed t, float now) {
            uint hash = Resolve(t.Kind, RangeKinds.CreatureHash(t.Kind));
            if (hash == 0u || PrefabOf(hash, t.Kind) == null) { j.Log.Add($"{t.Label}: skipped (no prefab of that name)"); return; }
            NetworkEntity? host = null;
            if (t.Host >= 0 && (!j.Spawned.TryGetValue(t.Host, out host) || host == null)) { j.Log.Add($"{t.Label}: skipped (its {(t.LastResort ? "stack" : "pad")} was not built)"); return; }
            var at = new Vector3(t.X, t.Y, t.Z);
            if (host != null) {
                // the creature goes on its pad's top: check that a cast down there finds the pad, not the ground below
                if (Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 2.0f, WorldProbe.GroundMask, QueryTriggerInteraction.Ignore)) {
                    NetworkEntity under = hit.collider.GetComponentInParent<NetworkEntity>();
                    bool onPad = under != null && (under == host || under.PrefabRoot == host);
                    if (!onPad || Mathf.Abs(hit.point.y - at.y) > 0.25f)
                        MelonLogger.Warning($"[Archery] TEST RANGE: {t.Label}: the cast down at its spot hit '{hit.collider.name}' at {hit.point.y:0.00} (its {(t.LastResort ? "stack" : "pad")}'s top was planned at {at.y:0.00}); it is spawned at the planned height");
                    else if (Logs.Debug) MelonLogger.Msg($"[Archery] TEST RANGE: {t.Label}: stands on its {(t.LastResort ? "stack" : "pad")} ('{hit.collider.name}' at {hit.point.y:0.00})");
                }
                else MelonLogger.Warning($"[Archery] TEST RANGE: {t.Label}: no cast hit under its spot {Targets.P(at)}; spawned at the planned height");
            }
            var post = new Post { Lane = t.Lane, Kind = t.Kind, Hash = hash, At = at, Facing = Quaternion.Euler(0f, t.Yaw, 0f), Dummy = t.Dummy, Free = t.Free, Stand = t.Note };
            r.Posts.Add(post);
            j.Probe.Ignore = host;
            string? blocked;
            try { blocked = j.Probe.Blocked(t.Box.Cx, t.Box.CentreY, t.Box.Cz, t.Box.Hx, t.Box.HalfY, t.Box.Hz, t.Box.Yaw); }
            finally { j.Probe.Ignore = null; }
            if (blocked != null) { post.DownSince = now; j.Log.Add($"{t.Label}: not spawned yet: {blocked} is in its place (the respawn rule tries again)"); return; }
            bool spawned;
            try { spawned = SpawnPost(post, now); }
            catch (Exception e) { spawned = false; MelonLogger.Warning($"[Archery] TEST RANGE: {t.Label}: the spawn threw {e.GetType().Name}: {e.Message}"); }
            if (spawned) j.Creatures++;
            else post.DownSince = now;
        }

        static void Finish(BuildJob j) {
            job = null;
            RangeState r = j.R!;
            PlanResult p = j.Plan!;
            nextTick = Time.time + Mathf.Max(0.1f, tickSeconds.Value);
            List<RangePiece> spots = RangeLayout.Spots(j.Input.Lanes, j.Origin.x, j.Origin.z, p.Yaw);
            // R2: on this shelf of ground six lanes cannot all keep 15 m apart (the default near lanes stand 4-10 m from each other):
            // one line, not one warning per pair
            List<string> close = RangeLayout.TooClose(spots, laneGap.Value);
            if (close.Count > 0 && Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: {close.Count} pair(s) of spots of different lanes stand closer than LaneGapMetres ({laneGap.Value:0} m: a Ricochet jump, a Shatter blast or a lock in one lane can reach the other); for example {close[0]}");
            var byRole = new Dictionary<PieceRole, int>();
            foreach (KeyValuePair<int, NetworkEntity> kv in j.Spawned) { PieceRole role = p.Pieces[kv.Key].Role; byRole[role] = (byRole.TryGetValue(role, out int n) ? n : 0) + 1; }
            int Count(PieceRole role) => byRole.TryGetValue(role, out int n) ? n : 0;
            int up = r.Posts.Count(x => x.Exists);
            MelonLogger.Warning($"[Archery] TEST RANGE: built for '{j.Owner}' at {Targets.P(j.Origin)} facing {Deg(p.Yaw)} degrees ({Bearing(p.Yaw)}), {j.ShrineDistance:0} m from the archery shrine: {r.Furniture.Count} shelf/shelves ({r.Slots.Count} slots), {j.Built} pieces ({Count(PieceRole.Wall)} walls, {Count(PieceRole.Lamp)} lamps, {Count(PieceRole.Pad)} pads, {Count(PieceRole.Tall) + Count(PieceRole.Frame)} stack pieces), {up} creatures/dummies; plan {j.FaceMs + j.LayoutMs:0} ms ({j.Probe.Casts} casts, {j.Probe.Boxes} box tests), {j.Frames} frames, the longest {j.WorstFrameMs:0.0} ms");
            foreach (string n in p.Notes) MelonLogger.Warning($"[Archery] TEST RANGE: {n}");
            if (p.CapHit > 0) MelonLogger.Warning($"[Archery] TEST RANGE: the piece cap ([TestRange] MaxPieces {j.Input.Values.MaxPieces}) left out {p.CapHit} piece(s)");
            if (Logs.Summary) {
                foreach (Placed s in p.Pieces.Where(x => x.Role == PieceRole.Shelf))
                    MelonLogger.Msg($"[Archery] TEST RANGE: {s.Label} at {Targets.P(new Vector3(s.X, s.Y, s.Z))} turned {Deg(s.Yaw)}{(s.Note.Length > 0 ? $" ({s.Note})" : "")}");
                foreach (LaneReport l in p.Lanes) {
                    Lane? lane = j.Input.Lanes.FirstOrDefault(x => x.Name == l.Lane);
                    int built = p.Pieces.Select((x, i) => (x, i)).Count(xi => xi.x.Lane == l.Lane && xi.x.Role != PieceRole.Creature && j.Spawned.ContainsKey(xi.i));
                    int creatures = r.Posts.Count(x => x.Lane == l.Lane && x.Exists);
                    MelonLogger.Msg($"[Archery] TEST RANGE: lane '{l.Lane}'{(lane != null ? $" @{lane.Bearing:0} (world {Deg(p.Yaw + lane.Bearing)}){(lane.Free ? ", moving" : "")}" : "")}: {creatures} of {l.Spots} spots up ({l.OnGround} on the ground, {l.OnPads} on pads, {l.Stacks} on stacks, {l.Skipped} skipped, {l.Moved} moved), backstop {l.Walls} of {l.WallsWanted} walls, {l.Lamps} lamp(s); {built} piece(s) built");
                }
                foreach (Placed t in p.Pieces.Where(x => x.Role == PieceRole.Creature))
                    MelonLogger.Msg($"[Archery] TEST RANGE:   {t.Label} at {Targets.P(new Vector3(t.X, t.Y, t.Z))}: {t.Note}");
                foreach (string s in p.Skipped) MelonLogger.Msg($"[Archery] TEST RANGE:   left out: {s}");
                foreach (string s in j.Log) MelonLogger.Msg($"[Archery] TEST RANGE:   {s}");
            }
            LogSlots(r);
            int skipped = p.Skipped.Count + j.SkippedAtSpawn + j.Log.Count(l => l.Contains("skipped") || l.Contains("not spawned") || l.Contains("EMPTY") || l.Contains("failed"));
            int empty = r.Slots.Count(s => s.Failed.Length > 0);
            Status(j.C, true, $"range built facing {Deg(p.Yaw)} degrees ({Bearing(p.Yaw)}{(j.Input.AutoFace ? ", AutoFace" : "")}): {up} creatures/dummies, {j.Built} pieces ({Count(PieceRole.Wall)} walls, {Count(PieceRole.Lamp)} lamps, {Count(PieceRole.Pad)} pads{(Count(PieceRole.Tall) > 0 ? $", {Count(PieceRole.Tall)} stacks" : "")}), {r.Slots.Count(s => s.Holds.Length > 0)} of {r.Slots.Count} shelf slots stocked{(skipped + empty > 0 ? $"; {skipped} thing(s) left out or skipped, {empty} slot(s) EMPTY{(empty > 0 ? ": " + string.Join("; ", r.Slots.Where(s => s.Failed.Length > 0).Select(s => $"{s.Want}: {s.Failed}").ToArray()) : "")} (server log: 'TEST RANGE:')" : "")}", skipped + empty > 0);
        }
    }
}
