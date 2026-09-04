using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// One window that answers, while the game is running, every question a
    /// movement or combat bug starts with: what state is the player in, how long
    /// is left of the window they are in, and what is invincible right now.
    ///
    /// It is not a HUD. Nothing here is a substitute for a real UI, and nothing
    /// in gameplay reads it. It exists so a design question can be answered in
    /// one play session instead of three.
    /// </summary>
    public sealed class DebugWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Debug Window %#w")]
        private static void Open()
        {
            var window = GetWindow<DebugWindow>();
            window.titleContent = new GUIContent("Weizza Debug");
            window.minSize = new Vector2(360f, 460f);
            window.Show();
        }

        private PlayerController _player;
        private double _nextScan;

        protected override void Initialize() => Scan();

        private void Update()
        {
            if (EditorApplication.timeSinceStartup > _nextScan)
            {
                _nextScan = EditorApplication.timeSinceStartup + 0.5;
                Scan();
            }
            Repaint();
        }

        private void Scan()
        {
            _player = Application.isPlaying ? FindFirstObjectByType<PlayerController>() : null;
        }

        private bool HasPlayer => Application.isPlaying && _player != null;

        [ShowIf("@!UnityEngine.Application.isPlaying")]
        [InfoBox("Enter play mode. Everything below reads the live scene.")]
        [ShowInInspector, HideLabel, DisplayAsString]
        private string NotPlaying => " ";

        [ShowIf(nameof(HasPlayer))]
        [ShowInInspector, HideLabel, DisplayAsString(EnableRichText = true)]
        [PropertyOrder(-10)]
        private string Headline =>
            $"<b><size=14>{_player.Stance}</size></b>   <color=#9AA0A6>{_player.State}</color>";

        // ---- Vitals ---------------------------------------------------------

        [BoxGroup("Vitals", centerLabel: true), ShowIf(nameof(HasPlayer))]
        [ProgressBar(0, nameof(MaxHealth), ColorGetter = nameof(HealthColor), Height = 22)]
        [ShowInInspector, LabelText("Health")]
        private float Health
        {
            get => HasPlayer && _player.Vitals != null ? _player.Vitals.Current : 0f;
            set { if (HasPlayer && _player.Vitals != null) _player.Vitals.SetHealth(value); }
        }

        private float MaxHealth =>
            HasPlayer && _player.Vitals != null ? _player.Vitals.Max : 100f;

        private Color HealthColor()
        {
            if (!HasPlayer || _player.Vitals == null) return Color.gray;
            var n = _player.Vitals.Normalized;
            if (n > 0.5f) return new Color(0.35f, 0.75f, 0.4f);
            return n > 0.25f ? new Color(0.9f, 0.72f, 0.25f) : new Color(0.85f, 0.3f, 0.3f);
        }

        [BoxGroup("Vitals"), ShowIf(nameof(HasPlayer))]
        [ButtonGroup("Vitals/Buttons")]
        [Button("Empty")] private void Empty() => _player.Vitals?.SetHealth(0f);

        [ButtonGroup("Vitals/Buttons")]
        [Button("-10")] private void Chip() =>
            _player.Vitals?.SetHealth(_player.Vitals.Current - 10f);

        [ButtonGroup("Vitals/Buttons")]
        [Button("Full")] private void Full() => _player.Vitals?.SetHealth(_player.Vitals.Max);

        [ButtonGroup("Vitals/Buttons")]
        [Button("Get up")] private void Revive() => _player.Revive();

        [BoxGroup("Vitals"), ShowIf(nameof(HasPlayer))]
        [ShowInInspector, ToggleLeft, LabelText("God mode")]
        private bool GodMode
        {
            get => HasPlayer && _player.Vitals != null && _player.Vitals.GodMode;
            set { if (HasPlayer && _player.Vitals != null) _player.Vitals.GodMode = value; }
        }

        // ---- Stance ---------------------------------------------------------

        [BoxGroup("Stance", centerLabel: true), ShowIf(nameof(HasPlayer))]
        [InfoBox("Switching from here goes through the same 0.4 second window " +
                 "the player pays for. There is no free switch, or the window " +
                 "stops being testable.")]
        [ButtonGroup("Stance/Pick")]
        [Button("Than", ButtonSizes.Large)] private void Than() => _player.TryStartSwitch(StanceId.Than);

        [ButtonGroup("Stance/Pick")]
        [Button("Se", ButtonSizes.Large)] private void Se() => _player.TryStartSwitch(StanceId.Se);

        [ButtonGroup("Stance/Pick")]
        [Button("In", ButtonSizes.Large)] private void In() => _player.TryStartSwitch(StanceId.In);

        [BoxGroup("Stance"), ShowIf(nameof(HasPlayer))]
        [ProgressBar(0, 1, ColorGetter = nameof(SwitchColor), Height = 16)]
        [ShowInInspector, LabelText("Vulnerable")]
        private float SwitchProgress
        {
            get
            {
                if (!HasPlayer || _player.Tuning == null) return 0f;
                var total = _player.Tuning.SwitchSeconds;
                return total <= 0f ? 0f : Mathf.Clamp01(_player.SwitchRemaining / total);
            }
        }

        private Color SwitchColor() =>
            SwitchProgress > 0f ? new Color(0.9f, 0.35f, 0.35f) : new Color(0.3f, 0.32f, 0.35f);

        [BoxGroup("Stance"), ShowIf(nameof(HasPlayer))]
        [ShowInInspector, DisplayAsString, LabelText("Stance intent")]
        private string StanceIntent =>
            HasPlayer && _player.CurrentStance != null ? _player.CurrentStance.intent : "-";

        // ---- Right now -------------------------------------------------------

        [BoxGroup("Right now", centerLabel: true), ShowIf(nameof(HasPlayer))]
        [ShowInInspector, DisplayAsString(EnableRichText = true), HideLabel]
        private string Flags
        {
            get
            {
                if (!HasPlayer) return "-";
                var vitals = _player.Vitals;
                return Chip("i-frames", vitals != null && vitals.Invincible, "#5BC8FF") +
                       Chip("super armor", vitals != null && vitals.SuperArmor, "#FFC24B") +
                       Chip("parry", vitals != null && vitals.Parrying, "#8CE68C") +
                       Chip("grounded", _player.Motor.Grounded, "#9AA0A6");
            }
        }

        private static string Chip(string label, bool on, string colour) =>
            on ? $"<color={colour}><b>[{label}]</b></color>  " : $"<color=#4A4D52>[{label}]</color>  ";

        [BoxGroup("Right now"), ShowIf(nameof(HasPlayer))]
        [ShowInInspector, DisplayAsString, LabelText("Ground speed")]
        private string SpeedReadout => HasPlayer ? $"{_player.Motor.GroundSpeed:0.00} m/s" : "-";

        [BoxGroup("Right now"), ShowIf(nameof(HasPlayer))]
        [ShowInInspector, DisplayAsString, LabelText("Position")]
        private string PositionReadout
        {
            get
            {
                if (!HasPlayer) return "-";
                var p = _player.Motor.transform.position;
                return $"x {p.x:0.00}   height {p.y:0.00}   depth {p.z:0.00}";
            }
        }

        [BoxGroup("Right now"), ShowIf(nameof(HasPlayer))]
        [ShowInInspector, DisplayAsString, LabelText("Dash cooldown")]
        private string DashReadout => HasPlayer ? $"{_player.DashCooldownRemaining * 1000f:0} ms" : "-";

        [BoxGroup("Right now"), ShowIf(nameof(HasPlayer))]
        [ShowInInspector, DisplayAsString, LabelText("Hit stop held")]
        private string HitStopReadout => HitStop.Held.ToString();

        // ---- Time ------------------------------------------------------------

        [BoxGroup("Time", centerLabel: true), ShowIf("@UnityEngine.Application.isPlaying")]
        [PropertyRange(0.05f, 1f)]
        [ShowInInspector, LabelText("Game speed")]
        [Tooltip("Slow motion for reading a single exchange frame by frame. " +
                 "Goes through StoryTimeScale, so pause still wins over it.")]
        private float GameSpeed
        {
            get => StoryKit.StoryTimeScale.Base;
            set => StoryKit.StoryTimeScale.SetBase(value);
        }

        [BoxGroup("Time"), ShowIf("@UnityEngine.Application.isPlaying")]
        [ButtonGroup("Time/Presets")]
        [Button("1x")] private void Speed1() => StoryKit.StoryTimeScale.SetBase(1f);

        [ButtonGroup("Time/Presets")]
        [Button("0.5x")] private void SpeedHalf() => StoryKit.StoryTimeScale.SetBase(0.5f);

        [ButtonGroup("Time/Presets")]
        [Button("0.15x")] private void SpeedSlow() => StoryKit.StoryTimeScale.SetBase(0.15f);

        // ---- Overlay ----------------------------------------------------------

        [BoxGroup("Overlay", centerLabel: true)]
        [ShowInInspector, ToggleLeft]
        [LabelText("Draw hitboxes in the game view")]
        private bool DrawHitboxes
        {
            get => CombatGizmos.Enabled;
            set => CombatGizmos.Enabled = value;
        }
    }
}
