# Skyfall plan map: claim-and-seam (2026-11-19, read-only)

## Map
CLAIM-AND-SEAM MAP for Skyfall (read-only; nothing edited, built or run). Roots: Q = C:\Users\dmand\OneDrive\Desktop\attmod\QuickshotRing, T = ...\attmod\QuickshotRing.Tests, D = ...\attmod\_decomp. Every type below is in namespace QuickshotRing unless a D path is given. Game types ShootArguments, Shooter, BowString and ExtraArrowUpdater are in the global namespace. LightBeamSkill and BowDrawSkill are in Alta.Progression.Skills.

======================================================================
0. ONE SHOT, IN ORDER (verified in D Shooter.cs and D BowString.cs)
- SHOOTER: BowString.StringReleased quantises the strength (D BowString.cs:264 `Compressor.Instance.SerializeFloat(ref strength, 0f, 1f, 0.001f);`) and calls Shooter.Shoot (265).
  - The Shooter.Shoot prefixes run first: Claims.Shot, HolyArrow.ReleaseStrength and Steady.Decide. None has a [HarmonyPriority], so their order is not fixed.
  - Then the body (D Shooter.cs:69-131):
    - PreShoot;
    - `pickup.Undock` (80);
    - `Arrow.Shoot` (100). With useNewPrecision false and !isFromNetwork, it rotates the arrow by a random deviation (D Arrow.cs:270-273). On the server it also sends the flight sync (D Arrow.cs:248-251).
    - `new ShootArguments(pickup.Entity, strength)` (121). This compresses the rotation and writes it back to the transform (D ShootArguments.cs:23-25).
    - `syncShoot.SendToChunks(value)` (122). This serialises now, so Claims.Bits.Postfix -> Claims.Write puts the claim on the wire HERE, BEFORE OnShoot.
    - `this.OnShoot?.Invoke` (124) -> BowString.OnShoot (private, D BowString.cs:155-158) -> OnShootingEntity. Its handlers run in this order:
      1. ExtraArrowUpdater.ShootExtraArrows, subscribed first at D ExtraArrowUpdater.cs:63. Its prefix is Volley.Fire, which calls Draws.Release.
      2. Each active skill's BowDrawSkill.OnArrowShot.
      3. LightBeamSkill.DestroyShotArrow, when Holy is armed.
  - Then the BowString.OnShoot postfixes: Quickdraw4.FromRelease (High), QuiverGather.Shot, Draws.ShotReset (Last) and AutoDock.Shot (Last).
  - Last, the Shooter.Shoot postfix Quickdraw4.ClientShot.
- SERVER, in SyncShoot (D Shooter.cs:45-67):
  - `args.Serialize` reads the message. Claims.Bits.Postfix -> Read runs: `incoming.Put`, and on the server `outgoing.Remove` and `senders[id]=username`.
  - It sets the arrow's pose while the arrow is still docked (57-58).
  - It calls `Shoot(pickup, args.strength, isFromNetwork: true)` (61). The Claims.Shot server branch puts the checked claim in `outgoing`. ... -> Volley.Fire -> Draws.Release -> Claims.PutResolved (which overwrites `outgoing`).
  - Then `syncShoot.SendToChunks(args, player)` (64) sends the RELAY, written from `outgoing` by Claims.Write. The original sender is excluded.
- SPECTATOR: SyncShoot reads -> Claims.Read (`incoming.Put`) -> Shoot(isFromNetwork). Claims.Shot returns at once (`if (isFromNetwork) return;`). Then OnShoot -> Volley.Fire -> Draws.Release spectator branch -> Claims.TryRelay.
  - If the arrow entity is gone (Holy's DestroyShotArrow ran on the server before the relay), Shoot gets a null pickup and OnShoot never fires (D Shooter.cs:50-61, 78).

======================================================================
1. Wire.cs (Q\Wire.cs, Unity-free and compiled by the tests)

ShotKind (line 7): `internal enum ShotKind { Early = 0, Snap = 1, Held = 2, Charged = 3 }`

ShotClaim (lines 9-31): `internal sealed class ShotClaim`, with public fields:
- `public ShotKind Kind;` (10)
- `public bool Steered;` (11)
- `public int Locks;` (13)
- `public int? TimeMs;` (14)
- `public int Stage;` (15)
- `public List<uint> Targets = new List<uint>();` (16)
- `public List<int> Picks = new List<int>();` (17)
- `public int LockCount => !Steered ? 0 : Math.Max(1, Math.Min(ShotClaimWire.MaxLocks, Locks));` (21)
- There is NO field for the window bits when unsteered, and none for the marks.

Copy (23-26), every field listed by hand:
```
public ShotClaim Copy() => new ShotClaim {
    Kind = Kind, Steered = Steered, Locks = Locks, TimeMs = TimeMs, Stage = Stage,
    Targets = new List<uint>(Targets), Picks = new List<int>(Picks)
};
```

ToString (28-30). The tests pin its exact text, so a new field must print NOTHING when it is 0:
```
$"{Kind} steer{(Steered ? 1 : 0)}{(Steered ? " locks" + LockCount : "")} t{...}ms stage{Stage} " + $"targets[...] picks[...]"
```

ShotClaimWire (40-99): `internal static class ShotClaimWire`.
- Constants (41-42): `Version = 1, MaxTargets = 6, MaxStage = 31, TimeBits = 13, TimeUnknown = 8191, TimeMax = 8190, EntityBits = 17, PickNone = 7, MaxLocks = 4`.

`public static int Write(IBitWriter w, ShotClaim claim)` (45-70) returns the number of bits written. Layout in order:
- version 4 (55);
- kind 2 (56);
- WINDOW 2 (57);
- STEERED 1 (58);
- time 13 (59);
- stage 5 (60);
- target count 3 (61);
- targets at 17 bits each (62);
- picks at 3 bits each, `stage` of them, only if there are targets (63-67);
- MARK COUNT 2 (68), always 0.
```
53  bool steered = claim.Steered && targets.Count > 0;
54  int locks = steered ? Math.Min(claim.LockCount, targets.Count) : 0;
57  Put(steered ? (uint)(locks - 1) : 0u, 2);   // window: the locks less one when steered (H1), else 0 as before
58  Put(steered ? 1u : 0u, 1);
68  Put(0u, 2);   // mark count: reserved
```
- So an unsteered Skyfall code goes into line 57's else branch (in place of 0u).
- Marks go at line 68: the mark count, then 17 bits each. The count field allows 0-3 marks.
- A claim with Steered=true but no targets is written unsteered (53).

`public static WireRead TryRead(IBitReader r, out ShotClaim? claim)` (72-98):
- It returns Absent when `r.Finished` (74) or version==0 (76), and UnknownVersion when version != 1 (77).
```
81  uint window = r.Read(2);   // the locks less one, only with the steered bit (H1); otherwise ignored
82  c.Steered = r.Read(1) != 0;
83  c.Locks = c.Steered ? (int)window + 1 : 0;
...
92  uint markCount = r.Read(2);   // reserved: any marks are skipped
93  for (uint i = 0; i < markCount; i++) r.Read(EntityBits);
94  if (c.Steered && c.Targets.Count == 0) { c.Steered = false; c.Locks = 0; }
95  else if (c.Steered) c.Locks = Math.Min(c.Locks, c.Targets.Count);
```
- Derive a Skyfall code from `window` when the RAW bit read at line 82 is 0, before the fix-up at 94.
- Marks are read and discarded at 93. Keep them in a list instead.
- Malformed means more than 6 targets (88).

Version handling:
- Version stays 1, since the layout has room.
- A bump would make every current reader return UnknownVersion. Claims.Read then warns and treats the claim as absent (Q Claims.cs:250-251).
- An old reader ignores the unsteered window (83) and skips the marks (93), so a Skyfall claim still parses on old DLLs. They also log no leftover-bits warning, because Claims.Read warns only when 8 or more bits are left (Q Claims.cs:245-247).

Supporting types:
- `IBitWriter.Write(uint value, int bits)` and `IBitReader { uint Read(int bits); bool Finished; BitsLeft }` (Q Build.cs:9-18).
- The game adapter is `StreamBits(Stream)` (Q StreamBits.cs:8).
- The tests' fake is `FakeBits`, with Write, Read, Seal, Dump, Count, Finished and BitsLeft (T FakeBits.cs:5).

TESTS THAT PIN THE LAYOUT
- T WireTests.cs, `internal static void Run()`:
  - 49-51: the minimal claim round trips and is exactly 32 bits.
  - 52-54: the Full() claim (Steered, 6 targets, 30 picks) round trips by ToString and is exactly 224 bits.
  - 56-60: an old block with window=1 and steered=1, then 2 marks, is read past and stays aligned.
  - 61-63: Full() writes the window as 0. Full has Steered=true and Locks=0, so LockCount is 1 and the window is 0.
  - 64-72: absent at every prefix length 0-63.
  - 73-76: version 0 is absent; version 2 is UnknownVersion.
  - 77-78: 7 targets is Malformed.
  - 79-93: caps and saturation.
  - 94: exact text "Held steer0 t1234ms stage5 targets[] picks[]".
  - 95-96: writes are repeatable.
  - 97: under 8 bits left after the game's bits.
- T HomingTests.cs:
  - `static void Wire()` (191-220):
    - 192-200: 1-4 locks ride the window at the same size as unsteered.
    - 201-204: an unsteered claim writes the window as 0.
    - 205-206: a block with window=2 and steered=0 must read `!Steered && Locks == 0` (a Skyfall code read from it still passes).
    - 216-217: exact texts "Held steer1 locks2 t5ms stage1 targets[] picks[]" and "Held steer0 t5ms stage1 targets[] picks[]".
    - 218-219: a copy keeps the locks.
  - `static void Unchanged()` (420-):
    - 433-441: "unchanged: an unlocked claim's bits are the old layout's". It writes a hand-built reference bit for bit, with window 0, steered 0 and mark count 0.
    - 443-445: LockClaim.Server with no locks equals ClaimRules.ServerCheck, compared by ToString.
- T RuleTests.cs:40-46: ServerCheck clears Steered, Targets and Picks; keeps Kind, TimeMs and Stage; leaves its input untouched.
- T MultiSideTests.cs:36-45: ServerCheck with facts.
- T QuickSideTests.cs:41-48 `Claim()`: a snap claim is 32 bits and round trips.
- T ReleaseTests.cs:181-201: ClaimTrust.EffectiveMs, Why and SnapReady.

======================================================================
2. Rules.cs: ClaimRules and ClaimTrust (Q\Rules.cs)

`internal static class ClaimRules` (197-233):
- `public const int ToleranceMs = 60;`
- `public static int? Diff(int? clientMs, int? serverMs)` (201)
- `public static UltimateUse SeenUltimate(ShotKind kind, Build build)` (204-205): a relayed Snap with Quickdraw4 in the build reads as a Line.
- `public static bool Agrees(...)` (207)
```
214  public static ShotClaim ServerCheck(ShotClaim claim) {
215      var c = claim.Copy();
216      c.Steered = false;
217      c.Targets.Clear(); c.Picks.Clear();
218      return c;
219  }
224  public static ShotClaim ServerCheck(ShotClaim claim, Func<uint, TargetFacts> facts, float range) {
225      ShotClaim c = ServerCheck(claim); ... re-adds kept targets (TargetRule.Keep), at most 6
```
- It KEEPS Kind, TimeMs, Stage and Locks; Locks is not cleared, but LockCount is 0 once Steered is false. Anything Copy carries survives.
- So a new Sky field and the marks must be cleared at line 216-217. Every server path goes through ServerCheck(claim):
  - Q Claims.cs:170, 177, 179 (the last via 225);
  - Q Homing.cs:536 (LockClaim.Server).
- Claims.Check (below) is then where a server-approved request is put back.

`internal static class ClaimTrust` (566-597):
- `public const int DefaultTrustMs = 1000;`
```
569 public static int? EffectiveMs(int? serverMs, int? claimedMs, int trustMs) {
570     if (trustMs <= 0 || !claimedMs.HasValue || claimedMs.Value < 0) return serverMs;
571     int c = claimedMs.Value;
572     if (c >= ShotClaimWire.TimeMax) return serverMs;
573     if (!serverMs.HasValue) return c <= trustMs ? c : (int?)null;
574     if (c < serverMs.Value) return serverMs;
575     return c - serverMs.Value <= trustMs ? c : serverMs;
576 }
```
- `public static string Why(int? serverMs, int? claimedMs, int trustMs)` (578-590).
- The "trusted claim" idiom to copy is SnapReady (594-596):
```
public static bool SnapReady(bool serverSnap, ShotKind? claimedKind, int? claimedMs, int? effectiveMs, int windowMs) =>
    serverSnap || (claimedKind == ShotKind.Snap && claimedMs.HasValue && effectiveMs == claimedMs
                   && claimedMs.Value <= windowMs + ClaimRules.ToleranceMs);
```
- In words, "trusted" = `claimedMs.HasValue && effectiveMs == claimedMs`. Note that when the client is behind, EffectiveMs returns serverMs. If serverMs == claimedMs that also compares equal; that is fine for a ">= ArmSeconds" test.
- `[Claim] TrustMs`: `internal static int TrustMs` at Q Claims.cs:29. Its setting is created at Q Claims.cs:20-26 (`Cfg.Category("Claim")`).
- `internal sealed class ClaimStore` (601-626): Lifetime 2 s, Cap 256, with Put, TryGet and Remove. Reading never removes. Claims expire 2 s after Put, so anything Skyfall needs later (the destroy at about 1 s, the waves) must be stored by Skyfall itself.

======================================================================
3. Claims.cs (Q\Claims.cs): `internal static class Claims`

Stores and helpers:
- `static readonly ClaimStore outgoing` (39): what this machine writes on its next shot message. On the server it is the checked claim and then the relay; on the shooter, its own claim.
- `incoming` (40): what was read from the last shot message (the server's raw client claim; a spectator's relay).
- `senders` (41): Dictionary<uint,string>, server, arrow id -> username.
- `networkShots` (42): HashSet<uint>, server, client shots awaiting PutResolved.
- `internal static int? ClaimedMs(uint arrowId)` (32-33) and `internal static ShotKind? ClaimedKind(uint arrowId)` (36-37): server only, from `incoming`. Copy this pattern for a raw "requested Sky" accessor.
- `internal static List<uint> LocksOf(uint arrowId, bool local)` (66-71) is the pattern for a per-arrow accessor every machine can use:
```
float now = Time.time; ShotClaim? c;
bool got = NetworkSceneManager.IsServer || local ? outgoing.TryGet(arrowId, now, out c) : incoming.TryGet(arrowId, now, out c);
```
- `internal static List<uint>? TargetsOf(uint arrowId, bool local)` (186-191) works the same way.
- `internal static bool TryRelay(uint arrowId, out ShotClaim? claim)` (74-77): client only, from `incoming`. This is how a spectator reads the relay.

SHOOTER BUILDS ITS CLAIM: `static class Shot`, `[HarmonyPatch(typeof(Shooter), nameof(Shooter.Shoot))]`, `static void Prefix(Shooter __instance, Pickup pickup, float strength, bool isFromNetwork)` (80-138):
```
86  if (pickup == null || pickup.Entity == null || NetworkSceneManager.IsLocalTest) return;
87  uint id = pickup.Entity.Identifier;
88  outgoing.Remove(id);
89  BowString? bow = Volley.BowForShooter(__instance);
92  if (!NetworkSceneManager.IsServer) {
93      if (isFromNetwork) return;
94      Resolution r = bow != null ? Draws.ResolveLocal(bow) : new Resolution(ShotKind.Early, UltimateUse.None, default, 0);
95      var claim = new ShotClaim { Kind = r.Kind, Stage = r.Plan.Stage, TimeMs = Draws.ElapsedMs(bow) };
101     LockClaim.Put(claim, Seek.ClaimLocks(bow, pickup, r));
102     PutTargets(claim, pickup);
103     outgoing.Put(id, claim, now);
104     return; }
```
What the shooter knows at this point:
- `bow`;
- `pickup` (still DOCKED);
- the `strength` parameter. It is by value; HolyArrow.ReleaseStrength may raise it in its own prefix, and the order between the two is not fixed;
- `Draws.ElapsedMs(bow)`, the ms since its 68% crossing;
- `Draws.ArrowAtCrossing(bow)`;
- `Builds.LocalBuild()`.

The pose here is `pickup.transform` BEFORE undock, before the deviation in Arrow.Shoot and before compression. It is NOT the pose that goes on the wire. The wire pose exists only from D Shooter.cs:121 on, and is visible in Claims.Bits Write (below), which runs before Draws.Release on the shooter.

Side effects to know about:
- `Seek.ClaimLocks` (Q Seek.cs:393-419, `internal static List<uint> ClaimLocks(BowString? bow, Pickup pickup, Resolution r)`) clears the shooter's lock set in its `finally` (`aim.Clear(); SeekLook.Claimed(...)`). Skipping it leaves the lock cages alive.
- `PutTargets` (145-155) adds Ricochet's and Shatter's ids.
- So a Skyfall request must clear Steered, Locks, Targets and Picks after these calls, or skip them knowing what that leaves behind.

SERVER CHECKS IT: the same prefix, server branch (107-134):
```
107 if (!isFromNetwork) return;
109 networkShots.Add(id);
112 int? serverMs = Draws.ElapsedMs(bow);
113 int? claimedMs = ClaimedMs(id);
114 int? effective = ClaimTrust.EffectiveMs(serverMs, claimedMs, TrustMs);
117 bool got1 = incoming.TryGet(id, now, out ShotClaim? got) && got != null;
134 if (got1) outgoing.Put(id, Check(got!, bow, id, pickup, effective), now);
```
- `static ShotClaim Check(ShotClaim got, BowString? bow, uint id, Pickup? pickup, int? effectiveMs)` (160-182):
  - A steered claim goes to Seek.Check first (164-169).
  - `if (got.Targets.Count == 0) return ClaimRules.ServerCheck(got);` (170).
  - The shooter comes from `bow.Pickup.LastPlayerInteractor` -> `.NetworkPlayer` (171-172).
  - Then the target paths (175-181).
- Check gets `effectiveMs` but NOT `claimedMs`. A Skyfall "trusted" test needs claimedMs passed in, or ClaimedMs(id) read again.
- The ShootArguments pose is NOT available here today (see Bits).

SERVER RELAYS ITS DECISION: `internal static void PutResolved(uint arrowId, Resolution r, int? effectiveMs, Build build)` (49-62). It is called only from Draws.Release, Q Draws.cs:270 (`if (server) Claims.PutResolved(id, r, ms, build);`).
```
50 if (!NetworkSceneManager.IsServer || !networkShots.Remove(arrowId)) return;
52 ShotClaim claim = outgoing.TryGet(arrowId, now, out ShotClaim? had) && had != null ? had.Copy() : new ShotClaim { TimeMs = null };
53 claim.Kind = r.Kind;
54 claim.Stage = r.Plan.Stage;
55 claim.TimeMs = effectiveMs;
57 if (claim.Steered && !LockRule.Steerable(r)) {
58     LockClaim.Unsteer(claim, Targets.Wanted(build));
59     if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek: arrow {arrowId}'s locks dropped: ...");
60 }
61 outgoing.Put(arrowId, claim, now);
```
- A Skyfall branch belongs before line 57. It should set the code (1/2/3), Kind Early and Stage 0, clear Steered, Locks, Targets and Picks, set the marks, then Put.
- It runs inside Shoot, before the relay is serialised.
- With no claim from an old client it builds `new ShotClaim { TimeMs = null }`.
- The relay is written by Claims.Write from `outgoing` when SyncShoot sends it (D Shooter.cs:64).

SPECTATORS READ THE RELAY: Claims.Read (230-252) does `ShotClaimWire.TryRead` -> `incoming.Put(id, claim, now)` (242-244). Draws.Release then calls `Claims.TryRelay(id, out c)` (Q Draws.cs:241).

Claims.Bits: `[HarmonyPatch(typeof(ShootArguments), nameof(ShootArguments.Serialize))] static class Bits` (196-210):
```
199 static void Postfix(ref ShootArguments __instance, IPlayer player, Alta.Serialization.Stream stream) {
203     uint id = __instance.target;
205     if (stream.IsWriting) Write(id, stream, now);
206     else if (stream.IsReading) Read(id, player, stream, now);
```
- It records NEITHER pose NOR strength today; only `__instance.target` is used.
- `__instance` (D ShootArguments.cs:7-37) has `public uint target; public Vector3 position; public Quaternion rotation; public float strength; public Chunk chunk;`. Serialize writes target, chunk+position, `SerializeQuaternionCompressed(ref rotation)` and `SerializeFloat01(ref strength, 0.001f)`.
- In Write (shooter), `__instance.rotation` is already the compressed rotation (the ctor compressed it, D ShootArguments.cs:24). In Read (server or spectator), it is the decoded rotation. So recording `(id -> rotation, position, strength)` in this postfix gives every machine the wire values, BEFORE Draws.Release on all three machines.
- Research says the rotation is bit-identical: Compressor is reading+writing (D Compressor.cs:26-29), so the shooter keeps the decoded floats. The strength sits on the same 1/1000 grid (D Stream.cs:542-593). Position is raw on the shooter and to 1 mm on readers.
- The server's/spectator's `transform.rotation` is NOT reliable: it is set while docked, then reparented by Undock.
- Compressing a pose yourself: `Compressor.Instance.SerializeQuaternionCompressed(ref q)` (D Stream.cs:149, `public void SerializeQuaternionCompressed(ref Quaternion value, int bitsPerPart = 10)`).
- Strength is also recorded elsewhere, but privately: HolyArrow.ReleaseStrength puts `shotStrength[id] = strength` (Q HolyArrow.cs:298, 328-330). It is private, capped at 32, and readable only inside Shoot through StrengthOfShot (303-306). Not usable.

======================================================================
4. Draws.cs (Q\Draws.cs): `internal static class Draws`

DrawRecord (15-23), `internal sealed class DrawRecord`:
```
public readonly DrawClock Clock = new DrawClock();
public Resolution? Last;
public uint LastArrow;
public float LastShotAt = -1f;
public int GameResetsLogged;
public float LastPull = float.NaN, SnapLoggedAt = float.NaN;   // server
```
- `internal static DrawRecord Get(BowString bow)` (70-80).
- The holder is NOT in the record. On the server it is `static readonly Dictionary<BowString, string> holders` (331, private), bow -> Builds.NameOf, set in `NewHolder` (334-355).
- On clients the draw clock runs only for the LOCAL holder (Clock postfix, 298).

DrawClock (Q Rules.cs:141-179):
- `CrossingPull = 0.68f`, `Grace = 1f`;
- private `crossedAt` and `endedAt`;
- `public uint ArrowAtCrossing { get; }`;
- `Update(float pull, uint arrowId, float now, float dipGrace)`, `Active(now, dipGrace)`, `TryElapsed(now, grace, out seconds)`.

Accessors:
- `internal static float? Elapsed(BowString? bow)` (83-84);
- `internal static int? ElapsedMs(BowString? bow)` (90-93);
- `internal static uint ArrowAtCrossing(BowString? bow)` (87-88);
- `internal static Resolution? ReleaseOf(uint arrowId)` (96);
- `internal static int? ServerTime(BowString bow, uint arrowId, out int? serverMs, out int? claimedMs)` (137-141), which is the effective time;
- `internal static Resolution ResolveLocal(BowString bow)` (195).
- The per-frame hook (for an arm timer) is `[HarmonyPatch(typeof(BowString), "HeldUpdate")] static class Clock`, `Postfix(BowString __instance, Interactor ___interactorHoldingBowString, float ___currentZ)` (292-328).
  - Its pull is `GetStrength(__instance, ___currentZ)`, using the delegate at 33-34 (`Func<BowString, float, float>`).

Release: `internal static Resolution Release(BowString bow, NetworkEntity? arrow)` (207-287). It is called ONLY from Q Volley.cs:478, and has no timing section of its own (Volley's TResolve wraps it).
- 208-215: locals `id`, `server`, `ms`, `test`, `local`, `owner`, `r`, `build`, `side`, `name`.
- 217-227, SERVER:
  - `pc = bow.Pickup.LastPlayerInteractor ?? ShooterOf(bow, arrow)`;
  - `owner = pc.NetworkPlayer`;
  - `name = Builds.NameOf(owner)`;
  - `ms = ServerTime(...)`;
  - `r = ResolveServer(bow, owner, id)`;
  - `build = Builds.ServerBuild(owner)`.
- 228-237, LOCAL SHOOTER (`pc.IsLocalPlayer`): `r = ResolveLocal(bow)`, i.e. it decides AGAIN rather than reading its own claim.
- 238-250, SPECTATOR:
```
239 build = Builds.ClientBuild(name);
241 if (Claims.TryRelay(id, out ShotClaim? c) && c != null) {
244     bool charged = c.Kind == ShotKind.Charged;
245     int? relayedMs = c.TimeMs;
246     r = new Resolution(c.Kind, ClaimRules.SeenUltimate(c.Kind, build), ReleasePlan.From(c.Kind, c.Stage),
247         charged ? Paths.StagesFor(build).Tier(build.Charge, relayedMs / 1000f) : 0);
248     ms = relayedMs;
249 }
250 else r = Resolver.Resolve(ClientFacts(bow, false, name));
```
- 258-259: the hold stage, `r = r.WithHold(VolleyHold.ForRelease(...))` (0 for Early).
- 261-270, the RECORD (what later readers use):
```
261 DrawRecord rec = Get(bow);
262 rec.Last = r; rec.LastArrow = id; rec.LastShotAt = Time.time; rec.GameResetsLogged = 0;
266 if (id != 0u) {
267     if (releases.Count >= ReleaseCap && !releases.ContainsKey(id)) { releases.Clear(); releaseBuilds.Clear(); }
268     releases[id] = r; releaseBuilds[id] = build;
270     if (server) Claims.PutResolved(id, r, ms, build);
273     server ? Weight.Arm(id, r, build, owner, bow) : Weight.ArmClient(id, r, build, local);
```
  - Readers of the record: Quickdraw4.cs:224 and 260 (`rec.LastArrow != id || rec.Last?.Ultimate != Line`), 295 (`ReleaseOf`); Draws.cs:703-715 (ShotReset multiplier); Weight.cs:367; Shatter.cs:212.
- 279-284: the Summary line `[Archery] {side}: arrow ...`.
- 285: `if (server) Shrine.PractiseOnShot(owner);` (Q Shrine.cs:259 `internal static void PractiseOnShot(Player? owner)`). Skip it for Skyfall.

Insertion for the Skyfall decision: as the FIRST branch, before line 217. It needs `id`, `server` and the owner (server: lines 219-220; client: ShooterOf, 229-230, plus `pc.IsLocalPlayer`). To be "neutral" it still has to:
- write the record (261-268), so that later readers see a plain shot;
- call a Skyfall branch of PutResolved rather than line 270's normal call.

The Resolution type (Q Release.cs:102-120), `internal readonly struct Resolution`:
- fields `Kind`, `Ultimate`, `Plan`, `ChargedTier`, `HoldStage`;
- ctor `Resolution(ShotKind kind, UltimateUse ultimate, ReleasePlan plan, int chargedTier, int holdStage = 0)`;
- `WithHold(int)`.
- `ReleasePlan` (66-85): `new ReleasePlan(int ghosts, int pellets, int echoes)`, `Stage`, `From(kind, stage)`.
- `UltimateUse { None, Line, Beam }` (87).

NEUTRAL RESOLUTION, exactly the resolver's fall-through (Release.cs:198):
`new Resolution(ShotKind.Early, UltimateUse.None, new ReleasePlan(0, 0, 0), 0)`. Claims.Shot uses the same thing with `default` (Claims.cs:94).
What it causes downstream (checked):
- `VolleyHold.ForRelease` gives 0 (163-164);
- `LockRule.Steerable` is false (Homing.cs:291-292);
- `RealArrowMultiplier`: null for main None, else 1f (Release.cs:215-226; applied in ShotReset, Draws.cs:703-715);
- Weight.Arm drops the carry (Weight.cs:100-103). Weight.ArmClient still stores an entry for a Ricochet or Shatter owner (110-117), which is harmless for a held arrow;
- Quickdraw4.ServerShot and FromRelease do nothing (Ultimate None).

ShotReset: `[HarmonyPatch(typeof(BowString), "OnShoot")] static class ShotReset`, `[HarmonyPriority(Priority.Last)] static void Postfix(BowString __instance, Pickup __1)` (664-721), server only.
- `Pvp.Shot(id)` (674);
- it resets EVERY input with `input.Reset(true)` (689-693). For an armed Holy input this deactivates it -> HandleInputChanged(false) -> unsubscribes DestroyShotArrow and calls DestroyAllExtraArrows (D LightBeamSkill.cs:17-25; D BowStringPullbackInputMethod.cs:83-87, 112-130);
- it sets the real arrow's multiplier from `ReleaseOf(id)` (703-715).

======================================================================
5. Volley.cs Fire (Q\Volley.cs). `internal static class Volley` (27); `static readonly Dictionary<BowString, State> states` (45).

```
464 [HarmonyPatch(typeof(ExtraArrowUpdater), "ShootExtraArrows")]
465 static class Fire {
474   static void Prefix(BowString __0, NetworkEntity __1, Vector3 __2) {
475     using var _t = Timing.Begin(T, Timing.On && __1 != null ? __1.Identifier : 0u);
476     if (__0 == null) return;
478     try { using (Timing.Begin(TResolve)) r = Draws.Release(__0, __1); }
479     catch (Exception e) { ...; r = new Resolution(ShotKind.Held, UltimateUse.None, new ReleasePlan(Ghosts(__0), 0, 0), 0); }
483     ChargeFx.Released(__0, __1, r);      // try/catch
485     SnapWeight.Released(__0, __1, r);    // try/catch
487     HolyArrow.ClearClientArmed(__0);
489     try {
490       Ghost?[] ghosts = states.TryGetValue(__0, out State s) ? s.ghosts : new Ghost?[VolleyMath.Total];
491       states.Remove(__0);
492       using (Timing.Begin(TDropFx)) DropFx(__0);
493       if (__1 != null) using (Timing.Begin(TLaunch)) Launch(__0, __1, __2, r, ghosts);
494       using (Timing.Begin(TGhostsGone)) for (...) { Destroy(ghosts[i].gameObject); ghosts[i] = null; }
500     } catch (Exception e) { MelonLogger.Error($"[Archery] Volley: firing failed: {e}"); }
```
- The Timing sections are declared at 466-473 (T, TResolve, TFillRelease, TSnapRelease, TDropFx, TLaunch, TGhostsGone).
- EXACT BRANCH POINT: line 493. Replace `Launch(...)` with the Skyfall fire for a Skyfall release. Keep 483-492 (they only drop held looks on a neutral release, per the research: ChargeFx.cs:252-262, SnapWeight.cs:410-421) and keep 494-498.
- The Fire prefix today takes no `__instance`. Either add `ExtraArrowUpdater __instance`, or use `__0.ExtraArrowUpdater` (public getter, D BowString.cs:111).
- The prefix returns void, so the original ShootExtraArrows body always runs after it (D ExtraArrowUpdater.cs:99-118). That body shoots every entry in `spawnedExtraArrows` (Holy's beam), then calls RemoveExtraArrows. Changing to `static bool Prefix` and returning false would skip that.
- `static void DropFx(BowString bow)` (203-207, private): VolleyFx.Beats.Forget, GoldHold.Forget, Burst.Held.Drop.
- `internal static void Launch(BowString bow, NetworkEntity arrowEntity, Vector3 velocity, Resolution r, Ghost?[] held)` (505). Its last step is Seek.HoldReal (726-731), after every ghost has copied the head.
- `internal static void Consume(BowString bow)` (242).

Game extras (D ExtraArrowUpdater.cs):
- `public List<ExtraArrowData> SpawnedExtraArrows => spawnedExtraArrows;` (49). It returns the live private list, so `.Clear()` empties it. Use this on CLIENTS (server too if wanted).
- `public void DestroyAllExtraArrows()` (254-261): `foreach ... Arrow.Entity.Spawner.Destroy(...)`, then `RemoveExtraArrows()`. SERVER ONLY: on a client Spawner.Destroy only logs "Destroy should not be called on clients" (D ClientSpawnManager.cs:21-24).
- `RemoveExtraArrows` (263-272, private): deregisters the alignment, clears the list, and on the server sends stopAligningArrows.

`internal static bool ForgetServer(BowString bow) => bow != null && (armed.Remove(bow) | waiting.Remove(bow));` (Q HolyArrow.cs:84).
- Removing `armed` makes HolyArrow.UnarmedShot (242-250) return false for LightBeamSkill's OnArrowShot (`return armed.Remove(bowString);`). No Holy multiplier or consume.

Volley.Ghost: `internal class Ghost : MonoBehaviour` (1478-1933), nested in Volley.
- `internal static Ghost Create(Arrow arrow, int index, bool rampTestOk = true)` (1489-1527). It makes a GameObject "ArcheryGhostArrow" with:
  - a LineRenderer shaft (`Fx.NewLine`);
  - a "head" child with copies of ONLY the arrowhead meshes, from `HeadMeshes(arrow, arrow.ArrowTip.transform)`. Each copy gets `part.transform.localPosition = tip.InverseTransformPoint(mf.transform.position)`, `localRotation = Inverse(tip.rotation) * mf.transform.rotation`, `localScale = mf.transform.lossyScale`, `sharedMesh = mf.sharedMesh`, `sharedMaterial = mat` (the ghost's own HeadMaterial()/Ghost0Material(), NOT the real materials), and shadows off.
  - `headOffset = Inverse(LookRotation(arrow.transform.forward)) * tip.rotation`.
- Call it BEFORE hiding the arrow: the copy reads only drawn, enabled renderers (`Drawn`, 1637), and a hold hides them on clients (the H4 note, 726-727).
- Other API:
  - `internal static Ghost CreateSmall(Arrow arrow, string name)` (1575);
  - `internal static float HeadBackOf(Arrow arrow)` (1559);
  - `internal void Style(bool isCharged, float bright)` (1752) and `Style(in GoldHold.Tone look, float bright)` (1756);
  - `internal bool MakeHeavy()` (1764);
  - `internal void Fly(Vector3 tip, Vector3 direction, float length)` (1895);
  - `internal void MoveTo(Vector3 tip, Vector3 velocity)` (1905);
  - `internal void MoveAlong(Vector3[] points, int count, Vector3 velocity)` (1913);
  - `internal void Fade(Vector3 tip, Vector3 direction, float length, float alpha)` (1932);
  - `internal void Resize(float headScale, float shaftMetres)` (1584);
  - the properties `Homing` and `Green` (1697).
- PRIVATE helpers a whole-arrow copy (the World-Nail) would want:
  - `static List<MeshFilter> HeadMeshes(Arrow, Transform tip)` (1605);
  - `static bool Drawn(Renderer?)` (1637);
  - `static List<Renderer> HighestDetail(Transform root)` (1647-1658): every drawn renderer at LOD0, or not in any LOD;
  - `CollectLods` (1660);
  - `NearestTip` (1674).
- No existing code copies a whole arrow with its REAL materials. There is no `sharedMaterials` copy anywhere in Q.

======================================================================
6. RELEASE PATHS THAT MUST STAND ASIDE

(a) Holy Arrow, server arming. `[HarmonyPatch(typeof(LightBeamSkill), "HandleInputChanged")] static class Arm` (Q HolyArrow.cs:175-239).
- `static bool Prefix(LightBeamSkill __instance, bool isEnabled, ISkillInputMethod input, BowString bowString)` (181). Arming happens on a live draw, during the hold.
- The Postfix (228-238) adds `armed` and calls `Volley.Consume(bow)`.
- The server does not know a Skyfall arm during the hold (the caster decides), so the guard is at RELEASE, inside the Volley.Fire Skyfall branch:
  - SERVER: `HolyArrow.ForgetServer(__0)` and `__0.ExtraArrowUpdater.DestroyAllExtraArrows()` (destroys the beam that arming spawned, D LightBeamSkill.cs:30-31);
  - CLIENTS: `__0.ExtraArrowUpdater.SpawnedExtraArrows.Clear()`.
- Client state already handled: `HolyArrow.ClearClientArmed(__0)` at Volley.cs:487.

(b) The arrow sacrifice: `private void DestroyShotArrow(BowString bowString, NetworkEntity shotEntity, Vector3 arg3)` (D Alta.Progression.Skills\LightBeamSkill.cs:66-71):
```
bowString.IsIgnoringCollisionOnShoot = true;
shotEntity.Spawner.Destroy(shotEntity);
bowString.OnShootingEntity -= DestroyShotArrow;
```
- It is subscribed at arming (29) and is in the invocation list captured before Volley.Fire runs, so it cannot be unsubscribed from Fire.
- It is NOT patched anywhere in Q today.
- New patch: `[HarmonyPatch(typeof(LightBeamSkill), "DestroyShotArrow")] static bool Prefix(BowString bowString, NetworkEntity shotEntity)`. Guard: return false when shotEntity's id is a Skyfall release; Draws.Release has already run earlier in the same invocation list.
- If it is skipped, ShotReset's Reset(true) later deactivates Holy's input, which unsubscribes it and destroys the extras (D LightBeamSkill.cs:17-25).

(c) Quickdraw4.ClientShot: `[HarmonyPatch(typeof(Shooter), nameof(Shooter.Shoot))] static class ClientShot`, `static void Postfix(Shooter __instance, Pickup pickup, bool isFromNetwork)` (Q Quickdraw4.cs:378-413). It runs after OnShoot, so the Skyfall decision is known.
```
384 if (NetworkSceneManager.IsServer || pickup == null) return;
385 Arrow arrow = pickup.GetComponent<Arrow>(); if (arrow == null) return;
387 if (pickup.Entity != null) QuiverGather.NoteShot(pickup.Entity.Identifier);
390 if (isFromNetwork && results.TryGetValue(arrow, out Result done) && Time.time - done.time < 1f) { Settle(...); return; }
...
406 if (isFromNetwork) return;
407 lastLocalArrow = arrow;
409 if (Main.QuickshotReady && LocalPlayerHasIt()) ResolveOnClient(arrow, true);   // moves the arrow and draws a line
```
- Insertion: guard the local prediction at line 409 (or return after 387) when the arrow is a Skyfall release.
- `Main.QuickshotReady` (Q Main.cs:68) is normally false after a 1 s hold, because Quickdraw's 0.25 s window has expired. The guard is cheap insurance.
- Spectators: the Settle path at 390 needs the server's Q4 marker, which a neutral release never sends.

(d) Quickdraw4.FromRelease: `[HarmonyPatch(typeof(BowString), "OnShoot")]`, `[HarmonyPriority(Priority.High)] static void Postfix(BowString __instance, Pickup __1)` (248-271), server.
- Guard already present at 260: `if (rec.LastArrow != entity.Identifier || rec.Last?.Ultimate != UltimateUse.Line) return;`.
- It needs no change if Skyfall writes rec.Last = neutral (or leaves the record at an older arrow id).
- ServerShot (BowDrawSkill.OnArrowShot prefix, 215-239) has the same guard at 224.

(e) Seek, the hold to copy (Q Seek.cs):
- `sealed class Hold { public uint Release; public float Until; public Vector3 At; public float CoreEndedAt = -1f; public readonly List<Renderer> Hidden; public readonly List<Collider> Off; }` (756-760).
- `static readonly Dictionary<Arrow, Hold> holds` (761), private.
- `internal static void HoldReal(Arrow arrow, uint release)` (773-796):
```
776 if (holds.TryGetValue(arrow, out Hold old)) { holds.Remove(arrow); Free(arrow, old, false); }
779 var hold = new Hold { Release = release, Until = Time.time + Values.MaxLifeSeconds + 1f, At = arrow.transform.position };
780 holds[arrow] = hold;              // recorded BEFORE anything changes
782 Stop(arrow);
783 foreach (Collider c in arrow.GetComponentsInChildren<Collider>(false)) if (c != null && c.enabled) { hold.Off.Add(c); c.enabled = false; }
786 if (!ApplicationManager.IsHeadless) foreach (Renderer r in arrow.GetComponentsInChildren<Renderer>(false))
788     if (r != null && r.enabled && r.GetComponentInParent<PooledObject>() == null) { hold.Hidden.Add(r); r.enabled = false; }
789 if (NetworkSceneManager.IsServer && arrow.Entity != null) arrow.Entity.ForcePositionSync();
791 catch { holds.Remove(arrow); Free(arrow, hold, false); throw; }
```
- `static void Stop(Arrow arrow)` (821-828), private: StopUpdatingArrowFlight; zero the velocity if not kinematic; set `collisionDetectionMode = Discrete` BEFORE `isKinematic = true`.
- `static void Free(Arrow? arrow, Hold h, bool leaveBody)` (832-844), private, never throws: colliders back on, renderers back on, and unless leaveBody, `isKinematic = false` when the arrow is not embedded.
- `[HarmonyPatch(typeof(Arrow), nameof(Arrow.StartHandlingArrowDeviance))] static class HeldFlight`, `static bool Prefix(Arrow __instance)` (848-861). On clients it refuses the server's flight sync for a held arrow until `Until`:
  `if (NetworkSceneManager.IsServer || holds.Count == 0 || __instance == null || !holds.TryGetValue(__instance, out Hold h) || Time.time > h.Until) return true; Stop(__instance); return false;`
  The server's flight sync is sent from the server's Arrow.Shoot BEFORE its OnShoot (D Arrow.cs:248-251), so it reaches the shooter after the release. Quickdraw4.Detect (416-433) is a second prefix on the same method.
- `[HarmonyPatch(typeof(Embeddable), "SyncEmbed")] static class HeldEmbed`, `static void Prefix(Embeddable __instance, Alta.Serialization.Stream stream)` (803-819). On clients it turns a held arrow's colliders back on before the server's embed. Skyfall only needs it if the server ever embeds the held arrow; its design destroys the arrow instead.
- `internal static void Tick()` (961-1013) runs from Main.OnUpdate on every machine (Q Main.cs:197-198).
  - `if (a == null) { holds.Remove(a!); Free(null, h, true); }` (971): a destroyed arrow is freed silently.
  - The client "server set it flying = refused" rule (978-989) is Seek-specific.
  - The timeout (1004-1009) logs a Warning at `MaxLifeSeconds + 1`. `Values.MaxLifeSeconds` is clamped to 0.5-10 s (Seek.cs:108; default 4). That is why Skyfall wants its own Hold.
- Destroying the arrow on the server: `public void SceneDestroy()` (D Alta.Networking\NetworkEntity.cs:597-604), server only, calls `Spawner.Destroy(this)`. The mod uses it, e.g. `a.SceneDestroy()` at Q AutoDock.cs:457. Equivalently `entity.Spawner.Destroy(entity)`, as LightBeamSkill.cs:69 does.

(f) Auto Dock's OnShoot re-nock: `[HarmonyPatch(typeof(BowString), "OnShoot")] static class Shot`, `[HarmonyPriority(Priority.Last)] static void Postfix(BowString __instance, Pickup __1)` (Q AutoDock.cs:132-152), server.
```
139 if (!NetworkSceneManager.IsServer || __instance == null) return;
140 if (__1 != null && __1.Entity != null) unshot.Remove(__1.Entity.Identifier);
141 if (!On) return;
144 if (pc == null || pc.NetworkPlayer == null || !Builds.ServerHas(pc.NetworkPlayer, Orb.AutoDock)) return;
147 pending[__instance] = new Entry { ..., Due = Time.time + Mathf.Max(0f, seconds.Value) };
```
- It only queues the next nock (the research calls it harmless).
- If a guard is wanted: insert it after line 140 (keep the `unshot.Remove`, since the arrow WAS shot), as `if (Skyfall release id) return;`.

(g) QuiverGather's tracking:
- Server: `[HarmonyPatch(typeof(BowString), "OnShoot")] static class Shot`, `static void Postfix(Pickup arg2)` (Q QuiverGather.cs:124-134). It sets `t.shot = true; t.due = Time.time + returnSeconds.Value;` for an arrow tracked since it was drawn from the quiver (TakenFromDock, 66-90).
- A destroyed Skyfall arrow is dropped at its due time: Tick (155-193) gives `exists = false` -> `QuiverRules.Decide` returns Forget (Q Rules.cs:82) -> `tracked.Remove`.
- If a guard is wanted: after line 130, `tracked.Remove(arg2.Entity.Identifier); return;` for a Skyfall release.
- Client: `QuiverGather.NoteShot(uint)` (43-46), called from ClientShot at Quickdraw4.cs:387. It only plays the pouch ghost when an arrow docks into a quiver, so it is harmless.

======================================================================
7. MAIN LOOP HOOKS (Q\Main.cs)
- OnInitializeMelon (72-123): the Init calls are 82-108 (`Claims.Init()` at 104), then `Cfg.Ready()` (109) and `SelfCheck.Run("start-up")` (116). A Skyfall Init goes before line 109.
- OnUpdate (176-210):
  - the per-frame ticks use the pattern `try { using (Timing.Begin(TSeek)) Seek.Tick(); } catch (Exception e) { MelonLogger.Error($"[Archery] Seek: tick failed: {e.Message}"); }` (197-198);
  - the headless/ring return is at 205 (`if (ring == null || !ring.activeSelf) return;`). Server ticks go before it, e.g. after 198;
  - the section names are declared in the list at 141-164 (`static readonly int TX = Timing.Section("Main.OnUpdate / ...")`).
- OnLateUpdate (212-227) returns on the headless server at 216.

## Gaps
I could not find or check these:
1. Claims.Bits does not record ShootArguments' pose or strength anywhere. There is no existing accessor. The only per-arrow strength record is private to HolyArrow and readable only inside Shoot. A new record must be added in Claims.Bits.Postfix (Write on the shooter, Read on the server and spectators).
2. Whether the rotation the shooter compresses is bit-identical to what the server and spectators decode. The research says it is; I only confirmed the code paths (D ShootArguments.cs:24, D Compressor.cs, D Stream.cs:149), not by measurement. Float results across different CPUs are also unverified.
3. The order of the Shooter.Shoot prefixes (Claims.Shot, HolyArrow.ReleaseStrength, Steady.Decide). None sets a [HarmonyPriority], so whether Claims.Shot sees the strength after HolyArrow raised it is not determined by the code.
4. HarmonyX behaviour when several prefixes on Arrow.StartHandlingArrowDeviance return false (Quickdraw4.Detect, Seek.HeldFlight, plus a Skyfall hold). Not verified.
5. Whether a flight sync from the server can reach a spectator before the relayed shot (and so start a flight on a still-docked arrow). The ordering between the destroy and the relay channels is unknown. I did not open D InternalMessage.cs or the channel code.
6. There is no whole-arrow mesh copy that keeps the arrow's real materials. Volley.Ghost.Create copies only the head and uses the ghost material. HighestDetail and Drawn are private to Volley.Ghost.
7. How old clients render a held or destroyed arrow. That is runtime behaviour, not readable from code.
8. Whether skipping LightBeamSkill.DestroyShotArrow always leaves Holy's input active, so that ShotReset's Reset(true) unsubscribes it. That path is inferred from D BowStringPullbackInputMethod.cs:83-130 and D LightBeamSkill.cs:17-25; I did not trace the ChangedActive event wiring to BowDrawSkill.InputChangedState.
9. No test file yet pins any Skyfall field. The existing pins (WireTests.cs:49-97, HomingTests.cs:191-220 and 433-445, RuleTests.cs:40-46, MultiSideTests.cs:36-45, QuickSideTests.cs:41-48) all assume a window of 0 when unsteered and a mark count of 0, plus exact ToString text.
10. TavernLib and TavernVoice were not opened: nothing in this item list needed them.
11. No build, test or game run was done. users.json, %APPDATA% and the token and secret files were not opened.
