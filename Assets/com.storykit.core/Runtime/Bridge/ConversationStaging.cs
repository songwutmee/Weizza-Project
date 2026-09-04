using UnityEngine;
using Cysharp.Threading.Tasks;

namespace StoryKit.Bridge
{
    /// <summary>
    /// The small thing that makes a conversation look like one.
    ///
    /// Two characters standing back to back while a dialogue box says they are
    /// talking is the fastest way to make a scene read as broken, and nobody
    /// writes a node for it because it feels like something the engine should
    /// already do. So it happens here, automatically, when a graph starts from
    /// this trigger.
    ///
    /// Both turns are optional and both are smoothed. Nothing is teleported,
    /// because a character snapping ninety degrees is worse than not turning.
    /// </summary>
    [AddComponentMenu("StoryKit/Conversation Staging")]
    [DisallowMultipleComponent]
    public sealed class ConversationStaging : MonoBehaviour
    {
        [Tooltip("Turn this object to face the player when a conversation starts. " +
                 "Leave the object itself here, or point at the model if the " +
                 "collider and the body are separate.")]
        [SerializeField] private Transform _speaker;

        [Tooltip("Turn the player to face this object as well. Off for games " +
                 "where the player keeps control of their own facing.")]
        [SerializeField] private bool _turnPlayer = true;

        [Tooltip("Degrees per second. Around 360 reads as a natural turn; " +
                 "anything above 1000 looks like a snap.")]
        [SerializeField, Min(30f)] private float _turnSpeed = 480f;

        [Tooltip("Do not turn for a difference smaller than this, in degrees. " +
                 "Stops a tiny correcting twitch when they already face roughly " +
                 "the right way.")]
        [SerializeField, Range(0f, 45f)] private float _deadZone = 8f;

        [Header("Cinematics")]
        [Tooltip("Automatically focus the camera on the speaker.")]
        [SerializeField] private bool _focusCamera = true;
        
        [Tooltip("How long the camera blend takes.")]
        [SerializeField, Min(0f)] private float _focusBlendTime = 1f;

        private Transform _turnTarget;
        private Transform _playerTarget;
        private bool _active;

        private void Awake()
        {
            if (_speaker == null) _speaker = transform;
        }

        /// <summary>
        /// Starts the turn. Called by the trigger that owns the conversation,
        /// so no graph has to remember to do it.
        /// </summary>
        public void FaceEachOther()
        {
            var player = StoryPlayer.Transform;
            if (player == null) return;

            _turnTarget = player;
            _playerTarget = _turnPlayer ? _speaker : null;
            _active = true;

            if (_focusCamera)
            {
                var cam = StoryServices.Get<ICameraService>();
                if (cam != null) cam.FocusAsync(_speaker, _focusBlendTime, default).Forget();
            }
        }

        /// <summary>Stops turning. The conversation ending is enough.</summary>
        public void Release()
        {
            _active = false;
            
            if (_focusCamera)
            {
                var cam = StoryServices.Get<ICameraService>();
                if (cam != null) cam.ReturnToDefault(_focusBlendTime);
            }
        }

        private void LateUpdate()
        {
            if (!_active) return;

            bool speakerDone = TurnToward(_speaker, _turnTarget);
            bool playerDone = _playerTarget == null
                              || TurnToward(StoryPlayer.Transform, _playerTarget);

            // Once both are looking the right way there is nothing left to do,
            // so this stops costing anything for the rest of the conversation.
            if (speakerDone && playerDone) _active = false;
        }

        /// <summary>Turns one transform toward another. True when it is close enough.</summary>
        private bool TurnToward(Transform mover, Transform target)
        {
            if (mover == null || target == null) return true;

            var flat = target.position - mover.position;
            flat.y = 0f;                       // never tip a character backwards
            if (flat.sqrMagnitude < 0.0001f) return true;

            var wanted = Quaternion.LookRotation(flat);
            if (Quaternion.Angle(mover.rotation, wanted) <= _deadZone) return true;

            mover.rotation = Quaternion.RotateTowards(
                mover.rotation, wanted, _turnSpeed * Time.deltaTime);

            return Quaternion.Angle(mover.rotation, wanted) <= 0.5f;
        }
    }
}
