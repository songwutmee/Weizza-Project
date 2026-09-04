using UnityEngine;

namespace StoryKit.Defaults
{
    /// <summary>
    /// Frictionless animation sync. Drop this on any character (Player, Boss, NPC) 
    /// that has an Animator. It automatically feeds locomotion data into the Animator 
    /// if the parameters exist.
    /// </summary>
    [AddComponentMenu("StoryKit/Simple Animation Controller")]
    public sealed class SimpleAnimationController : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        
        private AdvancedPlayerController _playerController;
        private Rigidbody _body;
        
        // Parameter hashes for performance
        private int _speedHash = Animator.StringToHash("Speed");
        private int _isGroundedHash = Animator.StringToHash("IsGrounded");
        private int _isSprintingHash = Animator.StringToHash("IsSprinting");

        private bool _hasSpeedParam;
        private bool _hasGroundedParam;
        private bool _hasSprintingParam;

        private void Awake()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;

            _playerController = GetComponent<AdvancedPlayerController>();
            _body = GetComponent<Rigidbody>();

            CacheParameters();
        }

        private void CacheParameters()
        {
            foreach (var param in _animator.parameters)
            {
                if (param.nameHash == _speedHash) _hasSpeedParam = true;
                if (param.nameHash == _isGroundedHash) _hasGroundedParam = true;
                if (param.nameHash == _isSprintingHash) _hasSprintingParam = true;
            }
        }

        private void Update()
        {
            if (_animator == null) return;

            float speed = 0f;
            bool isGrounded = true;
            bool isSprinting = false;

            if (_playerController != null)
            {
                speed = new Vector3(_playerController.Velocity.x, 0f, _playerController.Velocity.z).magnitude;
                isGrounded = _playerController.IsGrounded;
                isSprinting = _playerController.IsSprinting;
            }
            else if (_body != null)
            {
                speed = new Vector3(_body.linearVelocity.x, 0f, _body.linearVelocity.z).magnitude;
                isGrounded = Mathf.Abs(_body.linearVelocity.y) < 0.1f;
            }

            if (_hasSpeedParam) _animator.SetFloat(_speedHash, speed);
            if (_hasGroundedParam) _animator.SetBool(_isGroundedHash, isGrounded);
            if (_hasSprintingParam) _animator.SetBool(_isSprintingHash, isSprinting);
        }

        /// <summary>
        /// Used by visual scripting to play an arbitrary animation by name easily.
        /// It tries to fire a Trigger, or set a Bool.
        /// </summary>
        public void PlayAnimation(string animName)
        {
            if (_animator == null || string.IsNullOrEmpty(animName)) return;

            foreach (var param in _animator.parameters)
            {
                if (param.name == animName)
                {
                    if (param.type == AnimatorControllerParameterType.Trigger)
                    {
                        _animator.SetTrigger(param.nameHash);
                    }
                    else if (param.type == AnimatorControllerParameterType.Bool)
                    {
                        _animator.SetBool(param.nameHash, true);
                    }
                    return;
                }
            }

            // Fallback: crossfade to state directly if no parameter exists
            _animator.CrossFadeInFixedTime(animName, 0.2f);
        }
    }
}
