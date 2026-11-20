# Skyfall fact check: runtime-looks (2026-11-19, read-only)

## Recommendation
1. RING: build your own quad mesh with the cell's UVs (row 2 for the hard ring, row 3 for the soft glow) and vertex colours.
   - Put it on a copy of Particles_Combat_White_Hard, taken through FindEffect("Easter Egg Confetti Big").
   - Trade-off: additive white barely shows on a bright day sky. A Sprites/Default copy with the same texture (alpha-blended) reads better against daylight.

2. GLYPHS: use the ruins glyphs by name (always loaded) and Glyph_Bow_LightBeam (loaded once the trees are). Embed PNGs only for anything else.
   - Make embedded copies with a mip chain if they will be drawn small; mip-less 512 px textures shimmer when shrunk.

3. DOME: read the mesh and material once from PrefabManager.GetPrefab(31326).
   - Give it your own GameObject, scaled to R/2.97. Copy the material before tinting it.
   - Never instantiate the game's object: its ExplosionRadius component destroys itself without a parent.

4. EFFECTS:
   - Use Explosion and Shockwave Impact with Fx.GetScaled; both grow fully.
   - Keep impacts 1.5 m apart within 6 s, or they merge and cut each other off.
   - Do not also play "Huge Explosion": the Explosion effect already plays it.
   - Fireworks: call ColoredParticleSystem.SetColor after Get, and expect the main burst to stay the game's size.
   - Festival: Healing Explosion, Confetti and Level Up stay their native size.
   - The cap of 12 has to be the mod's own; the pools will not stop you.

5. OUT!: use the game's Larger TextPopup with the text "OUT!".
   - Show it only on that player's own client, at most 2-3 m in front of their head, and release it yourself.
   - No network traffic needed.

6. WORLD-NAIL: copy the four LOD0 renderers with their real materials, reusing Ghost's copying code.
   - Scale it uniformly, or along the arrow's own axes only.
   - Put it and all other big sky art on layer 1 'TransparentFX' so viewers more than 100 m away still see it. That layer has no cull distance and the eye camera renders it.

7. SKY DIAL: Lerp LightIntensityModifier and skyColorFade down once.
   - Keep skyColorFade at 0.95 or less.
   - Lerp both back only if DayTimeManager still exists and IsEnabled is true.
   - Drop the cached reference and timers when the scene unloads.
   - Do nothing on the server.
   - Ambient light needs OverrideAmbience; skip it unless the look demands it.

8. BUDGET:
   - Allow 2-4 Fx.Flash lights at once for the whole of Skyfall.
   - Gate every Get and every sound with !ApplicationManager.IsHeadless.
   - Spread explosions over waves: the first instances of each effect are created on demand.

## Findings
Roots: Q = C:\Users\dmand\OneDrive\Desktop\attmod\QuickshotRing; D = C:\Users\dmand\OneDrive\Desktop\attmod\_decomp; X = C:\ATTUnity\ExportedProject\Assets (the AssetRipper export: asset data only, no code); PS = C:\ATTUnity\ExportedProject\ProjectSettings; LA = C:\Users\dmand\OneDrive\Desktop\A Township Tale (client\UserData\QuickshotRing\loaded_assets.txt (snapshot from 2026-11-17 20:24).
How I checked: X matches the live build's hashes (Turabada 13804 at X\Resources\network prefabs\creatures\monsters\turabada variants\Turabada.prefab:2372, the same as the mod's SpawnRule). I identified the scripts on each prefab by Unity's fileID hash (C:\Users\dmand\AppData\Local\Temp\claude\2c7b432df063cefe\3aab37a1-c87d-4a47-8bd5-6f6c1d86eeb4\scratchpad\fileid.py). The particle enums (scaling Hierarchy=0, Local=1; simulation Local=0, World=1) come from decompiling the client's UnityEngine.ParticleSystemModule.dll.

CORRECTIONS TO THE SCOUT'S REPORT
- C1. No effect pool here ever recycles. Every definition has isMaxSizeLimitting 0: X\MonoBehaviour\Explosion.asset:19, and the same line in Firework Explosion, Easter Egg Confetti Big, Healing Explosion, Level Up Effect, Shockwave Impact, TextPopup and Larger TextPopup .asset. So the game always uses the unlimited PooledObjectPool (D\PooledObjectDefinition.cs:38-51). Only 2 of the 466 definitions in X use the limited pool (POD_RepairBox_DepositItem and its _Panel). The scout's "recycles at 5" (feasibility-scout.md:101,233,250) does not apply.
- C2. The game has no "OUT!" text anywhere. The only match in D is ArrowFlightSettings.cs:33 [Header("OUT")]. Searching X (GameObject, MonoBehaviour, TextAsset, Scenes, Resources) found nothing, and I2Languages.asset is only 1.6 KB.
- C3. The camera hides layer 0 beyond 100 m. PlayerController sets layerCullDistances[0] = DefaultCullingDistance with spherical culling (D\PlayerController.cs:231-237), and that setting defaults to 100 (D\Alta.Utilities\GameSettings.cs:115). The mod never sets a layer, so everything it draws is on layer 0.
- C4. The two sky dials need no per-frame fight (item 7). Only ambient light and fog are re-applied every frame.

1. RING SPRITE (SpriteSheet_Combat on Particles_Combat_White_Hard)
- The material uses Mobile/Particles/Additive (LA:1701; X\Material\Particles_Combat_White_Hard.mat:11). Its _MainTex (:22) is SpriteSheet_Combat: 2048x2048, white RGB with the shapes in alpha, no mipmaps, not CPU-readable (X\Texture2D\SpriteSheet_Combat.png.meta:11,25).
- The sheet is 8x8 cells of 256 px. Rows counted from the top:
  - row 0: starbursts;
  - row 1: spiked ring;
  - row 2: hard ring, breaking up over 8 frames;
  - row 3: soft glow ring;
  - row 4: splatter;
  - rows 5-7: empty.
  Preview: C:\Users\dmand\AppData\Local\Temp\claude\2c7b432df063cefe\3aab37a1-c87d-4a47-8bd5-6f6c1d86eeb4\scratchpad\combat_sheet_grid.png.
- The game's own 'Slashing_Cicrle' particle plays row 2 (tiles 8x8, single-row animation, rowIndex 2: X\GameObject\Easter Egg Confetti Big.prefab:10129-10132).
- It is always in memory on clients. The chain: the Easter Egg 04 network prefab → the Confetti Big definition → its Slashing_* renderers use this material. All network prefabs are loaded by D\Alta.Networking\PrefabManager.cs:23.
- Three ways to get it, each cached once:
  - Fx.FindEffect("Easter Egg Confetti Big").Prefab, then its ParticleSystemRenderer.sharedMaterial (same pattern as Q\Fx.cs:319-325);
  - FindObjectsOfTypeAll<Material> with an exact-name match (as Q\Tablet.cs:866 does); a "...Hard Emmissive" material also exists (LA:1702);
  - the Texture2D by name, as Tablet.FindTexture does (Q\Tablet.cs:929-938).
- To draw one cell:
  - Use your own quad mesh whose UVs cover the cell: u from c/8 to (c+1)/8, v from 1-(r+1)/8 to 1-r/8. Give it vertex colours (the Tablet.Quad pattern, Q\Tablet.cs:645-654).
  - Put it on a COPY of the material. About 60 game particle systems share it (LA:792-1205). The shader has no colour property, so tint and fade through the vertex colours.
  - Material offset/scale will not work on Sprites/Default, which passes UVs straight through (C:\Program Files\Unity\Hub\Editor\2020.3.23f1\Editor\Data\CGIncludes\UnitySprites.cginc:61).

2. GLYPHS
- All are 512x512, no mipmaps, not readable (X\Texture2D\Glyph_Ruins_Symbol_02.png.meta:9,23).
- Glyph_Ruins_Symbol_01..05 are the textures of the 'Ruins Weighted Orb 0N (Glyph)' materials (LA:1767-1771). Those belong to the network prefabs in X\Resources\network prefabs\ruins weighted orb prefabs\, so they are in memory on every client after PrefabManager.cs:23 and can be found by name. The Tablet already does this for _02 (Q\Tablet.cs:141, 1099-1105).
- Glyph_Bow_LightBeam is the slotIcon in X\Resources\progressiontrees\Ranged Skill Tree.asset:43. It loads when the trees load (D\ProfessionSkillTree.cs:69-81), which the mod triggers every second after joining (Q\Tree.cs:200-207).
- I did not trace the other 906 Glyph_* textures. Example: Glyph_ExplosionEffect is only referenced by the page glyph X\Resources\pages\glyphs\Glyph_ExplosionEffect.asset (pages load through HashedPath "Pages/Glyphs", D\Alta.Pages\Glyph.cs:7).
- Embedded PNGs are the guaranteed route (Q\Tree.cs:812-822). Tree builds them without mipmaps (Tree.cs:821).

3. DANGER DOME (StylizedWater_DYNAMITE)
- The Dynamite network prefab has hash 31326 (X\Resources\network prefabs\props\Dynamite.prefab:699).
- Its child 'ExplosionRadius' is inactive in the prefab (:357) and has scale 1.36 (:6229). It carries the game's ExplosionRadius component: explosionRadius 4, scaleMultiplier 0.34, scaler = RadiusVisual (:6247-6249).
  - Each LateUpdate it sets the scaler's scale (D\ExplosionRadius.cs:48-57).
  - It unparents itself on enable (:22-25) and destroys itself if it has no parent (:50-53).
- 'RadiusVisual' (y -0.1, :6258) holds:
  - the material StylizedWater_DYNAMITE (:6283), shadows off (:6272);
  - the mesh 'sphereInsideOut_test' (:6312): 1242 vertices, radius 2.92 across and 3.03 tall, centred (X\Mesh\sphereInsideOut_test.asset:153-155).
- You can take it without spawning anything: PrefabManager.GetPrefab(31326), or GetPrefab("dynamite"), returns the already-loaded prefab (PrefabManager.cs:88-98). Then GetComponentInChildren<ExplosionRadius>(true) gives you RadiusVisual's sharedMesh and sharedMaterial. Read it only, like the Tablet's crystals (Q\Tablet.cs:952-958).
- On the headless server, renderers' materials and meshes are stripped (PrefabManager.cs:24-27, 32-47).
- At runtime the shader is the game's real compiled 'Unlit/ExplosionRadius' (LA:1868). X only has a stub (X\Shader\Unlit_ExplosionRadius.shader:10). Its property list (:2-9) shows the live properties; with the material's values:
  - _EdgeTex (none set in the material);
  - _EdgeColor (1, 0.476, 0) (StylizedWater_DYNAMITE.mat:88);
  - _EdgeFactor 0.7 (:34);
  - _Cutoff 0 (:28);
  - _ScrollSpeed (1, 0, 0) (:92);
  - _RadiusSqrd 8.8 (:58).
  The other StylizedWater_* values in the material are stale. 8.8 is about the mesh's own radius squared (2.97²), so the shader probably works in object space. The game sizes the dome only through transform scale (ExplosionRadius.cs:56).

4. POOLED EFFECTS (unlimited, see C1)
How unlimited pools behave:
- A request with no idle copy creates a new instance (D\PooledObjectPool.cs:44-63).
- maximumPoolSize only limits how many idle copies are kept (:82-93).
- Nothing is created in advance (initializeSize 0; D\BasicPooledObjectPool.cs:14-20).
- Get fires Requested before Fx.GetScaled reads the scale (D\PooledObject.cs:40-44), so GetScaled multiplies the game's own scale.

Per effect:
- Explosion
  - Idle kept: 10 (asset:16).
  - It is a ConglomerateDestroyByTime with a 6 s life (X\GameObject\Explosion.prefab:227).
  - Merge range 1.5 m (nearbyRangeSquared 2.25, :252). Requesting one within 1.5 m of a live one RELEASES the live one; the new one grows (scale 1.5 to 3 over 0 to 5 merges) (D\ConglomerateDestroyByTime.cs:23-36). The list of live ones is shared with Healing Explosion (:6).
  - All 8 particle systems use Hierarchy scaling (:314, 2583, 4893, 7247, 9717, 11990, 14336, 16690), so it grows fully with Fx.GetScaled.
  - It plays "Huge Explosion" by itself: SoundSource with soundEffect 199 = HugeExplosion, playOnEnable (:19028; D\SoundEffect.cs:95; D\SoundSource.cs:48-55).
  - Its light child is inactive (:109).
  - About 440 particles each.
- Firework Explosion (prefab Firework Explosion Style 2)
  - Idle kept: 5. Life 8 s (:137).
  - RandomScalePooledObject sets the root to a random 1-3x on each Get (:173; D\RandomScalePooledObject.cs:128-141).
  - The main burst uses Local scaling (:231), so it does NOT grow; the two smoke systems do (:2559, :4842).
  - It plays its sound on each Get through PooledObjectSoundHook, which has no headless check (:124; D\PooledObjectSoundHook.cs:12-20).
  - It has an active point light, range 10, with a PlatformLight (:7086).
  - Its colour is set the way the game does it, through ColoredParticleSystem.SetColor (D\ColoredExplosive.cs:46-55).
  - It sits on layer 8 (:16).
- Easter Egg Confetti Big
  - Idle kept: 10. Life 3 s (:137).
  - Confetti and ribbon use Local scaling (:195, :2424) and do not grow; the three Slashing_* systems do (:4653, :7116, :9462).
  - Silent.
- Healing Explosion
  - Idle kept: 10. ConglomerateDestroyByTime with a 3 s life (:155) and a 1.14 m merge range (:180).
  - All five systems use Local scaling (:271, :2519, :4828, :7110, :9473), so nothing grows.
  - Plays SmallExplosion (198) by itself (:208).
- Level Up Effect
  - Idle kept: 5. Life 5.5 s (:118).
  - Its particles use Local scaling (:189, :2508) and do not grow; only the cylinder mesh does.
  - Silent.
- Shockwave Impact
  - Idle kept: 5. Life 3 s (:119).
  - All four systems use Hierarchy scaling (:176, :2481, :4719, :6957), so it grows fully.
  - Silent.

Always loaded on clients, because network prefabs reference them: Explosion (Dynamite.prefab:6363), Firework (props\Firework.prefab), Confetti Big (Easter Egg 04.prefab), Healing (Healing Pod.prefab), Level Up (VR Player Character New.prefab). Shockwave Impact is only referenced by professionskill\Power Shockwave.asset. All six are listed in LA (152, 154, 121, 199, 213, 381).

5. "OUT!" POPUP
- How to get one:
  - GlobalSettings<PickupTypeSettings>.Instance.LargerTextPopup or .SimpleTextPopup (D\PickupTypeSettings.cs:80-83, 182-184);
  - then .Get(pos, rot);
  - then GetComponent<Alta.Utilities.TextPopup>().UpdateText("OUT!", colour) (D\Alta.Utilities\TextPopup.cs:86-95; the default colour is red).
- The pool is unlimited, with up to 100 idle (X\MonoBehaviour\Larger TextPopup.asset:16,19).
- The root carries TextPopup plus a plain PooledObject and nothing times it out. The caller must Release it, as the game does (D\InteractableInspector.cs:76-103).
- What it looks like:
  - A world-space canvas (X\GameObject\Larger Text Popup.prefab:153) at scale 0.005, font size 60 (:254).
  - Material PopUpOverlay = UI/Default_OverlayNoZTest (:244; LA:1718), so it shows through walls.
  - While the LOCAL head is within about 3.3 m (maxDistance 10 is compared to a squared distance, :87; TextPopup.cs:60-75), it turns to face the head and scales from 0.15 to 0.41.
  - It does not rise (upSpeed 0, :86).
  - Farther away, its Update switches off until the player moves back within 3.16 m (:51-58).

6. WORLD-NAIL
- The arrow parts (X\Resources\network prefabs\props\combat\Arrow.prefab; LODGroup size 1.22):
  - head metal_detail_spike_fancy_rust_LOD0: 52 vertices, under 'Spike', which is rotated 90° about X with scale 1.21 / 0.75 / 0.86;
  - shaft arrow_shaft_Atlased: 43 vertices, 1.085 m long, 3.6 cm thick;
  - two feathers: 124 vertices each.
  Vertex counts and bounds are in X\Mesh\*.asset.
- Materials: Rust_m (Cheap/SimpleStandard) for the head and Wooden_Atlas_m (Hidden/SimpleStandardAtlasedTexcoord) for the rest (LA:1778, 2046).
- The arrow is about 1.2 m long, so 40x is about 48 m.
- Volley.Ghost copies only the head (Q\Volley.cs:1508-1520). HighestDetail already returns every LOD0 renderer (Volley.cs:1645-1657).
- Copying each part with lossyScale (Volley.cs:1514) reproduces this arrow exactly: every mesh has an identity rotation under its scaled parent.
- Culling:
  - Renderer bounds follow the transform, so frustum culling stays correct at any scale.
  - Copies without a LODGroup always draw LOD0.
  - The far clip is 2200 m on 'VR Head (eye)' (X\Resources\network prefabs\player\VR Player Character New.prefab:7162), so it is not a concern.
  - The 100 m limit on layer 0 (C3) does matter.
  - The eye camera renders every layer except 17 (mask 4294836223, :7169).
- Shadows only reach 75 m on the Fantastic quality setting (PS\QualitySettings.asset:16).

7. CAVE DIAL (DayTimeManager)
- The type is Alta.Environmental.Atmosphere.DayTimeManager, in D\Alta.Environmental.Atmosphere\DayTimeManager.cs.
- The three private members:
  - LightIntensityModifier (a LerpingFloatValue, :198). It multiplies the sun and moon intensity (:719-743).
  - skyColorFade (a LerpingFloatValue, :222). It sets the skybox's _SkyColorDisableFade (:758-761); when a fade ends above 0.99, DisableSkyColor turns on _DISABLED (:354-367).
  - skyBoxMaterial (:194). In builds this is the SHARED skybox asset itself (:254-261).
- On Awake, SetupVariableLerpers snaps LightIntensityModifier to 1 and skyColorFade to 0 (:311, :329-338).
- LerpingFloatValue.Lerp(target, lerpSpeed) takes a duration in seconds (D\Alta.Utilities\LerpingFloatValue.cs:31-61).
- To reach it: AtmosphereManager.instance.AtmospherePlugins (D\Alta.Environmental.Atmosphere\AtmosphereManager.cs:17,58), then Traverse.Field("LightIntensityModifier") and Traverse.Field("skyColorFade").
- Holding a value:
  - LateUpdate only advances these two while a fade is running (:477-488).
  - Every frame it re-applies Current while the plugin is enabled (:659, :682).
  - So a lerped value stays put.
  - The only other writers: Enabled() (:696-717: fade to 1, clear _DISABLED, fade the sky to 0), Disable() (:763-771), both driven by AtmosphereManager.cs:253,257,265; and Awake.
  - Only ambient and fog are fought every frame (:673-680).
- Ways out:
  - Disable(): in a cave the game fades the light to 0 and the sky to 1 by itself.
  - Scene change: nothing in the atmosphere code survives a scene change (no DontDestroyOnLoad), and the next instance's Awake resets both values.
  - Headless: DayTimeManager disables itself (:287-293).

8. BUDGETS AND SOUNDS
- Fx.Flash creates a new GameObject and Light (Auto render mode, no shadows) on every call. There is no pool and no overall cap (Q\Fx.cs:91-98, 386-402).
- Existing per-feature limits:
  - flashes: one per volley release (Q\VolleyFx.cs:371-385), one per Shatter blast (Shatter.cs:472), Holy Arrow (HolyArrow.cs:549, 738), lock cages up to 12 (SeekCage.cs:28);
  - lines: Ricochet arcs 60 per release (Ricochet.cs:113) from a pool of 64 (:612), Shatter pool 96 (Shatter.cs:386), Balance rings 6 (Balance.cs:22), Holy motes 32 (ChargeFx.cs:824);
  - one shared spark ParticleSystem, 2000 particles (VolleyFx.cs:227);
  - Fx.Bolt makes a new object every call (Fx.cs:84-89).
- The game's light budget: pixelLightCount is 5 on Fantastic and 1 on Low (PS\QualitySettings.asset:11,47). PlatformLights are re-sorted every 0.5 s and the nearest (count - 1) get pixel lighting (D\Alta.Lighting\PCLightsController.cs:84-114). Fx.Flash lights are not part of that sort.
- Sounds:
  - All five exist (LA:620, 684, 685, 687, 720) and Fx.FindSound matches them by exact name (Fx.cs:163-178).
  - Four are in the game's 'Sound Map' (X\MonoBehaviour\Sound Map.asset), so they are always loaded. Tower Completion is not; only X\GameObject\PS_TowerCompleted.prefab references it.
  - PlayAtPosition plays the set's own sound event (D\AudioManager.cs:184-187, 231-243), so Tower Completion's type 'None' (999) does not matter.
  - Calls with no caller object are not merged within a frame (D\AudioManager.cs:255-272).
- On a headless server, AudioManager disables itself and drops its sound map (D\AudioManager.cs:51-56, 131-135). Even so, a play call still pulls a pooled Wwise object and registers it before refusing (D\AudioManager.cs:204-212, 296-303).
- def.Get has no headless check of its own (D\PooledObjectDefinitionBase.cs:31-90).

## Risks
- No pool limit here. 12 live Explosions is about 5,000 particles, mostly large alpha smoke. Scaling them up adds overdraw in VR. The first uses also create new copies on the spot (an 11-object, 8-system prefab each), which can stall a frame.
- Merging: a later Explosion within 1.5 m, or a Healing Explosion within 1.14 m, instantly removes the live one and its sound. This also affects the game's dynamite and healing pods nearby.
- Layer 0 hides everything beyond 100 m by default. That covers the sigil, domes, nail and falling arrows for distant spectators, and arrows starting high in the sky until they come within 100 m.
- Doubled explosion sound if the design also plays "Huge Explosion". Fireworks' sound hook has no headless check: calling it on the server is unsafe.
- Fading skyColorFade above 0.99 blackens the shared skybox. Only DayTimeManager.Enabled() clears it, so it can outlast the effect.
- Restoring the light while in a cave lights the sun underground. Entering and leaving a cave mid-effect cancels the darkening.
- The OUT! text is small (estimated 6 cm letters at 1.5 m). It stops turning toward the player beyond 3.3 m.
- At 40x the nail's low-poly parts (43 and 52 vertices) will look faceted, and the atlas texture will be blurry.
- Fx.Flash lights compete with the game's lamps for at most 5 pixel lights (only 1 on the Low setting). Each pixel light is an extra render pass on everything it touches.
- An additive ring has weak contrast against a bright daytime sky.

## Not verified
- Assumes the AssetRipper export matches the installed build. Only Turabada's hash was cross-checked; Dynamite's 31326 and the pool settings were not checked in game.
- How the compiled 'Unlit/ExplosionRadius' shader uses _RadiusSqrd and _EdgeFactor. The object-space reading is an inference from 8.8 ≈ 2.97². The look at other sizes or colours, and whether the faces are culled, was not seen. The dome size of about 5.5 m on a real dynamite was not measured.
- Whether world-space smoke and embers also travel farther when scaled by Hierarchy at 3-10x. Fx.GetScaled has only been used up to 1.3x.
- Whether Mobile/Particles/Additive respects material tiling and offset. Use mesh UVs to avoid the question.
- That Unity numbers sheet rows from the top. This is consistent with Slashing_Cicrle using row 2, the ring row in the image.
- The user's actual DefaultCullingDistance. It is stored under %APPDATA%\A Township Tale, which I did not open as required. Also unverified: whether per-layer culling measures to the bounds' centre or their nearest point.
- Which quality level the user plays on.
- Whether the arrow meshes are CPU-readable at runtime. This only matters for CombineMeshes.
- That disconnecting unloads the world scene and so destroys DayTimeManager. There is no DontDestroyOnLoad, but this was not observed.
- Whether the Tablet's rune lookup works in play. No warnings were found in client logs, but success is never logged.
- Wwise voice limits when many sounds play at once.
- How long each first-time copy takes to create.
- How readable the OUT! text is.
- Which loaders pull in the remaining Glyph_* textures.
- Whether Tower Completion is in memory outside the 11-17 snapshot.
- Nothing was run in game. No edits, builds or installs were made.
