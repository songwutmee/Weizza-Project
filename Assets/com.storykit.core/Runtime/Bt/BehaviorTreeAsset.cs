using UnityEngine;

namespace StoryKit.Bt
{
    /// <summary>
    /// A behavior tree stored as an asset. Unity requires a ScriptableObject to
    /// live in a file matching its class name, so this type has its own file.
    /// </summary>
    [CreateAssetMenu(fileName = "BehaviorTree", menuName = "StoryKit/Behavior Tree")]
    public class BehaviorTreeAsset : ScriptableObject
    {
        [SerializeReference] private BtNode _root;
        [SerializeField] private int _schemaVersion = 1;

        public BtNode Root { get => _root; set => _root = value; }
        public int SchemaVersion => _schemaVersion;
    }
}
