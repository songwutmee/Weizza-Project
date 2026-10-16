using UnityEngine;
using UnityEngine.InputSystem;

namespace Weizza
{
    /// <summary>
    /// Controller rumble. Every screen shake already says "this hit mattered", so the
    /// shake drives the rumble (CameraFeel calls FromShake), and getting hurt adds a
    /// pulse of its own. One rule for both keeps them in step without every system
    /// having to remember to rumble.
    ///
    /// Short and event driven on purpose: constant buzzing turns into noise and tires
    /// the hands. The accessibility guidelines ask for a way to turn it off in the game
    /// itself, which is the Controller rumble slider in Settings.
    ///
    /// Low motor is the heavy thud, high motor the sharp buzz. Runs on unscaled time so
    /// it keeps going through hit stop and stops on time during slow motion.
    /// </summary>
    public sealed class Rumble : MonoBehaviour
    {
        private static Rumble _runner;
        private float _low, _high, _until;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _runner = null;

        /// <summary>A pulse. Strengths 0 to 1, a stronger pulse overrides a weaker one still running.</summary>
        public static void Pulse(float low, float high, float seconds)
        {
            var scale = GameSettings.RumbleScale;
            if (scale <= 0f || Gamepad.current == null) return;
            if (_runner == null)
            {
                var go = new GameObject("Rumble");
                DontDestroyOnLoad(go);
                _runner = go.AddComponent<Rumble>();
            }
            _runner.Add(Mathf.Clamp01(low * scale), Mathf.Clamp01(high * scale), seconds);
        }

        /// <summary>Turns a camera shake strength (about 0.05 to 0.6) into a matching pulse.</summary>
        public static void FromShake(float shake)
        {
            if (shake < 0.04f) return;
            var k = Mathf.Clamp01(shake / 0.5f);
            Pulse(0.25f + k * 0.6f, 0.15f + k * 0.35f, 0.08f + k * 0.16f);
        }

        private void Add(float low, float high, float seconds)
        {
            var now = Time.unscaledTime;
            if (now < _until && low + high < _low + _high) return;
            _low = low;
            _high = high;
            _until = now + seconds;
            Gamepad.current?.SetMotorSpeeds(_low, _high);
        }

        private void Update()
        {
            if (_until <= 0f || Time.unscaledTime < _until) return;
            _until = 0f;
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }

        // A pad left buzzing after a pause, a quit or a scene change is the worst
        // possible rumble bug, so every way out stops the motors.
        private void OnApplicationPause(bool paused) { if (paused) Stop(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Stop(); }
        private void OnDisable() => Stop();

        private void Stop()
        {
            _until = 0f;
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }
    }
}
