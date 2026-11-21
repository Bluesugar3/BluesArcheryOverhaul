using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace QuickshotRing {
    // Remembers each player's main path (the rule is in Path.cs). The server keeps one line per account in
    // UserData\QuickshotRing\main_paths.txt, written when a real (not test-build) decision changes it. The shooter's
    // own client keeps its own incumbent in main_path_local.txt, corrected whenever the server's build bits disagree.
    // File errors are a warning once; an unreadable file is "no history".
    internal static class Paths {
        const string LocalKey = "local";
        static PathStore? server, local;
        static bool warnedServer, warnedLocal;
        // Account key -> the main path last logged (real or test build), so the log shows each change once.
        static readonly Dictionary<string, MainPath> shown = new Dictionary<string, MainPath>();

        static string ServerFile => Path.Combine(Cfg.Folder, "main_paths.txt");
        static string LocalFile => Path.Combine(Cfg.Folder, "main_path_local.txt");

        // ---------------------------------------------------------------- complements (the numbers are in Path.cs)

        static MelonPreferences_Entry<float>? needleShare, snapStagger, volleyFaster, spreadTighter, ghostStagger, stageFaster, heavyShare, ghostDamage, sideOrbOffPath, volleyStage;
        static MelonPreferences_Entry<int>? needlesPerOrb, snapInterruptFrom, heavyPerOrb;

        /// <summary>The live complement numbers (shared settings, [Complements]); the defaults until Init ran.</summary>
        internal static ComplementValues Values => needleShare == null ? ComplementValues.Default
            : new ComplementValues(needleShare.Value, needlesPerOrb!.Value, snapStagger!.Value, snapInterruptFrom!.Value, volleyFaster!.Value,
                                   spreadTighter!.Value, ghostStagger!.Value, stageFaster!.Value, heavyPerOrb!.Value, heavyShare!.Value, ghostDamage!.Value,
                                   volleyStage!.Value);

        /// <summary>This build's charge stages: the shared stage times, sooner for a Charge main with Quick orbs.</summary>
        internal static ChargeStages StagesFor(Build b) => Tree.Stages.Scaled(Complements.StageFactor(b, Values));

        /// <summary>This build's volley timeline: shorter for a Multi main with Quick orbs.</summary>
        internal static VolleyTimes VolleyTimesFor(Build b) => Volley.Times.Scaled(Complements.VolleyFactor(b, Values));

        /// <summary>This build's wait for Holy Arrow, shortened by the same factor as the charge stages.</summary>
        internal static float HolySecondsFor(Build b) => Tree.HolySeconds * Complements.StageFactor(b, Values);

        /// <summary>The game's shared timers: one per skill for every player, so they are set to the fastest build's.</summary>
        internal static float SharedStageFactor => Complements.MinFactor(Values, true);
        internal static float SharedVolleyFactor => Complements.MinFactor(Values, false);

        /// <summary>The live numbers the shrine's tablet shows (Preview.cs): complements, stage and volley times, Holy Arrow's
        /// wait, the volley shares, the game's Quickdraw and Charge Shot damage tables and Weight's stagger, tiers 0..3.</summary>
        internal static FactNumbers Numbers() {
            var quick = new float[4];
            var charge = new float[4];
            var stagger = new float[4];
            WeightValues w = Weight.Values;
            for (int t = 0; t < 4; t++) {
                quick[t] = Tree.QuickMultiplier(t);
                charge[t] = Tree.ChargeDamageAt(t); // play-test 7: the tablet shows the mod's own table
                stagger[t] = WeightRules.TierStagger(t, w);
            }
            return new FactNumbers(Values, Tree.Stages, Volley.Times, Tree.HolySeconds, Volley.InnerShare, Volley.OuterShare, quick, charge, stagger, OrbNumbers);
        }

        // ---------------------------------------------------------------- the later orbs' numbers (OrbValues in Path.cs)

        static MelonPreferences_Entry<float>? ricochetMetres, seekerShare, shatterPerOrb, shatterBlast, ricochetJumpShare;
        static MelonPreferences_Entry<int>? shatterPellets, ricochetSpread;

        static OrbValues? orbCache;

        /// <summary>The live numbers of the later orbs (shared settings, one section per orb); the defaults until Init ran.
        /// Built once per settings load (Cfg.Reloaded, SelfCheck.Run after Cfg.Ready) and cached. fixF C-1: it reads only settings
        /// entries and leaf accessors (Quickdraw4.PierceShares, Shatter.MetresSetting), never another holder built from it
        /// (Shatter.Blast reads this one: reading Blast here was an endless recursion).</summary>
        internal static OrbValues OrbNumbers {
            get {
                if (ricochetMetres == null) return OrbValues.Default;
                if (orbCache.HasValue) return orbCache.Value;
                OrbValues v = new OrbValues(ricochetMetres.Value, seekerShare!.Value, shatterPellets!.Value, shatterPerOrb!.Value, shatterBlast!.Value,
                            ricochetJumpShare!.Value, Quickdraw4.PierceShares, Shatter.MetresSetting, ricochetSpread!.Value,
                            sideOrbOffPath != null ? sideOrbOffPath.Value : SideOrbShare.DefaultOffPath, Seek.Values,   // H1: Seek.Values is a leaf
                            Ricochet.Bounces);   // H5b: a leaf (reads its own entry only)
                orbCache = v;
                return v;
            }
        }

        /// <summary>Drops the cached orb numbers: the next read rebuilds them from the settings (after a load or a reload).</summary>
        internal static void ForgetCachedNumbers() => orbCache = null;

        static void InitOrbs() {
            OrbValues d = OrbValues.Default;
            var ricochet = Cfg.Category("Ricochet");
            ricochetMetres = Cfg.Shared(ricochet.CreateEntry("JumpMetres", d.RicochetMetres, description: "Ricochet (needs Burst; full strength for a multi-arrow main, [Complements] SideOrbOffPathShare for any other): every arrow of the release that hits a creature (ghost, pellet, needle, heavy arrow, the real arrow or a Quickdraw 4 line) jumps on to one of the nearest other creatures within this many metres (play-test 7: was 8)."));
            ricochetJumpShare = Cfg.Shared(ricochet.CreateEntry("JumpShare", d.RicochetJumpShare, description: "Ricochet: each jump's damage, as a share of that arrow's own damage."));
            ricochetSpread = Cfg.Shared(ricochet.CreateEntry("JumpSpread", d.RicochetSpread, description: "Ricochet: successive jumps from one struck creature go round this many nearest other creatures in turn (with one there, all go to it)."));
            seekerShare = Cfg.Shared(Cfg.Category("SeekingVolley").CreateEntry("SeekerShare", d.SeekerShare, description: "Seeking Volley: each homing ghost's and pellet's damage on a locked release, as a share of its usual share (the owner: full damage, 1.0). The core arrows carry the real arrow's own damage (CoreShare)."));
            var shatter = Cfg.Category("Shatter");
            // play-test 7: Shatter is a blast in a cone behind the struck creature (Splinters, SplintersPerOrb and Share are retired)
            shatterBlast = Cfg.Shared(shatter.CreateEntry("BlastShare", d.ShatterBlast, description: "Shatter: a creature right behind the struck one, on the arrow's line within FullMetres, takes this many times the damage the shattering hit really dealt (a Charge Shot main's charged arrow; for any other main path the first creature hit of any release, times [Complements] SideOrbOffPathShare); less with distance and angle."));
            shatterPerOrb = Cfg.Shared(shatter.CreateEntry("PerMultiOrb", d.ShatterPerOrb, description: "Shatter: added to BlastShare per multi-arrow orb owned (Two-Shot, Three-Shot, Burst)."));
            shatterPellets = shatter.CreateEntry("Pellets", d.ShatterPellets, description: "Shatter: how many pellet streaks the blast's look draws (looks only).");
        }

        /// <summary>For the release log: the main path and this build's own stage times and volley factor.</summary>
        internal static string TimingOf(Build b) {
            ChargeStages st = StagesFor(b);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "main {0} stages {1:0.###}/{2:0.###}/{3:0.###} volley x{4:0.##}",
                b.Main, st.S1, st.S2, st.S3, Complements.VolleyFactor(b, Values));
        }

        static void InitComplements() {
            var cat = Cfg.Category("Complements");
            ComplementValues d = ComplementValues.Default;
            needleShare = Cfg.Shared(cat.CreateEntry("NeedleShare", d.NeedleShare, description: "Quickdraw main with multi-arrow orbs: each needle's damage, as a share of the arrow's."));
            needlesPerOrb = Cfg.Shared(cat.CreateEntry("NeedlesPerOrb", d.NeedlesPerOrb, description: "Quickdraw main: needles beside every quick shot, per multi-arrow orb."));
            snapStagger = Cfg.Shared(cat.CreateEntry("SnapStaggerPerOrb", d.SnapStaggerPerOrb, description: "Quickdraw main with Charge Shot orbs: stagger every quick shot deals, per Charge Shot orb."));
            snapInterruptFrom = Cfg.Shared(cat.CreateEntry("SnapInterruptFrom", d.SnapInterruptFrom, description: "Quickdraw main: with at least this many Charge Shot orbs a quick shot interrupts."));
            volleyFaster = Cfg.Shared(cat.CreateEntry("VolleyFasterPerOrb", d.VolleyFasterPerOrb, description: "Multi-arrow main with Quickdraw orbs: the whole volley timeline is this much shorter per Quickdraw orb (never below 25%)."));
            spreadTighter = Cfg.Shared(cat.CreateEntry("SpreadTighterPerOrb", d.SpreadTighterPerOrb, description: "Multi-arrow main: the ghosts' spread narrows this much per Quickdraw orb."));
            ghostStagger = Cfg.Shared(cat.CreateEntry("GhostStaggerPerOrb", d.GhostStaggerPerOrb, description: "Multi-arrow main with Charge Shot orbs: stagger per ghost for each hold stage reached (VolleyStageSeconds)."));
            stageFaster = Cfg.Shared(cat.CreateEntry("StageFasterPerOrb", d.StageFasterPerOrb, description: "Charge Shot main with Quickdraw orbs: every charge stage and Holy Arrow's wait completes this much sooner per Quickdraw orb (never below 25%)."));
            heavyPerOrb = Cfg.Shared(cat.CreateEntry("HeavyPerOrb", d.HeavyPerOrb, description: "Charge Shot main with multi-arrow orbs: heavy arrows beside every charged shot, per multi-arrow orb."));
            heavyShare = Cfg.Shared(cat.CreateEntry("HeavyShare", d.HeavyShare, description: "Charge Shot main: each heavy arrow's damage, as a flat share of the arrow's."));
            ghostDamage = Cfg.Shared(cat.CreateEntry("GhostDamagePerOrb", d.GhostDamagePerOrb, description: "Multi-arrow main with Charge Shot orbs: its ghosts' and pellets' damage rises by this share for each hold stage reached (0.1 = +10%; three stages +30%); the real arrow is not changed. The ghosts' stagger is GhostStaggerPerOrb."));
            volleyStage = Cfg.Shared(cat.CreateEntry("VolleyStageSeconds", d.VolleyStageSeconds, description: "Multi-arrow main with Charge Shot orbs: once the volley is full (fifteen ghosts; with Burst, after the rack), each Charge Shot orb owned adds one hold stage, reached this many seconds apart. Released at stage s, the ghosts and pellets deal +GhostDamagePerOrb x s and each ghost carries GhostStaggerPerOrb x s, and the ghosts turn gold from stage 1. Released before stage 1: a plain volley."));
            sideOrbOffPath = Cfg.Shared(cat.CreateEntry("SideOrbOffPathShare", SideOrbShare.DefaultOffPath, description: "Shatter and Ricochet work for every main path: off their own line (Shatter: Charge Shot, Ricochet: multi-arrow), and for an archer with no main path, their damage is this share of what it would be (0 to 1; 0 = they do nothing off their line)."));
        }

        internal static void Init() {
            InitComplements();
            InitOrbs();
            Cfg.Reloaded += ForgetCachedNumbers;
            server = Load(ServerFile, ref warnedServer);
            local = Load(LocalFile, ref warnedLocal);
            MelonLogger.Msg($"[Archery] Path: remembered main paths loaded (server file {(File.Exists(ServerFile) ? "found" : "none")}, local {local.Get(LocalKey)})");
        }

        static readonly int T_Load = Timing.Section("file Paths.Load");
        static PathStore Load(string file, ref bool warned) {
            using var _t = Timing.Begin(T_Load, file);
            try { return PathStore.Parse(File.Exists(file) ? File.ReadAllText(file) : null); }
            catch (Exception e) {
                if (!warned) { warned = true; MelonLogger.Warning($"[Archery] Path: cannot read {file}; treated as no history: {e.Message}"); }
                return PathStore.Parse(null);
            }
        }

        static readonly int T_Save = Timing.Section("file Paths.Save");
        static void Save(PathStore store, string file, ref bool warned) {
            using var _t = Timing.Begin(T_Save, file);
            try {
                Directory.CreateDirectory(Cfg.Folder);
                File.WriteAllText(file, store.Serialize());
            }
            catch (Exception e) {
                if (!warned) { warned = true; MelonLogger.Warning($"[Archery] Path: cannot write {file}; the main path is kept in memory only: {e.Message}"); }
            }
        }

        static PathStore Server => server ??= Load(ServerFile, ref warnedServer);
        static PathStore Local => local ??= Load(LocalFile, ref warnedLocal);

        static string Counts(Build b) =>
            $"Quick {MainPathRule.Count(b, MainPath.Quick)}, Multi {MainPathRule.Count(b, MainPath.Multi)}, Charge {MainPathRule.Count(b, MainPath.Charge)}";

        /// <summary>Server: decide the main path for these counts with the remembered one as the incumbent. A real
        /// decision that changes it is stored and written; a test build or an orb being tried out is decided in
        /// memory only.</summary>
        static readonly int T_Stamp = Timing.Section("Paths.Stamp (main path)");
        internal static Build Stamp(Build counts, string accountKey, bool testBuild, string? name = null, bool tryingOut = false) {
            using var _t = Timing.Begin(T_Stamp, accountKey);
            MainPath remembered = Server.Get(accountKey);
            MainPath main = MainPathRule.Decide(counts, remembered);
            if (!testBuild && !tryingOut && MainPathRule.ShouldRemember(main, remembered) && Server.Set(accountKey, main)) Save(Server, ServerFile, ref warnedServer);
            MainPath before = shown.TryGetValue(accountKey, out MainPath s) ? s : remembered;
            if (main != before) {
                string why = testBuild ? "test build, not saved"
                           : tryingOut ? "trying an orb out, not saved"
                           : main == remembered ? "back to the saved path"
                           : MainPathRule.Why(counts, before, main);
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Path: '{name ?? accountKey}' main path {before} -> {main} ({Counts(counts)}; {why})");
            }
            shown[accountKey] = main;
            return counts.With(main);
        }

        /// <summary>Server, the play-test range (BluesArcheryTestKit's TestRange: reset, restore): sets the remembered main path of this account (None
        /// forgets it), written at once. Returns the one it had.</summary>
        internal static MainPath SetRemembered(string accountKey, MainPath main) {
            MainPath had = Server.Get(accountKey);
            if (Server.Set(accountKey, main)) Save(Server, ServerFile, ref warnedServer);
            shown.Remove(accountKey);
            return had;
        }

        /// <summary>Server: the same decision without storing or logging, for builds that are not final (save still
        /// loading, archery tree not mapped yet).</summary>
        internal static Build Peek(Build counts, string accountKey) =>
            counts.With(MainPathRule.Decide(counts, Server.Get(accountKey)));

        /// <summary>Shooter's client: the main path for the local player's own counts, with the local file's
        /// incumbent; written when it changes.</summary>
        internal static MainPath LocalIncumbent(Build counts) {
            MainPath remembered = Local.Get(LocalKey);
            MainPath main = MainPathRule.Decide(counts, remembered);
            if (MainPathRule.ShouldRemember(main, remembered) && Local.Set(LocalKey, main)) {
                Save(Local, LocalFile, ref warnedLocal);
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Path: local main path {remembered} -> {main} ({Counts(counts)}; {MainPathRule.Why(counts, remembered, main)})");
            }
            return main;
        }


        /// <summary>Shooter's client: the main path the local file remembers (read only).</summary>
        internal static MainPath LocalRemembered => Local.Get(LocalKey);

        /// <summary>Shooter's client: the server's build bits named the local player's main path; the server wins.</summary>
        internal static void FromServer(MainPath main) {
            MainPath remembered = Local.Get(LocalKey);
            // The server never forgets a path on zero counts (ShouldRemember), so neither does the local file.
            if (!MainPathRule.ShouldRemember(main, remembered) || !Local.Set(LocalKey, main)) return;
            Save(Local, LocalFile, ref warnedLocal);
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Path: the server says the local main path is {main} (local file had {remembered}); local file corrected");
        }
    }
}
