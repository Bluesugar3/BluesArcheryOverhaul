using System;
using System.Collections.Generic;

// Unity-free. The archery build (which orbs a player owns, boiled down to what the wire and the rules
// need) and its 32-bit block. Compiled by QuickshotRing.Tests, so only System.* in here.
namespace QuickshotRing {
    /// <summary>Writes bits into a message. <c>bits</c> is 1..32: the real adapter maps onto
    /// <c>Stream.SerializeBits</c>, and the game's BitWriter never takes more than 32 bits.</summary>
    internal interface IBitWriter {
        void Write(uint value, int bits);
    }

    /// <summary>Reads bits from one message. <c>bits</c> is 1..32 (same contract as <see cref="IBitWriter"/>).
    /// Reading past the end returns zeros. <c>Finished</c> is true when fewer than 8 bits remain, like the
    /// game's StreamReader.IsFinished. <c>BitsLeft</c> is never negative.</summary>
    internal interface IBitReader {
        uint Read(int bits);
        bool Finished { get; }
        int BitsLeft { get; }
    }

    internal enum WireRead { Absent, Ok, UnknownVersion, Malformed }

    /// <summary>The archery orbs. No number of this enum is stored or sent anywhere (saves hold slot ids, the wire holds the
    /// flag enums below); only the order of the members matters (the play-test kit's BuyRule sells the later one first on a tie).</summary>
    internal enum Orb {
        None, Quick1, Quick2, Quick3, Quickdraw4, AutoDock, Volley, FullVolley, Burst,
        SeekingVolley, Ricochet, Shatter, Charge1, Charge2, Charge3, HolyArrow,
        Gatherer
    }

    [Flags] internal enum Ultimates { None = 0, Quickdraw4 = 1, SeekingVolley = 2, HolyArrow = 4 }
    /// <summary>6 bits on the wire. Bits 1, 2, 4 (Snap Draw, Mark, Converge, deleted in 0.16.0) and 16, 32 (two removed
    /// Charge-line side orbs) are reserved: never set, and <see cref="BuildBits.TryRead"/> drops them.</summary>
    [Flags] internal enum SideOrbs { None = 0, Ricochet = 8 }
    /// <summary>4 bits on the wire. Bits 1, 2, 4 (Echo, Tracer, Gold Volley, deleted in 0.16.0) are reserved: never set, and
    /// <see cref="BuildBits.TryRead"/> drops them. Shatter keeps bit 8 (it was a bridge once: a bit is only a bit).</summary>
    [Flags] internal enum Bridges { None = 0, Shatter = 8 }

    internal readonly struct Build : IEquatable<Build> {
        public readonly int Quick, Volley, Charge;
        public readonly Ultimates Ultimates;
        public readonly SideOrbs Sides;
        public readonly Bridges Bridges;
        public readonly bool QuiverGather;
        public readonly MainPath Main;

        public Build(int quick, int volley, int charge, Ultimates ultimates, SideOrbs sides, Bridges bridges, bool quiverGather, MainPath main = MainPath.None) {
            Quick = Clamp3(quick); Volley = Clamp3(volley); Charge = Clamp3(charge);
            Ultimates = ultimates; Sides = sides; Bridges = bridges; QuiverGather = quiverGather; Main = main;
        }

        public Build With(MainPath main) => new Build(Quick, Volley, Charge, Ultimates, Sides, Bridges, QuiverGather, main);

        static int Clamp3(int v) => v < 0 ? 0 : v > 3 ? 3 : v;

        /// <summary>Set semantics: a duplicate counts once; AutoDock (only the server acts on it, no bit) and None are ignored. H3 section 6
        /// (the owner: "Combine gather and quiver plus the quiver gather into one skill"): the one gatherer orb, the game's Arrow Gatherer,
        /// also gives what Quiver Gather did, so it sets the QuiverGather flag (the wire bit keeps its place and meaning: quiver return on).</summary>
        public static Build From(IEnumerable<Orb> owned) {
            var set = new HashSet<Orb>(owned);
            int Count(params Orb[] orbs) { int n = 0; foreach (var o in orbs) if (set.Contains(o)) n++; return n; }
            var ult = Ultimates.None; var side = SideOrbs.None; var bridge = Bridges.None;
            if (set.Contains(Orb.Quickdraw4)) ult |= Ultimates.Quickdraw4;
            if (set.Contains(Orb.SeekingVolley)) ult |= Ultimates.SeekingVolley;
            if (set.Contains(Orb.HolyArrow)) ult |= Ultimates.HolyArrow;
            if (set.Contains(Orb.Ricochet)) side |= SideOrbs.Ricochet;
            if (set.Contains(Orb.Shatter)) bridge |= Bridges.Shatter;
            return new Build(Count(Orb.Quick1, Orb.Quick2, Orb.Quick3), Count(Orb.Volley, Orb.FullVolley, Orb.Burst),
                             Count(Orb.Charge1, Orb.Charge2, Orb.Charge3), ult, side, bridge, set.Contains(Orb.Gatherer));
        }

        static string Names(string[] names, int bits) {
            var parts = new List<string>();
            for (int i = 0; i < names.Length; i++) if ((bits & (1 << i)) != 0) parts.Add(names[i]);
            return parts.Count == 0 ? "-" : string.Join("+", parts);
        }

        public override string ToString() =>
            $"Q{Quick} V{Volley} C{Charge} ult:{Names(new[] { "Q4", "Seek", "Holy" }, (int)Ultimates)} " +
            $"side:{((Sides & SideOrbs.Ricochet) != 0 ? "Ricochet" : "-")} " +
            $"bridge:{((Bridges & Bridges.Shatter) != 0 ? "Shatter" : "-")} quiver:{(QuiverGather ? "yes" : "no")} main:{Main}";

        public bool Equals(Build o) => Quick == o.Quick && Volley == o.Volley && Charge == o.Charge && Ultimates == o.Ultimates
                                       && Sides == o.Sides && Bridges == o.Bridges && QuiverGather == o.QuiverGather && Main == o.Main;
        public override bool Equals(object? obj) => obj is Build b && Equals(b);
        public override int GetHashCode() => (((((Quick * 4 + Volley) * 4 + Charge) * 8 + (int)Ultimates) * 64 + (int)Sides) * 32 + (int)Bridges * 2 + (QuiverGather ? 1 : 0)) * 4 + (int)Main;
        public static bool operator ==(Build a, Build b) => a.Equals(b);
        public static bool operator !=(Build a, Build b) => !a.Equals(b);
    }

    /// <summary>The 32-bit build block at the end of a bow status message. Version 2: version 4, quick 2, volley 2,
    /// charge 2, ultimates 3, side orbs 6, bridges 4, quiver gather 1, main path 2, settings check 6.
    /// Version 1 (read only): the same without main path and with an 8-bit settings check.</summary>
    internal static class BuildBits {
        public const int Version = 2;
        public const int Size = 32;
        /// <summary>The settings check is 6 bits on the wire: callers comparing it apply this to both sides.</summary>
        public const int CheckMask = 0x3F;
        /// <summary>Side-orb bits 1, 2, 4 (deleted in 0.16.0) and 16, 32 (removed after 0.13.0): reserved for good, never an enum member.</summary>
        public const uint ReservedSides = 0x37;
        /// <summary>Bridge bits 1, 2, 4 (deleted in 0.16.0): reserved for good, never an enum member.</summary>
        public const uint ReservedBridges = 0x7;
        /// <summary>The bits in use: every member of the enum; anything else (the reserved bits) is dropped on read.</summary>
        static readonly uint KnownSides = AllBits(typeof(SideOrbs)) & ~ReservedSides, KnownBridges = AllBits(typeof(Bridges)) & ~ReservedBridges;

        static uint AllBits(Type flags) {
            uint all = 0;
            foreach (object s in Enum.GetValues(flags)) all |= Convert.ToUInt32(s);
            return all;
        }

        public static void Write(IBitWriter w, Build build, byte settingsCheck) {
            w.Write(Version, 4);
            w.Write((uint)build.Quick, 2);
            w.Write((uint)build.Volley, 2);
            w.Write((uint)build.Charge, 2);
            w.Write((uint)build.Ultimates, 3);
            w.Write((uint)build.Sides, 6);
            w.Write((uint)build.Bridges, 4);
            w.Write(build.QuiverGather ? 1u : 0u, 1);
            w.Write((uint)build.Main, 2);
            w.Write((uint)(settingsCheck & CheckMask), 6);
        }

        public static WireRead TryRead(IBitReader r, out Build build, out byte settingsCheck) {
            build = default; settingsCheck = 0;
            if (r.Finished) return WireRead.Absent;
            uint version = r.Read(4);
            if (version == 0) return WireRead.Absent;
            if (version != Version && version != 1) return WireRead.UnknownVersion;
            int quick = (int)r.Read(2), volley = (int)r.Read(2), charge = (int)r.Read(2);
            var ult = (Ultimates)r.Read(3);
            var side = (SideOrbs)(r.Read(6) & KnownSides); // reserved bits dropped
            var bridge = (Bridges)(r.Read(4) & KnownBridges);
            bool quiver = r.Read(1) != 0;
            if (version == 1) {
                settingsCheck = (byte)r.Read(8);
                build = new Build(quick, volley, charge, ult, side, bridge, quiver);
                build = build.With(MainPathRule.Decide(build, MainPath.None));
                return WireRead.Ok;
            }
            var main = (MainPath)r.Read(2);
            settingsCheck = (byte)r.Read(6);
            build = new Build(quick, volley, charge, ult, side, bridge, quiver, main);
            return WireRead.Ok;
        }
    }

    /// <summary>Ids the mod gives its orbs and skills. Saves store slot ids, so these are fixed forever once shipped.
    /// Every game slot id read in play-test 1 is below 65536, so the new ones are all above 65535.</summary>
    internal static class SlotIds {
        public const uint Quickdraw4 = 2718281828u;   // unchanged since 0.3.0 so an already-unlocked orb survives
        public const uint QuiverGather = 1618033988u;
        public const int QuiverSkill = 1414213562;
        /// <summary>The slots of the six parked orbs deleted in 0.16.0 (Snap Draw, Mark, Echo, Tracer, Converge, Gold Volley). A save
        /// that still holds one is handed back 1 skill point for it when it loads (Tree.OffOrbRefund).</summary>
        public static readonly uint[] DeletedSlots = { 1732050807u, 2236067977u, 2645751311u, 3162277660u, 3741657386u, 4123105625u, QuiverGather };
        // Reserved, never reuse: slots 1259921049 and 1442249570 and skills 1189207115 and 1316074013 belonged to two side
        // orbs of the Charge line removed after 0.13.0 (saves may still hold the slot hashes; the game skips them with a
        // warning); then the six DeletedSlots and their skills 1148698354, 1245730939, 1122462048, 1200936955, 1430969081,
        // 1307660486. The tree refuses (error log, orb left out) any slot or runtime skill id in these lists.
        // H3 section 6: the Quiver Gather slot and skill joined both lists
        public static readonly uint[] ReservedSlots = { 1259921049u, 1442249570u, 1732050807u, 2236067977u, 2645751311u, 3162277660u, 3741657386u, 4123105625u, QuiverGather };
        public static readonly int[] ReservedSkills = { 1189207115, 1316074013, 1148698354, 1245730939, 1122462048, 1200936955, 1430969081, 1307660486, QuiverSkill };
        /// <summary>The Volley skill's id (at most 65535). The game's own id field is <c>protected int hash</c>:
        /// set it with <c>Convert.ChangeType</c>, never by passing this uint.</summary>
        public const uint VolleySkill = 47011u;
        /// <summary>The points a save gets back at load for a deleted slot it still holds (Tree.OffOrbRefund); 0 for any other slot.
        /// H4 (H23-review-opus minor 6): the Quiver Gather slot cost what the Arrow Gatherer costs (Tree.AddSlot copied the cost of the
        /// slot it hangs off), so that comes back (1 when it cannot be read); the six orbs deleted in 0.16.0 cost 1.</summary>
        public static uint DeletedRefund(uint hash, uint gathererCost) =>
            hash == QuiverGather ? (gathererCost > 0u ? gathererCost : 1u) : Array.IndexOf(DeletedSlots, hash) >= 0 ? 1u : 0u;

        /// <summary>H4: unspent points at or above the game's banked-points cap (0 = no cap): the game pays that path no experience
        /// until some are spent. A refund is the player's point and is paid anyway; this says so.</summary>
        public static bool AtBankedCap(uint points, uint cap) => cap > 0u && points >= cap;

        public const uint Burst = 3316624790u, SeekingVolley = 3605551275u, Ricochet = 3872983346u, Shatter = 4242640687u, AutoDock = 2449489743u;

        /// <summary>The slot id of each orb this mod's later steps add (only Burst enters the tree in 0.8.0).</summary>
        public static readonly IReadOnlyDictionary<Orb, uint> New = new Dictionary<Orb, uint> {
            { Orb.Burst, Burst }, { Orb.SeekingVolley, SeekingVolley }, { Orb.Ricochet, Ricochet }, { Orb.Shatter, Shatter }, { Orb.AutoDock, AutoDock },
        };

        /// <summary>The runtime skill behind each side orb (Quiver Gather's pattern): fixed forever once shipped,
        /// below int.MaxValue (the game's id field is an int). Seeking Volley has none: it is the Volley skill's fourth level.</summary>
        public static readonly IReadOnlyDictionary<Orb, int> Skills = new Dictionary<Orb, int> {
            { Orb.AutoDock, 1379729661 }, { Orb.Ricochet, 1475773161 }, { Orb.Shatter, 1348006155 },
        };

    }

    /// <summary>How the build is made from what a player owns, the orb being tried out and the play-test kit's lent and hidden
    /// orbs (BluesArcheryTestKit, through KitHooks; none without it).</summary>
    internal static class TestBuildRule {
        /// <summary>The build the mod works with: owned orbs, then <paramref name="practising"/> (unless None), then <paramref name="extra"/>,
        /// each once, minus every orb in <paramref name="without"/>.</summary>
        public static List<Orb> Combine(IEnumerable<Orb> owned, Orb practising, IEnumerable<Orb> extra, IEnumerable<Orb> without) {
            var list = new List<Orb>();
            foreach (Orb o in owned) if (!list.Contains(o)) list.Add(o);
            if (practising != Orb.None && !list.Contains(practising)) list.Add(practising);
            foreach (Orb o in extra) if (!list.Contains(o)) list.Add(o);
            foreach (Orb o in without) list.Remove(o);
            return list;
        }
    }

    internal static class SkillLevels {
        /// <summary>Skill ids with a positive level before and none (or 0) after, ascending.</summary>
        public static List<uint> Dropped(IReadOnlyDictionary<uint, int> before, IReadOnlyDictionary<uint, int> after) {
            var dropped = new List<uint>();
            foreach (KeyValuePair<uint, int> p in before)
                if (p.Value > 0 && (!after.TryGetValue(p.Key, out int now) || now <= 0)) dropped.Add(p.Key);
            dropped.Sort();
            return dropped;
        }
    }
}
