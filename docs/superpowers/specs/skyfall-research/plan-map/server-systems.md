# Skyfall plan map: server-systems (2026-11-19, read-only)

## Map
Roots: QR = C:\Users\dmand\OneDrive\Desktop\attmod\QuickshotRing ; DEC = C:\Users\dmand\OneDrive\Desktop\attmod\_decomp ; SP = C:\Users\dmand\AppData\Local\Temp\claude\2c7b432df063cefe\3aab37a1-c87d-4a47-8bd5-6f6c1d86eeb4\scratchpad . Everything below was read in this session; nothing was built or run.

==================== (1) Main.cs (QR\Main.cs, 372 lines) ====================
- `public class Main : MelonMod` (46). Version string at line 9: `[assembly: MelonInfo(typeof(QuickshotRing.Main), "QuickshotRing", "0.17.15", "dmand")]`.
- `public override void OnInitializeMelon()` (72-123). Order: Logs.Init (75), TimingHost.Init (76), feature Init() calls (82-108: Tree ... Tablet), `Cfg.Ready();` (109), TimingHost.Refresh (110), `TestTools.Started(); TestRange.Started(); RangePhoto.Started();` (111-113), TestBuild.OpenMenu (114), VolleySkill.TryRegister (115), `SelfCheck.Run("start-up");` (116), `Cfg.Reloaded += () => SelfCheck.Run("settings reload");` (117, "subscribed last").
  * A new `Skyfall.Init()` that creates cfg entries MUST go in 82-108 (before Cfg.Ready at 109): Ready resets/migrates/saves and checksums only the categories that exist then.
  * Console registration has no Cfg dependency; it can go anywhere in OnInitializeMelon (e.g. after 113). Grants-file load: after Cfg.Ready (Cfg.Folder is valid any time; see 9).
- `public override void OnUpdate()` (176-210):
  177 TimingHost.Frame(); 178 QuiverGather.Poll; 179-180 AutoDock.Poll (try/catch); 181-182 Weight.Tick; 185-186 Ricochet.BounceTick; 187-188 TestTools.Poll; 189-190 `try { TestRange.Poll(); } catch (Exception e) { MelonLogger.Error($"[Archery] TEST RANGE: poll failed: {e.Message}"); }` (own sections, no wrapper); 191-192 VolleyFx.Pump; 193-194 Balance.Frame; 195-196 Hits.WatchTick; 197-198 Seek.Tick; 199 Tree.ClientLoad; 200-204 every 2 s `Cfg.Poll(); Tree.PollIcons();`; 205 `if (ring == null || !ring.activeSelf) return;`; 206-209 ring check.
  * CORRECTION to the brief: line 205 is NOT a headless test. It returns on the headless server (ring is null there) AND on every client whose Quickdraw ring is inactive. Nothing new may go after 205. Server ticks (and any every-machine tick) go before it, e.g. right after Seek.Tick (198). Client per-frame looks run in `OnLateUpdate()` (212-227), after `if (ApplicationManager.IsHeadless) return;` (216).
  Pattern to copy (Main.cs:145,185-186):
  ```
  TBounce = Timing.Section("Main.OnUpdate / Ricochet.BounceTick (waiting bounces)"),
  ...
  try { using (Timing.Begin(TBounce)) Ricochet.BounceTick(); }
  catch (Exception e) { MelonLogger.Error($"[Archery] Ricochet: bounce tick failed: {e.Message}"); }
  ```
  Section fields are one `static readonly int` list at 141-164 (add e.g. `TSkyfall = Timing.Section("Main.OnUpdate / Skyfall.Tick")`). Client late-frame pattern 223-224: `try { using (Timing.Begin(TTablet)) Tablet.Frame(); } catch (Exception e) { MelonLogger.Error(...); }`.
- `public override void OnApplicationQuit()` (125-133): TestTools.Shutdown, TestRange.Shutdown, AutoDock.Shutdown in a try; then TimingHost.Shutdown.
- Server test used by features: `static bool Server => NetworkSceneManager.IsServer;` (TestTools.cs:64, TestRange.cs:41). Sounds/effects guard: `if (!ApplicationManager.IsHeadless) Impact.Play(point, ...)` (HolyArrow.cs:456).

==================== (2) Cfg patterns (QR\Cfg.cs, 252 lines; QR\Rules.cs) ====================
- `internal static class Cfg` (13). Members:
  * `internal static MelonPreferences_Category Category(string name)` (34-41): one file `UserData\QuickshotRing.cfg`; returns an existing category of that name ("two owners of one table").
  * `internal static MelonPreferences_Entry<T> Shared<T>(MelonPreferences_Entry<T> entry)` (44-49): adds to the shared list and prefixes the description:
    ```
    if (!shared.Contains(entry)) shared.Add(entry);
    string d = entry.Description ?? "";
    if (!d.StartsWith("[shared] ", StringComparison.Ordinal)) entry.Description = "[shared] " + d;
    return entry;
    ```
  * `internal static uint SharedChecksum { get; private set; }` (29), `internal static byte SharedShort { get; private set; }` (30).
  * `static void UpdateChecksum(bool startup)` (51-56): `SettingsChecksum.Compute(shared.Select(e => new KeyValuePair<string,string>(e.Category.Identifier + "." + e.Identifier, SettingsChecksum.Format(e.BoxedValue))))`; `SharedShort = SettingsChecksum.Short(SharedChecksum)`; logs `[Archery] Settings: shared checksum {SharedChecksum:x8} (short {SharedShort:x2}) over {shared.Count} settings`. Run at Ready (71) and every reload (141).
  * `internal static void Ready()` (59-72): version reset (only if `[Settings] Version` < `const int SettingsVersion = 3` (17); never raise it), `MigrateSettings()`, `Logs.Refresh()`, `SaveToFile` per category, stamp, checksum.
  * `internal static event Action? Reloaded;` (24). `internal static void Poll()` (106-150): every 2 s from Main.OnUpdate:202; ONE `categories[0].LoadFromFile(false)` (123); on success Logs.Refresh, Summary line "Settings reloaded", UpdateChecksum, then each Reloaded handler in its own timing section (144-147).
  * `internal static string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "QuickshotRing");` (32). `internal static string DefaultsFile => Path.Combine(Folder, "shipped_defaults.txt");` (19).
  * `internal static Color Colour(MelonPreferences_Entry<string> entry, Color fallback)` (211-218): "#RRGGBB" entries, parse cached per string object.
- Rules.cs `internal static class SettingsChecksum` (628-647): `public static string Format(object? value)` (floats/doubles "R" invariant); `public static uint Compute(IEnumerable<KeyValuePair<string,string>>)` = FNV-1a 32 over UTF-8 "key=value\n" sorted ordinal; `public static byte Short(uint c) => (byte)(c ^ (c >> 8) ^ (c >> 16) ^ (c >> 24));`.
  Client compare today (Builds.cs:347-354): `if ((check & BuildBits.CheckMask) == (Cfg.SharedShort & BuildBits.CheckMask))` with `public const int CheckMask = 0x3F;` (Build.cs:99); mismatch -> `MelonLogger.Error("[Archery] Settings: SHARED SETTINGS DIFFER FROM THE SERVER (server {..:x2}, this client {..:x2}) ...")` once (warnedMismatch reset on reload, Builds.cs:25-27). A text tag can carry the full `Cfg.SharedChecksum` as `{0:x8}`.
- Shipped defaults (Cfg.cs:76-104 MigrateSettings): automatic for EVERY entry of every category registered before Ready; key "Category.Key"; writes `shipped_defaults.txt` each start. A brand-new key has no recorded old default, so it is left alone. Rule helpers in Rules.cs `internal static class SettingsMigration` (664-743): `ShouldReset(string key, string current, string? oldDefault, string newDefault)` (694), `IsRetired` table `retired` (670-682; add old multi-line default texts there when changing one), `Format`/`Parse`/`OldDefault`.
- Declaring a section (Seek.cs:39-90, the pattern to copy):
  ```
  internal static void Init() {
      HomingValues d = HomingValues.Default;
      var cat = Cfg.Category("SeekingVolley");
      MelonPreferences_Entry<float> F(string key, float value, string text, bool shared) {
          var e = cat.CreateEntry(key, value, description: text);
          return shared ? Cfg.Shared(e) : e;
      }
      lockMin = F("LockMinMetres", d.LockMinMetres, "The lock: ...", true);
      maxLocks = Cfg.Shared(cat.CreateEntry("MaxLocks", d.MaxLocks, description: "..."));
      chainLocks = Cfg.Shared(cat.CreateEntry("ChainLocks", HomingChain.Name(d.ChainLocks), description: "..."));
      Cfg.Reloaded += () => cache = null;
  }
  ```
  Entry fields: `static MelonPreferences_Entry<float> lockMin = null!, ...;` (Seek.cs:27-33). bool and string entries are Shared the same way (Targets.cs:33-34).
- Values holder, cached per load (Seek.cs:35, 92-123):
  ```
  static HomingValues? cache;
  internal static HomingValues Values {
      get {
          if (lockMin == null) return HomingValues.Default;   // before Init
          if (cache != null) return cache;
          var v = new HomingValues { LockMinMetres = Mathf.Max(0f, lockMin.Value), ..., SearchHz = Mathf.Clamp(searchHz.Value, 0.5f, 30f), ... };
          if (!known && chainWarned != chainLocks.Value) { chainWarned = chainLocks.Value; MelonLogger.Warning("[Archery] Seek: ... is not off, ghosts or all; ghosts is used"); }
          return cache = v;
      }
  }
  ```
  `HomingValues` is a class in the Unity-free Homing.cs with a static `Default`. Struct variant: Paths.cs:65-85 `static OrbValues? orbCache;` `internal static OrbValues OrbNumbers { get { if (ricochetMetres == null) return OrbValues.Default; if (orbCache.HasValue) return orbCache.Value; ... orbCache = v; return v; } }` and `internal static void ForgetCachedNumbers() => orbCache = null;` subscribed `Cfg.Reloaded += ForgetCachedNumbers;` (129). Rule (CLAUDE.md, SelfCheck.cs:6-39): a holder reads only its own entries and leaf accessors, never a holder that reads it.

==================== (3) SelfCheck, Timing, Logs ====================
- QR\SelfCheck.cs: `internal static class SelfCheck` (40); `internal static void Run(string when)` (42-96). Add a holder inside the try, beside the others (48-73):
  ```
  sink = Seek.Values; sets++; // H1: before OrbNumbers, which reads it
  ...
  sink = TestRange.Check(); sets++; // ... (its own [TestRange] entries only, a leaf)
  ```
  `object sink;` (47), `GC.KeepAlive(sink);` (89), success line `[Archery] Self-check: {sets} value sets and {texts} tablet texts evaluated ({when})` (90), failure `MelonLogger.Error($"[Archery] Self-check FAILED ...")` (94). The "who reads whom" graph is the header comment (13-39): add a line for the new holder. Leaf-check example to copy: `internal static int Check()` in TestRange.cs:538-545 (returns a count, reads its own entries only, never throws for a bad text).
- QR\Timing.cs: `internal static int Section(string name)` (72-81, registers once by name, never throws); `internal static TimingScope Begin(int id)` (94), `Begin(int id, string? context)` (98), `Begin(int id, uint context)` (102); `internal readonly struct TimingScope : IDisposable` (14); `internal static bool On;` (31). Usage (Targets.cs:88-90):
  ```
  static readonly int T_Centre = Timing.Section("Targets.Centre (bounds walk)");
  internal static Vector3 Centre(HealthObject h, out Transform root) {
      using var _t = Timing.Begin(T_Centre);
  ```
  Naming in use: "patch Type.Method / File.Class", "Main.OnUpdate / X.Y", "hit X.Y", "scene X", "asset X", "file X", "TestRange.Tick...".
- Logs (Cfg.cs:225-251): `internal static class Logs { internal static bool Summary { get; private set; } = true; internal static bool Debug { get; private set; } ... }`. Pattern: `if (Logs.Summary) MelonLogger.Msg($"[Archery] Path: ...");` (Paths.cs:177) and `if (Logs.Debug) MelonLogger.Msg($"[Archery] Holy: blast at ...");` (HolyArrow.cs:510). Warnings/errors always log: `MelonLogger.Warning($"[Archery] ...")`. Once-a-session warning: a `static bool warned` flag (Ricochet.cs:357, Targets.cs:104).

==================== (4) Damage ====================
QR\Targets.cs `internal static class Targets` (20):
- `internal static uint IdOf(HealthObject h)` (58-62): PrefabRoot.Identifier or 0.
- `internal static Transform RootOf(HealthObject h)` (65-68): `e != null && e.PrefabRoot != null ? e.PrefabRoot.transform : h.transform`.
- `internal static bool BoundsOf(Transform root, out Bounds b, bool includeInactive = false)` (74-85).
- `internal static Vector3 Centre(HealthObject h, out Transform root)` (89-93): bounds centre, else root + 0.5 m up. Has its own timing section.
- `internal static int Overlap(Vector3 centre, float radius, ref Collider[] buffer, int cap, ref bool warned, string what)` (99-106): `Physics.OverlapSphereNonAlloc(centre, radius, buffer, ~(1 << 2), QueryTriggerInteraction.Ignore)`, doubles the buffer while full up to cap, one warning per flag. Caller owns `static Collider[] buf = new Collider[2048]; static bool fullWarned;` (Targets.cs:21-25 uses `ColliderCap = 8192`).
- `internal static bool IsCreature(HealthObject? h) => h != null && !(h is PlayerHealth) && (h.HasCrippleStat || IsDummy(h));` (111). `internal static bool IsPlayer(HealthObject? h)` (139). `internal static Vector3? HeadOf(Player? p)` (142-146): `pc.Head.position`, else pc.transform.position, else null.
- Collider -> health: `ImpactDetector d = c.GetComponentInParent<ImpactDetector>(); if (d != null && d.HealthTarget != null) ...` (HolyArrow.cs:475-477).
Holy Arrow's blast, QR\HolyArrow.cs:466-514 (`static void Blast(Vector3 point, Collider? directHit, BeamInfo info, NetworkEntity beam)`; `class BeamInfo { public PlayerController? shooter; public float centreDamage; public Transform? bow; public HashSet<ImpactChannel>? channels; }` at 37). The recipe (486-503):
  ```
  foreach (HealthObject h in healths) {
      if (h == null) continue;
      Vector3 at = h.PositionHandler != null ? h.PositionHandler.GetPosition(point) : h.transform.position;
      float f = BlastMath.Fraction(Vector3.Distance(at, point), fullRadius.Value, radius.Value, edgeFraction.Value);
      if (f <= 0f) continue;
      ...
      var hit = new ImpactHit(info.centreDamage * healthK, cripple.Value * crippleK, 1f, f, null, ImpactType.Spiritual,
                              point, Vector3.zero, Vector3.zero, null, beam, info.shooter);
      h.DetectImpact(hit);
  ```
  It uses `Physics.OverlapSphere` (allocating) at 471 and `HashSet<HealthObject> healths` to dedupe; explosion force only `if (blastForce.Value > 0f && c.attachedRigidbody != null)` (479). Skyfall uses Targets.Overlap + IsCreature instead and no force.
- `internal static class BlastMath { public static float Fraction(float distance, float fullRadius, float radius, float edgeFraction) }` (Rules.cs:61-70): 1 inside fullRadius, linear to edgeFraction at radius, 0 beyond / NaN.
- Decompiled `public ImpactHit(float baseHealthDamage, float baseCrippleDamage, float multipliers, float effectiveness, ImpactDetector detector, ImpactType impactType, Vector3 point, Vector3 normal, Vector3 direction, Collider struckCollider, NetworkEntity wielder, PlayerController playerWielder)` (DEC\Alta.Impact\ImpactHit.cs:113). Source is `DamageSource.Impact` (99-100). `HealthDamage = BaseHealthDamage * Effectiveness * DamageMultiplier * Multipliers * HealthMultiplier` (47-57) where `DamageMultiplier` = server `ServerConfig.Settings.DamageX` (14-24). `IsValid => Effectiveness > 0f` (28).
- `public void DetectImpact(ImpactHit impact)` (DEC\HealthObject.cs:271-292): multiplies by the target's per-ImpactType multiplier and armor (only if impact.ImpactDetector != null), calls ReceiveDamage, fires OnHit. `public virtual void ReceiveDamage(float healthDamage, float crippleDamage, DamageData impact = null)` (294-323, server only): XP/assailant only for ImpactHitWithTool (301-305); skips if Health <= 0; `HealthStat.Base -= healthDamage; if (HasCrippleStat) crippleStat.Base -= crippleDamage;`. `public void Die()` (360-363) sets HealthStat.Base = 0. `public float Health { get; set; }` (115-125), `MaxHealth` (127-142), `HasCrippleStat` (175), `CrippleStat` (177), `PositionHandler` (173).
- `public enum ImpactType { Pierce, Slash, Blunt, Caustic, Poison, Explosive, Spiritual }` (DEC\Alta.Impact\ImpactType.cs). A creature's multiplier for a type may be 0 (Weight.SpiritualOf, Weight.cs:232-240 reads the private map).
- Cripple for knock-down: the batched cripple-only hit, Weight.cs:486-487:
  ```
  var hit = new ImpactHit(0f, cripple, 1f, 1f, null, ImpactType.Spiritual, h.transform.position, Vector3.zero, Vector3.zero, null, null, shooter);
  h.DetectImpact(hit);
  ```
  Knock-down = the creature's cripple pool (`h.CrippleStat`, Value/Maximum) emptied; the game then calls `Stun(float)` on Turabada, Gotera, GreaterGotera, Babu, Dragonfly through prefab events (no code caller in DEC; Weight probes it, Weight.cs:251-283). Holy Arrow's default `[HolyArrow] BlastCripple` 0.5 (HolyArrow.cs:35,52). Weight skips a creature for `[Weight] KnockDownImmunity` 4 s after a game knock-down (Weight.cs:51,474).
- Other server helpers: `internal static bool Loaded(Vector3 point)` (Hits.cs:111-117, `LocationChunkHelper.GetBestContaining(point)?.IsLoaded`); `internal static bool Dealing;` (Hits.cs:29). Due-time queue: `internal sealed class BounceQueue<T>` (Spectral.cs:555-585): `public const int Cap = 512; public int Count; public bool Add(T item, float dueAt); public int TakeDue(float now, List<T> into); public void Clear();`. Server use (Ricochet.cs:350,363-369):
  ```
  static readonly BounceQueue<Bounce> waiting = new BounceQueue<Bounce>();
  internal static void BounceTick() {
      if (waiting.Count == 0) return;
      dueNow.Clear();
      if (waiting.TakeDue(Time.time, dueNow) > 0)
          foreach (Bounce b in dueNow) DealBounce(b);
      dueNow.Clear();
  }
  ```
  Drop warning once a session at Ricochet.cs:357.

==================== (5) Killing a player, players, ids ====================
- Players' death is the Downed state: `PlayerHealth.HandleObjectReachedZeroHealth() => OnDowned()` (DEC\PlayerHealth.cs:127-130) -> `Downed` event (320-323) -> PlayerEventsManager (DEC\PlayerEventsManager.cs:61,68) -> `PlayingToDownedTransition` (DEC\Alta.PlayerStates.Transitions\PlayingToDownedTransition.cs:15-35) -> `DownedState` (gravestone, respawn orb, revive activity, `ServerConfig.Settings.DownedStateDuration`; DEC\Alta.PlayerStates\DownedState.cs:99-117,240-290). Health reaching 0 on the server is what starts it (HealthObject.StatChanged, 234-246).
- `public override void ReceiveDamage(float healthDamage, float crippleDamage, DamageData impact = null)` (DEC\PlayerHealth.cs:235-261), the gates, in order:
  ```
  playerDamageState.Setup(healthDamage, crippleDamage, impact);
  this.HandleDamage?.Invoke(playerDamageState);              // BlockDamageIfControlsAreLocked (119-125)
  if (playerDamageState.IsHandled || !player.IsAlive || (healthDamage > 0f && player.IsIgnoringDamage) || (isInvincibleToImpacts && impact != null && impact.Source == DamageSource.Impact)) return;
  if (impact is ImpactHitWithTool { WasByPlayer: not false }) { if (!IsPVPEnabled) return; healthDamage *= PVPMultiplier; crippleDamage *= PVPCrippleMultiplier; }
  healthDamage *= ServerHandler.Current.ServerConfig.Settings.PlayerDamageTakenMultiplier;
  base.ReceiveDamage(healthDamage, crippleDamage, impact);
  ```
  So a plain `ImpactHit` through `ph.DetectImpact(...)` (the Holy recipe) is the cleanest "normal death": no PvP gate (only ImpactHitWithTool), honours spawn invincibility (Source == Impact), IsAlive, IsIgnoringDamage, the immortals toggle (`PlayerHealth.ArePlayersAvoidingDamage`, sets healthStat.Minimum = Maximum, 109-112,132-142), and still multiplies by DamageX and PlayerDamageTakenMultiplier (0 there = no kill).
  Use a FINITE lethal number (e.g. `ph.MaxHealth * 1000f` or 1e6f), never float.MaxValue: DamageX > 1 overflows to Infinity, and Infinity x a 0 type multiplier = NaN; `Stat.Base` setter is `Mathf.Clamp(value, minimum, maximum)` (DEC\Alta.StatSystem\Stat.cs:81-92), which does not clean NaN.
- TavernLib's `player kill` for comparison (SP\tavernlib\TavernLib.ModulePorts\PlayerCommandModule.cs:697-721): turns off immortals and IsIgnoringDamage, sets PlayerDamageTakenMultiplier 1, `((HealthObject)val.Health).ReceiveDamage(float.MaxValue, 0f, new DamageData((DamageSource)2));` (2 = DamageSource.Command), restores. It BYPASSES spawn invincibility (source is not Impact), so it does not meet the design's "spawn invincibility still applies". `public enum DamageSource { FallDamage, Impact, Command, AreaOfEffect, Poison, Hunger, Unknown, Fire }` (DEC\Alta.Impact\DamageSource.cs); `public DamageData(DamageSource source)` (DamageData.cs:7).
- Spawn invincibility: `public async void GrantInvincibilityToImpacts(float invincibilityTimeInSeconds)` (PlayerHealth.cs:263-271), called on the server from `PlayerController.LinkToPlayer` (DEC\PlayerController.cs:274,311-318) and cave teleports (DEC\Alta.Caves\CaveTeleporterManager.cs:79), with `ServerConfig.Settings.ImpactInvincibilityTime`.
- Players: `public static HashSet<IPlayer> AllPlayers { get; }` (DEC\Player.cs:92; iterate `.ToList()` where it may change, as TestRangeHelpers.cs:83). `public static IPlayer Current => current;` (95, the local player on a client). `public PlayerController PlayerController { get; }` (127-137). `public UserInfoAndRole UserInfo { get; private set; }` (153). `public static Player GetPlayer(string playerName)` case-insensitive exact (921-923); `public static Player GetPlayer(int playerId)` (887-890). `public Connection ConnectionToRemotePlayer` (157).
- `UserInfoAndRole`: `public int Identifier => UserInfo.Identifier;` `public string Username => UserInfo.Username;` (DEC\UserInfoAndRole.cs:22,24). Identifier is an INT.
- PlayerController (DEC\PlayerController.cs): `public abstract Transform Head { get; }` (129; PlayerCharacter: `LocalityController.Head`, DEC\Alta.Character\PlayerCharacter.cs:88), `public bool IsAlive { get; set; }` (165), `public bool IsIgnoringDamage { get; set; }` (171), `public HealthObject Health => healthTarget;` (181; cast `as PlayerHealth`), `public PlayerMessageDisplay MessageDisplay => messageDisplay;` (187), `public Player NetworkPlayer { get; }` (117), `public virtual bool IsLocalPlayer` (113). A player's position is the Head, not the entity transform (CLAUDE.md).
- Mod helpers: `internal static string NameOf(IPlayer? p) => p?.UserInfo?.Username ?? "?";` (Builds.cs:30); `internal static string KeyOf(IPlayer? p, string name)` (Builds.cs:33-36: account id as string, else the name). Online-player loop with head, skipping the shooter and the dead (Ricochet.cs:227-233):
  ```
  foreach (IPlayer ip in Player.AllPlayers) {
      if (!(ip is Player p) || p == null) continue;
      PlayerController pc = p.PlayerController;
      if (pc == null || pc == shooter || !(pc.Health is PlayerHealth ph) || ph == null || !(ph.Health > 0f)) continue;
      Vector3? head = Targets.HeadOf(p);
      if (!head.HasValue) continue;
  ```
  "Fully loaded" gate: `internal static string? ReadyFor(string name, out Player? p, out PathExperience? exp)` (TestTools.cs:487; null = ready). Leaving is detected by polling (`Player.GetPlayer(owner) == null`, TestTools.cs:466); the mod subscribes to no join/leave event. Available: `public event Action<Player> PlayerJoined;` / `PlayerLeft;` and `public static ServerHandler Current` and `public static event Action<ServerHandler> Initialized;` (DEC\Alta.Networking.Servers\ServerHandler.cs:60,109-113). Server-settings read with a try/catch: Pvp.cs:56-67.

==================== (6) TestRangeHelpers.cs Heal and Feed (QR\TestRangeHelpers.cs, inside `internal static partial class TestRange`) ====================
Usings the cores need: `Alta.Networking.Scripts.Player`, `Alta.StatSystem` (Stat), MelonLoader, System.Linq. `Captured`, `Refuse`, `Status` are TestRange-private; only the cores move.
Heal (66-76), exact:
```
static void Heal(Captured c) {
    if (c.Name.Length == 0) { Refuse(c, "[TestBuild] Player is empty"); return; }
    string? why = TestTools.ReadyFor(c.Name, out Player? p, out _);
    if (why != null || p == null) { Refuse(c, $"'{c.Name}': {why ?? "not online"}"); return; }
    HealthObject? h = p.PlayerController != null ? p.PlayerController.Health : null;
    if (h == null) { Refuse(c, $"'{c.Name}' has no health to fill"); return; }
    float before = h.Health, max = h.MaxHealth;
    if (!(before > 0f)) { Refuse(c, $"'{c.Name}' is dead: respawn first"); return; }
    h.Health = max;
    Status(c, true, $"'{c.Name}' healed: {before:0.#} -> {h.Health:0.#} of {max:0.#} (food is not touched)");
}
```
Feed (78-99), exact (doc comment 78-79: "every online player's 'Hunger' stat to its maximum (the stat SmoothLocomotion reads by that name; its Base setter syncs it like the game's own drain). Dead players and spirits are left alone."):
```
static void Feed(Captured c) {
    int fed = 0;
    var skipped = new List<string>();
    foreach (IPlayer ip in Player.AllPlayers.ToList()) {
        if (!(ip is Player p)) continue;
        string name = Builds.NameOf(p);
        try {
            HealthObject? h = p.PlayerController != null ? p.PlayerController.Health : null;
            if (h == null || !(h.Health > 0f)) { skipped.Add(name + " (no body or dead)"); continue; }
            if (h.StatManager == null || !h.StatManager.GetStat("Hunger", out Stat hunger) || hunger == null) { skipped.Add(name + " (no hunger)"); continue; }
            float before = hunger.Value;
            hunger.Base = hunger.Maximum;
            fed++;
            if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: '{name}' hunger {before:0.##} -> {hunger.Value:0.##} of {hunger.Maximum:0.##}");
        }
        catch (Exception e) { skipped.Add(name + " (" + e.Message + ")"); }
    }
    if (fed == 0) { Refuse(c, skipped.Count > 0 ? "nobody fed: " + string.Join(", ", skipped.ToArray()) : "nobody is online"); return; }
    Status(c, true, $"hunger filled for {fed} player(s)" + (skipped.Count > 0 ? "; skipped " + string.Join(", ", skipped.ToArray()) : ""));
}
```
Registered at 22-23 via `Add(cat, "HealRequest", "heal", ..., Heal, () => TestBuild.Configured)` / `Add(cat, "FeedRequest", "fill hunger", ..., Feed)`. `public bool GetStat(string name, out Stat stat)` exists (DEC\Alta.StatSystem\StatManager.cs:161).

==================== (7) Console commands ====================
All in namespace `Alta.Console` (Root.Township.dll, already referenced). QuickshotRing has no `using Alta.Console` yet (no clash found).
- `[AttributeUsage(AttributeTargets.Class)] public class ModuleAttribute(string name, string description)` (DEC\Alta.Console\ModuleAttribute.cs:14).
- `[AttributeUsage(AttributeTargets.Method)] public class CommandAttribute` ctors `(string command, string description)` and `(string command)` (CommandAttribute.cs:16-26). `null` command = the module's default (used by the game: BaseCommands.cs:26, InfoCommandModule.cs:12).
- `public class PlayOnlyAttribute : RequirementAttribute { Check() => Application.isPlaying; }` (PlayOnlyAttribute.cs); `public class ServerOnlyAttribute : RequirementAttribute { Check() => ServerHandler.Current != null; }` (ServerOnlyAttribute.cs). No ctor args. On the class they gate the whole module (CommandPart.GetCommands checks requirements first, CommandPart.cs:41-48); a failed requirement just hides it ("Command not found").
- `public class AliasAttribute(params string[] aliases)` (AliasAttribute.cs), Class|Method.
- `public static class CommandService`; `public static CommandCollection CommandCollection { get; }` (CommandService.cs:22-33: first use collects Root.Township).
- `public Module ProcessModule(Type type, Assembly assembly, Module parent = null)` (CommandCollection.cs:67-89): a top-level module of the same name is replaced (`Modules.RemoveWhere(item => item.Name == result.Name)`, 85). `public List<Module> Collect(Assembly assembly)` (40-65) calls `assembly.GetTypes()`. Commands = static methods, public or not (`BindingFlags.Static | Public | NonPublic`, 105). Nested submodules must be PUBLIC (GetNestedTypes(), 93).
- Matching: input lowercased; module/command names compared exactly, so keep them lowercase (Module.cs:60, Command.cs:139). Results sorted `orderby item.success descending, item.command.Priority descending` (CommandService.cs:80-82) with `enum CommandResultSuccess { NotEnoughParameters, TooManyParameters, IncorrectParameterTypes, ParameterParsingFailure, Success }`, so `skyfall give X` picks Give over a parameterless null-named Status. The last `string` parameter takes the rest of the line (Command.cs:155-160,201-204); a parameter with a default is optional (Type.Missing -> default, 168-172,219-222). A returned string is the console reply (Command.cs:81-84); a thrown exception comes back wrapped (CommandService.cs:124-129).
- Threading: the WebSocket console runs each command through `MainThreadDispatcher.Run(delegate { RunCommand(request); })` (DEC\Alta.Console\WebSocketCommandHandler.cs:275-285): commands run on the main thread.
- TavernVoice template (SP\tavernvoice\TavernVoice\ServerCommands.cs, 118 lines), registration (8-21):
  ```
  [Module("tavernvoice", "TavernVoice proximity voice: status and settings")]
  internal static class ServerCommands
  {
      internal static void Register()
      {
          try { CommandService.CommandCollection.Collect(typeof(ServerCommands).Assembly); }
          catch (Exception ex) { MelonLogger.Warning("The tavernvoice console commands could not be added: " + ex.Message); }
      }
      [Command(null, "Shows the relay, the settings and every player's voice")]
      [Alias(new string[] { "status" })]
      private static string Status() { return ServerVoice.StatusText(); }
  ```
  One command (41-54):
  ```
  [Command("relay", "Turns the voice relay on or off (off: all voice goes through the game connection)")]
  private static string Relay(string value)
  {
      if (!TryOnOff(value, out var on)) return "Use: tavernvoice relay on, or tavernvoice relay off";
      ServerVoice.SetRelay(on);
      if (!on) return "Relay off (saved). Voice goes through the game connection.";
      return "Relay on (saved). Players switch to it within a few seconds.";
  }
  ```
  `private static bool TryOnOff(string value, out bool on)` (97-117): on/yes/1/true, off/no/0/false. TavernVoice has no PlayOnly/ServerOnly; it calls Register() only on the server process (ServerVoice.cs:132, from Mod.OnInitializeMelon when `ServerVoice.IsServerProcess`, Mod.cs:39-43). For Skyfall: `[PlayOnly, ServerOnly, Module("skyfall", "...")]` and `CommandService.CommandCollection.ProcessModule(typeof(SkyfallCommands), typeof(SkyfallCommands).Assembly)` in a try/catch.
- TavernLib's own commands for reference: `[PlayOnly] [Module("player", ...)] public static class PlayerCommandModule` with `[ServerOnly] [Command("message", "Sends a message to a player")] private static void Message(PlayerList players, string message, float duration = 10f)` -> `PlayerCommunicationManager.Instance.SendMessageToPlayer(player, message, duration);` (SP\tavernlib\TavernLib.ModulePorts\PlayerCommandModule.cs:30-31,685-694).

==================== (8) The signal ====================
- `public class PlayerCommunicationManager : NetworkEntityBehaviour` (DEC\PlayerCommunicationManager.cs): `public static PlayerCommunicationManager Instance => instance;` (14, set in Awake; can be null); `public void SendMessageToPlayer(IPlayer player, string message, float displayDuration = 5f)` (52-66): errors from a client; `Debug.LogWarning` and NO send when `player.PlayerController == null` (58-61); else `syncMessage.SendToPlayer(player, new PlayerTextMessage(message, displayDuration))`. `SendMessageToAllPlayers` (43-50) logs the text at Info (avoid). Client handler `private void HandlePlayerMessage(IPlayer player, Stream stream, PlayerTextMessage textMessage)` (27-41): `textMessage.Serialize(player, stream);` FIRST, then if `Player.Current.PlayerController == null` a warning, else `Player.Current.PlayerController.MessageDisplay.Display(textMessage.Message, textMessage.DisplayDuration);`.
- `public struct PlayerTextMessage` (DEC\PlayerTextMessage.cs): fields `private string message; private float displayDuration;`; `public string Message`, `public float DisplayDuration`; `public PlayerTextMessage(string message, float displayDuration)`; `public void Serialize(IPlayer player, Stream stream) { stream.SerializeString(ref message); stream.SerializeFloat(ref displayDuration, 0f, 100f, 0.1f); }` (20-24). `SerializeString(ref string value, StringEncoding encoding = StringEncoding.ASCII)` (DEC\Alta.Serialization\Stream.cs:103): keep the tag ASCII (no player names in it).
- `MethodSyncStruct<T>.Forward` passes `base.Value` by value (DEC\Alta.Networking\MethodSyncStruct.cs:21-28); Value is only set by SendToPlayer/SendToChunks on the sender (Sync.cs:61-71), so on a client a HandlePlayerMessage prefix always sees a default (null-message) struct. CustomIngots' HandlePlayerMessage prefix therefore always passes through; no conflict.
- `public class PlayerMessageDisplay : MonoBehaviour`: `public void Display(string message, float duration = 2f, DisplayMessageType type = DisplayMessageType.ForceNextKillCurrent)` (DEC\PlayerMessageDisplay.cs:51-54, this is the overload HandlePlayerMessage calls) -> `public void Display(DisplayMessage message, DisplayMessageType type = ...)` (56-92; ForceNextKillCurrent kills the popup showing). `public enum DisplayMessageType { Queue, ForceNext, ForceNextKillCurrent, OnEmptyQueueOnly }` (DEC\DisplayMessageType.cs).
- Hiding, CustomIngots (SP\dec-customingots\CustomIngots.Config.Integration\ClientConfigSync.cs): manual patch of the 3-arg overload (48-59) and the prefix (74-82):
  ```
  MethodInfo methodInfo2 = AccessTools.Method(typeof(PlayerMessageDisplay), "Display", new Type[3] { typeof(string), typeof(float), typeof(DisplayMessageType) }, (Type[])null);
  harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(ClientConfigSync), "DisplayPrefix", (Type[])null), ...);
  private static bool DisplayPrefix(string message)
  {
      if (message == null || !message.StartsWith("CUSTOM_INGOTS_CONFIG_V1|", StringComparison.Ordinal)) return true;
      ReceiveChunk(message.Substring("CUSTOM_INGOTS_CONFIG_V1|".Length));
      return false;
  }
  ```
  Its prefix const is `internal const string Prefix = "CUSTOM_INGOTS_CONFIG_V1|";` (27); chunks <= 220 chars (33).
- Reading, Pet.Client (SP\dec-pet\PetVendorDiagnostics\PetCloneMessagePatch.cs:8-30, patched at Core.cs:65 as a postfix on `AccessTools.Method(typeof(PlayerTextMessage), "Serialize")`):
  ```
  internal static void SerializedPostfix(PlayerTextMessage __instance, Stream stream)
  {
      if (!stream.IsReading || Core.Instance == null) return;
      string message = __instance.Message;
      if (message != null) { if (message.StartsWith("PET_ROSTER|", StringComparison.Ordinal)) ...
  ```
  Tags in use by others: "CUSTOM_INGOTS_CONFIG_V1|", "PET_ROSTER|", "PET_CLONE_GEAR|", "PET_CLONE|".
- QuickshotRing's own attribute form for a struct Serialize postfix (Claims.cs:196-209), to copy:
  ```
  [HarmonyPatch(typeof(ShootArguments), nameof(ShootArguments.Serialize))]
  static class Bits {
      static readonly int T = Timing.Section("patch ShootArguments.Serialize / Claims.Bits");
      static void Postfix(ref ShootArguments __instance, IPlayer player, Alta.Serialization.Stream stream) {
          using var _t = Timing.Begin(T);
          if (stream == null) return;
          try { ... if (stream.IsWriting) ...; else if (stream.IsReading) ...; }
          catch (Exception e) { MelonLogger.Warning($"[Archery] Claim: failed: {e}"); }
      }
  }
  ```
  Overload-specific attribute form in the mod: `[HarmonyPatch(typeof(SkillOrb), "UpdateAllowed", new[] { typeof(SkillStatus), typeof(bool) })]` (ShrineView.cs:422). QuickshotRing has no code touching PlayerMessageDisplay / PlayerTextMessage / PlayerCommunicationManager today.
- Server sender queue pattern (SP\dec-customingots\...\ServerConfigSync.cs:43-93): subscribe `server.PlayerJoined += QueueConfigForPlayer;`, keep `Dictionary<Player,float> PendingPlayers` with the join time, each Update: `if (instance != null && connection != null && player.PlayerController != null) { remove; send }`, give up after 60 s with a warning; sends with duration `0f` (`manager.SendMessageToPlayer(recipient, text4, 0f)`, 123).
- Client-side "joined" test used by the mod: `if (global::Player.Current == null) return; // not in a world yet` (Tree.cs:203).

==================== (9) Writing a small server file ====================
- `static void WriteWhole(string file, string text)` (QR\TestRange.cs:176-193; PRIVATE static in `internal static partial class TestRange`: make it internal or move it to a shared helper to reuse):
  ```
  Directory.CreateDirectory(Path.GetDirectoryName(file)!);
  string tmp = file + ".tmp";
  File.WriteAllText(tmp, text);
  for (int i = 0; ; i++) {
      try {
          if (File.Exists(file)) File.Replace(tmp, file, null);
          else File.Move(tmp, file);
          return;
      }
      catch (Exception e) when (i < 3 && (e is IOException || e is UnauthorizedAccessException)) { System.Threading.Thread.Sleep(15); }
      catch (Exception) {
          File.WriteAllText(file, text);
          try { File.Delete(tmp); } catch { }
          return;
      }
  }
  ```
  Callers wrap it: `try { WriteWhole(StatusFile, ...); } catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: cannot write {StatusFile}: {e.Message}"); }` (TestRange.cs:219-220, 230-231). Path: `static string StatusFile => Path.Combine(Cfg.Folder, "range-status.txt");` (171). Grants file would be `Path.Combine(Cfg.Folder, "skyfall-grants.txt")` = UserData\QuickshotRing\skyfall-grants.txt.
- Per-account file load/save with warn-once and timing (QR\Paths.cs:18,134-154; the closest analogue, but it saves with a plain non-atomic `File.WriteAllText`):
  ```
  static readonly int T_Load = Timing.Section("file Paths.Load");
  static PathStore Load(string file, ref bool warned) {
      using var _t = Timing.Begin(T_Load, file);
      try { return PathStore.Parse(File.Exists(file) ? File.ReadAllText(file) : null); }
      catch (Exception e) {
          if (!warned) { warned = true; MelonLogger.Warning($"[Archery] Path: cannot read {file}; treated as no history: {e.Message}"); }
          return PathStore.Parse(null);
      }
  }
  ```
  Unity-free store: `internal sealed class PathStore` (Path.cs:263-293): `key=value` lines, `Parse(string?)`, `Get`, `bool Set(...)` (true when changed), `Serialize()` — the model for a tested grant table (account id -> name).

## Gaps
Not verified at runtime (read-only task): that PlayerCommunicationManager.Instance is non-null on the local dedicated server; that a Harmony postfix with `ref PlayerTextMessage __instance` works on that struct (the mod does the same for the ShootArguments struct in Claims.cs:199, so it is likely); that a 0-second message shows nothing; the real length limit of a message string (Stream.SerializeString is abstract; the implementation was not read).

Corrections to earlier research:
- Main.cs:205 is not a headless return. It is the Quickdraw-ring return, taken on the server and on any client whose ring is inactive. Every tick must go before it. Client looks belong in OnLateUpdate after line 216.
- telling-the-caster.md says "console handlers may not run on the main thread". That is wrong: WebSocketCommandHandler.cs:280 dispatches through MainThreadDispatcher.Run.
- The MethodSyncStruct "old Value" seen by a HandlePlayerMessage prefix is always the default on a client, so CustomIngots' prefix never swallows other mods' messages.

"Instant kill" is the game's Downed state: gravestone, respawn orb, revive activity, and the server's DownedStateDuration. Whether a downed player can be revived on this server depends on that setting, which I did not read (it is in server config I was told not to open). A plain lethal ImpactHit is still blocked by:
- the immortals toggle;
- IsIgnoringDamage;
- locked controls (BlockDamageIfControlsAreLocked);
- spawn or cave invincibility;
- PlayerDamageTakenMultiplier or DamageX at 0.
Do not use float.MaxValue: overflow to Infinity times a 0 multiplier gives NaN, and Stat.Base's Mathf.Clamp does not clean NaN.

I found no exact cripple threshold for a knock-down. Stun is wired by prefab events, and the plain Gotera has none (CLAUDE.md). What is known: emptying the creature's CrippleStat (Value/Maximum) knocks it down on the five types Weight probes, and a creature's ImpactType multiplier (e.g. Spiritual) can be 0.

QuickshotRing has no join/leave event subscription today; it polls Player.GetPlayer. ServerHandler.PlayerJoined and PlayerLeft exist (ServerHandler.cs:111-113) but are unused by the mod.

I found no client-side hook in the mod for "left the server". Tree.ClientLoad only tests Player.Current == null.

Not covered here (other mappers' scope): the claim/seam code (Draws.Release, Claims.PutResolved, Wire), the client looks ('Larger TextPopup', the sky dial), the tests project, and the install tools.
