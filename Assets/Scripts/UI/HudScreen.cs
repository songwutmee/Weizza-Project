using System.Collections.Generic;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Weizza
{
    /// <summary>
    /// The in-game HUD, in UI Toolkit, built entirely in code: health with a
    /// lagging ghost bar, the stance pips with the switch window filling, the
    /// dash cooldown, the boss bar, toasts, area banners and damage numbers.
    ///
    /// Built in code rather than UXML so there are no layout files to keep in
    /// step with a script. Everything is anchored to a screen edge and sized
    /// against a 1920 by 1080 reference, so it holds at any resolution.
    ///
    /// Health, stance and boss changes arrive as events; Update only animates
    /// toward the latest values (the ghost bar easing down, the switch bar
    /// filling), which is the part that has to run every frame anyway.
    /// </summary>
    [AddComponentMenu("Weizza/HUD Screen")]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudScreen : MonoBehaviour
    {
        public static HudScreen Instance { get; private set; }

        [BoxGroup("Look")]
        [SerializeField] private Color healthColour = new(0.94f, 0.9f, 0.82f);

        [BoxGroup("Look")]
        [Tooltip("The health that was just lost, still showing for a moment. " +
                 "Red, because red already means the player losing health.")]
        [SerializeField] private Color ghostColour = new(0.85f, 0.3f, 0.3f);

        [BoxGroup("Look")]
        [SerializeField] private Color panelColour = new(0.05f, 0.07f, 0.06f, 0.55f);

        [BoxGroup("Look")]
        [PropertyRange(0.1f, 2f)]
        [Tooltip("Seconds the ghost bar waits before draining.")]
        [SerializeField] private float ghostDelay = 0.45f;

        [BoxGroup("Look")]
        [PropertyRange(0.2f, 5f)]
        [Tooltip("Fraction of the bar the ghost drains per second.")]
        [SerializeField] private float ghostDrainPerSecond = 0.8f;

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _wings;
        private readonly Dictionary<StanceId, VisualElement> _pips = new();
        private VisualElement _switchFill;
        private VisualElement _dashFill;
        private VisualElement _dashRow;
        private VisualElement _bossPanel;
        private Label _bossName;
        private VisualElement _bossFill;
        private VisualElement _bossGhost;
        private VisualElement _toastColumn;
        private Label _banner;
        private float _bannerUntil;

        private PlayerController _player;
        private float _healthShown = 1f;
        private float _ghostShown = 1f;
        private float _ghostHoldUntil;
        private float _bossGhostShown;
        private float _bannerStarted;
        private float _bannerSeconds = 2.5f;
        private readonly System.Collections.Generic.List<System.IDisposable> _subscriptions = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            Instance = this;
            _document = GetComponent<UIDocument>();
            if (GetComponent<DamageNumbers>() == null) gameObject.AddComponent<DamageNumbers>();
            if (GetComponent<LowHealthVignette>() == null) gameObject.AddComponent<LowHealthVignette>();
            if (GetComponent<SettingsScreen>() == null) gameObject.AddComponent<SettingsScreen>();
            if (GetComponent<MapScreen>() == null) gameObject.AddComponent<MapScreen>();
        }

        private void OnEnable()
        {
            Build();
            _player = PlayerLocator.Player;
            if (_player != null && _player.Vitals != null)
            {
                _player.Vitals.Damaged += OnPlayerDamaged;
            }
            _subscriptions.Add(StoryEvents.Subscribe(GameEvents.StanceSwitched, _ => RefreshPips()));
            _subscriptions.Add(StoryEvents.Subscribe(GameEvents.StanceLearned, _ => RefreshPips()));
            _subscriptions.Add(StoryEvents.Subscribe(GameEvents.StanceLocked, p => Toast(Loc.T("UI_TOAST_STANCE_LOCKED", "Not learned yet") + $" ({p})")));
            _subscriptions.Add(StoryEvents.Subscribe(GameEvents.GameSaved, _ => Toast(Loc.T("UI_TOAST_SAVED", "Saved"))));
            _subscriptions.Add(StoryEvents.Subscribe(GameEvents.CheckpointClaimed, _ => Toast(Loc.T("UI_TOAST_CHECKPOINT", "Checkpoint"))));
            RefreshPips();
        }

        private void OnDisable()
        {
            if (_player != null && _player.Vitals != null) _player.Vitals.Damaged -= OnPlayerDamaged;
            foreach (var s in _subscriptions) s?.Dispose();
            _subscriptions.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- Build ----------------------------------------------------------------------

        private void Build()
        {
            _root = _document.rootVisualElement;
            if (_root == null) return;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1f;

            // Health and stance, top left.
            var topLeft = Box(_root, null);
            Anchor(topLeft, left: 48, top: 40);
            topLeft.style.flexDirection = FlexDirection.Column;

            // Health is a butterfly with four wings, one quarter of health each.
            // The revive art is the body reassembling out of butterflies, so
            // losing wings and growing them back says "alive" in the game's own
            // picture instead of a generic bar.
            _wings = new VisualElement { pickingMode = PickingMode.Ignore };
            _wings.style.width = 132;
            _wings.style.height = 118;
            _wings.generateVisualContent += DrawWings;
            topLeft.Add(_wings);

            var pipRow = Box(topLeft, null);
            pipRow.style.flexDirection = FlexDirection.Row;
            pipRow.style.marginTop = 10;
            foreach (var id in new[] { StanceId.Normal, StanceId.Than, StanceId.Se, StanceId.In })
            {
                var pip = Box(pipRow, Color.gray);
                pip.style.width = 22;
                pip.style.height = 22;
                pip.style.marginRight = 10;
                Round(pip, 11);
                Border(pip, 2, new Color(1f, 1f, 1f, 0f));
                _pips[id] = pip;
            }

            var switchBack = Box(topLeft, panelColour);
            switchBack.style.width = 128;
            switchBack.style.height = 4;
            switchBack.style.marginTop = 6;
            _switchFill = Fill(switchBack, new Color(1f, 1f, 1f, 0.85f));

            _dashRow = Box(topLeft, panelColour);
            _dashRow.style.width = 128;
            _dashRow.style.height = 4;
            _dashRow.style.marginTop = 4;
            _dashFill = Fill(_dashRow, new Color(0.5f, 0.8f, 1f, 0.9f));

            // Boss bar, bottom centre.
            _bossPanel = Box(_root, null);
            Anchor(_bossPanel, left: 0, right: 0, bottom: 56);
            _bossPanel.style.alignItems = Align.Center;
            _bossName = new Label("Boss");
            _bossName.pickingMode = PickingMode.Ignore;
            _bossName.style.color = new Color(0.94f, 0.9f, 0.82f);
            _bossName.style.fontSize = 20;
            _bossName.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bossName.style.marginBottom = 6;
            _bossPanel.Add(_bossName);
            var bossBack = Box(_bossPanel, panelColour);
            bossBack.style.width = 760;
            bossBack.style.height = 12;
            Round(bossBack, 6);
            _bossGhost = Fill(bossBack, ghostColour);
            _bossFill = Fill(bossBack, new Color(0.9f, 0.82f, 0.62f));
            _bossPanel.style.display = DisplayStyle.None;

            // Toasts, top right.
            _toastColumn = Box(_root, null);
            Anchor(_toastColumn, right: 48, top: 40);
            _toastColumn.style.alignItems = Align.FlexEnd;

            // Area banner, upper centre.
            _banner = new Label("");
            _banner.pickingMode = PickingMode.Ignore;
            Anchor(_banner, left: 0, right: 0, top: 160);
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _banner.style.fontSize = 44;
            _banner.style.color = new Color(0.94f, 0.9f, 0.82f);
            _banner.style.opacity = 0f;
            _root.Add(_banner);

            // Area title card, dead centre and very large: the "you have arrived"
            // moment of a Neva chapter. A thin rule under it, and a slow letter
            // spread as it fades in, are what make it read as a title rather than
            // a notification.
            _title = new VisualElement { pickingMode = PickingMode.Ignore };
            Anchor(_title, left: 0, right: 0, top: 0, bottom: 0);
            _title.style.justifyContent = Justify.Center;
            _title.style.alignItems = Align.Center;
            _title.style.opacity = 0f;
            _titleLabel = new Label("") { pickingMode = PickingMode.Ignore };
            _titleLabel.style.fontSize = 128;
            _titleLabel.style.color = Color.white;
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(_titleLabel);
            _titleRule = new VisualElement { pickingMode = PickingMode.Ignore };
            _titleRule.style.height = 2;
            _titleRule.style.width = 0;
            _titleRule.style.marginTop = 6;
            _titleRule.style.backgroundColor = new Color(1f, 1f, 1f, 0.8f);
            _title.Add(_titleRule);
            _root.Add(_title);
        }

        private VisualElement _title;
        private Label _titleLabel;
        private VisualElement _titleRule;
        private float _titleStarted = -100f;
        private float _titleHold;
        private const float TitleFadeIn = 1.2f;
        private const float TitleFadeOut = 1.4f;

        /// <summary>Shows a huge centred area title. Returns how long it lasts in real seconds, for a cutscene to wait on.</summary>
        public float TitleCard(string text, float holdSeconds = 2.5f)
        {
            if (_titleLabel == null) return 0f;
            _titleLabel.text = text;
            _titleStarted = Time.unscaledTime;
            _titleHold = Mathf.Max(0.5f, holdSeconds);
            return TitleFadeIn + _titleHold + TitleFadeOut;
        }

        private void TickTitle()
        {
            if (_title == null) return;
            var t = Time.unscaledTime - _titleStarted;
            var total = TitleFadeIn + _titleHold + TitleFadeOut;
            if (t < 0f || t > total)
            {
                if (_title.style.opacity.value != 0f) _title.style.opacity = 0f;
                return;
            }
            var a = t < TitleFadeIn ? t / TitleFadeIn
                : t < TitleFadeIn + _titleHold ? 1f
                : 1f - (t - TitleFadeIn - _titleHold) / TitleFadeOut;
            var eased = a * a * (3f - 2f * a);
            _title.style.opacity = eased;

            // Letters drift apart over the whole card, the rule draws out under them.
            var spread = Mathf.Lerp(4f, 22f, Mathf.Clamp01(t / total));
            _titleLabel.style.letterSpacing = spread;
            _titleRule.style.width = Length.Percent(Mathf.Lerp(0f, 28f, Mathf.Clamp01(t / (TitleFadeIn + 0.6f))));
        }

        private static VisualElement Box(VisualElement parent, Color? colour)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            if (colour.HasValue) element.style.backgroundColor = colour.Value;
            parent.Add(element);
            return element;
        }

        private static VisualElement Fill(VisualElement parent, Color colour)
        {
            var fill = Box(parent, colour);
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.top = 0;
            fill.style.bottom = 0;
            fill.style.width = Length.Percent(100f);
            fill.style.borderTopLeftRadius = parent.style.borderTopLeftRadius;
            fill.style.borderBottomLeftRadius = parent.style.borderBottomLeftRadius;
            fill.style.borderTopRightRadius = parent.style.borderTopRightRadius;
            fill.style.borderBottomRightRadius = parent.style.borderBottomRightRadius;
            return fill;
        }

        private static void Anchor(VisualElement element, float? left = null, float? right = null,
            float? top = null, float? bottom = null)
        {
            element.style.position = Position.Absolute;
            if (left.HasValue) element.style.left = left.Value;
            if (right.HasValue) element.style.right = right.Value;
            if (top.HasValue) element.style.top = top.Value;
            if (bottom.HasValue) element.style.bottom = bottom.Value;
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void Border(VisualElement element, float width, Color colour)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = colour;
            element.style.borderBottomColor = colour;
            element.style.borderLeftColor = colour;
            element.style.borderRightColor = colour;
        }

        // ---- Live ---------------------------------------------------------------------------

        private void OnPlayerDamaged(DamageInfo info, HitResult result)
        {
            if (result is HitResult.Hit or HitResult.Armored) _ghostHoldUntil = Time.unscaledTime + ghostDelay;
        }

        private void Update()
        {
            // UIDocument can replace its root after this component built into
            // it (its own OnEnable may run second), which leaves the HUD drawn
            // into a tree no panel shows. Rebuilding on mismatch catches that
            // and any later document reload.
            if (_document != null && _document.rootVisualElement != null && _document.rootVisualElement != _root)
            {
                Build();
                RefreshPips();
            }

            if (_root == null) return;
            if (_player == null) _player = PlayerLocator.Player;
            var dt = Time.unscaledDeltaTime;

            TickHealth(dt);
            TickSwitchAndDash();
            TickBoss(dt);
            TickBanner();
            TickTitle();
        }

        private void TickHealth(float dt)
        {
            var vitals = _player != null ? _player.Vitals : null;
            var target = vitals != null ? vitals.Normalized : 0f;

            // Health snaps on loss (the hit already landed, the bar should say
            // so at once) and eases up on gain, so healing reads as filling.
            _healthShown = target < _healthShown ? target : Mathf.MoveTowards(_healthShown, target, dt * 1.5f);

            if (_ghostShown < _healthShown) _ghostShown = _healthShown;
            else if (Time.unscaledTime >= _ghostHoldUntil)
                _ghostShown = Mathf.MoveTowards(_ghostShown, _healthShown, dt * ghostDrainPerSecond);

            // Rebuilding the wing mesh is cheap but not free. Only when something
            // visible changed, which is a few frames around each hit or heal.
            var pending = vitals != null ? vitals.PendingWing : 0f;
            if (_wings != null && (!Mathf.Approximately(_healthShown, _wingsHealth) ||
                                   !Mathf.Approximately(_ghostShown, _wingsGhost) ||
                                   !Mathf.Approximately(pending, _wingsPending) || WingCount != _wingsCount))
            {
                _wingsHealth = _healthShown;
                _wingsGhost = _ghostShown;
                _wingsPending = pending;
                _wingsCount = WingCount;
                _wings.MarkDirtyRepaint();
            }
        }

        private float _wingsHealth = -1f;
        private float _wingsGhost = -1f;
        private float _wingsPending;
        private int _wingsCount;

        private int FirstEmptyWing() => Mathf.Clamp(Mathf.CeilToInt(_healthShown * WingCount - 0.001f), 0, WingCount - 1);

        // Every wing slot a butterfly can have, in order of loss reversed: slot 0 is
        // the last one standing (upper left), so a player on one wing still sees a
        // recognisable butterfly, just a lopsided one. Four wings use the first four
        // (upper and lower pairs). More wings add a pair of small tails, then a pair
        // of small inner wings, so five to eight still read as one butterfly.
        private static readonly (float sx, bool upper, float scale, float dy)[] WingSlots =
        {
            (-1f, true, 1f, 0f), (1f, true, 1f, 0f),
            (-1f, false, 1f, 0f), (1f, false, 1f, 0f),
            (-1f, false, 0.55f, 28f), (1f, false, 0.55f, 28f),
            (-1f, true, 0.5f, -4f), (1f, true, 0.5f, -4f),
        };

        // Read from the player's Vitals every draw, so changing Wings in the
        // Inspector or from a stance shows straight away (BUG-2).
        private int WingCount
        {
            get
            {
                var vitals = _player != null ? _player.Vitals : null;
                var count = vitals != null && vitals.UsesWings ? vitals.Wings : 4;
                return Mathf.Clamp(count, 1, WingSlots.Length);
            }
        }

        private void DrawWings(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = _wings.contentRect;
            var count = WingCount;
            var hinge = new Vector2(r.width * 0.5f, r.height * (count > 4 ? 0.4f : 0.45f));

            for (var i = 0; i < count; i++)
            {
                var (sx, upper, size, dy) = WingSlots[i];
                var root = hinge + new Vector2(0f, dy);
                var full = Mathf.Clamp01(_healthShown * count - i);
                var ghost = Mathf.Clamp01(_ghostShown * count - i);

                // Pure white, by Songwut's call: the butterfly is the one bright
                // shape in the corner. Empty sockets are a faint white outline so
                // what is missing can still be counted.
                Wing(p, root, sx, upper, size, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.35f));

                // Health just lost lingers as a translucent white wing, then the
                // solid wing grown from the hinge on top.
                if (ghost > full) Wing(p, root, sx, upper, ghost * size, new Color(1f, 1f, 1f, 0.4f), Color.clear);
                if (full > 0.001f) Wing(p, root, sx, upper, full * size, Color.white, Color.clear);

                // The first empty wing shows banked healing as a faint wing growing
                // in, so a kill that heals a third of a wing is still visible.
                if (full <= 0.001f && ghost <= 0.001f && i == FirstEmptyWing() && _wingsPending > 0.001f)
                    Wing(p, root, sx, upper, _wingsPending * size, new Color(1f, 1f, 1f, 0.28f), Color.clear);
            }

            // Body, white too, drawn last over the wing roots.
            p.strokeColor = Color.white;
            p.lineWidth = 5f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(hinge + new Vector2(0f, -22f));
            p.LineTo(hinge + new Vector2(0f, 26f));
            p.Stroke();
        }


        private static void Wing(Painter2D p, Vector2 hinge, float sx, bool upper, float scale, Color fill, Color line)
        {
            Vector2 P(float x, float y) => hinge + new Vector2(x * sx, y) * scale;

            p.BeginPath();
            p.MoveTo(hinge);
            if (upper)
            {
                p.BezierCurveTo(P(8f, -34f), P(52f, -52f), P(60f, -30f));
                p.BezierCurveTo(P(66f, -12f), P(36f, 2f), hinge);
            }
            else
            {
                p.BezierCurveTo(P(22f, 2f), P(46f, 18f), P(36f, 38f));
                p.BezierCurveTo(P(26f, 50f), P(6f, 30f), hinge);
            }
            p.ClosePath();
            if (fill.a > 0f) { p.fillColor = fill; p.Fill(); }
            if (line.a > 0f) { p.strokeColor = line; p.lineWidth = 1.5f; p.Stroke(); }
        }

        private void TickSwitchAndDash()
        {
            if (_player == null) return;

            var tuning = _player.Tuning;
            var switching = _player.State == PlayerState.StanceSwitch && tuning != null && tuning.SwitchSeconds > 0f;
            _switchFill.style.width = Length.Percent(switching ? (1f - _player.SwitchRemaining / tuning.SwitchSeconds) * 100f : 0f);

            var stance = _player.CurrentStance;
            var canDash = stance != null && stance.canDash;
            _dashRow.style.display = canDash ? DisplayStyle.Flex : DisplayStyle.None;
            if (!canDash) return;

            var total = Timing.Frames(stance.dashFrames + stance.dashCooldownFrames);
            var ready = total <= 0f ? 1f : 1f - _player.DashCooldownRemaining / total;
            _dashFill.style.width = Length.Percent(Mathf.Clamp01(ready) * 100f);
        }

        private void RefreshPips()
        {
            if (_player == null) _player = PlayerLocator.Player;
            foreach (var pair in _pips)
            {
                var definition = _player != null && _player.Tuning != null ? _player.Tuning.Get(pair.Key) : null;
                var colour = definition != null ? definition.tint : Color.gray;
                var learned = StanceProgress.IsLearned(pair.Key);
                var current = _player != null && _player.Stance == pair.Key;

                colour.a = learned ? 1f : 0.2f;
                pair.Value.style.backgroundColor = colour;
                pair.Value.style.scale = new Scale(Vector3.one * (current ? 1.25f : 1f));
                Border(pair.Value, 2, current ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0f));
            }
        }

        private void TickBoss(float dt)
        {
            var boss = MiniBossBrain.Active;
            var showing = boss != null && boss.Vitals != null;
            _bossPanel.style.display = showing ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showing) return;

            _bossName.text = boss.BossName;
            var target = boss.Vitals.Normalized;
            _bossGhostShown = Mathf.Max(target, Mathf.MoveTowards(_bossGhostShown, target, dt * 0.5f));
            _bossFill.style.width = Length.Percent(target * 100f);
            _bossGhost.style.width = Length.Percent(_bossGhostShown * 100f);
        }

        // ---- Toasts and banners -------------------------------------------------------------

        /// <summary>A short line in the corner that fades away. "Saved", "Checkpoint", an item name.</summary>
        public void Toast(string text, float seconds = 2.2f)
        {
            if (_toastColumn == null || string.IsNullOrEmpty(text)) return;

            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.color = new Color(0.94f, 0.9f, 0.82f);
            label.style.fontSize = 18;
            label.style.backgroundColor = panelColour;
            label.style.paddingLeft = 12;
            label.style.paddingRight = 12;
            label.style.paddingTop = 4;
            label.style.paddingBottom = 4;
            label.style.marginBottom = 6;
            Round(label, 6);
            _toastColumn.Add(label);

            label.schedule.Execute(() => label.style.opacity = 0f).StartingIn((long)(seconds * 1000f));
            label.style.transitionProperty = new List<StylePropertyName> { new("opacity") };
            label.style.transitionDuration = new List<TimeValue> { new(0.4f, TimeUnit.Second) };
            label.schedule.Execute(() => label.RemoveFromHierarchy()).StartingIn((long)((seconds + 0.5f) * 1000f));
        }

        /// <summary>Large centred text that fades in and out. Area names on entering a region.</summary>
        public void Banner(string text, float seconds = 2.5f)
        {
            if (_banner == null) return;
            _banner.text = text;
            _bannerStarted = Time.unscaledTime;
            _bannerSeconds = Mathf.Max(1f, seconds);
            _bannerUntil = _bannerStarted + _bannerSeconds;
        }

        private void TickBanner()
        {
            var left = _bannerUntil - Time.unscaledTime;
            var shown = Time.unscaledTime - _bannerStarted;
            var opacity = left <= 0f ? 0f : Mathf.Clamp01(Mathf.Min(shown / 0.4f, left / 0.6f));
            _banner.style.opacity = opacity;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Converts a world point to a position on this HUD, for damage numbers.</summary>
        public bool WorldToPanel(Vector3 world, out Vector2 panelPosition)
        {
            panelPosition = default;
            var camera = Camera.main;
            if (camera == null || _root?.panel == null) return false;
            if (camera.WorldToViewportPoint(world).z < 0f) return false;
            panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, camera);
            return true;
        }

        public VisualElement Root => _root;
    }
}
