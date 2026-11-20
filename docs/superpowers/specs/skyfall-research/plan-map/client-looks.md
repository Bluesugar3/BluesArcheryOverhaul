# Skyfall plan map: client-looks (2026-11-19, read-only)

## Map
CLIENT LOOK TOOLKIT FOR SKYFALL: facts with file:line. Paths are relative to C:\Users\dmand\OneDrive\Desktop\attmod. Q = QuickshotRing\, D = _decomp\, X = C:\ATTUnity\ExportedProject\Assets (private reference), LA = <client>\UserData\QuickshotRing\loaded_assets.txt (the 2026-11-17 snapshot).
Every Q helper below is `internal`, in namespace `QuickshotRing`. Fx.cs uses only `using MelonLoader; using UnityEngine;` (plus System.*). Volley.cs also has `using HarmonyLib; using Alta.Global; using Alta.Networking; ...`.

======================================================================
(1) Fx.cs: `internal static class Fx` (Q\Fx.cs:12)
======================================================================
- `internal static Material? LineMaterial()` (Fx.cs:21-39)
  - ONE shared `Sprites/Default` material, renderQueue 3100. The fallback is Hidden/Internal-Colored with alpha blend; it returns null if neither shader exists.
  - It is shared by every line in the mod, so never tint it directly. Make a copy:
    `new Material(Fx.LineMaterial()) { name = "...", color = c }` (Volley.cs:1428).
  - Textured copy, from Tablet.cs:1104-1108:
    ```
    Material? flat = Fx.LineMaterial();
    if (flat == null || !flat.HasProperty("_MainTex")) { Once("no textured flat shader: no rune"); return; }
    glyphCopy = new Material(flat) { name = "Archery Tablet Rune" };
    glyphCopy.SetTexture("_MainTex", glyphTexture);
    ```
- `internal static LineRenderer NewLine(GameObject go, int points, float width, bool taper)` (Fx.cs:41-52)
  - AddComponent<LineRenderer>; `material = LineMaterial()`; useWorldSpace true; widthCurve linear 1 to 0.2 when taper, else constant; widthMultiplier = width; no shadows; numCapVertices 2.
  - One renderer per GameObject: put every extra line on a child (CLAUDE.md).
- `internal static void BuildCircle(LineRenderer lr)` (Fx.cs:55-63)
  - Sets useWorldSpace false, loop true, 48 points on the unit circle in local XY. The radius is the transform's scale.
  - Usage (Volley.cs:1851-1858):
    ```
    LineRenderer lr = Fx.NewLine(go, 2, GoldHold.GlintWidth, false);
    Fx.BuildCircle(lr);
    ...
    t.SetPositionAndRotation(pos, Quaternion.LookRotation(direction));
    t.localScale = Vector3.one * r;
    ```
- `internal static void Basis(Vector3 axis, out Vector3 u, out Vector3 w)` (Fx.cs:65): two perpendiculars to the axis.
- `internal static void Zigzag(LineRenderer lr, Vector3 a, Vector3 b, float amplitude)` (Fx.cs:71).
- `internal static void Bolt(Vector3 a, Vector3 b, float amplitude, float life, float width, Color head, Color tail, int kinks = 4, float delay = 0f)` (Fx.cs:84).
  - Makes a NEW GameObject on every call; there is no pool.
- `internal static void Flash(Vector3 pos, Color color, float brightness, float range, float life, float delay = 0f)` (Fx.cs:92-98)
  - A new GameObject plus a point Light (no shadows) on every call. Its intensity falls linearly over `life`, then it destroys itself (FlashLight class, Fx.cs:386-402).
  - No pool and no global cap: Skyfall must count its own 2-4 lights.
  - Example (SeekCage.cs:204): `Fx.Flash(Where(c), s.Colour, s.Light, Mathf.Max(1.5f, r * 3f), Mathf.Max(0.05f, s.Look.FlashSeconds));`
- `internal static PooledObjectDefinition? FindEffect(string names)` (Fx.cs:123-138)
  - `names` = "A;B;C", the first one loaded wins. It matches `d.name` or `d.Prefab.name` among `Resources.FindObjectsOfTypeAll<PooledObjectDefinition>()`.
  - A hit is cached. A miss is retried after 5 s (`MissRetrySeconds`, Fx.cs:106), with one warning per name.
  - `Fx.ClearAssetCache()` (Fx.cs:184) is subscribed to `Cfg.Reloaded` at HolyArrow.cs:77.
- "Get": there is no Fx.Get. Use the game's API:
  - `public override PooledObject Get(Vector3 position, Quaternion rotation)` (D\PooledObjectDefinition.cs:53-57);
  - the base class also has `Get()`, `Get(Vector3)` and `Get(Transform parent)` (D\PooledObjectDefinitionBase.cs:9-29). `Get(Transform)` parents the object and keeps its localScale.
  - `Get` has no headless check (Base.cs:31-90; only the SpawnAt* helpers check), so gate every call with `!ApplicationManager.IsHeadless`.
  - Example (HolyArrow.cs:753):
    ```
    try { PooledObjectDefinition? def = Fx.FindEffect(impactEffects.Value); if (def != null) { def.Get(point, Quaternion.LookRotation(normal)); effect = def.name; } }
    catch (Exception e) { effect = "failed: " + e.Message; }
    ```
- `internal static bool GetScaled(PooledObjectDefinition def, Vector3 at, Quaternion rot, float k)` (Fx.cs:148-161)
  - `def.Get(at, rot)`, then `localScale *= k`. It subscribes to `PooledObject.Released` to restore the original scale.
  - False when the pool returns null.
  - Example (Shatter.cs:450-456, inside a `T_Spawn` timing section):
    ```
    PooledObjectDefinition? def = Fx.FindEffect(names);
    if (def != null) Fx.GetScaled(def, at, rot, k);
    else if (missing.Add(names)) MelonLogger.Warning($"[Archery] Shatter: effect '{names}' not found; the blast shows without it");
    ```
- `internal static SoundEffectSet? FindSound(string names)` (Fx.cs:163-178): the same caching and retry as FindEffect, with an exact name match on `SoundEffectSet.name`.
- Playing a sound:
  - the game's `public void PlayAtPosition(Vector3 position)` (D\SoundEffectSet.cs:42; a Quaternion overload is at :47);
  - or the mod's wrapper `VolleyFx.Play(string names, Vector3 at, float delay = 0f, float volume = 1f)` (Q\VolleyFx.cs:61-72):
    - headless-guarded;
    - a delay queues the sound (cap 64, pumped by `VolleyFx.Pump()` from Main.OnUpdate);
    - volume below 1 goes through Wwise output bus volume (PlayQuiet, :77-87).
  - Direct use (VolleyFx.cs:364-365): `SoundEffectSet? s = Fx.FindSound(landSound.Value); if (s != null) s.PlayAtPosition(at);`
- Haptics: `VolleyFx.Pulses(Controller? hand, int count, float strength, float gap)` (VolleyFx.cs:51). The bow hand comes from `Volley.BowHand(BowString bow)` -> `Controller?` (Volley.cs:195). Example: Shatter.cs:487-488.
- Material helpers:
  - `internal static (Material? mat, string failed) OrbLineMaterial()` (Fx.cs:319-325): the pattern for "material from a loaded effect's renderer";
  - `internal static string ResetColours(Material m)` (Fx.cs:328): sets every colour property to white;
  - `internal static string ShaderName(Material m)` (:341), `TextureProps(Material m)` (:344), `ColourProps(Material m)` (:354);
  - `Volley.HeavyLook.Tint(Material m, Color c)` -> string (Volley.cs:2204-2214): sets every Color property, returns their names, "" if none. HeavyLook is `internal class HeavyLook : MonoBehaviour` (Volley.cs:2082).
- Timing convention, required on every new look and asset search (CLAUDE.md):
  - `static readonly int T_X = Timing.Section("look Skyfall.X");` then `using var _t = Timing.Begin(T_X);` as the first statement;
  - asset searches are named "asset ...", for example Fx.cs:119-120;
  - logs: `if (Logs.Debug) MelonLogger.Msg(...)`; `Logs.Summary` / `Logs.Debug` are static bools (Cfg.cs:225-231).
- Colours from settings: `internal static Color Cfg.Colour(MelonPreferences_Entry<string> entry, Color fallback)` (Cfg.cs:211), which parses "#RRGGBB" and caches.
- Categories:
  - `internal static MelonPreferences_Category Cfg.Category(string name)` (Cfg.cs:34);
  - `Cfg.Shared<T>(entry)` (:43) marks [shared] entries;
  - `internal static event Action? Cfg.Reloaded` (Cfg.cs:24).

======================================================================
(2) VolleyFx.Sparks: `internal static class Sparks`, nested in `internal static class VolleyFx` (Q\VolleyFx.cs:225)
======================================================================
- `internal static void Emit(Vector3 at, Vector3 dir, int count, Color c, float spread, float speed, float sizeScale = 1f)` (VolleyFx.cs:304-318).
  - One shared world-space ParticleSystem, "ArcheryVolleySparks", made on first use with DontDestroyOnLoad (:241-242).
  - Fixed constants (:226-227): Life 0.35 s, Size 0.02 m, gravity 0.6, Stretch render mode, MaxParticles 2000.
  - Only colour, cone (`spread` degrees, clamped to 85), speed (each particle 0.5-1x) and size (`Size * sizeScale`) can be set per call. Lifetime cannot be set through Emit.
  - It returns quietly on a headless server (`System()` returns null, :239).
  - The material is the game's 'Sparks' particle material by name (`[Volley] SparkMaterial`), with the flat line material as fallback (:280-299).
  - The emitter GameObject is on layer 0 (the 100 m cull applies).
- Example (Shatter.cs:476): `VolleyFx.Sparks.Emit(back, axis, Mathf.Max(0, sparks.Value), spark, 25f, Mathf.Max(0f, sparkSpeed.Value));`
- Shard ring (Shatter.cs:477-484): a loop of `Emit(at, dirK, 1, colour, 4f, speed, size)` with `Fx.Basis` for the ring.

======================================================================
(3) Volley.Ghost: `internal class Ghost : MonoBehaviour`, nested in `internal static class Volley` (Q\Volley.cs:27, :1478)
======================================================================
- `internal static Ghost Create(Arrow arrow, int index, bool rampTestOk = true)` (Volley.cs:1489-1527)
  - Makes "ArcheryGhostArrow", a "shaft" child (`Fx.NewLine`, 2 points, width `[Volley] ShaftWidth` 0.012 x OuterScale when index >= VolleyMath.Inner) and a "head" child.
  - Each head mesh is copied relative to the tip. The copying pattern (1508-1520):
    ```
    foreach (MeshFilter mf in HeadMeshes(arrow, tip)) {
        if (mf.sharedMesh == null || mat == null) continue;
        var part = new GameObject(mf.name);
        part.transform.SetParent(headGo.transform, false);
        part.transform.localPosition = tip.InverseTransformPoint(mf.transform.position);
        part.transform.localRotation = Quaternion.Inverse(tip.rotation) * mf.transform.rotation;
        part.transform.localScale = mf.transform.lossyScale * (g.outer ? outerScale.Value : 1f);
        part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var mr = part.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        g.headParts.Add(mr);
    }
    g.headOffset = Quaternion.Inverse(Quaternion.LookRotation(arrow.transform.forward)) * tip.rotation;
    ```
- `internal static Ghost CreateSmall(Arrow arrow, string name)` (:1575).
- `internal void Resize(float headScale, float shaftMetres)` (:1584).
- `internal void Bigger(float k)` (Volley.cs:1728-1735): k times the ghost's ORIGINAL size, not cumulative.
  ```
  if (!(k > 0f) || k == size) return;
  float by = k / size; size = k;
  head.localScale = head.localScale * by;
  baseWidth *= by; glintScale *= by;
  ```
- Drawing:
  - `internal void Hold(Vector3 tip, Vector3 direction, float drawnLength, float alpha)` (:1892);
  - `internal void Fly(Vector3 tip, Vector3 direction, float length)` (:1895);
  - `internal void MoveTo(Vector3 tip, Vector3 velocity)` (:1905);
  - `internal void MoveAlong(Vector3[] points, int count, Vector3 velocity)` (:1913);
  - `internal void Fade(Vector3 tip, Vector3 direction, float length, float alpha)` (:1932);
  - `internal void Trail(float lengthFactor, float taper)` (:1739);
  - `internal void Style(bool isCharged, float bright)` (:1752) and `Style(in GoldHold.Tone look, float bright)` (:1756);
  - `internal float Green { get; set; }` (:1697) and `internal bool Homing` (:1717);
  - `internal void End()` (:1875): destroys the ghost.
  - All of them go through the private `Paint` (:1775-1804). The shaft colour is `GhostColour(tone.Gold, Green)`, and the head is shown only while tipAlpha > 0.5.
- Usage template (Q\SeekFx.cs:62-74, SeekReveal):
  ```
  g = Volley.Ghost.Create(arrow, 0, rampTestOk: false);
  g.Style(false, brightness);
  g.Homing = true;
  g.Bigger(Scale);
  ...
  ghost!.Fade(tip.position, arrow.transform.forward, Length, alpha);
  ```
- `static List<MeshFilter> HeadMeshes(Arrow arrow, Transform tip)` (PRIVATE, Volley.cs:1605-1634). Tries in order:
  1. LOD0 renderers under the tip;
  2. `HighestDetail(tip)`;
  3. the arrow's highest-detail meshes nearest the tip (`NearestTip`, HeadReach 0.25 m).
  It logs once per arrow kind.
- `static List<Renderer> HighestDetail(Transform root)` (PRIVATE, Volley.cs:1647-1658)
  - Every drawn MeshRenderer under `root` that is in LOD0 of some LODGroup, or in no LODGroup's lower LODs.
  - It uses `GetComponentsInChildren<MeshRenderer>(false)` and `Drawn(r)`.
  - `static bool Drawn(Renderer? r)` (:1637-1638): `r.enabled && r.gameObject.activeInHierarchy && r is MeshRenderer && r.GetComponentInParent<PooledObject>() == null`.
  - `CollectLods` is at :1660.
- A FULL copy of an arrow (shaft and feathers too). There is no such code today. To add it:
  - Iterate `HighestDetail(arrow.transform)`, which must become `internal`, or a copy.
  - Use the Create loop above with `arrow.transform` in place of `tip`.
  - Take the renderer's real materials with `mr.sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials`, not the ghost material.
  - runtime-looks.md:165-166 says lossyScale reproduces the plain Arrow exactly, because every mesh has identity rotation under its scaled parent.
  - The plain Arrow's parts (X\Resources\network prefabs\props\combat\Arrow.prefab:133-413): 'Mesh' > 'Spike' > metal_detail_spike_fancy_LOD0..2; 'Feather Blue' > spriggull_feather_bottom_LOD0..2; 'Arrow Shaft' > arrow_shaft_LOD0..2; 'Feather White' > spriggull_feather_bottom_LOD0..2.
  - The Arrow prefab hash is 56460 (Arrow.prefab:730; Q\Range.cs:91 `P("Arrow", 56460u)`). The Nail can therefore be read from `PrefabManager.GetPrefab(56460u)` instead of the caster's (possibly crafted) arrow.
  - Materials: Rust_m (Township Tale/Cheap/SimpleStandard, LA:1778) on the head; Wooden_Atlas_m (Hidden/SimpleStandardAtlasedTexcoord, LA:2046) on the shaft and feathers.
  - Arrow axis: `arrow.transform.forward` (as Create uses). Per-axis thickening = the parts under one container in the arrow's frame, scaled (x, y thickness; z length).
- The ghost head material: `internal static Material? Volley.ResolveHeadMaterial()` -> private `HeadMaterial()` (Volley.cs:1404-1436).
  - "skill" (the default, `[Volley] HeadMaterial`, Volley.cs:148):
    ```
    PooledObject prefab = GlobalSettings<ProgressionSettings>.Instance.ToolBasedEffect.Prefab;
    ToolBasedEffectBase effect = prefab != null ? prefab.GetComponent<ToolBasedEffectBase>() : null!;
    Material baseMat = effect != null ? Traverse.Create(effect).Field("baseMaterial").GetValue<Material>() : null!;
    if (baseMat != null) { headMaterial = new Material(baseMat); if (headMaterial.HasProperty("_Progress")) headMaterial.SetFloat("_Progress", 1f); }
    ```
  - Fallback: `new Material(Fx.LineMaterial()) { color = Colour }`.
  - The cache is cleared on `Cfg.Reloaded` (Volley.cs:155).
  - The shaft is the shared `Fx.LineMaterial()` with startColor/endColor.
- Tinted copy pattern (SeekLook.cs:313-320): `var m = new Material(own) { name = own.name + " (Archery homing arrow)" }; string props = Volley.HeavyLook.Tint(m, Colour);`. If props is "", destroy it.
- The Etheral-style flat colour:
  - The Etheral Arrow (hash 52366, X\...\combat\Etheral Arrow.prefab:454; Q\Range.cs:91) has its spike, shaft and feather renderers all on 'SelectedPiece' (guid 7cc7cfed..., Etheral Arrow.prefab:976-1515).
  - SelectedPiece is `Legacy Shaders/Particles/Additive` (LA:1789; X\Material\SelectedPiece.mat:11 builtin shader 200). It has no _MainTex texture (:22-23). `_TintColor` = (0, 0.586, 1, 0.5) (:59).
  - So the gold look = `new Material(selectedPiece)` + `SetColor("_TintColor", gold)` on copies of the Arrow's meshes.
  - Getting it:
    - `PrefabManager.GetPrefab(52366u).GetComponentsInChildren<MeshRenderer>(true)` and its sharedMaterial;
    - or `Resources.FindObjectsOfTypeAll<Material>()` with name == "SelectedPiece" (the Tablet.cs:969 pattern).
  - The ghost head's skill material is NOT this look.

======================================================================
(4) The SeekCage pooling pattern: `internal static class LockCage` (Q\SeekCage.cs:27)
======================================================================
- Constants (:28): `const int Cap = 12, Segments = 48`.
- Per-item record (:38-55): `sealed class Cage { public GameObject Go; public readonly Arc[] Arcs; public bool Busy; public uint Id; ... }`.
- Pool: `static readonly List<Cage> cages` (:57). Scratch: `static readonly Vector3[] points = new Vector3[Segments + 1]` (:58), so nothing is allocated per frame.
- Take, free, or build (:158-178):
  ```
  Cage? free = null;
  foreach (Cage c in cages) if (!c.Busy && c.Go != null) { free = c; break; }
  if (free == null) {
      cages.RemoveAll(c => c.Go == null);
      if (cages.Count >= Cap) return null;
      free = Build();
      cages.Add(free);
  }
  free.Busy = true; free.Id = id; ... // reset every field
  ```
- Release: `static void Free(Cage c)` (:180-184): `c.Busy = false; c.Root = null; if (c.Go != null && c.Go.activeSelf) c.Go.SetActive(false);`.
- Build (:238-271):
  - On first use, a host GameObject with `UnityEngine.Object.DontDestroyOnLoad(hostGo); host = hostGo.AddComponent<Host>();`.
  - Each item is parented under the host. Each line or glow sits on its own child; `glow.sortingOrder = -1` puts the glow under its line.
  - The item ends with `Go.SetActive(false)`.
- `static LineRenderer NewArc(GameObject go, Material? material)` (:273-280): `Fx.NewLine(go, 2, 0.02f, false)`, an optional sharedMaterial, `useWorldSpace = false` (the transform scales and turns it).
- Points are set only when they change (`SetArc`, :283-292): `lr.SetPositions(points)`.
- Drawing: `sealed class Host : MonoBehaviour` with `LateUpdate` inside a try/catch (:320-329). It loops over the busy items, frees finished ones, places them and paints width and colour (:331-388).
- Teardown:
  - `internal static void Clear()` (:73-76) destroys every Go;
  - `internal static void Reloaded()` (:70) retries the glow material.
- Glow material copy (:221-236): `(Material? m, string failed) = Fx.OrbLineMaterial(); var copy = new Material(m) { name = m.name + " (Archery lock cage)" }; Fx.ResetColours(copy);`.
- A simpler pool, one component per item: Ricochet.Arcs (Q\Ricochet.cs:611-697).
  - `PoolCap = 64`;
  - `Take()` (:663-673) makes `new GameObject` + DontDestroyOnLoad + `AddComponent<Arc>()` + `Build()`;
  - `Busy => gameObject.activeSelf` (:692);
  - `Build(){ line = Fx.NewLine(gameObject, JumpArc.Points, 0.01f, false); gameObject.SetActive(false); }` (:694-697).
- Scene handling:
  - Main overrides only `OnSceneWasInitialized(int, string)` (Q\Main.cs:135). No mod code overrides OnSceneWasUnloaded today.
  - Every pool host is DontDestroyOnLoad (SeekCage.cs:243, Ricochet.cs:669, VolleyFx.cs:242, Shatter.cs:528, Volley.cs:964, SeekLook.cs:472).

======================================================================
(5) Texture lookups
======================================================================
- `static Texture2D? FindTexture(string name, bool exact = false)` (PRIVATE in `internal static class Tablet`; Q\Tablet.cs:928-938).
  - It needs its own copy or must be made internal.
  ```
  static readonly int T_FindTexture = Timing.Section("asset Tablet.FindTexture");
  static Texture2D? FindTexture(string name, bool exact = false) {
      using var _t = Timing.Begin(T_FindTexture, name);
      Texture2D? loose = null;
      foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>()) {
          if (t == null) continue;
          if (t.name == name) return t;
          if (!exact && loose == null && t.name.StartsWith(name, StringComparison.Ordinal)) loose = t;
      }
      return loose;
  }
  ```
- Rune use: `static void Glyph()` (Tablet.cs:1098-1117).
  - Setting: `[Tablet] Glyph` default "Glyph_Ruins_Symbol_02" (:141).
  - The texture is cached by name (:1103): `if (glyphTexture == null || glyphFor != name) { glyphTexture = FindTexture(name); glyphFor = name; }`.
  - Then a Sprites/Default copy with `_MainTex`, a `Quad(colour)` mesh, `Flat(MeshRenderer, Material, int order)` (:656-661: sharedMaterial, no shadows, sortingOrder) and localScale size.
- Other names in Tablet:
  - `StoneTexture = "ruins_02_diffuse"`, `StoneEmissive = "ruins_02_emmissive"` (the meander band's map), `StoneMaterial = "Ruins_02_glow_m"` (Tablet.cs:26);
  - its material search is `Resources.FindObjectsOfTypeAll<Material>()` (:866).
- Glyph_Bow_LightBeam:
  - This is the Lightbeam slot's original icon (X\Resources\progressiontrees\Ranged Skill Tree.asset:43).
  - CAUTION: Tree.cs:374 puts that slot in `iconSlots["HolyArrow"]`, and `ApplyIcons` REPLACES its `slotIcon` with the mod's embedded HolyArrow.png (Tree.cs:812-824). Read it by texture name (FindTexture), not from `slot.SlotIcon`.
  - The mod's own HolyArrow.png is embedded as `QuickshotRing.Resources.HolyArrow.png`.
- Embedded PNG loading: `ApplyIcons()` (Q\Tree.cs:801-827). Core (806-821):
  ```
  using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"QuickshotRing.Resources.{pair.Key}.png");
  if (s != null) { png = new byte[s.Length]; s.Read(png, 0, png.Length); }
  ...
  // H3 section 4: no mip maps, as the game's own glyph textures
  var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
  if (!ImageConversion.LoadImage(tex, png)) { ...warn...; continue; }
  tex.name = "Archery_" + pair.Key;
  if (old != null) { tex.filterMode = old.filterMode; tex.wrapMode = old.wrapMode; tex.anisoLevel = old.anisoLevel; }
  if (!mipLogged) { mipLogged = true; if (Logs.Debug) MelonLogger.Msg($"... mipmapCount {tex.mipmapCount} ..."); }
  ```
  - Mip maps: the 4th constructor argument (`mipChain`) is `false` today. For Skyfall glyphs drawn small, pass `true`, and log `tex.mipmapCount` to confirm.
  - Embedding: Directory.Build.props:25-26 `<EmbeddedResource Include="Resources\**\*" ...>` embeds as `<AssemblyName>.Resources.<file name>`. A new PNG goes in Q\Resources\. Current files: AutoDock, Burst, HolyArrow, Quickdraw4, QuiverGather, Ricochet, SeekingVolley, Shatter, ThreeShot, TwoShot (.png).
  - Tree's usings include System.IO and System.Reflection.

======================================================================
(6) Particles_Combat_White_Hard, SpriteSheet_Combat, one cell on a quad
======================================================================
- The material: `Particles_Combat_White_Hard | Mobile/Particles/Additive` (LA:1701). The particle systems using it include Slashing_Cicrle (LA:1110-1116), Slashing_Circle, Slashing_Spike, Circle Hard and others (LA:792-1138).
- Its _MainTex is SpriteSheet_Combat: 2048x2048, 8x8 cells of 256 px, no mipmaps, not readable (runtime-looks.md:54-61).
  - Rows from the top: 0 starbursts, 1 spiked ring, 2 hard ring, 3 soft glow ring, 4 splatter.
- Route A, through a loaded effect (the Fx.OrbLineMaterial pattern, Fx.cs:319-325), using the always-loaded 'Easter Egg Confetti Big' (LA:121):
  ```
  PooledObjectDefinition? d = Fx.FindEffect("Easter Egg Confetti Big");
  if (d == null || d.Prefab == null) return null;
  foreach (ParticleSystemRenderer r in d.Prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
      if (r.sharedMaterial != null && r.sharedMaterial.name == "Particles_Combat_White_Hard") return r.sharedMaterial;
  ```
- Route B, by name over all particle renderers (Q\Balance.cs:55-57; VolleyFx.cs:282-284):
  `Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>().Where(r => r != null && r.sharedMaterial != null && r.sharedMaterial.name == "Particles_Combat_White_Hard").Select(r => r.sharedMaterial).FirstOrDefault();`
- Route C: `FindObjectsOfTypeAll<Material>()` by exact name. Beware a second material, "Particles_Combat_White_Hard Emmissive" (LA:1702).
- The texture: `material.mainTexture` (or `GetTexture("_MainTex")`), or `FindTexture("SpriteSheet_Combat", exact: true)`.
- About 60 game systems share this material, so always draw with `new Material(src) { name = "... (Archery Skyfall)" }`.
  - The shader has no colour property: tint through vertex colours.
  - For the alpha-blended under-layer: a `Sprites/Default` copy (`new Material(Fx.LineMaterial())`) with `SetTexture("_MainTex", sheet)`.
- Quad mesh with vertex colour: copy Tablet.Quad (Q\Tablet.cs:645-654), with the cell's UVs in place of 0..1.
  ```
  static Mesh Quad(Color colour) {
      var mesh = new Mesh { name = "Archery Tablet Quad" };
      madeMeshes.Add(mesh);
      mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
      mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
      mesh.colors = new[] { colour, colour, colour, colour };
      mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };   // seen from -z
      mesh.RecalculateBounds();
      return mesh;
  }
  ```
  - Cell (row r from the top, column c): u0 = c/8, u1 = (c+1)/8, v0 = 1-(r+1)/8, v1 = 1-r/8. The uv array becomes {(u0,v0),(u0,v1),(u1,v1),(u1,v0)} (runtime-looks.md:70).
  - Fade a quad by rewriting `mesh.colors` with the same array, or keep one mesh per alpha step.
  - Made meshes are tracked in `static readonly List<Mesh> madeMeshes` (Tablet.cs:200) and destroyed on teardown (:428-429).
- Alternative primitive with a MATERIAL colour (Q\Balance.cs:122-137): `GameObject.CreatePrimitive(PrimitiveType.Quad)`, then `DestroyImmediate(collider)`, then a material copy.
  - Unsuitable for Mobile/Particles/Additive, which has no colour property.
  - The material copy is destroyed on removal (Balance.cs:249).
- Firework colour (if used): `public void SetColor(Color color, bool isIgnoringAlpha = false)` (D\ColoredParticleSystem.cs:140).

======================================================================
(7) The dynamite dome (hash 31326): read only, never instantiated
======================================================================
- API (D\Alta.Networking\PrefabManager.cs):
  - `public static class PrefabManager` (:9);
  - `public static NetworkPrefab GetPrefab(uint hash)` (:88-92) calls PrepareSpawnSetups, then a dictionary lookup;
  - `public static NetworkPrefab GetPrefab(string name)` (:94-98);
  - `public static bool Exists(uint hash)` (:100).
  - `NetworkPrefab : NetworkEntityParent` (D\Alta.Networking\NetworkPrefab.cs:16) is a component, so GetComponentInChildren works. `using Alta.Networking;`.
  - On headless, renderers lose their materials and MeshFilters lose their meshes (:24-27, :32-47). Client only.
- Mod usage: `NetworkPrefab prefab = PrefabManager.GetPrefab(hash); if (prefab == null) {...}` (Q\TestTools.cs:386-387; TestRange.cs:283).
- Hierarchy (X\Resources\network prefabs\props\Dynamite.prefab):
  - 'ExplosionRadius' GameObject (:352, inactive :357, scale 1.36 :6229) carries `ExplosionRadius` (global namespace, D\ExplosionRadius.cs:4): explosionRadius 4, scaleMultiplier 0.34, scaler = RadiusVisual (:6247-6249).
  - 'RadiusVisual' is its only child (:6231, localPos y -0.1 :6258).
  - RadiusVisual has a MeshFilter (mesh 'sphereInsideOut_test') and a MeshRenderer (material StylizedWater_DYNAMITE, shader `Unlit/ExplosionRadius`, LA:1868).
- Reading pattern (the Tablet.cs:952-958 read-only rule):
  ```
  NetworkPrefab p = PrefabManager.GetPrefab(31326u);
  ExplosionRadius er = p != null ? p.GetComponentInChildren<ExplosionRadius>(true) : null;
  MeshFilter mf = er != null ? er.GetComponentInChildren<MeshFilter>(true) : null;   // RadiusVisual
  MeshRenderer mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
  Mesh dome = mf.sharedMesh; Material src = mr.sharedMaterial;   // never modify; copy: new Material(src)
  ```
  - `Traverse.Create(er).Field("scaler").GetValue<Transform>()` also reaches RadiusVisual.
- Material properties (runtime-looks.md:91-97): `_EdgeColor` (1, 0.476, 0), `_EdgeFactor` 0.7, `_Cutoff` 0, `_ScrollSpeed` (1,0,0), `_RadiusSqrd` 8.8; `_EdgeTex` is unset.
- Size:
  - The mesh is about 2.97 m in radius (X\Mesh\sphereInsideOut_test.asset:153-155).
  - In the game the dome's world scale is ExplosionRadius 1.36 x scaler (4 x 0.34 = 1.36, set each LateUpdate, D\ExplosionRadius.cs:56), about 1.85. That gives about 5.5 m, not measured.
  - For a mod-owned object, scale = R / 2.97.
- Why never instantiate it: `OnEnable` unparents (D\ExplosionRadius.cs:22-25), and `LateUpdate` destroys the object when `cachedParent == null` (:48-53).

======================================================================
(8) The pooled 'Larger TextPopup'
======================================================================
- Definition: `GlobalSettings<PickupTypeSettings>.Instance.LargerTextPopup`.
  - `public PooledObjectDefinition LargerTextPopup => largerTextPopup;` (D\PickupTypeSettings.cs:184); also `SimpleTextPopup` (:182).
  - `public class PickupTypeSettings : GlobalSettings<PickupTypeSettings>` (:10, global namespace; GlobalSettings is in `Alta.Global`).
  - Mod precedent: `GlobalSettings<PickupTypeSettings>.Instance.PickupMovingGhost.Get(pickup.transform)` (Q\AutoDock.cs:692; QuiverGather.cs:239).
  - Also findable as `Fx.FindEffect("Larger TextPopup")` (LA:212).
- `public class TextPopup : MonoBehaviour` in `namespace Alta.Utilities` (D\Alta.Utilities\TextPopup.cs:4-6):
  - `public PooledObject PooledObject { get; set; }` (:23), set in Awake from `GetComponent<PooledObject>()`;
  - `public void UpdateText(string textToDisplay)` (:86) uses red;
  - `public void UpdateText(string textToDisplay, Color color)` (:91-95).
- Show and release:
  ```
  PooledObject po = GlobalSettings<PickupTypeSettings>.Instance.LargerTextPopup.Get(pos, rot);
  var tp = po.GetComponent<Alta.Utilities.TextPopup>();
  tp.UpdateText("OUT!", colour);
  ... later: po.Release();
  ```
  - `public virtual void Release()` (D\PooledObject.cs:22-32). It destroys the object if it has no pool.
  - `public bool IsReleased { get; private set; }` (:9).
  - `public event PoolChangedHandler Released` / `Requested` (:11-13).
- Game example: D\InteractableInspector.cs:76 (`...LargerTextPopup.Get(base.transform).GetComponent<TextPopup>(); text.transform.localScale = Vector3.one;`) and :86 (`text?.PooledObject.Release();`).
- Nothing times it out: the caller must Release it.
- Behaviour (TextPopup.cs:60-75):
  - Each Update, while `PlayerController.Current.Head` is within sqrt(maxDistance*1.1) (maxDistance 10 is compared to a squared distance, so about 3.3 m), it moves up by upSpeed (0 in this prefab, X\GameObject\Larger Text Popup.prefab:86-87), turns its forward toward the head and sets scale from a curve.
  - Beyond that range it disables itself until the player moves back within about 3.16 m (:51-58).
  - Canvas scale 0.005 (:133), font 60 (:254), material PopUpOverlay = UI/Default_OverlayNoZTest (LA:1718).
- Local head on a client:
  - `PlayerController.Current?.Head` (as TextPopup.cs:30);
  - mod style: `Targets.HeadOf(Player.Current as Player)` -> `Vector3?` (Q\Targets.cs:142-146; used at Claims.cs:149).

======================================================================
(9) DayTimeManager (sky dial)
======================================================================
- `namespace Alta.Environmental.Atmosphere; [ExecuteInEditMode] public class DayTimeManager : AtmospherePlugin` (D\Alta.Environmental.Atmosphere\DayTimeManager.cs:12-15). Private members:
  - `private Material skyBoxMaterial;` (:194);
  - `private LerpingFloatValue LightIntensityModifier;` (:198);
  - `private LerpingFloatValue skyColorFade;` (:222).
- Setup (`SetupVariableLerpers`, :329-338): LightIntensityModifier.Snap(1f); skyColorFade.Snap(0f); `skyColorFade.FinishedLerp += DisableSkyColor`.
  - `DisableSkyColor` (:354-367) enables `_DISABLED` on the skybox when `Current > 0.99`. Keep the fade at 0.95 or less.
- Each LateUpdate (:476-488) advances both lerpers when `IsInitialized`.
  - `UpdateLightIntensity(LightIntensityModifier.Current)` is re-applied every frame only while `IsEnabled` (:659-682), so a lerped value stays.
- The game's own writers:
  - `protected override void Enabled()` (:696-717): Lerp(1f, manager.AmbienceLerpTime), clears _DISABLED, then skyColorFade.Lerp(0f, 0.5f);
  - `public override void Disable()` (:763-771), the cave: Lerp(0f, ...), skyColorFade.Lerp(1f, 0.5f).
  - Headless: Awake disables the component (:287-293).
- `public class LerpingFloatValue`, namespace Alta.Utilities (D\Alta.Utilities\LerpingFloatValue.cs:6):
  - `public bool IsLerping { get; private set; }` (:20);
  - `public float Current => current;` (:22);
  - `public event Action FinishedLerp;` (:24);
  - `public void Update()` (:31);
  - `public void Lerp(float target, float lerpSpeed)` (:57): lerpSpeed is the DURATION in seconds (progress += dt/lerpSpeed, :35);
  - `public void Lerp(Func<float> getTarget)` (:63);
  - `public void Snap(float newTarget)` (:71);
  - `public void SnapIfNotLerping(float)` (:80);
  - `public void Kill()` (:88).
- Reaching it:
  - `public static AtmosphereManager instance;` (AtmosphereManager.cs:17, set at :85 and :122);
  - `public AtmospherePlugin[] AtmospherePlugins => availablePlugins;` (:58);
  - pick the `DayTimeManager` from that array (`OfType<DayTimeManager>()`).
  - The game itself also uses `Object.FindObjectOfType<DayTimeManager>()` (PrefabSceneSettings.cs:160): a scene search, so do it once and cache.
  - `AtmospherePlugin` (abstract MonoBehaviour, AtmospherePlugin.cs:5): `public bool IsEnabled { get; private set; }` (:35), `public bool IsInitialized { get; protected set; }` (:60), `public Color? OverrideAmbience { get; set; }` (:31).
- Traverse pattern already in the mod (`using HarmonyLib;`):
  - instance field: Fx.cs:255 `HarmonyLib.Traverse.Create(e).Field("baseMaterial").GetValue<Material>()`; Volley.cs:1417; AutoDock.cs:421 `Traverse.Create(area).Field("shrine").GetValue<SkillShrine>()`;
  - static field: Tree.cs:234 `Traverse.Create(typeof(ProfessionSkillTree)).Field("treeMap").GetValue<Dictionary<...>>()`;
  - set: Tree.cs:615 `Traverse.Create(slot).Field("cost").SetValue(cost)`.
  - For the dial: `Traverse.Create(dtm).Field("LightIntensityModifier").GetValue<LerpingFloatValue>()` and `.Field("skyColorFade").GetValue<LerpingFloatValue>()`, then `.Lerp(0.5f, 2f)` / `.Lerp(0.6f, 2f)`.
  - Restore with `Lerp(1f, s)` / `Lerp(0f, s)` only when `dtm != null && dtm.IsEnabled`.
- No mod file touches DayTimeManager or AtmosphereManager today (grep).

======================================================================
(10) Layers
======================================================================
- Layer indices (C:\ATTUnity\ExportedProject\ProjectSettings\TagManager.asset `layers:`): 0 Default, 1 TransparentFX, 2 Ignore Raycast, 4 Water, 5 UI, 8 Terrain, 9 Player, 10 Ground, ..., 16 BlockTeleport, 17 HiddenFromPlayerHMD.
- Culling (D\PlayerController.cs:233-239, `UpdateCameraCulling`): `float[] array = new float[32]; array[0] = settings.DefaultCullingDistance; Camera.layerCullDistances = array; Camera.layerCullSpherical = true;`.
  - The default is 100 m (D\Alta.Utilities\GameSettings.cs:115).
  - Only layer 0 is culled by distance. Layer 1 uses the far clip (2200 m, runtime-looks.md:170).
  - The eye camera's mask excludes only layer 17 (runtime-looks.md:172).
- The mod NEVER assigns a layer today: no `.layer =` in Q\*.cs. It only reads layers:
  - `LayerMask.LayerToName(c.gameObject.layer)` (Draws.cs:630, Ricochet.cs:145);
  - `LayerMask.NameToLayer("BlockTeleport")`, cached in a static int (Seek.cs:557).
- In Unity, `new GameObject()` starts on layer 0 and does not inherit its parent's layer. Every GameObject that holds a renderer (each line child, glow child and copied mesh part) needs `go.layer = 1` (or `LayerMask.NameToLayer("TransparentFX")`).
- Pooled game effects keep their prefab's layer (Firework is on layer 8, runtime-looks.md:123). The shared spark emitter is on layer 0.

======================================================================
(11) ApplicationManager.IsHeadless
======================================================================
- `public class ApplicationManager : SingletonBehaviour<ApplicationManager>` (global namespace, D\ApplicationManager.cs:11).
- `public static bool IsHeadless { get; }` (:37-48): a cached `CommandLineArguments.Contains("-batchmode") || ...("-nographics")`.
- Not to be confused with `Discord.ApplicationManager` (D\Discord\ApplicationManager.cs).
- Used 86 times in Q. Typical guards: `if (ApplicationManager.IsHeadless) return;` (VolleyFx.cs:52, 63, 239, 350; Fx.cs:195; Tree.cs:803).
- Main.OnLateUpdate returns early on headless at Q\Main.cs:216. Client looks go after it.

## Gaps
Not found, or not verified:

1. **A full-arrow copy.** No existing code copies a whole arrow's renderers (shaft, feathers) with their real materials. Only head copies exist (Volley.Ghost.Create).
   - `HighestDetail` and `Drawn` are private to Ghost.
   - `Drawn` requires `r.enabled && r.gameObject.activeInHierarchy`. I did not verify that this returns true for a prefab ASSET from `PrefabManager.GetPrefab(56460u)`, which is not in a scene. When reading from the prefab, use `GetComponentsInChildren<MeshRenderer>(true)` and skip the activeInHierarchy test.
   - Shear from a non-uniformly scaled container over the rotated 'Spike' child was not checked.

2. **Etheral material at runtime.**
   - 'SelectedPiece' (Legacy Shaders/Particles/Additive, `_TintColor`) comes from the AssetRipper export and LA:1789. It was not read live.
   - I did not check that `PrefabManager.GetPrefab(52366u)`'s renderers still carry it on a client.
   - How `_TintColor` alpha and vertex colours combine on the Arrow's meshes was not seen.

3. **Shader facts from Unity's public built-in source, not checked locally** (that source is not on this machine):
   - Mobile/Particles/Additive is Cull Off and multiplies texture by vertex colour, with no tint property;
   - Sprites/Default is Cull Off and alpha-blended, with `_Color` x vertex colour.

4. **Glyph_Bow_LightBeam.**
   - Tree.ApplyIcons replaces the Lightbeam slot's slotIcon with the mod's HolyArrow.png (Tree.cs:374, 812-824).
   - Finding the original texture by name relies on it staying loaded after the swap, for example not being dropped by an UnloadUnusedAssets. Not verified.

5. **Mipmaps on embedded PNGs.** The code uses `new Texture2D(2, 2, RGBA32, false)` (Tree.cs:820). Whether `ImageConversion.LoadImage` keeps or generates a mip chain when constructed with `mipChain: true` was not checked here; log `tex.mipmapCount` to confirm.

6. **The dome.**
   - Hash 31326 and the hierarchy come from the export only, not checked in game.
   - How 'Unlit/ExplosionRadius' uses `_RadiusSqrd` and `_EdgeFactor` at other scales is unknown (stub shader only), as is whether it culls faces.

7. **The TextPopup's curve.** `scaleBasedOnDistance` was not read. Readability of "OUT!" is unverified.

8. **DayTimeManager in play.** I did not verify that a scene change or disconnect destroys DayTimeManager (no DontDestroyOnLoad was found). The mod has no OnSceneWasUnloaded override today; Main overrides only OnSceneWasInitialized (Main.cs:135).

9. **Layer 1 culling.** That layer 1 is never distance-culled follows from `new float[32]` with only index 0 set (PlayerController.cs:235-236). The user's actual DefaultCullingDistance (stored under %APPDATA%, not opened) and whether spherical culling measures from the bounds' centre are unverified.

10. **Out of scope.** TavernLib and TavernVoice were not opened: nothing in the eleven client-look items needed them.

11. **Nothing was built, run or edited.**
