using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Alta.Networking;
using Alta.StatSystem;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // The Balance Ring (client, the local player only): on a creature the local player's own arrows hit (real or spectral),
    // a ring on the ground that fills as the creature's cripple pool drains, as the client receives it (the cripple stat
    // reaches a client only after it moved by more than its sync step). Fades HoldSeconds after the last hit; shatters
    // when the received pool reaches its minimum (the game knocks the creature down). The easing and fade are BalanceRule.
    internal static class Balance {
        static MelonPreferences_Entry<bool> enabled = null!;
        static MelonPreferences_Entry<string> colour = null!;
        static MelonPreferences_Entry<float> width = null!, holdSeconds = null!, fadeSeconds = null!, easeRate = null!, radiusScale = null!;
        static MelonPreferences_Entry<int> shatterSparks = null!;
        static readonly Color DefaultColour = new Color(1f, 0.78f, 0.2f);

        const int Max = 6, ArcSegments = 64;                               // internal, not settings
        const float MinRadius = 0.4f, MaxRadius = 2f, FallbackRadius = 0.8f, Lift = 0.03f, GroundProbe = 3f,
            FlickerSeconds = 0.12f, FlickerDim = 0.25f, SparkSpread = 70f, SparkSpeed = 3f;
        const string CracksMaterial = "Particle_RingCracks";

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        internal static void Init() {
            var cat = Cfg.Category("Balance");
            enabled = cat.CreateEntry("Enabled", true, description: "The Balance Ring under creatures your own arrows hit: it fills as the creature's cripple pool drains (as your game receives it) and shatters when the creature goes down.");
            colour = cat.CreateEntry("Colour", "#FFC733", description: "Colour of the ring's cracks and its arc.");
            width = cat.CreateEntry("Width", 0.02f, description: "Width in metres of the ring's arc.");
            holdSeconds = cat.CreateEntry("HoldSeconds", 2f, description: "Seconds the ring stays after your last hit on that creature.");
            fadeSeconds = cat.CreateEntry("FadeSeconds", 0.5f, description: "Seconds it then takes to fade out.");
            easeRate = cat.CreateEntry("EaseRate", 8f, description: "How fast the ring follows the received pool (higher = snappier).");
            radiusScale = cat.CreateEntry("RadiusScale", 1f, description: "The ring's radius as a share of the creature's size (clamped to 0.4 to 2 m).");
            shatterSparks = cat.CreateEntry("ShatterSparks", 12, description: "Sparks thrown when the ring shatters (the creature went down).");
            Cfg.Reloaded += () => { cracksResolved = false; };
        }

        // ---------------------------------------------------------------- the cracks material

        static Material? cracks;
        static bool cracksResolved;

        /// <summary>'Particle_RingCracks', found as Fx finds 'WeaponTrail' (a loaded particle renderer's shared material), once;
        /// a miss is kept until the settings are reloaded.</summary>
        static readonly int T_Cracks = Timing.Section("asset Balance.Cracks (FindObjectsOfTypeAll)");
        static Material? Cracks() {
            using var _t = Timing.Begin(T_Cracks);
            if (cracksResolved) return cracks;
            cracksResolved = true;
            try {
                cracks = Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>()
                    .Where(r => r != null && r.sharedMaterial != null && r.sharedMaterial.name == CracksMaterial)
                    .Select(r => r.sharedMaterial).FirstOrDefault();
            }
            catch (Exception e) { cracks = null; MelonLogger.Warning($"[Archery] Balance: looking for '{CracksMaterial}' failed: {e.Message}"); }
            if (cracks == null) MelonLogger.Warning($"[Archery] Balance: cracks material '{CracksMaterial}' NOT FOUND; the ring is drawn as its arc only"); // G5 (M-5): a warning at every level
            else if (Logs.Summary) MelonLogger.Msg($"[Archery] Balance: cracks material '{cracks.name}' ({Fx.ShaderName(cracks)})");
            return cracks;
        }

        // ---------------------------------------------------------------- the rings

        sealed class Ring {
            public HealthObject Target = null!;
            public Transform Root = null!;
            public string Name = "?";
            public Stat? Pool;
            public StatHandler? OnPool;
            public GameObject Go = null!;
            public Material? QuadMat;
            public LineRenderer Arc = null!;
            public float Radius, Shown, LastHit, FlickerAt = -1f;
            public bool Down;
            public int[] ColourIds = new int[0];          // G2: the quad material's colour properties, found once at Create
        }

        static readonly string[] colourProps = { "_Color", "_TintColor", "_BaseColor" };

        static readonly Dictionary<int, Ring> rings = new Dictionary<int, Ring>();
        static readonly List<int> gone = new List<int>();

        /// <summary>Client: the local player's own arrow (real or spectral) hit this creature. <paramref name="chargeTier"/>
        /// 2 or higher flickers the ring once (the crack).</summary>
        static readonly int T_Hit = Timing.Section("look start Balance.Hit");
        internal static void Hit(HealthObject target, int chargeTier = 0) {
            using var _t = Timing.Begin(T_Hit);
            if (ApplicationManager.IsHeadless || enabled == null || !enabled.Value || target == null || target is PlayerHealth) return;
            if (!target.HasCrippleStat) return; // creatures only: a target or prop with health has no balance to show
            try {
                int key = target.GetInstanceID();
                float now = Time.time;
                if (!rings.TryGetValue(key, out Ring r) || r.Go == null) {
                    if (r != null) Remove(key);
                    while (rings.Count >= Max) {
                        int oldest = 0; float last = float.MaxValue;
                        foreach (KeyValuePair<int, Ring> kv in rings) if (kv.Value.LastHit < last) { last = kv.Value.LastHit; oldest = kv.Key; }
                        Remove(oldest);
                    }
                    r = Create(target);
                    rings[key] = r;
                }
                r.LastHit = now;
                if (chargeTier >= 2) r.FlickerAt = now;
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Balance: hit on '{(target != null ? target.name : "?")}' failed: {e.Message}"); }
        }

        static readonly int T_Create = Timing.Section("look Balance.Create (ring)");
        static Ring Create(HealthObject target) {
            using var _t = Timing.Begin(T_Create);
            Transform root = Targets.RootOf(target);
            var r = new Ring { Target = target, Root = root, Name = target.name };
            r.Radius = Mathf.Clamp(SizeOf(root) * Mathf.Max(0f, radiusScale.Value), MinRadius, MaxRadius);
            r.Go = new GameObject("ArcheryBalanceRing");
            Material? m = Cracks();
            if (m != null) {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Collider c = q.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c); // never something an arrow or a raycast can hit
                q.name = "Cracks";
                q.transform.SetParent(r.Go.transform, false);
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat, facing up
                q.transform.localScale = Vector3.one * (2f * r.Radius);
                r.QuadMat = new Material(m) { name = m.name + " (Archery Balance)" };
                var ids = new List<int>();
                foreach (string p in colourProps) if (r.QuadMat.HasProperty(p)) ids.Add(Shader.PropertyToID(p));
                r.ColourIds = ids.ToArray();
                MeshRenderer mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = r.QuadMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            var arcGo = new GameObject("Arc");
            arcGo.transform.SetParent(r.Go.transform, false);
            r.Arc = Fx.NewLine(arcGo, 2, width.Value, false);
            if (target.HasCrippleStat && target.CrippleStat != null) {
                Ring me = r;
                r.Pool = target.CrippleStat;
                r.Shown = BalanceRule.Drained(r.Pool.Value, r.Pool.Minimum, r.Pool.Maximum);
                r.OnPool = s => Received(me, s);
                r.Pool.Changed += r.OnPool;
            }
            Place(r);
            float v = r.Pool != null ? r.Pool.Value : float.NaN, max = r.Pool != null ? r.Pool.Maximum : float.NaN;
            if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Balance: ring on '{r.Name}' (radius {r.Radius:0.00} m, pool {v:0.00} of {max:0.00})"));
            return r;
        }

        /// <summary>The creature's horizontal half-size from its colliders' bounds (its renderers' when none has a size).</summary>
        static float SizeOf(Transform root) {
            bool any = Targets.BoundsOf(root, out Bounds b, true);
            if (!any)
                foreach (Renderer rd in root.GetComponentsInChildren<Renderer>(false)) {
                    if (rd == null || !rd.enabled) continue;
                    if (!any) { b = rd.bounds; any = true; } else b.Encapsulate(rd.bounds);
                }
            return any ? Mathf.Max(b.extents.x, b.extents.z) : FallbackRadius;
        }

        /// <summary>The creature's cripple stat changed as this client received it (Stat.Changed: LastValue is still the old value).</summary>
        static readonly int T_Received = Timing.Section("event Balance.Received (creature pool changed)");
        static void Received(Ring r, Stat s) {
            using var _t = Timing.Begin(T_Received);
            try {
                if (s == null) return;
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Balance: '{r.Name}' pool received {s.LastValue:0.00} -> {s.Value:0.00} of {s.Maximum:0.00}"));
                if (s.Value <= s.Minimum + 1e-4f) r.Down = true; // shattered in Frame, never inside the stat's event
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Balance: reading '{r.Name}' pool failed: {e.Message}"); }
        }

        static readonly int T_Place = Timing.Section("look Balance.Place (ground ray per ring)");
        static void Place(Ring r) {
            using var _t = Timing.Begin(T_Place);
            Vector3 at = r.Root.position;
            // flat on the ground under the creature (anything that is not the creature), else at its feet. G2: still cast every frame:
            // keeping the point while the creature stands still would miss anything that moves under it (not provably the same).
            if (Hits.Cast(at + Vector3.up, Vector3.down, GroundProbe, out RaycastHit h, r.Root)) at = h.point;
            r.Go.transform.position = at + Vector3.up * Lift;
            r.Go.transform.rotation = Quaternion.identity;
        }

        /// <summary>Every frame (Main.OnUpdate): each ring follows its creature, eases toward the received pool, fades, shatters.</summary>
        internal static void Frame() {
            if (rings.Count == 0 || ApplicationManager.IsHeadless) return;
            gone.Clear();
            float now = Time.time, dt = Time.deltaTime;
            bool on = enabled.Value;
            foreach (KeyValuePair<int, Ring> kv in rings) {
                Ring r = kv.Value;
                try {
                    if (!on || r.Go == null || r.Target == null || r.Root == null) { gone.Add(kv.Key); continue; }
                    if (r.Down) { Shatter(r); gone.Add(kv.Key); continue; }
                    float alpha = BalanceRule.Alpha(now, r.LastHit, holdSeconds.Value, fadeSeconds.Value);
                    if (alpha <= 0f) { gone.Add(kv.Key); continue; }
                    if (r.FlickerAt >= 0f && now - r.FlickerAt < FlickerSeconds) alpha *= FlickerDim; // the crack: one dip
                    float target = r.Pool != null ? BalanceRule.Drained(r.Pool.Value, r.Pool.Minimum, r.Pool.Maximum) : 0f;
                    r.Shown = BalanceRule.Ease(r.Shown, target, dt, easeRate.Value);
                    Place(r);
                    Draw(r, alpha);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Balance: ring on '{r.Name}' failed: {e.Message}"); gone.Add(kv.Key); }
            }
            foreach (int k in gone) Remove(k);
        }

        static readonly int T_Draw = Timing.Section("look Balance.Draw (per ring)");
        static void Draw(Ring r, float alpha) {
            using var _t = Timing.Begin(T_Draw);
            Color c = Cfg.Colour(colour, DefaultColour);
            if (r.QuadMat != null) {
                Color q = new Color(c.r, c.g, c.b, Mathf.Clamp01((0.3f + 0.7f * r.Shown) * alpha));
                foreach (int id in r.ColourIds) r.QuadMat.SetColor(id, q);
            }
            float shown = Mathf.Clamp01(r.Shown);
            if (shown <= 0.001f) { r.Arc.enabled = false; return; }
            r.Arc.enabled = true;
            int n = Mathf.Max(2, Mathf.CeilToInt(ArcSegments * shown) + 1);
            r.Arc.positionCount = n;
            Vector3 centre = r.Go.transform.position;
            float sweep = shown * Mathf.PI * 2f;
            for (int i = 0; i < n; i++) {
                float a = Mathf.PI * 0.5f - sweep * i / (n - 1);
                r.Arc.SetPosition(i, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r.Radius);
            }
            r.Arc.widthMultiplier = width.Value;
            Color arc = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(alpha));
            r.Arc.startColor = arc;
            r.Arc.endColor = arc;
        }

        static void Shatter(Ring r) {
            Vector3 at = r.Go != null ? r.Go.transform.position : r.Root.position;
            VolleyFx.Sparks.Emit(at + Vector3.up * 0.1f, Vector3.up, Mathf.Max(0, shatterSparks.Value), Cfg.Colour(colour, DefaultColour), SparkSpread, SparkSpeed);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Balance: '{r.Name}' went down; ring shattered");
        }

        static void Remove(int key) {
            if (!rings.TryGetValue(key, out Ring r)) return;
            rings.Remove(key);
            try { if (r.Pool != null && r.OnPool != null) r.Pool.Changed -= r.OnPool; } catch { }
            if (r.Go != null) UnityEngine.Object.Destroy(r.Go);
            if (r.QuadMat != null) UnityEngine.Object.Destroy(r.QuadMat);
        }
    }
}
