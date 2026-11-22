using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace BluesArcheryOverhaul {
    // Quest only. MelonLoader's automatic PatchAll is off for this mod ([assembly: HarmonyDontPatchAll] in QuestMain.cs): on IL2CPP a
    // method can be inlined or missing in the game's build, and one patch class that fails must not stop the rest. So every
    // [HarmonyPatch] class is applied on its own and each failure is named in the log.
    internal static class QuestPatches {
        internal static (int applied, int total) ApplyAll(HarmonyLib.Harmony harmony) {
            int applied = 0, total = 0;
            var failed = new List<string>();
            foreach (Type t in typeof(QuestPatches).Assembly.GetTypes()) {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                total++;
                try {
                    harmony.CreateClassProcessor(t).Patch();
                    applied++;
                } catch (Exception e) {
                    Exception inner = e.InnerException ?? e;
                    failed.Add($"{t.FullName}: {inner.GetType().Name}: {inner.Message}");
                }
            }
            MelonLogger.Msg($"[Archery] Quest: {applied} of {total} patches applied");
            foreach (string f in failed) MelonLogger.Error($"[Archery] Quest: patch NOT applied: {f}");
            return (applied, total);
        }
    }
}
