using System;
using System.Collections.Generic;
using System.Globalization;

namespace QuickshotRing {
    // Unity-free rules of the play-test range (TestRange*.cs; design docs/superpowers/specs/2026-11-18-test-range-design.md),
    // compiled into QuickshotRing.Tests (RangeTests.cs). Keep Unity types out of this file.
    //
    // Directions: yaw and bearings in degrees, clockwise seen from above, 0 = +Z (the game's forward), 90 = +X. A piece at
    // bearing b (right of the shooter positive) and distance d from the origin stands at x = d sin(yaw + b), z = d cos(yaw + b).

    /// <summary>The shipped defaults of the range's text settings (also used by the tests).</summary>
    internal static class RangeDefaults {
        public const string ArrowShelf = "Arrow x90; Arrow x90; Arrow x90; Spike Fancy/Copper x30; Spike Fancy/Iron x30; Spike Fancy/Silver x30; Spike Fancy/Mythril x30; Spike Fancy/Evinon Steel Alloy x30; Spike Tall/Iron x30; Spike Tall/Mythril x30; Small Bone Spike x30; Crystal Shard Blue x30; Explosive Spike x30; Healing Pod x15; Ka Karimata Arrow x30"; // R2: Etheral Arrow left out (it has no Pickup: no dock can hold it)
        public const string GearShelf = "Bow; Bow; Metal Bow; Metal Bow; Quiver+Arrow x90; Quiver+Arrow x90; Quiver+Arrow x90; Quiver+Arrow x90";
        // R2: built toward the way out (AutoFace); a long lane for Seeking Volley's 60 m reach and a lane of moving creatures
        // (laid out on the exported terrain of the archery shrine with tools/range_r2_plan.py: the far lanes on the facing's left, the near
        // ones on its right, so no backstop stands in the line to another lane's target; the whole fan is 68 degrees wide)
        public const string Lanes = "Dummies: combat_dummy @38 8,11,14 | Pack: Turabada @24 16,20L,20R,24 | Goteras: Gotera,Gotera Birch Variant,Gotera Ash Variant @10 26row4 | Homing: Turabada @-8 22,30L,38R,46 | Long: Turabada @-16 56 | Runners: Turabada @52 12,18 free";
    }

    /// <summary>One shelf slot as written in [TestRange] ArrowShelf / GearShelf: "Arrow x90" (a prefab, spawned as it is),
    /// "Spike Fancy/Mythril x30" (a known arrowhead in that metal, assembled on a wooden shaft), "Small Bone Spike x30" (a known
    /// arrowhead in its own material), "Bow" (one), "Quiver+Arrow x90" (a quiver holding 90 of the entry after the '+').</summary>
    internal sealed class ShelfEntry {
        public string Name = "";
        public string Material = "";   // crafted only; "" = the head's own material
        public int Count;              // 1..MaxCount
        public bool Crafted;           // Name is a known arrowhead: built on a wooden shaft
        public ShelfEntry? Inner;      // a container's contents ("Quiver+Arrow x90")
        public const int MaxCount = 90;

        /// <summary>The entry, or null with the reason.</summary>
        public static ShelfEntry? Parse(string? text, out string? error) {
            error = null;
            string t = (text ?? "").Trim();
            if (t.Length == 0) { error = "an empty slot entry"; return null; }
            int plus = t.IndexOf('+');
            if (plus >= 0) {
                ShelfEntry? outer = Parse(t.Substring(0, plus), out error);
                if (outer == null) return null;
                ShelfEntry? inner = Parse(t.Substring(plus + 1), out error);
                if (inner == null) { error = $"'{t}': its contents: {error}"; return null; }
                if (inner.Inner != null || outer.Crafted) { error = $"'{t}' is not a container followed by one item"; return null; }
                outer.Inner = inner;
                outer.Count = 1;
                return outer;
            }
            string name = t, num = "";
            int x = t.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            if (x > 0) { name = t.Substring(0, x).Trim(); num = t.Substring(x + 2).Trim(); }
            int count = 1;
            if (num.Length > 0 && !int.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out count)) { error = $"'{t}': '{num}' is not a count"; return null; }
            count = count < 1 ? 1 : count > MaxCount ? MaxCount : count;
            string material = "";
            int slash = name.IndexOf('/');
            if (slash >= 0) { material = name.Substring(slash + 1).Trim(); name = name.Substring(0, slash).Trim(); }
            if (name.Length == 0) { error = $"'{t}' names no item"; return null; }
            bool crafted = RangeKinds.IsHead(name);
            if (slash >= 0 && !crafted) { error = $"'{t}': '{name}' is not a known arrowhead, so it takes no material"; return null; }
            if (slash >= 0 && material.Length == 0) { error = $"'{t}' names no material after the '/'"; return null; }
            return new ShelfEntry { Name = name, Material = material, Count = count, Crafted = crafted };
        }

        /// <summary>Entries separated by ';' (empty parts skipped); every bad one is named in <paramref name="errors"/> and left out.</summary>
        public static List<ShelfEntry> ParseList(string? text, out List<string> errors) {
            var list = new List<ShelfEntry>();
            errors = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (string part in text!.Split(';')) {
                if (part.Trim().Length == 0) continue;
                ShelfEntry? e = Parse(part, out string? error);
                if (e != null) list.Add(e);
                else errors.Add(error ?? $"'{part.Trim()}'");
            }
            return list;
        }

        public override string ToString() =>
            Inner != null ? $"{Name}+{Inner}" : (Material.Length > 0 ? $"{Name}/{Material}" : Name) + (Count != 1 ? $" x{Count}" : "");
    }

    /// <summary>The prefabs the range knows by name (hashes from the exported prefabs' NetworkPrefab block; every one is checked
    /// by name against the game once at run time). Names are case-insensitive.</summary>
    internal static class RangeKinds {
        static readonly KeyValuePair<string, uint>[] heads = {
            P("Spike Fancy", 12492u), P("Spike Tall", 3558u), P("Spike Short", 6934u), P("Small Bone Spike", 61488u),
            P("Explosive Spike", 50412u), P("Healing Pod", 44360u), P("Crystal Shard Blue", 7092u),
        };
        static readonly KeyValuePair<string, uint>[] items = {
            P("Arrow", 56460u), P("Arrow Shaft Wooden", 29772u), P("Etheral Arrow", 52366u), P("Ka Karimata Arrow", 960u),
            P("Quiver", 64640u), P("Bow", 56546u), P("Metal Bow", 25100u), P("Bow Blade", 27416u), P("Pouch", 38942u),
        };
        static readonly KeyValuePair<string, uint>[] creatures = {
            P("Turabada", 13804u), P("Turabada Short Variant", 63878u), P("Turabada Large Variant", 41172u), P("Turabada Copper Variant", 39056u),
            P("Turabada Iron Variant", 8298u), P("Turabada Gold Variant", 37176u), P("Turabada Silver Variant", 45716u), P("Turabada Mythril Variant", 62968u),
            P("Gotera", 9466u), P("Gotera Birch Variant", 2370u), P("Gotera Walnut Variant", 38096u), P("Gotera Ash Variant", 61448u), P("Gotera Redwood Variant", 27128u),
            P("combat_dummy", 57126u), P("Fake Player combat_dummy", 45454u),
        };
        static readonly string[] dummies = { "combat_dummy", "Fake Player combat_dummy" };

        static KeyValuePair<string, uint> P(string n, uint h) => new KeyValuePair<string, uint>(n, h);

        static uint Find(KeyValuePair<string, uint>[] table, string? name) {
            string n = (name ?? "").Trim();
            foreach (KeyValuePair<string, uint> kv in table) if (string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return 0u;
        }

        public static bool IsHead(string? name) => Find(heads, name) != 0u;
        public static uint HeadHash(string? name) => Find(heads, name);
        public static uint ItemHash(string? name) => Find(items, name);
        public static uint CreatureHash(string? name) => Find(creatures, name);
        public static bool IsDummy(string? name) {
            string n = (name ?? "").Trim();
            foreach (string d in dummies) if (string.Equals(d, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal enum RefillKind { Nothing, Add, WrongKind }

    /// <summary>Keeping a shelf slot full ("unlimited" = refilling).</summary>
    internal static class RefillRule {
        /// <summary><paramref name="stored"/>: what the dock holds; <paramref name="kindMatches"/>: it holds the slot's own kind;
        /// <paramref name="room"/>: how many more the dock can take. An empty dock gets its target; a slot holding something else
        /// (a player put it there) is never touched (WrongKind: leave it, log once).</summary>
        public static RefillKind Need(int stored, int target, bool kindMatches, int room, out int add) {
            add = 0;
            if (stored > 0 && !kindMatches) return RefillKind.WrongKind;
            if (stored >= target || room <= 0) return RefillKind.Nothing;
            add = Math.Min(target - Math.Max(0, stored), room);
            return add > 0 ? RefillKind.Add : RefillKind.Nothing;
        }
    }

    internal enum PostAction { Wait, PutBack, Respawn, Spawn }

    /// <summary>What happens to one creature spot of the range on this tick.</summary>
    internal static class RespawnRule {
        /// <summary><paramref name="exists"/>: its entity is in the world; <paramref name="alive"/>: and alive; <paramref name="downFor"/>:
        /// seconds since it was first seen dead or gone; <paramref name="distance"/>: from its spot, level; <paramref name="playerNear"/>:
        /// a player is near enough for the area to be loaded; <paramref name="neverDies"/>: a combat dummy.
        /// Dead: after <paramref name="respawnSeconds"/> the body is removed and a new one spawned (Respawn); 0 or less never.
        /// Gone (unloaded, destroyed): spawned again after the same wait, only while a player is near. A free creature farther than
        /// <paramref name="leashMetres"/> from its spot is put back (removed and spawned there); a frozen one never; 0 = no leash.</summary>
        public static PostAction Next(bool exists, bool alive, float downFor, float respawnSeconds, float leashMetres, float distance, bool frozen, bool playerNear, bool neverDies) {
            bool respawns = respawnSeconds > 0f; // false for NaN too
            if (exists && (alive || neverDies)) {
                if (!frozen && !neverDies && leashMetres > 0f && distance > leashMetres) return PostAction.PutBack;
                return PostAction.Wait;
            }
            if (!respawns || !(downFor >= respawnSeconds)) return PostAction.Wait;
            if (exists) return PostAction.Respawn;
            return playerNear ? PostAction.Spawn : PostAction.Wait;
        }
    }

    /// <summary>The owner's decision (2026-11-18): the range builds only near the archery shrine.</summary>
    internal static class ShrineRule {
        public static string? Refusal(bool found, float distance, float maxMetres) {
            if (!found) return "no archery shrine is loaded near you: the range builds only at the archery shrine (press 'Teleport to the archery shrine')";
            if (float.IsNaN(distance) || distance > maxMetres)
                return $"you are {(float.IsNaN(distance) ? "?" : distance.ToString("0", CultureInfo.InvariantCulture))} m from the archery shrine; the range builds only within {maxMetres.ToString("0", CultureInfo.InvariantCulture)} m of it";
            return null;
        }
    }

    /// <summary>Where a range piece may stand.</summary>
    internal static class PlacementRule {
        /// <summary>Null when the spot is fine, else the plain reason: ground steeper than <paramref name="maxSlope"/> degrees, more
        /// than <paramref name="maxStep"/> m above or below the shooter's ground (a roof, a cliff), another player's head within
        /// <paramref name="clearMetres"/>.</summary>
        public static string? Check(float slopeDegrees, float stepMetres, float nearestOtherPlayer, float maxSlope, float maxStep, float clearMetres) {
            if (float.IsNaN(slopeDegrees) || slopeDegrees > maxSlope) return $"the ground is too steep ({F(slopeDegrees)} degrees, at most {F(maxSlope)})";
            if (float.IsNaN(stepMetres) || Math.Abs(stepMetres) > maxStep) return $"the ground is {F(Math.Abs(stepMetres))} m {(stepMetres > 0f ? "above" : "below")} yours (a roof or a cliff; at most {F(maxStep)})";
            if (nearestOtherPlayer < clearMetres) return $"another player stands {F(nearestOtherPlayer)} m from the spot";
            return null;
        }

        static string F(float v) => float.IsNaN(v) ? "?" : v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>UserData\QuickshotRing\range-status.txt: what the server last did with a range request, read by the window.</summary>
    internal static class RangeStatus {
        public static string Format(string number, string result, string text, string state, string time) =>
            $"number={Clean(number)}\nresult={Clean(result)}\ntext={Clean(text)}\nstate={Clean(state)}\ntime={Clean(time)}\n";

        static string Clean(string? s) => (s ?? "").Replace('\r', ' ').Replace('\n', ' ');

        /// <summary>key -> value of a status file's lines.</summary>
        public static Dictionary<string, string> Parse(string? text) {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return d;
            foreach (string line in text!.Split('\n')) {
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1).TrimEnd('\r');
            }
            return d;
        }
    }

    /// <summary>"For everyone": every online player in turn, each gated exactly as one player's buy request (BuyGate, Build.cs).</summary>
    internal static class EveryoneRule {
        /// <summary>The players ready now (in the given order) and the rest as "name (why)"; each with its gate's refusal (null = ready,
        /// TestTools.ReadyFor on the server, BuyGate.Refusal of its facts).</summary>
        public static void Split(IList<KeyValuePair<string, string?>> players, out List<string> ready, out List<string> skipped) {
            ready = new List<string>();
            skipped = new List<string>();
            foreach (KeyValuePair<string, string?> p in players) {
                if (p.Value == null) ready.Add(p.Key);
                else skipped.Add($"{p.Key} ({p.Value})");
            }
        }

        /// <summary>H4 (range C-1): why a buy-for-everyone list must not run (null = it may). It must name at least one orb besides the
        /// gatherer (which every buy keeps anyway): "sell everything" is never sent to everyone; a name that is not an orb refuses it all.</summary>
        public static string? BuyRefusal(string? text) {
            List<Orb> wanted = TestBuildRule.Parse(text, out List<string> unknown);
            if (unknown.Count > 0) return $"not orb names: {string.Join(", ", unknown.ToArray())}";
            if (!wanted.Exists(o => o != Orb.Gatherer)) return "Buy for all never sells everyone's orbs: the list must name an orb to buy ('Sell all archery orbs' is for one player only)";
            return null;
        }

        /// <summary>H4 (range C-1): the players a buy for everyone touches: only those whose snapshot was written first (<paramref name="files"/>[i]
        /// null or missing = not written); the rest are added to <paramref name="skipped"/> as "name (why)". Order kept.</summary>
        public static List<string> WithSnapshots(IList<string> ready, IList<string?> files, List<string> skipped) {
            var buy = new List<string>();
            for (int i = 0; i < ready.Count; i++) {
                if (i < files.Count && !string.IsNullOrEmpty(files[i])) buy.Add(ready[i]);
                else skipped.Add($"{ready[i]} (their snapshot could not be written, so nothing was changed for them)");
            }
            return buy;
        }
    }

    /// <summary>H4 (range C-1): UserData\QuickshotRing\range-online.txt, the players online as the server sees them, so the window's
    /// "Buy for all" question can say how many players it touches. Written by the server (only while [TestRange] Enabled) when it changes.</summary>
    internal static class RangeOnline {
        /// <summary><paramref name="players"/>: name and its gate's refusal (null = ready, fully loaded).</summary>
        public static string Format(IList<KeyValuePair<string, string?>> players, string state, string time) {
            var ready = new List<string>();
            var not = new List<string>();
            foreach (KeyValuePair<string, string?> p in players) {
                string name = Clean(p.Key);
                if (p.Value == null) ready.Add(name);
                else not.Add($"{name} ({Clean(p.Value)})");
            }
            return $"state={Clean(state)}\nready={ready.Count}\nnames={string.Join(", ", ready.ToArray())}\nnotready={string.Join("; ", not.ToArray())}\ntime={Clean(time)}\n";
        }

        static string Clean(string? s) => (s ?? "").Replace('\r', ' ').Replace('\n', ' ');
    }

    internal enum RemoveAction { Nothing, Destroy, DropBeside }

    /// <summary>H4 (range I-2, I-3): what is the range's own (range stock): every item it spawned onto a dock and the copy a stacked range
    /// dock makes of one taken off it (the game's CreateNewImage). Nothing else is ever made never-saved, tracked or destroyed by the range.</summary>
    internal static class RangeStock {
        /// <summary>A range dock (every dock of a range shelf, the inner dock of a range quiver) takes range stock only.</summary>
        public static bool DockAccepts(bool isStock) => isStock;

        /// <summary>An item left a range dock. True: it is range stock, so it is made never-saved (and tracked when it is an arrow).
        /// <paramref name="imageIsStock"/>: the dock's next copy of the stack (<paramref name="dockHoldsAnImage"/>) is range stock too.</summary>
        public static bool OnTaken(bool takenIsStock, bool dockHoldsAnImage, out bool imageIsStock) {
            imageIsStock = takenIsStock && dockHoldsAnImage;
            return takenIsStock;
        }

        /// <summary>Removing the range: range stock goes with its shelf; anything else is put down beside it with its own saving.</summary>
        public static RemoveAction OnRemove(int stored, bool isStock) => stored <= 0 ? RemoveAction.Nothing : isStock ? RemoveAction.Destroy : RemoveAction.DropBeside;

        /// <summary>The pouch the game makes for a stack taken off a range dock: never saved only while it lies loose (in a bag it is the
        /// player's), holds range stock, and the stack left the shelf within <paramref name="window"/> seconds.</summary>
        public static bool PouchNeverSaved(bool pouchDocked, bool holdsStock, float secondsSinceTaken, float window) =>
            !pouchDocked && holdsStock && secondsSinceTaken >= 0f && secondsSinceTaken <= window;
    }

    /// <summary>"Everyone gets points": each player's unspent Ranged points kept at TestBuildRule.TopUpTo(asked, cap) (one below the
    /// game's banked-points cap at most: at the cap the game pays no experience); a higher balance is never lowered.</summary>
    internal static class PointsRule {
        public static uint TopUp(uint had, int asked, uint cap) {
            int want = TestBuildRule.TopUpTo(asked, cap);
            return want > 0 && had < (uint)want ? (uint)want : had;
        }

        /// <summary>H4 (range I-1): the Ranged level once the unspent points are <paramref name="pointsAfter"/>. A level pays for the orbs
        /// owned and the points not spent (each level-up gives one point; PathExperience.LockSlot lowers the unsigned level by a sold orb's
        /// cost), so it is raised to cover both (BuyRule.LevelToCover, a wrapped level repaired first as TestTools does); never lowered.</summary>
        public static uint Level(uint level, uint ownedCost, uint pointsAfter) {
            uint l = BuyRule.RepairLevel(level, ownedCost, pointsAfter, out _);
            return BuyRule.LevelToCover(l, ownedCost + pointsAfter);
        }
    }

    /// <summary>What "Reset archery progress" does to one player's Ranged path (<see cref="ResetRule.Plan"/>).</summary>
    internal sealed class ResetPlan {
        /// <summary>Every owned Ranged slot, leaves first (no slot still to be sold depends on it), the vanilla orbs too.</summary>
        public readonly List<uint> Sell = new List<uint>();
        public readonly uint Level = 0u, Experience = 0u, Points = 0u;   // all end at 0
        public bool ForgetPractised = true, ForgetMainPath = true;
    }

    /// <summary>Reset (archery only, one named player) and its undo, restoring a snapshot. Slots by hash; deps = each slot's parents
    /// ("any of").</summary>
    internal static class ResetRule {
        static uint[] Parents(IReadOnlyDictionary<uint, uint[]> deps, uint s) => deps.TryGetValue(s, out uint[] p) && p != null ? p : new uint[0];

        public static ResetPlan Plan(IEnumerable<uint> owned, IReadOnlyDictionary<uint, uint[]> deps) {
            var plan = new ResetPlan();
            var left = new List<uint>();
            foreach (uint s in owned) if (!left.Contains(s)) left.Add(s);
            while (left.Count > 0) {
                int pick = -1;
                for (int i = left.Count - 1; i >= 0 && pick < 0; i--) {
                    bool needed = false;
                    foreach (uint other in left) if (other != left[i] && Array.IndexOf(Parents(deps, other), left[i]) >= 0) { needed = true; break; }
                    if (!needed) pick = i;
                }
                if (pick < 0) pick = left.Count - 1; // a loop of parents: cannot happen in a shrine tree
                plan.Sell.Add(left[pick]);
                left.RemoveAt(pick);
            }
            return plan;
        }

        /// <summary>The order to buy a snapshot's slots back in: each once its parent is bought (or it has none); a slot none of whose
        /// parents is in the list comes last, in the saved order (the game may refuse it).</summary>
        public static List<uint> BuyOrder(IEnumerable<uint> owned, IReadOnlyDictionary<uint, uint[]> deps) {
            var pending = new List<uint>();
            foreach (uint s in owned) if (!pending.Contains(s)) pending.Add(s);
            var order = new List<uint>();
            for (bool grew = true; grew;) {
                grew = false;
                foreach (uint s in pending.ToArray()) {
                    uint[] ps = Parents(deps, s);
                    bool ready = ps.Length == 0;
                    foreach (uint d in ps) if (order.Contains(d)) { ready = true; break; }
                    if (!ready) continue;
                    order.Add(s); pending.Remove(s); grew = true;
                }
            }
            order.AddRange(pending);
            return order;
        }

        /// <summary>H4 (range C-2): nothing a Reset (or a Snapshot) would keep: no archery orb owned, no experience, no practised orb. The
        /// level and the unspent points are left out: with no orb and no experience they are only unspent points (Everyone gets points
        /// hands them back within 2 s). Refusing here keeps a repeat press from writing an empty snapshot newer than the real one.</summary>
        public static bool NothingToReset(int owned, uint experience, int practised) => owned <= 0 && experience == 0u && practised <= 0;

        /// <summary>H4 (range minor 6): which of a snapshot's slots, in buy order (parents first), Restore buys back within the game's
        /// skill cap: <paramref name="room"/> = the cap less the skills owned in other professions (int.MaxValue: no cap; 0 or less:
        /// none). The rest go to <paramref name="over"/> (refunded).</summary>
        public static List<uint> WithinRoom(IList<uint> order, int room, out List<uint> over) {
            var buy = new List<uint>();
            over = new List<uint>();
            foreach (uint s in order) {
                if (buy.Count < room) buy.Add(s);
                else over.Add(s);
            }
            return buy;
        }

        /// <summary>H4 (range minor 6): the points and level a restore leaves. Every orb of the snapshot it could not buy back is refunded
        /// (<paramref name="refund"/> = the snapshot's owned cost less the cost bought back, added to its points), and the level covers
        /// what is owned plus the points (never lowered below the snapshot's), so a later sale cannot wrap it.</summary>
        public static void RestoreNumbers(uint snapLevel, uint snapPoints, uint snapOwnedCost, uint boughtCost, out uint points, out uint level, out uint refund) {
            refund = snapOwnedCost > boughtCost ? snapOwnedCost - boughtCost : 0u;
            points = snapPoints + refund;
            level = BuyRule.LevelToCover(snapLevel, boughtCost + points);
        }
    }

    /// <summary>H4 (range C-2): the snapshot files. One folder per account (range-snapshots\archery-&lt;account&gt;), one file per snapshot,
    /// named by the time it was taken and what took it ("20261118-101500-123 reset.txt"), never overwritten; the newest
    /// <see cref="Keep"/> per account are kept.</summary>
    internal static class SnapshotFiles {
        public const int Keep = 10;
        public const string Reset = "reset", Snapshot = "snapshot", BeforeRestore = "before-restore", BuyForAll = "buy-for-all";

        /// <summary>The account's folder (anything a file name cannot hold becomes '_').</summary>
        public static string FolderFor(string? key) {
            var sb = new System.Text.StringBuilder("archery-");
            foreach (char c in key ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == ' ' ? c : '_');
            return sb.ToString();
        }

        /// <summary>A new file name for a snapshot taken at <paramref name="when"/> by <paramref name="kind"/>, never one of <paramref name="existing"/>.</summary>
        public static string NewName(DateTime when, string kind, ICollection<string> existing) {
            string stamp = when.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string k = Kind(kind);
            string name = $"{stamp} {k}.txt";
            for (int n = 2; Contains(existing, name); n++) name = $"{stamp}-{n} {k}.txt";
            return name;
        }

        static string Kind(string? kind) {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (kind ?? "").ToLowerInvariant()) if ((c >= 'a' && c <= 'z') || c == '-') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : Snapshot;
        }

        static bool Contains(ICollection<string> names, string name) {
            foreach (string n in names) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The time stamp, counter and kind of a snapshot file name; false for any other file.</summary>
        static bool Split(string? name, out string stamp, out int counter, out string kind) {
            stamp = ""; counter = 1; kind = "";
            string n = name ?? "";
            if (!n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || n.Length < 25) return false;
            n = n.Substring(0, n.Length - 4);
            int space = n.IndexOf(' ');
            if (space < 19) return false;
            string head = n.Substring(0, space);
            kind = n.Substring(space + 1);
            if (kind.Length == 0) return false;
            foreach (char c in kind) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '-')) return false;
            stamp = head.Substring(0, 19);
            for (int i = 0; i < 19; i++) if (i == 8 || i == 15 ? stamp[i] != '-' : !char.IsDigit(stamp[i])) return false;
            if (head.Length == 19) return true;
            return head[19] == '-' && int.TryParse(head.Substring(20), NumberStyles.None, CultureInfo.InvariantCulture, out counter) && counter >= 2;
        }

        public static bool IsSnapshot(string? name) => Split(name, out _, out _, out _);

        public static string KindOf(string? name) => Split(name, out _, out _, out string kind) ? kind.ToLowerInvariant() : "";

        /// <summary>The snapshot files among <paramref name="names"/>, newest first (other files left out).</summary>
        public static List<string> NewestFirst(IEnumerable<string> names) {
            var list = new List<KeyValuePair<string, KeyValuePair<string, int>>>();
            foreach (string n in names) if (Split(n, out string stamp, out int counter, out _)) list.Add(new KeyValuePair<string, KeyValuePair<string, int>>(n, new KeyValuePair<string, int>(stamp, counter)));
            list.Sort((a, b) => {
                int c = string.CompareOrdinal(b.Value.Key, a.Value.Key);
                return c != 0 ? c : b.Value.Value.CompareTo(a.Value.Value);
            });
            return list.ConvertAll(x => x.Key);
        }

        /// <summary>The snapshot files to delete so that only the newest <paramref name="keep"/> remain.</summary>
        public static List<string> ToPrune(IEnumerable<string> names, int keep) {
            List<string> newest = NewestFirst(names);
            return newest.Count > keep ? newest.GetRange(Math.Max(0, keep), newest.Count - Math.Max(0, keep)) : new List<string>();
        }

        /// <summary>The file Restore uses from this account's files (<paramref name="newestFirst"/>): the one <paramref name="asked"/> names
        /// (".txt" optional, any case; only a bare file name), else the newest that is not a before-restore copy (those are restored only
        /// when named). Null with the reason (naming the newest files there are).</summary>
        public static string? Pick(IList<string> newestFirst, string? asked, out string? why) {
            why = null;
            string a = (asked ?? "").Trim();
            if (a.Length > 0) {
                if (a.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) { why = $"'{a}' is not a snapshot file name (give the file name only)"; return null; }
                if (!a.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) a += ".txt";
                foreach (string n in newestFirst) if (string.Equals(n, a, StringComparison.OrdinalIgnoreCase)) return n;
                why = $"this player has no snapshot '{a}'{Have(newestFirst)}";
                return null;
            }
            foreach (string n in newestFirst) if (KindOf(n) != BeforeRestore) return n;
            why = newestFirst.Count == 0 ? "no snapshot for this player (Reset, Snapshot and Buy for all write one)" : $"only before-restore copies, which are restored only when named{Have(newestFirst)}";
            return null;
        }

        static string Have(IList<string> newestFirst) {
            if (newestFirst.Count == 0) return "";
            var some = new List<string>();
            for (int i = 0; i < newestFirst.Count && i < 3; i++) some.Add(newestFirst[i]);
            return $" (newest: {string.Join(", ", some.ToArray())})";
        }
    }

    /// <summary>One player's archery progress, written before a reset, a buy for everyone, a restore and on Snapshot, read back by
    /// Restore: one file per snapshot in range-snapshots/archery-&lt;account&gt;/ in the mod's UserData folder (<see cref="SnapshotFiles"/>),
    /// "key=value" lines.</summary>
    internal sealed class ArcherySnapshot {
        public string Name = "", Key = "", MainPath = "None", Taken = "";
        public uint Level, Experience, Points;
        public uint OwnedCost;      // H4: the skill points the owned orbs cost when it was taken (Restore refunds what it cannot buy back)
        public bool HasOwnedCost;   // false: an older file without the line
        public readonly List<uint> Owned = new List<uint>(), Practised = new List<uint>();

        static string Line(string s) => (s ?? "").Replace('\r', ' ').Replace('\n', ' ');
        static string Join(List<uint> l) => string.Join(",", l.ConvertAll(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());

        public string Format() =>
            $"name={Line(Name)}\nkey={Line(Key)}\ntaken={Line(Taken)}\nlevel={Level}\nexperience={Experience}\npoints={Points}\n{(HasOwnedCost ? $"ownedcost={OwnedCost}\n" : "")}main={Line(MainPath)}\nowned={Join(Owned)}\npractised={Join(Practised)}\n";

        /// <summary>H4 (range C-2): the same archery progress (the same orbs in any order, level, experience and unspent points); a
        /// restore of it would change nothing that matters.</summary>
        public bool SameProgress(ArcherySnapshot other) {
            if (other == null || Level != other.Level || Experience != other.Experience || Points != other.Points) return false;
            var a = new HashSet<uint>(Owned);
            return a.SetEquals(other.Owned);
        }

        public static ArcherySnapshot? Parse(string? text, out string? error) {
            error = null;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in (text ?? "").Split('\n')) {
                int eq = raw.IndexOf('=');
                if (eq > 0) d[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).TrimEnd('\r');
            }
            foreach (string k in new[] { "key", "level", "experience", "points" })
                if (!d.ContainsKey(k)) { error = $"no '{k}=' line: not an archery snapshot"; return null; }
            var s = new ArcherySnapshot { Key = d["key"], Name = d.TryGetValue("name", out string n) ? n : "", Taken = d.TryGetValue("taken", out string t) ? t : "" };
            if (d.TryGetValue("main", out string m) && m.Trim().Length > 0) s.MainPath = m.Trim();
            if (!U(d["level"], out s.Level)) { error = $"level '{d["level"]}' is not a number"; return null; }
            if (!U(d["experience"], out s.Experience)) { error = $"experience '{d["experience"]}' is not a number"; return null; }
            if (!U(d["points"], out s.Points)) { error = $"points '{d["points"]}' is not a number"; return null; }
            if (d.TryGetValue("ownedcost", out string oc)) {
                if (!U(oc, out s.OwnedCost)) { error = $"ownedcost '{oc}' is not a number"; return null; }
                s.HasOwnedCost = true;
            }
            if (!List(d, "owned", s.Owned, out error) || !List(d, "practised", s.Practised, out error)) return null;
            return s;
        }

        static bool U(string s, out uint v) => uint.TryParse((s ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v);

        static bool List(Dictionary<string, string> d, string key, List<uint> into, out string? error) {
            error = null;
            if (!d.TryGetValue(key, out string text)) return true;
            foreach (string part in text.Split(',')) {
                if (part.Trim().Length == 0) continue;
                if (!U(part, out uint v)) { error = $"{key}: '{part.Trim()}' is not a slot"; return false; }
                into.Add(v);
            }
            return true;
        }

    }

    /// <summary>[TestRange] MaxLooseArrows: range arrows lying loose (on the ground, stuck in things; never one held or docked in a
    /// quiver, bag or on a string) past the cap are removed, the oldest first.</summary>
    internal static class LooseCap {
        /// <summary>H4 (range minor 1): when a tracked range arrow counts as loose from: the first pass that sees it loose (so an arrow
        /// put in a quiver long ago and shot now is the newest loose one, not the oldest); NaN while it is held or docked.</summary>
        public static float Stamp(bool looseNow, float since, float now) => !looseNow ? float.NaN : float.IsNaN(since) ? now : since;

        /// <summary>The indices to remove, oldest first; <paramref name="max"/> below 0 = no cap.</summary>
        public static List<int> Evict(IList<bool> loose, IList<float> since, int max) {
            var picked = new List<int>();
            if (max < 0) return picked;
            int n = Math.Min(loose.Count, since.Count);
            var candidates = new List<int>();
            for (int i = 0; i < n; i++) if (loose[i]) candidates.Add(i);
            int over = candidates.Count - max;
            if (over <= 0) return picked;
            float Key(int i) => float.IsNaN(since[i]) ? float.PositiveInfinity : since[i]; // not yet stamped: the newest
            candidates.Sort((a, b) => Key(a) != Key(b) ? Key(a).CompareTo(Key(b)) : a.CompareTo(b));
            for (int i = 0; i < over; i++) picked.Add(candidates[i]);
            return picked;
        }
    }

    /// <summary>Noon and the shrine spot.</summary>
    internal static class NoonRule {
        /// <summary>The game's time of day for an hour on the 24 h clock (the console's "time set": (hour - 6) / 24, wrapped); NaN = noon.</summary>
        public static float DayFraction(float hour) {
            if (float.IsNaN(hour) || float.IsInfinity(hour)) hour = 12f;
            float f = (hour - 6f) / 24f;
            f -= (float)Math.Floor(f);
            return f;
        }

        /// <summary>"x, y, z" in metres.</summary>
        public static bool Spot(string? text, out float x, out float y, out float z) {
            x = y = z = 0f;
            string[] p = (text ?? "").Split(',');
            if (p.Length != 3) return false;
            return F(p[0], out x) && F(p[1], out y) && F(p[2], out z);
        }

        static bool F(string s, out float v) => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
    }

    internal sealed class LaneSpot {
        public float Distance;
        public float Side;     // metres to the lane's right (negative: left)
        public float RowGap;   // > 0: the lane's kinds stand across the lane at this distance, this far apart
    }

    internal sealed class Lane {
        public string Name = "";
        public List<string> Kinds = new List<string>();
        public float Bearing;
        public List<LaneSpot> Spots = new List<LaneSpot>();
        public bool Free;    // R2: "free" after the spots: this lane's creatures are never frozen (a moving lane; leashed as all free ones)
        public bool NoWall;  // R2: "nowall": no backstop behind this lane
    }

    internal struct RangePiece {
        public string Lane, Kind;
        public int Index;
        public float X, Z, FacingDegrees;
        public float Bearing;  // R2: the lane's world bearing (yaw + lane bearing): "along the lane" for slides and backstops
        public bool Free;      // R2: the lane's creatures move (never frozen)
    }

    /// <summary>[TestRange] Lanes: "Name: kind[,kind...] @bearing spot,spot,... | ...". A spot is a distance in metres, with "L" or
    /// "R" for 2 m (or "L3.5": that many) to that side of the lane, or "rowN": the kinds stand across the lane at that distance,
    /// N m apart. Kinds: one for every spot, or one per spot (or per row place).</summary>
    internal static class RangeLayout {
        public const float DefaultSide = 2f;

        static bool Num(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);

        public static List<Lane> Parse(string? text, out List<string> errors) {
            var lanes = new List<Lane>();
            errors = new List<string>();
            if (string.IsNullOrEmpty(text)) return lanes;
            foreach (string raw in text!.Split('|')) {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                string? why = null;
                Lane? lane = ParseLane(part, ref why);
                if (lane != null) lanes.Add(lane);
                else errors.Add($"lane '{part}': {why}");
            }
            return lanes;
        }

        static Lane? ParseLane(string part, ref string? why) {
            int colon = part.IndexOf(':');
            if (colon <= 0) { why = "no 'Name:' at the start"; return null; }
            var lane = new Lane { Name = part.Substring(0, colon).Trim() };
            string rest = part.Substring(colon + 1);
            int at = rest.IndexOf('@');
            if (at < 0) { why = "no '@bearing'"; return null; }
            foreach (string k in rest.Substring(0, at).Split(',')) if (k.Trim().Length > 0) lane.Kinds.Add(k.Trim());
            if (lane.Kinds.Count == 0) { why = "no kind before the '@'"; return null; }
            string[] tail = rest.Substring(at + 1).Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (tail.Length < 2) { why = "no spots after the bearing"; return null; }
            if (!Num(tail[0], out lane.Bearing)) { why = $"'{tail[0]}' is not a bearing in degrees"; return null; }
            int places = 0;
            foreach (string s0 in tail[1].Split(',')) {
                string s = s0.Trim();
                if (s.Length == 0) continue;
                // R2: flags after a spot ("14,20 free nowall"): the words after the first are flags
                string[] words = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int w = 1; w < words.Length; w++) {
                    if (words[w].Equals("free", StringComparison.OrdinalIgnoreCase)) lane.Free = true;
                    else if (words[w].Equals("nowall", StringComparison.OrdinalIgnoreCase)) lane.NoWall = true;
                    else { why = $"'{s}' is not a spot ('{words[w]}' is neither a spot nor a flag: free, nowall)"; return null; }
                }
                s = words[0];
                var spot = new LaneSpot();
                int row = s.IndexOf("row", StringComparison.OrdinalIgnoreCase);
                int lr = s.IndexOfAny(new[] { 'L', 'R', 'l', 'r' });
                string dist = s;
                if (row > 0) {
                    dist = s.Substring(0, row);
                    if (!Num(s.Substring(row + 3), out spot.RowGap) || spot.RowGap <= 0f) { why = $"'{s}': the row gap is not a positive number"; return null; }
                    places += lane.Kinds.Count;
                }
                else if (lr > 0) {
                    dist = s.Substring(0, lr);
                    float side = DefaultSide;
                    string more = s.Substring(lr + 1);
                    if (more.Length > 0 && !Num(more, out side)) { why = $"'{s}' is not a spot"; return null; }
                    spot.Side = char.ToUpperInvariant(s[lr]) == 'L' ? -Math.Abs(side) : Math.Abs(side);
                    places++;
                }
                else places++;
                if (!Num(dist, out spot.Distance) || spot.Distance < 0f) { why = $"'{s}' is not a spot"; return null; }
                lane.Spots.Add(spot);
            }
            if (lane.Spots.Count == 0) { why = "no spots"; return null; }
            bool rows = lane.Spots.Exists(s => s.RowGap > 0f);
            if (!rows && lane.Kinds.Count != 1 && lane.Kinds.Count != places) { why = $"{lane.Kinds.Count} kinds for {places} spots (one kind, or one per spot)"; return null; }
            if (rows && lane.Spots.Exists(s => s.RowGap <= 0f) && lane.Kinds.Count != 1) { why = "a lane mixing rows and single spots takes one kind"; return null; }
            return lane;
        }

        static float Rad(float deg) => deg * (float)Math.PI / 180f;

        /// <summary>Degrees from (x, z) toward (tx, tz), in this file's convention.</summary>
        public static float Toward(float x, float z, float tx, float tz) => (float)(Math.Atan2(tx - x, tz - z) * 180.0 / Math.PI);

        /// <summary>Every creature spot of the lanes around the origin, facing the origin; Index counts within its lane.</summary>
        public static List<RangePiece> Spots(IList<Lane> lanes, float originX, float originZ, float yawDegrees) {
            var list = new List<RangePiece>();
            foreach (Lane lane in lanes) {
                float a = Rad(yawDegrees + lane.Bearing);
                float fx = (float)Math.Sin(a), fz = (float)Math.Cos(a);   // along the lane
                float rx = (float)Math.Cos(a), rz = -(float)Math.Sin(a);  // the lane's right
                int index = 0;
                foreach (LaneSpot s in lane.Spots) {
                    if (s.RowGap > 0f) {
                        int k = lane.Kinds.Count;
                        for (int i = 0; i < k; i++) {
                            float side = (i - (k - 1) * 0.5f) * s.RowGap;
                            list.Add(Make(lane, lane.Kinds[i], index++, originX + fx * s.Distance + rx * side, originZ + fz * s.Distance + rz * side, originX, originZ, yawDegrees + lane.Bearing));
                        }
                        continue;
                    }
                    string kind = lane.Kinds.Count == 1 ? lane.Kinds[0] : lane.Kinds[Math.Min(index, lane.Kinds.Count - 1)];
                    list.Add(Make(lane, kind, index++, originX + fx * s.Distance + rx * s.Side, originZ + fz * s.Distance + rz * s.Side, originX, originZ, yawDegrees + lane.Bearing));
                }
            }
            return list;
        }

        /// <summary>Every pair of pieces of different lanes closer than <paramref name="metres"/> (a Ricochet jump, a Shatter blast or a
        /// lock in one lane would reach the other): one warning each, "Lane n 'kind' and Lane m 'kind' are d m apart".</summary>
        public static List<string> TooClose(IList<RangePiece> pieces, float metres) {
            var warnings = new List<string>();
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++) {
                    RangePiece a = pieces[i], b = pieces[j];
                    if (a.Lane == b.Lane) continue;
                    double d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
                    if (d < metres) warnings.Add($"{a.Lane} {a.Index + 1} '{a.Kind}' and {b.Lane} {b.Index + 1} '{b.Kind}' are {d.ToString("0.#", CultureInfo.InvariantCulture)} m apart (under {metres.ToString("0.#", CultureInfo.InvariantCulture)}: a jump, blast or lock in one lane can reach the other)");
                }
            return warnings;
        }

        static RangePiece Make(Lane lane, string kind, int index, float x, float z, float ox, float oz, float bearing) =>
            new RangePiece { Lane = lane.Name, Kind = kind, Index = index, X = x, Z = z, FacingDegrees = Toward(x, z, ox, oz), Bearing = bearing, Free = lane.Free };

        /// <summary>A shelf one step to the shooter's right (the arrow shelf) or left (the gear shelf), facing the shooter.</summary>
        public static void Shelf(float originX, float originZ, float yawDegrees, float sideMetres, bool right, out float x, out float z, out float facingDegrees) {
            float a = Rad(yawDegrees);
            float s = right ? sideMetres : -sideMetres;
            x = originX + (float)Math.Cos(a) * s;
            z = originZ - (float)Math.Sin(a) * s;
            facingDegrees = Toward(x, z, originX, originZ);
        }
    }
}
