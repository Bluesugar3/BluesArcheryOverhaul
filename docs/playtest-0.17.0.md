# Play-test 0.17.0 — the homing arrow (Seeking Volley)

Server and every player's game must be on 0.17.0 (send your friends the new file). The test menu has two new buttons, "Homing" and "Homing + Ricochet", and "Spawn 4 Turabadas".

This build has plain looks on purpose: green arrows and trails, simple green rings. The finished look comes after you have confirmed it works.

No 0.16 or 0.17 build has been started yet. If you have not done it, do test 1 of `playtest-0.16.0.md` first (five plain arrows, then three full charges). If the game or server closes, or a second charge does nothing, stop and tell me.

## 1. One lock
- **Skills:** Press "Homing".
- **Do:** Press "Spawn 3 Turabadas". Draw a full volley and keep your aim on the nearest one until a green ring closes on it and your bow hand pulses. Release roughly toward it.
- **You should see:** All the arrows leave green, spread out as usual at first, then curve in and land on that Turabada, with five extra arrows in the middle. Your real arrow does not fly; it ends up stuck in the creature.
- **Wrong if:** The arrows fly straight, your real arrow flies as well, or your real arrow stays hanging in the air by the bow.

## 2. Four locks
- **Skills:** As test 1.
- **Do:** Press "Remove test enemies", then "Spawn 4 Turabadas". Hold a volley and rest your aim on each one in turn until each has its ring. Release toward the group.
- **You should see:** Four green rings, the first one brighter. The volley splits between the four. Your real arrow ends up in the first one you locked.
- **Wrong if:** Earlier rings vanish while you lock the next, all arrows go to one creature, or a ring appears on something you never aimed at.

## 3. Losing a lock
- **Skills:** As test 1.
- **Do:** Lock one Turabada, then swing the bow well away from it for half a second and shoot at empty ground.
- **You should see:** The ring breaks with a sound, and the volley is an ordinary one with your real arrow flying.
- **Wrong if:** The arrows still curve back to the creature.

## 4. Cover
- **Skills:** As test 1.
- **Do:** Lock a Turabada, then step so a tree or rock hides it. Wait a second. Then lock it again in the open, step behind the rock and release straight away.
- **You should see:** The ring drops while the creature stays hidden. Released from behind cover, the arrows hit the rock.
- **Wrong if:** Arrows pass through the rock and hit the creature.

## 5. No lock means nothing changed
- **Skills:** As test 1.
- **Do:** Fire three ordinary volleys at a wall without resting your aim on any creature: one early, one at the click, one held longer.
- **You should see:** Exactly the volleys you had before this version: normal colours, your real arrow flying, Burst's 30 arrows at the click.
- **Wrong if:** Anything green, a missing real arrow, or arrows curving.

## 6. Homing with Ricochet
- **Skills:** Press "Homing + Ricochet".
- **Do:** With three Turabadas standing close together, lock one and release.
- **You should see:** The volley lands on the locked one and arcs jump on to the other two. One bigger arc for your real arrow, not five.
- **Wrong if:** Arrows skip off the ground while still curving, or five fat arcs leave the middle.

## 7. A friend watching
- **Skills:** You as in test 1; your friend anything, standing beside you.
- **Do:** Lock a Turabada about 25 metres away and release while your friend watches the creature. Then swap.
- **You should see (the watcher):** The same green arrows curving in and landing, and the shooter's real arrow stuck in the creature afterwards.
- **Wrong if:** The watcher sees a straight volley, a flying real arrow, or the arrow jumping from the bow to the creature.

One known gap to watch for: if the server disagrees with your lock (for example the creature moved a lot), your screen still shows the arrows curving while the server fires an ordinary volley, so the creature takes less than it looks like. Tell me if hits seem not to count.
