using System;
using System.IO;
using StoryKit;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Weizza
{
    /// <summary>
    /// A clean still of the game for portfolios, presentations and store pages.
    ///
    /// F9 freezes the world and hides the HUD and the debug tools. F12 then saves the
    /// frame at twice the screen size into a PhotoCaptures folder beside the project (in a build,
    /// next to the save files), and F9 again puts everything back. Hit stop, motes and
    /// the light keep their frozen state, so the picture is exactly the moment caught.
    ///
    /// Editor and development builds only, the same gate as the developer console.
    /// </summary>
    [AddComponentMenu("Weizza/Debug/Photo Mode")]
    public sealed class PhotoMode : MonoBehaviour
    {
        private bool _on;
        private bool _hudWas;
        private IDisposable _lock;

        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            if (FindFirstObjectByType<PhotoMode>() != null) return;
            var go = new GameObject("Photo Mode");
            DontDestroyOnLoad(go);
            go.AddComponent<PhotoMode>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f9Key.wasPressedThisFrame) Toggle();
            if (_on && keyboard.f12Key.wasPressedThisFrame) Capture();
        }

        private void Toggle()
        {
            _on = !_on;
            Active = _on;
            var hud = HudScreen.Instance;
            if (_on)
            {
                StoryTimeScale.Stop();
                _lock = StoryControl.Lock("Photo mode");
                if (hud != null) { _hudWas = true; hud.SetVisible(false); }
                Debug.Log("[Photo Mode] On. F12 saves a picture, F9 leaves.");
            }
            else
            {
                StoryTimeScale.Resume();
                _lock?.Dispose();
                _lock = null;
                if (hud != null && _hudWas) hud.SetVisible(true);
            }
        }

        private void Capture()
        {
            var folder = Application.isEditor
                ? Path.Combine(Directory.GetCurrentDirectory(), "PhotoCaptures")
                : Path.Combine(Application.persistentDataPath, "PhotoCaptures");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Weizza_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
            ScreenCapture.CaptureScreenshot(path, 2);
            Debug.Log("[Photo Mode] Saved " + path);
        }

        private void OnDisable()
        {
            if (_on) Toggle();
        }
    }
}
