using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Puts bones into the team's single beetle painting so it can move. The body
    /// bends as one mesh, written straight into the sprite so it opens in the Skinning
    /// Editor like a rig made by hand. The tail is cut out into four rigid plates, one
    /// per segment, because a tail whipped hard inside one mesh smears. The painting
    /// itself is never touched: the cut copies live in Mock_Asset/BeetleRig.
    /// </summary>
    public static class BeetleRigForge
    {
        private const string ArtPath = "Assets/Mock_Asset/Miniboss_BugBettle.png";
        private const string PrefabPath = "Assets/Prefabs/Enemies/Beetle.prefab";
        private const string CutFolder = "Assets/Mock_Asset/BeetleRig";
        private const string BodyPath = CutFolder + "/Beetle_Body.png";

        // How wide the beetle stands in the world, in metres.
        private const float WorldWidth = 6.3f;
        private const int Cell = 8;

        // How far from the tail line, in painting pixels, still counts as tail.
        private const float TailRadius = 46f;

        private struct BoneSpec
        {
            public string name;
            public string parent;
            public Vector2 from, to;   // pixels, measured on the painting with y going DOWN
            public float reach;        // pixels of extra pull, so a big shell is not owned by a thin leg
        }

        // Read off the painting by eye. y is measured from the top, the way an image editor shows it.
        private static readonly BoneSpec[] Bones =
        {
            new() { name = "Body",   parent = null,   from = new(395, 255), to = new(255, 238), reach = 120 },
            new() { name = "Head",   parent = "Body", from = new(255, 238), to = new(190, 215), reach = 45 },
            new() { name = "Horn_1", parent = "Head", from = new(185, 180), to = new(110, 95),  reach = 6 },
            new() { name = "Horn_2", parent = "Horn_1", from = new(110, 95), to = new(28, 22), reach = 4 },
            new() { name = "Prong",  parent = "Head", from = new(165, 195), to = new(70, 145),  reach = 4 },
            new() { name = "Jaw",    parent = "Head", from = new(170, 228), to = new(50, 218),  reach = 4 },
            new() { name = "Tail_1", parent = "Body", from = new(478, 300), to = new(545, 268), reach = 8 },
            new() { name = "Tail_2", parent = "Tail_1", from = new(545, 268), to = new(572, 200), reach = 6 },
            new() { name = "Tail_3", parent = "Tail_2", from = new(572, 200), to = new(540, 145), reach = 5 },
            new() { name = "Tail_4", parent = "Tail_3", from = new(540, 145), to = new(450, 130), reach = 5 },
            new() { name = "Leg_1",  parent = "Body", from = new(225, 305), to = new(115, 350), reach = 3 },
            new() { name = "Leg_2",  parent = "Body", from = new(268, 322), to = new(255, 395), reach = 3 },
            new() { name = "Leg_3",  parent = "Body", from = new(335, 332), to = new(360, 400), reach = 3 },
            new() { name = "Leg_4",  parent = "Body", from = new(410, 332), to = new(445, 392), reach = 3 },
            new() { name = "Leg_5",  parent = "Body", from = new(462, 322), to = new(515, 388), reach = 3 },
        };

        [MenuItem("Weizza/Art/Rig Beetle Boss From Painting", priority = 61)]
        public static void Rig()
        {
            if (!File.Exists(ArtPath))
            {
                Debug.LogError($"[Weizza] No painting at {ArtPath}.");
                return;
            }

            // The painting itself is never changed. It is read from disk and cut into a body
            // and four tail pieces, and the rig is built on those copies.
            var painting = new Texture2D(2, 2);
            painting.LoadImage(File.ReadAllBytes(ArtPath));
            var w = painting.width;
            var h = painting.height;
            var source = painting.GetPixels32();
            Object.DestroyImmediate(painting);

            Directory.CreateDirectory(CutFolder);
            BuildBones(h, out var cutSegments);
            var pieces = CutTail(source, w, h, cutSegments, out var bodyPixels);
            WritePng(BodyPath, bodyPixels, w, h);
            AssetDatabase.ImportAsset(BodyPath, ImportAssetOptions.ForceSynchronousImport);
            foreach (var piece in pieces) ImportPiece(piece, w);

            var importer = (TextureImporter)AssetImporter.GetAtPath(BodyPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = w / WorldWidth;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            // Feet sit near the bottom of the painting, so standing on Y 0 needs no offset.
            settings.spritePivot = new Vector2(0.5f, 0.045f);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            var pixels = bodyPixels;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var rect = provider.GetSpriteRects()[0];
            var id = rect.spriteID;

            var bones = BuildBones(h, out var segments);
            BuildMesh(pixels, w, h, segments, out var vertices, out var indices, out var edges);

            provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(id, bones);
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            mesh.SetVertices(id, vertices);
            mesh.SetIndices(id, indices);
            mesh.SetEdges(id, edges);
            provider.Apply();
            importer.SaveAndReimport();

            Debug.Log($"[Weizza] Beetle rigged: {bones.Count} bones, {vertices.Length} vertices.");
            InstallOnPrefab(pieces);
        }

        // Bone positions are in the sprite's own pixel space, y up, measured from the bottom left.
        // A child bone's position is local to its parent, with x running along the parent.
        private static List<SpriteBone> BuildBones(int h, out List<(Vector2 a, Vector2 b, float reach, string name)> segments)
        {
            var list = new List<SpriteBone>();
            var index = new Dictionary<string, int>();
            var worldFrom = new List<Vector2>();
            var worldAngle = new List<float>();
            segments = new List<(Vector2, Vector2, float, string)>();

            foreach (var spec in Bones)
            {
                var a = new Vector2(spec.from.x, h - spec.from.y);
                var b = new Vector2(spec.to.x, h - spec.to.y);
                var dir = b - a;
                var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                var parent = spec.parent != null ? index[spec.parent] : -1;
                Vector2 localPos = a;
                var localAngle = angle;
                if (parent >= 0)
                {
                    var offset = a - worldFrom[parent];
                    localPos = Quaternion.Euler(0, 0, -worldAngle[parent]) * offset;
                    localAngle = angle - worldAngle[parent];
                }

                index[spec.name] = list.Count;
                worldFrom.Add(a);
                worldAngle.Add(angle);
                segments.Add((a, b, spec.reach, spec.name));

                list.Add(new SpriteBone
                {
                    name = spec.name,
                    guid = System.Guid.NewGuid().ToString(),
                    position = localPos,
                    rotation = Quaternion.Euler(0, 0, localAngle),
                    length = dir.magnitude,
                    parentId = parent,
                    color = Color.HSVToRGB(list.Count / (float)Bones.Length, 0.7f, 1f),
                });
            }

            return list;
        }

        // A grid over every cell that holds paint. Plain and dense on purpose: it bends smoothly
        // everywhere, and the Skinning Editor can simplify it later if it ever matters.
        private static void BuildMesh(Color32[] pixels, int w, int h,
            List<(Vector2 a, Vector2 b, float reach, string name)> segments,
            out Vertex2DMetaData[] vertices, out int[] indices, out Vector2Int[] edges)
        {
            var cols = Mathf.CeilToInt(w / (float)Cell);
            var rows = Mathf.CeilToInt(h / (float)Cell);
            var filled = new bool[cols, rows];
            for (var cy = 0; cy < rows; cy++)
            for (var cx = 0; cx < cols; cx++)
                filled[cx, cy] = CellHasPaint(pixels, w, h, cx, cy);

            var vertexIndex = new Dictionary<Vector2Int, int>();
            var verts = new List<Vertex2DMetaData>();
            var tris = new List<int>();
            var edgeList = new List<Vector2Int>();

            int Vertex(int gx, int gy)
            {
                var key = new Vector2Int(gx, gy);
                if (vertexIndex.TryGetValue(key, out var found)) return found;
                var p = new Vector2(Mathf.Min(gx * Cell, w), Mathf.Min(gy * Cell, h));
                verts.Add(new Vertex2DMetaData { position = p, boneWeight = Weigh(p, segments) });
                vertexIndex[key] = verts.Count - 1;
                return verts.Count - 1;
            }

            bool Filled(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows && filled[x, y];

            for (var cy = 0; cy < rows; cy++)
            for (var cx = 0; cx < cols; cx++)
            {
                if (!filled[cx, cy]) continue;
                var v00 = Vertex(cx, cy);
                var v10 = Vertex(cx + 1, cy);
                var v11 = Vertex(cx + 1, cy + 1);
                var v01 = Vertex(cx, cy + 1);
                tris.AddRange(new[] { v00, v01, v11, v00, v11, v10 });

                if (!Filled(cx, cy - 1)) edgeList.Add(new Vector2Int(v00, v10));
                if (!Filled(cx + 1, cy)) edgeList.Add(new Vector2Int(v10, v11));
                if (!Filled(cx, cy + 1)) edgeList.Add(new Vector2Int(v11, v01));
                if (!Filled(cx - 1, cy)) edgeList.Add(new Vector2Int(v01, v00));
            }

            vertices = verts.ToArray();
            indices = tris.ToArray();
            edges = edgeList.ToArray();
        }

        private static bool CellHasPaint(Color32[] pixels, int w, int h, int cx, int cy)
        {
            // Texture rows run from the bottom, the same way the sprite's own space does.
            for (var y = cy * Cell; y < Mathf.Min(h, (cy + 1) * Cell); y++)
            for (var x = cx * Cell; x < Mathf.Min(w, (cx + 1) * Cell); x++)
                if (pixels[y * w + x].a > 12) return true;
            return false;
        }

        // Nearest bones win, softly. Distance is shortened by each bone's reach so the shell
        // belongs to the body even right next to where a leg starts.
        private static BoneWeight Weigh(Vector2 p, List<(Vector2 a, Vector2 b, float reach, string name)> segments)
        {
            var best = new (int bone, float weight)[4];
            for (var i = 0; i < 4; i++) best[i] = (0, 0f);

            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                // The tail is cut out as rigid pieces, so the body mesh never follows it.
                if (s.name.StartsWith("Tail")) continue;
                var d = Mathf.Max(2f, DistanceToSegment(p, s.a, s.b) - s.reach);
                var weight = 1f / (d * d * d * d);
                for (var slot = 0; slot < 4; slot++)
                {
                    if (weight <= best[slot].weight) continue;
                    for (var move = 3; move > slot; move--) best[move] = best[move - 1];
                    best[slot] = (i, weight);
                    break;
                }
            }

            var total = best[0].weight + best[1].weight + best[2].weight + best[3].weight;
            return new BoneWeight
            {
                boneIndex0 = best[0].bone, weight0 = best[0].weight / total,
                boneIndex1 = best[1].bone, weight1 = best[1].weight / total,
                boneIndex2 = best[2].bone, weight2 = best[2].weight / total,
                boneIndex3 = best[3].bone, weight3 = best[3].weight / total,
            };
        }

        private static Transform FindChild(Transform root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;
            return null;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ---- Cutting the tail -----------------------------------------------------------
        //
        // A tail bent hard inside one mesh smears: the triangles between two segments
        // stretch across the gap. A scorpion tail is a row of hard plates anyway, so each
        // segment is cut out as its own picture and turns rigidly on its joint.

        private sealed class Piece
        {
            public string bone;
            public string path;
            public RectInt box;
            public Vector2 joint;   // the segment's start, in painting pixels, y up
        }

        private static List<Piece> CutTail(Color32[] source, int w, int h,
            List<(Vector2 a, Vector2 b, float reach, string name)> segments, out Color32[] body)
        {
            body = new Color32[source.Length];
            var owners = new List<int>[segments.Count];
            var tails = new List<int>();
            for (var i = 0; i < segments.Count; i++)
                if (segments[i].name.StartsWith("Tail")) { tails.Add(i); owners[i] = new List<int>(); }

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var index = y * w + x;
                if (source[index].a <= 12) continue;
                var p = new Vector2(x, y);

                // Tail or not is decided by plain distance to the tail line, not by the
                // weighted contest the mesh uses: the shell's big reach would otherwise
                // swallow the stinger hook and the stub where the tail leaves the body.
                var nearestTail = -1;
                var tailDistance = float.MaxValue;
                foreach (var t in tails)
                {
                    // Nothing behind the tail's first joint belongs to it: that is shell.
                    if (t == tails[0] && Vector2.Dot(p - segments[t].a, segments[t].b - segments[t].a) < 0f) continue;
                    var d = DistanceToSegment(p, segments[t].a, segments[t].b);
                    if (d < tailDistance) { tailDistance = d; nearestTail = t; }
                }
                var legDistance = float.MaxValue;
                for (var i = 0; i < segments.Count; i++)
                    if (segments[i].name.StartsWith("Leg"))
                        legDistance = Mathf.Min(legDistance, DistanceToSegment(p, segments[i].a, segments[i].b));

                if (nearestTail < 0 || tailDistance > TailRadius || legDistance < tailDistance)
                {
                    body[index] = source[index];
                    continue;
                }

                // Pixels right at a joint go to both neighbours, so a bent joint overlaps
                // instead of opening a crack. Only near the joint, or a copy sticks out
                // somewhere else once the tail bends.
                foreach (var t in tails)
                {
                    if (t == nearestTail) { owners[t].Add(index); continue; }
                    var nearJoint = Vector2.Distance(p, segments[t].a) < 22f || Vector2.Distance(p, segments[t].b) < 22f;
                    var d = DistanceToSegment(p, segments[t].a, segments[t].b);
                    if (nearJoint && d <= tailDistance + 5f) owners[t].Add(index);
                }
            }

            var pieces = new List<Piece>();
            foreach (var t in tails)
            {
                if (owners[t].Count == 0) continue;
                int x0 = w, y0 = h, x1 = 0, y1 = 0;
                foreach (var index in owners[t])
                {
                    var x = index % w;
                    var y = index / w;
                    x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y);
                    x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y);
                }
                var box = new RectInt(x0 - 2, y0 - 2, x1 - x0 + 5, y1 - y0 + 5);
                var pixels = new Color32[box.width * box.height];
                foreach (var index in owners[t])
                {
                    var x = index % w - box.x;
                    var y = index / w - box.y;
                    pixels[y * box.width + x] = source[index];
                }

                var piece = new Piece
                {
                    bone = segments[t].name,
                    path = $"{CutFolder}/Beetle_{segments[t].name}.png",
                    box = box,
                    joint = segments[t].a,
                };
                WritePng(piece.path, pixels, box.width, box.height);
                AssetDatabase.ImportAsset(piece.path, ImportAssetOptions.ForceSynchronousImport);
                pieces.Add(piece);
            }
            return pieces;
        }

        private static void ImportPiece(Piece piece, int paintingWidth)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(piece.path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = paintingWidth / WorldWidth;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            // Pivot on the joint, so turning the piece turns it about where it meets the last one.
            settings.spritePivot = new Vector2((piece.joint.x - piece.box.x) / piece.box.width,
                (piece.joint.y - piece.box.y) / piece.box.height);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void WritePng(string path, Color32[] pixels, int w, int h)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        // The rig rides beside the old frame by frame drawing rather than replacing it, so every
        // script that already talks to that renderer (flash, hide while dormant, the ink storm)
        // keeps working untouched. BeetleRig copies what it needs across each frame.
        private static void InstallOnPrefab(List<Piece> pieces)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BodyPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var drawing = root.transform.Find("Sprite");
                var old = drawing.Find("Rig");
                if (old != null) Object.DestroyImmediate(old.gameObject);

                var rigGo = new GameObject("Rig");
                rigGo.transform.SetParent(drawing, false);
                var renderer = rigGo.AddComponent<SpriteRenderer>();
                var source = drawing.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = source.sortingOrder;
                renderer.sharedMaterial = source.sharedMaterial;

                var skin = rigGo.AddComponent<SpriteSkin>();
                typeof(SpriteSkin).Assembly
                    .GetType("UnityEngine.U2D.Animation.SpriteSkinUtility")
                    .GetMethod("CreateBoneHierarchy",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(null, new object[] { skin });

                // Each tail plate hangs on its bone, drawn just behind the body so a tail
                // swung forward passes under the shell rather than over it. The bones are
                // still at rest here, so matching the rig root rotation puts every plate
                // exactly where it was painted.
                foreach (var piece in pieces)
                {
                    var bone = FindChild(rigGo.transform, piece.bone);
                    if (bone == null) continue;
                    var plate = new GameObject(piece.bone + " Plate");
                    plate.transform.SetParent(bone, false);
                    plate.transform.rotation = rigGo.transform.rotation;
                    var plateRenderer = plate.AddComponent<SpriteRenderer>();
                    plateRenderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(piece.path);
                    plateRenderer.sortingLayerID = source.sortingLayerID;
                    plateRenderer.sortingOrder = source.sortingOrder - 1;
                    plateRenderer.sharedMaterial = source.sharedMaterial;
                }

                var rig = rigGo.AddComponent<BeetleRig>();
                rig.Bind(source);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Weizza] Beetle prefab now draws the rigged painting.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
