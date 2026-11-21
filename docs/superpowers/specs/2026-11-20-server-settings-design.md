# Server-set settings (design)

Approved by the owner on 2026-11-20 ("Build the server set settings this way"), scope "Everything".

## Intent

The owner will tune balance on a public server while people play. Today every game reads its own settings file, so (1) a tuned
server and its players disagree until each player edits a file by hand, and (2) a player can edit their own file for an edge: the
shooter's own game launches its arrow with its own file's snap speed (`Steady.cs`). The owner: "Have all the settings be set by the
server, and a player can't change their own config on a player side."

## Goals

- While a game is on a server, the server's settings are the only ones that count, for every setting of the mod (play and looks).
- A player's own file is ignored while connected, including edits made mid-game, and is never overwritten.
- A change on the server reaches every connected game within a few seconds.
- When a player leaves, their own settings come back.
- Small on the wire: only what the server changed from the mod's built-in values is sent.

## Non-goals

- Stopping a modified copy of the mod. That needs the server to stop trusting the game (arrow speed first) and is separate work.
- The play-test kit's settings: the kit is not distributed and keeps its own file on each machine.
- Custom orb icon pictures (`UserData\BluesArcheryOverhaul\Icons`): files, not settings; they stay per player.

## Anti-goals

- A player's settings file changed or lost by joining a server.
- A fault in this feature refusing or kicking a player who would otherwise get in (as the join gate: only a decided refusal refuses).
- Any new work per shot or per frame beyond one flag test.

## Approaches

1. **The server sends its values; the game holds them in memory while connected (chosen).** The file is untouched and comes back
   on leaving.
2. **The server overwrites the player's file.** Simple, but it destroys the player's settings for other servers and offline play.
3. **Refuse any game whose file differs.** Sends nothing, but every player would have to copy the owner's file by hand at every
   tuning step.

## The design

- **Which settings:** every entry of the mod's settings file except the per-machine sections `Log`, `Timing`, `Telemetry`, `Join`
  and `Settings` (the file's own version entry).
- **What is sent:** the entries whose value on the server differs from the mod's built-in default, each as "Section.Name" and its
  value as text, plus a check number over all server-set entries. The game applies them over the built-in defaults (not over its
  own file) and answers with the check number it now has.
- **When:** in the join gate's own step, after the game passed the check and before the join completes; and again to every
  connected game whenever the server's settings are reloaded. Not sent with `[Join] Check = off` (that switch means the server says
  nothing to joining games).
- **On the wire:** the join gate's message (number 27) gains two kinds beside Here and Hello: Settings (server to game, in parts)
  and SettingsAck (game to server). This extends the join gate's owner-approved network exception; the owner approved it with this
  design. The wire number goes up, so everyone needs the new file.
- **If a game cannot match** (its check number differs: a build with other built-in values): the existing
  `[Join] SettingsMismatch` decides, `log` (default) or `refuse` with a reason the player can read.
- **While held:** an edit of the player's own file is remembered as theirs for later, and the server's values are put back at once.
- **Leaving or quitting:** the player's own values are put back before anything can save them.
- **Orb on/off switches** (`[Tree]`, read when the tree is first built in a game run): they follow the server when the settings
  arrive before that first build. If the tree was already built with other switches, the game says so and the answer carries a
  "restart needed" flag, treated as a mismatch.
- **What the player sees:** nothing on screen; one log line when the server's settings take over and one when their own return.

## Left to the builder

How the server reaches every connected game after the join, the largest safe size of one message part, where the game learns it
has left the server, and which entries are only read at start.
