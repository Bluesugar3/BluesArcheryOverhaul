"""Balance report for Blue's Archery Overhaul's telemetry: one self-contained HTML page of charts, compiled from every day logged.

The dedicated server compiles every shot and hit into UserData\\BluesArcheryOverhaul\\telemetry\\stats-v1-<day>-<session>.csv, one
small file per server run per day (Telemetry.cs, TelemetryStats.cs, since 0.19.5); up to 0.19.4, and with [Telemetry] KeepRows, it
writes one row per shot and hit to shots-v1-<day>.csv and hits-v1-<day>.csv (up to 0.18.3 the folder was QuickshotRing\\telemetry,
which stays where it was: pass it to read those days). This reads them and draws, per skill: shots, damage per shot, sustained damage
per minute, kills, where the damage comes from, and the same per player and per main path, so a skill that is too strong stands out.

    python -I tools/telemetry_report.py                     # every day in the server's folder, compiled into one page
    python -I tools/telemetry_report.py --day 2026-11-19     # that day only
    python -I tools/telemetry_report.py <folder or files>    # other data (a copy, a friend's server)
    python -I tools/telemetry_report.py --rows               # the row files even where statistics exist
    python -I tools/telemetry_report.py --compare            # both kinds of the same play: exit 1 where they differ
    python -I tools/telemetry_report.py --selftest           # checks the numbers on a tiny made-up day

A folder with statistics files is read from them only (a day with only row files is left out, and the report says so); --rows reads
the row files only. Stdlib only. "Damage" is what really came off creatures' health (armour, protection and overkill already out);
damage to players is its own column; Skyfall (the admin skill) is shown but kept out of every comparison. From statistics the median
and best 10% are accurate to a quarter point and the timeline is in hours.

The rows are compiled as they are read and none is kept, so weeks of a busy server are fine (measured on 30 days of 100,000 shots
a day, 10 million rows: see CLAUDE.md).
"""
import argparse
import array
import csv
import datetime as dt
import glob
import html
import itertools
import os
import re
import statistics
import sys
import tempfile
import webbrowser

SERVER_DATA = r"C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\UserData\BluesArcheryOverhaul\telemetry"
SHOT_COLUMNS = ("time,session,shot,arrow,player,account,kind,ult,ghosts,pellets,needles,tier,hold,ms,locks,sky,main,quick,volley,"
                "charge,ults,ricochet,shatter,gatherer,test,version,settings").split(",")
HIT_COLUMNS = "time,session,shot,player,source,target,target_id,victim,raw,dealt,cripple,killed,max_health".split(",")
SKYFALL = ("Skyfall", "Skyfall (fizzled)")
FLAG_AT = 1.5          # a skill at this many times the median damage per minute is flagged
BIN_MINUTES = 5        # the timeline's columns for a day; a longer stretch gets wider ones (BIN_CHOICES)
BIN_CHOICES = (5, 15, 30, 60, 180, 360, 720, 1440)
MAX_COLUMNS = 300      # the most columns the timeline draws (a day in 5-minute steps is 288)

# where damage comes from, folded to eight groups (the chart palette has eight hues; anything else is "Other")
SOURCE_GROUPS = [
    ("Real arrow", ("arrow",)),
    ("Ghosts", ("ghost", "core")),
    ("Pellets", ("pellet",)),
    ("Needles", ("needle",)),
    ("Quickdraw 4 line", ("line",)),
    ("Ricochet", ("ricochet",)),
    ("Shatter", ("shatter",)),
    ("Holy Arrow", ("holybeam", "holyblast")),
]
GROUP_OF = {src: name for name, srcs in SOURCE_GROUPS for src in srcs}


# ------------------------------------------------------------------------------------------------------------------- reading

def find_files(paths):
    """Every shots/hits file under the given folders, plus any row files named directly."""
    found = []
    for p in paths:
        if os.path.isdir(p):
            found += glob.glob(os.path.join(p, "shots-v1-*.csv")) + glob.glob(os.path.join(p, "hits-v1-*.csv"))
        elif os.path.isfile(p) and not is_stats(p):
            found.append(p)
    return sorted(set(found), key=written_order)


def is_stats(path):
    return os.path.basename(path).startswith("stats-")


def find_stats(paths):
    """Every statistics file (stats-v1-<day>-<session>.csv, one per server run per day) under the given folders, plus any named directly."""
    found = []
    for p in paths:
        if os.path.isdir(p):
            found += glob.glob(os.path.join(p, "stats-v1-*.csv"))
        elif os.path.isfile(p) and is_stats(p):
            found.append(p)
    return sorted(set(found))


def stats_day(path):
    m = re.match(r"stats-v1-(\d{4}-\d{2}-\d{2})-", os.path.basename(path))
    return m.group(1) if m else ""


def read_stats_file(path):
    """(head, records): the file's own header as name -> value, and each record as (type, {column: value}) by its type's header row."""
    head, columns, records = {}, {}, []
    with open(path, newline="", encoding="utf-8-sig") as f:
        for rec in csv.reader(f):
            if not rec:
                continue
            kind = rec[0]
            if kind == "#":
                head["layout"] = rec[2] if len(rec) > 2 else ""
                head.update(zip(rec[3::2], rec[4::2]))
            elif kind.startswith("#"):
                columns[kind[1:]] = rec[1:]
            elif kind in columns and len(rec) == len(columns[kind]) + 1:
                records.append((kind, dict(zip(columns[kind], rec[1:]))))
            else:
                raise SystemExit(f"{path}: a {kind!r} line that does not fit its header (a newer or older layout?)")
    if head.get("layout") != "v1":
        raise SystemExit(f"{path}: not a v1 statistics file")
    return head, records


def written_order(path):
    """Files in the order the server wrote them: by day, and a full day's parts after its first file (-2, -3, ... -10)."""
    name = os.path.basename(path)
    m = re.search(r"-v1-(\d{4}-\d{2}-\d{2})(?:-(\d+))?\.csv$", name)
    return (name.split("-v1-")[0], m.group(1), int(m.group(2) or 1), path) if m else (name, "", 0, path)


def day_of(path):
    """The day in a file's name; a full day goes on in parts ("hits-v1-2026-11-19-2.csv")."""
    m = re.search(r"-v1-(\d{4}-\d{2}-\d{2})(?:-\d+)?\.csv$", os.path.basename(path))
    return m.group(1) if m else ""


def read_rows(path, columns):
    """One file's rows, one at a time: none is kept."""
    with open(path, newline="", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        missing = [c for c in columns if c not in (reader.fieldnames or [])]
        if missing:
            raise SystemExit(f"{path}: missing columns {missing} (a newer or older layout?)")
        yield from reader


def read_day(paths, day=None, all_days=False):
    """(shots, hits, days): the rows of one day (the newest unless given) or of every day, each read as it is asked for."""
    files = find_files(paths)
    days = sorted({day_of(f) for f in files})
    if not days:
        raise SystemExit(f"no telemetry files (shots-v1-*.csv, hits-v1-*.csv) in {paths}")
    if not all_days:
        want = day or days[-1]
        if want not in days:
            raise SystemExit(f"no data for {want}; days found: {', '.join(days)}")
        days = [want]
    chosen = [f for f in files if day_of(f) in days]
    shots = (row for f in chosen if os.path.basename(f).startswith("shots-") for row in read_rows(f, SHOT_COLUMNS))
    hits = (row for f in chosen if not os.path.basename(f).startswith("shots-") for row in read_rows(f, HIT_COLUMNS))
    return shots, hits, days


def num(v, default=0.0):
    try:
        return float(v) if v not in ("", None) else default
    except ValueError:
        return default


def when(v):
    try:
        return dt.datetime.fromisoformat(v)   # the server's own format, several times faster than strptime
    except ValueError:
        return dt.datetime.strptime(v, "%Y-%m-%d %H:%M:%S.%f")


# ------------------------------------------------------------------------------------------------------------------- skills

def skill_of(s):
    """The skill a shot used, from the raw columns (the report's only interpretation of them)."""
    kind, ult, sky, main = s["kind"], s["ult"], s["sky"], s["main"]
    ghosts, pellets, locks = int(num(s["ghosts"])), int(num(s["pellets"])), int(num(s["locks"]))
    tier, quick = int(num(s["tier"])), int(num(s["quick"]))
    if sky == "Skyfall":
        return "Skyfall"
    if sky == "Fizzled":
        return "Skyfall (fizzled)"
    if ult == "Beam":
        return "Holy Arrow"
    if ult == "Line":
        return "Quickdraw 4"
    if kind == "Snap":
        return f"Quickdraw {quick}" if quick > 0 else "Quickdraw"
    heavy = kind == "Charged" and main == "Charge"
    if locks > 0 and ghosts > 0 and kind in ("Held", "Charged") and not heavy:
        return "Seeking Volley"
    if pellets > 0:
        return "Burst"
    if ghosts > 0 and heavy:
        return f"Charge Shot {tier} + heavy"
    if ghosts > 0:
        return "Two-Shot" if ghosts <= 5 else "Three-Shot"
    if kind == "Charged":
        return f"Charge Shot {tier}"
    return "Plain"


# ------------------------------------------------------------------------------------------------------------------- the numbers

def p90(values):
    """Nearest-rank 90th percentile (a good shot, not the luckiest one)."""
    if not values:
        return 0.0
    v = sorted(values)
    return v[max(0, -(-9 * len(v) // 10) - 1)]


def analyze(shots, hits, gap_cap=15.0, min_shots=20, include_test=False, version=None):
    """Everything the page and the summary show, compiled while the rows are read: no row is kept, only four numbers per shot (a hit
    names its shot, and the median needs each shot's damage), so weeks of a busy server fit in memory. Give it the shots first, then
    the hits. A shot's cost is the time since that player's previous shot in the same session, at most gap_cap (a player's first shot
    costs gap_cap), so damage per minute counts a skill's pace: a long hold costs its time, Auto Dock's and Quickdraw's speed pay off."""
    # ponytail: a session's shots are taken in file order, which is the order the server wrote them (time order); a row that is out of
    # order costs 0 s instead of being sorted into place. Sort each lane by time again if files of several servers are ever merged.
    groups = [name for name, _ in SOURCE_GROUPS] + ["Other"]
    G = len(groups)
    group_at = {g: i for i, g in enumerate(groups)}
    SKIP = -1
    slots = {}                               # session -> array: shot number -> that shot's index + 1, SKIP (left out), 0 (no such shot)
    keys, key_at = [], {}                    # the kinds of shot seen: (skill, player, main)
    kind, dealt, cost, pvp = array.array("i"), array.array("d"), array.array("d"), array.array("d")   # per shot
    k_kills, k_pvp_kills, k_held, k_held_n, k_sources = [], [], [], [], array.array("d")              # per kind of shot
    last = {}                                # (session, archer) -> when that archer last shot
    test_shots, tunings, sessions, archers = 0, {}, set(), set()
    for row in shots:
        session = row["session"]
        n = int(num(row["shot"]))
        slot = slots.get(session)
        if slot is None:
            slot = slots[session] = array.array("i")
        if n >= len(slot):
            slot.extend(itertools.repeat(0, n + 1 - len(slot)))
        left_out = bool(version) and row["version"] != version
        if str(row["test"]) == "1" and not include_test:   # a test build's borrowed orbs: not what players own
            test_shots += 1
            left_out = True
        if left_out:
            if n > 0:
                slot[n] = SKIP
            continue
        tune = (row["version"], row["settings"])
        tunings[tune] = tunings.get(tune, 0) + 1
        t = when(row["time"])
        lane = (session, row["account"] if row["account"] not in ("", "0") else row["player"])
        prev = last.get(lane)
        cost.append(gap_cap if prev is None else min(max((t - prev).total_seconds(), 0.0), gap_cap))
        if prev is None or t > prev:
            last[lane] = t
        key = (skill_of(row), row["player"], row["main"])
        at = key_at.get(key)
        if at is None:
            at = key_at[key] = len(keys)
            keys.append(key)
            k_kills.append(0)
            k_pvp_kills.append(0)
            k_held.append(0.0)
            k_held_n.append(0)
            k_sources.extend(itertools.repeat(0.0, G))
        if n > 0:
            slot[n] = len(kind) + 1
        kind.append(at)
        dealt.append(0.0)
        pvp.append(0.0)
        if row["ms"] != "":
            k_held[at] += num(row["ms"]) / 1000.0
            k_held_n[at] += 1
        sessions.add(session)
        archers.add(row["player"])

    skyfall = [key[0] in SKYFALL for key in keys]
    unattributed, things, mods, bins, hit_rows = {"hits": 0, "damage": 0.0}, 0, 0, {}, 0
    for h in hits:
        hit_rows += 1
        n = int(num(h["shot"]))
        slot = slots.get(h["session"])
        ref = slot[n] if slot is not None and 0 < n < len(slot) else 0
        if ref == SKIP:
            continue
        amount, killed, victim = num(h["dealt"]), str(h["killed"]) == "1", h["victim"]
        if h["source"] == "mod":
            mods += 1
        if victim == "thing":
            things += 1
            continue
        i = ref - 1
        if victim == "player":
            if ref:
                pvp[i] += amount
                k_pvp_kills[kind[i]] += killed
            continue
        if not ref:
            unattributed["hits"] += 1
            unattributed["damage"] += amount
        else:
            dealt[i] += amount
            k_kills[kind[i]] += killed
            k_sources[kind[i] * G + group_at[GROUP_OF.get(h["source"], "Other")]] += amount
        if not ref or not skyfall[kind[i]]:
            v = h["time"]                    # "2026-11-19 18:07:31.250" -> its 5-minute step "2026-11-19 18:05"
            try:
                minute = int(v[14:16])
            except ValueError:
                v = when(v).strftime("%Y-%m-%d %H:%M")
                minute = int(v[14:16])
            step = v[:14] + "%02d" % (minute - minute % BIN_MINUTES)
            bins[step] = bins.get(step, 0.0) + amount

    def table_row():
        return {"shots": 0, "players": set(), "damage": 0.0, "per_shot": array.array("d"), "cost": 0.0, "kills": 0,
                "held_sum": 0.0, "held_n": 0, "pvp_damage": 0.0, "pvp_kills": 0, "sources": {}}

    skills, players, mains = {}, {}, {}
    into = []                                # per kind of shot: the rows of the three tables it counts in
    for at, (skill, player, main) in enumerate(keys):
        rows = [skills.setdefault(skill, table_row()), players.setdefault((player, skill), table_row())]
        if skill not in SKYFALL:
            rows.append(mains.setdefault(main, table_row()))
        into.append(rows)
        for k in rows:
            k["players"].add(player)
            k["kills"] += k_kills[at]
            k["pvp_kills"] += k_pvp_kills[at]
            k["held_sum"] += k_held[at]
            k["held_n"] += k_held_n[at]
            for g in range(G):
                v = k_sources[at * G + g]
                if v:
                    k["sources"][groups[g]] = k["sources"].get(groups[g], 0.0) + v
    for i in range(len(kind)):               # in the order shot
        d, c, p = dealt[i], cost[i], pvp[i]
        for k in into[kind[i]]:
            k["shots"] += 1
            k["damage"] += d
            k["per_shot"].append(d)
            k["cost"] += c
            k["pvp_damage"] += p

    for table in (skills, players, mains):
        for k in table.values():
            k["players"] = len(k["players"])
            k["per_shot_mean"] = statistics.fmean(k["per_shot"])
            k["per_shot_median"] = statistics.median(k["per_shot"])
            k["per_shot_p90"] = p90(k["per_shot"])
            k["dpm"] = 60.0 * k["damage"] / k["cost"] if k["cost"] > 0 else 0.0
            k["kills_per_100"] = 100.0 * k["kills"] / k["shots"]
            k["held_mean"] = k["held_sum"] / k["held_n"] if k["held_n"] else None
            del k["per_shot"]                # the three numbers above are all the page uses

    eligible = [k["dpm"] for name, k in skills.items() if name not in SKYFALL and k["shots"] >= min_shots]
    median = statistics.median(eligible) if len(eligible) >= 2 else None
    for name, k in skills.items():
        k["x_median"] = k["dpm"] / median if median and name not in SKYFALL else None
        k["flag"] = k["x_median"] is not None and k["x_median"] >= FLAG_AT

    # the timeline: 5-minute steps for a day, wider ones for a longer stretch so it stays at most MAX_COLUMNS columns
    timeline, step_minutes = [], BIN_MINUTES
    if bins:
        steps = {dt.datetime.strptime(k, "%Y-%m-%d %H:%M"): v for k, v in bins.items()}
        span = (max(steps) - min(steps)).total_seconds() / 60.0 + BIN_MINUTES
        step_minutes = next((m for m in BIN_CHOICES if span / m <= MAX_COLUMNS), BIN_CHOICES[-1])
        if step_minutes != BIN_MINUTES:
            wide = {}
            for t in sorted(steps):
                minute = t.hour * 60 + t.minute
                at = t.replace(hour=0, minute=0) + dt.timedelta(minutes=minute - minute % step_minutes)
                wide[at] = wide.get(at, 0.0) + steps[t]
            steps = wide
        b, end = min(steps), max(steps)
        while b <= end:
            timeline.append((b, steps.get(b, 0.0) / step_minutes))
            b += dt.timedelta(minutes=step_minutes)
    return {"skills": skills, "players": players, "mains": mains, "median_dpm": median, "timeline": timeline, "step_minutes": step_minutes,
            "unattributed": unattributed, "things": things, "test_shots": test_shots, "tunings": tunings, "mods": mods,
            "shots": len(kind), "hits": hit_rows, "gap_cap": gap_cap, "min_shots": min_shots,
            "sessions": sorted(sessions), "archers": sorted(archers)}


def tally_rank(tally, k):
    """The k-th smallest (0-based) value of a tally [(value, count)] sorted by value."""
    seen = 0
    for v, c in tally:
        seen += c
        if k < seen:
            return v
    return tally[-1][0] if tally else 0.0


def read_stats(paths, day=None, min_shots=20, include_test=False, version=None):
    """What analyze gives, from the statistics files the server compiles (TelemetryStats.cs) instead of the row files: (r, days).
    Every S line counts as its shots did in analyze, every T line is an hour of the timeline, the M lines are the hit counts. A skill's
    median and best 10% come from its D lines added together, each step counted as its middle (accurate to a quarter point); per player
    and per main path they are the average (the page does not show them there). Statistics lose these against the rows: --version and
    --include-test cannot move a hit with no shot, or a test build's hits on things, into or out of the timeline and the hit counts."""
    files = find_stats(paths)
    days = sorted({stats_day(f) for f in files})
    if not days:
        raise SystemExit(f"no statistics files (stats-v1-*.csv) in {paths}")
    if day:
        if day not in days:
            raise SystemExit(f"no data for {day}; days found: {', '.join(days)}")
        days = [day]
    groups = [name for name, _ in SOURCE_GROUPS] + ["Other"]

    def table_row():
        return {"shots": 0, "players": set(), "damage": 0.0, "cost": 0.0, "kills": 0, "held_sum": 0.0, "held_n": 0,
                "pvp_damage": 0.0, "pvp_kills": 0, "sources": {}}

    skills, players, mains, tallies = {}, {}, {}, {}
    hours, tunings, sessions, archers = {}, {}, set(), set()
    unattributed, things, mods, hit_rows, test_shots, shots = {"hits": 0, "damage": 0.0}, 0, 0, 0, 0, 0
    gap_caps = set()
    for path in files:
        if stats_day(path) not in days:
            continue
        head, records = read_stats_file(path)
        gap_caps.add(num(head.get("gap_cap_s"), 15.0))
        for kind, line in records:
            if kind in ("S", "D"):
                left_out = bool(version) and line["version"] != version
                if str(line["test"]) == "1" and not include_test:   # a test build's borrowed orbs: not what players own
                    if kind == "S":
                        test_shots += int(num(line["shots"]))
                    left_out = True
                if left_out:
                    continue
                skill = skill_of(line)
                if kind == "D":
                    t = tallies.setdefault(skill, {})
                    zero = int(num(line["zero"]))
                    if zero:
                        t[0.0] = t.get(0.0, 0) + zero
                    for part in filter(None, line["steps"].split(";")):
                        step, count = part.split(":")
                        v = (int(step) + 0.5) * num(head.get("step"), 0.5)
                        t[v] = t.get(v, 0) + int(count)
                    continue
                n = int(num(line["shots"]))
                shots += n
                tune = (line["version"], line["settings"])
                tunings[tune] = tunings.get(tune, 0) + n
                sessions.add(head.get("session", path))
                player = line["player"]
                archers.add(player)
                rows = [skills.setdefault(skill, table_row()), players.setdefault((player, skill), table_row())]
                if skill not in SKYFALL:
                    rows.append(mains.setdefault(line["main"], table_row()))
                for k in rows:
                    k["shots"] += n
                    k["players"].add(player)
                    k["damage"] += num(line["damage"])
                    k["cost"] += num(line["cost_s"])
                    k["kills"] += int(num(line["kills"]))
                    k["pvp_damage"] += num(line["pvp_damage"])
                    k["pvp_kills"] += int(num(line["pvp_kills"]))
                    k["held_sum"] += num(line["held_ms"]) / 1000.0
                    k["held_n"] += int(num(line["held_n"]))
                    for column, value in line.items():
                        if column.startswith("by_") and value not in ("", "0"):
                            g = GROUP_OF.get(column[3:], "Other")
                            k["sources"][g] = k["sources"].get(g, 0.0) + num(value)
            elif kind == "T":
                if version and line["version"] != version:
                    continue
                at = dt.datetime.strptime(line["day"], "%Y-%m-%d") + dt.timedelta(hours=int(num(line["hour"])))
                hours[at] = hours.get(at, 0.0) + num(line["damage"])
            elif kind == "M":
                hit_rows += int(num(line["hits"]))
                unattributed["hits"] += int(num(line["unattributed_hits"]))
                unattributed["damage"] += num(line["unattributed_damage"])
                if version and line["version"] != version:
                    continue
                things += int(num(line["things"]))
                mods += int(num(line["mods"]))

    for name, k in skills.items():
        t = sorted(tallies.get(name, {}).items())
        n = sum(c for _, c in t)
        if n:
            mid = n // 2
            k["per_shot_median"] = tally_rank(t, mid) if n % 2 else (tally_rank(t, mid - 1) + tally_rank(t, mid)) / 2
            k["per_shot_p90"] = tally_rank(t, max(0, -(-9 * n // 10) - 1))
        else:
            k["per_shot_median"] = k["per_shot_p90"] = 0.0
    for table in (skills, players, mains):
        for name, k in table.items():
            k["players"] = len(k["players"])
            k["per_shot_mean"] = k["damage"] / k["shots"] if k["shots"] else 0.0
            if table is not skills:
                k["per_shot_median"] = k["per_shot_p90"] = k["per_shot_mean"]
            k["dpm"] = 60.0 * k["damage"] / k["cost"] if k["cost"] > 0 else 0.0
            k["kills_per_100"] = 100.0 * k["kills"] / k["shots"] if k["shots"] else 0.0
            k["held_mean"] = k["held_sum"] / k["held_n"] if k["held_n"] else None

    eligible = [k["dpm"] for name, k in skills.items() if name not in SKYFALL and k["shots"] >= min_shots]
    median = statistics.median(eligible) if len(eligible) >= 2 else None
    for name, k in skills.items():
        k["x_median"] = k["dpm"] / median if median and name not in SKYFALL else None
        k["flag"] = k["x_median"] is not None and k["x_median"] >= FLAG_AT

    # the timeline: hours, wider steps for a longer stretch so it stays at most MAX_COLUMNS columns
    timeline, step_minutes = [], 60
    if hours:
        span = (max(hours) - min(hours)).total_seconds() / 60.0 + 60
        step_minutes = next((m for m in HOUR_CHOICES if span / m <= MAX_COLUMNS), HOUR_CHOICES[-1])
        steps = {}
        for t in sorted(hours):
            minute = t.hour * 60
            at = t.replace(hour=0, minute=0) + dt.timedelta(minutes=minute - minute % step_minutes)
            steps[at] = steps.get(at, 0.0) + hours[t]
        b, end = min(steps), max(steps)
        while b <= end:
            timeline.append((b, steps.get(b, 0.0) / step_minutes))
            b += dt.timedelta(minutes=step_minutes)
    return ({"skills": skills, "players": players, "mains": mains, "median_dpm": median, "timeline": timeline, "step_minutes": step_minutes,
             "unattributed": unattributed, "things": things, "test_shots": test_shots, "tunings": tunings, "mods": mods,
             "shots": shots, "hits": hit_rows, "gap_cap": max(gap_caps) if gap_caps else 15.0, "min_shots": min_shots,
             "sessions": sorted(sessions), "archers": sorted(archers)}, days)


HOUR_CHOICES = (60, 180, 360, 720, 1440)


def load(paths, rows=False, day=None, gap_cap=15.0, min_shots=20, include_test=False, version=None):
    """(r, days, kind): the statistics when the paths hold any and rows is False, else the row files. Only one kind is ever read, so a
    folder that holds both (KeepRows on, or a server updated past 0.19.4) never counts a day twice."""
    if not rows and find_stats(paths):
        r, days = read_stats(paths, day, min_shots, include_test, version)
        return r, days, "stats"
    shots, hits, days = read_day(paths, day, not day)
    return analyze(shots, hits, gap_cap, min_shots, include_test, version), days, "rows"


def compare(a, b):
    """The differences between the result from the row files (a) and from the statistics (b) of the same play: any count, any sum by
    more than 0.1% and 0.01, a skill's median or best 10% by more than 0.25 more than that (the statistics' quarter point; the row files
    also round each hit to 3 decimals and each time to a millisecond). The timelines are compared by their total (rows in 5-minute steps,
    statistics in hours)."""
    out = []

    def tol(x, y):
        return max(0.001 * max(abs(x), abs(y)), 0.01)

    def same(where, x, y, extra=0.0):
        if x is None or y is None:
            if x != y:
                out.append(f"{where}: rows {x}, statistics {y}")
        elif isinstance(x, int) and isinstance(y, int):
            if x != y:
                out.append(f"{where}: rows {x}, statistics {y}")
        elif abs(x - y) > tol(x, y) + extra:
            out.append(f"{where}: rows {x:.4f}, statistics {y:.4f}")

    for name in ("shots", "hits", "things", "mods", "test_shots"):
        same(name, a[name], b[name])
    same("hits with no shot", a["unattributed"]["hits"], b["unattributed"]["hits"])
    same("damage with no shot", a["unattributed"]["damage"], b["unattributed"]["damage"])
    same("median damage per minute", a["median_dpm"], b["median_dpm"])
    same("timeline total", sum(v for _, v in a["timeline"]) * a["step_minutes"], sum(v for _, v in b["timeline"]) * b["step_minutes"])
    for name in ("sessions", "archers", "tunings"):
        if a[name] != b[name]:
            out.append(f"{name}: rows {a[name]}, statistics {b[name]}")
    for table in ("skills", "players", "mains"):
        ta, tb = a[table], b[table]
        for key in sorted(set(ta) | set(tb), key=str):
            if key not in ta or key not in tb:
                out.append(f"{table} {key}: only in the {'statistics' if key in tb else 'rows'}")
                continue
            x, y = ta[key], tb[key]
            for f in ("shots", "players", "kills", "pvp_kills", "held_n"):
                same(f"{table} {key} {f}", x[f], y[f])
            for f in ("damage", "cost", "pvp_damage", "held_sum", "dpm", "per_shot_mean", "kills_per_100"):
                same(f"{table} {key} {f}", x[f], y[f])
            for g in sorted(set(x["sources"]) | set(y["sources"])):
                same(f"{table} {key} source {g}", x["sources"].get(g, 0.0), y["sources"].get(g, 0.0))
            if table == "skills":
                same(f"skill {key} median", x["per_shot_median"], y["per_shot_median"], 0.25)
                same(f"skill {key} best 10%", x["per_shot_p90"], y["per_shot_p90"], 0.25)
    return out


def ranked(skills):
    """Skills by damage per minute, Skyfall last."""
    return sorted(skills.items(), key=lambda kv: (kv[0] in SKYFALL, -kv[1]["dpm"], kv[0]))


# ------------------------------------------------------------------------------------------------------------------- the page

def esc(s):
    return html.escape(str(s), quote=True)


def fmt(v, digits=1):
    if v is None:
        return "–"
    if abs(v) >= 100:
        return f"{v:,.0f}"
    s = f"{v:.{digits}f}".rstrip("0").rstrip(".")
    return "0" if s in ("", "-0") else s


def ticks(vmax, n=4):
    """Clean axis ticks from 0 past vmax."""
    if not vmax or vmax <= 0:
        return [0.0, 1.0]
    raw = vmax / n
    mag = 10 ** int(f"{raw:e}".split("e")[1])
    step = next(m * mag for m in (1, 2, 2.5, 5, 10) if m * mag >= raw)
    top = step * -(-vmax // step)
    return [i * step for i in range(int(round(top / step)) + 1)]


def tick_text(v):
    return f"{v / 1000:g}K" if v >= 10000 else f"{v:,.0f}" if v >= 10 or v == int(v) else f"{v:g}"


W, LABEL_W, RIGHT_W, ROW_H, BAR_H = 760, 170, 150, 30, 18


def hbar_path(x0, y, w, h, r=4.0):
    """A horizontal bar: square at the baseline, its data end rounded."""
    r = min(r, w / 2, h / 2)
    x1 = x0 + w
    return (f"M{x0:.1f},{y:.1f}H{x1 - r:.1f}Q{x1:.1f},{y:.1f} {x1:.1f},{y + r:.1f}V{y + h - r:.1f}"
            f"Q{x1:.1f},{y + h:.1f} {x1 - r:.1f},{y + h:.1f}H{x0:.1f}Z")


def column_path(x, yb, w, h, r=4.0):
    """A column: square at the baseline, its top rounded."""
    r = min(r, w / 2, h / 2)
    yt = yb - h
    return (f"M{x:.1f},{yb:.1f}V{yt + r:.1f}Q{x:.1f},{yt:.1f} {x + r:.1f},{yt:.1f}H{x + w - r:.1f}"
            f"Q{x + w:.1f},{yt:.1f} {x + w:.1f},{yt + r:.1f}V{yb:.1f}Z")


def figure(title, note, svg, table_head, table_rows, legend=""):
    head = "".join(f"<th>{esc(c)}</th>" for c in table_head)
    body = "".join("<tr>" + "".join(f"<td>{esc(c)}</td>" for c in r) + "</tr>" for r in table_rows)
    return (f'<figure><figcaption><h2>{esc(title)}</h2><p>{esc(note)}</p></figcaption>{legend}{svg}'
            f'<details><summary>Table</summary><table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table></details></figure>')


def bar_chart(rows, unit, ref=None, ref_label=""):
    """Ranked horizontal bars. rows: (label, value, tooltip value, flag text or "")."""
    top_pad, axis_h = (26 if ref else 8), 24
    plot_w = W - LABEL_W - RIGHT_W
    tk = ticks(max([v for _, v, _, _ in rows] + [ref or 0]))
    vmax = tk[-1]
    h = top_pad + len(rows) * ROW_H + axis_h
    x = lambda v: LABEL_W + plot_w * (v / vmax if vmax else 0)
    out = [f'<svg viewBox="0 0 {W} {h}" role="img" aria-label="{esc(unit)}">']
    y_end = top_pad + len(rows) * ROW_H
    for t in tk:
        out.append(f'<line class="grid" x1="{x(t):.1f}" x2="{x(t):.1f}" y1="{top_pad - 4}" y2="{y_end}"/>'
                   f'<text class="tick" x="{x(t):.1f}" y="{y_end + 16}" text-anchor="middle">{esc(tick_text(t))}</text>')
    for i, (label, v, tip, flag) in enumerate(rows):
        y = top_pad + i * ROW_H
        by = y + (ROW_H - BAR_H) / 2
        w = max(0.0, x(v) - LABEL_W)
        cls = "bar flag" if flag else "bar"
        out.append(f'<g class="mark" tabindex="0" data-tip="{esc(label)}" data-val="{esc(tip)}">'
                   f'<rect class="hit" x="0" y="{y}" width="{W}" height="{ROW_H}"/>'
                   f'<text class="label" x="{LABEL_W - 10}" y="{y + ROW_H / 2 + 4.5}" text-anchor="end">{esc(label)}</text>'
                   + (f'<path class="{cls}" d="{hbar_path(LABEL_W, by, w, BAR_H)}"/>' if w >= 0.5 else "")
                   + f'<text class="value" x="{LABEL_W + w + 6:.1f}" y="{y + ROW_H / 2 + 4.5}">'
                   f'{esc(fmt(v))}{(" " + esc(flag)) if flag else ""}</text></g>')
    out.append(f'<line class="axis" x1="{LABEL_W}" x2="{LABEL_W}" y1="{top_pad - 4}" y2="{y_end}"/>')
    if ref:
        out.append(f'<line class="ref" x1="{x(ref):.1f}" x2="{x(ref):.1f}" y1="{top_pad - 8}" y2="{y_end}"/>'
                   f'<text class="tick" x="{x(ref):.1f}" y="{top_pad - 12}" text-anchor="middle">{esc(ref_label)}</text>')
    out.append("</svg>")
    return "".join(out)


def stacked_chart(rows, groups):
    """100% stacked bars. rows: (label, {group: value}); groups: [(name, css var)]."""
    top_pad, axis_h, gap = 8, 24, 2.0
    plot_w = W - LABEL_W - 40
    h = top_pad + len(rows) * ROW_H + axis_h
    out = [f'<svg viewBox="0 0 {W} {h}" role="img" aria-label="share of damage by source">']
    y_end = top_pad + len(rows) * ROW_H
    for t in (0, 25, 50, 75, 100):
        gx = LABEL_W + plot_w * t / 100
        out.append(f'<line class="grid" x1="{gx:.1f}" x2="{gx:.1f}" y1="{top_pad - 4}" y2="{y_end}"/>'
                   f'<text class="tick" x="{gx:.1f}" y="{y_end + 16}" text-anchor="middle">{t}%</text>')
    for i, (label, values) in enumerate(rows):
        y = top_pad + i * ROW_H
        by = y + (ROW_H - BAR_H) / 2
        total = sum(values.values())
        out.append(f'<text class="label" x="{LABEL_W - 10}" y="{y + ROW_H / 2 + 4.5}" text-anchor="end">{esc(label)}</text>')
        if total <= 0:
            out.append(f'<text class="value" x="{LABEL_W + 6}" y="{y + ROW_H / 2 + 4.5}">no damage</text>')
            continue
        parts = [(g, var, values.get(g, 0.0)) for g, var in groups if values.get(g, 0.0) > 0]
        x0 = float(LABEL_W)
        for j, (g, var, v) in enumerate(parts):
            w = plot_w * v / total
            last = j == len(parts) - 1
            draw = w - (0 if last else gap)
            tip = f"{100 * v / total:.0f}% ({fmt(v)} damage)"
            shape = (hbar_path(x0, by, draw, BAR_H) if last else
                     f"M{x0:.1f},{by:.1f}h{draw:.1f}v{BAR_H}h{-draw:.1f}Z") if draw >= 0.5 else ""
            out.append(f'<g class="mark" tabindex="0" data-tip="{esc(label)} · {esc(g)}" data-val="{esc(tip)}">'
                       f'<rect class="hit" x="{x0:.1f}" y="{y}" width="{max(w, 6):.1f}" height="{ROW_H}"/>'
                       + (f'<path class="seg" style="fill:var({var})" d="{shape}"/>' if shape else "") + "</g>")
            x0 += w
    out.append("</svg>")
    return "".join(out)


def column_chart(points, multi_day, step_minutes=BIN_MINUTES):
    """Columns over time. points: [(datetime, value)], one per step_minutes."""
    top_pad, axis_h, left = 12, 26, 56
    plot_w, plot_h = W - left - 16, 180
    tk = ticks(max([v for _, v in points] + [0]))
    vmax = tk[-1]
    n = max(1, len(points))
    slot = plot_w / n
    cw = max(1.0, min(18.0, slot - 2))
    yb = top_pad + plot_h
    out = [f'<svg viewBox="0 0 {W} {yb + axis_h}" role="img" aria-label="damage per minute over time">']
    for t in tk:
        gy = yb - plot_h * (t / vmax if vmax else 0)
        out.append(f'<line class="grid" x1="{left}" x2="{W - 16}" y1="{gy:.1f}" y2="{gy:.1f}"/>'
                   f'<text class="tick" x="{left - 8}" y="{gy + 4:.1f}" text-anchor="end">{esc(tick_text(t))}</text>')
    every = max(1, int(70 // slot) + 1)
    for i, (t, v) in enumerate(points):
        cx = left + i * slot + (slot - cw) / 2
        hgt = plot_h * (v / vmax if vmax else 0)
        whole_day = step_minutes >= 1440
        when_text = t.strftime("%m-%d" if whole_day else "%m-%d %H:%M" if multi_day else "%H:%M")
        tip = when_text if whole_day else when_text + "–" + (t + dt.timedelta(minutes=step_minutes)).strftime("%H:%M")
        out.append(f'<g class="mark" tabindex="0" data-tip="{esc(tip)}" data-val="{esc(fmt(v))} damage per minute">'
                   f'<rect class="hit" x="{left + i * slot:.1f}" y="{top_pad}" width="{slot:.1f}" height="{plot_h}"/>'
                   + (f'<path class="bar" d="{column_path(cx, yb, cw, hgt)}"/>' if hgt >= 0.5 else "") + "</g>")
        if i % every == 0:
            out.append(f'<text class="tick" x="{cx + cw / 2:.1f}" y="{yb + 16}" text-anchor="middle">{esc(when_text)}</text>')
    out.append(f'<line class="axis" x1="{left}" x2="{W - 16}" y1="{yb}" y2="{yb}"/></svg>')
    return "".join(out)


CSS = """
:root{color-scheme:light;--surface:#fcfcfb;--page:#f9f9f7;--ink:#0b0b0b;--ink-2:#52514e;--muted:#898781;--grid:#e1e0d9;--axis:#c3c2b7;
--border:rgba(11,11,11,.10);--s1:#2a78d6;--s2:#eb6834;--s3:#1baf7a;--s4:#eda100;--s5:#e87ba4;--s6:#008300;--s7:#6250d6;--s8:#e34948;
--other:#898781;--serious:#ec835a}
@media (prefers-color-scheme:dark){:root:where(:not([data-theme="light"])){color-scheme:dark;--surface:#1a1a19;--page:#0d0d0d;--ink:#f0efec;
--ink-2:#c3c2b7;--grid:#2c2c2a;--axis:#383835;--border:rgba(255,255,255,.10);--s1:#3987e5;--s2:#d95926;--s3:#199e70;--s4:#c98500;
--s5:#d55181;--s6:#0ca30c;--s7:#9085e9;--s8:#e66767}}
:root[data-theme="dark"]{color-scheme:dark;--surface:#1a1a19;--page:#0d0d0d;--ink:#f0efec;--ink-2:#c3c2b7;--grid:#2c2c2a;--axis:#383835;
--border:rgba(255,255,255,.10);--s1:#3987e5;--s2:#d95926;--s3:#199e70;--s4:#c98500;--s5:#d55181;--s6:#0ca30c;--s7:#9085e9;--s8:#e66767}
*{box-sizing:border-box}
body{margin:0;background:var(--page);color:var(--ink);font:14px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:960px;margin:0 auto;padding:24px 16px 48px}
h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:0}
header p,figcaption p,.notes{color:var(--ink-2);margin:4px 0 0}
section,figure{background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:16px;margin:16px 0}
figcaption{margin-bottom:8px}
svg{display:block;width:100%;height:auto;overflow:visible}
.grid{stroke:var(--grid);stroke-width:1}.axis{stroke:var(--axis);stroke-width:1}.ref{stroke:var(--ink-2);stroke-width:1}
.tick{fill:var(--muted);font-size:11px;font-variant-numeric:tabular-nums}
.label{fill:var(--ink-2);font-size:12.5px}.value{fill:var(--ink);font-size:12px;font-variant-numeric:tabular-nums}
.bar{fill:var(--s1)}.bar.flag{fill:var(--serious)}
.hit{fill:transparent}
.mark{outline:none;cursor:default}.mark:hover .bar,.mark:hover .seg,.mark:focus .bar,.mark:focus .seg{filter:brightness(1.15)}
.mark:focus .hit{stroke:var(--ink-2);stroke-width:1}
.legend{display:flex;flex-wrap:wrap;gap:6px 16px;margin:0 0 8px;color:var(--ink-2);font-size:12.5px}
.legend i{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:6px;vertical-align:-1px}
details{margin-top:8px}summary{cursor:pointer;color:var(--ink-2)}
.wrap{overflow-x:auto}
table{border-collapse:collapse;width:100%;font-size:12.5px;font-variant-numeric:tabular-nums}
th,td{text-align:right;padding:4px 6px;border-bottom:1px solid var(--grid);white-space:nowrap}
th:first-child,td:first-child,.tl{text-align:left}th{color:var(--ink-2);font-weight:600}
.pill{display:inline-block;padding:0 6px;border-radius:9px;background:var(--serious);color:#0b0b0b;font-weight:600}
#tip{position:fixed;z-index:9;pointer-events:none;background:var(--surface);color:var(--ink);border:1px solid var(--border);
border-radius:6px;padding:6px 10px;box-shadow:0 4px 14px rgba(0,0,0,.18);font-size:12.5px;max-width:320px}
#tip b{display:block;font-size:14px}#tip span{color:var(--ink-2)}
"""

JS = """
(() => {
  const tip = document.getElementById('tip'), v = tip.querySelector('b'), l = tip.querySelector('span');
  function show(el, x, y) {
    v.textContent = el.getAttribute('data-val');
    l.textContent = el.getAttribute('data-tip');
    tip.hidden = false;
    const r = tip.getBoundingClientRect();
    tip.style.left = Math.max(8, Math.min(x + 14, innerWidth - r.width - 8)) + 'px';
    tip.style.top = Math.max(8, y - r.height - 12) + 'px';
  }
  const hide = () => { tip.hidden = true; };
  for (const el of document.querySelectorAll('[data-tip]')) {
    el.addEventListener('pointermove', e => show(el, e.clientX, e.clientY));
    el.addEventListener('pointerleave', hide);
    el.addEventListener('focus', () => { const b = el.getBoundingClientRect(); show(el, b.left + b.width / 2, b.top); });
    el.addEventListener('blur', hide);
  }
})();
"""


def render(r, meta):
    skills = r["skills"]
    order = ranked(skills)
    plain = [(n, k) for n, k in order if n not in SKYFALL]
    days = meta["days"]
    median = r["median_dpm"]

    def flag_text(k):
        return f"⚠ {k['x_median']:.1f}× median" if k["flag"] else ""

    # the skill table
    head = ["Skill", "Shots", "Archers", "Per shot", "Median", "Best 10%", "Per minute", "vs median", "Kills", "Kills / 100",
            "Held (s)", "To players"]
    trs = []
    for n, k in order:
        vs = (f'<span class="pill">{esc(flag_text(k))}</span>' if k["flag"]
              else esc(f"{k['x_median']:.2f}×") if k["x_median"] is not None else "–")
        cells = [esc(n), k["shots"], k["players"], esc(fmt(k["per_shot_mean"])), esc(fmt(k["per_shot_median"])),
                 esc(fmt(k["per_shot_p90"])), esc(fmt(k["dpm"])), vs, k["kills"], esc(fmt(k["kills_per_100"])),
                 esc(fmt(k["held_mean"])), esc(fmt(k["pvp_damage"]))]
        trs.append("<tr>" + "".join(f"<td>{c}</td>" for c in cells) + "</tr>")
    table = ('<section><h2>Every skill</h2><p class="notes">Ranked by damage per minute. Damage is what came off creatures\' health: '
             'per shot (average, median, best 10% of shots), per minute, and to players apart; held is seconds past the 68% pull. '
             f'A skill at {FLAG_AT}× the median or more is flagged ⚠ (median {esc(fmt(median))} per minute over the skills with at '
             f'least {r["min_shots"]} shots; Skyfall left out).</p><div class="wrap"><table><thead><tr>'
             + "".join(f"<th>{esc(c)}</th>" for c in head) + "</tr></thead><tbody>" + "".join(trs) + "</tbody></table></div></section>")

    figs = []
    rows = [(n, k["dpm"], f"{fmt(k['dpm'])} damage per minute ({fmt(k['damage'])} over {k['shots']} shots)", flag_text(k)) for n, k in plain]
    figs.append(figure("Damage per minute, by skill",
                       f"Damage over the time each shot took (the time since that archer's previous shot, at most {fmt(r['gap_cap'])} s). The line is the median.",
                       bar_chart(rows, "damage per minute", median, f"median {fmt(median)}" if median else ""),
                       ["Skill", "Damage per minute", "Damage", "Shots"], [(n, fmt(k["dpm"]), fmt(k["damage"]), k["shots"]) for n, k in plain]))
    by_shot = sorted(plain, key=lambda kv: -kv[1]["per_shot_mean"])
    rows = [(n, k["per_shot_mean"], f"{fmt(k['per_shot_mean'])} average · median {fmt(k['per_shot_median'])} · best 10% {fmt(k['per_shot_p90'])}", "")
            for n, k in by_shot]
    figs.append(figure("Damage per shot, by skill", "The average shot; misses count as 0.", bar_chart(rows, "damage per shot"),
                       ["Skill", "Average", "Median", "Best 10%", "Shots"],
                       [(n, fmt(k["per_shot_mean"]), fmt(k["per_shot_median"]), fmt(k["per_shot_p90"]), k["shots"]) for n, k in by_shot]))
    by_kills = sorted(plain, key=lambda kv: -kv[1]["kills_per_100"])
    rows = [(n, k["kills_per_100"], f"{fmt(k['kills_per_100'])} kills per 100 shots ({k['kills']} kills)", "") for n, k in by_kills]
    figs.append(figure("Kills per 100 shots, by skill", "Creatures whose last health a shot of this skill took.", bar_chart(rows, "kills per 100 shots"),
                       ["Skill", "Kills per 100 shots", "Kills", "Shots"], [(n, fmt(k["kills_per_100"]), k["kills"], k["shots"]) for n, k in by_kills]))
    by_use = sorted(skills.items(), key=lambda kv: -kv[1]["shots"])
    rows = [(n, k["shots"], f"{k['shots']} shots by {k['players']} archer(s)", "") for n, k in by_use]
    figs.append(figure("Shots, by skill", "How often each skill was used.", bar_chart(rows, "shots"),
                       ["Skill", "Shots", "Archers"], [(n, k["shots"], k["players"]) for n, k in by_use]))

    groups = [(name, f"--s{i + 1}") for i, (name, _) in enumerate(SOURCE_GROUPS)] + [("Other", "--other")]
    legend = '<div class="legend">' + "".join(f'<span><i style="background:var({var})"></i>{esc(name)}</span>' for name, var in groups) + "</div>"
    figs.append(figure("Where each skill's damage comes from",
                       "Share of each skill's damage by what dealt it: the real arrow, its spectral arrows, and the orbs that add hits.",
                       stacked_chart([(n, k["sources"]) for n, k in plain], groups),
                       ["Skill"] + [g for g, _ in groups],
                       [[n] + [fmt(k["sources"].get(g, 0.0)) for g, _ in groups] for n, k in plain], legend))
    mains = sorted(r["mains"].items(), key=lambda kv: -kv[1]["dpm"])
    rows = [(n, k["dpm"], f"{fmt(k['dpm'])} damage per minute · {fmt(k['per_shot_mean'])} per shot ({k['shots']} shots)", "") for n, k in mains]
    figs.append(figure("Damage per minute, by main path", "Every shot of archers on each main path (Skyfall left out).",
                       bar_chart(rows, "damage per minute by main path"),
                       ["Main path", "Damage per minute", "Damage per shot", "Shots"],
                       [(n, fmt(k["dpm"]), fmt(k["per_shot_mean"]), k["shots"]) for n, k in mains]))
    if r["timeline"]:
        m = r["step_minutes"]
        step_text = f"{m}-minute" if m < 60 else f"{m // 60}-hour" if m < 1440 else "one-day"
        figs.append(figure("Damage per minute over the day" if len(days) == 1 else "Damage per minute over time",
                           f"All archery damage to creatures in {step_text} steps (Skyfall left out).",
                           column_chart(r["timeline"], len(days) > 1, m),
                           ["From", "Damage per minute"], [(t.strftime("%Y-%m-%d %H:%M"), fmt(v)) for t, v in r["timeline"]]))

    # per player x skill
    prow = sorted(r["players"].items(), key=lambda kv: (kv[0][0].lower(), -kv[1]["dpm"]))
    ptable = ('<section><h2>Each archer, by skill</h2><p class="notes">One strong archer can make a skill look strong: compare the same skill '
              'across archers.</p><div class="wrap"><table><thead><tr><th>Archer</th><th class="tl">Skill</th><th>Shots</th><th>Damage per shot</th>'
              '<th>Damage per minute</th><th>Kills</th></tr></thead><tbody>'
              + "".join(f"<tr><td>{esc(p)}</td><td class=\"tl\">{esc(sk)}</td><td>{k['shots']}</td><td>{esc(fmt(k['per_shot_mean']))}</td>"
                        f"<td>{esc(fmt(k['dpm']))}</td><td>{k['kills']}</td></tr>" for (p, sk), k in prow)
              + "</tbody></table></div></section>")

    tunes = sorted(r["tunings"].items(), key=lambda kv: -kv[1])
    tune_text = "; ".join(f"{v} (settings {s}): {n} shots" for (v, s), n in tunes)
    notes = [f"{r['shots']} shots and {r['hits']} hit rows from {len(r['archers'])} archer(s) in {len(r['sessions'])} server session(s).",
             f"Damage per minute counts each shot's time as the gap since that archer's previous shot, at most {fmt(r['gap_cap'])} s; an archer's first shot counts {fmt(r['gap_cap'])} s."]
    if r["unattributed"]["hits"]:
        notes.append(f"{r['unattributed']['hits']} hit(s) ({fmt(r['unattributed']['damage'])} damage) had no recorded shot "
                     "(an arrow used by hand, or a shot from before the server started); they are in the timeline only.")
    notes.append("Tuning: " + (tune_text or "none") + (". This mixes tunings: run the report with --version to look at one." if len(tunes) > 1 else "."))
    if r["test_shots"]:
        notes.append(f"{r['test_shots']} shot(s) made with test-build orbs ([TestBuild]) were left out (--include-test keeps them).")
    if r["things"]:
        notes.append(f"{r['things']} hit(s) on things that are not creatures or players were left out.")
    if r["mods"]:
        notes.append(f"{r['mods']} hit(s) came from a damage site the telemetry does not know (source 'mod'): tell Claude, it is a gap in the recording.")
    day_text = days[0] if len(days) == 1 else f"{days[0]} to {days[-1]}"
    return ("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            f"<title>Blue's Archery Overhaul balance</title><style>{CSS}</style></head><body><main>"
            f"<header><h1>Blue's Archery Overhaul balance, {esc(day_text)}</h1><p>{esc(notes[0])} Hover or tab to a bar for its numbers; every chart has a table.</p></header>"
            + table + "".join(figs) + ptable
            + '<section><h2>Notes</h2>' + "".join(f'<p class="notes">{esc(n)}</p>' for n in notes[1:])
            + f'<p class="notes">Data: {esc(meta.get("source", ""))}</p></section>'
            + f'</main><div id="tip" role="tooltip" hidden><b></b><span></span></div><script>{JS}</script></body></html>')


def summary_text(r):
    lines = [f"{'Skill':24} {'Shots':>6} {'Dmg/shot':>9} {'Dmg/min':>8} {'vs median':>10} {'Kills/100':>9}"]
    for n, k in ranked(r["skills"]):
        vs = f"{k['x_median']:.2f}x" + (" !" if k["flag"] else "") if k["x_median"] is not None else "-"
        lines.append(f"{n:24} {k['shots']:>6} {fmt(k['per_shot_mean']):>9} {fmt(k['dpm']):>8} {vs:>10} {fmt(k['kills_per_100']):>9}")
    lines.append(f"median damage per minute: {fmt(r['median_dpm'])} (skills with {r['min_shots']}+ shots, Skyfall left out); ! = {FLAG_AT}x or more")
    return "\n".join(lines)


# ------------------------------------------------------------------------------------------------------------------- self-test

def selftest_stats(rows_folder, near):
    """The statistics files (TelemetryStats.cs) with known answers: two server runs on one day add together, a name with a comma and
    quotes, a skill's median and best 10% from its D lines, a test build's lines left out, Skyfall out of the median; and a folder that
    holds both kinds of file is read as one kind only (rows_folder holds selftest's row files)."""
    key = "player,account,kind,ult,ghosts,pellets,needles,tier,hold,locks,sky,main,quick,volley,charge,ults,ricochet,shatter,gatherer,test,version,settings".split(",")
    nums = "shots,cost_s,damage,kills,pvp_damage,pvp_kills,held_ms,held_n".split(",")
    by = ("arrow,ghost,pellet,needle,core,line,ricochet,shatter,holybeam,holyblast,weight,skyfall,mod").split(",")
    jo = 'Smith, "Jo"'

    def s_line(player, account, kind, ult, ghosts, pellets, tier, sky, main, ults, test, numbers, sources):
        k = dict(player=player, account=account, kind=kind, ult=ult, ghosts=ghosts, pellets=pellets, needles=0, tier=tier, hold=0, locks=0,
                 sky=sky, main=main, quick=0, volley=3 if main == "Multi" else 0, charge=3 if main == "Charge" else 0, ults=ults, ricochet=0,
                 shatter=0, gatherer=0, test=test, version="0.19.5", settings="00c0ffee")
        return (["S", "2026-11-19"] + [k[c] for c in key] + list(numbers) + [sources.get(b, 0) for b in by])

    def d_line(kind, ult, ghosts, pellets, tier, sky, main, test, shots, zero, steps):
        return ["D", "2026-11-19", kind, ult, ghosts, pellets, 0, tier, sky, main, 0, test, "0.19.5", "00c0ffee", shots, zero, steps]

    def write(path, session, s, d, t, m):
        with open(path, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.writer(f, lineterminator="\n")
            w.writerow(["#", "Blue's Archery Overhaul balance statistics", "v1", "gap_cap_s", "15", "step", "0.5", "session", session])
            w.writerow(["#S", "day"] + key + nums + ["by_" + b for b in by])
            w.writerows(s)
            w.writerow("#D,day,kind,ult,ghosts,pellets,locks,tier,sky,main,quick,test,version,settings,shots,zero,steps".split(","))
            w.writerows(d)
            w.writerow("#T,day,hour,version,damage".split(","))
            w.writerows(t)
            w.writerow("#M,day,version,hits,unattributed_hits,unattributed_damage,things,mods".split(","))
            w.writerows(m)

    folder = tempfile.mkdtemp(prefix="telemetry-selftest-stats-")
    write(os.path.join(folder, "stats-v1-2026-11-19-20261119-080000.csv"), "20261119-080000",
          [s_line("Ana", 1, "Held", "None", 15, 15, 0, "None", "Multi", "-", 0, (2, 20, 30, 1, 0, 0, 2000, 2), {"arrow": 10, "ghost": 12, "pellet": 8}),
           s_line(jo, 2, "Charged", "None", 0, 0, 3, "None", "Charge", "-", 0, (1, 15, 12, 0, 4, 0, 3000, 1), {"arrow": 12}),
           s_line(jo, 2, "Early", "None", 0, 0, 0, "Skyfall", "Charge", "-", 0, (1, 3, 30, 1, 0, 0, 1000, 1), {"skyfall": 30}),
           s_line("Ana", 1, "Held", "None", 15, 15, 0, "None", "Multi", "-", 1, (5, 75, 500, 5, 0, 0, 5000, 5), {"ghost": 500})],
          [d_line("Held", "None", 15, 15, 0, "None", "Multi", 0, 2, 1, "10:1"),
           d_line("Charged", "None", 0, 0, 3, "None", "Charge", 0, 1, 0, "24:1"),
           d_line("Early", "None", 0, 0, 0, "Skyfall", "Charge", 0, 1, 0, "60:1"),
           d_line("Held", "None", 15, 15, 0, "None", "Multi", 1, 5, 0, "200:5")],
          [["T", "2026-11-19", 18, "0.19.5", 50]],
          [["M", "2026-11-19", "0.19.5", 9, 1, 2.0, 1, 0]])
    write(os.path.join(folder, "stats-v1-2026-11-19-20261119-180000.csv"), "20261119-180000",
          [s_line("Ana", 1, "Held", "None", 15, 15, 0, "None", "Multi", "-", 0, (3, 30, 45, 2, 0, 0, 3000, 3), {"arrow": 15, "ghost": 20, "pellet": 10})],
          [d_line("Held", "None", 15, 15, 0, "None", "Multi", 0, 3, 0, "10:1;20:1;30:1")],
          [["T", "2026-11-19", 19, "0.19.5", 45]],
          [["M", "2026-11-19", "0.19.5", 6, 0, 0, 0, 1]])
    both = tempfile.mkdtemp(prefix="telemetry-selftest-both-")
    for name in os.listdir(rows_folder) + os.listdir(folder):
        src = os.path.join(rows_folder if name.startswith(("shots-", "hits-")) else folder, name)
        with open(src, "rb") as a, open(os.path.join(both, name), "wb") as b:
            b.write(a.read())
    try:
        r, days = read_stats([folder], min_shots=1)
        k = r["skills"]
        tl = r["timeline"]
        rs, ds, kind_s = load([both], min_shots=1)
        rr, dr, kind_r = load([both], rows=True, min_shots=1)
        rt, _ = read_stats([folder], min_shots=1, include_test=True)
        return [
            ("statistics: the result has every key analyze gives", set(r) == set(analyze(iter(()), iter(()))) and days == ["2026-11-19"]),
            ("statistics: skills", set(k) == {"Burst", "Charge Shot 3", "Skyfall"}),
            ("statistics: two runs on one day add together", k["Burst"]["shots"] == 5 and near(k["Burst"]["damage"], 75) and k["Burst"]["kills"] == 3
                                                           and r["sessions"] == ["20261119-080000", "20261119-180000"]),
            ("statistics: damage per minute and per shot", near(k["Burst"]["dpm"], 90.0) and near(k["Burst"]["per_shot_mean"], 15.0)
                                                          and near(k["Burst"]["held_mean"], 1.0)),
            ("statistics: median and best 10% from the D lines", near(k["Burst"]["per_shot_median"], 5.25) and near(k["Burst"]["per_shot_p90"], 15.25)
                                                                and near(k["Charge Shot 3"]["per_shot_median"], 12.25)),
            ("statistics: damage by source", near(k["Burst"]["sources"]["Real arrow"], 25) and near(k["Burst"]["sources"]["Ghosts"], 32)
                                             and near(k["Burst"]["sources"]["Pellets"], 18)),
            ("statistics: a name with a comma and quotes", (jo, "Charge Shot 3") in r["players"] and near(r["players"][(jo, "Charge Shot 3")]["pvp_damage"], 4)),
            ("statistics: a test build's lines left out", r["test_shots"] == 5 and k["Burst"]["shots"] == 5 and r["shots"] == 7
                                                          and r["tunings"] == {("0.19.5", "00c0ffee"): 7}),
            ("statistics: kept with --include-test", rt["skills"]["Burst"]["shots"] == 10 and rt["test_shots"] == 0
                                                     and near(rt["skills"]["Burst"]["per_shot_p90"], 100.25)),
            ("statistics: Skyfall out of the median and the main paths", near(r["median_dpm"], 69.0) and k["Skyfall"]["x_median"] is None
                                                                         and near(k["Skyfall"]["dpm"], 600.0) and r["mains"]["Charge"]["shots"] == 1
                                                                         and r["mains"]["Multi"]["shots"] == 5),
            ("statistics: the hit counts", r["hits"] == 15 and r["unattributed"]["hits"] == 1 and near(r["unattributed"]["damage"], 2)
                                           and r["things"] == 1 and r["mods"] == 1 and r["gap_cap"] == 15.0),
            ("statistics: the timeline in hours", r["step_minutes"] == 60 and len(tl) == 2 and tl[0][0] == dt.datetime(2026, 11, 19, 18)
                                                  and near(tl[0][1], 50 / 60) and near(tl[1][1], 45 / 60)),
            ("statistics: a folder with both kinds reads the statistics only", kind_s == "stats" and rs["shots"] == 7 and ds == ["2026-11-19"]),
            ("statistics: --rows reads the rows only", kind_r == "rows" and rr["shots"] == 6 and rr["test_shots"] == 1),
            ("statistics: the page draws", "Burst" in render(r, {"days": days, "source": folder})),
        ]
    except Exception as e:   # before read_stats exists, or a crash in it: one failed check naming it
        return [(f"statistics: {type(e).__name__}: {e}", False)]


def selftest():
    """A tiny made-up day with known answers: shot counts, damage per shot, damage per minute with the gap cap, kills, the median
    without Skyfall, a name with a comma and quotes, columns in another order, a shot that hit nothing."""
    jo = 'Smith, "Jo"'
    base = dt.datetime(2026, 11, 19, 18, 0, 0)

    def t(seconds):
        return (base + dt.timedelta(seconds=seconds)).strftime("%Y-%m-%d %H:%M:%S.%f")[:-3]

    def shot(n, sec, player, account, kind="Early", ult="None", ghosts=0, pellets=0, needles=0, tier=0, sky="None", main="None",
             quick=0, locks=0):
        return dict(time=t(sec), session="S1", shot=n, arrow=100 + n, player=player, account=account, kind=kind, ult=ult, ghosts=ghosts,
                    pellets=pellets, needles=needles, tier=tier, hold=0, ms=1000, locks=locks, sky=sky, main=main, quick=quick, volley=0,
                    charge=0, ults="-", ricochet=0, shatter=0, gatherer=0, test=0, version="0.18.2", settings="00c0ffee")

    shots = [
        shot(1, 0, "Ana", 1, kind="Held", ghosts=15, pellets=15, main="Multi"),
        shot(2, 5, jo, 2, kind="Charged", tier=3, main="Charge"),
        shot(3, 8, jo, 2, sky="Skyfall", main="Charge"),
        shot(4, 10, "Ana", 1, kind="Held", ghosts=15, pellets=15, main="Multi"),
        shot(5, 20, jo, 2, kind="Snap", needles=2, main="Quick", quick=3),
        shot(6, 100, "Ana", 1, main="Multi"),
        dict(shot(7, 110, "Ana", 1, kind="Held", ghosts=15, pellets=15, main="Multi"), test=1),   # a test build's borrowed orbs
    ]

    def hit(n, sec, player, source, dealt, victim="creature", target="Turabada", killed=0):
        return dict(time=t(sec), session="S1", shot=n, player=player, source=source, target=target, target_id=10 + n, victim=victim,
                    raw=dealt, dealt=dealt, cripple=0, killed=killed, max_health=30)

    hits = [
        hit(1, 0.3, "Ana", "arrow", 5), hit(1, 0.4, "Ana", "ghost", 6), hit(1, 0.4, "Ana", "pellet", 4),
        hit(1, 0.5, "Ana", "ghost", 1.5, victim="thing", target="Fence"),
        hit(4, 10.3, "Ana", "ghost", 10, target="Gotera", killed=1),
        hit(2, 5.3, jo, "arrow", 12, target="Wyrm"), hit(2, 5.4, jo, "arrow", 4, victim="player", target="Ana"),
        hit(3, 12.0, jo, "skyfall", 30, killed=1),
        hit(0, 30.0, "?", "arrow", 2),
        hit(7, 110.3, "Ana", "ghost", 50),
    ]
    part2 = [hit(5, 20.2, jo, "arrow", 4)]   # the day went on in a second file
    folder = tempfile.mkdtemp(prefix="telemetry-selftest-")
    with open(os.path.join(folder, "shots-v1-2026-11-19.csv"), "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=SHOT_COLUMNS)
        w.writeheader()
        w.writerows(shots)
    with open(os.path.join(folder, "hits-v1-2026-11-19.csv"), "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(reversed(HIT_COLUMNS)))   # another column order: read by name
        w.writeheader()
        w.writerows(hits)
    with open(os.path.join(folder, "hits-v1-2026-11-19-2.csv"), "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=HIT_COLUMNS)
        w.writeheader()
        w.writerows(part2)

    s, h, days = read_day([folder])
    r = analyze(s, h, gap_cap=15.0, min_shots=1)
    k = r["skills"]

    def near(a, b):
        return a is not None and abs(a - b) < 1e-9

    checks = [
        ("days", days == ["2026-11-19"]),
        ("skills", set(k) == {"Burst", "Charge Shot 3", "Skyfall", "Quickdraw 3", "Plain"}),
        ("Burst shots", k["Burst"]["shots"] == 2),
        ("Burst damage per shot", near(k["Burst"]["per_shot_mean"], 12.5) and near(k["Burst"]["per_shot_median"], 12.5)),
        ("Burst damage per minute (gap cap)", near(k["Burst"]["dpm"], 60.0)),
        ("Burst kills", k["Burst"]["kills"] == 1 and near(k["Burst"]["kills_per_100"], 50.0)),
        ("Burst's damage by source", near(k["Burst"]["sources"]["Real arrow"], 5) and near(k["Burst"]["sources"]["Ghosts"], 16)
                                      and near(k["Burst"]["sources"]["Pellets"], 4)),
        ("a thing is not creature damage", near(k["Burst"]["damage"], 25.0) and r["things"] == 1),
        ("Charge Shot: creature damage and PvP apart", near(k["Charge Shot 3"]["damage"], 12) and near(k["Charge Shot 3"]["pvp_damage"], 4)
                                                       and near(k["Charge Shot 3"]["dpm"], 48.0)),
        ("Quickdraw", near(k["Quickdraw 3"]["dpm"], 20.0)),
        ("a shot that hit nothing", k["Plain"]["shots"] == 1 and near(k["Plain"]["per_shot_mean"], 0.0) and near(k["Plain"]["dpm"], 0.0)),
        ("Skyfall counted", near(k["Skyfall"]["damage"], 30) and k["Skyfall"]["kills"] == 1 and near(k["Skyfall"]["dpm"], 600.0)),
        ("the median leaves Skyfall out", near(r["median_dpm"], 34.0) and k["Skyfall"]["x_median"] is None and not k["Skyfall"]["flag"]),
        ("a skill far above the median is flagged", near(k["Burst"]["x_median"], 60 / 34) and k["Burst"]["flag"]
                                                    and not k["Charge Shot 3"]["flag"]),
        ("a name with a comma and quotes", (jo, "Quickdraw 3") in r["players"] and r["players"][(jo, "Quickdraw 3")]["shots"] == 1),
        ("per main path, Skyfall left out", r["mains"]["Multi"]["shots"] == 3 and near(r["mains"]["Multi"]["dpm"], 37.5)
                                            and r["mains"]["Charge"]["shots"] == 1 and near(r["mains"]["Charge"]["dpm"], 48.0)),
        ("damage with no shot", r["unattributed"]["hits"] == 1 and near(r["unattributed"]["damage"], 2)),
        ("a test build's shot left out", r["test_shots"] == 1 and k["Burst"]["shots"] == 2),
        ("the tuning", r["tunings"] == {("0.18.2", "00c0ffee"): 6}),
        ("the timeline", len(r["timeline"]) == 1 and r["timeline"][0][0] == base and near(r["timeline"][0][1], 43 / 5)),
    ]
    page = render(r, {"days": days, "source": folder})
    checks += [
        ("the page names the flagged skill", "Burst" in page and "1.8\u00d7 median" in page),
        ("the page escapes names", "Smith, &quot;Jo&quot;" in page and 'Smith, "Jo"' not in page),
        ("every chart has a table", page.count("<figure") == page.count("<details") and page.count("<figure") >= 6),
    ]
    checks += selftest_stats(folder, near)
    failed = [name for name, ok in checks if not ok]
    for name in failed:
        print("FAIL", name)
    print("TELEMETRY REPORT SELFTEST PASS" if not failed else f"TELEMETRY REPORT SELFTEST FAILED ({len(failed)} of {len(checks)})")
    return 0 if not failed else 1


# ------------------------------------------------------------------------------------------------------------------- main

def main(argv=None):
    ap = argparse.ArgumentParser(description="Blue's Archery Overhaul balance report from the server's telemetry CSVs.")
    ap.add_argument("paths", nargs="*", help=f"folders or files (default: {SERVER_DATA})")
    ap.add_argument("--day", help="YYYY-MM-DD: that day only (default: every day found, compiled together)")
    ap.add_argument("--all", action="store_true", help="every day found (the default; kept so older commands still work)")
    ap.add_argument("--out", help="the page to write (default: report-<day>.html beside the data)")
    ap.add_argument("--gap-cap", type=float, default=15.0, help="seconds: the most a gap between one player's shots counts (default 15)")
    ap.add_argument("--min-shots", type=int, default=20, help="a skill needs this many shots to count toward the median (default 20)")
    ap.add_argument("--version", help="only shots made with this mod version (each shot row carries it)")
    ap.add_argument("--include-test", action="store_true", help="keep shots made with [TestBuild] borrowed orbs")
    ap.add_argument("--no-open", action="store_true", help="do not open the page")
    ap.add_argument("--rows", action="store_true", help="read the per-shot row files (shots-v1, hits-v1) even where statistics files exist")
    ap.add_argument("--compare", action="store_true", help="read both kinds from the same data, print where they differ, exit 1 if they do")
    ap.add_argument("--selftest", action="store_true", help="check the numbers on a tiny made-up day and exit")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    paths = a.paths or [SERVER_DATA]
    if a.compare:
        rows, days_r, _ = load(paths, True, a.day, a.gap_cap, a.min_shots, a.include_test, a.version)
        stats, days_s = read_stats(paths, a.day, a.min_shots, a.include_test, a.version)
        diffs = compare(rows, stats)
        if days_r != days_s:
            diffs.insert(0, f"days: rows {', '.join(days_r)}; statistics {', '.join(days_s)}")
        for d in diffs:
            print("DIFFERENT", d)
        print(f"COMPARE: {rows['shots']} shots, {rows['hits']} hits, {len(rows['skills'])} skills over {len(days_r)} day(s): "
              + ("the row files and the statistics agree" if not diffs else f"{len(diffs)} difference(s)"))
        return 1 if diffs else 0
    r, days, kind = load(paths, a.rows, a.day, a.gap_cap, a.min_shots, a.include_test, a.version)
    if kind == "stats":
        if a.gap_cap != r["gap_cap"]:
            print(f"--gap-cap {fmt(a.gap_cap)} is ignored: the statistics were compiled with {fmt(r['gap_cap'])} s (--rows reads the row files, if kept)")
        only_rows = sorted({day_of(f) for f in find_files(paths)} - set(days)) if not a.day else []
        if only_rows:
            print(f"{len(only_rows)} day(s) have only row files and are left out ({only_rows[0]} to {only_rows[-1]}): --rows reads the row files instead")
    folder = paths[0] if os.path.isdir(paths[0]) else os.path.dirname(os.path.abspath(paths[0]))
    out = a.out or os.path.join(folder, f"report-{days[0] if len(days) == 1 else days[0] + '_to_' + days[-1]}.html")
    with open(out, "w", encoding="utf-8") as f:
        f.write(render(r, {"days": days, "source": ", ".join(paths)}))
    print(summary_text(r))
    print(f"\nPage: {out}")
    if not a.no_open:
        webbrowser.open("file:///" + os.path.abspath(out).replace("\\", "/"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
