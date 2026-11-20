using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Chunks;
using Alta.Networking.Scripts.Player;
using Alta.StatSystem;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // The range's small helpers (TestRange.cs has the overview): Heal me, Noon, Teleport to the archery shrine, Collect loose
    // arrows and the loose-arrow cap. Heal and Teleport act on [TestBuild] Player (captured at the press), online and fully loaded.
    internal static partial class TestRange {
        static MelonPreferences_Entry<int> maxLoose = null!;
        static MelonPreferences_Entry<float> noonHour = null!;
        static MelonPreferences_Entry<string> shrineSpot = null!;

        static void InitHelpers(MelonPreferences_Category cat, string req) {
            maxLoose = cat.CreateEntry("MaxLooseArrows", 200, description: "Server only, live. Range arrows lying loose (on the ground or stuck in things, never one in a hand, a quiver, a bag or on a string) beyond this many are removed, the oldest first, every RefillSeconds. -1 = no cap.");
            noonHour = cat.CreateEntry("NoonHour", 12f, description: "Server only. The hour (24 h clock) NoonRequest sets for everyone.");
            shrineSpot = cat.CreateEntry("ShrineSpot", "-835.6, 214.3, 386.9", description: "Server only. Where TeleportRequest puts [TestBuild] Player: 'x, y, z' (the archery shrine at Archers Refuge).");
            Add(cat, "HealRequest", "heal", req + "Puts [TestBuild] Player's health back to full (not food).", Heal, () => TestBuild.Configured);
            Add(cat, "FeedRequest", "fill hunger", req + "Fills every online player's hunger (the game's 'Hunger' stat) to full.", Feed);
            Add(cat, "NoonRequest", "noon", req + "Sets the time of day to NoonHour for everyone.", Noon);
            Add(cat, "TeleportRequest", "teleport", req + "Moves [TestBuild] Player to ShrineSpot (the game's own server move: the client loads the spot first).", Teleport, () => TestBuild.Configured);
            Add(cat, "CollectRequest", "collect", req + "Removes every range arrow lying loose (on the ground or stuck in things); arrows in a hand, quiver, bag or on a string stay.", Collect);
            InitClean(cat, req);
        }

        static bool IsLoose(Loose l) {
            if (Gone(l)) return false;
            Pickup p = l.Entity.CommonPickup;
            return p != null && !p.IsDocked && !p.IsInteractedWith;
        }

        static readonly List<bool> looseFlags = new List<bool>();
        static readonly List<float> looseSince = new List<float>();

        static readonly int T_Loose = Timing.Section("TestRange.LooseTick (loose-arrow cap)");
        /// <summary>From TickShelf (every RefillSeconds): the loose range arrows beyond MaxLooseArrows go, the oldest first.</summary>
        static void LooseTick() {
            int max = maxLoose.Value;
            if (max < 0 || loose.Count <= max) return; // fewer tracked than the cap: nothing can be over it
            using var _t = Timing.Begin(T_Loose);
            looseFlags.Clear(); looseSince.Clear();
            foreach (Loose l in loose) { looseFlags.Add(IsLoose(l)); looseSince.Add(l.Since); }
            List<int> evict = LooseCap.Evict(looseFlags, looseSince, max);
            if (evict.Count == 0) return;
            foreach (int i in evict) Destroy(loose[i].Entity);
            loose.RemoveAll(GoneTest);
            if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: removed {evict.Count} loose range arrow(s) past MaxLooseArrows {max}");
        }

        static void Collect(Captured c) {
            int n = 0, kept = 0;
            foreach (Loose l in loose) {
                if (Gone(l)) continue;
                if (!IsLoose(l)) { kept++; continue; }
                Destroy(l.Entity);
                n++;
            }
            loose.RemoveAll(GoneTest);
            Status(c, true, $"{n} loose range arrow(s) removed; {kept} in hands, quivers or on strings left alone");
        }

        static void Heal(Captured c) {
            if (c.Name.Length == 0) { Refuse(c, "[TestBuild] Player is empty"); return; }
            string? why = TestTools.ReadyFor(c.Name, out Player? p, out _);
            if (why != null || p == null) { Refuse(c, $"'{c.Name}': {why ?? "not online"}"); return; }
            string? not = PlayerCare.Heal(p, out float before, out float after, out float max);
            if (not != null) { Refuse(c, $"'{c.Name}' {not}"); return; }
            Status(c, true, $"'{c.Name}' healed: {before:0.#} -> {after:0.#} of {max:0.#} (food is not touched)");
        }

        /// <summary>Fill hunger (the owner, 2026-11-18): every online player's 'Hunger' stat to its maximum (PlayerCare.Feed).</summary>
        static void Feed(Captured c) {
            int fed = 0;
            var skipped = new List<string>();
            foreach (IPlayer ip in Player.AllPlayers.ToList()) {
                if (!(ip is Player p)) continue;
                string name = Builds.NameOf(p);
                try {
                    string? not = PlayerCare.Feed(p, out float before, out float after, out float max);
                    if (not != null) { skipped.Add(name + " (" + not + ")"); continue; }
                    fed++;
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: '{name}' hunger {before:0.##} -> {after:0.##} of {max:0.##}");
                }
                catch (Exception e) { skipped.Add(name + " (" + e.Message + ")"); }
            }
            if (fed == 0) { Refuse(c, skipped.Count > 0 ? "nobody fed: " + string.Join(", ", skipped.ToArray()) : "nobody is online"); return; }
            Status(c, true, $"hunger filled for {fed} player(s)" + (skipped.Count > 0 ? "; skipped " + string.Join(", ", skipped.ToArray()) : ""));
        }

        static void Noon(Captured c) {
            GameTimeManager g = GameTimeManager.Instance;
            if (g == null) { Refuse(c, "the game's clock is not running"); return; }
            float hour = noonHour.Value;
            g.RushToTime(NoonRule.DayFraction(hour));
            Status(c, true, $"time of day set to {hour:0.#}:00 for everyone");
        }

        static void Teleport(Captured c) {
            if (c.Name.Length == 0) { Refuse(c, "[TestBuild] Player is empty"); return; }
            if (!NoonRule.Spot(shrineSpot.Value, out float x, out float y, out float z)) { Refuse(c, $"[TestRange] ShrineSpot '{shrineSpot.Value}' is not 'x, y, z'"); return; }
            string? why = TestTools.ReadyFor(c.Name, out Player? p, out _);
            if (why != null || p == null) { Refuse(c, $"'{c.Name}': {why ?? "not online"}"); return; }
            var to = new Vector3(x, y, z);
            LocationChunk chunk = LocationChunkHelper.GetBestContaining(to);
            if (chunk == null) { Refuse(c, $"no chunk at {Targets.P(to)}"); return; }
            string name = c.Name;
            // H4 (range minor 15): Player.SafeMoveToChunk is async void, so a failure inside it never comes back here: the arrival says
            // so in the window, and no arrival within TeleportWaitSeconds says that instead
            var t = new Teleporting { Of = c, Name = name, To = to, Deadline = Time.time + TeleportWaitSeconds };
            teleport = t;
            work = true;
            p.SafeMoveToChunk(chunk, to, () => {
                try {
                    t.Arrived = true;
                    if (ReferenceEquals(teleport, t)) teleport = null;
                    Status(t.Of, true, $"'{name}' arrived at the archery shrine {Targets.P(to)}");
                }
                catch { }
            });
            if (!t.Arrived) Status(c, true, $"'{name}' is being moved to the archery shrine {Targets.P(to)} (the game waits for their client to load it)");
        }

        const float TeleportWaitSeconds = 30f;
        sealed class Teleporting { public Captured Of = null!; public string Name = ""; public Vector3 To; public float Deadline; public bool Arrived; }
        static Teleporting? teleport;

        static readonly int T_Teleport = Timing.Section("TestRange.TickTeleport (arrival check)");
        static void TickTeleport(float now) {
            Teleporting? t = teleport;
            if (t == null) return;
            using var _t = Timing.Begin(T_Teleport);
            if (t.Arrived) { teleport = null; return; }
            if (now < t.Deadline) return;
            teleport = null;
            Status(t.Of, true, $"'{t.Name}' has not arrived at {Targets.P(t.To)} after {TeleportWaitSeconds:0} s (their client may still be loading; if they never arrive, the server's own log says why)", true);
        }
    }
}
