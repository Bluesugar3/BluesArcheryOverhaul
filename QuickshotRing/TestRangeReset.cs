using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alta.Global;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using MelonLoader;

namespace QuickshotRing {
    // Reset archery progress, with Snapshot / Restore as its undo (TestRange.cs has the overview). Archery (Ranged) only, one named
    // player ([TestRange] ResetPlayer, empty = [TestBuild] Player, captured at the press), online and fully loaded (TestTools.ReadyFor);
    // the window asks "are you sure" first. Reset ALWAYS writes a snapshot first and refuses if it cannot. It sells every Ranged orb
    // (the game's own too; leaves first, PathExperience.LockSlot with no refund, the level kept high during the sales as the test
    // buy does), sets the Ranged level, experience and unspent points to 0, forgets the Ranged practised orbs and the remembered
    // main path, refreshes once and writes the save. Restore sells the same way, buys the snapshot's orbs back (parents first, each
    // marked practised), puts level, experience, points, practised orbs and main path back, and writes the save.
    //
    // H4 (range review C-2, minors 6 and 7): snapshots are never overwritten. Each is its own file in
    // range-snapshots\archery-<account>\, named by when it was taken and what took it (SnapshotFiles, Range.cs: "... reset",
    // "... snapshot", "... buy-for-all", "... before-restore"); the newest SnapshotFiles.Keep (10) per account are kept. Reset and
    // Snapshot refuse when there is nothing to keep (ResetRule.NothingToReset: no orb, no experience, no practised orb), so a repeat
    // press never writes an empty state newer than the real one. Restore takes [TestRange] RestoreFile (a file name, captured at the
    // press) or else the newest that is not a before-restore copy (SnapshotFiles.Pick), only from the named player's own folder and only
    // when the file's account key is theirs; it refuses when the player already has exactly that progress (a repeat press changes
    // nothing). What it replaces is just another snapshot (before-restore). It buys back only within the game's skill cap across
    // professions (ResetRule.WithinRoom) and refunds every orb it could not buy back as points (ResetRule.RestoreNumbers). The build
    // batch always ends in a finally.
    internal static partial class TestRange {
        static MelonPreferences_Entry<string> resetPlayer = null!, restoreFile = null!;
        static AccessTools.FieldRef<PathExperience, uint>? xpRef, levelRef, pointsRef;
        static AccessTools.FieldRef<PathExperience, HashSet<uint>>? practisedRef;
        static AccessTools.FieldRef<PlayerProgressionManager, bool>? canBuyRef;

        static string SnapshotFolder => Path.Combine(Cfg.Folder, "range-snapshots");

        static void InitReset(MelonPreferences_Category cat, string req) {
            resetPlayer = cat.CreateEntry("ResetPlayer", "", description: "Server only. The player Reset, Snapshot and Restore act on (captured at the press). Empty = [TestBuild] Player.");
            restoreFile = cat.CreateEntry("RestoreFile", "", description: "Server only. The snapshot file RestoreRequest puts back: its file name only, from UserData\\QuickshotRing\\range-snapshots\\archery-<account> (captured at the press). Empty = the newest one that is not a before-restore copy.");
            Func<string> named = () => { string n = (resetPlayer.Value ?? "").Trim(); return n.Length > 0 ? n : TestBuild.Configured; };
            Add(cat, "ResetRequest", "reset archery", req + "DESTROYS SAVED PROGRESS: sells every archery orb of ResetPlayer (the game's own too), sets their archery level, experience and points to 0, forgets their main path and practised orbs, and writes the save. A new snapshot is written first (refused if it cannot be, or if there is nothing to reset); RestoreRequest puts it back.", Reset, named);
            Add(cat, "SnapshotRequest", "snapshot", req + "Writes ResetPlayer's archery orbs, level, experience, points, practised orbs and main path to a new file in UserData\\QuickshotRing\\range-snapshots (never overwritten; the newest 10 per player kept).", Snapshot, named);
            Add(cat, "RestoreRequest", "restore", req + "Puts ResetPlayer's archery progress back as a snapshot has it (RestoreFile, or the newest that is not a before-restore copy; saved). The state it replaces is written as a new snapshot first.", Restore, named, () => restoreFile.Value ?? "");
        }

        static bool ResetFields() {
            try {
                xpRef ??= AccessTools.FieldRefAccess<PathExperience, uint>("currentExperience");
                levelRef ??= AccessTools.FieldRefAccess<PathExperience, uint>("currentLevel");
                pointsRef ??= AccessTools.FieldRefAccess<PathExperience, uint>("freePoints");
                practisedRef ??= AccessTools.FieldRefAccess<PathExperience, HashSet<uint>>("practicedSlots");
                canBuyRef ??= AccessTools.FieldRefAccess<PlayerProgressionManager, bool>("canBuyMoreSkills");
                return true;
            }
            catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: cannot reach the game's archery progress: {e.Message}"); return false; }
        }

        sealed class Archer {
            public Player P = null!;
            public PlayerProgressionManager Pm = null!;
            public PathExperience Exp = null!;
            public ProfessionSkillTree Tree = null!;
            public string Name = "", Key = "";
            public Dictionary<uint, uint[]> Deps = new Dictionary<uint, uint[]>();
        }

        /// <summary>The named player's Ranged progress, or null after refusing with the reason.</summary>
        static Archer? ArcherOf(Captured c) {
            if (c.Name.Length == 0) { Refuse(c, "no player named ([TestRange] ResetPlayer and [TestBuild] Player are empty)"); return null; }
            Archer? a = ArcherFor(c.Name, out string? why);
            if (a == null) Refuse(c, why ?? $"'{c.Name}': not online");
            return a;
        }

        /// <summary>The player's Ranged progress (online and fully loaded), or null with why not.</summary>
        static Archer? ArcherFor(string name, out string? why) {
            why = TestTools.ReadyFor(name, out Player? p, out PathExperience? exp);
            if (why != null || p == null || exp == null) { why = $"'{name}': {why ?? "not online"}"; return null; }
            if (!ResetFields()) { why = "the game's archery progress cannot be reached (see the log)"; return null; }
            ProfessionSkillTree tree = ProfessionSkillTree.GetProfessionTree(exp.Path);
            if (tree == null) { why = "the archery skill tree is not loaded"; return null; }
            var a = new Archer { P = p, Pm = p.ProgressionManager, Exp = exp, Tree = tree, Name = Builds.NameOf(p) };
            a.Key = Builds.KeyOf(p, a.Name);
            foreach (ProgressionSlot s in tree.Slots) if (s != null) a.Deps[s.Hash] = (s.Dependencies ?? new List<uint>()).ToArray();
            return a;
        }

        /// <summary>The skill points these slots cost in the archery tree (a slot not in it counts 1, as Tree.OffOrbRefund pays).</summary>
        static uint CostOf(Archer a, IEnumerable<uint> slots) {
            uint cost = 0u;
            foreach (uint h in slots) cost += a.Tree.GetSlot(h, out ProgressionSlot s) && s != null ? s.Cost : 1u;
            return cost;
        }

        static ArcherySnapshot Take(Archer a) {
            var s = new ArcherySnapshot {
                Name = a.Name, Key = a.Key, Taken = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Level = levelRef!(a.Exp), Experience = xpRef!(a.Exp), Points = pointsRef!(a.Exp),
                MainPath = Builds.ServerBuild(a.P).Main.ToString(),
            };
            s.Owned.AddRange(ResetRule.BuyOrder(a.Exp.UnlockedSlots, a.Deps));
            s.OwnedCost = CostOf(a, s.Owned);
            s.HasOwnedCost = true;
            HashSet<uint>? practised = practisedRef!(a.Exp);
            if (practised != null) s.Practised.AddRange(practised);
            return s;
        }

        static bool Nothing(ArcherySnapshot s) => ResetRule.NothingToReset(s.Owned.Count, s.Experience, s.Practised.Count);

        static string FolderOf(string key) => Path.Combine(SnapshotFolder, SnapshotFiles.FolderFor(key));

        /// <summary>This account's snapshot files, newest first (file names only).</summary>
        static List<string> FilesOf(string key) {
            string folder = FolderOf(key);
            if (!Directory.Exists(folder)) return new List<string>();
            return SnapshotFiles.NewestFirst(Directory.GetFiles(folder, "*.txt").Select(Path.GetFileName));
        }

        /// <summary>Writes the snapshot as a NEW file in its account's folder (never over another), then keeps only the newest
        /// SnapshotFiles.Keep there. The file's path, or null after logging why it could not be written.</summary>
        static string? WriteNew(ArcherySnapshot s, string kind) {
            string folder = FolderOf(s.Key);
            string file = "";
            try {
                Directory.CreateDirectory(folder);
                var existing = Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
                string name = SnapshotFiles.NewName(DateTime.Now, kind, existing);
                file = Path.Combine(folder, name);
                using (var w = new FileStream(file, FileMode.CreateNew, FileAccess.Write)) // never over an existing file
                using (var sw = new StreamWriter(w)) sw.Write(s.Format());
                existing.Add(name);
                foreach (string old in SnapshotFiles.ToPrune(existing, SnapshotFiles.Keep)) {
                    if (string.Equals(old, name, StringComparison.OrdinalIgnoreCase)) continue; // never the one just written (a clock set back)
                    try { File.Delete(Path.Combine(folder, old)); }
                    catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: could not delete the old snapshot {old}: {e.Message}"); }
                }
                return file;
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: cannot write the snapshot {(file.Length > 0 ? file : folder)}: {e.Message}"); return null; }
        }

        static string Slots(Archer a, IEnumerable<uint> hashes) {
            var names = new List<string>();
            foreach (uint h in hashes) names.Add(Tree.OrbBySlot.TryGetValue(h, out Orb o) ? o.ToString() : (a.Tree.GetSlot(h, out ProgressionSlot s) && s != null && s.Skill != null ? s.Skill.name : h.ToString()));
            return names.Count > 0 ? string.Join("+", names.ToArray()) : "nothing";
        }

        /// <summary>Sells every Ranged orb, leaves first, no refund (the level kept high during the sales so the unsigned level never
        /// wraps). The orbs sold and the failures.</summary>
        static List<uint> SellAll(Archer a, List<string> failed) {
            var sold = new List<uint>();
            uint level = levelRef!(a.Exp);
            try {
                foreach (uint h in ResetRule.Plan(a.Exp.UnlockedSlots.ToList(), a.Deps).Sell) {
                    try {
                        if (!a.Exp.UnlockedSlots.Contains(h)) { sold.Add(h); continue; } // the game removed it with its parent
                        if (!a.Tree.GetSlot(h, out ProgressionSlot slot) || slot == null) { failed.Add($"slot {h} (not in the archery tree)"); continue; }
                        levelRef!(a.Exp) = level + 1000u;
                        a.Exp.LockSlot(slot, false);
                        if (a.Exp.UnlockedSlots.Contains(h)) failed.Add($"{Slots(a, new[] { h })} (still owned after the sale)");
                        else sold.Add(h);
                    }
                    catch (Exception e) { failed.Add($"{Slots(a, new[] { h })} (the sale threw: {e.Message})"); }
                }
            }
            finally { levelRef!(a.Exp) = level; }
            return sold;
        }

        /// <summary>Inside the batch's finally: the shop's slot flag, the recount, an open shrine told. Never throws.</summary>
        static void Recount(Archer a, List<string> failed) {
            try { Traverse.Create(a.Exp).Field("UpdatedPoints").GetValue<Action<ProgressionPath>>()?.Invoke(a.Exp.Path); } // an open shrine refreshes
            catch (Exception e) { failed.Add($"telling the shrine ({e.Message})"); }
            try {
                a.Pm.UpdateCounts();
                int cap = (int)GlobalSettings<ProgressionSettings>.Instance.MaxBoughtSkills;
                canBuyRef!(a.Pm) = cap < 1 || cap > a.Pm.GetSlots().Count();
            }
            catch (Exception e) { failed.Add($"recounting skills ({e.Message})"); }
        }

        /// <summary>After the batch: de-duplicated inputs and the save.</summary>
        static string Save(Archer a, List<string> failed) {
            int dupes = TestTools.OneSubscriptionEachFor(a.Pm);
            try { a.P.SaveControllerAndState(); a.P.WriteSave(false); return dupes > 0 ? $"; {dupes} doubled input subscription(s) removed" : ""; }
            catch (Exception e) { failed.Add($"writing the save now ({e.Message}; the game saves it later)"); return ""; }
        }

        static readonly int T_Reset = Timing.Section("TestRange.Reset");
        static void Reset(Captured c) {
            using var _t = Timing.Begin(T_Reset, c.Name);
            Archer? a = ArcherOf(c);
            if (a == null) return;
            ArcherySnapshot before = Take(a);
            if (Nothing(before)) { Refuse(c, $"'{a.Name}' has nothing to reset: no archery orb, experience or practised orb ({before.Points} unspent point(s) left as they are); no snapshot written, so the last one is still the one Restore puts back"); return; }
            string? file = WriteNew(before, SnapshotFiles.Reset);
            if (file == null) { Refuse(c, $"'{a.Name}': the snapshot could not be written, so nothing was reset"); return; }
            var failed = new List<string>();
            var sold = new List<uint>();
            Builds.BeginBatch(a.Pm);
            try {
                try { a.Pm.EndPractice(); } catch (Exception e) { failed.Add($"ending the try-out ({e.Message})"); }
                sold = SellAll(a, failed);
            }
            finally {
                try {
                    levelRef!(a.Exp) = 0u; xpRef!(a.Exp) = 0u; pointsRef!(a.Exp) = 0u;
                    practisedRef!(a.Exp)?.Clear();
                }
                catch (Exception e) { failed.Add($"zeroing the level ({e.Message})"); }
                try { Paths.SetRemembered(a.Key, MainPath.None); } catch (Exception e) { failed.Add($"forgetting the main path ({e.Message})"); }
                Recount(a, failed);
                Builds.EndBatch(a.Pm); // range minor 7: always, in the finally
            }
            string extra = Save(a, failed);
            MelonLogger.Warning($"[Archery] TEST RANGE: '{a.Name}' ARCHERY RESET: sold {Slots(a, sold)}; level {before.Level} -> 0, experience {before.Experience} -> 0, unspent points {before.Points} -> 0; main path {before.MainPath} and {before.Practised.Count} practised orb(s) forgotten{extra}{(failed.Count > 0 ? $"; PROBLEMS: {string.Join(", ", failed.ToArray())}" : "")}; the snapshot to restore: {file}");
            Status(c, true, $"'{a.Name}' reset: {sold.Count} archery orb(s) sold, level {before.Level} -> 0{(failed.Count > 0 ? $"; problems: {string.Join(", ", failed.ToArray())}" : "")}. Snapshot {Path.GetFileName(file)}: Restore snapshot puts it back.{(PointsOn ? " (Everyone gets points is on: the points come back within 2 s.)" : "")}", failed.Count > 0);
        }

        static void Snapshot(Captured c) {
            Archer? a = ArcherOf(c);
            if (a == null) return;
            ArcherySnapshot s = Take(a);
            if (Nothing(s)) { Refuse(c, $"'{a.Name}' has nothing to snapshot: no archery orb, experience or practised orb (a snapshot of that would hide the earlier ones from Restore)"); return; }
            string? file = WriteNew(s, SnapshotFiles.Snapshot);
            if (file == null) { Refuse(c, $"'{a.Name}': the snapshot could not be written (see the log)"); return; }
            Status(c, true, $"'{a.Name}' snapshot {Path.GetFileName(file)}: {Slots(a, s.Owned)}, level {s.Level}, {s.Points} point(s), main path {s.MainPath}");
        }

        static readonly int T_Restore = Timing.Section("TestRange.Restore");
        static void Restore(Captured c) {
            using var _t = Timing.Begin(T_Restore, c.Name);
            Archer? a = ArcherOf(c);
            if (a == null) return;
            List<string> files;
            try { files = FilesOf(a.Key); }
            catch (Exception e) { Refuse(c, $"'{a.Name}': the snapshot folder cannot be read ({e.Message})"); return; }
            string? name = SnapshotFiles.Pick(files, c.Text, out string? why);
            if (name == null) { Refuse(c, $"'{a.Name}': {why}"); return; }
            string file = Path.Combine(FolderOf(a.Key), name);
            ArcherySnapshot? snap;
            try { snap = ArcherySnapshot.Parse(File.ReadAllText(file), out why); }
            catch (Exception e) { snap = null; why = e.Message; }
            if (snap == null) { Refuse(c, $"'{a.Name}': {name}: {why ?? "the snapshot could not be read"}"); return; }
            if (!string.Equals(snap.Key, a.Key, StringComparison.Ordinal)) { Refuse(c, $"'{a.Name}': {name} belongs to another account ('{snap.Name}'); nothing was restored"); return; }
            ArcherySnapshot replaced = Take(a);
            if (replaced.SameProgress(snap)) { Refuse(c, $"'{a.Name}' already has what {name} holds ({Slots(a, snap.Owned)}, level {snap.Level}); nothing restored. Name an older file in 'Snapshot file' to go further back."); return; }
            string? keptAs = WriteNew(replaced, SnapshotFiles.BeforeRestore);
            if (keptAs == null) { Refuse(c, $"'{a.Name}': the current state could not be kept first, so nothing was restored"); return; }

            // range minor 6: the game's skill cap counts every profession's skills; what does not fit (or the game refuses) is refunded
            int cap = 0, others = 0;
            try {
                cap = (int)GlobalSettings<ProgressionSettings>.Instance.MaxBoughtSkills;
                foreach (KeyValuePair<ProgressionPath, PathExperience> pe in a.Pm.PathExperiences) {
                    if (pe.Key == a.Exp.Path || pe.Value == null) continue;
                    ProfessionSkillTree t = ProfessionSkillTree.GetProfessionTree(pe.Key);
                    if (t != null) foreach (uint h in pe.Value.UnlockedSlots) if (t.GetSlot(h, out ProgressionSlot _)) others++;
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: could not count '{a.Name}''s other skills ({e.Message}); restoring without the cap"); cap = 0; }
            int room = cap < 1 ? int.MaxValue : Math.Max(0, cap - others);
            List<uint> within = ResetRule.WithinRoom(ResetRule.BuyOrder(snap.Owned, a.Deps), room, out List<uint> over);
            uint snapCost = snap.HasOwnedCost ? snap.OwnedCost : CostOf(a, snap.Owned);

            var failed = new List<string>();
            var bought = new List<uint>();
            uint boughtCost = 0u, points = snap.Points, level = snap.Level, refund = 0u;
            Builds.BeginBatch(a.Pm);
            try {
                try { a.Pm.EndPractice(); } catch (Exception e) { failed.Add($"ending the try-out ({e.Message})"); }
                SellAll(a, failed);
                foreach (uint h in within) {
                    try {
                        if (!a.Tree.GetSlot(h, out ProgressionSlot slot) || slot == null) { failed.Add($"slot {h} (not in the archery tree now)"); continue; }
                        a.Pm.MarkSlotPracticed(slot.Path, slot.Hash);
                        if (pointsRef!(a.Exp) < slot.Cost) pointsRef!(a.Exp) = slot.Cost;
                        a.Pm.UnlockSlot(slot);
                        if (a.Exp.UnlockedSlots.Contains(h)) { bought.Add(h); boughtCost += slot.Cost; }
                        else failed.Add($"{Slots(a, new[] { h })} (refused by the game or the one-ultimate rule)");
                    }
                    catch (Exception e) { failed.Add($"{Slots(a, new[] { h })} (the purchase threw: {e.Message})"); }
                }
            }
            finally {
                try {
                    ResetRule.RestoreNumbers(snap.Level, snap.Points, snapCost, boughtCost, out points, out level, out refund);
                    levelRef!(a.Exp) = level; xpRef!(a.Exp) = snap.Experience; pointsRef!(a.Exp) = points;
                    HashSet<uint>? practised = practisedRef!(a.Exp);
                    if (practised != null) { practised.Clear(); foreach (uint h in snap.Practised) practised.Add(h); }
                }
                catch (Exception e) { failed.Add($"putting the level back ({e.Message})"); }
                try {
                    MainPath main = Enum.TryParse(snap.MainPath, true, out MainPath m) ? m : MainPath.None;
                    Paths.SetRemembered(a.Key, main);
                }
                catch (Exception e) { failed.Add($"putting the main path back ({e.Message})"); }
                Recount(a, failed);
                Builds.EndBatch(a.Pm); // range minor 7: always, in the finally
            }
            string extra = Save(a, failed);
            if (over.Count > 0) failed.Add($"{Slots(a, over)} (past the game's skill cap of {cap} with {others} skill(s) in other professions)");
            string refunded = refund > 0u ? $"; {refund} point(s) refunded for the orb(s) not bought back" : "";
            MelonLogger.Warning($"[Archery] TEST RANGE: '{a.Name}' ARCHERY RESTORED from {file} (taken {snap.Taken}): owns {Slots(a, bought)}; level {replaced.Level} -> {level}, experience {replaced.Experience} -> {snap.Experience}, unspent points {replaced.Points} -> {points}{refunded}, main path {snap.MainPath}{extra}{(failed.Count > 0 ? $"; PROBLEMS: {string.Join(", ", failed.ToArray())}" : "")}; what it replaced: {keptAs}");
            Status(c, true, $"'{a.Name}' restored from {name}: {bought.Count} of {snap.Owned.Count} archery orb(s), level {level}{refunded}{(failed.Count > 0 ? $"; problems: {string.Join(", ", failed.ToArray())}" : "")}. What it replaced: {Path.GetFileName(keptAs)}.", failed.Count > 0);
        }
    }
}
