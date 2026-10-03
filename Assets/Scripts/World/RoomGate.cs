using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A Hollow Knight style exit. Walk into it, the screen fades, the room on the
    /// other side loads if it is not already, and the player steps out of the
    /// matching gate there.
    ///
    /// Gates come in pairs and each one names the other, by room id and gate id,
    /// never by scene reference: two scenes cannot reference each other's objects,
    /// and an id survives the scene being renamed.
    ///
    /// Rooms linked Seamless need no gate at all, the player just walks across.
    /// </summary>
    [AddComponentMenu("Weizza/World/Room Gate")]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RoomGate : MonoBehaviour
    {
        public enum Side { Left, Right, Top, Bottom }

        private static readonly List<RoomGate> AllGates = new();
        private static bool _travelling;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AllGates.Clear();
            _travelling = false;
        }

        [BoxGroup("This Gate")]
        [Tooltip("Unique inside its room, e.g. left, right, top_shaft.")]
        [SerializeField] private string gateId = "right";

        [BoxGroup("This Gate")]
        [Tooltip("Which edge of the room this gate is on. Decides which way the player faces and moves when arriving here.")]
        [SerializeField] private Side side = Side.Right;

        [BoxGroup("This Gate")]
        [Tooltip("Where the player stands when arriving through this gate. Keep it a metre inside the trigger, or arriving would send them straight back.")]
        [SerializeField] private Transform arrivalPoint;

        [BoxGroup("Leads To")]
        [Tooltip("Room id on the other side.")]
        [SerializeField] private string targetRoom = "forest_02";

        [BoxGroup("Leads To")]
        [SerializeField] private string targetGate = "left";

        [BoxGroup("Feel")]
        [PropertyRange(0.05f, 1f)]
        [SerializeField] private float fadeSeconds = 0.25f;

        [BoxGroup("Feel")]
        [PropertyRange(0f, 20f)]
        [Tooltip("Upward speed given when arriving through a Bottom gate, so the player pops up out of the shaft the way Hollow Knight does it.")]
        [SerializeField] private float popUpSpeed = 14f;

        [BoxGroup("Feel")]
        [ToggleLeft]
        [Tooltip("Off makes this an arrival only gate: a one way drop, or a door opened later.")]
        [SerializeField] private bool canLeave = true;

        // A gate the player arrives at is disarmed until they step out of it,
        // otherwise arriving inside the trigger would bounce them straight back.
        private bool _armed = true;

        public string GateId => gateId;
        public RoomRoot Room { get; private set; }

        public Vector3 ArrivalPoint => arrivalPoint != null ? arrivalPoint.position : transform.position;

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1f, 4f, 6f);
            box.center = new Vector3(0f, 2f, 0f);
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake() => Room = GetComponentInParent<RoomRoot>();
        private void OnEnable() => AllGates.Add(this);
        private void OnDisable() => AllGates.Remove(this);

        private void OnTriggerEnter(Collider other)
        {
            if (!canLeave || !_armed || _travelling || !PlayerLocator.IsPlayer(other)) return;
            if (StoryControl.MovementLocked) return;
            TravelAsync(PlayerLocator.Player, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void OnTriggerExit(Collider other)
        {
            if (PlayerLocator.IsPlayer(other)) _armed = true;
        }

        public static RoomGate Find(string roomId, string gateId)
        {
            foreach (var gate in AllGates)
            {
                if (gate.gateId != gateId) continue;
                var room = gate.Room != null ? gate.Room.Definition : null;
                if (room != null && room.id == roomId) return gate;
            }
            return null;
        }

        /// <summary>
        /// Moves the player to any gate in any room. Public so a flow graph node,
        /// the dev console and a fast travel shrine can all use the same path.
        /// </summary>
        public static async UniTask TravelToAsync(string roomId, string gateId, float fade, CancellationToken ct)
        {
            var player = PlayerLocator.Player;
            if (player == null || _travelling) return;
            _travelling = true;
            try
            {
                using (StoryControl.Lock("room.gate"))
                {
                    var screen = ScreenFade.Instance;
                    if (screen != null) await screen.ToAsync(1f, fade, ct);

                    if (WorldStreamer.Instance != null) await WorldStreamer.Instance.EnsureLoadedAsync(roomId, ct);

                    // A freshly loaded scene registers its gates in OnEnable, which
                    // has run by the time the load reports done. One frame anyway,
                    // for the physics scene to pick up the new colliders.
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);

                    var target = Find(roomId, gateId);
                    if (target == null)
                    {
                        Debug.LogWarning($"[RoomGate] No gate '{gateId}' in room '{roomId}'. Check both gates name each other.");
                    }
                    else
                    {
                        target.Arrive(player);
                    }

                    StoryEvents.Raise(GameEvents.GateUsed, roomId);
                    await UniTask.DelayFrame(2, cancellationToken: ct);
                    if (screen != null) await screen.ToAsync(0f, fade, ct);
                }
            }
            finally
            {
                _travelling = false;
            }
        }

        private async UniTaskVoid TravelAsync(PlayerController player, CancellationToken ct)
        {
            try
            {
                await TravelToAsync(targetRoom, targetGate, fadeSeconds, ct);
            }
            catch (System.OperationCanceledException) { }
        }

        private void Arrive(PlayerController player)
        {
            _armed = false;
            var motor = player.Motor;
            motor.Teleport(ArrivalPoint);
            motor.Velocity = Vector3.zero;

            // Face away from the edge the player came through, into the room.
            switch (side)
            {
                case Side.Left: motor.SetFacing(Facing.Right); break;
                case Side.Right: motor.SetFacing(Facing.Left); break;
                case Side.Bottom: motor.Velocity.y = popUpSpeed; break;
            }

            Physics.SyncTransforms();
            if (Room != null)
            {
                WorldStreamer.Instance?.Enter(Room.Definition);
                Room.ApplyCameraBounds();
            }
            CameraAnchor.CutToPlayer();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = canLeave ? new Color(1f, 0.85f, 0.3f, 0.35f) : new Color(0.6f, 0.6f, 0.6f, 0.35f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(ArrivalPoint, 0.25f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * (box.size.y + 0.4f),
                $"{gateId} -> {targetRoom}/{targetGate}");
        }
#endif
    }
}
