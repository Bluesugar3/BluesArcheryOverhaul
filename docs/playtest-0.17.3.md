# Play-test 0.17.3

Server and every player's game must be on 0.17.3 (send your friends the new file). Nobody has started any 0.17 build after 0.17.0 yet, so this is the first one you'll see: everything from 0.17.1 and 0.17.2 is in it.

**Play `playtest-0.17.2.md` first, all seven tests.** They still apply as written. Add these two:

## A. The core arrows have heads
- **Skills:** Press "Homing".
- **Do:** Spawn 3 Turabadas, lock one about 15 metres away, release, and watch the middle of the volley.
- **You should see:** Five brighter green arrows in the middle, each with an arrowhead like the others.
- **Wrong if:** The five middle ones are bare shafts or glowing sticks.

## B. No hitch when the volley lands
- **Skills:** As test A.
- **Do:** Lock a Turabada and release five times in a row. Watch for a stutter at the moment the arrows land. Kill one and watch its body fall.
- **You should see:** Smooth landings; your real arrow appears stuck in the creature; the body falls normally.
- **Wrong if:** The game stutters each time the arrows land, or the body jumps or is flung when it dies.

## The play-test range is in this build but switched OFF
Nothing of it runs until `[TestRange] Enabled = true` is set in the server's settings. Leave it off for this test. We'll switch it on together for its own test, with nobody else online the first time.
