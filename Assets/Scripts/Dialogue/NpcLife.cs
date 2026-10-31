using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// A person going about their day: an unhurried stroll around where they stand, pauses,
    /// and small things while they stand (a glance the other way, shifting their weight, a
    /// little bounce on the toes). Nobody stands frozen until spoken to.
    ///
    /// They stop and stay put as soon as the player comes near, so talking to them is never
    /// a chase. Only the position and the facing are touched: the talk glow owns the scale.
    /// </summary>
    [AddComponentMenu("Weizza/Narrative/NPC Life")]
    [RequireComponent(typeof(NpcInteractable))]
    public sealed class NpcLife : MonoBehaviour
    {
        [BoxGroup("Stroll"), Tooltip("Metres either side of where they were placed that they wander.")]
        [SerializeField] private float range = 2f;

        [BoxGroup("Stroll"), SerializeField] private float speed = 0.7f;

        [BoxGroup("Stroll"), MinMaxSlider(0.5f, 15f, true), Tooltip("Seconds they stand between strolls.")]
        [SerializeField] private Vector2 pause = new(3f, 7f);

        [BoxGroup("Idle"), MinMaxSlider(1f, 10f, true), Tooltip("Seconds between small idle moves while they stand.")]
        [SerializeField] private Vector2 idleEvery = new(2.5f, 5f);

        private NpcInteractable _npc;
        private Vector3 _home;
        private float _heightAboveGround;
        private float _target;
        private bool _walking;
        private float _nextMoveAt;
        private float _nextIdleAt;
        private bool _facingRight = true;

        // An idle move in progress: an offset over time, and an optional glance back.
        private float _idleStart = -1f, _idleLength;
        private int _idleKind;

        private void Awake()
        {
            _npc = GetComponent<NpcInteractable>();
            _home = transform.position;
            _heightAboveGround = GroundPath.TryTerrainHeight(_home.x, out var g) ? _home.y - g : 0f;
            _target = _home.x;
            _nextMoveAt = Time.time + Random.Range(pause.x, pause.y);
            _nextIdleAt = Time.time + Random.Range(idleEvery.x, idleEvery.y);
        }

        private void Update()
        {
            var player = PlayerLocator.Player;
            var near = player != null && Mathf.Abs(player.transform.position.x - transform.position.x) <= _npc.NoticeRange;
            if (near || DialogueDirector.Talking)
            {
                // Someone wants them: stand still and let the talk component do the facing.
                _walking = false;
                _idleStart = -1f;
                _nextMoveAt = Time.time + Random.Range(pause.x, pause.y);
                return;
            }

            if (_walking) Walk();
            else Stand();
        }

        private void Walk()
        {
            var p = transform.position;
            var step = speed * Time.deltaTime;
            var x = Mathf.MoveTowards(p.x, _target, step);
            Place(x);
            if (Mathf.Abs(x - _target) < 0.01f)
            {
                _walking = false;
                _nextMoveAt = Time.time + Random.Range(pause.x, pause.y);
                _nextIdleAt = Time.time + Random.Range(idleEvery.x, idleEvery.y);
            }
        }

        private void Stand()
        {
            if (Time.time >= _nextMoveAt)
            {
                // A new spot at least a metre from here, so a stroll always looks like one.
                var next = _home.x + Random.Range(-range, range);
                if (Mathf.Abs(next - transform.position.x) < 1f) next = _home.x + (next > _home.x ? -range : range) * 0.7f;
                _target = next;
                _walking = true;
                _idleStart = -1f;
                _facingRight = _target > transform.position.x;
                _npc.Face(_facingRight);
                return;
            }

            if (_idleStart < 0f && Time.time >= _nextIdleAt)
            {
                _idleKind = Random.Range(0, 3);
                _idleLength = _idleKind == 0 ? 1.6f : _idleKind == 1 ? 1.4f : 0.5f;
                _idleStart = Time.time;
                if (_idleKind == 0) _npc.Face(!_facingRight);
            }

            if (_idleStart < 0f) return;

            var t = (Time.time - _idleStart) / _idleLength;
            var x = transform.position.x;
            var lift = 0f;
            switch (_idleKind)
            {
                case 0: // a glance the other way, then back
                    if (t >= 1f) _npc.Face(_facingRight);
                    break;
                case 1: // shifting weight: a slow lean one way and back
                    x = _target + Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * 0.12f * (_facingRight ? 1f : -1f);
                    break;
                case 2: // a small bounce on the toes
                    lift = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * 0.06f;
                    break;
            }
            Place(x, lift);

            if (t >= 1f)
            {
                _idleStart = -1f;
                _nextIdleAt = Time.time + Random.Range(idleEvery.x, idleEvery.y);
            }
        }

        // Feet on the terrain wherever they stand, so a stroll on a slope follows the slope.
        private void Place(float x, float lift = 0f)
        {
            var y = GroundPath.TryTerrainHeight(x, out var g) ? g + _heightAboveGround : _home.y;
            transform.position = new Vector3(x, y + lift, _home.z);
        }

        private void OnDrawGizmosSelected()
        {
            var at = Application.isPlaying ? _home : transform.position;
            Gizmos.color = new Color(0.6f, 0.9f, 0.6f, 0.8f);
            Gizmos.DrawLine(at + Vector3.left * range, at + Vector3.right * range);
        }
    }
}
