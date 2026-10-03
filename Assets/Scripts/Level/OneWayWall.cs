using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A wall that is only solid once the player is past it. The opening walk-in
    /// starts off screen to the left of the level, so a plain wall at the level's
    /// left edge would stop the walk before it begins. This one stays open while the
    /// player is behind it and closes the moment they are through, so nobody can walk
    /// back out of the frame afterwards. If the player is ever put behind it again
    /// (a respawn at the very start), it simply opens.
    /// </summary>
    [AddComponentMenu("Weizza/Level/One Way Wall")]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class OneWayWall : MonoBehaviour
    {
        [Tooltip("How far past the wall the player has to be before it closes, in metres. " +
                 "Keeps it from closing on top of the player's own capsule.")]
        [SerializeField] private float closeMargin = 0.6f;

        private BoxCollider _box;
        private float _nextCheck;
        private float _rightEdge;

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            // A disabled collider reports empty bounds, so read the edge first.
            _rightEdge = transform.TransformPoint(_box.center + Vector3.right * _box.size.x * 0.5f).x;
            _box.enabled = false;
        }

        // Four checks a second is plenty for a wall the player walks through once.
        private void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.25f;

            var player = PlayerLocator.Player;
            if (player == null) return;
            var past = player.transform.position.x > _rightEdge + closeMargin;
            if (_box.enabled != past) _box.enabled = past;
        }
    }
}
