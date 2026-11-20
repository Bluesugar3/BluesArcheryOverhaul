# Play-test 0.15.0 — fixes from your three-player test

Server and every player's game must both be on 0.15.0 (send your friends the new file). This version has never been started, so do test 1 first and stop if it fails; the 0.14.0 files are backed up.

**The test menu** (`Test Build Menu.cmd` in the server folder) now really buys the skills: be in the game, press a button, and the shrine shows those orbs as owned within a couple of seconds. It also has "Spawn 3 Turabadas", "Spawn 3 Birch Goteras" and "Remove test enemies": the enemies appear in a line in front of where you look and stand still.

## 1. First check: the game starts and a plain shot works
- **Skills:** Press "Sell all archery orbs".
- **Do:** Start the server, then the game. Nock a plain arrow by hand, draw fully, shoot a tree. Do it five times.
- **You should see:** Everything as normal: the string draws, the arrow flies and sticks.
- **Wrong if:** The game or server closes, the string will not draw, the arrow drops off the string, or nothing hits. Stop here and tell me.

## 2. Auto Dock: quick shots every time, and holstering
- **Skills:** Press "Auto Dock". Wear a quiver with ten or more arrows.
- **Do:** Fire ten quick shots as fast as the next arrow appears. Then let an arrow nock itself and put the bow on your back without shooting.
- **You should see:** The gold ring and a quick shot on every one of the ten, not every second one. After holstering, the arrow goes back into the quiver a moment later.
- **Wrong if:** Some shots come out as plain slow arrows although you hit the timing, or the arrow falls on the ground when you holster.

## 3. Holy Arrow holds to the end
- **Skills:** Press "Holy Arrow".
- **Do:** Nock one of your crafted wooden-shaft arrows and hold a full draw for eight seconds. Do it five times. Then five times with a plain arrow.
- **You should see:** Three thumps, the gold wind-up running to the end without restarting, and a Holy Arrow every time, with both kinds of arrow.
- **Wrong if:** The charge stops and starts over while you are still holding. Tell me which arrow it was.

## 4. Burst shows 30 arrows
- **Skills:** Press "Ricochet" (it includes Burst). For gold, also buy one Charge Shot orb by hand.
- **Do:** Draw fully, hold until the click at about a second and a half, look down the arrow, then shoot a wall from 10 metres.
- **You should see:** Ghost arrows with arrowheads. At the click, 30 arrows in two even rings round yours: ten close in, twenty further out, a small one exactly between each pair of big ones, all the same colour (gold if you own a Charge Shot orb). On the wall, two rings of hits.
- **Wrong if:** Any are blue beside gold, a small arrow hides behind a big one, a ring has a gap, you count fewer than 30, or they jump sideways when you let go.

## 5. Charge Shot hits harder than the line, and Shatter blasts
- **Skills:** Press "Shatter", then "Spawn 3 Turabadas". Later press "Piercing line" and spawn three more.
- **Do:** As the charge archer, hit the front Turabada with a full three-thump Charge Shot. As the quick archer, fire line shots at a fresh front one. Count shots to kill.
- **You should see:** The charged arrow kills in about three shots and throws a burst out of the creature's back: a flash, shards, about two dozen orange pellets fanning six metres behind, and the Turabada behind takes a heavy hit. The line takes about four shots and passes through all three.
- **Wrong if:** The charged arrow still needs five or six, the blast is hard to see or comes out of the front, nothing behind is hurt, or the line kills faster per shot than the charge.

## 6. Ricochet: every arrow jumps
- **Skills:** Press "Ricochet", then "Spawn 3 Turabadas".
- **Do:** Fire a full 30-arrow volley into the front one. Then shoot one that has another standing off to the side within about 15 metres.
- **You should see:** One thin arc for each arrow that lands, spreading over the other Turabadas, each arc a slightly different curve, and they take damage. The one off to the side gets arcs too.
- **Wrong if:** Only one or two arcs, all arcs go to one creature when three are near, or the game stutters when they appear.

## 7. With a friend: PvP, and sharing a bow
- **Skills:** Anything. Server PvP on.
- **Do:** Shoot your friend once with a plain arrow, then once with a charged one. Then fire ten fast quick shots with Auto Dock, hand them your bow, and have them hold a full Charge Shot and a Holy Arrow.
- **You should see:** Each arrow hurts once. On your bow, their charge and Holy Arrow work as on their own bow.
- **Wrong if:** Arrows do nothing, one arrow hurts twice, or their charge will not start on your bow.

Not in this guide but changed: the tablet's tilt (already live on your game), Shatter and Ricochet at half strength off your main path (buttons "Off-path Shatter" and "Off-path Ricochet"), the quiver in the Auto Dock try-out at the shrine, and +10% ghost damage per Charge Shot orb for a multi-arrow archer.
