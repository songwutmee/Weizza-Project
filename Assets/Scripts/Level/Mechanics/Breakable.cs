using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Pots, tall grass, cracked walls: anything the player can break by
    /// hitting it. Goes through the same IDamageable path enemies do, so it
    /// gets the hit stop and spark for free, and drops loot.
    ///
    /// Put it on the Enemy layer: the player's hitbox only searches that
    /// layer. A cracked wall that should only give to Than's heavy swings
    /// sets a minimum hit stop, the measure of how heavy a hit is.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Breakable")]
    [RequireComponent(typeof(Collider))]
    public sealed class Breakable : MonoBehaviour, IDamageable, ISaveable
    {
        [BoxGroup("Breakable")]
        [MinValue(1f)]
        [SerializeField] private float health = 1f;

        [BoxGroup("Breakable")]
        [PropertyRange(0, 20)]
        [Tooltip("Hits with less hit stop than this do nothing. 0 breaks to anything. " +
                 "Around 10 means only heavy swings (Than's) open it.")]
        [SerializeField] private int minimumHitStop;

        [BoxGroup("Breakable")]
        [SerializeField] private LootTable loot;

        [BoxGroup("Breakable")]
        [ToggleLeft]
        [Tooltip("Stays broken across saves. On for walls, off for grass that should regrow.")]
        [SerializeField] private bool persistent;

        [BoxGroup("Breakable")]
        [Tooltip("Shaken on every hit. Usually the art child.")]
        [SerializeField] private Transform visual;

        public Transform Transform => transform;
        public bool IsAlive => _health > 0f;
        public float DepthForHits => transform.position.z;

        private float _health;

        private void Awake()
        {
            _health = health;
            if (visual == null) visual = transform;
        }

        public HitResult Receive(in DamageInfo info)
        {
            if (!IsAlive) return HitResult.Missed;
            if (info.hitStopFrames < minimumHitStop)
            {
                Tween.ShakeLocalPosition(visual, new Vector3(0.03f, 0f, 0f), 0.12f);
                return HitResult.Armored;
            }

            _health -= info.amount;
            Tween.ShakeLocalPosition(visual, new Vector3(0.08f, 0.04f, 0f), 0.15f);
            if (_health > 0f) return HitResult.Hit;

            Break();
            return HitResult.Hit;
        }

        private void Break()
        {
            if (loot != null) loot.Drop(transform.position);
            HitSpark.Play(transform.position + Vector3.up * 0.5f, Vector2.up, 1f, new Color(0.9f, 0.85f, 0.7f));
            SetBroken();
        }

        private void SetBroken()
        {
            _health = 0f;
            LevelPiece.SetSolid(GetComponentsInChildren<Collider>(), GetComponentsInChildren<Renderer>(), false);
        }

        public string SaveKey => persistent ? "breakable." + this.StableKey() : null;
        public string CaptureState() => IsAlive ? "0" : "1";

        public void RestoreState(string state)
        {
            if (state == "1") SetBroken();
        }
    }
}
