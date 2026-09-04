using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StoryKit.Flow
{
    /// <summary>
    /// Stage nodes: full scene direction from the graph:
    /// actors (spawn/move/animate/look), camera (focus/shake/return),
    /// screen (banner/HUD), audio (sfx/stop bgm), and level loading.
    /// Scene objects are referenced by StoryStageId strings, never by
    /// direct object references, so graphs stay scene-independent assets.
    /// </summary>

    // ---------------- Actors ----------------

    /// <summary>Spawns an actor at a StoryStageId anchor. Consequence: a quest
    /// NPC that must exist after a skipped cutscene still spawns.</summary>
    [Serializable]
    [NodeMenu("Actor",
        "Spawn a character at a Story Stage Id anchor in the scene.")]
    public sealed class SpawnActorNode : ActionNode
    {
        [SerializeField] private string _actorId;
        [SerializeField] private string _spawnPointId;

        public string ActorId { get => _actorId; set => _actorId = value; }
        public string SpawnPointId { get => _spawnPointId; set => _spawnPointId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_actorId} @ {_spawnPointId}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var actors = StoryServices.Get<IActorService>();
            if (actors == null) return;
            var anchor = StoryStageId.FindTransform(_spawnPointId);
            Vector3 position = anchor != null ? anchor.position : Vector3.zero;
            await actors.SpawnAsync(_actorId, position, ct);
        }
    }

    [Serializable]
    [NodeMenu("Actor",
        "Remove a spawned character from the scene.")]
    public sealed class DespawnActorNode : ActionNode
    {
        [SerializeField] private string _actorId;

        public string ActorId { get => _actorId; set => _actorId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _actorId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<IActorService>()?.Despawn(_actorId);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Walks an actor to a StoryStageId anchor, awaiting arrival.</summary>
    [Serializable]
    [NodeMenu("Actor",
        "Walk a character to a Story Stage Id anchor and wait until they arrive.")]
    public sealed class ActorMoveNode : ActionNode
    {
        [SerializeField] private string _actorId;
        [SerializeField] private string _targetId;
        [SerializeField, Min(0.1f)] private float _speed = 3f;

        public string ActorId { get => _actorId; set => _actorId = value; }
        public string TargetId { get => _targetId; set => _targetId = value; }
        public float Speed { get => _speed; set => _speed = Mathf.Max(0.1f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_actorId} → {_targetId}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var actor = StoryServices.Get<IActorService>()?.Find(_actorId);
            var target = StoryStageId.FindTransform(_targetId);
            if (actor == null || target == null) return;
            await actor.MoveToAsync(target.position, _speed, ct);
        }
    }

    [Serializable]
    [NodeMenu("Actor",
        "Play an animation on a character, optionally waiting for it to finish.")]
    public sealed class ActorAnimationNode : ActionNode
    {
        [SerializeField] private string _actorId;
        [SerializeField] private string _animationName;
        [SerializeField] private bool _waitForEnd = true;

        public string ActorId { get => _actorId; set => _actorId = value; }
        public string AnimationName { get => _animationName; set => _animationName = value; }
        public bool WaitForEnd { get => _waitForEnd; set => _waitForEnd = value; }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_actorId}: {_animationName}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var actor = StoryServices.Get<IActorService>()?.Find(_actorId);
            if (actor == null) return;
            await actor.PlayAnimationAsync(_animationName, _waitForEnd, ct);
        }
    }

    [Serializable]
    [NodeMenu("Actor",
        "Turn a character to face a Story Stage Id anchor.")]
    public sealed class ActorLookAtNode : ActionNode
    {
        [SerializeField] private string _actorId;
        [SerializeField] private string _targetId;

        public string ActorId { get => _actorId; set => _actorId = value; }
        public string TargetId { get => _targetId; set => _targetId = value; }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_actorId} 👁 {_targetId}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var actor = StoryServices.Get<IActorService>()?.Find(_actorId);
            var target = StoryStageId.FindTransform(_targetId);
            if (actor != null && target != null)
                actor.LookAt(target.position);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Camera ----------------

    /// <summary>Focuses the camera on a stage anchor for the task's lifetime;
    /// releases back to default automatically (mandatory cleanup).</summary>
    [Serializable]
    [NodeMenu("Camera",
        "Point the camera at a target while this task runs. Returns to the player automatically when it ends.")]
    public sealed class CameraFocusNode : TaskNode
    {
        [SerializeField] private string _targetId;
        [SerializeField, Min(0f)] private float _blendTime = 0.6f;

        public string TargetId { get => _targetId; set => _targetId = value; }
        public float BlendTime { get => _blendTime; set => _blendTime = Mathf.Max(0f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _targetId;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var camera = StoryServices.Get<ICameraService>();
            var target = StoryStageId.FindTransform(_targetId);
            if (camera != null && target != null)
                await camera.FocusAsync(target, _blendTime, ct);
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<ICameraService>()?.ReturnToDefault(_blendTime);
            return UniTask.CompletedTask;
        }
    }

    [Serializable]
    [NodeMenu("Camera",
        "Shake the camera (explosion, impact). Respects the reduce-shake accessibility setting.")]
    public sealed class CameraShakeNode : ActionNode
    {
        [SerializeField, Min(0f)] private float _strength = 1f;
        [SerializeField, Min(0f)] private float _duration = 0.4f;

        public float Strength { get => _strength; set => _strength = Mathf.Max(0f, value); }
        public float Duration { get => _duration; set => _duration = Mathf.Max(0f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_strength:0.#} × {_duration:0.#}s";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var camera = StoryServices.Get<ICameraService>();
            if (camera != null)
                await camera.ShakeAsync(_strength, _duration, ct);
        }
    }

    // ---------------- Screen / Audio ----------------

    [Serializable]
    [NodeMenu("UI",
        "Big centered title text ('The Ancient Forest') that fades in and out.")]
    public sealed class BannerNode : ActionNode
    {
        [SerializeField] private string _lineKey;
        [SerializeField, Min(0.2f)] private float _duration = 2f;

        public string LineKey { get => _lineKey; set => _lineKey = value; }
        public float Duration { get => _duration; set => _duration = Mathf.Max(0.2f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _lineKey;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var screen = StoryServices.Get<IScreenUI>();
            if (screen == null) return;
            string text = StoryServices.Get<ILocalizationService>()?.Resolve(_lineKey) ?? _lineKey;
            await screen.ShowBannerAsync(text, _duration, ct);
        }
    }

    /// <summary>Hides the HUD while active, restores on completion/cancel.</summary>
    [Serializable]
    [NodeMenu("UI",
        "Hide the HUD while this task runs. Restores automatically when it ends.")]
    public sealed class HideHudNode : TaskNode
    {
        public override NodeEffect Effect => NodeEffect.Presentation;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<IScreenUI>()?.SetHudVisible(false);
            return UniTask.CompletedTask;
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<IScreenUI>()?.SetHudVisible(true);
            return UniTask.CompletedTask;
        }
    }

    [Serializable]
    [NodeMenu("Audio",
        "Play a one-shot sound effect.")]
    public sealed class PlaySfxNode : ActionNode
    {
        [SerializeField] private string _sfxId;

        public string SfxId { get => _sfxId; set => _sfxId = value; }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _sfxId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<IAudioService>()?.PlaySfx(_sfxId);
            return UniTask.CompletedTask;
        }
    }

    [Serializable]
    [NodeMenu("Audio",
        "Fade the background music out.")]
    public sealed class StopBgmNode : ActionNode
    {
        [SerializeField, Min(0f)] private float _fadeOut = 1f;

        public float FadeOut { get => _fadeOut; set => _fadeOut = Mathf.Max(0f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<IAudioService>()?.StopBgm(_fadeOut);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Level ----------------

    /// <summary>Async scene load with automatic fade out/in around it.</summary>
    [Serializable]
    [NodeMenu("Level",
        "Load another scene, with automatic fade out and in.")]
    public sealed class LoadLevelNode : ActionNode
    {
        [SerializeField] private string _sceneName;
        [SerializeField] private bool _fade = true;

        public string SceneName { get => _sceneName; set => _sceneName = value; }
        public bool Fade { get => _fade; set => _fade = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _sceneName;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_sceneName)) return;

            var screen = StoryServices.Get<IScreenUI>();
            if (_fade && screen != null && !context.FastForward)
                await screen.FadeAsync(1f, 0.4f, ct);

            var operation = SceneManager.LoadSceneAsync(_sceneName);
            if (operation == null) return;
            while (!operation.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            if (_fade && screen != null && !context.FastForward)
                await screen.FadeAsync(0f, 0.4f, ct);
        }
    }

    /// <summary>Enables/disables a scene object by StoryStageId: doors, walls,
    /// props, blockers. The classic "unlock the exit after the quest".</summary>
    [Serializable]
    [NodeMenu("Level",
        "Show or hide a scene object by its Story Stage Id (open a gate, remove a wall).")]
    public sealed class SetObjectActiveNode : ActionNode
    {
        [SerializeField] private string _stageId;
        [SerializeField] private bool _active;

        public string StageId { get => _stageId; set => _stageId = value; }
        public bool Active { get => _active; set => _active = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_stageId} = {(_active ? "on" : "off")}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var target = StoryStageId.Find(_stageId);
            if (target != null)
                target.gameObject.SetActive(_active);
            else
                Debug.LogWarning($"[StoryKit] SetObjectActive: stage id '{_stageId}' not found " +
                                 "(object inactive from scene start can never be found: " +
                                 "keep it active and disable a child instead).", context.Runner);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Save / checkpoint ----------------

    /// <summary>
    /// Records "the story reached here" into the Global board and requests a
    /// save. On load, StorySave.TryGetCheckpoint tells the game which graph
    /// and node to resume from (FlowRunner.PlayAsync accepts a start node).
    /// </summary>
    [Serializable]
    [NodeMenu("Meta",
        "Mark 'the story reached here' and save. Loading the game resumes from this node.")]
    public sealed class CheckpointNode : ActionNode
    {
        [SerializeField] private string _checkpointId;

        public string CheckpointId { get => _checkpointId; set => _checkpointId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _checkpointId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var board = StoryState.Global;
            board.Set(StorySave.CheckpointGraphKey, context.Runner.SourceAsset != null
                ? context.Runner.SourceAsset.name : "");
            board.Set(StorySave.CheckpointNodeKey, Id);
            board.Set(StorySave.CheckpointIdKey, _checkpointId ?? "");
            StoryServices.Get<ISaveService>()?.SetCheckpoint(_checkpointId);
            StorySave.SaveToDisk();
            return UniTask.CompletedTask;
        }
    }
}
