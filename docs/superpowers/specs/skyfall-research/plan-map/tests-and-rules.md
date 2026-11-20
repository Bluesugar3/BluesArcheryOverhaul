# Skyfall plan map: tests-and-rules (2026-11-19, read-only)

## Map
All paths are under C:\Users\dmand\OneDrive\Desktop\attmod. Nothing was edited, built or run. Line numbers are from the files as they are now. No test file or Unity-free source has changed since the 0.17.15 build was staged (newest edits 2026-11-18 20:47:08; staged 20:47:53).

====================================================================
(1) QuickshotRing.Tests
====================================================================

1a. How the Unity-free files get into the tests
- QuickshotRing.Tests\QuickshotRing.Tests.csproj:1-21. There is no ProjectReference: the source files are linked with Compile items, so `internal` types are visible without InternalsVisibleTo. Lines 6-20:
```
  <ItemGroup>
    <Compile Include="..\QuickshotRing\Rules.cs" />
    <Compile Include="..\QuickshotRing\Build.cs" />
    <Compile Include="..\QuickshotRing\Wire.cs" />
    <Compile Include="..\QuickshotRing\Release.cs" />
    <Compile Include="..\QuickshotRing\Spectral.cs" />
    <Compile Include="..\QuickshotRing\Charge.cs" />
    <Compile Include="..\QuickshotRing\Path.cs" />
    <Compile Include="..\QuickshotRing\Preview.cs" />
    <Compile Include="..\QuickshotRing\Lectern.cs" />
    <Compile Include="..\QuickshotRing\Timing.cs" />
    <Compile Include="..\QuickshotRing\Homing.cs" />
    <Compile Include="..\QuickshotRing\Range.cs" />
    <Compile Include="..\QuickshotRing\RangeBuild.cs" />
  </ItemGroup>
```
- The fourteenth file needs one more `<Compile Include="..\QuickshotRing\<File>.cs" />` after line 19. New test files in QuickshotRing.Tests\ need no csproj entry: the SDK's default glob picks them up, as it does for the 34 existing ones.
- The csproj sets only `<OutputType>Exe</OutputType>` (line 4). Everything else comes from Directory.Build.props, which applies to both projects:
  - :10 `<TargetFramework>net472</TargetFramework>`
  - :11 `<LangVersion>latest</LangVersion>`
  - :12 `<Nullable>enable</Nullable>`
  - :13 GenerateAssemblyInfo false; :15 AppendTargetFrameworkToOutputPath false.
- The same props also add the game, Unity, MelonLoader and SyncLib references to the TEST project (:20-23), all with Private=false.
  - Confirmed: QuickshotRing.Tests\obj\Release\QuickshotRing.Tests.csproj.AssemblyReference.cache lists UnityEngine.CoreModule.dll, MelonLoader.dll and Root.Township.dll.
  - bin\Release holds only QuickshotRing.Tests.exe and its .exe.config.
- The mod compiles every top-level *.cs file, the Unity-free ones included: QuickshotRing\QuickshotRing.csproj is just `<Project Sdk="Microsoft.NET.Sdk" />`. There are 62 files and none in subfolders.

1b. Registration and run (QuickshotRing.Tests\Program.cs)
```
internal static class Program {                 // :4, global namespace, `using QuickshotRing;` at :2
    static int total, failed;
    internal static void Check(string name, bool ok) {   // :7
        total++;
        if (ok) return;
        failed++;
        Console.WriteLine("FAIL " + name);
    }
    internal static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;   // :14
    static int Main(string[] args) {             // :16
        if (args.Length >= 3 && args[0] == "--plan") return PlanDump.Run(...);   // :18, the only argument parsed
        ...                                      // :20-60 inline checks (BlastMath at :47-52)
        WireTests.Run(); ... RangeBuildTests.Run();      // :62-92, one line per class; the last is RangeBuildTests.Run() at :92
        Console.WriteLine(failed == 0 ? $"ALL PASS ({total})" : $"FAILED {failed} of {total}");   // :94
        return failed == 0 ? 0 : 1;              // :95
```
- Register a new class by adding `<Name>Tests.Run();` after :92 and before :94.
- N is the number of Check calls actually executed (calls inside loops count once per pass).
- Each failure prints "FAIL <name>". The last line is "ALL PASS (N)" or "FAILED f of N"; the exit code is 0 or 1.
- Last recorded result: "ALL PASS (2042)", in tools\.install\staged\manifest.txt (`tests=ALL PASS (2042)`, 0.17.15) and CLAUDE.md:117.

1c. Assertion helpers
- Shared:
  - `Program.Check(string name, bool ok)` (Program.cs:7);
  - `Program.Near(float a, float b)`, absolute difference < 1e-4f (:14).
  - Both are reached through `using static Program;`.
- Per-file private copies:
  - LooksTests.cs:17 and G4Tests.cs:22: `static bool Near3(float a, float b, float eps = 1e-3f)`;
  - LecternTests.cs:17: `static bool Near(float a, float b, float eps = 1e-4f)`;
  - RangeTests.cs:273: `static bool Near1(float a, float b)` (1e-3f);
  - SeekLookTests.cs:19: `static bool Close(double a, double b, double eps = 1e-4)`.
- Shared fake: FakeBits.cs:5 `internal sealed class FakeBits : IBitWriter, IBitReader`.
  - Members: `Count`, `Write(uint,int)`, `Seal()` (pads to a byte), `Read(int)` (reads zeros past the end), `Finished`, `BitsLeft`, `string Dump()`.
- Reusable test worlds:
  - `HomingH3Tests.Box` (HomingH3Tests.cs:203, internal static class);
  - `HomingH3Tests.World` (:222, internal sealed class, with `Ground(double x, double z)` and `Ground(HVec)`), aliased in HomingH5Tests.cs:6-7 as `using Box = HomingH3Tests.Box; using World = HomingH3Tests.World;`;
  - `RangeBuildTests.FakeProbe : IRangeProbe` (RangeBuildTests.cs:255).
- Random scenes use a fixed seed: `var rng = new Random(5);` (RicochetBounceTests.cs:77; also 11 at :105 and 23 at :183).
- Environment variables only add printing: HOMING_DEBUG, HOMING_REPORT, HOMING_TRACE, QSR_PRINT_SAMPLE.

1d. Skeleton of a recent test file
H4Tests.cs:1-11, 15-16 (the shortest recent one):
```
using System;
using QuickshotRing;
using static Program;

// H4 (fixes after ...): one-line purpose of the file.
internal static class H4Tests {
    internal static void Run() {
        CoreCount();
        Refund();
        GoldenBlock();
    }
    static void CoreCount() {
        Check("H4: CoreArrows is at least 1", HomingValues.CoreCount(0) == 1 && HomingValues.CoreCount(-3) == 1);
```
RicochetBounceTests.cs:1-27 follows the same shape:
- usings System, System.Collections.Generic, System.Linq, QuickshotRing, `using static Program;`;
- a header comment quoting the owner;
- `internal static class RicochetBounceTests { internal static void Run() { Rules(); Chain(); ... Later(); } }`;
- section dividers `// ---------------------------------------------------------------- the rules`;
- private `static void Topic()` methods;
- check names written "topic: plain sentence";
- nested helper types such as `sealed class Scene : IJumpWorld` (:132) and `sealed class LaterWorld : IJumpWorld` (:396).

1e. Run command
- From the attmod folder: `dotnet run --project QuickshotRing.Tests -c Release` (CLAUDE.md:117, tools\stage-build.sh:20).
- From the test folder: `dotnet run -c Release` (csproj:2).
- **No single class can be run.** Main calls every Run() unconditionally; only `--plan <heightmap> <out> [lanes] [fixed]` is parsed (Program.cs:18). Running one class means editing Program.cs.
- The staging gate (tools\stage-build.sh):
  - :16 rewrites the MelonInfo version in Main.cs with sed;
  - :20-21: the last line of `dotnet run` must start with "ALL PASS";
  - :22-23: `dotnet build QuickshotRing -c Release --no-incremental` must show " 0 Warning(s)" and " 0 Error(s)".
- Because the Unity-free file is also compiled into the mod, any warning in it fails staging: a nullable warning (Nullable is on), or CS0649 for a field only the tests assign. Precedent: Release.cs:89-100 wraps `struct ReleaseFacts` in `#pragma warning disable CS0649` / `restore`.

====================================================================
(2) Unity-free building blocks
====================================================================

HVec (Homing.cs:699-712): a mutable struct; there is no readonly struct form.
```
    internal struct HVec {
        public double X, Y, Z;
        public HVec(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static HVec operator +(HVec a, HVec b) => new HVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static HVec operator -(HVec a, HVec b) => new HVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static HVec operator *(HVec a, double k) => new HVec(a.X * k, a.Y * k, a.Z * k);
        public static double Dot(HVec a, HVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static HVec Cross(HVec a, HVec b) => new HVec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(Dot(this, this));
        public HVec Normalized { get { double l = Length; return l > 1e-12 ? this * (1.0 / l) : new HVec(0, 0, 0); } }
        public bool Finite => !double.IsNaN(X) && ... && !double.IsInfinity(Z);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", X, Y, Z);
    }
```
- HVec does NOT have: unary minus, `double * HVec` (only `HVec * double`), division, Zero, Distance or Lerp.
- Other HVec statics:
  - `HomingChain.LevelTurn(HVec from, HVec want, double max)` (Homing.cs:182);
  - `HomingChain.Level(HVec want)` (:201);
  - `TrailCurve.At(HVec[] hist, int count, HVec next, double back)` (Spectral.cs:1220);
  - `TrailCurve.Heading(...)` (:1231).
- The Unity bridges live in game files:
  - Seek.cs:128 `static Vector3 V(HVec h)` (private);
  - Seek.cs:129 `internal static HVec H(Vector3 v) => new HVec(v.x, v.y, v.z);`;
  - Volley.cs:949, a private `V(HVec)`.

LookSeed (Spectral.cs:117-129)
```
    /// <summary>Looks only: a repeatable number from a seed (the release's arrow id), an index and a salt, so a look that wants
    /// variety (Shatter's fan, Ricochet's arcs) is the same every time it is asked and on every machine. Never used for anything
    /// that counts.</summary>
    internal static class LookSeed {
        /// <summary>In [0, 1).</summary>
        internal static float Unit(uint seed, int index, int salt) {
            unchecked {
                uint h = seed * 0x9E3779B1u ^ (uint)(index + 1) * 0x85EBCA6Bu ^ (uint)(salt + 1) * 0xC2B2AE35u;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }
    }
```
- Salts in use: SplinterPattern.Fan uses 1-4 (Spectral.cs:826-830); JumpArc.Shape uses 11 and 12 (:981-982).
- This is the only `unchecked` block in the mod.
- Tests: LooksTests.cs:21-38 check that it is in range, repeatable, at least 190 distinct values out of 200, and has a mean near 0.5.
- The only other deterministic hash is SettingsChecksum (Rules.cs:628-647):
  - `public static uint Compute(IEnumerable<KeyValuePair<string,string>>)`: FNV-1a 32 over the UTF-8 bytes of "key=value\n", pairs sorted ordinally;
  - `public static byte Short(uint c)`: an xor-fold to one byte.
  - `Cfg.SharedShort = Short(SharedChecksum)` (Cfg.cs:54). On the wire it is 6 bits: `BuildBits.CheckMask = 0x3F` (Build.cs:99), compared at Builds.cs:347.
  - `SpawnRule.Hash(string?)` (Build.cs:448) only looks a name up in a table; it is not a hash function.
- `UnityEngine.Random` is used only in game-side looks (ChargeFx, Fx, HolyArrow, Quickdraw4, SnapWeight, VolleyFx). It is not repeatable across machines.

BounceQueue<T> (Spectral.cs:552-585)
- Full public surface:
  - `internal sealed class BounceQueue<T>`
  - `public const int Cap = 512;` (:557). It is a constant: there is no constructor argument.
  - `public int Count` (:561)
  - `public bool Add(T item, float dueAt)` (:565): false once Cap items are waiting.
  - `public int TakeDue(float now, List<T> into)` (:573): moves items in the order queued and returns how many it moved.
  - `public void Clear()` (:584)
- An item is due when `!(d > now) || float.IsInfinity(d)`, so NaN and ±Infinity are due at once.
```
        public int TakeDue(float now, List<T> into) {
            int taken = 0, kept = 0;
            for (int i = 0; i < items.Count; i++) {
                float d = due[i];
                if (!(d > now) || float.IsInfinity(d)) { into.Add(items[i]); taken++; continue; }
                items[kept] = items[i]; due[kept] = d; kept++;
            }
            if (kept < items.Count) { items.RemoveRange(kept, items.Count - kept); due.RemoveRange(kept, due.Count - kept); }
            return taken;
        }
```
- Game-side pattern (Ricochet.cs:350-369):
```
        static readonly BounceQueue<Bounce> waiting = new BounceQueue<Bounce>();
        static readonly List<Bounce> dueNow = new List<Bounce>();
        static bool waitingFullWarned;
        static void WaitingFull(uint release) { if (waitingFullWarned) return; waitingFullWarned = true; MelonLogger.Warning($"... {BounceQueue<Bounce>.Cap} ... (said once a session)"); }
        internal static void BounceTick() {
            if (waiting.Count == 0) return;
            dueNow.Clear();
            if (waiting.TakeDue(Time.time, dueNow) > 0)
                foreach (Bounce b in dueNow) DealBounce(b);
            dueNow.Clear();
        }
```
- Where the tick goes in Main.cs:
  - :145 `TBounce = Timing.Section("Main.OnUpdate / Ricochet.BounceTick (waiting bounces)")`;
  - :185 `try { using (Timing.Begin(TBounce)) Ricochet.BounceTick(); } catch (Exception e) { MelonLogger.Error(...); }`;
  - :205 is the headless early return (`if (ring == null || !ring.activeSelf) return;`), so a server tick goes before it.
- Tests: RicochetBounceTests.cs:369-392 cover order, due exactly at its time, broken times, the cap, refill and clear.

BlastMath (Rules.cs:61-70)
```
    internal static class BlastMath {
        public static float Fraction(float distance, float fullRadius, float radius, float edgeFraction) {
            if (float.IsNaN(distance) || distance > radius) return 0f;
            if (distance <= fullRadius || radius <= fullRadius) return 1f;
            float t = (distance - fullRadius) / (radius - fullRadius);
            return 1f + (edgeFraction - 1f) * t;
        }
    }
```
- Tests: Program.cs:47-52 (centre, inside the full radius, edge, midway, outside, NaN).
- Uses:
  - HolyArrow.cs:489, 745, 776, 791, 820;
  - ShatterBlast.Share (Spectral.cs:942-943);
  - the start-up sanity check at Main.cs:119-122.

ArcModel (Spectral.cs:6-45): `internal static class`, the game's flight in closed form (constant speed; gravity turns the direction).
- Members:
  - `internal const double MaxElevation = 89.9 * Math.PI / 180.0;` (:8). Elevation is clamped to ±89.9°.
  - `internal static void Point(double speed, double elevation, double t, double g, out double along, out double up)` (:17)
  - `internal static void Velocity(double speed, double elevation, double t, double g, out double along, out double up)` (:24)
  - `internal static double TimeToAlong(double speed, double elevation, double along, double g)` (:34): NaN if the arrow never gets that far.
- Units: elevation in radians; g is a positive magnitude. `g <= 0` or `speed <= 0` gives a straight line.
- Game call: Volley.cs:986-1004.
  - `Aim` takes the elevation as `Mathf.Asin(Mathf.Clamp(d.y,-1,1))` of the normalised direction, plus the flat horizontal unit vector.
  - `static float Gravity => -Physics.gravity.y;` (:996).
- Tests: SpectralTests.cs:48-62.

HitBatch (Spectral.cs:270-294): one tick's hits per creature, keyed by int.
- `internal sealed class HitBatch` with:
  - `Add(int key, float share)`, `Add(int key, float share, bool jumps)`;
  - `IList<float> Shares(int key)`;
  - `Count`, `IEnumerable<int> Keys`;
  - `float Sum(int key)`, `int Hits(int key)`, `Clear()`.

Settings value holder: HomingValues
- Shape (Homing.cs:14-49): a sealed class with public mutable fields and inline defaults; the shipped defaults live here, once.
```
    /// <summary>[SeekingVolley] numbers (Seek.cs reads them from the settings; the defaults are the design's). A plain class: the
    /// game side builds one per settings load and never changes it after.</summary>
    internal sealed class HomingValues {
        public float LockMinMetres = 3f, LockRangeMetres = 60f;
        ...
        public int CoreArrows = 5;
        public static int CoreCount(int setting) => setting < 1 ? 1 : setting > 12 ? 12 : setting;   // :36, a clamp helper
        public ChainMode ChainLocks = ChainMode.Ghosts;
        public float ChainMaxLifeSeconds = 8f;
        public float FlightSeconds => ChainLocks == ChainMode.Off ? MaxLifeSeconds : Math.Max(MaxLifeSeconds, ChainMaxLifeSeconds);   // :46
        public static HomingValues Default => new HomingValues();   // :48
    }
```
- Filled from settings in Seek.cs:
  - :27-35, static entry fields `static MelonPreferences_Entry<float> lockMin = null!, ...;` and `static HomingValues? cache;`.
  - `internal static void Init()` (:39-90):
    - `HomingValues d = HomingValues.Default; var cat = Cfg.Category("SeekingVolley");`
    - a local helper `F(key, value, text, shared)` that calls `cat.CreateEntry(key, value, description: text)` and wraps it in `Cfg.Shared(e)` when shared; each default is passed as `d.Field`;
    - `Cfg.Reloaded += () => cache = null;` (:89).
  - `internal static HomingValues Values { get; }` (:94-123): `if (lockMin == null) return HomingValues.Default; if (cache != null) return cache;`, then `new HomingValues { X = Mathf.Max(0f, x.Value), ... }` with clamps, `return cache = v;`. An unknown enum text is warned once (:117-120).
- Cfg API (Cfg.cs):
  - :24 `internal static event Action? Reloaded;`
  - :34 `internal static MelonPreferences_Category Category(string name)`
  - :44 `internal static MelonPreferences_Entry<T> Shared<T>(MelonPreferences_Entry<T> entry)`, which prefixes "[shared] " and joins the checksum;
  - :59 `internal static void Ready()`.
- Call order in Main.OnInitializeMelon (Main.cs:72):
  - every Init call runs at :82-108 (`Seek.Init();` is :97);
  - `Cfg.Ready()` is :109;
  - `SelfCheck.Run("start-up")` is :116.
- SelfCheck (SelfCheck.cs):
  - `internal static void Run(string when)` (:42);
  - holders are read as `sink = Seek.Values; sets++;` (:48);
  - leaf checks end with `sink = TestRange.Check(); sets++;` (:72), just before `FactNumbers numbers = Paths.Numbers(); sets++;` (:73);
  - Check() methods are `internal static int Check()` (GoldHold.cs:274, SeekLook.cs:442, TestRange.cs:538);
  - the "A reads B" graph comment is at :1-39.
- How the tests build one:
  - `static readonly HomingValues V = HomingValues.Default;` (HomingH5Tests.cs:15; HomingTests.cs:10; HomingH3Tests.cs:12);
  - variants with an object initializer: `new HomingValues { ChainLocks = ChainMode.Off }` (HomingH5Tests.cs:43), `new HomingValues { ChainMaxLifeSeconds = 0.6f }` (:372).
- Other holder shapes in use:
  - a readonly struct with a constructor and `static Default`: `BlastValues` (Spectral.cs:873-880; `Default => new BlastValues(10f, 60f, 2f, 3f, 0.25f, 0.15f)`) and `JumpValues` (Spectral.cs:617-623);
  - a sealed class like HomingValues: `BuildValues` (RangeBuild.cs:95), filled in TestRangeBuild.cs:53-54.

TestRequest (Build.cs:239-247)
```
    internal static class TestRequest {
        public static bool IsNew(string? last, string? now) {
            string n = (now ?? "").Trim();
            return n.Length > 0 && n != (last ?? "").Trim();
        }
    }
```
- Tests: TestToolsTests.cs:34-38.
- Use pattern:
  - `TestTools.Started()` (TestTools.cs:81-86) records the start-up numbers after Cfg.Ready;
  - `Reload()` (:91-110) is subscribed with `Cfg.Reloaded += Reload` (:77) and runs `if (TestRequest.IsNew(lastX, entry.Value)) { lastX = entry.Value.Trim(); ... }`;
  - also TestRange.cs:117.

Other Unity-free pieces the new rules file can reach
- The claim (Wire.cs):
  - `internal sealed class ShotClaim` (:9-31) has fields Kind, Steered, Locks, `int? TimeMs`, Stage, `List<uint> Targets`, `List<int> Picks`.
  - `Copy()` (:23-26) must copy any new field. `ToString()` (:28-30) is pinned by tests.
  - `ShotClaimWire` (:40) has `Version = 1, MaxTargets = 6, MaxStage = 31, TimeBits = 13, TimeUnknown = 8191, TimeMax = 8190, EntityBits = 17, PickNone = 7, MaxLocks = 4` (:41-42).
  - The window is written at :57 (`steered ? locks-1 : 0`) and the reserved mark count (0) at :68.
  - TryRead reads the window at :81-83 (ignored when unsteered) and reads and discards the marks (17 bits each) at :92-93. The steered fix-up is at :94-95.
- Tests that pin today's unsteered window and claim text:
  - HomingTests.cs:201-206: an unsteered window is written as 0; a window without the steered bit is ignored;
  - HomingTests.cs:216-219: ToString and Copy;
  - WireTests.cs:48-54: minimal claim 32 bits, full claim 224 bits, ToString round trips;
  - WireTests.cs:55-63: an old window and marks are skipped;
  - WireTests.cs:94: `"Held steer0 t1234ms stage5 targets[] picks[]"`.
- ClaimRules (Rules.cs:197-233):
  - `public static ShotClaim ServerCheck(ShotClaim claim)` (:214) does `claim.Copy()` and then clears Steered, Targets and Picks. A new field that Copy keeps survives ServerCheck unless it is cleared here.
  - `ServerCheck(ShotClaim, Func<uint, TargetFacts>, float)` (:224).
  - Callers: Claims.cs:170, 177, 179 and Homing.cs:536.
- ClaimTrust (Rules.cs:566-597):
  - `public const int DefaultTrustMs = 1000;`
  - `public static int? EffectiveMs(int? serverMs, int? claimedMs, int trustMs)`
  - `public static string Why(...)`
- Draw clock (Rules.cs):
  - `DrawClock` (:141-179), with `public const float CrossingPull = 0.68f;` and `bool TryElapsed(float now, float grace, out float seconds)`;
  - `RelaxTimer` (:183-195), with `bool Relaxed(bool below, float now, float grace)`;
  - `DipGraceRule.Server(float)` (:549-555).
- There is no named "neutral resolution". The literal is Release.cs:198 `new Resolution(ShotKind.Early, UltimateUse.None, new ReleasePlan(0, 0, 0), 0)`; the constructor is :111 `Resolution(ShotKind kind, UltimateUse ultimate, ReleasePlan plan, int chargedTier, int holdStage = 0)`.
- The file pattern for an account-to-value table is `PathStore` (Path.cs:263-303):
  - `public static PathStore Parse(string? text)`
  - `public MainPath Get(string key)`
  - `public bool Set(string key, MainPath value)`
  - `public string Serialize()`, which writes "key=value\n" sorted by StringComparer.Ordinal.
  - Tests: PathTests.cs:28-32. File input and output: Paths.cs:136-158.
- Yaw convention (Range.cs:8-9, RangeBuild.cs:9-10): degrees clockwise seen from above, 0 = +Z, 90 = +X. The helper is `Footprint.ToWorld(float px, float pz, float yaw, float lx, float lz, out float x, out float z)` (RangeBuild.cs:184 class, :197 method).
- Timing (Timing.cs, also Unity-free):
  - `internal static int Section(string name)` (:72);
  - `internal static TimingScope Begin(int id)` (:94), `Begin(int id, string? context)` (:98), `Begin(int id, uint context)` (:102).

====================================================================
(3) Namespaces, usings, and keeping Unity out
====================================================================
- Every one of the thirteen uses a block-scoped `namespace QuickshotRing { ... }` with `internal` types. None uses System.Linq; the tests do.
- Usings per file:
  - Rules.cs: System, System.Collections.Generic, System.Globalization, System.Text.
  - Build.cs, Wire.cs, Lectern.cs: System, System.Collections.Generic.
  - Release.cs: System.
  - Spectral.cs: System, System.Collections.Generic.
  - Charge.cs, Preview.cs, Homing.cs, Range.cs, RangeBuild.cs: System, System.Collections.Generic, System.Globalization.
  - Path.cs: System, System.Collections.Generic, System.Text.
  - Timing.cs: System, System.Collections.Generic, System.Diagnostics, System.Globalization, System.IO, System.Runtime.CompilerServices, System.Text.
- Header comments that state the rule:
  - Build.cs:4-5, Wire.cs:4-5, Release.cs:3-4 and Homing.cs:4-5: "Compiled by QuickshotRing.Tests, so only System.* in here";
  - Rules.cs:5-7: "nothing in here may touch UnityEngine, MelonLoader or the game" (its list names only nine files and is stale);
  - Range.cs:6-7 and RangeBuild.cs:6-7: "keep Unity types out".
- **Nothing at compile time keeps Unity out.** The test project references UnityEngine*, MelonLoader and Root.Township through Directory.Build.props.
  - A UnityEngine type in a Unity-free file still compiles.
  - The DLLs are not copied to bin\Release, so it would fail only when the test exe runs (inferred, not tried).
  - What does fail at compile time is a reference to a game-side file of the mod (Cfg, Seek, Hits and so on), because those files are not in the test project's compile list.
  - A grep of the thirteen finds no Mathf, Vector3, Quaternion, MelonLogger or Time.time.
- How the seams are kept:
  - Interfaces:
    - IBitWriter and IBitReader (Build.cs:9-20), implemented by StreamBits.cs:8 and FakeBits.cs:5;
    - IJumpWorld (Spectral.cs:627), implemented by Ricochet.cs:249 and :534, and RicochetBounceTests.cs:132 and :396;
    - IRangeProbe (RangeBuild.cs:139), implemented by TestRangeBuild.cs:168, RangeBuildTests.cs:255 and PlanDump.cs:13.
  - Vectors cross as HVec or as float/double components:
    - `JumpCandidate(uint id, float x, float y, float z, bool struck, bool player)` (Spectral.cs:413-418);
    - `SplinterPattern.FanDirection(double ax, double ay, double az, uint seed, int index, int count, float coneDegrees, out double dx, out double dy, out double dz, out float reachShare, float minReach = MinReach)` (:834);
    - `ShatterBlast.Share(uint struck, double px, ..., SplinterCandidate c, BlastValues v, int multiOrbs)` (:926).
- Shipped defaults: the game side takes them from the Unity-free holder's `Default`, as in Seek.cs:40 and :46.
- Language and API limits (net472):
  - none of Math.Clamp, MathF, HashCode, record or init appears anywhere in the mod or the tests, and there is no IsExternalInit polyfill;
  - clamps are written by hand, e.g. `setting < 1 ? 1 : setting > 12 ? 12 : setting` (Homing.cs:36).
- Docs that count the files:
  - CLAUDE.md:117 says "thirteen" and "ALL PASS (2042)";
  - CLAUDE.md:125, the Rules.cs row, lists the thirteen;
  - CLAUDE.md:140 says "the thirteenth file".

## Gaps
- Nothing was built or run, as the task forbids it.
  - 2042 is the last recorded count (the 0.17.15 staged manifest and CLAUDE.md), not a fresh run. No test or Unity-free file has changed since then.
  - That a UnityEngine type in a Unity-free file compiles but fails at run time is inferred from the project's references and from bin\Release holding only the exe. It was not demonstrated.
- No way to run a single test class exists; Program.Main always runs every class.
- BounceQueue<T>.Cap is a const 512 shared by every use. A Skyfall queue that needs a different cap needs its own queue or a change to BounceQueue.
- HVec has no unary minus, no `double * HVec`, no division and no Distance, Lerp or Zero.
- There is no named "neutral Resolution" constant; only the literal at Release.cs:198 exists.
- No test or mechanism anywhere checks that floating-point results are identical across machines (Mono on clients and server against .NET Framework in the tests).
  - The existing "same on every machine" rules (HomingFlight, LookSeed, ArcModel) rely on identical code and identical inputs.
  - The research disputes that the release pose is bit-identical on the server and spectators (docs\superpowers\specs\skyfall-research\shot-seam-and-server.md:155-158). I did not verify this either way.
- TavernLib, TavernVoice and _decomp were not opened: nothing in this slice (tests and Unity-free rules) needs them.
- Nothing was opened under %APPDATA%, and no users.json, console_token.txt or server_secret.key was touched.
- The Rules.cs:5-7 header comment lists only nine of the thirteen files; it is stale.
