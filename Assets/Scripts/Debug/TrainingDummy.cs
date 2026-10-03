using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A target that never dies and says exactly what each hit did: damage,
    /// result, poise left, and damage per second over the last few seconds.
    /// For tuning stances against a fixed reference instead of against enemies
    /// that move.
    /// </summary>
    [AddComponentMenu("Weizza/Training Dummy")]
    [RequireComponent(typeof(Vitals))]
    public sealed class TrainingDummy : MonoBehaviour
    {
        [BoxGroup("Dummy")]
        [PropertyRange(1f, 10f)]
        [Tooltip("Seconds the damage per second readout averages over.")]
        [SerializeField] private float dpsWindow = 4f;

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly] public string LastHit { get; private set; } = "-";

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public float DamagePerSecond { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public int Hits { get; private set; }

        private Vitals _vitals;
        private Poise _poise;
        private readonly System.Collections.Generic.Queue<(float time, float amount)> _recent = new();

        private void Awake()
        {
            _vitals = GetComponent<Vitals>();
            _poise = GetComponent<Poise>();
            if (GetComponent<Targetable>() == null) gameObject.AddComponent<Targetable>();
        }

        private void OnEnable() => _vitals.Damaged += OnDamaged;
        private void OnDisable() => _vitals.Damaged -= OnDamaged;

        private void OnDamaged(DamageInfo info, HitResult result)
        {
            Hits++;
            var poise = _poise != null ? $", poise {_poise.Current:0}/{_poise.Max:0}" : "";
            LastHit = $"{info.amount:0.#} {result}, stop {info.hitStopFrames}f, stun {info.hitStunFrames}f{poise}";
            _recent.Enqueue((Time.time, info.amount));

            // Never dies: topped back up after every hit, so a combo can be
            // thrown at it forever.
            _vitals.SetHealth(_vitals.Max);
            HudScreen.Instance?.Toast(LastHit, 1.2f);
        }

        private void Update()
        {
            while (_recent.Count > 0 && Time.time - _recent.Peek().time > dpsWindow) _recent.Dequeue();
            var total = 0f;
            foreach (var hit in _recent) total += hit.amount;
            DamagePerSecond = total / dpsWindow;
        }
    }
}
