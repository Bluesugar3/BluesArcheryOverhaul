using System;
using System.Collections.Generic;
using System.Globalization;

// Unity-free. Weight's carry and stagger ledger, Steady, the Fill's timeline and the Balance Ring's easing.
// Compiled by QuickshotRing.Tests, so only System.* in here.
namespace QuickshotRing {
    internal readonly struct WeightValues {
        public readonly float Charge1, Charge2, Charge3, GhostFactor;

        public WeightValues(float charge1, float charge2, float charge3, float ghostFactor) {
            Charge1 = charge1; Charge2 = charge2; Charge3 = charge3; GhostFactor = ghostFactor;
        }

        public static WeightValues Default => new WeightValues(0.2f, 0.4f, 0.6f, 0.5f);
    }

    /// <summary>What one release carries to the real arrow's hit: stagger, interrupt and the ghosts' share. (Charge Shot 3's pierce
    /// was removed in batch 4b; Quickdraw 4's line pierces instead, LinePierce in Rules.cs.)</summary>
    internal readonly struct WeightCarry {
        public readonly float Stagger;
        public readonly bool Interrupt;
        public readonly float GhostStaggerPerShare;
        /// <summary>A Multi main's gold ghosts: a flat stagger every ghost adds when it hits a creature.</summary>
        public readonly float StaggerPerGhost;

        public WeightCarry(float stagger, bool interrupt, float ghostStaggerPerShare, float staggerPerGhost = 0f) {
            Stagger = stagger; Interrupt = interrupt; GhostStaggerPerShare = ghostStaggerPerShare; StaggerPerGhost = staggerPerGhost;
        }

        public static WeightCarry None => default(WeightCarry);

        public bool Any => Stagger != 0f || Interrupt || GhostStaggerPerShare != 0f || StaggerPerGhost != 0f;

        /// <summary>Whether the release's spectral arrows can add stagger (a charged shot's ghost share, a Multi main's gold ghosts).
        /// Needles and plain pellets carry none.</summary>
        public bool AddsSpectralStagger => GhostStaggerPerShare > 0f || StaggerPerGhost > 0f;

        /// <summary>Whether the real arrow's stagger should wait in the ledger for this release's spectral arrows: only while they
        /// are still out and can add stagger, so a Quick main's snap stagger lands with the arrow's hit.</summary>
        public bool WaitsForSpectral(bool spectralOut) => spectralOut && AddsSpectralStagger;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "stagger {0:0.##} interrupt {1} ghost stagger {2:0.###}/share ghost stagger {3:0.###}/ghost",
            Stagger, Interrupt ? "yes" : "no", GhostStaggerPerShare, StaggerPerGhost);
    }

    internal static class WeightRules {
        public static float TierStagger(int tier, WeightValues v) => tier <= 0 ? 0f : tier == 1 ? v.Charge1 : tier == 2 ? v.Charge2 : v.Charge3;

        /// <summary>A snap (a Line too) carries the Quick main's Charge complement; a charged shot its tier's weight; a Multi
        /// main's held volley its gold ghosts' stagger per ghost.</summary>
        public static WeightCarry ForRelease(Resolution r, Build build, WeightValues v, ComplementValues c) {
            if (r.Ultimate == UltimateUse.Beam) return WeightCarry.None;
            if (r.Kind == ShotKind.Snap) return new WeightCarry(Complements.SnapStagger(build, c), Complements.SnapInterrupts(build, c), 0f);
            if (r.Kind == ShotKind.Charged && r.ChargedTier >= 1) {
                float s = TierStagger(r.ChargedTier, v);
                return new WeightCarry(s, true, s * v.GhostFactor);
            }
            if (r.Kind == ShotKind.Held && build.Main == MainPath.Multi) {
                float g = Complements.GhostStaggerAt(build, c, r.HoldStage); // G3: the hold stage reached, not the orbs owned
                if (g > 0f) return new WeightCarry(0f, false, 0f, g);
            }
            return WeightCarry.None;
        }

        public static float Cripple(float stagger, float unit) => Math.Max(0f, stagger * unit);

        public static bool Immune(float now, float lastKnockDown, float seconds) => !float.IsNaN(lastKnockDown) && now - lastKnockDown < seconds;

        public static bool MayInterrupt(float now, float lastInterrupt, float cooldown, float lastKnockDown, float immunity,
                                        bool cancellable, bool isWyrm, bool wyrmsAllowed, bool enabled) =>
            enabled && cancellable && (!isWyrm || wyrmsAllowed) && !Immune(now, lastKnockDown, immunity)
            && (float.IsNaN(lastInterrupt) || now - lastInterrupt >= cooldown);
    }

    internal readonly struct WeightDue {
        public readonly uint Release;
        public readonly int Creature;
        public readonly float Stagger;
        public readonly int Parts;

        public WeightDue(uint release, int creature, float stagger, int parts) { Release = release; Creature = creature; Stagger = stagger; Parts = parts; }
    }

    /// <summary>One cripple hit per creature per release: staggers add up and are dealt together.</summary>
    internal sealed class WeightLedger {
        public const int Cap = 256;

        sealed class Entry {
            public uint Release; public int Creature; public float Stagger; public int Parts; public float FirstAt; public bool Open;
        }

        // kept in the order the entries were first added, so the oldest is first
        readonly List<Entry> entries = new List<Entry>();

        public int Pending => entries.Count;

        public void Add(uint release, int creature, float stagger, float now, bool releaseOpen) {
            if (stagger <= 0f) return;
            foreach (var e in entries) {
                if (e.Release != release || e.Creature != creature) continue;
                e.Stagger += stagger; e.Parts++; e.Open |= releaseOpen;
                return;
            }
            if (entries.Count >= Cap) entries.RemoveAt(0);
            entries.Add(new Entry { Release = release, Creature = creature, Stagger = stagger, Parts = 1, FirstAt = now, Open = releaseOpen });
        }

        public List<WeightDue> TakeDue(float now, float batchSeconds, float maxWaitSeconds) {
            var due = new List<WeightDue>();
            TakeDue(now, batchSeconds, maxWaitSeconds, due);
            return due;
        }

        /// <summary>G2: as above, into <paramref name="due"/> (cleared first), so the per-frame caller keeps one list.</summary>
        public void TakeDue(float now, float batchSeconds, float maxWaitSeconds, List<WeightDue> due) {
            due.Clear();
            for (int i = 0; i < entries.Count; i++) {
                var e = entries[i];
                float wait = e.Open ? maxWaitSeconds : batchSeconds;
                if (now - e.FirstAt < wait - 1e-6f) continue;
                due.Add(new WeightDue(e.Release, e.Creature, e.Stagger, e.Parts));
                entries.RemoveAt(i--);
            }
        }

        public List<WeightDue> TakeRelease(uint release) {
            var due = new List<WeightDue>();
            for (int i = 0; i < entries.Count; i++) {
                var e = entries[i];
                if (e.Release != release) continue;
                due.Add(new WeightDue(e.Release, e.Creature, e.Stagger, e.Parts));
                entries.RemoveAt(i--);
            }
            return due;
        }
    }

    internal readonly struct SteadyValues {
        public readonly float Wobble1, Wobble2, Wobble3, Speed1, Speed2, Speed3;

        public SteadyValues(float wobble1, float wobble2, float wobble3, float speed1, float speed2, float speed3) {
            Wobble1 = wobble1; Wobble2 = wobble2; Wobble3 = wobble3; Speed1 = speed1; Speed2 = speed2; Speed3 = speed3;
        }

        public static SteadyValues Default => new SteadyValues(0.75f, 0.5f, 0.35f, 0.10f, 0.20f, 0.30f);
    }

    internal static class SteadyRule {
        static int Tier(int t) => t < 0 ? 0 : t > 3 ? 3 : t;

        public static float Wobble(int quickTier, SteadyValues s) {
            int t = Tier(quickTier);
            return t == 0 ? 1f : t == 1 ? s.Wobble1 : t == 2 ? s.Wobble2 : s.Wobble3;
        }

        /// <summary>The Quick tier whose wobble modifier a release flies with: a Quick main (or no main path) on every shot as
        /// before, a Charge main on its charged shots (its Quick complement), a Multi main never (0).</summary>
        public static int WobbleTier(Build b, ShotKind kind) {
            switch (b.Main) {
                case MainPath.Multi: return 0;
                case MainPath.Charge: return kind == ShotKind.Charged ? Tier(b.Quick) : 0;
                default: return Tier(b.Quick);
            }
        }

        public static float SnapSpeed(int quickTier, ShotKind kind, UltimateUse ultimate, SteadyValues s) {
            if (kind != ShotKind.Snap || ultimate == UltimateUse.Line) return 1f;
            int t = Tier(quickTier);
            return 1f + (t == 0 ? 0f : t == 1 ? s.Speed1 : t == 2 ? s.Speed2 : s.Speed3);
        }

        public static float Boost(float speed, float factor, float max) => Math.Min(speed * factor, max);
    }

    /// <summary>Seconds after the 68% crossing at which each Charge orb's stage completes (stage 1 is the game's own Slowdraw timer).</summary>
    internal readonly struct ChargeStages {
        public readonly float S1, S2, S3;

        public ChargeStages(float s1, float s2, float s3) { S1 = s1; S2 = Math.Max(s1, s2); S3 = Math.Max(S2, s3); }

        public static ChargeStages Default => new ChargeStages(1f, 1.75f, 2.5f);

        /// <summary>Every stage time times <paramref name="factor"/> (a Charge main's Quick complement).</summary>
        public ChargeStages Scaled(float factor) => new ChargeStages(S1 * factor, S2 * factor, S3 * factor);

        /// <summary>When stage k completes; 0 for k below 1, stage 3's time above it.</summary>
        public float At(int k) => k <= 0 ? 0f : k == 1 ? S1 : k == 2 ? S2 : S3;

        /// <summary>The charge tier a charged release carries: one per owned Charge orb whose stage time has passed, at least 1
        /// (the game said charged). An unknown time gives every owned orb, as before stages existed.</summary>
        public int Tier(int owned, float? seconds) {
            if (owned <= 1 || !seconds.HasValue) return Math.Max(1, owned);
            int tier = 1;
            for (int k = 2; k <= Math.Min(3, owned); k++) if (seconds.Value + 1e-4f >= At(k)) tier = k;
            return tier;
        }

        /// <summary>How far stage k's strand has wound on at time t (0..1), each over its own stretch of the timeline.</summary>
        public float Progress(int k, float t) {
            float from = At(k - 1), to = At(k);
            return to <= from ? (t >= to ? 1f : 0f) : Math.Max(0f, Math.Min(1f, (t - from) / (to - from)));
        }
    }

    /// <summary>Play-test 7 balance: the real arrow's damage multiplier by the charge stage reached ([Charge] Stage1Damage, Stage2Damage,
    /// Stage3Damage, shared), a mod-owned table instead of Slowdraw's own x1.25 / x1.5 / x2. Only the real arrow of a main path's
    /// charged release (and Shatter's blast, which is a share of that hit) uses it; the ghosts, Holy Arrow, heavy arrows and the
    /// tablet's old numbers keep the game's table.</summary>
    internal readonly struct ChargeDamage {
        public readonly float S1, S2, S3;
        public ChargeDamage(float s1, float s2, float s3) { S1 = s1; S2 = s2; S3 = s3; }
        public static ChargeDamage Default => new ChargeDamage(2f, 3.5f, 6f);

        static float Sane(float v) => v > 0f && !float.IsInfinity(v) ? v : 1f;

        /// <summary>The multiplier at a stage: 1 below stage 1, stage 3's above it; a broken value is x1.</summary>
        public float At(int tier) => tier <= 0 ? 1f : Sane(tier == 1 ? S1 : tier == 2 ? S2 : S3);
    }

    internal static class FillTimeline {
        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        public static float Grown(float t, float thumpAt) => thumpAt <= 0f ? 1f : Clamp01(t / thumpAt);

        public static float Squeeze(float gather, float fraction, float coreFraction) => fraction - (fraction - coreFraction) * Clamp01(gather);
    }

    /// <summary>The charge strands' shape along their length (s = 0 at the hand end, 1 at the head), their pulse, their
    /// per-stage brightness and the shaft radius they wind around. Unity-free (ChargeFx draws them).</summary>
    internal static class StrandShape {
        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        static float Smooth(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Width factor: rises 0 to 1 over the first taperShare, falls to 0 over the last; 1 everywhere with 0.</summary>
        public static float Taper(float s, float taperShare) {
            float share = taperShare < 0f ? 0f : taperShare > 0.5f ? 0.5f : taperShare;
            if (share <= 0f) return 1f;
            s = Clamp01(s);
            return Smooth(Math.Min(s, 1f - s) / share);
        }

        /// <summary>Radius factor: 1 until s = 1 - fromShare, then eases to 0 at the head.</summary>
        public static float Converge(float s, float fromShare) {
            if (fromShare <= 0f) return 1f;
            float share = Math.Min(1f, fromShare), start = 1f - share;
            if (s <= start) return 1f;
            return 1f - Smooth((s - start) / share);
        }

        /// <summary>Brightness added at s by a bump of half-width <paramref name="width"/> that travels hand to head in
        /// <paramref name="seconds"/> after a thump; 0 before the thump, once it has passed, and with seconds &lt;= 0.</summary>
        public static float Pulse(float s, float sinceThump, float seconds, float width) {
            if (seconds <= 0f || width <= 0f || sinceThump < 0f) return 0f;
            float d = Math.Abs(s - sinceThump / seconds);
            if (d >= width) return 0f;
            return 0.5f * (1f + (float)Math.Cos(Math.PI * d / width));
        }

        public static float StageBrightness(int stagesDone, float perStage) => 1f + perStage * Math.Max(0, stagesDone - 1);

        /// <summary>A strand's distance from the shaft's axis at s: the gap closes onto the shaft's surface at the head, so the
        /// strand touches the shaft there and never sinks inside it.</summary>
        public static float Radius(float shaft, float gap, float s, float convergeShare) =>
            shaft + Math.Max(0f, gap) * Converge(s, convergeShare);

        /// <summary>How far behind the tip the strands' back end sits: <paramref name="squeeze"/> (0..1; not a number = 1) of
        /// the whole <paramref name="length"/>, but never closer to the tip than the head gap plus <paramref name="min"/>,
        /// so a full squeeze still leaves a short forward-running helix (no reversed or zero-length line).</summary>
        public static float Back(float length, float squeeze, float headGap, float min) {
            float k = float.IsNaN(squeeze) ? 1f : Clamp01(squeeze);
            return Math.Max(length * k, Math.Max(0f, headGap) + Math.Max(0f, min));
        }

        /// <summary>[Fill] GlowScale as a factor: never negative, 1 when it is not a number.</summary>
        public static float Glow(float scale) => float.IsNaN(scale) ? 1f : Math.Max(0f, scale);

        /// <summary>The smallest positive finite cross-size, halved, at most <paramref name="max"/>; fallback when there is none.</summary>
        public static float ShaftRadius(float[]? crossSizes, float fallback, float max) {
            if (crossSizes == null) return fallback;
            float best = float.MaxValue;
            foreach (float x in crossSizes)
                if (x > 0f && !float.IsNaN(x) && !float.IsInfinity(x) && x < best) best = x;
            if (best == float.MaxValue) return fallback;
            return Math.Min(best * 0.5f, max);
        }
    }

    /// <summary>Holy Arrow's wind-up on a Charge main's held arrow, from stage 3 until Holy Arrow is ready and on until the
    /// release: the colour shift, the motes along the strands, the strands braiding into one cord at the head, the armed
    /// ring's breathing. Unity-free (ChargeFx.HolyWindUp draws it).</summary>
    internal static class WindUp {
        const float CordEase = 0.25f; // share of the cord's stretch over which the gap starts closing (no kink at its back end)

        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        static float Smooth(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>0 at stage 3's time, 1 at Holy Arrow's, clamped; 1 when Holy Arrow is not later than stage 3.</summary>
        public static float Shift(float t, float stage3At, float holyAt) =>
            holyAt <= stage3At ? 1f : Clamp01((t - stage3At) / (holyAt - stage3At));

        /// <summary>The motes' speed in shaft-lengths per second: slow at stage 3, rising with the square of the shift.</summary>
        public static float MoteSpeed(float shift, float slow, float fast) {
            float s = Clamp01(shift);
            return slow + (fast - slow) * s * s;
        }

        /// <summary>How far the three strands have merged into one cord at the head (0..1): the gather, eased.</summary>
        public static float Braid(float gather) => Smooth(gather);

        /// <summary>The share of their full span (hand to head) the strands still cover: all of it until the gather, down to
        /// <paramref name="to"/> when Holy Arrow is ready, in step with the core line's squeeze (FillTimeline.Squeeze). The
        /// helix keeps its turns, so it packs denser into the front of the arrow. A share that is not a number leaves the
        /// strands alone; it never lengthens them.</summary>
        public static float Span(float gather, float to) {
            if (float.IsNaN(to) || float.IsNaN(gather)) return 1f;
            return FillTimeline.Squeeze(gather, 1f, Clamp01(to));
        }

        /// <summary>The factor on a strand's gap at s (0 = hand, 1 = head): 1 behind the front <paramref name="share"/> of
        /// the strand, then 1 - braid x depth once into it (eased in over its first quarter). Only the gap closes, so the
        /// strands wind onto the shaft's surface (StrandShape.Radius) and never sink inside it.</summary>
        public static float CordGap(float s, float braid, float share, float depth) {
            float sh = Clamp01(share);
            if (sh <= 0f) return 1f;
            float w = Smooth((s - (1f - sh)) / (sh * CordEase));
            return 1f - Clamp01(braid) * Clamp01(depth) * w;
        }

        /// <summary>Mote i of count's place along the strand (0 = hand, 1 = head) after <paramref name="travel"/>
        /// shaft-lengths: the motes start evenly spread and wrap round to the hand end.</summary>
        public static float MotePhase(float travel, int i, int count) {
            float x = travel + (count > 0 ? (float)i / count : 0f);
            return x - (float)Math.Floor(x);
        }

        /// <summary>How many of count motes reached the head (wrapped) while the travel went from before to after.</summary>
        public static int Arrivals(float before, float after, int count) {
            if (after <= before || count <= 0) return 0;
            int n = 0;
            for (int i = 0; i < count; i++) {
                float o = (float)i / count;
                n += (int)(Math.Floor(after + o) - Math.Floor(before + o));
            }
            return n;
        }

        /// <summary>0 at stage 3, 1 after <paramref name="seconds"/> (the motes' fade-in); a step with seconds &lt;= 0.</summary>
        public static float FadeIn(float t, float stage3At, float seconds) =>
            seconds <= 0f ? (t >= stage3At ? 1f : 0f) : Clamp01((t - stage3At) / seconds);

        /// <summary>A value swinging between low and high at hz, starting halfway and rising; high when hz &lt;= 0.</summary>
        public static float Breathe(float since, float hz, float low, float high) {
            if (hz <= 0f) return high;
            return low + (high - low) * 0.5f * (1f + (float)Math.Sin(2.0 * Math.PI * hz * since));
        }
    }

    internal static class BalanceRule {
        public static float Drained(float value, float min, float max) =>
            max <= min ? 0f : Math.Max(0f, Math.Min(1f, (max - value) / (max - min)));

        public static float Ease(float shown, float target, float dt, float rate) {
            float r = target + (shown - target) * (float)Math.Exp(-rate * dt);
            float lo = Math.Min(shown, target), hi = Math.Max(shown, target);
            return Math.Max(lo, Math.Min(hi, r));
        }

        public static float Alpha(float now, float lastHit, float holdSeconds, float fadeSeconds) {
            float late = now - lastHit;
            if (late <= holdSeconds) return 1f;
            if (fadeSeconds <= 0f) return 0f;
            return 1f - Math.Max(0f, Math.Min(1f, (late - holdSeconds) / fadeSeconds));
        }
    }
}
