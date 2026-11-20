# Skyfall fact check: shot-seam-and-server (2026-11-19, read-only)

## Recommendation
Each point gives the cheapest sound option and its cost.

1. Decide in one place.
- Make Skyfall.Decide the first branch of Draws.Release (Q Draws.cs:207). It returns the neutral Resolution and records the arrow, so every later reader sees a plain shot.
- Inputs on each machine:
  - Server: the grant set, held in memory and keyed by Builds.NameOf, together with the strength and pose from the ShootArguments that Claims.Bits records.
  - Shooter: the same rule.
  - Spectators: the relay's Sky field only.
- In Volley.Fire (Q Volley.cs:478-493), add one if right after Draws.Release:
  - Keep the calls that drop the held looks.
  - Server: call DestroyAllExtraArrows and HolyArrow.ForgetServer.
  - Clients: clear SpawnedExtraArrows.
  - Copy the arrow's meshes, run Skyfall.Fire, call Seek.HoldReal, and return without Launch.
- Add three guards:
  - A prefix on LightBeamSkill.DestroyShotArrow.
  - A Skyfall check in Quickdraw4.ClientShot.
  - A Skyfall branch in PutResolved.
- A BowString.OnShoot prefix that switches Holy off before the handler list is invoked would also work. It saves the DestroyShotArrow prefix but needs Holy's input looked up per bow. The cost is the same either way.

2. The claim.
- Put the flag in the window bits with steered = 0: 1 = Skyfall, 2 = festival.
- Send Kind Early, stage 0, no targets, and keep Version 1.
- Clear the flag in ServerCheck.
- Build the pattern from the arrow id, the canonical pose and [shared] settings, so no payload is needed. A mark can carry the server's centre ground height for spectators who have no colliders there.

3. The real arrow.
- Hold it with Seek.HoldReal on every machine, after copying its meshes.
- The server destroys it about 1 s later: after the relay, and before the hold limit of MaxLifeSeconds + 1 (5 s by default).
- Cost: the climb up to the sigil has to be a mod ghost, and the arrow is lost.
- Alternative: free it so it drops at the archer's feet, where Quiver Gather can return it. That needs a small public release method in Seek.
- Letting it fly is cheaper to write but breaks 'players never hurt' and fires the per-arrow hit hooks.

4. The aim.
- Take the pose from ShootArguments (or compress transform.rotation again).
- Put the plane 30 m above the release point. For a near-vertical aim, fall back to the canonical up vector (q * up) for direction.
- Find the ground with a static-ground cast plus Hits.Loaded. Skip the ground looks when that area is not loaded.

5. Damage.
- Use its own BounceQueue instance, ticked before Main.cs:205.
- For each impact: Targets.Overlap, creatures only, BlastMath falloff and a plain ImpactHit.
- Sum the hits per creature per tick. No force. Skip unloaded areas.

6. Festival.
- Move the Heal and Feed cores into a small shared helper rather than calling TestRange.
- For creatures, use a 0-damage, cripple-only ImpactHit.

7. 'OUT!'.
- Each client decides from its own view and shows the TextPopup itself. The server does nothing.

8. Decide now how the shooter learns its grant and festival mode.
- Cheapest way with no new traffic: the server confirms each Skyfall release with a second flight sync on the arrow (Quickdraw 4 precedent): speed 0 plus a magic seed that carries a festival bit, sent before HoldReal.
- The shooter remembers it for the session, so the wind-up looks can show from its next draw on.
- Optionally, the shooter opts in through its claim (decided in Claims.Shot from the pose it compresses itself), and the server requires both the opt-in and the grant.

## Findings
Roots: Q = C:\Users\dmand\OneDrive\Desktop\attmod\QuickshotRing, D = C:\Users\dmand\OneDrive\Desktop\attmod\_decomp, T = the scratchpad's tavernlib folder. The scout's report was read first and not redone; where it is wrong, that is said below.

1. WHERE TO DECIDE, AND WHAT TO SUPPRESS
- The order of one shot (D Shooter.cs:69-131):
  - First the Shooter.Shoot prefixes run: Claims.Shot (Q Claims.cs:80-138), HolyArrow.ReleaseStrength (Q HolyArrow.cs:311-336) and Steady.Decide (Q Steady.cs:125-160).
  - Then the arrow is undocked (D Shooter.cs:80) and Arrow.Shoot runs. On the server, Arrow.Shoot already sends the flight sync (D Arrow.cs:280-288 -> 235-251).
  - Then, on the shooter only, the rotation is compressed and the message is sent with the claim (D Shooter.cs:119-123). It is written during that call through static fields (D InternalMessage.cs:17-36), so a decision made later cannot change it.
  - Then OnShoot fires (D BowString.cs:155-158). Its handlers run in this order:
    - ExtraArrowUpdater.ShootExtraArrows, subscribed first (D ExtraArrowUpdater.cs:61-63), which carries Volley.Fire (Q Volley.cs:464-501).
    - Each active skill's OnArrowShot (D BowDrawSkill.cs:63-91).
    - When Holy Arrow is armed, LightBeamSkill.DestroyShotArrow (D LightBeamSkill.cs:26-31, 66-71).
  - Then the BowString.OnShoot postfixes: Q Quickdraw4.cs:248-271, Q Draws.cs:664-721, Q AutoDock.cs:132-152, Q QuiverGather.cs:124-134.
  - Then the Shoot postfix Q Quickdraw4.cs:378-413.
  - Last, the server relays the shot (D Shooter.cs:61-65).
- Draws.Release (Q Draws.cs:207-287) is the only per-release decision every machine passes through once. It is called only from Volley.cs:478.
  - Its branches: server 217-227, shooter 232-237, spectator 238-250 (the relay at 241).
  - Its record (261-270) is what later code reads: Q Quickdraw4.cs:224, 260, 295; Q Draws.cs:703-715; Q Weight.cs:367; Q Shatter.cs:212.
  - Decide Skyfall as its first branch and return a neutral Resolution(Early, None, empty plan, tier 0). Then:
    - The resolver (Q Release.cs:184-199) never runs.
    - Steerable is false (Q Homing.cs:291-292), so nothing homes.
    - The hold stage is 0 (Q Release.cs:163-164).
    - The real-arrow multiplier is 1 or left alone (Q Release.cs:215-226).
    - Weight.Arm drops the arrow (Q Weight.cs:100-106).
- Server: what to suppress.
  - (a) Skip Launch (Q Volley.cs:493). It holds the ghosts, needles, pellets, heavy arrows and homing; Targets.Open and side-orb practice (557-569); and HoldReal (728-731).
    - Keep ChargeFx.Released and SnapWeight.Released. For a tier-0, non-Line release they only drop the held looks (Q ChargeFx.cs:252-262, 1397-1404; Q SnapWeight.cs:410-421).
    - Keep DropFx (Q Volley.cs:203-207).
  - (b) The Holy beam is a networked extra arrow spawned when Holy arms (D LightBeamSkill.cs:26-31; D ExtraArrowUpdater.cs:164-190). It is shot by ShootExtraArrows' own body after Volley.Fire (D ExtraArrowUpdater.cs:99-118). Call DestroyAllExtraArrows on the server (D ExtraArrowUpdater.cs:254-272).
  - (c) DestroyShotArrow is already in the handler list captured at D BowString.cs:157, so unsubscribing it from inside Volley.Fire cannot stop it.
    - It destroys the arrow before the relay. Spectators' Shoot then finds no arrow (D Shooter.cs:50-61) and never decides Skyfall.
    - So it needs its own prefix that skips it for a Skyfall arrow.
    - ShotReset's Reset(true) (Q Draws.cs:689-693) later deactivates Holy's input, which unsubscribes DestroyShotArrow and destroys the extras (D BowStringPullbackInputMethod.cs:83-87, 112-129; D BowDrawSkill.cs:80-91; D LightBeamSkill.cs:17-25).
  - (d) Call HolyArrow.ForgetServer (Q HolyArrow.cs:84) so UnarmedShot skips Holy's OnArrowShot (Q HolyArrow.cs:242-250).
  - (e) Give PutResolved a Skyfall branch. Its 'locks dropped' line (Q Claims.cs:57-60) would be wrong.
  - (f) Skip Shrine.PractiseOnShot (Q Draws.cs:285).
- Server: harmless, leave as is.
  - Seek.Check's Summary line has already been logged (Q Claims.cs:164-168; Q Seek.cs:457-463).
  - Steady has already sped up the real arrow (Q Steady.cs:188-218).
  - Quickdraw 4 does nothing for a neutral release (Q Quickdraw4.cs:224, 260).
  - ShotReset resets every input (Q Draws.cs:689-693).
  - Auto Dock only queues the next nock (Q AutoDock.cs:132-152). It is not a release effect.
  - Quiver Gather: a destroyed arrow is forgotten (Q Rules.cs:82). A loose one is docked back after 15 s (Q QuiverGather.cs:162-185).
- Shooter.
  - Its claim was already sent with ResolveLocal's kind, Seek.ClaimLocks' locks and the targets (Q Claims.cs:92-104).
  - ClaimLocks also keeps the claimed lock cages for FlightSeconds + 0.5 s (Q Seek.cs:393-419, 417).
  - Skip Launch here too.
  - Clear __instance.SpawnedExtraArrows (D ExtraArrowUpdater.cs:49). Clients receive the beam by sync (120-154) and would shoot it locally (Q HolyArrow.cs:340-371).
  - Never call DestroyAllExtraArrows on a client: it only logs an error (D ClientSpawnManager.cs:21-24).
  - Quickdraw4.ClientShot predicts a line locally, draws it and moves the arrow (Q Quickdraw4.cs:406-409). It is a Shoot postfix outside Draws.Release, so it needs its own Skyfall check.
- Spectators.
  - Decide from the relay only (Q Draws.cs:241).
  - Run the same branch, and clear SpawnedExtraArrows too. Today they never reach ShootExtraArrows on a Holy shot only because the arrow is already gone (Q HolyArrow.cs:373-376).
- The shooter cannot know its grant before the release.
  - It never gets its own relay (D Shooter.cs:64 -> D EntityMessageHandler.cs:36 leaves out the sender).
  - TavernLib's roles are read-only from users.json, and the method that sends them is internal (T TavernLib.ModdingApi\Roles.cs:40-64, 141-154; T TavernLib.Patches\ServerSettingsRolePatch.cs:21-38).
  - The build bits ride only bow statuses, and the block is full (Q Builds.cs:295-310).
  - The only message the server sends the shooter for each shot is the arrow flight sync: a 32-bit seed plus a speed of 0-1000 at 0.01 (D Arrow.cs:14-31, 168-175). Quickdraw 4's marker is the precedent (Q Quickdraw4.cs:22-25, 289; its detector takes any speed of 998 or more as a line, 421).

2. MARKING THE RELEASE IN THE CLAIM
- Layout (Q Wire.cs:33-99): version 4 bits, kind 2, window 2, steered 1, time 13, stage 5, count 3, targets 17 each, picks 3 each (only with targets), mark count 2 plus 17 bits per mark.
  - The base is 32 bits. The writer always writes the window as 0 when unsteered, and a mark count of 0 (57, 68).
  - The reader ignores the window when unsteered (81-83) and reads and throws away the marks (92-93).
  - A fix-up at line 94 runs after the window is read. So derive the Skyfall flag from the raw steered bit, not the fixed-up one.
- What has to change:
  - ShotClaim: new fields (for example Sky 0-3 and Marks) (9-31), copied in Copy (23-26).
  - Write: put the window at line 57 and the marks at 68.
  - TryRead: read them at 81-83 and 92-93.
  - Keep Version 1 (41).
  - ClaimRules.ServerCheck (Q Rules.cs:214-219) must clear Sky and Marks. Every server path goes through it (Rules.cs:224-232; Q Homing.cs:535-550; Q Claims.cs:160-182). Otherwise PutResolved, which copies the checked claim (Q Claims.cs:52), would relay a Skyfall flag forged by a client.
  - PutResolved (49-62) sets Sky, Kind Early and Stage 0, and clears Steered, Locks, Targets and Picks.
  - The H1 test that an unsteered claim is the old layout bit for bit changes for Skyfall claims only.
- Old clients.
  - The claim still parses: the window is ignored and the marks skipped. No leftover-bits warning fires (Q Claims.cs:243-248).
  - They rebuild Early with an empty plan (Q Draws.cs:244-247; Q Release.cs:78-82; Q Rules.cs:204-205), so they show no rain and no ghosts.
  - They do not hold the arrow, so they see it fly from their own relayed Shoot (D Shooter.cs:61, 100).
  - A version bump would be worse: they would drop the claim with a warning (Q Wire.cs:77; Q Claims.cs:250-251) and guess the release from statuses (Q Draws.cs:250).

3. THE REAL ARROW
- Holding it with Seek.HoldReal (Q Seek.cs:773-796):
  - It stops the arrow, makes it kinematic, turns its colliders off, hides it on clients (pooled effects are left alone) and forces a position sync from the server.
  - HeldFlight refuses the server's flight sync, which was sent before OnShoot (Q Seek.cs:848-861).
  - An arrow destroyed while held is freed silently (970-971).
  - The hold times out at Until = MaxLifeSeconds (default 4, Q Homing.cs:29) + 1, with a Warning (Q Seek.cs:779, 1004-1009).
  - Copy the arrow's meshes before the hold (Q Volley.cs:726-728).
- Letting it fly leaves a live, damaging arrow in the world:
  - Its physics belongs to the shooter's client at first (D Shooter.cs:111-114).
  - A player it hits can report the hit and the server accepts it, with PvP on (Q Pvp.cs:40, 49; Q Draws.cs:674).
  - Hit hooks keyed by the arrow id would fire (Q Weight.cs:367; Q Shatter.cs:212).
  - Steady may speed it up (Q Steady.cs:188-218).
- Only the server can destroy it (D ServerSpawnManager.cs:22-26; LightBeamSkill.cs:69 does the same), and never before the relay (D Shooter.cs:50-65).

4. WHERE YOU POINT
- The shooter's rotation is identical to what other machines decode.
  - It is compressed in place after undocking and written back to the arrow (D ShootArguments.cs:23-25; D Shooter.cs:80, 121).
  - The Compressor both reads and writes, and its bit step does nothing (D Compressor.cs:26-28, 41-43; D Stream.cs:149-278), so the shooter gets exactly the floats a reader decodes.
- The strength is on the same 0.001 grid on every machine (D BowString.cs:264; D Stream.cs:542-593).
- Server and spectators: the scout's 'bit-identical' is NOT guaranteed.
  - SyncShoot sets the arrow's pose while it is still docked (D Shooter.cs:57-58).
  - Undock then reparents it (D Shooter.cs:80; D PickupDocker.cs:220), so float rounding can change transform.rotation.
  - Read the pose from the ShootArguments in the existing Claims.Bits postfix instead (Q Claims.cs:196-210), or compress the rotation again.
- Position: the shooter's is raw (D ShootArguments.cs:22); readers get it to 1 mm (D LocationChunk.cs:252-255). Measure '30 m up' from that release point, not from the player's head, which differs per machine.
- The ground cast.
  - Hits.Cast hits every layer except Ignore Raycast (Q Hits.cs:16, 96), so creatures, players and loose items can stop it, and they differ per machine.
  - Use a static mask that skips moving items, like WorldProbe.Ground (Q TestRangeBuild.cs:170, 182-195), plus Hits.Loaded (Q Hits.cs:111-117).
- Far spectators: a player gets no relay unless the bow is spawned for them (D EntityMessageHandler.cs:36). One who has the archer but not the landing area loaded gets a missed cast.

5. THE DAMAGE SCHEDULE
- The queue: BounceQueue<T> is generic, holds at most 512, keeps due order, and treats a broken time as due at once (Q Spectral.cs:555-585). Tick it before the headless return at Q Main.cs:205, like Ricochet's bounces at 183-186.
- Finding victims: use Targets.Overlap (Q Targets.cs:99-106), not Holy's allocating Physics.OverlapSphere (Q HolyArrow.cs:471). Keep IsCreature (Q Targets.cs:111) and drop IsPlayer (139).
- Falloff: BlastMath (Q Rules.cs:64-69), measured from PositionHandler (Q HolyArrow.cs:488).
- The hit: build a plain ImpactHit and call DetectImpact (Q HolyArrow.cs:500-502; D Explosive.cs:179-197).
  - It counts only when Effectiveness > 0 (D ImpactHit.cs:28), and its damage formula is at 47-70.
  - Damage-type multipliers and armour apply (D HealthObject.cs:271-292), and only on the server (294-323).
- Batch one hit per creature per tick, like FlushSpectral (Q Hits.cs:503-549).
- Players: a plain hit skips the PvP check (D PlayerHealth.cs:249-257), and even a zero-damage hit's cripple still lands (258-260 -> D HealthObject.cs:318-321). Leave players out before building any hit.
- Explosion force: AddExplosionForce pushes every loose rigidbody in range, players' gear included (Q HolyArrow.cs:479; D Explosive.cs:171-174). Leave it out.

6. FESTIVAL
- TestRange's Heal and Feed cannot be reused as they are.
  - They are private request handlers tied to the range's status file.
  - Heal only reaches [TestBuild] Player (Q TestRangeHelpers.cs:22-23, 66-99).
  - Reuse their cores instead:
    - Health = MaxHealth. The setter writes HealthStat.Base, the same stat the game's own damage writes (D HealthObject.cs:115-125, 317).
    - The 'Hunger' stat's Base = Maximum (Q TestRangeHelpers.cs:89-91).
    - Keep their checks for living players (73, 88).
- Knocking a creature over with a 0-damage, cripple-only hit is valid (D ImpactHit.cs:28) and lowers only the cripple stat (D HealthObject.cs:316-321). OnHit still fires (290).

7. 'OUT!'
- The game has no 'OUT!' popup. The only 'OUT' in its code is a header at D ArrowFlightSettings.cs:33, and the art notes call the popup a guess (Q Art\admin_skill\picture-check.md:28; idea-check.md:42).
- It would be the game's generic TextPopup with text set by the mod (D Alta.Utilities\TextPopup.cs:86-95). Its pools are D PickupTypeSettings.cs:80-84, 182-184, and the game uses it at D PlayerProgressionManager.cs:221.
- It runs on clients only. It faces the local player's head and switches itself off beyond its maxDistance (D TextPopup.cs:27-36, 60-74).

## Risks
- Shooter and server can disagree when the shooter's knowledge of its grant or of festival mode is stale (a revoke, a restart). Then the shooter holds its arrow and draws a rain nobody else sees, or fires its normal volley while the server rains. Today there is no channel for it.
- If the DestroyShotArrow prefix is missing, the real arrow is destroyed before the relay and spectators see no Skyfall at all.
- If the check in Quickdraw4.ClientShot is missing, the shooter draws a Quickdraw 4 line and moves its arrow locally.
- If the extras are not cleared on clients, the shooter and spectators see a phantom Holy beam trail.
- If ServerCheck does not clear the flag, a modified client can make spectators see a Skyfall.
- Reusing Seek.HoldReal ties Skyfall to [SeekingVolley] MaxLifeSeconds, clamped to 0.5-10 s (Q Seek.cs:108). A destroy later than that logs Seek's warning and drops the arrow.
- Destroying the arrow before a spectator has handled the relay leaves that spectator without the rain. The ordering between the two messages is unknown, so wait about 1 s.
- Using LookSeed for damage spots breaks its 'never used for anything that counts' contract (Q Spectral.cs:117-119). Use a separately named function.
- If a minimum wind-up time is required, the shooter's clock and the server's effective time can disagree near the threshold. Decide with the claimed ms when it is trusted.
- When several rains overlap, BounceQueue's cap of 512 drops impacts silently (Add returns false). Log it once.
- Mass kills spawn drops that are saved with the world.
- Admins who own no bow orbs never get the [shared] settings check, because it rides only bow statuses (Q Builds.cs:347).
- TextPopup pools can recycle popups when many players are hit, and bystanders beyond its maxDistance see no 'OUT!'.

## Not verified
- Whether transform.rotation really drifts on the server and spectators. This is inferred from the pose being set while the arrow is docked and then reparented; it was not measured.
- That float results are identical across different CPUs.
- The message ordering between the relay and the destroy (which channels they use).
- HarmonyX behaviour when one prefix returns false: whether later prefixes still run. This matters when stacking a marker prefix with Quickdraw 4's detector and Seek.HeldFlight on Arrow.StartHandlingArrowDeviance.
- What old clients show for a held arrow, or for a speed-0 marker flight sync.
- Knock-down wiring for each creature type, the loot spawned on a kill, and whether HealthStat.Base -= 0 triggers anything.
- The TextPopup prefab's font, maxDistance, pool size and auto-release.
- Whether Heal and Feed were ever confirmed in play.
- Whether the low-detail 'landmark' copies have colliders for far spectators.
- No build, test or game run was done; this was read-only.
- Logs, users.json and %APPDATA% were not opened.
