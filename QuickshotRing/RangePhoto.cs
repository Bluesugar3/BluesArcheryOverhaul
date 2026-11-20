using System;
using System.Collections.Generic;
using System.IO;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Client only, on request: photographs the play-test range with a temporary camera rendered to a texture, one view per frame,
    // PNGs to UserData\QuickshotRing\photos. Driven by [RangePhoto] Request in THIS machine's cfg: a new value after start-up takes
    // one set. Nothing is sent and nothing changes for anyone else. Each view's frame stalls while it renders and encodes (tens of ms).
    // The range is found from its own pieces near the local player (shelves, walls, pads, lamps); with none, from where the player looks.
    internal static class RangePhoto {
        static MelonPreferences_Entry<string> request = null!;
        static MelonPreferences_Entry<int> width = null!, height = null!;
        static MelonPreferences_Entry<float> fov = null!;
        static string last = "";

        struct View { public string Name; public Vector3 At, Look, UpHint; }
        static readonly Queue<View> views = new Queue<View>();
        static string set = "";

        static readonly string[] Construction = { "Temp Dynamic Wall", "woodenPlatform_1x1", "Tall Support", "Outdoor Light Fixture" };
        static readonly int T = Timing.Section("RangePhoto.Frame (client photos of the test range)");

        internal static void Init() {
            var cat = Cfg.Category("RangePhoto");
            request = cat.CreateEntry("Request", "", description: "Client only. Write a new value here while the game runs (not at start-up) to photograph the play-test range round you from a few fixed viewpoints: PNGs in UserData\\QuickshotRing\\photos. Each photo stalls one frame.");
            width = cat.CreateEntry("Width", 1920, description: "Client only. Photo width in pixels.");
            height = cat.CreateEntry("Height", 1080, description: "Client only. Photo height in pixels.");
            fov = cat.CreateEntry("FieldOfView", 60f, description: "Client only. The photo camera's vertical field of view in degrees.");
        }

        internal static void Started() => last = (request.Value ?? "").Trim();

        internal static void Frame() {
            if (views.Count == 0) {
                string now = (request.Value ?? "").Trim();
                if (now == last) return;
                last = now;
            }
            using var _t = Timing.Begin(T);
            if (views.Count > 0) { Shoot(views.Dequeue()); return; }
            Plan();
        }

        static void Plan() {
            Transform? head = global::Player.Current != null && global::Player.Current.PlayerController != null ? global::Player.Current.PlayerController.Head : null;
            if (head == null) { MelonLogger.Warning("[Archery] Range photo: no local player body; nothing taken"); return; }
            Vector3 me = head.position;
            Vector3 shelves = Vector3.zero, built = Vector3.zero;
            int nShelves = 0, nBuilt = 0;
            foreach (NetworkEntity e in UnityEngine.Object.FindObjectsOfType<NetworkEntity>()) {
                if (e == null) continue;
                Vector3 p = e.transform.position;
                if ((p - me).sqrMagnitude > 150f * 150f) continue;
                string n = e.gameObject.name;
                if (n.EndsWith("Shelf(Clone)") || n.EndsWith("Shelf Short(Clone)")) { shelves += p; nShelves++; continue; }
                foreach (string c in Construction) if (n.Contains(c)) { built += p; nBuilt++; break; }
            }
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 line = nShelves > 0 ? shelves / nShelves : new Vector3(me.x, me.y - 1.6f, me.z);
            Vector3 centre = nBuilt > 0 ? built / nBuilt : line + fwd * 25f;
            Vector3 facing = centre - line; facing.y = 0f;
            facing = facing.sqrMagnitude > 1f ? facing.normalized : fwd;
            Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;
            float reach = Mathf.Max(30f, (centre - line).magnitude * 2f);

            set = DateTime.Now.ToString("HHmmss");
            views.Clear();
            void Add(string name, Vector3 at, Vector3 look) => views.Enqueue(new View { Name = name, At = at, Look = look, UpHint = facing });
            Add("1-overhead", centre + Vector3.up * (reach * 1.1f), centre);
            Add("2-behind-the-line", line - facing * 9f + Vector3.up * 7f, centre);
            Add("3-archer", line + Vector3.up * 1.7f, line + facing * 30f + Vector3.up * 1.2f);
            Add("4-left", centre - right * reach * 0.9f + Vector3.up * 16f, centre);
            Add("5-right", centre + right * reach * 0.9f + Vector3.up * 16f, centre);
            Add("6-from-the-far-end", line + facing * (reach * 0.9f) + Vector3.up * 12f, line);
            MelonLogger.Msg($"[Archery] Range photo set {set}: {nShelves} shelf/shelves and {nBuilt} construction piece(s) within 150 m; line {Targets.P(line)}, centre {Targets.P(centre)}, facing {Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg:0} degrees; {views.Count} views, one per frame");
        }

        static void Shoot(View v) {
            Camera? main = Camera.main;
            if (main == null) foreach (Camera c in Camera.allCameras) if (c.enabled) { main = c; break; }
            var go = new GameObject("QuickshotRing photo camera");
            RenderTexture? rt = null;
            Texture2D? tex = null;
            try {
                var cam = go.AddComponent<Camera>();
                if (main != null) cam.CopyFrom(main); // also copies its transform and layer: set ours after
                cam.enabled = false;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.fieldOfView = fov.Value;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = Mathf.Max(main != null ? main.farClipPlane : 0f, 1500f);
                Vector3 dir = v.Look - v.At;
                Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.98f ? v.UpHint : Vector3.up;
                go.transform.SetPositionAndRotation(v.At, Quaternion.LookRotation(dir, up));
                int w = Mathf.Clamp(width.Value, 64, 4096), h = Mathf.Clamp(height.Value, 64, 4096);
                rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                cam.targetTexture = null;
                string dirPath = Path.Combine(Cfg.Folder, "photos");
                Directory.CreateDirectory(dirPath);
                string path = Path.Combine(dirPath, $"range-{set}-{v.Name}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                MelonLogger.Msg($"[Archery] Range photo: {v.Name} from {Targets.P(v.At)} -> {path}");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Range photo: {v.Name} failed: {e.Message}"); }
            finally {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
