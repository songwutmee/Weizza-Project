using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>Which built in effect an effect zone falls back to when no asset is assigned.</summary>
    public enum ZoneEffectPreset { Miasma, SnailSlime, Water, Regen }

    /// <summary>
    /// Applies an effect to whoever stands inside and removes it when they
    /// leave. One component covers miasma (poison, Se is immune), snail slime
    /// (walking is a crawl, a dash crosses it untouched), shallow water, and a
    /// shrine's healing ring.
    ///
    /// Slime is the Se + entry "dash past the snail's slime": the slow scales
    /// run speed, and a dash sets its own speed directly, so nothing here has
    /// to know about dashing at all.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Effect Zone")]
    public sealed class EffectZone : BodyZone
    {
        [BoxGroup("Effect")]
        [EnumToggleButtons]
        [SerializeField] private ZoneEffectPreset preset = ZoneEffectPreset.Miasma;

        [BoxGroup("Effect")]
        [Tooltip("Optional. An authored effect wins over the preset.")]
        [SerializeField] private GameplayEffect effect;

        [BoxGroup("Effect")]
        [PropertyRange(0.1f, 2f)]
        [Tooltip("Seconds between re-applying, which keeps a Refresh effect " +
                 "topped up while standing inside.")]
        [SerializeField] private float reapplyEvery = 0.4f;

        [BoxGroup("Effect")]
        [ToggleLeft]
        [Tooltip("Remove the effect on leaving. Off lets poison linger for its " +
                 "own duration after stepping out, which reads as the miasma " +
                 "clinging rather than switching off at a line.")]
        [SerializeField] private bool removeOnExit = true;

        private float _nextApply;

        public GameplayEffect Effect => effect != null ? effect : preset switch
        {
            ZoneEffectPreset.Miasma => DefaultEffects.Poison,
            ZoneEffectPreset.SnailSlime => DefaultEffects.Slime,
            ZoneEffectPreset.Water => DefaultEffects.Wet,
            _ => DefaultEffects.Regen,
        };

        protected override void OnEntered(CharacterMotor3D body) => Receiver(body)?.Apply(Effect, this);

        protected override void OnExited(CharacterMotor3D body)
        {
            if (removeOnExit) Receiver(body)?.Remove(Effect, this);
        }

        protected override void Update()
        {
            base.Update();
            if (Time.time < _nextApply) return;
            _nextApply = Time.time + reapplyEvery;
            foreach (var body in Inside)
            {
                if (body != null) Receiver(body)?.Apply(Effect, this);
            }
        }

        private static EffectReceiver Receiver(CharacterMotor3D body)
        {
            if (body == null) return null;
            var receiver = body.GetComponent<EffectReceiver>();
            if (receiver == null && body.GetComponent<Vitals>() != null)
                receiver = body.gameObject.AddComponent<EffectReceiver>();
            return receiver;
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => preset switch
        {
            ZoneEffectPreset.Miasma => new Color(0.6f, 0.42f, 0.85f, 0.22f),
            ZoneEffectPreset.SnailSlime => new Color(0.75f, 0.85f, 0.45f, 0.22f),
            ZoneEffectPreset.Water => new Color(0.37f, 0.6f, 0.66f, 0.22f),
            _ => new Color(0.5f, 0.88f, 0.69f, 0.22f),
        };
#endif
    }
}
