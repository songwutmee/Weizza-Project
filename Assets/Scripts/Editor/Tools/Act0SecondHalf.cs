using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds the second half of the Act 0 map from the team's own Forest Silhouette pieces:
    /// a stone run under dark foreground, an open meadow, the boss room with foliage on both
    /// sides, and the place Soka waits afterwards. Every piece is a copy of one already
    /// painted in chunks 1 to 4, so colliders, sway and parallax come along unchanged.
    ///
    /// Run it once. It writes a report to Temp/act0_second_half.txt.
    /// </summary>
    public static class Act0SecondHalf
    {
        private const string RoomsFolder = "Assets/GameData/World/Rooms/";
        private const string ScenesFolder = "Assets/Scenes/World/";
        private const string PackFolder = "Assets/Mock_Asset/Forest_Silhouette_01/";

        // The boss room: walls stand where the dark thickets end, so the room is clear of black.
        private const float RoomLeftWallX = 173.5f;
        private const float RoomRightWallX = 222.5f;
        private const float LandingX = 233.8f;

        private static readonly StringBuilder Report = new();
        private static readonly Dictionary<string, SpriteRenderer> Templates = new();
        private static System.Random _rng;

        [MenuItem("Weizza/Act 0/Build Second Half")]
        public static void Build()
        {
            Report.Clear();
            Templates.Clear();
            _rng = new System.Random(11);

            for (var x = 20f; x < 160f; x += 40f) ChunkPainting.ChunkAt(x, true);
            CacheTemplates();

            var chunk5 = EnsureChunk(5, 160f);
            var chunk6 = EnsureChunk(6, 200f);

            ClearFireflies();
            Ground(chunk5, new[] { 183.3f });
            Ground(chunk6, new[] { 204.7f, 226f });
            Physics.SyncTransforms();

            Backdrop(chunk5, 160f, 3, 4);
            Backdrop(chunk6, 200f, 5, 2);
            StoneRun();
            DarkTunnel();
            Meadow(chunk5);
            Flanks(chunk5);
            Tail(chunk6);
            Physics.SyncTransforms();

            Enemies();
            BossRoom(chunk5, chunk6);
            Checkpoints();
            Lighting();
            WipeSystem();
            FixBeetlePrefab();

            Physics.SyncTransforms();
            ColliderTable(118f, 205f);

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/act0_second_half.txt", Report.ToString());
            Debug.Log("[Weizza] Act 0 second half built. Report in Temp/act0_second_half.txt");
        }

        // ---- Templates and cloning ------------------------------------------------

        private static void CacheTemplates()
        {
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sr.sprite == null || sr.name.StartsWith("Thicket")) continue;
                // Pieces in the chunks this tool builds from scratch are never templates.
                var sceneName = sr.gameObject.scene.name;
                if (sceneName.StartsWith("Forest_") && string.CompareOrdinal(sceneName, "Forest_07") >= 0) continue;
                var name = sr.sprite.texture.name;
                if (!Templates.ContainsKey(name)) Templates[name] = sr;
            }
        }

        private static SpriteRenderer Tpl(string textureName)
        {
            if (Templates.TryGetValue(textureName, out var sr) && sr != null) return sr;
            Report.AppendLine("MISSING TEMPLATE " + textureName);
            return null;
        }

        private static GameObject Clone(SpriteRenderer tpl, RoomRoot chunk, Transform parent, float x, float? y = null,
            float scaleMul = 1f, bool? flip = null)
        {
            if (tpl == null) return null;
            var go = Object.Instantiate(tpl.gameObject);
            go.name = tpl.gameObject.name;
            go.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(go, chunk.gameObject.scene);
            go.transform.SetParent(parent, true);
            var p = tpl.transform.position;
            go.transform.position = new Vector3(x, y ?? p.y, p.z);
            go.transform.localScale = tpl.transform.localScale * scaleMul;
            if (flip.HasValue) go.GetComponent<SpriteRenderer>().flipX = flip.Value;
            return go;
        }

        // A foreground copy dressed as another piece of the pack, centred where asked.
        private static GameObject Piece(SpriteRenderer tpl, Sprite sprite, RoomRoot chunk, Transform parent, float centreX,
            float centreY, float scale, int order, bool hanging)
        {
            var go = Clone(tpl, chunk, parent, centreX);
            if (go == null) return null;
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            go.transform.localScale = Vector3.one * scale;
            go.name = sprite.name;
            var z = go.transform.position.z;
            go.transform.position = new Vector3(centreX - sprite.bounds.center.x * scale, centreY - sprite.bounds.center.y * scale, z);
            var sway = go.GetComponent<SpriteSway>();
            if (sway != null) sway.Configure(hanging, hanging ? 0.22f : 0.12f);
            return go;
        }

        private static Sprite PackSprite(string folder, string file)
        {
            var path = PackFolder + folder + "/" + file + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                // Imported as a plain texture: make it a sprite at the pack's scale.
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spritePixelsPerUnit = 90f;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    Report.AppendLine("imported as sprite " + path);
                }
                if (sprite == null) Report.AppendLine("NO SPRITE " + path);
            }
            return sprite;
        }

        private static Transform Level(RoomRoot c) => ChunkPainting.Child(ChunkPainting.Child(c.transform, "Level"), "Art");
        private static Transform Scenery(RoomRoot c, string group) => ChunkPainting.GroupIn(c, group);
        private static Transform Story(RoomRoot c) => ChunkPainting.Child(c.transform, "Story");
        private static Transform EnemyGroup(RoomRoot c) => ChunkPainting.Child(c.transform, "Enemies");

        private static float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private static float GroundY(float x)
        {
            float y = 0f, best = -100f;
            foreach (var h in Physics.RaycastAll(new Vector3(x, 30f, 0f), Vector3.down, 60f))
            {
                if (h.collider.isTrigger || h.collider.GetComponentInParent<CharacterController>() != null) continue;
                if (h.collider.name == "Soka") continue;
                if (h.point.y > best && h.point.y < 8f) { best = h.point.y; y = h.point.y; }
            }
            return y;
        }

        // ---- Chunks ---------------------------------------------------------------

        private static RoomRoot EnsureChunk(int n, float xMin)
        {
            var id = "forest_" + n.ToString("00");
            var sceneName = "Forest_" + n.ToString("00");
            var defPath = RoomsFolder + "ROOM_" + id + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<RoomDefinition>(defPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<RoomDefinition>();
                def.id = id;
                def.sceneName = sceneName;
                def.areaId = "forest";
                def.displayNameKey = "ROOM_FOREST_" + n.ToString("00");
                def.worldRect = new Rect(xMin, -14f, 40f, 40f);
                def.mapColour = new Color(0.4875f, 0.75f, 0.658f);
                def.neighbours = new[] { new RoomNeighbour { roomId = "forest_" + (n - 1).ToString("00"), link = RoomLink.Seamless } };
                AssetDatabase.CreateAsset(def, defPath);
            }

            var atlas = ChunkPainting.Atlas;
            var has = false;
            foreach (var r in atlas.rooms) if (r == def) has = true;
            if (!has)
            {
                var list = new List<RoomDefinition>(atlas.rooms) { def };
                atlas.rooms = list.ToArray();
                EditorUtility.SetDirty(atlas);
            }

            var path = ScenesFolder + sceneName + ".unity";
            var existing = ChunkPainting.FindLoaded(def);
            if (existing != null) return existing;

            Scene scene;
            if (File.Exists(path))
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                return ChunkPainting.FindIn(scene);
            }

            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = new GameObject("Chunk " + id, typeof(BoxCollider));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var rect = def.worldRect;
            go.transform.position = new Vector3(rect.xMin, rect.yMin, 0f);
            var box = go.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(rect.width + 1f, rect.height, 12f);
            box.center = new Vector3(rect.width * 0.5f, rect.height * 0.5f, 0f);
            var root = go.AddComponent<RoomRoot>();
            var so = new SerializedObject(root);
            so.FindProperty("definition").objectReferenceValue = def;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, path);
            Report.AppendLine("created " + path);
            return root;
        }

        private static void ClearFireflies()
        {
            foreach (var b in Object.FindObjectsByType<BombFlyBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var scene = b.gameObject.scene;
                Object.DestroyImmediate(b.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static void Ground(RoomRoot chunk, float[] xs)
        {
            var tpl = Tpl("L51_ground_flat1920x512");
            foreach (var x in xs) Clone(tpl, chunk, Level(chunk), x);
        }

        // Sky, mist, far trunks and a few near trees, laid out by the spacing rules the
        // parallax needs (far layers are painted wider apart than they should look).
        private static void Backdrop(RoomRoot chunk, float xMin, int trunkStart, int pods)
        {
            var far = Scenery(chunk, "1 Far Fog");
            Clone(Tpl("L0_Voidpng"), chunk, far, xMin + 10f);
            Clone(Tpl("L0_Voidpng"), chunk, far, xMin + 30f);
            Clone(Tpl("L1_MistBand"), chunk, far, xMin + 20f);
            for (var i = 0; i < 4; i++)
                Clone(Tpl("L1_TrunkFar" + (1 + (trunkStart + i) % 7)), chunk, far, xMin + 3.7f + i * 11.25f);

            var near = Scenery(chunk, "2 Dark Things");
            var podNames = new[] { "L4_trunkWithPod", "L4_trunkWithPod2", "L4_trunkWithPod3" };
            for (var i = 0; i < pods; i++)
            {
                var x = xMin + 5f + i * (34f / Mathf.Max(1, pods)) + R(-2f, 2f);
                Clone(Tpl(podNames[i % 3]), chunk, near, x, null, R(0.85f, 1.1f));
            }
        }

        // ---- The stone run --------------------------------------------------------

        private static void StoneRun()
        {
            // Steps and plateaus for the existing stump (x 136), with gaps a jump can cross.
            var stones = new (float x, string name, float scale)[]
            {
                (128.8f, "L4_platform_stone2", 0.80f),
                (132.2f, "L4_platform_buttress", 0.80f),
                (140.6f, "L4_platform_stone1", 0.78f),
                (144.2f, "L4_platform_stone2", 0.88f),
                (152.4f, "L4_platform_buttress", 0.82f),
            };
            foreach (var s in stones)
            {
                var chunk = ChunkPainting.ChunkAt(s.x, true);
                Clone(Tpl(s.name), chunk, Level(chunk), s.x, null, s.scale, _rng.NextDouble() > 0.5);
            }
        }

        // Dark foreground over the stone run: leaf curtains hang down, undergrowth fills the
        // bottom, canopy shuts the top. It drifts past the camera like the rest of the foreground.
        private static void DarkTunnel()
        {
            var band3 = Tpl("L6_undergrowthBand3");
            var band5 = Tpl("L6_undergrowthBand5");
            var canopy = Tpl("L6_canopy");
            var curtains = new[] { PackSprite("Lv6 foreground_", "L6_curtain1"), PackSprite("Lv6 foreground_", "L6_curtain2"), PackSprite("Lv6 foreground_", "L6_curtain3") };

            var i = 0;
            for (var x = 122f; x < 160f; x += 4.8f, i++)
            {
                var chunk = ChunkPainting.ChunkAt(x, true);
                var fg = Scenery(chunk, "4 Foreground Silhouette");
                Clone(i % 2 == 0 ? band3 : band5, chunk, fg, x, null, R(0.85f, 1.2f), i % 3 == 0);
            }

            i = 0;
            for (var x = 124f; x < 159f; x += 6.4f, i++)
            {
                var chunk = ChunkPainting.ChunkAt(x, true);
                var fg = Scenery(chunk, "4 Foreground Silhouette");
                // Bottoms stay above head height at the sides and come lower in the middle, so
                // the player slips in and out of the dark rather than walking through a wall.
                var bottom = 1.0f + (i % 3) * 0.7f;
                var scale = 0.8f;
                var sprite = curtains[i % curtains.Length];
                var centreY = bottom + sprite.bounds.size.y * scale * 0.5f;
                Piece(band3, sprite, chunk, fg, x, centreY, scale, 2 + i % 3, true);
            }

            foreach (var x in new[] { 125f, 152f })
            {
                var chunk = ChunkPainting.ChunkAt(x, true);
                Clone(canopy, chunk, Scenery(chunk, "4 Foreground Silhouette"), x);
            }

            // A few vines hanging in the gloom.
            var vineA = Tpl("ภาพวาด 20261010(1)");
            var vineB = Tpl("ภาพวาด 20261010");
            for (var v = 0; v < 5; v++)
            {
                var x = 126f + v * 7.5f + R(-1.5f, 1.5f);
                var chunk = ChunkPainting.ChunkAt(x, true);
                Clone(v % 2 == 0 ? vineA : vineB, chunk, Scenery(chunk, "5 Vines"), x);
            }
        }

        // ---- The meadow -----------------------------------------------------------

        private static void Meadow(RoomRoot chunk)
        {
            var near = Scenery(chunk, "2 Dark Things");
            var ferns = new SpriteRenderer[7];
            for (var i = 0; i < 7; i++) ferns[i] = Tpl("L4_fernCluster" + (i + 1));

            var n = 0;
            for (var x = 163.5f; x < 199f; x += 1.1f + (float)_rng.NextDouble() * 1.3f, n++)
                Clone(ferns[_rng.Next(ferns.Length)], chunk, near, x, null, R(0.9f, 1.7f), _rng.NextDouble() > 0.5);

            var fg = Scenery(chunk, "4 Foreground Silhouette");
            var band3 = Tpl("L6_undergrowthBand3");
            var band5 = Tpl("L6_undergrowthBand5");
            for (var k = 0; k < 4; k++)
                Clone(k % 2 == 0 ? band5 : band3, chunk, fg, 166f + k * 9.5f, null, R(0.7f, 0.95f), k % 2 == 1);
            Report.AppendLine("meadow ferns " + n);
        }

        // Foliage on both sides of the boss room. Static, so it stays where the walls are.
        private static void Flanks(RoomRoot chunk)
        {
            var fg = Scenery(chunk, "4 Foreground Silhouette");
            var canopy = Tpl("L6_canopy");
            var curtainSprites = new[] { PackSprite("Lv6 foreground_", "L6_curtain1"), PackSprite("Lv6 foreground_", "L6_curtain2"), PackSprite("Lv6 foreground_", "L6_curtain3") };
            var bandSprites = new[] { PackSprite("Lv6 foreground_", "L6_undergrowthBand1"), PackSprite("Lv6 foreground_", "L6_undergrowthBand2"), PackSprite("Lv6 foreground_", "L6_undergrowthBand4") };

            var left = new[] { 168.4f, 170.2f, 172.0f };
            var right = new[] { 196.6f, 194.8f, 193.0f };
            for (var i = 0; i < 3; i++)
            {
                Piece(canopy, curtainSprites[i], chunk, fg, left[i], 5.6f, 1.0f, 6 + i, true);
                Piece(canopy, curtainSprites[(i + 1) % 3], chunk, fg, right[i], 5.6f, 1.0f, 6 + i, true);
            }
            // Undergrowth along the foot, so the edge reads as dense leaves, not a flat curtain.
            Piece(canopy, bandSprites[0], chunk, fg, 171.4f, -0.2f, 1.1f, 9, false);
            Piece(canopy, bandSprites[2], chunk, fg, 193.4f, -0.2f, 1.1f, 9, false);
        }

        private static void Tail(RoomRoot chunk)
        {
            var near = Scenery(chunk, "2 Dark Things");
            var ferns = new SpriteRenderer[7];
            for (var i = 0; i < 7; i++) ferns[i] = Tpl("L4_fernCluster" + (i + 1));
            for (var x = 201f; x < 237f; x += 2.2f + (float)_rng.NextDouble() * 1.6f)
                Clone(ferns[_rng.Next(ferns.Length)], chunk, near, x, null, R(0.9f, 1.5f), _rng.NextDouble() > 0.5);

            // An invisible wall at the end of Act 0, so nobody walks off the map.
            var wall = new GameObject("End Wall", typeof(BoxCollider));
            SceneManager.MoveGameObjectToScene(wall, chunk.gameObject.scene);
            wall.transform.SetParent(Level(chunk), false);
            wall.transform.position = new Vector3(238.5f, 10f, 0f);
            var box = wall.GetComponent<BoxCollider>();
            box.size = new Vector3(1f, 30f, 6f);

            // Soka waits here, in the first chunk after the boss room.
            var soka = GameObject.Find("Soka");
            if (soka != null)
            {
                soka.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(soka, chunk.gameObject.scene);
                soka.transform.SetParent(Story(chunk), true);
                var sr = soka.GetComponent<SpriteRenderer>();
                var minY = float.MaxValue;
                foreach (var v in sr.sprite.vertices) minY = Mathf.Min(minY, v.y);
                const float x = 205.5f;
                soka.transform.position = new Vector3(x, GroundY(x) - minY * soka.transform.lossyScale.y, 0f);
                sr.flipX = false;
                Report.AppendLine("soka at " + soka.transform.position);
            }
            else Report.AppendLine("Soka not found");
        }

        // ---- Enemies, boss, checkpoints --------------------------------------------

        private static void Spawn(string prefabPath, float x, string label)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var chunk = ChunkPainting.ChunkAt(x, true);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, chunk.gameObject.scene);
            go.transform.SetParent(EnemyGroup(chunk), true);
            go.transform.position = new Vector3(x, GroundY(x) + 0.2f, 0f);
            go.name = label;
            EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
            Report.AppendLine(label + " at " + go.transform.position.ToString("0.0"));
        }

        private static void Enemies()
        {
            const string ant = "Assets/Prefabs/Enemies/AntRider.prefab";
            const string fly = "Assets/Prefabs/Enemies/BombFly.prefab";
            // A first mixed group on the flat, a few fireflies over the stones.
            Spawn(ant, 108f, "AntRider");
            Spawn(fly, 113f, "Firefly");
            Spawn(fly, 117f, "Firefly");
            Spawn(fly, 131f, "Firefly");
            Spawn(fly, 141f, "Firefly");
            Spawn(fly, 151f, "Firefly");
        }

        private static void BossRoom(RoomRoot chunk5, RoomRoot chunk6)
        {
            var story = Story(chunk5);

            // The beetle waits in the middle of the room, asleep as a clump of flowers.
            Spawn("Assets/Prefabs/Enemies/Beetle.prefab", 183f, "Bloom Beetle");

            var arena = new GameObject("Boss Room");
            SceneManager.MoveGameObjectToScene(arena, chunk5.gameObject.scene);
            arena.transform.SetParent(story, false);
            arena.transform.position = Vector3.zero;
            // Walls first, so BossArena finds both before it reads the room edges.
            foreach (var x in new[] { 170f, 195f })
            {
                var wall = new GameObject("Wall " + x, typeof(BoxCollider));
                wall.transform.SetParent(arena.transform, false);
                wall.transform.position = new Vector3(x, 10f, 0f);
                var box = wall.GetComponent<BoxCollider>();
                box.size = new Vector3(1f, 30f, 6f);
                box.enabled = false;
            }
            arena.AddComponent<BossArena>();

            var landing = new GameObject("Boss Aftermath Landing");
            SceneManager.MoveGameObjectToScene(landing, chunk5.gameObject.scene);
            landing.transform.SetParent(story, false);
            landing.transform.position = new Vector3(199.2f, GroundY(199.2f) + 0.1f, 0f);

            var after = new GameObject("Boss Aftermath");
            SceneManager.MoveGameObjectToScene(after, chunk5.gameObject.scene);
            after.transform.SetParent(story, false);
            var comp = after.AddComponent<BossAftermath>();
            var so = new SerializedObject(comp);
            so.FindProperty("destination").objectReferenceValue = landing.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(chunk5.gameObject.scene);
        }

        private static void Checkpoints()
        {
            var xs = new[] { 18f, 52f, 96f, 121f, 162f };
            for (var i = 0; i < xs.Length; i++) MakeCheckpoint(xs[i], i + 1);
            MakeCheckpoint(203f, xs.Length + 1);
        }

        private static void MakeCheckpoint(float x, int order)
        {
            var chunk = ChunkPainting.ChunkAt(x, true);
            var go = new GameObject("Checkpoint " + order, typeof(BoxCollider));
            SceneManager.MoveGameObjectToScene(go, chunk.gameObject.scene);
            go.transform.SetParent(Story(chunk), false);
            go.transform.position = new Vector3(x, GroundY(x), 0f);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = go.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.5f, 4f, 6f);
            box.center = new Vector3(0f, 2f, 0f);
            var cp = go.AddComponent<Checkpoint>();
            var so = new SerializedObject(cp);
            so.FindProperty("order").intValue = order;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
        }

        // ---- Light, the cut, the prefab ------------------------------------------

        private static void Lighting()
        {
            var look = Object.FindFirstObjectByType<SceneLook>();
            if (look == null) { Report.AppendLine("no SceneLook"); return; }
            var so = new SerializedObject(look);
            var zones = so.FindProperty("brightZones");
            zones.arraySize = 1;
            var z = zones.GetArrayElementAtIndex(0);
            z.FindPropertyRelative("from").floatValue = 163f;
            z.FindPropertyRelative("to").floatValue = 199f;
            z.FindPropertyRelative("multiplier").floatValue = 1.5f;
            z.FindPropertyRelative("fade").floatValue = 6f;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(look);
            EditorSceneManager.MarkSceneDirty(look.gameObject.scene);
        }

        private static void WipeSystem()
        {
            var wipe = Object.FindFirstObjectByType<ForegroundWipe>(FindObjectsInactive.Include);
            if (wipe == null)
            {
                Transform parent = null;
                foreach (var root in SceneManager.GetSceneByName("Act0_Prototype").GetRootGameObjects())
                    if (root.name == "Systems") parent = root.transform;
                var go = new GameObject("Foreground Wipe");
                if (parent != null) go.transform.SetParent(parent, false);
                wipe = go.AddComponent<ForegroundWipe>();
            }
            var so = new SerializedObject(wipe);
            var curtains = so.FindProperty("curtains");
            curtains.arraySize = 3;
            for (var i = 0; i < 3; i++) curtains.GetArrayElementAtIndex(i).objectReferenceValue = PackSprite("Lv6 foreground_", "L6_curtain" + (i + 1));
            var bands = so.FindProperty("bands");
            var bandFiles = new[] { "L6_undergrowthBand1", "L6_undergrowthBand2", "L6_undergrowthBand4" };
            bands.arraySize = bandFiles.Length;
            for (var i = 0; i < bandFiles.Length; i++) bands.GetArrayElementAtIndex(i).objectReferenceValue = PackSprite("Lv6 foreground_", bandFiles[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(wipe.gameObject.scene);
        }

        // Act 0 has no forced death: the beetle can simply be beaten.
        private static void FixBeetlePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beetle.prefab");
            var brain = prefab.GetComponent<MiniBossBrain>();
            var so = new SerializedObject(brain);
            so.FindProperty("forcedDeath").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            Report.AppendLine("beetle forcedDeath " + so.FindProperty("forcedDeath").boolValue + " name " + so.FindProperty("bossName").stringValue);
        }

        // ---- Second pass: solid black foliage -------------------------------------

        [MenuItem("Weizza/Act 0/Rework Black Foliage")]
        public static void Rework()
        {
            Report.Clear();
            Templates.Clear();
            _rng = new System.Random(23);
            for (var x = 20f; x < 240f; x += 40f) ChunkPainting.ChunkAt(x, true);
            CacheTemplates();

            RemoveSeeThrough();
            var chunk5 = ChunkPainting.ChunkAt(180f, true);
            var chunk6 = ChunkPainting.ChunkAt(220f, true);
            RebuildRoomWalls(chunk5);
            BlackThickets(chunk5);
            TunnelCeiling();
            FixDisguise();

            Physics.SyncTransforms();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/act0_second_half.txt", Report.ToString());
            Debug.Log("[Weizza] Black foliage reworked.");
        }

        // The hanging curtains let the picture through, so they are gone: the dark has to be solid.
        private static void RemoveSeeThrough()
        {
            var doomed = new List<GameObject>();
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sr.sprite == null) continue;
                var tex = sr.sprite.texture.name;
                var x = sr.transform.position.x;
                if (tex.StartsWith("L6_curtain")) doomed.Add(sr.gameObject);
                else if (sr.gameObject.scene.name == "Forest_05" && x > 165f && x < 201f
                         && (tex == "L6_undergrowthBand1" || tex == "L6_undergrowthBand2" || tex == "L6_undergrowthBand4"))
                    doomed.Add(sr.gameObject);
            }
            foreach (var go in doomed)
            {
                var scene = go.scene;
                Object.DestroyImmediate(go);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            Report.AppendLine("removed see-through pieces " + doomed.Count);
        }

        // The walls sit inside the black, so the player has already vanished into the dark
        // by the time the room closes behind them.
        private static void RebuildRoomWalls(RoomRoot chunk)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                if (t.name == "Boss Room" || t.name == "Boss Aftermath Landing")
                {
                    var scene = t.gameObject.scene;
                    Object.DestroyImmediate(t.gameObject);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            var story = Story(chunk);
            var arena = new GameObject("Boss Room");
            SceneManager.MoveGameObjectToScene(arena, chunk.gameObject.scene);
            arena.transform.SetParent(story, false);
            arena.transform.position = Vector3.zero;
            foreach (var x in new[] { RoomLeftWallX, RoomRightWallX })
            {
                var wall = new GameObject("Wall " + x, typeof(BoxCollider));
                wall.transform.SetParent(arena.transform, false);
                wall.transform.position = new Vector3(x, 10f, 0f);
                var box = wall.GetComponent<BoxCollider>();
                box.size = new Vector3(1f, 30f, 6f);
                box.enabled = false;
            }
            arena.AddComponent<BossArena>();

            var landing = new GameObject("Boss Aftermath Landing");
            SceneManager.MoveGameObjectToScene(landing, chunk.gameObject.scene);
            landing.transform.SetParent(story, false);
            landing.transform.position = new Vector3(LandingX, GroundY(LandingX) + 0.1f, 0f);

            BossAftermath after = null;
            foreach (var a in Object.FindObjectsByType<BossAftermath>(FindObjectsInactive.Include, FindObjectsSortMode.None)) after = a;
            if (after != null)
            {
                var so = new SerializedObject(after);
                so.FindProperty("destination").objectReferenceValue = landing.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
        }

        // A thicket of the forest's own trees, ferns and bushes pulled into the foreground and
        // tinted black. Dense in the middle and thinning at the ends, so it reads as growth,
        // not a wall. The player walks through it before the room opens up.
        private static void Thicket(RoomRoot chunk, float xFrom, float xTo, float thin = 1f)
        {
            var fg = Scenery(chunk, "4 Foreground Silhouette");
            var ferns = new SpriteRenderer[7];
            for (var i = 0; i < 7; i++) ferns[i] = Tpl("L4_fernCluster" + (i + 1));
            var bands = new[] { Tpl("L6_undergrowthBand3"), Tpl("L6_undergrowthBand5") };
            var mid = (xFrom + xTo) * 0.5f;
            var half = (xTo - xFrom) * 0.5f;
            var n = 0;

            // Big dark trunks standing in front of the player, the way Neva shows its forest
            // edge: full height, near black, each a little different so they do not read as a fence.
            for (var x = xFrom; x <= xTo; x += R(1.7f, 2.9f) / thin, n++)
            {
                var tpl = Tpl("L1_TrunkFar" + (1 + _rng.Next(7)));
                var go = Foreground(tpl, chunk, fg, x, R(1.15f, 1.55f), -1.0f, R(0.015f, 0.07f), 6 + n % 6);
                go.name = "Thicket Trunk";
            }

            // Bushes and ferns around the feet, thicker toward the middle.
            for (var x = xFrom; x <= xTo; x += 0.6f)
            {
                var density = (1f - Mathf.Abs(x - mid) / half * 0.7f) * thin;
                if (_rng.NextDouble() > density) continue;
                if (_rng.NextDouble() < 0.7)
                {
                    var tpl = ferns[_rng.Next(ferns.Length)];
                    var go = Foreground(tpl, chunk, fg, x + R(-0.3f, 0.3f), R(1.5f, 2.6f), -1.0f, R(0.02f, 0.05f), 12 + _rng.Next(4));
                    go.name = "Thicket Fern";
                }
                else
                {
                    var go = Clone(bands[_rng.Next(bands.Length)], chunk, fg, x, null, R(0.85f, 1.2f), _rng.NextDouble() > 0.5);
                    if (go == null) continue;
                    go.name = "Thicket Band";
                    go.GetComponent<SpriteRenderer>().sortingOrder = 14;
                }
            }

        }

        // A background drawing pulled into the foreground layer and turned near black. Its foot is
        // placed by where it should look planted, because depth lifts a sprite on a pitched camera.
        private static GameObject Foreground(SpriteRenderer tpl, RoomRoot chunk, Transform parent, float x, float scaleMul,
            float footY, float darkness, int order)
        {
            var go = Clone(tpl, chunk, parent, x, null, 1f, _rng.NextDouble() > 0.5);
            var drift = go.GetComponent<BackgroundDrift>();
            if (drift != null) Object.DestroyImmediate(drift);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = order;
            sr.color = new Color(darkness, darkness * 1.4f, darkness * 1.2f, 1f);
            var scale = tpl.transform.localScale.x * scaleMul;
            go.transform.localScale = Vector3.one * scale;
            var z = -2.5f + R(-0.2f, 0.2f);
            var y = footY - z * 0.2756f - sr.sprite.bounds.min.y * scale;
            go.transform.position = new Vector3(x, y, z);
            return go;
        }

        private static void BlackThickets(RoomRoot chunk)
        {
            RemoveOldThickets();
            // Wide enough that the interior stays hidden until the player is walking through.
            Thicket(chunk, 164.0f, 174.0f);
            Thicket(chunk, 191.5f, 201.5f);
            Report.AppendLine("thickets placed");
        }

        // Third pass, after playing it: the stone run is back to how the artist left it, the
        // boss room is 48 m wide, and its walls stand at the edge of the dark so nobody can
        // walk into the thickets while the fight is on.
        [MenuItem("Weizza/Act 0/Widen Boss Room")]
        public static void WidenBossRoom()
        {
            Report.Clear();
            Templates.Clear();
            _rng = new System.Random(47);
            for (var x = 20f; x < 240f; x += 40f) ChunkPainting.ChunkAt(x, true);
            CacheTemplates();

            RemoveTunnelAdditions();

            var chunk5 = ChunkPainting.ChunkAt(180f, true);
            var chunk6 = ChunkPainting.ChunkAt(227f, true);
            RemoveOldThickets();
            Thicket(chunk5, 164f, 174f);
            Thicket(chunk6, 222f, 232f);
            Physics.SyncTransforms();
            RebuildRoomWalls(chunk5);

            // The boss in the middle of the long room, Soka and the last checkpoint beyond the dark.
            foreach (var brain in Object.FindObjectsByType<MiniBossBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                const float x = 198f;
                brain.transform.position = new Vector3(x, GroundY(x) + 0.2f, 0f);
                EditorSceneManager.MarkSceneDirty(brain.gameObject.scene);
                Report.AppendLine("boss at " + brain.transform.position.ToString("0.0"));
            }
            var soka = GameObject.Find("Soka");
            if (soka != null)
            {
                var sr = soka.GetComponent<SpriteRenderer>();
                var minY = float.MaxValue;
                foreach (var v in sr.sprite.vertices) minY = Mathf.Min(minY, v.y);
                const float x = 236f;
                soka.transform.position = new Vector3(x, GroundY(x) - minY * soka.transform.lossyScale.y, 0f);
                EditorSceneManager.MarkSceneDirty(soka.scene);
                Report.AppendLine("soka at " + soka.transform.position.ToString("0.0"));
            }
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name != "Checkpoint 6") continue;
                t.position = new Vector3(233f, GroundY(233f), 0f);
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }

            // Ferns and bushes all the way across the longer meadow.
            var ferns = new SpriteRenderer[7];
            for (var i = 0; i < 7; i++) ferns[i] = Tpl("L4_fernCluster" + (i + 1));
            for (var x = 199f; x < 221.5f; x += 1.1f + (float)_rng.NextDouble() * 1.3f)
            {
                var chunk = ChunkPainting.ChunkAt(x, true);
                Clone(ferns[_rng.Next(ferns.Length)], chunk, Scenery(chunk, "2 Dark Things"), x, null, R(0.9f, 1.7f), _rng.NextDouble() > 0.5);
            }

            var look = Object.FindFirstObjectByType<SceneLook>();
            if (look != null)
            {
                var so = new SerializedObject(look);
                so.FindProperty("brightZones").GetArrayElementAtIndex(0).FindPropertyRelative("to").floatValue = 224f;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(look.gameObject.scene);
            }

            Physics.SyncTransforms();
            EditorSceneManager.SaveOpenScenes();
            ColliderTable(118f, 240f);
            File.WriteAllText("Temp/act0_second_half.txt", Report.ToString());
        }

        // ---- Polish pass: the new sketch frames, save lights, thinner boss thickets ----

        private const string SketchRoot = "Assets/Mock_Asset/Sketch_Character/";

        // The new frames are imported like the cleaned idle ones, so they stand the same size
        // on the same foot point.
        private static void ImportSketchFolder(string folder)
        {
            var reference = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Mock_Asset/Sketch_Character_Clean/Idle_Sketch/In_Idle_0001.png");
            var refImporter = (TextureImporter)AssetImporter.GetAtPath("Assets/Mock_Asset/Sketch_Character_Clean/Idle_Sketch/In_Idle_0001.png");
            var settings = new TextureImporterSettings();
            refImporter.ReadTextureSettings(settings);
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SketchRoot + folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SetTextureSettings(settings);
                importer.spritePixelsPerUnit = refImporter.spritePixelsPerUnit;
                importer.spritePivot = refImporter.spritePivot;
                importer.filterMode = refImporter.filterMode;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            if (reference == null) Report.AppendLine("no reference idle frame");
        }

        [MenuItem("Weizza/Act 0/Polish Pass")]
        public static void PolishPass()
        {
            Report.Clear();
            Templates.Clear();
            _rng = new System.Random(61);
            for (var x = 20f; x < 400f; x += 40f) ChunkPainting.ChunkAt(x, true);
            CacheTemplates();

            // 1. New player frames: hurt, death, and the wake up.
            foreach (var folder in new[] { "Hurt_Sketch", "Death_Sketch", "Respawn_Sketch" }) ImportSketchFolder(folder);
            var player = Object.FindFirstObjectByType<PlayerController>();
            var animator = player.GetComponentInChildren<SpriteAnimator>();
            animator.AddClipFromFolder(SketchRoot + "Hurt_Sketch", "Hurt", 9f, false);
            animator.AddClipFromFolder(SketchRoot + "Death_Sketch", "Common_Death", 14f, false);
            // A slow wake: each of the two drawings holds a long beat.
            animator.AddClipFromFolder(SketchRoot + "Respawn_Sketch", "Common_Revive", 0.6f, false);
            EditorUtility.SetDirty(animator);
            EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);

            // 2. The opening: wake up in the two drawings, idle a moment, walk a little, then play.
            var field = typeof(CutsceneDirector).GetField("beats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var cd in Object.FindObjectsByType<CutsceneDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cd.name != "Cutscene Wake") continue;
                CutsceneBeat B(CutsceneBeatKind k) => new CutsceneBeat { kind = k };
                var beats = new List<CutsceneBeat>();
                var lockBeat = B(CutsceneBeatKind.LockInput); lockBeat.lockReason = "wake"; beats.Add(lockBeat);
                var hudOff = B(CutsceneBeatKind.HudVisible); hudOff.hudShow = false; beats.Add(hudOff);
                var barsOn = B(CutsceneBeatKind.Letterbox); barsOn.letterboxShow = true; barsOn.seconds = 0.5f; beats.Add(barsOn);
                var revive = B(CutsceneBeatKind.PlayAnimation); revive.actorId = "Player"; revive.animationName = "Common_Revive"; revive.waitForEnd = true; beats.Add(revive);
                var idle = B(CutsceneBeatKind.PlayAnimation); idle.actorId = "Player"; idle.animationName = "Idle"; idle.waitForEnd = false; beats.Add(idle);
                var rest = B(CutsceneBeatKind.Wait); rest.seconds = 1.6f; beats.Add(rest);
                var walk = B(CutsceneBeatKind.MoveActor); walk.actorId = "Player"; walk.targetStageId = "Wake_Walk"; walk.moveSpeed = 1.5f; beats.Add(walk);
                var barsOff = B(CutsceneBeatKind.Letterbox); barsOff.letterboxShow = false; barsOff.seconds = 0.5f; beats.Add(barsOff);
                beats.Add(B(CutsceneBeatKind.UnlockInput));
                var hudOn = B(CutsceneBeatKind.HudVisible); hudOn.hudShow = true; beats.Add(hudOn);
                field.SetValue(cd, beats.ToArray());
                EditorUtility.SetDirty(cd);
                EditorSceneManager.MarkSceneDirty(cd.gameObject.scene);
                Report.AppendLine("wake cutscene beats " + beats.Count);
            }
            foreach (var s in Object.FindObjectsByType<StoryKit.Bridge.StoryStageId>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s.Id != "Wake_Walk") continue;
                s.transform.position = new Vector3(5.2f, GroundY(5.2f), 0f);
                EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
            }
            var motor = player.GetComponent<CharacterController>();
            motor.enabled = false;
            player.transform.position = new Vector3(2.2f, 0.3f, 0f);
            motor.enabled = true;
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

            // 3. The beetle can simply be beaten: no forced death on the placed one either.
            foreach (var brain in Object.FindObjectsByType<MiniBossBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(brain);
                so.FindProperty("forcedDeath").boolValue = false;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(brain.gameObject.scene);
                Report.AppendLine("beetle forcedDeath " + so.FindProperty("forcedDeath").boolValue);
            }

            // 4. Lighter thickets around the boss room.
            RemoveOldThickets();
            Thicket(ChunkPainting.ChunkAt(170f, true), 164f, 174f, 0.6f);
            Thicket(ChunkPainting.ChunkAt(227f, true), 222f, 232f, 0.6f);

            // 5. Every checkpoint becomes a light to rest at.
            var saves = 0;
            foreach (var cp in Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cp.GetComponent<SavePoint>() == null) cp.gameObject.AddComponent<SavePoint>();
                EditorSceneManager.MarkSceneDirty(cp.gameObject.scene);
                saves++;
            }
            Report.AppendLine("save points " + saves);

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/act0_second_half.txt", Report.ToString());
        }

        // ---- The long walk after the boss -----------------------------------------

        // Puts a piece's foot where it should look planted, whatever its scale. Depth lifts a
        // sprite on screen (pitched camera), so the foot is measured from the drawn mesh.
        private static void Seat(GameObject go, float footApparentY)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            var p = go.transform.position;
            var foot = sr.bounds.min.y + p.z * 0.2756f;
            go.transform.position = new Vector3(p.x, p.y + footApparentY - foot, p.z);
        }

        private static RoomRoot ChunkFor(float x) => ChunkPainting.ChunkAt(Mathf.Clamp(x, 0.5f, 399.5f), true);

        // One stretch of forest. Groves cluster and clearings open between them, so it never
        // reads as trees on a grid: gaps and spacing are random, scale is random, and ferns
        // come in small clumps rather than one at a time.
        private static void Grove(float from, float to, float treeGap, float fernClumpGap, float scaleBoost)
        {
            var podNames = new[] { "L4_trunkWithPod", "L4_trunkWithPod2", "L4_trunkWithPod3" };
            var ferns = new SpriteRenderer[7];
            for (var i = 0; i < 7; i++) ferns[i] = Tpl("L4_fernCluster" + (i + 1));

            // Near trees: the main wall of the forest.
            for (var x = from + R(0f, 4f); x < to; x += R(treeGap * 0.6f, treeGap * 1.5f))
            {
                var chunk = ChunkFor(x);
                var tpl = Tpl(podNames[_rng.Next(3)]);
                var go = Clone(tpl, chunk, Scenery(chunk, "2 Dark Things"), x, null, R(0.8f, 1.5f) * scaleBoost, _rng.NextDouble() > 0.5);
                if (go != null) Seat(go, -0.35f);
            }

            // Far trees: paler, drifting slower, bigger, for depth.
            for (var x = from + R(0f, 6f); x < to; x += R(treeGap * 1.1f, treeGap * 2f))
            {
                var chunk = ChunkFor(x);
                var tpl = Tpl(podNames[_rng.Next(3)]);
                var go = Clone(tpl, chunk, Scenery(chunk, "2 Dark Things"), x, null, R(1.3f, 2.0f) * scaleBoost, _rng.NextDouble() > 0.5);
                if (go == null) continue;
                var sr = go.GetComponent<SpriteRenderer>();
                sr.color = new Color(1f, 1f, 1f, 0.5f);
                sr.sortingOrder = 18 + _rng.Next(6);
                var drift = go.GetComponent<BackgroundDrift>();
                if (drift != null) drift.SetRate(0.4f);
                go.transform.position = new Vector3(x, go.transform.position.y, 7.5f);
                Seat(go, -0.6f);
            }

            // Fern clumps at the feet of everything.
            for (var x = from + R(0f, 3f); x < to; x += R(fernClumpGap * 0.6f, fernClumpGap * 1.4f))
            {
                var count = _rng.Next(3, 7);
                for (var i = 0; i < count; i++)
                {
                    var fx = x + R(-1.8f, 1.8f);
                    var chunk = ChunkFor(fx);
                    var go = Clone(ferns[_rng.Next(ferns.Length)], chunk, Scenery(chunk, "2 Dark Things"), fx, null, R(0.8f, 1.7f), _rng.NextDouble() > 0.5);
                    if (go != null) Seat(go, -0.5f);
                }
            }
        }

        // Low stones to hop over, hanging vines and the odd fallen log, kept sparse.
        private static void Details(float from, float to)
        {
            var stones = new[] { Tpl("L4_platform_stone1"), Tpl("L4_platform_stone2") };
            for (var x = from + R(4f, 10f); x < to; x += R(16f, 30f))
            {
                var chunk = ChunkFor(x);
                Clone(stones[_rng.Next(2)], chunk, Level(chunk), x, null, R(0.55f, 0.72f), _rng.NextDouble() > 0.5);
            }
            var vines = new[] { Tpl("ภาพวาด 20261010(1)"), Tpl("ภาพวาด 20261010") };
            for (var x = from + R(3f, 9f); x < to; x += R(12f, 24f))
            {
                var chunk = ChunkFor(x);
                Clone(vines[_rng.Next(2)], chunk, Scenery(chunk, "5 Vines"), x);
            }
            var log = Tpl("L4_fallwood");
            for (var x = from + R(8f, 20f); x < to; x += R(35f, 60f))
            {
                var chunk = ChunkFor(x);
                var go = Clone(log, chunk, Scenery(chunk, "2 Dark Things"), x, null, R(0.8f, 1.2f), _rng.NextDouble() > 0.5);
                if (go != null) Seat(go, -0.5f);
            }
        }

        [MenuItem("Weizza/Act 0/Build Long Walk")]
        public static void BuildLongWalk()
        {
            Report.Clear();
            Templates.Clear();
            _rng = new System.Random(77);
            for (var x = 20f; x < 240f; x += 40f) ChunkPainting.ChunkAt(x, true);
            CacheTemplates();

            // A rerun starts these four chunks empty, so nothing is placed twice.
            for (var n = 7; n <= 10; n++)
            {
                var fresh = EnsureChunk(n, 160f + (n - 5) * 40f);
                for (var i = fresh.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(fresh.transform.GetChild(i).gameObject);
            }

            // No wipe after the boss: the walls open and the player walks on.
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                if (t.name == "Boss Aftermath" || t.name == "Boss Aftermath Landing" || t.name == "End Wall")
                {
                    var scene = t.gameObject.scene;
                    Object.DestroyImmediate(t.gameObject);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            // Ground right to the end, and past it, because the zoomed camera sees beyond the last chunk.
            var groundTpl = Tpl("L51_ground_flat1920x512");
            for (var x = 247.3f; x < 430f; x += 21.33f) Clone(groundTpl, ChunkFor(Mathf.Min(x, 399f)), Level(ChunkFor(Mathf.Min(x, 399f))), x);
            Physics.SyncTransforms();

            // Far layers for the new chunks, scaled up so they reach the top of a zoomed frame.
            for (var n = 7; n <= 10; n++)
            {
                var chunk = ChunkFor(160f + (n - 5) * 40f + 20f);
                var xMin = 160f + (n - 5) * 40f;
                var far = Scenery(chunk, "1 Far Fog");
                Clone(Tpl("L0_Voidpng"), chunk, far, xMin + 10f);
                Clone(Tpl("L0_Voidpng"), chunk, far, xMin + 30f);
                Clone(Tpl("L1_MistBand"), chunk, far, xMin + 20f);
                for (var i = 0; i < 4; i++)
                {
                    var trunk = Clone(Tpl("L1_TrunkFar" + (1 + (n * 3 + i) % 7)), chunk, far, xMin + 3.7f + i * 11.25f, null, R(1.4f, 1.8f));
                    if (trunk != null) Seat(trunk, -2.2f);
                }
            }

            // Four stretches, each with its own feel.
            Grove(240f, 282f, 9f, 11f, 1.0f);   // first grove, gentle
            Details(240f, 282f);
            Grove(282f, 322f, 18f, 7f, 0.9f);   // open clearing, fewer trees, lots of ferns
            Details(282f, 322f);
            Grove(322f, 362f, 6.5f, 13f, 1.2f); // deep grove, close trees
            Details(322f, 362f);
            Grove(362f, 399f, 11f, 9f, 1.0f);   // sunlit glade to the end
            Details(362f, 392f);

            // Dark thickets to walk through between the stretches.
            var c = ChunkFor(280f); Thicket(c, 276f, 286f);
            c = ChunkFor(320f); Thicket(c, 316f, 326f);
            c = ChunkFor(360f); Thicket(c, 356f, 366f);

            // A few light fights and the sky over the glade.
            Spawn("Assets/Prefabs/Enemies/AntRider.prefab", 262f, "AntRider");
            Spawn("Assets/Prefabs/Enemies/BombFly.prefab", 272f, "Firefly");
            Spawn("Assets/Prefabs/Enemies/AntRider.prefab", 302f, "AntRider");
            Spawn("Assets/Prefabs/Enemies/BombFly.prefab", 338f, "Firefly");
            Spawn("Assets/Prefabs/Enemies/BombFly.prefab", 343f, "Firefly");
            Spawn("Assets/Prefabs/Enemies/AntRider.prefab", 348f, "AntRider");

            // Save points all the way along.
            var cps = new[] { 244f, 279f, 318f, 352f, 380f };
            for (var i = 0; i < cps.Length; i++) MakeCheckpoint(cps[i], 7 + i);

            // Soka waits at the end, and the ending plays when she has finished talking.
            var soka = FindSokaAfterBoss();
            if (soka != null)
            {
                var chunk = ChunkFor(388f);
                soka.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(soka, chunk.gameObject.scene);
                soka.transform.SetParent(Story(chunk), true);
                var sr = soka.GetComponent<SpriteRenderer>();
                var minY = float.MaxValue;
                foreach (var v in sr.sprite.vertices) minY = Mathf.Min(minY, v.y);
                const float sx = 388f;
                soka.transform.position = new Vector3(sx, GroundY(sx) - minY * soka.transform.lossyScale.y, 0f);
                if (soka.GetComponent<EndingCutscene>() == null) soka.AddComponent<EndingCutscene>();
                EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                Report.AppendLine("Soka at " + soka.transform.position.ToString("0.0"));
            }
            var wallChunk = ChunkFor(399f);
            var wall = new GameObject("End Wall", typeof(BoxCollider));
            SceneManager.MoveGameObjectToScene(wall, wallChunk.gameObject.scene);
            wall.transform.SetParent(Level(wallChunk), false);
            wall.transform.position = new Vector3(396f, 10f, 0f);
            wall.GetComponent<BoxCollider>().size = new Vector3(1f, 30f, 6f);

            // Zoom out after the thicket behind the boss, and light for each stretch.
            Core();

            Physics.SyncTransforms();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            ColliderTable(236f, 400f);
            File.WriteAllText("Temp/act0_second_half.txt", Report.ToString());
        }

        private static GameObject FindSokaAfterBoss()
        {
            GameObject found = null;
            foreach (var n in Object.FindObjectsByType<Narrative.NpcInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (n.name == "Soka" && (found == null || n.transform.position.x > found.transform.position.x)) found = n.gameObject;
            return found;
        }

        private static void Core()
        {
            Transform systems = null;
            foreach (var root in SceneManager.GetSceneByName("Act0_Prototype").GetRootGameObjects())
                if (root.name == "Systems") systems = root.transform;
            var zone = Object.FindFirstObjectByType<CameraZoomZone>(FindObjectsInactive.Include);
            if (zone == null)
            {
                var go = new GameObject("Camera Zoom Zone");
                if (systems != null) go.transform.SetParent(systems, false);
                zone = go.AddComponent<CameraZoomZone>();
            }
            var so = new SerializedObject(zone);
            var zones = so.FindProperty("zones");
            zones.arraySize = 1;
            var z = zones.GetArrayElementAtIndex(0);
            z.FindPropertyRelative("from").floatValue = 252f;
            z.FindPropertyRelative("to").floatValue = 410f;
            z.FindPropertyRelative("size").floatValue = 9.5f;
            z.FindPropertyRelative("ease").floatValue = 14f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);

            var look = Object.FindFirstObjectByType<SceneLook>();
            if (look == null) return;
            var lso = new SerializedObject(look);
            var bright = lso.FindProperty("brightZones");
            // Keep the meadow zone, add: clearing brighter, deep grove darker, glade brighter.
            var data = new (float from, float to, float mul, float fade)[]
            {
                (163f, 224f, 1.5f, 6f), (288f, 316f, 1.25f, 12f), (326f, 352f, 0.8f, 10f), (368f, 404f, 1.35f, 14f),
            };
            bright.arraySize = data.Length;
            for (var i = 0; i < data.Length; i++)
            {
                var e = bright.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("from").floatValue = data[i].from;
                e.FindPropertyRelative("to").floatValue = data[i].to;
                e.FindPropertyRelative("multiplier").floatValue = data[i].mul;
                e.FindPropertyRelative("fade").floatValue = data[i].fade;
            }
            lso.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(look.gameObject.scene);
        }

        // The order Songwut wants: ants, fireflies, Soka, ants and fireflies together, the boss
        // room, the boss, Soka again. Soka stands in two places, one conversation each.
        [MenuItem("Weizza/Act 0/Fix Flow Order")]
        public static void FixFlow()
        {
            Report.Clear();
            Templates.Clear();
            _rng = new System.Random(5);
            for (var x = 20f; x < 240f; x += 40f) ChunkPainting.ChunkAt(x, true);

            // Everything placed in the second half goes; the first two ants (x 78, 88) stay.
            var doomed = new List<GameObject>();
            foreach (var b in Object.FindObjectsByType<BombFlyBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.transform.position.x >= 99f) doomed.Add(b.gameObject);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (e.name == "AntRider" && e.transform.position.x >= 99f) doomed.Add(e.gameObject);
            foreach (var go in doomed)
            {
                var scene = go.scene;
                Object.DestroyImmediate(go);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            Physics.SyncTransforms();

            const string ant = "Assets/Prefabs/Enemies/AntRider.prefab";
            const string fly = "Assets/Prefabs/Enemies/BombFly.prefab";
            Spawn(fly, 102f, "Firefly");
            Spawn(fly, 107f, "Firefly");
            // Soka is placed between them and the next fight (below).
            Spawn(ant, 121f, "AntRider");
            Spawn(fly, 131f, "Firefly");
            Spawn(fly, 141f, "Firefly");
            Spawn(fly, 151f, "Firefly");
            Spawn(ant, 160.5f, "AntRider");

            var soka = GameObject.Find("Soka");
            if (soka != null)
            {
                // The one waiting after the boss keeps the second conversation.
                SetConversation(soka, "DLG_ACT0_SOKA_AGAIN");
                var first = Object.Instantiate(soka);
                first.name = "Soka";
                first.transform.SetParent(null, true);
                var chunk = ChunkPainting.ChunkAt(114f, true);
                SceneManager.MoveGameObjectToScene(first, chunk.gameObject.scene);
                first.transform.SetParent(Story(chunk), true);
                var sr = first.GetComponent<SpriteRenderer>();
                var minY = float.MaxValue;
                foreach (var v in sr.sprite.vertices) minY = Mathf.Min(minY, v.y);
                const float x = 114f;
                first.transform.position = new Vector3(x, GroundY(x) - minY * first.transform.lossyScale.y, 0f);
                SetConversation(first, "DLG_ACT0_SOKA_INTRO");
                EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                Report.AppendLine("first Soka at " + first.transform.position.ToString("0.0"));
            }

            // The beetle wakes well before the player reaches it, so the long room does not
            // let them stroll past a clump of ferns.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beetle.prefab");
            var so = new SerializedObject(prefab.GetComponent<MiniBossBrain>());
            so.FindProperty("wakeRange").floatValue = 14f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/act0_second_half.txt", Report.ToString());
        }

        private static void SetConversation(GameObject soka, string asset)
        {
            var npc = soka.GetComponent<Narrative.NpcInteractable>();
            var so = new SerializedObject(npc);
            so.FindProperty("conversation").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Narrative.Conversation>("Assets/GameData/Conversations/" + asset + ".asset");
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(soka);
            EditorSceneManager.MarkSceneDirty(soka.scene);
        }

        // Back to the foreground the artist painted: only the pieces that were in the chunks
        // before the second half was built stay in the stone run.
        private static void RemoveTunnelAdditions()
        {
            var keep = new[] { 141.2f, 149.8f, 122.6f, 137.7f, 145.0f, 149.1f };
            var doomed = new List<GameObject>();
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sr.sprite == null) continue;
                var parent = sr.transform.parent != null ? sr.transform.parent.name : "";
                if (parent != "4 Foreground Silhouette" && parent != "5 Vines") continue;
                var x = sr.transform.position.x;
                if (x < 121f || x > 160.5f) continue;
                if (sr.name.StartsWith("Thicket")) continue;
                var original = false;
                foreach (var k in keep) if (Mathf.Abs(x - k) < 0.4f) original = true;
                if (!original) doomed.Add(sr.gameObject);
            }
            foreach (var go in doomed)
            {
                var scene = go.scene;
                Object.DestroyImmediate(go);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            Report.AppendLine("removed added tunnel foreground " + doomed.Count);
        }

        [MenuItem("Weizza/Act 0/Redo Boss Room Thickets")]
        public static void RedoThickets()
        {
            Report.Clear();
            Templates.Clear();
            _rng = new System.Random(31);
            for (var x = 20f; x < 240f; x += 40f) ChunkPainting.ChunkAt(x, true);
            CacheTemplates();
            BlackThickets(ChunkPainting.ChunkAt(180f, true));
            EditorSceneManager.SaveOpenScenes();
            File.WriteAllText("Temp/act0_second_half.txt", Report.ToString());
        }

        // Clears the first attempt (canopy strips and fills) before laying the thickets again.
        private static void RemoveOldThickets()
        {
            var doomed = new List<GameObject>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                var sceneName = t.gameObject.scene.name;
                if (sceneName != "Forest_05" && sceneName != "Forest_06") continue;
                var x = t.position.x;
                if (x < 160f || x > 240f) continue;
                var sr = t.GetComponent<SpriteRenderer>();
                if (t.name == "Black Fill" || t.name.StartsWith("Thicket")) doomed.Add(t.gameObject);
                else if (sr != null && sr.sprite != null && sr.sprite.texture.name == "L6_canopy" && Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.z, 0f)) > 45f)
                    doomed.Add(t.gameObject);
            }
            foreach (var go in doomed)
            {
                var scene = go.scene;
                Object.DestroyImmediate(go);
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        // More shade over the stone run, from solid canopy and leafy bands only.
        private static void TunnelCeiling()
        {
            var canopy = Tpl("L6_canopy");
            var band3 = Tpl("L6_undergrowthBand3");
            var band5 = Tpl("L6_undergrowthBand5");
            foreach (var x in new[] { 130f, 146f })
            {
                var chunk = ChunkPainting.ChunkAt(x, true);
                // Hung lower than the other canopies, so the top of the frame is shut.
                Clone(canopy, chunk, Scenery(chunk, "4 Foreground Silhouette"), x, 5.6f);
            }
            var i = 0;
            for (var x = 123.5f; x < 159f; x += 3.1f, i++)
            {
                var chunk = ChunkPainting.ChunkAt(x, true);
                Clone(i % 2 == 0 ? band5 : band3, chunk, Scenery(chunk, "4 Foreground Silhouette"), x, null, R(1.25f, 1.7f), i % 2 == 0);
            }
            Report.AppendLine("tunnel ceiling placed");
        }

        // The beetle hides in the forest pack's own ferns now, swaying with the wind.
        private static void FixDisguise()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beetle.prefab");
            var disguise = prefab.GetComponent<FlowerDisguise>();
            if (disguise == null) { Report.AppendLine("no FlowerDisguise on Beetle"); return; }
            disguise.LoadBushes();
            var so = new SerializedObject(disguise);
            so.FindProperty("plantScale").vector2Value = new Vector2(0.5f, 0.85f);
            so.FindProperty("plantCount").intValue = 10;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            Report.AppendLine("disguise bushes " + so.FindProperty("bushes").arraySize + " sway " + (so.FindProperty("swayMaterial").objectReferenceValue != null));
        }

        // ---- Report ---------------------------------------------------------------

        private static void ColliderTable(float from, float to)
        {
            var rows = new List<(float min, string text)>();
            foreach (var c in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.isTrigger || c.name.StartsWith("L51")) continue;
                var b = c.bounds;
                if (b.max.x < from || b.min.x > to) continue;
                rows.Add((b.min.x, $"{c.name} x {b.min.x:0.0}..{b.max.x:0.0} top {b.max.y:0.00}"));
            }
            rows.Sort((a, b) => a.min.CompareTo(b.min));
            Report.AppendLine("--- colliders");
            foreach (var r in rows) Report.AppendLine(r.text);
        }
    }
}
