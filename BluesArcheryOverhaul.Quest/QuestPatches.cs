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
        /// <summary>The mod's own components (ghost arrows, trails, arcs, the volley's simulation ...) exist for the game only once
        /// they are registered with it; AddComponent of an unregistered one throws. Each has an IntPtr constructor (#if QUEST).</summary>
        internal static void RegisterComponents() {
            int done = 0; var failed = new List<string>();
            foreach (Type t in typeof(QuestPatches).Assembly.GetTypes()) {
                if (t.IsAbstract || !typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(t)) continue;
                try {
                    if (!Il2CppInterop.Runtime.Injection.ClassInjector.IsTypeRegisteredInIl2Cpp(t))
                        Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp(t);
                    done++;
                } catch (Exception e) { failed.Add($"{t.FullName}: {(e.InnerException ?? e).Message}"); }
            }
            MelonLogger.Msg($"[Archery] Quest: {done} of {done + failed.Count} components registered");
            foreach (string f in failed) MelonLogger.Error($"[Archery] Quest: component NOT registered (its look will not show): {f}");
        }

        internal static (int applied, int total) ApplyAll(HarmonyLib.Harmony harmony) {
            RegisterComponents();
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
