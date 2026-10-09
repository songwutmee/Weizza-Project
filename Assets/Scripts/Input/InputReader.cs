using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Weizza
{
    /// <summary>
    /// Every button this game reads, in one place.
    ///
    /// Bindings are built in code rather than loaded from an .inputactions asset
    /// so that nothing about control ever depends on a serialized reference
    /// being wired up in a scene. Rebinding comes later and will replace the
    /// body of Build, not the shape of this class.
    ///
    /// It also registers itself as StoryKit's input source, so dialogue advances
    /// on the same button that attacks, and there is never a second opinion
    /// about what "confirm" means.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("Weizza/Input Reader")]
    public sealed class InputReader : MonoBehaviour, IStoryInput
    {
        private static InputReader _instance;

        /// <summary>The active reader, or null before the first one wakes up.</summary>
        public static InputReader Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        [BoxGroup("Live", centerLabel: true)]
        [ShowInInspector, ReadOnly] public Vector2 MoveValue { get; private set; }

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly] public bool JumpHeld { get; private set; }

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly] public string LastPressed { get; private set; } = "-";

        private InputAction _move;
        private InputAction _jump;
        private InputAction _light;
        private InputAction _heavy;
        private InputAction _dash;
        private InputAction _guard;
        private InputAction _interact;
        private InputAction _pause;
        private InputAction _log;
        private InputAction _sprint;
        private InputAction _point;
        private InputAction _click;
        private InputAction _stanceThan;
        private InputAction _stanceSe;
        private InputAction _stanceIn;
        private InputAction _stanceCycle;
        private InputAction _aimStick;
        private InputAction _aimHold;
        private InputAction _lockOn;
        private InputAction _map;

        private void Awake()
        {
            _instance = this;
            Build();
            InputBindings.Load(this);
        }

        private void OnEnable()
        {
            EnableAll(true);
            StoryInput.Source = this;
        }

        private void OnDisable()
        {
            EnableAll(false);
            if (ReferenceEquals(StoryInput.Source, this)) StoryInput.Source = null;
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void Update()
        {
            MoveValue = _move.ReadValue<Vector2>();
            JumpHeld = _jump.IsPressed();

            if (LightPressed) LastPressed = "Light";
            else if (HeavyPressed) LastPressed = "Heavy";
            else if (DashPressed) LastPressed = "Dash";
            else if (JumpPressed) LastPressed = "Jump";
            else if (GuardPressed) LastPressed = "Guard";
        }

        private void Build()
        {
            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _jump = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            _light = Button("Light", "<Mouse>/leftButton", "<Gamepad>/buttonWest");
            _heavy = Button("Heavy", "<Mouse>/rightButton", "<Gamepad>/buttonNorth");
            _dash = Button("Dash", "<Keyboard>/leftShift", "<Gamepad>/rightShoulder");
            _dash.AddBinding("<Keyboard>/rightShift");
            _guard = Button("Guard", "<Keyboard>/f", "<Gamepad>/leftShoulder");
            _interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonEast");
            _pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            _log = Button("Log", "<Keyboard>/j", "<Gamepad>/select");
            _sprint = Button("Sprint", "<Keyboard>/leftCtrl", "<Gamepad>/leftStickPress");

            _point = new InputAction("Point", InputActionType.Value, expectedControlType: "Vector2");
            _point.AddBinding("<Mouse>/position");

            // Deliberately the same control as Light. A click on an interactable
            // is a different intent from a swing, and giving each intent its own
            // action means the one place that has to arbitrate between them is
            // whatever the pointer is over, not this file.
            _click = new InputAction("Click", InputActionType.Button);
            _click.AddBinding("<Mouse>/leftButton");

            _stanceThan = Button("StanceThan", "<Keyboard>/1", "<Gamepad>/dpad/left");
            _stanceSe = Button("StanceSe", "<Keyboard>/2", "<Gamepad>/dpad/up");
            _stanceIn = Button("StanceIn", "<Keyboard>/3", "<Gamepad>/dpad/right");
            _stanceCycle = Button("StanceCycle", "<Keyboard>/q", "<Gamepad>/dpad/down");

            // Ranged stances aim with the right stick or the mouse. Holding aim
            // is the right mouse button on purpose: a ranged stance has no
            // heavy swing, so the button is free to mean "slow down and aim".
            _aimStick = new InputAction("AimStick", InputActionType.Value, expectedControlType: "Vector2");
            _aimStick.AddBinding("<Gamepad>/rightStick");
            _aimHold = Button("AimHold", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            _lockOn = Button("LockOn", "<Keyboard>/tab", "<Gamepad>/rightStickPress");

            // Hollow Knight holds the map on a shoulder. Here the shoulders are
            // dash and guard, so the right trigger, which nothing else uses.
            _map = Button("Map", "<Keyboard>/m", "<Gamepad>/rightTrigger");
        }

        private static InputAction Button(string name, string keyboard, string gamepad)
        {
            var action = new InputAction(name, InputActionType.Button);
            action.AddBinding(keyboard);
            action.AddBinding(gamepad);
            return action;
        }

        private void EnableAll(bool enabled)
        {
            foreach (var action in All())
            {
                if (action == null) continue;
                if (enabled) action.Enable();
                else action.Disable();
            }
        }

        private InputAction[] All() => new[]
        {
            _move, _jump, _light, _heavy, _dash, _guard, _interact, _pause, _log,
            _sprint, _point, _click, _stanceThan, _stanceSe, _stanceIn, _stanceCycle,
            _aimStick, _aimHold, _lockOn, _map,
        };

        // ---- Gameplay queries ---------------------------------------------

        public Vector2 MoveRaw => MoveValue;
        public bool JumpPressed => _jump != null && _jump.WasPressedThisFrame();
        public bool JumpReleased => _jump != null && _jump.WasReleasedThisFrame();
        public bool LightPressed => _light != null && _light.WasPressedThisFrame();
        public bool HeavyPressed => _heavy != null && _heavy.WasPressedThisFrame();
        public bool DashPressed => _dash != null && _dash.WasPressedThisFrame();
        public bool GuardPressed => _guard != null && _guard.WasPressedThisFrame();
        public bool GuardHeld => _guard != null && _guard.IsPressed();
        public bool LightHeld => _light != null && _light.IsPressed();
        public bool LightReleased => _light != null && _light.WasReleasedThisFrame();
        public bool DownHeld => MoveValue.y < -0.5f;

        /// <summary>Right stick, zero when there is no pad.</summary>
        public Vector2 AimStick => _aimStick != null ? _aimStick.ReadValue<Vector2>() : Vector2.zero;
        public bool AimHeld => _aimHold != null && _aimHold.IsPressed();
        public bool AimPressed => _aimHold != null && _aimHold.WasPressedThisFrame();
        public bool AimReleased => _aimHold != null && _aimHold.WasReleasedThisFrame();
        public bool LockOnPressed => _lockOn != null && _lockOn.WasPressedThisFrame();
        public bool MapPressed => _map != null && _map.WasPressedThisFrame();
        public bool MapHeld => _map != null && _map.IsPressed();

        /// <summary>Every action this reader owns, for the rebinding screen.</summary>
        public InputAction[] Actions => All();

        /// <summary>Only the three real schools. Normal is a start state, not a slot to cycle back into.</summary>
        private static readonly StanceId[] RealSchools = { StanceId.Than, StanceId.Se, StanceId.In };

        /// <summary>
        /// The stance the player asked for this frame, or null. Cycle steps
        /// forward through the three real schools so a pad without a dpad
        /// still works. There is no button for Normal: it is what the
        /// protagonist starts as, not one more choice on the wheel.
        /// </summary>
        public StanceId? StanceRequest(StanceId current)
        {
            if (_stanceThan != null && _stanceThan.WasPressedThisFrame()) return StanceId.Than;
            if (_stanceSe != null && _stanceSe.WasPressedThisFrame()) return StanceId.Se;
            if (_stanceIn != null && _stanceIn.WasPressedThisFrame()) return StanceId.In;

            if (_stanceCycle == null || !_stanceCycle.WasPressedThisFrame()) return null;

            var index = System.Array.IndexOf(RealSchools, current);
            return RealSchools[(index + 1) % RealSchools.Length];
        }

        // ---- IStoryInput ---------------------------------------------------

        public bool AdvancePressed => LightPressed || (_interact != null && _interact.WasPressedThisFrame());
        public bool InteractPressed => _interact != null && _interact.WasPressedThisFrame();

        /// <summary>Pointer position in screen pixels. Zero when there is no mouse.</summary>
        public Vector2 PointerPosition => _point != null ? _point.ReadValue<Vector2>() : Vector2.zero;

        public bool PointerPressed => _click != null && _click.WasPressedThisFrame();
        public bool PausePressed => _pause != null && _pause.WasPressedThisFrame();
        public bool ToggleLogPressed => _log != null && _log.WasPressedThisFrame();
        public Vector2 Move => MoveValue;
        public Vector2 Look => AimStick;
        public bool Jump => JumpHeld;
        public bool Sprint => _sprint != null && _sprint.IsPressed();
        public bool Fire => _light != null && _light.IsPressed();
        public bool Aim => GuardHeld;
    }
}
