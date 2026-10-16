using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Puts the assist settings into the running game: the game speed and the
    /// player's invincibility. Celeste's lesson is that each assist is its own switch,
    /// so a player who only needs more time does not have to take invincibility too.
    /// The longer warnings are read where the wind up is timed, in EnemyController.
    ///
    /// The game speed is the base time scale, so every slow motion in the game (the
    /// death beat, aiming, the final blow) already returns to it rather than to 1.
    ///
    /// Builds itself on scene load, so no scene needs it placed.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Assist Options")]
    public sealed class AssistOptions : MonoBehaviour
    {
        /// <summary>The speed the game counts as normal. Slow motion effects return to this.</summary>
        public static float Normal => GameSettings.GameSpeed;

        private float _appliedSpeed = 1f;
        private bool _appliedInvincible;
        private PlayerController _player;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (FindFirstObjectByType<AssistOptions>() != null) return;
            var go = new GameObject("Assist Options");
            DontDestroyOnLoad(go);
            go.AddComponent<AssistOptions>();
        }

        private void OnEnable()
        {
            GameSettings.Changed += Apply;
            _appliedSpeed = StoryTimeScale.Base;
            Apply();
        }

        private void OnDisable() => GameSettings.Changed -= Apply;

        private void Update()
        {
            // The player is created and replaced by scene loads; catch the new one.
            var player = PlayerLocator.Player;
            if (player != _player)
            {
                _player = player;
                _appliedInvincible = false;
                ApplyInvincible();
            }
        }

        private void Apply()
        {
            // Only move the speed when it is still the one set here. In the middle of a
            // slow motion the effect owns it, and it returns to Normal by itself.
            var speed = GameSettings.GameSpeed;
            if (Mathf.Abs(StoryTimeScale.Base - _appliedSpeed) < 0.001f && Mathf.Abs(speed - _appliedSpeed) > 0.001f)
                StoryTimeScale.SetBase(speed);
            _appliedSpeed = speed;

            ApplyInvincible();
        }

        private void ApplyInvincible()
        {
            if (_player == null || _player.Vitals == null) return;
            var wanted = GameSettings.AssistInvincible;
            if (wanted == _appliedInvincible) return;
            // GodMode is shared with the developer console; only switch it off if this turned it on.
            if (wanted) _player.Vitals.GodMode = true;
            else if (_appliedInvincible) _player.Vitals.GodMode = false;
            _appliedInvincible = wanted;
        }
    }
}
