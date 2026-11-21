# Balance logging: compile on the server (design)

Approved by the owner on 2026-11-20 ("Yes, build it").

## Intent

The owner will run the mod on a public server for weeks to see which skills are overpowered, because players take a long time to
reach some orbs. In their words: "there shouldn't be one TXT file that shows every single shot ever for the past few days. It should
all be compiled immediately so it stays relatively small", and the logging must "not actually fill the memory or RAM".

## Goals

- No per-shot file. Each shot and hit goes straight into running statistics on the server and is then forgotten.
- Files stay small however long the logging runs (target: under 100 KB a day, a few MB a month, on a busy server).
- Memory stays small: the statistics of the day, plus the shots of the last two minutes.
- The balance report shows the same page as today, all days combined.
- No cost the game's thread can feel (the standing condition on this logging).

## Non-goals

- Looking up one shot afterwards, or re-cutting old data under different rules. The owner accepted losing both.
- Deleting old statistics files. They are small and the report needs every day.

## Anti-goals

- A hitch on the server (formatting or writing on the game's thread).
- Numbers that differ from what the per-shot report gave on the same play, beyond the one stated roughness below.

## Approaches

1. **Compile later from rows (0.19.4).** The server writes a row per shot and hit; the report compiles as it reads. Proven, but the
   row files stay on disk (1.1 GB for a heavy month). Ruled out by the owner.
2. **Compile once a day from rows.** Rows only live a day. Still a per-shot file, and a second compiler in C#. Ruled out.
3. **Compile as it happens (chosen).** The server adds each shot and hit to a small table in memory and rewrites one small
   statistics file about once a minute. Nothing per shot is ever written.

## The design

- **What a line is:** one line per local day, per player, per kind of shot. "Kind of shot" is the columns the report already uses to
  name a skill and a build (kind, ultimate, ghosts, pellets, needles, tier, hold stage, locks, Skyfall code, main path, the build's
  orb counts and side orbs, the test-build flag, the mod version and the shared-settings check). The report keeps naming the skill
  from those columns, so there is still one definition of "which skill was this".
- **What a line holds:** shots, time spent (the gap since that archer's previous shot, at most 15 s, as today), damage to creatures,
  kills, damage to players and player kills, held time, and damage by what dealt it.
- **Median and best 10%:** per kind of shot (not per player) a tally of how much single shots did, in steps of 0.5 damage, with
  misses counted exactly. This is the one number that gets rougher: accurate to about a quarter point.
- **Timeline:** damage per hour.
- **Late hits:** a hit names its shot, and hits arrive up to 120 s after the release. A shot's totals are added the moment a hit
  lands; only its own damage sum waits in memory until 125 s after the release, then goes into the tally and is dropped.
- **The file:** one small text file per server run per day, rewritten whole about once a minute (written beside and moved over, so a
  reader never sees half a file), and once more when the day ends or the server stops. The work on the game's thread is a copy of
  the table's numbers; formatting and writing happen on the background thread, as the row writer does today.
- **The row files stop.** `[Telemetry] KeepRows` (off) turns them back on for troubleshooting; the report can compare the two.
- **The low-disk stop** (0.19.4) covers the statistics file as well.
- **The report** reads the statistics files when the folder has them, and the row files otherwise.

## Left to the builder

The exact file layout, class and method names, and how the sample and stress generator feed both writers so the report can compare
them.
