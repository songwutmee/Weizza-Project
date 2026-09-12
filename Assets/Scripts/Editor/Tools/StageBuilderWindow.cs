using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds the stage a side scroller stands on: the lanes, the floor under
    /// each one, and the stack of backdrop layers behind them.
    ///
    /// The floor is the part that cannot be skipped. A lane with no collider
    /// under it drops the player through the world the first time they shift
    /// onto it, and because the shift itself is instant the fall reads as the
    /// shift being broken rather than as the level being unfinished.
    ///
    /// The backdrop is the part that is easy to do badly. Depth in a flat game
    /// comes from three things happening at once, and any one of them alone
    /// looks like a sticker: layers drifting at different rates, layers losing
    /// contrast the further back they are, and something in front of the player
    /// that is darker than everything else. This builds all three, so a scene
    /// starts out reading as a place rather than as a painted wall.
    /// </summary>
    public sealed class StageBuilderWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Stage Builder", priority = 4)]
        private static void Open()
        {
            var window = GetWindow<StageBuilderWindow>();
            window.titleContent = new GUIContent("Stage Builder");
            window.minSize = new Vector2(480f, 640f);
            window.Show();
        }

        // ---- Lanes ----------------------------------------------------------

        [TitleGroup("Lanes", "How many planes the level is played on", TitleAlignments.Centered)]
        [InfoBox("One lane is an ordinary side scroller. Two gives the In stance " +
                 "somewhere to blink to, and is where the mechanic starts paying " +
                 "for itself. Three is playable but hard to read at a glance.")]
        [PropertyRange(1, 3)]
        [SerializeField] private int lanes = 1;

        [TitleGroup("Lanes")]
        [PropertyRange(1f, 5f)]
        [Tooltip("Metres between lanes. Wider is easier to read and harder to " +
                 "fit a backdrop behind.")]
        [SerializeField] private float laneSpacing = 2.5f;

        [TitleGroup("Lanes")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Reads as")]
        private string LaneVerdict
        {
            get
            {
                if (lanes <= 1) return "a flat side scroller, nothing to shift between";

                var rig = FindAnyObjectByType<ViewRig>();
                var pitch = rig != null ? rig.PitchDegrees : ViewRig.DefaultPitch;
                var ppu = rig != null ? rig.PixelsPerUnit : 150f;

                if (pitch < 1f)
                {
                    return "nothing: the View Rig pitch is flat, so both lanes draw in the same place";
                }

                var pixels = laneSpacing * Mathf.Sin(pitch * Mathf.Deg2Rad) * ppu;
                return pixels < 20f
                    ? $"{pixels:0} px apart, too close to read, raise the pitch or the spacing"
                    : $"{pixels:0} px apart, clear enough to read";
            }
        }

        // ---- Floor ----------------------------------------------------------

        [TitleGroup("Floor", "What holds each lane up", TitleAlignments.Centered)]
        [PropertyRange(10f, 200f)]
        [SerializeField] private float floorLength = 60f;

        [TitleGroup("Floor")]
        [PropertyRange(0.5f, 4f)]
        [InfoBox("At a pitch of zero the camera looks straight at the front face, " +
                 "so this depth is never on screen: one metre and one centimetre " +
                 "look identical. It is here for footing only. Taken near zero the " +
                 "capsule barely overlaps the slab and a fast fall tunnels through " +
                 "it, so leave it around a metre and spend the thinness on the art " +
                 "instead.", InfoMessageType.Warning)]
        [SerializeField] private float floorWidth = 1f;

        [TitleGroup("Floor")]
        [Tooltip("The floor art. Tiled across the whole length, so one seamless " +
                 "strip is enough. Leaving it empty keeps the grey blockout slab.")]
        [PreviewField(58f, ObjectFieldAlignment.Left)]
        [SerializeField] private Sprite floorSprite;

        [TitleGroup("Floor")]
        [PropertyRange(0.2f, 4f)]
        [ShowIf("@floorSprite != null")]
        [Tooltip("How tall the band of floor art stands, in metres. Its top edge " +
                 "is the surface the player walks on.")]
        [SerializeField] private float floorArtHeight = 1f;

        [TitleGroup("Floor")]
        [ShowIf("@floorSprite != null")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Art is")]
        private string ArtReadout => floorSprite == null
            ? string.Empty
            : $"{floorSprite.bounds.size.y:0.00} m tall as drawn, scaled to {floorArtHeight:0.00} m here";

        [TitleGroup("Floor")]
        [HideIf("@floorSprite != null")]
        [Tooltip("Off leaves the collider invisible, for a level whose floor is " +
                 "painted by the art team. On draws a grey slab, which is what a " +
                 "blockout wants.")]
        [SerializeField] private bool visibleWhileBlockingOut = true;

        // ---- Backdrop -------------------------------------------------------

        [TitleGroup("Backdrop", "The stack that sells the distance", TitleAlignments.Centered)]
        [InfoBox("Each slot is one band of distance. Drop the art in afterwards: " +
                 "the slot already carries the drift rate and the wash, so a " +
                 "sprite dropped into Sky behaves like something far away without " +
                 "anyone tuning it.")]
        [PropertyRange(2, 6)]
        [SerializeField] private int backdropLayers = 4;

        [TitleGroup("Backdrop")]
        [Tooltip("Colour the distance washes towards. The sky, the fog, whatever " +
                 "the air in this place is made of. Neva leans on this harder " +
                 "than on anything else, and it is most of why it reads as deep.")]
        [SerializeField] private Color hazeColour = new(0.42f, 0.62f, 0.68f);

        [TitleGroup("Backdrop")]
        [PropertyRange(0f, 1f)]
        [Tooltip("How much of the haze the furthest layer takes. Zero keeps every " +
                 "layer at full contrast, which is the flat sticker look.")]
        [SerializeField] private float hazeStrength = 0.72f;

        [TitleGroup("Backdrop")]
        [Tooltip("A layer in front of the player, drifting the wrong way and " +
                 "darkened towards silhouette. One of these does more for depth " +
                 "than three more layers behind.")]
        [SerializeField] private bool addForegroundSilhouette = true;

        [TitleGroup("Backdrop")]
        [PropertyRange(0f, 1.5f)]
        [Tooltip("How far below the floor line the sit button buries each layer. " +
                 "A little overlap hides the hairline gap that shows up the moment " +
                 "the camera moves half a pixel.")]
        [SerializeField] private float sinkBelowFloor = 0.15f;

        // ---- Build ----------------------------------------------------------

        [PropertyOrder(10)]
        [Button("Build the stage", ButtonSizes.Gigantic)]
        [GUIColor(0.45f, 0.78f, 0.55f)]
        private void Build()
        {
            var root = Root("Stage");

            BuildLanes(root);
            BuildBackdrop(root);
            PushLanesToViewRig();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
        }

        [PropertyOrder(11)]
        [Button("Only re-apply the wash and the drift", ButtonSizes.Large)]
        [GUIColor(0.55f, 0.72f, 0.95f)]
        [Tooltip("Leaves the hierarchy and the art alone, and re-derives every " +
                 "rate and tint from the numbers above. Safe to press after " +
                 "dropping new art into the slots.")]
        private void Restyle()
        {
            var stage = GameObject.Find("Stage");
            if (stage == null)
            {
                Debug.LogWarning("Stage Builder: no Stage in this scene yet, build one first.");
                return;
            }

            var backdrop = stage.transform.Find("Backdrop");
            if (backdrop == null) return;

            var index = 0;
            var count = Mathf.Max(1, backdrop.childCount);
            foreach (Transform layer in backdrop)
            {
                StyleLayer(layer.gameObject, index, count);
                index++;
            }

            EditorSceneManager.MarkSceneDirty(stage.scene);
        }

        [PropertyOrder(12)]
        [Button("Sit every layer on the floor line", ButtonSizes.Large)]
        [GUIColor(0.9f, 0.72f, 0.4f)]
        [Tooltip("Measures what each layer actually draws and drops it until its " +
                 "bottom edge meets the floor. Only touches Y.")]
        private void SitOnFloor()
        {
            var stage = GameObject.Find("Stage");
            if (stage == null)
            {
                Debug.LogWarning("Stage Builder: no Stage in this scene yet, build one first.");
                return;
            }

            var moved = 0;
            var empty = 0;

            foreach (var layer in Layers(stage.transform))
            {
                // Measured from what is drawn, not from the transform. A layer's
                // own position says nothing about where its art sits: a strip
                // whose pivot is its centre and one whose pivot is its bottom
                // edge need opposite corrections from the same Y.
                var renderers = layer.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    empty++;
                    continue;
                }

                var drawn = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) drawn.Encapsulate(renderers[i].bounds);

                var drop = FloorTopY - sinkBelowFloor - drawn.min.y;
                if (Mathf.Abs(drop) < 0.001f) continue;

                Undo.RecordObject(layer, "Sit layers on floor");
                layer.position += new Vector3(0f, drop, 0f);
                moved++;
            }

            EditorSceneManager.MarkSceneDirty(stage.scene);

            var note = $"Stage Builder: sat {moved} layer(s) on the floor line.";
            if (empty > 0) note += $" {empty} slot(s) hold no art yet, so they were left alone.";
            Debug.Log(note);
        }

        /// <summary>
        /// Every group whose art should meet the floor.
        ///
        /// Both the slots this tool made and whatever was dragged in beside them,
        /// because a scene that predates the tool keeps its old backdrop root and
        /// there is no reason a button called sit on the floor should ignore the
        /// one layer the level actually uses.
        /// </summary>
        private static IEnumerable<Transform> Layers(Transform stage)
        {
            foreach (Transform child in stage)
            {
                if (child.name == "Lanes") continue;

                if (child.name == "Backdrop")
                {
                    foreach (Transform slot in child) yield return slot;
                    continue;
                }

                yield return child;
            }
        }

        /// <summary>Height of the surface the player walks on.</summary>
        private const float FloorTopY = 0f;

        // ---- Pieces ---------------------------------------------------------

        private void BuildLanes(GameObject root)
        {
            var group = Child(root.transform, "Lanes");

            for (var lane = 0; lane < lanes; lane++)
            {
                var holder = Child(group.transform, $"Lane {lane}");
                holder.transform.position = new Vector3(0f, 0f, lane * laneSpacing);

                var floor = Child(holder.transform, "Floor");
                floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                floor.transform.localScale = new Vector3(floorLength, 1f, floorWidth);

                if (floor.GetComponent<BoxCollider>() == null) floor.AddComponent<BoxCollider>();

                var filter = floor.GetComponent<MeshFilter>();
                var renderer = floor.GetComponent<MeshRenderer>();
                var showSlab = floorSprite == null && visibleWhileBlockingOut;

                if (showSlab)
                {
                    if (filter == null) filter = floor.AddComponent<MeshFilter>();
                    if (renderer == null) renderer = floor.AddComponent<MeshRenderer>();
                    filter.sharedMesh = CubeMesh();

                    // The back lane is drawn dimmer so a glance at the game view
                    // answers which lane something is on, before any art exists
                    // to answer it properly.
                    var shade = lanes <= 1 ? 0f : lane / (float)(lanes - 1);
                    renderer.sharedMaterial = BlockoutMaterial(shade);
                }
                else if (renderer != null)
                {
                    renderer.enabled = false;
                }

                DressFloor(holder.transform, lane);
                Undo.RegisterCreatedObjectUndo(holder, "Build stage");
            }

            // Dropping back to fewer lanes has to take the old ones away. A
            // leftover floor nobody can reach is invisible in the game view and
            // catches the player the first time they fall past it.
            for (var stale = lanes; ; stale++)
            {
                var leftover = group.transform.Find($"Lane {stale}");
                if (leftover == null) break;
                Undo.DestroyObjectImmediate(leftover.gameObject);
            }
        }

        /// <summary>
        /// Puts the floor art in front of the collider that does the real work.
        ///
        /// It is a sibling of the slab rather than a child of it because the slab
        /// is scaled to sixty by one by one, and anything parented to that
        /// inherits the stretch: a floor sprite would come out sixty times wider
        /// than it was drawn. Kept unscaled beside it, the sprite tiles at the
        /// size the artist drew it.
        /// </summary>
        private void DressFloor(Transform lane, int index)
        {
            var existing = lane.Find("Floor Art");

            if (floorSprite == null)
            {
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
                return;
            }

            var art = existing != null ? existing.gameObject : new GameObject("Floor Art");
            art.transform.SetParent(lane, false);

            // Scaled to the height that was asked for, not cropped to it. Tiled
            // draw mode fills a rectangle by repeating the sprite at the size it
            // was imported, so a rectangle shorter than the art shows a slice of
            // the picture rather than the whole thing shrunk, and a backdrop tile
            // used as a floor comes out as one flat band of its bottom colour.
            var drawn = Mathf.Max(0.0001f, floorSprite.bounds.size.y);
            var scale = floorArtHeight / drawn;

            // The walking surface is the top of the slab, at local zero, so the
            // band hangs below that rather than straddling it.
            art.transform.localPosition = new Vector3(0f, -floorArtHeight * 0.5f, 0f);
            art.transform.localScale = new Vector3(scale, scale, 1f);

            var sprite = art.GetComponent<SpriteRenderer>();
            if (sprite == null) sprite = art.AddComponent<SpriteRenderer>();

            sprite.sprite = floorSprite;
            sprite.drawMode = SpriteDrawMode.Tiled;
            sprite.tileMode = SpriteTileMode.Continuous;
            sprite.size = new Vector2(floorLength / scale, drawn);

            // Behind everything that stands on it. The characters in this game
            // sit between zero and twenty, and a floor drawn above them eats
            // their feet, which reads as the sprites being mis-pivoted rather
            // than as the floor being in front. A nearer lane still beats a
            // further one, one step at a time.
            sprite.sortingLayerName = "Default";
            sprite.sortingOrder = -20 - index;

            if (floorSprite.packed || floorSprite.texture == null) return;

            // Tiled draw mode has two import settings it will not tell you about.
            // Without Repeat the strip smears into one stretched tile, and with
            // the default Tight mesh it refuses to tile at all and logs a warning
            // that reads like a sprite problem rather than an import one.
            var path = AssetDatabase.GetAssetPath(floorSprite);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            var needsRepeat = importer.wrapMode != TextureWrapMode.Repeat;
            var needsFullRect = settings.spriteMeshType != SpriteMeshType.FullRect;
            if (!needsRepeat && !needsFullRect) return;

            importer.wrapMode = TextureWrapMode.Repeat;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private void BuildBackdrop(GameObject root)
        {
            var group = Child(root.transform, "Backdrop");

            for (var i = 0; i < backdropLayers; i++)
            {
                var layer = Child(group.transform, LayerName(i, backdropLayers));
                StyleLayer(layer, i, backdropLayers);
                Undo.RegisterCreatedObjectUndo(layer, "Build stage");
            }

            if (!addForegroundSilhouette) return;

            var front = Child(root.transform, "Foreground Silhouette");
            var parallax = front.GetComponent<ParallaxLayer>();
            if (parallax == null) parallax = front.AddComponent<ParallaxLayer>();

            // In front of the gameplay plane, so it sits between the camera and
            // lane zero, and it drifts the wrong way on purpose.
            front.transform.position = new Vector3(0f, 0f, -3f);
            SetParallax(parallax, horizontal: -0.35f, vertical: -0.12f);
            Undo.RegisterCreatedObjectUndo(front, "Build stage");
        }

        /// <summary>
        /// Gives one backdrop slot its drift and its wash.
        ///
        /// Both are derived from how far down the stack the slot sits rather
        /// than typed in per layer, because the thing that has to stay true is
        /// the ordering: every layer further back must drift slower and carry
        /// more haze than the one in front of it. Hand typed numbers stop
        /// obeying that the first time a layer is inserted in the middle.
        /// </summary>
        private void StyleLayer(GameObject layer, int index, int count)
        {
            var t = count <= 1 ? 1f : index / (float)(count - 1);

            var parallax = layer.GetComponent<ParallaxLayer>();
            if (parallax == null) parallax = layer.AddComponent<ParallaxLayer>();

            // Nearest band drifts almost with the world, furthest is nearly
            // glued to the camera.
            SetParallax(parallax,
                horizontal: Mathf.Lerp(0.2f, 0.92f, t),
                vertical: Mathf.Lerp(0.08f, 0.45f, t));

            // Behind every lane, spread out so the sorting is unambiguous.
            var behindLanes = lanes * laneSpacing;
            layer.transform.position = new Vector3(0f, 0f, behindLanes + 2f + index * 3f);

            var wash = Color.Lerp(Color.white, hazeColour, t * hazeStrength);
            foreach (var sprite in layer.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sprite.color = wash;
            }
        }

        private void PushLanesToViewRig()
        {
            var rig = FindAnyObjectByType<ViewRig>();
            if (rig == null)
            {
                Debug.LogWarning("Stage Builder: no View Rig in this scene, so the lane " +
                                 "count was not set. Scene Doctor can add one.");
                return;
            }

            var so = new SerializedObject(rig);
            so.FindProperty("laneCount").intValue = lanes;
            so.FindProperty("laneSpacing").floatValue = laneSpacing;
            so.ApplyModifiedProperties();

            rig.Apply();
        }

        // ---- Plumbing -------------------------------------------------------

        private static void SetParallax(ParallaxLayer parallax, float horizontal, float vertical)
        {
            var so = new SerializedObject(parallax);

            // Manual, not FromDistance. A derived rate reads the layer's Z, and
            // Z here only means sorting order, so the rate would change every
            // time a layer was nudged back to fix an overlap.
            so.FindProperty("mode").enumValueIndex = (int)ParallaxLayer.Mode.Manual;
            so.FindProperty("horizontalRate").floatValue = horizontal;
            so.FindProperty("verticalRate").floatValue = vertical;
            so.ApplyModifiedProperties();
        }

        private static string LayerName(int index, int count)
        {
            if (index == 0) return "Near";
            if (index == count - 1) return "Sky";
            return index == 1 ? "Mid" : $"Far {index}";
        }

        private static Material BlockoutMaterial(float shade)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader)
            {
                name = shade <= 0f ? "Blockout Front" : "Blockout Back",
                color = Color.Lerp(new Color(0.62f, 0.63f, 0.66f), new Color(0.3f, 0.33f, 0.4f), shade),
            };
        }

        private static Mesh _cube;

        private static Mesh CubeMesh()
        {
            if (_cube != null) return _cube;

            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube = probe.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(probe);
            return _cube;
        }

        private static GameObject Root(string name)
        {
            var existing = GameObject.Find(name);
            if (existing != null) return existing;

            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Build stage");
            return created;
        }

        private static GameObject Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.gameObject;

            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created;
        }
    }
}
