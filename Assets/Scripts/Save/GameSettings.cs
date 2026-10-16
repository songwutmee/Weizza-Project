using System;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Player preferences: volumes, screen shake, flashes, damage numbers,
    /// language, rebinds. Kept apart from the story save on purpose, so
    /// starting a new game or deleting a slot never resets someone's volume.
    ///
    /// Stored as one JSON string in PlayerPrefs. Every system reads the live
    /// values from here and listens to Changed, so a slider in the settings
    /// screen takes effect while it is being dragged.
    /// </summary>
    public static class GameSettings
    {
        [Serializable]
        private sealed class Data
        {
            public float master = 0.9f;
            public float music = 0.7f;
            public float sfx = 0.9f;
            public float ambience = 0.7f;
            public float voice = 1f;
            public float shake = 1f;
            public bool reduceFlashes;
            public float gameSpeed = 1f;
            public float rumble = 1f;
            public bool assistInvincible;
            public bool longerWarnings;
            public bool damageNumbers;
            public bool frameData;
            public string language = "th";
            public string bindings = "";
            public bool fullscreen = true;
            public bool vsync = true;
            public int width;
            public int height;
        }

        private const string Key = "weizza.settings";
        private static Data _data;

        /// <summary>Raised after any value changes.</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _data = null;
            Changed = null;
        }

        private static Data D
        {
            get
            {
                if (_data != null) return _data;
                var json = PlayerPrefs.GetString(Key, string.Empty);
                _data = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json) ?? new Data();
                return _data;
            }
        }

        public static float MasterVolume { get => D.master; set => Set(ref D.master, Mathf.Clamp01(value)); }
        public static float MusicVolume { get => D.music; set => Set(ref D.music, Mathf.Clamp01(value)); }
        public static float SfxVolume { get => D.sfx; set => Set(ref D.sfx, Mathf.Clamp01(value)); }
        public static float AmbienceVolume { get => D.ambience; set => Set(ref D.ambience, Mathf.Clamp01(value)); }
        public static float VoiceVolume { get => D.voice; set => Set(ref D.voice, Mathf.Clamp01(value)); }

        /// <summary>Multiplies every camera shake. Zero turns shake off entirely, an accessibility setting.</summary>
        public static float ShakeScale { get => D.shake; set => Set(ref D.shake, Mathf.Clamp(value, 0f, 1.5f)); }

        /// <summary>Softer hit flashes, for players sensitive to flashing.</summary>
        public static bool ReduceFlashes { get => D.reduceFlashes; set => Set(ref D.reduceFlashes, value); }

        /// <summary>Off by default: the design bible says hit stop reads better than numbers.</summary>
        /// <summary>Assist: the whole game slower, in steps of a tenth, like Celeste's assist mode. 1 is normal.</summary>
        public static float GameSpeed
        {
            get => D.gameSpeed <= 0f ? 1f : D.gameSpeed;
            set => Set(ref D.gameSpeed, Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 0.5f, 1f));
        }

        /// <summary>Controller rumble strength, 0 is off.</summary>
        public static float RumbleScale { get => D.rumble; set => Set(ref D.rumble, Mathf.Clamp01(value)); }

        /// <summary>Assist: the player takes no damage.</summary>
        public static bool AssistInvincible { get => D.assistInvincible; set => Set(ref D.assistInvincible, value); }

        /// <summary>Assist: enemies hold their wind up half as long again before they swing.</summary>
        public static bool LongerWarnings { get => D.longerWarnings; set => Set(ref D.longerWarnings, value); }

        public static bool DamageNumbers { get => D.damageNumbers; set => Set(ref D.damageNumbers, value); }

        /// <summary>Frame data bar over the player while attacking. A tuning aid.</summary>
        public static bool FrameDataOverlay { get => D.frameData; set => Set(ref D.frameData, value); }

        public static string Language { get => D.language; set => Set(ref D.language, value ?? "th"); }

        /// <summary>Input rebinds as JSON, one entry per action.</summary>
        public static string Bindings { get => D.bindings; set => Set(ref D.bindings, value ?? string.Empty); }

        // ---- Display (M8.4) --------------------------------------------------

        public static bool Fullscreen { get => D.fullscreen; set { Set(ref D.fullscreen, value); ApplyDisplay(); } }
        public static bool VSync { get => D.vsync; set { Set(ref D.vsync, value); ApplyDisplay(); } }

        /// <summary>Chosen resolution, or 0 x 0 for the screen's own.</summary>
        public static Vector2Int Resolution => new(D.width, D.height);

        public static void SetResolution(int width, int height)
        {
            D.width = width;
            D.height = height;
            Save();
            ApplyDisplay();
            Changed?.Invoke();
        }

        /// <summary>
        /// Pushes the display settings to the screen. At boot, and whenever one
        /// changes. In the Editor the Game view ignores most of this, which is fine.
        /// </summary>
        public static void ApplyDisplay()
        {
            QualitySettings.vSyncCount = D.vsync ? 1 : 0;
            // Without vsync, cap at the display's own rate rather than letting a
            // side scroller burn a GPU at 900 frames a second.
            Application.targetFrameRate = D.vsync ? -1 : Mathf.Max(60, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));

            var mode = D.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var width = D.width > 0 ? D.width : Screen.currentResolution.width;
            var height = D.height > 0 ? D.height : Screen.currentResolution.height;
            if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
                Screen.SetResolution(width, height, mode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyAtBoot()
        {
#if !UNITY_EDITOR
            ApplyDisplay();
#endif
        }

        private static void Set<T>(ref T field, T value)
        {
            if (Equals(field, value)) return;
            field = value;
            Save();
            Changed?.Invoke();
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(D));
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            _data = new Data();
            Save();
            Changed?.Invoke();
        }
    }
}
