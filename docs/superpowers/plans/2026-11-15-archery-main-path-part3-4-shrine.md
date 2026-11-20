# Archery Main Path, Parts 3 and 4 (tree colours and the test tablet) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At the archery shrine the player can see which path is their main one, which orbs are legendary and which purchase would change their main path, and a small tablet says in words and numbers what the held orb does to the build they have.

**Architecture:** Client-side only. One Unity-free file (`Preview.cs`) holds every rule: which line an orb belongs to, the build after a purchase, the tint of each orb, and the tablet's text as plain strings built from the live numbers. Two small Unity classes draw: `TreeColours` (its own rings and lines over the shrine's orbs; the game's materials are never touched) and `Tablet` (a slab with text beside the shrine). Both read the local player's owned orbs from the open shrine's orbs.

**Tech Stack:** C# net472, MelonLoader 0.7.3, HarmonyX, Unity 2020.3 Mono. Tests: the console project `QuickshotRing.Tests` (`Check(name, bool)`, `Near(a, b)`).

**Spec:** `docs/superpowers/specs/2026-11-13-archery-main-path-design.md`, sections "The tree", "The test tablet", "Never worse", "Small rulings". Parts 1 and 2 are built (0.11.0).

## Global Constraints

- Client-side only: no gameplay, no wire, no shared settings, nothing on the headless server (`ApplicationManager.IsHeadless` returns early everywhere).
- Never change a game-owned material, pooled object or orb component: the colours are the mod's own child objects (one Renderer per GameObject; every line or ring on its own child), destroyed when the shrine closes or the orb goes.
- Every size, colour, position and wording is a setting in `UserData\QuickshotRing.cfg` (not shared), with the defaults given here; re-read live (`Cfg.Reloaded`).
- The numbers on the tablet are read from the live settings and tables through one struct, never typed into strings by hand.
- The tablet is a test aid: it says so in one small line, same lettering as the rest; no banner, no red, no larger type. At most 8 short lines at once.
- In text the user reads: "Charge Shot", "Quickdraw", "multi-arrow"; orb names as the user knows them (Quickdraw 1-3, Quickdraw 4, Two-Shot, Three-Shot, Burst, Charge Shot 1-3, Holy Arrow, Quiver Gather).
- No per-frame allocation while the shrine is open; text is rebuilt only when the build, the held orb or the settings change.
- Unity types stay out of `Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`, `Charge.cs`, `Path.cs`, and the new `Preview.cs`.
- Every new behaviour logs a line starting `[Archery] ` when something changes, never per frame.
- Build stays at 0 warnings (`dotnet build QuickshotRing -c Release --no-incremental`). `dotnet run --project QuickshotRing.Tests -c Release` ends `ALL PASS (N)`; N starts at 424 and only grows. Never pass `-p:Deploy`; the controller installs.
- Not a git repo: "commit" means the task's file snapshot, taken by the controller.

## Review Focus

1. The shrine opened by another player: the local player's colours and tablet must not show for someone else's tree (an orb's status reads `OtherPlayer` for everyone but the opener). (Task 2 and 3, by reading; a Task 1 test on the pure "owned from statuses" rule.)
2. An orb that is not one of the mod's known orbs (another mod's slot, the gatherer): no tint, no tablet lines, no exception. (Task 1 tests.)
3. A purchase that both is legendary and flips the main path: the flip warning wins on the tree and the tablet says both. (Task 1 tests.)
4. A build with nothing owned, and a first purchase: "makes you a ... archer", no "from -> to" with an empty side. (Task 1 tests.)
5. More than 8 lines of text: the lowest-priority lines are dropped, the footer and the test note are never dropped. (Task 1 tests.)

## File Structure

| File | Change |
|---|---|
| `QuickshotRing/Preview.cs` (new, Unity-free) | `Lines`, `OrbTint`/`TreeTint`, `BuildPreview`, `FactNumbers`, `BuildFacts`, `TabletText`. |
| `QuickshotRing.Tests/PreviewTests.cs` (new), the tests csproj, `Program.cs` | Tests for `Preview.cs`. |
| `QuickshotRing/ShrineView.cs` (new) | Client: which shrine the local player has open, the orbs in it, the owned set and held orb; raises one "changed" event. |
| `QuickshotRing/TreeColours.cs` (new) | Rings and lines over the orbs. |
| `QuickshotRing/Tablet.cs` (new) | The slab and its text. |
| `QuickshotRing/Tutorial.cs` | Its one line moves onto the tablet; the floating text goes when the tablet is on. |
| `QuickshotRing/Paths.cs`, `Main.cs` | `Paths.Numbers()` (live `FactNumbers`); init calls. |

---

### Task 1: The rules and the words (Unity-free)

**Files:**
- Create: `QuickshotRing/Preview.cs`, `QuickshotRing.Tests/PreviewTests.cs`
- Modify: `QuickshotRing.Tests/QuickshotRing.Tests.csproj`, `QuickshotRing.Tests/Program.cs`, `QuickshotRing/Paths.cs`

**Interfaces:**
- Consumes: `Orb`, `Build`, `Build.From(IEnumerable<Orb>)`, `MainPath`, `MainPathRule.Count/Decide`, `ComplementValues`, `Complements`, `ChargeStages`, `VolleyTimes`.
- Produces (`Preview.cs`):
  - `static class Lines`: `MainPath Of(Orb o)` — Quick1-3 and Quickdraw4 → Quick; Volley, FullVolley, Burst, SeekingVolley → Multi; Charge1-3, HolyArrow → Charge; everything else → None. `bool Legendary(Orb o)` — Quickdraw4, SeekingVolley, HolyArrow. `string Name(Orb o)` — "Quickdraw 1".."Quickdraw 3", "Quickdraw 4", "Two-Shot", "Three-Shot", "Burst", "Seeking Volley", "Charge Shot 1".."Charge Shot 3", "Holy Arrow", "Quiver Gather"; `o.ToString()` for anything else. `string PathName(MainPath p)` — "Quick", "Multi-arrow", "Charge", "no".
  - `static class BuildPreview`: `Build Now(ICollection<Orb> owned, MainPath incumbent)` = `Build.From(owned)` with `Main = MainPathRule.Decide(that, incumbent)`; `Build After(ICollection<Orb> owned, Orb buy, MainPath incumbent)` = the same with `buy` added (a copy; `owned` is not changed), incumbent = `Now(...).Main`; `bool Flips(ICollection<Orb> owned, Orb buy, MainPath incumbent)` = Now's Main is not None and After's Main differs.
  - `enum OrbTint { None, MainPath, Legendary, FlipWarning }` and `static OrbTint TreeTint.For(Orb orb, bool ownedByViewer, ICollection<Orb> owned, MainPath incumbent)`: `FlipWarning` when the orb is not owned and `BuildPreview.Flips`; else `Legendary` when `Lines.Legendary(orb)`; else `MainPath` when `Lines.Of(orb)` is the current main path and not None; else `None`. An orb with `Lines.Of == None` and not legendary is always `None`.
  - `static bool TreeTint.LinkTinted(Orb from, Orb to, MainPath main)` — both ends on the main path's line.
  - `readonly struct FactNumbers { ComplementValues C; ChargeStages Stages; VolleyTimes Volley; float HolySeconds; float InnerShare, OuterShare; float[] QuickMultiplier, ChargeMultiplier; (index = tier 0..3) float[] ChargeStagger; (index = tier 0..3) }` with a constructor taking all of them and `static FactNumbers Default` (ComplementValues.Default, ChargeStages.Default, VolleyTimes.Default, 6.5f, VolleyMath.InnerShare, VolleyMath.OuterShare, {1,1.1f,1.2f,1.3f}, {1,1.25f,1.5f,2f}, {0,0.2f,0.4f,0.6f}).
  - `readonly struct Fact { string Key; string Text; }` and `static List<Fact> BuildFacts.Describe(Build b, FactNumbers n)` — the build's combined result, in this order, only the facts that apply:
    - Quick main: `quick` "Quick shots: x{QuickMultiplier[b.Quick]} damage"; with Quickdraw 4 `line` "Quickdraw 4: instant line shot"; `needles` "Quick shots: +{n} needle(s), {share%} each" when `Complements.Needles > 0`; `snapweight` "Quick shots: stagger {x}" (+ ", interrupt" when `SnapInterrupts`) when `SnapStagger > 0`.
    - Multi main: `volley` "Volley: {5|15} ghost arrows, ready at {VolleyTimesFor.VolleyAt}s" (15 from tier 2; with tier 3 add `burst` "Burst: 15 pellets at {RackAt}s"); `spread` "Volley: {percent}% tighter" when `SpreadFactor < 1`; `gold` "Ghosts: gold, stagger {x} each" when `GhostStagger > 0`. The times are the scaled ones (`n.Volley.Scaled(Complements.VolleyFactor(b, n.C))`).
    - Charge main: `charge` "Charged shots: {b.Charge} stage(s), full at {scaled stage time of tier b.Charge}s, x{ChargeMultiplier[b.Charge]} damage"; `stagger` "Charged shots: stagger {ChargeStagger[b.Charge]}"; with Holy Arrow `holy` "Holy Arrow: ready at {HolySeconds * StageFactor}s"; `heavy` "Charged shots: +{n} heavy arrows, {share%} each" when `HeavyArrows > 0`; `steady` "Charged shots: steadier (Quickdraw {b.Quick})" when `b.Quick > 0`.
    - No main path: one fact `none` "No archery path yet".
    - A legendary owned in a line that is not the main path adds `idle` "{name}: does nothing until {path} is your main path".
    - Number formatting: invariant culture, at most 2 decimals, trailing zeros trimmed ("1.6s", "x1.25", "15%").
  - `static class TabletText`:
    - `List<string> ForBuild(ICollection<Orb> owned, MainPath incumbent, FactNumbers n, string testNote)` — holding nothing: title "Your archery build", then every fact's text, then the footer, then the test note.
    - `List<string> ForOrb(Orb orb, bool owned, string cannotBuy, ICollection<Orb> ownedSet, MainPath incumbent, FactNumbers n, string tryOut, string testNote)` — line 1 `Lines.Name(orb)`; line 2 "{PathName} line, orb {position} of {count in that line incl. legendary}" (omitted for a no-line orb); `tryOut` when not empty (the try-out instruction); then "With your build:" and one line per fact that is new or whose text changed between `Describe(Now)` and `Describe(After)`: a changed fact as "{new text} (was: {old value part})" is too long, so: a new fact is "+ {text}", a changed fact is "{text before the colon}: {old after-colon} -> {new after-colon}"; when owned already, "You own this." instead of the block; then the outcome line: "Keeps you a {path} archer." | "Makes you a {path} archer." (first line orb) | "Makes you a {new} archer (you are {old} now)." ; `cannotBuy` when not empty; the footer; the test note.
    - `string Footer(Build b)` — "Charge {c} · Quick {q} · Multi {m}" using `MainPathRule.Count`, the main path's entry in square brackets, e.g. "[Charge 3] · Quick 1 · Multi 0"; when another line is 1 or 2 orbs from out-counting the main path, append " ({n} more {path} changes your path)" for the nearest one.
    - `List<string> Fit(List<string> lines, int max)` — when over `max`, drop from the fact/change lines at the end of that block first, never the first line, the outcome line, the footer or the test note.
  - `Paths.Numbers()` (Unity side): the live `FactNumbers` from `Paths.Values`, `Tree.Stages`, `Volley.Times`, `Tree.HolySeconds`, the live shares, `Tree.QuickMultiplier(t)`/`Tree.ChargeMultiplier(t)` for t = 0..3 and Weight's stagger settings.

- [ ] **Step 1: Failing tests** (`PreviewTests.cs`, `internal static class PreviewTests { internal static void Run() }`, called from `Program.cs`), with `var n = FactNumbers.Default;` and `HashSet<Orb> S(params Orb[] o)`:

```csharp
Check("lines: every bow orb has its line", Lines.Of(Orb.Quick2) == MainPath.Quick && Lines.Of(Orb.Quickdraw4) == MainPath.Quick && Lines.Of(Orb.Burst) == MainPath.Multi
      && Lines.Of(Orb.HolyArrow) == MainPath.Charge && Lines.Of(Orb.QuiverGather) == MainPath.None && Lines.Of(Orb.Gatherer) == MainPath.None);
Check("lines: names the user knows", Lines.Name(Orb.Volley) == "Two-Shot" && Lines.Name(Orb.FullVolley) == "Three-Shot" && Lines.Name(Orb.Charge2) == "Charge Shot 2"
      && Lines.Name(Orb.Quickdraw4) == "Quickdraw 4" && Lines.PathName(MainPath.Multi) == "Multi-arrow");
var c3q3 = S(Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Quick1, Orb.Quick2, Orb.Quick3);
Check("preview: a tie keeps the incumbent, Quickdraw 4 flips it", BuildPreview.Now(c3q3, MainPath.Charge).Main == MainPath.Charge
      && BuildPreview.Flips(c3q3, Orb.Quickdraw4, MainPath.Charge) && BuildPreview.After(c3q3, Orb.Quickdraw4, MainPath.Charge).Main == MainPath.Quick && c3q3.Count == 6);
Check("preview: a first orb is not a flip", !BuildPreview.Flips(S(), Orb.Quick1, MainPath.None) && BuildPreview.After(S(), Orb.Quick1, MainPath.None).Main == MainPath.Quick);
Check("tint: flip warning beats legendary beats main path", TreeTint.For(Orb.Quickdraw4, false, c3q3, MainPath.Charge) == OrbTint.FlipWarning
      && TreeTint.For(Orb.HolyArrow, false, c3q3, MainPath.Charge) == OrbTint.Legendary && TreeTint.For(Orb.Charge2, true, c3q3, MainPath.Charge) == OrbTint.MainPath
      && TreeTint.For(Orb.Quick2, true, c3q3, MainPath.Charge) == OrbTint.None);
Check("tint: an owned orb never warns; unknown orbs are never tinted", TreeTint.For(Orb.Quick3, true, c3q3, MainPath.Charge) == OrbTint.None
      && TreeTint.For(Orb.QuiverGather, false, c3q3, MainPath.Charge) == OrbTint.None && TreeTint.For(Orb.None, false, c3q3, MainPath.Charge) == OrbTint.None);
Check("tint: links on the main path only", TreeTint.LinkTinted(Orb.Charge1, Orb.Charge2, MainPath.Charge) && !TreeTint.LinkTinted(Orb.Quick1, Orb.Quick2, MainPath.Charge)
      && !TreeTint.LinkTinted(Orb.Charge1, Orb.Charge2, MainPath.None));
var chargeFacts = BuildFacts.Describe(BuildPreview.Now(S(Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Quick1, Orb.Volley), MainPath.Charge), n);
Check("facts: a charge archer with complements", chargeFacts.Exists(f => f.Key == "charge" && f.Text == "Charged shots: 3 stages, full at 2.2s, x2 damage")
      && chargeFacts.Exists(f => f.Key == "heavy" && f.Text == "Charged shots: +2 heavy arrows, 15% each") && chargeFacts.Exists(f => f.Key == "steady")
      && !chargeFacts.Exists(f => f.Key == "quick" || f.Key == "volley"));
Check("facts: nothing owned", BuildFacts.Describe(BuildPreview.Now(S(), MainPath.None), n).Count == 1 && BuildFacts.Describe(BuildPreview.Now(S(), MainPath.None), n)[0].Key == "none");
Check("footer: counts, the main path marked, the nearest flip", TabletText.Footer(BuildPreview.Now(c3q3, MainPath.Charge)) == "[Charge 3] · Quick 3 · Multi 0 (1 more Quick changes your path)"
      && TabletText.Footer(BuildPreview.Now(S(Orb.Charge1, Orb.Charge2, Orb.Charge3), MainPath.None)) == "[Charge 3] · Quick 0 · Multi 0");
var buyTwoShot = TabletText.ForOrb(Orb.Volley, false, "", S(Orb.Quick1, Orb.Quick2, Orb.Quick3), MainPath.Quick, n, "", "Test aid: this tablet is temporary.");
Check("tablet: buying Two-Shot as a quick archer", buyTwoShot[0] == "Two-Shot" && buyTwoShot[1] == "Multi-arrow line, orb 1 of 4" && buyTwoShot.Contains("With your build:")
      && buyTwoShot.Contains("+ Quick shots: +1 needle, 15% each") && buyTwoShot.Contains("Keeps you a Quick archer.")
      && buyTwoShot[buyTwoShot.Count - 1] == "Test aid: this tablet is temporary." && buyTwoShot[buyTwoShot.Count - 2] == "[Quick 3] · Charge 0 · Multi 1");
var flip = TabletText.ForOrb(Orb.Quickdraw4, false, "", c3q3, MainPath.Charge, n, "", "note");
Check("tablet: a legendary that flips says so", flip.Contains("Makes you a Quick archer (you are Charge now)."));
var first = TabletText.ForOrb(Orb.Charge1, false, "", S(), MainPath.None, n, "Pull fully and hold.", "note");
Check("tablet: a first orb", first.Contains("Makes you a Charge archer.") && first.Contains("Pull fully and hold.") && !first.Exists(l => l.Contains("->")));
Check("tablet: an owned orb and one that cannot be bought", TabletText.ForOrb(Orb.Charge1, true, "", S(Orb.Charge1), MainPath.Charge, n, "", "note").Contains("You own this.")
      && TabletText.ForOrb(Orb.HolyArrow, false, "Another legendary orb is owned.", S(Orb.Charge1), MainPath.Charge, n, "", "note").Contains("Another legendary orb is owned."));
var changed = TabletText.ForOrb(Orb.Quick2, false, "", S(Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Quick1), MainPath.Charge, n, "", "note");
Check("tablet: a changed fact reads from -> to", changed.Exists(l => l.StartsWith("Charged shots: ") && l.Contains(" -> ")));
var many = new List<string> { "A", "B", "With your build:", "1", "2", "3", "4", "5", "6", "Keeps you a Quick archer.", "footer", "note" };
var fit = TabletText.Fit(many, 8);
Check("tablet: fits eight lines, keeping the ends", fit.Count == 8 && fit[0] == "A" && fit[7] == "note" && fit[6] == "footer" && fit.Contains("Keeps you a Quick archer.") && !fit.Contains("6"));
Check("tablet: an unknown orb has a name and no line", TabletText.ForOrb(Orb.QuiverGather, false, "", S(), MainPath.None, n, "", "note")[0] == "Quiver Gather");
```

Notes on the expected values: Charge 3 + Quick 1 → stage factor 0.88, stage 3 at 2.5 × 0.88 = 2.2 s; the footer lists the main path first then the other two in the order Charge, Quick, Multi (so a Quick main reads "[Quick 3] · Charge 0 · Multi 1"; a Charge main "[Charge 3] · Quick 3 · Multi 0"). `Fit` decides which lines are droppable by position: lines after "With your build:" (or after the title when there is no such line) up to the outcome line.

- [ ] **Step 2: Run** `dotnet run --project QuickshotRing.Tests -c Release`. Expected: does not compile (`Lines`).
- [ ] **Step 3: Implement** `Preview.cs`, add it to the tests csproj, implement `Paths.Numbers()`.
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS (N ≥ 442)`, 0 warnings.

---

### Task 2: The open shrine and the tree colours

**Files:**
- Create: `QuickshotRing/ShrineView.cs`, `QuickshotRing/TreeColours.cs`
- Modify: `QuickshotRing/Main.cs`

**Interfaces:**
- Consumes: `Lines`, `TreeTint`, `BuildPreview` (Task 1); `Tree.OrbBySlot` (slot hash → `Orb`); `Paths.LocalIncumbent`; `Builds.LocalBuild()`.
- Produces:
  - `static class ShrineView` (client): tracks the Ranged shrine the LOCAL player has open. `static bool Open`; `static IReadOnlyList<SkillOrb> Orbs`; `static HashSet<Orb> Owned` (orbs whose `SkillOrb.Status` is `TestingLockedOwned`, `OwnedLocked`, `TestingOwned` or `Owned`); `static MainPath Incumbent` (the local build's `Main`, else `Paths.LocalIncumbent`); `static SkillOrb? Held` (the orb open in the try-out area: `SkillShrineTestingArea.Open()`'s `openSkillOrb`, cleared on `Disable`); `static event Action Changed` raised when the shrine opens or closes, an orb's status changes (`SkillOrb.AlowedUpdated`), the held orb changes or the settings reload. A shrine whose orbs all read `SkillStatus.OtherPlayer` is someone else's: `Open` stays false. Find the client-side hooks in `_decomp\SkillShrine.cs` (`OpenOnClient`, `CloseInternal`, the orb spawn) and `_decomp\SkillOrb.cs` (`InitializeSkill`, `UpdateAllowed(SkillStatus, bool)`); orbs arrive one by one after the shrine opens, so `Changed` fires as they come.
  - `static class TreeColours` (client), settings in `[TreeColours]`: `Enabled = true`, `Legendary = "#E8DCFF"`, `Quick = "#FFC733"`, `Multi = "#5BC8FF"`, `Charge = "#FF8A2E"`, `FlipWarning = "#FFB000"`, `RingRadius = 0.062` m, `RingWidth = 0.006`, `LineWidth = 0.008`, `WarningPulseHz = 1.5`.
- Behaviour: on `ShrineView.Changed`, for every orb in `ShrineView.Orbs` with a known `Orb`: `TreeTint.For(...)` decides a ring (a line-renderer circle on a child object of the orb, facing the viewer's side of the tree as the orb's icon does; hidden for `OrbTint.None`): main path → that path's colour, legendary → the legendary colour, flip warning → the warning colour, pulsing in alpha at `WarningPulseHz` (the only per-frame work: one alpha set per warning ring). For every dependency link between two orbs where `TreeTint.LinkTinted`, a line in the path's colour drawn just in front of the game's own connection line (the game's line and its shared material are never touched). Everything is destroyed when the shrine closes.

- [ ] **Step 1:** Read the hooks named above in `_decomp`; write in the report which methods are patched and why they run on the client.
- [ ] **Step 2: Implement** `ShrineView`, then `TreeColours`; call their `Init()` from `Main`.
- [ ] **Step 3: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Logs: `[Archery] Shrine: open for the local player (<n> orbs, main path <path>)`, `[Archery] Tree colours: <a> main-path, <b> legendary, <c> flip-warning ring(s), <d> link(s)` on each change, `[Archery] Shrine: closed`.

---

### Task 3: The test tablet

**Files:**
- Create: `QuickshotRing/Tablet.cs`
- Modify: `QuickshotRing/Tutorial.cs`, `QuickshotRing/Main.cs`

**Interfaces:**
- Consumes: `ShrineView` (Task 2), `TabletText`, `Paths.Numbers()` (Task 1), `Tutorial`'s per-orb lines.
- Produces: `static class Tablet` (client), settings in `[Tablet]`: `Enabled = true`; `Offset = "0.75, 0.95, 0.15"` (metres right, up, forward from the shrine's root, in the shrine's own axes); `Tilt = 20` (degrees leaning back); `Yaw = -15` (turned toward the player standing at the tree); `Width = 0.34`, `Height = 0.24`; `SlabColour = "#1B1E24"`; `TextColour = "#F2EFE6"`; `TextSize = 0.0105`; `Font = ""` (empty: the first font loaded in the game whose name is not Arial, else the built-in Arial; the chosen font's name is logged); `MaxLines = 8`; `TestNote = "Test aid: this tablet is temporary."`.
- Behaviour:
  - While `ShrineView.Open`: a thin dark slab (a quad or thin cube with an unlit colour material; a game stone material if `SlabMaterial = ""` names one that is found) at the offset, with one text mesh on it, left-aligned, the first line slightly larger (`TitleScale = 1.25`), a thin pale border line. Readable from where the player stands at the tree.
  - Text: `TabletText.ForOrb(...)` while an orb is held (`ShrineView.Held`), else `TabletText.ForBuild(...)`, passed through `TabletText.Fit(lines, MaxLines)`. Rebuilt only on `ShrineView.Changed`. `cannotBuy` comes from the held orb's status: `CantBuy`/`TestingCantBuy` → "Cannot buy this yet." (another legendary owned → "Another legendary orb is owned." when the mod's one-ultimate rule is the reason: `UltimateRule.Blocks`); `tryOut` is the orb's `Tutorial` line.
  - `Tutorial`: when `[Tablet] Enabled` is on and the tablet exists, the floating try-out text is not shown (the tablet carries the line); with the tablet off, it shows as before.
  - Everything is destroyed when the shrine closes. One Renderer per GameObject.

- [ ] **Step 1: Implement** `Tablet`; change `Tutorial` as above.
- [ ] **Step 2: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Logs: `[Archery] Tablet: shown at <position> (font '<name>')`, `[Archery] Tablet: <first line> | <line count> line(s)` on each rebuild, `[Archery] Tablet: hidden`.

---

### Task 4: Notes and the play-test guide (controller)

- [ ] Version `0.12.0` in `Main.cs`; `CLAUDE.md` file table rows for `Preview.cs`, `ShrineView.cs`, `TreeColours.cs`, `Tablet.cs`; the test count.
- [ ] `docs/playtest-0.12.0.md`: at most 6 tests in the four-line format: the main path's orbs and links are coloured; legendary orbs are pale; an orb that would flip the path pulses amber and the tablet says so; the tablet lists the build when nothing is held and the held orb's effect when one is; the footer counts; the tablet's position and readability.
- [ ] Install when the game and server are not running.

### Task 3 design addendum (2026-11-15)

After seeing a preview the user asked for the tablet to be built from the game's own stone in the game's layout. The tablet is the game's ruins stone atlas (`ruins_02_diffuse`: carved meander band with its glow on top, the smooth slate band as the writing face, the brick row as the foot), lettered in the game's Vinque font with a rune glyph ornament. The full addendum is in the task's brief (`.superpowers/sdd/2026-11-15-archery-main-path-part3-4-shrine/task-3-brief.md`); the reference image is `QuickshotRing/Art/drafts/preview_tablet.png`, made by `QuickshotRing/Art/make_shrine_preview.py`.

## Not in this plan

A purpose-made tablet model or the game's own lettering beyond picking a loaded font; warnings on sales; anything on the tablet for other skill trees.
