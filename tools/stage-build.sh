#!/usr/bin/env bash
# Stage a QuickshotRing build for install: set the version, run the tests, do a clean rebuild, and only if the tests pass and
# the rebuild has 0 warnings and 0 errors copy the DLL and the test menu into tools/.install/staged (replacing any older staged
# build, so a waiting installer always installs the latest). Never touches the game or the server, except for one small status
# text file in the server folder that the test menu reads.
#   tools/stage-build.sh <version> <friends: yes|no> "<what changed>" "<what to test>"
# Then start tools/install-when-closed.sh in the background.
set -u
cd /c/Users/dmand/OneDrive/Desktop/attmod || exit 1
VER="${1:?version, e.g. 0.16.1}"; FRIENDS="${2:?yes or no}"; CHANGED="${3:?what changed}"; TEST="${4:?what to test}"
S="/c/Users/dmand/OneDrive/Desktop/A Township Tale(server"
I=tools/.install; mkdir -p "$I"
case "$VER" in [0-9]*.[0-9]*.[0-9]*) ;; *) echo "STAGE FAILED: '$VER' is not a version"; exit 2;; esac

# 1. the version every log line of this build will show
sed -i -E "s/(MelonInfo\(typeof\(QuickshotRing\.Main\), \"QuickshotRing\", \")[^\"]+(\")/\1$VER\2/" QuickshotRing/Main.cs
grep -q "\"QuickshotRing\", \"$VER\"" QuickshotRing/Main.cs || { echo "STAGE FAILED: could not set the version in Main.cs"; exit 2; }

# 2. the gate: tests, then a clean rebuild
T=$(dotnet run --project QuickshotRing.Tests -c Release 2>&1 | tail -1)
case "$T" in "ALL PASS"*) ;; *) echo "STAGE FAILED: tests: $T"; exit 3;; esac
B=$(dotnet build QuickshotRing -c Release --no-incremental 2>&1)
echo "$B" | grep -q " 0 Warning(s)" && echo "$B" | grep -q " 0 Error(s)" || { echo "STAGE FAILED: the rebuild has warnings or errors"; echo "$B" | grep -E "warning|error" | head -5; exit 3; }
DLL=QuickshotRing/bin/Release/QuickshotRing.dll
[ -f "$DLL" ] || { echo "STAGE FAILED: no DLL"; exit 3; }

# 3. stage it (built aside, then swapped in, so a waiting installer never sees half a build)
N="$I/staged.new"; rm -rf "$N"; mkdir -p "$N"
cp "$DLL" "$N/QuickshotRing.dll" && cp tools/TestBuildMenu.ps1 "$N/" && cp "tools/Test Build Menu.cmd" "$N/" || { echo "STAGE FAILED: copy"; exit 4; }
[ -f "$I/post-install.sh" ] && mv "$I/post-install.sh" "$N/post-install.sh"   # optional one-off settings step for this build
MD5=$(md5sum "$N/QuickshotRing.dll" | cut -c1-32)
{ echo "version=$VER"; echo "md5=$MD5"; echo "friends=$FRIENDS"; echo "changed=$CHANGED"; echo "test=$TEST"; echo "tests=$T"; echo "staged=$(date '+%Y-%m-%d %H:%M:%S')"; } > "$N/manifest.txt"
rm -rf "$I/staged.old"; [ -d "$I/staged" ] && mv "$I/staged" "$I/staged.old"; mv "$N" "$I/staged"; rm -rf "$I/staged.old"

# 4. the menu's status line (a text file, safe to write while the server runs)
ST="$S/QuickshotRing-install-status.txt"
INST=$(grep -m1 '^installed=' "$ST" 2>/dev/null || echo "installed=unknown")
{ echo "$INST"; echo "waiting=$VER"; } > "$ST" 2>/dev/null
echo "STAGED $VER ($T, 0 warnings) md5 ${MD5:0:12}"
