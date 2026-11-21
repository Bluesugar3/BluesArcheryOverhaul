# QuickshotRing on Quest, and the join gate

Date: 2026-11-20. Status: draft for the owner's approval (server question settled).

## Why

The archery mod now replaces the whole archery skill tree and adds skills. The
owner wants Quest players on the server to have all of it, and wants the mod
to be all but mandatory: the server and every player run it, and a player
without it is turned away at the door with a reason they can read.

## What was found (2026-11-20, Modding Tavern Discord, GitHub, the game's code)

- **Quest servers are PC servers.** Shadow (maker of the Quest build): the
  server "still runs on the pc game files"; "Porting would only be needed for
  a quest client side mod". The server keeps today's `QuickshotRing.dll`.
- **Quest players run Modding Tavern's patched APK** (IL2CPP, MelonLoader
  0.6.5 Open-Beta for Android, game `main-1.7.2.1.42203`: the same game build
  as our PC client and server). Mods are DLLs in
  `Android\data\com.Alta.ATownshipTale\files\MelonLoader\com.Alta.ATownshipTale\Mods`,
  copied by hand or installed from the in-game Download Board that Shadow
  curates.
- **PC and Quest mods are two DLLs.** `outdatedc/Outs-CustomQuickAccessBubbles`
  ships a net472-style PC DLL and a net8.0 Quest DLL built against the APK's
  `assets\MelonLoader\Il2CppAssemblies` and `assets\MelonLoader\net8`. Quest
  code differs by: `Il2Cpp`-prefixed game namespaces, `[MelonGame("Alta",
  "Att_Quest")]`, `ClassInjector.RegisterTypeInIl2Cpp` for the mod's own
  classes the game must see, `.Cast<T>()` for interfaces. Harmony patches read
  the same. One Unity method stripped from the Quest build broke that mod's
  saving: expect a few such.
- **The standard join check is ArmorRestored's** (`TheeFizzjay/ArmorPC`,
  decompiled and read, not run; we write our own, we do not copy its code):
  the server adds a step to the game's own join sequence
  (`ServerHandler.AddConnectionStep`), greets the joining game on a message
  number of its own, waits 5 s for the mod's answer, and on no answer or a
  mismatch sends the game's own join refusal (`MessageType.RequestJoinResponse`,
  a `ConfirmJoinMessage` with `isAllowed` false and an error text) and fails
  the step. A setting chooses `refuse | log | off`.
- **SyncLib is not ported to IL2CPP.** QuickshotRing does not use it (checked:
  no call, no reference in the built DLL), so that does not block us.
- **Size of the mod today:** 76 files, about 38,400 lines. The 15 Unity-free
  files (about 9,900 lines) need no change. The rest holds 95 Harmony patches,
  about 120 reflection/`Traverse` uses, 22 `MonoBehaviour` classes and about
  170 `+=` subscriptions.

## Goals

1. A join gate on the server: no matching QuickshotRing, no entry, with the
   reason shown to the player. PC and Quest alike.
2. A Quest client DLL that plays every skill the PC client plays, by the same
   rules, on the same server.
3. One source of truth: rules, shared settings and wire formats cannot differ
   between the PC and Quest builds.

## Non-goals

- A Quest-hosted or IL2CPP server. The server stays the PC build.
- One DLL for both platforms. Not possible (Mono vs IL2CPP).
- SyncLib on Quest, Pico support, festival mode.
- Every look at full PC strength on Quest. Looks may be lighter there.
- Sending the server's shared settings to clients at join (see "Shared
  settings" below: likely the next piece of work, not this one).

## Anti-goals (a failure even if it "works")

- Any change in how the PC client or the server plays. `#if QUEST` only ever
  adds the Quest path; the PC path compiles to what it is today.
- A Quest build that silently lacks a gameplay piece (a shot that behaves
  differently from the PC one). Arrow physics is client-first, so a missing
  piece is a desync, not a missing effect.
- A gate that locks everyone out by accident. It is proven in `log` mode
  before `refuse` is switched on.
- Redistributing game files: the APK's assemblies are private build inputs,
  like the ripped Unity project.

## Three pieces, in this order

### Piece 1: the join gate (PC and server; needs no Quest)

**What the player sees.** Joining without the mod, with too old or too new a
build: the join fails with one of
- "This server needs the QuickshotRing archery mod (X). Install it, then join
  again."
- "Your QuickshotRing X is too old for this server's Y. Update it, then join
  again."
- "Your QuickshotRing X is newer than this server's Y. The server needs an
  update first."

**How.**
- Server, once, when the `ServerHandler` exists and before any player is in:
  `ServerHandler.Current.AddConnectionStep(step)`. (The game logs an error if a
  step is added after players joined.) Each new connection gets the mod's
  handler: a postfix on `ServerHandler.ConnectionCreated` calls
  `connection.SetHandler(<our message type>, ServerRead)`.
- Client: a postfix on `JoinServerPipeline.ConnectionApprovedByServer` sets
  `connection.SetHandler(<our message type>, ClientRead)`.
- The step: the server sends **Here** (tag, mod version text, wire number).
  The client answers **Hello** (tag, wire number, mod version text, the full
  32-bit shared-settings checksum `Cfg.SharedChecksum`, platform PC or Quest).
  The server waits up to `[Join] AnswerSeconds` (5) in 100 ms steps, then
  decides.
- Refusing (checked against the game's code by the design's reviewer): the
  step runs after the server's "allowed, doing prerequisites" reply, and the
  joining game keeps the FIRST refusal it gets, so ours goes out before the
  step throws (a throw makes the game send its own "Failed join
  prerequisites."). Ours is `RequestJoinResponse` written by hand in
  `ConfirmJoinMessage`'s layout (that type is internal): bool false, bool
  false, the join-result integer, the error text as an ASCII string. The
  integer is a value the game does not define, so the joining game does not
  retry and shows the text (a defined value shows a canned line and drops
  it; the game's own 33 retries 24 times). The progress callback is not used
  for the reason: the joining game shows a step's status only after it stood
  2 s.
- **What an unmodded game shows** is therefore the game's wrapper round our
  text: "Unknown connection error N, please contact support. Error: This
  server needs the QuickshotRing archery mod (X). Install it, then join
  again." That wrapper is the game's (`JoinResult.ErrorMessage`) and cannot be
  changed on a game that does not run the mod. A game that runs the mod (too
  old, too new) also logs the plain reason.
- **Wire number**: one integer constant in `Wire.cs` (Unity-free), raised
  whenever a build is one that "friends need" (the build block, the shot
  claim, the set of `[shared]` settings, or client-side behaviour that must
  match changes). It is the same judgement `tools/stage-build.sh` already
  asks for with `friends: yes|no`. The mod version text is only for the
  message.
- **The decision is a Unity-free rule** (`JoinRule` beside the wire number):
  facts in (answered or not, both wire numbers, both checksums, the setting),
  verdict and reason text out. Unit-tested like the other rules.
- **Settings** (server, not shared): `[Join] Check = refuse | log | off`
  (default `refuse`), `[Join] AnswerSeconds = 5`, `[Join] SettingsMismatch =
  log | refuse` (default `log`). With `off` the server sends nothing.
- **Logging:** one Summary line per join: `[Archery] Join: <name> passed
  (client X, wire N, Quest|PC, settings same|DIFFERENT)` or `... refused:
  <reason>` / `... would be refused (log only): <reason>`. The client logs
  the server's Here once.
- A timing section on each new patch and on the step's decision, as every
  patch has.

**This is new network traffic**: two small messages per join, none during
play. It is the second written exception to "No new network traffic" (after
Skyfall's signal) and goes into CLAUDE.md as such, with what, when and why.

**Message number.** The game writes a message type in 5 bits
(`OutgoingPacketManager.StartSerialize`, `MessageProcessor`), so only 0-31
exist. Not in the game's enum: 3, 5, 8, 14, 16, 18, 19, 27, 31; 18 is
SyncLib's. The builder picks one of the rest as a named constant after
checking that no mod on the server or bundled in the Quest APK uses it
(TavernLib and Modding Tavern's Quest mods first). ArmorRestored's decompiled
code names 137, which does not fit 5 bits (it would arrive as 9,
`RequestJoin`): how it works on the wire is not understood, so its pattern
is followed and its numbers are not.

**Old builds.** A 0.18.3 client has no gate code, so it never answers and is
refused with "needs the QuickshotRing archery mod (X)". That is intended.

**A game without the mod receiving Here** logs the game's own "Unsupported
message type" error and carries on (`MessageProcessor.HandleMessage` returns
false). ArmorRestored does exactly this on live servers. The builder confirms
with an unmodded client in `log` mode that the join still completes.

**Shared settings.** A differing checksum is logged, not refused, by default
(owner can set `refuse`). Reason: the owner re-tunes `[shared]` numbers often,
and a refusal would lock every friend out after each tune until they copy the
cfg; on a headset, editing a cfg is painful. The real fix is the server
sending its shared values at join so clients use them. That is a separate
piece of work with its own design (it changes how every shared setting is
read); the gate's Hello/Here pair is where it would ride.

**Roll-out.** The shipped default is `refuse`, and builds install
themselves, so the first gate build's `post-install.sh` sets `Check = log` in
the owner's server cfg. After one play session whose Join lines read right,
the owner switches to `refuse`.

**Quest players before the Quest build exists** are refused like anyone
without the mod (designer's call, from "almost force people to have the
mod"). No Quest exemption is built.

### Piece 2: the Quest test (throwaway; one day once the headset is ready)

Needs from the owner: the Quest with Modding Tavern's APK installed and able
to join the local server; a way to copy files to and from the headset
(SideQuest or adb).

**The server the headset joins must run only mods a Quest game can match.**
Today's server also runs `SyncLib.dll`, `RevampedSkills.dll` and
`attbullshit2.dll`. SyncLib has no Quest build; a RevampedSkills mismatch is
known to break the shrine and it patches archery. The owner chose a cut-down
server: see "Decided by the owner" at the end.

- Build inputs: the APK's `assets\MelonLoader\Il2CppAssemblies` and
  `assets\MelonLoader\net8` folders, copied to `C:\ATTUnity\QuestRefs` (off
  OneDrive; private reference, never shared). `questdlls.zip` from the
  Discord is the fallback if they cannot be pulled from the APK.
- A tiny mod `QuestSpike` (net8.0, `[MelonGame("Alta", "Att_Quest")]`) that
  does one of each thing the port depends on:
  1. a Harmony postfix on `BowString.HeldUpdate` that logs once a second;
  2. answers the join gate's Here with a Hello (the IL2CPP form of
     `SetHandler` and `Send` with the game's delegate types);
  3. reads one private field of a game object and subscribes to one game event;
  4. adds one `MonoBehaviour` of its own with one `LineRenderer` on the bow;
  5. reads and writes one MelonPreferences file under `UserData`.
- Pass: all five show in the headset's MelonLoader log and on the bow, and the
  headset joins the owner's server with the gate on `refuse`.
- The spike also answers: how the headset reaches a TavernLauncher server on
  the owner's PC, where logs and `UserData` live on the headset, and how long
  a copy-test round trip takes.
- It is throwaway code. What it teaches goes into Piece 3's plan; anything
  that fails is brought back to the owner before Piece 3 starts.

### Piece 3: the Quest client

**Approach chosen: one codebase, two projects.** (See Approaches.)

- New project `QuickshotRing.Quest\QuickshotRing.Quest.csproj`: net8.0,
  defines `QUEST`, compiles the files of `..\QuickshotRing\` by link,
  references `QuestRefs`. Output `QuickshotRing.Quest.dll`. The PC project is
  unchanged. `Directory.Build.props` gets a condition so its net472 settings
  and PC references do not apply to Quest projects.
- **Left out of the Quest compile** (server-only or PC-desktop-only):
  `Telemetry.cs`, `TelemetryRules.cs`, `TestRange*.cs`, `TestTools.cs`,
  `SkyfallServer.cs`, `SkyfallCommands.cs`, `PlayerCare.cs`, `RangePhoto.cs`,
  and `TestBuild`'s desktop menu opener. Server branches inside shared files
  stay (they never run on a headset) unless they do not compile there, in
  which case they go under `#if !QUEST`.
- **One small platform layer** (new file(s), the only place the two builds
  differ on purpose besides `#if QUEST` lines):
  - the game's namespaces as `global using` lists, one per platform
    (`Alta.X` vs `Il2CppAlta.X`, global-namespace game types vs `Il2Cpp.X`);
  - private members: one helper per access pattern (`Traverse` on PC, the
    interop's generated members on Quest);
  - the mod's own `MonoBehaviour`s and any class handed to the game:
    registered with the interop on Quest;
  - event subscriptions and delegates handed to the game: converted on Quest;
  - the game's own collections and arrays: helpers where the Il2Cpp types
    differ from the .NET ones.
  The builder decides the helpers' exact shape from what the spike showed.
- **`[assembly: MelonInfo]`** keeps the same name, author and version on both;
  `MelonGame` differs by `#if`. Both DLLs always ship together with the same
  version and wire number.
- **Order of work (each step verified on the headset before the next):**
  1. *Gameplay parity.* Settings and self-check; the tree; builds and claims
     on the wire; the draw clock and release; flight and physics that the
     shooter's game owns (Steady's speed, Quickdraw 4's line, the volley and
     its virtual arrows, Seeking Volley's lock and held arrow, Holy Arrow's
     client part, Auto Dock's client part, Skyfall's ask and held arrow).
     Pass: a Quest archer and a PC archer on the server each fire every skill
     and the server's `Release:` lines and damage match for both.
  2. *Looks, one family at a time* (ring, Fill, ghosts, Burst, Ricochet,
     Shatter, homing looks, Skyfall), each read on the headset with the
     mod's own `[Timing]` report. Looks settings are already per client, so
     Quest gets its own lighter defaults where the report says so (counts,
     pool sizes, lights). A look that cannot run well on Quest ships switched
     off there by default, and that is said in the release notes.
  3. *The shrine view*: tablet, tree rings, tutorial text.
- **Asset names.** Looks find game effects, sounds and materials by name; the
  Quest build may lack or rename some. Every look already fails soft with one
  log line; the port keeps that and lists what was missing.
- **A second archer is needed** for step 1's pass and for everything a
  watcher sees: one account cannot be on the server twice, so the owner on
  the headset needs a friend (or a second account) on PC, and the reverse.
- **Checks.** The 2,586 unit tests keep covering the shared rule files. From
  the end of step 1, `tools/stage-build.sh` also builds the Quest project, so
  a PC change that breaks the Quest compile fails staging. Quest behaviour is
  checked by a written headset checklist per step (in game terms, as the
  owner's play-test steps are).
- **Getting it to players.** Each release carries both DLLs. The owner asks
  Shadow to list the Quest DLL on the Download Board; until then it is copied
  by hand to the headset's `Mods` folder. The README gains a Quest section.

## Approaches considered

- **A. One codebase, two projects (chosen).** Same source files compile into
  both DLLs; differences live in one platform layer and `#if QUEST`. Rules,
  shared settings and wire formats cannot drift. Cost: a one-time pass over
  the game-facing files.
- **B. A separate, hand-copied Quest source** (what the Bubbles mod does).
  Quickest start; every later change must be made twice and one miss is a
  desync. Ruled out for a mod this size that changes weekly.
- **C. A thin Quest client** (tree, wire, physics; no looks). Smallest port,
  but Quest players would see nothing of what they shoot. Kept as step 1 of
  A, not as the product.

The owner handed the "how" over ("go with the standard"); A in C's order is
the designer's call, and the gate follows ArmorRestored's pattern at the
owner's request.

## Risks

- **Harmony on IL2CPP.** Methods the compiler inlined cannot be patched, and
  patches with `ref`/`in` struct arguments or value-type `__result` need care.
  The 95 patches are checked one by one in step 1; one that cannot be patched
  is brought to the owner with the alternative.
- **Stripped Unity methods** on Quest fail at run time, not at build. Found
  only on the headset.
- **"Blind" Quest code.** The interop assemblies have names only. Our PC
  decompile is the same game build, so logic should match, but Quest-only
  branches (`PlatformProvider.IsAndroid()`) exist.
- **Loader gap.** PC runs MelonLoader 0.7.3, Quest 0.6.5 Open-Beta: small API
  differences in the loader itself.
- **The Quest game can update apart from PC** (Modding Tavern's APK builds),
  breaking the Quest DLL alone.
- **Performance.** A phone-class GPU; Skyfall and the homing looks are the
  heaviest. Handled per look in step 2.
- **Test loop is slow.** Every Quest check needs the headset on the owner's
  head; no automated run exists.
- **Two builds forever.** Every later feature is finished only when it is
  checked on both.

## Left to the builder

The message number; the exact byte layout of Here and Hello; where the server
installs the join step; the platform layer's shape; which shared-file server
branches need `#if !QUEST`; the Quest defaults of each look; the headset
checklists; how `stage-build.sh` and the installer treat the Quest DLL (it is
never installed into the PC game or the server).

## Decided by the owner (2026-11-20): a cut-down server

The server Quest players join runs QuickshotRing plus only mods that have a
Quest build or are server-only. `RevampedSkills.dll` and `SyncLib.dll` come
off it (and with SyncLib, any mod that registers prefabs through it).
`attbullshit2.dll` stays only if it turns out to be server-only or to have a
Quest build; Piece 2's plan starts by checking it. Before the first mod is
removed the world save is backed up: items a removed mod added are orphaned.
Considered and not chosen: the server as it is (SyncLib has no Quest build),
and two servers (twice the upkeep). Piece 1 does not depend on this.

## CLAUDE.md, when each piece lands

Piece 1: the second network exception, the `[Join]` settings, the wire-number
rule. Piece 3: the Quest project, the platform layer, "every new patch, look
and private-member access goes through the platform layer and is checked on
both builds", the build inputs' location and that they are private.
