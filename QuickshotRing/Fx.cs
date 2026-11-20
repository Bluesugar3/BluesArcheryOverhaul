using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Shared drawing primitives and game-asset lookup. No effect design lives here: each effect is its own
    // small class next to the feature it belongs to, so one can be replaced without touching the others.
    internal static class Fx {
        static bool viewLogged;

        /// <summary>The local player's own eye: the game's PlayerController.Current.Camera (what the game itself uses), else
        /// Camera.main. Camera.main is the first enabled camera tagged MainCamera, which another mod's or the game's extra
        /// camera can be; once a session, a Summary line says when it is not the player's camera.</summary>
        internal static Camera? View() {
            PlayerController pc = PlayerController.Current;
            Camera eye = pc != null ? pc.Camera : null!;
            Camera main = Camera.main;
            if (eye != null && eye.isActiveAndEnabled) {
                if (!viewLogged && main != null && main != eye) {
                    viewLogged = true;
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] View: Camera.main is '{main.name}' at {main.transform.position}, not the player's camera '{eye.name}' at {eye.transform.position}; the player's is used");
                }
                return eye;
            }
            return main;
        }

        const int CircleSegments = 48;
        static Material? lineMaterial;
        static readonly Dictionary<string, PooledObjectDefinition?> effects = new Dictionary<string, PooledObjectDefinition?>();
        static readonly Dictionary<string, SoundEffectSet?> sounds = new Dictionary<string, SoundEffectSet?>();
        static bool dumped;

        // One flat, untextured, vertex-coloured transparent material shared by every line the mod draws.
        static readonly int T_LineMaterial = Timing.Section("asset Fx.LineMaterial");
        internal static Material? LineMaterial() {
            using var _t = Timing.Begin(T_LineMaterial);
            if (lineMaterial != null) return lineMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            bool fallback = shader == null;
            if (fallback) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) {
                MelonLogger.Error("[Archery] No usable transparent shader found; line effects are off.");
                return null;
            }
            lineMaterial = new Material(shader) { renderQueue = 3100 };
            if (fallback) {
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMaterial.SetInt("_Cull", 0);
                lineMaterial.SetInt("_ZWrite", 0);
            }
            return lineMaterial;
        }

        internal static LineRenderer NewLine(GameObject go, int points, float width, bool taper) {
            var lr = go.AddComponent<LineRenderer>();
            lr.material = LineMaterial();
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.widthCurve = taper ? AnimationCurve.Linear(0f, 1f, 1f, 0.2f) : AnimationCurve.Constant(0f, 1f, 1f);
            lr.widthMultiplier = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            return lr;
        }

        /// <summary>A closed unit circle in the object's local XY plane; radius = transform scale.</summary>
        internal static void BuildCircle(LineRenderer lr) {
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = CircleSegments;
            for (int i = 0; i < CircleSegments; i++) {
                float a = i * Mathf.PI * 2f / CircleSegments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
        }

        internal static void Basis(Vector3 axis, out Vector3 u, out Vector3 w) {
            u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            w = Vector3.Cross(axis, u);
        }

        /// <summary>A jagged line from a to b, pinned at both ends. Call again to re-roll the kinks.</summary>
        internal static void Zigzag(LineRenderer lr, Vector3 a, Vector3 b, float amplitude) {
            int n = lr.positionCount;
            if (n < 2) return;
            Basis((b - a).normalized, out Vector3 u, out Vector3 w);
            for (int i = 0; i < n; i++) {
                float f = i / (float)(n - 1);
                float amp = amplitude * Mathf.Sin(f * Mathf.PI);
                lr.SetPosition(i, Vector3.Lerp(a, b, f) + (u * UnityEngine.Random.Range(-1f, 1f) + w * UnityEngine.Random.Range(-1f, 1f)) * amp);
            }
        }

        /// <summary>A short-lived flickering bolt. kinks = number of bends between the ends.</summary>
        static readonly int T_Bolt = Timing.Section("look Fx.Bolt (new object)");
        internal static void Bolt(Vector3 a, Vector3 b, float amplitude, float life, float width, Color head, Color tail, int kinks = 4, float delay = 0f) {
            using var _t = Timing.Begin(T_Bolt);
            if (LineMaterial() == null) return;
            var f = new GameObject("ArcheryBolt").AddComponent<Flicker>();
            f.a = a; f.b = b; f.amplitude = amplitude; f.life = life; f.width = width; f.head = head; f.tail = tail; f.points = kinks + 2; f.delay = delay;
        }

        static readonly int T_Flash = Timing.Section("look Fx.Flash (new light)");
        internal static void Flash(Vector3 pos, Color color, float brightness, float range, float life, float delay = 0f) {
            using var _t = Timing.Begin(T_Flash);
            var go = new GameObject("ArcheryFlash");
            go.transform.position = pos;
            var f = go.AddComponent<FlashLight>();
            f.color = color; f.brightness = brightness; f.range = range; f.life = life; f.delay = delay;
        }

        // ---------------------------------------------------------------- the game's own assets, by name

        static IEnumerable<string> Names(string list) => list.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0);

        // A name that does not resolve (a typo, a renamed asset, not loaded yet) is not searched for again on every call
        // (spec Part 11): the miss is remembered for MissRetrySeconds, or until the settings are reloaded.
        const float MissRetrySeconds = 5f; // Ruling 9: internal, not a setting
        static readonly Dictionary<string, float> effectMisses = new Dictionary<string, float>(), soundMisses = new Dictionary<string, float>();
        static readonly HashSet<string> missLogged = new HashSet<string>();

        static bool MissedRecently(Dictionary<string, float> misses, string names) =>
            misses.TryGetValue(names, out float until) && Time.unscaledTime < until;

        static void Missed(Dictionary<string, float> misses, string names) {
            misses[names] = Time.unscaledTime + MissRetrySeconds;
            if (missLogged.Add(names))
                MelonLogger.Warning($"[Archery] Asset '{names}' not found; trying again in {MissRetrySeconds:0} s or after a settings reload"); // G5 (M-5): every level
        }

        static readonly int TFindEffect = Timing.Section("asset Fx.FindEffect search (FindObjectsOfTypeAll)"),
            TFindSound = Timing.Section("asset Fx.FindSound search (FindObjectsOfTypeAll)");

        /// <summary>First of the named pooled effects the game has loaded, or null. names = "A;B;C".</summary>
        internal static PooledObjectDefinition? FindEffect(string names) {
            if (effects.TryGetValue(names, out PooledObjectDefinition? cached) && cached != null) return cached;
            if (!Names(names).Any() || MissedRecently(effectMisses, names)) return null;
            using var _t = Timing.Begin(TFindEffect, names); // G5: the search only (cache hits and recent misses return above)
            PooledObjectDefinition[] all = Resources.FindObjectsOfTypeAll<PooledObjectDefinition>();
            PooledObjectDefinition? hit = null;
            bool first = true;
            foreach (string n in Names(names)) {
                hit = all.FirstOrDefault(d => d != null && (d.name == n || (d.Prefab != null && d.Prefab.name == n)));
                if (hit != null) break;
                first = false;
            }
            if (hit != null) { effects[names] = hit; effectMisses.Remove(names); Fallback(effectFallbacks, names, !first); }
            else Missed(effectMisses, names);
            return hit;
        }

        // G2: lists whose cached asset is a later name's (an earlier name was not loaded then). A reload drops these, as it dropped
        // every found asset before, so an earlier name that has loaded since still wins after a settings save.
        static readonly HashSet<string> effectFallbacks = new HashSet<string>(), soundFallbacks = new HashSet<string>();
        static void Fallback(HashSet<string> set, string names, bool fallback) { if (fallback) set.Add(names); else set.Remove(names); }

        /// <summary>One of the game's pooled effects at <paramref name="k"/> times its own size. The pooled object is scaled
        /// only while it plays: it is put back to its own scale the moment it returns to its pool, so the game's own use of
        /// it is never smaller. False when the pool gave nothing.</summary>
        internal static bool GetScaled(PooledObjectDefinition def, Vector3 at, Quaternion rot, float k) {
            PooledObject got = def.Get(at, rot);
            if (got == null) return false;
            Transform tr = got.transform;
            Vector3 original = tr.localScale;
            tr.localScale = original * k;
            PoolChangedHandler? restore = null;
            restore = p => {
                if (p != null) p.transform.localScale = original;
                got.Released -= restore;
            };
            got.Released += restore;
            return true;
        }

        internal static SoundEffectSet? FindSound(string names) {
            if (sounds.TryGetValue(names, out SoundEffectSet? cached) && cached != null) return cached;
            if (!Names(names).Any() || MissedRecently(soundMisses, names)) return null;
            using var _t = Timing.Begin(TFindSound, names); // G5: the search only
            SoundEffectSet[] all = Resources.FindObjectsOfTypeAll<SoundEffectSet>();
            SoundEffectSet? hit = null;
            bool first = true;
            foreach (string n in Names(names)) {
                hit = all.FirstOrDefault(s => s != null && s.name == n);
                if (hit != null) break;
                first = false;
            }
            if (hit != null) { sounds[names] = hit; soundMisses.Remove(names); Fallback(soundFallbacks, names, !first); }
            else Missed(soundMisses, names);
            return hit;
        }

        /// <summary>After a settings change: forget the names that were NOT found, so they are tried again at once. G2: an asset found
        /// on its list's FIRST name is kept (it is keyed by its own names string, so a changed setting is a new key; a destroyed asset
        /// fails the cached != null test and is searched again; a new search would find that first name again), which saves a full
        /// FindObjectsOfTypeAll per name on the first shots after a save. One found on a later name is dropped as before.</summary>
        internal static void ClearAssetCache() {
            effectMisses.Clear(); soundMisses.Clear();
            foreach (string names in effectFallbacks) effects.Remove(names);
            foreach (string names in soundFallbacks) sounds.Remove(names);
            effectFallbacks.Clear(); soundFallbacks.Clear();
        }

        /// <summary>One-time list of what the game has loaded, so asset names in the settings can be chosen from facts.</summary>
        static readonly int T_DumpAssetsOnce = Timing.Section("asset Fx.DumpAssetsOnce ([Log] DumpAssets)");
        internal static void DumpAssetsOnce() {
            using var _t = Timing.Begin(T_DumpAssetsOnce);
            if (dumped || ApplicationManager.IsHeadless) return;
            dumped = true;
            try {
                var sb = new StringBuilder();
                sb.AppendLine("# Game assets loaded right now that the archery mod can reuse. Put names in UserData\\QuickshotRing.cfg.");
                sb.AppendLine("\n## Pooled effects (ImpactEffects)");
                foreach (string n in Resources.FindObjectsOfTypeAll<PooledObjectDefinition>().Where(d => d != null)
                             .Select(d => d.name + (d.Prefab != null ? "  -> prefab " + d.Prefab.name : "")).Distinct().OrderBy(n => n)) sb.AppendLine(n);
                sb.AppendLine("\n## Sounds (ImpactSounds)");
                foreach (string n in Resources.FindObjectsOfTypeAll<SoundEffectSet>().Where(s => s != null).Select(s => s.name).Distinct().OrderBy(n => n)) sb.AppendLine(n);
                sb.AppendLine("\n## Particle systems: name | material | shader");
                foreach (string n in Resources.FindObjectsOfTypeAll<ParticleSystem>().Where(p => p != null).Select(p => {
                             var r = p.GetComponent<ParticleSystemRenderer>();
                             Material? m = r != null ? r.sharedMaterial : null;
                             return $"{p.gameObject.name} | {(m != null ? m.name : "-")} | {(m != null && m.shader != null ? m.shader.name : "-")}";
                         }).Distinct().OrderBy(n => n)) sb.AppendLine(n);
                Material[] mats = Resources.FindObjectsOfTypeAll<Material>().Where(m => m != null).ToArray();
                sb.AppendLine("\n## Materials: name | shader");
                foreach (string n in mats.Select(m => $"{m.name} | {(m.shader != null ? m.shader.name : "-")}").Distinct().OrderBy(n => n)) sb.AppendLine(n);
                sb.AppendLine("\n## Line renderers: object | material | shader");
                foreach (string n in Resources.FindObjectsOfTypeAll<LineRenderer>().Where(r => r != null).Select(r => RendererLine(r.gameObject.name, r.sharedMaterial)).Distinct().OrderBy(n => n)) sb.AppendLine(n);
                sb.AppendLine("\n## Trail renderers: object | material | shader");
                foreach (string n in Resources.FindObjectsOfTypeAll<TrailRenderer>().Where(r => r != null).Select(r => RendererLine(r.gameObject.name, r.sharedMaterial)).Distinct().OrderBy(n => n)) sb.AppendLine(n);
                Directory.CreateDirectory(Cfg.Folder);
                string path = Path.Combine(Cfg.Folder, "loaded_assets.txt");
                File.WriteAllText(path, sb.ToString());
                MelonLogger.Msg($"[Archery] Wrote the list of loaded game assets to {path}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Asset list failed: {e.Message}"); }
            LogMaterialLookups();
        }

        static string RendererLine(string obj, Material? m) => $"{obj} | {(m != null ? m.name : "-")} | {(m != null && m.shader != null ? m.shader.name : "-")}";

        static void LookupLine(string label, string path, string expected, Func<(Material? mat, string failed)> find) {
            try {
                (Material? mat, string failed) = find();
                if (mat == null) MelonLogger.Msg($"[Archery] Material lookup {label}: via {path} -> NOT FOUND ({failed})");
                else MelonLogger.Msg($"[Archery] Material lookup {label}: via {path} -> '{mat.name}' ({(mat.shader != null ? mat.shader.name : "-")}), expected '{expected}': {(mat.name == expected ? "match" : "DIFFERENT")}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Material lookup {label} failed: {e.Message}"); }
        }

        /// <summary>The game's own materials the line/trail effects could reuse (spec Part 12 item 13), and what the mod uses now.</summary>
        static void LogMaterialLookups() {
            // also the Fill's halo and its gradient test (ChargeFx), which take copies of it
            LookupLine("orb-line", "LineRenderer in pooled effect 'Skill Orb Connection'", "Orb Connection Line", OrbLineMaterial);
            LookupLine("wind-streak", "TrailRenderer in pooled effect 'Quick Draw Shoot'", "WindStreak_edge_m", () => {
                PooledObjectDefinition? d = FindEffect("Quick Draw Shoot");
                if (d == null) return (null, "pooled effect not found");
                if (d.Prefab == null) return (null, "effect has no prefab");
                TrailRenderer? r = d.Prefab.GetComponentInChildren<TrailRenderer>(true);
                return r == null ? (null, "no TrailRenderer in prefab") : (r.sharedMaterial, "no material");
            });
            LookupLine("tool-effect", "ProgressionSettings.ToolBasedEffect baseMaterial", "Tool Effect Material", () => {
                PooledObjectDefinition? d = Alta.Global.GlobalSettings<ProgressionSettings>.Instance.ToolBasedEffect;
                if (d == null) return (null, "ToolBasedEffect not set");
                if (d.Prefab == null) return (null, "effect has no prefab");
                ToolBasedEffectBase? e = d.Prefab.GetComponent<ToolBasedEffectBase>();
                if (e == null) return (null, "no ToolBasedEffectBase on prefab");
                Material? m = HarmonyLib.Traverse.Create(e).Field("baseMaterial").GetValue<Material>();
                return (m, "baseMaterial is null");
            });
            LookupLine("steel-song", "Renderer in pooled effect 'Steel Song Mesh Effect'", "Steel Song Tool Effect Material", () => {
                PooledObjectDefinition? d = FindEffect("Steel Song Mesh Effect");
                if (d == null) return (null, "pooled effect not found");
                if (d.Prefab == null) return (null, "effect has no prefab");
                Material? m = d.Prefab.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(x => x != null);
                return (m, "no renderer with a material in prefab");
            });
            LogSteelSongBase();
            LookupLine("weapon-trail", "ParticleSystemRenderer.sharedMaterial named 'WeaponTrail'", "WeaponTrail", () => {
                Material? m = Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>().Where(r => r != null && r.sharedMaterial != null && r.sharedMaterial.name == "WeaponTrail")
                    .Select(r => r.sharedMaterial).FirstOrDefault();
                return (m, "no particle renderer uses it");
            });
            try {
                Material? line = LineMaterial();
                Material? heads = Volley.ResolveHeadMaterial();
                MelonLogger.Msg($"[Archery] Materials in use: line '{(line != null ? line.name : "-")}' ({(line != null && line.shader != null ? line.shader.name : "-")}), ghost heads '{(heads != null ? heads.name : "-")}' ({(heads != null && heads.shader != null ? heads.shader.name : "-")})");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Materials in use probe failed: {e.Message}"); }
        }

        const string SteelSongMaterial = "Steel Song Tool Effect Material", SteelSongMeshEffect = "Steel Song Mesh Effect";

        // The fallback material for the ramp test (spec Part 12 item 13), by two routes. 'Steel Song Mesh Effect' is the
        // MeshEffect pooled object, not a ToolBasedEffectBase: the effect base holds it in a private meshEffectDefinition and
        // puts its own baseMaterial on each mesh effect's renderer at run time (ToolBasedEffectBase.cs:11-14, :73-83), which
        // is why play-test 1 found no material on that prefab.
        static void LogSteelSongBase() {
            try {
                Material? m = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(x => x != null && x.name == SteelSongMaterial);
                if (m == null) MelonLogger.Msg($"[Archery] Material lookup steel-song-base: direct '{SteelSongMaterial}' -> NOT FOUND");
                else MelonLogger.Msg($"[Archery] Material lookup steel-song-base: direct '{SteelSongMaterial}' -> '{m.name}' ({ShaderName(m)}); colours: {ColourProps(m)}; textures: {TextureProps(m)}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Material lookup steel-song-base (direct) failed: {e.Message}"); }
            try {
                ToolBasedEffectBase[] bases = Resources.FindObjectsOfTypeAll<ToolBasedEffectBase>().Where(b => b != null).ToArray();
                var pairs = new List<string>();
                ToolBasedEffectBase? steel = null;
                foreach (ToolBasedEffectBase b in bases) {
                    HarmonyLib.Traverse t = HarmonyLib.Traverse.Create(b);
                    PooledObjectDefinition def = t.Field("meshEffectDefinition").GetValue<PooledObjectDefinition>();
                    Material mat = t.Field("baseMaterial").GetValue<Material>();
                    string pair = $"{(def != null ? def.name : "-")}->{(mat != null ? mat.name : "-")}";
                    if (!pairs.Contains(pair)) pairs.Add(pair);
                    if (steel == null && def != null && (def.name == SteelSongMeshEffect || (def.Prefab != null && def.Prefab.name == SteelSongMeshEffect))) steel = b;
                }
                MelonLogger.Msg($"[Archery] Material lookup tool-effect bases: {(pairs.Count > 0 ? string.Join(" ", pairs) : "none loaded")}");
                const string via = "[Archery] Material lookup steel-song-base: via the ToolBasedEffectBase using '" + SteelSongMeshEffect + "' -> ";
                if (bases.Length == 0) MelonLogger.Msg(via + "NOT FOUND (no ToolBasedEffectBase is loaded)");
                else if (steel == null) MelonLogger.Msg(via + $"NOT FOUND (none of the {bases.Length} ToolBasedEffectBase(s) has it as meshEffectDefinition)");
                else {
                    Material found = HarmonyLib.Traverse.Create(steel).Field("baseMaterial").GetValue<Material>();
                    if (found == null) MelonLogger.Msg(via + $"NOT FOUND ('{steel.name}' uses it, but its baseMaterial is null)");
                    else MelonLogger.Msg(via + $"'{found.name}' ({ShaderName(found)})");
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Material lookup steel-song-base (via the effect) failed: {e.Message}"); }
        }

        /// <summary>The game's 'Orb Connection Line' material, through the 'Skill Orb Connection' line renderer (a material
        /// only: the effect itself is never spawned), or null and why.</summary>
        internal static (Material? mat, string failed) OrbLineMaterial() {
            PooledObjectDefinition? d = FindEffect("Skill Orb Connection");
            if (d == null) return (null, "pooled effect not found");
            if (d.Prefab == null) return (null, "effect has no prefab");
            LineRenderer? r = d.Prefab.GetComponentInChildren<LineRenderer>(true);
            return r == null ? (null, "no LineRenderer in prefab") : (r.sharedMaterial, "no material");
        }

        /// <summary>Sets every colour property of the material's shader to white; returns their names, or "none".</summary>
        internal static string ResetColours(Material m) {
            if (m.shader == null) return "none";
            var parts = new List<string>();
            int n = m.shader.GetPropertyCount();
            for (int i = 0; i < n; i++) {
                if (m.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                string p = m.shader.GetPropertyName(i);
                m.SetColor(p, Color.white);
                parts.Add(p);
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        internal static string ShaderName(Material m) => m.shader != null ? m.shader.name : "-";

        /// <summary>"prop='texture', ..." for every texture property of the material, or "none".</summary>
        internal static string TextureProps(Material m) {
            var parts = new List<string>();
            foreach (string p in m.GetTexturePropertyNames()) {
                Texture tex = m.GetTexture(p);
                parts.Add($"{p}='{(tex != null ? tex.name : "-")}'");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        /// <summary>"prop=RGBA(...), ..." for every colour property of the material's shader, or "none".</summary>
        internal static string ColourProps(Material m) {
            if (m.shader == null) return "none";
            var parts = new List<string>();
            int n = m.shader.GetPropertyCount();
            for (int i = 0; i < n; i++) {
                if (m.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                string p = m.shader.GetPropertyName(i);
                parts.Add($"{p}={m.GetColor(p).ToString("F3")}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        // ---------------------------------------------------------------- components

        class Flicker : MonoBehaviour {
            internal Vector3 a, b; internal float amplitude, life, width, delay; internal int points = 6; internal Color head, tail;
            LineRenderer line = null!; float start, next;
            void Start() { start = Time.time + delay; line = NewLine(gameObject, points, width, true); line.enabled = false; }
            static readonly int TUpdate = Timing.Section("Update Fx.Flicker");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float t = Time.time - start;
                if (t < 0f) return;
                if (t >= life) { Destroy(gameObject); return; }
                float k = 1f - t / life;
                line.enabled = true;
                line.startColor = new Color(head.r, head.g, head.b, head.a * k);
                line.endColor = new Color(tail.r, tail.g, tail.b, tail.a * k);
                if (Time.time >= next) { next = Time.time + 0.03f; Zigzag(line, a, b, amplitude); }
            }
        }

        class FlashLight : MonoBehaviour {
            internal float brightness, range, life, delay; internal Color color;
            Light lamp = null!; float start;
            void Start() {
                start = Time.time + delay;
                lamp = gameObject.AddComponent<Light>();
                lamp.type = LightType.Point; lamp.color = color; lamp.range = range; lamp.shadows = LightShadows.None; lamp.intensity = 0f;
            }
            static readonly int TUpdate = Timing.Section("Update Fx.FlashLight");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float t = Time.time - start;
                if (t < 0f) return;
                if (t >= life) { Destroy(gameObject); return; }
                lamp.intensity = brightness * (1f - t / life);
            }
        }
    }
}
