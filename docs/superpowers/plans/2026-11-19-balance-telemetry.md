# Balance Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The dedicated server records every bow release and every archery hit (damage really dealt, kills) to daily CSV files, and `tools/telemetry_report.py` turns a play-test day into one HTML page of charts that shows which skills are too strong.

**Architecture:** One Harmony prefix/postfix on `HealthObject.ReceiveDamage` sees every hit on the server; the mod's own damage sites name themselves with a one-line `HitScope` so the hook knows the source; `Draws.Release` records each shot. Hooks only append structs; every 5 s the main thread swaps the queue to one background worker that formats and appends (the game's thread never touches the disk). The Unity-free part (`TelemetryRules.cs`) is unit-tested; the script derives skills and metrics from raw columns.

**Tech Stack:** C# net472 (Unity 2020.3 Mono), MelonLoader 0.7.3, Harmony; the repo's console test runner; Python 3.13 with matplotlib 3.11 (installed), `python -I`.

**Spec:** `docs/superpowers/specs/2026-11-19-balance-telemetry-design.md` (approved by the owner 2026-11-19 on the condition "make sure it dosent cause preformance issues").

## Global Constraints

- Server only: clients never record, queue or write anything. `[Telemetry] Enabled` (default true) is per machine, never `[shared]`.
- No new network traffic.
- Unity 2020.3 Mono / net472 only: no API newer than .NET Framework 4.7.2.
- Nothing on the shot or hit path builds strings, touches files, takes locks or lets an exception out (each hook body is one try/catch).
- Every new patch, tick and hook gets a timing section (`static readonly int T = Timing.Section("...")`, `using var _t = Timing.Begin(T);` first). `Timing` is main-thread only: the writer thread never calls it.
- Files: `UserData\QuickshotRing\telemetry\shots-v1-YYYY-MM-DD.csv` and `hits-v1-YYYY-MM-DD.csv` (server-local date of each row); UTF-8 with a BOM written only when a file is created; numbers in invariant culture; a file stops growing at 256 MB; the queue holds at most 100 000 rows.
- Log lines start `[Archery] Telemetry:`; one Summary line at start; a given warning at most once per 10 minutes; no per-shot or per-hit line at any level.
- Edits to `Draws.cs`, `Volley.cs`, `Hits.cs`, `HolyArrow.cs`, `Quickdraw4.cs`, `Ricochet.cs`, `Shatter.cs`, `Weight.cs`, `SkyfallServer.cs`, `Main.cs` and `CLAUDE.md` wait for the parent session's "fixes are in" message. Until then no `.cs` file goes into `QuickshotRing\` or `QuickshotRing.Tests\` (the parent's 0.18.1 build compiles every `.cs` there, and MelonLoader applies every `[HarmonyPatch]` it finds): the new C# lives in `.superpowers\sdd\2026-11-19-telemetry\src\` and is built and tested in an isolated copy (Task 1 Step 0), never in the repo's `bin`/`obj`. Task 5 moves it in.
- No commits (the owner's rule: only when asked).
- Version 0.18.2, staged only after the parent has staged 0.18.1. Never close, start or touch the game or the server.
- Python: stdlib + matplotlib only; the page is one self-contained file (no network).

## Review Focus

1. A player or creature name holding a comma, a quote or a line break: one CSV field, read back intact by the script (Task 1 `CsvQuotes`; Task 4 selftest player `Smith, "Jo"`).
2. One flush whose rows straddle local midnight: each row lands in its own day's file (Task 2 `MidnightSplits`).
3. The day's file held open by another program (OneDrive, Excel): that write fails, the rows land exactly once on a later flush, the game's thread never waits (Task 2 `LockedFileRetries`).
4. Damage seconds after its arrow is gone (Skyfall's Nail, Ricochet bounces) or an arrow id reused by a later shot: the right shot within its window, the newest shot after reuse, shot 0 when unknown (Task 1 `ShotIndex*`).
5. Quitting while a background write runs: wait up to 2 s, then write the rest once; nothing lost or doubled (Task 2 `ShutdownWhileWriting`).

---

### Task 1: `TelemetryRules.cs`: rows, CSV, sources, spectral mix, shot index, hit scope (Unity-free)

**Files:**
- Create: `.superpowers/sdd/2026-11-19-telemetry/src/TelemetryRules.cs` (moves to `QuickshotRing/` in Task 5)
- Create: `.superpowers/sdd/2026-11-19-telemetry/src/TelemetryTests.cs` (moves to `QuickshotRing.Tests/` in Task 5)
- Isolated copy only (the repo's get the same lines in Task 5): its `QuickshotRing.Tests.csproj` compiles `src\TelemetryRules.cs` and `src\TelemetryTests.cs` by absolute path, its `QuickshotRing.csproj` compiles every `src\*.cs` except the tests, its `Program.cs` calls `TelemetryTests.Run();` after `SkyfallFinalFixTests.Run();`

- [ ] **Step 0: Make the isolated copy:** copy `Directory.Build.props`, `QuickshotRing\` (sources, csproj, `Resources\`) and `QuickshotRing.Tests\` (without `bin`/`obj`) to `<scratchpad>\iso\attmod\`, add the includes above, and check it matches the repo: `dotnet run --project <iso>\attmod\QuickshotRing.Tests -c Release` -> the repo's `ALL PASS (N)`; `dotnet build <iso>\attmod\QuickshotRing -c Release --no-incremental` -> 0 warnings, 0 errors.

**Interfaces:**
- Consumes: `ShotKind` (Wire.cs), `UltimateUse`, `Resolution` (Release.cs), `Build`, `Ultimates`, `SideOrbs`, `Bridges` (Build.cs), `MainPath` (Path.cs), `SkyCode` (SkyfallRules.cs), `SpectralKind` (Spectral.cs).
- Produces:
  - `enum HitSource : byte { Arrow, Ghost, Pellet, Needle, Core, Line, Ricochet, Shatter, HolyBeam, HolyBlast, Weight, Skyfall, Mod }`; CSV names `TelemetryCsv.Name(HitSource)`: arrow, ghost, pellet, needle, core, line, ricochet, shatter, holybeam, holyblast, weight, skyfall, mod.
  - `enum Victim : byte { Creature, Player, Thing }` (CSV creature/player/thing; Thing = a HealthObject that is not a combatant).
  - `struct ShotRow { long Utc; int Shot; uint Arrow; string Player; int Account; ShotKind Kind; UltimateUse Ult; int Ghosts, Pellets, Needles, Tier, Hold, Ms /* -1 unknown */, Locks; SkyCode Sky; Build Build; bool Test; }` with `static ShotRow Of(long utc, int shot, uint arrow, string player, int account, Resolution r, int? ms, int locks, SkyCode sky, Build build, bool test)` (Needles = `r.Plan.Echoes`).
  - `struct HitRow { long Utc; int Shot; string Player; HitSource Source; string Target; uint TargetId; Victim Victim; float Raw, Dealt, Cripple; bool Killed; float MaxHealth; }`.
  - `static class TelemetryCsv { const string ShotHeader, HitHeader; static void Shot(StringBuilder sb, in ShotRow r, string session); static void Hit(StringBuilder sb, in HitRow r, string session); static string FileName(string kind, DateTime localDay); static string Name(HitSource s); }`
  - `static class TelemetryRules { static HitSource SourceOf(SpectralKind kind, bool core); static bool TryDecide(bool scoped, HitSource scope, bool dealing, bool hasTool, bool toolOnArrow, bool knownBeam, out HitSource source); static float Dealt(float before, float after); static bool Killed(float before, float after); static string CleanName(string unityName); }`
  - `struct SpectralMix { float Ghost, Pellet, Needle, Core; void Add(HitSource kind, float share); float Total { get; } float Of(HitSource kind); HitSource Largest { get; } }`
  - `readonly struct ShotRef { int Shot; string Player; int Account; double At; }` and `sealed class ShotIndex { const double RealArrowSeconds = 15, LateSeconds = 120, PlayerSeconds = 15; int Next(); void Put(uint arrow, int account, string player, int shot, double now); bool TryArrow(uint arrow, double now, double maxAge, out ShotRef s); bool TryPlayer(int account, double now, out ShotRef s); void Beam(uint beam, uint arrow); bool IsBeam(uint beam); bool TryBeam(uint beam, double now, out ShotRef s); }`
  - `readonly struct HitScope : IDisposable { static HitScope As(HitSource source, uint arrow = 0); static HitScope Spectral(uint release, in SpectralMix mix); static bool Open; static HitSource Source; static uint Arrow; static SpectralMix Mix; void Dispose(); }` (the statics are the innermost open scope; Dispose restores the one outside it; `default(HitScope).Dispose()` does nothing).

Exact headers (the script reads these names):

```
time,session,shot,arrow,player,account,kind,ult,ghosts,pellets,needles,tier,hold,ms,locks,sky,main,quick,volley,charge,ults,ricochet,shatter,gatherer,test
time,session,shot,player,source,target,target_id,victim,raw,dealt,cripple,killed,max_health
```

`time` is local `yyyy-MM-dd HH:mm:ss.fff` from `Utc`; floats `0.###`; bools 0/1; `ms` empty when -1; `ults` is `Q4+Seek+Holy` order, `-` for none; enums by name (`Early`, `Line`, `Skyfall`, `Multi`, ...).

The hook's classification (`TryDecide`), first match wins:

```
scoped                         -> scope
hasTool && toolOnArrow         -> dealing ? Mod : Arrow     (a hand-made hit without a scope is a bug marker)
hasTool && knownBeam           -> HolyBeam                  (the game's beam collision, TunnelGuard's Apply)
hasTool                        -> false                     (melee, thrown things: not archery)
knownBeam                      -> HolyBlast                 (the blast's ImpactHit carries the beam as Wielder)
dealing                        -> Mod
else                           -> false                     (creature attacks, falls)
```

- [ ] **Step 1: Write the failing tests** in `TelemetryTests.cs` (`internal static class TelemetryTests { internal static void Run() }`, using `Program.Check`):
  - `HeadersExact`: both constants equal the two lines above.
  - `ShotRowFormat`: a Burst release (`Held`, ghosts 15, pellets 15, hold 2, ms 2100, Multi main, `Q1 V3 C0`, Ricochet) formats to the exact expected line, session `20261119-200000`; `ms` null gives an empty field.
  - `CsvQuotes`: target `Smith, "Jo"` becomes `"Smith, ""Jo"""`; a name with `\n` is quoted; a plain name is not.
  - `NumbersInvariant`: under `CultureInfo.CurrentCulture = de-DE`, 1234.5 formats `1234.5`.
  - `FileNames`: `FileName("hits", 2026-11-19)` is `hits-v1-2026-11-19.csv`.
  - `SourceOf`: Inner and Outer -> Ghost, Pellet -> Pellet, Echo -> Needle, any kind with core -> Core.
  - `DecideOrder`: each line of the table above, plus scope beating everything.
  - `DealtAndKilled`: (10, 4) dealt 6 not killed; (3, 0) dealt 3 killed; (3, -2) dealt 3 killed; (0, 0) not killed; NaN in either gives dealt 0.
  - `MixSplit`: Add(Ghost 0.6), Add(Pellet 0.3), Add(Pellet 0.3) -> Total 1.2, Of(Pellet) 0.6, Largest Ghost on a tie order Ghost, Pellet, Needle, Core; Add of a non-spectral source counts as Ghost.
  - `ShotIndexArrow`: Put(7, ...) at t=0; TryArrow(7, 10, 15) found; TryArrow(7, 20, 15) not; TryArrow(7, 20, 120) found; Put(7, shot 2) replaces shot 1; unknown arrow not found; 5000 Puts keep the newest 4096 findable (cap).
  - `ShotIndexPlayerAndBeam`: TryPlayer within 15 s only; Beam(900, 7) then TryBeam(900) gives arrow 7's shot even when the beam was registered before arrow 7's Put; IsBeam false for unknown.
  - `ScopeNesting`: As(Line, 5) inside it As(Needle, 5) -> Source Needle; after the inner Dispose Source Line; after the outer, Open false; Spectral(9, mix) carries the mix; `default(HitScope).Dispose()` leaves an open scope untouched.
  - `CleanName`: `Turabada(Clone)` -> `Turabada`; null -> `?`.

- [ ] **Step 2: Run** `dotnet run --project <iso>\attmod\QuickshotRing.Tests -c Release`. Expected: build error (types missing).

- [ ] **Step 3: Implement `TelemetryRules.cs`** to the interfaces above. `ShotIndex` keeps `Dictionary<uint, ShotRef>` for arrows (cleared of entries older than `LateSeconds` when it passes 4096, then hard-cleared if still full), `Dictionary<int, ShotRef>` for accounts, `Dictionary<uint, uint>` beam -> arrow (cleared past 64). `HitScope` statics are plain fields (main thread only).

- [ ] **Step 4: Run the tests.** Expected: `ALL PASS (N)` with N = the old count plus the new checks.

### Task 2: `TelemetryLog`: the background writer and its cost (Unity-free, in `TelemetryRules.cs`)

**Files:**
- Modify: `src/TelemetryRules.cs` (add the class), `src/TelemetryTests.cs` (add the tests and `internal static int Sample(string dir)`); the isolated copy's `Program.cs` gets a `--telemetry-sample <dir>` mode beside `--plan` that returns `TelemetryTests.Sample(dir)` (the repo's in Task 5)

**Interfaces:**
- Consumes: Task 1's rows and `TelemetryCsv`.
- Produces: `sealed class TelemetryLog { TelemetryLog(string folder, string session, long maxFileBytes = 256L << 20, int maxRows = 100000); void Add(in ShotRow r); void Add(in HitRow r); int Pending { get; } long Dropped { get; } bool Writing { get; } bool Flush(bool background); bool WaitIdle(int milliseconds); string? TakeProblem(); }`. `Flush` returns false (rows keep waiting) while a write runs. `TakeProblem` returns the newest problem once, then null.

The hand-over (the only threading in the feature):

```
main  Add:   if pending rows >= maxRows: Dropped++ else append to shots/hits
main  Flush: if writing: return false
             swap (shots, hits) with (outShots, outHits)   // the out lists are empty: the worker cleared them
             writing = true (volatile)
             background ? ThreadPool.QueueUserWorkItem(Write) : Write()
worker Write: try   format rows into one StringBuilder per (kind, local day); per file:
                    stop when the file is >= maxFileBytes (count dropped, one problem);
                    create it with BOM + header when missing or empty; append the rest (UTF-8, no BOM);
                    a failed append keeps that text in `carry[path]` and is retried first next time (carry capped at 64 MB)
              catch everything -> problem
              finally clear outShots/outHits; writing = false (volatile)
```

- [ ] **Step 1: Write the failing tests** (each in its own fresh folder under `Path.GetTempPath()`, deleted after):
  - `WritesHeaderOnce`: two flushes of one row each into the same day -> one BOM, one header, two rows.
  - `MidnightSplits`: rows at 23:59:59.900 and 00:00:00.100 local in one flush -> two files, one row each.
  - `LockedFileRetries`: hold the hits file open with `FileShare.None`, flush (no throw, `TakeProblem` not null), release, flush with no new rows -> the row is in the file exactly once.
  - `CapDrops`: `maxRows` 3, Add 5 -> Pending 3, Dropped 2.
  - `FileCap`: `maxFileBytes` 200, many rows -> the file stays under 200 + one batch, `Dropped` counts the rest, `TakeProblem` says the file is full.
  - `BusyRefuses`: while a background write runs (a test hook delays `Write` by 200 ms), `Flush` returns false and the new rows stay pending; after `WaitIdle(2000)` the next flush writes them.
  - `ShutdownWhileWriting`: background flush, Add more, `WaitIdle(2000)` then `Flush(false)` -> every row exactly once.
  - `Cost` (report only, not a pass/fail on speed): time 100 000 `Add` calls and one synchronous `Flush` of them; print `TELEMETRY COST: add N ns/row, write M ms per 100k rows, K MB` to the console; fail only if Add exceeds 2 000 ns/row (a regression guard far above the expected tens of ns).

- [ ] **Step 2: Run them.** Expected: build error (no `TelemetryLog`).

- [ ] **Step 3: Implement `TelemetryLog`** to the interface and the hand-over above (`File.WriteAllText(path, header, new UTF8Encoding(true))` on create, `File.AppendAllText(path, text, new UTF8Encoding(false))` after; folder created on the first write). The delay hook is `internal static int TestDelayMs` (0 in the mod).

- [ ] **Step 4: Run the tests.** Expected: `ALL PASS`, and the `TELEMETRY COST` line printed. Record its numbers for the owner.

- [ ] **Step 5: Add the sample mode:** `dotnet run --project ...Tests -c Release -- --telemetry-sample <dir>` writes a realistic day (60 shots of every skill by three players, their hits, a few kills, a PvP hit, a Skyfall cast, names with commas) through `TelemetryLog` and returns 0. Task 4 reads it.

### Task 3: `Telemetry.cs`: the server hooks (new file, wired in Task 5)

**Files:**
- Create: `.superpowers/sdd/2026-11-19-telemetry/src/Telemetry.cs` (moves to `QuickshotRing/` in Task 5); built in the isolated copy.

**Interfaces:**
- Consumes: Tasks 1-2; `Cfg.Category`, `Cfg.Folder`, `Timing`, `Logs`, `Builds.NameOf`, `Weight.ArrowOf`, `Targets.IdOf`, `Targets.RootOf`, `Hits.Dealing`, `Claims.LocksOf(uint, bool)`.
- Produces (Task 5 calls these):
  - `internal static void Init()` — `[Telemetry] Enabled` (bool, true; description: server, not shared, re-read live, what it writes and where, the report command); must run before `Cfg.Ready()`.
  - `internal static void Shot(uint arrow, Player? owner, Resolution r, Build build, int? ms, SkyCode sky, bool test)` — server; `locks` = `Claims.LocksOf(arrow, false).Count`; puts the shot in the index and queues its row.
  - `internal static void Beam(uint beam, ImpactTool? arrowTool)` — links a Holy beam to the arrow it sacrificed (`Weight.ArrowOf(arrowTool)?.Entity.Identifier`).
  - `internal static void Tick()` — every 5 s (unscaled time): `Flush(true)`; problems and drops logged per Global Constraints.
  - `internal static void Shutdown()` — `WaitIdle(2000)` then `Flush(false)`.
  - The patch class `Damage` on `HealthObject.ReceiveDamage(float, float, DamageData)`: Prefix decides (Task 1 table) and keeps `{ on, before, source, arrow, beam, mix, shooter account/name }` in `__state`; Postfix reads health after and queues one row, or one row per kind of a spectral mix (each kind's share of raw, dealt and cripple; `killed` only on `Mix.Largest`).

Attribution in the postfix: the scope's or the tool's arrow -> `TryArrow` (window `RealArrowSeconds` for `Arrow`, `LateSeconds` for scoped sources); else the beam -> `TryBeam`; else the shooter's account (`ImpactHit.PlayerWielder`, else `ImpactHitWithTool.HittingPlayer`) -> `TryPlayer`; else shot 0 with the shooter's name or `?`. Target: a `PlayerHealth` is the player's name (`Victim.Player`); else `CleanName(Targets.RootOf(h).name)`, `Targets.IdOf(h)`, `Creature` when `h.IsCombatant` else `Thing`. The beam's id is the tool's `Wielder.Identifier` (a beam has no pickup, so `Wielder` is its root entity); the blast's is `ImpactHit.Wielder.Identifier`.

`On` = the setting and `NetworkSceneManager.IsServer`; off costs one bool test per hit and per shot. The log is created on first use (`Cfg.Folder\telemetry`, session = start time `yyyyMMdd-HHmmss`), with one Summary line naming the folder.

- [ ] **Step 1: Refresh the isolated copy** from the repo if the parent has changed it since Step 0 (sources only; the includes stay).
- [ ] **Step 2: Write `Telemetry.cs`** to the interface above (timing sections `patch HealthObject.ReceiveDamage / Telemetry.Damage (prefix)`, `(postfix)`, `Telemetry.Shot`; `Tick`'s section is Main's in Task 5).
- [ ] **Step 3: Build the isolated copy.** Expected: 0 warnings, 0 errors. Run its tests: `ALL PASS`.

### Task 4: `tools/telemetry_report.py`: the charts

**Files:**
- Create: `tools/telemetry_report.py`

**Interfaces:**
- Consumes: the CSV headers of Task 1; the sample folder of Task 2 Step 5.
- Produces: `python -I tools/telemetry_report.py [files or folders...] [--day YYYY-MM-DD | --all] [--out FILE.html] [--gap-cap 15] [--selftest]`. Default input: `<server>\UserData\QuickshotRing\telemetry`; default day: the newest found; default out: `report-<day>.html` beside the data. Prints a text table of the skills and the page's path.

Load the `dataviz` skill before writing chart code.

Skill of a shot (first match wins; the report's only interpretation of the raw columns):

```
sky Skyfall -> "Skyfall"; sky Fizzled -> "Skyfall (fizzled)"
ult Beam -> "Holy Arrow"; ult Line -> "Quickdraw 4"; kind Snap -> "Quickdraw"
locks > 0 and ghosts > 0 and kind in (Held, Charged) and not (kind Charged and main Charge) -> "Seeking Volley"
pellets > 0 -> "Burst"
ghosts > 0 and kind Charged and main Charge -> "Charge Shot {tier} + heavy"
ghosts > 0 -> "Two-Shot" if ghosts <= 5 else "Three-Shot"
kind Charged -> "Charge Shot {tier}"
else -> "Plain"
```

Metrics (creature damage = rows with victim creature; players are their own columns; things are left out):
- per shot: the sum of `dealt` of its creature rows (0 for a shot that hit nothing); damage per shot = mean, median, p90 over every shot of the skill.
- sustained damage per minute = 60 x sum(dealt) / sum(cost), cost = min(seconds since the same player's previous shot in the same session, gap cap); a player's first shot costs the gap cap.
- kills, kills per 100 shots; player damage and player kills; mean held seconds (`ms` / 1000 where known).
- "x median": a skill's DPM over the median DPM of the skills with at least 20 shots, Skyfall left out; 1.5 or more is flagged.

Page: the skill table first (flag cells), then charts: DPM by skill (sorted, median line, flagged bars), damage per shot (median and p90), shots by skill, kills per 100 shots, damage by source per skill (stacked, shares), DPM per main path, a player x skill table, damage per minute over the day by skill. Charts as base64 PNG (Agg); fonts and colours per the dataviz skill.

- [ ] **Step 1: Write `--selftest`** first: it writes a tiny known day to a temp folder (two players, three skills, one kill, a PvP row, a Skyfall cast, a player named `Smith, "Jo"`, a shot with no hits), runs the pipeline and asserts: shot counts per skill; Burst damage per shot mean exactly as built; DPM with the gap cap as built; kills; Skyfall absent from the median; the comma name read intact; an unknown column order still read by name; then prints `TELEMETRY REPORT SELFTEST PASS`.
- [ ] **Step 2: Run** `python -I tools/telemetry_report.py --selftest`. Expected: fails (no pipeline yet).
- [ ] **Step 3: Implement** the reader, the skill rules, the metrics and the page.
- [ ] **Step 4: Run the selftest.** Expected: `TELEMETRY REPORT SELFTEST PASS`.
- [ ] **Step 5: End to end:** run Task 2's sample mode into `<scratchpad>\telemetry-sample`, then `python -I tools/telemetry_report.py <scratchpad>\telemetry-sample --out <scratchpad>\telemetry-sample\report.html`. Expected: no error, every chart present. Show the owner the page (made-up data, said so).

### Task 5: Wiring (only after the parent's "fixes are in" message)

**Files (one to three lines each, all in the repo):**
- `QuickshotRing/Draws.cs` `Release`: after `Claims.PutResolved` / Weight arming, `if (server) Telemetry.Shot(id, owner, r, build, ms, sky, test);` (inside `if (id != 0u)`).
- `QuickshotRing/Volley.cs` the `Hits.Spectral(...)` call in `VolleySim`: add `TelemetryRules.SourceOf(s.kind, s.core)` as the last argument.
- `QuickshotRing/Hits.cs`: `Spectral(..., bool jumps = true, HitSource kind = HitSource.Ghost)` adds `share` to `Pending.mix[key]` (`Dictionary<int, SpectralMix>`); `FlushSpectral` deals `hitNow.Hit()` inside `using (HitScope.Spectral(rp.Key, mix))`.
- `QuickshotRing/HolyArrow.cs` `BeamCreated.Postfix`: `Telemetry.Beam(entity.Identifier, arrowTool);` after `beams[...] = ...`.
- `QuickshotRing/Quickdraw4.cs` the server line: `using var _tel = HitScope.As(HitSource.Line, shotEntity.Identifier);` before the needles block; `using var _needle = HitScope.As(HitSource.Needle, shotEntity.Identifier);` first inside `if (needles > 0) {`.
- `QuickshotRing/Ricochet.cs` `Hit(...)`: first statement `using var _tel = HitScope.As(HitSource.Ricochet, release);`.
- `QuickshotRing/Shatter.cs` the blast's dealing loop: `using var _tel = HitScope.As(HitSource.Shatter, rec.Id);` before the loop.
- `QuickshotRing/Weight.cs` `Deal`: `using (HitScope.As(HitSource.Weight, d.Release)) h.DetectImpact(hit);`.
- `QuickshotRing/SkyfallServer.cs`: `Deal()` `using (HitScope.As(HitSource.Skyfall, b.R.S.ArrowId)) h.DetectImpact(hit);`; `Kill()` `using (HitScope.As(HitSource.Skyfall, r.S.ArrowId)) ph.DetectImpact(hit);`.
- `QuickshotRing/Main.cs`: `Telemetry.Init();` before `Cfg.Ready();`; `try { using (Timing.Begin(TTelemetry)) Telemetry.Tick(); } catch (...)` before the headless return, with section `Main.OnUpdate / Telemetry.Tick (hand rows to the writer)`; `try { Telemetry.Shutdown(); } catch (...)` in `OnApplicationQuit` before `TimingHost.Shutdown()`.
- `CLAUDE.md`: a table row for `Telemetry.cs` / `TelemetryRules.cs`; the Unity-free file list gains `TelemetryRules.cs` (fifteen); the report command beside the other commands; under "easy to get wrong": every new damage site opens a `HitScope`, or its damage shows as `mod`.

- [ ] **Step 1: Move the new files in** (`src\TelemetryRules.cs`, `src\Telemetry.cs` to `QuickshotRing\`; `src\TelemetryTests.cs` to `QuickshotRing.Tests\`), add the tests' `<Compile Include="..\QuickshotRing\TelemetryRules.cs" />` and `TelemetryTests.Run();` (and the `--telemetry-sample` mode) to the repo's test project. Re-read each target's current text (the parent's fix wave may have moved it) and make the edits.
- [ ] **Step 2:** `dotnet run --project QuickshotRing.Tests -c Release` -> `ALL PASS`; `dotnet build QuickshotRing -c Release --no-incremental` -> 0 warnings, 0 errors.
- [ ] **Step 3: Grep check:** every `Hits.Apply(`, `Hits.Copy(`, `.Hit()` and `DetectImpact(` in `QuickshotRing/*.cs` is under a `HitScope`, the spectral flush, or a tool on an arrow or beam (list any that is not, and fix it).

### Task 6: Stage, install, play-test

- [ ] **Step 1:** Wait until the parent has staged 0.18.1 (its message). Then: `bash tools/stage-build.sh 0.18.2 no "Balance telemetry: the server records every shot and archery hit to UserData\QuickshotRing\telemetry (CSV per day); tools/telemetry_report.py draws the graphs" "<steps below>"` -> `STAGED 0.18.2`.
- [ ] **Step 2:** `bash tools/install-when-closed.sh` with `run_in_background`. Tell the owner in two lines: version, what changed, what to test, friends need nothing new for this.
- [ ] **Step 3: Play-test steps** for the owner (game terms): shoot a few of each skill at creatures; afterwards run the report command and open the page. On the server, `[Timing] Enabled = true` for that session (a test server, editable without asking) so the timing report shows the hook's and the hand-over's real cost.
- [ ] **Step 4: After the play-test** (Claude's own checklist): `[Archery] Telemetry:` start line on the server; the two day files exist and grow; no `mod` rows (an unscoped site); `timing-server.txt` shows the hook under a few microseconds a call and no telemetry section among the worst frames.
