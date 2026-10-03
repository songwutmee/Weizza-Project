using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The one object at the top of every room scene. Says which room this is,
    /// marks its bounds with a trigger, and owns the box the camera stays inside
    /// while the player is here.
    ///
    /// Everything else in the scene goes under it, so turning one object off
    /// hides the whole room, and the bounds and the content can never drift
    /// into different scenes.
    /// </summary>
    [AddComponentMenu("Weizza/World/Room Root")]
    [RequireComponent(typeof(BoxCollider))]
    [DefaultExecutionOrder(-900)]
    public sealed class RoomRoot : MonoBehaviour
    {
        private static readonly List<RoomRoot> AllRooms = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AllRooms.Clear();

        public static IReadOnlyList<RoomRoot> All => AllRooms;

        [BoxGroup("Room")]
        [Required]
        [SerializeField] private RoomDefinition definition;

        [BoxGroup("Room")]
        [Tooltip("Where the player appears when the game starts here, or a debug play begins in this scene. Empty uses the room centre.")]
        [SerializeField] private Transform defaultSpawn;

        [BoxGroup("Camera")]
        [Tooltip("Box the camera centre is held inside. Usually the room bounds shrunk by half a screen. Empty leaves the confiner alone.")]
        [SerializeField] private BoxCollider cameraBounds;

        [BoxGroup("Streaming")]
        [ToggleLeft]
        [Tooltip("Switch this chunk's contents on over several frames after it loads, instead of all in one frame. " +
                 "Leave on. Off is only for a tiny room where it does not matter.")]
        [SerializeField] private bool spreadActivation = true;

        [BoxGroup("Streaming")]
        [PropertyRange(1, 4)]
        [Tooltip("How deep into the hierarchy the pieces that switch on one at a time sit. 2 means each child of " +
                 "Level, Scenery and Enemies is one piece. Deeper is smoother and slightly slower overall.")]
        [SerializeField] private int pieceDepth = 2;

        public RoomDefinition Definition => definition;
        public BoxCollider Bounds { get; private set; }
        public BoxCollider CameraBounds => cameraBounds;

        /// <summary>Pieces still waiting to be switched on after the load.</summary>
        public int PendingPieces => _pending.Count - _pendingIndex;

        private readonly List<GameObject> _pending = new();
        private int _pendingIndex;

        public Vector3 SpawnPoint => defaultSpawn != null ? defaultSpawn.position : Bounds.bounds.center;

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(40f, 16f, 12f);
            box.center = new Vector3(20f, 4f, 0f);
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            Bounds = GetComponent<BoxCollider>();
            Bounds.isTrigger = true;
            if (cameraBounds != null) cameraBounds.isTrigger = true;

            // Runs before everything else in the chunk (execution order -900), so
            // pieces switched off here never pay for Awake and OnEnable in the
            // load frame. They pay for it later, a few at a time.
            if (spreadActivation && Application.isPlaying) StagePieces();
        }

        private void OnEnable() => AllRooms.Add(this);
        private void OnDisable() => AllRooms.Remove(this);

        private void OnTriggerEnter(Collider other) => TryEnter(other);

        // ---- Spread activation ----------------------------------------------------

        private void StagePieces()
        {
            var solid = new List<GameObject>();
            var rest = new List<GameObject>();
            Collect(transform, 0, solid, rest);

            // Ground first. A chunk that shows its trees before its floor is only
            // ugly; one that has trees and no floor yet drops the player through.
            _pending.AddRange(solid);
            _pending.AddRange(rest);
            foreach (var go in _pending) go.SetActive(false);
            if (_pending.Count > 0) ActivateOverTimeAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void Collect(Transform parent, int depth, List<GameObject> solid, List<GameObject> rest)
        {
            foreach (Transform child in parent)
            {
                var go = child.gameObject;
                if (!go.activeSelf || IsInfrastructure(child)) continue;
                if (depth + 1 < pieceDepth && child.childCount > 0)
                {
                    Collect(child, depth + 1, solid, rest);
                    continue;
                }
                var hasSolid = child.GetComponentInChildren<Collider>() is { isTrigger: false };
                (hasSolid ? solid : rest).Add(go);
            }
        }

        // The bounds, the camera box, the spawn and the gates must work from the
        // first frame, so they are never held back.
        private bool IsInfrastructure(Transform t) =>
            (cameraBounds != null && t == cameraBounds.transform) ||
            (defaultSpawn != null && t == defaultSpawn) ||
            t.GetComponent<RoomGate>() != null;

        private async Cysharp.Threading.Tasks.UniTaskVoid ActivateOverTimeAsync(System.Threading.CancellationToken ct)
        {
            var atlas = WorldAtlas.Load();
            var budget = atlas != null ? atlas.activationBudgetMs : 1.5f;
            var watch = new System.Diagnostics.Stopwatch();
            try
            {
                while (_pendingIndex < _pending.Count)
                {
                    watch.Restart();
                    // Always at least one piece per frame, so a single heavy piece
                    // cannot stall the chunk forever.
                    do
                    {
                        var go = _pending[_pendingIndex++];
                        if (go != null) go.SetActive(true);
                    } while (_pendingIndex < _pending.Count && watch.Elapsed.TotalMilliseconds < budget);

                    await Cysharp.Threading.Tasks.UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, ct);
                }
            }
            catch (System.OperationCanceledException) { }
        }

        /// <summary>Switches every waiting piece on now. Used behind a black screen, where a hitch is invisible.</summary>
        public void ActivateAllNow()
        {
            while (_pendingIndex < _pending.Count)
            {
                var go = _pending[_pendingIndex++];
                if (go != null) go.SetActive(true);
            }
        }

        private void TryEnter(Collider other)
        {
            if (definition == null || !PlayerLocator.IsPlayer(other)) return;

            // Rooms overlap a little at their borders so there is never a gap.
            // Only switch once the player's centre is actually inside this room,
            // or standing on the seam would flip back and forth every frame.
            if (!Bounds.bounds.Contains(other.bounds.center)) return;

            WorldStreamer.Instance?.Enter(definition);
            ApplyCameraBounds();
        }

        public void ApplyCameraBounds()
        {
            if (cameraBounds == null) return;

            // A linear world uses one box for the whole line (LinearCameraBounds).
            var atlas = WorldAtlas.Load();
            if (atlas != null && atlas.linear) return;

            var confiner = FindAnyObjectByType<CinemachineConfiner3D>();
            if (confiner == null || confiner.BoundingVolume == cameraBounds) return;
            confiner.BoundingVolume = cameraBounds;
        }

        public static RoomRoot Find(string roomId)
        {
            foreach (var room in AllRooms)
                if (room.definition != null && room.definition.id == roomId) return room;
            return null;
        }

#if UNITY_EDITOR
        [Button(ButtonSizes.Large), GUIColor(0.45f, 0.78f, 0.5f)]
        [Tooltip("Copies this room's trigger box into the Room Definition, so the map and the streamer agree with the scene.")]
        private void WriteBoundsToDefinition()
        {
            if (definition == null) return;
            var box = GetComponent<BoxCollider>();
            var b = new Bounds(transform.TransformPoint(box.center), Vector3.Scale(box.size, transform.lossyScale));
            UnityEditor.Undo.RecordObject(definition, "Write room bounds");
            definition.worldRect = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            definition.sceneName = gameObject.scene.name;
            UnityEditor.EditorUtility.SetDirty(definition);
        }

        [Button]
        [Tooltip("Makes a camera bounds box half a screen inside the room on every side.")]
        private void BuildCameraBounds()
        {
            var box = GetComponent<BoxCollider>();
            if (cameraBounds == null)
            {
                var go = new GameObject("Camera Bounds", typeof(BoxCollider));
                UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Camera bounds");
                go.transform.SetParent(transform, false);
                go.layer = LayerMask.NameToLayer("Ignore Raycast");
                cameraBounds = go.GetComponent<BoxCollider>();
            }

            // Only x is held in. Height is owned by CameraAnchor, and a tight
            // vertical box fights it, the same reason the Stage box is 60 m tall.
            var halfWidth = 3.6f * 16f / 9f;
            cameraBounds.isTrigger = true;
            cameraBounds.center = box.center;
            cameraBounds.size = new Vector3(Mathf.Max(0.1f, box.size.x - halfWidth * 2f), 60f, 40f);
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            var colour = definition != null ? definition.mapColour : Color.white;
            Gizmos.matrix = transform.localToWorldMatrix;
            colour.a = 0.06f;
            Gizmos.color = colour;
            Gizmos.DrawCube(box.center, box.size);
            colour.a = 0.8f;
            Gizmos.color = colour;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
            if (definition != null)
                UnityEditor.Handles.Label(transform.TransformPoint(box.center + Vector3.up * box.size.y * 0.5f),
                    $"{definition.id}  ({definition.sceneName})");
        }
#endif
    }
}
