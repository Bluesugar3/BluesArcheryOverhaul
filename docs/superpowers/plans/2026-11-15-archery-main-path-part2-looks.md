# Archery Main Path, Part 2 (looks) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Needles and heavy arrows get their own looks, and the Charge strands and the Holy Arrow wind-up are finished to the standard the user asked for ("very high effort"), so every form can be told apart at a glance.

**Architecture:** Client-side looks only; no gameplay, damage, timing or wire change. Shape and timing maths that can be tested go into the Unity-free `Charge.cs` as pure functions; the drawing stays in small classes in `ChargeFx.cs` and `Volley.cs`. Everything is line renderers and game assets found at run time, as the existing looks are.

**Tech Stack:** C# net472, MelonLoader 0.7.3, HarmonyX, Unity 2020.3 Mono. Tests: the console project `QuickshotRing.Tests` (`Check(name, bool)`, `Near(a, b)`).

**Spec:** `docs/superpowers/specs/2026-11-13-archery-main-path-design.md`, section "Looks" (and "Small rulings"). Part 1 is built (0.10.0): `docs/superpowers/plans/2026-11-13-archery-main-path-part1.md`.

## Global Constraints

- Looks only: no change to what a shot does, to any shared setting's meaning, or to the wire. The server must behave identically before and after (the headless server draws nothing: every look returns early on `ApplicationManager.IsHeadless`).
- Every size, colour, count and timing is a setting in `UserData\QuickshotRing.cfg` (not shared: looks are per client), with the defaults given here.
- Art must match the game: build from the game's own materials, pooled effects and sounds (names in `UserData\QuickshotRing\loaded_assets.txt`; find them with `Fx.FindEffect` / `Fx.FindSound` / `Fx.LineMaterial()` / `Fx.OrbLineMaterial()`); a missing asset logs once and the look degrades, never throws.
- Each effect is its own small class so it can be replaced without touching gameplay code.
- Never remove a piece of feedback before its replacement draws: if a new look fails to build for an arrow, the old one stays for that arrow.
- Minimise lag: no per-frame allocations in a draw loop (reuse arrays, gradients and line renderers); pooled objects go back to their pools.
- The shooter sees everything at full strength; other archers' looks are multiplied by `Volley.OtherArcherBrightness`.
- Unity types stay out of `Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`, `Charge.cs`, `Path.cs`.
- Every new behaviour logs a line starting `[Archery] ` once per draw or per release at most, never per frame.
- Build stays at 0 warnings (`dotnet build QuickshotRing -c Release --no-incremental`). `dotnet run --project QuickshotRing.Tests -c Release` ends `ALL PASS (N)`; N starts at 399 and only grows. Never pass `-p:Deploy`: the user is playing; the controller installs at the end.
- Not a git repo: "commit" means the task's file snapshot, taken by the controller.
- In text the user reads: "Charge Shot", "Quickdraw", "multi-arrow".

## Review Focus

1. An arrow with no measurable shaft (a modded arrow, a shrine training arrow): the strands still draw at the fallback radius; nothing throws. (Task 1 test on the pure radius choice.)
2. A stage time of zero or two equal stage times: pulses and knots do not divide by zero or draw NaN positions. (Task 1 tests.)
3. A draw cancelled or an arrow swapped mid wind-up: motes, the braid and the hum stop and pooled objects are released. (Task 2, by reading; named in the report.)
4. Six heavy arrows and three needles on one release with other archers nearby: no look allocates per frame. (Tasks 3 and 4, by reading; named in the report.)
5. A spectator whose copy of the shooter's build has not arrived: the relayed kind decides the look (a Charged release's ghosts are heavy arrows, a Snap's are needles), not a default build. (Tasks 3 and 4.)

## File Structure

| File | Change |
|---|---|
| `QuickshotRing/Charge.cs` (Unity-free) | New `StrandShape` (taper, pulse, knots, brightness steps) and `WindUp` (colour shift, mote speed, braid) pure functions. |
| `QuickshotRing/ChargeFx.cs` | Strand polish in `DrawStrands`; shaft radius measured from the shaft; new small classes `StrandKnots`, `HolyWindUp`. |
| `QuickshotRing/Volley.cs` | `Ghost` gains a look kind (ghost, needle, heavy); new small classes `NeedleLook`, `HeavyLook`; pellets are never gold. |
| `QuickshotRing/VolleyFx.cs` | The needle "tk" and the heavy arrows' slide-out sound. |
| `QuickshotRing.Tests/ChargeTests.cs` | Tests for `StrandShape` and `WindUp`. |

---

### Task 1: Charge strands, finished

**Files:**
- Modify: `QuickshotRing/Charge.cs`, `QuickshotRing.Tests/ChargeTests.cs`, `QuickshotRing/ChargeFx.cs`

**Interfaces:**
- Produces (Unity-free, `Charge.cs`):
  - `static class StrandShape`:
    - `float Taper(float s, float taperShare)` — width factor along a strand, `s` in 0..1 from hand to head: rises 0 to 1 over the first `taperShare`, 1 in the middle, falls to 0 over the last `taperShare`; `taperShare` clamped to 0..0.5; with 0 it is 1 everywhere.
    - `float Converge(float s, float fromShare)` — radius factor: 1 until `s = 1 - fromShare`, then eases to 0 at `s = 1` (the strand closes onto the head); `fromShare <= 0` gives 1 everywhere.
    - `float Pulse(float s, float sinceThump, float seconds, float width)` — brightness added at point `s` by a pulse that travels hand to head in `seconds` after a thump: a bump of half-width `width` centred at `sinceThump / seconds`; 0 before the thump, after it has passed, and when `seconds <= 0`.
    - `float StageBrightness(int stagesDone, float perStage)` — `1 + perStage * max(0, stagesDone - 1)`.
    - `float ShaftRadius(float[] crossSizes, float fallback, float max)` — the smallest positive cross-size among the arrow's meshes, halved, clamped to `max`; `fallback` when the array is null, empty or has no positive finite value.
- Behaviour (`ChargeFx.cs`), defaults in `[Fill]`:
  - `StrandGap` default changes 0.012 to 0.004 (migrates through `shipped_defaults.txt`).
  - Shaft radius: for each arrow prefab, collect every mesh's two cross-axis extents (the two smaller of its scaled bounds) and take `StrandShape.ShaftRadius(sizes, 0.006f, 0.02f)`; today's code takes the largest mesh's cross-size, which includes the head or fletching. Log the measured value once per prefab as today.
  - Each strand: width multiplied by `Taper(s, StrandTaper = 0.12)`, radius by `Converge(s, StrandConverge = 0.15)`; a second, wider and fainter line under each strand (`StrandHaloWidth = 0.018`, `StrandHaloAlpha = 0.25`, the Fill's halo material); a slight width flicker (`StrandFlicker = 0.15`, one random factor per strand per frame, as the ember does).
  - Pulse: at each stage's thump, `Pulse(s, sinceThump, StrandPulseSeconds = 0.25, 0.12)` times `StrandPulseBoost = 1.5` is added to that strand's brightness along its length (per-vertex colour through a reused `Gradient`, at most 8 keys).
  - Brightness steps: every drawn strand's alpha and whiteness are multiplied by `StageBrightness(f.stage, StrandStageBrightness = 0.25)`, so the arrow visibly gets brighter at each thump.
  - Knots (`StrandKnots`, new small class): each completed stage leaves one small bright knot at the head end (a short thick line ring segment or the game's spark effect if `KnotEffect = ""` names one), spaced `KnotSpacing = 0.012` m back from the head gap; at stage 3 the arrowhead itself glows: request the game's own dissipating tool effect on the head renderers if any are found (the code that exists in `Thump` for `HeadFlare`), else a small steady `Fx.Flash`-style light at the tip (`HeadGlow = true`).
  - The settings `HeadFlare` and the unpatched `Charge2Sound` class are dead since 0.9.1 and 0.10.0: remove them.

- [ ] **Step 1: Failing tests** (`ChargeTests.cs`):

```csharp
Check("strand taper: zero at both ends, full in the middle", Near(StrandShape.Taper(0f, .12f), 0f) && Near(StrandShape.Taper(.06f, .12f), .5f)
      && Near(StrandShape.Taper(.5f, .12f), 1f) && Near(StrandShape.Taper(.94f, .12f), .5f) && Near(StrandShape.Taper(1f, .12f), 0f));
Check("strand taper: none when the share is zero, clamped above a half", Near(StrandShape.Taper(0f, 0f), 1f) && Near(StrandShape.Taper(.25f, 5f), .5f));
Check("strand converge: full radius until the last stretch, zero at the head", Near(StrandShape.Converge(.5f, .15f), 1f) && Near(StrandShape.Converge(.85f, .15f), 1f)
      && StrandShape.Converge(.95f, .15f) < 1f && Near(StrandShape.Converge(1f, .15f), 0f) && Near(StrandShape.Converge(1f, 0f), 1f));
Check("strand pulse: travels hand to head once", StrandShape.Pulse(.5f, .125f, .25f, .12f) > .9f && Near(StrandShape.Pulse(.5f, 0f, .25f, .12f), 0f)
      && Near(StrandShape.Pulse(.1f, .125f, .25f, .12f), 0f) && Near(StrandShape.Pulse(.5f, 1f, .25f, .12f), 0f));
Check("strand pulse: no pulse before the thump or with a zero duration", Near(StrandShape.Pulse(.5f, -1f, .25f, .12f), 0f) && Near(StrandShape.Pulse(.5f, .1f, 0f, .12f), 0f));
Check("strand brightness steps up per stage", Near(StrandShape.StageBrightness(0, .25f), 1f) && Near(StrandShape.StageBrightness(1, .25f), 1f)
      && Near(StrandShape.StageBrightness(3, .25f), 1.5f));
Check("shaft radius: the thinnest mesh, halved", Near(StrandShape.ShaftRadius(new[] { .036f, .011f, .05f }, .006f, .02f), .0055f));
Check("shaft radius: fallback for none, clamp for a fat one", Near(StrandShape.ShaftRadius(null, .006f, .02f), .006f) && Near(StrandShape.ShaftRadius(new float[0], .006f, .02f), .006f)
      && Near(StrandShape.ShaftRadius(new[] { float.NaN, 0f, -1f }, .006f, .02f), .006f) && Near(StrandShape.ShaftRadius(new[] { .2f }, .006f, .02f), .02f));
```

- [ ] **Step 2: Run** `dotnet run --project QuickshotRing.Tests -c Release`. Expected: does not compile (`StrandShape`).
- [ ] **Step 3: Implement** `StrandShape`, then the `ChargeFx` changes.
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Log once per draw: `[Archery] Fill: strands on '<bow>' radius <r> m (shaft <measured> m + gap <g>), <n> owned stage(s)`.

---

### Task 2: The Holy Arrow wind-up

**Files:**
- Modify: `QuickshotRing/Charge.cs`, `QuickshotRing.Tests/ChargeTests.cs`, `QuickshotRing/ChargeFx.cs`

**Interfaces:**
- Consumes: `StrandShape` (Task 1); the existing `HolyGather` record and `HolyShape` in `ChargeFx.cs`; `Paths.HolySecondsFor(build)` and `Paths.StagesFor(build)` (the player's own times); `Volley.GatherSeconds`.
- Produces (Unity-free, `Charge.cs`): `static class WindUp`:
  - `float Shift(float t, float stage3At, float holyAt)` — 0 at stage 3's time, 1 at Holy Arrow's time, clamped; 1 when `holyAt <= stage3At`.
  - `float MoteSpeed(float shift, float slow, float fast)` — `slow + (fast - slow) * shift * shift`.
  - `float Braid(float gather)` — how far the three strands have merged into one cord at the head, 0..1: `gather` clamped, eased (smoothstep).
- Behaviour (`ChargeFx.cs`, new small class `HolyWindUp`, settings in `[HolyWindUp]`), only for an archer with Holy Arrow whose main path is Charge, only after stage 3:
  - Colour: the three strands (and their halos) blend from the Fill's colours to `BeamColour = "#FFF1C2"` by `Shift`.
  - Motes: `MoteCount = 6` small bright points (one reused line renderer or pooled sparks, never a new object per mote per frame) drift along the shaft from the hand end to the head, at `MoteSpeed(shift, 0.25, 1.6)` shaft-lengths per second, wrapping; they fade in over the first 0.3 s after stage 3.
  - Braid: over the gather (the last `Volley.GatherSeconds` before Holy Arrow is ready) the strands' radius is multiplied by `1 - Braid(gather) * 0.85` over the front third of the shaft, so they wind into one cord at the head (this replaces the existing "squeeze toward the tip" of the strands; the core line's squeeze stays).
  - Armed: the cord and head pulse as the core does today (`HolyPulseHz`), and a soft ring breathes around the head (`RingRadius = 0.05` m, `RingWidth = 0.004`, alpha swinging 0.25 to 0.6 at the same rate), drawn with the game's orb-line material.
  - Sound: a looping low hum that rises in pitch with `Shift` (`HumSound = ""`: empty means none; if a name is given and found, play it attached to the tip and stop it on release, cancel or arrow change). The existing armed cue stays.
  - Haptics for the local shooter: a faint tick in the bow hand at each mote's arrival at the head while `shift > 0.5` (`MoteHaptic = 0.15`).
  - Everything stops and is released on release, cancel, a lost charge or another arrow on the string (Review Focus 3).

- [ ] **Step 1: Failing tests** (`ChargeTests.cs`):

```csharp
Check("wind-up shift: 0 at stage 3, 1 at Holy Arrow", Near(WindUp.Shift(2.5f, 2.5f, 6.5f), 0f) && Near(WindUp.Shift(4.5f, 2.5f, 6.5f), .5f)
      && Near(WindUp.Shift(9f, 2.5f, 6.5f), 1f) && Near(WindUp.Shift(1f, 2.5f, 6.5f), 0f) && Near(WindUp.Shift(3f, 3f, 3f), 1f));
Check("wind-up motes speed up toward the end", Near(WindUp.MoteSpeed(0f, .25f, 1.6f), .25f) && Near(WindUp.MoteSpeed(1f, .25f, 1.6f), 1.6f)
      && WindUp.MoteSpeed(.5f, .25f, 1.6f) < .925f);
Check("wind-up braid eases from apart to one cord", Near(WindUp.Braid(0f), 0f) && Near(WindUp.Braid(1f), 1f) && Near(WindUp.Braid(.5f), .5f)
      && WindUp.Braid(.25f) < .25f && Near(WindUp.Braid(-1f), 0f) && Near(WindUp.Braid(2f), 1f));
```

- [ ] **Step 2: Run.** Expected: does not compile (`WindUp`).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Logs, once per draw each: `[Archery] Holy wind-up: started on '<bow>' (stage 3 at <s>, ready at <s>)`, `... braid`, `... armed ring`, `... stopped (<why>)`.

---

### Task 3: The needle look

**Files:**
- Modify: `QuickshotRing/Volley.cs`, `QuickshotRing/VolleyFx.cs`, `QuickshotRing/Quickdraw4.cs`

**Interfaces:**
- Consumes: `SpectralKind.Echo` virtual arrows (needles since part 1), launched in `Volley.Launch`; the relayed release kind (`Draws.ReleaseOf(arrowId)`).
- Produces: `Volley.NeedleLook` (new small class, settings in `[Needles]`): the look of one flying needle.
- Behaviour:
  - A needle is a thin short bright streak, not an arrow: one line `Length = 0.35` m long, `Width = 0.008` tapering to 0 at the tail, colour `Colour = "#FFE9A8"` (the Quickdraw ring's gold, lighter at the head), with no arrowhead mesh and no shaft mesh. Behind it a hairline trail (`TrailSeconds = 0.12`, `TrailWidth = 0.003`).
  - Needles show at release only; nothing is drawn on the held string.
  - Sound: one sharp "tk" per needle at the bow (`Sound = "Click"`, 20 ms apart so three needles are heard as three), through `VolleyFx.Play`.
  - Landing: the existing echo landing spark, tinted the needle colour, at 60% size.
  - A spectator draws needles for a relayed Snap's stage whatever build it holds for the shooter (Review Focus 5).
  - Quickdraw 4's needle lines take the same colour (`Quickdraw4`'s `NeedleLineWidth` stays).
  - If the needle look cannot be built (no line material), the old echo look is used for that needle.

- [ ] **Step 1:** Read `Volley.Ghost` and how an Echo-kind virtual arrow gets its visuals; list in the report every place that branches on `SpectralKind.Echo`.
- [ ] **Step 2: Implement** `NeedleLook` and switch Echo-kind visuals to it.
- [ ] **Step 3: Run tests and build.** Expected: `ALL PASS (unchanged count or more)`, 0 warnings. Log once per release: `[Archery] Needles: arrow N <n> needle(s) drawn (<own|seen>)`.

---

### Task 4: The heavy arrow look

**Files:**
- Modify: `QuickshotRing/Volley.cs`, `QuickshotRing/VolleyFx.cs`, `QuickshotRing/ChargeFx.cs`

**Interfaces:**
- Consumes: heavy arrows as launched in part 1 (the plan's ghost slot on a Charge main's Charged release; `HeavyShowing` on the held string); `StrandShape` (Task 1); `ChargeFx.EmberTrail`.
- Produces: `Volley.HeavyLook` (new small class, settings in `[HeavyArrows]`).
- Behaviour:
  - Every heavy arrow, the sixth included, has the same look: a full-size arrow shape (the ghost's head and shaft meshes at `Scale = 1.15`) in the charge's orange-gold (`Colour = "#FF9A2E"`, head `HeadColour = "#FFC733"`), `Thickness = 1.4` times a ghost's shaft width, at `Alpha = 0.85`.
  - Held: they slide out of the real arrow's tip one pair per multi-arrow orb as stage 1 completes (pairs 0.08 s apart), each wrapped in one turn of a strand like the main arrow's (`StrandShape` taper and converge, the strand colour of stage 1), and they brighten at each later stage's thump with the main arrow.
  - Sound on slide-out: the charge's own thump sound at 60% volume instead of the volley's slide-out (`SlideSound = ""` keeps today's).
  - Flight: each trails embers like the charged arrow (`ChargeFx.EmberTrail`, half its width).
  - Landing: the charged hit effect at 50% size (the Fill's `HitEffect`), not the ghost's blue spark.
  - A spectator draws a relayed Charged release's ghosts as heavy arrows whatever build it holds for the shooter (Review Focus 5).
  - A Multi main's gold ghosts keep the ghost shape (thin, fanned) in gold; Burst's pellets are never gold (they carry no stagger).
  - If the heavy look cannot be built, the gold ghost look is used for that arrow.

- [ ] **Step 1:** Read how part 1 marks a launched ghost as heavy (`heavy` in `Volley.Launch`) and how the held heavy arrows are laid out; list in the report where the look is chosen.
- [ ] **Step 2: Implement** `HeavyLook`, the held slide-out in pairs, the strand wrap, the trail and landing.
- [ ] **Step 3: Run tests and build.** Expected: `ALL PASS`, 0 warnings. Logs once per draw/release: `[Archery] Heavy arrows: <n> on '<bow>' (pair <k> slid out)`, `[Archery] Heavy arrows: arrow N <n> flying (<own|seen>)`.

---

### Task 5: Notes and the play-test guide (controller)

- [ ] Version `0.11.0` in `Main.cs`; `CLAUDE.md`: the new classes in the file table rows for `ChargeFx.cs` and `Volley.cs`, the test count.
- [ ] `docs/playtest-0.11.0.md`: at most 6 tests, four lines each (Skills / Do / You should see / Wrong if), in game terms: the strands sit close to the shaft and finish at the head with knots; the arrow brightens per stage; the Holy Arrow wind-up (colour shift, motes, braid, ring); a quick archer's needles are thin gold streaks with a "tk"; a charge archer's heavy arrows are thick orange arrows that slide out in pairs; a multi-arrow archer's gold ghosts stay thin and Burst's pellets stay blue.
- [ ] Install only when the game and server are not running (`tasklist`); if they are, leave the build uninstalled and say so.

## Not in this plan

Tree colours and the flip warning (part 3), the test tablet (part 4). The deferred part 1 minors that are not looks stay in that plan's ledger.
