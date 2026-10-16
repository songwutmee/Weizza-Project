using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The boss introduces itself: rears up, roars, and its name fades up in the
    /// middle of the screen like an area title. Picked by id as a phase's entry move, so it runs
    /// once when the fight opens and the fight carries on from there.
    ///
    /// The player is held still to watch it. The roar is felt as well as heard:
    /// shockwaves bend the screen outward from the mouth, the camera gives one
    /// short shake per beat of the sound, and leaves near the boss blow away.
    /// The name card comes after the roar, the player moves again, and the
    /// boss holds still until the name has gone before it swings.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Roar Skill")]
    public sealed class RoarSkill : BossSkill
    {
        [BoxGroup("Card", centerLabel: true)]
        [Tooltip("Seconds the name stays in the middle of the screen, the same card an area title uses.")]
        [PropertyRange(1f, 5f)]
        [SerializeField] private float cardSeconds = 2.4f;

        [BoxGroup("Roar", centerLabel: true)]
        [SerializeField] private string roarSound = "boss.beetle.roar";

        [BoxGroup("Roar")]
        [PropertyRange(0.2f, 1.5f), Tooltip("Seconds it rears back before the roar comes out.")]
        [SerializeField] private float rearSeconds = 0.55f;

        [BoxGroup("Roar")]
        [PropertyRange(0.5f, 3f)]
        [SerializeField] private float roarSeconds = 1.5f;

        [BoxGroup("Roar")]
        [PropertyRange(2, 10), Tooltip("Shockwaves pushed out while it roars, one per beat of the sound.")]
        [SerializeField] private int rings = 4;

        [BoxGroup("Roar")]
        [Tooltip("Where the mouth is, from the boss's feet, for the beetle facing left.")]
        [SerializeField] private Vector2 mouthOffset = new(-2.1f, 1.65f);

        private readonly List<GameObject> _spawned = new();
        private IDisposable _lock;
        private BeetleRig _rig;
        private EnemyController _body;
        private SpriteRenderer _source;

        private void Awake()
        {
            _rig = GetComponentInChildren<BeetleRig>(true);
            _body = GetComponent<EnemyController>();
            var feel = GetComponentInChildren<SpriteFeel>();
            _source = feel != null ? feel.GetComponent<SpriteRenderer>() : null;
        }

        public override async UniTask Run(MiniBossBrain brain, CancellationToken ct)
        {
            // The player watches the roar, then gets the fight. Released in Abort, which
            // always runs, so a cancelled roar can never leave the player frozen.
            _lock = StoryKit.StoryControl.Lock("Boss roar");
            // Nothing on screen but the roar. The HUD returns with the name card.
            HudScreen.Instance?.SetVisible(false);
            try
            {
                if (_body != null) _body.Held = true;
                FacePlayer();

                // Let the petal burst finish before anything else asks for attention.
                await UniTask.Delay(TimeSpan.FromSeconds(0.35f), cancellationToken: ct);

                _rig?.HoldPose("Rear", rearSeconds + 0.1f);
                await UniTask.Delay(TimeSpan.FromSeconds(rearSeconds), cancellationToken: ct);

                _rig?.HoldPose("Roar", roarSeconds);
                AudioDirector.Instance?.PlayAt(roarSound, transform.position);
                HitStop.Freeze(6);

                // Shockwaves leave the mouth on the beat of the rasp, each with one short
                // shake, the first one hardest. Small and quick on purpose: the bend in
                // the picture carries the roar, the shake only punctuates it.
                var gap = roarSeconds / rings;
                LiftLeaves(Mouth());
                for (var i = 0; i < rings; i++)
                {
                    var falloff = 1f - i / (float)rings;
                    ScreenShockwave.Play(Mouth(), 0.75f, Mathf.Lerp(0.025f, 0.06f, falloff), 1.5f);
                    CameraFeel.Instance?.Shake(Mathf.Lerp(0.08f, 0.28f, falloff), i % 2 == 0 ? Vector2.right : Vector2.up);
                    // Each wave of the roar bends the plants back from the beetle.
                    WindField.Ripple(Mouth(), Mathf.Lerp(0.15f, 0.4f, falloff));
                    await UniTask.Delay(TimeSpan.FromSeconds(gap), cancellationToken: ct);
                }

                // The name comes once the roar is over. The player gets control back as it
                // appears, and the boss only watches until the name has gone, so the first
                // swing never lands on someone who has not had a chance to move yet.
                _rig?.HoldPose(null, 0f);
                HudScreen.Instance?.SetVisible(true);
                HudScreen.Instance?.TitleCard(brain.BossName, cardSeconds);
                _lock?.Dispose();
                _lock = null;
                await UniTask.Delay(TimeSpan.FromSeconds(cardSeconds + 1.2f), cancellationToken: ct);
            }
            finally
            {
                Abort();
            }
        }

        public override void Abort()
        {
            HudScreen.Instance?.SetVisible(true);
            _lock?.Dispose();
            _lock = null;
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
            _rig?.HoldPose(null, 0f);
            if (_body != null) _body.Held = false;
        }

        private void FacePlayer()
        {
            var player = PlayerLocator.Player;
            if (player == null || _source == null) return;
            // flipX true means facing left, the same rule SpriteFacing uses.
            _source.flipX = player.transform.position.x < transform.position.x;
        }

        private Vector3 Mouth()
        {
            var facingLeft = _source == null || _source.flipX;
            var x = facingLeft ? mouthOffset.x : -mouthOffset.x;
            return transform.position + new Vector3(x, mouthOffset.y, -0.6f);
        }

        // A handful of leaves torn up and blown away from the mouth.
        private void LiftLeaves(Vector3 from)
        {
            var away = _source != null && _source.flipX ? -1f : 1f;
            for (var i = 0; i < 14; i++)
            {
                var velocity = new Vector2(away * UnityEngine.Random.Range(4f, 10f), UnityEngine.Random.Range(1f, 5f));
                BlowLeaf(from + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-0.8f, 0.4f), 0f), velocity).Forget();
            }
        }

        private async UniTaskVoid BlowLeaf(Vector3 at, Vector2 velocity)
        {
            var go = new GameObject("Roar Leaf");
            _spawned.Add(go);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ProceduralSprites.Diamond(32);
            var green = UnityEngine.Random.Range(0.35f, 0.6f);
            sr.color = new Color(green * 0.45f, green, green * 0.5f, 0.95f);
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 44;
            go.transform.localScale = new Vector3(0.12f, 0.26f, 1f);
            var spin = UnityEngine.Random.Range(-720f, 720f);

            const float life = 1.3f;
            var t = 0f;
            var token = destroyCancellationToken;
            while (t < life && go != null)
            {
                var dt = Time.deltaTime;
                t += dt;
                velocity *= 1f - dt * 1.8f;
                velocity.y -= 3f * dt;
                go.transform.position += (Vector3)(velocity * dt);
                go.transform.Rotate(0f, 0f, spin * dt);
                sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0.95f * (1f - t / life));
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            if (go != null) Destroy(go);
            _spawned.Remove(go);
        }
    }
}
