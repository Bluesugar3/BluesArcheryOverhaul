# QuickshotRing

An archery overhaul mod for **A Township Tale** (PC, MelonLoader).
It adds new orbs to the Ranged skill tree at the archery shrine, new shot types, and visual effects that use the game's own art.

## Install

You need:

- MelonLoader **0.7.3**
- **SyncLib** (in `Mods`)
- The same `QuickshotRing.dll` on **the server and every player's game**. A player with an older or missing copy will see plain arrows and may be kicked by SyncLib.

Steps:

1. Download `QuickshotRing.dll` from the [Releases](../../releases) page.
2. Close the game and the server.
3. Put the DLL in `A Township Tale\Mods\` (game) and in the server's `Mods\` folder.
4. Start the server, then the game. The log `MelonLoader\Latest.log` should show lines starting with `[Archery]`, including `[Archery] Self-check:` (if it says `FAILED`, don't play on that build).

## What it adds

Visit the archery shrine and open the Ranged tree. The tree has three lines. Whichever line you own the most orbs in becomes your **main path**; orbs from the other lines still help, but at a smaller share.

| Line | What it does |
|---|---|
| **Quick** | Faster, steadier snap shots. Quickdraw 4: an instant straight shot that pierces through up to 8 creatures. **Auto Dock**: the next arrow from your quiver nocks itself after each shot. |
| **Multi (Volley)** | Two-Shot and Three-Shot fire ghost arrows with the real one. Holding longer turns the ghosts gold and stronger. **Burst**: 30 spectral arrows in a ring. **Ricochet**: arrows jump to nearby creatures (up to 2 bounces). **Seeking Volley**: hold your aim on creatures to lock them, and the volley homes in on them. |
| **Charge Shot** | Hold to charge in 3 stages for more damage, stagger and interrupts. **Shatter**: a charged hit blasts out behind the creature, hurting everything in a cone. |
| **Holy Arrow** (top) | Hold a full draw to arm a golden beam: fast, heavy hit plus a 7 m stunning blast. |
| **Arrow Gatherer** | Shot arrows return to your quiver after 15 seconds. |

Only one top-tier ("ultimate") orb can be bought at a time.

### Skyfall (admin only)

A server-console skill, not part of the tree. In the server launcher's Console:

```
skyfall                   list who has it
skyfall give <player>     give it (exact name, any case)
skyfall give all          give it to everyone online
skyfall take <player>     remove it
```

To cast: draw fully, aim at least 25° up, hold for 1 second, release. A sigil opens in the sky, giant arrows rain down in 3 waves, then a huge nail drops in the middle. **Everything it hits dies** (creatures and players), except the caster and anyone within 3 m of them.

## Settings

All numbers and colours are in `UserData\QuickshotRing.cfg`. Edits are picked up within 2 seconds while the game runs.

- Settings marked `[shared]` must be the same on the server and every player. Compare the `[Archery] Settings: shared checksum` line in both logs.
- `[Log] Level = Debug` gives detailed logs for troubleshooting.
- `[PvP]` controls whether arrows can hurt players (the server's own PvP setting must also be on).
- Custom orb icons: drop `<OrbName>.png` into `UserData\QuickshotRing\Icons\`.

## Known conflicts

- **RevampedSkills**: if the server and players run different versions, the shrine breaks (no buying or selling). Everyone must run the server's exact mod list.

## Building from source

Requires the .NET SDK and a local copy of the game (the build references its DLLs).

```
dotnet build QuickshotRing -c Release -p:GameDir="C:\path\to\A Township Tale"
```

Output: `QuickshotRing\bin\Release\QuickshotRing.dll`.
Add `-p:Deploy=true` to copy it into the game's `Mods` folder, `-p:DeployServer=true` for the server.

Unit tests:

```
dotnet run --project QuickshotRing.Tests -c Release
```

## Repository layout

| Folder | Contents |
|---|---|
| `QuickshotRing/` | The mod's source (C#). `Resources/` holds the orb icons embedded in the DLL. |
| `QuickshotRing.Tests/` | Unit tests for the game-independent rule files. |
| `Template/` | Empty starting point for a new mod. |
| `tools/` | Test-build menu and install scripts for play-testing. |
| `docs/` | Design specs, plans and play-test notes. |
