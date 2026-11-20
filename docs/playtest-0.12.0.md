# Play-test 0.12.0 — the shrine: tree colours and the floating tablet

Restart the server and the game first. This version only adds things you see at the archery shrine; shots work as in 0.11.0.

If you have not yet tested 0.10.0 (the main-path rules) and 0.11.0 (the new looks), do `docs/playtest-0.10.0.md` tests 1, 3 and 5 first. They take ten minutes and everything here rests on them.

## 1. The tablet is there and readable
- **Skills:** Any.
- **Do:** Open the archery tree at the shrine and look to the right of it.
- **You should see:** A carved stone tablet floating at about chest height over a small stone block in the floor, facing you. Four blue crystals hold its corners, a larger crystal turns slowly beneath it, and small shards and stone chips drift around. It bobs very slightly. The lettering is the game's own font.
- **Wrong if:** It is missing, inside the floor or a wall, facing away, in your line of fire at the try-out target, or you cannot read it from where you stand.

## 2. Holding nothing, it lists your build
- **Skills:** Charge Shot 1, 2 and 3 bought first, then Quickdraw 1, then Two-Shot.
- **Do:** Hold no orb. Read the tablet.
- **You should see:** The title "Your archery build", lines for your charged shots (3 stages and when they are full, the stagger, 2 heavy arrows, steadier), and at the bottom "[Charge 3] · Quick 1 · Multi 1".
- **Wrong if:** It mentions quick shots or a ghost volley, or the bottom line's counts are wrong.

## 3. Holding an orb, it says what that orb changes
- **Skills:** The build from test 2.
- **Do:** Pick up the Quickdraw 2 orb and read the tablet. Put it back.
- **You should see:** "Quickdraw 2", a line showing your full-charge time getting shorter (for example "full at 2.2s -> 1.9s"), "Keeps you a Charge archer.", and the bottom line now reading Quick 2.
- **Wrong if:** The times do not change, or it says you become a Quick archer.

## 4. Your main path is coloured on the tree
- **Skills:** The build from test 2.
- **Do:** Look at the tree.
- **You should see:** An orange ring around each Charge Shot orb and orange lines along the links between them. A pale ring around Holy Arrow and Quickdraw 4. No rings on the Quickdraw or multi-arrow orbs.
- **Wrong if:** There are no rings, the rings sit edge-on so you cannot see them, or another line is coloured.

## 5. An orb that would change your path warns you
- **Skills:** Sell everything. Buy Charge Shot 1 and 2 first, then Quickdraw 1 and 2.
- **Do:** Look at Quickdraw 3 on the tree, then pick it up, read the tablet, and try it out by firing a quick shot at the target. Read the tablet again. Do not buy it.
- **You should see:** Quickdraw 3 has a pulsing amber ring. The tablet says "Makes you a Quick archer (you are Charge now)." and "You lose: the charge." in amber. After you fire the try-out shot the tablet and the ring still say the same.
- **Wrong if:** The warning or the amber ring disappears after the try-out shot.

## 6. Nothing is in the way
- **Skills:** Any.
- **Do:** With an orb in the try-out area, draw a bow and move your hands and the bow in front of the tablet. Then close the tree and walk away.
- **You should see:** The tablet and rings vanish when the tree closes. Tell me whether the lettering shows through your hands and bow, and whether the crystals' colour looks like the game's blue crystals.
- **Wrong if:** Anything stays behind after the tree closes, or the game stutters each time you open the tree.
