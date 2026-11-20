using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ATT.Saving;
using Alta.Intelligence;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // The range's clean-up (TestRange.cs has the overview): leftovers lying round the range on a play-test server.
    //   SurveyRequest: logs every SAVED network entity within CleanMetres of the range (or ShrineSpot without one), grouped by name,
    //                  with what each is (loose item, docked/held item, creature, other) and the nearest one's distance. Changes nothing.
    //   CleanRequest:  destroys, within CleanMetres, every SAVED loose item (a pickup neither docked nor held) whose name is not in
    //                  CleanKeep, plus every saved entity whose name is in CleanNames (e.g. a boulder someone spawned), never a player,
    //                  a living creature, or anything docked or held (a worn quiver, a bag's contents, a hand's item).
    // Only SAVED entities are ever looked at: the range's own pieces, its stock and every test creature are SaveMode.Ignore. Server only.
    internal static partial class TestRange {
        static MelonPreferences_Entry<float> cleanMetres = null!;
        static MelonPreferences_Entry<string> cleanNames = null!, cleanKeep = null!, buildSpot = null!, cleanFallen = null!;
        static MelonPreferences_Entry<float> fallenDegrees = null!;
        static readonly int T_Clean = Timing.Section("TestRange.Survey/Clean (scene search, on request)");

        static void InitClean(MelonPreferences_Category cat, string req) {
            buildSpot = cat.CreateEntry("BuildSpot", "", description: "Server only. Empty: Build stands where [TestBuild] Player stands and faces (AutoFace may turn it). 'x, z, yaw' (yaw in degrees clockwise from north): Build always builds there, facing that way, AutoFace skipped, so the range comes out the same every time (still only within ShrineMetres of the shrine).");
            cleanMetres = cat.CreateEntry("CleanMetres", 40f, description: "Server only. SurveyRequest and CleanRequest look this far from the range's origin (or ShrineSpot when no range stands).");
            cleanNames = cat.CreateEntry("CleanNames", "", description: "Server only. Names (';' between) of saved things CleanRequest also removes within CleanMetres whatever they are (not players or living creatures), e.g. a boulder; the name as SurveyRequest logs it.");
            cleanKeep = cat.CreateEntry("CleanKeep", "combat_dummy", description: "Server only. Names (';' between) of saved loose items CleanRequest never removes.");
            cleanFallen = cat.CreateEntry("CleanFallen", "combat_dummy; Fake Player combat_dummy", description: "Server only. Names (';' between) of saved things CleanRequest removes within CleanMetres only when they have FALLEN OVER (tilted more than FallenDegrees from how their prefab stands); standing ones stay. SurveyRequest logs each one's place and tilt.");
            fallenDegrees = cat.CreateEntry("FallenDegrees", 30f, description: "Server only. A thing named in CleanFallen counts as fallen over when its up direction is tilted more than this from how its prefab stands.");
            Add(cat, "SurveyRequest", "survey", req + "Logs every saved thing within CleanMetres of the range by name and kind (server log 'TEST RANGE: survey'); changes nothing.", c => Clean(c, false));
            Add(cat, "CleanRequest", "clean up", req + "Removes saved loose items within CleanMetres (except CleanKeep) and every saved thing named in CleanNames; never players, living creatures, or anything docked or held.", c => Clean(c, true));
        }

        static string BareName(GameObject g) {
            string n = g.name;
            int dash = n.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && n.Substring(0, dash).All(char.IsDigit)) n = n.Substring(dash + 3);
            if (n.EndsWith("(Clone)", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 7);
            return n.Trim();
        }

        /// <summary>How far (degrees) a saved thing's up direction is from how its prefab stands: the prefab's own root rotation is how
        /// it stands (a combat dummy's root is turned -90 degrees about x), so its local "standing up" is Inverse(prefab root) * up.</summary>
        static float TiltOf(NetworkEntity e, string name) {
            Quaternion baseRot = Quaternion.identity;
            try { NetworkPrefab? pf = PrefabOf(Resolve(name, 0u), name); if (pf != null) baseRot = pf.transform.localRotation; } catch { }
            Vector3 standingUp = e.transform.rotation * (Quaternion.Inverse(baseRot) * Vector3.up);
            return Vector3.Angle(standingUp, Vector3.up);
        }

        static HashSet<string> NameSet(string s) =>
            new HashSet<string>(s.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);

        static void Clean(Captured c, bool act) {
            using var _t = Timing.Begin(T_Clean);
            Vector3 centre;
            if (range != null) centre = range.Origin;
            else if (NoonRule.Spot(shrineSpot.Value, out float x, out float y, out float z)) centre = new Vector3(x, y, z);
            else { Refuse(c, $"ShrineSpot '{shrineSpot.Value}' is not 'x, y, z'"); return; }
            float reach = Mathf.Max(1f, cleanMetres.Value);
            HashSet<string> extra = NameSet(cleanNames.Value), keep = NameSet(cleanKeep.Value), fallen = NameSet(cleanFallen.Value);
            var groups = new SortedDictionary<string, (int n, float nearest, string kind)>(StringComparer.OrdinalIgnoreCase);
            var doomed = new List<(NetworkEntity e, string name, string why, float d)>();
            foreach (NetworkEntity e in UnityEngine.Object.FindObjectsOfType<NetworkEntity>()) {
                if (e == null || e.IsBeingDestroyed || e.Prefab == null || e.Prefab.SaveMode == SaveMode.Ignore) continue;
                float d = Vector3.Distance(e.transform.position, centre);
                if (d > reach) continue;
                if (e.GetComponentInParent<Player>() != null) continue;
                // a part joined to another item (an arrow's head and fletching under its shaft, JointSlot.AlignAndChild) is not docked
                // but belongs to that item: never treated on its own (0.17.7 removed the head and fletching of an arrow in a quiver)
                if (e.transform.parent != null && e.transform.parent.GetComponentInParent<NetworkEntity>() != null) continue;
                if (BareName(e.gameObject).IndexOf("Player Character", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string name = BareName(e.gameObject);
                Pickup p = e.CommonPickup;
                Agent a = e.GetComponentInChildren<Agent>();
                string kind = a != null ? (a.IsAlive ? "creature" : "dead creature")
                    : p == null ? "other" : p.IsDocked ? "docked item" : p.IsInteractedWith ? "held item" : "loose item";
                groups[name] = groups.TryGetValue(name, out var g) ? (g.n + 1, Mathf.Min(g.nearest, d), g.kind == kind ? kind : g.kind + "/" + kind) : (1, d, kind);
                if (kind == "creature" || kind == "docked item" || kind == "held item") continue;
                if (fallen.Contains(name)) {
                    float tilt = TiltOf(e, name);
                    bool down = tilt > fallenDegrees.Value;
                    MelonLogger.Msg($"[Archery] TEST RANGE: survey: '{name}' at {Targets.P(e.transform.position)}, {d:0.0} m, tilted {tilt:0} degrees from standing: {(down ? "FALLEN" : "standing")}");
                    if (down) { doomed.Add((e, name, $"fallen over ({tilt:0} degrees)", d)); continue; }
                }
                if (extra.Contains(name)) doomed.Add((e, name, "named in CleanNames", d));
                else if (kind == "loose item" && !keep.Contains(name)) doomed.Add((e, name, "loose item", d));
            }
            var sb = new StringBuilder();
            foreach (var kv in groups) sb.Append($"; {kv.Key} x{kv.Value.n} ({kv.Value.kind}, nearest {kv.Value.nearest:0.0} m)");
            MelonLogger.Msg($"[Archery] TEST RANGE: survey within {reach:0} m of {Targets.P(centre)}: {groups.Values.Sum(v => v.n)} saved thing(s) in {groups.Count} name(s){sb}");
            if (!act) {
                Status(c, true, $"survey: {groups.Values.Sum(v => v.n)} saved thing(s) within {reach:0} m; a clean-up would remove {doomed.Count} ({string.Join(", ", doomed.GroupBy(x => x.name).Select(x => $"{x.Key} x{x.Count()}").ToArray())}) (server log: 'TEST RANGE: survey')");
                return;
            }
            int n = 0;
            foreach (var x in doomed) {
                try { if (!x.e.IsBeingDestroyed) { x.e.SceneDestroy(); n++; MelonLogger.Msg($"[Archery] TEST RANGE: cleaned '{x.name}' at {Targets.P(x.e.transform.position)}, {x.d:0.0} m ({x.why})"); } }
                catch (Exception ex) { MelonLogger.Warning($"[Archery] TEST RANGE: cleaning '{x.name}' failed: {ex.Message}"); }
            }
            MelonLogger.Warning($"[Archery] TEST RANGE: clean-up removed {n} saved thing(s) within {reach:0} m of {Targets.P(centre)}");
            Status(c, true, $"clean-up: {n} thing(s) removed ({string.Join(", ", doomed.GroupBy(x => x.name).Select(x => $"{x.Key} x{x.Count()}").ToArray())})");
        }
    }
}
