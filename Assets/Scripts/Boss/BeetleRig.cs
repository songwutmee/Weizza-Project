using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Moves the bones of the rigged beetle painting from whatever clip the frame by frame
    /// animator is playing. The old renderer keeps receiving every flash, hide and clip
    /// change from the scripts that already drive it; this copies those across and turns
    /// each clip name into a pose, so no boss script had to learn about bones.
    /// </summary>
    // Runs in edit mode too, but only to hide the old drawing: without it the Scene view
    // showed the flat sheet beetle and the rig on top of each other.
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Boss/Beetle Rig")]
    public sealed class BeetleRig : MonoBehaviour
    {
        [BoxGroup("Wiring", centerLabel: true)]
        [Tooltip("The frame by frame renderer the boss scripts talk to. Hidden, but still in charge.")]
        [SerializeField] private SpriteRenderer source;

        [BoxGroup("Wiring")]
        [ToggleLeft, Tooltip("Off draws the old frame by frame placeholder again. Nothing else changes.")]
        [SerializeField] private bool useRig = true;

        [BoxGroup("Wiring")]
        [ToggleLeft, Tooltip("The painting looks to the left. Untick if new art faces right.")]
        [SerializeField] private bool artFacesLeft = true;

        [BoxGroup("Feel", centerLabel: true)]
        [PropertyRange(2f, 30f), Tooltip("How fast the bones reach a new pose. Higher snaps harder.")]
        [SerializeField] private float poseSpeed = 12f;

        [BoxGroup("Feel")]
        [PropertyRange(0f, 30f), Tooltip("Degrees each leg swings while walking.")]
        [SerializeField] private float legSwing = 16f;

        [BoxGroup("Feel")]
        [PropertyRange(0f, 12f), Tooltip("Degrees the tail sways while standing still.")]
        [SerializeField] private float tailSway = 4f;

        [BoxGroup("Feel")]
        [PropertyRange(0f, 0.1f), Tooltip("Metres the body rises and falls when breathing.")]
        [SerializeField] private float breathe = 0.025f;

        [ShowInInspector, ReadOnly, BoxGroup("Feel")] private string _pose = "-";

        private struct Pose
        {
            public float body, head, horn, prong, jaw, tail, legs, lean, lift;
            // A whip turns each tail segment clockwise by its own share, which flips the
            // curl: the tail swings down behind, under the body, and out in front.
            public float whip;
        }

        // Degrees are counter clockwise on the painting as drawn, facing left.
        // So for the head, a negative number lifts it and a positive one drops it.
        private static readonly Dictionary<string, Pose> Poses = new()
        {
            ["Idle"]       = new Pose(),
            ["Clamp_Open"] = new Pose { body = -3, head = -10, horn = -6, prong = -16, jaw = 24, tail = 6 },
            ["Clamp"]      = new Pose { body = 4, head = 12, horn = 8, prong = 10, jaw = -8, tail = -4, lean = 0.3f },
            // The spit is a tail whip from the air: it rises off the ground, coils the tail up
            // behind, then snaps it forward under the body and the ink leaves the stinger.
            ["Spit_Ready"] = new Pose { body = -4, head = -6, horn = -3, jaw = 4, tail = 26, legs = 18, lean = -0.15f, lift = 0.9f },
            ["Spit"]       = new Pose { body = -8, head = 4, horn = 2, jaw = 10, whip = -1f, legs = 14, lean = 0.1f, lift = 1.1f },
            ["Hop"]        = new Pose { body = -6, head = -6, tail = 12, legs = 26, lift = 0.05f },
            ["Hurt"]       = new Pose { body = 6, head = 10, horn = 5, jaw = 10, tail = -10 },
            ["Dead"]       = new Pose { body = 10, head = 16, horn = 8, jaw = 14, tail = -18, legs = 34, lift = -0.1f },
            ["Rear"]       = new Pose { body = -6, head = -10, horn = -4, prong = 8, jaw = -6, tail = 16, legs = -6, lean = -0.25f },
            ["Roar"]       = new Pose { body = -6, head = -15, horn = -5, prong = -18, jaw = 26, tail = 22, legs = -4, lean = 0.1f, lift = 0.06f },
        };

        // Degrees each tail segment turns at a full whip, base to stinger. The whole tail swings
        // down from its root as one piece and the tip curls a little forward. Bending the middle
        // joints hard opened gaps between the plates, so they only follow.
        private static readonly float[] WhipShare = { 125f, -12f, -15f, -10f };

        private SpriteRenderer _renderer;
        private readonly List<SpriteRenderer> _plates = new();
        private SpriteAnimator _animator;
        private EnemyController _enemy;
        private Transform _body, _head, _horn1, _horn2, _prong, _jaw;
        private readonly List<Transform> _tail = new();
        private readonly List<Transform> _legs = new();
        private readonly Dictionary<Transform, Quaternion> _rest = new();
        private Vector3 _bodyRest;
        private Pose _now;
        private float _walk, _walkPhase, _lastX;

        private string _held;
        private float _heldUntil;

        /// <summary>Holds a named pose for a while over whatever the clip says. Null lets go.</summary>
        public void HoldPose(string pose, float seconds)
        {
            _held = pose != null && Poses.ContainsKey(pose) ? pose : null;
            _heldUntil = Time.time + seconds;
        }

        public void Bind(SpriteRenderer drawing)
        {
            source = drawing;
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                if (sr != _renderer) _plates.Add(sr);
            if (source != null) _animator = source.GetComponent<SpriteAnimator>();
            _enemy = GetComponentInParent<EnemyController>();

            _body = Find("Body");
            _head = Find("Head");
            _horn1 = Find("Horn_1");
            _horn2 = Find("Horn_2");
            _prong = Find("Prong");
            _jaw = Find("Jaw");
            for (var i = 1; i <= 4; i++) _tail.Add(Find("Tail_" + i));
            for (var i = 1; i <= 5; i++) _legs.Add(Find("Leg_" + i));
            if (_body != null) _bodyRest = _body.localPosition;
            _lastX = transform.position.x;
        }

        private void OnEnable()
        {
            // Hidden straight away, not on the next LateUpdate: in edit mode LateUpdate
            // only runs when the editor happens to repaint, so the old drawing could sit
            // on top of the rig until then.
            if (source != null) source.forceRenderingOff = useRig;
        }

        private void OnDisable()
        {
            if (source != null) source.forceRenderingOff = false;
        }

        private Transform Find(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name != boneName) continue;
                _rest[t] = t.localRotation;
                return t;
            }
            return null;
        }

        private void LateUpdate()
        {
            if (source == null || _body == null) return;

            // The old renderer stays the one everybody talks to. It just never draws.
            source.forceRenderingOff = useRig;
            _renderer.enabled = useRig && source.enabled;
            if (!useRig) { foreach (var plate in _plates) plate.enabled = false; return; }
            _renderer.color = source.color;
            _renderer.sortingLayerID = source.sortingLayerID;
            _renderer.sortingOrder = source.sortingOrder;
            foreach (var plate in _plates)
            {
                plate.enabled = _renderer.enabled;
                plate.color = source.color;
                plate.sortingLayerID = source.sortingLayerID;
                plate.sortingOrder = source.sortingOrder - 1;
            }

            // Edit mode stops here: the old drawing is hidden and the rig shows in its
            // rest pose, nothing animates while someone is laying out a level.
            if (!Application.isPlaying) return;

            var facingLeft = source.flipX;
            var flip = facingLeft == artFacesLeft ? 1f : -1f;
            transform.localScale = new Vector3(flip, 1f, 1f);

            var dt = Time.deltaTime;
            var speed = dt > 0f ? Mathf.Abs(transform.position.x - _lastX) / dt : 0f;
            _lastX = transform.position.x;
            _walk = Mathf.MoveTowards(_walk, speed > 0.3f ? 1f : 0f, dt * 5f);
            _walkPhase += dt * Mathf.Lerp(0f, 11f, Mathf.Clamp01(speed / 3f));

            var target = PoseFor(out var snap);
            var k = 1f - Mathf.Exp(-(snap ? poseSpeed * 2f : poseSpeed) * dt);
            _now = Lerp(_now, target, k);
            Apply(Time.time);
        }

        private Pose PoseFor(out bool snap)
        {
            snap = false;
            var key = "Idle";
            if (_held != null && Time.time < _heldUntil)
            {
                _pose = _held;
                snap = true;
                return Poses[_held];
            }
            if (_enemy != null && _enemy.Current == EnemyController.State.Dead) key = "Dead";
            else if (_animator != null)
            {
                var clip = _animator.CurrentClipName;
                // Longest names first, so "Clamp_Open" is not read as "Clamp".
                if (clip.Contains("Clamp_Open")) key = "Clamp_Open";
                else if (clip.Contains("Clamp")) { key = "Clamp"; snap = true; }
                else if (clip.Contains("Spit_Ready")) key = "Spit_Ready";
                else if (clip.Contains("Spit")) { key = "Spit"; snap = true; }
                else if (clip.Contains("Hop")) key = "Hop";
                else if (clip.Contains("Hurt")) { key = "Hurt"; snap = true; }
                else if (clip.Contains("Death") || clip.Contains("Dead")) key = "Dead";
            }
            _pose = key;
            return Poses[key];
        }

        private void Apply(float t)
        {
            var breath = Mathf.Sin(t * 2.2f) * breathe;
            var step = Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.04f * _walk;

            // Hovering, it bobs on its wings rather than breathing.
            var hover = _now.lift > 0.3f ? Mathf.Sin(t * 9f) * 0.05f : 0f;
            _body.localPosition = _bodyRest + new Vector3(-_now.lean, breath + step + _now.lift + hover, 0f);
            Turn(_body, _now.body + Mathf.Sin(t * 2.2f) * 0.8f);
            // A roar shakes the whole head, fast and small, like a voice too big for the body.
            var shiver = _pose == "Roar" ? Mathf.Sin(t * 47f) * 2.5f : 0f;
            Turn(_head, _now.head + Mathf.Sin(t * 2.2f + 0.6f) * 1.5f + shiver);
            Turn(_horn1, _now.horn);
            Turn(_horn2, _now.horn * 0.5f);
            Turn(_prong, _now.prong + Mathf.Sin(t * 3.1f) * 2f);
            Turn(_jaw, _now.jaw + Mathf.Sin(t * 3.1f + 1f) * 2f);

            for (var i = 0; i < _tail.Count; i++)
            {
                // Each segment lags the one before it, so the sway travels out to the stinger.
                var sway = Mathf.Sin(t * 1.8f - i * 0.7f) * tailSway * (0.5f + i * 0.25f);
                Turn(_tail[i], _now.tail * (0.6f + i * 0.2f) + sway + _now.whip * WhipShare[i]);
            }

            for (var i = 0; i < _legs.Count; i++)
            {
                // Neighbouring legs move opposite each other, the way a beetle walks.
                var phase = _walkPhase + (i % 2 == 0 ? 0f : Mathf.PI);
                var swing = Mathf.Sin(phase) * legSwing * _walk;
                Turn(_legs[i], _now.legs + swing);
            }
        }

        private void Turn(Transform bone, float degrees)
        {
            if (bone == null) return;
            bone.localRotation = _rest[bone] * Quaternion.Euler(0f, 0f, degrees);
        }


        private static Pose Lerp(Pose a, Pose b, float k) => new()
        {
            body = Mathf.Lerp(a.body, b.body, k),
            head = Mathf.Lerp(a.head, b.head, k),
            horn = Mathf.Lerp(a.horn, b.horn, k),
            prong = Mathf.Lerp(a.prong, b.prong, k),
            jaw = Mathf.Lerp(a.jaw, b.jaw, k),
            tail = Mathf.Lerp(a.tail, b.tail, k),
            legs = Mathf.Lerp(a.legs, b.legs, k),
            lean = Mathf.Lerp(a.lean, b.lean, k),
            lift = Mathf.Lerp(a.lift, b.lift, k),
            whip = Mathf.Lerp(a.whip, b.whip, k),
        };
    }
}
