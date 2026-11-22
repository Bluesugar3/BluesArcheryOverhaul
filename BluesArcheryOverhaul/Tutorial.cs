using System;
using System.Collections.Generic;
using Alta.Progression.Skills;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Client: while an orb is tried out at the shrine, one line of floating text under the game's action glyphs says what
    // to do. The game's own try-out shows only pictograms, and the mod's orbs have none of their own.
    // While the shrine's tablet (Tablet.cs) carries that line the floating text is not shown; whenever the tablet does not
    // (not raised yet or sinking, switched off, raised for another player's tree, or the line did not fit), the floating
    // text is, so the instruction always exists. The tablet calls Sync whenever its lettering appears, changes or goes.
    // ponytail: legacy TextMesh with the built-in font (always in the build); swap for the game's font if it looks off.
    internal static class Tutorial {
        static MelonPreferences_Entry<bool> enabled = null!;
        static MelonPreferences_Entry<float> size = null!, drop = null!;
        static MelonPreferences_Entry<string> colour = null!;
        static readonly Dictionary<Orb, MelonPreferences_Entry<string>> lines = new Dictionary<Orb, MelonPreferences_Entry<string>>();
        static GameObject? shown;
        static bool live, yielded;
        // the try-out now open that has a line: its orb, the text and where it floats
        static bool active;
        static Orb current;
        static string currentText = "";
        static Vector3 currentAt;

        internal static void Init() {
            var cat = Cfg.Category("Tutorial");
            enabled = cat.CreateEntry("Enabled", true, description: "Floating text at the shrine saying how to try out an orb.");
            size = cat.CreateEntry("Size", 0.012f, description: "Text size (metres per character unit).");
            drop = cat.CreateEntry("Drop", 0.45f, description: "Metres below the game's action glyphs.");
            colour = cat.CreateEntry("Colour", "#FFE9B0");
            void Line(Orb orb, string text) => lines[orb] = cat.CreateEntry(orb.ToString(), text);
            Line(Orb.Charge1, "Pull the bow fully and hold\nuntil the thump (1 second), then shoot.");
            Line(Orb.Charge2, "Pull fully and keep holding\nfor the second thump, then shoot.");
            Line(Orb.Charge3, "Pull fully and keep holding\nfor the third thump, then shoot.");
            Line(Orb.HolyArrow, "Pull fully and hold (about 6 seconds)\nuntil the arrow turns to light, then shoot.");
            Line(Orb.Volley, "Pull fully and wait a moment\nfor five ghost arrows, then shoot.");
            Line(Orb.FullVolley, "Pull fully and hold 1.5 seconds\nuntil fifteen ghost arrows show, then shoot.");
            Line(Orb.Burst, "Pull fully and hold 1.5 seconds\nuntil the click, then shoot.");
            Line(Orb.Quickdraw4, "Pull fully and release at once.");
            // The later orbs: what their "Practised by" asks for (spec 2026-11-16). Until each orb is built, any shot
            // while it is held practises it (Shrine.PractiseOnShot).
            Line(Orb.AutoDock, "Shoot once: the next arrow nocks\nitself from the quiver beside you.");
            // fix batch D: both work for every main path (half strength off their own line); the own action per main path is SideOrbPractice
            Line(Orb.Ricochet, "Multi-arrow archer: hold until fifteen ghost arrows show, then shoot.\nOther archers (half strength): shoot your main path's own shot.");
            // H1: any volley practises it; locking needs a creature (none stands at the shrine)
            Line(Orb.SeekingVolley, "Hold a volley and shoot to try it. To lock: rest your aim\non a creature until the green rings snap shut round it, then shoot near it.");

            Line(Orb.Shatter, "Charge archer: hold a Charge Shot until the thump, then shoot.\nOther archers (half strength): shoot your main path's own shot.");
            // 0.19.11: an orb off the main path never fires its own shot; grabbing it was its try-out (Lines.OffPath, Shrine.PractiseOnGrab)
            offPath = cat.CreateEntry("OffPath", "Off your {main} main path this orb has no shot of its own:\ngrabbing it was the try-out. It adds to your {main} shots.",
                description: "The try-out line for an orb of another line than your main path; {main} is that path's name.");
        }

        static MelonPreferences_Entry<string>? offPath;

        /// <summary>The orb's try-out line for the local build, as written (a "\n" not yet a line break); empty when it has none.</summary>
        static string TextFor(Orb orb) {
            MainPath main = Lines.TryOutMain(ShrineView.Owned, orb, ShrineView.Incumbent);
            if (offPath != null && Lines.OffPath(orb, main)) return (offPath.Value ?? "").Replace("{main}", main == MainPath.Quick ? "Quick" : main == MainPath.Charge ? "Charge Shot" : "multi-arrow");
            return lines.TryGetValue(orb, out var entry) ? entry.Value ?? "" : "";
        }

        /// <summary>The orb's try-out line as one line of text (for the tablet); empty when it has none.</summary>
        internal static string LineFor(Orb orb) => TextFor(orb).Replace("\\n", " ").Replace('\n', ' ').Trim();

        /// <summary>Called by the tablet after it wrote, rebuilt or hid itself, and when a try-out opens without it: the
        /// floating text is shown exactly when a try-out with a line is open and the tablet's text does not carry that line.</summary>
        static readonly int T_Sync = Timing.Section("Tutorial.Sync");
        internal static void Sync() {
            using var _t = Timing.Begin(T_Sync);
            if (!active) return;
            if (Tablet.Carries(current)) {
                if (shown != null) Hide();
                if (!yielded) { yielded = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Tutorial: the tablet carries the try-out line; no floating text"); }
            }
            else if (shown == null) Show();
        }

        static readonly int T_Show = Timing.Section("look Tutorial.Show");
        static void Show() {
            using var _t = Timing.Begin(T_Show);
            var go = new GameObject("ArcheryTutorialText");
            shown = go;   // before anything that can throw: Hide() then removes it
            go.transform.position = currentAt;
            var mesh = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            mesh.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            mesh.text = currentText;
            mesh.fontSize = 64;
            mesh.characterSize = size.Value;
            mesh.anchor = TextAnchor.UpperCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = Cfg.Colour(colour, new Color(1f, 0.91f, 0.69f));
            go.AddComponent<FaceCamera>();
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Tutorial: text for {current} shown at {go.transform.position}" + (Tablet.Shown ? " (the tablet does not carry it)" : ""));
        }

        static void Hide() {
            if (shown != null) UnityEngine.Object.Destroy(shown);
            shown = null;
        }

        [HarmonyPatch(typeof(SkillShrineTestingArea), "Open", new Type[0])]
        static class Opened {
            static readonly int T = Timing.Section("patch SkillShrineTestingArea.Open / Tutorial.Opened");
#if QUEST
            static void Postfix(SkillShrineTestingArea __instance) {
                SkillOrb ___openSkillOrb = __instance.openSkillOrb;
                ActionGlyphsPlacer ___actionGlyphPlacer = __instance.actionGlyphPlacer;
#else
            static void Postfix(SkillOrb ___openSkillOrb, ActionGlyphsPlacer ___actionGlyphPlacer) {
#endif
                using var _t = Timing.Begin(T);
                try {
                    if (ApplicationManager.IsHeadless) return;
                    if (!live) { live = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Tutorial: patch on SkillShrineTestingArea.Open is live"); }
                    Hide();
                    active = false;
                    if (!enabled.Value || ___openSkillOrb == null || ___openSkillOrb.Slot == null) return;
                    if (!Tree.OrbBySlot.TryGetValue(___openSkillOrb.Slot.Hash, out Orb orb)) return;
                    string text = TextFor(orb).Replace("\\n", "\n");
                    if (text.Trim().Length == 0) return;
                    Transform anchor = ___actionGlyphPlacer != null ? ___actionGlyphPlacer.transform : ___openSkillOrb.transform;
                    current = orb;
                    currentText = text;
                    currentAt = anchor.position + Vector3.down * drop.Value;
                    active = true;
                    // With the tablet standing, its next rewrite decides (Tablet.Frame calls Sync after it), so the text does
                    // not flash for a frame; without it, the floating text shows at once.
                    if (Tablet.Shown) Tablet.Recheck();
                    else Sync();
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Tutorial: text failed: {e.Message}"); Hide(); }
            }
        }

        [HarmonyPatch(typeof(SkillShrineTestingArea), nameof(SkillShrineTestingArea.Disable))]
        static class Closed {
            static readonly int T = Timing.Section("patch SkillShrineTestingArea.Disable / Tutorial.Closed");
            static void Postfix() { using var _t = Timing.Begin(T); active = false; try { Hide(); } catch { } }
        }

        sealed class FaceCamera : MonoBehaviour {
#if QUEST
            public FaceCamera(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
            static readonly int TLateUpdate = Timing.Section("LateUpdate Tutorial.FaceCamera");
            void LateUpdate() {
                using var _t = Timing.Begin(TLateUpdate);
                Camera? cam = Fx.View();
                if (cam == null) return;
                Vector3 away = transform.position - cam.transform.position;
                if (away.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }
    }
}
