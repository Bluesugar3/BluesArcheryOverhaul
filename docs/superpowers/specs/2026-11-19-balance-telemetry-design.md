# Balance telemetry (QuickshotRing) — short design

## Intent

The owner: "Have it keep logs of all arrows shot, skills used and damage delt, so after a day of playtest we can take all that
information and graph it to visualize if some skills are to op". So: the dedicated server records every bow release and every
bit of archery damage for a whole play-test day, and a script turns a day of it into charts that make an over-strong skill stand out.

- Goals: every release the server decides (Skyfall casts included); every hit attributed to its shot and to the part of the skill
  that dealt it; real damage (what came off the health bar) and kills; cheap enough to leave on all day.
- Non-goals: melee, creature attacks, fall damage; anything on clients; live dashboards.
- Anti-goals: a stall or exception on the shot/hit path; new network traffic; a shared setting (friends must not need it).

## Approaches

1. **Chosen: one choke point plus named scopes.** A prefix/postfix on `HealthObject.ReceiveDamage` (the game's single place where
   health comes off; `PlayerHealth` reaches it through `base`, after PvP, invincibility and its multipliers) reads health before
   and after: damage = what really came off (armour, protection and the health left all counted), a kill = health crossing 0. The
   mod's own damage call sites open a one-line `using (Telemetry.As(source, arrowId))` scope so the hook knows which part dealt
   it; without a scope, a hit from an arrow's tool is the real arrow (the game's own hit, RevampedSkills' too), from another
   projectile's tool the Holy beam; anything else is not archery and is ignored.
2. Record at each mod dealing site only: misses the real arrow's own game hit (most of most shots' damage) and logs damage asked,
   not dealt.
3. Parse `Latest.log` Debug lines: needs Debug all day (0.2-0.35 ms a line), fragile, and still misses the game's own hits.

## Design

- **Files** (server only): `UserData\QuickshotRing\telemetry\shots-v1-YYYY-MM-DD.csv` and `hits-v1-YYYY-MM-DD.csv`, one pair
  per server-local day (the `v1` changes if the columns ever change, so a day's file never mixes layouts). Append only; a file
  that reaches 256 MB stops growing with one warning. Opened in Excel too.
- **shots**: `time, session, shot, arrow, player, account, kind, ult, ghosts, pellets, echoes, tier, hold, ms, locks, sky, main,
  quick, volley, charge, ults, ricochet, shatter, gatherer, test` — written from `Draws.Release`'s server branch (the decided
  resolution, the time it decided with, the build, the server's kept Seeking Volley locks, the Skyfall code).
- **hits**: `time, session, shot, player, source, target, target_id, victim, raw, dealt, cripple, killed, max_health` — `source`
  is one of arrow, ghost, pellet, needle, core (Seeking Volley's core cluster), line (Quickdraw 4), ricochet, shatter, holybeam,
  holyblast, weight (stagger only), skyfall, mod (an unscoped hand-made hit: a bug marker). A batched spectral hit is split
  back into ghost/pellet/needle/core rows by each kind's share of the batch. `shot` joins to the shots file within a
  `session` (server start stamp); 0 = no recorded shot.
- **Cost**: the hooks only append a small struct to a list (no strings, no IO, everything inside try/catch); a flush every 5 s from
  `Main.OnUpdate` (before the headless return) formats and appends, retrying a failed write next time (queue capped). Timing
  sections on the patch and the flush. `[Telemetry] Enabled` (server, not shared, default on).
- **Edits outside the new files** (one to three lines each): `Draws.cs` (the shot), `Volley.cs` (pass the spectral kind),
  `Hits.cs` (per-kind shares in a batch, the spectral scope), `HolyArrow.cs` (blast scope), `Quickdraw4.cs` (line, needles),
  `Ricochet.cs`, `Shatter.cs`, `Weight.cs`, `SkyfallServer.cs` (scopes), `Main.cs` (init, flush, quit flush), `CLAUDE.md` (a row).
- **Unity-free part** (`TelemetryRules.cs`, tested): the row formats, CSV quoting, file names, the spectral split.
- **Graph**: `python -I tools/telemetry_report.py [folder|files] [--day YYYY-MM-DD]` writes one self-contained HTML page
  (matplotlib charts embedded) and prints a short summary: per skill (Plain, Quickdraw 1-3, Quickdraw 4, Volley, Burst, Seeking
  Volley, Charge Shot 1-3, Holy Arrow, Skyfall) shots, damage per shot (mean, median, p90), sustained damage per minute (damage over
  the time since that player's previous shot, gaps capped at 15 s, so Auto Dock's and Quickdraw's pace count), kills and kills per
  100 shots, damage by source, x-times-the-median flags; per player x skill and per main path tables; a damage-per-minute timeline.
  Creature damage is the balance measure; damage to players is its own column; Skyfall is shown but kept out of the comparisons
  (an admin toy, "no need to balance").

## Performance (the owner's condition for approval, 2026-11-19: "make sure it dosent cause preformance issues, then approved")

- **Off** (`[Telemetry] Enabled = false`, and always on clients): one cached bool test per damage event and per shot.
- **The hit hook** runs for every damage event on the server, archery or not. A non-archery hit costs a type check and a scope
  read, plus one `GetComponentInParent` for a tool hit. An archery hit appends one struct to a list; names are taken as strings
  the game already holds (a creature's root name is read once per hit). No formatting, no file access, no locks, and every
  path is inside try/catch.
- **The disk is never touched on the game's thread.** Every 5 s the main thread swaps the queued rows for an empty list (a
  pointer swap) and hands them to one background worker, which formats and appends them. While a write is still running the
  rows wait for the next swap. A failed write (a OneDrive lock) is retried. The queue is capped at 100 000 rows (about 15 MB)
  and drops the newest beyond that, with one warning.
- **Size:** about 10-40 MB a day of heavy play; a file that reaches 256 MB stops growing, with one warning.
- **Checked:** the test runner times formatting and writing 100 000 rows. The hook, the shot record and the swap get timing
  sections, so `[Timing] Enabled = true` on the server during the play-test reports their real cost in game. The target is a
  few microseconds per hit and no frame spike. Neither has been measured in play yet.

## Left to the builder

Column order and names, chart styling, the skill-label rules in the script (derived from the raw columns, so a regrouping never
needs a new mod build).
