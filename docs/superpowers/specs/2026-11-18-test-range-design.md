# Archery test range: design

Status: proposal for the owner to approve. Evidence (types, members, hashes, file:line) is in
`.superpowers/sdd/2026-11-18-homing/research-test-range.md`; this document cites it as [R§n].

The owner's request (2026-11-18): add to the archery test tools a real play-test area: a shelf with a ton of useful items,
unlimited arrows, different arrow tips to test damage, Goteras and different Turabadas that constantly respawn, combat dummies,
the archery shrine, unlimited XP for everyone on the test server, other ideas that help play-testing, perhaps a button that gets
rid of all the levels.

---

## Part 1. In plain words

### 1.1 What the range is

You stand where you want to shoot from, facing down-range, and press **Build test range** in the button window. Within two
seconds the server builds this around you (seen from above; you are `@`, looking up the page; distances in metres from you):

```
                         G1      G2      G3                  Gotera row, 30 m straight ahead, 5 m apart
                      (Gotera) (Birch)  (Ash)                (frozen turrets; Ash takes 30 hits of plain arrows)

     T4 (28 m)                                       H4 (44 m)
       T2  T3 (24 m)                              H3 (36 m)
         T1 (20 m)                             H2 (28 m)
   Turabada pack, 55° left                  H1 (20 m)
   (Shatter, Ricochet, pierce)              Homing lane, 55° right: four lone
                                            Turabadas 8 m apart, alternately 2 m
                                            left and right so each is in clear view
                         D3 (14 m)
                         D2 (11 m)          Dummy line, straight ahead:
                         D1  (8 m)          three combat dummies that never die (pierce, damage readout)

   [Bow & quiver shelf]   @   [Arrow shelf]   one step left / one step right of you, turned toward you
         1.6 m left    shooting   1.6 m right
                         line
```

- **Arrow shelf** (the game's 20-slot wooden `Shelf`): three slots of plain arrows (90 each), then one slot per tip:
  Spike Fancy in Copper, Iron, Silver, Mythril and Evinon Steel; Spike Tall in Iron and Mythril; Bone spike; Crystal shard;
  Explosive spike; Healing pod; Etheral and Ka Karimata arrows. Each slot is kept full (see "unlimited" in 1.3).
  What the tips do, from the game's own numbers: a plain arrow hits for 0.6; a Copper Spike Fancy 2.25; Iron 3.2; Silver 6.4;
  Mythril 8.0; Evinon 9.6 (the 9.6 your logs already show); Spike Tall Iron 2.25, Mythril 5.6; Bone 1.2; Crystal 3.25;
  Explosive 2 plus its blast [R§1.2]. Bows all shoot at the same speed in this game, so bow type only changes feel, not damage.
- **Bow and quiver shelf** (the 10-slot `Shelf Short`): two Bows, two Metal Bows, four Quivers each already holding 90 plain arrows.
- **Dummy line**: three combat dummies straight ahead. They have no health pool, so they never die: every hit is logged and they
  stay for pierce tests and damage numbers [R§3].
- **Turabada pack** (55° to your left, 20 m): one in front, two 4 m behind it slightly to each side, one 8 m behind on your line.
  The two behind sit inside Shatter's cone (60°, 10 m) behind the front one, the one on the line is what Quickdraw 4's line
  pierces into, and all four are inside Ricochet's 15 m jump.
- **Homing lane** (55° to your right): four Turabadas at 20, 28, 36 and 44 m, each in clear view, so a lock can be made on each.
- **Gotera row** (30 m straight ahead, behind the dummies): plain Gotera (never knocked down), Birch and Ash (both knocked down;
  Ash is the tough one).
- Spacing rule: every target of one lane is more than 15 m from every target of another lane, so a Ricochet jump, a Shatter
  blast or a homing lock in one lane never touches another. Inside a lane they are close on purpose.
- Kinds per lane are settings, so the Turabada pack can be four Iron Turabadas or a mix (Turabada 5 health, Short 3, Large 7,
  Copper 5, Iron 15, Gold 15, Silver 20, Mythril 30; Gotera 5, Birch 15, Walnut 15, Ash 30, Redwood 50) [R§3].
- **Respawning**: a test creature that dies comes back at its own spot after `RespawnSeconds` (5 s). Frozen ones stand still
  (they still flinch, fall over and die). Free ones move and attack; one that strays more than 8 m from its spot is put back
  (removed and spawned again at its spot), so they come at you but never reach you.
- **Where**: anywhere you press the button, but it is meant to be pressed at the archery shrine (Archers Refuge, chunk 18-38):
  the shrine is the real one, so buying and trying orbs work as always, and there are already combat dummies about 15 m from it.
  The range does not spawn a second shrine (1.3).

### 1.2 The buttons

Everything is in the existing window `tools\TestBuildMenu.ps1`, in a new "Range" group. Each button writes one request number
into the server's `QuickshotRing.cfg`; the server acts once per new number, as today. Cost marks: cheap = an hour or two,
moderate = half a day, expensive = a day or more with play-testing.

| Button | What it does exactly | Cost |
|---|---|---|
| **Build test range here** | Builds 1.1 around `[TestBuild] Player` (you), facing where your head faces. Refused if a range already exists (remove it first), if you are not online and loaded, or if the ground is unsuitable (too steep, a roof, another player in the way); the refusal says which piece and why. Pieces that cannot be placed are skipped and named in the log. | moderate |
| **Remove test range** | Removes the shelves, what is still on them, the dummies and every test creature. Arrows players carry are left alone (decision 3). | cheap |
| **Enemies: frozen / free** | Switches every test creature between standing still and moving/attacking, at once and for later respawns. | cheap |
| **Respawn: 3 s / 10 s / 30 s / off** | Sets how long a dead creature stays dead before it comes back. Off = they stay dead until the range is rebuilt. | cheap |
| **Refill shelves** | Tops every slot back to full now (the server also does it by itself every 2 s). | cheap |
| **Everyone gets points: on / off** | On: every player on the server keeps 2 unspent archery points (one below the game's cap of 3, because at 3 the game pays no experience), topped up whenever they spend, join or the setting reloads. Off: stops topping up; points already given stay. | cheap |
| **Buy preset: for me / for everyone** | The existing buy buttons (Auto Dock, Ricochet, Shatter, …) get a "who" switch. "Everyone" runs the same really-buy for each online, fully loaded player in turn; anyone not ready is skipped and named. | moderate |
| **Reset archery progress (named player)** | Sells every archery orb (the vanilla ones too), sets the archery level, experience and points to 0, forgets their main path and what they practised, and saves. Asks "are you sure" in the window first. | moderate |
| **Kill all test enemies** | Sets every test creature's health to 0 (they die normally and respawn if respawn is on). Why: watch the respawn, clear a lane for a clean test. | cheap |
| **Heal me** | Puts your health back to full. Why: free enemies and Goteras hurt. Food: not known yet how to fill it [R§6]. | cheap |
| **Noon** | Sets the time of day to 12:00 for everyone. Why: effects and lines are judged in daylight. | cheap |
| **Teleport to the archery shrine** | Moves you to the shrine's spot (-835.6, 214.3, 386.9). Why: one press from anywhere to the place the range is meant for. | cheap |
| **Collect loose arrows** | Removes range arrows lying on the ground or stuck in things (not those in someone's hand, quiver or bag). Why: a long session leaves hundreds of networked arrows that cost every VR client. | moderate |
| **Last shot readout** (a panel, not a button) | The window reads the server's log and shows the last release: arrows that landed, damage per creature, stagger, Ricochet jumps, Shatter catches, locks claimed and kept. Why: today you read `Latest.log` by hand after each shot. Window-only, no mod change. | cheap |
| Damage numbers over dummies | Floating numbers on the dummy you hit. Why: see damage without the readout. Needs client-side drawing and the client does not know the damage (the server decides it), so it needs a new idea or new traffic. | expensive |
| Lend every orb to everyone | `[TestBuild] Orbs` (already there, never saved, ignores the 8-skill cap) for every player instead of one. Why: try builds the cap forbids. | cheap |
| Snapshot / restore a player's archery | Saves the owned orbs, level, points and main path to a file before a reset or buy, and puts them back on request. Why: an undo for Reset. | moderate |

Recommended first: **the last-shot readout** (cheap, no mod change, helps every test from today) and **step 1 of the range** (1.6).

### 1.3 What it will not do, and the honest limits

- **No second shrine.** The game's shrine is one huge saved object whose profession comes from the world save; a spawned one
  starts as a Forging shrine and its tree model is set before the mod could change it. Making it a working archery shrine is
  untested surgery, and two archery shrines confuse the tablet. Build at the real shrine instead [R§4].
- **"Unlimited" means refilling.** The game has no bottomless shelf or dispenser [R§2]. The server tops each slot back up; you can
  empty a slot faster than 2 s, and every arrow taken is a real networked object until removed.
- **The 8-skill cap stays.** It is read by every client too (the shrine's crystal ring has 8 holders), so raising it on the server
  breaks the shrine's display. To test a build bigger than 8, use lending (never saved, ignores the cap) [R§5].
- **"Unlimited XP" is 2 spare points, not a high level.** The game stops paying experience at 3 banked points and a level is only
  "points earned". Keeping everyone at 2 spare points means anyone can always buy the next orb. For fast experience from shooting,
  the game's own server setting `RangedXPX` does that without the mod.
- **No pen.** The game has no pen and spawned furniture is not known to block creature pathing; the 8 m put-back is the pen.
- **Loot.** A killed creature drops its normal loot, which is saved into the world like any loot. A respawning Turabada lane makes
  ore pile up over a long session. Version 1 leaves it (disposable server); a "clean drops" step is listed in 1.6 as optional.
- **Not Wyrms or Spriggulls.** They have no stagger pool, so the mod's Ricochet, Shatter and target list ignore them [R§3].
- **The range vanishes if nobody is near it.** The game removes never-saved objects when their area unloads; the log says so and
  you press Build again.

### 1.4 Safety

- **Nothing of the range is saved into the world.** Shelves, dummies, creatures and every arrow on the shelves are spawned
  "never save" and, for items, "never save after being picked up or dropped" too (the game otherwise flips a dropped item back to
  saved: [R§1.3]). The exception, by your decision 3: range arrows a player puts in their own quiver or bag are kept by that player.
- **Removed by**: the Remove button; the server shutting down; the last player leaving the server; and the game itself when the area
  unloads. Each removal is one log line naming why.
- **Never acts by surprise**: a request number the server started with is never acted on; a press while the server is still
  starting is recorded and ignored; every tool refuses unless `[TestRange] Enabled = true` in the server's settings (default
  false), so a normal server never builds anything even if someone copies the window.
- **"Everyone" touches other players' saves**: Everyone-gets-points raises each player's unspent archery points (saved); Buy for
  everyone really sells and buys their archery orbs (saved); Reset wipes one named player's archery progress (saved). Each player
  touched gets one loud log line on the server: `[Archery] TEST RANGE: '<name>' …` with what changed, before and after.

### 1.5 Cost

- Networked objects added by a full range: 2 shelves, about 20 stacks on the arrow shelf (a crafted arrow is 2 objects: shaft and
  tip), 8 bows and quivers, 3 dummies (each a jointed body that swings), 11 creatures: **about 70 objects**. Each arrow taken out
  and shot adds 1 or 2 more until collected.
- Server: respawn and refill checks run twice a second over a list of ~35 records: negligible. A free creature runs the game's own
  AI (11 at most). A dummy's swinging body is physics on the server.
- VR clients: comparable to standing in a town with a dozen creatures. The real risk is loose arrows; hence a cap
  (`MaxLooseArrows` 200: the oldest loose range arrow on the ground is removed when a new one would pass it) and the Collect button.

### 1.6 The build in steps (each can be tried on its own)

1. **Smallest useful range**: the arrow shelf with refilling plain arrows and four crafted arrows (Spike Fancy Iron and Mythril,
   Bone, Crystal), three combat dummies on the line, three respawning Turabadas in the pack spots; Build, Remove, Refill; the master
   switch. Proves: spawning furniture, filling and refilling docks, assembling crafted arrows, respawning, removal.
2. The rest of 1.1: all tips, the bow and quiver shelf, the Gotera row, the homing lane, lane kinds as settings, placement checks.
3. Frozen/free switch with the 8 m put-back; respawn-time buttons; Kill all.
4. Everyone gets points; Buy for everyone.
5. Reset archery progress, with Snapshot/restore as its undo.
6. Small helpers: Heal me, Noon, Teleport to the shrine, Collect loose arrows, the loose-arrow cap.
7. Optional: clean up loot dropped by test creatures; lend every orb to everyone.

The last-shot readout can be built at any time; it only changes the window.

### 1.7 Open decisions (my recommendation first)

1. **Where it builds.** (a) Anywhere you press it, meant for the archery shrine (recommended: it also works on flat ground away
   from players), or (b) only within 60 m of the archery shrine, refused elsewhere.
2. **Free enemies.** (a) Put back at their spot after straying 8 m (recommended: moving targets that never reach you), or
   (b) no leash: they hunt you, and you heal or kill them by button.
3. **Range arrows a player pockets.** (a) Theirs to keep (recommended: disposable server, arrows are harmless), or (b) tracked and
   destroyed wherever they are when the range is removed (more code; arrows vanish from quivers).
4. **"Unlimited XP" for everyone.** (a) Two spare points kept topped up (recommended), or (b) that plus every orb lent to everyone
   beyond the cap (never saved; every player's bow then behaves as a full build, which may not be what other testers want).
5. **Reset scope.** (a) Archery only, one named player (recommended), or (b) archery for everyone at once.

---

## Part 2. Technical

### 2.1 Settings (server, `[TestRange]` in `UserData\QuickshotRing.cfg`; none shared)

Same pattern as `TestTools.cs`: request entries hold a number the window writes; `TestRequest.IsNew` against the number seen at
`Started()`; a press while not yet the server is recorded and ignored.

| Entry | Default | Meaning |
|---|---|---|
| `Enabled` | false | Master switch. False: every request is logged as refused and nothing is built or touched. |
| `BuildRequest`, `RemoveRequest`, `RefillRequest`, `KillRequest`, `HealRequest`, `NoonRequest`, `TeleportRequest`, `CollectRequest`, `ResetRequest`, `SnapshotRequest`, `RestoreRequest` | "" | One action per new number. Heal/Teleport act on `[TestBuild] Player`; Reset/Snapshot/Restore on `[TestRange] ResetPlayer` (empty = `[TestBuild] Player`). |
| `ResetPlayer` | "" | The player Reset acts on (captured at the press, as BuyRequest). |
| `Frozen` | true | Live. All test creatures frozen (true) or free. |
| `RespawnSeconds` | 5 | Live. 0 = no respawn. |
| `LeashMetres` | 8 | Live. A free creature farther than this from its post is put back. 0 = no leash. |
| `ArrowShelf` | `Arrow x90; Arrow x90; Arrow x90; Spike Fancy/Copper x30; Spike Fancy/Iron x30; Spike Fancy/Silver x30; Spike Fancy/Mythril x30; Spike Fancy/Evinon Steel Alloy x30; Spike Tall/Iron x30; Spike Tall/Mythril x30; Small Bone Spike x30; Crystal Shard Blue x30; Explosive Spike x30; Healing Pod x15; Etheral Arrow x30; Ka Karimata Arrow x30` | One entry per slot: a prefab name (plain) or `Head/Material` (crafted on a wooden shaft), and the target count. |
| `GearShelf` | `Bow; Bow; Metal Bow; Metal Bow; Quiver+Arrow x90; Quiver+Arrow x90; Quiver+Arrow x90; Quiver+Arrow x90` | Slots of the bow and quiver shelf; `Quiver+X xN` = a quiver filled with N of X. |
| `Lanes` | `Dummies: combat_dummy @0 8,11,14 | Pack: Turabada @-55 20,24L,24R,28 | Homing: Turabada @55 20,28L,36R,44 | Goteras: Gotera,Gotera Birch Variant,Gotera Ash Variant @0 30row5` | Lanes: name, kinds (one, or one per spot), bearing in degrees (right positive), spots (metres; `L`/`R` = 2 m to that side; `rowN` = across, N m apart). |
| `MaxLooseArrows` | 200 | Loose range arrows kept before the oldest is removed. |
| `EveryonePoints` | false | Live. Keep every online player's unspent Ranged points topped up (`TestBuildRule.TopUpTo(2, cap)`). |
| `BuyFor` | `Player` | `Player` or `Everyone`: who the existing `BuyRequest` acts on. |
| `MaxSlopeDegrees`, `MaxStepMetres`, `PlayerClearMetres` | 35, 3, 2 | Placement checks. |

The window (`TestBuildMenu.ps1`) gets the Range group, writes these, and adds the readout panel (tails
`<server>\MelonLoader\Latest.log` for `[Archery] Hit:`, `Weight:`, `Ricochet`, `Shatter`, `Lock` lines of the last release id).

### 2.2 Server state (`TestRange.cs`, server only; nothing static per shot)

- `Range`: origin, yaw, owner name, build time, list of `Piece` (shelf, dummy, creature post) and the loose-arrow ring.
- `Post` (one per creature spot): kind hash, world position, facing, the live `NetworkEntity`/`Agent`/`HealthObject`, state
  (`Alive`, `DeadSince`, `GoneSince`), frozen-applied flag, spawn count, last spawn time.
- `Slot` (one per shelf dock): the `PickupDock`, the wanted item (plain hash, or head hash + material), target count, last refill.
- `Tracked` items: every pickup spawned by the range or undocked from a range dock (from `PickupDock.ItemUndocked`), with
  `Prefab.SaveMode = Ignore` and `Pickup.BaseSaveMode = Ignore`.
- Poll from `Main.OnUpdate` before the headless early return, as `TestTools.Poll`; the expensive parts every 0.5 s.

### 2.3 What each piece depends on (from [R]; verified in source unless marked)

| Piece | Game members | Verified |
|---|---|---|
| Spawn furniture, dummies, creatures | `SpawnHelper.Spawn(prefab, SpawnData.Default, chunk, pos, rot, preChunking)` (SpawnHelper.cs:99-113); `LocationChunkHelper.GetBestContaining` | yes; hashes run-time-check |
| Never saved | `NetworkPrefab.SaveMode = Ignore` (DynamicChunkContentManager.cs:286-291, :508); items also `Interactable.BaseSaveMode = Ignore` (Interactable.cs:105; PickupDock.cs:1271-1277) | yes |
| Fill a dock | `SpawnHelper.SpawnDocked(hash, n, SpawnData.DontGenerate, dock)` (SpawnHelper.cs:198-226); `PickupDock.QuantityStored`, `MaxQuantityCurrentlyPossible`, `DestroyAmountToEmpty`, `ItemUndocked` (PickupDock.cs:135-203, :409) | yes; stacking crafted arrows UNVERIFIED |
| Crafted arrow | `SpawnUnchunked`; `ISpawnArguments.CheckArguments({"forged", material})` (PhysicalMaterialPart.cs:178, ForgedModel.cs:418); `JointSlot.Supports`/`TryConnect` (JointSlot.cs:71, :115) | yes; head acceptance per kind UNVERIFIED |
| Damage numbers quoted | `ImpactTool.MaxDamage`, `ImpactGenerator.HealthDamageModifier`, `PhysicalMaterialPart.SetMaterial` (ImpactTool.cs:156, ImpactGenerator.cs:34, PhysicalMaterialPart.cs:68-96) | yes (Evinon Fancy matches the log) |
| Shelf docks | `Shelf` 23630: 20 × `Shelf Storage` (3 stacks, tag All); `Shelf Short` 20452: 10 | prefab data; pivot/footprint UNVERIFIED |
| Freeze / free | `Agent.IsEnabled` (Agent.cs:98-121) | yes (TestTools uses it) |
| Death, kill | `HealthObject.Killed`, `OnKilled`, `Die()` (HealthObject.cs:183, :339-363); `Agent.IsAlive` | yes; body removal and drops UNVERIFIED |
| Put back (leash) | `NetworkEntity.SceneDestroy()` + spawn at post | yes (no teleport of creatures) |
| Points for everyone | `PathExperience.freePoints` + `UpdatedPoints` as `TestBuild.TopUp` (TestBuild.cs:134-154); `Player.AllPlayers` | yes |
| Buy for everyone | `TestTools.Gate`/`Buy` per player (TestTools.cs:160-321) | yes (existing) |
| Reset | `PathExperience.LockSlot(slot, false)`, private `currentLevel`/`currentExperience`/`freePoints`/`practicedSlots` (PathExperience.cs:15-23); `pm.UpdateCounts`, `EndPractice`; `Builds.BeginBatch/EndBatch`; main path file in `Paths` | yes; forgetting the main path needs a small `Paths` entry point |
| Heal | `HealthObject.Health = MaxHealth` on `PlayerHealth` (HealthObject.cs:115-135) | yes; food UNVERIFIED |
| Noon | `GameTimeManager.Instance.RushToTime(0.25f)` (GameTimeManager.cs:325; TimeCommandModule.cs:16-36) | yes |
| Teleport | `PlayerController.ForceTeleportTo(Vector3)` (WorldCommandModule.cs:308) | yes |
| Removal on quit / last player | `TestTools.Shutdown` path; `Player.AllPlayers` empty | yes |

### 2.4 Unity-free rules and their tests (`Range.cs`, compiled into `QuickshotRing.Tests`)

- `RangeLayout.Parse(lanes)` and `RangeLayout.Spots(originX, originZ, yawDegrees)`: world x/z and facing of every piece.
  Tests: bearing 0 puts the dummy line straight ahead; `@-55` is to the left; `L`/`R` offsets are perpendicular to the lane;
  `rowN` spacing; the shelves at ±1.6 m face the origin; creatures face the origin; yaw 90 rotates everything; a bad lane string is
  refused with its reason; **the spacing check**: with the default lanes every cross-lane pair is more than 15 m apart, and a lane
  string that breaks it is reported (warning, not refusal).
- `PlacementRule.Check(slopeDeg, stepMetres, nearestPlayerMetres, overlap)` -> null or a reason.
- `RespawnRule.Next(state, now, respawnSeconds, leashMetres, distanceFromPost, frozen)` -> Wait / RemoveBody / Spawn / PutBack.
  Tests: dead waits `RespawnSeconds`; 0 never respawns; gone (unloaded) is not respawned while no player is near; a frozen
  creature is never put back; a free one past the leash is; a creature that died during its put-back is not doubled.
- `RefillRule.Need(stored, target, kindMatches)` -> how many to add, or "replace" when the dock holds the wrong kind (a player put
  something else there) -> leave it (never destroy a player's item) and log once.
- `ShelfEntry.Parse("Spike Fancy/Mythril x30")`: plain vs crafted, material names, counts clamp to 1..90.
- `EveryoneRule.Who(players)`: online, save read, body, loaded state; skips and names the rest (reuses `BuyGate` facts).
- `ResetRule.Plan(owned, deps)`: leaves first; level/experience/points end at 0; refused when the player is not fully loaded.
- `LooseCap.Evict(list, max)`: oldest loose (not held, not docked) first.

### 2.5 Logging (every line starts `[Archery] TEST RANGE: `)

Build (origin, yaw, each piece placed or skipped with reason), Remove (count, why), each respawn and put-back (Summary level),
each refill batch (count), each player touched by Everyone/Buy/Reset (before and after), refusals with the request number,
the first run's prefab hash -> name check for every kind used.

## Owner's decisions (2026-11-18)

The owner answered one of the open decisions: "Only near the Archer Shrine."

1. **Where it builds: only near the archery shrine** (the owner's words; not the design's first option). "Build test range" is refused with a plain message unless the named player stands within a set distance of the Ranged shrine (`[TestRange] ShrineMetres`, 60 as in option (b) of 1.7). No second shrine is spawned.
2. **Free enemies: put back after straying 8 m** (NOT answered: the owner said "Ignore what I mean by go back straight", so this is the design's recommendation, the controller's call).
3. **Arrows in a player's own quiver: theirs to keep** (not answered; the design's recommendation, the controller's call).
4. **"Unlimited XP": two spare points kept topped up for everyone** (not answered; recommendation, the controller's call). Lending every orb stays a separate button.
5. **Reset: archery only, one named player, with a confirmation** (not answered; recommendation, the controller's call).

Build order stands: the last-shot readout, then range step 1 (which proves server-assembled crafted arrows in a shelf slot before anything depends on it). Builds after batch H3.

**Go-ahead (owner, 2026-11-18): "Build these features in Opus agents."** Built in a separate copy of the source (`C:	mpttmod-range`, base = the 0.17.1 tree) while batch H3 edits the main tree; merged afterwards by the controller. Steps 1 to 6 of 1.6 and the last-shot readout; step 7 not built.
