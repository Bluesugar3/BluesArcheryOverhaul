# Archery Steps 2 and 3: Draw Record, One Ultimate, the Volley Skill and Burst Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship 0.8.0: every release is decided once on the server by a draw record and a resolver (no stale timers, a fixed re-grab clock, one buyable ultimate, correct builds after sales, batched spectral hits with honest experience), and the multi-shot line becomes the Volley skill with the new timeline (5 ghosts at 0.75 s, the ratchet to 1.45 s, gold at the thump), echoes on snaps and early shots, Burst (30 spectral arrows), one flight model, and Holy Arrow after Charge 3 with no volley needed, so the next play-test has things to see.

**Architecture:** As in Step 1, every decision that can be stated without Unity lives in Unity-free files the test console compiles (`Rules.cs`, `Build.cs`, `Wire.cs`, and new `Release.cs`, `Spectral.cs`): the volley timeline, the resolver, the one-ultimate rule, the Holy Arrow live-draw gate, the flight arc, the Burst pattern, hit batching and the experience ledger, settings migration and the test-build parser. Thin Unity-bound files hold the Harmony patches: `Draws.cs` (one draw record per bow string, the release), `Shrine.cs` (one ultimate, sale fixes), `TestBuild.cs` (the server-only override), `VolleySkill.cs` (the copied skill), and the visual classes `VolleyFx.cs` and `Burst.cs`. No new message types: the server's decision rides back to spectators in the shot claim's existing kind and stage fields.

**Tech Stack:** C# (`net472`, `LangVersion latest`, nullable on), MelonLoader 0.7.3, HarmonyX (`0Harmony.dll` from `MelonLoader\net35`), Unity 2020.3 Mono, `dotnet` SDK for the build and the test console, Python 3 + Pillow for the icon script.

**Spec:** `docs/superpowers/specs/2026-11-12-archery-combinations-design.md`. This plan implements Part 13 Step 2 and Step 3 with their "Must also log" lists, using Part 12 items 1, 3, 4, 5 (the `DrawGrace` part only), 6 (live-draw gate and charge tier only), 7 (per-hit batching and experience only), 9, 10, 12 (Quiver Gather standing down only), 13 (glow off, material logs), 14; Part 11's live cap, LOD0 heads, no light per hit and cached misses; Part 9 items 5, 6, 9 (other archers' brightness) and 13; Part 14's per-key migration. Read Part 2, Part 4 (Volley, Full Volley, Burst, Charge 1, Holy Arrow entries), Part 12 items 1, 3, 4 and Part 13 Steps 2 and 3 before starting any task. The play-test 1 facts are in `.superpowers/sdd/2026-11-12-archery-step1-wire/progress.md` under "Play-test 1".

**Not in this plan (later steps; say so if asked):** Converge and Ricochet (the spec suggested Step 3; this plan leaves them for a later step), Seeking Volley, Weight (stagger, interrupt, the Balance Ring, the Fill, gold ghosts' stagger), Brace, Overcharge, Steady, Snap Draw, Mark, the four bridges, Quickdraw 4 line windows and the claim tolerance (Step 7), Holy Arrow forks, rods, the pour and the PvP blast (Step 8), the haptic mixer, the Ring's Edge colour (Step 5).

## Global Constraints

- One DLL, same build, on the dedicated server and every client. Version after this plan: `0.8.0` (`[assembly: MelonInfo(typeof(QuickshotRing.Main), "QuickshotRing", "0.8.0", "dmand")]`).
- No new network message types and no networked objects. The build bits (32 bits, version 1) and the shot claim (version 1) keep the Step 1 layouts exactly; this plan only starts using the claim's `Charged` kind and gives `stage` a meaning (Task 2, `ReleasePlan`).
- The server decides; clients only predict and draw. Spectators take the server's decision from the relayed claim.
- Unity-free files (`Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`) use only `System.*`: no `UnityEngine`, `MelonLoader`, `HarmonyLib` or game types. The test console compiles exactly these five files.
- Only APIs that exist in Unity 2020.3's Mono (`net472`): no `Math.Asinh`, `Math.Acosh`, `MathF`, `HashCode`, `System.Range`. Write `asinh(x)` as `Math.Log(x + Math.Sqrt(x * x + 1))`.
- Do not raise `Cfg.SettingsVersion` (it resets the whole file, `Cfg.cs:68-75`). A changed shipped default goes through the per-key migration of Task 1.
- Every new number and colour is a live setting in `UserData\QuickshotRing.cfg`. A setting the server's damage, timing or tree depends on is wrapped in `Cfg.Shared(...)` (`Cfg.cs:46-51`); look-only ones are not. Shared settings must match on the server and every client (the Step 1 checksum checks it).
- Each visual effect is its own small class. Art reuses the game's own effects, sounds, materials and textures by name (`UserData\QuickshotRing\loaded_assets.txt` lists them); never spawn the game's teleport, unlock, buff aura, echolocation dome or sword target-orb effects.
- Spectral arrows never damage players (`Hits.cs:98-99` stays). Holy Arrow's blast is unchanged in this plan.
- Nothing the game identifies by type may be used to find Slowdraw once the Volley copy exists (both are `BowSlowdraw`): find Slowdraw, Two-Shot and Three-Shot once, before re-pointing, and afterwards compare by reference or slot id.
- Server state is per bow string and per arrow id, never a single static "current shot".
- The assistant cannot run the game. Every behaviour that is not unit-tested emits one greppable line starting `[Archery] ` on the side where it happens, and every new Harmony patch logs once that it fired. Patch targets and reflection names are only checked at runtime.
- RevampedSkills stays disabled on the local server (spec "Decisions pending" item 1). Patches on methods RevampedSkills also patches (`PlayerProgressionManager.IsSlotAvailable`, `UnlockSlot`, `BowStringPullbackInputMethod.CheckForPullback`, `Reset`, `BowString.OnShoot`) carry the explicit `[HarmonyPriority]` given in their task.
- Not a git repository: no git commands. Each task ends with a Checkpoint: run `dotnet run --project QuickshotRing.Tests -c Release` and `dotnet build QuickshotRing -c Release` from `C:\Users\dmand\OneDrive\Desktop\attmod`, expect the stated `ALL PASS (n)` and `0 Error(s)`, keep the warning count at the 0.7.0 baseline (0), and list the files changed.

## Review Focus

1. **Owners of Two-Shot and Three-Shot keep their ghosts after the re-point.** Saves store slot ids (`_decomp/PathExperience.cs:150-162`); the levels are counted against whatever skill the slot points at when the save loads, so the slot must already point at the Volley skill. The tree is injected inside `ProfessionSkillTree.GetProfessionTree`, which `PathExperience.Serialize` calls before it reads any slot (`PathExperience.cs:138`). Pinned by Task 8's start-up line `Volley skill: level {n} for '{name}'` (logged at save load), its hard-fail ERROR `Volley skill: '{name}' owns Two-Shot/Three-Shot slots but the Volley level is 0` (log row 14's "none of" list), and play-test rows A2/A7.
2. **Slowdraw must not change because of the copy.** The game keys each player's input by the input-structure asset (`_decomp/HeldPickupHandler.cs:18`, `:32-47`); a shared asset means a shared timer and shared sounds. Task 8's log `Volley skill: own pull settings ... separate settings objects: yes ... own input maps: yes` and play-test row A7 (Slowdraw's charge sound still close to two seconds after full draw).
3. **Two charged skills writing the arrow's one-off multiplier.** `BowDrawSkill.OnArrowShot` assigns, it does not multiply (`_decomp/Alta.Progression.Skills/BowDrawSkill.cs:63-78`), and handler order is subscription order. A flat 1.0 table on the Volley copy would still overwrite Slowdraw's 1.25 if Volley ran last, so Task 8 restores the multiplier around the copy's handler and logs it; the resolver test "resolve charged volley takes the charge tier" (Task 2) pins the spectral side.
4. **A short dip at full draw, now for every input including Quickdraw.** `DrawGrace` holds any in-range input for `RelaxGraceSeconds` (Task 6), and the draw clock resumes the same span through such a dip. Tests "clock stays active through a short dip", "clock short dip resumes the span", "clock long dip starts a new span" (Task 2); play-test row A9.
5. **A spectator whose own state disagrees with the server.** Spectators draw what the relayed claim says (kind and stage), not their own guess; the server always relays its decision, even for a shooter that sent no claim (Task 6 Step 3). Tests "plan stage round trip", "plan charged with pellets" (Task 2); log row 13 (B6's relay). A second client is not available this session, so what a spectator sees is listed as untested.
6. **A client without QuickshotRing (SyncLib still on both sides).** The server sends that client the Volley statuses for skill id 47011, which it cannot look up: `HashedGeneralValue.Get` logs `Missing hash` and `NetworkSkillMessage` then calls `SendSerializedMessage` on null (`_decomp/PlayerProgressionManager.cs:449-453`). Expected: it stays connected and sees plain arrows. Checked by optional play-test row C1 and log row 15 (that client's game log: count of `Missing hash 47011`, any disconnect).
7. **An input still in range when the arrow leaves keeps timing and charges the next arrow.** `CheckForPullback` returns early while no arrow is docked (`_decomp/BowStringPullbackInputMethod.cs:95-98`), so a snap leaves Volley's and Slowdraw's timers running (and Holy Arrow's 6.5 s one). Pinned by Task 6 Step 4's reset on every shot and its log line; play-test rows A12 and B4b.

Also kept in view (tests exist): a hand-tuned setting at a changed default ("migrate keeps a hand-tuned value"), and a kept value whose meaning changed ("meaning-changed keys listed", logged by Task 1 Step 5); an arrow changed on the string before Holy Arrow arms ("holy refuses a swapped arrow"); the 10961 ms re-grab claim ("clock after a re-grab a light shot has no time"); a weak pull that never reached 68% ("resolve uncrossed early has no echoes"); a second ultimate while one is owned ("second ultimate blocked"); a sale that leaves a shared skill at level 0 ("dropped skills found", "a shared skill dropped to 0 is found").

---

## Decisions made here (spec "Left to the builder")

**Slot and skill ids** (fixed forever once shipped; all in `Build.cs` `SlotIds`). Game slot ids read in play-test 1 are all below 65536 (Quick1 41888, Quick2 32348, Quick3 41311, Charge1 60535, Charge2 63112, Charge3 63178, Two-Shot 32124, Three-Shot 32416, Lightbeam 32504, Gatherer 37735), so every new slot id is above 65535 and cannot collide with one; Task 9 still checks at runtime.

| Orb | Slot id | | Orb | Slot id |
|---|---|---|---|---|
| Snap Draw | 1732050807 | | Converge | 3741657386 |
| Mark | 2236067977 | | Ricochet | 3872983346 |
| Echo | 2645751311 | | Gold Volley | 4123105625 |
| Tracer | 3162277660 | | Shatter | 4242640687 |
| Burst | 3316624790 | | Brace | 1259921049 |
| Seeking Volley | 3605551275 | | Overcharge | 1442249570 |

The Volley skill's id is `47011` (at most 65535, spec Part 12 item 3; checked unused at runtime). Only Burst's slot enters the tree in this plan.

**Timeline** (seconds after the 68% crossing): Volley ghosts at `[Archery] TwoShotSeconds` 0.75 (the Volley skill's own timer), the ratchet from `ThreeShotSeconds` 1.0 every `[Volley] OuterInterval` 0.045 (the tenth at 1.405), the ripple at `[Archery] RippleSeconds` 1.5 or at Slowdraw's own charge time (1.75, read from its pull settings) when a Charge orb is owned, the rack at `[Archery] RackSeconds` 2.0. The spec's ripple at 1.5 sits 0.05 s after the ratchet ends (it calls the ratchet "one textured beat"); this plan keeps the spec's times.

**What the claim's `stage` means:** the number of spectral arrows the release fires. For `Snap` and `Early` they are echoes (0 to 3); for `Held` and `Charged` the first 15 are ghosts and the rest are pellets (0 to 15). `ReleasePlan.From(kind, stage)` decodes it.

**What counts as an early shot:** a release after the snap window and before anything has charged, so the string must have crossed 68% (spec line 601: "after the snap window, before ghosts"). A weak pull that never crossed is a plain arrow with no echoes (resolver rule 5, Task 2).

**Echo layout:** echo `i` (0 to 2) flies from inner ghost `i`'s tip position (top, then 72 degrees round). **Pellet start:** pellet `p` starts at inner ghost `p % 5`'s tip and flies on the fixed daisy direction. **Nock point:** `[Volley] NockBack` 0.75 m behind the real arrow's tip along its forward direction (Part 12 item 4: "the tip minus the arrow's forward direction times its length"); "up" is the arrow's own up.

**Test build syntax:** server cfg `[TestBuild] Player` (a username, case-insensitive) and `[TestBuild] Orbs` (orb names from `enum Orb`, separated by `;`, case-insensitive, e.g. `Quickdraw4;HolyArrow;Charge2;Charge3`).

**Gold at the thump:** once the shot is charged (Slowdraw charged), shafts turn `[Volley] ChargedColour` `#FFC733` (spec: "orange-to-gold always means charged"). Heads stay on the skill material; recolouring heads waits for the ramp test (Task 11).

## Settings

New or changed in this plan (`S` = shared). Changed defaults migrate per key (Task 1).

| Key | Default | S | Note |
|---|---|---|---|
| `[Archery] TwoShotSeconds` | 0.75 (was 0.5) | S | "Seconds after the 68% crossing until the five Volley ghosts appear (the Volley skill's own timer)." |
| `[Archery] ThreeShotSeconds` | 1.0 | S | "Seconds after the 68% crossing until the Full Volley ratchet starts." Meaning changed (was "after Slowdraw"; Task 1 logs it) |
| `[Archery] RippleSeconds` | 1.5 | S | full-volley ripple without a Charge orb |
| `[Archery] RackSeconds` | 2.0 | S | Burst's rack |
| `[Archery] HideGameGlow` | true | | the game's whole-arrow glow on Slowdraw, Volley and Holy Arrow |
| `[Volley] OuterInterval` | 0.045 (was 0.12) | S | |
| `[Volley] Colour` | `#00E3FF` (was `#FFEDB8`) | | uncharged ghosts |
| `[Volley] ChargedColour` | `#FFC733` | | ghosts of a charged shot |
| `[Volley] HitFlash` | 1.5 | | description: "Brightness of the one light per volley, at its densest landing. 0 = none." Meaning changed (was one light per ghost) |
| `[Volley] RelaxGraceSeconds` | 0.35 | S | description in Task 6 Step 2. Meaning changed (now every input, Quickdraw included) |
| `[Volley] EchoShare` | 0.10 (`SpectralShares.Echo`) | S | |
| `[Volley] NockBack` | 0.75 | S | |
| `[Volley] LiveCap` | 200 | | server: above this many live spectral arrows each casts every other tick |
| `[Volley] ClientCastRange` | 40 | | clients cast only for spectral arrows this close to the camera |
| `[Volley] OtherArcherBrightness` | 0.6 | | |
| `[Volley] LandSparks` | 3 | | |
| `[Volley] SparkMaterial` | `Sparks` | | particle material name from the dump |
| `[Volley] LandSound`, `LandSoundsMax` | `Arrow Impact Sounds`, 3 | | |
| `[Volley] SlideSound`, `RatchetSound`, `ReleaseSound` | `Skill Orb Interact Sounds`, `Click`, `Arrow Woosh Sounds` | | |
| `[Volley] RippleLength`, `RippleBoost` | 0.25, 1.6 | | |
| `[Volley] HapticStrength` | 0.5 | | |
| `[Volley] RampTest` | true | | ghost 0 uses a ramp-swapped head (Task 11) |
| `[Burst] PelletShare` | 0.04 (`BurstPattern.PelletShare`) | S | |
| `[Burst] InnerDegrees`, `OuterDegrees` | 2.5, 5 (`BurstPattern.InnerDegrees`, `OuterDegrees`) | S | |
| `[Burst] Range` | 30 (`BurstPattern.Range`) | S | metres from the release point |
| `[Burst] BarbScale`, `BreatheSeconds`, `BreatheAmount` | 0.35, 1, 0.08 | | |
| `[Burst] StreakLength`, `ShedSparks`, `PuffSpecks` | 0.15, 12, 4 | | |
| `[Burst] RackSound`, `RackClick`, `RackClickDelay`, `WhumpSound` | `Dock Hovered Over Sounds`, `Click`, 0.08, `Bow Shoot Sounds` | | |
| `[Tree] BurstCost` | 1 | S | "Skill points for the Burst orb. Changing it after someone bought the orb refunds the wrong amount when they sell it." |
| `[TreeLayout] HolyArrow` | `auto` (was `-0.19, 0.55, 0.44`) | S | `auto` = continue the Slowdraw column above Charge 3 |
| `[TreeLayout] Burst` | `-0.19, 0.55, 0.44` | S | Holy Arrow's old spot, above Three-Shot |
| `[TestBuild] Player`, `Orbs` | `""`, `""` | | server only |
| `[Wire] LogWire` | false (was true) | | Step 1's deferred note: per-key migration in Step 2 |

Defaults named in brackets are constants in the Unity-free files (Task 3) and are used as the cfg defaults, so the tests pin the shipped values. Which task creates or changes each entry: Task 1 `LogWire`; Task 4 `[TestBuild]`; Task 6 `RelaxGraceSeconds`' description; Task 8 `TwoShotSeconds` and `ThreeShotSeconds` (`Tree.cs:57-58`: defaults 0.75 and 1.0, new descriptions), `RippleSeconds`, `RackSeconds`, `HideGameGlow` (Step 2), `OuterInterval` (`Volley.cs:47`: 0.045); Task 9 `BurstCost`, `[TreeLayout] HolyArrow` and `Burst`; Task 10 `Colour` (`Volley.cs:58-59`: `#00E3FF`, fallback `DefaultColour` at `Volley.cs:36` becomes `new Color(0f, 0.89f, 1f)`), `HitFlash`' description, `ChargedColour`, `EchoShare`, `NockBack`, `LiveCap`, `ClientCastRange`, `OtherArcherBrightness`, `LandSparks`, `SparkMaterial`, `LandSound`, `LandSoundsMax`, `[Burst] PelletShare`, `InnerDegrees`, `OuterDegrees`, `Range` (these four shared), `StreakLength`, `PuffSpecks`; Task 11 every other new entry (`SlideSound`, `RatchetSound`, `ReleaseSound`, `RippleLength`, `RippleBoost`, `HapticStrength`, `RampTest`, `BarbScale`, `BreatheSeconds`, `BreatheAmount`, `ShedSparks`, `RackSound`, `RackClick`, `RackClickDelay`, `WhumpSound`).

---

## File Structure

| File | Responsibility |
|---|---|
| `QuickshotRing/Rules.cs` (modify) | `SettingsMigration`; `DrawClock` gains the arrow at the crossing, a dip grace and `Reset`; retires `DrawTimeline.ExtrasShowing` and its constants (Task 8). |
| `QuickshotRing/Release.cs` (new, Unity-free) | `VolleyTimes`, `VolleyTimeline`, `VolleyBeat`, `ReleasePlan`, `ReleaseFacts`, `UltimateUse`, `Resolution`, `Resolver`, `UltimateRule`, `HolyGate`. |
| `QuickshotRing/Spectral.cs` (new, Unity-free) | `ArcModel`, `BurstPattern`, `SpectralMath`, `HitBatch`, `ReleaseLedger`, `SpectralBudget`. |
| `QuickshotRing/Build.cs` (modify) | `SlotIds`, `TestBuildRule`, `SkillLevels`. |
| `QuickshotRing/Cfg.cs` (modify) | Per-key migration and the shipped-defaults file. |
| `QuickshotRing/Builds.cs` (modify) | Server build cache and refresh postfixes; client build accessor; two-ultimate load log. |
| `QuickshotRing/TestBuild.cs` (new) | The server-only test build and its game-skill grants. |
| `QuickshotRing/Shrine.cs` (new) | One buyable ultimate; sale fixes; cost and multiplier logs. |
| `QuickshotRing/Draws.cs` (new) | Draw record per bow string, re-grab reset, `DrawGrace`, the release (resolver call), input reset on release, clear-on-release logs. |
| `QuickshotRing/VolleySkill.cs` (new) | The copied Volley skill, its registration, glow switch-off, multiplier guard, status hooks. |
| `QuickshotRing/Volley.cs` (modify) | Volley state from the Volley skill and the timeline; release layout from the real arrow; echoes, ghosts, pellets on the arc; landing visuals. |
| `QuickshotRing/VolleyFx.cs` (new) | Beats: slide-out, ratchet, ripple, release flare; shared spark emitter; haptic pulses. |
| `QuickshotRing/Burst.cs` (new) | Barbs, rack, shed, pellet streaks and puff. |
| `QuickshotRing/Hits.cs` (modify) | Batched spectral hits, experience ledger, no cripple on spectral hits, immunity groups, `Describe` with cripple. |
| `QuickshotRing/Tree.cs` (modify) | Re-point Two-Shot and Three-Shot, Volley root, Burst slot, Holy Arrow after Charge 3, `auto` layout, positions log, multipliers by tier, icons. |
| `QuickshotRing/HolyArrow.cs` (modify) | Live-draw gate, charge tier for the beam, fold whatever is showing. |
| `QuickshotRing/Claims.cs` (modify) | Clock moved to `Draws`; client claim from the local resolution; server relays its resolution. |
| `QuickshotRing/Quickdraw4.cs`, `QuiverGather.cs`, `Main.cs`, `Fx.cs`, `Probe.cs` (modify) | Ownership from the build; start-up registration; cached misses; logs. |
| `QuickshotRing/Art/make_top_icons.py`, `QuickshotRing/Resources/Burst.png` | Burst icon and comparison sheet. |
| `QuickshotRing.Tests/*` | `MigrationTests.cs`, `ReleaseTests.cs`, `SpectralTests.cs`, `BuildTests.cs` (new); `Program.cs`, `.csproj` (modify). |
| `CLAUDE.md` | File table, test count, new rules learned. |

---

### Task 1: Per-key settings migration

**Files:**
- Modify: `QuickshotRing/Rules.cs` (append `SettingsMigration`), `QuickshotRing/Cfg.cs:68-79` (`Ready`), `QuickshotRing/Cfg.cs:53-58` (`LogWire` default `false`), `QuickshotRing.Tests/Program.cs:73-74` (call `MigrationTests.Run()` after `RuleTests.Run()`)
- Create: `QuickshotRing.Tests/MigrationTests.cs`

**Interfaces:**
- Consumes: `SettingsChecksum.Format(object?)` (`Rules.cs:175-180`), `Cfg.Folder` (`Cfg.cs:35`), MelonLoader `MelonPreferences_Entry.BoxedValue`, `.ResetToDefault()`, `.Identifier`, `.Category.Identifier` (as `Cfg.cs:61-62`). The default value is the public property `DefaultValue` of `MelonPreferences_Entry<T>` (checked by reflection on the installed `MelonLoader\net35\MelonLoader.dll` while planning); read it untyped with `Traverse.Create(entry).Property("DefaultValue").GetValue()`. Do not use `GetDefaultValueAsString()`: its text is MelonLoader's, not `SettingsChecksum.Format`'s.
- Produces (namespace `QuickshotRing`, `internal`):
  - `static class SettingsMigration { static bool ShouldReset(string current, string? oldDefault, string newDefault); static string Format(IDictionary<string, string> defaults); static Dictionary<string, string> Parse(string text); static IReadOnlyDictionary<string, string> Legacy070 { get; } static IReadOnlyCollection<string> MeaningChanged070 { get; } static string? OldDefault(string key, IDictionary<string, string>? recorded); }`
  - `Cfg.DefaultsFile` = `Path.Combine(Folder, "shipped_defaults.txt")`.

- [ ] **Step 1: Write the failing tests in `MigrationTests.cs`**

```csharp
using System.Collections.Generic;
using QuickshotRing;
using static Program;

internal static class MigrationTests {
    internal static void Run() {
        Check("migrate resets an untouched old default", SettingsMigration.ShouldReset("0.5", "0.5", "0.75"));
        Check("migrate keeps a hand-tuned value", !SettingsMigration.ShouldReset("0.6", "0.5", "0.75"));
        Check("migrate leaves an unchanged default alone", !SettingsMigration.ShouldReset("6.5", "6.5", "6.5"));
        Check("migrate needs a known old default", !SettingsMigration.ShouldReset("0.5", null, "0.75"));
        Check("migrate value already at the new default", !SettingsMigration.ShouldReset("0.75", "0.5", "0.75"));
        var map = new Dictionary<string, string> { { "Volley.Colour", "#FFEDB8" }, { "Archery.TwoShotSeconds", "0.5" } };
        string text = SettingsMigration.Format(map);
        Check("defaults file sorted, one per line", text == "Archery.TwoShotSeconds=0.5\nVolley.Colour=#FFEDB8\n");
        var back = SettingsMigration.Parse(text);
        Check("defaults file round trip", back.Count == 2 && back["Volley.Colour"] == "#FFEDB8" && back["Archery.TwoShotSeconds"] == "0.5");
        Check("defaults file skips junk", SettingsMigration.Parse("# note\n\nA.B=1\nnoequals\n=x\n").Count == 1);
        Check("defaults value may contain '='", SettingsMigration.Parse("A.B=x=y\n")["A.B"] == "x=y");
        Check("defaults file tolerates CRLF", SettingsMigration.Parse("A.B=1\r\nC.D=2\r\n")["A.B"] == "1");
        var legacy = SettingsMigration.Legacy070;
        Check("legacy table is the 0.7.0 defaults that change", legacy.Count == 5 && legacy["Archery.TwoShotSeconds"] == "0.5"
              && legacy["Volley.OuterInterval"] == "0.12" && legacy["Volley.Colour"] == "#FFEDB8"
              && legacy["TreeLayout.HolyArrow"] == "-0.19, 0.55, 0.44" && legacy["Wire.LogWire"] == "True");
        Check("old default prefers the recorded file",
              SettingsMigration.OldDefault("Volley.Colour", new Dictionary<string, string> { { "Volley.Colour", "#123456" } }) == "#123456");
        Check("old default falls back to the legacy table", SettingsMigration.OldDefault("Volley.Colour", null) == "#FFEDB8"
              && SettingsMigration.OldDefault("Volley.Colour", new Dictionary<string, string>()) == "#FFEDB8");
        Check("old default unknown key", SettingsMigration.OldDefault("X.Y", null) == null);
        Check("format matches what the file holds", SettingsChecksum.Format(0.12f) == "0.12" && SettingsChecksum.Format(0.045f) == "0.045"
              && SettingsChecksum.Format(true) == "True");
        var meaning = SettingsMigration.MeaningChanged070;
        Check("meaning-changed keys listed", meaning.Count == 3 && meaning.Contains("Archery.ThreeShotSeconds")
              && meaning.Contains("Volley.HitFlash") && meaning.Contains("Volley.RelaxGraceSeconds"));
    }
}
```

That is 16 checks (add `using System.Linq;` for `Contains`).

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `SettingsMigration` not defined.

- [ ] **Step 3: Implement `SettingsMigration` in `Rules.cs`**

Implement as the Interfaces block and the tests state (comparisons ordinal; in `Parse`, later duplicates win). `Legacy070` holds exactly the five pairs the test names (values as `SettingsChecksum.Format` writes them); `MeaningChanged070` exactly the three keys.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (121)`.

- [ ] **Step 5: Wire the migration into `Cfg.Ready`**

After the `SettingsVersion` block and before `SaveToFile`: read `DefaultsFile` if it exists (`try`; a read failure counts as "no file"), and for every entry of every category compute `key = Category.Identifier + "." + Identifier`, `current = SettingsChecksum.Format(BoxedValue)`, `newDefault = SettingsChecksum.Format(<entry's default value>)`, `old = SettingsMigration.OldDefault(key, recorded)`. If `ShouldReset`, `ResetToDefault()` and log; else if `old != null && old != newDefault`, log the kept line. Separately, when the recorded defaults file was absent and a `MeaningChanged070` key's `current != newDefault`, log the meaning line (its default did not change, so the per-key migration cannot see it). Then write `DefaultsFile` with every entry's `newDefault` (create `Folder` first) and log the recorded line. Skip the `Settings.Version` entry. Set `LogWire`'s default to `false` (`Cfg.cs:57`); its description becomes `Log every build-bits and shot-claim message (wire test). Off by default since 0.8.0.`

Log lines, exactly:
- `[Archery] Settings: {key} reset to the new default {new} (it was still the old default {old})`
- `[Archery] Settings: {key} kept at your value {value} (its default changed from {old} to {new})`
- `[Archery] Settings: shipped defaults recorded for {n} settings ({DefaultsFile})`
- `[Archery] Settings: {key} kept at your value {value}, but since 0.8.0 it means: {entry.Description}`

Edge: a `SettingsVersion` reset in the same start (an old file) makes every value equal to its new default, so nothing is logged as reset; that is correct.

- [ ] **Step 6: Checkpoint**

Tests `ALL PASS (121)`; build `0 Error(s)`, 0 warnings. Files changed: `Rules.cs`, `Cfg.cs`, `QuickshotRing.Tests/MigrationTests.cs`, `QuickshotRing.Tests/Program.cs`.

---

### Task 2: Release rules and the draw clock (Unity-free)

**Files:**
- Create: `QuickshotRing/Release.cs`, `QuickshotRing.Tests/ReleaseTests.cs`
- Modify: `QuickshotRing/Rules.cs:99-122` (`DrawClock`), `QuickshotRing/Rules.cs:6-7` (header comment lists the five Unity-free files), `QuickshotRing.Tests/QuickshotRing.Tests.csproj` (add `<Compile Include="..\QuickshotRing\Release.cs" />` and `Spectral.cs`, the latter an empty `namespace QuickshotRing { }` file created now so Task 3 only fills it), `QuickshotRing.Tests/Program.cs` (call `ReleaseTests.Run()`)

**Interfaces:**
- Consumes: `Build`, `Build.From`, `Orb`, `Ultimates` (`Build.cs:24-87`), `ShotKind` (`Wire.cs`), `DrawTimeline.HolyTotal`, `GatherSeconds` (`Rules.cs:16-17`). The game's `SkillStatus` values (`_decomp/SkillStatus.cs`): `TestingCantBuy = 2`, `CantBuy = 3`, `TestingAllowed = 8`, `Allowed = 9`.
- Produces (namespace `QuickshotRing`, `internal`):
  - `readonly struct VolleyTimes { readonly float VolleyAt, FullAt, Interval, RippleAt, ThumpAt, RackAt; VolleyTimes(float volleyAt, float fullAt, float interval, float rippleAt, float thumpAt, float rackAt); static VolleyTimes Default /* 0.75, 1.0, 0.045, 1.5, 1.75, 2.0 */; }`
  - `[Flags] enum VolleyBeat { None = 0, SlideOut = 1, Ripple = 2, Rack = 4 }`
  - `static class VolleyTimeline { static int Ghosts(int volleyTier, bool volleyCharged, float t, VolleyTimes tm); static bool Racked(int volleyTier, bool volleyCharged, float t, VolleyTimes tm); static float RippleAt(int chargeTier, VolleyTimes tm); static VolleyBeat BeatsBetween(float from, float to, int volleyTier, int chargeTier, VolleyTimes tm); }`
  - `readonly struct ReleasePlan { readonly int Ghosts, Pellets, Echoes; int Stage /* sum */; ReleasePlan(int ghosts, int pellets, int echoes); static ReleasePlan From(ShotKind kind, int stage); ToString() => $"ghosts {Ghosts} pellets {Pellets} echoes {Echoes}"; }`
  - `enum UltimateUse { None, Line, Beam }`
  - `struct ReleaseFacts { Build Build; bool SnapReady, ChargeActive, HolyArmed, Racked, Crossed; int Ghosts; }` (`Crossed`: the string crossed 68% with this arrow; server `Draws.Elapsed(bow) != null`, shooter the local clock likewise)
  - `readonly struct Resolution { readonly ShotKind Kind; readonly UltimateUse Ultimate; readonly ReleasePlan Plan; readonly int ChargedTier; Resolution(ShotKind kind, UltimateUse ultimate, ReleasePlan plan, int chargedTier); ToString() => $"{Kind} ult:{(Ultimate == UltimateUse.None ? "-" : Ultimate.ToString())} {Plan} charge tier {ChargedTier}"; }`
  - `static class Resolver { static Resolution Resolve(ReleaseFacts f); }`
  - `static class UltimateRule { const int TestingCantBuy = 2, CantBuy = 3, TestingAllowed = 8, Allowed = 9; static bool IsUltimate(Orb o); static bool Blocks(Orb slotOrb, Ultimates owned); static int Downgrade(int status); }`
  - `static class HolyGate { static bool MayArm(bool stringHeld, bool crossed, uint arrowNow, uint arrowAtCrossing); }`
  - `DrawClock` additions: `void Update(float pull, uint arrowId, float now, float dipGrace)`, `void Reset()`, `uint ArrowAtCrossing { get; }`, `bool Active(float now, float dipGrace)`. The existing `Update(float pull, bool nocked, float now)` stays and calls `Update(pull, nocked ? 1u : 0u, now, 0f)`.

- [ ] **Step 1: Write the failing tests in `ReleaseTests.cs`**

`internal static class ReleaseTests { internal static void Run() { ... } }` with `using QuickshotRing; using static Program;`.

```csharp
var tm = VolleyTimes.Default;
// Part 2's clock: 5 ghosts at 0.75, ten more one every 0.045 from 1.0, ripple 1.5 (1.75 with a Charge orb), rack 2.0
Check("timeline defaults are the spec's", Near(tm.VolleyAt, 0.75f) && Near(tm.FullAt, 1f) && Near(tm.Interval, 0.045f)
      && Near(tm.RippleAt, 1.5f) && Near(tm.ThumpAt, 1.75f) && Near(tm.RackAt, 2f));
Check("no volley orb, no ghosts", VolleyTimeline.Ghosts(0, true, 5f, tm) == 0);
Check("volley not charged, no ghosts", VolleyTimeline.Ghosts(3, false, 5f, tm) == 0);
Check("volley shows five", VolleyTimeline.Ghosts(1, true, 0.75f, tm) == 5 && VolleyTimeline.Ghosts(1, true, 9f, tm) == 5);
Check("full volley waits for 1.0 s", VolleyTimeline.Ghosts(2, true, 0.99f, tm) == 5);
Check("ratchet first at 1.0 s", VolleyTimeline.Ghosts(2, true, 1.0f, tm) == 6);
Check("ratchet second at 1.045 s", VolleyTimeline.Ghosts(2, true, 1.045f, tm) == 7);
Check("ratchet done at 1.405 s", VolleyTimeline.Ghosts(2, true, 1.405f, tm) == 15 && VolleyTimeline.Ghosts(2, true, 1.404f, tm) == 14);
Check("ratchet capped at fifteen", VolleyTimeline.Ghosts(3, true, 60f, tm) == 15);
Check("rack needs Burst, the charge and 2.0 s", VolleyTimeline.Racked(3, true, 2f, tm) && !VolleyTimeline.Racked(3, true, 1.99f, tm)
      && !VolleyTimeline.Racked(2, true, 5f, tm) && !VolleyTimeline.Racked(3, false, 5f, tm));
Check("ripple at 1.5 s with no charge orb", Near(VolleyTimeline.RippleAt(0, tm), 1.5f));
Check("ripple folds into the thump with a charge orb", Near(VolleyTimeline.RippleAt(2, tm), 1.75f));
Check("beat slide-out once", VolleyTimeline.BeatsBetween(0.7f, 0.76f, 1, 0, tm) == VolleyBeat.SlideOut
      && VolleyTimeline.BeatsBetween(0.76f, 0.8f, 1, 0, tm) == VolleyBeat.None);
Check("beat ripple needs Full Volley", VolleyTimeline.BeatsBetween(1.49f, 1.51f, 1, 0, tm) == VolleyBeat.None
      && VolleyTimeline.BeatsBetween(1.49f, 1.51f, 2, 0, tm) == VolleyBeat.Ripple);
Check("beat ripple moves to the thump", VolleyTimeline.BeatsBetween(1.49f, 1.51f, 2, 1, tm) == VolleyBeat.None
      && VolleyTimeline.BeatsBetween(1.74f, 1.76f, 2, 1, tm) == VolleyBeat.Ripple);
Check("beat rack needs Burst", VolleyTimeline.BeatsBetween(1.99f, 2.01f, 3, 0, tm) == VolleyBeat.Rack
      && VolleyTimeline.BeatsBetween(1.99f, 2.01f, 2, 0, tm) == VolleyBeat.None);
Check("several beats in one step", VolleyTimeline.BeatsBetween(0f, 3f, 3, 0, tm) == (VolleyBeat.SlideOut | VolleyBeat.Ripple | VolleyBeat.Rack));
Check("the holy pause still comes after the rack", DrawTimeline.HolyTotal - DrawTimeline.GatherSeconds > tm.RackAt + 0.5f);

// the claim's stage counts every spectral arrow: echoes for snaps and early shots, else ghosts then pellets
Check("plan early is echoes", ReleasePlan.From(ShotKind.Early, 3) is var p1 && p1.Echoes == 3 && p1.Ghosts == 0 && p1.Pellets == 0);
Check("plan snap echoes capped at three", ReleasePlan.From(ShotKind.Snap, 9).Echoes == 3);
Check("plan held is ghosts", ReleasePlan.From(ShotKind.Held, 15) is var p2 && p2.Ghosts == 15 && p2.Pellets == 0 && p2.Echoes == 0);
Check("plan charged with pellets", ReleasePlan.From(ShotKind.Charged, 30) is var p3 && p3.Ghosts == 15 && p3.Pellets == 15);
Check("plan stage round trip", ReleasePlan.From(ShotKind.Held, 30).Stage == 30 && ReleasePlan.From(ShotKind.Early, 2).Stage == 2
      && ReleasePlan.From(ShotKind.Held, 0).Stage == 0);

// the resolver: one decision per release (Part 12 item 4, rule 3, rule 4, Part 5 "Two ultimates")
Build b6 = Build.From(new[] { Orb.Quick1, Orb.Quick2, Orb.Quick3, Orb.Volley, Orb.FullVolley, Orb.Charge1 }); // the user's character
Build burst = Build.From(new[] { Orb.Volley, Orb.FullVolley, Orb.Burst });
Build both = Build.From(new[] { Orb.Quick1, Orb.Quick2, Orb.Quick3, Orb.Quickdraw4, Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.HolyArrow, Orb.Volley });
Resolution r1 = Resolver.Resolve(new ReleaseFacts { Build = b6, SnapReady = true });
Check("resolve snap carries echoes", r1.Kind == ShotKind.Snap && r1.Ultimate == UltimateUse.None && r1.Plan.Echoes == 2 && r1.Plan.Ghosts == 0 && r1.ChargedTier == 0);
Check("resolve early carries echoes", Resolver.Resolve(new ReleaseFacts { Build = b6, Crossed = true }) is var r2 && r2.Kind == ShotKind.Early && r2.Plan.Echoes == 2);
Check("resolve uncrossed early has no echoes", Resolver.Resolve(new ReleaseFacts { Build = b6 }) is var r2b && r2b.Kind == ShotKind.Early && r2b.Plan.Stage == 0);
Check("resolve early without volley orbs", Resolver.Resolve(new ReleaseFacts { Build = Build.From(new[] { Orb.Quick1 }), Crossed = true }).Plan.Stage == 0);
Check("resolve held is uncharged", Resolver.Resolve(new ReleaseFacts { Build = b6, Ghosts = 15 }) is var r3 && r3.Kind == ShotKind.Held
      && r3.Plan.Ghosts == 15 && r3.Plan.Echoes == 0 && r3.ChargedTier == 0);
Check("resolve charged volley takes the charge tier", Resolver.Resolve(new ReleaseFacts { Build = b6, Ghosts = 15, ChargeActive = true }) is var r4
      && r4.Kind == ShotKind.Charged && r4.ChargedTier == 1);
Check("resolve charged without ghosts", Resolver.Resolve(new ReleaseFacts { Build = b6, ChargeActive = true }) is var r5
      && r5.Kind == ShotKind.Charged && r5.Plan.Stage == 0 && r5.ChargedTier == 1);
Check("resolve charged tier at least 1", Resolver.Resolve(new ReleaseFacts { Build = Build.From(new Orb[0]), ChargeActive = true }).ChargedTier == 1);
Check("resolve racked adds fifteen pellets", Resolver.Resolve(new ReleaseFacts { Build = burst, Ghosts = 15, Racked = true }).Plan.Pellets == 15);
Check("resolve pellets need the rack", Resolver.Resolve(new ReleaseFacts { Build = burst, Ghosts = 15 }).Plan.Pellets == 0);
Check("resolve burst owner early shot has three echoes", Resolver.Resolve(new ReleaseFacts { Build = burst, Crossed = true }).Plan.Echoes == 3);
Check("two ultimates: a snap is a Line", Resolver.Resolve(new ReleaseFacts { Build = both, SnapReady = true }).Ultimate == UltimateUse.Line);
Check("two ultimates: armed is the beam, ghosts absorbed", Resolver.Resolve(new ReleaseFacts { Build = both, HolyArmed = true, ChargeActive = true, Ghosts = 5 }) is var r6
      && r6.Ultimate == UltimateUse.Beam && r6.Plan.Stage == 0 && r6.ChargedTier == 3);
Check("two ultimates: armed beats a snap", Resolver.Resolve(new ReleaseFacts { Build = both, HolyArmed = true, SnapReady = true }).Ultimate == UltimateUse.Beam);
Check("two ultimates: a held shot is normal", Resolver.Resolve(new ReleaseFacts { Build = both, ChargeActive = true, Ghosts = 5 }).Ultimate == UltimateUse.None);
Check("resolve ghosts capped at fifteen", Resolver.Resolve(new ReleaseFacts { Build = b6, Ghosts = 40 }).Plan.Ghosts == 15);

// only one ultimate can be bought (rule 9); SkillStatus ints are the game's
Check("ultimate free when none owned", !UltimateRule.Blocks(Orb.HolyArrow, Ultimates.None));
Check("second ultimate blocked", UltimateRule.Blocks(Orb.HolyArrow, Ultimates.Quickdraw4) && UltimateRule.Blocks(Orb.Quickdraw4, Ultimates.HolyArrow)
      && UltimateRule.Blocks(Orb.SeekingVolley, Ultimates.HolyArrow));
Check("an owned ultimate is never blocked", !UltimateRule.Blocks(Orb.HolyArrow, Ultimates.HolyArrow)
      && !UltimateRule.Blocks(Orb.HolyArrow, Ultimates.HolyArrow | Ultimates.Quickdraw4));
Check("other orbs never blocked", !UltimateRule.Blocks(Orb.Burst, Ultimates.HolyArrow | Ultimates.Quickdraw4));
Check("allowed becomes cannot buy, testing stays testing", UltimateRule.Downgrade(9) == 3 && UltimateRule.Downgrade(8) == 2
      && UltimateRule.Downgrade(7) == 7 && UltimateRule.Downgrade(1) == 1);

// Holy Arrow arms only on a live draw (Part 5)
Check("holy arms on the live draw", HolyGate.MayArm(true, true, 42u, 42u));
Check("holy refuses a swapped arrow", !HolyGate.MayArm(true, true, 43u, 42u));
Check("holy refuses an empty string", !HolyGate.MayArm(true, true, 0u, 0u));
Check("holy refuses a released string", !HolyGate.MayArm(false, true, 42u, 42u));
Check("holy refuses without a crossing", !HolyGate.MayArm(true, false, 42u, 42u));

// the draw clock: arrow at the crossing, a dip grace, and a reset on every grab (the 10961 ms bug)
var g = new DrawClock();
g.Update(0.9f, 7u, 1f, 0.35f);
Check("clock remembers the arrow at the crossing", g.ArrowAtCrossing == 7u && g.Active(1f, 0.35f));
g.Update(0.5f, 7u, 2f, 0.35f);
Check("clock stays active through a short dip", g.Active(2.2f, 0.35f) && !g.Active(2.4f, 0.35f));
g.Update(0.9f, 7u, 2.2f, 0.35f);
Check("clock short dip resumes the span", g.TryElapsed(2.5f, DrawClock.Grace, out float d1) && Near(d1, 1.5f));
g.Update(0.5f, 7u, 3f, 0.35f); g.Update(0.9f, 8u, 3.4f, 0.35f);
Check("clock long dip starts a new span", g.TryElapsed(3.5f, DrawClock.Grace, out float d2) && Near(d2, 0.1f) && g.ArrowAtCrossing == 8u);
g.Reset();
Check("clock reset forgets everything", !g.TryElapsed(3.5f, DrawClock.Grace, out _) && g.ArrowAtCrossing == 0u && !g.Active(3.5f, 0.35f));
var h = new DrawClock(); h.Update(0.9f, 5u, 0f, 0.35f); h.Reset(); h.Update(0.3f, 5u, 10f, 0.35f);
Check("clock after a re-grab a light shot has no time", !h.TryElapsed(10.5f, DrawClock.Grace, out _));
```

That is 55 checks.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `VolleyTimes` not defined.

- [ ] **Step 3: Implement `Release.cs` and the `DrawClock` changes**

The tests fix most bodies; only these choices are left open:
- `Ghosts` (the boundary checks at 1.404/1.405 s need this exact form): 0 unless `volleyTier >= 1 && volleyCharged` (the game's charged state is the authority for the first five, whatever `t` says); then 5; with `volleyTier >= 2` and `t + 1e-5 >= FullAt`, add `min(10, 1 + floor((t - FullAt + 1e-5) / Interval))`, computed in `double`. `Racked` uses the same `1e-5`. `BeatsBetween` fires a beat when `from < at && at <= to`, so consecutive frames never fire it twice.
- `Resolver.Resolve`, first match wins: (1) `HolyArmed` → `Charged`, `Beam`, empty plan, tier `max(1, Build.Charge)`; (2) `SnapReady` → `Snap`, `Line` if `Build.Ultimates` has `Quickdraw4` else `None`, echoes `Build.Volley`, tier 0; (3) `Ghosts > 0` → `Charged` if `ChargeActive` else `Held`, ghosts `min(Ghosts, 15)`, pellets `Racked ? 15 : 0`, tier `ChargeActive ? max(1, Build.Charge) : 0`; (4) `ChargeActive` → `Charged`, empty plan, tier `max(1, Build.Charge)`; (5) `Early`, echoes `Build.Volley` when `Crossed`, else an empty plan; tier 0.
- `DrawClock.Update(pull, arrowId, now, dipGrace)`: when `arrowId != 0 && pull > CrossingPull`: no span → start (`crossedAt = now`, `endedAt = NaN`, `ArrowAtCrossing = arrowId`); ended span with `now - endedAt <= dipGrace` → resume (`endedAt = NaN`); ended longer → start. Otherwise an active span ends at `now`. The ten existing clock checks in `RuleTests.cs` still pass through the old overload.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (176)`.

- [ ] **Step 5: Checkpoint**

Tests `ALL PASS (176)`; build `0 Error(s)`. Files changed: `Release.cs`, `Spectral.cs` (empty), `Rules.cs`, `QuickshotRing.Tests/ReleaseTests.cs`, `.csproj`, `Program.cs`.

---

### Task 3: Spectral rules (Unity-free)

**Files:**
- Modify: `QuickshotRing/Spectral.cs`, `QuickshotRing.Tests/Program.cs` (call `SpectralTests.Run()`)
- Create: `QuickshotRing.Tests/SpectralTests.cs`

**Interfaces:**
- Consumes: the game's flight: each physics tick the arrow's velocity is set to its flight speed in its current direction, then gravity turns it (`_decomp/Arrow.cs:223-233`, `_decomp/ArrowFlightSettings.cs:65-71`); every real arrow prefab logged in play-test 1 has the newer-precision flag, gravity on and drag 0 (client `Latest.log` 23:10:34). The continuous form of that scheme: speed `s` constant, elevation `θ` with `dθ/dt = -(g/s)·cos θ`. With `k = g/s` and `u = gd⁻¹(θ)` (`gd⁻¹(θ) = ln(tan θ + sec θ)`), `u(t) = u0 - k·t`, `along(t) = (s/k)·(gd(u0) - gd(u0 - k·t))`, `up(t) = (s/k)·(ln cosh u0 - ln cosh(u0 - k·t))`, velocity `(s·sech u, s·tanh u)`, `gd(u) = atan(sinh u)`.
- Produces (namespace `QuickshotRing`, `internal`):
  - `static class ArcModel { const double MaxElevation /* 89.9 degrees in radians */; static void Point(double speed, double elevation, double t, double g, out double along, out double up); static void Velocity(double speed, double elevation, double t, double g, out double along, out double up); static double TimeToAlong(double speed, double elevation, double along, double g) /* NaN if never reached */; }`, elevation clamped to ±`MaxElevation` inside every call.
  - `enum SpectralKind { Inner, Outer, Echo, Pellet }` (here, not in a Unity file, so `SpectralLife` can use it).
  - `static class SpectralShares { const float Echo = 0.10f; }`
  - `static class SpectralLife { static bool Ended(SpectralKind kind, float distanceFromStart, float age, float range, float life); }` (`age >= life`, or a pellet at `distanceFromStart >= range`)
  - `static class BurstPattern { const int Count = 15, Inner = 5; const float InnerDegrees = 2.5f, OuterDegrees = 5f, PelletShare = 0.04f, Range = 30f; static void Pellet(int index, float innerDegrees, float outerDegrees, out float angleDegrees, out float azimuthDegrees); static int Owner(int index) /* index % 5 */; }`: pellets 0 to 4 at `innerDegrees`, azimuth `(90 + 72·i) % 360`; pellets 5 to 14 at `outerDegrees`, azimuth `36·(i - 5)`.
  - `static class SpectralMath { static void Split(float shares, out float effectiveness, out float extraMultiplier) /* e = min(1, shares), x = shares / e, both 0 when shares <= 0 */; static int Densest(IList<float> xs, IList<float> ys, IList<float> zs, float radius) /* index with most other points within radius, first on ties; -1 when empty */; }`
  - `sealed class HitBatch { void Add(int key, float share); int Count { get; } IEnumerable<int> Keys { get; } float Sum(int key); int Hits(int key); void Clear(); }`
  - `sealed class ReleaseLedger { const int Cap = 64; int OpenCount { get; } void Open(uint release, int arrows); void Add(uint release, int creature, float share); bool ArrowDone(uint release) /* true exactly when the last open arrow ends */; Dictionary<int, float> Take(uint release) /* removes it; empty if unknown */; }`. `Open` on a full ledger drops the oldest-opened release first.
  - `static class SpectralBudget { static bool CastThisTick(int live, int cap, int index, int tick) /* live <= cap, or (index + tick) % 2 == 1 */; }`

- [ ] **Step 1: Write the failing tests in `SpectralTests.cs`**

```csharp
// one flight model (rule 6): constant speed, gravity turns the direction
ArcModel.Point(50, 0, 0, 9.81, out double x0, out double y0);
Check("arc starts at the origin", x0 == 0 && y0 == 0);
ArcModel.Point(50, 0, 0.01, 9.81, out double x1, out double y1);
Check("arc leaves along its line at its speed", Math.Abs(x1 - 0.5) < 1e-3 && Math.Abs(y1) < 1e-3);
double t30 = ArcModel.TimeToAlong(50, 0, 30, 9.81);
ArcModel.Point(50, 0, t30, 9.81, out double x30, out double y30);
Check("arc level 30 m shot drops like ballistics", Math.Abs(x30 - 30) < 1e-3 && -y30 > 1.70 && -y30 < 1.85);
bool constant = true;
for (int i = 0; i <= 40; i++) { ArcModel.Velocity(50, 0.3, i * 0.05, 9.81, out double vx, out double vy); if (Math.Abs(Math.Sqrt(vx * vx + vy * vy) - 50) > 1e-6) constant = false; }
Check("arc keeps a constant speed", constant);
ArcModel.Velocity(50, 0.3, 2, 9.81, out _, out double vy2);
Check("arc turns downward", vy2 < 50 * Math.Sin(0.3));
ArcModel.Point(50, Math.PI / 2, 1, 9.81, out double xu, out double yu);
Check("arc straight up stays finite", !double.IsNaN(xu) && !double.IsNaN(yu) && yu > 40);
Check("arc never reaches an impossible distance", double.IsNaN(ArcModel.TimeToAlong(10, 0, 1000, 9.81)));

// Burst's fixed daisy, identical on every machine
BurstPattern.Pellet(0, 2.5f, 5f, out float a0, out float z0);
Check("pellet 0 is the inner ring, straight up", Near(a0, 2.5f) && Near(z0, 90f));
bool innerOk = true;
for (int i = 0; i < 5; i++) { BurstPattern.Pellet(i, 2.5f, 5f, out float a, out float z); if (!Near(a, 2.5f) || !Near(z, (90f + 72f * i) % 360f)) innerOk = false; }
Check("inner five even at 2.5 degrees", innerOk);
bool outerOk = true;
for (int i = 5; i < 15; i++) { BurstPattern.Pellet(i, 2.5f, 5f, out float a, out float z); if (!Near(a, 5f) || !Near(z, 36f * (i - 5))) outerOk = false; }
Check("outer ten even at 5 degrees", outerOk);
Check("fifteen pellets, five inner", BurstPattern.Count == 15 && BurstPattern.Inner == 5);
Check("each pellet is shed by an inner ghost", BurstPattern.Owner(0) == 0 && BurstPattern.Owner(7) == 2 && BurstPattern.Owner(14) == 4);
Check("burst defaults are the spec's", BurstPattern.InnerDegrees == 2.5f && BurstPattern.OuterDegrees == 5f && BurstPattern.PelletShare == 0.04f
      && BurstPattern.Range == 30f && SpectralShares.Echo == 0.10f);
double tan5 = Math.Tan(5 * Math.PI / 180), tan25 = Math.Tan(2.5 * Math.PI / 180);
// spec: all 15 pellets on a 1 m creature to about 6 m, the inner 5 to about 12 m
Check("outer pellets cover a 1 m creature to about 6 m", 0.04 + 6 * tan5 <= 0.6 && 0.04 + 7.5 * tan5 > 0.6);
Check("inner pellets to about 12 m", 0.04 + 11 * tan25 <= 0.55);
Check("pellet ends at 30 m", !SpectralLife.Ended(SpectralKind.Pellet, 29.9f, 1f, BurstPattern.Range, 3f)
      && SpectralLife.Ended(SpectralKind.Pellet, 30f, 1f, BurstPattern.Range, 3f));
Check("ghost ignores range", !SpectralLife.Ended(SpectralKind.Inner, 100f, 1f, BurstPattern.Range, 3f)
      && !SpectralLife.Ended(SpectralKind.Echo, 100f, 1f, BurstPattern.Range, 3f));
Check("everything ends at LifeSeconds", SpectralLife.Ended(SpectralKind.Inner, 0f, 3f, 30f, 3f) && SpectralLife.Ended(SpectralKind.Outer, 0f, 3f, 30f, 3f)
      && SpectralLife.Ended(SpectralKind.Echo, 0f, 3f, 30f, 3f) && SpectralLife.Ended(SpectralKind.Pellet, 0f, 3f, 30f, 3f));

// spectral hits: share as effectiveness (Part 10), batched per creature per tick, experience per creature per release
SpectralMath.Split(0.15f, out float e1, out float m1);
Check("split small share is the effectiveness", Near(e1, 0.15f) && Near(m1, 1f));
SpectralMath.Split(1.6f, out float e2, out float m2);
Check("split large share keeps effectiveness at 1", Near(e2, 1f) && Near(m2, 1.6f));
SpectralMath.Split(0f, out float e3, out float m3);
Check("split nothing", e3 == 0f && m3 == 0f);
var batch = new HitBatch(); batch.Add(1, 0.15f); batch.Add(1, 0.06f); batch.Add(2, 0.04f);
Check("batch sums per creature", batch.Count == 2 && batch.Hits(1) == 2 && Near(batch.Sum(1), 0.21f) && Near(batch.Sum(2), 0.04f));
batch.Clear();
Check("batch clears", batch.Count == 0 && batch.Hits(1) == 0 && batch.Sum(1) == 0f);
var led = new ReleaseLedger(); led.Open(10, 3); led.Add(10, 5, 0.15f); led.Add(10, 5, 0.06f); led.Add(10, 6, 0.04f);
Check("ledger waits for the last arrow", !led.ArrowDone(10) && !led.ArrowDone(10) && led.ArrowDone(10));
var took = led.Take(10);
Check("ledger sums per creature", took.Count == 2 && Near(took[5], 0.21f) && Near(took[6], 0.04f));
Check("ledger forgets a taken release and ignores unknown ones", led.Take(10).Count == 0 && !led.ArrowDone(99));
for (uint i = 100; i < 300; i++) led.Open(i, 1);
Check("ledger stays bounded", led.OpenCount <= ReleaseLedger.Cap);
Check("densest landing is in the cluster", SpectralMath.Densest(new[] { 0f, 10f, 10.3f, 10.1f }, new[] { 0f, 0f, 0f, 0.2f }, new[] { 0f, 0f, 0f, 0f }, 1f) >= 1);
Check("densest of one point", SpectralMath.Densest(new[] { 3f }, new[] { 0f }, new[] { 0f }, 1f) == 0);
Check("budget casts every tick under the cap", SpectralBudget.CastThisTick(150, 200, 7, 0) && SpectralBudget.CastThisTick(150, 200, 7, 1));
Check("budget alternates over the cap", SpectralBudget.CastThisTick(250, 200, 7, 2) && !SpectralBudget.CastThisTick(250, 200, 7, 1)
      && SpectralBudget.CastThisTick(250, 200, 8, 1));
```

That is 31 checks (`using System; using QuickshotRing; using static Program;`).

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `ArcModel` not defined.

- [ ] **Step 3: Implement `Spectral.cs`** as the Interfaces block states. `TimeToAlong`: target `w = gd(u0) - along·k/s`; return NaN when `w <= -π/2 + 1e-9`, else `t = (u0 - gd⁻¹(w)) / k`. For `g <= 0` fall back to a straight line (`along = s·cos θ·t`, `up = s·sin θ·t`).

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (207)`.

- [ ] **Step 5: Checkpoint**

Tests `ALL PASS (207)`; build `0 Error(s)`. Files changed: `Spectral.cs`, `QuickshotRing.Tests/SpectralTests.cs`, `Program.cs`.

---

### Task 4: Build cache, the test build, ownership from the build

**Files:**
- Modify: `QuickshotRing/Build.cs` (append `SlotIds`, `TestBuildRule`, `SkillLevels`), `QuickshotRing/Builds.cs` (whole file), `QuickshotRing/Tree.cs:26-28` (use `SlotIds`), `Tree.cs:70-74` (delete `HasQuickdraw4`, `HasQuiverGather`), `Tree.cs` (add `SlotOf`), `QuickshotRing/QuiverGather.cs:55-58`, `QuickshotRing/Quickdraw4.cs:62`, `:92-98`, `QuickshotRing/Main.cs:57-63` (`TestBuild.Init()` before `Cfg.InitWire()`), `QuickshotRing.Tests/Program.cs` (call `BuildTests.Run()`)
- Create: `QuickshotRing/TestBuild.cs`, `QuickshotRing.Tests/BuildTests.cs`

**Interfaces:**
- Consumes:
  - `Builds.ServerRead(Player)` today (`Builds.cs:28-49`): owned slots (`PathExperience.UnlockedSlots`, `_decomp/PathExperience.cs:38`) plus `PracticingOrb` (`_decomp/PlayerProgressionManager.cs:70`), looked up with `TryGetValue` (`Builds.cs:31-32`).
  - Refresh points: `PlayerProgressionManager.UnlockSlot(ProgressionSlot, bool, bool)` (`PlayerProgressionManager.cs:326-350`), `PracticeSlot(SkillOrb)` (`:352-382`), `EndPractice()` (`:384-411`), `LockSlot(ProgressionSlot)` (`:413-435`), `LoadFrom(PlayerSave)` (`:183-193`); `PathExperience.LockSlot(ProgressionSlot, bool)` (`PathExperience.cs:237-261`), reached also from `RemoveFreePoints`/`RemoveSlots` (`:336-395`) with no manager call; its owner is the private field `targetPlayer` (`PathExperience.cs:18`).
  - Game skill levels: private `Dictionary<uint, int> skillLevel` (`PlayerProgressionManager.cs:49`); a level reaching 1 calls `skill.Unlock(pm)`, which registers an input skill with the player's tool helper (`_decomp/PickupBasedSkill.cs:182-189`); a level reaching 0 calls `Lock` (`:191-198`). `PracticeSlot` does exactly this for a tried orb (`PlayerProgressionManager.cs:370-377`).
  - Client cache keyed by player name (`Builds.cs:18`, `:103-106`); `Player.Current` (as `Main.cs:154`).
  - `Probe.RevampedSkillsLoaded` (`Probe.cs:28`).
- Produces:
  - `Build.cs`: `static class SlotIds` (constants from "Decisions made here", plus `Quickdraw4 = 2718281828u`, `QuiverGather = 1618033988u`, `const int QuiverSkill = 1414213562`, `VolleySkill = 47011u`, and `static IReadOnlyDictionary<Orb, uint> New` with the 12 new orbs); `static class TestBuildRule { static List<Orb> Parse(string? text, out List<string> unknown); static bool AppliesTo(string? configured, string? player); }` (names trimmed, case-insensitive, distinct; `None` counts as unknown); `static class SkillLevels { static List<uint> Dropped(IReadOnlyDictionary<uint, int> before, IReadOnlyDictionary<uint, int> after); }` (keys with a positive level before and none or 0 after, ascending). No "except the sold skill": several slots now share one skill (Two-Shot, Three-Shot and Burst all point at Volley), and on the shrine path the manager has already lowered the sold skill's level before `PathExperience.LockSlot` runs (`_decomp/PlayerProgressionManager.cs:413-434`), so the sold skill can legitimately be the one left registered at level 0.
  - `SlotIds.VolleySkill` stays `uint` (the claim and tree use `uint`); the game's own id field is `protected int hash` (in `Alta.Values.dll`, not in `_decomp`), so Task 8 sets it with `Convert.ChangeType`, never by passing the `uint`.
  - `Builds`: `static Build ServerBuild(Player? p)` (owned + practising + test build, cached per `PlayerProgressionManager`; `default(Build)` for null); `static Ultimates ServerOwnedUltimates(PlayerProgressionManager? pm)` (owned + test build, no practice; `Ultimates.None` for null); `static void Refresh(PlayerProgressionManager pm)`; `static Build ClientBuild(string? playerName)`; `static Build LocalBuild()` (`ClientBuild(Player.Current?.UserInfo?.Username)`); `static string NameOf(IPlayer?)` becomes `internal`.
  - `TestBuild`: `static void Init()`; `static IReadOnlyList<Orb> ExtraFor(string? playerName)`; `static bool ActiveFor(string? playerName)`; `static void Sync(PlayerProgressionManager pm, IEnumerable<Orb> realOwned)` (grants and revokes game skills); `static void Reapply(PlayerProgressionManager pm)` (re-adds granted levels after the game rebuilt `skillLevel`).
  - `Tree.SlotOf(Orb) -> ProgressionSlot?` (reverse of `OrbBySlot`).

- [ ] **Step 1: Write the failing tests in `BuildTests.cs`**

```csharp
var ids = SlotIds.New;
Check("twelve new orbs have slot ids", ids.Count == 12 && ids.ContainsKey(Orb.Burst) && ids.ContainsKey(Orb.SeekingVolley) && !ids.ContainsKey(Orb.Quick1));
Check("new slot ids are distinct and above every game slot id", new HashSet<uint>(ids.Values).Count == 12 && ids.Values.All(v => v > 65535u));
Check("new slot ids avoid the mod's own", !ids.Values.Contains(SlotIds.Quickdraw4) && !ids.Values.Contains(SlotIds.QuiverGather)
      && !ids.Values.Contains((uint)SlotIds.QuiverSkill));
Check("volley skill id fits 16 bits", SlotIds.VolleySkill > 0u && SlotIds.VolleySkill <= 65535u);
Check("test build parse", TestBuildRule.Parse("Quickdraw4; HolyArrow ;charge3", out var unk1) is var o1 && o1.Count == 3 && o1.Contains(Orb.Charge3) && unk1.Count == 0);
Check("test build reports unknown names", TestBuildRule.Parse("Quickdraw5;Burst", out var unk2).Count == 1 && unk2.Count == 1 && unk2[0] == "Quickdraw5");
Check("test build empty and None", TestBuildRule.Parse("  ", out _).Count == 0 && TestBuildRule.Parse(null, out _).Count == 0
      && TestBuildRule.Parse("None", out var unk3).Count == 0 && unk3.Count == 1);
Check("test build names one player", TestBuildRule.AppliesTo("Alice", "alice") && !TestBuildRule.AppliesTo("Alice", "Bob")
      && !TestBuildRule.AppliesTo("", "Alice") && !TestBuildRule.AppliesTo("Alice", null));
var before = new Dictionary<uint, int> { { 1, 1 }, { 2, 2 }, { 3, 1 } };
var after = new Dictionary<uint, int> { { 2, 1 } };
Check("dropped skills found", SkillLevels.Dropped(before, after) is var dr && dr.Count == 2 && dr[0] == 1 && dr[1] == 3);
Check("a shared skill dropped to 0 is found", SkillLevels.Dropped(new Dictionary<uint, int> { { 47011, 1 } }, new Dictionary<uint, int>()) is var ds
      && ds.Count == 1 && ds[0] == 47011u);
```

That is 10 checks (`using System.Collections.Generic; using System.Linq; using QuickshotRing; using static Program;`).

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `SlotIds` not defined.

- [ ] **Step 3: Implement the three classes in `Build.cs`** as the Interfaces block states.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (217)`.

- [ ] **Step 5: `TestBuild.cs`**

Category `TestBuild`, entries `Player` and `Orbs` (both `""`, not shared), descriptions, exactly: `Player`: `Server only, for play-tests. The username that gets the extra orbs below. Empty = off.`; `Orbs`: `Server only, for play-tests. Orb names separated by ';' (Quick1-3, Quickdraw4, Volley, FullVolley, Burst, Charge1-3, HolyArrow, Gatherer, QuiverGather, ...) the player is treated as owning. Never saved; empty = off.` On `Init` (and on `Cfg.Reloaded`) parse, and if active log the warning below; log unknown names; then `Sync` every manager that has grants or a cached build. Clients ignore both settings (`NetworkSceneManager.IsServer` gate everywhere).

`Sync(pm, realOwned)`: wanted = `ExtraFor(name)` minus `realOwned`, mapped through `Tree.SlotOf`. Do not subtract the practising orb: `Build.From` is set-based, so a lent orb that is also being tried only counts one extra level, while revoking it at `PracticeSlot` and re-granting it at `EndPractice` would re-run `Unlock` on a bow in hand (`PickupBasedSkill.Unlock` → `RegisterSkill` → `ValidatePickupHandler` re-activates every active skill, so each input fires twice and `BowDrawSkill.Register` logs `Already have input`, `_decomp/PlayerToolBasedSkillHelper.cs:38-59`, `:125-149`). Orbs (orbs with no slot yet only change the build); grant each wanted slot not yet granted (`skillLevel[slot.Skill.Hash]++`; at 1, `slot.Skill.Unlock(pm)`); revoke each granted slot no longer wanted (decrement; at 0 remove the key, then `slot.Skill.Lock(pm)`). Keep `Dictionary<PlayerProgressionManager, List<ProgressionSlot>> granted`. `Reapply(pm)`: for each granted slot `skillLevel[hash]++` without calling `Unlock` (registration survived; only the count was cleared). Reach `skillLevel` with `AccessTools.FieldRefAccess<PlayerProgressionManager, Dictionary<uint, int>>("skillLevel")`.

Log lines, exactly:
- warning, at `Init` and every reload while active: `[Archery] TEST BUILD ACTIVE: '{player}' is treated as also owning {orbs joined "+"}. Nothing is saved. Clear [TestBuild] Orbs in the server's QuickshotRing.cfg to stop.`
- warning: `[Archery] TEST BUILD: unknown orb name(s) {list}; known: {all Orb names except None}`
- `[Archery] TEST BUILD: granted the game's '{skill.name}' to '{player}' (level {n}; holding {k} item(s))` and `[Archery] TEST BUILD: took back the game's '{skill.name}' from '{player}' (level {n}; holding {k} item(s))`, where `k = pm.GetComponent<PlayerToolBasedSkillHelper>()?.HeldInteractables.Count ?? 0` (public, `_decomp/PlayerToolBasedSkillHelper.cs:12`). A grant with `k > 0` is the case that can double-register; the play-test asks the user to put the bow away before each test-build change.
- once when cleared: `[Archery] TEST BUILD: off`

- [ ] **Step 6: The build cache in `Builds.cs`**

`ServerBuild(p)` returns the cached build for `p.ProgressionManager`, computing it on a miss as today's `ServerRead` plus `TestBuild.ExtraFor(name)` (the existing "Build: ... =" line gains `, TEST BUILD +{orbs}` when extras applied). Postfixes (all server only, all calling `Refresh(pm)`, which recomputes, calls `TestBuild.Sync` and logs on change): `UnlockSlot`, `LockSlot`, `PracticeSlot`, `EndPractice`, `LoadFrom`, and `PathExperience.LockSlot` (owner from `___targetPlayer`). `Cfg.Reloaded` clears the cache. Trim destroyed managers when the cache passes 64 entries. `Write` (`Builds.cs:74-89`) uses `ServerBuild`. `LoadFrom`'s postfix also logs, as an error when `ServerOwnedUltimates` (real slots only, test build excluded for this line) holds two flags: `[Archery] Build: '{name}' OWNS TWO ULTIMATES in the save ({ultimates}); both stay active and the moment of release decides`. Each new postfix logs once `[Archery] Build: refresh patch on {Type}.{Method} is live`.

- [ ] **Step 7: Ownership consumers**

- `QuiverGather.cs:58`: `Builds.ServerBuild(owner.NetworkPlayer).QuiverGather`; before it, when `Probe.RevampedSkillsLoaded`, return and log once `[Archery] Quiver: RevampedSkills is loaded; Quiver Gather stands down and RevampedSkills returns arrows`.
- `Quickdraw4.LocalPlayerHasIt()` (`Quickdraw4.cs:62`): `localHasIt || (Builds.LocalBuild().Ultimates & Ultimates.Quickdraw4) != 0`.
- `Quickdraw4.ServerShot.Prefix` (`Quickdraw4.cs:97`): `player != null && (Builds.ServerBuild(player).Ultimates & Ultimates.Quickdraw4) != 0` (Task 6 replaces this with the release).
- `Tree.HasQuickdraw4`, `Tree.HasQuiverGather` deleted; `Tree.Quickdraw4SlotHash`, `QuiverSlotHash`, `QuiverSkillHash` read `SlotIds`.

- [ ] **Step 8: Checkpoint**

Tests `ALL PASS (217)`; build `0 Error(s)`. Files changed: `Build.cs`, `Builds.cs`, `TestBuild.cs`, `Tree.cs`, `QuiverGather.cs`, `Quickdraw4.cs`, `Main.cs`, `QuickshotRing.Tests/BuildTests.cs`, `Program.cs`.

---

### Task 5: The shrine: one buyable ultimate, sale fixes, costs

**Files:**
- Create: `QuickshotRing/Shrine.cs`
- Modify: `QuickshotRing/Tree.cs:184-189` (call `Shrine.LogTree(tree)` at the end of `Inject`), `QuickshotRing/Probe.cs:43` (the progression line)

**Interfaces:**
- Consumes:
  - `PathExperience.IsSlotAvailable(ProgressionSlot)` (`_decomp/PathExperience.cs:165-192`) and `PlayerProgressionManager.IsSlotAvailable(ProgressionSlot)` (`_decomp/PlayerProgressionManager.cs:287-324`, which calls the first and maps `CantBuy` to `TestingCantBuy` for the orb being tried, `:303-305`). `PracticeSlot` lets a `CantBuy` orb be tried (`:358-359`: practice counts when the status is below `TestingLockedOwned`); `UnlockSlot` refuses a non-load unlock unless the path says `Allowed` (`:333-337`). `SkillOrb.Operate` buys at `Status >= TestingAllowed` and sells at `>= TestingOwned` (`_decomp/SkillOrb.cs:381-404`).
  - Whether a parent can be sold while a child is owned: `PathExperience.IsSlotAvailable` returns `OwnedLocked` (5, below the sell threshold 6) for such a parent unless `SettingsManager.GameConfig.Settings.CanSellSkillsWithChildren` (`_decomp/PathExperience.cs:167-181`, `_decomp/Alta.Utilities/GameSettings.cs:89`). It is `false` in this machine's `%APPDATA%\A Township Tale\GameConfiguration.json` (read while planning), so on the shrine path the cascade below happens only with that setting on; `RemoveFreePoints`/`RemoveSlots` reach it regardless.
  - Sales: `PlayerProgressionManager.LockSlot` (`:413-435`) decrements the count and the sold skill's level (removing the key at 0), then `PathExperience.LockSlot` (`PathExperience.cs:237-261`) removes children through `RecursiveLockSlot` (`:263-285`), calling `child.Skill.Lock` while the manager still counts the child, so `PickupBasedSkill.Lock` sees a level above 0 and does not deregister (`_decomp/PickupBasedSkill.cs:191-198`). `UpdateCounts()` (`PlayerProgressionManager.cs:571-590`) rebuilds the count and `skillLevel`; the private fields `currentlyOwnedSkillsCount` (`:59`) and `canBuyMoreSkills` (`:61`); the cap rule `MaxBoughtSkills < 1 || MaxBoughtSkills > count` (`:339`, `:430`).
  - `Builds.ServerOwnedUltimates`, `Builds.Refresh`, `TestBuild.Reapply` (Task 4); `UltimateRule` (Task 2); `SkillLevels.Dropped` (Task 4).
  - Damage tables: `BowDrawSkill.damageMultiplierPerLevel` (`_decomp/Alta.Progression.Skills/BowDrawSkill.cs:11`), `ValuePerLevel<T>.GetForLevel(int)` (`_decomp/Alta.Progression.Skills/ValuePerLevel.cs:13-17`, clamps to the table).
- Produces: `internal static class Shrine { static void LogTree(ProfessionSkillTree tree); }`; `Tree.ChargeMultiplier(int tier)` and `Tree.QuickMultiplier(int tier)` (`1f` at tier 0, else the table at that level; replace `Tree.SlowdrawMultiplier`, `Tree.cs:76-83`, and its callers `Volley.cs:253`, `HolyArrow.cs:128` with `Tree.ChargeMultiplier(Builds.ServerBuild(player).Charge)` for now).

- [ ] **Step 1: One buyable ultimate**

Postfix `[HarmonyPriority(Priority.Last)]` on both `IsSlotAvailable` methods with `ref SkillStatus __result` (server only): when `Tree.OrbBySlot` maps the slot to an orb and `UltimateRule.Blocks(orb, Builds.ServerOwnedUltimates(pm))`, set `__result = (SkillStatus)UltimateRule.Downgrade((int)__result)`. The path's owner is `___targetPlayer`. Prefix `[HarmonyPriority(Priority.First)]` on `PlayerProgressionManager.UnlockSlot` (it coexists with `Tree.GathererRefund`, `Tree.cs:229-244`): when `!isFromLoad` and the slot is blocked, return `false` and warn. Loading never checks this (both ultimates of a two-ultimate save load, spec Part 12 item 10).

Log lines, exactly:
- once per (player, orb, status) change: `[Archery] Shrine: '{name}' sees {orb} as {status} (was {original status}; free points {path.FreePoints}, cost {slot.Cost}): they already own {ultimates}; only one ultimate can be bought (it can still be tried)` (free points and cost, because the game's own `CantBuy` for too few points looks the same in the shrine)
- warning: `[Archery] Shrine: refused to unlock {orb} for '{name}': they already own {ultimates}`

- [ ] **Step 2: Sale fixes**

Prefix on `PathExperience.LockSlot` (server) stores, in `__state`, a copy of `skillLevel` and of `UnlockedSlots`. Postfix: removed = before minus after; if `pm.PracticingOrb != null` warn `[Archery] Sale: '{name}' sold {orb} while trying an orb; counts left to the game` and stop; else `pm.UpdateCounts()`, `TestBuild.Reapply(pm)`, recompute `canBuyMoreSkills` with the cap rule, then for each hash in `SkillLevels.Dropped(before, after)` (with `after` read after `UpdateCounts` and `Reapply`) find the skill among the sold and removed slots and call `skill.Lock(pm)` only when `pm.GetLevel(skill) == 0`. `Lock` on an already-deregistered skill is harmless (a missing key is a no-op in `PlayerToolBasedSkillHelper.DeregisterSkill` and `HeldPickupHandler.DeregisterSkill`, `_decomp/PlayerToolBasedSkillHelper.cs:151-158`, `_decomp/HeldPickupHandler.cs:99-104`). `Builds.Refresh(pm)`.

Log line, exactly: `[Archery] Sale: '{name}' sold {orb or slot name}; also removed: {list|none}; owned orbs {n} (the game had counted {m}), can buy more: {yes|no}; skills switched off: {list|none}`

- [ ] **Step 3: Costs and multipliers at load** (`Shrine.LogTree`, both sides)

- `[Archery] Tree: orb costs {Orb}={cost} {Orb}={cost} ...` (every mapped archery slot)
- `[Archery] Probe: damage by level 1-4: Quickdraw {a} {b} {c} {d}, Slowdraw {a} {b} {c} {d}` (Step 2 must confirm 1.25, 1.5, 1.75; Task 8 appends `, Volley {a} {b} {c} {d}`)
- `Probe.LogProgression` (`Probe.cs:43`) appends `, can sell with children: {SettingsManager.GameConfig.Settings.CanSellSkillsWithChildren}` to its `[Archery] Progression:` line (both sides; the server's value decides).

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (217)`; build `0 Error(s)`. Files changed: `Shrine.cs`, `Tree.cs`, `Volley.cs`, `HolyArrow.cs`, `Probe.cs`.

---

### Task 6: The draw record and the release

**Files:**
- Create: `QuickshotRing/Draws.cs`
- Modify: `QuickshotRing/Claims.cs:16-58` (clock moves out), `:63-103` (claim from the resolution; server relays its own), `QuickshotRing/Volley.cs:166-195` (`DrawGrace` moves to `Draws`), `:225-271` (`Fire` and `Launch` take a `Resolution`), `QuickshotRing/Quickdraw4.cs:92-98` (gate on the release), `QuickshotRing/Volley.cs` (add `Ghosts(BowString)`, `Racked(BowString)`, `ChargedOnClient(BowString)`, `RelaxGrace`)

**Interfaces:**
- Consumes:
  - `BowString.StringGrabbed(Interactable, Interactor)` (private, `_decomp/BowString.cs:208-227`); `HeldUpdate` (`:269-299`, the pull is `GetStrength(currentZ)`, `:287`); `OnShoot(Shooter, Pickup, Vector3)` (private, `:155-158`), which raises `OnShootingEntity` for every handler, so a postfix runs after every skill's `OnArrowShot`; `ExtraArrowUpdater.ShootExtraArrows` subscribes first (`_decomp/ExtraArrowUpdater.cs:63`, `:99`), so `Volley.Fire` runs before any skill resets its input.
  - Input state: `BowDrawSkill` private `inputs` (`BowDrawSkill.cs:15`); `ISkillInputMethod.IsActive`; `input.Handler.Inputs` (`_decomp/HeldPickupHandler.cs:18`); `ISkillInputMethod.Reset(bool)` (`_decomp/BowStringPullbackInputMethod.cs:83-87`, which sends `Cancelled` for an input still in range, `:112-130`); `HeldPickupHandler.Clear()` (`HeldPickupHandler.cs:64-73`), run when the held pickup's interaction ends (`_decomp/PlayerToolBasedSkillHelper.cs:61-99`; bow or string, to be settled by the log), with each input's `Reset` deferred one frame (`BowStringPullbackInputMethod.cs:44-52`). Expect its log line only when the user lets go of the bow. A `Cancelled` right after `Consumed` for one arrow is already vanilla (`BowDrawSkill.OnArrowShot` → `Reset(true)`; seen in play-test 1's server log), so resetting every input on a shot adds no new kind of message.
  - `CheckForPullback(float)` (private, `BowStringPullbackInputMethod.cs:93-110`; the dip branch runs only while `isInPullbackRange`, i.e. after the crossing).
  - `Tree.Quickdraw`, `Tree.Slowdraw`, `Tree.Holy` (`Tree.cs:30-32`), `HolyArrow.IsArmed` (`HolyArrow.cs:75`), `Builds.ServerBuild/LocalBuild` (Task 4), `Main.QuickshotReady` (`Main.cs:45`), `Resolver`, `ReleasePlan`, `DrawClock` (Task 2).
  - Claims: `incoming`/`outgoing` stores and `ShotClaim` (`Claims.cs:21-24`, `Wire.cs`).
- Produces:
  - `internal sealed class DrawRecord { readonly DrawClock Clock; Resolution? Last; uint LastArrow; float LastShotAt = -1; int GameResetsLogged; }`
  - `internal static class Draws { static DrawRecord Get(BowString bow); static float? Elapsed(BowString? bow); static int? ElapsedMs(BowString? bow); static bool InputActive(BowDrawSkill? skill, BowString bow); static Resolution ResolveServer(BowString bow, Player? owner); static Resolution ResolveLocal(BowString bow); static Resolution? ReleaseOf(uint arrowId); }`
  - `Claims.PutResolved(uint arrowId, Resolution r)` (server; only for ids marked by the `Shoot` prefix as a network shot), `Claims.TryRelay(uint arrowId, out ShotClaim? claim)` (client).
  - `Volley.Launch(BowString bow, NetworkEntity arrowEntity, Vector3 velocity, Resolution r, Ghost?[] held)`.

- [ ] **Step 1: Draw record, clock, re-grab reset**

Move the `HeldUpdate` postfix from `Claims.Clock` (`Claims.cs:35-58`) into `Draws`, calling `Clock.Update(pull, arrowId, Time.time, Volley.RelaxGrace)` with `arrowId = StringDock.QuantityStored > 0 ? StringDock.DockedPickup.Entity.Identifier : 0`. Postfix on `BowString.StringGrabbed` (both sides; on a client only for a local interactor): when the record's clock had a span (`TryElapsed(Time.time, DrawClock.Grace, out s)`), log `[Archery] Draw: string grabbed on '{bow.name}'; the last draw's clock ({ms} ms) was cleared`, then `Clock.Reset()`. `Claims.ClockMs` becomes `Draws.ElapsedMs`.

- [ ] **Step 2: `DrawGrace` from the crossing** (spec Part 12 item 5, Charge 1: "a dip of the string shorter than 0.35 seconds loses nothing")

Move the prefix (`Volley.cs:171-195`) into `Draws` with `[HarmonyPriority(Priority.Low)]` (after RevampedSkills' pull rewrite). Drop the "nothing worth protecting" exit (`Volley.cs:181`): every input in range, Quickdraw's included, waits `RelaxGraceSeconds` before a dip cancels it. The log names the skill by reference (`ReferenceEquals(___skill, Tree.Slowdraw)`, `Tree.Holy`, `Tree.Quickdraw`; Task 8 adds `VolleySkill.Skill`), never by type: `[Archery] Draw: {Slowdraw|Volley|Holy Arrow|Quickdraw} lost, the string stayed relaxed at {pull:0.00} (needs more than {value:0.00}) for {grace} s`. Update the `RelaxGraceSeconds` description: `From the 68% crossing on, the string must stay relaxed this long before any bow skill is lost (Quickdraw included, so a missed snap re-arms only after this). 0 = the game's instant cancel.`

- [ ] **Step 3: The release**

In `Volley.Fire` (`Volley.cs:225-239`), before anything else, resolve:
- server (`IsServer`): `Draws.ResolveServer(bow, owner)` with `owner = bow.Pickup.LastPlayerInteractor?.NetworkPlayer` and facts `SnapReady = InputActive(Tree.Quickdraw, bow)`, `ChargeActive = InputActive(Tree.Slowdraw, bow)`, `HolyArmed = HolyArrow.IsArmed(bow)`, `Ghosts = Volley.Ghosts(bow)`, `Racked = Volley.Racked(bow)`, `Crossed = Draws.Elapsed(bow) != null`, `Build = Builds.ServerBuild(owner)`; store `Last`, `LastArrow`, `LastShotAt` and `releases[arrowId]` (bounded 64); `Claims.PutResolved(arrowId, r)`.
- shooting client (the arrow's `LastPlayerInteractor` is local): `Draws.ResolveLocal(bow)` with `SnapReady = Main.QuickshotReady`, `ChargeActive = Volley.ChargedOnClient(bow)`, `HolyArmed = false`, ghosts and racked from `Volley`, `Crossed` from the local clock, `Build = Builds.LocalBuild()`.
- spectator: `Claims.TryRelay(arrowId, out c)` → `new Resolution(c.Kind, UltimateUse.None, ReleasePlan.From(c.Kind, c.Stage), 0)`; without a relay, the local facts.

`Launch` takes the resolution: ghost count `r.Plan.Ghosts`; spectral multiplier `r.ChargedTier > 0 ? Tree.ChargeMultiplier(r.ChargedTier) : 1f` (spec: "ghosts of an uncharged shot dealing their plain share (multiplier 1.0 in the log)"). Echoes and pellets are launched from Task 10; this task only logs them.

`Claims` changes: the client claim (`Claims.cs:72-81`) takes `Kind` and `Stage` from `Draws.ResolveLocal(bow).Plan` instead of `Main.QuickshotReady`/`Volley.Showing`; the server's `Shoot` prefix keeps writing `ServerCheck(got)` and marks the id; `PutResolved` replaces that with a copy whose `Kind` and `Stage` are the server's (time kept from the client). If no claim exists for the id (the shooter sent none: `SendClaims = false`, or a client without the mod), `PutResolved` creates one with `TimeMs = null` and the server's `Kind` and `Stage`, so spectators always get the server's decision. `Volley.Showing` is renamed `Ghosts` (with `Racked` returning `false` and `ChargedOnClient` returning `false` until Task 8).

`Quickdraw4.ServerShot.Prefix`: the Line fires when `Draws.Get(bowString).LastArrow == shotEntity.Identifier && Draws.Get(bowString).Last?.Ultimate == UltimateUse.Line` (add `NetworkEntity shotEntity` to the prefix's parameters).

Log line, exactly (server `Release:`, shooting client `Release (local):`, spectator `Release (seen):`):
`[Archery] Release: arrow {id} '{name}' {kind} t{ms|?} ult:{Line|Beam|-} {plan} spectral x{m:0.00} | build {build}{ | TEST BUILD}` (the middle is `Resolution.ToString()`'s kind, ult and plan)

- [ ] **Step 4: Reset every input when the arrow leaves; log the game's own clears**

Postfix `[HarmonyPriority(Priority.Last)]` on `BowString.OnShoot` (server): find the bow's handler through the first of `Tree.Quickdraw`, `Tree.Slowdraw`, `Tree.Holy` whose `inputs` holds the bow; note which inputs were still in range or active (by skill name), then call `Reset(true)` on every `handler.Inputs` value with a static `ours` flag set. Read the real arrow's `ImpactTool.OneOffMultiplier` (Hits.Tools max) after the skills ran.

Prefix `[HarmonyPriority(Priority.First)]` on `BowStringPullbackInputMethod.Reset(bool)` and postfix on `HeldPickupHandler.Clear()` (server): when not `ours`, the bow's record had a shot in the last 10 s, and fewer than 4 lines were logged for that shot, log the timing.

Log lines, exactly:
- `[Archery] Release: arrow {id} game set x{oneOff:0.###} on the real arrow; reset {n} inputs (still charging: {skills|none})`
- `[Archery] Draw: the game reset {skill.name} on '{bow}' {ms} ms after arrow {id} was shot`
- `[Archery] Draw: the game cleared the inputs on '{bow}' {ms} ms after arrow {id} was shot`

- [ ] **Step 5: Checkpoint**

Tests `ALL PASS (217)`; build `0 Error(s)`. Files changed: `Draws.cs`, `Claims.cs`, `Volley.cs`, `Quickdraw4.cs`.

---

### Task 7: Batched spectral hits and their experience

**Files:**
- Modify: `QuickshotRing/Hits.cs` (add the spectral path; `Describe` gains cripple), `QuickshotRing/Volley.cs:326-359` (`VolleySim` calls the new path and flushes)

**Interfaces:**
- Consumes:
  - `ImpactHitWithTool(ImpactTool, ImpactGenerator, ImpactDetector, float multipliers, float effectiveness, ImpactChannel, Vector3 point, Vector3 normal)` (`_decomp/Alta.Impact/ImpactHitWithTool.cs:51-74`), `.Hit()` (`:106-111`); `ImpactHit.HealthDamage = base × Effectiveness × DamageX × Multipliers × HealthMultiplier` and `CrippleDamage` with `CripplingMultiplier` (`_decomp/Alta.Impact/ImpactHit.cs:47-78`).
  - The game's immunity groups multiply effectiveness (`_decomp/Alta.Impact/ImpactTool.cs:677-680`): `ImpactGenerator.ImmunityGroups` (`_decomp/Alta.Impact/ImpactGenerator.cs:28`), `ImpactDetector.ImmunityGroups` (`_decomp/Alta.Impact/ImpactDetector.cs:145`), `ImpactImmunityGroup.Multiplier` (`_decomp/Alta.Impact/ImpactImmunityGroup.cs:15`).
  - Experience: `ImpactDetector.RunPlayerEvents` (`ImpactDetector.cs:268-278`) → `ProgressionSettings.ValidatePathHit(ImpactHitWithTool, bool isGettingExperience, out ProgressionPath)` (`_decomp/ProgressionSettings.cs:258`) → `CombatImpactProgressionFilter.ApplyImpact`, which sets the path to Ranged only inside its `isGettingExperience` branch (`_decomp/CombatImpactProgressionFilter.cs:9-16`) and then calls the protected non-virtual `BaseImpactChannelProgressionFilter.GainExperience(ImpactHitWithTool, ProgressionPath)` (`_decomp/BaseImpactChannelProgressionFilter.cs:14-21`), which adds `GetDamageExperience(hit.Effectiveness, path)` (`ProgressionSettings.cs:238`); `PlayerProgressionManager.GainExperience(IPlayer, ProgressionPath, float)` rounds once (`PlayerProgressionManager.cs:195-198`). `HealthObject.IsCombatant` (`_decomp/HealthObject.cs:171`), `ReceiveDamage(float, float, DamageData)` (`:294`).
  - `Hits.Apply` (`Hits.cs:92-135`), `cooldownField` (`:15`), `Shared` (`:137-143`); `SpectralMath`, `HitBatch`, `ReleaseLedger` (Task 3).
- Produces: `Hits.Spectral(ImpactTool tool, RaycastHit hit, float share, float chargeMultiplier, uint release, Player? shooter)`; `Hits.FlushSpectral()`; `Hits.OpenRelease(uint release, int arrows, Player? shooter, ImpactTool? tool)`; `Hits.ArrowDone(uint release)`; `VolleySim.Shot` gains `uint release; Player? shooter; float share, charge` (today's `multiplier`, `Volley.cs:306`, splits into the share and the charge multiplier).

- [ ] **Step 1: Queue and flush**

`Spectral` resolves the detector and best generator exactly as `Apply` does (players skipped, hardness and channel filters) and adds `share` to a per-tick `HitBatch` keyed by the detector's instance id, remembering the first hit's payload (tool, generator, channel, point, normal, release, shooter, charge multiplier). `FlushSpectral` (end of `VolleySim.FixedUpdate`): for each key, `Split(sum, out e, out x)`; multiply `e` by the product of the immunity multipliers shared by the generator and detector (each is `[Range(0f, 1f)]`, `_decomp/Alta.Impact/ImpactImmunityGroup.cs:12-15`, so the product never exceeds 1 and `e` stays in 0..1, as the game's own clamp at `ImpactTool.cs:676-683`); build the hit with `multipliers = bestTool.Damage × chargeMultiplier × x`, `effectiveness = e`, `CripplingMultiplier = 0` (spec Part 10: all stagger goes through Weight later; ghosts therefore knock creatures down less than in 0.7.0, which play-test row A10 tells the user); clear the tool's cooldown before and after (as `Hits.cs:122-126`); call `Hit()` with a static `inSpectral` flag set. Add the creature's sum to the release ledger when the detector has a combatant `HealthTarget`.

A prefix on `BaseImpactChannelProgressionFilter.GainExperience` (protected; patch by string name, `[HarmonyPatch(typeof(BaseImpactChannelProgressionFilter), "GainExperience")]`) returns `false` while `inSpectral` (experience comes from the ledger). Do not clear `isGettingExperience` in `ValidatePathHit` instead: the filter would then leave the path at Melee and the player's path events would report each spectral hit as a Melee hit. A postfix on `HealthObject.ReceiveDamage` counts calls per release while `inSpectral`.

- [ ] **Step 2: Experience per creature per release**

`OpenRelease` at launch (Volley's `Launch`, server) with the number of spectral arrows; `ArrowDone` when each ends (hit, life or range); when the ledger says the last arrow is done, `Take` it and for each creature `PlayerProgressionManager.GainExperience(shooter, ProgressionPath.Ranged, settings.GetDamageExperience(sum, ProgressionPath.Ranged))`, then log.

Log lines, exactly:
- `[Archery] Xp: arrow {id} '{creature}' shares {sum:0.000} -> {xp:0.###} Ranged xp ({hits} spectral hits, {xp / hits:0.###} each)`
- `[Archery] Volley: arrow {id} {landed}/{total} spectral arrows landed; {k} creature(s) hit in {b} damage events ({h} health changes); damage x{sumShares × charge:0.00} of the arrow; sim max {ms:0.00} ms/tick` (replaces `Volley.cs:355-358`; `sim max` is the largest `Stopwatch` time of `VolleySim.FixedUpdate` during the release)
- once per release, the tool: `[Archery] Volley: arrow {id} tool {Hits.Describe(tool)}`, where `Describe` (`Hits.cs:72-84`) adds `cripple {best part's CrippleDamageModifier:0.###}` to each part (spec Step 2: "the arrowhead's own cripple value")

`Hits.Apply` (Lines and the beam) is unchanged.

- [ ] **Step 3: Checkpoint**

Tests `ALL PASS (217)`; build `0 Error(s)`. Files changed: `Hits.cs`, `Volley.cs`.

---

### Task 8: The Volley skill

**Files:**
- Create: `QuickshotRing/VolleySkill.cs`
- Modify: `QuickshotRing/Tree.cs:95-189` (`Inject`: find once, re-point, Volley root, orb map; `:153` replaced by `ChargeSlots`), `:57-58` (`TwoShotSeconds`/`ThreeShotSeconds` defaults 0.75 and 1.0 and their new descriptions from the Settings table, since `DrawTimeline.TwoShotAfterSlowdraw`/`ThreeShotAfterSlowdraw` go), `:323-333` (`ApplyTimers`), `QuickshotRing/Volley.cs:47` (`OuterInterval` default 0.045, since `DrawTimeline.OuterInterval` goes), `QuickshotRing/Volley.cs:13-101` and `:147-220` (state from the Volley skill and the timeline; delete `ServerStage`, `ClientStage`, `Set`; keep `NoVanillaExtras`), `QuickshotRing/Main.cs:49-69` (registration at start, `OnSceneWasInitialized` retry, self-check), `QuickshotRing/Rules.cs:11-37` (delete `TwoShotAfterSlowdraw`, `ThreeShotAfterSlowdraw`, `OuterInterval`, `ExtrasShowing`), `QuickshotRing.Tests/Program.cs:17-27` and `:35-36` (delete those 11 checks), `QuickshotRing/Shrine.cs` (Volley in the multipliers line), `QuickshotRing/Builds.cs` (the Volley level line in the `LoadFrom` postfix)

**Interfaces:**
- Consumes:
  - `ProfessionSkill`'s per-skill content (`_decomp/ProfessionSkill.cs:105-120`): `targetAchievement` (completed on every unlock, `PathExperience.cs:228` → `CompleteTargetAchievement`, `ProfessionSkill.cs:152-158`), `staticContentPrefabs` and `dynamicContentPrefabs` (the shrine's try-out props, `_decomp/SkillShrineTestingArea.cs:283-298`), `practiceGlyphSprites` (`_decomp/ActionGlyphsPlacer.cs:19-36`). The private non-serialized `bowStrings`/`inputs` dictionaries (`BowDrawSkill.cs:13-15`) are not copied by `Instantiate`; the copy gets fresh ones from the field initializers.
  - Fields to clean on the copy: `PickupBasedSkill`'s transform and position events (`_decomp/PickupBasedSkill.cs:15-61`: `SkillChargedTransform`, `SkillExecutedTransform`, `SkillCanceledTransform`, `SkillChargingStartedTransform`, `hitPositionEffect`, `hitPositionAndBladeAlignedEffect`, `localPlayerChargedTransform`, `localPlayerExecutedTransform`, `localPlayerCanceledTransform`, `localPlayerHitPositionEffect`, `localPlayerHitPositionAndBladeAlignedEffect`), `inputStructure` (`:66`); `ProfessionSkill.skillPracticeTrigger` (`_decomp/ProfessionSkill.cs:117`; 0 means "this skill's own hash", `:128-138`, which `SkillShrine.SkillPracticed` compares, `_decomp/SkillShrine.cs:235-241`); the id field `hash` (found with `AccessTools.Field(typeof(ProfessionSkill), "hash")`, as `Tree.cs:290-291`); `BowDrawSkill.damageMultiplierPerLevel` (`BowDrawSkill.cs:11`), `ValuePerLevel<T>.valuePerLevel` (`ValuePerLevel.cs:11`).
  - The structure to copy: `BowStringPullbackStructure.pullbackValue`, `minTimeToPullback`, `isTimingOut`, `timeoutDuration` (`_decomp/BowStringPullbackStructure.cs:10-20`); its sound events `chargedDefaultEffect`, `executedDefaultEffect`, `canceledDefaultEffect` (`_decomp/SkillInputStructure.cs:6-12`, played at `PickupBasedSkill.cs:164`).
  - The skill table: `Alta.HashedGeneralValue<ProfessionSkill>.CheckItems()` and its private `items` dictionary (`Tree.cs:293-297`; the generic base is not in `_decomp`).
  - Glow: `ProfessionSkill.RequestToolEffect(NetworkEntity)` and `ReleaseToolEffect(NetworkEntity)` (protected virtual, `ProfessionSkill.cs:207-223`; Quickdraw opts out by overriding, `_decomp/Alta.Progression.Skills/BowQuickdraw.cs`); called for statuses on clients (`PickupBasedSkill.cs:127`, `:135`, `:143`).
  - Multiplier: `BowDrawSkill.OnArrowShot(BowString, NetworkEntity, Vector3)` (private, `BowDrawSkill.cs:63-78`) assigns `OneOffMultiplier` and calls `SendConsumed` (practice and the consumed status, `PickupBasedSkill.cs:200-207`).
  - State: `BowDrawSkill.InputChangedState(bool, ISkillInputMethod)` (`BowDrawSkill.cs:80-91`, server) with private `bowStrings` (`:13`); client statuses through `PickupBasedSkill.SendSerializedMessage` (`PickupBasedSkill.cs:98-167`, as `HolyArrow.Status`, `HolyArrow.cs:281-310`); the owner name via `Builds.NameOf(__0)`.
  - Two-Shot's modified skill: `PickupBasedSkillModifier.ModifiedSkill` (`_decomp/PickupBasedSkillModifier.cs:25`).
  - `VolleyTimeline`, `VolleyTimes` (Task 2); `Draws.Elapsed` (Task 6); `Builds.ServerBuild/ClientBuild/LocalBuild` (Task 4).
- Produces:
  - `internal static class VolleySkill { static BowSlowdraw? Skill; static BowStringPullbackStructure? Pull; static bool Registered; static bool TryRegister(string when); }`
  - `Tree.TwoShotSlot`, `Tree.ThreeShotSlot` (`ProgressionSlot?`), `Tree.SlowdrawThumpSeconds` (Slowdraw's `MinTimeToPullback`); `Tree.Slowdraw` is set only in `Inject`'s first pass and compared by reference afterwards.
  - `internal static IReadOnlyList<ProgressionSlot> Tree.ChargeSlots`: Slowdraw's own slots (skill is `Tree.Slowdraw` by reference), ordered by `GetIndentLevel(tree)`, recorded in the first slot loop before any re-point. It replaces `Tree.cs:153` (`tree.Slots.Where(s => s.Skill is BowSlowdraw)`), which after the re-point would also pick up Two-Shot, Three-Shot and Burst. The orb map uses `ChargeSlots[0..2]` for Charge1-3; Task 9 uses it for Holy Arrow's link and the `auto` layout.
  - `Volley`: `static VolleyTimes Times` (from settings and `Tree.SlowdrawThumpSeconds`); `static int Ghosts(BowString)`, `static bool Racked(BowString)`, `static bool ChargedOnClient(BowString)` (real bodies now); `static void ServerVolleyChanged(BowString bow, bool charged, Player? owner)`; `static void ClientVolleyStatus(BowString bow, ToolBasedSkillStatus status, string owner, bool local)`; `static void ClientChargeStatus(BowString bow, ToolBasedSkillStatus status)`.

- [ ] **Step 1: Make and register the copy** (`VolleySkill.TryRegister`, idempotent)

First line: `if (Registered) return true;`. Call `CheckItems()` (the game never warms the skill table itself, `_decomp/HashedGeneralValuesWarmer.cs:24-34`, so this may be its first load); stop with "not yet" unless the table holds at least 10 skills and one `BowSlowdraw` whose hash is not `SlotIds.VolleySkill` (this is the one type test, made before any copy exists). Stop with an error if `SlotIds.VolleySkill` is already in the table under another skill. Then: `Pull = Object.Instantiate(slowdraw's structure)` with `pullbackValue = 0.68`, `minTimeToPullback = [Archery] TwoShotSeconds`, `isTimingOut = false`, the three sound events set to fresh instances of their field types (`Activator.CreateInstance(field.FieldType)`); `Skill = Object.Instantiate(slowdraw)`, `name = "Bow Volley"`, the id set as `Tree.cs:290-291` does (`hash.SetValue(Skill, Convert.ChangeType(SlotIds.VolleySkill, hash.FieldType))`: the field is `protected int hash`, and passing the `uint` throws), `skillPracticeTrigger = 0`, `targetAchievement = null` (else buying Two-Shot, Three-Shot or Burst completes Slowdraw's achievement), every event field listed above set to a fresh instance, `inputStructure = Pull`, `damageMultiplierPerLevel.valuePerLevel = { 1f, 1f, 1f, 1f }`; `hideFlags = HideFlags.DontUnloadUnusedAsset` on both `Skill` and `Pull`; insert into `items` under the key `(uint)SlotIds.VolleySkill` (as `Tree.cs:294-297`). The try-out content (`staticContentPrefabs`, `dynamicContentPrefabs`, `practiceGlyphSprites`) is copied from Two-Shot's skill in Step 3, once `Inject` has found it. Attempts: `Main.OnInitializeMelon` (after `Cfg.Ready`, `"start"`), every `OnSceneWasInitialized` until registered (`"scene {name}"`), and a prefix on `ProfessionSkillTree.GetProfessionTree` (`"tree load"`, last resort, before `Inject`). That prefix runs on every experience gain (`PlayerProgressionManager.cs:202`, `:513`, `:577`), so it returns at once when `Registered` or `Tree.Ready`. `Inject` logs the error line and leaves Two-Shot and Three-Shot untouched if it is still not registered.

Log lines, exactly:
- `[Archery] Volley skill: registered '{name}' id {hash} at {when} (skill table {before} -> {after})`
- warning: `[Archery] Volley skill: not registered at {when}: {reason}`
- error: `[Archery] Volley skill: could not be made; Two-Shot and Three-Shot keep the game's skills and Burst is off`
- `[Archery] Volley skill: own pull settings: threshold {p}, {t} s, timeout {bool}; Slowdraw keeps threshold {p}, {t} s; separate settings objects: {yes|NO}; own input maps: {yes|NO}; achievement cleared: {yes|NO}; sounds cleared: {n}; events cleared: {n}` (`own input maps` = `!ReferenceEquals` of the copy's and Slowdraw's private `inputs` dictionaries)

- [ ] **Step 2: Guards on the copy**

- Create `[Archery] HideGameGlow` (bool, default `true`, not shared, description "Hide the game's whole-arrow glow on Slowdraw, Volley and Holy Arrow; the ghosts show the charge."). Prefix on `ProfessionSkill.RequestToolEffect` only (protected, so `[HarmonyPatch(typeof(ProfessionSkill), "RequestToolEffect")]`): return `false` when `HideGameGlow` and `__instance` is `Tree.Slowdraw`, `VolleySkill.Skill` or `Tree.Holy` (by reference). Leave `ReleaseToolEffect` alone: releasing a glow that was never requested is already a no-op (`ProfessionSkill.cs:191-205`), and blocking it would leave a glow stuck forever if the setting is switched on while one is showing. Log once: `[Archery] Glow: the game's whole-arrow glow is {hidden|shown} for Slowdraw, Volley and Holy Arrow`.
- Prefix/postfix on `BowDrawSkill.OnArrowShot` for `VolleySkill.Skill` only: store the shot arrow's tools' `OneOffMultiplier` values, restore them after. Log (server): `[Archery] Volley skill: consumed on arrow {id}; real arrow multiplier kept at x{m:0.###}` (spec Step 3: "the real arrow's one-off multiplier on a Volley-only charged shot").

- [ ] **Step 3: Tree: find once, re-point, Volley as a root**

In `Inject`'s first slot loop (`Tree.cs:104-111`), record `Tree.Slowdraw` (the `BowSlowdraw` slot whose skill is not `VolleySkill.Skill`), Two-Shot and Three-Shot as today (`:114-119`), their hashes, `Tree.ChargeSlots` (replacing `:153`), and log `[Archery] Tree: Two-Shot ({hash}) modified '{TwoShot.ModifiedSkill?.name ?? "-"}'; Three-Shot ({hash})`. Then, when `VolleySkill.Registered`: copy `staticContentPrefabs`, `dynamicContentPrefabs` and `practiceGlyphSprites` from Two-Shot's skill (`Tree.TwoShot`) onto `VolleySkill.Skill`, so trying a Volley orb shows Two-Shot's try-out props and glyphs, not Slowdraw's; set both slots' `skill` field to `VolleySkill.Skill`; make Two-Shot a root (remove it from every slot's `inheritSlots`, `dependencies = new List<uint>()`); keep Three-Shot depending on Two-Shot. Every later lookup compares by reference or slot id; add `VolleySkill.Skill` to the skills `Draws` searches for a bow's inputs (Task 6 Step 4) and names in its `DrawGrace` log (Task 6 Step 2), so a Volley-only archer's inputs are reset too. `ApplyTimers`: `Pull.minTimeToPullback = TwoShotSeconds` (the `TwoShot`/`ThreeShot` structure writes go). Log `[Archery] Tree: Two-Shot and Three-Shot now use the Volley skill; Two-Shot needs nothing first`. Save load logs, per player whose Volley level is above 0 (postfix in `Builds` on `LoadFrom`): `[Archery] Volley skill: level {n} for '{name}' after loading`. The same postfix logs an ERROR when the player owns `Tree.TwoShotSlot` or `Tree.ThreeShotSlot` and `pm.GetLevel(VolleySkill.Skill) == 0`: `[Archery] Volley skill: '{name}' owns Two-Shot/Three-Shot slots but the Volley level is 0 (the tree was not re-pointed before the save loaded)`.

- [ ] **Step 4: Volley state from the skill and the timeline**

`Volley.Times` = `new VolleyTimes(TwoShotSeconds, ThreeShotSeconds, OuterInterval, RippleSeconds, Tree.SlowdrawThumpSeconds, RackSeconds)` (new settings `[Archery] RippleSeconds` 1.5 and `RackSeconds` 2.0, shared; `TwoShotSeconds`, `ThreeShotSeconds` and `OuterInterval` get their new defaults and descriptions from the Settings table, migrated by Task 1). Server: postfix on `BowDrawSkill.InputChangedState` for `VolleySkill.Skill` → `ServerVolleyChanged(bow, isEnabled, input.Handler.PlayerHelper.Player)`. Clients: postfix on `PickupBasedSkill.SendSerializedMessage` (reading, not headless) for `VolleySkill.Skill` → `ClientVolleyStatus` (in `WaitForSpawn`, as `HolyArrow.cs:288-306`) and for `Tree.Slowdraw` → `ClientChargeStatus`. `Ghosts(bow)` = `VolleyTimeline.Ghosts(tier, charged, t, Times)` with, on the server, `tier = ServerBuild(owner).Volley` and `t = Draws.Elapsed(bow)`; on the shooting client, `LocalBuild().Volley` and the local clock; on a spectator, `ClientBuild(owner).Volley` and `t = now - chargingStartedAt` (or `VolleyAt` before now when only `Charged` was seen). `Racked` likewise. `DrawHeld` (`Volley.cs:365-404`) shows `Ghosts(bow)`; the Holy Arrow fold applies to whatever is showing (`n > 0`, not `n >= Total`, `Volley.cs:379`). The start-up self-check (`Main.cs:65-68`) uses `VolleyTimeline.Ghosts(3, true, 60f, VolleyTimes.Default) != VolleyMath.Total`.

Log line, on each change of the count on a bow: `[Archery] Volley: {armed|showing} {n} on '{bow}' (tier {v}, t {ms} ms)`.

- [ ] **Step 5: Retire the old timeline and its 11 checks, run the tests**

Delete `DrawTimeline.TwoShotAfterSlowdraw`, `ThreeShotAfterSlowdraw`, `OuterInterval`, `ExtrasShowing` and the checks at `Program.cs:17-27` and `:35-36` (Task 2's "the holy pause still comes after the rack" replaces the last one).

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (206)`.

- [ ] **Step 6: Checkpoint**

Tests `ALL PASS (206)`; build `0 Error(s)`. Files changed: `VolleySkill.cs`, `Tree.cs`, `Volley.cs`, `Main.cs`, `Rules.cs`, `Shrine.cs`, `Builds.cs`, `QuickshotRing.Tests/Program.cs`.

---

### Task 9: Burst's slot, Holy Arrow after Charge 3, the live-draw gate

**Files:**
- Modify: `QuickshotRing/Tree.cs:43-68` (settings), `:163-189` (Burst slot, Holy re-link, layout, icons), `:253-265` (`ApplyLayout` takes `auto`), `QuickshotRing/HolyArrow.cs:85-101` (`Arm`), `:114-142` (`BeamCreated`)

**Interfaces:**
- Consumes: `AddSlot` (`Tree.cs:269-284`, copies the parent's cost, then overridden), `Relink` (`:247-251`), `PickPosition` (`:307-319`), the indent reset (`:179`), `iconSlots` (`:52`, `:180-182`); `LightBeamSkill.HandleInputChanged(bool, ISkillInputMethod, BowString)` (`_decomp/Alta.Progression.Skills/LightBeamSkill.cs:17-33`); `BowString.Pickup.IsInteractedWith`, `StringDock.QuantityStored`, `DockedPickup.Entity.Identifier`; `Draws.Get(bow).Clock` (Task 6), `HolyGate` (Task 2), `Tree.ChargeMultiplier`, `Builds.ServerBuild` (Tasks 4, 5).
- Produces: the Burst slot (`SlotIds.Burst`, skill `VolleySkill.Skill`, needs Three-Shot, cost `[Tree] BurstCost`), mapped as `Orb.Burst`; `iconSlots["Burst"]`.

- [ ] **Step 1: Tree edits** (only when the Volley skill is registered; otherwise skip Burst and keep Holy's old link with an error line)

- Burst: `AddSlot(tree, "Burst", VolleySkill.Skill, ThreeShotSlot, SlotIds.Burst, <layout>)`, cost from `[Tree] BurstCost` (shared, default 1, description in the Settings table). Before adding any new slot, check no tree slot already has that id; on a collision log `[Archery] Tree: slot id {id} for {orb} is already used by '{name}'; {orb} is off` and skip it.
- Holy Arrow: `Relink(tree, holySlot, Tree.ChargeSlots[Math.Min(2, Tree.ChargeSlots.Count - 1)])` (Slowdraw's own slots, never Two-Shot's or Three-Shot's, which are also `BowSlowdraw` now; skip with an error line if the list is empty). This also drops it from Three-Shot's dependents.
- Layout: `[TreeLayout] HolyArrow` default `auto` (`auto` = `PickPosition(tree, charge3.Position, charge3.Position - charge2.Position)` with `charge3`, `charge2` the last two of `Tree.ChargeSlots`, computed in `Inject` and kept per slot), `[TreeLayout] Burst` default `-0.19, 0.55, 0.44`; `ApplyLayout` accepts `auto` for any placed slot that has a computed position.
- Icons: `iconSlots["Burst"] = burstSlot`.

Log lines, exactly:
- `[Archery] Tree: Burst at {pos} (slot {id}, needs '{ThreeShotSlot.Name}', cost {c})`
- `[Archery] Tree: Lightbeam now requires '{charge3.Name}'`
- after `ApplyLayout`, one line per mapped archery slot (spec Step 3: "each slot's shrine position"): `[Archery] Tree: slot {Orb} '{name}' at ({x:0.00}, {y:0.00}, {z:0.00}) needs {dependency orbs joined "+"|nothing}`

- [ ] **Step 2: Holy Arrow's live-draw gate** (replaces "the volley is full", `HolyArrow.cs:90-92`)

`Arm.Prefix` allows the arm when `HolyGate.MayArm(bowString.Pickup.IsInteractedWith, record.Clock.Active(Time.time, Volley.RelaxGrace), arrowNow, record.Clock.ArrowAtCrossing)`. The postfix keeps absorbing whatever ghosts are showing (`Volley.Consume`).

Log lines, exactly: `[Archery] Holy: armed on a live draw (arrow {id}); {n} ghost(s) absorbed` and `[Archery] Holy: not armed: {string not held|no arrow on the string|no crossing|the arrow changed since the crossing (was {a}, now {b})}`

- [ ] **Step 3: The beam from the charge tier** (`BeamCreated`, `HolyArrow.cs:128`)

`charged = Tree.ChargeMultiplier(Builds.ServerBuild(shooter != null ? shooter.NetworkPlayer : null).Charge) × Hits.Power(arrowTool)` (`shooter` is a `PlayerController` here, `HolyArrow.cs:124-126`; the explicit null test, as `:128` does today, because `?.` skips Unity's destroyed-object check); the existing line `Holy: beam ready ... (Slowdraw x{slow})` becomes `(charge tier {t} x{m:0.##})`. A Holy owner with only Charge 1 gets 1.25 × 4 = 5 times arrow power (spec Part 14, save migration).

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (206)`; build `0 Error(s)`. Files changed: `Tree.cs`, `HolyArrow.cs`.

---

### Task 10: One flight model, echoes, pellets, landing

**Files:**
- Modify: `QuickshotRing/Volley.cs:241-360` (`Launch`, `Layout`, `VolleySim`), `:433-498` (`Ghost`: LOD0 heads, colours, other archers), `:36` (`DefaultColour = new Color(0f, 0.89f, 1f)`), `:58-59` (`Colour` default `#00E3FF`; `HitFlash` description from the Settings table)
- Create: `QuickshotRing/VolleyFx.cs` (only `LandFx` and the shared `Sparks` emitter in this task)
- Settings created here (Settings table; defaults from the Task 3 constants where named): `[Volley] ChargedColour`, `EchoShare`, `NockBack`, `LiveCap`, `ClientCastRange`, `OtherArcherBrightness`, `LandSparks`, `SparkMaterial`, `LandSound`, `LandSoundsMax`; `[Burst] PelletShare`, `InnerDegrees`, `OuterDegrees`, `Range` (all four `Cfg.Shared`), `StreakLength`, `PuffSpecks`

**Interfaces:**
- Consumes: `ArcModel`, `BurstPattern`, `SpectralBudget`, `SpectralMath.Densest` (Task 3); `VolleyMath.TipOffset`, `ExtraShare` (`Rules.cs:39-66`); `Hits.Spectral/FlushSpectral/OpenRelease/ArrowDone` (Task 7); `Hits.Cast` (`Hits.cs:19-39`); the real arrow's pose after `SyncShoot` placed it (`_decomp/Shooter.cs:45-61`) and its launch speed `velocity.magnitude` from `ShootExtraArrows` (`_decomp/ExtraArrowUpdater.cs:99`), computed from the shared strength on every machine (`Shooter.cs:99-100`); `Arrow.ArrowTip` (`_decomp/Arrow.cs:94`); `Physics.gravity`; the dump's particle material `Sparks` (`loaded_assets.txt` line 1168: `Sparks | Sparks | Mobile/Particles/Additive`), found as `Fx.cs:203-206` finds `WeaponTrail`; `Fx.FindSound` (`Fx.cs:111-121`), `Fx.Flash` (`:87-92`).
- Produces:
  - `VolleySim.Shot` (beyond Task 7's fields; `SpectralKind` comes from Task 3) gains `Vector3 p0, flat; float elevation, speed, born, lastCast, maxRange; SpectralKind kind; PelletStreak? streak`, and loses `position`, `velocity`, `age` (positions come from the arc).
  - `VolleyFx.Sparks.Emit(Vector3 at, Vector3 dir, int count, Color c, float spread, float speed)`; `VolleyFx.LandFx.Land(uint release, Vector3 at, bool charged)`.
  - `PelletStreak` (in `Burst.cs`, created here): a 2-point line `[Burst] StreakLength` long behind the pellet, width and alpha falling linearly to 30% at `[Burst] Range`, ending in `[Burst] PuffSpecks` specks from `Sparks`.

- [ ] **Step 1: Layout from the real arrow alone** (spec Part 12 item 4)

`ReleaseLayout(Arrow arrow, float nockBack)`: `tip` = arrow tip position, `forward` = `arrow.transform.forward`, `up` = the arrow's up made orthogonal to `forward`, `right = Cross(up, forward)`, `nock = tip - forward × nockBack`. Ghost `i` (and echo `i`): `tip_i = tip + right·x + up·y` from `TipOffset(i)`, direction `(tip_i - nock).normalized`. Pellet `p`: start `tip_{Owner(p)}`, direction `cos α·forward + sin α·(cos φ·right + sin φ·up)` from `BurstPattern.Pellet(p, InnerDegrees, OuterDegrees)`. The held ghosts in `DrawHeld` keep their bow-based layout (visual only).

- [ ] **Step 2: Launch every spectral part on the arc**

`Launch(bow, arrowEntity, velocity, r, held)`: `speed = velocity.magnitude`; for each ghost (`r.Plan.Ghosts`, shares inner/outer), echo (`r.Plan.Echoes`, `[Volley] EchoShare`, uncharged) and pellet (`r.Plan.Pellets`, `[Burst] PelletShare`): `elevation = asin(dir.y)` clamped, `flat` = horizontal part of `dir` normalised (or `forward`'s horizontal part when vertical), `born = Time.time`. Echoes and pellets get new visuals (echoes are `Ghost`s that slide out of the tip in `ArriveSeconds` at release; pellets get a `PelletStreak`). Server: `Hits.OpenRelease(id, total, shooter, arrow.ImpactTool)`.

`VolleySim.FixedUpdate`: `age = Time.time - born` at the fixed step; position from `ArcModel.Point(speed, elevation, age, -Physics.gravity.y)` placed as `p0 + flat·along + up·Vector3.up`; cast from `pos(lastCast)` to `pos(age)` when `SpectralBudget.CastThisTick(live.Count, [Volley] LiveCap, index, tick)` (then `lastCast = age`); clients cast only within `[Volley] ClientCastRange` of `Camera.main`, falling back to `Player.Current.PlayerController.Head` (`_decomp/PlayerController.cs:129`) when no camera is tagged main, and casting everything when neither exists; log once `[Archery] Volley: client cast range measured from {camera name|head|nothing}`. A hit: server → `Hits.Spectral(...)`; everyone → `LandFx.Land`. End when `SpectralLife.Ended(kind, distance from p0, age, [Burst] Range, LifeSeconds)` (a pellet ending by range puffs, no hit). Each end calls `Hits.ArrowDone(release)` on the server. After the loop, `Hits.FlushSpectral()`. Visuals move in `LateUpdate` from `Time.time - born`, so they are smooth in the headset.

- [ ] **Step 3: Landing: one light per volley, shared sparks, at most three sounds** (spec Volley entry, Part 9 items 5 and 6)

`LandFx.Land`: emit `[Volley] LandSparks` sparks from the shared `Sparks` emitter (cyan, or `ChargedColour` when charged); play `[Volley] LandSound` for the first `LandSoundsMax` landings of a release; buffer the release's landing points for 0.15 s after its first landing, then one `Fx.Flash` (brightness `HitFlash`) at `SpectralMath.Densest(points, 1 m)`. Delete the per-ghost flash (`Volley.cs:339`).

- [ ] **Step 4: Heads, colours, other archers**

`Ghost.Create` (`Volley.cs:440-472`): when the tip has a `LODGroup`, copy only `GetLODs()[0].renderers`' meshes (Part 11: LOD0 only); log once per tip name `[Archery] Volley: ghost heads use {n} mesh(es) from LOD0 of '{tip}'`. Shafts use `[Volley] Colour` (`#00E3FF`) and, once `ChargedOnClient(bow)` (or the release is charged), `ChargedColour`. Ghosts of a bow whose shooter is not the local player are drawn at `OtherArcherBrightness`.

- [ ] **Step 5: The flight check** (spec Step 3: "the real arrow's and a ghost's positions at 30 metres on the server and on the shooter")

Per release (server and shooting client), track the real arrow; at the first fixed step where it is 30 m (horizontal) from its release tip, log, exactly:
`[Archery] Flight check: arrow {id} at 30 m after {t:0.000} s: real {pos}, model {pos}, real-model {d:0.00} m; ghost 0 at {pos}`
where `model` is `ArcModel` from the real arrow's own release pose and speed, and `ghost 0` is inner ghost 0's model position at the same `t`. An arrow that stops before 30 m logs nothing; the play-test's open-ground shot gives the line.

- [ ] **Step 6: Checkpoint**

Tests `ALL PASS (206)`; build `0 Error(s)`. Files changed: `Volley.cs`, `VolleyFx.cs`, `Burst.cs`.

---

### Task 11: Beats, Burst's look, the ramp test

**Files:**
- Modify: `QuickshotRing/VolleyFx.cs` (beats, pulses), `QuickshotRing/Burst.cs` (barbs, rack, shed), `QuickshotRing/Volley.cs` (`DrawHeld` drives beats and barbs; release flare), `QuickshotRing/Fx.cs:99-124` (cached misses), `:172-207` (steel-song fallback lookup), `QuickshotRing/Main.cs:81-94` (pulse pump in `OnUpdate`); creates the remaining new settings (Settings table, "Task 11")

**Interfaces:**
- Consumes: `VolleyTimeline.BeatsBetween`, `Ghosts`, `Racked` (Task 2), `Volley.Times` (Task 8); `Controller.TriggerHaptic(float strength, float duration, float frequency = 80f)` (`_decomp/Controller.cs:205`), the bow hand as `bow.Bow.Pickup.Interactors[0].Controller` (as `_decomp/BowString.cs:256-259`); `SoundEffectSet.PlayAtPosition` (as `HolyArrow.cs:429`); `ToolBasedEffectBase.baseMaterial` (`_decomp/ToolBasedEffectBase.cs:14`); `Material.GetTexturePropertyNames()`, `Shader.GetPropertyCount/GetPropertyName/GetPropertyType` (Unity 2020.3); `MaterialPropertyBlock` with `_Progress` (the property the game animates, `ToolBasedEffectBase.cs:8`, `_decomp/ToolBasedEffect.cs:21`).
- Produces: `VolleyFx.Pulses(Controller c, int count, float strength, float gap)` (queued, pumped from `Main.OnUpdate`); `VolleyFx.Beats` (per local bow: slide-out, ratchet clicks, ripple, release flare); `Burst.Barbs` (per bow), `Burst.Rack`, `Burst.Shed(release, inner tips, charged)`.

- [ ] **Step 1: Beats on the shooter's bow** (spec Volley, Full Volley, Burst entries; clients only; spectators get the sounds, not the haptics)

- Slide-out when `Volley.Ghosts(bow)` goes from 0 to 5 on that bow (not `BeatsBetween`: the ghosts wait for the server's `Charged` status, a round trip after the local clock's 0.75 s, and sound and sight must not split): `SlideSound` at the tip, 2 pulses (`HapticStrength`, 0.03 s, 0.08 s apart). `BeatsBetween` drives Ripple and Rack only.
- Ratchet: one `RatchetSound` per new outer ghost, a pulse on every other one.
- Ripple at `RippleAt(chargeTier)`: every head's `_Progress` (property block) dips to `1 / RippleBoost` and returns to 1 over `RippleLength`, shafts brighten by `RippleBoost` over the same time.
- Release with ghosts: shafts flare to full length (already `Ghost.Fly`), one `ReleaseSound`.
- Burst rack at `RackAt`: `RackSound`, then `RackClick` `RackClickDelay` later, 2 firm pulses (`HapticStrength × 1.6`); barbs appear: 3 copies of the head mesh at `BarbScale` round each inner head at 0, 120, 240 degrees, pointing forward, scale breathing ±`BreatheAmount` once per `BreatheSeconds`, no jitter.
- Burst release: one `WhumpSound` at the bow, `ShedSparks` cyan spark streaks from each inner head (from `Sparks`, a forward cone), one `Fx.Flash` at the real tip.

Log once: `[Archery] Volley FX: sounds slide '{x}', ratchet '{x}', release '{x}', land '{x}', rack '{x}', click '{x}', whump '{x}' (missing: {list|none})`, and per rack `[Archery] Burst: racked on '{bow}'`, per release `[Archery] Burst: shed {n} pellets on arrow {id}`.

- [ ] **Step 2: Lookups that miss are cached** (Part 11)

`Fx.FindEffect`/`FindSound` cache a miss and retry it only after 5 s or on `Fx.ClearAssetCache` (settings reload). Log once per name: `[Archery] Asset '{names}' not found; trying again in 5 s or after a settings reload`.

- [ ] **Step 3: The ramp test and the fallback material** (spec Step 3 "Must also log"; Part 12 item 13)

At the first head material: log every texture property and its texture name. When `[Volley] RampTest`: ghost 0 (the top inner ghost) gets its own copy of the head material in which every texture property whose texture name contains `ramp` (ordinal-ignore-case) is set to the first such texture; ghosts 1 to 14 keep the original. In `Fx.LogMaterialLookups` add a `steel-song-base` lookup by two routes, run once, logging colour and texture properties of what each finds. 'Steel Song Mesh Effect' is the `MeshEffect` pooled object, not a `ToolBasedEffectBase`: the effect base holds a private `meshEffectDefinition` and puts its own material on each mesh effect's renderer at runtime (`_decomp/ToolBasedEffectBase.cs:11-14`, `:73-83`), which is why play-test 1 found no material on that prefab. (1) Direct: `Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "Steel Song Tool Effect Material")` (loaded: `loaded_assets.txt` line 1859, shader `Custom/Steel Song Effect`). (2) Via the effect: among `Resources.FindObjectsOfTypeAll<ToolBasedEffectBase>()`, the one whose private `meshEffectDefinition` is named `Steel Song Mesh Effect`, reading its private `baseMaterial`; the same pass logs every base's pair once.

Log lines, exactly:
- `[Archery] Volley FX: head material '{m}' ({shader}) textures: {prop}='{tex}', ...`
- `[Archery] Volley FX: ramp test on ghost 0: {prop}<-'{tex}', ... | not run ({why})`
- `[Archery] Material lookup tool-effect bases: {meshEffectDefinition name}->{baseMaterial name} ...`
- `[Archery] Material lookup steel-song-base: direct 'Steel Song Tool Effect Material' -> '{m}' ({shader}); colours: {props|none}; textures: {props|none}` or `... -> NOT FOUND`
- `[Archery] Material lookup steel-song-base: via the ToolBasedEffectBase using 'Steel Song Mesh Effect' -> '{m}' ({shader})` or `... -> NOT FOUND ({which step failed})`

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (206)`; build `0 Error(s)`. Files changed: `VolleyFx.cs`, `Burst.cs`, `Volley.cs`, `Fx.cs`, `Main.cs`.

---

### Task 12: The Burst icon

**Files:**
- Modify: `QuickshotRing/Art/make_top_icons.py`
- Create: `QuickshotRing/Art/drafts/Burst.png`, `QuickshotRing/Resources/Burst.png`

**Interfaces:**
- Consumes: `make_icons2` helpers (`alpha`, `clear_symbol_area`, `scratches`, `stamp`, `shapes`, `save`; `Art/make_icons2.py:15-71`) and the game glyphs in `Art/game_icons` (`Glyph_Bow_ThreeShot.png`); the existing `volley3` and `arrow` crops (`make_icons2.py`, `build`).
- Produces: `burst()` in `make_top_icons.py`; the sheet `Art/drafts/sheet_final.png` with six columns (Quickdraw4, QuiverGather, TwoShot, ThreeShot, HolyArrow, Burst; Burst's reference is `Glyph_Bow_ThreeShot`).

- [ ] **Step 1: Draw it from the game's glyph** (memory rule "Art must match the game")

`burst()`: `clear_symbol_area(alpha(SRC, "Glyph_Bow_ThreeShot").copy())`, `scratches(a, random.Random(5))`, the vanilla bracket and three vanilla volley arrows stamped as Three-Shot's first row (`stamp(a, arrow, (322 + col * 56, 250), scale=0.34, boost=1.5, fatten=3)` for col 0 to 2), then with `shapes()` a shotgun daisy in front of the arrows: 5 short streaks at a narrow angle and 10 at a wider one, fanned from (452, 256), each 4 px wide at `S` scale, the wide ones shorter, mirroring Burst's 2.5/5 degree rings. Append `"Burst"` to `names` and `"Glyph_Bow_ThreeShot"` to `refs`; the sheet width becomes `cell * 6`; the copy loop then also copies `Burst.png` to `Resources`.

- [ ] **Step 2: Run it**

Run (from `QuickshotRing\Art`): `python make_top_icons.py`
Expected: `ok: sheet_final.png written, six icons copied to Resources` (update the message), and `Resources\Burst.png` exists.

- [ ] **Step 3: The user sees the sheet before it ships**

The controller sends `QuickshotRing\Art\drafts\sheet_final.png` to the user (`SendUserFile`) and asks whether the Burst icon (bottom right, game's Three-Shot above it) may ship. If the answer is not yes before Task 13's deploy, ship the clearly marked placeholder instead: copy `Art\game_icons\Glyph_Bow_ThreeShot.png` to `Resources\Burst.png` and set `Tree.PlaceholderIcons = { "Burst" }` so `ApplyIcons` logs `[Archery] Icon 'Burst' loaded from built-in (PLACEHOLDER: the game's Three-Shot glyph until the drawn icon is approved)`.

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (206)`; build `0 Error(s)` (the resource is embedded by `Directory.Build.props`). Files changed: `make_top_icons.py`, `drafts/Burst.png`, `drafts/sheet_final.png`, `Resources/Burst.png` (and `Tree.cs` if the placeholder ships).

---

### Task 13: Version, CLAUDE.md, backup, deploy, and the play-test

**Files:**
- Modify: `QuickshotRing/Main.cs:9` (`0.8.0`), `CLAUDE.md`

- [ ] **Step 1: Version** `[assembly: MelonInfo(typeof(QuickshotRing.Main), "QuickshotRing", "0.8.0", "dmand")]`.

- [ ] **Step 2: `CLAUDE.md`** (QuickshotRing section)

File table: add `Release.cs`, `Spectral.cs`, `Draws.cs`, `Shrine.cs`, `TestBuild.cs`, `VolleySkill.cs`, `VolleyFx.cs`, `Burst.cs`, one line each; `Rules.cs` row says Unity-free files are `Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`; the test command result becomes `ALL PASS (206)`. Replace the bullet "Changing a shipped default? Raise `Cfg.SettingsVersion`" with: shipped defaults are recorded in `UserData\QuickshotRing\shipped_defaults.txt`; a changed default resets only entries still at the old default; never raise `SettingsVersion`. Replace the Two-Shot/Three-Shot bullet with: both slots now point at the mod's Volley skill (a cleaned copy of Slowdraw with its own pull settings, id 47011); Slowdraw is found once by type before the copy exists and by reference afterwards. Add: `[TestBuild] Player/Orbs` in the server's cfg lend orbs for play-tests (logged loudly, never saved); the release is resolved once on the server in `Volley.Fire` and relayed in the claim's kind and stage.

- [ ] **Step 3: Three questions for the user, before deploying**

1. "0.8.0 makes a second ultimate unbuyable. If you want to test a real save that owns two (Quickdraw 4 and Holy Arrow), buy both now under 0.7.0; it uses your last 2 of 8 slots. If you buy them, you are at 8 of 8 slots: the Quiver Gather test (A3) becomes impossible, and selling either ultimate later means it can never be bought back while the other is owned. Otherwise that check stays untested and I use the test build. Buy them, or skip?" (Recommended default: skip.)
2. The Burst icon question from Task 12 Step 3, if not yet answered.
3. "The game's 'sell skills with children' setting is off on this PC (`%APPDATA%\A Township Tale\GameConfiguration.json`, `CanSellSkillsWithChildren: false`). With it off, Arrow Gatherer cannot be sold while Quiver Gather is owned, so 'a sale that also removes a child orb' cannot be seen; A3 then sells them one at a time. Turn it on for this session (I edit the file with the game and server closed, and set it back afterwards), or leave it?" (Recommended default: leave it. It is the game's default and how you play; the server's start-up line `Progression: ... can sell with children:` shows which value it used.)

- [ ] **Step 4: Back up** (spec Part 13 "Before every deploy")

Copy into `C:\Users\dmand\OneDrive\Desktop\attmod\backups\2026-11-13-pre-0.8.0\`: `C:\Users\dmand\AppData\Roaming\A Township Tale\Servers\-1\Save`, both `UserData\QuickshotRing.cfg` (as `client-` and `server-QuickshotRing.cfg`) and both installed `Mods\QuickshotRing.dll` (as `client-` and `server-QuickshotRing.dll`). Expected: five items in the folder. If question 3 was answered "turn it on", also copy `%APPDATA%\A Township Tale\GameConfiguration.json` there first (six items), then set `CanSellSkillsWithChildren` to `true` with the game and server closed; set it back after the play-test.

- [ ] **Step 5: Final checks and deploy**

Run: `dotnet run --project QuickshotRing.Tests -c Release` → `ALL PASS (206)`. Run: `dotnet build QuickshotRing -c Release -p:Deploy=true -p:DeployServer=true` → `0 Error(s)`, the DLL in the client `Mods`, `Modpacks\My mods` and the server `Mods`. It fails while the game or server runs: then ask the user to close both and run it again.

- [ ] **Step 6: Hand the user the play-test script (a)**

Fill `<you>` with the username the server log showed in play-test 1, and the icon line in A1 with the answer to Step 3 question 2, before sending. The server's file is `C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\UserData\QuickshotRing.cfg`; the client's file is `C:\Users\dmand\OneDrive\Desktop\A Township Tale (client\UserData\QuickshotRing.cfg`. Each setting is one line under its `[Section]` header, e.g. under `[Archery]`: `HideGameGlow = true`. Saved edits are picked up within 2 seconds while the game runs. "Full draw" below means the moment the string reaches full pull; timings are given as things you can hear or count, not stopwatch numbers.

Setup: start the server, then the client, and join. Bring a bow and a quiver with at least 20 arrows, and start at the Ranged shrine. Find a wall, cliff face or big rock about 20 m away (to see the pellet pattern), and open ground or water running well past 40 m (for the ghosts' flight and the pellets' 30 m cut-off). Creatures nearby help with A10. Do all of Part A before Part B. Part B means taking the headset off to edit the server's file; the `[TestBuild]` section is in it once the server has started with 0.8.0. Values go in quotes, e.g. `Orbs = "Burst"`. Before each Part B change, put the bow away (on your back or on the ground); after saving, wait a couple of seconds, take the bow out and do one throwaway draw before the real test. Your save from before 0.8.0 is backed up in `C:\Users\dmand\OneDrive\Desktop\attmod\backups\2026-11-13-pre-0.8.0`.

**Part A: your own character (Quickdraw 1-3, Two-Shot, Three-Shot, Slowdraw 1)**

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| A1 | Open the Ranged shrine and look at the bow orbs | Two-Shot no longer has a line running down to the Slowdraw column; it may have no line below it at all. A new orb sits above Three-Shot, where Holy Arrow used to be: that is Burst. Its icon is the new one you approved, or, if you didn't approve it, a copy of Three-Shot's icon (expected for now). Holy Arrow has moved to the top of the Slowdraw column, above Slowdraw 3, with a line to it. It looks like other orbs whose parent you don't own yet | the shrine fails to open; Burst is missing; Holy Arrow still hangs off Three-Shot; two orbs sit inside each other |
| A2 | Look at your own orbs | Quickdraw 1-3, Two-Shot, Three-Shot and Slowdraw 1 all still show as owned | any of them shows as not owned: stop and tell Claude |
| A3 | Optional, and it costs you: selling refunds only 80% of the points, so you lose a little for good (the backup can undo it if you ask Claude). Skip it if you don't want that, or if you bought both ultimates in Step 3. At the shrine, check you have enough free points for Arrow Gatherer plus Quiver Gather (the shrine shows the prices). (1) Pick up Quiver Gather to try it, put it back without buying, then shoot one arrow taken from your quiver and wait 20 s. (2) Buy Arrow Gatherer, then Quiver Gather. Shoot one quiver arrow and wait 15 s. (3) Try to sell Arrow Gatherer. If the shrine won't let go of it (it stays put while Quiver Gather is owned; that is the game's own rule when "sell skills with children" is off), sell Quiver Gather first, then Arrow Gatherer, and tell Claude which way it went. Shoot one more quiver arrow and wait 20 s | (1) The arrow stays where it landed. (2) After about 15 s a ghost of the arrow flies back into your quiver. (3) Quiver Gather goes, either together with Arrow Gatherer or by itself first; you get points back (a bit less than you paid), and the third arrow stays where it landed | (1) the arrow comes back; (2) it does not come back; (3) it still comes back, or Quiver Gather still shows as owned after the sales |
| A4 | Stay at the shrine. Pick up the Burst orb (this is trying it, not buying it). Pull to full and keep holding: you get the five heads, the fast rattle and Slowdraw's charge sound. Keep holding a moment longer, release anywhere, and put the orb back. Do NOT buy Burst this session: owning it changes what A5 to A12 should look like, and B1 tests it properly | About a quarter-second after the charge sound you hear a soft clack and a click, feel two firm buzzes, and see small barbs grow round the five ghost heads closest to the arrow. After that one shot the orb shows as practised (buyable if you had a free point) | you cannot pick it up; no click or barbs while trying it; after the shot the orb does not show as practised |
| A5 | Snap: pull to full and let go at once, while the gold ring is still closing | The snap works as in 0.7.0: the ring closes and the arrow flies hard and straight. Two faint cyan ghost arrows ("echoes") slide out of the tip, one from above and one from up-and-to-the-side. They fly alongside the arrow, spreading slightly, and each lands in a tiny cyan spark | no echoes, three or more, echoes going sideways or backwards, no gold ring |
| A6 | Early shot: pull to full, wait until the gold ring has gone, and let go before any cyan heads appear round the tip | A normal arrow with the same two echoes beside it | no echoes. (If five or more ghosts fly, you held too long; try again) |
| A7 | Pull to full and hold for a slow count of three, then release over open ground | In order: (1) About three-quarters of a second after full draw, five cyan ghost heads slide out round the tip, with the sound a shrine orb makes when you touch it and two light buzzes in the bow hand. (2) A quarter-second later ten more snap on in one fast rattle of soft clicks with buzzing; it is over in under half a second. (3) Close to two seconds after full draw, Slowdraw's usual charge sound plays and the ghost shafts change from cyan to gold. The heads keep their colour, and all the ghosts pulse once. (4) On release: a whoosh, the shafts stretch to full length, and all 15 ghosts fly with your arrow, spreading a little and following the same curve. They land round where your arrow lands in small gold sparks, with ONE flash of light and at most three impact thuds. While holding, look at the top ghost head (12 o'clock) and tell Claude whether it looks different from the other four (colour, brightness, pattern); either answer is useful | the ghosts only appear after two seconds or more (the 0.7.0 timing); the shafts never turn gold; the charge sound is missing; the ghosts land clearly short of or past your arrow; a flash for every ghost; more than three thuds |
| A7b | Pull to full and release during the fast rattle or just after it, BEFORE Slowdraw's charge sound (roughly one and a half seconds after full draw) | The ghosts that are out stay cyan (not gold), fly with the arrow and land in cyan sparks, with one flash | the ghosts are gold, or none fly |
| A7c | Hold for a slow count of three, then shoot a high lob, about 45 degrees up, over open ground | The ghosts rise and fall together with your arrow and land in a loose ring round it | the ghosts come down well before or after the arrow, or keep flying straight |
| A8 | Hold past Slowdraw's charge sound and look at the arrow itself, then release. To compare: take the headset off, set `HideGameGlow = false` under `[Archery]` in the client's file, save, put the headset back on and charge another arrow. Then set it back to `true` | With `true`, the game's own glow over the whole arrow (the one Slowdraw gave in 0.7.0) is gone; only the gold ghost shafts show the charge. With `false`, it is back | the whole-arrow glow shows with `true` |
| A9 | With the ghosts out, ease the string forward until the bow clearly relaxes, then pull straight back to full in under a third of a second (a quick flinch). Next, ease it forward and keep it there for a full second, then pull to full again | The quick flinch keeps every ghost and the charge. The one-second relax makes the ghosts vanish, and the new pull starts over: five heads about three-quarters of a second after full draw | the quick flinch loses the ghosts, or the long relax keeps them |
| A9b | Pull to full and keep holding until the gold ring has gone. Twitch the string forward and back fast. Then ease it forward, wait half a second, pull to full again and snap | The twitch does NOT bring the gold ring back. After the half-second relax the ring closes again and the snap works | the twitch brings the ring back, or the proper relax doesn't |
| A10 | Two full-hold shots at a creature: one aimed straight at it, one aimed so the real arrow just misses beside it | Straight hit: it dies in fewer shots than with a plain arrow. Near miss: the ghosts that strike it still hurt it (it flinches or turns on you), but ghost hits alone no longer knock it down. That is intended until Weight (a later step); in 0.7.0 ghosts could knock creatures over | the ghosts pass through with no reaction, the ghosts alone knock it over, or the game hitches when they land |
| A11 | Pull to full, then put the bow on your back (or drop it) without shooting. Take it out again, nock, and within a second pull only halfway and let go | A weak plain arrow: no echoes, no ghosts, no gold | the half-draw shot comes out with ghosts, echoes, gold shafts, or as a snap |
| A11b | Take the bow out again, pull to full and release after about half a second (after the ring, before the heads) | A normal arrow with two echoes, as in A6 | as A6 |
| A12 | Snap one arrow. Then wait about 3 s with no arrow on the string, listening. Nock a new arrow, pull only halfway and let go at once | No Slowdraw charge sound plays in the 3 s after the snap; no ghosts appear on the new arrow before you draw; the half-draw shot is a weak plain arrow | a charge sound plays after the snap; ghosts sit on the new arrow before you draw; the half-draw shot has ghosts, echoes or gold |

**Part B: the test build (in the server's file, under `[TestBuild]`, set `Player = "<you>"` and the `Orbs` below)**

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| B1 | Server file: `Player = "<you>"`, `Orbs = "Burst"`, save. Take the bow out, one throwaway draw. Then hold until you hear the click that follows Slowdraw's charge sound, and release at the 20 m wall. Do it three times, then once over open ground | Rack: the sound of an item hovering over a slot, a click right after, two firm buzzes. Three small barbs grow round each of the five inner ghost heads and swell gently about once a second. They never shake, and the middle of your aim stays clear. Release: a heavy bow-shot whump, sparks spray forward from the five inner heads, and 15 short streaks fly out in a flower. On the wall the sparks form 5 in a tight ring (about 1.7 m across) and 10 in a wider ring (about 3.5 m across), the same shape on all three shots. Over open ground the streaks thin out and vanish in a small puff about 30 m out | the barbs jitter or cover your aim; the flower changes shape between shots; streaks carry on past roughly 30 m |
| B1b | With `Orbs` still `"Burst"`, do an early shot as in A6 | Three cyan echoes instead of two | two or none |
| B2 | Bow away. Server file: `Orbs = "HolyArrow"`, save. Walk to the shrine (step away and back if nothing has changed). Look at Quickdraw 4, then pick it up to try it and snap once. Put it back. Do NOT push it in to buy it; if it looks buyable, stop and tell Claude. This row only counts if you have at least as many free points as Quickdraw 4 costs (the shrine shows both); otherwise Claude reads the log | Quickdraw 4 looks like an orb you cannot buy, but it still comes out to try. While you hold it, a snap is the instant straight line from 0.7.0. Holy Arrow itself still looks not owned in the shrine; that is expected, because the test build only lends it for shooting | Quickdraw 4 looks buyable; it cannot be picked up; the snap while trying it is a normal arrow |
| B3 | Bow away. `Orbs = "Quickdraw4;HolyArrow;Charge2;Charge3"`, save, one throwaway draw. Snap once at the wall. Then pull to full and hold for a slow count of eight, and release | The snap is the instant straight Line, with two cyan echoes flying normally beside it. On the hold the ghosts come as in A7 and turn gold. About six seconds in they draw back into the arrow, there is a gold flash and both hands buzz: Holy Arrow is ready. The release fires the beam and blast as in 0.7.0 | the snap is a normal arrow; nothing arms by the count of eight; ghosts are still out after Holy Arrow arms |
| B3b | Same `Orbs`. Headset off: in BOTH files set `ThreeShotSeconds = 9` under `[Archery]` (it is shared, so both must match), save both. One throwaway draw, then pull to full, hold for a slow count of eight and release. Afterwards set `ThreeShotSeconds = 1` again in both files | Only the five inner ghosts come out (the rattle never starts), they fold into the arrow, Holy Arrow arms with the gold flash and buzz, and the release fires the beam | no gold flash by the count of eight; an ordinary arrow |
| B4 | Same build, `ThreeShotSeconds` back at 1: three more times, hold for a slow count of ten before releasing | The beam every time | any of them is an ordinary arrow |
| B4b | Same build. Snap one arrow, then do not nock another: pull the empty string to full, hold it for a slow count of eight, and let it go gently | Nothing happens: no gold flash, no buzz, no charge sound | Holy Arrow's gold flash or buzz, or any charge sound |
| B6 | Optional: set `LogWire = true` under `[Wire]` in both files. Set `SendBuildBits = false` in the server's file, draw once, set it back. Then `SendClaims = false` in the client's file, shoot once, set it back. Set `LogWire` back to `false` in both | Shots behave normally (Claude checks the log) | errors, or a shot looks different from the same shot without the change |
| B7 | Bow away. `Player = ""` and `Orbs = ""`, save, one throwaway draw. Snap once, then hold for a slow count of eight and release | The snap is a normal arrow with two echoes (no Line). The long hold gives gold ghosts but no Holy flash, and the release is a normal volley | the Line or Holy Arrow still happens |

**Part C: optional, at the very end**

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| C1 | Close the game (leave the server running). Ask Claude to move `QuickshotRing.dll` out of the client's `Mods` and `Modpacks\My mods` (Claude keeps a copy). Start the client and join. Take the bow out, pull to full, hold for a slow count of three, release. Close the game and ask Claude to put the DLL back | You stay connected. Arrows are plain: no ghosts, no echoes, no gold. The shrine may look different; don't buy anything | you are disconnected, the game freezes, or you cannot join |

Untested in this session unless you choose otherwise: what a second player sees (your ghosts, the pellet flower, and other archers' ghosts drawn dimmer; needs a second client); selling one of two ultimates (only on a copy of the save or a spare character, and only with your go-ahead); a save that really owns two ultimates (only if you answered "buy" in Step 3); a sale that also removes a child orb (only with "sell skills with children" on; see Step 3 question 3); a character with no Charge orb trying, practising and buying Volley, and the ripple brightening at about one and a half seconds that such a character gets (needs a spare character with no Slowdraw; A7 shows the same brightening at the charge sound); a client without the mod, unless you do C1.

- [ ] **Step 7: Claude's log checklist (b)** (read `MelonLoader\Latest.log` on the server and the client; the game logs, `%APPDATA%\A Township Tale\{Client,Servers\-1}\logs`, for rows 12, 14 and 15)

| # | After | Expect (grep `[Archery]`) |
|---|---|---|
| 1 | start | both `QuickshotRing v0.8.0`; `Settings: ... reset to the new default` for `Archery.TwoShotSeconds`, `Volley.OuterInterval`, `Volley.Colour`, `TreeLayout.HolyArrow`, `Wire.LogWire` on the server (its file was reset in 0.7.0), and either reset or `kept at your value` lines on the client, plus any `but since 0.8.0 it means` line (record it); `Settings: shipped defaults recorded`; `shared checksum` equal on both sides; server `Progression: ... can sell with children: {value}` (record it: it decides A3's path); `Volley skill: registered 'Bow Volley' id 47011 at start` (or at a later attempt) with the table count on both sides; `Volley skill: own pull settings ... separate settings objects: yes; own input maps: yes; achievement cleared: yes`; `Tree: Two-Shot (32124) modified '...'` (the modified skill's name); `Tree: Two-Shot and Three-Shot now use the Volley skill`; `Tree: Burst at ...`; `Tree: Lightbeam now requires '...'` (Slowdraw 3); one `Tree: slot ...` line per orb; `Tree: orb costs ...`; `Probe: damage by level 1-4: Quickdraw 1.25 1.5 1.75 ..., Slowdraw 1.25 1.5 1.75 ..., Volley 1 1 1 1` (record any other values); at join, `Build: refresh patch on {Type}.{Method} is live` for `UnlockSlot` and `LoadFrom` only. The other refresh-patch lines need play and are not missing at start: `PracticeSlot` and `EndPractice` appear at A4 (trying an orb); both `LockSlot` lines (`PlayerProgressionManager.LockSlot`, `PathExperience.LockSlot`) and `UpdateCounts` appear only on a sale, A3 (`Shrine.Sale` calls `UpdateCounts`); the client's `Glow: ... hidden` appears at the first `Charged` status (A7). A once-only fired line for every other new patch, by the end of the session (list any that are missing); `Icon 'Burst' loaded from ...` matching the user's icon answer (PLACEHOLDER or not); the three `Material lookup` lines of Task 11 Step 3; no `Tree: slot id ... is already used` |
| 2 | join | server `Build: '<you>' = Q3 V2 C1 ...`; `Volley skill: level 2 for '<you>' after loading`; no `Volley level is 0` ERROR |
| 3 | A3 | `Build: '<you>' = ... quiver:yes ... practising QuiverGather` while trying and `quiver:no` after putting it back, with no `Quiver: tracking arrow` for that shot; `Quiver: tracking arrow` for the bought one. Setting on: `Sale: '<you>' sold Arrow Gatherer; also removed: Quiver Gather; owned orbs ... can buy more: yes; skills switched off: Quiver Gather`. Setting off: two `Sale:` lines, Quiver Gather first, each `also removed: none`. Either way no `Quiver: tracking arrow` after the sales |
| 4 | A5, A6 | server `Release: arrow n '<you>' Snap t... ult:- ghosts 0 pellets 0 echoes 2 ... spectral x1.00` and `Early ... echoes 2`; client `Release (local):` with the same kind and plan |
| 5 | A7 | server `Volley: armed 5` with t near 750 ms, then `armed 15` once, at release (the server logs the count only when something asks for it: the Volley input changing and the release; nothing asks per frame on the headless server); client `Volley: showing 5` with t near 750 ms, then `showing 6` to `showing 15` with t from 1000 to 1405 ms (a frame may skip a number); `Release: ... Charged ... ghosts 15 ... spectral x1.25`; `Release: arrow n game set x1.25 on the real arrow; reset k inputs` (the final multiplier); `Volley skill: consumed on arrow n; real arrow multiplier kept at x...` (whatever was there before Volley's handler: 1 or 1.25 by handler order, never Volley's own table); `Volley: arrow n 15/15 ... damage events`; `Flight check: ... real-model d m` on server and shooter (d under about 0.5 m; if missing, check whether the arrow stopped before 30 m); client `Volley: client cast range measured from ...` (once); `Volley FX: head material ... textures:` and `ramp test on ghost 0:` (record them with the user's answer about the top head) |
| 6 | A7b | `Release: ... Held t... ult:- ghosts 5..15 pellets 0 echoes 0 ... spectral x1.00`; `Volley skill: consumed on arrow n; real arrow multiplier kept at x1`; `Release: arrow n game set x1 on the real arrow` (x1, not 1.25) |
| 7 | A7c | a `Flight check:` line for the lob, real-model under about 0.5 m on both sides |
| 8 | A9, A9b | quick flinch: no `lost` line; one-second relax: `Draw: Volley lost ...` and `Draw: Slowdraw lost ...`; A9b: the twitch leaves no `Draw: Quickdraw lost` line, the half-second relax does |
| 9 | A10 | `Volley: arrow n ... k creature(s) hit in b damage events (h health changes)` with b well below the hit count; `Xp: arrow n '...' shares ... -> ... Ranged xp`; `tool [... cripple x ...]` (record the arrowhead's cripple value) |
| 10 | A11, A11b | `Draw: string grabbed on '...'; the last draw's clock (... ms) was cleared`; A11's shot: server `Release: ... Early t? ... echoes 0` and the client's claim time `?`, so no timing diff for it; A11b's `Claim: timing` with client and server both about 500 ms and the diff under about 250 ms (play-test 1 saw -78 to -231 ms); nothing like 10961 ms |
| 11 | A12, B4b | after the snap, `Release: ... reset k inputs (still charging: ...)` naming Volley and Slowdraw; no client `Volley: showing 5` line for that bow in the 3 s after it, and the user heard no slide-out (with `LogWire` off no status line is logged, so the absence of `showing 5` is the check); the half-draw shot `Release: ... Early t? ... echoes 0 spectral x1.00` with `game set x1`; B4b: no `Holy: armed` after the snap (a `Holy: not armed: no arrow on the string` means the reset missed and the gate caught it: record it); `Draw: the game reset ...` and `Draw: the game cleared ...` lines (record the timings; the cleared line is expected only when the user lets go of the bow) |
| 12 | B1-B4, B7 | `TEST BUILD ACTIVE: '<you>' ...` on each change; `TEST BUILD: granted the game's 'Bow Lightbeam'` (and Slowdraw levels) for B3, each with `holding 0 item(s)` (if not 0, check the server game log for `Already have input`); `Burst: racked`, `Burst: shed 15 pellets`, `Release: ... ghosts 15 pellets 15`; B1b `Release: ... Early ... echoes 3`; B2 `Shrine: '<you>' sees Quickdraw4 as CantBuy (was Allowed; free points f, cost c)` and no `Shrine: refused to unlock` unless the user pressed buy (record it if there is one); B3 `Release: ... Snap ... ult:Line` and `Q4: line`; `Holy: armed on a live draw (arrow n); k ghost(s) absorbed`, `Release: ... ult:Beam`, `Holy: beam ready ... (charge tier 3 x...)`; B3b `Holy: armed on a live draw (arrow n); 5 ghost(s) absorbed`; `Volley FX: sounds ...` (none missing); B7 `TEST BUILD: off` first (logged when the file is read), then `TEST BUILD: took back the game's ...` for each grant, and the client's `Build: '<you>' is now ...` without ultimates |
| 13 | B6 | client `Build bits: none on ... (absent)`; server `Claim: ... has no claim (absent)`, and the server's `Release:` line for that shot still carries its plan (`PutResolved` created the relayed claim; no spectator this session to read it) |
| 14 | anywhere | none of: `failed`, `could not be made`, `layouts disagree`, `does not understand`, `OWNS TWO ULTIMATES` (unless the user bought both in Step 3), `owns Two-Shot/Three-Shot slots but the Volley level is 0`, `Tree: slot id ... is already used`; `Asset '...' not found` only for names the user changed; the server game log's `Error handling message` lines are only the known AudioManager ones (spec "Constraints"), and it has no `Already have input` from `BowDrawSkill` after B1-B4 |
| 15 | C1 (if done) | that client's game log: count of `Missing hash 47011`, and any disconnect or exception after them; the server's `MelonLoader\Latest.log` shows the join and no error for that player |

If row 1's Volley skill lines are missing, or row 2's level is 0 or shows the ERROR, stop: the Volley re-point did not take, and Two-Shot/Three-Shot owners would lose their ghosts.

- [ ] **Step 8: Checkpoint**

Tests `ALL PASS (206)`; build `0 Error(s)`. Files changed in this task: `Main.cs`, `CLAUDE.md`; deployed to the three install locations; backup folder created.

---

## Deferred (not in this plan)

- Converge and Ricochet (spec suggested Step 3), Seeking Volley, the Quick visuals and Mark, Weight (stagger, interrupt, the Balance Ring, the Fill, gold ghosts' stagger), Brace, Overcharge, Steady, Snap Draw, the bridges, Quickdraw 4 line windows and the claim tolerance (Step 7), Holy Arrow forks, rods, the pour and the PvP blast (Step 8), the haptic mixer.
- Recolouring ghost heads cyan-only or orange-only: waits for the ramp test's result; shafts carry the colours meanwhile.
- `DrawGrace`'s per-player parts (Snap Draw's 0.55 threshold, Brace's band) and the `TimeoutInput` replacement (Part 12 item 5): with Snap Draw and Brace.
- Echo beads beside a Quickdraw 4 Line (Part 5): echoes fly as normal echoes until Step 7.
- Part 9 item 8, thinning the drawn ghosts past 15 (it matters once Burst's 30 are in the air): Task 10's `LiveCap` covers the casting cost only.
- Part 11's cached ignore list in `Hits.Cast` and the arrow-to-bow map (Task 11 Step 2 caches only missed asset lookups).
