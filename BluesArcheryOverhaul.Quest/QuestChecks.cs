using System;
using MelonLoader;

namespace BluesArcheryOverhaul {
    // Quest only: facts about the IL2CPP interop the shared code leans on, checked on the headset and logged once.
    internal static class QuestChecks {
        static bool identityLogged;

        /// <summary>The shared code compares game objects with ReferenceEquals and keys dictionaries by them. That holds on Quest
        /// only if the interop hands back the same managed object for the same game object each time it is read.</summary>
        internal static void WrapperIdentity(ProfessionSkillTree tree) {
            if (identityLogged) return;
            identityLogged = true;
            try {
                if (tree.Slots == null || tree.Slots.Count == 0) return;
                ProgressionSlot a = tree.Slots[0], b = tree.Slots[0];
                ProfessionSkill? ka = a.Skill, kb = tree.Slots[0].Skill;
                bool slotSame = ReferenceEquals(a, b), skillSame = ReferenceEquals(ka, kb);
                string text = $"[Archery] Quest: the same game object read twice is {(slotSame ? "the same" : "NOT the same")} managed object (a plain object), " +
                              $"{(skillSame ? "the same" : "NOT the same")} (a Unity object)";
                if (slotSame && skillSame) MelonLogger.Msg(text);
                else MelonLogger.Error(text + ": every ReferenceEquals and dictionary keyed by a game object in the shared code is wrong on Quest");
            } catch (Exception e) { MelonLogger.Error($"[Archery] Quest: the object identity check failed: {e.Message}"); }
        }
    }
}
