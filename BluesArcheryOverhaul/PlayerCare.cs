using Alta.Networking.Scripts.Player;
using Alta.StatSystem;

namespace BluesArcheryOverhaul {
    // Heal and Feed, shared by the play-test range (BluesArcheryTestKit's TestRangeHelpers.cs) and Skyfall's festival mode (SkyfallServer.cs). Server only:
    // the game syncs both stats from the server. Neither catches: the caller does, per player.
    internal static class PlayerCare {
        /// <summary>Health back to its maximum (food is not touched). Null when done, else why not.</summary>
        internal static string? Heal(Player p, out float before, out float after, out float max) {
            before = after = max = 0f;
            HealthObject? h = p.PlayerController != null ? p.PlayerController.Health : null;
            if (h == null) return "has no health to fill";
            before = h.Health; max = h.MaxHealth;
            if (!(before > 0f)) return "is dead: respawn first";
            h.Health = max;
            after = h.Health;
            return null;
        }

        /// <summary>True when the player's health went up.</summary>
        internal static bool Heal(Player p) => Heal(p, out float before, out float after, out _) == null && after > before;

        /// <summary>The 'Hunger' stat to its maximum (the stat SmoothLocomotion reads by that name; its Base setter syncs it like the
        /// game's own drain). Dead players and spirits are left alone. Null when done, else why not.</summary>
        internal static string? Feed(Player p, out float before, out float after, out float max) {
            before = after = max = 0f;
            HealthObject? h = p.PlayerController != null ? p.PlayerController.Health : null;
            if (h == null || !(h.Health > 0f)) return "no body or dead";
            if (h.StatManager == null || !h.StatManager.GetStat("Hunger", out Stat hunger) || hunger == null) return "no hunger";
            before = hunger.Value;
            hunger.Base = hunger.Maximum;
            after = hunger.Value; max = hunger.Maximum;
            return null;
        }

        /// <summary>True when the player's hunger went up.</summary>
        internal static bool Feed(Player p) => Feed(p, out float before, out float after, out _) == null && after > before;
    }
}
