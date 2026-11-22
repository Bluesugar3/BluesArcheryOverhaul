using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Skyfall's storm look (Task 11): looks only, clients only; the server decides every hit (SkyfallServer), and nothing here is sent.
    // Every machine draws the same storm from the arrow id, the relayed sigil centre and the [shared] settings; t below is
    // Time.time - StartTime (the release on this machine). The timeline:
    //   - at the release, ReleaseSound plays at the release point and a gold ghost of the plain Arrow (its real size) streaks up to the
    //     sigil centre in StreakSeconds, slowing as it arrives;
    //   - from the release to BuildSeconds the sky sigil unfolds core first; when the ghost reaches it (StreakSeconds) SigilSound plays
    //     and the storm's first light flashes there (through Bright, the one bright-moment gate: at most 3 a second here);
    //   - from TwinDelaySeconds its twin burns into the ground under the centre (TwinSound there), unfolding until BuildSeconds;
    //   - each rain arrow, FallSeconds before it lands, falls as a giant see-through gold Arrow (ArrowLength long, the Lightbeam's trail
    //     behind it) from the sigil plane to its spot, speeding up;
    //   - each landing (a whole wave at once): only a share of each wave explodes in the game's 'Explosion' (it plays 'Huge Explosion'
    //     itself), within this machine's ExplosionBudget and never within MinImpactSpacing of another explosion still alive; the rest
    //     show fire sparks and OverBudgetSound (never in a frame that started an explosion); every landing leaves a scorch that cools
    //     from fire to char and fades; the World-Nail's landing is SkyfallNail's (Task 12);
    //   - when the Nail lands the twin burns away (0.4 s); the sky sigil fades 1.5 s later (0.6 s) unless SkyfallNail took it to shatter;
    //   - a fizzle (a refused cast relayed as Fizzled, or the caster's own storm when Skyfall was taken within a second of the release):
    //     only the streak and the sky sigil's core, which puffs out over FizzleSeconds in FizzlePuff, gold sparks and FizzleSound; no
    //     rain, no twin, no dim.
    // The rain's danger domes, the festival looks and "OUT!" were cut (owner, 2026-11-19: Marked Ground and festival mode dropped):
    // a storm's Festival code is drawn as a normal storm here. The game's objects are never instantiated or changed: the arrow and the
    // materials are read and copied (the Arrow prefab 56460's meshes, the Etheral Arrow's 'SelectedPiece', the Lightbeam's trail). Every
    // renderer is on layer 1 (SkyfallSigil.Layer) under one DontDestroyOnLoad host; giant arrows are pooled (64); scorches are
    // SkyfallSigil's loose pieces (at most 40 scorches). Asset searches run at an idle moment (the warm-up), never in a release's frame;
    // then the warm-up builds the first storm's pools ahead, one item per idle frame (SkyfallPools; final review I-2), a landing's
    // explosion and scorch start at most one of each a frame (SkyfallPacer), and a wave's explosions are one bright moment (Bright).
    internal static class SkyfallLook {
        const float TakenWindowSeconds = 1f;   // a take this soon after the release may have reached the server before the shot
        const float LateSeconds = 1f;          // a landing first seen this long after its time shows nothing
        const float TwinBurnSeconds = 0.4f, SkyHoldSeconds = 1.5f, SkyFadeSeconds = 0.6f, FizzleSwell = 0.25f;
        const float WarmAfterSeconds = 5f;
        const int ArrowCap = SkyfallPools.GiantArrows, ScorchCap = 40, RingCap = 64;
        const uint PlainArrowHash = 56460u, EtheralArrowHash = 52366u, LightBeamHash = 1798u;
        const string RainSourceName = "SelectedPiece", TrailSourceName = "Slashing_Spikes_Solid_m";

        // ---------------------------------------------------------------- settings (per client, never [shared])

        static MelonPreferences_Entry<float>? lookMax, streakSeconds, twinDelay, rainAlpha, rainFan, trailMetres, trailWidth, explosionScale,
            explosionLife, scorchSize, scorchSeconds, scorchCool, flashBrightness, flashRange, flashSeconds, fizzleSeconds;
        static MelonPreferences_Entry<int>? impactSparks;
        static MelonPreferences_Entry<string>? explosionEffect, overBudgetSound, releaseSound, sigilSound, twinSound, fizzlePuff, fizzleSound;

        internal static void Init(MelonPreferences_Category cat) {
            const string P = "Clients (looks only): ";
            MelonPreferences_Entry<float> F(string key, float value, string text) => cat.CreateEntry(key, value, description: P + text);
            MelonPreferences_Entry<string> S(string key, string value, string text) => cat.CreateEntry(key, value, description: P + text);
            lookMax = F("LookMaxMetres", 600f, "a storm whose sigil is farther than this from this player is not drawn here (the archer always draws their own).");
            streakSeconds = F("StreakSeconds", 0.35f, "seconds the archer's arrow takes to streak up into the sigil as a gold ghost.");
            twinDelay = F("TwinDelaySeconds", 0.25f, "seconds after the release before the sigil's twin starts to burn into the ground.");
            rainAlpha = F("RainAlpha", 0.55f, "how solid the see-through gold giant arrows are.");
            rainFan = F("RainFan", 0.5f, "where a giant arrow starts in the sigil: this share of the way from the sigil's centre to its landing spot (0 = all from the centre, 1 = straight down).");
            trailMetres = F("RainTrailMetres", 9f, "the length of a giant arrow's trail.");
            trailWidth = F("RainTrailWidth", 0.9f, "its width at the arrow, in metres.");
            explosionEffect = S("ExplosionEffect", "Explosion", "the game's effect for a landing. It plays 'Huge Explosion' itself: never add that sound.");
            explosionScale = F("ExplosionScale", 1.6f, "its size, times the game's own.");
            explosionLife = F("ExplosionLifeSeconds", 6f, "how long one counts against MaxLiveExplosions (the game's effect lives 6 s).");
            impactSparks = cat.CreateEntry("ImpactSparks", 24, description: P + "the sparks of a landing that shows no explosion.");
            overBudgetSound = S("OverBudgetSound", "Small Explosion", "the sound of a landing that shows no explosion, played only when no explosion started in that frame.");
            scorchSize = F("ScorchSize", 3.5f, "metres across a landing's scorch.");
            scorchSeconds = F("ScorchSeconds", 14f, "seconds a scorch stays (it fades over its last 3).");
            scorchCool = F("ScorchCoolSeconds", 6f, "seconds a scorch takes to cool from fire to char.");
            releaseSound = S("ReleaseSound", "Lightbeam Shot Effect Sounds", "at the release, as the ghost streaks up.");
            sigilSound = S("SigilSound", "Shrine Activate Sounds", "at the sigil when the ghost reaches it.");
            twinSound = S("TwinSound", "Shockwave Sounds", "where the twin burns into the ground.");
            flashBrightness = F("FlashBrightness", 3f, "the storm's one light, when the sigil opens.");
            flashRange = F("FlashRange", 40f, "its range in metres.");
            flashSeconds = F("FlashSeconds", 0.5f, "its life in seconds (also the World-Nail's light).");
            fizzleSeconds = F("FizzleSeconds", 0.8f, "seconds a refused cast's sigil takes to puff out.");
            fizzlePuff = S("FizzlePuff", "Large Smoke Puff", "the puff it ends in.");
            fizzleSound = S("FizzleSound", "Small Explosion", "its sound.");
            Cfg.Reloaded += Reloaded;
        }

        // a broken (NaN) setting reads as its default, anything else is held to [lo, hi] (an infinite one falls to its bound)
        static float G(MelonPreferences_Entry<float>? e, float fallback, float lo, float hi) {
            float x = e != null ? e.Value : fallback;
            return float.IsNaN(x) ? fallback : x < lo ? lo : x > hi ? hi : x;
        }
        static string Name(MelonPreferences_Entry<string>? e, string fallback) => e != null ? e.Value ?? "" : fallback;

        static float LookMaxMetres => G(lookMax, 600f, 0f, 100000f);
        static float StreakSeconds => G(streakSeconds, 0.35f, 0f, 5f);
        static float TwinDelaySeconds => G(twinDelay, 0.25f, 0f, 10f);
        static float RainAlpha => G(rainAlpha, 0.55f, 0f, 1f);
        static float RainFan => G(rainFan, 0.5f, 0f, 1f);
        static float RainTrailMetres => G(trailMetres, 9f, 0f, 200f);
        static float RainTrailWidth => G(trailWidth, 0.9f, 0f, 20f);
        static float ExplosionScale => G(explosionScale, 1.6f, 0.05f, 20f);
        static int ImpactSparks { get { int n = impactSparks != null ? impactSparks.Value : 24; return n < 0 ? 0 : n > 200 ? 200 : n; } }
        static float ScorchSize => G(scorchSize, 3.5f, 0f, 50f);
        static float ScorchSeconds => G(scorchSeconds, 14f, 0.1f, 600f);
        static float ScorchCoolSeconds => G(scorchCool, 6f, 0.01f, 600f);
        static float FizzleSeconds => G(fizzleSeconds, 0.8f, 0f, 10f);

        /// <summary>[Skyfall] ExplosionLifeSeconds: how long one explosion counts against <see cref="Budget"/>.</summary>
        internal static float ExplosionLifeSeconds => G(explosionLife, 6f, 0f, 60f);
        /// <summary>[Skyfall] ExplosionEffect: the game's pooled effect for a landing (plays 'Huge Explosion' itself).</summary>
        internal static string ExplosionName => Name(explosionEffect, "Explosion");
        /// <summary>[Skyfall] FlashSeconds: the storm's light's life (the World-Nail's light uses it too).</summary>
        internal static float FlashSeconds => G(flashSeconds, 0.5f, 0f, 10f);
        /// <summary>[Skyfall] OverBudgetSound: a landing that shows no explosion (the World-Nail's dimmed finale uses it too).</summary>
        internal static string SpareSoundName => Name(overBudgetSound, "Small Explosion");

        static readonly SkyfallFlashGate flashGate = new SkyfallFlashGate();
        /// <summary>The one gate every bright moment of a Skyfall look goes through on this machine, across every storm (final review
        /// I-3, the owner's VR rule: no more than 3 bright flashes a second; SkyfallFlashGate, tested): an explosion moment, an Fx.Flash
        /// light, the World-Nail's finale and its appear flash. True: show it (it is counted); false: show it dimmed, sparks and smoke
        /// with no light and no explosion. Moments a frame or two apart are one (a wave's explosions).</summary>
        internal static bool Bright(float now) => flashGate.TryTake(now);

        static ExplosionBudget? budget;
        static int budgetCap = -1;
        /// <summary>This machine's cap on live explosions (Skyfall.Values.MaxLiveExplosions), made on first use and remade when that
        /// value changes after a reload. Task 12's Nail takes from it too.</summary>
        internal static ExplosionBudget Budget {
            get {
                int cap = Skyfall.Values.MaxLiveExplosions;
                if (budget == null || cap != budgetCap) { budget = new ExplosionBudget(cap); budgetCap = cap; }
                return budget;
            }
        }

        static bool lookFailed, rainMatDone, trailMatDone, partsDone, rainWarned, partsWarned;
        static int warmStep;                                                // 0 = not warmed this settings load; one lookup per idle frame

        static void Reloaded() {
            lookFailed = false;
            warmStep = 0;
            prebuildPhase = 0;                                              // a pool already full is passed in one idle frame
            rainMatDone = false;                                            // a new gold copy (the colour and RainAlpha may have changed)
            if (trailMat == null || trailFlat) trailMatDone = false;         // the trail again only when its source was missing
            if (parts == null) partsDone = false;                           // the Arrow again only when it was missing
            rainWarned = partsWarned = false;
            fullSaid.Clear();
        }

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        static Vector3 V(HVec h) => new Vector3((float)h.X, (float)h.Y, (float)h.Z);
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        static float Ground(double x, double z, double top, double bottom) => Skyfall.GroundAt(x, z, top, bottom, out float y) ? y : float.NaN;

        // ---------------------------------------------------------------- the game's pieces: the plain Arrow, the gold, the trail

        internal enum ArrowRole { Head, Shaft, Vane }
        /// <summary>One renderer of the plain Arrow, in the arrow's frame: +z toward the point, the point at the origin, real metres.</summary>
        internal sealed class ArrowPart {
            public Mesh Mesh = null!; public Material[] Materials = null!; public string Name = "";
            public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; public ArrowRole Role;
        }

        static List<ArrowPart>? parts;
        static float plainLength;

        static readonly int T_PlainArrow = Timing.Section("asset SkyfallLook.PlainArrow");
        /// <summary>The plain Arrow (56460)'s LOD0 parts, read once from its prefab (never instantiated; a client keeps its meshes and
        /// materials); null = not available (warned once, read again after a settings reload).</summary>
        internal static IReadOnlyList<ArrowPart>? PlainArrow() {
            if (partsDone || ApplicationManager.IsHeadless) return parts;
            using var _t = Timing.Begin(T_PlainArrow);
            partsDone = true;
            parts = ReadArrow(out plainLength);
            if (parts == null) {
                plainLength = 0f;
                if (!partsWarned) { partsWarned = true; MelonLogger.Warning("[Archery] Skyfall: the plain Arrow's meshes were not found; no giant arrows until the settings are reloaded"); }
            }
            else if (Logs.Summary) {
                var names = new string[parts.Count];
                for (int i = 0; i < names.Length; i++) names[i] = parts[i].Name;
                MelonLogger.Msg(Inv($"[Archery] Skyfall: giant arrows are the Arrow's {parts.Count} parts ({string.Join(", ", names)}), {plainLength:0.00} m (said once)"));
            }
            return parts;
        }

        /// <summary>The plain Arrow's length from its point to its nock, in metres (about 1.2); 0 when <see cref="PlainArrow"/> is null.</summary>
        internal static float PlainArrowLength => PlainArrow() != null ? plainLength : 0f;

        // The LOD0 renderers of the Arrow's LODGroup (else every MeshRenderer named ..._LOD0, else every MeshRenderer), each posed in
        // the prefab root's frame (lossyScale over the root's reproduces it: runtime-looks.md:166); then shifted so the point (the
        // farthest +z of the head's mesh bounds) is at the origin. Not activeInHierarchy: this is an asset, not a scene object.
        static List<ArrowPart>? ReadArrow(out float length) {
            length = 0f;
            NetworkPrefab? prefab = PrefabManager.GetPrefab(PlainArrowHash);
            if (prefab == null) return null;
            Transform root = prefab.transform;
            var renderers = new List<MeshRenderer>();
            LODGroup? lods = prefab.GetComponentInChildren<LODGroup>(true);
            if (lods != null) {
                LOD[] all = lods.GetLODs();
                if (all.Length > 0 && all[0].renderers != null)
#if QUEST
                    foreach (Renderer r in all[0].renderers) if (r.Is(out MeshRenderer mr) && mr != null) renderers.Add(mr);
#else
                    foreach (Renderer r in all[0].renderers) if (r is MeshRenderer mr && mr != null) renderers.Add(mr);
#endif
            }
            if (renderers.Count == 0)
                foreach (MeshRenderer mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr != null && mr.name.EndsWith("_LOD0", StringComparison.Ordinal)) renderers.Add(mr);
            if (renderers.Count == 0)
                foreach (MeshRenderer mr in prefab.GetComponentsInChildren<MeshRenderer>(true)) if (mr != null) renderers.Add(mr);

            var list = new List<ArrowPart>();
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            Vector3 rs = root.lossyScale;
            foreach (MeshRenderer mr in renderers) {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                Mesh? mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                Transform t = mr.transform;
                Vector3 ls = t.lossyScale;
                string lower = mesh.name.ToLowerInvariant();
                list.Add(new ArrowPart {
                    Mesh = mesh, Materials = mr.sharedMaterials, Name = mesh.name,   // the getter returns a copy of the array
                    Pos = root.InverseTransformPoint(t.position), Rot = inverse * t.rotation,
                    Scale = new Vector3(ls.x / (rs.x != 0f ? rs.x : 1f), ls.y / (rs.y != 0f ? rs.y : 1f), ls.z / (rs.z != 0f ? rs.z : 1f)),
                    Role = lower.Contains("spike") ? ArrowRole.Head : lower.Contains("feather") ? ArrowRole.Vane : ArrowRole.Shaft,
                });
            }
            if (list.Count == 0) return null;

            bool anyHead = list.Exists(p => p.Role == ArrowRole.Head);
            float point = float.NegativeInfinity, nock = float.PositiveInfinity;
            foreach (ArrowPart p in list) {
                Bounds b = p.Mesh.bounds;
                for (int c = 0; c < 8; c++) {
                    var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    float z = (p.Pos + p.Rot * Vector3.Scale(p.Scale, corner)).z;
                    if ((!anyHead || p.Role == ArrowRole.Head) && z > point) point = z;
                    if (z < nock) nock = z;
                }
            }
            if (!(point > nock) || float.IsInfinity(point) || float.IsInfinity(nock)) return null;
            foreach (ArrowPart p in list) p.Pos.z -= point;
            length = point - nock;
            return list;
        }

        static Material? rainSource, rainMat, trailMat;
        static bool trailFlat;

        static readonly int T_RainMaterial = Timing.Section("asset SkyfallLook.RainMaterial");
        /// <summary>The giant arrows' gold: a copy of the Etheral Arrow's 'SelectedPiece' (Legacy Shaders/Particles/Additive) tinted Gold at
        /// RainAlpha; else the flat line material in that colour (warned once); null when neither exists (no giant arrows). Made once per
        /// settings load; a new copy is put on every pooled arrow before the old one is destroyed.</summary>
        static Material? RainMaterial() {
            if (rainMatDone) return rainMat;
            using var _t = Timing.Begin(T_RainMaterial);
            rainMatDone = true;
            if (rainSource == null) rainSource = FindRainSource();
            Color gold = SkyfallSigil.Gold;
            var tint = new Color(gold.r, gold.g, gold.b, RainAlpha);
            Material? made = null;
            if (rainSource != null) {
                made = new Material(rainSource) { name = "SelectedPiece (Archery Skyfall gold)" };
                if (made.HasProperty("_TintColor")) made.SetColor("_TintColor", tint);
                else if (Volley.HeavyLook.Tint(made, tint) == "") { UnityEngine.Object.Destroy(made); made = null; }
            }
            if (made == null) {
                Material? flat = Fx.LineMaterial();
                if (flat != null) made = new Material(flat) { name = "Archery Skyfall gold (flat)", color = tint };
                if (!rainWarned) { rainWarned = true; MelonLogger.Warning("[Archery] Skyfall: the Etheral Arrow's material was not found; the giant arrows are flat gold"); }
            }
            Material? old = rainMat;
            rainMat = made;
            if (made != null) foreach (GiantArrow g in arrows) if (g.Alive) g.Paint(made);
            if (old != null && old != made) UnityEngine.Object.Destroy(old);
            return rainMat;
        }

        static Material? FindRainSource() {
            NetworkPrefab? etheral = PrefabManager.GetPrefab(EtheralArrowHash);
            if (etheral != null)
                foreach (MeshRenderer r in etheral.GetComponentsInChildren<MeshRenderer>(true))
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.name == RainSourceName) return r.sharedMaterial;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name == RainSourceName) return m;
            return null;
        }

        static readonly int T_TrailMaterial = Timing.Section("asset SkyfallLook.TrailMaterial");
        /// <summary>The giant arrows' trail: a copy of the Lightbeam's own 'Slashing_Spikes_Solid_m' (on 'Arrow Light Beam Effect', 1798,
        /// else any loaded material of that name), else of the flat line material. Its colours go through the LineRenderer. Once.</summary>
        static Material? TrailMaterial() {
            if (trailMatDone) return trailMat;
            using var _t = Timing.Begin(T_TrailMaterial);
            trailMatDone = true;
            Material? src = FindTrailSource();
            bool flat = src == null;
            if (flat && trailMat != null) return trailMat;                   // still only the flat one: its copy stays
            if (flat) src = Fx.LineMaterial();
            if (src == null) return trailMat;                                // nothing at all: the arrows have no trail
            Material? old = trailMat;
            trailMat = new Material(src) { name = src.name + " (Archery Skyfall trail)" };
            trailFlat = flat;
            foreach (GiantArrow g in arrows) if (g.Alive) g.Retrail(trailMat);
            if (old != null) UnityEngine.Object.Destroy(old);
            return trailMat;
        }

        static Material? FindTrailSource() {
            NetworkPrefab? beam = PrefabManager.GetPrefab(LightBeamHash);
            if (beam != null) {
                foreach (TrailRenderer r in beam.GetComponentsInChildren<TrailRenderer>(true))
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.name == TrailSourceName) return r.sharedMaterial;
                foreach (ParticleSystemRenderer r in beam.GetComponentsInChildren<ParticleSystemRenderer>(true)) {
                    if (r == null) continue;
                    if (r.sharedMaterial != null && r.sharedMaterial.name == TrailSourceName) return r.sharedMaterial;
                    if (r.trailMaterial != null && r.trailMaterial.name == TrailSourceName) return r.trailMaterial;
                }
            }
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name == TrailSourceName) return m;
            return null;
        }

        // ---------------------------------------------------------------- the host and the giant arrows (pooled)

        static GameObject? host;
        static readonly HashSet<string> fullSaid = new HashSet<string>();

        static Transform Host() {
            if (host == null) {
                host = new GameObject("ArcherySkyfallLook");
                UnityEngine.Object.DontDestroyOnLoad(host);
                SkyfallSigil.Layer(host);
            }
            return host.transform;
        }

        static void Full(string pool, int cap) {
            if (fullSaid.Add(pool) && Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: the {pool} pool is full ({cap}); a look is skipped (said once)");
        }

        /// <summary>One giant arrow: a root (scaled to its length) with one child per Arrow part on the gold material, and a trail child.</summary>
        sealed class GiantArrow {
            readonly GameObject root;
            readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
            readonly List<int> slots = new List<int>();
            readonly LineRenderer? trail;
            Material? painted;

            internal bool Busy { get; private set; }
            internal bool Alive => root != null;

            internal GiantArrow(Transform parent, IReadOnlyList<ArrowPart> parts) {
                root = new GameObject("ArcherySkyfallArrow");
                SkyfallSigil.Layer(root);
                root.transform.SetParent(parent, false);
                foreach (ArrowPart p in parts) {
                    var go = new GameObject(p.Name);
                    SkyfallSigil.Layer(go);
                    Transform t = go.transform;
                    t.SetParent(root.transform, false);
                    t.localPosition = p.Pos;
                    t.localRotation = p.Rot;
                    t.localScale = p.Scale;
                    go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    renderers.Add(mr);
                    slots.Add(Math.Max(1, p.Mesh.subMeshCount));
                }
                Material? tm = TrailMaterial();
                if (tm != null) {
                    var tg = new GameObject("trail");
                    SkyfallSigil.Layer(tg);
                    tg.transform.SetParent(root.transform, false);
                    LineRenderer lr = Fx.NewLine(tg, 2, RainTrailWidth, true);
                    lr.sharedMaterial = tm;
                    lr.textureMode = LineTextureMode.Stretch;
                    lr.enabled = false;
                    trail = lr;
                }
                root.SetActive(false);
            }

            /// <summary>Every part on <paramref name="gold"/> (one per submesh).</summary>
            internal void Paint(Material gold) {
                if (painted == gold) return;
                painted = gold;
                for (int i = 0; i < renderers.Count; i++) {
                    if (renderers[i] == null) continue;
                    var mats = new Material[slots[i]];
                    for (int k = 0; k < mats.Length; k++) mats[k] = gold;
                    renderers[i].sharedMaterials = mats;
                }
            }

            internal void Retrail(Material m) { if (trail != null) trail.sharedMaterial = m; }

            internal void Take(Material gold, float scale, float width) {
                Busy = true;
                Paint(gold);
                root.transform.localScale = new Vector3(scale, scale, scale);
                if (trail != null) {
                    trail.widthMultiplier = width;
                    Color pale = SkyfallSigil.GoldPale;
                    trail.startColor = new Color(pale.r, pale.g, pale.b, 0.9f);
                    trail.endColor = new Color(pale.r, pale.g, pale.b, 0f);
                    trail.enabled = false;
                }
                if (!root.activeSelf) root.SetActive(true);
            }

            /// <summary>The point at <paramref name="point"/>, flying along <paramref name="dir"/> (unit); the trail from the nock (length
            /// metres behind the point) back along the way it came, as far as it has <paramref name="travelled"/>, at most RainTrailMetres.</summary>
            internal void Place(Vector3 point, Vector3 dir, float length, float travelled) {
                Transform t = root.transform;
                t.position = point;
                t.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
                if (trail == null) return;
                float back = Mathf.Min(travelled, RainTrailMetres);
                bool on = back > 0.01f;
                if (trail.enabled != on) trail.enabled = on;
                if (!on) return;
                Vector3 nock = point - dir * length;
                trail.SetPosition(0, nock);
                trail.SetPosition(1, nock - dir * back);
            }

            internal void Release() {
                Busy = false;
                if (root != null && root.activeSelf) root.SetActive(false);
            }
        }

        static readonly List<GiantArrow> arrows = new List<GiantArrow>();

        static readonly int T_TakeArrow = Timing.Section("look SkyfallLook.TakeArrow (take or make a giant arrow)");
        /// <summary>A giant arrow from the pool (a free one, else a new one) at <paramref name="scale"/> times the plain Arrow, its trail
        /// <paramref name="width"/> wide; null without the Arrow or the gold, or when the pool is full (said once).</summary>
        static GiantArrow? TakeArrow(float scale, float width) {
            using var _t = Timing.Begin(T_TakeArrow);
            IReadOnlyList<ArrowPart>? p = PlainArrow();
            Material? gold = RainMaterial();
            if (p == null || gold == null) return null;
            GiantArrow? a = null;
            foreach (GiantArrow x in arrows) if (!x.Busy && x.Alive) { a = x; break; }
            if (a == null) {
                arrows.RemoveAll(x => !x.Alive);
                if (arrows.Count >= ArrowCap) { Full("giant arrow", ArrowCap); return null; }
                a = new GiantArrow(Host(), p);
                arrows.Add(a);
            }
            a.Take(gold, scale, width);
            return a;
        }

        // the idle warm-up (final review I-2): one more giant arrow built into the pool, taken and released at once (never shown), until
        // it holds SkyfallPools.WarmArrows (a wave and the streak); true when it does, or cannot (no Arrow or no gold)
        static bool PrebuildArrow() {
            IReadOnlyList<ArrowPart>? p = PlainArrow();
            Material? gold = RainMaterial();
            if (p == null || gold == null) return true;
            arrows.RemoveAll(x => !x.Alive);
            if (arrows.Count >= SkyfallPools.WarmArrows) return true;
            var a = new GiantArrow(Host(), p);
            arrows.Add(a);
            a.Take(gold, 1f, RainTrailWidth);
            a.Release();
            return arrows.Count >= SkyfallPools.WarmArrows;
        }

        // ---------------------------------------------------------------- the effects helpers (Task 12 uses them too)

        static readonly int T_GetScaledParts = Timing.Section("look SkyfallLook.GetScaledParts");
        /// <summary>One of the game's pooled effects at <paramref name="k"/> times its size: the root scaled as Fx.GetScaled, and also every
        /// child ParticleSystem that scales LOCALLY (those ignore their parents' scale). Every original is put back when the pooled object
        /// returns to its pool. False for a null definition, an empty pool or on the headless server.</summary>
        internal static bool GetScaledParts(PooledObjectDefinition? def, Vector3 at, Quaternion rot, float k) {
            using var _t = Timing.Begin(T_GetScaledParts);
            if (ApplicationManager.IsHeadless || def == null) return false;
            PooledObject got = def.Get(at, rot);
            if (got == null) return false;
            Transform tr = got.transform;
            Vector3 original = tr.localScale;
            tr.localScale = original * k;
            var locals = new List<Transform>();
            var scales = new List<Vector3>();
            foreach (ParticleSystem ps in got.GetComponentsInChildren<ParticleSystem>(true)) {
                if (ps == null || ps.transform == tr || ps.main.scalingMode != ParticleSystemScalingMode.Local) continue;
                Transform pt = ps.transform;
                locals.Add(pt);
                scales.Add(pt.localScale);
                pt.localScale = pt.localScale * k;
            }
            PoolChangedHandler? restore = null;
#if QUEST
            restore = DelegateSupport.ConvertDelegate<PoolChangedHandler>(new Action<PooledObject>(p => {
                if (p != null) p.transform.localScale = original;
                for (int i = 0; i < locals.Count; i++) if (locals[i] != null) locals[i].localScale = scales[i];
                got.remove_Released(restore);
            }));
            got.add_Released(restore);
#else
            restore = p => {
                if (p != null) p.transform.localScale = original;
                for (int i = 0; i < locals.Count; i++) if (locals[i] != null) locals[i].localScale = scales[i];
                got.Released -= restore;
            };
            got.Released += restore;
#endif
            return true;
        }

        // ---------------------------------------------------------------- scorches

        sealed class ScorchMark { public readonly SkyfallSigil.Quad?[] Pieces = new SkyfallSigil.Quad?[5]; public float Born; }   // 0 splatter, 1-3 claws, 4 glow
        static readonly List<ScorchMark> scorches = new List<ScorchMark>();
        static readonly Color[] stops = new Color[4];

        static readonly int T_Scorch = Timing.Section("look SkyfallLook.Scorch");
        /// <summary>A scorch flat on the ground at <paramref name="at"/> (at.y + 0.04): a splatter, three claw strokes and a glow from
        /// SkyfallSigil's loose pieces, <paramref name="size"/> metres across, turned by <paramref name="seed"/>. It cools from fire to char
        /// over ScorchCoolSeconds and fades over the last 3 of ScorchSeconds. At most 40: the oldest goes first.</summary>
        internal static void Scorch(Vector3 at, float size, uint seed) {
            using var _t = Timing.Begin(T_Scorch);
            if (ApplicationManager.IsHeadless || !Finite(at) || !(size > 0f) || float.IsInfinity(size)) return;
            while (scorches.Count >= ScorchCap) { ReleaseScorch(scorches[0]); scorches.RemoveAt(0); }
            var m = new ScorchMark { Born = Time.time };
            float y = at.y + 0.04f;
            var mid = new Vector3(at.x, y, at.z);
            float turn = 360f * LookSeed.Unit(seed, 3, 51);
            m.Pieces[0] = Flat(SkyfallSigil.Piece(SkyfallSigil.Tex.Combat, 4, 7, SkyfallSigil.Blend.Alpha), mid, turn, size);
            for (int k = 0; k < 3; k++) {
                float th = 120f * k + 40f * (LookSeed.Unit(seed, k, 51) - 0.5f);
                float rad = th * Mathf.Deg2Rad, r = 0.15f * size;
                var p = new Vector3(at.x + Mathf.Sin(rad) * r, y, at.z + Mathf.Cos(rad) * r);
                m.Pieces[1 + k] = Flat(SkyfallSigil.Piece(SkyfallSigil.Tex.Slash, 0, 0, SkyfallSigil.Blend.Alpha), p, th, 0.9f * size);
            }
            m.Pieces[4] = Flat(SkyfallSigil.Piece(SkyfallSigil.Tex.Combat, 4, 7, SkyfallSigil.Blend.Add), mid, turn, 1.1f * size);
            scorches.Add(m);
            FillStops();
            PaintScorch(m, 0f);                                             // a reused piece never shows its last colour
        }

        // flat on the ground, turned th degrees about up (Task 10's geometry: the quad's XY plane laid into XZ)
        static SkyfallSigil.Quad? Flat(SkyfallSigil.Quad? q, Vector3 pos, float th, float side) {
            q?.Place(pos, Quaternion.AngleAxis(th, Vector3.up) * Quaternion.Euler(90f, 0f, 0f), side);
            return q;
        }

        static void FillStops() {
            stops[0] = SkyfallSigil.FireHot; stops[1] = SkyfallSigil.Fire; stops[2] = SkyfallSigil.FireDeep; stops[3] = SkyfallSigil.Char;
        }

        static void PaintScorch(ScorchMark m, float age) {
            float cool = age / ScorchCoolSeconds;
            int i = SkyfallCurves.RampStop(cool, out float blend);
            Color c = Color.Lerp(stops[i], stops[i + 1], blend);
            float a = SkyfallCurves.Envelope(age, ScorchSeconds, 0.1f, 3f);
            c.a = a;
            for (int k = 0; k < 4; k++) m.Pieces[k]?.Paint(c);
            Color glow = stops[0];
            glow.a = SkyfallCurves.Glow(cool) * a;
            m.Pieces[4]?.Paint(glow);
        }

        static void ReleaseScorch(ScorchMark m) {
            for (int k = 0; k < m.Pieces.Length; k++) { m.Pieces[k]?.Release(); m.Pieces[k] = null; }
        }

        static void ScorchFrame(float now) {
            if (scorches.Count == 0) return;
            FillStops();
            float life = ScorchSeconds;
            for (int i = scorches.Count - 1; i >= 0; i--) {
                ScorchMark m = scorches[i];
                float age = now - m.Born;
                if (age >= life || !(age >= 0f)) { ReleaseScorch(m); scorches.RemoveAt(i); continue; }
                PaintScorch(m, age);
            }
        }

        // ---------------------------------------------------------------- one storm's look

        sealed class Landing { public float GroundY = float.NaN; public bool Landed, Tried; public GiantArrow? Arrow; }

        sealed class Run {
            public SkyStorm S = null!;
            public bool Caster;
            public float NailLand, Ends, FizzledAt = -1f, TwinY = float.NaN;
            public int ArrowsPerWave = 1, PerWave = 1;
            public Vector3 Centre, Release;
            public SkyfallSigil.Sigil? Sky, Twin;
            public Landing[] L = new Landing[0];
            public GiantArrow? Streak;
            public bool StreakTried, StreakDone, Flashed, TwinStarted, Puffed;
            public int Explosions, SparksOnly, NoGround;
        }

        static readonly Dictionary<uint, Run> runs = new Dictionary<uint, Run>();
        static readonly List<uint> scratch = new List<uint>();

        static readonly int T_Start = Timing.Section("look SkyfallLook.Start");
        /// <summary>Skyfall.Begin on a client: a storm's look starts (a fizzle too: its core and puff). Nothing for a fizzle whose centre is
        /// unknown, a storm already drawn here, or a spectator's storm farther than LookMaxMetres from this player's head.</summary>
        internal static void Start(SkyStorm s, bool caster) {
            using var _t = Timing.Begin(T_Start);
            if (ApplicationManager.IsHeadless || lookFailed || s == null || double.IsNaN(s.CX)) return;
            try {
                if (runs.ContainsKey(s.ArrowId)) {
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: storm {s.ArrowId} is already drawn here");
                    return;
                }
                var centre = new Vector3((float)s.CX, (float)s.CY, (float)s.CZ);
                if (!Finite(centre)) return;
                if (!caster) {
#if QUEST
#if QUEST
                    Vector3? head = Targets.HeadOf(Il2Cpp.Player.Current?.TryCast<Il2Cpp.Player>());
#else
                    Vector3? head = Targets.HeadOf(Il2Cpp.Player.Current as Il2Cpp.Player);
#endif
#else
                    Vector3? head = Targets.HeadOf(global::Player.Current as global::Player);
#endif
                    float max = LookMaxMetres;
                    if (head.HasValue && (head.Value - centre).sqrMagnitude > max * max) {
                        if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: storm {s.ArrowId} is {(head.Value - centre).magnitude:0} m away (LookMaxMetres {max:0}); not drawn here"));
                        return;
                    }
                }
                SkyfallValues v = Skyfall.Values;
                bool fizzled = s.Code == SkyCode.Fizzled;
                float nail = SkyfallPattern.NailLandAt(v), lastLand = 0f;
                var r = new Run {
                    S = s, Caster = caster, NailLand = nail, Centre = centre, Release = V(s.Release),
                    ArrowsPerWave = Math.Max(1, v.ArrowsPerWave),
                    PerWave = Math.Max(1, (v.MaxLiveExplosions + v.Waves - 1) / v.Waves),   // ceil without float: every wave lands at once
                    L = new Landing[s.Impacts.Length],
                };
                // the ground: SkyfallServer's bounds (Task 9), so a landing's look sits where the server's blast is. Only the twin's is
                // cast here; each landing's is cast as its arrow starts to fall (final review I-2: not 25 casts in the release's frame)
                if (!fizzled) r.TwinY = Ground(s.CX, s.CZ, s.CY + 0.5, s.Release.Y - Skyfall.GroundBelowMetres);
                for (int i = 0; i < s.Impacts.Length; i++) {
                    r.L[i] = new Landing();
                    if (s.Impacts[i].LandAt > lastLand) lastLand = s.Impacts[i].LandAt;
                }
                r.Ends = Math.Max(lastLand + 0.5f, nail + SkyHoldSeconds + SkyFadeSeconds);
                r.Sky = SkyfallSigil.Build(SkyfallSigil.Style.Sky, s.ArrowId);
                r.Sky?.Place(centre, Quaternion.identity, v.SigilRadius);
                if (!fizzled) r.Twin = SkyfallSigil.Build(SkyfallSigil.Style.Ground, s.ArrowId);
                if (fizzled) r.FizzledAt = StreakSeconds;
                runs[s.ArrowId] = r;
                if (!fizzled) SkyfallSky.Dim(s.ArrowId, centre, s.StartTime, s.StartTime + nail + 1f + v.SkyFadeSeconds, 1f);
                if (Finite(r.Release)) VolleyFx.Play(Name(releaseSound, "Lightbeam Shot Effect Sounds"), r.Release);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: storm {s.ArrowId} drawn here ({(caster ? "mine" : "a spectator's")}, {s.Code}, {s.Impacts.Length} landings)");
                SkyfallNail.Start(s);                                        // after the run is recorded, so its shatter can take the sky sigil (Task 12)
            }
            catch (Exception e) { Failed(e); }
        }

        /// <summary>That storm puffs out now (from StreakSeconds at the earliest): its rain arrows and twin go at once and its sky dim is
        /// released; its core puffs out over FizzleSeconds. Nothing for a storm not drawn here or already fizzling.</summary>
        internal static void Fizzle(uint arrowId) {
            if (!runs.TryGetValue(arrowId, out Run r) || r.FizzledAt >= 0f) return;
            r.FizzledAt = Math.Max(Time.time - r.S.StartTime, StreakSeconds);
            foreach (Landing l in r.L) { l.Arrow?.Release(); l.Arrow = null; }
            r.Twin?.Release(); r.Twin = null;
            SkyfallNail.Stop(arrowId);                                       // a fizzled storm has no World-Nail
            SkyfallSky.Undim(arrowId);
        }

        /// <summary>The storm's sky sigil, handed over (Task 12's shatter): this look no longer places, shows or releases it. Null when the
        /// storm is not drawn here or its sigil is gone.</summary>
        internal static SkyfallSigil.Sigil? TakeSkySigil(uint arrowId) {
            if (!runs.TryGetValue(arrowId, out Run r)) return null;
            SkyfallSigil.Sigil? s = r.Sky;
            r.Sky = null;
            return s;
        }

        // ---------------------------------------------------------------- the frame

        static float playerSince = -1f, lastTakenAt = -1f;
        static bool explodedThisFrame, spareSound;
        static Vector3 spareSoundAt;

        // the explosions this machine started lately (any storm), so none starts within MinImpactSpacing of one still alive
        static readonly Vector3[] ringAt = new Vector3[RingCap];
        static readonly float[] ringTime = NewRing();
        static int ringNext;
        static float[] NewRing() { var a = new float[RingCap]; for (int i = 0; i < a.Length; i++) a[i] = float.NegativeInfinity; return a; }

        static bool NearAnExplosion(Vector3 at, float now, float gap, float life) {
            float g2 = gap * gap;
            for (int i = 0; i < RingCap; i++) {
                if (!(now - ringTime[i] < life)) continue;
                float dx = ringAt[i].x - at.x, dz = ringAt[i].z - at.z;
                if (dx * dx + dz * dz < g2) return true;
            }
            return false;
        }

        static void Remember(Vector3 at, float now) {
            ringAt[ringNext] = at;
            ringTime[ringNext] = now;
            ringNext = (ringNext + 1) % RingCap;
        }

        // final review I-2: a landing's explosion and scorch start at most one of each a frame, in the order they landed (SkyfallPacer,
        // tested). The game makes a new 'Explosion' (eleven objects, eight particle systems) when its pool is empty, and a wave is 8
        // landings at once; 2-4 frames still read as together. What is decided (budget, spacing, the bright gate) is decided at the
        // landing; only the start waits.
        struct Boom { public PooledObjectDefinition Def; public Vector3 At; public float Scale; }
        struct ScorchCall { public Vector3 At; public float Size; public uint Seed; }
        static readonly SkyfallPacer<Boom> booms = new SkyfallPacer<Boom>();
        static readonly SkyfallPacer<ScorchCall> scorchCalls = new SkyfallPacer<ScorchCall>();
        static readonly List<Boom> boomScratch = new List<Boom>();
        static readonly List<ScorchCall> scorchScratch = new List<ScorchCall>();

        static readonly int T_Paced = Timing.Section("look SkyfallLook.Paced (one explosion and one scorch a frame)");
        static void Paced(float now) {
            if (booms.Count == 0 && scorchCalls.Count == 0) return;
            using var _t = Timing.Begin(T_Paced);
            boomScratch.Clear();
            booms.Take(now, 1, boomScratch);
            foreach (Boom b in boomScratch) if (b.Def != null) Fx.GetScaled(b.Def, b.At, Quaternion.identity, b.Scale);
            scorchScratch.Clear();
            scorchCalls.Take(now, 1, scorchScratch);
            foreach (ScorchCall c in scorchScratch) Scorch(c.At, c.Size, c.Seed);
        }

        /// <summary>Main.OnLateUpdate (clients): every storm drawn, the scorches cooled, the caster's storms fizzled after a take. While
        /// nothing runs, one lookup of the look's assets per idle frame (once per settings load, 5 s after the player is set), so no asset
        /// search runs in a release's frame. No player (a disconnect): everything released.</summary>
        internal static void Tick() {
            if (ApplicationManager.IsHeadless) return;
#if QUEST
            var me = Il2Cpp.Player.Current;
#else
            var me = global::Player.Current;
#endif
#if QUEST
            if (me == null || (me.Is(out UnityEngine.Object o) && o == null)) {
                playerSince = -1f;
#else
            if (me == null || (me is UnityEngine.Object o && o == null)) {
                playerSince = -1f;
#endif
                if (runs.Count > 0 || scorches.Count > 0) Clear();
                return;
            }
            float now = Time.time;
            if (playerSince < 0f) playerSince = now;
            try {
                Takes();                                                     // tracked every frame, so a take is only ever seen when it comes
                if (lookFailed) return;
                if (runs.Count == 0) {
                    if (warmStep >= 0 && now - playerSince >= WarmAfterSeconds) WarmStep();
                    Paced(now);
                    ScorchFrame(now);
                    return;
                }
                SkyfallValues v = Skyfall.Values;
                explodedThisFrame = spareSound = false;
                scratch.Clear();
                foreach (var kv in runs) scratch.Add(kv.Key);
                foreach (uint id in scratch) {
                    if (!runs.TryGetValue(id, out Run r)) continue;
                    if (!Frame(r, now, v)) continue;
                    ReleaseRun(r);
                    runs.Remove(id);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: storm {id} look done: {r.Explosions} explosions, {r.SparksOnly} sparks only, {r.NoGround} without ground");
                }
                if (spareSound && !explodedThisFrame) VolleyFx.Play(Name(overBudgetSound, "Small Explosion"), spareSoundAt);
                Paced(now);
                ScorchFrame(now);
            }
            catch (Exception e) { Failed(e); }
        }

        // the asset lookups, one per idle frame (each is cached; a FindObjectsOfTypeAll costs a frame some milliseconds); then the pools a
        // first storm takes in its first frames, built ahead one item per idle frame (final review I-2)
        static void WarmStep() {
            switch (warmStep) {
                case 0: SkyfallSigil.Warm(); break;
                case 1: PlainArrow(); break;
                case 2: RainMaterial(); break;
                case 3: TrailMaterial(); break;
                case 4: Fx.FindEffect(ExplosionName); break;
                case 5: Fx.FindEffect(Name(fizzlePuff, "Large Smoke Puff")); break;
                case 6: Fx.FindSound(Name(releaseSound, "Lightbeam Shot Effect Sounds")); break;
                case 7: Fx.FindSound(Name(sigilSound, "Shrine Activate Sounds")); break;
                case 8: Fx.FindSound(Name(twinSound, "Shockwave Sounds")); break;
                case 9: Fx.FindSound(Name(overBudgetSound, "Small Explosion")); break;
                case 10: Fx.FindSound(Name(fizzleSound, "Small Explosion")); break;
                case 11: SkyfallNail.Warm(); break;                         // the World-Nail's gold head and dome (Task 12)
                case 12: VolleyFx.Sparks.Warm(); break;                     // the shared spark emitter and its material search
                case 13: if (!Prebuild()) return; break;                    // stays on this step, one item a frame, until every pool is built
                default: warmStep = -1; return;                              // done until the settings are reloaded
            }
            warmStep++;
        }

        static int prebuildPhase;
        static readonly int T_Prebuild = Timing.Section("look SkyfallLook.Prebuild (one pooled item ahead, idle)");
        // one item of the next pool short of its SkyfallPools.Warm* size (each built into its pool, taken and released at once, never
        // shown); true when all are there. A pool that cannot be built (art missing, a look failed) is passed.
        static bool Prebuild() {
            using var _t = Timing.Begin(T_Prebuild);
            bool done;
            switch (prebuildPhase) {
                case 0: done = SkyfallSigil.Prebuild(SkyfallSigil.Style.Sky, SkyfallPools.WarmSky); break;
                case 1: done = SkyfallSigil.Prebuild(SkyfallSigil.Style.Ground, SkyfallPools.WarmGround); break;
                case 2: done = SkyfallSigil.Prebuild(SkyfallSigil.Style.Hold, SkyfallPools.WarmHold); break;
                case 3: done = SkyfallSigil.Prebuild(SkyfallSigil.Style.NailBand, SkyfallPools.WarmNailBands); break;
                case 4: done = PrebuildArrow(); break;
                case 5: done = SkyfallSigil.PrebuildLoose(SkyfallPools.WarmLoose, SkyfallPools.LoosePerFrame); break;
                case 6: done = SkyfallNail.Prebuild(); break;
                default:
                    if (Logs.Debug) MelonLogger.Msg("[Archery] Skyfall: the look's pools are built ahead (said once a settings load)");
                    return true;
            }
            if (done) prebuildPhase++;
            return false;
        }

        // a take reached this game: each of its own storms released within TakenWindowSeconds before it fizzles (the server may have had the
        // take before the shot and refused it); an older one was surely accepted, and spectators see it run, so it runs here too
        static void Takes() {
            float taken = Skyfall.LocalTakenAt;
            if (taken == lastTakenAt) return;
            lastTakenAt = taken;
            if (taken < 0f || runs.Count == 0) return;                       // < 0: no take (left the server)
            scratch.Clear();
            foreach (var kv in runs)
                if (kv.Value.Caster && kv.Value.FizzledAt < 0f && taken - kv.Value.S.StartTime <= TakenWindowSeconds) scratch.Add(kv.Key);
            foreach (uint id in scratch) {
                if (Logs.Summary) MelonLogger.Msg(Inv($"[Archery] Skyfall: storm {id} fizzles here: Skyfall was taken {taken - runs[id].S.StartTime:0.00} s after the release"));
                Fizzle(id);
            }
        }

        // one storm's frame; true when it is over
        static bool Frame(Run r, float now, SkyfallValues v) {
            SkyStorm s = r.S;
            float t = now - s.StartTime;
            if (r.FizzledAt >= 0f && t >= r.FizzledAt) return FizzleFrame(r, t, v);
            bool fizzling = r.FizzledAt >= 0f;                                // decided, not yet due: the streak and the core only

            // the streak: a gold ghost of the plain Arrow from the release up into the sigil, slowing as it arrives
            if (!r.StreakDone) {
                float ss = StreakSeconds;
                if (!(ss > 0f) || t >= ss || !Finite(r.Release)) { r.StreakDone = true; r.Streak?.Release(); r.Streak = null; }
                else {
                    float plain = PlainArrowLength;
                    if (r.Streak == null && !r.StreakTried && plain > 0f) {
                        r.StreakTried = true;
                        r.Streak = TakeArrow(1f, RainTrailWidth * plain / v.ArrowLength);
                    }
                    Vector3 way = r.Centre - r.Release;
                    if (r.Streak != null && way.sqrMagnitude > 1e-4f) {
                        Vector3 p = r.Release + way * (1f - SkyfallCurves.Fall(1f - t / ss));
                        r.Streak.Place(p, way.normalized, plain, (p - r.Release).magnitude);
                    }
                }
            }

            // the sky sigil: unfolds core first; fades after the Nail has landed unless Task 12 took it
            if (r.Sky != null) {
                float fadeFrom = r.NailLand + SkyHoldSeconds;
                float a = t < fadeFrom ? 1f : 1f - SkyfallCurves.Ease((t - fadeFrom) / SkyFadeSeconds);
                if (a <= 0f) { r.Sky.Release(); r.Sky = null; }
                else {
                    r.Sky.Place(r.Centre, Quaternion.identity, v.SigilRadius);
                    r.Sky.Show(t, v.BuildSeconds, a, fizzling ? 0b1 : ~0);
                }
            }

            if (fizzling) return false;

            // the ghost reaches the sigil: its sound and the storm's first light (never for a fizzle; no light as a fourth bright
            // moment within a second on this machine, Bright)
            if (!r.Flashed && t >= StreakSeconds) {
                r.Flashed = true;
                VolleyFx.Play(Name(sigilSound, "Shrine Activate Sounds"), r.Centre);
                if (Bright(now)) Fx.Flash(r.Centre, SkyfallSigil.GoldPale, G(flashBrightness, 3f, 0f, 20f), G(flashRange, 40f, 0f, 500f), FlashSeconds);
            }

            // the twin burns into the ground under the centre, and burns away when the Nail lands
            if (r.Twin != null) {
                float delay = TwinDelaySeconds;
                if (t >= delay) {
                    if (!r.TwinStarted) {
                        r.TwinStarted = true;
                        if (float.IsNaN(r.TwinY)) r.TwinY = Ground(s.CX, s.CZ, s.CY + 0.5, s.Release.Y - Skyfall.GroundBelowMetres);   // once more
                        if (!float.IsNaN(r.TwinY)) VolleyFx.Play(Name(twinSound, "Shockwave Sounds"), new Vector3(r.Centre.x, r.TwinY, r.Centre.z));
                    }
                    float a = t < r.NailLand ? 1f : 1f - SkyfallCurves.Ease((t - r.NailLand) / TwinBurnSeconds);
                    if (float.IsNaN(r.TwinY) || a <= 0f) { r.Twin.Release(); r.Twin = null; }   // no ground: no twin
                    else {
                        r.Twin.Place(new Vector3(r.Centre.x, r.TwinY + 0.12f, r.Centre.z), Quaternion.identity, SkyfallStrike.KillArea(v));   // the storm's kill area
                        r.Twin.Show(t - delay, v.BuildSeconds - delay, a);
                    }
                }
            }

            // the rain: each giant arrow falls in its last FallSeconds; landings in index order, all in this frame
            float fall = v.FallSeconds, fan = RainFan, rainScale = PlainArrowLength > 0f ? v.ArrowLength / PlainArrowLength : 0f;
            for (int i = 0; i < r.L.Length; i++) {
                Landing l = r.L[i];
                if (l.Landed) continue;
                SkyImpact imp = s.Impacts[i];
                if (t >= imp.LandAt) { Land(r, l, imp, t, now, v); continue; }
                if (imp.Nail || t < imp.LandAt - fall) continue;                // the Nail is Task 12's
                if (!l.Tried) {
                    l.Tried = true;
                    if (float.IsNaN(l.GroundY)) l.GroundY = Ground(imp.X, imp.Z, s.CY + 0.5, s.Release.Y - Skyfall.GroundBelowMetres);   // its ground, as it starts to fall
                    if (rainScale > 0f) l.Arrow = TakeArrow(rainScale, RainTrailWidth);
                }
                if (l.Arrow == null) continue;
                var start = new Vector3((float)(s.CX + (imp.X - s.CX) * fan), (float)s.CY, (float)(s.CZ + (imp.Z - s.CZ) * fan));
                var end = new Vector3((float)imp.X, float.IsNaN(l.GroundY) ? r.Release.y : l.GroundY, (float)imp.Z);
                Vector3 way = end - start;
                if (way.sqrMagnitude < 1e-4f) way = Vector3.down * 1e-2f;
                Vector3 p = start + way * SkyfallCurves.Fall((t - (imp.LandAt - fall)) / fall);
                l.Arrow.Place(p, way.normalized, v.ArrowLength, (p - start).magnitude);
            }
            return t >= r.Ends;
        }

        // one landing: its arrow goes; with ground and in time, the explosion share (or sparks), then the scorch. The explosion and the
        // scorch start through the pacers (at most one of each a frame); a wave's explosions are one bright moment (Bright), and as a
        // fourth within a second on this machine they show as sparks
        static void Land(Run r, Landing l, SkyImpact imp, float t, float now, SkyfallValues v) {
            l.Landed = true;
            l.Arrow?.Release();
            l.Arrow = null;
            if (imp.Nail) return;                                            // the World-Nail's landing is SkyfallNail's (Task 12)
            if (t - imp.LandAt > LateSeconds) return;                        // a hitch or a late start: no effects
            if (!l.Tried) {                                                  // never seen falling (a hitch): its ground now
                l.Tried = true;
                l.GroundY = Ground(imp.X, imp.Z, r.S.CY + 0.5, r.S.Release.Y - Skyfall.GroundBelowMetres);
            }
            if (float.IsNaN(l.GroundY)) { r.NoGround++; return; }
            var at = new Vector3((float)imp.X, l.GroundY, (float)imp.Z);
            bool exploded = false;
            if (imp.Index % r.ArrowsPerWave < r.PerWave) {
                PooledObjectDefinition? def = Fx.FindEffect(ExplosionName);
                float life = ExplosionLifeSeconds;
                if (def != null && !NearAnExplosion(at, now, v.MinImpactSpacing, life) && flashGate.Allows(now) && Budget.TryTake(now, life)
                    && booms.Add(new Boom { Def = def, At = at, Scale = ExplosionScale }, now)) {
                    flashGate.TryTake(now);
                    exploded = explodedThisFrame = true;
                    Remember(at, now);
                    r.Explosions++;
                }
            }
            if (!exploded) {
                VolleyFx.Sparks.Emit(at + Vector3.up * 0.3f, Vector3.up, ImpactSparks, SkyfallSigil.FireHot, 70f, 9f, 4f);
                if (!spareSound) { spareSound = true; spareSoundAt = at; }
                r.SparksOnly++;
            }
            scorchCalls.Add(new ScorchCall { At = at, Size = ScorchSize, Seed = r.S.ArrowId ^ (uint)imp.Index }, now);
        }

        // a fizzle due: everything but the sky sigil's core goes; the core swells a little and fades over FizzleSeconds; true when over
        static bool FizzleFrame(Run r, float t, SkyfallValues v) {
            if (!r.Puffed) {
                r.Puffed = true;
                r.Streak?.Release(); r.Streak = null; r.StreakDone = true;
                foreach (Landing l in r.L) { l.Arrow?.Release(); l.Arrow = null; }
                r.Twin?.Release(); r.Twin = null;
                GetScaledParts(Fx.FindEffect(Name(fizzlePuff, "Large Smoke Puff")), r.Centre, Quaternion.identity, 2f);
                VolleyFx.Sparks.Emit(r.Centre, Vector3.up, 30, SkyfallSigil.Gold, 85f, 6f, 3f);
                VolleyFx.Play(Name(fizzleSound, "Small Explosion"), r.Centre);
            }
            float fs = FizzleSeconds, since = t - r.FizzledAt;
            if (r.Sky != null) {
                float e = fs > 0f ? SkyfallCurves.Ease(since / fs) : 1f;
                if (e >= 1f) { r.Sky.Release(); r.Sky = null; }
                else {
                    r.Sky.Place(r.Centre, Quaternion.identity, v.SigilRadius * (1f + FizzleSwell * e));
                    r.Sky.Show(t, v.BuildSeconds, 1f - e, 0b1);
                }
            }
            return since >= fs;
        }

        static void ReleaseRun(Run r) {
            r.Sky?.Release(); r.Sky = null;
            r.Twin?.Release(); r.Twin = null;
            r.Streak?.Release(); r.Streak = null;
            foreach (Landing l in r.L) { l.Arrow?.Release(); l.Arrow = null; }
        }

        /// <summary>Every storm, scorch and giant arrow released, and the sky put back (no player, or the look failed).</summary>
        internal static void Clear() {
            foreach (var kv in runs) ReleaseRun(kv.Value);
            runs.Clear();
            foreach (ScorchMark m in scorches) ReleaseScorch(m);
            scorches.Clear();
            booms.Clear();
            scorchCalls.Clear();
            foreach (GiantArrow g in arrows) if (g.Busy) g.Release();
            SkyfallNail.Clear();
            SkyfallSky.Restore();
        }

        static void Failed(Exception e) {
            if (lookFailed) return;
            lookFailed = true;
            try { Clear(); } catch { }
            MelonLogger.Warning($"[Archery] Skyfall: the storm could not be shown ({e.Message}); storms are not drawn here until the settings are reloaded");
        }

        /// <summary>SelfCheck (0 when headless): the fall, the scorch's glow and its colour stop at 5 points. Throws on a number that is
        /// not one; returns the count evaluated.</summary>
        internal static int Check() {
            if (ApplicationManager.IsHeadless) return 0;
            int n = 0;
            for (int i = 0; i <= 4; i++) {
                float u = i * 0.25f;                                         // 0, 0.25, 0.5, 0.75, 1
                float f = SkyfallCurves.Fall(u), g = SkyfallCurves.Glow(u);
                int k = SkyfallCurves.RampStop(u, out float b);
                if (float.IsNaN(f) || float.IsNaN(g) || float.IsNaN(b) || k < 0 || k > 2) throw new InvalidOperationException("a Skyfall storm look number is not a number");
                n += 3;
            }
            return n;
        }
    }
}
