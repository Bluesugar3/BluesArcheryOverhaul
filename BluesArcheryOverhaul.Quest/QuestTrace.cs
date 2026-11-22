using MelonLoader;
using System.Collections.Generic;

namespace BluesArcheryOverhaul {
    // Quest only, while the port is being checked: one log line the first time each step is reached, so a hard crash (which
    // leaves no exception in the log) shows the last step that ran.
    internal static class QuestTrace {
        static readonly HashSet<string> seen = new HashSet<string>();
        internal static void Step(string what) {
            if (QuestWatch.On && seen.Add(what)) MelonLogger.Msg($"[Archery] Quest trace: {what}");
        }
    }
}
