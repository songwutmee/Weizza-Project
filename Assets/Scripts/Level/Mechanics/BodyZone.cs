using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A trigger volume that knows which characters are inside it. Wind,
    /// updrafts, miasma, slime and hazards all start from this.
    ///
    /// Bodies are tracked by their motor, not by collider, so a character
    /// with several colliders counts once, and one that is destroyed while
    /// inside is dropped on the next frame rather than left as a ghost entry.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public abstract class BodyZone : MonoBehaviour
    {
        [BoxGroup("Zone")]
        [ToggleLeft]
        [Tooltip("Off affects the player only. On affects enemies too.")]
        [SerializeField] protected bool affectsEnemies = true;

        protected readonly List<CharacterMotor3D> Inside = new(4);

        protected virtual void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4f, 4f, 2f);
            box.center = new Vector3(0f, 2f, 0f);
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            var body = LevelPiece.BodyOf(other);
            if (body == null || Inside.Contains(body)) return;
            if (!affectsEnemies && !PlayerLocator.IsPlayer(body)) return;
            Inside.Add(body);
            OnEntered(body);
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            var body = LevelPiece.BodyOf(other);
            if (body == null || !Inside.Remove(body)) return;
            OnExited(body);
        }

        protected virtual void OnDisable()
        {
            for (var i = Inside.Count - 1; i >= 0; i--)
            {
                if (Inside[i] != null) OnExited(Inside[i]);
            }
            Inside.Clear();
        }

        protected virtual void Update()
        {
            for (var i = Inside.Count - 1; i >= 0; i--)
            {
                var body = Inside[i];
                if (body == null || !body.isActiveAndEnabled)
                {
                    Inside.RemoveAt(i);
                    continue;
                }
                WhileInside(body, Time.deltaTime);
            }
        }

        protected virtual void OnEntered(CharacterMotor3D body) { }
        protected virtual void OnExited(CharacterMotor3D body) { }
        protected virtual void WhileInside(CharacterMotor3D body, float dt) { }

#if UNITY_EDITOR
        protected virtual Color GizmoColour => new(0.5f, 0.8f, 1f, 0.25f);

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = GizmoColour;
            Gizmos.DrawCube(box.center, box.size);
            var edge = GizmoColour;
            edge.a = 0.9f;
            Gizmos.color = edge;
            Gizmos.DrawWireCube(box.center, box.size);
        }
#endif
    }
}
