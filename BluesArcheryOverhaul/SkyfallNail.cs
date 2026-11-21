using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Skyfall's finale (Task 12): the World-Nail and the sky sigil's shatter. Looks only, clients only; the server decides every hit
    // (SkyfallServer), and nothing here is sent. Every machine draws it from the arrow id, the relayed centre and the settings. Times
    // are seconds after the release: land = SkyfallPattern.NailLandAt, slide = land - NailSlideSeconds, bands = slide - NailBandLeadSeconds.
    //   - from the Nail impact's DomeAt: its kill zone, the game's dynamite danger dome (prefab 31326's RadiusVisual mesh and a copy of its
    //     material, on the mod's own object; the game's object is never instantiated: its ExplosionRadius destroys itself without a
    //     parent), NailDomeRadius on the ground under the centre, growing in, gone just after the landing; over ground only;
    //   - at bands: seven NailBand sigils open (0.5 s each, band k 0.08k s later), stacked over the centre from the ground up to the sigil
    //     (SkyfallCurves.BandHeight), narrower toward the top (BandRadius); odd bands carry the cyan inner ring;
    //   - at slide: the Nail appears with its point at the sigil plane (NailAppearEffect, NailAppearSound) and slides straight down
    //     (SkyfallCurves.Slide) until NailEmbedShare of it is in the ground; each band flares as the point passes it;
    //   - at land: the biggest blast ('Explosion' x NailExplosionScale, which plays 'Huge Explosion' itself; it counts against
    //     SkyfallLook.Budget but plays even when refused: one per storm), NailShockwave, the flint debris (NailSlam), a scorch and the
    //     storm's second and last light (as a fourth bright moment within a second here, SkyfallLook.Bright, it lands dimmed: smoke
    //     and sparks, no explosion, shockwave or light); each band drops and rolls out over the ground as a pair of the game's hand-drawn rings, breaking
    //     up as it goes (SkyfallCurves.RollOut, RollFrame);
    //   - at land + ShatterDelaySeconds: the sky sigil (taken from SkyfallLook) shatters: every rune falls, spinning, and the rim breaks
    //     into two broken rings that grow and fade; ShatterSound;
    //   - from land: the Nail stands NailStandSeconds, then crumbles top to bottom in five puffs CrumbleStepSeconds apart (vanes, shaft,
    //     then head gone), and the run ends.
    // No ground at the centre: the bands span the SigilHeight under the sigil, the Nail slides to its bottom, and at land the Nail and the
    // bands go with no impact look; the shatter still happens. An impact first seen more than 1 s late skips its effects but still rolls,
    // shatters and stands. A fizzled storm never gets a Nail; a festival-coded storm draws the normal Nail (festival was dropped).
    // The Nail is the game's own Arrow (56460, SkyfallLook.PlainArrow: its four LOD0 parts with their real materials, shared and never
    // changed), never a new model (kit trap 7): one root per storm (pooled, at most SkyfallPools.Nails 6, one built ahead by the warm-up), a container per part role scaled thicker along the
    // arrow's own x and y than its length alone would make it, so a feather's offset grows with the shaft. Every renderer is on layer 1
    // (SkyfallSigil.Layer) under one DontDestroyOnLoad host. Asset searches run at an idle moment (Warm, from SkyfallLook's warm-up, and
    // one effect or sound per idle frame after it), never in a release's frame.
    internal static class SkyfallNail {
        // final review I-4: a Nail lived about 23.5 s (slide to last crumble; 9.5 s since 0.18.1's 6 s stand), so a pool of 2 left a
        // third concurrent storm without its Nail; the pools hold SkyfallPools.Storms (6) storms' Nails and domes (and SkyfallSigil 7 bands for each)
        const int BandCount = SkyfallPools.NailBands, NailCap = SkyfallPools.Nails, DomeCap = SkyfallPools.Domes, RuneCap = 64, CrumblePuffs = 5;
        const float BandOpenSeconds = 0.5f, BandStepSeconds = 0.08f, BandRest = 0.75f, FlareSeconds = 0.3f;
        const float LateSeconds = 1f;                                       // an impact first seen this long after its time shows no effects
        const float RimGrowSeconds = 0.6f, RimGrow = 0.3f, RuneFadeSeconds = 1.2f, RuneLift = 2f;
        // the dome's timing: Task 11's cut dome numbers (it grew in over 0.3 s, stayed 0.2 s after its landing, shrank over 0.3 s)
        const float DomeGrowSeconds = 0.3f, DomeLingerSeconds = 0.2f, DomeShrinkSeconds = 0.3f, DomeMeshRadius = 2.97f;
        // a SpriteSheet_Combat ring's middle line as a share of its cell, rows 2 and 3 (SkyfallSigil's RingShare)
        const float HardRingShare = 0.70f, SoftRingShare = 0.58f;
        const uint DynamiteHash = 31326u;

        // ---------------------------------------------------------------- settings (per client, never [shared])

        static MelonPreferences_Entry<float>? shaftThick, headThick, vaneThick, embedShare, bandScale, bandLead, appearScale, explosionScale,
            shockwaveScale, slamScale, scorchSize, flashBrightness, flashRange, rollMetres, rollSeconds, shatterDelay, shatterSeconds,
            shatterKick, shatterGravity, crumbleScale, crumbleStep;
        static MelonPreferences_Entry<string>? headMaterial, appearEffect, appearSound, shockwave, slam, shatterSound, crumbleEffect, crumbleSound;
        static MelonPreferences_Entry<bool>? shadows;

        internal static void Init(MelonPreferences_Category cat) {
            const string P = "Clients (looks only), the World-Nail: ";
            MelonPreferences_Entry<float> F(string key, float value, string text) => cat.CreateEntry(key, value, description: P + text);
            MelonPreferences_Entry<string> S(string key, string value, string text) => cat.CreateEntry(key, value, description: P + text);
            shaftThick = F("NailShaftThick", 2.9f, "how much thicker than its length alone would make it the shaft is (the owner's picture: not a hairline).");
            headThick = F("NailHeadThick", 2.1f, "the same for the head.");
            vaneThick = F("NailVaneThick", 2.3f, "the same for the feathers.");
            headMaterial = S("NailHeadMaterial", "GoldNonForging Static", "the head's material: the game's static gold metal (empty = the arrow's own rusty head).");
            shadows = cat.CreateEntry("NailShadows", true, description: P + "it casts a shadow (the game draws shadows to about 75 m).");
            embedShare = F("NailEmbedShare", 0.06f, "the share of its length it ends buried in the ground.");
            bandScale = F("NailBandScale", 1.0f, "the seven ring bands' size, times NailDomeRadius (that is the lowest; each higher one is smaller).");
            bandLead = F("NailBandLeadSeconds", 0.8f, "seconds before the slide that the bands start to open.");
            appearEffect = S("NailAppearEffect", "Lightbeam Hit Effect", "the flash in the sigil as it appears.");
            appearScale = F("NailAppearScale", 4f, "that flash's size (each of its parts scaled: it ignores its root's size).");
            appearSound = S("NailAppearSound", "Lightbeam Shot Impact Sounds", "its sound.");
            explosionScale = F("NailExplosionScale", 4f, "the landing's explosion (ExplosionEffect), times the game's size. It plays 'Huge Explosion' itself.");
            shockwave = S("NailShockwave", "Shockwave Impact", "the ground shock under it.");
            shockwaveScale = F("NailShockwaveScale", 5f, "its size.");
            slam = S("NailSlam", "Turabada Ground Slam Large Effect", "the flint rocks thrown out (each of its parts scaled).");
            slamScale = F("NailSlamScale", 4f, "its size.");
            scorchSize = F("NailScorchSize", 9f, "metres across its scorch.");
            flashBrightness = F("NailFlashBrightness", 4f, "the one light at the landing (the storm's second and last).");
            flashRange = F("NailFlashRange", 45f, "its range in metres.");
            rollMetres = F("NailRollMetres", 24f, "how far the bands roll out over the ground.");
            rollSeconds = F("NailRollSeconds", 1.4f, "seconds the lowest band takes to roll out (each higher one 0.1 s more).");
            shatterDelay = F("ShatterDelaySeconds", 0.35f, "seconds after the landing that the sky sigil shatters.");
            shatterSeconds = F("ShatterSeconds", 2.8f, "how long its runes fall.");
            shatterKick = F("ShatterKick", 4f, "metres per second the runes are thrown outward.");
            shatterGravity = F("ShatterGravity", 6f, "how fast they fall (metres per second, each second).");
            shatterSound = S("ShatterSound", "Tower Completion;Shrine Training Complete Sounds", "unused: the shatter plays no sound since 0.19.1.");
            crumbleEffect = S("CrumbleEffect", "Large Smoke Puff", "the puffs it crumbles into.");
            crumbleScale = F("CrumbleScale", 3f, "their size (each of its parts scaled).");
            crumbleStep = F("CrumbleStepSeconds", 0.25f, "seconds between puffs, top to bottom.");
            crumbleSound = S("CrumbleSound", "Boulder Breaking Sounds", "as it starts to crumble.");
            Cfg.Reloaded += Reloaded;
        }

        // a broken (NaN) setting reads as its default, anything else is held to [lo, hi] (an infinite one falls to its bound)
        static float G(MelonPreferences_Entry<float>? e, float fallback, float lo, float hi) {
            float x = e != null ? e.Value : fallback;
            return float.IsNaN(x) ? fallback : x < lo ? lo : x > hi ? hi : x;
        }
        static string Name(MelonPreferences_Entry<string>? e, string fallback) => e != null ? e.Value ?? "" : fallback;

        static float ShaftThick => G(shaftThick, 2.9f, 0.05f, 50f);
        static float HeadThick => G(headThick, 2.1f, 0.05f, 50f);
        static float VaneThick => G(vaneThick, 2.3f, 0.05f, 50f);
        static string HeadMaterialName => Name(headMaterial, "GoldNonForging Static").Trim();
        static bool Shadows => shadows == null || shadows.Value;
        static float EmbedShare => G(embedShare, 0.06f, 0f, 0.9f);
        static float BandScale => G(bandScale, 1f, 0f, 10f);
        static float BandLeadSeconds => G(bandLead, 0.8f, 0f, 10f);
        static float AppearScale => G(appearScale, 4f, 0.05f, 50f);
        static float ExplosionScale => G(explosionScale, 4f, 0.05f, 50f);
        static float ShockwaveScale => G(shockwaveScale, 5f, 0.05f, 50f);
        static float SlamScale => G(slamScale, 4f, 0.05f, 50f);
        static float ScorchSize => G(scorchSize, 9f, 0f, 100f);
        static float FlashBrightness => G(flashBrightness, 4f, 0f, 20f);
        static float FlashRange => G(flashRange, 45f, 0f, 500f);
        static float RollMetres => G(rollMetres, 24f, 0f, 200f);
        static float RollSeconds => G(rollSeconds, 1.4f, 0f, 20f);
        static float ShatterDelaySeconds => G(shatterDelay, 0.35f, 0f, 1.5f);   // the storm look starts to fade the sky sigil 1.5 s after the landing
        static float ShatterSeconds => G(shatterSeconds, 2.8f, 0f, 20f);
        static float ShatterKick => G(shatterKick, 4f, 0f, 100f);
        static float ShatterGravity => G(shatterGravity, 6f, 0f, 100f);
        static float CrumbleScale => G(crumbleScale, 3f, 0.05f, 50f);
        static float CrumbleStepSeconds => G(crumbleStep, 0.25f, 0f, 10f);

        static bool nailFailed, modelSaid, domeWarned;
        static readonly HashSet<string> fullSaid = new HashSet<string>();

        static void Reloaded() {
            nailFailed = false;
            headDone = false;
            headMat = null;                                                 // looked for again by the next warm-up (the name may have changed)
            if (domeMesh == null) domeDone = false;                         // the dome again only when it was missing
            warmStep = -1;
            modelSaid = domeWarned = false;
            fullSaid.Clear();
        }

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        static void Full(string pool, int cap) {
            if (fullSaid.Add(pool) && Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: the {pool} pool is full ({cap}); a look is skipped (said once)");
        }

        // ---------------------------------------------------------------- the game's pieces: the gold head, the dynamite's dome

        static Material? headMat;
        static bool headDone;

        static readonly int T_HeadMaterial = Timing.Section("asset SkyfallNail.HeadMaterial");
        // the material of that exact name among the loaded ones (one search), used as it is: shared, never changed
        static Material? FindHeadMaterial(string name) {
            using var _t = Timing.Begin(T_HeadMaterial);
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name == name) return m;
            return null;
        }

        static Mesh? domeMesh;
        static Material? domeMat;
        static float domeRadius = DomeMeshRadius;
        static Vector3 domeCentre;
        static bool domeDone;

        static readonly int T_Dome = Timing.Section("asset SkyfallNail.Dome");
        /// <summary>The dynamite (31326)'s danger dome: its 'RadiusVisual' mesh and a copy of its material (untinted: the game's danger
        /// colours), read once from the loaded prefab, which is never instantiated (its ExplosionRadius unparents and destroys itself).
        /// False when missing (warned once; the kill zone is then a fire ring).</summary>
        static bool Dome() {
            if (domeDone) return domeMesh != null && domeMat != null;
            using var _t = Timing.Begin(T_Dome);
            domeDone = true;
            NetworkPrefab? prefab = PrefabManager.GetPrefab(DynamiteHash);
            ExplosionRadius? er = prefab != null ? prefab.GetComponentInChildren<ExplosionRadius>(true) : null;
            MeshFilter? mf = er != null ? er.GetComponentInChildren<MeshFilter>(true) : null;
            MeshRenderer? mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            Mesh? mesh = mf != null ? mf.sharedMesh : null;
            Material? src = mr != null ? mr.sharedMaterial : null;
            if (mesh == null || src == null) {
                if (!domeWarned) { domeWarned = true; MelonLogger.Warning("[Archery] Skyfall: the dynamite's danger dome was not found; the World-Nail's kill zone is marked with a fire ring instead"); }
                return false;
            }
            domeMesh = mesh;
            if (domeMat == null) domeMat = new Material(src) { name = src.name + " (Archery Skyfall Nail dome)" };
            Bounds b = mesh.bounds;
            domeRadius = b.extents.x > 0f ? b.extents.x : DomeMeshRadius;
            domeCentre = b.center;
            if (Logs.Summary) MelonLogger.Msg(Inv($"[Archery] Skyfall: the World-Nail's dome is the dynamite's '{mesh.name}' on '{src.name}' ({Fx.ShaderName(src)}), radius {domeRadius:0.00} m (said once)"));
            return true;
        }

        // the idle warm-up after Warm: one effect or sound lookup per frame while no Nail runs (each is cached by Fx)
        static int warmStep = -1, warmFrame = -1;

        /// <summary>SkyfallLook's warm-up, while no storm runs (once per settings load): the gold head's search by its exact name (only
        /// when NailHeadMaterial is not empty) and the dome's read, ahead of time, so Start and the slide only read the caches; then
        /// this look's effects and sounds, one per idle frame. Never on the headless server.</summary>
        internal static void Warm() {
            if (ApplicationManager.IsHeadless) return;
            try {
                if (!headDone) {
                    headDone = true;
                    string name = HeadMaterialName;
                    headMat = name.Length > 0 ? FindHeadMaterial(name) : null;
                    if (name.Length > 0 && headMat == null)
                        MelonLogger.Warning($"[Archery] Skyfall: the Nail's head material '{name}' was not found; it keeps the arrow's own head");
                    else if (headMat != null && Logs.Summary)
                        MelonLogger.Msg($"[Archery] Skyfall: the World-Nail's head is '{name}' ({Fx.ShaderName(headMat)}) (said once)");
                }
                Dome();
                warmStep = 0;
                warmFrame = Time.frameCount;                                // the effects start next frame: this one already searched
            }
            catch (Exception e) { Failed(e); }
        }

        static void WarmStep() {
            if (warmStep < 0 || Time.frameCount == warmFrame) return;
            switch (warmStep++) {
                case 0: Fx.FindEffect(Name(appearEffect, "Lightbeam Hit Effect")); break;
                case 1: Fx.FindEffect(Name(shockwave, "Shockwave Impact")); break;
                case 2: Fx.FindEffect(Name(slam, "Turabada Ground Slam Large Effect")); break;
                case 3: Fx.FindEffect(Name(crumbleEffect, "Large Smoke Puff")); break;
                case 4: Fx.FindSound(Name(appearSound, "Lightbeam Shot Impact Sounds")); break;
                case 5: break;                                              // was the shatter sound (removed)
                case 6: Fx.FindSound(Name(crumbleSound, "Boulder Breaking Sounds")); break;
                default: warmStep = -1; break;                               // done until the next warm-up
            }
        }

        // ---------------------------------------------------------------- the host, the Nail model and the dome (pooled)

        static GameObject? host;

        static Transform Host() {
            if (host == null) {
                host = new GameObject("ArcherySkyfallNails");
                UnityEngine.Object.DontDestroyOnLoad(host);
                SkyfallSigil.Layer(host);
            }
            return host.transform;
        }

        /// <summary>One World-Nail: a root (its origin the point, its +z toward the point), one container per part role at the root's
        /// origin (scaled per take: thicker along the arrow's x and y, its length along z), and the Arrow's parts at their own poses
        /// under their role's container with their real materials. Eight objects for the plain Arrow, made once.</summary>
        sealed class NailModel {
            readonly GameObject root;
            readonly Transform[] containers = new Transform[3];             // by SkyfallLook.ArrowRole
            readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
            readonly List<SkyfallLook.ArrowRole> roles = new List<SkyfallLook.ArrowRole>();
            readonly List<Material[]> own = new List<Material[]>();

            internal bool Busy { get; private set; }
            internal bool Alive => root != null;

            internal NailModel(Transform parent, IReadOnlyList<SkyfallLook.ArrowPart> parts) {
                root = new GameObject("ArcherySkyfallNail");
                SkyfallSigil.Layer(root);
                root.transform.SetParent(parent, false);
                for (int k = 0; k < containers.Length; k++) {
                    var c = new GameObject(((SkyfallLook.ArrowRole)k).ToString());
                    SkyfallSigil.Layer(c);
                    c.transform.SetParent(root.transform, false);
                    containers[k] = c.transform;
                }
                foreach (SkyfallLook.ArrowPart p in parts) {
                    var go = new GameObject(p.Name);
                    SkyfallSigil.Layer(go);
                    Transform t = go.transform;
                    t.SetParent(containers[(int)p.Role], false);
                    t.localPosition = p.Pos;
                    t.localRotation = p.Rot;                                // kept exactly (the spike's 90 degrees): Unity draws with the full matrix
                    t.localScale = p.Scale;
                    go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = p.Materials;
                    renderers.Add(mr);
                    roles.Add(p.Role);
                    own.Add(p.Materials);
                }
                root.SetActive(false);
            }

            /// <summary>Taken: S times the plain Arrow along its length, times each role's thickness across it; the head on the gold when
            /// given (every slot), every other part on its own materials; shadows as NailShadows; every part shown.</summary>
            internal void Take(float s, float shaft, float head, float vane, Material? gold, bool castShadows) {
                Busy = true;
                containers[(int)SkyfallLook.ArrowRole.Shaft].localScale = new Vector3(s * shaft, s * shaft, s);
                containers[(int)SkyfallLook.ArrowRole.Head].localScale = new Vector3(s * head, s * head, s);
                containers[(int)SkyfallLook.ArrowRole.Vane].localScale = new Vector3(s * vane, s * vane, s);
                var mode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                for (int i = 0; i < renderers.Count; i++) {
                    MeshRenderer mr = renderers[i];
                    if (mr == null) continue;
                    if (roles[i] == SkyfallLook.ArrowRole.Head && gold != null) {
                        var mats = new Material[Math.Max(1, own[i].Length)];
                        for (int k = 0; k < mats.Length; k++) mats[k] = gold;
                        mr.sharedMaterials = mats;
                    }
                    else mr.sharedMaterials = own[i];
                    mr.shadowCastingMode = mode;
                }
                foreach (Transform c in containers) if (c != null && !c.gameObject.activeSelf) c.gameObject.SetActive(true);
                if (!root.activeSelf) root.SetActive(true);
            }

            internal void Place(Vector3 point, Quaternion facing) => root.transform.SetPositionAndRotation(point, facing);

            internal void Hide(SkyfallLook.ArrowRole role) {
                Transform c = containers[(int)role];
                if (c != null && c.gameObject.activeSelf) c.gameObject.SetActive(false);
            }

            internal void Release() {
                Busy = false;
                if (root != null && root.activeSelf) root.SetActive(false);
            }
        }

        static readonly List<NailModel> nails = new List<NailModel>();

        static readonly int T_TakeNail = Timing.Section("look SkyfallNail.TakeNail (take or make the World-Nail)");
        static NailModel? TakeNail(IReadOnlyList<SkyfallLook.ArrowPart> parts, float s) {
            using var _t = Timing.Begin(T_TakeNail);
            NailModel? n = null;
            foreach (NailModel x in nails) if (!x.Busy && x.Alive) { n = x; break; }
            if (n == null) {
                nails.RemoveAll(x => !x.Alive);
                if (nails.Count >= NailCap) { Full("World-Nail", NailCap); return null; }
                n = new NailModel(Host(), parts);
                nails.Add(n);
            }
            n.Take(s, ShaftThick, HeadThick, VaneThick, headMat, Shadows);    // the head's gold only from the warm-up's cache
            return n;
        }

        /// <summary>The dome on the mod's own object: the dynamite's mesh and the material copy, no shadows.</summary>
        sealed class DomeObject {
            readonly GameObject go;
            internal bool Busy { get; private set; }
            internal bool Alive => go != null;

            internal DomeObject(Transform parent, Mesh mesh, Material mat) {
                go = new GameObject("ArcherySkyfallNailDome");
                SkyfallSigil.Layer(go);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
            }

            internal void Take() { Busy = true; if (!go.activeSelf) go.SetActive(true); }

            /// <summary>Centred on <paramref name="at"/> (the mesh's own centre there), <paramref name="radius"/> metres.</summary>
            internal void Place(Vector3 at, float radius) {
                float k = radius / domeRadius;
                Transform t = go.transform;
                t.localScale = new Vector3(k, k, k);
                t.localPosition = at - domeCentre * k;
            }

            internal void Release() { Busy = false; if (go != null && go.activeSelf) go.SetActive(false); }
        }

        static readonly List<DomeObject> domes = new List<DomeObject>();

        static readonly int T_Prebuild = Timing.Section("look SkyfallNail.Prebuild (the Nail and its dome ahead, idle)");
        /// <summary>SkyfallLook's idle warm-up (final review I-2): one World-Nail model, then one dome, built into their pools (taken and
        /// released at once, never shown), one a call. True when both are there, or cannot be (headless, failed, the Arrow or the dome
        /// missing).</summary>
        internal static bool Prebuild() {
            if (ApplicationManager.IsHeadless || nailFailed) return true;
            using var _t = Timing.Begin(T_Prebuild);
            try {
                nails.RemoveAll(x => !x.Alive);
                IReadOnlyList<SkyfallLook.ArrowPart>? parts = SkyfallLook.PlainArrow();
                if (nails.Count < SkyfallPools.WarmNails && parts != null) {
                    var n = new NailModel(Host(), parts);
                    nails.Add(n);
                    n.Take(1f, ShaftThick, HeadThick, VaneThick, headMat, Shadows);
                    n.Release();
                    return false;
                }
                domes.RemoveAll(x => !x.Alive);
                if (domes.Count < SkyfallPools.WarmDomes && Dome()) {
                    var d = new DomeObject(Host(), domeMesh!, domeMat!);
                    domes.Add(d);
                    d.Take();
                    d.Release();
                    return false;
                }
                return true;
            }
            catch (Exception e) { Failed(e); return true; }
        }

        static DomeObject? TakeDome() {
            if (!Dome()) return null;
            DomeObject? d = null;
            foreach (DomeObject x in domes) if (!x.Busy && x.Alive) { d = x; break; }
            if (d == null) {
                domes.RemoveAll(x => !x.Alive);
                if (domes.Count >= DomeCap) { Full("World-Nail dome", DomeCap); return null; }
                d = new DomeObject(Host(), domeMesh!, domeMat!);
                domes.Add(d);
            }
            d.Take();
            return d;
        }

        // ---------------------------------------------------------------- one storm's Nail

        sealed class FallingRune { public SkyfallSigil.Quad Q = null!; public Vector3 Pos, Vel; public Quaternion Rot; public float Size, Spin; public Color Colour; }

        sealed class Run {
            public SkyStorm S = null!;
            public float Land, SlideAt, BandsAt, DomeAt, CrumbleAt, Top, Ground = float.NaN, Bottom, ShatteredAt;
            public Quaternion Facing;
            public bool GroundFixed, GroundTriedAtDome, DomeTaken, BandsBuilt, Shown, Landed, Shattered, Rolling;
            public int CrumbleStep;
            public readonly SkyfallSigil.Sigil?[] Bands = new SkyfallSigil.Sigil?[BandCount];
            public readonly float[] PassedAt = new float[BandCount], BandY = new float[BandCount], BandR = new float[BandCount], RollTurn = new float[BandCount];
            public readonly SkyfallSigil.Quad?[] Rolls = new SkyfallSigil.Quad?[2 * BandCount];   // 2k the hard ring, 2k + 1 the soft one
            public readonly SkyfallSigil.Quad?[] Rim = new SkyfallSigil.Quad?[2];
            public readonly float[] RimTurn = new float[2];
            public readonly List<FallingRune> Runes = new List<FallingRune>();
            public NailModel? Model;
            public DomeObject? Dome;
            public SkyfallSigil.Quad? DomeRing;
        }

        static readonly Dictionary<uint, Run> runs = new Dictionary<uint, Run>();
        static readonly List<uint> scratch = new List<uint>();
        static readonly List<SkyfallSigil.RuneSpot> spots = new List<SkyfallSigil.RuneSpot>();

        static float GroundOf(SkyStorm s) =>
            Skyfall.GroundAt(s.CX, s.CZ, s.CY + 0.5, s.Release.Y - Skyfall.GroundBelowMetres, out float y) ? y : float.NaN;   // SkyfallServer's bounds

        static readonly int T_Start = Timing.Section("look SkyfallNail.Start");
        /// <summary>SkyfallLook.Start (clients, once its storm run is recorded): records this storm's Nail and casts the ground under the
        /// centre. Nothing is built yet. Nothing for a fizzle, a storm without a Nail impact or one already recorded.</summary>
        internal static void Start(SkyStorm s) {
            using var _t = Timing.Begin(T_Start);
            if (ApplicationManager.IsHeadless || nailFailed || s == null || s.Code == SkyCode.Fizzled || runs.ContainsKey(s.ArrowId)) return;
            try {
                int nail = -1;
                for (int i = 0; i < s.Impacts.Length; i++) if (s.Impacts[i].Nail) { nail = i; break; }
                if (nail < 0) return;
                SkyfallValues v = Skyfall.Values;                           // clamped
                float land = SkyfallPattern.NailLandAt(v), slide = land - v.NailSlideSeconds;
                Vector3 yaw = Quaternion.Euler(0f, 360f * LookSeed.Unit(s.ArrowId, 0, 61), 0f) * Vector3.forward;
                var r = new Run {
                    S = s, Land = land, SlideAt = slide, BandsAt = slide - BandLeadSeconds, DomeAt = s.Impacts[nail].DomeAt,
                    CrumbleAt = land + v.NailStandSeconds, Top = (float)s.CY, Ground = GroundOf(s),
                    Facing = Quaternion.LookRotation(Vector3.down, yaw),
                };
                for (int k = 0; k < BandCount; k++) r.PassedAt[k] = float.NaN;
                runs[s.ArrowId] = r;
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: World-Nail of storm {s.ArrowId} recorded ({(float.IsNaN(r.Ground) ? "no ground yet" : "over ground")})");
            }
            catch (Exception e) { Failed(e); }
        }

        /// <summary>That storm's Nail goes at once: its bands, model, dome, rolls, runes and rim (a fizzle, or the storm look cleared).</summary>
        internal static void Stop(uint arrowId) {
            try {
                if (!runs.TryGetValue(arrowId, out Run r)) return;
                ReleaseRun(r);
                runs.Remove(arrowId);
            }
            catch (Exception e) { Failed(e); }
        }

        /// <summary>Every storm's Nail released (no player, the storm look cleared, or a failure).</summary>
        internal static void Clear() {
            foreach (var kv in runs) ReleaseRun(kv.Value);
            runs.Clear();
            foreach (NailModel n in nails) if (n.Busy) n.Release();
            foreach (DomeObject d in domes) if (d.Busy) d.Release();
        }

        static void ReleaseRun(Run r) {
            for (int k = 0; k < r.Bands.Length; k++) { r.Bands[k]?.Release(); r.Bands[k] = null; }
            for (int k = 0; k < r.Rolls.Length; k++) { r.Rolls[k]?.Release(); r.Rolls[k] = null; }
            for (int k = 0; k < r.Rim.Length; k++) { r.Rim[k]?.Release(); r.Rim[k] = null; }
            foreach (FallingRune f in r.Runes) f.Q.Release();
            r.Runes.Clear();
            r.Model?.Release(); r.Model = null;
            r.Dome?.Release(); r.Dome = null;
            r.DomeRing?.Release(); r.DomeRing = null;
            r.Rolling = false;
        }

        static void Failed(Exception e) {
            if (nailFailed) return;
            nailFailed = true;
            try { Clear(); }
            catch (Exception c) { MelonLogger.Warning($"[Archery] Skyfall: the World-Nail could not be cleared ({c.Message})"); }
            MelonLogger.Warning($"[Archery] Skyfall: the World-Nail could not be shown ({e.Message}); storms end without it until the settings are reloaded");
        }

        // ---------------------------------------------------------------- the frame

        /// <summary>Main.OnLateUpdate (clients), after the storm look (which hands over the sky sigil): every storm's Nail. While none
        /// runs, the warm-up's next lookup. No player (a disconnect): everything released.</summary>
        internal static void Tick() {
            if (ApplicationManager.IsHeadless) return;
            try {
                var me = global::Player.Current;
                if (me == null || (me is UnityEngine.Object o && o == null)) {
                    if (runs.Count > 0) Clear();
                    return;
                }
                if (nailFailed) return;
                if (runs.Count == 0) { WarmStep(); return; }
                float now = Time.time, dt = Time.deltaTime;
                SkyfallValues v = Skyfall.Values;
                scratch.Clear();
                foreach (var kv in runs) scratch.Add(kv.Key);
                foreach (uint id in scratch) {
                    if (!runs.TryGetValue(id, out Run r) || !Frame(r, now, dt, v)) continue;
                    ReleaseRun(r);
                    runs.Remove(id);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: World-Nail of storm {id} done");
                }
            }
            catch (Exception e) { Failed(e); }
        }

        // one storm's Nail; true when it is over
        static bool Frame(Run r, float now, float dt, SkyfallValues v) {
            SkyStorm s = r.S;
            float t = now - s.StartTime;
            float cx = (float)s.CX, cz = (float)s.CZ;

            // the ground: cast once more when the dome is due and at the bands while unknown; fixed from the bands on
            if (!r.GroundFixed) {
                bool atDome = !r.GroundTriedAtDome && t >= r.DomeAt, atBands = t >= r.BandsAt;
                if (atDome) r.GroundTriedAtDome = true;
                if ((atDome || atBands) && float.IsNaN(r.Ground)) r.Ground = GroundOf(s);
                if (atBands) Fix(r, v);
            }
            bool ground = !float.IsNaN(r.Ground);

            // the kill zone: the dynamite's dome under the centre, from the Nail's DomeAt until just after the landing (over ground only)
            float dome = ground ? DomeSize(t, r.DomeAt, r.Land) : 0f;
            if (dome > 0f) {
                if (!r.DomeTaken) {
                    r.DomeTaken = true;
                    r.Dome = TakeDome();
                    if (r.Dome == null) r.DomeRing = SkyfallSigil.Piece(SkyfallSigil.Tex.Combat, 2, 1, SkyfallSigil.Blend.Add);
                }
                var at = new Vector3(cx, r.Ground, cz);
                float radius = v.NailDomeRadius * dome;
                r.Dome?.Place(at, radius);
                if (r.DomeRing != null) {
                    r.DomeRing.Place(new Vector3(cx, r.Ground + 0.06f, cz), Quaternion.Euler(90f, 0f, 0f), 2f * radius / HardRingShare);
                    r.DomeRing.Paint(SkyfallSigil.Fire);
                }
            }
            else if (r.DomeTaken && t > r.Land) { r.Dome?.Release(); r.Dome = null; r.DomeRing?.Release(); r.DomeRing = null; }

            // the Nail: taken at the slide (point at the sigil plane), sliding down to its bottom by the landing
            if (!r.Shown && t >= r.SlideAt) {
                r.Shown = true;
                if (t < (ground ? r.CrumbleAt : r.Land)) {                   // a run first seen after the Nail's time skips it
                    IReadOnlyList<SkyfallLook.ArrowPart>? parts = SkyfallLook.PlainArrow();
                    float plain = SkyfallLook.PlainArrowLength;
                    if (parts == null || !(plain > 0f)) {
                        if (!modelSaid && Logs.Debug) { modelSaid = true; MelonLogger.Msg("[Archery] Skyfall: no World-Nail model (the Arrow's parts are missing); bands and shatter only"); }
                    }
                    else r.Model = TakeNail(parts, v.NailLength / plain);
                }
                if (r.Model != null && t - r.SlideAt <= LateSeconds && t < r.Land) {   // the Nail's own flash: none without it, or late
                    var sigil = new Vector3(cx, r.Top, cz);
                    if (SkyfallLook.Bright(now))                             // a bright moment: not as a fourth within a second here
                        SkyfallLook.GetScaledParts(Fx.FindEffect(Name(appearEffect, "Lightbeam Hit Effect")), sigil, Quaternion.identity, AppearScale);
                    VolleyFx.Play(Name(appearSound, "Lightbeam Shot Impact Sounds"), sigil);
                }
            }
            float pointY = r.Top + (r.Bottom - r.Top) * SkyfallCurves.Slide((t - r.SlideAt) / v.NailSlideSeconds);
            r.Model?.Place(new Vector3(cx, pointY, cz), r.Facing);

            // the seven bands: open from the bands' time, each flaring as the point passes it, until the landing
            if (!r.Landed && t >= r.SlideAt)
                for (int k = 0; k < BandCount; k++) if (float.IsNaN(r.PassedAt[k]) && pointY <= r.BandY[k]) r.PassedAt[k] = t;
            if (!r.Landed && t >= r.BandsAt && t < r.Land) {
                if (!r.BandsBuilt) {
                    r.BandsBuilt = true;
                    for (int k = 0; k < BandCount; k++) {
                        SkyfallSigil.Sigil? b = SkyfallSigil.Build(SkyfallSigil.Style.NailBand, unchecked(s.ArrowId + (uint)k), accent: (k & 1) == 1);
                        b?.Place(new Vector3(cx, r.BandY[k], cz), Quaternion.identity, r.BandR[k]);
                        r.Bands[k] = b;
                    }
                }
                for (int k = 0; k < BandCount; k++) {
                    float alpha = BandRest + (1f - BandRest) * SkyfallCurves.Envelope(t - r.PassedAt[k], FlareSeconds, 0f, FlareSeconds);
                    r.Bands[k]?.Show(t - r.BandsAt - BandStepSeconds * k, BandOpenSeconds, alpha);
                }
            }

            // the landing
            if (!r.Landed && t >= r.Land) {
                r.Landed = true;
                for (int k = 0; k < BandCount; k++) { r.Bands[k]?.Release(); r.Bands[k] = null; }
                if (!ground) { r.Model?.Release(); r.Model = null; }        // no ground: no impact look
                else {
                    if (t - r.Land <= LateSeconds) Impact(r, new Vector3(cx, r.Ground, cz), now);
                    StartRoll(r);
                }
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: World-Nail of storm {s.ArrowId} landed at ({s.CX:0.0}, {r.Ground:0.0}, {s.CZ:0.0}){(ground ? "" : ", no ground")}"));
            }
            if (r.Rolling) RollFrame(r, t - r.Land);

            // the sky sigil's shatter
            if (!r.Shattered && t >= r.Land + ShatterDelaySeconds) Shatter(r, t);
            if (r.Shattered) ShatterFrame(r, t - r.ShatteredAt, dt, v);

            // the crumble: five puffs from the top down, then the Nail is gone
            if (r.Model != null && t >= r.CrumbleAt) Crumble(r, t, v);

            return r.Landed && r.Shattered && r.Model == null && !r.Rolling && r.Runes.Count == 0 && r.Rim[0] == null && r.Rim[1] == null
                && r.Dome == null && r.DomeRing == null;
        }

        // the bands' heights and radii and the Nail's bottom, from the ground (or, with none, the SigilHeight under the sigil)
        static void Fix(Run r, SkyfallValues v) {
            r.GroundFixed = true;
            bool ground = !float.IsNaN(r.Ground);
            float floor = ground ? r.Ground : r.Top - v.SigilHeight;
            for (int k = 0; k < BandCount; k++) {
                r.BandY[k] = floor + SkyfallCurves.BandHeight(k, BandCount) * (r.Top - floor);
                r.BandR[k] = v.NailDomeRadius * BandScale * SkyfallCurves.BandRadius(k, BandCount);
            }
            r.Bottom = ground ? r.Ground - EmbedShare * v.NailLength : floor;
        }

        // the dome's size, 0 (hidden) to 1: grows in from DomeAt, full until the landing has lingered, then shrinks away
        static float DomeSize(float t, float domeAt, float land) =>
            Math.Min(SkyfallCurves.Ease((t - domeAt) / DomeGrowSeconds), 1f - SkyfallCurves.Ease((t - land - DomeLingerSeconds) / DomeShrinkSeconds));

        // the biggest blast, the shock, the flint, the scorch and the storm's last light. Final review I-3: the finale is a bright moment
        // (SkyfallLook.Bright, the owner's "no more than 3 bright flashes a second" across every storm: four Nails landing within a second
        // after `skyfall give all`); as a fourth within a second on this machine it lands dimmed: smoke and sparks, no explosion, no
        // shockwave and no light. The flint and the scorch either way.
        static void Impact(Run r, Vector3 at, float now) {
            if (SkyfallLook.Bright(now)) {
                PooledObjectDefinition? boom = Fx.FindEffect(SkyfallLook.ExplosionName);
                SkyfallLook.Budget.TryTake(now, SkyfallLook.ExplosionLifeSeconds);   // it counts against the cap, but the finale plays even when refused
                if (boom != null) Fx.GetScaled(boom, at, Quaternion.identity, ExplosionScale);
                PooledObjectDefinition? wave = Fx.FindEffect(Name(shockwave, "Shockwave Impact"));
                if (wave != null) Fx.GetScaled(wave, at, Quaternion.identity, ShockwaveScale);
                Fx.Flash(at + Vector3.up * 2f, SkyfallSigil.FireHot, FlashBrightness, FlashRange, SkyfallLook.FlashSeconds);
            }
            else {
                SkyfallLook.GetScaledParts(Fx.FindEffect(Name(crumbleEffect, "Large Smoke Puff")), at, Quaternion.identity, CrumbleScale);
                VolleyFx.Sparks.Emit(at + Vector3.up * 0.5f, Vector3.up, 40, SkyfallSigil.FireHot, 75f, 10f, 4f);
                VolleyFx.Play(SkyfallLook.SpareSoundName, at);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: World-Nail of storm {r.S.ArrowId} landed dimmed (a fourth bright moment within a second here)");
            }
            SkyfallLook.GetScaledParts(Fx.FindEffect(Name(slam, "Turabada Ground Slam Large Effect")), at, Quaternion.identity, SlamScale);
            SkyfallLook.Scorch(at, ScorchSize, r.S.ArrowId);
        }

        // every band falls to the ground as a ring pair: the hard hand-drawn ring (row 2) over a soft one (row 3)
        static void StartRoll(Run r) {
            for (int k = 0; k < BandCount; k++) {
                r.Rolls[2 * k] = SkyfallSigil.Piece(SkyfallSigil.Tex.Combat, 2, 0, SkyfallSigil.Blend.Both);
                r.Rolls[2 * k + 1] = SkyfallSigil.Piece(SkyfallSigil.Tex.Combat, 3, 0, SkyfallSigil.Blend.Add);
                r.RollTurn[k] = 360f * LookSeed.Unit(r.S.ArrowId, k, 74);
            }
            r.Rolling = true;
        }

        static void RollFrame(Run r, float since) {
            bool any = false;
            Color gold = SkyfallSigil.Gold, cyan = SkyfallSigil.CyanPale;
            float metres = RollMetres, seconds = RollSeconds;
            for (int k = 0; k < BandCount; k++) {
                SkyfallSigil.Quad? hard = r.Rolls[2 * k], soft = r.Rolls[2 * k + 1];
                if (hard == null && soft == null) continue;
                float rs = seconds + 0.1f * k;
                if (!(since < rs)) { hard?.Release(); soft?.Release(); r.Rolls[2 * k] = r.Rolls[2 * k + 1] = null; continue; }
                any = true;
                float radius = r.BandR[k] + metres * SkyfallCurves.RollOut(since, rs);
                float fade = 1f - SkyfallCurves.Ease(since / rs);
                int frame = SkyfallCurves.RollFrame(since, rs);
                var at = new Vector3((float)r.S.CX, r.Ground + 0.1f + 0.02f * k, (float)r.S.CZ);   // a little higher each band: no z-fighting
                Quaternion flat = Quaternion.AngleAxis(r.RollTurn[k], Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                Color c = (k & 1) == 0 ? gold : cyan;
                if (hard != null) {
                    hard.Cell(2, frame);
                    hard.Place(at, flat, 2f * radius / HardRingShare);
                    hard.Paint(new Color(c.r, c.g, c.b, c.a * fade));
                }
                if (soft != null) {
                    soft.Cell(3, frame);
                    soft.Place(at, flat, 2f * radius / SoftRingShare);
                    soft.Paint(new Color(c.r, c.g, c.b, c.a * fade * 0.5f));
                }
            }
            r.Rolling = any;
        }

        static readonly int T_Shatter = Timing.Section("look SkyfallNail.Shatter");
        // the sky sigil, taken from the storm look: each rune of it becomes a falling piece (at most RuneCap for every storm), the rim two
        // broken rings, and the sigil itself goes
        static void Shatter(Run r, float t) {
            using var _t = Timing.Begin(T_Shatter);
            r.Shattered = true;
            r.ShatteredAt = t;
            SkyfallSigil.Sigil? sky = SkyfallLook.TakeSkySigil(r.S.ArrowId);
            if (sky == null) return;                                        // not drawn here, or already gone: nothing to shatter
            uint id = r.S.ArrowId;
            var centre = new Vector3((float)r.S.CX, (float)r.S.CY, (float)r.S.CZ);
            int live = 0;
            foreach (var kv in runs) live += kv.Value.Runes.Count;
            spots.Clear();
            sky.Runes(spots);
            float kick = ShatterKick;
            for (int j = 0; j < spots.Count; j++) {
                if (live >= RuneCap) { Full("falling rune", RuneCap); break; }
                SkyfallSigil.RuneSpot spot = spots[j];
                SkyfallSigil.Quad? q = SkyfallSigil.Piece(spot.Symbol, 0, 0, SkyfallSigil.Blend.Both);
                if (q == null) break;                                       // the loose pool is full or the art is missing
                q.Place(spot.Pos, spot.Rot, spot.Size);
                q.Paint(spot.Colour);
                var outward = new Vector3(spot.Pos.x - centre.x, 0f, spot.Pos.z - centre.z);
                if (outward.sqrMagnitude < 1e-6f) outward = Quaternion.Euler(0f, 360f * LookSeed.Unit(id, j, 70), 0f) * Vector3.forward;
                outward.Normalize();
                float spin = 60f + 60f * LookSeed.Unit(id, j, 72);
                if (LookSeed.Unit(id, j, 73) < 0.5f) spin = -spin;
                r.Runes.Add(new FallingRune {
                    Q = q, Pos = spot.Pos, Rot = spot.Rot, Size = spot.Size, Colour = spot.Colour, Spin = spin,
                    Vel = outward * kick * (0.6f + 0.8f * LookSeed.Unit(id, j, 71)) + Vector3.up * RuneLift,
                });
                live++;
            }
            for (int i = 0; i < r.Rim.Length; i++) {
                r.Rim[i] = SkyfallSigil.Piece(SkyfallSigil.Tex.Combat, 2, 6 + i, SkyfallSigil.Blend.Both);
                r.RimTurn[i] = 360f * LookSeed.Unit(id, i, 75);
            }
            sky.Release();
            // the shatter plays no sound (owner, 2026-11-20: the Tower Completion swell removed)
        }

        // the falling runes (each frame: gravity, then the step) and the rim's broken rings; placed from the shatter's own frame on
        static void ShatterFrame(Run r, float age, float dt, SkyfallValues v) {
            float life = ShatterSeconds, gravity = ShatterGravity;
            for (int i = r.Runes.Count - 1; i >= 0; i--) {
                FallingRune f = r.Runes[i];
                if (!(age < life)) { f.Q.Release(); r.Runes.RemoveAt(i); continue; }
                f.Vel.y -= gravity * dt;
                f.Pos += f.Vel * dt;
                f.Q.Place(f.Pos, f.Rot * Quaternion.AngleAxis(f.Spin * age, Vector3.forward), f.Size);   // about its own normal (the quad's z)
                Color c = f.Colour;
                c.a *= SkyfallCurves.Envelope(age, life, 0f, RuneFadeSeconds);
                f.Q.Paint(c);
            }
            RimFrame(r, age, v);
        }

        // the rim's two broken rings at the sigil, growing to 1.3 x its size over 0.6 s as they fade
        static void RimFrame(Run r, float age, SkyfallValues v) {
            if (r.Rim[0] == null && r.Rim[1] == null) return;
            if (!(age < RimGrowSeconds)) { for (int i = 0; i < r.Rim.Length; i++) { r.Rim[i]?.Release(); r.Rim[i] = null; } return; }
            float e = SkyfallCurves.Ease(age / RimGrowSeconds);
            float side = 2f * v.SigilRadius * (1f + RimGrow * e) / HardRingShare;
            var centre = new Vector3((float)r.S.CX, (float)r.S.CY, (float)r.S.CZ);
            Color gold = SkyfallSigil.Gold;
            for (int i = 0; i < r.Rim.Length; i++) {
                SkyfallSigil.Quad? q = r.Rim[i];
                if (q == null) continue;
                q.Place(centre, Quaternion.AngleAxis(r.RimTurn[i], Vector3.up) * Quaternion.Euler(90f, 0f, 0f), side);
                q.Paint(new Color(gold.r, gold.g, gold.b, gold.a * (1f - e)));
            }
        }

        // the puffs due now, from the top down (several at once after a hitch): the vanes go at the first, the shaft at the third, the
        // head at the last, and then the Nail is released
        static void Crumble(Run r, float t, SkyfallValues v) {
            float step = CrumbleStepSeconds, standing = v.NailLength * (1f - EmbedShare);
            while (r.Model != null && r.CrumbleStep < CrumblePuffs && t >= r.CrumbleAt + r.CrumbleStep * step) {
                int j = r.CrumbleStep++;
                var p = new Vector3((float)r.S.CX, r.Ground + (0.9f - 0.2f * j) * standing, (float)r.S.CZ);
                SkyfallLook.GetScaledParts(Fx.FindEffect(Name(crumbleEffect, "Large Smoke Puff")), p, Quaternion.identity, CrumbleScale);
                VolleyFx.Sparks.Emit(p, Vector3.up, 20, SkyfallSigil.FireHot, 80f, 3f, 3f);
                if (j == 0) { VolleyFx.Play(Name(crumbleSound, "Boulder Breaking Sounds"), p); r.Model.Hide(SkyfallLook.ArrowRole.Vane); }
                if (j == 2) r.Model.Hide(SkyfallLook.ArrowRole.Shaft);
                if (j == CrumblePuffs - 1) { r.Model.Hide(SkyfallLook.ArrowRole.Head); r.Model.Release(); r.Model = null; }
            }
        }

        /// <summary>SelfCheck (0 when headless): the slide, the seven bands' heights and radii, and their roll and ring frame with the
        /// NailRollSeconds just loaded. Throws on a number that is not one; returns the count evaluated.</summary>
        internal static int Check() {
            if (ApplicationManager.IsHeadless) return 0;
            float seconds = RollSeconds;
            int n = 0;
            for (int k = 0; k < BandCount; k++) {
                float u = k / (BandCount - 1f), rs = seconds + 0.1f * k;
                float slide = SkyfallCurves.Slide(u), height = SkyfallCurves.BandHeight(k, BandCount), radius = SkyfallCurves.BandRadius(k, BandCount);
                float roll = SkyfallCurves.RollOut(u * rs, rs);
                int frame = SkyfallCurves.RollFrame(u * rs, rs);
                if (float.IsNaN(slide) || float.IsNaN(height) || float.IsNaN(radius) || float.IsNaN(roll) || frame < 0 || frame > 7)
                    throw new InvalidOperationException("a Skyfall World-Nail number is not a number");
                n += 5;
            }
            return n;
        }
    }
}
