using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A wall only one school walks through. The first use is a curtain of poison thorns
    /// over a low tunnel: Se, immune to poison, parts it and walks on; anyone else is
    /// stung, pushed back and has to come back once they hold Se.
    ///
    /// Works by switching collision between the player and this collider, so it needs a
    /// solid (not trigger) collider. Its drawings lean aside while the right school passes.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Stance Barrier")]
    [RequireComponent(typeof(Collider))]
    public sealed class StanceBarrier : MonoBehaviour
    {
        [BoxGroup("Barrier", centerLabel: true)]
        [SerializeField] private StanceId stance = StanceId.Se;

        [BoxGroup("Barrier")]
        [ToggleLeft, Tooltip("Sting anyone else who presses into it with poison, which Se cannot catch anyway.")]
        [SerializeField] private bool poisonOthers = true;

        [BoxGroup("Barrier")]
        [Tooltip("Push given to a refused player, metres per second, away from the barrier.")]
        [SerializeField] private float pushBack = 6f;

        [BoxGroup("Barrier")]
        [Tooltip("Drawings that part while the right school walks through. Usually the art children.")]
        [SerializeField] private Transform[] parts = System.Array.Empty<Transform>();

        [BoxGroup("Barrier")]
        [PropertyRange(0f, 60f), Tooltip("Degrees each part leans aside when parted.")]
        [SerializeField] private float partAngle = 25f;

        private Collider _wall;
        private Collider _ignoring;
        private Quaternion[] _rest;
        private float _open, _nextSting;

        private void Awake()
        {
            _wall = GetComponent<Collider>();
            _rest = new Quaternion[parts.Length];
            for (var i = 0; i < parts.Length; i++) if (parts[i] != null) _rest[i] = parts[i].localRotation;
        }

        private void OnDisable() => SetPass(false);

        private void Update()
        {
            var player = PlayerLocator.Player;
            if (player == null || player.Motor == null || player.Motor.Controller == null) return;

            var pass = player.Stance == stance;
            SetPass(pass);

            var near = Mathf.Abs(player.transform.position.x - _wall.bounds.center.x) < _wall.bounds.extents.x + 0.9f;
            _open = Mathf.MoveTowards(_open, pass && near ? 1f : 0f, Time.deltaTime * 4f);
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                // Alternate sides, like a curtain drawn back from the middle.
                var side = i % 2 == 0 ? 1f : -1f;
                parts[i].localRotation = _rest[i] * Quaternion.Euler(0f, 0f, side * partAngle * _open);
            }

            if (pass || !near || Time.time < _nextSting) return;
            // Pressed against it as the wrong school: sting and shove back, once a second.
            var into = player.Motor.Velocity.x * Mathf.Sign(_wall.bounds.center.x - player.transform.position.x);
            if (into <= 0.3f) return;
            _nextSting = Time.time + 1f;
            var away = Mathf.Sign(player.transform.position.x - _wall.bounds.center.x);
            player.Motor.Velocity.x = away * pushBack;
            if (poisonOthers) player.GetComponent<EffectReceiver>()?.Apply(DefaultEffects.Poison, this);
            foreach (var p in parts) if (p != null) PrimeTween.Tween.ShakeLocalPosition(p, new Vector3(0.06f, 0f, 0f), 0.25f);
        }

        private void SetPass(bool pass)
        {
            var player = PlayerLocator.Player;
            var body = player != null && player.Motor != null ? player.Motor.Controller : null;
            if (_ignoring != null && _ignoring != body) { Physics.IgnoreCollision(_ignoring, _wall, false); _ignoring = null; }
            if (body == null || _wall == null) return;
            Physics.IgnoreCollision(body, _wall, pass);
            _ignoring = pass ? body : null;
        }
    }
}
