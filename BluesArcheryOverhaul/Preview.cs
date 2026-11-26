using System;
using System.Collections.Generic;
using System.Globalization;

// Unity-free. What the shrine shows about an archery build: which line each orb belongs to, the tree's tints, and
// the test tablet's wording (the build's facts, what buying an orb changes, the footer). Compiled by
// BluesArcheryOverhaul.Tests, so only System.* in here. Every number comes in through FactNumbers, never typed into a string.
namespace BluesArcheryOverhaul {
    internal static class Lines {
        /// <summary>The line an orb belongs to (its legendary included); None for every other orb.</summary>
        public static MainPath Of(Orb o) {
            switch (o) {
                case Orb.Quick1: case Orb.Quick2: case Orb.Quick3: case Orb.Quickdraw4: return MainPath.Quick;
                case Orb.Volley: case Orb.FullVolley: case Orb.Burst: case Orb.SeekingVolley: return MainPath.Multi;
                case Orb.Charge1: case Orb.Charge2: case Orb.Charge3: case Orb.HolyArrow: return MainPath.Charge;
                default: return MainPath.None;
            }
        }

        public static bool Legendary(Orb o) => o == Orb.Quickdraw4 || o == Orb.SeekingVolley || o == Orb.HolyArrow;

        /// <summary>0.19.11 (the owner: with Quick main, the multi-arrow and Charge try-outs give no feedback): an orb of a line other
        /// than the main path never fires its own shot (only the main path's does), so its try-out could never be practised. Grabbing
        /// it practises it (Shrine.PractiseOnGrab) and the try-out line says so (Tutorial).</summary>
        public static bool OffPath(Orb o, MainPath main) => Of(o) != MainPath.None && main != MainPath.None && Of(o) != main;

        /// <summary>The main path while <paramref name="tried"/> is tried out beside <paramref name="owned"/>: the try-out counts, and
        /// the remembered path keeps a tie (as the server's build, Builds.Compute).</summary>
        public static MainPath TryOutMain(IEnumerable<Orb> owned, Orb tried, MainPath incumbent) {
            var all = new List<Orb>(owned) { tried };
            return MainPathRule.Decide(Build.From(all), incumbent);
        }

        /// <summary>True when the shrine has the Seeking Volley orb (<c>[Tree] SeekingVolleyOrb</c>, set by the tree at load on
        /// both sides); the multi-arrow line then has four orbs.</summary>
        public static bool SeekingVolleyBuilt { get; set; }

        /// <summary>Orbs the shrine has on a line, its legendary included. The multi-arrow line has three (Two-Shot,
        /// Three-Shot, Burst) until Seeking Volley is in the shrine. Side orbs never count.</summary>
        public static int Size(MainPath line) {
            switch (line) {
                case MainPath.Quick: return 4;
                case MainPath.Multi: return SeekingVolleyBuilt ? 4 : 3;
                case MainPath.Charge: return 4;
                default: return 0;
            }
        }

        /// <summary>Orbs of a line the shrine still has for this build to buy.</summary>
        public static int Unowned(Build b, MainPath line) =>
            Math.Max(0, Size(line) - (line == MainPath.Multi && !SeekingVolleyBuilt ? b.Volley : MainPathRule.Count(b, line)));

        /// <summary>What kind of orb this is: one of a line's own (its legendary included), a side orb, or other.</summary>
        public static OrbKind Kind(Orb o) {
            if (Of(o) != MainPath.None) return OrbKind.Line;
            return SideOf(o) != MainPath.None ? OrbKind.Side : OrbKind.Other;
        }

        /// <summary>The line a side orb improves; None for every other orb.</summary>
        public static MainPath SideOf(Orb o) {
            switch (o) {
                case Orb.AutoDock: return MainPath.Quick;
                case Orb.Ricochet: return MainPath.Multi;
                case Orb.Shatter: return MainPath.Charge;   // batch 4b (user): a Charge side orb off Charge Shot 3, no longer a bridge
                default: return MainPath.None;
            }
        }

        /// <summary>The orb is on <paramref name="line"/>: one of its own, or its side orb.</summary>
        public static bool Touches(Orb o, MainPath line) => line != MainPath.None && (Of(o) == line || SideOf(o) == line);

        public const string SideSuffix = " line, side orb";

        /// <summary>The tablet's place line for a held orb: "Quick line, orb 2 of 4", "Quick line, side orb"; empty for other orbs.</summary>
        public static string Place(Orb o) {
            MainPath line = Of(o);
            if (line != MainPath.None) return $"{PathName(line)} line, orb {Position(o)} of {Size(line)}";
            MainPath side = SideOf(o);
            return side != MainPath.None ? PathName(side) + SideSuffix : "";
        }

        /// <summary>1-based place of an orb in its line (the legendary last); 0 for a no-line orb.</summary>
        public static int Position(Orb o) {
            switch (o) {
                case Orb.Quick1: case Orb.Volley: case Orb.Charge1: return 1;
                case Orb.Quick2: case Orb.FullVolley: case Orb.Charge2: return 2;
                case Orb.Quick3: case Orb.Burst: case Orb.Charge3: return 3;
                case Orb.Quickdraw4: case Orb.SeekingVolley: case Orb.HolyArrow: return 4;
                default: return 0;
            }
        }

        /// <summary>The orb's name as the player knows it.</summary>
        public static string Name(Orb o) {
            switch (o) {
                case Orb.Quick1: return "Quickdraw 1";
                case Orb.Quick2: return "Quickdraw 2";
                case Orb.Quick3: return "Quickdraw 3";
                case Orb.Quickdraw4: return "Quickdraw 4";
                case Orb.Volley: return "Two-Shot";
                case Orb.FullVolley: return "Three-Shot";
                case Orb.Burst: return "Burst";
                case Orb.SeekingVolley: return "Seeking Volley";
                case Orb.Charge1: return "Charge Shot 1";
                case Orb.Charge2: return "Charge Shot 2";
                case Orb.Charge3: return "Charge Shot 3";
                case Orb.HolyArrow: return "Holy Arrow";
                case Orb.Gatherer: return "Arrow Gatherer";   // the game's name (H3 section 6: the one gatherer orb)
                case Orb.AutoDock: return "Auto Dock";
                default: return o.ToString();
            }
        }

        /// <summary>The path's name in sentences ("Makes you a Multi-arrow archer.").</summary>
        public static string PathName(MainPath p) {
            switch (p) {
                case MainPath.Quick: return "Quick";
                case MainPath.Multi: return "Multi-arrow";
                case MainPath.Charge: return "Charge";
                default: return "no";
            }
        }

        /// <summary>The path's short name in the footer's counts.</summary>
        public static string ShortName(MainPath p) => p == MainPath.Multi ? "Multi" : PathName(p);
    }

    /// <summary>How an orb held in the shrine's try-out is practised (a runtime-made orb cannot be bought before that).</summary>
    internal enum PracticeBy { Game, OwnAction, AnyShot }

    /// <summary>Review ruling I-3 (batch 1): an orb must always be practisable. Where the spec's "Practised by" action is
    /// possible for the player's main path, that action marks it; where the orb does nothing for that main path (or there is
    /// none), any shot fired while it is held marks it. Orbs outside the mod's later orbs are the game's (its own practice triggers).
    /// Orbs whose behaviour is not built yet stay on any shot.</summary>
    internal static class PracticeRule {
        public static PracticeBy For(Orb orb, MainPath main) {
            switch (orb) {
                // fix batch D: both work for every main path (half strength off their own line), so both have an own action for
                // every main path (SideOrbPractice, Spectral.cs; Volley.Launch marks it). On their own line as before: Ricochet a full
                // volley; Shatter a charged release, not a hit (fix 5, I-1 ruling: the try-out has no target a charged arrow can damage).
                case Orb.Ricochet:
                case Orb.Shatter: return PracticeBy.OwnAction;
                case Orb.AutoDock: return PracticeBy.OwnAction; // a convenience that works for every main path
                // H1: a multi-arrow main practises it with any volley released while it is held (Volley.Launch); it does nothing
                // for any other main path, so there any shot does (an orb must always be practisable)
                case Orb.SeekingVolley: return main == MainPath.Multi ? PracticeBy.OwnAction : PracticeBy.AnyShot;
                default: return Array.IndexOf(NewOrbs.All, orb) >= 0 ? PracticeBy.AnyShot : PracticeBy.Game;
            }
        }
    }

    /// <summary>G3: what the game's own availability check says about a slot, as the client sees it (the orb's status from the
    /// server, SkillOrb.Status): owned, buyable, "cannot buy" (points or the slot cap; the one-legendary rule too), not allowed
    /// (no parent owned), or not known.</summary>
    internal enum ShrineSays { Unknown, Buyable, CantBuy, NotAllowed, Owned, OwnedLocked }

    /// <summary>G3: the facts behind "why can this orb not be bought", as a client knows them. Null = this client cannot tell.</summary>
    internal struct BuyFacts {
        public Orb Orb;
        public ShrineSays Status;
        public bool ParentOwned;
        public string Parents;          // the parents' names, "A or B"
        public int? SlotsHeld;          // bought skills over every profession (the shrine's bought-orb ring)
        public int SlotCap;             // the game's slot cap (0 = none)
        public int? FreePoints;         // this path's unspent points (the shrine's open holders)
        public int Cost;
        public bool? Practised;         // the local player's practised slots (the game sends them to every client)
        public bool UltimateBlocked;    // another legendary is owned (Shrine's one-ultimate rule)
        public string TryOut;           // the orb's try-out line (Tutorial), may be empty
        public string Above;            // OwnedLocked: the owned orbs above it that must be sold first ("A, B"), may be empty
    }

    /// <summary>G3 (play-test 8: Auto Dock and Shatter could be tried but never bought, with no way to tell why): one plain line saying
    /// why the held orb cannot be bought, the first that applies of: its parent, one legendary, the slot cap, points, not tried yet.
    /// Empty when it can be bought, is owned, or this client cannot tell. The cap is blamed before points (more points would not
    /// help); when the game says "cannot buy" and neither shows here, both are named.</summary>
    internal static class BuyBlock {
        public const string Unclear = "Cannot buy now: all skill slots are taken or there are not enough skill points";

        public static string Reason(BuyFacts f) {
            // The game's own rule while the server's CanSellSkillsWithChildren is off (PathExperience.IsSlotAvailable): an owned orb with
            // an owned orb above it cannot be sold, and the attempt does nothing at all.
            if (f.Status == ShrineSays.OwnedLocked) return "To sell it, first sell " + (string.IsNullOrEmpty(f.Above) ? "the orbs above it" : f.Above);
            if (f.Status == ShrineSays.Owned || f.Status == ShrineSays.Unknown) return "";
            if (!f.ParentOwned || f.Status == ShrineSays.NotAllowed) return string.IsNullOrEmpty(f.Parents) ? "Needs another orb first" : "Needs " + f.Parents;
            if (f.UltimateBlocked) return "Only one legendary can be owned";
            if (f.Status == ShrineSays.CantBuy) {
                if (f.SlotCap > 0 && f.SlotsHeld.HasValue && f.SlotsHeld.Value >= f.SlotCap)
                    return $"You already hold {f.SlotsHeld.Value} of {f.SlotCap} skills (all professions)";
                if (f.FreePoints.HasValue && f.FreePoints.Value < f.Cost) return $"Not enough skill points ({f.Cost} needed)";
                return Unclear;
            }
            if (f.Practised == false) return "Try it first: " + (string.IsNullOrEmpty(f.TryOut) ? "use it once while the orb is held" : f.TryOut);
            return "";
        }

        static string YesNo(bool? b) => b.HasValue ? (b.Value ? "yes" : "no") : "?";
        static string Known(int? v) => v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "?";

        /// <summary>For the Debug log line whenever an orb is held: every fact the reason came from.</summary>
        public static string Describe(BuyFacts f) =>
            $"{f.Orb} {f.Status}: parent owned {YesNo(f.ParentOwned)} ({(string.IsNullOrEmpty(f.Parents) ? "none" : f.Parents)}), slots {Known(f.SlotsHeld)} of {f.SlotCap}, " +
            $"free points {Known(f.FreePoints)}, cost {f.Cost}, practised {YesNo(f.Practised)}, legendary blocked {YesNo(f.UltimateBlocked)}";
    }

    internal enum OrbKind { Other, Line, Side }

    /// <summary>The later orbs (spec 2026-11-16; six parked ones were deleted in 0.16.0): which they are, what they need and
    /// where they sit by default.</summary>
    internal static class NewOrbs {
        public static readonly Orb[] All = { Orb.AutoDock, Orb.Ricochet, Orb.SeekingVolley, Orb.Shatter };

        static readonly Orb[] None = new Orb[0];

        /// <summary>The default of <c>[Tree] &lt;Orb&gt;Orb</c>: the four orbs worked on and built are in the shrine by default
        /// (user, 2026-11-16: Auto Dock, Ricochet, Seeking Volley and Shatter; Seeking Volley since its batch H1). The shipped-defaults
        /// rule moves a setting still at the old default (false) to true; one set off by hand stays off.</summary>
        public static bool OnByDefault(Orb o) => o == Orb.AutoDock || o == Orb.Ricochet || o == Orb.Shatter || o == Orb.SeekingVolley;

        /// <summary>The orbs one of which must be owned first; empty for every other orb.</summary>
        public static Orb[] Parents(Orb o) {
            switch (o) {
                case Orb.AutoDock: return new[] { Orb.Quick3 };
                case Orb.Ricochet: return new[] { Orb.Burst };       // batch 4b: the user's sketch hangs it off the third multi-arrow orb
                case Orb.SeekingVolley: return new[] { Orb.Burst };
                case Orb.Shatter: return new[] { Orb.Charge3 };      // batch 4b: a Charge side orb
                default: return None;
            }
        }

        /// <summary>The default <c>[TreeLayout]</c> position of a later orb: the one table, <see cref="OrbPlaces.Default"/>.</summary>
        public static string DefaultPlace(Orb o) => o == Orb.AutoDock || o == Orb.Ricochet || o == Orb.SeekingVolley || o == Orb.Shatter ? OrbPlaces.Default(o) : "";
    }

    /// <summary>"depth, sideways, height" positions for the shrine, and how close two orbs may sit.</summary>
    internal static class OrbPlaces {
        public const float MinGap = 0.12f;

        /// <summary>H3 section 5 (the owner's sketch, then "exactly straight and even with every single side evenly spaced out") and
        /// section 6 (one gatherer orb): the 16 orbs of the shrine, each a [TreeLayout] setting.</summary>
        public static readonly Orb[] All = {
            Orb.Quick1, Orb.Quick2, Orb.Quick3, Orb.Quickdraw4, Orb.AutoDock, Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.HolyArrow, Orb.Shatter,
            Orb.Volley, Orb.FullVolley, Orb.Burst, Orb.SeekingVolley, Orb.Ricochet, Orb.Gatherer,
        };

        /// <summary>Where each orb sits by default, "depth, sideways, height" in metres from the shrine's Skill Tree Parent (slot x =
        /// depth, y = sideways with negative the player's left, z = height; 0 is 1.14 m above the floor; the base the root orbs link to,
        /// Skill Tree Origin, is at height -0.331). One square grid, every orb at depth -0.10, mirror-symmetric about sideways 0:
        /// columns every 0.25 m, rows every 0.25 m from -0.09; Quick on the left and Multi on the right fanning out after their
        /// second orb, Charge up the middle, the three legendaries on the top row, the side orbs half a row above the third row,
        /// the one gatherer bottom left. (tree-layout.md, the owner's grid in H3-brief.md section 5.)</summary>
        public static string Default(Orb o) {
            switch (o) {
                case Orb.Quick1: return "-0.10, -0.25, -0.09";
                case Orb.Quick2: return "-0.10, -0.25, 0.16";
                case Orb.Quick3: return "-0.10, -0.50, 0.41";
                case Orb.Quickdraw4: return "-0.10, -0.50, 0.66";
                case Orb.AutoDock: return "-0.10, -0.75, 0.535";
                case Orb.Charge1: return "-0.10, 0.00, -0.09";
                case Orb.Charge2: return "-0.10, 0.00, 0.16";
                case Orb.Charge3: return "-0.10, 0.00, 0.41";
                case Orb.HolyArrow: return "-0.10, 0.00, 0.66";
                case Orb.Shatter: return "-0.10, -0.25, 0.535";
                case Orb.Volley: return "-0.10, 0.25, -0.09";
                case Orb.FullVolley: return "-0.10, 0.25, 0.16";
                case Orb.Burst: return "-0.10, 0.50, 0.41";
                case Orb.SeekingVolley: return "-0.10, 0.50, 0.66";
                case Orb.Ricochet: return "-0.10, 0.75, 0.535";
                case Orb.Gatherer: return "-0.10, -0.75, -0.09";
                default: return "";
            }
        }

        /// <summary>The [TreeLayout] entry's name: the eight that existed before H3 keep theirs (HolyArrow, Burst, ArrowGatherer,
        /// AutoDock, Ricochet, SeekingVolley, Shatter; QuiverGather is read by nothing since section 6), the game's own slots get
        /// Quickdraw1-3, ChargeShot1-3, TwoShot, ThreeShot, and Quickdraw4.</summary>
        public static string SettingName(Orb o) {
            switch (o) {
                case Orb.Quick1: return "Quickdraw1";
                case Orb.Quick2: return "Quickdraw2";
                case Orb.Quick3: return "Quickdraw3";
                case Orb.Charge1: return "ChargeShot1";
                case Orb.Charge2: return "ChargeShot2";
                case Orb.Charge3: return "ChargeShot3";
                case Orb.Volley: return "TwoShot";
                case Orb.FullVolley: return "ThreeShot";
                case Orb.Gatherer: return "ArrowGatherer";
                default: return o.ToString();
            }
        }

        public static bool TryParse(string? text, out float depth, out float sideways, out float height) {
            depth = sideways = height = 0f;
            string[] parts = (text ?? "").Split(',');
            var inv = CultureInfo.InvariantCulture;
            return parts.Length == 3 && float.TryParse(parts[0].Trim(), NumberStyles.Float, inv, out depth)
                   && float.TryParse(parts[1].Trim(), NumberStyles.Float, inv, out sideways)
                   && float.TryParse(parts[2].Trim(), NumberStyles.Float, inv, out height);
        }

        public static float Distance(float ax, float ay, float az, float bx, float by, float bz) {
            float x = ax - bx, y = ay - by, z = az - bz;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }

    /// <summary>The shrine sends its connection lines as one list of orb pairs whose length is serialized as 0..127
    /// (_decomp\SkillShrine.cs:24-51): one pair per dependency, one pair (orb, 0) for an orb with none.</summary>
    internal static class ShrineLinks {
        public const int Cap = 127;

        public static int Entries(IEnumerable<int> dependencyCounts) {
            int n = 0;
            foreach (int d in dependencyCounts) n += 2 * Math.Max(1, d);
            return n;
        }
    }

    internal static class BuildPreview {
        /// <summary>The build these orbs make, its main path decided with <paramref name="incumbent"/>.</summary>
        public static Build Now(ICollection<Orb> owned, MainPath incumbent) {
            Build b = Build.From(owned);
            return b.With(MainPathRule.Decide(b, incumbent));
        }

        /// <summary>The build after buying <paramref name="buy"/> (<paramref name="owned"/> is not changed).</summary>
        public static Build After(ICollection<Orb> owned, Orb buy, MainPath incumbent) {
            var with = new List<Orb>(owned) { buy };
            return Now(with, Now(owned, incumbent).Main);
        }

        /// <summary>True when buying <paramref name="buy"/> moves an existing main path to another line.</summary>
        public static bool Flips(ICollection<Orb> owned, Orb buy, MainPath incumbent) {
            MainPath now = Now(owned, incumbent).Main;
            return now != MainPath.None && After(owned, buy, incumbent).Main != now;
        }
    }

    /// <summary>The main path the shrine shows for the local player. The server's build includes an orb that is only
    /// being tried out, so its main path is trusted only when its three line counts are those of the orbs really owned.</summary>
    internal static class ShrineIncumbent {
        public static bool SameCounts(Build a, Build b) =>
            MainPathRule.Count(a, MainPath.Quick) == MainPathRule.Count(b, MainPath.Quick)
            && MainPathRule.Count(a, MainPath.Multi) == MainPathRule.Count(b, MainPath.Multi)
            && MainPathRule.Count(a, MainPath.Charge) == MainPathRule.Count(b, MainPath.Charge);

        /// <summary><paramref name="server"/>: the build the server last sent (<paramref name="serverKnown"/> false before
        /// any arrived); <paramref name="owned"/>: the build of the orbs the shrine shows as owned; <paramref name="last"/>:
        /// the last trusted main path. Same counts: the server's main path (<paramref name="fromServer"/> true). Otherwise
        /// the rule the server applies to real purchases, from the last trusted path.</summary>
        public static MainPath Decide(bool serverKnown, Build server, Build owned, MainPath last, out bool fromServer) {
            fromServer = serverKnown && server.Main != MainPath.None && SameCounts(server, owned);
            return fromServer ? server.Main : MainPathRule.Decide(owned, last);
        }
    }

    internal enum OrbTint { None, MainPath, Legendary, FlipWarning }

    internal static class TreeTint {
        /// <summary>Flip warning beats legendary beats main path; a no-line, non-legendary orb is never tinted.</summary>
        public static OrbTint For(Orb orb, bool ownedByViewer, ICollection<Orb> owned, MainPath incumbent) {
            MainPath line = Lines.Of(orb);
            bool legendary = Lines.Legendary(orb);
            if (line == MainPath.None && !legendary) {
                // A side orb never moves a main path (it counts on no line): it only takes the main path's tint
                // when it is on that line.
                if (Lines.Kind(orb) != OrbKind.Side) return OrbTint.None;
                return TintLine(orb, BuildPreview.Now(owned, incumbent).Main) != MainPath.None ? OrbTint.MainPath : OrbTint.None;
            }
            if (!ownedByViewer && BuildPreview.Flips(owned, orb, incumbent)) return OrbTint.FlipWarning;
            if (legendary) return OrbTint.Legendary;
            MainPath main = BuildPreview.Now(owned, incumbent).Main;
            return main != MainPath.None && line == main ? OrbTint.MainPath : OrbTint.None;
        }

        /// <summary>The line whose colour a main-path ring on this orb takes: a line orb's own line; a side orb's
        /// the main path when the orb is on it; None otherwise.</summary>
        public static MainPath TintLine(Orb orb, MainPath main) {
            MainPath line = Lines.Of(orb);
            if (line != MainPath.None) return line;
            return Lines.Touches(orb, main) ? main : MainPath.None;
        }

        /// <summary>A connection line is tinted when both its ends are on the main path's line (side orbs included).</summary>
        public static bool LinkTinted(Orb from, Orb to, MainPath main) =>
            main != MainPath.None && Lines.Touches(from, main) && Lines.Touches(to, main);
    }

    /// <summary>Every number the tablet shows: the live settings and tables on the game side (<c>Paths.Numbers()</c>),
    /// the shipped defaults in tests. The arrays are indexed by tier 0..3.</summary>
    internal readonly struct FactNumbers {
        public readonly ComplementValues C;
        public readonly ChargeStages Stages;
        public readonly VolleyTimes Volley;
        public readonly float HolySeconds;
        public readonly float InnerShare, OuterShare;
        public readonly float[] QuickMultiplier, ChargeMultiplier, ChargeStagger;
        public readonly OrbValues O;

        public FactNumbers(ComplementValues c, ChargeStages stages, VolleyTimes volley, float holySeconds, float innerShare, float outerShare,
                           float[] quickMultiplier, float[] chargeMultiplier, float[] chargeStagger, OrbValues orbs) {
            C = c; Stages = stages; Volley = volley; HolySeconds = holySeconds; InnerShare = innerShare; OuterShare = outerShare;
            QuickMultiplier = quickMultiplier; ChargeMultiplier = chargeMultiplier; ChargeStagger = chargeStagger; O = orbs;
        }

        public static FactNumbers Default => new FactNumbers(ComplementValues.Default, ChargeStages.Default, VolleyTimes.Default, DrawTimeline.HolyTotal,
            VolleyMath.InnerShare, VolleyMath.OuterShare, new[] { 1f, 1.1f, 1.2f, 1.3f }, new[] { 1f, ChargeDamage.Default.S1, ChargeDamage.Default.S2, ChargeDamage.Default.S3 },
            new[] { 0f, 0.2f, 0.4f, 0.6f }, OrbValues.Default);

        /// <summary>The table's value at <paramref name="tier"/>, clamped to its ends; <paramref name="fallback"/> for a missing table.</summary>
        public static float At(float[] table, int tier, float fallback) {
            if (table == null || table.Length == 0) return fallback;
            return table[Math.Max(0, Math.Min(table.Length - 1, tier))];
        }
    }

    /// <summary>One thing a build does, as one tablet line. <c>Key</c> names the fact so two builds can be compared.</summary>
    internal readonly struct Fact {
        public readonly string Key;
        public readonly string Text;
        public Fact(string key, string text) { Key = key; Text = text; }
        public override string ToString() => Key + ": " + Text;
    }

    internal static class BuildFacts {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>At most 2 decimals, trailing zeros trimmed, invariant culture.</summary>
        public static string Num(float v) => v.ToString("0.##", Inv);
        public static string Percent(float share) => Num(share * 100f);
        static string Plural(int count, string one, string many) => count == 1 ? one : many;

        static readonly Orb[] LegendaryOrbs = { Orb.Quickdraw4, Orb.SeekingVolley, Orb.HolyArrow };
        static readonly Ultimates[] LegendaryFlags = { Ultimates.Quickdraw4, Ultimates.SeekingVolley, Ultimates.HolyArrow };

        /// <summary>The build's combined result: the main shot first, then its complements, the side orbs that work
        /// for its main path, then idle legendaries and side orbs. <paramref name="autoDock"/>: Auto Dock is owned (it
        /// has no bit in the build, so only the owner's own tablet knows it).</summary>
        public static List<Fact> Describe(Build b, FactNumbers n, bool autoDock = false) {
            var facts = new List<Fact>();
            if (b.Main == MainPath.None) { facts.Add(new Fact("none", "No archery path yet")); return facts; }

            if (b.Main == MainPath.Quick) {
                if (b.Quick > 0) facts.Add(new Fact("quick", $"Quick shots: x{Num(FactNumbers.At(n.QuickMultiplier, b.Quick, 1f))} damage"));
                if ((b.Ultimates & Ultimates.Quickdraw4) != 0) facts.Add(new Fact("line", "Quickdraw 4: instant line shot, pierces creatures (" + PierceText(n.O.PierceShares) + ")"));
                int needles = Complements.Needles(b, n.C);
                if (needles > 0) facts.Add(new Fact("needles", $"Quick shots: +{needles} {Plural(needles, "needle", "needles")}, {Percent(n.C.NeedleShare)}% each"));
                float snap = Complements.SnapStagger(b, n.C);
                if (snap > 0f) facts.Add(new Fact("snapweight", $"Quick shots: stagger {Num(snap)}" + (Complements.SnapInterrupts(b, n.C) ? ", interrupt" : "")));
            }
            else if (b.Main == MainPath.Multi) {
                VolleyTimes tm = n.Volley.Scaled(Complements.VolleyFactor(b, n.C));
                if (b.Volley > 0) {
                    string volley = $"Volley: {VolleyMath.Inner} ghost arrows at {Num(tm.VolleyAt)}s";
                    if (b.Volley >= 2) volley += $", {VolleyMath.Total} at {Num(tm.FullAt + (VolleyMath.Outer - 1) * tm.Interval)}s";   // the ratchet's first ghost at FullAt (VolleyTimeline.Ghosts)
                    facts.Add(new Fact("volley", volley));
                }
                if (b.Volley >= 3) facts.Add(new Fact("burst", $"Burst: {BurstPattern.Count} pellets at {Num(tm.RackTime)}s"));
                float spread = Complements.SpreadFactor(b, n.C);
                if (spread < 1f) facts.Add(new Fact("spread", $"Volley: {Percent(1f - spread)}% tighter"));
                // G3: the Charge Shot orbs are earned by holding a full volley (VolleyHold): one stage per orb, each turning the ghosts
                // gold and adding the per-stage damage and stagger
                int stages = VolleyHold.Orbs(b);
                float perDamage = Complements.GhostDamageAt(b, n.C, 1) - 1f, perStagger = Complements.GhostStaggerAt(b, n.C, 1);
                if (stages > 0 && (perDamage > 1e-4f || perStagger > 0f)) {
                    var parts = new List<string>();
                    if (perDamage > 1e-4f) parts.Add($"+{Percent(perDamage)}% damage");
                    if (perStagger > 0f) parts.Add($"{Num(perStagger)} stagger");
                    facts.Add(new Fact("gold", $"Hold a full volley: each {Num(n.C.VolleyStageSeconds)} s turns the ghosts gold one stage, {string.Join(" and ", parts)} each, up to {stages} {Plural(stages, "stage", "stages")}"));
                }
                if ((b.Ultimates & Ultimates.SeekingVolley) != 0) facts.Add(new Fact("seek", SeekFact(b, n.O)));
            }
            else if (b.Main == MainPath.Charge) {
                float factor = Complements.StageFactor(b, n.C);
                if (b.Charge > 0) {
                    float full = n.Stages.Scaled(factor).At(b.Charge);
                    facts.Add(new Fact("charge", $"Charged shots: {b.Charge} {Plural(b.Charge, "stage", "stages")}, full at {Num(full)}s, x{Num(FactNumbers.At(n.ChargeMultiplier, b.Charge, 1f))} damage"));
                    float stagger = FactNumbers.At(n.ChargeStagger, b.Charge, 0f);
                    if (stagger > 0f) facts.Add(new Fact("stagger", $"Charged shots: stagger {Num(stagger)}"));
                }
                if ((b.Ultimates & Ultimates.HolyArrow) != 0) facts.Add(new Fact("holy", $"Holy Arrow: ready at {Num(n.HolySeconds * factor)}s"));
                int heavy = Complements.HeavyArrows(b, n.C);
                if (heavy > 0) facts.Add(new Fact("heavy", $"Charged shots: +{heavy} {Plural(heavy, "heavy arrow", "heavy arrows")}, {Percent(n.C.HeavyShare)}% each"));
                if (b.Quick > 0) facts.Add(new Fact("steady", $"Charged shots: steadier (Quickdraw {b.Quick})"));
            }

            // The side orbs a build owns: their line's fact when it is the main path (Auto Dock always).
            var idle = new List<Orb>();
            foreach (Orb o in SmallOrbs) {
                if (!Owns(b, o, autoDock)) continue;
                string text = SmallFact(o, b, n.O, n.C);
                if (text.Length > 0) facts.Add(new Fact(o.ToString().ToLowerInvariant(), text));
                else idle.Add(o);
            }

            for (int i = 0; i < LegendaryOrbs.Length; i++) {
                Orb o = LegendaryOrbs[i];
                MainPath line = Lines.Of(o);
                if ((b.Ultimates & LegendaryFlags[i]) != 0 && line != b.Main)
                    facts.Add(new Fact("idle", $"{Lines.Name(o)}: does nothing until {Lines.PathName(line)} is your main path"));
            }
            // every side orb has a line (G1-review M-2: the "does nothing for a ... archer" branch was for the deleted bridges)
            foreach (Orb o in idle) facts.Add(new Fact("idle", $"{Lines.Name(o)}: does nothing until {Lines.PathName(Lines.SideOf(o))} is your main path"));
            return facts;
        }

        /// <summary>H1: Seeking Volley on the multi-arrow main, from the live numbers: how long the aim rests to lock (near and far,
        /// shorter with Quickdraw orbs), how many locks, the release cone, and the seekers' damage when it is not their full share.</summary>
        static string SeekFact(Build b, OrbValues o) {
            HomingValues h = o.Homing;
            string near = Num(LockRule.Dwell(h.DwellNearMetres, b.Quick, h)), far = Num(LockRule.Dwell(h.DwellFarMetres, b.Quick, h));
            string locks = h.MaxLocks > 1 ? $"up to {h.MaxLocks}" : "one at a time";
            string damage = Math.Abs(o.SeekerShare - 1f) > 1e-4f ? $" ({Percent(o.SeekerShare)}% damage)" : "";
            return $"Volley: aim at a creature {near} to {far} s to lock it, {locks}; shoot within {Num(h.ReleaseConeDegrees)} degrees and every arrow homes in{damage}";
        }

        /// <summary>The side orbs the tablet describes, in the order it lists them.</summary>
        static readonly Orb[] SmallOrbs = { Orb.AutoDock, Orb.Ricochet, Orb.Shatter };

        static bool Owns(Build b, Orb o, bool autoDock) {
            switch (o) {
                case Orb.Ricochet: return (b.Sides & SideOrbs.Ricochet) != 0;
                case Orb.Shatter: return (b.Bridges & Bridges.Shatter) != 0;
                case Orb.AutoDock: return autoDock;
                default: return false;
            }
        }

        static readonly string[] Ordinals = { "1st", "2nd", "3rd" };
        static string Ordinal(int n) => n >= 1 && n <= 3 ? Ordinals[n - 1] : n.ToString(Inv) + "th";

        /// <summary>Batch 4b: "2nd 70%, then 50%" from [Quickdraw4] PierceShares (the last value repeats); one value: "each after the first 50%".</summary>
        static string PierceText(float[]? shares) {
            if (shares == null || shares.Length == 0) shares = LinePierce.DefaultShares;
            if (shares.Length == 1) return $"each after the first {Percent(shares[0])}%";
            var parts = new List<string>();
            for (int i = 0; i < shares.Length - 1; i++) parts.Add($"{Ordinal(i + 2)} {Percent(shares[i])}%");
            parts.Add($"then {Percent(shares[shares.Length - 1])}%");
            return string.Join(", ", parts);
        }

        /// <summary>Fix batch D: "half strength" for the shipped share, else "N% strength".</summary>
        /// <summary>H5b: the words for [Ricochet] Bounces past the first jump (each further jump is JumpShare of the one before; the off-path
        /// half is taken once, on the first). Empty for one jump.</summary>
        static string OnAgain(OrbValues v) => v.RicochetBounces <= 1 ? "" : v.RicochetBounces == 2
            ? $", then on to one more for {Percent(v.RicochetJumpShare)}% of that"
            : $", then on to {v.RicochetBounces - 1} more for {Percent(v.RicochetJumpShare)}% of the last each";

        static string Strength(float share) => Math.Abs(share - 0.5f) < 1e-4f ? "half strength" : $"{Percent(share)}% strength";

        /// <summary>A side orb's tablet line for this main path; empty when it does nothing for it.</summary>
        static string SmallFact(Orb o, Build b, OrbValues v, ComplementValues c) {
            MainPath main = b.Main;
            bool multi = main == MainPath.Multi, charge = main == MainPath.Charge;
            switch (o) {
                case Orb.AutoDock: return "After a shot: the next arrow nocks itself from your quiver";   // a convenience: every main path
                case Orb.Ricochet: {
                    if (multi) return $"Ghosts: skip once off the ground and fly on; every hit jumps to a creature within {Num(v.RicochetMetres)} m for {Percent(v.RicochetJumpShare)}%{OnAgain(v)}";
                    // fix batch D: off the Multi main every arrow of the release that hits jumps, and the build's own spectral arrows skip
                    float share = SideOrbShare.Ricochet(b, v.SideOrbOffPathShare);
                    if (!(share > 0f)) return "";
                    string extra = Complements.Needles(b, c) > 0 ? "needles" : Complements.HeavyArrows(b, c) > 0 ? "heavy arrows" : "";
                    return $"{(extra.Length > 0 ? "Arrow and " + extra : "Arrow")}: every hit jumps to a creature within {Num(v.RicochetMetres)} m for {Percent(v.RicochetJumpShare * share)}%{OnAgain(v)}"
                           + (extra.Length > 0 ? $"; {extra} skip once off the ground" : "") + $" ({Strength(share)} off the {Lines.PathName(MainPath.Multi)} path)";
                }
                case Orb.Shatter: {
                    // play-test 7: a blast behind the hit; its share close behind for this build (x1 plus 5% per multi-arrow orb by default since 0.19.17)
                    float peak = ShatterBlast.Peak(new BlastValues(v.ShatterMetres, 60f, v.ShatterBlast, 2f, 0.25f, v.ShatterPerOrb), b.Volley);
                    if (charge) return ShatterRule.Half(b) != ShatterHalf.None && peak > 0f ? $"Charged shots: blast behind the hit, {Num(v.ShatterMetres)} m, up to x{Num(peak)} of the hit" : "";
                    // fix batch D: off the Charge main the first creature hit of any release blasts, at the off-path share, no multi-arrow bonus
                    float share = SideOrbShare.Shatter(b, v.SideOrbOffPathShare);
                    float off = ShatterBlast.Peak(new BlastValues(v.ShatterMetres, 60f, v.ShatterBlast, 2f, 0.25f, v.ShatterPerOrb), 0) * share;
                    return off > 0f ? $"Arrow hits: blast behind the first creature, {Num(v.ShatterMetres)} m, up to x{Num(off)} of the hit ({Strength(share)} off the {Lines.PathName(MainPath.Charge)} path)" : "";
                }
                default: return "";
            }
        }

    }

    internal static class TabletText {
        public const string Title = "Your archery build";
        /// <summary>H3 section 6: what the one gatherer orb does, in plain words (the game's gathering and Quiver Gather's return).</summary>
        public const string GathererLine = "Gathers arrows and feathers into your pouch, and arrows you shoot from your quiver come back to it";
        public const string ChangesHeader = "With your build:";
        public const string NoChange = "With your build: no change.";
        public const string Owned = "You own this.";
        const string Separator = " · ";
        static readonly MainPath[] FooterOrder = { MainPath.Charge, MainPath.Quick, MainPath.Multi };

        /// <summary>The tablet while holding nothing: title, the build's facts, footer, test note.</summary>
        public static List<string> ForBuild(ICollection<Orb> owned, MainPath incumbent, FactNumbers n, string testNote) {
            Build b = BuildPreview.Now(owned, incumbent);
            var lines = new List<string> { Title };
            foreach (Fact f in BuildFacts.Describe(b, n, owned.Contains(Orb.AutoDock))) lines.Add(f.Text);
            lines.Add(Footer(b));
            if (!string.IsNullOrEmpty(testNote)) lines.Add(testNote);
            return lines;
        }

        public const string OtherTitle = "'s archery build", UnnamedOther = "Another archer's build";

        /// <summary>The tablet as someone other than the tree's opener sees it: "&lt;name&gt;'s archery build", and when
        /// this client knows that player's build (<paramref name="known"/>) its facts and the footer; then the test note.
        /// Nothing about a held orb: another client cannot know what the opener would get.</summary>
        public static List<string> ForOther(string name, bool known, Build b, FactNumbers n, string testNote) {
            name = (name ?? "").Trim();
            var lines = new List<string> { name.Length == 0 || name == "?" ? UnnamedOther : name + OtherTitle };
            if (known) {
                foreach (Fact f in BuildFacts.Describe(b, n)) lines.Add(f.Text.Replace("is your main path", "is their main path"));
                lines.Add(Footer(b, false));
            }
            if (!string.IsNullOrEmpty(testNote)) lines.Add(testNote);
            return lines;
        }

        /// <summary>The tablet while an orb is held: its name and place, the try-out line, what buying it changes,
        /// the outcome for the main path, why it cannot be bought, footer, test note.</summary>
        public static List<string> ForOrb(Orb orb, bool owned, string cannotBuy, ICollection<Orb> ownedSet, MainPath incumbent, FactNumbers n,
                                          string tryOut, string testNote) {
            var lines = new List<string> { Lines.Name(orb) };
            string place = Lines.Place(orb);
            if (place.Length > 0) lines.Add(place);
            if (orb == Orb.Gatherer) lines.Add(GathererLine);
            if (!string.IsNullOrEmpty(tryOut)) lines.Add(tryOut);

            Build now = BuildPreview.Now(ownedSet, incumbent);
            Build after = owned ? now : BuildPreview.After(ownedSet, orb, incumbent);
            if (owned) lines.Add(Owned);
            else {
                bool dockNow = ownedSet.Contains(Orb.AutoDock);
                List<string> changes = Changes(BuildFacts.Describe(now, n, dockNow), BuildFacts.Describe(after, n, dockNow || orb == Orb.AutoDock));
                if (changes.Count == 0) lines.Add(NoChange);
                else { lines.Add(ChangesHeader); lines.AddRange(changes); }
            }

            string outcome = Outcome(now.Main, after.Main);
            if (outcome.Length > 0) lines.Add(outcome);
            if (now.Main != MainPath.None && after.Main != MainPath.None && now.Main != after.Main) lines.Add(Loss(now));
            if (!string.IsNullOrEmpty(cannotBuy)) lines.Add(cannotBuy);
            lines.Add(Footer(after));
            if (!string.IsNullOrEmpty(testNote)) lines.Add(testNote);
            return lines;
        }

        static string Head(string text) { int i = text.IndexOf(": ", StringComparison.Ordinal); return i < 0 ? text : text.Substring(0, i); }
        static string Tail(string text) { int i = text.IndexOf(": ", StringComparison.Ordinal); return i < 0 ? text : text.Substring(i + 2); }

        static readonly string[] PartSeparator = { ", " };
        static readonly char[] Space = { ' ' };

        /// <summary>Facts new in <paramref name="after"/> ("+ text") or changed. Facts are matched by key and the text before the
        /// colon (two idle legendaries share a key). Facts that go away are not listed (a flip's loss has its own line).</summary>
        static List<string> Changes(List<Fact> before, List<Fact> after) {
            var lines = new List<string>();
            foreach (Fact f in after) {
                string head = Head(f.Text);
                Fact? old = null;
                foreach (Fact o in before) if (o.Key == f.Key && Head(o.Text) == head) { old = o; break; }
                if (old == null) lines.Add("+ " + f.Text);
                else if (old.Value.Text != f.Text) lines.Add(Changed(head, Tail(old.Value.Text), Tail(f.Text), f.Text));
            }
            return lines;
        }

        /// <summary>A changed fact, only what changed: the tails split on ", "; with the same number of parts each differing
        /// part reads "old -> new" (see <see cref="PartChange"/>), joined by ", ". Different part counts: "+ " and the whole new fact.</summary>
        static string Changed(string head, string oldTail, string newTail, string newText) {
            string[] a = oldTail.Split(PartSeparator, StringSplitOptions.None), b = newTail.Split(PartSeparator, StringSplitOptions.None);
            if (a.Length != b.Length) return "+ " + newText;
            var parts = new List<string>();
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) parts.Add(PartChange(a[i], b[i]));
            return $"{head}: {string.Join(", ", parts)}";
        }

        /// <summary>"full at 2.2s" / "full at 1.9s" -> "full at 2.2s -> 1.9s": the words both share at the start and end are said
        /// once when what is left on each side is one word without brackets; otherwise "old -> new" in full.</summary>
        static string PartChange(string a, string b) {
            string[] x = a.Split(Space), y = b.Split(Space);
            int pre = 0;
            while (pre < x.Length && pre < y.Length && x[pre] == y[pre]) pre++;
            int post = 0;
            while (post < x.Length - pre && post < y.Length - pre && x[x.Length - 1 - post] == y[y.Length - 1 - post]) post++;
            if (x.Length - pre - post == 1 && y.Length - pre - post == 1) {
                string ox = x[pre], ny = y[pre];
                if (ox.IndexOfAny(Brackets) < 0 && ny.IndexOfAny(Brackets) < 0) {
                    string start = pre > 0 ? string.Join(" ", x, 0, pre) + " " : "";
                    string end = post > 0 ? " " + string.Join(" ", x, x.Length - post, post) : "";
                    return $"{start}{ox} -> {ny}{end}";
                }
            }
            return $"{a} -> {b}";
        }
        static readonly char[] Brackets = { '(', ')', '[', ']' };

        static string Outcome(MainPath now, MainPath after) {
            if (after == MainPath.None) return "";
            if (now == MainPath.None) return $"Makes you a {Lines.PathName(after)} archer.";
            if (now == after) return $"Keeps you a {Lines.PathName(after)} archer.";
            return $"Makes you a {Lines.PathName(after)} archer (you are {Lines.PathName(now)} now).";
        }

        public const string LossPrefix = "You lose: ";

        /// <summary>What a flip away from <paramref name="now"/>'s main path loses: "You lose: the charge and Holy Arrow."</summary>
        public static string Loss(Build now) {
            string thing; Ultimates legendary; Orb orb;
            switch (now.Main) {
                case MainPath.Quick: thing = "the quick shot"; legendary = Ultimates.Quickdraw4; orb = Orb.Quickdraw4; break;
                case MainPath.Multi: thing = "the volley"; legendary = Ultimates.SeekingVolley; orb = Orb.SeekingVolley; break;
                case MainPath.Charge: thing = "the charge"; legendary = Ultimates.HolyArrow; orb = Orb.HolyArrow; break;
                default: return "";
            }
            if ((now.Ultimates & legendary) != 0) thing += " and " + Lines.Name(orb);
            return LossPrefix + thing + ".";
        }

        /// <summary>"[Charge 3] · Quick 1 · Multi 0": the main path first and in brackets, then the others in the order Charge,
        /// Quick, Multi; plus the nearest line that 1 or 2 more orbs would make the main path (only when the shrine really
        /// has that many unowned orbs on that line). <paramref name="hint"/> off: the counts alone (another player's build).</summary>
        public static string Footer(Build b, bool hint = true) {
            var parts = new List<string>();
            if (b.Main != MainPath.None) parts.Add($"[{Lines.ShortName(b.Main)} {MainPathRule.Count(b, b.Main)}]");
            foreach (MainPath p in FooterOrder) if (p != b.Main) parts.Add($"{Lines.ShortName(p)} {MainPathRule.Count(b, p)}");
            string footer = string.Join(Separator, parts);
            if (b.Main == MainPath.None || !hint) return footer;

            int mainCount = MainPathRule.Count(b, b.Main), best = int.MaxValue;
            MainPath nearest = MainPath.None;
            foreach (MainPath p in FooterOrder) {
                if (p == b.Main) continue;
                int count = MainPathRule.Count(b, p), need = mainCount - count + 1;
                if (need > Lines.Unowned(b, p)) continue;   // the shrine has not that many more orbs on that line
                if (need < best) { best = need; nearest = p; }
            }
            if (nearest != MainPath.None && best >= 1 && best <= 2) footer += $" ({best} more {Lines.ShortName(nearest)} changes your path)";
            return footer;
        }

        static bool IsOutcome(string l) => l.StartsWith("Keeps you a ", StringComparison.Ordinal) || l.StartsWith("Makes you a ", StringComparison.Ordinal);

        static bool IsHeader(string l) => l == ChangesHeader || l == NoChange || l == Owned;
        static bool IsPosition(string l) => (l.Contains(" line, orb ") && l.Contains(" of ")) || l.EndsWith(Lines.SideSuffix, StringComparison.Ordinal);


        /// <summary>At most <paramref name="max"/> lines. Drops, in order: the "... line, orb N of M" line, then fact or
        /// change lines from the end of their block (after "With your build:", or after the title when there is no such
        /// line, up to the outcome line or else the footer), and last of all the try-out line (<paramref name="tryOut"/>,
        /// as it was given to <see cref="ForOrb"/>: an orb must be practised before it can be bought, so its instruction
        /// outlives every other droppable line). Never the first line, the outcome, "You lose: ...", a cannot-buy line,
        /// the footer or the test note; "With your build:" goes only when every line under it has gone. It may stay over
        /// <paramref name="max"/> when nothing droppable is left.</summary>
        public static List<string> Fit(List<string> lines, int max, string tryOut = "") {
            var fit = new List<string>(lines);
            bool hasTryOut = !string.IsNullOrEmpty(tryOut);
            while (fit.Count > max) {
                int header = fit.FindIndex(1, IsHeader);
                if (header > 1) {   // the orb's own lines sit between line 1 and the header
                    int position = fit.FindIndex(1, header - 1, l => IsPosition(l) && !(hasTryOut && l == tryOut));
                    if (position >= 0) { fit.RemoveAt(position); continue; }
                }
                if (header < 0 || fit[header] == ChangesHeader) {   // "no change" / "you own this": no block
                    int start = header >= 0 ? header + 1 : 1;
                    if (header < 0 && hasTryOut && start < fit.Count && fit[start] == tryOut) start++;   // its header has gone; it is not a change line
                    int end = fit.FindIndex(start, IsOutcome);
                    if (end < 0) end = fit.FindLastIndex(l => l.Contains(Separator));
                    if (end < 0) end = fit.Count - 1;
                    if (end > start) {
                        fit.RemoveAt(end - 1);
                        if (end - 1 == start && header >= 0) fit.RemoveAt(header);   // no dangling "With your build:"
                        continue;
                    }
                }
                int line = hasTryOut ? fit.IndexOf(tryOut, 1) : -1;
                if (line < 1) break;
                fit.RemoveAt(line);   // nothing else droppable is left
            }
            return fit;
        }
    }
}
