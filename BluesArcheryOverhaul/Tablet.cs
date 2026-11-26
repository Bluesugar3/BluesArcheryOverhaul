using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Client: the test tablet at the archery shrine. A carved stone slab (raised frame, recessed writing face,
    // chamfered blocks textured from the game's ruins atlas) carried by the game's own crystals: a gem in the claw of
    // the shrine's U-shaped stone cradle, four shards at the slab's corners, splinters drifting round the column of
    // air between them, and a few stone chips. The shapes and the arithmetic are in Lectern.cs (tested).
    //
    // Every client near the shrine has it (since 0.13.0). While nobody has the archery tree open it is dormant: the
    // slab lies small in the cradle with the crystals folded round it, the gem dim, no lettering (RestLayout). When
    // someone opens the tree it rises and unfolds (Raise), and sinks back when the tree closes. Its face says, for
    // the player who opened the tree, what the held orb would do to their build (TabletText, Preview.cs); for
    // everyone else, whose build it is and what this client knows of it.
    //
    // Everything shown is the mod's own GameObjects. The game's crystal meshes and material, the shrine's stones and
    // the font are used as they are (shared, never changed, never destroyed); the stone is ONE copy of the game's
    // ruins material, the lettering ONE material of the mod's on the font's atlas, the gem ONE copy of the crystal
    // material (so it can be dimmed). Geometry is built once per shrine (and on a settings reload), text only on
    // ShrineView.Changed, and the only per-frame work is Drive(): cached transforms set from the mod's own clock
    // while the tablet is raised or moving; dormant, nothing. Everything goes when the shrine goes.
    internal static class Tablet {
        const string StoneTexture = "ruins_02_diffuse", StoneEmissive = "ruins_02_emmissive", StoneMaterial = "Ruins_02_glow_m";
        const string StoneShader = "Township Tale/Cheap/SimpleStandard";
        const string CrystalMaterial = "Crystals Dynamic_Atlas_m";
        const string ShardPrefab = "network prefabs/crafting/combat/spikes/Crystal Shard Blue", GemPrefab = "network prefabs/props/mining/Crystal Gem Blue";
        static readonly string[] ShardMeshes = { "crystal_shard_Atlased", "crystal_shard" }, GemMeshes = { "crystal_gem_Atlased", "crystal_gem" };
        const string VinquePath = "fonts/vinque/vinque", VinqueName = "vinque";
        const int FontPx = 64;

        // The shrine's U-shaped cradle: two crossed stone arcs on the hand-stone deck, to the right of the tree
        // (network prefab 'Profession Upgrade Shrine' > Hand Stone Deck > Profession Handstone Deck Dynamic Content >
        // Meshes > professionShrine_handStoneDeck_arc_grp, twice, on one pivot). The arcs' mesh as the game has it,
        // used when it cannot be read: metres below and above the pivot, and half its width.
        const string CradleDefault = "professionShrine_handStoneDeck_arc";
        const float ArcBelow = 0.307f, ArcAbove = 0.042f, ArcHalf = 0.266f;
        const float CradleCluster = 0.3f, NearMatch = 0.75f;
        static readonly Vector3 OldSpot = new Vector3(1.05f, 0f, -0.77f), CradleExpected = new Vector3(1.24f, -0.81f, -0.34f);
        // Depth-tested shaders that draw a font atlas, in order of preference (TextMaterial says why each suits).
        static readonly string[] AutoTextShaders = { "UI/Default", "Sprites/Default" };
        const float LookSeconds = 0.2f;

        // ---------------------------------------------------------------- settings ([Tablet], none shared)

        static MelonPreferences_Entry<bool> enabled = null!, facePlayer = null!, solid = null!, light = null!, motion = null!, crystals = null!, restVisible = null!;
        static MelonPreferences_Entry<string> offset = null!, baseSize = null!, slabColour = null!, slabMaterial = null!, stoneTint = null!, glowColour = null!,
            fontName = null!, textShader = null!, textColour = null!, carveColour = null!, plusColour = null!, warnColour = null!, footerColour = null!, noteColour = null!,
            ruleColour = null!, glyphName = null!, lightColour = null!, testNote = null!,
            cradleName = null!, cradleSpot = null!, cradleBase = null!, readerSpot = null!;
        static MelonPreferences_Entry<float> yaw = null!, tilt = null!, width = null!, height = null!, thickness = null!, frameWidth = null!, frameDepth = null!, topRail = null!,
            footRail = null!, bevel = null!, sink = null!, socketHeight = null!, floorBelowTree = null!, floatHeight = null!, bobHeight = null!, bobSeconds = null!, swayDegrees = null!,
            gemGap = null!, gemScale = null!, gemTurnSeconds = null!, shardGap = null!, shardScale = null!, shardTilt = null!, splinterScale = null!, orbitRadius = null!,
            orbitSeconds = null!, lightIntensity = null!, lightRange = null!, glowStrength = null!, textSize = null!, titleScale = null!, noteScale = null!, minLineScale = null!,
            margin = null!, textLift = null!, carveOffset = null!, ruleWidth = null!, glyphSize = null!, glyphAlpha = null!,
            cradleFloor = null!, cradleRadius = null!, cradleGemGap = null!, floorStart = null!, floorTolerance = null!,
            riseSeconds = null!, sinkSeconds = null!, textAppears = null!, restHeight = null!, restSlabScale = null!, restSlabTilt = null!, restGemScale = null!,
            restShardScale = null!, restSplinterScale = null!, restSplinterRadius = null!, restChipScale = null!, restChipRadius = null!, restLight = null!;
        static MelonPreferences_Entry<int> shardCount = null!, splinterCount = null!, fragmentCount = null!, maxLines = null!;

        internal static void Init() {
            var cat = Cfg.Category("Tablet");
            MelonPreferences_Entry<float> F(string name, float value, string description) => cat.CreateEntry(name, value, description: description);
            MelonPreferences_Entry<string> S(string name, string value, string description) => cat.CreateEntry(name, value, description: description);
            MelonPreferences_Entry<int> I(string name, int value, string description) => cat.CreateEntry(name, value, description: description);
            MelonPreferences_Entry<bool> B(string name, bool value, string description) => cat.CreateEntry(name, value, description: description);

            enabled = B("Enabled", true, "The stone tablet at the archery shrine (a test aid). Every player near the shrine sees it.");
            offset = S("Offset", "auto", "auto: the tablet's base sits in the shrine's U-shaped stone cradle, right of the tree. Else three numbers, a free-standing plinth: metres to the right, up from the floor found under it, and forward (away from you), measured from the foot of the skill tree as you face it (it used to stand at 1.05, 0, -0.77).");
            cradleName = S("CradleName", CradleDefault, "Start of the name of the shrine's objects that make the cradle (Offset = auto).");
            cradleSpot = S("CradleSpot", "1.24, -0.81, -0.34", "Where the cradle's middle is expected: metres to the right, up and forward from the foot of the skill tree. Used to pick the nearest match when nothing has that name, and as the spot itself when nothing matches.");
            cradleFloor = F("CradleFloor", 0.45f, "Height of the floor of the cradle's hollow (the block in its middle), as a share of the cradle's height.");
            cradleRadius = F("CradleRadius", 0.62f, "Radius of the cradle's hollow, as a share of the cradle's half width.");
            cradleBase = S("CradleBase", "0.20, 0.05, 0.20", "The seat stone in the cradle's hollow: width, height, depth in metres (made smaller if the hollow is).");
            cradleGemGap = F("CradleGemGap", 0.06f, "Metres the anchor gem hovers above the seat stone in the cradle.");
            floorBelowTree = F("FloorBelowTree", 1.15f, "Metres from the foot of the skill tree down to the shrine floor: where the floor is looked for under a free-standing plinth, and the floor's height when none is found.");
            floorStart = F("FloorStart", 0.35f, "The floor is looked for from this many metres above where it is expected (nothing higher can be mistaken for it).");
            floorTolerance = F("FloorTolerance", 0.5f, "... and down to this many metres below it.");
            facePlayer = B("FacePlayer", true, "The tablet turns to the player who opens the tree as it rises. Off: it always faces the spot in front of the tree (ReaderSpot).");
            readerSpot = S("ReaderSpot", "0, 0, -1.1", "Where a reader stands, as right, up, forward from the foot of the skill tree: the tablet faces it while dormant.");
            yaw = F("Yaw", 0f, "Degrees the tablet is turned about the vertical, added to the facing above.");
            tilt = F("Tilt", 18f, "Degrees the slab leans back (play-test 7: was 25).");
            width = F("Width", 0.42f, "Slab width in metres.");
            height = F("Height", 0.30f, "Slab height in metres.");
            thickness = F("Thickness", 0.06f, "Slab thickness in metres.");
            frameWidth = F("FrameWidth", 0.022f, "Width of the raised frame's side rails in metres.");
            frameDepth = F("FrameDepth", 0.008f, "Metres the frame stands proud of the writing face.");
            topRail = F("TopRail", 0.13f, "Height of the frame's top rail (the glowing meander band), as a share of the slab's height.");
            footRail = F("FootRail", 0.09f, "Height of the frame's bottom rail (the brick row), as a share of the slab's height.");
            bevel = F("Bevel", 0.006f, "Chamfer on every stone edge in metres.");
            solid = B("Solid", false, "Give the stone parts colliders. Off: nothing to trip over.");
            baseSize = S("BaseSize", "0.34, 0.10, 0.26", "The free-standing plinth (Offset not auto): width, height, depth in metres.");
            sink = F("Sink", 0.02f, "Metres the plinth or the seat stone is sunk into what it stands on.");
            socketHeight = F("SocketHeight", 0.035f, "Height of the carved socket block on the free-standing plinth (0 = none).");
            floatHeight = F("FloatHeight", 1.15f, "Metres from the shrine floor to the slab's centre when it floats.");
            motion = B("Motion", true, "The slab, crystals and stone chips drift. Off: everything rests at its mid position.");
            bobHeight = F("BobHeight", 0.015f, "Metres the slab rises and falls.");
            bobSeconds = F("BobSeconds", 5f, "Seconds per rise and fall.");
            swayDegrees = F("SwayDegrees", 0.6f, "Degrees the slab sways.");
            crystals = B("Crystals", true, "The game's crystals around the tablet. Off: the slab and the stone chips only.");
            gemGap = F("GemGap", 0.10f, "Metres the anchor gem hovers above the free-standing plinth.");
            gemScale = F("GemScale", 0.9f, "Size of the anchor gem (1 = the game's own size).");
            gemTurnSeconds = F("GemTurnSeconds", 24f, "Seconds per turn of the anchor gem.");
            shardCount = I("ShardCount", 4, "Crystal shards carrying the slab's corners (0-4).");
            shardGap = F("ShardGap", 0.035f, "Metres of air between a carrier shard and the slab's edge.");
            shardScale = F("ShardScale", 1f, "Size of the carrier shards.");
            shardTilt = F("ShardTilt", 8f, "Degrees the carrier shards lean outward.");
            splinterCount = I("SplinterCount", 5, "Small shards drifting round the column between gem and slab (0-12).");
            splinterScale = F("SplinterScale", 0.45f, "Size of the drifting splinters.");
            orbitRadius = F("OrbitRadius", 0.16f, "Radius of the splinters' ring in metres.");
            orbitSeconds = F("OrbitSeconds", 30f, "Seconds per turn of the splinters' ring.");
            light = B("Light", true, "A soft light at the anchor gem (no shadows).");
            lightColour = S("LightColour", "#3FB4FF", "Colour of the gem's light.");
            lightIntensity = F("LightIntensity", 0.9f, "Brightness of the gem's light.");
            lightRange = F("LightRange", 1.2f, "Reach of the gem's light in metres.");
            fragmentCount = I("FragmentCount", 4, "Small stone chips hovering below and behind the slab (0-6).");
            slabMaterial = S("SlabMaterial", "", "Empty: the game's ruins stone. Else the name of a loaded game material to copy for the stone.");
            slabColour = S("SlabColour", "#1B1E24", "Plain slab colour, used only when the game's stone is not found.");
            stoneTint = S("StoneTint", "#9EADCC", "Colour the stone is multiplied by (darker lets pale lettering read).");
            glowColour = S("GlowColour", "#78DCFF", "Glow of the meander band's channel and colour of the rune.");
            glowStrength = F("GlowStrength", 1.2f, "Strength of the meander band's glow (0 = none).");
            fontName = S("Font", "", "Empty: the game's Vinque. Else the name of a loaded font.");
            textShader = S("TextShader", "auto", "auto: the lettering is hidden behind anything in front of it (hands, the bow, the slab itself), on the first shader the game has that can draw the font that way. A shader name: that shader. font: the font's own material, which draws over everything.");
            textSize = F("TextSize", 0.014f, "Height of the body lettering in metres.");
            titleScale = F("TitleScale", 1.45f, "Title size, as a multiple of the body lettering.");
            noteScale = F("NoteScale", 0.7f, "Test note size, as a multiple of the body lettering.");
            minLineScale = F("MinLineScale", 0.7f, "A long line shrinks to this share of its size before it is cut in two.");
            margin = F("Margin", 0.012f, "Metres between the frame and the lettering, left and right.");
            textLift = F("TextLift", 0.001f, "Metres the lettering is lifted off the writing face.");
            textColour = S("TextColour", "#EEE9DC", "Lettering.");
            carveColour = S("CarveColour", "#12161E", "The dark copy behind each line that makes it read as cut into the stone.");
            carveOffset = F("CarveOffset", 0.0012f, "Metres the dark copy sits down and right of the line (0 = none).");
            plusColour = S("PlusColour", "#FFD678", "Lines that start with '+ ' (what buying the orb adds).");
            warnColour = S("WarnColour", "#FFB000", "Lines that say the orb changes your main path, and what you lose.");
            footerColour = S("FooterColour", "#BEE1FF", "The footer with the orb counts.");
            noteColour = S("NoteColour", "#96A0B2", "The test note.");
            ruleColour = S("RuleColour", "#96A5BE", "The thin rule under the title.");
            ruleWidth = F("RuleWidth", 0.0008f, "Thickness of the rule under the title in metres (0 = none).");
            glyphName = S("Glyph", "Glyph_Ruins_Symbol_02", "The game's rune texture in the face's top-right corner (empty = none).");
            glyphSize = F("GlyphSize", 0.03f, "Size of the rune in metres.");
            glyphAlpha = F("GlyphAlpha", 0.55f, "Opacity of the rune (0-1).");
            maxLines = I("MaxLines", 8, "Most lines shown at once.");
            testNote = S("TestNote", "","The small last line (empty = none).");
            // G3: [Tablet] CannotBuy and LegendaryOwned are retired: the reason line comes from BuyBlock (Preview.cs)
            restVisible = B("RestVisible", true, "While nobody has the archery tree open the tablet rests, small and dim, in the cradle. Off: it is hidden then.");
            riseSeconds = F("RiseSeconds", 1.4f, "Seconds the tablet takes to rise out of the cradle and unfold when someone opens the tree.");
            sinkSeconds = F("SinkSeconds", 1.0f, "Seconds it takes to fold and sink back when the tree is closed.");
            textAppears = F("TextAppears", 0.7f, "How far through the rise (0-1) the lettering shows. It goes dark the moment the tree closes.");
            restHeight = F("RestHeight", 0.18f, "Dormant: everything lies below this many metres above the floor of the cradle's hollow.");
            restSlabScale = F("RestSlabScale", 0.42f, "Dormant: the slab's size (1 = full). It is made smaller still if the cradle needs it.");
            restSlabTilt = F("RestSlabTilt", 78f, "Dormant: degrees the slab leans back (90 = lying flat, face up).");
            restGemScale = F("RestGemScale", 0.45f, "Dormant: the gem's size, as a share of its floating size.");
            restShardScale = F("RestShardScale", 0.5f, "Dormant: the carrier shards' size, as a share (they lie folded along the slab's sides).");
            restSplinterScale = F("RestSplinterScale", 0.4f, "Dormant: the splinters' size, as a share.");
            restSplinterRadius = F("RestSplinterRadius", 0.07f, "Dormant: radius of the splinters' drawn-in ring in metres.");
            restChipScale = F("RestChipScale", 0.6f, "Dormant: the stone chips' size, as a share.");
            restChipRadius = F("RestChipRadius", 0.10f, "Dormant: how far from the middle the stone chips lie, in metres.");
            restLight = F("RestLight", 0.15f, "Dormant: the gem's light and glow, as a share of full (0-1).");

            if (ApplicationManager.IsHeadless) return;
            ShrineView.Changed += () => textDirty = true;
            Cfg.Reloaded += () => { geometryDirty = true; textDirty = true; };
            Font.textureRebuilt += FontRebuilt;
        }

        // ---------------------------------------------------------------- state

        static bool textDirty, geometryDirty;
        static GameObject? root, face;
        static SkillShrine? builtFor;
        static Transform? turnT, tabletT, gemT;
        static readonly Transform?[] shardT = new Transform?[FloatLayout.MaxShards], splinterT = new Transform?[FloatLayout.MaxSplinters],
                                     fragmentT = new Transform?[FloatLayout.MaxFragments];
        static int shards, splinters, fragments;
        static FloatSpec spec;
        static RestSpec rest;
        static bool moving;

        // The rise and the sink. raised: 0 = dormant in the cradle .. 1 = floating; it runs evenly with time toward
        // wantUp (Raise.Step) on every frame the tablet exists, so it can never stop half-way. posed: the raised value
        // the pieces were last set for (-1 = not yet). clock: the drift's own time, standing still while dormant.
        static float raised, posed = -1f, clock, nextLook;
        static bool wantUp, openerMine;
        static string openerShown = "";
        // the stand turning to its reader
        static Quaternion turnFrom, turnTo;
        static float turnK;
        static bool turning;
        // where things are, kept from the build for the facing
        static Vector3 standAt, readerAt, treeForward;
        // the gem's light and glow
        static Light? lamp;
        static float lampFull, glowFull, glowShown = -1f;
        static int emissionStrength;
        static bool rootShown = true;

        // what this build made and must destroy (never anything of the game's)
        static readonly List<Mesh> madeMeshes = new List<Mesh>();
        static Material? stoneCopy, glyphCopy, textCopy, gemCopy;

        // the writing face
        static Font? font;
        static Material? textMaterial;
        static TabletMetrics metrics;
        static float faceLeft, faceTop, faceZ;
        static readonly List<TextMesh> inks = new List<TextMesh>(), carves = new List<TextMesh>();
        static Transform? rule;
        static string shownText = "";
        static readonly Func<string, float> emWidth = EmWidth;
        // lettering: textOn = the rise is far enough and the tree is open; front = the camera is on the face's side
        static bool textOn, front = true, faceShown;
        // another player's build as it was last written (their client's bits can arrive after the tree opened)
        static bool otherKnown;
        static BluesArcheryOverhaul.Build otherBuild;
        // true while Build() poses the pieces: the text is written right after, so the try-out line is not judged yet
        static bool building;
        // the tree's foot as it stood when the tablet was built (a shrine still settling into place is built again)
        static Transform? builtTree;
        static Vector3 builtTreeAt;

        // the game's assets, found once and kept (Unity nulls them if the game unloads them)
        static Mesh? shardMesh, gemMesh;
        static Material? shardMaterial, gemMaterial, stoneSource;
        static Texture2D? glyphTexture;
        static string shardHow = "", gemHow = "", stoneHow = "", glyphFor = "", stoneFor = "\0";
        static readonly HashSet<string> said = new HashSet<string>();

        /// <summary>True while the tablet is up (or rising) for the local player's own open tree.</summary>
        internal static bool Shown => root != null && wantUp && openerMine;

        // The orb whose try-out line the fitted text carries (Wording); Tutorial floats the line whenever this is not so.
        static Orb carriedOrb;
        static bool carries;
        // The root Clear() last sent for destruction (alive until the end of that frame).
        static Transform? oldRoot;

        /// <summary>True when the tablet's lettering is showing and has this orb's try-out line.</summary>
        internal static bool Carries(Orb orb) => root != null && textOn && carries && carriedOrb == orb;

        /// <summary>Asks for the text to be written again on the next frame (Tutorial.Sync runs after it).</summary>
        internal static void Recheck() => textDirty = true;

        static void Once(string message) { if (said.Add(message)) { if (Logs.Summary) MelonLogger.Msg("[Archery] Tablet: " + message); } }

        // ---------------------------------------------------------------- per frame

        /// <summary>Called each frame (LateUpdate) on clients: build or rewrite after a change, then the rise, the sink and the drift.</summary>
        internal static void Frame() {
            if (ApplicationManager.IsHeadless) return;
            if (textDirty || geometryDirty) {
                bool rebuild = geometryDirty;
                textDirty = geometryDirty = false;
                try { Refresh(rebuild); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Tablet: build failed: {e.Message}"); Clear(); Forget(); }
                // Whatever happened to the tablet, an orb being tried out keeps its instruction: on the tablet, or floating.
                SyncTutorial();
            }
            if (root is null) return;
            if (root == null) {   // destroyed with its scene: let go of the meshes and materials made for it
                Clear();
                Forget();
                textDirty = true;   // and, should the archery shrine have outlived that scene, build it again next frame
                return;
            }
            Drive(Time.deltaTime);
        }

        static void SyncTutorial() {
            try { Tutorial.Sync(); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Tablet: try-out text check failed: {e.Message}"); }
        }

        /// <summary>The only per-frame work. Dormant (raised 0, nothing changing) it does nothing but count time for the
        /// slow look; otherwise it moves raised toward wantUp and sets the cached transforms. Nothing is allocated,
        /// nothing is looked up (Raise and FloatMotion are arithmetic on structs).</summary>
        static void Drive(float dt) {
            float before = raised;
            raised = Raise.Step(raised, wantUp, dt, riseSeconds.Value, sinkSeconds.Value);
            if (raised > 0f || before > 0f) clock += dt;
            if (raised != before) {
                if (raised >= 1f) { if (Logs.Debug) MelonLogger.Msg($"[Archery] Tablet: raised for {(openerMine ? "you" : "'" + openerShown + "'")}"); }
                else if (raised <= 0f) { if (Logs.Debug) MelonLogger.Msg(restVisible.Value ? "[Archery] Tablet: sunk into the cradle (dormant)" : "[Archery] Tablet: sunk and hidden"); }
            }
            if (turning && turnT != null) {
                // toward the opener over the rise, back to the resting direction over the sink
                float seconds = Mathf.Max(0.05f, wantUp ? riseSeconds.Value : sinkSeconds.Value);
                turnK = Mathf.Min(1f, turnK + dt / seconds);
                turnT.rotation = Quaternion.Slerp(turnFrom, turnTo, Raise.Ease(turnK));
                if (turnK >= 1f) turning = false;
            }
            bool still = raised == posed && (raised <= 0f || (raised >= 1f && !moving));
            if (!still) Arrange();
            if (Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + LookSeconds; Look(); }
        }

        /// <summary>Every piece to its pose for the present raised value and time.</summary>
        static void Arrange() {
            Transform? slab = tabletT;
            if (slab == null || root == null) return;
            float k = raised, t = clock;
            Put(slab, Raise.Tablet(spec, rest, k, t, moving));
            if (gemT != null) Put(gemT, Raise.Gem(spec, rest, k, t, moving));
            for (int i = 0; i < shards; i++) { Transform? s = shardT[i]; if (s != null) Put(s, Raise.Shard(spec, rest, i, k, t, moving)); }
            for (int i = 0; i < splinters; i++) { Transform? s = splinterT[i]; if (s != null) Put(s, Raise.Splinter(spec, rest, i, k, t, moving)); }
            for (int i = 0; i < fragments; i++) { Transform? s = fragmentT[i]; if (s != null) Put(s, Raise.Fragment(spec, rest, i, k, t, moving)); }

            float glow = Raise.Light(rest, k);
            if (glow != glowShown) {
                glowShown = glow;
                if (lamp != null) lamp.intensity = lampFull * glow;
                if (gemCopy != null) gemCopy.SetFloat(emissionStrength, glowFull * glow);
            }
            bool visible = k > 0f || wantUp || restVisible.Value;
            if (visible != rootShown) { rootShown = visible; root.SetActive(visible); }
            bool words = wantUp && k >= Mathf.Clamp01(textAppears.Value);
            if (words != textOn) {
                textOn = words;
                ShowFace();
                if (!building) SyncTutorial();   // the try-out line is on the tablet only while its lettering shows
            }
            posed = k;
        }

        static void Put(Transform t, Pose p) {
            t.localPosition = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
            t.localRotation = Quaternion.Euler(p.Euler.X, p.Euler.Y, p.Euler.Z);
            float s = p.Scale > 0.001f ? p.Scale : 0.001f;
            t.localScale = new Vector3(s, s, s);
        }

        static void ShowFace() {
            if (face == null) return;
            bool on = textOn && front;
            if (on != faceShown) { faceShown = on; face.SetActive(on); }
        }

        /// <summary>A few times a second, never per frame: is the camera behind the slab (the lettering is switched off
        /// there, using the slab's own facing), has the other player's build arrived or changed, is the font's atlas still the one drawn.</summary>
        static readonly int T_Look = Timing.Section("Tablet.Look");
        static void Look() {
            using var _t = Timing.Begin(T_Look);
            Transform? slab = tabletT;
            Camera? cam = Fx.View();
            if (slab != null && cam != null) {
                bool now = Vector3.Dot(cam.transform.position - slab.position, slab.forward) < 0f;
                if (now != front) { front = now; ShowFace(); }
            }
            // a shrine that was still being put in place when the tablet was built: build it again where the shrine is now
            if (builtTree != null && (builtTree.position - builtTreeAt).sqrMagnitude > 1e-4f) geometryDirty = true;
            if (!wantUp) return;
            if (!openerMine) {
                bool known = Builds.ClientBuildKnown(openerShown);
                if (known != otherKnown || (known && Builds.ClientBuild(openerShown) != otherBuild)) textDirty = true;
            }
            KeepAtlas();
        }

        // ---------------------------------------------------------------- build / clear

        static readonly int T_Refresh = Timing.Section("Tablet.Refresh");
        static void Refresh(bool rebuild) {
            using var _t = Timing.Begin(T_Refresh);
            SkillShrine? shrine = ShrineView.Site;
            bool up = shrine != null && ShrineView.SiteOpen;
            // It exists while the archery shrine does: dormant when nobody uses it (unless RestVisible is off and it has finished sinking).
            if (shrine == null || !enabled.Value || !(up || raised > 0f || restVisible.Value)) {
                // Always: a root the game already destroyed (scene unload, disconnect) leaves the meshes and material copies made here.
                bool stood = !(root is null);
                Clear();
                Forget();
                if (stood) { if (Logs.Debug) MelonLogger.Msg(shrine == null ? "[Archery] Tablet: removed (the archery shrine is gone)" : "[Archery] Tablet: removed"); }
                return;
            }
            if (root == null || rebuild || !ReferenceEquals(builtFor, shrine)) {
                bool another = !(builtFor is null) && !ReferenceEquals(builtFor, shrine);
                Clear();
                if (another) Forget();
                Build(shrine);
            }
            string who = up ? ShrineView.Opener : "";
            bool mine = up && ShrineView.SiteMine;
            if (up != wantUp || (up && (who != openerShown || mine != openerMine))) {
                if (up) BeginRise(who, mine); else BeginSink();
            }
            if (wantUp) Write();
        }

        /// <summary>Back to "nobody here": the next opening starts a rise from the cradle.</summary>
        static void Forget() {
            raised = 0f; wantUp = false; openerMine = false; openerShown = ""; turning = false; carries = false;
        }

        static void BeginRise(string who, bool mine) {
            wantUp = true; openerShown = who; openerMine = mine;
            otherKnown = false; otherBuild = default;
            shownText = "";   // whatever was written for the last reader is written again
            if (turnT != null) { turnFrom = turnT.rotation; turnTo = Facing(true); turnK = 0f; turning = true; }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Tablet: rising for {(mine ? "you" : "'" + who + "' (another player's tree)")}"
                            + (raised > 0f ? $", from {raised.ToString("0.##", CultureInfo.InvariantCulture)} of the way up" : ""));
        }

        static void BeginSink() {
            string who = openerMine ? "you" : "'" + openerShown + "'";
            wantUp = false; openerMine = false; openerShown = "";
            carries = false;
            shownText = "";
            // The words go dark at once: nothing of the last reader's build shows while the stone folds, or when it next rises.
            if (textOn) { textOn = false; ShowFace(); }
            // and the stand turns back to its resting direction (ReaderSpot) as it sinks, not keeping the last opener's
            if (turnT != null) { turnFrom = turnT.rotation; turnTo = Facing(false); turnK = 0f; turning = true; }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Tablet: sinking ({who} closed the tree)");
        }

        static void Clear() {
            // Destroy takes effect at the end of the frame: a tablet rebuilt right now must not find the old one's colliders as floor (FindFloor).
            oldRoot = root != null ? root.transform : null;
            carries = false;
            if (root != null) UnityEngine.Object.Destroy(root);   // every stone, crystal holder, line of text and the light are its children
            root = null; face = null; builtFor = null; builtTree = null; turnT = null; tabletT = null; gemT = null; rule = null; lamp = null;
            Array.Clear(shardT, 0, shardT.Length);
            Array.Clear(splinterT, 0, splinterT.Length);
            Array.Clear(fragmentT, 0, fragmentT.Length);
            shards = splinters = fragments = 0;
            inks.Clear();
            carves.Clear();
            foreach (Mesh m in madeMeshes) if (m != null) UnityEngine.Object.Destroy(m);   // only meshes made here
            madeMeshes.Clear();
            if (stoneCopy != null) UnityEngine.Object.Destroy(stoneCopy);
            if (glyphCopy != null) UnityEngine.Object.Destroy(glyphCopy);
            if (textCopy != null) UnityEngine.Object.Destroy(textCopy);
            if (gemCopy != null) UnityEngine.Object.Destroy(gemCopy);
            stoneCopy = glyphCopy = textCopy = gemCopy = null;
            textMaterial = null;
            shownText = "";
            textOn = false; faceShown = false; front = true; rootShown = true;
            posed = -1f; glowShown = -1f; turning = false;
        }

        /// <summary>Where the tablet's base goes and what it is made of.</summary>
        struct Seat {
            public Vector3 Origin;          // the stand's origin: the floor of the cradle's hollow, or the floor under the plinth
            public Quaternion Facing;       // the base's own facing (it does not turn with the slab)
            public StonePart[] Parts;
            public float Top;               // origin to the base's top
            public float Drop;              // origin above the shrine floor
            public float RestRadius, RestHeight, SplinterFloor, GemGap;
            public string How;
        }

        static readonly int T_Build = Timing.Section("look Tablet.Build (geometry)");
        static void Build(SkillShrine shrine) {
            using var _t = Timing.Begin(T_Build);
            float w = Mathf.Max(0.05f, width.Value), h = Mathf.Max(0.05f, height.Value), th = Mathf.Max(0.01f, thickness.Value);
            float fw = Mathf.Clamp(frameWidth.Value, 0.002f, w * 0.3f), fd = Mathf.Clamp(frameDepth.Value, 0f, th * 0.8f);
            float top = Mathf.Clamp(topRail.Value, 0.02f, 0.4f) * h, foot = Mathf.Clamp(footRail.Value, 0.02f, 0.4f) * h, inner = h - top - foot;
            float cut = Mathf.Max(0f, bevel.Value);

            // The frame is the skill tree's: its foot is the origin, "forward" is the way you look at it, "right" is your
            // right. The shrine's own root is not used: it lies on its back (z up) several metres behind the tree.
            Transform tree = shrine.SkillTreeTarget != null ? shrine.SkillTreeTarget : shrine.transform;
            Vector3 forward = Vector3.ProjectOnPlane(tree.rotation * Vector3.right, Vector3.up);   // slots sit at depth along the target's x (SkillShrine.OpenOnServer)
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(tree.forward, Vector3.up);
            forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward), foot0 = tree.position;
            treeForward = forward;
            Vector3 reader = Vec(readerSpot.Value, new Vector3(0f, 0f, -1.1f));
            readerAt = foot0 + right * reader.x + Vector3.up * reader.y + forward * reader.z;

            Seat seat = Place(shrine, foot0, right, forward, cut);

            bool withCrystals = crystals.Value && FindCrystals();
            spec = FloatSpec.Default;
            spec.Width = w; spec.Height = h; spec.Thickness = th; spec.Tilt = tilt.Value;
            spec.FloatHeight = Mathf.Max(seat.Top + 0.3f, floatHeight.Value - seat.Drop);
            spec.PlinthTop = seat.Top;
            spec.GemGap = seat.GemGap; spec.GemScale = gemScale.Value;
            spec.ShardCount = withCrystals ? shardCount.Value : 0; spec.ShardGap = shardGap.Value; spec.ShardScale = shardScale.Value; spec.ShardTilt = shardTilt.Value;
            spec.SplinterCount = withCrystals ? splinterCount.Value : 0; spec.SplinterScale = splinterScale.Value; spec.OrbitRadius = orbitRadius.Value;
            spec.SplinterFloor = seat.SplinterFloor;
            spec.FragmentCount = fragmentCount.Value;
            spec.BobHeight = bobHeight.Value; spec.BobSeconds = bobSeconds.Value; spec.SwayDegrees = swayDegrees.Value;
            spec.GemTurnSeconds = gemTurnSeconds.Value; spec.OrbitSeconds = orbitSeconds.Value;
            if (withCrystals) {   // the game's meshes as they are loaded now, not as they were measured in the editor
                Bounds sb = shardMesh!.bounds, gb = gemMesh!.bounds;
                spec.ShardMinY = sb.min.y; spec.ShardMaxY = sb.max.y; spec.ShardHalfWidth = sb.extents.x;
                spec.GemMinY = gb.min.y; spec.GemMaxY = gb.max.y;
                spec.GemHalfWidth = Mathf.Max(Mathf.Abs(gb.min.x), Mathf.Abs(gb.max.x), Mathf.Abs(gb.min.z), Mathf.Abs(gb.max.z));
            }
            rest = new RestSpec {
                Radius = seat.RestRadius, Height = seat.RestHeight, SlabScale = restSlabScale.Value, SlabTilt = restSlabTilt.Value, GemScale = restGemScale.Value, GemGap = 0.005f,
                ShardScale = restShardScale.Value, SplinterScale = restSplinterScale.Value, SplinterRadius = restSplinterRadius.Value,
                ChipScale = restChipScale.Value, ChipRadius = restChipRadius.Value, Light = restLight.Value
            };
            moving = motion.Value;

            Material stone = Stone(out bool plain);

            root = new GameObject("ArcheryTablet");
            builtFor = shrine;
            Transform ground = root.transform;
            ground.SetPositionAndRotation(seat.Origin, seat.Facing);
            standAt = seat.Origin;

            // the base: the seat stone in the cradle's hollow, or the free-standing plinth with its carved socket block
            foreach (StonePart part in seat.Parts) Block(part, ground, stone);

            // the stand: everything that rises, turning as one to face its reader
            var stand = new GameObject("Tablet Stand");
            turnT = stand.transform;
            turnT.SetParent(ground, false);
            turnT.rotation = Facing(wantUp);
            turning = false;

            // the slab: four frame rails round a recessed panel (TABLET frame: x right, y up the slab, the face looks along -z)
            var slab = new GameObject("Tablet Slab");
            tabletT = slab.transform;
            tabletT.SetParent(turnT, false);
            foreach (StonePart part in TabletParts.Slab(w, h, th, fw, fd, top, foot, cut)) Block(part, tabletT, stone);
            float panelW = w - 2f * fw;

            // stone chips, below and behind the slab
            fragments = FloatLayout.Fragments(spec);
            for (int i = 0; i < fragments; i++) fragmentT[i] = Block(TabletParts.Chip(i, cut), turnT, stone);

            // the game's crystals: its own meshes on the mod's holders, with its own material (the gem: a copy that can be dimmed)
            lampFull = 0f; glowFull = 0f;
            if (withCrystals) {
                Material gemShown = gemMaterial!;
                if (emissionStrength == 0) emissionStrength = Shader.PropertyToID("_EmissionStrength");
                if (gemMaterial!.HasProperty(emissionStrength)) {
                    gemCopy = new Material(gemMaterial) { name = "Archery Tablet Gem" };
                    glowFull = gemCopy.GetFloat(emissionStrength);
                    gemShown = gemCopy;
                }
                else Once($"the gem's shader '{Fx.ShaderName(gemMaterial)}' has no _EmissionStrength: dormant, the gem is small and its light dim, but its own glow is not dimmed");
                gemT = Crystal("Anchor Gem", turnT, gemMesh!, gemShown, spec.GemScale);
                shards = FloatLayout.Shards(spec);
                for (int i = 0; i < shards; i++) shardT[i] = Crystal("Carrier Shard " + (i + 1), tabletT, shardMesh!, shardMaterial!, spec.ShardScale);
                splinters = FloatLayout.Splinters(spec);
                for (int i = 0; i < splinters; i++) splinterT[i] = Crystal("Splinter " + (i + 1), turnT, shardMesh!, shardMaterial!, spec.SplinterScale);
                if (light.Value) {
                    var glowGo = new GameObject("Gem Light");
                    glowGo.transform.SetParent(gemT, false);
                    glowGo.transform.localPosition = new Vector3(0f, 0.5f * (spec.GemMinY + spec.GemMaxY), 0f);
                    lamp = glowGo.AddComponent<Light>();
                    lamp.type = LightType.Point;
                    lamp.shadows = LightShadows.None;
                    lamp.color = Cfg.Colour(lightColour, new Color(0.25f, 0.71f, 1f));
                    lampFull = Mathf.Max(0f, lightIntensity.Value);
                    lamp.intensity = lampFull * Raise.Light(rest, raised);
                    lamp.range = Mathf.Max(0.05f, lightRange.Value);
                }
            }

            // the writing face: lettering, rule and rune, lifted off the panel; dark until the tablet has risen
            face = new GameObject("Tablet Lettering");
            face.transform.SetParent(tabletT, false);
            face.SetActive(false);
            faceShown = false; textOn = false; front = true;
            faceLeft = -panelW * 0.5f;
            faceTop = h * 0.5f - top;
            faceZ = -th * 0.5f + fd - Mathf.Max(0f, textLift.Value);
            metrics = new TabletMetrics {
                FaceWidth = panelW, FaceHeight = inner, Margin = Mathf.Clamp(margin.Value, 0f, panelW * 0.3f), TextSize = Mathf.Max(0.002f, textSize.Value),
                TitleScale = Mathf.Max(0.2f, titleScale.Value), NoteScale = Mathf.Max(0.2f, noteScale.Value), MinScale = Mathf.Clamp(minLineScale.Value, 0.1f, 1f), GlyphSize = 0f
            };
            font = FindFont(out string fontHow);
            textMaterial = TextMaterial(font, out string textHow);
            Glyph();
            if (ruleWidth.Value > 0f && Fx.LineMaterial() is Material lines) {
                var go = new GameObject("Tablet Rule");
                go.transform.SetParent(face.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Quad(Cfg.Colour(ruleColour, new Color(0.59f, 0.65f, 0.75f)));
                Flat(go.AddComponent<MeshRenderer>(), lines, 0);
                rule = go.transform;
            }

            // every piece to where it is now: dormant in the cradle, or wherever the rise had got to before a rebuild
            posed = -1f; glowShown = -1f; rootShown = true;
            building = true;
            try { Arrange(); }
            finally { building = false; }
            builtTree = tree;
            builtTreeAt = tree.position;
            nextLook = 0f;   // which side the camera is on, this frame

            int stones = madeMeshes.Count;
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Tablet: built {(raised > 0f ? "raised" : "dormant")} at {seat.Origin.ToString("F2")}; {seat.How}; floats {spec.FloatHeight.ToString("0.##", CultureInfo.InvariantCulture)} m above its base; "
                            + $"rest inside {RestLayout.Radius(rest).ToString("0.###", CultureInfo.InvariantCulture)} m and under {RestLayout.Height(spec, rest).ToString("0.###", CultureInfo.InvariantCulture)} m");
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Tablet: stone {(plain ? "plain slab colour" : "'" + stone.name + "'")} ({stoneHow}); shard {(withCrystals ? shardHow : "none")}; gem {(withCrystals ? gemHow : "none")}; "
                            + $"font '{(font != null ? font.name : "-")}' {fontHow}; lettering {textHow}; rune {(glyphCopy != null ? "'" + glyphFor + "'" : "none")}; "
                            + $"{stones} stone/quad meshes, {shards} shard(s), {splinters} splinter(s), {fragments} chip(s), motion {(moving ? "on" : "off")}");
        }

        /// <summary>One chamfered stone block on its own child object, its mesh made here and destroyed in Clear().</summary>
        static Transform Block(StonePart part, Transform parent, Material stone) {
            string name = part.Name;
            Vector3 size = new Vector3(part.Size.X, part.Size.Y, part.Size.Z), at = new Vector3(part.Centre.X, part.Centre.Y, part.Centre.Z);
            StoneMesh m = StoneBox.Build(size.x, size.y, size.z, part.Bevel, part.Uv);
            int n = m.VertexCount;
            var v = new Vector3[n];
            var nr = new Vector3[n];
            var tg = new Vector4[n];
            var uv = new Vector2[n];
            for (int i = 0; i < n; i++) {
                v[i] = new Vector3(m.Vertices[i * 3], m.Vertices[i * 3 + 1], m.Vertices[i * 3 + 2]);
                nr[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
                tg[i] = new Vector4(m.Tangents[i * 4], m.Tangents[i * 4 + 1], m.Tangents[i * 4 + 2], m.Tangents[i * 4 + 3]);
                uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
            }
            var mesh = new Mesh { name = "Archery " + name };
            madeMeshes.Add(mesh);
            mesh.vertices = v;
            mesh.normals = nr;
            mesh.tangents = tg;
            mesh.uv = uv;
            mesh.triangles = m.Triangles;
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = stone;
            if (solid.Value) go.AddComponent<BoxCollider>().size = size;
            return go.transform;
        }

        /// <summary>A holder of the mod's own showing one of the game's crystal meshes with the game's crystal material
        /// (or, for the gem, the mod's copy of it).</summary>
        static Transform Crystal(string name, Transform parent, Mesh mesh, Material material, float scale) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;         // sharedMesh / sharedMaterial: the game's own objects, never copies made by Unity
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>A unit square in XY (centre at the origin) with one vertex colour.</summary>
        static Mesh Quad(Color colour) {
            var mesh = new Mesh { name = "Archery Tablet Quad" };
            madeMeshes.Add(mesh);
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.colors = new[] { colour, colour, colour, colour };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };   // seen from -z (the reader's side)
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Flat(MeshRenderer r, Material m, int order) {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = order;
        }

        // ---------------------------------------------------------------- placement

        /// <summary>The stand's facing (z away from the reader): toward the player who has the tree open (FacePlayer),
        /// else toward the spot a reader stands at in front of the tree. The same on every client.</summary>
        static Quaternion Facing(bool forOpener) {
            Vector3 reader = readerAt;
            if (forOpener && facePlayer.Value) {
                if (openerMine) { Camera? cam = Fx.View(); if (cam != null) reader = cam.transform.position; }
                else if (ShrineView.OpenerPosition(out Vector3 at)) reader = at;
            }
            Vector3 away = Vector3.ProjectOnPlane(standAt - reader, Vector3.up);
            if (away.sqrMagnitude < 0.04f) away = Vector3.ProjectOnPlane(standAt - readerAt, Vector3.up);   // standing on it: the usual side
            if (away.sqrMagnitude < 1e-4f) away = treeForward;
            return Quaternion.AngleAxis(yaw.Value, Vector3.up) * Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>Where the base goes: in the shrine's cradle (Offset = auto), or a free-standing plinth at Offset.</summary>
        static Seat Place(SkillShrine shrine, Vector3 foot, Vector3 right, Vector3 forward, float cut) {
            string spot = (offset.Value ?? "").Trim();
            bool auto = spot.Length == 0 || string.Equals(spot, "auto", StringComparison.OrdinalIgnoreCase);
            Vector3 old = foot + right * OldSpot.x + forward * OldSpot.z;
            var seat = new Seat();

            if (auto) {
                Vector3 e = Vec(cradleSpot.Value, CradleExpected);
                Vector3 expected = foot + right * e.x + Vector3.up * e.y + forward * e.z;
                Cradle c = FindCradle(shrine, expected);
                Vector3 size = Vec(cradleBase.Value, new Vector3(0.20f, 0.05f, 0.20f));
                size = new Vector3(Mathf.Max(0.02f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.02f, size.z));
                float corner = 0.5f * Mathf.Sqrt(size.x * size.x + size.z * size.z), most = c.Radius * 0.95f;
                if (corner > most) { size.x *= most / corner; size.z *= most / corner; }   // the seat stone shrinks to fit the hollow
                float sunk = Mathf.Clamp(sink.Value, 0f, size.y);
                seat.Parts = TabletParts.Plinth(new P3(size.x, size.y, size.z), sunk, 0f, cut);
                seat.Origin = new Vector3(c.Centre.x, c.Floor, c.Centre.z);
                seat.Facing = c.Yaw;
                seat.Top = size.y - sunk;
                seat.Drop = c.Floor - c.Bottom;
                seat.RestRadius = c.Radius;
                seat.RestHeight = Mathf.Max(0.05f, restHeight.Value);
                seat.SplinterFloor = (c.Top - c.Floor) + 0.10f;   // the splinters' ring clears the prongs' tips
                seat.GemGap = Mathf.Max(0f, cradleGemGap.Value);
                Vector3 moved = Vector3.ProjectOnPlane(seat.Origin - old, Vector3.up);
                seat.How = $"in the cradle: {c.How}, at {c.Centre.ToString("F2")}, {Num((c.Centre - expected).magnitude)} m from where it is expected; hollow floor {Num(c.Floor - c.Bottom)} m above the cradle's foot, "
                           + $"prong tips {Num(c.Top - c.Floor)} m above that, hollow radius {Num(c.Radius)} m; seat stone {Num(size.x)} x {Num(size.y)} x {Num(size.z)} m; "
                           + $"the base moved {Num(moved.magnitude)} m from the old plinth spot";
                return seat;
            }

            // a free-standing plinth, as before 0.13.0, on the floor found under it
            Vector3 o = Vec(spot, OldSpot);
            Vector3 plinth = Vec(baseSize.Value, new Vector3(0.34f, 0.10f, 0.26f));
            plinth = new Vector3(Mathf.Max(0.02f, plinth.x), Mathf.Max(0.01f, plinth.y), Mathf.Max(0.02f, plinth.z));
            float down = Mathf.Clamp(sink.Value, 0f, plinth.y), socket = Mathf.Max(0f, socketHeight.Value);
            Vector3 at = foot + right * o.x + forward * o.z;
            float floor = FindFloor(at, foot.y - floorBelowTree.Value, out string how);
            seat.Parts = TabletParts.Plinth(new P3(plinth.x, plinth.y, plinth.z), down, socket, cut);
            seat.Origin = new Vector3(at.x, floor + o.y, at.z);
            Vector3 away = Vector3.ProjectOnPlane(seat.Origin - readerAt, Vector3.up);
            seat.Facing = Quaternion.LookRotation(away.sqrMagnitude > 1e-4f ? away.normalized : forward, Vector3.up);
            seat.Top = plinth.y - down + socket;
            seat.Drop = 0f;
            seat.RestRadius = 0.45f * Mathf.Min(plinth.x, plinth.z);
            seat.RestHeight = seat.Top + Mathf.Max(0.05f, restHeight.Value) - 0.03f;
            seat.SplinterFloor = 0f;
            seat.GemGap = Mathf.Max(0f, gemGap.Value);
            seat.How = $"a free-standing plinth at Offset '{spot}' ({Num(Vector3.ProjectOnPlane(seat.Origin - old, Vector3.up).magnitude)} m from the old plinth spot), {how}";
            return seat;
        }

        static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>The shrine's U-shaped cradle as found at run time.</summary>
        struct Cradle {
            public Vector3 Centre;                    // the arcs' pivot: the cradle's upright axis runs through it
            public float Bottom, Top, Floor, Radius;  // world heights of the cradle's foot, its prongs' tips and the hollow's floor; the hollow's radius
            public Quaternion Yaw;
            public string How;
        }

        static bool CradleNamed(string name, string wanted) =>
            name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) && name.IndexOf("frame", StringComparison.OrdinalIgnoreCase) < 0;

        /// <summary>Finds the cradle among the shrine's own objects, which are only read: first the objects named like the
        /// arcs (their group objects, whose pivot is the cradle's axis), else the object with "arc" in its name nearest
        /// to where the cradle is expected, else that expected spot itself with the arcs' known measures.</summary>
        static readonly int T_FindCradle = Timing.Section("scene Tablet.FindCradle");
        static Cradle FindCradle(SkillShrine shrine, Vector3 expected) {
            using var _t = Timing.Begin(T_FindCradle);
            string wanted = (cradleName.Value ?? "").Trim();
            if (wanted.Length == 0) wanted = CradleDefault;
            var picked = new List<Transform>();
            string how = "";
            Transform[] all = shrine.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all) if (t != null && CradleNamed(t.name, wanted) && t.name.EndsWith("_grp", StringComparison.OrdinalIgnoreCase)) picked.Add(t);
            if (picked.Count == 0) foreach (Transform t in all) if (t != null && CradleNamed(t.name, wanted)) picked.Add(t);
            if (picked.Count > 0) how = "by name";
            else {
                Transform? near = null;
                float best = NearMatch * NearMatch;
                foreach (Transform t in all) {
                    if (t == null || t.name.IndexOf("arc", StringComparison.OrdinalIgnoreCase) < 0 || t.name.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    float d = (t.position - expected).sqrMagnitude;
                    if (d < best) { best = d; near = t; }
                }
                if (near != null) { picked.Add(near); how = $"nearest match to where it is expected (nothing is named '{wanted}*')"; }
            }

            var c = new Cradle { Centre = expected, Yaw = Quaternion.LookRotation(treeForward, Vector3.up) };
            float half = ArcHalf;
            bool measured = false;
            if (picked.Count > 0) {
                // the ones that stand together, round the one nearest to where the cradle is expected
                Transform anchor = picked[0];
                foreach (Transform t in picked) if ((t.position - expected).sqrMagnitude < (anchor.position - expected).sqrMagnitude) anchor = t;
                Vector3 sum = Vector3.zero;
                int count = 0;
                float low = float.MaxValue, high = float.MinValue, widest = 0f;
                for (int pass = 0; pass < 2 && !measured; pass++) {   // the full-detail meshes first, then any
                    foreach (Transform t in picked) {
                        if ((t.position - anchor.position).sqrMagnitude > CradleCluster * CradleCluster) continue;
                        if (pass == 0) { sum += t.position; count++; }
                        foreach (MeshFilter f in t.GetComponentsInChildren<MeshFilter>(true)) {
                            Mesh m = f != null ? f.sharedMesh : null!;
                            if (m == null || (pass == 0 && f!.name.IndexOf("LOD0", StringComparison.OrdinalIgnoreCase) < 0)) continue;
                            Bounds b = m.bounds;   // read only
                            for (int corner = 0; corner < 8; corner++) {
                                Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                                float y = f!.transform.TransformPoint(p).y;
                                low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                            }
                            Vector3 scale = f!.transform.lossyScale;
                            widest = Mathf.Max(widest, Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                            measured = true;
                        }
                    }
                }
                if (count > 0) c.Centre = sum / count;
                // the cradle's cross: whichever of the arc's axes lies flattest
                Vector3 axis = anchor.right;
                if (Mathf.Abs(anchor.up.y) < Mathf.Abs(axis.y)) axis = anchor.up;
                if (Mathf.Abs(anchor.forward.y) < Mathf.Abs(axis.y)) axis = anchor.forward;
                axis = Vector3.ProjectOnPlane(axis, Vector3.up);
                if (axis.sqrMagnitude > 1e-4f) c.Yaw = Quaternion.LookRotation(axis.normalized, Vector3.up);
                if (measured && high - low > 0.1f && high - low < 1.5f && widest > 0.05f && widest < 1.5f) { c.Bottom = low; c.Top = high; half = widest; }
                else measured = false;
                how = $"{count} object(s) '{anchor.name}' {how}" + (measured ? "" : ", measures assumed (no mesh could be read)");
            }
            else how = $"no object of the shrine is named '{wanted}*' or has 'arc' in its name near there: the spot where the cradle is expected (CradleSpot), measures assumed";
            if (!measured) { c.Bottom = c.Centre.y - ArcBelow; c.Top = c.Centre.y + ArcAbove; }
            c.Floor = c.Bottom + Mathf.Clamp(cradleFloor.Value, 0f, 0.95f) * (c.Top - c.Bottom);
            c.Radius = Mathf.Max(0.03f, Mathf.Clamp(cradleRadius.Value, 0.2f, 1f) * half);
            c.How = how;
            return c;
        }

        /// <summary>The floor under a free-standing plinth. One short cast at build time, from just above where the floor
        /// is expected (so nothing higher up can be taken for it) to a little below. Players, anything with a rigidbody
        /// (held and loose items), triggers and the mod's own stones are not floor. Nothing sane: the expected height.</summary>
        static readonly int T_FindFloor = Timing.Section("scene Tablet.FindFloor (ray)");
        static float FindFloor(Vector3 spot, float expected, out string how) {
            using var _t = Timing.Begin(T_FindFloor);
            float above = Mathf.Clamp(floorStart.Value, 0.05f, 1f), below = Mathf.Clamp(floorTolerance.Value, 0.05f, 2f);
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(spot.x, expected + above, spot.z), Vector3.down, above + below, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, y = expected;
            string name = "";
            int skipped = 0;
            foreach (RaycastHit hit in hits) {
                Collider col = hit.collider;
                if (col == null) continue;
                bool own = (oldRoot != null && col.transform.IsChildOf(oldRoot)) || (root != null && col.transform.IsChildOf(root.transform));
                if (own || col.attachedRigidbody != null || col is CharacterController || col.GetComponentInParent<Player>() != null) { skipped++; continue; }
                if (hit.distance >= best) continue;
                best = hit.distance; y = hit.point.y; name = col.name;
            }
            string ignored = skipped > 0 ? $", {skipped} player/item collider(s) ignored" : "";
            if (name.Length > 0) how = $"floor found on '{name}', {Num(y - expected)} m from the expected height ({Num(floorBelowTree.Value)} m below the tree's foot){ignored}";
            else how = $"no floor within +{Num(above)} / -{Num(below)} m of the expected height: standing {Num(floorBelowTree.Value)} m below the tree's foot{ignored}";
            return y;
        }

        static Vector3 Vec(string text, Vector3 fallback) {
            string[] parts = (text ?? "").Split(',');
            if (parts.Length != 3) return fallback;
            var v = new float[3];
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || float.IsNaN(v[i]) || float.IsInfinity(v[i])) return fallback;
            return new Vector3(v[0], v[1], v[2]);
        }

        // ---------------------------------------------------------------- the game's assets

        static Texture? MainTexture(Material m) => m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;

        /// <summary>The tablet's stone: ONE copy of the game's ruins material (the game's own is never changed), tinted,
        /// its meander channel lit. Without the game's stone: a plain dark material, so the tablet always shows.</summary>
        static readonly int T_Stone = Timing.Section("asset Tablet.Stone");
        static Material Stone(out bool plain) {
            using var _t = Timing.Begin(T_Stone);
            string wanted = (slabMaterial.Value ?? "").Trim();
            if (stoneSource == null || stoneFor != wanted) {
                stoneSource = null;
                stoneFor = wanted;
                Material? glow = null, any = null, named = null;
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>()) {
                    if (m == null || m.name.StartsWith("Archery Tablet", StringComparison.Ordinal)) continue;   // never one of the mod's own (a copy being destroyed this frame)
                    if (wanted.Length > 0) { if (m.name == wanted) { named = m; break; } continue; }
                    Texture? t = MainTexture(m);
                    if (t == null || t.name != StoneTexture) continue;
                    any ??= m;
                    if (m.name == StoneMaterial) { glow = m; break; }
                    if (glow == null && m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null) glow = m;
                }
                stoneSource = named ?? glow ?? any;
                stoneHow = stoneSource == null ? "" : wanted.Length > 0 ? "the material named in SlabMaterial" : $"a loaded material whose main texture is {StoneTexture}";
                if (stoneSource == null && wanted.Length > 0) Once($"no loaded material is named '{wanted}' (SlabMaterial); using the plain slab");
            }

            plain = false;
            if (stoneSource != null) stoneCopy = new Material(stoneSource) { name = "Archery Tablet Stone" };
            else if (wanted.Length == 0) {
                // the texture without its material: the game's stone shader with the game's texture
                Texture2D? atlas = FindTexture(StoneTexture, true);
                Shader lit = Shader.Find(StoneShader);
                if (atlas != null && lit != null) {
                    stoneCopy = new Material(lit) { name = "Archery Tablet Stone" };
                    stoneCopy.SetTexture("_MainTex", atlas);
                    stoneHow = $"texture {StoneTexture} on the shader '{StoneShader}' (no material uses it)";
                }
            }
            if (stoneCopy != null) {
                if (stoneCopy.HasProperty("_Color")) stoneCopy.SetColor("_Color", Cfg.Colour(stoneTint, new Color(0.62f, 0.68f, 0.8f)));
                else Once($"the stone's shader '{Fx.ShaderName(stoneCopy)}' has no _Color: StoneTint is not applied");
                Glow(stoneCopy);
                return stoneCopy;
            }

            plain = true;
            stoneHow = "the game's ruins stone was not found";
            Once($"no loaded material or texture named {StoneTexture}: the tablet is a plain dark slab");
            Shader flat = Shader.Find("Unlit/Unlit Color");
            if (flat == null) flat = Shader.Find("Unlit/Color");
            if (flat == null) flat = Shader.Find("Sprites/Default");
            if (flat == null) flat = Shader.Find("Hidden/Internal-Colored");
            stoneCopy = new Material(flat) { name = "Archery Tablet Slab" };
            if (stoneCopy.HasProperty("_Color")) stoneCopy.SetColor("_Color", Cfg.Colour(slabColour, new Color(0.11f, 0.12f, 0.14f)));
            return stoneCopy;
        }

        /// <summary>The meander band's channel is lit through the material's own emission map (the map is dark everywhere else on the atlas).</summary>
        static void Glow(Material stone) {
            if (glowStrength.Value <= 0f) return;
            if (!stone.HasProperty("_EmissionColor") || !stone.HasProperty("_EmissionMap")) { Once($"the stone's shader '{Fx.ShaderName(stone)}' has no emission: the meander does not glow"); return; }
            if (stone.GetTexture("_EmissionMap") == null) {
                Texture2D? map = FindTexture(StoneEmissive, true);
                if (map == null) { Once($"the stone has no emission map and {StoneEmissive} is not loaded: the meander does not glow"); return; }
                stone.SetTexture("_EmissionMap", map);
            }
            Color c = Cfg.Colour(glowColour, new Color(0.47f, 0.86f, 1f)) * glowStrength.Value;
            stone.SetColor("_EmissionColor", new Color(c.r, c.g, c.b, 1f));
            stone.EnableKeyword("_EMISSION");
            stone.EnableKeyword("_UseEmissionMap");
        }

        /// <param name="exact">Only a texture of exactly this name (the stone and its emission map: a name that merely starts
        /// the same, such as ruins_02_diffuse_PlatformTexture_tex, is another texture). Otherwise the first whose name starts with it.</param>
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

        /// <summary>The game's crystal shard and gem, each a mesh with the material the game itself puts on it.</summary>
        static readonly int T_FindCrystals = Timing.Section("asset Tablet.FindCrystals");
        static bool FindCrystals() {
            using var _t = Timing.Begin(T_FindCrystals);
            if (shardMesh == null || shardMaterial == null) FindCrystal(ShardPrefab, ShardMeshes, out shardMesh, out shardMaterial, out shardHow);
            if (gemMesh == null || gemMaterial == null) FindCrystal(GemPrefab, GemMeshes, out gemMesh, out gemMaterial, out gemHow);
            bool found = shardMesh != null && shardMaterial != null && gemMesh != null && gemMaterial != null;
            if (!found) Once($"the game's crystals were not found (shard: {shardHow}; gem: {gemHow}): the tablet floats with its stone chips only");
            return found;
        }

        static void FindCrystal(string prefabPath, string[] meshNames, out Mesh? mesh, out Material? material, out string how) {
            mesh = null; material = null;
            // 1. the game's own prefab (under Resources, so loaded with the game): the pair on its crystal's LOD0.
            //    Read only: the prefab is never instantiated (it carries network and physics parts) and never changed.
            GameObject? prefab = null;
            try { prefab = Resources.Load<GameObject>(prefabPath); } catch { }
            if (prefab != null && Pair(prefab.GetComponentsInChildren<MeshFilter>(true), meshNames, false, out mesh, out material)) {
                how = $"mesh '{mesh!.name}' with material '{material!.name}' from the prefab '{prefab.name}'";
                return;
            }
            // 2. any loaded object that shows the crystal with the game's atlas material (a crystal in the world, a prefab under another path)
            if (Pair(Resources.FindObjectsOfTypeAll<MeshFilter>(), meshNames, true, out mesh, out material)) {
                how = $"mesh '{mesh!.name}' with material '{material!.name}' from a loaded object";
                return;
            }
            // 3. the mesh and the material by name; only the _Atlased mesh has the UVs of the atlas material
            mesh = null; material = null;
            foreach (Mesh m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m != null && m.name == meshNames[0]) { mesh = m; break; }
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && m.name == CrystalMaterial) { material = m; break; }
            if (mesh != null && material != null) { how = $"mesh '{mesh.name}' and material '{material.name}' by name"; return; }
            how = (mesh == null ? $"no mesh '{meshNames[0]}'" : "") + (mesh == null && material == null ? ", " : "") + (material == null ? $"no material '{CrystalMaterial}'" : "");
            mesh = null; material = null;
        }

        /// <summary>The first mesh name (in order of preference) some filter shows, with its own renderer's material.</summary>
        static bool Pair(MeshFilter[] filters, string[] meshNames, bool atlasOnly, out Mesh? mesh, out Material? material) {
            foreach (string wanted in meshNames) {
                foreach (MeshFilter f in filters) {
                    if (f == null || f.sharedMesh == null || f.sharedMesh.name != wanted) continue;
                    if (root != null && f.transform.IsChildOf(root.transform)) continue;
                    if (oldRoot != null && f.transform.IsChildOf(oldRoot)) continue;   // a tablet being replaced this frame (its gem wears the mod's copy)
                    var r = f.GetComponent<MeshRenderer>();
                    Material? m = r != null ? r.sharedMaterial : null;
                    if (m == null || (atlasOnly && m.name != CrystalMaterial)) continue;
                    mesh = f.sharedMesh; material = m;
                    return true;
                }
            }
            mesh = null; material = null;
            return false;
        }

        // ---------------------------------------------------------------- lettering

        static readonly int T_FindFont = Timing.Section("asset Tablet.FindFont");
        static Font FindFont(out string how) {
            using var _t = Timing.Begin(T_FindFont);
            string wanted = (fontName.Value ?? "").Trim();
            Font[]? loaded = null;
            if (wanted.Length > 0) {
                loaded = Resources.FindObjectsOfTypeAll<Font>();
                foreach (Font f in loaded) if (f != null && string.Equals(f.name, wanted, StringComparison.OrdinalIgnoreCase)) { how = "named in Font"; return f; }
                Once($"no loaded font is named '{wanted}' (Font); using the game's Vinque");
            }
            Font? vinque = null;
            try { vinque = Resources.Load<Font>(VinquePath); } catch { }
            if (vinque != null) { how = $"the game's Vinque (Resources '{VinquePath}')"; return vinque; }
            loaded ??= Resources.FindObjectsOfTypeAll<Font>();
            foreach (Font f in loaded) if (f != null && string.Equals(f.name, VinqueName, StringComparison.OrdinalIgnoreCase)) { how = "the game's Vinque (a loaded font)"; return f; }
            foreach (Font f in loaded) if (f != null && f.name != "Arial" && f.material != null) { how = "the first loaded font that is not Arial (Vinque not found)"; return f; }
            how = "built-in Arial (no game font found)";
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>The lettering's material. The font's own (GUI/Text Shader) draws over everything, hands and the slab's
        /// back included, so the mod makes ONE material of its own on a depth-tested shader, with the font's atlas as
        /// its texture; the font, its material and its atlas are only read.
        ///
        /// A font atlas is an alpha-only texture: its colour channels read as black on the PC's graphics API, so the
        /// shader must take the letter's shape from the texture's alpha and its colour from the vertex colour (TextMesh
        /// puts each line's colour there). In order:
        ///   UI/Default      (Unity built-in; what the game's own UI Text draws this same atlas with): colour =
        ///                   (texture + _TextureSampleAdd) x vertex colour x _Color. With _TextureSampleAdd = (1,1,1,0)
        ///                   an alpha-only atlas reads white with the glyph's alpha. Its depth test is the property
        ///                   unity_GUIZTestMode, set here to LessEqual on the material.
        ///   Sprites/Default (known to be in this build): colour = texture x vertex colour, depth-tested as it is. An
        ///                   alpha-only atlas would give black letters, so it is used only when the atlas has colour.
        /// "GUI/3D Text Shader" is not one of Unity's built-in shaders and is not among the game's own, so it is not
        /// tried; it can be named in TextShader. Nothing suitable: the font's own material, as before 0.13.0 (the
        /// lettering is then still switched off from behind, Look()).</summary>
        static readonly int T_TextMaterial = Timing.Section("asset Tablet.TextMaterial");
        static Material? TextMaterial(Font f, out string how) {
            using var _t = Timing.Begin(T_TextMaterial);
            Material own = f.material;
            if (own == null) { how = "none (the font has no material)"; return null; }
            string wanted = (textShader.Value ?? "").Trim();
            if (string.Equals(wanted, "font", StringComparison.OrdinalIgnoreCase)) { how = $"on the font's own material '{Fx.ShaderName(own)}' (TextShader = font): drawn over everything"; return own; }
            bool auto = wanted.Length == 0 || string.Equals(wanted, "auto", StringComparison.OrdinalIgnoreCase);
            Texture atlas = own.mainTexture;
            bool alphaOnly = !(atlas is Texture2D t2) || t2.format == TextureFormat.Alpha8;
            string passed = "";
            foreach (string name in auto ? AutoTextShaders : new[] { wanted }) {
                Shader s = Shader.Find(name);
                if (s == null) { passed += $"no '{name}'; "; continue; }
                if (auto && alphaOnly && name == "Sprites/Default") { passed += "'Sprites/Default' would draw an alpha-only atlas black; "; continue; }
                textCopy = new Material(s) { name = "Archery Tablet Text" };
                textCopy.mainTexture = atlas;
                if (textCopy.HasProperty("_Color")) textCopy.SetColor("_Color", Color.white);
                textCopy.SetVector("_TextureSampleAdd", alphaOnly ? new Vector4(1f, 1f, 1f, 0f) : Vector4.zero);        // UI shaders: an alpha-only texture reads white
                textCopy.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);            // UI shaders: tested against depth
                how = $"on the shader '{name}' (hidden behind anything in front; atlas '{(atlas != null ? atlas.name : "-")}' {(alphaOnly ? "alpha-only" : "with colour")})";
                Once($"lettering {how}" + (passed.Length > 0 ? " [" + passed.TrimEnd(' ', ';') + "]" : ""));
                return textCopy;
            }
            how = $"on the font's own material '{Fx.ShaderName(own)}', drawn over everything ({passed.TrimEnd(' ', ';')})";
            Once($"lettering {how}");
            return own;
        }

        /// <summary>A dynamic font rebuilds its atlas as new letters are asked for; the lettering's material follows it.</summary>
        static readonly int T_FontRebuilt = Timing.Section("event Tablet.FontRebuilt");
        static void FontRebuilt(Font rebuilt) {
            using var _t = Timing.Begin(T_FontRebuilt);
            try {
                if (font == null || rebuilt != font) return;
                KeepAtlas();
                // The game's own text shares this dynamic font. A rebuild keeps only the letters asked for in the last frame and moves
                // every glyph, and nothing rebuilds the tablet's text meshes: the lettering would go blank (or turn to other letters)
                // until the text next changed. So it is written again on the next frame.
                atlasRebuilds++;
                if (root != null && wantUp) { shownText = ""; textDirty = true; }
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Tablet: the font's atlas was rebuilt (rebuild {atlasRebuilds}; {(font.material != null && font.material.mainTexture != null ? font.material.mainTexture.width + "x" + font.material.mainTexture.height : "?")}){(root != null && wantUp ? "; the lettering is written again" : "")}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Tablet: font atlas check failed: {e.Message}"); }
        }

        static int atlasRebuilds;

        static void KeepAtlas() {
            if (textCopy == null || font == null) return;
            Material own = font.material;
            Texture atlas = own != null ? own.mainTexture : null!;
            if (atlas != null && textCopy.mainTexture != atlas) textCopy.mainTexture = atlas;
        }

        static float EmWidth(string text) {
            Font? f = font;
            if (string.IsNullOrEmpty(text)) return 0f;
            int px = f == null ? 0 : f.dynamic ? FontPx : f.fontSize;
            if (f == null || px <= 0) return text.Length * 0.5f;
            if (f.dynamic) f.RequestCharactersInTexture(text, FontPx, FontStyle.Normal);
            float sum = 0f;
            foreach (char c in text)
                sum += f.GetCharacterInfo(c, out UnityEngine.CharacterInfo info, f.dynamic ? FontPx : 0, FontStyle.Normal) && info.advance > 0 ? info.advance : px * 0.5f;
            return sum / px;
        }

        /// <summary>The rune in the face's top-right corner: the game's texture on the mod's own flat material (depth-tested).</summary>
        static void Glyph() {
            string name = (glyphName.Value ?? "").Trim();
            float size = glyphSize.Value;
            if (name.Length == 0 || size <= 0f || face == null) return;
            if (glyphTexture == null || glyphFor != name) { glyphTexture = FindTexture(name); glyphFor = name; }
            Material? flat = Fx.LineMaterial();
            if (glyphTexture == null) { Once($"no loaded texture named '{name}' (Glyph): no rune"); return; }
            if (flat == null || !flat.HasProperty("_MainTex")) { Once("no textured flat shader: no rune"); return; }
            glyphCopy = new Material(flat) { name = "Archery Tablet Rune" };
            glyphCopy.SetTexture("_MainTex", glyphTexture);
            Color c = Cfg.Colour(glowColour, new Color(0.47f, 0.86f, 1f));
            var go = new GameObject("Tablet Rune");
            go.transform.SetParent(face.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad(new Color(c.r, c.g, c.b, Mathf.Clamp01(glyphAlpha.Value)));
            Flat(go.AddComponent<MeshRenderer>(), glyphCopy, 0);
            metrics.GlyphSize = size;
            TabletPage corner = TabletLayout.Layout(new string[0], false, metrics, emWidth);
            go.transform.localPosition = new Vector3(faceLeft + corner.GlyphX + size * 0.5f, faceTop - corner.GlyphY - size * 0.5f, faceZ);
            go.transform.localScale = new Vector3(size, size, 1f);
        }

        static Color Ink(LineKind kind) {
            switch (kind) {
                case LineKind.Plus: return Cfg.Colour(plusColour, new Color(1f, 0.84f, 0.47f));
                case LineKind.Warn: return Cfg.Colour(warnColour, new Color(1f, 0.69f, 0f));
                case LineKind.Footer: return Cfg.Colour(footerColour, new Color(0.75f, 0.88f, 1f));
                case LineKind.Note: return Cfg.Colour(noteColour, new Color(0.59f, 0.63f, 0.7f));
                default: return Cfg.Colour(textColour, new Color(0.93f, 0.91f, 0.86f));
            }
        }

        /// <summary>What the tablet says to the player who opened the tree: the held orb against their build, or the build itself.</summary>
        static List<string> Wording() {
            FactNumbers numbers = Paths.Numbers();
            string note = (testNote.Value ?? "").Trim();
            SkillOrb? held = ShrineView.Held;
            List<string> lines;
            string tryOut = "", carrier = "";
            carries = false;
            if (held != null && held.Slot != null && Tree.OrbBySlot.TryGetValue(held.Slot.Hash, out Orb orb)) {
                string instruction = Tutorial.LineFor(orb);
                carriedOrb = orb;
                SkillStatus status = held.Status;
                // G3: why it cannot be bought, from what this client knows (BuyBlock in Preview.cs, tested)
                BuyFacts facts = Facts(held, orb, status, instruction);
                string blocked = BuyBlock.Reason(facts);
                // "Try it first: <what to do>" carries the try-out line itself, so the try-out line is not repeated above it; the
                // reason line (never dropped by Fit) is then what carries the instruction for the floating text (Tutorial.Sync)
                bool inReason = blocked.Length > 0 && instruction.Length > 0 && blocked.EndsWith(instruction, StringComparison.Ordinal);
                tryOut = inReason ? "" : instruction;
                carrier = inReason ? blocked : instruction;
                lines = TabletText.ForOrb(orb, ShrineView.IsOwned(status), blocked, ShrineView.Owned, ShrineView.Incumbent, numbers, tryOut, note);
                string said = BuyBlock.Describe(facts) + " -> " + (blocked.Length > 0 ? "'" + blocked + "'" : "buyable or owned");
                if (Logs.Debug && said != lastBuyFacts) MelonLogger.Msg($"[Archery] Tablet: held orb {said}");
                lastBuyFacts = said;
            }
            else lines = TabletText.ForBuild(ShrineView.Owned, ShrineView.Incumbent, numbers, note);
            lines = TabletText.Fit(lines, Mathf.Max(3, maxLines.Value), tryOut);
            carries = carrier.Length > 0 && lines.Contains(carrier);   // not so: Tutorial floats the line (Tutorial.Sync)
            return lines;
        }

        static string lastBuyFacts = "";

        /// <summary>G3: the facts behind the held orb's cannot-buy line, as this client knows them (ShrineView): the game's status for it,
        /// its parents, the skills held over every profession and the slot cap, the free points, its cost, whether the local player
        /// has practised it, and the one-legendary rule. Whether the server requires practice at all (its CanOnlyBuySkillsAfterPracticing)
        /// is not sent to clients: "Try it first" shows only while the game says it is otherwise buyable.</summary>
        static BuyFacts Facts(SkillOrb held, Orb orb, SkillStatus status, string tryOut) {
            ProgressionSlot slot = held.Slot;
            bool parent = ShrineView.ParentOwned(slot, out string parents);
            uint cap = 0u;
            try { cap = Alta.Global.GlobalSettings<ProgressionSettings>.Instance.MaxBoughtSkills; } catch { }
            return new BuyFacts {
                Orb = orb, Status = ShrineView.Says(status), ParentOwned = parent, Parents = parents,
                SlotsHeld = ShrineView.SlotsHeld(), SlotCap = (int)cap, FreePoints = ShrineView.FreePoints(), Cost = (int)slot.Cost,
                Practised = ShrineView.Practised(slot),
                UltimateBlocked = UltimateRule.Blocks(orb, BluesArcheryOverhaul.Build.From(ShrineView.Owned).Ultimates),
                TryOut = tryOut,
                Above = status == SkillStatus.OwnedLocked || status == SkillStatus.TestingLockedOwned ? ShrineView.OwnedAbove(slot) : "",
            };
        }

        /// <summary>What it says to everyone else: whose build it is and, as far as this client has been told by the
        /// server (the build bits on that player's bow statuses), what it does. Never anything about the orb they hold.</summary>
        static List<string> OtherWording() {
            carries = false;
            otherKnown = Builds.ClientBuildKnown(openerShown);
            otherBuild = otherKnown ? Builds.ClientBuild(openerShown) : default;
            List<string> lines = TabletText.ForOther(openerShown, otherKnown, otherBuild, Paths.Numbers(), (testNote.Value ?? "").Trim());
            return TabletText.Fit(lines, Mathf.Max(3, maxLines.Value));
        }

        /// <summary>Rewrites the face. Runs on ShrineView.Changed and after a build while the tree is open, never per
        /// frame and never while dormant; an unchanged text is left alone.</summary>
        static readonly int T_Write = Timing.Section("look Tablet.Write (text)");
        static void Write() {
            using var _t = Timing.Begin(T_Write);
            if (root == null || face == null || font == null || textMaterial == null || !wantUp) return;
            List<string> lines = openerMine ? Wording() : OtherWording();
            string text = string.Join("\n", lines);
            if (text == shownText) return;
            shownText = text;

            TabletPage page = TabletLayout.Layout(lines, (testNote.Value ?? "").Trim().Length > 0, metrics, emWidth);
            float nudge = Mathf.Max(0f, carveOffset.Value);
            Color cut = Cfg.Colour(carveColour, new Color(0.07f, 0.09f, 0.12f));
            int rows = page.Rows.Count;
            for (int i = 0; i < rows; i++) {
                TabletRow row = page.Rows[i];
                if (i >= inks.Count) { carves.Add(Line("Tablet Carve", 1)); inks.Add(Line("Tablet Line", 2)); }
                Vector3 at = new Vector3(faceLeft + row.X, faceTop - row.Y, faceZ);
                Set(inks[i], row, at, Ink(row.Kind), true);
                Set(carves[i], row, at + new Vector3(nudge, -nudge, 0f), cut, nudge > 0f);
            }
            for (int i = rows; i < inks.Count; i++) { inks[i].gameObject.SetActive(false); carves[i].gameObject.SetActive(false); }
            if (rule != null) {
                rule.gameObject.SetActive(rows > 0);
                rule.localPosition = new Vector3(faceLeft + (page.RuleX0 + page.RuleX1) * 0.5f, faceTop - page.RuleY, faceZ);
                rule.localScale = new Vector3(Mathf.Max(0.001f, page.RuleX1 - page.RuleX0), ruleWidth.Value, 1f);
            }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Tablet: {(lines.Count > 0 ? lines[0] : "-")} | {lines.Count} line(s)" + (rows != lines.Count ? $" on {rows} rows" : "")
                            + (page.BodyScale < 1f ? $", body at {page.BodyScale.ToString("0.##", CultureInfo.InvariantCulture)}" : "")
                            + (openerMine ? "" : otherKnown ? " (another player's build, as the server sent it)" : " (another player's tree; their build is not known here yet)"));
        }

        /// <summary>One line of lettering on its own child object (a TextMesh brings its own renderer).</summary>
        static TextMesh Line(string name, int order) {
            var go = new GameObject(name);
            go.transform.SetParent(face!.transform, false);
            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.richText = false;
            mesh.fontSize = font!.dynamic ? FontPx : 0;
            mesh.anchor = TextAnchor.UpperLeft;
            mesh.alignment = TextAlignment.Left;
            Flat(go.GetComponent<MeshRenderer>(), textMaterial!, order);
            return mesh;
        }

        static void Set(TextMesh mesh, TabletRow row, Vector3 at, Color colour, bool show) {
            mesh.gameObject.SetActive(show);
            if (!show) return;
            int px = font!.dynamic ? FontPx : Mathf.Max(1, font.fontSize);
            mesh.characterSize = row.Size * 10f / px;   // a TextMesh letter is its pixel size x characterSize / 10 tall
            mesh.color = colour;
            if (mesh.text == row.Text) mesh.text = "";   // the same text again keeps the old mesh: its letters' places in an atlas since rebuilt
            mesh.text = row.Text;
            mesh.transform.localPosition = at;
        }
    }
}
