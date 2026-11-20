# Archery Step 4: Weight, Brace, Overcharge, Steady and the Echo Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship 0.9.0: the Charge line gets its own feedback and weight (the Fill and the thump replace the game's glow in the same build, Charge 2's ember head, Charge 3's shell and pierce, stagger batched per creature per release into one cripple hit, the reduced interrupt behind a switch, the Balance Ring, the 4 s knock-down immunity), two new side orbs (Brace: rest the string at 40% without losing anything, Holy Arrow included; Overcharge: hold to 3.75 s for a knock-down and one more pierce), Steady on Quickdraw 1-3 (less wobble on every shot, faster snaps only), and the echo fix, so the next play-test answers the user's two complaints: "the arrow never glows and doesn't go into the second charge" and "Holy Arrow is lost when the string relaxes for half a second".

**Architecture:** As in Steps 1 to 3, every rule that can be stated without Unity lives in Unity-free files the test console compiles: the claim-trust rule, the clock's brace hold and the resolver's overcharge (`Rules.cs`, `Release.cs`), the test build's `Without` (`Build.cs`), and a new `Charge.cs` (Weight's carry, stagger ledger and interrupt rule, the Brace tracker, Steady, the Fill timeline, the Balance Ring's easing). Thin Unity-bound files hold the patches and the looks: `Weight.cs` (server: the real arrow's hit, stagger, interrupt, immunity, pierce, the knock-down probes), `Brace.cs`, `Steady.cs`, `ChargeFx.cs` (the Fill, thumps, collar, Overcharge's heat, hit effects) and `Balance.cs`. No new message types: Brace and Overcharge ride in the existing build bits (`SideOrbs`), the release decision in the existing claim, and the server judges time from the client's claimed crossing when it is ahead by no more than a measured tolerance.

**Tech Stack:** C# (`net472`, `LangVersion latest`, nullable on), MelonLoader 0.7.3, HarmonyX (`0Harmony.dll` from `MelonLoader\net35`), Unity 2020.3 Mono, `dotnet` SDK for the build and the test console, Python 3 + Pillow for the icon script.

**Spec:** `docs/superpowers/specs/2026-11-12-archery-combinations-design.md`. This plan implements Part 13 Step 4 and its "Must also log" list, plus the echo disagreement (Part 12 item 2, "The echo disagreement ... fixed in Step 4"). It uses Part 1 rules 4 to 8, Part 2 (the thump at 1.75 s, the overcharge at 3.75 s), Part 4 entries Charge 1 to 3, Brace, Overcharge and Quickdraw 1 to 3 (Steady only), Part 9 items 1, 3, 5, 9, 10, 14 and 15, Part 10 (stagger values, guards), Part 11 (one call per creature per release), Part 12 items 5 (Brace's floor only), 7, 8, 11, 13 (gradient test, 'Hot Effect', the glow switch-off) and 14, and Part 14. Read Part 4's Charge 1, Brace and Overcharge entries, Part 9 item 15, Part 10 "Guards" and Part 12 items 2, 7 and 8 before starting any task. Play-test 2's results are in `.superpowers/sdd/2026-11-13-archery-steps2-3/progress.md` under "Play-test 2".

**Not in this plan (later steps; say so if asked):** Mark (Step 5; **the spec's Step 4 test item "a charged hit using up a mark for the extra 0.4" moves to Step 5 with Mark**, and Step 5 adds its setting and hook), Snap Draw (Step 5; Brace honours its 55% loose line only if the test build grants it), the Quick visuals (the Ring's Edge colour, the Streak, spikes, the Sight; Step 5), Seeking Volley (Step 6), Quickdraw 4's line windows, charged Lines, the Lance, the Brace re-pull window and the claim tolerance for windows (Step 7), Holy Arrow's forks, rods, motes, the armed core's arcs, the PvP blast and Overcharge's full-strength blast stun (Step 8; Task 12 pulls only the Fill's half of the pour forward, see below), the Fill's haptic swell in the bow hand (spec Part 4, Charge 1; with the haptic mixer), the bridges including Gold Volley (Step 9), the haptic mixer, the white-hot look of overcharged ghosts, the orange-only recolour of the arrowhead (waits for the ramp-swap result), and the Fill's frost-white re-skin under Quickdraw 4 (comes with Step 7's windows).

## Global Constraints

- One DLL, same build, on the dedicated server and every client. Version after this plan: `0.9.0` (`[assembly: MelonInfo(typeof(QuickshotRing.Main), "QuickshotRing", "0.9.0", "dmand")]`).
- No new network message types and no networked objects. The build bits (32 bits, version 1) and the shot claim (version 1) keep their layouts; Brace and Overcharge are already bits of `SideOrbs` (`Build.cs:31`), Steady's tier is `Build.Quick`.
- The server decides; clients only predict and draw. Spectators take the server's decision from the relayed claim.
- Unity-free files (`Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`, and new `Charge.cs`) use only `System.*`: no `UnityEngine`, `MelonLoader`, `HarmonyLib` or game types. The test console compiles exactly these six files.
- Only APIs that exist in Unity 2020.3's Mono (`net472`): no `MathF`, `HashCode`, `System.Range`, `Math.Clamp`.
- Do not raise `Cfg.SettingsVersion` (`Cfg.cs:17`). This plan changes no shipped default; any default a later fix changes goes through the per-key migration (`Cfg.MigrateSettings`, `Cfg.cs:86-115`).
- Every new number and colour is a live setting in `UserData\QuickshotRing.cfg` (Settings table). A setting both the server and the clients compute with is wrapped in `Cfg.Shared(...)` (`Cfg.cs:48-53`); server-only and look-only settings are not.
- Each visual effect is its own small class. Art reuses the game's own effects, sounds, materials and textures by name (`UserData\QuickshotRing\loaded_assets.txt`); never spawn the game's teleport, unlock, buff aura, echolocation dome or sword target-orb effects ('Orb Connection Line' is borrowed as a material only).
- Nothing the mod adds damages or staggers players: spectral hits keep skipping them (`Hits.cs:106`, `:216`), and Weight's stagger, interrupt and pierce skip every `PlayerHealth` at each step where they act (the hook, `Weight.Tick` before dealing, the interrupt, `PierceRule` and `Hits.Copy`: defence in depth). Holy Arrow's blast is unchanged.
- Never remove a piece of feedback before its replacement ships (spec Part 9 item 15, binding). The game's glow on Slowdraw goes only while the Fill is drawn on that arrow; Holy Arrow's (Lightbeam's) glow stays until Step 8's armed core ships.
- Server state is per bow string and per arrow id, never a single static "current shot". Client looks are per bow string.
- Every hit the mod builds by hand (`Hits.Apply`, `Hits.FlushSpectral`, `Hits.Copy`) runs with `Hits.Dealing` set, and the real-arrow hook ignores hits made while it is set (Review Focus 1).
- The assistant cannot run the game. Every behaviour that is not unit-tested emits one greppable line starting `[Archery] ` on the side where it happens, and every new Harmony patch logs once that it fired. Patch targets and reflection names are only checked at runtime.
- RevampedSkills stays disabled on the local server. `DrawGrace` keeps `[HarmonyPriority(Priority.Low)]` (`Draws.cs:260`).
- Not a git repository: no git commands. Each task ends with a Checkpoint: run `dotnet run --project QuickshotRing.Tests -c Release` and `dotnet build QuickshotRing -c Release` from `C:\Users\dmand\OneDrive\Desktop\attmod`, expect the stated `ALL PASS (n)` and `0 Error(s)`, keep the warning count at the 0.8.0 baseline (0), and list the files changed. Nothing is deployed by an implementer.

## Review Focus

1. **The mod's own hand-made hits re-entering the real-arrow hook.** `ImpactHitWithTool.Hit()` always ends in `ImpactDetector.OnToolHit` (`_decomp/Alta.Impact/ImpactHitWithTool.cs:106-111`), and spectral hits, Lines and pierce continuations all use the real arrow's own tool. Without a guard, one ghost hit would count as the real arrow's hit: a second stagger, an interrupt, a pierce from a ghost, a pierce chaining off its own continuation. Pinned by `Hits.Dealing` (Task 4 Step 1), the hook's early return (Task 4 Step 3) and log row 8 ("one `Hit:` line per real arrow hit, and none for spectral, Line or pierce hits").
2. **A long brace must hold everything and re-arm nothing.** The game ends a pull the instant the string reads at or below 68% (`_decomp/BowStringPullbackInputMethod.cs:106-109`), the draw clock ends its span on the first low update (`Rules.cs:106-108`), Holy Arrow's gate needs that span (`HolyArrow.cs:121`), and the ghost count reads the clock (`Volley.cs:116-121`). Pinned by the clock-hold tests (Task 1), the Brace tracker tests (Task 2), `DrawGrace`'s brace branch keeping every input in range so a re-pull past 68% never calls `WaitForCharge` again (`BowStringPullbackInputMethod.cs:99-104`, Task 7), and play-test rows B1 and B2.
3. **An interrupt that ends a knock-down, hits a Wyrm, or juggles.** `Agent.CancelCurrentAction` cancels whatever is running (`_decomp/Alta.Intelligence/Agent.cs:461-464`), which can be the creature's Stunned action, and the creature re-decides at once (`Agent.cs:351-360`), often picking the same move again. Pinned by the tests "weight interrupt leaves Wyrms and knocked-down creatures alone", "weight interrupt cooldown", "weight interrupt only cancellable actions", "weight interrupt switch" (Task 2) and log row 8 (every interrupt names the action and its cancellable flag, and what the creature does 0.3 s later).
4. **The shooter seeing something the server will not do because the server's view of the pull lags.** Echoes (play-test 2), the ratchet count, Burst's rack, Overcharge and Snap versus Early (Steady's faster snaps, the Line) are decided on both sides. Pinned by the trust tests, "echo fix: a crossing only the client saw still fires the echoes" and the `ClaimTrust.SnapReady` tests (Task 1), the server deciding with `ClaimTrust.EffectiveMs` and the claimed snap (Task 3), the thump waiting for the server's own Slowdraw status (Task 9 Step 3), and log row 11 (every client `Snap` matched by a server `Snap` for the same arrow).
5. **A charged shot with no spectral arrows at all** (the user's own character owns no Volley orb). Stagger must not wait for a release ledger that never opens (`Hits.OpenRelease` returns early for 0 arrows, `Hits.cs:196`), and `Weight.Tick` must run on the headless server, where `Main.OnUpdate` returns early at `Main.cs:98`. Pinned by "ledger waits for the batch" and "ledger batches one release on one creature" (Task 2), `Weight.Tick` placed right after `QuiverGather.Poll()` (Task 4 Step 4); log row 8 (the stagger line about 0.2 s after its `Hit:` line) and row 1 (`Weight: tick is live (server)`).

Further failure modes the review must name (each pinned by a test or a log row):

6. **Players are never staggered, interrupted or pierced into.** `Weight.Tick`, `Hits.Copy`, `PierceRule` (players pass through) and the interrupt each skip `PlayerHealth` (Global Constraints). Pinned by "pierce passes through a player" (Task 2) and log row 8: no `Weight:` line names a player.
7. **A braced release versus Holy armed versus a snap.** A re-pull past 68% after a brace must keep the original crossing, or the shooter (who owns Quickdraw 4) fires a Line. Pinned by "a re-pull after a brace keeps the crossing (never a snap)" (Task 1), the B2 "Broken if" ("the pulled-back release fires a Quickdraw line or a quick snap") and log row 6.
8. **The 100 m/s network clamp with Steady.** A faster bow or a lighter arrow (`_decomp/Shooter.cs:99`, `maxExtra`) times 1.3 can pass `PhysicsSerializationSettings.VelocityMax`, where the shooter's and server's arcs would differ. Pinned by the `SteadyRule.Boost` tests (Task 2) and the `(capped at ...)` log text (Task 8).
9. **Spectators and the arrow's speed.** The relay carries only Kind and Stage, and a Line is relayed as Snap. Pinned by the `ClaimRules.SeenUltimate` tests (Task 1), used by the spectator branch of `Draws.Release` and by Steady. Untested in VR without a second client.
10. **Two archers on one creature.** Each release is its own ledger entry ("two archers: a second release on the same creature is its own hit", Task 2); the interrupt cooldown and the knock-down immunity are per creature whoever caused them (`MayInterrupt(10f, 8f, 3f, NaN, 4f, true, false, false, true) == false` in "weight interrupt cooldown"; `Immune` in "weight immunity lasts four seconds" covers the second archer's batch after the first archer's knock-down). Untested in VR without a second client.
11. **The Fill on other players' bows.** `ChargeFx.FillShownFor` works per arrow (Task 9 Step 4), so the game's glow shows on any arrow that has no Fill drawn. Untested without a second client.

Also kept in view (tests exist): Brace's grace restarting after a return above the floor ("brace back above the floor restarts the grace"); a lost brace never re-opening ("brace lost stays lost after the charge ends; Reset clears it"); a Line or a beam never getting Steady's speed ("steady a Line does not fly"); the 10961 ms clock bug never trusted ("trust: client far ahead"); a span never held for an arrow that left ("clock hold needs the crossing's arrow").

---

## Decisions made here (spec "Left to the builder", and where this plan departs)

**Ids** (fixed forever once shipped). Slot ids are the Step 2 constants: Brace `1259921049`, Overcharge `1442249570` (`Build.cs:136`). New passive skill ids (for the skill objects the two orbs point at, as Quiver Gather's `1414213562`): `SlotIds.BraceSkill = 1189207115`, `SlotIds.OverchargeSkill = 1316074013` (`int`, never sent in a status message, so the Volley skill's 65535 limit does not apply; checked unused at runtime).

**The echo fix: the server decides with the client's claimed time when the client is ahead by at most `[Claim] TrustMs` (1000 ms).** The other option, the client waiting for the server, needs a server-to-shooter carrier for uncharged shots, and there is none: the relay skips the shooter (`_decomp/Shooter.cs:62-65`, `SendToChunks(args, player)`) and an early shot sends no "consumed" status. Trusting the claim needs no message at all, because the claim (with the client's milliseconds since its crossing) is read before `Shoot` runs (`Shooter.cs:45-61`; `Claims.cs:96-108`). Measured gaps (server minus client at receipt, play-test 2 server log): -58 to -111 ms on snaps and short holds, -303 to -378 ms on 4 to 7 s holds. Two logged pairs (`client 8190 ms, server 7622 ms`, `client 8190 ms, server 9402 ms`) are not measurements: the claim's time field saturates at `ShotClaimWire.TimeMax` 8190 (`Wire.cs:34`, `:43`), so a claim at 8190 means "at least 8190" and is never trusted as an exact time (harmless: every beat the effective time decides comes before 8 s). 1000 ms is about 2.6 times the largest measured gap and also covers a slow pull that hovers at 68%, where the server sees no crossing at all and the client's claim is the only time; a client more than 1000 ms ahead is not trusted (the 10961 ms clock bug of play-test 1 is refused), and a client behind the server is never used. **Departures:** the same effective time also decides the ghost ratchet, Burst's rack and Overcharge, which are the same kind of disagreement (a held release in the ratchet would otherwise fly ghosts the server does not fire). And Snap versus Early: the server's own Quickdraw input lags the same way (a snap 83 ms after the client's crossing is Early on the server), which would make the shooter's Steady speed, echoes-on-snap and Line disagree with the server's; so the server also accepts the client's claimed `Snap` when the claim's time is trusted and inside Quickdraw's window (`ClaimTrust.SnapReady`). The game's own Quickdraw effect (`BowDrawSkill.OnArrowShot` checks its input's `IsActive`, `_decomp/Alta.Progression.Skills/BowDrawSkill.cs:63-65`) still follows the server's input; only the mod's decision changes. Line windows stay Step 7's.

**Brace.** Floor 0.40, grace 0.6 s below it, loose line 0.68 (0.55 when `SideOrbs.SnapDraw` is in the build). Active only after the thump (Slowdraw's input active on the server, Slowdraw's `Charged` status seen on the client). While braced or in the grace, `DrawGrace` keeps every input of the bow in range (Quickdraw's included), and the draw clock holds its span. A release past the loose line after bracing, or any release with Holy Arrow armed, goes out at strength 1 (the shooter's client raises `strength` in a prefix on `Shooter.Shoot`, so the shot message carries it to the server and spectators; `Shooter.cs:121-122`). **Departure:** the armed-Holy case also covers a light release without Brace (spec rule 7, Part 12 item 6's full-draw beam speed), done through the release strength and, as a backstop for a shooter whose client missed or lagged Lightbeam's `Charged`, in `BeamShot` too: the beam's speed is worked out from the shot's own strength back to the full-draw speed (the launch speed is linear in strength, `_decomp/Shooter.cs:99`, `:104`) before the `[HolyArrow]` speed factor. A lost brace stays lost until the arrow is shot, the string is let go or the arrow changes.

**Overcharge.** At `[Overcharge] Seconds` 3.75 after the crossing (effective time on the server), with the shot charged and Overcharge in the build. Stagger 1.0 instead of the tier's, pierce +1, ghost stagger 1.0 × share × 0.5.

**Weight.** Stagger 0.2 / 0.4 / 0.6 by Charge tier, 1.0 overcharged; one cripple hit per creature per release (spec Part 10 Guards, Part 11): the real arrow's stagger and its ghosts' (`tier stagger × share × 0.5`) are summed per creature and dealt when the release's last spectral arrow has landed (`Hits.Close` → `Weight.ReleaseClosed`), or `[Weight] MaxWaitSeconds` (2 s) after the first one landed if the release never closes. A release with no spectral arrows out (none fired, `Hits.IsOpen` false) is dealt `[Weight] BatchSeconds` (0.2 s) after its first stagger, so a charged shot with no Volley orb never waits. **Accepted edge:** a real arrow that lands after its release has already closed is dealt as its own hit (normally the arrow lands with or before its ghosts, which fly the same model). The cripple amount is `stagger × HolyArrow.BlastCripple` (1.0 = the blast centre, spec Terms), dealt the way the blast deals its cripple (`HolyArrow.cs:304-306`: an `ImpactHit` of type Spiritual through `HealthObject.DetectImpact`), with zero health damage. Interrupt on a charged hit, and on a snap (a Quickdraw 4 Line included, rule 3) with Charge 2 or higher owned; only when the creature's current action is cancellable (`_decomp/Alta.Intelligence/IActionSettings.cs:9`), never on a Wyrm (`[Weight] InterruptWyrms` false), never within 4 s of a knock-down by the game, 3 s cooldown, all behind `[Weight] Interrupt` (the user's fallback switch). **Risk to watch:** a non-cancellable action makes the agent stop re-deciding until it finishes (`Agent.cs:281-284`), so attacks may well be non-cancellable and the interrupt may do nothing visible on them; and `CancelCurrentAction` only calls `ActiveAction.Cancel()` (`Agent.cs:461-464`), whose handler clears the action and sets `nextUpdate = 0` (`Agent.cs:351-360`), so the creature re-decides on the next agent update and will often start the same attack again from the beginning (a snap back, not a pause). Log row 8 settles both before any tuning. Pierce: from the real arrow's hit point along its pre-impact direction, each hop up to `[Weight] PierceRange` 8 m measured from the last hit point, chosen by the Unity-free `PierceRule` (a solid without health stops it, players are passed through and never hit, the creatures already struck are skipped); the next creature takes a copy of the real hit with **the same base damage** (the hit's `Multipliers` and `Effectiveness`, the arrowhead's own cripple included; the first hit's weak-spot and directional bonuses, which are applied to the hit after it is built, are not carried over, the second creature's own type and armour multipliers apply, and enchantment modifiers fire again on the copy because `.Hit()` re-runs `OnHittingImpactDetector`, `_decomp/Alta.Impact/ImpactHitWithTool.cs:106-111`); **decided here:** Weight's stagger and the interrupt go to the first creature only (Overcharge says "1.0 stagger to the first creature").

**Steady.** Wobble ×0.75 / 0.5 / 0.35 by `Build.Quick` on every shot, through one game deviance modifier per tier added to `Arrow.FlightModifiers` (`_decomp/Arrow.cs:116`, `_decomp/ArrowFlightSettings.cs:65-85`); snaps (not Lines) leave 10 / 20 / 30% faster. The speed is applied as the speed argument of `Arrow.Shoot(Vector3, float, bool)` (`Arrow.cs:268-278`), so the server's flight message (`Arrow.cs:235-253`) and the velocity `Arrow.Shoot` returns (which the spectral arrows launch from, `_decomp/ExtraArrowUpdater.cs:99`) both carry it. It is decided in a prefix on `Shooter.Shoot` on every machine: the server from its resolution (which accepts the client's claimed snap, see the echo fix), the shooter from its own, a spectator from the relayed claim (which always exists since 0.8.0's `Claims.PutResolved`) with `ClaimRules.SeenUltimate`, because a Line is relayed as `Snap`. The boosted speed is capped at `PhysicsSerializationSettings.VelocityMax - 1` (`SteadyRule.Boost`), so the network clamp never cuts it differently on two machines. Snap wobble depends on both: the game reads its deviance curve at `flightTime / flightDuration × (speed / the settings' speed)` (`_decomp/ArrowFlightSettings.cs:65-70`), so a 1.3x snap also runs through the curve 1.3x faster, partly cancelling the wobble multiplier on snaps (charged shots are unaffected). **Departure:** no cache-and-re-apply of flight messages on spectators (Part 12 item 8's third bullet): a spectator boosts its own launch from the same claim, so a flight message arriving first and the later local launch agree.

**The Fill.** Drawn on every client for every drawing archer whose Slowdraw `ChargingStarted` status was seen (other archers at `[Volley] OtherArcherBrightness`), on the front `[Fill] Fraction` 40% of the shaft, from the back of that stretch toward the tip, **offset off the shaft toward the viewer's camera** by the shaft's radius plus 2 mm (the flat material keeps the default depth test, `Fx.cs:20-37`, so a line on the axis would sit inside the opaque shaft and only the halo would show; drawing with depth test off would show it through hands and walls instead). The shooter times it on its own draw clock, spectators from the status. It reaches the tip at Slowdraw's own charge time and **the thump plays on Slowdraw's `Charged` status**, not on the local clock, because the server's charge is the game's own input, which no claim can move (a shooter who thumps first and releases would otherwise get no charge). Core: the flat line material with an orange-to-gold gradient (vertex colours work on it, `Fx.cs:20-37`). Halo: a copy of 'Orb Connection Line' (its shader is `Custom/TeleporterPointer`, `loaded_assets.txt` line 2073) with every colour property reset to white, or the flat material if it is not found. **Departures:** the arrowhead flare at the thump is the game's own `DissipateToolBasedEffect` unrecoloured (`_decomp/ProfessionSkill.cs:181`, `ProgressionSettings.cs:184`); the orange-only recolour of the head (flare and Charge 3's head shell) waits for the ramp-swap result, and Charge 3's shell is built only on the front 40%.

**The game's glow.** `[Archery] HideGameGlow` (still `true`) now hides Slowdraw's glow only while the Fill is drawn on that arrow, and Volley's (ghosts replace it, shipped in 0.8.0). A new `[Archery] HideHolyGlow` (`false`) leaves Lightbeam's glow on: with Slowdraw's hidden, Lightbeam's request at 6.5 s starts the game's whole-arrow glow (`ProfessionSkill.cs:58-100`, `:170-183`), which is the "second charge" the user missed, until Step 8's armed core replaces it. **Departure** from spec Part 12 item 13 (glow off for Lightbeam with the Fill), required by Part 9 item 15.

**Test build `Without`** (new, **Claude's addition**): `[TestBuild] Without` names orbs the mod's build treats as not owned (never the game's skill levels, never the shrine's one-ultimate rule). Needed because the user's character owns Quickdraw 4, so every snap is a Line: without it Steady's faster snaps, echoes on snaps and the snap interrupt cannot be seen.

**Task 12 pulls the Fill's half of Holy Arrow's pour forward from Step 8** (squeeze forward and brighten from 5.25 to 6.5 s, then a small pulsing core on the head). It is part of this plan: the user's binding feedback already asks for "more feedback in between the charge of Holy Arrow".

**Gold ghosts' stagger** (deferred from Steps 2 and 3) is built here, since it is part of "stagger batched per creature per release".

**Practice** (spec Part 12 item 11): Brace is practised by bracing once (the server's tracker entering Braced) while it is the practising orb; Overcharge by one overcharged release while it is the practising orb.

**Shrine positions:** `[TreeLayout] Brace` and `Overcharge` default `auto` = `PickPosition(tree, parent.Position, new Vector3(0f, -0.16f, 0.06f))` (toward the gap between the Slowdraw column at sideways 0.29 and the Quickdraw column at -0.30, a little up), parent Charge 1 and Charge 2. Costs 1 each.

## Settings

New or changed in this plan (`S` = shared). No shipped default changes, so no per-key migration entry is needed; `HideGameGlow` keeps its default and gets a new description.

| Key | Default | S | Note |
|---|---|---|---|
| `[Claim] TrustMs` | 1000 | | Server. "Server: a shot's time since the 68% crossing is taken from the shooter's claim when the claim is ahead of the server's own time by at most this many milliseconds, or when the server saw no crossing and the claim is at most this old (the server sees the pull late; play-test 2 measured 58 to 378 ms). It decides echoes, the ghost ratchet, Burst's rack, Overcharge and whether a claimed snap counts. 0 = the server's own time only." |
| `[Weight] Charge1Stagger`, `Charge2Stagger`, `Charge3Stagger` | 0.2, 0.4, 0.6 | S | "Added to a creature's cripple pool by a charged hit at Charge 1/2/3. 1.0 = the centre of a Holy Arrow blast ([HolyArrow] BlastCripple)." |
| `[Weight] OverchargeStagger` | 1.0 | S | |
| `[Weight] GhostStaggerFactor` | 0.5 | S | "Ghosts of a charged shot stagger by the charge's stagger x their share x this." |
| `[Weight] PierceRange` | 8 | S | metres behind the first creature |
| `[Weight] BatchSeconds` | 0.2 | | Server. "A release with no spectral arrows out deals its one cripple hit per creature this long after its first stagger lands." |
| `[Weight] MaxWaitSeconds` | 2 | | Server. "A release with spectral arrows out deals its stagger when its last spectral arrow has landed, or this long after its first stagger, whichever comes first." |
| `[Weight] Interrupt` | true | | Server. "Charged hits (and snaps with Charge 2 or higher) cancel the creature's current action, only when the game marks it cancellable. Off = Weight is stagger and pierce only." |
| `[Weight] InterruptWyrms` | false | | Server. "Wyrms are left out until tested: cancelling a dig or an emerge can leave one visible while flagged as burrowed." |
| `[Weight] InterruptCooldown`, `KnockDownImmunity` | 3, 4 | | Server, seconds |
| `[Brace] Floor`, `GraceSeconds`, `LooseLine`, `SnapDrawLooseLine` | 0.40, 0.6, 0.68, 0.55 | S | |
| `[Brace] CollarColour`, `CollarWidth`, `CollarClosedRadius`, `CollarOpenRadius` | `#FFC733`, 0.006, 0.008, 0.016 | | |
| `[Brace] ClampSound`, `OpenSound`, `LostSound` | `KeyEnterLock`, `Click`, `Wood Thump` | | |
| `[Brace] BracedDim`, `BreatheAmount`, `LostDimSeconds` | 0.4, 0.1, 0.4 | | |
| `[Overcharge] Seconds` | 3.75 | S | after the 68% crossing |
| `[Overcharge] HotColour` | `#FFF4E0` | | |
| `[Overcharge] HotEffect`, `SecondThumpEffect`, `SecondThumpSound`, `ReleaseSound`, `HitEffect` | `Hot Effect`, `Forge Hot`, `Power Strike Start Sounds`, `Power Strike Use Sounds`, `Turabada Ground Slam Effect` | | |
| `[Overcharge] SecondThumpHaptic` | 1.0 | | |
| `[Steady] Wobble1`, `Wobble2`, `Wobble3` | 0.75, 0.5, 0.35 | S | |
| `[Steady] SnapSpeed1`, `SnapSpeed2`, `SnapSpeed3` | 0.10, 0.20, 0.30 | S | added to 1 on snaps only |
| `[Fill] Enabled` | true | | "Off = no Fill, and the game's glow comes back." |
| `[Fill] Fraction`, `HeadGap` | 0.4, 0.03 | | share of the shaft from the tip; metres left clear behind the head |
| `[Fill] BackColour`, `FrontColour` | `#FF6A00`, `#FFC733` | | |
| `[Fill] CoreWidth`, `HaloWidth`, `HaloAlpha`, `HaloMaterial` | 0.006, 0.02, 0.35, `orb-line` | | `orb-line` or `flat` |
| `[Fill] EmberSize`, `Charge2EmberSize` | 0.012, 0.02 | | |
| `[Fill] FlareBoost`, `FlareSeconds`, `HeadFlare` | 2.5, 0.15, true | | |
| `[Fill] Charge2ThumpSound` | `Power Strike Start Sounds` | | "Charge 2 and 3: this replaces Slowdraw's own charge sound. Empty = keep the game's." |
| `[Fill] ThumpHaptic`, `Charge2ThumpHaptic` | 0.7, 0.9 | | |
| `[Fill] ShellWidth`, `ShellAlpha` | 0.035, 0.2 | | Charge 3 |
| `[Fill] TrailSeconds`, `TrailWidth` | 0.25, 0.01 | | ember trail in flight |
| `[Fill] HitEffect`, `Charge2HitEffect`, `GroundEffect`, `PierceExitEffect` | `Charge Hit Use`, `Power Strike Use`, `GroundHitPuff`, `Furor Heartcaller Hit Effect` | | |
| `[Fill] DustRingFrom`, `DustRingTo`, `DustRingSeconds` | 0.3, 1.2, 0.3 | | Charge 2 |
| `[Fill] GradientTest` | true | | "The red-to-green test line at the first draw. It draws once, then writes UserData\QuickshotRing\gradient_test_done and stays off while that file exists; delete the file to see it again." (spec Step 4 "Must also log") |
| `[Fill] HolySqueeze`, `HolyCoreFraction`, `HolySqueezeBoost`, `HolyPulseHz` | true, 0.08, 1.8, 2 | | Task 12 |
| `[Balance] Enabled`, `Colour`, `Width`, `HoldSeconds`, `FadeSeconds`, `EaseRate`, `RadiusScale`, `ShatterSparks` | true, `#FFC733`, 0.02, 2, 0.5, 8, 1, 12 | | |
| `[Archery] HideGameGlow` | true (unchanged) | | new description: "Hide the game's whole-arrow glow on Slowdraw while the Fill is drawn on that arrow, and on Volley (the ghosts show it). Holy Arrow's glow: see HideHolyGlow." |
| `[Archery] HideHolyGlow` | false | | "Hide the game's glow when Holy Arrow is ready. Off until the armed core replaces it (Step 8)." |
| `[Tree] BraceCost`, `OverchargeCost` | 1, 1 | S | "Skill points for the ... orb. Changing it after someone bought the orb refunds the wrong amount when they sell it." |
| `[TreeLayout] Brace`, `Overcharge` | `auto`, `auto` | S | |
| `[TestBuild] Without` | `""` | | Server only. "Orb names separated by ';' that the archery build treats as NOT owned (the game's own skill levels are untouched). For trying snaps while Quickdraw 4 is owned. Empty = off." |

Defaults named in brackets are constants in the Unity-free files (Task 2: `WeightValues.Default`, `BraceValues.Default`, `SteadyValues.Default`; Task 1: `ClaimTrust.DefaultTrustMs`) and are used as the cfg defaults, so the tests pin the shipped values. Which task creates each entry: Task 3 `[Claim]`, `[TestBuild] Without`; Task 4 `[Weight]` (all); Task 6 `[Tree]`, `[TreeLayout]`; Task 7 `[Brace] Floor`, `GraceSeconds`, `LooseLine`, `SnapDrawLooseLine`; Task 8 `[Steady]`; Task 9 `[Fill]` (except the Holy ones), `HideGameGlow`'s description, `HideHolyGlow`; Task 10 the look-only `[Overcharge]` entries (owned by `ChargeFx.HotEmbers`; `Seconds` is created in Task 4 by Weight, which needs it) and the rest of `[Brace]` (owned by `ChargeFx.Collar`); Task 11 `[Balance]`; Task 12 the `[Fill] Holy...` entries.

---

## File Structure

| File | Responsibility |
|---|---|
| `QuickshotRing/Rules.cs` (modify) | `ClaimTrust` (effective time, claimed snap); `ClaimRules.SeenUltimate`; `DrawClock.Update(..., bool hold)`; header lists the six Unity-free files. |
| `QuickshotRing/Release.cs` (modify) | `ReleaseFacts.Overcharged`, `Resolution.Overcharged`. |
| `QuickshotRing/Build.cs` (modify) | `TestBuildRule.Combine`; `SlotIds.BraceSkill`, `OverchargeSkill`. |
| `QuickshotRing/Charge.cs` (new, Unity-free) | `WeightValues`, `WeightCarry`, `WeightRules`, `WeightDue`, `WeightLedger`, `PierceCandidate`, `PierceRule`, `BraceState`, `BraceValues`, `BraceTracker`, `BraceRule`, `SteadyValues`, `SteadyRule`, `FillTimeline`, `BalanceRule`. |
| `QuickshotRing/Weight.cs` (new) | Server: knock-down probes, the real arrow's hit, stagger ledger and cripple hits, immunity, interrupt, pierce; clients: the carry per arrow for the looks. |
| `QuickshotRing/Brace.cs` (new) | Per-bow tracker on the server and the shooter; `DrawGrace`'s brace branch; full strength on release; Brace practice. |
| `QuickshotRing/Steady.cs` (new) | Deviance modifiers, faster snaps, cleanup on dock. |
| `QuickshotRing/ChargeFx.cs` (new) | Client looks: `Fill`, `Thump`, `Collar` (owns Brace's look settings), `HotEmbers` (owns Overcharge's look settings), `EmberTrail`, `PierceStreak`, `DustRing`; the thump-sound swap; `FillShownFor`. |
| `QuickshotRing/Balance.cs` (new) | Client: `BalanceRing` on creatures the local player hits. |
| `QuickshotRing/Draws.cs`, `Claims.cs` (modify) | Effective time and its log; the claimed snap; overcharge in the facts; the relayed time is the server's; carry on release; clock hold. |
| `QuickshotRing/Hits.cs` (modify) | `Dealing`; `Copy`; `IsOpen`; spectral stagger hand-off; release close hand-off. |
| `QuickshotRing/Volley.cs`, `VolleySkill.cs`, `HolyArrow.cs`, `Quickdraw4.cs`, `Main.cs`, `Tree.cs`, `Shrine.cs`, `Builds.cs`, `TestBuild.cs`, `Fx.cs` (modify) | Hooks named in each task. |
| `QuickshotRing/Art/make_top_icons.py`, `QuickshotRing/Resources/Brace.png`, `Overcharge.png` | Icons and the comparison sheet. |
| `QuickshotRing.Tests/*` | `ChargeTests.cs` (new); `ReleaseTests.cs`, `BuildTests.cs`, `Program.cs`, `.csproj` (modify). |
| `CLAUDE.md` | The QuickshotRing section brought up to 0.9.0. |

---

### Task 1: Claim trust, the clock's hold, overcharge in the resolver, the test build's `Without` (Unity-free)

**Files:**
- Modify: `QuickshotRing/Rules.cs:6-7` (header: six files), `:88-126` (`DrawClock`), add `SeenUltimate` to `ClaimRules` and append `ClaimTrust` after it (`:144-163`); `QuickshotRing/Release.cs:72-76` (`ReleaseFacts`), `:79-91` (`Resolution`), `:93-109` (`Resolver`); `QuickshotRing/Build.cs:126-144` (`SlotIds`), `:147-171` (`TestBuildRule`)
- Test: `QuickshotRing.Tests/ReleaseTests.cs` (append), `QuickshotRing.Tests/BuildTests.cs` (append)

**Interfaces:**
- Consumes: `DrawClock` (`Rules.cs:88-126`), `ClaimRules` (`Rules.cs:144-163`), `ShotClaimWire.TimeMax` (8190, `Wire.cs:34`), `ShotKind` (`Wire.cs`), `Resolver`, `ReleaseFacts`, `Resolution`, `ReleasePlan`, `UltimateUse` (`Release.cs`), `Build.From`, `Build.Ultimates`, `Ultimates.Quickdraw4`, `Orb`, `TestBuildRule` (`Build.cs`).
- Produces (namespace `QuickshotRing`, `internal`):
  - `static class ClaimTrust { const int DefaultTrustMs = 1000; static int? EffectiveMs(int? serverMs, int? claimedMs, int trustMs); static string Why(int? serverMs, int? claimedMs, int trustMs); static bool SnapReady(bool serverSnap, ShotKind? claimedKind, int? claimedMs, int? effectiveMs, int windowMs); }`
  - `static UltimateUse ClaimRules.SeenUltimate(ShotKind kind, Build build)` (a spectator's reading of a relayed kind: `Snap` with Quickdraw 4 in the shooter's build is a Line, the same rule as `Resolver`'s snap branch).
  - `DrawClock.Update(float pull, uint arrowId, float now, float dipGrace, bool hold)`; the 4-argument `Update` calls it with `hold: false`.
  - `ReleaseFacts.Overcharged` (bool field); `Resolution.Overcharged` (readonly bool); constructor `Resolution(ShotKind kind, UltimateUse ultimate, ReleasePlan plan, int chargedTier, bool overcharged = false)`; `ToString()` appends `" overcharged"` only when set.
  - `static List<Orb> TestBuildRule.Combine(IEnumerable<Orb> owned, Orb practising, IEnumerable<Orb> extra, IEnumerable<Orb> without)`
  - `SlotIds.BraceSkill = 1189207115`, `SlotIds.OverchargeSkill = 1316074013` (`const int`).

- [ ] **Step 1: Write the failing tests**

Append to `ReleaseTests.Run()`:

```csharp
// Step 4: the server decides with the client's claimed time when the client is ahead by at most TrustMs (the echo fix)
Check("trust: no claim keeps the server's time", ClaimTrust.EffectiveMs(500, null, 600) == 500);
Check("trust: the play-test 2 echo case", ClaimTrust.EffectiveMs(null, 83, 600) == 83);
Check("trust: no crossing and an old claim", ClaimTrust.EffectiveMs(null, 700, 600) == null);
Check("trust: client ahead within the limit", ClaimTrust.EffectiveMs(1000, 1400, 600) == 1400);
Check("trust: client ahead at the limit", ClaimTrust.EffectiveMs(100, 700, 600) == 700);
Check("trust: client far ahead (the 10961 ms clock bug)", ClaimTrust.EffectiveMs(682, 10961, 600) == 682);
Check("trust: client behind keeps the server's time", ClaimTrust.EffectiveMs(900, 800, 600) == 900);
Check("trust: off", ClaimTrust.EffectiveMs(null, 83, 0) == null && ClaimTrust.EffectiveMs(500, 600, 0) == 500);
Check("trust why: the echo case", ClaimTrust.Why(null, 83, 600) == "the server saw no crossing; the client's claim is trusted");
Check("trust why: ahead", ClaimTrust.Why(1000, 1400, 600) == "the client's time (ahead 400 ms)");
Check("trust: a 568 ms lead is trusted at the shipped 1000 ms", ClaimTrust.DefaultTrustMs == 1000 && ClaimTrust.EffectiveMs(1000, 1568, ClaimTrust.DefaultTrustMs) == 1568);
Check("trust: no server crossing, slow pull", ClaimTrust.EffectiveMs(null, 950, ClaimTrust.DefaultTrustMs) == 950);
Check("trust: a saturated claim is never an exact time", ClaimTrust.EffectiveMs(7622, ShotClaimWire.TimeMax, 1000) == 7622
      && ClaimTrust.EffectiveMs(null, ShotClaimWire.TimeMax, 1000) == null);
Build volley2 = Build.From(new[] { Orb.Volley, Orb.FullVolley });
Check("echo fix: a crossing only the client saw still fires the echoes",
      Resolver.Resolve(new ReleaseFacts { Build = volley2, Crossed = ClaimTrust.EffectiveMs(null, 83, 600) != null }).Plan.Echoes == 2);

// Snap versus Early: the server accepts the shooter's claimed snap when the claim's time is trusted and inside Quickdraw's window
Check("snap: a claimed snap the server's lagging input missed is accepted", ClaimTrust.SnapReady(false, ShotKind.Snap, 83, 83, 250)
      && ClaimTrust.SnapReady(true, null, null, null, 250));
Check("snap: refused when untrusted, outside the window, or not claimed", !ClaimTrust.SnapReady(false, ShotKind.Snap, 10961, 682, 250)
      && !ClaimTrust.SnapReady(false, ShotKind.Snap, 400, 400, 250) && !ClaimTrust.SnapReady(false, ShotKind.Early, 83, 83, 250)
      && !ClaimTrust.SnapReady(false, ShotKind.Snap, null, null, 250));

// spectators: a Line is relayed as Snap
Build q4 = Build.From(new[] { Orb.Quick1, Orb.Quickdraw4 });
Check("seen ultimate: a relayed snap with Quickdraw 4 is a Line", ClaimRules.SeenUltimate(ShotKind.Snap, q4) == UltimateUse.Line);
Check("seen ultimate: a relayed snap without Quickdraw 4 is none", ClaimRules.SeenUltimate(ShotKind.Snap, Build.From(new[] { Orb.Quick1 })) == UltimateUse.None);
Check("seen ultimate: a relayed charged shot is never a Line", ClaimRules.SeenUltimate(ShotKind.Charged, q4) == UltimateUse.None);

// Brace: the clock holds its span while braced, and a hold never starts one
var k = new DrawClock();
k.Update(0.9f, 7u, 1f, 0.35f);
k.Update(0.5f, 7u, 1.1f, 0.35f, true);
Check("clock hold keeps the span through a long brace", k.Active(5f, 0.35f) && k.TryElapsed(5f, DrawClock.Grace, out float kb) && Near(kb, 4f));
var k2 = new DrawClock(); k2.Update(0.5f, 7u, 1f, 0.35f, true);
Check("clock hold never starts a span", !k2.Active(1f, 0.35f) && k2.ArrowAtCrossing == 0u);
var k3 = new DrawClock(); k3.Update(0.9f, 7u, 1f, 0.35f); k3.Update(0.5f, 7u, 2f, 0.35f); k3.Update(0.5f, 7u, 2.2f, 0.35f, true);
Check("clock hold resumes a span inside the dip grace", k3.Active(4f, 0.35f) && k3.TryElapsed(4f, DrawClock.Grace, out float kc) && Near(kc, 3f));
var k4 = new DrawClock(); k4.Update(0.9f, 7u, 1f, 0.35f); k4.Update(0.5f, 7u, 2f, 0.35f); k4.Update(0.5f, 7u, 3f, 0.35f, true);
Check("clock hold after a long dip does not resume", !k4.Active(3f, 0.35f));
var k5 = new DrawClock(); k5.Update(.9f, 7u, 1f, .35f); k5.Update(.5f, 7u, 1.1f, .35f, true); k5.Update(.9f, 7u, 4f, .35f);
Check("a re-pull after a brace keeps the crossing (never a snap)", k5.TryElapsed(4f, DrawClock.Grace, out float k5s) && Near(k5s, 3f) && k5.ArrowAtCrossing == 7u);
var k6 = new DrawClock(); k6.Update(.9f, 7u, 1f, .35f); k6.Update(.5f, 0u, 1.1f, .35f, true);
Check("clock hold needs the crossing's arrow", !k6.Active(3f, .35f));

// Overcharge rides on a charged release only
Build oc2 = Build.From(new[] { Orb.Charge1, Orb.Charge2, Orb.Overcharge });
Check("resolve overcharged charged shot", Resolver.Resolve(new ReleaseFacts { Build = oc2, ChargeActive = true, Overcharged = true, Crossed = true }) is var o1
      && o1.Kind == ShotKind.Charged && o1.Overcharged && o1.ChargedTier == 2);
Check("resolve overcharge needs the charge", Resolver.Resolve(new ReleaseFacts { Build = oc2, Overcharged = true, Crossed = true }) is var o2
      && o2.Kind == ShotKind.Early && !o2.Overcharged);
Check("resolve overcharged volley", Resolver.Resolve(new ReleaseFacts { Build = oc2, ChargeActive = true, Overcharged = true, Ghosts = 15 }).Overcharged);
Check("resolve a beam is never overcharged", !Resolver.Resolve(new ReleaseFacts { Build = oc2, HolyArmed = true, ChargeActive = true, Overcharged = true }).Overcharged);
Check("resolution text names overcharge",
      Resolver.Resolve(new ReleaseFacts { Build = oc2, ChargeActive = true, Overcharged = true }).ToString().EndsWith(" overcharged")
      && !Resolver.Resolve(new ReleaseFacts { Build = oc2, ChargeActive = true }).ToString().Contains("overcharged"));
```

Append to `BuildTests.Run()`:

```csharp
// the test build's Without: orbs the archery build treats as not owned
var mine = new[] { Orb.Quick1, Orb.Quick2, Orb.Quick3, Orb.Quickdraw4, Orb.Charge1 };
Check("test build combine adds practising and extra", TestBuildRule.Combine(mine, Orb.Brace, new[] { Orb.Overcharge }, new Orb[0]) is var tb1
      && tb1.Count == 7 && tb1.Contains(Orb.Brace) && tb1.Contains(Orb.Overcharge));
Check("test build without removes an owned orb", TestBuildRule.Combine(mine, Orb.None, new Orb[0], new[] { Orb.Quickdraw4 }) is var tb2
      && tb2.Count == 4 && !tb2.Contains(Orb.Quickdraw4));
Check("test build without beats extra and practising", TestBuildRule.Combine(mine, Orb.Brace, new[] { Orb.Brace, Orb.Volley }, new[] { Orb.Brace }) is var tb3
      && !tb3.Contains(Orb.Brace) && tb3.Contains(Orb.Volley));
Check("test build combine counts each orb once", TestBuildRule.Combine(mine, Orb.Quick1, new[] { Orb.Quick1 }, new Orb[0]).Count == 5);
Check("passive skill ids are distinct", new HashSet<int> { SlotIds.QuiverSkill, SlotIds.BraceSkill, SlotIds.OverchargeSkill }.Count == 3
      && SlotIds.BraceSkill > 65535 && SlotIds.OverchargeSkill > 65535);
```

That is 30 checks in `ReleaseTests` and 5 in `BuildTests`, 35 in all.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `ClaimTrust` not defined.

- [ ] **Step 3: Implement**

- `ClaimTrust.EffectiveMs`, first match wins: `trustMs <= 0` or `claimedMs` null or negative → `serverMs`; `claimedMs >= ShotClaimWire.TimeMax` (saturated: "at least 8190", never an exact time) → `serverMs`; `serverMs` null → `claimedMs <= trustMs ? claimedMs : null`; `claimedMs < serverMs` → `serverMs`; `claimedMs - serverMs <= trustMs` → `claimedMs`; else `serverMs`.
- `ClaimTrust.Why`, the same order, exactly: `"trust is off"`, `"no claimed time"` (also for a negative claim), `$"the client's time is saturated (at least {ShotClaimWire.TimeMax} ms); the server's own"`, `"the server saw no crossing; the client's claim is trusted"`, `$"the server saw no crossing; the client's {c} ms is over {trust} ms"`, `$"the server's own (the client is {s - c} ms behind)"`, `$"the client's time (ahead {c - s} ms)"`, `$"the server's own (the client is {c - s} ms ahead, over {trust} ms)"`.
- `ClaimTrust.SnapReady` = `serverSnap || (claimedKind == ShotKind.Snap && claimedMs.HasValue && effectiveMs == claimedMs && claimedMs.Value <= windowMs + ClaimRules.ToleranceMs)` (the claim's time was the one trusted, and it lies inside Quickdraw's window give or take the existing 60 ms tolerance).
- `ClaimRules.SeenUltimate` = `kind == ShotKind.Snap && (build.Ultimates & Ultimates.Quickdraw4) != 0 ? UltimateUse.Line : UltimateUse.None`.
- `DrawClock.Update(pull, arrowId, now, dipGrace, hold)`: when `hold`, `arrowId != 0 && arrowId == ArrowAtCrossing`, and a span exists that is running, or ended no more than `dipGrace` ago (`dipGrace > 0`), set `endedAt = NaN` and return; otherwise do exactly what the 4-argument form does today (so a hold for an arrow that was shot or removed ends the span like any low update).
- `Resolver`: `overcharged = f.ChargeActive && f.Overcharged` in the ghosts branch and the charged branch; every other branch passes `false`.
- `TestBuildRule.Combine`: owned in order, then `practising` (unless `None`), then `extra`, each orb once, then remove every orb in `without`.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (250)`.

- [ ] **Step 5: Checkpoint**

Tests `ALL PASS (250)`; build `0 Error(s)`, 0 warnings. Files changed: `Rules.cs`, `Release.cs`, `Build.cs`, `QuickshotRing.Tests/ReleaseTests.cs`, `QuickshotRing.Tests/BuildTests.cs`.

---

### Task 2: Weight, Brace, Steady, Fill and Balance rules (Unity-free)

**Files:**
- Create: `QuickshotRing/Charge.cs`, `QuickshotRing.Tests/ChargeTests.cs`
- Modify: `QuickshotRing.Tests/QuickshotRing.Tests.csproj` (add `<Compile Include="..\QuickshotRing\Charge.cs" />`), `QuickshotRing.Tests/Program.cs:65` (call `ChargeTests.Run()` after `BuildTests.Run()`)

**Interfaces:**
- Consumes: `Build`, `SideOrbs`, `Orb` (`Build.cs`), `Resolution`, `ReleasePlan`, `UltimateUse` (`Release.cs`), `ShotKind` (`Wire.cs`).
- Produces (namespace `QuickshotRing`, `internal`):
  - `readonly struct WeightValues { readonly float Charge1, Charge2, Charge3, Overcharge, GhostFactor; WeightValues(float charge1, float charge2, float charge3, float overcharge, float ghostFactor); static WeightValues Default /* 0.2, 0.4, 0.6, 1.0, 0.5 */; }`
  - `readonly struct WeightCarry { readonly float Stagger; readonly bool Interrupt; readonly int Pierce; readonly float GhostStaggerPerShare; WeightCarry(float stagger, bool interrupt, int pierce, float ghostStaggerPerShare); static WeightCarry None; bool Any; ToString() => $"stagger {Stagger:0.##} interrupt {(Interrupt ? "yes" : "no")} pierce {Pierce} ghost stagger {GhostStaggerPerShare:0.###}/share"; }` (invariant culture)
  - `static class WeightRules { static float TierStagger(int tier, WeightValues v); static bool Overcharged(Build build, bool charged, float? seconds, float overchargeAt); static WeightCarry ForRelease(Resolution r, Build build, WeightValues v); static float Cripple(float stagger, float unit); static bool Immune(float now, float lastKnockDown, float seconds); static bool MayInterrupt(float now, float lastInterrupt, float cooldown, float lastKnockDown, float immunity, bool cancellable, bool isWyrm, bool wyrmsAllowed, bool enabled); }` (`float.NaN` = "never")
  - `readonly struct WeightDue { readonly uint Release; readonly int Creature; readonly float Stagger; readonly int Parts; }`
  - `sealed class WeightLedger { const int Cap = 256; int Pending { get; } void Add(uint release, int creature, float stagger, float now, bool releaseOpen); List<WeightDue> TakeDue(float now, float batchSeconds, float maxWaitSeconds); List<WeightDue> TakeRelease(uint release); }` (`releaseOpen`: the release still has spectral arrows out, `Hits.IsOpen`)
  - `readonly struct PierceCandidate { readonly int Id; readonly float Distance; readonly bool HasHealth; readonly bool IsPlayer; PierceCandidate(int id, float distance, bool hasHealth, bool isPlayer); }` (`Id` = the creature's key, the same for all its colliders; `Distance` from the last hit point)
  - `static class PierceRule { static int Next(IReadOnlyList<PierceCandidate> byDistance, ICollection<int> struck, float range); }` (the index of the next creature to pierce, or -1)
  - `enum BraceState { Off, Drawn, Braced, Grace, Lost }`
  - `readonly struct BraceValues { readonly float Floor, GraceSeconds, Crossing, LooseLine; BraceValues(float floor, float graceSeconds, float crossing, float looseLine); static BraceValues Default /* 0.40, 0.6, 0.68, 0.68 */; static float LooseLineFor(bool snapDraw, float normal, float snapDrawLine); }`
  - `sealed class BraceTracker { BraceState State { get; } bool WasBraced { get; } bool Holds /* Braced or Grace */; bool Update(bool owned, bool charged, float pull, float now, BraceValues v) /* true when State changed */; bool Open(float pull, BraceValues v); void Reset() /* the only way out of Lost */; }`
  - `static class BraceRule { static float ReleaseStrength(float strength, bool open, bool holyArmed); }`
  - `readonly struct SteadyValues { readonly float Wobble1, Wobble2, Wobble3, Speed1, Speed2, Speed3; SteadyValues(...six floats in that order...); static SteadyValues Default /* 0.75, 0.5, 0.35, 0.10, 0.20, 0.30 */; }`
  - `static class SteadyRule { static float Wobble(int quickTier, SteadyValues s); static float SnapSpeed(int quickTier, ShotKind kind, UltimateUse ultimate, SteadyValues s); static float Boost(float speed, float factor, float max); }`
  - `static class FillTimeline { static float Grown(float t, float thumpAt); static float WhiteHot(float t, float thumpAt, float overchargeAt); static float Braced(float since, float dim, float amplitude, float period); static float LostRemaining(float sinceLost, float seconds); static bool Between(float from, float to, float at); static float Squeeze(float gather, float fraction, float coreFraction); }`
  - `static class BalanceRule { static float Drained(float value, float min, float max); static float Ease(float shown, float target, float dt, float rate); static float Alpha(float now, float lastHit, float holdSeconds, float fadeSeconds); }`

- [ ] **Step 1: Write the failing tests in `ChargeTests.cs`**

`internal static class ChargeTests { internal static void Run() { ... } }` with `using System.Collections.Generic; using QuickshotRing; using static Program;`.

```csharp
// Weight (spec Part 10): stagger by tier, overcharge, pierce, gold ghosts, the snap interrupt, the guards
var v = WeightValues.Default;
Check("weight values are the spec's", Near(v.Charge1, .2f) && Near(v.Charge2, .4f) && Near(v.Charge3, .6f) && Near(v.Overcharge, 1f)
      && Near(v.GhostFactor, .5f));
Check("weight tier stagger", WeightRules.TierStagger(0, v) == 0f && Near(WeightRules.TierStagger(1, v), .2f) && Near(WeightRules.TierStagger(2, v), .4f)
      && Near(WeightRules.TierStagger(3, v), .6f) && Near(WeightRules.TierStagger(4, v), .6f));
Build c2 = Build.From(new[] { Orb.Charge1, Orb.Charge2 });
Build c3 = Build.From(new[] { Orb.Charge1, Orb.Charge2, Orb.Charge3 });
Build c2o = Build.From(new[] { Orb.Charge1, Orb.Charge2, Orb.Overcharge });
Build c3o = Build.From(new[] { Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Overcharge });
var plan0 = new ReleasePlan(0, 0, 0);
WeightCarry w1 = WeightRules.ForRelease(new Resolution(ShotKind.Charged, UltimateUse.None, plan0, 2), c2, v);
Check("weight charged shot carries its tier", Near(w1.Stagger, .4f) && w1.Interrupt && w1.Pierce == 0 && Near(w1.GhostStaggerPerShare, .2f));
WeightCarry w2 = WeightRules.ForRelease(new Resolution(ShotKind.Charged, UltimateUse.None, plan0, 3), c3, v);
Check("weight charge 3 pierces one creature", Near(w2.Stagger, .6f) && w2.Pierce == 1 && Near(w2.GhostStaggerPerShare, .3f));
WeightCarry w3 = WeightRules.ForRelease(new Resolution(ShotKind.Charged, UltimateUse.None, plan0, 2, true), c2o, v);
Check("weight overcharge knocks down and pierces one more", Near(w3.Stagger, 1f) && w3.Interrupt && w3.Pierce == 1 && Near(w3.GhostStaggerPerShare, .5f));
Check("weight overcharge at charge 3 pierces two", WeightRules.ForRelease(new Resolution(ShotKind.Charged, UltimateUse.None, plan0, 3, true), c3o, v).Pierce == 2);
WeightCarry w5 = WeightRules.ForRelease(new Resolution(ShotKind.Snap, UltimateUse.None, plan0, 0), c2, v);
Check("weight snap interrupts with charge 2", w5.Interrupt && w5.Stagger == 0f && w5.Pierce == 0 && w5.GhostStaggerPerShare == 0f);
Check("weight snap with charge 1 does nothing", !WeightRules.ForRelease(new Resolution(ShotKind.Snap, UltimateUse.None, plan0, 0), Build.From(new[] { Orb.Charge1 }), v).Any);
Check("weight a Line interrupts with charge 2", WeightRules.ForRelease(new Resolution(ShotKind.Snap, UltimateUse.Line, plan0, 0), c2, v).Interrupt);
Check("weight held and early carry nothing", !WeightRules.ForRelease(new Resolution(ShotKind.Held, UltimateUse.None, new ReleasePlan(15, 0, 0), 0), c3, v).Any
      && !WeightRules.ForRelease(new Resolution(ShotKind.Early, UltimateUse.None, new ReleasePlan(0, 0, 2), 0), c3, v).Any);
Check("weight a beam carries nothing", !WeightRules.ForRelease(new Resolution(ShotKind.Charged, UltimateUse.Beam, plan0, 3), c3o, v).Any);
Check("weight cripple from stagger", Near(WeightRules.Cripple(1f, .5f), .5f) && Near(WeightRules.Cripple(.2f, .5f), .1f) && WeightRules.Cripple(-1f, .5f) == 0f);
Check("weight overcharge needs the orb, the charge and the time", WeightRules.Overcharged(c2o, true, 3.75f, 3.75f) && !WeightRules.Overcharged(c2, true, 9f, 3.75f)
      && !WeightRules.Overcharged(c2o, false, 9f, 3.75f) && !WeightRules.Overcharged(c2o, true, 3.7f, 3.75f) && !WeightRules.Overcharged(c2o, true, null, 3.75f));
Check("weight immunity lasts four seconds", WeightRules.Immune(5f, 2f, 4f) && !WeightRules.Immune(6.1f, 2f, 4f) && !WeightRules.Immune(5f, float.NaN, 4f));
// MayInterrupt(now, lastInterrupt, cooldown, lastKnockDown, immunity, cancellable, isWyrm, wyrmsAllowed, enabled)
Check("weight interrupt cooldown", !WeightRules.MayInterrupt(10f, 8f, 3f, float.NaN, 4f, true, false, false, true)
      && WeightRules.MayInterrupt(10f, 6.9f, 3f, float.NaN, 4f, true, false, false, true) && WeightRules.MayInterrupt(10f, float.NaN, 3f, float.NaN, 4f, true, false, false, true));
Check("weight interrupt only cancellable actions", !WeightRules.MayInterrupt(10f, float.NaN, 3f, float.NaN, 4f, false, false, false, true));
Check("weight interrupt leaves Wyrms and knocked-down creatures alone", !WeightRules.MayInterrupt(10f, float.NaN, 3f, float.NaN, 4f, true, true, false, true)
      && WeightRules.MayInterrupt(10f, float.NaN, 3f, float.NaN, 4f, true, true, true, true) && !WeightRules.MayInterrupt(10f, float.NaN, 3f, 8f, 4f, true, false, false, true));
Check("weight interrupt switch", !WeightRules.MayInterrupt(10f, float.NaN, 3f, float.NaN, 4f, true, false, false, false));

// one cripple hit per creature per release (spec Part 10 Guards, Part 11): a release with spectral arrows out waits for its
// last one (TakeRelease) or the max wait; a release with none out (no Volley orb) flushes after the batch time
var led = new WeightLedger();
led.Add(1u, 10, .2f, 0f, false); led.Add(1u, 10, .1f, .05f, false);
Check("ledger waits for the batch", led.TakeDue(.1f, .2f, 2f).Count == 0 && led.Pending == 1);
List<WeightDue> due = led.TakeDue(.2f, .2f, 2f);
Check("ledger batches one release on one creature", due.Count == 1 && due[0].Release == 1u && due[0].Creature == 10 && Near(due[0].Stagger, .3f)
      && due[0].Parts == 2 && led.Pending == 0);
led.Add(1u, 10, .2f, 0f, false); led.Add(2u, 10, .4f, 0f, false); led.Add(2u, 11, .4f, 0f, false);
Check("ledger keeps releases and creatures apart", led.TakeDue(1f, .2f, 2f).Count == 3);
led.Add(3u, 11, .5f, 0f, true);
List<WeightDue> closed = led.TakeRelease(3u);
Check("ledger takes a closed release at once", closed.Count == 1 && Near(closed[0].Stagger, .5f) && led.TakeDue(9f, .2f, 2f).Count == 0);
led.Add(4u, 12, 0f, 0f, false);
Check("ledger ignores no stagger", led.Pending == 0);
led.Add(5u, 13, .4f, 0f, true);
Check("ledger holds an open release past the batch", led.TakeDue(1f, .2f, 2f).Count == 0);
Check("ledger flushes an open release at max wait", led.TakeDue(2f, .2f, 2f).Count == 1 && led.Pending == 0);
led.Add(6u, 10, .4f, 0f, true); led.Add(6u, 10, .1f, .6f, true);
List<WeightDue> both = led.TakeRelease(6u);
Check("real hit and ghost 0.6 s apart are one hit", both.Count == 1 && Near(both[0].Stagger, .5f) && both[0].Parts == 2);
led.Add(7u, 10, .6f, 0f, false); led.Add(8u, 10, .6f, .05f, false);
Check("two archers: a second release on the same creature is its own hit", led.TakeDue(.3f, .2f, 2f).Count == 2);
for (int i = 0; i <= WeightLedger.Cap; i++) led.Add((uint)(100 + i), i, .1f, 0f, false);
Check("ledger is capped", led.Pending == WeightLedger.Cap);

// pierce (spec Charge 3, Overcharge): each hop measured from the last hit point, solids without health stop it, players are passed
var first = new List<int> { 10 }; // the first creature, already struck
Check("pierce range is per hop: 7.9 m is taken, 8.1 m is not",
      PierceRule.Next(new[] { new PierceCandidate(20, 7.9f, true, false) }, first, 8f) == 0
      && PierceRule.Next(new[] { new PierceCandidate(20, 8.1f, true, false) }, first, 8f) == -1);
Check("pierce stops at terrain before a creature",
      PierceRule.Next(new[] { new PierceCandidate(0, 3f, false, false), new PierceCandidate(20, 5f, true, false) }, first, 8f) == -1);
Check("pierce passes through a player",
      PierceRule.Next(new[] { new PierceCandidate(30, 2f, true, true), new PierceCandidate(20, 4f, true, false) }, first, 8f) == 1);
Check("pierce skips the first creature's second collider",
      PierceRule.Next(new[] { new PierceCandidate(10, .2f, true, false), new PierceCandidate(20, 6f, true, false) }, first, 8f) == 1);
Check("pierce: a second hop gets a fresh 8 m from the second creature",
      PierceRule.Next(new[] { new PierceCandidate(20, 7f, true, false), new PierceCandidate(30, 13f, true, false) }, first, 8f) == 0
      && PierceRule.Next(new[] { new PierceCandidate(20, .1f, true, false), new PierceCandidate(30, 6f, true, false) }, new List<int> { 10, 20 }, 8f) == 1);

// Brace (spec Part 4): rest at 40% after the thump, 0.6 s of grace below it, the loose line
var bv = BraceValues.Default;
Check("brace values are the spec's", Near(bv.Floor, .4f) && Near(bv.GraceSeconds, .6f) && Near(bv.Crossing, .68f) && Near(bv.LooseLine, .68f));
var b = new BraceTracker();
b.Update(false, true, .5f, 0f, bv);
Check("brace off without the orb", b.State == BraceState.Off && !b.Holds);
b.Update(true, false, .5f, 0f, bv);
Check("brace off before the thump", b.State == BraceState.Off && !b.Holds);
bool changed = b.Update(true, true, .9f, 1f, bv);
Check("brace drawn at full pull, change reported once", changed && b.State == BraceState.Drawn && !b.Holds && !b.Update(true, true, .95f, 1.1f, bv));
Check("brace not open without bracing first", !b.Open(.9f, bv));
b.Update(true, true, .5f, 2f, bv);
Check("brace holds in the band", b.State == BraceState.Braced && b.Holds && b.WasBraced);
b.Update(true, true, .4f, 2.1f, bv);
Check("brace holds at exactly the floor", b.State == BraceState.Braced);
Check("brace open needs the loose line", !b.Open(.6f, bv) && b.Open(.6f, new BraceValues(.4f, .6f, .68f, .55f)));
b.Update(true, true, .7f, 2.5f, bv);
Check("brace open past the loose line after bracing", b.State == BraceState.Drawn && b.Open(.7f, bv));
b.Update(true, true, .3f, 3f, bv);
Check("brace grace below the floor", b.State == BraceState.Grace && b.Holds);
b.Update(true, true, .5f, 3.4f, bv); b.Update(true, true, .3f, 3.5f, bv); b.Update(true, true, .3f, 4f, bv);
Check("brace back above the floor restarts the grace", b.State == BraceState.Grace);
b.Update(true, true, .3f, 4.2f, bv);
Check("brace lost after the grace", b.State == BraceState.Lost && !b.Holds && !b.Open(.9f, bv));
b.Update(true, true, .9f, 4.3f, bv);
Check("brace lost stays lost while charged", b.State == BraceState.Lost);
b.Update(true, false, .9f, 4.4f, bv); // the server's game cancels Slowdraw on the Lost update: charged reads false from here on
bool lostAfterCharge = b.State == BraceState.Lost && !b.Holds;
b.Reset();
Check("brace lost stays lost after the charge ends; Reset clears it", lostAfterCharge && b.State == BraceState.Off && !b.WasBraced);
Check("brace loose line with Snap Draw", Near(BraceValues.LooseLineFor(true, .68f, .55f), .55f) && Near(BraceValues.LooseLineFor(false, .68f, .55f), .68f));
Check("brace release full when open past the loose line", BraceRule.ReleaseStrength(.7f, true, false) == 1f);
Check("brace release keeps a light pull, Holy armed is full", Near(BraceRule.ReleaseStrength(.5f, false, false), .5f) && BraceRule.ReleaseStrength(.45f, false, true) == 1f);

// Steady (spec Quickdraw 1-3): wobble on every shot, speed on snaps only
var sv = SteadyValues.Default;
Check("steady wobble by tier", SteadyRule.Wobble(0, sv) == 1f && Near(SteadyRule.Wobble(1, sv), .75f) && Near(SteadyRule.Wobble(2, sv), .5f) && Near(SteadyRule.Wobble(3, sv), .35f));
Check("steady wobble capped at tier 3", Near(SteadyRule.Wobble(5, sv), .35f));
Check("steady snaps faster", Near(SteadyRule.SnapSpeed(1, ShotKind.Snap, UltimateUse.None, sv), 1.1f) && Near(SteadyRule.SnapSpeed(3, ShotKind.Snap, UltimateUse.None, sv), 1.3f));
Check("steady other shots keep their speed", SteadyRule.SnapSpeed(3, ShotKind.Charged, UltimateUse.None, sv) == 1f
      && SteadyRule.SnapSpeed(3, ShotKind.Held, UltimateUse.None, sv) == 1f && SteadyRule.SnapSpeed(3, ShotKind.Early, UltimateUse.None, sv) == 1f);
Check("steady a Line does not fly", SteadyRule.SnapSpeed(3, ShotKind.Snap, UltimateUse.Line, sv) == 1f);
Check("steady no Quick orb no bonus", SteadyRule.SnapSpeed(0, ShotKind.Snap, UltimateUse.None, sv) == 1f);
Check("steady boost keeps play-test 1's measured snap", Near(SteadyRule.Boost(54.56f, 1.3f, 99f), 70.928f));
Check("steady boost is capped below the network clamp", SteadyRule.Boost(80f, 1.3f, 99f) == 99f);

// the Fill (spec Charge 1, Overcharge, Brace)
Check("fill grows to full at the thump", FillTimeline.Grown(-1f, 1.75f) == 0f && Near(FillTimeline.Grown(.875f, 1.75f), .5f) && FillTimeline.Grown(3f, 1.75f) == 1f);
Check("fill with no thump time is full at once", FillTimeline.Grown(0f, 0f) == 1f);
Check("white-hot creeps over two seconds", FillTimeline.WhiteHot(1.75f, 1.75f, 3.75f) == 0f && Near(FillTimeline.WhiteHot(2.75f, 1.75f, 3.75f), .5f)
      && FillTimeline.WhiteHot(5f, 1.75f, 3.75f) == 1f && FillTimeline.WhiteHot(1f, 1.75f, 3.75f) == 0f);
Check("braced fill breathes once a second", Near(FillTimeline.Braced(0f, .4f, .1f, 1f), .4f) && Near(FillTimeline.Braced(.25f, .4f, .1f, 1f), .5f)
      && Near(FillTimeline.Braced(.75f, .4f, .1f, 1f), .3f));
Check("a lost charge dims backward", FillTimeline.LostRemaining(0f, .4f) == 1f && Near(FillTimeline.LostRemaining(.2f, .4f), .5f) && FillTimeline.LostRemaining(1f, .4f) == 0f);
Check("the overcharge beat fires once", FillTimeline.Between(3.7f, 3.76f, 3.75f) && !FillTimeline.Between(3.76f, 3.8f, 3.75f));
Check("holy gather squeezes the lit stretch to the core", Near(FillTimeline.Squeeze(0f, .4f, .08f), .4f) && Near(FillTimeline.Squeeze(1f, .4f, .08f), .08f)
      && Near(FillTimeline.Squeeze(.5f, .4f, .08f), .24f));

// the Balance Ring
Check("balance drained share", BalanceRule.Drained(1f, 0f, 1f) == 0f && BalanceRule.Drained(0f, 0f, 1f) == 1f && Near(BalanceRule.Drained(.25f, 0f, 1f), .75f)
      && BalanceRule.Drained(5f, 0f, 0f) == 0f);
Check("balance eases toward the target", BalanceRule.Ease(0f, 1f, .1f, 10f) is var be && be > .6f && be < .65f && BalanceRule.Ease(1f, 1f, .1f, 10f) == 1f);
Check("balance easing never overshoots", BalanceRule.Ease(0f, 1f, 10f, 10f) is var be2 && be2 <= 1f && be2 > .999f);
Check("balance shows 2 s after the last hit, then fades", BalanceRule.Alpha(1f, 0f, 2f, .5f) == 1f && Near(BalanceRule.Alpha(2.25f, 0f, 2f, .5f), .5f)
      && BalanceRule.Alpha(3f, 0f, 2f, .5f) == 0f);
```

That is 69 checks (Weight 18, ledger 10, pierce 5, Brace 17, Steady 8, Fill 7, Balance 4).

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `WeightValues` not defined.

- [ ] **Step 3: Implement `Charge.cs`**

The tests fix most bodies; only these choices are left open:
- `WeightRules.ForRelease`, first match wins: `Ultimate == Beam` → `None`; `Kind == Snap` → `new WeightCarry(0, build.Charge >= 2, 0, 0)`; `Kind == Charged && ChargedTier >= 1` → `s = r.Overcharged ? v.Overcharge : TierStagger(ChargedTier, v)`, `new WeightCarry(s, true, (ChargedTier >= 3 ? 1 : 0) + (r.Overcharged ? 1 : 0), s * v.GhostFactor)`; otherwise `None`. `Any` = any field non-zero or `Interrupt`.
- `Overcharged`: `(build.Sides & SideOrbs.Overcharge) != 0 && charged && seconds.HasValue && seconds.Value + 1e-4f >= overchargeAt`.
- `Immune`: `!float.IsNaN(last) && now - last < seconds`. `MayInterrupt`: `enabled && cancellable && (!isWyrm || wyrmsAllowed) && !Immune(now, lastKnockDown, immunity) && (float.IsNaN(lastInterrupt) || now - lastInterrupt >= cooldown)`.
- `WeightLedger`: one entry per (release, creature) holding the sum, the part count, the time of its first add and whether any add said `releaseOpen`; `Add` ignores `stagger <= 0`; at `Cap` entries a new key drops the oldest-added entry first. `TakeDue` returns and removes, oldest first, every entry that is not open with `now - firstAt >= batchSeconds - 1e-6f` and every open entry with `now - firstAt >= maxWaitSeconds - 1e-6f`. `TakeRelease` returns and removes all of a release's entries, open or not.
- `PierceRule.Next`: walk `byDistance` in order; skip a candidate whose `Id` is in `struck` and every `IsPlayer` one (passed through, never hit); stop with -1 at the first candidate with `Distance > range` or without `HasHealth` (a solid: terrain, a rock, a wall); otherwise return its index.
- `SteadyRule.Boost` = `Math.Min(speed * factor, max)`.
- `FillTimeline.Squeeze` = `fraction - (fraction - coreFraction) * clamp01(gather)`.
- `BraceTracker.Update`, first match wins: `State == Lost` → unchanged (only `Reset` leaves it; the server's game cancels Slowdraw on the Lost update, and a tracker that went `Off` there would hand Lightbeam and Volley a fresh dip grace); not `owned` or not `charged` → `Off`, `WasBraced = false`, below-time cleared; `pull > v.Crossing` → `Drawn`, below-time cleared; `pull >= v.Floor` → `Braced`, `WasBraced = true`, below-time cleared; else start the below-time if unset, `WasBraced = true`, and `State = now - belowSince >= v.GraceSeconds ? Lost : Grace`. `Open(pull, v)` = `WasBraced && (State == Drawn || State == Braced) && pull >= v.LooseLine`. `BraceValues.LooseLineFor(snapDraw, normal, snapDrawLine)` = `snapDraw ? snapDrawLine : normal`. `BraceRule.ReleaseStrength` = `open || holyArmed ? 1f : strength`.
- `SteadyRule`: tier clamped to 0..3; `SnapSpeed` is `1 + Speed(tier)` only for `Kind == Snap && ultimate != Line`, else 1.
- `FillTimeline.Grown` = `thumpAt <= 0 ? 1 : clamp01(t / thumpAt)`; `WhiteHot` = `clamp01((t - thumpAt) / (overchargeAt - thumpAt))` (0 when `overchargeAt <= thumpAt` and `t < overchargeAt`, else 1); `Braced` = `dim + amplitude * sin(2π · since / period)`; `LostRemaining` = `seconds <= 0 ? 0 : 1 - clamp01(since / seconds)`; `Between` = `from < at && at <= to`.
- `BalanceRule.Drained` = `max <= min ? 0 : clamp01((max - value) / (max - min))`; `Ease` = `target + (shown - target) * exp(-rate * dt)`, clamped between `shown` and `target`; `Alpha` = 1 while `now - lastHit <= hold`, then `1 - clamp01((now - lastHit - hold) / fade)`.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (319)`.

- [ ] **Step 5: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Charge.cs`, `QuickshotRing.Tests/ChargeTests.cs`, `.csproj`, `Program.cs`.

---

### Task 3: The echo fix and the test build's `Without`

**Files:**
- Modify: `QuickshotRing/Claims.cs` (header `:10-15`; add `Init`, `TrustMs`, `ClaimedMs`, `ClaimedKind`; `PutResolved` `:29-36`; the receipt line `:76-82`), `QuickshotRing/Draws.cs:104-112` (`ServerFacts`), `:115-123` (`ClientFacts`), `:125-128` (`ResolveServer`, `ResolveLocal`), `:140-196` (`Release`), `QuickshotRing/Volley.cs:116-121`, `:140-156` (`Ghosts`, `Racked` at a given time), `QuickshotRing/Builds.cs:50-66` (`Compute`), `:112-116` (`LocalBuildKnown`), `QuickshotRing/TestBuild.cs:25-45` (`Without`), `QuickshotRing/Quickdraw4.cs:62` (`LocalPlayerHasIt`), `QuickshotRing/Main.cs:59-66` (`Claims.Init()` before `Cfg.Ready`)

**Interfaces:**
- Consumes: `ClaimTrust`, `ClaimRules.SeenUltimate`, `ReleaseFacts.Overcharged`, `Resolution.Overcharged`, `TestBuildRule.Combine` (Task 1); `WeightRules.Overcharged` (Task 2); `Claims`' `incoming` store (`Claims.cs:18`), filled by the read in `ShootArguments.Serialize`'s postfix before `Shooter.SyncShoot` calls `Shoot` (`_decomp/Shooter.cs:45-61`); Quickdraw's pull settings (`Tree.Quickdraw.InputStructure as BowStringPullbackStructure`: `IsTimingOut`, `TimeoutDuration`, as `Main.cs:161-178` reads them).
- Produces:
  - `Claims.Init()`, `static int Claims.TrustMs` (`[Claim] TrustMs`), `static int? Claims.ClaimedMs(uint arrowId)` and `static ShotKind? Claims.ClaimedKind(uint arrowId)` (server: the time and kind of this arrow's incoming claim, null when none).
  - `static void Claims.PutResolved(uint arrowId, Resolution r, int? effectiveMs)` (replaces the two-argument form; the relayed claim's `TimeMs` becomes `effectiveMs`, the time the server decided with).
  - `static int? Draws.ServerTime(BowString bow, uint arrowId, out int? serverMs, out int? claimedMs)` (the effective milliseconds); `Draws.ResolveServer(BowString bow, Player? owner, uint arrowId)` (replaces the two-argument form); `static int Draws.QuickdrawWindowMs` (`TimeoutDuration × 1000`, or 1 000 000 when Quickdraw does not time out or its settings are missing).
  - `Volley.Ghosts(BowString bow, float? atSeconds)`, `Volley.Racked(BowString bow, float? atSeconds)`: `atSeconds` null = this machine's own time, as today; the one-argument forms call them with null.
  - `Weight.OverchargeSeconds` is read here; until Task 4 creates it, use `3.75f` through a temporary `const` in `Draws` that Task 4 replaces (Task 4's Interfaces say so).
  - `static bool Builds.LocalBuildKnown` (the server has sent this client its own build).

- [ ] **Step 1: The server decides with the effective time**

`Draws.ServerTime`: `serverMs = ElapsedMs(bow)`, `claimedMs = Claims.ClaimedMs(arrowId)`, return `ClaimTrust.EffectiveMs(serverMs, claimedMs, Claims.TrustMs)`. `ServerFacts(bow, owner, arrowId)`: `t = effective / 1000f` (null when none); `SnapReady = ClaimTrust.SnapReady(InputActive(Tree.Quickdraw, bow), Claims.ClaimedKind(arrowId), claimedMs, effective, QuickdrawWindowMs)`; `Crossed = t != null`; `Ghosts = Volley.Ghosts(bow, t)`; `Racked = Volley.Racked(bow, t)`; `Overcharged = WeightRules.Overcharged(build, ChargeActive, t, OverchargeSeconds)`. In `Volley`, `Inputs` (`Volley.cs:116-121`) takes the given time on the server when it is not null. `ClientFacts` sets `Overcharged = WeightRules.Overcharged(build, ChargeActive, Elapsed(bow), OverchargeSeconds)` on the shooter; a spectator's stays false (it uses the relay). `Release` calls `ResolveServer(bow, owner, id)` and `Claims.PutResolved(id, r, effective)`, and in the spectator branch (`Draws.cs:168-175`) builds the relayed resolution with `ultimate = ClaimRules.SeenUltimate(c.Kind, build)`, `chargedTier = c.Kind == ShotKind.Charged ? Math.Max(1, build.Charge) : 0` and `overcharged = WeightRules.Overcharged(build, c.Kind == ShotKind.Charged, c.TimeMs / 1000f, OverchargeSeconds)` (`build` = `Builds.ClientBuild(name)`; `c.TimeMs` is now the server's effective time, so spectators judge Overcharge on the time the server decided with), so every client knows a seen release's charge and Line for the looks of Tasks 9 to 11 and Steady. The `Release:` log line's `t` becomes the effective time on the server. Claims' header comment (`Claims.cs:10-15`) says the relayed time is the time the server decided with, not the client's.

Log line: extend the existing receipt line in `Claims.Shot`'s prefix (`Claims.cs:80-82`, both the claim and the no-claim branch) rather than adding a second per-shot line; it ends, exactly, with ` -> deciding with {effective|none} ms: {ClaimTrust.Why(serverMs, claimedMs, TrustMs)}` (the prefix runs in the same frame as `Draws.Release`, with the same inputs). For example: `[Archery] Claim: timing arrow 123 from 'name': client 83 ms, server no crossing ms at receipt, diff ? ms, within 60 ms: no | stage client 0, server 0 -> deciding with 83 ms: the server saw no crossing; the client's claim is trusted`.

- [ ] **Step 2: The test build's `Without`**

`[TestBuild] Without` (description in the Settings table), parsed with `TestBuildRule.Parse` like `Orbs`; `TestBuild.WithoutFor(string? playerName)` mirrors `ExtraFor` (`TestBuild.cs:55-56`). `Builds.Compute` (`Builds.cs:50-66`) builds from `TestBuildRule.Combine(owned, practising, extra, without)`. `ServerOwnedUltimates` (`:87-92`) and `TestBuild.Sync` (`:79-120`) ignore `Without`. The `TEST BUILD ACTIVE` line (`TestBuild.cs:40-41`) becomes: `[Archery] TEST BUILD ACTIVE: '{player}' is treated as also owning {orbs|nothing} and as not owning {without|nothing}. Nothing is saved. Clear [TestBuild] Orbs and Without in the server's QuickshotRing.cfg to stop.`; active = a player is named and either list is non-empty. `Builds.LocalBuildKnown` is true once the client cache holds the local name. `Quickdraw4.LocalPlayerHasIt()` becomes `Builds.LocalBuildKnown ? (Builds.LocalBuild().Ultimates & Ultimates.Quickdraw4) != 0 : localHasIt`, so a client whose build says "no Quickdraw 4" never predicts a Line from the session's old latch.

- [ ] **Step 3: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Claims.cs`, `Draws.cs`, `Volley.cs`, `Builds.cs`, `TestBuild.cs`, `Quickdraw4.cs`, `Main.cs`.

---

### Task 4: Weight on the server: probes, the real arrow's hit, stagger, immunity, interrupt

**Files:**
- Create: `QuickshotRing/Weight.cs`
- Modify: `QuickshotRing/Hits.cs:99-129` (`Apply` under `Dealing`), `:180-206` (`IsOpen`), `:245-295` (`FlushSpectral` under `Dealing`; hand its sums to Weight), `:306-326` (`Close` tells Weight), `QuickshotRing/HolyArrow.cs:42-75` (`BlastCripple` accessor), `QuickshotRing/Draws.cs:140-196` (arm the carry; replace Task 3's temporary constant with `Weight.OverchargeSeconds`), `QuickshotRing/Quickdraw4.cs:106-131` (Line hit), `QuickshotRing/Main.cs:49` (`Weight.LogPatches()`), `:59-66` (`Weight.Init()`), `:90` (`Weight.Tick()` right after `QuiverGather.Poll();`)

**Interfaces:**
- Consumes:
  - The five knock-downs: `Turabada.Stun(float)` (`_decomp/Alta.Intelligence/Turabada.cs:64-68`), `Gotera.Stun(float)` (`Gotera.cs:67-70`), `GreaterGotera.Stun(float)` (`GreaterGotera.cs:63-66`), `Babu.Stun(float)` (`Babu.cs:56-59`), `Dragonfly.Stun(float)` (`Dragonfly.cs:64-67`); `Target.HealthObject` (`Alta.Intelligence/Target.cs:41`). The likely trigger is `FullEmptyStatTrigger`, which fires on empty only once the pool was full again (`_decomp/Alta.StatSystem/FullEmptyStatTrigger.cs:39-68`).
  - The cripple pool: `HealthObject.HasCrippleStat`, `CrippleStat` (`_decomp/HealthObject.cs:175-177`), `DetectImpact(ImpactHit)` (`:271-292`; it scales cripple by the creature's own multiplier for the hit's `ImpactType`, the private `impactTypeMultipliers`, and by the detector's armour, `:277-285`), `ReceiveDamage` takes cripple off the pool on the server only (`:294-323`); `Stat.Value`, `Minimum`, `Maximum`, `Changed` (`_decomp/Alta.StatSystem/Stat.cs:148`, `:47`, `:64`, `:154`). The blast's recipe: `new ImpactHit(health, cripple, 1f, 1f, null, ImpactType.Spiritual, point, Vector3.zero, Vector3.zero, null, wielder, playerWielder)` then `DetectImpact` (`HolyArrow.cs:304-306`; constructor `_decomp/Alta.Impact/ImpactHit.cs:113`).
  - The real arrow's hit: a flying arrow is not held, so the server resolves its collision itself (`_decomp/Alta.Impact/ImpactDetector.cs:182-203`, `OnCollisionEnter`), ending in `ImpactDetector.OnToolHit(ImpactHitWithTool)` (`:237-266`; returns early on the per-tool cooldown `hitCooldowns`, `:92`, `:239-242`), which runs on clients too when the creature's colliders are active there (`:191`; `Target` disables its own object on clients, `Alta.Intelligence/Target.cs:54-58`, so this is checked at runtime, Task 11). `ImpactHitWithTool.Tool`, `Impactor`, `Point`, `Normal`, `HealthDamage`, `Multipliers`, `Effectiveness` (`ImpactHitWithTool.cs:15-32`, `ImpactHit.cs:44-78`), `RelativeVelocity` (`ImpactHit.cs:32`, set at `ImpactTool.cs:705`, `DelayedImpactResolver.cs:52`), `Direction` (`ImpactHit.cs:85`); the tool's `Entity` is the `NetworkEntity` on the tool's own object (`ImpactTool.cs:86-88`), and an arrow has several tools (its own and its parts'; `Hits.Tools`), so the arrow is found from the tool, never from `Tool.Entity` alone. `ImpactDetector.HealthTarget` (`ImpactDetector.cs:130`); `NetworkEntity.PrefabRoot` (`_decomp/Alta.Networking/NetworkEntity.cs:217`).
  - The interrupt: `Alta.Intelligence.Agent` (it derives from `Target`, so it has `HealthObject`; `Agent.cs:18`; write the namespace out everywhere, `_decomp` also has `Pathfinding.RVO.Sampled.Agent`), `Agent.ActiveAction` (`Agent.cs:127`), `IActiveAction.Action` (`IActiveAction.cs:7`), `IAction.Name`, `IAction.GeneralSettings` (`IAction.cs:9-23`), `IActionSettings.IsCancellable` (`IActionSettings.cs:9`), `Agent.CancelCurrentAction()` (`Agent.cs:461-464`, server only; it only calls `ActiveAction.Cancel()`, and the cancelled handler clears the action and sets `nextUpdate = 0`, `:351-360`, so the agent re-decides at once); `Wyrm` (`Alta.Intelligence/Wyrm.cs:10`).
  - `WeightValues`, `WeightCarry`, `WeightRules`, `WeightLedger` (Task 2); `Draws.ReleaseOf` (`Draws.cs:74`); `Hits.Spectral`'s batch (`Hits.cs:245-295`).
- Produces:
  - `internal static class Weight { static WeightValues Values; static float OverchargeSeconds; static float StaggerUnit /* HolyArrow.BlastCripple */; static void Init(); static void LogPatches(); static void Arm(uint arrowId, Resolution r, Build build, Player? shooter) /* server */; static void ArmClient(uint arrowId, Resolution r, Build build, bool local) /* clients */; static WeightCarry CarryOf(uint arrowId); static void AddSpectral(uint release, HealthObject target, float shares); static void ReleaseClosed(uint release); static void OnLineHit(uint arrowId, RaycastHit hit, Player? shooter); static void Tick(); }`
  - `static bool Hits.Dealing` (true while the mod deals a hand-made tool hit); `static bool Hits.IsOpen(uint release)` (the release still has spectral arrows out: `releases.ContainsKey(release)`, `Hits.cs:181`).
  - `static float HolyArrow.BlastCripple` (`cripple.Value`, `HolyArrow.cs:49`).
  - `static Arrow? Weight.ArrowOf(ImpactTool tool)` (`tool.GetComponentInParent<Arrow>()`, else `tool.Rigidbody?.GetComponent<Arrow>()`); armed arrows are keyed by `arrow.Entity.Identifier`. Task 11 uses it on clients.

- [ ] **Step 1: `Hits.Dealing`**

Set `Dealing = true` around every `.Hit()` call in `Apply` (`Hits.cs:118-119`) and `FlushSpectral` (`:270-274`), cleared in the same `finally` blocks that clear the cooldowns. (Task 5's `Copy` does the same.)

- [ ] **Step 2: Settings and the knock-down probes**

`Weight.Init` creates `[Weight]` and `[Overcharge] Seconds` (Settings table; the stagger values and `PierceRange` shared). A patch class with `TargetMethods` returning the five `Stun(float)` methods (`AccessTools.Method(type, "Stun", new[] { typeof(float) })`, skipping nulls) and a postfix `(Alta.Intelligence.Agent __instance, float strength)` (server only): record the knock-down time per creature (key: the `HealthObject`'s instance id; a record class per creature holds the last knock-down, the last interrupt, whether its pool was full since the last knock-down, and the `HealthObject`; capped at 256, destroyed ones dropped first). The first time a creature is seen, subscribe to its `CrippleStat.Changed` (server) to note "full again" (`Value >= Maximum - 0.0001f`). `LogPatches` (from `Main.OnLateInitializeMelon`) lists which of the five methods carry this mod's patch (`Harmony.GetPatchInfo`).

Log lines, exactly:
- `[Archery] Weight: knock-down patches live on {Turabada, Gotera, GreaterGotera, Babu, Dragonfly as found} ({n} of 5)`
- `[Archery] Weight: '{creature}' ({type}) knocked down by the game (Stun {strength:0.00}); pool {value:0.00} of {max:0.00}; previous knock-down {s:0.0} s ago|never; pool full again since: {yes|no|-}`

- [ ] **Step 3: The real arrow's hit (server and clients)**

Prefix and postfix on `ImpactDetector.OnToolHit(ImpactHitWithTool impactWithTool)`. Prefix: `__state` = the target's pool value (NaN when none) and whether the hit will pass the cooldown (the tool is not in `hitCooldowns` or its time has passed). Postfix, in this order: return when `Hits.Dealing`, when the hit was blocked, or when the target is a `PlayerHealth` or has no `HealthTarget` (a terrain or object hit only disarms an armed arrow); the arrow is `Weight.ArrowOf(hit.Tool)` (return when null), its id `arrow.Entity.Identifier`; on the server, log the `Hit:` line below for every real arrow (armed or not: it is the cripple probe); return when that id is not an arrow this machine armed (`Arm`/`ArmClient`). Each armed arrow acts on its **first** creature hit only. On the server, from the arrow's carry: the stagger goes into the ledger keyed by (arrow id, creature) with `releaseOpen = Hits.IsOpen(arrowId)`; the interrupt (Step 5); the pierce (Task 5). On clients the same postfix is Task 11's hook for the looks; this task only logs the shooter's own hit.

Log lines, exactly:
- server, every real arrow hit on a creature, armed or not (this is the cripple-before-and-after probe the spec asks for before tuning): `[Archery] Hit: arrow {id} on '{creature}' {damage:0.##} damage at {speed:0.0} m/s at ({x:0.00}, {y:0.00}, {z:0.00}) ({kind}{ overcharged}); pool {before:0.00} -> {after:0.00} of {max:0.00}` (`kind` from `Draws.ReleaseOf(id)`, `?` when unknown; `speed` = `RelativeVelocity.magnitude`)
- once per side, on the first real arrow hit on a creature: `[Archery] Hit: tool '{tool.name}' entity {tool.Entity.Identifier} -> arrow {arrowId}` (a mismatch between the tool's entity and the arrow shows here)
- shooting client, its own arrow: `[Archery] Hit (local): arrow {id} on '{creature}' at ({x:0.00}, {y:0.00}, {z:0.00})`

- [ ] **Step 4: Stagger: one cripple hit per creature per release**

`Draws.Release` calls `Arm` on the server and `ArmClient` on clients, after the decision, with the build it already resolved (server `ServerBuild`, shooter `LocalBuild`, spectator `ClientBuild`). Both store `WeightRules.ForRelease(r, build, Values)` per arrow id (cap 64, expires after 10 s) when `Any`; `Arm` logs it. `AddSpectral` (from `FlushSpectral`, after a dealt hit on a non-player `HealthTarget`): `shares × carry.GhostStaggerPerShare` into the ledger with `releaseOpen = true` (the release is open while its spectral hits flush). `ReleaseClosed` (from `Hits.Close`) deals that release's entries at once (`TakeRelease`). `Tick` (server) takes `TakeDue(Time.time, BatchSeconds, MaxWaitSeconds)` and deals each: skip a destroyed creature and any `PlayerHealth` (defence in depth); skip (and log) when `WeightRules.Immune(now, lastKnockDown, KnockDownImmunity)`; else deal `new ImpactHit(0f, WeightRules.Cripple(stagger, StaggerUnit), 1f, 1f, null, ImpactType.Spiritual, position, Vector3.zero, Vector3.zero, null, null, shooter's PlayerController)` through `HealthObject.DetectImpact`, reading the pool before and after, and the creature's Spiritual multiplier (`impactTypeMultipliers[ImpactType.Spiritual]` read once per creature by `AccessTools.Field`, 1 when absent; a kind with 0 never staggers, and the log says why).

**Where `Tick` runs:** `Main.OnUpdate` returns early at `Main.cs:98` (`if (ring == null || !ring.activeSelf) return;`), and on the headless server `ring` is always null, so a call after line 97 never runs there. Call `Weight.Tick()` inside its own `try/catch` right after `QuiverGather.Poll();` at `Main.cs:90`, before the slow-poll block.

Log lines, exactly:
- once, on the server's first `Tick`: `[Archery] Weight: tick is live (server)`
- server, at release: `[Archery] Weight: arrow {id} carries {carry}` (only when `Any`)
- `[Archery] Weight: arrow {id} '{creature}' stagger {s:0.00} in {parts} part(s) -> cripple {c:0.000} (Spiritual x{m:0.##}); pool {before:0.00} -> {after:0.00} of {max:0.00}`
- `[Archery] Weight: arrow {id} '{creature}' stagger {s:0.00} skipped: knocked down by the game {t:0.0} s ago`

- [ ] **Step 5: The interrupt**

On the first creature of an armed arrow whose carry has `Interrupt`, and in `OnLineHit` (called from `Quickdraw4.ServerShot`'s postfix after `Hits.Apply` struck a creature), never on a `PlayerHealth`: find the creature's agent from its prefab root, `h.Entity.PrefabRoot.GetComponentInChildren<Alta.Intelligence.Agent>(true)`, keeping the first whose `HealthObject == h` (the agent's object is disabled on clients and need not sit above the `HealthObject`, `Target.cs:18-19`, `:54-58`), cached in the creature's record; read `ActiveAction` and its settings; decide with `WeightRules.MayInterrupt(now, lastInterrupt, InterruptCooldown, lastKnockDown, KnockDownImmunity, cancellable, agent is Alta.Intelligence.Wyrm, InterruptWyrms, Interrupt)`; when allowed, `CancelCurrentAction()` and record the time. The cooldown and the immunity live on the creature's record, so they hold whoever caused them (two archers, Review Focus 10). 0.3 s after each `cancelled`, log what the agent is doing (it re-decides at once, `Agent.cs:351-360`).

Log lines, exactly:
- at every attempt: `[Archery] Weight: interrupt '{creature}' action '{name|none}' cancellable {yes|no}: {cancelled|skipped (off)|skipped (no Agent found)|skipped (no action)|skipped (not cancellable)|skipped (Wyrm)|skipped (knocked down {s:0.0} s ago)|skipped (cooldown, {s:0.0} s left)}` (the first reason that applies, in that order).
- 0.3 s after a `cancelled`: `[Archery] Weight: after interrupt '{creature}' now '{ActiveAction?.Action.Name|none}'`

- [ ] **Step 6: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Weight.cs`, `Hits.cs`, `HolyArrow.cs`, `Draws.cs`, `Quickdraw4.cs`, `Main.cs`.

---

### Task 5: Pierce

**Files:**
- Modify: `QuickshotRing/Hits.cs` (add `Copy`), `QuickshotRing/Weight.cs` (pierce on the first creature)

**Interfaces:**
- Consumes: `ImpactHitWithTool(ImpactTool tool, ImpactGenerator impactor, ImpactDetector detector, float multipliers, float effectiveness, ImpactChannel channel, Vector3 point, Vector3 normal)` (`_decomp/Alta.Impact/ImpactHitWithTool.cs:51-74`) and `.Hit()` (`:106-111`); the real hit's `Multipliers` and `Effectiveness` carry the arrow's damage, one-off multiplier and speed effectiveness (`ImpactTool.cs:685`), and the tool's one-off multiplier is already back to 1 by then (`ImpactTool.cs:706`), so the copy must reuse the hit's numbers, not the tool's. The 8-argument constructor copies the base damage, `Multipliers` and `Effectiveness` only: `HealthMultiplier`/`CripplingMultiplier`, which weak spots (`_decomp/Alta.Impact/DamageMultiplierCollider.cs:35-39`), directional colliders (`_decomp/Alta.Impact/DirectionalMultiplierCollider.cs:26-27`) and enchantment `ImpactModifier`s set after the hit is built, are not carried over, and `.Hit()` fires the tool's and impactor's `OnHittingImpactDetector` again (`ImpactHitWithTool.cs:108-109`), so enchantment modifiers run again on the copy. `PierceCandidate`, `PierceRule` (Task 2); `Physics.RaycastAll(origin, direction, range, ~0, QueryTriggerInteraction.Ignore)`; `Hits.Shared` (`:349-355`), the cooldown clearing (`:116-120`); `WeightCarry.Pierce`, `Weight.PierceRange` (Tasks 2, 4); `NetworkEntity.PrefabRoot`.
- Produces: `static bool Hits.Copy(ImpactHitWithTool first, ImpactDetector next, Vector3 point, Vector3 normal, out string what)` (server; same filters as `Apply`: players skipped, the impactor's hardness and a shared channel; under `Dealing`; clears the tool's cooldown before and after).

- [ ] **Step 1: The continuation**

On the server, for the first creature of an armed arrow with `Pierce > 0`. **Direction:** the pre-impact direction `hit.RelativeVelocity.normalized` (set at `ImpactTool.cs:705`, `DelayedImpactResolver.cs:52`), flipped if it points back toward the shooter (`Vector3.Dot(dir, hit.Point - shooterPosition) < 0`: Unity's sign for `relativeVelocity` is the detector's view, so it is checked, not assumed), falling back to `hit.Direction`, then to the arrow's `transform.forward` (read in a postfix after the hit, the arrow may already be embedded or re-aligned). Log once which source and sign were used. **Each hop:** `RaycastAll` from the last hit point up to `PierceRange` (a fresh 8 m per hop), sorted by distance; colliders under the arrow, the shooter or the bow are dropped; each other hit becomes a `PierceCandidate` with `Id` = its `ImpactDetector`'s `HealthTarget`'s `PrefabRoot` instance id (0 when it has none), `HasHealth` = it has a `HealthTarget`, `IsPlayer` = that target is a `PlayerHealth`; `struck` starts with the first creature's id (its `Entity.PrefabRoot`, never `transform.root`, which can be the chunk and would hide the second creature too). `PierceRule.Next` picks the creature that takes `Hits.Copy`; repeat from its hit point up to `Pierce` times. Pierced creatures take the damage hit only (no stagger, no interrupt).

Log lines, exactly: once, `[Archery] Weight: pierce direction from {RelativeVelocity|RelativeVelocity (flipped)|Direction|arrow forward}`; per pierced shot (spec "Must also log": the real arrow's hit beside the continuation's): `[Archery] Weight: arrow {id} pierce: '{first}' {damage:0.##} at ({x:0.00}, {y:0.00}, {z:0.00}) -> '{next}' {what} ({copyDamage:0.##} damage) at ({x2:0.00}, {y2:0.00}, {z2:0.00}){ -> ...| -> stopped by '{solid}' at {d:0.0} m| -> nothing within {range} m}`

- [ ] **Step 2: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Hits.cs`, `Weight.cs`.

---

### Task 6: Brace and Overcharge in the tree

**Files:**
- Modify: `QuickshotRing/Tree.cs:15` (add `public class PassiveOrbSkill : ProfessionSkill { }`), `:83` (`PlaceholderIcons` becomes a dictionary), `:88-108` (settings), `:152-303` (`Inject`: slots, orb map, layout, icons), `:393-399` (`ApplyBurstCost` becomes `ApplyCosts`), `:460-480` (`MakeQuiverSkill` generalised), `:531-560` (`ApplyIcons` placeholder line); `QuickshotRing/Shrine.cs` (add `Practise`)

**Interfaces:**
- Consumes: `AddSlot` (`Tree.cs:443-458`), `SlotIdFree` (`:385-391`), `PickPosition` (`:482-497`), `chargeSlots` (`:38-40`, ordered by depth), `placed` and `autoPosition` (`:59-61`, `:280-284`), `iconSlots` (`:84`); `SlotIds.Brace`, `SlotIds.Overcharge` (`Build.cs:136`), `SlotIds.BraceSkill`, `OverchargeSkill` (Task 1); `PlayerProgressionManager.PracticingOrb` (`_decomp/PlayerProgressionManager.cs:70`), `MarkSlotPracticed(ProgressionPath, uint)` (`:537`).
- Produces: slots mapped as `Orb.Brace` (needs `chargeSlots[0]`) and `Orb.Overcharge` (needs `chargeSlots[1]`), each pointing at its own `PassiveOrbSkill`; `iconSlots["Brace"]`, `iconSlots["Overcharge"]`; `static void Shrine.Practise(Player? owner, Orb orb, string how)`.

- [ ] **Step 1: The slots**

`MakePassiveSkill(ProfessionSkill skill, string name, int id)` is today's `MakeQuiverSkill` body (`Tree.cs:460-480`) with the type, name and id as parameters; Quiver Gather calls it with `QuiverGatherSkill`. Before inserting a passive skill, if the skill table already holds that id, log `[Archery] Tree: skill id {id} for {orb} is already used by '{name}'; {orb} is off` and skip the orb. After the Burst block: when `chargeSlots.Count >= 1` and `SlotIdFree(tree, SlotIds.Brace, Orb.Brace)`, `AddSlot(tree, "Brace", braceSkill, chargeSlots[0], SlotIds.Brace, auto)`; likewise Overcharge after `chargeSlots[1]` (needs `Count >= 2`). `auto` = `PickPosition(tree, parent.Position, new Vector3(0f, -0.16f, 0.06f))`, kept in `autoPosition`; `[TreeLayout] Brace`/`Overcharge` (default `auto`) join `placed`. Map both in the orb map (`:237-250`). Costs from `[Tree] BraceCost`/`OverchargeCost` in `ApplyCosts`, with Burst's.

Log line, exactly, per orb: `[Archery] Tree: {Brace|Overcharge} at {pos} (slot {id}, needs '{parent.Name}', cost {c}, skill id {skillId})`; the existing `Tree: slot ...` lines list both.

- [ ] **Step 2: Placeholder icons**

`PlaceholderIcons` becomes `Dictionary<string, string>`: `{ "Burst", "the game's Three-Shot glyph" }`, `{ "Brace", "the game's Slowdraw glyph" }`, `{ "Overcharge", "the game's Slowdraw Plus glyph" }`. The log line (`Tree.cs`, in `ApplyIcons`) becomes: `[Archery] Icon '{name}' loaded from built-in (PLACEHOLDER: {glyph} until the drawn icon is approved)`. Task 13 removes entries once the user approves the drawn ones. Copy `Art\game_icons\Glyph_Bow_Slowdraw.png` to `Resources\Brace.png` and `Glyph_Bow_Slowdraw_Plus.png` to `Resources\Overcharge.png` now, so the orbs show a placeholder from this task on.

- [ ] **Step 3: `Shrine.Practise`**

Server only: when `owner`'s `PracticingOrb` slot is `Tree.SlotOf(orb)`, call `MarkSlotPracticed(slot.Path, slot.Hash)` and log exactly: `[Archery] Shrine: '{name}' practised {orb} by {how} (first time: {yes|no})`. The callers are Brace (Task 7, "bracing") and `Draws.Release` for `Overcharge` when `r.Overcharged` ("an overcharged shot"; add that call here).

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Tree.cs`, `Shrine.cs`, `Draws.cs`, `Resources/Brace.png`, `Resources/Overcharge.png`.

---

### Task 7: Brace: hold everything, full strength past the loose line

**Files:**
- Create: `QuickshotRing/Brace.cs`
- Modify: `QuickshotRing/Draws.cs:201-221` (clock hold), `:256-281` (`DrawGrace`'s brace branch), `QuickshotRing/VolleySkill.cs:182-214` (`ClientStatus` also hands every Slowdraw status to `Brace.ClientStatus`), `QuickshotRing/HolyArrow.cs:200-215` (`BeamShot`: the beam from the full-draw speed), `QuickshotRing/Main.cs:183` (the ring's relax test), `:59-66` (`Brace.Init()`)

**Interfaces:**
- Consumes: `BraceTracker`, `BraceValues`, `BraceRule` (Task 2); `DrawClock.Update(..., hold)` (Task 1); `Draws.InputActive(Tree.Slowdraw, bow)` (`Draws.cs:96-100`), `Builds.ServerBuild/LocalBuild`, `HolyArrow.ArmedOnClient` (`HolyArrow.cs:83-84`), `Volley.BowForShooter` (`Volley.cs:236-245`), `Shrine.Practise` (Task 6). The game: `CheckForPullback` and `Deactivate` (`_decomp/BowStringPullbackInputMethod.cs:93-130`; an input that stays in range never calls `WaitForCharge` again, `:99-104`, `:140-146`); `Shooter.Shoot(Pickup pickup, float strength, bool isFromNetwork, Vector3 direction)` builds the shot message from `strength` (`_decomp/Shooter.cs:69-131`, `:121-122`); the release strength is the string's pull at release (`_decomp/BowString.cs:248`, `:265`).
- Produces: `internal static class Brace { static void Init(); static BraceValues ValuesFor(Build build); static bool Update(BowString bow, float pull) /* true while it holds */; static BraceTracker? TrackerOf(BowString bow); static bool HoldsLocal(BowString bow); static void ClientStatus(BowString bow, ToolBasedSkillStatus status) /* Slowdraw only */; static bool SlowdrawChargedOnClient(BowString bow); static void Released(BowString bow) /* from Draws.Release on every machine */; }`

- [ ] **Step 1: The tracker per bow**

`Brace.Update` (server and the shooting client): `owned` = `SideOrbs.Brace` in the build (server: the string's last interactor's `ServerBuild`, as `Draws.Release` finds it; client: `LocalBuild()`); `charged` = server `InputActive(Tree.Slowdraw, bow)`, client `SlowdrawChargedOnClient(bow)`; values from `ValuesFor(build)` (`LooseLine` = `BraceValues.LooseLineFor(SideOrbs.SnapDraw in the build, LooseLine, SnapDrawLooseLine)`). On every change of state log; on the server, entering `Braced` calls `Shrine.Practise(owner, Orb.Brace, "bracing")`.
- **One decision per bow per frame:** the tracker's state is latched with `Time.frameCount`; the first call in a frame updates it and every later call in that frame (every input's `CheckForPullback` in one `OnUpdatedStringPull`, then `Draws.Clock`) returns the same answer. Without the latch, Slowdraw's input deactivating on the Lost branch makes `charged` read false for the inputs checked after it, in subscription order.
- **The client's own Slowdraw flag:** not `Volley.ChargedOnClient`, which reads the Volley state that `HolyArrow.Status` removes on Lightbeam's `Charged` (`HolyArrow.cs:344` → `Volley.Consume` → `states.Remove`, `Volley.cs:166-171`), so the collar and the brace would vanish the moment Holy Arrow arms. `Brace.ClientStatus` sets a per-bow flag on Slowdraw `Charged` and clears it on Slowdraw `Cancelled` or `Consumed` and in `Brace.Released`.
- **Reset:** the tracker is `Reset()` in `Brace.Released` (the arrow is shot; `Draws.Release` calls it after the decision, so Task 7 Step 3's prefix still reads the tracker), and by `Update` when the string is no longer held (`bow.Pickup.IsInteractedWith` false) or the arrow on the string differs from the one the tracker started with; this is the only way out of `Lost`.

Log line, exactly, on each state change: `[Archery] Brace ({server|local}): '{bow}' {Off|Drawn|Braced|Grace|Lost} at pull {p:0.00}{ (open past the loose line {l:0.00})}`.

- [ ] **Step 2: Hold the inputs and the clock**

`Draws.Clock`'s postfix calls `Brace.Update(bow, pull)` and passes its result as `hold` to `Clock.Update`. In `DrawGrace` (`Draws.cs:266`, right after the existing "not in range or above the threshold" exit): call `Brace.Update(___bowString, currentPull)`; while it holds, clear this input's dip timer and `return false` (every input of the bow, Quickdraw's included, stays in range); while the tracker is `Lost` (until its `Reset`), clear the dip timer and `return true` at once for every input of the bow, not only Slowdraw's (the brace's own grace already ran; Lightbeam and Volley must not get a fresh dip grace), logging `[Archery] Draw: {skill} lost: braced below {floor:0.00} for {grace} s`. `Main.cs:183` passes `pull <= settings.PullbackValue && !Brace.HoldsLocal(__instance)`, so the gold ring never re-arms during a brace.

- [ ] **Step 3: Full strength on release**

Prefix on `Shooter.Shoot` (`ref float strength`), shooting client only (`!isFromNetwork`, not the server): `bow = Volley.BowForShooter(__instance)`; `open` = the tracker's `Open(strength, values)`; `strength = BraceRule.ReleaseStrength(strength, open, HolyArrow.ArmedOnClient(bow))`. Log when the tracker was braced this draw or Holy Arrow is armed.

Log line, exactly: `[Archery] Brace: arrow {id} released at pull {p:0.000}: {full strength (past the loose line)|full strength (Holy Arrow armed)|kept (a slower arrow)}`.

The beam backstop (every machine): the same `Shooter.Shoot` prefix, which runs on every machine for this, records the shot's `strength` per arrow id (cap 32, after Brace's raise on the shooter; the server and spectators see the strength the shot message carried). In `HolyArrow.BeamShot`'s postfix (`HolyArrow.cs:208`), `v = __state.normalized × (__state.magnitude / s) × speed.Value` with `s` = that strength when it is known and between 0.05 and 1, else `v = __state × speed.Value` as today (the launch speed is linear in strength, `_decomp/Shooter.cs:99`, `:104`). The `Holy: fired, speed ...` line gains ` (full draw from strength {s:0.00}|as shot)`.

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Brace.cs`, `Draws.cs`, `VolleySkill.cs`, `HolyArrow.cs`, `Main.cs`.

---

### Task 8: Steady: less wobble, faster snaps

**Files:**
- Create: `QuickshotRing/Steady.cs`
- Modify: `QuickshotRing/Main.cs:59-66` (`Steady.Init()`)

**Interfaces:**
- Consumes: `SteadyRule` (`Boost` included), `SteadyValues` (Task 2); `ClaimRules.SeenUltimate` (Task 1); `Draws.ResolveServer(bow, owner, arrowId)`, `Draws.ResolveLocal(bow)`, `Claims.TryRelay` (`Claims.cs:39-42`), `Builds.*`. The game: `PhysicsSerializationSettings.VelocityMax` (100, `_decomp/Alta.Networking/PhysicsSerializationSettings.cs:11`); `ArrowFlightSettingsDevianceMultiplierModifier` with its private `devianceMultiplier` (`_decomp/ArrowFlightSettingsDevianceMultiplierModifier.cs:3-10`), multiplied into every tick's deviance (`ArrowFlightSettings.cs:65-85`; the curve is read at a time scaled by the speed, `:65-70`) from `Arrow.FlightModifiers` (`Arrow.cs:116`; the game adds fletching's the same way, `ArrowFlightModifierComponent.cs:20-36`); `Arrow.Shoot(Vector3 baseDirection, float strength, bool isApplyingDeviation)` (`Arrow.cs:268-278`; `strength` there is the speed in m/s, `Shooter.cs:99-100`), which returns the launch velocity the spectral arrows use; `Pickup.OnDocked` (private; already patched by `QuiverGather.cs:89-90`).
- Produces: `internal static class Steady { static void Init(); static SteadyValues Values; }`

- [ ] **Step 1: Decide per arrow at `Shooter.Shoot`**

Prefix on `Shooter.Shoot` (every machine): find the bow and the arrow id; the resolution and tier: server → `ResolveServer(bow, owner, id)`, `ServerBuild(owner).Quick`; shooting client → `ResolveLocal(bow)`, `LocalBuild().Quick`; spectator → the relayed claim's kind with `ClaimRules.SeenUltimate(kind, ClientBuild(name))` (a relayed Line is `Snap`, and must not be sped up), `ClientBuild(name).Quick`. Store `(SnapSpeed, Wobble)` for the arrow id (cap 64). The server's resolution already includes the claimed snap (Task 3), so a snap the shooter boosted is boosted on the server too.

- [ ] **Step 2: Apply in `Arrow.Shoot`**

Prefix on `Arrow.Shoot(Vector3, float, bool)` (`ref float strength`; patch by argument types, there is a one-argument overload): take the pending entry for this arrow; remove every modifier this mod added from `FlightModifiers`; add the shared modifier instance for the tier (three instances made once with `ScriptableObject.CreateInstance`, `devianceMultiplier` set from the live settings and refreshed on `Cfg.Reloaded`, `hideFlags = HideFlags.DontUnloadUnusedAsset`); set `strength = SteadyRule.Boost(strength, factor, max)` with `max = PhysicsSerializationSettings.VelocityMax - 1` (read at runtime, 99 if the read fails), only when the factor is above 1 and `strength < max` (a speed already at or over the cap is left as the game made it). A postfix on `Pickup.OnDocked` removes this mod's modifiers from a docked arrow.

Log line, exactly, per shot with a tier above 0: `[Archery] Steady: arrow {id} '{name}' tier {q} {kind}: speed {before:0.0} -> {after:0.0} m/s{ (capped at {max:0})}, wobble x{w:0.00} ({server|own|seen})`. (A snap's wobble also depends on its speed: the game reads its deviance curve faster for a faster arrow, `ArrowFlightSettings.cs:65-70`.)

- [ ] **Step 3: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Steady.cs`, `Main.cs`.

---

### Task 9: The Fill and the thump, and the glow goes in the same build

**Files:**
- Create: `QuickshotRing/ChargeFx.cs`
- Modify: `QuickshotRing/VolleySkill.cs:182-214` (`ClientStatus` also hands every Slowdraw status, with its owner and `local`, to `ChargeFx.Status`), `:220-236` (`NoGlow`), `QuickshotRing/Volley.cs:276-301` (`Fire` tells `ChargeFx.Released`), `QuickshotRing/Tree.cs:94` (`HideGameGlow`'s description; add `HideHolyGlow`), `QuickshotRing/Fx.cs:191` (`LogMaterialLookups`: the gradient test material), `QuickshotRing/Main.cs:59-66` (`ChargeFx.Init()`), `:104` (`OnLateUpdate`: `ChargeFx.Frame()` after `Volley.DrawHeld()`)

**Interfaces:**
- Consumes: `FillTimeline` (Task 2); `Draws.Elapsed` (local clock), `Builds.LocalBuild/ClientBuild`, `Volley.OtherArcherBrightness`, `Volley.BowOf`, `VolleyFx.Play` (`VolleyFx.cs:58`), `VolleyFx.Pulses` (`:50`), `Fx.LineMaterial`, `Fx.NewLine` (`Fx.cs:20-50`), `Fx.FindEffect` (`:114`), `Fx.ColourProps`; `Tree.SlowdrawThumpSeconds`. The game: the status callback (`_decomp/PickupBasedSkill.cs:121-166`, run through `WaitForSpawn` before the mod's own `ClientStatus` callback) runs `RequestToolEffect` on `Charged` (`:134-135`) and then the input structure's `PlaySoundAt` (`:164`, `SkillInputStructure.PlaySoundAt(Transform, ToolBasedSkillStatus)`, `_decomp/SkillInputStructure.cs:21-37`), which invokes the whole `chargedDefaultEffect` `TransformEvent` (it may hold visual effects as well as the sound); `ProfessionSkill.RequestToolEffect(NetworkEntity entity)` (protected, `ProfessionSkill.cs:207-218`); the flare: `GlobalSettings<ProgressionSettings>.Instance.DissipateToolBasedEffect.Get()` as `ToolBasedEffectBase`, then `InitializeEffect(Component)` (`ProfessionSkill.cs:181`, `_decomp/ToolBasedEffectBase.cs:56`); 'Orb Connection Line' through the 'Skill Orb Connection' line renderer (`Fx.cs:192-198`); `Controller.TriggerHaptic(float, float, float)` (`_decomp/Controller.cs:205`).
- Produces: `internal static class ChargeFx { static void Init(); static void Status(BowString bow, ToolBasedSkillStatus status, string owner, bool local); static void Released(BowString bow, uint arrowId, Resolution r); static void Frame(); static bool FillShownFor(NetworkEntity arrow); }` and the classes `Fill` and `Thump` inside it.

- [ ] **Step 1: The Fill**

Per bow string (clients, not headless): created on Slowdraw `ChargingStarted` (owner and whether it is local come with the status; `ClientStatus` already resolves the bow, `VolleySkill.cs:182-214`), removed on `Cancelled`, `Consumed`, `Released` or when the string has no arrow. Each frame: `t` = the local clock (`Draws.Elapsed`) for the local player, else `now - startedAt`; `grown = FillTimeline.Grown(t, Tree.SlowdrawThumpSeconds)`; shaft length `L` = the distance from the arrow tip to the string's grab point (as `Volley`'s `HeldLayout`, at least 0.3 m); the lit stretch runs from `tip - forward × Fraction × L` toward `tip - forward × HeadGap`, `grown` of the way. **Off the axis:** every point of the core, ember, halo and (Task 10) shell is moved toward the viewer, `Camera.main`'s position (the local player's head), by the shaft radius plus 0.002 m, perpendicular to the shaft (`offset = normalize(toCamera - forward × dot(toCamera, forward)) × (radius + 0.002)`). The shaft radius is measured once per arrow prefab: the smaller of the two cross-axis extents of the arrow's largest `MeshFilter.sharedMesh.bounds`, times its `lossyScale`, halved; 0.006 m when that fails. Without it the core sits inside the opaque shaft (the flat material keeps the default depth test, `Fx.cs:20-37`). Core: `Fx.NewLine` on the flat material, width `CoreWidth`, colour gradient `BackColour` to `FrontColour`. Halo: the same points, width `HaloWidth`, alpha `HaloAlpha`, on a copy of 'Orb Connection Line' with every colour property set to white (or the flat material when `HaloMaterial = flat` or the lookup failed). Ember: a short bright segment at the front, `EmberSize` long (`Charge2EmberSize` at Charge 2 or higher), flickering. Other archers' Fills at `Volley.OtherArcherBrightness`.

Log lines, exactly: once, `[Archery] Fill: halo material '{m}' ({shader}); colour properties reset: {props|none}` or `[Archery] Fill: halo uses the flat material ({why})`; once per arrow prefab, `[Archery] Fill: shaft radius {r:0.000} m, core offset {o:0.000} m ('{prefab}')`.

- [ ] **Step 2: The gradient test** (spec Step 4 "Must also log")

When `[Fill] GradientTest` and `UserData\QuickshotRing\gradient_test_done` does not exist, at the local player's first draw after start: one `LineRenderer` on its own copy of 'Orb Connection Line', 0.3 m long, 0.25 m above the arrow, start colour red, end colour green, for 5 s; then write that file (a failed write only logs). Log exactly: `[Archery] Fill: gradient test line on a copy of '{m}' ({shader}): red at the bow end, green at the far end; colour properties reset: {props|none}`.

- [ ] **Step 3: The thump**

On Slowdraw `Charged` for that bow (the server's own charge; the Fill may sit full for the round trip): flare the core and halo by `FlareBoost` over `FlareSeconds`; when `HeadFlare`, the game's `DissipateToolBasedEffect` on the arrow entity; one `Fx.Flash` at the tip; the haptic (local only, bow hand, `ThumpHaptic`, `Charge2ThumpHaptic` at Charge 2 or higher, 0.08 s); at Charge 2 or higher, a prefix on `SkillInputStructure.PlaySoundAt` returns `false` for Slowdraw's structure (`Tree.Slowdraw.InputStructure`, by reference) on `Charged` when that arrow's bow has a Fill of tier 2 or higher (a Fill exists only after Slowdraw's `ChargingStarted`, so nothing set at `Charged` is needed: the game's callback runs before the mod's) and the setting is not empty, and records that it suppressed the game's sound for that bow; `Charge2ThumpSound` is played at the tip only when that record exists (one sound per beat; a missed `ChargingStarted` leaves the game's sound alone and adds none). At Charge 3 the shell: a third line over the lit stretch, `ShellWidth`, alpha `ShellAlpha`, `FrontColour`, from the thump on.

Log lines, exactly: `[Archery] Fill: thump on '{bow}' (charge tier {t}{, local}) {ms} ms after the crossing; sound '{name|the game's}'`; once, the first time the prefix suppresses: `[Archery] Fill: Slowdraw's charged default effect has {n} listener(s): {target.method, ...}` (`TransformEvent`'s persistent and runtime listeners; it shows whether visual effects go with the sound).

- [ ] **Step 4: The glow goes only where the Fill is**

`NoGlow` (`VolleySkill.cs:220-236`) takes `NetworkEntity entity` and decides: Slowdraw → hide when `HideGameGlow && ChargeFx.FillShownFor(entity)`; the Volley skill → hide when `HideGameGlow`; Lightbeam → hide when `HideHolyGlow` (new, default false). Leave `ReleaseToolEffect` alone, as today. `HideGameGlow`'s description and `HideHolyGlow` as in the Settings table.

`FillShownFor(entity)` is true only when all hold: not headless; `[Fill] Enabled`; `Volley.BowOf(entity)` has a Fill whose arrow id equals `entity.Identifier`; and that Fill's core `LineRenderer` exists, has a material and is enabled. Anything else keeps the game's glow (the flat material lookup failed, the Fill is switched off, the string's arrow changed, or a `Cancelled`/`Released` removal raced the request), so no arrow is ever left with neither (spec Part 9 item 15). Each refusal on an arrow that had a Fill record says why.

Log lines, exactly: once per arrow (on its first Slowdraw glow request) and on every change, `[Archery] Glow: Slowdraw {hidden (the Fill shows the charge)|shown (no Fill on this arrow)}, Volley {hidden|shown}, Holy Arrow {hidden|shown} (arrow n)`; when a Fill record exists but is not drawn, `[Archery] Fill: not drawn on '{bow}' ({why}); the game's glow stays`.

- [ ] **Step 5: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `ChargeFx.cs`, `Volley.cs`, `VolleySkill.cs`, `Tree.cs`, `Fx.cs`, `Main.cs`.

---

### Task 10: Overcharge's heat and second thump; Brace's collar, rest and loss

**Files:**
- Modify: `QuickshotRing/ChargeFx.cs` (add `Collar`, which creates and owns the rest of `[Brace]`, the look-only entries; add `HotEmbers`, which creates and owns the look-only `[Overcharge]` entries; extend `Fill`), `QuickshotRing/Brace.cs` (`GhostDim`), `QuickshotRing/Volley.cs:711` (ghost brightness while braced), `QuickshotRing/Fx.cs` (the 'Hot Effect' check at first draw). `Weight.cs` keeps only `[Overcharge] Seconds` (Task 4).

**Interfaces:**
- Consumes: `FillTimeline.WhiteHot`, `Braced`, `LostRemaining`, `Between` (Task 2); `Brace.TrackerOf`, `Brace.ValuesFor` (Task 7); `Weight.OverchargeSeconds` (Task 4). The game: `EmissionControl.SetEmissionMultiplier(float)` (`_decomp/EmissionControl.cs:48-56`), `PooledObjectDefinition.Get(Vector3, Quaternion)` (`_decomp/PooledObjectDefinition.cs:53`).
- Produces: `ChargeFx.Collar` (with `static float BracedDim`), `ChargeFx.HotEmbers`; `static float Brace.GhostDim(BowString bow)` (1, or `ChargeFx.Collar.BracedDim` while braced: the ghosts dim but keep their ring formation).

- [ ] **Step 1: Overcharge (local draw and spectators alike, with Overcharge in that archer's build)**

From the thump to `[Overcharge] Seconds`, the core's colour turns `HotColour` from its back end forward by `FillTimeline.WhiteHot(t, thumpAt, overchargeAt)` (gradient keys); 'Hot Effect' is taken from the pool at the tip at the thump, parented to the tip, its emission multiplier ramped 0.2 to 1 by the same share when it has `EmissionControl` (else just on), released with the Fill. At `FillTimeline.Between(last t, t, overchargeAt)`: `SecondThumpEffect` at the tip, `SecondThumpSound`, the local bow hand's haptic at `SecondThumpHaptic`. On an overcharged release (`ChargeFx.Released` with `r.Overcharged`): `ReleaseSound` at the bow.

Log lines, exactly: at the first draw, `[Archery] Overcharge: 'Hot Effect' -> prefab '{p}', EmissionControl: {yes|no}` or `-> NOT FOUND`; per draw, `[Archery] Overcharge: second thump on '{bow}' at {ms} ms`.

- [ ] **Step 2: Brace (local draw only, with Brace in the build)**

At the thump, the collar: a ring (`Fx.BuildCircle`, flat material) around the shaft at the back end of the lit stretch, `CollarWidth` wide, radius `CollarClosedRadius`, `CollarColour`, with `ClampSound`. While the tracker is `Braced` or `Grace`: the Fill's alpha is `FillTimeline.Braced(since braced, BracedDim, BreatheAmount, 1)`, and `Volley`'s held ghosts take `Brace.GhostDim`. When `Open(pull)` becomes true: radius `CollarOpenRadius` and `OpenSound`; back to closed when it ends. On `Lost`: the Fill shrinks from the tip back over `LostDimSeconds` (`FillTimeline.LostRemaining`) with `LostSound`, then goes; the collar goes with it.

- [ ] **Step 3: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `ChargeFx.cs`, `Brace.cs`, `Volley.cs`, `Fx.cs`.

---

### Task 11: Charged hits you can see, and the Balance Ring

**Files:**
- Create: `QuickshotRing/Balance.cs`
- Modify: `QuickshotRing/ChargeFx.cs` (add `EmberTrail`, `PierceStreak`, `DustRing`; the client half of the hit hook), `QuickshotRing/Weight.cs` (the client branch of the `OnToolHit` postfix calls `ChargeFx.Hit` and `Balance.Hit`; the embed fallback), `QuickshotRing/Volley.cs` (own spectral hits on a creature call `Balance.Hit`), `QuickshotRing/Main.cs:59-66`, `:104` (`Balance.Init()`, `Balance.Frame()`)

**Interfaces:**
- Consumes: `Weight.CarryOf`, `Weight.ArrowOf` (Task 4), `PierceRule` (Task 2), `BalanceRule` (Task 2), `Embeddable.Embedded` (event `Action<Embeddable>`), `Embeddable.EmbeddedEntity` (`_decomp/Embeddable.cs:113`, `:132`), `VolleyFx.Sparks.Emit` (`VolleyFx.cs:255`), `Fx.FindEffect`, `Hits.Cast`; the cripple stat on clients arrives only after it moved by more than its sync step (`_decomp/Alta.StatSystem/Stat.cs:192-217`, `StatDefinition.cs:51`, `:93-96`; read at `Stat.cs:243-269`); the 'Particle_RingCracks' particle material (`loaded_assets.txt` line 1703), found as `Fx.cs` finds 'WeaponTrail' (a `ParticleSystemRenderer` whose shared material has that name), once, with the miss cached.
- Produces: `internal static class Balance { static void Init(); static void Hit(HealthObject target); static void Frame(); }`; `ChargeFx.Hit(uint arrowId, ImpactHitWithTool? hit, Vector3 point, Vector3 normal, HealthObject? creature)` (`hit` is null from the embed fallback).

- [ ] **Step 1: Hit looks** (every client; the carry from `Weight.ArmClient`)

On an armed charged arrow's hit: on a creature, `HitEffect` at the point, plus `Charge2HitEffect` and, when the ground is within 1.5 m below, a `DustRing` (a circle growing from `DustRingFrom` to `DustRingTo` over `DustRingSeconds`, fading) at Charge 2 or higher; overcharged, `[Overcharge] HitEffect`; with `Pierce > 0`, a `PierceStreak` (a 2-point gold line, `FrontColour`, 0.3 s fade) along the same cast as the server's (Task 5's rule, run locally for the look only) and `PierceExitEffect` at each exit point. On terrain (no `HealthTarget`), `GroundEffect`. In flight: an `EmberTrail` (`TrailRenderer`, `TrailSeconds`, `TrailWidth`, back colour to transparent) added at release (`ChargeFx.Released`) to a charged arrow, removed when it stops or after 3 s.

**The embed fallback** (whether clients get `OnToolHit` for creatures at all is unknown: `Target` disables its object on clients, `Target.cs:54-58`, and the creature's colliders may sit under it). At `ArmClient`, subscribe to the armed arrow's `Embeddable.Embedded`; when it fires with an `EmbeddedEntity` whose `PrefabRoot` holds a non-player `HealthObject`, and no client `OnToolHit` was seen for that arrow, call `ChargeFx.Hit` (with a null hit: point = the arrow tip, normal = `-forward`) and, for the local player's arrow, `Balance.Hit`. Unsubscribe when the arrow's carry expires.

Log line, exactly: `[Archery] Hit (embed fallback): arrow {id} in '{creature}' at ({x:0.00}, {y:0.00}, {z:0.00})`.

- [ ] **Step 2: The Balance Ring** (the local player only)

`Balance.Hit` from the local player's own real arrow hits (the client branch of Task 4's hook) and own spectral hits on a creature: create or refresh a `BalanceRing` for that `HealthObject` (at most 6; the oldest goes). The ring: at the creature's position, flat on the ground, radius from its colliders' bounds × `RadiusScale` (clamped 0.4 to 2 m); a quad with a copy of 'Particle_RingCracks' tinted `Colour`, alpha `0.3 + 0.7 × drained`; a `LineRenderer` arc on the flat material over `drained × 360` degrees (`Width`). `drained` eases (`BalanceRule.Ease`, `EaseRate`) toward `BalanceRule.Drained(CrippleStat.Value, Minimum, Maximum)`. Each hit by a shot of Charge 2 or higher flickers it once (the crack). It fades by `BalanceRule.Alpha(now, lastHit, HoldSeconds, FadeSeconds)`. When the received value reaches the minimum, it shatters: `ShatterSparks` sparks from `VolleyFx.Sparks`, and it goes. The ring subscribes to the creature's `CrippleStat.Changed` while it lives.

Log lines, exactly: `[Archery] Balance: ring on '{creature}' (radius {r:0.00} m, pool {v:0.00} of {max:0.00})` on creation; `[Archery] Balance: '{creature}' pool received {old:0.00} -> {new:0.00} of {max:0.00}` on every received change (the spec's "steps in which the client receives the cripple value"); `[Archery] Balance: '{creature}' went down; ring shattered` on the shatter.

- [ ] **Step 3: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `Balance.cs`, `ChargeFx.cs`, `Weight.cs`, `Volley.cs`, `Main.cs`.

---

### Task 12: Holy Arrow's gather on the Fill

Pulled forward from Step 8 to answer the user's "there needs to be more feedback in between the charge of Holy Arrow".

**Files:**
- Modify: `QuickshotRing/ChargeFx.cs` (`Fill`), `QuickshotRing/HolyArrow.cs:330-358` (`Status` also tells `ChargeFx`), `QuickshotRing/Volley.cs` (expose `GatherSeconds`)

**Interfaces:**
- Consumes: `FillTimeline.Squeeze` (Task 2), `DrawTimeline.GatherProgress(float sinceHolyStarted, float holySeconds, float gatherSeconds)` (`Rules.cs:18-22`), `Tree.HolySeconds`, `[Volley] GatherSeconds` (expose `Volley.GatherSeconds`); Lightbeam's statuses as `HolyArrow.Status` reads them (ChargingStarted at the crossing, Charged when armed, Cancelled).
- Produces: `static void ChargeFx.Holy(BowString bow, ToolBasedSkillStatus status)`.

- [ ] **Step 1: Squeeze and core**

With `[Fill] HolySqueeze`: while Lightbeam is charging on that bow, `gather = DrawTimeline.GatherProgress(now - holyStartedAt, Tree.HolySeconds, Volley.GatherSeconds)`; the lit stretch's back end sits at `FillTimeline.Squeeze(gather, Fraction, HolyCoreFraction) × L` behind the tip (so only `HolyCoreFraction × L` stays lit at full gather), and the core and halo brighten by `1 + (HolySqueezeBoost - 1) × gather`. From Lightbeam's `Charged` until release, that short core pulses at `HolyPulseHz`. A Lightbeam `Cancelled` returns the Fill to its normal shape.

Log lines, exactly: `[Archery] Fill: Holy gather on '{bow}'` (once per draw, when `gather` first rises above 0) and `[Archery] Fill: Holy armed core on '{bow}'`.

- [ ] **Step 2: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `ChargeFx.cs`, `HolyArrow.cs`, `Volley.cs`.

---

### Task 13: The Brace and Overcharge icons

**Files:**
- Modify: `QuickshotRing/Art/make_top_icons.py`
- Create: `QuickshotRing/Art/drafts/Brace.png`, `QuickshotRing/Art/drafts/Overcharge.png`

**Interfaces:**
- Consumes: `make_icons2` helpers (`alpha`, `clear_symbol_area`, `scratches`, `stamp`, `shapes`, `save`; `Art/make_icons2.py`), `Art/game_icons/Glyph_Bow_Slowdraw.png` (dashed frame, bow, the target-and-spark at about (266, 257), one chevron at about (285-340, 225-290), a small arrow at about (372-460, 243-268), on the 512 px glyph) and `Glyph_Bow_Slowdraw_Plus.png` (plain frame, two chevrons at about (288-380, 225-290)).
- Produces: `brace()`, `overcharge()`; the sheet `Art/drafts/sheet_final.png` with eight columns (Quickdraw4, QuiverGather, TwoShot, ThreeShot, HolyArrow, Burst, Brace, Overcharge; the last two referenced by `Glyph_Bow_Slowdraw` and `Glyph_Bow_Slowdraw_Plus`).

- [ ] **Step 1: Draw them from the game's glyphs** (memory rule "Art must match the game")

`brace()`: `clear_symbol_area` on a copy of `Glyph_Bow_Slowdraw`, `scratches(a, random.Random(6))`, stamp back the game's target-and-spark and its small arrow (crops measured on the glyph), then with `shapes()` a clamp on the shaft line between them: a solid band 12 px wide and 46 px tall centred on the shaft at about x = 330, with a 4 px gap ring either side (the collar), and a short downward-then-level stroke under it (the eased string). `overcharge()`: on a copy of `Glyph_Bow_Slowdraw_Plus`, keep the bow and the target, stamp the game's two chevrons, and with `shapes()` three short wavy heat lines rising above the chevrons and a small 7-ray burst at the shaft's tip. Append both to `names` and their glyphs to `refs`; the sheet width becomes `cell * 8`. Add `BRACE_APPROVED = False` and `OVERCHARGE_APPROVED = False` beside `BURST_APPROVED`; while not approved, the copy loop copies the reference glyph (the placeholder) instead of the drawing, as it does for Burst.

- [ ] **Step 2: Run it**

Run (from `QuickshotRing\Art`): `python make_top_icons.py`
Expected: `ok: sheet_final.png written, eight icons copied to Resources (...PLACEHOLDER...)` naming each placeholder, and `Resources\Brace.png`, `Resources\Overcharge.png` exist.

- [ ] **Step 3: The user sees the sheet before it ships**

The controller sends `QuickshotRing\Art\drafts\sheet_final.png` to the user (`SendUserFile`) and asks whether the Brace and Overcharge icons (the two right-hand columns, the game's Slowdraw glyphs above them) may ship. For each approved one: set its `..._APPROVED = True`, re-run the script, and remove it from `Tree.PlaceholderIcons`. Unapproved ones ship as the marked placeholders of Task 6.

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed: `make_top_icons.py`, `drafts/Brace.png`, `drafts/Overcharge.png`, `drafts/sheet_final.png`, `Resources/Brace.png`, `Resources/Overcharge.png` (and `Tree.cs` if an icon was approved).

---

### Task 14: Version, CLAUDE.md, final checks, and the two checklists

**Files:**
- Modify: `QuickshotRing/Main.cs:9` (`0.9.0`), `:14-21` (file list in the header comment), `CLAUDE.md` (QuickshotRing section)

- [ ] **Step 1: Version** `[assembly: MelonInfo(typeof(QuickshotRing.Main), "QuickshotRing", "0.9.0", "dmand")]`.

- [ ] **Step 2: `CLAUDE.md`** (QuickshotRing section; it still describes 0.7.0 in places)

- The test command's result becomes `ALL PASS (319)`; the `Rules.cs` row says the tests compile the six Unity-free files `Rules.cs`, `Build.cs`, `Wire.cs`, `Release.cs`, `Spectral.cs`, `Charge.cs`, and to keep Unity types out of all six.
- File table: replace the `Volley.cs` row ("Two-Shot/Three-Shot as 5/15 spectral extras ...") with the Volley skill's ghosts, echoes and pellets on one flight model; replace "Logging only in 0.7.0" in the `Builds.cs` and `Claims.cs` rows with what they do now (the server's build per player and the bits on bow statuses; the claim, the server's relayed decision and the trusted crossing time); `HolyArrow.cs`: arms on a live draw after Charge 3, no volley needed; add one line each for `Release.cs`, `Spectral.cs`, `Charge.cs`, `Draws.cs`, `Shrine.cs`, `TestBuild.cs`, `VolleySkill.cs`, `VolleyFx.cs`, `Burst.cs`, `Weight.cs`, `Brace.cs`, `Steady.cs`, `ChargeFx.cs`, `Balance.cs`.
- Replace the "Wire test (0.7.0)" paragraph with: `[Wire]` switches stay for wire tests; `[TestBuild] Player`, `Orbs` and `Without` in the server's cfg lend or hide orbs for play-tests (logged loudly, never saved); every release is decided once (`Draws.Release`) and relayed in the claim's kind and stage; the server decides time-based beats with the client's claimed crossing when it is ahead by at most `[Claim] TrustMs` (1000 ms; a claim at the 8190 ms cap is never an exact time), accepts the client's claimed snap when that time was trusted and inside Quickdraw's window, and relays the time it decided with.
- Replace "Changing a shipped default? Raise `Cfg.SettingsVersion`" with: shipped defaults are recorded in `UserData\QuickshotRing\shipped_defaults.txt`; a changed default resets only entries still at the old default; never raise `SettingsVersion`.
- Replace the Two-Shot/Three-Shot bullet with: both slots point at the mod's Volley skill (a cleaned copy of Slowdraw, id 47011, its own pull settings); Slowdraw is found once by type before the copy exists and by reference afterwards.
- Add, under "easy to get wrong": every hand-made hit runs under `Hits.Dealing` and the real-arrow hook (`ImpactDetector.OnToolHit`, which runs for real arrows on the server and on every client) ignores it; Weight's cripple goes through `HealthObject.DetectImpact` like the Holy blast, players never; the game's glow on Slowdraw is hidden only while the Fill is drawn on that arrow and Lightbeam's stays until the armed core ships (never remove feedback before its replacement); Brace holds every input by keeping it in pullback range in `DrawGrace`, and the draw clock with `hold`; Steady's speed rides `Arrow.Shoot`'s speed argument, never the shot message's strength (which clamps at 1), capped at `VelocityMax - 1`; a faster arrow also runs through the game's deviance curve faster, so snap wobble depends on speed and the modifier together; `Main.OnUpdate` returns early on the headless server (`ring` is null), so server work goes before that return; find a creature's `Alta.Intelligence.Agent` from `HealthObject.Entity.PrefabRoot`, and an arrow from a hit tool with `GetComponentInParent<Arrow>()` (an arrow has several tools, each with its own entity).

- [ ] **Step 3: Final checks** (no deploy: backup, deploy and the hand-over are the controller's after the final review)

Run: `dotnet run --project QuickshotRing.Tests -c Release` → `ALL PASS (319)`. Run: `dotnet build QuickshotRing -c Release` → `0 Error(s)`, 0 warnings.

- [ ] **Step 4: The play-test script (a), for the controller to hand over after deploying**

Before sending: fill `<you>` with the username in the server log; fill the icon sentence in A4 from Task 13's answer; write the result to `docs/playtest-0.9.0.md`. Claude looked for a way to bring creatures to the user and found none: the game's console starts only for an account with the `debug_features` permission (`_decomp/ServerConsoleManager.cs:47-58`), and TavernLib closes the remote console on this server (server `Latest.log`: "Closing ServerRemoteConsole instantly"); so the script says to hunt, as below. Rows are in priority order (the user explores and skips rows): the Fill and thump, the feedback up to Holy Arrow, Brace holding Holy Arrow, Overcharge's knock-down, the interrupt, then the echo fix.

The script, as handed over:

The server's settings file is `C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\UserData\QuickshotRing.cfg`. A setting is one line under its `[Section]`. Saved edits are picked up within 2 seconds. "Full draw" means the moment the string reaches full pull. Before each settings change, put the bow away; after saving, wait a couple of seconds, take it out and do one throwaway draw.

Setup: start the server, then the client, and join. Bring a bow and at least 40 ordinary arrows with metal heads, not the blunt training arrows: training arrows deal no damage, so a creature would barely react. Start at the Archers Refuge shrine, with open ground running well past 40 m. **Creatures:** Claude knows of none close to the Archers Refuge shrine and found no way to bring one to you (the game's console is not available on this server). Part C says when to go looking.

**Part A: your own character (Quickdraw 1-4, Slowdraw 1-3), at the shrine**

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| A1 | Pull to full and hold for a slow count of four, then release over open ground | Right away a thin line starts growing along the front part of the arrow (the part away from your face), orange at the back turning gold toward the tip, with a small bright spark riding its front. About two seconds after full draw it reaches the tip (it may sit full for a split second) and flares: a heavy "power strike" whoomph, a hard thump in the bow hand, a quick flash on the arrowhead. After that a faint wider gold sheath stays on the front of the arrow. The arrow as a whole does not light up while you hold. On release a short ember trail follows the arrow, and for a moment after the release the flying arrow may light up as a whole with the game's own skill glow; that brief glow is expected. Your very first draw also shows a short test line about a hand above the arrow for five seconds: tell Claude whether it fades from red at the bow end to green at the far end, or is one colour all along | no line; only a faint wide glow, no thin bright line; the line on the part near your face; the whole arrow lights up at the thump; no thump sound or buzz |
| A2 | At the shrine, pick up Holy Arrow to try it (it can be tried, not bought). Pull to full and hold for a slow count of eight, then release | As A1, then from about five seconds after full draw the line squeezes forward toward the tip and brightens; at about six and a half seconds a gold flash, both hands buzz, and the whole arrow lights up with the game's own skill glow, so you can see Holy Arrow is ready; the line stays as a small pulsing core on the head. The release fires the beam and blast | nothing changes between the thump and the count of eight; no glow and no flash when ready; an ordinary arrow comes out |
| A3 | Still trying Holy Arrow: hold to the gold flash, then ease the string forward until the bow clearly relaxes, keep it there for a slow count of two, pull back and release. Then put Holy Arrow back in the shrine. Part B must start with no orb being tried | Holy Arrow is lost after about a third of a second, as before (you do not own Brace here); the release is an ordinary arrow. Row B1 is the same with Brace | Holy Arrow survives the relax (tell Claude) |
| A4 | Snap: pull to full and let go at once. Then look at the shrine's bow orbs | The Quickdraw 4 line, as before. Two new orbs sit beside the Slowdraw column, one next to Slowdraw 1 (Brace) and one next to Slowdraw 2 (Overcharge), each with a line to it. Their icons are <the new ones you approved / a copy of the game's Slowdraw icons, expected for now>. Nothing overlaps | the line is gone; an orb is missing or sits inside another; the shrine fails to open |

**Part B: the test build, at the shrine.** Before Part B: no orb is being tried at the shrine (put any orb you picked up back). Open the server's QuickshotRing.cfg, find the `[TestBuild]` section and make it read exactly:

```
[TestBuild]
Player = "<you>"
Orbs = "Brace;HolyArrow"
Without = ""
```

For each later row change only the `Orbs` and `Without` lines as the row says. Every row starts with its Orbs/Without. When you finish for the day, set `Orbs = ""` and `Without = ""`.

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| B1 | `Orbs = "Brace;HolyArrow"`. Pull to full and hold to the gold flash (about six and a half seconds). Ease the string to about half, rest there for a slow count of five, and release from half. Next arrow: hold to the flash, ease to half, then let the string go almost slack for a blink (well under half a second) and come straight back to half; release from there | At the first thump a thin gold collar clicks onto the shaft. From about five seconds after full draw, as the line squeezes forward toward the tip, the collar slides forward with it, and the line brightens even while you are eased (bracing normally dims it). Holy Arrow stays ready all through the rest: the glow (and the pulsing core) stay on. Released from half, the full beam and blast come out at full speed. The blink loses nothing | no collar at the thump (the test build did not take: tell Claude and stop Part B); Holy Arrow is lost while resting or after the blink; the beam is slow; an ordinary arrow comes out |
| B2 | `Orbs = "Brace;Volley;FullVolley"`. Pull to full and hold until the thump (ghost arrows gather around yours as you hold). Ease the string forward to about half and hold it there for a slow count of five. Pull back to full and release over open ground. Next arrow: the same, but release while still eased to half | At the thump the gold collar snaps onto the shaft at the back of the line, with a lock click. Eased to half, the line dims and breathes slowly and the ghosts dim but keep their ring; nothing else changes and no gold ring comes back. Pulled back past two-thirds, the collar springs open with a tick; that release flies like a full-draw shot, ghosts with it. Released at half, the arrow is slower and drops sooner, its ghosts follow the same slower, droopier arc and land with it, and it still carries the charge (the ember trail) | the charge sound or line is lost while eased; the ghosts vanish while eased; the gold ring re-appears; the pulled-back release fires a Quickdraw line or a quick snap; the pulled-back release flies weak; the half release's ghosts fly the full-speed arc; the half release has no ember trail |
| B3 | Same settings. Hold to the thump, then let the string go almost completely slack and keep it there for a slow count of two. Then pull back to full with the same arrow, and after a moment ease the string forward to about half | After a little over half a second the line shrinks back from the tip and goes out within about half a second more, with a low wooden thud. That is the only "you lost it" look. Pulling back to full then starts a brand-new charge from nothing (a new line grows, a new thump later), and on that pull easing the string below about two-thirds loses it at once, with no short grace |  the line stays; it vanishes with no thud; something else plays |

**Part C: Overcharge and creatures.** At the shrine set `Orbs = "Overcharge"` and `Without = ""` once; they cover every C row (a shot released before the second thump is an ordinary charged shot). Do C1 at the shrine. Then walk out from the shrine across the open ground for up to five minutes, looking and listening; any creature that comes at you will do. On a Wyrm the interrupt is switched off on purpose: judge only the ring, the hit looks and the knock-down. If you find none in five minutes, carry on with Part D and tell Claude: the knock-down and the interrupt then wait for a way to bring a creature to you.

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| C1 | At the shrine: pull to full and keep holding to a slow count of five, then release | After the thump the line turns white-hot from its back end forward over about two seconds and embers rise from the tip. About four seconds after full draw: a spit of forge sparks, the power-strike sound again and a heavier thump. The release plays a power-strike hit sound | no white heat; no second thump; the second thump comes before the line is white |
| C2 | Hold to the second thump and hit a creature. If it falls over, hit it again with another overcharged shot as soon as you can | The first hit knocks over the kinds that can fall (the ring at its feet shatters into sparks). A second overcharged hit within about four seconds of the first fall does not knock it over again | three overcharged hits on the same creature and it never falls (tell Claude what kind it was: some kinds may not be able to fall); it is knocked over again within about four seconds of the first fall |
| C3 | Charged shots at one creature, released after the first thump but before the second (about a slow count of three), several times | Each hit: an orange burst, a power-strike flash and, on the ground, a dust ring. A gold cracked ring appears at its feet and fills as it is shaken; it fades about two seconds after your last hit. When hit, the creature's current move may restart (this is the interrupt): tell Claude whether you see an attack snap back to its beginning, and whether that looks right or broken | no ring; the ring jumps wildly; the creature slides, freezes or glitches (tell Claude: the interrupt can be switched off) |
| C4 | Snap (the line) at a creature that is about to attack | Its attack may restart from the beginning (Charge 2 and 3 make snaps interrupt). Tell Claude what you saw, in particular an animation snapping back to the start of an attack | the creature freezes or glitches |
| C5 (only if it happens) | If two creatures ever stand roughly in a line, the far one within about 8 m behind the near one, shoot a charged shot through the near one | The arrow sticks in the near one; a gold streak carries on out of its far side into the second, with a burst where it comes out; the second reacts as if hit | no streak; the second does not react |
| C6 (optional, needs a cfg edit) | `Orbs = "Volley;FullVolley"`, `Without = "Quickdraw4"`. From about 10 m, snap five times at a creature as fast as you can | Every cyan echo that touches the creature flashes on it and the creature reacts | an echo visibly strikes the creature with no flash and no reaction |

**Part D: back at the shrine, or wherever you are**

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| D1 | `Orbs = "Volley;FullVolley"`, `Without = "Quickdraw4"`. Ten snaps as fast as you can (pull to full, let go at once). Then five quick shots: let go about a quarter of a second after full draw, before the extra ghost arrows appear around yours | Snaps are normal arrows, not lines. Every one of the fifteen shots has two cyan echoes beside it (whether each echo really counted is in Claude's log: that is what this row checks) | a snap comes out as a line; any shot with no echoes or only one |

If you still have time (fine to skip):

| # | Do | What it should look, sound and feel like | Broken if |
|---|---|---|---|
| D2 | `Orbs = ""`, `Without = "Quick1;Quick2;Quick3;Quickdraw4"`. Pick a far mark and aim at the same spot every time: three snaps, then three charged shots (hold past the thump). Then `Without = "Quickdraw4"` and the same six | In the second set the snaps fly visibly flatter and land farther; the charged shots land where they did in the first set | the second set's snaps are no faster; its charged shots land clearly shorter or longer |
| D3 | `Orbs = ""`, `Without = ""`. At the shrine, pick up Brace to try it, hold to the thump, ease to half for a moment, release. Put it back. Then the same with Overcharge, holding to the second thump. Put it back | After its try, each orb shows as practised (buyable if you have a free point). Do not buy unless you want to; you have 1 free slot | an orb never shows as practised |

Finally set `Orbs = ""` and `Without = ""` in the server's `[TestBuild]`.

Untested in this session unless a second player joins: what other players see of your Fill (dimmer), your pierce streak, your ghosts, your arrow's speed, and two archers on one creature.

- [ ] **Step 5: Claude's log checklist (b)** (server and client `MelonLoader\Latest.log`; game logs in `%APPDATA%\A Township Tale\{Client,Servers\-1}\logs`)

| # | After | Expect (grep `[Archery]`) |
|---|---|---|
| 0 | every cfg change in Parts B-D | server `TEST BUILD ACTIVE: '<you>' is treated as also owning ... and as not owning ...` naming exactly the orbs set and hidden; `TEST BUILD: granted the game's ...` for orbs with a slot; never `unknown orb name(s)`; `TEST BUILD: off` after the final clear |
| 1 | start | both `QuickshotRing v0.9.0`; `shared checksum` equal on both sides; no `Settings: ... reset` lines (no default changed); `Tree: Quickdraw 4 at (...) (level 4)` (its automatic place, continuing the Quickdraw column; record the position and check it overlaps no other orb's `Tree: slot ...` position); `Tree: Brace at ...` and `Tree: Overcharge at ...` with `needs` Charge 1 and Charge 2; one `Tree: slot ...` line for each; `Icon 'Brace'`/`'Overcharge' loaded from ...` matching the icon answer; server `Weight: knock-down patches live on ... (5 of 5)` (record any fewer) and `Weight: tick is live (server)` |
| 2 | first draw (client) | `Fill: halo material ...` (or `flat` with why); `Fill: shaft radius r m, core offset o m` (r about 0.004 to 0.01); `Fill: gradient test line on a copy of ...` (record the user's A1 answer) and no test line at the next start (the done file); `Overcharge: 'Hot Effect' -> ...` (record the EmissionControl answer) |
| 3 | A1 | client `Fill: thump on '...' (charge tier 3, local) ... sound 'Power Strike Start Sounds'` and, once, `Fill: Slowdraw's charged default effect has n listener(s): ...` (record whether visuals ride with the sound); `Glow: Slowdraw hidden (the Fill shows the charge), Volley hidden, Holy Arrow shown (arrow n)` once for every charged draw (match each arrow n to its `Fill: thump`); no `Glow: Slowdraw hidden` for an arrow with no `Fill: thump` line; no `Fill: not drawn` lines (record any and their why); server `Release: ... Charged ... charge tier 3`, `Weight: arrow n carries stagger 0.6 interrupt yes pierce 1 ...`; `Steady: arrow n ... tier 3 Charged: speed a -> a m/s, wobble x0.35` on server and client (same speed before and after) |
| 4 | A2, A3 | server `Holy: armed on a live draw` about 6500 ms after the crossing; client `Fill: Holy gather on '...'` then `Fill: Holy armed core on '...'`; `Glow: ... Holy Arrow shown`; `Release: ... ult:Beam`; `Holy: fired, speed ... (full draw from strength ...)`; A3 `Draw: Holy Arrow lost ...` as before |
| 5 | B1 | `Holy: armed on a live draw` while `Brace (server): ... Braced`; no `Holy: not armed`; `Brace (local): ... Braced` stays (no `Off`) between `Holy: armed` and the release; `Brace: arrow n released at pull ...: full strength (Holy Arrow armed)`; `Release: ... ult:Beam`; `Holy: fired, speed` about 5 x a full-draw speed; the blink: `Brace (server): ... Grace` then `Braced` again, never `Lost` |
| 6 | B2, B3 | `Brace (server)` and `Brace (local)` going `Drawn` -> `Braced` -> `Drawn (open past the loose line 0.68)`; no `Draw: ... lost` while braced; the re-pulled release's server `Release:` and client `Release (local):` lines read `Charged` (not `Snap`, not `ult:Line`), with no second `Fill: thump` for that arrow; `Brace: arrow n released at pull 0.7..: full strength (past the loose line)`; the half release `kept (a slower arrow)` with the server's `Release: ... Charged ... ghosts n`; B3 `Brace (...): ... Lost` then `Draw: Slowdraw lost: braced below 0.40 for 0.6 s` and the same for the Volley skill's input, and no later `Draw: ... lost` for that arrow after a fresh dip grace |
| 7 | C1, C2 | `Release: ... Charged ... overcharged`; `Weight: arrow n carries stagger 1 ... pierce 2` (Charge 3 plus Overcharge); client `Overcharge: second thump on '...' at 37xx ms`; C2 `Weight: '...' (Type) knocked down by the game ...` then, within 4 s, `stagger 1.00 skipped: knocked down by the game x.x s ago`; record `pool full again since` on every knock-down (it answers whether the game's own trigger already blocks a second knock-down) |
| 8 | C3, C4 | once per side `Hit: tool '...' entity e -> arrow n` (record whether e differs from n); one `Hit: arrow n on '...'` per real arrow hit, with `pool before -> after` (record the values per creature kind for 4 or 5 kinds: the tuning data), and none for spectral, Line or pierce hits; with no Volley orb, `Weight: arrow n '...' stagger 0.60 in 1 part(s) -> cripple 0.300 (Spiritual xm)` about 0.2 s after its `Hit:` line (record any `x0`); every `Weight: interrupt '...' action '...' cancellable yes (or no): ...` line and each `Weight: after interrupt '...' now '...'` (record the action names and flags, and whether the same action restarts: if attacks are never cancellable, or always restart, tell the user before judging the interrupt); C4 (Line): `Weight: interrupt ...` with no `Hit:` line for the Line; no `Weight:` line names a player; client: whether `Hit (local)` appears for creatures at all, or only `Hit (embed fallback)`; `Balance: ring on '...'` and the `pool received` steps (record how coarse they are; a lone cripple of 0.100 or less may never reach the client, since the stat syncs only on a change of more than 0.1) |
| 9 | C5 | `Weight: pierce direction from ...` (once); `Weight: arrow n pierce: '...' ... -> '...' (... damage) at ...`, beside the client's `Hit (local): arrow n on '...' at ...` (compare the points: the spec asks how far apart they are) |
| 10 | C6 | server `Release: ... echoes 2` equal to the client's for every snap; the server's `Volley: arrow n x/y spectral arrows landed; k creature(s) hit ...` counts the echoes that touched the creature |
| 11 | D1 | server `Claim: timing arrow n ... -> deciding with c ms: the server saw no crossing; the client's claim is trusted` on the quickest shots; for every shot the server's `Release: ... echoes 2` equals the client's `Release (local): ... echoes 2`, and no shot whose client line has echoes and whose server line has `echoes 0`; every client `Release (local): ... Snap` is matched by a server `Release: ... Snap ult:-` for the same arrow id (no client Snap paired with a server Early), with `Steady: ... Snap: speed a -> 1.3a` on both; record every timing pair (they size Step 7's tolerance) and every `over ... ms` refusal |
| 12 | D2 | first set: no `Steady:` lines (tier 0); second set: `Steady: ... tier 3 Snap: speed a -> 1.3a` and `tier 3 Charged: speed a -> a` |
| 13 | D3 | `Shrine: '<you>' practised Brace by bracing (first time: yes)` and `... Overcharge by an overcharged shot ...` |
| 14 | anywhere | none of: `failed`, `could not be made`, `layouts disagree`, `is already used`, `OWNS TWO ULTIMATES` (except under a test build that grants a second ultimate, as B1 does); the server game log's `Error handling message` lines are only the known AudioManager ones |

If row 3 shows `Glow: Slowdraw shown (no Fill on this arrow) ... (arrow n)` for an arrow on which the user saw the Fill, row 4 shows no `Fill: Holy gather`, or rows 5 or 6 show a lost charge during a brace, stop: the two complaints this step exists for are not answered.

- [ ] **Step 6: Checkpoint**

Tests `ALL PASS (319)`; build `0 Error(s)`. Files changed in this task: `Main.cs`, `CLAUDE.md`.

---

## Deferred (not in this plan)

- Mark, its "charged hit uses up a mark" for the extra 0.4 stagger (with its setting and hook, and the spec's Step 4 test item for it), Snap Draw's window (only its loose line is honoured, through the test build), the Quick visuals: Step 5.
- Seeking Volley: Step 6. Quickdraw 4's windows, charged Lines, the Lance, the Brace re-pull window, and the claim tolerance for windows: Step 7.
- The rest of the pour (motes, the ghost fold's outline), the armed core's arcs, forks, rods, Overcharge's full-strength blast stun, the PvP blast: Step 8. Bridges: Step 9.
- The orange-only recolour of the arrowhead (the thump's head flare and Charge 3's head shell), until the ramp swap is verified; the Fill's frost-white re-skin under Quickdraw 4 and its glass-crack thump, with the windows; white-hot ghost shafts; the haptic mixer, and with it the Fill's slow haptic swell in the bow hand (spec Part 4, Charge 1); caching flight messages on spectators.
