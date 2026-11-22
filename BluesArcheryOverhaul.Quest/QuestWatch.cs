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

        static bool Armed => Time.unscaledTime < until;

        internal static void Arm(string why) {
            counts.Clear();
            until = Time.unscaledTime + Seconds;
            MelonLogger.Msg($"[Archery] Quest watch: {why}; logging the mod's routines for {Seconds:0} s");
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

        internal static void Exit(int id) {
            if (!Armed) return;
            if (counts.TryGetValue(id, out int n) && n <= PerSection) MelonLogger.Msg($"[Archery] Quest watch: end {Timing.NameOf(id)}");
        }

        [HarmonyPatch(typeof(SkillShrineTestingArea), "Open", new System.Type[] { typeof(SkillOrb) })]
        static class TryOutOpening {
            static void Prefix(SkillOrb orb) {
                Arm($"a try-out is opening ({(orb != null ? orb.name : "no orb")})");
            }
            static void Postfix() { MelonLogger.Msg("[Archery] Quest watch: the game's Open(orb) returned"); }
        }

        [HarmonyPatch(typeof(SkillShrineTestingArea), "Open", new System.Type[0])]
        static class TryOutOpeningAgain {
            static void Prefix() { if (!Armed) Arm("a try-out is opening again"); else MelonLogger.Msg("[Archery] Quest watch: Open() starts"); }
            static void Postfix() { MelonLogger.Msg("[Archery] Quest watch: the game's Open() returned"); }
        }
    }
}
