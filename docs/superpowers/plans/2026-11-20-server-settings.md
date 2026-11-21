# Server-Set Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While a game is on a server, every setting of the mod comes from the server; the player's own file is ignored and never overwritten.

**Architecture:** A Unity-free file holds the rules: which entries are server-set, what to send (differences from the built-in defaults), the byte layout, splitting into parts, and what a game must set. The join gate's message 27 gains two kinds, Settings and SettingsAck. A game-facing class sends at the join step and on every server settings reload, and on a client applies the values to the MelonPreferences entries in memory, remembers the player's own, puts the server's back if the file is edited, and restores the player's own on leaving.

**Tech Stack:** C# (net472; Unity 2020.3 Mono at run time, so no newer BCL APIs), MelonLoader 0.7.3 preferences, the game's `Alta.Networking` `Connection.Send` and `Alta.Serialization.Stream` through the existing `StreamBits`, the project's console test runner (`Check(name, bool)`).

**Spec:** `docs/superpowers/specs/2026-11-20-server-settings-design.md`. Read `CLAUDE.md` in the main checkout (git-ignored) for the project rules, the `JoinGate.cs`, `JoinRules.cs` row and the two network-exception bullets, and read `BluesArcheryOverhaul/Cfg.cs`, `JoinGate.cs`, `JoinRules.cs`, `SkyfallLink.cs` (how a client notices it left the server) before writing anything.

## Global Constraints

- Server-set: every entry of the mod's settings file except the sections `Log`, `Timing`, `Telemetry`, `Join`, `Settings`. The play-test kit's file and the icon pictures are not touched.
- Sent: only entries whose server value differs from the built-in default, as `Section.Name` and the value as text (`SettingsChecksum.Format`, the text the shared checksum already uses), plus a 32-bit check over all server-set entries (`SettingsChecksum.Compute` over key and text). The game applies them over the built-in defaults, never over its own file.
- A player's settings file is never written by this feature. The player's own values are kept in memory while the server's are held and are put back on leaving the server and at quit, before MelonLoader's own save at a clean quit.
- Message type 27 only, with two new kinds after `KindHere = 1` and `KindHello = 2`: `KindSettings = 3`, `KindSettingsAck = 4`. Here and Hello keep their exact layouts (an older game must still be told it is too old). Do not change `JoinWire.Version` by hand: staging raises it.
- Nothing is sent with `[Join] Check = off`. A fault anywhere in this feature never refuses or drops a player: only a decided mismatch under `[Join] SettingsMismatch = refuse` does, the way the gate refuses today.
- No per-shot work, and per frame at most one flag test. Work on the game's thread only; no `Timing` section off the main thread. Every new patch, tick and handler gets a timing section as `CLAUDE.md` says.
- Bounds against a faulty or hostile server: at most 255 parts, at most 900 bytes of payload a part unless the game's own limit read in `_decomp` is lower, at most 4,000 entries; anything over is dropped whole with one warning. Only keys of existing, server-set entries are applied; a value that does not parse for its entry's type is skipped and counted.
- Unity types stay out of the new rules file (the Quest port will link it). Do not stage, deploy, install, bump the version or push; no AI attribution in commits; `git add` by explicit path; keep each file's line endings.

## Review Focus

1. **A player edits their file while connected, or a script rewrites it every second.** The server's values are back within a frame of MelonLoader's own reload or within the mod's 2-second poll, whichever comes first, and what they typed is what they get back after leaving. Test the rule in Task 1; say in the report how the game-side re-apply is triggered.
2. **The game is closed, killed, or loses the connection while holding the server's values.** The file on disk still holds the player's own values. Test the hold/restore bookkeeping in Task 1; in Task 2 put the restore before MelonLoader's quit save and say how you checked the order.
3. **A value with line breaks, quotes, commas or non-ASCII text** (the tutorial lines, colour and effect lists) arrives byte for byte. Test in Task 1.
4. **A game built with other defaults** (an older looks build with the same wire number). Its check differs: logged under `log`, refused with a readable reason under `refuse`, never a half-applied state that plays differently without a line in both logs. Test the decision in Task 1.
5. **The server reloads its settings twice quickly, or while a game is still receiving the parts of the last send.** The newer send wins whole; parts of two sends are never mixed. Test in Task 1.

---

### Task 1: the rules and the wire (Unity-free)

**Files:**
- Create: `BluesArcheryOverhaul/SettingsSync.cs`
- Modify: `BluesArcheryOverhaul/JoinRules.cs` (the two kinds and their layouts in `JoinWire`)
- Modify: `BluesArcheryOverhaul.Tests/BluesArcheryOverhaul.Tests.csproj`, `BluesArcheryOverhaul.Tests/Program.cs`
- Test: `BluesArcheryOverhaul.Tests/SettingsSyncTests.cs` (new)

**Interfaces:**
- Consumes: `SettingsChecksum.Format/Compute` (find them; they are Unity-free already), `IBitWriter`/`IBitReader`, `JoinWire`'s head (`WriteHead`/`ReadHead`), `ModVersion`.
- Produces:

```csharp
internal struct SettingEntry { public string Key, Text, Default; }      // "Section.Name", current value and built-in default as text

internal static class SettingsSync {
    internal static readonly string[] LocalSections = { "Log", "Timing", "Telemetry", "Join", "Settings" };
    internal const int MaxParts = 255, MaxPartBytes = 900, MaxEntries = 4000;
    internal static bool ServerSet(string key);                                        // not in a local section
    internal static List<KeyValuePair<string, string>> Changed(IEnumerable<SettingEntry> all);   // server-set and Text != Default
    internal static uint Check(IEnumerable<SettingEntry> all);                         // over every server-set entry's key and Text
    internal static byte[] Pack(List<KeyValuePair<string, string>> changed);           // UTF-8, length-prefixed key and value
    internal static bool TryUnpack(byte[] bytes, out List<KeyValuePair<string, string>> changed);
    internal static List<byte[]> Split(byte[] packed, int partBytes = MaxPartBytes);    // at least one part, even when empty
    /// What a game must set: for every server-set entry, the server's text when sent, else the entry's own default.
    internal static List<KeyValuePair<string, string>> Targets(IEnumerable<SettingEntry> mine, List<KeyValuePair<string, string>> changed, out int unknownKeys);
}

/// One game's receipt of a send in parts: a newer sequence replaces an older one whole.
internal sealed class SettingsInbox {
    internal bool Add(int sequence, int part, int parts, uint check, byte[] payload, out byte[] whole, out uint wholeCheck);   // true once complete
}

/// The player's own values while the server's are held (texts by key).
internal sealed class SettingsHold {
    internal bool Held { get; }
    internal void Take(IEnumerable<SettingEntry> mine);                                // remembers Text of every server-set entry, once
    internal void FileChanged(IEnumerable<SettingEntry> reloaded);                     // the player's file was reloaded: these are now theirs
    internal List<KeyValuePair<string, string>> Release();                             // their own texts to put back; no longer held
}
```
  and in `JoinWire`: `KindSettings = 3`, `KindSettingsAck = 4`, `struct JoinSettings { int Wire; ModVersion Version; int Sequence, Part, Parts; uint Check; byte[] Payload; }`, `struct JoinSettingsAck { int Wire; ModVersion Version; int Sequence; uint Check; bool RestartNeeded; int Skipped; }`, `WriteSettings/ReadSettings`, `WriteSettingsAck/ReadSettingsAck` (the existing head, then: sequence 16 bits, part 8, parts 8, check 32, payload length 10 bits, the payload bytes; the ack: sequence 16, check 32, restart 1, skipped 12), and `JoinRule.SettingsDecision(uint serverCheck, JoinSettingsAck? ack, bool refuse, out string text)` returning pass, log or refuse with the text for the player and the log.

- [ ] **Step 1: Write the failing tests** in `SettingsSyncTests.cs` (`Check(...)` calls, run from `Program.Main`):
  - `LocalSectionsStayOut`: `Changed` and `Check` ignore `Log.Level`, `Timing.Enabled`, `Telemetry.Enabled`, `Join.Check`, `Settings.Version`; a section named `Logs` is server-set.
  - `OnlyChangedAreSent`: of five entries, the two whose `Text != Default` come back, in order.
  - `RoundTrip` (Review Focus 3): values `"a,b"`, `"say \"hi\""`, a two-line text, `"×–é"`, an empty string and a 3,000-character text survive `Pack` → `Split(…, 200)` → joined → `TryUnpack` byte for byte; an empty list packs to at least one part.
  - `UnpackRejectsDamage`: a truncated buffer, a length that runs past the end, and more than `MaxEntries` entries give `false` and no exception.
  - `TargetsResetTheRest`: with mine = {A changed by me, B at default, C changed by me} and the server sending {A: x}, the targets are A = x, B = default, C = default; a sent key that is not one of mine, or is in a local section, is counted in `unknownKeys` and not targeted.
  - `SameCheckAfterApplying`: the server's `Check` over its entries equals a game's `Check` after its texts are replaced by `Targets`, when both have the same defaults; with one differing default it differs (Review Focus 4).
  - `InboxNewerWins` (Review Focus 5): parts 0 and 1 of sequence 7 (of 3), then all parts of sequence 8 → complete with sequence 8's bytes; a late part 2 of sequence 7 is ignored; a duplicate part changes nothing; parts in the order 2, 0, 1 complete; `parts = 0`, `part >= parts`, more than `MaxParts` and a payload over `MaxPartBytes` are refused.
  - `HoldRemembersTheirs` (Review Focus 1 and 2): `Take` with A = "1"; `FileChanged` with A = "2" while held; `Release` gives A = "2" and `Held` is false; `Take` twice keeps the first values; `Release` when not held gives an empty list.
  - `WireRoundTrip`: `WriteSettings`/`ReadSettings` and `WriteSettingsAck`/`ReadSettingsAck` through the tests' bit buffer; `ReadHere` and `ReadHello` still reject a Settings message and read their own as before (their bit counts, 54 and 88, are unchanged).
  - `Decision`: equal checks pass; a differing check, a missing ack and `RestartNeeded` are `log` unless `refuse` is set, then refuse; each text names the server's settings and, for a restart, says to restart the game.
- [ ] **Step 2: Run** `dotnet run --project BluesArcheryOverhaul.Tests -c Release`. Expected: does not compile.
- [ ] **Step 3: Implement** `SettingsSync`, `SettingsInbox`, `SettingsHold` and the `JoinWire`/`JoinRule` additions.
- [ ] **Step 4: Run the tests.** Expected: `ALL PASS (N)`, N = 2300 plus the new checks.
- [ ] **Step 5: Commit.**

### Task 2: the server sends, the game holds

**Files:**
- Create: `BluesArcheryOverhaul/ServerSettings.cs`
- Modify: `BluesArcheryOverhaul/Cfg.cs` (list every entry as `SettingEntry`; set an entry from its text by its type; run the reload handlers and the checksum after a set; tell `ServerSettings` when the file was reloaded), `BluesArcheryOverhaul/JoinGate.cs` (the step sends the settings after a pass and waits for the ack; both sides read the two new kinds), `BluesArcheryOverhaul/Main.cs` (init, tick, quit), `BluesArcheryOverhaul/Builds.cs` and `BluesArcheryOverhaul/SkyfallLink.cs` (their "copy the [shared] settings from the server's cfg" texts now say the game could not take the server's settings), `BluesArcheryOverhaul/SelfCheck.cs` if it lists holders

**Interfaces:**
- Consumes: Task 1.
- Produces: `ServerSettings.Init()`, `ServerSettings.Tick()` (from `Main.OnUpdate`, before the headless return), `ServerSettings.Quit()` (first thing in `Main.OnApplicationQuit`), `ServerSettings.Held` (a client holds a server's settings now).

- [ ] **Step 1: Read the game.** In `_decomp`: the largest payload one `Connection.Send` message may carry (lower `MaxPartBytes` to fit, with margin, if it is under 900 bytes plus the head); how the server reaches each connected player's `Connection` after the join (for the send on reload); where a client learns it has left the server (`SkyfallLink.cs` already does this). In MelonLoader 0.7.3 (decompile `MelonLoader.dll` from the client's `MelonLoader\net35` into a scratch folder): whether its save at a clean quit runs before or after mods' `OnApplicationQuit`, and whether its own file watcher reloads entry values on another thread. Write what you found as comments at the top of `ServerSettings.cs`, as `JoinGate.cs` does.
- [ ] **Step 2: Server.** In the join step, after the Hello decision passes (also under `log`): pack the changed entries, send the parts, wait up to `[Join] AnswerSeconds` for the ack, decide with `JoinRule.SettingsDecision` and `[Join] SettingsMismatch`, log one line per join (`[Archery] Join: <player> took the server's settings (N sent, check xxxxxxxx)` or why not). On every `Cfg.Reloaded` on the server: next sequence, the same send to every connected game, acks compared and a mismatch logged (never a kick after the join). The packed bytes are built once per reload, not per player.
- [ ] **Step 3: Client.** On a complete Settings: `Take` the player's own values if not held, set every target from text, run the reload handlers once, answer with the ack (the check it now has; `RestartNeeded` when a start-only entry's value differs from what the tree was built with in this game run; the count of skipped values). One Summary line: `[Archery] Settings: this server sets the mod's settings (N differ from the built-in ones; your own file is not used on this server)`. When the player's file is reloaded while held (the mod's poll, or MelonLoader's watcher if it has one): `FileChanged`, then the server's targets again. On leaving the server, and in `Quit()`: `Release` and set the player's own texts back, with one Summary line. The file is never saved by this feature.
- [ ] **Step 4: Verify.** `dotnet run --project BluesArcheryOverhaul.Tests -c Release` → `ALL PASS`; `dotnet build BluesArcheryOverhaul -c Release --no-incremental` and `dotnet build BluesArcheryTestKit -c Release --no-incremental` → 0 warnings, 0 errors; `dotnet run --project BluesArcheryTestKit.Tests -c Release` → `ALL PASS (401)`.
- [ ] **Step 5: Commit.**

### Task 3: the words

**Files:**
- Modify: `README.md` (the Settings section: on a server the server's settings apply to everyone and the player's own file is not used; the per-machine sections; drop the line that `[shared]` settings must be matched by hand), the description of `[Join] SettingsMismatch` in `JoinGate.cs`, a copy of `CLAUDE.md` in your worktree root (git-ignored there): the join gate row and its network-exception bullet (two more kinds, what they carry, when), a row for `SettingsSync.cs` and `ServerSettings.cs`, the test count and the list of Unity-free files

- [ ] **Step 1:** Make those edits; keep the README plain and short (it is for players).
- [ ] **Step 2: Commit** `README.md` and `JoinGate.cs`.

## Report back

Worktree path and branch; the commits; test counts before and after; what you found in Step 1 of Task 2 (message size limit, how the server reaches connected games, the quit-save order, the file watcher); which entries are start-only; where you departed from the plan and why; exactly what the owner should see in the server's and the game's logs on a first join; what you could not verify (nothing here runs in the game: say so); any bug or risk you found.
