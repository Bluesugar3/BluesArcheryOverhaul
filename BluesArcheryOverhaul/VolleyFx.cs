using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // The volley's effects outside the ghosts themselves, each its own small class so one can be replaced alone.
    //   Sparks  one shared particle emitter (the game's 'Sparks' particle material) for every spark the mod throws
    //   LandFx  where spectral arrows land: sparks each, at most LandSoundsMax sounds and one light per release
    //   Beats   the draw's beats on each drawn bow: slide-out, ratchet clicks, ripple, release flare (sounds for everyone,
    //           haptics only on the shooter's own bow hand)
    //   Pulses / Play  queued haptic pulses and delayed sounds, pumped from Main.OnUpdate
    // Client only: nothing here runs on a headless server.
    internal static class VolleyFx {
        static MelonPreferences_Entry<int> landSparks = null!, landSoundsMax = null!;
        static MelonPreferences_Entry<string> sparkMaterial = null!, landSound = null!, slideSound = null!, ratchetSound = null!, releaseSound = null!;
        static MelonPreferences_Entry<float> rippleLength = null!, rippleBoost = null!, hapticStrength = null!;

        /// <summary>Creates this file's settings in the [Volley] category (called from Volley.Init).</summary>
        internal static void Init(MelonPreferences_Category volley) {
            landSparks = volley.CreateEntry("LandSparks", 3, description: "Sparks from the shared emitter where each spectral arrow lands. 0 = none.");
            sparkMaterial = volley.CreateEntry("SparkMaterial", "Sparks", description: "Particle material for every spark, by name (loaded_assets.txt, 'Particle systems': name | material | shader).");
            landSound = volley.CreateEntry("LandSound", "Arrow Impact Sounds", description: "Sound where a spectral arrow lands (names separated by ';', the first one found is used).");
            landSoundsMax = volley.CreateEntry("LandSoundsMax", 3, description: "At most this many landing sounds per release.");
            slideSound = volley.CreateEntry("SlideSound", "Skill Orb Interact Sounds", description: "Sound at the arrow's tip when the five ghosts slide out (names separated by ';', the first one found is used; empty = none).");
            ratchetSound = volley.CreateEntry("RatchetSound", "Click", description: "Sound for each of the ten outer ghosts as it arrives (the Full Volley ratchet).");
            releaseSound = volley.CreateEntry("ReleaseSound", "Arrow Woosh Sounds", description: "One sound when a release carries ghosts and their shafts flare to full length.");
            rippleLength = volley.CreateEntry("RippleLength", 0.25f, description: "Seconds the full-volley ripple takes: every ghost head dips and returns, every shaft brightens and returns.");
            rippleBoost = volley.CreateEntry("RippleBoost", 1.6f, description: "Strength of the ripple: heads' glow progress dips to 1 / this, shafts brighten by this factor. 1 = no ripple.");
            hapticStrength = volley.CreateEntry("HapticStrength", 0.5f, description: "Strength (0 to 1) of the volley's pulses in your bow hand: 2 at the slide-out, one on every other ratchet ghost; Burst's rack pulses at 1.6 times this. 0 = none.");
            Cfg.Reloaded += Sparks.Forget;
        }

        internal static float HapticStrength => hapticStrength != null ? hapticStrength.Value : 0f;

        // ---------------------------------------------------------------- pulses and sounds, queued

        internal const float PulseSeconds = 0.03f, PulseGap = 0.08f; // Ruling 9: internal, not settings
        const int QueueCap = 64;

        struct Pulse { public Controller hand; public float at, strength; }
        struct LaterSound { public string names; public Vector3 at; public float when, volume; }
        static readonly List<Pulse> pulses = new List<Pulse>();
        static readonly List<LaterSound> later = new List<LaterSound>();
        static bool loggedQuietFailure;

        /// <summary>Queues <paramref name="count"/> pulses of PulseSeconds on <paramref name="hand"/>, <paramref name="gap"/>
        /// seconds apart, the first now. Played by <see cref="Pump"/>.</summary>
        internal static void Pulses(Controller? hand, int count, float strength, float gap) {
            if (hand == null || count <= 0 || strength <= 0f || ApplicationManager.IsHeadless) return;
            float now = Time.time, s = Mathf.Clamp01(strength);
            for (int k = 0; k < count && pulses.Count < QueueCap; k++) pulses.Add(new Pulse { hand = hand, at = now + k * gap, strength = s });
            Pump();
        }

        /// <summary>Plays the first loaded sound of <paramref name="names"/> at <paramref name="at"/>, now or after <paramref name="delay"/> seconds,
        /// at <paramref name="volume"/> (0 to 1; below 1 through the sound's own pooled emitter's output bus volume).</summary>
        static readonly int T_Play = Timing.Section("look VolleyFx.Play (sound)");
        internal static void Play(string names, Vector3 at, float delay = 0f, float volume = 1f) {
            using var _t = Timing.Begin(T_Play, names);
            if (ApplicationManager.IsHeadless || string.IsNullOrWhiteSpace(names) || volume <= 0f) return;
            if (delay > 0f) {
                if (later.Count < QueueCap) later.Add(new LaterSound { names = names, at = at, when = Time.time + delay, volume = volume });
                return;
            }
            SoundEffectSet? s = Fx.FindSound(names);
            if (s == null) return;
            if (volume >= 0.999f) { s.PlayAtPosition(at); return; }
            PlayQuiet(s, at, Mathf.Clamp01(volume));
        }

        // The game's sounds have no volume argument: the one-shot's pooled Wwise emitter gets an output bus volume towards
        // every default listener. Nothing resets it: when the sound ends the game releases the emitter, which unregisters it
        // from Wwise and so clears the volume; touching it after that would re-register a game-owned object.
        static void PlayQuiet(SoundEffectSet s, Vector3 at, float volume) {
            try {
#if QUEST
                AudioManager am = Il2CppAlta.Utilities.SingletonBehaviour<AudioManager>.Instance;
#else
                AudioManager am = Alta.Utilities.SingletonBehaviour<AudioManager>.Instance;
#endif
                if (am == null) { s.PlayAtPosition(at); return; }
#if QUEST
                // Quest: the game's "params" list is a plain argument there, and leaving it out passes null, where PC passes an empty one
                am.PlaySoundEffectAt(s, at, null!, out AudioControl control, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ISoundProperty>(0));
#else
                am.PlaySoundEffectAt(s, at, null!, out AudioControl control);
#endif
                AkGameObj ak = control != null ? control.AkGameObj : null!;
                if (ak == null) return;
                if (SetVolume(ak.gameObject, volume) == 0) QuietFailed("no audio listener");
            }
            catch (Exception e) { QuietFailed(e.Message); }
        }

        static int SetVolume(GameObject emitter, float volume) {
            int n = 0;
#if QUEST
            foreach (AkAudioListener l in AkAudioListener.DefaultListeners.ListenerList.Each()) {
#else
            foreach (AkAudioListener l in AkAudioListener.DefaultListeners.ListenerList) {
#endif
                if (l == null) continue;
                AkSoundEngine.SetGameObjectOutputBusVolume(emitter, l.gameObject, volume);
                n++;
            }
            return n;
        }

        static void QuietFailed(string why) {
            if (loggedQuietFailure) return;
            loggedQuietFailure = true;
            MelonLogger.Warning($"[Archery] Sound: a quieter sound could not be set below full volume ({why}); it plays at full volume");
        }

        /// <summary>Every frame (Main.OnUpdate): plays the pulses and sounds that are due.</summary>
        internal static void Pump() {
            if (pulses.Count == 0 && later.Count == 0) return;
            float now = Time.time;
            for (int i = pulses.Count - 1; i >= 0; i--) {
                Pulse p = pulses[i];
                if (p.at > now) continue;
                pulses.RemoveAt(i);
                if (p.hand != null) p.hand.TriggerHaptic(p.strength, PulseSeconds);
            }
            for (int i = later.Count - 1; i >= 0; i--) {
                LaterSound l = later[i];
                if (l.when > now) continue;
                later.RemoveAt(i);
                Play(l.names, l.at, 0f, l.volume);
            }
        }

        /// <summary>The draw's beats on each drawn bow (spec Volley, Full Volley entries). Sounds for every bow drawn here,
        /// pulses only on the shooter's own bow hand. Slide-out from the ghost count going 0 -> 5 (the ghosts wait for the
        /// server's Charged status, so sound and sight must not split); the ripple and the rack from VolleyTimeline.BeatsBetween.</summary>
        internal static class Beats {
            sealed class Bow {
                public int ghosts;
                public float t, rippleAt = -1f;
                public bool racked;
                public int clicks, ticks;
            }

            const int Cap = 64;
#if QUEST
            static readonly Dictionary<BowString, Bow> bows = new Dictionary<BowString, Bow>(Il.ByPointer<BowString>());
#else
            static readonly Dictionary<BowString, Bow> bows = new Dictionary<BowString, Bow>();
#endif
            static bool loggedSounds;

            /// <summary>One frame of a drawn bow. Returns the ripple's strength now (0 = none, 1 = its peak); <paramref name="rack"/>
            /// is true on the frame Burst racks.</summary>
            // heavy: the ghosts are a Charge main's heavy arrows (Volley.HeavyLook), whose slide-out plays [HeavyArrows]
            // SlideSound instead of the volley's (an empty one keeps the volley's).
            static readonly int T_Frame = Timing.Section("look VolleyFx.Frame (per bow per frame)");
            internal static float Frame(BowString bow, bool local, int ghosts, float t, int volleyTier, int chargeTier, bool racked,
                                        Vector3 tip, Controller? hand, VolleyTimes tm, out bool rack, bool heavy = false) {
                using var _t = Timing.Begin(T_Frame);
                LogSoundsOnce();
                if (!bows.TryGetValue(bow, out Bow b)) {
                    if (bows.Count >= Cap) bows.Clear();
                    bows[bow] = b = new Bow { t = t };
                }
                VolleyBeat beats = VolleyTimeline.BeatsBetween(b.t, t, volleyTier, chargeTier, tm); // the archer's own timeline (Paths.VolleyTimesFor)
                float strength = HapticStrength;
                if (ghosts > b.ghosts) {
                    if (b.ghosts == 0) {
                        // the slide-out (a late status that brings outer ghosts in the same frame folds them into it)
                        if (!(heavy && Volley.HeavyLook.PlaySlide(tip))) Play(slideSound.Value, tip);
                        if (local) Pulses(hand, 2, strength, PulseGap);
                        b.clicks = b.ticks = 0;
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley FX: slide-out of {ghosts} on '{bow.name}' ({(local ? "own, 2 pulses" : "seen")})");
                    }
                    else {
                        // the ratchet: one click per new outer ghost, a pulse on every other one
                        for (int i = Math.Max(b.ghosts, VolleyMath.Inner); i < ghosts; i++) {
                            Play(ratchetSound.Value, tip);
                            b.clicks++;
                            if (local && (i - VolleyMath.Inner) % 2 == 0) { Pulses(hand, 1, strength, PulseGap); b.ticks++; }
                        }
                        if (ghosts >= VolleyMath.Total)
                            { if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley FX: ratchet done on '{bow.name}' at t {Mathf.RoundToInt(t * 1000f)} ms: {b.clicks} click(s), {b.ticks} pulse(s)"); }
                    }
                }
                if ((beats & VolleyBeat.Ripple) != 0 && ghosts > 0) {
                    b.rippleAt = Time.time;
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley FX: ripple on '{bow.name}' at t {Mathf.RoundToInt(t * 1000f)} ms ({ghosts} heads, charge tier {chargeTier})");
                }
                // The rack: the first frame Burst shows on the bow (also when the Volley status arrived after RackAt), so the sound
                // never comes without them and never twice (Racked allows a tiny epsilon that BeatsBetween does not).
                rack = racked && !b.racked;
                b.ghosts = ghosts;
                b.t = t;
                b.racked = racked;
                if (ghosts == 0) b.rippleAt = -1f;
                return Ripple(b);
            }

            static float Ripple(Bow b) {
                if (b.rippleAt < 0f) return 0f;
                float len = rippleLength.Value, age = Time.time - b.rippleAt;
                if (len <= 0f || age >= len) { b.rippleAt = -1f; return 0f; }
                return Mathf.Sin(Mathf.PI * Mathf.Clamp01(age / len));
            }

            static float Boost => rippleBoost.Value > 0f ? rippleBoost.Value : 1f;
            /// <summary>The heads' _Progress at ripple strength f: 1, dipping to 1 / RippleBoost at the peak.</summary>
            internal static float HeadProgress(float f) => 1f - (1f - 1f / Boost) * f;
            /// <summary>The shafts' brightness factor at ripple strength f: 1, rising to RippleBoost at the peak.</summary>
            internal static float ShaftBoost(float f) => 1f + (Boost - 1f) * f;

            /// <summary>A release carried ghosts: their shafts flare to full length (Ghost.Fly) with one ReleaseSound.</summary>
            static readonly int T_Release = Timing.Section("look start VolleyFx.Release (flare)");
            internal static void Release(uint arrow, Vector3 at, int ghosts) {
                using var _t = Timing.Begin(T_Release, arrow);
                Play(releaseSound.Value, at);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Volley FX: release flare on arrow {arrow} ({ghosts} ghost(s))");
            }

            internal static void Forget(BowString bow) => bows.Remove(bow);

            static void LogSoundsOnce() {
                if (loggedSounds) return;
                loggedSounds = true;
                var missing = new List<string>();
                string Name(string key, string names) {
                    SoundEffectSet? s = Fx.FindSound(names);
                    if (s == null) missing.Add($"{key} '{names}'");
                    return s != null ? s.name : names;
                }
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Volley FX: sounds slide '{Name("slide", slideSound.Value)}', ratchet '{Name("ratchet", ratchetSound.Value)}', " +
                                $"release '{Name("release", releaseSound.Value)}', land '{Name("land", landSound.Value)}', rack '{Name("rack", Burst.RackSoundName)}', " +
                                $"click '{Name("click", Burst.RackClickName)}', whump '{Name("whump", Burst.WhumpSoundName)}' (missing: {(missing.Count > 0 ? string.Join(", ", missing) : "none")})");
            }
        }

        /// <summary>One particle system for every spark: emitted on demand, simulated in world space, never looping on its own.</summary>
        internal static class Sparks {
            const float Life = 0.35f, Size = 0.02f, GravityModifier = 0.6f, StretchVelocity = 0.04f, RetrySeconds = 5f;
            const int MaxParticles = 2000;
            static ParticleSystem? system;
            static string materialFor = "";   // the SparkMaterial value the current material was resolved for
            static float retryAt;
            static string lastLog = "";

            /// <summary>Settings reloaded: resolve the material again on the next emit.</summary>
            internal static void Forget() { materialFor = ""; retryAt = 0f; }

            /// <summary>Skyfall's idle warm-up (final review I-2): the emitter and its material search ahead of time, so a player's first
            /// landing spark never searches in its frame. Nothing is emitted.</summary>
            internal static void Warm() { System(); }

            static readonly int T_System = Timing.Section("look VolleyFx.System (spark emitter)");
            static ParticleSystem? System() {
                using var _t = Timing.Begin(T_System);
                if (ApplicationManager.IsHeadless) return null;
                if (system == null) {
                    var go = new GameObject("ArcheryVolleySparks");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    ParticleSystem ps = go.AddComponent<ParticleSystem>();
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystem.MainModule main = ps.main;
                    main.loop = true;
                    main.playOnAwake = false;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
#if QUEST
                    main.startLifetimeMultiplier = Life;
#else
                    main.startLifetime = Life;
#endif
                    main.startSize = Size;
                    main.startSpeed = 0f;
#if QUEST
                    main.gravityModifierMultiplier = GravityModifier;
#else
                    main.gravityModifier = GravityModifier;
#endif
                    main.maxParticles = MaxParticles;
                    ParticleSystem.EmissionModule emission = ps.emission;
                    emission.enabled = false;
                    ParticleSystem.ShapeModule shape = ps.shape;
                    shape.enabled = false;
                    ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
#if QUEST
                    // ponytail: the Quest build's ColorOverLifetime has no setter for enabled, so sparks there end without fading
#else
                    fade.enabled = true;
#endif
                    var gradient = new Gradient();
                    gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    fade.color = new ParticleSystem.MinMaxGradient(gradient);
                    ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = StretchVelocity;
                    r.lengthScale = 1f;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    ps.Play();
                    system = ps;
                }
                string want = sparkMaterial.Value.Trim();
                if (materialFor != want && Time.unscaledTime >= retryAt) ApplyMaterial(system, want);
                return system;
            }

            // Found the way Fx.LogMaterialLookups finds 'WeaponTrail': the material of a loaded particle renderer, by name.
            static readonly int T_ApplyMaterial = Timing.Section("asset VolleyFx.ApplyMaterial (FindObjectsOfTypeAll)");
            static void ApplyMaterial(ParticleSystem ps, string want) {
                using var _t = Timing.Begin(T_ApplyMaterial, want);
                Material? m = Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>()
                    .Where(r => r != null && r.sharedMaterial != null && r.sharedMaterial.name == want)
                    .Select(r => r.sharedMaterial).FirstOrDefault();
                string line;
                bool missing = m == null; // G5 (G2 review M-5): a missing material is a warning at every level
                if (m != null) {
                    materialFor = want;
                    line = $"[Archery] Volley FX: sparks use the game's particle material '{m.name}' ({(m.shader != null ? m.shader.name : "-")})";
                }
                else {
                    // not loaded yet, or a wrong name: flat sparks for now, look again in a few seconds
                    m = Fx.LineMaterial();
                    retryAt = Time.unscaledTime + RetrySeconds;
                    line = $"[Archery] Volley FX: particle material '{want}' NOT FOUND; sparks use the flat line material until it is (retried every {RetrySeconds:0} s)";
                }
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
                if (line != lastLog) { lastLog = line; if (missing) MelonLogger.Warning(line); else if (Logs.Summary) MelonLogger.Msg(line); }
            }

            /// <summary>Throws <paramref name="count"/> sparks from <paramref name="at"/> into a cone of <paramref name="spread"/>
            /// degrees round <paramref name="dir"/>, at up to <paramref name="speed"/> m/s, at <paramref name="sizeScale"/> of the usual size.</summary>
            static readonly int T_Emit = Timing.Section("look VolleyFx.Emit (sparks)");
            internal static void Emit(Vector3 at, Vector3 dir, int count, Color c, float spread, float speed, float sizeScale = 1f) {
                using var _t = Timing.Begin(T_Emit);
                if (count <= 0) return;
                ParticleSystem? ps = System();
                if (ps == null) return;
                Vector3 axis = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
                float cone = Mathf.Tan(Mathf.Clamp(spread, 0f, 85f) * Mathf.Deg2Rad);
#if QUEST
                float size = Size * Mathf.Max(0f, sizeScale);   // the Quest build's EmitParams has no setters: the older Emit takes the same values
                for (int i = 0; i < count; i++) {
                    Vector3 d = (axis + UnityEngine.Random.insideUnitSphere * cone).normalized;
                    ps.Emit(at, d * speed * UnityEngine.Random.Range(0.5f, 1f), size, Life, c);
                }
#else
                var ep = new ParticleSystem.EmitParams { position = at, startColor = c, applyShapeToPosition = false };
                if (sizeScale != 1f) ep.startSize = Size * Mathf.Max(0f, sizeScale);
                for (int i = 0; i < count; i++) {
                    Vector3 d = (axis + UnityEngine.Random.insideUnitSphere * cone).normalized;
                    ep.velocity = d * speed * UnityEngine.Random.Range(0.5f, 1f);
                    ps.Emit(ep, 1);
                }
#endif
            }
        }

        /// <summary>Landing of spectral arrows (spec Volley entry; Part 9 items 5 and 6): sparks from the shared emitter on
        /// every landing, a sound for the first LandSoundsMax of a release, and one light per release at its densest landing,
        /// 0.15 s after the first.</summary>
        internal static class LandFx {
            const float BufferSeconds = 0.15f, DensestRadius = 1f;          // Ruling 9: internal, not settings
            const float FlashRange = 1.5f, FlashLife = 0.12f;               // as the per-ghost flash this replaces
            const float SparkSpread = 60f, SparkSpeed = 2.5f;
            const float ForgetSeconds = 10f;                                 // longer than any spectral arrow lives
            const int Cap = 64;

            sealed class Landing {
                public float first;
                public int sounds;
                public bool flashed, charged, green;
                public readonly List<float> xs = new List<float>(), ys = new List<float>(), zs = new List<float>();
            }

            static readonly Dictionary<uint, Landing> releases = new Dictionary<uint, Landing>();
            static readonly List<uint> drop = new List<uint>();
            static readonly HashSet<string> missingSounds = new HashSet<string>();

            /// <summary><paramref name="needle"/>: a needle with the needle look landed: its sparks take the needle colour at
            /// [Needles] LandSparkScale of the size.</summary>
            // sparks false: no sparks (a heavy arrow's landing shows the charged hit effect instead); the sound and the
            // release's light stay.
            static readonly int T_Land = Timing.Section("look start VolleyFx.Land");
            // green (H2): an arrow of a locked (homing) release landed: its sparks, and the release's one light, are the homing green.
            internal static void Land(uint release, Vector3 at, bool charged, bool needle = false, bool sparks = true, bool green = false) {
                using var _t = Timing.Begin(T_Land, release);
                if (ApplicationManager.IsHeadless) return;
                if (!releases.TryGetValue(release, out Landing l)) {
                    if (releases.Count >= Cap) releases.Clear();
                    releases[release] = l = new Landing { first = Time.time };
                }
                l.charged |= charged;
                l.green |= green;
                if (!sparks) { /* the heavy arrow's hit effect stands in */ }
                else if (green) Sparks.Emit(at, Vector3.up, landSparks.Value, SeekLook.Colour, SparkSpread, SparkSpeed);
                else if (needle) Sparks.Emit(at, Vector3.up, landSparks.Value, Volley.NeedleLook.Colour, SparkSpread, SparkSpeed, Volley.NeedleLook.LandSparkScale);
                else Sparks.Emit(at, Vector3.up, landSparks.Value, charged ? Volley.ChargedColour : Volley.Colour, SparkSpread, SparkSpeed);
                if (l.sounds < landSoundsMax.Value) {
                    l.sounds++;
                    SoundEffectSet? s = Fx.FindSound(landSound.Value);
                    if (s != null) s.PlayAtPosition(at);
                    else if (missingSounds.Add(landSound.Value)) MelonLogger.Warning($"[Archery] Volley FX: land sound '{landSound.Value}' not found");
                }
                if (!l.flashed) { l.xs.Add(at.x); l.ys.Add(at.y); l.zs.Add(at.z); }
            }

            /// <summary>Every frame (VolleySim.LateUpdate): one light for each release whose buffer has closed.</summary>
            internal static void Pump() {
                if (releases.Count == 0) return;
                float now = Time.time;
                drop.Clear();
                foreach (KeyValuePair<uint, Landing> kv in releases) {
                    Landing l = kv.Value;
                    if (!l.flashed && now - l.first >= BufferSeconds) {
                        l.flashed = true;
                        int best = SpectralMath.Densest(l.xs, l.ys, l.zs, DensestRadius);
                        if (best >= 0) {
                            var at = new Vector3(l.xs[best], l.ys[best], l.zs[best]);
                            float b = Volley.HitFlash;
                            if (b > 0f) Fx.Flash(at, l.green ? SeekLook.Colour : l.charged ? Volley.ChargedColour : Volley.Colour, b, FlashRange, FlashLife);
                            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture,
                                "[Archery] Volley FX: arrow {0} landing light at the densest of {1} landing(s) ({2}), brightness {3:0.##}{4}; {5} sound(s)",
                                kv.Key, l.xs.Count, at.ToString("F2"), b, b > 0f ? "" : " (off)", l.sounds));
                        }
                    }
                    else if (l.flashed && now - l.first > ForgetSeconds) drop.Add(kv.Key);
                }
                foreach (uint id in drop) releases.Remove(id);
            }
        }
    }
}
