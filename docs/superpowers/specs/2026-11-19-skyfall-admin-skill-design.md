# Skyfall: the admin sky-rain skill (design)

Written 2026-11-19 against QuickshotRing 0.17.15 as installed. This is a design only: no code was changed or run for it.

**Status: approved by the owner on 2026-11-19 ("Approved on message and plan").** That includes the one invisible message exception to the no-new-traffic rule (Part B, "Telling the caster").

## What it is

An over-the-top extra skill for the owner's server of friends. You hold a full draw at the sky and rings climb your arrow. A sigil of the game's own glyphs forms in the sky where you point. On release, every landing spot burns into the ground inside the game's danger dome. Giant arrows rain into the domes in waves and explode, and the finale is one cliff-tall real arrow driven through the sigil into the ground, where it stands like a monument.

It is a toy. No balancing.

**How decisions are marked:**
- "(owner)": the owner's decision, with its date.
- "(my call)": Claude's decision. Every one can be overruled.

Unmarked facts were checked in the code or the decompiled game. The evidence, with file:line, is in `docs/superpowers/specs/skyfall-research/`:
- `feasibility-scout.md`
- `commands.md`
- `telling-the-caster.md`
- `shot-seam-and-server.md`
- `runtime-looks.md`

**The owner's words**, 2026-11-18:
- "lets make a admin skill ... something way over the top for admins and player to have fun playing with. no need to ballenceing, i want you to aim up into the sky and it do crazy over the top gliffs and magic rings, then up in the sky i want to rain massive arrows and explode on empact."
- "this admin skill is only ment to be get via commands and dont change any skilltree or normal game play. this is just a extra skill"

**What the owner chose:**
- The combo, 2026-11-19: Skyfall Sigil as the core, Marked Ground's danger domes, the World-Nail as the finale, and a festival switch by command.
- "Where I point" for where the storm lands.
- "Yes, inside" for building it inside QuickshotRing.

**Concept pictures** (private; they use the game's textures), in `QuickshotRing\Art\drafts\admin_skill\`:
- `storyboard.png` and `skyfall-sequence.mp4` (the hero);
- `variant-world-nail*.png` (the finale);
- `variant-marked-ground*.png` (the domes);
- `variant-lantern-rain*.png` (festival).

**The look rules:** `QuickshotRing\Art\admin_skill\game-native-kit.md`, plus the "Standard swaps" S1-S18 in `idea-check.md` and `picture-check.md`, all in that folder.

---

## Part A. In plain words

### Goals
1. **The sequence.** The owner's own skill, start to finish: aim at the sky, rings and glyphs, a rain of giant arrows exploding on impact, then the World-Nail. (owner)
2. **Given and taken only by command; festival mode switched by command.** (owner)
3. **Everyone nearby sees the same storm**, worked out on each machine from the shot the game already sends. (owner, 2026-11-19: built inside QuickshotRing)
4. **It looks like A Township Tale**, built from the game's own glyphs, ring sprites, arrows, explosions, danger dome and sounds. (owner's standing art rule)
5. **It is fun to watch and to dodge.** The domes give everyone a beat to run. (owner, the combo)

### Non-goals
- **No shrine orb and no skill-tree change.** No change to any shot by a player who does not hold Skyfall, and no change to a holder's shots that are not aimed at the sky. (owner)
- **No balancing.** Damage numbers are simply large. (owner)
- **Almost nothing saved.** Grants are kept in a small file on the server, so a restart does not wipe them; every install restarts the server, and typing the gives again each time would be a chore. Festival mode always starts off. Nothing else is saved. (my call, after the builder check)
- **No harm to players outside the domes:** no damage and no shove anywhere else. Inside a dome when it lands is a different matter; see "Caught in a dome".
- **No time-of-day change, no weather, no fog.** The sky is only dimmed on nearby machines, as the game does in caves. (game-feel check)
- **No commands from inside VR.** The game cannot route them on this server (see Part B).
- **No new Unity assets or bundles.** Every look is built in code from the game's own pieces, as the mod already does.

### Anti-goals (it fails if any of these happen, even if it works)
- Normal archery changes for anyone.
- A player dies anywhere except inside a dome that warned them first, or the caster dies to their own storm.
- VR discomfort: camera shake, strobing (more than 3 bright flashes a second), or a frame stall when it fires.
- It looks pasted on: purple or violet, anime circles, hexagrams, thin perfect vector rings, photoreal fire.
- The sky stays dark, or someone holds Skyfall without the owner being able to see it (`skyfall` lists every grant) or remove it (`skyfall take` works for offline players too).
- The server freezes or floods: unbounded hits, effects or loot.

### How it plays, moment by moment

1. **Getting it** (owner: commands only). The owner types into the TavernLauncher Server window's **Console**. A remote admin can use the Client launcher's Remote Console or TavernKeeper with the owner's console token.
   - `skyfall`: who holds Skyfall, and whether festival mode is on.
   - `skyfall give <player>`, and `skyfall give all` for everyone online now (my call).
   - `skyfall take <player>`.
   - `skyfall festival on|off`.

   The grantee's game is told quietly: nothing shows on anyone's screen. See Part B "Telling the caster".
2. **The trigger.** All three must hold:
   - you hold Skyfall;
   - the string is at full draw (strength at least `[Skyfall] MinStrength` 0.9; my call);
   - you are aimed at least `MinElevationDegrees` 25 above level (my call).

   Hold it there for `ArmSeconds` 1.0 (my call: an armed thump in the bow hand), so that a casual upward shot never casts by accident. A release before then, or below the angle, is a completely normal shot.

   **Who decides the arm** (my call, after the builder check):
   - The caster's own game decides "armed": it held a full draw at 25 degrees or more for `ArmSeconds`.
   - It asks for the cast in its own shot claim.
   - The server accepts the request only when all of these hold:
     - the player holds Skyfall;
     - the release pose is at 25 degrees or more;
     - the claim's time since the 68% crossing is at least `ArmSeconds`, and the time the existing `[Claim] TrustMs` rule settles on is at least `ArmSeconds` too. That is plan ruling 1: the stricter "the two times are equal" would fizzle every hold over 8.19 s, and every cast whose client clock runs behind.
   - This keeps the caster and the server agreeing except in rare races. The server's own copy of a draw arrives 60-380 ms late, so timing the hold there would disagree near the threshold.
   - A request the server refuses (the grant was taken during the draw, or the checks fail) **fizzles**: the arrow is used up, the sigil puffs out for everyone, and nothing falls. The caster's game drops its storm when the "taken" message reaches it.
3. **The hold** (only you see it; nothing tells other players about a hold).
   - Three glyph rings flare round your arrow and climb its line.
   - A faint sigil forms where your aim meets the sky. That point is on a plane `SigilHeight` 30 m above you, and its distance out along the ground is held to `MinDistance` 15 m to `MaxDistance` 60 m: aim steeper and it is closer, flatter and it is farther. (owner: "where I point")
   - A thin target glyph shows on the ground under it.
   - The sky dims gently, and the game's 'Shrine Activate Sounds' swells.
4. **The release** (everyone near sees it from here).
   - Your arrow streaks up into the sigil point as a ghost; the real arrow is held hidden and used up (my call).
   - The sigil unfolds core-first and its bands spin up. Its twin burns into the ground.
   - Every landing spot burns in inside the game's orange dynamite danger dome.
   - This build-up takes `BuildSeconds` 1.75 (my call) before the first arrow: everyone's "run!" moment.
5. **The rain.**
   - `Waves` 3 of `ArrowsPerWave` 8 giant arrows (my call; settings), `WaveGapSeconds` 1.0 apart.
   - Each is a game arrow about 5 m long, in the Etheral Arrow's flat see-through look recoloured skill-gold, with the Lightbeam trail. Each falls into its own dome.
   - Each impact plays the game's 'Explosion', which plays 'Huge Explosion' itself, so it is never doubled. Burning claw scorches are left in the fire ramp.
   - At most 12 explosions alive at once, and impacts at least 1.5 m apart (the game's effect merges closer ones).
   - Creatures in a blast take heavy damage and are knocked down.
6. **The finale, the World-Nail.**
   - One copy of the game's real Arrow (bark shaft, blue and white feathers, the fancy spike in gold), about 38x size (~46 m). It is thickened along its own axes so it is not a hairline: shaft x2.9, head x2.1, vanes x2.3, as in the picture.
   - It slides point-first down through seven ring bands in `NailSlideSeconds` 2.5, into the sigil's centre.
   - The impact: the biggest blast, the bands rolling out over the ground as rings, flint debris, and creatures within `NailBlastRadius` knocked down.
   - The sigil shatters into falling ruins runes.
   - The Nail stands `NailStandSeconds` 6 (20 until 0.18.1: the owner's play-test found it lingered too long), then crumbles into the game's 'Large Smoke Puff' and embers.
7. **Afterwards.** The sky eases back over about 2 s. Nothing is left in the world except loot from creatures that died (see Part E).
8. **Festival mode** (owner: switched by command).
   - The same show, but impacts are the game's 'Firework Explosion' (gold and cyan), 'Easter Egg Confetti Big', and 'Healing Explosion' near players.
   - Players near an impact are healed and fed (my call).
   - Creatures are knocked over but take no damage.
   - The Nail bursts into the largest firework.
9. **Caught in a dome** (owner, 2026-11-19: "Instantly kill them").
   - A player inside a dome when its arrow lands dies instantly, by the game's normal death: their body and items stay where they fell, as with any death.
   - The World-Nail's big dome counts too (my call).
   - Never the caster (my call).
   - The server's PvP setting does not change this, because it is an admin skill (my call).
   - Every dome shows for at least `DomeWarningSeconds` 1.5 before its arrow lands (my call). Later waves get longer warnings, never shorter.
   - Festival mode never kills. A caught player sees the game's 'Larger TextPopup' with "OUT!" in front of their face instead, only on their own screen (my call).
10. **Limits** (my calls, after the builder check):
    - One storm per caster at a time: a sky shot while yours still runs is a normal shot. The caster's game knows this too, so they agree.
    - There is no server-wide cap on storms. A cap the caster cannot see would make the two disagree.
    - Load is bounded instead in two places. On the server, rain impacts are queued with a hard cap, and a drop is logged once. On each client, at most 12 explosions are alive at once; beyond that, an impact shows only its spark and sound.

### Who is hurt by what
- **Creatures:** everything. Damage and knock-downs come from the rain and the Nail, normal mode only.
- **Players:**
  - in normal mode, killed instantly when caught inside a dome as it lands (owner), never the caster;
  - nothing anywhere else: no damage and no shove;
  - in festival mode, only "OUT!", plus heal and feed.
- **Loose items:** no explosion force on anything, so no flung gear.

---

## Part B. How it works (the parts already decided)

### Where the code lives (owner, 2026-11-19: "inside, walled off")
- **Its own files in QuickshotRing.** The Unity-free rules go in a tested file, the fourteenth the tests compile:
  - the trigger rule;
  - the sigil point and its clamp;
  - the landing pattern and its spacing rule;
  - wave timing;
  - the claim's Skyfall code;
  - the grant table;
  - the message's tag format.

  The game-facing parts go in their own files: commands, the server's storm and damage queue, the client's looks, the sky dial.
- **It touches the shot code in one place.** `Draws.Release` decides "Skyfall" first, on every machine. For a Skyfall release it returns the neutral resolution, so nothing else in the mod treats it as special. `Volley.Fire` then branches once to start the storm instead of the volley.
- **Small guards so nothing else fires on that release:**
  - Holy Arrow's server arming and its arrow sacrifice (a prefix on `LightBeamSkill.DestroyShotArrow`);
  - `Quickdraw4.ClientShot`;
  - `Claims.PutResolved`;
  - any held looks and extra arrows dropped as `Volley.Fire` already does.

  `shot-seam-and-server.md` lists every one with file:line. A shot that is not a Skyfall release must reach exactly the code it reaches today.

### Commands (`commands.md`)
- **The only working way in** is the game's own WebSocket console (port 1760). TavernLib secures it, and the owner already uses it from the TavernLauncher Server window's Console, with commands like "player teleport" and "spawn".
- **Dead routes:**
  - the server window takes no input;
  - TavernLib closes the TCP console;
  - TavernLib cancels in-game command routing, so nothing works from VR.
- **Registration:**
  - A `[PlayOnly, ServerOnly, Module("skyfall", ...)]` class is registered in `OnInitializeMelon` with `CommandService.CommandCollection.ProcessModule(type, assembly)`, inside a try/catch. TavernVoice already does the same on this server.
  - Names are lowercase. Players are looked up by name among the online players, and a typo is refused with the list of who is online.
  - Every reply is a string, never a thrown error.
  - Commands run in a server frame, so they only set state; sends happen from `Main.OnUpdate`.
- **Grants:** a map from account to name.
  - It is saved to a small file on the server (`UserData\QuickshotRing\skyfall-grants-v2.txt` since 0.18.1; 0.18.0 and earlier used `skyfall-grants.txt`), loaded at start, and rewritten on every give or take.
  - A build that changes what a grant does must change the file name, so a rollback to an older build never honours a newer build's grants (final review I-1: 0.17.16/0.17.17 hold the kills but none of the looks). 0.18.1 carries a `skyfall-grants.txt` over once and moves it aside (`.retired`). A file that cannot be read is never taken for "nobody" and never written over.
  - `skyfall give` needs the player online, to learn their account. `skyfall take` also works by the saved name for an offline player.
  - Festival mode lives in memory and starts off. (my call)
- **Delegation is all-or-nothing:** the console token gives full control of the server. That is the existing state of this server, not something Skyfall adds.

### Telling the caster (`telling-the-caster.md`). An invisible signal; the owner's OK is pending
- **The problem.** The caster's own game must know it holds Skyfall, both to show the hold and to decide the release exactly as the server does. The shooter never receives its own claim relay, and an admin with no bow orbs sends no statuses at all. No message the game already sends reaches that player with this fact.
- **The owner's ruling** (2026-11-19): "No need to player messaged people". So nothing is ever shown to anyone.
- **The proposal (my call).** The server quietly tells only the holder's game, through the game's own player text message (`PlayerCommunicationManager.SendMessageToPlayer`):
  - it carries only a tag, `QSR_SKYFALL_V1|holds|festival|<shared settings check>`;
  - it is sent on give, take, festival on/off, and once per rejoin, queued until the player's controller exists;
  - on the client, a postfix on `PlayerTextMessage.Serialize` latches the state, and a prefix on `PlayerMessageDisplay.Display` swallows the message, so no popup ever appears. A client mod in this modpack, CustomIngots, already hides its own messages this way.

  It is one message per grant change, never per shot. It also gives admins with no orbs the shared-settings check they lack today. But it is the first message QuickshotRing would send that the game would not have sent, so the "no new network traffic" rule in CLAUDE.md gains one written exception.
- **Other players are not told.** They see the show from the release on.
- **Rejected alternatives:**
  - TavernLib roles: they persist in users.json, the file that holds login tokens, and depend on another mod's internals.
  - Reserved bits of the build block: those bits are "reserved for good", and the build block never reaches an admin with no orbs.
  - A flight-sync marker on the first cast: the caster's first storm would look wrong on their own screen.

### Spectators
- **The claim marks the release, both ways** (my call, after the builder check). Both directions use the unsteered window bits.
  - The caster's claim carries its request: 1 = "cast". `ClaimRules.ServerCheck` keeps a request only when the server's own checks agree (see "Who decides the arm").
  - The server relays its own decision: 0 none (as today), 1 Skyfall, 2 Skyfall in festival mode, 3 fizzled.
- **No version bump.** Every other claim stays bit for bit the same, and current readers ignore the field. A Skyfall release is always relayed unsteered, Kind Early, stage 0, with no targets.
- **Everything a spectator draws comes from:**
  - the arrow id;
  - the compressed release pose, which is bit-identical on every machine;
  - the [shared] settings.

  So no payload is needed. If a spectator has no colliders loaded where the storm lands, the server's ground height can ride in the claim's mark slot (up to 3 x 17 bits). Otherwise that spectator skips the ground looks and still draws the sky.
- **The pattern** (landing spots, arrow order, wave times) comes from its own named deterministic function, not `LookSeed`. LookSeed's contract is "looks only", and these spots decide damage.

### The real arrow
- On every machine it is held and hidden at release, after its meshes are copied for the ghost.
- The server destroys it about 1 s later: after the relay, and before any hold limit.
- Skyfall has its own hold, not Seek's, so it is not tied to `[SeekingVolley] MaxLifeSeconds`.
- One arrow is used up per cast (my call).

### The server: damage and festival
- **A due-time queue** of impacts (the `BounceQueue` pattern), ticked in `Main.OnUpdate` before the headless return, with a hard cap and one log line when it drops.
- **Each impact:**
  - `Targets.Overlap`, creatures only;
  - Holy Arrow's plain `ImpactHit` recipe with `BlastMath` falloff, so no XP and no PvP gate is involved;
  - one batched hit per creature per tick;
  - no explosion force;
  - areas that are not loaded are skipped.
- **Players caught in a dome** (normal mode only):
  - At each landing, the server checks every player's head position (`Player.PlayerController.Head`) against that dome, skipping the caster.
  - It kills a caught player with a lethal hit through the game's normal death, so the body and items stay as with any death.
  - The game's own spawn invincibility still applies.
  - A one-line Summary log names who was caught by whose storm.
- **Festival:**
  - the Heal and Feed cores move out of the test range into a small shared helper;
  - creatures get a cripple-only hit with no damage.
- **Sounds and pooled effects never run on the headless server.**

### The looks, on clients (`runtime-looks.md`, the game-native kit)
- **Rings:** quads with `SpriteSheet_Combat` cells (row 2 the hand-drawn ring, row 1 the spiky star-ring, row 3 the soft glow). The material is a copy of `Particles_Combat_White_Hard`, taken from a loaded effect. An alpha-blended copy is laid under the additive one so the rings read against a daylight sky.
- **Runes and glyphs:**
  - the ruins symbols and the meander band, found by name;
  - the skill glyph in the middle is `Glyph_Bow_LightBeam`;
  - anything not reliably loaded is embedded as a PNG with mip maps;
  - no hexagram.
- **Colours:** skill gold #ffce00 / #ffd872 / #fff285; ruins cyan #3adeff / #66f6ff; fire orange only for danger and explosions; never purple.
- **The danger dome:** the mesh and material of the dynamite prefab (hash 31326), read once from `PrefabManager`. They go on the mod's own object with a copied material. The game's object is never instantiated: its `ExplosionRadius` destroys itself without a parent.
- **The World-Nail:** copies of the real Arrow's four LOD0 renderers with their real materials, reusing the ghost-copy code, scaled along the arrow's own axes.
- **Distance:** the big sky art goes on layer 1 'TransparentFX', which has no cull distance, so spectators beyond 100 m still see the sigil and the Nail.
- **Effects:**
  - 'Explosion' and 'Shockwave Impact' through `Fx.GetScaled`, where they grow fully.
  - 'Level Up Effect', 'Healing Explosion' and the confetti stay their native size unless their parts are scaled one by one.
  - Explosions are spaced 1.5 m apart within 6 s.
  - At most 2-4 `Fx.Flash` lights for the whole of Skyfall.
- **The sky dial:**
  - The game's own cave dimming: `DayTimeManager`'s `LightIntensityModifier` and `skyColorFade`.
  - Untinted; sun to about x0.5, `skyColorFade` at most 0.6 (the hard limit is 0.95, above which the skybox blackens).
  - Only on clients within about 150 m of the sigil, only while the storm runs, and fading over 1.5-3 s.
  - Restored on every way out: storm end, cave entry, scene change or disconnect, and a timeout. Nothing happens on the server.
- **"OUT!" (festival mode only):** each client checks only its own player's head against the dome spots at landing, and shows the game's 'Larger TextPopup' in front of their face. No traffic.
- **Sounds, by exact name:** 'Shrine Activate Sounds', 'Skill Orb Acquire Sounds', 'Shockwave Sounds', 'Tower Completion'. 'Huge Explosion' comes only from 'Explosion' itself.

### Testing with one client (my call, after the builder check)
- A debug switch, `[Skyfall] SpectatorTest`, runs the caster's own release through the spectator path as well, and logs both landing patterns. One client can then confirm that a spectator would rebuild the same storm.
- A real second player, when one is available, checks the spectator view for real, including a friend still on the old DLL. They should see a plain arrow fly and vanish, and nothing worse.
- **Where builds go** (owner, 2026-11-19: "Restore my pack first"). The owner switches the game back to their QuickshotRing pack before any Skyfall test. Until then:
  - the standing auto-install must not run;
  - nothing is copied into the game's current pack, which holds RevampedSkills;
  - the installer must refuse while the launcher's "My mods" pack folder is missing, and never leave a half install.

### The project's rules, which apply as always (CLAUDE.md)
- Every number and colour is a `[Skyfall]` setting, live-reloaded. Anything that changes the pattern or its timing is `[shared]`.
- A timing section on every new patch, tick and look.
- New value holders go into `SelfCheck.Run`, with no circular reads.
- `[Archery] Skyfall:` log lines, with per-shot detail at Debug only.
- Every patch is wrapped in a try/catch.
- No scene-wide searches per shot.
- Server ticks go before the headless return.
- State is kept per bow string or per arrow.
- Install with `tools/stage-build.sh`. Friends need the new DLL, because the settings and the claim field are shared.

---

## Part C. Approaches considered

### Where the code lives (owner picked 1)
1. **Inside QuickshotRing, walled off (picked).**
   - It reuses the shot pipeline, the claim relay, the effects, damage, timing, self-check and tests.
   - Friends keep one DLL.
   - Cost: its single seam sits in code every shot passes through. It is covered by tests, and the seam checks "holds Skyfall and aimed at the sky" first.
2. **A separate Skyfall.dll.**
   - Delete it and it is gone.
   - But friends would need a second matching DLL; that kind of mismatch is what broke selling with RevampedSkills.
   - It cannot make QuickshotRing stand aside, so a sky shot would also fire the caster's normal volley.
   - It would duplicate the arrow, effect and damage code.
3. **Unity asset bundles for the looks (dropped).** The export's shaders are placeholders, so every material would need swapping at runtime. The mod's proven way is building looks in code from the game's runtime textures and effects.

### Where the storm lands (owner picked A)
- **A. Where you point (picked):** one action you can watch form in the sky. Its aim is rough.
- **B. A second arrow marks it:** precise, but needs two shots and a waiting window.
- **C. Around the caster:** simplest, but you do not choose the spot.

### Telling the caster (my proposal; the owner decides)
- **The game's player message, made invisible (proposed):** one per grant change, to the holder only, never shown on screen; covers admins with no orbs; one exception to the no-new-traffic rule.
- **TavernLib roles (rejected):** they persist in the token file and depend on another mod's internals.
- **A marker on the first cast (rejected):** the first storm looks wrong to its caster.

---

## Part D. Constraints
- One DLL on the server and every client, with MelonLoader 0.7.3 and net472 APIs only.
- The server decides all damage. Clients never deal any.
- The headless server must never play sounds or effects; the game's sound calls throw there.
- VR: never move or shake the player's camera; no strobing; frame budget first.
- The art must come from or match the game's own: see the game-native kit.
- The private Unity export (`C:\ATTUnity\ExportedProject`) is reference only. Its pixels may be embedded only where the mod already embeds game art (icons); it is never shipped or published.
- Model roles: Opus builds and Fable reviews (owner). Fable's usage limit runs until 2026-11-20 1pm PT; until then reviews run on Opus at max effort (owner, 2026-11-18).

---

## Part E. Left to the builder
- Exact defaults for every count, size, speed and duration, tuned in play-tests. The numbers above are starting points, and all of them are settings.
- How the sigil is composed (bands, runes, spin speeds) within the kit's rules. The storyboard is the target look.
- The hold looks' timing curve and the bow-hand haptics.
- Which sounds play at each beat, from the kit's named list. One per beat, never stacked.
- The file split inside "walled off", and the test design for the rules file.
- How to show the server's refusals (no such player, the storm cap reached) in the console reply.
- Loot from mass kills is saved with the world, as any kill's is. The range's existing "Clean up loot and junk..." button already removes it. Whether Skyfall should do more is left open.

## Part F. Owner's answers after the builder check (2026-11-19)
1. **Messages:** "No need to player messaged people." So the signal to the holder's game is invisible and goes to the holder only, and other players see the show from the release.
   - Still open: approving the design approves this one invisible exception to the no-new-traffic rule.
   - Without it, the holder's game cannot know it holds Skyfall until its first cast in a session. That first cast would show no rings while you hold, and the caster's own screen would start the storm late.
2. **Testing:** "Restore my pack first." See "Where builds go".
3. **A player caught in a dome:** "Instantly kill them." See "Caught in a dome".
