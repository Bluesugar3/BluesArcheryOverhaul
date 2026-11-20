# QuickshotRing 0.9.0 play-test

About 15 minutes. Do the steps in order and say out loud what you see. You don't need to read any logs.

## Before you start

1. Close the game and the server.
2. Open this file in Notepad:
   `C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\UserData\QuickshotRing.cfg`
3. Find the `[TestBuild]` section and make it read exactly this, then save:

   ```
   [TestBuild]
   Player = "Blue_XD"
   Orbs = "Brace;Overcharge;HolyArrow"
   Without = ""
   ```

   This lends you three orbs for the test. Nothing is bought or saved.
4. Start the server, then the game, and join.
5. Bring a bow and about 30 normal arrows (not the blunt training arrows from the shrine).
6. Stand at the archery shrine, facing open ground.

"Full pull" below means the string pulled all the way back.

## Part 1: charging a shot (5 shots)

**1. Charge and shoot.** Pull to full, count slowly to three, let go.
- You should see: a thin orange-to-gold line grow along the front half of the arrow. At about two seconds it reaches the tip with a loud thump and a buzz in your bow hand.
- Problem if: no line, no thump, or the line is on the half of the arrow near your face.

**2. Overcharge.** Pull to full, count slowly to five, let go.
- You should see: after the first thump the line turns white-hot and sparks rise from the tip. At about four seconds, a second, heavier thump.
- Problem if: no white heat, or no second thump.

**3. Holy Arrow.** Pull to full, count slowly to eight, let go.
- You should see: both thumps as above. Then the line squeezes toward the tip and gets brighter. At about six and a half seconds, a gold flash and a buzz in both hands. Letting go fires the beam and the blast.
- Problem if: nothing happens between the second thump and the gold flash, or a normal arrow comes out.

## Part 2: resting the string (Brace) (3 shots)

**4. Rest, then shoot.** Pull to full and wait for the first thump. Ease the string forward to about half and hold it there for a slow count of five. Then pull back to full and let go.
- You should see: a small gold collar click onto the arrow at the thump. While you rest, the line dims but stays. When you pull back, the collar pops open and the shot flies like a normal charged shot.
- Problem if: the line disappears while you rest, or the shot comes out as an instant line.

**5. Rest with Holy Arrow.** Pull to full and hold to the gold flash. Ease to about half, count slowly to five, and let go from there.
- You should see: Holy Arrow stays ready the whole time you rest. Letting go from half still fires the full beam.
- Problem if: Holy Arrow is lost while resting, or the beam is slow or missing.

**6. Lose it on purpose.** Pull to full, wait for the first thump, then let the string go almost fully slack and keep it there for a slow count of two.
- You should see: after about half a second the line shrinks back from the tip and goes out, with a low thud. That is the "you lost the charge" signal.
- Problem if: the line stays, or it vanishes with no thud.

## Part 3: quick shots (10 shots)

Take your headset off for a moment. In the same file, change two lines and save:

```
Orbs = "Volley;FullVolley"
Without = "Quickdraw4"
```

Put the bow away, wait a few seconds, take it out again.

**7. Ten fast shots.** Pull to full and let go straight away, ten times, as fast as you comfortably can.
- You should see: every shot is a normal arrow with two small cyan arrows flying beside it.
- Problem if: any shot has no cyan arrows or only one, or a shot comes out as an instant line.

## Part 4: the shrine (1 look)

**8. Look at the bow orbs.** Open the archery shrine.
- You should see: two new orbs next to the Slowdraw column, each joined to it by a line. Their icons are copies of the Slowdraw icons for now.
- Problem if: an orb is missing, two orbs overlap, or the shrine won't open.

## Part 5: creatures (only if you find one)

Change the file back to `Orbs = "Brace;Overcharge;HolyArrow"` and `Without = ""`, save, and put the bow away and take it out again. Walk out from the shrine for a few minutes. If you don't find a creature, skip this part and say so.

**9. Charged shots.** Hit a creature with three shots held past the first thump.
- You should see: an orange burst on each hit and a gold cracked ring at its feet that fills up as you hit it.
- Tell me: did the creature's attack ever stop and restart when you hit it? Did that look right, or did it look broken (sliding, freezing)?

**10. Overcharged shot.** Hit a creature with a shot held past the second thump.
- You should see: it gets knocked over, and the ring at its feet shatters.
- Tell me: what kind of creature it was, and whether it fell.

## When you're done

Set both lines back to empty and save:

```
Orbs = ""
Without = ""
```

Then tell me you've finished and I'll read the logs.

## Things that are normal, not bugs

- Right after a charged shot leaves the bow, the whole arrow glows for a moment in flight.
- On your very first draw, a short coloured test line appears above the arrow for five seconds. Tell me if it fades from red to green or is one colour.
- The first draw after joining may stutter briefly.
- Training arrows do no damage, so creatures barely react to them.
