using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Burst (Volley 3): from the rack on the 15 pellets show on the bow as their own small arrows (Held, fix batch C after
    // play-test 7: "make it show all arrows"; 30 arrows with the 15 ghosts), each on its ghosts' ring between two ghosts
    // (BurstPattern, Spectral.cs: two evenly spaced rings of 10 and 20; fix E); on release each becomes its pellet's streak,
    // from that place. The pellets fly in Volley's simulation like every other spectral arrow; this file holds Burst's
    // settings and its look: the rack, the held pellet arrows, the shed and the pellets' streaks. (The barbs on each inner
    // ghost, the look before 0.15.0 and then its fallback, were removed in 0.16.0.)
    // Colour (fix E, the user: "extra arrows still blue"): everything of the fifteen takes the colour of that archer's ghosts
    // at that moment (Volley.GhostColour with the ghosts' own gold switch: held arrows, streaks, shed sparks, landing sparks
    // and puffs). There is no pellet colour of its own.
    internal static class Burst {
        static MelonPreferences_Entry<float> pelletShare = null!, innerDegrees = null!, outerDegrees = null!, range = null!, streakLength = null!,
            rackClickDelay = null!;
        static MelonPreferences_Entry<int> puffSpecks = null!, shedSparks = null!;
        static MelonPreferences_Entry<string> rackSound = null!, rackClick = null!, whumpSound = null!;

        /// <summary>Called from Volley.Init, before Cfg.Ready.</summary>
        internal static void Init() {
            var cat = Cfg.Category("Burst");
            pelletShare = Cfg.Shared(cat.CreateEntry("PelletShare", BurstPattern.PelletShare, description: "Damage of each of Burst's 15 pellets, as a share of the arrow (times the charge multiplier on a charged shot)."));
            innerDegrees = Cfg.Shared(cat.CreateEntry("InnerDegrees", BurstPattern.InnerDegrees, description: "Angle in degrees between the arrow's line and each of the 5 inner pellets."));
            outerDegrees = Cfg.Shared(cat.CreateEntry("OuterDegrees", BurstPattern.OuterDegrees, description: "Angle in degrees between the arrow's line and each of the 10 outer pellets."));
            range = Cfg.Shared(cat.CreateEntry("Range", BurstPattern.Range, description: "Metres from the release point at which a pellet vanishes (in a puff of specks)."));
            streakLength = cat.CreateEntry("StreakLength", 0.15f, description: "Length in metres of the line drawn behind each pellet.");
            puffSpecks = cat.CreateEntry("PuffSpecks", 4, description: "Specks from the shared spark emitter where a pellet vanishes at its range. 0 = none.");
            shedSparks = cat.CreateEntry("ShedSparks", 12, description: "Spark streaks each inner ghost sheds forward when Burst is released. 0 = none.");
            rackSound = cat.CreateEntry("RackSound", "Dock Hovered Over Sounds", description: "Sound at the arrow's tip when Burst racks (names separated by ';', the first one found is used; empty = none).");
            rackClick = cat.CreateEntry("RackClick", "Click", description: "Sound RackClickDelay seconds after RackSound.");
            rackClickDelay = cat.CreateEntry("RackClickDelay", 0.08f, description: "Seconds between RackSound and RackClick.");
            whumpSound = cat.CreateEntry("WhumpSound", "Bow Shoot Sounds", description: "Sound at the bow when Burst's pellets are released (the muzzle whump).");
            Held.Init(cat);
        }

        internal static float PelletShare => pelletShare != null ? pelletShare.Value : BurstPattern.PelletShare;
        internal static float InnerDegrees => innerDegrees != null ? innerDegrees.Value : BurstPattern.InnerDegrees;
        internal static float OuterDegrees => outerDegrees != null ? outerDegrees.Value : BurstPattern.OuterDegrees;
        internal static float Range => range != null ? range.Value : BurstPattern.Range;
        internal static float StreakLength => streakLength != null ? streakLength.Value : 0.15f;
        internal static int PuffSpecks => puffSpecks != null ? puffSpecks.Value : 0;
        internal static string RackSoundName => rackSound != null ? rackSound.Value : "";
        internal static string RackClickName => rackClick != null ? rackClick.Value : "";
        internal static string WhumpSoundName => whumpSound != null ? whumpSound.Value : "";

        // ---------------------------------------------------------------- the rack (clients; spectators hear it, only the shooter feels it)

        const float FirmPulse = 1.6f; // the rack's double pulse, relative to HapticStrength (from the plan)

        /// <summary>Burst racks on this bow: RackSound, RackClick RackClickDelay later, 2 firm pulses in the shooter's bow hand.</summary>
        static readonly int T_Rack = Timing.Section("look Burst.Rack");
        internal static void Rack(BowString bow, Vector3 tip, Controller? hand) {
            using var _t = Timing.Begin(T_Rack);
            VolleyFx.Play(rackSound.Value, tip);
            VolleyFx.Play(rackClick.Value, tip, Mathf.Max(0.001f, rackClickDelay.Value));
            VolleyFx.Pulses(hand, 2, VolleyFx.HapticStrength * FirmPulse, VolleyFx.PulseGap);
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Burst: racked on '{bow.name}'");
        }

        // ---------------------------------------------------------------- the fifteen pellet arrows on the bow (fix batch C)

        // Play-test 7 (the user: "the 30 arrows dont show up when chargeing the bow", "make it show all arrows"): from the rack on,
        // each of the 15 pellets is its own small arrow on the bow, so 30 show with the 15 ghosts. Each is one of the volley's
        // own ghost arrows (Volley.Ghost: a copy of the nocked arrowhead in the game's skill material, a shaft line) at
        // HeldScale / HeldWidth / HeldLength, in the colour of that archer's ghosts (gold when they are gold: fix E), so it
        // reads as a second, smaller kind by size alone. It sits where its pellet leaves from (BurstPattern.TipOffset, tested):
        // on its ghosts' ring, midway between two ghosts, so the archer sees two evenly spaced rings round the real arrow (10
        // inside, 20 outside), and it points along its pellet's own line (Volley's HeldLayout.PelletTip). They slide out of the
        // tip in a ripple outward over HeldRippleSeconds at the rack, with the rack's sound and click (Rack). On release each
        // pellet's streak starts where its held arrow was and joins its real path over JoinSeconds (PelletStreak.StartFrom:
        // the held frame hangs on the bow, the release's on the arrow). Every client that draws the bow draws them (other
        // archers' dimmer). If they cannot be built: one log line, and nothing shows on the bow (the volley itself is unchanged).
        internal static class Held {
            const int SetCap = 64;
            static MelonPreferences_Entry<bool> on = null!;
            static MelonPreferences_Entry<float> scale = null!, width = null!, length = null!, brightness = null!, ripple = null!, arrive = null!, join = null!;
#if QUEST
            static readonly Dictionary<BowString, Set> sets = new Dictionary<BowString, Set>(Il.ByPointer<BowString>());
#else
            static readonly Dictionary<BowString, Set> sets = new Dictionary<BowString, Set>();
#endif
            static bool failed;
            // what the last dropped set was showing, for the release of the same frame (Left)
            static BowString? leftBow;
            static int leftFrame = -1;
            static readonly Vector3[] leftTips = new Vector3[BurstPattern.Count];
            static readonly bool[] leftShown = new bool[BurstPattern.Count];

            internal sealed class Set {
                internal readonly Volley.Ghost?[] arrows = new Volley.Ghost?[BurstPattern.Count];
                internal readonly Vector3[] tips = new Vector3[BurstPattern.Count];
                internal readonly float[] outness = new float[BurstPattern.Count];
                internal Arrow arrow = null!;
                internal float since;
            }

            internal static void Init(MelonPreferences_Category cat) {
                on = cat.CreateEntry("HeldArrows", true, description: "From the rack on, show Burst's fifteen pellets as their own small arrows on the bow (30 arrows showing with the fifteen ghosts). Off = nothing on the bow for them (looks only).");
                scale = cat.CreateEntry("HeldScale", 0.55f, description: "Size of a held pellet arrow's head, relative to an inner ghost's.");
                width = cat.CreateEntry("HeldWidth", 0.6f, description: "Width of a held pellet arrow's shaft, relative to an inner ghost's ([Volley] ShaftWidth).");
                length = cat.CreateEntry("HeldLength", 0.5f, description: "How much of a held pellet arrow's shaft is drawn, relative to a ghost's ([Volley] VisibleFraction).");
                brightness = cat.CreateEntry("HeldBrightness", 1f, description: "Brightness of the held pellet arrows' shafts relative to the ghosts of the ring they sit on (1 = the same: an inner ghost's on the inner ring, [Volley] OuterBrightness on the outer ring; other archers' also times [Volley] OtherArcherBrightness).");
                ripple = cat.CreateEntry("HeldRippleSeconds", 0.15f, description: "Seconds the held pellet arrows take to ripple outward at the rack: the five inner ones first, then the ten outer ones in turn.");
                arrive = cat.CreateEntry("HeldArriveSeconds", 0.08f, description: "Seconds each held pellet arrow takes to slide from the real arrow's tip out to its place.");
                // [Burst] HeldInnerTurn (fix batch C, never installed) is gone: the pattern itself puts every pellet between two ghosts
                join = cat.CreateEntry("JoinSeconds", 0.12f, description: "Seconds a released pellet's streak takes to move from where its held arrow was onto its real path. 0 = it starts on the path.");
                Cfg.Reloaded += () => failed = false; // a changed setting may be what makes it buildable: try again
            }

            static float JoinSeconds => join != null ? Mathf.Max(0f, join.Value) : 0.12f;

            static void Failed(string why) {
                if (!failed) MelonLogger.Warning($"[Archery] Burst: the fifteen pellet arrows could not be built ({why}); nothing shows on the bow for them");
                failed = true;
                foreach (KeyValuePair<BowString, Set> kv in sets) Destroy(kv.Value);
                sets.Clear();
            }

            static void Destroy(Set set) {
                for (int p = 0; p < set.arrows.Length; p++) {
                    if (set.arrows[p] != null) Object.Destroy(set.arrows[p]!.gameObject);
                    set.arrows[p] = null;
                }
            }

            /// <summary>Every frame for each drawn bow (Volley.DrawHeld). While <paramref name="show"/> (Burst has racked), this
            /// bow's set of pellet arrows for the nocked <paramref name="arrow"/>, made on the first such frame (the rack: the
            /// ripple starts then); otherwise none. False when they are not shown: not racked, switched off, or not buildable.</summary>
            static readonly int T_Begin = Timing.Section("look Burst.Held.Begin");
            internal static bool Begin(BowString bow, Arrow arrow, bool show, out Set? set) {
                using var _t = Timing.Begin(T_Begin);
                set = null;
                if (!show || failed || on == null || !on.Value || arrow == null) { Drop(bow); return false; }
#if QUEST
                if (sets.TryGetValue(bow, out Set known) && Il.Same(known.arrow, arrow)) { set = known; return true; }
#else
                if (sets.TryGetValue(bow, out Set known) && ReferenceEquals(known.arrow, arrow)) { set = known; return true; }
#endif
                Drop(bow); // another arrow on the string
                if (Fx.LineMaterial() == null) { Failed("no line material"); return false; }
                if (sets.Count >= SetCap) { foreach (KeyValuePair<BowString, Set> kv in sets) Destroy(kv.Value); sets.Clear(); }
                sets[bow] = set = new Set { arrow = arrow, since = Time.time };
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Burst: {BurstPattern.Count} pellet arrows on '{bow.name}' ({VolleyMath.Total + BurstPattern.Count} arrows showing)");
                return true;
            }

            /// <summary>Pellet <paramref name="p"/>'s held arrow this frame: sliding from the real tip out to
            /// <paramref name="tip"/> (its turn in the ripple), pointing along <paramref name="dir"/>, the front
            /// <paramref name="drawnLength"/> x HeldLength of it drawn; <paramref name="folded"/> (0 to 1) pulls it back into the
            /// arrow (Holy Arrow's gather). <paramref name="tone"/>: the look of the ghosts of its own ring this frame
            /// (Volley.DrawHeld: their one gold switch, or its ring's turn in a multi-arrow main's hold stages, G4), so it has
            /// their colour. No allocation once built. False when it could not be built (the look is then off).</summary>
            /// <param name="green">H2: how far it has turned to the homing green (0 to 1), with the ghosts of its ring (SeekLook.BowGreen).</param>
            internal static bool Place(Set set, int p, Vector3 realTip, Vector3 tip, Vector3 dir, float drawnLength, float bright, float folded, in GoldHold.Tone tone, float green = 0f) {
                try {
                    float since = Time.time - set.since - BurstPattern.HeldDelay(p, ripple.Value);
                    Volley.Ghost? g = set.arrows[p];
                    if (g == null) {
                        // made when its turn in the ripple comes, so the fifteen are not all built in the rack's one frame
                        if (since < 0f) { set.tips[p] = realTip; set.outness[p] = 0f; return true; }
                        set.arrows[p] = g = Volley.Ghost.CreateSmall(set.arrow, "ArcheryBurstPelletArrow");
                    }
                    g.Resize(Mathf.Max(0.01f, scale.Value), Volley.ShaftWidth * Mathf.Max(0f, width.Value));
                    float appear = Mathf.Clamp01(since / Mathf.Max(0.01f, arrive.Value));
                    float outness = appear * (1f - Mathf.Clamp01(folded));
                    Vector3 at = Vector3.Lerp(realTip, tip, outness);
                    float ring = p < BurstPattern.Inner ? 1f : Volley.OuterBrightness; // fixF: as bright as the ghosts of its own ring
                    g.Style(tone, bright * ring * Mathf.Max(0f, brightness.Value)); // the colour of this archer's ghosts
                    g.Green = green;
                    g.Ripple(VolleyFx.Beats.HeadProgress(tone.Flare), VolleyFx.Beats.ShaftBoost(tone.Flare)); // its turn at a hold stage (1, 1 otherwise)
                    g.Hold(at, dir, drawnLength * Mathf.Max(0f, length.Value), outness);
                    set.tips[p] = at;
                    set.outness[p] = outness;
                    return true;
                }
                catch (System.Exception e) { Failed(e.Message); return false; }
            }

            /// <summary>The bow's pellet arrows are gone (released, consumed, cancelled, no longer racked, or the bow is gone).
            /// Where they were is kept for the rest of this frame, for the release (<see cref="Left"/>).</summary>
            internal static void Drop(BowString bow) {
#if QUEST
                if (Il.Same(bow, null) || !sets.TryGetValue(bow, out Set set)) return;
#else
                if (ReferenceEquals(bow, null) || !sets.TryGetValue(bow, out Set set)) return;
#endif
                leftBow = bow;
                leftFrame = Time.frameCount;
                for (int p = 0; p < BurstPattern.Count; p++) { leftTips[p] = set.tips[p]; leftShown[p] = set.outness[p] > 0.5f; }
                Destroy(set);
                sets.Remove(bow);
            }

            /// <summary>At the release (Volley.Launch, the frame the set was dropped): where pellet <paramref name="p"/>'s held
            /// arrow was. False when this machine was not showing it.</summary>
            static bool Left(BowString bow, int p, out Vector3 tip) {
                tip = default;
#if QUEST
                if (p < 0 || p >= BurstPattern.Count || !Il.Same(leftBow, bow) || leftFrame != Time.frameCount || !leftShown[p]) return false;
#else
                if (p < 0 || p >= BurstPattern.Count || !ReferenceEquals(leftBow, bow) || leftFrame != Time.frameCount || !leftShown[p]) return false;
#endif
                tip = leftTips[p];
                return true;
            }

            /// <summary>At the release: pellet <paramref name="p"/>'s streak starts where its held arrow was (no pop) and joins
            /// its real path, which starts at <paramref name="start"/> along <paramref name="dir"/>. Nothing when this machine
            /// was not showing that arrow, or with JoinSeconds 0.</summary>
            internal static void Hand(BowString bow, int p, PelletStreak? streak, Vector3 start, Vector3 dir) {
                float seconds = JoinSeconds;
                if (streak == null || seconds <= 0f || !Left(bow, p, out Vector3 was)) return;
                streak.StartFrom(was - start, seconds, start, dir);
            }
        }


        // ---------------------------------------------------------------- the shed (every machine that draws)

        const float ShedSpread = 20f, ShedSpeed = 6f;                                   // Ruling 9: a forward cone of streaks
        const float ShedFlashBrightness = 1.5f, ShedFlashRange = 1.5f, ShedFlashLife = 0.12f;

        /// <summary>Burst's release: one WhumpSound at the bow, ShedSparks spark streaks forward from each inner head, one
        /// light at the real tip, in the colour of the release's ghosts (<paramref name="gold"/>: their gold switch).</summary>
        static readonly int T_Shed = Timing.Section("look start Burst.Shed");
        /// <param name="colour">H2: the colour of a locked (homing) release's shed, in place of its ghosts' own.</param>
        internal static void Shed(uint release, Vector3[] innerTips, Vector3[] innerDirs, bool gold, float brightness, Vector3 realTip, Vector3 bowAt, int pellets, Color? colour = null) {
            using var _t = Timing.Begin(T_Shed, release);
            VolleyFx.Play(whumpSound.Value, bowAt);
            Color c = colour ?? Volley.GhostColour(gold);
            var spark = new Color(c.r, c.g, c.b, brightness);
            for (int i = 0; i < innerTips.Length && i < innerDirs.Length; i++)
                VolleyFx.Sparks.Emit(innerTips[i], innerDirs[i], shedSparks.Value, spark, ShedSpread, ShedSpeed);
            Fx.Flash(realTip, c, ShedFlashBrightness * brightness, ShedFlashRange, ShedFlashLife);
            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Burst: shed {0} pellets on arrow {1}", pellets, release));
        }
    }

    /// <summary>One pellet's look: a 2-point line StreakLength long behind it, its width and alpha falling linearly to 30% at
    /// Burst's range, ending in a puff of specks from the shared spark emitter when it reaches that range. Streak and puff
    /// have the colour it was made with: that of the release's ghosts (Volley.Launch).</summary>
    internal class PelletStreak : MonoBehaviour {
#if QUEST
        public PelletStreak(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
        const float EndFraction = 0.3f;                // width and alpha at the range, from the spec
        const float PuffSpread = 80f, PuffSpeed = 0.8f; // a soft puff, not a spray
        LineRenderer line = null!;
        Color colour;
        float brightness = 1f, width;
        Vector3 offset;          // fix batch C: from its real path to where its held arrow was on the bow, shrinking to nothing
        float joinFrom, joinSeconds;
        float lengthScale = 1f;  // H2: a homing pellet's streak is longer
        bool steady;             // H2: and as bright and wide all the way (it flies to its creature, not out to Burst's range)

        /// <summary>H2 (SeekLook.Dress): this pellet flies in a locked release: its streak <paramref name="widthScale"/> times as
        /// wide and <paramref name="trailScale"/> times as long, with no fall-off toward Burst's range.</summary>
        internal void Homing(float widthScale, float trailScale) {
            width *= Mathf.Max(0f, widthScale);
            lengthScale = Mathf.Max(0f, trailScale);
            steady = true;
        }

        static readonly int T_Create = Timing.Section("look Burst.PelletStreak.Create");
        internal static PelletStreak? Create(Color c, float brightness) {
            using var _t = Timing.Begin(T_Create);
            if (Fx.LineMaterial() == null) return null;
            var go = new GameObject("ArcheryPelletStreak");
            PelletStreak s = go.AddComponent<PelletStreak>();
            s.width = Volley.ShaftWidth;
            s.line = Fx.NewLine(go, 2, s.width, true);
            s.colour = c;
            s.brightness = brightness;
            s.line.enabled = false; // until its first position
            return s;
        }

        /// <summary>The pellet had its own arrow on the bow (Burst.Held): the streak starts there, <paramref name="from"/> off
        /// its real path's <paramref name="start"/>, and joins the path over <paramref name="seconds"/> (BurstPattern.Join).
        /// Drawn at once, so there is no frame with neither the held arrow nor the streak.</summary>
        internal void StartFrom(Vector3 from, float seconds, Vector3 start, Vector3 dir) {
            offset = from;
            joinFrom = Time.time;
            joinSeconds = seconds;
            Move(start, dir, 0f);
        }

        /// <summary>Place the streak with its front at <paramref name="at"/>, <paramref name="travelled"/> metres from the release point.</summary>
        internal void Move(Vector3 at, Vector3 dir, float travelled) {
            if (dir.sqrMagnitude < 1e-6f) return;
            if (joinSeconds > 0f) {
                float left = BurstPattern.Join(Time.time - joinFrom, joinSeconds);
                if (left > 0f) at += offset * left;
                else joinSeconds = 0f; // on its path from here on
            }
            float r = Burst.Range;
            float k = steady ? 1f : Mathf.Lerp(1f, EndFraction, r > 0f ? Mathf.Clamp01(travelled / r) : 1f);
            line.widthMultiplier = width * k;
            line.SetPosition(0, at);
            line.SetPosition(1, at - dir.normalized * (Burst.StreakLength * lengthScale));
            line.startColor = new Color(colour.r, colour.g, colour.b, k * brightness);
            line.endColor = new Color(colour.r, colour.g, colour.b, 0f);
            line.enabled = true;
        }

        /// <summary>The pellet is gone: a puff of specks when it reached its range (not when it hit something), then removed.</summary>
        internal void End(bool puff, Vector3 at, Vector3 dir) {
            if (puff) VolleyFx.Sparks.Emit(at, dir, Burst.PuffSpecks, new Color(colour.r, colour.g, colour.b, brightness), PuffSpread, PuffSpeed);
            Destroy(gameObject);
        }
    }
}
