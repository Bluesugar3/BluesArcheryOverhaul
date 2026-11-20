# Skyfall plan map: tools-and-docs (2026-11-19, read-only)

## Map
Read-only. Nothing was edited, built, run or installed. Line numbers were read on 2026-11-19. Root is C:\Users\dmand\OneDrive\Desktop\attmod. CLAUDE.md uses LF line endings.

=== (1) tools/install-when-closed.sh (82 lines, bash, `set -u`, no `set -e`) ===
How it is called: `bash tools/install-when-closed.sh [--rollback]`. MODE="${1:-install}" (L24).
- Only "--rollback" is recognised (L34, L65).
- There is NO dry-run or check flag. Any other first argument (for example `--dry-run` or `--check`) falls into the install branch and does a REAL install. Nothing rejects unknown arguments.

The only test hook today is the environment overrides (L10-14), used for the original "Dry-run on scratch folders" (.superpowers/sdd/2026-11-18-playtest8/progress.md:70). Caveats:
- L9 always cd's into the real repo.
- running() (L17) always checks the REAL game and server processes, so a scratch run still waits for both to be closed (3 checks, about 8-12 s).

Excerpt L10-18:
```
10 # QR_CLIENT / QR_SERVER / QR_STATE / QR_BACKUPS exist only so the script can be tried on scratch folders.
11 C="${QR_CLIENT:-/c/Users/dmand/OneDrive/Desktop/A Township Tale (client}"
12 S="${QR_SERVER:-/c/Users/dmand/OneDrive/Desktop/A Township Tale(server}"
13 BK="${QR_BACKUPS:-backups}"
14 I="${QR_STATE:-tools/.install}"; mkdir -p "$I"; LOG="$I/install.log"; ST="$S/QuickshotRing-install-status.txt"
15 TARGETS=("$C/Mods/QuickshotRing.dll" "$C/Modpacks/My mods/QuickshotRing.dll" "$S/Mods/QuickshotRing.dll")
16 say() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*" | tee -a "$LOG"; }
17 running() { tasklist 2>/dev/null | grep -i -q "township"; }
18 md5() { md5sum "$1" 2>/dev/null | cut -c1-32; }
```

Structure:
- L20-22: one waiting installer at a time. If $I/waiting.pid is alive it echoes "an installer is already waiting; it will install the latest staged build" (not logged) and exits 0. Otherwise it writes its pid, with trap rm on EXIT.
- L25: if running, it logs `say "WAITING: the game or the server is running; it installs once both are closed"`.
- L26-32: the wait loop. It goes on once both are closed on 3 checks in a row, with `sleep 4` between checks. After 86400 s it logs `say "gave up after 24 hours: still running"` and exits 2.
- L34-44: the mode branch.
  - --rollback: SRC = the newest `$BK/auto-*` (`ls | sort | tail -1`), and it needs `$SRC/client/QuickshotRing.dll`. Otherwise "ROLLBACK FAILED: no backup in $BK/auto-*", exit 4.
  - install: reads $I/staged/manifest.txt (version, md5, friends, changed, test with `sed -n 's/^key=//p'`). Refusals: "NOTHING STAGED: run tools/stage-build.sh first", exit 4 (L40); "INSTALL REFUSED: the staged file does not match its manifest (staging in progress?)", exit 4 (L43).
- L46-47 is the ONLY target check: an md5 compare of the three TARGETS.

Excerpt L44-47:
```
44 fi
45
46 same=1; for t in "${TARGETS[@]}"; do [ "$(md5 "$t")" = "$WANT" ] || same=0; done
47 if [ $same = 1 ]; then say "ALREADY INSTALLED: $VER (${WANT:0:12})"; exit 0; fi
```

A missing target gives an empty md5, so `same` becomes 0 and the script goes on. Nothing anywhere checks that a target's folder exists.

Backup, abort re-check, copy order, verify. Excerpt L49-63:
```
49 # back up what is installed now
50 WAS=$(sed -n 's/^installed=\([^ ]*\).*/\1/p' "$ST" 2>/dev/null | head -1); [ -n "$WAS" ] || WAS=unknown
51 B="$BK/auto-$(date +%Y-%m-%d-%H%M%S)-v$WAS"; mkdir -p "$B/client" "$B/pack" "$B/server"
52 cp "${TARGETS[0]}" "$B/client/" 2>>"$LOG"; cp "${TARGETS[1]}" "$B/pack/" 2>>"$LOG"; cp "${TARGETS[2]}" "$B/server/" 2>>"$LOG"
53 cp "$C/UserData/QuickshotRing.cfg" "$B/client/" 2>>"$LOG"; cp "$S/UserData/QuickshotRing.cfg" "$B/server/" 2>>"$LOG"
54 cp "$S/TestBuildMenu.ps1" "$B/server/" 2>/dev/null
55 say "backed up $WAS to $B"
56
57 if running; then say "INSTALL ABORTED: the game or the server started again; nothing was copied. Start the installer again."; exit 5; fi
58 ok=1
59 for t in "${TARGETS[@]}"; do
60   cp -f "$FILE" "$t" 2>>"$LOG"
61   [ "$(md5 "$t")" = "$WANT" ] && say "ok   $t" || { say "FAIL $t"; ok=0; }
62 done
63 if [ $ok != 1 ]; then say "INSTALL INCOMPLETE: a copy differs (file locked?). Backup of the previous files: $B"; exit 3; fi
```

- Copy order is the array order: client Mods, then client "Modpacks/My mods", then server Mods.
- A failed copy does NOT stop the loop and does NOT undo the targets already copied. The previous files are only in $B, and nothing restores them automatically.

The rest:
- L65-69 (install mode only): the test menu files "TestBuildMenu.ps1" and "Test Build Menu.cmd" are copied into "$S/" when their md5 differs (`say "test menu updated: $f"`). A staged post-install.sh is run, then renamed post-install.done.
- L70 / L72: `echo "installed=$VER at $(date '+%Y-%m-%d %H:%M')" > "$ST"`.
- L75-76: keeps the newest 5 `$BK/auto-*` (`say "old backup removed: $old"`).
- L78-82: `say "INSTALLED $VER (${WANT:0:12}) on the game, the launcher pack and the server"`, then "changed: …", "test: …", "FRIENDS NEED THIS FILE TOO: $C/Mods/QuickshotRing.dll" (when friends=yes), then exit 0.

Exit codes:
- 0: installed, already installed, or another installer is waiting.
- 2: gave up after 24 h.
- 3: INSTALL INCOMPLETE.
- 4: NOTHING STAGED, INSTALL REFUSED or ROLLBACK FAILED.
- 5: INSTALL ABORTED.
- 1: only if the cd fails.

State checked on 2026-11-19:
- `<client>\Mods` EXISTS but holds another pack: 57 files, including RevampedSkills.dll, SyncLib.dll and TavernVoice.dll. There is NO QuickshotRing.dll in it.
- `<client>\Modpacks` does NOT exist at all, so "My mods" is missing too.
- `<server>\Mods\QuickshotRing.dll` has md5 bf4e9f4a231a (0.17.15).
- The status file reads "installed=0.17.15 at 2026-11-18 21:54".
- There is no tools/.install/waiting.pid, so no installer is waiting.
- tools/.install/staged holds 0.17.15 (already installed): manifest tests=ALL PASS (2042), friends=yes.
- The newest backup is backups/auto-2026-11-18-215443-v0.17.13, with client/pack/server DLLs and cfgs.

What today's script would do in this state:
1. L46: `same` is 0.
2. L51: it makes `backups/auto-<now>-v0.17.15`. The client and pack copies at L52 fail into the log.
3. L59-62: it copies into the client's current (RevampedSkills) pack Mods (ok), FAILs on My mods (no folder), and copies to the server (ok).
4. L63: INSTALL INCOMPLETE, exit 3. That is a half install.
5. The new newest backup has no client/QuickshotRing.dll, so a later --rollback fails at L36.

--rollback today would also copy 0.17.13 into the client's current pack and FAIL on My mods.

Where the preflight refusal goes:
- Between L44 (the `fi` that ends the mode branch) and L46 (`same=1…`).
- That point is after the wait, so it checks the folders as they are at copy time. It covers install and --rollback alike. It comes before L51's mkdir and before every cp (L52-54 backups, L59-62 installs).
- A refusal after L51 would leave an empty newest auto-* folder, which breaks --rollback (L35-36).
- Optionally, the same check can also go between L24 and L25 so the installer refuses at once. Otherwise a background installer waits up to 24 h holding waiting.pid, and any second installer exits immediately.

Suggested shape in the script's own style (new code, not existing):
```
for t in "${TARGETS[@]}"; do d=$(dirname "$t"); [ -d "$d" ] || { say "INSTALL REFUSED: $d is missing; nothing was copied"; exit 4; }; done
```

Notes on the check:
- Exit 4 is what the existing refusals use. 6 is unused.
- A folder check alone passes `<client>\Mods` today, because it exists but holds the other pack. Only the "Modpacks\My mods" check stops the install today.

=== (2) tools/stage-build.sh (39 lines) ===
Call: `bash tools/stage-build.sh <version> <friends: yes|no> "<what changed>" "<what to test>"` (L6). All four are required through `${N:?}` (L10).

What it does:
- L13: `case "$VER" in [0-9]*.[0-9]*.[0-9]*) ;; *) echo "STAGE FAILED: '$VER' is not a version"; exit 2;; esac`
- L16-17 set the version (see 5). A failure exits 2.
- L20-21: `T=$(dotnet run --project QuickshotRing.Tests -c Release 2>&1 | tail -1)`. It must start with "ALL PASS"; otherwise "STAGE FAILED: tests: $T", exit 3.
- L22-23: `dotnet build QuickshotRing -c Release --no-incremental`. The output must contain " 0 Warning(s)" and " 0 Error(s)"; otherwise it prints the first 5 warning or error lines and exits 3.
- L24-25: QuickshotRing/bin/Release/QuickshotRing.dll must exist, or exit 3.
- L28-33: staged.new gets the DLL, tools/TestBuildMenu.ps1 and "tools/Test Build Menu.cmd" (exit 4 on copy failure). An optional tools/.install/post-install.sh is moved in. manifest.txt gets version=, md5=, friends=, changed=, test=, tests=$T and staged=. Then staged.new is swapped in for staged.
- L36-38: rewrites `<server>\QuickshotRing-install-status.txt` to "installed=…" plus "waiting=$VER".
- L39: `echo "STAGED $VER ($T, 0 warnings) md5 ${MD5:0:12}"`.

Facts that matter for Skyfall:
- It never deploys and never starts the installer. But any installer already waiting installs whatever is staged.
- L16 edits Main.cs BEFORE the gate, and the edit is not reverted if the tests or build fail.
- L36-38 make the test menu say "Version X is ready: close the game and the server to install it" (tools/TestBuildMenu.ps1:250-263, text at :259-260), even when no installer is running.
- The manifest is read line by line, so "what changed" and "what to test" must be one line each.
- The build compiles against the client install (Directory.Build.props:5, 20-23, including `$(GameDir)\Mods\SyncLib.dll`). The client's SyncLib.dll md5 is 35a23b4499ba, the same as the server's, and MelonLoader\net35 and Managed\Root.Township.dll exist. So compiling still works with the other pack in client Mods.

Directory.Build.props:29-37:
- `-p:Deploy=true` copies to `$(GameDir)\Mods` UNCONDITIONALLY (L31). Today that drops the DLL into the RevampedSkills pack.
- The pack copy (L33) runs only if "Modpacks\My mods\QuickshotRing.dll" exists.
- `-p:DeployServer=true` copies to the server's Mods (L36).

Plan precedent:
- "Never pass `-p:Deploy`; the controller installs." (plans/2026-11-15-archery-main-path-part3-4-shrine.md:24; part2-looks.md:24)
- "Never pass `-p:Deploy=true` or `-p:DeployServer=true`." (.superpowers/sdd/2026-11-18-homing/H5b-brief.md:10)

=== (3) "commit" in this non-git workspace ===
Plan wording:
- plans/2026-11-13-archery-main-path-part1.md:23: "- Not a git repo: \"commit\" in this plan means take the task's file snapshot (`.superpowers/sdd/2026-11-13-archery-main-path-part1/snap.sh N`, as earlier plans did)."
- The step format there (L98): "- [ ] **Step 5: Snapshot** (`snap.sh 1`)." It is numbered per task, up to `snap.sh 8` at L336.
- part2-looks.md:25 and part3-4-shrine.md:25: "- Not a git repo: \"commit\" means the task's file snapshot, taken by the controller."
- The earliest plan, 2026-11-11-archery-tree.md:25: "Where the skill says \"commit\", run the build and tests and stop there; do not `git init` unless the user asks."

snap.sh: all 11 copies under .superpowers/sdd/*/snap.sh are byte-identical except the W path (checked). Excerpt from 2026-11-18-homing/snap.sh:
```
1 #!/usr/bin/env bash
2 # snap.sh N : copy the source tree as it is now into snap-N (the BASE for task N's diff)
3 set -e; cd /c/Users/dmand/OneDrive/Desktop/attmod; W=.superpowers/sdd/2026-11-18-homing; D=$W/snap-$1; rm -rf $D; mkdir -p $D
4 for d in QuickshotRing QuickshotRing.Tests; do mkdir -p $D/$d; (cd $d && find . -type f -not -path './bin/*' -not -path './obj/*' -not -path './Art/*' -not -path './Resources/*' -exec cp --parents {} ../$D/$d/ \;); done
5 cp CLAUDE.md $D/CLAUDE.md; echo "snapshot $D"
```

pkg.sh (identical everywhere except W): `pkg.sh BASE_SNAP OUTNAME [SNAP]`. It writes "# Review package", a Stat and a "Diff (-U10)" via `git diff --no-index --ignore-cr-at-eol` from $W/$1 to the live tree (copied to $W/tmpcur) or to $W/$3.

How the controller used them in practice:
- homing/progress.md:6: "snap-0 = the installed 0.16.1 source".
- progress.md:8: "snap-0 -> snap-1, H1-package.md, H1-report.md. Staged 0.17.0 by the controller…"
- playtest8/progress.md:68: "G34-package.md (snap-2 -> snap-4)".
- Each sdd folder holds <X>-brief.md, <X>-report.md, <X>-package.md, <X>-review.md and progress.md.
- Work from 2026-11-16 on used briefs in .superpowers/sdd/<date-topic>/ rather than plan docs. The newest file in docs/superpowers/plans is from 2026-11-15.
- No 2026-11-19 sdd folder exists yet.

Coverage gap: snap.sh and pkg.sh capture only QuickshotRing/ and QuickshotRing.Tests/ (minus bin, obj, Art, Resources) plus CLAUDE.md. They do NOT capture:
- tools/ (install-when-closed.sh, stage-build.sh). Note that tools/.install/ holds a 1.5 MB staged DLL and install.log.
- docs/
- Directory.Build.props
- QuickshotRing/Resources (embedded PNGs)

Live source vs .superpowers/sdd/2026-11-18-homing/snap-13 (the 0.17.15 source), compared with CRs ignored: QuickshotRing/ and QuickshotRing.Tests/ are identical. Only CLAUDE.md changed (L109, the RevampedSkills note, was added). There is no Skyfall code anywhere in QuickshotRing/*.cs, the tests or tools/.

=== (4) CLAUDE.md, the places a Skyfall build updates ===
- L117, in the commands block:
  `dotnet run --project QuickshotRing.Tests -c Release                         # unit tests for the thirteen Unity-free files -> "ALL PASS (2042)"`
  This is the ONLY line in CLAUDE.md with the count. L25 says "ALL PASS" with no number.
- L125, the tail of the Rules.cs row: "The Unity-free files the tests compile are `Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`, `Charge.cs`, `Path.cs`, `Preview.cs`, `Lectern.cs`, `Timing.cs`, `Homing.cs`, `Range.cs` and `RangeBuild.cs`; keep Unity types out of all thirteen."
- File table: header at L123 `| File | What it owns |` and L124 `|---|---|`. Rows run L125-177 (the last is SelfCheck.cs) and L178 is blank. Newer features were appended near the end: L171 Timing, L172 Homing, L173-176 Seek/SeekLook/SeekCage/SeekFx, L177 SelfCheck.
- Row format examples:
  - L139 (tested): "| `Range.cs` | Unity-free (tested, `RangeTests.cs`; the twelfth file the tests compile): the range's rules: `ShelfEntry` (…), … |"
  - L140: "| `RangeBuild.cs` | R2, Unity-free (tested, `RangeBuildTests.cs`; the thirteenth file the tests compile): … |"
  - L151, short: "| `Preview.cs` | Unity-free (tested): which line an orb belongs to (`Lines`), the build after a purchase (`BuildPreview`), orb tints (`TreeTint`), the tablet's wording from the live numbers (`BuildFacts`, `TabletText`). |"
  - L176, client: "| `SeekFx.cs` | H2, clients: `SeekBurst` (…) and `SeekReveal` (…). |"
  - So a new rules row reads "…; the fourteenth file the tests compile)".
- Existing defect: the RangePhoto row is split over L141-142. A real newline stands where `photos\range-<time>-<view>.png` had "\r".
- L192, the bullet that gets the approved exception: "- **No new network traffic.** Everything is driven by messages the game already sends. The Quickdraw 4 line is signalled by sending the normal arrow flight sync with the impossible speed 999 (`Quickdraw4.Marker`); clients treat that as \"line shot\" and never start a flight."
- Lines the Skyfall claim code contradicts (each says the unsteered window and marks are written 0):
  - L164, the Build.cs/Wire.cs row: "…the window and mark count of the orbs deleted in 0.16.0 stay in the layout, written 0 and ignored on read".
  - L186: "The claim's window bits carry the number of locks less one ONLY with the steered bit; an unsteered claim is bit for bit the old layout (tested)."
  - L213: "…the shot claim keeps their window and mark-count bits, written 0 (since H1 the window carries Seeking Volley's lock count with the steered bit)."
  - L167, the Claims.cs row: it relays "the kind, stage and the time it decided with".
- Other related lines:
  - L211: "`Main.OnUpdate` returns early on the headless server … server work … goes before that return".
  - L22 and L30: the standing auto-install rule and "Stage only when no agent is editing the mod". The same rule is in the memory index entry auto-install-after-code-changes.md.
  - L107: the pack copy lives in `<client>\Modpacks\My mods\`.
  - L114: the Design/Plans list.
  - In code, Main.cs:13-42 is a header-comment file index. New features were added there (Seek*.cs at :32-33), though not every file is listed.
- The test mechanism behind "the files the tests compile":
  - QuickshotRing.Tests/QuickshotRing.Tests.csproj L7-19 lists 13 `<Compile Include="..\QuickshotRing\X.cs" />`. A new one goes after L19 (`RangeBuild.cs`), before `</ItemGroup>` at L20.
  - Program.cs L62-92 calls `<X>Tests.Run();`. The last is L92 `RangeBuildTests.Run();`. L94 is `Console.WriteLine(failed == 0 ? $"ALL PASS ({total})" : $"FAILED {failed} of {total}");`.
  - total counts `Check(string name, bool ok)` calls (Program.cs:7-12). `internal static bool Near(float a, float b)` is at :14.
  - Test class pattern (RicochetBounceTests.cs:1-13): `using static Program;` then `internal static class XTests { internal static void Run() { … } }`.
- Count: 2042, from CLAUDE.md:117, the staged manifest and homing/progress.md:124. The live tests equal snap-13.

=== (5) Version ===
- QuickshotRing/Main.cs:9: `[assembly: MelonInfo(typeof(QuickshotRing.Main), "QuickshotRing", "0.17.15", "dmand")]`. This is the only version string in the code.
- Main.cs:73 logs it at start: `LoggerInstance.Msg($"[Archery] QuickshotRing v{Info.Version} ({(ApplicationManager.IsHeadless ? "server" : "client")})");`
- stage-build.sh sets it:
  - L16: `sed -i -E "s/(MelonInfo\(typeof\(QuickshotRing\.Main\), \"QuickshotRing\", \")[^\"]+(\")/\1$VER\2/" QuickshotRing/Main.cs`
  - L17: `grep -q "\"QuickshotRing\", \"$VER\"" QuickshotRing/Main.cs || { echo "STAGE FAILED: could not set the version in Main.cs"; exit 2; }`
- Installed history (tools/.install/install.log): 0.16.0, 0.16.1, 0.17.0, 0.17.2, 0.17.3 … 0.17.13, then 0.17.15 at 2026-11-18 21:54.
  - 0.17.1 and 0.17.14 were staged and then replaced before an install. 0.17.14 had 2012 passes and md5 71e44abe3876 (homing/progress.md:123).
- Feature batches started a new minor version: 0.16.0 (G1) and 0.17.0 (H1 homing). The manual backups are named backups/2026-11-1x-pre-0.7.0 … pre-0.15.0.
- CLAUDE.md:30: "Bump the version on every staged build (0.16.1, 0.16.2, ...)". Nothing decides between 0.17.16 and 0.18.0 for Skyfall.
- The design was written against 0.17.15 (spec line 3).

## Gaps
- Nothing was run (no tests, build or installer), as instructed. The 2042 count comes from the staged manifest and progress.md. The live QuickshotRing/ and QuickshotRing.Tests/ sources match snap-13 (the source it was measured on) when CRs are ignored, so it should still hold.
- I did not check whether the owner's "restore my pack" recreates `<client>\Modpacks\My mods`. That folder, and the whole Modpacks folder, is missing today. I also did not check which pack the client launcher has selected; that state lives under %APPDATA%\TheModdingTavern, which was not opened per the security rule.
- I did not check whether the game or server is running right now.
- I did not read the memory file auto-install-after-code-changes.md itself, only its MEMORY.md index line.
- I read feasibility-scout.md and runtime-looks.md only by name. They are not needed for the tools and docs items. The design and the other three research files were read in full.
- The installer's scratch-folder dry run (playtest8/progress.md:70) is recorded only as prose. No test script for install-when-closed.sh exists in the repo, so a new preflight has no harness. The QR_* environment overrides are the only hook, and they still wait on the real game and server processes.
- I could not find any recorded rule for choosing between 0.17.16 and 0.18.0. The only evidence is the precedent of minor bumps for feature batches.
- The CLAUDE.md line numbers are a point-in-time read. CLAUDE.md has already changed once since 0.17.15 (L109 was added), and other agents in this session could edit it again.
