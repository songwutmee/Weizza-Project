using System.Collections.Generic;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.InputSystem;
using Weizza.Narrative;

namespace Weizza
{
    /// <summary>
    /// The in game half of the debug tooling: the same numbers as the editor
    /// window, but readable while your hands are on the controls and present in
    /// a build, so a playtester can be asked what their screen said.
    ///
    /// Toggled with F1. Deliberately IMGUI, not Odin: Odin's editor widgets
    /// (ProgressBar, ButtonGroup, the whole styled look of the Weizza Debug
    /// Window) live in an editor-only assembly and simply do not exist in a
    /// build. This panel has to survive a build, so everything drawn here is
    /// hand rolled GUI.DrawTexture calls dressed up to look like the real thing.
    ///
    /// For a fuller, actually Odin-rendered view while working in the editor,
    /// use Weizza to Debug Window (Ctrl+Shift+W) instead. That one gets the
    /// real widgets because it never has to run in a build.
    /// </summary>
    [AddComponentMenu("Weizza/Debug Panel")]
    [DefaultExecutionOrder(1000)]
    public sealed class DebugPanel : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [SerializeField] private bool visibleAtStart = true;

        [BoxGroup("Setup")]
        [Tooltip("Also draw hit and hurt boxes over the game view.")]
        [SerializeField] private bool boxesAtStart;

        [BoxGroup("Setup")]
        [SerializeField] private Key toggleKey = Key.F1;

        [BoxGroup("Setup")]
        [SerializeField] private Key boxKey = Key.F2;

        [BoxGroup("Setup")]
        [MinValue(220f)]
        [SerializeField] private float panelWidth = 320f;

        private const float Pad = 12f;
        private const float Gap = 6f;
        private const float BarHeight = 16f;
        private const float ChipHeight = 20f;

        private static readonly Color Ink = new(0.93f, 0.94f, 0.96f);
        private static readonly Color Muted = new(0.58f, 0.61f, 0.68f);
        private static readonly Color PanelBack = new(0.07f, 0.075f, 0.09f, 0.92f);
        private static readonly Color CardBack = new(1f, 1f, 1f, 0.045f);
        private static readonly Color HairlineColour = new(1f, 1f, 1f, 0.09f);
        private static readonly Color Accent = new(0.55f, 0.68f, 0.95f);
        private static readonly Color Danger = new(0.86f, 0.32f, 0.35f);
        private static readonly Color Off = new(1f, 1f, 1f, 0.06f);

        private PlayerController _player;
        private float _nextScan;
        private bool _visible;

        private GUIStyle _h1;
        private GUIStyle _h2;
        private GUIStyle _label;
        private GUIStyle _mono;
        private GUIStyle _button;
        private GUIStyle _buttonActive;
        private GUIStyle _toggle;
        private Texture2D _white;

        private void Awake()
        {
            _visible = visibleAtStart;
            CombatGizmos.Enabled = boxesAtStart;
            _white = Texture2D.whiteTexture;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[toggleKey].wasPressedThisFrame) _visible = !_visible;
                if (keyboard[boxKey].wasPressedThisFrame) CombatGizmos.Enabled = !CombatGizmos.Enabled;
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.5f;
            if (_player == null) _player = FindFirstObjectByType<PlayerController>();
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (CombatGizmos.Enabled) DrawBoxes();
            if (!_visible) return;

            // A conversation, or the item reveal card, already claims the
            // screen and both already gate against each other the same way.
            // Left alone the debug panel sat on top of the dialogue box every
            // time, which is exactly the kind of thing a playtester assumes is
            // a real UI bug rather than a stats overlay that forgot to get out
            // of the way.
            if (DialogueDirector.Talking || ItemRevealScreen.Showing) return;

            var stanceTint = _player != null && _player.CurrentStance != null
                ? _player.CurrentStance.tint
                : Accent;

            GUILayout.BeginArea(new Rect(Pad, Pad, panelWidth, Screen.height - Pad * 2f));
            Panel(PanelBack, () =>
            {
                Header(stanceTint);

                if (_player == null)
                {
                    GUILayout.Space(Gap);
                    MutedLine("No PlayerController in the scene.");
                }
                else
                {
                    DrawVitals(stanceTint);
                    DrawStance();
                    DrawStatus();
                    DrawNumbers();
                }

                DrawTime();
            });
            GUILayout.EndArea();
        }

        // ---- Sections ---------------------------------------------------------

        private void Header(Color stanceTint)
        {
            GUILayout.BeginHorizontal();
            var barRect = GUILayoutUtility.GetRect(4f, 20f, GUILayout.Width(4f));
            Fill(barRect, stanceTint);
            GUILayout.Space(8f);
            GUILayout.BeginVertical();
            GUILayout.Label("WEIZZA", _h1);
            GUILayout.Label($"F1 panel  ·  F2 boxes", _label);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            Hairline();
        }

        private void DrawVitals(Color stanceTint)
        {
            Card(() =>
            {
                GUILayout.BeginHorizontal();
                var previous = GUI.color;
                GUI.color = stanceTint;
                GUILayout.Label(_player.Stance.ToString().ToUpperInvariant(), _h2);
                GUI.color = previous;
                GUILayout.FlexibleSpace();
                Chip(_player.State.ToString(), Muted, subtle: true);
                GUILayout.EndHorizontal();

                var vitals = _player.Vitals;
                if (vitals == null)
                {
                    MutedLine("No Vitals component on the player.");
                    return;
                }

                GUILayout.Space(Gap);
                Bar("Health", vitals.Normalized, HealthColour(vitals.Normalized),
                    $"{vitals.Current:0} / {vitals.Max:0}");

                var tuning = _player.Tuning;
                if (tuning != null && tuning.SwitchSeconds > 0f)
                {
                    var fraction = Mathf.Clamp01(_player.SwitchRemaining / tuning.SwitchSeconds);
                    Bar("Vulnerable", fraction, Danger,
                        fraction > 0f ? $"{_player.SwitchRemaining * 1000f:0} ms" : "ready");
                }
            });
        }

        private void DrawStance()
        {
            Card(() =>
            {
                MutedLine("STANCE");
                GUILayout.Space(2f);

                // Normal is a real stance the player starts in, so it belongs in
                // the row. Leaving it out meant that standing in Normal lit
                // nothing up at all, which read as the panel being broken.
                GUILayout.BeginHorizontal();
                StanceButton(StanceId.Normal);
                GUILayout.Space(4f);
                StanceButton(StanceId.Than);
                GUILayout.Space(4f);
                StanceButton(StanceId.Se);
                GUILayout.Space(4f);
                StanceButton(StanceId.In);
                GUILayout.EndHorizontal();

                DrawSwitchWindow();

                var vitals = _player.Vitals;
                if (vitals == null) return;

                GUILayout.Space(Gap);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-10", _button)) vitals.SetHealth(vitals.Current - 10f);
                if (GUILayout.Button("Empty", _button)) vitals.SetHealth(0f);
                if (GUILayout.Button("Full", _button)) vitals.SetHealth(vitals.Max);
                if (GUILayout.Button("Get up", _button)) _player.Revive();
                GUILayout.EndHorizontal();

                GUILayout.Space(2f);
                vitals.GodMode = GUILayout.Toggle(vitals.GodMode, " God mode", _toggle);
            });
        }

        private readonly Dictionary<Color, GUIStyle> _tintedButtons = new();

        private void StanceButton(StanceId id)
        {
            var definition = _player.Tuning != null ? _player.Tuning.Get(id) : null;
            var isCurrent = _player.Stance == id;

            // A stance with no asset behind it silently does nothing when
            // pressed, which is indistinguishable from a broken button. Saying
            // so on the button itself is the whole reason this branch exists.
            if (definition == null)
            {
                var was = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUILayout.Button($"{id}\nno asset", _button, GUILayout.Height(34f));
                GUI.color = was;
                return;
            }

            if (!isCurrent)
            {
                if (GUILayout.Button(id.ToString(), _button, GUILayout.Height(34f)))
                {
                    _player.TryStartSwitch(id);
                }
                return;
            }

            // The active stance is filled with its own tint rather than one
            // fixed accent colour, so the panel and the world agree on what
            // each school looks like. The brackets are there for the same
            // reason a colour alone is not enough: on a small screen, in a
            // hurry, shape reads faster than hue. Plain ASCII on purpose, since
            // the built in IMGUI font cannot be trusted with symbols.
            var label = $"[ {id} ]";
            if (GUILayout.Button(label, TintedButtonStyle(definition.tint), GUILayout.Height(34f)))
            {
                _player.TryStartSwitch(id);
            }
        }

        /// <summary>
        /// Draws the switch window while it is open.
        ///
        /// The 0.4 seconds with no armour and no i-frames is the price of being
        /// the only person alive who can change school mid fight. It is also
        /// invisible, so tuning it without a readout is guesswork.
        /// </summary>
        private void DrawSwitchWindow()
        {
            var remaining = _player.SwitchRemaining;
            if (remaining <= 0f) return;

            var total = _player.Tuning != null ? _player.Tuning.SwitchSeconds : 0.4f;
            var fraction = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

            GUILayout.Space(4f);

            var rect = GUILayoutUtility.GetRect(1f, 4f, GUILayout.ExpandWidth(true));
            Fill(rect, Off);

            var filled = rect;
            filled.width *= fraction;
            Fill(filled, Danger);

            GUILayout.Space(2f);
            var was = GUI.color;
            GUI.color = Danger;
            GUILayout.Label($"vulnerable  {remaining * 1000f:0} ms", _mono);
            GUI.color = was;
        }

        private GUIStyle TintedButtonStyle(Color tint)
        {
            if (_tintedButtons.TryGetValue(tint, out var style)) return style;

            var textColour = tint.grayscale > 0.6f ? Color.black : Ink;
            style = new GUIStyle(_buttonActive)
            {
                normal = { textColor = textColour, background = Solid(tint) },
                hover = { textColor = textColour, background = Solid(tint) },
                active = { textColor = textColour, background = Solid(tint) },
            };
            _tintedButtons[tint] = style;
            return style;
        }

        private void DrawStatus()
        {
            var chips = ActiveChips();
            if (chips.Count == 0) return;

            Card(() =>
            {
                GUILayout.BeginHorizontal();
                foreach (var (label, colour) in chips)
                {
                    Chip(label, colour, subtle: false);
                    GUILayout.Space(4f);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            });
        }

        private List<(string label, Color colour)> ActiveChips()
        {
            var chips = new List<(string, Color)>(5);
            var vitals = _player.Vitals;

            if (vitals != null)
            {
                if (vitals.Invincible) chips.Add(("i-frames", new Color(0.4f, 0.78f, 1f)));
                if (vitals.SuperArmor) chips.Add(("super armor", new Color(1f, 0.76f, 0.35f)));
                if (vitals.Parrying) chips.Add(("parry", new Color(0.55f, 0.9f, 0.55f)));
            }
            if (!_player.Motor.Grounded) chips.Add(("airborne", Muted));
            if (HitStop.Held > 0) chips.Add(("hit stop", Danger));

            return chips;
        }

        private void DrawNumbers()
        {
            Card(() =>
            {
                var motor = _player.Motor;
                var lane = ViewRig.LaneCount > 1
                    ? $"{motor.Lane} of {ViewRig.LaneCount - 1}"
                    : "single";
                Row("speed", $"{motor.GroundSpeed:0.00} m/s", "lane", lane);
                Row("height", $"{motor.transform.position.y:0.00}", "dash cd",
                    $"{_player.DashCooldownRemaining * 1000f:0} ms");
            });
        }

        private void Row(string labelA, string valueA, string labelB, string valueB)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(labelA, _label, GUILayout.Width(52f));
            GUILayout.Label(valueA, _mono, GUILayout.Width(84f));
            GUILayout.Label(labelB, _label, GUILayout.Width(48f));
            GUILayout.Label(valueB, _mono);
            GUILayout.EndHorizontal();
        }

        private void DrawTime()
        {
            Card(() =>
            {
                MutedLine("TIME");
                GUILayout.Space(2f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("1x", _button)) StoryTimeScale.SetBase(1f);
                if (GUILayout.Button("0.5x", _button)) StoryTimeScale.SetBase(0.5f);
                if (GUILayout.Button("0.15x", _button)) StoryTimeScale.SetBase(0.15f);
                GUILayout.EndHorizontal();
            });
        }

        // ---- Small drawing primitives -----------------------------------------

        /// <summary>
        /// A GUIStyle background stretches to fill whatever the layout group
        /// it is attached to ends up measuring, so the whole panel and each
        /// card get a solid backdrop without a manual two-pass measure step.
        /// </summary>
        private void Panel(Color back, System.Action content)
        {
            GUILayout.BeginVertical(PanelStyle(back));
            content();
            GUILayout.EndVertical();
        }

        private void Card(System.Action content)
        {
            GUILayout.Space(Gap);
            GUILayout.BeginVertical(CardStyle());
            content();
            GUILayout.EndVertical();
        }

        private GUIStyle _panelStyle;
        private GUIStyle _cardStyle;

        private GUIStyle PanelStyle(Color back) => _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            normal = { background = Solid(back) },
            padding = new RectOffset(14, 14, 12, 12),
            margin = new RectOffset(0, 0, 0, 0),
        };

        private GUIStyle CardStyle() => _cardStyle ??= new GUIStyle(GUI.skin.box)
        {
            normal = { background = Solid(CardBack) },
            padding = new RectOffset(10, 10, 8, 8),
            margin = new RectOffset(0, 0, 0, 0),
        };

        private void Hairline()
        {
            GUILayout.Space(6f);
            var rect = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            Fill(rect, HairlineColour);
        }

        private void MutedLine(string text)
        {
            var previous = GUI.color;
            GUILayout.Label(text, _label);
            GUI.color = previous;
        }

        private void Chip(string text, Color colour, bool subtle)
        {
            var content = new GUIContent(text);
            var size = _mono.CalcSize(content);
            var rect = GUILayoutUtility.GetRect(size.x + 14f, ChipHeight);

            Fill(rect, subtle ? new Color(colour.r, colour.g, colour.b, 0.12f)
                              : new Color(colour.r, colour.g, colour.b, 0.22f));

            var previous = GUI.color;
            GUI.color = subtle ? Muted : colour;
            GUI.Label(rect, text, _chipLabel ??= new GUIStyle(_mono)
            {
                alignment = TextAnchor.MiddleCenter,
            });
            GUI.color = previous;
        }

        private GUIStyle _chipLabel;

        private void Bar(string label, float fraction, Color colour, string suffix)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(suffix, _mono);
            GUILayout.EndHorizontal();

            var rect = GUILayoutUtility.GetRect(10f, BarHeight, GUILayout.ExpandWidth(true));
            Fill(rect, Off);

            var fillRect = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height);
            Fill(fillRect, colour);

            GUILayout.Space(3f);
        }

        private void Fill(Rect rect, Color colour)
        {
            var previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
        }

        private static Texture2D Solid(Color colour)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, colour);
            texture.Apply();
            return texture;
        }

        // ---- Overlay boxes ------------------------------------------------------

        private void DrawBoxes()
        {
            var camera = Camera.main;
            if (camera == null) return;

            foreach (var body in CombatGizmos.Bodies)
            {
                if (body == null) continue;
                DrawWorldBox(camera, body.bounds.center, body.bounds.size,
                    new Color(0.35f, 0.7f, 1f, 0.55f));
            }

            foreach (var emitter in CombatGizmos.Emitters)
            {
                if (emitter == null) continue;
                if (!emitter.TryGetBox(out var center, out var size)) continue;
                DrawWorldBox(camera, center, size, new Color(1f, 0.25f, 0.25f, 0.75f));
            }
        }

        /// <summary>
        /// Projects the eight corners and boxes them in. A 3D box seen through a
        /// tilted camera is not a rectangle on screen, so taking two corners
        /// would draw something smaller than what the game actually tested.
        /// </summary>
        private void DrawWorldBox(Camera camera, Vector3 centre, Vector3 size, Color colour)
        {
            var half = size * 0.5f;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            for (var i = 0; i < 8; i++)
            {
                var corner = centre + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);

                var screen = camera.WorldToScreenPoint(corner);
                min = Vector2.Min(min, new Vector2(screen.x, screen.y));
                max = Vector2.Max(max, new Vector2(screen.x, screen.y));
            }

            var rect = new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
            const float t = 1.5f;
            Fill(new Rect(rect.x, rect.y, rect.width, t), colour);
            Fill(new Rect(rect.x, rect.yMax - t, rect.width, t), colour);
            Fill(new Rect(rect.x, rect.y, t, rect.height), colour);
            Fill(new Rect(rect.xMax - t, rect.y, t, rect.height), colour);
        }

        private static Color HealthColour(float normalized)
        {
            if (normalized > 0.5f) return new Color(0.4f, 0.8f, 0.45f);
            return normalized > 0.25f
                ? new Color(0.95f, 0.75f, 0.3f)
                : new Color(0.9f, 0.35f, 0.35f);
        }

        // ---- Styles ------------------------------------------------------------

        private void EnsureStyles()
        {
            if (_h1 != null) return;

            var font = GUI.skin.font;

            _h1 = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Ink },
            };

            _h2 = new GUIStyle(_h1) { fontSize = 14 };

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = Muted },
            };

            _mono = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = Ink },
            };

            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                normal = { textColor = Ink, background = Solid(new Color(1f, 1f, 1f, 0.08f)) },
                hover = { textColor = Ink, background = Solid(new Color(1f, 1f, 1f, 0.14f)) },
                active = { textColor = Ink, background = Solid(new Color(1f, 1f, 1f, 0.2f)) },
                fixedHeight = 24f,
                margin = new RectOffset(0, 4, 2, 2),
            };

            _buttonActive = new GUIStyle(_button)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.black, background = Solid(Accent) },
                hover = { textColor = Color.black, background = Solid(Accent) },
                active = { textColor = Color.black, background = Solid(Accent) },
            };

            _toggle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 11,
                normal = { textColor = Muted },
                onNormal = { textColor = Ink },
            };
        }
    }
}
