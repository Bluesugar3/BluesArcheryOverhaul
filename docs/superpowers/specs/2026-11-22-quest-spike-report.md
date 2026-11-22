# Quest test (spike): report

2026-11-22. Plan: `docs/superpowers/plans/2026-11-20-quest-spike.md`. The probe mod is on branch `quest-spike` (throwaway, never merged).

## Verdict

The port can go ahead. Every thing it depends on worked on the owner's headset (Quest 2, Android 14) against the owner's
server: a Harmony patch on the bow, the join gate's messages, private game fields, a game event, a component of the mod's
own that draws a line, a settings file, and the game's effects, textures, shaders and prefabs. Nothing blocks it. It is a
large conversion, not a research problem.

## The six probes

| # | Probe | Result |
|---|---|---|
| 1 | Harmony postfix on `BowString.HeldUpdate` | PASS. Ran every held frame (481 calls in the test). |
| 2 | Answering the join gate (Here in, Hello out, message 27) | PASS. Server: `Join: Blue passed (client 0.19.10, wire 5, Quest, ..., answered in 100 ms)`. |
| 3 | A private game field and a game event | PASS. `interactorHoldingBowString` read; `OnUpdatedStringPull` fired with the pull 0.00 to 1.00. |
| 4 | Own `MonoBehaviour` with a `LineRenderer` | PASS. The owner saw the green ring; `Sprites/Default` exists on Quest. |
| 5 | Own MelonPreferences file under `UserData` | PASS. Written, saved, reloaded, read back. |
| 6 | Game assets, 30 s into the world | PASS. Found: `Explosion`, `Charged Axe Shatter Effect`, `Axe Shatter Damage Effect`, `Combat Hit`, `Large Smoke Puff` (429 pooled effects loaded); textures `Glyph_Bow_LightBeam`, `SpriteSheet_Combat`, `Glyph_Ruins_Symbol_01`; material `Particles_Combat_White_Hard`; shaders `Sprites/Default`, `Hidden/Internal-Colored`, `UI/Default`, `Unlit/Unlit Color`; prefabs Arrow 56460 and Dynamite 31326. |

Not found by probe 6, each with a likely harmless cause that is not yet confirmed: `Huge Explosion` (a sound, not a pooled
effect: the probe looked in the wrong place), `Glyph_Bow_Quickdraw` (the name was a guess; the mod ships its own icons),
the material `Orb Connection Line` (probably loaded only while a shrine tree is open). At the main menu almost nothing is
loaded: an asset search must run in the world.

## The server

- A server serves Quest players only on the game's Quest world (TavernLauncher's `quest_scene`; the headset loads scene 5,
  `Overworld Chunked[Q]`). PC players can join it.
- The archery mod started cleanly on the Quest world with no new warning or error of its own. The headset saw the mod's
  16-orb tree in its new places with the game's own icons: the tree is the server's, the icons are the client's.
- The server ran only TavernLib and the archery mod (the owner had moved every other mod into `Mods\disabled`): the
  cut-down server the owner decided on. A Quest join against SyncLib, RevampedSkills or CaptureOrbs was NOT tested.
- The headset's account (`Blue`) is not the PC's (`Blue_XD`), so both can be on the server at once: the owner can be the
  second archer for most checks.
- Gate on `log`, no mod: let in, `would be refused (log only): This server needs the mod ...`. Gate on `refuse` with the
  probe: let in (5 s slower: the probe does not answer the server's settings, which is logged and let through under
  `SettingsMismatch = log`). Gate on `refuse`, no mod: turned away; the server logged `refused` and one clean
  `Destroying connection`.
- OPEN: what the headset shows a refused player. Its log has no line for our refusal (TavernEssentials logs the reason of
  refusals it knows, such as "Already connected to this server."). The owner saw a refusal but its wording is not recorded.

## Building for Quest

- References: one flat folder, `C:\ATTUnity\QuestRefs\ATT-Quest-Modding-dlls\questdlls` (github.com/TheRavenSeb/
  ATT-Quest-Modding-dlls, commit 65350ba): the game's Il2Cpp assemblies, MelonLoader 0.6.5, Il2CppInterop.Runtime 1.4.6,
  0Harmony 2.13, Unity modules. Private: never in the repo. The headset runs the same: MelonLoader 0.6.5 Open-Beta, game
  `main-1.7.2.1.42203`, Unity 2020.3.23f1.
- Project: net8.0, `[MelonGame("Alta", "Att_Quest")]`, references `$(QuestReferences)\*.dll`. A `Directory.Build.props`
  inside the project's own folder shadows the repo root's (net472, PC references), so the root file needs no change.
- All sixteen Unity-free files compile for net8.0 unchanged.
- Names: PC `Alta.X` is `Il2CppAlta.X`; PC global-namespace game types are in the namespace `Il2Cpp`.
- What worked, one line each:
  - Harmony: `[HarmonyPatch(typeof(BowString), "HeldUpdate")] static void Postfix(BowString __instance)`.
  - A private field: `__instance.interactorHoldingBowString` (a public member on Quest; Harmony's `___field` is not used).
  - A game delegate: `(SerializeConnectionMethod)new Action<Connection, Stream>(Read)` (implicit operator); an
    `Il2CppSystem.Action<float>` through `DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnPull))`.
    Every delegate handed to the game is kept in a static field or list.
  - An event: `bowString.add_OnUpdatedStringPull(handler)`.
  - A cast: `stream.TryCast<StreamReader>()`.
  - Own component: a class with `public X(IntPtr pointer) : base(pointer) { }`, registered once at start with
    `ClassInjector.RegisterTypeInIl2Cpp<X>()`, then `AddComponent<X>()`. NOT MelonLoader's `[RegisterTypeInIl2Cpp]`
    attribute: on this loader its own registration path fails at start-up (`MissingMethodException:
    RegisterTypeOptions.set_LogSuccess`), and the mods that ship with the patched game call `ClassInjector` themselves.
  - Message length: the Quest `StreamReader` has no `IsFinished` / `TotalByteCount`; `reader.BitsRead` and
    `reader.TotalBits` (its `BitReader`) give the same numbers.
  - Settings: `MelonPreferences.CreateCategory`, `SetFilePath`, `SaveToFile`, `LoadFromFile` as on PC;
    `MelonEnvironment.UserDataDirectory` is
    `/storage/emulated/0/Android/data/com.Alta.ATownshipTale/files/MelonLoader/com.Alta.ATownshipTale/UserData`.
- Message numbers in use on the headset: 30 and 36 (voice), 32 and 35 (TavernEssentials). 27 is free. The Quest side uses
  8-bit message types.

## The first compile of the whole mod for Quest

With one names file (the PC namespaces declared empty, the Quest ones imported everywhere) and the server-only console
file left out, the 54 game-facing files (26,193 lines) give **191 compile errors** in 39 files
(`2026-11-22-quest-port-first-compile-errors.txt`). By kind: about 80 are interfaces (on Quest an interface is a class:
`Player` does not convert to `IPlayer`, an `IPlayer` is not pattern-matched to `Player`); 20 are delegates and lambdas
handed to the game; about 25 are the game's own collections (`foreach`, LINQ); about 30 are fully written-out names
(`global::Player`, `Alta.Serialization.Stream`); the rest are single members that differ.

What does not show as a compile error and still has to be converted: 106 Harmony `___field` parameters (13 files), about
100 `as` / `is` casts on game objects (they compile and silently give null on Quest), 92 `Traverse` / `AccessTools` uses,
64 event subscriptions, 22 components, 6 `NonAlloc` physics calls, and every patch whose target the Quest build inlined
or lacks. About 500 places in all.

## The loop

Build 15 to 25 s; `adb push` under a second; the game's start to the menu about 30 s; a join about 15 s; `adb pull` of the
log under a second. One round with the owner in the headset is about three minutes plus what is being looked at.

```
A=C:\ATTUnity\_downloads\platform-tools\platform-tools\adb.exe
M=/sdcard/Android/data/com.Alta.ATownshipTale/files/MelonLoader/com.Alta.ATownshipTale
adb push <dll> $M/Mods/<dll>          adb shell am force-stop com.Alta.ATownshipTale
adb pull $M/MelonLoader/Latest.log    adb shell rm $M/Mods/<dll>
```

Force-stopping the game while the player is connected leaves the server holding the connection for a few seconds: the
next join is first turned away with "Already connected to this server." and the game retries by itself.

## For the port's plan

1. One names file removes the namespace problem; nothing else is free.
2. Interfaces, casts, delegates, events and components each need one helper and then a pass over every site.
3. The mod must apply its own Harmony patches one class at a time on Quest and log each that fails: one bad patch must
   not stop the rest.
4. Asset lookups run in the world, never at the menu.
5. The Quest game must answer the server's settings (message kinds 3 and 4) as well as the greeting.
6. The headset's refusal text is still to be looked at.
