# Archery Main Path, Part 1 (rule and complements) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every archer has one main path (Quick, Multi or Charge); only that path's shot exists for them, and orbs of the other two lines improve it as complements, at the design's numbers, using looks the mod already has.

**Architecture:** The main path is a new field of `Build`, decided on the server by a Unity-free rule with a remembered incumbent, sent to clients in a new version of the 32-bit build block. The release resolver reads it: facts of off-path lines are zeroed before `Resolver.Resolve`, and the server sets the real arrow's multiplier from the resolution instead of trusting the game's skills. Per-player timing (faster stages, faster volley) is done on the mod's own draw clock: the game's skill timers are set to the fastest any build can have and the mod gates on its clock.

**Tech Stack:** C# net472, MelonLoader 0.7.3, HarmonyX, Unity 2020.3 Mono. Tests: the console project `QuickshotRing.Tests` (`Check(name, bool)`, `Near(a, b)`).

**Spec:** `docs/superpowers/specs/2026-11-13-archery-main-path-design.md` (this plan covers its part 1 only: "main path rule, storage, wire and resolver, with complements at their numbers but existing looks"). It amends `docs/superpowers/specs/2026-11-12-archery-combinations-design.md`.

## Global Constraints

- No new networked objects or message types. The build block stays 32 bits.
- Every number is a setting in `UserData\QuickshotRing.cfg`; settings both sides must agree on are made with `Cfg.Shared(...)` and their description starts with `[shared]` (as the existing ones do). Never raise `SettingsVersion`; changed shipped defaults migrate through `shipped_defaults.txt`.
- Unity types stay out of the Unity-free files: `Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`, `Charge.cs`, and the new `Path.cs`.
- Decide on the server; the shooter's client decides the same thing locally; spectators draw what is relayed.
- Every new behaviour logs a line starting `[Archery] `.
- Build stays at 0 warnings. `dotnet run --project QuickshotRing.Tests -c Release` ends `ALL PASS (N)`; N starts at 329 and only grows.
- Brace and Overcharge stay shelved; nothing here may depend on them.
- Players are never staggered, interrupted or pierced; hand-made hits run under `Hits.Dealing`.
- Not a git repo: "commit" in this plan means take the task's file snapshot (`.superpowers/sdd/2026-11-13-archery-main-path-part1/snap.sh N`, as earlier plans did).
- In text the user reads: "Charge Shot", "Quickdraw", "multi-arrow". Play-test steps use four lines: Skills / Do / You should see / Wrong if.

## Review Focus

1. A player whose main path flips while an arrow is nocked or in flight: the arrow already resolved keeps its resolution; the next draw uses the new path. (Task 3 test: resolution is a pure function of the facts at release.)
2. Tied counts with no history on a fresh server file, and a corrupt or missing file: rule 3's fallback order (Charge, Quick, Multi), no exception. (Task 1 and Task 2 tests.)
3. A client on the old build-block version or a mod-less client: bits unreadable means "derive from counts"; no misread build. (Task 1 test.)
4. A Charge main with three Quickdraw orbs releasing between the scaled and the unscaled stage time: the tier is the scaled one on server and shooter alike. (Task 4 test.)
5. `[TestBuild]` lending orbs: the remembered main path file is unchanged after the override is removed. (Task 2 test on the pure part.)

## File Structure

| File | Change |
|---|---|
| `QuickshotRing/Path.cs` (new, Unity-free) | `MainPath`, `MainPathRule`, `ComplementValues`, `Complements`, `PathStore` (the file format as pure text in/out). |
| `QuickshotRing/Build.cs` | `Build.Main`, `BuildBits` version 2. |
| `QuickshotRing/Paths.cs` (new) | Server and local files, wiring into `Builds`, settings for `ComplementValues`, logs. |
| `QuickshotRing/Builds.cs`, `TestBuild.cs` | Set `Build.Main` when a build is computed. |
| `QuickshotRing/Release.cs`, `Draws.cs` | Main-gated facts; real-arrow multiplier from the resolution; scaled clocks. |
| `QuickshotRing/Charge.cs`, `Tree.cs`, `ChargeFx.cs`, `HolyArrow.cs` | Scaled stages; stage 1 on the mod's clock; Holy scaled. |
| `QuickshotRing/Volley.cs`, `VolleySkill.cs`, `Spectral.cs` | Scaled volley timeline; needles; heavy arrows; gold-ghost stagger; spread. |
| `QuickshotRing/Weight.cs`, `Steady.cs`, `Quickdraw4.cs`, `Main.cs` | Snap weight; wobble on charged shots; line-shot needles; ring only for Quick mains. |
| `QuickshotRing.Tests/PathTests.cs` (new), `QuickshotRing.Tests.csproj`, `Program.cs` | Tests for `Path.cs`; compile it; call `PathTests.Run()`. |

---

### Task 1: The main path rule, the build field and build block version 2

**Files:**
- Create: `QuickshotRing/Path.cs`, `QuickshotRing.Tests/PathTests.cs`
- Modify: `QuickshotRing/Build.cs`, `QuickshotRing.Tests/QuickshotRing.Tests.csproj`, `QuickshotRing.Tests/Program.cs`, `QuickshotRing.Tests/BuildTests.cs`

**Interfaces:**
- Produces:
  - `enum MainPath { None = 0, Quick = 1, Multi = 2, Charge = 3 }`
  - `static class MainPathRule`:
    - `int Count(Build b, MainPath line)` — Quick: `b.Quick` + 1 with `Ultimates.Quickdraw4`; Multi: `b.Volley` + 1 with `SeekingVolley`; Charge: `b.Charge` + 1 with `HolyArrow`; None: 0.
    - `MainPath Decide(Build b, MainPath incumbent)` — spec "The main path rule" 1 to 4; fallback order Charge, Quick, Multi.
    - `bool WouldFlip(Build now, MainPath main, Build after)` — `Decide(after, main) != main && main != MainPath.None`.
  - `Build`: new readonly field `MainPath Main`; constructor gains a last optional parameter `MainPath main = MainPath.None`; `Build With(MainPath main)`; `Equals`/`GetHashCode`/`ToString` include it (`ToString` appends ` main:Quick`).
  - `BuildBits.Version = 2`, layout: version 4, quick 2, volley 2, charge 2, ultimates 3, sides 6, bridges 4, quiver 1, **main 2, settings check 6**. `Write(IBitWriter, Build, byte settingsCheck)` writes `settingsCheck & 0x3F`. `TryRead` accepts version 2; version 1 is read with its old layout and returns the build with `Main = MainPathRule.Decide(build, MainPath.None)` and the 8-bit check; other versions `UnknownVersion`. New `const int CheckMask = 0x3F` that callers comparing checksums apply to both sides.

- [ ] **Step 1: Write the failing tests** in `PathTests.cs` (`internal static class PathTests { internal static void Run() { ... } }`, called from `Program.cs` beside the other `Run()` calls):

```csharp
Build B(int q, int v, int c, Ultimates u = Ultimates.None) => new Build(q, v, c, u, SideOrbs.None, Bridges.None, false);
Check("path: no line orbs, no main path", MainPathRule.Decide(B(0, 0, 0), MainPath.None) == MainPath.None);
Check("path: the only line owned is main", MainPathRule.Decide(B(0, 2, 0), MainPath.None) == MainPath.Multi);
Check("path: highest count wins", MainPathRule.Decide(B(1, 3, 2), MainPath.Quick) == MainPath.Multi);
Check("path: a tie keeps the incumbent", MainPathRule.Decide(B(3, 0, 3), MainPath.Charge) == MainPath.Charge
      && MainPathRule.Decide(B(3, 0, 3), MainPath.Quick) == MainPath.Quick);
Check("path: a tie without the incumbent falls back Charge, Quick, Multi", MainPathRule.Decide(B(2, 2, 2), MainPath.None) == MainPath.Charge
      && MainPathRule.Decide(B(2, 2, 0), MainPath.None) == MainPath.Quick && MainPathRule.Decide(B(2, 2, 0), MainPath.Charge) == MainPath.Quick);
Check("path: the legendary counts (user's example)", MainPathRule.Decide(B(3, 0, 3, Ultimates.Quickdraw4), MainPath.Charge) == MainPath.Quick);
Check("path: count", MainPathRule.Count(B(3, 1, 3, Ultimates.HolyArrow), MainPath.Charge) == 4 && MainPathRule.Count(B(3, 1, 3), MainPath.Multi) == 1);
Check("path: would flip", MainPathRule.WouldFlip(B(3, 0, 3), MainPath.Charge, B(3, 0, 3, Ultimates.Quickdraw4))
      && !MainPathRule.WouldFlip(B(2, 0, 3), MainPath.Charge, B(3, 0, 3)) && !MainPathRule.WouldFlip(B(0, 0, 0), MainPath.None, B(1, 0, 0)));
```

and in `BuildTests.cs` (using the existing `FakeBits` writer/reader):

```csharp
var withMain = new Build(3, 1, 2, Ultimates.Quickdraw4, SideOrbs.None, Bridges.None, true, MainPath.Quick);
// round trip through version 2
Check("bits v2: main path and build survive", /* write withMain, check 0x2A; read */ read == WireRead.Ok && got == withMain && got.Main == MainPath.Quick && check == 0x2A);
Check("bits v2: still 32 bits", /* writer bit count */ bits == 32);
Check("bits v2: the check is masked to 6 bits", /* write check 0xFF; read */ check2 == 0x3F);
Check("bits v1: old block still reads, main derived from counts", /* hand-write a version 1 block for Q0 V2 C0 */ r1 == WireRead.Ok && old.Main == MainPath.Multi);
Check("build: main path is part of equality", withMain != withMain.With(MainPath.Charge));
```

- [ ] **Step 2: Run** `dotnet run --project QuickshotRing.Tests -c Release`. Expected: does not compile (`MainPath` not defined).
- [ ] **Step 3: Implement** `Path.cs` (rule only for now), the `Build` field and `BuildBits` version 2. Add `<Compile Include="..\QuickshotRing\Path.cs" />` to the tests csproj. Fix every existing checksum comparison in the mod to apply `BuildBits.CheckMask` (find them with a search for `settingsCheck` / `TryRead(`).
- [ ] **Step 4: Run tests.** Expected: `ALL PASS (N)` with N ≥ 342. Then `dotnet build QuickshotRing -c Release`: 0 warnings.
- [ ] **Step 5: Snapshot** (`snap.sh 1`).

---

### Task 2: Remembering and deciding the main path (server file, local file, test builds)

**Files:**
- Modify: `QuickshotRing/Path.cs`, `QuickshotRing.Tests/PathTests.cs`, `QuickshotRing/Builds.cs`, `QuickshotRing/TestBuild.cs`, `QuickshotRing/Main.cs` (init)
- Create: `QuickshotRing/Paths.cs`

**Interfaces:**
- Consumes: `MainPathRule.Decide`, `Build.With`.
- Produces:
  - `sealed class PathStore` (Unity-free, in `Path.cs`): `static PathStore Parse(string? text)` (one `key=Quick|Multi|Charge` per line; unknown or malformed lines ignored, never throws); `MainPath Get(string key)`; `bool Set(string key, MainPath value)` (true when changed; `None` removes); `string Serialize()` (sorted by key, so the file is stable).
  - `static class Paths` (in `Paths.cs`): `void Init()`; `Build Stamp(Build counts, string accountKey, bool testBuild)` — server: `Decide(counts, remembered)`; when `!testBuild` and the result differs from the remembered value, store and write the file; when `testBuild`, never write. Returns `counts.With(main)`. `MainPath LocalIncumbent(Build counts)` — shooter's client: the same with the local file `UserData\QuickshotRing\main_path_local.txt` (one key, `local`). File on the server: `UserData\QuickshotRing\main_paths.txt`.
  - The account key is the player's account id as a string (the game's `UserInfo.Identifier`); fall back to the username when it is 0 or missing.
  - `Builds.Compute` (server) returns `Paths.Stamp(...)`'d builds, so `Builds.ServerBuild(p).Main` is always decided. On clients, `Builds.ClientBuild(name).Main` is what the bits said; `Builds.LocalBuild()` before any bits arrived is the local owned orbs stamped with `Paths.LocalIncumbent` (find how the client lists its own owned slots in `Builds.RealOwned`; the local player's `PlayerProgressionManager` exists on the client). When the server's bits disagree with the local file, log and overwrite the local file.

- [ ] **Step 1: Failing tests** (`PathTests.cs`):

```csharp
var store = PathStore.Parse("123=Charge\n\ngarbage\n77=Bogus\n=Quick\n456=Multi\n");
Check("store: parses good lines, ignores bad ones", store.Get("123") == MainPath.Charge && store.Get("456") == MainPath.Multi && store.Get("77") == MainPath.None);
Check("store: null and empty text are an empty store", PathStore.Parse(null).Get("1") == MainPath.None && PathStore.Parse("").Serialize() == "");
Check("store: set reports change, None removes", store.Set("123", MainPath.Quick) && !store.Set("123", MainPath.Quick) && store.Set("456", MainPath.None) && store.Get("456") == MainPath.None);
Check("store: serialises sorted and round-trips", PathStore.Parse(store.Serialize()).Get("123") == MainPath.Quick && store.Serialize() == "123=Quick\n");
```

- [ ] **Step 2: Run.** Expected: does not compile (`PathStore`).
- [ ] **Step 3: Implement** `PathStore`, `Paths`, and the wiring. Log on every decision that changes: `[Archery] Path: '<name>' main path <old> -> <new> (Quick n, Multi n, Charge n; <why: first orb | out-counted | tie kept | tie fallback | test build, not saved>)`. File errors are a warning once, never an exception; an unreadable file is an empty store.
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Check by reading `Builds.cs` that every path that produces a server `Build` goes through `Stamp` (the build log line now ends ` main:<path>`).
- [ ] **Step 5: Snapshot** (`snap.sh 2`).

---

### Task 3: Only the main path's shot exists

**Files:**
- Modify: `QuickshotRing/Release.cs`, `QuickshotRing.Tests/ReleaseTests.cs`, `QuickshotRing/Draws.cs`, `QuickshotRing/Main.cs`, `QuickshotRing/ChargeFx.cs`, `QuickshotRing/VolleySkill.cs`, `QuickshotRing/Volley.cs`, `QuickshotRing/HolyArrow.cs`

**Interfaces:**
- Consumes: `Build.Main`.
- Produces:
  - `static ReleaseFacts Resolver.Gate(ReleaseFacts f)` in `Release.cs`: returns the facts with off-path lines removed, by `f.Build.Main`: not Quick → `SnapReady = false`; not Charge → `ChargeActive = false`, `HolyArmed = false`, `Overcharged = false`; not Multi → `Ghosts = 0`, `Racked = false`. `MainPath.None` leaves the facts alone. `Resolver.Resolve` calls `Gate` first. (Complement spectral counts are added in Tasks 5 to 7; after this task an off-path line simply gives nothing.)
  - `Resolver.Resolve`: echoes on Snap and Early are 0 from now on (needles return in Task 5 for Quick mains on Snap only).
  - `static float Resolver.RealArrowTier(Resolution r, Build b, out bool quick)`: Snap → `quick = true`, tier `b.Quick`; Charged (not a beam) → tier `r.ChargedTier`; else 0.
  - `Draws.ShotReset` (server): sets every tool's `OneOffMultiplier` on the shot arrow from the resolution: Snap → `Tree.QuickMultiplier(b.Quick)`, Charged → `Tree.ChargeMultiplier(r.ChargedTier)`, a Quickdraw 4 line or a Holy beam → leave what their own code set, anything else → 1. This replaces the 0.9.1 staged-charge block there. Log `[Archery] Release: arrow N real arrow x<game> -> x<set> (<kind>, main <path>)`.

- [ ] **Step 1: Failing tests** (`ReleaseTests.cs`), with `Build M(MainPath m)` = Q3 V3 C3 with that main and `ReleaseFacts All(Build b)` = `SnapReady, ChargeActive, Crossed` true, `Ghosts = 15`, `Seconds = 3f`, `Stages = ChargeStages.Default`:

```csharp
Check("gate: a Quick main snaps and has no charge or ghosts", Resolver.Resolve(All(M(MainPath.Quick))).Kind == ShotKind.Snap
      && Resolver.Resolve(new ReleaseFacts { Build = M(MainPath.Quick), ChargeActive = true, Ghosts = 15, Crossed = true }).Kind == ShotKind.Early);
Check("gate: a Charge main charges and never snaps", Resolver.Resolve(All(M(MainPath.Charge))).Kind == ShotKind.Charged
      && Resolver.Resolve(All(M(MainPath.Charge))).Plan.Ghosts == 0);
Check("gate: a Multi main holds a volley and never snaps or charges", Resolver.Resolve(All(M(MainPath.Multi))).Kind == ShotKind.Held
      && Resolver.Resolve(All(M(MainPath.Multi))).Plan.Ghosts == 15 && Resolver.Resolve(All(M(MainPath.Multi))).ChargedTier == 0);
Check("gate: Holy Arrow only for a Charge main", Resolver.Resolve(new ReleaseFacts { Build = M(MainPath.Quick), HolyArmed = true, Crossed = true }).Ultimate == UltimateUse.None);
Check("gate: no main path leaves the facts alone", Resolver.Resolve(All(new Build(0, 0, 0, Ultimates.None, SideOrbs.None, Bridges.None, false))).Kind == ShotKind.Snap);
Check("gate: no echoes any more", Resolver.Resolve(All(M(MainPath.Quick))).Plan.Echoes == 0);
Check("real arrow: tier follows the resolution", Resolver.RealArrowTier(Resolver.Resolve(All(M(MainPath.Quick))), M(MainPath.Quick), out bool q) == 3 && q
      && Resolver.RealArrowTier(Resolver.Resolve(new ReleaseFacts { Build = M(MainPath.Charge), SnapReady = true, Crossed = true }), M(MainPath.Charge), out _) == 0);
```

Existing resolver tests build `Build`s without a main path; give each the main path its assertion is about (a test that expects ghosts gets `MainPath.Multi`, and so on). Delete tests that assert echoes on Early.

- [ ] **Step 2: Run.** Expected: fails to compile (`Resolver.Gate`).
- [ ] **Step 3: Implement** `Gate`, `RealArrowTier`, the `ShotReset` change.
- [ ] **Step 4: Hide what an off-path player would otherwise see and hear** (client, each a one-line gate on `Builds.LocalBuild().Main` for the local player and `Builds.ClientBuild(owner).Main` for others):
  - `Main.cs`: the Quickdraw ring is shown only when the local main path is Quick or None.
  - `ChargeFx.Status`: no Fill is created unless the archer's main path is Charge or None.
  - `VolleySkill.NoGlow`: Slowdraw's glow is skipped (returns false, as the hidden branch does) when the archer's main path is not Charge or None; Holy Arrow's likewise.
  - `Volley.cs`: the held ghosts (`Volley.Ghosts(bow)`/the held layout) are 0 unless the archer's main path is Multi or None.
  - Slowdraw's Charged sound: re-attach the `[HarmonyPatch(typeof(SkillInputStructure), nameof(SkillInputStructure.PlaySoundAt))]` attribute on `ChargeFx.Charge2Sound` and make its prefix return false only when the arrow's archer is known and their main path is neither Charge nor None (drop the old `f.suppressed` logic).
  - `HolyArrow`: arming (`Arm`) is refused on the server when the owner's main path is not Charge or None; log once per draw.
- [ ] **Step 5: Run tests and build.** Expected: `ALL PASS`, 0 warnings.
- [ ] **Step 6: Snapshot** (`snap.sh 3`).

---

### Task 4: Complement numbers and per-player timing

**Files:**
- Modify: `QuickshotRing/Path.cs`, `QuickshotRing.Tests/PathTests.cs`, `QuickshotRing/Charge.cs`, `QuickshotRing/Release.cs`, `QuickshotRing/Paths.cs`, `QuickshotRing/Tree.cs`, `QuickshotRing/Draws.cs`, `QuickshotRing/Volley.cs`, `QuickshotRing/ChargeFx.cs`, `QuickshotRing/HolyArrow.cs`

**Interfaces:**
- Produces (Unity-free, `Path.cs`):
  - `readonly struct ComplementValues { NeedleShare = 0.15f; NeedlesPerOrb = 1; SnapStaggerPerOrb = 0.1f; SnapInterruptFrom = 2; VolleyFasterPerOrb = 0.25f; SpreadTighterPerOrb = 0.15f; GhostStaggerPerOrb = 0.03f; StageFasterPerOrb = 0.12f; HeavyPerOrb = 2; HeavyShare = 0.15f; }` with `static Default` and a constructor taking all ten.
  - `static class Complements` (every method returns the neutral value unless `b.Main` is the path named):
    - `float StageFactor(Build b, ComplementValues v)` — Charge main: `1 - v.StageFasterPerOrb * b.Quick`, floor 0.25; else 1.
    - `float VolleyFactor(Build b, ComplementValues v)` — Multi main: `1 - v.VolleyFasterPerOrb * b.Quick`, floor 0.25; else 1.
    - `float SpreadFactor(Build b, ComplementValues v)` — Multi main: `1 - v.SpreadTighterPerOrb * b.Quick`; else 1.
    - `int Needles(Build b, ComplementValues v)` — Quick main: `v.NeedlesPerOrb * b.Volley`; else 0.
    - `int HeavyArrows(Build b, ComplementValues v)` — Charge main: `v.HeavyPerOrb * b.Volley`; else 0.
    - `float SnapStagger(Build b, ComplementValues v)`, `bool SnapInterrupts(Build b, ComplementValues v)` — Quick main only.
    - `float GhostStagger(Build b, ComplementValues v)` — Multi main: `v.GhostStaggerPerOrb * b.Charge`; else 0.
    - `float MinFactor(ComplementValues v, bool stages)` — the factor at 3 off-path orbs (what the game's shared timers are set to).
  - `ChargeStages ChargeStages.Scaled(float factor)`; `VolleyTimes VolleyTimes.Scaled(float factor)` (every time field times the factor; `Interval` too).
  - `Paths.Values` (live `ComplementValues` from shared settings in category `[Complements]`), `Paths.StagesFor(Build b)` = `Tree.Stages.Scaled(Complements.StageFactor(b, Values))`, `Paths.VolleyTimesFor(Build b)`, `Paths.HolySecondsFor(Build b)` = `Tree.HolySeconds * StageFactor`.
- Behaviour:
  - The game's shared timers are set to the fastest any build can have: Slowdraw's to `Tree.Stages.S1 * MinFactor(stages)`, the Volley skill's to `Tree.TwoShotSeconds * MinFactor(volley)`, Holy's to `Tree.HolySeconds * MinFactor(stages)` (`Tree.ApplyTimers`).
  - The mod's clock gates each player: in `Draws.ServerFacts`/`ClientFacts`, `ChargeActive` additionally needs `t >= Paths.StagesFor(build).S1`; `Stages = Paths.StagesFor(build)`; `Volley.Ghosts`/`Racked` use `Paths.VolleyTimesFor(build)` and need `t >= VolleyAt` for the first five as well (change `VolleyTimeline.Ghosts` so the game's charged flag is necessary but not sufficient). Holy Arrow may arm only when `t >= Paths.HolySecondsFor(build)`; if the game's Charged status for Lightbeam arrives earlier, the server arms when the clock reaches it (check each `HeldUpdate` tick in the existing draw-clock patch).
  - `ChargeFx`: the stage-1 thump moves onto the same clock as stages 2 and 3 (the Fill's stage loop starts from stage 0 once the game's Charged status has arrived or the clock passes the scaled S1, whichever is later); strands use `Paths.StagesFor(f.Build)`.
  - `TwoShotSeconds` default goes back to being the Multi main's number; leave its value at 0.3.

- [ ] **Step 1: Failing tests** (`PathTests.cs`), with `var v = ComplementValues.Default;` and `Build M(q, vol, c, main)`:

```csharp
Check("complement: stages 12% faster per Quick orb, Charge main only", Near(Complements.StageFactor(M(3, 0, 3, MainPath.Charge), v), 0.64f)
      && Near(Complements.StageFactor(M(3, 0, 3, MainPath.Quick), v), 1f));
Check("complement: stage 3 at 1.6 s with three Quick orbs", Near(ChargeStages.Default.Scaled(0.64f).S3, 1.6f));
Check("complement: volley 25% faster per Quick orb, Multi main only", Near(Complements.VolleyFactor(M(3, 3, 0, MainPath.Multi), v), 0.25f)
      && Near(VolleyTimes.Default.Scaled(0.25f).VolleyAt, 0.075f) && Near(Complements.VolleyFactor(M(3, 3, 0, MainPath.Quick), v), 1f));
Check("complement: spread 15% tighter per Quick orb", Near(Complements.SpreadFactor(M(2, 3, 0, MainPath.Multi), v), 0.70f));
Check("complement: needles one per Multi orb, Quick main only", Complements.Needles(M(3, 2, 0, MainPath.Quick), v) == 2 && Complements.Needles(M(3, 2, 0, MainPath.Multi), v) == 0);
Check("complement: heavy arrows two per Multi orb, Charge main only", Complements.HeavyArrows(M(0, 3, 3, MainPath.Charge), v) == 6 && Complements.HeavyArrows(M(0, 3, 3, MainPath.Multi), v) == 0);
Check("complement: snap weight", Near(Complements.SnapStagger(M(3, 0, 2, MainPath.Quick), v), 0.2f) && Complements.SnapInterrupts(M(3, 0, 2, MainPath.Quick), v)
      && !Complements.SnapInterrupts(M(3, 0, 1, MainPath.Quick), v) && Near(Complements.SnapStagger(M(3, 0, 2, MainPath.Charge), v), 0f));
Check("complement: gold ghost stagger", Near(Complements.GhostStagger(M(0, 3, 3, MainPath.Multi), v), 0.09f));
Check("complement: the shared timers use the factor at three orbs", Near(Complements.MinFactor(v, true), 0.64f) && Near(Complements.MinFactor(v, false), 0.25f));
Check("review focus 4: tier between scaled and unscaled stage time", ChargeStages.Default.Scaled(0.64f).Tier(3, 1.7f) == 3 && ChargeStages.Default.Tier(3, 1.7f) == 1);
// The balance check of the spec, on spectral shares per release at the defaults:
float multiMain = VolleyMath.Inner * VolleyMath.InnerShare + VolleyMath.Outer * VolleyMath.OuterShare;          // 1.35
float quickComplement = Complements.Needles(M(3, 3, 0, MainPath.Quick), v) * v.NeedleShare;                    // 0.45
float chargeComplement = Complements.HeavyArrows(M(0, 3, 3, MainPath.Charge), v) * v.HeavyShare;               // 0.90
Check("balance: a Multi main gets more from three Multi orbs than any complement does", multiMain > chargeComplement && chargeComplement > quickComplement);
```

- [ ] **Step 2: Run.** Expected: does not compile (`ComplementValues`).
- [ ] **Step 3: Implement** the Unity-free parts, then `Paths` settings and the three `...For(Build)` helpers, then the timer and gating changes listed under Behaviour.
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Log lines to add: `[Archery] Tree: timers ... (shared timers at the fastest build: charge x0.64, volley x0.25)`, and per release `main <path> stages <s1>/<s2>/<s3> volley x<factor>`.
- [ ] **Step 5: Snapshot** (`snap.sh 4`).

---

### Task 5: Quick archer complements (needles, snap weight, the line shot)

**Files:**
- Modify: `QuickshotRing/Release.cs`, `QuickshotRing.Tests/ReleaseTests.cs`, `QuickshotRing/Charge.cs`, `QuickshotRing.Tests/ChargeTests.cs`, `QuickshotRing/Volley.cs`, `QuickshotRing/Spectral.cs`, `QuickshotRing/Weight.cs`, `QuickshotRing/Quickdraw4.cs`, `QuickshotRing/Draws.cs`

**Interfaces:**
- Consumes: `Complements.Needles/SnapStagger/SnapInterrupts`, `Paths.Values`.
- Produces:
  - `ReleaseFacts.Needles` (int, set by `Draws` from `Complements.Needles(build, Paths.Values)`); `Resolver.Resolve`: a Snap's plan is `new ReleasePlan(0, 0, f.Needles)` (the claim's stage carries the count, as echoes did; `ReleasePlan.From` already caps Snap at 3 — raise that cap to 6).
  - `WeightRules.ForRelease(Resolution r, Build build, WeightValues v, ComplementValues c)`: Snap → `new WeightCarry(Complements.SnapStagger(build, c), Complements.SnapInterrupts(build, c), 0, 0f)` (replaces "interrupt with Charge 2+" for everyone).
  - Needles reuse the echo launch in `Volley.cs` (find `Echoes` in `Fire`/`Launch`) with share `Paths.Values.NeedleShare` (the `[Volley] EchoShare` setting is retired: remove the entry, `SpectralShares.Echo` goes) and a 1.5 degree cross pattern (the pattern the existing echo code uses when there are several; if it has none, place needle i at 1.5 degrees off the arrow's line, rotated `i * 360 / n` degrees around it).
  - Quickdraw 4 line shot: `Quickdraw4.ServerShot` casts one extra ray per needle with the same pattern, applies `Hits.Apply` at `NeedleShare` (no 3x), and the snap's stagger and interrupt go to the creature the main line hit (through `Weight`'s existing batched cripple path); clients draw a thinner `Line` per needle from the relayed stage.

- [ ] **Step 1: Failing tests:**

```csharp
// ReleaseTests.cs
Check("needles: a Quick main's snap carries its needles", Resolver.Resolve(new ReleaseFacts { Build = M(MainPath.Quick), SnapReady = true, Crossed = true, Needles = 3 }).Plan.Echoes == 3);
Check("needles: nobody else gets them", Resolver.Resolve(new ReleaseFacts { Build = M(MainPath.Multi), SnapReady = true, Crossed = true, Needles = 3 }).Plan.Echoes == 0);
Check("needles: the relayed stage keeps up to six", ReleasePlan.From(ShotKind.Snap, 6).Echoes == 6);
// ChargeTests.cs
var cv = ComplementValues.Default;
var snap = new Resolution(ShotKind.Snap, UltimateUse.None, new ReleasePlan(0, 0, 0), 0);
Check("snap weight: 0.1 stagger per Charge orb for a Quick main", Near(WeightRules.ForRelease(snap, Qm(3, 0, 2), WeightValues.Default, cv).Stagger, 0.2f)
      && WeightRules.ForRelease(snap, Qm(3, 0, 2), WeightValues.Default, cv).Interrupt && !WeightRules.ForRelease(snap, Qm(3, 0, 1), WeightValues.Default, cv).Interrupt);
Check("snap weight: none for a Quick main with no Charge orbs", !WeightRules.ForRelease(snap, Qm(3, 0, 0), WeightValues.Default, cv).Any);
```

(`Qm(q, v, c)` = a Build with `MainPath.Quick`.) Update the existing `ForRelease` tests for the new parameter.

- [ ] **Step 2: Run.** Expected: does not compile (`ReleaseFacts.Needles`).
- [ ] **Step 3: Implement.** Snap stagger on a creature goes through the same batched cripple hit as charged stagger (`Weight.Arm` already receives the carry).
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Logs: `[Archery] Volley: fired arrow N: ... <n> needles x0.15`, `[Archery] Line: arrow N <n> needle line(s), <hits> hit(s)`.
- [ ] **Step 5: Snapshot** (`snap.sh 5`).

---

### Task 6: Multi archer complements (tighter volley, gold ghosts with stagger)

**Files:**
- Modify: `QuickshotRing/Charge.cs`, `QuickshotRing.Tests/ChargeTests.cs`, `QuickshotRing/Volley.cs`, `QuickshotRing/Weight.cs`

**Interfaces:**
- Consumes: `Complements.SpreadFactor/GhostStagger`, `Paths.VolleyTimesFor` (Task 4 already makes the volley faster).
- Produces:
  - `WeightRules.ForRelease`: Held with a Multi main and `Complements.GhostStagger(build, c) > 0` → `new WeightCarry(0f, false, 0, ghostStaggerPerShare: 0f)` plus a new field `WeightCarry.StaggerPerGhost` = `GhostStagger(build, c)` (add the field as a fifth constructor parameter, default 0; `Any` includes it). The server's ghost-hit batching adds `StaggerPerGhost` per ghost that hits a creature, once per creature per release, through the existing ghost stagger path.
  - `Volley.cs`: the fan's spread angle (held layout and release layout) is multiplied by `Complements.SpreadFactor(build, Paths.Values)`; ghosts are drawn with the existing gold look whenever the archer is a Multi main with `build.Charge > 0` (today that look is switched by Slowdraw's charged flag; find the flag in `Volley.cs`, `chargeCharged`).

- [ ] **Step 1: Failing tests** (`ChargeTests.cs`):

```csharp
var held = new Resolution(ShotKind.Held, UltimateUse.None, new ReleasePlan(15, 0, 0), 0);
Check("gold ghosts: 0.03 stagger per ghost per Charge orb for a Multi main", Near(WeightRules.ForRelease(held, Mm(0, 3, 3), WeightValues.Default, cv).StaggerPerGhost, 0.09f)
      && WeightRules.ForRelease(held, Mm(0, 3, 3), WeightValues.Default, cv).Any);
Check("gold ghosts: none without Charge orbs", !WeightRules.ForRelease(held, Mm(0, 3, 0), WeightValues.Default, cv).Any);
```

- [ ] **Step 2: Run.** Expected: does not compile (`StaggerPerGhost`).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Log: the existing `Weight: arrow N carries ...` line gains `ghost stagger <x>/ghost`; `Volley: ... spread x<factor>`.
- [ ] **Step 5: Snapshot** (`snap.sh 6`).

---

### Task 7: Charge archer complements (heavy arrows, steadier charged shots)

**Files:**
- Modify: `QuickshotRing/Release.cs`, `QuickshotRing.Tests/ReleaseTests.cs`, `QuickshotRing/Draws.cs`, `QuickshotRing/Volley.cs`, `QuickshotRing/Steady.cs`

**Interfaces:**
- Consumes: `Complements.HeavyArrows`, `Paths.Values.HeavyShare`, `WeightCarry.GhostStaggerPerShare` (existing: half the stage's stagger per share).
- Produces:
  - `ReleaseFacts.Heavy` (int, from `Complements.HeavyArrows`); `Resolver.Resolve`: a Charged release that is not a beam has plan `new ReleasePlan(f.Heavy, 0, 0)` (heavy arrows travel as the plan's ghosts, so the claim's stage relays them with no wire change; at most 6).
  - `Volley.cs`: for a Charge main, the held layout shows `Heavy` ghosts in the gold look from the moment stage 1 completes (two per Multi orb), and the release launches them with share `HeavyShare` flat (not multiplied by `Tree.ChargeMultiplier`: pass spectral multiplier 1 for a Charge main's heavy arrows) and the existing per-share ghost stagger. A beam release launches none.
  - `Steady.cs`: its wobble modifier (not its speed part) also applies to a Charge main's Charged releases at tier `build.Quick`.

- [ ] **Step 1: Failing tests** (`ReleaseTests.cs`):

```csharp
var cf = new ReleaseFacts { Build = M(MainPath.Charge), ChargeActive = true, Crossed = true, Seconds = 3f, Stages = ChargeStages.Default, Heavy = 6 };
Check("heavy arrows: a Charge main's charged shot carries them", Resolver.Resolve(cf).Kind == ShotKind.Charged && Resolver.Resolve(cf).Plan.Ghosts == 6);
cf.HolyArmed = true;
Check("heavy arrows: a beam absorbs them", Resolver.Resolve(cf).Ultimate == UltimateUse.Beam && Resolver.Resolve(cf).Plan.Stage == 0);
cf.HolyArmed = false; cf.ChargeActive = false;
Check("heavy arrows: none on an uncharged release", Resolver.Resolve(cf).Plan.Stage == 0);
```

- [ ] **Step 2: Run.** Expected: does not compile (`ReleaseFacts.Heavy`).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Logs: `[Archery] Volley: fired arrow N: <n> heavy arrows x0.15 (flat)`, `[Archery] Steady: arrow N ... Charged (Charge main): wobble x<m>`.
- [ ] **Step 5: Snapshot** (`snap.sh 7`).

---

### Task 8: Version, notes, backup, deploy and the play-test guide

**Files:**
- Modify: `QuickshotRing/Main.cs` (version `0.10.0`), `CLAUDE.md` (file table rows for `Path.cs` and `Paths.cs`; a "main path" entry under things easy to get wrong; the test count), `docs/superpowers/specs/2026-11-12-archery-combinations-design.md` (one line at the top pointing at the amending spec)
- Create: `docs/playtest-0.10.0.md`, `backups/2026-11-13-pre-0.10.0/` (server Save folder, both `QuickshotRing.cfg` files, both installed DLLs)

- [ ] **Step 1:** Take the backup before anything is deployed.
- [ ] **Step 2:** `dotnet run --project QuickshotRing.Tests -c Release` → `ALL PASS`; `dotnet build QuickshotRing -c Release -p:Deploy=true -p:DeployServer=true` → 0 warnings; the three installed DLLs have the same md5.
- [ ] **Step 3:** Write `docs/playtest-0.10.0.md`: at most 7 tests in the four-line format, in game terms, covering: a Quick archer (Quickdraw 1 to 3 only) snaps and holding gives a plain arrow; add Two-Shot → each snap fires one needle; a Charge archer with Quickdraw 2 has no quick shot and charges visibly faster; add Two-Shot → two gold arrows beside each charged shot; a Multi archer with Quickdraw orbs gets the volley sooner and tighter; a tie keeps the main path (Charge Shot 3 then Quickdraw 3 stays a Charge archer) and Quickdraw 4 flips it.
- [ ] **Step 4:** Snapshot (`snap.sh 8`).

## Not in this plan

Parts 2 to 4 of the spec: the three distinct spectral looks and the strand and Holy Arrow polish; tree colours and the flip warning on orbs; the test tablet. Until part 2, needles look like today's echoes and heavy arrows like today's gold ghosts. Until parts 3 and 4, nothing at the shrine tells the player their main path; the play-test guide says which orbs to buy in which order instead.
