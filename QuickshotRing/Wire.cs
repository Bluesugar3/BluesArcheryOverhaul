using System;
using System.Collections.Generic;

// Unity-free. The shot claim: what the shooter says about its shot, as a block at the end of the shot
// message. Compiled by QuickshotRing.Tests, so only System.* in here.
namespace QuickshotRing {
    internal enum ShotKind { Early = 0, Snap = 1, Held = 2, Charged = 3 }

    internal sealed class ShotClaim {
        public ShotKind Kind;
        public bool Steered;
        /// <summary>H1 (Seeking Volley): how many of the first targets are locks (1 to 4); meaningful only when <see cref="Steered"/>.</summary>
        public int Locks;
        public int? TimeMs;
        public int Stage;
        public List<uint> Targets = new List<uint>();
        public List<int> Picks = new List<int>();
        /// <summary>Skyfall (admin skill): the shooter's request (Skyfall) or the server's relayed decision (Skyfall, Festival,
        /// Fizzled). Rides the window bits only when the claim goes out unsteered; None for every other claim.</summary>
        public SkyCode Sky;
        /// <summary>Skyfall: the sigil centre as three offsets from the release point (SkyfallRules.QuantizeOffset), in the mark
        /// slot; empty for every other claim.</summary>
        public List<uint> Marks = new List<uint>();

        /// <summary>The locks this claim says it has: 0 unsteered, else Locks clamped to 1..MaxLocks (an old steered claim with no
        /// count reads as one lock, which is what its window bits of 0 say).</summary>
        public int LockCount => !Steered ? 0 : Math.Max(1, Math.Min(ShotClaimWire.MaxLocks, Locks));

        public ShotClaim Copy() => new ShotClaim {
            Kind = Kind, Steered = Steered, Locks = Locks, TimeMs = TimeMs, Stage = Stage,
            Targets = new List<uint>(Targets), Picks = new List<int>(Picks),
            Sky = Sky, Marks = new List<uint>(Marks)
        };

        public override string ToString() =>
            $"{Kind} steer{(Steered ? 1 : 0)}{(Steered ? " locks" + LockCount : "")} t{(TimeMs.HasValue ? TimeMs.Value.ToString() : "?")}ms stage{Stage} " +
            $"targets[{string.Join(",", Targets)}] picks[{string.Join(",", Picks)}]" +
            (Sky != SkyCode.None ? " sky:" + Sky : "") +
            (Marks.Count > 0 ? $" marks[{string.Join(",", Marks)}]" : "");
    }

    /// <summary>The claim block, 32 to 278 bits: version 4, kind 2, window 2, steered 1, time 13, stage 5,
    /// target count 3, targets 17 each, picks 3 each (stage of them, only when there are targets),
    /// mark count 2, marks 17 each. The window (Snap Draw's) and the mark slot (Mark's) belonged to two orbs deleted in 0.16.0
    /// and stay in the layout. H1: with the steered bit set (Seeking Volley's lock), the window carries the number of locks
    /// less one (1 to 4 locks, the first targets in lock order). A steered claim with no target is written unsteered.
    /// Skyfall (admin skill): without the steered bit the window carries the claim's Skyfall code (<see cref="SkyCode"/>). It is
    /// 0 for every claim without Skyfall, as before: every sender since 0.16.0 writes 0 there, and before that only the deleted
    /// Snap Draw orb set it. The mark slot carries up to three 17-bit marks (Skyfall's sigil centre), all or none; its count is
    /// 0 for every claim without marks. Version and widths are unchanged, so an old reader ignores the code (it reads the
    /// window only with the steered bit) and skips the marks (17 bits each).</summary>
    internal static class ShotClaimWire {
        public const int Version = 1, MaxTargets = 6, MaxStage = 31, TimeBits = 13,
                         TimeUnknown = 8191, TimeMax = 8190, EntityBits = 17, PickNone = 7, MaxLocks = 4, MaxMarks = 3;

        /// <summary>Returns the number of bits written. Never changes <paramref name="claim"/>.</summary>
        public static int Write(IBitWriter w, ShotClaim claim) {
            uint idMax = (1u << EntityBits) - 1;
            int stage = Math.Max(0, Math.Min(MaxStage, claim.Stage));
            var targets = new List<uint>(); foreach (uint t in claim.Targets) if (t <= idMax && targets.Count < MaxTargets) targets.Add(t);
            int time = claim.TimeMs.HasValue ? Math.Max(0, Math.Min(TimeMax, claim.TimeMs.Value)) : TimeUnknown;
            int bits = 0;
            void Put(uint v, int n) { w.Write(v, n); bits += n; }

            bool steered = claim.Steered && targets.Count > 0;
            int locks = steered ? Math.Min(claim.LockCount, targets.Count) : 0;
            Put(Version, 4);
            Put((uint)claim.Kind & 3u, 2);
            Put(steered ? (uint)(locks - 1) : (uint)claim.Sky & 3u, 2);   // window: the locks less one when steered (H1), else the Skyfall code (0 without Skyfall, as before)
            Put(steered ? 1u : 0u, 1);
            Put((uint)time, TimeBits);
            Put((uint)stage, 5);
            Put((uint)targets.Count, 3);
            foreach (uint t in targets) Put(t, EntityBits);
            if (targets.Count > 0)
                for (int i = 0; i < stage; i++) {
                    int pick = i < claim.Picks.Count ? claim.Picks[i] : PickNone;
                    Put((uint)(pick >= 0 && pick < targets.Count ? pick : PickNone), 3);
                }
            int marks = Math.Min(MaxMarks, claim.Marks.Count);   // Skyfall's sigil centre: the first three, all or none
            for (int i = 0; i < marks; i++) if (claim.Marks[i] > idMax) { marks = 0; break; }
            Put((uint)marks, 2);
            for (int i = 0; i < marks; i++) Put(claim.Marks[i], EntityBits);
            return bits;
        }

        public static WireRead TryRead(IBitReader r, out ShotClaim? claim) {
            claim = null;
            if (r.Finished) return WireRead.Absent;
            uint version = r.Read(4);
            if (version == 0) return WireRead.Absent;
            if (version != Version) return WireRead.UnknownVersion;
            var c = new ShotClaim {
                Kind = (ShotKind)r.Read(2)
            };
            uint window = r.Read(2);   // the locks less one with the steered bit (H1); otherwise the Skyfall code
            bool steeredBit = r.Read(1) != 0;
            c.Steered = steeredBit;
            c.Locks = steeredBit ? (int)window + 1 : 0;
            c.Sky = steeredBit ? SkyCode.None : (SkyCode)window;   // from the raw bit: a steered block's window held locks, never a code
            uint time = r.Read(TimeBits);
            c.TimeMs = time == TimeUnknown ? (int?)null : (int)time;
            c.Stage = (int)r.Read(5);
            uint targetCount = r.Read(3);
            if (targetCount > MaxTargets) return WireRead.Malformed;
            for (uint i = 0; i < targetCount; i++) c.Targets.Add(r.Read(EntityBits));
            if (targetCount > 0)
                for (int i = 0; i < c.Stage; i++) c.Picks.Add((int)r.Read(3));
            uint markCount = r.Read(2);   // Skyfall's sigil centre (0 for every other claim)
            for (uint i = 0; i < markCount; i++) c.Marks.Add(r.Read(EntityBits));
            if (c.Steered && c.Targets.Count == 0) { c.Steered = false; c.Locks = 0; }   // a lock names a target, or there is none
            else if (c.Steered) c.Locks = Math.Min(c.Locks, c.Targets.Count);
            claim = c;
            return WireRead.Ok;
        }
    }
}
