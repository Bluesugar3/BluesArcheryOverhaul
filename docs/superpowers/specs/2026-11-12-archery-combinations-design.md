# Archery combinations: design ("Three Layers, One Crown", revision 2)

> **Amended 2026-11-13:** Rule 1 ("the shot you make decides") is replaced by `2026-11-13-archery-main-path-design.md` (one main path per archer; other lines are complements). Where the two disagree, that document wins.

Project: `attmod\QuickshotRing`, a MelonLoader mod (C#, one DLL installed on both the game client and the dedicated server) for A Township Tale (Unity 2020.3, Mono). Version 0.6.1 is built and installed. Date of the conversation this records: 2026-11-12. Since then, version 0.7.0 (Step 1 of Part 13: build bits and the shot claim on the wire) has been installed and play-tested once, on 2026-11-12; its results are recorded under Step 1 and wherever they correct this document. Version 0.8.0 (Steps 2 and 3) was installed and play-tested on 2026-11-13 (play-test 2); its results are recorded under Steps 2 and 3, and what it changed for later steps under Step 4, Part 9 and Part 12, item 2.

This document follows `2026-11-11-archery-tree-design.md` (called "the first spec" below). The first spec built four separate features. This one turns the Ranged skill tree into three lines of orbs that combine with each other on a single shot. The section "What this replaces and what still stands" says exactly which parts of the first spec are superseded.

**Who decided what.** The user's own decisions are listed in "What the user decided". Everything else is Claude's proposal for meeting those decisions. The user read a summary of the proposal and said "Write the spec"; they have not reviewed every rule or number. Where Claude chose between real alternatives, or changed something the first spec recorded as agreed, the text is marked **(Claude's call)** so the user can overrule it. Those calls are gathered again in "Claude's calls, in one list". Every number that is not in "What the user decided" is a starting value proposed by Claude and is a live setting.

**What has been checked.** The design was checked by reading the decompiled game code (`attmod\_decomp`) and the mod's source. Apart from Step 1's plumbing (play-test 1) and part of Steps 2 and 3 (play-test 2, Part 13), nothing in it has been run. The section "What is unverified" lists the claims the rest of the design leans on.

## What this replaces and what still stands

From the first spec:

- **Part 1, Quiver Gather: still stands.** Two things change. Ownership is read from a different place (see "How it is built", item 1), which fixes a fault where merely trying the orb at the shrine granted it for the rest of the session (found by reading the code, not seen in play). On servers that run the RevampedSkills mod, Quiver Gather stands down (item 12).
- **Part 2, Two-Shot and Three-Shot: replaced.** The two orbs become Volley and Full Volley on a skill of their own **(Claude's call)**, need no Charge orb, appear earlier in the draw and gain two more tiers. What still stands from Part 2: the extra arrows are never real items and cost nothing; they never interact with each other; they do not spin; they all point as if from one spot at the string hand, so the volley starts tight and fans out; each is drawn on its front part only and fades toward the hand; each slides out of the real arrow's tip in about a sixth of a second. Four of the user's Part 2 decisions are changed for owners of specific orbs, each **(Claude's call)** and marked where it occurs:
  1. "An extra disappears the moment it hits a target or a surface." Ricochet lets it bounce once off terrain; Gold Volley with Charge 3, and Seeking Volley with Charge 3, let a ghost pass through one creature.
  2. "Each extra can hurt one thing once." Changed by the same pierce cases.
  3. "No trails." Burst pellets, gold ghosts, Holy Arrow's storm ghosts, seekers and Tracer ghosts carry short trails.
  4. "Firing early fires exactly the arrows that have appeared so far." Snaps and early shots now also carry echoes.
- **Part 3, Holy Arrow: partly replaced.** The beam, its damage, its blast, its stun marker and its look all stand, with two changes: the beam's speed is now taken from a full draw, and Overcharge and Converge change the blast for their owners. The blast now follows the server's PvP setting: where PvP is off it leaves players alone, the shooter included **(the user's decision, 2026-11-12)**; where PvP is on it hurts and stuns them exactly as 0.6.1 does. What else changes: Holy Arrow sits after Charge 3 instead of after Three-Shot, it no longer needs a volley to arm, and a volley that was showing comes back at the impact as forks.
- **Part 4, Quickdraw 4: still stands.** It gains "line windows" later in the draw. The first spec's "single target, no blast, no knockdown" stays true for the plain snap Line. With other orbs owned, a Line can now jump to a marked creature, bend once off terrain, pass through creatures (the Lance) and carry stagger. That widening is **(Claude's call)**.
- **"The two top shots side by side": replaced.** There are now three top shots (ultimates) and an archer may buy only one. A character whose save already owns two keeps both working (Part 5, "Two ultimates on one character").
- **The damage table: the Quickdraw 4 and Holy Arrow rows stand.** The volley row is replaced by rule 4 below.
- **"Answers to builder questions": mostly stand.** A release during Holy Arrow's gathering still fires the full volley. Every client is expected to run the same mod version, but nothing enforces it: the SyncLib build installed here lets a client without the mod connect anyway (see "How it is built", item 3). The RevampedSkills answer ("client-only, do not plan for coexistence") is replaced, because the servers that allow 16 skills run RevampedSkills on the server.
- **Icons, how the user wants to work, and the standing art rules: still stand.**

The first spec also differs from what 0.6.1 actually ships, because of tuning during the builds. This document describes 0.6.1 as built, read from the source: a volley of 5 inner extras at 15% each and 10 outer extras at 6% each (the same totals as the first spec's 3 at 25% and 6 at 10%), Holy Arrow ready at 6.5 seconds, a 7 metre blast with full damage inside 2 metres, and the two pouch gatherer orbs merged into one. The record available to this document does not say who chose each of those values.

## Why

After 0.6.1 the archery tree works, but its pieces do not combine. Research into the game code summed it up: hold time alone picks one of three payoffs (a quick shot, a charged volley, or Holy Arrow), and no release ever combines two lines. An orb bought outside your main line is an alternative shot, not an upgrade.

The user wants the tree to work the way Wynncraft's does. (Wynncraft is a Minecraft game whose ability tree has archetypes, and nodes whose behaviour changes when you own other nodes.) Specifically:

- Quickdraw 1 to 3 look the same as the unmodded game. Each level should add more aggressive flair.
- The charge shot (the game calls it Slowdraw) gives no feedback while you hold it and needed a rework.
- Two-Shot and Three-Shot currently lead straight into Holy Arrow. The user does not want multi-shot to be a direct line into Holy Arrow; multi-shot gets its own ultimate.

Research also found that a volley today only lands on one target at very close range, because the fan opens with distance. Worked out on paper, today's full volley is worth about 2.4 times the arrow at 10 metres and about 2.0 times at 25 and 50 metres.

## What the user decided

These are the user's decisions, condensed from their words. Only these are theirs.

- There are three main lines in the archery tree: multi-arrow, charge shot and quick draw. The tree is Wynncraft-style: archetypes, and nodes that change when you own other nodes. The design is specified all the way down one line and then branches into the neighbouring line.
- The first orb of each line (its root) needs nothing, so anyone can start any line. Their example: someone who owns Holy Arrow buys Quickdraw 1 and 2. How does that interfere?
- The base game allows 8 owned skills in total; modded servers allow 16. An archer may own only one ultimate, but may own every other orb of every line. (Refined on 2026-11-12: nobody can buy a second ultimate, but a save that already owns two keeps both; see below.) On 16 slots someone can own one ultimate plus nearly every other orb. Being rather overpowered is fine if it comes from cool, practical features, not from busted damage.
- The ultimates are Quickdraw 4 (the instant line shot) for the quick draw line, Holy Arrow for the charge line, and Seeking Volley (homing arrows) for the multi-arrow line.
- Multi-shot must not be a direct line into Holy Arrow.
- The charge shot (Slowdraw) gave no feedback and needed a rework.
- Seeking Volley: while you aim at an enemy or a player you can see the lock building. Once locked, a release in roughly that direction makes all the arrows curve to hit. They worry that homing may lag the server.
- The third volley orb adds damage by adding more arrows, like a shotgun burst.
- Combinations must look good together: identifiable pieces, a chain that builds on itself, not just a couple of lines. Every animation is at Holy Arrow's quality: not flat, with real response to the player, each orb worth picking. Not 100% over the top.
- Quickdraw 1 to 3 get progressively more aggressive flair per level. Quickdraw 4 is the only instant one and needs more flair than it has.
- Skill point prices are assessed when building.
- Standing rules: all art matches A Township Tale's own style and reuses the game's assets; minimise lag; no extra networked objects; prefer no new network messages; every number and colour is a live setting so the art can be iterated later; each visual effect is its own small class; use brainstorming for every feature; damage to players from the mod's shots that never fly stays parked.
- Four answers given on 2026-11-12, after reading this document:
  - **Weight's interrupt stays, in its reduced form.** It is used only on actions the game marks cancellable, not on Wyrms until they have been tested, and judged in VR at Step 4. If it looks broken there, Weight falls back to stagger and pierce only.
  - **Under Seeking Volley the real arrow curves into the lock too**, not only the spectral arrows. (How, and the one exception Claude added for snaps, are in Part 5.)
  - **Owning two ultimates.** Both should keep working "because one charges and another is a quick shot ability", but the slots stay locked so nobody can get both. So a character whose save already owns two ultimates keeps both active, and nobody can buy a second one. In existing saves only Quickdraw 4 with Holy Arrow is possible, since Seeking Volley is new.
  - **Holy Arrow's blast follows the server's PvP setting.** Where PvP is on, the blast hurts players at `PlayerDamageShare` (0.5) and stuns them, the shooter included, as 0.6.1 does today. Where PvP is off, it leaves players alone entirely: no damage and no stun, the shooter included. Everything new the mod adds (forks, rods, sparks, seekers, splinters, Lines) still does not damage players.
- Feedback from play-test 2 (2026-11-13), about 0.8.0: "the arrow never glows and doesn't go into the second charge ... there needs to be more feedback in between the charge of Holy Arrow." 0.8.0 switched off the game's whole-arrow glow before the Fill that replaces it existed, so from the thump to Holy Arrow being ready nothing showed. This became a standing rule: **never remove a piece of feedback before its replacement ships** (Part 9, item 15). The user also lost a Holy Arrow charge by letting the string relax for about a third of a second (the log: the pull fell to 0.31 and stayed relaxed for 0.35 seconds, the whole of the shared grace); Brace (Step 4) is the design's answer to that.
- Numbers the user approved earlier and that this design keeps: Quickdraw 4 deals 3 times the arrow; Holy Arrow deals 4 times the charged arrow on a direct hit and 2 times at the centre of its blast, and flies at 5 times a normal arrow's speed; Quiver Gather returns an arrow 15 to 20 seconds after the shot (the user's range; the 15 second default was Claude's call in the first spec).

## Goals

1. Three lines that any archer can start in any order, and that add to each other on one release.
2. One ultimate can be bought per archer, with every other orb ownable. (A character that already owned two before this design keeps both.)
3. A tree that is worth playing at 8 slots (one line and a taste of another) and at 16 slots (one ultimate plus nearly everything).
4. Power that comes from practical features (reliability, control, comfort, versatility), not from larger damage multipliers.
5. Feedback for every orb at Holy Arrow's quality, and combinations that read as identifiable pieces building on each other.
6. Seeking Volley as the user described it, without lagging the server.
7. Everything tunable while the game runs.

## Non-goals

- Damage to players from shots that never fly (ghost arrows, Lines, forks, seekers). It stays parked.
- Names or text shown in game. Orb names exist only in code and in this document.
- Final skill point prices. The default here is 1 point per new orb, with the existing orbs at the game's costs **(Claude's call)**; the user said prices are assessed when building.
- Exempting archery orbs from the 8 or 16 slot cap. The cap is treated as real.
- Changing what the gatherer orbs do.
- Supporting a client that does not have the mod. Such a client must not break the server, and that is all (see "How it is built", item 3).

## Anti-goals (would make it a failure even if it works)

- **Buying into a second line makes your first line worse or different.** The user's own test case is a Holy Arrow owner adding Quickdraw 1 and 2. If their Holy Arrow, their charged shot or the arc of their arrows changes, the design has failed.
- **Busted damage.** A 16-slot archer may be strong, but through features. If any build's single-target damage runs away from the others, or one ultimate becomes the default pick, the design has failed. Guaranteed hits count: reliability can be as "busted" as a multiplier. Seeking Volley, whose real arrow now curves into the lock too, is the build most at risk of this (Part 10).
- **Flat or silent orbs.** Any orb that has no visible, audible or felt response fails the user's bar, as Quickdraw 1 to 3 and Slowdraw do today.
- **A combination that reads as "a couple of lines".** Owning everything must look like one bow that has gained pieces, each still recognisable.
- **Clutter.** Effects that hide the target, glow next to the player's eye, or stack several cues on one moment.
- **Over the top.** No domes, no screen effects, no camera shake.
- **Lag.** No extra networked objects, no heavy particle effects, and homing that costs the server measurably more than the volley already does.
- **New network message types.** Everything rides on messages the game already sends.
- **Art that does not match the game.** Game effects that players already read as something else (teleport, unlock, buffs) must never be spawned for archery.
- **Multi-shot as a road into Holy Arrow.**
- **A second ultimate that can be bought.** The one exception is a save that already owned two ultimates before this design (only Quickdraw 4 with Holy Arrow is possible); it keeps both active **(the user's decision, 2026-11-12)**.
- **Effects the user already loves getting worse.** The Quickdraw 4 column's 0.5 second fade and Holy Arrow's 16 upward bolts stay.

## Constraints and known facts

Facts about the game, from reading the decompiled code. None of these was tested for this design unless the project's `CLAUDE.md` already records it as seen in play.

- Skill logic runs on the server. A client learns skill state only from the status messages the game broadcasts for each skill (charging started, charged, cancelled, consumed) and from its own input. The client's own record of owned orbs is never filled. A client learns which orbs are owned only while a shrine is open (the shrine sends each orb's status to the player using it, and the crystals of bought skills carry their slot ids to nearby clients); outside a shrine it knows nothing about any player's orbs, its own included.
- A skill's level is the number of owned orbs that point at the same skill. The game's bow damage tiers read that level, so a new orb that reused the Quickdraw or Slowdraw skill would move their damage tier. Side orbs and bridges therefore need skill objects of their own.
- The only cap on owned orbs is one number (`MaxBoughtSkills`) that counts every orb in every profession, gatherers included. Each orb counts once whatever it costs. The game code's default is 3 and the game's settings asset overrides it; the base game's value was first inferred to be 8 (an achievement fires at the cap and is named for 8 skills), and play-test 1 read 8 from the game's settings asset on the local server, which runs without RevampedSkills. The same start-up log read the banked-points cap as 3 and the sell refund as 0.8. The value 16 is only the default of the RevampedSkills mod (version 0.4.4), which the 16-slot servers run: it replaces the cap with its own `MaxOwnedSkills` setting, which each server sets anywhere from 1 to 64.
- Skill points are per profession and keep arriving until the profession has banked the game's maximum of unspent points (`MaxFreePoints`; 0 means no cap). Points are therefore a time gate, and the slot cap is the only hard budget.
- An orb can be bought when it is a root or when any one of the orbs it depends on is owned. Dependencies are "any of". An owned orb stays owned when its links change.
- An orb can only be bought after it has been "practised" at the shrine. An orb the shrine shows as "cannot buy" can still be picked up and tried.
- The game cancels every bow skill the instant the string reads at or below its draw threshold, even for one update. The mod's `DrawGrace` patch already delays that in some cases.
- Every bow skill that has its own input (Quickdraw, Slowdraw, Lightbeam, and the new Volley skill) starts its timer at the same moment: the first update in which the string is pulled past 68% of full draw. This document calls that moment "the 68% crossing". Modifier skills (Two-Shot and Three-Shot today) are different: they only get an input once the skill they modify has charged, so they start counting then, one delayed call later.
- A timer that has not finished when the arrow leaves keeps running, and the skill then charges with no arrow on the string. Two facts are known: the game ignores string movement while no arrow is nocked, and the game's own clear-on-release path (which should stop every timer) evidently does not run in time. Why it does not is unknown. 0.6.1 hides this for Holy Arrow by accident. The mod's reset at release (item 4 of "How it is built") is the fix either way.
- Two-Shot and Three-Shot are "modifier" skills: the game adds a modifier only if the skill it modifies is already active, and gives it an input only once that skill's input goes active. That the modified skill is Slowdraw was inferred from log timings, not read from the asset. A volley that works without a Charge orb cannot reuse them.
- A real arrow's flight, for arrows whose prefab has the game's newer precision flag (`useNewPrecision`), is set each physics tick to a constant speed in its current direction (plus a small seeded random wobble), and Unity's gravity then turns it. It is approximately a constant-speed curve, stepped per physics tick, so it depends on the tick length and is not an exact function of time. Arrows without that flag fly plain Unity ballistics. Which kind the game's arrows are is prefab data; play-test 1 saw real arrows ('Rusty Arrow') receive the newer-precision flight message, so at least that arrow has the flag. A simulation of the per-tick scheme over a flat 30 metre shot gives almost the same drop as plain ballistics (about 1.9 metres against 1.8 at 50 metres per second), so the earlier claim that ghosts fall much further than real arrows is withdrawn; how far apart they land has to be measured in play.
- The game clamps networked speeds to 100 metres per second.
- How a flying arrow is moved (read from `Arrow.cs`, `Shooter.cs` and `NetworkRigidbody.cs` in `_decomp`). `Shooter.Shoot` runs on every machine: on the shooter's client from its own input, on the server when the shot message arrives (`isFromNetwork`), and on other clients when the server relays it. Each machine's `Arrow.Shoot` starts the arrow's own flight routine, `ComplexArrowFlight` for arrows with the precision flag or `SimpleArrowFlight` otherwise, registered on the game's fixed-update module, so it runs once per physics tick on that machine (`Arrow.cs` lines 215 to 261). `ComplexArrowFlight` writes the rigidbody's velocity every tick (line 226). The server also sends a flight message that restarts the routine on every client (lines 168 to 175 and 248 to 251). On a client, the shooter's own arrow ignores the server's position updates for about the first 0.2 seconds (`LerpAuthorityFromClient(-0.1, 2)` in `Shooter.cs` line 113), then blends toward them each physics tick at a rate that rises to 0.3 for position and 0.6 for velocity by about 0.8 seconds (`NetworkRigidbody.ClientUpdate`, lines 282 to 308, on its own fixed-update module, line 18); an update more than 2 metres from where the client has the arrow snaps it there (lines 474 and 483). The flight routine stops when the arrow hits, collides or embeds (`Arrow.cs` lines 182 to 185 and 313 to 321).
- The server's PvP switch is `ServerSettings.IsPVPEnabled` (in `Alta.Utilities`), reached on the server as `ServerHandler.Current.ServerConfig.Settings.IsPVPEnabled`; it can change while the server runs. The game applies it in `PlayerHealth.ReceiveDamage` (`PlayerHealth.cs` lines 249 to 257): a hit made with a tool by a player (`ImpactHitWithTool` with `WasByPlayer`) on a player is dropped when PvP is off, and otherwise scaled by the server's `PVPMultiplier` (code default 0.1) for health and `PVPCrippleMultiplier` (code default 0.3) for cripple. Holy Arrow's blast in 0.6.1 is a plain `ImpactHit`, not a tool hit, so that check never sees it, which is why the blast hurts players whatever the setting (`HolyArrow.cs` lines 254 to 258). Every player hit is also scaled by `PlayerDamageTakenMultiplier` (line 258).
- Creature stun in this game is "cripple": a pool on each creature that drains per hit and refills over time. Only five creature kinds have code for being knocked down (Turabada, Gotera, Greater Gotera, Babu and Dragonfly), and their knock-down ignores strength. Wyrms, Spriggulls and others have none. Nothing in the code calls those five stun methods directly, so they are reached through events set up on the creature prefabs; the likely trigger re-arms only after the pool has refilled completely, which may already block a second knock-down.
- The game has a public call, `Agent.CancelCurrentAction`, which has effect only on the server (creatures run their AI only there). It ends the creature's current AI action and makes it choose again at once, possibly the same attack. It does not stop an attack animation that has already been triggered, and it ignores the action's own "cancellable" flag. On a Wyrm that is digging or emerging it can leave the body visible and hittable while flagged as burrowed, because those actions restore their state only when they finish normally.
- The game's own "skill glow" on a charged arrow is said to be one material coloured by two colour-ramp textures, cyan `#00E3FF` and orange `#FF6A00`. That was read from the asset files, not from code; the code only ever sets its progress value. Only the Quickdraw skill opts out of it.
- Every network message has a length prefix and is read by its own reader, so bits added at the end of an existing message should be skipped by a reader that does not expect them. The writer pads each message to a whole byte, and reading past the end returns zeros instead of failing. This was read from code; play-test 1 then showed bits added at the end of the bow status and the shot message arriving intact from client to server, with the game's own messages still reading correctly (Part 13, Step 1).
- The server's physics rate moves between 15 and 45 ticks a second with load. Clients run at their own rate. Anything that depends on step size drifts apart between machines; a path that is a pure function of time does not.
- The game plays controller vibration (haptics) by starting a separate routine per cue; overlapping cues overwrite each other frame by frame. One headset runtime (OpenXR) ignores the frequency value. On every release the game itself plays a one-second fading cue on every hand holding the bow, at the release strength.
- RevampedSkills 0.4.4 also changes Holy Arrow (it keeps the arrow by default and multiplies the beam's damage), the draw threshold check for Holy Arrow, and arrow return. On this machine it is loaded on the client and sits disabled on the local server.
- The SyncLib build installed here (1.1.1-custom) treats a client that lacks a mod's prefabs as optional: it logs a warning, hides those items from that client, and lets it play. It does not close the game.
- The dedicated server's game log shows "Error handling message" errors on Docking and InteractEnd messages. They are the game's own: the headless server has no audio, and `AudioManager.Resolve` throws a null-reference error when `BowString` plays its knock and release sounds there. Server logs from before 0.7.0 have the same errors (20 or more per session), so they are not caused by the mod and are not a sign that the added bits broke anything.

What 0.6.1 does today, from the source:

- `Tree.cs` adds the Quickdraw 4 orb (slot id 2718281828) and the Quiver Gather orb (slot id 1618033988, skill id 1414213562), merges the two pouch gatherers into one unlinked orb, links Holy Arrow after Three-Shot, and sets the draw timers.
- `Main.cs` draws one gold ring (`#FFC733`) that closes on the arrow tip for Quickdraw. It shows nothing between 0.25 seconds and the first ghosts.
- `Volley.cs` shows 5 ghosts at about 2.25 seconds and 10 more from 2.75 seconds, one every 0.12 seconds. It only works once Slowdraw has charged. Each ghost that lands spawns its own light.
- `HolyArrow.cs` arms Holy Arrow at 6.5 seconds only if all 15 ghosts are showing, absorbs them, and fires the beam.
- `Quickdraw4.cs` turns a Quickdraw release into an instant Line, and tells clients by sending the arrow's normal flight message with the impossible speed 999.
- Every line the mod draws uses one flat, untextured material. Research named this as the main reason the effects read as flat.
- `Cfg.cs` resets every setting to its default whenever the settings version is raised.

## Terms

- **Orb**: one purchasable skill at the shrine. **Slot**: one of the 8 (or 16) orbs a character may own.
- **Line**: one of the three chains of orbs. **Root**: a line's first orb. **Side orb**: an orb hanging off one line orb. **Bridge**: an orb that sits between two lines and can be bought from either. **Ultimate**: a line's top orb.
- **Layer**: what a line adds to a shot. Edge is the Quick line's layer, Swarm the Multi line's, Weight the Charge line's.
- **The 68% crossing**: the moment the string passes 68% of full draw. All times in this document are seconds after it unless stated otherwise.
- **Snap**: a release inside the short window just after the crossing (today's Quickdraw shot). **Early shot**: a release after the snap window and before anything has charged. **Held shot**: a release after ghosts are showing. **Charged shot**: a release after the charge has completed at 1.75 seconds.
- **Real arrow**: the arrow item on the string. **Spectral arrow**: any arrow the mod simulates and draws that is never an item. The kinds are: **ghost** (shown round the tip while held), **echo** (a spare ghost that rides a snap or early shot), **barb** or **pellet** (the third volley orb's shot), **splinter** (thrown by Shatter), **needle** (a ghost's look under Quickdraw 4), **seeker** (a spectral arrow that is curving to a target) and **fork** (a ghost returning at a Holy Arrow impact).
- **Arrow power**: the real arrow's own damage rating. **Share**: the fraction of arrow power a spectral arrow deals.
- **Volley tier**: how many of Volley, Full Volley and Burst you own (0 to 3). Seeking Volley does not add a tier.
- **Stagger**: the amount the mod adds to a creature's cripple pool. A stagger of 1.0 equals the centre of a Holy Arrow blast, which knocks creatures down in play (it is the existing `BlastCripple` setting of 0.5). **Interrupt**: cancelling what a creature is doing. **Knock-down**: the game's own stunned state on the species that have one.
- **Beat**: one moment on the draw that has a flash, a sound and a haptic.
- **Line** (capitalised): Quickdraw 4's instant straight shot. **Line window**: a short period later in the draw during which a release is a Line. **Lance**: a Line that passes through creatures.
- **Lock**: Seeking Volley's hold on a target. **Dwell**: the time aim must rest on a candidate before it locks.
- **Mark** or **pin**: a marker the shooter places on a creature. **Rod**: a mark struck by Holy Arrow.
- **Brace**: resting the string partly relaxed without losing the charge. **Loose line**: the draw fraction past which a braced arrow flies at full speed.
- **Build bits**, **shot claim**, **draw record**, **target list**: the four pieces of plumbing, defined in "How it is built".
- **Harmony prefix and postfix**: the mod's way of running its own code before or after one of the game's methods.
- **LOD0**: the most detailed version of a mesh. The game ships each arrowhead at several levels of detail.
- **PvP**: player-versus-player damage, a server setting (`ServerSettings.IsPVPEnabled`; see "Constraints").
- **Steered arrow**: under Seeking Volley, the real arrow of a locked release that curves into the lock (Part 5).

## Part 1: The nine core rules

The whole design in one sentence: every arrow carries every line you own, and one ultimate crowns it.

**Rule 1. Three layers.** Each layer has one colour meaning, one place on the arrow and one feel in the hand.

- Edge (Quick line) is blue-white `#B8E6FF` and lives on the tip: the Ring, the snap, and steadier arrows.
- Swarm (Multi line) is the game's skill cyan `#00E3FF`, turning white as a lock completes: spectral arrows in rings round the tip, and the lock arcs on a target.
- Weight (Charge line) runs from orange `#FF6A00` to gold `#FFC733` to white-hot, on the front 40% of the shaft. It brings knock-down build-up, interrupts and pierce, and never extra damage beyond the game's own charge multiplier.
- Holy gold `#FFDB73` belongs to Holy Arrow alone.
- The colour rule: orange-to-gold always means "charged", cyan-to-white always means "swarm or lock", and blue-white always means "Edge".

**Rule 2. One clock per draw.** See Part 2.

**Rule 3. Every release carries every layer you own, scaled by when you let go.**

- A snap carries Edge's multiplier, plus echoes if you own Volley orbs (1, 2 or 3 ghost arrows at 10% each, one per Volley tier). With Charge 2 or higher it also interrupts the creature it hits.
- An early shot is a normal arrow with the same echoes.
- A held shot carries every ghost that is showing. Once charged, it carries the Charge tier, its stagger and its interrupt.

**Rule 4. Damage.** The real arrow gets one multiplier, set once at release by the moment: the Quick tier for a snap, the Charge tier for a charged shot. Every spectral arrow deals a fixed share of the arrow's power times the Charge multiplier only (1.0 if the shot is not charged). Quick multipliers and Quickdraw 4's 3 times never reach spectral arrows or charged Lines. **(Claude's call)** A steered real arrow under Seeking Volley keeps that same one multiplier, times a server setting for its share on a locked release, 100% by default (Part 5) **(Claude's call)**.

**Rule 5. Weight is the game's own stun.** A creature has a cripple pool that drains per hit and refills. When it empties, a species that can be knocked down goes down. Weight adds to that pool, shows it filling (the Balance ring) and interrupts on heavy hits. A stagger of 1.0 equals a Holy Arrow blast's centre. A creature that the game knocks down takes no knock-down stagger from the mod for 4 seconds. A creature the mod interrupted cannot be interrupted again for 3 seconds. **(Claude's call)**

**Rule 6. One flight model per release.** Spectral arrows leave at the real arrow's speed and follow the same arc model the game gives real arrows (approximately constant speed, or plain ballistics if that is what the game's arrows use; see Part 12, item 9). Ghosts, Converge and the lock all agree with where your arrow actually goes.

**Rule 7. A release looses whatever the bow holds, wherever the string is.** A light pull only makes the real arrow and its ghosts slower. Ghosts that are showing are never swapped for echoes, wherever the string is. An armed Holy Arrow always fires the full beam. **(Claude's call)**

**Rule 8. Nothing you buy makes another moment worse.** No timer moves later, no window shrinks, no arc you have learned changes speed, and no orb takes away what another gives. One case moves a beat, stated here so it is not a surprise: the rack (Burst's beat) moves from 2.0 to 2.25 seconds whenever a line window is open at 2.0 or closed less than 0.12 seconds before it. That is always the case for an archer who owns Quickdraw 4, Burst and a Charge orb (the thump window ends at 2.00, or 2.10 with Snap Draw). With no Charge orb the rack itself opens the window and does not move. Calling this an exception to rule 8 is this document's reading of the approved design, which states the move but not that it is an exception. **(Claude's call)**

**Rule 9. One ultimate can be bought.** Quickdraw 4, Seeking Volley or Holy Arrow. Owning one makes the other two unbuyable, though you can still try them at the shrine, and the one being tried is the one that runs. A character whose save already owns two ultimates (only Quickdraw 4 with Holy Arrow is possible) keeps both active; the moment of release decides which one a shot follows (Part 5, "Two ultimates on one character") **(the user's decision, 2026-11-12)**. Your ultimate re-skins the shapes of the other layers but never their colour meanings.

How the user's points map onto the design: starting from any root is answered by rules 3, 4, 7 and 8, by each orb's "with the other lines" entry and by the worked example in Part 6. Going down one line and branching into the neighbour is the bridges in Part 3. Eight against sixteen slots is Part 7. One ultimate is rule 9, with the two-ultimate case in Part 5. Combinations are Part 8. Homing and lock-on are Seeking Volley in Part 5. The shotgun is Burst in Part 4.

## Part 2: The one clock

Every beat of a draw sits on one timeline, counted from the 68% crossing.

| Seconds after the 68% crossing | Beat |
|---|---|
| 0 | The snap window opens and lasts 0.25 seconds. With Snap Draw it opens earlier, at 55% draw, and ends at whichever is later: 0.35 seconds after the 55% crossing, or 0.25 seconds after the 68% crossing. |
| 0.75 | Five ghosts appear (Volley). |
| 1.0 to 1.45 | The ratchet: ten more ghosts, one every 0.045 seconds (Full Volley). |
| 1.5 | The full-volley ripple. If you own a Charge orb it is folded into the thump instead. |
| 1.75 | The thump: the charge completes (Charge 1 to 3). |
| 2.0 | The rack (Burst). It moves to 2.25 whenever a line window is open at 2.0 or closed less than 0.12 seconds before it (always the case with Quickdraw 4, Burst and a Charge orb; see rule 8). |
| 3.75 | The overcharge (Overcharge). |
| 5.25 to 6.5 | The pour (Holy Arrow gathering). |
| 6.5 | Holy Arrow is armed. |

A beat is a flash, a sound and a haptic. Each beat is at least 0.12 seconds from the next. The ratchet counts as one textured beat.

Compared with 0.6.1: the ghosts no longer wait for the charge (they came at about 2.25 seconds and from 2.75 to 3.83 seconds), the charge still completes at 1.75 seconds, and Holy Arrow's 6.5 seconds and its 1.25 second gathering are unchanged.

## Part 3: The tree (24 orbs)

Every new orb costs 1 point by default, through a per-orb setting **(Claude's call; the user will assess prices when building)**, and every orb takes one slot of the 8 or 16. The 12 existing orbs keep the costs the game gives them **(Claude's call)**: when an orb is sold the game refunds according to that orb's current cost, so changing the cost of an orb a character already owns would refund the wrong number of points on the next sale, on the user's own character first. Every archery orb's cost is logged at start-up for the price pass, and the setting's description warns that changing a cost after the orb has been bought is unsafe. (Today the mod copies a neighbouring slot's cost for its new orbs.)

The lines sit in the order Quick, Multi, Charge. Bridges exist only between neighbours (Quick and Multi, Multi and Charge) and can be bought from either side; there is no bridge between Quick and Charge. That is how an archer goes down one line and branches into the next.

Roots need nothing. Side orbs and bridges are dead ends: nothing depends on them. An ultimate needs the third orb of its own line.

**Which half of a bridge applies** is decided by what the archer owns now, not by which neighbour the bridge was bought through (the game does not record that, and parents can be sold later) **(Claude's call)**. The Quick half applies with any Quick-line orb owned (Quickdraw 1 or higher), the Multi half with Volley tier 1 or higher, the Charge half with Charge 1 or higher. "Bought from the Quick side" below means "with a Quick-line orb owned", and so on.

**Selling an orb that others hang off** needs no code of the mod's own. The game sells each dependent orb with a refund unless one of its other dependencies is still owned, so a bridge survives while either parent remains and is sold with the last one. The builder only has to keep each slot's list of dependents correct on both sides of every link: add each new orb to both of its parents, remove Volley from Slowdraw 1's list and Holy Arrow from Three-Shot's. The build is refreshed after the sale (item 1 of "How it is built").

| Orb | Group | Needs | Slot and skill it uses |
|---|---|---|---|
| Gatherer | Gatherers, unlinked root | nothing | The game's Arrow Gatherer orb, which since 0.6.1 gathers feathers and arrows. Unchanged. |
| Quiver Gather | Gatherers | Gatherer | The mod's existing orb (slot id 1618033988, skill id 1414213562). Unchanged. |
| Quickdraw 1 | Quick line, root | nothing | The game's orb and Quickdraw skill. |
| Quickdraw 2 | Quick line | Quickdraw 1 | The game's orb. |
| Quickdraw 3 | Quick line | Quickdraw 2 | The game's orb. |
| Quickdraw 4 | Quick line, ultimate | Quickdraw 3 | The mod's existing orb (slot id 2718281828, unchanged). It is level 4 of the Quickdraw skill. |
| Snap Draw | Quick side orb | Quickdraw 1 | New orb. |
| Mark | Quick side orb | Quickdraw 2 | New orb. |
| Echo | Bridge between Quick and Multi | Quickdraw 1 or Volley | New orb. |
| Tracer | Bridge between Quick and Multi | Quickdraw 2 or Full Volley | New orb. |
| Volley | Multi line, root | nothing | The game's Two-Shot orb, same slot id so saves keep it, now pointing at the mod's new Volley skill. Its dependency on Slowdraw is removed. |
| Full Volley | Multi line | Volley | The game's Three-Shot orb, same slot id, now level 2 of the Volley skill. |
| Burst (also called Volley 3) | Multi line | Full Volley | New orb. Level 3 of the Volley skill. |
| Seeking Volley | Multi line, ultimate | Burst | New orb. Level 4 of the Volley skill. |
| Converge | Multi side orb | Volley | New orb. |
| Ricochet | Multi side orb | Full Volley | New orb. |
| Gold Volley | Bridge between Multi and Charge | Volley or Charge 1 | New orb. |
| Shatter | Bridge between Multi and Charge | Full Volley or Charge 2 | New orb. |
| Charge 1 | Charge line, root | nothing | The game's Slowdraw 1 orb and Slowdraw skill. |
| Charge 2 | Charge line | Charge 1 | The game's Slowdraw 2 orb. |
| Charge 3 | Charge line | Charge 2 | The game's Slowdraw 3 orb. |
| Holy Arrow | Charge line, ultimate | Charge 3 | The game's Lightbeam orb and skill, re-linked to need Charge 3. In 0.6.1 it needs Three-Shot. |
| Brace | Charge side orb | Charge 1 | New orb. |
| Overcharge | Charge side orb | Charge 2 | New orb. |

That is 12 existing orbs and 12 new ones. The 12 new orbs each need a fixed slot id that never changes once shipped, and the 10 that are not Volley levels (every new orb except Burst and Seeking Volley) need a passive skill object of their own, in the way Quiver Gather has one. The ids themselves are left to the builder; they must avoid every existing Ranged slot id and the old Feather slot id, which the mod still maps to Arrow Gatherer. Every dependency must be a slot that is in the tree, or the shrine fails when it opens.

While a shrine is open, the game spawns one networked orb object per slot, so the 12 new orbs mean 12 more networked objects per open shrine. These are the game's own shrine orbs, not objects the mod creates, but the "no extra networked objects" rule should know about them. The shrine's list of connection lines is capped at 127 entries; 24 orbs with 4 two-parent bridges need about 56.

The orb names "Volley", "Full Volley", "Charge 1" and so on exist only in code and in this document; the game shows no names.

A mock-up of the 24 orbs laid out on the shrine, with the Multi line in the middle column, exists as editor renders at `C:\ATTUnity\shots\tree24_*.png`. Whether the layout fits the real shrine is an open question.

## Part 4: The orbs, one by one

Each orb is described in four facets: what it does on its own, what you see, hear and feel, what it does with the other lines, and what it does with each ultimate. Names in single quotes are the names of the game's own effects, sounds, materials and textures, which the mod plays or copies. None of the sounds has been heard; they were picked by name.

### Quickdraw 1

Quick line (Edge), root, the game's orb.

**On its own.** A snap deals 1.25 times the arrow (the game's value, which sits in the game's data and has not yet been logged; see Step 2). Steady I: every arrow you fire wobbles 25% less (the game's deviance is multiplied by 0.75, stacking with whatever the arrow's fletching does), and your snaps leave 10% faster. Charged and early shots keep their speed, so arcs you have learned never change. **(Claude's call: the speed bonus is for snaps only.)**

**What you see, hear and feel.** The Ring, in Edge blue-white, drawn with a copy of the game's 'Orb Connection Line' laser texture (a core and a halo) instead of a flat line.

- It closes from 7 centimetres to 1.2 centimetres on the tip, timed from your own pull. It is drawn thin below 68% draw, so a snap from a light pull reads as weak.
- At 68% it snaps solid with the game's 'Quick Draw Active' effect and one sharp tick in the bow hand, then drains to 25% across the window.
- After the window it shrinks to the Sight Dot (1.2 centimetres, 30% brightness).
- On release: the game's 'Quick Draw Shoot' plus a 0.1 second flicker of the Streak (see Quickdraw 2), so that Steady is felt.
- On a hit: 3 blue-white needle spikes lasting 0.2 seconds, never drawn smaller than 0.8 degrees of your view, plus a hit-confirm tick.

**With the other lines.** With Multi, snaps carry echoes (heard as "tk-tk"). With Charge, Steady's lower wobble rides your charged shots and their speed is untouched; with Charge 2 or higher, snaps interrupt. With both, one snap is a needle crack, echo ticks and, on a creature winding up, a cancelled attack, all over in 0.2 seconds.

**With each ultimate.** Quickdraw 4: this is the first step, and the Ring later re-forms on line windows. Seeking Volley: the lock dwell is 15% shorter. Holy Arrow: the Ring and Streak stay blue-white, and snap hits leave a 0.3 second two-arc crackle in Holy gold.

### Quickdraw 2

Quick line (Edge).

**On its own.** A snap deals 1.5 times the arrow. Steady II: wobble multiplied by 0.5 on every shot, and snaps 20% faster.

**What you see, hear and feel.** The Double Ring: a thin outer ring, 1.5 times the size, closes onto the inner one; both snap with two 'Click' sounds 40 milliseconds apart. The Streak: snaps leave a 2 centimetre blue-white wind trail using the game's 'WindStreak_edge_m' material, lasting 0.2 seconds. Other shots carry it at 40% strength. Hit spikes: 6.

**With the other lines.** With Multi, the echoes ride inside the Streak, like beads on a line of light. With Charge, charged shots wobble half as much and carry a faint Streak inside their ember trail.

**With each ultimate.** Quickdraw 4: line windows open with the double-ring snap. Seeking Volley: the lock dwell is 25% shorter. Holy Arrow: steadier charged shots between Holy Arrows.

### Quickdraw 3

Quick line (Edge).

**On its own.** A snap deals 1.75 times the arrow. Steady III: wobble multiplied by 0.35, and snaps 30% faster (about 65 metres per second at full draw, under the game's 100 metres per second cap). The Sight: for anything that flies dead straight (Quickdraw 4 Lines and the Holy Arrow beam) a small ring shows where it will stop. It never predicts a normal arrow's arc; aiming those stays the player's skill. **(Claude's call)**

**What you see, hear and feel.** Ring Shatter: a snap release breaks the Ring into 12 forward needle sparks (0.15 seconds) drawn with the game's 'Slashing_Spike' texture, through a copy of the material 'DamageBuffPlus' or 'Slashing_Spikes_Solid_m' ('Slashing_Spike' itself is the name of a texture and of some particle objects, not of an effect or material the mod can look up), with a quiet 'Glass Break'. On a hit: 9 spikes plus a hollow shock ring growing from 0.1 to 0.4 metres in 0.12 seconds. The Sight is a 6 centimetre blue-white ring at 50% brightness, never smaller than 0.8 degrees.

**With the other lines.** With Multi and Converge, a cyan star marks where the ghosts will cross, but only while a straight shot (a Line) is possible. With Charge, wobble is multiplied by 0.35 on charged shots.

**With each ultimate.** Quickdraw 4: this orb is required. The Sight shows from 40% pull whenever your next release could be a Line. Seeking Volley: the lock dwell is 35% shorter. Holy Arrow: once Holy Arrow is armed, the Sight paints a faint gold 7 metre ring where the beam will land.

### Snap Draw

Quick side orb. Needs Quickdraw 1.

**On its own.** Your snap window opens at 55% draw. It ends at whichever is later: 0.35 seconds after the 55% crossing, or 0.25 seconds after the 68% crossing. Buying it can only lengthen the window. Other beats still count from 68%. A snap from a light pull is a slower arrow, because the game sets speed from pull; Steady's snap speed wins some of that back.

**What you see, hear and feel.** The Ring closes faster and shuts at 55%, thin until 68%, then full. Its snap is a brighter 0.05 second bloom with a 'Click'.

**With the other lines.** With Multi, echoes ride light snaps. With Charge and Brace, it sets your loose line to 55%, so the gold collar springs open there.

**With each ultimate.** Quickdraw 4: this is the real payoff. A Line does not fly, so a 55% pull is a full 3 times Line, and every line window lasts 0.35 seconds. Seeking Volley: quick seeking snaps. Holy Arrow: easy snaps between wind-ups.

### Mark

Quick side orb. Needs Quickdraw 2.

**On its own.** Snap hits and Quickdraw 4 Lines pin the creature for 12 seconds. Up to 3 creatures can be pinned at once; a fourth replaces the oldest. Marks go on creatures only, and only the shooter sees them. A second snap on a marked creature pops the mark for 0.2 stagger and an interrupt.

**What you see, hear and feel.** The Pin: a 6 centimetre blue-white ring in three segments where the arrow went in, plus a faint 0.6 metre shaft of light above the creature (the game's light-shaft texture). It sits in the world, never shows through walls, pulses once a second and fades over its last second. Marking plays 'Skill Orb Interact Sounds' and a tick. A pop is 12 sparks with 'Glass Break'.

**With the other lines.** With Multi, a ghost bends toward a mark only when that mark is the creature nearest your aim line, within 3 degrees. Marks never pull a volley off the creature you are aiming at. With Charge, a charged hit on a marked creature uses up the mark for an extra 0.4 stagger: tag, hold, drop.

**With each ultimate.** Quickdraw 4: a Line into a marked creature jumps once to the nearest other mark within 15 metres, at half the Line's damage. Seeking Volley: aiming at a marked creature locks it instantly. Other marks join only the outer ring, and only after you hold your aim between them for 0.3 seconds. Holy Arrow: marks last until your next beam (up to 20 seconds) and become lightning rods. The blast forks to every mark within 20 metres for edge-of-blast damage and a full stun.

### Volley

Multi line (Swarm), root. The old Two-Shot orb, now on its own skill.

**On its own.** 0.75 seconds after the 68% crossing, 5 ghosts slide out round the tip in a 4 centimetre ring. They fly the real arrow's arc at its speed along the existing "tight, then fan out" lines, and deal 15% each (times the charge multiplier if the shot is charged). Early shots carry 1 echo. No Charge orb is needed.

**What you see, hear and feel.** The Ghosts: copies of your arrowhead (the LOD0 mesh only) in a cyan-only copy of the game's skill material, with shafts drawn on the front 40% and fading toward your hand.

- They slide out in 0.17 seconds with 'Skill Orb Interact Sounds' and a train of 2 pulses in the bow hand.
- On release the shafts flare to full length with 'Arrow Woosh Sounds'.
- On landing each gives 3 cyan sparks from a shared emitter. There is one light for the whole volley, at its densest landing, and at most 3 impact sounds.

**With the other lines.** With Quick, snaps carry echoes. With Charge, at the thump the Fill's flare runs out into the ring and turns every ghost orange-gold, which is how you see that they carry the charge. Gold ghosts deal stagger equal to the charge tier's stagger times their share times 0.5.

**With each ultimate.** Quickdraw 4: ghosts become slim blue-white needles, the size of an arrowhead while held and stretched on release. Seeking Volley: this is the first step, and ghost heads swivel toward the lock. Holy Arrow: pale white-gold "storm ghosts" that turn deep gold at the thump, fold in for Holy Arrow and return as forks.

### Full Volley

Multi line (Swarm). The old Three-Shot orb.

**On its own.** Ten more ghosts on a 10 centimetre outer ring, one every 0.045 seconds from 1.0 seconds, all out by 1.45 seconds, dealing 6% each. Early shots carry 2 echoes.

**What you see, hear and feel.** Outer ghosts are 70% as thick and 55% as bright as the inner ones. The ratchet is a soft 'Click' per arrival and a light pulse on every other one. The full-volley ripple at 1.5 seconds brightens all 15 heads. The approved design names the game's 'Dissipate Tool Mesh Effect' for it, but that effect can only wrap separate mesh renderers, which conflicts with drawing the heads in one batch (Part 11). This document does the ripple by animating the skill material's progress value on the batched heads the way that effect does, so the heads stay batched **(Claude's call)**. Whether it reads as "brighten" is checked in Step 3. If you own a Charge orb, the ripple is folded into the thump instead.

**With the other lines.** With Charge: the ratchet, then the thump turning all 15 gold, then the rack, as one wind-up. With Quick: snaps carry 2 echoes.

**With each ultimate.** Quickdraw 4: 15 needles. With no Charge orb, the 1.5 second ripple opens a line window. Seeking Volley: the outer ring is the part that is shared out to a second and third lock. Holy Arrow: the 15 fold in, the last to appear folding first.

### Burst (Volley 3)

Multi line (Swarm). New orb. This is the user's shotgun.

**On its own.** At 2.0 seconds each inner ghost racks 3 barbs: 15 pellets, which makes 30 spectral arrows in all. On release the barbs shed into a fixed daisy pattern round the arrow's line, identical on every machine: 5 pellets at 2.5 degrees and 10 at 5 degrees. Each deals a flat 4% (times the charge multiplier); the spread is the falloff. They vanish at 30 metres. All 15 land inside about 6 metres, and the inner 5 out to about 12 metres. Early shots carry 3 echoes.

**What you see, hear and feel.** The barbs sit on the inner heads, which keeps the centre of the sight clear. They breathe once a second with no jitter. The Rack: 'Dock Hovered Over Sounds', then 'Click' 0.08 seconds later, with a firm double pulse. On release: a muzzle whump. Each inner ghost sheds its barbs in a puff of 12 cyan spark streaks, with one flash, and 'Bow Shoot Sounds' layered on. In flight: 15 centimetre streaks that shrink and dim with range, ending in a puff of specks at 30 metres.

**With the other lines.** Converge chokes the burst to 1 and 2 degrees, centred on the predicted impact, so the inner 5 land out to about 30 metres. (The approved design files this sentence under "with Quick", here and in Converge's own entry, but names Converge, a Multi side orb, as the orb that does it and names no Quick orb. This document reads it as needing Converge only; see "Left to the builder".) With Charge, pellets arrive already gold. At point-blank range a charged burst adds 1.2 times the arrow and its stagger.

**With each ultimate.** Quickdraw 4: in a line window the pellets ride the column as a choked cone; they never become lines. Seeking Volley: pellets seek only inside 15 metres. Holy Arrow: the barbs fold in first and return as 15 short sparks within 3 metres of the blast centre, striking only creatures other than the one the beam hit.

### Converge

Multi side orb. Needs Volley.

**On its own.** Your rings aim to cross at the real arrow's predicted impact point, found by stepping along the same arc the arrow will fly, and limited to between 8 and 40 metres. If your arrow hits, the volley lands on the same creature at any range.

**What you see, hear and feel.** The Claw: at full volley the heads visibly tilt inward at the bow, like a closing claw. When ghosts land as a cluster, one shared cracked-ring burst (the game's 'Particle_RingCracks') appears there with a star twinkle and one light.

**With the other lines.** It chokes Burst (see above). With Charge, the stagger of gold ghosts lands on one creature.

**With each ultimate.** Quickdraw 4: needles meet where the Line stopped. Seeking Volley: Converge becomes target assignment. Unlocked, one ghost goes to each creature in your cone before any creature gets a second (a sweep). Locked, all seekers arrive within 0.05 seconds with one cracked-ring burst and one light. Holy Arrow: the blast centre snaps onto a creature within 2 metres of where the beam lands. It snaps onto creatures only, never onto a player or the shooter.

### Ricochet

Multi side orb. Needs Full Volley.

**On its own.** A ghost, echo or pellet that hits ground, rock or wood bounces once toward a creature within 6 metres of the bounce, inside 120 degrees of its glancing direction. The only candidates are the creatures on the release's target list (sent with the shot, see "How it is built") and the creature your arrow hit, so every screen shows the same bounce. With no candidate it skips and fades in 0.5 seconds. This changes the first spec's "an extra disappears the moment it hits a surface" for owners of this orb. **(Claude's call)**

**What you see, hear and feel.** A white spark, a white kink in the trail and a 'Click'. It is never orange, since orange means charged. At most 3 bounce clicks per volley.

**With the other lines.** With Quick, a mark is preferred only if it is inside the normal 6 metre pick. With Charge, gold ghosts keep their stagger after the bounce.

**With each ultimate.** Quickdraw 4: Refraction. A Line that ends on terrain kinks once into the nearest listed creature within 4 metres, at half damage. Seeking Volley: a seeker blocked by cover bounces once and seeks its target again. Holy Arrow: forks reach 14 metres instead of 10.

### Charge 1

Charge line (Weight), root. The game's Slowdraw 1.

**On its own.** Hold 1.75 seconds past the 68% crossing for a charged shot at 1.25 times the arrow (the game's value). Weight I: the hit adds 0.2 stagger to the creature's cripple pool and interrupts it. The interrupt calls the game's own `CancelCurrentAction` on the server, which ends the creature's current AI action and makes it choose again at once. It does not stop an attack animation that has already been triggered, so a swing already under way still plays; what it can cut short is the rest of the action and whatever would follow it. The mod interrupts only when the creature's current action is marked cancellable by the game, never while a Wyrm digs or emerges, and leaves Wyrms out entirely until they have been tested. The interrupt is kept in this reduced form, judged in VR at Step 4, and dropped in favour of stagger and pierce only if it looks broken there **(the user's decision, 2026-11-12)**. There is a 3 second cooldown per creature. From the 68% crossing on, a dip of the string shorter than 0.35 seconds loses nothing.

**What you see, hear and feel.** The game's own charge glow on the whole arrow is switched off (as the game already does for Quickdraw). The Fill replaces it: a line with a core and a halo fills the front 40% of the shaft over 1.75 seconds, orange at the back to gold at the front, with an ember riding its front. The haptic is a slow swell in the bow hand.

- The Thump at 1.75 seconds: the Fill reaches the tip and flares, with 'Slow Draw Charge End Sounds' and a hard thump in the hand. The flare on the arrowhead is a 'Dissipate Tool Mesh Effect' with an orange-only copy of the skill material; that effect wraps whole mesh parts, and the arrowhead is its own part. The front 40% of the shaft cannot be covered by it, so the shaft's flare is a shell or line the mod builds itself. The game reuses that pooled effect for its own flares, so the mod restores the effect's material after use, or the game's later flares turn orange.
- In flight: an ember trail. On a hit: 'GroundHitPuff' on terrain, 'Charge Hit Use' on a creature.
- The Balance Ring: on a creature you hit, a gold cracked ring at its feet shows how far its cripple pool is drained. Clients read the creature's cripple value as the game syncs it, which is coarse: the game sends it only after it has moved by more than the stat's sync step (0.1 by default, per creature kind) since the last send, so small hits and refilling arrive only once they add up. A 0.2 stagger (a raw 0.1 cripple) may not be sent on its own. The ring therefore eases between received values; whether that is good enough, or the shooter's client must count the mod's own stagger, is checked in Step 4. It fades 2 seconds after your last hit.

**With the other lines.** With Quick, charged shots wobble less and their speed is unchanged. With Multi, the thump turns ghosts gold. The Fill is on the shaft and the ghosts sit round the tip, so they never touch.

**With each ultimate.** Quickdraw 4: the Fill runs frost-white over orange, and the thump is a glass crack that opens a line window. Seeking Volley: the Fill keeps its orange-gold, and seekers carry the charge. Holy Arrow: this is the first step.

### Charge 2

Charge line (Weight).

**On its own.** A charged shot deals 1.75 times the arrow and 0.4 stagger.

**What you see, hear and feel.** The Fill's front becomes a bright ember head. The thump uses 'Power Strike Start Sounds' instead of the lower tier's sound: one sound per beat. On a creature hit: 'Charge Hit Use' plus 'Power Strike Use'. Near the ground, a dust ring grows from 0.3 to 1.2 metres in 0.3 seconds. The Balance Ring takes a visible crack per hit.

**With the other lines.** With Quick, your snaps interrupt too, so a snap cancels a wind-up in a hurry. With Multi, gold ghosts carry 0.4 times their share times 0.5 as stagger.

**With each ultimate.** Quickdraw 4: charged Lines interrupt and stagger. Seeking Volley: a locked gold volley lands as one batched stagger. Holy Arrow: this is the second step.

### Charge 3

Charge line (Weight).

**On its own.** A charged shot deals 2.0 times the arrow and 0.6 stagger, plus Pierce: the real arrow sticks in the first creature, and a gold continuation hits the next creature up to 8 metres behind it for the same damage.

**What you see, hear and feel.** At the thump the head and the front 40% take an orange-only skill shell, so the arrow looks loaded; the half next to your face stays clean. As with the thump's flare, the game's effect covers the head; the front 40% of the shaft is a shell the mod builds. Pierce shows as a gold streak out of the far side with 'Furor Heartcaller Hit Effect' at the exit.

**With the other lines.** With Quick, snaps interrupt. With Multi and Gold Volley, the inner 5 ghosts pierce too.

**With each ultimate.** Quickdraw 4: charged Lines pierce, with the column carrying on through. Seeking Volley: gold seekers punch through the lock into a creature within 4 metres behind it. Holy Arrow: this orb is required.

### Brace

Charge side orb. Needs Charge 1.

**On its own.** After the thump you can ease the string down as far as 40% draw and rest. Everything holds: the charge, the timers still running, the ghosts, a lock and an armed Holy Arrow. Below 40% you have 0.6 seconds of grace. The loose line is 68% (55% with Snap Draw). Draw back past it and the arrow flies at full speed. The game sets arrow speed from the string's position at release, so this needs a change of the mod's own: for a braced arrow released past the loose line, the shooter's client raises the release strength to full in a prefix on `Shooter.Shoot`, before the shot message is built, so the server and spectators receive the same value. The strength field cannot exceed full, so nothing flies faster than a full draw. Letting go anywhere looses everything the bow holds. A braced release below the loose line simply throws a slower real arrow, and its ghosts follow its slower arc. An armed Holy Arrow always fires the full beam. **(Claude's call)**

**What you see, hear and feel.** The Clamp: at the thump a gold collar (a 6 millimetre band) snaps onto the shaft at the back end of the Fill, with 'KeyEnterLock'.

- While braced, the Fill dims to 40% and breathes once a second.
- Past the loose line, the collar springs open with a tick, meaning "ready".
- If the grace runs out, the Fill dims backward from the tip with a low thud. That is the only "you lost it" animation in the mod.

**With the other lines.** With Quick, pulling back again never opens a snap window (the grace holds the Quickdraw input too), so a braced release can never turn your charge into a snap. With Multi, ghosts dim and hold their formation.

**With each ultimate.** Quickdraw 4: after being braced for at least 0.4 seconds, pulling back past the loose line opens a line window. A Line there uses your charge multiplier, stagger and pierce. It is the same Line as the 1.75 second window gives: a way to re-time it, not a better shot. Seeking Volley: the lock and the volley hold while you rest. Holy Arrow: rest through the 6.5 seconds, then release anywhere for the full beam.

### Overcharge

Charge side orb. Needs Charge 2.

**On its own.** Keep holding 2 seconds past the charge, to 3.75 seconds. The shot deals 1.0 stagger to the first creature (a Holy Arrow blast's centre, which is a knock-down for species that can be knocked down) and pierces one more creature (2 with Charge 3). It adds no damage.

**What you see, hear and feel.** From 1.75 seconds the Fill turns white-hot from its back end forward, and the game's 'Hot Effect' embers on the tip ramp up. The Second Thump at 3.75 seconds: a 'Forge Hot' spit of sparks, 'Power Strike Start Sounds' and a heavier thump. On release: 'Power Strike Use Sounds'. On a hit: the small 'Turabada Ground Slam Effect', and the Balance Ring shatters if the creature goes down.

**With the other lines.** With Quick, nothing beyond Steady. With Multi, white-hot ghosts carry 1.0 times their share times 0.5 as stagger.

**With each ultimate.** Quickdraw 4: it opens a Lance window. The Line passes every creature along 30 metres and deals 1.0 stagger to the first. Seeking Volley: the lock takes 1.0 stagger on arrival, once per lock. Holy Arrow: the blast stuns at full strength across all 7 metres; damage still falls off. The full-strength stun applies to creatures only. Where PvP is on, players and the shooter keep today's falloff for both damage and stun; where PvP is off, the blast leaves them alone (Part 5) **(Claude's call, within the user's PvP decision)**.

### Echo

Bridge between Quick and Multi. Needs Quickdraw 1 or Volley.

**On its own.** Snaps and early shots carry twice the echoes (2 per Volley tier, and at least 2), at 8% each. They fly their own paths in a tight 1.5 degree cross, so they can catch what the arrow just missed.

**What you see, hear and feel.** Cyan ghosts at 65% size. The beads split into a cross 3 metres out; each landing is a spark and a tick ("tk-tk-tk").

**With the other lines.** For a Quick main, snaps become burst fire. For a Multi main, quick shots between volleys stay worth taking. With Charge, echoes are uncharged.

**With each ultimate.** Quickdraw 4: echoes are thin instant beads in a cross beside the Line. Seeking Volley: echoes seek the lock, or the creature nearest within 8 degrees. Holy Arrow: echoes are gold sparks, each leaving a one-arc crackle.

### Tracer

Bridge between Quick and Multi. Needs Quickdraw 2 or Full Volley. **(Claude's call: it was reworked into "tracer rounds" so it does something whichever neighbour you buy it from.)**

**On its own.** "Bought from" a side means owning an orb of that line now (see "Which half of a bridge applies" in Part 3). Bought from the Quick side: every snap and early shot carries one tracer echo (10%, uncharged) on the arrow's exact path, 0.05 seconds behind it, so a snap that hits lands twice. Bought from the Multi side: the first ghost to land on each creature pins it, as Mark does (12 seconds, up to 3, seen by the shooter only). With both sides owned, tracer echoes pin as well.

**What you see, hear and feel.** Echoes and ghosts trail a short blue-white Streak tail, so one ghost shows two layers: a cyan head and an Edge tail. Pins look as Mark's do.

**With the other lines.** For a Quick main, a second hit on every good snap. For a Multi main, volleys tag the pack for your snaps and your ultimate. With Charge, gold heads with blue-white tails: all three colours on one ghost.

**With each ultimate.** Quickdraw 4: Lines jump between pins made by ghosts. Seeking Volley: pins made by ghosts lock instantly on your next draw. Holy Arrow: pins made by ghosts become lightning rods.

### Gold Volley

Bridge between Multi and Charge. Needs Volley or Charge 1.

**On its own.** With both lines owned: after the thump, ghosts carry their full share of the stagger instead of half, and with Charge 3 the inner 5 pierce. With Multi only: each ghost hit adds 0.05 stagger. With Charge only: 2 gold ghosts slide out at the thump and fly with the charged shot, at 15% each.

**What you see, hear and feel.** Gold ghosts have orange-ramp heads, gold shafts, an ember trailing each, and a dust puff where each lands. One 'Charge Hit Use' plays when the summed stagger of one release knocks the creature down.

**With the other lines.** For a Charge main, 2 ghosts on every charged shot. For a Multi main, volleys fill the Balance Ring. With both at Charge 3: the arrow's 0.6 plus 15 ghosts' 0.81 gives 1.41 stagger, which is a knock-down. With Quick, snap echoes stay unweighted.

**With each ultimate.** Quickdraw 4: needles with gold cores and full stagger. Seeking Volley: a locked gold volley is the once-per-lock knock-down. Holy Arrow: forks stagger what they strike.

### Shatter

Bridge between Multi and Charge. Needs Full Volley or Charge 2.

**On its own.** The heaviest hit of each release shatters: a charged arrow's hit, or the first ghost to land if you own no Charge orb. It throws 3 splinters, plus 3 per Volley tier (so 3, 6, 9 or 12), onward in a 40 degree cone up to 4 metres, at 5% each (times the charge multiplier). Splinters only strike creatures on the release's target list or right behind the struck one, and never the struck creature itself.

**What you see, hear and feel.** 'Axe Charge Shatter Sounds', the game's 'shards_m' shard sprites, and small splinter ghosts on 0.15 second flights: orange if charged, cyan otherwise.

**With the other lines.** For a Charge main, charged hits clear what stands behind. For a Multi main, the first ghost to land breaks open. With Quick, splinters are uncharged.

**With each ultimate.** Quickdraw 4: a charged Line's end bursts past its target. Seeking Volley: splinters seek listed creatures within 6 metres. Holy Arrow: 6 extra splinter forks, one per creature, never on the direct target.

### Quickdraw 4, Seeking Volley and Holy Arrow

The three ultimates are described in Part 5. None can be bought while another is owned; the other two show as unbuyable but can still be tried. A save that already owns two keeps both (Part 5, "Two ultimates on one character").

### Gatherer

Gatherers, a separate unlinked path, root.

**On its own.** As now: it pulls nearby feathers and arrows into your pouches.

**What you see, hear and feel.** Unchanged: 'Gatherer Use' and 'Gatherer Inhale Sounds'. Which effect and sound the game's Gatherer actually uses is asset data and was not read; the mod does not touch them, so "unchanged" holds whatever they are.

**With the other lines.** It is not part of the bow layers. Spectral arrows leave nothing to gather. It still costs a slot.

**With each ultimate.** Unchanged.

### Quiver Gather

Gatherers. Needs Gatherer.

**On its own.** As now: arrows drawn from your quiver return 15 seconds after the shot. On servers running RevampedSkills it stands down and RevampedSkills' own arrow return takes over, so the two never fight over one arrow. **(Claude's call)**

**What you see, hear and feel.** Unchanged: the pouch-gatherer ghost flying into the quiver.

**With the other lines.** With Charge 3's pierce, the arrow returns from the first creature. A braced slow arrow returns like any other.

**With each ultimate.** Quickdraw 4: the arrow at a Line's end comes home, even from 150 metres away. Seeking Volley: unchanged. Holy Arrow: the beam's arrow stays sacrificed, except where RevampedSkills' "preserve Holy arrow" setting is on; there it follows that setting. **(Claude's call)**

## Part 5: The three ultimates

### Quickdraw 4: the Line

Top of the Quick line. It is level 4 of the Quickdraw skill; its slot id 2718281828 is unchanged.

**Signature, as built.** A snap is an instant, ruler-straight Line at 3 times the arrow, to the first solid thing, up to 150 metres or the edge of the loaded world. Single target. The real arrow ends stuck where the Line stops; on a miss it drops straight down there.

**Line windows on later beats.** A line window opens on:

- the thump (1.75 seconds);
- with no Charge orb, the volley's last beat instead (0.75 seconds, the 1.5 second ripple, or the rack, whichever is the last one you own);
- the overcharge (3.75 seconds);
- a Brace re-pull, after being braced for at least 0.4 seconds.

Each window lasts 0.25 seconds (0.35 with Snap Draw). Releasing inside one makes the main arrow a Line carrying that moment:

- the Charge multiplier (1.0 if nothing is charged, otherwise 1.25 to 2.0 times), never 3 times, so 3 times stays the reward of the cold snap **(Claude's call)**;
- its stagger and interrupt, and pierce with Charge 3;
- with Overcharge, the Lance: through every creature along 30 metres, with 1.0 stagger to the first.

While a window is open nothing else fires: the rack moves to 2.25 seconds whenever a line window is open at 2.0 or closed less than 0.12 seconds before it (see rule 8).

**Ghosts never turn instant.** **(Claude's call)** Needles ride the fading column, guided flat to the Line's end, at their charged shares. Their number is the stage reached at release. Pellets follow as a choked cone. Missing a window never makes the shot worse than it would be without Quickdraw 4.

**Agreement between machines.** The shooter's client claims "Line, window number k" inside the shot itself, together with its own time since the 68% crossing. The server honours the claim if its own clock is within 60 milliseconds of that window, and only honoured Lines get the 999 marker. A Line you see is a Line that hit. The 60 milliseconds is a starting value, and play-test 1 showed it is too tight as it stands: on a local server, the server's time minus the client's claimed time was -78 and -94 milliseconds on snaps and -156 and -231 milliseconds on long holds (four samples). Before windows are built, the tolerance must be sized from more samples, or a measured offset corrected first; Step 7 decides which. A client without the mod does not know the marker: it flies the arrow at 999 metres per second until the server's position or embed message arrives, so on such a client Lines streak off.

**What you see, hear and feel.**

- The snap Line, as built: a blue-white core, 2 spiral strands that turn and open as they fade (the 0.5 second fade is kept as the default), pinpoint spikes and a snap of light.
- New at release: the Ring shatters at the bow into 12 cold sparks that become the start of the Line. 'Glass Break' plays under 'Quick Draw Shoot Sounds'. There is one hard, sharp tick in the bow hand. The fade shortens to 0.3 seconds only when another Line fired within the last 0.8 seconds, so rapid snaps do not stack up.
- Windows use the same grammar as the snap. Half a second before the beat the sight dot breathes open. On the beat the Ring re-forms solid and drains across the window. It vanishes with a soft click when the window closes. The Sight shows from 40% pull whenever the next release could be a Line.
- A charged Line has 4 strands, the inner pair frost-gold, and a 'Charge Hit Use' puff at the end.
- The Lance has a 5 centimetre core for its first 0.06 seconds and a burst of spikes on each creature it passes.
- Needles are slim blue-white heads, with 3 tiny spikes per hit and no lights.

**How it reshapes the other lines.**

- Swarm becomes needles. Held ghosts are slim needles. Echoes are instant beads beside the Line. Converge meets at the Line's end. Ricochet refracts. Tracer pins become jump points.
- Weight becomes the Lance. The thump is a glass crack that opens a window. Charge 3's pierce carries the Line through. Overcharge is the Lance. Brace lets you re-time a charged Line. Gold Volley gives needles gold cores. Shatter bursts past the end.
- Edge, its own layer: Mark and Tracer pins let Lines jump, and the Sight shows where a Line will stop.

### Seeking Volley: the Lock

Top of the Multi line. It is level 4 of the Volley skill.

**Who chooses the target.** The shooter's own client picks the lock from what the shooter sees, and the pick travels inside the shot itself. The server checks it with a looser cone, range and line of sight around the real arrow's release pose, which every machine shares, and falls back to its own pick only if the check fails. The lock you see is the one that gets hit, with no lock messages at all. **(Claude's call)**

**Acquiring a lock.**

- The cone is 5 degrees plus the angle that 0.6 metres makes at the target's distance, measured from your arrow's predicted arc, so aiming high for a far creature still counts.
- The range is 3 to 60 metres, in plain sight. Creatures always win over players. A player can be locked only when no creature is in the cone. A server setting, `LockPlayers`, is `Always` by default because the user asked for players to be lockable; the other values are `PvPOnly` and `Never`.
- The dwell starts at 40% pull and fills only while the bow turns slower than 30 degrees a second. It takes 0.6 seconds at 10 metres, rising to 1.2 seconds at 50 metres. Quickdraw 1, 2 and 3 shorten it by 15%, 25% and 35%. Looking away drains it twice as fast as it fills.
- A candidate that stays twice as close to your aim for 0.2 seconds takes over, at any fill. A swing across a pack does not stick.

**Targets.** One, or up to 3 with marks (see Mark). The lock breaks if you aim more than 15 degrees off for 0.3 seconds, if the target is out of sight for 0.5 seconds, or if it dies, despawns or passes 75 metres. A draw lost beyond the grace breaks it too; Brace holds it.

**A lock outlives the release.** The approved design does not list the release among the things that break a lock, and "once per lock" only means something if a lock can outlast one volley. So the lock belongs to the player, survives the release and the next nock, and breaks only on the conditions above; the aim-off timer keeps running between draws, and the lock also breaks after about 3 seconds with no arrow nocked (a number this document adds; the approved design has none). A server setting (suggested name `LockSurvivesRelease`, default on) lets the user switch to one lock per draw after the Step 6 test. **(Claude's call)**

**Release.** Within 25 degrees of the line to your lock, every spectral arrow curves in, and so does the real arrow **(the user's decision, 2026-11-12)**. Further off, the volley and the arrow fly normally.

**The steered arrow.** On a locked release within 25 degrees, the real arrow curves into the lock on the same kind of curve as the inner ring: it blooms along its own line for 30% of the way, then bends in from its approach angle (how far its own line points off the target, at most 25 degrees) to the seekers' aim point (see "Where a seeker aims"). It flies at its own speed, the one the game gave it at release, so a braced or light release is a slower curve. It ends in a real collision, which the server resolves as it does any arrow: the game's normal arrow damage, with the one multiplier set at release (rule 4), times the real arrow's share on a locked release (a server setting, 100% by default) **(Claude's call)**. Because it is a real collision, armour, hardness and the game's speed-and-angle effectiveness all apply, and Charge 3's pierce and Quiver Gather work on it as on any arrow.

- **A snap's real arrow is not steered, even when a lock exists; its echoes still seek.** **(Claude's call; the user can overrule it.)** Otherwise every snap on a Seeking Volley build becomes auto-aim, and the Quick line stops being a skill. An early shot (after the snap window, before ghosts) is steered; if that turns out to be quick auto-aim in play, the same exception can be widened to "steered only once ghosts are showing" after Step 6.
- If the target dies, despawns or goes behind cover in mid-flight, the steer lets go and the arrow flies on as a normal arrow from where it is **(Claude's call)**. It does not switch targets as the ghosts do. The steer also lets go if the curve's end time passes without a collision.
- The steered arrow follows a curve with no drop, so a far target no longer needs the arrow aimed high once it is locked.
- A locked player is hit by a steered arrow only under the game's own rules for arrows, not the mod's parked player damage. The game drops a player's arrow hit on a player when PvP is off, and otherwise scales it by the server's own `PVPMultiplier` and `PVPCrippleMultiplier` (see "Constraints"). So on a PvP server with `LockPlayers` at its default `Always`, a locked release is an aim-assisted real arrow at another player. That follows from the user's two answers together; `LockPlayers` is the setting to change if it plays badly.

How it is moved (from reading the code; nothing was run; Part 12, item 9b): the game's flight is client-first. The shooter's client launches the arrow and ignores the server's corrections for about its first 0.2 seconds, then blends toward the server's position, and the server flies its own copy and sends that position to everyone. So the steer runs on both the shooter's client and the server, every physics tick, right after the game's own flight routine has set the arrow's velocity, and sets the velocity along the curve. The curve is evaluated at absolute time from the release pose and the claimed target that both sides share, re-aimed each tick at the target's current position. Spectators run no steer: they follow the game's normal network sync of the arrow. The steered speed is the arrow's own, well under the game's 100 metres per second clamp on networked speeds, so the sync carries it intact.

**The price of certainty.** Locked seekers deal 60% of their normal share (a setting). The steered real arrow deals its full damage by default; its share on a locked release is the knob if Seeking Volley dominates (Part 10). A guaranteed knock-down (from Overcharge or Gold Volley) happens at most once per lock. **(Claude's call)** "Guaranteed" means guaranteed arrival, not guaranteed damage: a seeker ends with a short real raycast toward its aim point and its hit goes through the same damage rules as a real arrow (armour and hardness included), so a seeker that arrives on a part it cannot hurt deals nothing. Such arrivals are logged. **(Claude's call)**

**Where a seeker aims.** At the point where the shooter's aim met the creature when the lock completed, stored relative to the creature so it moves with it. For marks, forks and bounces, which have no such point, it is the centre of the creature's collider. Each machine moves the end of the curve with its own view of the creature, so the end points differ slightly between machines; the server's is the one that counts. **(Claude's call)**

**The curve.** Each ghost blooms along its own line for 30% of the way, then bends in from its approach angle (inner ring 20 degrees, outer ring 35, pellets 50), closing like an iris. Its speed is the real arrow's speed. Inner ring, outer ring and pellets land 0.04 seconds apart (all within 0.05 seconds with Converge). Each curve is a smooth four-point curve (a cubic Bezier) evaluated at absolute time, so server and clients agree on its shape; only its end point, which follows the moving creature, differs slightly between machines (see "Where a seeker aims").

- Cover stops a ghost.
- If the target dies in mid-flight, each ghost switches to the nearest listed creature within 5 metres, or lands where the target stood.

**Unlocked.** Each ghost may bend onto a listed creature within 8 degrees of its own path, out to 30 metres, at its full share. The shooter's client sends up to 6 target ids and a 3-bit pick per spectral arrow with the shot, so every screen shows the same curves. Pellets seek only inside 15 metres. Without a lock the real arrow is never steered.

**Players.** You can lock them and see the lock. Player damage from spectral arrows is parked, so seekers reach a player as harmless sparks. On servers with PvP switched off the arrival is muted: no slam and no flash. The steered real arrow is a normal arrow and follows the game's own rules for arrows on players (see "The steered arrow" above).

**What you see, hear and feel.**

While aiming (shooter only):

- Ghost heads swivel toward the target, up to 25 degrees.
- The Lock Arcs, a Swarm piece of their own: 3 arc segments of 100 degrees each on the target. They face you, are never smaller than 1.5 degrees of your view, and close from 1.6 times to 1.05 times the body's size. Their colour runs from cyan `#00E3FF` to white as the dwell fills.
- The Ring stays on your tip for the snap. When the snap window ends it shrinks to the dot while the arcs brighten: a visible hand-over.
- There are ticks at one third and two thirds: a train of 2 pulses and 'Skill Orb Interact Sounds'.

Locked:

- The arcs snap into a white ring with a cracked-ring flash and a star.
- 'Skill Orb Acquire Sounds' and a firm train of pulses.
- A faint tether previews the curve. It is drawn only from about 3 metres out, and only its far half. It shows only while a release would be accepted, which teaches the 25 degree rule.

Lost: the ring breaks into 6 sparks with 'Skill Orb Return Sounds'.

Flight (everyone sees this): the bloom, then curving ghosts with short ribbons (one shared ribbon system using the game's 'WeaponTrail'), cyan, or gold if charged. The real arrow curves in among them on a locked release (other than a snap).

Arrival: a spark per ghost and at most 3 impact sounds, then the lock ring slams shut with 'Orbskill Placed', one light and a patter of 3 ticks.

Spectators see the bloom, the curves, the slam and a faint ring on the target for 0.4 seconds.

**How it reshapes the other lines.**

- Edge sharpens the lock. Quick tiers shorten the dwell; marks lock instantly when aimed at; snap echoes seek; Steady keeps the unsteered real arrow true (snaps, and releases outside 25 degrees or with no lock), so the snap stays a test of aim.
- Weight is the hammer. After the thump, seekers are gold and carry the charge. Their stagger is batched into one hit on arrival. Charge 3 punches 4 metres past the lock. Overcharge or Gold Volley gives one guaranteed knock-down per lock. Brace holds the lock and the volley while you rest. The Fill keeps its orange-gold, so charge and lock never share a colour.

### Holy Arrow: the Storm

Top of the Charge line. The game's Lightbeam orb, re-linked after Charge 3.

**Signature.** Hold to 6.5 seconds. Holy Arrow no longer needs a volley **(Claude's call; the first spec recorded "it can only be fired by absorbing the nine extra arrows" as agreed)**. It arms only on a live draw, meaning the string is held and the arrow on it is the same arrow that was there at the 68% crossing. That check also removes today's stale timer.

**The beam, as built, with one change.** It always leaves at 5 times a full-draw arrow's speed, so a braced or light release still fires the real beam **(Claude's call)**. A direct hit deals 4 times the charged arrow. (In code the beam's damage is 4 times the charge times the beam prefab's own damage rating and modifier; the "4 times" holds only if those come to 1. That is logged in Step 8 and divided out if it is not 1.) The blast deals 2 times at the centre over 7 metres with falloff and knock-down. The taser-style stun marker stays. The arrow is sacrificed, or kept where RevampedSkills' setting says so.

**The blast and players follow the PvP setting** **(the user's decision, 2026-11-12)**. The server reads `ServerSettings.IsPVPEnabled` at each blast (it can change while the server runs). Where PvP is on, players in the blast take `PlayerDamageShare` (0.5) of its damage and its stun, with today's falloff, the shooter included, exactly as 0.6.1 does. Where PvP is off, the blast leaves players alone entirely: no damage and no stun, the shooter included. Since 0.6.1's blast hit is not a tool hit, the game's own `PVPMultiplier` and `PVPCrippleMultiplier` do not apply to it today, and this design keeps it that way so the numbers stay 0.6.1's **(Claude's call)**. The blast's shove on rigidbodies is not damage and is unchanged. Everything else the mod adds round the blast (forks, rods, sparks, splinters) never damages players, whatever the setting.

An armed Holy Arrow release is always the beam and never a snap: the Quickdraw input is held, not re-armed. Released before 6.5 seconds, the bow fires whatever it has built. A release during the fold still fires the full volley.

**Forks (with any Volley orb).** The ghosts that folded in return at the impact as gold forks.

- One fork goes to each creature within 10 metres (14 with Ricochet), nearest first, at most one per creature, and never to the creature the beam hit directly, which already took the 4 times hit. **(Claude's call)**
- Each fork carries that ghost's charged share and stagger. Leftover forks ground out as visuals only.
- Burst's barbs return as sparks within 3 metres, under the same rules.

Forks clear a pack and add nothing against a lone target, so the most a single target can take stays at 8 times arrow power (4 times the charged arrow at Charge 3's 2.0). Forks are picked by the same rule on every machine, like today's stun marker; the damage follows the server.

**Rods (with Mark or Tracer).** Marks last until your next beam, up to 20 seconds. The shot carries your current mark ids, so everyone sees each rod: a long fork to every mark within 20 metres, for edge-of-blast damage and a full stun.

**What you see, hear and feel.**

- As built, and kept: the jagged flickering gold trail, the flash and the haptic in both hands, 3 ground rings, all 16 upward bolts, 2.5 seconds of crackle, the stun marker, and the game's 'Shockwave Impact' effect and 'Huge Explosion' sound.
- The Pour (5.25 to 6.5 seconds), which must clearly not look like a loss. The Fill squeezes forward while brightening, with a rising tone, into a pulsing gold core on the arrowhead. The ghosts fold in, last out first in: barbs, then outer ring, then inner ring. Each leaves a faint outline that bursts back out if you release early. With no ghosts, gold motes stream into the tip, so a pure Charge-line Holy Arrow still gathers.
- Armed: the gold core stays on the head and pulses, with tiny stun-marker arcs crawling over it. You always know Holy Arrow is ready.
- At impact: 0.1 seconds after the flash your ghosts reappear in their two rings 1 metre above the blast and hang for 0.15 seconds. Then each darts out as an aimed gold fork (3 or 4 kinks, 0.2 seconds), on top of the bolts, ending in a short stun-marker burst. Rods are longer, brighter forks; the pin turns gold just before it bursts.

**How it reshapes the other lines.**

- Edge stays blue-white and takes on static. **(Claude's call)** The Ring, Streak and Sight keep Edge's colour. Snap hits leave a gold two-arc crackle. Quickdraw 3's Sight paints the 7 metre blast ring while armed. Marks and Tracer pins become rods.
- Swarm becomes storm ghosts: pale white-gold until the thump, deep gold with ember tails after it, so the thump is still visible. They fold in and return as forks. Converge snaps the blast onto a nearby creature. Echoes are gold sparks.
- Weight is the road. Overcharge gives a full-strength stun across 7 metres (to creatures only; players, where PvP is on, keep today's falloff), Brace rests you through the 6.5 seconds, Gold Volley makes forks stagger, and Shatter adds splinter forks.

### Two ultimates on one character

Nobody can buy a second ultimate, but a character whose save already owns two keeps both active **(the user's decision, 2026-11-12)**: in the user's words, both keep working "because one charges and another is a quick shot ability". Only Quickdraw 4 with Holy Arrow can occur, since Seeking Volley is new. The user's own character is not such a case: play-test 1 showed it owns Quickdraw 1 to 3, Two-Shot, Three-Shot and Slowdraw 1 and no ultimate (it had only tried Quickdraw 4 and Holy Arrow at the shrine). The rule still stands for any save that owns both, and Part 13 says how it is tested. The two never share a moment, so the rule is that the moment of release decides **(Claude's call)**:

- A snap, and a release inside any line window (the thump, the overcharge, or a Brace re-pull), follows Quickdraw 4's rules and look: a Line at 3 times for a cold snap, or at the charge multiplier in a window.
- An armed Holy Arrow release (from 6.5 seconds) always fires the beam. The Quickdraw input is held while armed, as in "Signature" above, so no window can open then.
- Every other release (held, outside a window, before Holy Arrow arms) follows the normal held-shot rules.
- The held ghosts take Holy Arrow's storm-ghost skin, because they fold into Holy Arrow; in a line window they ride the column in that skin rather than as needles.
- Line windows still open on the thump and the overcharge, and the rack still moves to 2.25 seconds when a window is open at 2.0 (rule 8).
- Marks are both: a Line jumps off them, and they last until the next beam (up to 20 seconds) and become lightning rods.

Selling one of the two is a normal sale, with the game's normal refund. Once sold, it cannot be bought back while the other ultimate is owned. Nothing is deleted, sold or switched off automatically.

## Part 6: The worked example, Holy Arrow plus Quickdraw 1 and 2

This is the user's own question: a Holy Arrow owner buys Quickdraw 1 and 2. How does that interfere?

**The build.** Charge 1 to 3, Holy Arrow, Quickdraw 1 and Quickdraw 2. That is 6 orbs, leaving 2 slots on a base-game server.

**Nothing in the Holy Arrow line changes, not even the arc.** Steady's speed bonus applies only to snaps. Charged arrows fly exactly as before and only wobble less (multiplied by 0.5). The Fill, the thump at 1.75 seconds, Holy Arrow at 6.5 seconds, the beam, the blast and the crackle are untouched.

**Moment by moment.**

1. As you pull, the blue-white Ring closes on the tip. At 68% it snaps solid with a tick, drains for 0.25 seconds, then shrinks to the sight dot. From 5.25 seconds the dot steps aside: the tip belongs to the pour.
2. New: release inside the 0.25 seconds for a snap at 1.5 times the arrow. It flies 20% faster, carries a Streak and 6 spikes, and leaves a 0.3 second gold crackle on what it hits. With Charge 2 or 3 owned (as here) it interrupts the creature, a quick answer to a wind-up.
3. Every shot wobbles half as much. Nothing else about your aim changes.

**Does anything interfere?** Checked case by case:

- Time: the snap window is 0 to 0.25 seconds, the charge is at 1.75 seconds and Holy Arrow at 6.5 seconds. Holding through the window costs nothing.
- Damage: there is one multiplier per shot, set once at release. Quick never touches Holy Arrow's numbers.
- Space: the Ring is on the tip only while you pull. The Fill is on the shaft. The pour owns the tip from 5.25 seconds.
- The draw: a dip shorter than 0.35 seconds loses nothing from the 68% crossing on. Every timer resets when the arrow leaves, which removes today's stale Slowdraw and Holy Arrow charges after a quick release. An armed Holy Arrow release is always the beam, never a snap.
- Network: Quickdraw adds 3 status messages per draw, of a kind the game already sends.
- The one real trade-off: letting go early gives up that draw's Holy Arrow.

**Good next picks for the 2 free slots.**

- Quickdraw 3: snaps at 1.75 times, and the Sight paints Holy Arrow's 7 metre blast ring before you release.
- Mark: snap up to three creatures; they stay marked until your next beam (up to 20 seconds), and the beam strikes all of them as lightning rods.
- Brace: rest at 40% through the 6.5 seconds.

## Part 7: Builds at 8 and 16 slots

### The three 16-slot builds, release by release

All three own the same 15 orbs plus one ultimate: Quickdraw 1 to 3, Snap Draw, Mark, Volley, Full Volley, Burst, Converge, Ricochet, Charge 1 to 3, Brace and Overcharge. That is every slot, so these builds own no bridge and no gatherer. Times are seconds after the 68% crossing; the snap window opens at 55%.

Common to all three:

- Wobble is multiplied by 0.35 on every shot, and snaps are 30% faster.
- Snaps interrupt and mark.
- A charged hit on a mark adds 0.4 stagger.
- Brace is available after the thump, and any release looses everything the bow holds.
- The game's knock-down sets 4 seconds of immunity; the mod's interrupt has a 3 second cooldown per creature.

**With Quickdraw 4.**

- From 55% for 0.35 seconds: the Line at 3 times, with 3 uncharged echo beads, an interrupt and a mark. A Line into a marked creature jumps to a second mark within 15 metres; one ending on terrain refracts into a listed creature within 4 metres.
- Until 0.75: an early arrow (1.0 times) with 3 needle echoes.
- 0.75 to 1.75: 5, then 15 needles converging on the predicted impact; the arrow is 1.0 times.
- 1.75, the thump, with a line window to 2.10: a charged Line (2.0 times, 0.6 stagger, pierce) with 15 gold needles riding the column. The rack waits until 2.25.
- 2.25, the rack: 30 arrows. Releases until 3.75 are a charged arrow with a gold volley and a choked burst.
- 3.75, the overcharge, with a Lance window to 4.10: the Lance through every creature along 30 metres, 1.0 stagger to the first, and 30 white-hot needles behind. After 4.10 it is an overcharged volley.
- Braced for at least 0.4 seconds, then pulled back: a charged Line, the same as the thump window's.

**With Seeking Volley.**

- 55%: a snap at 1.75 times with seeking echoes. The snap's real arrow is not steered, so it still has to be aimed. The dwell started at 40% pull and takes 0.39 to 0.78 seconds with Quickdraw 3, depending on distance.
- 0.75: 5 ghosts. Aiming at a mark locks it instantly.
- Before the thump: every ghost showing curves into the lock at 60% share, all arriving within 0.05 seconds (the Converge pincer), and the real arrow curves in with them at its full damage.
- 1.75, the thump: the arrow is 2.0 times (0.6 stagger, pierce); seekers turn gold.
- 2.0, the rack: 30 seekers. Pellets seek inside 15 metres.
- 3.75, the overcharge: the lock takes 1.0 stagger on arrival, once per lock; the arrow pierces 2.
- Unlocked, at any moment: Converge sweeps the pack, one ghost per creature at full share.

**With Holy Arrow.**

- 55%: a snap at 1.75 times with gold echo sparks, a crackle, an interrupt and a mark that waits for your beam.
- 0.75 to 1.75: pale storm ghosts (5, then 15); release for a volley at 1.0 times.
- 1.75, the thump: the ghosts turn deep gold; 2.0 times, pierce.
- 2.0, the rack: 30 arrows.
- 3.75, the overcharge: white-hot ghosts, pierce 2.
- 5.25 to 6.5, the pour: a release here still fires the full overcharged volley.
- 6.5, armed: a pulsing gold core with crawling arcs. The Sight paints the 7 metre ring.
- Release, braced or not: a direct hit at 8 times arrow power; a 7 metre blast at full stun strength on creatures (players are touched only where PvP is on, with today's falloff), centred on a creature within 2 metres; one fork to each other creature within 14 metres; sparks within 3 metres; and rods to every mark within 20 metres.

### Example builds

**Snap Duelist (8 slots, Quickdraw 4).** Quickdraw 1 to 4, Snap Draw, Mark, Volley, Echo. Short pulls and constant snaps. Every snap from 55% is an instant Line at 3 times with 2 echo beads in a cross. Lines mark and then jump between marks. A rare 0.75 second hold opens one window: a Line at 1.0 times plus 5 needles. It feels like a gunslinger with a bow: click, crack, tk-tk, Lines leaping between tagged targets, nothing on screen for long.

**Lancer (8 slots, Quickdraw 4).** Quickdraw 1 to 4, Charge 1, Charge 2, Brace, Overcharge. Snap Lines up close. For big targets: charge, then fire the Line in the 1.75 second window (1.75 times, with an interrupt), or brace, rest and re-time it. Hold to 3.75 seconds for a Lance through every creature along 30 metres. It feels like a crossbow you can carry at rest: frost light on the shaft, a gold collar, then one heavy crack.

**Everything, Quickdraw 4 (16 slots).** The 15 common orbs plus Quickdraw 4. Cold snap Lines at 3 times, a charged piercing Line on the thump with gold needles, the Lance at 3.75 seconds, and a strong converging 30-arrow volley between windows. It feels like a metronome: ring, click, crack, and a swarm of needles arriving down the column behind the Line.

**Pack Hunter (8 slots, Seeking Volley).** Volley, Full Volley, Burst, Seeking Volley, Converge, Charge 1, Charge 2, Gold Volley. Unlocked, Converge sweeps the pack, one ghost per creature at full share. Locked on the leader at the 2.0 second rack: the arrow's 0.4, plus the ghosts' 0.54, plus the pellets' 0.24 inside 15 metres, gives 1.18 stagger, one knock-down per lock. You release roughly at a pack and watch the arrows spread to find every creature, or lock one and fold it.

**Tracker (8 slots, Seeking Volley).** Volley, Full Volley, Burst, Seeking Volley, Quickdraw 1, Quickdraw 2, Mark, Tracer. Snap-mark creatures while volleys pin the first creatures they touch; marks last 12 seconds. The next draw locks every mark you aim between, and the volley splits across them. The dwell is 25% shorter. Tag a herd, then one volley fans out and finds every tag.

**Everything, Seeking Volley (16 slots).** The 15 common orbs plus Seeking Volley. Seeking snaps, instant mark locks, a dwell 35% shorter, a sweep when unlocked, a pincer when locked, gold seekers that punch 4 metres past the lock, and one knock-down per lock. Its seekers hit for 60%, so it trades peak damage for certainty. Nothing you looked at is safe: once locked, your own arrow curves in too, and only the snap still has to be aimed.

**Storm Caller (8 slots, Holy Arrow).** Charge 1 to 3, Holy Arrow, Brace, Overcharge, Volley, Full Volley. Rest on Brace through the wind-up: storm ghosts, the thump, white-hot at 3.75 seconds, the pour, Holy Arrow. The blast stuns at full strength and 15 forks hit 15 different creatures. A slow, comfortable wind-up that ends with lightning picking off everything round the impact.

**Thunder Marksman (8 slots, Holy Arrow).** Charge 1 to 3, Holy Arrow, Quickdraw 1, Quickdraw 2, Mark, Brace. Snap-tag up to 3 dangerous creatures (snaps at 1.5 times that interrupt); the marks wait for your beam. A braced Holy Arrow: the beam takes the main target and rods strike every mark within 20 metres. Tag, tag, tag, then the sky answers all of them at once.

**The user's example exactly (6 slots, Holy Arrow).** Charge 1 to 3, Holy Arrow, Quickdraw 1, Quickdraw 2. Holy Arrow unchanged, charged arcs unchanged, plus a fast snap at 1.5 times that interrupts, and steadier arrows. Your Holy Arrow bow, plus a quick answer when 6.5 seconds is too long.

**Everything, Holy Arrow (16 slots).** The 15 common orbs plus Holy Arrow. Snaps that plant rods; charged converging volleys with a choked burst; then the pour and the beam: 8 times direct, a full-strength 7 metre stun, forks to 14 metres and rods to 20 metres. The single-target ceiling stays 8 times. You decide how much storm to let out: a spark, a volley or the whole sky.

**Ranger (8 slots, no ultimate).** Quickdraw 1, Volley, Full Volley, Charge 1, Echo, Gold Volley, Gatherer, Quiver Gather. Snaps at 1.25 times with 4 echoes, a 15-ghost volley by 1.45 seconds, and a charged shot at 1.25 times whose gold ghosts carry full stagger and interrupt. Arrows come home. Nothing explosive, but every release does something and every piece shows.

## Part 8: How the pieces chain visually

The picture is one bow gaining pieces. Each piece has its own place:

- On the tip: the Ring.
- Round the tip: the ghost rings (4 and 10 centimetres), with Burst's barbs on the inner heads.
- On the front 40% of the shaft: the Fill and the charged shell.
- At the back end of the Fill: the Clamp.
- On a target: the Pin, the lock arcs and the Balance Ring.

Nothing new is drawn on the half of the arrow next to your face, and the game's own whole-arrow charge glow is switched off.

**Edge builds.** Quickdraw 1: the Ring snaps and rides on as the dot. Quickdraw 2: the double ring and the Streak. Quickdraw 3: the Ring shatters forward, and the Sight. Snap Draw: the Ring shuts at 55%. Mark: the Pin. Quickdraw 4: the Ring shatters into the Line and re-forms on every window.

**Swarm builds.** Volley: five cyan ghosts. Full Volley: the ratchet and the ripple. Burst: barbs racked onto the inner heads, then the whump. Converge: the claw closes at the bow, and a cracked-ring burst marks a cluster landing. Ricochet: white kinks. Seeking Volley: heads swivel, cyan arcs close on the target, the ring slams shut.

**Weight builds.** Charge 1: the Fill runs to the tip and thumps, and the Balance Ring appears on what you hit. Charge 2: an ember head, and cracks in the ring. Charge 3: the front shell and a gold exit streak. Brace: the collar. Overcharge: white-hot and a second thump. Holy Arrow: the pour into a gold core, then the storm.

**Where lines meet, one piece acts on another.**

1. The thump recolours the swarm. At 1.75 seconds the Fill's flare runs out into the rings and turns cyan ghosts orange-gold (under Holy Arrow, pale to deep gold). Since orange always means charged, you can see which arrows carry the charge.
2. The Ring carries Quickdraw 4. Under Seeking Volley it hands over to the cyan arcs; under Holy Arrow it keeps Edge's colour and adds gold crackle.
3. Many into one, one into many. The pour folds every ghost into the gold core; the blast throws them back out in their own two rings as forks.
4. The Pin is currency every layer spends. Ghosts bend to it when you aim at it, a charged hit cashes it for stagger, a Line jumps off it, Seeking Volley locks it, and Holy Arrow strikes it. Tracer lets the swarm mint pins too.
5. Two layers on one ghost. Tracer gives a cyan head a blue-white tail; Gold Volley turns the head gold; with both, all three colours ride one arrow.
6. The Balance Ring links Weight to every layer. Every gold ghost, pellet, fork and charged Line visibly cracks the same ring until the creature goes down.

**A release is one gesture.** The leading layer plays its full cue and each other layer adds one accent. **An impact nests:** dust (Weight), cold spikes (Edge) and cyan sparks (Swarm) under one light.

**Each ultimate has one payoff.** The Ring shatters into the Line. The lock ring slams shut when the last seeker lands. The volley reappears above the blast and forks out.

**The hand knows the layer by rhythm.** All of the mod's new haptics play on the bow hand **(Claude's call)**, through one haptic mixer per hand. Edge is one sharp tick, Swarm is a train of 2 or 3 pulses, Weight is a swell and then a thump. Frequency is extra character where the headset supports it. (Holy Arrow's existing ready cue in both hands is kept.) The game itself plays a one-second fading cue on the bow hand at every release, exactly where the mod's release ticks go, and a mixer of the mod's own cues cannot stop it overwriting them. So the mixer also takes in the game's cues, through a prefix on the game's haptic call (`PlayerInput.TriggerHaptic`, or at least the bow string's release curve), and applies the player's own haptic strength setting, which a direct call to the device would skip. **(Claude's call)** Cues during the draw are not affected: the game's per-frame draw cue is on the string hand.

## Part 9: Clutter limits

1. Places, not layers. The pieces live on the tip, round the tip, on the front 40% of the shaft, at the clamp point, and on the target. Nothing glows on the half next to your eye; the game's whole-arrow charge glow is switched off once the Fill replaces it (item 15); Charge 3's shell covers only the head (the game's effect) and the front 40% (a shell the mod builds).
2. A clear sight. Burst's barbs sit on the inner heads (centre clear, no jitter, at most a once-a-second breathe). The Seeking Volley tether starts about 3 metres out and draws only its far half. Needles are arrowhead-size until release. Lock arcs and the Sight frame a target and never cover it.
3. One beat at a time. Beats are at least 0.12 seconds apart, with one flash, one sound and one haptic each. The highest tier's thump sound replaces the lower ones, the ripple folds into the thump when you own a Charge orb, and nothing else fires inside an open Quickdraw 4 window (the rack moves to 2.25 seconds whenever a line window is open at 2.0 or closed less than 0.12 seconds before it).
4. One release cue. The leading layer plays its cue in full; every other layer adds one accent.
5. Lights. At most 1 per shot event and at most 6 of the mod's lights on screen. Spectral hits use shared particle sparks; today every landing ghost lights up.
6. Sound. At most 3 impact sounds and 3 bounce clicks per volley.
7. Frequent shots. Everything after a snap is gone in 0.2 seconds. The Line keeps its 0.5 second fade unless another Line fired within 0.8 seconds.
8. Thinning. Past 15 ghosts on a bow, pellets and the outer ring show heads only. Past 40 live ghosts in view, every other one drops its trail.
9. Other archers are drawn at 60% brightness, with no Ring, no lock building, no tether, no pins and no Balance Ring. You still see their Fill, ghosts, curves and hits.
10. Marks: at most 3, in the world, for the shooter only, on creatures only. The Balance Ring is for the shooter only and fades 2 seconds after your last hit.
11. Haptics: one mixer per hand, which also takes in the game's own release cue, so no overlapping cues fight each other; only every other ratchet ghost pulses.
12. No domes, screen effects or camera shake. Holy Arrow keeps its bolts, and its forks are aimed, not random.
13. No mislabelled game effects. The game's teleport, unlock, buff aura, echolocation dome and sword target-orb effects are never spawned; only textures are borrowed.
14. Everything is a setting, reloaded live, and each effect is its own small class.
15. **Never remove a piece of feedback before its replacement ships (binding, from the user's play-test 2 feedback, 2026-11-13).** Cutting clutter must never leave a stretch of the draw silent. 0.8.0 broke this: its `HideGameGlow` setting (on by default) switched off the game's whole-arrow glow for Slowdraw, Volley and Holy Arrow, but the Fill that replaces it is a Step 4 piece, so from the thump at 1.75 seconds to Holy Arrow being ready at 6.5 seconds the arrow showed nothing and the user could not tell the second charge had happened. So the game's glow stays on (`HideGameGlow` off by default, through the per-key migration of Part 14) until the Fill ships, and the step that ships the Fill is the one that switches the glow off, in the same build. The same applies to every other cue the design replaces: the old cue goes only in the build where the new one arrives.

## Part 10: Power, numbers and guards

**The multipliers stay the game's own, or ones the user approved.** Snaps: 1.25, 1.5 and 1.75 times. Charge: 1.25, 1.75 and 2.0 times. Quickdraw 4: 3 times, on a cold snap only. Holy Arrow: 4 times direct and 2 times at the blast centre, of the charged arrow. One multiplier per shot.

**The only added damage is more arrows.** Each is a fixed share of the arrow times the Charge multiplier (1.0 if uncharged):

- an inner ghost 15%, an outer ghost 6%, a pellet a flat 4%;
- an echo 10% (8% each with the Echo orb), a tracer echo 10%, a splinter 5%;
- locked seekers at 60% of their share.

Edge and Weight add no damage. Weight is the game's own cripple pool plus interrupts and pierce.

Every hit the mod deals by hand already carries the arrowhead's own cripple value times its share, on top of Weight's stagger and outside the 4 second immunity. Whether arrowheads have any is asset data. The mod sets the cripple multiplier to 0 on spectral hits so that all of its stagger goes through the batched Weight hit **(Claude's call)**; the arrowhead's cripple value is logged in Step 2 to show what that removes.

Experience points follow each spectral arrow's share. In the game, experience per hit is the path's experience per damage times the hit's effectiveness, rounded to a whole number per hit, and does not otherwise depend on damage. Today the mod passes full effectiveness, so every ghost hit earns a whole arrow hit's experience. Spectral hits instead pass their share as the effectiveness (which also scales their damage and cripple, so the base damage is set to match) and are added up per creature before rounding, or small shares such as 4% pellets round to nothing. The plain blast hit gives no experience at all, as today.

**Stagger values.** Charge 1: 0.2. Charge 2: 0.4. Charge 3: 0.6. Overcharge: 1.0. A mark popped by a second snap: 0.2. A charged hit that uses up a mark: an extra 0.4. A gold ghost: the tier's stagger times its share times 0.5, or times its full share with Gold Volley. Gold Volley with no Charge orb: 0.05 per ghost hit.

**Expected damage.** These are multiples of arrow power, with Charge 3, against a creature 1 metre wide, with the real arrow on target, for the 16-slot builds (which own Converge and Burst). They are the approved design's arithmetic, not measurements.

| Shot | At 10 m | At 25 m | At 50 m |
|---|---|---|---|
| Today (0.6.1), full volley. The fan misses at range. | about 2.4 | about 2.0 | about 2.0 |
| Quickdraw 4, thump window (Line plus 15 needles riding the column) | 4.7 | 4.7 | 4.7 |
| Quickdraw 4, Lance window (adds a choked burst) | 5.9 | 5.3 | 4.7 |
| Any 16-slot held volley outside windows (Converge on the predicted impact, choked burst) | 5.9 | 5.3 | 4.7 |
| Seeking Volley, locked, any release within 25 degrees of the lock other than a snap (the real arrow curves in, so these hold even when your own aim would have missed) | 4.3 | 3.6 | 3.6 |
| Holy Arrow, direct, on the target. Forks hit only other creatures. | 8.0 | 8.0 | 8.0 |

Copied from the approved design. One cell does not reproduce exactly from the shares above (5.3 at 25 m). The builder recomputes the table in the `Rules.cs` tests before any tuning. The Holy Arrow row also assumes Charge 3 and a beam whose own damage rating comes to 1 (Part 5). The approved design's Seeking Volley row already assumed the real arrow hits; with the steered arrow (the user's decision, 2026-11-12) that assumption now holds for any locked release within 25 degrees, not only for a well-aimed one, so the row with the real arrow missing is gone. The Seeking Volley row assumes the real arrow's share on a locked release is at its default of 100%.

A cold snap is worth 2.05. A Quickdraw 4 snap Line is worth 3.3 (3.5 with the Echo orb).

**Seeking Volley is now the most reliable single-target pick.** Its locked number (4.3, 3.6, 3.6) is below Quickdraw 4's thump window (4.7) and Holy Arrow's 8.0, but those need precise aim or a 6.5 second hold, while a locked Seeking release lands that number with aim up to 25 degrees off and on a moving target. Since the anti-goals count reliability as damage, this is the build to watch. The knob is a server setting for the real arrow's damage share on a locked release, 100% by default **(Claude's call)**; at Charge 3 the real arrow is worth 2.0 times arrow power, so each 10% taken off cuts the locked row by 0.2 (at 60%, matching the locked seekers, the row would read about 3.5, 2.8, 2.8). Step 6 compares Seeking Volley with Quickdraw 4 and Holy Arrow before anything is tuned. Its unlocked sweep stays the best tool against packs, and its real arrow is unsteered when unlocked.

**What the 16-slot archer gets instead of bigger numbers.**

- Versatility: every moment of the draw pays.
- Reliability: low wobble, Converge, marks and seeking (under Seeking Volley the locked real arrow too), so the shares actually land.
- Control: interrupts, the Balance Ring, knock-downs, pierce and full-blast stuns.
- Comfort: Brace for tired arms, and arrows that come home.

**The price.** All 16 slots, with nothing left for any other profession. On 8 slots you get one line plus a taste. Points keep coming until the banked-points cap is reached, so the slot cap and the rule that only one ultimate can be bought are the real budget.

**Guards.**

- 4 seconds of knock-down immunity after any knock-down by the game, read from the creatures' own stun calls. The game's own trigger may already block a second knock-down until the pool has refilled completely, which would make this guard redundant; Step 4 finds out.
- A 3 second interrupt cooldown per creature.
- One guaranteed knock-down per lock.
- Damage (and any cripple left on it) is batched into one hit per creature per physics tick; Weight's stagger is batched into one hit per creature per release. (Both are the approved design's; an earlier draft of this document blurred them.) Because ghosts land over 0.3 to 1.4 seconds, the per-release stagger hit is sent when the release's last spectral arrow has landed. Larger single hits also change reactions that scale with damage, such as a Phantom's knock-back.
- If creatures feel juggled in play, cut Mark's extra 0.4 and Gold Volley's full share first.
- If Seeking Volley dominates single targets in Step 6, lower the real arrow's share on a locked release first.
- A character that kept two ultimates gets no stacking: each release follows one of them, by the moment (Part 5).

## Part 11: Lag budget

The worst case considered: 4 players on 16-slot builds releasing 30-arrow volleys together, about once per player every 2.5 seconds. The costs below are estimates from reading code; none was measured.

**Server.**

- A draw record per bow being drawn (a few numbers), and per-player timing read inside patches that already run on every pull.
- No lock scan at all. The shooter's client picks; the server checks the claim once at release (one cone test and one line-of-sight ray per claimed target, at most 6).
- Per release: up to 30 spectral arrows, each costing one short raycast per physics tick plus one curve evaluation. With 4 volleys in the air that is about 120 rays per tick at 45 ticks a second, roughly 0.25 to 1 millisecond of a 22 millisecond tick, during about 0.3 to 1.4 seconds of flight.
- Homing adds about 1 to 2% on top. Arrow count drives the cost, not homing. This is the answer to the user's worry about homing.
- The steered real arrow costs one curve evaluation and one velocity write per physics tick, for one arrow per locked release, on the server and on the shooter's client only. Its collision is the game's own, so it adds no raycasts.
- Hits are batched per creature per tick: a locked volley is about 3 damage events, which also cuts today's traffic from creature health updates.
- A server-wide cap of 200 live spectral arrows. Above it, each arrow casts every other tick at double length.
- Interrupts and stagger are one call per creature per release.
- Quickdraw 4: 1 to 3 rays per Line. Holy Arrow: one overlap query plus fork picks, once every 6.5 seconds at most. Shatter: at most 12 short rays.

**Network.** No new message types and no networked arrows. The steered real arrow travels on the game's own position sync for that arrow, which it already sends.

- Build bits: about 26 bits added to bow-skill status messages the game already sends. The ultimate part is one flag per ultimate rather than a single choice, so a character that kept two ultimates is described correctly.
- Shot claim: a trailer on the shot message the game already sends. It holds the kind of shot and the window (4 bits), whether the real arrow is steered (1 bit), the shooter's time since the 68% crossing (about 10 to 12 bits of milliseconds), the stage count (5 bits), up to 6 target ids (17 bits each; the game sends entity ids in the range 0 to 100000, which is 17 bits), a 3-bit pick per spectral arrow, and up to 3 mark ids. That is at most about 40 bytes, once per shot. The server relays its checked version to everyone else.
- The "consumed" status the game already sends on charged shots carries corrections back to the shooter.
- Steady's tier rides in the build bits, which clients already cache per player. The approved design put it in the hundredths of the flight speed the game already sends; that is kept only if the Step 1 log shows it arriving exactly (it survives the wire up to about 495 metres per second when rounded, but a flight message exists only for arrows with the newer precision flag, and a later local launch can overwrite it). **(Claude's call)** Play-test 1 showed the hundredths arriving exactly on two shots (5200 and 5456 sent and received) and 'Rusty Arrow' using the newer-precision flight; the overwrite risk is unchanged, so which carrier Steady uses is settled when Steady is built (Step 4).

**Client, your own bow.** The Ring, Fill, Clamp, ghost heads drawn in one batch from the LOD0 mesh, shafts as one mesh, and lock arcs. About 8 to 10 draw calls, and no lights except beat flashes.

**Client, per release seen.** Batched heads, one shared ribbon system, 3 shared particle emitters, and at most 1 light. Ghosts are drawn from their curves every rendered frame (smooth in the headset), not on physics ticks. Clients cast only the final segment of each path, or only for ghosts within 40 metres of the camera, using a cached ignore list so that no memory is allocated per cast.

**Four archers in view.** About 35 to 40 draw calls and at most 6 lights. That is less than one of today's volleys can cost.

**Fixes that pay for it.** Ghost heads from the LOD0 mesh only. No light per landed ghost (`Volley.cs` line 339 today). Pooled bolts and flashes. A cached ignore list in `Hits.Cast`. A map from arrow to bow filled when the arrow is nocked, instead of searching every object in the scene.

**Asset lookups must stay cheap.** Today `Fx.cs` caches only names it found. A name that does not resolve (a typo in the settings, a name a game update changed, or an asset not yet loaded) searches every loaded effect or sound again on every call, and with beats every 0.12 seconds and sparks per ghost that becomes a repeated full search. Misses are therefore cached too and retried at most every few seconds or on a settings reload. Materials are resolved once at load through the fields of known game prefabs (see item 13 of Part 12), never by searching every material in memory.

**Measure.** The server's physics step and the volley simulation's milliseconds per tick during 4-player volleys; the cost of one raycast; how often creature health updates are sent per hit.

## Part 12: How it is built

Everything is decided or checked on the server. No new networked objects or message types. Every item below was checked against the decompiled game and the mod by reading; nothing was run. Game class and method names are given so the builder can find them; names of patches inside the mod (`Volley.Fire`, `DrawGrace` and so on) are today's.

**1. Builds (which orbs each player owns).** The server reads each player's build from the list of owned slots (`PathExperience.UnlockedSlots`) plus the orb currently being tried (`PracticingOrb`). A build can hold two ultimates (item 10). It does not use `GetLevel`, which goes stale after a sale that also removes child orbs.

A sale that removes child orbs leaves three things stale in the game, and the mod fixes each:

- the owned-orb counts: the mod calls the game's `UpdateCounts()`. That call also clears a practice in progress, so it is made only after the practice has ended (the game's own sale ends it first);
- the "can buy more skills" flag, which `UpdateCounts()` does not recompute: the mod recomputes it;
- the child orb's game skill: the game checks its level before the child is removed, so the skill stays registered with the player until they log in again. The mod deregisters any child skill whose level fell to 0.

The build is refreshed in postfixes on the progression manager's `UnlockSlot`, `LockSlot`, `PracticeSlot` and `EndPractice`, and also on `PathExperience.LockSlot`, because sales also arrive through `RemoveFreePoints` and `RemoveSlots` (used for offline players), which bypass the manager's `LockSlot`. `Tree.HasQuiverGather` moves to the same source.

About 26 bits describing the build ride on bow status messages through a postfix on `PickupBasedSkill.SerializeInternal` (the owner is remembered by a prefix on `SendSerializedMessage`). Clients cache them per player; outside an open shrine this is the only way any client knows a player's build. These are the **build bits**. The block is at least 8 bits long (see item 2). They carry up to two ultimates: one flag per ultimate, not a single choice.

**A test-only override** **(Claude's call)**: a server-only debug setting, off by default, lists orb names to treat as owned for one named player. It feeds the same build function, never touches the save, and logs loudly whenever it is active. It lets each step's play-test, and the 16-slot builds, be tried on the local server without the user selling and re-buying orbs on their real character and without enabling RevampedSkills. It is also the main way two kept ultimates are tested, since the user's character owns none (play-test 1). Real buying, practising, selling and migration are still tested on the real character in Step 2.

**2. The shot claim (per-shot agreement).**

- A postfix on `ShootArguments.Serialize` appends, after the game's last field (the release strength): a version number (4 bits), the shot kind and window number, whether the real arrow is steered (Seeking Volley, item 9b), the shooter's time since its 68% crossing (about 10 to 12 bits of milliseconds), the stage count, up to 6 target ids (17 bits each), a 3-bit pick per spectral arrow, and up to 3 mark ids. This is the **shot claim**, and the target ids are the **target list**.
- The shooting client fills it in a handler on `Shooter.PreShoot`, before the shot is sent.
- **How the target list is picked** **(Claude's call)**: one picker in `Rules.cs` for every build, not only Seeking Volley, since Ricochet, Shatter and Converge also act only on listed creatures. It takes the lock first, then marks, then creatures in plain sight within 60 metres ordered by their angle from the predicted arc, out to 25 degrees either side, and stops at 6. Seeking Volley's own cones are narrower tests applied to the same list.
- The server reads the claim in `SyncShoot`, before `Shoot`. It compares the claimed window or stage with its own time since its own crossing at receipt, and honours it within 60 milliseconds. That starting value is too tight as measured: in play-test 1, on a local server, server minus client was -78 and -94 milliseconds on snaps and -156 and -231 milliseconds on long holds, and play-test 2 widened that to -78 to -111 milliseconds on snaps and -156 to -568 milliseconds on long holds (the server's pull lags the client's). The tolerance must be sized from more samples, or a measured offset corrected before comparing; Step 7 decides for windows and stages. (The claimed time also saturates at 8190 milliseconds on the wire.)
- **The echo disagreement (play-test 2, 2026-11-13; fixed in Step 4).** The same lag already matters for echoes. On a very quick early shot the shooter's client saw the 68% crossing (83 milliseconds before release) and flew 2 echoes, while the server, whose view of the pull runs about 80 to 110 milliseconds behind, never saw a crossing and fired none. The shooter saw echoes that dealt nothing. The server decides, so those echoes should never have been drawn (Step 7 asks the same of Lines: "no Line drawn that the server did not fire"). 0.8.0 has no correction for it: the "consumed" status correction below was deferred to Step 7, and an uncharged early shot sends no "consumed" status anyway. Step 4 must make sure the shooter never sees spectral arrows the server will not fire, for example by having the server accept the client's claimed crossing time (the claim already carries the client's milliseconds since its crossing) within a measured tolerance for the echo decision, or by having the client hold its echoes until the server agrees. Which one, and why, is chosen in the Step 4 plan. It checks each target id with a looser cone, range and line of sight around the shot's rotation, falling back to its own pick. It honours the "steered" bit only if the shot is not a snap, the lock target passes that check, and the release is within the 25 degree rule by the same looser margin; otherwise it clears the bit, and its relay carries the cleared bit.
- The server's relay to other clients (which skips the shooter) writes the checked claim, so spectators get it with the shot itself. That also fixes spectators possibly never seeing volleys fly.
- A missing trailer (a client without the mod) is detected with `(stream as StreamReader)?.IsFinished ?? true`, checked before reading any trailer bit. The general stream type has no `IsFinished`; the reader's version is true when fewer than 8 bits remain, and messages are padded to a whole byte. So every trailer, and the build-bits block, must be at least 8 bits long, or it could hide inside the padding and read as absent. As a second guard, version 0 means "absent", since reading past the end returns zeros.
- The "consumed" status, when the shot was charged, echoes corrections to the shooter, whose client dissolves any ghost the server never fired. (Not built in 0.8.0; deferred to Step 7.)
- Only honoured Lines get the 999 marker. `Quickdraw4.Detect` compares the speed rounded to hundredths (`Mathf.RoundToInt(speed * 100)`) with the marker values, never with a plain equality test on the number and never "998 or more". The wire's step for that field is about 0.0099999, not exactly 0.01, so some marker values do not come back exactly.
- Log once that the patch on the struct fires. At each release, log the server's own time since the crossing at receipt, the client's claimed time, and the difference.

**2b. Marks** **(Claude's call)**. The server keeps the authoritative list of each shooter's marks, built from the hits it resolves (it resolves the real arrow's hit itself, and the ghosts and Lines are resolved on the server). The shooter's client keeps a mirror by applying the same rule to the same events it sees: its own arrow's hit, its own ghost simulation, and the 999 Line marker. The claim's mark ids are the shooter's view; the server uses only those that are also on its own list, and relays the result so spectators can draw rods. A disagreement heals itself when marks expire (12 seconds, or 20 under Holy Arrow). Both lists are logged at each release.

**3. The Volley skill.** It is a copy of the Slowdraw skill object (`ScriptableObject.Instantiate` of `BowSlowdraw`) with its own pull settings: threshold 0.68, 0.75 seconds, no timeout.

- `Instantiate` does not copy other assets the skill points at. The pull settings and the charged, executed and cancelled sounds live on a separate settings asset (`BowStringPullbackStructure`), and the game keys each input by that asset. An uncopied one would give Volley and Slowdraw one shared input and timer, and changing its numbers or sounds would retime or silence Slowdraw (today's `Tree.SetTimer` writes that shared asset). So the mod also instantiates Slowdraw's pullback structure, sets 0.68, 0.75 seconds and no timeout on the copy, clears its three sound events, and assigns it to the Volley copy's private `inputStructure` field.
- The skill's own transform events are fields of the skill itself and are cleared on the copy. The copy also inherits Slowdraw's id (`hash`), which is overwritten with the Volley skill's own fixed id (65535 or less, checked to be unused) before registration. Its practice trigger is set to 0. Its requests for the game's whole-arrow glow (`RequestToolEffect` and `ReleaseToolEffect`) are made to do nothing.
- The copy is a bow draw skill, so on a charged release the game sets the real arrow's one-off damage multiplier from the copy's own damage table, which is a deep copy of Slowdraw's (1.25 to 2.5 times). Left alone, Volley levels 1 to 4 would give the real arrow Slowdraw's tiers, and with Slowdraw also charged, whichever skill runs last would overwrite the other's multiplier. The copy's damage table is therefore set to 1.0 at every level. Its release handling is not skipped, because the "consumed" status and practice depend on it. The one-off multiplier is logged on a Volley-only charged shot.
- Nothing may identify Slowdraw by its type any more, since the copy has the same type. Today `Tree.cs` (line 101) and `Volley.cs` (line 188) test the type; Slowdraw is found by reference or by id from the untouched game slot instead. The Two-Shot and Three-Shot slots are found by their ids, since today's test for the modifier type stops working once they point at the copy. Whether RevampedSkills tests types the same way was not checked.
- It is registered in the game's skill table on both sides at mod start, not on first tree load, by inserting it as `Tree.cs` does today. The table's own load must run first: if the table is empty when something is inserted, the game's skills never load. Whether that load works this early, and whether `BowSlowdraw` can be found to copy then, is unknown; the count of the table and the copied skill's name and id are logged at start on both sides.
- The Two-Shot and Three-Shot slots point at it, so saves keep their ghosts. Burst and Seeking Volley are its levels 3 and 4.
- Nothing makes a client without the mod install it. The SyncLib build installed here lets such a client connect anyway (see "Constraints"), so a marker prefab would not help. Such a client ignores the build bits and the shot claim, logs two errors per Volley status (a "Missing hash" line, then a caught null-reference error), sees ordinary ghostless arrows, and sees Quickdraw 4 Lines streak off at 999 metres per second. Whether it stays connected is checked if such a client is available.

**4. The draw record and the resolver.** One **draw record** per bow string holds the crossing time, a snapshot of the build, the beats reached, whether the bow is braced, the claim, and the id of the nocked arrow. `Volley.Fire` (the prefix on `ShootExtraArrows`, which runs first on every machine at every release) launches every spectral part from a layout built from the real arrow alone: the nock point is the tip minus the arrow's forward direction times its length, and "up" is the arrow's own up. Only the real arrow's pose (position, compressed rotation and strength) travels in the shot message; the string position and the bow's up direction that `Volley.Layout` uses today come from each machine's own view of the bow, so the ghosts' directions would differ between machines.

A postfix on `BowString.OnShoot` writes the shot's final multiplier, stagger, interrupt and pierce onto the draw record, and resets every input on the bow. That reset is what removes the stale timers. Nothing has been hit at that moment, so the stagger, interrupt and pierce are applied on the server when the hit happens: in `Hits.Apply` for spectral parts, and for the real arrow in a postfix on `ImpactDetector.OnToolHit` (or `Projectile.OnCollision`), matched by tool. The server handles the real arrow's collision itself. (RevampedSkills also has a prefix on `BowString.OnShoot`; see item 12.) The spectral multiplier comes from the record's charged tier, never from the player's Slowdraw level.

**5. Timing.** Per-player values (Snap Draw's 0.55 threshold and its "later of" window end, Brace's floor) never come from writing the game's settings assets, which are shared by every player and whose values are read-only. Instead `CheckForPullback` and `TimedOut` are replaced by prefixes that do the game's work with the player's own values (calling the game's private helpers through reflection). `TimeoutInput` is a coroutine whose duration is read inside code the compiler generated, so a prefix cannot change a value inside it; the prefix returns false and hands back the mod's own coroutine, which polls the draw record for the player's end time, then calls `TimedOut` and clears the game's handle to it. Polling is needed because Snap Draw's window ends at the later of two times and the second one (0.25 seconds after the 68% crossing) is still in the future when the window opens at 55%. `DrawGrace` covers every input from the 68% crossing (0.35 seconds), plus the brace band while braced, with the Quickdraw input included, so pulling back never re-arms a snap. Quickdraw 4's later windows are the mod's own, opened from the pull the server already sees.

**6. Holy Arrow.** The "volley is full" gate is replaced by the live-draw check. The beam's speed is set from a full-draw speed (in the existing `BeamShot` postfix). An armed release is always the beam. Fork damage uses the arrow's power and damage channels, saved when the beam is created (`BeamCreated`), through a new form of `Hits.Apply` that takes an explicit base damage. Forks follow one rule on every machine. Rods use the mark ids the server checked (item 2b).

The blast reads the PvP switch on the server at each blast, as `ServerHandler.Current.ServerConfig.Settings.IsPVPEnabled` (see "Constraints"). With it off, `Blast` in `HolyArrow.cs` skips every `PlayerHealth` (today it includes them, even a directly hit player: lines 240, 246 and 254 to 258), so players take neither the damage nor the cripple; the shooter is a player like any other. With it on, nothing changes from 0.6.1. The setting and the decision are logged on each blast line. Converge's blast-centre snap and Overcharge's full-strength stun already apply to creatures only.

Forks give proportional experience in the way Part 10 describes. For a fork to earn experience at all, its hit needs an impact tool that belongs to an item; the arrow is destroyed at release, so only the beam's tool is left, and whether the beam has one is unknown (logged in Step 8). The beam's own damage rating is logged with the direct hit's actual damage; if it is not 1, `BeamCreated` divides it out so the direct hit stays 4 times the charged arrow.

**Hits.Apply, as it is today and as it must become.** Today it applies the damage channel, hardness and armour, clears the per-tool hit cooldowns, and deals the hit through the game's hit object, but it differs from a real arrow hit in four ways: it ignores the game's immunity groups, always uses full effectiveness (the game's depends on speed and angle), skips a target that is too hard (the game deals a zero-damage hit there), and leaves the hit's direction unset (so, for example, a Phantom's knock-back gets no direction). The new explicit-base form adds the immunity-group multipliers and passes the share as effectiveness (Part 10). Whether to set the direction is left to the builder.

**7. Weight.**

- Stagger is batched per creature per release into one cripple hit (1.0 equals `BlastCripple` 0.5). Players are skipped. Spectral hits carry no cripple of their own (Part 10).
- Interrupt calls the game's public `Agent.CancelCurrentAction` on the server (it has no effect on clients), with a 3 second cooldown per creature. It is called only when the creature's current action is marked cancellable by the game, never during a Wyrm's dig or emerge, and not on Wyrms at all until they have been tested; it is judged in VR at Step 4 and replaced by stagger and pierce only if it looks broken **(the user's decision, 2026-11-12)**. It ends the AI action, not an attack animation already started (see "Constraints").
- Postfixes on the five `Stun(float)` methods (Turabada, Gotera, GreaterGotera, Babu, Dragonfly) record the time of real knock-downs for the 4 second immunity, and build the list of species that can be knocked down. No code calls those methods directly, so whether the postfixes see every knock-down is checked in Step 4.
- The Balance Ring reads the creature's cripple value on clients as the game syncs it, in steps of at least the stat's sync size (Charge 1 in Part 4). It eases between received values; if that is not good enough, the shooter's client counts the mod's own stagger instead.
- Pierce: the server sees the real arrow's hit on the first creature, then casts on from the hit point, ignoring that creature, and deals the continuation through `Hits.Apply`. Because the arrow is under the shooter's client's control for its first moments, the server may see the hit at a slightly different place than the shooter did; Step 4 compares the two.

**8. Steady.** Wobble goes through the game's own modifier for arrow deviance (`ArrowFlightSettingsDevianceMultiplierModifier` on `Arrow.FlightModifiers`). The snap speed bonus is applied in three places:

- On the server, in a prefix ahead of `Arrow.Shoot` (on `Shooter.Shoot`, or on the arrow's `ShootArrowWithVelocity` or `StartHandlingArrowDeviance`). The server sends the flight message from inside `Arrow.Shoot`, before the shot's other handlers run, so a bonus applied later would miss it.
- On the shooting client, from its own build bits, to both the real arrow and the spectral arrows' speed. Otherwise the shooter launches everything at the unboosted speed a round trip before the server's flight message arrives.
- On every client, the last flight message per arrow is cached and re-applied after a later local launch, in the way `Quickdraw4.Settle` does today. Spectators can receive the flight message before the relayed shot, and the local launch would otherwise overwrite the server's speed.

The tier itself rides in the build bits (Part 11). Both wobble and speed are removed when the arrow lands or is nocked again.

**9. One flight model.** Real arrows that carry the game's newer precision flag are set each physics tick to a constant speed in their current direction, and gravity then turns them (`ArrowFlightSettings.GetVelocity`, applied per tick in the arrow's flight routine). That is approximately a constant-speed curve, stepped per physics tick, with a seeded random wobble drawn each tick. Spectral arrows use the continuous closed form of that curve as an approximation, from the speed the game sends. Arrows without the flag fly plain Unity ballistics; if the game's arrows are of that kind, spectral arrows use plain ballistics instead. The mod logs the arrow prefabs' precision flag, gravity and drag at load before relying on either. Converge and the lock cone step the chosen model ahead from the release pose. How far the ghosts and the real arrow land apart is measured in Step 3.

**9b. Steering the real arrow under Seeking Volley** (the user's decision, 2026-11-12; the mechanism is Claude's proposal from reading the code, and none of it has been run).

- **Where it hooks.** Harmony postfixes on the arrow's own per-tick flight routines, `Arrow.ComplexArrowFlight` and `Arrow.SimpleArrowFlight` (both private, `Arrow.cs` lines 215 to 233), which the game registers on its fixed-update module when the arrow is shot (lines 255 to 261) and deregisters when it hits, collides or embeds (lines 182 to 185, 192 to 194, 313 to 321). A postfix runs right after the game's own velocity write (line 226), once per physics tick, only while the game still considers the arrow in flight, so steering ends by itself at the collision. **(Claude's call.)** The alternative, a per-arrow component the mod adds with its own `FixedUpdate`, was rejected because Unity does not order a component's `FixedUpdate` against the game's update module, so the game's write could land after the mod's on some ticks.
- **What it writes.** For an arrow on the steered list (filled from the honoured claim, keyed by the arrow's entity), the postfix evaluates the curve at the absolute time since release, using the release pose from the shot message and the claimed lock, with the end point moved to the target's current position as each machine sees it. It sets the velocity that carries the arrow from where it is now to the curve's next point in one tick, at the arrow's own speed, and turns the arrow along it, so small errors do not build up. Gravity is switched off on the rigidbody while it is steered, as `HolyArrow.cs` already does for the beam (lines 163 and 185), and switched back on when the steer lets go, so the arrow does not sag between writes.
- **Where it runs.** On the server, which reads the claim in `SyncShoot` before `Shoot`, and on the shooter's client, which knows its own claim. Not on spectators: they follow the server's position sync. Spectators do still run the game's own flight routine locally, because the server's flight message starts it on every client (`Arrow.cs` lines 168 to 175), so a spectator's copy starts straight and is pulled onto the server's curve by the network blending. Whether that looks acceptable is measured in Step 6; if it does not, spectators can run the same steer from the relayed claim, which carries everything needed. **(Claude's call.)**
- **Restarts.** The server's flight message restarts the client's flight routine and resets its flight time (lines 235 to 253). The steer keeps its own state per arrow and reads absolute time from the release, so a restart does not move the curve.
- **When the server clears the bit.** The shooter's client starts steering before the server has checked the claim. If the server clears the "steered" bit (item 2), the shooter's client stops steering once the server's correction arrives; for a charged shot that rides on the "consumed" status (item 2), and otherwise the server's position sync pulls the arrow back. Logged whenever it happens.
- **The clamp.** The steered speed is the arrow's own (about 65 metres per second at most with Steady III on a snap, and snaps are never steered), under the 100 metres per second clamp on networked speeds (`PhysicsSerializationSettings.VelocityMax`, used by `NetworkRigidbody.cs` line 442), so the sync carries it intact.
- **What Step 6 must measure.** Smoothness on the shooter's screen at the authority hand-over: the shooter's client ignores the server's position for about the first 0.2 seconds and then blends toward it (`Shooter.cs` line 113; `NetworkRigidbody.cs` lines 282 to 308 and 363 to 370), so any disagreement between the two curves shows up as a drift from about 0.2 seconds on. Snapping corrections: a server position more than 2 metres from the client's snaps the arrow (`NetworkRigidbody.cs` lines 474 and 483); log every such snap on a steered arrow. Whether the game's flight code overwrites the steer: `NetworkRigidbody.ClientUpdate` also writes the velocity every physics tick on clients (line 307), on a different fixed-update module from the arrow's flight (line 18), and their order is unknown; log the arrow's velocity before and after both for a few ticks on the shooter. Also log, per tick for one steered arrow, the server's and the shooter's positions and the curve's point.

**10. One ultimate can be bought.** Postfixes on both of the game's "is this slot available" methods turn a second ultimate into "cannot buy" (still triable), with a guard prefix on `UnlockSlot` for calls that are not save loading. That stays as it was.

A save that already owns two ultimates keeps both, and both stay active **(the user's decision, 2026-11-12)**. The save load needs no migration code for this: the guard on `UnlockSlot` does not act during save loading, so both slots load as owned. A postfix on the save load (`LoadFrom`) only logs loudly, once the whole save is known, that the character owns two ultimates and which. Both are in the build and in the build bits (item 1). Which one a release follows is decided by the moment of release, by the rules in Part 5 ("Two ultimates on one character"), inside the resolver (item 4). Today the mod already treats a Quickdraw level of 4 or more as owning Quickdraw 4, and Holy Arrow's Lightbeam skill stays unlocked by its slot, so neither needs switching back on.

Selling one of the two is a normal sale with the game's normal refund; the mod does not touch it. After the sale the build is refreshed (item 1), and the sold ultimate shows as "cannot buy" while the other is owned, so it cannot be bought back. There is no dormant state, no priority setting and no special refund.

**11. Practice.** Each passive orb is practised by doing its thing once while it sits in the shrine's practice slot (Mark: mark something; Brace: brace once; Converge: land a converged volley). The mod calls the game's public `MarkSlotPracticed` on the server when the action happens while `PracticingOrb` is that orb's slot; that call also tells the client. `Tree.QuiverPractice` uses the same call today, but on touch rather than on doing anything. Bridge icons get a two-part glyph naming their two lines.

**12. RevampedSkills.** Detect it at load. Quiver Gather stands down. Holy Arrow's arrow follows its `PreserveHolyArrow` setting, and its Holy Arrow damage multiplier is multiplied in, not overwritten.

Methods both mods patch, found by decompiling the client's copy of RevampedSkills 0.4.4 (confirm with a start-up log of each method's patch list once both mods are loaded together): `CheckForPullback`, `DestroyShotArrow`, `ExtraArrowUpdater.CreateExtraArrow` (RevampedSkills sets Holy Arrow's damage in a prefix there and marks every extra arrow, the beam included, as a skill copy in a postfix; the mod reads the multiplier after that prefix) and `BowString.OnShoot` (RevampedSkills records the shot in a prefix). These get explicit Harmony priorities. `Projectile.Shoot` and `HealthObject.DetectImpact` are also patched by RevampedSkills but do not interact: one acts only on Power Cross Slash projectiles, the other only inside one area-attack skill, so the mod's own damage calls are unaffected.

RevampedSkills keeps Holy Arrow from cancelling at pulls of 0.60 or more (0.53 for under 0.2 seconds), with a prefix on `CheckForPullback` that rewrites the pull value, for Holy Arrow's input only. It does not decide when Holy Arrow fires. Its order relative to `DrawGrace` and Brace matters, which is why the priorities are explicit. Brace stays compatible because an armed release is always the beam.

**13. Visual plumbing.**

- Materials are reached through fields of game prefabs the mod already knows, not by searching all materials by name: 'Orb Connection Line' through the line renderer of the 'Skill Orb Connection' pooled effect; 'WindStreak_edge_m' through the trail of 'Quick Draw Shoot' (according to the research); 'Tool Effect Material' through the base material of the game's tool-effect prefab, as `Volley.cs` does today; 'Steel Song Tool Effect Material' through 'Steel Song Mesh Effect'. 'WeaponTrail' is a particle material and is in the dumped list. The other four are not in `loaded_assets.txt`, because that dump lists only pooled effects, sounds and particle materials; their names do appear in the game's asset files. Step 1 extends the dump to all materials and line and trail materials, and every lookup is logged. Play-test 1 found 'Orb Connection Line', 'WindStreak_edge_m', 'Tool Effect Material' and 'WeaponTrail'. 'Steel Song Mesh Effect' has no renderer with a material, so 'Steel Song Tool Effect Material' cannot be reached that way (NOT FOUND).
- 'Orb Connection Line' is coloured by the game only through its tint property, on the shared material, so the mod's copy must reset that tint. Whether its shader also uses the line's own vertex colours is unknown. If it does not, a colour gradient along one line (the Fill's orange-to-gold, the white-hot from the back end) is impossible with this material and each colour needs its own material or per-renderer property block. Checked in Step 4.
- The cyan-only and orange-only skill materials come from swapping the colour ramps on a copy of 'Tool Effect Material'. That is unverified: the ramp property names come from reading the asset files, and the code only sets the material's progress. The fallback was 'Steel Song Tool Effect Material', whose colour properties are also unverified (their names are in the asset files, but no code uses them). Play-test 1 could not reach it through 'Steel Song Mesh Effect' (no renderer with a material there), so the fallback needs another source: another prefab field that holds that material, or a different material altogether. The builder finds one, logs it, and checks it in Step 3 before the ramp swap is relied on. Holy Arrow's pale white-gold and deep gold storm ghosts need a ramp texture built at run time, which is more than a swap.
- 'Hot Effect' can be ramped up only if its prefab has the game's emission control component; logged before Overcharge's visuals are built (Step 4).
- The game's whole-arrow glow is switched off for three skills: Slowdraw, the Volley skill and Lightbeam (the same opt-out the game uses for Quickdraw; for the Volley copy, by the do-nothing `RequestToolEffect` and `ReleaseToolEffect` in item 3). It is switched off only in the build that ships the Fill (Part 9, item 15); 0.8.0 switched it off early, and play-test 2 showed the charge going silent.
- Every pooled effect and sound named in this document has at least one matching entry in `loaded_assets.txt`. Several names match more than one entry ('Click', 'Power Strike Use', 'Turabada Ground Slam'), so the exact entry must be chosen and logged.
- Lookups that miss are cached and retried only now and then (Part 11).
- Added to that: shared particle emitters, batched heads, pooled bolts, and 12 new icons shown to the user on the comparison sheet first.

**14. Settings.** Shipped defaults change, so settings migrate per key. See Part 14.

### Files that change

This is a description of where today's code is affected, not a prescribed layout.

| File | What changes |
|---|---|
| `Rules.cs` | New Unity-free rules: the clock, the snap window with Snap Draw, shares and stagger sums, the approximate constant-speed arc, the seek curve (for ghosts and the steered real arrow), the moment-decides rule for two kept ultimates, window and claim tolerance, the target-list picker, fork and pick rules, and a recomputation of the expected-damage table. The test project grows with it. |
| `Tree.cs` | The 24-orb tree, the Volley skill (with its own copied pullback settings and a flat damage table), re-linking, "only one ultimate can be bought" and the log for saves that own two, migration, practice hooks, 12 new icons, reading builds from owned slots, finding Slowdraw, Two-Shot and Three-Shot by id instead of by type. |
| `Volley.cs` | Ghosts on the Volley skill, the new beats, Burst, one flight model, target lists, batching, the live-arrow cap, drawing from curves, no light per hit. |
| `Quickdraw4.cs` | Line windows, the claim, a marker test on the speed rounded to hundredths, charged Lines, the Lance, jumps and refraction, the Ring shatter. |
| `HolyArrow.cs` | The live-draw check, full-draw beam speed, the pour and the armed core, forks, rods, RevampedSkills settings, the blast reading the server's PvP switch. |
| `Hits.cs` | A cached ignore list, an explicit-base form of `Apply` with immunity groups and the share as effectiveness, share-proportional experience added up per creature, no cripple on spectral hits, per-creature batching. |
| `Fx.cs` | Copied game materials reached through prefab fields, cached misses, shared emitters, pooling, and a home for the haptic mixer (which also takes in the game's own cues). |
| `Cfg.cs` | Per-key settings migration. |
| `Main.cs` | The Ring becomes Edge's piece (blue-white, textured, per tier) and should move to its own class; start-up registration of the Volley skill. |
| `QuiverGather.cs` | Ownership from the build; standing down under RevampedSkills. |

New responsibilities that need homes of their own: the build bits, the shot claim, the draw record and resolver, Weight (stagger, interrupt, immunity, the Balance Ring), Steady, Mark (server list and client mirror), the Seeking Volley lock, the steered real arrow (item 9b), the precedence between two kept ultimates, the test-only build override, the shared-settings checksum, and RevampedSkills detection.

## Part 13: Build order and play-tests

The order below is the design's. After each step the user plays it in VR, and the logs are read. The assistant cannot play the game, so every step must log enough to be judged from `MelonLoader\Latest.log` on both sides. What each test must show is derived here from the rules above; the design itself gives only the order. The design does not place these in a step, so the placements below are suggestions and are left to the builder: Snap Draw, Converge, Ricochet, the one flight model, the tree re-link and the new orbs' slots (suggested: each orb's slot is added in the step that builds it; the re-link in Step 3), and the removal of Holy Arrow's full-volley gate (suggested: Step 3, see below).

Each step starts with the brainstorming pass the user requires for every feature, using the Archery Lab for looks.

**How the tree looks between steps** **(Claude's call)**. All 12 new slot ids and the Volley skill's id are fixed as constants in Step 2, since they must never change once shipped. Each new orb is added to the tree only in the step that makes it work, so no orb can be bought that does nothing. Holy Arrow's re-link to Charge 3 and the replacement of its "volley is full" gate by the live-draw check both happen in Step 3, together with the Volley skill, because that step changes when ghosts exist; re-linking without removing the gate would make Holy Arrow buyable but impossible to arm without a volley. Forks, rods and the pour stay in Step 8.

**Getting the orbs each test needs.** The local server caps at 8 slots (read in play-test 1). The user's character owns 6 archery orbs: Quickdraw 1 to 3, Two-Shot, Three-Shot and Slowdraw 1, and no ultimate (it only tried Quickdraw 4 and Holy Arrow at the shrine). Several tests need builds that do not fit together in 8 slots, or orbs the character does not own. Tests use the test-only build override (item 1 of Part 12) for the orb sets, including every test of an ultimate and of two kept ultimates; buying, practising, selling and migration are tested on the real character in Step 2, except selling an ultimate, which is tried only on a copy of the save or a spare character (Step 2). A save that really owns two ultimates can only be made before Step 2's one-ultimate rule is installed: under 0.7.0, Quickdraw 4 follows Quickdraw 3 and Holy Arrow still follows Three-Shot, so both can be bought, which on the user's character would take its last 2 of 8 slots. Whether to make one, and on which character, is the user's choice; without one, the save-load half of the two-ultimate tests stays untested and is said so. (None was made; Step 2 shipped without one.)

**The user's character since play-test 2 (2026-11-13).** During play-test 2 the user bought Quickdraw 4, sold Three-Shot and Two-Shot (at the game's 80% refund), and bought Slowdraw 2 and 3. It now owns Quickdraw 1 to 4 and Slowdraw 1 to 3: 7 of the local server's 8 slots. It owns no Volley orb, and since it owns Quickdraw 4 it can only try Holy Arrow, not buy it (the one-ultimate rule). The save from before 0.8.0 is in the dated backup folder (`backups/2026-11-13-pre-0.8.0`). From Step 4 on, every test of a Volley orb, of Holy Arrow owned, of Brace, Overcharge or Steady, or of any set that does not fit in the one free slot uses the test-only build override (the server file's `[TestBuild]` `Player` and `Orbs`, which grants orbs without buying them) as the main way to try them. Weight's tests also need creatures to hit, so each test script says where to find them near the Archers Refuge shrine, or what to do if none are near.

**Before every deploy.** The project is not under version control. Copy the server's `Save` folder, both `QuickshotRing.cfg` files and the previous DLL into a dated folder, so a bad step can be undone. **(Claude's call)**

**Step 1. Build bits and the shot claim on the wire. Done: version 0.7.0, play-tested 2026-11-12** (results below). Nothing visible changes. The test must show: the log line that the patch on the shot message fires; the added bits arriving intact on a bow status and on the shot message, including the server's relay to a second client; the game's own messages still reading correctly with the bits present; a message without a trailer read as "absent"; the server's time since the crossing at receipt, the client's claimed time and their difference at each release; and the start-up log of the slot cap, the banked-points cap and the sell refund percentage. Most of the design depends on this step. If the bits do not survive, stop and rethink before building anything else. A second client is needed for the relay; if one is not available, say so and treat the relay as untested.

Must also log in Step 1: on a client, the received flight speed for an ordinary shot (if no flight message arrives, the arrow has no newer-precision flight and the speed cannot carry anything) and, for each Quickdraw 4 marker value, the value as received and rounded to hundredths; the arrow prefabs' precision flag, gravity and drag; the slot cap read from the game's settings on a server without RevampedSkills; the list of methods RevampedSkills patches (by decompiling its DLL now, and by logging each method's patch list whenever both mods are loaded together); the asset dump extended to all materials and line and trail materials, with each named lookup's result, and on each client at first draw the list of materials it resolved; and a checksum of the shared settings on each side (Part 14).

**Step 1 results (play-test 1, 2026-11-12, one client and the local server).** The wire works. The patch on the shot message fired on both sides; every build bit and every claim arrived intact (54 build-bit reads, all correct, across Quickdraw, Slowdraw and Lightbeam statuses; claims and release strength identical on both sides), and the game's own messages still read correctly. The shared-settings checksums matched. Flight speeds survived in hundredths (5200 and 5456 sent and received), the 999 marker came back as 999, and real arrows ('Rusty Arrow') use the newer-precision flight. The start-up log read a slot cap of 8, a banked-points cap of 3 and a sell refund of 0.8; RevampedSkills was not loaded. The user's character owns Quickdraw 1 to 3, Two-Shot, Three-Shot and Slowdraw 1, and no ultimate. Release timing, server minus client: -78 and -94 milliseconds on snaps, -156 and -231 milliseconds on long holds, so the 60 millisecond tolerance is too tight (Part 12, item 2; Step 7 decides). One early shot after re-grabbing the bow claimed 10961 milliseconds against the server's 682, which confirms the client's draw clock is not reset on a new grab (fixed in Step 2). Of the named materials, 'Steel Song Mesh Effect' has no material (Part 12, item 13). The "Error handling message" errors in the server's game log are the game's own (see "Constraints"). Nothing visible changed, which is what this step promised, but the user expected to see something, and asked that each play-test be handed to them as in-game actions (what to buy or try, draw, hold, release and aim at) with what each should look, sound and feel like and what counts as broken, with the log lines Claude checks kept in a separate list.

What stayed untested, and where it is re-checked:

- The server's relay to a second client (the claim and the build bits reaching a spectator): no second client was available. Re-checked in Step 2 (the build bits a second client receives) and Step 3 (a spectator seeing the volley fly, the burst pattern on a second client).
- A client without the mod: none was run. Re-checked in Step 3, where its errors and whether it stays connected are logged if such a client is available.
- A message without a trailer read as "absent" (switching off the client's claims or the server's build bits for one shot): not tried. Re-tried in Step 2, since it needs only a settings switch.
- Quiver Gather: not owned, not tried. Step 2 already tests it.

**Step 2. Draw record, resolver, timer reset, the one-ultimate rule and two kept ultimates, migration, experience and per-hit fixes. Done: version 0.8.0 (built together with Step 3), play-tested 2026-11-13** (results after Step 3). The test must show: no stale charge after a quick release (today the log shows Holy Arrow charging 6.5 seconds after a crossing with no arrow on the string); the client's draw clock reset on a new grab, so an early shot after re-grabbing the bow claims about the same time as the server measures (play-test 1 confirmed the bug: client 10961 milliseconds against the server's 682, because the clock was not reset when the bow was grabbed again); the user's own character (Quickdraw 1 to 3, Two-Shot, Three-Shot, Slowdraw 1, no ultimate) loading with its 6 orbs still owned; with the test-only build override giving the user both Quickdraw 4 and Holy Arrow, a snap firing a Line at 3 times and a release after 6.5 seconds firing the beam, with the resolver logging which ultimate each release followed; if a save that really owns both exists (Part 13, "Getting the orbs each test needs"), that save loading with both still owned and both active and the loud log line naming the two, and otherwise that check reported as untested; both ultimates set in the build bits a second client receives (if no second client is available, the relay stays untested, as in Step 1); a message without a trailer read as "absent" (Step 1 did not try it); a second ultimate showing as unbuyable at the shrine while still being triable, with the tried one running; selling one of two ultimates refunding normally and then showing it as unbuyable while the other is owned. That sale is not tried on the user's real character (which owns no ultimate today), because a sold ultimate can never be bought back there while the other is owned; it is tried on a copy of the save or a spare character, and only with the user's go-ahead **(Claude's call)**; trying Quiver Gather no longer granting it after the try ends; a sale that removes a child orb leaving the build, the "can buy" flag and the child's skill correct without a relog; ghosts of an uncharged shot dealing their plain share (multiplier 1.0 in the log); and hits arriving batched per creature.

Must also log in Step 2: around a quick release on the server, when the game's own clear-on-release runs (`HeldPickupHandler.Clear` and the pullback input's `Reset`) relative to the shot, which is the unknown cause of the stale timer; the game's Quickdraw damage multiplier for levels 1 to 3, to confirm 1.25, 1.5 and 1.75; the arrowhead's own cripple value (extend `Hits.Describe`); the Ranged experience gained per ghost hit; the number of creature health updates the game sends per volley; and every archery orb's cost.

**Step 3. The Volley skill and Burst** (suggested: also the one flight model, Converge and Ricochet, and Holy Arrow's re-link and live-draw gate). **Done: version 0.8.0 (built together with Step 2), play-tested 2026-11-13** (results below). The test must show: a character with no Charge orb can try, practise and buy Volley; an existing owner of Two-Shot and Three-Shot still has their ghosts; Slowdraw's timing and sounds unchanged by the copy; 5 ghosts at 0.75 seconds, 10 more by 1.45 seconds, the ripple at 1.5 seconds reading as a brightening; no whole-arrow glow from the game; echoes on early shots; the rack at 2.0 seconds with barbs on the inner heads and a clear sight; the burst pattern looking the same to a second client; pellets ending at 30 metres; ghosts landing where the real arrow lands at range; a spectator seeing the volley fly; and Holy Arrow still arming, now after Charge 3 and with no volley needed.

Must also log in Step 3: the skill table's count and the copied skill's name and id at start on both sides; the name of the skill Two-Shot modifies, read at load; the real arrow's one-off multiplier on a Volley-only charged shot; the real arrow's and a ghost's positions at 30 metres on the server and on the shooter; the colour-ramp texture names on the copied head material, then one ghost with its ramps swapped, and, for the fallback material found to replace the Steel Song one (play-test 1 could not reach it; Part 12, item 13), where it was found and whether it has the colour properties; each slot's shrine position, and a look at the shrine in VR to see whether the orbs fit; and, if a client without the mod is available, its "Missing hash" and caught errors and whether it stays connected.

**Steps 2 and 3 results (play-test 2, 2026-11-13, one client and the local server, script `docs/playtest-0.8.0.md`).** The run was partial: the user opened the shrine (A1), tried Burst (A4), tried and then bought Quickdraw 4, and tried Holy Arrow with Charge 1 to 3. What worked: the settings migrated per key (5 defaults reset, the rest kept); the Volley skill registered and Two-Shot and Three-Shot point at it; the tree is as designed (Holy Arrow on top of the Slowdraw column, needing Charge 3; Burst above Three-Shot); the one-ultimate rule (Holy Arrow triable but not buyable while Quickdraw 4 is owned); sales (Full Volley and Volley sold, counts correct, nothing left behind); Burst's timeline and pellets, with the server and the client agreeing on held releases; Holy Arrow arming on a live draw with no volley and firing; and the Quickdraw 4 Line. What did not work: (1) the charge showed nothing from the thump to Holy Arrow being ready, because 0.8.0 switched off the game's glow before the Fill exists (now a binding rule, Part 9, item 15); (2) Holy Arrow was lost when the string relaxed for about a third of a second, which is the design's behaviour until Brace (Step 4); (3) on a very quick early shot the shooter saw 2 echoes the server never fired (Part 12, item 2; fixed in Step 4). Release timing, server minus client: -78 to -111 milliseconds on snaps and -156 to -568 milliseconds on long holds. The client's KCP socket errors at the end of the session came from shutting the server down while connected, and the server's audio errors are the game's own (see "Constraints"). The user's character changed during the test (Part 13, "The user's character since play-test 2").

What stayed untested, and where it is re-checked:

- Most of Part A of the script: snaps and early shots with echoes, the held volley's beats and landing, the glow comparison, relaxing and twitching, hits on a creature, the stale-timer and re-grab checks (A2 and A5 to A12 were not walked as written). Re-checked in the Step 4 play-test where they still apply.
- All of Part B (the test build): the Burst build, Holy Arrow owned with Quickdraw 4 (the two kept ultimates through the override), the shared `ThreeShotSeconds` check, the empty-string check and a message without a trailer read as "absent". Part B becomes the main route in Step 4's script.
- A client without the mod: none was run. Still untested; re-checked whenever such a client is available.
- The server's relay to a second client (build bits and claims reaching a spectator, a spectator seeing the volley and the burst pattern): no second client was available. Still untested, as after Step 1.

**Step 4. Weight (stagger, interrupt, the Balance Ring), Brace, Overcharge and Steady.** Before tuning anything, log the five stun calls and each creature's cripple value before and after hits on 4 or 5 species. Carried in from play-test 2: the Fill and the switch-off of the game's glow ship in the same build (Part 9, item 15), so the charge is never silent between the thump and Holy Arrow being ready; Brace answers the user's lost Holy Arrow after a short relax; and the echo disagreement is fixed so the shooter never sees spectral arrows the server will not fire (Part 12, item 2). The user's character owns no Volley orb and cannot buy Holy Arrow, so this step's script leads with the test build (Part 13, "The user's character since play-test 2"). The test must show: the Fill and the thump, with the game's glow gone in the same build and never before; on a very quick early shot, the echoes the shooter sees matching what the server fires (none drawn that the server did not fire, logged with both crossing times); whether an interrupt reads as a flinch or looks broken (the user chose to keep the reduced interrupt on the condition that it passes this check; if broken, Weight falls back to stagger and pierce only); the Balance Ring tracking the pool; no knock-down from the mod within 4 seconds of a knock-down by the game; Charge 3's pierce hitting a second creature within 8 metres for the same damage; a snap interrupting with Charge 2 owned; a charged hit using up a mark for the extra 0.4; Brace holding everything down to 40% with 0.6 seconds of grace below it, the collar opening at the loose line, a braced release throwing a slower arrow with its ghosts, and a braced arrow released past the loose line flying at full speed; the second thump at 3.75 seconds; less wobble on every shot, faster snaps on the shooter's own screen from the moment of release, and charged arrows landing exactly where they did before buying a Quick orb.

Must also log in Step 4: whether a second knock-down can happen before the pool has refilled completely; the steps in which the client receives the cripple value; for one pierced shot, the server's hit of the real arrow (creature, point, damage) beside the continuation's result, compared with what the shooter saw; one line renderer on a copy of 'Orb Connection Line' with a red start colour and a green end colour, to see whether gradients work; whether 'Hot Effect' has the emission control component; and the creature's current action and its cancellable flag at each interrupt.

**Step 5. Quick visuals and Mark** (suggested: also Snap Draw). The test must show: the blue-white textured Ring, the double ring, the Streak, the Ring shatter and the Sight, each visibly more aggressive than the tier below; hit spikes visible at 25 metres; pins lasting 12 seconds, at most 3, seen only by the shooter and not through walls; a pop on a second snap; the server's and the shooter's mark lists agreeing in the log; and with Snap Draw, a window that is never shorter than before.

**Step 6. Seeking Volley.** The test must show: the lock arcs building and handing over from the Ring; in a group of creatures standing close together, the creature shown as locked being the one hit; a release within 25 degrees curving in, the real arrow included, and one outside flying normally; a locked snap whose real arrow flies straight while its echoes seek; the steered real arrow ending in a real collision that deals the game's normal arrow damage with the release's multiplier and the locked share (logged), and letting go when the target dies or goes behind cover; the steered arrow looking smooth on the shooter's screen through the authority hand-over, with every snapping correction logged (Part 12, item 9b lists what to log); the same curves on a second client, and how the steered arrow looks to that spectator; on a server with PvP on and on one with PvP off, what a steered arrow does to a locked player; locked seekers dealing 60%; a comparison of Seeking Volley's locked release with Quickdraw 4's thump-window Line and Holy Arrow on the same creature (damage per release, hit rate over at least 10 releases, and time to kill), which decides whether the real arrow's locked share needs lowering; the lock breaking under each listed condition, and surviving a release otherwise; an ally in the line of fire not stealing the lock from a creature; and the volley simulation's time per tick, which answers the lag worry with a number. Must also log: every seeker that arrived but dealt nothing.

**Step 7. Quickdraw 4 windows.** The test must show: the Ring re-forming on each window and vanishing when it closes; a Line in a window carrying the charge multiplier and not 3 times; the rack waiting until 2.25 seconds; the Lance; a Brace re-pull opening a window only after 0.4 seconds braced; with real network delay, no Line drawn that the server did not fire; the claim tolerance settled before windows are judged: either sized from more release samples than play-test 1's four (server minus client -78 and -94 milliseconds on snaps, -156 and -231 milliseconds on long holds, on a local server), or a measured client-to-server offset corrected and the 60 milliseconds kept for what remains, with the choice and the samples logged; and, with both Quickdraw 4 and Holy Arrow owned (through the test-only build override, or a save that really owns both if one was made), windows opening on the thump and the overcharge, the held ghosts in Holy Arrow's storm-ghost skin riding the column in a window, and no window once Holy Arrow is armed.

**Step 8. Holy Arrow forks and rods.** The test must show: Holy Arrow with no Volley orb gathering motes instead of ghosts; the pour reading as gathering and not as loss; the armed core staying on; a braced or light release still firing the full beam; forks going one to each creature and never to the direct target; all 16 bolts still present; marks lasting until the beam and becoming rods; the same forks on a second client; with the server's PvP switched off, the blast leaving a second player and the shooter untouched (no damage, no cripple change in the log), and with it on, both taking half the damage and the stun with today's falloff; and, with Quickdraw 4 also owned, a Line marking a creature that later becomes a rod. Must also log: the beam's own damage rating and the direct hit's actual damage; whether the beam's impact tool belongs to an item (which decides whether forks can earn experience); and the experience gained per fork.

**Step 9. Bridges.** The test must show, for each of Echo, Tracer, Gold Volley and Shatter: that it does something visible when bought from either neighbour alone, and what its section says when both are owned.

The 12 new icons are shown to the user on a comparison sheet (the game's icons above, the new ones below) before any of them ships, as the standing rule requires.

## Part 14: Settings and migration

**Settings.** Every number and colour in this document is a setting in `UserData\QuickshotRing.cfg`, re-read within 2 seconds while the game runs, as today.

Each machine reads its own file. Settings fall into two groups. Shared settings (the clock, shares, stagger, burst and fan angles, ring radii, seek-curve shape, lock cone, range and dwell, window lengths, the snap speed bonus) must be the same on the server and every client; the server's values decide damage, and a client with different values only sees wrong pictures. Look-only settings (colours, widths, brightness, sounds, haptics) are per client. Each side logs a checksum of the shared group at start-up and on every reload, so a mismatch shows in the logs; if the build bits have room, the server's short checksum rides in them and a client whose checksum differs logs one loud warning. The file's header tells the user that shared groups must be edited in both files. **(Claude's call; not in the approved design.)** This matters already: today the client's file is at settings version 3 and the server's at version 2, with different tree layout values.

Named in the design: `LockPlayers` (`Always`, `PvPOnly` or `Never`; default `Always`), a server setting; the locked-seeker share (60%); and the real arrow's damage share on a locked Seeking Volley release, a server setting, default 100% **(Claude's call;** suggested name `LockedArrowShare`**)**. There is no ultimate priority setting: a character that kept two ultimates has both active. Holy Arrow's existing `PlayerDamageShare` (0.5) keeps its meaning but now applies only where the server's PvP switch is on; its description says so. Names for the rest are left to the builder. Icons stay overridable from `UserData\QuickshotRing\Icons`.

**Settings migration.** Today, raising the settings version resets every entry, which would wipe the user's hand-tuned values such as the lengthened Holy Arrow explosion. Instead, settings migrate per key: the mod stores each entry's shipped default, and an entry is reset only if its default changed and its current value still equals the old default. **(Claude's call)**

Defaults in today's file that this design changes, from comparing the source with the design:

| Setting today | Today's default | What the design needs |
|---|---|---|
| `[QuickshotRing] Colour` | gold `#FFC733` | Edge blue-white `#B8E6FF`; gold now means charged. |
| `[Volley] Colour` | `#FFEDB8` | cyan `#00E3FF` for uncharged ghosts. |
| `[Archery] TwoShotSeconds` | 0.5 seconds after Slowdraw charges | 0.75 seconds after the 68% crossing. |
| `[Archery] ThreeShotSeconds` | 1.0 second after Slowdraw charges | 1.0 second after the 68% crossing. |
| `[Volley] OuterInterval` | 0.12 seconds | 0.045 seconds. |
| `[Volley] HitFlash` | a light per landed ghost | one light per volley. |

**Save migration.**

- Owners of Two-Shot and Three-Shot keep them as Volley and Full Volley, because the slot ids do not change.
- Quickdraw 4's slot id does not change.
- Holy Arrow's slot id does not change; only its link moves to Charge 3. An owned orb stays owned when its links change. A character can therefore own Holy Arrow without Charge 2 and 3: in 0.6.1 and 0.7.0 Holy Arrow follows Three-Shot, so any save that bought it that way with only Slowdraw 1 is such a case. (This document earlier named the user's own character as one; play-test 1 showed it owns Slowdraw 1 but not Holy Arrow.) The design is silent on this. The default here is to leave it owned and let it scale from whatever Charge tier the character has, so at Charge 1 its direct hit is 5 times arrow power (4 times 1.25), as today, not the 8 times quoted elsewhere. **(Claude's call)**
- A character that owns two ultimates keeps both, and both stay active **(the user's decision, 2026-11-12)**. Only Quickdraw 4 with Holy Arrow can occur. No save known today is such a case (the user's character owns no ultimate, play-test 1), so this path is tested through the test-only build override unless a real two-ultimate save is made (Part 13). No code changes the save; the load is only logged (item 10 of Part 12). Selling one is a normal sale, and it cannot be bought back while the other is owned.
- The 12 existing orbs keep their game costs, so sales of orbs already owned refund correctly (Part 3).
- Slot ids unknown to a server without the mod are skipped with a warning, so removing the mod stays safe.

## The Archery Lab

An "Archery Lab" exists for tuning looks before building them. It is a Unity editor tool, not game code, and nothing in it ships. Its source is in `attmod\UnityBridge\SpellLab` (namespace `ArcheryLab`) and it runs in the Unity project at `C:\ATTUnity`, in the scene `Assets/_Preview/Archery Lab.unity`, with a control window under the menu "Archery Lab".

What it does: you tick which of 22 bow orbs you own (it fills in what each needs and allows one ultimate), set the moment of release, and scrub or play one timeline counted from the 68% crossing. It draws the bow, the Ring, the ghost rings, barbs, the Fill, the collar, the lock arcs, curves, the Line, the pour and the Holy Arrow impact against six stand-in targets, and prints in words what a release at that moment does. It can render comparison sheets: 13 builds (each line alone, each pair, all three, each ultimate alone, and each ultimate with everything) at 4 held moments and 4 moments after release. The current sheets are in `C:\ATTUnity\shots\combos` (`sheet_1` to `sheet_4`). The sheets of the bow read clearly; the sheets at the target are faint, because effects are drawn at their real widths.

How it relates to the mod: its settings are grouped the way the mod's are (colours, the clock, and a group per line and per ultimate) and several carry the mod's own names and values (for example the Holy Arrow rings, bolts and crackle, and the Quickdraw 4 line and spirals). Values tuned in the lab are saved to `SpellLabSettings.json` in the Unity project and have to be copied by hand into the mod's defaults; nothing links the two. The lab draws only flat coloured lines, so it shows placement, timing, colour and count, and not the textures, particles or materials that will carry the final quality. It contains some look values this document does not define (for example the fan angles of the two ghost rings and the claw's tilt), and at least one that disagrees with it (the pin is drawn at a 14 cm radius against the design's 6 cm ring; the lab's Line also stops at 60 metres against the design's 150). Where the lab and this document disagree, this document is the design and the lab is a preview.

## Claude's calls, in one list

The user can overrule any of these.

1. Three layers ride on every shot (rule 3).
2. Spectral arrows scale only with the Charge multiplier; Quick multipliers and Quickdraw 4's 3 times never reach them (rule 4).
3. Steady's speed bonus applies to snaps only. Wobble is 0.75, 0.5 and 0.35, stacking with fletching.
4. Holy Arrow no longer needs a full volley.
5. Holy Arrow's beam speed no longer depends on pull, so an armed Holy Arrow is always the real beam, braced or not.
6. Holy Arrow's forks never hit the beam's own target, so its single-target ceiling stays at the 8 times the user approved.
7. A release always looses whatever the bow holds. Brace is "rest and release anywhere", not "pull back to loose".
8. Two-Shot and Three-Shot become Volley and Full Volley on a skill of their own, with saves kept.
9. ~~Two owned ultimates: one goes dormant (default Holy Arrow active), and the dormant one can be sold for a full point. Nothing is deleted or sold automatically.~~ Replaced by the user's decision of 2026-11-12: a save that owns two ultimates keeps both active, with no dormancy and no full-point sale. See items 48 and 49.
10. Locked seekers deal 60% of their share. Unlocked homing keeps the full share because it already depends on where you aim.
11. The shooter's client picks the lock and the server only checks it.
12. Players stay lockable by default because the user asked, but creatures always win the lock, and seekers reach players as muted sparks where PvP is off.
13. Weight is built on the game's real cripple pool, with an interrupt as its payoff and the Balance Ring to show it. (Keeping the interrupt in its reduced form, and dropping it if it looks bad in VR at Step 4, is now the user's decision of 2026-11-12.)
14. The Sight never predicts a normal arrow's arc, only straight shots. Converge uses the prediction only internally.
15. Tracer became tracer rounds.
16. Burst's pellets are barbs on the inner ghosts.
17. Under Holy Arrow, Edge stays blue-white and only adds gold crackle.
18. Ghosts never turn instant under Quickdraw 4.
19. A Line that carries a charge uses the charge multiplier, never 3 times.
20. ~~The real arrow is never steered.~~ Overruled by the user on 2026-11-12: on a locked release within 25 degrees the real arrow curves in too. See items 50 to 53.
21. Only one ultimate can be bought, with the others still triable.
22. Every new orb costs 1 point by default; the existing orbs keep the game's costs so that sales refund correctly.
23. Holy Arrow keeps sacrificing its arrow by default (kept from the first spec, where it was the user's decision).
24. The tight-then-fan volley is kept (kept from the first spec, where it was the user's decision).
25. All new haptics are on the bow hand, and the mixer also takes in the game's own release cue.
26. On RevampedSkills servers, Quiver Gather stands down and RevampedSkills' Holy Arrow settings are respected.
27. Settings migrate per key.
28. With other orbs owned, a Quickdraw 4 Line can jump, refract, pass through creatures and carry stagger, which widens the first spec's "single target, no knockdown".
29. Four of the user's first-spec volley decisions change for owners of specific orbs: extras no longer always vanish on a target or surface (Ricochet bounces once off terrain; Gold Volley with Charge 3 and Seeking Volley with Charge 3 pass through one creature); an extra can then hurt more than one thing; Burst pellets, gold ghosts, Holy Arrow's storm ghosts, seekers and Tracer ghosts carry short trails; and snaps and early shots carry echoes, so firing early no longer fires exactly what has appeared.
30. The rack moves to 2.25 seconds whenever a line window is open at 2.0 or closed less than 0.12 seconds before it; this document reads that as the one exception to rule 8.
31. A character that already owns Holy Arrow without Charge 2 or 3 keeps it; it scales from the Charge tier owned (5 times direct at Charge 1, as today).
32. Settings are split into a shared group that must match on every machine (checked by a logged checksum) and a look-only group per client.
33. A server-only, off-by-default test override lets play-tests use orb sets the real character does not own.
34. The server keeps the authoritative list of marks; the shooter's client mirrors it.
35. A seeker aims at the point where the lock completed, finishes with a real raycast, and goes through the normal damage rules: guaranteed arrival, not guaranteed damage.
36. A Seeking Volley lock belongs to the player and survives the release (switchable by a server setting).
37. One target-list picker serves every build.
38. ~~A dormant ultimate is left out of the build entirely; a dormant Quickdraw 4 snaps at Quickdraw 3's 1.75 times, a dormant Holy Arrow cannot charge; selling the active one wakes it.~~ Removed with dormancy (the user's decision of 2026-11-12).
39. Which half of a bridge applies depends on the lines owned now, not on purchase history.
40. ~~Weight's interrupt is used only on actions the game marks cancellable, and not on Wyrms until tested.~~ Now the user's decision of 2026-11-12, no longer Claude's call.
41. Spectral hits carry no cripple of their own; all of the mod's stagger goes through Weight.
42. Braced arrows released past the loose line get full release strength from the shooter's client.
43. Steady's tier rides in the build bits rather than in the flight speed, unless Step 1 shows the speed carries it exactly.
44. The full-volley ripple animates the batched heads' material instead of using the game's per-renderer effect.
45. Ghosts fold into Holy Arrow last out first in: barbs, then outer ring, then inner ring.
46. Overcharge's full-strength blast stun applies to creatures only; where PvP is on, players and the shooter keep today's falloff. (Before 2026-11-12 this held "until pending decision 3 is answered"; the user's PvP answer did not mention Overcharge, so it stays Claude's call.)
47. Each new orb enters the tree in the step that makes it work; Holy Arrow's re-link and live-draw gate come with the Volley skill in Step 3; saves, settings and the previous DLL are backed up before every deploy.
48. With two kept ultimates (Quickdraw 4 and Holy Arrow), the moment of release decides: a snap or a Line window follows Quickdraw 4, an armed Holy Arrow release always fires the beam, other held releases follow the normal held-shot rules; the held ghosts take Holy Arrow's storm-ghost skin; marks are both jump points and lightning rods (Part 5).
49. Selling an ultimate is never tried on the user's real character during tests, because it could not be bought back there. (As of play-test 1 that character owns no ultimate; the rule holds if it buys one. Two kept ultimates are tested through the test-only override, item 33, or a save made for the purpose with the user's go-ahead.)
50. A snap's real arrow is not steered even when a lock exists; its echoes still seek. Otherwise every snap on a Seeking Volley build is auto-aim. Early shots are steered; this can widen to "steered only once ghosts are showing" after Step 6.
51. The steered real arrow deals its full damage by default, through a server setting for its share on a locked release (default 100%), the first knob if Seeking Volley dominates.
52. The steer is a Harmony postfix on the arrow's own per-tick flight routines, runs on the server and the shooter's client only, switches gravity off while steering, and lets go (the arrow flies on normally) when the target dies, despawns, goes behind cover or the curve's time runs out. Spectators follow the network sync unless Step 6 shows that looks bad.
53. The shot claim carries one "steered" bit, which the server checks and relays.
54. Holy Arrow's blast on players where PvP is on keeps 0.6.1's numbers and does not add the game's own `PVPMultiplier` and `PVPCrippleMultiplier`.

## Why the non-obvious rules exist

The first version of this design was walked through as six builds and checked against the game code. These rules are the answers to what broke. They are recorded so that nobody "simplifies" them back.

- **A release looses whatever the bow holds.** The earlier Brace needed a pull back past 68% to fire. In every 16-slot build that pull opened a fresh snap window, and the rules then contradicted each other: a locked 30-seeker volley would become 3 echoes, nothing decided whether an armed Holy Arrow fired the beam or a snap, and Snap Draw disagreed with Brace at 60% draw. The game also launches the beam on any release whatever the mod wants, since the beam is a spawned extra arrow.
- **Holy Arrow's speed from a full draw.** Without it a braced Holy Arrow would fire at 40% of beam speed. It also matches RevampedSkills.
- **Marks last 12 seconds, and until the next beam under Holy Arrow.** At 6 seconds, every mark expired before a Holy Arrow could land (6.5 seconds of hold plus about a second to nock and draw), so rods could never fire.
- **One flight model.** Ghosts and the real arrow were believed to fall differently, so the layers would need different aim points at range, and the lock cone would miss far targets that needed elevation. Later code reading suggests the gap is small; using one model keeps them agreeing either way, and Step 3 measures it.
- **Locked seekers at 60%, a dwell that grows with distance, one knock-down per lock.** Seeking Volley turned a volley that only lands point blank into guaranteed hits out to 60 metres, with 25 degrees of slack. It became the best pick for single targets and packs alike: "busted" through hit rate instead of through a number. Since then the user decided that the real arrow curves in too (2026-11-12), which raises that hit rate again, so the real arrow's share on a locked release is a setting and Step 6 compares Seeking Volley with the other two ultimates.
- **A snap's real arrow is not steered.** With the real arrow curving into the lock, every snap on a Seeking Volley build would be an auto-aimed shot at up to 1.75 times, released a quarter of a second into the draw, and the Quick line's whole skill (timing and aim) would stop mattering.
- **The steer is a postfix on the arrow's own flight routine, on the server and the shooter only.** The game writes the arrow's velocity every physics tick, so anything that writes it at another point in the tick can be overwritten. The arrow is client-first, so steering only on the server would have the shooter see a straight arrow pulled sideways, and steering only on the shooter would have the server's arrow, which deals the damage, fly straight.
- **Creatures beat players, the dwell needs a steady bow, a better candidate takes over.** An ally standing in the line of fire stole the lock, and a fast lock grabbed whatever crossed the cone during the swing.
- **The shooter's client picks the lock.** With the server picking on its own delayed view, the server could lock a neighbouring creature in a pack, and the shooter's arrows then bent away from the one shown as locked.
- **Target lists sent with the shot.** Unlocked homing, bounces, splinters and forks were each chosen separately on every machine, so the arrows seen and the damage dealt disagreed.
- **Converge assigns targets under Seeking Volley.** Otherwise it did nothing when locked and collapsed the sweep onto one creature when unlocked.
- **Marks only help the target you aim at.** Marks pulled volleys off the creature being aimed at, and split a focused lock.
- **Weight on the cripple pool, with an interrupt.** The game's stun ignores strength and exists for five species, so graded "flinch" and "stagger" tiers would have shown nothing, small taps would have caused knock-downs out of nowhere, and Weight would have done nothing to a Wyrm. (Code reading since then shows the interrupt is risky on Wyrms, which are left out until tested; the user chose on 2026-11-12 to keep the interrupt in that reduced form; see Charge 1.) The snap taps (0.05 and 0.1) were cut. Gold Volley's 0.05 per ghost hit with no Charge orb is kept; it is the first value to cut if small hits cause knock-downs with no visible build-up.
- **Charged Lines use the charge multiplier, and a Brace re-pull needs 0.4 seconds.** A quick flick of the string after the thump gave a Line at 3 times with full stagger and pierce, strictly better than the windows, which made Brace mandatory and the windows dead.
- **Windows use the snap's grammar, the rack moves, the Sight shows from 40% pull.** Windows were invisible while open, and the rack's cue fired in the middle of one.
- **The lock arcs are their own piece.** The Ring was being asked to be the snap timer on the tip and the lock marker on the target at the same time.
- **One meaning per colour.** Under Holy Arrow everything turned gold, which hid the thump; under Seeking Volley the charge and the lock both ran cyan to orange; Ricochet put "charged" orange on an uncharged ghost.
- **The pour squeezes forward; only a backward dim means loss.** Arming and losing the charge had the same shape.
- **The game's whole-arrow glow is switched off.** It wrapped the whole arrow in cyan and orange, including the half next to the face, just as the Fill flared.
- **Barbs on the inner heads, no jitter.** Fifteen jittering pellets sat on the front sight exactly where the target is.
- **Burst at a flat 4% with 2.5 and 5 degree rings.** The earlier pattern had two falloffs at once and landed almost nothing beyond about 5 metres.
- **Forks never hit the direct target.** Otherwise a lone boss took about half as much again as Holy Arrow's approved number.
- **Tracer rounds.** The earlier Tracer did nothing at all when bought from the Quick side.
- **One haptic mixer, identity by rhythm.** Overlapping cues overwrite each other, one runtime drops frequency, and the game plays its own release cue on the bow hand at the same moment as the mod's release ticks.
- **Steady's speed for snaps only, and wobble never zero.** Faster charged arrows changed the learned arc by a quarter or more the moment a snap orb was bought, which broke the user's own example, and zero wobble made fletching meaningless.
- **Snap Draw ends at the later of two times.** With a slow hand, the earlier rule closed the window sooner than before buying the orb.
- **The ratchet done by 1.45 seconds, the ripple at 1.5 or folded into the thump.** The earlier ratchet broke the beat spacing and buried the ripple.
- **The server favours the shooter within 60 milliseconds.** Players are taught to release on beats, which is exactly where two clocks disagree. (Play-test 1 measured the disagreement at 78 to 231 milliseconds on a local server, so the number itself is re-sized in Step 7; the rule of favouring the shooter stands.)
- **Minimum on-screen sizes and near-bow cues.** Converge, Ricochet, the Quickdraw 1 hit and Steady I read as flat at 20 to 30 metres.
- **The Line's 0.5 second fade and all 16 bolts kept.** The user loves both; shortening or replacing them was never asked for.
- **Practising a passive orb means doing its thing.** The shrine shows no text, and touching an orb taught nothing.
- **Two kept ultimates stay active, and the moment decides.** An earlier version of this document put one of them to sleep ("dormancy"). The user decided on 2026-11-12 that both keep working, because one charges and the other is a quick shot, so they never compete for the same moment. Deciding anything during save load would not have worked anyway (the save arrives one orb at a time), so the load is only logged.
- **Builds read from owned slots.** The game's level count goes stale after some sales, and its "is unlocked" answer stays true after an orb was only tried.
- **The Volley skill copy is cleaned and registered at start.** An uncleaned copy inherits Slowdraw's practice trigger, effects, glow, id and damage tiers, shares Slowdraw's input and its sounds through the shared pullback settings, and a status for it could arrive before the skill exists on a client.
- **Spectral multiplier from the draw record.** The game's helper returns 1.25 for a player with no Charge orb, and today's code applies the Slowdraw level whether or not the shot was charged.
- **Fork damage from a snapshot.** The arrow is destroyed at release, before the forks exist.
- **Clients draw from curves and cast little.** A ray per ghost per tick on every client allocates memory and causes hitches in a headset.
- **Per-key settings migration.** A version bump would wipe the user's tuning.

## Left to the builder

- How to split the new code into files, beyond "each visual effect in its own small class".
- The slot ids for the 12 new orbs, the Volley skill's id, and the skill objects for the passive orbs.
- The layout of the build bits and the exact bit layout of the shot claim.
- Names of all new settings.
- Shrine positions for the new orbs.
- The exact sizes the design does not give: line widths, brightness, the fan angles of the two ghost rings, the claw's tilt, the Balance Ring's size, particle counts not stated.
- How the dwell time behaves nearer than 10 metres and beyond 50 metres (the design gives 0.6 seconds at 10 metres rising to 1.2 at 50, within a 3 to 60 metre range).
- Whether a locked seeker's stagger is also cut to 60%. The design's arithmetic for Pack Hunter uses the full share for stagger on a locked volley, so only damage appears to be cut, but it does not say so in words.
- Whether the choke on Burst needs Converge alone, as this document reads it, or also a Quick orb. The approved design files the choke under "with Quick" in two places while naming only Converge.
- How "practise by doing its thing" works for each passive orb beyond the three examples given (Mark, Brace, Converge).
- Where Snap Draw, Converge, Ricochet, the one flight model, the tree re-link, the new orbs' slots and the removal of Holy Arrow's full-volley gate fall in the build order (Part 13 gives suggestions).
- Which settings are shared and which are look-only, beyond the examples in Part 14.
- The fold order. The approved design gives two (barbs first in Burst's entry, barbs last in Holy Arrow's); this document uses barbs first.
- Whether the mod's hand-built hits set the hit direction (Part 12, item 6).
- The exact curve for the steered real arrow (control points, how its arrival is timed against the inner ring's), within "the same kind of curve as the inner ring" (Part 5).
- Whether the steer sets velocity only, or also nudges position, if Step 6 shows drift between the server's and the shooter's copies.
- How the shooter's client learns that the server cleared the "steered" bit on an uncharged shot, beyond the position sync pulling the arrow back (Part 12, item 9b).
- The exact bit counts of the claim's time field and of any settings checksum in the build bits.
- The glyphs for the 12 new icons, and whether the Two-Shot and Three-Shot icons are redrawn for 5 and 15 arrows. All go to the user on the comparison sheet first.
- What a character that owns Holy Arrow without Charge 3 should see at the shrine.
- How to test the relay and spectator behaviour if a second client is not available.
- The next version number.

## Open questions that need measuring in play

Each of these, and every other claim this document could not settle by reading code, is attached in Part 13 to the build step that first depends on it (the "Must also log" lists). In short: the wire, the marker and flight-speed values, the arrow's flight model, the slot cap, RevampedSkills' patches and the materials in Step 1; the stale timer's cause, the Quickdraw multipliers, the arrowhead's cripple, experience per hit and health-update traffic in Step 2; the Volley skill's registration, what Two-Shot modifies, the Volley copy's multiplier, ghost-against-arrow landing, the colour ramps, the fallback material that replaces the Steel Song one, shrine fit and a client without the mod in Step 3; knock-downs, the cripple sync, pierce, line-material gradients, 'Hot Effect' and the interrupt in Step 4; seekers that arrive without damage, the steered real arrow's smoothness and corrections, and Seeking Volley's balance against the other ultimates in Step 6; the beam's damage rating, fork experience and the blast with PvP on and off in Step 8.

- Do the added bits survive on both carriers, the bow status and the shot message, including the server's relay? Test this first; most of the design depends on it. (Play-test 1: yes, from client to server on both carriers; the relay to a second client is still untested.)
- Which creatures does cripple actually knock down, and how big and how fast-refilling is each species' pool? Log the five stun calls and cripple before and after hits on 4 or 5 species before tuning 0.2, 0.4, 0.6 and 1.0.
- How does `Agent.CancelCurrentAction` read in play? Code reading already shows part of what was hoped for is impossible: it cannot stop an attack animation that has already started, so "the wind-up is cancelled" holds only for what comes after the animation trigger, and the creature may simply choose the same attack again. On Wyrms it can break the dig and emerge states, so Wyrms are left out until tested. If it looks broken, Weight falls back to stagger and pierce only. The user chose on 2026-11-12 to keep the interrupt in this reduced form, subject to that Step 4 check.
- Does swapping the colour ramps on 'Tool Effect Material' recolour the skill glow cyan-only and orange-only? If not, use a fallback material. The Steel Song material named before cannot be reached through 'Steel Song Mesh Effect' (play-test 1), so the fallback needs another source.
- How far apart are the server's and the client's clocks at release under real network delay? This sizes the 60 millisecond tolerance for line windows and stages. Play-test 1, on a local server with no real network delay, already measured server minus client at -78 and -94 milliseconds on snaps and -156 and -231 milliseconds on long holds, beyond 60; play-test 2 measured -78 to -111 on snaps and -156 to -568 on long holds. Step 7 sizes the tolerance from more samples or corrects an offset. The gap also decides echoes on very quick early shots, which Step 4 fixes (Part 12, item 2).
- Do 24 orbs fit the shrine with the Multi line in the middle column?
- ~~What are the shipped slot cap (8 assumed), the banked-points cap and the sell refund percentage?~~ Answered in play-test 1 on the local server: 8, 3 and 0.8.
- Are the game's arrows on the newer constant-speed flight or on plain ballistics, and how far apart do ghosts and the real arrow land at 30 metres? (Play-test 1: 'Rusty Arrow' is on the newer flight; other arrows and the landing gap are still to measure.)
- Does the Balance Ring read well when the creature's cripple value arrives in coarse steps?
- Does QuickshotRing coexist with RevampedSkills 0.4.4 on one server (the draw threshold check, Holy Arrow, extra arrows, arrow return)?
- What does a raycast cost on the user's server, and what is its physics rate under load?
- Does the steered real arrow look smooth on the shooter's screen when the server's position starts blending in at about 0.2 seconds, does it ever snap (a server position more than 2 metres off), and does the game's own client-side blending, which also writes the velocity each physics tick, fight the steer? How does it look to a spectator who runs no steer?
- Is Seeking Volley, with the real arrow steered at full share, clearly stronger on single targets than Quickdraw 4 and Holy Arrow in play?

Two more, raised by the stress test and the research rather than by the approved design:

- Does a faster snap arrow deal more damage or experience than a slower one? Edge is meant to add none; log a real arrow's hit at about 50 and about 65 metres per second.
- Do spectators see volleys fly today? This was never tested with a second player.

## Decisions pending with the user

This is not decided. It has a default that the builder uses until the user answers. Items 2 to 5 of the earlier list (which ultimate stays active on the user's character, the Holy Arrow blast and players, Seeking Volley and the real arrow, and Weight's reduced interrupt) were answered on 2026-11-12 and are recorded in "What the user decided"; their numbers are kept here so earlier references still make sense.

1. **RevampedSkills and the 16-slot servers.** Will the 16-slot servers the user plays on install QuickshotRing on the server too? It has to be on both sides. And may RevampedSkills be switched on in the user's local server (it is in `Mods_disabled` there) for a combined test? Every 16-slot promise in this document depends on this. No default was agreed for this one, so the cautious one applies: build and test against the local server with the base 8 slots (16-slot builds tried only through the test-only override), treat every 16-slot behaviour as unproven, and do not enable RevampedSkills locally until the user says so. Note that 16 is only RevampedSkills' default; each server sets its own cap from 1 to 64.
2. ~~Which ultimate stays active on the user's own character.~~ Answered: both stay active; nobody can buy a second (Part 5, "Two ultimates on one character"). The question assumed the user's character owned both; play-test 1 showed it owns neither (Quickdraw 1 to 3, Two-Shot, Three-Shot and Slowdraw 1 only), so the answer applies to any save that owns both. A save that owns Holy Arrow with only Slowdraw 1 (Charge 1) keeps it scaling from Charge 1 (5 times direct, as today) until Charge 2 and 3 are bought (Claude's call 31).
3. ~~What the Holy Arrow blast does to players on servers with PvP switched off.~~ Answered: the blast follows the PvP setting (Part 5, Holy Arrow).
4. ~~Seeking Volley and the real arrow.~~ Answered: the real arrow curves into the lock too, except on a snap (Claude's call 50) (Part 5, Seeking Volley).
5. ~~Weight's interrupt in its reduced form.~~ Answered: kept in the reduced form, judged at Step 4 (Part 4, Charge 1).

## What is unverified

- Apart from Step 1 (play-test 1, 2026-11-12) and the parts of Steps 2 and 3 that play-test 2 covered (2026-11-13; see the Steps 2 and 3 results in Part 13), nothing in this design has been run. All other feasibility claims come from reading decompiled code and the mod's source.
- Adding bits to the end of existing messages works from client to server on both carriers (play-test 1). The server's relay to a second client, a client without the mod, and a message without a trailer read as "absent" have not been tried.
- The slot cap of 8 was read from the game's settings asset on the local server (play-test 1); the 16-slot servers' caps are still unread.
- 'Rusty Arrow' flies the newer constant-speed model (play-test 1); the other arrows' precision flag has not been read.
- The two-ultimate path (save load, the loud log, the moment-decides resolver) has no real save to test against: the user's character owned no ultimate at play-test 1, and since play-test 2 it owns only Quickdraw 4, with Holy Arrow triable but not buyable. No two-ultimate save was made before Step 2's one-ultimate rule shipped, so the path is tested only through the test-only override, which play-test 2 did not reach (Part B).
- The claim tolerance: the timing samples from play-tests 1 and 2 (-78 to -111 milliseconds on snaps, -156 to -568 on long holds) are too few to size it, and none was taken under real network delay. Play-test 2 showed the gap already decides whether echoes fire (Part 12, item 2).
- How the interrupt looks, whether the stun postfixes see every knock-down, how coarse the synced cripple value is, how much cripple an arrowhead carries, and the beam's own damage rating are all unread asset data or unseen behaviour.
- Of the materials reached through prefab fields, 'Orb Connection Line', 'WindStreak_edge_m' and 'Tool Effect Material' were found, and 'Steel Song Tool Effect Material' was not ('Steel Song Mesh Effect' has no renderer with a material; play-test 1). The source for the recolour fallback, and whether 'Orb Connection Line' takes vertex colours, are unchecked.
- How a client without the mod behaves (two errors per Volley status, Lines streaking off) is read from code, not seen.
- None of the stagger numbers has been tuned against a real creature; which creatures can be knocked down is known only from which have stun code.
- No sound named here has been heard, and no borrowed effect or material has been seen in the new use. The colour-ramp swap is a guess with a named fallback.
- Every cost in the lag budget is an estimate.
- The expected-damage table is arithmetic, not measurement.
- The mod has only been tested with the server on the same PC as the client, so nothing is known about its behaviour under real network delay, and nothing has been tested with a second player.
- Coexistence with RevampedSkills on a server is untested.
- The shrine layout mock-up is an editor render, not the game.
- Steering the real arrow is read from code only. The hook (postfixes on `Arrow.ComplexArrowFlight` and `Arrow.SimpleArrowFlight`) is a private method patch, checked only at runtime. Which of the two routines the game's arrows use is the unread precision flag. The order between the arrow's flight routine and `NetworkRigidbody.ClientUpdate` on a client, how the hand-over from the shooter's authority to the server's looks, and how a spectator who runs no steer sees the arrow, are all unknown until Step 6.
- That a steered arrow hitting a locked player follows the game's PvP rules is read from `PlayerHealth.ReceiveDamage`; it assumes the real arrow's hit reaches the player as an `ImpactHitWithTool` marked as made by a player, which was not traced through the arrow's collision code.
- The PvP switch (`ServerSettings.IsPVPEnabled`) is read from code; it has not been read at run time on the user's server, and its value there is unknown.
