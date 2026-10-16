using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Gives a painted piece a collider that follows its drawn outline instead of a box.
    ///
    /// A box over a rock either leaves air under the dome or fills a corner the art
    /// does not have, and the player stands on nothing or walks into nothing. The
    /// player moves on a CharacterController, which is PhysX, so this is a 3D mesh
    /// collider (a PolygonCollider2D would be invisible to it). The mesh is the
    /// sprite's own physics shape, thinned out so the top is smooth to walk on,
    /// then pulled one metre deep like the box was.
    ///
    /// One mesh asset per sprite (and one mirrored copy for flipX), shared by every
    /// piece that uses the sprite. The mesh is in the sprite's local units, so the
    /// piece's own scale resizes the collider exactly as it resizes the drawing.
    /// </summary>
    public static class OutlineColliders
    {
        private const string Folder = "Assets/GameData/Colliders";

        // Pieces that fill more of their rectangle than this stay boxes: a flat
        // ground strip gains nothing from an outline.
        private const float BoxIsFineAbove = 0.85f;

        // How far the simplified outline may stray from the drawn one, in sprite
        // units (about metres). Small enough to keep the shape, big enough to drop
        // the moss fuzz that would catch the player's feet.
        private const float Tolerance = 0.05f;

        // Half of the depth. Matches the old box (size z 1).
        private const float HalfDepth = 0.5f;

        // An island smaller than this share of the biggest piece is a speck.
        private const float SpeckShare = 0.05f;

        /// <summary>True when this sprite is shaped enough that a box would be wrong.</summary>
        public static bool Wants(Sprite sprite)
        {
            if (sprite == null || sprite.GetPhysicsShapeCount() == 0) return false;
            var paths = ReadPaths(sprite, false);
            if (paths.Count == 0) return false;
            var area = 0f;
            foreach (var p in paths) area += Mathf.Abs(SignedArea(p));
            var b = sprite.bounds;
            var box = b.size.x * b.size.y;
            return box > 0.0001f && area / box < BoxIsFineAbove;
        }

        /// <summary>Replaces the box on this renderer's object with the outline mesh. False if nothing changed.</summary>
        public static bool Apply(SpriteRenderer renderer)
        {
            var sprite = renderer.sprite;
            if (!Wants(sprite)) return false;

            var mesh = MeshFor(sprite, renderer.flipX);
            if (mesh == null) return false;

            var go = renderer.gameObject;
            var box = go.GetComponent<BoxCollider>();
            var existing = go.GetComponent<MeshCollider>();
            if (box != null) Undo.DestroyObjectImmediate(box);

            var collider = existing != null ? existing : Undo.AddComponent<MeshCollider>(go);
            Undo.RecordObject(collider, "Outline collider");
            collider.sharedMesh = mesh;
            collider.convex = false;
            collider.isTrigger = false;
            EditorUtility.SetDirty(collider);
            return true;
        }

        [MenuItem("Weizza/Art/Fit Colliders To Outline (All Open Scenes)", priority = 70)]
        private static void FitAll()
        {
            var changed = 0;
            var skipped = 0;
            var names = new SortedDictionary<string, int>();
            foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var box = r.GetComponent<BoxCollider>();
                if (box == null || box.isTrigger || r.sprite == null) continue;
                if (Apply(r))
                {
                    changed++;
                    names[r.sprite.name] = names.TryGetValue(r.sprite.name, out var n) ? n + 1 : 1;
                }
                else skipped++;
            }

            AssetDatabase.SaveAssets();
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetSceneAt(i));

            var report = new System.Text.StringBuilder($"[Outline Colliders] {changed} piece(s) now follow their outline, {skipped} kept as boxes.");
            foreach (var kv in names) report.Append($"\n  {kv.Value} x {kv.Key}");
            Debug.Log(report.ToString());
        }

        [MenuItem("Weizza/Art/Fit Colliders To Outline (Selected)", priority = 71)]
        private static void FitSelected()
        {
            var changed = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true))
                    if (r.GetComponent<BoxCollider>() != null && Apply(r)) changed++;
            AssetDatabase.SaveAssets();
            Debug.Log($"[Outline Colliders] {changed} piece(s) changed.");
        }

        // ---- Mesh asset -----------------------------------------------------------

        /// <summary>The shared outline mesh for a sprite, built and saved on first use.</summary>
        public static Mesh MeshForSprite(Sprite sprite, bool flip) => MeshFor(sprite, flip);

        private static Mesh MeshFor(Sprite sprite, bool flip)
        {
            var path = $"{Folder}/Outline_{sprite.name}{(flip ? "_flip" : "")}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            mesh = Build(sprite, flip);
            if (mesh == null) return null;

            if (!AssetDatabase.IsValidFolder("Assets/GameData")) AssetDatabase.CreateFolder("Assets", "GameData");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/GameData", "Colliders");
            mesh.name = $"Outline_{sprite.name}{(flip ? "_flip" : "")}";
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh Build(Sprite sprite, bool flip)
        {
            var paths = ReadPaths(sprite, flip);
            if (paths.Count == 0) return null;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            foreach (var outline in paths)
            {
                var poly = Simplify(outline, Tolerance);
                if (poly.Count < 3) continue;
                if (SignedArea(poly) < 0f) poly.Reverse();

                var capIndices = Triangulate(poly);

                // Front cap faces the camera (minus Z), back cap faces away.
                var front = vertices.Count;
                foreach (var p in poly) vertices.Add(new Vector3(p.x, p.y, -HalfDepth));
                var back = vertices.Count;
                foreach (var p in poly) vertices.Add(new Vector3(p.x, p.y, HalfDepth));

                for (var i = 0; i < capIndices.Count; i += 3)
                {
                    AddFacing(vertices, triangles, front + capIndices[i], front + capIndices[i + 1], front + capIndices[i + 2], Vector3.back);
                    AddFacing(vertices, triangles, back + capIndices[i], back + capIndices[i + 1], back + capIndices[i + 2], Vector3.forward);
                }

                // Side walls: the surface the player actually stands on and runs into.
                for (var i = 0; i < poly.Count; i++)
                {
                    var j = (i + 1) % poly.Count;
                    var edge = poly[j] - poly[i];
                    var outward = new Vector3(edge.y, -edge.x, 0f);
                    AddFacing(vertices, triangles, front + i, front + j, back + j, outward);
                    AddFacing(vertices, triangles, front + i, back + j, back + i, outward);
                }
            }

            if (triangles.Count == 0) return null;

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>Adds a triangle wound so its face looks along "outward".</summary>
        private static void AddFacing(List<Vector3> v, List<int> tris, int a, int b, int c, Vector3 outward)
        {
            var normal = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(normal, outward) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        // ---- Outline ---------------------------------------------------------------

        /// <summary>The sprite's physics shape, specks removed, mirrored for flipX.</summary>
        private static List<List<Vector2>> ReadPaths(Sprite sprite, bool flip)
        {
            var all = new List<List<Vector2>>();
            var biggest = 0f;
            for (var i = 0; i < sprite.GetPhysicsShapeCount(); i++)
            {
                var points = new List<Vector2>();
                sprite.GetPhysicsShape(i, points);
                if (points.Count < 3) continue;
                biggest = Mathf.Max(biggest, Mathf.Abs(SignedArea(points)));
                all.Add(points);
            }

            var kept = new List<List<Vector2>>();
            foreach (var points in all)
            {
                if (Mathf.Abs(SignedArea(points)) < biggest * SpeckShare) continue;
                if (flip)
                {
                    for (var i = 0; i < points.Count; i++) points[i] = new Vector2(-points[i].x, points[i].y);
                    points.Reverse();
                }
                kept.Add(points);
            }
            return kept;
        }

        private static float SignedArea(List<Vector2> p)
        {
            var a = 0f;
            for (var i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                a += p[i].x * q.y - q.x * p[i].y;
            }
            return a * 0.5f;
        }

        /// <summary>Douglas-Peucker on a closed outline: split at the two farthest points, thin each side.</summary>
        private static List<Vector2> Simplify(List<Vector2> points, float tolerance)
        {
            if (points.Count <= 4) return new List<Vector2>(points);

            var far = 0;
            var best = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var d = (points[i] - points[0]).sqrMagnitude;
                if (d > best) { best = d; far = i; }
            }

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[far] = true;
            Thin(points, 0, far, tolerance, keep);
            Thin(points, far, points.Count, tolerance, keep);

            var result = new List<Vector2>();
            for (var i = 0; i < points.Count; i++) if (keep[i]) result.Add(points[i]);
            return result;
        }

        // The chain runs from "from" to "to" (exclusive end wraps to point 0 when to == Count).
        private static void Thin(List<Vector2> pts, int from, int to, float tolerance, bool[] keep)
        {
            if (to - from < 2) return;
            var a = pts[from];
            var b = pts[to % pts.Count];
            var worst = -1;
            var worstDistance = tolerance;
            for (var i = from + 1; i < to; i++)
            {
                var d = DistanceToSegment(pts[i], a, b);
                if (d > worstDistance) { worstDistance = d; worst = i; }
            }
            if (worst < 0) return;
            keep[worst] = true;
            Thin(pts, from, worst, tolerance, keep);
            Thin(pts, worst, to, tolerance, keep);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len = ab.sqrMagnitude;
            if (len < 1e-9f) return (p - a).magnitude;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return (p - (a + ab * t)).magnitude;
        }

        // ---- Triangulation ----------------------------------------------------------

        /// <summary>Ear clipping for a counter clockwise outline. Returns indices into it, three per triangle.</summary>
        private static List<int> Triangulate(List<Vector2> poly)
        {
            var result = new List<int>();
            var index = new List<int>();
            for (var i = 0; i < poly.Count; i++) index.Add(i);

            var guard = poly.Count * poly.Count;
            while (index.Count > 3 && guard-- > 0)
            {
                var clipped = false;
                for (var i = 0; i < index.Count; i++)
                {
                    var ia = index[(i + index.Count - 1) % index.Count];
                    var ib = index[i];
                    var ic = index[(i + 1) % index.Count];
                    var a = poly[ia];
                    var b = poly[ib];
                    var c = poly[ic];

                    if (Cross(b - a, c - b) <= 1e-7f) continue;   // reflex or flat corner

                    var ear = true;
                    foreach (var other in index)
                    {
                        if (other == ia || other == ib || other == ic) continue;
                        if (InTriangle(poly[other], a, b, c)) { ear = false; break; }
                    }
                    if (!ear) continue;

                    result.Add(ia);
                    result.Add(ib);
                    result.Add(ic);
                    index.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // a pinched outline: the side walls still work without every cap triangle
            }

            if (index.Count == 3)
            {
                result.Add(index[0]);
                result.Add(index[1]);
                result.Add(index[2]);
            }
            return result;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(b - a, p - a);
            var d2 = Cross(c - b, p - b);
            var d3 = Cross(a - c, p - c);
            var neg = d1 < 0f || d2 < 0f || d3 < 0f;
            var pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }
    }
}
