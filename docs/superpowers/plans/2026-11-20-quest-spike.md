# Quest Test (Spike) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove, on the owner's headset and server, each thing the Quest port of Blue's Archery Overhaul depends on, before any of the port is written.

**Architecture:** A throwaway Quest mod (`Spike.Quest`, net8.0, IL2CPP) with six probes, each of which logs one `PASS` or `FAIL` line. It is built against the patched APK's own assemblies, copied to the headset over USB, and run against the owner's server, which is first shown to accept an unmodded Quest player at all. What the probes teach is written down for the port's plan; the spike's code is not kept.

**Tech Stack:** C# net8.0, MelonLoader 0.6.5 for Android (LemonLoader), Il2CppInterop, HarmonyX, Android platform-tools (`adb`), the mod's Unity-free `JoinRules.cs` and `Build.cs` compiled as they are.

**Spec:** `docs/superpowers/specs/2026-11-20-quest-port-join-gate-design.md`, section "Piece 2: the Quest test". Piece 1 (the join gate) is done; Piece 3 (the port) gets its own plan from this plan's report.

## Global Constraints

- This is a spike: no unit tests, no review per task. The probes are the tests. Its code is throwaway and never merged to `main`.
- Work in a git worktree on branch `quest-spike` (other sessions share the main checkout and commit to whatever is checked out there). No Claude attribution lines in commits.
- The APK's assemblies are private build inputs, like the ripped Unity project: they live in `C:\ATTUnity\QuestRefs`, never in the repo, never shared.
- On the headset, touch only `Android/data/com.Alta.ATownshipTale/files/MelonLoader/com.Alta.ATownshipTale/Mods/` (one file, `Spike.Quest.dll`) and read logs. Never repatch the APK, never clear the game's data (that wipes the saved login).
- Nothing the PC game or the server plays may change. The only change outside the spike's own folder is the condition in `Directory.Build.props` (Task 3), which leaves every existing project's build exactly as it is.
- This session does the Quest port only (the owner's split of 2026-11-20). The PC mod, the join gate, staging, installing, the install scripts, `CLAUDE.md`, the README and pushing belong to the session "Private GitHub page and mod documentation": never stage, install or push from here; a change the port needs in those is asked of that session. Tell that session before changing the server's `Mods` or settings (Tasks 2 and 4), and check `tools/.install/waiting.pid`: do not move files in a `Mods` folder while an install is waiting. Task 5's `CLAUDE.md` section is sent to that session to add.
- Downloads need the owner's yes at the time: Android platform-tools (Task 1). Nothing else is downloaded.
- Back up before changing the server: its `UserData`, its `Mods`, and its world save folder (Task 2 says where).
- The server's `[Join] Check` is the owner's setting: every step that changes it says so and puts it back.
- A probe that fails is a finding, not a blocker: at most three attempts to fix it (superpowers:systematic-debugging), then record what was learned and go on.
- Play steps given to the owner are in game terms (what to do, what should be seen), never log lines.

## Review Focus

A spike has no reviewer; these are what its report must not leave unanswered, most likely first:

1. **The headset cannot reach the server** (wrong world, wrong network, login). Expected: Task 2 proves an unmodded Quest join before any mod is involved, so a later failure is the mod's.
2. **The server's world changes when it is switched to the Quest world.** Expected: backed up first; the report says what happened to the existing save and to the archery shrine.
3. **A Harmony patch silently does nothing on IL2CPP** (inlined method). Expected: probe 1 logs from inside the patch, so "patched" is seen, not assumed.
4. **A shader or game asset the looks need is missing on Quest.** Expected: probe 4 and probe 6 name each one found or missing.
5. **The spike's wire number does not match the server's.** Expected: the spike compiles the repo's own `JoinRules.cs` at the commit the installed server DLL was built from; Task 3 checks the two numbers before building.

---

### Task 1: Reach the headset from this PC

**Files:**
- Create: `C:\ATTUnity\QuestRefs\Il2CppAssemblies\*.dll`, `C:\ATTUnity\QuestRefs\net8\*.dll` (copied from the headset)
- Create: `C:\ATTUnity\QuestRefs\NOTES.md` (facts found; private)

**Interfaces:**
- Produces: `adb` on the PATH of the session; `C:\ATTUnity\QuestRefs` (the build's reference folder, used as `-p:QuestReferences=C:\ATTUnity\QuestRefs`); `NOTES.md` with the headset paths, loader version and the list of mods the patched APK ships.

- [ ] **Step 1: Get `adb`.** No `adb` and no SideQuest are on this PC. Ask the owner for a yes to download Google's Android SDK Platform-Tools for Windows (`https://dl.google.com/android/repository/platform-tools-latest-windows.zip`, about 7 MB) into `C:\ATTUnity\_downloads\platform-tools`, unzipped in its own empty folder. If the owner says no: they copy files with Windows Explorer (the headset shows as a drive) and every `adb` step below becomes a hand copy the owner does; say so and continue.

- [ ] **Step 2: Connect.** Owner: plug the headset into the PC by USB, put it on, and choose **Allow** on the "Allow USB debugging?" prompt (tick "Always allow from this computer").

Run: `adb devices`
Expected: one line ending in `device` (not `unauthorized`, not empty).

- [ ] **Step 3: Find the game and its loader files.**

```bash
adb shell pm list packages | grep -i alta                      # expect: package:com.Alta.ATownshipTale
adb shell dumpsys package com.Alta.ATownshipTale | grep -E "versionName|versionCode"
adb shell ls -R /sdcard/Android/data/com.Alta.ATownshipTale/files/MelonLoader | head -80
```

Expected: a `Mods` folder, a `UserData` folder, a `Latest.log` (or `Logs/`), and an `Il2CppAssemblies` folder. Record the exact paths in `NOTES.md`. If `Il2CppAssemblies` is not under `files/MelonLoader`, pull the APK instead (`adb shell pm path com.Alta.ATownshipTale`, `adb pull <that path> C:\ATTUnity\QuestRefs\base.apk`) and unzip `assets/MelonLoader/Il2CppAssemblies` and `assets/MelonLoader/net8` from it.

- [ ] **Step 4: Copy the build references and the last log.**

```bash
adb pull /sdcard/Android/data/com.Alta.ATownshipTale/files/MelonLoader/com.Alta.ATownshipTale/Il2CppAssemblies C:/ATTUnity/QuestRefs/Il2CppAssemblies
adb pull /sdcard/Android/data/com.Alta.ATownshipTale/files/MelonLoader/net8 C:/ATTUnity/QuestRefs/net8
adb pull /sdcard/Android/data/com.Alta.ATownshipTale/files/MelonLoader/com.Alta.ATownshipTale/Latest.log C:/ATTUnity/QuestRefs/first-Latest.log
```

(Use the paths Step 3 actually found.) Expected: `Il2CppAssemblies` holds `Assembly-CSharp.dll` or `Il2CppRoot.Township.dll`-style files plus `UnityEngine.CoreModule.dll`; `net8` holds `MelonLoader.dll`, `0Harmony.dll`, `Il2CppInterop.Runtime.dll`.

- [ ] **Step 5: Write `NOTES.md`.** From the log header and the folders: MelonLoader version, game version line, Unity version, "Game Type: Il2cpp", the device, every mod and plugin the log lists as loaded (name, version), the real names of the game assemblies and of the namespace that holds `BowString` (`grep` the reference DLLs' names; `ilspycmd -l c <dll>` lists types), and whether a TavernLib for Quest is among them. These decide the `using` lines of Task 3.

---

### Task 2: A server a Quest player can join, shown without any mod of ours

**Files:**
- Create: `backups/quest-spike-<date>/` (the server's `UserData`, `Mods` and world save, copied)
- Modify (owner, in the launcher): the server's Quest world switch (`quest_scene` in `%APPDATA%\TheModdingTavern\server_settings.json`)
- Modify: the server's `UserData\BluesArcheryOverhaul.cfg`, `[Join] Check` (set to `log` for this task; put back at the end of Task 4)

**Interfaces:**
- Consumes: Task 1's `adb` (to read the headset's log).
- Produces: the server running the Quest world with the join gate in `log`; a line in `NOTES.md` on what the Quest world is and what became of the old save.

Found on 2026-11-20 (TavernLib 1.8.5, decompiled): a server serves Quest players only when it runs the game's Quest world (`quest_scene` true, or `/questScene` on the command line: scene index 4, listed as "running for Quest"). The owner's server has `quest_scene = False`. PC players can join a Quest-world server.

- [ ] **Step 1: Back up** (game and server closed): copy `<server>\UserData`, `<server>\Mods` and the server's save folder (`%APPDATA%\A Township Tale\Servers\-1`) into `backups/quest-spike-<date>/`. Check the copy's file count matches.

- [ ] **Step 2: Cut the server down to what a Quest game can match** (the owner's decision of 2026-11-20). Move `RevampedSkills.dll` out of `<server>\Mods` and out of `<client>\Mods` into the backup folder (clients must run the server's mod set). `BluesInGameStringTool.dll` is another of the owner's mods: ask the owner whether it stays for this test; if unsure, move it out on both sides too. `BluesArcheryOverhaul.dll` and `BluesArcheryTestKit.dll` stay. `attbullshit2.dll` is already off the server (since 16:51 on 2026-11-20) and still in the client's `Mods`: leave it unless a join fails because of it. `SyncLib.dll` is already off both.

- [ ] **Step 3: Switch the server to the Quest world.** Owner: in the TavernLauncher Server window, turn on the Quest world option (the setting saved as `quest_scene`), keep the port at 1757, start the server. Executor: `python tools/set-cfg.py "<server>/UserData/BluesArcheryOverhaul.cfg" Join Check '"log"'` and confirm the cfg still parses.

Expected in the server's `MelonLoader\Latest.log`: the server finishes booting; `[Archery] Self-check:` is not `FAILED`; `[Archery] Join: check installed on message 27 (log)`. Record in `NOTES.md` any `[Archery]` error or warning that was not there on the PC world (the tree, the shrine, and anything that names a missing prefab or scene object).

- [ ] **Step 4: A PC join on the Quest world.** Owner: start the PC game and join. Expected in game: you are in a world; say whether it is your old world or a fresh one, and whether the archery shrine is where it was and opens with the mod's orbs. Record both answers.

- [ ] **Step 5: The headset joins, with no mod of ours.** Owner: on the headset, start the game, find the server on the server board (it should be marked as a Quest server; "Blue_XD Test Server") and join; if it is not listed, use the main menu's private-server join with this PC's local address and port 1757. Expected in game: you are in the world, and the PC player (if still in) can be seen.

Executor reads the server log. Expected: `[Archery] Join: <name> would be refused (log only): This server needs the mod Blue's Archery Overhaul (X). Install it, then join again. (no answer in 5 s)`.

- [ ] **Step 6: If the headset cannot join**, this is the spike's first finding. Pull the headset's log (`adb pull .../Latest.log`), read why (the Quest build says why a server turned it away), and stop here with the reason for the owner: the port cannot be tested until this works.

---

### Task 3: Build the spike

**Files:**
- Modify: `Directory.Build.props` (wrap the net472 settings and PC references so they do not apply to a project whose name ends in `.Quest`)
- Create: `Spike.Quest/Spike.Quest.csproj`
- Create: `Spike.Quest/Spike.cs`

**Interfaces:**
- Consumes: `C:\ATTUnity\QuestRefs` and `NOTES.md` (Task 1); `BluesArcheryOverhaul/JoinRules.cs` and `BluesArcheryOverhaul/Build.cs` (compiled by link, unchanged).
- Produces: `Spike.Quest/bin/Release/net8.0/Spike.Quest.dll`.

- [ ] **Step 1: Worktree.** Create a worktree on a new branch `quest-spike` from `main` (superpowers:using-git-worktrees). Check that `JoinWire.Version` in that tree equals the wire number the running server logs on a PC join (`... wire N ...`); if `main` has moved on, branch from the commit the installed server DLL was staged from.

- [ ] **Step 2: `Directory.Build.props`.** Put `Condition="!$(MSBuildProjectName.EndsWith('.Quest'))"` on the property group's `TargetFramework`, on the item group of game and loader references, and on the two deploy targets, so a `.Quest` project gets none of them. (The same rule serves `BluesArcheryOverhaul.Quest` later.)

Run: `dotnet build BluesArcheryOverhaul -c Release` and `dotnet run --project BluesArcheryOverhaul.Tests -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`; `ALL PASS`: the PC build is unchanged.

- [ ] **Step 3: `Spike.Quest.csproj`.**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Spike.Quest</AssemblyName>
    <RootNamespace>BluesArcheryOverhaul</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DebugType>none</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Spike.cs" />
    <Compile Include="..\BluesArcheryOverhaul\JoinRules.cs" />
    <Compile Include="..\BluesArcheryOverhaul\Build.cs" />
    <Reference Include="$(QuestReferences)\Il2CppAssemblies\*.dll;$(QuestReferences)\net8\*.dll" Private="false" />
  </ItemGroup>
  <Target Name="CheckQuestReferences" BeforeTargets="ResolveReferences" Condition="!Exists('$(QuestReferences)\net8\MelonLoader.dll')">
    <Error Text="Pass -p:QuestReferences=C:\ATTUnity\QuestRefs (the patched APK's Il2CppAssemblies and net8 folders)." />
  </Target>
</Project>
```

If `Build.cs` needs another Unity-free file to compile, link that file too; never copy or edit a rule file for the spike.

- [ ] **Step 4: `Spike.cs`**: one `MelonMod` (`[assembly: MelonInfo(typeof(BluesArcheryOverhaul.Spike), "QuestSpike", "0.0.1", "Blue")]`, `[assembly: MelonGame("Alta", "Att_Quest")]`) and six probes. Every probe is in its own try/catch and logs exactly one line when it first succeeds or fails, `[QuestSpike] PROBE <n> <name>: PASS <detail>` or `... FAIL <exception type and message>`; `OnInitializeMelon` logs `[QuestSpike] started` and, 60 s after the first scene, `[QuestSpike] SUMMARY` listing every probe not yet reported as `NOT RUN`.

| n | name | what it does | PASS detail |
|---|---|---|---|
| 1 | `bow patch` | Harmony postfix on `BowString.HeldUpdate` (the patch the PC mod's ring hangs on); from inside it, once a second while a string is held, reads the string's pull the way `Main.Draw` does | the pull value, and "called N times" |
| 2 | `join answer` | postfix on `JoinServerPipeline.ConnectionApprovedByServer` sets a handler on message `JoinWire.Message`; on the server's Here (`JoinWire.ReadHere`) it sends Hello (`JoinWire.WriteHello`: `JoinWire.Version`, the version text of the server's build, `Check` 0, `JoinPlatform.Quest`). The stream is read and written through a small `IBitReader`/`IBitWriter` adapter over the Il2Cpp `Stream.SerializeBits`, as `StreamBits.cs` does on PC | "Here from server X wire N; Hello sent" |
| 3 | `private field and event` | in probe 1's postfix: reads the private field the PC mod reads there (`interactorHoldingBowString`) through the interop's generated member, and subscribes once to `BowString.OnUpdatedStringPull` with a managed delegate | the field's value is non-null while held; "event fired N times" |
| 4 | `own component and line` | registers a `MonoBehaviour` of the spike's own with Il2CppInterop (`ClassInjector.RegisterTypeInIl2Cpp`), adds it to a new `GameObject` parented to the held bow string, with a `LineRenderer` drawing a 5 cm green ring; looks up `Shader.Find("Sprites/Default")`, then `"Hidden/Internal-Colored"`, as `Fx.cs` does | which shader was found, and "Update ran N times" |
| 5 | `settings file` | a MelonPreferences category with its own file `UserData/QuestSpike.cfg`: creates an entry, saves, reloads, reads it back | the full path of the file and the value read back |
| 6 | `game assets` | after the first scene: `Application.unityVersion`, the game's build version, `SystemInfo.deviceModel`; counts `Resources.FindObjectsOfTypeAll<PooledObjectDefinition>()`; says found or missing for each name the PC looks use most: `Explosion`, `Combat Hit`, `Large Smoke Puff`, `Charged Axe Shatter Effect`, the plain Arrow prefab by hash 56460 (`PrefabManager.GetPrefab`), and the textures `Glyph_Bow_LightBeam`, `SpriteSheet_Combat` | one line with every name and found/missing |

The namespaces and member names come from `NOTES.md` and the reference DLLs (the PC names are in `_decomp`; on Quest the same types sit under `Il2Cpp...` namespaces). Where the PC mod passes a managed delegate to the game (`SetHandler`, `Send`, an event), use the interop's delegate conversion; record in the report which form compiled and worked.

- [ ] **Step 5: Build.**

Run: `dotnet build Spike.Quest -c Release -p:QuestReferences=C:\ATTUnity\QuestRefs`
Expected: `Build succeeded`, `0 Error(s)`, and `Spike.Quest/bin/Release/net8.0/Spike.Quest.dll`. Record in the report every compile error met and what fixed it: each is a rule for the port.

- [ ] **Step 6: Commit on `quest-spike`.**

```bash
git add Directory.Build.props Spike.Quest/Spike.Quest.csproj Spike.Quest/Spike.cs
git commit -m "Quest spike: six probes (throwaway)"
```

---

### Task 4: Run it on the headset

**Files:**
- Create: `docs/superpowers/specs/2026-11-20-quest-spike-run.log` (the headset's log of the run; trimmed to the `[QuestSpike]` lines and any exception)

**Interfaces:**
- Consumes: Task 3's DLL; Task 2's server (Quest world, gate in `log`).
- Produces: one PASS/FAIL per probe, seen on the headset.

- [ ] **Step 1: Install the spike** (game closed on the headset):

```bash
adb push Spike.Quest/bin/Release/net8.0/Spike.Quest.dll /sdcard/Android/data/com.Alta.ATownshipTale/files/MelonLoader/com.Alta.ATownshipTale/Mods/Spike.Quest.dll
adb shell am force-stop com.Alta.ATownshipTale
```

- [ ] **Step 2: First run, gate in `log`.** Owner: start the game on the headset, join the server, take a bow and an arrow, nock and hold the string at full draw for about five seconds, let go, do it twice more, then stay in for a minute. Expected in game: you join as before; while you hold the string a small green ring sits on it; nothing else is different.

```bash
adb pull /sdcard/Android/data/com.Alta.ATownshipTale/files/MelonLoader/com.Alta.ATownshipTale/Latest.log C:/ATTUnity/QuestRefs/run1-Latest.log
grep -a "QuestSpike" C:/ATTUnity/QuestRefs/run1-Latest.log
```

Expected: `[QuestSpike] started`, six `PROBE` lines, `SUMMARY` with nothing `NOT RUN`. Server log expected: `[Archery] Join: <name> passed (client X, wire N, Quest, settings DIFFERENT, answered in N ms)` (the settings differ because the spike sends no checksum; that is expected).

- [ ] **Step 3: Work each FAIL** (at most three attempts each: fix, rebuild, push, rerun the part that failed). A probe still failing after three is written up with what was tried.

- [ ] **Step 4: The gate on `refuse`.** Executor: set the server's `[Join] Check` to `"refuse"` with `tools/set-cfg.py`. Owner: leave and join again with the spike installed. Expected in game: you get in. Then executor removes the spike (`adb shell rm .../Mods/Spike.Quest.dll`, force-stop), owner joins once more. Expected in game: turned away, and the message names "Blue's Archery Overhaul"; say exactly what the headset shows (the Quest build says why a server turned you away, and its wording may differ from the PC's "Unknown connection error 60").

- [ ] **Step 5: Leave things as they were.** The spike DLL is off the headset. Ask the owner how to leave the server: Quest world or back to the PC world (`quest_scene`), `[Join] Check` (`refuse` or `log`), and whether `RevampedSkills.dll` goes back for now (the backup folder has it). Do what they say and confirm it from the server's log.

---

### Task 5: Write down what was learned

**Files:**
- Create: `docs/superpowers/specs/2026-11-20-quest-spike-report.md`
- Modify: `CLAUDE.md` (a short "Quest" section)
- Modify: `docs/superpowers/specs/2026-11-20-quest-port-join-gate-design.md` (Piece 3: correct anything the spike disproved)

- [ ] **Step 1: The report.** Sections, each a few lines of fact:
  - *Verdict*: can the port go ahead; what, if anything, blocks it.
  - *The six probes*: PASS/FAIL, what it took.
  - *Building for Quest*: the reference folders, the csproj, every compile error and its fix, the delegate, private-member and component-registration forms that worked (with a line of code each), assembly and namespace names.
  - *The server*: what the Quest world is (the old save, the shrine, anything the mod logged that it does not log on the PC world), how the headset finds the server, what a refused Quest player sees.
  - *The loop*: how long one build-push-run-read round takes, and how logs are read.
  - *Assets*: probe 6's found/missing list, and what that means for which looks.
  - *For the port's plan*: the things the port must do differently from what the design assumed.
- [ ] **Step 2: CLAUDE.md**: where the Quest references are and that they are private, the `adb` commands that install a DLL and pull the log, the `.Quest` project-name rule in `Directory.Build.props`, that a Quest server runs the Quest world, and the report's path.
- [ ] **Step 3: The design.** Edit Piece 3 where the spike showed otherwise; add "Decided/learned 2026-11-2x" notes rather than rewriting history.
- [ ] **Step 4: Commit the report, the run log and the design change to `main`** (docs only; `CLAUDE.md` is not in git). The `quest-spike` branch and worktree stay unmerged; say to the owner that they can be deleted once the port's plan is written.
- [ ] **Step 5: Tell the owner the verdict** in a few lines, and what the port's plan will be built on.
