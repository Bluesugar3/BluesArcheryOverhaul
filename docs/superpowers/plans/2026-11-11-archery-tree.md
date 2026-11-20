# Archery Tree Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the Ranged skill tree of A Township Tale in the `QuickshotRing` mod: add Quiver Gather, turn Two-Shot/Three-Shot into a 3/9 spectral volley, make Holy Arrow a 5-second area shot that absorbs the volley, and make the fourth Quickdraw orb an instant straight-line shot.

**Architecture:** One MelonLoader DLL loaded by both the dedicated server and every client. The server owns every gameplay decision and applies all damage; shots that never physically fly (volley extras, the Quickdraw 4 line) are raycasts on the server, never networked objects. Clients draw everything locally, driven only by messages the game already sends (skill status broadcasts, the arrow flight sync, docking, projectile collision). All arithmetic and decision rules live in one Unity-free file so they can be unit-tested from a console project.

**Tech Stack:** C# (`net472`, `LangVersion latest`), MelonLoader 0.7.3, HarmonyLib 2.10, Unity 2020.3 Mono. Icons: Python 3.13 + Pillow. Build: `dotnet` 8 SDK.

**Spec:** `docs/superpowers/specs/2026-11-11-archery-tree-design.md` (read it first; "my call" markers there are binding unless the user changes them).

## Global Constraints

- One DLL, same build, on client and server. Log `QuickshotRing v<version> (<server|client>)` at start-up.
- No new networked objects. No new network message types: signal through existing messages only.
- Server decides and applies damage; client code must never change gameplay state except predicting the local player's own shot.
- Every art asset is the game's own, or built from the game's own art and shown to the user on a comparison sheet (real above, new below) before it ships. No effect may obscure the shooter's aim.
- No per-frame work on the server except stepping live virtual arrows and Holy Arrow flights.
- All tunable numbers are `MelonPreferences` entries; defaults are the spec's values, copied below.
- Every stage logs one line with the `[Archery]` prefix so the user's play-tests can be read from `MelonLoader\Latest.log` on both sides. The assistant cannot run the game.
- Server state is per bow string and per arrow, never a single static "current shot". (The existing gold ring keeps its one-ring-per-client statics; that is client-only.)
- **Built to be iterated on (user requirement, 2026-11-11):** the user expects to come back and change the art after seeing it. So: every visual number (sizes, colours, timings, brightness, counts, asset names) is a pref, never a literal in effect code; prefs are re-read while the game runs (the client checks the config file's timestamp every 2 s and reloads its categories) so a tweak needs no restart; icons load from `UserData\QuickshotRing\Icons\<Name>.png` when that file exists, falling back to the embedded copy, and are re-read when the file changes; each effect is one small self-contained class so it can be replaced without touching gameplay code.
- Version after this plan: `0.5.0`.
- The project is not a git repository. Where the skill says "commit", run the build and tests and stop there; do not `git init` unless the user asks.
- Game installs: client `C:\Users\dmand\OneDrive\Desktop\A Township Tale (client`, server `C:\Users\dmand\OneDrive\Desktop\A Township Tale(server`. DLLs are locked while either is running.

## Numbers (from the spec)

| Name | Value |
|---|---|
| Draw threshold | 0.68 |
| Slowdraw charge | 1.75 s (unchanged) |
| Two-Shot timer after Slowdraw charges | 0.5 s (vanilla 0.75) |
| Three-Shot timer after Slowdraw charges | 1.0 s (vanilla 1.5) |
| Interval between the six outer extras | 0.2 s (first appears when Three-Shot charges) |
| Holy Arrow timer from threshold | 5.0 s (vanilla 4.0) |
| Gathering window | 3.75 s to 5.0 s |
| Inner extra share / outer extra share | 0.25 / 0.10 of the charged main arrow |
| Holy direct / blast centre | 4.0 / 2.0 x charged main arrow |
| Blast radius / full-damage radius / edge fraction | 5.0 m / 1.5 m / 0.5 |
| Holy speed | 5 x launch speed, no gravity |
| Quickdraw 4 damage | 3.0 x arrow, replaces the vanilla level value |
| Quickdraw 4 reach | 150 m hard cap, or edge of loaded world |
| Quiver return delay | 15 s from the shot |
| Quiver retry interval while waiting | 1 s |
| Tip layout | inner triangle radius 0.035 m at 90/210/330 deg; outer ring radius 0.09 m at 0/60/.../300 deg |

## Review Focus

1. **Player leaves or dies** with arrows tracked, a volley armed, or a Holy Arrow charging: all state for that player is dropped, nothing throws, no arrow teleports to a missing quiver. (Task 3, Task 5, Task 6)
2. **A tracked or line-shot arrow is destroyed** (durability break, chunk cleanup) before its timer or placement completes: skipped silently. (Task 3, Task 4)
3. **Two players, or one player with two bows, drawing at once:** each bow string has its own volley and Holy state; one release never fires or cancels the other. (Task 5, Task 6)
4. **Line or virtual arrow starts inside or against the shooter:** the shooter's own body, bow and nocked arrow are never the hit. (Task 4, Task 5)
5. **A status message arrives on a client before the arrow it names has spawned there, or names an arrow that is already gone:** visuals wait for spawn or are skipped; no null reference. (Task 5, Task 6)

Known side effect to check in play, not a test: applying extras through the real arrow's `ImpactTool` may wear the real arrow's durability nine times per volley (Task 5, Step 9).

---

## File Structure

| File | Responsibility |
|---|---|
| `QuickshotRing/Rules.cs` (new) | Unity-free constants, arithmetic and decision rules. The only file the test project compiles. |
| `QuickshotRing.Tests/` (new) | Console test runner for `Rules.cs`. |
| `QuickshotRing/Tree.cs` (new) | Skill-tree edits at load: Quickdraw 4 slot (moved from `Lightning.cs`), Quiver Gather slot + runtime skill, Lightbeam re-link, draw-timer changes, icon assignment. Exposes the skill handles everyone else uses. |
| `QuickshotRing/Hits.cs` (new) | Server: apply an arrow's damage at a raycast hit; shooter-collider filtering; loaded-world check. |
| `QuickshotRing/QuiverGather.cs` (new) | Server tracking and return; client return ghost. |
| `QuickshotRing/Quickdraw4.cs` (new, replaces `Lightning.cs`) | Instant line shot: server resolve and arrow placement, client prediction and line visual. |
| `QuickshotRing/Volley.cs` (new) | Per-bow volley state, server virtual arrows, client ring/flight/gather visuals. |
| `QuickshotRing/HolyArrow.cs` (new) | Beam speed and damage, server blast, client trail/impact/stun marker. |
| `QuickshotRing/Fx.cs` (renamed from `LightningFx.cs`) | Shared line-drawing primitives and game-asset lookup only. The 0.4.0 beam, rings, sky bolt and crawl are deleted. |
| `QuickshotRing/Main.cs` (modify) | Start-up log, prefs, self-check call; gold ring unchanged. |
| `QuickshotRing/Art/` | Icon scripts and extracted reference icons (already present). |
| `QuickshotRing/Resources/` (new) | Final icon PNGs, embedded by `Directory.Build.props`. |
| `_decomp/` (new, reference only) | Decompiled game source for reading signatures. Never compiled. |

---

### Task 1: Rules, tests and tooling

**Files:**
- Create: `QuickshotRing/Rules.cs`, `QuickshotRing.Tests/QuickshotRing.Tests.csproj`, `QuickshotRing.Tests/Program.cs`
- Modify: `Directory.Build.props` (deploy target), `QuickshotRing/Main.cs`

**Interfaces:**
- Produces (all `internal static`, namespace `QuickshotRing`, no `UnityEngine` types):
  - `DrawTimeline.ExtrasShowing(bool twoShotCharged, bool threeShotCharged, float secondsSinceThreeShotCharged) -> int`
  - `DrawTimeline` constants: `SlowdrawCharge`, `TwoShotAfterSlowdraw`, `ThreeShotAfterSlowdraw`, `OuterInterval`, `HolyTotal`, `GatherSeconds` (= 1.25f)
  - `VolleyMath.ExtraShare(int index, float inner, float outer) -> float`; `VolleyMath.TipOffset(int index, out float x, out float y)`; `VolleyMath.MaxTotal(int extras, float inner, float outer) -> float`
  - `BlastMath.Fraction(float distance, float fullRadius, float radius, float edgeFraction) -> float`
  - `enum ReturnAction { Wait, Return, GiveUp, Forget }`; `QuiverRules.Decide(bool arrowExists, bool areaLoaded, bool held, bool storedElsewhere, bool quiverCarried, bool quiverHasRoom) -> ReturnAction`

- [ ] **Step 1: Decompile the game for reference**

Run: `ilspycmd -p -o _decomp "C:\Users\dmand\OneDrive\Desktop\A Township Tale (client\A Township Tale_Data\Managed\Root.Township.dll"`
Expected: `_decomp\Arrow.cs`, `_decomp\BowString.cs`, `_decomp\Alta.Progression.Skills\LightBeamSkill.cs` exist. Later tasks cite files in this folder.

- [ ] **Step 2: Create the test project**

`QuickshotRing.Tests.csproj`: `<Project Sdk="Microsoft.NET.Sdk">` with `OutputType` `Exe`, and `<Compile Include="..\QuickshotRing\Rules.cs" />`. It inherits `net472` from `Directory.Build.props`. `Program.cs` holds a `Check(string name, bool ok)` that counts failures, prints `FAIL <name>` per failure, ends with `ALL PASS (<n>)` or `FAILED <k> of <n>` and exit code 1.

- [ ] **Step 3: Write the failing tests**

```csharp
// timeline
Check("no skills", DrawTimeline.ExtrasShowing(false, false, 0f) == 0);
Check("two-shot", DrawTimeline.ExtrasShowing(true, false, 0f) == 3);
Check("three-shot just charged", DrawTimeline.ExtrasShowing(true, true, 0f) == 4);
Check("three-shot 0.19", DrawTimeline.ExtrasShowing(true, true, 0.19f) == 4);
Check("three-shot 0.2", DrawTimeline.ExtrasShowing(true, true, 0.2f) == 5);
Check("three-shot 1.0", DrawTimeline.ExtrasShowing(true, true, 1.0f) == 9);
Check("three-shot long", DrawTimeline.ExtrasShowing(true, true, 60f) == 9);
Check("three without two still has inner", DrawTimeline.ExtrasShowing(false, true, 0f) == 4);
Check("negative time", DrawTimeline.ExtrasShowing(true, true, -5f) == 4);
// shares
Check("inner share", VolleyMath.ExtraShare(2, 0.25f, 0.10f) == 0.25f);
Check("outer share", VolleyMath.ExtraShare(3, 0.25f, 0.10f) == 0.10f);
Check("two-shot total", Near(VolleyMath.MaxTotal(3, 0.25f, 0.10f), 1.75f));
Check("three-shot total", Near(VolleyMath.MaxTotal(9, 0.25f, 0.10f), 2.35f));
// layout: inner radius 0.035, outer 0.09, first inner tip straight up
VolleyMath.TipOffset(0, out float x0, out float y0); Check("inner top", Near(x0, 0f) && Near(y0, 0.035f));
VolleyMath.TipOffset(3, out float x3, out float y3); Check("outer first", Near(x3, 0.09f) && Near(y3, 0f));
// blast
Check("centre", BlastMath.Fraction(0f, 1.5f, 5f, 0.5f) == 1f);
Check("inside full", BlastMath.Fraction(1.5f, 1.5f, 5f, 0.5f) == 1f);
Check("edge", Near(BlastMath.Fraction(5f, 1.5f, 5f, 0.5f), 0.5f));
Check("midway", Near(BlastMath.Fraction(3.25f, 1.5f, 5f, 0.5f), 0.75f));
Check("outside", BlastMath.Fraction(5.01f, 1.5f, 5f, 0.5f) == 0f);
Check("nan", BlastMath.Fraction(float.NaN, 1.5f, 5f, 0.5f) == 0f);
// quiver rules (arrowExists, areaLoaded, held, storedElsewhere, quiverCarried, quiverHasRoom)
Check("gone", QuiverRules.Decide(false, true, false, false, true, true) == ReturnAction.Forget);
Check("left behind", QuiverRules.Decide(true, false, false, false, true, true) == ReturnAction.GiveUp);
Check("stored elsewhere", QuiverRules.Decide(true, true, false, true, true, true) == ReturnAction.GiveUp);
Check("held", QuiverRules.Decide(true, true, true, false, true, true) == ReturnAction.Wait);
Check("quiver off", QuiverRules.Decide(true, true, false, false, false, true) == ReturnAction.Wait);
Check("quiver full", QuiverRules.Decide(true, true, false, false, true, false) == ReturnAction.GiveUp);
Check("return", QuiverRules.Decide(true, true, false, false, true, true) == ReturnAction.Return);
```

`Near(a, b)` is `Math.Abs(a - b) < 1e-4f`.

- [ ] **Step 4: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `DrawTimeline` not defined.

- [ ] **Step 5: Implement `Rules.cs`**

Precedence in `QuiverRules.Decide`, first match wins: not exists → `Forget`; area not loaded → `GiveUp`; stored elsewhere → `GiveUp`; held → `Wait`; quiver not carried → `Wait`; no room → `GiveUp`; else `Return`. `ExtrasShowing`: three-shot charged → `3 + min(6, 1 + floor(max(0, t) / 0.2))`; else two-shot charged → 3; else 0. `TipOffset`: index 0..2 at 90/210/330 degrees, radius 0.035; index 3..8 at `(index-3)*60` degrees, radius 0.09.

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (28)`.

- [ ] **Step 7: Make deploy cover all three install locations**

In `Directory.Build.props`, the `DeployMod` target also copies to `$(GameDir)\Modpacks\My mods` when that folder exists. Add a start-up line in `Main.OnInitializeMelon`: `QuickshotRing v<Info.Version> (<server|client>)` using `ApplicationManager.IsHeadless || NetworkSceneManager.IsServer` only after a scene exists; at init use `ApplicationManager.IsHeadless`.

- [ ] **Step 8: Build**

Run: `dotnet build QuickshotRing -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

---

### Task 2: Tree edits and draw timers

**Files:**
- Create: `QuickshotRing/Tree.cs`
- Modify: `QuickshotRing/Lightning.cs` (remove `AddSlot`, `Inject`, `PickPosition`; it is deleted entirely in Task 4)
- Reference: `_decomp/ProfessionSkillTree.cs`, `ProgressionSlot.cs`, `PickupBasedSkillModifier.cs`, `BowStringPullbackStructure.cs`, `HashedGeneralValue.cs`

**Interfaces:**
- Produces:
  - `Tree.Quickdraw`, `Tree.Slowdraw` (`BowDrawSkill?`), `Tree.TwoShot`, `Tree.ThreeShot` (`BowMultiArrowShot?`), `Tree.Holy` (`LightBeamSkill?`), `Tree.QuiverGather` (`ProfessionSkill?`)
  - `Tree.Quickdraw4Level -> int` (number of Quickdraw slots after injection)
  - `Tree.HasQuickdraw4(Player?) -> bool`, `Tree.HasQuiverGather(Player?) -> bool` (server-reliable only)
  - `Tree.Ready -> bool`
  - `class QuiverGatherSkill : ProfessionSkill` (empty body)
- Fixed ids: Quickdraw 4 slot hash `2718281828u` (unchanged from 0.3.0, so the user's unlocked orb survives); Quiver Gather slot hash `1618033988u`; Quiver Gather skill hash `1414213562u`.

- [ ] **Step 1: Move slot injection into `Tree.cs`**

Keep the trigger (Harmony postfix on `ProfessionSkillTree.GetProfessionTree`, run-once, retry while the tree map is empty) and `PickPosition`. Resolve the skill handles by walking the Ranged tree's slots: `Skill is BowQuickdraw`, `is BowSlowdraw`, `is LightBeamSkill`; the two `BowMultiArrowShot` skills are told apart by `arrowConfiguration.ArrowOffsets.Length` (1 = Two-Shot, 2 = Three-Shot, private field, read with `Traverse`).

- [ ] **Step 2: Add the Quiver Gather skill and slot**

`ScriptableObject.CreateInstance<QuiverGatherSkill>()`, `name = "Quiver Gather"`, private `hash` set to `1414213562`, added to the private static `items` dictionary of `HashedGeneralValue<ProfessionSkill>`. Slot: name `Quiver Gather`, cost 1, dependencies = the hash of the slot whose name is `Arrow Gatherer`, icon = that slot's icon for now, position = `PickPosition(tree, arrowGatherer.Position, Vector3.forward * 0.24f)` (the tree's tiers step along +Z; `(0, 0, 0.55)` is taken by Lightbeam, so the helper steps sideways).

- [ ] **Step 3: Re-link Lightbeam**

Set the Lightbeam slot's `dependencies` to exactly `[ThreeShotSlot.Hash]`, remove it from the old parent's `inheritSlots`, call `AddInherit(tree)` again.

- [ ] **Step 4: Change the draw timers**

Set private `minTimeToPullback` on three `BowStringPullbackStructure` assets: Two-Shot's own structure (private `inputStructure` on `PickupBasedSkillModifier`) to `0.5`; Three-Shot's to `1.0`; Holy's (`Tree.Holy.InputStructure`) to `5.0`. Values come from prefs `[Archery] TwoShotSeconds`, `ThreeShotSeconds`, `HolySeconds`.

- [ ] **Step 5: Log**

One line each: `[Archery] Tree: Quickdraw 4 at <pos> (level <n>)`, `Tree: Quiver Gather at <pos> after 'Arrow Gatherer'`, `Tree: Lightbeam now requires '<Three-Shot slot name>'`, `Tree: timers two=<a> three=<b> holy=<c>`. If any handle is null, log an error naming it and leave that feature off; never throw out of the postfix.

- [ ] **Step 6: Build, deploy, play-check**

Run: `dotnet build QuickshotRing -c Release -p:Deploy=true -p:DeployServer=true` (game and server closed).
User check: both logs show the four `Tree:` lines; the shrine shows the Quiver Gather orb joined to Arrow Gatherer and the Holy orb joined to Three-Shot; previously unlocked Quickdraw 4 is still owned; holding a full draw reaches Two-Shot sooner than before and Holy later.

---

### Task 3: Quiver Gather

**Files:**
- Create: `QuickshotRing/QuiverGather.cs`
- Reference: `_decomp/Quiver.cs`, `Alta.Inventory/PickupDock.cs`, `Alta.Inventory/Pickup.cs`, `PickupDocker.cs`, `Embeddable.cs`, `Gatherer.cs` (`MovePickupToPouch`), `Alta.Character/Inventory.cs`, `Timeline.cs`

**Interfaces:**
- Consumes: `Tree.HasQuiverGather(Player?)`, `QuiverRules.Decide(...)`, `ReturnAction`
- Produces: `QuiverGather.Init(MelonPreferences_Category)`; nothing else is called from outside.

- [ ] **Step 1: Track draws from a quiver (server)**

Harmony postfix on `Pickup.OnUndocked` (signature in `Pickup.cs`; arguments are interactor, pickup, dock). Only on the server. Track when: the dock has a `Quiver` component, the pickup has an `Arrow` component, and the interactor's player passes `Tree.HasQuiverGather`. Store per arrow entity id: quiver `PickupDock`, owner `PlayerController`, `shot = false`. Cap 40 tracked arrows per owner (pref `MaxTracked`); the oldest is forgotten first.

- [ ] **Step 2: Arm on the shot**

Postfix on `BowString.OnShoot(Shooter, Pickup, Vector3)`, server only: if the arrow is tracked, set `shot = true` and schedule `Timeline.Instance.CallAfterDuration(() => Tick(id), delay)` with pref `ReturnSeconds` (default 15). Re-drawing and re-shooting a tracked arrow cancels the previous entry (`Timeline.Cancel(ref entry)`) and schedules a new one.

- [ ] **Step 3: Decide and act**

`Tick(id)` gathers the six booleans and calls `QuiverRules.Decide`:
- `arrowExists`: pickup not null and `!Entity.IsBeingDestroyed`
- `areaLoaded`: `pickup.Entity.Chunk != null && pickup.Entity.Chunk.IsLoaded`
- `held`: `pickup.IsInteractedWith`
- `storedElsewhere`: `pickup.IsDocked` into any dock other than its quiver and other than a bow string
- `quiverCarried`: the quiver's own pickup is docked on the owner's back or belt docks, or held by the owner (`PlayerCharacter.Inventory`; mirror the walk described in the spec's research: back, four belt docks, two hands, and items nested in them)
- `quiverHasRoom`: `quiverDock.CanDockAmount(1)`

`Wait` → reschedule in 1 s. `GiveUp`/`Forget` → drop tracking. `Return` → `Embeddable.UnEmbed()` if embedded, `Arrow.StopUpdatingArrowFlight()`, zero the rigidbody velocities, then `pickup.DockInto(null, quiverDock, 1, true)`; a result other than `AllowedResult.Ok` is treated as `GiveUp`.

- [ ] **Step 4: Cancel paths**

Subscribe once per tracked arrow to `Pickup.Docked`: docking into a non-quiver, non-bow-string dock drops tracking (someone stored it). Owner disconnect or death (Review Focus 1): on `Tick`, a null or destroyed owner controller means `Forget`. Wrap `Tick` in try/catch that logs and drops tracking.

- [ ] **Step 5: Client return ghost**

Prefix + postfix pair on the client-side docking read path used by `PickupDocker` (find the method that applies a dock on clients in `PickupDocker.cs`): the prefix records the arrow's world position; the postfix, when the interactor is null, the dock has a `Quiver`, and the recorded position is more than 1.5 m from the dock, plays the same ghost call `Gatherer.MovePickupToPouch` uses (`GlobalSettings<PickupTypeSettings>.Instance.PickupMovingGhost`) from the recorded position to the dock. Skip when `ApplicationManager.IsHeadless`.

- [ ] **Step 6: Log**

Server: `[Archery] Quiver: tracking arrow <id> for <player>`, `Quiver: arrow <id> -> <Return|Wait|GiveUp|Forget> (<which rule>)`. Client: `Quiver: return ghost for arrow <id>`.

- [ ] **Step 7: Build, tests, deploy, play-check**

Run: `dotnet run --project QuickshotRing.Tests -c Release` → `ALL PASS (28)`; then build and deploy as in Task 2.
User check, with Quiver Gather unlocked: shoot three quiver arrows into ground, a wall and a living creature → all three return after 15 s with the ghost; fill the quiver before the timer → arrow stays and never returns later; take the quiver off, wait past 15 s, put it on → arrow returns; shoot then run until the area unloads → arrow stays where it was.

---

### Task 4: Shot damage helper and Quickdraw 4

**Files:**
- Create: `QuickshotRing/Hits.cs`, `QuickshotRing/Quickdraw4.cs`
- Delete: `QuickshotRing/Lightning.cs`
- Rename and cut down: `QuickshotRing/LightningFx.cs` → `QuickshotRing/Fx.cs`
- Modify: `QuickshotRing/Main.cs`
- Reference: `_decomp/Alta.Impact/ImpactTool.cs` (`GenerateImpactOnDetector`), `Alta.Impact/ImpactDetector.cs`, `Embeddable.cs` (private `Embed(Rigidbody, Vector3, Quaternion)`), `Alta.Chunks/LocationChunkHelper.cs`, `Shooter.cs`, `Arrow.cs`

**Interfaces:**
- Consumes: `Tree.Quickdraw`, `Tree.HasQuickdraw4`, `Main.QuickshotReady`
- Produces:
  - `Hits.Apply(ImpactTool tool, RaycastHit hit, float oneOffMultiplier) -> bool` (server; true if something with an `ImpactDetector` was hit)
  - `Hits.Cast(Vector3 from, Vector3 direction, float maxDistance, Transform? shooterRoot, Transform? ignoreItem, out RaycastHit hit) -> bool`
  - `Hits.LoadedReach(Vector3 from, Vector3 direction, float maxDistance) -> float`
  - `Fx.LineMaterial() -> Material?`, `Fx.NewLine(GameObject, int points, float width, bool taper) -> LineRenderer`, `Fx.Zigzag(LineRenderer, Vector3 a, Vector3 b, float amplitude)`, `Fx.Bolt(Vector3 a, Vector3 b, float amplitude, float life, float width, Color head, Color tail)`, `Fx.Flash(Vector3 pos, Color color, float brightness, float range, float life)`, `Fx.FindEffect(string names) -> PooledObjectDefinition?`, `Fx.FindSound(string names) -> SoundEffectSet?`
  - `Quickdraw4.Blue` (`Color`, 0.72/0.9/1.0), `Quickdraw4.LocalPlayerHasIt() -> bool`

- [ ] **Step 1: `Hits.Cast`**

`Physics.RaycastAll`, triggers ignored, nearest hit that is not a child of `shooterRoot` and not a child of `ignoreItem` (Review Focus 4). `LoadedReach` walks the ray in 10 m steps and returns the distance of the last point for which `LocationChunkHelper.GetBestContaining(point)` is non-null and `IsLoaded`, capped at `maxDistance`.

- [ ] **Step 2: `Hits.Apply`**

`detector = hit.collider.GetComponentInParent<ImpactDetector>()`; null → return false. Save `tool.OneOffMultiplier`, set it to `oneOffMultiplier`, call `tool.GenerateImpactOnDetector(detector, 0, 1f, 0, hit.point, hit.normal)`, restore. Catch and log any exception with the detector's name; this call is the untested assumption in the spec and its failure mode must be visible.

- [ ] **Step 3: Server shot**

Keep the existing prefix/postfix pair on `BowDrawSkill.OnArrowShot` (prefix computes "this was a charged Quickdraw by a player with `Tree.HasQuickdraw4`"). The postfix, instead of boosting speed:
1. origin = arrow tip position, direction = arrow forward.
2. `reach = Hits.LoadedReach(origin, direction, pref MaxReach = 150)`.
3. `Hits.Cast(origin, direction, reach, shooter root, arrow transform, out hit)`.
4. Signal clients: `arrow.StartHandlingArrowDeviance(999f)` (the existing flight sync; 999 is the marker, range limit is 1000), then `arrow.StopUpdatingArrowFlight()`.
5. Hit: `Hits.Apply(arrowTool, hit, pref Damage = 3)`, set `OneOffMultiplier = 1`, move the arrow so its tip is at `hit.point` facing `direction`, zero velocities, call the private `Embeddable.Embed(hit.rigidbody, position, rotation)` by reflection, `Entity.ForcePositionSync()`.
6. Miss: move the arrow to `origin + direction * reach`, zero velocities, leave gravity on, `ForcePositionSync()`.

If the arrow is null or being destroyed at any point, stop (Review Focus 2).

- [ ] **Step 4: Client side**

- Postfix on `Shooter.Shoot` (runs for every shot on every client): remember `origin` and `direction` per arrow for 2 s.
- Prefix on `Arrow.StartHandlingArrowDeviance`: on a client, `speed >= 998f` → return false (do not start flight), look up the remembered origin and direction, compute the same reach and cast locally, and draw the line to that end point. If it is the local player's own arrow, set the session flag behind `LocalPlayerHasIt()`.
- Local prediction: in the `Shooter.Shoot` postfix, when not from the network, `Main.QuickshotReady` and `LocalPlayerHasIt()`, do the cast and place the local arrow at the end point at once and draw the line, so the shooter never sees it fly.

- [ ] **Step 5: The line visual**

One world-space `LineRenderer`, two points, width 0.02, colour `Quickdraw4.Blue` with a white start, no taper, full brightness for 0.05 s then alpha to zero over 0.3 s. A single `Fx.Flash` at the end point (range 3, life 0.15) and three 0.1 m `Fx.Bolt` flecks there. Nothing else.

- [ ] **Step 6: Strip the 0.4.0 effects**

`Fx.cs` keeps only what the Interfaces block lists plus the asset dump. Delete `Beam`, `Muzzle`, `Impact`, `Crawl`, `Pulse`, `SparkShower`, `ChargeArcs` and their prefs. `Main.cs`: the ring still turns `Quickdraw4.Blue` when `QuickshotReady && Quickdraw4.LocalPlayerHasIt()`; remove the `ChargeArcs` call.

- [ ] **Step 7: Log**

Server: `[Archery] Q4: line <reach>m, hit '<detector or none>', damage x3`. Client: `Q4: line drawn (local=<bool>)`.

- [ ] **Step 8: Build, tests, deploy, play-check**

Tests still `ALL PASS (28)`; build 0 warnings.
User check: a charged quickshot at a creature shows an instant straight blue line and the creature takes clearly more damage than a normal quickshot; the arrow is stuck in it and can be pulled out; a shot at the sky leaves the arrow falling from the end of the line; with Quiver Gather the arrow comes back; server log shows the `Q4:` line with a detector name. If `Hits.Apply` logged an exception, stop and report it before Task 5.

---

### Task 5: The volley

**Files:**
- Create: `QuickshotRing/Volley.cs`
- Reference: `_decomp/Alta.Progression.Skills/BowMultiArrowShot.cs`, `ExtraArrowUpdater.cs`, `PickupBasedSkillModifier.cs`, `ToolBasedEffectBase.cs`, `Quiver.cs` (`CreateDummyArrow`)

**Interfaces:**
- Consumes: `Tree.TwoShot`, `Tree.ThreeShot`, `Tree.Slowdraw`, `DrawTimeline.ExtrasShowing`, `VolleyMath.*`, `Hits.Cast`, `Hits.Apply`
- Produces:
  - `Volley.Showing(BowString) -> int` (0..9, valid on server and clients)
  - `Volley.IsFull(BowString) -> bool`
  - `Volley.Consume(BowString)` (clears the state; used by Holy Arrow)
  - `Volley.BeginGather(BowString, float seconds)` and `Volley.CancelGather(BowString)` (client visual only)

- [ ] **Step 1: Per-bow state**

`Dictionary<BowString, State>` with `twoShotCharged`, `threeShotCharged`, `threeShotChargedAt` (`Time.time`). `Showing` = `DrawTimeline.ExtrasShowing(two, three, Time.time - threeShotChargedAt)`. Entries are removed when the bow string is destroyed or its dock empties.

- [ ] **Step 2: Server: stop the vanilla extras, record the stage**

Prefix on `BowMultiArrowShot.CreateExtraArrows(BowString)` returning false. Postfix on `BowMultiArrowShot.InputChangedState(bool isEnabled, ISkillInputMethod input)`: resolve the bow string from the private `bowStrings` map; set or clear the flag for whichever of `Tree.TwoShot`/`Tree.ThreeShot` `__instance` is. Because the two modifiers are exclusive, Three-Shot charging cancels Two-Shot: clearing `twoShotCharged` must not reduce `Showing` (the test `three without two still has inner` pins this).

- [ ] **Step 3: Clients: record the stage from the status broadcast**

Postfix on `PickupBasedSkillModifier.SendSerializedMessage` (or the base method it inherits; check which class declares it), reading side only: `__instance` is `Tree.TwoShot`/`Tree.ThreeShot`; `Charged` sets the flag, `Cancelled` and `Consumed` clear it. The message names the nocked arrow by id; resolve the bow string through `NetworkSceneManager.Current.Spawner.WaitForSpawn(id, ...)` and the arrow's current dock (Review Focus 5: if the arrow never spawns or is not docked on a bow string, ignore).

- [ ] **Step 4: Fire**

Prefix on `ExtraArrowUpdater.ShootExtraArrows(BowString, NetworkEntity shotEntity, Vector3 shotVelocity)`; this method runs on the shooter, the server and other clients. `n = Volley.Showing(bowString)`; if 0, do nothing. For each index `< n`: tip target = arrow tip + `right * x + up * y` from `VolleyMath.TipOffset`; direction = `(tipTarget - nockPoint).normalized` where `nockPoint` is the bow string's dock position; launch a virtual arrow with velocity `direction * shotVelocity.magnitude`. Then clear the state for that bow.

- [ ] **Step 5: Virtual arrows**

A single hidden `MonoBehaviour` (`VolleySim`) holding a list of structs: position, velocity, age, index, and (server only) the main arrow's `ImpactTool`, the multiplier, the shooter root. `FixedUpdate`: velocity += `Physics.gravity * dt`; `Hits.Cast` along the step; on a hit the server calls `Hits.Apply(tool, hit, share * slowdrawMultiplier)` where `share = VolleyMath.ExtraShare(index, pref InnerShare, pref OuterShare)` and `slowdrawMultiplier` is the value `BowDrawSkill` gave the main arrow (`tool.OneOffMultiplier` read once at launch, before any hit resets it). Each virtual arrow is removed on its first hit or at age 3 s. No virtual arrow ever checks another (spec: extras never interact).

- [ ] **Step 6: Client ring**

While `Showing > 0` for a bow, keep a pooled set of up to nine ghost objects, updated in one `LateUpdate` per bow: each is a copy of the nocked arrow's head and shaft `MeshRenderer`s only (no fletching), using the base material of `GlobalSettings<ProgressionSettings>.Instance.ToolBasedEffect`'s prefab. Each ghost's tip sits at its `TipOffset` position; it points along its own launch direction; it is clipped or faded so that nothing is drawn within the rear 60% of its length (pref `VisibleFraction = 0.4`). Inner three at full brightness; outer six at 70% scale thickness and 55% brightness. Arrival: slide forward from the real arrow's tip over 1/6 s while alpha goes 0 → 1. If the skill material cannot be found, fall back to `Fx.LineMaterial()` with the Holy/skill colour from that effect's prefab and log it once.

- [ ] **Step 7: Client flight and impact**

On fire, each ghost becomes fully visible along its whole length and is moved by the same `VolleySim` step; on its hit, one `Fx.Flash` (range 1.5, life 0.12) and the ghost returns to the pool. No trails.

- [ ] **Step 8: Log**

Server: `[Archery] Volley: armed <n> on bow <id>`, `Volley: fired <n>, hits <h>, total x<sum of applied multipliers>`. Client: `Volley: showing <n>` on each change.

- [ ] **Step 9: Build, tests, deploy, play-check**

User check: holding a full draw shows three ghosts at about 2.25 s, then six more one at a time; they do not spin and nothing glows next to the eye; releasing early fires exactly what was showing (compare the server's `fired <n>`); a full volley into one close target kills faster than a single charged arrow; two players drawing at once do not affect each other's ring (Review Focus 3). Check the real arrow's durability after several volleys and report whether it wore faster than normal.

---

### Task 6: Holy Arrow

**Files:**
- Create: `QuickshotRing/HolyArrow.cs`
- Reference: `_decomp/Alta.Progression.Skills/LightBeamSkill.cs`, `ExtraArrowUpdater.cs` (`CreateExtraArrow`), `Projectile.cs` (`Shoot`, `OnCollision`, the client handler for `collidedSync`), `Explosive.cs` (`Trigger`), `Alta.Impact/ImpactHit.cs`, `HealthObject.cs`

**Interfaces:**
- Consumes: `Tree.Holy`, `Tree.Slowdraw`, `Volley.IsFull`, `Volley.Consume`, `Volley.BeginGather`, `Volley.CancelGather`, `BlastMath.Fraction`, `Fx.*`
- Produces: `HolyArrow.Gold` (`Color`, 1.0/0.86/0.45); `HolyArrow.Init(MelonPreferences_Category)`

- [ ] **Step 1: Gate on the full volley (server)**

Prefix on `LightBeamSkill.HandleInputChanged(bool isEnabled, ISkillInputMethod input, BowString bowString)`: when `isEnabled` and `!Volley.IsFull(bowString)`, return false (no beam is created; releasing fires whatever the volley has). When it proceeds, call `Volley.Consume(bowString)` after the original so no extras fire with the beam.

- [ ] **Step 2: Gathering visual (clients)**

The Holy timer is one number (5.0 s), so clients start the gathering themselves: when a bow's `Showing` reaches 9, call `Volley.BeginGather(bowString, DrawTimeline.GatherSeconds)`: the outer six, then the inner three, slide back into the real arrow's tip one after another across the window, the real arrow's glow stepping up each time. On the Lightbeam `Charged` broadcast: one `Fx.Flash` in `HolyArrow.Gold` at the tip and a `TriggerHaptic(1f, 0.2f)` on both of the local shooter's controllers. If the string is released or relaxed first, `Volley.CancelGather` snaps all nine back out before the volley fires (spec: early release fires the full nine).

- [ ] **Step 3: Beam damage and speed**

- Postfix on `ExtraArrowUpdater.CreateExtraArrow(Vector3, NetworkPrefab extraArrowPrefab, float damageMultiplier)`: when `extraArrowPrefab` is `Tree.Holy`'s private `beamPrefab`, set the beam `ImpactTool.OneOffMultiplier = pref DirectMultiplier (4) * slowdrawMultiplier * nockedArrowTool.MaxDamage`, where `slowdrawMultiplier` is `Tree.Slowdraw`'s protected `GetDamageMultiplier(player)` called by reflection. Remember `beam entity id -> (shooter PlayerController, blastCentreDamage = pref BlastMultiplier (2) * slowdrawMultiplier * MaxDamage)`.
- Prefix on `Projectile.Shoot` (all machines): if the projectile's prefab hash equals the beam prefab's, multiply the velocity argument by pref `SpeedMultiplier` (5) and set `Rigidbody.useGravity = false`.

- [ ] **Step 4: Tunnelling backstop (server)**

A small component added to the beam at `Shoot`: each `FixedUpdate`, `Hits.Cast` from the previous position to the current one; if it finds an `ImpactDetector` the projectile has not already collided with, move the projectile to the hit point and invoke its collision path so the normal direct hit and the blast both happen there.

- [ ] **Step 5: The blast (server)**

Prefix on `Projectile.OnCollision` for remembered beams, once per beam. At the collision point: `Physics.OverlapSphere(point, pref Radius = 5)`, skip triggers, group by `GetComponentInParent<ImpactDetector>()` exactly as `Explosive.Trigger` does (health targets once each, bare detectors otherwise). Skip the directly hit target. For each: `f = BlastMath.Fraction(distance, pref FullRadius = 1.5, Radius, pref EdgeFraction = 0.5)`; build `new ImpactHit(blastCentreDamage, pref BlastCripple = 0.5, 1f, f, null, ImpactType.Spiritual, point, Vector3.zero, Vector3.zero, null, beamEntity, shooter)` and deliver with `health.DetectImpact(hit)` or `detector.OnImpactHit(hit)`. `AddExplosionForce(pref BlastForce = 300, point, Radius)` on attached rigidbodies. Players and the shooter are included (spec). If the shooter controller is gone (Review Focus 1), pass null as wielder and still apply the blast.

- [ ] **Step 6: Trail (clients)**

From `Projectile.Shoot` until the collision: record the beam's position each frame; draw one gold `LineRenderer` along the recorded path, re-sampled every 0.6 m, each interior sample offset sideways by up to 0.12 m with offsets re-rolled every 0.05 s (sharp kinks, not a ruled line), plus one 0.5 m `Fx.Bolt` side fork per 4 m of path. After the collision it holds 0.5 s while fading. Width 0.03, white core start, `HolyArrow.Gold` end.

- [ ] **Step 7: Impact and falloff (clients)**

On the client's projectile-collided handler for a beam: a core `Fx.Flash` (range `FullRadius * 2`, life 0.2); one ground ring (unit circle `LineRenderer` via `Main.BuildCircle`, laid on the surface normal found by a short raycast) expanding from 0.3 m to `Radius` over 0.45 s with alpha `BlastMath.Fraction(currentRadius, ...)` so it visibly dims toward the edge and ends there; eight `Fx.Bolt` wisps whose length and alpha scale with `Fraction` at their start distance (long and bright near the centre, short and faint near the edge). Then the game's own effect and sound through `Fx.FindEffect(pref ImpactEffects)` and `Fx.FindSound(pref ImpactSounds)`, defaults taken from `UserData\LightningArrow_assets.txt` after the first run that produces it. Nothing persists past 0.6 s.

- [ ] **Step 8: Stun marker (clients)**

At the same moment, each client finds health targets within `Radius` of the point the same way as Step 5 and attaches a marker for `4 s * Fraction`: bursts of two or three `Fx.Bolt` arcs (three or four kinks, width 0.008, life 0.06 s, `HolyArrow.Gold`) between random points on that target's renderer bounds, a burst every 0.15 s at first, stretching to every 0.6 s by the end. A target that is destroyed ends its marker.

- [ ] **Step 9: Log**

Server: `[Archery] Holy: armed on bow <id>`, `Holy: fired, speed <v>`, `Holy: blast at <p>, <n> targets, centre <d>, cripple <c>`; for the first player caught, also log `CrippleStat` before and after so the stun can be tuned. Client: `Holy: trail`, `Holy: impact using '<effect>' / '<sound>'`, `Holy: stun marker on <n>`.

- [ ] **Step 10: Build, tests, deploy, play-check**

User check: Holy cannot be reached without Three-Shot; at 3.75 s the nine start folding in and at 5 s there is a flash and a buzz; releasing at 4.5 s fires nine arrows and keeps the real arrow; the Holy shot is visibly a fast gold bolt with a jagged trail, not a blue line; a group of creatures all take damage and get knocked down; standing inside 5 m hurts and slows the shooter; a creature at the edge takes clearly less than one at the centre; the ground ring's reach matches where damage stopped. Report the logged `CrippleStat` numbers.

---

### Task 7: Icons

**Files:**
- Modify: `QuickshotRing/Art/make_icons2.py`, `QuickshotRing/Art/make_lightning2.py`; `QuickshotRing/Tree.cs`
- Create: `QuickshotRing/Resources/QuiverGather.png`, `TwoShot.png`, `ThreeShot.png`, `HolyArrow.png`, `Quickdraw4.png`

**Interfaces:**
- Consumes: reference icons in `QuickshotRing/Art/game_icons/` (`Glyph_Bow_*.png`, `Glyph_Gatherer_*.png`)
- Produces: embedded resources named `QuickshotRing.Resources.<Name>.png`

- [ ] **Step 1: Redraw the two top-tier icons**

Quickdraw 4: top-tier frame, the vanilla bow-tip spark, the vanilla Quickdraw chevrons untouched, then one bold ruler-straight horizontal line running to a small arrowhead near the ring. No jagged shapes. Holy Arrow: vanilla beam frame and beam, with the beam's outline given three sharp lightning kinks and short forked wisps, a burst at the tip, and small arrows folding into its tail.

- [ ] **Step 2: Comparison sheet and review**

Run: `python QuickshotRing/Art/make_icons2.py QuickshotRing/Art/game_icons QuickshotRing/Art/drafts`
Send the user `drafts/sheet2.png` (real icons above, new below). Iterate until the user accepts all five. Do not proceed on silence.

- [ ] **Step 3: Ship**

Copy the five accepted PNGs into `QuickshotRing/Resources/`. In `Tree.cs`, load each with `ImageConversion.LoadImage` into a `Texture2D` (512x512, `filterMode` and wrap copied from the vanilla icon it replaces) and assign to the slot's `slotIcon`: Quickdraw 4, Quiver Gather, Two-Shot, Three-Shot, Lightbeam. Skip on headless. A missing resource logs an error and leaves the vanilla icon.

- [ ] **Step 4: Build, deploy, play-check**

User check: all five orbs show the new icons at the shrine at the same size and brightness as their neighbours.

---

### Task 8: Finish

**Files:**
- Modify: `QuickshotRing/Main.cs` (version `0.5.0`), `CLAUDE.md`

- [ ] **Step 1: Settings pass**

Confirm every number in the Numbers table is a pref under `[Archery]`, `[QuiverGather]`, `[Quickdraw4]`, `[Volley]` or `[HolyArrow]`, with the table's value as default. Remove the old `[LightningArrow]` category's unused entries.

- [ ] **Step 2: Update `CLAUDE.md`**

Replace the QuickshotRing section with the new file list and one paragraph per feature; add the facts learned: stun is cripple damage on `ImpactHit`; damage for non-flying shots goes through `ImpactTool.GenerateImpactOnDetector`; the 999 flight-speed marker; where the icon scripts live; the test command.

- [ ] **Step 3: Final verification**

Run: `dotnet run --project QuickshotRing.Tests -c Release` → `ALL PASS (28)`
Run: `dotnet build QuickshotRing -c Release -p:Deploy=true -p:DeployServer=true` → `0 Warning(s)`, `0 Error(s)`
Confirm the three installed DLLs have the same hash as `QuickshotRing\bin\Release\QuickshotRing.dll`.
User check: one session touching all four features; both logs free of `[ERROR]` lines mentioning `QuickshotRing` or `[Archery]`.
