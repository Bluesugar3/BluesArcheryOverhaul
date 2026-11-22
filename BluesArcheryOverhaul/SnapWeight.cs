using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using Alta.Progression.Skills;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // What a Quick main's Charge Shot orbs do to a quick shot, made visible (clients only; looks only). The shot itself is
    // unchanged: its stagger and interrupt are Complements.SnapStagger / SnapInterrupts, dealt by Weight on the server.
    // Everything here is in the charge line's own orange-gold ([Fill] BackColour and FrontColour), so it reads as "charge
    // on a quick shot", and exists only for a Quick main with at least one Charge Shot orb (SnapWeightLook, Path.cs).
    // G4 (play-test 8, the user: "very lackluster"; chosen: "the quick shot's extra stagger gets a stronger look and sound"):
    //   Ember     on the string: an orange ring of fire round the shaft at the rear of the arrowhead, facing the archer like
    //             the Quickdraw ring, with a small light on the head. Wider, brighter and hotter (toward gold) with each Charge
    //             Shot orb. (Until 0.16.0 it was a 6 mm bead at the arrow's 'Arrow Tip', which is not the point: on the plain
    //             Arrow that is inside a head 14 cm long and 7.6 cm wide, on a shaft 3.6 cm thick, so it could not be seen;
    //             EmberAtBase off puts it back there.) When the shot can interrupt (two orbs or more) a second, thinner orange
    //             ring rides inside the gold Quickdraw ring as that closes and comes to rest just outside the ember, where it
    //             pulses once the quick shot is ready. The local player's follows the Quickdraw ring (Main.Draw); another
    //             archer's follows Quickdraw's status broadcasts.
    //   LetGo     the quick window is missed with the arrow still drawn: nothing charges for a quick archer, and the bow is seen
    //             to let the weight go: the bead and the ring open and fade over LetGoSeconds, a few embers fall from the head,
    //             LetGoSound plays quietly.
    //   Trail     in flight: the charged arrow's ember trail (ChargeFx.EmberTrail), wider and longer per orb.
    //   Line      Quickdraw 4's line turns to the charge's orange and widens over its last third (Quickdraw4.Line); the creatures
    //             it passes through after the first show the hit at their share of the line.
    //   Hit       on a creature: the charged hit effect ([Fill] HitEffect) at HitEffectScale, at HeavyHitEffectScale from two
    //             orbs up; a short orange light; a low thud (ThudSound) beside the arrow's own sound. When the shot can
    //             interrupt: a sharp crack (InterruptSound), a brief orange ring opening on the creature and a crown of spikes.
    //             The interrupt itself is the server's decision and no message says whether it happened (G3: it cannot be relayed
    //             without a new message), so the cue plays when the shot carries one and a creature was hit, at most once per
    //             creature per interrupt cooldown.
    // Other archers' at [Volley] OtherArcherBrightness, from the relayed release and the build the server sent for them.
    // Settings in [SnapWeight] (looks, not shared). Each look that cannot be built is skipped (one warning), never thrown.
    internal static class SnapWeight {
        static MelonPreferences_Entry<bool> enabled = null!, emberAtBase = null!;
        static MelonPreferences_Entry<float> emberRadius = null!, emberWidth = null!, emberBrightness = null!, emberPerOrb = null!, emberLight = null!,
            emberLightRange = null!, emberSizePerOrb = null!, trailWidthScale = null!, trailWidthPerOrb = null!, trailLengthPerOrb = null!, lineTint = null!,
            lineWidthPerOrb = null!, hitEffectScale = null!, heavyHitEffectScale = null!, hitFlash = null!, hitFlashRange = null!, hitFlashSeconds = null!,
            thudVolume = null!, ringShare = null!, ringWidth = null!, ringPulse = null!, ringPulseHz = null!, cueVolume = null!, cueRingFrom = null!,
            cueRingTo = null!, cueRingSeconds = null!, cueRingWidth = null!, cueSpikeLength = null!, letGoSeconds = null!, letGoOpen = null!, letGoVolume = null!;
        static MelonPreferences_Entry<int> cueSpikes = null!, letGoSparks = null!;
        static MelonPreferences_Entry<string> interruptSound = null!, thudSound = null!, letGoSound = null!;

        const int Cap = 32;
        const int HeavyHitFrom = 2;          // "the charged hit effect at full size from two orbs up"
        const float SeenEmberSeconds = 5f;   // another archer's ember never outlives a missed Cancelled status by more than this
        const float OwnEmberSeconds = 0.2f;  // the local player's goes this long after the ring was last drawn (as the ring itself, Main.OnUpdate)
        const float SeenChargingLevel = 0.6f; // another archer's ember while their Quickdraw charges (1 once it is ready)
        const float FlightSeconds = 10f;      // as ChargeFx's charged flights
        const float EmberAhead = 0.004f;      // EmberAtBase off: metres ahead of the arrow's 'Arrow Tip' the bead sits
        const float EmberBehind = 0.01f;      // metres behind the rear of the arrowhead the ember's ring sits
        const float EmberGap = 0.003f;        // metres between the shaft's surface and the ember's ring
        const float RingGap = 0.004f;         // metres between the ember and the second ring once that has closed onto it
        const float MinHeadBack = 0.01f;      // a head shorter than this behind the tip is taken as not measured
        const float LogGapSeconds = 1f;
        const float ThudOneOrb = 0.6f;        // the thud's volume with one orb, as a share of ThudVolume (full at three)

        static readonly HashSet<string> warned = new HashSet<string>();

        internal static void Init() {
            var cat = Cfg.Category("SnapWeight");
            enabled = cat.CreateEntry("Enabled", true, description: "A Quickdraw main's Charge Shot orbs show on the quick shot: an orange ember on the arrowhead while the Quickdraw ring closes, an ember trail in flight (Quickdraw 4's line turns orange over its last third), the charged hit effect on a creature and a sound when the shot can interrupt. Looks only: off changes nothing the shot does.");
            emberAtBase = cat.CreateEntry("EmberAtBase", true, description: "The ember is a ring round the shaft at the rear of the arrowhead, where you see it from behind the arrow. Off = a small bead at the arrow's tip point as before 0.16.0 (inside the head of most arrows: only its light shows).");
            emberRadius = cat.CreateEntry("EmberRadius", 0.006f, description: "With EmberAtBase off: radius in metres of the ember's bead, with one Charge Shot orb. (At the head's rear the ring sits on the shaft, whatever its thickness.)");
            emberWidth = cat.CreateEntry("EmberWidth", 0.008f, description: "Thickness in metres of the ember's ring, with one Charge Shot orb.");
            emberSizePerOrb = cat.CreateEntry("EmberSizePerOrb", 0.75f, description: "The ember's ring is this much thicker, and its light reaches this much farther and is this much brighter, for each Charge Shot orb after the first (0.75 = 1.75 times with two orbs, 2.5 times with three).");
            emberBrightness = cat.CreateEntry("EmberBrightness", 0.5f, description: "Brightness (0 to 1) of the ember, the trail and the line's tint with one Charge Shot orb.");
            emberPerOrb = cat.CreateEntry("EmberPerOrb", 0.25f, description: "Brightness added for each further Charge Shot orb (three orbs = EmberBrightness + 2 x this, at most 1). The ember also turns from orange toward gold with each orb.");
            emberLight = cat.CreateEntry("EmberLight", 0.5f, description: "Brightness of the small orange light the ember throws on the arrowhead (times the ember's brightness and size). 0 = no light.");
            emberLightRange = cat.CreateEntry("EmberLightRange", 0.15f, description: "Range in metres of the ember's light, with one Charge Shot orb.");
            ringShare = cat.CreateEntry("InterruptRingShare", 0.72f, description: "When the quick shot can interrupt (two or more Charge Shot orbs): a second orange ring inside the gold Quickdraw ring, at this share of its radius, closing with it until it rests just outside the ember. 0 = no second ring.");
            ringWidth = cat.CreateEntry("InterruptRingWidth", 0.003f, description: "Thickness in metres of that ring.");
            ringPulse = cat.CreateEntry("InterruptRingPulse", 0.4f, description: "Once the quick shot is ready that ring pulses: how much (0 to 1) it dims and swells with each pulse. 0 = steady.");
            ringPulseHz = cat.CreateEntry("InterruptRingPulseHz", 3f, description: "Pulses per second of that ring.");
            letGoSeconds = cat.CreateEntry("LetGoSeconds", 0.45f, description: "The quick window was missed with the arrow still drawn: seconds the ember and its ring take to open and fade, so the bow is seen to let the weight go. 0 = they vanish at once.");
            letGoOpen = cat.CreateEntry("LetGoOpen", 2.5f, description: "How many times its size the ember's bead and ring open to while they fade.");
            letGoSparks = cat.CreateEntry("LetGoSparks", 6, description: "Small embers that fall from the arrowhead as the weight is let go. 0 = none.");
            letGoSound = cat.CreateEntry("LetGoSound", "Power Strike End Sounds", description: "A soft sound at the arrowhead as the weight is let go (names separated by ';', the first one found is used; empty = none).");
            letGoVolume = cat.CreateEntry("LetGoVolume", 0.5f, description: "Volume (0 to 1) of LetGoSound.");
            trailWidthScale = cat.CreateEntry("TrailWidthScale", 0.4f, description: "Width of the quick shot's ember trail with one Charge Shot orb, relative to a charged arrow's ([Fill] TrailWidth). 0 = no trail.");
            trailWidthPerOrb = cat.CreateEntry("TrailWidthPerOrb", 0.75f, description: "The trail is this much wider for each further Charge Shot orb (0.75 = 1.75 times with two orbs, 2.5 times with three: a charged arrow's own width).");
            trailLengthPerOrb = cat.CreateEntry("TrailLengthPerOrb", 0.4f, description: "The trail lasts this much longer for each further Charge Shot orb, relative to [Fill] TrailSeconds (0.4 = 1.4 times with two orbs, 1.8 times with three).");
            lineTint = cat.CreateEntry("LineTint", 1f, description: "How strongly (0 to 1) Quickdraw 4's line turns to the charge's orange over its last third (times the ember's brightness). 0 = no tint.");
            lineWidthPerOrb = cat.CreateEntry("LineWidthPerOrb", 0.35f, description: "Quickdraw 4's line is this much wider over its last third for each Charge Shot orb (0.35 = about twice as wide with three). 0 = no widening.");
            hitEffectScale = cat.CreateEntry("HitEffectScale", 0.5f, description: "Size of the charged hit effect ([Fill] HitEffect) where a weighted quick shot hits a creature with one Charge Shot orb, relative to a charged arrow's. 0 = none.");
            heavyHitEffectScale = cat.CreateEntry("HeavyHitEffectScale", 1f, description: "Its size from two Charge Shot orbs up (the heavier impact). 0 = none.");
            hitFlash = cat.CreateEntry("HitFlash", 3f, description: "Brightness of the short orange light where a weighted quick shot hits a creature (times the ember's brightness). 0 = none.");
            hitFlashRange = cat.CreateEntry("HitFlashRange", 2.5f, description: "Metres that light reaches.");
            hitFlashSeconds = cat.CreateEntry("HitFlashSeconds", 0.15f, description: "Seconds that light takes to go out.");
            thudSound = cat.CreateEntry("ThudSound", "Power Hit Strike Sounds;Wood Thump", description: "A low thud where a weighted quick shot hits a creature, beside the arrow's own sound (names separated by ';', the first one found is used; empty = none).");
            thudVolume = cat.CreateEntry("ThudVolume", 1f, description: "Volume (0 to 1) of the thud with three Charge Shot orbs (quieter with fewer). 0 = none.");
            interruptSound = cat.CreateEntry("InterruptSound", "Shield Bash Metal Sounds;Blade Clash Sounds;Click", description: "A sharp crack where a quick shot that can interrupt hits a creature (two or more Charge Shot orbs; names separated by ';', the first one found is used; empty = none). At most once per creature per [Weight] InterruptCooldown.");
            cueVolume = cat.CreateEntry("InterruptVolume", 1f, description: "Volume (0 to 1) of InterruptSound.");
            cueRingFrom = cat.CreateEntry("InterruptRingFrom", 0.15f, description: "With the crack: a brief orange ring opens on the creature round the hit, from this radius in metres.");
            cueRingTo = cat.CreateEntry("InterruptRingTo", 0.7f, description: "The radius in metres that ring opens to while it fades. 0 = no ring.");
            cueRingSeconds = cat.CreateEntry("InterruptRingSeconds", 0.3f, description: "Seconds that ring lasts.");
            cueRingWidth = cat.CreateEntry("InterruptRingLineWidth", 0.035f, description: "Thickness in metres of that ring.");
            cueSpikes = cat.CreateEntry("InterruptSpikes", 6, description: "Sharp orange spikes snapping out of the hit with the crack. 0 = none.");
            cueSpikeLength = cat.CreateEntry("InterruptSpikeLength", 0.45f, description: "Length in metres of those spikes.");
        }

        static bool On => enabled != null && enabled.Value && !ApplicationManager.IsHeadless;

        static float Brightness(int orbs) => SnapWeightLook.Brightness(orbs, emberBrightness.Value, emberPerOrb.Value);

        static void Warn(string what, string why) {
            if (warned.Add(what)) MelonLogger.Warning($"[Archery] Snap weight: {what} could not be shown ({why}); the quick shot goes without it");
        }

        // ---------------------------------------------------------------- on the string: the ember

        sealed class Ember {
            public bool local;
            public int orbs;
            public bool interrupts;      // the shot can interrupt: the second ring shows
            public bool ready;           // the quick shot is ready: the second ring pulses
            public float level;          // 0..1: how far the Quickdraw ring has closed (the local player's), or the status level
            public float ringRadius;     // metres: the gold Quickdraw ring's radius now (another archer's: its closed radius)
            public uint arrow;           // another archer's: the arrow the status was for
            public float since;          // Time.time the ring was last drawn (the local player's) or of the last status (another archer's)
            public float letGoAt = -1f;  // Time.time the weight was let go (the window was missed); below 0 = it is held
            public bool atBase;          // it is a ring round the shaft at the rear of the head (else the bead at the tip)
            public float back, shaft;    // metres from the tip back to the head's rear; the shaft's radius there
            public GameObject? go;
            public LineRenderer? bead, ring;
            public Light? lamp;
            public bool failed;
        }

#if QUEST
        static readonly Dictionary<BowString, Ember> embers = new Dictionary<BowString, Ember>(Il.ByPointer<BowString>());
#else
        static readonly Dictionary<BowString, Ember> embers = new Dictionary<BowString, Ember>();
#endif
        static readonly List<BowString> gone = new List<BowString>();
        static float lastEmberLog = float.NegativeInfinity;

        static float LetGoSeconds => letGoSeconds != null ? Mathf.Max(0f, letGoSeconds.Value) : 0.45f;

        /// <summary>The local player, every frame the Quickdraw ring is drawn on this bow (Main.Draw): <paramref name="level"/>
        /// 0..1 is how far the ring has closed (1 = charged, draining as the window runs out), <paramref name="radius"/> the
        /// ring's radius in metres this frame, <paramref name="ready"/> whether the quick shot is ready.</summary>
        static readonly int T_Ring = Timing.Section("look SnapWeight.Ring (per frame)");
        internal static void Ring(BowString bow, Build build, float level, float radius, bool ready) {
            using var _t = Timing.Begin(T_Ring);
            if (!On || bow == null) return;
            int orbs = SnapWeightLook.Orbs(build);
            if (orbs <= 0) return; // an entry left from an earlier build runs out by itself (Frame)
            if (!embers.TryGetValue(bow, out Ember e)) {
                if (embers.Count >= Cap) return;
                embers[bow] = e = new Ember { local = true };
                LogEmber(bow, orbs, true);
            }
            e.local = true;
            e.orbs = orbs;
            e.interrupts = SnapWeightLook.Interrupts(build, Paths.Values);
            e.ready = ready;
            e.level = level;
            e.ringRadius = radius;
            e.since = Time.time;
            e.letGoAt = -1f; // the ring shows again: the weight is back
        }

        /// <summary>The local player's Quickdraw ring was hidden (Main.Hide, possibly every frame): its ember goes with it, unless
        /// it is being let go (LetGo: it then fades by itself).</summary>
        internal static void RingHidden() {
            if (embers.Count == 0) return;
            foreach (KeyValuePair<BowString, Ember> kv in embers) if (kv.Value.local && kv.Value.letGoAt < 0f) kv.Value.since = float.NegativeInfinity;
        }

        /// <summary>The local player missed the quick window with the arrow still drawn (Main.Draw, every frame while that is so):
        /// nothing charges for a quick archer, so the ember on this bow is let go: it opens and fades, embers fall, once.</summary>
        internal static void LetGo(BowString bow) {
            if (embers.Count == 0 || bow == null || !embers.TryGetValue(bow, out Ember e) || e.letGoAt >= 0f) return;
            StartLetGo(bow, e);
        }

        static void StartLetGo(BowString bow, Ember e) {
            e.letGoAt = Time.time;
            string note = "";
            try {
                if (On && e.go != null && LetGoSeconds > 0f) {
                    Vector3 at = e.go.transform.position;
                    float k = Brightness(e.orbs) * (e.local ? 1f : Volley.OtherArcherBrightness);
                    Color c = ChargeFx.BackColour;
                    // they drop off the head and fall (the shared emitter's sparks have weight)
                    VolleyFx.Sparks.Emit(at, Vector3.down, Mathf.Max(0, letGoSparks.Value), new Color(c.r, c.g, c.b, Mathf.Clamp01(k + 0.25f)), 50f, 0.6f, 1.2f);
                    VolleyFx.Play(letGoSound.Value, at, 0f, Mathf.Clamp01(letGoVolume.Value) * (e.local ? 1f : Mathf.Clamp01(Volley.OtherArcherBrightness)));
                    note = $", {Mathf.Max(0, letGoSparks.Value)} falling ember(s), sound '{letGoSound.Value}'";
                }
            }
            catch (Exception ex) { Warn("the weight being let go", ex.Message); }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Snap weight: the quick window was missed on '{bow.name}' ({(e.local ? "own" : "seen")}); the ember is let go{note}");
        }

        /// <summary>Clients: a status of Quickdraw for the arrow on this bow (VolleySkill.ClientStatus). Another archer's
        /// ember shows from ChargingStarted to Cancelled or Consumed; the local player's follows the ring instead. Cancelled with
        /// the arrow still on the string lets it go (it fades; Frame removes it at once if the arrow has left).</summary>
        static readonly int T_Status = Timing.Section("SnapWeight.Status");
        internal static void Status(BowString bow, ToolBasedSkillStatus status, string owner, bool local, uint arrowId) {
            using var _t = Timing.Begin(T_Status, owner);
            if (bow == null || local || ApplicationManager.IsHeadless) return;
            bool starts = status == ToolBasedSkillStatus.ChargingStarted || status == ToolBasedSkillStatus.Charged;
            Build build = starts && On ? Builds.ClientBuild(owner) : default;
            int orbs = starts && On ? SnapWeightLook.Orbs(build) : 0;
            if (orbs <= 0) {
                // fix1 (final-G34 M-3): a seen archer's weight is let go (embers, LetGoSound) only when the quick window passed: the
                // last status was Charged. An ordinary relax (Cancelled after ChargingStarted) removes the ember silently. Once per
                // draw: letGoAt stays set until the next ChargingStarted, and a fading ember is left to fade
                if (status == ToolBasedSkillStatus.Cancelled && On && embers.TryGetValue(bow, out Ember old) && !old.local && old.go != null && LetGoSeconds > 0f
                    && (old.ready || old.letGoAt >= 0f)) {
                    if (old.letGoAt < 0f) StartLetGo(bow, old);
                }
                else Remove(bow);
                return;
            }
            if (!embers.TryGetValue(bow, out Ember e) || e.local) {
                if (e != null) Remove(bow);
                if (embers.Count >= Cap) return;
                embers[bow] = e = new Ember { local = false };
                LogEmber(bow, orbs, false);
            }
            e.orbs = orbs;
            e.interrupts = SnapWeightLook.Interrupts(build, Paths.Values);
            e.ready = status == ToolBasedSkillStatus.Charged;
            e.arrow = arrowId;
            e.level = status == ToolBasedSkillStatus.Charged ? 1f : SeenChargingLevel;
            e.ringRadius = Main.TightRadius; // their ring is not drawn here: the second ring sits where it would have closed
            e.since = Time.time;
            e.letGoAt = -1f;
        }

        static void LogEmber(BowString bow, int orbs, bool local) {
            if (Time.time - lastEmberLog < LogGapSeconds) return; // once per draw: a ring that blinks does not fill the log
            lastEmberLog = Time.time;
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Snap weight: ember on '{bow.name}' ({orbs} Charge Shot orb(s), {(local ? "own" : "seen")})");
        }

        static void Remove(BowString bow) {
#if QUEST
            if (Il.Same(bow, null) || !embers.TryGetValue(bow, out Ember e)) return;
#else
            if (ReferenceEquals(bow, null) || !embers.TryGetValue(bow, out Ember e)) return;
#endif
            embers.Remove(bow);
            if (e.go != null) UnityEngine.Object.Destroy(e.go);
        }

        /// <summary>Every frame (Main.OnLateUpdate): each ember is put on its arrow's point, or goes when its draw is over.</summary>
        internal static void Frame() {
            if (embers.Count == 0 || ApplicationManager.IsHeadless) return;
            gone.Clear();
            bool on = On;
            float now = Time.time;
            foreach (KeyValuePair<BowString, Ember> kv in embers) {
                BowString bow = kv.Key; Ember e = kv.Value;
                bool inTime = e.letGoAt >= 0f ? now - e.letGoAt < LetGoSeconds : now - e.since <= (e.local ? OwnEmberSeconds : SeenEmberSeconds);
                bool live = on && bow != null && !e.failed && inTime;
                Arrow? arrow = null;
                if (live) {
                    PickupDock dock = bow!.StringDock;
                    Pickup p = dock != null && dock.HasDockedPickup ? dock.DockedPickup : null!;
                    if (p != null) {
                        Arrow a = p.GetComponent<Arrow>();
                        bool same = e.local || (p.Entity != null && p.Entity.Identifier == e.arrow);
                        if (a != null && same) arrow = a;
                    }
                }
                if (arrow == null) { gone.Add(bow!); continue; }
                try { Draw(e, arrow, now); }
                catch (Exception ex) { e.failed = true; Warn("the ember on the arrowhead", ex.Message); }
            }
            foreach (BowString b in gone) Remove(b); // a destroyed bow is still its own key
        }

        static bool placeLogged;

        static readonly int T_Make = Timing.Section("look SnapWeight.Make (ember)");
        static bool Make(Ember e, Arrow arrow) {
            using var _t = Timing.Begin(T_Make);
            if (Fx.LineMaterial() == null) { e.failed = true; Warn("the ember on the arrowhead", "no line material"); return false; }
            // Where it can be seen: round the shaft at the rear of this arrow's own head (measured from its meshes). An arrow
            // whose head or shaft cannot be measured keeps the bead at the tip.
            e.atBase = false;
            if (emberAtBase.Value) {
                try {
                    e.back = Volley.Ghost.HeadBackOf(arrow);
                    e.shaft = e.back >= MinHeadBack ? ChargeFx.ShaftRadiusOf(arrow) : 0f;
                    e.atBase = e.back >= MinHeadBack && e.shaft > 0f;
                }
                catch (Exception ex) { Warn("the ember's place at the rear of the arrowhead", ex.Message); }
            }
            if (!placeLogged) {
                placeLogged = true;
                if (Logs.Summary) MelonLogger.Msg(e.atBase
                    ? string.Format(CultureInfo.InvariantCulture, "[Archery] Snap weight: the ember is a ring round the shaft (radius {0:0.000} m) at the rear of the arrowhead, {1:0.000} m behind the arrow's tip point ('{2}'; said once)", e.shaft, e.back, arrow.name)
                    : $"[Archery] Snap weight: the ember is a bead at the arrow's tip point ('{arrow.name}': {(emberAtBase.Value ? "its head could not be measured" : "EmberAtBase is off")}; said once)");
            }
            var go = new GameObject("ArcherySnapEmber");
            LineRenderer lr = Fx.NewLine(go, 2, Mathf.Max(0f, emberWidth.Value), false);
            Fx.BuildCircle(lr);
            // the light is on a child of its own; the root holds the one Renderer
            var lampGo = new GameObject("light");
            lampGo.transform.SetParent(go.transform, false);
            lampGo.transform.localPosition = Vector3.up; // on the ring's top (the ring is a unit circle scaled to its radius): a light on the axis would be inside the shaft
            Light lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            lamp.intensity = 0f;
            e.go = go; e.bead = lr; e.lamp = lamp;
            return true;
        }

        // The second ring (the shot can interrupt), on a child of its own: made when first wanted; one that cannot be made
        // only loses the ring (one warning), the ember stays.
        static bool ringFailed;

        static LineRenderer? MakeRing(Ember e) {
            if (ringFailed || e.go == null) return null;
            try {
                var go = new GameObject("interrupt ring");
                go.transform.SetParent(e.go.transform, false);
                LineRenderer lr = Fx.NewLine(go, 2, Mathf.Max(0f, ringWidth.Value), false);
                Fx.BuildCircle(lr);
                lr.sortingOrder = 1; // over the bead
                lr.enabled = false;
                return lr;
            }
            catch (Exception ex) { ringFailed = true; Warn("the second ring (the shot can interrupt)", ex.Message); return null; }
        }

        // The ember faces along the arrow, as the Quickdraw ring does: the archer looks down the shaft, so anything drawn
        // along it would be seen end-on. It is a ring round the shaft just behind the arrowhead: from behind it shows in
        // front of the head, and nothing of the arrow hides it (the 'Arrow Tip' it used to sit on is inside the head).
        // Like the Quickdraw ring it rides the arrow as a child, so it cannot lag behind the hand in VR; Remove destroys it
        // (the arrow released, the ring hidden, the draw over), so it is never left on an arrow.
        static readonly int T_Draw = Timing.Section("look SnapWeight.Draw (ember per frame)");
        static void Draw(Ember e, Arrow arrow, float now) {
            using var _t = Timing.Begin(T_Draw);
            if (e.go == null && !Make(e, arrow)) return;
            Transform tipT = arrow.ArrowTip != null ? arrow.ArrowTip.transform : arrow.transform;
            Vector3 fwd = arrow.transform.forward;
            if (fwd.sqrMagnitude < 1e-8f) return;
            float level = Mathf.Clamp01(e.level);
            // letting go (the window was missed): what is left of it, and how far it has opened
            float left = e.letGoAt >= 0f ? SnapWeightLook.LetGo(now - e.letGoAt, LetGoSeconds) : 1f;
            float open = e.letGoAt >= 0f ? SnapWeightLook.Open(left, letGoOpen.Value) : 1f;
            float size = SnapWeightLook.Size(e.orbs, emberSizePerOrb.Value); // bigger with each Charge Shot orb
            float seen = e.local ? 1f : Volley.OtherArcherBrightness;
            float k = Brightness(e.orbs) * (0.35f + 0.65f * level) * seen * left;
            float flicker = UnityEngine.Random.Range(0.75f, 1f); // one factor per frame, as the Fill's ember
            Transform t = e.go!.transform;
            // at the head's rear it hangs on the arrow itself (the 'Arrow Tip' is scaled unevenly: a ring on it is an oval)
            Transform parent = e.atBase ? arrow.transform : tipT;
            if (t.parent != parent) t.SetParent(parent, false);
            Vector3 at = e.atBase ? tipT.position - fwd * (e.back + EmberBehind) : tipT.position + fwd * EmberAhead;
            t.SetPositionAndRotation(at, Quaternion.LookRotation(fwd));
            float parentScale = parent.lossyScale.x;
            // its thickness grows as the ring closes and with each orb; it thins as it opens and goes
            float thick = Mathf.Max(0f, emberWidth.Value) * size * (0.6f + 0.4f * level) * (0.9f + 0.1f * flicker);
            float radius = e.atBase
                ? (e.shaft + EmberGap + thick * 0.5f) * open                                                              // its inner edge stays just off the shaft
                : Mathf.Max(0f, emberRadius.Value) * size * (0.7f + 0.3f * level) * (0.9f + 0.1f * flicker) * open;        // the bead at the tip
            radius = Mathf.Max(1e-5f, radius);
            t.localScale = Vector3.one * (radius / (parentScale > 0f ? parentScale : 1f));
            LineRenderer bead = e.bead!;
            bead.widthMultiplier = thick * left;
            // orange, a little toward gold as it closes, and hotter (gold, then toward white) with each further orb
            float heat = (Mathf.Min(Complements.MaxOrbs, e.orbs) - 1) * 0.5f;
            Color c = Color.Lerp(ChargeFx.BackColour, ChargeFx.FrontColour, Mathf.Clamp01(level * 0.5f + 0.5f * heat));
            c = Color.Lerp(c, Color.white, 0.25f * heat * level);
            c.a = Mathf.Clamp01(k * flicker);
            bead.startColor = c;
            bead.endColor = c;
            Light lamp = e.lamp!;
            float light = Mathf.Max(0f, emberLight.Value) * k * flicker * size;
            lamp.enabled = light > 0.001f;
            if (lamp.enabled) {
                lamp.color = ChargeFx.BackColour;
                lamp.intensity = light;
                lamp.range = Mathf.Max(0f, emberLightRange.Value) * size;
            }

            // the second ring: the shot can interrupt
            float share = Mathf.Clamp01(ringShare.Value);
            bool wanted = e.interrupts && share > 0f && e.ringRadius > 0f && ringWidth.Value > 0f;
            if (wanted && e.ring == null) e.ring = MakeRing(e);
            if (e.ring == null) return;
            if (!wanted) { if (e.ring.enabled) e.ring.enabled = false; return; }
            float pulse = 0f; // 0..1, only once the quick shot is ready
            float depth = Mathf.Clamp01(ringPulse.Value);
            if (e.ready && e.letGoAt < 0f && depth > 0f && ringPulseHz.Value > 0f) pulse = 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * ringPulseHz.Value * now));
            // inside the gold ring while that closes; it comes to rest just outside the ember (the gold ring closes on into the head)
            float ringRadius = Mathf.Max(share * e.ringRadius, radius / open + thick * 0.5f + RingGap) * open;
            e.ring.transform.localScale = Vector3.one * (ringRadius / radius); // its parent is scaled to the bead's radius
            e.ring.widthMultiplier = Mathf.Max(0f, ringWidth.Value) * (1f + depth * pulse) * left;
            Color rc = ChargeFx.BackColour;
            rc.a = Mathf.Clamp01((0.5f + 0.5f * level) * (1f - 0.5f * depth * (1f - pulse)) * seen * left);
            e.ring.startColor = rc;
            e.ring.endColor = rc;
            if (!e.ring.enabled) e.ring.enabled = true;
        }

        // ---------------------------------------------------------------- in flight: the trail

        struct Flight { public int Orbs; public bool Interrupts, Local; public float At; }

        static readonly Dictionary<uint, Flight> flights = new Dictionary<uint, Flight>();

        /// <summary>Every client, from Volley.Fire: the arrow left this bow. A Quick main's flying quick shot with Charge
        /// Shot orbs gets the ember trail (wider and longer per orb) and its first creature hit is remembered (Hit).
        /// Quickdraw 4's line has its own look (Line), so nothing is kept for it here.</summary>
        internal static void Released(BowString bow, NetworkEntity? arrowEntity, Resolution r) {
            if (ApplicationManager.IsHeadless || bow == null) return;
            Remove(bow); // the arrow has left: its ember goes
            if (!On || arrowEntity == null || arrowEntity.Identifier == 0u) return;
            uint id = arrowEntity.Identifier;
            flights.Remove(id);
            PlayerController pc = arrowEntity.CommonPickup != null ? arrowEntity.CommonPickup.LastPlayerInteractor : null!;
            if (pc == null && bow.Pickup != null) pc = bow.Pickup.LastPlayerInteractor;
            if (pc == null) return;
            bool local = pc.IsLocalPlayer;
            Build build = local ? Builds.LocalBuild() : Builds.ClientBuild(Builds.NameOf(pc.NetworkPlayer));
            if (!SnapWeightLook.Shows(r.Kind, r.Ultimate, build) || r.Ultimate == UltimateUse.Line) return;
            int orbs = SnapWeightLook.Orbs(build);
            if (flights.Count >= Cap) flights.Clear();
            flights[id] = new Flight { Orbs = orbs, Interrupts = SnapWeightLook.Interrupts(build, Paths.Values), Local = local, At = Time.time };
            bool trail = false;
            float width = Mathf.Max(0f, trailWidthScale.Value) * SnapWeightLook.Size(orbs, trailWidthPerOrb.Value);
            float length = SnapWeightLook.Size(orbs, trailLengthPerOrb.Value);
            try {
                Arrow arrow = arrowEntity.GetComponent<Arrow>();
                if (arrow != null && width > 0f)
                    trail = ChargeFx.EmberTrail.Attach(arrow, width, Brightness(orbs) * (local ? 1f : Volley.OtherArcherBrightness), length) != null;
            }
            catch (Exception e) { Warn("the ember trail", e.Message); }
            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture,
                "[Archery] Snap weight: arrow {0} flies weighted ({1} Charge Shot orb(s), {2}); ember trail {3} (x{4:0.##} of a charged arrow's width, x{5:0.##} of its length)",
                id, orbs, local ? "own" : "seen", trail ? "on" : "off", width, length));
        }

        // ---------------------------------------------------------------- Quickdraw 4's line

        /// <summary>What one line shot shows of the weight (Quickdraw4.Line draws it): how strongly its last third turns to the
        /// charge's orange, the Charge Shot orbs that widen it there, and whose it is. Orbs 0 = the plain line.</summary>
        internal struct LineWeight {
            public float Tint, WidthPerOrb;
            public int Orbs;
            public bool Local, Interrupts;
        }

        /// <summary>Clients, once per line shot (Quickdraw4.ResolveOnClient): the weight this line shows (nothing for anyone but
        /// a Quick main with Charge Shot orbs, or when the archer is not known). Where the line struck a creature it also
        /// shows the hit look, and the local player's feeds the Balance Ring (the line is cast, so no arrow hit ever
        /// reaches it there).</summary>
        static readonly int T_Line = Timing.Section("SnapWeight.Line");
        internal static LineWeight Line(Arrow arrow, bool local, bool struck, RaycastHit hit) {
            using var _t = Timing.Begin(T_Line);
            try {
                if (!On || arrow == null) return default;
                Build build;
                if (local) build = Builds.LocalBuild();
                else {
                    Pickup p = arrow.Entity != null ? arrow.Entity.CommonPickup : null!;
                    PlayerController pc = p != null ? p.LastPlayerInteractor : null!;
                    if (pc == null) return default;
                    if (pc.IsLocalPlayer) { local = true; build = Builds.LocalBuild(); }
                    else build = Builds.ClientBuild(Builds.NameOf(pc.NetworkPlayer));
                }
                int orbs = SnapWeightLook.Orbs(build);
                if (orbs <= 0) return default;
                uint id = arrow.Entity != null ? arrow.Entity.Identifier : 0u;
                var weight = new LineWeight {
                    Tint = Mathf.Clamp01(lineTint.Value) * Brightness(orbs) * (local ? 1f : Volley.OtherArcherBrightness),
                    WidthPerOrb = Mathf.Max(0f, lineWidthPerOrb.Value), Orbs = orbs, Local = local, Interrupts = SnapWeightLook.Interrupts(build, Paths.Values),
                };
                HealthObject? creature = Creature(struck, hit);
                if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Snap weight: line arrow {0} weighted ({1} Charge Shot orb(s), {2}); orange over its last third at {3:0.##}, x{4:0.##} as wide at its end",
                    id, orbs, local ? "own" : "seen", weight.Tint, SnapWeightLook.LineWidth(1f, orbs, weight.WidthPerOrb)));
                if (creature != null) {
                    Show(id, hit.point, hit.normal, creature, orbs, weight.Interrupts, local, "line", 1f);
                    if (local) Balance.Hit(creature, 0); // the staggered creature's ring, as for a flying quick shot
                }
                return weight;
            }
            catch (Exception e) { Warn("the line's weight", e.Message); return default; }
        }

        /// <summary>Clients: a creature the weighted line passed through after its first (Quickdraw4.ResolveOnClient): the hit
        /// look at its <paramref name="share"/> of the line (the server deals it that share of the stagger), never the
        /// interrupt's cue. Nothing for a line that carries no weight.</summary>
        internal static void Passed(in LineWeight weight, uint arrowId, RaycastHit hit, float share) {
            try {
                if (!On || weight.Orbs <= 0) return;
                HealthObject? creature = Creature(true, hit);
                float k = SnapWeightLook.PassShare(share);
                if (creature == null || !(k > 0f)) return;
                Show(arrowId, hit.point, hit.normal, creature, weight.Orbs, false, weight.Local, "line (passed through)", k);
            }
            catch (Exception e) { Warn("the line's weight on a creature it passed", e.Message); }
        }

        static HealthObject? Creature(bool struck, RaycastHit hit) {
            if (!struck || hit.collider == null) return null;
            ImpactDetector det = hit.collider.GetComponentInParent<ImpactDetector>();
            HealthObject h = det != null ? det.HealthTarget : null!;
#if QUEST
            return h != null && !(h.Is<PlayerHealth>()) && h.HasCrippleStat ? h : null;
#else
            return h != null && !(h is PlayerHealth) && h.HasCrippleStat ? h : null;
#endif
        }

        // ---------------------------------------------------------------- the hit

        static readonly Dictionary<int, float> cues = new Dictionary<int, float>(); // creature -> Time.time of its last interrupt cue

        /// <summary>Every client, from Weight's hit hook or its embed fallback: the weighted quick shot's first hit. On a
        /// creature it shows the hit look; anything else (terrain, a prop, a player) only ends the wait.</summary>
        static readonly int T_Hit = Timing.Section("look start SnapWeight.Hit");
        internal static void Hit(uint arrowId, Vector3 point, Vector3 normal, HealthObject? creature) {
            using var _t = Timing.Begin(T_Hit, arrowId);
            if (ApplicationManager.IsHeadless || flights.Count == 0) return;
            if (!flights.TryGetValue(arrowId, out Flight f)) return;
            flights.Remove(arrowId); // the first hit only
            if (!On || Time.time - f.At > FlightSeconds) return;
#if QUEST
            if (creature == null || creature.Is<PlayerHealth>() || !creature.HasCrippleStat) return;
#else
            if (creature == null || creature is PlayerHealth || !creature.HasCrippleStat) return;
#endif
            Show(arrowId, point, normal, creature, f.Orbs, f.Interrupts, f.Local, "quick shot", 1f);
        }

        // The weighted hit on a creature: the charged hit effect (heavier from two orbs up), a short orange light and a low
        // thud; when the shot can interrupt, the cue. `share`: 1, or a pierced creature's share of the line (smaller, quieter).
        static readonly int T_Show = Timing.Section("look SnapWeight.Show");
        static void Show(uint arrowId, Vector3 point, Vector3 normal, HealthObject creature, int orbs, bool interrupts, bool local, string what, float share) {
            using var _t = Timing.Begin(T_Show, arrowId);
            bool log = Logs.Debug; // fix1 (final-G34 M-5): the log's strings are built only when the line is printed
            string effect = "no hit effect", thud = "", cue = "";
            float seen = local ? 1f : Mathf.Clamp01(Volley.OtherArcherBrightness);
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            try {
                float k = SnapWeightLook.HitScale(orbs, hitEffectScale.Value, heavyHitEffectScale.Value, HeavyHitFrom) * share;
                string names = ChargeFx.HitEffect;
                if (k > 0f && !string.IsNullOrWhiteSpace(names)) {
                    PooledObjectDefinition? def = Fx.FindEffect(names); // a miss is logged by Fx
                    if (def != null && Fx.GetScaled(def, point, Quaternion.LookRotation(n), k)) { if (log) effect = string.Format(CultureInfo.InvariantCulture, "'{0}' at {1:0.##} size", def.name, k); }
                    else if (log) effect = $"'{names}' not found";
                }
                float flash = Mathf.Max(0f, hitFlash.Value) * Brightness(orbs) * share * seen;
                if (flash > 0f && hitFlashSeconds.Value > 0f) Fx.Flash(point + n * 0.05f, ChargeFx.BackColour, flash, Mathf.Max(0.1f, hitFlashRange.Value), hitFlashSeconds.Value);
            }
            catch (Exception e) { Warn("the hit effect", e.Message); }
            try {
                string sound = thudSound.Value;
                float volume = Mathf.Clamp01(thudVolume.Value) * Mathf.Lerp(ThudOneOrb, 1f, (Mathf.Min(Complements.MaxOrbs, orbs) - 1) * 0.5f) * share * seen;
                if (volume > 0.01f && !string.IsNullOrWhiteSpace(sound)) {
                    VolleyFx.Play(sound, point, 0f, volume);
                    if (log) thud = string.Format(CultureInfo.InvariantCulture, ", thud '{0}' at {1:0.##}", sound, volume);
                }
            }
            catch (Exception e) { Warn("the thud", e.Message); }
            try {
                if (interrupts) {
                    int key = creature.GetInstanceID();
                    float now = Time.time;
                    float last = cues.TryGetValue(key, out float at) ? at : float.NaN;
                    if (SnapWeightLook.CueDue(now, last, Weight.InterruptCooldown)) {
                        if (cues.Count >= Cap && !cues.ContainsKey(key)) cues.Clear();
                        cues[key] = now;
                        string played = Cue(point, n, seen, log);
                        if (log) cue = ", interrupt cue: " + played;
                    }
                    else if (log) cue = ", interrupt cue waits (cooldown)";
                }
            }
            catch (Exception e) { Warn("the interrupt cue", e.Message); }
            Vector3 p = point;
            if (Logs.Debug) MelonLogger.Msg(string.Format(CultureInfo.InvariantCulture, "[Archery] Snap weight: {0} arrow {1} hit '{2}' at ({3:0.00}, {4:0.00}, {5:0.00}) ({6}, {7} Charge Shot orb(s)): {8}{9}{10}",
                what, arrowId, creature.name, p.x, p.y, p.z, local ? "own" : "seen", orbs, effect, thud, cue));
        }

        /// <summary>The interrupt's cue at the hit: a sharp crack, a brief orange ring opening on the creature and a crown of
        /// spikes snapping out of the hit (straight, as Quickdraw 4's). Returns what it played, for the log.</summary>
        static string Cue(Vector3 point, Vector3 normal, float seen, bool log) {
            string sound = interruptSound.Value;
            float volume = Mathf.Clamp01(cueVolume.Value) * seen;
            bool cracked = !string.IsNullOrWhiteSpace(sound) && volume > 0.01f;
            if (cracked) VolleyFx.Play(sound, point, 0f, volume);
            Color orange = ChargeFx.BackColour;
            bool ring = CueRing.Play(point, normal, new Color(orange.r, orange.g, orange.b, seen), cueRingFrom.Value, cueRingTo.Value, cueRingSeconds.Value, cueRingWidth.Value);
            int spikes = Mathf.Clamp(cueSpikes.Value, 0, 24);
            float length = Mathf.Max(0f, cueSpikeLength.Value);
            if (spikes > 0 && length > 0f) {
                Fx.Basis(normal, out Vector3 u, out Vector3 w);
                float life = Mathf.Max(0.05f, cueRingSeconds.Value * 0.6f), turn = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                var tail = new Color(orange.r, orange.g, orange.b, seen);
                for (int i = 0; i < spikes; i++) {
                    float phi = turn + i * (2f * Mathf.PI / spikes);
                    // across the surface and a little out of it: a crown round the hit
                    Vector3 dir = ((Mathf.Cos(phi) * u + Mathf.Sin(phi) * w) * 0.85f + normal * 0.5f).normalized;
                    Fx.Bolt(point, point + dir * (length * UnityEngine.Random.Range(0.6f, 1f)), 0f, life * UnityEngine.Random.Range(0.7f, 1f), 0.016f, new Color(1f, 1f, 1f, seen), tail, 0);
                }
            }
            if (!log) return "";
            return $"{(cracked ? $"'{sound}'" : "no sound")}{(ring ? ", ring" : "")}{(spikes > 0 && length > 0f ? $", {spikes} spike(s)" : "")}";
        }

        /// <summary>The brief ring of the interrupt's cue: a circle lying on the struck surface round the hit, opening from one
        /// radius to another while it fades (as the charged hit's dust ring, ChargeFx.DustRing, in the charge's orange).</summary>
        sealed class CueRing : MonoBehaviour {
            const float Lift = 0.03f; // off the surface, so it is not buried in it
            LineRenderer lr = null!;
            Color colour;
            float born, from, to, seconds, width;

            internal static bool Play(Vector3 at, Vector3 normal, Color c, float from, float to, float seconds, float width) {
                if (!(to > 0f) || !(seconds > 0f) || !(width > 0f) || Fx.LineMaterial() == null) return false;
                var go = new GameObject("ArcherySnapInterruptRing");
                go.transform.position = at + normal * Lift;
                go.transform.rotation = Quaternion.LookRotation(normal); // the circle's local XY plane lies on the surface
                CueRing r = go.AddComponent<CueRing>();
                r.lr = Fx.NewLine(go, 2, width, false);
                Fx.BuildCircle(r.lr);
                r.colour = c;
                r.born = Time.time;
                r.from = Mathf.Max(0.01f, Mathf.Min(from, to));
                r.to = to;
                r.seconds = seconds;
                r.width = width;
                r.Frame(0f);
                return true;
            }

            void Frame(float k) {
                float eased = 1f - (1f - k) * (1f - k); // fast out, slowing: a snap
                transform.localScale = Vector3.one * Mathf.Lerp(from, to, eased);
                lr.widthMultiplier = width * (1f - 0.6f * k);
                Color c = colour;
                c.a = colour.a * (1f - k);
                lr.startColor = c;
                lr.endColor = c;
            }

            static readonly int TUpdate = Timing.Section("Update SnapWeight.CueRing");
            void Update() {
                using var _t = Timing.Begin(TUpdate);
                float k = (Time.time - born) / seconds;
                if (k >= 1f) { Destroy(gameObject); return; }
                Frame(k);
            }
        }
    }
}
