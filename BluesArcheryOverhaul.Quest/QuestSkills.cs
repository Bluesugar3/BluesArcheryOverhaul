using System;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Quest only. On PC a later orb's skill is a new PassiveOrbSkill (an empty subclass of the game's ProfessionSkill). On Quest a
    // managed subclass would have to be injected into IL2CPP, and ScriptableObject.CreateInstance of the game's own ProfessionSkill
    // gave null on the headset (2026-11-22: "adding AutoDock failed ... NullReferenceException in MakePassiveSkill"). So: the
    // plain CreateInstance first, then by type name, then a copy of a skill of the game's whose class is exactly ProfessionSkill
    // (no behaviour of its own); the log says which one worked.
    internal static class QuestSkills {
        static bool logged, passiveRegistered;

        /// <summary>At start (QuestMain): registers QuestPassiveSkill with the game.</summary>
        internal static void Register() {
            try {
                if (!Il2CppInterop.Runtime.Injection.ClassInjector.IsTypeRegisteredInIl2Cpp<QuestPassiveSkill>())
                    Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<QuestPassiveSkill>();
                passiveRegistered = true;
            } catch (Exception e) { MelonLogger.Error($"[Archery] Quest: could not register the later orbs' skill class: {e}"); }
        }

        // The game's collector never looks inside an object of a class the mod registered (Il2CppInterop builds the class without
        // the game's "holds references" flag or field map), so the lists and arrays a QuestPassiveSkill points to looked unused and
        // were freed: on the headset (2026-11-25) Auto Dock read 0/2/2 try-out entries, later 1757/0/0, Shatter 9, and opening a later
        // orb's try-out ended the game. The mod's own wrapper of each holds a strong handle on it, so keeping the wrappers keeps them.
        static readonly System.Collections.Generic.Dictionary<IntPtr, object?[]> pinned = new System.Collections.Generic.Dictionary<IntPtr, object?[]>();

        /// <summary>Keeps the try-out lists and glyphs of a skill of the mod's registered class alive. Call after every write of them.</summary>
        internal static void Pin(ProfessionSkill skill) {
            if (skill == null || skill.GetIl2CppType().Name != nameof(QuestPassiveSkill)) return;
            pinned[skill.Pointer] = new object?[] { skill.staticContentPrefabs, skill.dynamicContentPrefabs, skill.practiceGlyphSprites };
        }

        internal static ProfessionSkill NewPassive(string name) {
            ProfessionSkill? made = null;
            string how = "";
            // First: the mod's own empty skill class, registered with the game at start (QuestPassiveSkill below). The game has no
            // plain ProfessionSkill asset, and Unity will not make an instance of a class it has no script for (headset, 2026-11-22).
            if (passiveRegistered) {
                try {
                    ScriptableObject? o = ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.Of<QuestPassiveSkill>());
                    made = o != null ? o.TryCast<ProfessionSkill>() : null;
                    how = "the mod's own registered skill class";
                } catch (Exception e) { how = "the registered class threw " + e.GetType().Name + ": " + e.Message; }
            }
            if (made == null) {
                try { made = ScriptableObject.CreateInstance<ProfessionSkill>(); how += "; CreateInstance<ProfessionSkill>"; } catch (Exception e) { how += "; CreateInstance<T> threw " + e.GetType().Name; }
            }
            if (made == null) {
                try {
                    ScriptableObject? o = ScriptableObject.CreateInstance("ProfessionSkill");
                    made = o != null ? o.TryCast<ProfessionSkill>() : null;
                    how = "CreateInstance(\"ProfessionSkill\")";
                } catch (Exception e) { how += "; by name threw " + e.GetType().Name; }
            }
            if (made == null) {
                var items = Il2CppAlta.HashedGeneralValue<ProfessionSkill>.items;
                if (items != null)
                    foreach (var e in items) {
                        ProfessionSkill k = e.Value;
                        if (k == null || k.GetIl2CppType().Name != "ProfessionSkill") continue;
                        made = UnityEngine.Object.Instantiate(k);
                        how = $"a copy of the game's '{k.name}' (a plain ProfessionSkill)";
                        break;
                    }
            }
            if (!logged || made == null) {
                logged = true;
                if (made != null) MelonLogger.Msg($"[Archery] Quest: a later orb's skill is made by {how}");
                else MelonLogger.Error($"[Archery] Quest: no way to make a skill object for '{name}' ({how}); the orb is off");
            }
            if (made == null) throw new InvalidOperationException("no skill object could be made on Quest");
            made.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return made;
        }
    }

    /// <summary>Quest's PassiveOrbSkill: a skill with no behaviour of its own behind a later orb (Auto Dock, Ricochet, Shatter).</summary>
    public class QuestPassiveSkill : ProfessionSkill {
        public QuestPassiveSkill(IntPtr pointer) : base(pointer) { }
        public QuestPassiveSkill() : base(Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorPointer<QuestPassiveSkill>()) {
            Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorBody(this);
        }
    }
}
