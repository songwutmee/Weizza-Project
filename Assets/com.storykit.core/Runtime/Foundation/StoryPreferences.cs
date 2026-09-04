using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// Player-facing settings every story game needs (research: Ren'Py /
    /// Naninovel settings screens). Values live in the Global blackboard under
    /// "_pref.*" keys, so StorySave persists them automatically with the save
    /// file: no separate settings system required.
    ///
    /// Your options menu just reads/writes these properties; SimpleDialogueUI
    /// and the audio nodes consume them live.
    /// </summary>
    public static class StoryPreferences
    {
        private const string P = "_pref.";

        /// <summary>Characters revealed per second in dialogue (default 40).</summary>
        public static float TextSpeed
        {
            get => StoryState.Global.Get(P + "textSpeed", 40f);
            set => StoryState.Global.Set(P + "textSpeed", Mathf.Clamp(value, 5f, 200f));
        }

        /// <summary>Seconds auto mode waits after a line finishes (default 1.6).</summary>
        public static float AutoDelay
        {
            get => StoryState.Global.Get(P + "autoDelay", 1.6f);
            set => StoryState.Global.Set(P + "autoDelay", Mathf.Clamp(value, 0.2f, 10f));
        }

        public static bool AutoMode
        {
            get => StoryState.Global.Get(P + "autoMode", 0f) > 0f;
            set => StoryState.Global.Set(P + "autoMode", value ? 1f : 0f);
        }

        public static bool SkipReadMode
        {
            get => StoryState.Global.Get(P + "skipRead", 0f) > 0f;
            set => StoryState.Global.Set(P + "skipRead", value ? 1f : 0f);
        }

        /// <summary>Accessibility: disable wave/shake text animation.</summary>
        public static bool ReduceTextMotion
        {
            get => StoryState.Global.Get(P + "reduceMotion", 0f) > 0f;
            set => StoryState.Global.Set(P + "reduceMotion", value ? 1f : 0f);
        }

        /// <summary>Accessibility: disable camera shake.</summary>
        public static bool ReduceCameraShake
        {
            get => StoryState.Global.Get(P + "reduceShake", 0f) > 0f;
            set => StoryState.Global.Set(P + "reduceShake", value ? 1f : 0f);
        }

        /// <summary>Accessibility: turn off controller rumble / phone vibration.</summary>
        public static bool DisableHaptics
        {
            get => StoryState.Global.Get(P + "noHaptics", 0f) > 0f;
            set => StoryState.Global.Set(P + "noHaptics", value ? 1f : 0f);
        }

        /// <summary>Accessibility: show captions for important non-speech sounds.</summary>
        public static bool SoundCaptions
        {
            get => StoryState.Global.Get(P + "soundCaptions", 0f) > 0f;
            set => StoryState.Global.Set(P + "soundCaptions", value ? 1f : 0f);
        }

        public static float BgmVolume
        {
            get => StoryState.Global.Get(P + "bgmVol", 1f);
            set => StoryState.Global.Set(P + "bgmVol", Mathf.Clamp01(value));
        }

        public static float SfxVolume
        {
            get => StoryState.Global.Get(P + "sfxVol", 1f);
            set => StoryState.Global.Set(P + "sfxVol", Mathf.Clamp01(value));
        }

        public static float VoiceVolume
        {
            get => StoryState.Global.Get(P + "voiceVol", 1f);
            set => StoryState.Global.Set(P + "voiceVol", Mathf.Clamp01(value));
        }

        public static string Language
        {
            get => StoryState.Global.Get(P + "language", "");
            set
            {
                StoryState.Global.Set(P + "language", value ?? "");
                if (StoryServices.TryGet<ILocalizationService>(out var loca))
                    loca.ActiveLanguage = value;
            }
        }
    }
}
