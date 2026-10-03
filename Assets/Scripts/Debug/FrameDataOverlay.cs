using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A startup, active and recovery bar drawn over the player during every
    /// attack, with a marker on the current frame. Turned on in Settings or with
    /// the "frames" console command.
    ///
    /// Frame data is the unit this game is balanced in, and reading it off the
    /// attack asset while the swing plays is the fastest way to see whether a
    /// change landed. IMGUI so it works in a build.
    /// </summary>
    [AddComponentMenu("Weizza/Frame Data Overlay")]
    public sealed class FrameDataOverlay : MonoBehaviour
    {
        private static readonly Color Startup = new(0.45f, 0.65f, 0.95f, 0.9f);
        private static readonly Color Active = new(0.95f, 0.4f, 0.35f, 0.95f);
        private static readonly Color Recovery = new(0.55f, 0.55f, 0.6f, 0.9f);

        private float _attackStarted;
        private AttackDefinition _tracked;
        private Texture2D _pixel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            if (FindFirstObjectByType<FrameDataOverlay>() != null) return;
            new GameObject("Frame Data Overlay").AddComponent<FrameDataOverlay>();
        }

        private void Awake()
        {
            _pixel = new Texture2D(1, 1);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void Update()
        {
            var player = PlayerLocator.Player;
            var attack = player != null && player.State == PlayerState.Attack ? player.CurrentAttack : null;
            if (attack != _tracked)
            {
                _tracked = attack;
                _attackStarted = Time.time;
            }
        }

        private void OnGUI()
        {
            if (!GameSettings.FrameDataOverlay || _tracked == null) return;
            var player = PlayerLocator.Player;
            var camera = Camera.main;
            if (player == null || camera == null) return;

            var screen = camera.WorldToScreenPoint(player.transform.position + Vector3.up * 2.3f);
            if (screen.z < 0f) return;

            const float pixelsPerFrame = 4f;
            var total = _tracked.TotalFrames;
            var width = total * pixelsPerFrame;
            var x = screen.x - width * 0.5f;
            var y = Screen.height - screen.y;

            Bar(x, y, _tracked.startupFrames * pixelsPerFrame, Startup);
            Bar(x + _tracked.startupFrames * pixelsPerFrame, y, _tracked.activeFrames * pixelsPerFrame, Active);
            Bar(x + (_tracked.startupFrames + _tracked.activeFrames) * pixelsPerFrame, y,
                _tracked.recoveryFrames * pixelsPerFrame, Recovery);

            var frame = Mathf.FloorToInt((Time.time - _attackStarted) / Timing.Frame);
            Bar(x + Mathf.Min(frame, total) * pixelsPerFrame, y - 4f, 2f, Color.white, 18f);

            GUI.color = Color.white;
            GUI.Label(new Rect(x, y + 12f, 260f, 20f),
                $"{_tracked.displayName}  {_tracked.startupFrames}/{_tracked.activeFrames}/{_tracked.recoveryFrames}  f{frame}");
        }

        private void Bar(float x, float y, float width, Color colour, float height = 10f)
        {
            GUI.color = colour;
            GUI.DrawTexture(new Rect(x, y, width, height), _pixel);
        }
    }
}
