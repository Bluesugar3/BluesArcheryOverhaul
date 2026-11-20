# Fact-check: vial arrows, potions and special arrows

Date: 2026-11-16. Read-only check of `docs/research/2026-11-16-arrows-potions-feasibility.md` (called "the report" below). Nothing was built, deployed or run. Sources:
- `_decomp/` (game source).
- My own decompiles, made for this check under `%TEMP%\claude\...\scratchpad\research-check\`: installed `<client>\Mods\SyncLib.dll`, `CookingLib.dll`, `CustomRecipesAPI.dll`, `CustomDistributionAPI.dll`, `PortalGun.dll`, and the ATT Workshop Utilities DLL.
- GitHub head of both libraries: CustomRecipesAPI commit `16aa565`, `Core.cs`; CookingLib `5c855d2`.
- The exported project (`C:\ATTUnity\ExportedProject`, private: only field values are cited here).
- Both `MelonLoader\Latest.log` files.

`<client>` = `C:\Users\dmand\OneDrive\Desktop\A Township Tale (client`. Prefab paths are relative to `ExportedProject\Assets\Resources\network prefabs\`.

## 1. Key claims and their marking

| # | Report claim | Mark | Evidence |
|---|---|---|---|
| 1 | Moulds take molten ingots only, so a sandstone mould cannot work | **CONFIRMED (code-enforced)** | `_decomp/MouldDefinition.cs:18` is only a header. The real gate is `Alta.Blacksmithing/Smelter.cs:316-326` (`CheckMould`): the input needs an `Ingot` component, must be in `AllowedMaterials`, and its `PhysicalMaterial` must be unlocked on that smelter. Moulds are used at the smelter, not the anvil. |
| 2 | A smelter recipe through CustomRecipesAPI can turn Sandstone Stone into a new item | **CONFIRMED in code, with two catches the report missed** | `Smelter.FindRecipe` (`Smelter.cs:254-300`) accepts any `Item`s whose input set matches exactly. Output is spawned with `SpawnHelper.Spawn(item.Prefab, …)` (`:394`), so a non-ingot output is fine. `itemsToAddToSmelter` adds Sandstone to the ore dock (CRAPI `Core.cs:103-107`). **Catch 1:** `SetUpRecipes` fires in `OnLateInitializeMelon` (`Core.cs:79-92`), before any SyncLib item exists, so the Empty Vial cannot be the output there. CRAPI's own GitHub comment says the same (`Core.cs` head, line 71). Call `SetUpSmeltingRecipeThroughCode` yourself from SyncLib's `RegisterPrefabs` handler instead. **Catch 2:** by default the recipe is added only to upgrade set 5674, the *simple-server* default (`Core.cs:206-214`). A smelter on a non-simple server unlocks `defaultUpgrades` instead (`Alta.Blacksmithing/SmelterUnlockManager.cs:31-46`; simple = `ServerInfo.Target & 2`, `Alta.Networking/NetworkScene.cs:155-158`). The recipe may simply not appear. Pass the smelter's default upgrades too. |
| 3 | Alternative: a chisel recipe (CustomRecipesAPI) | **WRONG for sandstone** | The chiselling deck is carpentry. It cuts a `TimberBlock` made from a docked **wood wedge** (`Alta.Carpentry/BuilderDock.cs:175`, `wedgeItem`; `ChisellingDeck.cs:165-168`). A chisel recipe cannot consume Sandstone. ("Chisel Sandstone Distribution" is a separate loot table.) |
| 4 | Arrow shaft (29772) has Slot Feather/Grass/Deco; Slot Deco accepts only Insert Deco | **CONFIRMED** | `props/combat/Arrow Shaft Wooden.prefab` lines 137/174/245, hash at 358. `MonoBehaviour/Slot Deco.asset` `allowedInserts` lists only Insert Deco. `JointSlot.Supports` checks `AllowedInserts` (`_decomp/Alta.Crafting/JointSlot.cs:71-74`). |
| 5 | Items with Insert Deco: spikes, bone spike, Crystal Shard Blue, Explosive Spike, Healing Pod, pommels, pole huggers | **CONFIRMED** | 13 prefabs reference the Insert Deco guid. The ArrowAssembler JSON (`<client>\Mods\AssemblerDisassembler.recipes.json:38-62`) whitelists 7 arrowhead heads, including 50412 and 44360. |
| 6 | Grass Clump and Buran Resin carry Insert Grass | **CONFIRMED** | Only those two prefabs reference the Insert Grass guid. |
| 7 | SyncLib resolves the Workshop insert by Slot-name, misses "Insert Deco", keeps item 42570's type, so the head "probably" won't dock | **CONFIRMED, and it is certain, not probable** | Installed `SyncLib.Items/WorkshopApplier.cs:121` looks up a `JointInsertType` named from the Workshop enum `JointSlotType` (`WorkshopUtil/Enums.cs:363-389`: `SlotDeco` becomes "Slot Deco" via `NetworkPrefabRegistry.cs:264-276`). No `JointInsertType` asset has a "Slot …" name (`Insert Deco` ≠ `Slot Deco`, and they are separate classes: `_decomp/Alta.Crafting/JointInsertType.cs`, `JointSlotType.cs`). So every SyncLib insert keeps the template's type. The template is Flint (item 42570 → prefab 39484, `props/mining/Flint.prefab:985`), whose type is **Insert Primitive SwordType**. A mod head will never fit an arrow without the fix-up. The fix-up is one reflection write of `JointInsert.type` = the "Insert Deco" asset. |
| 8 | Server can apply a liquid to anything with health via `LiquidData.Apply` | **WRONG as stated** | `LiquidData.Apply` is server-only and null-safe (`Alta.Liquid/LiquidData.cs:150-168`). But many creatures' `HealthObject.effectHandler` is **null**, so nothing happens to them. Null on: all Turabada variants, Dais (deer), Greater Gotera, Death Fern, Mother Fern, Vine Attack Fern (each prefab's `effectHandler: {fileID: 0}`). Turabada's own EffectHandler also has `health: 0`, so `PoisonEffect.CheckApply` skips it (`Alta.Alchemy/PoisonEffect.cs`). |
| 9 | "Every creature and player has an EffectHandler" | **WRONG** | 31 prefabs carry one (players, Babu, Gotera + wood variants, Wyrm, Crystal Wyrm, Spriggull, Phantom, Phantom Head, Spectral Ghost, Turabada). Of those, only players, Babu, Gotera, Wyrms, Spriggull, Phantom and Spectral Ghost are reachable *and* working. Immunities (`immuneTo`): Phantom 3 = **poison + teleport**, Wyrm 2 = teleport, Babu 2 = teleport. |
| 10 | 13 existing effects (list) | **CONFIRMED** | 13 assets in `cooking/effects/`, hashes as listed. CookingLib exposes 11 aliases (`CookingLib/EffectNames.cs`). |
| 11 | Slow/weaken/expose/glow by negative-strength buffs ("easy, needs testing") | **WRONG for most hostiles** | `IndirectStatModifierDefinition.Apply` and `StatEffect.Apply` call `target.StatManager…` with no null check (`Alta.StatSystem/IndirectStatModifierDefinition.cs`, `Alta.Alchemy/StatEffect.cs`). `statManager` is null on Gotera (except Redwood), Wyrm, Crystal Wyrm and Turabada, so these effects throw there. Only Phantom, Babu, Spriggull and Redwood Gotera have a StatManager. |
| 12 | Poison applied once never wears off by itself | **CONFIRMED** | `EffectHandler.PoisonOverTime`/`RunPoison` (`EffectHandler.cs:118-168`): it loops while health > 0 and stops only on a negative application. Area triggers undo it on leave *and* on disable (`_decomp/VolumeTrigger.cs:31-60`). |
| 13 | Thrown potions already shatter and splash | **CONFIRMED (code path)** | `props/alchemy/Potion Medium.prefab:1026` `breakVelocitySquared: 64`; break calls `SpawnAndColorParticles`. `ImpactDetector.HandleParticleAsLiquidCollision` applies the liquid at `SplashParticlesStrength` to whatever health the particles touch (`Alta.Impact/ImpactDetector.cs:339-349`). There is **no radius parameter**: reach is wherever pooled particles collide. Whether a headless server simulates them is unverified. |
| 14 | Effects and explosions skip the PvP setting | **CONFIRMED** | The PvP gate is only for `ImpactHitWithTool` by a player (`PlayerHealth.cs:248-257`). `Explosive.Trigger` builds plain `ImpactHit`s (`Explosive.cs:179-197`). Poison uses `ReceiveDamage(…, DamageSource.Poison)` (`EffectHandler.cs:166`). Defaults 0.1 / 0.3 (`Alta.Utilities/ServerSettings.cs:50,55`). |
| 15 | Death Fern is a creature, hash 37642, drops nothing, releases a poison cloud on death | **CONFIRMED** | `creatures/reactive hazards/Death Fern.prefab:292`. Its only spawner prefab is the cloud (`:748`, a guid that resolves to `geode/Death Fern Poison Cloud.prefab`). The cloud fires from `destroyed` (`:347`) **and** from a sense event (`:607`, the gas attack). `DeathFern.Killed` (`Alta.Intelligence/DeathFern.cs`) is patchable. |
| 16 | Poison cloud "about 10 m across", lifetime unverified | **WRONG size** | Trigger `m_Radius: 10` at scale 1, so **20 m across**. A timer component has `time: 16` (lines 245-259), so about 16 s. Poison over Time at 0.2. An arrow version would hit the shooter and everyone nearby. |
| 17 | Death Fern overworld locations unknown | COULD NOT VERIFY | Same as the report. |
| 18 | Phantom drops only Phantom Guard at 1% (distribution 52318) | **CONFIRMED** | `distributions/Phantom Drop Distribution.asset`: `nullChance: 0.99`, one topic = `crafting/aeshetic/guards/Phantom Guard.prefab`. Used by Phantom and Phantom Head. The spawner fires from `HealthObject.destroyed` (`Phantom.prefab:1462-1538`). |
| 19 | Phantom "counts as killed" when it fades at dawn | **CONFIRMED, refined** | `Phantom.FadeOnDay` calls `Killed(null)` directly (`Alta.Intelligence/Phantom.cs:295-307`). `Target.Killed` is normally driven by `HealthObject.Destroyed` (`Target.cs:52`), so the vanilla drop spawner does **not** fire at dawn. A `Killed` postfix needs the null check. The distribution route (CustomDistributionAPI) is dawn-safe by construction. |
| 20 | The user's three-armed bone piece "may be" the Phantom Guard | COULD NOT VERIFY | I have no image. Plausible: the Phantom Guard prefab has `Phantom_orb` + `Phantom_orb_ring` meshes and a "Slot Multi" (an orb socket), and it is the only phantom item. |
| 21 | The server can teleport a player anywhere via the existing `TeleportRequest`, no new traffic | **CONFIRMED** | `Alta.Alchemy/PlayerEffectController.cs`: server branch `SendToPlayer(TeleportRequest)`; the owning client runs `LocomotionController.TeleportTo` + `WaitForChunkLoad`. Correction: the player override **ignores `immuneTo`**, and the 2 s cooldown applies only on the client. `PortalGun.dll` (caleb, in `Mods\New folder`) moves players client-side, which is a working precedent. |
| 22 | Vanilla Teleport Potion = Teleport Home + Reveal; creatures go to the respawn area | **CONFIRMED, plus one omission** | `Alta.Alchemy/TeleportEffect.cs`. It is a `ChanceBasedEffectDefinition`: **strength is a probability** (`Random.value < strength`). As a splash (0.3) it fires 30% of the time per application. A creature hit by it is teleported to the town respawn point, which is a griefing vector. |
| 23 | Only the player prefab can take fire damage; BurnEffect/BurnStat unused; buildings have nothing that burns | **CONFIRMED** | `HeatManager` fields appear only in `player/VR Player Character New.prefab`. No asset has `fireTick`. The only `IFlammable`s are `Fire` and `Fuse`. |
| 24 | Grass-clump fire arrow: ignites at 60°, heat area outer radius 0.5 m | **CONFIRMED** | `props/mining/Grass Clump.prefab:829,961`. |
| 25 | `Fire.SimulateSparks` lights flammables in a sphere | **CONFIRMED** | `Alta.Heat/Fire.cs:600-617`. |
| 26 | Explosive Spike: 1.5/1.5, full to 0.5 m, zero at 1 m, force 15, fires when the head breaks | **CONFIRMED** | `Explosive Spike.prefab:1562-1567`, `onServerBreak → Trigger` (`:631-649`), break at `breakVelocitySquared 64` (`:628`). The mesh is `Turabada_Explosive_Shard_01`. |
| 27 | Healing Pod and Firework blast force "—" | **WRONG (minor)** | Both are 15 (`Healing Pod.prefab:943`, `props/Firework.prefab:6478`). The Healing Pod also deals 1 cripple, i.e. it staggers the people it heals. |
| 28 | Firework: 4 s fuse, 0.2 damage, 3.5/4 m, no joint insert, cannot go on an arrow as it is | **CONFIRMED** | `props/Firework.prefab:1296, 6473-6476`. No Insert/Slot children. Item under `items/items/themed/`. How it is obtained: COULD NOT VERIFY. |
| 29 | CookingLib can only use the game's effects; no new status effects | **CONFIRMED for CookingLib's API, misleading for the project** | `EffectNames.Need` refuses non-game effects (`CookingLib/EffectNames.cs`). But the arrow mod does not need CookingLib to apply anything: it can subclass `EffectDefinition` or simply run its own server timer. |
| 30 | OverSeconds only for Heal/Nourish, strength within ±100 | **CONFIRMED** | `CookingLib/EffectSpread.cs`, `LiquidFactory.cs:119`. |
| 31 | "0.2 has not run on a live server" | **CONFIRMED** | GitHub README. The README also says server-only liquids and **infusions need nothing on players**. |
| 32 | CookingLib 0.2.1 = GitHub head `5c855d2`; MIT; 7 commits; no releases | **CONFIRMED** | `CookingLib/Properties/AssemblyInfo.cs`; GitHub API: last commit 2026-10-23. |
| 33 | CustomRecipesAPI 1.0.0 `cda998a`, MIT, 20 commits, head "dominion", has a Smelter Autodock tag the DLL lacks, nothing for cauldrons/liquids | **CONFIRMED** | `AssemblyInfo.cs`. GitHub API: head `16aa565` "dominion", 2026-11-17T02:11Z (UTC). Head `Core.cs:86,188-191` has the autodock tag. No cauldron or liquid code. |
| 34 | CRAPI copies `allowedMaterials` from mould 22952, so custom moulds take metal ingots only | **CONFIRMED** | `CustomRecipesAPI/Core.cs:129-133`. |
| 35 | Neither library is loaded on this install | **CONFIRMED** | Client log loads TavernLib, kammmmerplant (attbullshit2.dll), QuickshotRing, SyncLib, WorkshopExamples. Server log loads TavernLib, QuickshotRing, SyncLib, TavernEssentials ("by Shadow", not CookingLib), WorkshopExamples. |
| 36 | Coexistence with our SyncLib: no SyncLib reference; the pack's SyncLib is "the same size" | **CONFIRMED, stronger** | Neither csproj references SyncLib. The pack's `SyncLib.dll`, `CookingLib.dll` and `CustomRecipesAPI.dll` are **MD5-identical** to ours (35a23b…, e5ae95…, 8dc103…). The one shared hook is `PrefabManager.PrepareSpawnSetups`: both are postfixes, and CookingLib runs first (priority 800), so its food walk never sees SyncLib items. That is harmless for this plan. |
| 37 | CustomDistributionAPI can add an item to a drop table | **CONFIRMED** | `CustomDistributionAPI/Core.cs:26`. An item added to Phantom's table shares the 1% roll. |
| 38 | Infusion = drop item in container with input liquid → output; endless sources refused | **CONFIRMED** | `CookingLib/InfusionPatch.cs`. Catch: the item is resolved by name at `WorldUp` (first update with a scene, `Main.cs:47-70`). An infusion registered in CookingLib's `Setup` that names a SyncLib item can race SyncLib's registration and be refused for good. Register it from SyncLib's `RegisterPrefabs` handler. |
| 39 | Sandstone Stone item 19822, prefab 55930, 0.07 kg | **CONFIRMED** | `items/items/Sandstone Stone.asset`. |

Tally over the 39 rows: **31 CONFIRMED** (several with corrections attached), **6 WRONG** (#3, #8, #9, #11, #16, and #27, which is minor), **2 COULD NOT VERIFY** (#17, #20). Inside a CONFIRMED row, how players get fireworks (#28) also could not be verified.

## 2. Corrections that change verdicts

1. **Splash effects mostly miss the monsters archers shoot.** No vanilla effect reaches Turabada (every variant) or deer (Dais). Greater Gotera and ferns are also out. Phantoms are immune to poison. Gotera, Wyrm and Turabada have no StatManager, so stat buffs/debuffs throw there. What works through `LiquidData.Apply`: players, Babu, Gotera (poison/reveal/teleport), Wyrms (poison only), Spriggull, Phantom (reveal only).
   - A poison arrow that matters must deal its own damage over time on the server: `HealthObject.ReceiveDamage(x, 0, new DamageData(DamageSource.Poison))` per tick, with its own expiry. That is not hard, but it is the real design, not "call `LiquidData.Apply`".
2. **The SyncLib insert bug is certain.** Treat the "Insert Deco" fix-up as a required step in registration, not an unknown.
3. **Sandstone → vial:** the smelter is the only vanilla station that can take it. It needs the timing workaround (claim 2) and the right upgrade set, and a hash that is not taken (`Dictionary.Add` throws). Chisel is out (wood only).
4. **Death Fern cloud as a "poison-cloud arrow"** is 20 m wide for about 16 s, hits the shooter, and ignores PvP. Usable only after shrinking it, and it still won't touch Turabada or deer.

## 3. What the report missed

- **Simpler head route:** clone the vanilla Explosive Spike or Healing Pod at runtime as the base for every special head. It already has Insert Deco, a durability module that breaks at 8 m/s, an `onServerBreak` event and an `Explosive`. This avoids Workshop art for version 1 (a tint is enough). Whether `RegisterCustomPrefab` accepts a clone of a vanilla network prefab is UNVERIFIED and is a 30-minute test.
- **CookingLib can stay server-only** for idea 3 if the output is the vanilla Poison liquid (46018), per its README. Only the leaf item needs both sides (SyncLib). Fewer client installs.
- **Library timing:** both CustomRecipesAPI (`SetUpRecipes`) and CookingLib (`Setup`/`WorldUp`) run before SyncLib items exist. Every recipe/infusion naming a mod item must be registered from SyncLib's `RegisterPrefabs`.
- **Players without the mod:** the installed SyncLib 1.1.1-custom has "optional prefabs". A client lacking an item mod gets those entities hidden (`ClientPrefabSupport.CanSpawn`, `NetworkPrefabRegistry.cs:156-178`), not kicked. CLAUDE.md's "game closes" line describes upstream behaviour. A vial docked on a vanilla arrow would be hidden on such a client while the shaft shows (UNVERIFIED in play).
- **Removing the mod later:** saved vials/leaves/cores become unknown hashes on load. Not checked; test before shipping on a server with real saves.
- **ArrowAssembler** (if the server ever runs it) whitelists head prefab ids in its JSON, so new heads need adding there.
- **Vanilla Teleport is a dice roll** (strength = chance). Any "teleport" liquid made with CookingLib inherits that. The teleport arrow should call `PlayerEffectController.Teleport` directly.
- **Teleport targets only the shooter,** a player, and players always have a working EffectHandler. So idea 4 avoids every creature problem in 2.1 and is simpler than poison splash. Its risks are landing and art, not plumbing.
- **Area checks:** one `OverlapSphere` per break is trivial. Only the 20 m cloud and repeated ticks over many targets need care.

## 4. Verdicts where I differ

| # | Report | Mine | Why |
|---|---|---|---|
| 1 | Doable with real work | Same, with the steps known | Joint fix-up is mandatory and simple. The sandstone smelter recipe needs the SyncLib-timing and upgrade-set workarounds. Drop the chisel alternative. |
| 2 | Doable with real work | **Doable, but more work than stated, and weaker on Turabada and deer unless the mod deals its own damage over time** | See section 2. |
| 3 | Straightforward to doable | Straightforward | CookingLib server-only is possible. The potion's real value depends on 2. |
| 4 | Doable; teleport carries the main risk | Doable; **plumbing easier than 2** | Only the shooter is moved, through an existing message. Main costs are a safe landing check and core art. |
| 5, 6, 7 | as report | Agree | — |
| D | slow/weaken/expose "easy" | **Not practical on most hostiles** | No StatManager means a NullReferenceException. Reveal on Phantoms does work (not immune). The poison-cloud arrow needs shrinking. |

**Build order:** I agree with putting Explosive-Spike variants first. Then I would do a one-day spike before anything else: a tinted clone of the Explosive Spike registered through SyncLib with the Insert Deco fix-up, docked on an arrow, shot at a Turabada and a deer. That answers docking, breaking on soft targets and the clone route at once. After that: fire, then the **teleport arrow before** poison splash (it is simpler), then poison (own damage over time), then Death Fern leaf/infusion, then sandstone vials, then fireworks.

**Three unknowns:** #1 (docking) is answered: it will not dock without the fix-up. I would replace it with "does `RegisterCustomPrefab` accept a cloned vanilla head". I agree with #2 (break on soft targets) and #3 (teleport landing). I would add "is the sandstone recipe visible on this server's smelters (simple vs default upgrade set)".

## 5. What I would tell the user

1. The report's facts are mostly right. 31 of 39 key claims check out against the game files and library code.
2. Its biggest error: splash potions do **not** reach most monsters. Turabada and deer have no effect handler. Phantoms are immune to poison.
3. So a poison arrow has to deal its own damage over time on the server. It is still doable, just not "apply the potion".
4. Slow, weaken and expose splashes are not practical: most monsters lack the stat system, and those effects would throw errors.
5. A mod-made arrowhead will **certainly** not fit the arrow without a one-line fix in our code (SyncLib gives it the flint's joint type). That is fixable, not a risk.
6. A sandstone mould is impossible (moulds require real ingots in code). A smelter recipe works, but it must be registered after SyncLib's items and on the right smelter upgrade set.
7. A chisel recipe cannot use sandstone: the chisel bench only cuts wood.
8. The Death Fern cloud is 20 m wide and lasts about 16 s. As an arrow it would gas you too.
9. The teleport arrow is simpler than the report suggests: only the shooter moves, through a message the game already has.
10. Both libraries are MIT, unloaded today, and byte-identical to the ones a live server runs next to our exact SyncLib.
11. CookingLib can run on the server only if the potion is the game's own Poison.
12. Do first: Explosive-Spike variants (server only, no new items).
13. Then a one-day test: a tinted copy of the Explosive Spike head, docked and shot at a Turabada and a deer.
14. Then fire, teleport, poison (own damage over time), the Death Fern leaf, sandstone vials, fireworks.
15. Still untested by anyone: whether heads break on creatures, safe teleport landing, and what happens to saves if the mod is removed.
