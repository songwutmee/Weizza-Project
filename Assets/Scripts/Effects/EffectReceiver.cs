using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Runs the gameplay effects on one character: ticks damage and healing,
    /// holds the tags each effect grants, and answers the speed and damage
    /// multipliers everything else asks for.
    ///
    /// Ticks on scaled time, so hit stop and aim slow pause poison along with
    /// everything else. A poison that keeps ticking during a hit freeze reads
    /// as damage from nowhere.
    /// </summary>
    [AddComponentMenu("Weizza/Effect Receiver")]
    [RequireComponent(typeof(Vitals))]
    public sealed class EffectReceiver : MonoBehaviour
    {
        private sealed class Running
        {
            public GameplayEffect effect;
            public float remaining;
            public float nextTick;
            public int stacks;
            public readonly List<IDisposable> tags = new(2);
            public object source;
        }

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to find one in children. Flashes the tick tint.")]
        [SerializeField] private SpriteFeel spriteFeel;

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly, DisplayAsString, HideLabel]
        private string Readout
        {
            get
            {
                if (_running.Count == 0) return "no effects";
                var parts = new List<string>();
                foreach (var r in _running)
                {
                    var time = r.effect.duration <= 0f ? "held" : $"{r.remaining:0.0}s";
                    parts.Add(r.stacks > 1 ? $"{r.effect.displayName} x{r.stacks} ({time})" : $"{r.effect.displayName} ({time})");
                }
                return string.Join(", ", parts);
            }
        }

        public event Action<GameplayEffect> Applied;
        public event Action<GameplayEffect> Removed;

        /// <summary>Product of every running effect's speed multiplier.</summary>
        public float MoveSpeedMultiplier { get; private set; } = 1f;

        /// <summary>Product of every running effect's damage taken multiplier.</summary>
        public float DamageTakenMultiplier { get; private set; } = 1f;

        private readonly List<Running> _running = new(4);
        private Vitals _vitals;
        private TagContainer _tags;

        private void Awake()
        {
            _vitals = GetComponent<Vitals>();
            _tags = this.Tags();
            if (spriteFeel == null) spriteFeel = GetComponentInChildren<SpriteFeel>();
            _vitals.RefreshTags();
        }

        private void OnDisable() => ClearAll();

        public bool Has(GameplayEffect effect) => Find(effect, null) != null;

        public bool HasId(string id)
        {
            foreach (var r in _running) if (r.effect.id == id) return true;
            return false;
        }

        /// <summary>
        /// Applies an effect. Source lets a zone remove exactly the copy it
        /// applied, so two overlapping miasma zones do not cure each other.
        /// Returns false when the effect was refused.
        /// </summary>
        public bool Apply(GameplayEffect effect, object source = null)
        {
            if (effect == null || _vitals == null || !_vitals.IsAlive) return false;
            if (effect.blockedByTag && _tags.Has(effect.blockingTag)) return false;

            var existing = Find(effect, effect.stacking == EffectStacking.Stack ? null : source);
            if (existing == null) existing = Find(effect, null);

            if (existing != null)
            {
                switch (effect.stacking)
                {
                    case EffectStacking.Ignore:
                        return false;
                    case EffectStacking.Refresh:
                        existing.remaining = effect.duration;
                        return true;
                    case EffectStacking.Stack:
                        existing.remaining = effect.duration;
                        existing.stacks = Mathf.Min(effect.maxStacks, existing.stacks + 1);
                        Recalculate();
                        return true;
                }
            }

            var running = new Running
            {
                effect = effect,
                remaining = effect.duration,
                nextTick = effect.period,
                stacks = 1,
                source = source,
            };

            if (effect.grantsTags != null)
            {
                foreach (var tag in effect.grantsTags) running.tags.Add(_tags.Add(tag));
            }

            _running.Add(running);
            Recalculate();
            Applied?.Invoke(effect);
            return true;
        }

        /// <summary>Removes the copy a source applied, or every copy when source is null.</summary>
        public void Remove(GameplayEffect effect, object source = null)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var r = _running[i];
                if (r.effect != effect) continue;
                if (source != null && !ReferenceEquals(r.source, source)) continue;
                End(i);
            }
        }

        public void RemoveById(string id)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                if (_running[i].effect.id == id) End(i);
            }
        }

        /// <summary>Removes everything that deals damage. Shrines call this.</summary>
        public void Cleanse()
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                if (_running[i].effect.damagePerTick > 0f) End(i);
            }
        }

        public void ClearAll()
        {
            for (var i = _running.Count - 1; i >= 0; i--) End(i);
        }

        private void Update()
        {
            if (_running.Count == 0) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var r = _running[i];

                // An effect refused by a tag the character gained after it
                // started (switching into Se mid poison) ends right away.
                if (r.effect.blockedByTag && _tags.Has(r.effect.blockingTag))
                {
                    End(i);
                    continue;
                }

                r.nextTick -= dt;
                while (r.nextTick <= 0f)
                {
                    r.nextTick += Mathf.Max(0.05f, r.effect.period);
                    Tick(r);
                    if (_vitals == null || !_vitals.IsAlive) return;
                }

                if (r.effect.duration <= 0f) continue;
                r.remaining -= dt;
                if (r.remaining <= 0f) End(i);
            }
        }

        private void Tick(Running r)
        {
            var effect = r.effect;

            if (effect.damagePerTick > 0f)
            {
                _vitals.Drain(effect.damagePerTick * r.stacks, effect.canKill);
            }

            if (effect.healPerTick > 0f) _vitals.Heal(effect.healPerTick * r.stacks);

            if (effect.flashOnTick && spriteFeel != null) spriteFeel.Flash(effect.tickTint);
        }

        private void End(int index)
        {
            var r = _running[index];
            foreach (var tag in r.tags) tag.Dispose();
            r.tags.Clear();
            _running.RemoveAt(index);
            Recalculate();
            Removed?.Invoke(r.effect);
        }

        private Running Find(GameplayEffect effect, object source)
        {
            foreach (var r in _running)
            {
                if (r.effect != effect) continue;
                if (source != null && !ReferenceEquals(r.source, source)) continue;
                return r;
            }
            return null;
        }

        private void Recalculate()
        {
            var speed = 1f;
            var damage = 1f;
            foreach (var r in _running)
            {
                for (var s = 0; s < r.stacks; s++)
                {
                    speed *= r.effect.moveSpeed;
                    damage *= r.effect.damageTaken;
                }
            }
            MoveSpeedMultiplier = speed;
            DamageTakenMultiplier = damage;
        }
    }
}
