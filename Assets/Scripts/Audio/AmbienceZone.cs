using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Changes the ambience bed, and optionally the music, while the player is
    /// inside. Forest birds outside, dripping water in a cave, wind on a ridge.
    /// Leaving hands back to whatever the previous zone set.
    /// </summary>
    [AddComponentMenu("Weizza/Ambience Zone")]
    public sealed class AmbienceZone : BodyZone
    {
        [BoxGroup("Sound")]
        [Tooltip("Ambience sound id in a bank. Empty means silence while inside.")]
        [SerializeField] private string ambienceId = "amb.forest";

        [BoxGroup("Sound")]
        [Tooltip("Music id to switch to while inside. Empty leaves music alone.")]
        [SerializeField] private string musicId = "";

        [BoxGroup("Sound")]
        [PropertyRange(0.1f, 5f)]
        [SerializeField] private float fade = 1.5f;

        private static AmbienceZone _current;
        private AmbienceZone _previous;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _current = null;

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
        }

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (!PlayerLocator.IsPlayer(body) || _current == this) return;
            _previous = _current;
            _current = this;
            Apply();
        }

        protected override void OnExited(CharacterMotor3D body)
        {
            if (!PlayerLocator.IsPlayer(body) || _current != this) return;
            _current = _previous;
            if (_current != null) _current.Apply();
            else AudioDirector.Instance?.SetAmbience(null, fade);
        }

        private void Apply()
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;
            audio.SetAmbience(ambienceId, fade);
            if (!string.IsNullOrEmpty(musicId)) audio.PlayBgm(musicId, fade);
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.9f, 0.85f, 0.5f, 0.12f);
#endif
    }
}
