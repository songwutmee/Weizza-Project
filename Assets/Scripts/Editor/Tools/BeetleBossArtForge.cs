using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Paints placeholder drawings for the beetle mini boss (a tortoise beetle shell,
    /// a stag beetle's horn pincers, a scorpion tail) so the fight can be built and
    /// played before the real art exists. Real frames replace these by being
    /// dropped into the same folder under the same names.
    ///
    /// Every pose is the same creature with a few numbers changed (pincer opening,
    /// tail curl, body squash), drawn shape by shape with soft edges. Faces right.
    /// </summary>
    public static class BeetleBossArtForge
    {
        public const string Folder = "Assets/Art/Characters/BeetleBoss";
        private const int W = 1250, H = 940;
        private const float Ppu = 250f;            // the Characters import rule; 5 x 3.76 m canvas
        private static readonly Vector2 Origin = new(2.3f, 0.35f); // feet centre on the canvas, metres

        private struct Pose
        {
            public string name;
            public float pincerOpen;   // 0 closed, 1 wide
            public float tailCurl;     // 0 tail low behind, 1 curled high over the back, 1.4 thrust forward
            public float squash;       // >0 crouched, <0 stretched
            public float lean;         // metres the front shifts forward
            public float bob;          // metres up
            public float legTuck;      // 0 standing, 1 tucked (airborne)
        }

        private static readonly Pose[] Poses =
        {
            new() { name = "Beetle_Idle_0", pincerOpen = 0.25f, tailCurl = 0.95f },
            new() { name = "Beetle_Idle_1", pincerOpen = 0.32f, tailCurl = 1.02f, bob = 0.04f, squash = -0.02f },
            new() { name = "Beetle_Clamp_0", pincerOpen = 1f, tailCurl = 0.9f, squash = 0.08f, lean = -0.08f },
            new() { name = "Beetle_Clamp_1", pincerOpen = 0f, tailCurl = 1f, squash = -0.05f, lean = 0.28f },
            new() { name = "Beetle_Spit_0", pincerOpen = 0.4f, tailCurl = 0.7f, squash = 0.06f, lean = -0.1f },
            new() { name = "Beetle_Spit_1", pincerOpen = 0.45f, tailCurl = 1.4f, squash = -0.04f, lean = 0.12f },
            new() { name = "Beetle_Hop", pincerOpen = 0.6f, tailCurl = 1.1f, squash = -0.1f, bob = 0.15f, legTuck = 1f },
        };

        [MenuItem("Weizza/Art/Forge Beetle Boss Placeholder", priority = 60)]
        public static void Forge()
        {
            Directory.CreateDirectory(Folder);
            foreach (var pose in Poses)
            {
                var path = $"{Folder}/{pose.name}.png";
                File.WriteAllBytes(path, Paint(pose).EncodeToPNG());
            }
            AssetDatabase.Refresh();
            foreach (var pose in Poses)
            {
                var path = $"{Folder}/{pose.name}.png";
                if (AssetImporter.GetAtPath(path) is not TextureImporter imp) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = Ppu;
                imp.filterMode = FilterMode.Bilinear;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                var st = new TextureImporterSettings();
                imp.ReadTextureSettings(st);
                st.spriteAlignment = (int)SpriteAlignment.Custom;
                st.spritePivot = new Vector2(Origin.x * Ppu / W, Origin.y * Ppu / H);
                st.spriteMeshType = SpriteMeshType.FullRect;
                imp.SetTextureSettings(st);
                imp.SaveAndReimport();
            }
            Debug.Log($"[Beetle Art] Painted {Poses.Length} placeholder poses into {Folder}.");
        }

        public static Sprite Load(string poseName) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{poseName}.png");

        // ---- Painting -----------------------------------------------------------

        private static readonly Color Shell = new(0.72f, 0.12f, 0.11f);
        private static readonly Color ShellDark = new(0.16f, 0.07f, 0.08f);
        private static readonly Color Cream = new(0.97f, 0.88f, 0.82f);
        private static readonly Color Chitin = new(0.13f, 0.1f, 0.12f);
        private static readonly Color ChitinLight = new(0.3f, 0.22f, 0.24f);
        private static readonly Color Horn = new(0.45f, 0.13f, 0.1f);
        private static readonly Color Eye = new(1f, 0.85f, 0.25f);

        private static Texture2D Paint(Pose p)
        {
            var c = new Canvas(W, H);
            var lift = p.bob + (p.legTuck * 0.35f);
            var sy = 1f - p.squash;
            var sx = 1f + p.squash * 0.6f;
            Vector2 B(float x, float y) => new(x * sx + (x > 0.3f ? p.lean : p.lean * 0.3f), y * sy + lift);

            // Far legs, behind everything.
            Legs(c, B, p, far: true);

            // Tail: a chain of beads from the rear, curling up over the back.
            Tail(c, B, p);

            // Shell: dark rim, red dome, black pattern, cream highlight.
            var shellC = B(-0.05f, 0.95f);
            c.Ellipse(shellC, new Vector2(1.08f * sx, 0.66f * sy), ShellDark);
            c.Ellipse(shellC + new Vector2(0f, 0.03f), new Vector2(1.0f * sx, 0.6f * sy), Shell);
            c.Ellipse(shellC + new Vector2(-0.35f, 0.15f), new Vector2(0.3f, 0.2f), ShellDark);
            c.Ellipse(shellC + new Vector2(0.25f, 0.22f), new Vector2(0.22f, 0.15f), ShellDark);
            c.Ellipse(shellC + new Vector2(-0.05f, -0.2f), new Vector2(0.45f, 0.12f), ShellDark);
            c.Ellipse(shellC + new Vector2(-0.6f, -0.12f), new Vector2(0.14f, 0.1f), ShellDark);
            c.Ellipse(shellC + new Vector2(0.05f, 0.32f), new Vector2(0.55f, 0.14f), Cream, 0.85f);
            c.Ellipse(shellC + new Vector2(-0.15f, 0.36f), new Vector2(0.25f, 0.06f), Color.white, 0.6f);

            // Head and horn pincers (the stag beetle part).
            var head = B(1.02f, 0.78f);
            c.Ellipse(head, new Vector2(0.36f, 0.3f), Chitin);
            c.Ellipse(head + new Vector2(0.05f, 0.1f), new Vector2(0.2f, 0.08f), ChitinLight, 0.8f);
            Pincer(c, head, p.pincerOpen, upper: true);
            Pincer(c, head, p.pincerOpen, upper: false);
            c.Circle(head + new Vector2(0.14f, 0.05f), 0.065f, Eye);
            c.Circle(head + new Vector2(0.16f, 0.06f), 0.025f, Chitin);

            // Near legs over the shell's lower edge.
            Legs(c, B, p, far: false);

            return c.ToTexture();
        }

        private static void Pincer(Canvas c, Vector2 head, float open, bool upper)
        {
            var s = upper ? 1f : -1f;
            var spread = Mathf.Lerp(0.05f, 0.55f, open) * s;
            var root = head + new Vector2(0.25f, 0.08f * s);
            var mid = root + new Vector2(0.38f, spread * 0.8f + 0.12f * s);
            var tip = mid + new Vector2(0.32f, -spread * 0.7f - 0.05f * s);
            c.Capsule(root, mid, 0.085f, Horn);
            c.Capsule(mid, tip, 0.06f, Horn);
            // An inner tooth on each blade.
            c.Capsule(mid, mid + new Vector2(0.06f, -0.14f * s), 0.03f, Horn);
            c.Capsule(root, mid, 0.03f, new Color(0.75f, 0.3f, 0.22f), 0.7f);
        }

        private static void Tail(Canvas c, System.Func<float, float, Vector2> b, Pose p)
        {
            var start = b(-0.95f, 1.0f);
            // Bezier from the rear, up, and over: tailCurl swings the control points.
            var curl = p.tailCurl;
            var c1 = start + new Vector2(-0.75f, 0.35f + curl * 0.5f);
            var c2 = start + new Vector2(-0.3f + curl * 0.6f, 1.25f + curl * 0.45f);
            var end = start + new Vector2(0.15f + curl * 0.75f, 1.1f + curl * 0.35f - Mathf.Max(0f, curl - 1f) * 0.9f);
            const int beads = 9;
            var prev = start;
            for (var i = 0; i <= beads; i++)
            {
                var t = i / (float)beads;
                var pt = Bezier(start, c1, c2, end, t);
                var r = Mathf.Lerp(0.2f, 0.1f, t);
                c.Circle(pt, r + 0.025f, ShellDark);
                c.Circle(pt, r, i % 2 == 0 ? Chitin : ChitinLight);
                prev = pt;
            }
            // Stinger: a hooked spike off the last bead, pointing along the curl.
            var dir = (end - Bezier(start, c1, c2, end, 0.9f)).normalized;
            var hook = new Vector2(dir.y, -dir.x);
            var s0 = prev + dir * 0.08f;
            var s1 = s0 + dir * 0.28f + hook * 0.1f;
            var s2 = s1 + dir * 0.12f + hook * 0.18f;
            c.Capsule(s0, s1, 0.08f, Chitin);
            c.Capsule(s1, s2, 0.035f, Chitin);
            c.Circle(s0, 0.11f, Horn);
        }

        private static void Legs(Canvas c, System.Func<float, float, Vector2> b, Pose p, bool far)
        {
            var xs = far ? new[] { -0.55f, 0.05f, 0.6f } : new[] { -0.7f, -0.1f, 0.5f };
            var color = far ? ShellDark : Chitin;
            foreach (var x in xs)
            {
                var hip = b(x, 0.62f);
                var outward = x < 0f ? -1f : 1f;
                var knee = hip + new Vector2(0.22f * outward, 0.22f - p.legTuck * 0.1f);
                var foot = new Vector2(hip.x + 0.38f * outward, Mathf.Lerp(0f, hip.y - 0.25f, p.legTuck));
                c.Capsule(hip, knee, far ? 0.05f : 0.06f, color);
                c.Capsule(knee, foot, far ? 0.04f : 0.05f, color);
                c.Capsule(foot, foot + new Vector2(0.09f * outward, 0f), 0.03f, color);
            }
        }

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            var u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        /// <summary>Soft edged shape painting in metres, alpha blended over what is already there.</summary>
        private sealed class Canvas
        {
            private readonly int _w, _h;
            private readonly Color[] _px;

            public Canvas(int w, int h)
            {
                _w = w; _h = h;
                _px = new Color[w * h];
            }

            private Vector2 ToPx(Vector2 m) => (m + Origin) * Ppu;

            public void Ellipse(Vector2 centre, Vector2 radii, Color col, float alpha = 1f) =>
                Fill(centre, Mathf.Max(radii.x, radii.y), q =>
                {
                    var d = new Vector2(q.x / radii.x, q.y / radii.y);
                    return (d.magnitude - 1f) * Mathf.Min(radii.x, radii.y);
                }, col, alpha);

            public void Circle(Vector2 centre, float r, Color col, float alpha = 1f) =>
                Fill(centre, r, q => q.magnitude - r, col, alpha);

            public void Capsule(Vector2 a, Vector2 b, float r, Color col, float alpha = 1f)
            {
                var mid = (a + b) * 0.5f;
                var reach = Vector2.Distance(a, b) * 0.5f + r;
                Fill(mid, reach, q =>
                {
                    var p = q + mid;
                    var ab = b - a;
                    var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                    return Vector2.Distance(p, a + ab * t) - r;
                }, col, alpha);
            }

            /// <summary>Signed distance in metres (negative inside), one pixel of soft edge.</summary>
            private void Fill(Vector2 centre, float reach, System.Func<Vector2, float> sdf, Color col, float alpha)
            {
                var cp = ToPx(centre);
                var rp = (reach + 0.05f) * Ppu;
                var x0 = Mathf.Max(0, Mathf.FloorToInt(cp.x - rp)); var x1 = Mathf.Min(_w - 1, Mathf.CeilToInt(cp.x + rp));
                var y0 = Mathf.Max(0, Mathf.FloorToInt(cp.y - rp)); var y1 = Mathf.Min(_h - 1, Mathf.CeilToInt(cp.y + rp));
                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var m = new Vector2(x + 0.5f, y + 0.5f) / Ppu - Origin - centre;
                    var cover = Mathf.Clamp01(0.5f - sdf(m) * Ppu) * alpha;
                    if (cover <= 0f) continue;
                    var i = y * _w + x;
                    var dst = _px[i];
                    var outA = cover + dst.a * (1f - cover);
                    var rgb = outA > 0f ? ((Vector4)col * cover + (Vector4)dst * dst.a * (1f - cover)) / outA : Vector4.zero;
                    _px[i] = new Color(rgb.x, rgb.y, rgb.z, outA);
                }
            }

            public Texture2D ToTexture()
            {
                var tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false);
                tex.SetPixels(_px);
                tex.Apply();
                return tex;
            }
        }
    }
}
