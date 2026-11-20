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
    /// flag enums below); only the order of the members matters (BuyRule sells the later one first on a tie).</summary>
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

    /// <summary>The server-only test build: <c>[TestBuild] Player</c> and <c>[TestBuild] Orbs</c>.</summary>
    internal static class TestBuildRule {
        /// <summary>Orb names separated by ';', trimmed, case-insensitive, each once. Names that are not an orb
        /// (and "None") go to <paramref name="unknown"/>, trimmed, as written.</summary>
        public static List<Orb> Parse(string? text, out List<string> unknown) {
            var orbs = new List<Orb>();
            unknown = new List<string>();
            if (string.IsNullOrEmpty(text)) return orbs;
            foreach (string raw in text!.Split(';')) {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                Orb found = Orb.None;
                foreach (Orb o in (Orb[])Enum.GetValues(typeof(Orb)))
                    if (o != Orb.None && string.Equals(o.ToString(), name, StringComparison.OrdinalIgnoreCase)) { found = o; break; }
                // H3 section 6: Quiver Gather is part of the one gatherer orb now; old lists that name it keep working
                if (found == Orb.None && string.Equals(name, "QuiverGather", StringComparison.OrdinalIgnoreCase)) found = Orb.Gatherer;
                if (found == Orb.None) unknown.Add(name);
                else if (!orbs.Contains(found)) orbs.Add(found);
            }
            return orbs;
        }

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

        /// <summary>True when the configured username names this player (case-insensitive). Empty = off.</summary>
        public static bool AppliesTo(string? configured, string? player) {
            string c = (configured ?? "").Trim();
            return c.Length > 0 && player != null && string.Equals(c, player.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>G3 (play-test 8, diagnosis 9): [TestBuild] FreePoints as topped up. AT or above the game's banked-points cap the
        /// game stops paying that path experience at all (PathExperience.GainExperienceUnmultiplied: freePoints >= MaxFreePoints;
        /// fix1, final-G34 M-1), so the top-up stops at <paramref name="cap"/> - 1 (0 = no cap); 0 or less is off.</summary>
        public static int TopUpTo(int want, uint cap) => want <= 0 ? 0 : cap > 0u && want >= cap ? (int)(cap - 1u) : want;
    }

    /// <summary>The button window's one-shot requests: <c>[TestBuild] BuyRequest</c>, <c>SpawnRequest</c>, <c>ClearRequest</c>.</summary>
    internal static class TestRequest {
        /// <summary>True when <paramref name="now"/> is a request: not empty and not the number last acted on (or the one the
        /// server started with), both trimmed. So a restart never re-applies an old request and every press is one.</summary>
        public static bool IsNew(string? last, string? now) {
            string n = (now ?? "").Trim();
            return n.Length > 0 && n != (last ?? "").Trim();
        }
    }

    /// <summary>What the server knows about the named player at the moment a buy request is seen (fix batch D, fixB-review C-1).
    /// A null means "cannot tell", which refuses as surely as false.</summary>
    internal struct BuyGateFacts {
        public bool Named;              // [TestBuild] Player is not empty
        public bool Found;              // a player of that name is on the server now
        public bool? Started;           // the game has read their save (Player.StartUp ran: LoadFrom)
        public bool HasController;      // they have a body (PlayerController): not joining, not leaving
        public bool? StateLoaded;       // their player state is loaded and savable (not None, not character customization)
        public bool TreeReady;          // the archery tree is in the game
        public bool RangedExperience;   // their Ranged path experience exists
        public bool SlotData;           // and its owned-slot list exists
        public int UnknownNames;        // names in [TestBuild] Buy that are not orbs
    }

    /// <summary>Fix batch D (fixB-review C-1, the controller's ruling): a buy acts only on a player who is online AND fully
    /// loaded at the moment the request is seen; anything uncertain refuses it, and a refused request is dropped (never waits).</summary>
    internal static class BuyGate {
        /// <summary>Why the request is refused, or null to act on it.</summary>
        public static string? Refusal(BuyGateFacts f) {
            if (!f.Named) return "[TestBuild] Player is empty";
            if (f.UnknownNames > 0) return $"{f.UnknownNames} unknown orb name(s) in [TestBuild] Buy (nothing is bought or sold for a typo)";
            if (!f.Found) return "the player is not online";
            if (f.Started != true) return f.Started == null ? "cannot tell whether the player's save has been read" : "the player's save has not been read yet (still joining)";
            if (!f.HasController) return "the player has no body right now (joining or leaving)";
            if (f.StateLoaded != true) return f.StateLoaded == null ? "cannot tell the player's state" : "the player's state is not loaded or not savable";
            if (!f.TreeReady) return "the archery tree is not in the game yet";
            if (!f.RangedExperience) return "the player has no Ranged experience data";
            if (!f.SlotData) return "the player's owned orbs cannot be read";
            return null;
        }
    }

    /// <summary>What a buy request does to one player's archery orbs (<see cref="BuyRule.Plan"/>).</summary>
    internal sealed class BuyPlan {
        /// <summary>The orbs to sell, in this order: leaves first (no orb still to be sold needs it).</summary>
        public readonly List<Orb> Sell = new List<Orb>();
        /// <summary>Owned orbs the game removes by itself when a sold parent was their last owned parent (bought again if listed).</summary>
        public readonly List<Orb> RemovedWith = new List<Orb>();
        /// <summary>The orbs to buy, in this order: each one's parent is owned by then; the list's order among the ready ones.</summary>
        public readonly List<Orb> Buy = new List<Orb>();
        /// <summary>Owned before and after, untouched (listed and owned, or a gatherer not listed), in orb order.</summary>
        public readonly List<Orb> Kept = new List<Orb>();
        /// <summary>Listed orbs that are not bought, in the list's order, with the reason.</summary>
        public readonly List<KeyValuePair<Orb, string>> LeftOut = new List<KeyValuePair<Orb, string>>();
        /// <summary>Free slots under the cap once the sales are done (int.MaxValue with no cap).</summary>
        public int Room;
    }

    /// <summary>The test build's real purchase: make the player own exactly the listed archery orbs.</summary>
    internal static class BuyRule {
        /// <summary>Never sold unless listed: the gatherer orbs are not part of any test.</summary>
        public static readonly Orb[] KeepUnlessListed = { Orb.Gatherer };   // H3 section 6: the one gatherer orb

        static string Names(IEnumerable<Orb> orbs) => string.Join(" or ", new List<Orb>(orbs).ConvertAll(o => o.ToString()).ToArray());

        /// <summary><paramref name="deps"/>: every archery orb in the shrine and its parents ("any of"; no parents = free to buy;
        /// Orb.None stands for a parent that is not an archery orb and is never owned). <paramref name="cap"/>: the game's slot
        /// cap over every profession (below 1 = none); <paramref name="usedElsewhere"/>: the slots the player owns that are not
        /// archery orbs. Rules: an orb not in <paramref name="deps"/> is left out; only the first listed ultimate; an orb whose
        /// parents are neither listed (and buyable) nor kept is left out; every owned archery orb not listed and not a kept
        /// gatherer is sold, leaves first, and the game's own removal of children left with no owned parent is followed;
        /// then the listed orbs not owned are bought in passes over the list until none is ready, while room lasts.</summary>
        public static BuyPlan Plan(IEnumerable<Orb> owned, IEnumerable<Orb> wanted, IReadOnlyDictionary<Orb, Orb[]> deps, int cap, int usedElsewhere) {
            var plan = new BuyPlan();
            var have = new HashSet<Orb>();
            foreach (Orb o in owned) if (deps.ContainsKey(o)) have.Add(o);
            Orb[] Parents(Orb o) => deps.TryGetValue(o, out Orb[] p) && p != null ? p : new Orb[0];

            // 1. the list: in the shrine, once, one ultimate
            var listed = new List<Orb>();
            bool ultimate = false;
            foreach (Orb o in wanted) {
                if (o == Orb.None || listed.Contains(o) || plan.LeftOut.Exists(k => k.Key == o)) continue;
                if (!deps.ContainsKey(o)) { plan.LeftOut.Add(new KeyValuePair<Orb, string>(o, "not in the shrine")); continue; }
                if (UltimateRule.IsUltimate(o)) {
                    if (ultimate) { plan.LeftOut.Add(new KeyValuePair<Orb, string>(o, "a second ultimate (only one can be owned)")); continue; }
                    ultimate = true;
                }
                listed.Add(o);
            }

            // 2. what stays no matter what: owned gatherers not listed
            var keep = new HashSet<Orb>();
            foreach (Orb o in KeepUnlessListed) if (have.Contains(o) && !listed.Contains(o)) keep.Add(o);

            // 3. reachable: no parents, or a parent kept or reachable (fixpoint)
            var reach = new HashSet<Orb>();
            for (bool grew = true; grew;) {
                grew = false;
                foreach (Orb o in listed) {
                    if (reach.Contains(o)) continue;
                    Orb[] ps = Parents(o);
                    bool ok = ps.Length == 0;
                    foreach (Orb d in ps) if (keep.Contains(d) || reach.Contains(d)) { ok = true; break; }
                    if (ok) { reach.Add(o); grew = true; }
                }
            }
            var target = new List<Orb>();
            foreach (Orb o in listed) {
                if (reach.Contains(o)) target.Add(o);
                else plan.LeftOut.Add(new KeyValuePair<Orb, string>(o, $"needs {Names(Parents(o))} first (not in the list)"));
            }

            // 4. sell, leaves first (ties: the later orb first); follow the game's removal of children with no owned parent
            var toSell = new List<Orb>();
            foreach (Orb o in (Orb[])Enum.GetValues(typeof(Orb))) if (have.Contains(o) && !target.Contains(o) && !keep.Contains(o)) toSell.Add(o);
            while (toSell.Count > 0) {
                Orb pick = Orb.None;
                for (int i = toSell.Count - 1; i >= 0 && pick == Orb.None; i--) {
                    Orb c = toSell[i];
                    bool needed = false;
                    foreach (Orb other in toSell) if (other != c && Array.IndexOf(Parents(other), c) >= 0) { needed = true; break; }
                    if (!needed) pick = c;
                }
                if (pick == Orb.None) pick = toSell[toSell.Count - 1]; // a loop of parents: cannot happen in a shrine tree
                toSell.Remove(pick);
                plan.Sell.Add(pick);
                have.Remove(pick);
                Cascade(pick, have, deps, plan, toSell);
            }

            foreach (Orb o in (Orb[])Enum.GetValues(typeof(Orb))) if (have.Contains(o)) plan.Kept.Add(o);

            // 5. room under the cap, then buy in passes over the list
            int room = cap < 1 ? int.MaxValue : Math.Max(0, cap - usedElsewhere - have.Count);
            plan.Room = room;
            var pending = new List<Orb>();
            foreach (Orb o in target) if (!have.Contains(o)) pending.Add(o);
            for (bool bought = true; bought && room > 0;) {
                bought = false;
                foreach (Orb o in pending.ToArray()) {
                    if (room <= 0) break;
                    Orb[] ps = Parents(o);
                    bool ready = ps.Length == 0;
                    foreach (Orb d in ps) if (have.Contains(d)) { ready = true; break; }
                    if (!ready) continue;
                    plan.Buy.Add(o); have.Add(o); pending.Remove(o); room--; bought = true;
                }
            }
            foreach (Orb o in pending)
                plan.LeftOut.Add(new KeyValuePair<Orb, string>(o, room <= 0 ? $"slot cap {cap} (all professions)" : $"needs {Names(Parents(o))} first"));
            // keep LeftOut in the list's order
            var order = new List<Orb>(wanted);
            plan.LeftOut.Sort((a, b) => order.IndexOf(a.Key).CompareTo(order.IndexOf(b.Key)));
            return plan;
        }

        /// <summary>Fix batch D (fixB-review M-4): after a batch the Ranged level is at least the cost of the orbs now owned, so a
        /// later sale by hand (the game lowers the unsigned level by the sold orb's cost) cannot wrap it.</summary>
        public static uint LevelToCover(uint level, uint ownedCost) => Math.Max(level, ownedCost);

        /// <summary>G3 (play-test 8: a save held Ranged level 4294967258, an unsigned level wrapped by earlier sales): no level above
        /// this is real.</summary>
        public const uint SaneLevelMax = 1000u;

        /// <summary>G3: a wrapped level (above <see cref="SaneLevelMax"/>) is repaired to the cost of the orbs owned plus the points
        /// still unspent (fix1, final-G34 M-7: the real level is both); a sane level is kept, never lowered.</summary>
        public static uint RepairLevel(uint level, uint ownedCost, uint freePoints, out bool repaired) {
            repaired = level > SaneLevelMax;
            return repaired ? ownedCost + freePoints : level;
        }

        // PathExperience.LockSlot -> RecursiveLockSlot: a child of the sold orb with no owned parent left goes too, and so on.
        static void Cascade(Orb sold, HashSet<Orb> have, IReadOnlyDictionary<Orb, Orb[]> deps, BuyPlan plan, List<Orb> toSell) {
            foreach (KeyValuePair<Orb, Orb[]> child in deps) {
                if (!have.Contains(child.Key) || child.Value == null || Array.IndexOf(child.Value, sold) < 0) continue;
                bool parentLeft = false;
                foreach (Orb d in child.Value) if (have.Contains(d)) { parentLeft = true; break; }
                if (parentLeft) continue;
                have.Remove(child.Key);
                if (toSell.Remove(child.Key)) plan.Sell.Add(child.Key); // was to be sold anyway: the game sold it with its parent
                else plan.RemovedWith.Add(child.Key);
                Cascade(child.Key, have, deps, plan, toSell);
            }
        }
    }

    /// <summary>The test build's standing creatures: <c>[TestBuild] Spawn</c> = "Kind xN".</summary>
    internal static class SpawnRule {
        static readonly string[] kinds = { "Turabada", "Gotera", "GoteraBirch", "Wyrm" };
        static readonly uint[] hashes = { 13804u, 9466u, 2370u, 21642u }; // the exported prefabs' NetworkPrefab.hash

        public static string Known => string.Join(", ", kinds);

        /// <summary>"Turabada x3", "goterabirch x 2", "Wyrm" (one): a known kind (returned in its own spelling) and a count of 1 or more.</summary>
        public static bool Parse(string? text, out string kind, out int count) {
            kind = ""; count = 0;
            string t = (text ?? "").Trim();
            if (t.Length == 0) return false;
            string name = t, num = "1";
            int x = t.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            if (x > 0) { name = t.Substring(0, x).Trim(); num = t.Substring(x + 2).Trim(); }
            if (!int.TryParse(num, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out count) || count < 1) { count = 0; return false; }
            foreach (string k in kinds) if (string.Equals(k, name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) { kind = k; return true; }
            count = 0;
            return false;
        }

        /// <summary>The prefab hash of a known kind, 0 for anything else.</summary>
        public static uint Hash(string? kind) {
            for (int i = 0; i < kinds.Length; i++) if (string.Equals(kinds[i], kind, StringComparison.OrdinalIgnoreCase)) return hashes[i];
            return 0u;
        }

        /// <summary>How far ahead of the player each creature stands: the first at <paramref name="first"/>, the rest <paramref name="gap"/> apart behind it.</summary>
        public static float[] Distances(int count, float first, float gap) {
            var d = new float[Math.Max(0, count)];
            for (int i = 0; i < d.Length; i++) d[i] = first + gap * i;
            return d;
        }

        public readonly struct Spot { public readonly float Side, Ahead; public Spot(float s, float a) { Side = s; Ahead = a; } }
        /// <summary>H3: where <paramref name="count"/> creatures stand for a test of several locks: side by side across the player's
        /// view, <paramref name="gap"/> apart, the middle of the row <paramref name="first"/> ahead, the outer ones a little further
        /// (a shallow arc: Depth x side squared), so no creature stands behind another. One creature stands straight ahead.</summary>
        public static List<Spot> Arc(int count, float first, float gap) {
            var l = new List<Spot>();
            for (int i = 0; i < Math.Max(0, count); i++) {
                float side = (i - (count - 1) * 0.5f) * gap;
                l.Add(new Spot(side, first + ArcDepth * side * side));
            }
            return l;
        }

        /// <summary>H3: how much further the outer creatures of an arc stand, per square metre of their sideways place.</summary>
        public const float ArcDepth = 0.06f;

        /// <summary>H3: the spots of a spawn: three or fewer in the line straight ahead (pierce and Shatter tests), four or more in the
        /// arc (several locks, none behind another).</summary>
        public static List<Spot> Spots(int count, float first, float gap) {
            if (count >= 4) return Arc(count, first, gap);
            var l = new List<Spot>();
            foreach (float d in Distances(count, first, gap)) l.Add(new Spot(0f, d));
            return l;
        }

        /// <summary>H3: "Kind xN moving" (or "Kind moving"): the same spawn, never frozen (the owner: "can it kind of follow it if it moves?").</summary>
        public static bool Parse(string? text, out string kind, out int count, out bool moving) {
            string t = (text ?? "").Trim();
            moving = t.EndsWith(" moving", StringComparison.OrdinalIgnoreCase);
            if (moving) t = t.Substring(0, t.Length - " moving".Length);
            return Parse(t, out kind, out count);
        }
        /// <summary>A request fits only whole: the alive test creatures plus the new ones at most <paramref name="max"/>.</summary>
        public static bool Allowed(int alive, int adding, int max) => adding > 0 && max > 0 && alive + adding <= max;

        /// <summary>Fix batch D (fixB-review M-8): the first creature never closer than this ahead, the others never closer together.</summary>
        public const float MinAhead = 2f, MinGap = 1f;
        public static float ClampAhead(float metres) => float.IsNaN(metres) || metres < MinAhead ? MinAhead : metres;
        public static float ClampGap(float metres) => float.IsNaN(metres) || metres < MinGap ? MinGap : metres;

        /// <summary>Fix batch D (fixB-review M-1): a planned spot (x, z) with one of this tool's live creatures within
        /// <paramref name="metres"/> of it on the ground (level distance) is blocked, so a second press never stacks creatures.</summary>
        public static bool Blocked(float x, float z, IList<float> xs, IList<float> zs, float metres) {
            if (xs == null || zs == null) return false;
            double r2 = (double)metres * metres + 1e-6;
            for (int i = 0; i < xs.Count && i < zs.Count; i++) {
                double dx = xs[i] - x, dz = zs[i] - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
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
