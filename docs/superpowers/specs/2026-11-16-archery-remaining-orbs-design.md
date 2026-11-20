# Archery: the remaining orbs, under the main-path rule

Date: 2026-11-16. Mod: QuickshotRing, currently 0.13.0.
The user asked (2026-11-16): "Build the orbs that are not built yet. Get rid of Brace and Overcharge. Add another orb off of Quickdraw 3, which adds auto docking for quicker arrows: if you have a quiver, after you shoot an arrow, the next arrow comes out of your quiver and docks into the bow automatically."

The user was away play-testing when this was written and asked for building to start, so **every design choice in this document is Claude's call** unless it quotes the user. Each orb's first description comes from `2026-11-12-archery-combinations-design.md` (Part 4 and Part 5), which the user approved; this document re-fits those orbs to the rule that replaced that design's Rule 1: one main path per archer (`2026-11-13-archery-main-path-design.md`). Where the two older documents and this one disagree about these ten orbs, this one wins.

## What is built by this document

Ten orbs. Nine were designed and never built; one is new.

| Orb | Kind | Needs | In one line |
|---|---|---|---|
| Snap Draw | Quick side orb | Quickdraw 1 | The quick-shot window opens at a lighter pull and lasts longer. |
| Mark | Quick side orb | Quickdraw 2 | Quick shots pin creatures; a second quick shot on a pinned creature pops the pin. |
| **Auto Dock** (new) | Quick side orb | Quickdraw 3 | After a shot, the next arrow leaves your quiver and nocks itself. |
| Converge | Multi side orb | Two-Shot | The volley's rings cross where your arrow will land. |
| Ricochet | Multi side orb | Three-Shot | A ghost that hits ground, rock or wood bounces once toward a creature. |
| Seeking Volley | Multi legendary | Burst | Hold your aim on a creature to lock it; the volley and the arrow curve in. |
| Echo | Bridge, Quick and Multi | Quickdraw 1 or Two-Shot | Quick archer: twice the needles. Multi archer: an early release still fires two. |
| Tracer | Bridge, Quick and Multi | Quickdraw 2 or Three-Shot | Quick archer: a tracer follows each quick shot and hits again. Multi archer: ghosts pin what they hit. |
| Gold Volley | Bridge, Multi and Charge | Two-Shot or Charge Shot 1 | Multi archer: ghosts stagger twice as hard. Charge archer: two more heavy arrows. |
| Shatter | Bridge, Multi and Charge | Three-Shot or Charge Shot 2 | The heaviest hit of a release throws splinters at what stands behind. |

Brace and Overcharge are removed from the mod entirely (the user's words), not shelved.

## Rules that apply to all ten

1. **Line counts do not change.** Only the main orbs and the legendary of a line count toward a line (Quick 4, Multi 4 once Seeking Volley exists, Charge 4). Side orbs and bridges never count, so buying one can never change a main path, and the tablet's "orb N of M" stays about the main line. `Lines.Size(Multi)` becomes 4.
2. **A side orb works only when its line is the main path.** It improves that line's own shot, which nobody else has. For any other archer it does nothing, and the tablet says so ("does nothing until Quick is your main path"). Auto Dock is the one exception: it is a convenience, and works for whoever owns it.
3. **A bridge has two halves, chosen by the main path**, not by which neighbour it was bought through. A Charge archer who owns a Quick-and-Multi bridge gets nothing from it; the tablet says so.
4. **Never worse.** No orb here reduces anything.
5. **Slots.** Each costs one skill point (a per-orb setting) and one of the player's skill slots, like every other orb. With the usual 8 slots this is the real price.
6. **Buying.** A runtime-made orb cannot be bought until it is "practised". Each orb is marked practised when its owner-to-be does the thing its try-out line asks for (listed per orb below); the try-out area sets out the bow, arrows and target of the parent orb's skill (as `Tree.CopyTryOut` does).
7. **No new network traffic.** Everything rides the shot claim (kind, stage, targets, picks, marks: its fields for target ids and per-arrow picks already exist in `Wire.cs`) and the build bits (the side-orb and bridge flags already exist). Auto Dock needs no bit: only the server acts on it.
8. **Players** are never pinned, staggered, interrupted or locked by these orbs' own effects (player damage from spectral arrows stays parked). Seeking Volley may lock a player only as the older design says (`LockPlayers`, default `Never` until the user has tried it in PvP; the older design's default was `Always`).
9. **Numbers** are shared settings where the server's result depends on them; looks are per client.
10. **Looks** follow the standing rules: the game's own assets, each look its own small class with a fallback, no per-frame allocation, one Renderer per GameObject. Icons are built from the game's `Glyph_*` textures on the game's bow, each unique, and the sheet (`Art/drafts/sheet_final.png`) is shown to the user before the build that ships them is installed.

## The orbs

### Snap Draw (Quick side, needs Quickdraw 1)
For a Quick archer the quick-shot window opens at 55% draw instead of 68%, and ends at whichever is later: 0.35 s after the 55% crossing or 0.25 s after the 68% crossing. Buying it can only lengthen the window. Every other beat still counts from 68%. A quick shot from a light pull is a slower arrow (the game sets speed from pull). With Quickdraw 4, a line shot from 55% is a full line.
- Looks: the gold ring closes sooner and shuts at 55%, thin until 68%, then full.
- Practised by: a quick shot released below 68% draw while the orb is held for try-out.
- Tablet: "Quick shots: window opens at 55% pull, lasts 0.35s".

### Mark (Quick side, needs Quickdraw 2)
A Quick archer's quick-shot hits (flying or line) pin the creature for 12 s. Up to 3 pins per archer; a fourth replaces the oldest. A second quick shot on a pinned creature pops the pin: 0.2 extra stagger and an interrupt of a cancellable action. With Quickdraw 4, a line into a pinned creature jumps once to the nearest other pinned creature within 15 m at half the line's damage.
- The server owns the pins (per archer, creatures only). The shooter learns its own pins from its own hits; nobody else sees them.
- Looks (shooter only): a small three-segment ring where the arrow went in and a faint shaft of light above the creature, pulsing once a second, fading in its last second; a spark burst and a glass sound on a pop.
- Practised by: pinning the try-out target or any creature.
- Tablet: "Quick shots: pin creatures 12s (3 at once); a second hit pops for stagger 0.2".

### Auto Dock (Quick side, needs Quickdraw 3) — new
The user's words: "if you have a quiver, after you shoot an arrow, the next arrow comes out of your quiver and docks into the bow automatically."
- When the owner releases an arrow from a bow, and the bow's string is empty, and the owner carries a quiver (worn or held, with at least one arrow in it), then `[AutoDock] Seconds = 0.35` later the server takes one arrow out of that quiver and docks it on that bow's string, exactly as if the player had nocked it. The arrow taken is the one the quiver would hand out next.
- It does nothing if: the string already holds an arrow, the bow is no longer held by the owner, the owner grabbed the string or an arrow in the meantime, there is no quiver or it is empty, or the arrow cannot dock there. It never creates an arrow.
- Works for any main path (a convenience), and with every kind of shot, including the Quickdraw 4 line and Holy Arrow.
- With Quiver Gather: an arrow that Quiver Gather has returned to the quiver is used like any other.
- Looks: the arrow visibly travels from the quiver to the string over about 0.2 s (the game's own pouch-gatherer ghost, which Quiver Gather already plays, drawn toward the bow), a soft dock sound, and a light tick in the bow hand for the shooter.
- Practised by: shooting once with a quiver holding arrows while the orb is held for try-out (the server docks the next arrow as a demonstration even though the orb is not owned yet).
- Tablet: "After a shot: the next arrow nocks itself from your quiver".
- How it is done is the builder's to find in the game's code (`QuiverGather.cs` already tracks which quiver an arrow came from and docks arrows back into quivers on the server; the game's `PickupDock` docking and undocking are networked game calls, so no new messages are needed).

### Converge (Multi side, needs Two-Shot)
For a Multi archer the volley's rings aim to cross at the real arrow's predicted impact point, found by stepping the same arc the arrow flies, limited to between 8 and 40 m. If the arrow hits, the volley lands on the same creature at any range. It chokes Burst's pellet cone to 40% of its spread, centred on that point.
- Server and clients compute the same crossing point from the release pose (shared arc model).
- Looks: on the string, the ghost heads visibly tilt inward like a closing claw; where a cluster lands, one cracked-ring burst and one light.
- Practised by: releasing a volley while the orb is held for try-out.
- Tablet: "Volley: crosses where your arrow lands (8 to 40 m)".

### Ricochet (Multi side, needs Three-Shot)
For a Multi archer, a ghost or pellet that hits ground, rock or wood bounces once toward a creature within 6 m of the bounce, inside 120 degrees of its glancing direction. Candidates are only the creatures on the release's target list (sent with the shot) and the creature the real arrow hit, so every screen shows the same bounce. With no candidate it fades.
- Looks: a white spark, a white kink in the trail, a click; at most 3 clicks per volley.
- Practised by: releasing a full volley while the orb is held for try-out.
- Tablet: "Ghosts: bounce once off terrain toward a creature within 6 m".

### Seeking Volley (Multi legendary, needs Burst)
As Part 5 of the 2026-11-12 design ("Seeking Volley: the Lock"), with these changes for the main-path rule:
- Only a Multi archer can use it (a legendary owned off-path does nothing, as for the others). It is the fourth level of the Volley skill and counts as the Multi line's fourth orb.
- A Multi archer has no quick shot, so the "snap is not steered" exception is gone: with a lock, every release within 25 degrees of the line to the lock is steered, ghosts and the real arrow alike (the user's decision of 2026-11-12 that the real arrow curves too stands).
- The dwell is shortened by Quickdraw orbs as a complement (15%, 25%, 35%), as designed.
- Pins (from Tracer's Multi half) lock instantly when aimed at.
- "Once per lock" guaranteed knock-down: dropped with Overcharge; Gold Volley's doubled stagger applies normally.
- Locked seekers deal 60% of their share (a setting); the steered real arrow deals its full damage (a setting).
- `LockPlayers` default `Never` (see rule 8).
- Practised by: completing a lock on the try-out target.
- Tablet: "Volley: hold aim to lock a creature; ghosts and arrow curve in (seekers 60% damage)".
This is the largest piece and is built last, on its own.

### Echo (bridge, Quick and Multi; needs Quickdraw 1 or Two-Shot)
- Quick archer: quick shots fire twice the needles (2 per multi-arrow orb, and at least 2 with none), at 10% each instead of 15%.
- Multi archer: a release before the ghosts have formed, which today is a plain arrow, fires 2 needles at 10%.
- Charge archer: nothing.
- Practised by: a quick shot or an early release that fires its needles while the orb is held for try-out.
- Tablet: Quick "Quick shots: needles doubled (10% each)"; Multi "Early releases: 2 needles, 10% each".

### Tracer (bridge, Quick and Multi; needs Quickdraw 2 or Three-Shot)
- Quick archer: every quick shot carries one tracer on the arrow's exact path 0.05 s behind it, at 10%, so a quick shot that hits lands twice. With Mark, the tracer never pops the pin its own arrow just made: a pin is popped only by a later release.
- Multi archer: the first ghost to land on each creature pins it exactly as Mark does (12 s, up to 3, shooter only). Pins made this way lock instantly under Seeking Volley and are popped by nothing (a Multi archer has no quick shot); they are for Seeking Volley and for the Holy/Quick archers' rules to ignore.
- Charge archer: nothing.
- Practised by: a quick shot with its tracer, or a volley that pins a creature, while held for try-out.
- Tablet: Quick "Quick shots: a tracer follows and hits again, 10%"; Multi "Ghosts: pin what they hit 12s (locks instantly with Seeking Volley)".

### Gold Volley (bridge, Multi and Charge; needs Two-Shot or Charge Shot 1)
The older design's Gold Volley is now mostly the built-in complements (gold ghosts that stagger; heavy arrows). It becomes the orb that strengthens them:
- Multi archer: ghost stagger doubles (0.06 per ghost per Charge Shot orb instead of 0.03), and with no Charge Shot orbs each ghost still carries 0.03.
- Charge archer: 2 more heavy arrows on every charged shot (so 2 with no multi-arrow orbs, up to 8 with three). The builder extends the heavy-arrow layout to 8: the hexagon plus one above and one below, mirror-symmetric and centred.
- Quick archer: nothing.
- Practised by: a volley or a charged shot that fires the gold or heavy arrows while held for try-out.
- Tablet: Multi "Ghosts: stagger doubled"; Charge "Charged shots: +2 heavy arrows".

### Shatter (bridge, Multi and Charge; needs Three-Shot or Charge Shot 2)
The heaviest hit of each release shatters: for a Charge archer the charged arrow's hit on a creature; for a Multi archer the first ghost to land on a creature. It throws splinters onward in a 40 degree cone up to 4 m: 3, plus 3 per multi-arrow orb (3 to 12), each at 5% of the arrow's power (flat, as heavy arrows are). Splinters strike only creatures on the release's target list or directly behind the struck one, never the struck creature itself.
- Quick archer: nothing.
- Looks: the game's shatter sound and shard sprites, small splinter streaks on 0.15 s flights: orange for a Charge archer, the ghosts' colour for a Multi archer.
- Practised by: a hit that shatters while held for try-out (the try-out target counts as struck; splinters need no second target to practise).
- Tablet: "Heaviest hit: throws 3 to 12 splinters behind it, 5% each".

## The tree

The ten orbs go where `docs/tree24-layout.json` puts them (side orbs beside their parent, bridges between their two lines' columns), Auto Dock beside Quickdraw 3 on the side away from the Multi column. Positions are `[TreeLayout]` settings. Slot ids for nine of them are already reserved in `SlotIds`; Auto Dock gets a new fixed id. The shrine's connection lines stay under the game's cap of 127. The tree colours and the tablet learn the new orbs through `Lines` and `TabletText` (names, which line or bridge, the "does nothing until ..." line, and one fact line per orb and main path as listed above).

## Build order

Each batch is built by Opus (gameplay and rules) with Fable for the looks and icons, reviewed by Fable, and ends in something that can be play-tested.

0. Remove Brace and Overcharge.
1. Scaffolding: all ten orbs in the tree (ids, skills, positions, costs, links, try-outs, practice hooks), build flags, tablet and tutorial wording, ten unique icons. After this batch every orb can be seen, read about, tried and bought, and does nothing yet.
2. Quick side: Snap Draw, Mark, Auto Dock.
3. Multi side: Converge, Ricochet.
4. Bridges: Echo, Tracer, Gold Volley, Shatter.
5. Seeking Volley.

## Risks

- Ten orbs are being built while 0.13.0 has not been play-tested, on design choices the user has not seen. Each batch is separately reviewable and each orb can be left out of the shrine by a `[Tree] <Orb>Orb = false` setting, so a bad one can be switched off without a rebuild.
- Auto Dock depends on docking an arrow on a string from the server with nobody holding it; if the game refuses that, the fallback is docking it the moment the owner next grabs the string.
- Seeking Volley steers a real arrow on two machines at once; it is the piece most likely to need a second pass after play.
- With 8 slots a player cannot hold more than a main line plus a few of these; that is intended, but nothing here has been balanced in play.

## What the research changed (2026-11-16, from reading the game's code; nothing checked in play)

Notes: `.superpowers/sdd/2026-11-16-orbs/research-autodock.md` and `research-targets.md`. These are Claude's rulings on top of the design above.

- **Auto Dock** is feasible as written: the server docks the quiver's next arrow on the string with no hand (`DockInto(null, bow.StringDock, 1, instant)`), from a server postfix on `BowString.OnShoot` and a poll. It must set the arrow's last interactor to the owner before docking (an arrow never held would otherwise be able to hit the archer's own bow or body at release), and hand the arrow to Quiver Gather's tracking so it still returns.
- **Creatures are named on the wire by `HealthObject.Entity.PrefabRoot.Identifier`** (fits the claim's 17 bits), never by instance id.
- **Targets must be checked and relayed by the server.** Today the server clears the claim's targets, picks and marks before relaying, and the shooter does not write them. Ricochet, Shatter and Seeking Volley add: shooter writes, server checks (each target exists, is a creature, is within range of the shot) and relays.
- **The shooter never receives the relay of its own shot.** So the shooter's view of its own pins (Mark, Tracer) and its lock is a local mirror the server cannot correct. Ruling: the server's pins decide what happens; the shooter's markers are looks only and may be wrong until the pin expires. Never tell the player a pop happened unless the shooter's own mirror says so.
- **The server decides where a steered real arrow lands** (the shooter keeps authority for only about 0.2 s). Seeking Volley steers the real arrow in a postfix on `Arrow.ComplexArrowFlight` on the server and the shooter, with its own clock, gravity off; the shooter's copy is expected to be corrected toward the server's.
- **Mark needs the shooter to know its own quick shot hit a creature.** The client hit hook is unproven for plain arrows; the embed fallback used for armed arrows is extended to every local quick shot.
- **Ricochet's "ground, rock or wood"** has no material name in the game. Ruling: a hit bounces when it has no creature or player health behind it and is on terrain or a static world object; the batch starts with a log probe of what layers and channels real hits report, and the rule is a setting-driven list.
- **Shatter and Tracer's "first ghost on a creature this release"** is decided where the server flushes a release's spectral hits (`Hits.FlushSpectral`), not per physics tick. Shatter's Charge half sits next to the pierce in `Weight.RealHit`.

## Added by the user, 2026-11-16: ricochet on the second multi-arrow orb, a chain bounce on the third

The user's words: "new addons to add to multi shot. Add ricochet from the second orb of multi-arrow as a buff. And then on the third orb of multi-shot, have it be, for example, whenever you hit an enemy, it bounces to another enemy and does diminished amount of damage."

- **Ricochet** stays as designed above (a ghost that hits ground, rock or wood bounces once toward a creature) and is tied to the second multi-arrow orb, Three-Shot. That is where the design already put it.
- **Chain** (new; the name is Claude's) is tied to the third multi-arrow orb, Burst. Multi main only. The numbers and limits below are Claude's calls; the user gave the idea "bounces to another enemy and does diminished damage".
  - When a release's ghosts or pellets land on a creature, the hit jumps once to the nearest other creature within `[Chain] Range` 8 m and deals `[Chain] Share` 50% of the damage the struck creature took from that release's ghosts. One jump per struck creature per release, delivered as one batched hit (so thirty pellets on one creature are one jump, not thirty). A jump never jumps again and never returns to a creature already struck by that release when another candidate exists.
  - The real arrow's hit on a creature jumps too, at the same share (`[Chain] RealArrow`, on).
  - Candidates are the release's relayed targets and the creatures that release struck, so every screen draws the same jump; the server's choice is the one that deals damage. Never players.
  - A ghost that reached its creature by a Ricochet bounce can still chain.
  - Looks: one thin arc of the ghosts' colour from the struck creature to the next, a small spark where it lands, one soft sound; at most 3 sounds per release.
  - Practised by: a volley that hits a creature while the orb is held for try-out (through the "own action or any shot" rule).
  - Tablet: "Hits: jump once to another creature within 8 m, 50% damage".
- **Decided by the user (2026-11-16): "Separate side orbs".** Ricochet stays its own side orb beside Three-Shot. Chain is an eleventh orb, a Multi side orb that needs Burst, costing a point and a slot like the others, with its own icon and `[Tree] ChainOrb` setting. Like every side orb it works only for a Multi main. Clients need to know who has it (the shooter learns its own build from the server's bits, and spectators draw the jump), so the build block goes to version 3 and side-orb bit 16 becomes Chain; bit 32 stays reserved. This replaces the earlier ruling that both freed bits stay unused for good: that ruling was made before an orb needed one, and a new block version is what makes the reuse safe (Claude's call).

## Scope (the user, 2026-11-16): four orbs only

The user's words: "lets go back, only work on, Autodock, Ricochet, Seeking Voly, and shatter".

- **Worked on:** Auto Dock, Ricochet, Seeking Volley, Shatter.
- **Parked:** Snap Draw, Mark, Converge, Chain, Echo, Tracer, Gold Volley. Claude's reading of "only work on": parked means not shown and not running, not deleted. Their tree scaffolding and icons stay in the code, and Snap Draw and Mark keep their built behaviour, all behind `[Tree] <Orb>Orb = false` by default, so each can be brought back with a setting. Chain is not built; the build block stays at version 2 unless Chain's scaffolding was already complete when the scope changed (see `b3-report.md`).
- **What changes in the four:** with Mark and Tracer parked there are no pins, so Seeking Volley has no "pinned creatures lock instantly" rule. With Gold Volley parked, the ghost stagger complement stays at its current value. Ricochet's candidates and Shatter's splinter targets still come from the release's relayed target list.

## Ricochet redefined (the user, 2026-11-16): one orb, ground skip plus creature jump

The user's words: "combine Ricochet Chain, if it hits the ground have it bounce following its direction, then if it hits a enimy ahve it bounce to a different creature".

This replaces the Ricochet section above and the separate Chain orb. One orb, **Ricochet**: Multi side orb, needs Three-Shot, works for a Multi main. There is no Chain orb and no change to the build block.

- **Ground skip.** A ghost or pellet that hits ground, rock or wood skips once and keeps flying along its own direction, like a stone on water. It no longer turns toward a creature. Claude's calls on the details: the new direction is the incoming direction laid along the surface and lifted 10 degrees; it keeps 70% of its speed; a hit steeper than 75 degrees from the surface does not skip; one skip per ghost; a skipped ghost deals its normal share. Looks: white spark, a white kink in the trail, a click (at most 3 per volley).
- **Creature jump.** When a release's ghosts or pellets land on a creature, the hit jumps once to the nearest other creature within 8 m for 50% of the damage that creature took from the release's ghosts (one batched jump per struck creature per release; a jump never jumps again; never players). The real arrow's hit jumps too. The range, share and limits are Claude's calls. Looks: a thin arc in the ghosts' colour to the next creature, a small spark, one soft sound.
- A ghost that reached its creature after a ground skip can still jump.
- Tablet: "Ghosts: skip once off the ground and fly on; a hit jumps to another creature within 8 m for 50%".

## The user's changes of 2026-11-16 (with a sketch of the tree)

The user's text: "1. Quickdraw 4 gains pierce. The line shot passes through every creature on its path instead of stopping at the first. How much each later creature takes is undecided; I suggested 70%, then 50%. 2. Charge Shot 3 loses pierce. 3. Shatter becomes a Charge side orb off Charge Shot 3. It is no longer a bridge. It needs Charge Shot 3 to buy and only works for a Charge main, like every side orb. What it does is unchanged: a charged hit on a creature throws splinters at whatever stands behind it. The Multi half is removed, and there is no Quick half."

The sketch shows three lines, each with one side orb on its third orb: "Reload" (Auto Dock) off Quickdraw 3, Shatter off Charge Shot 3, Ricochet off the third multi-arrow orb; legendaries on top: "pierce" (Quickdraw 4), Holy Arrow, "homing" (Seeking Volley); Quiver Gather to the side. A second, unlabelled loop on Charge Shot 3 is not built.

- **Quickdraw 4** pierces every creature on the line and stops at the first world surface. The first creature takes full damage; Claude's call on the undecided shares: the second takes 70%, the third and every later one 50% (`[Quickdraw4] PierceShares`), at most 8 creatures.
- **Charge Shot 3** no longer pierces. Nothing else about it changes.
- **Shatter** is a Charge side orb off Charge Shot 3, Charge main only. This replaces the Shatter section above where they differ (no bridge, no Multi half).
- **Ricochet** hangs off Burst, the third multi-arrow orb. Claude's reading of the sketch; the user's earlier words were "from the second orb". One-line change either way.
- So the tree for now is: the three lines of four, Quiver Gather, and three side orbs (Auto Dock, Shatter, Ricochet). Everything else stays parked.
