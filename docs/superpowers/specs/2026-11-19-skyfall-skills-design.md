# Skyfall skills: the World-Nail, Ceiling of Arrows and Crown of Giants, and choosing a skill (design)

Written 2026-11-19. This is a design only.

**Status: approved by the owner on 2026-11-19, NOT started.** The owner's words: "aqpporved design for the other skills, but dont start them on the agent". No plan is written for it and nothing is handed to the build session until the owner says so. The owner first wants to research RevampedSkills' issues with the archery mod. It builds on the approved `2026-11-19-skyfall-admin-skill-design.md` (called "the first design" below), whose skill, now called **the Sigil**, is being finished as a play-test build (0.18.0). Everything in the first design still holds unless this document changes it.

**How decisions are marked:**
- "(owner)": the owner's decision, with their words.
- "(my call)": Claude's decision. Every one can be overruled.

## What changed, in the owner's words (all 2026-11-19)
- "the ones i want is 1, 2 5 and 6, plus the aniamted desgin you amde": the World-Nail, Ceiling of Arrows, Crown of Giants, Marked Ground and the Sigil.
- "wait, they look like different archery admin skills, not one combined skill that actaves all of them". So these are **separate skills**.
- "Admin gives one". A player holds **one** skill, given by command.
- "Lets remove marked ground. all attacks are ment to kill everything it hits". So **Marked Ground is out**, and every attack **kills everything it hits**.
- "Also finish the first nearly built one and give me a build to playtest when you work on the others". The Sigil ships first. These three follow.
- Earlier: festival mode and Crystal Fall are out.

So there are four skills. Every one is fired the same way: a full draw at the sky. What happens next depends on the skill the player holds:

| Skill | Command name | Where it lands | What it is |
|---|---|---|---|
| The Sigil | `sigil` | where you point | The first design (play-test build): the sigil opens, three waves of giant arrows fall, then the colossal arrow. |
| The World-Nail | `nail` | where you point | One cliff-tall real arrow slides down through seven rings into a danger dome, then stands as a monument. |
| Ceiling of Arrows | `ceiling` | where you point | A ceiling of a few hundred arrows hangs in silence under a gold core, then the whole ceiling falls. |
| Crown of Giants | `crown` | around you | Giant gold bows draw with you, then a ring of fire comes down at their feet all round you. |

Concept pictures (private; they use the game's textures), in `QuickshotRing/Art/drafts/admin_skill/`:
- `variant-world-nail*.png`
- `variant-arrow-ceiling*.png`
- `variant-crown-of-giants*.png`
- the Sigil's `storyboard.png` and `skyfall-sequence.mp4`

## Goals
1. Four separate admin skills. An admin gives a player exactly one of them (owner).
2. Every attack kills everything it hits: every creature and every player inside an impact's kill zone. Never the caster (my call, kept from the first design). The server's PvP setting does not matter (my call, kept). Nothing outside a kill zone is touched (owner: "everything it hits").
3. Each skill keeps the look of its concept picture, built from the game's own pieces (the look rules of the first design and the game-native kit).
4. The Sigil play-test build is not disturbed. These skills arrive in a later build.

## Non-goals
- No festival mode. No Marked Ground and no Crystal Fall.
- No switching skills from inside VR (owner: "Admin gives one").
- Never more than one skill held per player.
- No warning domes on the Sigil's rain. That was Marked Ground's part of the old combo.
- No change to the skill tree, any orb, or any shot that is not a skill's cast (first design).

## Shared rules (all four skills)

### Commands (changes the first design's commands)
- `skyfall give <player> <skill>`, where `<skill>` is `sigil`, `nail`, `ceiling` or `crown`. A give replaces whatever skill that player held.
- `skyfall give all <skill>` gives every online player that skill.
- `skyfall take <player>` takes it away, whichever skill it is.
- `skyfall` lists each holder with their skill.
- `skyfall festival` is gone.
- The grant file saves each player's skill. A file written by the play-test build, which has no skill, reads as `sigil` (my call).

### Telling the holder's game
- The same invisible message as the first design. Its tag carries the **skill** instead of the festival flag (my call): `QSR_SKYFALL_V2|<skill>|<check>`.
- A game that reads the V1 tag treats it as `sigil` (my call).
- Nothing shows on screen.

### Telling spectators
- The claim's window code stays: 1 = cast, 3 = fizzled.
- The **skill id** rides in the claim's marks, which have room to spare (my call; the builder picks the exact bits).
- A friend on an older DLL ignores it, as the first design's claim already allows.
- A relayed cast with no skill id reads as `sigil`.

### The trigger
- Identical for all four (first design and plan ruling 3): full draw (MinStrength 0.9), at least 25 degrees up, drawn at least ArmSeconds 1.0 s past the 68% mark.
- What the hold shows depends on the skill held. Only the caster sees the hold; friends see everything from the release on (first design).

### Killing
"Everything it hits dies" (owner). Each impact has a **kill zone**:
- Every creature whose body reaches into the zone takes a lethal hit.
- Every player whose head is inside it is killed by the game's normal death, the Downed state, as the play-test build does.
- Never the caster.
- The game's own spawn invincibility still applies.
- Each skill's zone is given below.

The play-test build carries this rule too. It was passed to the build session as a small task before its docs.

### The same on every machine
- Every pattern (where and when each impact lands, and which arrows are drawn) comes from three things: the arrow id, the relayed centre, and the shared `[Skyfall]` settings, through `SkyfallRules.Unit` (first design).
- Every new number that changes a pattern or its timing is `[shared]`.

### Festival plumbing
The play-test build keeps dormant festival code: the signal flag, the Festival relay code, and festival care. This rework removes it. The shared Heal and Feed helper stays, because the test range uses it.

## The World-Nail (`nail`)
- **Where:** where you point, by the same sigil-point rule as the Sigil: 30 m up, 15-60 m out.
- **Hold** (caster only): seven ring bands of the game's ring sprites stack up in the sky over the spot, and a faint dome shows on the ground.
- **Release** (everyone):
  - The game's dynamite danger dome appears on the ground: NailDomeRadius 10 m.
  - The cliff-tall copy of the real arrow (about 46 m, thickened along its own axes, as in the play-test build) appears at the top of the barrel.
  - It slides point-first down through the bands over NailSlideSeconds, 2.5 s. That slide is the only warning.
- **Impact:**
  - **Kill zone: the dome.** Everything inside it dies.
  - The biggest blast: the game's Explosion, Shockwave Impact, and flint debris from the Turabada slam.
  - The bands roll out over the ground as rings.
  - The Nail stands for NailStandSeconds, 20 s, then crumbles into Large Smoke Puff and embers.
- **Reuse:** the play-test build's Nail look (its Task 12) is this skill's look. What is new is one impact instead of a storm, and the hold's band stack.

## Ceiling of Arrows (`ceiling`)
- **Where:** where you point, by the same rule.
- **Release** (everyone):
  - A gold Lightbeam core opens high over the spot: CeilingHeight, about 60 m (my call).
  - A bell-shaped ceiling of the game's arrows forms under it in rings, all pointing down and all still. There are CeilingArrows of them, about 300 (my call).
  - The outer ring hangs lowest; it is the "bell".
  - **The held breath:** CeilingHoldSeconds, about 3 s, of silence while it hangs. That is the warning.
- **The fall:** every arrow drops at once. The outer ring lands first and the centre last, over about 1 s, as a ring of fire closing in.
- **Kill zone: the whole disc under the ceiling** (my call), out to CeilingRadius, about 18 m from the spot.
  - Everything inside dies, ring by ring as the arrows land.
  - Why: a few hundred arrows visibly land across the disc. If only a few spots killed, players would watch arrows hit them and live, which breaks "kills everything it hits".
- **Looks:**
  - The few hundred arrows are drawn as one cheap batch (GPU instancing of the arrow's mesh): looks only, nothing per arrow on the server.
  - Explosions run along the rings within the 12-at-once budget. The rest of the landings show sparks and scorches.
  - The game's ring sprites and ruins runes wrap the core, gold and cyan, as in the concept picture.

## Crown of Giants (`crown`)
- **Where:** around **you**. The centre is your release point, not where you point (the concept picture).
- **Hold** (caster only):
  - Giant gold copies of the game's own recurve rise in a ring round you: CrownBows, 12; CrownRadius, about 19 m; each about 21 m tall.
  - They are see-through gold, like the Etheral Arrow, built from the bow's own meshes and bent by its own draw animation.
  - They draw as you draw, all aimed at a rune ring above you.
- **Release** (everyone): friends first see the giants here, already drawn.
  - The giants loose with you, and their arrows fly up into the ring.
  - The ring throws every arrow back down onto its own giant's feet: twelve impacts in a ring round you, the **ring of fire**.
- **Kill zone:** each impact kills everything within CrownBlastRadius, about 6 m (my call). On a 19 m circle the twelve zones overlap into one continuous ring of death.
  - The middle, where you stand, is safe, and so is anyone standing there with you.
  - Anyone in the ring dies.
- **Looks:**
  - The giants break into motes after the loose.
  - The fire ring uses the play-test build's explosions and scorches.
  - A readability note from the picture check: from far away, twelve see-through bows read as a lattice. From the caster's eye they stand round you, which reads fine. The count is a setting.

## Approaches considered
- **How a player picks a skill:**
  - The admin gives one (owner, picked).
  - Switching in VR with a gesture (offered, not picked).
- **Where the skill id travels:**
  - In the claim's marks (my pick: room to spare, no version bump, older readers skip marks).
  - The window code (rejected: 2 bits cannot hold four skills plus "cast" and "fizzled").
- **Ceiling's kill zone:**
  - The whole disc (my pick: everything you see land kills).
  - A fixed number of kill spots (rejected: arrows that visibly hit people without killing them).
- **Crown's impacts:**
  - Twelve overlapping zones at the giants' feet (my pick: a solid ring with a safe middle).
  - One wide ring band (rejected: harder to show matching the twelve arrows).
- **Order of building:** the Sigil first, as a play-test build (owner). Then the selection rework and the World-Nail, which reuses the most. Then Ceiling, which needs instancing. Then Crown, the hardest look (my call).

## Constraints
- Everything in the first design's Part D still holds:
  - one DLL;
  - the server deals all damage;
  - nothing on the headless server;
  - VR comfort;
  - the look kit;
  - the private export;
  - Opus builds and Fable reviews, with reviews on Opus max until Fable's limit lifts (2026-11-20 1pm PT).
- No new network traffic beyond the first design's one invisible message, which now carries the skill.
- The play-test build must ship untouched by this work. This work starts on the code once that build's own plan is finished.

## Left to the builder
- Every number above marked "about": tuned in play-tests, and all `[Skyfall]` settings.
- The exact bits for the skill id in the marks.
- How the arrow instancing is done.
- How the giants' draw follows the caster's pull.
- The test design for each new pattern: the Nail's single impact, the Ceiling's rings and its disc, and the Crown's ring.

## After the builder check (2026-11-19)

Three builders read this document. Their questions were answered by the owner or settled as calls. These rules override anything above.

### A safe circle round the caster, in every skill (owner)
The owner's answer: "Safe circle (Recommended)". The option read: "A clear circle around the caster in every skill: no arrow lands there, and anyone standing in it lives, like the Crown's safe middle."
- **The circle:** CasterClearance (3 m) round the caster's release point, fixed at the release.
  - No arrow lands in it.
  - No kill zone reaches into it.
  - The server never kills a player whose head is in it.
- **The Sigil:** every impact stays at least its kill radius plus CasterClearance from the caster: BlastRadius 4 m + 3 m = 7 m at the defaults. This was passed to the play-test build's kill-rule task (12c).
- **The World-Nail:** its spot is always 15 m or more out and its dome is 10 m, so the circle is clear at the defaults. A [shared] clamp keeps MinDistance at least NailDomeRadius + CasterClearance (my call).
- **Ceiling of Arrows:** when the ceiling is cast steeply over its own caster, it has a **hole** over the circle. No arrow is drawn landing there and nobody in it dies. The rest of the disc kills as described above.
- **Crown of Giants:** its safe middle (inside the fire ring) already contains the circle.

### Where the skill id travels (my call, correcting the "room to spare" above)
The Sigil already fills all three marks. So the skill id rides the claim's **stage** field (5 bits) instead: a Skyfall cast always sends stage 0 today. The builder confirms against Wire.cs, and pins it with tests. An older DLL reads it as a stage on a plain arrow, which is looks only.

### Ceiling of Arrows' hold (my call)
The Sigil's own hold, with rings climbing the arrow and a faint mark where the aim meets the sky, plus a faint gold core over the spot. It reuses the play-test build's hold looks. The ceiling itself forms only at the release, so the caster and friends see the same thing from then on.

### When this work touches the code (my call)
- Designs and plans now.
- Code only after the Sigil's 0.18.0 is staged. The selection rework changes the formats the Sigil introduces (the hidden message to V2, the skill id in the claim, the grant file), so it must not leak into 0.18.0.
- Until each skill is built, `skyfall give <player> <skill>` refuses it, so a build staged in the meantime never exposes an unfinished skill.

### The game's setup (owner)
RevampedSkills now runs on both the server and the game. The owner's answer: "Yes, keep it on both". The installer now installs to the game's Mods and the server's Mods only. That was changed at 18:48 in another session; there is no pack copy any more. A read-only check of RevampedSkills against QuickshotRing and Skyfall is running; its result goes here.

## Questions for the owner
None open after the builder check. Every call above can be changed while you read.
