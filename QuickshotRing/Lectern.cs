using System;
using System.Collections.Generic;

// Unity-free. The shrine tablet's geometry and arithmetic: chamfered stone blocks with UVs into the game's ruins
// atlas, the kinds of the tablet's lines, how lines are fitted and laid out on the writing face, and where the
// floating pieces (tablet, crystals, stone chips) rest and how they move, where they lie while the tablet is dormant
// in the shrine's cradle and how they rise out of it. Compiled by QuickshotRing.Tests, so only System.* in here. Tablet.cs turns these numbers into Unity objects.
namespace QuickshotRing {
    /// <summary>A rectangle of the texture: (U0, V0) bottom-left, (U1, V1) top-right. U may run outside 0..1 (the atlas repeats sideways).</summary>
    internal readonly struct UvRect {
        public readonly float U0, V0, U1, V1;
        public UvRect(float u0, float v0, float u1, float v1) { U0 = u0; V0 = v0; U1 = u1; V1 = v1; }
    }

    /// <summary>Bands of the game's ruins atlas (ruins_02_diffuse, 2048 x 2048), by pixel row from the top.</summary>
    internal static class StoneUv {
        public const int Atlas = 2048;
        public const int MeanderTop = 1000, MeanderBottom = 1200;   // the carved meander, lit by the emissive map
        public const int SlateTop = 1345, SlateBottom = 1555;       // the smooth slate (the writing surface)
        public const int BrickTop = 1700, BrickBottom = 1900;       // the small brick row
        const float MaxSpan = 8f;

        /// <summary>The band's rows over a face of this size: v covers the rows, u is centred on <paramref name="centreU"/>
        /// and as wide as keeps the stone's proportions.</summary>
        public static UvRect Band(int rowTop, int rowBottom, float width, float height, float centreU = 0.5f) {
            float rows = (rowBottom - rowTop) / (float)Atlas;
            float span = height > 1e-5f && width > 0f ? width / height * rows : rows;
            if (float.IsNaN(span) || float.IsInfinity(span) || span <= 0f) span = rows;
            if (span > MaxSpan) span = MaxSpan;
            return new UvRect(centreU - span * 0.5f, 1f - rowBottom / (float)Atlas, centreU + span * 0.5f, 1f - rowTop / (float)Atlas);
        }

        public static UvRect Meander(float width, float height, float centreU = 0.5f) => Band(MeanderTop, MeanderBottom, width, height, centreU);
        public static UvRect Slate(float width, float height, float centreU = 0.5f) => Band(SlateTop, SlateBottom, width, height, centreU);
        public static UvRect Bricks(float width, float height, float centreU = 0.5f) => Band(BrickTop, BrickBottom, width, height, centreU);
    }

    /// <summary>A mesh as plain arrays: 3 floats per vertex and normal, 4 per tangent (w = -1), 2 per UV.</summary>
    internal sealed class StoneMesh {
        public float[] Vertices = new float[0], Normals = new float[0], Tangents = new float[0], Uvs = new float[0];
        public int[] Triangles = new int[0];
        public int VertexCount => Vertices.Length / 3;
        public int TriangleCount => Triangles.Length / 3;
    }

    /// <summary>A box centred on the origin with all twelve edges chamfered: 6 faces, 12 edge strips, 8 corner
    /// triangles, flat-shaded (no vertex is shared between faces). Front faces are wound as Unity wants them
    /// (cross(b - a, c - a) along the normal). Each face takes a UV rectangle, in the order +X, -X, +Y, -Y, +Z, -Z;
    /// u runs to the right and v up as seen from outside (top: v to +Z; bottom: v to -Z). A chamfer continues the
    /// flat mapping of one of its faces (Z faces first, then X, then Y), so the stone runs over the bevel.</summary>
    internal static class StoneBox {
        public const int Faces = 6;
        public const int BevelledVertices = 96, BevelledTriangles = 44, PlainVertices = 24, PlainTriangles = 12;
        const float MaxBevelShare = 0.49f, MinSize = 1e-4f;

        //                                  +X  -X  +Y  -Y  +Z  -Z
        static readonly int[] UAxis = { 2, 2, 0, 0, 0, 0 };
        static readonly int[] USign = { 1, -1, 1, 1, -1, 1 };
        static readonly int[] VAxis = { 1, 1, 2, 2, 1, 1 };
        static readonly int[] VSign = { 1, 1, 1, -1, 1, 1 };

        public static int Face(int axis, int sign) => axis * 2 + (sign > 0 ? 0 : 1);

        /// <summary>The bevel actually cut: never negative, never NaN, under half the smallest side.</summary>
        public static float Bevel(float sx, float sy, float sz, float bevel) {
            if (float.IsNaN(bevel) || bevel <= 0f) return 0f;
            float min = Math.Min(Clean(sx), Math.Min(Clean(sy), Clean(sz)));
            return Math.Min(bevel, min * MaxBevelShare);
        }

        static float Clean(float size) => float.IsNaN(size) || float.IsInfinity(size) || size < MinSize ? MinSize : size;

        public static StoneMesh Build(float sx, float sy, float sz, float bevel, UvRect[] faceUv) {
            var h = new[] { Clean(sx) * 0.5f, Clean(sy) * 0.5f, Clean(sz) * 0.5f };
            float b = Bevel(sx, sy, sz, bevel);
            var w = new Writer(h, faceUv);
            var n = new float[3];
            var p = new float[4][];
            for (int i = 0; i < 4; i++) p[i] = new float[3];

            for (int f = 0; f < Faces; f++) {
                int a = f / 2, s = f % 2 == 0 ? 1 : -1, o1 = (a + 1) % 3, o2 = (a + 2) % 3;
                for (int k = 0; k < 4; k++) {
                    int c1 = k < 2 ? -1 : 1, c2 = k == 1 || k == 2 ? 1 : -1;
                    p[k][a] = s * h[a]; p[k][o1] = c1 * (h[o1] - b); p[k][o2] = c2 * (h[o2] - b);
                }
                n[0] = n[1] = n[2] = 0f; n[a] = s;
                w.Add(p, 4, n, f);
            }
            if (b > 0f) {
                float r2 = (float)(1.0 / Math.Sqrt(2.0)), r3 = (float)(1.0 / Math.Sqrt(3.0));
                for (int a1 = 0; a1 < 3; a1++)
                    for (int a2 = a1 + 1; a2 < 3; a2++) {
                        int a3 = 3 - a1 - a2;
                        for (int s1 = -1; s1 <= 1; s1 += 2)
                            for (int s2 = -1; s2 <= 1; s2 += 2) {
                                for (int k = 0; k < 4; k++) {
                                    bool onFirst = k < 2;                       // two points on face a1, then two on face a2
                                    int along = k == 1 || k == 2 ? 1 : -1;
                                    p[k][a1] = s1 * (onFirst ? h[a1] : h[a1] - b);
                                    p[k][a2] = s2 * (onFirst ? h[a2] - b : h[a2]);
                                    p[k][a3] = along * (h[a3] - b);
                                }
                                n[a1] = s1 * r2; n[a2] = s2 * r2; n[a3] = 0f;
                                w.Add(p, 4, n, Owner(a1, s1, a2, s2));
                            }
                    }
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2) {
                            Set(p[0], x * h[0], y * (h[1] - b), z * (h[2] - b));
                            Set(p[1], x * (h[0] - b), y * h[1], z * (h[2] - b));
                            Set(p[2], x * (h[0] - b), y * (h[1] - b), z * h[2]);
                            n[0] = x * r3; n[1] = y * r3; n[2] = z * r3;
                            w.Add(p, 3, n, Face(2, z));
                        }
            }
            return w.Done();
        }

        static void Set(float[] p, float x, float y, float z) { p[0] = x; p[1] = y; p[2] = z; }

        // Z faces own their chamfers first (the fronts carry the carved bands), then X, then Y.
        static int Owner(int a1, int s1, int a2, int s2) {
            if (a1 == 2) return Face(a1, s1);
            if (a2 == 2) return Face(a2, s2);
            return a1 == 0 ? Face(a1, s1) : Face(a2, s2);
        }

        sealed class Writer {
            readonly float[] h;
            readonly UvRect[] uv;
            readonly List<float> v = new List<float>(BevelledVertices * 3), nrm = new List<float>(BevelledVertices * 3),
                                 tan = new List<float>(BevelledVertices * 4), tex = new List<float>(BevelledVertices * 2);
            readonly List<int> tri = new List<int>(BevelledTriangles * 3);

            public Writer(float[] half, UvRect[] faceUv) { h = half; uv = faceUv; }

            public void Add(float[][] p, int count, float[] n, int owner) {
                // wind the polygon so that cross(p1 - p0, p2 - p0) points along the normal
                float ax = p[1][0] - p[0][0], ay = p[1][1] - p[0][1], az = p[1][2] - p[0][2];
                float bx = p[2][0] - p[0][0], by = p[2][1] - p[0][1], bz = p[2][2] - p[0][2];
                float facing = (ay * bz - az * by) * n[0] + (az * bx - ax * bz) * n[1] + (ax * by - ay * bx) * n[2];
                bool flip = facing < 0f;

                int ua = UAxis[owner], us = USign[owner], va = VAxis[owner], vs = VSign[owner];
                UvRect r = uv != null && owner < uv.Length ? uv[owner] : new UvRect(0f, 0f, 1f, 1f);
                // tangent: the owner's u direction, laid into this face's plane
                float tx = ua == 0 ? us : 0f, ty = ua == 1 ? us : 0f, tz = ua == 2 ? us : 0f;
                float d = tx * n[0] + ty * n[1] + tz * n[2];
                tx -= d * n[0]; ty -= d * n[1]; tz -= d * n[2];
                float len = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
                if (len > 1e-6f) { tx /= len; ty /= len; tz /= len; }

                int first = v.Count / 3;
                for (int i = 0; i < count; i++) {
                    float[] q = p[flip ? count - 1 - i : i];
                    v.Add(q[0]); v.Add(q[1]); v.Add(q[2]);
                    nrm.Add(n[0]); nrm.Add(n[1]); nrm.Add(n[2]);
                    tan.Add(tx); tan.Add(ty); tan.Add(tz); tan.Add(-1f);
                    float cu = us * q[ua] / (2f * h[ua]) + 0.5f, cv = vs * q[va] / (2f * h[va]) + 0.5f;
                    tex.Add(r.U0 + cu * (r.U1 - r.U0)); tex.Add(r.V0 + cv * (r.V1 - r.V0));
                }
                tri.Add(first); tri.Add(first + 1); tri.Add(first + 2);
                if (count == 4) { tri.Add(first); tri.Add(first + 2); tri.Add(first + 3); }
            }

            public StoneMesh Done() => new StoneMesh {
                Vertices = v.ToArray(), Normals = nrm.ToArray(), Tangents = tan.ToArray(), Uvs = tex.ToArray(), Triangles = tri.ToArray()
            };
        }
    }

    /// <summary>One stone block of the tablet: its size, where its centre sits in its parent's frame, its chamfer and
    /// its six UV rectangles (+X, -X, +Y, -Y, +Z, -Z).</summary>
    internal struct StonePart {
        public string Name;
        public P3 Size, Centre;
        public float Bevel;
        public UvRect[] Uv;
    }

    /// <summary>The blocks the tablet is built from. The slab (TABLET frame: x right, y up the slab, the reader looks
    /// along +z at its -z side) is four frame rails round a thinner panel: the rails are the slab's full thickness and
    /// the panel is flush with their backs, so the writing face lies FrameDepth behind the frame. The top rail's front
    /// carries the meander band, the bottom rail's the brick row, everything else the slate band.</summary>
    internal static class TabletParts {
        public const int TopRail = 0, FootRail = 1, LeftRail = 2, RightRail = 3, Panel = 4;
        public const float SocketShare = 0.72f;   // the socket block's footprint, of the plinth's

        static UvRect[] Six(UvRect x, UvRect y, UvRect back, UvRect front) => new[] { x, x, y, y, back, front };

        /// <param name="top">height of the top rail, metres</param>
        /// <param name="foot">height of the bottom rail, metres</param>
        public static StonePart[] Slab(float w, float h, float th, float frameWidth, float frameDepth, float top, float foot, float bevel) {
            float inner = h - top - foot, railY = (foot - top) * 0.5f, panelW = w - 2f * frameWidth, panelT = th - frameDepth;
            UvRect lid = StoneUv.Slate(w, th, 0.4f), end = StoneUv.Slate(frameWidth, th);
            return new[] {
                new StonePart { Name = "Top Rail", Size = new P3(w, top, th), Centre = new P3(0f, (h - top) * 0.5f, 0f), Bevel = bevel,
                                Uv = Six(StoneUv.Slate(th, top, 0.12f), lid, StoneUv.Slate(w, top, 0.35f), StoneUv.Meander(w, top)) },
                new StonePart { Name = "Foot Rail", Size = new P3(w, foot, th), Centre = new P3(0f, -(h - foot) * 0.5f, 0f), Bevel = bevel,
                                Uv = Six(StoneUv.Slate(th, foot, 0.88f), lid, StoneUv.Slate(w, foot, 0.65f), StoneUv.Bricks(w, foot)) },
                new StonePart { Name = "Left Rail", Size = new P3(frameWidth, inner, th), Centre = new P3(-(w - frameWidth) * 0.5f, railY, 0f), Bevel = bevel,
                                Uv = Six(StoneUv.Slate(th, inner, 0.22f), end, StoneUv.Slate(frameWidth, inner, 0.15f), StoneUv.Slate(frameWidth, inner, 0.08f)) },
                new StonePart { Name = "Right Rail", Size = new P3(frameWidth, inner, th), Centre = new P3((w - frameWidth) * 0.5f, railY, 0f), Bevel = bevel,
                                Uv = Six(StoneUv.Slate(th, inner, 0.78f), end, StoneUv.Slate(frameWidth, inner, 0.85f), StoneUv.Slate(frameWidth, inner, 0.92f)) },
                // no chamfer: its edges lie against the rails
                new StonePart { Name = "Writing Face", Size = new P3(panelW, inner, panelT), Centre = new P3(0f, railY, frameDepth * 0.5f), Bevel = 0f,
                                Uv = Six(StoneUv.Slate(panelT, inner), StoneUv.Slate(panelW, panelT), StoneUv.Slate(panelW, inner, 0.6f), StoneUv.Slate(panelW, inner)) },
            };
        }

        /// <summary>STAND frame: the plinth, sunk <paramref name="sink"/> into the floor (brick rows round it, slate on
        /// top), and on it the low socket block the gem hovers over (the meander band round it); no socket at height 0.</summary>
        public static StonePart[] Plinth(P3 size, float sink, float socket, float bevel) {
            var plinth = new StonePart { Name = "Plinth", Size = size, Centre = new P3(0f, size.Y * 0.5f - sink, 0f), Bevel = bevel,
                Uv = new[] { StoneUv.Bricks(size.Z, size.Y, 0.31f), StoneUv.Bricks(size.Z, size.Y, 0.69f), StoneUv.Slate(size.X, size.Z), StoneUv.Slate(size.X, size.Z, 0.3f),
                             StoneUv.Bricks(size.X, size.Y, 0.83f), StoneUv.Bricks(size.X, size.Y) } };
            if (!(socket > 0.001f)) return new[] { plinth };
            var cap = new P3(size.X * SocketShare, socket, size.Z * SocketShare);
            return new[] { plinth, new StonePart { Name = "Plinth Socket", Size = cap, Centre = new P3(0f, size.Y - sink + socket * 0.5f, 0f), Bevel = bevel * 0.5f,
                Uv = new[] { StoneUv.Meander(cap.Z, cap.Y, 0.27f), StoneUv.Meander(cap.Z, cap.Y, 0.73f), StoneUv.Slate(cap.X, cap.Z, 0.62f), StoneUv.Slate(cap.X, cap.Z),
                             StoneUv.Meander(cap.X, cap.Y, 0.9f), StoneUv.Meander(cap.X, cap.Y) } } };
        }

        /// <summary>Stone chip <paramref name="i"/> (FloatLayout places it): slate chips and brick chips in turn, each from its own part of the atlas.</summary>
        public static StonePart Chip(int i, float bevel) {
            P3 size = FloatLayout.FragmentSize(i);
            float u = 0.17f + 0.23f * i;
            bool bricks = i % 2 == 1;
            UvRect x = bricks ? StoneUv.Bricks(size.Z, size.Y, u) : StoneUv.Slate(size.Z, size.Y, u);
            UvRect z = bricks ? StoneUv.Bricks(size.X, size.Y, u + 0.2f) : StoneUv.Slate(size.X, size.Y, u + 0.2f);
            return new StonePart { Name = "Stone Chip " + (i + 1), Size = size, Centre = new P3(0f, 0f, 0f), Bevel = bevel * 0.6f, Uv = Six(x, StoneUv.Slate(size.X, size.Z, u + 0.4f), z, z) };
        }
    }

    internal enum LineKind { Title, Body, Plus, Warn, Footer, Note }

    /// <summary>What each of the tablet's lines is, from its place and wording (TabletText returns plain strings).</summary>
    internal static class TabletLines {
        public static bool IsWarn(string line) =>
            line.StartsWith(TabletText.LossPrefix.TrimEnd(), StringComparison.Ordinal)
            || (line.StartsWith("Makes you a", StringComparison.Ordinal) && line.Contains("(you are"));

        public static bool IsOutcome(string line) =>
            line.StartsWith("Keeps you a ", StringComparison.Ordinal) || line.StartsWith("Makes you a ", StringComparison.Ordinal);

        public static bool IsHeader(string line) => line == TabletText.ChangesHeader || line == TabletText.NoChange || line == TabletText.Owned;

        /// <summary>First line = title; with a test note the last line is the note and the one before it the footer,
        /// without one the last line is the footer; "+ " lines are changes; a path-changing outcome and "You lose:" warn.</summary>
        public static LineKind[] Classify(IList<string> lines, bool hasNote) {
            int count = lines.Count;
            var kinds = new LineKind[count];
            int note = hasNote && count >= 2 ? count - 1 : -1;
            int footer = hasNote ? count - 2 : count - 1;
            for (int i = 0; i < count; i++) {
                string l = lines[i] ?? "";
                kinds[i] = i == 0 ? LineKind.Title
                         : i == note ? LineKind.Note
                         : i == footer ? LineKind.Footer
                         : l.StartsWith("+ ", StringComparison.Ordinal) ? LineKind.Plus
                         : IsWarn(l) ? LineKind.Warn
                         : LineKind.Body;
            }
            return kinds;
        }

        /// <summary>A small gap above "With your build:" (or "no change" / "You own this.") and above the outcome,
        /// as in the preview; never right under the title.</summary>
        public static bool GapBefore(IList<string> lines, LineKind[] kinds, int i) {
            if (i < 2 || i >= lines.Count || (kinds[i] != LineKind.Body && kinds[i] != LineKind.Warn)) return false;
            string l = lines[i] ?? "";
            return IsHeader(l) || IsOutcome(l);
        }
    }

    /// <summary>One row of lettering on the writing face. X from the face's left edge, Y from its top edge down to the
    /// row's top, Size = letter (em) height, all in metres.</summary>
    internal struct TabletRow {
        public LineKind Kind;
        public string Text;
        public float X, Y, Size;
    }

    internal struct TabletMetrics {
        public float FaceWidth, FaceHeight;   // the recessed face inside the frame
        public float Margin;                  // from the frame to the lettering, left and right
        public float TextSize;                // body letter height
        public float TitleScale, NoteScale;   // title and test-note sizes as shares of the body size
        public float MinScale;                // a long line shrinks to this share before it wraps
        public float GlyphSize;               // the rune in the top-right corner (0 = none)
    }

    internal sealed class TabletPage {
        public readonly List<TabletRow> Rows = new List<TabletRow>();
        public float RuleX0, RuleX1, RuleY;   // the rule under the title
        public float GlyphX, GlyphY;          // the rune's top-left corner
        public float BodyScale = 1f;          // under 1 when the body had to be squeezed to fit
    }

    /// <summary>Lays the tablet's lines on the face, in the preview's proportions (make_shrine_preview.py; one body
    /// letter height = 34 px there).</summary>
    internal static class TabletLayout {
        public const float TopPad = 0.76f;            // face top to the title
        public const float TitleAdvance = 1.24f;      // of the title's size, title to rule
        public const float RuleGap = 0.41f;           // rule to the first body row
        public const float Pitch = 1.294f;            // body row to body row
        public const float Gap = 0.35f;               // the small gap between blocks
        public const float PlusIndent = 0.76f;        // "+ " lines sit in a little
        public const float BottomPad = 0.3f;          // face bottom to the lowest row's bottom
        public const float NoteRow = 1.1f;            // of the note's size
        public const float FooterRow = 1.23f;         // of the body size
        public const float BodyClear = 0.15f;         // body to footer
        public const float GlyphTop = 0.65f;
        public const float MinSqueeze = 0.75f;

        /// <summary>The size a line is set at so it fits <paramref name="maxWidth"/>: its own size, or shrunk down to
        /// <paramref name="minScale"/> of it; a line that would need less is cut in two at the space that balances
        /// the halves (<paramref name="second"/>), and only if a half is still too long does it shrink further.
        /// <paramref name="emWidth"/> gives a text's width in letter heights.</summary>
        public static float FitLine(string text, float size, float maxWidth, float minScale, Func<string, float> emWidth, out string first, out string second) {
            first = text ?? "";
            second = "";
            float width = emWidth(first) * size;
            if (!(width > maxWidth) || maxWidth <= 0f || size <= 0f) return size;
            float scale = maxWidth / width;
            if (scale >= minScale) return size * scale;

            int best = -1;
            float bestWidth = float.MaxValue;
            for (int i = first.IndexOf(' '); i > 0; i = first.IndexOf(' ', i + 1)) {
                float w = Math.Max(emWidth(first.Substring(0, i)), emWidth(first.Substring(i + 1)));
                if (w <= bestWidth) { bestWidth = w; best = i; }   // on a tie the first row is the longer one
            }
            if (best < 0) return size * scale;   // one long word: nothing to cut
            second = first.Substring(best + 1);
            first = first.Substring(0, best);
            float cut = bestWidth * size;
            return cut > maxWidth ? size * (maxWidth / cut) : size;
        }

        public static TabletPage Layout(IList<string> lines, bool hasNote, TabletMetrics m, Func<string, float> emWidth) {
            var page = new TabletPage();
            float s = m.TextSize, width = m.FaceWidth - 2f * m.Margin;
            LineKind[] kinds = TabletLines.Classify(lines, hasNote);
            page.RuleX0 = m.Margin;
            page.RuleX1 = m.FaceWidth - m.Margin;
            page.GlyphX = m.FaceWidth - m.Margin - m.GlyphSize;
            page.GlyphY = GlyphTop * s;

            // the title, clear of the rune
            float y = TopPad * s;
            if (lines.Count > 0) {
                float titleWidth = m.GlyphSize > 0f ? width - m.GlyphSize - m.Margin * 0.5f : width;
                float size = FitLine(lines[0], s * m.TitleScale, titleWidth, m.MinScale, emWidth, out string a, out string b);
                page.Rows.Add(new TabletRow { Kind = LineKind.Title, Text = a, X = m.Margin, Y = y, Size = size });
                y += TitleAdvance * size;
                if (b.Length > 0) {
                    page.Rows.Add(new TabletRow { Kind = LineKind.Title, Text = b, X = m.Margin, Y = y, Size = size });
                    y += TitleAdvance * size;
                }
            }
            page.RuleY = y;
            y += RuleGap * s;
            float bodyTop = y;

            // the footer and the note, pinned to the bottom, laid from the bottom up
            float cursor = m.FaceHeight - BottomPad * s;
            for (int i = lines.Count - 1; i >= 1; i--) {
                if (kinds[i] != LineKind.Note && kinds[i] != LineKind.Footer) continue;
                bool note = kinds[i] == LineKind.Note;
                float size = FitLine(lines[i], note ? s * m.NoteScale : s, width, m.MinScale, emWidth, out string a, out string b);
                float row = note ? NoteRow * s * m.NoteScale : FooterRow * s;
                if (b.Length > 0) { cursor -= row; page.Rows.Add(new TabletRow { Kind = kinds[i], Text = b, X = m.Margin, Y = cursor, Size = size }); }
                cursor -= row;
                page.Rows.Add(new TabletRow { Kind = kinds[i], Text = a, X = m.Margin, Y = cursor, Size = size });
            }
            float bodyBottom = cursor - BodyClear * s;

            // the body: fitted at full size first, then squeezed as a whole if it runs into the footer
            var body = new List<TabletRow>();
            var gaps = new List<bool>();
            float needed = 0f;
            for (int i = 1; i < lines.Count; i++) {
                if (kinds[i] == LineKind.Note || kinds[i] == LineKind.Footer) continue;
                float indent = kinds[i] == LineKind.Plus ? PlusIndent * s : 0f;
                float size = FitLine(lines[i], s, width - indent, m.MinScale, emWidth, out string a, out string b);
                bool gap = TabletLines.GapBefore(lines, kinds, i);
                body.Add(new TabletRow { Kind = kinds[i], Text = a, X = m.Margin + indent, Size = size });
                gaps.Add(gap);
                needed += Pitch * s + (gap ? Gap * s : 0f);
                if (b.Length > 0) {
                    body.Add(new TabletRow { Kind = kinds[i], Text = b, X = m.Margin + indent, Size = size });
                    gaps.Add(false);
                    needed += Pitch * s;
                }
            }
            float room = bodyBottom - bodyTop;
            float k = needed > room && needed > 0f ? Math.Max(MinSqueeze, room / needed) : 1f;
            if (float.IsNaN(k) || k <= 0f) k = 1f;
            page.BodyScale = k;
            for (int i = 0; i < body.Count; i++) {
                TabletRow r = body[i];
                if (gaps[i]) y += Gap * s * k;
                r.Y = y;
                r.Size *= k;
                page.Rows.Add(r);
                y += Pitch * s * k;
            }
            return page;
        }
    }

    internal struct P3 {
        public float X, Y, Z;
        public P3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>Where a piece is: position, rotation as Unity Euler angles (degrees; z, then x, then y), uniform scale.</summary>
    internal struct Pose {
        public P3 Position, Euler;
        public float Scale;
    }

    /// <summary>The floating tablet's measures. The pieces live in two frames: the STAND frame (origin on the floor
    /// under the plinth's centre, x to the reader's right, y up, z away from the reader) and the TABLET frame (origin
    /// at the slab's centre, x right, y up along the slab, z into the slab; the writing face looks along -z).</summary>
    internal struct FloatSpec {
        public float Width, Height, Thickness;       // the slab
        public float Tilt;                           // degrees the slab leans back
        public float FloatHeight;                    // floor to the slab's centre
        public float PlinthTop;                      // floor to the plinth's top
        public float GemGap, GemScale, GemMinY, GemMaxY;       // the gem mesh's lowest and highest point (unscaled)
        public float GemHalfWidth;                   // the gem mesh's widest reach from its axis (unscaled)
        public int ShardCount;
        public float ShardGap, ShardScale, ShardTilt;
        public float ShardMinY, ShardMaxY, ShardHalfWidth;     // the shard mesh: its long axis is +y from its base
        public int SplinterCount;
        public float SplinterScale, OrbitRadius;
        public float SplinterFloor;                  // the splinters' ring stays above this height (0 = from the gem's centre up)
        public int FragmentCount;
        public float BobHeight, BobSeconds, SwayDegrees, GemTurnSeconds, OrbitSeconds;

        /// <summary>The shipped settings with the game's crystal meshes' measured bounds.</summary>
        public static FloatSpec Default => new FloatSpec {
            Width = 0.42f, Height = 0.30f, Thickness = 0.06f, Tilt = 18f, FloatHeight = 1.15f, PlinthTop = 0.115f,
            GemGap = 0.10f, GemScale = 0.9f, GemMinY = -0.0876f, GemMaxY = 0.099f, GemHalfWidth = 0.092f,
            ShardCount = 4, ShardGap = 0.035f, ShardScale = 1f, ShardTilt = 8f, ShardMinY = -0.0334f, ShardMaxY = 0.1762f, ShardHalfWidth = 0.0358f,
            SplinterCount = 5, SplinterScale = 0.45f, OrbitRadius = 0.16f, FragmentCount = 4,
            BobHeight = 0.015f, BobSeconds = 5f, SwayDegrees = 0.6f, GemTurnSeconds = 24f, OrbitSeconds = 30f
        };
    }

    /// <summary>Where the floating pieces rest. Everything here is fixed for given settings; FloatMotion adds the drift.</summary>
    internal static class FloatLayout {
        public const int MaxShards = 4, MaxFragments = 6, MaxSplinters = 12;
        public const float ShardOverhang = 0.43f;     // share of a carrier shard's length that reaches past the slab's corner
        public const float SplinterMaxTilt = 35f;
        const float Deg = (float)(Math.PI / 180.0);

        static float Frac(float v) => v - (float)Math.Floor(v);

        /// <summary>The slab's lowest point above the floor (its lower front edge, since it leans back).</summary>
        public static float TabletLowEdge(FloatSpec s) =>
            s.FloatHeight - 0.5f * s.Height * (float)Math.Cos(s.Tilt * Deg) - 0.5f * s.Thickness * (float)Math.Abs(Math.Sin(s.Tilt * Deg));

        /// <summary>STAND frame: the tablet's centre, leaning back.</summary>
        public static Pose Tablet(FloatSpec s) => new Pose { Position = new P3(0f, s.FloatHeight, 0f), Euler = new P3(s.Tilt, 0f, 0f), Scale = 1f };

        /// <summary>STAND frame: the anchor gem, its lowest point GemGap above the plinth.</summary>
        public static Pose Gem(FloatSpec s) =>
            new Pose { Position = new P3(0f, s.PlinthTop + s.GemGap - s.GemScale * s.GemMinY, 0f), Scale = s.GemScale };

        public static float GemCentreY(FloatSpec s) => Gem(s).Position.Y + s.GemScale * 0.5f * (s.GemMinY + s.GemMaxY);

        public static int Shards(FloatSpec s) => Math.Max(0, Math.Min(MaxShards, s.ShardCount));
        public static int Splinters(FloatSpec s) => Math.Max(0, Math.Min(MaxSplinters, s.SplinterCount));
        public static int Fragments(FloatSpec s) => Math.Max(0, Math.Min(MaxFragments, s.FragmentCount));

        /// <summary>TABLET frame: carrier shard <paramref name="i"/> (0 lower-left, 1 lower-right, 2 upper-right,
        /// 3 upper-left). The lower pair point up and the upper pair down, each beside the slab's side edge with
        /// ShardGap of air, its base past the corner and swung outward by ShardTilt so the tips close on the slab.</summary>
        public static Pose Shard(FloatSpec s, int i) {
            bool right = i == 1 || i == 2, upper = i >= 2;
            float side = right ? 1f : -1f;
            float length = (s.ShardMaxY - s.ShardMinY) * s.ShardScale;
            float lean = s.ShardMaxY * s.ShardScale * (float)Math.Sin(Math.Abs(s.ShardTilt) * Deg);   // how far the tip swings in (the mesh turns about its origin, near its base)
            float x = side * (0.5f * s.Width + s.ShardGap + s.ShardHalfWidth * s.ShardScale + Math.Max(0f, lean));
            float baseY = 0.5f * s.Height + ShardOverhang * length;                // the base's distance from the slab's centre line
            float y = upper ? baseY + s.ShardMinY * s.ShardScale : -baseY - s.ShardMinY * s.ShardScale;
            float roll = upper ? 180f - side * s.ShardTilt : side * s.ShardTilt;
            return new Pose { Position = new P3(x, y, 0f), Euler = new P3(0f, side * (upper ? -12f : 12f), roll), Scale = s.ShardScale };
        }

        public static float ShardPhase(int i) => i * 1.9f;

        /// <summary>STAND frame: splinter <paramref name="i"/>'s height on its ring, spread between the gem and the slab.</summary>
        public static float SplinterHeight(FloatSpec s, int i) {
            float low = Math.Max(GemCentreY(s), s.SplinterFloor), high = TabletLowEdge(s) - 0.14f;
            if (high < low) high = low;
            return low + (high - low) * Frac(0.12f + i * 0.618034f);
        }

        /// <summary>Degrees splinter <paramref name="i"/> starts round the ring.</summary>
        public static float SplinterAngle(FloatSpec s, int i) { int n = Splinters(s); return n > 0 ? 360f * i / n : 0f; }

        /// <summary>Splinter <paramref name="i"/>'s own tilt (x, z) and spin about its length (y), degrees.</summary>
        public static P3 SplinterTilt(int i) =>
            new P3(SplinterMaxTilt * (2f * Frac(0.31f + i * 0.447f) - 1f), 360f * Frac(i * 0.29f), SplinterMaxTilt * (2f * Frac(0.83f + i * 0.709f) - 1f));

        // Chips broken from the shrine, below and behind the slab's lower edge: offset from the slab's centre
        // (for a 0.42 x 0.30 slab), size, resting tumble. Each a different proportion; all outside the splinters' ring.
        static readonly float[][] Chips = {
            //  x       y       z      w      h      d       ex    ey    ez
            new[] { -0.25f, -0.21f, 0.09f, 0.070f, 0.050f, 0.045f, 14f, 28f, -9f },
            new[] { 0.24f, -0.26f, 0.11f, 0.050f, 0.035f, 0.060f, -11f, -35f, 17f },
            new[] { -0.16f, -0.31f, 0.21f, 0.040f, 0.040f, 0.030f, 24f, 52f, 12f },
            new[] { 0.14f, -0.36f, 0.22f, 0.060f, 0.030f, 0.035f, -8f, 15f, -21f },
            new[] { 0.31f, -0.15f, 0.06f, 0.035f, 0.030f, 0.050f, 19f, -18f, 7f },
            new[] { -0.30f, -0.34f, 0.12f, 0.030f, 0.045f, 0.030f, -16f, 40f, 26f },
        };

        public static P3 FragmentSize(int i) { float[] c = Chips[i % Chips.Length]; return new P3(c[3], c[4], c[5]); }
        /// <summary>Chip <paramref name="i"/>'s offset from the slab's centre (for a 0.42 x 0.30 slab).</summary>
        public static P3 FragmentOffset(int i) { float[] c = Chips[i % Chips.Length]; return new P3(c[0], c[1], c[2]); }
        /// <summary>Chip <paramref name="i"/>'s resting tumble, degrees.</summary>
        public static P3 FragmentTumble(int i) { float[] c = Chips[i % Chips.Length]; return new P3(c[6], c[7], c[8]); }

        /// <summary>STAND frame: stone chip <paramref name="i"/> at rest.</summary>
        public static Pose Fragment(FloatSpec s, int i) {
            float[] c = Chips[i % Chips.Length];
            float wide = s.Width / 0.42f;
            return new Pose {
                Position = new P3(c[0] * wide, s.FloatHeight + c[1] - 0.5f * (s.Height - 0.30f), c[2]),
                Euler = new P3(c[6], c[7], c[8]), Scale = 1f
            };
        }

        public static float FragmentPhase(int i) => 0.7f + i * 2.4f;
    }

    /// <summary>The drift of the floating pieces at time <paramref name="t"/> (seconds). With <c>moving</c> off every
    /// piece is at its mid position. Pure arithmetic on structs: nothing is allocated.</summary>
    internal static class FloatMotion {
        const float Tau = (float)(Math.PI * 2.0);
        public const float GemBobShare = 0.6f, ShardBobShare = 0.3f, SplinterBobShare = 0.5f, FragmentBobShare = 0.5f;
        public const float FragmentWobble = 4f;   // degrees

        static float Wave(float t, float seconds, float phase) => seconds > 1e-3f ? (float)Math.Sin(Tau * t / seconds + phase) : 0f;
        static float Turn(float t, float seconds) => Math.Abs(seconds) > 1e-3f ? 360f * (float)((t / seconds) % 1.0) : 0f;

        /// <summary>STAND frame: the slab bobs, rolls a little in its own plane and turns a little about the vertical.</summary>
        public static Pose Tablet(FloatSpec s, float t, bool moving) {
            Pose p = FloatLayout.Tablet(s);
            if (!moving) return p;
            p.Position.Y += s.BobHeight * Wave(t, s.BobSeconds, 0f);
            p.Euler.Z += s.SwayDegrees * Wave(t, s.BobSeconds * 1.7f, 0.6f);
            p.Euler.Y += s.SwayDegrees * 0.6f * Wave(t, s.BobSeconds * 2.3f, 2.1f);
            return p;
        }

        /// <summary>STAND frame: the gem turns and bobs against the slab (half a beat behind it).</summary>
        public static Pose Gem(FloatSpec s, float t, bool moving) {
            Pose p = FloatLayout.Gem(s);
            if (!moving) return p;
            p.Position.Y += GemBobShare * s.BobHeight * Wave(t, s.BobSeconds, (float)Math.PI);
            p.Euler.Y = Turn(t, s.GemTurnSeconds);
            return p;
        }

        /// <summary>TABLET frame: a carrier shard breathes along the slab's side, each on its own beat.</summary>
        public static Pose Shard(FloatSpec s, int i, float t, bool moving) {
            Pose p = FloatLayout.Shard(s, i);
            if (moving) p.Position.Y += ShardBobShare * s.BobHeight * Wave(t, s.BobSeconds * 0.8f, FloatLayout.ShardPhase(i));
            return p;
        }

        /// <summary>Degrees splinter <paramref name="i"/> is round its ring at time <paramref name="t"/>.</summary>
        public static float SplinterAngle(FloatSpec s, int i, float t, bool moving) => FloatLayout.SplinterAngle(s, i) + (moving ? Turn(t, s.OrbitSeconds) : 0f);

        /// <summary>STAND frame: a splinter rides its ring round the column of air between gem and slab.</summary>
        public static Pose Splinter(FloatSpec s, int i, float t, bool moving) {
            float angle = SplinterAngle(s, i, t, moving);
            double a = angle * Math.PI / 180.0;
            P3 tilt = FloatLayout.SplinterTilt(i);
            float y = FloatLayout.SplinterHeight(s, i);
            if (moving) y += SplinterBobShare * s.BobHeight * Wave(t, s.BobSeconds * 1.3f, i * 1.7f);
            return new Pose {
                Position = new P3(s.OrbitRadius * (float)Math.Cos(a), y, s.OrbitRadius * (float)Math.Sin(a)),
                Euler = new P3(tilt.X, tilt.Y - angle, tilt.Z), Scale = s.SplinterScale
            };
        }

        /// <summary>STAND frame: a stone chip bobs slowly and rocks a few degrees.</summary>
        public static Pose Fragment(FloatSpec s, int i, float t, bool moving) {
            Pose p = FloatLayout.Fragment(s, i);
            if (!moving) return p;
            float phase = FloatLayout.FragmentPhase(i);
            p.Position.Y += FragmentBobShare * s.BobHeight * Wave(t, s.BobSeconds * 1.6f, phase);
            p.Euler.Y += FragmentWobble * Wave(t, s.BobSeconds * 3.1f, phase * 1.3f);
            return p;
        }
    }    /// <summary>The dormant tablet's measures: while nobody uses the shrine the pieces lie folded in the cradle, inside
    /// <c>Radius</c> of the stand's axis and below <c>Height</c> above the stand's origin (the floor of the cradle's
    /// hollow). The scales are shares of the floating sizes; RestLayout shrinks them further when they would not fit.</summary>
    internal struct RestSpec {
        public float Radius, Height;
        public float SlabScale, SlabTilt;            // the resting slab: its size (1 = full) and degrees it leans back (90 = flat, face up)
        public float GemScale, GemGap;               // the resting gem: share of its floating size, metres above the seat
        public float ShardScale;                     // the folded carrier shards, share of their floating size
        public float SplinterScale, SplinterRadius;  // the splinters drawn in: share of their size, radius of their ring
        public float ChipScale, ChipRadius;          // the stone chips gathered round the seat
        public float Light;                          // the gem's light and glow at rest, share of full

        public static RestSpec Default => new RestSpec {
            Radius = 0.165f, Height = 0.18f, SlabScale = 0.42f, SlabTilt = 78f, GemScale = 0.45f, GemGap = 0.005f, ShardScale = 0.5f,
            SplinterScale = 0.4f, SplinterRadius = 0.07f, ChipScale = 0.6f, ChipRadius = 0.10f, Light = 0.15f
        };
    }

    /// <summary>Where the pieces lie while the tablet is dormant (same frames as FloatLayout). Built to fit: whatever
    /// the settings, every piece stays inside the RestSpec's radius and under its height. The slab lies small and
    /// nearly flat like a lid, its carrier shards folded along its sides, the gem under it on the seat, the splinters
    /// and chips drawn in round them.</summary>
    internal static class RestLayout {
        public const float Margin = 0.005f;           // kept clear of the radius and the height
        public const float ShardSide = 0.004f;        // air between a folded shard and the slab's side
        public const float MaxShardShare = 0.48f;     // a folded shard's length, of the slab's height at most (two lie along a side)
        public const float MinShare = 0.05f, MinRoom = 0.04f;
        const float Deg = (float)(Math.PI / 180.0);

        static bool Bad(float v) => float.IsNaN(v) || float.IsInfinity(v);
        static float Share(float v) => Bad(v) ? MinShare : Math.Max(MinShare, Math.Min(1f, v));
        static float Positive(float v, float fallback) => Bad(v) || v <= 0f ? fallback : v;
        static float Frac(float v) => v - (float)Math.Floor(v);

        public static float Radius(RestSpec r) => Math.Max(0.02f, Positive(r.Radius, 0.165f));
        /// <summary>The height everything stays under: the spec's, but never less than a little room above the seat.</summary>
        public static float Height(FloatSpec s, RestSpec r) => Math.Max(Positive(r.Height, 0.18f), Seat(s) + MinRoom);
        static float Seat(FloatSpec s) => Bad(s.PlinthTop) ? 0f : Math.Max(0f, s.PlinthTop);
        public static float Tilt(RestSpec r) => Bad(r.SlabTilt) ? 78f : Math.Max(0f, Math.Min(90f, r.SlabTilt));

        /// <summary>The folded carrier shards' size.</summary>
        public static float ShardScale(FloatSpec s, RestSpec r) {
            float scale = Positive(s.ShardScale, 1f) * Share(r.ShardScale), length = s.ShardMaxY - s.ShardMinY;
            if (length > 1e-5f && length * scale > MaxShardShare * s.Height) scale = MaxShardShare * s.Height / length;
            return scale;
        }

        /// <summary>TABLET frame: carrier shard <paramref name="i"/> folded: lying along the slab's side edge, its base at
        /// the corner and its tip toward the middle of the side.</summary>
        public static Pose Shard(FloatSpec s, RestSpec r, int i) {
            bool right = i == 1 || i == 2, upper = i >= 2;
            float scale = ShardScale(s, r);
            float x = (right ? 1f : -1f) * (0.5f * s.Width + ShardSide + s.ShardHalfWidth * scale);
            float y = (upper ? 1f : -1f) * (0.5f * s.Height + s.ShardMinY * scale);
            return new Pose { Position = new P3(x, y, 0f), Euler = new P3(0f, 0f, upper ? 180f : 0f), Scale = scale };
        }

        /// <summary>TABLET frame: half the size of the box round the slab with its shards folded (before the slab's own scale).</summary>
        public static P3 FoldedHalf(FloatSpec s, RestSpec r) {
            float shard = FloatLayout.Shards(s) > 0 ? ShardScale(s, r) : 0f;
            float reach = shard > 0f ? ShardSide + 2f * s.ShardHalfWidth * shard : 0f;
            return new P3(0.5f * s.Width + reach, 0.5f * s.Height, Math.Max(0.5f * s.Thickness, s.ShardHalfWidth * shard));
        }

        /// <summary>STAND frame: the slab at rest, as high as the height allows, small enough for the radius.</summary>
        public static Pose Tablet(FloatSpec s, RestSpec r) {
            float tilt = Tilt(r), sin = (float)Math.Sin(tilt * Deg), cos = (float)Math.Cos(tilt * Deg);
            P3 half = FoldedHalf(s, r);
            float deep = half.Y * sin + half.Z * cos, tall = half.Y * cos + half.Z * sin;
            float reach = (float)Math.Sqrt(half.X * half.X + deep * deep);
            float top = Height(s, r) - Margin, room = top - Seat(s);
            float scale = Share(r.SlabScale);
            if (reach > 1e-6f && scale * reach > Radius(r) - Margin) scale = (Radius(r) - Margin) / reach;
            if (tall > 1e-6f && 2f * scale * tall > room) scale = room / (2f * tall);
            if (Bad(scale) || scale < 1e-3f) scale = 1e-3f;
            return new Pose { Position = new P3(0f, top - scale * tall, 0f), Euler = new P3(tilt, 0f, 0f), Scale = scale };
        }

        /// <summary>STAND frame: the resting slab's lowest point.</summary>
        public static float TabletLowEdge(FloatSpec s, RestSpec r) {
            float tilt = Tilt(r);
            P3 half = FoldedHalf(s, r);
            Pose p = Tablet(s, r);
            return p.Position.Y - p.Scale * (half.Y * (float)Math.Cos(tilt * Deg) + half.Z * (float)Math.Sin(tilt * Deg));
        }

        /// <summary>STAND frame: the gem at rest, small, on the seat under the slab.</summary>
        public static Pose Gem(FloatSpec s, RestSpec r) {
            float gap = Bad(r.GemGap) ? 0f : Math.Max(0f, r.GemGap), bottom = Seat(s) + gap;
            float tall = s.GemMaxY - s.GemMinY, top = Height(s, r) - Margin;
            if (bottom > top - 0.01f) bottom = Math.Max(0f, top - 0.01f);
            float scale = Positive(s.GemScale, 0.9f) * Share(r.GemScale);
            // under the slab when there is room for a gem worth seeing, else through it; never over the height or the radius
            float under = TabletLowEdge(s, r) - bottom, most = top - bottom;
            float room = Math.Max(under, 0.5f * most);
            if (tall > 1e-5f && scale * tall > room) scale = room / tall;
            if (s.GemHalfWidth > 1e-5f && scale * s.GemHalfWidth > Radius(r) - Margin) scale = (Radius(r) - Margin) / s.GemHalfWidth;
            if (Bad(scale) || scale < 1e-3f) scale = 1e-3f;
            return new Pose { Position = new P3(0f, bottom - scale * s.GemMinY, 0f), Scale = scale };
        }

        // a splinter's farthest point from its own origin, per unit of scale
        static float SplinterReach(FloatSpec s) => Math.Max(Math.Abs(s.ShardMinY), Math.Abs(s.ShardMaxY)) + Math.Abs(s.ShardHalfWidth);

        public static float SplinterScale(FloatSpec s, RestSpec r) {
            float scale = Positive(s.SplinterScale, 0.45f) * Share(r.SplinterScale), reach = SplinterReach(s);
            float most = Math.Min(Radius(r) - Margin, 0.5f * (Height(s, r) - Margin));
            if (reach > 1e-5f && scale * reach > most) scale = most / reach;
            return Bad(scale) || scale < 1e-3f ? 1e-3f : scale;
        }

        public static float SplinterRadius(FloatSpec s, RestSpec r) {
            float most = Radius(r) - Margin - SplinterScale(s, r) * SplinterReach(s);
            return Math.Max(0f, Math.Min(Bad(r.SplinterRadius) ? 0f : r.SplinterRadius, most));
        }

        /// <summary>STAND frame: splinter <paramref name="i"/>'s height on its drawn-in ring, each its own.</summary>
        public static float SplinterHeight(FloatSpec s, RestSpec r, int i) {
            float reach = SplinterScale(s, r) * SplinterReach(s);
            float high = Height(s, r) - Margin - reach, low = Math.Min(high, Math.Max(reach, Seat(s)));
            return low + (high - low) * Frac(0.12f + i * 0.618034f);
        }

        /// <summary>STAND frame: splinter <paramref name="i"/> at rest, <paramref name="angle"/> degrees round its ring.</summary>
        public static Pose Splinter(FloatSpec s, RestSpec r, int i, float angle) {
            double a = angle * Math.PI / 180.0;
            float radius = SplinterRadius(s, r);
            P3 tilt = FloatLayout.SplinterTilt(i);
            return new Pose {
                Position = new P3(radius * (float)Math.Cos(a), SplinterHeight(s, r, i), radius * (float)Math.Sin(a)),
                Euler = new P3(tilt.X, tilt.Y - angle, tilt.Z), Scale = SplinterScale(s, r)
            };
        }

        /// <summary>STAND frame: stone chip <paramref name="i"/> at rest, small, low, on its own side of the seat.</summary>
        public static Pose Fragment(FloatSpec s, RestSpec r, int i) {
            P3 size = FloatLayout.FragmentSize(i), from = FloatLayout.FragmentOffset(i);
            float reach = 0.5f * (float)Math.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z);
            float scale = Share(r.ChipScale), most = Math.Min(Radius(r) - Margin, 0.5f * (Height(s, r) - Margin));
            if (scale * reach > most) scale = most / reach;
            float radius = Math.Max(0f, Math.Min(Bad(r.ChipRadius) ? 0f : r.ChipRadius, Radius(r) - Margin - scale * reach));
            float flat = (float)Math.Sqrt(from.X * from.X + from.Z * from.Z);
            float dx = flat > 1e-5f ? from.X / flat : 0f, dz = flat > 1e-5f ? from.Z / flat : 1f;
            float spare = Height(s, r) - Margin - 2f * scale * reach;
            float y = scale * reach + Math.Max(0f, spare) * 0.2f * Frac(0.37f * i);
            return new Pose { Position = new P3(dx * radius, y, dz * radius), Euler = FloatLayout.FragmentTumble(i), Scale = scale };
        }
    }

    /// <summary>The tablet's rise out of the cradle and its sinking back: every piece's pose as a function of
    /// <c>raised</c> (0 = dormant in the cradle, 1 = floating). <c>raised</c> itself runs evenly with time (Step);
    /// each piece moves through its own part of it, eased (smoothstep): the gem wakes first, the slab lifts, turns
    /// upright and grows, the splinters spiral out, the chips follow one after another, the carrier shards unfold
    /// last. At 1 every pose is exactly FloatMotion's; at 0 exactly RestLayout's. Pure arithmetic: nothing is allocated.</summary>
    internal static class Raise {
        public const float GemFrom = 0f, GemTo = 0.55f, SlabFrom = 0.08f, SlabTo = 0.92f, ShardFrom = 0.5f, ShardTo = 1f,
                           SplinterFrom = 0.2f, SplinterTo = 1f, ChipFrom = 0.12f, ChipTo = 0.82f, ChipStagger = 0.03f;
        public const float SplinterSwirl = 140f;   // degrees the splinters' ring is wound back at rest

        /// <summary><paramref name="raised"/> after <paramref name="dt"/> seconds of rising or sinking: even in time, 0..1.</summary>
        public static float Step(float raised, bool up, float dt, float riseSeconds, float sinkSeconds) {
            if (float.IsNaN(raised)) raised = up ? 1f : 0f;
            float seconds = up ? riseSeconds : sinkSeconds;
            if (!(seconds > 1e-3f)) return up ? 1f : 0f;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            raised += (up ? dt : -dt) / seconds;
            return raised <= 0f ? 0f : raised >= 1f ? 1f : raised;
        }

        /// <summary>Smoothstep: 0 at or under 0 (and for NaN), 1 at or over 1.</summary>
        public static float Ease(float x) => !(x > 0f) ? 0f : x >= 1f ? 1f : x * x * (3f - 2f * x);

        /// <summary>How far a piece is through its own part [from, to] of the rise, eased.</summary>
        public static float Window(float raised, float from, float to) => to - from > 1e-5f ? Ease((raised - from) / (to - from)) : raised >= to ? 1f : 0f;

        public static float Lerp(float a, float b, float k) => !(k > 0f) ? a : k >= 1f ? b : a + (b - a) * k;

        static Pose Blend(Pose a, Pose b, float k) => new Pose {
            Position = new P3(Lerp(a.Position.X, b.Position.X, k), Lerp(a.Position.Y, b.Position.Y, k), Lerp(a.Position.Z, b.Position.Z, k)),
            Euler = new P3(Lerp(a.Euler.X, b.Euler.X, k), Lerp(a.Euler.Y, b.Euler.Y, k), Lerp(a.Euler.Z, b.Euler.Z, k)),
            Scale = Lerp(a.Scale, b.Scale, k)
        };

        /// <summary>STAND frame: the slab.</summary>
        public static Pose Tablet(FloatSpec s, RestSpec r, float raised, float t, bool moving) {
            if (raised >= 1f) return FloatMotion.Tablet(s, t, moving);
            return Blend(RestLayout.Tablet(s, r), FloatMotion.Tablet(s, t, moving), Window(raised, SlabFrom, SlabTo));
        }

        /// <summary>STAND frame: the gem. Its turn is the floating gem's at every height, so it never jumps.</summary>
        public static Pose Gem(FloatSpec s, RestSpec r, float raised, float t, bool moving) {
            Pose high = FloatMotion.Gem(s, t, moving);
            if (raised >= 1f) return high;
            Pose p = Blend(RestLayout.Gem(s, r), high, Window(raised, GemFrom, GemTo));
            p.Euler.Y = high.Euler.Y;
            return p;
        }

        /// <summary>TABLET frame: a carrier shard, unfolding from the slab's side to its corner.</summary>
        public static Pose Shard(FloatSpec s, RestSpec r, int i, float raised, float t, bool moving) {
            if (raised >= 1f) return FloatMotion.Shard(s, i, t, moving);
            return Blend(RestLayout.Shard(s, r, i), FloatMotion.Shard(s, i, t, moving), Window(raised, ShardFrom, ShardTo));
        }

        /// <summary>STAND frame: a splinter, spiralling out from its drawn-in ring to its orbit.</summary>
        public static Pose Splinter(FloatSpec s, RestSpec r, int i, float raised, float t, bool moving) {
            if (raised >= 1f) return FloatMotion.Splinter(s, i, t, moving);
            float k = Window(raised, SplinterFrom, SplinterTo);
            float angle = FloatMotion.SplinterAngle(s, i, t, moving) - SplinterSwirl * (1f - k);
            double a = angle * Math.PI / 180.0;
            Pose low = RestLayout.Splinter(s, r, i, angle), high = FloatMotion.Splinter(s, i, t, moving);
            float radius = Lerp(RestLayout.SplinterRadius(s, r), s.OrbitRadius, k);
            return new Pose {
                Position = new P3(radius * (float)Math.Cos(a), Lerp(low.Position.Y, high.Position.Y, k), radius * (float)Math.Sin(a)),
                Euler = low.Euler, Scale = Lerp(low.Scale, high.Scale, k)
            };
        }

        /// <summary>STAND frame: a stone chip, each a little after the one before.</summary>
        public static Pose Fragment(FloatSpec s, RestSpec r, int i, float raised, float t, bool moving) {
            if (raised >= 1f) return FloatMotion.Fragment(s, i, t, moving);
            float lag = ChipStagger * (i % FloatLayout.MaxFragments);
            return Blend(RestLayout.Fragment(s, r, i), FloatMotion.Fragment(s, i, t, moving), Window(raised, ChipFrom + lag, ChipTo + lag));
        }

        /// <summary>The gem's light and glow, as a share of full: the RestSpec's at rest, full once the gem has woken.</summary>
        public static float Light(RestSpec r, float raised) {
            float low = float.IsNaN(r.Light) ? 0f : Math.Max(0f, Math.Min(1f, r.Light));
            return Lerp(low, 1f, Window(raised, GemFrom, GemTo));
        }

        /// <summary>The slab's part of the rise (the stand turns to its reader with it).</summary>
        public static float Slab(float raised) => Window(raised, SlabFrom, SlabTo);
    }
}
