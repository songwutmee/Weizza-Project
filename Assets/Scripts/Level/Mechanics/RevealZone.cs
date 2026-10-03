using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Foreground silhouettes that fade when the player walks behind them,
    /// showing a hidden passage. Neva and Hollow Knight both use this read;
    /// it pairs with the solid black foreground layer the project notes calls the
    /// cheapest depth there is.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Reveal Zone")]
    public sealed class RevealZone : BodyZone, ISaveable
    {
        [BoxGroup("Reveal")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [Tooltip("The foreground art to fade. Empty uses every sprite under this object.")]
        [SerializeField] private List<SpriteRenderer> covers = new();

        [BoxGroup("Reveal")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float revealedAlpha = 0.12f;

        [BoxGroup("Reveal")]
        [PropertyRange(0.05f, 2f)]
        [SerializeField] private float fadeSeconds = 0.4f;

        [BoxGroup("Reveal")]
        [ToggleLeft]
        [Tooltip("Stays see-through once found, across saves.")]
        [SerializeField] private bool staysRevealed = true;

        private readonly List<float> _baseAlpha = new();
        private bool _inside;
        private bool _found;
        private float _blend;

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
        }

        private void Awake()
        {
            if (covers.Count == 0) covers.AddRange(GetComponentsInChildren<SpriteRenderer>());
            foreach (var cover in covers) _baseAlpha.Add(cover != null ? cover.color.a : 1f);
        }

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (!PlayerLocator.IsPlayer(body)) return;
            _inside = true;
            if (!_found && staysRevealed) _found = true;
        }

        protected override void OnExited(CharacterMotor3D body)
        {
            if (PlayerLocator.IsPlayer(body)) _inside = false;
        }

        protected override void Update()
        {
            base.Update();
            var want = _inside || _found ? 1f : 0f;
            if (Mathf.Approximately(_blend, want)) return;

            _blend = Mathf.MoveTowards(_blend, want, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
            for (var i = 0; i < covers.Count; i++)
            {
                if (covers[i] == null) continue;
                var c = covers[i].color;
                c.a = Mathf.Lerp(_baseAlpha[i], revealedAlpha, _blend);
                covers[i].color = c;
            }
        }

        public string SaveKey => staysRevealed ? "reveal." + this.StableKey() : null;
        public string CaptureState() => _found ? "1" : "0";
        public void RestoreState(string state) => _found = state == "1";

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.3f, 0.3f, 0.35f, 0.15f);
#endif
    }
}
