using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Batch H2: the finished look of the homing volley (Seeking Volley). The owner played the plain H1 look and said: "How you lock
    // onto enemies is pretty bad because it's just a 2D circle that sits on it no matter how you look. ... Make the arrows a little
    // bit bigger and have a trail be a little bit longer", and chose "Rings around the body" for the lock. Everything a player sees,
    // hears or feels of the lock and of the homing colour goes through this one class; nothing in it decides anything (Seek.cs and
    // Volley.cs call it). Clients only; the headless server never draws.
    //   The lock     LockCage (SeekCage.cs): three green arcs on three tilted planes round the creature, closing with the dwell,
    //                snapping shut at the lock, springing open when it is lost; the main lock has thicker, brighter arcs and a
    //                fourth, outer ring that tumbles the other way. If the cage cannot be built: one log line and the plain rings
    //                of H1 (PlainRings, below) until the settings are reloaded.
    //   The bow      while a release now would home, the ghosts and Burst's pellet arrows on the bow turn the homing green in a
    //                sweep round their rings and each leans toward the creature it will fly to (Bow, BowGreen, Lean).
    //   In flight    homing arrows are bigger and carry a longer, tapering trail drawn on a smooth curve (Dress; Volley.Ghost,
    //                VolleySim, TrailCurve); the core arrows a little bigger still and brighter. Everything they do is green.
    //   Arrival      SeekBurst (SeekFx.cs): a short green burst on each locked creature as its arrows land, a heavier one with a
    //                thump in the shooter's bow hand where the core cluster lands; the real arrow is flashed in there (SeekReveal).
    // Shape maths (tested, SeekLookTests.cs): CageShape, HomingLean, TrailCurve in Spectral.cs.
    // Settings: [SeekLook] (all looks, per client, re-read live); the colour, the lock's sounds and the bow-hand pulse stay in
    // [SeekingVolley], where H1 made them. This file reads only its own entries and leaf values of other files.
    internal static class SeekLook {
        /// <summary>One creature the shooter's lock is on or closing on: its id, its root, its middle and its bounds' half-sizes as
        /// last measured, and that middle in the root's own space at that moment (the look follows the root between the shooter's
        /// sweeps; unused without a root).</summary>
        internal struct Mark { public uint Id; public Transform? Root; public Vector3 Centre, Extents, Offset; }

        static MelonPreferences_Entry<string> colour = null!, lockSound = null!, lostSound = null!, arriveSound = null!, coreSound = null!, arriveEffect = null!, coreEffect = null!;
        static MelonPreferences_Entry<float> ringWidth = null!, fillWidth = null!, pulse = null!;
        static MelonPreferences_Entry<bool> cage = null!, mainRing = null!, bowGreen = null!, smooth = null!, arrive = null!, greenHeads = null!;
        static MelonPreferences_Entry<float> cageScale = null!, cageMin = null!, cageMax = null!, openScale = null!, nearScale = null!, openDegrees = null!, nearDegrees = null!,
            formAlpha = null!, cageAlpha = null!, otherAlpha = null!, cageWidth = null!, mainWidth = null!, minAngle = null!, tailAlpha = null!, spinSlow = null!, spinFast = null!,
            spinLocked = null!, precess = null!, snapSeconds = null!, flashSeconds = null!, flashBoost = null!, lostSeconds = null!, lostSpring = null!, halo = null!, haloAlpha = null!,
            lockLight = null!, mainRingScale = null!, mainRingTilt = null!, mainRingSpin = null!,
            bowSweep = null!, bowTurn = null!, bowFade = null!, leanDegrees = null!, leanSeconds = null!,
            arrowScale = null!, trailScale = null!, trailTaper = null!, coreScale = null!, coreGlow = null!, releaseFlash = null!,
            arriveLight = null!, arriveRingTo = null!, arriveSeconds = null!, arriveVolume = null!, arriveEffectScale = null!,
            coreLight = null!, coreRingTo = null!, coreSeconds = null!, coreVolume = null!, coreHaptic = null!, coreHapticSeconds = null!, coreEffectScale = null!, revealSeconds = null!;
        static MelonPreferences_Entry<int> trailPoints = null!, arriveSparks = null!, arriveSoundsMax = null!, coreSparks = null!;
        static readonly Color DefaultColour = new Color(0.4f, 0.88f, 0.48f); // #66E07A: a leaf green beside the ghosts' cyan and the charge's gold
        const float Never = -1e9f;
        static bool cageFailed, plainFailed, bowFailed, flightFailed, arriveFailed, revealFailed;

        internal static void Init(MelonPreferences_Category seeking) {
            colour = seeking.CreateEntry("Colour", "#66E07A", description: "Clients, looks: the homing green. On a locked release every homing arrow, its trail, its sparks and its landing light are this colour (in place of the ghosts' own and of the hold stages' gold); the lock's cage and the ghosts on the bow while a release would home are too.");
            ringWidth = seeking.CreateEntry("RingWidth", 0.02f, description: "Clients, looks: width of the plain ring on each locked creature (only shown when the cage of [SeekLook] is off or cannot be built).");
            fillWidth = seeking.CreateEntry("FillRingWidth", 0.01f, description: "Clients, looks: width of the plain closing ring on the creature being locked (only shown when the cage of [SeekLook] is off or cannot be built).");
            lockSound = seeking.CreateEntry("LockSound", "Skill Orb Acquire Sounds", description: "Clients, looks: the game's sound at each lock (names from loaded_assets.txt, ';' between them; empty = none).");
            lostSound = seeking.CreateEntry("LostSound", "Skill Orb Return Sounds", description: "Clients, looks: the game's sound when a lock is lost (empty = none).");
            pulse = seeking.CreateEntry("LockPulse", 0.6f, description: "Clients, looks: strength (0 to 1) of the bow-hand pulse at each lock (two short pulses). 0 = none.");

            CageLook d = CageLook.Default;
            var cat = Cfg.Category("SeekLook");
            MelonPreferences_Entry<float> F(string key, float value, string text) => cat.CreateEntry(key, value, description: text);
            cage = cat.CreateEntry("Cage", true, description: "The lock's look on a creature: three green arcs on three tilted planes round its body (a cage), closing as the lock forms and snapping shut when it locks. Off = the plain rings that face you. Looks only, as everything in [SeekLook]; only the shooter sees locks.");
            cageScale = F("CageScale", 1.15f, "The locked cage's radius, as a multiple of the creature's largest half-size (from its colliders' bounds).");
            cageMin = F("CageMinRadius", 0.45f, "The locked cage is never smaller than this (metres), so a small creature's still reads.");
            cageMax = F("CageMaxRadius", 2.5f, "The locked cage is never larger than this (metres).");
            openScale = F("CageOpenScale", d.OpenScale, "While the lock forms: the cage starts this many times its locked size...");
            nearScale = F("CageNearScale", d.NearScale, "... and has tightened to this many times its locked size when the dwell is full; it then snaps shut onto the body.");
            openDegrees = F("CageOpenDegrees", d.OpenDegrees, "While the lock forms: each arc starts as this many degrees of its circle...");
            nearDegrees = F("CageNearDegrees", d.NearDegrees, "... and has grown to this many when the dwell is full (360 = a closed ring; it closes in the snap). How closed the arcs are is how far the lock has come.");
            formAlpha = F("CageFormAlpha", d.FormAlpha, "Brightness of the cage when the lock starts to form, as a share of its full brightness (it brightens as it closes).");
            cageAlpha = F("CageAlpha", 0.95f, "Brightness (0 to 1) of the main lock's cage, and of a cage that is forming.");
            otherAlpha = F("CageOtherAlpha", 0.7f, "Brightness of the other locks' cages (the second to the fourth), so the main lock stands out.");
            cageWidth = F("CageWidth", 0.018f, "Width in metres of the cage's arcs.");
            mainWidth = F("CageMainWidth", 1.6f, "The main lock's arcs are this many times as wide (the first lock: the core arrows fly to it and your real arrow ends in it).");
            minAngle = F("CageMinAngle", 0.07f, "The arcs are never thinner on screen than this many degrees of your view, so a cage 60 m away still reads (they are drawn wider with distance). 0 = always CageWidth.");
            tailAlpha = F("CageTailAlpha", 0.35f, "Each arc fades along its length to this share of its brightness, so its bright end is seen to run round the creature.");
            spinSlow = F("CageSpinSlow", 40f, "Degrees a second the arcs run round the creature when the lock starts to form...");
            spinFast = F("CageSpinFast", 220f, "... quickening to this when the dwell is full.");
            spinLocked = F("CageSpinLocked", 60f, "Degrees a second the bright ends run round a locked cage.");
            precess = F("CageTurn", 25f, "Degrees a second the whole cage turns round the creature (slower while it forms).");
            snapSeconds = F("CageSnapSeconds", d.SnapSeconds, "Seconds of the snap shut at the lock.");
            flashSeconds = F("CageFlashSeconds", d.FlashSeconds, "Seconds the flash at the lock takes to fade.");
            flashBoost = F("CageFlash", 2.2f, "At the lock the arcs flash this many times as bright (toward white) and wider, fading over CageFlashSeconds. 1 = no flash.");
            lostSeconds = F("CageLostSeconds", d.LostSeconds, "Seconds a lost lock's cage takes to spring open and fade.");
            lostSpring = F("CageLostSpring", d.LostSpring, "A lost cage springs open to this many times the size it had.");
            halo = F("CageHalo", 3f, "A soft glow under each arc, this many times as wide as the arc (the game's orb-line material). 0 = none.");
            haloAlpha = F("CageHaloAlpha", 0.3f, "Brightness of that glow, as a share of the arc's.");
            lockLight = F("LockLight", 1.2f, "Brightness of the short green light on the creature at the lock. 0 = none.");
            mainRing = cat.CreateEntry("MainRing", true, description: "The main lock's cage has a fourth ring: a little larger, tilted, tumbling the other way round the cage.");
            mainRingScale = F("MainRingScale", 1.2f, "Size of that ring, as a multiple of the cage's.");
            mainRingTilt = F("MainRingTilt", 35f, "Degrees that ring leans from level.");
            mainRingSpin = F("MainRingSpin", 90f, "Degrees a second it tumbles round the creature (against the cage's turn).");

            bowGreen = cat.CreateEntry("BowGreen", true, description: "While a release would home (a lock held within the release cone), the ghosts and Burst's pellet arrows on your bow turn the homing green and each leans toward the creature it will fly to. Off = the bow is as without a lock.");
            bowSweep = F("BowSweepSeconds", 0.2f, "Seconds the green takes to sweep round the rings on the bow (the top arrow first, then clockwise). 0 = all at once.");
            bowTurn = F("BowTurnSeconds", 0.08f, "Seconds each arrow on the bow takes to turn green when its turn comes.");
            bowFade = F("BowFadeSeconds", 0.15f, "Seconds the green takes to leave the bow when no release would home any more.");
            leanDegrees = F("LeanDegrees", 6f, "Each ghost on the bow leans up to this many degrees toward the creature it will fly to. 0 = no lean.");
            leanSeconds = F("LeanSeconds", 0.2f, "Seconds the lean takes to come in.");

            greenHeads = cat.CreateEntry("GreenHeads", true, description: "The arrowheads of homing ghosts (on the bow once it is green, and in flight) are green too: a copy of the ghosts' head material with its colours set to the homing green. Off = they keep the ghosts' own heads (the look of 0.17.0).");
            arrowScale = F("ArrowScale", 1.3f, "Homing arrows in flight (ghosts, pellets, the core arrows) are this many times the size of an ordinary volley's: head and shaft width (the owner: \"a little bit bigger\").");
            trailScale = F("TrailScale", 1.5f, "A homing arrow's trail is this many times as long as an ordinary ghost's shaft (a pellet's streak likewise).");
            trailTaper = F("TrailTaper", 0.1f, "A homing arrow's trail narrows to this share of its width at its end (and fades to nothing).");
            trailPoints = cat.CreateEntry("TrailPoints", 12, description: "Points each homing arrow's trail is drawn with (4 to 24): more is smoother through the curve.");
            coreScale = F("CoreScale", 1.25f, "The core arrows (the five that take the real arrow's place) are this many times larger again.");
            coreGlow = F("CoreGlow", 1.6f, "The core arrows are this many times as bright (toward white), so the eye follows them. 1 = as the others.");
            smooth = cat.CreateEntry("SmoothFlight", true, description: "Homing arrows and their trails are drawn on a smooth curve through the flight's steps, one physics step behind it. Off = drawn straight from the last step (the look of 0.17.0: corners in a hard turn).");
            releaseFlash = F("ReleaseFlash", 1.5f, "Brightness of the green light at the arrow's tip when a locked volley without Burst is released (with Burst its own shed light is green). 0 = none.");

            arrive = cat.CreateEntry("Arrive", true, description: "The arrival: a short green burst on each locked creature as its arrows land, and a heavier one where the core cluster lands (with the thump in the shooter's bow hand). Off = only the landing sparks.");
            arriveSparks = cat.CreateEntry("ArriveSparks", 10, description: "Sparks of the burst on a locked creature. 0 = none.");
            arriveLight = F("ArriveLight", 1.5f, "Brightness of the green light of that burst. 0 = none.");
            arriveRingTo = F("ArriveRingTo", 1.7f, "The creature's cage is thrown outward to this many times its size as it fades.");
            arriveSeconds = F("ArriveSeconds", d.BurstSeconds, "Seconds that burst lasts.");
            arriveSound = cat.CreateEntry("ArriveSound", "Orbskill Placed", description: "The game's sound of that burst (names from loaded_assets.txt, ';' between them; empty = none).");
            arriveVolume = F("ArriveVolume", 0.5f, "Volume (0 to 1) of ArriveSound.");
            arriveSoundsMax = cat.CreateEntry("ArriveSoundsMax", 2, description: "At most this many ArriveSounds per release.");
            arriveEffect = cat.CreateEntry("ArriveEffect", "", description: "One of the game's pooled effects played on the creature as well (names from loaded_assets.txt; empty = none). Its colour is the game's own.");
            arriveEffectScale = F("ArriveEffectScale", 0.4f, "Size of ArriveEffect, as a share of the game's own.");
            coreSparks = cat.CreateEntry("CoreSparks", 28, description: "Sparks of the heavier burst where the core cluster lands. 0 = none.");
            coreLight = F("CoreLight", 3f, "Brightness of its green light. 0 = none.");
            coreRingTo = F("CoreRingTo", 2.4f, "The main lock's cage is thrown outward to this many times its size.");
            coreSeconds = F("CoreSeconds", 0.45f, "Seconds the heavier burst lasts.");
            coreSound = cat.CreateEntry("CoreSound", "Power Hit Strike Sounds;Wood Thump", description: "The game's sound where the core cluster lands (the first name found is used; empty = none).");
            coreVolume = F("CoreVolume", 0.8f, "Volume (0 to 1) of CoreSound.");
            coreHaptic = F("CoreHaptic", 0.8f, "Strength (0 to 1) of the thump in the shooter's bow hand when the core cluster lands. 0 = none.");
            coreHapticSeconds = F("CoreHapticSeconds", 0.08f, "Seconds of that thump.");
            coreEffect = cat.CreateEntry("CoreEffect", "", description: "One of the game's pooled effects played where the core cluster lands as well (empty = none). Its colour is the game's own.");
            coreEffectScale = F("CoreEffectScale", 0.6f, "Size of CoreEffect, as a share of the game's own.");
            revealSeconds = F("RevealSeconds", 0.35f, "Your real arrow does not pop into view where the core cluster lands: a green ghost of it fades out over it for this long. 0 = it just appears.");
            Cfg.Reloaded += () => { cageFailed = plainFailed = bowFailed = flightFailed = arriveFailed = revealFailed = false; headTried = false; LockCage.Reloaded(); };
        }

        /// <summary>The homing green ([SeekingVolley] Colour).</summary>
        internal static Color Colour => colour != null ? Cfg.Colour(colour, DefaultColour) : DefaultColour;

        static void FailedOnce(ref bool flag, string what, string instead, Exception e) {
            if (flag) return;
            flag = true;
            MelonLogger.Warning($"[Archery] Seek look: {what} could not be shown ({e.Message}); {instead} until the settings are reloaded");
        }

        // ================================================================ the lock (the shooter only)

        /// <summary>What the cage is drawn with this frame ([SeekLook], read once per frame by LockCage).</summary>
        internal struct CageSet {
            public CageLook Look;
            public Color Colour;
            public float Scale, Min, Max, Alpha, OtherAlpha, Width, MainWidth, MinRadians, TailAlpha, SpinSlow, SpinFast, SpinLocked, Turn, FlashBoost,
                Halo, HaloAlpha, Light, MainRingScale, MainRingTilt, MainRingSpin;
            public bool MainRing;
        }

        internal static CageSet Cage() => new CageSet {
            Look = new CageLook {
                OpenScale = Mathf.Max(1f, openScale.Value), NearScale = Mathf.Clamp(nearScale.Value, 1f, Mathf.Max(1f, openScale.Value)),
                OpenDegrees = Mathf.Clamp(openDegrees.Value, 5f, 360f), NearDegrees = Mathf.Clamp(nearDegrees.Value, Mathf.Clamp(openDegrees.Value, 5f, 360f), 360f),
                FormAlpha = Mathf.Clamp01(formAlpha.Value), SnapSeconds = Mathf.Max(0f, snapSeconds.Value), FlashSeconds = Mathf.Max(0f, flashSeconds.Value),
                LostSeconds = Mathf.Max(0.01f, lostSeconds.Value), LostSpring = Mathf.Max(1f, lostSpring.Value), BurstSeconds = Mathf.Max(0.01f, arriveSeconds.Value),
            },
            Colour = Colour, Scale = Mathf.Max(0.1f, cageScale.Value), Min = Mathf.Max(0.05f, cageMin.Value), Max = Mathf.Max(0.05f, cageMax.Value),
            Alpha = Mathf.Clamp01(cageAlpha.Value), OtherAlpha = Mathf.Clamp01(otherAlpha.Value), Width = Mathf.Max(0.001f, cageWidth.Value),
            MainWidth = Mathf.Max(0.1f, mainWidth.Value), MinRadians = Mathf.Max(0f, minAngle.Value) * Mathf.Deg2Rad, TailAlpha = Mathf.Clamp01(tailAlpha.Value),
            SpinSlow = spinSlow.Value, SpinFast = spinFast.Value, SpinLocked = spinLocked.Value, Turn = precess.Value, FlashBoost = Mathf.Max(1f, flashBoost.Value),
            Halo = Mathf.Max(0f, halo.Value), HaloAlpha = Mathf.Clamp01(haloAlpha.Value), Light = Mathf.Max(0f, lockLight.Value),
            MainRing = mainRing.Value, MainRingScale = Mathf.Max(1f, mainRingScale.Value), MainRingTilt = Mathf.Clamp(mainRingTilt.Value, 0f, 85f), MainRingSpin = mainRingSpin.Value,
        };

        static bool CageOn => cage != null && cage.Value && !cageFailed;

        /// <summary>The cage look failed (LockCage): one log line, its objects go, and the plain rings stand in until a settings reload.</summary>
        internal static void CageFailed(Exception e) {
            FailedOnce(ref cageFailed, "the lock's cage", "the plain rings show instead", e);
            LockCage.Clear();
        }

        static readonly List<Vector3> plainCentres = new List<Vector3>();
        static readonly List<float> plainRadii = new List<float>();
        static float PlainRadius(Vector3 e) => Mathf.Clamp(Mathf.Max(e.x, Mathf.Max(e.y, e.z)), 0.3f, 3f);

        /// <summary>One held frame of the local player's volley with the orb working (Seek.Frame): a cage on each lock, in lock
        /// order (the first is the main lock), and a closing one on the creature being locked (<paramref name="fill"/> 0 to 1).
        /// Not called = nothing to show: the cages spring open a moment later.</summary>
        internal static void Frame(List<Mark> locks, bool filling, in Mark fillAt, float fill) {
            if (ApplicationManager.IsHeadless) return;
            if (CageOn) {
                try { LockCage.Frame(locks, filling, fillAt, fill); return; }
                catch (Exception e) { CageFailed(e); }
            }
            if (plainFailed) return;
            try {
                plainCentres.Clear(); plainRadii.Clear();
                foreach (Mark m in locks) { plainCentres.Add(m.Centre); plainRadii.Add(PlainRadius(m.Extents)); }
                PlainRings.Get().Show(plainCentres, plainRadii, filling, fillAt.Centre, PlainRadius(fillAt.Extents), fill);
            }
            catch (Exception e) { FailedOnce(ref plainFailed, "the lock rings", "locks show nothing", e); PlainRings.HideAll(); }
        }

        /// <summary>A lock was gained (<paramref name="count"/> held now): the lock sound and two pulses in the bow hand. The cage
        /// snaps shut and flashes by itself (LockCage sees the lock in the next Frame).</summary>
        internal static void Locked(Vector3 at, Controller? hand, int count) {
            if (ApplicationManager.IsHeadless) return;
            try {
                VolleyFx.Play(lockSound.Value, at);
                VolleyFx.Pulses(hand, 2, pulse.Value, VolleyFx.PulseGap);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek look: the lock's sound or pulse failed: {e.Message}"); }
        }

        /// <summary>A lock was lost: the lost sound (its cage springs open by itself).</summary>
        internal static void Lost(Vector3 at) {
            if (ApplicationManager.IsHeadless) return;
            try { VolleyFx.Play(lostSound.Value, at); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek look: the lost sound failed: {e.Message}"); }
        }

        /// <summary>The draw ended without a shot: every cage of it springs open now.</summary>
        internal static void DrawEnded() {
            if (ApplicationManager.IsHeadless) return;
            try { LockCage.Release(null, 0f); PlainRings.HideAll(); }
            catch (Exception e) { CageFailed(e); }
        }

        /// <summary>H4 (H23-review minor 5): the server refused this release's lock (Seek.Tick saw it fly the real arrow): every cage kept
        /// for it springs open now (DrawEnded leaves cages held for a release in flight alone, so it is not that).</summary>
        internal static void Refused(Seek.Flight f) {
            if (ApplicationManager.IsHeadless || f == null) return;
            try { foreach (uint id in f.Ids) LockCage.Let(id); }
            catch (Exception e) { CageFailed(e); }
        }

        /// <summary>The shot left (Seek.ClaimLocks): the cages of the locks it homes on (<paramref name="kept"/>; null = none) stay on
        /// their creatures for up to <paramref name="seconds"/>, until its arrows land there; the others spring open.</summary>
        internal static void Claimed(List<uint>? kept, float seconds) {
            if (ApplicationManager.IsHeadless) return;
            try { LockCage.Release(kept, seconds); PlainRings.HideAll(); }
            catch (Exception e) { CageFailed(e); }
        }

        // ================================================================ the bow tells you too (the shooter only)

        static bool bowOn;
        static float bowOnAt = Never, bowOffAt = Never;
        static int bowFrame = -10;

        /// <summary>Every frame the local player's volley shows on the bow (Volley.DrawHeld): whether a release now would home.
        /// Starts the green's sweep round the rings when it first would, and its fade when it no longer would.</summary>
        internal static void Bow(bool homes) {
            int frame = Time.frameCount;
            if (frame - bowFrame > 2) { bowOn = false; bowOnAt = bowOffAt = Never; } // another draw: nothing carries over
            bowFrame = frame;
            bool on = homes && bowGreen != null && bowGreen.Value && !bowFailed;
            if (on == bowOn) return;
            bowOn = on;
            if (on) bowOnAt = Time.time; else bowOffAt = Time.time;
        }

        /// <summary>How green (0 to 1) the arrow on the bow at <paramref name="azimuthDegrees"/> round the real arrow is this frame:
        /// the green sweeps round each ring as the hold stages' gold does (GoldStages: the top arrow first, then clockwise), and
        /// fades from all of them together when no release would home any more.</summary>
        internal static float BowGreen(double azimuthDegrees) {
            if (bowOnAt <= Never || Time.frameCount - bowFrame > 2) return 0f;
            float delay = GoldStages.SweepDelay(azimuthDegrees, Mathf.Max(0f, bowSweep.Value)), turn = Mathf.Max(0f, bowTurn.Value);
            if (bowOn) return GoldStages.Turned(Time.time - bowOnAt, delay, turn);
            float fade = Mathf.Max(0f, bowFade.Value), gone = fade > 0f ? Mathf.Clamp01((Time.time - bowOffAt) / fade) : 1f;
            return gone >= 1f ? 0f : GoldStages.Turned(bowOffAt - bowOnAt, delay, turn) * (1f - gone);
        }

        /// <summary><paramref name="dir"/> of an arrow on the bow whose tip is at <paramref name="tip"/>, leaned toward
        /// <paramref name="target"/> (the middle of the creature it will fly to) by up to LeanDegrees, easing in over LeanSeconds
        /// from the moment the bow turned green (HomingLean).</summary>
        internal static Vector3 Lean(Vector3 tip, Vector3 dir, Vector3 target) {
            if (!bowOn) return dir;
            float max = leanDegrees.Value, seconds = leanSeconds.Value;
            if (!(max > 0f)) return dir;
            float share = seconds > 0f ? Mathf.Clamp01((Time.time - bowOnAt) / seconds) : 1f;
            share = share * share * (3f - 2f * share);
            Vector3 to = target - tip;
            HomingLean.Toward(dir.x, dir.y, dir.z, to.x, to.y, to.z, max, share, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }

        /// <summary>The bow's homing look failed (Volley.DrawHeld): one log line, and the bow is as without a lock.</summary>
        internal static void BowFailed(Exception e) {
            FailedOnce(ref bowFailed, "the homing look on the bow", "the bow shows no lock", e);
            bowOn = false; bowOnAt = bowOffAt = Never;
        }

        // ================================================================ in flight (every client that draws the release)

        static Material? headMaterial, headBase;
        static bool headTried;

        /// <summary>The green arrowhead of a homing ghost: a copy of the ghosts' own head material (the game's skill-glow material,
        /// or the flat one) with every colour property set to the homing green, made once per settings load and shared by every
        /// homing ghost. Null when it is switched off, or when that material has no colour to set: the heads then stay as they are
        /// (said once).</summary>
        static readonly int T_HeadMaterial = Timing.Section("asset SeekLook.HeadMaterial (green ghost heads)");
        /// <summary>An earlier green copy, destroyed after the longest a held or flying arrow could still show it.</summary>
        static void Retire(Material? old) {
            if (old == null) return;
            try { UnityEngine.Object.Destroy(old, 120f); } catch { }
            if (ReferenceEquals(old, headMaterial)) headMaterial = null;
        }

        internal static Material? HeadMaterial() {
            if (headTried) return headMaterial;
            using var _t = Timing.Begin(T_HeadMaterial);
            headTried = true;
            // H4 (H23-review-opus minor 10): a settings reload no longer leaves the last copy behind: the same ghosts' material is
            // re-tinted in place; a different one gets a new copy and the old copy is destroyed once no arrow can still show it
            Material? previous = headMaterial;
            headMaterial = null;
            try {
                if (greenHeads == null || !greenHeads.Value) { Retire(previous); return null; }
                Material? own = Volley.ResolveHeadMaterial();
                if (own == null) { Retire(previous); return null; }
                if (previous != null && headBase == own) {
                    if (Volley.HeavyLook.Tint(previous, Colour).Length > 0) { headMaterial = previous; return headMaterial; }
                }
                Retire(previous);
                var m = new Material(own) { name = own.name + " (Archery homing arrow)" };
                string props = Volley.HeavyLook.Tint(m, Colour);
                if (props.Length == 0) {
                    UnityEngine.Object.Destroy(m);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek look: the ghosts' head material '{own.name}' has no colour to set; homing arrowheads keep the ghosts' own look");
                    return null;
                }
                headMaterial = m;
                headBase = own;
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek look: homing arrowheads use a green copy of '{own.name}' ({Fx.ShaderName(own)}), colours set: {props}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek look: the green arrowheads could not be made ({e.Message}); homing arrowheads keep the ghosts' own look"); }
            return headMaterial;
        }

        /// <summary>Homing arrows and their trails are drawn on the smooth curve through the flight's steps ([SeekLook] SmoothFlight).</summary>
        internal static bool Smooth => smooth != null && smooth.Value && !flightFailed;
        /// <summary>Points a homing arrow's trail is drawn with (4 to MaxTrailPoints).</summary>
        internal static int TrailPoints => trailPoints != null ? Mathf.Clamp(trailPoints.Value, 4, MaxTrailPoints) : 12;
        internal const int MaxTrailPoints = 24;

        /// <summary>The smooth flight failed (VolleySim): one log line, and homing arrows are drawn straight from their last step
        /// (the look of H1).</summary>
        internal static void FlightFailed(Exception e) => FailedOnce(ref flightFailed, "the homing arrows' smooth trail", "they are drawn straight from each step", e);

        /// <summary>Dresses a ghost that flies in a locked release: ArrowScale times the size, a trail TrailScale times as long that
        /// tapers and fades; a core arrow CoreScale larger again and CoreGlow brighter. A ghost that cannot be dressed keeps the
        /// look of H1 (green, its own size).</summary>
        internal static void Dress(Volley.Ghost ghost, bool core, float brightness) {
            if (ghost == null || flightFailed) return;
            try {
                float k = Mathf.Clamp(arrowScale.Value, 0.2f, 4f) * (core ? Mathf.Clamp(coreScale.Value, 0.2f, 4f) : 1f);
                if (core) ghost.Style(new GoldHold.Tone { Gold = 0f, Glow = Mathf.Max(1f, coreGlow.Value), Width = 1f, Shimmer = -1f }, brightness);
                ghost.Bigger(k);
                ghost.Trail(Mathf.Clamp(trailScale.Value, 0.1f, 10f), Mathf.Clamp01(trailTaper.Value));
            }
            catch (Exception e) { FlightFailed(e); }
        }

        /// <summary>Dresses a pellet's streak that flies in a locked release: ArrowScale times as wide, TrailScale times as long,
        /// and as bright all the way (it flies to its creature, not to Burst's range).</summary>
        internal static void Dress(PelletStreak? streak) {
            if (streak == null || flightFailed) return;
            try { streak.Homing(Mathf.Clamp(arrowScale.Value, 0.2f, 4f), Mathf.Clamp(trailScale.Value, 0.1f, 10f)); }
            catch (Exception e) { FlightFailed(e); }
        }

        /// <summary>A locked volley left the bow: one green light at the arrow's tip, unless Burst's shed (green too) lights it.</summary>
        internal static void Released(uint release, Vector3 tip, float brightness, bool shed) {
            if (ApplicationManager.IsHeadless || shed) return;
            try { if (releaseFlash.Value > 0f) Fx.Flash(tip, Colour, releaseFlash.Value * brightness, 1.5f, 0.12f); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek look: the release light failed: {e.Message}"); }
        }

        // ================================================================ arrival (every client that draws the release)

        static bool ArriveOn => arrive != null && arrive.Value && !arriveFailed;

        static void Measure(Transform root, out Vector3 centre, out Vector3 extents) {
            if (Targets.BoundsOf(root, out Bounds b)) { centre = b.center; extents = b.extents; }
            else { centre = root.position + Vector3.up * 0.5f; extents = new Vector3(0.5f, 0.5f, 0.5f); }
        }

        /// <summary>The first arrows of a locked release landed on lock <paramref name="index"/>'s creature (Seek.Arrived, once per
        /// lock): a short green burst there. The shooter's cage on it is thrown outward; a watcher gets a cage just for that.</summary>
        internal static void Arrived(Seek.Flight f, int index, Transform root, Vector3 point) {
            if (ApplicationManager.IsHeadless) return;
            try {
                if (!ArriveOn) { LockCage.Let(f.Ids[index]); return; } // no burst: the shooter's cage just springs open
                float bright = f.Local ? 1f : Mathf.Clamp01(Volley.OtherArcherBrightness);
                Measure(root, out Vector3 centre, out Vector3 extents);
                bool sound = f.ArriveSounds < Mathf.Max(0, arriveSoundsMax.Value);
                if (sound) f.ArriveSounds++;
                SeekBurst.Play(point, centre, Vector3.up, Colour, bright, Mathf.Max(0, arriveSparks.Value), Mathf.Max(0f, arriveLight.Value), Mathf.Max(0.05f, arriveSeconds.Value),
                               sound ? arriveSound.Value : "", Mathf.Clamp01(arriveVolume.Value), arriveEffect.Value, arriveEffectScale.Value, false);
                if (CageOn) LockCage.Arrive(f.Ids[index], root, centre, extents, Mathf.Max(1f, arriveRingTo.Value), Mathf.Max(0.05f, arriveSeconds.Value), false, bright);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek look: arrow {f.Release} arrival burst on lock {index + 1} of {f.Ids.Length} ({(f.Local ? "own" : "seen")})");
            }
            catch (Exception e) { FailedOnce(ref arriveFailed, "the arrival burst", "only the landing sparks show", e); }
        }

        /// <summary>The core cluster of a locked release ended (Seek.CoreEnded, once per release): where it landed, the heavier
        /// burst, with a thump in the shooter's own bow hand; the main lock's cage is thrown outward when that creature is what it
        /// struck (<paramref name="struck"/>), and springs open when it is not. Ended in the air: only the cage springs open.</summary>
        internal static void CoreLanded(Seek.Flight f, bool landed, HealthObject? struck, Vector3 point, Vector3 dir) {
            if (ApplicationManager.IsHeadless) return;
            try {
                uint main = f.Ids.Length > 0 ? f.Ids[0] : 0u;
                bool onMain = landed && struck != null && main != 0u && Targets.IdOf(struck) == main;
                if (f.Arrived.Length > 0) f.Arrived[0] = true;
                if (!landed || !ArriveOn) { LockCage.Let(main); return; }
                float bright = f.Local ? 1f : Mathf.Clamp01(Volley.OtherArcherBrightness);
                Vector3 centre = point; Vector3 extents = new Vector3(0.5f, 0.5f, 0.5f); Transform? root = null;
                if (onMain) { root = Targets.RootOf(struck!); Measure(root, out centre, out extents); }
                SeekBurst.Play(point, centre, -dir, Colour, bright, Mathf.Max(0, coreSparks.Value), Mathf.Max(0f, coreLight.Value), Mathf.Max(0.05f, coreSeconds.Value),
                               coreSound.Value, Mathf.Clamp01(coreVolume.Value), coreEffect.Value, coreEffectScale.Value, true);
                if (onMain && CageOn) LockCage.Arrive(main, root, centre, extents, Mathf.Max(1f, coreRingTo.Value), Mathf.Max(0.05f, coreSeconds.Value), true, bright);
                else LockCage.Let(main);
                if (f.Local && coreHaptic.Value > 0f) {
                    BowString? bow = Targets.Of(f.Release)?.Bow;
                    Controller? hand = bow != null ? Volley.BowHand(bow) : null;
                    if (hand != null) hand.TriggerHaptic(Mathf.Clamp01(coreHaptic.Value), Mathf.Clamp(coreHapticSeconds.Value, 0.01f, 0.5f));
                }
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Seek look: arrow {f.Release} core burst at {Targets.P(point)}{(onMain ? " on the main lock" : "")} ({(f.Local ? "own, bow-hand thump" : "seen")})");
            }
            catch (Exception e) { FailedOnce(ref arriveFailed, "the core's burst", "only the landing sparks show", e); }
        }

        /// <summary>The real arrow of a locked release has just been shown where the core cluster ended (Seek.PlaceReal): a green
        /// ghost of it fades out over it, so it is flashed in with the burst and does not pop.</summary>
        internal static void Reveal(Arrow arrow, bool local) {
            if (ApplicationManager.IsHeadless || arrow == null || revealFailed) return;
            try {
                float seconds = revealSeconds.Value;
                if (seconds > 0f) SeekReveal.Play(arrow, seconds, local ? 1f : Mathf.Clamp01(Volley.OtherArcherBrightness));
            }
            catch (Exception e) { FailedOnce(ref revealFailed, "the real arrow's flash-in", "it appears at once", e); }
        }

        // ================================================================ start-up check

        /// <summary>SelfCheck, clients: the cage's poses and the bow's numbers with the settings just loaded (numbers only, no
        /// objects), so a broken entry shows at start-up and not on the first lock. Returns how many it evaluated.</summary>
        internal static int Check() {
            if (ApplicationManager.IsHeadless || cage == null) return 0;
            int n = 0;
            CageSet s = Cage();
            for (int i = 0; i <= 10; i++) {
                CagePose a = CageShape.Forming(i / 10f, s.Look), b = CageShape.Locked(i * 0.03f, s.Look), c = CageShape.Lost(i * 0.03f, a.Scale, a.Sweep, s.Look),
                    d = CageShape.Burst(i * 0.03f, Mathf.Max(1f, arriveRingTo.Value), s.Look.BurstSeconds);
                float w = CageShape.Width(s.Width, i * 6f, s.MinRadians), r = CageShape.Radius(i * 0.3f, 1f, 0.5f, s.Scale, s.Min, s.Max);
                if (float.IsNaN(a.Scale + a.Sweep + a.Alpha + b.Scale + b.Sweep + b.Flash + c.Scale + c.Alpha + d.Scale + d.Alpha + w + r))
                    throw new InvalidOperationException("a lock cage number is not a number");
                n += 4;
            }
            float lean = leanDegrees.Value + leanSeconds.Value + bowSweep.Value + bowTurn.Value + bowFade.Value + arrowScale.Value + trailScale.Value + trailTaper.Value + coreScale.Value + coreGlow.Value;
            if (float.IsNaN(lean)) throw new InvalidOperationException("a homing look number is not a number");
            return n + 1;
        }

        // ================================================================ the plain rings of H1 (the fallback)

        // One object holding the rings (pooled LineRenderers, one per lock and one for the filling), facing the camera; they hide
        // themselves when no frame came for a moment (the string was let go, the shot left).
        sealed class PlainRings : MonoBehaviour {
            static PlainRings? instance;
            readonly List<LineRenderer> rings = new List<LineRenderer>();
            LineRenderer? fillRing;
            float lastShown = -1f;

            internal static PlainRings Get() {
                if (instance == null) {
                    var go = new GameObject("ArcherySeekLook");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<PlainRings>();
                }
                return instance;
            }

            internal static void HideAll() { if (instance != null) instance.Hide(); }

            LineRenderer NewRing(float width) {
                var go = new GameObject("ring");
                go.transform.SetParent(transform, false);
                LineRenderer lr = Fx.NewLine(go, 2, width, false);
                Fx.BuildCircle(lr);
                return lr;
            }

            static void Place(LineRenderer lr, Vector3 at, float radius, float width, Color c) {
                Camera cam = Camera.main;
                Transform t = lr.transform;
                Vector3 look = cam != null ? at - cam.transform.position : Vector3.forward;
                t.SetPositionAndRotation(at, Quaternion.LookRotation(look.sqrMagnitude > 1e-6f ? look : Vector3.forward));
                t.localScale = Vector3.one * Mathf.Max(0.05f, radius);
                lr.widthMultiplier = width;
                lr.startColor = lr.endColor = c;
                if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
            }

            static readonly int T_Show = Timing.Section("look Seek.Look.Show (plain lock rings, the fallback)");
            internal void Show(List<Vector3> locks, List<float> radii, bool filling, Vector3 fillAt, float fillRadius, float fill) {
                using var _t = Timing.Begin(T_Show);
                if (Fx.LineMaterial() == null) return;
                Color c = Colour;
                while (rings.Count < locks.Count) rings.Add(NewRing(ringWidth.Value));
                for (int i = 0; i < rings.Count; i++) {
                    if (i < locks.Count) Place(rings[i], locks[i], radii[i] * 1.15f, ringWidth.Value, new Color(c.r, c.g, c.b, i == 0 ? 1f : 0.75f));
                    else if (rings[i].gameObject.activeSelf) rings[i].gameObject.SetActive(false);
                }
                if (filling) {
                    if (fillRing == null) fillRing = NewRing(fillWidth.Value);
                    float k = Mathf.Clamp01(fill);
                    Place(fillRing, fillAt, fillRadius * Mathf.Lerp(2.5f, 1.15f, k), fillWidth.Value, new Color(c.r, c.g, c.b, Mathf.Lerp(0.35f, 0.9f, k)));
                }
                else if (fillRing != null && fillRing.gameObject.activeSelf) fillRing.gameObject.SetActive(false);
                lastShown = Time.time;
            }

            void Hide() {
                foreach (LineRenderer r in rings) if (r != null && r.gameObject.activeSelf) r.gameObject.SetActive(false);
                if (fillRing != null && fillRing.gameObject.activeSelf) fillRing.gameObject.SetActive(false);
            }

            static readonly int TLateUpdate = Timing.Section("LateUpdate Seek.Look (stale plain rings)");
            void LateUpdate() {
                using var _t = Timing.Begin(TLateUpdate);
                if (lastShown >= 0f && Time.time - lastShown > 0.15f) { Hide(); lastShown = -1f; }
            }
        }
    }
}
