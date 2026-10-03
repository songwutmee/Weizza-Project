using System.Collections.Generic;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Weizza
{
    /// <summary>
    /// The world map, Hollow Knight style: rooms you have walked through, drawn as
    /// their real rectangles, with shrines and doors as pins and the player as a
    /// dot. Hold the map button to look, let go to put it away.
    ///
    /// Drawn with the UI Toolkit vector painter straight from the Room Definitions,
    /// so nobody ever paints a map image and it can never disagree with the level.
    /// A hand drawn map skin can sit on top later, the rectangles stay as the truth.
    ///
    /// Auto-added by HudScreen. Silent when there is no WorldAtlas.
    /// </summary>
    [AddComponentMenu("Weizza/UI/Map Screen")]
    public sealed class MapScreen : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsOpen = false;

        [BoxGroup("Behaviour")]
        [ToggleLeft]
        [Tooltip("On: hold the button to look. Off: press to open, press again to close.")]
        [SerializeField] private bool holdToView = true;

        [BoxGroup("Behaviour")]
        [ToggleLeft]
        [Tooltip("Hollow Knight hides your own position until you own a compass. On means the dot needs the blackboard key below.")]
        [SerializeField] private bool playerDotNeedsCompass;

        [BoxGroup("Behaviour")]
        [ShowIf(nameof(playerDotNeedsCompass))]
        [SerializeField] private string compassKey = "map.compass";

        [BoxGroup("Look")]
        [SerializeField] private Color backdrop = new(0.04f, 0.05f, 0.05f, 0.82f);

        [BoxGroup("Look")]
        [SerializeField] private Color outline = new(0.92f, 0.88f, 0.78f, 0.9f);

        [BoxGroup("Look")]
        [SerializeField] private Color playerColour = new(1f, 0.92f, 0.55f);

        [BoxGroup("Look")]
        [PropertyRange(0.05f, 0.4f)]
        [SerializeField] private float padding = 0.12f;

        private VisualElement _overlay;
        private VisualElement _canvas;
        private Label _title;
        private Label _hint;
        private readonly List<RoomDefinition> _rooms = new();
        private Rect _worldView;
        private float _openedAt;

        private void Update()
        {
            var input = InputReader.Instance;
            var atlas = WorldAtlas.Load();
            if (input == null || atlas == null) return;

            if (holdToView)
            {
                if (input.MapHeld && !IsOpen && CanOpen()) Open(atlas);
                else if (!input.MapHeld && IsOpen) Close();
            }
            else if (input.MapPressed)
            {
                if (IsOpen) Close();
                else if (CanOpen()) Open(atlas);
            }

            if (IsOpen) _canvas?.MarkDirtyRepaint();
        }

        private static bool CanOpen() =>
            !SettingsScreen.IsOpen && !Narrative.DialogueDirector.Talking && !StoryControl.MovementLocked;

        private void Open(WorldAtlas atlas)
        {
            var hud = HudScreen.Instance;
            if (hud == null || hud.Root == null) return;
            if (_overlay == null || _overlay.parent != hud.Root) Build(hud.Root);

            MapService.CollectRevealed(atlas, _rooms);
            _worldView = Pad(MapService.RevealedBounds(_rooms));

            var current = WorldStreamer.Instance != null ? WorldStreamer.Instance.Current : null;
            _title.text = current != null ? Loc.T(current.displayNameKey, current.id) : string.Empty;
            _hint.text = _rooms.Count == 0 ? Loc.T("UI_MAP_EMPTY", "Nothing mapped yet") : string.Empty;

            _overlay.style.display = DisplayStyle.Flex;
            _openedAt = Time.unscaledTime;
            IsOpen = true;
            StoryEvents.Raise(GameEvents.MapOpened, current != null ? current.id : string.Empty);
        }

        private void Close()
        {
            IsOpen = false;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        }

        private void OnDisable() => Close();

        private Rect Pad(Rect r)
        {
            var pad = Mathf.Max(r.width, r.height) * padding;
            return new Rect(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f);
        }

        // ---- Build ----------------------------------------------------------------

        private void Build(VisualElement root)
        {
            _overlay = new VisualElement { name = "map-overlay", pickingMode = PickingMode.Ignore };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.right = 0; _overlay.style.top = 0; _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = backdrop;
            _overlay.style.display = DisplayStyle.None;

            _title = new Label();
            _title.style.position = Position.Absolute;
            _title.style.top = 28; _title.style.left = 0; _title.style.right = 0;
            _title.style.unityTextAlign = TextAnchor.MiddleCenter;
            _title.style.fontSize = 30;
            _title.style.color = outline;
            _overlay.Add(_title);

            _hint = new Label();
            _hint.style.position = Position.Absolute;
            _hint.style.bottom = 28; _hint.style.left = 0; _hint.style.right = 0;
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hint.style.fontSize = 18;
            _hint.style.color = new Color(outline.r, outline.g, outline.b, 0.6f);
            _overlay.Add(_hint);

            _canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            _canvas.style.position = Position.Absolute;
            _canvas.style.left = Length.Percent(6); _canvas.style.right = Length.Percent(6);
            _canvas.style.top = Length.Percent(12); _canvas.style.bottom = Length.Percent(10);
            _canvas.generateVisualContent += Draw;
            _overlay.Add(_canvas);

            root.Add(_overlay);
        }

        // ---- Draw -----------------------------------------------------------------

        private void Draw(MeshGenerationContext ctx)
        {
            var area = _canvas.contentRect;
            if (area.width < 10f || area.height < 10f || _worldView.width <= 0f) return;

            // One uniform scale so rooms keep their real shape.
            var scale = Mathf.Min(area.width / _worldView.width, area.height / _worldView.height);
            var offset = new Vector2(
                (area.width - _worldView.width * scale) * 0.5f,
                (area.height - _worldView.height * scale) * 0.5f);

            Vector2 ToPanel(Vector2 world) => new(
                offset.x + (world.x - _worldView.xMin) * scale,
                // Panel y grows downward, world y grows up.
                offset.y + (_worldView.yMax - world.y) * scale);

            var p = ctx.painter2D;
            var current = WorldStreamer.Instance != null ? WorldStreamer.Instance.Current : null;

            // A gentle fade in, so the map does not slam onto the screen.
            var fade = Mathf.Clamp01((Time.unscaledTime - _openedAt) / 0.15f);

            foreach (var room in _rooms)
            {
                var r = room.worldRect;
                var a = ToPanel(new Vector2(r.xMin, r.yMax));
                var b = ToPanel(new Vector2(r.xMax, r.yMin));
                var fill = room.mapColour;
                fill.a = (room == current ? 0.75f : 0.38f) * fade;
                var line = outline;
                line.a *= fade;

                p.fillColor = fill;
                p.strokeColor = line;
                p.lineWidth = room == current ? 3f : 1.5f;
                p.BeginPath();
                p.MoveTo(a);
                p.LineTo(new Vector2(b.x, a.y));
                p.LineTo(b);
                p.LineTo(new Vector2(a.x, b.y));
                p.ClosePath();
                p.Fill();
                p.Stroke();

                foreach (var pin in room.pins)
                {
                    if (!MapService.PinVisible(pin)) continue;
                    DrawPin(p, ToPanel(pin.position), pin.kind, fade);
                }
            }

            var player = PlayerLocator.Player;
            var showDot = !playerDotNeedsCompass || StoryState.Global.Get(compassKey, false);
            if (player != null && showDot)
            {
                var pos = ToPanel(player.transform.position);
                var pulse = 5f + Mathf.Sin(Time.unscaledTime * 6f) * 1.2f;
                var c = playerColour;
                c.a *= fade;
                p.fillColor = c;
                p.BeginPath();
                p.Arc(pos, pulse, 0f, 360f);
                p.Fill();
            }
        }

        private static void DrawPin(Painter2D p, Vector2 at, MapPin.Kind kind, float fade)
        {
            var colour = kind switch
            {
                MapPin.Kind.Shrine => new Color(0.6f, 0.85f, 1f),
                MapPin.Kind.Boss => new Color(0.9f, 0.45f, 0.4f),
                MapPin.Kind.Npc => new Color(0.95f, 0.85f, 0.5f),
                MapPin.Kind.Secret => new Color(0.75f, 0.6f, 0.95f),
                MapPin.Kind.Door => new Color(0.85f, 0.75f, 0.6f),
                _ => Color.white,
            };
            colour.a *= fade;
            p.fillColor = colour;
            const float s = 6f;
            p.BeginPath();
            p.MoveTo(at + new Vector2(0f, -s));
            p.LineTo(at + new Vector2(s, 0f));
            p.LineTo(at + new Vector2(0f, s));
            p.LineTo(at + new Vector2(-s, 0f));
            p.ClosePath();
            p.Fill();
        }
    }
}
