# Blue's Archery Overhaul

An archery overhaul for **A Township Tale** (PC, MelonLoader). It rebuilds the Ranged skill tree into three paths: **Quick**, **Charge** and **Multi**. You commit to one, and the orbs you buy on the other two change how that one shoots.

Six of the 16 orbs are new: Quickdraw 4, Auto Dock, Burst, Seeking Volley, Ricochet and Shatter. The rest are the game's own, reworked.

## Skill tree

The Ranged tree as it stands at the archery shrine.

```
               Quickdraw 4                  Holy Arrow                Seeking Volley
                    |                           |                           |
  Auto Dock         |          Shatter          |                           |          Ricochet
           '--.     |                 '--.      |                           |  .------'
               Quickdraw 3                Charge Shot 3                   Burst
                          \                     |                     /
                           \                    |                    /
                            \                   |                   /
                             Quickdraw 2  Charge Shot 2   Three-Shot
                                  |             |             |
                                  |             |             |
                                  |             |             |
Arrow Gatherer               Quickdraw 1  Charge Shot 1    Two-Shot
      |                           |             |             |
      '---------------------------'-------------+-------------'
                                                |
                                              shrine
```

- **Top row:** the three legendaries. You can own one.
- **Auto Dock, Shatter, Ricochet:** side orbs, each off the third orb of its path.
- **Arrow Gatherer:** needs nothing and counts toward no path.

## Main path

Your main path is the path where you own the most orbs. Side orbs and the Arrow Gatherer don't count. Only the main path's shot works:

| Main path | Shot | Let go |
|---|---|---|
| **Quick** (left) | Snap | within 0.25 s |
| **Charge** (middle) | Charged shot | after 1 s or more |
| **Multi** (right) | Volley | after 0.3 s or more |

- **Ties:** you keep your current path. If it isn't one of the tied paths, Charge wins over Quick, and Quick over Multi.
- **Switching:** an orb that would change your main path shows a pulsing amber ring, and the stone tablet beside the tree lists what you would lose.

## Orbs

Default numbers, with the orb's own path as your main path. Times count from the moment the draw passes about two-thirds. 2x means twice your arrow's damage.

| Orb | Effect |
|---|---|
| **Quickdraw 1** | Snap: 1.25x damage, arrow 10% faster. Less wobble on every shot. |
| **Quickdraw 2** | Snap 1.5x, 20% faster. |
| **Quickdraw 3** | Snap 1.75x, 30% faster. |
| **Quickdraw 4** | Legendary. The snap becomes an instant straight line, 150 m long, through up to 8 creatures: 2.5x to the first, 1.75x to the second, 1.25x to each one after. It stops at anything solid or at a player. |
| **Auto Dock** | Side orb. While you keep hold of the bow, the next arrow from your quiver nocks itself 0.35 s after each shot. |
| **Charge Shot 1** | Hold 1 s: 2x damage, a stagger and an interrupt. |
| **Charge Shot 2** | Hold 1.75 s: 3.5x and a bigger stagger. |
| **Charge Shot 3** | Hold 2.5 s: 6x and the biggest stagger. |
| **Holy Arrow** | Legendary. Hold the same draw to 5.5 s: a golden beam at 5 times the arrow's speed that blasts and stuns creatures within 7 m. |
| **Shatter** | Side orb. A charged hit blasts out behind the creature in a 60° cone, 10 m deep: up to 2x what the hit dealt, full within 3 m, a quarter at the edges. |
| **Two-Shot** | From 0.3 s: 5 ghost arrows, 15% damage each. |
| **Three-Shot** | From 1 s: 10 more ghosts, all 15 by 1.4 s, 6% each. |
| **Burst** | At 1.5 s: 15 pellets, 4% each, 30 m range. |
| **Seeking Volley** | Legendary. Rest your aim on a creature, or on another player when the server has PvP on, to lock it (0.45 s at 10 m, 0.9 s at 50 m), up to 4 locks. Let go within 25° of a lock and the volley homes, each ghost flying on from one lock to the next. Your own arrow doesn't fly: 5 ghosts take its place and it ends up where they land. |
| **Ricochet** | Side orb. Ghosts and pellets skip once off the ground. An arrow that hits a creature jumps to another within 15 m for half its damage, then once more for half of that. |
| **Arrow Gatherer** | Arrows and feathers go to your pouch. Arrows shot from a quiver return to it after 15 s, or at once if one falls out of the world or sinks into a wall. |

Stagger and interrupts work on creatures only. An interrupt cancels what the creature is doing where the game allows it: never on Wyrms, and at most once every 3 s per creature.

## Off-path orbs

Orbs outside your main path don't give you their own shot. They add to yours:

| Main path | Quickdraw 1-3 add | Charge Shot 1-3 add | Two-Shot, Three-Shot, Burst add |
|---|---|---|---|
| **Quick** | | **Snap weight.** Snaps stagger, more per orb. From 2 orbs they can interrupt. | **Needles.** 1 thin extra arrow per orb on each snap, 15% damage each. With Quickdraw 4 they are lines too. |
| **Charge** | **Faster charge.** Stages and Holy Arrow come 12% sooner per orb (3 orbs: Holy Arrow at 3.5 s). Steadier charged shots. | | **Heavy arrows.** 2 per orb beside each charged shot, 15% damage each, not multiplied by the charge. Shatter's blast gains 0.15x per orb. |
| **Multi** | **Faster volley.** Every volley time is 25% shorter per orb (3 orbs: a full volley by 0.4 s). Per orb, a 15% tighter spread and 15% quicker Seeking locks. | **Hold stages.** Past a full volley, one stage per orb, 0.75 s apart. The ghosts turn gold, and each stage adds 10% ghost and pellet damage and a little stagger. | |

- **Side orbs** work on any main path. Auto Dock always works in full. Ricochet and Shatter work at half strength off their own path.
- **Legendaries** work only on their own path, and buying one makes that path your main path. To switch legendary, sell yours first.

## Install

Download [`BluesArcheryOverhaul.dll`](BluesArcheryOverhaul.dll) (0.19.14; also on the [Releases](../../releases) page) and put it in your `Mods` folder. Required on both the server and the client (MelonLoader 0.7.3).

## Skyfall (admins only)

A skill that only a server admin can hand out, from the server launcher's Console. It isn't part of the tree.

```
skyfall                   list who has it
skyfall give <player>     give it (exact name, any case)
skyfall give all          give it to everyone online
skyfall take <player>     take it away
```

To cast: draw fully, aim at least 25° up, hold 1 s, let go. A sigil opens in the sky, giant arrows rain down in 3 waves, then a huge nail drops in the middle. **Everything it hits dies**, creatures and players, except the caster and players within 3 m of where the caster let go.

## Settings

Everything is in `UserData\BluesArcheryOverhaul.cfg`, and edits apply within 2 s while the game runs. The `[Tree]` switches that add or remove an orb need a restart of the server and every game.

- **On a server, the server's settings count.** When you join, the server sends its settings and your game uses them until you leave; your own file is not used there (edits to it wait until you leave). Your file is never changed. When the server owner edits the server's file, every game on it gets the change within a few seconds.
- **Your own sections:** `[Log]`, `[Timing]`, `[Telemetry]`, `[Join]` and `[Settings]` are never set by a server.
- **`[Join] SettingsMismatch`** (server): a game that cannot take the server's settings (another build of the mod) is only logged (`log`, the default) or turned away (`refuse`).
- **`[TreeLayout]`** places each orb at the shrine.
- **`[PvP]`** decides whether arrows hurt players. Everything is on by default: arrows, the extra arrows, Ricochet's jumps, Shatter's blast and Holy Arrow's blast all hurt other players (the mod's own hits at half a creature's damage, `ModHitShare`), and Seeking Volley locks them (`[SeekingVolley] LockPlayers`). The server's own PvP setting must be on too, and its PvP damage multiplier applies on top.
- **`[Log] Level = Debug`** gives detailed logs.
- **Orb icons:** put `<Name>.png` in `UserData\BluesArcheryOverhaul\Icons\`. Name is Quickdraw4, TwoShot, ThreeShot, Burst, SeekingVolley, HolyArrow, AutoDock, Ricochet, Shatter or QuiverGather (the Arrow Gatherer).

## License

The code is under the [MIT License](LICENSE). The images in `BluesArcheryOverhaul/Resources/` (also embedded in the DLL) are made from A Township Tale's own art and are not covered by it; they belong to Alta.
