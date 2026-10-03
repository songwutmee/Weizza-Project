using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A name for one object that survives a reload, so the save knows which
    /// door was opened and which page was picked up.
    ///
    /// Scene object names are not good enough: two "Door" objects are common,
    /// and renaming one in the Hierarchy would silently forget that it was
    /// ever opened. A GUID stamped once and never touched again is.
    ///
    /// Duplicating an object in the editor copies its id too. Weizza >
    /// Validate Persistent Ids finds and fixes those, and it is worth running
    /// before any build.
    /// </summary>
    [AddComponentMenu("Weizza/Persistent Id")]
    [DisallowMultipleComponent]
    public sealed class PersistentId : MonoBehaviour
    {
        [InfoBox("Stamped automatically. Do not edit by hand: the save file keys " +
                 "everything on this, so changing it forgets this object's saved " +
                 "state. Duplicated objects share an id until Weizza > Validate " +
                 "Persistent Ids fixes them.")]
        [ReadOnly]
        [SerializeField] private string id;

        public string Id
        {
            get
            {
                if (string.IsNullOrEmpty(id)) id = NewId();
                return id;
            }
        }

        /// <summary>Editor tools only. Changing an id at runtime breaks saves.</summary>
        public void Regenerate() => id = NewId();

        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        private void Reset() => id = NewId();

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Only fills an empty id. Fixing duplicates here would run during
            // scene load too, where which copy counts as the original is not
            // guaranteed, and the wrong one would lose its save state.
            if (string.IsNullOrEmpty(id)) id = NewId();
        }
#endif
    }

    public static class PersistentIdExtensions
    {
        /// <summary>
        /// The stable id of this object, or its scene path when it has no
        /// PersistentId. The path fallback works until the object is renamed
        /// or moved, which is why PersistentId exists.
        /// </summary>
        public static string StableKey(this Component component)
        {
            if (component == null) return string.Empty;
            var pid = component.GetComponent<PersistentId>();
            if (pid != null) return pid.Id;

            var t = component.transform;
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return component.gameObject.scene.name + ":" + path;
        }
    }
}
