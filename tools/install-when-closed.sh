#!/usr/bin/env bash
# Install the staged QuickshotRing build (tools/.install/staged, made by tools/stage-build.sh) as soon as BOTH the game and the
# server are closed. It never closes, stops or restarts anything and never copies while either is running.
#   tools/install-when-closed.sh              wait, back up what is installed, install the staged build, verify
#   tools/install-when-closed.sh --rollback   wait, then put back the newest backup in backups/auto-*
#   tools/install-when-closed.sh --preflight  only check that the two install folders exist (exit 0 yes, 4 no); writes nothing
# Installs to the game's Mods and the server's Mods (the launcher's pack copy was dropped 2026-11-19: the user, "skip the pack folder"), checks both match the staged build, copies
# the test menu to the server folder if it changed, keeps the last five backups, and writes tools/.install/install.log.
# Refuses (exit 4) before backing up or copying anything while one of the two folders is missing, at start and after the wait.
set -u
cd /c/Users/dmand/OneDrive/Desktop/attmod || exit 1
# QR_CLIENT / QR_SERVER / QR_STATE / QR_BACKUPS exist only so the script can be tried on scratch folders.
C="${QR_CLIENT:-/c/Users/dmand/OneDrive/Desktop/A Township Tale (client}"
S="${QR_SERVER:-/c/Users/dmand/OneDrive/Desktop/A Township Tale(server}"
BK="${QR_BACKUPS:-backups}"
I="${QR_STATE:-tools/.install}"; mkdir -p "$I"; LOG="$I/install.log"; ST="$S/QuickshotRing-install-status.txt"
TARGETS=("$C/Mods/QuickshotRing.dll" "$S/Mods/QuickshotRing.dll")
say() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*" | tee -a "$LOG"; }
running() { tasklist 2>/dev/null | grep -i -q "township"; }
md5() { md5sum "$1" 2>/dev/null | cut -c1-32; }

# every target's folder must exist: a missing one would give a half
# install, so this refuses before anything is backed up or copied, at start and again after the wait
first_missing() { for t in "${TARGETS[@]}"; do [ -d "$(dirname "$t")" ] || { dirname "$t"; return 0; }; done; return 1; }
refuse_missing() {
  say "INSTALL REFUSED: missing $1"
  if [ -d "$S" ]; then
    INST=$(grep -m1 '^installed=' "$ST" 2>/dev/null || echo "installed=unknown")
    { echo "$INST"; echo "refused=INSTALL REFUSED: missing $1 at $(date '+%Y-%m-%d %H:%M')"; } > "$ST"
  fi
  exit 4
}
MODE="${1:-install}"
case "$MODE" in
  install|--rollback) ;;
  --preflight) if d=$(first_missing); then echo "PREFLIGHT REFUSED: missing $d"; exit 4; fi; echo "PREFLIGHT OK: both install folders exist"; exit 0;;
  *) echo "unknown option '$MODE': use no option, --rollback or --preflight"; exit 6;;
esac
if d=$(first_missing); then refuse_missing "$d"; fi

# one waiting installer at a time: a second one leaves, the first installs whatever is staged when the game closes
if [ -f "$I/waiting.pid" ] && kill -0 "$(cat "$I/waiting.pid")" 2>/dev/null; then echo "an installer is already waiting; it will install the latest staged build"; exit 0; fi
echo $$ > "$I/waiting.pid"; trap 'rm -f "$I/waiting.pid"' EXIT

if running; then say "WAITING: the game or the server is running; it installs once both are closed"; fi
tries=0
while :; do
  if running; then tries=0; else tries=$((tries+1)); fi
  [ $tries -ge 3 ] && break            # closed on three checks in a row (about 10 s), not a restart in between
  sleep 4
  [ $SECONDS -gt 86400 ] && { say "gave up after 24 hours: still running"; exit 2; }
done

if [ "$MODE" = "--rollback" ]; then
  SRC=$(ls -1d "$BK"/auto-* 2>/dev/null | sort | tail -1)
  [ -n "$SRC" ] && [ -f "$SRC/client/QuickshotRing.dll" ] || { say "ROLLBACK FAILED: no backup in $BK/auto-*"; exit 4; }
  VER="rollback to $(basename "$SRC")"; FILE="$SRC/client/QuickshotRing.dll"; WANT=$(md5 "$FILE"); FRIENDS=yes; CHANGED="rolled back"; TEST="-"
else
  SRC="$I/staged"; M="$SRC/manifest.txt"; FILE="$SRC/QuickshotRing.dll"
  [ -f "$M" ] && [ -f "$FILE" ] || { say "NOTHING STAGED: run tools/stage-build.sh first"; exit 4; }
  VER=$(sed -n 's/^version=//p' "$M"); WANT=$(sed -n 's/^md5=//p' "$M"); FRIENDS=$(sed -n 's/^friends=//p' "$M")
  CHANGED=$(sed -n 's/^changed=//p' "$M"); TEST=$(sed -n 's/^test=//p' "$M")
  [ "$(md5 "$FILE")" = "$WANT" ] || { say "INSTALL REFUSED: the staged file does not match its manifest (staging in progress?)"; exit 4; }
fi
if d=$(first_missing); then refuse_missing "$d"; fi    # again: a folder may have gone during the wait

same=1; for t in "${TARGETS[@]}"; do [ "$(md5 "$t")" = "$WANT" ] || same=0; done
if [ $same = 1 ]; then say "ALREADY INSTALLED: $VER (${WANT:0:12})"; exit 0; fi

# back up what is installed now
WAS=$(sed -n 's/^installed=\([^ ]*\).*/\1/p' "$ST" 2>/dev/null | head -1); [ -n "$WAS" ] || WAS=unknown
B="$BK/auto-$(date +%Y-%m-%d-%H%M%S)-v$WAS"; mkdir -p "$B/client" "$B/server"
cp "${TARGETS[0]}" "$B/client/" 2>>"$LOG"; cp "${TARGETS[1]}" "$B/server/" 2>>"$LOG"
cp "$C/UserData/QuickshotRing.cfg" "$B/client/" 2>>"$LOG"; cp "$S/UserData/QuickshotRing.cfg" "$B/server/" 2>>"$LOG"
cp "$S/TestBuildMenu.ps1" "$B/server/" 2>/dev/null
say "backed up $WAS to $B"

if running; then say "INSTALL ABORTED: the game or the server started again; nothing was copied. Start the installer again."; exit 5; fi
ok=1
for t in "${TARGETS[@]}"; do
  cp -f "$FILE" "$t" 2>>"$LOG"
  [ "$(md5 "$t")" = "$WANT" ] && say "ok   $t" || { say "FAIL $t"; ok=0; }
done
if [ $ok != 1 ]; then say "INSTALL INCOMPLETE: a copy differs (file locked?). Backup of the previous files: $B"; exit 3; fi

if [ "$MODE" != "--rollback" ]; then
  for f in "TestBuildMenu.ps1" "Test Build Menu.cmd"; do
    if [ -f "$SRC/$f" ] && [ "$(md5 "$SRC/$f")" != "$(md5 "$S/$f")" ]; then cp -f "$SRC/$f" "$S/$f" && say "test menu updated: $f"; fi
  done
  if [ -f "$SRC/post-install.sh" ]; then bash "$SRC/post-install.sh" 2>&1 | tee -a "$LOG"; mv "$SRC/post-install.sh" "$SRC/post-install.done"; fi
  echo "installed=$VER at $(date '+%Y-%m-%d %H:%M')" > "$ST"
else
  echo "installed=$VER at $(date '+%Y-%m-%d %H:%M')" > "$ST"
fi

# keep the last five automatic backups
ls -1d "$BK"/auto-* 2>/dev/null | sort | head -n -5 | while read -r old; do rm -rf "$old" && say "old backup removed: $old"; done

say "INSTALLED $VER (${WANT:0:12}) on the game and the server"
say "changed: $CHANGED"
say "test: $TEST"
[ "$FRIENDS" = "yes" ] && say "FRIENDS NEED THIS FILE TOO: $C/Mods/QuickshotRing.dll"
exit 0
