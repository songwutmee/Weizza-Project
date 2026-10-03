using System;
using System.Collections.Generic;
using StoryKit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Weizza
{
    /// <summary>
    /// Settings, drawn on the HUD's UI Toolkit panel: volumes, screen shake,
    /// flash reduction, damage numbers, frame data, and key rebinding.
    ///
    /// Opens with F10 (or the "settings" console command) until the pause
    /// flipbook art has buttons drawn on it to open it from. While open the
    /// game is stopped through StoryTimeScale and the player is locked through
    /// StoryControl, the same two owners every other overlay borrows from.
    /// </summary>
    [AddComponentMenu("Weizza/Settings Screen")]
    public sealed class SettingsScreen : MonoBehaviour
    {
        /// <summary>True while the screen is up. The pause menu checks this before reacting to Escape.</summary>
        public static bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsOpen = false;

        private static readonly string[] RebindableActions =
        {
            "Jump", "Light", "Heavy", "Dash", "Interact", "StanceThan", "StanceSe", "StanceIn", "StanceCycle", "AimHold",
        };

        private VisualElement _panel;
        private IDisposable _lock;
        private bool _stopped;
        private readonly List<(Button button, InputAction action, int index)> _rebindButtons = new();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f10Key.wasPressedThisFrame) Toggle();
            if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !_rebinding) Close();
        }

        private bool _rebinding;

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            var hud = HudScreen.Instance;
            if (IsOpen || hud == null || hud.Root == null) return;

            IsOpen = true;
            _lock = StoryControl.Lock("Settings");
            StoryTimeScale.Stop();
            _stopped = true;
            StoryUi.ReleaseCursor();

            _panel = BuildPanel();
            hud.Root.Add(_panel);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _panel?.RemoveFromHierarchy();
            _panel = null;
            _rebindButtons.Clear();
            _lock?.Dispose();
            _lock = null;
            if (_stopped) StoryTimeScale.Resume();
            _stopped = false;
            StoryUi.RestoreCursor();
        }

        private void OnDisable() => Close();

        // ---- Build -----------------------------------------------------------------------

        private VisualElement BuildPanel()
        {
            var shade = new VisualElement();
            shade.style.position = Position.Absolute;
            shade.style.left = 0;
            shade.style.right = 0;
            shade.style.top = 0;
            shade.style.bottom = 0;
            shade.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            shade.style.alignItems = Align.Center;
            shade.style.justifyContent = Justify.Center;

            var card = new ScrollView();
            card.style.width = 620;
            card.style.maxHeight = 860;
            card.style.backgroundColor = new Color(0.07f, 0.09f, 0.08f, 0.96f);
            card.style.paddingLeft = 32;
            card.style.paddingRight = 32;
            card.style.paddingTop = 24;
            card.style.paddingBottom = 24;
            card.style.borderTopLeftRadius = 10;
            card.style.borderTopRightRadius = 10;
            card.style.borderBottomLeftRadius = 10;
            card.style.borderBottomRightRadius = 10;
            shade.Add(card);

            card.Add(Heading("Settings", 30));

            card.Add(Heading("Sound", 20));
            card.Add(SliderRow("Master", GameSettings.MasterVolume, v => GameSettings.MasterVolume = v));
            card.Add(SliderRow("Music", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v));
            card.Add(SliderRow("Effects", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v));
            card.Add(SliderRow("Ambience", GameSettings.AmbienceVolume, v => GameSettings.AmbienceVolume = v));
            card.Add(SliderRow("Voice", GameSettings.VoiceVolume, v => GameSettings.VoiceVolume = v));

            card.Add(Heading("Comfort", 20));
            card.Add(SliderRow("Screen shake", GameSettings.ShakeScale, v => GameSettings.ShakeScale = v, 0f, 1.5f));
            card.Add(ToggleRow("Softer flashes", GameSettings.ReduceFlashes, v => GameSettings.ReduceFlashes = v));
            card.Add(ToggleRow("Damage numbers", GameSettings.DamageNumbers, v => GameSettings.DamageNumbers = v));
            card.Add(ToggleRow("Frame data overlay", GameSettings.FrameDataOverlay, v => GameSettings.FrameDataOverlay = v));

            card.Add(Heading("Display", 20));
            card.Add(ToggleRow("Fullscreen", GameSettings.Fullscreen, v => GameSettings.Fullscreen = v));
            card.Add(ToggleRow("VSync", GameSettings.VSync, v => GameSettings.VSync = v));
            card.Add(ResolutionRow());

            card.Add(Heading("Language", 20));
            var languages = new VisualElement();
            languages.style.flexDirection = FlexDirection.Row;
            foreach (var code in new[] { "th", "en" })
            {
                var button = new Button(() => SetLanguage(code)) { text = code == "th" ? "ไทย" : "English" };
                button.style.marginRight = 8;
                languages.Add(button);
            }
            card.Add(languages);

            card.Add(Heading("Controls (keyboard)", 20));
            var reader = InputReader.Instance;
            if (reader != null)
            {
                foreach (var action in reader.Actions)
                {
                    if (action == null || Array.IndexOf(RebindableActions, action.name) < 0) continue;
                    card.Add(RebindRow(reader, action));
                }
                card.Add(new Button(() =>
                {
                    InputBindings.ResetAll(reader);
                    RefreshRebindLabels();
                }) { text = "Reset controls" });
            }

            var close = new Button(Close) { text = "Close" };
            close.style.marginTop = 18;
            close.style.height = 36;
            card.Add(close);

            return shade;
        }

        // The default theme's dark label text disappears on the dark card.
        private static readonly Color LabelColour = new(0.86f, 0.84f, 0.78f);

        private static Label Heading(string text, int size)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = new Color(0.94f, 0.9f, 0.82f);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = size > 24 ? 0 : 14;
            label.style.marginBottom = 6;
            return label;
        }

        private static VisualElement SliderRow(string label, float value, Action<float> set, float min = 0f, float max = 1f)
        {
            var slider = new Slider(label, min, max) { value = value, showInputField = false };
            slider.style.marginBottom = 4;
            slider.labelElement.style.color = LabelColour;
            slider.RegisterValueChangedCallback(e => set(e.newValue));
            return slider;
        }

        // Every size the screen offers, once each (the list repeats per refresh rate),
        // with the screen's own size first as "Native".
        private static VisualElement ResolutionRow()
        {
            var choices = new List<string> { "Native" };
            foreach (var r in Screen.resolutions)
            {
                var text = $"{r.width} x {r.height}";
                if (!choices.Contains(text)) choices.Add(text);
            }

            var current = GameSettings.Resolution;
            var shown = current.x > 0 ? $"{current.x} x {current.y}" : "Native";
            if (!choices.Contains(shown)) shown = "Native";

            var field = new DropdownField("Resolution", choices, shown);
            field.labelElement.style.color = LabelColour;
            field.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == "Native") { GameSettings.SetResolution(0, 0); return; }
                var parts = e.newValue.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var w) && int.TryParse(parts[1].Trim(), out var h))
                    GameSettings.SetResolution(w, h);
            });
            return field;
        }

        private static VisualElement ToggleRow(string label, bool value, Action<bool> set)
        {
            var toggle = new Toggle(label) { value = value };
            toggle.labelElement.style.color = LabelColour;
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            return toggle;
        }

        private VisualElement RebindRow(InputReader reader, InputAction action)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 4;

            var name = new Label(action.name);
            name.style.color = new Color(0.85f, 0.85f, 0.8f);
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(name);

            // The first non-composite binding is the keyboard one for every
            // action the reader builds with its Button helper.
            var index = 0;
            var button = new Button { text = InputBindings.Display(action, index) };
            button.style.minWidth = 180;
            button.clicked += () =>
            {
                _rebinding = true;
                button.text = "press a key...";
                InputBindings.Rebind(reader, action, index, _ =>
                {
                    _rebinding = false;
                    RefreshRebindLabels();
                });
            };
            _rebindButtons.Add((button, action, index));
            row.Add(button);
            return row;
        }

        private void RefreshRebindLabels()
        {
            foreach (var (button, action, index) in _rebindButtons) button.text = InputBindings.Display(action, index);
        }

        private static void SetLanguage(string code)
        {
            GameSettings.Language = code;
            var localization = StoryServices.Get<ILocalizationService>();
            if (localization != null) localization.ActiveLanguage = code;
        }
    }
}
