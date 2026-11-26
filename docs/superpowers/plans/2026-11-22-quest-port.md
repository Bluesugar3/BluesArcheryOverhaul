# Quest Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `BluesArcheryOverhaul.Quest.dll`: Blue's Archery Overhaul for Quest players, playing every skill by the same rules as the PC game on the same server.

**Architecture:** One codebase, two projects. `BluesArcheryOverhaul.Quest` (net8.0, IL2CPP) compiles the PC mod's own source files with `QUEST` defined. Every difference is either in the Quest project's own files (names, the platform helpers' Quest half, start-up) or an `#if QUEST` alternative beside a PC line that itself stays byte for byte as it is. The port goes in by file list: a file joins the Quest compile when its phase starts, and each phase ends with a check on the owner's headset.

**Tech Stack:** C# net8.0, MelonLoader 0.6.5 for Android, Il2CppInterop 1.4.6, HarmonyX 2.13, `adb`.

**Spec:** `docs/superpowers/specs/2026-11-20-quest-port-join-gate-design.md` (Piece 3) and `docs/superpowers/specs/2026-11-22-quest-spike-report.md` (what the headset test showed; read it first).

## Global Constraints

- **The PC mod does not change.** In a shared file a PC line is never edited, moved or deleted: Quest code is added as `#if QUEST ... #else <the PC lines, untouched> #endif`, or as a new member. After every task: `dotnet build BluesArcheryOverhaul -c Release` gives 0 warnings and 0 errors, `dotnet run --project BluesArcheryOverhaul.Tests -c Release` gives `ALL PASS`, and `git diff main -- BluesArcheryOverhaul | grep '^-[^-]'` shows no removed PC line.
- Work on branch `quest-port` in the worktree `.claude/worktrees/quest-port`. This session stages, installs and pushes nothing for PC; merging `quest-port` into `main` is the PC session's ("Private GitHub page and mod documentation"), in batches it agrees to. Rebase on `main` at the start of every phase (the PC mod changes daily). No Claude attribution lines in commits.
- The Quest references (`C:\ATTUnity\QuestRefs`) are private: never in the repo.
- On the headset only `Mods/BluesArcheryOverhaul.Quest.dll` and the mod's own files under `UserData` are written.
- Server-only files are never compiled for Quest: `SkyfallCommands.cs`, `SkyfallServer.cs`, `Telemetry.cs`, `PlayerCare.cs`. A server-only member inside a shared file is put under `#if !QUEST` only when it does not compile for Quest; otherwise it stays (it never runs on a headset).
- A Quest game must never behave differently from a PC game in what a shot does: same claim, same client-side flight, same build bits read. A piece that cannot be ported is brought to the owner; it is not left out silently.
- The Quest DLL compiles `JoinWire.Version` from the same commit as the server's build. Before a headset check, the branch is rebased onto the commit the installed server DLL was staged from.
- Asset lookups run in the world, never at the menu. Every delegate handed to the game is kept alive in a static field or list. Own components are registered with `ClassInjector.RegisterTypeInIl2Cpp<T>()` at start, never with MelonLoader's attribute.
- Every look keeps its fail-soft rule: a look that cannot be built on Quest logs one line and is off; it never throws into a game callback.
- Headset steps are given to the owner in game terms. The owner's standing rule on looks applies on Quest too: no camera movement, at most 3 bright flashes a second.
- Build: `dotnet build BluesArcheryOverhaul.Quest -c Release "-p:QuestReferences=C:\ATTUnity\QuestRefs\ATT-Quest-Modding-dlls\questdlls"`; 0 errors. Warnings from unused members of shared files are expected until the last phase.

## Review Focus

1. **A cast that compiles and gives null on Quest** (`x as T`, `x is T y` on a game object). Expected: no bare `as`/`is` on a game type runs on Quest; each goes through `Il.As` / `Il.Is` (Task 1), and Task 1's check greps for the rest.
2. **A Harmony patch that silently does not apply** (inlined or missing on Quest). Expected: the Quest start-up applies patches one class at a time and logs `[Archery] Quest: N of M patches applied` naming each failure (Task 1).
3. **The game's collections** (`Il2CppSystem` lists, dictionaries, arrays handed to Unity). Expected: a `NonAlloc` buffer is an Il2Cpp array kept by the mod, never a managed array converted per call (it would be a copy, and the results would be lost).
4. **A delegate the garbage collector takes** while the game still holds it: a crash minutes later. Expected: every one is stored (Task 1's `Il.Keep`).
5. **Server and Quest disagree on a shot** because a client-side piece was skipped. Expected: phase 2's check compares the server's `Release:` lines for the same shots from PC and from Quest.

---

## What the conversion is (from the first whole-mod compile, 2026-11-22)

54 game-facing files, 26,193 lines. 191 compile errors in 39 files, plus what compiles but would not work: 106 `___field` patch parameters, about 100 casts, 92 reflection uses, 64 event subscriptions, 22 components, 6 `NonAlloc` calls. About 500 places. The table says how each kind is converted; Task 1 builds the helpers, the phases apply them file by file.

| Kind on PC | On Quest | How |
|---|---|---|
| `using Alta.X;`, bare game types | `Il2CppAlta.X`, `Il2Cpp.Type` | `QuestNames.cs` (done): the PC namespaces declared empty, the Quest ones as global usings. No edit in shared files. |
| A name written out in full (`Alta.Serialization.Stream`, `global::Player`) | does not exist | The PC line stays. Its `#if QUEST` alternative names the Quest type (`Il2CppAlta.Serialization.Stream`, `Il2Cpp.Player`). |
| `Postfix(BowString __instance, Interactor ___holder)` | no `___` injection | `#if QUEST` signature without it and a first line `Interactor ___holder = __instance.holder;`; `#else` the PC signature. The body is shared. |
| `x as T`, `x is T y` on a game object | null / false | `Il.As<T>(x)`, `Il.Is(x, out T y)`: on PC the plain cast, on Quest `TryCast<T>()`. Used only in the `#if QUEST` alternative of the line. |
| `Player` passed as `IPlayer`, an `IPlayer` matched as `Player` | an interface is a class | `Il.Iface<IPlayer>(player)` (`Cast<IPlayer>()` on Quest), `Il.As<Player>(iplayer)`. |
| `Traverse.Create(o).Field("f")`, `AccessTools.Field(...)` | fields are properties of the proxy | the member itself (`o.f`) in the Quest alternative; where the name is data, `Il.Member<T>(o, "f")` (reflection on the proxy's property). |
| `thing.Event += Handler;` | `add_Event(converted delegate)` | `Il.Keep(DelegateSupport.ConvertDelegate<...>(...))` and `add_` / `remove_` in the Quest alternative. |
| A lambda or method handed to the game (`SetHandler`, `Send`, callbacks) | needs the game's delegate type | the implicit operator or `ConvertDelegate`, kept with `Il.Keep`. |
| `class X : MonoBehaviour` | must be registered, needs an `IntPtr` constructor | `#if QUEST public X(IntPtr p) : base(p) { } #endif` in the class; one list in the Quest start-up registers them all. |
| `foreach` / LINQ over a game collection | `Il2CppSystem` types | an index loop in the Quest alternative, or `Il.Each(list)` (an adapter that yields the items). |
| `Physics.RaycastNonAlloc(ray, RaycastHit[] buffer, ...)` | a managed array would be copied | `Il.Buffer<T>`: an `Il2CppStructArray<T>` the mod keeps and reads back by index. |
| `StartCoroutine` (1 use) | needs a wrapped enumerator | `MelonCoroutines.Start(...)` in the Quest alternative. |

---

### Task 1: The platform layer and a mod that starts

**Files:**
- Create: `BluesArcheryOverhaul/Il.cs` (shared; PC half is plain C#, Quest half under `#if QUEST`): `Il.As`, `Il.Is`, `Il.Iface`, `Il.Member`, `Il.Keep`, `Il.Each`, `Il.Buffer<T>`
- Create: `BluesArcheryOverhaul.Quest/QuestMain.cs` (the Quest entry while the port is in phases), `BluesArcheryOverhaul.Quest/QuestPatches.cs`
- Modify: `BluesArcheryOverhaul.Quest/BluesArcheryOverhaul.Quest.csproj` (an explicit list of the shared files compiled so far, in place of `*.cs`)
- Modify (shared, `#if QUEST` only): `Cfg.cs`, `TimingHost.cs`, `StreamBits.cs`, `SelfCheck.cs`, `JoinGate.cs`, `ServerSettings.cs`

**Interfaces:**
- Produces: `Il.*` as above; `QuestPatches.ApplyAll(HarmonyLib.Harmony)` (every `[HarmonyPatch]` class of the assembly applied on its own, failures logged, a count returned); `QuestMain` (`[MelonGame("Alta", "Att_Quest")]`, the same `MelonInfo` name and `Main.Version`).

- [x] **Step 1:** `Il.cs`, with the Quest half as the spike's forms (the report lists the line that worked for each). `Il.Keep` adds to a static list and returns its argument.
- [x] **Step 2:** The csproj's explicit list: the sixteen Unity-free files, `Il.cs`, `Cfg.cs`, `Timing.cs`, `TimingHost.cs`, `StreamBits.cs`, `SelfCheck.cs`, `JoinGate.cs`, `ServerSettings.cs`. `Main.cs` is NOT in it until Task 5.
- [x] **Step 3:** `QuestMain.OnInitializeMelon`: the start-up line `[Archery] Blue's Archery Overhaul v<Main.Version> (Quest)`, `Logs.Init`, `TimingHost.Init`, the `Init()` of each module compiled so far in `Main.cs`'s order, `Cfg.Ready`, component registration, `QuestPatches.ApplyAll`, the self-check. `OnUpdate`: `TimingHost.Frame`, `Cfg.Poll`, `ServerSettings.Tick`, the ticks of the modules compiled so far. `SelfCheck.Run` reads only holders that are compiled (`#if QUEST` list).
- [x] **Step 4:** `StreamBits` on Quest reads `BitsLeft` / `Finished` from `StreamReader.reader`. `JoinGate`'s client half answers with `JoinPlatform.Quest`; its server half is under `#if !QUEST`. `ServerSettings`' client half applies the server's settings and sends the SettingsAck; its server half is under `#if !QUEST`.
- [x] **Step 5:** PC check (Global Constraints, first bullet). Quest build: 0 errors.
- [x] **Step 6: Headset check.** Server gate on `refuse`, `[Join] SettingsMismatch = log` (see "Done" below). Owner: join on the headset. Expected in game: you get in, at the same speed as without any mod. Executor reads the headset log (`[Archery] ... (Quest)`, `Settings: shared checksum`, `Self-check:`, `Quest: N of N patches applied`, `Settings: this server sets the mod's settings`) and the server log (`Join: Blue passed (..., Quest, ...)` and `took the server's settings`). Then put `SettingsMismatch` back to `log`.
- [x] **Step 7:** Commit.

**Done 2026-11-22** (commit 22680cf). The headset joined under `refuse`: server `Join: Blue passed (client 0.19.10, wire 5, Quest, settings DIFFERENT, answered in 100 ms)`; headset `Quest: 1 of 1 patches applied`, `this server sets the mod's settings`, no error. As built, not as first planned:
- `Il.cs` lives in the Quest project, not the shared folder: the PC never calls it, so the PC DLL gains nothing. Its `Send` / `SetHandler` extension methods make the shared lines compile and work unchanged (C# looks for an extension only when no instance method fits).
- `SelfCheck.cs` is not compiled yet (it reads every holder of the mod); `QuestMain` reads the holders that are compiled. `SelfCheck.cs` joins when its holders do.
- Members of files not yet compiled are stood in for by `QuestStubs.cs` (each deleted in the task that adds its file: a duplicate type is a compile error, so none can outlive it).
- `Main.Version` is read from `Main.cs` by the project file at build time (`obj/.../QuestVersion.g.cs`).
- **Until the port is complete, the server's `[Join] SettingsMismatch` must stay `log`**: a Quest build that lacks a module also lacks its settings, so its settings check differs from the server's (today: "1 of the server's values could not be taken (1 unknown to this copy of the mod)"), and `refuse` would turn the headset away.

### Task 2: The tree and its icons

**Files (join the Quest compile):** `Tree.cs`, `Paths.cs`, `Builds.cs`, `Fx.cs` (asset and texture lookup), `VolleySkill.cs`, `KitHooks.cs`

What the owner saw missing first: the mod's orbs with the game's icons. This is the first thing a Quest player sees, and the tree's skills (the Volley skill, id 47011) must exist on the client for the bow statuses of Task 3.

- [x] **Step 1:** Convert each file by the table. `Tree.cs` has the most compile errors (21): slot and skill lists are game collections, icons are textures loaded from the DLL's embedded PNGs (`ImageConversion.LoadImage`: check it is not stripped; if it is, the icons are loaded with the game's own texture path the report's assets list offers, and that is brought to the owner as a finding).
- [x] **Step 2:** PC check; Quest build.
- [x] **Step 3: Headset check.** Owner: go to the archery shrine and open the tree. Expected in game: the 16 orbs with the mod's icons (Quickdraw 4, Two-Shot, Three-Shot, Burst, Holy Arrow, Auto Dock, Ricochet, Seeking Volley, Shatter, the gatherer), the same as on PC. Buy one orb, leave, rejoin: it is still owned. Executor reads `Tree: ready on the client`.
- [x] **Step 4:** Commit.

**Done 2026-11-22** (branch `quest-port`, commits up to 0d6df66; the owner's screenshot `docs/quest-shots/shrine-2026-11-22-0111.jpg`: all 16 orbs with the mod's icons). Found on the headset, each now a rule for the rest of the port:
- **Object identity.** The interop gives a NEW managed object each time a game object is read: its object cache (`Il2CppObjectPool`) is switched off because the garbage-collector hook it needs is not found in this game build (`GarbageCollector_RunFinalizer_Patch.TargetMethodNotFound` sets `DisableCaching`). Forcing it on could hand back stale objects. So every `ReferenceEquals` on game objects becomes `Il.Same` (compares `Pointer`), every `Dictionary`/`HashSet` keyed by a game type gets `Il.ByPointer<T>()`, and `==`/`!=` between plain (non-Unity) game objects becomes `Il.Same`. About 80 `ReferenceEquals` and 90 such collections in the whole mod. Symptom seen: only one Charge Shot orb of three found, so Shatter was left out.
- **Audit of 2026-11-22 (four reviewers, every file; fixes in commits "Quest audit, part 1-3").** Classes that compiled and were dead or dangerous on Quest: (1) `Physics.*NonAlloc` with a managed buffer: the array is copied in, so the hits never come back; use `Il.RaycastNonAlloc` / `SphereCastNonAlloc` / `OverlapSphereNonAlloc`. (2) The same game object read twice is two managed objects, Unity objects included (logged by `QuestChecks`): a managed `List.Contains/Remove`, `HashSet` or `Dictionary` of game objects needs `Il.Has` / `Il.Drop` / `Il.ByPointer`; only `==` on Unity objects and `Il.Same` compare. (3) The mod's own `MonoBehaviour`s must be registered (`QuestPatches.RegisterComponents`), each with an `IntPtr` constructor, every method that is not a Unity message marked `[HideFromIl2Cpp]`, and no two with the same name. (4) A patch on a struct's method never runs (`ShootArguments.Serialize`, `PlayerTextMessage.Serialize`): hook the caller. (5) Server-only patches are left out with `#if !QUEST` round the attribute. (6) A game `params` argument left out is null on Quest. Still unproven on the headset: the `PickupDocker.OnPreDocked` patches (a one-line method that may be inlined; look for `[Archery] Quest: reached: PickupDocker.OnPreDocked`), `foreach` over `ImpactTool.ImpactorsPerCollider`, per-frame `SetPositions` / `Gradient.SetKeys` allocations, and `Il.Keep` growing by one delegate per skill status.
- **A class the mod registers with the game is invisible to the game's collector (2026-11-25).** Il2CppInterop builds it without the "holds references" flag or field map, so anything only a QuestPassiveSkill points to (the later orbs' try-out lists) was freed; opening Auto Dock's or Shatter's try-out then read freed memory and the game died. `QuestSkills.Pin` keeps the mod's own wrapper of each (a strong handle). Any new game object reachable only from an instance of a registered class needs the same. Also: never walk `HealthObject.ImpactDetectors` (a non-generic IEnumerable) with `foreach` on Quest; the game's list enumerator threw "Collection was modified" (`Il.Detectors` reads the list by index). The mod creating its own game-side array (`new Il2CppStructArray<T>`) ended the process twice; line points are set one at a time (`Il.SetPositions`).
- **Headset timing, 2026-11-25 (`[Timing] Enabled` on the headset, 14 reports):** the frame budget is about 14 ms; the drops after a shot are the mod's own work: building ghost arrows on every draw (about 7 ms each, not pooled), the Fill about 2 ms every held frame, a release about 27 ms, a landing up to 116 ms, Shatter's blast look about 115 ms, each new light about 5 ms, and `Fx.FindEffect` / `FindSound` searches (20-30 ms each) repeated 12 and 15 times. Next: pool ghosts and lights, cache the searches' misses, trim the Fill.
- **Quest runs quiet and lean (owner, 2026-11-22: "turn off the logs for everything that takes a small cut of performance").** `[Log] Level` defaults to Quiet there; every port trace (`QuestWatch`, `QuestTrace`) is off unless `UserData/BluesArcheryOverhaul/quest-watch.txt` exists. The mod making a game-side `RaycastHit` or `Collider` array ended the game process (Skyfall's first ground cast; cause unknown), so `Il.*NonAlloc` ask the game for the hits (`RaycastAll`, `SphereCastAll`, `OverlapSphere`), and `Hits.Cast` and `Skyfall.GroundAt` first ask for the single nearest hit (`Physics.Raycast`, no array) and take the full list only when that hit must be passed over. `LineRenderer.SetPositions` and `Gradient.SetKeys` go through `Il.SetPositions` / `Il.SetKeys` (one kept game array per length; a managed array would be converted to a new one on every call). Quiet sounds play at full volume on Quest. Not measured on the headset after these changes.
- **Never patch an empty game method.** `PickupBasedSkill.SerializeInternal` is empty: a few bytes of native code, smaller than the patch's jump, so patching it overwrote the code beside it and the game died (signal 11) at the first bow-skill status, a little after the patch ran. Patch its caller instead (the build bits are read in a postfix on `SendSerializedMessage`). One-line targets still to watch if a crash follows their first call: `BowString.OnShoot`, `PickupDocker.OnPreDocked`, `ProfessionSkill.ReleaseEffect`, `Projectile.Shoot`, `Arrow.Shoot`, `PlayerMessageDisplay.Display`.
- **AOT generics.** A game collection's generic code exists only for the instantiations the game compiled. Walking `ProfessionSkillTree.treeMap.Values` (keyed by an enum) aborted the game natively (`FORTIFY: pthread_mutex_lock called on a destroyed mutex`, no managed exception). Even `PlayerProgressionManager.PathExperiences.TryGetValue` (keyed by the enum `ProgressionPath`) aborted the same way when the tablet asked whether the held orb was practised (the try-out crash); use the game's own `pm.IsSlotPracticed(slot)` and `pm.GetSlots()` instead. Prefer the game's own methods over reading or enumerating its enum-keyed collections at all; a collection keyed by a reference type or `uint` is usually safe (shared code).
- **Making game objects.** `ScriptableObject.CreateInstance<ProfessionSkill>()` gives null (no script for a class with no asset); the later orbs use `QuestPassiveSkill`, registered with `ClassInjector` at start.
- **Arrays in generic game classes** are the abstract `Il2CppArrayBase<T>`: the reflection stand-in makes `Il2CppStructArray`/`Il2CppReferenceArray`.
- **Trace lines** (`QuestTrace.Step`) find a native crash's place: the log keeps the last line before the abort.
- Approach change for task 3 on: the rest of the mod is compiled for Quest at once (Volley.cs calls into nearly every other file, so per-file stand-ins stopped paying), and still checked on the headset feature by feature.

### Task 3: The shot (gameplay parity, part 1)

**Files (join the Quest compile):** `Draws.cs`, `Claims.cs`, `Hits.cs`, `Targets.cs`, `Steady.cs`, `Quickdraw4.cs`, `Volley.cs`, `VolleyFx.cs`, `Burst.cs`, `Pvp.cs`, `QuiverGather.cs`, `AutoDock.cs`, `Weight.cs`, `HeldArrow.cs`

Everything the shooter's own game must do for the server to decide the same release as for a PC shooter: the draw clock, the claim at the end of the shot message, the client-side flight (Steady's speed, the Quickdraw 4 line, the volley's virtual arrows).

- [ ] **Step 1:** Convert by the table, in the order above (each file compiles only with the ones before it). `Draws.cs` (19 errors) and `Volley.cs` (2,334 lines, the patch on `ExtraArrowUpdater.ShootExtraArrows`) are the two large ones. `Hits.cs`'s raycast buffers become `Il.Buffer<RaycastHit>`.
- [ ] **Step 2:** PC check; Quest build.
- [ ] **Step 3: Headset check, with the PC game on the same server** (two accounts, both the owner's). On the headset, with a test build of each main path in turn: a plain shot, a quick shot, the Quickdraw 4 line, a two-shot and three-shot volley, Burst, a held and a fully charged shot. Expected in game: each looks at least like its plain form (ghost arrows fly, the line shows; the richer looks come in Task 5) and hurts a creature. Executor compares the server's `Release:` lines for the Quest shots with the same shots from PC: the same kind, stage and counts.
- [ ] **Step 4:** Commit.

### Task 4: The legendaries and the later orbs (gameplay parity, part 2)

**Files (join the Quest compile):** `HolyArrow.cs`, `Homing.cs` is already in, `Seek.cs`, `Ricochet.cs`, `Shatter.cs`, `Skyfall.cs`, `SkyfallLink.cs`, `Shrine.cs`

- [ ] **Step 1:** Convert by the table. Each of these has a client half that decides or claims something: Holy Arrow's armed flag and release strength, Seeking Volley's lock (the claim's lock list) and held arrow, Skyfall's ask and held arrow, the latch from Skyfall's signal.
- [ ] **Step 2:** PC check; Quest build.
- [ ] **Step 3: Headset check.** Holy Arrow (hold 5.5 s, release: the beam, and a watching PC player sees it); Seeking Volley (a lock forms and the volley homes; plain rings stand in for the cage until Task 5); Ricochet and Shatter on a pack; Skyfall after `skyfall give Blue` on the server console (the storm falls where aimed). Executor reads the server's lines for each and the headset's log for errors.
- [ ] **Step 4:** Commit. **Gameplay parity is reached here: from this commit a Quest player is not at a disadvantage.**

### Task 5: The looks, one family at a time

**Files (join the Quest compile):** `Main.cs` (the Quickdraw ring; `QuestMain.cs` is retired and `Main.cs` gains its Quest start-up under `#if QUEST`), `ChargeFx.cs`, `SnapWeight.cs`, `GoldHold.cs`, `Balance.cs`, `SeekLook.cs`, `SeekCage.cs`, `SeekFx.cs`, `SkyfallSigil.cs`, `SkyfallHold.cs`, `SkyfallLook.cs`, `SkyfallNail.cs`, `SkyfallSky.cs`

- [ ] **Step 1:** One family per round: the ring; the Charge line (Fill, thump, embers, Holy wind-up); the volley's gold stages and snap weight; the homing looks; Skyfall. Convert, build, push, and the owner looks at it on the headset beside the PC game.
- [ ] **Step 2:** For each family, read the headset's `[Timing]` report (`[Timing] Enabled = true` in the headset's cfg): the mod's share of a frame and the worst frames. Where a look costs too much on the headset, its Quest default is lowered (pool sizes, counts, lights; every look setting is already per client) under `#if QUEST` in that look's `CreateEntry` defaults, and the change is listed for the owner. A look that cannot run well ships switched off on Quest by default, said in the release notes.
- [ ] **Step 3:** PC check after each family; commit after each family.

### Task 6: The shrine view

**Files (join the Quest compile):** `ShrineView.cs`, `TreeColours.cs`, `Tablet.cs`, `Tutorial.cs`

- [ ] **Step 1:** Convert by the table. `Tablet.cs` builds meshes and text (1,250 lines): mesh APIs take Il2Cpp arrays, the font atlas callback is an event.
- [ ] **Step 2:** PC check; Quest build; headset check: the tablet rises at the shrine with the same text as on PC, the main path's rings show, the try-out line shows.
- [ ] **Step 3:** Commit.

### Task 7: Ship it

- [ ] **Step 1:** The whole checklist once more on the headset with a second player on PC (every skill, the shrine, a join under `refuse`, a join with an old Quest DLL: turned away, and what the headset shows is written down).
- [ ] **Step 2:** A fresh reviewer reads the whole branch against this plan's Global Constraints and Review Focus.
- [ ] **Step 3:** Hand `quest-port` to the PC session to merge, with: the Quest project, the rule "a new patch, cast, event, component or game-collection use is written for both builds and checked on both", the CLAUDE.md section, and the request that `tools/stage-build.sh` also builds the Quest project and stages `BluesArcheryOverhaul.Quest.dll` beside the PC one (it is never installed into the PC game or the server).
- [ ] **Step 4:** The owner asks Shadow to list the Quest DLL on the Download Board; until then it is copied to the headset's `Mods` folder. The README gains a Quest section (the PC session's).

## Size, said plainly

Tasks 1 and 2 are a session each. Tasks 3 and 4 are the bulk of the conversion (about 12,000 lines between them) and several sessions. Task 5 depends on what the headset can draw. Every task needs the owner in the headset at its end, and Tasks 3, 4 and 7 need the PC game running beside it. Between sessions the PC mod keeps changing, so every phase starts with a rebase.
