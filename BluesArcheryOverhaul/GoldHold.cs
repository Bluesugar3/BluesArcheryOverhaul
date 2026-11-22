using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // G4 (play-test 8, the user agreed to: "After the volley is full, each Charge Shot orb adds one more stage to hold for. At each
    // stage the ghosts turn gold ring by ring with a thump"). The look of a multi-arrow main's hold stages (clients only; looks
    // only). The stage is VolleyHold's (Release.cs, G3: one function, read through Volley.DrawHeld and the release's HoldStage);
    // which ring turns at which stage, the sweep round a ring and the shimmer are GoldStages (Spectral.cs, tested).
    //   On the bow   before stage 1 the ghosts keep their own colour; a faint gold shimmer creeps along their shafts from the back
    //                toward the tip as the next stage comes nearer (colour, brightness and width of the ghosts' own shaft lines, no
    //                extra object). At each stage: the Charge Shot's own thump sound, its pulse in the shooter's bow hand, a short
    //                gold light at the arrow's tip, and gold sweeps round the ring that turns (top first, clockwise, over
    //                SweepSeconds, each arrow easing over TurnSeconds with the volley's own ripple on its head and shaft). Burst's
    //                fifteen pellet arrows turn with the ring they sit on (10 arrows inside, 20 outside). The brighter level (the
    //                third stage): every shaft whiter, brighter and wider, a small gold glint on every head (a bead at the head's
    //                rear, facing the archer) and one small gold light at the tip.
    //   In flight    each ghost and pellet carries the level of its ring at the stage the release reached (Volley.Launch): shaft
    //                and streak colour, the glint, gold landing sparks. Stage 0: none of it.
    //   Who sees it  the shooter from its own draw clock; another player from the time since the Volley skill's status while the bow
    //                is held (so its thumps can differ from the shooter's by the status's delay) and from the time the server
    //                relayed at the release. Sounds for everyone near, the pulse only in the shooter's own bow hand.
    // Everything is the game's or the volley's own: the ghosts' shaft lines and head material, [Volley] ChargedColour, the Charge
    // Shot's thump ([Fill] Charge2ThumpSound, ThumpHaptic), the flat line material for the glint. If the look cannot be built: one
    // log line, and the ghosts go back to the one switch (all gold from stage 1) until the settings are reloaded.
    // Settings in [GoldHold] (looks, not shared); this file reads only its own entries and leaf values (colours, the thump).
    internal static class GoldHold {
        static MelonPreferences_Entry<bool> enabled = null!, glints = null!;
        static MelonPreferences_Entry<float> sweepSeconds = null!, turnSeconds = null!, shimmerStrength = null!, shimmerSoft = null!, shimmerGlow = null!,
            shimmerWidth = null!, shimmerFlicker = null!, shimmerHz = null!, brightBoost = null!, brightWidth = null!, flareSeconds = null!,
            flashBrightness = null!, flashSeconds = null!, flashRange = null!, thumpVolume = null!, haptic = null!, glintRadius = null!, glintWidth = null!,
            glintAlpha = null!, glintTwinkle = null!, glintHz = null!, lampBrightness = null!, lampRange = null!;
        static MelonPreferences_Entry<string> thumpSound = null!, glintColour = null!;
        static readonly Color DefaultGlint = new Color(1f, 0.945f, 0.761f); // #FFF1C2, the pale gold of Holy Arrow's wind-up
        const int Cap = 64;
        const float Never = -1e9f;
        /// <summary>Metres behind the rear of a ghost's head the glint sits (Volley.Ghost.HeadRear): seen from behind, in front of the head.</summary>
        internal const float GlintBehind = 0.005f;
        static bool failed;

        internal static void Init() {
            var cat = Cfg.Category("GoldHold");
            enabled = cat.CreateEntry("Enabled", true, description: "A multi-arrow archer's Charge Shot orbs: while a full volley is held the ghosts turn gold ring by ring, one stage per orb, with a thump at each. Off = the look before 0.16.0's: every ghost gold from the first stage. Looks only: the stages themselves are not changed by this.");
            sweepSeconds = cat.CreateEntry("SweepSeconds", 0.2f, description: "Seconds the gold takes to sweep round a ring at its stage: the top arrow first, then clockwise. 0 = the whole ring at once.");
            turnSeconds = cat.CreateEntry("TurnSeconds", 0.06f, description: "Seconds each arrow takes to turn gold when its turn in the sweep comes.");
            shimmerStrength = cat.CreateEntry("ShimmerStrength", 0.5f, description: "Before a stage: how strongly (0 to 1) the shimmer tints a ghost's shaft toward the next stage's gold as it creeps from the back of the shaft to the tip. 0 = no shimmer.");
            shimmerSoft = cat.CreateEntry("ShimmerSoft", 0.35f, description: "How soft the shimmer's front is, as a share of the shaft that is drawn (0 = a hard edge).");
            shimmerGlow = cat.CreateEntry("ShimmerGlow", 0.3f, description: "The shaft is also this much more opaque where the shimmer has reached, so the faded back end lights up as it fills. 0 = colour only.");
            shimmerWidth = cat.CreateEntry("ShimmerWidth", 0.25f, description: "The shafts of the ring that turns next grow this much wider as its stage comes nearer (0.25 = a quarter wider just before the thump).");
            shimmerFlicker = cat.CreateEntry("ShimmerFlicker", 0.2f, description: "How much (0 to 1) the shimmer flickers, each arrow in its own time.");
            shimmerHz = cat.CreateEntry("ShimmerHz", 7f, description: "Flickers per second of the shimmer.");
            brightBoost = cat.CreateEntry("BrightBoost", 0.6f, description: "The third stage (everything brightens): the gold shafts, pellet streaks and arrows in flight are this much brighter (toward white and more opaque).");
            brightWidth = cat.CreateEntry("BrightWidth", 0.3f, description: "The third stage: the shafts are this much wider.");
            flareSeconds = cat.CreateEntry("FlareSeconds", 0.25f, description: "Seconds of the ripple on each arrow as it turns at a stage (its head dips and its shaft brightens by [Volley] RippleBoost). 0 = none.");
            flashBrightness = cat.CreateEntry("FlashBrightness", 1.8f, description: "Brightness of the short gold light at the arrow's tip at each stage's thump. 0 = none.");
            flashSeconds = cat.CreateEntry("FlashSeconds", 0.15f, description: "Seconds that light takes to go out.");
            flashRange = cat.CreateEntry("FlashRange", 1f, description: "Metres that light reaches.");
            thumpSound = cat.CreateEntry("ThumpSound", "", description: "Sound at the arrow's tip at each stage (names separated by ';', the first one found is used). Empty = the Charge Shot's own thump ([Fill] Charge2ThumpSound).");
            thumpVolume = cat.CreateEntry("ThumpVolume", 1f, description: "Volume (0 to 1) of that sound. 0 = no sound.");
            haptic = cat.CreateEntry("Haptic", 1f, description: "The thump in your bow hand at each stage, as a share of the Charge Shot's own ([Fill] ThumpHaptic at stage 1, Charge2ThumpHaptic at stage 2, full at stage 3). 0 = none.");
            glints = cat.CreateEntry("Glints", true, description: "The third stage: a small gold glint on every ghost's and pellet arrow's head, on the bow and in flight.");
            glintRadius = cat.CreateEntry("GlintRadius", 0.006f, description: "Radius in metres of a ghost's glint, a bead at the rear of its head where you see it from behind (a pellet arrow's and an outer ghost's are smaller with their heads).");
            glintWidth = cat.CreateEntry("GlintWidth", 0.006f, description: "Thickness in metres of the glint's bead.");
            glintColour = cat.CreateEntry("GlintColour", "#FFF1C2", description: "Colour of the glints.");
            glintAlpha = cat.CreateEntry("GlintAlpha", 0.9f, description: "Brightness (0 to 1) of the glints (other archers' also times [Volley] OtherArcherBrightness).");
            glintTwinkle = cat.CreateEntry("GlintTwinkle", 0.35f, description: "How much (0 to 1) the glints twinkle on the bow, each in its own time. 0 = steady.");
            glintHz = cat.CreateEntry("GlintHz", 3f, description: "Twinkles per second.");
            lampBrightness = cat.CreateEntry("LampBrightness", 0.6f, description: "The third stage: brightness of the small gold light at the arrow's tip while the volley is held. 0 = none.");
            lampRange = cat.CreateEntry("LampRange", 0.6f, description: "Range in metres of that light (it sits in the real arrow's head, so it shows on what is round it: the bow, your hands).");
            Cfg.Reloaded += () => failed = false; // a changed setting may be what makes it buildable: try again
        }

        /// <summary>The staged look is drawn ([GoldHold] Enabled, and it has not failed since the settings were last read).</summary>
        internal static bool On => !failed && enabled != null && enabled.Value;

        /// <summary>The look failed: one log line, and the ghosts are all gold from stage 1 (the look before it) until the settings are reloaded.</summary>
        internal static void Failed(string why) {
            if (failed) return;
            failed = true;
            MelonLogger.Warning($"[Archery] Gold hold: the staged look could not be built ({why}); the ghosts turn gold all at once at the first stage until the settings are reloaded");
        }

        internal static float ShimmerSoft => shimmerSoft != null ? Mathf.Max(0f, shimmerSoft.Value) : 0.35f;
        internal static float GlintRadius => glintRadius != null ? Mathf.Max(0f, glintRadius.Value) : 0.006f;
        internal static float GlintWidth => glintWidth != null ? Mathf.Max(0f, glintWidth.Value) : 0.006f;
        internal static Color GlintColour => glintColour != null ? Cfg.Colour(glintColour, DefaultGlint) : DefaultGlint;
        static float BrightBoost => brightBoost != null ? Mathf.Max(0f, brightBoost.Value) : 0.6f;
        static float BrightWidth => brightWidth != null ? Mathf.Max(0f, brightWidth.Value) : 0.3f;
        static float GlintStrength => glints != null && glints.Value ? Mathf.Clamp01(glintAlpha.Value) : 0f;

        /// <summary>Brighter = toward white (vertex colours clamp at 1), as the Fill and the heavy arrows brighten.</summary>
        internal static Color Brighter(Color c, float k) => k <= 1f ? c : Color.Lerp(c, Color.white, 1f - 1f / k);

        // ---------------------------------------------------------------- one arrow's look

        /// <summary>What one ghost or pellet arrow shows this frame (Volley.Ghost paints it). A struct: nothing is allocated.</summary>
        internal struct Tone {
            /// <summary>0 = the archer's own ghost colour, 1 = gold ([Volley] ChargedColour).</summary>
            public float Gold;
            /// <summary>1 = plain; above it the shaft is whiter and more opaque (the brighter level).</summary>
            public float Glow;
            /// <summary>Factor on the shaft's width.</summary>
            public float Width;
            /// <summary>0 to 1: the small glint on the head.</summary>
            public float Glint;
            /// <summary>The ripple on this arrow as it turns (0 to 1): its head dips, its shaft brightens (VolleyFx.Beats).</summary>
            public float Flare;
            /// <summary>Below 0: no shimmer. Else how far the shimmer has crept along the shaft (VolleyHold.Toward), toward the colour
            /// of <see cref="NextGold"/> and <see cref="NextGlow"/>, tinting by <see cref="ShimmerTint"/> and adding
            /// <see cref="ShimmerGlow"/> of opacity where it has reached.</summary>
            public float Shimmer, NextGold, NextGlow, ShimmerTint, ShimmerGlow;

            /// <summary>The one switch (a charged shot's ghosts, a heavy arrow, the look with [GoldHold] off): gold or not, nothing else.</summary>
            internal static Tone Plain(bool gold) => new Tone { Gold = gold ? 1f : 0f, Glow = 1f, Width = 1f, Shimmer = -1f };
        }

        /// <summary>In flight: an arrow of a ring at <paramref name="level"/> (GoldStages.Level at the stage the release reached).</summary>
        internal static Tone Flight(int level) {
            int over = Math.Max(0, level - 1);
            return new Tone {
                Gold = level >= 1 ? 1f : 0f, Glow = 1f + BrightBoost * over, Width = 1f + BrightWidth * over, Shimmer = -1f,
                Glint = GoldStages.Glints(level) ? GlintStrength : 0f,
            };
        }

        /// <summary>The colour of a pellet's streak in flight at <paramref name="level"/>: the ghosts' colour, gold, or brighter gold.</summary>
        internal static Color FlightColour(int level) => Brighter(Volley.GhostColour(level >= 1), 1f + BrightBoost * Math.Max(0, level - 1));

        // ---------------------------------------------------------------- one bow's hold

        sealed class Bow {
            public int stage;
            public bool seen;                                   // drawn before: a stage already reached when first seen has no thump
            public readonly float[] at = { Never, Never, Never, Never }; // Time.time stage k was reached here (index = stage)
            public Light? lamp;
        }

#if QUEST
        static readonly Dictionary<BowString, Bow> bows = new Dictionary<BowString, Bow>(Il.ByPointer<BowString>());
#else
        static readonly Dictionary<BowString, Bow> bows = new Dictionary<BowString, Bow>();
#endif

        /// <summary>This frame's hold on one bow, with the settings it is drawn with: <see cref="For"/> gives each arrow its look.</summary>
        internal struct Look {
            internal int Stage, Orbs, Rings;
            internal float Since, Toward, Now, Sweep, Turn, Bright, Wide, FlareLen, Glint, GlintSwing, GlintHz, Tint, TintGlow, TintWide, Flicker, FlickerHz;

            /// <summary>The arrow of ring <paramref name="ring"/> that sits <paramref name="azimuth"/> degrees round the real arrow
            /// (VolleyMath.Azimuth, BurstPattern.Pellet); <paramref name="index"/> only gives it a time of its own to flicker in.</summary>
            internal Tone For(int ring, double azimuth, int index) {
                int now = GoldStages.Level(ring, Stage, Orbs, Rings), before = GoldStages.Level(ring, Stage - 1, Orbs, Rings);
                float delay = now != before ? GoldStages.SweepDelay(azimuth, Sweep) : 0f;
                float turned = now != before ? GoldStages.Turned(Since, delay, Turn) : 1f;
                float shown = GoldStages.Shown(before, now, turned);
                float over = shown > 1f ? shown - 1f : 0f;
                var t = new Tone { Gold = shown >= 1f ? 1f : shown, Glow = 1f + Bright * over, Width = 1f + Wide * over, Shimmer = -1f };
                if (now != before && FlareLen > 0f) {
                    float age = Since - delay;
                    if (age >= 0f && age < FlareLen) t.Flare = Mathf.Sin(Mathf.PI * age / FlareLen);
                }
                if (over > 0f && Glint > 0f)
                    t.Glint = Mathf.Clamp01(over) * Glint * (1f - GlintSwing * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * GlintHz * Now + index * 2.4f)));
                if (Tint <= 0f) return t;
                float flicker = 1f - Flicker * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * FlickerHz * Now + index * 1.7f));
                if (now != before && turned < 1f) {
                    // waiting for its turn in the sweep, or turning: the shimmer it wore just before the thump stays until it has
                    // turned, so no arrow falls back to its old colour between the thump and its turn
                    t.Shimmer = 1f;
                    t.NextGold = now >= 1 ? 1f : 0f;
                    t.NextGlow = 1f + Bright * Math.Max(0, now - 1);
                    t.ShimmerTint = Tint * flicker;
                    t.ShimmerGlow = TintGlow;
                    t.Width *= 1f + TintWide * (1f - turned);
                }
                else if (Toward > 0f) {
                    int next = GoldStages.Level(ring, Stage + 1, Orbs, Rings);
                    if (next > now) {
                        t.Shimmer = Toward;
                        t.NextGold = next >= 1 ? 1f : 0f;
                        t.NextGlow = 1f + Bright * Math.Max(0, next - 1);
                        t.ShimmerTint = Tint * flicker;
                        t.ShimmerGlow = TintGlow;
                        t.Width *= 1f + TintWide * Toward;
                    }
                }
                return t;
            }
        }

        static Look Snapshot(int stage, float toward, int orbs, int rings, float since, float now) => new Look {
            Stage = stage, Orbs = orbs, Rings = rings, Since = since, Toward = Mathf.Clamp01(toward), Now = now,
            Sweep = Mathf.Max(0f, sweepSeconds.Value), Turn = Mathf.Max(0f, turnSeconds.Value), Bright = BrightBoost, Wide = BrightWidth,
            FlareLen = Mathf.Max(0f, flareSeconds.Value), Glint = GlintStrength, GlintSwing = Mathf.Clamp01(glintTwinkle.Value), GlintHz = Mathf.Max(0f, glintHz.Value),
            Tint = Mathf.Clamp01(shimmerStrength.Value), TintGlow = Mathf.Max(0f, shimmerGlow.Value), TintWide = Mathf.Max(0f, shimmerWidth.Value),
            Flicker = Mathf.Clamp01(shimmerFlicker.Value), FlickerHz = Mathf.Max(0f, shimmerHz.Value),
        };

        /// <summary>Every frame a multi-arrow archer with Charge Shot orbs holds a volley on this bow (Volley.DrawHeld):
        /// <paramref name="stage"/> and <paramref name="toward"/> are VolleyHold's for this machine's clock. A stage reached since
        /// the last frame thumps (sound and light for everyone, the pulse in <paramref name="hand"/>, the shooter's own bow hand);
        /// the third stage's light follows <paramref name="tip"/>. Returns this frame's look for the bow's arrows.</summary>
        static readonly int T_Frame = Timing.Section("look GoldHold.Frame");
        internal static Look Frame(BowString bow, bool local, int stage, float toward, int orbs, int rings, Vector3 tip, Controller? hand, float brightness) {
            using var _t = Timing.Begin(T_Frame);
            if (!bows.TryGetValue(bow, out Bow b)) {
                if (bows.Count >= Cap) Clear();
                bows[bow] = b = new Bow();
            }
            stage = Mathf.Clamp(stage, 0, b.at.Length - 1);
            float now = Time.time;
            if (!b.seen) { b.stage = stage; b.seen = true; }
            else if (stage > b.stage) {
                for (int k = b.stage + 1; k <= stage; k++) b.at[k] = now;
                b.stage = stage;
                Thump(bow, local, stage, orbs, tip, hand, brightness);
            }
            else if (stage < b.stage) {
                // the same string drawn again without a release in between (a cancelled volley): the later stages are to come again
                for (int k = stage + 1; k < b.at.Length; k++) b.at[k] = Never;
                b.stage = stage;
            }
            float since = stage > 0 ? now - b.at[stage] : 0f;
            Look look = Snapshot(stage, toward, orbs, rings, since, now);

            // the third stage's light at the tip, coming up with the sweep
            int lit = GoldStages.Level(GoldStages.Inner, stage, orbs, rings), was = GoldStages.Level(GoldStages.Inner, stage - 1, orbs, rings);
            float level = GoldStages.Shown(was, lit, lit != was ? GoldStages.Turned(since, 0f, look.Sweep) : 1f) - 1f;
            float want = level > 0f ? Mathf.Max(0f, lampBrightness.Value) * Mathf.Min(1f, level) * brightness : 0f;
            if (want > 0.001f) {
                if (b.lamp == null) {
                    var go = new GameObject("ArcheryGoldHoldLight");
                    b.lamp = go.AddComponent<Light>();
                    b.lamp.type = LightType.Point;
                    b.lamp.shadows = LightShadows.None;
                }
                b.lamp.transform.position = tip;
                b.lamp.color = Volley.ChargedColour;
                b.lamp.intensity = want;
                b.lamp.range = Mathf.Max(0f, lampRange.Value);
                if (!b.lamp.enabled) b.lamp.enabled = true;
            }
            else if (b.lamp != null && b.lamp.enabled) b.lamp.enabled = false;
            return look;
        }

        static readonly int T_Thump = Timing.Section("look start GoldHold.Thump");
        static void Thump(BowString bow, bool local, int stage, int orbs, Vector3 tip, Controller? hand, float brightness) {
            using var _t = Timing.Begin(T_Thump);
            string names = thumpSound.Value;
            if (string.IsNullOrWhiteSpace(names)) names = ChargeFx.ThumpSound; // the Charge Shot's own
            float volume = Mathf.Clamp01(thumpVolume.Value);
            VolleyFx.Play(names, tip, 0f, volume);
            if (flashBrightness.Value > 0f && flashSeconds.Value > 0f)
                Fx.Flash(tip, Volley.ChargedColour, flashBrightness.Value * brightness, Mathf.Max(0.1f, flashRange.Value), flashSeconds.Value);
            float pulse = local ? Mathf.Clamp01(ChargeFx.ThumpStrength(stage) * Mathf.Max(0f, haptic.Value)) : 0f;
            if (pulse > 0f && hand != null) hand.TriggerHaptic(pulse, ChargeFx.ThumpHapticSeconds);
            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Gold hold: stage {0} of {1} on '{2}' ({3}): thump '{4}'{5}",
                stage, orbs, bow.name, local ? "own" : "seen", names, pulse > 0f && hand != null ? string.Format(CultureInfo.InvariantCulture, ", pulse {0:0.##}", pulse) : ""));
        }

        /// <summary>The bow's hold is over (released, cancelled, consumed, its bow gone, or its archer has no stage to hold for).</summary>
        internal static void Forget(BowString bow) {
#if QUEST
            if (bows.Count == 0 || Il.Same(bow, null) || !bows.TryGetValue(bow, out Bow b)) return;
#else
            if (bows.Count == 0 || ReferenceEquals(bow, null) || !bows.TryGetValue(bow, out Bow b)) return;
#endif
            bows.Remove(bow);
            if (b.lamp != null) UnityEngine.Object.Destroy(b.lamp.gameObject);
        }

        static void Clear() {
            foreach (KeyValuePair<BowString, Bow> kv in bows) if (kv.Value.lamp != null) UnityEngine.Object.Destroy(kv.Value.lamp.gameObject);
            bows.Clear();
        }

        /// <summary>SelfCheck, clients: every level of every ring at every stage with the settings just loaded (numbers only, no
        /// objects), so a broken entry shows at start-up and not on the first held volley. Returns how many looks it evaluated.</summary>
        internal static int Check() {
            int n = 0;
            if (ApplicationManager.IsHeadless) return n; // a client's look: the server draws nothing
            for (int orbs = 1; orbs <= Complements.MaxOrbs; orbs++)
                for (int stage = 0; stage <= orbs; stage++) {
                    Look look = Snapshot(stage, 0.5f, orbs, 2, 0.1f, 0f);
                    for (int i = 0; i < VolleyMath.Total; i++) { Tone t = look.For(GoldStages.GhostRing(i), VolleyMath.Azimuth(i), i); if (float.IsNaN(t.Gold + t.Glow + t.Width)) throw new InvalidOperationException("a gold hold look is not a number"); n++; }
                    Tone f = Flight(GoldStages.Level(GoldStages.Outer, stage, orbs, 2));
                    Color c = FlightColour(GoldStages.Level(GoldStages.Inner, stage, orbs, 2));
                    if (float.IsNaN(f.Glow + c.r)) throw new InvalidOperationException("a gold hold flight look is not a number");
                }
            return n;
        }
    }
}
