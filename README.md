# QuickshotRing

QuickshotRing is an archery mod for **A Township Tale** on PC. It rebuilds the Ranged skill tree at the archery shrine so you can choose what kind of archer you want to be.

There are three kinds:

- **Quick**: fast snap shots, one after another.
- **Multi**: volleys of ghost arrows that fly with your own.
- **Charge**: one heavy arrow that you hold to power up.

You pick one as your main path. The orbs you buy on the other two paths aren't wasted: they make your main shot better. The effects are made from the game's own art, so they look like they belong in the game.

## The skill tree

This is the Ranged tree with the mod installed. It has 16 orbs.

```
                  QUICK                 CHARGE                MULTI
             fast snap shots       one heavy arrow      volleys of arrows

 Legendary   [ Quickdraw 4 ]        [ Holy Arrow ]      [ Seeking Volley ]
                    |                     |                     |
 Side orb           +--[ Auto Dock ]      +--[ Shatter ]        +--[ Ricochet ]
                    |                     |                     |
 Third       [ Quickdraw 3 ]      [ Charge Shot 3 ]         [ Burst ]
                    |                     |                     |
 Second      [ Quickdraw 2 ]      [ Charge Shot 2 ]       [ Three-Shot ]
                    |                     |                     |
 First       [ Quickdraw 1 ]      [ Charge Shot 1 ]        [ Two-Shot ]


 On its own  [ Arrow Gatherer ]
```

How to read it:

- Each column is a **path**. You buy it from the bottom up: every orb needs the one below it.
- After the third orb of a path you can buy its **side orb**, its **legendary**, or both.
- The Arrow Gatherer needs nothing and belongs to no path.

## Pick a path

The tree is built so that you specialise. **Your main path is the path where you own the most orbs**, and it decides what your bow does:

| Main path | Your shot |
|---|---|
| **Quick** | **Snap shot.** Let go right after you draw, while a gold ring closes on the arrow tip. The arrow hits harder and flies faster. |
| **Multi** | **Volley.** Hold the draw for a moment and ghost arrows fly with yours: 5 with Two-Shot, 15 with Three-Shot, 30 with Burst. |
| **Charge** | **Charged shot.** Hold the draw to power up one arrow: up to 6 times the damage, and it staggers what it hits. |

Only your main path's shot works. A Quick archer doesn't fire volleys, and a Multi archer doesn't charge.

Good to know:

- Side orbs and the Arrow Gatherer don't count when the game works out your main path.
- On a tie you stay on the path you were already on. If you were on none of the tied paths, Charge wins over Quick, and Quick over Multi.
- Before you buy an orb that would switch your path, the shrine puts a pulsing amber ring on it, and the stone tablet beside the tree tells you what you would lose.

## The other paths back up your main path

This is the heart of the mod. An orb you buy outside your main path doesn't give you that path's shot. It adds something to **your** shot instead.

| If your main path is | Quick orbs add | Multi orbs add | Charge orbs add |
|---|---|---|---|
| **Quick** | (your snap shot) | **Needles.** One thin extra arrow per orb flies with every snap. | **Weight.** Your snaps stagger creatures. With two or more orbs they can also interrupt what a creature is doing. |
| **Multi** | **Speed.** The volley is ready much sooner (with three orbs, a full volley in under half a second) and flies tighter. | (your volley) | **Gold arrows.** Keep holding a full volley and the ghost arrows turn gold, one step per orb. Each step adds damage. |
| **Charge** | **Speed.** Every charge is ready sooner (12% per orb) and the shot is steadier. | **Heavy arrows.** Two per orb fly beside every charged shot. | (your charged shot) |

So a Quick archer with Charge orbs shoots fast arrows that knock creatures around, and a Charge archer with all three Multi orbs fires a charged arrow with six heavy arrows beside it.

Two more rules:

- **Side orbs work on any main path.** Auto Dock always works in full. Ricochet and Shatter work at half strength when their path isn't your main one.
- **You can own only one legendary.** It works only on its own path, and buying it makes that path your main path. You can still try the other two at the shrine. To switch, sell yours first.

## What each orb does

Each orb as it works when its path is your main path.

| Orb | What it does |
|---|---|
| **Quickdraw 1, 2, 3** | Snap shots: let go within about a quarter of a second of drawing. Damage goes up to 1.25x, 1.5x and 1.75x, and the arrow gets faster and steadier with each orb. |
| **Quickdraw 4** (legendary) | Every snap becomes an instant straight line that goes through up to 8 creatures. The first takes 2.5x damage, the ones behind it less. |
| **Auto Dock** (side orb) | Keep hold of the bow, and after each shot the next arrow from your quiver puts itself on the string. |
| **Two-Shot** | Hold the draw for a moment and 5 ghost arrows fly with yours. |
| **Three-Shot** | Hold a little over a second and 10 more ghost arrows join, 15 in all. |
| **Burst** | Hold a second and a half and 15 small pellets join the ghosts. They only reach 30 metres. |
| **Seeking Volley** (legendary) | Rest your aim on a creature to lock on to it, up to 4 creatures. Let go and the volley homes in, flying on from one locked creature to the next. |
| **Ricochet** (side orb) | Ghost arrows and pellets skip once off the ground. An arrow that hits a creature jumps to another one nearby for half its damage, then once more. |
| **Charge Shot 1, 2, 3** | Hold the draw to charge the arrow: 1 second for 2x damage, 1.75 seconds for 3.5x, 2.5 seconds for 6x. Charged hits stagger creatures. |
| **Holy Arrow** (legendary) | Keep holding the same draw for 5.5 seconds, then let go: a golden beam that hits hard, and blasts and stuns creatures within 7 metres of where it lands. |
| **Shatter** (side orb) | When a charged arrow hits a creature, a blast bursts out of its back and hurts the creatures behind it, up to 10 metres away. |
| **Arrow Gatherer** | Picks up arrows and feathers into your pouch. Arrows you shoot from a quiver come back to it after about 15 seconds. |

## Example builds

Each one uses 8 orbs.

- **Piercer** (Quick): Quickdraw 1 to 4, Charge Shot 1 to 3, Shatter. Every snap is a line through a row of creatures that staggers each one it passes.
- **Swarm** (Multi): Two-Shot, Three-Shot, Burst, Seeking Volley, Ricochet, Quickdraw 1 to 3. A full volley in under half a second that homes in on up to 4 creatures, and every hit jumps on to another.
- **Siege** (Charge): Charge Shot 1 to 3, Holy Arrow, Shatter, Two-Shot, Three-Shot, Burst. A 6x arrow with six heavy arrows beside it and a blast behind whatever it hits. Hold longer for Holy Arrow.

## Install

You need MelonLoader **0.7.3**, and the same `QuickshotRing.dll` on **the server and every player's game**. A player without it only sees plain arrows.

1. Download `QuickshotRing.dll` from the [Releases](../../releases) page.
2. Close the game and stop the server.
3. Put the file in the game's `Mods` folder and in the server's `Mods` folder.
4. Start the server, then the game.

To check that it loaded, open `MelonLoader\Latest.log`. You should see lines that start with `[Archery]`, and one that says `[Archery] Self-check:`. If that line says `FAILED`, don't play on that build.

## Buying and selling orbs

- **Try first.** If the server requires it, hold the orb at the shrine and use it once before you buy it. The tablet says what to do, and why an orb can't be bought.
- **Sell from the top down.** Unless the server allows it, you can't sell an orb while you own one above it.
- **Skill cap.** The game limits how many skills you can own across all professions. A full archery build is 14 orbs, so you may have to choose.

## Skyfall (admins only)

Skyfall is a skill that only a server admin can hand out. It isn't part of the tree. Type these into the server launcher's Console:

```
skyfall                   list who has it
skyfall give <player>     give it (exact name, any case)
skyfall give all          give it to everyone online
skyfall take <player>     take it away
```

To cast it: draw fully, aim at least 25 degrees up, hold for 1 second, then let go. A sigil opens in the sky, giant arrows rain down in 3 waves, and a huge nail drops in the middle. **Everything it hits dies**, creatures and players, except the caster and players within 3 metres of where the caster let go.

## Settings

Every number and colour is in `UserData\QuickshotRing.cfg`. Changes are picked up within 2 seconds while the game runs. The one exception is the `[Tree]` switches that add or remove an orb: those need a restart of the server and every game.

- Settings marked `[shared]` must be the same on the server and on every player's game. To compare, look for the `[Archery] Settings: shared checksum` line in both logs.
- `[PvP]` decides whether arrows can hurt players. The server's own PvP setting must be on too.
- `[Log] Level = Debug` gives detailed logs when something goes wrong.
- To use your own orb icons, put `<Name>.png` in `UserData\QuickshotRing\Icons\`. Name is one of Quickdraw4, TwoShot, ThreeShot, Burst, SeekingVolley, HolyArrow, AutoDock, Ricochet, Shatter or QuiverGather (the Arrow Gatherer).

## Known conflicts

- **RevampedSkills.** If the server and a player run different versions of it, the shrine breaks for that player: no buying, no selling. Everyone must run the server's exact list of mods. With RevampedSkills installed, it returns your shot arrows instead of the Arrow Gatherer.

## Building from source

For modders. You need the .NET SDK and your own copy of the game, because the build uses the game's files.

```
dotnet build QuickshotRing -c Release -p:GameDir="C:\path\to\A Township Tale"
```

The result is `QuickshotRing\bin\Release\QuickshotRing.dll`. Add `-p:Deploy=true` to copy it into the game's `Mods` folder, and `-p:DeployServer=true -p:ServerDir="C:\path\to\server"` for the server.

To run the tests:

```
dotnet run --project QuickshotRing.Tests -c Release
```
