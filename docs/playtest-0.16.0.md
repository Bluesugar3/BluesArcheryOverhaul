# Play-test 0.16.0 — fixes, the gold hold, Shatter at 10 m, and timing

Server and every player's game must be on 0.16.0 (send your friends the new file). The test menu opens by itself when your game starts. Its buttons now freeze the server far less.

This version has not been started yet, so do test 1 first and stop if it fails. The 0.15.0 files are backed up.

Timing is switched on for this session on your server and your game. You don't need to do anything for it: just play, then quit the game and stop the server normally so the last minute gets written. I read the timing files myself afterwards.

## 1. First check: it starts and a plain shot works
- **Skills:** Press "Sell all archery orbs".
- **Do:** Start the server, then the game. Nock a plain arrow by hand, draw fully, shoot a tree. Five times. Then press "Charge Shot 3" and charge a full shot three times in a row.
- **You should see:** Everything as normal, and no hitch on the very first arrow you nock. All three charges wind up and thump, the third exactly like the first.
- **Wrong if:** The game or server closes, the string will not draw, the arrow drops, nothing hits, or the second or third charge does nothing. Stop and tell me.

## 2. Charge strands work without visiting the shrine
- **Skills:** Stay far from the archery shrine. Press "Charge Shot 3".
- **Do:** Draw and hold for three seconds, then shoot a creature. Then press five other buttons one after another while holding the bow, finish on "Charge Shot 3" and hold again.
- **You should see:** Three strands winding up with three thumps on the first draw after the button. After the swaps it still charges normally.
- **Wrong if:** Only the game's plain glow and no thumps, a charge look that gets stuck half-way, or charge effects on a build with no Charge Shot orbs.

## 3. Buy Auto Dock and Shatter at the shrine by hand
- **Skills:** Press "Sell all archery orbs". At the shrine buy Quickdraw 1, 2, 3 by hand. Keep at most seven skills in total across all professions.
- **Do:** Hold the Auto Dock orb and read the tablet. Shoot once from the try-out's quiver. Read the tablet again, then buy it. Later do the same for Shatter with Charge Shot 1 to 3: charge until the thump and shoot.
- **You should see:** The tablet first says "Try it first" with what to do. After the shot that line goes and the orb can be bought. With eight skills held it says so instead; with no points it says so.
- **Wrong if:** The orb still cannot be bought after trying it and the tablet gives no reason, or the reason does not match.

## 4. Shatter reaches 10 metres, and the dummy counts
- **Skills:** Press "Shatter", then "Spawn 3 Turabadas".
- **Do:** Hit the front Turabada with a full three-thump Charge Shot along the line. Then stand a creature behind the combat dummy and hit the dummy.
- **You should see:** A burst out of the creature's back and thick orange streaks fanning out to about 10 metres in a quarter of a second. The ones 3 and 6 metres behind are both hurt, the far one less. Through the dummy, the creature behind takes the blast.
- **Wrong if:** The streaks stop near 6 metres or look like thin dashes, only the nearest one is hurt, or nothing happens behind the dummy.

## 5. Multi-arrow: gold is earned by holding
- **Skills:** Press "Ricochet", then buy Charge Shot 1, 2, 3 by hand if you have room (sell Ricochet first if you are at eight).
- **Do:** Draw and release right at the click (about a second and a half). Then hold through one thump, two thumps, three thumps, each time into a creature.
- **You should see:** At the click all the arrows are their normal colour, with a faint gold creeping up the inner ring's shafts. First thump: gold runs round the inner ten. Second: the outer twenty. Third: everything brighter with a glint on each head. The longer you hold, the harder the volley hits. Released after one thump, the inner arrows fly gold and the outer ones don't.
- **Wrong if:** Gold from the start, a ring turning all at once, a thump you cannot hear or feel, or the same damage however long you hold.

## 6. Quick archer with Charge Shot orbs: weight you can see
- **Skills:** Press "Off-path Shatter" (Quickdraw 1 to 4 with Charge Shot 1 to 3).
- **Do:** Press "Spawn 3 Turabadas" and fire quick shots and a line shot at them (not at the combat dummy: the hit effect only shows on real creatures). Then pull and keep holding past the quick window.
- **You should see:** An orange ring of fire round the shaft behind the arrowhead, and a second thin ring that pulses when the shot is ready. On the hit: an orange flash, a thud, a sharp crack and an orange ring on the creature. Holding too long: the rings open and fade so you can see the weight let go.
- **Wrong if:** Nothing orange on the arrow, hits that look and sound like plain quick shots, or the ring just vanishing.

## 7. Everything else still works (quick pass)
- **Skills:** Use the buttons: "Auto Dock", "Ricochet", "Piercing line", "Holy Arrow".
- **Do:** Ten fast quick shots with Auto Dock. A full volley into the spawned Turabadas. A line through the dummy into a creature. Two Holy Arrows, one with a crafted arrow.
- **You should see:** As on 0.15.0: the next arrow nocks itself, every arrow that lands jumps to another creature, the line passes through the dummy, Holy Arrow arms every time.
- **Wrong if:** Anything that worked on 0.15.0 no longer does.
