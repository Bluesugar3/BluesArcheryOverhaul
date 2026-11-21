using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace QuickshotRing {
    // All of the mod's settings live in one file, UserData\QuickshotRing.cfg, and are re-read while the game
    // runs: save the file and the change is live within two seconds. This is what makes the art cheap to tune.
    internal static class Cfg {
        static readonly List<MelonPreferences_Category> categories = new List<MelonPreferences_Category>();
        // Raise this whenever shipped defaults change. A settings file saved by an older version is then put back
        // to the new defaults once (hand-made changes in it are lost, which the log says).
        const int SettingsVersion = 3;
        static string path = "";
        internal static string DefaultsFile => Path.Combine(Folder, "shipped_defaults.txt");
        static DateTime stamp;
        static float nextPoll;

        /// <summary>Raised after the file changed on disk and the new values are loaded.</summary>
        internal static event Action? Reloaded;

        static readonly List<MelonPreferences_Entry> shared = new List<MelonPreferences_Entry>();

        /// <summary>Hash of every [shared] setting's value, recomputed after each load; the server and every client must agree on it.</summary>
        internal static uint SharedChecksum { get; private set; }
        internal static byte SharedShort { get; private set; }

        internal static string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "QuickshotRing");

        /// <summary>H4 (range minor 9): a whole file at once: written beside it, then moved over it (File.Replace), so a reader never
        /// reads half a file and its open read (FileShare.Delete) never blocks the write; a few tries, then a plain write. Skyfall's
        /// grant file, and the play-test kit's status files.</summary>
        internal static void WriteWhole(string file, string text) {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, text);
            for (int i = 0; ; i++) {
                try {
                    if (File.Exists(file)) File.Replace(tmp, file, null);
                    else File.Move(tmp, file);
                    return;
                }
                catch (Exception e) when (i < 3 && (e is IOException || e is UnauthorizedAccessException)) { System.Threading.Thread.Sleep(15); }
                catch (Exception) {
                    File.WriteAllText(file, text);
                    try { File.Delete(tmp); } catch { }
                    return;
                }
            }
        }

        internal static MelonPreferences_Category Category(string name) {
            if (path.Length == 0) path = Path.Combine(MelonEnvironment.UserDataDirectory, "QuickshotRing.cfg");
            foreach (MelonPreferences_Category known in categories) if (known.Identifier == name) return known; // two owners of one table
            MelonPreferences_Category c = MelonPreferences.CreateCategory(name);
            c.SetFilePath(path, true, false);
            categories.Add(c);
            return c;
        }

        /// <summary>Marks a setting the server's damage or timing depends on: it must match on the server and every client.</summary>
        internal static MelonPreferences_Entry<T> Shared<T>(MelonPreferences_Entry<T> entry) {
            if (!shared.Contains(entry)) shared.Add(entry);
            string d = entry.Description ?? "";
            if (!d.StartsWith("[shared] ", StringComparison.Ordinal)) entry.Description = "[shared] " + d;
            return entry;
        }

        static void UpdateChecksum(bool startup) {
            SharedChecksum = SettingsChecksum.Compute(shared.Select(e =>
                new KeyValuePair<string, string>(e.Category.Identifier + "." + e.Identifier, SettingsChecksum.Format(e.BoxedValue))));
            SharedShort = SettingsChecksum.Short(SharedChecksum);
            if (startup || Logs.Summary) MelonLogger.Msg($"[Archery] Settings: shared checksum {SharedChecksum:x8} (short {SharedShort:x2}) over {shared.Count} settings");
        }

        /// <summary>Call once after every entry is created: writes the file so all settings are visible in it.</summary>
        internal static void Ready() {
            MelonPreferences_Entry<int> version = Category("Settings").CreateEntry("Version", 0, description: "Written by the mod. Do not edit. Settings whose description starts with [shared] must have the same value in the server's and every client's QuickshotRing.cfg; compare the '[Archery] Settings: shared checksum' log line on both sides.");
            if (version.Value < SettingsVersion) {
                foreach (MelonPreferences_Category c in categories)
                    foreach (MelonPreferences_Entry e in c.Entries) e.ResetToDefault();
                version.Value = SettingsVersion;
                MelonLogger.Msg($"[Archery] Settings file was from an older version: every setting is back to this version's defaults (settings version {SettingsVersion}).");
            }
            MigrateSettings();
            Logs.Refresh(); // after any reset or migration
            foreach (MelonPreferences_Category c in categories) c.SaveToFile(false);
            stamp = SafeStamp();
            UpdateChecksum(true);
        }

        /// <summary>Per-key migration: a setting still at its old shipped default moves to the new default, a hand-tuned one is kept.
        /// The shipped defaults of this run are recorded in shipped_defaults.txt for the next one.</summary>
        static void MigrateSettings() {
            Dictionary<string, string>? recorded = null;
            try { if (File.Exists(DefaultsFile)) recorded = SettingsMigration.Parse(File.ReadAllText(DefaultsFile)); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Settings: could not read {DefaultsFile}: {e.Message}"); }
            var now = new Dictionary<string, string>();
            foreach (MelonPreferences_Category c in categories) {
                foreach (MelonPreferences_Entry e in c.Entries) {
                    if (c.Identifier == "Settings" && e.Identifier == "Version") continue;
                    string key = c.Identifier + "." + e.Identifier;
                    string current = SettingsChecksum.Format(e.BoxedValue);
                    string newDefault = SettingsChecksum.Format(Traverse.Create(e).Property("DefaultValue").GetValue());
                    now[key] = newDefault;
                    string? old = SettingsMigration.OldDefault(key, recorded);
                    if (SettingsMigration.ShouldReset(key, current, old, newDefault)) {
                        e.ResetToDefault();
                        MelonLogger.Msg($"[Archery] Settings: {key} reset to the new default {newDefault} ({(current == old ? $"it was still the old default {old}" : "it held a text this mod shipped earlier")})");
                    }
                    else if (old != null && old != newDefault && current != newDefault)
                        MelonLogger.Msg($"[Archery] Settings: {key} kept at your value {current} (its default changed from {old} to {newDefault})");

                }
            }
            try {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(DefaultsFile, SettingsMigration.Format(now));
                MelonLogger.Msg($"[Archery] Settings: shipped defaults recorded for {now.Count} settings ({DefaultsFile})");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Settings: could not write {DefaultsFile}: {e.Message}"); }
        }

        internal static void Poll() {
            if (path.Length == 0 || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 2f;
            DateTime now = SafeStamp();
            if (now == stamp && retriesLeft <= 0) return;
            if (now != stamp) retriesLeft = 0;
            stamp = now;
            using var _t = Timing.Begin(TReload);
            try {
                // G5 (G2 review I-1): MelonLoader latches its File's error flag after one failed read (a typo saved mid-edit, or the
                // file still locked by the writer; its own watcher thread may have read first) and from then on every load and save
                // of the file is a silent no-op. Cleared before the load, so this load really reads the file.
                bool hadError = ClearFileError();
                // G2: ONE load. Every category shares one MelonLoader File object (SetFilePath reuses it by path), and
                // LoadFromFile -> MelonPreferences.LoadFileAndRefreshCategories parses the file once and refreshes every entry of
                // every category on that File (MelonLoader 0.7.3, decompiled). A load per category re-parsed the 77 KB file 41
                // times: about 0.8 s of frozen server per settings change.
                if (categories.Count > 0) categories[0].LoadFromFile(false);
                if (ClearFileError()) {
                    // fix1 (final-G5 M-1): put MelonLoader's flag back, so MelonLoader still refuses to save over a file it could not
                    // read (its own save at a clean quit would write the old values over the unfixed file). It is cleared only
                    // around the mod's own load attempt: the next poll clears it again before its load.
                    SetFileError();
                    // this read failed: no "reloaded", no handlers on stale values. Read again at the next polls (a writer's lock
                    // passes; MaxRetries per saved version, so a typo is not re-parsed and re-reported by MelonLoader forever) and
                    // at once whenever the file is saved again. Warned once per saved version.
                    if (now != failedStamp) { failedStamp = now; retriesLeft = MaxRetries; MelonLogger.Warning($"[Archery] Settings: {path} could not be read (a typo, or the file was still being written); the old values stay. Fix it and save again; it is read again at the next save"); }
                    else retriesLeft--;
                    return;
                }
                if (failedStamp != default || hadError) MelonLogger.Msg("[Archery] Settings: QuickshotRing.cfg is readable again");
                retriesLeft = 0;
                failedStamp = default;
                Logs.Refresh();
                if (Logs.Summary) MelonLogger.Msg("[Archery] Settings reloaded from QuickshotRing.cfg");
                UpdateChecksum(false);
                // G5: each handler its own timing section, in the multicast's order; an exception still ends the pass at the
                // handler that threw, as Invoke did (it reaches the catch below)
                Action? handlers = Reloaded;
                if (handlers != null)
                    foreach (Delegate d in handlers.GetInvocationList())
                        using (Timing.Begin(Timing.On ? HandlerSection(d) : 0)) ((Action)d)();
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Could not reload settings: {e.Message}"); }
        }

        /// <summary>G5: "reload handler Owner.Method" for a Reloaded subscriber (a lambda is named after the class that wrote it).</summary>
        static int HandlerSection(Delegate d) {
            Type? t = d.Method.DeclaringType;
            while (t != null && t.Name.StartsWith("<", StringComparison.Ordinal) && t.DeclaringType != null) t = t.DeclaringType;
            return Timing.Section($"reload handler {t?.Name ?? "?"}.{d.Method.Name}");
        }

        const int MaxRetries = 3;
        static int retriesLeft;
        static bool fileErrorUnknown;
        static DateTime failedStamp;
        static readonly int TReload = Timing.Section("Cfg.Poll settings reload");
        static System.Reflection.FieldInfo? categoryFile, fileWasError;

        /// <summary>G5 (I-1): MelonLoader 0.7.3's <c>MelonPreferences_Category.File</c> (internal) is a
        /// <c>MelonLoader.Preferences.IO.File</c> whose private <c>_waserror</c> makes every later Load/Save a no-op once set
        /// (decompiled: File.Load, LoadFileAndRefreshCategories). Clears it; true when it was set. Never throws.</summary>
        static bool ClearFileError() {
            try {
                if (fileErrorUnknown || categories.Count == 0) return false;
                categoryFile ??= AccessTools.Field(typeof(MelonPreferences_Category), "File");
                object? file = categoryFile?.GetValue(categories[0]);
                if (file == null) { NoErrorFlag("MelonPreferences_Category.File"); return false; }
                fileWasError ??= AccessTools.Field(file.GetType(), "_waserror");
                if (fileWasError == null || fileWasError.FieldType != typeof(bool)) { NoErrorFlag(file.GetType().FullName + "._waserror"); return false; }
                if (!(bool)fileWasError.GetValue(file)) return false;
                fileWasError.SetValue(file, false);
                return true;
            }
            catch (Exception e) { NoErrorFlag(e.Message); return false; }
        }

        /// <summary>fix1 (final-G5 M-1): sets MelonLoader's read-error flag again after a failed load (the private field, not the
        /// <c>WasError</c> property, whose setter logs and drops the file watcher). Only after <see cref="ClearFileError"/> found
        /// the field. Never throws.</summary>
        static void SetFileError() {
            try {
                if (fileErrorUnknown || fileWasError == null || categoryFile == null || categories.Count == 0) return;
                object? file = categoryFile.GetValue(categories[0]);
                if (file != null) fileWasError.SetValue(file, true);
            }
            catch (Exception e) { NoErrorFlag(e.Message); }
        }

        static void NoErrorFlag(string what) {
            fileErrorUnknown = true;
            MelonLogger.Warning($"[Archery] Settings: cannot see MelonLoader's read-error flag ({what}); a failed read of QuickshotRing.cfg may stop live reloads until a restart");
        }

        static DateTime SafeStamp() {
            try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default; }
            catch { return default; }
        }

        sealed class ParsedColour { public string? Text; public bool Ok; public Color Value; }
        static readonly Dictionary<MelonPreferences_Entry<string>, ParsedColour> colours = new Dictionary<MelonPreferences_Entry<string>, ParsedColour>();

        /// <summary>Colours are stored as "#RRGGBB" so they can be edited by hand. G2: the parse is kept per entry and redone only
        /// when the entry's string is another object (a reload stores a new string), so the result is the parse of the current text.</summary>
        internal static Color Colour(MelonPreferences_Entry<string> entry, Color fallback) {
            string text = entry.Value;
            if (!colours.TryGetValue(entry, out ParsedColour p)) colours[entry] = p = new ParsedColour();
            else if (p.Text != null && ReferenceEquals(p.Text, text)) return p.Ok ? p.Value : fallback;
            p.Text = text;
            p.Ok = ColorUtility.TryParseHtmlString(text, out p.Value);
            return p.Ok ? p.Value : fallback;
        }
    }

    /// <summary>G2: how much the mod logs on this machine, [Log] Level (not shared: each side reads its own). Quiet: errors, warnings
    /// and the start-up lines. Summary (default): also one line per release and one per rare change of state (a build, a sale, a main
    /// path, the shrine). Debug: every line (play-tests). A detail line is written <c>if (Logs.Debug) MelonLogger.Msg(...)</c> so its
    /// string is never built when the level is lower.</summary>
    internal static class Logs {
        static MelonPreferences_Entry<string>? level;
        static string warned = "";

        internal static bool Summary { get; private set; } = true;
        internal static bool Debug { get; private set; }

        internal static void Init() {
            var cat = Cfg.Category("Log");
            level = cat.CreateEntry("Level", "Summary", description: "How much this machine logs (not shared). Quiet: errors, warnings and the start-up lines. Summary: also one line per release and one per rare change (a build, a sale, a main path, the shrine). Debug: every line, for play-tests (switch it on before a play-test that will be diagnosed from the log).");
            Refresh();
        }

        /// <summary>Re-reads [Log] Level (start-up and every settings reload, before the reload's own lines).</summary>
        internal static void Refresh() {
            string v = (level?.Value ?? "Summary").Trim();
            Debug = v.Equals("Debug", StringComparison.OrdinalIgnoreCase);
            Summary = Debug || !v.Equals("Quiet", StringComparison.OrdinalIgnoreCase);
            bool known = Debug || !Summary || v.Equals("Summary", StringComparison.OrdinalIgnoreCase);
            if (!known && v != warned) { warned = v; MelonLogger.Warning($"[Archery] Log: [Log] Level = '{v}' is not Quiet, Summary or Debug; logging at Summary"); }
        }
    }
}
