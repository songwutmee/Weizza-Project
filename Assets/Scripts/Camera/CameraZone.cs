using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Switches to a hand placed camera while the player is inside: a wider
    /// shot for an arena, a closer one for a tight shaft, a locked frame for a
    /// boss room.
    ///
    /// Does nothing but switch the camera object on and off. The Cinemachine
    /// brain blends to whichever camera is active with the highest priority,
    /// so no Cinemachine API is touched here and a package update cannot break
    /// it. Give the zone camera a priority above CM_Follow.
    /// </summary>
    [AddComponentMenu("Weizza/Camera Zone")]
    public sealed class CameraZone : BodyZone
    {
        [BoxGroup("Camera")]
        [Required]
        [Tooltip("A Cinemachine Camera object, switched off by default, with a higher priority than the follow camera.")]
        [SerializeField] private GameObject zoneCamera;

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
        }

        private void Awake()
        {
            if (zoneCamera != null) zoneCamera.SetActive(false);
        }

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (PlayerLocator.IsPlayer(body) && zoneCamera != null) zoneCamera.SetActive(true);
        }

        protected override void OnExited(CharacterMotor3D body)
        {
            if (PlayerLocator.IsPlayer(body) && zoneCamera != null) zoneCamera.SetActive(false);
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.85f, 0.85f, 1f, 0.08f);
#endif
    }
}
