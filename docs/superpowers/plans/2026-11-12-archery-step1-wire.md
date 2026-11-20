# Archery Step 1: Build Bits and the Shot Claim on the Wire Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put the two pieces of plumbing that the whole combinations design leans on onto the wire, the **build bits** on bow status messages and the **shot claim** on the shot message, and log everything Step 1's play-test needs. Nothing visible changes in play.

**Architecture:** The bit layouts live in two Unity-free files (`Build.cs`, `Wire.cs`) behind a two-method bit-writer/bit-reader interface, so the test console can write, pad and read them exactly as the game's `StreamWriter`/`StreamReader` do. Thin Unity-bound files adapt the game's `Alta.Serialization.Stream` to that interface (`StreamBits.cs`) and hold the Harmony patches: build bits ride at the end of `BowDrawSkill` status messages (postfix on `PickupBasedSkill.SerializeInternal`, server writes, clients read); the shot claim rides at the end of `ShootArguments` (shooter's client writes, server reads, checks and writes it again on its relay, spectators read). A draw clock per bow string on every machine gives the "time since the 68% crossing" both sides compare. A probe file adds the one-off start-up and first-draw logs Step 1 asks for.

**Tech Stack:** C# (`net472`, `LangVersion latest`, nullable on), MelonLoader 0.7.3, HarmonyX (`0Harmony.dll` from `MelonLoader\net35`), Unity 2020.3 Mono, `dotnet` SDK for build and the test console.

**Spec:** `docs/superpowers/specs/2026-11-12-archery-combinations-design.md`. This plan implements only Part 13 Step 1 ("Build bits and the shot claim on the wire. Nothing visible changes."), with the parts of Part 12 items 1 and 2, Part 11 (network), Part 14 (shared settings and checksum) and the Step 1 "Must also log" lines that Step 1's test needs. Read Part 12 items 1 and 2 and Part 13 Step 1 before starting any task.

## Global Constraints

- One DLL, same build, on the dedicated server and every client. Version after this plan: `0.7.0` (`[assembly: MelonInfo(..., "0.7.0", ...)]` in `Main.cs`).
- No new network message types and no networked objects. Bits are added only at the very end of two messages the game already sends: the bow status message (after `PickupBasedSkill.SerializeInternal`) and the shot message (after `ShootArguments.Serialize`).
- Every added block is at least 8 bits long (spec Part 12 item 2): `StreamReader.IsFinished` is true when fewer than 8 bits remain, and messages are padded to a whole byte, so a shorter block could hide in the padding. Both blocks start with a 4-bit version; version `0` means "absent", because reading past the end returns zeros.
- A missing block is detected with `(stream as StreamReader)?.IsFinished ?? true`, checked before reading any bit of it.
- Entity ids on the wire are 17 bits (the game sends 0 to 100000, `StreamEntityExtensions.cs:78-81`). The claim holds up to 6 target ids, a 3-bit pick per spectral arrow, up to 3 mark ids, 1 "steered" bit, 4 bits of kind and window, and the shooter's milliseconds since its 68% crossing (spec Part 12 item 2, Part 11).
- The server decides; the client only claims. Step 1 honours nothing from the claim: it logs, checks, clears what cannot be verified yet, and relays.
- Nothing visible or audible changes in play. Existing behaviour (Quickdraw ring, Quickdraw 4 Lines, volley, Holy Arrow, Quiver Gather) must be unchanged; in particular `Quickdraw4.Detect` keeps its current `speed >= 998` gate in this step and only logs what the rounded-hundredths rule would decide.
- The assistant cannot run the game. Every behaviour that is not unit-tested emits one greppable line starting `[Archery] ` on the side where it happens. Harmony patch targets and reflection names are only checked at runtime, so each new patch logs once that it fired.
- Unity-free files (`Rules.cs`, `Build.cs`, `Wire.cs`) use only `System.*`: no `UnityEngine`, `MelonLoader`, `HarmonyLib` or game types. The test console compiles exactly these three files.
- Only APIs that exist in Unity 2020.3's Mono (`net472`). Newer BCL calls compile but fail at runtime.
- Do not raise `Cfg.SettingsVersion` (it resets the user's whole settings file, `Cfg.cs:36-41`). This plan only adds new entries and changes descriptions, which needs no version change. Note that the server's own `UserData\QuickshotRing.cfg` is still at `[Settings] Version = 2` (it last ran 0.6.0), so the first 0.7.0 server start resets that file to defaults anyway; Task 7 Step 3 warns the user before deploying.
- Server state is per bow string and per arrow id, never a single static "current shot".
- RevampedSkills stays disabled on the local server (spec "Decisions pending" item 1).
- Not a git repository: no git commands. Each task ends with a Checkpoint: run the tests and `dotnet build QuickshotRing -c Release` from `C:\Users\dmand\OneDrive\Desktop\attmod`, expect `0 Error(s)`, note the warning count, and list the files changed.

## Review Focus

1. **A vanilla message with no trailer** (a game message from a sender without the mod, or with the switch off) must read as Absent, never as garbage. Tests: "build absent at every prefix length 0-63", "claim absent at every prefix length 0-63" (Task 1); play-test rows 7 and 8.
2. **A block shorter than 8 bits could hide in the byte padding** and be read as "absent" or mistaken for padding. Tests: "build block is 32 bits", "claim minimal is 32 bits" (Task 1).
3. **The server relay re-serialises the claim**: `EntityMessageHandler.SendToChunks` on a client calls `InternalMessage.SendToServer` and then `SendToPlayers` (`_decomp/Alta.Networking/EntityMessageHandler.cs:32-36`), each serialising once for all its recipients (`_decomp/Alta.Networking/Socket.cs:202-235`), and the server relays the claim it stored at receipt. Every pass must write identical bits, and writing or reading the store must never consume the claim. Tests: "claim write repeatable" (Task 1), "store get does not consume" (Task 2); play-test row 6.
4. **Client and server run different layouts or versions** of the mod: an unknown version is ignored, and leftover bits are reported. Tests: "build unknown version", "claim unknown version" (Task 1); the "8 or more bits left ... the layouts disagree" warnings (Tasks 4 and 5); play-test row 11.
5. **A client without the mod receives the bits** (build bits on a status message, a relayed claim): it must read the game's fields and ignore the rest. No unit test is possible, because this rests on our block being the last field of the message (`_decomp/PickupBasedSkill.cs:116`, `_decomp/ShootArguments.cs:35`) and the game reading one `StreamReader` per message. Covered by play-test row 12.

Also kept in view (tests already exist): an arrow recovered and shot again within 2 s, or a reused entity id ("store expires after 2 s", "store newer put replaces", "store stays bounded"); the string relaxing, or being held relaxed, before the shot message arrives ("clock ended keeps span within grace", "clock ended forgets after grace", "clock repeated low updates do not extend grace"); holds longer than the time field or no crossing seen ("claim time saturates", "claim unknown time round trip", "unknown never agrees"); different Windows locales ("format float invariant under de-DE").

---

## Wire layouts (decided here; spec "Left to the builder")

Each row is one `Write(value, bits)` / `Read(bits)` call, in this order.

**Build bits** (exactly 32 bits; server writes after `PickupBasedSkill.SerializeInternal` on statuses of `BowDrawSkill` skills; clients read):

| Field | Bits | Values |
|---|---|---|
| version | 4 | `1`; `0` = absent |
| quick tier | 2 | 0 to 3: how many of Quickdraw 1, 2, 3 are owned (also Steady's tier, Part 11) |
| volley tier | 2 | 0 to 3: how many of Volley, Full Volley, Burst |
| charge tier | 2 | 0 to 3: how many of Charge 1, 2, 3 |
| ultimates | 3 | bit 0 Quickdraw 4, bit 1 Seeking Volley, bit 2 Holy Arrow (one flag each, so a save with two is described correctly) |
| side orbs | 6 | bit 0 Snap Draw, 1 Mark, 2 Converge, 3 Ricochet, 4 Brace, 5 Overcharge |
| bridges | 4 | bit 0 Echo, 1 Tracer, 2 Gold Volley, 3 Shatter |
| quiver gather | 1 | |
| settings check | 8 | the server's `SettingsChecksum.Short` (Part 14) |

The spec estimates "about 26 bits"; 24 describe the build and 8 carry the server's short settings checksum, which Part 14 puts here "if the build bits have room".

**Shot claim** (32 to 278 bits; the shooter's client writes after `ShootArguments.Serialize`, the server reads it and writes its checked copy on the relay, other clients read):

| Field | Bits | Values |
|---|---|---|
| version | 4 | `1`; `0` = absent |
| kind | 2 | `ShotKind`: 0 Early, 1 Snap, 2 Held, 3 Charged |
| window | 2 | 0 = no line window, 1 to 3 = window number |
| steered | 1 | |
| time | 13 | milliseconds since the shooter's 68% crossing, 0 to 8190, saturating; 8191 = unknown |
| stage | 5 | spectral arrows showing, 0 to 31 |
| target count | 3 | 0 to 6; 7 is malformed |
| targets | 17 each | entity ids |
| picks | 3 each, `stage` of them, only when target count > 0 | index into targets; 7 = none |
| mark count | 2 | 0 to 3 |
| marks | 17 each | entity ids |

Minimum 32 bits; with stage 30, 6 targets and 3 marks, 275 bits (35 bytes, inside Part 11's "at most about 40 bytes").

What Step 1 fills, because later steps own the rest (the task text requires this to be stated): build bits carry real tiers and flags for the 12 existing orbs; the 12 new orbs have no slot ids until Step 2, so their bits are always 0. The claim carries the real time and stage; kind is Snap (Quickdraw ring charged), Held (ghosts showing) or Early, never Charged (the resolver in Step 2 owns it); window, steered, targets, picks and marks are always empty (Steps 5 to 7 own them).

---

## File Structure

| File | Responsibility |
|---|---|
| `QuickshotRing/Build.cs` (new, Unity-free) | `Orb` ids, the `Build` value (tiers and flags), `Build.From`, its log text. |
| `QuickshotRing/Wire.cs` (new, Unity-free) | `IBitWriter`/`IBitReader`, `WireRead`, `BuildBits`, `ShotKind`, `ShotClaim`, `ShotClaimWire`. |
| `QuickshotRing/Rules.cs` (modify) | Adds `DrawClock`, `ClaimRules`, `ClaimStore`, `SettingsChecksum`, `SpeedWire`. |
| `QuickshotRing/StreamBits.cs` (new) | Adapter from `Alta.Serialization.Stream` to the two interfaces. |
| `QuickshotRing/Builds.cs` (new) | Server: a player's build from owned slots. Client: build cache per player and the settings-checksum comparison. The status-message patches. |
| `QuickshotRing/Claims.cs` (new) | Draw clock per bow string, claim fill on the shooter, check and relay on the server, the shot-message patch. |
| `QuickshotRing/Probe.cs` (new) | One-off logs: progression caps, arrow prefabs, flight speeds, patches shared with other mods. |
| `QuickshotRing/Cfg.cs` (modify) | Shared/look-only marking, checksum, the `[Wire]` test switches. |
| `QuickshotRing/Tree.cs` (modify) | Slot-id to `Orb` map, the Ranged path, the progression and shared-patch log calls. |
| `QuickshotRing/Fx.cs` (modify) | Asset dump extended to materials, line and trail renderers; named material lookups; materials in use. |
| `QuickshotRing/Quickdraw4.cs` (modify) | Flight-speed log in `Detect`; `Marker` made internal; shared marks. |
| `QuickshotRing/Main.cs` (modify) | Version 0.7.0, `Cfg.InitWire()`, `OnLateInitializeMelon` patch log. |
| `QuickshotRing/Volley.cs`, `HolyArrow.cs`, `QuiverGather.cs` (modify) | Shared marks; `Volley.ResolveHeadMaterial()`. |
| `QuickshotRing.Tests/QuickshotRing.Tests.csproj` (modify) | Compiles `Build.cs` and `Wire.cs` too. |
| `QuickshotRing.Tests/FakeBits.cs`, `WireTests.cs`, `RuleTests.cs` (new) | Bit buffer with the game's padding and end-of-message behaviour; the new checks. |
| `QuickshotRing.Tests/Program.cs` (modify) | `Check`/`Near` become `internal`; calls the new test classes. |
| `CLAUDE.md` (modify) | File table, test count. |
| `_decomp/Alta.Serialization/` (added while planning) | Decompiled `Alta.Serialization.Runtime.dll` (Stream, StreamReader, StreamWriter, BitReader, BitWriter, StreamCounter). Reference only, never compiled. If missing, regenerate: `ilspycmd -p -o <scratch> "C:\Users\dmand\OneDrive\Desktop\A Township Tale (client\A Township Tale_Data\Managed\Alta.Serialization.Runtime.dll"` and copy its `Alta.Serialization` folder into `_decomp`. |

---

### Task 1: Build model and the two bit layouts (Unity-free)

**Files:**
- Create: `QuickshotRing/Build.cs`, `QuickshotRing/Wire.cs`, `QuickshotRing.Tests/FakeBits.cs`, `QuickshotRing.Tests/WireTests.cs`
- Modify: `QuickshotRing.Tests/QuickshotRing.Tests.csproj` (add `<Compile Include="..\QuickshotRing\Build.cs" />` and `<Compile Include="..\QuickshotRing\Wire.cs" />`), `QuickshotRing.Tests/Program.cs:5-14` (make `Check` and `Near` `internal static`; call `WireTests.Run()` before the summary line), `QuickshotRing/Rules.cs:3-4` (header comment: "Rules.cs, Build.cs and Wire.cs are the files QuickshotRing.Tests compiles")

**Interfaces:**
- Consumes (behaviour mirrored by `FakeBits`, read from the game): `StreamReader.IsFinished => reader.BitsRead >= reader.TotalBits - 7` (`_decomp/Alta.Serialization/StreamReader.cs:17`); reading past the last word shifts in zeros (`_decomp/Alta.Serialization/BitReader.cs:99-115`); the writer masks each value to its bit count (`_decomp/Alta.Serialization/BitWriter.cs:67-73`) and pads the message with zero bits to a whole byte (`BitWriter.cs:146-160`); one `StreamReader` per network message sized to that message's byte count (`_decomp/Alta.Networking.Internal/MessageProcessor.cs:23-35`).
- Produces (namespace `QuickshotRing`, all `internal`):
  - `enum Orb { None, Quick1, Quick2, Quick3, Quickdraw4, SnapDraw, Mark, Echo, Tracer, Volley, FullVolley, Burst, SeekingVolley, Converge, Ricochet, GoldVolley, Shatter, Charge1, Charge2, Charge3, HolyArrow, Brace, Overcharge, Gatherer, QuiverGather }`
  - `[Flags] enum Ultimates { None = 0, Quickdraw4 = 1, SeekingVolley = 2, HolyArrow = 4 }`; `[Flags] enum SideOrbs { None = 0, SnapDraw = 1, Mark = 2, Converge = 4, Ricochet = 8, Brace = 16, Overcharge = 32 }`; `[Flags] enum Bridges { None = 0, Echo = 1, Tracer = 2, GoldVolley = 4, Shatter = 8 }`
  - `readonly struct Build : IEquatable<Build>` with `int Quick, Volley, Charge` (each clamped 0..3 by the constructor), `Ultimates Ultimates`, `SideOrbs Sides`, `Bridges Bridges`, `bool QuiverGather`; constructor `Build(int quick, int volley, int charge, Ultimates ultimates, SideOrbs sides, Bridges bridges, bool quiverGather)`; `static Build From(IEnumerable<Orb> owned)` (set semantics: duplicates count once; tiers count Quick1..3, Volley/FullVolley/Burst, Charge1..3; `Gatherer` and `None` are ignored); `ToString()`, `Equals`, `GetHashCode`, `==`, `!=`.
  - `Build.ToString()` format, exactly: `Q{Quick} V{Volley} C{Charge} ult:{u} side:{s} bridge:{b} quiver:{yes|no}` where each list is the set flag names joined by `+` in bit order, or `-` when empty. Names: `Q4`, `Seek`, `Holy`; `SnapDraw`, `Mark`, `Converge`, `Ricochet`, `Brace`, `Overcharge`; `Echo`, `Tracer`, `GoldVolley`, `Shatter`.
  - `interface IBitWriter { void Write(uint value, int bits); }`
  - `interface IBitReader { uint Read(int bits); bool Finished { get; } int BitsLeft { get; } }`
  - Both interfaces document, in a doc comment, that `bits` is 1..32: the real adapter maps onto `Stream.SerializeBits`, and the game's `BitWriter` never takes more than 32 bits (`_decomp/Alta.Serialization/BitWriter.cs:67-74`). `BitsLeft` is never negative.
  - `enum WireRead { Absent, Ok, UnknownVersion, Malformed }`
  - `static class BuildBits { const int Version = 1; const int Size = 32; static void Write(IBitWriter w, Build build, byte settingsCheck); static WireRead TryRead(IBitReader r, out Build build, out byte settingsCheck); }`
  - `enum ShotKind { Early = 0, Snap = 1, Held = 2, Charged = 3 }`
  - `sealed class ShotClaim` with fields `ShotKind Kind; int Window; bool Steered; int? TimeMs; int Stage; List<uint> Targets; List<int> Picks; List<uint> Marks` (lists never null), `ShotClaim Copy()`, `ToString()`.
  - `ShotClaim.ToString()` format, exactly: `{Kind} w{Window} steer{0|1} t{TimeMs|?}ms stage{Stage} targets[{ids joined by ","}] picks[{picks joined by ","}] marks[{ids joined by ","}]`, e.g. `Held w0 steer0 t1234ms stage5 targets[] picks[] marks[]`.
  - `static class ShotClaimWire { const int Version = 1, MaxTargets = 6, MaxMarks = 3, MaxStage = 31, TimeBits = 13, TimeUnknown = 8191, TimeMax = 8190, EntityBits = 17, PickNone = 7; static int Write(IBitWriter w, ShotClaim claim) /* returns bits written */; static WireRead TryRead(IBitReader r, out ShotClaim? claim); }`

- [ ] **Step 1: Write `FakeBits`**

A bit buffer that behaves like the game's writer and reader. This code fixes the game behaviour the tests rely on:

```csharp
using System.Collections.Generic;
using System.Text;
using QuickshotRing;

internal sealed class FakeBits : IBitWriter, IBitReader {
    readonly List<bool> bits = new List<bool>();
    int pos, sealedCount = -1;
    public int Count => bits.Count;
    public void Write(uint value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1u) != 0); }
    public void Seal() { while (bits.Count % 8 != 0) bits.Add(false); sealedCount = bits.Count; } // AlignAndFlush: zero padding to a byte
    int Total => sealedCount < 0 ? bits.Count : sealedCount;
    public uint Read(int count) {                                  // past the end reads zeros, like BitReader
        uint v = 0;
        for (int i = 0; i < count; i++, pos++) v = (v << 1) | (pos < bits.Count && bits[pos] ? 1u : 0u);
        return v;
    }
    public bool Finished => Total - pos < 8;                       // StreamReader.IsFinished
    public int BitsLeft => Total - pos > 0 ? Total - pos : 0;
    public string Dump() { var sb = new StringBuilder(); foreach (bool b in bits) sb.Append(b ? '1' : '0'); return sb.ToString(); }
}
```

- [ ] **Step 2: Write the failing tests in `WireTests.cs`**

`internal static class WireTests { internal static void Run() { ... } }`, with `using System;` and `using static Program;` so `Check` and `Near` read as below. Helpers local to the file: `static Build All()` = `Build.From` of every `Orb` value; `static ShotClaim Full()` = Kind `Charged`, Window 3, Steered true, TimeMs 6500, Stage 30, Targets `{1, 2, 99999, 100000, 4242, 131071}`, Picks `i % 6` for i in 0..29, Marks `{7, 8, 100000}`.

```csharp
var sample = Build.From(new[] { Orb.Quick1, Orb.Quick2, Orb.Quickdraw4, Orb.Charge1, Orb.HolyArrow, Orb.Volley, Orb.QuiverGather });

// build bits
var a = new FakeBits(); a.Write(0x1ABCD, 21); BuildBits.Write(a, sample, 0x5E); a.Seal();
uint game = a.Read(21);
Check("build round trip after game bits", BuildBits.TryRead(a, out Build b1, out byte c1) == WireRead.Ok && b1 == sample && c1 == 0x5E);
Check("build game bits intact", game == 0x1ABCD);
var size = new FakeBits(); BuildBits.Write(size, sample, 0); Check("build block is 32 bits", size.Count == 32 && BuildBits.Size == 32);
Check("build consumed to padding", a.Finished);
var all = new FakeBits(); BuildBits.Write(all, All(), 0xFF); all.Seal();
Check("build all orbs round trip", BuildBits.TryRead(all, out Build b2, out byte c2) == WireRead.Ok && b2 == All() && c2 == 0xFF);
bool absent = true;
for (int n = 0; n < 64; n++) {   // prefixes written and read in chunks of at most 32 bits, the interfaces' contract
    var f = new FakeBits();
    for (int k = n; k > 0; k -= 32) f.Write(0, Math.Min(k, 32));
    f.Seal();
    for (int k = n; k > 0; k -= 32) f.Read(Math.Min(k, 32));
    if (BuildBits.TryRead(f, out _, out _) != WireRead.Absent) absent = false;
}
Check("build absent at every prefix length 0-63", absent);
var v0 = new FakeBits(); v0.Write(1, 5); v0.Write(0, 8); v0.Seal(); v0.Read(5);
Check("build version 0 is absent", BuildBits.TryRead(v0, out _, out _) == WireRead.Absent);
var v15 = new FakeBits(); v15.Write(15, 4); v15.Write(0, 28); v15.Seal();
Check("build unknown version", BuildBits.TryRead(v15, out _, out _) == WireRead.UnknownVersion);
Check("build from counts tiers", All().Quick == 3 && All().Volley == 3 && All().Charge == 3);
Check("build from ignores duplicates", Build.From(new[] { Orb.Quick1, Orb.Quick1, Orb.Charge2, Orb.Charge2 }) is var d && d.Quick == 1 && d.Charge == 1);
Check("build text", sample.ToString() == "Q2 V1 C1 ult:Q4+Holy side:- bridge:- quiver:yes");
var r1 = new FakeBits(); var r2 = new FakeBits(); BuildBits.Write(r1, sample, 9); BuildBits.Write(r2, sample, 9);
Check("build write repeatable", r1.Dump() == r2.Dump());

// shot claim
var min = new ShotClaim { Kind = ShotKind.Early, TimeMs = null, Stage = 0 };
var m = new FakeBits(); int minBits = ShotClaimWire.Write(m, min); m.Seal();
Check("claim minimal round trip", ShotClaimWire.TryRead(m, out ShotClaim? gm) == WireRead.Ok && gm!.ToString() == min.ToString());
Check("claim minimal is 32 bits", minBits == 32);
var fb = new FakeBits(); fb.Write(0x3FF, 10); int fullBits = ShotClaimWire.Write(fb, Full()); fb.Seal(); fb.Read(10);
Check("claim full round trip", ShotClaimWire.TryRead(fb, out ShotClaim? gf) == WireRead.Ok && gf!.ToString() == Full().ToString());
Check("claim full is 275 bits", fullBits == 275);
absent = true;
for (int n = 0; n < 64; n++) {
    var f = new FakeBits();
    for (int k = n; k > 0; k -= 32) f.Write(uint.MaxValue, Math.Min(k, 32));
    f.Seal();
    for (int k = n; k > 0; k -= 32) f.Read(Math.Min(k, 32));
    if (ShotClaimWire.TryRead(f, out _) != WireRead.Absent) absent = false;
}
Check("claim absent at every prefix length 0-63", absent);
var z = new FakeBits(); z.Write(0, 16); z.Seal();
Check("claim version 0 is absent", ShotClaimWire.TryRead(z, out _) == WireRead.Absent);
var u = new FakeBits(); u.Write(2, 4); u.Write(0, 28); u.Seal();
Check("claim unknown version", ShotClaimWire.TryRead(u, out _) == WireRead.UnknownVersion);
var bad = new FakeBits(); bad.Write(1, 4); bad.Write(0, 2); bad.Write(0, 2); bad.Write(0, 1); bad.Write(0, 13); bad.Write(0, 5); bad.Write(7, 3); bad.Write(0, 2); bad.Seal();
Check("claim 7 targets is malformed", ShotClaimWire.TryRead(bad, out _) == WireRead.Malformed);
var many = new ShotClaim { Targets = { 1, 2, 3, 4, 5, 6, 7, 8 }, Marks = { 1, 2, 3, 4, 5 } };
var mb = new FakeBits(); ShotClaimWire.Write(mb, many); mb.Seal(); ShotClaimWire.TryRead(mb, out ShotClaim? gmany);
Check("claim writer caps targets and marks", gmany!.Targets.Count == 6 && gmany.Marks.Count == 3 && gmany.Targets[5] == 6);
var st = new FakeBits(); ShotClaimWire.Write(st, new ShotClaim { Stage = 40 }); st.Seal(); ShotClaimWire.TryRead(st, out ShotClaim? gst);
Check("claim writer caps stage", gst!.Stage == 31);
var big = new FakeBits(); ShotClaimWire.Write(big, new ShotClaim { Targets = { 200000, 5 }, Marks = { 300000 } }); big.Seal(); ShotClaimWire.TryRead(big, out ShotClaim? gbig);
Check("claim drops ids over 17 bits", gbig!.Targets.Count == 1 && gbig.Targets[0] == 5 && gbig.Marks.Count == 0);
var pk = new FakeBits(); ShotClaimWire.Write(pk, new ShotClaim { Stage = 3, Targets = { 10, 11 }, Picks = { 1, 9 } }); pk.Seal(); ShotClaimWire.TryRead(pk, out ShotClaim? gpk);
Check("claim missing or out-of-range picks read as 7", gpk!.Picks.Count == 3 && gpk.Picks[0] == 1 && gpk.Picks[1] == 7 && gpk.Picks[2] == 7);
var ts = new FakeBits(); ShotClaimWire.Write(ts, new ShotClaim { TimeMs = 9000 }); ts.Seal(); ShotClaimWire.TryRead(ts, out ShotClaim? gts);
Check("claim time saturates", gts!.TimeMs == 8190);
var tn = new FakeBits(); ShotClaimWire.Write(tn, new ShotClaim { TimeMs = -5 }); tn.Seal(); ShotClaimWire.TryRead(tn, out ShotClaim? gtn);
Check("claim negative time is 0", gtn!.TimeMs == 0);
Check("claim unknown time round trip", gm!.TimeMs == null);
Check("claim text", new ShotClaim { Kind = ShotKind.Held, TimeMs = 1234, Stage = 5 }.ToString() == "Held w0 steer0 t1234ms stage5 targets[] picks[] marks[]");
var w1 = new FakeBits(); var w2 = new FakeBits(); ShotClaimWire.Write(w1, Full()); ShotClaimWire.Write(w2, Full());
Check("claim write repeatable", w1.Dump() == w2.Dump());
Check("claim after game bits leaves under 8 bits", fb.Finished && fb.BitsLeft < 8);
```

That is 30 checks.

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `Build`, `BuildBits`, `ShotClaim` not defined.

- [ ] **Step 4: Implement `Build.cs` and `Wire.cs`**

Field order and widths are the two tables in "Wire layouts" above. `TryRead` on both: if `r.Finished` return `Absent` without reading; read the version; `0` returns `Absent`; anything but `Version` returns `UnknownVersion`; then read the rest. `ShotClaimWire.TryRead` returns `Malformed` for a target count above 6, `TimeMs = null` for 8191, and keeps picks as read. `ShotClaimWire.Write` applies, in order: window clamped 0..3; stage clamped 0..31; target and mark ids above `(1 << 17) - 1` dropped, then the first 6 targets and first 3 marks kept; `TimeMs` null to 8191, otherwise clamped 0..8190; when at least one target is kept, one pick per stage entry, `Picks[i]` if it exists and is in `0..targetCount-1`, else 7. Writing never changes the `ShotClaim` passed in.

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (69)` (39 existing + 30).

- [ ] **Step 6: Checkpoint**

Run the tests (expected `ALL PASS (69)`) and `dotnet build QuickshotRing -c Release` (expected `0 Error(s)`; note the warning count). Files changed: `QuickshotRing/Build.cs`, `QuickshotRing/Wire.cs`, `QuickshotRing/Rules.cs` (comment only), `QuickshotRing.Tests/QuickshotRing.Tests.csproj`, `QuickshotRing.Tests/Program.cs`, `QuickshotRing.Tests/FakeBits.cs`, `QuickshotRing.Tests/WireTests.cs`.

---

### Task 2: Clock, claim rules, claim store, settings checksum, flight-speed rule (Unity-free)

**Files:**
- Modify: `QuickshotRing/Rules.cs` (append the classes below), `QuickshotRing.Tests/Program.cs` (call `RuleTests.Run()` after `WireTests.Run()`)
- Create: `QuickshotRing.Tests/RuleTests.cs`

**Interfaces:**
- Consumes: `ShotClaim`, `ShotClaim.Copy()` (Task 1). The game's flight-speed field: `stream.SerializeFloat(ref flightSpeed, 0f, 1000f, 0.01f)` (`_decomp/Arrow.cs:27-31`), whose arithmetic is `Stream.SerializeFloat(ref float, float, float, float)` (`_decomp/Alta.Serialization/Stream.cs:542-562`). `Mathf.RoundToInt(f)` is `(int)Math.Round(f)`.
- Produces (namespace `QuickshotRing`, all `internal`):
  - `sealed class DrawClock { const float CrossingPull = 0.68f; const float Grace = 1f; void Update(float pull, bool nocked, float now); bool TryElapsed(float now, float grace, out float seconds); }`
  - `static class ClaimRules { const int ToleranceMs = 60; static int? Diff(int? clientMs, int? serverMs) /* server - client */; static bool Agrees(int? clientMs, int? serverMs, int toleranceMs = ToleranceMs); static ShotClaim ServerCheck(ShotClaim claim); }`
  - `sealed class ClaimStore { const float Lifetime = 2f; const int Cap = 256; int Count { get; } void Put(uint arrowId, ShotClaim claim, float now); bool TryGet(uint arrowId, float now, out ShotClaim? claim); void Remove(uint arrowId); }`. `TryGet` never removes an entry (the relay and every repeat serialisation read the same claim); only `Remove`, a newer `Put` and the bound in `Put` drop one.
  - `static class SettingsChecksum { static string Format(object? value); static uint Compute(IEnumerable<KeyValuePair<string, string>> settings); static byte Short(uint checksum); }`
  - `static class SpeedWire { static float RoundTrip(float speed); static int Hundredths(float speed); static bool IsMarker(float received, float marker); }`

- [ ] **Step 1: Write the failing tests in `RuleTests.cs`**

`internal static class RuleTests { internal static void Run() { ... } }`, with `using static Program;` and `using System.Collections.Generic;`.

```csharp
// DrawClock: crosses on the first update with pull > 0.68 and an arrow nocked; a span ends when pull <= 0.68 or no arrow
var c = new DrawClock();
Check("clock fresh unknown", !c.TryElapsed(0f, DrawClock.Grace, out _));
c.Update(0.5f, true, 1f); Check("clock below crossing unknown", !c.TryElapsed(1f, DrawClock.Grace, out _));
c.Update(0.68f, true, 1.5f); Check("clock exactly 0.68 does not cross", !c.TryElapsed(1.5f, DrawClock.Grace, out _));
var c2 = new DrawClock(); c2.Update(0.9f, false, 1f); Check("clock no arrow does not cross", !c2.TryElapsed(1f, DrawClock.Grace, out _));
c.Update(0.69f, true, 2f); Check("clock crosses", c.TryElapsed(3f, DrawClock.Grace, out float e1) && Near(e1, 1f));
c.Update(0.95f, true, 2.5f); Check("clock keeps first crossing while above", c.TryElapsed(3f, DrawClock.Grace, out float e2) && Near(e2, 1f));
c.Update(0.6f, true, 3f); Check("clock ended keeps span within grace", c.TryElapsed(3.5f, 1f, out float e3) && Near(e3, 1.5f));
Check("clock ended forgets after grace", !c.TryElapsed(4.01f, 1f, out _));
c.Update(0.5f, true, 3.4f); Check("clock repeated low updates do not extend grace", !c.TryElapsed(4.01f, 1f, out _)); // HeldUpdate keeps calling with low pull; the end stays at 3.0
c.Update(0.8f, true, 5f); Check("clock re-crosses", c.TryElapsed(5.25f, DrawClock.Grace, out float e4) && Near(e4, 0.25f));

// ClaimRules
Check("agree at +60", ClaimRules.Agrees(1000, 1060));
Check("agree at -60", ClaimRules.Agrees(1060, 1000));
Check("disagree at 61", !ClaimRules.Agrees(1000, 1061));
Check("unknown never agrees", !ClaimRules.Agrees(null, 1000) && !ClaimRules.Agrees(1000, null) && ClaimRules.Diff(null, 5) == null);
Check("diff is server minus client", ClaimRules.Diff(1000, 1026) == 26);
var claim = new ShotClaim { Kind = ShotKind.Held, Window = 2, Steered = true, TimeMs = 900, Stage = 5, Targets = { 4, 5 }, Picks = { 0, 1 }, Marks = { 9 } };
var checkedClaim = ClaimRules.ServerCheck(claim);
Check("server check clears steered", !checkedClaim.Steered);
Check("server check clears lists", checkedClaim.Targets.Count == 0 && checkedClaim.Picks.Count == 0 && checkedClaim.Marks.Count == 0);
Check("server check keeps kind, window, time, stage", checkedClaim.Kind == ShotKind.Held && checkedClaim.Window == 2 && checkedClaim.TimeMs == 900 && checkedClaim.Stage == 5);
Check("server check leaves input untouched", claim.Steered && claim.Targets.Count == 2);

// ClaimStore
var s = new ClaimStore(); var one = new ShotClaim { Stage = 1 }; var two = new ShotClaim { Stage = 2 };
s.Put(42, one, 10f);
Check("store get fresh", s.TryGet(42, 11.9f, out ShotClaim? g1) && g1!.Stage == 1);
Check("store expires after 2 s", !s.TryGet(42, 12.01f, out _));
s.Put(42, one, 20f); s.Put(42, two, 20.5f); Check("store newer put replaces", s.TryGet(42, 21f, out ShotClaim? g2) && g2!.Stage == 2);
s.Put(7, one, 30f); Check("store get does not consume", s.TryGet(7, 30.1f, out _) && s.TryGet(7, 30.2f, out _));
s.Remove(42); Check("store remove", !s.TryGet(42, 21f, out _));
for (uint i = 0; i < 1000; i++) s.Put(i, one, 100f + i * 0.01f);
Check("store stays bounded", s.Count <= ClaimStore.Cap);

// SettingsChecksum: FNV-1a 32 over UTF-8 "key=value\n", keys sorted ordinal
Check("checksum empty is FNV offset", SettingsChecksum.Compute(new KeyValuePair<string, string>[0]) == 2166136261u);
var p1 = new[] { new KeyValuePair<string, string>("Volley.InnerShare", "0.15"), new KeyValuePair<string, string>("Archery.HolySeconds", "6.5") };
var p2 = new[] { p1[1], p1[0] };
Check("checksum order independent", SettingsChecksum.Compute(p1) == SettingsChecksum.Compute(p2));
Check("checksum sees a value change", SettingsChecksum.Compute(p1) != SettingsChecksum.Compute(new[] { p1[0], new KeyValuePair<string, string>("Archery.HolySeconds", "6.6") }));
Check("checksum sees a key change", SettingsChecksum.Compute(p1) != SettingsChecksum.Compute(new[] { p1[0], new KeyValuePair<string, string>("Archery.HolySecond", "6.5") }));
Check("short fold", SettingsChecksum.Short(0x12345678u) == 0x08);
var old = System.Threading.Thread.CurrentThread.CurrentCulture;
System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
Check("format float invariant under de-DE", SettingsChecksum.Format(0.5f) == "0.5" && SettingsChecksum.Format(6.5) == "6.5");
System.Threading.Thread.CurrentThread.CurrentCulture = old;
Check("format int, bool, string, null", SettingsChecksum.Format(40) == "40" && SettingsChecksum.Format(true) == "True" && SettingsChecksum.Format("#FFC733") == "#FFC733" && SettingsChecksum.Format(null) == "");

// SpeedWire: the arrow flight speed survives the wire in hundredths up to 495.38 m/s (spec Part 11 says "about 495")
Check("marker 999 hundredths 99900", SpeedWire.Hundredths(SpeedWire.RoundTrip(999f)) == 99900);
bool exact = true;
for (int i = 0; i <= 49538; i++) if (SpeedWire.Hundredths(SpeedWire.RoundTrip(i / 100f)) != i) { exact = false; break; }
Check("speeds to 495.38 survive in hundredths", exact);
Check("IsMarker 999", SpeedWire.IsMarker(SpeedWire.RoundTrip(999f), 999f));
Check("ordinary speed is not marker", !SpeedWire.IsMarker(52.37f, 999f));
```

That is 36 checks.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: build error, `DrawClock` not defined.

- [ ] **Step 3: Implement the classes in `Rules.cs`**

`DrawClock` keeps `crossedAt` and `endedAt` (both `NaN` when unset). `Update`: if `nocked && pull > CrossingPull`, start a new span (`crossedAt = now`, `endedAt = NaN`) unless one is active; otherwise a low update sets `endedAt = now` only when the span is active (`endedAt` is `NaN` and `crossedAt` is set). An already-ended span is left alone, so repeated low updates while the string is held relaxed never extend the grace. `TryElapsed`: no span → false; active → `now - crossedAt`; ended → `now - crossedAt` only while `now - endedAt <= grace`.

`ClaimRules.ServerCheck` returns `claim.Copy()` with `Steered = false` and empty `Targets`, `Picks` and `Marks`: nothing in Step 1 can verify a lock, a target or a mark (spec Part 12 item 2: the server honours "steered" only after checking the lock, and checks every target id). Later steps replace this body. Doc comment says so.

`ClaimStore.Put`: when `Count >= Cap`, first drop entries older than `Lifetime`, and if still full, clear all; then store `(claim, now)`.

`SettingsChecksum.Format`: `float` and `double` with `"R"` and `CultureInfo.InvariantCulture`, `null` as `""`, anything else `Convert.ToString(value, CultureInfo.InvariantCulture)`. `Compute`: FNV-1a 32 (offset 2166136261, prime 16777619) over the UTF-8 bytes of `key + "=" + value + "\n"` for each pair, sorted by key with `StringComparer.Ordinal`. `Short(c) = (byte)(c ^ (c >> 8) ^ (c >> 16) ^ (c >> 24))`.

`SpeedWire.RoundTrip` must copy the game's float arithmetic exactly (`Stream.cs:542-562` with minimum 0, maximum 1000, precision 0.01f), because the test loop pins its result:

```csharp
internal static float RoundTrip(float speed) {
    float range = 1000f; uint steps = (uint)(range / 0.01f) + 1;           // 100001, 17 bits
    uint q = 0; if (speed > 0f) { q = (uint)(speed / range * (float)steps + 0.5f); if (q > steps) q = steps; }
    return (float)q / (float)steps * range;
}
```

`Hundredths(s) = (int)Math.Round(s * 100f)` (what `Mathf.RoundToInt(speed * 100)` gives). `IsMarker(received, marker) = Hundredths(received) == Hundredths(RoundTrip(marker))`.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet run --project QuickshotRing.Tests -c Release`
Expected: `ALL PASS (105)`.

- [ ] **Step 5: Checkpoint**

Tests `ALL PASS (105)`; `dotnet build QuickshotRing -c Release` `0 Error(s)`, note warnings. Files changed: `QuickshotRing/Rules.cs`, `QuickshotRing.Tests/Program.cs`, `QuickshotRing.Tests/RuleTests.cs`.

---

### Task 3: Shared and look-only settings, checksum, wire test switches

**Files:**
- Modify: `QuickshotRing/Cfg.cs` (whole file), `QuickshotRing/Main.cs:47-66` (call `Cfg.InitWire()` just before `Cfg.Ready()`), `QuickshotRing/Tree.cs:49-62`, `QuickshotRing/Volley.cs:43-62`, `QuickshotRing/Quickdraw4.cs:38-58`, `QuickshotRing/HolyArrow.cs:41-70`, `QuickshotRing/QuiverGather.cs:32-37`

**Interfaces:**
- Consumes: `SettingsChecksum.Format/Compute/Short` (Task 2). MelonLoader: `MelonPreferences_Entry.Description` has a public setter, `MelonPreferences_Entry.BoxedValue`, `.Identifier`, `.Category.Identifier` (checked by reflection on the installed `MelonLoader\net35\MelonLoader.dll` while planning). Existing: `Cfg.Category` (`Cfg.cs:25-31`), `Cfg.Ready` (`Cfg.cs:34-44`), `Cfg.Poll` (`Cfg.cs:46-58`).
- Produces:
  - `internal static MelonPreferences_Entry<T> Cfg.Shared<T>(MelonPreferences_Entry<T> entry)`: records the entry as shared and prefixes its description with `[shared] ` (once).
  - `internal static uint Cfg.SharedChecksum`, `internal static byte Cfg.SharedShort` (recomputed in `Ready()` and after every reload in `Poll()`, before `Reloaded` fires).
  - `internal static void Cfg.InitWire()` creating category `Wire` with `internal static MelonPreferences_Entry<bool> Cfg.SendBuildBits` (default `true`), `Cfg.SendClaims` (default `true`), `Cfg.LogWire` (default `true`). None of them is shared.

- [ ] **Step 1: Mark the shared settings**

Wrap exactly these 29 `CreateEntry` calls in `Cfg.Shared(...)`; every other entry is look-only (Part 14: the clock, shares, stagger, ring radii and anything else the server's damage or timing depends on must match; colours, widths, brightness, sounds and effect names are per client):

| Category | Shared entries |
|---|---|
| `Archery` | `TwoShotSeconds`, `ThreeShotSeconds`, `HolySeconds` |
| `TreeLayout` | `HolyArrow`, `ArrowGatherer`, `QuiverGather` (Part 14 names the client/server layout mismatch as the case that matters today) |
| `Volley` | `InnerShare`, `OuterShare`, `OuterInterval`, `InnerRadius`, `OuterRadius`, `RelaxGraceSeconds`, `GatherSeconds`, `LifeSeconds` |
| `Quickdraw4` | `Damage`, `MaxReach` |
| `HolyArrow` | `DirectMultiplier`, `BlastMultiplier`, `Radius`, `FullRadius`, `EdgeFraction`, `BlastCripple`, `BlastForce`, `PlayerDamageShare`, `SpeedMultiplier` |
| `QuiverGather` | `Enabled`, `ReturnSeconds`, `RetrySeconds`, `MaxTracked` |

- [ ] **Step 2: Checksum and file header in `Cfg.cs`**

Checksum key is `"{Category.Identifier}.{Identifier}"`, value `SettingsChecksum.Format(entry.BoxedValue)`, over the shared entries only. The `[Settings] Version` entry's description becomes, exactly: `Written by the mod. Do not edit. Settings whose description starts with [shared] must have the same value in the server's and every client's QuickshotRing.cfg; compare the '[Archery] Settings: shared checksum' log line on both sides.`

Log at `Ready()` and after each reload:
`[Archery] Settings: shared checksum {SharedChecksum:x8} (short {SharedShort:x2}) over {n} settings`

`[Wire]` descriptions, exactly:
- `SendBuildBits`: `Server: add the build bits to bow status messages. Turn off to check that clients log them as absent.`
- `SendClaims`: `Client: add the shot claim to your own shots. Turn off to check that the server logs it as absent.`
- `LogWire`: `Log every build-bits and shot-claim message (the 0.7.0 wire test).`

- [ ] **Step 3: Checkpoint**

Tests `ALL PASS (105)`; build `0 Error(s)`, note warnings. Files changed: `Cfg.cs`, `Main.cs`, `Tree.cs`, `Volley.cs`, `Quickdraw4.cs`, `HolyArrow.cs`, `QuiverGather.cs`.

---

### Task 4: Build read on the server and build bits on bow status messages

**Files:**
- Create: `QuickshotRing/StreamBits.cs`, `QuickshotRing/Builds.cs`
- Modify: `QuickshotRing/Tree.cs` (orb map inside `Inject`, `Tree.cs:89-165`)

**Interfaces:**
- Consumes:
  - Task 1: `Orb`, `Build`, `Build.From`, `BuildBits.Write/TryRead`, `WireRead`, `IBitWriter`, `IBitReader`. Task 3: `Cfg.SharedShort`, `Cfg.SendBuildBits`, `Cfg.LogWire`, `Cfg.Reloaded`.
  - `Stream.SerializeBits(ref uint value, int bitCount)` (`_decomp/Alta.Serialization/Stream.cs:636`); `Stream.IsReading`/`IsWriting` (`Stream.cs:30-32`); `StreamReader.IsFinished` (`StreamReader.cs:17`), `StreamReader.BitIndex` and `TotalByteCount` (`StreamReader.cs:13-15`).
  - `PickupBasedSkill.SendSerializedMessage(IPlayer player, Stream stream)` (`_decomp/PickupBasedSkill.cs:98-167`): `player` is the skill's owner, because `PlayerProgressionManager.NetworkSkillMessage` passes `base.Player` (`_decomp/PlayerProgressionManager.cs:449-454`). The game's own fields (arrow id, status, hit data) are serialised at lines 101-114; `SerializeInternal(stream)` is the last thing written or read (line 116), before the effects callback (lines 121-166).
  - `protected virtual void PickupBasedSkill.SerializeInternal(Stream stream)` (`PickupBasedSkill.cs:174-176`); `BowDrawSkill` and its subclasses `BowQuickdraw`, `BowSlowdraw`, `LightBeamSkill` do not override it (only `AreaAttackSkill`, `PowerStinger`, `Gatherer`, `MiningEcho` do). Protected fields `pickupIdentifier` (line 69) and `status` (line 72).
  - `PickupBasedSkillModifier.SendSerializedMessage` (`_decomp/PickupBasedSkillModifier.cs:88-116`) never calls `SerializeInternal`, so Two-Shot and Three-Shot statuses carry no build bits.
  - `PlayerProgressionManager.PathExperiences` (`PlayerProgressionManager.cs:67`), `PracticingOrb` (`:70`), `SkillOrb.Slot` (`_decomp/SkillOrb.cs:96`), `ProgressionSlot.Hash` (`_decomp/ProgressionSlot.cs:39`), `PathExperience.UnlockedSlots` (`_decomp/PathExperience.cs:38`), `Player.ProgressionManager` (`_decomp/Player.cs:161`), `IPlayer.UserInfo` (`_decomp/Alta.Networking.Scripts.Player/IPlayer.cs:16`, used as `player.UserInfo.Username` in `_decomp/Alta.Networking/EntityMessageHandler.cs:61`).
  - In `Tree.Inject`: `quick` list ordered by depth (`Tree.cs:117`), `q4` (`:118-125`), `twoSlot`, `threeSlot`, `holySlot` (`:96-113`), `gatherer` and `quiverSlot` (`:129-139`), `tree.Path` (`Tree.cs:255`).
- Produces:
  - `internal sealed class StreamBits : IBitWriter, IBitReader` with `StreamBits(Alta.Serialization.Stream stream)`; `Finished => (stream as StreamReader)?.IsFinished ?? true`; `BitsLeft => stream is StreamReader r ? Math.Max(0, (int)(r.TotalByteCount * 8) - (int)r.BitIndex) : 0` (clamped: the reader keeps counting past the end, `_decomp/Alta.Serialization/BitReader.cs:274`).
  - `internal static IReadOnlyDictionary<uint, Orb> Tree.OrbBySlot` (empty until the tree is injected), `internal static ProgressionPath Tree.RangedPath` (`ProgressionPath` is an enum, `_decomp/ProgressionPath.cs`, so it is never null; it means nothing until `Tree.Ready`).
  - `internal static Build Builds.ServerRead(Player player)`. The client's build cache per player is private state (it feeds the "is now" log); later steps expose it when they need it.

- [ ] **Step 1: The orb map in `Tree.Inject`**

After the Quiver Gather block (`Tree.cs:139`), fill `OrbBySlot`: the game's Quickdraw slots (the `quick` list without `Quickdraw4SlotHash`, ordered by depth) as `Quick1..Quick3`; `Quickdraw4SlotHash` as `Quickdraw4`; the `BowSlowdraw` slots ordered by `GetIndentLevel(tree)` as `Charge1..Charge3`; `twoSlot` as `Volley`, `threeSlot` as `FullVolley`, `holySlot` as `HolyArrow`, `quiverSlot` as `QuiverGather`, `gatherer` as `Gatherer`. Set `RangedPath = tree.Path`. Log, exactly:
`[Archery] Build: orb map {Orb}={hash} {Orb}={hash} ... ({n} archery orbs mapped)`

- [ ] **Step 2: `Builds.ServerRead`**

Only called when `Tree.Ready && Tree.OrbBySlot.Count > 0` (Step 3 writes nothing otherwise). Look the path up with `p.ProgressionManager.PathExperiences.TryGetValue(Tree.RangedPath, out var exp)`, never the indexer: a player who has never earned Ranged experience has no entry (the game creates them lazily, `_decomp/PlayerProgressionManager.cs:148-151`, `:207-210`, `:328-331`, `:356-363`), and that is an empty owned set, not an error. `Build.From` of the mapped orbs in `exp.UnlockedSlots` (none when missing) plus `PracticingOrb?.Slot?.Hash` when it maps (spec Part 12 item 1: owned slots plus the orb being tried; never `GetLevel`). Read on demand at every status message: in Step 1 this replaces the cached build and its refresh postfixes, which come with the sale fixes in Step 2. When a player's build text differs from the last one logged for them:
`[Archery] Build: '{name}' = {build} (owned {n} archery orbs{, practising {Orb}})`

- [ ] **Step 3: The patches in `Builds.cs`**

- Prefix on `PickupBasedSkill.SendSerializedMessage` stores `player` in a static `owner`; a postfix on the same method clears it.
- Postfix on `PickupBasedSkill.SerializeInternal` (by name, it is protected) with `(PickupBasedSkill __instance, Alta.Serialization.Stream stream, uint ___pickupIdentifier, ToolBasedSkillStatus ___status)`; does nothing unless `__instance is BowDrawSkill` (both sides know the skill from its hash, so writer and reader agree).
  - Server (`NetworkSceneManager.IsServer && stream.IsWriting && owner is Player p && Cfg.SendBuildBits.Value`): if the tree is not injected (`!Tree.Ready || Tree.OrbBySlot.Count == 0`), write nothing and log once the warning below, so clients read "absent" for a known reason; otherwise `BuildBits.Write(new StreamBits(stream), Builds.ServerRead(p), Cfg.SharedShort)`. Never read on the server; never write on a client.
  - Client (`!NetworkSceneManager.IsServer && stream.IsReading`): `BuildBits.TryRead(new StreamBits(stream), ...)`. `Ok` updates the cache for `owner` and compares the settings check with `Cfg.SharedShort`.
- Wrap each body in `try/catch`, logging `[Archery] Build bits: failed: {e}` as a warning.

Log lines, exactly (`{name}` = `owner?.UserInfo?.Username ?? "?"`, `{skill}` = `__instance.name`, `{arrow}` = `___pickupIdentifier`, `{cfg}` as two hex digits):
- first write on the server, once: `[Archery] Build bits: status-message patch is live (writing)`; first read on a client, once: `[Archery] Build bits: status-message patch is live (reading)`
- server, tree not injected, warning, once: `[Archery] Build bits: archery tree not injected; sending none`
- server, when `Cfg.LogWire`, once per distinct (owner, skill, status, arrow) in a row: `[Archery] Build bits: sent '{name}' {skill}/{status} arrow {arrow}: {build} cfg {cfg}`
- client `Ok`, when `Cfg.LogWire`: `[Archery] Build bits: got '{name}' {skill}/{status} arrow {arrow}: {build} cfg {cfg} | {BitsLeft} bits left`
- client `Absent`, when `Cfg.LogWire`: `[Archery] Build bits: none on '{name}' {skill}/{status} arrow {arrow} (absent)`
- client `UnknownVersion` or `Malformed`, warning, once per session: `[Archery] Build bits: '{name}' {skill}/{status} carries bits this version does not understand ({result}); ignored`
- client, `Ok` with 8 or more bits left, warning: `[Archery] Build bits: {BitsLeft} unread bits after the build bits on {skill}; the layouts disagree`
- client, cache changed: `[Archery] Build: '{name}' is now {build} (from the server)`
- client, first `Ok` with matching check, once: `[Archery] Settings: shared settings match the server (cfg {cfg})`
- client, mismatch, error, once until the client's settings are reloaded: `[Archery] Settings: SHARED SETTINGS DIFFER FROM THE SERVER (server {cfg}, this client {mine}). Copy the [shared] settings from the server's QuickshotRing.cfg.`

The `{status}` and `{arrow}` in both the "sent" and "got" lines come from the game's own fields, read before ours: matching values on both sides are the play-test's proof that the game's message still reads correctly with the bits present.

Runtime-only risk: `SerializeInternal`'s body is a bare `ret` (`PickupBasedSkill.cs:174-176`), and whether HarmonyX can detour it under Unity's Mono is known only in play. The two "patch is live" lines settle it. Fallback if either never appears after a Quickdraw or Slowdraw status: move the read and write into a postfix on `PickupBasedSkill.SendSerializedMessage` (same `__instance is BowDrawSkill` gate). The stream is at the same bit position there, because nothing is read or written after `SerializeInternal` (`PickupBasedSkill.cs:117-166` reads no bits; `PlayerProgressionManager.cs:452-453` writes nothing after), and that method is already patched and proven live (`Main.cs:123`, `HolyArrow.cs:281`).

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (105)`; build `0 Error(s)`, note warnings. Files changed: `StreamBits.cs`, `Builds.cs`, `Tree.cs`.

---

### Task 5: Draw clock and the shot claim on the shot message

**Files:**
- Create: `QuickshotRing/Claims.cs`

**Interfaces:**
- Consumes:
  - Task 1: `ShotClaim`, `ShotKind`, `ShotClaimWire`, `WireRead`. Task 2: `DrawClock`, `ClaimRules`, `ClaimStore`. Task 3: `Cfg.SendClaims`, `Cfg.LogWire`. Task 4: `StreamBits`. Existing: `Volley.BowForShooter(Shooter?)` (`Volley.cs:123-131`), `Volley.Showing(BowString)` (`Volley.cs:70-71`), `Main.QuickshotReady` (`Main.cs:45`).
  - `ShootArguments` (`_decomp/ShootArguments.cs`): struct; `target` (line 9), `strength` (line 15); `Serialize(IPlayer player, Stream stream)` (lines 30-36) writes the arrow id, chunk and position, rotation and strength (`SerializeFloat01(ref strength, 0.001f)`), and nothing after.
  - `Shooter.SyncShoot` (`_decomp/Shooter.cs:45-67`): `args.Serialize(player, stream)` first (line 47); when reading, it places the arrow and calls `Shoot(pickup, args.strength, isFromNetwork: true)` (line 61); on the server it then relays with `syncShoot.SendToChunks(args, player)` (line 64), which skips the sender. If the arrow's entity is missing or being destroyed, `Shoot` gets `pickup = null` (lines 50-61) but the relay still runs.
  - The `player` argument of `ShootArguments.Serialize` is meaningful **only on read**: it is the sending connection's player (`connection?.Player`, `_decomp/Alta.Networking/InternalMessage.cs:57`), and can be null before the connection's player is set. On write it is the first recipient on the server's relay (`Socket.cs:210-212`) or null on a client sending to the server (`Socket.cs:228`); never use it on write. The relay is serialised once for all recipients, so neither this step nor later ones may tailor the relayed bits per recipient.
  - `Shooter.Shoot(Pickup pickup, float strength, bool isFromNetwork = false, Vector3 direction = default)` (`Shooter.cs:69-131`): `PreShoot` is invoked as its first statement (line 73); a local shot is sent after the launch (lines 119-123). A Harmony prefix on `Shoot` therefore runs at the same moment as a `PreShoot` handler (spec Part 12 item 2 names `PreShoot`), without subscribing to every `Shooter` instance.
  - `MethodSyncStruct.Forward` and `Sync<T>.SendToChunks` (`_decomp/Alta.Networking/MethodSyncStruct.cs:21-28`, `_decomp/Alta.Networking/Sync.cs:61-65`): the value is stored, then serialised by the message layer. On a client, `EntityMessageHandler.SendToChunks` calls `InternalMessage.SendToServer` and then `SendToPlayers` (`_decomp/Alta.Networking/EntityMessageHandler.cs:32-36`), each serialising once (`Socket.cs:202-235`; `SendToPlayers` returns early with no recipients), so the same claim may be written twice.
  - `BowString.HeldUpdate()` (private, `_decomp/BowString.cs:269-299`), which is inferred to run on the server too (the server's pull checks hang off its `OnUpdatedStringPull`, `_decomp/BowStringPullbackInputMethod.cs:70` and `93-110`; `HeldUpdate` itself is a `PreIKUpdates` delegate subscribed in `StringGrabbed` only for a `PlayerCharacter` controller, `BowString.cs:213-217`). Not observed: play-test rows 5 and 10 settle it. `StringReleased` unsubscribes `HeldUpdate` before calling `Shoot`, so after a release the span stays active until the next grab; the timing is still right, but the "ended within grace" path covers only a string relaxed before release. Private field `currentZ` (`BowString.cs:92`) and private `float GetStrength(float z)` (`BowString.cs:306`): the game's crossing test uses `GetStrength(currentZ)` (lines 287, 298); private field `interactorHoldingBowString` (`BowString.cs:94`); `StringDock.QuantityStored` (`_decomp/Alta.Inventory/PickupDock.cs:151`), the same "arrow nocked" test the game uses (`BowStringPullbackInputMethod.cs:95`). On release, `StringReleased` calls `shooter.Shoot` synchronously (`BowString.cs:261-266`), so the clock still holds the crossing.
  - `NetworkEntity.Identifier` (`_decomp/Alta.Networking/NetworkEntity.cs:82`).
- Produces: nothing public. The clock per bow string, the two `ClaimStore`s (`outgoing`, `incoming`) and the sender names are private state of `Claims`; later steps expose what they need.

- [ ] **Step 1: Draw clock**

Postfix on `BowString.HeldUpdate` with `(BowString __instance, Interactor ___interactorHoldingBowString, float ___currentZ)`: on a client only when `___interactorHoldingBowString != null && ___interactorHoldingBowString.IsLocal`; on the server always. `clocks[bow].Update(GetStrength(bow, ___currentZ), bow.StringDock != null && bow.StringDock.QuantityStored > 0, Time.time)`, so the pull is exactly what `OnUpdatedStringPull` received. Get `GetStrength(float)` as `AccessTools.MethodDelegate<Func<BowString, float, float>>(AccessTools.Method(typeof(BowString), "GetStrength", new[] { typeof(float) }))`, as `Main.cs:41-42` does for its delegate. When the dictionary passes 64 entries, remove keys that are destroyed (`key == null`).

- [ ] **Step 2: Prefix on `Shooter.Shoot`**

Signature `(Shooter __instance, Pickup pickup, float strength, bool isFromNetwork)`. Skip when `pickup?.Entity == null` or `NetworkSceneManager.IsLocalTest`. `bow = Volley.BowForShooter(__instance)`; `ms(seconds) = (int)Math.Round(seconds * 1000)`.

- Shooter's client (`!IsServer && !isFromNetwork`): when `Cfg.SendClaims.Value`, build `new ShotClaim { Kind = Main.QuickshotReady ? Snap : Volley.Showing(bow) > 0 ? Held : Early, TimeMs = clock time or null, Stage = Volley.Showing(bow) }` and `outgoing.Put(id, claim, Time.time)`; when off, `outgoing.Remove(id)`.
- Server (`IsServer && isFromNetwork`): `serverMs` from the bow's clock with `DrawClock.Grace`; if `incoming.TryGet(id, ...)`: log the timing line, then `outgoing.Put(id, ClaimRules.ServerCheck(claim), Time.time)` for the relay; else `outgoing.Remove(id)` and log the no-claim timing line.
- Server (`IsServer && !isFromNetwork`, a shot the server launches itself, which also sends `ShootArguments`, `Shooter.cs:119-123`): `outgoing.Remove(id)`, so it never carries an earlier client claim for the same arrow id.

Timing lines, exactly (`{d}` formatted `+0;-0;0`; `{name}` from the private `senders` map that Step 3 fills, `"?"` when absent, because the `Shoot` prefix receives no player and `pickup.LastPlayerInteractor` is overwritten later inside `Shoot`, `Shooter.cs:117`):
- `[Archery] Claim: timing arrow {id} from '{name}': client {c|?} ms, server {s|no crossing} ms at receipt, diff {d|?} ms, within 60 ms: {yes|no} | stage client {claim.Stage}, server {Volley.Showing(bow)}`
- `[Archery] Claim: timing arrow {id} from '{name}': no claim, server {s|no crossing} ms at receipt`

- [ ] **Step 3: Postfix on `ShootArguments.Serialize`**

Signature `(ref ShootArguments __instance, IPlayer player, Alta.Serialization.Stream stream)` (a struct: `__instance` must be `ref`). `id = __instance.target`.

- Writing (either side): if `outgoing.TryGet(id, Time.time, out claim)`, `ShotClaimWire.Write(new StreamBits(stream), claim)`; otherwise write nothing (readers see "absent"). Never remove the claim on write (Review Focus 3); log only the first write of each stored claim.
- Reading (either side): `ShotClaimWire.TryRead(new StreamBits(stream), out claim)`; `Ok` → `incoming.Put(id, claim, Time.time)`; anything else → `incoming.Remove(id)`.
- Reading on the server, additionally and before storing `incoming`: `outgoing.Remove(id)` (if `Shoot` is then skipped because the arrow entity is missing, the relay still runs and must not carry an older claim for this id; the `Shoot` prefix re-adds the checked claim when it runs), and `senders[id] = player?.UserInfo?.Username ?? "?"` for every read result, `Ok` or not. `senders` is a `Dictionary<uint, string>` cleared when it passes 256 entries.
- `try/catch` around the body, warning `[Archery] Claim: failed: {e}`.

Log lines, exactly (`{who}` is `player?.UserInfo?.Username ?? "?"` on a server read and `server` on a client; `{strength:0.000}` is `__instance.strength`, the game's own last field, so sender and receiver lines prove it still reads correctly):
- once per process, at the first call: `[Archery] Claim: shot-message patch is live (first use: {writing|reading})`
- client write, when `Cfg.LogWire`: `[Archery] Claim: sent arrow {id}: {claim} | strength {strength:0.000}`
- server write (the relay), when `Cfg.LogWire`: `[Archery] Claim: relayed arrow {id}: {claim} | strength {strength:0.000}`
- read `Ok`, when `Cfg.LogWire`: `[Archery] Claim: {got|got relay} arrow {id} from '{who}': {claim} | strength {strength:0.000} | {BitsLeft} bits left` (`got relay` on a client)
- read `Absent`, when `Cfg.LogWire`: `[Archery] Claim: arrow {id} from '{who}' has no claim (absent) | strength {strength:0.000}`
- read `UnknownVersion` or `Malformed`, warning: `[Archery] Claim: arrow {id} from '{who}' carries a claim this version does not understand ({result}); treated as absent`
- read `Ok` with 8 or more bits left, warning: `[Archery] Claim: {BitsLeft} unread bits after the claim on arrow {id}; the layouts disagree`

Checked while planning: `ShootArguments.Serialize` is a non-virtual instance method on a struct, `strength` is its last field (`ShootArguments.cs:35`), `Shooter.SyncShoot` calls it first on write and read, and nothing is read or written after it in the same message, so the trailer sits at the message's end. Whether the detour fires on all three sides is runtime-only (its IL is about 50 bytes, above Mono's inlining limit); the "patch is live" lines, the server's `relayed arrow` and the second client's `got relay arrow` settle it.

- [ ] **Step 4: Checkpoint**

Tests `ALL PASS (105)`; build `0 Error(s)`, note warnings. Files changed: `Claims.cs`.

---

### Task 6: Start-up and probe logs

**Files:**
- Create: `QuickshotRing/Probe.cs`
- Modify: `QuickshotRing/Tree.cs` (call `Probe.LogProgression()` at the end of `Inject`, `Tree.cs:162-164`), `QuickshotRing/Main.cs` (override `OnLateInitializeMelon` calling `Probe.LogSharedPatches(HarmonyInstance.Id)`), `QuickshotRing/Tree.cs` (also call `Probe.LogSharedPatches()` at the end of `Inject`), `QuickshotRing/Quickdraw4.cs:19` (`Marker` becomes `internal const`) and `:163-179` (`Detect.Prefix` gains `int randomSeed` and, as its first statement and before the `IsServer` early return, calls `Probe.LogFlight(__instance, speed, randomSeed)` on both sides; its `speed < Marker - 1f` gate is unchanged), `QuickshotRing/Fx.cs:127-150` (`DumpAssetsOnce`), `QuickshotRing/Volley.cs` (add `internal static Material? ResolveHeadMaterial() => HeadMaterial();`)

**Interfaces:**
- Consumes:
  - `ProgressionSettings.MaxBoughtSkills`, `MaxFreePoints`, `SellingSkillPointsRefundPercentage` (`_decomp/ProgressionSettings.cs:172-176`), private fields `maxFreePoints` (`:88`), `maxBoughtSkills` (`:91`, code default 3), `sellingSkillPointsRefundPercentage` (`:95`); `GlobalSettings<ProgressionSettings>.Instance` (as `Volley.cs:413`). RevampedSkills 0.4.4 patches `ProgressionSettings.get_MaxBoughtSkills`, so with it loaded the property returns its cap; the private field is the game's asset value.
  - `Arrow.useNewPrecision` (private, `_decomp/Arrow.cs:40`), `Arrow.ArrowSettings` (`:119-129`), `Arrow.Rigidbody` (`:108`); `Arrow.StartHandlingArrowDeviance(float speed, int randomSeed = -1)` (`:235-253`): a client's call from the server's flight message passes the server's seed (`SyncFlight`, `:168-175`), a local launch passes -1 (`ShootArrowWithVelocity`, `:285-288`).
  - `SpeedWire.Hundredths/IsMarker` (Task 2), `Quickdraw4.Marker`.
  - HarmonyX (installed `MelonLoader\net35\0Harmony.dll`): static `Harmony.GetAllPatchedMethods()` and `Harmony.GetPatchInfo(MethodBase)`; `Patches` with `Owners` (a property), `Prefixes`, `Postfixes`, `Transpilers`, `Finalizers`, `ILManipulators`.
  - `Arrow.StartHandlingArrowDeviance` on the server sends the unrounded `speed` it was given to clients (`_decomp/Arrow.cs:248-251`); the server computes its own velocity in its `Shoot` (`Shooter.cs:99-100`), so a shooter's "local launch" speed is not the value clients receive. MelonLoader: `MelonBase.RegisteredMelons`, `MelonBase.OnLateInitializeMelon`, `HarmonyInstance` (checked on the installed `MelonLoader.dll` while planning). RevampedSkills' `MelonInfo` name is `"Revamped Skills"` (with a space).
  - Existing asset helpers: `Fx.FindEffect(string)` (`Fx.cs:99-109`), `PooledObjectDefinition.Prefab`; the tool-effect material path in `Volley.HeadMaterial` (`Volley.cs:412-416`).
- Produces: `internal static class Probe { static bool RevampedSkillsLoaded { get; } static void LogProgression(); static void LogSharedPatches(string? myHarmonyId = null) /* stores the id on the first call; later calls reuse it */; static void LogFlight(Arrow arrow, float speed, int randomSeed); }` plus its own postfix on `BowString.HeldUpdate` that logs the arrow prefabs once per process.

Log lines, exactly, one step each:

- [ ] **Step 1: Progression caps** (both sides, at tree load; on failure a warning naming the exception)

`[Archery] Progression: slot cap {MaxBoughtSkills} (asset value {maxBoughtSkills}), banked points cap {MaxFreePoints} (0 = no cap), sell refund {SellingSkillPointsRefundPercentage:0.###} | RevampedSkills {loaded|not loaded}`

- [ ] **Step 2: Arrow prefabs** (both sides, at the first `HeldUpdate`; `Resources.FindObjectsOfTypeAll<Arrow>()`, one line per distinct name with "(Clone)" stripped, at most 30, then a count line)

`[Archery] Arrow prefab '{name}': newer precision {bool}, flight settings '{ArrowSettings?.name ?? "-"}', gravity {useGravity}, drag {drag}, angular drag {angularDrag}, mass {mass}`
`[Archery] Arrow prefabs: {n} kinds logged`

- [ ] **Step 3: Flight speeds** (from `Quickdraw4.Detect`, both sides; `{id}` = `arrow.Entity.Identifier`)

- server, when `Cfg.LogWire`: `[Archery] Flight: sent speed {speed:R} m/s (hundredths {SpeedWire.Hundredths(SpeedWire.RoundTrip(speed))}) for arrow {id}`
- client, seed `!= -1` (from the server), when `Cfg.LogWire`: `[Archery] Flight: received speed {speed:R} m/s (hundredths {SpeedWire.Hundredths(speed)}) for arrow {id}`
- client, seed `== -1`, when `Cfg.LogWire`: `[Archery] Flight: local launch speed {speed:R} m/s for arrow {id}`
- client, always (not gated), when either `speed >= Quickdraw4.Marker - 1f` or `SpeedWire.IsMarker(speed, Quickdraw4.Marker)`: `[Archery] Flight: MARKER received {speed:R} (hundredths {h}, marker hundredths {SpeedWire.Hundredths(SpeedWire.RoundTrip(Quickdraw4.Marker))}), rounded rule {agrees|DISAGREES} with the >= 998 test`

If no "received speed" line ever follows an ordinary shot, the game's arrows have no newer-precision flight and the speed cannot carry anything (spec Part 13 Step 1). The unit test pins `SpeedWire.RoundTrip` on .NET Framework (the test project is `net472`), not on Unity's Mono, whose float arithmetic may round differently near .5; equal server "sent" and client "received" hundredths for the same arrow id (play-test row 13) and the MARKER line settle it.

- [ ] **Step 4: Patches shared with other mods** (both sides; run at `OnLateInitializeMelon` and again once at the end of `Tree.Inject`, because RevampedSkills may patch later; each method line is logged at most once per process, keyed by method and counts, and the summary lines are logged again only when their numbers change)

Loop 1, every method whose owners include `myHarmonyId` and any other id:
`[Archery] Patches: {DeclaringType.Name}.{Name} is also patched by {other owners joined ", "} (prefixes {n}, postfixes {n}, transpilers {n}, finalizers {n}, ilmanipulators {n})`
`[Archery] Patches: {k} methods shared with other mods | RevampedSkills {loaded|not loaded}`

Loop 2, when RevampedSkills is loaded (its melon found in `MelonBase.RegisteredMelons` by `Info.Name == "Revamped Skills"`, its id = that melon's `HarmonyInstance.Id`): every method in `Harmony.GetAllPatchedMethods()` whose `Owners` include that id, whether or not this mod patches it (this is how the methods Step 2 must set priorities for, such as `IsSlotAvailable`, `BowStringPullbackInputMethod.Deactivate` and `Reset`, become visible, along with any patch RevampedSkills adds at runtime to a method Step 2 will patch, such as `PlayerProgressionManager.LockSlot`, and how the two reflection-found targets listed below are confirmed):
`[Archery] Patches: RevampedSkills patches {DeclaringType.Name}.{Name} (prefixes {n}, postfixes {n}, transpilers {n}, finalizers {n}, ilmanipulators {n})`
`[Archery] Patches: RevampedSkills patches {k} methods`

The spec asks for RevampedSkills' patch list "by decompiling its DLL now"; that was done while writing this plan (see "RevampedSkills 0.4.4 patch targets" below). This runtime line is the "whenever both mods are loaded together" half.

- [ ] **Step 5: Materials** (clients only, inside `Fx.DumpAssetsOnce`, which already runs once at the first draw, `Main.cs:153-156`)

Add three sections to `loaded_assets.txt`: `## Materials: name | shader` (every `Resources.FindObjectsOfTypeAll<Material>()`), `## Line renderers: object | material | shader`, `## Trail renderers: object | material | shader`. Then one line per named lookup from spec Part 12 item 13:

| Label | Path | Expected material |
|---|---|---|
| `orb-line` | `LineRenderer` in the prefab of pooled effect `Skill Orb Connection` | `Orb Connection Line` |
| `wind-streak` | `TrailRenderer` in the prefab of pooled effect `Quick Draw Shoot` | `WindStreak_edge_m` |
| `tool-effect` | `baseMaterial` of `ToolBasedEffectBase` on `ProgressionSettings.ToolBasedEffect.Prefab` | `Tool Effect Material` |
| `steel-song` | any `Renderer` material in the prefab of pooled effect `Steel Song Mesh Effect` | `Steel Song Tool Effect Material` |
| `weapon-trail` | a `ParticleSystemRenderer.sharedMaterial` named `WeaponTrail` | `WeaponTrail` |

`[Archery] Material lookup {label}: via {path} -> '{material}' ({shader}), expected '{expected}': {match|DIFFERENT}` or `[Archery] Material lookup {label}: via {path} -> NOT FOUND ({which step failed})`

Then the materials the mod resolved itself:
`[Archery] Materials in use: line '{Fx.LineMaterial().name}' ({shader}), ghost heads '{Volley.ResolveHeadMaterial().name}' ({shader})`

- [ ] **Step 6: Checkpoint**

Tests `ALL PASS (105)`; build `0 Error(s)`, note warnings. Files changed: `Probe.cs`, `Tree.cs`, `Main.cs`, `Quickdraw4.cs`, `Fx.cs`, `Volley.cs`.

---

### Task 7: Version, backup, deploy and the play-test checklist

**Files:**
- Modify: `QuickshotRing/Main.cs:9` (version `0.7.0`), `CLAUDE.md` (QuickshotRing file table: add `Build.cs`, `Wire.cs`, `StreamBits.cs`, `Builds.cs`, `Claims.cs`, `Probe.cs` with one line each; test command result `ALL PASS (105)`; note the `[Wire]` switches and `[shared]` settings)

**Interfaces:**
- Consumes: everything above. Produces: the installed 0.7.0 DLL on client, launcher pack copy and server.

- [ ] **Step 1: Raise the version**

`[assembly: MelonInfo(typeof(QuickshotRing.Main), "QuickshotRing", "0.7.0", "dmand")]`.

- [ ] **Step 2: Update `CLAUDE.md`**

As listed under Files. Keep the existing rows.

- [ ] **Step 3: Back up before deploying** (spec Part 13, "Before every deploy")

Copy into `C:\Users\dmand\OneDrive\Desktop\attmod\backups\2026-11-12-pre-0.7.0\`: the folder `C:\Users\dmand\AppData\Roaming\A Township Tale\Servers\-1\Save`, `C:\Users\dmand\OneDrive\Desktop\A Township Tale (client\UserData\QuickshotRing.cfg` (as `client-QuickshotRing.cfg`), `C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\UserData\QuickshotRing.cfg` (as `server-QuickshotRing.cfg`), and both installed `Mods\QuickshotRing.dll` (as `client-` and `server-QuickshotRing.dll`). Expected: the five items exist in the backup folder.

Then read the `[Settings] Version` value in the server's `QuickshotRing.cfg`. If it is below 3 (it was 2 while planning: the server last ran 0.6.0), tell the user before deploying, in these words: "The server's settings file is from an older version and will be reset to defaults on first start; the old file is in the backup." Today the server's file differs from the client's only in `[TreeLayout] ArrowGatherer` and `QuiverGather`, which this plan makes shared; the server's old `[TreeLayout] ArrowGatherer` and `QuiverGather` values are the 0.6.0 three-orb layout from before the gatherer merge, so the reset to defaults is kept and nothing is copied back (both files then hold the same shared values).

- [ ] **Step 4: Final checks**

Run: `dotnet run --project QuickshotRing.Tests -c Release` → `ALL PASS (105)`. Run: `dotnet build QuickshotRing -c Release` → `0 Error(s)`.

- [ ] **Step 5: Deploy**

Run: `dotnet build QuickshotRing -c Release -p:Deploy=true -p:DeployServer=true`
Expected: `0 Error(s)` and the DLL copied to the client `Mods`, `Modpacks\My mods` and the server `Mods`. This fails while the game or the server is running (the DLL is locked): if it does, stop and ask the user to close both, then run it again.

- [ ] **Step 6: Hand the play-test checklist to the user**

The user plays; the assistant reads `MelonLoader\Latest.log` on the server and on each client, and for row 11 also the game logs (`%APPDATA%\A Township Tale\Client\logs` and `%APPDATA%\A Township Tale\Servers\-1\logs`), because the game's own parse failures are logged there by NLog, not in `Latest.log`. Setup: local server with the default 8 slots and RevampedSkills disabled; the user's character (owns Quickdraw 4 and Holy Arrow). A second client is needed for the relay; if none is available, say so and record the relay as untested (spec Part 13 Step 1).

| # | Do | Expect (grep `[Archery]`) |
|---|---|---|
| 1 | Start server and client | Both: `QuickshotRing v0.7.0`; `Settings: shared checksum ... over 29 settings` (compare the hex on both sides; server, first start only, preceded by `Settings file was from an older version`, after which its checksum equals the client's if the client's 29 shared settings are at their defaults); `Build: orb map` with Quick1-3, Quickdraw4, Charge1-3, Volley, FullVolley, HolyArrow, Gatherer, QuiverGather; `Progression: slot cap` (server: the base-game cap and asset value with `RevampedSkills not loaded`); `Patches: ... methods shared` (client lists RevampedSkills' overlaps, server none); client: `Patches: RevampedSkills patches ...` lines and the `RevampedSkills patches {k} methods` count (compare with the decompiled list below) |
| 2 | Join, draw and release a few times | Server `Build bits: status-message patch is live (writing)`, client `(reading)` (the `(writing)` line needs `[Wire] SendBuildBits = true` and an injected tree; the server's `Build bits: status-message postfix fired on the server (writing)` line tells a patch that never fired apart from one that returned early; apply the `SendSerializedMessage` fallback in Task 4 Step 3 only if the "postfix fired" line is also missing after a Quickdraw or Slowdraw status); no server `archery tree not injected`; client `Settings: shared settings match the server` or the loud mismatch line; server `Build: '<you>' = Q3 ... ult:Q4+Holy ...` (Q3 because Quickdraw 4 needs Quickdraw 3; the other tiers are whatever the character owns) and client `Build: '<you>' is now` the same text |
| 3 | Same draws | Each server `Build bits: sent '<you>' <skill>/<status> arrow <n>` has a client `Build bits: got` line with the same skill, status, arrow and build text, `0`-`7 bits left`; the Quickdraw ring, Quickdraw 4 Line, volley, Holy Arrow and Quiver Gather (an arrow drawn from the quiver and shot docks back into the quiver about 15 s later) behave as in 0.6.1 (the game's own status still reads) |
| 4 | First shot | Shooter's client `Claim: shot-message patch is live (first use: writing)`, server `(first use: reading)` |
| 5 | Shots at different moments: a snap, a hold with ghosts, a long hold past Holy Arrow | Client `Claim: sent arrow <n>: <claim> | strength s`; server `Claim: got arrow <n> from '<you>'` with the same claim text and strength, `0`-`7 bits left`; server `Claim: timing arrow <n> from '<you>'`: client ms, server ms, diff and `within 60 ms` (record the diffs; they size the 60 ms tolerance). A server number, not `no crossing`, also confirms that `BowString.HeldUpdate` runs on the dedicated server. A long hold past Holy Arrow logs the claim as `Early ... stage0` on the client (Holy Arrow consumes the volley, and the Charged kind is Step 2's), and the server's stage may differ; this is expected, not a bug |
| 6 | With a second client watching | Server `Claim: relayed arrow <n>`; second client `Claim: got relay arrow <n> from 'server'` with the same claim text (steered 0, lists empty) and strength; second client `Build bits: got '<you>' ...` with the same build text as the server's `sent` line, and `Build: '<you>' is now ... (from the server)` |
| 7 | Set `[Wire] SendClaims = false` in the client's cfg, shoot, set it back | Server `Claim: arrow <n> from '<you>' has no claim (absent)` and `timing ... no claim`; the shot behaves normally |
| 8 | Set `[Wire] SendBuildBits = false` in the server's cfg, draw, set it back | Client `Build bits: none on '<you>' ... (absent)`; the ring and ghosts behave normally |
| 9 | Ordinary shot, then a Quickdraw 4 snap | Client `Flight: received speed ... (hundredths ...)` for the ordinary arrow (or only `local launch` lines: then the arrows have no newer-precision flight); for the snap `Flight: MARKER received 999 (hundredths 99900 ...) rounded rule agrees` |
| 10 | First draw | Both: `Arrow prefab '...': newer precision ..., gravity ..., drag ...` and the count line (on the server this also confirms `HeldUpdate` runs there); client: five `Material lookup` lines, `Materials in use`, and `loaded_assets.txt` now has the three new sections |
| 11 | Anywhere | No `layouts disagree`, `does not understand`, `Build bits: failed` or `Claim: failed` lines in `Latest.log`; and the game logs (`%APPDATA%\A Township Tale\Client\logs`, `%APPDATA%\A Township Tale\Servers\-1\logs`) contain no `Error handling message`, `encountered an error when trying to handle` or `Reading over data` lines dated after the deploy |
| 12 | Optional: a second client with QuickshotRing removed from its `Mods` (or still on 0.6.1) watches the user draw and shoot | Arrows fly, no new errors in its game log or `Latest.log`, and it stays connected. If no such client can be run, record "mod-less receiver untested" |
| 13 | Same ordinary shots as row 9 | For the same arrow id, server `Flight: sent speed ... (hundredths h)` and client `Flight: received speed ... (hundredths h)` show the same `h` (pins `SpeedWire.RoundTrip` on Unity's Mono) |

The first draw after joining may hitch briefly in VR, because the asset dump now also scans materials and renderers once; this is expected for this wire-test build.

If rows 3, 5, 6 or 13 show differing text, unread bits or game fields that no longer match, stop: the spec says most of the design depends on these bits surviving, so nothing else is built until that is understood.

- [ ] **Step 7: Checkpoint**

Tests `ALL PASS (105)`; build `0 Error(s)`, note warnings. Files changed in this task: `Main.cs`, `CLAUDE.md`; deployed to the three install locations; backup folder created.

---

## RevampedSkills 0.4.4 patch targets (decompiled while planning)

From `ilspycmd -p` on `<client>\Mods\RevampedSkills.dll` (MelonInfo name `Revamped Skills`, 0.4.4): `ExtraArrowUpdater.CreateExtraArrow` (two patches), `BowStringPullbackInputMethod.CheckForPullback`, `.Deactivate`, `.Reset`, `.SetArrowEntity`, `PlayerController.Initialize`, `Arrow.<StopUpdatingArrowFlight>b__53_0`, `LightBeamSkill.DestroyShotArrow`, `LightBeamSkill.HandleInputChanged`, `PickupDock.ItemDockedSound`, `BowString.OnShoot`, `Chunker.EvaluateChunk(bool)`, `NetworkRigidbody.DelayDestroy`, `DynamicChunkContentManager.UnloadSingle`, `.UnloadInternal`, `Interactable.FinishInteraction`, `Pickup.OnUndocked`, `Projectile.Shoot`, `AreaAttackSkill.DealDamage`, `HealthObject.DetectImpact`, `ProgressionSettings.get_MaxBoughtSkills`, `PlayerProgressionManager.UnlockSlot`, `PlayerProgressionManager.IsSlotAvailable`, `BoughtSkillVisualizer.Awake`, `.PrepareOnServer`, `.Disable`, `.Serialize`, `BoughtSkillOrbReplacer.FilterInteraction`, `ConnectToServerUtility.SetupConnectionToServer`, plus two found by reflection at runtime (an `AssignOrb` handler and an `<ExecuteWithHit>b__0` callback), which this static list cannot confirm; Task 6 Step 4 loop 2 lists what is actually patched in play.

Beyond the four the spec lists (Part 12 item 12), it also patches methods this mod patches now or will: `PlayerProgressionManager.UnlockSlot` (this mod's `Tree.GathererRefund` prefix today), `PlayerProgressionManager.IsSlotAvailable` (Step 2's one-ultimate postfixes), `ProgressionSettings.get_MaxBoughtSkills` (so the slot cap read with RevampedSkills loaded is its own), and `BowStringPullbackInputMethod.Deactivate` and `Reset` (Step 2's timer reset). Step 2 must set Harmony priorities for those too.

## Deferred to later steps (not in this plan)

- The cached build with refresh postfixes on `UnlockSlot`, `LockSlot`, `PracticeSlot`, `EndPractice` and `PathExperience.LockSlot`; `UpdateCounts()`, the "can buy" flag and child-skill deregistration after sales; `Tree.HasQuiverGather` moving to the build; the test-only build override (Part 12 item 1). Step 1 reads the build on demand at each status message instead.
- The `Charged` kind, line windows, steering, targets, picks and marks in the claim; checking targets and the lock on the server; corrections on the "consumed" status (Part 12 items 2 and 9b).
- Switching `Quickdraw4.Detect` to the rounded-hundredths rule, which this plan only logs.
- Per-key settings migration (Part 14): no shipped default changes in this step.
