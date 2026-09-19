using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Walks a clone of the Main Camera across the level and stitches every
    /// slice into one long panorama, so the whole level can be looked at, or
    /// handed to the art team, without moving the Scene view by hand tens of
    /// times. The clone copies every rendering component off the real camera
    /// (post processing, URP additional data) so the strip shows the actual
    /// colour grade and bloom, not a bare unlit render.
    /// </summary>
    public sealed class LevelCaptureWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Level Capture", priority = 7)]
        private static void Open()
        {
            var window = GetWindow<LevelCaptureWindow>();
            window.titleContent = new GUIContent("Level Capture");
            window.minSize = new Vector2(320f, 260f);
            window.Show();
        }

        [TitleGroup("Range", "Where the strip starts and ends", TitleAlignments.Centered)]
        [InfoBox("Each slice is exactly one camera width wide, so slices tile with no seam and no overlap.")]
        [SerializeField] private float fromX = -4f;

        [TitleGroup("Range")]
        [SerializeField] private float toX = 530f;

        [TitleGroup("Range")]
        [Tooltip("Added to the ground height under each slice, so the strip frames the walkable band " +
                 "instead of the sky or the earth fill below it. 5.2 matches how this project's own " +
                 "screenshots have been framed by hand.")]
        [SerializeField] private float heightAboveGround = 5.2f;

        [TitleGroup("Range")]
        [Tooltip("A slice with no ground under it (open sky, or a gap wider than the camera) reuses the " +
                 "last known ground height instead of leaving a gap in the strip.")]
        [SerializeField] private bool followGround = true;

        [TitleGroup("Output")]
        [Tooltip("Height in pixels of one slice. Width is worked out from the camera's own aspect.")]
        [PropertyRange(180, 1080)]
        [SerializeField] private int sliceHeight = 360;

        [TitleGroup("Output")]
        [SerializeField] private string outputPath = "Assets/Captures/Level_Strip.png";

        [BoxGroup("Live", centerLabel: true)]
        [Button(ButtonSizes.Large, Icon = SdfIconType.Camera), GUIColor(0.4f, 0.8f, 0.5f)]
        private void CaptureStrip()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("[Weizza] No Main Camera tagged in the scene, nothing to clone.");
                return;
            }

            var mask = ~LayerMask.GetMask("Player", "Enemy");
            var aspect = mainCamera.aspect > 0.01f ? mainCamera.aspect : 16f / 9f;
            var sliceWidthMetres = mainCamera.orthographicSize * 2f * aspect;
            var sliceWidthPixels = Mathf.RoundToInt(sliceHeight * aspect);

            var sliceCount = Mathf.Max(1, Mathf.CeilToInt((toX - fromX) / sliceWidthMetres));

            // Unity caps a Texture2D at 16384 on a side. A long level plus a tall slice height
            // crosses that with no warning beyond an exception mid capture, so the slice size is
            // scaled down first rather than found out the hard way on a full level sweep.
            const int maxDimension = 16384;
            var totalWidth = sliceWidthPixels * sliceCount;
            if (totalWidth > maxDimension)
            {
                var scale = (float)maxDimension / totalWidth;
                sliceHeight = Mathf.Max(60, Mathf.FloorToInt(sliceHeight * scale));
                sliceWidthPixels = Mathf.RoundToInt(sliceHeight * aspect);
                totalWidth = sliceWidthPixels * sliceCount;
                Debug.LogWarning($"[Weizza] {sliceCount} slices at the requested height would be " +
                    $"wider than Unity's {maxDimension} px texture limit. Slice height reduced to " +
                    $"{sliceHeight} so the strip fits.");
            }

            var strip = new Texture2D(totalWidth, sliceHeight, TextureFormat.RGB24, false);

            var rigGo = new GameObject("LevelCapture_TempCamera");
            var rigCam = rigGo.AddComponent<Camera>();
            EditorUtility.CopySerialized(mainCamera, rigCam);
            rigCam.enabled = true;
            rigCam.orthographic = true;
            rigCam.orthographicSize = mainCamera.orthographicSize;
            rigGo.transform.rotation = mainCamera.transform.rotation;

            var rt = new RenderTexture(sliceWidthPixels, sliceHeight, 24);
            rigCam.targetTexture = rt;

            var lastGroundY = fromX <= toX ? SurfaceY(fromX + sliceWidthMetres * 0.5f, mask) : mainCamera.transform.position.y;

            for (var i = 0; i < sliceCount; i++)
            {
                var centreX = fromX + sliceWidthMetres * (i + 0.5f);
                var groundY = followGround ? SurfaceY(centreX, mask) : float.NaN;
                if (float.IsNaN(groundY)) groundY = lastGroundY; else lastGroundY = groundY;

                rigGo.transform.position = new Vector3(centreX, groundY + heightAboveGround, mainCamera.transform.position.z);

                rigCam.Render();
                RenderTexture.active = rt;
                var slice = new Texture2D(sliceWidthPixels, sliceHeight, TextureFormat.RGB24, false);
                slice.ReadPixels(new Rect(0, 0, sliceWidthPixels, sliceHeight), 0, 0);
                slice.Apply();
                strip.SetPixels(i * sliceWidthPixels, 0, sliceWidthPixels, sliceHeight, slice.GetPixels());
                UnityEngine.Object.DestroyImmediate(slice);
            }

            RenderTexture.active = null;
            rigCam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(rigGo);

            strip.Apply();
            var png = strip.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(strip);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputPath, png);
            AssetDatabase.Refresh();

            Debug.Log($"[Weizza] Wrote a {sliceWidthPixels * sliceCount} x {sliceHeight} strip, " +
                      $"{sliceCount} slices from x {fromX} to {toX}, to {outputPath}");
            EditorUtility.RevealInFinder(outputPath);
        }

        private static float SurfaceY(float x, LayerMask mask)
        {
            var best = float.NaN;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, 60f, 0f), Vector3.down, 120f, mask))
            {
                if (hit.collider.isTrigger) continue;
                if (float.IsNaN(best) || hit.point.y > best) best = hit.point.y;
            }
            return best;
        }
    }
}
