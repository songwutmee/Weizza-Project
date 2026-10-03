using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The crosshair that sits on the cursor, plus a ring around it that fills
    /// while a throw winds up or a charge builds.
    ///
    /// Drawn in the world on the Foreground sorting layer rather than as UI,
    /// because it has to sit exactly on the lane point the shot will fly to,
    /// and a UI element would need converting back every frame anyway.
    /// Animates on unscaled time so it stays crisp while aiming slows the
    /// world down.
    /// </summary>
    public sealed class AimReticle
    {
        private readonly Transform _root;
        private readonly SpriteRenderer _cross;
        private readonly SpriteRenderer _ring;
        private readonly SpriteRenderer _lock;
        private float _pulse;
        private float _shownAlpha;
        private bool _visible;

        public AimReticle(Transform parent)
        {
            _root = new GameObject("Aim Reticle").transform;
            _root.SetParent(parent, false);

            _cross = Make("Cross", ProceduralSprites.Crosshair(), 0.55f, 200);
            _ring = Make("Charge Ring", ProceduralSprites.Ring(), 0.9f, 199);
            _lock = Make("Lock Brackets", ProceduralSprites.Brackets(), 1.1f, 201);
            _lock.enabled = false;
        }

        private SpriteRenderer Make(string name, Sprite sprite, float size, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.one * size;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = "Foreground";
            renderer.sortingOrder = order;
            return renderer;
        }

        public void SetVisible(bool visible) => _visible = visible;

        private float _punch;
        private float _punchVelocity;
        private Transform _lockedOn;
        private Targetable _lockedTarget;

        /// <summary>
        /// A springy scale kick. Positive swells (press, full charge), negative
        /// pinches (release). Spring rather than a fixed tween, so two punches in a
        /// row add up instead of one cutting the other off.
        /// </summary>
        public void Punch(float amount) => _punchVelocity += amount * 40f;

        /// <summary>Call once a frame with where the shot will go.</summary>
        public void Tick(Vector3 worldPoint, float charge, Color colour, Color fullColour, Transform lockedOn)
        {
            var dt = Time.unscaledDeltaTime;
            _shownAlpha = Mathf.MoveTowards(_shownAlpha, _visible ? 1f : 0f, dt * 8f);
            _root.gameObject.SetActive(_shownAlpha > 0.001f);
            if (_shownAlpha <= 0.001f) return;

            _root.position = worldPoint;
            _root.rotation = Quaternion.Euler(ViewRig.Pitch, 0f, 0f);

            // Damped spring back to zero, critically damped enough to settle in
            // about a tenth of a second with one small overshoot.
            _punchVelocity += (-_punch * 900f - _punchVelocity * 38f) * dt;
            _punch += _punchVelocity * dt;
            _root.localScale = Vector3.one * Mathf.Max(0.2f, 1f + _punch);

            // The crosshair tightens as charge builds: focus, read at a glance.
            _cross.transform.localScale = Vector3.one * 0.55f * Mathf.Lerp(1f, 0.7f, charge);

            var full = charge >= 0.999f;
            if (full) _pulse += dt * 10f;

            var crossColour = full ? fullColour : colour;
            crossColour.a *= _shownAlpha;
            _cross.color = crossColour;

            // The ring grows in as charge builds and pulses once full, so the
            // moment a shot is at its strongest can be read without looking
            // away from where it is aimed.
            var ringScale = Mathf.Lerp(1.6f, 0.9f, charge) * (full ? 1f + Mathf.Sin(_pulse) * 0.08f : 1f);
            _ring.transform.localScale = Vector3.one * ringScale;
            var ringColour = Color.Lerp(colour, fullColour, charge);
            ringColour.a *= _shownAlpha * Mathf.Clamp01(charge * 1.5f);
            _ring.color = ringColour;
            // At charge 0 (Cursor Aim never charges) the ring is fully clear, so
            // switch it off rather than drawing an invisible sprite (BUG-4).
            _ring.enabled = ringColour.a > 0.001f;

            _lock.enabled = lockedOn != null;
            if (lockedOn != null)
            {
                if (_lockedOn != lockedOn)
                {
                    _lockedOn = lockedOn;
                    _lockedTarget = lockedOn.GetComponent<Targetable>();
                }
                var target = _lockedTarget;
                _lock.transform.position = target != null ? target.AimPoint : lockedOn.position;
                _lock.transform.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 90f);
                var lockColour = fullColour;
                lockColour.a *= _shownAlpha;
                _lock.color = lockColour;
            }
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }

    /// <summary>
    /// The flight path, drawn as running dashes, that draws itself out from
    /// the hand when it appears.
    ///
    /// The draw-on is the "line animation" the charge mode asks for: the path
    /// growing outward reads as the throw being prepared, where a line that is
    /// simply there reads as a debug overlay.
    /// </summary>
    public sealed class TrajectoryLine
    {
        private readonly LineRenderer _line;
        private readonly Material _material;
        private readonly List<Vector3> _points = new(64);
        private readonly List<Vector3> _shown = new(64);
        private Vector3[] _buffer = new Vector3[64];
        private float _drawn;
        private float _scroll;
        private bool _visible;
        private float _drawSeconds = 0.25f;

        public TrajectoryLine(Transform parent)
        {
            var go = new GameObject("Trajectory Line");
            go.transform.SetParent(parent, false);
            _line = go.AddComponent<LineRenderer>();
            _material = ProceduralSprites.NewLineMaterial(ProceduralSprites.DashTexture());
            _line.material = _material;
            _line.textureMode = LineTextureMode.Tile;
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
            _line.sortingLayerName = "Foreground";
            _line.sortingOrder = 198;
            _line.positionCount = 0;
            _line.enabled = false;
        }

        /// <summary>
        /// Shows or hides the line. Showing again after hiding restarts the
        /// draw-on, so every new charge animates in rather than popping.
        /// </summary>
        public void SetVisible(bool visible, float drawSeconds)
        {
            if (visible && !_visible) _drawn = 0f;
            _visible = visible;
            _drawSeconds = Mathf.Max(0.01f, drawSeconds);
            if (!visible) _line.enabled = false;
        }

        public void Tick(Vector3 origin, Vector3 velocity, float gravity, float seconds, float radius,
            LayerMask worldMask, float width, Color colour, float scrollSpeed)
        {
            if (!_visible) return;

            var dt = Time.unscaledDeltaTime;
            _drawn = Mathf.Min(1f, _drawn + dt / _drawSeconds);
            _scroll -= scrollSpeed * dt;

            Ballistics.SamplePath(origin, velocity, gravity, seconds, radius, worldMask, _points);
            Trim(_points, EaseOut(_drawn), _shown);

            _line.enabled = _shown.Count >= 2;
            // One SetPositions call from a reused array instead of one native call
            // per point. The buffer grows once to the largest path and stays.
            if (_buffer.Length < _shown.Count) _buffer = new Vector3[Mathf.NextPowerOfTwo(_shown.Count)];
            for (var i = 0; i < _shown.Count; i++) _buffer[i] = _shown[i];
            _line.positionCount = _shown.Count;
            _line.SetPositions(_buffer);

            _line.startWidth = width;
            _line.endWidth = width * 0.4f;
            var end = colour;
            end.a *= 0.15f;
            _line.startColor = colour;
            _line.endColor = end;
            _material.mainTextureOffset = new Vector2(_scroll, 0f);
        }

        /// <summary>Keeps the first fraction of the path by length, for the draw-on.</summary>
        private static void Trim(List<Vector3> path, float fraction, List<Vector3> into)
        {
            into.Clear();
            if (path.Count < 2) return;

            var total = 0f;
            for (var i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            var keep = total * Mathf.Clamp01(fraction);

            into.Add(path[0]);
            var run = 0f;
            for (var i = 1; i < path.Count; i++)
            {
                var segment = Vector3.Distance(path[i - 1], path[i]);
                if (run + segment >= keep)
                {
                    var t = segment > 0f ? (keep - run) / segment : 0f;
                    into.Add(Vector3.Lerp(path[i - 1], path[i], t));
                    return;
                }
                run += segment;
                into.Add(path[i]);
            }
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        public void Destroy()
        {
            if (_line != null) Object.Destroy(_line.gameObject);
            if (_material != null) Object.Destroy(_material);
        }
    }
}
