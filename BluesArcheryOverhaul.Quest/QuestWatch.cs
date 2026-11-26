using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Quest only, while the port is being checked: a hard crash leaves no exception in the log. For a few seconds after a shrine
    // try-out opens, every timed section of the mod (Timing.Begin: every patch, tick and look) logs when it starts and ends, the
    // first few times each, so the last "start" with no "end" names the routine that was running.
    internal static class QuestWatch {
        const float Seconds = 3f;
        const int PerSection = 3;
        static float until = -1f;
        static readonly Dictionary<int, int> counts = new Dictionary<int, int>();

        // Off unless the file UserData/BluesArcheryOverhaul/quest-watch.txt exists at start: the trace wrote about 15,000 log lines in
        // a ten-minute session (every grab and shot), which the owner felt as lag. To trace a crash, create that (empty) file.
        internal static readonly bool On = System.IO.File.Exists(System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "BluesArcheryOverhaul", "quest-watch.txt"));
        static bool Armed => On && Time.unscaledTime < until;

        internal static void Arm(string why, float seconds = Seconds) {
            if (!On) return;
            counts.Clear();
            until = Time.unscaledTime + seconds;
            MelonLogger.Msg($"[Archery] Quest watch: {why}; logging the mod's routines for {seconds:0} s");
        }

        internal static void Enter(int id) {
            if (!Armed) return;
            counts.TryGetValue(id, out int n);
            counts[id] = n + 1;
            if (n < PerSection) MelonLogger.Msg($"[Archery] Quest watch: start {Timing.NameOf(id)}");
        }

        /// <summary>A step inside a routine, logged while armed (the first PerSection times each).</summary>
        internal static void Mark(string step) {
            if (!Armed) return;
            int id = step.GetHashCode() | int.MinValue;   // apart from the section ids, which are small and positive
            counts.TryGetValue(id, out int n);
            counts[id] = n + 1;
            if (n < 20) MelonLogger.Msg($"[Archery] Quest watch: at {step}");
        }

        static readonly HashSet<string> said = new HashSet<string>();
        /// <summary>One line the first time a routine is reached, armed or not: proof on the headset that a patch really runs.</summary>
        internal static void Once(string what) { if (On && said.Add(what)) MelonLogger.Msg($"[Archery] Quest: reached: {what}"); }

        internal static void Exit(int id) {
            if (!Armed) return;
            if (counts.TryGetValue(id, out int n) && n <= PerSection) MelonLogger.Msg($"[Archery] Quest watch: end {Timing.NameOf(id)}");
        }

        [HarmonyPatch(typeof(SkillShrineTestingArea), "Open", new System.Type[] { typeof(SkillOrb) })]
        static class TryOutOpening {
            static void Prefix(SkillOrb orb) {
                Arm($"a try-out is opening ({(orb != null ? orb.name : "no orb")})");
                if (!On || orb == null) return;
                // what the game's Open() reads next, one read at a time (a crash after one of these names the next read)
                MelonLogger.Msg("[Archery] Quest watch: try-out reads: slot " + (orb.Slot != null ? orb.Slot.Name : "null"));
                ProfessionSkill sk = orb.Skill;
                MelonLogger.Msg("[Archery] Quest watch: try-out reads: skill " + (sk == null ? "null" : sk.name + " (" + sk.GetIl2CppType().FullName + ")"));
                if (sk == null) return;
                var st = sk.StaticContentPrefabs; MelonLogger.Msg("[Archery] Quest watch: try-out reads: static content " + (st == null ? "null" : st.Count.ToString()));
                var dy = sk.DynamicContentPrefabs; MelonLogger.Msg("[Archery] Quest watch: try-out reads: dynamic content " + (dy == null ? "null" : dy.Count.ToString()));
                var gl = sk.PracticeGlyphSprites; MelonLogger.Msg("[Archery] Quest watch: try-out reads: glyphs " + (gl == null ? "null" : gl.Length.ToString()));
                var icon = orb.Slot != null ? orb.Slot.SlotIcon : null; MelonLogger.Msg("[Archery] Quest watch: try-out reads: icon " + (icon == null ? "null" : icon.name));
                
                MelonLogger.Msg("[Archery] Quest watch: try-out reads: all read");
            }
            static void Postfix() { if (On) MelonLogger.Msg("[Archery] Quest watch: the game's Open(orb) returned"); }
        }

        // not patched since 2026-11-25: arming on every shot and grab logged hundreds of lines a shot (felt as an FPS drop)
        // [HarmonyPatch] (shot, grab)
        static class Shooting {
            [HarmonyPriority(1000)]
            static void Prefix() { Arm("a shot", 8f); }
        }

        // not patched since 2026-11-25: arming on every shot and grab logged hundreds of lines a shot (felt as an FPS drop)
        // [HarmonyPatch] (shot, grab)
        static class Grabbed {
            static void Prefix() { Arm("a bow string was grabbed", 20f); }
        }

        [HarmonyPatch(typeof(SkillShrineTestingArea), "Open", new System.Type[0])]
        static class TryOutOpeningAgain {
            static void Prefix() { if (!On) return; if (!Armed) Arm("a try-out is opening again"); else MelonLogger.Msg("[Archery] Quest watch: Open() starts"); }
            static void Postfix() { if (On) MelonLogger.Msg("[Archery] Quest watch: the game's Open() returned"); }
        }
    }
}
