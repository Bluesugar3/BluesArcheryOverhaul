using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Alta.Networking.Scripts.Player;

// The play-test kit (BluesArcheryTestKit, its own mod and DLL) reaches this mod's internals; it checks this mod's version at start.
[assembly: InternalsVisibleTo("BluesArcheryTestKit")]

namespace BluesArcheryOverhaul {
    // The only places this mod calls into the play-test kit. The kit sets them at its start; without the kit every one stays null
    // (or false) and this mod behaves exactly as it did with the test settings at their defaults: no test build, no test tools.
    internal static class KitHooks {
        /// <summary>Builds (server): the orbs lent to this player (BluesArcheryTestKit's TestBuild, [TestBuild] Orbs).</summary>
        internal static Func<string?, IReadOnlyList<Orb>>? Lent;
        /// <summary>Builds (server): the orbs hidden from this player's archery build (BluesArcheryTestKit's TestBuild, [TestBuild] Without).</summary>
        internal static Func<string?, IReadOnlyList<Orb>>? Hidden;
        /// <summary>Builds.Refresh (server): a manager's build was recomputed; the orbs it really owns (BluesArcheryTestKit's TestBuild.Sync).</summary>
        internal static Action<PlayerProgressionManager, List<Orb>>? Refreshed;
        /// <summary>Builds.OnUpdateCounts (server): the game recounted a manager's skill levels (BluesArcheryTestKit's TestBuild.Reapply).</summary>
        internal static Action<PlayerProgressionManager>? Recounted;
        /// <summary>Skyfall.DecideShooter: also run the caster's release through the spectator path (BluesArcheryTestKit, [Kit] SkyfallSpectatorTest).</summary>
        internal static bool SpectatorTest;
    }
}
