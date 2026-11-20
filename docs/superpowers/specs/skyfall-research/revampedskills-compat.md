# RevampedSkills 0.4.7 vs QuickshotRing and Skyfall (read-only check, 2026-11-19)

## Verdict
The two mods do not crash each other, but the fit is not clean, and every real fight is over the real arrow.

1. **Holy Arrow keeps its arrow.** RevampedSkills 0.4.7 cancels the sacrifice that QuickshotRing's [HolyArrow] KeepsArrow = false asks for. Your logs show it at 19:05: a thunk at the archer's feet on each Holy shot, then the arrow back in the quiver 10 s after the last shot.
2. **Watchers' relayed Holy shot now finds the arrow.** QuickshotRing's beam code assumes it never does. What a watcher actually sees is unverified.
3. **The one real Skyfall break.** By the loader's own code, RevampedSkills is patched twice. That leaves its 'do not destroy this Holy arrow' latch stuck on the last arrow it kept. If Holy Arrow is armed when Skyfall is cast, the latch swallows Skyfall's 1 s destroy and the used-up arrow comes back. The same latch blocks any QuickshotRing destroy of that arrow.
4. **Arrows follow you everywhere.** RevampedSkills drags along every arrow that no carried quiver will take, and it is happening right now: 9-21 arrows re-parked at your feet, with a clatter, at every chunk change since 19:20. The range's range-only quivers and bottomless shelves make this the normal case.

Everything else checks out or is cosmetic: the shrine pages and slot cap agree (your client log reads 'slots 8 of 16'). RevampedSkills never touches the skill tree, the draw, the claim bits or Skyfall's message. The Unity warning 'Kinematic body only supports Speculative Continuous collision detection' was already there before RevampedSkills (343 times in the 18 Nov 20:42 log), so it is not this.

Do not play-test Skyfall with a Holy Arrow owner until check 1 passes and the latch-clearing finalizer is in. Then decide which mod owns the Holy sacrifice; right now RevampedSkills overrides your setting silently.

## Conflicts
- **breaks**: Skyfall's 1 s server destroy of its held arrow is silently skipped if Holy Arrow was armed at the cast, or if the cast arrow is the last one a Holy shot kept. This depends on RevampedSkills being patched twice. The loader's code says it is, but that was not seen at runtime.
  - Evidence: RevampedSkills has no [HarmonyDontPatchAll]: rs_server Properties/AssemblyInfo.cs:7-8, and the DLL's metadata has no such name. So MelonLoader 0.7.3 patches every [HarmonyPatch] class in it (scratchpad melonloader MelonBase.cs:548-570, MelonAssembly.cs:297/313, MelonMod.cs:68-75). Then Core.cs:16-33 patches them all again. HarmonyX keeps both copies (harmonyx PatchInfo.cs:122-130), runs every prefix even after one returns false (HarmonyManipulator.cs:161-203), and gives both copies one __state, keyed by declaring type (HarmonyManipulator.cs:323-329, 806-809). Result: PreserveHolyArrow.cs:48-51 plus the finalizer at :74-77 leave the [ThreadStatic] 'active' (:45-46) pointing at the arrow after every DestroyShotArrow. SkipAmmoDeletion (:26-43) then refuses ServerSpawnManager.Destroy(thatArrow, isUnload:false) until the next DestroyShotArrow. For a cast, QSR's SkyfallKeepsArrow (HolyArrow.cs:259-270) returns false, but RevampedSkills' prefixes still run. Skyfall.cs:566 then calls SceneDestroy -> Spawner.Destroy(this) with isUnload false (_decomp NetworkEntity.cs:597-603, SpawnManager.cs:159), which returns normally. Held.Tick has already removed the hold (:563) without Free, so on the server the arrow stays kinematic with its colliders off (HeldArrow.cs:29-30, 47-54). Clients reveal it hanging at the release point 5 s later (Skyfall.cs:575-578, SkyfallRules.cs:454). RevampedSkills tracked it at the shot (RecordBowShot.cs:17-19) and docks it into the caster's quiver 10 s after their last shot (ArrowReturn.cs:303-345). Into a full quiver it is merged, so the cast cost nothing. Into an empty quiver it stays an arrow whose server colliders are still off. Skyfall.cs:590 (Shutdown) is swallowed the same way, so a server quit can save it into the world. Holy arms at 5.5 s on this server (server log 'Tree: timers ... holy=5.5'), so any Holy owner who holds a sky draw past 5.5 s hits this.
  - Fix: In QSR, add a [HarmonyPriority(Priority.Last)] finalizer on LightBeamSkill.DestroyShotArrow. It runs after RevampedSkills' Normal-priority finalizers and sets RevampedSkills.Archery.PreserveHolyArrow.active to null through AccessTools.Field (main thread). In Skyfall.Held.Tick, also check a.Entity.IsBeingDestroyed after SceneDestroy: if it is false, warn, Free the arrow and retry. Ask the RevampedSkills author to add [assembly: HarmonyDontPatchAll].
- **risky**: The same stuck latch makes QSR's other destroys of the last Holy-kept arrow do nothing. The worst case reopens the try-out arrow leak (fixA-review I-2).
  - Evidence: Every QSR destroy goes through SceneDestroy with isUnload false, so each is skipped while the latch points at that arrow. AutoDock.cs:520 (try-out End): the arrow survives, is forgotten at :527, KeepTryOutArrows (AutoDock.cs:565-584) stops refusing it, and RevampedSkills' next return or a hand can stack it into the player's saved quiver. TestRangeShelf.cs:121, used by Collect (TestRangeHelpers.cs:54-64) and the MaxLooseArrows eviction: the status counts the arrow as removed, but it stays. TestRangeClean.cs:100. Arrows merged into a non-empty quiver are destroyed with isUnload:true (_decomp PickupDock.cs:936-952), so ordinary returns are not affected. That is why 9643, 9846 and 10130 did merge this session.
  - Fix: The same finalizer as the first item.
- **risky**: Holy Arrow no longer uses up its arrow. This overrides QSR's [HolyArrow] KeepsArrow = false ('Off: the game's sacrifice').
  - Evidence: Server QuickshotRing.cfg:202 has KeepsArrow = false (HolyArrow.cs:58). RevampedSkills skips the Spawner.Destroy at LightBeamSkill.cs:69 (PreserveHolyArrow.cs:26-43). Its finalizer parks the arrow embedded 0.35 m above the archer's feet (ArrowReturn.cs:140-146, 196-209) and docks it 10 s after the archer's last shot (ArrowReturn.cs:303-345). Seen this session: the client log has 'Arrow Impact Sounds' for 9846, 9643 and 10130 at 19:05:31.650, 19:05:40.607 and 19:05:53.689, 30-55 ms after each Holy release. That is the server's park reaching the shooter, a thunk at their feet. 'Unembedding ... due to destruction' at 19:05:50.595-.597 and 19:06:03.684 is the merge into the quiver, exactly 10.0 s after the last shot. The kept arrow carries the release's one-off multiplier while it lies at the feet ('Release: arrow 9846 game set x2.5 on the real arrow'; Draws.cs:708 leaves a beam's real arrow as the game set it). RevampedSkills 0.4.7 has no switch for this on its own: 0.4.4's PreserveHolyArrow key is gone, and Archery.Enabled=false turns off all recovery.
  - Fix: Decide which mod owns this rule. To keep the arrow: reword KeepsArrow so it says RevampedSkills keeps it. To keep the sacrifice: QSR destroys the kept arrow itself on the next frame, after the relay. That needs the first item's fix first, or the latch swallows the destroy.
- **risky**: Watchers' relayed Holy shot now finds the arrow, which QSR's beam code assumes never happens. Not seen: there was no second player this session.
  - Evidence: In vanilla, the server destroys the arrow inside OnShoot before Shooter.SyncShoot relays it (_decomp Shooter.cs:61-65 and :124; LightBeamSkill.cs:66-71). With RevampedSkills the arrow is alive and parked when the relay goes out. So every watching client runs Shoot on it (Shooter.cs:50-61): Undock, then Arrow.Shoot on an arrow the server just embedded (Arrow.cs:268-279), then LerpAuthorityFromClient (Shooter.cs:111-114). OnShoot then reaches ExtraArrowUpdater.ShootExtraArrows (ExtraArrowUpdater.cs:63, 99-117), so Volley.Fire (Volley.cs:472-500) and BeamShot (HolyArrow.cs:399-433) run on watchers. HolyArrow.cs:432-435 says that never happens there. BeamFiredSync (HolyArrow.cs:442) then takes its 'boosted' early return, so watchers get the shooter's full-bright following bolt instead of the dimmer 'seen' bolt (OtherArcherBrightness, follow:false). What the real arrow does on their screen is not verified: it may hang at the release pose or fly, and either way it ends at the archer's feet. If it flies into a player, Pvp.cs:148-185 would accept that player's report of a hit on their own body: the arrow is not held or docked, and was stamped by Pvp.Shot at Draws.cs:681 within 10 s. That would mean extra damage on top of the beam, with the 2.5x one-off multiplier.
  - Fix: On a client that is not the shooter, hold and hide the real arrow of a relayed Holy release (HeldArrow.Take, as Skyfall.Held does). Keep BeamShot's own-shot path off for relayed shots, so the 'seen' path draws the bolt.
- **risky**: Arrows that no carried quiver will take are dragged after the archer for good. This is happening in the current session.
  - Evidence: ArrowReturn.cs:320-350: when no carried quiver passes CanDockAmount and CheckAllowed, every pending arrow is parked at the owner's feet and stays pending. When its chunk unloads it is parked at the owner's feet again (RescueArrowsBeforeChunkUnload.cs:10-23 -> ArrowRecoveryScheduler.cs:24-37 -> ArrowReturn.cs:225-231). The server log has 306 '[Archery] Rescued an arrow leaving the map' lines since 19:20:36, in bursts of 9, 12 and later 21, one burst at each PlayerChunkManager 'moved from' line. The client log has matching bursts of 9-21 'Arrow Impact Sounds' (19:20:36 to 19:26:47): a clatter at your feet roughly every 64 m. The test range makes this the normal case. A range quiver's inner dock refuses anything that is not range stock (TestRangeShelf.cs:96-104, :201), so your own arrows cannot go back into a worn range quiver. The shelves (90 per slot, refilled every 2 s) easily outrun quiver room. TestRange.Remove deliberately leaves loose arrows (TestRange.cs:547-548). The 19:20:36.564 'range removed ... 0 object(s)' came about 0.1 s after RevampedSkills had pulled the arrows out of the unloading area. MaxLooseArrows (200) and Collect still limit and clear range stock. Nothing limits your own arrows.
  - Fix: Give range stock RevampedSkills' own exclusion marker: add a 'RevampedSkills.Archery.SkillArrowCopy' component by type name. RevampedSkills checks for it at ArrowReturn.cs:99 and ArrowRecoveryBeacon.cs:19. Alternatively, Collect loose stock inside RemoveAll. Tell testers about the Collect button.
- **risky**: Quiver Gather stands down whenever RevampedSkills is loaded, even when RevampedSkills' recovery is switched off. The Arrow Gatherer orb's tablet line still promises the return.
  - Evidence: QuiverGather.cs:55-61 checks only the mod's name, and stands down at :77-80 and :200. RevampedSkills returns nothing when Archery.Enabled is false (ArrowReturn.cs:62-65), or when any value in its json is invalid (ArrowRecoveryConfig.cs:84-90). So the obvious remedy for the two items above (recovery off) leaves no arrow return at all. Preview.cs:572 ('arrows you shoot from your quiver come back to it') is now true for everyone, on RevampedSkills' timing (10 s after the archer's last shot, not [QuiverGather] ReturnSeconds 15). QSR's client return ghost (QuiverGather.cs:229-251) can also play on RevampedSkills' returns, on top of RevampedSkills' own sound and haptic.
  - Fix: Have RevampedSkillsLoaded also read Archery.Enabled from the server's UserData\RevampedSkills.json. Change the Arrow Gatherer orb's tablet line (Preview.cs:572) while RevampedSkills returns arrows.
- **harmless**: Shrine pages, the slot cap and the sales against ShrineView, Tablet, Shrine.cs and TestTools.
  - Evidence: RevampedSkills makes the ring 64 holders (PreparePagedHolders.cs:9-16) and puts 3 uints before the ring data (SyncShrinePages.cs:13-26). The layout is symmetric because both sides load the same DLL the same way. QSR counts filled holders (ShrineView.cs:277-288) and reads MaxBoughtSkills live (Tablet.cs:1171). After a shrine sync RevampedSkills answers with the server's limit (ConfiguredSkillLimit.cs:14-17). Seen working: client 19:01:16.335 'Tablet: held orb AutoDock ... slots 8 of 16'. Before the first sync the client sees cap 8 (client 19:00:44.815 'slot cap 8', Tree.cs:392), which only affects a log line. The tablet has its own root (Tablet.cs:500), so ShrinePages.ApplyVisibility (ShrinePages.cs:252-274) never hides it. Both caps read the same patched value (Shrine.cs:200, TestTools.cs:240, TestRangeReset.cs:177/255). QSR's sale recount (Shrine.cs:195-202) fixes the count that EnforceSkillCapacity.cs:8-12 reads. RevampedSkills refuses an unlock silently, so TestTools.cs:284's 'see the lines above' points at nothing. Not checked: whether RevampedSkills' page panel (ShrineAttachmentView.cs:116-121, under 'Ruins Visual Base') overlaps the tablet or the try-out text. If a client ever runs QSR without RevampedSkills (the client copy is managed by the SheikahMods pack: manifest.json lists RevampedSkills.dll but not QuickshotRing.dll), that client misreads every shrine sync and ShrineView reads garbage.
  - Fix: Optional: have TestTools name RevampedSkills' cap in its 'refused' line. Before each play-test, check that RevampedSkills.dll has the same md5 on the server and every client.
- **harmless**: The skill tree, the claim bits and custom messages.
  - Evidence: A grep of the 0.4.7 decompile finds no reference to ProfessionSkillTree, ProgressionSlot, skill levels, practice, BowDrawSkill, BowStringPullbackInputMethod, Shooter or ShootArguments. So Tree.cs's slot ids and injections, the refunds, the timers and Skyfall's claim field (Claims.cs:220) are untouched. RevampedSkills' only new message is 247 on player entities (ArrowReturnFeedback.cs:15, :42-49). QSR sends none. Skyfall's signal uses the game's player text message (SkyfallLink.cs:143, :188), which RevampedSkills does not touch.
  - Fix: 
- **harmless**: Seek's and Skyfall's held real arrows otherwise survive RevampedSkills' world safety.
  - Evidence: Both holds are kinematic and still. KeepContinuousCollision skips kinematic bodies (ArrowReturn.cs:150), and a still arrow never trips the probe. Holds end within 1 s (Skyfall) or MaxLifeSeconds+1 = 5 s (Seek), before the 10 s return. The one exception is Seek.PlaceReal's 'in the air' placement (Seek.cs:880-906), which is not embedded. RevampedSkills sweeps the straight line from the hold point (ArrowReturn.cs:242-249, ArrowTerrainProbe.cs:21-48) and can embed the arrow in the first hill or wall that the curved homing path flew over. That is cosmetic, because recovery returns the arrow anyway.
  - Fix: 
- **harmless**: RevampedSkills' recovery marker shows over arrows QSR keeps hidden.
  - Evidence: On the shooter's client, ArrowRecoveryBeacon.cs:17-79 adds a cyan diamond child 0.5 s after the local archer's arrow stops. HeldArrow.Take (HeldArrow.cs:33-35) hid only the renderers present at release. So the diamond floats where a Seek or Skyfall arrow is meant to be invisible: up to about 5 s for Seek, about 0.5 s for Skyfall.
  - Fix: Disable the ArrowRecoveryBeacon behaviour (by type name) in HeldArrow.Take and turn it back on in Free.
- **harmless**: The other shared patch targets.
  - Evidence: On BowString.OnShoot RevampedSkills only tracks the arrow in a prefix, while QSR has postfixes (AutoDock.cs:144, Draws.cs:671, Quickdraw4.cs:248, QuiverGather.cs:124). On ExtraArrowUpdater.CreateExtraArrow RevampedSkills only adds a marker (ExcludeExtraArrows.cs:8-14), beside HolyArrow.cs:313. On Projectile.Shoot it acts only on Air Slash (AirSlashHoming.cs:28-43). On Pickup.OnUndocked and Interactable.FinishInteraction it only watches loose arrows. On HealthObject.DetectImpact it records only inside a Power Shockwave cast (PowerShockwaveBounce.cs:50-67), so Skyfall's kills and blasts (SkyfallServer.cs:192-223) and Holy's blast are untouched. Try-out arrows that QSR refuses (AutoDock.cs:565-584) are parked at the player's feet instead of docked, which is fine. The dock-sound mute can land on Auto Dock's put-back instead of RevampedSkills' own return (QuietAutomaticArrowDocking.cs:9-22).
  - Fix: 
- **harmless**: RevampedSkills 0.4.4 in the server's Mods_disabled folder, and log tags.
  - Evidence: 0.4.4 is not loaded. If it were re-enabled, its beam damage multiplier would stack on top of QSR's BeamCreated (HolyArrow.cs:313-339). Its CheckForPullback rewrite would run ahead of DrawGrace, which Draws.cs:572 still describes as 'after RevampedSkills' pull rewrite'. RevampedSkills also writes '[Archery] ...' lines under [Revamped_Skills], so a grep for '[Archery]' mixes the two mods. The test window's last-shot panel is safe because it keys on 'arrow <id>'.
  - Fix: Delete the 0.4.4 copy, or never re-enable it. To read QSR's lines, grep '[QuickshotRing] [Archery]'.

## Play-test checks
1. **Is RevampedSkills patched twice?** This decides the Skyfall break and the stuck-latch item. Ask someone who has Power Cross Slash to swing it once on the server. If the server console shows '[AirSlashHoming] Spawn callback fired on server' twice for that one swing, the stuck latch is real.
2. **Skyfall with Holy Arrow armed.** As a Skyfall holder who owns Holy Arrow (Charge main), aim at the sky and hold the full draw until the Holy Arrow glow arms (about 6 s), then release.
   - Pass: the storm plays, and the arrow is gone for good.
   - Fail: about 5 s after release a plain arrow appears in the air where you released and drops; about 10 s after your last shot your quiver gains an arrow.
   - Control: the same cast released at 1-2 s, before the Holy glow, must pass.
3. **Holy sacrifice.** Fire a Holy Arrow with arrows in your quiver. Today you hear a thunk at your feet, the arrow sits stuck by your feet, and 10 s after your last shot it jumps into your quiver. Decide whether that is wanted.
4. **What a watcher sees.** Have a second player watch you fire a Holy Arrow from 10-20 m to the side.
   - Fail signs: a plain arrow hangs at, or flies from, your bow; or your gold bolt looks full-bright instead of the dimmer 'other archer' bolt.
   - With PvP on, have them stand in the shot's line: they should take only the beam and blast, never a separate arrow hit.
5. **Arrows following you.** At the range, wear only a range quiver and shoot 20 or more of your own arrows, or take more shelf arrows than your quiver holds. Stop shooting for 10 s: everything loose jumps to your feet. Walk 150 m away: about every 64 m you hear a clatter and the pile is back beside you. Press Collect: the range's arrows go, your own stay.
6. **Arrow return trap.** Only if you plan to turn RevampedSkills' recovery off: set "Archery": {"Enabled": false} in the server's RevampedSkills.json and restart. Then, owning the Arrow Gatherer orb, shoot from your quiver: the arrows should come back after 15 s, and they will not.
7. **Seek.** Lock a creature behind a low ridge or wall so the volley curves over it, then release.
   - The real arrow should end where the core cluster ended. Fail: it is stuck in the ridge face, on the straight line from your bow.
   - While the volley flies, look at your bow: a small cyan diamond floating there is RevampedSkills' marker over the hidden arrow.
8. **Shrine.** With 9 or more skills owned, open the Ranged tree.
   - RevampedSkills' page buttons should appear, and the tablet should rise in its cradle, clear of the page panel.
   - Touch Next: the page turns, and the tablet does not re-rise or go blank.
   - Hold an orb that the cap blocks: the tablet's reason line should show the same numbers as RevampedSkills' 'owned / limit' counter.

## The map
### Same file
IDENTICAL. Server Mods\RevampedSkills.dll and client Mods\RevampedSkills.dll are both md5 1d221862ee176c29400e14f58551a59c (sha256 e76c9941746a048b028086800cb2ac8c40db215ae9e97ccab6cc7b835752975c), 133,120 bytes, file time 2026-11-18 23:18:34. That is version 0.4.7.

Version strings:
- Attributes: [MelonInfo(typeof(RevampedSkills.Core), "Revamped Skills", "0.4.7", "ATT", null)] and [MelonGame("Alta", "A Township Tale")] (Properties/AssemblyInfo.cs:7-8).
- AssemblyVersion is 0.0.0.0. It is a Debug build (DebuggableAttribute 263).
- There are no other Melon attributes: no MelonPriority, no VerifyLoaderVersion, no dependency attributes and no HarmonyDontPatchAll.
- It references MelonLoader 0.7.3.0, 0Harmony 2.10.2.0 (HarmonyX), Root.Township 0.0.39.2 and Newtonsoft.Json 13.0.0.0, plus the Unity Core/Physics/TextRendering/AssetBundle modules, Alta.Serialization.Runtime, Alta.Singletons, Alta.Global.Runtime, Alta.Values and System.Numerics.
- Both logs say "MelonLoader v0.7.3 Open-Beta", list the mod as "Revamped Skills v0.4.7", and print it under the logger name [Revamped_Skills].
- Start-up line (Core.cs:34): "Revamped Skills 0.4.7 initialized with 0 patch failure(s): combat improvements, shrine pages, server arrow recovery including Holy Arrow ammo, terrain protection, and return feedback. Matching server/client versions required."

When each side loaded it:
- The server started 18:51:02 and logged the start-up line at 18:51:16, "[SkillLimit] Server limit: 16" at 18:52:03, and "[Archery] Recovery: True, delay: 10s" at 18:59:47.
- The client started 19:00:14 and logged the start-up line at 19:00:18.

The client Mods folder changed mid-task:
- When I started (around 18:59) the client Mods folder had no RevampedSkills.dll. The client's SheikahMods manager had the modpack parked: SheikahMods\switching.json reads {"parked": true}, and the copy sat in SheikahMods\parked\.
- The client Mods folder changed at 19:00:01. The copy now there is the same md5, and the running client loaded it.

Other copies:
- Client SheikahMods\parked\ and the three SheikahMods\transactions\*\ folders all hold the same md5. The modpack's SheikahMods\manifest.json lists the same sha256 (source att.aservertown.fans).
- Server Mods_disabled\RevampedSkills.dll is a DIFFERENT, older build: 0.4.4, md5 884c66bef7f5cb16dc388de039395055, 136,192 bytes, dated 2026-11-11. 0.4.4 is the version in the friend's logs. Its extra patches are listed under patches.
- The embedded resources (RevampedSkills.ShrineBundle and RevampedSkills.ShrinePlacement) are byte-identical in 0.4.4 and 0.4.7.
### Patches
File:line refers to the 0.4.7 decompile (rs_server) unless marked 0.4.4. There are 27 patch classes and 31 patch methods, and no transpilers. Core.OnInitializeMelon applies them itself: it loops over every [HarmonyPatch] type and patches it under the Harmony id "ATT.RevampedSkills" (Core.cs:13-35). See "How they are applied" at the end: by the loader's code, each patch is very likely applied TWICE.

ARCHERY / ARROWS / BOW

1. BowString.OnShoot (private; the handler for Shooter.OnShoot, game BowString.cs:155-158). PREFIX. RecordBowShot.cs:6-34.
   - Server: calls ArrowReturn.TrackBowShot for every bow shot. This tracks the arrow and resets that archer's return clock (ArrowReturn.cs:60-78).
   - Client: if the archer is the local player, starts ArrowRecoveryBeacon.Track on the arrow.
   - Never skips the original.

2. LightBeamSkill.DestroyShotArrow (Holy Arrow's sacrifice, game LightBeamSkill.cs:66-71). PREFIX (PreserveHolyArrow.cs:48-72) and FINALIZER (:74-90); the class attribute is at :8.
   - Server prefix: calls TrackBowShot on the consumed arrow and stores a [ThreadStatic] Consumption (:45-46).
   - The original still runs: it sets IsIgnoringCollisionOnShoot, calls Spawner.Destroy, and unsubscribes.
   - Finalizer: if the destroy was blocked (patch 3), it calls ParkHolyAmmo (ArrowReturn.cs:140-146). That parks the arrow embedded 0.35 m above the archer's feet, or else just stops its flight.

3. ServerSpawnManager.Destroy(NetworkEntity, bool). PREFIX returning bool. Nested class SkipAmmoDeletion, PreserveHolyArrow.cs:26-43.
   - Returns false (skips the destroy) only while a Consumption is active, isUnload is false, and the entity is that arrow. It marks the Consumption as Preserved.
   - Net effect: Holy Arrow no longer destroys the arrow.

4. ExtraArrowUpdater.CreateExtraArrow. POSTFIX. ExcludeExtraArrows.cs:5-14.
   - Adds an empty marker component, SkillArrowCopy (SkillArrowCopy.cs:5-7), to every extra arrow and every Holy beam the game creates.
   - Recovery and the beacon skip anything with that marker (ArrowReturn.cs:99, ArrowRecoveryBeacon.cs:19).

5. Arrow.<StopUpdatingArrowFlight>b__53_0. This is the delayed lambda that sets collisionDetectionMode to Discrete after a flight stops (game Arrow.cs:201-207). POSTFIX. KeepRecoverableArrowCollision.cs:6-27.
   - Server, tracked arrow with world safety active: sets ContinuousDynamic again (ArrowReturn.cs:148-154).
   - Does nothing if the arrow is kinematic or embedded.

6. Interactable.FinishInteraction(Interactor). POSTFIX. WatchReleasedArrow.cs:5-14.
   - Runs ArrowReturn.WatchLoose (ArrowReturn.cs:80-94) on any released Pickup.
   - Applies only on the server, with recovery enabled, for an Arrow that is neither docked nor held and whose owner is alive.
   - Tracks it with autoReturn=false: world safety only, no return to the quiver unless a return was already pending.

7. Pickup.OnUndocked(Interactor, Pickup, PickupDock). POSTFIX. WatchUndockedArrow.cs:5-11.
   - Same WatchLoose. The owner is the undocking player, or LastPlayerInteractor.
   - Also fires on every shot, because Shooter.Shoot undocks the arrow first (game Shooter.cs:80).

8. PickupDock.ItemDockedSound(Interactor, Pickup, PickupDock). PREFIX returning bool. QuietAutomaticArrowDocking.cs:6-22.
   - Client only, automatic docks only (interactor is null).
   - Skips the game's dock sound for a quiver dock that the server announced in a Phase 1 message (ArrowReturnFeedback.cs:156-159; ArrowReturnFeedbackPolicy.cs:12-47: 5 s window, counted per dock).

9. PlayerController.Initialize. POSTFIX. InitializeArrowReturnFeedback.cs:5-11.
   - ArrowReturnFeedback.Attach (ArrowReturnFeedback.cs:29-57) adds the component and registers custom entity message 247 on every player entity, on both server and client.
   - If 247 already has a handler, it logs a warning and gives that player no feedback (:42-46).

10. Chunker.EvaluateChunk(bool). PREFIX returning bool. RescueArrowBeforeChunkExit.cs:6-20.
    - Server: a tracked arrow whose frame position is outside every chunk is parked at its owner's feet, and the game's evaluation is skipped.
    - The game would otherwise post it to the player or destroy it (game Chunker.cs:226-270).

11. NetworkRigidbody.DelayDestroy. PREFIX returning bool. RescueArrowBeforeInvalidChunkRemoval.cs:7-21.
    - The same rescue, run before the game's chunk re-evaluation and SceneDestroy (game NetworkRigidbody.cs:610-620).

12. DynamicChunkContentManager.UnloadSingle(NetworkEntity) (an entity entering an unloaded chunk). PREFIX returning bool. RescueArrowEnteringUnloadedChunk.cs:8-31.
    - A tracked arrow is rescued to its owner's feet, and the unload or destroy is skipped (:24).
    - The unload goes ahead if the rescue failed, or if the arrow is still in that chunk.

13. DynamicChunkContentManager.UnloadInternal (a chunk unloading). PREFIX. RescueArrowsBeforeChunkUnload.cs:7-24.
    - Server: every tracked arrow in the unloading chunk is parked at its owner's feet, if the owner is in another chunk (ArrowRecoveryScheduler.cs:24-37; ArrowReturn.cs:225-231).

0.4.7 does NOT patch Shooter, BowStringPullbackInputMethod, BowDrawSkill or any bow skill.

COMBAT (melee skills, but these two targets are also used by arrows and the Holy beam)

14. PowerCrossSlash.<>c__DisplayClass9_0.<ExecuteWithHit>b__0, the spawn callback of Power Cross Slash's projectile. Found by TargetMethod (AirSlashHoming.cs:13-16; type at :45-49). POSTFIX. AirSlashHoming.cs:10-26.
    - Logs "[AirSlashHoming] Spawn callback fired on server|client ..." on every cast (:23).
    - Attaches AirSlashHomingController.

15. Projectile.Shoot(Vector3, ColliderGroup). POSTFIX. AirSlashHoming.cs:28-43.
    - Runs for EVERY Projectile, the Holy Arrow beam included. Arrows are not Projectiles (game Arrow.cs:12).
    - Acts only when an AirSlashHomingController is present (calls BeginFlight).

16. AreaAttackSkill.DealDamage. PREFIX (ref __state), POSTFIX and FINALIZER. PowerShockwaveBounce.cs:17-48.
    - Server, skill hash 21572 (Power Shockwave, SkillIds.cs:5) only.
    - Opens a [ThreadStatic] BounceContext and logs; the postfix then calls TryBounce.

17. HealthObject.DetectImpact(ImpactHit). PREFIX. PowerShockwaveBounce.cs:50-67.
    - Every impact on every HealthObject passes through this prefix. That includes QuickshotRing's hand-made hits, Weight's cripple and the Holy blast.
    - It only records anything while a Power Shockwave context is open.

SKILLS / SHRINE / PROGRESSION

18. ProgressionSettings.get_MaxBoughtSkills. POSTFIX. ConfiguredSkillLimit.cs:5-18.
    - Server: returns MaxOwnedSkills from the config (16 here).
    - Client: returns the server's limit (RemoteLimit) once a shrine sync has delivered it; until then, the vanilla value.
    - Vanilla is 8 on this install: QuickshotRing logged "slot cap 8" on the client (19:00:44) and "slot cap 16" on the server.

19. PlayerProgressionManager.UnlockSlot(slot, isConsumingPoints, isFromLoad). PREFIX returning bool. EnforceSkillCapacity.cs:5-12; rule in SkillLimitPolicy.cs:21-24.
    - Server, not from load: SILENTLY skips the unlock (nothing logged) when currentlyOwnedSkillsCount is at or above the limit.
    - Loads always pass. Clients always pass.

20. PlayerProgressionManager.IsSlotAvailable(slot). PREFIX. RefreshSkillCapacity.cs:6-12.
    - Before every check, sets canBuyMoreSkills = owned < MaxBoughtSkills (the patched value).
    - This drops the vanilla "0 = no cap" case (game PlayerProgressionManager.cs:339).

21. BoughtSkillVisualizer.Awake. PREFIX and POSTFIX. PreparePagedHolders.cs:6-21.
    - Prefix: sets crystalCount to 64, and re-runs PlaceOrbHolders if an existing holder array is not 64 long.
    - Postfix: ShrinePages.Initialize (ShrinePages.cs:65-102). It lays the 64 holders onto the vanilla ring's 8 positions (index i % 8) and builds the paging panel.

22. BoughtSkillVisualizer.PrepareOnServer(PlayerProgressionManager). POSTFIX. PreparePageOwner.cs:5-11.
    - ShrinePages.SetOwner (ShrinePages.cs:124-147): a new owner starts on page 0.
    - Extent = max(limit, index of the last filled holder + 1).

23. BoughtSkillVisualizer.Serialize(Stream, PlayerProgressionManager). PREFIX. SyncShrinePages.cs:7-28. THIS IS THE WIRE CHANGE.
    - Writes or reads 3 uints in front of the vanilla payload: limit (server writes ServerLimit, a client writes 8), page, extent (:13-18).
    - Client, reading: throws InvalidOperationException "Invalid RevampedSkills shrine page data. Server and client must use the same version." if limit is not 1-64, extent is below limit or above 64, or page is out of range (:21-24).
    - Otherwise it sets RemoteLimit and the current page (:25-26).

24. BoughtSkillVisualizer.Disable. PREFIX. ResetShrinePages.cs:5-11.
    - ResetPage: back to page 0, owner cleared, hidden holders restored.

25. BoughtSkillVisualizer.OrbHolderHandle.AssignOrb(BoughtSkillOrbReplacer). Private nested method, found by TargetMethod (RefreshAssignedOrbPage.cs:9-12). POSTFIX. RefreshAssignedOrbPage.cs:6-21.
    - Marks the pages dirty so visibility is re-applied.

26. BoughtSkillOrbReplacer.FilterInteraction(Interactor, Interactable, ref bool result). POSTFIX. PreventHiddenOrbInteraction.cs:5-15.
    - Sets result = false for a bought-skill crystal that is not on the visible page.
    - Also false while the shrine is closed or the pages are not set up yet (ShrinePages.cs:45-63).

27. ConnectToServerUtility.SetupConnectionToServer. PREFIX. ResetServerSkillLimit.cs:6-12.
    - Clears RemoteLimit on every connect. Core.OnSceneWasLoaded clears it on every scene load as well (Core.cs:37-40).

Non-Harmony hooks:
- Core.OnUpdate and OnFixedUpdate call ArrowRecoveryScheduler.Tick (Core.cs:42-50), which does nothing off the server (ArrowRecoveryScheduler.cs:39-71).
- ShrinePages.LateUpdate (a MonoBehaviour) does the server's page turning.

0.4.4 ONLY (decompile rs_disabled; removed in 0.4.7)

a. BowStringPullbackInputMethod.CheckForPullback. PREFIX (ref float currentPull). HolyArrowCharge.cs:9, body :47-85.
   - For an input whose skill is LightBeamSkill, while the string is held with an arrow on it: if the pull is within max(0.08, 25%) of the threshold, it is rewritten to just above the threshold. If it is within max(0.15, 50%), the same happens for up to 0.2 s. So Holy Arrow's charge survives small dips.
   - Plus prefixes on Deactivate, Reset and SetArrowEntity that clear its table (:12-37).
   - QuickshotRing's Draws.DrawGrace still uses Priority.Low "after RevampedSkills' pull rewrite" (Draws.cs:570-581). In 0.4.7 that rewrite no longer exists.

b. LightBeamSkill.HandleInputChanged. PREFIX and FINALIZER. HolyArrowDamage.cs:10, :44-57.
   - Nested ExtraArrowUpdater.CreateExtraArrow PREFIX (ref float damageMultiplier), :26-37.
   - Server: when Holy Arrow arms, multiplies the beam's damage multiplier by Archery.HolyArrowDamageMultiplier (default 2.0).

c. LightBeamSkill.DestroyShotArrow. PREFIX that returns false, i.e. a full replacement. PreserveHolyArrow.cs:10-61 (0.4.4).
   - Sets IsIgnoringCollisionOnShoot, unsubscribes the game's handler, and never destroys the arrow.
   - Server: tracks the arrow and calls StopHolyArrow (ArrowReturn.cs:116-130 in 0.4.4). StopHolyArrow sets OneOffMultiplier to 0 for 0.5 s, then parks the arrow near the owner, or at their feet 0.45 m in front.
   - 0.4.4 has no ServerSpawnManager patch.

HOW THEY ARE APPLIED (checked in the MelonLoader 0.7.3 and HarmonyX 2.10.2 code on this machine; not yet seen happen at runtime)

Applied twice:
- RevampedSkills has no [HarmonyDontPatchAll]. So MelonLoader's MelonBase.HarmonyInit first patches every annotated class in it, under the owner "<assembly full name>:Revamped Skills" (scratchpad melonloader\MelonLoader.MelonBase.cs:506 and :548-570).
- MelonLoader's Core.Start fires HarmonyInit before OnApplicationStart, which is what calls OnInitializeMelon (MelonLoader.Core.cs:103-106).
- Core.OnInitializeMelon then patches the same classes again, under "ATT.RevampedSkills".
- HarmonyX never removes duplicates: PatchInfo.cs:122-130, PatchClassProcessor.cs:198-201 and PatchSorter.cs:216-220 all keep both copies.
- So every patch method above very likely runs twice per call.
- The two copies of a class share one __state, because HarmonyX keys it by declaring type (HarmonyManipulator.cs:323-329, :806-809).

Prefix order:
- HarmonyX runs every prefix even after one returns false, and ANDs their results (HarmonyManipulator.cs:161-203).
- So RevampedSkills' prefixes always run next to QuickshotRing's, even when QuickshotRing's skip the original.

Not observed because:
- MelonDebug is off, so the "HarmonyInit PatchAll" lines are not printed.
- No Air Slash or Power Shockwave line, the only per-call logging, appears in any current or older log.
- Quick check: in UnityExplorer, read Harmony.GetPatchInfo(<BowString.OnShoot>).Owners.
### Behaviours
ARROW RECOVERY (server, every archer, no orb or skill needed; on whenever Archery.Enabled is true, which it is on this server)

What gets tracked:
- Every arrow shot from a bow, except transient arrows and the game's extra arrows (RecordBowShot; ArrowReturn.cs:60-128).
- Each shot resets that archer's clock (ArrowRecoveryClock.cs:12-15).

The return:
- It starts once the archer has gone ReturnAfterSeconds (10 s) without shooting.
- Every 2 s until it succeeds, each tracked arrow that is still loose (not held, not docked, entity alive, owner alive) is:
  - moved to 0.35 m above the archer's feet and embedded there (ArrowReturn.cs:196-209);
  - un-embedded, its flight stopped;
  - docked instantly into the first carried quiver that accepts it (ArrowReturn.cs:303-351).
- Quivers searched: BackDock, both hip docks, both belt docks and both hands, then every pouch or bag inside those (CarriedQuivers.cs:13-79). Acceptance is checked with PickupDock.CheckAllowed, and the dock is done with DockInto.
- If no quiver takes it, the arrow is left parked at the feet (:347-350).
- So, by the code, arrows anywhere in loaded chunks come back however far away they are.
- Picking an arrow up or docking it anywhere cancels its tracking (:116-121, :267-288).
- Arrows dropped by hand or taken out of a dock are tracked for world safety only.

Feedback on the archer's client:
- Phase 1 (the dock is coming): the game's own dock sound for that quiver is muted.
- Phase 2 (the arrow arrived; at most one every 0.35 s): the game's DockAddedTo sound plays on the quiver at 0.25 volume, plus a haptic pulse (0.12 strength, 0.035 s, 90 Hz) in the hand nearer the quiver (ArrowReturnFeedback.cs:59-212).

Beacon (client only; it also works when the server lacks the mod):
- An arrow the local player shot gets a small cyan diamond 18 cm above it, facing the player's head, once it has been still for 0.5 s (ArrowRecoveryBeacon.cs:17-92).
- It is drawn with a LineRenderer and the 'Unlit/Color' shader.
- It is removed when the arrow is picked up or docked.

Log lines:
- "[Archery] Recovery: True, delay: 10s ..." once (ArrowRecoveryConfig.cs:81).
- "[Archery] Rescued an arrow leaving the map; return cooldown retained." (ArrowReturn.cs:220).
- Warnings for a cancelled return, a failed terrain check and a failed parking.

WORLD SAFETY (server, tracked loose arrows)

- Terrain probe (ArrowReturn.cs:233-260; ArrowTerrainProbe.cs:21-72; ArrowWorldSafety.cs:55-76):
  - Every physics tick, for an arrow that is not embedded, it sphere-casts 1 cm from the arrow's previous position to its current one, and the same from the tip.
  - It checks the Default, Terrain, Ground, GroundOverlay, Wall and TerrainWallOnly layers, and ignores triggers, rigidbodies, pickups, players and anything with a HealthObject.
  - On a crossing, it puts the arrow 2 cm off the hit surface, embeds it there (Embeddable.Embed(null, ...) through reflection) and forces a position sync.
  - It also keeps ContinuousDynamic collision on (patch 5).
- Leaving the map: if an arrow leaves every chunk, enters an unloaded chunk, or its chunk unloads while the owner is elsewhere, it is parked at the owner's feet instead of being destroyed or unloaded (patches 10-13).

HOLY ARROW / LightBeamSkill (server)

- 0.4.7: the arrow Holy Arrow consumes is NOT destroyed any more.
  - It is parked, embedded, 0.35 m above the archer's feet.
  - It then returns to the quiver like any other shot arrow, 10 s after the archer's last shot.
  - This needs Archery.Enabled; with recovery off, the game's sacrifice is unchanged.
  - The beam itself and the charge are untouched in 0.4.7.
- Effect on spectators:
  - In vanilla, the server destroys the arrow inside OnShoot, before Shooter.SyncShoot relays the shot to everyone else (game Shooter.cs:45-66 and :124; LightBeamSkill.cs:66-71).
  - With 0.4.7 the arrow is alive, and already parked, when the relay goes out. So a spectator's relayed Shooter.Shoot now finds the arrow.
  - QuickshotRing's stated assumption ("a spectator's relayed Holy shot gets no arrow") does not hold on this server.
  - What spectators and the shooter actually see is not verified.
- 0.4.4 (the friend's version):
  - the charge survives small dips in the pull;
  - the beam deals Archery.HolyArrowDamageMultiplier (2.0) times its damage;
  - the arrow is kept, its damage muted for 0.5 s, and parked.

BOWS / BowString / Shooter

- The only hooks are: the BowString.OnShoot prefix, the OnUndocked and FinishInteraction watchers, the CreateExtraArrow marker, the flight-stop lambda, and the Projectile.Shoot postfix (which only acts for Air Slash).
- Nothing changes the draw, pull, release, speed, deviance, damage or the skills' timers in 0.4.7.

SKILLS / SHRINE (both sides)

The owned-skill cap:
- MaxOwnedSkills (1-64; 16 here; vanilla 8) replaces ProgressionSettings.MaxBoughtSkills.
- It is enforced in IsSlotAvailable and in UnlockSlot. The UnlockSlot refusal on the server is silent.
- The game's "8 skills" achievement now fires at the configured count.
- No change to skill trees, slots, dependencies, orb positions or practice: the mod never references ProfessionSkillTree or ProgressionSlot.

The bought-orb ring (BoughtSkillVisualizer):
- 64 holders on the vanilla 8 positions, shown 8 per page, up to 8 pages.
- Holders on other pages have their renderers and colliders turned off, and their crystals cannot be grabbed.

The paging panel:
- Built from the embedded asset bundle: prefab 'ShrineNavigationRuntime', placed under 'Ruins Visual Base' according to the embedded ShrinePlacement JSON.
- It has Previous and Next buttons, page markers and an "owned / limit" counter.
- It uses the game's 'Ruins_02_glow_m' material, and falls back to Arial text labels if the bundle or that material is missing.

Page turning:
- The SERVER turns the page when the shrine owner's hand comes within 8 cm of Previous or Next. There is a 0.5 s cooldown, and no turn while a crystal is held.
- The server then re-sends the shrine visuals (ProgressionShrine.UpdateVisuals through reflection; ShrinePages.cs:194-212).
- Clients only animate the button and give a haptic tick (ShrineAttachmentView.cs:235-281).

NETWORKING

1. New custom message: EntityMessageType 247.
   - Lives on each player entity, as a MethodSyncStruct<ArrowReturnFeedbackPacket> (ArrowReturnFeedback.cs:15 and :49).
   - Sent server to that player only (SendToPlayer, :109).
   - Payload (:113-121): uint Phase (1 = dock coming, 2 = arrived) and an entity identifier Dock (the quiver's dock).
   - The game's enum has no value 247 (EntityMessageType.cs jumps from 220 to 250).
   - Registered on both sides at PlayerController.Initialize.

2. Existing message changed: the shrine's SyncD (ProgressionShrine.SyncCurrentPlayer, which calls the visualizer's Serialize; game ProgressionShrine.cs:62-91).
   - It gains 3 uints in front of the ring data, or 6 if the mod is patched twice.
   - The ring data itself becomes 64 holder flags instead of 8.
   - A client without this exact build misreads it.
   - A client with the mod and a server without it throws on every shrine sync. That is the friend's failure; the ring crystals are then never anchored, so grabbing them throws in FilterInteraction.

3. Existing messages used more:
   - ForcePositionSync and the embed sync (parking);
   - dock updates (returns);
   - Power Shockwave's own effect list (up to 31 positions).

4. Nothing else is sent: no scene-level messages, and no SyncLib.

So friends need this exact file. Both server and every client must run the identical 0.4.7, or none of them.

COMBAT (not archery)

- Power Cross Slash's air slash homes gently:
  - it searches within 30 m for a living, non-player combatant;
  - the target must be within 12 degrees of the launch line, at most 2.5 m off it, and within 20 m and 20 degrees of the projectile's heading;
  - steering is at most 12 degrees off the launch direction, at 0.45 rad/s, keeping speed;
  - it searches again every 0.15 s with FindObjectsOfType for PlayerController and HealthObject, a scene-wide search;
  - it logs every target it acquires (AirSlashHomingController.cs; AirSlashTrackingPolicy.cs).
- Power Shockwave (hash 21572) chains:
  - after the original hits, it jumps from the first creature hit to the nearest unhit loaded combatant within 10 m, then on from each one, with no limit;
  - each jump deals an ImpactHitWithTool at half effectiveness;
  - aggro goes to the caster, and the hit effect is added to the skill's message (PowerShockwaveBounce.cs:141-255);
  - it does one FindObjectsOfType<HealthObject> scene search per cast.

CONFIG READ

File: <game>\UserData\RevampedSkills.json, read through MelonEnvironment.UserDataDirectory. It uses no MelonPreferences and is never reloaded while the game runs.
- "MaxOwnedSkills": integer, 1-64.
  - If the file is missing, it is created with 16.
  - An invalid value logs an error and falls back to 8.
  - Read lazily and cached; only the server uses it (SkillLimitConfig.cs:14-46).
- "Archery": an object.
  - "Enabled": bool, default true.
  - "ReturnAfterSeconds": number, 1-600, default 10.
  - Read lazily on the server at the first arrow event, then cached (ArrowRecoveryConfig.cs:14-92).
  - If the section is missing, the mod writes the defaults into the file. It did exactly that here: file time 18:59:47.631, log line at 18:59:47.633.
  - Any invalid value logs an error and turns recovery OFF.
- Current server values: {"MaxOwnedSkills": 16, "Archery": {"Enabled": true, "ReturnAfterSeconds": 10}}. The client has no such file, because nothing reads it on a client.
- 0.4.4 also reads "PreserveHolyArrow" (bool, default true) and "HolyArrowDamageMultiplier" (0.1-10, default 2.0).
- Hard-coded values that are not settings:
  - shrine pages of 8, at most 64;
  - touch radius 8 cm;
  - feet offset 0.35 m;
  - retry every 2 s;
  - beacon after 0.5 s still;
  - terrain probe radius 1 cm, clearance 2 cm;
  - Power Shockwave: 10 m, half effectiveness, cap 31;
  - Air Slash: the numbers above.

INTERPLAY WITH QUICKSHOTRING AS IT STANDS (from the code; nothing here was observed in play)

Patch targets shared with QuickshotRing:
- BowString.OnShoot: AutoDock.cs:144, Draws.cs:671, Quickdraw4.cs:248, QuiverGather.cs:124.
- LightBeamSkill.DestroyShotArrow: HolyArrow.cs:258.
- ExtraArrowUpdater.CreateExtraArrow: HolyArrow.cs:313.
- Projectile.Shoot: HolyArrow.cs:399.
- Pickup.OnUndocked: AutoDock.cs:638, QuiverGather.cs:66.
- PlayerProgressionManager.UnlockSlot: Builds.cs:199, Shrine.cs:119, Tree.cs:477.
- PlayerProgressionManager.IsSlotAvailable: Shrine.cs:87.

Interactions:
1. Quiver Gather stands down while RevampedSkills is loaded (QuiverGather.cs:55-59, :77-80, :200).
   - So arrow return is RevampedSkills' 10 s rule for everyone. The server logged this at 19:02:47.
   - Try-out arrows that QuickshotRing's CheckAllowed refuses for a player's own quivers end up parked at that player's feet.
   - Range arrows a player shoots get docked into that player's own quiver. They stay never-saved.
2. With the double patching:
   - PreserveHolyArrow leaves its [ThreadStatic] 'active' pointing at the consumed arrow after every DestroyShotArrow. The second prefix copies the first's Consumption into the shared __state, and the last finalizer restores it (PreserveHolyArrow.cs:48-51, :74-77).
   - Until the next DestroyShotArrow, any ServerSpawnManager.Destroy(thatArrow, false) is silently skipped (:33-41).
   - QuickshotRing destroys through SceneDestroy, which calls Spawner.Destroy(this) (game NetworkEntity.cs:597-603). Affected call sites: Skyfall.cs:566 and :590 (a Skyfall cast fired while Holy Arrow was armed), TestRange.cs:578, AutoDock.cs:469. Such an arrow would then survive its clean-up.
   - Power Shockwave's chain would be dealt twice.
   - The dock-sound mute would work for only every other arrow when several reach the same quiver together.
3. Seek.PlaceReal's "in the air" placement (Seek.cs:887-906) moves the real arrow up to 80 m without embedding it.
   - RevampedSkills' terrain probe then sweeps a straight line from the hold point. It can re-park the arrow at the first terrain on that line, since the homing path was curved.
4. On the shooter's client, the beacon is a child object created 0.5 s after the arrow settles. It may show over Seek's hidden, held real arrow.
5. The client sees cap 8 until a shrine sync sets RemoteLimit, and RemoteLimit resets on every scene load and connect. QuickshotRing's Tablet.cs:1171 reads MaxBoughtSkills on the client.
6. QuickshotRing's Builds.OnUnlockSlot postfix (Builds.cs:199-202) still runs when RevampedSkills refuses an unlock at the cap. Its Shrine.RefuseSecondUltimate prefix runs alongside RevampedSkills' prefix.
7. With QuickshotRing's [HolyArrow] KeepsArrow at false (server cfg line 202), the game's sacrifice runs, so RevampedSkills' keep-and-park takes effect. With KeepsArrow on, both mods would act.
