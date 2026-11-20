using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Inventory;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Skyfall's hold looks (Task 10): clients only, the local caster only (a game told it holds Skyfall: Skyfall.LocalHolds). While the
    // archer holds a full draw at the sky, what only they see (nothing is sent; other players see the show from the release):
    //   - three glyph rings (Hold sigils: gold, cyan, gold) flare round the arrow and climb its line as the hold arms;
    //   - a faint sky sigil where the aim meets the sky (SkyfallRules.SigilCentre, the cast's own rule), following the aim smoothly;
    //   - the target glyph on the ground under it (the Ground sigil's core and rim; the ground from Skyfall.GroundAt, the server's way);
    //   - the sky dims a little (SkyfallSky, HoldKey), and 'Shrine Activate Sounds' swells as the hold at the sky starts;
    //   - when the hold is armed (SkyfallRules.Armed on the draw clock's time since the 68% crossing, the clock Task 8's arm reads, so
    //     the thump comes exactly when a release would ask to cast): the chime and two pulses in the bow hand.
    // Fed every held frame by the draw clock (Draws.Clock -> Frame, which only records); drawn in Main.OnLateUpdate (Tick) from the
    // arrow's pose in this frame. While the caster's own storm still runs a sky shot is a normal shot, so no hold look shows.
    internal static class SkyfallHold {
        static MelonPreferences_Entry<float>? ringRadius, ringSpan, sigilAlpha, groundAlpha, holdDim, followSeconds, pulseStrength;
        static MelonPreferences_Entry<string>? holdSound, armSound;
        static MelonPreferences_Entry<int>? pulses;

        const float EndAfterSeconds = 0.25f, PresenceSeconds = 0.25f, FlareSeconds = 0.25f, SoundGapSeconds = 3f, GroundEvery = 0.1f;
        const int HoldCap = 8;

        internal static void Init(MelonPreferences_Category cat) {
            const string P = "Clients (looks only), the archer's own hold: ";
            ringRadius = cat.CreateEntry("HoldRingRadius", 0.5f, description: P + "metres: the largest of the three glyph rings round the arrow, at full hold.");
            ringSpan = cat.CreateEntry("HoldRingSpan", 3.6f, description: P + "metres ahead of the arrow tip that the farthest ring climbs to.");
            sigilAlpha = cat.CreateEntry("HoldSigilAlpha", 0.3f, description: P + "how strongly the faint sigil shows in the sky while holding.");
            groundAlpha = cat.CreateEntry("HoldGroundAlpha", 0.35f, description: P + "how strongly the target glyph shows on the ground under it.");
            holdDim = cat.CreateEntry("HoldDim", 0.5f, description: P + "the share of the storm's sky dim while holding.");
            followSeconds = cat.CreateEntry("HoldFollowSeconds", 0.15f, description: P + "how quickly the faint sigil follows the aim (smoothing, seconds).");
            holdSound = cat.CreateEntry("HoldSound", "Shrine Activate Sounds", description: P + "played once when a hold at the sky starts.");
            armSound = cat.CreateEntry("ArmSound", "Skill Orb Acquire Sounds", description: P + "the chime when the hold is armed.");
            pulses = cat.CreateEntry("ArmPulses", 2, description: P + "haptic pulses in the bow hand when armed.");
            pulseStrength = cat.CreateEntry("ArmPulseStrength", 0.85f, description: P + "their strength (0 to 1).");
            Cfg.Reloaded += () => holdFailed = false;
        }

        // a broken (NaN or infinite) setting reads as its default, so no pose is ever NaN
        static float F(MelonPreferences_Entry<float>? e, float fallback) {
            if (e == null) return fallback;
            float x = e.Value;
            return float.IsNaN(x) || float.IsInfinity(x) ? fallback : x;
        }
        static float RingRadius => F(ringRadius, 0.5f);
        static float RingSpan => F(ringSpan, 3.6f);

        // ---------------------------------------------------------------- one hold per bow string

        sealed class Hold {
            public ShotPose Aim;
            public float Held, LastFrame, AimedSince = -1f, EndedAt = -1f, Presence;
            public bool Sounded, Armed, WasAimed, Tried, GroundTried, HasCentre;
            public float SoundedAt = -1f, ArmedAt = -1f;
            public readonly SkyfallSigil.Sigil?[] Rings = new SkyfallSigil.Sigil?[3];
            public SkyfallSigil.Sigil? Sky, Ground;
            public uint Seed;
            public Vector3 Centre, Tip, Fwd = Vector3.up, Pos;
            public float GroundY = float.NaN, NextGround;

            public void ReleaseSigils() {
                for (int k = 0; k < Rings.Length; k++) { Rings[k]?.Release(); Rings[k] = null; }
                Sky?.Release(); Sky = null;
                Ground?.Release(); Ground = null;
                Tried = GroundTried = false;
            }
        }

        static readonly Dictionary<BowString, Hold> holds = new Dictionary<BowString, Hold>();
        static readonly List<BowString> scratch = new List<BowString>();
        static bool holdFailed;
        static float lastSoundAt = -1000f;

        static readonly int T_Frame = Timing.Section("look SkyfallHold.Frame");
        /// <summary>The draw clock (Draws.Clock), every frame the local holder holds a string: records the aim and the time since the
        /// 68% crossing; drawing waits for Tick, so the looks follow the arrow's pose in this frame's LateUpdate.</summary>
        internal static void Frame(BowString bow, ShotPose aim, float heldSeconds) {
            using var _t = Timing.Begin(T_Frame);
            if (ApplicationManager.IsHeadless || !Skyfall.LocalHolds || bow == null || holdFailed) return;
            if (!holds.TryGetValue(bow, out Hold h)) {
                if (holds.Count >= HoldCap) return;
                holds[bow] = h = new Hold();
            }
            h.Aim = aim;
            h.Held = heldSeconds;
            h.LastFrame = Time.time;
        }

        /// <summary>The hold on this string ends (released, the arrow left, or no Frame for 0.25 s): its looks fade out and its sky dim
        /// is released at once.</summary>
        internal static void End(BowString bow) {
            if (!ReferenceEquals(bow, null) && holds.TryGetValue(bow, out Hold h)) End(h);
            else SkyfallSky.Undim(SkyfallSky.HoldKey);
        }

        static void End(Hold h) {
            if (h.EndedAt < 0f) h.EndedAt = Time.time;
            SkyfallSky.Undim(SkyfallSky.HoldKey);
        }

        /// <summary>The docked arrow's pose and the pull (the strength the shot will carry), for the draw clock; the default pose (not
        /// aimed) with no docked arrow.</summary>
        internal static ShotPose AimOf(BowString bow, float pull) {
            Arrow? a = bow != null ? ArrowObj(bow) : null;
            if (a == null) return default;
            Transform t = a.transform;
            return new ShotPose { Pos = Seek.H(t.position), Dir = Seek.H(t.forward), Strength = pull };
        }

        // the arrow on the string (ChargeFx.ArrowObj's pattern)
        static Arrow? ArrowObj(BowString bow) {
            PickupDock dock = bow.StringDock;
            if (dock == null || !dock.HasDockedPickup || dock.DockedPickup == null) return null;
            Arrow a = dock.DockedPickup.GetComponent<Arrow>();
            return a != null ? a : null;
        }

        static Vector3 V(HVec h) => new Vector3((float)h.X, (float)h.Y, (float)h.Z);
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        static bool NoPlayer() {
            var me = global::Player.Current;
            return me == null || (me is UnityEngine.Object o && o == null);
        }

        /// <summary>Main.OnLateUpdate (clients): every hold drawn from its arrow's pose this frame. One count test while none. A throw
        /// stops the hold looks (said once) until the settings are reloaded.</summary>
        internal static void Tick() {
            if (holds.Count == 0) return;
            if (ApplicationManager.IsHeadless) return;
            if (NoPlayer()) { Clear(); return; }
            try {
                float now = Time.time, dt = Time.deltaTime;
                SkyfallValues v = Skyfall.Values;
                scratch.Clear();
                foreach (var kv in holds) scratch.Add(kv.Key);
                foreach (BowString bow in scratch) {
                    if (!holds.TryGetValue(bow, out Hold h)) continue;
                    if (Step(bow, h, now, dt, v)) { h.ReleaseSigils(); holds.Remove(bow); }
                }
            }
            catch (Exception e) {
                holdFailed = true;
                try { Clear(); } catch { }
                MelonLogger.Warning($"[Archery] Skyfall: the hold looks could not be shown ({e.Message}); the hold is drawn without them until the settings are reloaded");
            }
        }

        // one hold's frame; true when it is over (its looks have faded after its end)
        static bool Step(BowString bow, Hold h, float now, float dt, SkyfallValues v) {
            if (h.EndedAt < 0f && now - h.LastFrame > EndAfterSeconds) End(h);   // no Frame for 0.25 s
            Arrow? arrow = bow != null ? ArrowObj(bow) : null;
            if (arrow == null && h.EndedAt < 0f) End(h);                          // the arrow left the string (the release)
            if (arrow != null) {
                Transform at = arrow.transform;
                h.Fwd = at.forward;
                h.Pos = at.position;
                h.Tip = arrow.ArrowTip != null ? arrow.ArrowTip.transform.position : h.Pos + h.Fwd * 0.6f;
            }
            Vector3 tip = h.Tip, fwd = h.Fwd;

            // judged as it is now (the cast itself is judged on the wire pose at the release, strictly); never while this caster's storm
            // runs. Final review I-1, no strobe at the eyes: once aimed, the looks stay aimed 2 degrees and 0.03 of pull under the
            // trigger (SkyfallHoldLook.Aimed), and the flare, the hum and the dim's ramp start over only when the aim comes back after
            // the looks have gone (SkyfallHoldLook.Restarts); a shorter break only dims them through Presence.
            bool aimed = arrow != null && h.EndedAt < 0f && SkyfallHoldLook.Aimed(h.Aim, h.WasAimed, v) && !Skyfall.CasterBusy(Skyfall.OwnAccount, now);
            if (SkyfallHoldLook.Restarts(aimed, h.WasAimed, h.Tried && h.Presence > 0f)) {   // the first aimed tick of a stretch
                h.AimedSince = now;
                h.Sounded = false;
            }
            h.WasAimed = aimed;
            h.Presence = SkyfallCurves.Toward(h.Presence, aimed ? 1f : 0f, dt, PresenceSeconds);
            if (h.Presence <= 0f) {
                if (h.EndedAt >= 0f) return true;
                if (h.Tried) { h.ReleaseSigils(); SkyfallSky.Undim(SkyfallSky.HoldKey); }   // hidden while the aim is off the sky
                return false;
            }
            float p = v.ArmSeconds > 0f ? Mathf.Clamp01(h.Held / v.ArmSeconds) : 0f;
            if (float.IsNaN(p)) p = 0f;

            if (aimed && !h.Sounded) {                                            // the hum, once a stretch and at most once every 3 s
                h.Sounded = true;
                if (now - lastSoundAt >= SoundGapSeconds) {
                    lastSoundAt = h.SoundedAt = now;
                    VolleyFx.Play(holdSound?.Value ?? "", tip);
                }
            }

            HVec target = SkyfallRules.SigilCentre(Seek.H(h.Pos), Seek.H(fwd), v);
            if (aimed && SkyfallRules.Armed(h.Aim, h.Held, v)) {
                if (!h.Armed) {                                                   // the arm: chime and thump
                    h.Armed = true;
                    h.ArmedAt = now;
                    VolleyFx.Play(armSound?.Value ?? "", tip);
                    if (bow != null) VolleyFx.Pulses(Volley.BowHand(bow), pulses != null ? pulses.Value : 2, F(pulseStrength, 0.85f), 0.08f);
                    if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: hold armed after {h.Held:0.00} s, sigil at {target}"));
                }
            }
            else if (h.Armed && !(h.Held >= v.ArmSeconds - SkyfallRules.HeldSlackSeconds)) h.Armed = false;

            if (aimed && !h.Tried && bow != null) {                               // the looks, built once per aimed stretch
                h.Tried = true;
                h.Seed = (uint)bow.GetInstanceID();
                for (int k = 0; k < 3; k++) h.Rings[k] = SkyfallSigil.Build(SkyfallSigil.Style.Hold, h.Seed + (uint)k, accent: k == 1);
                h.Sky = SkyfallSigil.Build(SkyfallSigil.Style.Sky, h.Seed);
            }

            // the rings: flare open in 0.25 s from the aim's start, climb the arrow's line as the hold arms
            Quaternion facing = Quaternion.FromToRotation(Vector3.up, fwd);
            float alpha = h.Presence * (0.7f + 0.3f * p);
            if (h.EndedAt < 0f && h.ArmedAt >= 0f && now - h.ArmedAt < FlareSeconds) alpha = 1f;   // the flash
            float span = RingSpan, radius = RingRadius;
            for (int k = 0; k < 3; k++) {
                SkyfallSigil.Sigil? ring = h.Rings[k];
                if (ring == null) continue;
                ring.Place(tip + fwd * SkyfallCurves.HoldRingDistance(k, p, span), facing, radius * SkyfallCurves.HoldRingScale(k, p));
                ring.Show(now - h.AimedSince, FlareSeconds, alpha);
            }

            // the faint sigil where the aim meets the sky, smoothed
            Vector3 want = V(target);
            if (Finite(want)) {
                if (!h.HasCentre) { h.Centre = want; h.HasCentre = true; }
                else {
                    float follow = F(followSeconds, 0.15f);
                    h.Centre += (want - h.Centre) * (follow > 0f ? 1f - Mathf.Exp(-dt / follow) : 1f);
                }
            }
            Vector3 c = h.Centre;
            float ease = SkyfallCurves.Ease(p);
            if (h.Sky != null && h.HasCentre) {
                h.Sky.Place(c, Quaternion.identity, v.SigilRadius);
                h.Sky.Show(p * v.BuildSeconds, v.BuildSeconds, F(sigilAlpha, 0.3f) * h.Presence * ease);
            }

            // the target glyph on the ground under it (the core and the rim of the ground twin), the ground found at most 10 times a second
            if (h.HasCentre && now >= h.NextGround) {
                h.NextGround = now + GroundEvery;
                h.GroundY = Skyfall.GroundAt(c.x, c.z, c.y + 0.5, tip.y - Skyfall.GroundBelowMetres, out float y) ? y : float.NaN;
            }
            if (!float.IsNaN(h.GroundY)) {
                if (h.Ground == null && !h.GroundTried && h.EndedAt < 0f) { h.GroundTried = true; h.Ground = SkyfallSigil.Build(SkyfallSigil.Style.Ground, h.Seed); }
                if (h.Ground != null) {
                    h.Ground.Place(new Vector3(c.x, h.GroundY + 0.15f, c.z), Quaternion.identity, SkyfallStrike.KillArea(v));   // the twin's size
                    h.Ground.Show(p * v.BuildSeconds, v.BuildSeconds, F(groundAlpha, 0.35f) * h.Presence * ease, 0b1001);
                }
            }
            else if (h.Ground != null) { h.Ground.Release(); h.Ground = null; h.GroundTried = false; }   // no ground: hidden

            // the sky dims a little; until stays more than SkyFadeSeconds ahead so SkyDial's ramp-out never caps it (End releases it)
            if (h.EndedAt < 0f && h.AimedSince >= 0f && h.HasCentre)
                SkyfallSky.Dim(SkyfallSky.HoldKey, c, h.AimedSince, now + v.SkyFadeSeconds + 0.3f, F(holdDim, 0.5f) * h.Presence * ease);
            return false;
        }

        /// <summary>Every hold's looks released and the table emptied (no player, or the looks failed).</summary>
        internal static void Clear() {
            foreach (var kv in holds) kv.Value.ReleaseSigils();
            holds.Clear();
            SkyfallSky.Undim(SkyfallSky.HoldKey);
        }

        /// <summary>SelfCheck (0 when headless): the three rings' climb and size at p = 0, 0.5 and 1 with the settings just loaded.
        /// Throws on NaN; returns 9.</summary>
        internal static int Check() {
            if (ApplicationManager.IsHeadless) return 0;
            float span = RingSpan, radius = RingRadius;
            int n = 0;
            for (int k = 0; k < 3; k++)
                for (int i = 0; i <= 2; i++) {
                    float p = i * 0.5f;
                    if (float.IsNaN(SkyfallCurves.HoldRingDistance(k, p, span)) || float.IsNaN(radius * SkyfallCurves.HoldRingScale(k, p)))
                        throw new InvalidOperationException("a Skyfall hold number is not a number");
                    n++;
                }
            return n;
        }
    }
}
