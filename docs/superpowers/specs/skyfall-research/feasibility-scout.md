# Feasibility scout for the admin skill (2026-11-18)

### trigger
Roots: QuickshotRing\ and _decomp\ = C:\Users\dmand\OneDrive\Desktop\attmod\QuickshotRing and ...\attmod\_decomp.
- Shot message: sent for EVERY bow shot, no skill needed. It is a MethodSyncStruct<ShootArguments> on InteractEnd (_decomp\Shooter.cs:42). It carries the arrow id, chunk+position, the compressed rotation (= direction) and strength as float01 at 0.001 (_decomp\ShootArguments.cs:30-36). The server sets the pose, runs Shoot, then relays the same args to everyone except the sender (_decomp\Shooter.cs:45-66).
- Pose and strength are already bit-identical on every machine when Volley.Fire runs. The shooter compresses strength before its own Shoot (_decomp\BowString.cs:264-265). The ShootArguments constructor compresses the rotation and writes it back onto the shooter's arrow (_decomp\ShootArguments.cs:24-25), before OnShoot fires (_decomp\Shooter.cs:119-124). So 'full draw at the sky' = strength >= x AND arrow.forward.y >= sin(minElev), decided the same way everywhere inside Volley.Fire (QuickshotRing\Volley.cs:464-501; comment at 516-517). No new bits are needed for this.
- During the hold, per machine:
  - Shooter: the BowString.HeldUpdate postfix pattern (Main.cs:284-288, Seek.cs:162-164). Pull = GetStrength(currentZ) (_decomp\BowString.cs:287,306-309). Aim = the docked arrow's forward, which the game points at the bow's hand rest every update (_decomp\BowString.cs:297,385-399).
  - Server: that postfix already runs for every held string (Draws.Clock, Draws.cs:291-322: pull, 68% crossing, arrow at crossing). The holder comes from ___interactorHoldingBowString.
  - Spectators: HeldUpdate also runs for remote holders (every player is a PlayerCharacter; IK hook added in StringGrabbed, _decomp\BowString.cs:213-216; the mod filters them out at Draws.cs:298 and Main.cs:288, and Volley.DrawHeld lays out other archers' ghosts from the docked arrow's forward, Volley.cs:1287). But no message tells a spectator that the archer is an admin. The only per-draw messages are bow-skill status broadcasts, and those exist only for skills the archer owns (Builds.cs:293-310; Draws.cs:359-361).
- Claim (end of the shot message; Claims.cs:193-252; layout Wire.cs:33-99):
  - Fields: version 4, kind 2 (Early/Snap/Held/Charged, all 4 used, Wire.cs:7), window 2, steered 1, ms since the 68% crossing 13 (8190 cap, Wire.cs:41-42), stage 5, target count 3 + up to 6 x 17-bit ids, picks 3 each, mark count 2.
  - The server relays its own decision even when the shooter sent no claim (PutResolved, Claims.cs:45-62, called from Draws.cs:270). Spectators rebuild the release from the relay (Draws.cs:238-250).
  - The ultimate is NOT on the wire: a Line is the impossible speed 999 in the flight sync (Quickdraw4.cs:23-25); a Beam is its projectile's own messages.
  - The server can check hold time with the trusted claim time (Claims.cs:111-116).
- Room for a new release kind: the kind bits are full, but there are three ways in.
  - (a) The window bits are written 0 and ignored on read when the claim is not steered (Wire.cs:57,81-83). For example, steered=0 with window=3 could mean 'sky rain', and current readers would ignore it.
  - (b) Readers read the mark count and skip 17 bits per mark (Wire.cs:68,92-93). That is up to 51 bits of payload (a seed or a quantized rain centre) that current readers silently skip.
  - (c) A Version bump makes old readers drop the claim with a warning (Wire.cs:75-77, Claims.cs:250-251).
  - In every case ShotClaim needs a new field kept through Copy (Wire.cs:23-26) and through ServerCheck, which clears steered and targets (Rules.cs:214-219).
- Holy Arrow parts that can be reused:
  - HolyGate.MayArm (string held, crossed 68%, same arrow; Release.cs:254-257) on the server's draw record (HolyArrow.cs:186-203).
  - The per-bow-string armed set (HolyArrow.cs:38-41).
  - The shooter forcing strength 1.0 into the shot message (HolyGate.ReleaseStrength, HolyArrow.cs:308-336).
- Holy Arrow parts that cannot be reused as they are:
  - The client's armed flag comes only from LightBeamSkill's Charged status (HolyArrow.cs:526-557).
  - HolyWindUp runs only on a Charge main's Fill at stage 3 with the Holy orb (ChargeFx.cs:771-788).
- HolyWindUp pieces worth lifting: the breathing ring on a white copy of 'Orb Connection Line' (ChargeFx.cs:968-986, 1018-1047); the looping hum through AudioControl with optional RTPC pitch (1050-1063, stopped at 1067-1078); bow-hand ticks (961-966); the WindUp maths (Charge.cs:293); the ready flash and both-hand haptics (HolyArrow.cs:549-553).

### admin_gating
- The game's own admin/permission layer is dead here:
  - It is built on Alta web-API roles: UserInfoAndRole (_decomp\UserInfoAndRole.cs:39-58) and DevOnly from the identity token (_decomp\Alta.Console\DevOnlyAttribute.cs:82-89).
  - Server group permissions are fetched from that API (_decomp\Alta.Console\WebSocketCommandHandler.cs:243-246). PermissionsHelper needs GroupPermissions.Console from the API, and IsMenuOrCommandsAvailableForUser simply returns false (_decomp\ATT.Character.QuickAccessMenu\PermissionsHelper.cs:16-37). AdminTool is gated on UserPermissions 'debug_features' (_decomp\AdminTool.cs:14-17).
  - TavernEssentials' own config says that API 'is gone' (C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\UserData\TavernEssentials\config.txt:134).
- TavernLib (Plugins\TavernLib.dll, same md5 on client and server) has a working public API, TavernLib.ModdingApi.Roles:
  - HasRole(Connection, role), GetRoles(username) (case-insensitive) and a RolesChanged event.
  - It reads %APPDATA%\TheModdingTavern\users.json, polls it every 1 s, reloads on change, and starts only in server mode.
  - Source (decompiled): C:\Users\dmand\AppData\Local\Temp\claude\2c7b432df063cefe\3aab37a1-c87d-4a47-8bd5-6f6c1d86eeb4\scratchpad\tavernlib — TavernLib.ModdingApi\Roles.cs:40-154, TavernLib\Tavern.cs:83-95, TavernLib\TavernDirectories.cs:18.
  - VERIFIED live: the server's MelonLoader\Latest.log:38,45 says 'Booting TavernLib in server mode' and 'Read roles for 3 users', and users.json gives 3 accounts the role 'admin'.
  - Use the API rather than reading users.json yourself: that file also holds login tokens.
  - TavernLib's own IsModerator means only moderator or owner (TavernLib.Backend.Server.Configs\UserConfig.cs:25). TavernEssentials counts moderator, owner or admin (config.txt:147). So check 'admin' explicitly, plus moderator/owner.
- Each client receives only ITS OWN roles:
  - MessageType 32, sent at InitializePlayerOnServer and on every change (TavernLib.Patches\ServerSettingsRolePatch.cs:13-38).
  - On the client: TavernServices.GetService<EntranceMessageHandler>().LocalUser.Roles / HasRole (TavernLib.Backend\EntranceMessageHandler.cs:12,27-53).
  - VERIFIED: the client's Latest.log:525-527 reads 'ReceiveRoles result: IsMod: False, Roles: ["admin"]'.
  - Result: the shooter can predict, the server decides, and spectators cannot know until the relayed claim.
- How to reach it: AccessTools.TypeByName (reflection, keeps an install without TavernLib working), or a Private=false reference to $(GameDir)\Plugins\TavernLib.dll.
- Cheapest fallback: a server-only cfg list like [TestBuild] Player (TestBuild.cs:34; case-insensitive match, Build.cs:227-231), live within 2 s (Cfg.Poll, Main.cs:200-203), plus a button in tools\TestBuildMenu.ps1, which writes the server cfg (:60-68,141).
- Lending without a shrine orb: yes, and it should NOT be an Orb.
  - The build block is exactly 32 bits, with all 3 ultimate bits used (Build.cs:32, 92-124). The 5 side and 3 bridge bits are 'reserved for good' (Build.cs:100-103).
  - TestBuild only lends existing Orb members and the game skill behind their slot (TestBuild.cs:35, 170-211).
  - The rain needs no skill at all: the server clock runs for every held string (Draws.cs:291-322), every shot reaches Volley.Fire on every machine (Volley.cs:462-501), and the server relays a claim for every client shot (Claims.cs:45-62).

### damage
- Best recipe: Holy Arrow's blast, which is the game's own Explosive recipe. A plain ImpactHit(health, cripple, 1, effectiveness, ...) goes into HealthObject.DetectImpact (HolyArrow.cs:466-514; _decomp\Explosive.cs:138-218, falloff at 225-240). What that gives:
  - Damage is server-only (_decomp\HealthObject.cs:294-299).
  - Per-ImpactType multipliers and armour apply (271-285).
  - There is NO per-tool cooldown.
  - There is NO path experience: XP is paid only through ImpactDetector.OnToolHit -> RunPlayerEvents (_decomp\Alta.Impact\ImpactDetector.cs:237-277). So an admin toy can't farm XP.
- Players: a plain ImpactHit skips the game's PvP gate. Only an ImpactHitWithTool made by a player is gated (_decomp\PlayerHealth.cs:249-257). Spawn invincibility to DamageSource.Impact, IsIgnoringDamage and PlayerDamageTakenMultiplier still apply (:243, :258). Gate players yourself, as Holy does with Pvp.HolyBlast (server PvP on, PVPMultiplier/PVPCrippleMultiplier, never the shooter; Pvp.cs:58-80, 104-108), or skip players entirely.
- Hits.Apply (a hand-made ImpactHitWithTool) is the other option:
  - It needs an arrow's tool and filters by hardness and channel (BestPart, Hits.cs:362-379).
  - It clears the detector's per-tool HitCooldown before and after (Hits.cs:218-242 vs ImpactDetector.cs:239-243) and runs under Hits.Dealing.
  - It DOES go through OnToolHit, so it pays XP and shows damage popups. Use it only if the hits should behave like arrows.
- Finding victims:
  - Use Targets.Overlap per impact: OverlapSphereNonAlloc, triggers and layer 2 skipped, buffer doubles up to a cap, warns once (Targets.cs:95-106).
  - Not Holy's allocating Physics.OverlapSphere, which also runs again on every client for the stun marker (HolyArrow.cs:471, 814).
  - Count each HealthObject once per blast (HolyArrow.cs:462-506). Falloff: BlastMath.Fraction (Rules.cs:61-70). Creature test: Targets.IsCreature (Targets.cs:111).
- Limits:
  - The game has no hits-per-frame limit. The cost is the overlaps plus one health/cripple stat change (and its sync) per hit.
  - Batch one DetectImpact per creature per tick across simultaneous impacts, as FlushSpectral does (Hits.cs:390-396, 506-541).
  - Queue server impacts by due time like BounceQueue (Spectral.cs:555-575), ticked in Main.OnUpdate BEFORE the headless return (Main.cs:183-186 vs 205).
- Knock-down:
  - It is cripple damage into CrippleStat (HealthObject.cs:318-321). The knock-down itself is prefab wiring, and the plain Gotera has none.
  - Weight's stagger ledger, knock-down immunity and interrupt are fed only by real-arrow and ghost hits, so a rain's cripple bypasses them unless it is routed through Weight.
- Side effects:
  - AddExplosionForce on every rigidbody in range (HolyArrow.cs:479; Explosive.cs:171-174) also flings players' loose gear and loot.
  - The game's Explosive also hits detectors that have no health (Explosive.cs:200-208); Holy deliberately does not (HolyArrow.cs:473-478).
  - Mass kills mean mass loot spawns (saved world entities).

### visuals
- Spectral arrow = Volley.Ghost:
  - A world-space LineRenderer shaft on the flat Sprites/Default material (renderQueue 3100, no shadows; Fx.cs:21-52).
  - Plus copies of the real arrowhead's LOD0 meshes as plain MeshRenderers with no LODGroup (Volley.cs:1489-1527; HeadMeshes 1594-1636), on a copy of the game's skill-effect material or flat colour (Volley.cs:1407-1436).
  - Ghost.Bigger(k) scales the head and the shaft width (Volley.cs:1727-1735; SeekLook uses 1.3x).
- 5 m or 30 m ghosts work:
  - There is no LOD to switch, and renderer bounds follow the transform scale, so frustum culling stays correct. The shaft length is just its 2 points.
  - The 13.6 cm head mesh will look faceted at 40x.
  - Keep sky art inside the camera's far clip: the quick menu caps it at 800 m (_decomp\Alta.QuickAccessActions\UpdateFarClipMenuAction.cs:23); the Android default is 800 m (_decomp\PlayerController.cs:66); the PC default was not read.
- Rings:
  - Fx.BuildCircle is a 48-segment unit circle in local XY with radius = scale; with useWorldSpace=false the width stays in metres (Fx.cs:55-63; SeekCage.cs:24-28,276 pools 12 cages). Use far more segments at a 30-60 m radius.
  - Glow: a white copy of 'Orb Connection Line' (ChargeFx.cs:1030-1047).
- Glyphs as big rings in the sky:
  - (a) The 'Practice Glyph' pooled prefab (loaded_assets.txt:346) is an ActionGlyphAnimator: a SpriteRenderer with a fade curve (_decomp\ActionGlyphAnimator.cs:4-80), fed from ProfessionSkill.PracticeGlyphSprites (_decomp\ActionGlyphsPlacer.cs:17-41). The mod already reads practiceGlyphSprites (Tree.cs:560-576). Scale its transform to tens of metres.
  - (b) Cheaper and fully controllable: the Tablet's rune pattern, a Texture2D found by name on a Sprites/Default copy on a mod quad (Tablet.cs:929-938, 1099-1117).
  - (c) Or an embedded PNG like the orb icons (Tree.cs:802-827), drawn from the game's Glyph_* art (QuickshotRing\Art\game_icons\Glyph_Bow_LightBeam.png, Glyph_ExplosionEffect.png).
  - On PC Unity fog is off (_decomp\Alta.Environmental.Atmosphere\DayTimeManager.cs:302-310) and Sprites/Default ignores the game's _fogColor, so sky art stays crisp.
- Pooled game effects:
  - Fx.FindEffect does one cached FindObjectsOfTypeAll and retries misses every 5 s (Fx.cs:104-138), then def.Get.
  - Loaded effects include 'Explosion', 'Firework Explosion', 'Lightbeam Hit Effect', 'Shockwave Impact', 'Small Explosion'; sounds include 'Firework Explosion Sounds', 'Huge Explosion', 'Lightbeam Shot Impact Sounds', 'Shockwave Sounds' (C:\Users\dmand\OneDrive\Desktop\A Township Tale (client\UserData\QuickshotRing\loaded_assets.txt:152,154,214,381,396,585,620,630,684). That file is only a snapshot of what was loaded at the time.
  - A size-limited pool (default maximumPoolSize 5, _decomp\PooledObjectDefinition.cs:16-28) RECYCLES its oldest active instance when full (_decomp\MaxLimitPooledObjectPool.cs:17-54). Unlimited pools Instantiate on first use, which can hitch.
  - Fx.GetScaled scales the pooled root only while it plays (Fx.cs:145-161). Whether child particle systems actually grow depends on their scaling mode; it has only been used at 1.3x (Shatter.cs:454). Unverified at 5-10x.
- Lights:
  - Fx.Flash adds a new GameObject + point Light per call, with no pooling (Fx.cs:91-98, 386-392).
  - These sit outside the game's light budget: its lamps are sorted to pixelLightCount-1 (_decomp\Alta.Lighting\PlatformLight.cs:64). Dozens of flashes steal pixel-light slots from lamps and cost a forward pass each in VR. Cap concurrent flashes at 2-4.
- Cheap debris: VolleyFx.Sparks, one shared world-space ParticleSystem driven by Emit (VolleyFx.cs:225-260).
- Holy's impact kit (racing rings, bolts, crackle, stun taser; HolyArrow.cs:706-846) makes new GameObjects per bolt (Fx.Bolt, Fx.cs:84-89): fine for one blast, needs pooling for 30.

### sky
- Changing the time of day for everyone is the wrong tool:
  - The range's Noon helper calls GameTimeManager.RushToTime(NoonRule.DayFraction(hour)), where the fraction is (hour-6)/24 (TestRangeHelpers.cs:101-107; Range.cs:559-566).
  - RushToTime is server-only. It sets a target and fast-forwards toward it, and only goes FORWARD: a target already passed means the next day (_decomp\GameTimeManager.cs:174-190, 325-347).
  - It is synced by the game's own SyncA message (isRushing, targetTime; :225-233, 282-323), saved with the world, and nightfall fires NightChanged/DuskReached for everything.
  - Getting back from a 10 s darkening would mean a whole day passing.
- Client-only darkening works, with no traffic, triggered from the relayed claim:
  - Sun and sky: DayTimeManager has two private LerpingFloatValues. LightIntensityModifier multiplies sun and moon intensity. skyColorFade drives the skybox's _SkyColorDisableFade; above 0.99 it enables _DISABLED, which means no sky, as in caves (DayTimeManager.cs:198-222, 329-338, 354-367, 719-761). The game itself lerps both in Disable/Enabled for caves (:696-717, 763-771) and advances them in LateUpdate (:477-488). Reach them with Traverse and call Lerp(0.3, t), then Lerp back.
  - Ambient and fog: AtmosphereManager.instance.AmbientColor and FogColor (AtmosphereColor) and FogDensity write RenderSettings.ambientLight, RenderSettings.fogColor and the global _fogColor the game's shaders read (_decomp\Alta.Environmental.Atmosphere\AtmosphereManager.cs:142-167, Update at 199-208).
  - DayTimeManager re-snaps ambient and fog every LateUpdate whenever they are not lerping and it has priority (DayTimeManager.cs:673-681). To hold a darker value, use a postfix on DayTimeManager.LateUpdate or keep a lerp running.
  - Restore on every way out: cave entry, scene change, disconnect.
  - Headless server: nothing to do.
- The mod's own lines (Sprites/Default) are not darkened, so they keep their contrast against a dark sky.

### same_on_every_machine
- How spectators reproduce a release today:
  - The server decides each release once (Draws.Release, Draws.cs:207-287) and relays kind, stage and time (Claims.cs:45-62).
  - Spectators rebuild the release from the relay (Draws.cs:238-250) and launch everything 'from the real arrow alone (its pose travels in the shot message), so every machine aims the same way' (Volley.cs:516-517).
  - LookSeed.Unit(arrow id, index, salt) gives identical pseudo-random numbers on every machine, for looks only (Spectral.cs:110-118).
  - ArcModel is closed-form double maths (Spectral.cs:6-44).
- The release pose and strength are already bit-identical at Volley.Fire (see trigger).
- The claim rides INSIDE the shot message, so a spectator always has it when its Shoot runs. There is no ordering race, unlike the flight sync.
- A rain pattern (centre, N arrows, offsets, delays, sizes) can be exactly the same everywhere if it is a pure function of these inputs:
  - the arrow id;
  - the relayed claim bits (the stage's 5 bits could carry a count; the mark-count slot up to 51 bits for a seed or a quantized centre, Wire.cs:68,92-93);
  - the compressed pose;
  - [shared] settings (Cfg.Shared; their checksum is compared from the build block, Builds.cs:347-354).
- Landing points:
  - A downward Hits.Cast from each sky point onto the static world gives the same point on every machine that has the chunk loaded. This is the H3 rule that 'the room comes from the static world'.
  - Check Hits.Loaded / LoadedReach first (Hits.cs:110-139): a far spectator may not have the colliders loaded.
- Never the same on every machine: creatures, players, loose items, and anything using UnityEngine.Random (e.g. Holy's wisps, HolyArrow.cs:740-750).
- Damage is server-only (HealthObject.cs:294-299), so only the visuals need to agree. The server's overlap at impact time decides who is hurt.
- The shooter never receives its own relay (Claims.cs:184-191). It must decide 'rain' locally by the same rule, using its own TavernLib roles, or it will disagree with the server.

### landing_point
- How the real arrow flies: with useNewPrecision, the game resets the velocity every physics tick to the constant flightSpeed in its current direction (plus a seeded wobble), and gravity only turns it (_decomp\Arrow.cs:223-233, 235-253; _decomp\ArrowFlightSettings.cs:65-71). Drag doesn't matter because the speed is reset each tick.
- The mod's ArcModel is that curve in closed form, dTheta/dt = -(g/s)cos Theta (Spectral.cs:6-44). VolleySim positions arrows with it (Volley.cs:996-1008) and casts each tick from the last cast point (Volley.cs:1046, 1075-1094; SpectralBudget at Spectral.cs:331-333).
- Full-draw speed:
  - Formula: maxVelocity x strength x (min(1, tightness/weight) + tightness x maxExtra) (_decomp\Shooter.cs:99-100).
  - Logged at 52.0 m/s: C:\Users\dmand\OneDrive\Desktop\attmod\.superpowers\sdd\2026-11-16-playtest6-fixes\playtest6-server.log:44741.
  - Holy beams fired at 235-275 m/s, which is 5 x 47-55 (server MelonLoader\Logs\26-11-18_17-16-16.log:34843).
  - The 100 m/s network clamp (_decomp\Alta.Networking\PhysicsSerializationSettings.cs:11, applied in Projectile.Shoot at _decomp\Projectile.cs:98) only matters for networked rigidbodies. The rain's own visuals are local and not clamped.
- Verified in play:
  - A steep 52 m/s shot was 30 m out and about 120 m up after 2.269 s, with ArcModel off by 2.00 m (playtest6-server.log:44762).
  - Flat shots were off by 0.13-0.23 m at 30 m (:24029, 35142, 45726).
- Closed form, with s = 52 and g = 9.81 (s^2/g = 275.6 m):
  - Apex H = (s^2/g) ln(sec th). Flat range R = 2 th(rad) s^2/g. Flight time T = 2 asinh(tan th) s/g.
  - 45 deg: H 96 m, R 433 m, T 9.3 s.
  - 60 deg: 191 m, 577 m, 14.0 s. At 2 s it is 84 m up and 61 m out.
  - 70 deg: 296 m, 674 m, 18.4 s.
  - 80 deg: 483 m, 770 m, 25.8 s. At 2 s it is 102 m up and 22 m out.
  - 85 deg: 673 m, 818 m, 33.2 s.
  - 89.9 deg (ArcModel's clamp): about 1750 m, 865 m, 75 s.
  - Because the speed never drops, a steeper shot lands FARTHER.
- What that means in play:
  - The steep play-test arrow 'entered the unloaded chunk' about 6 s after release (playtest6-server.log:44814).
  - The server then unloads it into that far chunk (_decomp\Alta.Chunks\ChunkEntityManager.cs:91-113; DynamicChunkContentManager.cs:53 UnloadSingle). The arrow is lost and saved as junk far away.
- Practical approach:
  - Pick the rain centre by rule. For example, directly under ArcModel.Point(speed, elevation, tVanish), letting the arrow vanish at 1.5-2 s when it is about 80-100 m up and 20-60 m out; or map elevation to a distance.
  - Cast down from the sky there.
  - Require Hits.Loaded on the server, which only keeps chunks near players loaded.

### gotchas
- Sound calls throw on the headless server (AudioManager.Resolve). Keep sounds and effects off the server path, as Holy does with !ApplicationManager.IsHeadless (HolyArrow.cs:456).
- Never destroy the real arrow before the relay:
  - The server's Shoot runs before SyncShoot relays (_decomp\Shooter.cs:61-65). Holy's sacrificed arrow makes spectators' Shoot find nothing, so Volley.Fire never runs there (Release.cs:264-267; HolyArrow.cs:373-376).
  - Hold it the way Seek does (HoldReal Seek.cs:773-796; HeldEmbed and HeldFlight 804-850), or place or destroy it later.
- Pre-empt, on every machine, everything else that fires on the same release:
  - Holy arms on the server and sacrifices the arrow (HolyArrow.cs:175-250).
  - The Resolver order is Beam > Snap/Line > volley > charged > early (Release.cs:184-199).
  - Quickdraw 4 fires from the server's BowString.OnShoot (FromRelease).
  - Seek steers the claim.
  - Auto Dock re-nocks on OnShoot.
  - QuiverGather returns arrows 15 s after the shot.
  - The one-ultimate rule only governs buying (Release.cs:229-242), so a non-orb skill bypasses it.
- No new network traffic: use the existing claim and the game's existing syncs. The claim's time caps at 8190 ms (Wire.cs:41-42), so a long sky hold reads as an inexact time.
- [shared] numbers must match on every machine, but the checksum only reaches a client through bow status messages (Builds.cs:347-354, 293-310).
- Spectators know nothing about an archer before release (no roles, no statuses for archers without bow skills).
- Every new patch, tick and look needs a Timing section with `using var _t = Timing.Begin(...)` as its first statement; asset searches are named and timed separately.
- No scene-wide searches per shot (use BowRegistry); FindObjectsOfTypeAll results must be cached (Fx.cs:104-138; Tablet.cs:929-938).
- Server ticks must go before Main.OnUpdate's headless return (Main.cs:205).
- Keep state per bow string or per arrow. A multicast event stops at the first handler that throws, so wrap every patch in try/catch.
- Settings-derived value holders must not read each other in a circle. Add new ones to SelfCheck.Run. Guard detail lines behind [Log] Level Debug.
- A plain ImpactHit bypasses the PvP gate (PlayerHealth.cs:249-257), so gate players yourself.
- Changing shipped defaults: record them in shipped_defaults.txt and never raise SettingsVersion. New art must come from the game's Glyph_* textures and be shown to the owner before it ships.
- The server only keeps chunks near players loaded: a rain far from everyone hurts nothing.

### cheapest_path
1. Gate.
   - Server: TavernLib.ModdingApi.Roles.GetRoles(name) contains 'admin' (via reflection). Fallback: a server cfg list, like [TestBuild] Player.
   - Shooter: TavernServices.GetService<EntranceMessageHandler>().LocalUser.HasRole('admin').
2. Hold looks, on the shooter only.
   - Trigger in the HeldUpdate postfix pattern (Main.cs:284-288 / Seek.cs:162): admin + past the 68% crossing (Draws clock) + arrow.forward.y >= sin(minElev).
   - Above the predicted centre: a mod quad with an embedded glyph PNG (as Tablet.Glyph does) and Fx.BuildCircle rings on a copy of Orb Connection Line; a HolyWindUp-style hum and haptics.
   - Once armed, force strength 1.0 into the shot message (the HolyArrow.ReleaseStrength pattern).
3. Decide at Volley.Fire / Draws.Release on every machine, and decide Rain BEFORE the Resolver.
   - Server: roles + strength + pose.
   - Shooter: its own roles.
   - Spectators: the relay.
   - The server puts it into the relayed claim in PutResolved: window bits with steered=0, or the mark-count slot for a seed or the centre.
4. The real arrow: hold and hide it at release, or after a visible 1.5 s climb (Seek.HoldReal style). Embed or destroy it after the rain; never before the relay.
5. The pattern.
   - Centre = the ArcModel point at the vanish time (or an elevation-to-distance map).
   - N offsets and delays from LookSeed(arrow id, i), using [shared] numbers.
   - Landing points from a Hits.Cast down from the sky, checked with Hits.Loaded.
6. Server damage.
   - A due-time queue like BounceQueue, ticked before the headless return.
   - Each impact: Targets.Overlap + Holy's Blast recipe (plain ImpactHit, BlastMath falloff, cripple for knock-down, Pvp.HolyBlast or no players at all, an optional small AddExplosionForce).
   - Batch one hit per creature per tick.
7. Client looks per impact.
   - A big Volley.Ghost (Create + Bigger) falling to the point.
   - Fx.FindEffect('Explosion;Small Explosion;Shockwave Impact').Get, staggered and with several different effects to dodge pool limits; Fx.FindSound('Huge Explosion;...').
   - VolleyFx.Sparks, Holy's ring and bolt kit, and at most 2-4 Fx.Flash lights.
8. Sky: a client-only postfix on DayTimeManager.LateUpdate that lerps LightIntensityModifier, skyColorFade and the ambient down for the length of the rain, then back.
9. Housekeeping: an [AdminRain] settings section, timing sections, a SelfCheck entry and [Archery] log lines. Play-test on the local server.

### hard_parts
- Spectators' wind-up before the release: no message says who is an admin, or that an admin is drawing. Roles reach only their owner, and an admin with no orbs sends no statuses. Options:
  - Start the spectators' show at the relayed release, and delay the rain 1.5-3 s so the sky has time to build.
  - A [shared] admin list in every cfg.
  - A build-block bit (but the reserved bits were reserved 'for good').
  - Remember 'this archer rained before' from earlier claims.
- Holding or hiding the real arrow on every machine against the server's position and flight syncs. Seek needed four rounds (H1-H4) of fixes for this: HeldFlight, HeldEmbed, restoring colliders and renderers in a finally, leaving pooled effects alone.
- Keeping the looks the same where colliders may not be loaded: far spectators, and a server that only loads chunks near players.
- VR performance: 20-50 big particle effects + scaled ghosts + lights. Pool limits recycle effects early, Fx.GetScaled is untested at 5-10x, and large alpha smoke costs fill rate.
- Sky darkening fights DayTimeManager every frame and must be undone on every way out; caves turn the sky off anyway.
- Settling Holy, Quickdraw 4, Seek, Volley, Auto Dock and QuiverGather on the same release, identically on the server, the shooter and spectators.
- Mass kills: loot spawning (saved entities), the explosion force flinging gear, and PvP rules for friends standing in the rain.

### surprises
- The rain cannot be the real arrow coming back down.
  - The game's arrows keep full speed in flight. A shot into the sky lands 0.6-0.87 km away after 14-75 s, after peaking 190-1750 m up, and steeper shots land FARTHER.
  - In play-test 6 a steep arrow left the server's loaded chunks about 6 s after release and was unloaded into a far chunk (playtest6-server.log:44814).
  - So build the rain as the mod's own effect at a chosen spot, and hold or retire the real arrow.
- A working admin role system already exists: TavernLib.ModdingApi.Roles on the server, read from users.json, where 3 accounts have 'admin'.
  - Verified in the server log; each client also knows its own roles (verified 'Roles: ["admin"]' in the client log).
  - The game's own admin layer is dead: the Alta web API is gone and IsMenuOrCommandsAvailableForUser returns false.
- The release pose and strength are already bit-identical on every machine: strength is compressed before the shooter's Shoot, and the rotation is compressed and written back. 'Full draw at the sky' needs no new bits.
- The claim has two silent extension slots: the window bits when unsteered, and the mark count, whose marks current readers skip (up to 51 bits).
- An admin who owns no bow orbs sends no status messages at all, so spectators see nothing of the draw until the release.
- The game's cave mechanism (DayTimeManager's LightIntensityModifier + skyColorFade) is a ready-made client-side 'darken the sky' dial. The range's Noon (RushToTime) is a world-wide, forward-only, saved clock change: useless for a short effect.
- Size-limited effect pools recycle their oldest instance (default 5): a large simultaneous barrage of one effect cuts off the earlier explosions.