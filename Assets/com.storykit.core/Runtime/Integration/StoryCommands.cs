using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Flow;
using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// The custom-system bridge: how graphs call game code the package has
    /// never heard of (Ink's "external functions", Blueprint's custom events).
    ///
    /// Your game registers named commands once:
    ///
    ///   StoryCommands.Register("weather.set", (args, board) => {
    ///       WeatherSystem.SetWeather(args[0]);            // your custom system
    ///   });
    ///   StoryCommands.RegisterAsync("boss.intro", async (args, board, ct) => {
    ///       await MyBossIntroSequence.PlayAsync(ct);      // awaitable too
    ///   });
    ///
    /// Then any graph uses CustomCommandNode ("weather.set storm"), and any
    /// .story file writes:  &lt;&lt; call weather.set storm &gt;&gt;
    /// Zero package changes needed per system: this is the extension point
    /// that makes StoryKit fit every project, old or new.
    /// </summary>
    public static class StoryCommands
    {
        public delegate void SyncCommand(string[] args, Blackboard board);
        public delegate UniTask AsyncCommand(string[] args, Blackboard board, CancellationToken ct);

        private static readonly Dictionary<string, AsyncCommand> _commands =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string name, SyncCommand command)
        {
            if (string.IsNullOrEmpty(name) || command == null) return;
            _commands[name] = (args, board, ct) =>
            {
                command(args, board);
                return UniTask.CompletedTask;
            };
        }

        public static void RegisterAsync(string name, AsyncCommand command)
        {
            if (string.IsNullOrEmpty(name) || command == null) return;
            _commands[name] = command;
        }

        public static void Unregister(string name) => _commands.Remove(name);
        public static bool IsRegistered(string name) => _commands.ContainsKey(name);
        public static void Reset() => _commands.Clear();

        public static async UniTask InvokeAsync(
            string name, string[] args, Blackboard board, CancellationToken ct)
        {
            if (_commands.TryGetValue(name, out var command))
            {
                await command(args ?? Array.Empty<string>(), board, ct);
            }
            else
            {
                Debug.LogWarning($"[StoryKit] Custom command '{name}' is not registered. " +
                                 "Register it with StoryCommands.Register(...) at startup.");
            }
        }
    }
}

namespace StoryKit.Flow
{
    /// <summary>Invokes a StoryCommands entry. The graph-side of the bridge.</summary>
    [Serializable]
    [NodeMenu("Integration",
        "Call YOUR game code by name (register with StoryCommands). The bridge to any custom system.")]
    public sealed class CustomCommandNode : ActionNode
    {
        [SerializeField] private string _command;
        [Tooltip("Space-separated arguments passed to the handler.")]
        [SerializeField] private string _arguments;

        [Tooltip("Consequence commands always run when the player skips a cutscene. "
               + "Pick Presentation only for pure-visual commands.")]
        [SerializeField] private NodeEffect _effect = NodeEffect.Consequence;

        public string Command { get => _command; set => _command = value; }
        public string Arguments { get => _arguments; set => _arguments = value; }
        public NodeEffect EffectSetting { get => _effect; set => _effect = value; }

        public override NodeEffect Effect => _effect;
        public override string GetSummary() => $"{_command} {_arguments}".TrimEnd();

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_command)) return;
            string[] args = string.IsNullOrWhiteSpace(_arguments)
                ? Array.Empty<string>()
                : _arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            await StoryCommands.InvokeAsync(_command, args, context.Board, ct);
        }
    }

    /// <summary>
    /// Bark: a short floating line above a scene object that never blocks
    /// gameplay ("Who's there?!"). Self-contained world-space text: works
    /// with zero setup; replace with your own by registering a custom
    /// command and using that instead.
    /// </summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Floating text above a scene object that never pauses gameplay ('Who's there?!').")]
    public sealed class BarkNode : ActionNode
    {
        [SerializeField] private string _stageId;
        [SerializeField] private string _lineKey;
        [SerializeField, TextArea] private string _fallbackText;
        [SerializeField, Min(0.5f)] private float _duration = 2.2f;

        public string StageId { get => _stageId; set => _stageId = value; }
        public string LineKey { get => _lineKey; set => _lineKey = value; }
        public string FallbackText { get => _fallbackText; set => _fallbackText = value; }
        public float Duration { get => _duration; set => _duration = Mathf.Max(0.5f, value); }

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_stageId}: {_fallbackText}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var anchor = Bridge.StoryStageId.FindTransform(_stageId);
            if (anchor == null) return UniTask.CompletedTask;

            string text = StoryServices.Get<ILocalizationService>()?.Resolve(_lineKey) ?? _lineKey;
            if ((text == _lineKey || string.IsNullOrEmpty(text)) && !string.IsNullOrEmpty(_fallbackText))
                text = _fallbackText;
            text = SayNode.Interpolate(text, context.Board);

            ShowBark(anchor, text, _duration);
            return UniTask.CompletedTask;   // fire and forget: barks never block
        }

        private static void ShowBark(Transform anchor, string text, float duration)
        {
            var go = new GameObject("Bark");
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.up * 1.6f;

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 48;
            mesh.characterSize = 0.045f;
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = Color.white;

            go.AddComponent<BarkLifetime>().Init(duration);
        }

        private sealed class BarkLifetime : MonoBehaviour
        {
            private float _dieAt;
            private TextMesh _mesh;

            public void Init(float duration)
            {
                _dieAt = StoryTime.Time + duration;
                _mesh = GetComponent<TextMesh>();
            }

            private void Update()
            {
                float remain = _dieAt - StoryTime.Time;
                if (remain <= 0f) { Destroy(gameObject); return; }

                // Rise + fade in the last half second.
                transform.localPosition += Vector3.up * (0.25f * Time.deltaTime);
                if (remain < 0.5f && _mesh != null)
                {
                    var color = _mesh.color;
                    color.a = remain * 2f;
                    _mesh.color = color;
                }

                // Face the camera.
                var cam = Camera.main;
                if (cam != null)
                    transform.rotation = cam.transform.rotation;
            }
        }
    }
}
