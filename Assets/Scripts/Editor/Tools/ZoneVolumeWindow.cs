using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds a region of the level its own colour grade, the tool answer to
    /// "can the grade vary per biome". Nothing new is invented: Unity's own
    /// Volume framework already blends a local Volume against the global one
    /// by distance from a trigger collider, which is exactly what a forest
    /// reading differently from a mountain needs. This just authors one in a
    /// few clicks instead of by hand: name it, drag the width, press the
    /// button, and it duplicates the current global grade as a starting point
    /// so a new zone begins identical to the rest of the level and is nudged
    /// from there, rather than starting blank and fighting to match.
    /// </summary>
    public sealed class ZoneVolumeWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Create Zone Volume", priority = 6)]
        private static void Open()
        {
            var window = GetWindow<ZoneVolumeWindow>();
            window.titleContent = new GUIContent("Zone Volume");
            window.minSize = new Vector2(320f, 260f);
            window.Show();
        }

        [TitleGroup("Zone", "A region with its own colour grade", TitleAlignments.Centered)]
        [InfoBox("Spans this X range, centred on Y and deep enough on Z to never " +
                 "need edging. Weight fades in over Blend Distance on either " +
                 "side, so walking into a zone is a grade that drifts, not a cut.")]
        [SerializeField] private string zoneName = "Mountain";

        [TitleGroup("Zone")]
        [SerializeField] private float xFrom = 180f;

        [TitleGroup("Zone")]
        [SerializeField] private float xTo = 260f;

        [TitleGroup("Zone")]
        [PropertyRange(1f, 30f)]
        [Tooltip("How far outside the zone the grade keeps blending in or out. " +
                 "Too short and a walk across the border reads as a jump cut.")]
        [SerializeField] private float blendDistance = 8f;

        [TitleGroup("Zone")]
        [PropertyRange(0f, 100f)]
        [Tooltip("Where two zones overlap, the higher priority wins the centre " +
                 "of the blend. Leave every zone equal unless two are deliberately " +
                 "stacked.")]
        [SerializeField] private float priority = 0f;

        [TitleGroup("Source")]
        [InfoBox("Copied into the new zone's own profile as a starting point, so " +
                 "a new zone opens identical to the rest of the level rather than " +
                 "blank. Defaults to the level's global grade.")]
        [Required]
        [SerializeField] private VolumeProfile startFrom;

        [TitleGroup("Source")]
        [Tooltip("Where the new profile asset is written.")]
        [SerializeField] private string outputFolder = "Assets/GameData/Atmosphere/Zones";

        private void OnEnable()
        {
            if (startFrom == null)
            {
                startFrom = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                    "Assets/GameData/Atmosphere/SceneGrade.asset");
            }
        }

        [BoxGroup("Live", centerLabel: true)]
        [Button(ButtonSizes.Large, Icon = SdfIconType.PlusCircle), GUIColor(0.4f, 0.8f, 0.5f)]
        private void CreateZone()
        {
            if (string.IsNullOrWhiteSpace(zoneName))
            {
                Debug.LogWarning("[Weizza] Give the zone a name first.");
                return;
            }

            if (startFrom == null)
            {
                Debug.LogWarning("[Weizza] No profile to start from. Assign one, or make sure " +
                                  "Assets/GameData/Atmosphere/SceneGrade.asset still exists.");
                return;
            }

            System.IO.Directory.CreateDirectory(outputFolder);
            var profilePath = $"{outputFolder}/{zoneName}_Grade.asset";

            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) != null)
            {
                Debug.LogWarning($"[Weizza] {profilePath} already exists. Pick a different name " +
                                  "or delete the old one first, nothing was created.");
                return;
            }

            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(startFrom), profilePath))
            {
                Debug.LogWarning($"[Weizza] Could not copy the profile to {profilePath}.");
                return;
            }

            var newProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);

            var systems = GameObject.Find("/Systems");
            var go = new GameObject($"Zone: {zoneName}");
            Undo.RegisterCreatedObjectUndo(go, "Create zone volume");
            if (systems != null) go.transform.SetParent(systems.transform, false);

            var centreX = (xFrom + xTo) * 0.5f;
            var width = Mathf.Abs(xTo - xFrom);
            go.transform.position = new Vector3(centreX, 0f, 0f);

            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            // 200 tall and 40 deep on purpose: this only ever has to bound a
            // grade, never gameplay, so it is built wide enough that nobody
            // building a tall room later has to remember to widen it, the same
            // mistake the camera bounds box already taught this project once.
            collider.size = new Vector3(width, 200f, 40f);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.sharedProfile = newProfile;
            volume.blendDistance = blendDistance;
            volume.priority = priority;

            Selection.activeGameObject = go;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log($"[Weizza] Zone '{zoneName}' created at x {xFrom} to {xTo}, profile at " +
                      $"{profilePath}. Edit that profile's overrides to give the zone its own look.");
        }
    }
}
