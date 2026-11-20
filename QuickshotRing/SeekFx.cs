using System;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Batch H2, the arrival of a locked (homing) release. Looks only; every client that draws the release; SeekLook.cs decides when
    // and with which numbers.

    /// <summary>The green burst where a locked release's arrows land: sparks from the mod's one spark emitter (the game's 'Sparks'
    /// particle material), one short light, the game's own sound, and optionally one of the game's pooled effects at a set size.
    /// The lighter one on each locked creature; the heavier one (more sparks thrown back along the arrows' way and outward, a
    /// brighter and wider light) where the core cluster lands. The creature's cage being thrown outward is LockCage's.</summary>
    internal static class SeekBurst {
        const float Spread = 70f, Speed = 2.5f, HeavySpread = 80f, HeavySpeed = 4.5f, HeavySize = 1.4f, LightRange = 2.5f, HeavyLightRange = 4.5f, BrightBoost = 1.4f;
        static bool effectLogged;

        /// <param name="point">Where the arrows hit.</param>
        /// <param name="centre">The creature's middle (the light sits there; the point itself for a landing on anything else).</param>
        /// <param name="back">The way the sparks are thrown (up for the lighter burst, back along the arrows' way for the heavier).</param>
        static readonly int T_Play = Timing.Section("look start SeekBurst.Play (arrival burst)");
        internal static void Play(Vector3 point, Vector3 centre, Vector3 back, Color colour, float brightness, int sparks, float light, float seconds,
                                  string sound, float volume, string effect, float effectScale, bool heavy) {
            using var _t = Timing.Begin(T_Play);
            Color c = heavy ? GoldHold.Brighter(colour, BrightBoost) : colour;
            var spark = new Color(c.r, c.g, c.b, Mathf.Clamp01(brightness));
            if (sparks > 0) {
                if (heavy) {
                    // half thrown back the way the arrows came, half outward all round, so it is a burst and not a spray
                    VolleyFx.Sparks.Emit(point, back, sparks / 2, spark, Spread, HeavySpeed, HeavySize);
                    VolleyFx.Sparks.Emit(point, Vector3.up, sparks - sparks / 2, spark, HeavySpread, HeavySpeed * 0.7f, HeavySize);
                }
                else VolleyFx.Sparks.Emit(point, back, sparks, spark, Spread, Speed);
            }
            if (light > 0f) Fx.Flash(heavy ? point : centre, c, light * brightness, heavy ? HeavyLightRange : LightRange, Mathf.Max(0.05f, seconds));
            if (!string.IsNullOrWhiteSpace(sound) && volume > 0f) VolleyFx.Play(sound, point, 0f, volume);
            if (!string.IsNullOrWhiteSpace(effect) && effectScale > 0f) {
                PooledObjectDefinition? def = Fx.FindEffect(effect); // a miss is logged by Fx
                if (def != null) {
                    Quaternion rot = back.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(back) : Quaternion.identity;
                    if (Fx.GetScaled(def, point, rot, effectScale) && !effectLogged) {
                        effectLogged = true;
                        if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek look: arrival bursts also show '{def.name}' at {effectScale.ToString("0.##", CultureInfo.InvariantCulture)} size");
                    }
                }
            }
        }
    }

    /// <summary>The real arrow of a locked release is shown again where the core cluster ended (it was hidden while the cluster
    /// flew in its place). So that it does not pop into view, a green ghost of it (the volley's own ghost arrow: a copy of its head
    /// in the game's skill-glow material and a shaft line) is drawn over it, a little larger, and fades out: the spectral arrow is
    /// seen to become the real one, inside the core's burst. Follows the arrow while it fades (it may be stuck in a creature that
    /// moves) and ends with it. One small object for a third of a second per locked release.</summary>
    internal sealed class SeekReveal : MonoBehaviour {
        const float Length = 0.7f, Scale = 1.1f;
        Arrow? arrow;
        Volley.Ghost? ghost;
        float born, seconds;

        static readonly int T_Play = Timing.Section("look start SeekReveal.Play (the real arrow flashed in)");
        internal static void Play(Arrow arrow, float seconds, float brightness) {
            using var _t = Timing.Begin(T_Play);
            if (Fx.LineMaterial() == null) return;
            var go = new GameObject("ArcherySeekReveal");
            Volley.Ghost? g = null;
            try {
                g = Volley.Ghost.Create(arrow, 0, rampTestOk: false);
                g.Style(false, brightness);
                g.Homing = true;
                g.Bigger(Scale);
                SeekReveal r = go.AddComponent<SeekReveal>();
                r.arrow = arrow; r.ghost = g; r.born = Time.time; r.seconds = Mathf.Max(0.02f, seconds);
                r.Frame(1f);
            }
            catch {
                if (g != null) Destroy(g.gameObject);
                Destroy(go);
                throw;
            }
        }

        void Frame(float alpha) {
            Transform tip = arrow!.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
            ghost!.Fade(tip.position, arrow.transform.forward, Length, alpha);
        }

        void End() {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            Destroy(gameObject);
        }

        static readonly int TLateUpdate = Timing.Section("LateUpdate SeekReveal (the real arrow flashed in)");
        void LateUpdate() {
            using var _t = Timing.Begin(TLateUpdate);
            float t = (Time.time - born) / seconds;
            if (arrow == null || ghost == null || !(t < 1f)) { End(); return; }
            try { Frame(1f - t); }
            catch (Exception e) {
                MelonLogger.Warning($"[Archery] Seek look: the real arrow's flash-in ended early: {e.Message}");
                End();
            }
        }
    }
}
