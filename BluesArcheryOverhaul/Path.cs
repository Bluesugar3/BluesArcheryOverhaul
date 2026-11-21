using System;
using System.Collections.Generic;
using System.Text;

// Unity-free. The main path rule: which of the three lines (Quickdraw, multi-arrow, Charge Shot) is a
// player's main one. Compiled by BluesArcheryOverhaul.Tests, so only System.* in here.
namespace BluesArcheryOverhaul {
    internal enum MainPath { None = 0, Quick = 1, Multi = 2, Charge = 3 }

    internal static class MainPathRule {
        /// <summary>Orbs owned on a line, its legendary orb counting as one more.</summary>
        public static int Count(Build b, MainPath line) {
            switch (line) {
                case MainPath.Quick: return b.Quick + ((b.Ultimates & Ultimates.Quickdraw4) != 0 ? 1 : 0);
                case MainPath.Multi: return b.Volley + ((b.Ultimates & Ultimates.SeekingVolley) != 0 ? 1 : 0);
                case MainPath.Charge: return b.Charge + ((b.Ultimates & Ultimates.HolyArrow) != 0 ? 1 : 0);
                default: return 0;
            }
        }

        /// <summary>No orbs on any line: None. Otherwise the line with the most orbs; on a tie the incumbent
        /// stays if it is among the leaders, else the order Charge, Quick, Multi.</summary>
        public static MainPath Decide(Build b, MainPath incumbent) {
            int q = Count(b, MainPath.Quick), m = Count(b, MainPath.Multi), c = Count(b, MainPath.Charge);
            int best = Math.Max(q, Math.Max(m, c));
            if (best <= 0) return MainPath.None;
            if (incumbent != MainPath.None && Count(b, incumbent) == best) return incumbent;
            if (c == best) return MainPath.Charge;
            if (q == best) return MainPath.Quick;
            return MainPath.Multi;
        }

        /// <summary>True when owning <paramref name="after"/> instead of <paramref name="now"/> would move an
        /// existing main path to another line.</summary>
        public static bool WouldFlip(Build now, MainPath main, Build after) =>
            main != MainPath.None && Decide(after, main) != main;

        /// <summary>True when a decided path should replace the remembered one. A build with no line orbs (None) never
        /// removes or rewrites it: on a joining player or a client whose orbs have not arrived yet, zero counts are
        /// not a real sale, and erasing the incumbent would change later ties.</summary>
        public static bool ShouldRemember(MainPath decided, MainPath remembered) =>
            decided != MainPath.None && decided != remembered;

        /// <summary>Why <c>Decide(b, old)</c> gave <paramref name="now"/>, for the log: first orb, out-counted,
        /// tie kept, tie fallback, no line orbs.</summary>
        public static string Why(Build b, MainPath old, MainPath now) {
            if (now == MainPath.None) return "no line orbs";
            int best = Count(b, now), leaders = 0;
            foreach (MainPath p in new[] { MainPath.Quick, MainPath.Multi, MainPath.Charge }) if (Count(b, p) == best) leaders++;
            if (now == old) return leaders > 1 ? "tie kept" : "out-counted";
            if (leaders > 1) return "tie fallback";
            return old == MainPath.None ? "first orb" : "out-counted";
        }
    }

    /// <summary>What the two off-path lines give the main shot, per orb (shared settings, [Complements]).</summary>
    internal readonly struct ComplementValues {
        public readonly float NeedleShare;          // Quick main + Multi orbs: each needle's share of the arrow's power
        public readonly int NeedlesPerOrb;
        public readonly float SnapStaggerPerOrb;    // Quick main + Charge orbs: stagger on a snap
        public readonly int SnapInterruptFrom;      // ... and from this many Charge orbs a snap interrupts
        public readonly float VolleyFasterPerOrb;   // Multi main + Quick orbs: the volley timeline shortens
        public readonly float SpreadTighterPerOrb;  // ... and the ghosts' spread narrows
        public readonly float GhostStaggerPerOrb;   // Multi main + Charge orbs: stagger per ghost
        public readonly float StageFasterPerOrb;    // Charge main + Quick orbs: every stage (and Holy Arrow's wait) shortens
        public readonly int HeavyPerOrb;            // Charge main + Multi orbs: heavy arrows per orb
        public readonly float HeavyShare;           // ... each this share of the arrow's power
        public readonly float GhostDamagePerOrb;    // Multi main + Charge orbs: ghosts' and pellets' damage, +this per stage held (fix batch D; G3: per stage)
        public readonly float VolleyStageSeconds;   // Multi main + Charge orbs (G3): seconds per hold stage after the volley is full

        public ComplementValues(float needleShare, int needlesPerOrb, float snapStaggerPerOrb, int snapInterruptFrom, float volleyFasterPerOrb,
                                float spreadTighterPerOrb, float ghostStaggerPerOrb, float stageFasterPerOrb, int heavyPerOrb, float heavyShare, float ghostDamagePerOrb = 0.10f,
                                float volleyStageSeconds = VolleyHold.DefaultStageSeconds) {
            NeedleShare = needleShare; NeedlesPerOrb = needlesPerOrb; SnapStaggerPerOrb = snapStaggerPerOrb; SnapInterruptFrom = snapInterruptFrom;
            VolleyFasterPerOrb = volleyFasterPerOrb; SpreadTighterPerOrb = spreadTighterPerOrb; GhostStaggerPerOrb = ghostStaggerPerOrb;
            StageFasterPerOrb = stageFasterPerOrb; HeavyPerOrb = heavyPerOrb; HeavyShare = heavyShare; GhostDamagePerOrb = ghostDamagePerOrb;
            VolleyStageSeconds = volleyStageSeconds;
        }

        public static ComplementValues Default => new ComplementValues(0.15f, 1, 0.1f, 2, 0.25f, 0.15f, 0.03f, 0.12f, 2, 0.15f, 0.10f, VolleyHold.DefaultStageSeconds);
    }

    /// <summary>The numbers of the later orbs (spec 2026-11-16) that the shrine's tablet shows. Shared settings: each orb's
    /// batch makes the server's result depend on them. Until then they are only words on the tablet.</summary>
    internal readonly struct OrbValues {
        public readonly float RicochetMetres;                        // Ricochet: a creature hit jumps to another creature within this
        public readonly float SeekerShare;                           // Seeking Volley: a locked seeker's share
        public readonly int ShatterPellets;                          // Shatter (play-test 7): pellets the blast's look draws
        public readonly float ShatterPerOrb, ShatterBlast, ShatterMetres; // ... the blast's extra share per multi-arrow orb, its share close behind, its reach
        public readonly float RicochetJumpShare;                     // Ricochet: each landed arrow's jump, its share of that arrow's damage
        public readonly float[] PierceShares;                        // Quickdraw 4 (batch 4b): the share each creature after the first takes
        public readonly int RicochetSpread;                          // Ricochet (play-test 7): jumps go round this many nearest creatures
        public readonly float SideOrbOffPathShare;                   // fix batch D: Shatter's and Ricochet's damage off their own main path
        public readonly HomingValues Homing;                         // H1: Seeking Volley's lock and flight numbers ([SeekingVolley])
        public readonly int RicochetBounces;                         // H5b: creatures one jump chain reaches ([Ricochet] Bounces; 1 = one jump)

        public OrbValues(float ricochetMetres, float seekerShare, int shatterPellets, float shatterPerOrb, float shatterBlast,
                         float ricochetJumpShare = 0.5f, float[]? pierceShares = null, float shatterMetres = 10f, int ricochetSpread = 3, float sideOrbOffPathShare = 0.5f,
                         HomingValues? homing = null, int ricochetBounces = 1) {
            RicochetMetres = ricochetMetres; SeekerShare = seekerShare;
            Homing = homing ?? HomingValues.Default;
            RicochetBounces = ricochetBounces < 1 ? 1 : ricochetBounces;
            ShatterPellets = shatterPellets; ShatterPerOrb = shatterPerOrb; ShatterBlast = shatterBlast;
            ShatterMetres = shatterMetres; RicochetSpread = ricochetSpread; SideOrbOffPathShare = sideOrbOffPathShare;
            RicochetJumpShare = ricochetJumpShare;
            PierceShares = pierceShares != null && pierceShares.Length > 0 ? pierceShares : LinePierce.DefaultShares;
        }

        // play-test 7: Ricochet reaches 15 m (was 8) and spreads over 3; Shatter is a blast (24 pellets, x2, +15% per orb, 6 m)
        // G3 (play-test 8): Shatter reaches 10 m (was 6)
        // H1 (the owner, 2026-11-17: "Full damage"): Seeking Volley's seekers deal their whole share (was 0.6, words only until H1)
        public static OrbValues Default => new OrbValues(15f, 1f, 24, 0.15f, 2f, 0.5f, null, 10f, 3);

    }

    /// <summary>The complements: every method gives the neutral value unless the build's main path is the one named.</summary>
    internal static class Complements {
        /// <summary>No timeline gets shorter than this share of its own length.</summary>
        public const float FloorFactor = 0.25f;
        /// <summary>The most off-path orbs a line can count (an off-path legendary adds nothing).</summary>
        public const int MaxOrbs = 3;

        static float Faster(float perOrb, int orbs) => Math.Max(FloorFactor, 1f - perOrb * orbs);

        /// <summary>Charge main: each Quick orb makes every charge stage (and Holy Arrow's wait) complete sooner.</summary>
        public static float StageFactor(Build b, ComplementValues v) => b.Main == MainPath.Charge ? Faster(v.StageFasterPerOrb, b.Quick) : 1f;

        /// <summary>Multi main: each Quick orb shortens the whole volley timeline.</summary>
        public static float VolleyFactor(Build b, ComplementValues v) => b.Main == MainPath.Multi ? Faster(v.VolleyFasterPerOrb, b.Quick) : 1f;

        /// <summary>Multi main: each Quick orb narrows the ghosts' spread.</summary>
        public static float SpreadFactor(Build b, ComplementValues v) => b.Main == MainPath.Multi ? Math.Max(0f, 1f - v.SpreadTighterPerOrb * b.Quick) : 1f;

        /// <summary>Quick main: needles fired beside every quick shot.</summary>
        public static int Needles(Build b, ComplementValues v) => b.Main == MainPath.Quick ? Math.Max(0, v.NeedlesPerOrb * b.Volley) : 0;

        /// <summary>Charge main: heavy arrows fired beside every charged shot.</summary>
        public static int HeavyArrows(Build b, ComplementValues v) => b.Main == MainPath.Charge ? Math.Max(0, v.HeavyPerOrb * b.Volley) : 0;

        /// <summary>Quick main: the stagger every quick shot deals.</summary>
        public static float SnapStagger(Build b, ComplementValues v) => b.Main == MainPath.Quick ? Math.Max(0f, v.SnapStaggerPerOrb * b.Charge) : 0f;

        /// <summary>Quick main: a quick shot interrupts a cancellable action.</summary>
        public static bool SnapInterrupts(Build b, ComplementValues v) => b.Main == MainPath.Quick && b.Charge >= v.SnapInterruptFrom && b.Charge > 0;

        /// <summary>Multi main: the stagger each ghost carries.</summary>
        public static float GhostStagger(Build b, ComplementValues v) => b.Main == MainPath.Multi ? Math.Max(0f, v.GhostStaggerPerOrb * b.Charge) : 0f;

        /// <summary>Fix batch D (the user: "Have it give it a damage and stagger buff"): Multi main, the factor on its ghosts' and
        /// pellets' damage, 1 + GhostDamagePerOrb per Charge Shot orb (1.0 with none, and for every other main path; a broken
        /// setting is 1.0). The real arrow is not changed.</summary>
        public static float GhostDamage(Build b, ComplementValues v) {
            if (b.Main != MainPath.Multi || float.IsNaN(v.GhostDamagePerOrb) || float.IsInfinity(v.GhostDamagePerOrb)) return 1f;
            return Math.Max(0f, 1f + v.GhostDamagePerOrb * b.Charge);
        }

        /// <summary>G3 (earned by holding): the factor on a Multi main's ghosts' and pellets' damage at hold stage
        /// <paramref name="stage"/> (VolleyHold), 1 + GhostDamagePerOrb x the stage, never past the stages the build can hold for.
        /// 1.0 at stage 0, for every other main path and for a broken setting. (<see cref="GhostDamage"/> is the most it can reach.)</summary>
        public static float GhostDamageAt(Build b, ComplementValues v, int stage) {
            int s = Math.Max(0, Math.Min(stage, VolleyHold.Orbs(b)));
            if (s == 0 || float.IsNaN(v.GhostDamagePerOrb) || float.IsInfinity(v.GhostDamagePerOrb)) return 1f;
            return Math.Max(0f, 1f + v.GhostDamagePerOrb * s);
        }

        /// <summary>G3: the stagger each of a Multi main's ghosts carries at hold stage <paramref name="stage"/>: GhostStaggerPerOrb x
        /// the stage (never past the stages it can hold for); 0 at stage 0 and for every other build. (<see cref="GhostStagger"/> is the most.)</summary>
        public static float GhostStaggerAt(Build b, ComplementValues v, int stage) {
            int s = Math.Max(0, Math.Min(stage, VolleyHold.Orbs(b)));
            float g = v.GhostStaggerPerOrb * s;
            return s > 0 && g > 0f && !float.IsInfinity(g) ? g : 0f;
        }

        /// <summary>The factor at three off-path orbs, the fastest any build can have: the game's shared timers (one per skill,
        /// for every player) are set to it and each player is gated on the mod's clock. <paramref name="stages"/>: the charge
        /// stages' factor, else the volley's.</summary>
        public static float MinFactor(ComplementValues v, bool stages) => Faster(stages ? v.StageFasterPerOrb : v.VolleyFasterPerOrb, MaxOrbs);

        /// <summary>The mod's clock has reached <paramref name="at"/> (seconds after the crossing); an unknown time never has.</summary>
        public static bool Reached(float? t, float at) => t.HasValue && t.Value + 1e-4f >= at;
    }

    /// <summary>The look of a Quick main's snap weight (its Charge Shot orbs' stagger and interrupt on a quick shot): who
    /// shows it and how strongly. Looks only; what the shot does is <see cref="Complements.SnapStagger"/> and
    /// <see cref="Complements.SnapInterrupts"/>.</summary>
    internal static class SnapWeightLook {
        /// <summary>Where along Quickdraw 4's line the charge's orange starts: its last third.</summary>
        public const float LineTintFrom = 2f / 3f;

        /// <summary>Charge Shot orbs that show on this archer's quick shots (0 = no look): a Quick main's, at most three.</summary>
        public static int Orbs(Build b) => b.Main == MainPath.Quick ? Math.Max(0, Math.Min(Complements.MaxOrbs, b.Charge)) : 0;

        /// <summary>0..1: <paramref name="one"/> with one orb, <paramref name="perOrb"/> more for each further one.</summary>
        public static float Brightness(int orbs, float one, float perOrb) {
            if (orbs <= 0) return 0f;
            float k = one + perOrb * (orbs - 1);
            return k < 0f || float.IsNaN(k) ? 0f : k > 1f ? 1f : k;
        }

        /// <summary>A snap (flying, or Quickdraw 4's line) from a Quick main with Charge Shot orbs; never a beam.</summary>
        public static bool Shows(ShotKind kind, UltimateUse ultimate, Build b) =>
            kind == ShotKind.Snap && ultimate != UltimateUse.Beam && Orbs(b) > 0;

        /// <summary>The quick shot can interrupt (the cue's condition; the server alone knows whether it did).</summary>
        public static bool Interrupts(Build b, ComplementValues v) => Orbs(b) > 0 && Complements.SnapInterrupts(b, v);

        /// <summary>The interrupt cue on one creature: not again within the server's interrupt cooldown.</summary>
        public static bool CueDue(float now, float lastCue, float cooldown) => float.IsNaN(lastCue) || now - lastCue >= cooldown;

        // ---- G4 (play-test 8, the user: "very lackluster"; chosen: "the quick shot's extra stagger gets a stronger look and sound")

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>How much bigger the ember, its light and the trail are than with one orb: 1 with one, <paramref name="perOrb"/>
        /// more for each further Charge Shot orb (at most three). Never smaller than 1; a broken number is 1.</summary>
        public static float Size(int orbs, float perOrb) {
            if (orbs <= 1 || !(perOrb > 0f) || float.IsInfinity(perOrb)) return 1f;
            return 1f + perOrb * (Math.Min(Complements.MaxOrbs, orbs) - 1);
        }

        /// <summary>The size of the charged hit effect where a weighted quick shot hits a creature: <paramref name="one"/> with fewer
        /// than <paramref name="heavyFrom"/> Charge Shot orbs, <paramref name="heavy"/> from there on (the heavier impact). 0 with no
        /// orb or a size that is not a positive number.</summary>
        public static float HitScale(int orbs, float one, float heavy, int heavyFrom) {
            if (orbs <= 0) return 0f;
            float k = orbs >= heavyFrom ? heavy : one;
            return k > 0f && !float.IsInfinity(k) ? k : 0f;
        }

        /// <summary>A creature a Quickdraw 4 line passed through after its first shows the weight at its share of the line (0 to 1).</summary>
        public static float PassShare(float share) => float.IsNaN(share) ? 0f : Clamp01(share);

        /// <summary>The quick window was missed (or the string relaxed) with the arrow still on it: how much of the ember is left
        /// <paramref name="age"/> seconds on (1 down to 0 over <paramref name="seconds"/>, eased), so the bow is seen to let the
        /// weight go. Gone at once with no time; a broken age is gone.</summary>
        public static float LetGo(float age, float seconds) {
            if (float.IsNaN(age) || !(seconds > 0f)) return 0f;
            if (age <= 0f) return 1f;
            float t = Clamp01(age / seconds);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>While it lets go the ember's ring opens: its size factor, 1 while all of it is <paramref name="left"/> (1), up to
        /// <paramref name="to"/> when nothing is. Never under 1; a broken number is 1.</summary>
        public static float Open(float left, float to) {
            if (float.IsNaN(to) || float.IsNaN(left) || !(to > 1f) || float.IsInfinity(to)) return 1f;
            return to + (1f - to) * Clamp01(left);
        }

        const float LineTintRise = 0.08f; // share of the line over which the weight comes in, from the start of its last third

        /// <summary>Quickdraw 4's line carries the weight on its last third: its width at <paramref name="u"/> (0 = the bow, 1 = its
        /// end) as a factor on the plain line, 1 up to <see cref="LineTintFrom"/>, then rising to 1 + <paramref name="perOrb"/> per
        /// Charge Shot orb (at most three). 1 with no orbs or a broken number.</summary>
        public static float LineWidth(float u, int orbs, float perOrb) {
            if (orbs <= 0 || !(perOrb > 0f) || float.IsInfinity(perOrb) || float.IsNaN(u) || u <= LineTintFrom) return 1f;
            float t = Clamp01((u - LineTintFrom) / LineTintRise);
            return 1f + perOrb * Math.Min(Complements.MaxOrbs, orbs) * (t * t * (3f - 2f * t));
        }
    }

    /// <summary>Remembered main paths: one <c>key=Quick|Multi|Charge</c> per line. Bad lines are ignored.</summary>
    internal sealed class PathStore {
        readonly Dictionary<string, MainPath> map = new Dictionary<string, MainPath>(StringComparer.Ordinal);

        public static PathStore Parse(string? text) {
            var store = new PathStore();
            if (string.IsNullOrEmpty(text)) return store;
            foreach (string raw in text!.Split('\n')) {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string key = raw.Substring(0, eq).Trim();
                MainPath value = ParseValue(raw.Substring(eq + 1).Trim());
                if (key.Length > 0 && value != MainPath.None) store.map[key] = value;
            }
            return store;
        }

        static MainPath ParseValue(string s) {
            foreach (MainPath p in new[] { MainPath.Quick, MainPath.Multi, MainPath.Charge })
                if (string.Equals(p.ToString(), s, StringComparison.OrdinalIgnoreCase)) return p;
            return MainPath.None;
        }

        public MainPath Get(string key) => key != null && map.TryGetValue(key, out MainPath p) ? p : MainPath.None;

        /// <summary>True when the stored value changed. <c>None</c> removes the key.</summary>
        public bool Set(string key, MainPath value) {
            if (string.IsNullOrEmpty(key)) return false;
            if (value == MainPath.None) return map.Remove(key);
            if (map.TryGetValue(key, out MainPath old) && old == value) return false;
            map[key] = value;
            return true;
        }

        public string Serialize() {
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string k in keys) sb.Append(k).Append('=').Append(map[k]).Append('\n');
            return sb.ToString();
        }
    }
}
