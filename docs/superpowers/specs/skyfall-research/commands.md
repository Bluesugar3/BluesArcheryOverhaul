# Skyfall fact check: commands (2026-11-19, read-only)

## Recommendation
Use the launcher's console. Add a `skyfall` module to QuickshotRing in its own file (for example SkyfallCommands.cs), with one line in Main.OnInitializeMelon that calls its Register().

```
[PlayOnly, ServerOnly, Module("skyfall", "Skyfall admin skill")]
internal static class SkyfallCommands {
  internal static void Register() { try { CommandService.CommandCollection.ProcessModule(typeof(SkyfallCommands), typeof(SkyfallCommands).Assembly); } catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: console commands not added: {e.Message}"); } }
  [Command(null, "Who holds Skyfall; festival on/off")] static string Status() {...}
  [Command("give", "Give Skyfall to an online player")] static string Give(string name) {...}
  [Command("take", "Take Skyfall away")] static string Take(string name) {...}
  [Command("festival", "festival on|off")] static string Festival(string state = "") {...}
}
```

Why ProcessModule rather than Collect:
- It does the same thing for this one class.
- Collect calls GetTypes() on the whole DLL. If QuickshotRing ever gets a hard reference to an optional DLL (for example TavernLib with Private=false, which the scout suggested), that call throws and every command is lost.
- Registering on every process is harmless, because ServerOnly hides the module on clients. TavernVoice registers only on the server process; either works.

What happens for "skyfall give Blue_XD":
1. The owner types it in TavernLauncher - Server.exe's Console. A remote admin could use Client.exe's Remote Console or TavernKeeper with the owner's token.
2. The launcher sends {id, content} over ws 1760. The server dispatches it to the main thread and runs CommandService.Handle.
3. The search lowercases the words, matches the module and the command, the requirements pass, and the name is bound to the string parameter.
4. Give looks the name up among Player.AllPlayers using PlayerConverter's two rules:
   - It must match exactly one player.
   - Otherwise it returns a refusal such as "No online player 'vamn' (online: Blue_XD, VMan)".
5. It records Grants[UserInfo.Identifier] = Username in memory only. The grant is gone at restart, survives a rejoin, and is never saved.
6. It writes one "[Archery] Skyfall:" line at Summary level.
7. It tells the client:
   - The server sends a fixed PlayerMessage text.
   - The QuickshotRing client recognizes that text with a prefix on PlayerMessageDisplay.Display and sets its local "I hold Skyfall" flag.
   - The server's tick sends it again once per connection, as soon as the grantee has a PlayerController. This covers rejoins and client restarts.
8. It returns "Skyfall given to Blue_XD (2000000001)", which appears in the launcher's console.

"skyfall take <name>":
- Same route in.
- It checks the names stored in Grants first, so it works for offline players, then falls back to online players.
- It removes the grant, notifies the player if they are online, and returns a line of text.
- Still to decide: what happens to a rain that is already falling.

"skyfall festival on|off":
- Parse on/off/yes/no/1/0 the way TavernVoice does (TV ServerCommands.cs:97-117). With no argument, it reports the current state.
- It sets a flag in server memory and notifies the online grantees, because their hold looks need it.
- Spectators read it from the relayed claim's spare bits at release (scout: Wire.cs:57,81-83).
- Do not call it "festivities": the game already has `festivities start/stop` (G\FestivityCommandModule.cs:9).

Return a string for every refusal instead of calling CommandService.ThrowError. A thrown error arrives wrapped in a TargetInvocationException because the command is called through method.Invoke (Command.cs:71; CommandService.cs:128-133).

Trade-offs of this route:
- Good: it adds no new traffic, the reply appears instantly in the console, and the owner already uses this console.
- Bad: only the owner can use it, only from a desktop window, and it is all-or-nothing power.

Fallback (not needed, because a working console exists; use it only if the owner wants buttons instead of typing):
- Add [Skyfall] Player, GiveRequest, TakeRequest and FestivalRequest to the server cfg.
- Act on them with TestRequest.IsNew (QR\Build.cs:240-247). Record the start-up numbers and never act on them (QR\TestTools.cs:80-110), so a restart re-grants nothing.
- The server picks them up within 2 s (QR\Main.cs:200-203).
- Buttons in tools\TestBuildMenu.ps1 would use Set-CfgKeys, Test-Name and a ticks stamp (:141-167,193). The answer would go in a status file read with Read-Shared (:169-179).
- Costs: up to 2 s delay, works only on the server PC, and a bad write resets every setting (:72-73).

## Findings
Path roots: G = C:\Users\dmand\OneDrive\Desktop\attmod\_decomp; QR = ...\attmod\QuickshotRing; SP = C:\Users\dmand\AppData\Local\Temp\claude\2c7b432df063cefe\3aab37a1-c87d-4a47-8bd5-6f6c1d86eeb4\scratchpad; TL = SP\tavernlib; TE = SP\tavernessentials and TV = SP\tavernvoice (both decompiled during this check); LS = SP\launcher_strings (strings taken from the two launchers' PyInstaller bundles; nothing was run, so these have no line numbers); SLOG = C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\MelonLoader\Latest.log; OLD = ...\MelonLoader\Logs\.

WHERE AN ADMIN CAN TYPE A COMMAND
- There is exactly one working way in: the game's own WebSocket console.
  - It listens on ws://0.0.0.0:1760 (G\Alta.Console\WebSocketRemoteConsole.cs:13,50).
  - It starts at login when the server's own login has debug_features (G\ServerConsoleManager.cs:47-58,67-73).
  - Login: the first message must be a JWT, checked by ValidateConsoleToken (G\Alta.Console\WebSocketCommandHandler.cs:180-206).
  - TavernLib replaces that check with an HMAC check against %APPDATA%\TheModdingTavern\server_secret.key, and the token must carry the Policy server_owner (TL\TavernLib.Patches\TeenyPatches.cs:274-312). TavernLib also gives every connection the Console group permission (TL\TavernLib.Patches\CommandSecurityPatches.cs:31-38).
  - Each request is {id, content}. It is dispatched to the main thread, runs as CommandService.Handle(content, context), and the result is sent back on the socket (WebSocketCommandHandler.cs:32-37, 275-300).
- VERIFIED live:
  - The socket started in all 10 server logs (SLOG:147).
  - A client at 127.0.0.1 logged in as "(Server:0)" with Group Permissions: Console (SLOG:15630-15640).
  - That client then ran typed commands:
    - SLOG:16214-16227: "player teleport vamn blue_XD". The typo gave "Could not find the player 'vamn'. Skipping" and then "Teleporting no one".
    - SLOG:17177-17187: player list.
    - SLOG:17817-17828: a teleport.
    - SLOG:23815-24027: wacky destroyall.
    - OLD\26-11-18_17-16-16.log at 17:34:16: "spawn blue_XD spyglass" worked.
  - All 6 logged connections came from 127.0.0.1.
- The programs that send these commands are the TavernLauncher apps:
  - TavernLauncher - Server.exe has a "Console" button. Its strings describe a "Local (owner-side) live console window" that reads console_token.txt, connects to 127.0.0.1:1760 and has a free-text box. The same window has a "Copy Console Token" button (LS\server.core.console_window, server.core.launcher_window).
  - TavernLauncher - Client.exe has "Remote Console" and "TavernKeeper". Both ask for the server IP plus "the console token they give you directly". TavernKeeper describes itself as "a plain admin pipe into the server" (LS\client.core.remote_console, client.core.tavernkeeper.window).
  - Autocomplete comes from a fixed list that is "Not guaranteed 100% exhaustive" (LS\tavern_shared.console_commands). So `skyfall` can be typed but will not autocomplete.
- These routes are dead:
  - The server process's own console window:
    - The game's input console (WindowsServerConsole) starts only with the /console argument or a Backquote key press (ServerConsoleManager.cs:53-57, 99-120).
    - SLOG:145 says "There is no keyboard attached!", and "Console Started" appears in none of the 10 logs.
    - The black window you see is MelonLoader's log output only; it takes no input.
  - The TCP remote console: TavernLib closes it as soon as it starts (CommandSecurityPatches.cs:23-29; SLOG:149-150).
  - Anything in VR:
    - RuntimeConsole's "Run on Server" and every menu or board command row call CommandSync.RouteCommand (G\RuntimeConsole.cs:156-184; G\Alta.Menu\ServerCommandDefinition.cs:20). TavernLib cancels that call (CommandSecurityPatches.cs:15-21).
    - Even without TavernLib, the server's SyncCommand only reads the string (G\ATT.Character.QuickAccessMenu\CommandSync.cs:45-48), and nothing else ever reads commandToSync.
    - IsMenuOrCommandsAvailableForUser always returns false (PermissionsHelper.cs:34-37).
- TavernEssentials is NOT installed on the server. The brief's path is wrong.
  - server\Mods holds only QuickshotRing, SyncLib and TavernVoice (SLOG:58-62,87 "3 Mods loaded"). TavernEssentials was last loaded in OLD\26-11-18_16-56-13.log:80.
  - I decompiled server\Patch\TavernEssentials.dll instead (md5 4802aafc…, the same as Desktop\TavernEssentials.dll).
  - It registers no commands and has no command input. Its settings board logs every command-row press as "this version runs none of them" (TE\TavernEssentials.SettingsBoard\SettingsBoardFeature.cs:255-265).
  - "console commands by name" (server UserData\TavernEssentials\config.txt:133-135) refers to its Names feature, which patches UserInfoAccess.SearchForUserAsync (TE\TavernEssentials.Names\NamesFeature.cs:62-72). That only affects offline name↔id lookups, which otherwise go to Alta's dead web API (G\UserInfoAccess.cs:81-97).

HOW COMMANDS GET REGISTERED
- TavernLib registers its own commands in Tavern.OnInitializeMelon with CommandService.CommandCollection.Collect(Assembly.GetExecutingAssembly()) (TL\TavernLib\Tavern.cs:34-37).
  - That adds the modules player (TL\TavernLib.ModulePorts\PlayerCommandModule.cs:30-31), chunks (ChunkCommandModule.cs:27) and document (TL\TavernLib.Debugging\DocumentModule.cs:15).
  - The first use of the getter collects only Root.Township (G\Alta.Console\CommandService.cs:22-33). Root.Township has no player or chunks module, so TavernLib's are additions.
  - A top-level module with an existing name is replaced silently (G\Alta.Console\CommandCollection.cs:85-86).
- There is already a working precedent on this server: TavernVoice registers "tavernvoice" the same way.
  - It runs only on the server process, in OnInitializeMelon, inside a try/catch (TV\TavernVoice\ServerCommands.cs:8-21; ServerVoice.cs:132; Mod.cs:21-42).
  - SLOG has no failure warning for it.
- QuickshotRing can do the same.
  - Root.Township is already referenced by every mod (attmod\Directory.Build.props:20). Collect and ProcessModule are both public (CommandCollection.cs:40,67).
  - Names must be lowercase. Input is lowercased (CommandService.cs:192-197) and then compared exactly (Module.cs:60; Command.cs:139).
  - Commands are static methods and can be private (CommandCollection.cs:105).
  - Nested submodules must be PUBLIC, because they are found with GetNestedTypes() (:93).
  - Collect reflects over every type in the DLL with assembly.GetTypes() (:42). ProcessModule only touches the one class.
  - There is no "too late": the command list is searched again for every command typed (CommandService.cs:73), and registering again is harmless (:85-86).
  - When to call it: once, on the main thread, in OnInitializeMelon (QR\Main.cs:72). QuickshotRing starts at SLOG:90 (20:42:36), 3 s before the socket opens (SLOG:147).

PERMISSION CHECKS
- Requirements are checked during the command search. A command that fails one is simply hidden, so the caller sees "Command not found" (G\Alta.Console\CommandPart.cs:29-48; CommandService.cs:74-78).
- ServerOnly = ServerHandler.Current != null. PlayOnly = Application.isPlaying (ServerOnlyAttribute.cs:7-9; PlayOnlyAttribute.cs:7-9). The game's own convention puts both on the module (G\Alta.Console.Commands\TimeCommandModule.cs:8-10).
- DevOnly checks the SERVER's own login token, not the caller's (DevOnlyAttribute.cs:7-13). Avoid it.
- NotAllowedInRemote compares the caller's id with the server's own login id (NotAllowedInRemoteAttribute.cs:5-15). It would block every console caller. Avoid it.
- WithPermissionOnly and NonSimpleServer check the Policies in the caller's token (WithPermissionOnlyAttribute.cs:12-18; NonSimpleServerAttribute.cs:9-25). The console token carries server_owner and debug_features (TeenyPatches.cs:244-255).
  - NonSimpleServer was seen passing live: the spawn module uses it (G\Alta.Console.Commands\SpawnCommandModule.cs:17-19), and the 17:34:16 spawn above worked.
- There are no permission levels. Every connection is the same identity, "(Server:0)" (SLOG:15635-15637). The roles in users.json are never consulted by the console.

FINDING A PLAYER BY NAME
- Player / IPlayer parameters use PlayerConverter. It searches ONLINE players only: an exact lowercase username or a numeric id, then the same with spaces and underscores ignored. If nothing matches, parsing fails with "value was null" (G\PlayerConverter.cs:20-36; G\Alta.Console\Command.cs:244-249).
- PlayerList parameters take a comma-separated list, and "*" means everyone online. Unknown names are skipped silently (G\PlayerListConverter.cs:19-49). That is what happened in the live 'vamn' case.
- Player.GetPlayer(string) is a case-insensitive exact match among online players (G\Player.cs:921-923).
- Offline players: TavernLib's account table, keyed by lowercase name, which is users.json (TL PlayerCommandModule.cs:628-640).
- A name is a reliable identity here: to join, the username, the id and a per-user token must all match users.json (TL\TavernLib.Patches\PlayerJoinFilter.cs:79-103). Ids look like 2000000001 (SLOG:17186).
- If the last parameter is a string, it takes the rest of the line, so names with spaces need no quotes (Command.cs:155-160,196-205). Quotes also work (CommandService.cs:163-187).
- Whatever string the command returns is the reply shown in the console (Command.cs:81-84; WebSocketCommandHandler.cs:295-299). The game's own modules do this, e.g. G\FestivityCommandModule.cs:18.

THE GAP A COMMAND-ONLY GRANT CREATES
- The shooter's own client has no way to learn it holds Skyfall:
  - It never receives its own relay (QR\Claims.cs:184-186).
  - An archer who owns no orbs sends no statuses (feasibility-scout.md:10,248).
- There is an existing server-to-one-player message:
  - PlayerCommunicationManager.SendMessageToPlayer sends EntityMessageType.PlayerMessage, which the client shows with PlayerController.MessageDisplay.Display (G\PlayerCommunicationManager.cs:24,27-41,52-65; G\PlayerMessageDisplay.cs:51).
  - TavernLib's `player message` command uses it (TL PlayerCommandModule.cs:685-694).
  - It silently drops a player who has no PlayerController yet (:58-61).
- A silent alternative is TavernLib's roles message, type 32 (TL\TavernLib.Patches\ServerSettingsRolePatch.cs:21-38). But SendRoles is internal, and TavernLib rewrites the roles at every join (:13-19), so an injected role would keep being overwritten.

## Risks
- Delegation is all-or-nothing. Anyone besides the owner who should give Skyfall must hold the console token, and the token gives full control of the server: save wipes, killing players, spawning, settings. There are no per-command or per-person permissions: every caller is "(Server:0)", and the "admin" role in users.json means nothing to the console.
- Port 1760 listens on all network interfaces (WebSocketRemoteConsole.cs:50). The HMAC secret is the only lock, so a leaked console_token.txt gives total control.
- There is no way to type a command from inside VR. In a headset the owner needs a desktop overlay, or should grant before or after the session.
- The game's player parameter types fail badly. PlayerList silently does nothing on a typo (the live 'vamn' case), and Player fails with a bare "value was null". Take a string and look the player up yourself.
- A top-level module with an existing name is replaced silently (CommandCollection.cs:85-86). "skyfall" is free today: the game's modules run from agents to world, TavernLib has player, chunks and document, and TavernVoice has tavernvoice.
- Telling the client through message text is a hidden channel:
  - The exact text must ship in the same DLL on both sides.
  - Anyone with the console token can forge it with `player message`. That is harmless, because the server still decides all damage.
  - It must be sent again on every connection.
  - SendMessageToPlayer silently drops a player who has no PlayerController yet.
- Commands run inside a server frame on the main thread. Keep them proportional to the number of players, with no scene-wide searches.
- A restart, or a crash, drops every grant and the festival flag without a word. That is intended, so the status reply should say so.
- If someone later hard-references an optional DLL, Collect would throw and lose every command. ProcessModule plus the try/catch avoids that.

## Not verified
- Remote Console and TavernKeeper from another PC: I could not check whether port 1760 is reachable through the firewall or router, and no non-local connection appears in any of the 10 logs.
- Launcher behaviour comes only from extracted strings (PyInstaller bundles, Python 3.14). I did not run them, and there are no line numbers.
- QuickshotRing's `skyfall` module is not built or tested; this was a read-only task. TavernVoice's module registered without a warning, but nobody ever typed a tavernvoice command, so that is not proven end to end either.
- I don't know how the launcher console displays an error result (an exception wrapped in TargetInvocationException).
- PlayerMessage is untested on this server: `player message` was never used in the logs, PlayerCommunicationManager.Instance was never seen, and the client prefix on PlayerMessageDisplay.Display is untried.
- Not known for this server: the value of IsSimpleServer and whether DevOnly passes. That group-permission value 8 means Console is inferred from the log text.
- The server's real launch arguments are unknown, because MelonLoader's "Command-Line:" line is blank (SLOG:10). That /console is absent is inferred from the missing "Console Started" lines.
- Whether TavernEssentials will go back onto the server is unknown. It was removed between the 16:56 and 17:07 runs on 2026-11-18. Nothing in it changes this answer.
- I did not check the client logs for cancelled RouteCommand attempts.
- Security handling:
  - In %APPDATA%\TheModdingTavern I listed file names only (console_token.txt and server_secret.key exist) and opened neither, nor users.json, nor anything under %APPDATA%\A Township Tale.
  - Every log grep excluded lines containing listing, token or bearer.
  - Scratch files are in SP\cmdscout (scripts), SP\launcher_strings, SP\tavernessentials and SP\tavernvoice.
