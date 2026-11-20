using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Global;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // The range's archery-progress tools (TestRange.cs has the overview). Server only; each acts only on players who are online and
    // fully loaded right now, gated exactly as a buy request (TestTools.ReadyFor), and every player touched gets one loud log line
    // ("[Archery] TEST RANGE: '<name>' ...") with what changed. These change players' saves.
    internal static partial class TestRange {
        static MelonPreferences_Entry<bool> everyonePoints = null!;
        static MelonPreferences_Entry<int> everyonePointsTo = null!;
        static MelonPreferences_Entry<string> buyEveryone = null!;
        static AccessTools.FieldRef<PathExperience, uint>? freePointsField, levelField;
        static bool lastPoints;
        static float nextPoints;
        static readonly HashSet<string> pointsAnnounced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static bool PointsOn => enabled != null && enabled.Value && everyonePoints.Value;

        static void InitArchery(MelonPreferences_Category cat) {
            everyonePoints = cat.CreateEntry("EveryonePoints", false, description: "Server only, live, needs Enabled. Every online, fully loaded player keeps EveryonePointsTo unspent archery (Ranged) skill points, topped up every 2 seconds (after they spend, join, or this is switched on). Never at or above the game's banked-points cap (at the cap the game pays no experience); a higher balance is never lowered. Off: no more top-ups; points already given stay. This changes players' saves.");
            everyonePointsTo = cat.CreateEntry("EveryonePointsTo", 2, description: "Server only. How many unspent archery points EveryonePoints keeps (one below the game's cap of 3 at most).");
            buyEveryone = cat.CreateEntry("BuyEveryone", "", description: "Server only. The orb list (as [TestBuild] Buy) BuyEveryoneRequest really buys for every online, fully loaded player in turn (each exactly as one player's buy: every other archery orb sold, these bought, the save written). Players not ready are skipped and named. It must name an orb to buy: an empty list (sell everything) is refused for everyone.");
            string req = "Server only. The button window writes a new number here on every press; the server acts once per new number while it runs (the number it started with is never acted on). ";
            Add(cat, "BuyEveryoneRequest", "buy for everyone", req + "Buys BuyEveryone for every online, fully loaded player (saved). A snapshot of each is written first (range-snapshots); a player whose snapshot cannot be written is skipped. RestoreRequest undoes it per player.", BuyForEveryone, null, () => buyEveryone.Value ?? "");
            InitReset(cat, req);
            InitHelpers(cat, req);
        }

        static string PointsState() => PointsOn ? $"; everyone keeps {TestBuildRule.TopUpTo(everyonePointsTo.Value, Cap())} archery point(s)" : "";

        static void RememberPoints() => lastPoints = PointsOn;

        static void PointsChanged(List<string> said) {
            if (PointsOn == lastPoints) return;
            said.Add(PointsOn ? $"everyone keeps {TestBuildRule.TopUpTo(everyonePointsTo.Value, Cap())} unspent archery point(s) (saved)" : "points top-up off (points already given stay)");
            nextPoints = 0f;
        }

        static uint Cap() {
            try { return GlobalSettings<ProgressionSettings>.Instance.MaxFreePoints; }
            catch { return 3u; }
        }

        static readonly int T_Points = Timing.Section("TestRange.TickArchery (everyone's points)");
        static void TickArchery(float now) {
            if (!PointsOn || now < nextPoints) return;
            using var _t = Timing.Begin(T_Points);
            nextPoints = now + 2f;
            uint cap = Cap();
            foreach (IPlayer ip in Player.AllPlayers.ToList()) {
                if (!(ip is Player pl)) continue;
                string name = Builds.NameOf(pl);
                if (TestTools.ReadyFor(name, out Player? p, out PathExperience? exp) != null || p == null || exp == null) continue;
                try {
                    freePointsField ??= AccessTools.FieldRefAccess<PathExperience, uint>("freePoints");
                    levelField ??= AccessTools.FieldRefAccess<PathExperience, uint>("currentLevel");
                    uint had = freePointsField(exp);
                    uint want = PointsRule.TopUp(had, everyonePointsTo.Value, cap);
                    if (want == had) continue;
                    // H4 (range I-1): the points come with the levels they stand for, so a later sale at the shrine (which lowers the
                    // unsigned level by the orb's cost) can never wrap it, as TestTools' buy does
                    uint level = levelField(exp), newLevel = PointsRule.Level(level, TestTools.OwnedCost(exp), want);
                    freePointsField(exp) = want;
                    if (newLevel != level) levelField(exp) = newLevel;
                    Traverse.Create(exp).Field("UpdatedPoints").GetValue<Action<ProgressionPath>>()?.Invoke(exp.Path); // an open shrine refreshes
                    string lv = newLevel != level ? $", Ranged level {level} -> {newLevel}" : "";
                    if (pointsAnnounced.Add(name)) MelonLogger.Warning($"[Archery] TEST RANGE: '{name}' unspent archery points {had} -> {want}{lv} (Everyone gets points; this changes their save)");
                    else if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: '{name}' unspent archery points {had} -> {want}{lv}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: could not top up '{name}''s points: {e.Message}"); }
            }
        }

        static readonly int T_BuyAll = Timing.Section("TestRange.BuyForEveryone");
        /// <summary>The list bought for every online, fully loaded player in turn (TestTools' own buy, gated per player). H4 (range C-1):
        /// never "sell everything" (EveryoneRule.BuyRefusal), and a snapshot of EVERY affected player is written before any of them is
        /// touched; a player whose snapshot cannot be written is skipped and named (EveryoneRule.WithSnapshots). Restore undoes it per player.</summary>
        static void BuyForEveryone(Captured c) {
            using var _t = Timing.Begin(T_BuyAll);
            string? refusal = EveryoneRule.BuyRefusal(c.Text);
            if (refusal != null) { Refuse(c, refusal); return; }
            var gates = new List<KeyValuePair<string, string?>>();
            foreach (IPlayer ip in Player.AllPlayers.ToList()) {
                if (!(ip is Player pl)) continue;
                string name = Builds.NameOf(pl);
                gates.Add(new KeyValuePair<string, string?>(name, TestTools.ReadyFor(name, out _, out _)));
            }
            EveryoneRule.Split(gates, out List<string> ready, out List<string> skipped);
            if (ready.Count == 0 && skipped.Count == 0) { Refuse(c, "nobody is online"); return; }
            var files = new List<string?>();
            foreach (string name in ready) {
                string? file = null;
                try {
                    Archer? a = ArcherFor(name, out string? why);
                    if (a != null) file = WriteNew(Take(a), SnapshotFiles.BuyForAll);
                    else MelonLogger.Warning($"[Archery] TEST RANGE: buy for everyone: no snapshot of '{name}': {why}");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: buy for everyone: the snapshot of '{name}' threw: {e.Message}"); }
                files.Add(file);
            }
            List<string> buy = EveryoneRule.WithSnapshots(ready, files, skipped);
            var done = new List<string>();
            foreach (string name in buy) {
                Player before = Player.GetPlayer(name);
                MelonLogger.Warning($"[Archery] TEST RANGE: '{name}' buy for everyone '{c.Text}': before {(before != null ? Builds.ServerBuild(before).ToString() : "?")}");
                string? why = TestTools.BuyNow(name, c.Text, c.Number);
                if (why != null) { skipped.Add($"{name} ({why})"); continue; }
                Player after = Player.GetPlayer(name);
                MelonLogger.Warning($"[Archery] TEST RANGE: '{name}' buy for everyone: after {(after != null ? Builds.ServerBuild(after).ToString() : "?")} (the TEST BUILD line above says what was bought and sold)");
                done.Add(name);
            }
            Status(c, true, $"bought '{c.Text}' for {(done.Count > 0 ? string.Join(", ", done.ToArray()) : "nobody")}{(skipped.Count > 0 ? $"; skipped {string.Join(", ", skipped.ToArray())}" : "")}. Each one's progress before it is in a snapshot: 'Restore snapshot (named)' with their name puts it back.", skipped.Count > 0);
        }
    }
}
