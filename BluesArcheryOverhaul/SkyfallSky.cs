using System;
using System.Collections.Generic;
using Alta.Environmental.Atmosphere;
using Alta.Utilities;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Skyfall's sky dial (Task 10): clients only, looks only. Near a storm (and, faintly, while its caster holds a draw at the sky) the
    // sky dims through the game's own cave dimming, never tinted: DayTimeManager's private LightIntensityModifier (the sun and moon,
    // 1 = unchanged) and skyColorFade (the sky colour's fade, 0 = none) are written through LerpingFloatValue.Snap, which raises no
    // FinishedLerp, so DisableSkyColor (DayTimeManager.cs:354-367: the skybox's _DISABLED keyword above 0.99) never blackens the sky;
    // the fade is also held to 0.95 at most. Each look asks with Dim(key, centre, from, until, strength) (SkyDial.Amount ramps it in and
    // out); Frame takes the largest within [Skyfall] SkyDimMetres of the local head and slews toward it (SkyfallCurves.Toward).
    // The ways out, each putting the sky back:
    //   - a storm ends: its request's until passes, the dial slews back to 0 and lets go (Snap 1 and 0);
    //   - a cave: the plugin is disabled and the game's own Disable() fades it (the dial yields while the game lerps, from 0 after);
    //   - a scene change or disconnect: the DayTimeManager is destroyed (the next one resets itself on Awake) or Player.Current is null;
    //   - a timeout: no request lives past MaxDimSeconds after its start;
    //   - [Skyfall] SkyDim switched off: Restore (the game's own lerp back).
    // Nothing on the server: every entry point returns at once when headless (the server's DayTimeManager disables itself there).
    internal static class SkyfallSky {
        internal const uint HoldKey = 0u;                                   // the caster's hold; a storm asks with its arrow id
        const float MaxDimSeconds = 60f;                                    // the timeout: no request lives longer after its start
        const int MaxRequests = 32;

        static MelonPreferences_Entry<bool>? skyDim;

        struct Request { public Vector3 Centre; public float From, Until, Strength; }
        static readonly Dictionary<uint, Request> requests = new Dictionary<uint, Request>();
        static readonly List<uint> scratch = new List<uint>();

        static DayTimeManager? dtm;
        static LerpingFloatValue? light, fade;
        static bool driving, dialFailed, liveSaid, fullSaid;
        static float shown, nextFind;

        internal static void Init(MelonPreferences_Category cat) {
            skyDim = cat.CreateEntry("SkyDim", true, description: "Clients (looks only): dim the sky near a storm on this machine (the game's own cave dimming, never tinted). Off: Skyfall never touches the sky here.");
            Cfg.Reloaded += () => dialFailed = false;
        }

        /// <summary>Adds or replaces the request under <paramref name="key"/>: dim at <paramref name="strength"/> (0 to 1) of the storm's
        /// dim near <paramref name="centre"/>, from <paramref name="from"/> until <paramref name="until"/> (Time.time; held to at most
        /// MaxDimSeconds after from). At most 32 requests; a new key beyond that is ignored. Nothing on the headless server.</summary>
        internal static void Dim(uint key, Vector3 centre, float from, float until, float strength) {
            if (ApplicationManager.IsHeadless) return;
            if (!Finite(from) || float.IsNaN(until) || !Finite(centre.x) || !Finite(centre.y) || !Finite(centre.z)) return;
            if (until > from + MaxDimSeconds) until = from + MaxDimSeconds;
            if (!requests.ContainsKey(key) && requests.Count >= MaxRequests) {
                if (!fullSaid && Logs.Debug) { fullSaid = true; MelonLogger.Msg($"[Archery] Skyfall: the sky dial has {MaxRequests} requests; a new one is ignored (said once)"); }
                return;
            }
            requests[key] = new Request { Centre = centre, From = from, Until = until, Strength = strength > 0f ? (strength < 1f ? strength : 1f) : 0f };
        }

        /// <summary>That request ends now (the dial still eases back over SkyFadeSeconds).</summary>
        internal static void Undim(uint key) {
            if (requests.Count == 0 || !requests.TryGetValue(key, out Request r)) return;
            float now = Time.time;
            if (r.Until > now) { r.Until = now; requests[key] = r; }
        }

        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        static bool NoPlayer() {
            var me = global::Player.Current;
            return me == null || (me is UnityEngine.Object o && o == null);   // a destroyed player counts as none
        }

        static readonly int T_FindDial = Timing.Section("asset SkyfallSky.FindDial (DayTimeManager)");
        /// <summary>The DayTimeManager among AtmosphereManager.instance's plugins and its two lerpers (Traverse, as Fx.cs:255). While
        /// nothing is found, searched again at most once a second. A throw (a renamed field) stops the dial until the settings are
        /// reloaded.</summary>
        static void FindDial() {
            if (dialFailed || Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 1f;
            using var _t = Timing.Begin(T_FindDial);
            try {
                AtmosphereManager am = AtmosphereManager.instance;
                if (am == null) return;
                AtmospherePlugin[] plugins = am.AtmospherePlugins;
                if (plugins == null) return;
                foreach (AtmospherePlugin p in plugins) {
                    if (!(p is DayTimeManager d) || d == null) continue;
                    Traverse t = Traverse.Create(d);
                    Traverse lt = t.Field("LightIntensityModifier"), ft = t.Field("skyColorFade");
                    if (!lt.FieldExists() || !ft.FieldExists()) throw new MissingFieldException("DayTimeManager", !lt.FieldExists() ? "LightIntensityModifier" : "skyColorFade");
                    LerpingFloatValue l = lt.GetValue<LerpingFloatValue>(), f = ft.GetValue<LerpingFloatValue>();
                    if (l == null || f == null) return;                  // not set up yet: asked again in a second
                    dtm = d; light = l; fade = f;
                    return;
                }
            }
            catch (Exception e) {
                dialFailed = true;
                MelonLogger.Warning($"[Archery] Skyfall: the sky dial could not be found ({e.Message}); the sky is not dimmed until the settings are reloaded");
            }
        }

        /// <summary>Main.OnLateUpdate, after every look has asked: the dial slewed toward the largest request in range, written only
        /// while the game is not lerping it itself and the plugin is enabled (not in a cave). One count test while nothing asks.</summary>
        internal static void Frame() {
            if (ApplicationManager.IsHeadless || skyDim == null) return;
            if (!skyDim.Value || NoPlayer()) { Restore(); return; }
            if (requests.Count == 0 && !driving) { shown = 0f; return; }
            if (!ReferenceEquals(dtm, null) && dtm == null) {              // destroyed (a scene change): the next one reset itself on Awake
                dtm = null; light = fade = null;
                driving = false; shown = 0f;
            }
            if (dtm == null) FindDial();
            float now = Time.time;
            SkyfallValues v = Skyfall.Values;
            scratch.Clear();
            foreach (var kv in requests)
                if (now > kv.Value.Until + v.SkyFadeSeconds || now > kv.Value.From + MaxDimSeconds) scratch.Add(kv.Key);
            foreach (uint k in scratch) requests.Remove(k);
            if (dtm == null || light == null || fade == null) { shown = 0f; driving = false; return; }

            float want = 0f;
            Vector3? head = Targets.HeadOf(global::Player.Current as global::Player);
            if (head.HasValue) {
                float range2 = v.SkyDimMetres * v.SkyDimMetres;
                bool cave = !dtm.IsEnabled;
                foreach (var kv in requests) {
                    Request r = kv.Value;
                    if ((r.Centre - head.Value).sqrMagnitude > range2) continue;
                    float a = SkyDial.Amount(true, cave, now - r.From, r.Until - now, v.SkyFadeSeconds) * r.Strength;
                    if (a > want) want = a;
                }
            }

            // the game owns the dial in a cave and during its own fade; after one, ours starts again from 0
            bool canWrite = dtm.IsEnabled && dtm.IsInitialized && !light.IsLerping && !fade.IsLerping;
            if (!canWrite) { shown = 0f; driving = false; return; }

            shown = SkyfallCurves.Toward(shown, want, Time.deltaTime, v.SkyFadeSeconds);
            if (shown > 0f) {
                float skyFade = Mathf.Min(v.SkyFade, SkyfallValues.MaxSkyFade);
                SnapIf(light, 1f - shown * (1f - v.SkySun));
                SnapIf(fade, shown * skyFade);
                driving = true;
                if (!liveSaid && Logs.Summary) {
                    liveSaid = true;
                    MelonLogger.Msg($"[Archery] Skyfall: the sky dial is live (sun to {v.SkySun.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}, sky fade to {skyFade.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}) (said once)");
                }
            }
            else if (driving) {
                light.Snap(1f);
                fade.Snap(0f);
                driving = false;
            }
        }

        static void SnapIf(LerpingFloatValue l, float value) {
            if (Mathf.Abs(l.Current - value) > 1e-4f) l.Snap(value);
        }

        /// <summary>Every request dropped; a sky this dial was dimming eases back through the game's own lerp (when its DayTimeManager
        /// is alive and enabled; in a cave the game's own fade owns it). Never throws.</summary>
        internal static void Restore() {
            try {
                requests.Clear();
                if (driving && dtm != null && dtm.IsEnabled && light != null && fade != null) {
                    float s = Mathf.Max(0.1f, Skyfall.Values.SkyFadeSeconds);
                    light.Lerp(1f, s);
                    fade.Lerp(0f, s);
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: the sky could not be put back ({e.Message})"); }
            driving = false;
            shown = 0f;
        }

        /// <summary>SelfCheck (0 when headless): SkyDial.Amount and SkyfallCurves.Toward with the settings just loaded, at 5 times, and
        /// the sun and sky-fade values they would write. Throws on NaN; returns the count evaluated.</summary>
        internal static int Check() {
            if (ApplicationManager.IsHeadless) return 0;
            SkyfallValues v = Skyfall.Values;
            int n = 0;
            for (int i = 0; i < 5; i++) {
                float t = i * 0.75f;                                         // 0, 0.75, 1.5, 2.25, 3 s
                float a = SkyDial.Amount(true, false, t, 3f - t, v.SkyFadeSeconds);
                float s = SkyfallCurves.Toward(0.5f, a, 0.016f, v.SkyFadeSeconds);
                float sun = 1f - s * (1f - v.SkySun), sky = s * Mathf.Min(v.SkyFade, SkyfallValues.MaxSkyFade);
                if (float.IsNaN(a) || float.IsNaN(s) || float.IsNaN(sun) || float.IsNaN(sky)) throw new InvalidOperationException("a Skyfall sky dial number is not a number");
                n += 2;
            }
            return n;
        }
    }
}
