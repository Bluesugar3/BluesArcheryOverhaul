# 0.9.0 play-test: the short version

Before you start: open the server's `UserData\QuickshotRing.cfg`, find `[TestBuild]`, and set `Orbs = ""` and `Without = ""`. Save. (It is still lending you Holy Arrow, Brace and Overcharge from last time, which is why Quickdraw 4 showed as "can't buy".)

Do the tests in order. Each one is: the skills you need, one thing to do, what you should see. Tell me which number looked wrong.

## Test 1: charge feedback
- **Skills:** Slowdraw 1 (you own it).
- **Do:** pull to full, hold for a slow count of three, release.
- **You should see:** an orange-to-gold line growing along the front of the arrow shaft, then a thump (sound and a pulse in your bow hand) at about two seconds.
- **Wrong if:** nothing appears on the arrow while you hold.

## Test 2: Brace
- **Skills:** at the shrine, pick up the **Brace** orb (the new orb beside Slowdraw 1). You are trying it, not buying it yet.
- **Do:** pull to full and hold until the thump. Ease the string forward to about half and rest there for a slow count of five. Pull back to full and release.
- **You should see:** a gold collar snap onto the shaft at the thump. While you rest, the line dims and slowly breathes. When you pull back, the collar opens and the shot fires as a charged shot.
- **Wrong if:** the line shrinks backward with a thud while you are resting at half (that is the "charge lost" animation).
- **After this:** put the orb back. It should now be buyable. Buy it if you have a point.

## Test 3: Overcharge
- **Skills:** buy Slowdraw 2. Then pick up the **Overcharge** orb (the new orb beside Slowdraw 2) to try it.
- **Do:** pull to full and keep holding for a slow count of five, then release.
- **You should see:** after the first thump, the line turns white-hot starting from the back, with embers at the tip. A second, heavier thump at about four seconds.
- **Wrong if:** there is no second thump, or the line never changes colour.
- **After this:** put the orb back. It should now be buyable.

## Test 4: Holy Arrow wind-up
- **Skills:** buy Slowdraw 3. Then pick up the **Holy Arrow** orb at the top of the Slowdraw column to try it.
- **Do:** pull to full and hold for a slow count of eight, then release.
- **You should see:** from about five seconds, the line squeezes toward the arrowhead and gets brighter. A gold flash at about six and a half seconds, then the core keeps pulsing. Release fires the beam.
- **Wrong if:** nothing changes on the arrow between the thump and the gold flash.

## Test 5: Holy Arrow with Brace
- **Skills:** own Brace (from Test 2), and keep the Holy Arrow orb in hand.
- **Do:** hold to the gold flash, ease the string to half, rest for a slow count of five, then just let go.
- **You should see:** Holy stays armed while you rest. Letting go from half fires the full beam.
- **Wrong if:** Holy is lost while resting, or a normal arrow fires.

## Test 6: quick snaps
- **Skills:** Quickdraw 1 to 3 and Two-Shot (you own them).
- **Do:** ten snaps, as fast as you can: pull to full and let go at once.
- **You should see:** each snap flies normally with one or two small cyan echo arrows beside it.
- **Wrong if:** a snap fires with no echoes, or arrows look like they go wild.

## Test 7 (only if you find a creature)
- **Skills:** Slowdraw 1 or higher.
- **Do:** hit it with a charged shot (released after the thump).
- **You should see:** a gold cracked ring at its feet that fills a bit more with each charged hit.
- **Wrong if:** no ring appears on a creature, or a ring appears on a rock or a tree.
