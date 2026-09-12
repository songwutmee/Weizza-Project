using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using StoryKit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;
using UnityEngine.UI;
using Weizza.Narrative;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds the whole talking rig in one press: the panel, the blur, the
    /// service objects, and the assets the sheet import writes into.
    ///
    /// Assembling this by hand is about forty clicks across six inspectors, and
    /// the failure mode is not a compile error. A panel with one reference left
    /// empty opens, shows nothing, and waits forever for a press it never reads,
    /// which looks exactly like a bug in the dialogue code. Building it from one
    /// place means the wiring is either all there or the tool says so.
    /// </summary>
    public sealed class DialogueRigWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Dialogue Rig", priority = 6)]
        private static void Open()
        {
            var window = GetWindow<DialogueRigWindow>();
            window.titleContent = new GUIContent("Dialogue Rig");
            window.minSize = new Vector2(480f, 520f);
            window.Show();
        }

        [TitleGroup("Look", "The panel across the bottom", TitleAlignments.Centered)]
        [SerializeField] private Color panelColour = new(0.36f, 0.07f, 0.55f, 0.94f);

        [TitleGroup("Look")]
        [SerializeField] private Color frameColour = new(1f, 0.35f, 0.72f);

        [TitleGroup("Look")]
        [SerializeField] private Color textColour = new(1f, 0.83f, 0.2f);

        [TitleGroup("Look")]
        [PropertyRange(180f, 420f)]
        [SerializeField] private float panelHeight = 260f;

        [TitleGroup("Look")]
        [PropertyRange(0f, 4f)]
        [Tooltip("How hard the world goes soft behind the panel. Tunable " +
                 "afterwards on the volume itself.")]
        [SerializeField] private float blurStrength = 1.6f;

        [TitleGroup("Assets", "Where the sheet import writes", TitleAlignments.Centered)]
        [FolderPath]
        [SerializeField] private string assetFolder = "Assets/GameData/Narrative";

        [PropertyOrder(10)]
        [Button("Build the dialogue rig", ButtonSizes.Gigantic)]
        [GUIColor(0.45f, 0.78f, 0.55f)]
        private void Build()
        {
            _lookedForFont = false;

            var table = EnsureAsset<LocalizationTableAsset>("LocalizationTable");
            var speakers = EnsureAsset<SpeakerDatabase>("SpeakerDatabase");

            var screen = BuildCanvas(speakers);
            var director = BuildDirector(screen, table);

            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            Selection.activeGameObject = director.gameObject;

            Debug.Log("[Weizza] Dialogue rig built. Point Sheet Sync at the two " +
                      "assets in " + assetFolder + " and pull.");
        }

        // ---- Camera ---------------------------------------------------------

        [PropertyOrder(11)]
        [TitleGroup("Camera", "What the view actually follows", TitleAlignments.Centered)]
        [InfoBox("A camera pointed straight at the player climbs on every jump, " +
                 "and a dead zone makes it worse rather than better: the view " +
                 "drifts up during the jump and the landing is still inside the " +
                 "zone, so nothing ever asks it to come back down. This puts an " +
                 "anchor between them that only changes height when the player " +
                 "is standing on something.")]
        [Button("Stop the camera climbing on jumps", ButtonSizes.Large)]
        [GUIColor(0.9f, 0.72f, 0.4f)]
        private void FixJumpCamera()
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                Debug.LogWarning("[Weizza] No PlayerController in the scene, so there is nothing to anchor to.");
                return;
            }

            var go = Find("Camera Anchor") ?? new GameObject("Camera Anchor");
            go.transform.position = player.transform.position;

            var anchor = Ensure<CameraAnchor>(go);
            var so = new SerializedObject(anchor);
            so.FindProperty("player").objectReferenceValue = player;
            so.ApplyModifiedProperties();

            var repointed = 0;
            foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                if (camera.Follow == player.transform || camera.Follow == null)
                {
                    camera.Follow = go.transform;
                    EditorUtility.SetDirty(camera);
                    repointed++;
                }
            }

            Undo.RegisterCreatedObjectUndo(go, "Fix jump camera");
            EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log($"[Weizza] Camera anchor ready, {repointed} camera(s) repointed at it.");
        }

        // ---- NPC ------------------------------------------------------------

        [PropertyOrder(12)]
        [TitleGroup("Place an NPC", "Someone to walk up to", TitleAlignments.Centered)]
        [SerializeField] private string npcName = "Soka";

        [PropertyOrder(12)]
        [TitleGroup("Place an NPC")]
        [PreviewField(58f, ObjectFieldAlignment.Left)]
        [SerializeField] private Sprite npcSprite;

        [PropertyOrder(12)]
        [TitleGroup("Place an NPC")]
        [Tooltip("What they say when the player presses talk.")]
        [SerializeField] private Conversation npcConversation;

        [PropertyOrder(12)]
        [TitleGroup("Place an NPC")]
        [Tooltip("Metres to the right of the player. They land on the floor line " +
                 "either way, so this is only about how far the walk is.")]
        [PropertyRange(-20f, 20f)]
        [SerializeField] private float npcOffsetX = 5f;

        [PropertyOrder(12)]
        [TitleGroup("Place an NPC")]
        [PropertyRange(0.5f, 4f)]
        [Tooltip("How tall they stand in metres. A portrait is drawn to fill a " +
                 "dialogue panel, so dropped into a street at its own size it is " +
                 "three storeys high.")]
        [SerializeField] private float npcHeight = 1.8f;

        [PropertyOrder(13)]
        [Button("Place them in the scene", ButtonSizes.Large)]
        [GUIColor(0.55f, 0.72f, 0.95f)]
        private void PlaceNpc()
        {
            if (npcSprite == null)
            {
                Debug.LogWarning("[Weizza] Give the NPC a sprite first, or there is nothing to stand there.");
                return;
            }

            if (string.IsNullOrWhiteSpace(npcName))
            {
                Debug.LogWarning("[Weizza] The NPC needs a name to be findable later.");
                return;
            }

            var existing = GameObject.Find(npcName);
            var go = existing != null ? existing : new GameObject(npcName);
            if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Place NPC");

            var renderer = Ensure<SpriteRenderer>(go);
            renderer.sprite = npcSprite;
            renderer.sortingLayerName = "Default";

            // Half a sprite above the floor line, because a sprite drawn with its
            // pivot in the middle would otherwise stand knee deep in the ground.
            var drawn = npcSprite.bounds.size;
            var scale = drawn.y > 0.001f ? npcHeight / drawn.y : 1f;
            var size = drawn * scale;

            var player = FindAnyObjectByType<PlayerController>();
            var originX = player != null ? player.transform.position.x : 0f;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            go.transform.position = new Vector3(originX + npcOffsetX, size.y * 0.5f, 0f);

            // Sized to the drawing and marked as a trigger. It exists so a click
            // can land on this character, and a solid one would also be a wall
            // the player bumps into on the way over.
            var box = Ensure<BoxCollider>(go);
            box.size = new Vector3(Mathf.Max(0.2f, drawn.x), Mathf.Max(0.2f, drawn.y), 0.5f / Mathf.Max(0.01f, scale));
            box.center = Vector3.zero;
            box.isTrigger = true;

            var talk = Ensure<NpcInteractable>(go);
            var so = new SerializedObject(talk);
            so.FindProperty("conversation").objectReferenceValue = npcConversation;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            if (npcConversation == null)
            {
                Debug.LogWarning($"[Weizza] {npcName} is standing there with nothing to say. " +
                                 "Pull the sheet, then drop a conversation into the field.");
                return;
            }

            Debug.Log($"[Weizza] {npcName} placed at x {go.transform.position.x:0.0}, " +
                      $"saying {npcConversation.name}.");
        }

        /// <summary>
        /// Gets a component, or adds it.
        ///
        /// The cast to Component is the whole point and cannot be removed. Unity
        /// overloads the equality operator so that an object whose native half is
        /// gone compares equal to null, but that overload is chosen at compile
        /// time from the static type, and a bare generic T is not a type the
        /// compiler will pick it for. Written as "found != null" on the generic,
        /// the comparison quietly falls back to reference equality, a missing
        /// component comes back as a live looking wrapper around nothing, and the
        /// next line sets a sprite on it and throws. Comparing as Component is
        /// what makes Unity's own operator apply.
        /// </summary>
        private static T Ensure<T>(GameObject go) where T : Component
        {
            Component found = go.GetComponent<T>();
            return found != null ? (T)found : Undo.AddComponent<T>(go);
        }

        // ---- Canvas ---------------------------------------------------------

        private DialogueScreen BuildCanvas(SpeakerDatabase speakers)
        {
            var canvasGo = Find("Dialogue Canvas") ?? new GameObject("Dialogue Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above whatever HUD arrives later. The panel is the only thing on
            // screen while it is up, so it has no business sorting against
            // anything else.
            canvas.sortingOrder = 500;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            EnsureEventSystem();

            var panel = Child(canvasGo.transform, "Panel");
            var panelRect = Rect(panel);
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.offsetMin = new Vector2(80f, 48f);
            panelRect.offsetMax = new Vector2(-80f, panelHeight);

            var group = Ensure<CanvasGroup>(panel);

            var frame = Ensure<Image>(panel);
            frame.color = frameColour;

            var fill = Child(panel.transform, "Fill");
            var fillRect = Rect(fill);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(6f, 6f);
            fillRect.offsetMax = new Vector2(-6f, -6f);
            var fillImage = Ensure<Image>(fill);
            fillImage.color = panelColour;

            // Inside the panel, not beside it. Outside, it keeps drawing after the
            // panel hides, and an Image with no sprite is a white rectangle over
            // the game. Inside, it fades and disappears with everything else.
            var portrait = Adopt(canvasGo.transform, panel.transform, "Portrait");
            var portraitRect = Rect(portrait);
            portraitRect.anchorMin = new Vector2(0f, 0f);
            portraitRect.anchorMax = new Vector2(0f, 0f);
            portraitRect.pivot = new Vector2(0f, 0f);
            portraitRect.anchoredPosition = new Vector2(-40f, 0f);
            portraitRect.sizeDelta = new Vector2(420f, 620f);
            var portraitImage = Ensure<Image>(portrait);
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            var nameLabel = Label(panel.transform, "Name", 46f, FontStyles.Bold);
            var nameRect = Rect(nameLabel.gameObject);
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 0f);
            nameRect.anchoredPosition = new Vector2(60f, 6f);
            nameRect.sizeDelta = new Vector2(600f, 60f);
            nameLabel.color = textColour;

            var body = Label(panel.transform, "Body", 34f, FontStyles.Normal);
            var bodyRect = Rect(body.gameObject);
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(440f, 60f);
            bodyRect.offsetMax = new Vector2(-60f, -34f);
            body.color = textColour;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.textWrappingMode = TextWrappingModes.Normal;

            var hint = Label(panel.transform, "Advance Hint", 26f, FontStyles.Italic);
            hint.text = "E";
            hint.alignment = TextAlignmentOptions.BottomRight;
            hint.color = textColour;
            var hintRect = Rect(hint.gameObject);
            hintRect.anchorMin = new Vector2(1f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(1f, 0f);
            hintRect.anchoredPosition = new Vector2(-30f, 18f);
            hintRect.sizeDelta = new Vector2(120f, 44f);

            var choiceRoot = Child(panel.transform, "Choices");
            var choiceRect = Rect(choiceRoot);
            choiceRect.anchorMin = new Vector2(0f, 1f);
            choiceRect.anchorMax = new Vector2(1f, 1f);
            choiceRect.pivot = new Vector2(0.5f, 0f);
            choiceRect.offsetMin = new Vector2(440f, 20f);
            choiceRect.offsetMax = new Vector2(-60f, 220f);

            var layout = choiceRoot.GetComponent<VerticalLayoutGroup>()
                         ?? choiceRoot.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var buttons = new List<DialogueScreen.ChoiceButton>();
            for (var i = 0; i < 3; i++) buttons.Add(ChoiceButton(choiceRoot.transform, i));
            choiceRoot.SetActive(false);

            var screen = Ensure<DialogueScreen>(panel);
            var so = new SerializedObject(screen);
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("nameLabel").objectReferenceValue = nameLabel;
            so.FindProperty("bodyLabel").objectReferenceValue = body;
            so.FindProperty("portrait").objectReferenceValue = portraitImage;
            so.FindProperty("advanceHint").objectReferenceValue = hint.gameObject;
            so.FindProperty("choiceRoot").objectReferenceValue = choiceRoot;
            so.FindProperty("speakers").objectReferenceValue = speakers;

            var list = so.FindProperty("choices");
            list.arraySize = buttons.Count;
            for (var i = 0; i < buttons.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("button").objectReferenceValue = buttons[i].button;
                element.FindPropertyRelative("label").objectReferenceValue = buttons[i].label;
            }

            so.ApplyModifiedProperties();
            return screen;
        }

        private DialogueScreen.ChoiceButton ChoiceButton(Transform parent, int index)
        {
            var go = Child(parent, $"Choice {index + 1}");
            var image = Ensure<Image>(go);
            image.color = new Color(frameColour.r, frameColour.g, frameColour.b, 0.35f);

            var button = Ensure<Button>(go);
            button.targetGraphic = image;

            var element = Ensure<LayoutElement>(go);
            element.preferredHeight = 54f;

            var label = Label(go.transform, "Label", 28f, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.Left;
            label.color = textColour;
            var rect = Rect(label.gameObject);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20f, 0f);
            rect.offsetMax = new Vector2(-20f, 0f);

            return new DialogueScreen.ChoiceButton { button = button, label = label };
        }

        // ---- Systems --------------------------------------------------------

        private DialogueDirector BuildDirector(DialogueScreen screen, LocalizationTableAsset table)
        {
            var systems = Find("Narrative") ?? new GameObject("Narrative");

            var localization = Ensure<SheetLocalization>(systems);
            var locaSo = new SerializedObject(localization);
            locaSo.FindProperty("table").objectReferenceValue = table;
            locaSo.ApplyModifiedProperties();

            var volume = BuildBlur(systems.transform);

            var director = Ensure<DialogueDirector>(systems);

            var so = new SerializedObject(director);
            so.FindProperty("blurVolume").objectReferenceValue = volume;

            // Whatever drives the player goes on the suspend list. Found rather
            // than asked for, because the one time it matters is the first build,
            // and being told to go and drag it in is how it ends up empty.
            var suspend = so.FindProperty("suspendWhileTalking");
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                suspend.arraySize = 1;
                suspend.GetArrayElementAtIndex(0).objectReferenceValue = player;
            }

            so.ApplyModifiedProperties();

            Undo.RegisterCreatedObjectUndo(systems, "Build dialogue rig");
            if (screen != null) Undo.RegisterCreatedObjectUndo(screen.gameObject, "Build dialogue rig");

            return director;
        }

        private Volume BuildBlur(Transform parent)
        {
            var go = Child(parent, "Dialogue Blur");
            var volume = Ensure<Volume>(go);

            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 0f;

            var path = $"{assetFolder}/DialogueBlur.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // Three effects, not one, because the obvious single answer is not
            // dependable here. Depth of field is the only real blur URP ships,
            // and it derives every pixel's blur from how far away that pixel is,
            // which an orthographic camera does not describe the way a
            // perspective one does. Where it works it is the nicest of the
            // three; where it does nothing, the other two still take the world
            // back and leave the panel holding the eye.
            var dof = Override<DepthOfField>(profile);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0.1f);
            dof.gaussianEnd.Override(2f);
            dof.gaussianMaxRadius.Override(Mathf.Clamp(blurStrength, 0.5f, 1.5f));

            // Needs no depth and no perspective, so this is the part that is
            // certain to happen: colour drains out of the world and it steps
            // back a stop.
            var colour = Override<ColorAdjustments>(profile);
            colour.saturation.Override(-45f);
            colour.postExposure.Override(-0.85f);

            // Darkened corners, so the eye is pulled to the middle and down to
            // the panel rather than wandering the street behind it.
            var vignette = Override<Vignette>(profile);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.55f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            volume.sharedProfile = profile;

            EnablePostProcessing();
            return volume;
        }

        /// <summary>
        /// Gets an effect out of a profile, or adds one that will still be there
        /// tomorrow.
        ///
        /// VolumeProfile.Add builds the effect in memory and hands it back, but
        /// never files it inside the profile asset. Unity then saves a profile
        /// whose component list points at an object that was never written, and
        /// the next time the project loads the list is simply empty. Nothing
        /// warns: the volume is present, its weight animates, and no effect ever
        /// happens. AddObjectToAsset is what makes the effect part of the file.
        /// </summary>
        private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing) && existing != null) return existing;

            var added = profile.Add<T>();
            added.active = true;

            if (!AssetDatabase.Contains(added))
            {
                added.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(added, profile);
            }

            return added;
        }

        /// <summary>
        /// Turns post processing on for the cameras that render the game.
        ///
        /// A volume with a depth of field in it does nothing at all unless the
        /// camera is asked to run post effects, and the switch is off by default
        /// on a camera added by hand. Nothing warns about it: the blur simply
        /// never happens, which reads as the volume being mis-configured rather
        /// than as a camera checkbox.
        /// </summary>
        private static void EnablePostProcessing()
        {
            var turnedOn = 0;

            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var data = camera.GetUniversalAdditionalCameraData();
                if (data == null || data.renderPostProcessing) continue;

                Undo.RecordObject(data, "Enable post processing");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                turnedOn++;
            }

            if (turnedOn > 0)
            {
                Debug.Log($"[Weizza] Post processing switched on for {turnedOn} camera(s), " +
                          "which is what lets the dialogue blur show up at all.");
            }
        }

        // ---- Plumbing -------------------------------------------------------

        private T EnsureAsset<T>(string fileName) where T : ScriptableObject
        {
            EnsureFolder(assetFolder);

            var path = $"{assetFolder}/{fileName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;

            // Without one the choice buttons are drawn and never clickable, which
            // reads as the buttons being broken rather than as the scene missing
            // a manager nobody remembers adding.
            var go = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build dialogue rig");
        }

        private static TextMeshProUGUI Label(Transform parent, string name, float size, FontStyles style)
        {
            var go = Child(parent, name);
            var label = Ensure<TextMeshProUGUI>(go);
            label.fontSize = size;
            label.fontStyle = style;
            label.raycastTarget = false;

            var font = AnyFont();
            if (font != null) label.font = font;

            return label;
        }

        private static TMP_FontAsset _font;
        private static bool _lookedForFont;

        /// <summary>
        /// Any TextMeshPro font in the project, or nothing.
        ///
        /// A project that has never used TextMeshPro has no font assets and no
        /// TMP Settings, and a label built in that state draws nothing at all
        /// while reporting no error. The panel then opens, waits for a keypress
        /// and closes with every line invisible, which reads as broken dialogue
        /// rather than as a missing import.
        /// </summary>
        private static TMP_FontAsset AnyFont()
        {
            if (_lookedForFont) return _font;
            _lookedForFont = true;

            var found = AssetDatabase.FindAssets("t:TMP_FontAsset");
            if (found.Length == 0)
            {
                Debug.LogWarning("[Weizza] This project has no TextMeshPro font asset, so " +
                                 "dialogue text will be invisible. Run Window > TextMeshPro > " +
                                 "Import TMP Essential Resources, then build the rig again.");
                return null;
            }

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(found[0]));
            return _font;
        }

        private static RectTransform Rect(GameObject go)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect != null) return rect;

            return go.AddComponent<RectTransform>();
        }

        private static GameObject Find(string name)
        {
            var existing = GameObject.Find(name);
            return existing;
        }

        /// <summary>
        /// Finds a named object anywhere under a root and moves it where it
        /// belongs, creating it if there is none.
        ///
        /// A plain child lookup only sees one level, so rebuilding a rig whose
        /// parts moved would quietly make a second copy of each one and leave
        /// the original behind, still drawing.
        /// </summary>
        private static GameObject Adopt(Transform root, Transform parent, string name)
        {
            var found = Descendant(root, name);
            if (found == null)
            {
                found = new GameObject(name, typeof(RectTransform)).transform;
            }

            found.SetParent(parent, false);
            return found.gameObject;
        }

        private static Transform Descendant(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;

                var deeper = Descendant(child, name);
                if (deeper != null) return deeper;
            }

            return null;
        }

        private static GameObject Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.gameObject;

            var created = new GameObject(name, typeof(RectTransform));
            created.transform.SetParent(parent, false);
            return created;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var built = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
