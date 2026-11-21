# Compiled Balance Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The server compiles every shot and hit into small per-day statistics as they happen, writes no per-shot file by default, and the balance report shows the same page from those statistics.

**Architecture:** A new Unity-free class, `TelemetryStats`, takes the same `ShotRow` and `HitRow` structs the row writer takes today, adds them to an in-memory table for the day, keeps each shot's own damage for 125 s (hits arrive late) and then folds it into a per-skill tally. About once a minute the game's thread copies the table's numbers and a background thread formats them and replaces one small file. `tools/telemetry_report.py` gains a second loader that builds the same result dictionary `analyze` returns, so `render` is untouched.

**Tech Stack:** C# (net472, Unity 2020.3 Mono at run time: no newer BCL APIs), the project's own console test runner (`Check(name, bool)` in `BluesArcheryOverhaul.Tests`), Python 3 stdlib.

**Spec:** `docs/superpowers/specs/2026-11-20-compiled-telemetry-design.md`. Read `CLAUDE.md` (repo root of the main checkout, git-ignored) for the `Telemetry.cs`, `TelemetryRules.cs` row and the project rules.

## Global Constraints

- Nothing on the game's thread may format text, touch the disk or call `Timing` off the main thread. The hooks only update numbers; `Flush` on the game's thread only copies numbers.
- Nothing in `TelemetryStats` throws out of `Add`, `Tick`, `Flush`, `CloseAll` or the worker; a problem is kept for `TakeProblem()` as `TelemetryLog` does.
- The row files' format, the telemetry CSV column names, wire formats, shared settings and the shared checksum do not change. `[Telemetry]` settings are server-only and not shared.
- Gap cap 15 s, close after 125 s, tally step 0.5 damage, write about every 60 s: use these exact values.
- Old statistics files are never deleted. The low-disk stop (`[Telemetry] MinFreeMb`, 0.19.4) also stops the statistics file from being written; one implementation of the check, used by both writers.
- Player names are free text: every text field goes through the same CSV quoting `TelemetryCsv` uses.
- Do not stage, deploy or install; do not bump the version; do not push; no AI attribution in commits; `git add` by explicit path. Keep each file's existing line endings (`BluesArcheryOverhaul.Tests/TelemetryTests.cs` is CRLF).

## Review Focus

1. **The server is killed between writes.** The file on disk must still be a whole, valid file from the last write (written beside, then moved over); at most the last minute and the open shots are lost. Test in Task 1.
2. **The statistics file is held open by OneDrive or Excel.** No throw, nothing lost: the day stays dirty and is written on a later flush. Test in Task 1.
3. **A shot before midnight whose hit lands after it.** The hit counts on the shot's own day; that day's table is dropped only after its last open shot closed and its file was written. Test in Task 1.
4. **A player called `Smith, "Jo"` (or with a line break).** The file parses back to the same name. Test in Task 1 and in the report's self-test (Task 3).
5. **Two server runs on one day, and a folder that still holds row files.** The report adds the runs together and never counts a day twice from both kinds of file. Test in Task 3.

---

### Task 1: `TelemetryStats`, the compiler

**Files:**
- Create: `BluesArcheryOverhaul/TelemetryStats.cs`
- Modify: `BluesArcheryOverhaul/TelemetryRules.cs` (the low-disk check moves out of `TelemetryLog` into one small shared helper both writers use; `TelemetryCsv`'s field writers become reachable from the new file)
- Modify: `BluesArcheryOverhaul.Tests/BluesArcheryOverhaul.Tests.csproj` (compile the new file), `BluesArcheryOverhaul.Tests/Program.cs` (run the new tests)
- Test: `BluesArcheryOverhaul.Tests/TelemetryStatsTests.cs` (new)

**Interfaces:**
- Consumes: `ShotRow`, `HitRow`, `DealtBy`, `Victim`, `TelemetryCsv.LocalDay`, `TelemetryCsv.Name(DealtBy)` (all in `TelemetryRules.cs`).
- Produces:

```csharp
internal sealed class TelemetryStats {
    internal const double GapCapSeconds = 15, CloseSeconds = 125, Step = 0.5;
    internal TelemetryStats(string folder, string session, string version);
    internal long MinFreeBytes { get; set; }
    internal Func<long>? FreeProbe { get; set; }        // tests only
    internal int FreeAskMs { get; set; }                // tests only, 30000 by default
    internal void Add(in ShotRow r);                    // game thread
    internal void Add(in HitRow h);                     // game thread; h.Shot names its shot (0 = none)
    internal void Tick(long utcNow);                    // game thread: closes shots older than CloseSeconds
    internal void CloseAll();                           // quitting: every open shot closes now
    internal bool Flush(bool background);               // false while a write is running (nothing lost: the days stay dirty)
    internal bool WaitIdle(int milliseconds);
    internal string? TakeProblem();
    internal int OpenShots { get; }                     // shots waiting for late hits
    internal int Lines { get; }                         // stat lines held in memory, all days
    internal static string FileName(DateTime localDay, string session);   // "stats-v1-2026-11-19-20261119-180000.csv"
}
```

**The file** (one per local day per server run, in the telemetry folder; CSV; the first field is the record type; a row whose first field starts with `#` is that type's header and the reader maps columns by name):

```
#,Blue's Archery Overhaul balance statistics,v1,gap_cap_s,15,step,0.5,session,20261119-180000
#S,day,player,account,kind,ult,ghosts,pellets,needles,tier,hold,locks,sky,main,quick,volley,charge,ults,ricochet,shatter,gatherer,test,version,settings,shots,cost_s,damage,kills,pvp_damage,pvp_kills,held_ms,held_n,by_arrow,by_ghost,by_pellet,by_needle,by_core,by_line,by_ricochet,by_shatter,by_holybeam,by_holyblast,by_weight,by_skyfall,by_mod
S,2026-11-19,Ana,101,Held,None,15,15,0,0,1,0,None,Multi,0,3,1,Seek,1,0,0,0,0.19.5,00c0ffee,42,301.5,588.25,7,0,0,61234,42,210.5,180,150.75,0,0,0,47,0,0,0,0,0,0
#D,day,kind,ult,ghosts,pellets,locks,tier,sky,main,quick,test,version,settings,shots,zero,steps
D,2026-11-19,Held,None,15,15,0,0,None,Multi,0,0,0.19.5,00c0ffee,42,6,12:3;13:5;14:9;27:19
#T,day,hour,version,damage
T,2026-11-19,18,0.19.5,2210.5
#M,day,version,hits,unattributed_hits,unattributed_damage,things,mods
M,2026-11-19,0.19.5,311,2,7.5,4,0
```

- An `S` line's key is every column up to `settings` (the columns of `TelemetryCsv.ShotHeader` except `time`, `session`, `shot`, `arrow`, `ms`, written exactly as `TelemetryCsv.Shot` writes them, with `day` in front). `cost_s`: the gap since that archer's previous shot in this run (archer = account, or the name when the account is 0), clamped to 0..15, and 15 for an archer's first shot. `held_ms`, `held_n`: sum and count of `Ms` where it is known (`>= 0`).
- A hit with `Victim.Thing` only counts in `M.things` (and `M.mods` when its source is `Mod`). A hit on a player adds to its shot's `pvp_damage`/`pvp_kills` and nothing else. A hit on a creature adds `Dealt` to its shot's `damage`, `kills`, its `by_<source>` column and the shot's own running sum; with no shot (`Shot == 0` or unknown) it adds to `M.unattributed_*`. A creature hit also adds to the `T` hour of the hit's own local time unless its shot's `Sky` is not `None`. `M.hits` counts every hit given to `Add`. Hits of a shot with `Test` set touch only that shot's `S` line and `M.hits`. This mirrors `analyze` in `tools/telemetry_report.py` line for line: read it.
- A `D` line is keyed by the columns the report's `skill_of` reads, without the player. When a shot closes, its own creature damage goes in: `zero` when it is exactly 0, else step `floor(damage / 0.5)`.
- Numbers: counts as integers; damage and seconds with up to 4 decimals, invariant culture; NaN and infinity as 0.

- [ ] **Step 1: Write the failing tests** in `TelemetryStatsTests.cs`, each ending in `Check(...)` calls, run from `Program.Main` beside `TelemetryTests.Run()`. Use a temp folder per test as `TelemetryTests.InFolder` does, a local parser that splits the written file into records by type, and times built with a helper that takes local wall-clock values. Tests and what each asserts:
  - `OneShotOneLine`: one `ShotRow` (Held, 15 ghosts, 15 pellets, Multi, account 101, `Ms` 1200) and three creature hits on it (arrow 5, ghost 6, pellet 4, the last one a kill), `CloseAll`, `Flush(false)` → one `S` line with shots 1, cost_s 15, damage 15, kills 1, held_ms 1200, held_n 1, by_arrow 5, by_ghost 6, by_pellet 4; one `D` line with shots 1, zero 0, steps `30:1`; `T` for that hour 15; `M` hits 3.
  - `GapCostClamps`: the same archer shoots at t, t+4 s, t+40 s, and at a time 10 s earlier than the last → cost_s 15 + 4 + 15 + 0.
  - `SameKindSharesALine` / `OtherPlayerOtherLine`: two shots of one kind by one archer make one `S` line with shots 2; the same kind by another archer makes a second `S` line and still one `D` line with shots 3.
  - `VictimsApart`: a thing hit counts only in `M.things`; a player hit only in `pvp_damage`; a hit with `Shot = 0` only in `M.unattributed_*` and `T`; a `Mod` source counts in `M.mods`.
  - `SkyfallOffTheTimeline`: hits of a shot with `Sky = SkyCode.Skyfall` are in its `S` line and not in `T`.
  - `TestBuildApart`: hits of a `Test` shot change only its `S` line (`test` 1); `T` and `M` stay as they were.
  - `LateHitThenClose`: a hit 100 s after its shot counts; `Tick` at 124 s leaves `OpenShots == 1` and no `D` shots; `Tick` at 126 s gives `OpenShots == 0` and the `D` line; a hit naming that shot at 130 s is unattributed.
  - `MissIsZero`: a shot with no hit closes into `zero`, not into step 0.
  - `MidnightHit` (Review Focus 3): a shot at 23:59:50, its hit at 00:00:10 → the hit is on the first day's `S` line and in the second day's `T`; after `Tick` past the close and a `Flush(false)`, both days' files exist and `Lines` no longer counts the first day.
  - `OddNames` (Review Focus 4): players `Smith, "Jo"` and `Two\nLines` come back from the file unchanged.
  - `WholeFileOrOldFile` (Review Focus 1): after one flush, add more, make the next write fail (hold the target open with `FileShare.None`), flush: the file still parses and holds the first flush's numbers, `TakeProblem()` is not null and no `.tmp` is left as the day's file.
  - `LockedFileKeepsTheDay` (Review Focus 2): release that lock, flush again with nothing new added: the file now holds the newer numbers.
  - `TwoRunsTwoFiles`: two `TelemetryStats` with different sessions on one day write two files, neither touching the other's.
  - `LowDiskWritesNothing`: with `FreeProbe` under `MinFreeBytes` no file appears and `TakeProblem()` names `MinFreeMb`; with room again the file appears with everything, because the table was never dropped.
  - `BusyRefuses`: while a background write runs, `Flush` returns false and the next one writes.
  - `NothingThrows`: NaN and negative `Dealt`, a null player, `Shot = -5`, a hit before any shot → no exception, and the file's numbers are all finite.
  - `StaysSmall`: 200,000 shots by 20 archers over 3 days with `Tick` and a flush every 2,000 shots → `OpenShots` never above what 125 s of that stream holds, `Lines` under 2,000 at every flush, each file under 200 KB. Print one line: `TELEMETRY STATS: <rows> rows in, <bytes> bytes of files, add <ns> ns/row, most open shots <n>`.
- [ ] **Step 2: Run** `dotnet run --project BluesArcheryOverhaul.Tests -c Release`. Expected: does not compile (`TelemetryStats` missing).
- [ ] **Step 3: Implement `TelemetryStats`.** Per-day tables in dictionaries keyed by a struct of the key columns; open shots in a dictionary by shot number plus a queue in time order; a day is dropped once a later day exists, none of its shots is open and its last copy was written. `Flush` copies the dirty days' numbers on the caller's thread and the worker (ThreadPool, as `TelemetryLog`) formats each into text and writes `<file>.tmp` then `File.Replace`/`File.Move`. Move the low-disk check out of `TelemetryLog` into one helper both use, keeping `TelemetryLog`'s members and tests as they are.
- [ ] **Step 4: Run the tests.** Expected: `ALL PASS (N)` with N = 2252 plus the new checks, and the existing `TELEMETRY COST:` line still printed.
- [ ] **Step 5: Commit** (`BluesArcheryOverhaul/TelemetryStats.cs`, `BluesArcheryOverhaul/TelemetryRules.cs`, the three test-project files).

### Task 2: the server uses it

**Files:**
- Modify: `BluesArcheryOverhaul/Telemetry.cs`
- Modify: `BluesArcheryOverhaul.Tests/TelemetryTests.cs` (`Sample`), `BluesArcheryOverhaul.Tests/Program.cs` if the arguments change

**Interfaces:**
- Consumes: Task 1's `TelemetryStats`.
- Produces: the setting `[Telemetry] KeepRows` (bool, false, server, not shared); `Tests.exe --telemetry-sample <dir> [days shotsPerDay archers]` writes BOTH the row files and the statistics files for the same made-up stream, which Task 3's `--compare` reads.

- [ ] **Step 1:** In `Telemetry.cs` every `Log.Add(row)` (one for shots, two for hits) becomes one private `Record(in ...)` that gives the row to the statistics and, only when `KeepRows` is on, to the row writer. `Tick` (every 5 s as now): `stats.Tick(DateTime.UtcNow.Ticks)`, `stats.MinFreeBytes`, and `stats.Flush(true)` every 60 s; the row writer is flushed as now only if it exists. Problems and dropped rows are reported as now, at most every 600 s. `Shutdown`: `CloseAll`, wait up to 2 s for a running write, `Flush(false)`, then the row writer as now. The start-up line says statistics are compiled to the folder and names `KeepRows`; the `Enabled` and `MinFreeMb` descriptions are reworded to match (no per-shot files unless `KeepRows`).
- [ ] **Step 2:** `TelemetryTests.Sample` gives each row to a `TelemetryStats` as well as the `TelemetryLog`, calls `Tick` with the stream's own time as it goes, and at the end `CloseAll` and `Flush(false)`. The default one-day run must still write byte-identical row files (md5 of `shots-v1-2026-11-19.csv` `231ec1339a391579011e7a13d00e15be`, of `hits-v1-2026-11-19.csv` `a93b2743fb74f61512bc8be1e16ad515`). The stress form also prints the statistics files' total size.
- [ ] **Step 3: Verify.** `dotnet run --project BluesArcheryOverhaul.Tests -c Release` → `ALL PASS`. `dotnet build BluesArcheryOverhaul -c Release --no-incremental` and `dotnet build BluesArcheryTestKit -c Release --no-incremental` → 0 warnings, 0 errors. `BluesArcheryTestKit.Tests` → `ALL PASS (401)`. The two md5 values above. `Tests.exe --telemetry-sample <dir> 30 100000 20` → report the statistics files' total size and the run time.
- [ ] **Step 4: Commit.**

### Task 3: the report reads the statistics

**Files:**
- Modify: `tools/telemetry_report.py`
- Modify: a copy of `CLAUDE.md` in your worktree root (git-ignored there): the telemetry row, the commands block, the test count

**Interfaces:**
- Consumes: the file layout in Task 1.
- Produces:
  - `read_stats(paths, day=None, min_shots=20, include_test=False, version=None) -> (r, days)` where `r` has exactly the keys `analyze` returns (`skills`, `players`, `mains`, `median_dpm`, `timeline`, `step_minutes`, `unattributed`, `things`, `test_shots`, `tunings`, `mods`, `shots`, `hits`, `gap_cap`, `min_shots`, `sessions`, `archers`) and every table row has the fields `render` and `summary_text` read. The skill of a line comes from the existing `skill_of`. `per_shot_mean` is damage / shots. In `skills`, `per_shot_median` and `per_shot_p90` come from the `D` lines of that skill added together (a step counts as its middle, `(i + 0.5) * step`; `zero` as exactly 0; the median as `statistics.median` defines it, the p90 by the same nearest rank as `p90`); in `players` and `mains` they are the mean (the page does not show them there). The timeline starts from hours: `step_minutes` is the first of 60, 180, 360, 720, 1440 that gives at most `MAX_COLUMNS` columns.
  - `main`: statistics files are used when the chosen folder has any; `--rows` forces the row files. When a folder has both, only one kind is ever read (Review Focus 5). `--gap-cap` other than the file's value with statistics prints one line saying the statistics were compiled with 15 s.
  - `--compare`: reads both kinds from the same folder and exits 1, naming each, when any count differs, any sum differs by more than 0.1% and 0.01, or a skill's median or best 10% differs by more than 0.25 plus that. (Row files round each hit to 3 decimals and each time to a millisecond; the statistics do not.)
- [ ] **Step 1: Extend `selftest()`** with a hand-written statistics file of known answers (two runs on one day adding together; the name `Smith, "Jo"`; a `D` line whose median and best 10% are known; a test-build line left out; Skyfall kept out of the median) and a folder holding both kinds of file read once. Run `python -I tools/telemetry_report.py --selftest`. Expected: FAILED, naming the new checks.
- [ ] **Step 2: Implement** `read_stats`, the choice in `main`, `--rows`, `--compare`. `render` and `analyze` are not changed.
- [ ] **Step 3: Verify.** `--selftest` → `TELEMETRY REPORT SELFTEST PASS`. On Task 2's one-day sample folder: `python -I tools/telemetry_report.py <dir> --compare` → exit 0; `--rows --no-open --out a.html` and `--no-open --out b.html` both write a page. On the 30-day stress folder: `--compare` → exit 0, and report the time the statistics page takes.
- [ ] **Step 4: Update the `CLAUDE.md` copy** and commit `tools/telemetry_report.py`.

## Report back

Worktree path and branch; the commits; the test counts before and after; the `TELEMETRY STATS:` and `TELEMETRY COST:` lines; the 30-day statistics size and timings; what `--compare` printed on both folders; anything you could not verify (nothing here runs in the game: say so); any bug or risk you found in the existing logging.
