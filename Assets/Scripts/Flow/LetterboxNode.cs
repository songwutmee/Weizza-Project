using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Flow;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Slides the cutscene bars in or out. Lives in the game project rather
    /// than in com.storykit.core: StoryKit's node base classes are public on
    /// purpose so a game can add nodes for things it owns, and a letterbox is
    /// a Weizza visual choice, not a StoryKit concept.
    /// </summary>
    [Serializable]
    [NodeMenu("UI", "Slide the letterbox bars in or out for a cutscene.")]
    public sealed class LetterboxNode : ActionNode
    {
        [SerializeField] private bool _show = true;
        [SerializeField, Min(0f)] private float _duration = 0.5f;

        public bool Show { get => _show; set => _show = value; }
        public float Duration { get => _duration; set => _duration = Mathf.Max(0f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _show ? "bars in" : "bars out";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (Letterbox.Instance != null) await Letterbox.Instance.SetAsync(_show, _duration, ct);
        }
    }
}
