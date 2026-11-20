# Skyfall fact check: telling-the-caster (2026-11-19, read-only)

## Recommendation
CASTER: use the game's own player text message, not TavernLib roles.
- Server side:
  - Keep grants and festival mode in memory, keyed by account.
  - From Main.OnUpdate, before the headless return, call PlayerCommunicationManager.Instance.SendMessageToPlayer(player, "QSR_SKYFALL_V1|<holds 0/1>|<festival 0/1>|<server shared-settings check>|Skyfall is yours: draw a full bow at the sky", seconds).
  - Send it on grant, on revoke, to every holder when festival mode is toggled, and once per rejoin as soon as player.PlayerController != null. Queue it with a timeout, as CustomIngots' ServerConfigSync.cs:59-93 does.
  - The server's mod code needs no patch, only the call.
- Client side: one Harmony postfix on PlayerTextMessage.Serialize, active while reading, with `ref PlayerTextMessage __instance`.
  - Latch holds and festival in the postfix, and compare the settings check the way Builds.cs:347-354 does.
  - Strip the tag from the private `message` field so the popup reads plain text. To hide the popup instead, use a prefix on PlayerMessageDisplay.Display, as CustomIngots does.
  - Clear the latch when the player leaves the server.
- What this gives:
  - The game's existing message type and public API, and the same approach two mods in this modpack already use.
  - Nothing saved, nothing sent per shot, no new reference, no dependency on TavernLib.
  - The caster sees immediate, readable feedback.
  - It also closes the gap where the [shared] settings are never checked for an admin with no orbs.
- What it costs: QuickshotRing would, for the first time, send a message the game would not have sent (one per grant, revoke, toggle or rejoin). No zero-send option reaches an admin with no orbs before release. The owner should approve this exception and the CLAUDE.md rule should be amended.

TAVERNLIB VERDICT
- Writing users.json is NOT acceptable: it persists the grant, which breaks 'never saved', and it rewrites the login-token file.
- Pushing a role list by reflection is not 'messages the game already sends'. Type 32 is TavernLib's message, the method is internal, and TavernLib overwrites that list at every join and every users.json change.
- In spirit it is about as rare as the text message, but strictly worse: it makes one mod's state depend on another mod's internals.

SPECTATORS: use the claim relay.
- In PutResolved, the server writes, from its OWN decision (never from the shooter's claim), a Skyfall code into the unsteered window bits: 0 = none (as today), 1 = Skyfall, 2 = Skyfall in festival mode, 3 = spare.
- Add a field to ShotClaim and Copy, write it at Wire.cs:57 when the claim is unsteered, and read it at :81-83.
- A Skyfall release must always be relayed unsteered.
- No version bump: every claim that is not Skyfall stays bit for bit the same, and current readers ignore the window.
- Spectators already have the relay when their Shoot runs (Draws.cs:241), so Skyfall can take over there.
- If the rain ever needs a seed or a centre, the mark-count slot holds up to 3 x 17 bits.

FESTIVAL MODE TO EVERYONE: not needed.
- Only the caster (before the release, through its message) and spectators (at the cast, through the relay) use it. Healing, feeding and knock-overs are server-side.
- If an announcement is wanted: SendMessageToAllPlayers, and send it again to players who join later.
- Side note: the 'OUT!' popup needs no traffic. Each client can show it for its own player from the relayed cast through PlayerMessageDisplay.Display, which is local (PlayerMessageDisplay.cs:51-54).

## Findings
Roots: QR = C:\Users\dmand\OneDrive\Desktop\attmod\QuickshotRing, DEC = C:\Users\dmand\OneDrive\Desktop\attmod\_decomp, TL = C:\Users\dmand\AppData\Local\Temp\claude\2c7b432df063cefe\3aab37a1-c87d-4a47-8bd5-6f6c1d86eeb4\scratchpad\tavernlib. Two third-party mods from the user's client Mods folder, decompiled for this check: CI = ...\scratchpad\dec-customingots, PET = ...\scratchpad\dec-pet.

TAVERNLIB ROLES (version 1.5.4, TL\TavernLib\Tavern.cs:20; the same md5 63fdba57... in the client and server Plugins folders)
- The whole public API:
  - `Roles.RolesChanged` (TavernEvent<RolesChange>, Roles.cs:24). Its Subscribe and Unsubscribe(Action<RolesChange>) are public; Invoke is internal (TL\TavernLib.Utils\TavernEvents.cs:12-25).
  - `bool HasRole(Connection, string)` (Roles.cs:40-43).
  - `IReadOnlyList<string> GetRoles(Connection)` (:45-58).
  - `IReadOnlyList<string> GetRoles(string username)`, case-insensitive (:60-64).
  - `RolesChange { Username, OldRoles, NewRoles }` (RolesChange.cs:5-19).
  - `Start()` is internal (:66). There is NO add, remove or set.
- It runs on the server only: Start is called only under /start_server (Tavern.cs:83-99), so on a client GetRoles is always empty. Re-verified in the server's Latest.log:38,45.
- The only source is users.json (Roles.cs:28,141-154). It is polled every 1 s and reloaded once the file's write time has held for one poll (:93-106), so a change takes 1-2 s.
- On a change, Announce sends MessageType 32 (a count, then the strings) to that user's connection, then raises RolesChanged (Roles.cs:156-193; ServerSettingsRolePatch.cs:21-38).
- At join, a postfix on Player.InitializePlayerOnServer sends the roles from the file (ServerSettingsRolePatch.cs:13-19). The file is re-read at each join (PlayerJoinFilter.cs:94-95,132-133).
- On the client: TavernServices.GetService<EntranceMessageHandler>() (TavernServices.cs:22-30), then .LocalUser.Roles or .HasRole(string) (EntranceMessageHandler.cs:12; UserConfig.cs:21-32).
  - Each type-32 message clears and refills that list and logs a WARNING (EntranceMessageHandler.cs:38-53). The handler is set only on clients (:27-36).
  - Nothing clears the list when the player leaves a server.
- Granting from code is possible only two ways:
  - (a) Edit TavernServices.GetService<TavernManager>().UserConfig.LastRead.Users[name].Roles and call UserConfig.WriteToFile() (all public: TavernManager.cs:15; ServerConfigFile.cs:11,42-63).
    - The role is PERSISTED: it survives restarts and crashes.
    - It rewrites the whole file, which holds every account's login token (UserConfig.cs:15-16), with a non-atomic File.WriteAllText (ServerConfigFile.cs:56).
    - That write races TavernLib's own read and rewrite on every login (AuthManager.cs:123,148-173).
  - (b) Call the internal `ServerSettingsRolePatch.SendRoles(Connection, IReadOnlyList<string>)` by reflection (:21). Nothing is saved, but:
    - TavernLib replaces the client's list with the file's roles at every join and on every users.json change for that user.
    - Roles.GetRoles on the server never shows the role.
- Type 32 is TavernLib's own message type (TavernMessages.cs:3-7). It exists only because TavernLib widens the game's message-type field (MessageExpanderPatch.cs:12-49).

QUICKSHOTRING'S OWN CHANNELS
- Build block: exactly 32 bits, none free.
  - Layout: 4+2+2+2+3+6+4+1+2+6 (QR\Build.cs:92-124). Ultimates use 3 of 3 bits (:32); main path uses all 4 values (QR\Path.cs:8).
  - Side bits 1,2,4,16,32 and bridge bits 1,2,4 are 'reserved for good' and dropped on read (Build.cs:33-38,100-105,134-135).
  - A version-3 bump would work mechanically: version-2 readers return UnknownVersion and skip it (Build.cs:129-131; Builds.cs:359-364). The tests pin 32 bits and version 2 (QuickshotRing.Tests\WireTests.cs:24, HomingH3Tests.cs:43).
  - It is still the wrong channel. The block rides only BowDrawSkill status messages (Builds.cs:293-323). Those exist only for players who own a bow skill (registered at level 1, DEC\PickupBasedSkill.cs:182-189), are sent only when a draw's state changes (:90-96,200-240), and go to every player near the archer (DEC\PlayerProgressionManager.cs:437-441). An admin with no orbs gets none.
  - Pushing a status on demand with the public PlayerProgressionManager.SendMessageToPlayer (:443-447) is no good either: the client would run that status's effects and sound on a pickup id (PickupBasedSkill.cs:115-166).
- Claim relay: goes to everyone near the bow EXCEPT the shooter (DEC\Shooter.cs:62-65; DEC\Alta.Networking\Sync.cs:61-64; EntityMessageHandler.cs:23-36; QR\Claims.cs:184-185).
  - A spectator reads it before its own Shoot runs (Shooter.cs:47,61; Claims.cs:196-252) and uses it at QR\Draws.cs:241-249.
  - Spare room: the unsteered window's 2 bits are written 0 (QR\Wire.cs:57) and ignored on read (:81-83). The mark count is written 0 (:68) and readers skip 17 bits per mark (:92-93).
  - The server writes its own decision into every relay in PutResolved (Claims.cs:49-62). ServerCheck copies through Copy (QR\Rules.cs:214-219; Wire.cs:23-26).
- Flight sync: the server sends it to everyone near, the shooter included, from its own Shoot (DEC\Arrow.cs:235-253; fields :27-31). It arrives only after the release.
  - Quickdraw 4 marks it with speed 999 (QR\Quickdraw4.cs:23-25,289).
  - Seek refuses it for an arrow it is holding (QR\Seek.cs:849-850).
- The [shared] settings checksum reaches a client only inside the build block (QR\Builds.cs:347-354). An admin with no orbs is never checked.

GAME MESSAGES TO ONE PLAYER
- `PlayerCommunicationManager.Instance.SendMessageToPlayer(IPlayer, string, float = 5f)` (DEC\PlayerCommunicationManager.cs:52-66):
  - It is public, server-only, EntityMessageType.PlayerMessage (:24).
  - It carries a string and a 0-100 s float (DEC\PlayerTextMessage.cs:20-24).
  - The server refuses to send if the player has no PlayerController (:58-61). The client deserializes the message, then shows it only if it has a PlayerController (:27-41).
  - The game uses it for AFK warnings (DEC\AfkTimer.cs:146,148), and so does TavernLib's console command `player message` (TL\TavernLib.ModulePorts\PlayerCommandModule.cs:685-694).
  - It lives in Root.Township.dll, which the mod already references.
- It is already a mod channel in this modpack:
  - CustomIngots.Config waits for a joining player's PlayerController (up to 60 s, CI\CustomIngots.Config.Integration\ServerConfigSync.cs:41-93). It then sends tagged parts 'CUSTOM_INGOTS_CONFIG_V1|...' with duration 0 (:95-128) and hides them on the client (ClientConfigSync.cs:27,43-82).
  - Pet.Client reads 'PET_...|' tags in a postfix on PlayerTextMessage.Serialize (PET\PetVendorDiagnostics\PetCloneMessagePatch.cs:8-30; Core.cs:65).
- Where to hook on the client:
  - A HandlePlayerMessage prefix sees the sync's old Value, not the arriving message (DEC\Alta.Networking\MethodSyncStruct.cs:21-28; Sync.cs:67-71). The message is only deserialized inside the method body (PlayerCommunicationManager.cs:29).
  - A prefix on Display misses messages that arrive while there is no PlayerController (:32-35). The client's Latest.log:337006 has 8 such warnings.
  - The Serialize postfix (Pet.Client's way) sees every message.
  - A message for an entity the client has not spawned yet is replayed once it spawns (DEC\Alta.Networking\InternalMessage.cs:52-66).
- Rejected:
  - FestivityManager: a world-wide game festivity, reset by date every second, which also drives loot, caves, populations and decorations (DEC\FestivityManager.cs:64-91; Alta.Loot\Origin.cs:125; Alta.Caves\Distribution.cs:190-239; FestiveFeature.cs:37,75).
  - UserInfoAndRole.MemberStatus: read-only (Developer/AltaMod/Subscriber/None), sent only when a player connects (UserInfoAndRole.cs:26-51; Player.cs:874-885).
  - Progress flags: go through the dead web API (GameTaskManager.cs:126-133,187-191).
  - ServerSettingsSync.syncAvailability: one private bit, sent at connect and on change (ATT.Character.QuickAccessMenu\ServerSettingsSync.cs:27,37,49-63,205-212). It could carry festival mode to everyone, but nothing needs that.

## Risks
- Grant, revoke or festival toggle while the bow is drawn:
  - The shooter's local decision can disagree with the server's. This affects only looks; damage is server-only.
  - An optional fix is a Skyfall marker in the server's flight sync (Arrow.cs:248-251), like Quickdraw 4's 999. Seek-style held arrows refuse that sync on the shooter (Seek.cs:849-850), so it would need its own handling.
- The text message:
  - The server refuses it while the player has no PlayerController, so it must be queued and retried.
  - A message that arrives first kills whatever popup is showing (PlayerMessageDisplay.cs:51-54,77-82).
  - It is a string protocol: pick a tag no other mod uses (CustomIngots uses CUSTOM_INGOTS_CONFIG_V1|, Pet.Client uses PET_ROSTER|, PET_CLONE|, PET_CLONE_GEAR|) and keep it short (CustomIngots splits at 220 characters; the real limit is unknown).
- A Skyfall grant command cannot run from in-game: TavernLib cancels in-game command routing (CommandSecurityPatches.cs:15-21).
  - It has to be a server console command (TavernLib registers its own through CommandService.CommandCollection.Collect, Tavern.cs:36) or a request in the cfg, as tools\TestBuildMenu.ps1 does today.
  - Console handlers may not run on the main thread, so send from Main.OnUpdate.
- Spectators:
  - They see nothing before the release: there are no statuses for an admin with no orbs. A wind-up before release would need a holder list sent to every player.
  - Only players near enough to have the bow spawned get the relay.
  - Players who join during the 20 s World-Nail miss it.
- If TavernLib roles are used anyway:
  - Re-send after TavernLib's join postfix and in RolesChanged, which runs after Announce's own send (Roles.cs:176-188).
  - The client keeps the last server's roles after leaving.
  - UserConfigFile.GetUser lowercases the name for a case-sensitive dictionary (UserConfigFile.cs:12-16), while Roles is case-insensitive.
- Do not reuse the build block's reserved bits ('reserved for good'). A v3 bump also means changing the tests.
- The scout's suggested gate on the 'admin' role is replaced by the owner's command-only grant. A role is at most the transport, never the grant.

## Not verified
- Not run in-game:
  - That PlayerCommunicationManager.Instance exists on the local dedicated server.
  - That the message reaches the local client from the local server. Delivery and the 'no player controller' path are only seen in today's client log (Latest.log:337006), from a remote server.
  - That writing the private `message` through `ref __instance` in a Serialize postfix changes the text shown. This follows from the struct `this` being passed by reference (PlayerCommunicationManager.cs:29,38) but was not run.
  - That SyncChannel null is reliable and ordered, and that duration 0 shows nothing.
- Socket.SendToPlayers was not decompiled. Synchronous serialization is inferred from InternalMessage.cs:30-36. It matters only if bits are appended instead of a text tag.
- Whether the PlayerCommunicationManager entity is global (spawned for every player) is unchecked. If it never spawned on a client, the message would wait forever (InternalMessage.cs:61-66).
- The scout's 'Roles: ["admin"]' on the client could not be re-checked.
  - Today's client logs (Latest.log:2981 and 5 archived logs) all read Roles: [], from sessions on a remote server.
  - The logs from 11-10 to 11-18 have been rotated away.
  - The client's Mods folder currently holds a different modpack: no QuickshotRing.dll, and RevampedSkills is installed.
- Client mods checked only by a string search of the binaries: 6 of about 55 mention PlayerTextMessage or PlayerCommunicationManager. Only CustomIngots.Config and Pet.Client were decompiled; PlayerArmor.Client, RepairShop, CombatTrialOverhaul and Televator200 were not. The server's QuickshotRing, SyncLib and TavernVoice do not reference them.
- How far the relay reaches (the bow entity's spawn radius) was not measured.
- What 'the game's OUT! popup' is: no 'OUT!' string exists in the decompiled code. The only match is [Header("OUT")] at ArrowFlightSettings.cs:33; it may be an asset.
- TavernLib was read from the earlier decompile (1.5.4, matching the DLL's version), not decompiled again.
