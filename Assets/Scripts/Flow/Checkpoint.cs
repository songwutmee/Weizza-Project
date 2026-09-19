using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A place worth waking up at. Walking through one tells the respawn
    /// director where to put the player back.
    ///
    /// Deliberately a trigger the player walks into rather than a list of
    /// positions on the director: a checkpoint is a thing in the level, and
    /// moving a piece of level around should move its checkpoint with it.
    /// </summary>
    [AddComponentMenu("Weizza/Checkpoint")]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Checkpoint : MonoBehaviour
    {
        [BoxGroup("Placement")]
        [InfoBox("The respawn point is this object's position, not the point " +
                 "the player touched the trigger at. Put it on solid ground.")]
        [Tooltip("Lifted a little so a respawn never starts inside the floor.")]
        [SerializeField] private float standOffset = 0.05f;

        [BoxGroup("Placement")]
        [Tooltip("Later checkpoints win over earlier ones, so a player who " +
                 "walks back down the level keeps the furthest point reached.")]
        [SerializeField] private int order;

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly]
        public bool Claimed { get; private set; }

        public int Order => order;
        public Vector3 RespawnPoint => transform.position + Vector3.up * standOffset;

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.5f, 3f, 2f);
            box.center = new Vector3(0f, 1.5f, 0f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Claimed) return;
            if (other.GetComponentInParent<PlayerController>() == null) return;

            Claimed = true;
            RespawnDirector.Instance?.Claim(this);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Claimed
                ? new Color(0.4f, 0.85f, 0.5f, 0.9f)
                : new Color(0.9f, 0.8f, 0.35f, 0.6f);
            Gizmos.DrawWireSphere(RespawnPoint, 0.35f);
            Gizmos.DrawLine(RespawnPoint, RespawnPoint + Vector3.up * 2f);
        }
    }
}
