using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Fading copies of the body left behind during a dash, so a dash that crosses
    /// three metres in ten frames reads as a sweep instead of a teleport.
    ///
    /// Spaced by distance travelled, not by time, so the gaps stay even when the frame
    /// rate wobbles. Copies the current frame and flip, draws one step behind the body,
    /// and takes the stance colour, so a Se dash leaves a herbal green trail. Pooled.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Dash Ghosts")]
    public sealed class DashGhosts : MonoBehaviour
    {
        [BoxGroup("Ghosts", centerLabel: true)]
        [PropertyRange(0.1f, 1.5f), Tooltip("Metres between two copies.")]
        [SerializeField] private float spacing = 0.45f;

        [BoxGroup("Ghosts")]
        [PropertyRange(0.05f, 0.8f), Tooltip("Real seconds a copy takes to fade.")]
        [SerializeField] private float fadeSeconds = 0.22f;

        [BoxGroup("Ghosts")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float startAlpha = 0.45f;

        private sealed class Ghost
        {
            public SpriteRenderer renderer;
            public float born;
            public Color colour;
        }

        private readonly List<Ghost> _ghosts = new(12);
        private PlayerController _player;
        private SpriteRenderer _body;
        private Vector3 _lastDrop;
        private bool _dashing;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _body = GetComponentInChildren<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            var dashing = _player != null && _player.State == PlayerState.Dash;
            if (dashing && !_dashing) { _lastDrop = _body.transform.position; Drop(); }
            if (dashing && (_body.transform.position - _lastDrop).sqrMagnitude >= spacing * spacing) Drop();
            _dashing = dashing;

            var now = Time.unscaledTime;
            foreach (var ghost in _ghosts)
            {
                if (!ghost.renderer.enabled) continue;
                var k = (now - ghost.born) / Mathf.Max(0.01f, fadeSeconds);
                if (k >= 1f) { ghost.renderer.enabled = false; continue; }
                var c = ghost.colour;
                c.a = startAlpha * (1f - k) * (1f - k);
                ghost.renderer.color = c;
            }
        }

        private void Drop()
        {
            if (_body == null || _body.sprite == null) return;
            _lastDrop = _body.transform.position;

            var ghost = Free();
            var r = ghost.renderer;
            r.transform.SetPositionAndRotation(_body.transform.position, _body.transform.rotation);
            r.transform.localScale = _body.transform.lossyScale;
            r.sprite = _body.sprite;
            r.flipX = _body.flipX;
            r.sortingLayerID = _body.sortingLayerID;
            r.sortingOrder = _body.sortingOrder - 1;
            var tint = _player.CurrentStance != null ? _player.CurrentStance.tint : Color.white;
            ghost.colour = Color.Lerp(Color.white, tint, 0.6f);
            ghost.born = Time.unscaledTime;
            r.enabled = true;
        }

        private Ghost Free()
        {
            foreach (var g in _ghosts) if (!g.renderer.enabled) return g;
            var go = new GameObject("Dash Ghost");
            // Not parented to the player: a copy stays where it was dropped while the body moves on.
            go.transform.SetParent(null, false);
            DontDestroyOnLoad(go);
            var ghost = new Ghost { renderer = go.AddComponent<SpriteRenderer>() };
            ghost.renderer.sharedMaterial = _body.sharedMaterial;
            ghost.renderer.enabled = false;
            _ghosts.Add(ghost);
            return ghost;
        }

        private void OnDestroy()
        {
            foreach (var g in _ghosts) if (g.renderer != null) Destroy(g.renderer.gameObject);
            _ghosts.Clear();
        }
    }
}
