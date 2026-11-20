using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // Batch H2 (the owner, of the plain look: "it's just a 2D circle that sits on it no matter how you look"; chose "Rings around the
    // body"): the lock's cage on a creature. Looks only; clients only; SeekLook.cs decides when, this draws.
    //   - Three green arcs on three tilted planes round the creature's middle (CageShape, Spectral.cs, tested: square to one another,
    //     each leaning 54.7 degrees, so from any side at least one is seen well open and none lies flat). While the aim rests on the
    //     creature they are wide and mostly open and run slowly round it; as the dwell fills they tighten and close, in step with it,
    //     and run faster; at the lock they snap shut into closed rings with a flash and a short green light, then turn steadily;
    //     lost, the cage springs open and fades in about a quarter of a second. Each arc fades along its length, so its bright end
    //     is seen running round the creature.
    //   - The main lock (the first: the core arrows fly to it, the real arrow ends in it) is told apart without numbers: its arcs
    //     are wider and at full brightness, and it has a fourth ring, a little larger, tilted, tumbling the other way.
    //   - Sized from the creature's own bounds between a floor and a ceiling; never thinner on screen than a set angle (it reads at
    //     60 m); placed every frame in LateUpdate from the creature's root, so it follows the creature between the shooter's sweeps;
    //     drawn with the mod's depth-tested line material, so a wall that hides the creature hides its cage.
    //   - At a release the cages of the locks it homes on stay until its arrows land on their creatures; then each is thrown
    //     outward and fades (the arrival; a watcher gets a cage only for that moment).
    // Made of: the flat line material every line of the mod uses ('Sprites/Default') and, under each arc, a soft glow on a white
    // copy of the game's 'Orb Connection Line' material (as the Fill's halo and Holy Arrow's armed ring), coloured by the line.
    // Cost: at most Cap cages, pooled (one GameObject with four arcs, each one line and one glow line of 48 points in the arc's
    // own space, made once); per frame a cage sets four transforms, widths and colours; an arc's points are set again only while
    // its opening changes (forming, the snap, lost). Nothing is allocated per frame.
    internal static class LockCage {
        const int Cap = 12, Segments = 48, ArcCount = CageShape.Rings + 1; // the fourth arc is the main lock's outer ring
        const float StaleSeconds = 0.15f, MainRingSeconds = 0.15f, FlashWidth = 0.6f, FormTurn = 0.6f;

        sealed class Arc {
            public Transform T = null!;
            public LineRenderer Line = null!;
            public LineRenderer? Glow;
            public float Sweep = -1f;      // the opening its points were last set for
        }

        sealed class Cage {
            public GameObject Go = null!;
            public readonly Arc[] Arcs = new Arc[ArcCount];
            public bool Busy;
            public uint Id;
            public Transform? Root;
            public bool HasRoot;
            public Vector3 Offset, Centre, Extents;
            public CageState State;
            public float Progress, Since, HoldUntil;
            public int SeenFrame;
            public bool Main, WasLocked;
            public float MainShown, Phase, Turn, Tumble, Bright = 1f;
            public float FromScale = 1f, FromSweep = 360f, FromAlpha = 1f;   // what it showed when it was lost
            public float BurstTo = 1.7f, BurstSeconds = 0.3f;
            public bool Heavy;
            public CagePose Shown;
        }

        static readonly List<Cage> cages = new List<Cage>();
        static readonly Vector3[] points = new Vector3[Segments + 1];
        static readonly Quaternion[] planes = new Quaternion[CageShape.Rings];
        static bool planesBuilt;
        static Host? host;
        static Material? glowMaterial;
        static bool glowTried, logged;
        static float lastFrame = -1f;
        static int frameNo;

        /// <summary>Settings reloaded: the glow's material is looked for again.</summary>
        // H4 (H23-review-opus minor 10): the glow's copy depends on no setting, so a reload keeps it (it made a new Material each time);
        // only a fall-back to the flat line material is tried again
        internal static void Reloaded() { if (glowMaterial == null || glowMaterial == Fx.LineMaterial()) glowTried = false; }

        /// <summary>Every cage goes (the look failed, or was switched off).</summary>
        internal static void Clear() {
            foreach (Cage c in cages) if (c.Go != null) UnityEngine.Object.Destroy(c.Go);
            cages.Clear();
        }

        // ---------------------------------------------------------------- what SeekLook tells it

        /// <summary>One held frame: a locked cage on each of <paramref name="locks"/> (the first is the main lock), a forming one on
        /// <paramref name="fillAt"/> when <paramref name="filling"/>. A cage of this draw that is named by neither springs open.</summary>
        static readonly int T_Frame = Timing.Section("look LockCage.Frame (the shooter's locks, per held frame)");
        internal static void Frame(List<SeekLook.Mark> locks, bool filling, in SeekLook.Mark fillAt, float progress) {
            using var _t = Timing.Begin(T_Frame);
            if (Fx.LineMaterial() == null) throw new InvalidOperationException("no line material");
            float now = Time.time;
            frameNo++;
            lastFrame = now;
            for (int i = 0; i < locks.Count; i++) {
                SeekLook.Mark m = locks[i];
                Cage? c = Find(m.Id) ?? Take(m.Id, m, now, CageState.Locked);
                if (c == null) continue;
                Refresh(c, m);
                if (c.State == CageState.Forming) Lock(c, now);
                c.Main = i == 0;
                c.SeenFrame = frameNo;
            }
            if (filling && fillAt.Id != 0u) {
                Cage? c = Find(fillAt.Id) ?? Take(fillAt.Id, fillAt, now, CageState.Forming);
                if (c != null) {
                    Refresh(c, fillAt);
                    if (c.State == CageState.Forming) { c.Progress = progress; c.Main = locks.Count == 0; }
                    c.SeenFrame = frameNo;
                }
            }
            foreach (Cage c in cages)
                if (c.Busy && c.SeenFrame != frameNo && c.HoldUntil < now && (c.State == CageState.Forming || c.State == CageState.Locked)) Lose(c, now);
        }

        /// <summary>The shot left, or the draw ended: the cages of <paramref name="keep"/> (null = none) stay locked for up to
        /// <paramref name="seconds"/>, until their release's arrows land; every other cage of this draw springs open.</summary>
        internal static void Release(List<uint>? keep, float seconds) {
            float now = Time.time;
            foreach (Cage c in cages) {
                if (!c.Busy || c.HoldUntil >= now || (c.State != CageState.Forming && c.State != CageState.Locked)) continue;
                if (c.State == CageState.Locked && keep != null && keep.Contains(c.Id)) {
                    c.HoldUntil = now + Mathf.Max(0f, seconds);
                    c.Main = keep[0] == c.Id; // the release's main lock is the first it homes on
                }
                else Lose(c, now);
            }
        }

        /// <summary>The cage kept on creature <paramref name="id"/> for a release in flight springs open (its arrows will not land
        /// on it: the core cluster ended elsewhere).</summary>
        internal static void Let(uint id) {
            if (id == 0u) return;
            Cage? c = Find(id);
            if (c != null) Lose(c, Time.time);
        }

        /// <summary>A locked release's arrows landed on creature <paramref name="id"/>: its cage (the shooter's own, kept since the
        /// release; a new one for a watcher) is thrown outward to <paramref name="to"/> times its size and fades over
        /// <paramref name="seconds"/>; <paramref name="heavy"/> = the core cluster's landing on the main lock (brighter, with its
        /// fourth ring).</summary>
        internal static void Arrive(uint id, Transform? root, Vector3 centre, Vector3 extents, float to, float seconds, bool heavy, float bright) {
            if (Fx.LineMaterial() == null) throw new InvalidOperationException("no line material");
            float now = Time.time;
            var m = new SeekLook.Mark { Id = id, Root = root, Centre = centre, Extents = extents, Offset = root != null ? root.InverseTransformPoint(centre) : default };
            Cage? c = Find(id) ?? Take(id, m, now, CageState.Locked);
            if (c == null) return;
            Refresh(c, m);
            c.FromAlpha = c.State == CageState.Forming ? c.Shown.Alpha : 1f;
            c.State = CageState.Burst;
            c.Since = now;
            c.HoldUntil = 0f;
            c.BurstTo = to; c.BurstSeconds = seconds; c.Heavy = heavy; c.Bright = bright;
            if (heavy) { c.Main = true; c.MainShown = 1f; }
        }

        // ---------------------------------------------------------------- the cages

        static Cage? Find(uint id) {
            foreach (Cage c in cages) if (c.Busy && c.Id == id && (c.State == CageState.Forming || c.State == CageState.Locked)) return c;
            return null;
        }

        static Cage? Take(uint id, in SeekLook.Mark m, float now, CageState state) {
            Cage? free = null;
            foreach (Cage c in cages) if (!c.Busy && c.Go != null) { free = c; break; }
            if (free == null) {
                cages.RemoveAll(c => c.Go == null); // one whose object is gone is never used again
                if (cages.Count >= Cap) return null;
                free = Build();
                cages.Add(free);
            }
            free.Busy = true;
            free.Id = id;
            free.State = state;
            free.Since = state == CageState.Locked ? now - 60f : now; // a lock first seen locked is settled: no snap, no flash
            free.Progress = 0f; free.HoldUntil = 0f; free.Main = false; free.WasLocked = state == CageState.Locked; free.MainShown = 0f;
            free.Phase = (id % 360u); free.Turn = (id * 7u % 360u); free.Tumble = 0f; free.Bright = 1f; free.Heavy = false;
            free.FromScale = 1f; free.FromSweep = 360f; free.FromAlpha = 1f;
            free.HasRoot = false; free.Root = null;
            free.Centre = m.Centre;
            foreach (Arc a in free.Arcs) a.Sweep = -1f;
            return free;
        }

        static void Free(Cage c) {
            c.Busy = false;
            c.Root = null;
            if (c.Go != null && c.Go.activeSelf) c.Go.SetActive(false);
        }

        // where the creature's middle is, kept in its root's own space so the cage follows it every frame; a new measure eases in
        static void Refresh(Cage c, in SeekLook.Mark m) {
            c.Extents = m.Extents;
            if (m.Root != null) {
                c.Offset = c.HasRoot && ReferenceEquals(c.Root, m.Root) ? Vector3.Lerp(c.Offset, m.Offset, 0.35f) : m.Offset;
                c.Root = m.Root; c.HasRoot = true;
            }
            else { c.HasRoot = false; c.Root = null; c.Centre = m.Centre; }
        }

        static void Lock(Cage c, float now) {
            c.State = CageState.Locked;
            c.Since = now;
            c.WasLocked = true;
            try {
                SeekLook.CageSet s = SeekLook.Cage();
                if (s.Light > 0f) {
                    float r = CageShape.Radius(c.Extents.x, c.Extents.y, c.Extents.z, s.Scale, s.Min, s.Max);
                    Fx.Flash(Where(c), s.Colour, s.Light, Mathf.Max(1.5f, r * 3f), Mathf.Max(0.05f, s.Look.FlashSeconds));
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Seek look: the lock's light failed: {e.Message}"); }
        }

        static void Lose(Cage c, float now) {
            c.FromScale = c.Shown.Scale > 0f ? c.Shown.Scale : 1f;
            c.FromSweep = c.Shown.Sweep > 0f ? c.Shown.Sweep : 360f;
            c.FromAlpha = c.Shown.Alpha > 0f ? c.Shown.Alpha : 1f;
            c.State = CageState.Lost;
            c.Since = now;
            c.HoldUntil = 0f;
        }

        static Vector3 Where(Cage c) => c.HasRoot && c.Root != null ? c.Root.TransformPoint(c.Offset) : c.Centre;

        static Material? GlowMaterial() {
            if (glowTried) return glowMaterial;
            glowTried = true;
            (Material? m, string failed) = Fx.OrbLineMaterial();
            if (m == null) {
                glowMaterial = Fx.LineMaterial();
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek look: the cage's glow uses the flat line material ('Orb Connection Line' not found: {failed})");
            }
            else {
                var copy = new Material(m) { name = m.name + " (Archery lock cage)" };
                string props = Fx.ResetColours(copy);
                glowMaterial = copy;
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek look: the cage's glow uses '{m.name}' ({Fx.ShaderName(m)}); colour properties reset: {props}");
            }
            return glowMaterial;
        }

        static readonly int T_Build = Timing.Section("look LockCage.Build (a new cage)");
        static Cage Build() {
            using var _t = Timing.Begin(T_Build);
            if (host == null) {
                var hostGo = new GameObject("ArcheryLockCages");
                UnityEngine.Object.DontDestroyOnLoad(hostGo);
                host = hostGo.AddComponent<Host>();
            }
            if (!planesBuilt) {
                for (int k = 0; k < CageShape.Rings; k++) {
                    CageShape.Plane(k, out _, out _, out _, out float vx, out float vy, out float vz, out float nx, out float ny, out float nz);
                    planes[k] = Quaternion.LookRotation(new Vector3(nx, ny, nz), new Vector3(vx, vy, vz)); // the arc's own x, y, z -> u, v, n
                }
                planesBuilt = true;
            }
            var c = new Cage { Go = new GameObject("cage") };
            c.Go.transform.SetParent(host.transform, false);
            Material? glow = GlowMaterial();
            for (int k = 0; k < ArcCount; k++) {
                var go = new GameObject(k < CageShape.Rings ? "arc" + k : "main ring");
                go.transform.SetParent(c.Go.transform, false);
                var a = new Arc { T = go.transform, Line = NewArc(go, null) };
                if (glow != null) {
                    var glowGo = new GameObject("glow"); // a GameObject holds one Renderer
                    glowGo.transform.SetParent(go.transform, false);
                    a.Glow = NewArc(glowGo, glow);
                    a.Glow.sortingOrder = -1; // under its arc
                }
                c.Arcs[k] = a;
            }
            c.Go.SetActive(false);
            if (!logged) { logged = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] Seek look: the lock's cage is built (three arcs and the main lock's fourth ring; at most {Cap} cages, pooled)"); }
            return c;
        }

        static LineRenderer NewArc(GameObject go, Material? material) {
            LineRenderer lr = Fx.NewLine(go, 2, 0.02f, false);
            if (material != null) lr.sharedMaterial = material;
            lr.useWorldSpace = false; // its points are the unit circle in its own space; the transform sizes and turns it
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            return lr;
        }

        // the arc's points for an opening of `sweep` degrees: a closed ring is a loop of Segments points
        static void SetArc(Arc a, float sweep) {
            if (Mathf.Abs(sweep - a.Sweep) < 0.2f) return;
            a.Sweep = sweep;
            bool closed = sweep >= 359.5f;
            int n = closed ? Segments : Segments + 1;
            float step = (closed ? 360f : sweep) / Segments * Mathf.Deg2Rad;
            for (int i = 0; i < n; i++) points[i] = new Vector3(Mathf.Cos(i * step), Mathf.Sin(i * step), 0f);
            Set(a.Line, n, closed);
            if (a.Glow != null) Set(a.Glow, n, closed);
        }

        static void Set(LineRenderer lr, int n, bool closed) {
            if (lr.positionCount != n) lr.positionCount = n;
            if (lr.loop != closed) lr.loop = closed;
            lr.SetPositions(points);
        }

        static void Paint(Arc a, bool show, Quaternion rotation, float radius, float sweep, float width, Color c, float alpha, in SeekLook.CageSet s) {
            GameObject go = a.T.gameObject;
            if (!show || alpha <= 0.003f) { if (go.activeSelf) go.SetActive(false); return; }
            if (!go.activeSelf) go.SetActive(true);
            SetArc(a, sweep);
            a.T.localRotation = rotation;
            a.T.localScale = new Vector3(radius, radius, radius);
            a.Line.widthMultiplier = width;
            a.Line.startColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, alpha));
            a.Line.endColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, alpha * s.TailAlpha));
            if (a.Glow == null) return;
            bool glow = s.Halo > 0f && s.HaloAlpha > 0f;
            if (a.Glow.enabled != glow) a.Glow.enabled = glow;
            if (!glow) return;
            float ga = alpha * s.HaloAlpha;
            a.Glow.widthMultiplier = width * s.Halo;
            a.Glow.startColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, ga));
            a.Glow.endColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, ga * s.TailAlpha));
        }

        sealed class Host : MonoBehaviour {
            static readonly int TLateUpdate = Timing.Section("LateUpdate LockCage (the locks' cages, per frame)");

            // LateUpdate: after the frame's animation and movement, so a cage sits on its creature as it is drawn this frame
            void LateUpdate() {
                using var _t = Timing.Begin(TLateUpdate);
                if (cages.Count == 0) return;
                try { Draw(); }
                catch (Exception e) { SeekLook.CageFailed(e); }
            }

            static void Draw() {
                float now = Time.time, dt = Time.deltaTime;
                bool stale = now - lastFrame > StaleSeconds; // no held frame for a moment: the string was let go
                SeekLook.CageSet s = default;
                bool read = false;
                Vector3 eye = default;
                bool hasEye = false;
                foreach (Cage c in cages) {
                    if (!c.Busy) continue;
                    if (c.Go == null) { c.Busy = false; continue; }
                    if (!read) {
                        s = SeekLook.Cage(); read = true;
                        Camera cam = Camera.main;
                        if (cam != null) { eye = cam.transform.position; hasEye = true; }
                    }
                    bool live = c.State == CageState.Forming || c.State == CageState.Locked;
                    // its creature is gone, its draw is over, or its release's arrows never landed on it: it springs open
                    if (live && ((c.HasRoot && c.Root == null) || (c.HoldUntil < now && stale))) { Lose(c, now); live = false; }
                    float age = now - c.Since;
                    CagePose p;
                    float spin;
                    switch (c.State) {
                        case CageState.Forming: p = CageShape.Forming(c.Progress, s.Look); spin = CageShape.Spin(c.Progress, s.SpinSlow, s.SpinFast); break;
                        case CageState.Locked: p = CageShape.Locked(age, s.Look); spin = s.SpinLocked; break;
                        case CageState.Lost:
                            if (CageShape.Over(CageState.Lost, age, s.Look)) { Free(c); continue; }
                            p = CageShape.Lost(age, c.FromScale, c.FromSweep, s.Look); p.Alpha *= c.FromAlpha; spin = s.SpinFast; break;
                        default:
                            if (!(age < c.BurstSeconds)) { Free(c); continue; }
                            p = CageShape.Burst(age, c.BurstTo, c.BurstSeconds); p.Alpha *= c.FromAlpha; spin = s.SpinFast; break;
                    }
                    if (live) c.Shown = p;
                    Vector3 centre = Where(c);
                    c.Centre = centre;
                    if (!c.Go.activeSelf) c.Go.SetActive(true);
                    c.Go.transform.position = centre;
                    c.Phase = Mathf.Repeat(c.Phase + spin * dt, 360f);
                    c.Turn = Mathf.Repeat(c.Turn + s.Turn * (c.State == CageState.Forming ? FormTurn : 1f) * dt, 360f);
                    c.Tumble = Mathf.Repeat(c.Tumble - s.MainRingSpin * dt, 360f);
                    // the main lock's fourth ring comes in after the lock, and stays with a cage that was the main one as it goes
                    bool mainNow = c.Main && c.State != CageState.Forming && s.MainRing;
                    c.MainShown = Mathf.MoveTowards(c.MainShown, mainNow ? 1f : 0f, dt / MainRingSeconds);

                    float radius = CageShape.Radius(c.Extents.x, c.Extents.y, c.Extents.z, s.Scale, s.Min, s.Max) * p.Scale;
                    float distance = hasEye ? (centre - eye).magnitude : 0f;
                    bool bold = c.Main || c.State == CageState.Forming;
                    float boost = 1f + p.Flash * (s.FlashBoost - 1f) * (c.Heavy ? 1.5f : 1f);
                    Color col = GoldHold.Brighter(s.Colour, boost);
                    float alpha = p.Alpha * (bold ? s.Alpha : s.OtherAlpha) * c.Bright * boost;
                    // the main lock's arcs stay MainWidth times as wide at any distance (the far-away floor is widened with them)
                    float width = CageShape.Width(s.Width, distance, s.MinRadians) * (c.Main && c.WasLocked ? s.MainWidth : 1f) * (1f + p.Flash * FlashWidth);
                    Quaternion turn = Quaternion.AngleAxis(c.Turn, Vector3.up);
                    for (int k = 0; k < CageShape.Rings; k++)
                        Paint(c.Arcs[k], true, turn * planes[k] * Quaternion.AngleAxis(c.Phase + k * 120f, Vector3.forward), radius, p.Sweep, width, col, alpha, s);
                    // a ring whose own z (its normal) leans MainRingTilt from straight up, tumbling round it against the cage's turn
                    Quaternion tumble = Quaternion.AngleAxis(c.Tumble, Vector3.up) * Quaternion.AngleAxis(s.MainRingTilt - 90f, Vector3.right) * Quaternion.AngleAxis(-c.Phase, Vector3.forward);
                    Paint(c.Arcs[CageShape.Rings], c.MainShown > 0.003f, tumble, radius * s.MainRingScale, 360f, width, col, alpha * c.MainShown, s);
                }
            }
        }
    }
}
