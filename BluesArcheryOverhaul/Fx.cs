using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
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
#if QUEST
            Flicker f = Flicker.Take(); // Quest: a kept bolt (a new one costs about 4 ms on the headset: QuestStock)
            f.a = a; f.b = b; f.amplitude = amplitude; f.life = life; f.width = width; f.head = head; f.tail = tail; f.points = kinks + 2; f.delay = delay;
            f.Begin();
#else
            var f = new GameObject("ArcheryBolt").AddComponent<Flicker>();
            f.a = a; f.b = b; f.amplitude = amplitude; f.life = life; f.width = width; f.head = head; f.tail = tail; f.points = kinks + 2; f.delay = delay;
#endif
        }

        static readonly int T_Flash = Timing.Section("look Fx.Flash (new light)");
        internal static void Flash(Vector3 pos, Color color, float brightness, float range, float life, float delay = 0f) {
            using var _t = Timing.Begin(T_Flash);
#if QUEST
            FlashLight f = FlashLight.Take(); // Quest: a kept light (QuestStock)
            f.transform.position = pos;
            f.color = color; f.brightness = brightness; f.range = range; f.life = life; f.delay = delay;
            f.Begin();
#else
            var go = new GameObject("ArcheryFlash");
            go.transform.position = pos;
            var f = go.AddComponent<FlashLight>();
            f.color = color; f.brightness = brightness; f.range = range; f.life = life; f.delay = delay;
#endif
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
            return IndexedEffect(names);
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
#if QUEST
            PoolChangedHandler? restore = null;
            restore = Il.Keep(DelegateSupport.ConvertDelegate<PoolChangedHandler>(new Action<PooledObject>(p => {
                if (p != null) p.transform.localScale = original;
                got.remove_Released(restore);
            })));
            got.add_Released(restore);
#else
            PoolChangedHandler? restore = null;
            restore = p => {
                if (p != null) p.transform.localScale = original;
                got.Released -= restore;
            };
            got.Released += restore;
#endif
            return true;
        }

        internal static SoundEffectSet? FindSound(string names) {
            if (sounds.TryGetValue(names, out SoundEffectSet? cached) && cached != null) return cached;
            if (!Names(names).Any() || MissedRecently(soundMisses, names)) return null;
            return IndexedSound(names);
        }

        // Every first use of a name scanned every loaded asset and read every name (20-30 ms each on the Quest headset; 12 effects
        // and 15 sounds in one session, 2026-11-25; less on PC, but the same work). One scan indexes every name instead (on Quest the
        // warm-up runs it while idle after joining, QuestStock); a name the index lacks scans again at most once a minute. Matching
        // is as before: the first asset, in the order the game lists them, whose own name (or, for an effect, its prefab's) is the
        // name. Found assets stay cached by their names string (above); a miss is retried after MissRetrySeconds as before.
        static Dictionary<string, PooledObjectDefinition>? effectIndex;
        static Dictionary<string, SoundEffectSet>? soundIndex;
        static float effectIndexAt = -999f, soundIndexAt = -999f;
        const float IndexAgeSeconds = 60f;

        internal static void IndexEffects() {
            using var _t = Timing.Begin(TFindEffect, "index");
            var index = new Dictionary<string, PooledObjectDefinition>();
            foreach (PooledObjectDefinition d in Resources.FindObjectsOfTypeAll<PooledObjectDefinition>()) {
                if (d == null) continue;
                string n = d.name;
                if (!string.IsNullOrEmpty(n) && !index.ContainsKey(n)) index[n] = d;
                var pf = d.Prefab;
                if (pf != null) { string p = pf.name; if (!string.IsNullOrEmpty(p) && !index.ContainsKey(p)) index[p] = d; }
            }
            effectIndex = index; effectIndexAt = Time.unscaledTime;
        }

        internal static void IndexSounds() {
            using var _t = Timing.Begin(TFindSound, "index");
            var index = new Dictionary<string, SoundEffectSet>();
            foreach (SoundEffectSet s in Resources.FindObjectsOfTypeAll<SoundEffectSet>()) {
                if (s == null) continue;
                string n = s.name;
                if (!string.IsNullOrEmpty(n) && !index.ContainsKey(n)) index[n] = s;
            }
            soundIndex = index; soundIndexAt = Time.unscaledTime;
        }

        static T? Lookup<T>(Dictionary<string, T> index, string names, out bool first) where T : UnityEngine.Object {
            first = true;
            foreach (string n in Names(names)) {
                if (index.TryGetValue(n, out T? hit) && hit != null) return hit;
                first = false;
            }
            return null;
        }

        static PooledObjectDefinition? IndexedEffect(string names) {
            if (effectIndex == null) IndexEffects();
            PooledObjectDefinition? hit = Lookup(effectIndex!, names, out bool first);
            if (hit == null && Time.unscaledTime - effectIndexAt > IndexAgeSeconds) { IndexEffects(); hit = Lookup(effectIndex!, names, out first); }
            if (hit != null) { effects[names] = hit; effectMisses.Remove(names); Fallback(effectFallbacks, names, !first); }
            else Missed(effectMisses, names);
            return hit;
        }

        static SoundEffectSet? IndexedSound(string names) {
            if (soundIndex == null) IndexSounds();
            SoundEffectSet? hit = Lookup(soundIndex!, names, out bool first);
            if (hit == null && Time.unscaledTime - soundIndexAt > IndexAgeSeconds) { IndexSounds(); hit = Lookup(soundIndex!, names, out first); }
            if (hit != null) { sounds[names] = hit; soundMisses.Remove(names); Fallback(soundFallbacks, names, !first); }
            else Missed(soundMisses, names);
            return hit;
        }

        internal static bool EffectsIndexed => effectIndex != null;
        internal static bool SoundsIndexed => soundIndex != null;
#if QUEST
        internal static int StockBolts => Flicker.Spare;
        internal static int StockLights => FlashLight.Spare;
        internal static void StockBolt() => Flicker.Stock();
        internal static void StockLight() => FlashLight.Stock();
#endif

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
#if QUEST
            public Flicker(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            internal Vector3 a, b; internal float amplitude, life, width, delay; internal int points = 6; internal Color head, tail;
            LineRenderer line = null!; float start, next;
#if QUEST
            // Quest: bolts are kept and used again (QuestStock): a new one cost about 4 ms on the headset (2026-11-25)
            static readonly Stack<Flicker> spare = new Stack<Flicker>();
            bool begun;
            internal static int Spare => spare.Count;
            internal static Flicker Take() {
                while (spare.Count > 0) { Flicker k = spare.Pop(); if (k != null) { k.gameObject.SetActive(true); return k; } }
                return New();
            }
            internal static Flicker New() => new GameObject("ArcheryBolt").AddComponent<Flicker>();
            internal static void Stock() { Flicker k = New(); k.gameObject.SetActive(false); spare.Push(k); }
            [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
            internal void Begin() {
                begun = true;
                start = Time.time + delay; next = 0f;
                if (line == null) line = NewLine(gameObject, points, width, true);
                else { line.positionCount = points; line.widthMultiplier = width; }
                line.enabled = false;
            }
            [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
            void Give() {
                begun = false;
                if (line != null) line.enabled = false;
                if (spare.Count >= 96) { Destroy(gameObject); return; }
                gameObject.SetActive(false);
                spare.Push(this);
            }
            void Start() { if (!begun) Begin(); }
#else
            void Start() { start = Time.time + delay; line = NewLine(gameObject, points, width, true); line.enabled = false; }
#endif
            static readonly int TUpdate = Timing.Section("Update Fx.Flicker");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float t = Time.time - start;
                if (t < 0f) return;
#if QUEST
                if (t >= life) { Give(); return; }
#else
                if (t >= life) { Destroy(gameObject); return; }
#endif
                float k = 1f - t / life;
                line.enabled = true;
                line.startColor = new Color(head.r, head.g, head.b, head.a * k);
                line.endColor = new Color(tail.r, tail.g, tail.b, tail.a * k);
                if (Time.time >= next) { next = Time.time + 0.03f; Zigzag(line, a, b, amplitude); }
            }
        }

        class FlashLight : MonoBehaviour {
#if QUEST
            public FlashLight(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            internal float brightness, range, life, delay; internal Color color;
            Light lamp = null!; float start;
#if QUEST
            // Quest: lights are kept and used again (QuestStock), as the bolts are
            static readonly Stack<FlashLight> spare = new Stack<FlashLight>();
            bool begun;
            internal static int Spare => spare.Count;
            internal static FlashLight Take() {
                while (spare.Count > 0) { FlashLight k = spare.Pop(); if (k != null) { k.gameObject.SetActive(true); return k; } }
                return New();
            }
            internal static FlashLight New() => new GameObject("ArcheryFlash").AddComponent<FlashLight>();
            internal static void Stock() { FlashLight k = New(); k.gameObject.SetActive(false); spare.Push(k); }
            [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
            internal void Begin() {
                begun = true;
                start = Time.time + delay;
                if (lamp == null) { lamp = gameObject.AddComponent<Light>(); lamp.type = LightType.Point; lamp.shadows = LightShadows.None; }
                lamp.color = color; lamp.range = range; lamp.intensity = 0f;
            }
            [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
            void Give() {
                begun = false;
                if (lamp != null) lamp.intensity = 0f;
                if (spare.Count >= 32) { Destroy(gameObject); return; }
                gameObject.SetActive(false);
                spare.Push(this);
            }
            void Start() { if (!begun) Begin(); }
#else
            void Start() {
                start = Time.time + delay;
                lamp = gameObject.AddComponent<Light>();
                lamp.type = LightType.Point; lamp.color = color; lamp.range = range; lamp.shadows = LightShadows.None; lamp.intensity = 0f;
            }
#endif
            static readonly int TUpdate = Timing.Section("Update Fx.FlashLight");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float t = Time.time - start;
                if (t < 0f) return;
#if QUEST
                if (t >= life) { Give(); return; }
#else
                if (t >= life) { Destroy(gameObject); return; }
#endif
                lamp.intensity = brightness * (1f - t / life);
            }
        }
    }
}
