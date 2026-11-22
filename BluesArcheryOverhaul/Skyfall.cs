using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Alta.Impact;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Skyfall, the admin sky-rain skill (design docs/superpowers/specs/2026-11-19-skyfall-admin-skill-design.md): given and taken only
    // by the server console's `skyfall` command; it never touches the skill tree, the build block, any orb or a non-holder's shot.
    //   SkyfallRules.cs     Unity-free, tested: SkyfallValues, SkyCode, ShotPose, the trigger, the centre and its marks, SkyImpact, SkyStorm, SkyfallPattern, the grants, the signal, names, SkyfallCaught, ExplosionBudget, SkyDial
    //   Skyfall.cs          this hub: the [Skyfall] settings and the cached Values; the server's grants (file load/save) and festival flag; the client latch; per-arrow pose records and storms; the held real arrow (Skyfall.Held: hidden at release, destroyed by the server 1 s later); the seam entry points every other file calls
    //   SkyfallCommands.cs  the `skyfall` console module (status, give, give all, take; give refuses until SkyfallCommands.Ready), registered in OnInitializeMelon
    //   SkyfallLink.cs      the one message: the server's send queue (flushed from Main.OnUpdate); the client's latch (PlayerTextMessage.Serialize) and swallow (PlayerMessageDisplay.Display)
    //   SkyfallServer.cs    the server's storm: the impact queue, creature blasts, player kills, festival care
    //   PlayerCare.cs       Heal and Feed, shared by the play-test range and Skyfall
    //   SkyfallSigil.cs     client: rings, rune bands and glyph quads from the game's SpriteSheet_Combat, ruins glyphs and Lightbeam glyph (pooled)
    //   SkyfallHold.cs      client, caster only: the hold looks (rings climbing the arrow, faint sigil, ground glyph, hum, arm thump)
    //   SkyfallLook.cs      client: a storm's timeline (the streak, sigil unfold and its ground twin, rain arrows, explosions with budget and spacing, scorches, the fizzle puff)
    //   SkyfallNail.cs      client: the World-Nail (slide, impact, stand, crumble) and the sigil's shatter
    //   SkyfallSky.cs       client: the sky dial (DayTimeManager, driven by SkyDial), restored on every exit
    // Timing sections: "patch Type.Method / SkyfallX.Class", "Main.OnUpdate / SkyfallX.Tick (...)", "look SkyfallX.Method",
    // "hit SkyfallServer.X (...)", "file Skyfall.X", "scene Skyfall.X (...)", "asset SkyfallX.Find..." (the search only, not the cached return).
    internal static class Skyfall {
        static MelonPreferences_Entry<float> minStrength = null!, minElevation = null!, armSeconds = null!, sigilHeight = null!, minDistance = null!,
            maxDistance = null!, sigilRadius = null!, rainRadius = null!, buildSeconds = null!, waveGap = null!, domeRadius = null!, domeWarning = null!,
            minSpacing = null!, casterClearance = null!, arrowLength = null!, fallSeconds = null!, impactDamage = null!, impactCripple = null!,
            blastRadius = null!, nailLength = null!, nailGap = null!, nailSlide = null!, nailDome = null!, nailBlast = null!, nailDamage = null!,
            nailStand = null!, skyDim = null!, skySun = null!, skyFade = null!, skyFadeSeconds = null!, festivalCare = null!;
        static MelonPreferences_Entry<int> waves = null!, arrowsPerWave = null!, maxLiveExplosions = null!;
        static SkyfallValues? cache;
        static int entryCount;

        /// <summary>The [Skyfall] settings: one entry per SkyfallValues field (key = the field's name; [shared] unless the field is in
        /// SkyfallValues.LooksOnly). Before Cfg.Ready, which records, migrates and checksums only the
        /// categories that exist then.</summary>
        internal static void Init() {
            var cat = Cfg.Category("Skyfall");
            // first: a reload clears the cached Values before any look's reload handler (subscribed below) can read them
            Cfg.Reloaded += () => cache = null;
            SkyfallValues d = SkyfallValues.Default;
            MelonPreferences_Entry<float> F(string key, float value, string text) {
                var e = cat.CreateEntry(key, value, description: text);
                entryCount++;
                return Array.IndexOf(SkyfallValues.LooksOnly, key) < 0 ? Cfg.Shared(e) : e;
            }
            MelonPreferences_Entry<int> I(string key, int value, string text) {
                var e = cat.CreateEntry(key, value, description: text);
                entryCount++;
                return Array.IndexOf(SkyfallValues.LooksOnly, key) < 0 ? Cfg.Shared(e) : e;
            }
            minStrength = F(nameof(SkyfallValues.MinStrength), d.MinStrength, "The trigger: the bow string must be drawn at least this far (the game's shot strength, 0 to 1) for a Skyfall cast. Below it every shot is a normal shot.");
            minElevation = F(nameof(SkyfallValues.MinElevationDegrees), d.MinElevationDegrees, "The trigger: the arrow must point at least this many degrees above level. Below it every shot is a normal shot.");
            armSeconds = F(nameof(SkyfallValues.ArmSeconds), d.ArmSeconds, "Seconds the full draw at the sky must be held before a release casts Skyfall (the bow hand feels a thump when it is armed). The server checks the shot's time since the 68% crossing against it. A release before then is a normal shot.");
            sigilHeight = F(nameof(SkyfallValues.SigilHeight), d.SigilHeight, "The sigil (the storm's centre) is on a level plane this many metres above the release point, where the aim meets it.");
            minDistance = F(nameof(SkyfallValues.MinDistance), d.MinDistance, "The sigil is never nearer than this many metres from the archer along the ground (aim steeper: nearer, down to this).");
            maxDistance = F(nameof(SkyfallValues.MaxDistance), d.MaxDistance, "The sigil is never farther than this many metres from the archer along the ground (aim flatter: farther, up to this).");
            sigilRadius = F(nameof(SkyfallValues.SigilRadius), d.SigilRadius, "Clients: the sigil's radius in the sky, in metres (looks only).");
            rainRadius = F(nameof(SkyfallValues.RainRadius), d.RainRadius, "The giant arrows land within this many metres of the point under the sigil.");
            buildSeconds = F(nameof(SkyfallValues.BuildSeconds), d.BuildSeconds, "Seconds from the release to the first landing, while the sigil unfolds and its twin burns into the ground (everyone's moment to run).");
            waves = I(nameof(SkyfallValues.Waves), d.Waves, "How many waves of giant arrows fall before the World-Nail.");
            arrowsPerWave = I(nameof(SkyfallValues.ArrowsPerWave), d.ArrowsPerWave, "Giant arrows in each wave.");
            waveGap = F(nameof(SkyfallValues.WaveGapSeconds), d.WaveGapSeconds, "Seconds between one wave and the next.");
            domeRadius = F(nameof(SkyfallValues.DomeRadius), d.DomeRadius, "(unused in this build) Was each giant arrow's danger dome, in metres. The rain's domes were dropped: a landing kills everything within BlastRadius.");
            domeWarning = F(nameof(SkyfallValues.DomeWarningSeconds), d.DomeWarningSeconds, "The World-Nail's dome (its kill zone) shows at least this many seconds before the Nail lands; the first landing also comes at least this long after the release.");
            minSpacing = F(nameof(SkyfallValues.MinImpactSpacing), d.MinImpactSpacing, "Landing spots are at least this many metres apart (the game's explosion merges closer ones).");
            casterClearance = F(nameof(SkyfallValues.CasterClearance), d.CasterClearance, "The archer's safe circle, in metres round where they released: no player standing in it is killed, even inside a kill zone (creatures in it are not spared), and the giant arrows land at least BlastRadius plus this from it. The archer is never killed, wherever they stand.");
            arrowLength = F(nameof(SkyfallValues.ArrowLength), d.ArrowLength, "Clients: each giant arrow's length in metres (looks only).");
            fallSeconds = F(nameof(SkyfallValues.FallSeconds), d.FallSeconds, "Seconds a giant arrow takes to fall from the sigil to the ground.");
            impactDamage = F(nameof(SkyfallValues.ImpactDamage), d.ImpactDamage, "(unused in this build) Was a giant arrow's damage to creatures. Every landing now kills everything in its kill zone (BlastRadius).");
            impactCripple = F(nameof(SkyfallValues.ImpactCripple), d.ImpactCripple, "(unused in this build) Was a giant arrow's cripple (the game's knock-down damage) on creatures. Every landing now kills everything in its kill zone (BlastRadius).");
            blastRadius = F(nameof(SkyfallValues.BlastRadius), d.BlastRadius, "Each giant arrow's kill zone, in metres: when it lands, every creature and every player inside the half-sphere of this radius standing on the ground where it lands, with 1 m to spare over its surface and under the ground, is killed (players by the game's normal death, whatever the server's PvP setting), never the archer and never a player in the archer's safe circle (CasterClearance). Nothing outside it is touched.");
            nailLength = F(nameof(SkyfallValues.NailLength), d.NailLength, "Clients: the World-Nail's length in metres (looks only).");
            nailGap = F(nameof(SkyfallValues.NailGapSeconds), d.NailGapSeconds, "Seconds after the last wave before the World-Nail starts to fall.");
            nailSlide = F(nameof(SkyfallValues.NailSlideSeconds), d.NailSlideSeconds, "Seconds the World-Nail takes to slide down through the sigil into the ground.");
            nailDome = F(nameof(SkyfallValues.NailDomeRadius), d.NailDomeRadius, "The World-Nail's kill zone, in metres, shown as its danger dome: when it lands, everything inside the half-sphere of this radius on the ground under the sigil, with 1 m to spare over its surface and under the ground, is killed, by the same rules as BlastRadius. On a steep slope part of the downhill side lies under the half-sphere and lives.");
            nailBlast = F(nameof(SkyfallValues.NailBlastRadius), d.NailBlastRadius, "(unused in this build) Was how far the World-Nail's blast reached creatures, in metres. Its kill zone is NailDomeRadius.");
            nailDamage = F(nameof(SkyfallValues.NailDamage), d.NailDamage, "(unused in this build) Was the World-Nail's damage to creatures. Its landing now kills everything in its kill zone (NailDomeRadius).");
            nailStand = F(nameof(SkyfallValues.NailStandSeconds), d.NailStandSeconds, "Seconds the World-Nail stands before it crumbles. The storm runs until then: the archer's next sky shot is a normal shot until it has crumbled (one storm per archer).");
            maxLiveExplosions = I(nameof(SkyfallValues.MaxLiveExplosions), d.MaxLiveExplosions, "Clients: at most this many explosion effects play at once; beyond that a landing shows only its spark and sound.");
            skyDim = F(nameof(SkyfallValues.SkyDimMetres), d.SkyDimMetres, "Clients: the sky dims only for a player within this many metres of the sigil.");
            skySun = F(nameof(SkyfallValues.SkySun), d.SkySun, "Clients: the sun's strength while the sky is dimmed (1 = unchanged).");
            skyFade = F(nameof(SkyfallValues.SkyFade), d.SkyFade, "Clients: how far the sky fades while dimmed (0 = not at all; at most 0.95, above which the sky turns black).");
            skyFadeSeconds = F(nameof(SkyfallValues.SkyFadeSeconds), d.SkyFadeSeconds, "Clients: seconds the sky takes to dim, and to come back.");
            festivalCare = F(nameof(SkyfallValues.FestivalCareRadius), d.FestivalCareRadius, "(unused in this build: festival mode was dropped) Festival mode: players whose head is within this many metres of a landing are healed and fed.");
            SkyfallSigil.Init(cat); SkyfallHold.Init(cat); SkyfallSky.Init(cat); SkyfallLook.Init(cat); SkyfallNail.Init(cat); // Tasks 10-12: the looks' own entries (colours, sounds, sizes), per client, never [shared]
        }

        /// <summary>The live [Skyfall] numbers, built once per settings load and always the <see cref="SkyfallValues.Clamped"/> copy (the
        /// one tested place that holds them to safe ranges; SigilCentre, Aimed, ServerAccepts, QuantizeOffset and NailLandAt do not clamp).
        /// A leaf: it reads only its own entries.</summary>
        internal static SkyfallValues Values {
            get {
                if (minStrength == null) return SkyfallValues.Default.Clamped();
                if (cache != null) return cache;
                return cache = new SkyfallValues {
                    MinStrength = minStrength.Value, MinElevationDegrees = minElevation.Value, ArmSeconds = armSeconds.Value,
                    SigilHeight = sigilHeight.Value, MinDistance = minDistance.Value, MaxDistance = maxDistance.Value, SigilRadius = sigilRadius.Value,
                    RainRadius = rainRadius.Value,
                    BuildSeconds = buildSeconds.Value, Waves = waves.Value, ArrowsPerWave = arrowsPerWave.Value, WaveGapSeconds = waveGap.Value,
                    DomeRadius = domeRadius.Value, DomeWarningSeconds = domeWarning.Value, MinImpactSpacing = minSpacing.Value,
                    CasterClearance = casterClearance.Value, ArrowLength = arrowLength.Value, FallSeconds = fallSeconds.Value,
                    ImpactDamage = impactDamage.Value, ImpactCripple = impactCripple.Value, BlastRadius = blastRadius.Value,
                    NailLength = nailLength.Value, NailGapSeconds = nailGap.Value, NailSlideSeconds = nailSlide.Value, NailDomeRadius = nailDome.Value,
                    NailBlastRadius = nailBlast.Value, NailDamage = nailDamage.Value, NailStandSeconds = nailStand.Value,
                    MaxLiveExplosions = maxLiveExplosions.Value,
                    SkyDimMetres = skyDim.Value, SkySun = skySun.Value, SkyFade = skyFade.Value, SkyFadeSeconds = skyFadeSeconds.Value,
                    FestivalCareRadius = festivalCare.Value,
                }.Clamped();
            }
        }

        /// <summary>[Kit] SkyfallSpectatorTest on this machine: a play-test kit setting (KitHooks; off without the kit).</summary>
        internal static bool SpectatorTest => KitHooks.SpectatorTest;

        // ---------------------------------------------------------------- who holds it (server) and what this game was told (client)

        /// <summary>Clients: the latch, what the server's last quiet signal said (holds, festival mode).
        /// Written only by SkyfallLink: set by the message, cleared when this game leaves the server. <see cref="LocalTakenAt"/> is
        /// <c>Time.time</c> when a "does not hold" signal followed a "holds" one (the caster's storm fizzles on a take), -1 when none.</summary>
        internal static bool LocalHolds, LocalFestival;
        internal static float LocalTakenAt = -1f;

        /// <summary>Server: festival mode for storms cast from now on. Memory only; starts off. Dormant: the <c>skyfall festival</c>
        /// command was removed (Task 12b, the owner dropped festival mode), so nothing sets it.</summary>
        internal static bool Festival { get; set; }

        /// <summary>Server: UserData\BluesArcheryOverhaul\skyfall-grants-v2.txt (SkyfallConsole.GrantFile; 0.17.16 to 0.18.0 read
        /// skyfall-grants.txt, which this build carries over once and moves aside, so no earlier build reads a grant again).</summary>
        internal static string GrantsFile => Path.Combine(Cfg.Folder, SkyfallConsole.GrantFile);

        static readonly int TLoadGrants = Timing.Section("file Skyfall.LoadGrants"), TWriteGrants = Timing.Section("file Skyfall.WriteGrants"),
            TRetireGrants = Timing.Section("file Skyfall.RetireOldGrants");
        // The server's grant files in UserData\BluesArcheryOverhaul, for SkyfallGrantStore (which decides; tested). UTF-8 (WriteWhole writes
        // without a byte-order mark; File.ReadAllText honours one a hand edit adds), so any player name survives. A file that exists
        // but cannot be read (an offline OneDrive placeholder) throws, and the store never takes that for "nobody".
        sealed class GrantFiles : SkyfallGrantStore.IFiles {
            static string PathOf(string name) => Path.Combine(Cfg.Folder, name);
            public string? Read(string name) {
                using var _t = Timing.Begin(TLoadGrants);
                string f = PathOf(name);
                return File.Exists(f) ? File.ReadAllText(f) : null;
            }
            public void Write(string name, string text) {
                using var _t = Timing.Begin(TWriteGrants);
                Cfg.WriteWhole(PathOf(name), text);
            }
            public bool Retire(string name) {
                using var _t = Timing.Begin(TRetireGrants);
                string f = PathOf(name), to = f + ".retired";
                if (!File.Exists(f)) return false;
                if (File.Exists(to)) File.Delete(to);
                File.Move(f, to);
                return true;
            }
        }
        static readonly SkyfallGrantStore grantStore = new SkyfallGrantStore(new GrantFiles());

        /// <summary>Server: who holds Skyfall. Read from <see cref="GrantsFile"/> at the first use (the server's first SkyfallLink tick),
        /// then kept in memory; the console's give and take change it and rewrite the file (<see cref="SaveGrants"/>). A file that cannot
        /// be read holds nobody for now, is read again every 10 s and is never written over meanwhile (SkyfallGrantStore). A hand edit
        /// while the server runs is overwritten by the next give or take. Clients never read it.</summary>
        internal static SkyfallGrants Grants {
            get {
                SkyfallGrants g = grantStore.Table(Time.unscaledTime, out GrantLoad what);
                if (what != GrantLoad.Cached) SayLoad(what, g);
                return g;
            }
        }

        // one line per load that read something (Summary), a warning for a file that cannot be read or a problem
        static void SayLoad(GrantLoad what, SkyfallGrants g) {
            string file = GrantsFile, old = Path.Combine(Cfg.Folder, SkyfallConsole.OldGrantFile);
            string Names() {
                var all = g.All;
                var names = new string[all.Count];
                for (int i = 0; i < names.Length; i++) names[i] = $"{all[i].Value} ({all[i].Key})";
                return $"{all.Count} grant(s){(all.Count > 0 ? ": " + string.Join(", ", names) : "")}";
            }
            switch (what) {
                case GrantLoad.NoFile:
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: no grant file yet ({file}): nobody holds Skyfall");
                    break;
                case GrantLoad.Loaded:
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: {Names()} loaded from {file}");
                    break;
                case GrantLoad.Migrated:
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: {Names()} carried over from {old} to {file}");
                    break;
                case GrantLoad.Unreadable:
                    MelonLogger.Warning($"[Archery] Skyfall: cannot read {file} ({grantStore.ReadError}); nobody holds Skyfall until it can be read. It is read again every {SkyfallGrantStore.RetrySeconds:0} s, and no give or take writes over it meanwhile");
                    break;
                case GrantLoad.StillUnreadable:
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: {file} still cannot be read ({grantStore.ReadError})");
                    break;
                case GrantLoad.Recovered:
                    MelonLogger.Warning($"[Archery] Skyfall: {file} could be read again: {Names()} (the file's, and the ones given meanwhile)");
                    break;
            }
            if (grantStore.RetiredOld && Logs.Summary)
                MelonLogger.Msg($"[Archery] Skyfall: the old grant file was moved aside to {old}.retired, so no earlier build (0.17.16 to 0.18.0) reads a grant of this one");
            if (grantStore.Problem != null) MelonLogger.Warning($"[Archery] Skyfall: grant file: {grantStore.Problem}");
        }

        /// <summary>Server: whether this account holds Skyfall (never account 0). Called per Skyfall release and per signal, never per frame.</summary>
        internal static bool Holds(int account) {
            if (account == 0) return false;
            foreach (var kv in Grants.All) if (kv.Key == account) return true;   // a handful of grants
            return false;
        }

        static readonly int TSaveGrants = Timing.Section("file Skyfall.SaveGrants");
        /// <summary>Server: rewrites <see cref="GrantsFile"/> whole (Cfg.WriteWhole: beside it, then moved over it). Null when
        /// written, else why not, already warned. Refused, writing nothing, while the file cannot be read (SkyfallGrantStore.Save).</summary>
        internal static string? SaveGrants() {
            using var _t = Timing.Begin(TSaveGrants);
            try {
                _ = Grants;   // read first: a save never writes over grants it has not read
                string? error = grantStore.Save();
                if (error != null) MelonLogger.Warning($"[Archery] Skyfall: {GrantsFile} not written: {error}");
                return error;
            }
            catch (Exception e) {
                MelonLogger.Warning($"[Archery] Skyfall: cannot write {GrantsFile}: {e.Message}");
                return e.Message;
            }
        }

        /// <summary>SelfCheck (a leaf: Values and the Unity-free pattern only; never writes anything): every SkyfallValues field has its
        /// entry, and the pattern with the settings just loaded ends with the World-Nail and a finite end. Returns the impact count.</summary>
        internal static int Check() {
            if (minStrength == null) return 0;
            int fields = typeof(SkyfallValues).GetFields(BindingFlags.Public | BindingFlags.Instance).Length;
            if (fields != entryCount) throw new InvalidOperationException($"[Skyfall] has {entryCount} number settings for {fields} SkyfallValues fields");
            SkyImpact[] plan = SkyfallPattern.Plan(1u, 0, 30, 0, 0, Values);
            float end = SkyfallPattern.EndsAt(Values);
            if (plan.Length == 0 || !plan[plan.Length - 1].Nail || !(end > 0f && end < float.PositiveInfinity))
                throw new InvalidOperationException("the Skyfall pattern came out empty, without the World-Nail last, or with no end");
            return plan.Length;
        }

        // ---------------------------------------------------------------- the ground under a spot (every machine)

        // Default, Terrain, Ground, GroundOverlay, Wall, TerrainWallOnly: the play-test range's ground (BluesArcheryTestKit's TestRangeBuild.cs), whose WorldProbe is private
        internal const int GroundMask = (1 << 0) | (1 << 8) | (1 << 10) | (1 << 11) | (1 << 13) | (1 << 19);
        // every ground search (server landings, client domes, the hold's glyph, the Nail) reaches this far below the release (ruling 8)
        internal const float GroundBelowMetres = 80f;
        static readonly RaycastHit[] groundHits = new RaycastHit[48];
        static readonly GroundHit[] groundFacts = new GroundHit[48];
        static bool groundFullWarned;

        static readonly int TGround = Timing.Section("scene Skyfall.GroundAt (ground cast)");
        /// <summary>The ground at (<paramref name="x"/>, <paramref name="z"/>) between <paramref name="top"/> and <paramref name="bottom"/>:
        /// one cast straight down on <see cref="GroundMask"/> (no triggers), the highest hit that is neither a creature's or a player's
        /// body (<see cref="IsBody"/>, kinematic rigidbody or not: a creature's solid colliders are on Default under kinematic
        /// rigidbodies) nor a loose item (a non-kinematic Rigidbody); the choice is <see cref="SkyGround.Highest"/> (tested). True only
        /// when such a hit was found and its own chunk is loaded (Hits.Loaded at the hit, so a far landmark copy never counts);
        /// otherwise false with <paramref name="y"/> NaN.</summary>
        internal static bool GroundAt(double x, double z, double top, double bottom, out float y) {
            using var _t = Timing.Begin(TGround);
            y = float.NaN;
            float fx = (float)x, fz = (float)z, ft = (float)top, length = (float)(top - bottom);
            if (float.IsNaN(fx) || float.IsInfinity(fx) || float.IsNaN(fz) || float.IsInfinity(fz) || float.IsNaN(ft) || float.IsInfinity(ft)
                || !(length > 0f) || float.IsInfinity(length)) return false;
            int n = Physics.RaycastNonAlloc(new Vector3(fx, ft, fz), Vector3.down, groundHits, length, GroundMask, QueryTriggerInteraction.Ignore);
            if (n == groundHits.Length && !groundFullWarned) {
                groundFullWarned = true;
                MelonLogger.Warning(Inv($"[Archery] Skyfall: a ground cast at ({x:0.0}, {z:0.0}) filled its {groundHits.Length} hits (said once); the highest of those is taken"));
            }
            for (int i = 0; i < n; i++) {
                Collider col = groundHits[i].collider;
                if (col == null) { groundFacts[i] = new GroundHit { Height = float.NaN }; continue; }   // gone: never the ground
                Rigidbody rb = col.attachedRigidbody;
                groundFacts[i] = new GroundHit { Height = groundHits[i].point.y, Body = IsBody(col), Loose = rb != null && !rb.isKinematic };
            }
            float best = SkyGround.Highest(groundFacts, n);
            if (float.IsNaN(best) || !Hits.Loaded(new Vector3(fx, best, fz))) return false;
            y = best;
            return true;
        }

        /// <summary>The collider belongs to a creature's or a player's body: an ImpactDetector on it or above it whose HealthTarget is a
        /// creature (Targets.IsCreature, the test SkyfallServer.Landing strikes by) or a player. Anything else with health (a Rock)
        /// stays ground.</summary>
        static bool IsBody(Collider col) {
            ImpactDetector det = col.GetComponentInParent<ImpactDetector>();
            HealthObject? h = det != null ? det.HealthTarget : null;
            return h != null && (Targets.IsCreature(h) || Targets.IsPlayer(h));
        }

        // ================================================================ the seam (Task 8): one release through Skyfall
        // Every rule is SkyfallSeam's (Unity-free, tested); this is where it meets the game. A release that is not a Skyfall release
        // meets one dictionary lookup here (IsCast, TryPose, the Decide* calls) and the pose record, which nothing reads unless the
        // arrow is a Skyfall release. Shooter: Claims.Shot -> Ask (a candidate), Claims.Bits Write -> RecordPose (the wire pose),
        // Claims.Write -> Finalise (the request, or nothing), Draws.Release -> DecideShooter, Volley.Fire -> Fire. Server: Claims.Bits
        // Read -> RecordPose, Draws.Release -> DecideServer (which puts the relay: Claims.PutSkyfall), Volley.Fire -> Fire, Held.Tick
        // destroys the arrow 1 s later. Spectator: Claims.Bits Read -> RecordPose, Draws.Release -> DecideSeen, Volley.Fire -> Fire.

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- the release as it went on the wire (every machine)

        struct PoseEntry { public ShotPose Pose; public float At; }
        const int PoseCap = 64;
        const float PoseLifetime = 2f;   // as ClaimStore's: read in the same Shoot, never later
        static readonly Dictionary<uint, PoseEntry> poses = new Dictionary<uint, PoseEntry>();
        static readonly List<uint> poseScratch = new List<uint>();

        /// <summary>Every machine: the release as it went on the wire. On the shooter's write the rotation is already the compressed
        /// one (the ShootArguments constructor), the position and strength raw; on a read, the decoded values.</summary>
        internal static ShotPose PoseOf(in ShootArguments a) =>
            new ShotPose { Pos = Seek.H(a.position), Dir = Seek.H(a.rotation * Vector3.forward), Strength = a.strength };

        /// <summary>Every machine, Claims.Bits (before the claim is written or read): this arrow's wire pose. 64 at most; entries older
        /// than 2 s are ignored and pruned when full (the ClaimStore pattern).</summary>
        internal static void RecordPose(uint arrowId, ShotPose pose) {
            float now = Time.time;
            if (poses.Count >= PoseCap && !poses.ContainsKey(arrowId)) {
                poseScratch.Clear();
                foreach (var kv in poses) if (now - kv.Value.At > PoseLifetime) poseScratch.Add(kv.Key);
                foreach (uint k in poseScratch) poses.Remove(k);
                if (poses.Count >= PoseCap) poses.Clear();
            }
            poses[arrowId] = new PoseEntry { Pose = pose, At = now };
        }

        /// <summary>This arrow's wire pose, if recorded in the last 2 s. Never removes it (the shooter's message may be written twice).</summary>
        internal static bool TryPose(uint arrowId, out ShotPose pose) {
            pose = default;
            if (poses.Count == 0 || !poses.TryGetValue(arrowId, out PoseEntry e) || Time.time - e.At > PoseLifetime) return false;
            pose = e.Pose;
            return true;
        }

        /// <summary>Clients: this game's account (Player.Current's UserInfo.Identifier); 0 when unknown.</summary>
        internal static int OwnAccount {
            get {
#if QUEST
                IPlayer? me = Il2Cpp.Player.Current;
#else
                IPlayer? me = global::Player.Current;
#endif
                if (me is UnityEngine.Object o && o == null) return 0;   // a destroyed player counts as none
                return me?.UserInfo?.Identifier ?? 0;
            }
        }

        // ---------------------------------------------------------------- the shooter: a candidate at Shoot, the request on the wire

        const int AskCap = 16;
        // Every per-arrow record below carries the time it was made and is read only while SkyfallSeam.Fresh (2 s, every reader runs in
        // the release's own frame): arrow ids are reused (EntityManager's fixed id pool), so a record of an older shot must never meet a
        // later shot of the same id (final review, the Task 8 minor and its shooter twin).
        struct AskEntry { public int? Ms; public float At; }   // the draw's ms since the 68% crossing, and when it was asked
        static readonly Dictionary<uint, AskEntry> asked = new Dictionary<uint, AskEntry>();
        sealed class Request { public ShotPose Pose; public List<uint> Marks = new List<uint>(); public ShotClaim Claim = null!; public float At; }
        static readonly Dictionary<uint, Request> requested = new Dictionary<uint, Request>();

        static readonly int TAsk = Timing.Section("Skyfall.Ask (shooter)");
        /// <summary>Shooter, Claims.Shot (after its claim is built and stored, exactly as for any shot): this release may be a cast
        /// when this game holds Skyfall, no storm of its own is running and the draw has lasted ArmSeconds. The wire pose decides
        /// (Finalise). Any older candidate or request of this arrow id is dropped first; then it returns at once for everyone else.</summary>
        internal static void Ask(uint arrowId, int? elapsedMs) {
            using var _t = Timing.Begin(TAsk);
            if (NetworkSceneManager.IsServer) return;
            if (asked.Count > 0) asked.Remove(arrowId);           // first: an older shot's candidate or request never rides this one
            if (requested.Count > 0) requested.Remove(arrowId);
            if (!LocalHolds) return;
            try {
                bool busy = CasterBusy(OwnAccount, Time.time);
                if (!SkyfallSeam.ShooterAsks(true, busy, elapsedMs, Values)) return;
                if (asked.Count >= AskCap) asked.Clear();
                asked[arrowId] = new AskEntry { Ms = elapsedMs, At = Time.time };
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {arrowId} may cast (drawn {Ms(elapsedMs)} ms); the wire pose decides");
            }
            catch (Exception e) { asked.Remove(arrowId); MelonLogger.Warning($"[Archery] Skyfall: arrow {arrowId}'s cast check failed; a normal shot: {e.Message}"); }
        }

        static readonly int TFinalise = Timing.Section("Skyfall.Finalise (shooter, the wire pose)");
        /// <summary>Shooter, Claims.Write (the claim about to go on the wire, after RecordPose): a candidate whose wire pose is armed
        /// becomes a request, in place (SkyfallSeam.Finalise); otherwise the claim goes out exactly as built. Once per arrow: the
        /// message's second serialisation finds no candidate and writes the same bits.</summary>
        internal static void Finalise(uint arrowId, ShotClaim claim) {
            using var _t = Timing.Begin(TFinalise);
            if (asked.Count == 0 || !asked.TryGetValue(arrowId, out AskEntry entry)) return;
            asked.Remove(arrowId);
            if (!SkyfallSeam.Fresh(entry.At, Time.time)) {
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {arrowId}'s candidate is from an older shot; not a cast");
                return;
            }
            int? elapsedMs = entry.Ms;
            if (!TryPose(arrowId, out ShotPose wire)) {
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {arrowId} no wire pose; not a cast");
                return;
            }
            List<uint> locks = LockClaim.Of(claim);   // before the claim changes: Seek kept these cages for a flight that will not come
            SkyfallValues v = Values;
            if (!SkyfallSeam.Finalise(claim, wire, elapsedMs, v)) {
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: arrow {arrowId} not a cast: the release went out at {SkyfallRules.ElevationDegrees(wire.Dir):0.0} deg, strength {wire.Strength:0.000} (needs {v.MinElevationDegrees:0.#} deg and {v.MinStrength:0.00})"));
                return;
            }
            if (requested.Count >= AskCap) requested.Clear();
            requested[arrowId] = new Request { Pose = wire, Marks = new List<uint>(claim.Marks), Claim = claim.Copy(), At = Time.time };
            if (!ApplicationManager.IsHeadless)
                foreach (uint lockId in locks) {
                    try { LockCage.Let(lockId); }
                    catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: arrow {arrowId}'s lock cage on {lockId} could not be let go: {e.Message}"); }
                }
            if (Logs.Summary) {
                SkyfallSeam.TryCentre(wire.Pos, claim.Marks, out HVec centre);
                MelonLogger.Msg(Inv($"[Archery] Skyfall: arrow {arrowId} asks to cast: elevation {SkyfallRules.ElevationDegrees(wire.Dir):0.0} deg, strength {wire.Strength:0.000}, centre {centre}{(locks.Count > 0 ? $", {locks.Count} lock cage(s) let go" : "")}"));
            }
        }

        // ---------------------------------------------------------------- the decisions (Draws.Release, first in each branch)

        sealed class Decided { public SkyCode Code; public SkyStorm Storm = null!; public bool Caster; public float At; }
        const int DecidedCap = 64;
        static readonly Dictionary<uint, Decided> decided = new Dictionary<uint, Decided>();

        static void Record(uint arrowId, SkyCode code, SkyStorm storm, bool caster) {
            if (decided.Count >= DecidedCap && !decided.ContainsKey(arrowId)) decided.Clear();
            decided[arrowId] = new Decided { Code = code, Storm = storm, Caster = caster, At = Time.time };
        }

        // this arrow's decision, only while it is fresh (made in this release's Draws.Release; never an older shot's of a reused id)
        static bool TryDecided(uint arrowId, out Decided d) {
            d = null!;
            return decided.Count > 0 && decided.TryGetValue(arrowId, out d) && SkyfallSeam.Fresh(d.At, Time.time);
        }

        /// <summary>This machine treats the arrow as a Skyfall release (a cast or a fizzle): decided in this release's Draws.Release
        /// (a decision older than SkyfallSeam.RecordSeconds is an older shot's: a recycled arrow id never replays a storm).</summary>
        internal static bool IsCast(uint arrowId) => TryDecided(arrowId, out Decided d) && d.Code != SkyCode.None;

        static string Ms(int? ms) => ms.HasValue ? ms.Value.ToString(CultureInfo.InvariantCulture) : "?";

        /// <summary>Server, Draws.Release: the shooter's raw request (Claims.ClaimedSky) decided by SkyfallSeam.Server with the grant,
        /// the one-storm rule, the wire pose and both times; the storm recorded; the relay put last (Claims.PutSkyfall), so a throw
        /// anywhere before it leaves a plain shot everywhere (PutResolved relays it). No request: a plain shot, the record cleared.</summary>
        internal static SkyCode DecideServer(NetworkEntity arrow, Player? owner, int? effectiveMs, int? claimedMs) {
            uint id = 0u;
            try {
                id = arrow.Identifier;
                if (Claims.ClaimedSky(id, out List<uint>? marks) == SkyCode.None) { decided.Remove(id); return SkyCode.None; }
                int account = owner?.UserInfo?.Identifier ?? 0;   // as Builds.KeyOf keys accounts; 0 (unknown) is never granted
                float now = Time.time;
                SkyfallValues v = Values;
                bool hasPose = TryPose(id, out ShotPose p);
                SkyVerdict verdict = SkyfallSeam.Server(true, account != 0 && Holds(account), CasterBusy(account, now), hasPose ? p : (ShotPose?)null,
                                                        claimedMs, effectiveMs, marks, Festival, v);
                HVec release = hasPose ? p.Pos : SkyfallSeam.Unknown;
                SkyfallSeam.TryCentre(release, verdict.Marks, out HVec centre);
                SkyStorm storm = SkyfallSeam.Storm(id, verdict.Code, release, centre, account, now, v);
                Record(id, verdict.Code, storm, false);
                if (Logs.Summary) {
                    string e = hasPose ? SkyfallRules.ElevationDegrees(p.Dir).ToString("0.0", CultureInfo.InvariantCulture) : "?";
                    string s = hasPose ? p.Strength.ToString("0.000", CultureInfo.InvariantCulture) : "?";
                    MelonLogger.Msg($"[Archery] Skyfall: arrow {id} '{Builds.NameOf(owner)}' {verdict.Code} ({verdict.Why}): claimed {Ms(claimedMs)} ms, decided with {Ms(effectiveMs)} ms, " +
                                    $"elevation {e} deg, strength {s}, centre {centre}{(Festival ? ", festival" : "")}");
                }
                Claims.PutSkyfall(id, verdict, effectiveMs);   // last: the relay goes out only once everything else has worked
                return verdict.Code;
            }
            catch (Exception e) { decided.Remove(id); MelonLogger.Warning($"[Archery] Skyfall: deciding arrow {id} failed; a plain shot: {e.Message}"); return SkyCode.None; }
        }

        /// <summary>Shooter, Draws.Release: the request this game put on the wire (Finalise) is its cast, Festival in festival mode,
        /// over its own wire pose and marks. With [Kit] SkyfallSpectatorTest the same release also runs the spectator's path (what the
        /// relay would say), both storms are logged, and this game draws the spectator's; IsCast and the hold still follow the request.
        /// No request: a plain shot, the record cleared.</summary>
        internal static SkyCode DecideShooter(NetworkEntity arrow) {
            uint id = 0u;
            try {
                id = arrow.Identifier;
                if (requested.Count == 0 || !requested.TryGetValue(id, out Request q)) { decided.Remove(id); return SkyCode.None; }
                requested.Remove(id);
                if (!SkyfallSeam.Fresh(q.At, Time.time)) {   // an older shot's request of a reused arrow id: a plain shot
                    decided.Remove(id);
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {id}'s request is from an older shot; a plain shot");
                    return SkyCode.None;
                }
                SkyCode code = LocalFestival ? SkyCode.Festival : SkyCode.Skyfall;
                int own = OwnAccount;
                float now = Time.time;
                SkyfallValues v = Values;
                SkyfallSeam.TryCentre(q.Pose.Pos, q.Marks, out HVec centre);
                SkyStorm storm = SkyfallSeam.Storm(id, code, q.Pose.Pos, centre, own, now, v);
                Record(id, code, storm, true);
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: arrow {id} cast from this game ({code}): {storm.Impacts.Length} impacts, centre {centre}");
                if (SpectatorTest) {
                    ShotClaim relay = SkyfallSeam.Relay(q.Claim, new SkyVerdict { Code = code, Why = SkyWhy.Cast, Marks = q.Marks }, q.Claim.TimeMs);
                    SkyCode seen = SkyfallSeam.Seen(relay, q.Pose.Pos, out HVec c2);
                    SkyStorm s2 = SkyfallSeam.Storm(id, seen, q.Pose.Pos, c2, own, now, v);
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: spectator test arrow {id}: {(SkyfallSeam.SamePattern(storm, s2, 0) ? "same" : "DIFFERENT")}: " +
                                                      $"caster {SkyfallSeam.Describe(storm)} | spectator {SkyfallSeam.Describe(s2)}");
                    Record(id, code, s2, false);   // this one client draws the spectator path; the code stays the request's
                }
                return code;
            }
            catch (Exception e) { decided.Remove(id); MelonLogger.Warning($"[Archery] Skyfall: deciding arrow {id} failed; a plain shot: {e.Message}"); return SkyCode.None; }
        }

        /// <summary>Spectator, Draws.Release: what the server's relay says (SkyfallSeam.Seen) over this machine's own wire pose. No
        /// relay, or one with no code: a plain shot, the record cleared.</summary>
        internal static SkyCode DecideSeen(NetworkEntity arrow, ShotClaim? relay, Player? shooter) {
            uint id = 0u;
            try {
                id = arrow.Identifier;
                if (relay == null || relay.Sky == SkyCode.None) { decided.Remove(id); return SkyCode.None; }
                HVec release = TryPose(id, out ShotPose p) ? p.Pos : SkyfallSeam.Unknown;
                SkyCode code = SkyfallSeam.Seen(relay, release, out HVec centre);
                SkyStorm storm = SkyfallSeam.Storm(id, code, release, centre, shooter?.UserInfo?.Identifier ?? 0, Time.time, Values);
                Record(id, code, storm, false);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {id} '{Builds.NameOf(shooter)}' seen {code}: {storm.Impacts.Length} impacts, centre {centre}");
                return code;
            }
            catch (Exception e) { decided.Remove(id); MelonLogger.Warning($"[Archery] Skyfall: deciding arrow {id} failed; a plain shot: {e.Message}"); return SkyCode.None; }
        }

        // ---------------------------------------------------------------- the release (Volley.Fire's branch) and the storm records

        static readonly int TFire = Timing.Section("Skyfall.Fire");
        /// <summary>Every machine, Volley.Fire, for a Skyfall release (cast or fizzle) in place of the launch: the extras first (Holy
        /// Arrow's beam, spawned when it armed, must not be shot by ShootExtraArrows' own body after this prefix), then the real
        /// arrow's hold (Held), then Begin. Each step on its own, so a failure in one never skips the next. No mesh is copied.</summary>
        internal static void Fire(BowString bow, NetworkEntity arrow) {
            using var _t = Timing.Begin(TFire);
            if (arrow == null) return;
            uint id = arrow.Identifier;
            bool server = NetworkSceneManager.IsServer;
            try {
                ExtraArrowUpdater? eau = bow != null ? bow.ExtraArrowUpdater : null;
                if (server) {
                    if (bow != null) HolyArrow.ForgetServer(bow);   // UnarmedShot then skips LightBeamSkill.OnArrowShot
                    if (eau != null && eau.SpawnedExtraArrows.Count > 0) eau.DestroyAllExtraArrows();   // only when there is one: it also sends the game's stopAligningArrows
                }
                else if (eau != null) eau.SpawnedExtraArrows.Clear();   // never DestroyAllExtraArrows on a client (the game only logs there)
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: arrow {id}'s extra arrows could not be dropped: {e.Message}"); }
            try {
                Arrow a = arrow.GetComponent<Arrow>();
                if (a == null) MelonLogger.Warning($"[Archery] Skyfall: arrow {id} has no Arrow component; it is not held");
                else Held.Hold(a, id);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: arrow {id}'s real arrow could not be held: {e.Message}"); }
            bool caster = false;
            try {
                if (TryDecided(id, out Decided d)) { caster = d.Caster; Begin(d.Storm, d.Caster); }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: arrow {id}'s storm could not begin: {e.Message}"); }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {id} held ({(server ? "server" : caster ? "own" : "seen")})");
        }

        sealed class StormRecord { public SkyStorm Storm = null!; public float BusyUntil; }
        static readonly Dictionary<uint, StormRecord> storms = new Dictionary<uint, StormRecord>();
        static readonly List<uint> stormScratch = new List<uint>();

        static readonly int TBegin = Timing.Section("Skyfall.Begin");
        /// <summary>Every machine, from Fire: the storm is recorded (its busy time worked out once here, SkyfallSeam.BusyUntil) and
        /// handed to the server's damage and the clients' looks. Called for fizzles too: no impact, and a NaN centre when unknown.</summary>
        internal static void Begin(SkyStorm storm, bool caster) {
            using var _t = Timing.Begin(TBegin);
            if (storm == null) return;
            storms[storm.ArrowId] = new StormRecord { Storm = storm, BusyUntil = SkyfallSeam.BusyUntil(storm.StartTime, Values, NetworkSceneManager.IsServer) };
            try { if (NetworkSceneManager.IsServer && (storm.Code == SkyCode.Skyfall || storm.Code == SkyCode.Festival)) SkyfallServer.Start(storm); } // the server's landings; a fizzle has none (Skyfall.Held destroys its arrow)
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: server storm start failed: {e.Message}"); }
            try { if (!NetworkSceneManager.IsServer && !ApplicationManager.IsHeadless) SkyfallLook.Start(storm, caster); } // fizzles too: the puff
            catch (Exception e) { MelonLogger.Error($"[Archery] Skyfall: storm look start failed: {e.Message}"); }
        }

        /// <summary>One storm per caster: a recorded Skyfall or Festival storm of this account still runs (until its BusyUntil).</summary>
        internal static bool CasterBusy(int account, float now) {
            if (storms.Count == 0) return false;
            foreach (StormRecord r in storms.Values)
                if (r.Storm.CasterAccount == account && (r.Storm.Code == SkyCode.Skyfall || r.Storm.Code == SkyCode.Festival) && now < r.BusyUntil) return true;
            return false;
        }

        /// <summary>Main.OnUpdate, every machine, before the headless return: the held real arrows (Held.Tick), and each storm record
        /// dropped a second after its busy time. One count test while there is nothing.</summary>
        internal static void Tick() {
            if (Held.Count == 0 && storms.Count == 0) return;
            Held.Tick();
            if (storms.Count == 0) return;
            float now = Time.time;
            stormScratch.Clear();
            foreach (var kv in storms) if (now > kv.Value.BusyUntil + 1f) stormScratch.Add(kv.Key);
            foreach (uint k in stormScratch) storms.Remove(k);
        }

        // ---------------------------------------------------------------- the real arrow: held, never flown, destroyed by the server

        /// <summary>A Skyfall release's real arrow (cast or fizzle) is held where it left the bow on every machine: stopped, kinematic
        /// (Discrete first), its colliders off and (not headless) hidden. The server destroys it DestroySeconds after the release; a
        /// client lets go of one still there ClientHoldSeconds after (the server's destroy never came). Modelled on Seek's hold, but its
        /// own (not tied to [SeekingVolley] MaxLifeSeconds): the arrow is used up, never placed. The server's flight sync, which can
        /// reach a client after its release, is refused for a held arrow (HeldFlight). What a hold changes and how it is turned back
        /// (Stop, colliders, renderers, Free) is HeldArrow's, shared with Seek's hold.</summary>
        internal static class Held {
            sealed class Entry : HeldArrow {
                protected override string Who => "Skyfall";
                /// <summary>Server: the destroys tried so far (SkyfallSeam.DestroyNext).</summary>
                public int Tries;
            }
#if QUEST
            static readonly Dictionary<Arrow, Entry> holds = new Dictionary<Arrow, Entry>(Il.ByPointer<Arrow>());
#else
            static readonly Dictionary<Arrow, Entry> holds = new Dictionary<Arrow, Entry>();
#endif
            static readonly List<Arrow> scratch = new List<Arrow>();
            static bool keptWarned;   // the "could not be destroyed" warning, once a session

            internal static int Count => holds.Count;

            static readonly int THold = Timing.Section("Skyfall.Held.Hold (the real arrow held)");
            internal static void Hold(Arrow arrow, uint release) {
                using var _t = Timing.Begin(THold, release);
                if (arrow == null) return;
                // recorded before anything changes, a throw part-way frees what was changed (HeldArrow.Take, as Seek.HoldReal)
                HeldArrow.Take(holds, arrow, new Entry { Release = release, Until = Time.time + (NetworkSceneManager.IsServer ? SkyfallSeam.DestroySeconds : SkyfallSeam.ClientHoldSeconds) });
            }

            /// <summary>From Skyfall.Tick. A destroyed arrow is forgotten; at its time the server destroys the arrow, a client lets it go.
            /// Task 12d: the server's destroy checks itself. The game's destroy marks the entity at once (ServerSpawnManager.Destroy ->
            /// DestroyLocal sets IsBeingDestroyed), so an unmarked entity after SceneDestroy is a destroy another mod's prefix skipped
            /// (RevampedSkills' Holy Arrow keep, its latch stuck on this arrow). It stays held and is tried again on each of the next
            /// ticks (SkyfallSeam.DestroyNext); one that still stands is let go as a normal arrow (Free: colliders on, shown, its body
            /// back so it falls), never left frozen and colliderless.</summary>
            internal static void Tick() {
                if (holds.Count == 0) return;
                float now = Time.time;
                bool server = NetworkSceneManager.IsServer;
                scratch.Clear();
                foreach (var kv in holds) scratch.Add(kv.Key);
                foreach (Arrow a in scratch) {
                    if (!holds.TryGetValue(a, out Entry e)) continue;
                    if (a == null) { holds.Remove(a!); e.Free(null, true); continue; }   // destroyed (the server's destroy reached this client)
                    if (now < e.Until) continue;
                    if (!server) {
                        holds.Remove(a);
                        e.Free(a, false);
                        MelonLogger.Warning(Inv($"[Archery] Skyfall: arrow {e.Release}'s real arrow was still here {SkyfallSeam.ClientHoldSeconds:0.#} s after the release; shown where it is"));
                        continue;
                    }
                    try {
                        var entity = a.Entity;
                        if (entity != null && !entity.IsBeingDestroyed) entity.SceneDestroy();
                        e.Tries++;
                        SkyDestroy step = SkyfallSeam.DestroyNext(e.Tries, entity != null && entity.IsBeingDestroyed);
                        if (step == SkyDestroy.Retry) {
                            if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {e.Release}'s real arrow still stands after destroy try {e.Tries}; trying again next tick");
                            continue;   // still held: the next tick tries again
                        }
                        holds.Remove(a);
                        if (step == SkyDestroy.Done) {
                            if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Skyfall: arrow {e.Release}'s real arrow destroyed {SkyfallSeam.DestroySeconds:0.#} s after the release (try {e.Tries})"));
                            continue;
                        }
                        e.Free(a, false);
                        try { if (entity != null) entity.ForcePositionSync(); } catch { }
                        if (!keptWarned) {
                            keptWarned = true;
                            MelonLogger.Warning($"[Archery] Skyfall: the held arrow {e.Release} could not be destroyed (another mod kept it, e.g. RevampedSkills' Holy Arrow keep); it was let go as a normal arrow.");
                        }
                        else if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: the held arrow {e.Release} could not be destroyed either; let go as a normal arrow");
                    }
                    catch (Exception ex) {
                        holds.Remove(a);
                        e.Free(a, false);
                        try { if (a.Entity != null) a.Entity.ForcePositionSync(); } catch { }
                        MelonLogger.Warning($"[Archery] Skyfall: arrow {e.Release}'s real arrow could not be destroyed; let go where it is: {ex.Message}");
                    }
                }
            }

            /// <summary>Server quit: every held arrow destroyed (each on its own, as AutoDock.Shutdown), so none outlives the server
            /// hidden; then the table is cleared. Never throws.</summary>
            internal static void Shutdown() {
                try {
                    if (NetworkSceneManager.IsServer)
                        foreach (var kv in holds) {
                            try {
                                Arrow a = kv.Key;
                                if (a != null && a.Entity != null && !a.Entity.IsBeingDestroyed) a.Entity.SceneDestroy();
                            }
                            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: held arrow {kv.Value.Release} could not be destroyed at shutdown: {e.Message}"); }
                        }
                    holds.Clear();
                }
                catch { }
            }

            /// <summary>Clients: the server's flight sync for a held arrow would set it flying again: refused while it is held. Every
            /// prefix on this method runs and their results are ANDed (HarmonyX), so no priority is needed beside Seek's and Quickdraw 4's.</summary>
            [HarmonyPatch(typeof(Arrow), nameof(Arrow.StartHandlingArrowDeviance))]
            static class HeldFlight {
                static readonly int T = Timing.Section("patch Arrow.StartHandlingArrowDeviance / Skyfall.HeldFlight");
                static bool Prefix(Arrow __instance) {
                    using var _t = Timing.Begin(T);
                    try {
                        if (NetworkSceneManager.IsServer || holds.Count == 0 || __instance == null || !holds.TryGetValue(__instance, out Entry e)) return true;
                        HeldArrow.Stop(__instance);
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: arrow {e.Release}'s flight sync refused: the real arrow is held");
                        return false;
                    }
                    catch (Exception ex) { MelonLogger.Warning($"[Archery] Skyfall: held-arrow check failed: {ex.Message}"); return true; }
                }
            }
        }
    }
}
