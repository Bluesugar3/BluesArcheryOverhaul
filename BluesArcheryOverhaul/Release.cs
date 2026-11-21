using System;

// Unity-free. The release decision: the volley timeline, what a claim's stage means, the resolver, the one-ultimate rule
// and the Holy Arrow live-draw gate. Compiled by BluesArcheryOverhaul.Tests, so only System.* in here.
namespace BluesArcheryOverhaul {
    /// <summary>Seconds after the 68% crossing at which each beat of the volley happens.</summary>
    internal readonly struct VolleyTimes {
        public readonly float VolleyAt, FullAt, Interval, RippleAt, ThumpAt, RackAt;

        public VolleyTimes(float volleyAt, float fullAt, float interval, float rippleAt, float thumpAt, float rackAt) {
            VolleyAt = volleyAt; FullAt = fullAt; Interval = interval; RippleAt = rippleAt; ThumpAt = thumpAt; RackAt = rackAt;
        }

        public static VolleyTimes Default => new VolleyTimes(0.3f, 1f, 0.045f, 1.5f, 1.75f, 1.5f); // 0.9.1: the ghosts slide out as Quickdraw's 0.25 s window ends (was 0.75); play-test 7: Burst racks at 1.5 s (was 2)

        /// <summary>When the fifteenth ghost shows: the ratchet's last step (never before the five).</summary>
        public float GhostsFullAt => Math.Max(VolleyAt, FullAt + (Interval > 0f ? (VolleyMath.Outer - 1) * Interval : 0f));

        /// <summary>Burst's rack: RackAt, but never before the fifteen ghosts are full (play-test 7: "have it be at 1.5 seconds";
        /// if a build's ghosts fill later, the rack waits for them).</summary>
        public float RackTime => Math.Max(RackAt, GhostsFullAt);

        /// <summary>Every time field (the ratchet's interval too) times <paramref name="factor"/> (a Multi main's Quick complement).</summary>
        public VolleyTimes Scaled(float factor) =>
            new VolleyTimes(VolleyAt * factor, FullAt * factor, Interval * factor, RippleAt * factor, ThumpAt * factor, RackAt * factor);
    }

    [Flags] internal enum VolleyBeat { None = 0, SlideOut = 1, Ripple = 2, Rack = 4 }

    internal static class VolleyTimeline {
        const double Eps = 1e-5;

        /// <summary>Ghosts showing: the first five need the game's charged state and the player's own VolleyAt (the game's timer is
        /// shared by every player and set to the fastest build's); the ratchet adds ten, one per interval.</summary>
        public static int Ghosts(int volleyTier, bool volleyCharged, float t, VolleyTimes tm) {
            if (volleyTier < 1 || !volleyCharged || (double)t + Eps < tm.VolleyAt) return 0;
            int n = VolleyMath.Inner;
            if (volleyTier >= 2 && (double)t + Eps >= tm.FullAt) {
                int steps = tm.Interval > 0f ? 1 + (int)Math.Floor(((double)t - tm.FullAt + Eps) / tm.Interval) : VolleyMath.Outer;
                n += Math.Min(VolleyMath.Outer, steps);
            }
            return n;
        }

        public static bool Racked(int volleyTier, bool volleyCharged, float t, VolleyTimes tm) =>
            volleyTier >= 3 && volleyCharged && (double)t + Eps >= tm.RackTime;

        /// <summary>The ripple folds into the thump when a Charge orb is owned.</summary>
        public static float RippleAt(int chargeTier, VolleyTimes tm) => chargeTier >= 1 ? tm.ThumpAt : tm.RippleAt;

        /// <summary>Beats that fall in (from, to]; consecutive frames never fire one twice.</summary>
        public static VolleyBeat BeatsBetween(float from, float to, int volleyTier, int chargeTier, VolleyTimes tm) {
            var beats = VolleyBeat.None;
            if (volleyTier >= 1 && from < tm.VolleyAt && tm.VolleyAt <= to) beats |= VolleyBeat.SlideOut;
            float ripple = RippleAt(chargeTier, tm);
            if (volleyTier >= 2 && from < ripple && ripple <= to) beats |= VolleyBeat.Ripple;
            float rack = tm.RackTime;
            if (volleyTier >= 3 && from < rack && rack <= to) beats |= VolleyBeat.Rack;
            return beats;
        }
    }

    /// <summary>What a release fires. The claim's stage is the number of spectral arrows: echoes (0..6; a Quick main's needles
    /// on a snap) for Snap and Early, else the first 15 are ghosts and the rest pellets (0..15). A Charge main's heavy arrows
    /// (0..6) on a Charged release travel as ghosts.</summary>
    internal readonly struct ReleasePlan {
        public readonly int Ghosts, Pellets, Echoes;

        public ReleasePlan(int ghosts, int pellets, int echoes) { Ghosts = ghosts; Pellets = pellets; Echoes = echoes; }

        public int Stage => Ghosts + Pellets + Echoes;

        /// <summary>The most needles (echoes) a snap's stage relays.</summary>
        public const int MaxNeedles = 6;
        /// <summary>The most heavy arrows (a Charge main's, in the ghost slot) a charged shot carries.</summary>
        public const int MaxHeavy = 6;

        public static ReleasePlan From(ShotKind kind, int stage) {
            if (stage < 0) stage = 0;
            if (kind == ShotKind.Early || kind == ShotKind.Snap) return new ReleasePlan(0, 0, Math.Min(MaxNeedles, stage));
            return new ReleasePlan(Math.Min(VolleyMath.Total, stage), Math.Min(VolleyMath.Total, Math.Max(0, stage - VolleyMath.Total)), 0);
        }

        public override string ToString() => $"ghosts {Ghosts} pellets {Pellets} echoes {Echoes}";
    }

    internal enum UltimateUse { None, Line, Beam }

    // Filled in by Draws.cs (a later task) and by the tests; until then the mod project never assigns the fields.
#pragma warning disable CS0649
    internal struct ReleaseFacts {
        public Build Build;
        public bool SnapReady, ChargeActive, HolyArmed, Racked, Crossed;
        public int Ghosts;
        public int Needles;         // a Quick main's needles (Complements.Needles), fired beside a snap
        public int Heavy;           // a Charge main's heavy arrows (Complements.HeavyArrows), fired beside a charged shot
        public float? Seconds;     // since the crossing, as this machine decides with
        public ChargeStages? Stages; // null = every owned Charge orb counts at once
    }
#pragma warning restore CS0649

    internal readonly struct Resolution {
        public readonly ShotKind Kind;
        public readonly UltimateUse Ultimate;
        public readonly ReleasePlan Plan;
        public readonly int ChargedTier;
        /// <summary>G3: a Multi main's held volley, the hold stage it reached (0 to its Charge Shot orbs; VolleyHold). Decided where the
        /// release is (Draws.Release), from the time the release is decided with; 0 for every other release.</summary>
        public readonly int HoldStage;

        public Resolution(ShotKind kind, UltimateUse ultimate, ReleasePlan plan, int chargedTier, int holdStage = 0) {
            Kind = kind; Ultimate = ultimate; Plan = plan; ChargedTier = chargedTier; HoldStage = holdStage < 0 ? 0 : holdStage;
        }

        /// <summary>The same release with this hold stage.</summary>
        public Resolution WithHold(int holdStage) => new Resolution(Kind, Ultimate, Plan, ChargedTier, holdStage);

        public override string ToString() =>
            $"{Kind} ult:{(Ultimate == UltimateUse.None ? "-" : Ultimate.ToString())} {Plan} charge tier {ChargedTier}" + (HoldStage > 0 ? $" hold stage {HoldStage}" : "");
    }

    /// <summary>G3 (the user: "Yes, build that"): a multi-arrow main's Charge Shot orbs are earned by holding. Once the volley is
    /// full for the archer (Two-Shot: the five; Three-Shot: the fifteenth ghost; Burst: the rack), each Charge Shot orb owned adds one
    /// stage to hold for: stage k is reached [Complements] VolleyStageSeconds x k after that. Released at stage s the ghosts and
    /// pellets deal x (1 + GhostDamagePerOrb x s) and each ghost carries GhostStaggerPerOrb x s (Complements.GhostDamageAt /
    /// GhostStaggerAt). Stage 0 is a plain volley. Every other main path, and a Multi main with no Charge Shot orbs, has no stage.</summary>
    internal static class VolleyHold {
        public const float DefaultStageSeconds = 0.75f;
        const double Eps = 1e-4;

        /// <summary>The stages this build can hold for: a Multi main's Charge Shot orbs (at most three); 0 for every other build.</summary>
        public static int Orbs(Build b) => b.Main == MainPath.Multi && b.Volley > 0 ? Math.Max(0, Math.Min(Complements.MaxOrbs, b.Charge)) : 0;

        /// <summary>When the volley is full for this Volley tier: the five (tier 1), the fifteenth ghost (2), Burst's rack (3).</summary>
        public static float FullAt(int volleyTier, VolleyTimes tm) => volleyTier >= 3 ? tm.RackTime : volleyTier == 2 ? tm.GhostsFullAt : tm.VolleyAt;

        /// <summary>The stage reached <paramref name="seconds"/> after the 68% crossing (the mod's draw clock); 0 with no time, a
        /// broken stage time, or nothing to hold for.</summary>
        public static int Stage(Build b, float? seconds, VolleyTimes tm, float stageSeconds) {
            int orbs = Orbs(b);
            if (orbs <= 0 || !seconds.HasValue || float.IsNaN(seconds.Value) || !(stageSeconds > 0f) || float.IsInfinity(stageSeconds)) return 0;
            double held = (double)seconds.Value - FullAt(b.Volley, tm);
            if (held + Eps < 0) return 0;
            int k = (int)Math.Floor((held + Eps) / stageSeconds);
            return k < 0 ? 0 : k > orbs ? orbs : k;
        }

        /// <summary>G4, looks only (the shimmer that creeps along the ghosts' shafts): how far the hold has come toward its next stage,
        /// 0 to 1, on the same clock and with the same arithmetic as <see cref="Stage"/>, so it is empty again the moment the stage
        /// steps. 0 before the volley is full, at the last stage owned, with no time, a broken stage time or nothing to hold for.</summary>
        public static float Toward(Build b, float? seconds, VolleyTimes tm, float stageSeconds) {
            int orbs = Orbs(b);
            if (orbs <= 0 || !seconds.HasValue || float.IsNaN(seconds.Value) || !(stageSeconds > 0f) || float.IsInfinity(stageSeconds)) return 0f;
            double held = (double)seconds.Value - FullAt(b.Volley, tm);
            if (held + Eps < 0) return 0f;
            int k = (int)Math.Floor((held + Eps) / stageSeconds);
            if (k < 0 || k >= orbs) return 0f;
            double f = (held - (double)k * stageSeconds) / stageSeconds;
            return f < 0 ? 0f : f > 1 ? 1f : (float)f;
        }

        /// <summary>The stage a release carries: only a held volley (ghosts, no ultimate) of a build that can hold.</summary>
        public static int ForRelease(Resolution r, Build b, float? seconds, VolleyTimes tm, float stageSeconds) =>
            r.Kind == ShotKind.Held && r.Ultimate == UltimateUse.None && r.Plan.Ghosts > 0 ? Stage(b, seconds, tm, stageSeconds) : 0;
    }

    internal static class Resolver {
        /// <summary>A line's own looks and sounds show for this main path: its own line, or no main path (unmodded behaviour).</summary>
        public static bool OnPath(MainPath main, MainPath line) => main == MainPath.None || main == line;

        /// <summary>Only the main path's shot exists: the facts with the off-path lines removed. MainPath.None (no line orbs,
        /// or a main path not known yet) leaves them alone.</summary>
        public static ReleaseFacts Gate(ReleaseFacts f) {
            MainPath main = f.Build.Main;
            if (main == MainPath.None) return f;
            if (main != MainPath.Quick) { f.SnapReady = false; f.Needles = 0; }
            if (main != MainPath.Charge) { f.ChargeActive = false; f.HolyArmed = false; f.Heavy = 0; }
            if (main != MainPath.Multi) { f.Ghosts = 0; f.Racked = false; }
            return f;
        }

        /// <summary>One decision per release, first match wins. Echoes are gone; a Quick main's snap carries its needles in
        /// the plan's echo slot (the claim's stage relays the count, as it relayed echoes).</summary>
        public static Resolution Resolve(ReleaseFacts f) {
            f = Gate(f);
            int tier = f.Stages.HasValue ? f.Stages.Value.Tier(f.Build.Charge, f.Seconds) : Math.Max(1, f.Build.Charge);
            if (f.HolyArmed) return new Resolution(ShotKind.Charged, UltimateUse.Beam, new ReleasePlan(0, 0, 0), tier);
            if (f.SnapReady) {
                var use = (f.Build.Ultimates & Ultimates.Quickdraw4) != 0 ? UltimateUse.Line : UltimateUse.None;
                return new Resolution(ShotKind.Snap, use, new ReleasePlan(0, 0, Math.Max(0, Math.Min(ReleasePlan.MaxNeedles, f.Needles))), 0);
            }
            if (f.Ghosts > 0) {
                return new Resolution(f.ChargeActive ? ShotKind.Charged : ShotKind.Held, UltimateUse.None,
                    new ReleasePlan(Math.Min(f.Ghosts, VolleyMath.Total), f.Racked ? VolleyMath.Total : 0, 0), f.ChargeActive ? tier : 0);
            }
            if (f.ChargeActive)
                return new Resolution(ShotKind.Charged, UltimateUse.None, new ReleasePlan(Math.Max(0, Math.Min(ReleasePlan.MaxHeavy, f.Heavy)), 0, 0), tier);
            return new Resolution(ShotKind.Early, UltimateUse.None, new ReleasePlan(0, 0, 0), 0);
        }

        /// <summary>The tier whose multiplier the real arrow carries: a snap carries the Quick tier (quick = true), a charged
        /// shot that is not a beam its charged tier, anything else 0 (x1).</summary>
        public static float RealArrowTier(Resolution r, Build b, out bool quick) {
            quick = r.Kind == ShotKind.Snap;
            if (quick) return b.Quick;
            if (r.Kind == ShotKind.Charged && r.Ultimate != UltimateUse.Beam) return r.ChargedTier;
            return 0;
        }

        /// <summary>The real arrow's one-off multiplier after a release, or null to leave what the game (or a Line's or a
        /// beam's own code) set. <paramref name="game"/> is the value the game set; the tables are Quickdraw's and Slowdraw's
        /// multiplier at a tier. No main path keeps the game's value except that a staged charge is lowered to the stage it
        /// reached on Slowdraw's own table (0.9.1, exactly as 0.9.2 did). A main path's charged shot carries
        /// <paramref name="chargeDamage"/> (play-test 7: the mod's [Charge] StageNDamage table; null = Slowdraw's table).</summary>
        public static float? RealArrowMultiplier(Resolution r, Build b, float game, Func<int, float> quickTable, Func<int, float> chargeTable,
                                                 Func<int, float>? chargeDamage = null) {
            if (r.Ultimate != UltimateUse.None) return null;
            if (b.Main == MainPath.None) {
                if (r.Kind != ShotKind.Charged || r.ChargedTier < 1) return null;
                float staged = chargeTable(r.ChargedTier);
                return staged < game - 0.001f ? staged : (float?)null;
            }
            int tier = (int)RealArrowTier(r, b, out bool quick);
            if (tier <= 0) return 1f;
            return quick ? quickTable(tier) : (chargeDamage ?? chargeTable)(tier);
        }
    }

    /// <summary>Only one ultimate can be bought. The ints are the game's SkillStatus values.</summary>
    internal static class UltimateRule {
        public const int TestingCantBuy = 2, CantBuy = 3, TestingAllowed = 8, Allowed = 9;

        public static bool IsUltimate(Orb o) => o == Orb.Quickdraw4 || o == Orb.SeekingVolley || o == Orb.HolyArrow;

        static Ultimates Flag(Orb o) => o == Orb.Quickdraw4 ? Ultimates.Quickdraw4 : o == Orb.SeekingVolley ? Ultimates.SeekingVolley
                                         : o == Orb.HolyArrow ? Ultimates.HolyArrow : Ultimates.None;

        /// <summary>True when the slot is an ultimate other than one already owned and some ultimate is owned.</summary>
        public static bool Blocks(Orb slotOrb, Ultimates owned) => IsUltimate(slotOrb) && owned != Ultimates.None && (owned & Flag(slotOrb)) == 0;

        public static int Downgrade(int status) => status == Allowed ? CantBuy : status == TestingAllowed ? TestingCantBuy : status;
    }

    /// <summary>Whether a spectator draws a relayed release's ghosts as a Charge main's heavy arrows (looks only, never the
    /// count): a Charged release (no ultimate) with 1 to MaxHeavy ghosts and no pellets, from a shooter whose build is not
    /// known here yet or is known to be a Charge main. A known None or Multi main is never heavy (a None main's charged
    /// volley must look as in 0.9.2).</summary>
    internal static class HeavyLookRule {
        public static bool Spectator(ShotKind kind, UltimateUse ultimate, int ghosts, int pellets, bool buildKnown, MainPath main) =>
            kind == ShotKind.Charged && ultimate == UltimateUse.None && ghosts >= 1 && ghosts <= ReleasePlan.MaxHeavy && pellets == 0
            && (!buildKnown || main == MainPath.Charge);
    }

    internal static class HolyGate {
        /// <summary>Holy Arrow arms only on a live draw: string held, crossed, and the same arrow as at the crossing.</summary>
        public static bool MayArm(bool stringHeld, bool crossed, uint arrowNow, uint arrowAtCrossing) =>
            stringHeld && crossed && arrowNow != 0u && arrowNow == arrowAtCrossing;

        /// <summary>The shooter's release strength: an armed Holy Arrow always goes out at full draw (the beam's speed is
        /// read from it), anything else as pulled.</summary>
        public static float ReleaseStrength(float strength, bool holyArmed) => holyArmed ? 1f : strength;
    }

    /// <summary>What a client draws for a Holy Arrow beam. The shooter's beam is launched on its own machine
    /// (Projectile.Shoot, "shot here"). Every other client never sees that call: the server destroys the sacrificed arrow
    /// before it relays the shot, so the relayed Shooter.Shoot finds no arrow and stops. Those clients only get the beam's
    /// own messages: "fired" (Projectile.SyncFired) and "collided" (Projectile.SyncCollided).</summary>
    internal enum BeamLanding { None, Own, Seen }

    internal static class SeenBeamRule {
        /// <summary>On the beam's "fired" message: start a seen beam's trail (once, never for a beam this client shot).</summary>
        public static bool DrawsFired(bool server, bool headless, bool isBeam, bool shotHere, bool alreadySeen) =>
            !server && !headless && isBeam && !shotHere && !alreadySeen;

        /// <summary>On the beam's "collided" message: the shooter's own landing, a seen beam's landing (also when its
        /// "fired" message was missed), or nothing.</summary>
        public static BeamLanding Landing(bool server, bool headless, bool shotHere, bool seenFired, bool isBeam) =>
            server || headless ? BeamLanding.None
            : shotHere ? BeamLanding.Own
            : seenFired || isBeam ? BeamLanding.Seen
            : BeamLanding.None;

        /// <summary>A trail's brightness: full for the local archer's beam, [Volley] OtherArcherBrightness (0..1) for another's.</summary>
        public static float TrailBrightness(bool local, float otherArcher) =>
            local ? 1f : otherArcher < 0f ? 0f : otherArcher > 1f ? 1f : otherArcher;
    }
}
