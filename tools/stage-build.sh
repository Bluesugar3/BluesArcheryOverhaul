#!/usr/bin/env bash
# Stage a Blue's Archery Overhaul build for install: set the version, run the tests, do a clean rebuild, and only if the tests
# pass and the rebuild has 0 warnings and 0 errors copy the DLL, the play-test kit's DLL and the test menu into
# tools/.install/staged (replacing any older staged build, so a waiting installer always installs the latest). Never touches the
# game or the server, except for one small status text file in the server folder that the test menu reads.
#   tools/stage-build.sh <version> <friends: yes|no> "<what changed>" "<what to test>"
# Then start tools/install-when-closed.sh in the background (it also removes the old QuickshotRing.dll from both Mods).
set -u
cd /c/Users/dmand/OneDrive/Desktop/attmod || exit 1
VER="${1:?version, e.g. 0.16.1}"; FRIENDS="${2:?yes or no}"; CHANGED="${3:?what changed}"; TEST="${4:?what to test}"
S="/c/Users/dmand/OneDrive/Desktop/A Township Tale(server"
I=tools/.install; mkdir -p "$I"
case "$VER" in [0-9]*.[0-9]*.[0-9]*) ;; *) echo "STAGE FAILED: '$VER' is not a version"; exit 2;; esac

# 1. the version every log line of this build will show (the play-test kit takes the same version from it when it is built)
sed -i -E "s/(internal const string Version = \")[^\"]+(\")/\1$VER\2/" BluesArcheryOverhaul/Main.cs
grep -q "internal const string Version = \"$VER\"" BluesArcheryOverhaul/Main.cs || { echo "STAGE FAILED: could not set the version in Main.cs"; exit 2; }

# 2. the gate: both test runners, then a clean rebuild of the mod and of the play-test kit
T=$(dotnet run --project BluesArcheryOverhaul.Tests -c Release 2>&1 | tail -1)
case "$T" in "ALL PASS"*) ;; *) echo "STAGE FAILED: tests: $T"; exit 3;; esac
TK=$(dotnet run --project BluesArcheryTestKit.Tests -c Release 2>&1 | tail -1)
case "$TK" in "ALL PASS"*) ;; *) echo "STAGE FAILED: the test kit's tests: $TK"; exit 3;; esac
for P in BluesArcheryOverhaul BluesArcheryTestKit; do
  B=$(dotnet build "$P" -c Release --no-incremental 2>&1)
  echo "$B" | grep -q " 0 Warning(s)" && echo "$B" | grep -q " 0 Error(s)" || { echo "STAGE FAILED: the rebuild of $P has warnings or errors"; echo "$B" | grep -E "warning|error" | head -5; exit 3; }
done
DLL=BluesArcheryOverhaul/bin/Release/BluesArcheryOverhaul.dll; KIT=BluesArcheryTestKit/bin/Release/BluesArcheryTestKit.dll
[ -f "$DLL" ] && [ -f "$KIT" ] || { echo "STAGE FAILED: no DLL"; exit 3; }

# 3. stage it (built aside, then swapped in, so a waiting installer never sees half a build)
N="$I/staged.new"; rm -rf "$N"; mkdir -p "$N"
cp "$DLL" "$N/BluesArcheryOverhaul.dll" && cp "$KIT" "$N/BluesArcheryTestKit.dll" && cp BluesArcheryTestKit/tools/TestBuildMenu.ps1 "$N/" && cp "BluesArcheryTestKit/tools/Test Build Menu.cmd" "$N/" || { echo "STAGE FAILED: copy"; exit 4; }
[ -f "$I/post-install.sh" ] && mv "$I/post-install.sh" "$N/post-install.sh"   # optional one-off settings step for this build
MD5=$(md5sum "$N/BluesArcheryOverhaul.dll" | cut -c1-32); KMD5=$(md5sum "$N/BluesArcheryTestKit.dll" | cut -c1-32)
{ echo "version=$VER"; echo "md5=$MD5"; echo "kitmd5=$KMD5"; echo "friends=$FRIENDS"; echo "changed=$CHANGED"; echo "test=$TEST"; echo "tests=$T; kit $TK"; echo "staged=$(date '+%Y-%m-%d %H:%M:%S')"; } > "$N/manifest.txt"
rm -rf "$I/staged.old"; [ -d "$I/staged" ] && mv "$I/staged" "$I/staged.old"; mv "$N" "$I/staged"; rm -rf "$I/staged.old"

# 4. the menu's status line (a text file, safe to write while the server runs)
ST="$S/BluesArcheryOverhaul-install-status.txt"
INST=$(grep -m1 '^installed=' "$ST" 2>/dev/null || grep -m1 '^installed=' "$S/QuickshotRing-install-status.txt" 2>/dev/null || echo "installed=unknown")
{ echo "$INST"; echo "waiting=$VER"; } > "$ST" 2>/dev/null
echo "STAGED $VER ($T, kit $TK, 0 warnings) md5 ${MD5:0:12}"
