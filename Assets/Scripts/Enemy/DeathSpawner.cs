using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Leaves something behind when this character dies: a spore cloud, a health
    /// mote, a key, a smaller enemy. A Spore Puff that bursts into poison is this
    /// component plus an Effect Zone prefab, with no new enemy code.
    /// </summary>
    [AddComponentMenu("Weizza/Enemy/Death Spawner")]
    [RequireComponent(typeof(Vitals))]
    public sealed class DeathSpawner : MonoBehaviour
    {
        [Serializable]
        public sealed class Drop
        {
            [Required] public GameObject prefab;

            [Tooltip("Offset from the body, X mirrored with facing.")]
            public Vector3 offset = new(0f, 0.5f, 0f);

            [PropertyRange(0f, 1f)]
            public float chance = 1f;

            [PropertyRange(1, 10)]
            public int count = 1;

            [Tooltip("Random spread per copy, in metres.")]
            public float scatter = 0.3f;

            [Tooltip("Seconds before the spawned object is removed. 0 keeps it.")]
            public float lifetime;
        }

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [SerializeField] private Drop[] drops = Array.Empty<Drop>();

        private Vitals _vitals;

        private void Awake() => _vitals = GetComponent<Vitals>();
        private void OnEnable() { if (_vitals != null) _vitals.Died += OnDied; }
        private void OnDisable() { if (_vitals != null) _vitals.Died -= OnDied; }

        private void OnDied()
        {
            var motor = GetComponent<CharacterMotor3D>();
            var sign = motor != null ? motor.Facing.Sign() : 1f;
            foreach (var drop in drops)
            {
                if (drop == null || drop.prefab == null) continue;
                for (var i = 0; i < drop.count; i++)
                {
                    if (UnityEngine.Random.value > drop.chance) continue;
                    var at = transform.position + new Vector3(drop.offset.x * sign, drop.offset.y, drop.offset.z);
                    at += new Vector3(UnityEngine.Random.Range(-drop.scatter, drop.scatter), UnityEngine.Random.Range(0f, drop.scatter), 0f);
                    // Pooled, so a room full of spore puffs dying at once costs no
                    // allocations, and parented under the pool root, which outlives
                    // every room scene.
                    PrefabPool.Spawn(drop.prefab, at, Quaternion.identity, drop.lifetime);
                }
            }
        }
    }
}
