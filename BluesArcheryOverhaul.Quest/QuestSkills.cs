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
        static bool logged;

        internal static ProfessionSkill NewPassive(string name) {
            ProfessionSkill? made = null;
            string how = "";
            try { made = ScriptableObject.CreateInstance<ProfessionSkill>(); how = "CreateInstance<ProfessionSkill>"; } catch (Exception e) { how = "CreateInstance<T> threw " + e.GetType().Name; }
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
}
