using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Skyfall's sigils (Task 10): clients only, looks only; nothing here decides anything or is sent. A sigil is a root (scaled to its
    // radius) with one child per band, each band turning about the root's up (its normal), and flat textured quads on each band:
    //   - rings: the game's own SpriteSheet_Combat cells (row 0 starbursts, row 1 spiked rings, row 2 hand-drawn rings, row 3 soft
    //     rings), on a copy of the game's 'Particles_Combat_White_Hard' (additive), over an alpha-blended copy (Sprites/Default) so
    //     the rings read against a day sky;
    //   - runes: the ruins' five symbols (Glyph_Ruins_Symbol_01..05), the meander groove of the ruins stone on an annulus, and the
    //     Lightbeam skill glyph in the core; always the embedded copies of the game's pixels (Resources\Skyfall_*.png, made by
    //     Art\admin_skill\make_skyfall_glyphs.py), which carry mip maps. Never the game's own textures by name: those are 512 px DXT5
    //     with no mips (bilinear, aniso 1) and alias at sky distance, and whether they are loaded varies by session;
    //   - the styles (Sky 6 bands, Ground 4, Hold 2, NailBand 2) are fixed in Sigil's constructor; colours, the under layer's strength
    //     and the sky sigil's spins are [Skyfall] settings (per client, never [shared]).
    // Every renderer is on layer 1 'TransparentFX', whose cull distance is the far clip (D\PlayerController.cs:233-239 culls only
    // layer 0 at 100 m), so a sigil in the sky is seen from far away. Tint and fade are vertex colours (the additive shader has no
    // colour property). The game's materials and textures are only read and copied, never changed. Everything is pooled under one
    // DontDestroyOnLoad host: loose pieces (at most 400) and sigils per style (Sky 6, Ground 6, Hold 8, NailBand 42: seven bands for
    // each of SkyfallPools.Storms storms); the idle warm-up builds the first storm's ahead (Prebuild, PrebuildLoose; final review I-2).
    internal static class SkyfallSigil {
        internal const int SkyLayer = 1;                                    // 'TransparentFX': only layer 0 is culled at 100 m
        internal enum Style { Sky, Ground, Hold, NailBand }
        internal enum Tex { Combat, Rune1, Rune2, Rune3, Rune4, Rune5, LightBeam, Meander, Slash }
        internal enum Blend { Add, Alpha, Both }                           // additive; alpha-blended; additive over an alpha-blended copy
        internal struct RuneSpot { public Vector3 Pos; public Quaternion Rot; public float Size; public Tex Symbol; public Color Colour; }

        const string AddName = "Particles_Combat_White_Hard";
        const int TexCount = 9, LooseCap = SkyfallPools.Loose, AnnulusSegments = 64, MeanderRepeats = 6;
        static readonly int[] SigilCap = { SkyfallPools.SkySigils, SkyfallPools.GroundSigils, SkyfallPools.HoldSigils, SkyfallPools.NailBandSigils };   // per Style
        const float SheetInset = 0.5f / 2048f;
        const float LooseMeanderInner = 0.87f;                              // a loose meander piece's inner edge, as a share of its outer edge
        // a SpriteSheet_Combat ring's middle line as a share of its cell (rows 1-3), measured from kit-previews\spritesheet_combat_rows.png;
        // constants, checked by eye in Task 14
        static readonly float[] RingShare = { 1f, 0.45f, 0.70f, 0.58f };

        // ---------------------------------------------------------------- settings

        static MelonPreferences_Entry<string>? gold, goldDeep, goldPale, cyan, cyanPale, fireHot, fire, fireDeep, charred, sigilSpin;
        static MelonPreferences_Entry<float>? underAlpha;
        // 0.18.1, the owner's play-test of 0.18.0 ("the glyphs are a bit bright and i cant really read it"): the spins halved (were
        // "-10, 14, -6, 9, -4, 3"), the golds and cyans dimmed about 25% with the same hues, the under layer 0.8 (was 0.45)
        const string DefaultSpin = "-5, 7, -3, 4.5, -2, 1.5";
        const float DefaultUnderAlpha = 0.8f;

        static readonly Color DefGold = Rgb(0xC9A84A), DefGoldDeep = Rgb(0xC99E00), DefGoldPale = Rgb(0xC8BE68), DefCyan = Rgb(0x2AA6C0),
            DefCyanPale = Rgb(0x4CB8C4), DefFireHot = Rgb(0xFFB42B), DefFire = Rgb(0xFF5B02), DefFireDeep = Rgb(0xC74E00), DefChar = Rgb(0x451709);

        static Color Rgb(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        internal static void Init(MelonPreferences_Category cat) {
            MelonPreferences_Entry<string> S(string key, string value, string text) => cat.CreateEntry(key, value, description: "Clients (looks only): " + text);
            gold = S("ColourGold", "#C9A84A", "skill gold: the sigil's rings, the rain arrows.");
            goldDeep = S("ColourGoldDeep", "#C99E00", "the gold of glyphs: the Lightbeam glyph in the sigil's core, the medallions' runes.");
            goldPale = S("ColourGoldPale", "#C8BE68", "soft glows and trails.");
            cyan = S("ColourCyan", "#2AA6C0", "ruins cyan: the runes and the meander band.");
            cyanPale = S("ColourCyanPale", "#4CB8C4", "ruins cyan: the inner rings.");
            fireHot = S("ColourFireHot", "#FFB42B", "danger fire, hottest: the ground twin, fresh scorches.");
            fire = S("ColourFire", "#FF5B02", "danger fire.");
            fireDeep = S("ColourFireDeep", "#C74E00", "danger fire, cooling.");
            charred = S("ColourChar", "#451709", "a scorch once it has cooled.");
            underAlpha = cat.CreateEntry("SigilUnderAlpha", DefaultUnderAlpha, description: "Clients (looks only): how strongly the see-through copy under each ring and rune shows, so the rings read against a day sky (0 = none).");
            sigilSpin = S("SigilSpin", DefaultSpin, "degrees per second each band of the sky sigil turns, core first; the ground twin turns at a third of these.");
            Cfg.Reloaded += Reloaded;
        }

        static void Reloaded() {
            failed = false;
            spins = null;
            warmed = false;
            artWarned = addWarned = false;                                 // the "missing" notes may be said again
            if (texturesMissing || artMissing) texturesDone = false;       // the texture pass again only when something was missing
            if (texturesMissing || artMissing || addSource == null) materialsDone = false;
        }

        internal static Color Gold => gold != null ? Cfg.Colour(gold, DefGold) : DefGold;
        internal static Color GoldDeep => goldDeep != null ? Cfg.Colour(goldDeep, DefGoldDeep) : DefGoldDeep;
        internal static Color GoldPale => goldPale != null ? Cfg.Colour(goldPale, DefGoldPale) : DefGoldPale;
        internal static Color Cyan => cyan != null ? Cfg.Colour(cyan, DefCyan) : DefCyan;
        internal static Color CyanPale => cyanPale != null ? Cfg.Colour(cyanPale, DefCyanPale) : DefCyanPale;
        internal static Color FireHot => fireHot != null ? Cfg.Colour(fireHot, DefFireHot) : DefFireHot;
        internal static Color Fire => fire != null ? Cfg.Colour(fire, DefFire) : DefFire;
        internal static Color FireDeep => fireDeep != null ? Cfg.Colour(fireDeep, DefFireDeep) : DefFireDeep;
        internal static Color Char => charred != null ? Cfg.Colour(charred, DefChar) : DefChar;

        /// <summary>[Skyfall] SigilUnderAlpha held to 0..1 (a broken value: the default).</summary>
        static float UnderAlpha {
            get {
                float a = underAlpha != null ? underAlpha.Value : DefaultUnderAlpha;
                return float.IsNaN(a) ? DefaultUnderAlpha : a < 0f ? 0f : a > 1f ? 1f : a;
            }
        }

        enum Hue { Gold, GoldDeep, GoldPale, Cyan, CyanPale, FireHot, Fire, FireDeep, Char }
        static readonly Color[] palette = new Color[9];
        static void FillPalette() {
            palette[0] = Gold; palette[1] = GoldDeep; palette[2] = GoldPale; palette[3] = Cyan; palette[4] = CyanPale;
            palette[5] = FireHot; palette[6] = Fire; palette[7] = FireDeep; palette[8] = Char;
        }

        // [Skyfall] SigilSpin, parsed once per settings load: token k is band k (a token that is not a finite number turns at 0, and a
        // missing band turns at 0); spinNumbers = how many tokens were numbers
        static float[]? spins;
        static int spinNumbers;
        static string spinWarnedFor = "";

        static float[] Spins() {
            if (spins != null) return spins;
            var s = new float[6];
            int n = 0;
            string[] parts = (sigilSpin?.Value ?? DefaultSpin).Split(',');
            for (int i = 0; i < parts.Length; i++) {
                if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)
                    || float.IsNaN(f) || float.IsInfinity(f)) continue;
                if (i < s.Length) s[i] = f;
                n++;
            }
            spinNumbers = n;
            return spins = s;
        }

        static float SpinOf(Style style, int band) {
            switch (style) {
                case Style.Sky: return band < 6 ? Spins()[band] : 0f;
                case Style.Ground: return band < 4 ? Spins()[band] / 3f : 0f;
                case Style.Hold: return band == 0 ? 40f : -25f;
                default: return band == 0 ? 12f : -18f;
            }
        }

        static int BandsOf(Style style) => style == Style.Sky ? 6 : style == Style.Ground ? 4 : 2;

        // ---------------------------------------------------------------- the art: textures, then materials

        static readonly Texture2D?[] textures = new Texture2D?[TexCount];  // the glyphs, always embedded; Combat stays null here: the sheet is the source's or sheetByName
        static Texture2D? sheetByName;
        static bool texturesDone, texturesMissing;
        static readonly HashSet<string> embeddedSaid = new HashSet<string>();

        static readonly int T_FindTextures = Timing.Section("asset SkyfallSigil.FindTextures");
        /// <summary>Every glyph (the five runes, the Lightbeam glyph, the meander, the slash) from its embedded PNG, which has mip maps;
        /// never the game's Glyph_Ruins_Symbol_01..05 or Glyph_Bow_LightBeam by name (no mips: they alias at sky distance). Only the
        /// texture with no embedded copy, SpriteSheet_Combat, is looked for by its exact name, in one pass over the loaded textures
        /// (Tablet.FindTexture's pattern); it is the fallback when the ring material does not carry the sheet. Once per session; again
        /// after a settings reload only when something was missing (a slot already filled is kept).</summary>
        static void FindTextures() {
            if (texturesDone) return;
            using var _t = Timing.Begin(T_FindTextures);
            texturesDone = true;
            if (sheetByName == null)
                foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
                    if (t != null && t.name == "SpriteSheet_Combat") { sheetByName = t; break; }
            texturesMissing = false;                                           // the sheet's absence is Ready's (artMissing): the source's own may do
            for (int k = 1; k < TexCount; k++) {
                if (textures[k] == null) textures[k] = Embedded(((Tex)k).ToString(), (Tex)k == Tex.Meander);
                if (textures[k] == null) texturesMissing = true;
            }
        }

        /// <summary>QuickshotRing.Resources.Skyfall_{name}.png (Tree.ApplyIcons' stream code) with mip maps (Trilinear, aniso 4, clamped;
        /// the meander repeats along u), named Archery_Skyfall_{name}; null (warned) when missing or unreadable.</summary>
        static Texture2D? Embedded(string name, bool repeatU) {
            byte[]? png = null;
            using (Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"QuickshotRing.Resources.Skyfall_{name}.png")) {
                if (s != null) {
                    png = new byte[s.Length];
                    int read = 0;
                    while (read < png.Length) { int got = s.Read(png, read, png.Length - read); if (got <= 0) break; read += got; }
                    if (read < png.Length) png = null;
                }
            }
            if (png == null) { MelonLogger.Warning($"[Archery] Skyfall: the embedded glyph '{name}' is missing from the DLL; the sigil is drawn without it"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(tex, png)) {
                UnityEngine.Object.Destroy(tex);
                MelonLogger.Warning($"[Archery] Skyfall: the embedded glyph '{name}' is not a readable PNG; the sigil is drawn without it");
                return null;
            }
            if (tex.mipmapCount <= 1) {                                    // LoadImage made no mip chain: copy into one that has it
                var mipped = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, true);
                mipped.SetPixels32(tex.GetPixels32());
                mipped.Apply(true, true);
                UnityEngine.Object.Destroy(tex);
                tex = mipped;
            }
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.wrapMode = TextureWrapMode.Clamp;
            if (repeatU) { tex.wrapModeU = TextureWrapMode.Repeat; tex.wrapModeV = TextureWrapMode.Clamp; }
            tex.name = "Archery_Skyfall_" + name;
            if (embeddedSaid.Add(name) && Logs.Debug)
                MelonLogger.Msg($"[Archery] Skyfall: embedded glyph '{name}' {tex.width}x{tex.height}, mipmapCount {tex.mipmapCount} (said once)");
            return tex;
        }

        static readonly Material?[] addMats = new Material?[TexCount], underMats = new Material?[TexCount];
        static Material? addSource;
        static bool materialsDone, artMissing, artWarned, addWarned, readySaid;

        /// <summary>The game's ring material: through the always-loaded 'Easter Egg Confetti Big' effect's particle renderers (the
        /// Fx.OrbLineMaterial pattern), else any loaded particle renderer, by the exact name (never its "... Emmissive" twin).</summary>
        static Material? FindAdditive() {
            PooledObjectDefinition? confetti = Fx.FindEffect("Easter Egg Confetti Big");
            if (confetti != null && confetti.Prefab != null)
                foreach (ParticleSystemRenderer r in confetti.Prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.name == AddName) return r.sharedMaterial;
            foreach (ParticleSystemRenderer r in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>())
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.name == AddName) return r.sharedMaterial;
            return null;
        }

        static readonly int T_Materials = Timing.Section("asset SkyfallSigil.Materials");
        /// <summary>The textures, then one additive copy (queue 3060) and one alpha-blended copy (queue 3050, under it) per Tex, made
        /// once and cached (a later pass after a reload only fills what was missing, on the same material objects). False when the
        /// sheet or the flat material is missing: no sigil until the settings are reloaded.</summary>
        static bool Ready() {
            if (materialsDone) return !artMissing;
            FindTextures();
            using var _t = Timing.Begin(T_Materials);
            materialsDone = true;
            if (addSource == null) addSource = FindAdditive();
            bool sheetFromSource = addSource != null && addSource.mainTexture != null;
            Texture? sheet = sheetFromSource ? addSource!.mainTexture : sheetByName;
            Material? flat = Fx.LineMaterial();
            if (sheet == null || flat == null || !flat.HasProperty("_MainTex")) {
                artMissing = true;
                if (!artWarned) { artWarned = true; MelonLogger.Warning("[Archery] Skyfall: the sigil's art (SpriteSheet_Combat or the flat material) was not found; no sigil until the settings are reloaded"); }
                return false;
            }
            artMissing = false;
            if (addSource == null && !addWarned) {
                addWarned = true;
                MelonLogger.Warning($"[Archery] Skyfall: the game's ring material '{AddName}' was not found; the sigil is drawn see-through only, without its glow, until the settings are reloaded");
            }
            for (int i = 0; i < TexCount; i++) {
                Texture? tex = i == (int)Tex.Combat ? sheet : textures[i];
                if (tex == null) continue;
                Material? under = underMats[i];
                if (under == null) underMats[i] = under = new Material(flat) { name = "Archery Skyfall under " + (Tex)i, renderQueue = 3050 };
                under.SetTexture("_MainTex", tex);
                if (addSource != null) {
                    Material? add = addMats[i];
                    if (add == null) addMats[i] = add = new Material(addSource) { name = AddName + " (Archery Skyfall " + (Tex)i + ")", renderQueue = 3060 };
                    add.SetTexture("_MainTex", tex);
                }
            }
            if (!readySaid && Logs.Summary) {                              // which source each texture came from, with its mip count
                readySaid = true;
                var line = new System.Text.StringBuilder("[Archery] Skyfall: sigil art ready: rings on '");
                line.Append(addSource != null ? addSource.name : "nothing (see-through only)").Append("' over '").Append(Fx.ShaderName(flat)).Append("'; textures:");
                for (int i = 0; i < TexCount; i++) {
                    Texture? tex = i == (int)Tex.Combat ? sheet : textures[i];
                    line.Append(i == 0 ? " " : "; ").Append((Tex)i).Append(' ');
                    if (tex == null) { line.Append("missing"); continue; }
                    if (i == (int)Tex.Combat) line.Append(sheetFromSource ? "from the ring material '" : "by name '").Append(tex.name).Append("' ");
                    else line.Append("embedded ");
                    line.Append(tex.width).Append('x').Append(tex.height).Append(" mipmapCount ").Append(tex.mipmapCount);
                }
                MelonLogger.Msg(line.Append(" (said once)").ToString());
            }
            return true;
        }

        /// <summary>Task 11's warm-up: the texture pass and the materials the first Build would make, ahead of time, so no
        /// FindObjectsOfTypeAll runs in a release's frame. Builds nothing. At once when headless, failed or already done this settings
        /// load.</summary>
        internal static void Warm() {
            if (ApplicationManager.IsHeadless || failed || warmed) return;
            warmed = true;
            try { Ready(); }
            catch (Exception e) { Fail(e); }
        }

        static bool failed, warmed;
        static void Fail(Exception e) {
            if (failed) return;
            failed = true;
            MelonLogger.Warning($"[Archery] Skyfall: the sigil could not be shown ({e.Message}); no sigil until the settings are reloaded");
        }

        /// <summary>Puts a GameObject that holds a renderer on layer 1 (a new GameObject starts on layer 0 and never takes its parent's).</summary>
        internal static void Layer(GameObject go) { if (go != null) go.layer = SkyLayer; }

        // ---------------------------------------------------------------- the host and the pools

        static GameObject? host;
        static readonly List<Quad> loose = new List<Quad>();
        static readonly List<Sigil>[] sigils = { new List<Sigil>(), new List<Sigil>(), new List<Sigil>(), new List<Sigil>() };
        static readonly HashSet<string> fullSaid = new HashSet<string>();
        static MaterialPropertyBlock? block;                                // made at the first under layer (clients only)
        static int colorId = -1;

        static Transform Host() {
            if (host == null) {
                host = new GameObject("ArcherySkyfall");
                UnityEngine.Object.DontDestroyOnLoad(host);
                Layer(host);
            }
            return host.transform;
        }

        static void Full(string pool, int cap) {
            if (fullSaid.Add(pool) && Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: the {pool} pool is full ({cap}); a look is skipped (said once)");
        }

        static readonly int T_Build = Timing.Section("look SkyfallSigil.Build (take or make a sigil)");
        /// <summary>A sigil of this style from its pool (a free one, else a new one), its bands' start angles from the seed, shown by
        /// <see cref="Sigil.Show"/>. Null when the pool is full, the art is missing or the look failed (said once).</summary>
        internal static Sigil? Build(Style style, uint seed, bool accent = false) {
            using var _t = Timing.Begin(T_Build);
            if (ApplicationManager.IsHeadless || failed) return null;
            try {
                if (!Ready()) return null;
                List<Sigil> pool = sigils[(int)style];
                Sigil? s = null;
                foreach (Sigil x in pool) if (!x.Busy && x.Alive) { s = x; break; }
                if (s == null) {
                    pool.RemoveAll(x => !x.Alive);                     // one whose objects are gone is never used again
                    int cap = SigilCap[(int)style];
                    if (pool.Count >= cap) { Full(style + " sigil", cap); return null; }
                    s = new Sigil(style, (uint)pool.Count, Host());
                    pool.Add(s);
                }
                s.Take(seed, accent);
                return s;
            }
            catch (Exception e) { Fail(e); return null; }
        }

        static readonly int T_Piece = Timing.Section("look SkyfallSigil.Piece (take or make a loose piece)");
        /// <summary>A loose textured quad from the pool, placed in world space (<see cref="Quad.Place"/>), for Tex.Combat on that
        /// SpriteSheet_Combat cell. Null when the pool is full, the art (or this glyph) is missing or the look failed (said once).</summary>
        internal static Quad? Piece(Tex tex, int row, int col, Blend blend) {
            using var _t = Timing.Begin(T_Piece);
            if (ApplicationManager.IsHeadless || failed) return null;
            try {
                if (!Ready() || underMats[(int)tex] == null) return null;
                Quad? q = null;
                foreach (Quad x in loose) if (!x.Busy && x.Alive) { q = x; break; }
                if (q == null) {
                    loose.RemoveAll(x => !x.Alive);
                    if (loose.Count >= LooseCap) { Full("loose piece", LooseCap); return null; }
                    q = new Quad(Host());
                    loose.Add(q);
                }
                q.Setup(tex, blend, LooseMeanderInner);
                if (tex == Tex.Combat) q.Cell(row, col);
                q.Take();
                return q;
            }
            catch (Exception e) { Fail(e); return null; }
        }

        static readonly int T_Prebuild = Timing.Section("look SkyfallSigil.Prebuild (one sigil ahead, idle)"),
            T_PrebuildLoose = Timing.Section("look SkyfallSigil.PrebuildLoose (loose pieces ahead, idle)");
        /// <summary>The idle warm-up (final review I-2: a first storm built 72 objects for its sky sigil and 224 for the World-Nail's bands
        /// in the frame it showed them): one more sigil of this style built into its pool, taken and released at once (never shown), until
        /// the pool holds <paramref name="want"/> (at most its cap). True when it does, or cannot (headless, failed, the art missing).</summary>
        internal static bool Prebuild(Style style, int want) {
            if (ApplicationManager.IsHeadless || failed) return true;
            using var _t = Timing.Begin(T_Prebuild);
            try {
                if (!Ready()) return true;
                List<Sigil> pool = sigils[(int)style];
                pool.RemoveAll(x => !x.Alive);
                int target = Math.Min(want, SigilCap[(int)style]);
                if (pool.Count >= target) return true;
                var s = new Sigil(style, (uint)pool.Count, Host());
                pool.Add(s);
                s.Take(0u, false);
                s.Release();
                return pool.Count >= target;
            }
            catch (Exception e) { Fail(e); return true; }
        }

        /// <summary>The idle warm-up: up to <paramref name="perCall"/> more loose pieces built into the pool (each with its see-through
        /// under copy, taken and released at once, never shown), until it holds <paramref name="want"/> (at most 400). True when it does,
        /// or cannot.</summary>
        internal static bool PrebuildLoose(int want, int perCall) {
            if (ApplicationManager.IsHeadless || failed) return true;
            using var _t = Timing.Begin(T_PrebuildLoose);
            try {
                if (!Ready()) return true;
                loose.RemoveAll(x => !x.Alive);
                int target = Math.Min(want, LooseCap);
                for (int i = 0; i < perCall && loose.Count < target; i++) {
                    var q = new Quad(Host());
                    q.Setup(Tex.Combat, Blend.Both, LooseMeanderInner);   // its under child too
                    q.Take();
                    q.Release();
                    loose.Add(q);
                }
                return loose.Count >= target;
            }
            catch (Exception e) { Fail(e); return true; }
        }

        /// <summary>SelfCheck, clients only (0 when headless or before Init): SigilSpin gives the sky sigil's 6 bands their numbers
        /// (warned once per text when it does not), and every style's unfold with the settings just loaded is a number. Returns the
        /// count evaluated.</summary>
        internal static int Check() {
            if (ApplicationManager.IsHeadless || sigilSpin == null) return 0;
            spins = null;
            Spins();
            string text = sigilSpin.Value ?? "";
            if (spinNumbers != 6 && text != spinWarnedFor) {
                spinWarnedFor = text;
                MelonLogger.Warning($"[Archery] Skyfall: SigilSpin has {spinNumbers} numbers for the sky sigil's 6 bands; the rest stand still");
            }
            float build = Skyfall.Values.BuildSeconds;
            int n = 0;
            for (int s = 0; s < SigilCap.Length; s++) {
                var style = (Style)s;
                int bands = BandsOf(style);
                for (int k = 0; k < bands; k++) {
                    if (float.IsNaN(SpinOf(style, k))) throw new InvalidOperationException("a Skyfall sigil number is not a number");
                    for (int i = 0; i <= 4; i++) {                                     // t = 0, 0.5, 1, 1.5, 2
                        if (float.IsNaN(SkyfallCurves.Unfold(i * 0.5f, k, bands, build))) throw new InvalidOperationException("a Skyfall sigil number is not a number");
                        n++;
                    }
                }
            }
            return n;
        }

        // ---------------------------------------------------------------- one quad

        static readonly Vector3[] QuadVerts = { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
        static readonly Vector2[] FullUv = { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        static readonly int[] QuadTris = { 0, 1, 2, 0, 2, 3 };
        static readonly Vector2[] cellUv = new Vector2[4];

        /// <summary>One textured quad (a unit square in its own XY plane, or for the meander an annulus of outer diameter 1), pooled.
        /// Its main renderer draws its blend; Blend.Both adds an "under" child on the same mesh with the alpha-blended copy at
        /// [Skyfall] SigilUnderAlpha. Without the game's additive material every quad draws the alpha-blended copy at full alpha.</summary>
        internal sealed class Quad {
            readonly GameObject go;
            readonly Mesh mesh;
            readonly MeshRenderer mr;
            GameObject? underGo;
            MeshRenderer? underMr;
            Color[] colours = new Color[4];
            Color painted;
            bool everPainted, annulus;
            float annulusInner = -1f;
            int cell = -2;                                                  // the UVs shown: -1 the whole texture, row * 8 + col a sheet cell
            Blend blend;
            internal Tex Tex { get; private set; }

            internal Transform T => go.transform;
            internal bool Busy { get; private set; }
            internal bool Alive => go != null;
            internal GameObject Go => go;
            internal Color Painted => painted;
            internal bool Usable => mr.sharedMaterial != null;

            internal Quad(Transform parent) {
                go = new GameObject("Skyfall Quad");
                Layer(go);
                go.transform.SetParent(parent, false);
                mesh = new Mesh { name = "Archery Skyfall Quad" };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                mr = go.AddComponent<MeshRenderer>();
                NoShadows(mr);
                MakeQuad();
                go.SetActive(false);
            }

            static void NoShadows(MeshRenderer r) {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            /// <summary>Its texture, blend and shape (the meander's annulus with this inner share of its outer edge), and the
            /// materials of now.</summary>
            internal void Setup(Tex tex, Blend b, float meanderInner) {
                Tex = tex; blend = b;
                if (tex == Tex.Meander) MakeAnnulus(meanderInner);
                else {
                    if (annulus) MakeQuad();
                    if (tex != Tex.Combat) SetCell(-1);
                }
                ApplyMaterials();
            }

            /// <summary>The materials of now on the main renderer and the under child (a reload can bring the game's additive back),
            /// and the under layer's strength ([Skyfall] SigilUnderAlpha, read here: when the piece is taken).</summary>
            internal void ApplyMaterials() {
                Material? add = addMats[(int)Tex], under = underMats[(int)Tex];
                bool useAdd = blend != Blend.Alpha && add != null;
                mr.sharedMaterial = useAdd ? add : under;
                float a = UnderAlpha;
                bool wantUnder = blend == Blend.Both && useAdd && under != null && a > 0f;
                if (wantUnder && underGo == null) {
                    underGo = new GameObject("under");
                    Layer(underGo);
                    underGo.transform.SetParent(go.transform, false);
                    underGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                    underMr = underGo.AddComponent<MeshRenderer>();
                    NoShadows(underMr);
                }
                if (wantUnder && underMr != null) {
                    underMr.sharedMaterial = under;
                    block ??= new MaterialPropertyBlock();
                    if (colorId < 0) colorId = Shader.PropertyToID("_Color");
                    block.Clear();
                    block.SetColor(colorId, new Color(1f, 1f, 1f, a));   // Sprites/Default multiplies _Color into the vertex colour
                    underMr.SetPropertyBlock(block);
                }
                if (underGo != null && underGo.activeSelf != wantUnder) underGo.SetActive(wantUnder);
            }

            internal void Take() {
                Busy = true;
                if (!go.activeSelf) go.SetActive(true);
            }

            /// <summary>Local to its parent (the host for a loose piece = world); size = side in metres.</summary>
            internal void Place(Vector3 pos, Quaternion rot, float size) {
                Transform t = go.transform;
                t.localPosition = pos;
                t.localRotation = rot;
                t.localScale = new Vector3(size, size, size);
            }

            /// <summary>Tex.Combat only: show that SpriteSheet_Combat cell (row from the top, col from the left).</summary>
            internal void Cell(int row, int col) {
                if (Tex != Tex.Combat) return;
                row = row < 0 ? 0 : row > 7 ? 7 : row;
                col = col < 0 ? 0 : col > 7 ? 7 : col;
                SetCell(row * 8 + col);
            }

            void SetCell(int key) {
                if (annulus || key == cell) return;
                cell = key;
                if (key < 0) { mesh.uv = FullUv; return; }
                int r = key / 8, c = key % 8;
                float u0 = c / 8f + SheetInset, u1 = (c + 1) / 8f - SheetInset;
                float v0 = 1f - (r + 1) / 8f + SheetInset, v1 = 1f - r / 8f - SheetInset;
                cellUv[0] = new Vector2(u0, v0); cellUv[1] = new Vector2(u0, v1); cellUv[2] = new Vector2(u1, v1); cellUv[3] = new Vector2(u1, v0);
                mesh.uv = cellUv;
            }

            /// <summary>Vertex colours, alpha included; rewritten only when a channel moved by more than 1/255.</summary>
            internal void Paint(Color c) {
                const float Step = 1f / 255f;
                if (everPainted && Mathf.Abs(c.r - painted.r) <= Step && Mathf.Abs(c.g - painted.g) <= Step
                    && Mathf.Abs(c.b - painted.b) <= Step && Mathf.Abs(c.a - painted.a) <= Step) return;
                painted = c;
                everPainted = true;
                for (int i = 0; i < colours.Length; i++) colours[i] = c;
                mesh.colors = colours;
            }

            internal void Release() {
                Busy = false;
                if (go != null && go.activeSelf) go.SetActive(false);
            }

            void MakeQuad() {
                mesh.Clear();
                mesh.vertices = QuadVerts;
                mesh.uv = FullUv;
                colours = new Color[4];
                mesh.colors = colours;
                mesh.triangles = QuadTris;
                mesh.RecalculateBounds();
                annulus = false;
                annulusInner = -1f;
                cell = -1;
                everPainted = false;
            }

            // 64 segments; u = 6 * i / 64 (the meander six times round), v 0 on the inner edge and 1 on the outer
            void MakeAnnulus(float inner) {
                if (annulus && annulusInner == inner) return;
                int n = AnnulusSegments + 1;
                var verts = new Vector3[n * 2];
                var uv = new Vector2[n * 2];
                var tris = new int[AnnulusSegments * 6];
                for (int i = 0; i < n; i++) {
                    float a = i * Mathf.PI * 2f / AnnulusSegments;
                    var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    verts[2 * i] = dir * (0.5f * inner);
                    verts[2 * i + 1] = dir * 0.5f;
                    float u = MeanderRepeats * i / (float)AnnulusSegments;
                    uv[2 * i] = new Vector2(u, 0f);
                    uv[2 * i + 1] = new Vector2(u, 1f);
                }
                for (int i = 0; i < AnnulusSegments; i++) {
                    int t = i * 6, a0 = 2 * i;
                    tris[t] = a0; tris[t + 1] = a0 + 1; tris[t + 2] = a0 + 3;
                    tris[t + 3] = a0; tris[t + 4] = a0 + 3; tris[t + 5] = a0 + 2;
                }
                mesh.Clear();
                mesh.vertices = verts;
                mesh.uv = uv;
                colours = new Color[n * 2];
                mesh.colors = colours;
                mesh.triangles = tris;
                mesh.RecalculateBounds();
                annulus = true;
                annulusInner = inner;
                cell = -2;
                everPainted = false;
            }
        }

        // ---------------------------------------------------------------- one sigil

        /// <summary>One composed sigil, pooled per style: Root (scaled to the radius, so every share below is in radii), one child per
        /// band (turned about its local y), the pieces flat in each band's XZ plane. A reused sigil keeps its rune symbols; the seed
        /// sets only each band's start angle.</summary>
        internal sealed class Sigil {
            sealed class Part { public Quad Q = null!; public Hue Hue, AccentHue; public float Alpha = 1f, Side; public bool Rune, AccentOnly; }
            sealed class Band { public Transform T = null!; public readonly List<Part> Parts = new List<Part>(); public float Start; }

            readonly Style style;
            readonly GameObject root;
            readonly Band[] bands;
            readonly uint slot;
            int runeIndex;
            bool accent;

            internal bool Busy { get; private set; }
            internal bool Alive => root != null;

            internal Sigil(Style style, uint poolSlot, Transform parent) {
                this.style = style;
                slot = poolSlot;
                root = new GameObject("Skyfall Sigil " + style);
                Layer(root);
                root.transform.SetParent(parent, false);
                bands = new Band[BandsOf(style)];
                for (int k = 0; k < bands.Length; k++) {
                    var b = new GameObject("band " + k);
                    Layer(b);
                    b.transform.SetParent(root.transform, false);
                    bands[k] = new Band { T = b.transform };
                }
                switch (style) {
                    case Style.Sky:
                        Centred(0, Tex.LightBeam, 0.34f, Hue.GoldDeep, 1f, Blend.Both);
                        CombatAt(0, 0, 3, 0.5f, 0f, 0f, Hue.GoldPale, 1f, Blend.Add);                   // the starburst
                        Ring(0, 3, 2, 0.3f, Hue.Gold, 0.5f, Blend.Add);
                        for (int k = 0; k < 6; k++) {                                                    // the medallions
                            float th = 30f + 60f * k;
                            CombatAt(1, 2, 3, 0.15f, 0.42f, th, Hue.Gold, 1f, Blend.Both);
                            RuneAt(1, 0.09f, 0.42f, th, Hue.GoldDeep, Hue.GoldDeep, Blend.Both);
                        }
                        Ring(2, 1, 2, 0.52f, Hue.Gold, 1f, Blend.Both);
                        Runes(3, 14, 0.1f, 0.7f, Hue.Cyan, Hue.Cyan, Blend.Both);
                        Ring(3, 2, 4, 0.62f, Hue.CyanPale, 1f, Blend.Both);
                        Meander(4, 0.82f, 0.94f, Hue.Cyan, Blend.Both);
                        Ring(5, 2, 0, 1f, Hue.Gold, 1f, Blend.Both);                                     // the rim
                        Ring(5, 3, 1, 1f, Hue.GoldPale, 0.5f, Blend.Add);
                        break;
                    case Style.Ground:
                        Centred(0, Tex.LightBeam, 0.3f, Hue.FireHot, 1f, Blend.Add);
                        Runes(1, 10, 0.1f, 0.58f, Hue.Fire, Hue.Fire, Blend.Add);
                        Ring(1, 2, 2, 0.5f, Hue.FireHot, 1f, Blend.Add);
                        Meander(2, 0.74f, 0.86f, Hue.Fire, Blend.Add);
                        Ring(3, 2, 0, 1f, Hue.FireHot, 1f, Blend.Add);
                        Ring(3, 3, 1, 1f, Hue.Fire, 0.5f, Blend.Add);
                        break;
                    case Style.Hold:
                        Ring(0, 2, 1, 1f, Hue.Gold, 1f, Blend.Both, Hue.CyanPale);
                        Ring(0, 3, 3, 1f, Hue.GoldPale, 0.5f, Blend.Add);
                        Runes(1, 8, 0.22f, 0.8f, Hue.GoldDeep, Hue.Cyan, Blend.Both);
                        break;
                    default:                                                                             // NailBand
                        Ring(0, 2, 0, 1f, Hue.Gold, 1f, Blend.Both);
                        Ring(0, 3, 2, 1f, Hue.GoldPale, 0.5f, Blend.Add);
                        Ring(0, 2, 3, 0.45f, Hue.CyanPale, 1f, Blend.Both, null, accentOnly: true);
                        Runes(1, 12, 0.08f, 0.83f, Hue.GoldDeep, Hue.GoldDeep, Blend.Both);
                        break;
                }
                root.SetActive(false);
            }

            // ---- the composition helpers: a piece at angle th (degrees) and radius share r round its band, flat, its top outward

            Part Add(int band, Tex tex, Blend blend, float side, float r, float th, Hue hue, Hue accentHue, float alpha, bool rune, bool accentOnly, float meanderInner = 0f) {
                var q = new Quad(bands[band].T);
                q.Setup(tex, blend, meanderInner);
                float rad = th * Mathf.Deg2Rad;
                q.Place(new Vector3(Mathf.Sin(rad) * r, 0f, Mathf.Cos(rad) * r), Quaternion.AngleAxis(th, Vector3.up) * Quaternion.Euler(90f, 0f, 0f), side);
                var p = new Part { Q = q, Hue = hue, AccentHue = accentHue, Alpha = alpha, Side = side, Rune = rune, AccentOnly = accentOnly };
                bands[band].Parts.Add(p);
                return p;
            }

            void Centred(int band, Tex tex, float size, Hue hue, float alpha, Blend blend) => Add(band, tex, blend, size, 0f, 0f, hue, hue, alpha, false, false);

            void CombatAt(int band, int row, int col, float size, float r, float th, Hue hue, float alpha, Blend blend) =>
                Add(band, Tex.Combat, blend, size, r, th, hue, hue, alpha, false, false).Q.Cell(row, col);

            // a centred SpriteSheet_Combat ring of radius r: a quad of side 2r / RingShare[row]
            void Ring(int band, int row, int col, float r, Hue hue, float alpha, Blend blend, Hue? accentHue = null, bool accentOnly = false) =>
                Add(band, Tex.Combat, blend, 2f * r / RingShare[row], 0f, 0f, hue, accentHue ?? hue, alpha, false, accentOnly).Q.Cell(row, col);

            void RuneAt(int band, float size, float r, float th, Hue hue, Hue accentHue, Blend blend) {
                var symbol = (Tex)((int)Tex.Rune1 + (int)(5f * LookSeed.Unit(slot, runeIndex++, 41)));
                Add(band, symbol, blend, size, r, th, hue, accentHue, 1f, true, false);
            }

            void Runes(int band, int count, float size, float r, Hue hue, Hue accentHue, Blend blend) {
                for (int i = 0; i < count; i++) RuneAt(band, size, r, 360f * i / count, hue, accentHue, blend);
            }

            // the meander groove on an annulus from radius share inner to outer
            void Meander(int band, float inner, float outer, Hue hue, Blend blend) =>
                Add(band, Tex.Meander, blend, 2f * outer, 0f, 0f, hue, hue, 1f, false, false, inner / outer);

            // ---- use

            internal void Take(uint seed, bool withAccent) {
                Busy = true;
                accent = withAccent;
                for (int k = 0; k < bands.Length; k++) {
                    Band b = bands[k];
                    b.Start = 360f * LookSeed.Unit(seed, k, 43);
                    if (b.T.gameObject.activeSelf) b.T.gameObject.SetActive(false);   // shown from the first Show, never with the last use's look
                    foreach (Part p in b.Parts) {
                        p.Q.ApplyMaterials();
                        bool on = p.Q.Usable && (!p.AccentOnly || accent);
                        if (p.Q.Go.activeSelf != on) p.Q.Go.SetActive(on);
                    }
                }
                if (!root.activeSelf) root.SetActive(true);
            }

            /// <summary>Facing's up (y) is the sigil's normal; local to the host = world.</summary>
            internal void Place(Vector3 centre, Quaternion facing, float radius) {
                Transform t = root.transform;
                t.localPosition = centre;
                t.localRotation = facing;
                t.localScale = new Vector3(radius, radius, radius);
            }

            static readonly int T_Show = Timing.Section("look SkyfallSigil.Show");
            /// <summary>Core-first unfold (SkyfallCurves.Unfold over <paramref name="openSeconds"/>), spin and fade: band k shows when
            /// it has begun to open, bit k of <paramref name="mask"/> is set and <paramref name="alpha"/> is above 0; its scale is
            /// 0.6 + 0.4 x open, its angle its start plus its spin x the time since it opened (from 0), and every piece is painted its
            /// colour at its own alpha x alpha x open. No allocation.</summary>
            internal void Show(float sinceOpen, float openSeconds, float alpha, int mask = ~0) {
                using var _t = Timing.Begin(T_Show);
                alpha = alpha > 0f ? (alpha < 1f ? alpha : 1f) : 0f;       // NaN: 0
                FillPalette();
                float turned = sinceOpen > 0f ? sinceOpen : 0f;
                for (int k = 0; k < bands.Length; k++) {
                    Band b = bands[k];
                    float open = SkyfallCurves.Unfold(sinceOpen, k, bands.Length, openSeconds);
                    bool on = open > 0f && alpha > 0f && (mask & (1 << k)) != 0;
                    GameObject bg = b.T.gameObject;
                    if (bg.activeSelf != on) bg.SetActive(on);
                    if (!on) continue;
                    float s = 0.6f + 0.4f * open;
                    b.T.localScale = new Vector3(s, s, s);
                    b.T.localRotation = Quaternion.Euler(0f, b.Start + SpinOf(style, k) * turned, 0f);
                    float a = alpha * open;
                    for (int i = 0; i < b.Parts.Count; i++) {
                        Part p = b.Parts[i];
                        if (!p.Q.Usable || (p.AccentOnly && !accent)) continue;
                        Color c = palette[(int)(accent ? p.AccentHue : p.Hue)];
                        c.a *= p.Alpha * a;
                        p.Q.Paint(c);
                    }
                }
            }

            /// <summary>Where every rune of a shown band is now (Task 12's shatter): its world position and rotation, its world side
            /// (side x Root.lossyScale.x), its symbol and its colour as last painted. Appends; returns how many.</summary>
            internal int Runes(List<RuneSpot> into) {
                if (into == null || !Busy) return 0;
                int n = 0;
                float scale = root.transform.lossyScale.x;
                foreach (Band b in bands) {
                    if (!b.T.gameObject.activeSelf) continue;
                    foreach (Part p in b.Parts) {
                        if (!p.Rune || !p.Q.Go.activeSelf) continue;
                        Transform t = p.Q.T;
                        into.Add(new RuneSpot { Pos = t.position, Rot = t.rotation, Size = p.Side * scale, Symbol = p.Q.Tex, Colour = p.Q.Painted });
                        n++;
                    }
                }
                return n;
            }

            internal void Release() {
                Busy = false;
                if (root != null && root.activeSelf) root.SetActive(false);
            }
        }
    }
}
