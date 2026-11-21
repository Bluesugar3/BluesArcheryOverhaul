#!/usr/bin/env bash
# tools/test-installer.sh : the test of tools/install-when-closed.sh. It runs the installer ONLY on scratch folders made
# here (the QR_CLIENT / QR_SERVER / QR_STATE / QR_BACKUPS overrides) and never touches the game, the server or tools/.install.
# Checks: a missing server or client Mods folder is refused at once with exit 4, before any backup or copy, in install,
# --rollback and --preflight; --preflight passes when both folders exist and writes nothing, with no launcher pack folder
# (skipped since 2026-11-19: the installer never needs or writes "Modpacks/My mods"); an unknown option is refused.
# When neither the game nor the server is running it also does full scratch installs and rollbacks across the rename (the mod
# was QuickshotRing.dll up to 0.18.3, BluesArcheryOverhaul.dll after): old name -> new name (QuickshotRing.dll removed from
# both Mods, the play-test kit added), rollback new -> old (QuickshotRing.dll back, the new name and the kit removed), and a
# rollback to a backup that held the new name (the old name stays away).
#   QR_SCRIPT=<path> picks the installer to test (default: the one beside this script).
set -u
SCRIPT="${QR_SCRIPT:-$(cd "$(dirname "$0")" && pwd)/install-when-closed.sh}"
R=$(mktemp -d) && [ -n "$R" ] && [ -d "$R" ] || { echo "no scratch folder"; exit 1; }
export QR_CLIENT="$R/game (client" QR_SERVER="$R/game(server" QR_STATE="$R/state" QR_BACKUPS="$R/bk"
CL="$QR_CLIENT"; SV="$QR_SERVER"; ST="$SV/BluesArcheryOverhaul-install-status.txt"; LOG="$R/state/install.log"
MISS="$SV/Mods"; PACK="$CL/Modpacks/My mods"
OLD=QuickshotRing.dll; NEW=BluesArcheryOverhaul.dll; KIT=BluesArcheryTestKit.dll
pass=0; fail=0
check() { local name="$1"; shift; if "$@"; then echo "PASS $name"; pass=$((pass+1)); else echo "FAIL $name"; fail=$((fail+1)); fi; }
run() { local t0=$SECONDS; out=$(timeout "$TO" bash "$SCRIPT" "$@" 2>&1); rc=$?; secs=$((SECONDS-t0)); }
untouched() { [ "$(cat "$CL/Mods/$OLD")" = old ] && [ "$(cat "$SV"/Mods*/$OLD)" = old ] && [ ! -e "$CL/Mods/$NEW" ] && [ ! -e "$CL/Mods/$KIT" ] \
  && [ "$(ls -1d "$R/bk"/auto-* | wc -l)" = 1 ] && [ ! -e "$R/state/waiting.pid" ]; }
last_is() { [[ "$(printf '%s\n' "$out" | tail -1)" == *" $1" ]]; }
refusals() { grep -cF "INSTALL REFUSED: missing $1" "$LOG" 2>/dev/null; }
# holds <file> <content> on both sides, or absent with "-"
both() { local f="$1" want="$2" d; for d in "$CL/Mods" "$SV/Mods"; do
  if [ "$want" = "-" ]; then [ ! -e "$d/$f" ] || return 1; else [ "$(cat "$d/$f" 2>/dev/null)" = "$want" ] || return 1; fi; done; }
newest() { ls -1d "$R/bk"/auto-* | sort | tail -1; }
stage() { # stage <version> <main content> [kit content]
  rm -rf "$R/state/staged"; mkdir -p "$R/state/staged"; echo "$2" > "$R/state/staged/$NEW"
  local k=""; if [ -n "${3:-}" ]; then echo "$3" > "$R/state/staged/$KIT"; k="kitmd5=$(md5sum "$R/state/staged/$KIT" | cut -c1-32)"; fi
  printf 'version=%s\nmd5=%s\n%s\nfriends=no\nchanged=scratch\ntest=scratch\n' "$1" "$(md5sum "$R/state/staged/$NEW" | cut -c1-32)" "$k" > "$R/state/staged/manifest.txt"
}

# the scene: client Mods holds the old name's "old", the server's Mods is MISSING (its "old" waits in Mods.away), there is no
# launcher pack folder at all, one old backup (of the old name), one staged build with the play-test kit
mkdir -p "$CL/Mods" "$SV/Mods.away" "$R/state" "$R/bk/auto-2026-01-01-000000-v0.0.1/client"
echo old > "$CL/Mods/$OLD"; echo old > "$SV/Mods.away/$OLD"
echo backup > "$R/bk/auto-2026-01-01-000000-v0.0.1/client/$OLD"
printf 'installed=0.0.1 at 2026-01-01 00:00\nwaiting=9.9.9\n' > "$ST"
stage 9.9.9 new kit
TO=20

run --preflight
check "installer preflight: a missing server Mods folder gives exit 4" [ "$rc" = 4 ]
check "installer preflight: it prints only the missing server folder" [ "$out" = "PREFLIGHT REFUSED: missing $MISS" ]
check "installer preflight: it writes no log, status, backup or copy" eval '[ ! -e "$LOG" ] && untouched && [ "$(cat "$ST")" = "$(printf "installed=0.0.1 at 2026-01-01 00:00\nwaiting=9.9.9")" ]'

run
check "installer install: a missing server Mods folder gives exit 4" [ "$rc" = 4 ]
check "installer install: it refuses before the wait (under 5 s)" [ "$secs" -lt 5 ]
check "installer install: its last line is the refusal" last_is "INSTALL REFUSED: missing $MISS"
check "installer install: nothing was backed up or copied" untouched
check "installer install: the log holds the refusal once" [ "$(refusals "$MISS")" = 1 ]
check "installer install: the status keeps the installed line" [ "$(sed -n 1p "$ST")" = "installed=0.0.1 at 2026-01-01 00:00" ]
check "installer install: the status names the refusal, no waiting line" eval '[ "$(wc -l < "$ST")" = 2 ] && [[ "$(sed -n 2p "$ST")" == "refused=INSTALL REFUSED: missing $MISS at "* ]]'

run --rollback
check "installer rollback: a missing server Mods folder gives exit 4" [ "$rc" = 4 ]
check "installer rollback: it refuses before the wait (under 5 s)" [ "$secs" -lt 5 ]
check "installer rollback: nothing was backed up or copied" untouched
check "installer rollback: the log holds the second refusal" [ "$(refusals "$MISS")" = 2 ]

mv "$SV/Mods.away" "$SV/Mods"; mv "$CL/Mods" "$CL/Mods.away"
run --preflight
check "installer client: a missing client Mods folder gives exit 4" [ "$rc" = 4 ]
check "installer client: it prints only the missing client folder" [ "$out" = "PREFLIGHT REFUSED: missing $CL/Mods" ]
mv "$CL/Mods.away" "$CL/Mods"

run --preflight
check "installer preflight: both folders present (no pack folder) give exit 0" [ "$rc" = 0 ]
check "installer preflight: it prints the ok line only" [ "$out" = "PREFLIGHT OK: both install folders exist" ]
check "installer preflight: the ok check writes nothing" eval 'untouched && [ "$(refusals "$MISS")" = 2 ] && [ ! -e "$CL/Modpacks" ]'

run --prefight
check "installer option: an unknown option gives exit 6" [ "$rc" = 6 ]
check "installer option: it names the option and the three valid ones" [ "$out" = "unknown option '--prefight': use no option, --rollback or --preflight" ]
check "installer option: nothing was backed up or copied" untouched

if tasklist 2>/dev/null | grep -i -q "township"; then
  echo "SKIP installer install: the game or the server is running, so the full scratch installs and rollbacks were not tried"
else
  TO=90; run
  check "installer install: with both folders it installs as before" eval '[ "$rc" = 0 ] && last_is "test: scratch"'
  check "rename install: both Mods hold the new name and the kit, no pack folder made" eval 'both $NEW new && both $KIT kit && [ ! -e "$PACK" ]'
  check "rename install: the old QuickshotRing.dll is gone from both Mods" both $OLD -
  check "rename install: the backup holds the old name on both sides and nothing else" eval 'B=$(newest); [ "$(cat "$B/client/$OLD")" = old ] && [ "$(cat "$B/server/$OLD")" = old ] && [ ! -e "$B/client/$NEW" ] && [ ! -e "$B/server/$NEW" ] && [ ! -e "$B/client/$KIT" ]'
  check "installer install: the status reads installed=9.9.9" [ "$(sed -n 's/^installed=\([^ ]*\).*/\1/p' "$ST")" = 9.9.9 ]
  run
  check "rename install: a second install of the same build is already installed" eval '[ "$rc" = 0 ] && last_is "ALREADY INSTALLED: 9.9.9"'

  run --rollback
  check "rename rollback: it rolls back" eval '[ "$rc" = 0 ] && [[ "$out" == *"INSTALLED rollback to auto-"* ]]'
  check "rename rollback: QuickshotRing.dll is back on both sides as it was" both $OLD old
  check "rename rollback: the new name and the kit are gone from both Mods" eval 'both $NEW - && both $KIT -'
  check "rename rollback: its own backup holds the new name and the kit" eval 'B=$(newest); [ "$(cat "$B/client/$NEW")" = new ] && [ "$(cat "$B/server/$KIT")" = kit ] && [ ! -e "$B/client/$OLD" ]'

  run   # old -> new again, then a newer build without a kit over it
  stage 9.9.10 newer
  run
  check "rename install: a build over the new name replaces it and drops a kit it does not carry" eval '[ "$rc" = 0 ] && both $NEW newer && both $KIT - && both $OLD -'
  run --rollback
  check "rename rollback: a backup of the new name puts it and its kit back, the old name stays away" eval '[ "$rc" = 0 ] && both $NEW new && both $KIT kit && both $OLD -'
  check "installer backups: at most five automatic backups are kept" [ "$(ls -1d "$R/bk"/auto-* | wc -l)" -le 5 ]
fi

rm -rf "$R"
if [ $fail = 0 ]; then echo "INSTALLER TEST ALL PASS ($pass)"; else echo "INSTALLER TEST FAILED $fail of $((pass+fail))"; exit 1; fi
