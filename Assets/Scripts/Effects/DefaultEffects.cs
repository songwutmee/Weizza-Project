using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Effects built in code, used whenever a zone has no effect asset
    /// assigned. A miasma zone dropped into a room works the moment it is
    /// placed; an authored asset replaces these once someone wants to tune.
    /// Effect Forge writes these same numbers out as real assets.
    /// </summary>
    public static class DefaultEffects
    {
        private static GameplayEffect _poison;
        private static GameplayEffect _slime;
        private static GameplayEffect _regen;
        private static GameplayEffect _wet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _poison = null;
            _slime = null;
            _regen = null;
            _wet = null;
        }

        /// <summary>Miasma. Ticks while inside, refused by PoisonImmune (Se).</summary>
        public static GameplayEffect Poison => _poison != null ? _poison : _poison = Make(e =>
        {
            e.id = "poison";
            e.displayName = "Poison";
            e.duration = 2.5f;
            e.period = 0.5f;
            e.damagePerTick = 3f;
            e.stacking = EffectStacking.Refresh;
            e.grantsTags = new[] { GameTag.Poisoned };
            e.blockedByTag = true;
            e.blockingTag = GameTag.PoisonImmune;
            e.tickTint = new Color(0.604f, 0.42f, 0.847f);
        });

        /// <summary>Snail slime. Walking is a crawl, a dash is not slowed at all.</summary>
        public static GameplayEffect Slime => _slime != null ? _slime : _slime = Make(e =>
        {
            e.id = "slime";
            e.displayName = "Slime";
            e.duration = 0f;
            e.moveSpeed = 0.25f;
            e.flashOnTick = false;
        });

        /// <summary>Shrine and resting regeneration.</summary>
        public static GameplayEffect Regen => _regen != null ? _regen : _regen = Make(e =>
        {
            e.id = "regen";
            e.displayName = "Regen";
            e.duration = 3f;
            e.period = 0.25f;
            e.healPerTick = 4f;
            e.tickTint = new Color(0.498f, 0.878f, 0.69f);
        });

        /// <summary>Standing in water. Slower, nothing else.</summary>
        public static GameplayEffect Wet => _wet != null ? _wet : _wet = Make(e =>
        {
            e.id = "wet";
            e.displayName = "Wet";
            e.duration = 0f;
            e.moveSpeed = 0.7f;
            e.grantsTags = new[] { GameTag.Wet };
            e.flashOnTick = false;
        });

        private static GameplayEffect Make(System.Action<GameplayEffect> setup)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.hideFlags = HideFlags.DontSave;
            setup(effect);
            effect.name = "Default " + effect.displayName;
            return effect;
        }
    }
}
