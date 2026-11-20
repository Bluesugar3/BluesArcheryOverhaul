# Vial arrows, potions and special arrows: feasibility

Date: 2026-11-16. Read-only research. Nothing was built, deployed or run in game, and nothing was tried in game. Everything below comes from the decompiled game code (`_decomp/`), the exported game project (`C:\ATTUnity\ExportedProject`, which is private reference material) and the installed library DLLs, which were decompiled into the scratchpad. **UNVERIFIED** marks anything I could not confirm from source.

## Summary

| # | Idea | Verdict | Why |
|---|------|---------|-----|
| 1 | Sandstone becomes vials at the blacksmith, a vial is filled with potion, and the vial is used as an arrowhead | **Doable with real work** | The arrowhead slot already exists: any item with an "Insert Deco" joint fits the arrow shaft (metal spikes, bone spike, Explosive Spike, Healing Pod). A **mould will not work**, because moulds take molten ingots only. The nearest workable version is a **smelter recipe** that turns Sandstone Stone into an empty vial (CustomRecipesAPI can add one). Filling the vial is the hard part. Version 1 should use one item per liquid ("Poison Vial", "Teleport Vial") and swap the empty vial for the filled one when it is dipped, instead of putting a live liquid container on an arrow part. A new vial model is needed, or the game's flask mesh scaled down. |
| 2 | A vial arrow splashes its effect over an area, on players and enemies | **Doable with real work** | The game already applies a liquid's effects to anything with health (`LiquidData.Apply`, server only), and thrown potions already shatter and splash. Explosive Spike and Healing Pod arrows already do a small area burst when the head breaks. The mod does a sphere check on impact and applies the liquid's effects. Two catches: **effects ignore the PvP setting**, so the mod must check it, and poison applied once **never wears off by itself**. |
| 3 | Death Fern drops a leaf that brews into a poison potion | **Straightforward to doable** | The Death Fern is real: `Alta.Intelligence.DeathFern`, a "reactive hazard" creature (prefab hash 37642). Today it drops **no item**. On death it releases a networked poison cloud. Adding a drop means one server patch plus one new leaf item, which needs art. "Brewing" works most simply as a CookingLib **infusion**: drop the leaf in water and it becomes Poison. A vanilla Poison liquid already exists. Where Death Ferns spawn in the overworld is UNVERIFIED. |
| 4 | Phantoms drop a core; core plus teleport potion makes an arrow potion that teleports you to where the arrow lands | **Doable with real work; the teleport carries the main risk** | Phantoms today drop only a **Phantom Guard** (a sword guard), at a **1% chance**. The bone-coloured three-armed piece with a hole in your picture may *be* the Phantom Guard (UNVERIFIED: I could not see the image). The server can teleport a player to any point through an existing message (`PlayerEffectController.Teleport`), so no new network traffic is needed. The risks are landing inside walls or terrain and griefing. Watch out: phantoms count as "killed" when they fade at dawn. |
| 5 | Fire arrows deal fire damage and set things alight | **Doable for a weak version; hard for real burning** | Today a lit grass-clump arrow only heats a 0.5 m area. **Only the player prefab can take fire damage. Creatures cannot burn.** The `BurnEffect` and `BurnStat` code exists but no asset uses it. A mod can set nearby flammables alight (`Fire.SimulateSparks`), which covers firewood, grass, fuses and fireworks. It can also add fire damage over time on hit. **Buildings have nothing that can burn**, so "light structures on fire" is not practical. A flame effect on a burning creature needs a small networked prefab. |
| 6 | Build on the Turabada-core explosive arrow | **Straightforward** | It is the vanilla **Explosive Spike** (hash 50412). The head's `Explosive` fires when its durability breaks: 1.5 health and 1.5 cripple damage, full within 0.5 m, falling to zero at 1 m, with a blast force of 15. Variants with more damage or a bigger radius, sticky delayed charges and cluster heads are all small changes. Same PvP catch as idea 2. |
| 7 | Firework arrowhead: flies straight, then booms on impact or after a set time | **Doable with real work** | The game's Firework is a whole item: a fuse, then a rocket push, then an `Explosive` (0.2 damage, 4 m radius). It is **not** an arrowhead. It needs a new "firework head" item with an Insert Deco joint. Straight flight means turning off gravity on the arrow **on both the shooter's machine and the server**. The burst can reuse the game's firework explosion effect. How players get fireworks is UNVERIFIED: the item sits under "themed" and is probably an event item. |
| 8 | More potions and arrows from existing items | Listed in section D | Examples: a Reveal arrow that knocks Phantoms down, a Healing Pod arrow (already in the game), slow, weaken and expose splashes from the buff effects with negative strength, and a poison-cloud arrow that reuses the Death Fern's own cloud. |

**Recommendation in one line:** build it as one "Alchemy Arrows" mod on SyncLib (items) + CookingLib (liquids/infusions) + CustomRecipesAPI (one smelter recipe), starting with the Explosive-Spike variants and a poison splash head, and leave the teleport arrow until splash works.

---

## A. The two libraries

### Are they installed?

- **Not loaded on either side today.** The client's `MelonLoader\Latest.log` loads only attbullshit2, QuickshotRing, SyncLib and WorkshopExamples (plus TavernLib). The server's log loads QuickshotRing, SyncLib, TavernEssentials and WorkshopExamples.
- Copies sit on disk but are inactive:
  - `<client>\Mods\New folder\` holds CookingLib, CustomRecipesAPI, MateriaLib, CustomDistributionAPI, CGNiksCustomLiquids, ArrowAssembler and more. MelonLoader does not load that subfolder (confirmed by the log).
  - `<client>\Modpacks\att.aservertown.fans\` is another server's pack. Its manifest lists cookinglib, customrecipesapi, customdistributionapi, customingots, materialib, cgnikscustomliquids, arrowassembler and **synclib**. Its SyncLib.dll is 129,024 bytes, the same size as ours.
- **Fit with our SyncLib 1.1.1-custom:** neither library references SyncLib. CookingLib has no SyncLib or `FindMelon` references at all, and CustomRecipesAPI mentions SyncLib only in a comment. That live pack ships both of them alongside what looks like our SyncLib, which is good circumstantial evidence that they coexist (UNVERIFIED by running them).
- I decompiled the copies in that pack. Decompiled output is in the scratchpad under `research\`.

### CustomRecipesAPI (CG-Nik): installed DLL 1.0.0, commit `cda998a`

- **What it can do.** Everything is static on `CustomRecipesAPI.Core`. You subscribe to `SetUpRecipes` (or Pre/Post), which fires in `OnLateInitializeMelon`.
  - **Moulds:** `SetUpMould(item, mouldDefinition, addMouldItemComponent, addToStandardPress, addToHebiosPress, posOffset, rotOffset)`. It registers a `MouldDefinition` and adds the mould item to the Standard/Hebios mould press docks. It **copies `allowedMaterials` from the vanilla axe-head mould (22952)**, so every custom mould takes **metal ingots only**. You must still make the mould item itself, a SyncLib prefab with art.
  - **Smelter recipes:** `SetUpSmeltingRecipeThroughCode(name, hash, duration, ItemCount[] in, ItemCount[] out, upgrades, addToDefault)` plus `CreateItemCount`. Inputs and outputs are arbitrary `Item`s. `itemsToAddToSmelter` lets the smelter dock accept a new input item, such as Sandstone Stone. It also patches `SpawnHelper.Spawn` to offset where smelter output appears.
  - **Chisel definitions:** `SetUpChiselDefinitionThroughCode(...)` for the carving or chiseling station.
- **What it cannot do:** no anvil or forging recipes, no workbench recipes (SyncLib 1.1.1-custom has its own workbench crafting), nothing for cauldrons or liquids, and no new physical materials. Any non-metal "mould" is out.
- **Requirements:** both server and client (README), MelonLoader. No other dependency. **MIT** licence.
- **Activity:** 20 commits from 2026-08-17 to 2026-11-17, latest titled "dominion". GitHub head is newer than the installed DLL: it mentions a "Smelter Autodock tag", but the installed build still says 1.0.0. No releases, 0 stars. Hashes are hard-coded vanilla ids (22952, 31642, 46222, 5674), so a game update could break it.
- **Known problems:**
  - `SpawnPatch` reads a stack frame (`frames[2]`) and a compiler-generated type name, which is fragile.
  - `Register*` uses `Dictionary.Add`, so registering a hash that is already taken throws.
  - It does reflection on private fields everywhere.

### CookingLib (Nyx12012 / "Shadow"): installed DLL 0.2.1, which matches GitHub head `5c855d2`

- **Liquids:**
  - `Liquids.RegisterShared(hash 1-65535, LiquidSpec)` makes a liquid both sides must have.
  - `RegisterServerOnly(spec)` makes one only the server knows. Vanilla clients can still drink it.
  - `LiquidSpec` fields: Id ("author.thing"), Name, Look (a vanilla liquid visual), Color, Effects, Chunks, ConsumableThroughSkin (splashes apply it), SplashStrength, Thirst.
  - Other calls: `Liquids.Fill(container, id, amount)` (server), `Find`, `Identify`.
- **Effects:** only the **game's own `EffectDefinition`s** (`EffectNames.Need`, game kinds only). **You cannot make a new status effect with CookingLib.** Strength must be between -100 and 100. `OverSeconds` is allowed only for Heal and Nourish. Effects available in this build (`Resources/cooking/effects`):
  - Speed Indirect (6764), Damage Indirect (654), Damage Protection Indirect (64858) and Luminosity (65294). These are timed stat buffs; Speed lasts 900 s.
  - Heal (48456), Feed (3280), Nourish (20046)
  - Poison over Time (29250)
  - Reveal (62720)
  - Teleport Home (44252), Teleport Original Respawn (27214), BuildupTeleportToOverworld
  - Inate (22728), which does nothing
- **Infusions:** `Infusions.Register(new InfusionSpec{ Id, Item, InputLiquids, OutputLiquid, ConsumeItem })`. Any item dropped into a container holding one of the input liquids turns that liquid into the output. Endless sources such as rivers are refused. It patches `LiquidContainerTrigger.OnTriggerEnter`. This is the simplest "brewing" route, and **CGNiksCustomLiquids already ships it** (Stone into Water gives Mineral Water).
- **Stews:** `StewRecipes.Register/RegisterShared(spec)`, where a cauldron recipe can output a liquid (`ResultLiquid`). Slots need items that carry a vanilla `CookableItem` component, so a brand-new leaf item would need that component too (UNVERIFIED whether a SyncLib item can carry one).
- **Other features:** vanilla edits (recipes, ingredients, garnishes, foods, liquids), pot rules, roasting rules, and events `Drinks.Drank`, `Ate`, `CanDrink` and `StewEvents`, which run **on the server only**.
- **Requirements:** the server always needs it. Clients need it for shared liquids. No SyncLib dependency. **MIT** licence.
- **Activity:** 7 commits from 2026-10-12 to 10-23, 2 forks, no releases.
- **README's own warnings:**
  - "0.2 has not run on a live server yet."
  - Shared recipe pages show blank to vanilla players.
  - Several behaviours are untested.

### Other installed-but-inactive helpers worth knowing

- **CustomDistributionAPI (CGNik 1.0.0):** `CreateEmptyDistribution`, `AddToDistribution(distribution, topic, weight, ...)`. It can add an item to a creature's drop table.
- **ArrowAssembler (caleb 0.2.2, uses SyncLib):** a machine that assembles arrows. Its recipe JSON lists the arrowhead slot as prefabs 7092, 61488, 12492, 6934, 3558, **50412 (Explosive Spike)** and **44360 (Healing Pod)**, which independently confirms which heads exist.
- **CustomIngots.API:** custom ingot definitions. Its author is not shown in the DLL's MelonInfo. It is the only route to a "glass ingot", and that route is not recommended.

---

## B. The game systems these ideas touch

### B1. How an arrow is built

- **The shaft:** `Arrow Shaft Wooden` (hash 29772). It has three slots: **Slot Feather** (accepts Insert Feather), **Slot Grass** (accepts Insert Grass) and **Slot Deco**, the arrowhead slot (accepts Insert Deco). The "Tip" child is only a marker. Slot types are `Alta.Crafting.JointSlotType.AllowedInserts` (`_decomp/Alta.Crafting/JointSlotType.cs`, `JointInsertType.cs`).
- **Items with Insert Deco**, which can be arrowheads (exported prefabs under `network prefabs/crafting/combat/spikes/`):
  - Spike Fancy / Short / Tall. These are mould-cast metal; Spike Fancy has a `MouldItemComponent`.
  - Small Bone Spike, Crystal Shard Blue
  - **Explosive Spike**: the Turabada core, mesh `Turabada_Explosive_Shard`
  - **Healing Pod**
  - Pole huggers and pommels also carry Insert Deco.
- **Items with Insert Grass:** Grass Clump (the dry-grass fire arrow) and **Buran Resin**.
- **Behaviour:** `Arrow` (`_decomp/Arrow.cs`) handles flight (`ArrowFlightSettings`, the arrow's weight, the `ArrowSync` message) and `ShotOnServer`. Damage comes from the head's `ImpactTool`/`Impactor (Archery)`. A head's special behaviour lives on the head itself: Explosive Spike and Healing Pod carry an `Explosive` that fires from `onServerBreak` when their `DurabilityModule` breaks.
- **SyncLib gotcha (found in the decompiled installed SyncLib `WorkshopApplier.ApplyJoints`):** a Workshop `JointInsert` placeholder's type is resolved by name from the Workshop's `JointSlotType` enum. That enum has only `Slot...` names, for example "SlotDeco" which becomes "Slot Deco". The real insert asset is called **"Insert Deco"**, so the lookup misses silently and the insert keeps the template's type (item 42570's insert). A Workshop-made arrowhead will probably **not** dock on an arrow unless the mod sets `JointInsert.type` to the "Insert Deco" asset after registration. This is UNVERIFIED in play.

### B2. Fire, heat and burning

- `Alta.Heat.Fire` (`_decomp/Alta.Heat/Fire.cs`) is an `IFlammable` heat source on fuel items. `Fuse` is the only other `IFlammable`. `Fire.SimulateSparks(position, radius, chance)` is public and static; it lights every flammable in a sphere. `FireLighter` lights flammables by trigger.
- **Grass Clump** carries Fire (ignites at 60°, goes out at 50°) and a heat area of 400° with a 0.5 m outer radius. So a lit arrow only heats things within about 0.5 m of itself.
- **Damage from heat:**
  - `Alta.Heat.HeatManager` (`DamageSource.Fire`) turns hot heat points into health damage. In the whole exported project only **`VR Player Character New.prefab`** has one.
  - **Creatures cannot take fire damage.**
  - `Alta.Alchemy.BurnEffect` and `Alta.StatSystem.BurnStat` exist in code, but **no asset uses them**: there is no burn effect asset and no "burn" stat asset.
  - `ImpactType` has no fire type (Pierce, Slash, Blunt, Caustic, Poison, Explosive, Spiritual).
- **Buildings:** structures have no `IFlammable`, so nothing in the game sets buildings on fire.

### B3. Explosions (the Turabada Explosive Spike, Healing Pod and Firework)

- **How it works:** `Explosive` (`_decomp/Explosive.cs`) runs on the server.
  - `Trigger()` does an `OverlapSphere`, filters by impact channel and hardness, and calls `health.DetectImpact(new ImpactHit(...))` with a falloff from `maxDamageRange` to `range`, plus an explosion force.
  - The shooter is credited through `Pickup.LastPlayerInteractor`.
  - Clients see the effect through the existing `EffectRequest` message.
- **Values** (from the prefabs):

  | Item | Health / cripple | Full-strength radius | Total radius | Blast force | Fires on |
  |---|---|---|---|---|---|
  | Explosive Spike | 1.5 / 1.5 | 0.5 m | 1 m | 15 | head breaks |
  | Healing Pod | **-1** health (it heals) / 1 cripple | 1 m | 1 m | — | head breaks |
  | Firework | 0.2 / 0.2 | 3.5 m | 4 m | — | after its fuse, or when it hits something in flight |

  The Healing Pod is already a vanilla **area-heal arrow**.
- **PvP:** `PlayerHealth.ReceiveDamage` applies the PvP switch and multipliers only to `ImpactHitWithTool` hits made by a player. `Explosive` builds a plain `ImpactHit` with no tool, so **explosions appear to skip the PvP setting** (inferred from code; UNVERIFIED in play).

### B4. Fireworks

- `Firework.prefab` (item 44786, prefab 40748): a `Fuse` (4 s) triggers a `PropelledProjectile`. That component pushes along a force curve, enables collision after 1 s and invokes `hitWhileFlying` when it hits something faster than 1 m/s. That fires the `Explosive` and the firework burst effect.
- The firework has **no joint insert**, so it cannot go on an arrow as it is.
- The item lives under `items/themed`, so how players get it is UNVERIFIED.

### B5. Potions, effects and splashing

- Every creature and player has an `EffectHandler` on its `HealthObject` (`_decomp/Alta.Alchemy/EffectHandler.cs`). Players use the subclass `PlayerEffectController`. It provides:
  - **Poison** (`PoisonOverTime`): stacks, and deals `strength × the creature's poison multiplier` every second. A stack is removed only by applying a negative strength, so **poison applied once lasts until death or until removed**. Area sources such as the Death Fern cloud remove their stack when you leave.
  - **Teleport** (`Teleport(position, ...)`): has a 2 s cooldown and can be blocked by immunity flags.
  - **Reveal** (`OnReveal`): a Phantom's `Reveal()` takes 10 cripple damage, which knocks it down.
  - Timed effects (`EffectOverTime`).
- **Applying a liquid:** `LiquidData.Apply(EffectHandler, strength)` is **server only**. **Splashing is already in the game:**
  - `ImpactDetector.HandleParticleAsLiquidCollision` applies a liquid with `isConsumableThroughSkin` to whatever its particles touch.
  - A `Potion Medium` flask breaks at about 8 m/s (`breakVelocitySquared 64`) and its `onServerBreak` spawns coloured splash particles (`ColoredPooledParticleSpawner`, `PotionVisuals`, `LiquidParticleMarker`).
  - This particle route is imprecise. A mod should do its own sphere check.
- **Area effects that already exist:**
  - `EffectVolumeTrigger` applies effects on enter and removes them on leave.
  - `EffectArea` does the same with a liquid.
  - The **Death Fern Poison Cloud** (`network prefabs/geode/Death Fern Poison Cloud.prefab`, a networked prefab) is a sphere trigger about 10 m across, as authored, that applies Poison over Time at 0.2. Its lifetime and real size in play are UNVERIFIED.
- **Vanilla liquids** (`Resources/cooking/liquids`):
  - **Poison** (46018): Poison over Time ×1, skin-absorbable.
  - **Teleport Potion** (27100): Teleport Home + Reveal, splash strength 0.3. **What it does exactly:** `TeleportEffect` with landing target Home sends a player to their own respawn point (`GetSpawnPosition(isRespawning:true)`). A non-player goes to the respawn spawn area. It also reveals.
  - Health Potion, Damage Buff, Damage Resist, Self Damage.
  - "Jungle Juice Poison" (34492), which despite its name applies Nourish ×0.5.
  - How players get these potions in vanilla is UNVERIFIED (probably loot).
- **Teleporting a player is safe to do from the server:** `PlayerEffectController.Teleport` on the server sends the existing `TeleportRequest` message to that player's client, which moves itself (`LocomotionController.TeleportTo`). No new traffic is needed. Server-side anti-cheat on position: none seen in this class (UNVERIFIED).

### B6. Cauldrons and brewing

- The cauldron (`Cauldron Medium`, item 25660) is a stew pot (`Alta.Cooking.StewManager`, `StewRecipe`). Vanilla has **no potion brewing**. CookingLib adds stews that output liquids, and infusions.
- **Filling a flask** is vanilla dipping or pouring through `LiquidContainerTrigger`/`LiquidContainer` (`containerSize`). Potion Medium (prefab 23644) holds `containerSize: 1`.
- **"Dip an arbitrary item to fill it"** only works if that item *is* a `LiquidContainer`. SyncLib's `WorkshopApplier` has no liquid-container placeholder, so a mod would have to bolt one on in code (UNVERIFIED that this works on a crafted part).

### B7. Smithing, moulds and sandstone

- `MouldDefinition` (`_decomp/MouldDefinition.cs`) has the field header "**Must be ingots**", plus `allowedMaterials`, `product` and `quantityProduced`. Moulds only take molten metal.
- **Sandstone Stone** (item 19822, prefab 55930, 0.07 kg) is a mining and chiseling stone (Sandstone boulders, "Chisel Sandstone Distribution"). It is **not** an ingot.
- **There is no glass material or item.** The Spyglass recipe exists but no glass resource does.

### B8. Drops and loot

- Creature drops are prefab spawners that point at an `Alta.Caves.Distribution` (`_decomp/Alta.Caves/Distribution.cs`: `nullChance`, weighted `items`).
- The **Phantom** uses `Phantom Drop Distribution` (52318): **nullChance 0.99**, the only item being the **Phantom Guard**. That is a 1% drop.
- A mod can add to a distribution (CustomDistributionAPI) or spawn items from a Harmony postfix on the creature's `Killed` (server).

### B9. The Phantom

- `Alta.Intelligence.Phantom` (`_decomp/Alta.Intelligence/Phantom.cs`) is a night spawner (Phantom Global Population). It is immaterial or materialized, can be cracked by a mining echo, and is hurt by Reveal.
- It **fades at dawn by calling `Killed(null)`**. A naive "on killed, drop a core" hook would drop cores every morning, so check that the killer is not null and that health is zero.

### B10. The Death Fern

- `Alta.Intelligence.DeathFern` (prefab "Death Fern", hash 37642, under `creatures/reactive hazards/`) is a stationary hazard creature with health.
- When destroyed it plays "Leaves Break" and spawns the **Death Fern Poison Cloud**. It drops no item.
- Relatives: **Mother Fern**, which sprays **Death Fern Seeds** that plant new ferns ("Death Fern Seed Spawn Distribution": Death Fern, Mother Fern at 0.15 weight, Vine Attack Fern), and Vine Attack Fern.
- **Where it spawns:**
  - Populations "Death Fern Population" and "Death Fern Climbing Population" are referenced by `Rock Spire Gcore Dynamic` (a Hebios camp).
  - No ripped overworld chunk prefab places one directly.
  - The user says forest. The exact overworld locations are **UNVERIFIED** (they may be streamed or seeded at run time).
- A "red leaf" item does not exist. The fern's own frond meshes (`deathFern_poison_fronds_LOD*`) could be reused for one.

### B11. PvP rules for splash

- Server settings: `IsPVPEnabled`, `PVPMultiplier` (default 0.1) and `PVPCrippleMultiplier` (default 0.3). They are enforced only in `PlayerHealth.ReceiveDamage`, and only for player tool hits.
- **Effects** (poison, teleport, buffs) and plain `ImpactHit` explosions **are not filtered**. A splash mod must check `ServerSettings.IsPVPEnabled` itself and decide whether players are included (recommended: players get hit only when PvP is on, never the shooter).

---

## C. Each idea in detail

### Idea 1: sandstone to vial to filled vial to vial arrowhead

- **As stated:** "use sandstone in the blacksmith ... to create vials" through a **mould cannot work**, because moulds take molten ingots only and sandstone is not an ingot.
- **Nearest workable version:**
  - **Smelter recipe:** Sandstone Stone ×N gives one Empty Vial, via `CustomRecipesAPI.SetUpSmeltingRecipeThroughCode` with Sandstone added to `itemsToAddToSmelter`. It reads as "melt sand into glass". Whether the smelter accepts a non-ore input and spawns a non-ingot output is UNVERIFIED.
  - Alternatives: a chisel recipe (CustomRecipesAPI), or a SyncLib workbench recipe.
- **Pieces needed:**
  - **Empty Vial item** (SyncLib prefab, Insert Deco joint fixed up in code as described in B1).
  - **Filled vial items**, one per liquid (Poison Vial, Teleport Vial, Healing Vial and so on). Each needs a new prefab; the same mesh with a tint is fine.
  - **Fill:** a server Harmony postfix on `LiquidContainerTrigger.OnTriggerEnter`, the same hook CookingLib uses. When an Empty Vial enters a container holding a known liquid, the mod drains one unit and replaces the vial with the matching filled vial (server spawn and destroy, no new traffic).
- **Art:** vial and glass. The game's `potion_medium_flask_01` / `Spherical_Bottle_Smashed` meshes can stand in at a small scale. A proper slim vial-tip mesh needs new art, made under Fable per your model-roles note.
- **Sides:** both (SyncLib items). The fill logic is server only.
- **Main risk:** the arrowhead insert type (B1), and replacing a part that is already docked on an arrow. Fill the vial **before** attaching it.
- **Minimal version 1:** skip sandstone. One "Poison Vial" item, spawned by a server command, that docks as an arrowhead.
- **Try in game first:** does it dock on Slot Deco? Does it break on impact? Does it survive a quiver and the ArrowAssembler? Does the smelter accept sandstone?

### Idea 2: splash on impact

- **How:** give each vial head a fragile `DurabilityModule` (it breaks on impact like Explosive Spike). On server break, the mod runs `OverlapSphere(radius)` and calls `liquid.Data.Apply(health.EffectHandler, strength)` for each hit, skipping players unless PvP is on.
- **Visuals:** call the game's potion splash (`ColoredPooledParticleSpawner` / `PotionImpactColorer`, coloured by the liquid) at the impact point on clients. Hang it on the head's existing break or destroy sync. No new message types.
- **Must handle:**
  - Poison never expires on its own: apply negative strength after N seconds.
  - Teleport Home inside a splash would send *victims* home. Decide per liquid what splash means.
- **Sides:** server decides. Clients need the mod for the items and the splash look.
- **Risk:** whether the head breaks reliably on soft targets such as creatures (UNVERIFIED). Fallback: hook the arrow's impact or `Embeddable.Embedded` on the server.
- **Version 1:** a Poison Vial that applies Poison over Time to creatures within 2 m for 10 s.
- **Easier alternative:** spawn the vanilla **Death Fern Poison Cloud** prefab at the impact point. That gives a working, networked, already-art-complete poison area with no splash code. Its size and lifetime need checking in play.

### Idea 3: Death Fern leaf and the poison potion

- **Drop:** server postfix on `DeathFern.Killed`. Spawn a "Death Fern Leaf" (new SyncLib item; a frond mesh could be reused) with a configurable chance.
- **Brewing:** CookingLib `Infusions.Register({Item="Death Fern Leaf", InputLiquids={"Water"}, OutputLiquid="Poison"})`. Vanilla Poison is skin-absorbable, so it splashes.
  - "Max poison" as a stronger liquid would be `Liquids.RegisterShared(hash, spec)` with Poison over Time at a higher strength.
  - A real cauldron recipe is possible, but the leaf would need a `CookableItem` component (UNVERIFIED).
- **Sides:** both (new item and shared liquid). Infusion and drop logic run on the server.
- **Risk:** low. Where the ferns are is unknown (B10).
- **Version 1:** the leaf drop plus a Water-to-Poison infusion.

### Idea 4: Phantom core plus teleport potion makes a teleport arrow

- **The core item:** either reuse the **Phantom Guard** (no new art, but it is a 1% drop and a crafting part people may want for swords), or make a new "Phantom Core" (needs art). Add the core with `CustomDistributionAPI.AddToDistribution` (it shares the 1% roll) or with a `Phantom.Killed` postfix (own chance; skip the dawn fade).
- **The liquid:** `RegisterShared` "Phantom Teleport" (an Inate effect, or Teleport Home for drinking), made by infusion: core into Teleport Potion.
- **Arrow behaviour:** a Teleport Vial head. On impact, the server takes the shooter (`Pickup.LastPlayerInteractor` / `Explosive`'s wielder logic) and calls `PlayerEffectController.Teleport(safePoint)`.
  - `safePoint` is the impact point pushed out along the surface normal and checked by raycast for headroom.
  - The vanilla teleport puff plays through the same message.
- **Risks:**
  - Landing inside geometry or under terrain.
  - Teleporting into other players' builds (decide a range cap).
  - Client-side locomotion edge cases while climbing, mounted (Horse mods) or gliding.
  - The 2 s cooldown.
  - Combining with QuickshotRing: volley ghosts and spectral arrows are not real arrows and must not teleport.
- **Version 1:** a server command gives a Teleport Vial, shooting it teleports the shooter, capped at 40 m.

### Idea 5: real fire arrows

- **As stated:** "light things on fire" works for **items** (logs, grass, fuses, fireworks, campfires) through `Fire.SimulateSparks`. It **cannot** work for buildings, which have nothing flammable.
- **"Fire damage"** on creatures does not exist in the game. The mod would deal damage over time itself (`HealthObject.ReceiveDamage(amount, 0, new DamageData(DamageSource.Fire))`, server) when an arrow whose Grass Clump is lit hits.
- **Showing creatures on fire:** clients need a small networked "burning" prefab attached to the creature (SyncLib), or the game's fire effect (`PooledFireEffect`) driven from an existing message.
- **Version 1:** on a lit-arrow impact, `SimulateSparks(point, 1.5 m)` plus 3 s of fire damage over time on the creature hit. The PvP rule applies to players.
- **Risk:** fire spreading through stockpiles, campfires and fuse puzzles. Gate the radius.
- **Try in game first:** whether a lit arrow stays lit in flight (the vanilla grass fire's `fireCollider` behaviour).

### Idea 6: Turabada core variants

- **Straightforward.** Explosive Spike is already an arrowhead with an `Explosive`. Options:
  - **(a)** Patch `Explosive.Trigger` for heads on arrows to scale damage and radius by a setting.
  - **(b)** A new "Refined Explosive Head" item with an `Explosive` copied from Explosive Spike at registration and bigger numbers. A recipe could be core plus metal spike.
  - **(c)** A sticky head that embeds and triggers after 2 s.
  - **(d)** A cluster head that triggers three small blasts.
- **Server only for gameplay.** Effects already sync.
- **PvP:** explosions bypass the PvP check (B3), so add one.

### Idea 7: firework arrows

- **Pieces:** a "Firework Head" item (Insert Deco), with an `Explosive` copied from the Firework (4 m) and the firework's pooled explosion effect.
- **On shot:** set `Rigidbody.useGravity = false` **on the shooter's client and on the server**, because arrow physics is client-first. Optionally add a small push like `PropelledProjectile`, and keep the speed under the 100 m/s network clamp.
- **Burst:** on impact (head breaks) or after a configurable time (server timer), call `Trigger()`.
- **Recipe:** Firework plus spike. Firework availability is UNVERIFIED; Turabada core plus resin could replace it.
- **Risk:** straight flight also changes how ghost or volley copies behave. Spectators must see the arrow fly straight; arrow flight sync is existing traffic, but how it deals with no gravity is UNVERIFIED.
- **Version 1:** no new head. A Firework placed on a vanilla arrow is not possible (no joint), so version 1 *does* need the new head item.

---

## D. More potions and arrows from what is already in the game

| Idea | How it would work | Effort |
|---|---|---|
| Healing Pod arrow (already vanilla) | Document and balance it; it heals 1 health in 1 m. A bigger healing splash could use a Health Potion vial. | trivial |
| **Reveal arrow** (Teleport Potion's Reveal) | Splash applies Reveal (62720). Phantoms take 10 cripple damage and get knocked down, giving a phantom-hunter arrow. | easy once splash exists |
| Slow splash | Speed Indirect (6764) at negative strength. Whether creatures have the stat is UNVERIFIED; Phantom has a "speed" stat. | easy, needs testing |
| Weaken / Expose splash | Damage Indirect (654) or Damage Protection Indirect (64858) at negative strength. | easy, needs testing |
| Glow arrow | Luminosity (65294) splash. A light that marks a target or lights a cave; Glowing Mushroom is the natural ingredient. | easy |
| Poison-cloud arrow | Spawn the vanilla **Death Fern Poison Cloud** at the impact point. No new art, already networked. | easy |
| Crystal Shard Blue head | Already an arrowhead. Add a Luminosity flash or a "spiritual" damage type on hit. | easy |
| Buran Resin fire arrow | Resin already fits the grass slot. Make resin fire last longer or burn hotter than grass. | easy, with fire work |
| Home-recall arrow | Teleport Original Respawn (27214) on the *shooter* when the arrow embeds: an escape arrow. | medium (same as idea 4) |
| Self-damage curse vial | Splash with the vanilla Self Damage Potion. | easy |
| Explosive arrow knock-up | Explosive Spike variant with a high `blastForce`, and no damage, to throw creatures and items. | easy |

---

## E. Recommendation

- **Libraries:**
  - **SyncLib** (already ours) for every new item.
  - **CookingLib** for liquids and infusions. It is the only way to add potions cleanly, but it is young: "0.2 never ran on a live server", per its own README.
  - **CustomRecipesAPI** only for the one sandstone smelter recipe.
  - Optionally **CustomDistributionAPI** for drops. A Harmony postfix is just as easy and gives more control.
  - Install the libraries for real on **both** client and server; the local copies are inactive today.
- **Order:**
  1. Explosive-Spike variants and PvP-gated explosions. These use existing heads and are server-side.
  2. Fire-arrow sparks and damage over time.
  3. Poison Vial head plus splash and timed poison, or start with the poison-cloud shortcut.
  4. Death Fern leaf and the poison infusion.
  5. Sandstone vial smelting and dip-to-fill.
  6. Firework head.
  7. Teleport vial and the Phantom core.
- **Packaging:** one mod ("Alchemy Arrows") for vials, splash, liquids and recipes. Keep it **separate from QuickshotRing** (skills and shrine), but test the two together: volley ghosts and spectral arrows must not trigger vial effects. Fire and explosive tweaks can live in the same mod as separate settings.
- **Three biggest unknowns:**
  1. Whether a mod-made arrowhead docks on the arrow shaft at all, given SyncLib's joint-insert name lookup (B1).
  2. Whether a head breaks reliably on impact with creatures and players, so it triggers.
  3. How the server-driven player teleport behaves in real play: safe landing, climbing, mounts, and other clients' view.
