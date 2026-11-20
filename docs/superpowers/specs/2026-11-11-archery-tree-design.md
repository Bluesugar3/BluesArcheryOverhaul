# Archery tree rework: design

Project: `attmod\QuickshotRing`, a MelonLoader mod (C#, one DLL installed on both the game client and the dedicated server) for A Township Tale (Unity 2020.3, Mono). Date of the conversation this records: 2026-11-11.

This document replaces the earlier draft plan in this file. It records what the user decided in a question-by-question conversation. Anything marked **(my call)** was chosen by the assistant and not confirmed in those words by the user.

## Why

The user finds the Ranged skill tree "not worth it and too inconvenient". In their words:

- Once you shoot from a quiver "you spend most of your time picking up the arrows than actually shooting the bow", and you lose many of them. The game's existing Arrow Gatherer only works with pouches.
- Two-Shot and Three-Shot don't add enough damage to be worth the points.
- Holy Arrow destroys your arrow for a "slight damage increase", which is never worth it.
- The Quickdraw line is "already really good"; a fourth orb there is an "ultimate", something "really cool to everyone else".

## Goals

1. Give back the time spent collecting arrows (Quiver Gather).
2. Make the Slowdraw side worth taking: Two-Shot, Three-Shot, then Holy Arrow as a payoff that justifies sacrificing an arrow.
3. Give Quickdraw a top tier that is clearly the best quick shot.
4. Make the two top-tier shots instantly distinguishable to anyone watching.

## Non-goals

- Fixing damage against other players. Arrows currently stick into players but "never initiated combat". The user wants this later, as a separate feature.
- Showing names or text in game. Skill names exist only in code.
- Changing Quickdraw tiers 1 to 3, Slowdraw itself, or the pouch gatherers.

## Anti-goals (would make it a failure even if it works)

- Art that looks out of place. Every model, texture, icon and effect must match A Township Tale's own style. Reuse the game's assets where they exist; anything new must be built to match the real thing after looking at it. The user rejected clean vector concept art as "very poorly made".
- Lag. The user wants to "minimize the amount of things happening that physically could cause lag". No extra networked objects, no heavy particle effects.
- Clutter: effects that obscure the player's aim or read as noise.
- Holy Arrow being spammable.
- The two top shots looking alike.

## Constraints and known facts

- Skill logic runs on the server. Clients learn skill state only from the status messages the game already broadcasts (charging / charged / cancelled / used) and from their own input. Clients do not reliably know skill levels.
- The arrow's physics is owned first by the shooting client, then the server.
- A skill's level is the number of its orbs owned. The shrine builds orbs and connecting lines from a plain list, so orbs can be added or re-linked at load time on both sides. Saves store orb ids and skip unknown ones.
- Vanilla charged-shot damage multipliers by level: Quickdraw 1.25 / 1.5 / 1.75 / 2.0; Slowdraw 1.25 / 1.75 / 2.0 / 2.5; Holy Arrow 2.5. (The fourth values exist in data but vanilla has only three orbs per line.)
- Vanilla draw timings, counted from the string passing 68% of full draw: Quickdraw charges at once with a 0.25 s window; Slowdraw 1.75 s; Two-Shot 0.75 s after Slowdraw charges; Three-Shot 1.5 s after Slowdraw charges; Holy Arrow 4 s on its own timer. Only Quickdraw times out.
- Vanilla extra arrows are full networked objects and are the expensive path; vanilla fires them with no spread.
- Holy Arrow's beam is a projectile whose damage is its multiplier times the sacrificed arrow's maximum damage. The bow has no effect on it.
- The game has: an explosion system with distance falloff and knock-back; a stun that slows a player's movement (to roughly 60%, per the user) and recovers gradually; a "ghost item flies into a container" effect used by the pouch gatherer; a server call that docks an item into a quiver.
- Skill icons are 512x512 white-on-transparent textures. Bow skills share one template (ringed circle, the same drawn bow on the left, a symbol on the right; first tiers add broken outer dashes). Gatherer skills use a hand-inked object.
- Already built and installed but never seen by the user in game: QuickshotRing 0.4.0 (gold quickshot ring; a fourth Quickdraw orb that multiplies speed by 10 and damage by 2 with a code-drawn beam, shockwave rings, sky bolt and crawling arcs).
- A third-party mod, RevampedSkills, is on the client only. It also alters Holy Arrow (its own damage setting, and an option to save the arrow) and has its own arrow recovery. If it is ever installed on the server it will conflict.

## Part 1: Quiver Gather

**What it is.** A new orb in the Ranged tree, above Arrow Gatherer, requiring it. Always on once owned **(my call)**.

**Behaviour (user's decisions)**
- Applies to arrows the player took from their quiver and shot from their bow.
- 15 to 20 seconds after the shot the arrow returns and docks into the quiver it was drawn from. Only that quiver; only that player's arrows. Default 15 s, adjustable **(my call)**.
- It returns from wherever it is: on the ground, in a wall, in a living creature, or still in the air.
- Quiver full when the time comes: the arrow gives up permanently and is an ordinary arrow.
- Quiver not being worn: the arrow keeps waiting and returns once the quiver is worn again.
- Someone else picks it up and stores it: ordinary arrow from then on.
- Someone else picks it up and drops it: it can still return.
- Left behind: if the place it lies unloads before it returned (the player moved away or teleported), it becomes an ordinary arrow. This is the intended way to lose arrows: possible, but not easy.

**Assistant's calls, accepted by the user as a list**
- If someone is holding the arrow when its time comes, it waits until released.
- If the owner picks it up and shoots it again, the countdown restarts.
- If the owner stores it somewhere other than its quiver, it becomes ordinary.

**Look.** Reuse the game's pouch-gatherer effect (a ghost copy of the item flying into the container) and its sounds. The user will adjust the look after seeing it. Their original picture was "a beam of small, not inconvenienced light" zipping from the arrow to the quiver.

## Part 2: Two-Shot and Three-Shot (the volley)

**What it is.** The existing skills, improved. Same way of charging and firing.

**Behaviour (user's decisions)**
- Extra arrows follow the path of the aimed arrow. More arrows means a wider spread.
- Extras never interact with each other in flight.
- The purpose is consistency: a volley reliably lands some arrows.
- An extra disappears the moment it hits a target or a surface.
- The main arrow's damage is unchanged. Each extra adds a percentage of the main arrow's damage.
- Two-Shot: 3 extras, tight around the main arrow, 25% each. Maximum total 175%.
- Three-Shot: those same 3 at 25%, plus 6 in a wider ring at 10% each. Maximum total 235%. (The split was the assistant's proposal, so Three-Shot can never be worse than Two-Shot; the user chose it over a flat 15%.)
- Firing early fires exactly the arrows that have appeared so far.

**Assistant's calls, accepted by the user as a list**
- Extras cost nothing and are never real items; nothing to collect.
- "A percentage of the main arrow" includes the main arrow's charged bonus.
- Each extra can hurt one thing, once.
- All percentages are adjustable in a settings file.

**Arrangement and look (user's decisions)**
- No spinning.
- All extras point as if from one shared spot at the string hand (the user's reference: the three-arrow shot in Mulan), with their tips arranged around the real arrow's tip: a tight triangle for the three, a wider ring for the six. The real arrow is in the centre.
- Each extra is fully visible at its tip and fades to nothing well before the string, so nothing clutters the view next to the player's eye. The user also wants this because it is cheaper.
- Only the real arrow has fletching. The outer six are slimmer and dimmer than the inner three. The real arrow stays solid and normal.
- Arrival: each extra slides forward out of the real arrow's tip into place and brightens as it settles, about a sixth of a second. The three arrive almost together; the six arrive one at a time.
- In flight each extra is a full glowing arrow beside the real one. On impact: a small flash, then gone. No trails, nothing left behind.
- Extras look like the nocked arrow with the game's skill glow, so they read as a skill and not as real arrows **(my call)**.
- Known limitation, told to the user: one or two extras will pass through the bow's grip or the bow hand near the front. The user said a more abstract look is fine.

## Part 3: Holy Arrow

**What it is.** The end of a straight line: Slowdraw, Two-Shot, Three-Shot, Holy Arrow. It can only be fired by absorbing the nine extra arrows. The game's existing skill is kept as the base and changed, not replaced.

**Charge timeline (user set the 5 s total; the split is the assistant's, accepted)**

| Time from 68% draw | Event |
|---|---|
| 1.75 s | Slowdraw charged (unchanged) |
| 2.25 s | Two-Shot: three extras appear almost at once |
| 2.75 to 3.75 s | Three-Shot: six more appear one at a time, one every 0.2 s |
| 3.75 to 5.0 s | The nine gather into the nocked arrow |
| 5.0 s | Holy Arrow ready |

The user wants the gathering to feel quick, filling only that last 1.25 s.

**Behaviour (user's decisions)**
- Flight: very fast but not instant, flying true to where it was aimed. Five times a normal arrow's speed.
- On landing it explodes and damages everything in the area, so a pinpoint hit is not needed.
- Direct hit: 4x the charged main arrow. Blast: 2x at the centre, falling off toward the edge. A direct hit may one-shot most things but must not be a guaranteed kill on everything.
- Anything that can be knocked down is knocked down. Players get the game's existing stun slowdown; nothing new is built for that.
- Power comes only from the sacrificed arrow. The bow does not matter.
- It destroys the arrow.
- Not spammable; the 5 s wind-up is the cost.

**From earlier multiple-choice answers, listed back to the user and not objected to**
- Blast radius 5 m, with full damage inside about 1.5 m **(the 1.5 m is my call)**.
- The blast hurts everything in range: creatures, other players, and the shooter.
- Holy Arrow sits after Three-Shot in the tree.

**Assistant's calls, listed back and not objected to**
- Releasing before 5 s fires the volley and keeps the arrow.
- Once ready, it stays ready until fired or relaxed; relaxing loses nothing.

**Look (user's decisions)**
- Colour and character: warm white and gold, radiant. This is the lightning shot: "a holy arrow of lightning".
- Gathering: the arrival in reverse, with no spin. The outer six and then the inner three slide back into the real arrow's tip, which brightens each time; a single flash when the last goes in, and the arrow becomes the game's own holy arrow. A controller buzz signals ready.
- Trail: as the arrow flies straight, a golden bolt is drawn along its path with a few sharp kinks, never a ruled line. It flickers, has a few short side forks, lights up the route, and hangs for about half a second after landing.
- Impact: a flash of light with small lightning wisps coming off it.
- Falloff must be readable without a dome. Agreed starting point ("kind of how I was picturing it", to be adjusted after seeing it): brightness means damage. A bright core flash the size of the full-damage zone, a ring racing outward along the ground that dims as it goes and dies at the edge, and wisps that are long and bright near the centre and short and faint at the edge. About half a second, nothing left behind.
- Stun marker on each stunned creature or player: like a taser. Thin, very fast, small arcs on the body, in gold. The user's requirement: it must "look like lightning without it looking like clutter". Proposed rules, which the user said they "kind of like" before moving on, so treat as a first version: two or three arcs at once; each lasts a couple of frames; three or four sharp kinks each; thin, bright core, tapered ends; each arc runs between two points on the body; they come in bursts with gaps; bursts thin out as the stun wears off.

## Part 4: Quickdraw 4

**What it is.** The fourth Quickdraw orb. Internal name "Quickdraw 4". It replaces what the 0.4.0 build calls Lightning Arrow.

**Behaviour (user's decisions)**
- An instant, perfectly straight line from the bow along the aim. Whatever is first on the line is hit. No flight and none of the game's arrow physics for this shot.
- 3x damage.
- Fired exactly like Quickdraw: release inside the short window.
- The real arrow instantly ends up stuck in whatever the line hit, where the line stops. It must not fly off. It stays a real arrow, recoverable by hand and by Quiver Gather.
- On a miss the line ends at a maximum reach and the arrow drops from there as a normal arrow.
- Single target. No blast, no knockdown.

**Assistant's calls, accepted**
- The line ends at the first of: a hit, a hard cap (start at 150 m), or the edge of the world currently loaded around the shooter. So a miss always lands somewhere Quiver Gather can reach.
- At the end point the arrow drops straight down with no forward speed, so it cannot carry itself out of the loaded area.

**Look (user's decisions)**
- Cold blue-white, sharp. The whole line appears at once, thin, bright and ruler-straight; it holds for a blink and fades over about a third of a second, with a small sharp snap of light at the hit. Nothing lingers or spreads.
- The quickshot ring turning blue when this shot is armed stays **(my call)**.

**Changes to what is already built**
- Remove from the 0.4.0 build: the 10x speed, the travelling beam with spirals, the shockwave rings, the sky bolt, the crawling arcs. Lightning now belongs to Holy Arrow.

## The two top shots side by side

| | Quickdraw 4 | Holy Arrow |
|---|---|---|
| Travel | instant straight line | 5x speed, real flight |
| Shape | ruler-straight | jagged, crackling trail |
| Colour | cold blue-white | warm white and gold |
| Target | one point | 5 m area |
| Damage | 3x | 4x direct, 2x blast |
| Cost | quick release | 5 s hold and the arrow |

## Damage: what each multiplier multiplies

"Base" below means the sacrificed or fired arrow's own maximum damage rating.

| Shot | Damage | Source |
|---|---|---|
| Quickdraw 4 | 3 x base. Replaces the vanilla level-4 value of 2.0; does not stack with it | user agreed to a table showing "3x one plain arrow" |
| Volley extra | 25% or 10% of (base x the Slowdraw level multiplier), fixed at the moment of release | user: "a percentage of what the main arrow does"; fixing it at release is **my call** |
| Holy Arrow, direct hit | 4 x (base x the Slowdraw level multiplier), i.e. 8 x base with Slowdraw maxed | user agreed to a table showing "8x, plus the blast" |
| Holy Arrow, blast | 2 x (base x the Slowdraw level multiplier) at the centre, linear falloff to the edge | user agreed |

- A target struck directly by Holy Arrow takes the direct hit only, not the blast on top **(my call)**.
- Quickdraw 4 never flies, so there is no impact speed. It uses the arrow's rating at full effectiveness; the bow does not change it **(my call)**. Holy Arrow ignores the bow by the user's decision.

## Answers to builder questions

- **Releasing during the gathering second (3.75 to 5.0 s) fires the full nine-arrow volley** (user's decision). The arrows that had already slid in pop back out as the string is released. Owning Holy Arrow must never make Three-Shot worse. So the server treats the volley as fully armed from 3.75 s until Holy Arrow becomes ready at 5.0 s; the gathering is visual only until that moment.
- **The Holy Arrow blast damages and stuns players in this build** (user's decision), through the game's explosion route, which is separate from a direct arrow hit and may already work on players. Confirm on the first test. Direct hits on players from Holy Arrow, Quickdraw 4 and volley extras stay as they are (not registering) until the parked player-damage feature. The shooter is included in the blast.

- **Volley extras diverge.** They leave on the lines they were already pointing along from the shared spot, so the volley starts tight and opens with distance. Each extra is its own straight path, simulated on the server and drawn on each client; none of them is a networked object. (User: "having more arrows gives you a bigger spread"; they chose "tight then fan".)
- **Holy Arrow flies dead straight, with no drop**, and lands where it was aimed **(my call**, from the user's "the arrow is still flying where it is pointed. And lands where it's pointed"**)**.
- **Every client runs this mod at the same version.** This is the user's own private server and the mod ships to clients through their launcher pack. No support for unmodded or older clients is planned **(my call)**. Log the mod version on both sides at start-up.
- **RevampedSkills stays as it is, client-only.** Its Holy Arrow and arrow-recovery logic only acts on a server, so it is inert here. Do not plan for coexistence on the server; if it is ever installed there, one of the two has to be switched off **(my call)**.

## Icons

- New icons are built from the game's own icon art so frame, bow and line quality match. Working method: keep the ring and bow from a vanilla bow icon, clear the symbol area, and compose a new symbol from vanilla pieces plus shapes drawn at the same weight. The gatherer-family icon is hand-inked in the pouch icon's style.
- Needed: Quiver Gather, Two-Shot (3), Three-Shot (9), Holy Arrow, Quickdraw 4.
- Status: first drafts of the first four exist only as a temporary comparison sheet and still need the user's review. The user chose an icon for the fourth Quickdraw orb (jagged, lightning-style chevrons) before the redesign in Part 4 made that shot a straight line; **both top-tier icons must be redrawn**: Quickdraw 4 around a straight line, Holy Arrow around lightning.
- Always show the user a sheet with the real icons above and the new ones below before shipping.

## How the user wants to work

- Iteratively: sketch, react, adjust. Mock-ups should be quick, not polished.
- Anything visual gets reviewed before it ships, and the looks agreed here are starting points to tune once seen in the headset.
- The assistant cannot play the game. Every check in game needs the user in VR; the mod should log each stage so results can be read from log files.

## Left to the builder

- Order of building and how to split the code into files.
- How the server applies damage for shots that never physically fly (the volley extras and the Quickdraw 4 line). Intended: through the arrow's own damage rating, so armour, hit reactions and experience behave as for a real arrow. Untested; try this first.
- How to keep a 5x-speed Holy Arrow from passing through thin targets (a line check per step was suggested).
- How to trigger the game's knockdown and player stun, and how long each lasts. Not yet looked up.
- How clients learn which stage a draw is at and how many extras are showing, without new network traffic where possible.
- Which of the game's explosion, flash and sound assets to use; the mod already writes a list of loaded assets to help choose.
- Exact sizes: triangle and ring radii at the arrow tip, spread angle, line widths, brightness.
- Whether small hits (10%) are swallowed by armour or a minimum-damage rule; check on first test.

## Open items for the user

- Review of the four draft icons, and new drafts for the two top-tier icons.
- The player-damage problem, parked.
- Whether the stun marker rules are right once seen.
