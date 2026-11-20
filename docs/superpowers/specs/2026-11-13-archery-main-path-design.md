# Archery: main path, complements, tree colours and the test tablet

Date: 2026-11-13. Mod: QuickshotRing (one DLL on the dedicated server and every client), currently 0.9.2.
Decided with the user in conversation after play-test 5. Items marked **(Claude's call)** were not decided by the user.

This document replaces Rule 1 ("the shot you make decides") of `2026-11-12-archery-combinations-design.md`. Everything in
that document that this one does not mention still stands (the wire format, the claim, Holy Arrow's beam and blast,
Quickdraw 4's line, Quiver Gather, the settings system, "no new network traffic").

## Why

The user wants archers to be different from each other. In the unmodded game, and in the mod up to 0.9.2, one archer
can be a quick archer, a volley archer and a charge archer at once, switching by how long they hold the string. With
the usual 8 skill slots per player, the user wants a build to commit to one line and to use the other lines only as
support for it.

Play-test 5 also showed that Quickdraw and Two-Shot "counteract each other": a quick shot gets one faint extra arrow,
the full volley needs a hold, so owning both feels like owning one. And the user could not tell from the shrine what
any orb does to the build they already have.

## Goals

1. Every archer has exactly one **main path**: Quick, Multi or Charge. Only the main path's own shot exists for them.
2. Orbs from the other two lines are **complements**: they make the main path's shot better, in a visible way, and
   are clearly weaker than going down that line as a main.
3. Buying an orb never makes a build worse, with one allowed exception: a purchase that changes the main path, which
   is always announced before the purchase.
4. The tree shows which path is the main one and which orbs are the three "legendary" ones.
5. A small tablet at the shrine says, in words and numbers, what the held orb does to the current build.
6. The Charge strands and the Holy Arrow wind-up look finished, not like a first draft.

## Non-goals

- Brace and Overcharge stay shelved (`[Tree] BraceOrb`, `OverchargeOrb` off). Nothing here depends on them.
- The side orbs and bridges of the 24-orb design that are not built yet (Snap Draw, Mark, Echo, Tracer, Converge,
  Ricochet, Gold Volley, Shatter) and Seeking Volley are not built by this document. The rules below must not block
  them: each will later be a complement or a main-path orb under the same rule.
- No new networked objects or message types.
- Player damage from shots that never fly stays parked.

## Anti-goals

- A build that is best at everything. If Quick 3 + Volley 3 + Charge 3 is simply the strongest archer, this failed.
- An archer who buys an orb and is surprised that their main shot changed.
- A tablet that looks like a debug overlay, or a wall of text.

## Words

In everything the user reads: "Charge Shot" (the game's Slowdraw), "Quickdraw" (the user also says "quick shot"),
"multi-arrow" (Two-Shot, Three-Shot, Burst; the code says Volley). Code, logs and setting names keep their names.

- **Line**: Quick (Quickdraw 1, 2, 3, Quickdraw 4), Multi (Two-Shot, Three-Shot, Burst, later Seeking Volley),
  Charge (Charge Shot 1, 2, 3, Holy Arrow).
- **Line count**: how many orbs of a line the player owns, legendary included. Quiver Gather and the gatherer orb
  belong to no line. Orbs lent or hidden by `[TestBuild]` count as owned or not owned, as the build already treats them.
- **Legendary**: Quickdraw 4, Seeking Volley, Holy Arrow (the code's "ultimates"). The one-ultimate rule stands.

## The main path rule

1. A player with no line orbs has no main path. Their bow is the plain game bow.
2. The main path is the line with the highest line count.
3. On a tie for highest, the line that is already the main path stays. If none of the tied lines is the current main
   path (first purchases, or a sale changed things), the first of Charge, Quick, Multi among the tied lines wins
   **(Claude's call; the game records no purchase order, so "bought first" cannot be recovered after the fact)**.
4. The main path changes only when another line's count becomes strictly higher than the main path's, by a purchase
   or by a sale.
5. The server is the authority and remembers each player's main path across restarts, because rule 3 depends on
   history. **(Claude's call)** It keeps it in a small file of its own in the server's `UserData\QuickshotRing\`
   folder, keyed by the player's account id, written when it changes. A missing or unreadable file means "no
   history": rule 3's fallback order applies. The file is not part of the world save.
6. `[TestBuild]` never touches the remembered main path **(Claude's call)**. While a test build is active for a
   player, their main path is worked out from the test build's counts with the remembered real one as the incumbent,
   held in memory only; when the override is removed the remembered value is as it was.
7. The shooter's own client runs the same rule on its own owned orbs (it sees every purchase and sale of its own as
   they happen) and keeps its own incumbent in a small local file, so the tree colours and the tablet are right
   while the shrine is open, before any bow has been drawn **(Claude's call)**. The server's value in the build bits
   wins whenever it arrives; a disagreement is logged and the local value corrected.
8. Clients learn the main path from the build bits. The 32-bit build block is full, so the block gets a new version
   with 2 bits for the main path (0 none, 1 Quick, 2 Multi, 3 Charge). **(Claude's call)** Take the 2 bits from the
   settings checksum (8 bits to 6). A reader that sees the old version treats the main path as "derive it from the
   counts with rule 3's fallback".

Example the user gave: Charge Shot 3, then Quickdraw 3: Charge stays main (3 against 3, incumbent stays). Buy
Quickdraw 4: Quick 4 against Charge 3, the main path becomes Quick.

## What each archer can do

Only the main path's shot exists. The other lines' own shots are gone for that player:

| Main path | Their shot | What they do not have |
|---|---|---|
| Quick | The quick shot: release inside Quickdraw's window (0.25 s after the string passes 68%). With Quickdraw 4, the line shot. | No charge, no stages, no Holy Arrow, no ghost volley. A held shot is a plain arrow. |
| Multi | The volley: hold, the ghosts form, release. Three-Shot's ratchet and Burst's rack as now. | No quick shot bonus, no charge. A snap is a plain arrow with whatever ghosts have formed. |
| Charge | The staged charge (one stage per Charge Shot orb), Holy Arrow with the orb. | No quick shot bonus, no ghost volley. A snap or an early release is a plain arrow. |

"Gone" means the game's own skill for that line must not fire either: a Charge archer who owns Quickdraw 2 must not
get the game's Quickdraw damage bonus on a snap, and must not see the Quickdraw ring. How that is suppressed is the
builder's choice (the mod already controls every bow input in `Draws.cs`); it must be decided on the server and
agree on the shooter's client.

A player with no main path keeps the unmodded behaviour of whatever they own (they own nothing in the three lines,
so this only matters for `[TestBuild]` oddities).

## Complements

Each off-path line gives the main shot one thing, scaled by that line's count, capped at 3 (an off-path legendary adds nothing; see
"Legendaries" below). All numbers are shared settings; the values
here are the starting defaults.

### Quick archer

- **+ Multi orbs: needles.** Every quick shot fires 1 needle per Multi orb beside the arrow, instantly, each dealing
  15% of the arrow's power (no Quick multiplier on needles, as spectral arrows today). They fly their own paths in a
  tight cross (1.5 degrees), so they can hit what the arrow just missed. This replaces today's "echo" (10%).
- **+ Charge orbs: weight on snaps.** Every quick shot deals stagger 0.1 per Charge orb to the creature it hits,
  and with 2 or more Charge orbs it interrupts a cancellable action (the existing interrupt rule and cooldown).

### Multi archer

- **+ Quick orbs: a faster, tighter volley.** The whole volley timeline (first ghosts, ratchet, ripple, rack) is
  25% shorter per Quick orb, so with 3 it is nearly instant (the first five ghosts at 0.3 s become 0.075 s). The
  ghosts' spread narrows by 15% per Quick orb.
- **+ Charge orbs: gold ghosts.** Every ghost carries stagger: 0.03 per ghost per Charge orb, batched per creature
  per release as ghost stagger is today. The ghosts are drawn gold (see Looks).

### Charge archer

- **+ Quick orbs: faster stages.** Every charge stage completes 12% sooner per Quick orb (three orbs: stage 3 at
  2.5 s becomes 1.6 s). Holy Arrow's 6.5 s is shortened by the same factor **(Claude's call)**. The arrow's wobble
  drops as Steady does today (the per-tier deviance modifier), on charged shots.
- **+ Multi orbs: heavy arrows.** Every charged shot fires 2 heavy arrows per Multi orb beside it, each dealing a flat 15%
  of the arrow's power (not multiplied by the charge multiplier **(Claude's call, so the balance check below holds
  whatever the game's charge table says)**), and each carrying half of that stage's
  stagger share (the existing ghost-stagger rule). A Holy Arrow beam absorbs them (no heavy arrows on a beam).

### Balance check the builder must keep true

Measured per release, counting only what the line adds (spectral arrows' summed share of the arrow's power, or the
equivalent for timing and stagger), not the main arrow's own multiplier. For every line X and every count n: an
X-main archer with n orbs in X gets more from X than any other main gets from n orbs of X as a complement. Put the
spectral-share part of this in a unit test over the default settings. Concretely, with the defaults: 3 Multi orbs give a Quick archer 3 needles (45%) per
snap and a Charge archer 6 heavy arrows (90%) per charge, against a Multi main's
15 ghosts (5 x 15% + 10 x 6% = 135%) plus Burst's pellets per roughly 2 s hold. If tuning ever breaks this ordering,
the complement is too strong.

### Legendaries

Each legendary needs the top orb of its own line (Quickdraw 3, Burst or Three-Shot per the tree, Charge Shot 3), so
its owner has at least 4 in that line. A player can therefore only own a legendary of a line that is, or ties for,
their main path. If a sale leaves a legendary owned in a line that is no longer the main path (possible only through
ties and `[TestBuild]`), the legendary does nothing until its line is the main path again; the tablet says so.

### Quickdraw 4's line shot

Complements apply to it; otherwise buying Quickdraw 4 would make a Quick archer's Multi and Charge orbs worthless,
which breaks "never worse" **(Claude's call)**. Needles on a line shot are thin extra lines beside the main line,
resolved by the same server raycast (one per needle, the same tight cross, 15% each, never the 3x). The Charge
complement's stagger and interrupt apply to the creature the main line hits. Spectators draw the needle lines from
the relayed stage, as they draw echoes today.

### Small rulings

Holy Arrow is not a fourth charge stage; stages come only from Charge Shot 1 to 3. On the tree, the legendary tint
wins over the main-path tint, and the flip-warning tint wins over both **(Claude's calls)**.

## Never worse

- Buying an orb in the main path: more of the main shot. Never worse.
- Buying an off-path orb that does not out-count the main path: adds or raises a complement. Never worse.
- Buying an orb that changes the main path: the old main shot is lost and becomes a complement. This is the allowed
  exception, and it is announced twice before the purchase (tree colour and tablet, below).
- Staged charge: owning more Charge orbs never delays an earlier stage (already true since 0.9.1).
- Selling: can change the main path too. The tablet's footer shows the counts so the player can see it coming; no
  extra warning on sales **(Claude's call)**.

## Looks

The user asked for "very high effort" on visuals, for each form to be told apart at a glance, and for everything to
match the game's own art. The rule from the project stands: build looks from the game's assets (`Glyph_*` textures,
its materials, pooled effects and sounds listed in `UserData\QuickshotRing\loaded_assets.txt`), every size, colour
and timing a live setting, each effect in its own small class.

**The three spectral forms.** One flight model (the existing `Volley.cs` virtual arrows), three looks:

| Form | Used by | Look |
|---|---|---|
| Ghost | Multi main | As today: the blue-to-yellow ghost arrows, fanned, sliding out of the tip. With Charge orbs: gold. |
| Needle | Quick main with Multi orbs | Thin, short, bright white-gold streaks with no arrowhead mesh, the colour of the Quickdraw ring. They appear at release only (nothing shows on the held string) and leave a hairline trail. A sharp "tk" per needle. |
| Heavy arrow | Charge main with Multi orbs | Full-size arrow shapes in the charge's orange-gold, thicker than a ghost, sliding out one pair per Multi orb as the first stage completes, each wrapped in one turn of the same strand the main arrow wears. They fly with ember trails. |

**Charge strands (polish).** Keep the design the user approved (one strand per stage winding around the outside of
the shaft, a thump per stage). Changes:
- Sit closer to the shaft: the default gap drops from 12 mm to about 4 mm. The measured shaft radius is too big
  (it is taken from the largest mesh's cross-size, which includes the head or fletching); measure the shaft itself.
- The tip end must finish, not just stop: each strand tapers and converges onto the arrowhead, and a completed
  stage leaves a small bright knot at the head (one, two, three knots). At stage 3 the head itself glows.
- Strands taper at both ends, flicker slightly in width, and each has a faint wider halo strand under it so it
  reads as energy rather than wire.
- Each stage's thump sends a quick pulse of brightness along its strand from hand to head.
- The user thought the arrow "gets brighter" per stage but was not sure. Make it true: the game's glow stays on, and
  the strands' brightness steps up visibly at each stage.

**Holy Arrow wind-up (polish).** Today the strands squeeze toward the head over the last 1.25 s, then pulse. Flesh it
out: after stage 3 the three strands slowly turn from orange-gold to the beam's pale gold over the wait; small motes
drift from the hand end toward the head, faster as the moment nears; at the squeeze the strands braid into one
bright cord at the head; when armed, the cord and head pulse and a soft ring breathes around the head. Sounds and
haptics rise with it (a low hum that climbs; the existing armed cue stays). No new look for the beam itself.

**What is drawn for whom.** As today: the shooter sees everything on their own bow; other players see it dimmer
(`Volley.OtherArcherBrightness`). Needles and heavy arrows are relayed the way ghosts and echoes are now (the shot
claim's kind and stage), so spectators draw the same count.

## The tree

Client-side looks on the shrine's orbs; no gameplay.

- **Legendary orbs** (Quickdraw 4, Seeking Volley when it exists, Holy Arrow) are always tinted a distinct colour
  **(Claude's call: a pale violet-white, unlike any line colour)**, owned or not.
- **Main path**: the orbs and connecting lines of the player's main path are tinted the path's colour. Quick: the
  Quickdraw ring's gold. Multi: the ghosts' blue. Charge: the strands' orange. **(Claude's call on the three
  colours: each is what that path already looks like in play.)** The other two lines stay as the game draws them.
- **Flip warning**: any unowned orb whose purchase would change the main path is tinted a warning colour
  **(Claude's call: the game's own "can't buy" red is taken, so a clear amber)** while the shrine is open.
- The colours follow the viewing player's own build (the shrine is opened by one player at a time).
- How an orb is tinted is the builder's choice. Known from the game's code: a `SkillOrb` has a shell renderer whose
  material is picked per status, an inner model drawn with a property block, and a per-status connection line
  colour. If the shell cannot be tinted without breaking the status materials, tint the connection lines and add a
  thin coloured ring around the orb instead; say which was done.

## The test tablet

A temporary aid so testers can see what orbs do. It must look like it belongs at the shrine and must say, quietly,
that it is for testing.

**Where.** At the right of the archery shrine, on or beside the small pedestal that stands on the ground there
(the shrine is the `Profession Upgrade Shrine` prefab; the Unity project at `C:\ATTUnity\ExportedProject` and the
bridge command in the project notes show it). Position, tilt and size are settings. Client-side only; it exists
while the local player has the Ranged tree open.

**Look.** A small dark slab (a game mesh or a plain rounded quad with one of the game's stone or slate materials)
with pale ink-white lettering, the white of the orb icons. Readable at arm's length, no larger than about
35 cm x 25 cm. The font should be the closest thing the game ships to its own lettering; the built-in Arial used by
`Tutorial.cs` is the fallback, not the goal. Icons from the orb's own glyph where a line refers to an orb.

**Content while holding an orb** (the orb open in the try-out area):

```
[icon]  Two-Shot
Multi-arrow, orb 1 of 3.

With your build (Quick main):
  Quick shots: +1 needle, 15% each.

Buying this keeps you a Quick archer.
```

- Line 1: icon and the orb's name. Line 2: its line and position.
- "With your build": only lines that apply to the build the player has now, as it would be after buying this orb,
  each as "what changes: from -> to" with real numbers read from the live settings (never typed by hand, so the
  tablet cannot disagree with the game).
- Last line: one of "keeps you a <path> archer", "makes you a <path> archer (you are <path> now): <what you lose in
  five words>", or for a first orb "makes you a <path> archer".
- If the orb cannot be bought (another legendary owned, slot cap reached, not enough points, parent missing), one
  line saying which.

**Content while holding nothing:** the build's combined result, as lines of the same form ("Charged shots: 3 stages,
full at 1.6 s", "Charged shots: +4 heavy arrows, 15% each"), one per thing the build does.

**Footer, always**, Wynncraft-style: `Charge 3 · Quick 1 · Multi 0`, the main path's entry emphasised, and how many
more orbs of another line would change the main path when that number is 1 or 2.

**Test note:** one line in the same small lettering at the very bottom, such as "Test aid: this tablet is
temporary." No banner, no red, no larger type.

**Length:** at most about eight short lines at once. If a build produces more, the most important lines win
(main shot first, then complements, then the test note).

The existing floating tutorial line (`Tutorial.cs`) stays for now; when the tablet is in, the try-out instruction
moves onto the tablet as its own line and the floating text goes **(Claude's call)**.

## What changes in existing behaviour

- `Release.cs` resolver: the main path is a new input. Snap bonuses only for a Quick main; ghosts only for a Multi
  main; charge only for a Charge main. Echoes become needles (Quick main only). The "one decision per release,
  relayed in the claim" structure stands.
- The Volley skill's ghosts must not show on the string for non-Multi mains. Heavy arrows show for Charge mains.
- Quickdraw's game skill must not apply its damage tier or ring for non-Quick mains; Slowdraw's must not charge for
  non-Charge mains (no thump, no glow, no multiplier).
- `[Archery] TwoShotSeconds` goes back to being the Multi main's own number (0.3 s today); the 0.9.1 reason for
  lowering it (the dead zone between quick shot and volley) no longer exists.
- Steady (Quick tier lowers wobble and speeds snaps) applies to a Quick main's quick shots as today, and its wobble
  part to a Charge main's charged shots as the Quick complement. It does nothing for a Multi main beyond the tighter
  volley above.
- Build log lines and the tablet both state the main path.

## Must also log

Every new behaviour logs a line starting `[Archery] `: the main path whenever it is decided or changes (with the
counts and why), each release's main path and complement counts, the tablet's content when it changes, and what was
tinted on the tree. Play-test instructions are written in the user's four-line format (Skills / Do / You should
see / Wrong if), in game terms.

## Left to the builder

- How each game skill is suppressed for off-path mains.
- The wire layout of the new build block version, within "2 bits for the main path, no new traffic".
- The exact meshes, materials, sounds and timings of the looks, within the descriptions above; all as settings.
- How orbs are tinted (shell, ring or lines), and the tablet's mesh, material and font.
- Order of work. A sensible split: (1) main path rule, storage, wire and resolver, with complements at their
  numbers but existing looks; (2) the three spectral looks and the strand and Holy polish; (3) tree colours;
  (4) the tablet. Each ends in a play-test.

## Open risks

- Vanilla players lose the do-everything bow. The user chose this knowingly.
- A 25%-per-orb faster volley at three Quick orbs is nearly instant; whether that makes a Multi main with three
  Quick orbs feel like a better Quick archer is the first thing to watch in play.
- Whether an orb's shell can be tinted is unverified.
- The plain Gotera cannot be knocked down by stagger (its prefab has no stun event; its wood variants do). Stagger
  from complements will fill the Balance Ring on it with no effect, as charged shots do today.
