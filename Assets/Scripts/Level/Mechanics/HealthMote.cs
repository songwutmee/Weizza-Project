using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A small drifting light that heals on touch. Pops out with a little
    /// scatter, settles, then drifts to the player once they are close, so
    /// collecting it never asks for precise platforming.
    ///
    /// Mint, the healing colour in the colour table.
    /// </summary>
    [AddComponentMenu("Weizza/Health Mote")]
    public sealed class HealthMote : MonoBehaviour, IPoolable
    {
        private static ComponentPool<HealthMote> _pool;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _pool = null;

        private static readonly Color Mint = new(0.498f, 0.878f, 0.69f);

        private SpriteRenderer _renderer;
        private Vector3 _velocity;
        private float _heal;
        private float _age;

        private const float AttractRange = 3f;
        private const float CollectRange = 0.55f;
        private const float Lifetime = 12f;

        public static void Spawn(Vector3 at, float heal)
        {
            _pool ??= new ComponentPool<HealthMote>(Create, PoolRoot.Child("Motes"));
            var mote = _pool.Take();
            mote.transform.position = at + Vector3.up * 0.6f;
            mote._velocity = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(3f, 5.5f), 0f);
            mote._heal = heal;
            mote._age = 0f;
        }

        private static HealthMote Create()
        {
            var go = new GameObject("Health Mote");
            var mote = go.AddComponent<HealthMote>();
            mote._renderer = go.AddComponent<SpriteRenderer>();
            mote._renderer.sprite = ProceduralSprites.Dot();
            mote._renderer.color = Mint;
            mote._renderer.sortingLayerName = "Foreground";
            mote._renderer.sortingOrder = 40;
            go.transform.localScale = Vector3.one * 0.35f;
            return mote;
        }

        public void OnTakenFromPool() { }
        public void OnReturnedToPool() { }

        private void Update()
        {
            var dt = Time.deltaTime;
            _age += dt;
            if (_age > Lifetime)
            {
                _pool?.Return(this);
                return;
            }

            var player = PlayerLocator.Player;
            var position = transform.position;

            if (player != null && _age > 0.4f)
            {
                var target = player.transform.position + Vector3.up * 0.9f;
                var to = target - position;
                if (to.magnitude < CollectRange)
                {
                    player.Vitals?.Heal(_heal);
                    player.GetComponentInChildren<SpriteFeel>()?.Flash(Mint);
                    _pool?.Return(this);
                    return;
                }

                if (to.magnitude < AttractRange)
                {
                    _velocity = Vector3.MoveTowards(_velocity, to.normalized * 9f, 30f * dt);
                    transform.position = position + _velocity * dt;
                    Pulse();
                    return;
                }
            }

            // Pop and settle: fall under light gravity, slow sideways, hover once slow.
            _velocity.y -= 9f * dt;
            _velocity.x = Mathf.MoveTowards(_velocity.x, 0f, 3f * dt);
            if (_age > 0.8f) _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, 12f * dt);
            transform.position = position + _velocity * dt;
            Pulse();
        }

        private void Pulse()
        {
            var c = Mint;
            c.a = 0.75f + Mathf.Sin(Time.time * 6f + transform.position.x) * 0.25f;
            _renderer.color = c;
        }
    }
}
