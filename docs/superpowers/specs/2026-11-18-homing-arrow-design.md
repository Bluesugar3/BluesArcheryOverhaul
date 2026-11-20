# Homing Arrow (Seeking Volley): base design

Written 2026-11-17 against QuickshotRing 0.16.0 as installed. Read-only design: no code was changed or run for it.

The multi-arrow line's legendary, the last unbuilt orb of the archery tree. The owner calls it "homing" (their sketch of the tree: Quick ends in "pierce", Charge in Holy Arrow, Multi in "homing"); the code and older documents call it Seeking Volley. In anything a player reads (tablet, tutorial, icons) this document suggests **Homing Volley** (my call; one word in `Lines.Name`).

Sources, in the order they win: the owner's own words; the later spec `2026-11-16-archery-remaining-orbs-design.md` (Seeking Volley section, "What the research changed", "Scope", "Ricochet redefined", "The user's changes"); the owner-approved Part 5 of `2026-11-12-archery-combinations-design.md` ("Seeking Volley: the Lock"); research `.superpowers/sdd/2026-11-16-orbs/research-targets.md`; the brief `b5-brief.md`; play-test ledgers 7 and 8. The code as installed was re-read for every fact below that the design leans on.

**Marking.** "(owner)" = the owner's decision, with its date. "(earlier design)" = Claude's call in an earlier document the owner did not overrule. "(my call)" = new in this document; every one of these can be overruled.

What the owner has said about this orb, all of it:
- 2026-11-12: "while you aim at an enemy or a player you can see the lock building. Once locked, a release in roughly that direction makes all the arrows curve to hit." They worried that homing may lag the server.
- 2026-11-12: the real arrow curves into the lock too, not only the spectral arrows.
- 2026-11-16: it stays one of the four orbs being worked on; the sketch labels it "homing", on top of the multi-arrow line, after Burst.

---

## Part A. Homing in plain words

### What it is

The top orb of the multi-arrow line. You keep your aim on one creature while you hold your volley; a ring closes on it; that is the **lock**. Release roughly toward it and every arrow of the volley (the 5 inner ghosts, the 10 outer ghosts, Burst's 15 pellets and your real arrow) bends in flight and lands on that creature, even if it is moving, far away, or your aim was off by a lot.

It needs Burst. It is a legendary: you can own only one of Quickdraw 4, Holy Arrow and Homing, and it works only when the multi-arrow line is your main path (owned off the main path it does nothing, like the other two).

### How you use it

1. Draw fully. The ghosts slide out as today.
2. Put your aim on a creature and keep it there. The lock starts filling the moment the string passes the 68% mark. A near creature locks in about 0.6 seconds, a far one (50 m) in about 1.2 seconds. Holding your volley for Burst takes 1.5 seconds anyway, so on a normal full volley the lock is usually done before you would release.
3. Keep holding if you want the gold hold stages (your Charge Shot orbs), exactly as today. The lock stays while you hold.
4. Release with the bow pointing within about 25 degrees of the creature. Everything curves in.
5. Release pointing further away than that, or without a lock: an ordinary volley, exactly as without the orb.

### What you see, hear and feel, moment by moment (only you see the lock; everyone sees the flight)

- **Aiming, lock forming.** Three thin cyan arcs appear round the creature nearest your aim, facing you, and close in toward its outline as the lock fills, turning from cyan toward white. Two soft ticks (one third, two thirds) with a small double pulse in your bow hand. The ghost heads on your bow lean a little toward the creature. Look away and the arcs open again, twice as fast as they closed.
- **Locked.** The arcs snap into a white ring with a small cracked-ring flash and a star, the game's orb-acquire sound, and a firm pulse train in the bow hand. A faint tether line shows the curve your arrows would take; it shows only while a release would be accepted (within 25 degrees), so it teaches the rule. It is drawn only from about 3 m out, so it never hides your bow.
- **Lost.** The ring breaks into six sparks with the game's orb-return sound. A lock is lost when your aim stays more than 15 degrees off it for 0.3 s, it is out of sight for 0.5 s, it dies or despawns, it goes past 75 m, or the draw ends without a shot.
- **Release.** The usual release flare, and the arrows **bloom**: for the first 30% of the way each flies on its own line (the volley's normal spread), then they all bend in, the inner ring first, the outer ring and the pellets 0.04 s apart behind it, closing like an iris. Your real arrow curves in among them. No arc drop: a locked shot does not need to be aimed high.
- **Hit.** A spark per arrow, at most three impact sounds, then the lock ring slams shut with the game's orb-placed sound, one light and a patter of three ticks. Gold ghosts (hold stages) arrive gold, and their stagger lands as one hit as it does today.
- **What a watching player sees.** The bloom, the curves, the arrival and a faint ring on the creature for 0.4 s. They never see your lock forming.

### What it does not do

- It is not instant auto-aim. A creature has to stay in your aim for the dwell first, and a release more than 25 degrees off is a normal volley.
- It does not see through walls. A lock needs a clear line of sight, and cover still stops an arrow in flight.
- It does not pick a new creature in flight. If the locked creature dies before the arrows arrive, they fly straight on from where they are.
- It does not lock players (see decision 3).
- It does not make any arrow hit harder (decision 1 recommends exactly the normal damage). It makes them arrive.
- It does nothing for an archer whose main path is not the multi-arrow line.
- It does not lag the server. Arrow count drives the cost, and homing adds almost nothing to it (Part F).

### One shot, start to finish

You are a multi-arrow archer with Volley, Full Volley, Burst, Homing, two Charge Shot orbs and Ricochet. A Turabada trots across a clearing 30 m away, with two more behind it. You draw: ghosts slide out at 0.3 s, the ratchet fills the outer ring by about 1.4 s. You keep the bow on the first Turabada; cyan arcs close on it and at about 0.9 s it locks with a white ring and a pulse in your hand. At 1.5 s Burst racks its 15 pellets. You keep holding: at 2.25 s the inner ring turns gold (hold stage 1), at 3.0 s the outer ring (stage 2). The Turabada has moved; your bow now points 12 degrees behind it. You release. The 31 arrows bloom, then bend in and arrive together on the trotting Turabada; your real arrow arrives with them. Ricochet sends each landed arrow's jump on to the two Turabadas behind. The ring slams shut. Without Homing, at 30 m and 12 degrees off, the real arrow would have missed and only a few ghosts would have touched it.

---

## Part B. Decisions for the owner (at most six)

Each is an either/or with my recommendation first. Until answered, the builder uses the recommendation.

1. **Damage of homing arrows.** (a) **Every homing arrow deals its normal damage** (recommended), or (b) 60% of it, the earlier design's call. Why (a): at 60% a locked volley on a creature 10 m away deals less than the same volley aimed by hand (2.2 against about 2.45 times the arrow), so buying the orb would make that shot worse, which breaks the standing rule that buying never makes anything worse; the price of homing is the dwell, the 25 degree rule and the legendary slot instead.
2. **Where a locked volley goes.** (a) **Every arrow goes to the one locked creature** (recommended for the base build), or (b) the inner ring and your real arrow go to the lock while the outer ring and the pellets spread over the other creatures in your aim. Why (a): it is what you described ("all the arrows curve to hit"), every screen agrees on it, and Ricochet already spreads a locked volley onto the pack; (b) fits on the wire as it is and can come as a later step.
3. **Locking players.** (a) **Never, for now** (recommended), or (b) only on servers with PvP on, creatures always winning. Why (a): with (b) a locked release is an aim-assisted real arrow at another player, and nobody has tried even the creature version yet; your 2026-11-12 words ("an enemy or a player") are why (b) exists at all.
4. **How long a lock lasts.** (a) **One lock per draw: it ends with the shot** (recommended), or (b) it carries over to your next draw for about 3 seconds. Why (a): the dwell runs during the 1.5 s you hold for Burst anyway, so re-locking costs almost nothing, and (b) adds a timer nobody can see and makes follow-up shots free.
5. **If the first test shows your real arrow jumping on your own screen** (Part G, step 0). (a) **The ghosts and pellets home and the real arrow flies straight** (recommended fallback), or (b) the real arrow keeps curving and you accept a visible correction about 0.2 s after release. Why (a): a real arrow that teleports mid-flight looks broken, and the volley still homes; this only applies if the test fails.
6. **Burst's pellets on a lock beyond 30 m.** (a) **They follow the lock to its full 60 m** (recommended), or (b) they keep Burst's 30 m cut-off and vanish in their puff as today. Why (a): otherwise half the volley's arrows do nothing on exactly the long shots where Homing matters most.

---

## Part C. The rules, with their settings

All in a new `[SeekingVolley]` category (the category already exists for `SeekerShare`). "Shared" means the server's result depends on it, so it must match on every machine and counts in the shared checksum. Looks are per client.

### C1. Who has it

- Owns the orb (build bit `Ultimates.SeekingVolley`, already on the wire) **and** main path Multi. Owned off the main path it does nothing and the tablet says so (later spec, rule for legendaries).
- Only one legendary can be bought (existing `UltimateRule`, `Shrine.cs`).
- It is level 4 of the Volley skill (already scaffolded: `Tree.cs:442-445`). The Volley skill's damage table is a flat x1 for all four levels (`VolleySkill.cs:128-130`), so level 4 changes no number by itself.

### C2. Gaining a lock (shooter's client)

| Rule | Setting | Default | Why |
|---|---|---|---|
| The lock can fill from the 68% crossing until the release, only while the string is held. | none | | (my call; the earlier design said from 40% pull.) A multi-arrow archer's volley starts at the crossing, and starting there lets the server check the dwell from the shot's own time (C4). |
| Candidates: creatures (and combat dummies when `[Targets] DummiesCount` is on), alive, 3 to 60 m from you, in clear sight. | `LockMinMetres`, `LockRangeMetres` (shared) | 3, 60 | (earlier design) 60 m matches the target list's range. |
| The cone: 5 degrees plus the angle 0.6 m makes at the creature's distance, round the arrow's predicted path (so aiming high at a far creature still counts). | `LockConeDegrees`, `LockConeMetres` | 5, 0.6 | (earlier design) 8.4 degrees at 10 m, 5.7 at 50 m. |
| Dwell: 0.6 s at 10 m or nearer, rising in a straight line to 1.2 s at 50 m and beyond. | `DwellNearSeconds`, `DwellNearMetres`, `DwellFarSeconds`, `DwellFarMetres` (shared) | 0.6, 10, 1.2, 50 | (earlier design) Shared because the server checks it (C4). |
| Fills only while the bow turns slower than 30 degrees a second; looking off the candidate drains it twice as fast as it fills. | `StillDegreesPerSecond`, `DrainFactor` | 30, 2 | (earlier design) Swinging across a pack never locks anything. |
| Another creature that stays twice as close to your aim for 0.2 s takes over the filling, at any fill. | `TakeoverSeconds` | 0.2 | (earlier design) |
| Quick orbs (an off-path complement for a multi-arrow main) shorten the dwell by 15% each. | `DwellFasterPerOrb` (shared) | 0.15 | (my call; the earlier design had 15/25/35%.) One number per orb, like every other complement; they already speed up the volley and tighten its spread. |
| One lock at a time. | | | (earlier design; with Mark parked there are no pins to add more.) |

### C3. Losing a lock

| Rule | Setting | Default |
|---|---|---|
| Aim more than 15 degrees off the lock for 0.3 s. | `BreakOffDegrees`, `BreakOffSeconds` | 15, 0.3 |
| Out of sight (one sight ray every 0.1 s) for 0.5 s. | `SightLostSeconds` | 0.5 |
| Dies, despawns, or is farther than 75 m. | `BreakMetres` | 75 |
| The draw ends without a shot (string let go, or the draw "lost" after the dip grace). | | |
| The shot itself (decision 4, recommended). | `LockSurvivesRelease` (shared) | off |

### C4. The release

- **Steered**: the release has a lock, has ghosts (a held or charged volley, never an early release), and the arrow points within 25 degrees of the line to the lock. `ReleaseConeDegrees` (shared) 25. (owner 2026-11-12: "a release in roughly that direction"; 25 is the earlier design's number.)
- The server keeps the shooter's lock only when, as **it** sees things: the creature exists, is a creature, is alive, is within `LockRangeMetres + ServerRangeMetres` (5 m slack), the arrow's release pose points within `ReleaseConeDegrees + ServerToleranceDegrees` (10 degrees slack) of it, and the shot's time since the 68% crossing (the time the server decided with, `ClaimTrust`) is at least the dwell for that distance less `ServerDwellSlackSeconds` (0.15 s). Otherwise the release is an ordinary volley on the server and for every watcher. (my call) The slack exists because the server sees the creature 100 to 200 ms late (play-test 7). No line-of-sight ray at release on the server: a false refusal is worse than a lock that later hits cover, and cover is still checked in flight.
- Whichever arrows home (decision 2a): all ghosts, all pellets and the real arrow (owner 2026-11-12 for the real arrow).

### C5. How the arrows fly (the curve)

- Each arrow follows its own smooth curve (a cubic Bezier) from where it leaves the bow to the creature: along its own line for the first `BloomShare` (0.3) of the way, then bending in from its own approach angle: inner ghosts 20 degrees, outer ghosts 35, pellets 50 (`InnerDegrees`, `OuterDegrees`, `PelletDegrees`, shared). (earlier design) It does not drop.
- Arrival time is fixed at release: the curve's length at release divided by the arrow's speed; the inner ring, outer ring and pellets land `RingGapSeconds` (0.04, shared) apart. (earlier design)
- The curve's end follows the creature: every physics tick it is moved to the middle of the creature's colliders as that machine sees it. So on every screen the arrows arrive on the creature, and the server's arrows (the only ones that deal damage) arrive on the server's creature. (earlier design)
- **How hard they turn**, in plain terms: up to 25 degrees from where you pointed, plus the ring's own approach angle, plus whatever the creature moves during the flight. A guard lets go of an arrow when the creature has moved more than `LetGoDriftMetres` (10, shared) from where it was at release, or when it would need to turn more than 90 degrees from its current heading (it ran behind you). (my call)
- **Cover**: every arrow still casts along its path every tick, as today; whatever solid thing is in the way stops it (and deals damage if it is something that can be hurt).
- **The creature dies mid-flight**: each arrow lets go and flies straight on from where it is, at its speed, until it hits something or its life ends. (my call; the earlier design retargeted ghosts to the nearest creature within 5 m. Retargeting is cheap to add later, but each machine would pick on its own view; flying on is the same everywhere.)
- **Life**: a homing arrow's life is the longer of `[Volley] LifeSeconds` (3) and its arrival time plus 0.3 s, at most 4 s; a release whose arrival would take longer than that is not steered (a very weak pull at a far creature). (my call)
- **Pellets**: with a lock, Burst's 30 m cut-off is lifted for the pellets (decision 6a). (my call)

### C6. The real arrow

- Flies the same kind of curve as an inner ghost (approach angle = how far you pointed off, at most 25 degrees), at its own speed, allowed to vary by `RealArrowSpeedBand` (25%, shared) to keep to the curve, never above 99 m/s. Gravity off while steered, as Holy Arrow does with its beam (`HolyArrow.cs:356`). (earlier design, item 9b)
- Lets go on the same conditions as the ghosts, and when its curve time plus 0.5 s passes without a collision; on let-go gravity comes back on and it is a normal arrow.
- Its hit is a real collision, so armour, hardness, the game's speed-and-angle effectiveness, Ricochet's real-arrow jump and Quiver Gather all work as on any arrow. Damage share: `RealArrowShare` (shared) 1.0. (earlier design; the first knob if Homing dominates.)

### C7. Damage

- `SeekerShare` (shared): **1.0** recommended (decision 1). The shipped default today is 0.6 (`Path.cs:106`, only words on the tablet so far); changing it follows the shipped-defaults rule (an entry still at 0.6 moves to 1.0).
- Everything else is unchanged: inner 0.15, outer 0.06, pellets 0.04 of the arrow's power each, x(1 + 0.10 per hold stage) for a multi-arrow main's Charge Shot orbs, the real arrow x1.
- Guaranteed arrival is not guaranteed damage: an arrow that lands on a part it cannot hurt deals what the game says, as today.

### C8. With everything else that is built

| With | What happens | Whose call |
|---|---|---|
| Burst (30 arrows) | All 30 spectral arrows home; pellets keep their normal share; their cut-off lifts on a lock (decision 6). | my call |
| Ricochet, ground skip | A homing arrow that clips ground, rock or wood skips once, then homes again from the skip point on a fresh curve at Ricochet's kept speed (70%). Without Ricochet it lands there. | my call (the earlier design: "a seeker blocked by cover bounces once and seeks its target again") |
| Ricochet, creature jump | Unchanged: every arrow that lands on a creature jumps to the nearest others within 15 m at 50% of its own damage. A locked volley lands all of its arrows, so all of them jump. This is the strongest pack damage in the mod: watch it (Part D). | unchanged rule, flagged |
| Hold stages (Charge Shot orbs owned by a multi-arrow main) | Independent of the lock: hold for gold as today; gold arrows home and their stagger lands as one batched hit per creature. | my call |
| Quick orbs owned (off-path) | Shorter dwell (C2), on top of the faster volley and tighter spread they already give. | earlier design, re-numbered |
| Shatter (owned off-path, half strength) | Its trigger is the real arrow's first creature hit; with Homing that hit is now reliable, and the blast goes out behind the locked creature, skipping creatures the release already damaged (as today). | unchanged rule |
| Auto Dock | No interaction: the next arrow nocks itself; the next draw starts a new lock. | |
| Quiver Gather | The steered real arrow is a real arrow: it comes back as any arrow does. | |
| Combat dummy | Lockable when `[Targets] DummiesCount` is on (default on): the easiest way to try the lock. | follows G3 |
| PvP | Players are never lock candidates (decision 3). A homing arrow that meets a player on its way follows today's PvP rule for the mod's hits (`[PvP] ModHitShare` 0.5, only with the server's PvP on, never the shooter). Players are never staggered. | follows `Pvp.cs` |
| A watcher | Sees the curves and the arrival from the relayed lock; arrows beyond `[Volley] ClientCastRange` (40 m) of the watcher still curve and fade at their arrival time but show no landing, as today. | |
| Two ultimates on an old save | Not possible with Homing (only Quickdraw 4 + Holy Arrow can exist from before). | |

### C9. Shrine, tablet, try-out

- Turn `[Tree] SeekingVolleyOrb` on by default only in the last step (Part G). The multi-arrow line then has four orbs (`Lines.Size`).
- Practice: the game's own trigger for the Volley skill (any volley released while the orb is held for try-out) marks it practised. (my call; the brief asked for "a completed lock", but the try-out area has no creature, and play-test 8 showed what an unpractisable orb costs: nobody could buy Shatter or Auto Dock at the shrine.)
- Tablet: "Volley: keep your aim on a creature to lock it; released within 25°, every arrow curves in." Off-path: "does nothing unless the multi-arrow line is your main path."
- Tutorial line (`Tutorial.cs:48`): "Aim at a creature and hold until the ring closes,\nthen shoot anywhere near it." Add: "No creature here: use the test menu's spawn button."

### C10. Looks and sound (client, each its own small class with a fallback, game assets)

As Part A. Assets named in the earlier design: arcs and ring are line renderers in `Sprites/Default`; sounds 'Skill Orb Interact Sounds' (ticks), 'Skill Orb Acquire Sounds' (locked), 'Skill Orb Return Sounds' (lost), 'Orbskill Placed' (arrival); trails as the ghosts' today. Colours: lock arcs cyan `#00E3FF` to white; the charge keeps its orange-gold, so lock and charge never share a colour. All are client settings. Fable builds them (memory: Fable for art).

---

## Part D. The balance question, argued

All numbers are multiples of the arrow's power for one release of a multi-arrow archer with Burst, against a creature about 1 m wide, from the shares in the code. The unlocked numbers are on paper (ring radii at range), not measured.

Where a plain volley's arrows go: the inner ring leaves the arrow's line at 1.75 degrees and the outer at 3.5, so the inner ring is about 0.3 m off the centre at 10 m, 0.76 m at 25 m and 1.5 m at 50 m; the outer ring twice that.

| Release | 10 m | 25 m | 50 m |
|---|---|---|---|
| Plain Burst volley, well aimed | about 2.45 | about 1.25 | about 1.0 (real arrow only, if it hits after the drop) |
| Homing, locked, damage x1.0 (decision 1a) | 2.95 | 2.95 | 2.95 |
| Homing, locked, damage x0.6 (decision 1b) | 2.17 | 2.17 | 2.17 |
| Homing, locked, hold stage 3 (three Charge Shot orbs, 3.75 s hold) | 3.54 | 3.54 | 3.54 |
| Homing locked plus Ricochet: extra jump damage on up to 3 neighbours within 15 m | +1.48 | +1.48 | +1.48 |
| Quickdraw 4 line (first creature; 70% then 50% for the next ones) | 2.5 | 2.5 | 2.5 |
| Holy Arrow direct (plus a 7 m blast at 2x the charged arrow, with stun) | about 8 | about 8 | about 8 |

Per second, with rough cycles (a full Burst release plus nocking about 2.3 s; a Quickdraw 4 snap about 1.1 s; a Holy Arrow 6.5 s hold plus about 1 s): Homing locked about 1.3 per second, Quickdraw 4 about 2.3 per second when it hits, Holy Arrow about 1.1 per second plus its blast and stun.

**What Homing gives.** Not more damage per arrow: arrival. Beyond about 20 m it more than doubles what a volley actually lands (2.95 against 1.25 at 25 m), it works on a moving creature, and you no longer aim high for the drop. In VR, where long aimed shots are hard, that is a real legendary, not a cosmetic one.

**What it costs.** The legendary slot (no Quickdraw 4, no Holy Arrow), four slots of eight for the line, the dwell (you must keep a creature in your aim during the hold), the 25 degree rule, and focus: a locked volley puts all 31 arrows into one creature, so on a pack it gives up the spread a plain volley has, unless you also own Ricochet.

**Is it a no-brainer?** For a multi-arrow main, yes, as every legendary is for its own line. Against the other two lines, no: per second it stays below Quickdraw 4 on one target and below Holy Arrow on groups. That is the point of recommending the full damage (decision 1a): at 60% it would be below a hand-aimed volley at 10 m and buying it would make close shots worse.

**Is it useless?** No, for the range and moving-target reasons above.

**The combination to watch: Homing plus Ricochet on a pack.** All 31 arrows land, so all 31 jump: about 4.4 per release spread over the locked creature and up to three neighbours. That is the highest pack damage per release in the mod; per second (about 1.9) it is close to a Quickdraw 4 line that pierces three creatures. If it is too much in play, lower `[Ricochet] JumpShare` for locked releases before touching the seekers.

**Numbers I recommend:** `SeekerShare` 1.0, `RealArrowShare` 1.0, `ReleaseConeDegrees` 25, dwell 0.6 s (10 m) to 1.2 s (50 m), `DwellFasterPerOrb` 0.15, lock range 3 to 60 m, break at 75 m / 15 degrees for 0.3 s / 0.5 s out of sight, approach angles 20 / 35 / 50 degrees, bloom 0.3, ring gap 0.04 s, server slack 10 degrees / 5 m / 0.15 s.

**The three I am least sure of:**
1. `SeekerShare` 1.0 together with Ricochet's jumps (pack damage above).
2. The dwell (0.6 to 1.2 s): too short and Homing is auto-aim; too long and it never locks in a real fight. It overlaps Burst's 1.5 s hold by design, so most of its cost is aim discipline, not time.
3. The 25 degree release cone: generous. It is the owner's "roughly that direction", but if locked releases feel like they need no aim at all, 15 degrees is the next value to try.

---

## Part E. How it can be built within the hard rules (technical)

Hard rules respected: no new messages or networked objects (everything rides the shot message and the game's flight sync); every number a live setting; state per player, bow string, arrow and release; players never staggered; buying never makes anything worse (decision 1a); game assets only; per-tick cast budget unchanged.

### E1. What travels in the shot claim (`Wire.cs`, version 1, unchanged)

The claim block today (`ShotClaimWire.Write`, `Wire.cs:36-59`), 32 to 227 bits:

| Field | Bits | Today |
|---|---|---|
| version | 4 | used (1) |
| kind | 2 | used |
| window | 2 | **reserved**: always written 0, ignored on read (Snap Draw's, deleted in 0.16.0). Free for reuse without a version change. |
| steered | 1 | **defined, never set**: the shooter does not write it and `ClaimRules.ServerCheck` (`Rules.cs:214-219`) clears it on every claim. Free, and meant for exactly this. |
| time | 13 | used (ms since the 68% crossing) |
| stage | 5 | used (number of spectral arrows) |
| target count + targets | 3 + 17 each, at most 6 | used by Ricochet and Shatter owners (`Targets.Wanted`, `Claims.PutTargets`), checked and relayed by the server (`Claims.Check`, `ServerCheck(claim, facts, range)`, `Rules.cs:224-232`) |
| picks | 3 x stage, only when there is at least one target | **written as 7 ("none") for every arrow and never read for anything.** Free; decision 2b would use them (pick = index into the targets). Note they already cost up to 93 bits on every Ricochet owner's shot. |
| mark count | 2 | **reserved**, written 0; but a reader skips 17 bits per mark it reads, so reusing it changes the layout. Do not use. |

**The lock on the wire: `Steered = 1` and `Targets[0]` = the locked creature's id** (`HealthObject.Entity.PrefabRoot.Identifier`, `Targets.IdOf`, as Ricochet's ids). The rest of the list stays Ricochet's and Shatter's candidates (the lock removed from them, at most six in all). No version change, no layout change, no new bits. A shooter without the orb writes exactly what it writes today.

### E2. Who decides what

| Machine | Decides | Draws |
|---|---|---|
| Shooter's client | The lock (dwell, break, takeover), from its own view, in a new per-bow-string lock record. At the claim (client branch of `Claims.Shot`, `Claims.cs:78-89`, before the shot is sent) it sets `Steered` and puts the lock first in `Targets` when C4's rule holds on its side. | Lock arcs, ring, tether, sounds, haptics. At `Volley.Launch` its own seekers and its own real-arrow steer, from its own claim (it never receives the server's relay: `Shooter.cs:64`). |
| Server | Keeps or clears the lock in `Claims.Check` (C4's checks with slack, from the release pose the message carries, `Shooter.cs:56-57`); clears `Steered` again in `Claims.PutResolved` (`Draws.cs:270`) when its own resolution has no ghosts. Its seekers are the only ones that deal damage (`Hits.Spectral`, server only, `Volley.cs:885-886`); its real-arrow steer is authoritative after about 0.2 s. | Nothing (headless). |
| Watcher's client | Reads the relayed claim (`Claims.TargetsOf` / `TryRelay`). Steers only if `Steered` and `Targets[0]` resolves here. | Seekers and (recommended, E5) the real-arrow steer from the relay; a faint ring on the creature at arrival. |

The server never runs the dwell (it sees the string 100 to 200 ms late; play-test 7): it only checks the claimed time since the crossing against the dwell, which is why the dwell starts at the crossing.

What the shooter can never be told: whether the server kept its lock. If the server refuses, the shooter's own ghosts still curve and land on its screen (looks only) while the server's fly straight; only the real arrow is visibly pulled back. The slack in C4 exists to make that rare; every refusal is logged on the server with the reason.

### E3. Ghosts and pellets steering "identically enough"

- `VolleySim` keeps every spectral arrow in a closed form of its age (`Volley.cs:743-761`, `Position` at `:819`). A seeker gets a second closed form: the cubic Bezier of C5, evaluated at its age. Its first three control points are fixed at launch from the release pose and the arrow's index, the same on every machine (`ReleaseLayout`, `Volley.cs:629-656`; the pose travels in the shot message). Its fourth, the end, is the creature's collider-bounds centre (`Targets.Centre`, `Targets.cs:89-93`) as this machine sees it, re-read once per release per physics tick and shared by all of that release's seekers (one bounds walk per release per tick, not per arrow).
- So every machine draws the same shape ending on its own view of the creature; the server's arrows end on the server's creature and deal damage there. The only disagreement is where the creature is (network delay), which shows as arrows arriving a little ahead or behind the creature on a watcher's screen, never as arrows flying somewhere else.
- `Shot` gains: the target id and resolved `HealthObject`, the three fixed control points, the arrival time, and a "let go" flag. On let-go the arrow is re-seeded into today's closed form from its current point and direction, exactly as Ricochet's skip re-seeds (`Volley.cs:873-882`). A Ricochet skip on a seeker re-seeds a new Bezier from the skip point (C8).
- Casting is unchanged: one ray per arrow per tick from the last cast point to the new one (`Volley.cs:867-870`), halved above `[Volley] LiveCap` by `SpectralBudget.CastThisTick` (`Spectral.cs:327-329`). `LateUpdate` places visuals from the same Bezier each frame (`Volley.cs:935-951`).

### E4. The real arrow (authority after about 0.2 s)

What the game does (all re-read today in `_decomp`):
- The shooter's client launches the arrow (`Shooter.Shoot`, `Shooter.cs:69-131`: `Arrow.Shoot` at `:100`), sets `LerpAuthorityFromClient(-0.1, 2)` on **every** client that runs the shot, watchers included (`:111-114`), and sends the shot (`:119-123`).
- The server places the arrow at the message's pose and shoots it (`Shooter.SyncShoot`, `:45-67`), then relays to everyone but the sender (`:64`). Its `StartHandlingArrowDeviance` sends `ArrowFlightSync(speed, seed)` to all clients (`Arrow.cs:248-251`), which restarts their flight routine and resets `flightTime` (`Arrow.cs:168-175`, `:238`).
- Each physics tick `Arrow.ComplexArrowFlight` (`Arrow.cs:223-233`) sets the velocity from the **current** velocity's direction, turned by the deviance curve, at `flightSpeed` (`ArrowFlightSettings.GetVelocity`, `ArrowFlightSettings.cs:65-71`); gravity bends it between ticks. Flight stops on hit, collision or embed (`Arrow.cs:313-321`, `:157`, `:192-208`).
- On clients, `NetworkRigidbody.ClientUpdate` (`NetworkRigidbody.cs:267-308`, its own fixed-update module, `:18`) does nothing while `lerp <= 0` (the first 0.2 s: `FadeDampenJump`, `:363-370`, raises lerp from -0.1 by deltaTime/2), then pulls position and velocity toward the server's each tick (`:305-307`); a server position more than 2 m away snaps the arrow (`:474`).

The steer:
- A Harmony postfix on `Arrow.ComplexArrowFlight` (and `SimpleArrowFlight`, `:215-221`, for safety), for arrows on a per-arrow steered table filled at `Volley.Launch`. It runs once per physics tick only while the game considers the arrow in flight, so it ends by itself at the collision.
- It evaluates the arrow's curve at its own clock (time since this machine's launch; never `flightTime`, which the flight sync resets), sets the velocity that reaches the curve's next point in one tick (speed within the band, under 99 m/s, below the 100 m/s network clamp, `PhysicsSerializationSettings.cs:11`), and turns the arrow along it (as `AlignWithFlightDirection`, `Arrow.cs:263-266`). Because the game's own write starts from the current direction, the next tick starts from the steered one: the steer and the game do not fight on the server.
- Gravity off while steered, back on at let-go (as `HolyArrow.cs:356`, `:383`).
- Runs on the server (authoritative), the shooter (its first 0.2 s, then it is pulled to the server's copy anyway) and, recommended, watchers (E5).
- Do not suppress `ClientUpdate`: if the two curves agree there is nothing to fight; if not, the server must win.

### E5. Why watchers should steer the real arrow too (my call, a change from the earlier design)

The earlier design let watchers follow the server's position sync. But every client that runs the relayed shot also free-flies the arrow for its first 0.2 s (`Shooter.cs:111-114` runs for `isFromNetwork` too), so a watcher's copy would fly straight for 0.2 s (about 10 m at full draw) and then be pulled toward the curved one, which can exceed 2 m and snap. Watchers already have everything needed in the relayed claim (the shot message carries it and is read before the shot runs, `Claims.Bits` postfix on `ShootArguments.Serialize`), so they run the same steer. If the flight sync arrives before the relayed shot (known message order on clients), the steer starts at `Launch` anyway; its clock is its own.

### E6. Cost, and how it stays within budget

- **Server, per tick:** no new rays. Per seeker one Bezier evaluation (cheaper than today's arc, which uses sinh/atan), per release one bounds walk for the creature's centre, per steered real arrow one evaluation and one velocity write. Play-test 8 measured the spectral simulation at a median 1.75 ms per volley tick (max 16 ms); homing changes the per-arrow work, not the arrow count. Locked arrows land within a few ticks of each other, so `Hits.FlushSpectral` batches them into one damage event per creature: fewer health messages than a scattered volley. This answers the owner's worry about lag.
- **Server, per release:** one target check (exists, creature, alive, distance, angle, time): the existing `Targets.Facts` plus two dot products.
- **Shooter's client, while drawing (the cost to watch):** the lock search. The existing shot search (`Targets.ForShot`) measured 4.29 ms once (1,859 colliders); running that ten times a second in VR would drop frames. The lock search must instead be a narrow sweep along the predicted path (three capsule casts of about 1.5 m radius out to 60 m, ten times a second, staggered), plus one sight ray every 0.1 s on the current candidate; per frame only the angle arithmetic on the current candidate. Measure it with `Timing` before step 2 ships.
- Every new patch body, tick and look gets a `Timing.Section` (G5's rule): "patch Arrow.ComplexArrowFlight / Seek.Steer", "Seek.LockSearch (shooter)", "Seek.Check (server)", the seeker branch inside `VolleySim.FixedUpdate`, each look's update. Logging: `[Archery] Seek:` lines at `Summary` for lock gained/lost (shooter), lock kept/refused with reason (server), let-go reasons; per-tick steer traces only at `Debug` and only for the first few arrows of a session.

### E7. Game and mod members the approach depends on

"Verified" = I read it in `_decomp` or the mod's source today (2026-11-17). Runtime behaviour is never verified by reading.

| Member | Where | What it is used for | Verified |
|---|---|---|---|
| `Arrow.ComplexArrowFlight` (private) | `_decomp/Arrow.cs:223-233`, velocity write `:226` | the steer's postfix | yes (source); patching a private method: same as `Quickdraw4.Detect` does on `SyncFlight` |
| `Arrow.SimpleArrowFlight` | `Arrow.cs:215-221` | same, fallback | yes |
| `Arrow.StartHandlingArrowDeviance` | `Arrow.cs:235-253` (flight time reset `:238`, flight sync sent `:248-251`, registers on the shared fixed-update module `:255-261`) | why the steer keeps its own clock | yes |
| `Arrow.SyncFlight` | `Arrow.cs:168-175` | clients restart flight on the server's sync | yes |
| `Arrow.StopUpdatingArrowFlight`, `OnHit`, `OnCollided`, embed | `Arrow.cs:192-208`, `:313-321`, `:157` | steer ends at the collision | yes |
| `Arrow.AlignWithFlightDirection` | `Arrow.cs:263-266` | how to turn the arrow | yes |
| `ArrowFlightSettings.GetVelocity` | `_decomp/ArrowFlightSettings.cs:65-71` | starts from the current direction, resets the speed | yes |
| `Shooter.SyncShoot` | `_decomp/Shooter.cs:45-67` (pose `:56-57`, relay without the sender `:64`) | server's release pose; shooter gets no relay | yes |
| `Shooter.Shoot` | `Shooter.cs:69-131` (`LerpAuthorityFromClient` on every client `:111-114`) | the 0.2 s free flight, watchers included | yes |
| `Sync<T>.SendToChunks(value, except)` | `_decomp/Alta.Networking/Sync.cs:61` | relay excludes the sender | yes (signature) |
| `NetworkRigidbody.ClientUpdate`, `LerpAuthorityFromClient`, `FadeDampenJump`, 2 m snap, velocity serialisation | `_decomp/Alta.Networking/NetworkRigidbody.cs:267-308`, `:325`, `:363-370`, `:474`, `:442`; module `:18` | the hand-over | yes (source); **the order of this module against the arrow's flight module is not known** |
| `PhysicsSerializationSettings.VelocityMax` = 100 | `_decomp/Alta.Networking/PhysicsSerializationSettings.cs:11` | speed cap | yes |
| `UnityUpdateManager.FixedUpdateModule`, its loop | `_decomp/UnityUpdateManager.cs:37`, `:194-200` | modules run in registration order | yes (order at runtime unknown) |
| `BowString.HeldUpdate` | `_decomp/BowString.cs:269` (subscribed `:216`) | where the lock updates (the mod already postfixes it: `Main.cs:264`, `Draws.cs:292`) | yes |
| `NetworkScene.GetEntity`, `NetworkEntity.Identifier` | `_decomp/Alta.Networking/NetworkScene.cs:233`, `NetworkEntity.cs:82` | creature ids on the wire | yes |
| `ImpactDetector.OnCollisionEnter` | `_decomp/Alta.Impact/ImpactDetector.cs:182-203` | the steered arrow's hit on a creature is resolved on the server | source yes; that it holds in play is from the mod's own logs (Weight, Ricochet's real-arrow jump), not re-checked by me |
| Mod: `ShotClaim`, `ShotClaimWire` | `QuickshotRing/Wire.cs:9-85` | E1 | yes |
| Mod: `Claims.Shot` prefix, `PutTargets`, `Check`, `TargetsOf` | `Claims.cs:66-122`, `:129-138`, `:143-157`, `:161-166` | E2 | yes |
| Mod: `ClaimRules.ServerCheck` (clears `Steered`) | `Rules.cs:214-232` | to be replaced for the lock | yes |
| Mod: `Draws.Release` -> `Claims.PutResolved` | `Draws.cs:270`, `Claims.cs:49-57` | clear `Steered` when the server's release has no ghosts | yes |
| Mod: `Volley.Launch`, `VolleySim` | `Volley.cs:438-622`, `:743-951` | E3 | yes |
| Mod: `Targets.ForShot`, `Centre`, `IsCreature`, `IsDummy` | `Targets.cs:165-203`, `:89-93`, `:111`, `:125-138` | candidates, aim point, dummies | yes |
| Mod: Seeking Volley's slot, level 4, parent Burst, place | `Tree.cs:442-445`, `Preview.cs:190-224` | already scaffolded, `[Tree] SeekingVolleyOrb` off | yes |
| Mod: `[SeekingVolley] SeekerShare` (default 0.6) | `Paths.cs:93`, `Path.cs:106` | C7 | yes |

---

## Part F. What cannot be done as the owner might imagine it, and the nearest thing

1. **"The lock I see is exactly what gets hit."** Not exactly. The server decides every hit with its own view of the creature, 100 to 200 ms behind yours, and it can refuse a lock you saw (the creature already left your cone on its side). You are never told: the shooter never receives the relay of its own shot and there is no message to tell it. Nearest: slack in the server's check so refusals are rare, arrows whose curve ends on the creature on every screen, and a log line for every refusal.
2. **"All the arrows curve to hit" as a guarantee.** Cover still stops arrows (anything else would be shooting through walls on the server), a creature that dies mid-flight is not chased, and a real arrow is a real physics object that a branch can stop. Nearest: guaranteed arrival in the open, which is where you will use it.
3. **A real arrow that curves smoothly on every screen.** The real arrow belongs to the server after about 0.2 s. If the server's curve and yours disagree by more than 2 m, the game snaps your arrow to the server's. This is the riskiest assumption in this design (Part G, step 0). Nearest if it fails: the ghosts and pellets home and the real arrow flies straight (decision 5).
4. **Others seeing your lock form.** No message carries it, and none may be added. Nearest: watchers see the curves and a ring on the creature as the arrows arrive.
5. **"You can see the lock building" on a player** (the owner's 2026-11-12 words). Possible to build (player ids fit the same field), but it is an aim-assisted real arrow at another player. Parked behind decision 3.
6. **A locked volley that also spreads over a pack.** Not in the base build (decision 2). Ricochet already does it after the hit.
7. **Lag-free homing for free.** It is nearly free on the server; the cost that is real is the lock search on the shooter's headset (E6), which must be built narrow from the start.

---

## Part G. Risks, and a build order in small play-testable steps

### Risks, most dangerous first

1. **The real arrow's hand-over at 0.2 s** (snaps over 2 m, drift, the unknown order of the arrow's flight module and the network module on clients, the flight sync restarting the flight). Unknowable by reading; step 0 settles it.
2. **Server refusals the shooter cannot see.** Fast creatures at range plus 200 ms of lag. Measure the refusal rate from the logs in step 2; widen the slack before anything else.
3. **The shooter's lock search costing frames in VR** (E6). Measure in step 1 with `Timing`.
4. **Balance with Ricochet** (Part D). Measure per-release damage, locked and unlocked, at 10, 25 and 50 m in step 5.
5. **The aim point.** A creature's collider-bounds centre may sit in empty space for a long or odd-shaped creature (a Wyrm), or include colliders that are not its body. The cast still ends on the first solid thing, so the cost is a miss, not wrong damage. Log the aim point and where seekers landed in step 2.
6. **Things that look fine in code fail on first start** (the 0.15.0 recursion, play-test 7 and 8 ledgers). Every step ends in a first start with `[Log] Level = Debug`, the self-check line on both sides, and a bow drawn before anything is reviewed further.
7. **Changing the shipped default of `SeekerShare`** (0.6 to 1.0) follows the shipped-defaults rule; never raise `SettingsVersion`.

### Build order

Each step ends in something the owner can play-test, alone and with a second player watching.

0. **Prove the real-arrow steer, with no lock at all** (the riskiest assumption first). A server test setting, `[SeekingVolley] SteerTest = <player>`: that player's real arrows follow a fixed curve, bending 10 degrees to the right over the first 0.4 s and then flying straight with gravity off for 1 s. The shooter sets the claim's `Steered` bit while the test is on; the server keeps it for that player and relays it; server, shooter and watchers run the same steer. Nothing depends on a creature, so the curve is identical everywhere and any difference is the hand-over itself. Logs: per tick for the first three arrows, the velocity before and after the game's write, the server's position against the shooter's, every snap over 2 m. **Play-test:** shoot at a tree 20 m away: does the arrow bend right on your screen and stick where it bent? Does a watching player see the same arrow in the same tree without a jump? Then shoot at 40 m. If it snaps, decision 5 applies before step 3.
1. **The lock on the shooter only.** Arcs, ring, tether, sounds, haptics, break rules, the narrow search; no effect on any shot. `[Tree] SeekingVolleyOrb` on in the test cfg; bought with the test menu. **Play-test:** spawn frozen Turabadas with the test menu; lock one at 10 m and at 40 m; swing across three of them and check nothing sticks; look away and watch it drain; check the frame timings file for the search.
2. **The lock in the shot; ghosts and pellets home everywhere; the real arrow still flies straight.** Claim (E1), server check and relay (E2), seekers (E3), pellets' cut-off lifted. **Play-test:** locked volleys at frozen, then unfrozen Turabadas at 10, 25, 50 m; a release 30 degrees off (must be a normal volley); a watcher confirms the curves land on the creature on their screen; read the refusal lines.
3. **The real arrow follows the lock** (step 0's mechanism on the lock's curve, or decision 5's fallback). **Play-test:** locked releases at a moving creature; the arrow should arrive with the ghosts on your screen and the watcher's.
4. **Combinations and the shrine.** Ricochet (skip and re-seek; jumps from every landed arrow), gold hold stages, off-path Shatter, the dummy, PvP checks (a player standing in the path; no player ever locked), tablet, tutorial, practice; then `[Tree] SeekingVolleyOrb` on by default. **Play-test:** buy Homing at the shrine by hand (practise with a volley); each combination once.
5. **Balance pass from measured logs** (the per-release damage of locked and unlocked volleys at three ranges, against Quickdraw 4 and Holy Arrow), then the owner's verdict on the three uncertain numbers in Part D.

## Decided by the owner (2026-11-17, answers to four of the open decisions)

1. **Damage:** "Full damage": every homing arrow deals its normal share (`SeekerShare` 1.0) and the real arrow its full damage.
2. **Target:** "All onto the locked creature": one lock, one creature; no spreading over other creatures.
4. **Lock length:** "One lock per draw": every volley earns its own lock.
6. **Pellet range:** "Follow the lock to 60 m": with a lock, Burst's pellets fly to the locked creature anywhere inside lock range.

Not asked, left at the document's recommendation until the owner says otherwise: (3) players are never lockable for now; (5) whether the real arrow keeps its curve is decided after build step 0 has been seen in game.

## Changed by the owner (2026-11-17): no flying real arrow on a locked shot

The owner's words: "just for like performance wise, whenever we get to this, can we have it remove the sender arrow so it's only the multi-shot arrows? And then have it add more multi-shot arrows and just have it do the multi-shot arrows."

This replaces every part of the document that steers the real arrow (section 4's real-arrow steering, the watcher steering call, build step 0's bend test, open decision 5). On a locked release:
- **The real arrow does not fly.** Only spectral arrows home. No networked arrow is steered on any machine, so the hand-over of a flying arrow to the server, which was this design's riskiest assumption, no longer exists for homing.
- **More spectral arrows take its place.** Claude's call on the number: 5 extra "core" arrows in a tight cluster on the arrow's own line, together carrying exactly the real arrow's damage (1.0 of the arrow, 0.2 each), so a locked volley is 35 homing arrows (15 ghosts, 15 pellets, 5 core) and its total damage is unchanged. The count and share are settings.
- **What happens to the real arrow** (Claude's call, modelled on the Quickdraw 4 line): the server places it in the locked creature when the core arrows land, so the archer keeps the arrow and Quiver Gather can return it. If the volley never reaches the creature (it died, went behind cover), the arrow is placed where the core arrows ended.
- **Everything that hangs on "the real arrow's hit" needs a stand-in on a locked release:** the core cluster's landing plays that role once (Ricochet's real-arrow jump, off-path Shatter's trigger, Weight's hooks, the hold-stage stagger), never five times.
- An unlocked release is a normal volley with a flying real arrow, exactly as today.
- New build order: step 1 is the lock (shooter, server check, relay) with homing ghosts only; there is no step 0.

## Asked by the owner (2026-11-17): several locks, each arrow homing on its own enemy; and green

The owner's words: "Besides locking onto one target, it can lock onto multiple targets, and each individual arrow can home onto each own enemy. That might be really laggy though." and "turn all the ghost arrows green and have a green trail whenever it fires."

**Feasibility of several locks (checked against Wire.cs; not built, not decided):**
- The shot message already carries up to 6 creatures (`MaxTargets = 6`, 17 bits each) and the `Steered` bit. So up to 6 locks per shot need no new message and no layout change.
- Which arrow goes to which creature does not need to be sent: it is computed the same way on every machine from the relayed list (arrow i takes target i modulo the number of locks, the core cluster takes the first lock). The per-arrow pick fields are not needed, which matters because they exist for at most 31 arrows and a locked volley has 35.
- Lag: homing on several creatures costs no more in flight than homing on one. Each spectral arrow already does one cast per physics tick whatever it is aimed at; steering is a few multiplications per arrow. The cost that needs care is the same as for one lock: the shooter's search for creatures under the aim while the string is held (must be a narrow sweep a few times a second, timed before it ships). Following up to six locked creatures per tick is a position lookup each.
- What it costs in play, not in frames: full damage is split, so six locks put about six arrows on each creature. And each further lock needs its own dwell, so more locks mean a longer hold.
- Proposed rule (Claude's, awaiting the owner): while holding, every creature the aim rests on for the dwell gets its own lock mark, up to `[SeekingVolley] MaxLocks` (4; 6 is the message's limit); the first lock is the "main" one (it gets the core arrows and the embedded real arrow); arrows are dealt round the locks in turn; a lock is lost like a single one (aim far off all locks, out of sight, out of range, dead). With one lock it is exactly the single-target design the owner chose earlier.

**Green (decided by the owner):** on a locked release every homing arrow is green with a green trail. Claude's calls on what that touches: the lock marks and the closing arcs while holding are green too (the earlier text said cyan and white), so green means "homing" from the first moment; green takes the place of the ghosts' normal colour and of the gold of a multi-arrow archer's hold stages on those arrows (the stage still thumps and adds its glint and its damage); an unlocked volley keeps today's colours. The exact green is a client setting.

**Decided by the owner (2026-11-17): "Yes, up to four".** Homing allows several locks, `[SeekingVolley] MaxLocks` 4, by the rule proposed above: each creature the aim rests on for the dwell gets its own lock while the string is held; the first is the main lock (core arrows and the embedded real arrow); arrows are dealt round the locks in turn; one lock behaves exactly as the single-target design.
