using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Client: coloured rings round the archery shrine's orbs and coloured links between them, so the player sees their
    // main path, the legendary orbs, and the orbs whose purchase would move their main path (a pulsing warning ring).
    // The rules are TreeTint's (Preview.cs). Everything drawn is the mod's own: one LineRenderer per child object, on
    // the mod's own line material, coloured through vertex colours. The game's orbs, connection lines and their shared
    // material are never touched. Everything is destroyed when the shrine closes or the orb goes.
    internal static class TreeColours {
        static MelonPreferences_Entry<bool> enabled = null!;
        static MelonPreferences_Entry<string> legendary = null!, quick = null!, multi = null!, charge = null!, flipWarning = null!;
        static MelonPreferences_Entry<float> ringRadius = null!, ringWidth = null!, lineWidth = null!, pulseHz = null!, warningMinAlpha = null!, linkLift = null!;

        struct Pulse { public LineRenderer Line; public Color Colour; }
        struct Link { public LineRenderer Line; public Transform From, To; }

        static readonly List<GameObject> drawn = new List<GameObject>();
        static readonly List<Pulse> pulses = new List<Pulse>();
        static readonly List<Link> links = new List<Link>();
        static bool dirty;

        internal static void Init() {
            var cat = Cfg.Category("TreeColours");
            enabled = cat.CreateEntry("Enabled", true, description: "Coloured rings and links on the archery shrine's tree.");
            legendary = cat.CreateEntry("Legendary", "#E8DCFF", description: "Ring round a legendary orb (Quickdraw 4, Seeking Volley, Holy Arrow).");
            quick = cat.CreateEntry("Quick", "#FFC733", description: "Rings and links of the Quickdraw line when it is your main path.");
            multi = cat.CreateEntry("Multi", "#5BC8FF", description: "Rings and links of the multi-arrow line when it is your main path.");
            charge = cat.CreateEntry("Charge", "#FF8A2E", description: "Rings and links of the Charge Shot line when it is your main path.");
            flipWarning = cat.CreateEntry("FlipWarning", "#FFB000", description: "Pulsing ring round an orb whose purchase would change your main path.");
            ringRadius = cat.CreateEntry("RingRadius", 0.062f, description: "Ring radius in metres (the orb's shell is about 0.05).");
            ringWidth = cat.CreateEntry("RingWidth", 0.006f, description: "Ring line width in metres.");
            lineWidth = cat.CreateEntry("LineWidth", 0.008f, description: "Link line width in metres.");
            pulseHz = cat.CreateEntry("WarningPulseHz", 1.5f, description: "Pulses per second of the flip-warning ring.");
            warningMinAlpha = cat.CreateEntry("WarningMinAlpha", 0.3f, description: "Lowest opacity of the flip-warning ring's pulse (0-1).");
            linkLift = cat.CreateEntry("LinkLift", 0.004f, description: "Metres the links are drawn toward the viewer, in front of the game's own connection lines.");
            if (ApplicationManager.IsHeadless) return;
            ShrineView.Changed += () => dirty = true;
        }

        static Color PathColour(MainPath p) {
            switch (p) {
                case MainPath.Quick: return Cfg.Colour(quick, new Color(1f, 0.78f, 0.2f));
                case MainPath.Multi: return Cfg.Colour(multi, new Color(0.36f, 0.78f, 1f));
                case MainPath.Charge: return Cfg.Colour(charge, new Color(1f, 0.54f, 0.18f));
                default: return Color.white;
            }
        }

        static void Clear() {
            foreach (GameObject go in drawn) if (go != null) UnityEngine.Object.Destroy(go);
            drawn.Clear();
            pulses.Clear();
            links.Clear();
        }

        /// <summary>Called each frame (LateUpdate) on clients: rebuild after a change, then the only per-frame work:
        /// the warning rings' alpha and the links' two ends (the orbs move while they rise into place and while held).</summary>
        internal static void Frame() {
            if (ApplicationManager.IsHeadless) return;
            if (dirty) {
                dirty = false;
                try { Rebuild(); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Tree colours: rebuild failed: {e.Message}"); Clear(); }
            }
            if (pulses.Count > 0) {
                float min = Mathf.Clamp01(warningMinAlpha.Value);
                float k = 0.5f + 0.5f * Mathf.Cos(Time.time * pulseHz.Value * 2f * Mathf.PI);
                float a = Mathf.Lerp(min, 1f, k);
                for (int i = 0; i < pulses.Count; i++) {
                    Pulse p = pulses[i];
                    if (p.Line == null) continue;
                    Color c = new Color(p.Colour.r, p.Colour.g, p.Colour.b, p.Colour.a * a);
                    p.Line.startColor = c;
                    p.Line.endColor = c;
                }
            }
            if (links.Count > 0) {
                Camera? cam = Fx.View();
                float lift = linkLift.Value;
                for (int i = 0; i < links.Count; i++) {
                    Link l = links[i];
                    if (l.Line == null || l.From == null || l.To == null) continue;
                    Vector3 a = l.From.position, b = l.To.position;
                    Vector3 toward = cam != null ? cam.transform.position - (a + b) * 0.5f : Vector3.zero;
                    Vector3 off = toward.sqrMagnitude > 1e-6f ? toward.normalized * lift : Vector3.zero;
                    l.Line.SetPosition(0, a + off);
                    l.Line.SetPosition(1, b + off);
                }
            }
        }

        static readonly int T_Rebuild = Timing.Section("look TreeColours.Rebuild");
        static void Rebuild() {
            using var _t = Timing.Begin(T_Rebuild);
            Clear();
            SkillShrine? shrine = ShrineView.Shrine;
            if (!ShrineView.Open || shrine == null || !enabled.Value || Fx.LineMaterial() == null) return;

            ICollection<Orb> owned = ShrineView.Owned;
            MainPath incumbent = ShrineView.Incumbent;
            MainPath main = BuildPreview.Now(owned, incumbent).Main;

            // The tree's plane: slots sit at SkillTreeTarget.position + rotation * (depth, sideways, height)
            // (_decomp\SkillShrine.cs:257-267), so the plane's normal is the target's x and its up is the target's z.
            Transform frame = shrine.SkillTreeTarget;
            Quaternion facing = frame != null
                ? Quaternion.LookRotation(frame.rotation * Vector3.right, frame.rotation * Vector3.forward)
                : Quaternion.identity;

            var byHash = new Dictionary<uint, (SkillOrb game, Orb orb)>();
            int mainRings = 0, legendaryRings = 0, warningRings = 0;
            foreach (SkillOrb so in ShrineView.Orbs) {
                if (so == null || so.Slot == null || !Tree.OrbBySlot.TryGetValue(so.Slot.Hash, out Orb orb)) continue;
                byHash[so.Slot.Hash] = (so, orb);
                OrbTint tint = TreeTint.For(orb, owned.Contains(orb), owned, incumbent);
                if (tint == OrbTint.None) continue;
                Color colour = tint == OrbTint.FlipWarning ? Cfg.Colour(flipWarning, new Color(1f, 0.69f, 0f))
                             : tint == OrbTint.Legendary ? Cfg.Colour(legendary, new Color(0.91f, 0.86f, 1f))
                             : PathColour(TreeTint.TintLine(orb, main));   // a side orb: the main path's colour
                LineRenderer ring = Ring(so.transform, facing, colour);
                if (tint == OrbTint.FlipWarning) { pulses.Add(new Pulse { Line = ring, Colour = colour }); warningRings++; }
                else if (tint == OrbTint.Legendary) legendaryRings++;
                else mainRings++;
            }

            if (main != MainPath.None) {
                Color colour = PathColour(main);
                foreach (var end in byHash.Values) {
                    List<uint>? deps = end.game.Slot.Dependencies;
                    if (deps == null) continue;
                    foreach (uint dep in deps) {
                        if (!byHash.TryGetValue(dep, out var start) || !TreeTint.LinkTinted(end.orb, start.orb, main)) continue;
                        var go = new GameObject("ArcheryTreeLink");
                        drawn.Add(go);
                        LineRenderer lr = Fx.NewLine(go, 2, lineWidth.Value, false);
                        lr.numCapVertices = 0;
                        lr.startColor = lr.endColor = colour;
                        lr.SetPosition(0, end.game.transform.position);
                        lr.SetPosition(1, start.game.transform.position);
                        links.Add(new Link { Line = lr, From = end.game.transform, To = start.game.transform });
                    }
                }
            }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Tree colours: {mainRings} main-path, {legendaryRings} legendary, {warningRings} flip-warning ring(s), {links.Count} link(s)");
        }

        /// <summary>A circle on its own child of the orb, centred on it, lying in the tree's plane.</summary>
        static LineRenderer Ring(Transform orb, Quaternion facing, Color colour) {
            var go = new GameObject("ArcheryTreeRing");
            drawn.Add(go);
            go.transform.SetParent(orb, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.rotation = facing;
            float scale = orb.lossyScale.x;
            go.transform.localScale = Vector3.one * (ringRadius.Value / (scale > 1e-4f ? scale : 1f));
            LineRenderer lr = Fx.NewLine(go, 2, ringWidth.Value, false);
            Fx.BuildCircle(lr);
            lr.numCapVertices = 0;
            lr.startColor = lr.endColor = colour;
            return lr;
        }
    }
}
