using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit;
using StoryKit.Bridge;
using StoryKit.Flow;
using UnityEngine;

// Flow graph nodes for things Weizza owns. They sit in the graph editor next to
// StoryKit's own nodes, under their own menu folders. Each one is a thin call
// into a game system that already exists, so a graph and game code always do a
// thing the same way. None of them reach inside com.storykit.core.

namespace Weizza
{
    /// <summary>
    /// Sends the player through to a gate in another room, with the fade, the
    /// streaming and the camera cut a walked gate gives. StoryKit's own Load Level
    /// node replaces every open scene, which would unload the player, so use this
    /// one in Weizza.
    /// </summary>
    [Serializable]
    [NodeMenu("Weizza/World", "Send the player to a gate in a room, loading the room if needed.")]
    public sealed class TravelToRoomNode : ActionNode
    {
        [SerializeField] private string _roomId = "forest_01";
        [SerializeField] private string _gateId = "left";
        [SerializeField, Min(0.05f)] private float _fade = 0.35f;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_roomId} / {_gateId}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            await RoomGate.TravelToAsync(_roomId, _gateId, context.FastForward ? 0.05f : _fade, ct);
        }
    }

    /// <summary>Holds the graph until the player walks into a given room.</summary>
    [Serializable]
    [NodeMenu("Weizza/World", "Wait until the player enters a room.")]
    public sealed class WaitForRoomNode : ActionNode
    {
        [SerializeField] private string _roomId = "forest_02";

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _roomId;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            while (WorldStreamer.Instance == null || WorldStreamer.Instance.Current == null ||
                   WorldStreamer.Instance.Current.id != _roomId)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
    }

    /// <summary>Draws an area on the map, the way buying a map from a cartographer does.</summary>
    [Serializable]
    [NodeMenu("Weizza/World", "Reveal every room of an area on the map.")]
    public sealed class RevealAreaMapNode : ActionNode
    {
        [SerializeField] private string _areaId = "forest";

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _areaId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryState.Global.Set(MapService.OwnedAreaPrefix + _areaId, true);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Teaches the player a stance. Pyada's gift, or a school's master.</summary>
    [Serializable]
    [NodeMenu("Weizza/Player", "Teach the player a stance so it can be switched to.")]
    public sealed class LearnStanceNode : ActionNode
    {
        [SerializeField] private StanceId _stance = StanceId.Se;
        [SerializeField] private bool _switchToIt = true;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _switchToIt ? $"{_stance}, switch" : _stance.ToString();

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StanceProgress.Learn(_stance);
            if (_switchToIt) PlayerLocator.Player?.ForceStance(_stance);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Puts the player into a stance with no switch window. For cutscene starts.</summary>
    [Serializable]
    [NodeMenu("Weizza/Player", "Set the player's stance at once, no switch window.")]
    public sealed class SetStanceNode : ActionNode
    {
        [SerializeField] private StanceId _stance = StanceId.Normal;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _stance.ToString();

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            PlayerLocator.Player?.ForceStance(_stance);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Heals the player, or revives them. A shrine rest written as a node.</summary>
    [Serializable]
    [NodeMenu("Weizza/Player", "Restore the player's health to a fraction of full.")]
    public sealed class HealPlayerNode : ActionNode
    {
        [SerializeField, Range(0f, 1f)] private float _toFraction = 1f;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_toFraction:P0}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var vitals = PlayerLocator.Player != null ? PlayerLocator.Player.Vitals : null;
            if (vitals != null && vitals.Normalized < _toFraction) vitals.SetHealth(vitals.Max * _toFraction);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Freezes the game for a few frames. The punctuation on a dramatic beat.</summary>
    [Serializable]
    [NodeMenu("Weizza/Feel", "Hit stop: freeze the game for some frames at 60 fps.")]
    public sealed class HitStopNode : ActionNode
    {
        [SerializeField, Range(1, 60)] private int _frames = 10;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_frames}f";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (!context.FastForward) HitStop.Freeze(_frames);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Wakes a mini boss: plays its intro and starts the fight.</summary>
    [Serializable]
    [NodeMenu("Weizza/Combat", "Wake a mini boss found by its Story Stage Id, and optionally wait for it to fall.")]
    public sealed class WakeBossNode : ActionNode
    {
        [SerializeField] private string _bossStageId = "boss";
        [SerializeField] private bool _waitUntilDefeated;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _waitUntilDefeated ? $"{_bossStageId}, wait" : _bossStageId;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var stage = StoryStageId.Find(_bossStageId);
            var boss = stage != null ? stage.GetComponent<MiniBossBrain>() : null;
            if (boss == null)
            {
                Debug.LogWarning($"[WakeBossNode] No MiniBossBrain with Story Stage Id '{_bossStageId}'.");
                return;
            }
            boss.Activate();
            if (!_waitUntilDefeated) return;
            while (boss != null && boss.State != MiniBossBrain.Phase.Defeated)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    /// <summary>Saves to the current slot right now. Use after a story beat that must not be replayed.</summary>
    [Serializable]
    [NodeMenu("Weizza/Meta", "Save the game to the current slot now.")]
    public sealed class SaveNowNode : ActionNode
    {
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => "save";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (SaveSystem.Instance != null) SaveSystem.Instance.Save();
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Shows a localized line as the big centre banner. Text comes from the sheet.</summary>
    [Serializable]
    [NodeMenu("Weizza/UI", "Show a sheet key as the centre banner, like an area name.")]
    public sealed class LocalizedBannerNode : ActionNode
    {
        [SerializeField] private string _key = "AREA_FOREST";
        [SerializeField] private string _fallback = "The Forest";
        [SerializeField, Min(0.5f)] private float _seconds = 2.5f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _key;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (!context.FastForward) HudScreen.Instance?.Banner(Loc.T(_key, _fallback), _seconds);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// A huge centred area title, faded in and out, and waits for it to finish.
    /// Text from the sheet. The chapter card of a Neva style arrival.
    /// </summary>
    [Serializable]
    [NodeMenu("Weizza/UI", "Big centred area title (The Forest), waits until it has faded out.")]
    public sealed class AreaTitleNode : ActionNode
    {
        [SerializeField] private string _key = "AREA_FOREST";
        [SerializeField] private string _fallback = "The Forest";
        [SerializeField, Min(0.5f)] private float _hold = 2.5f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _key;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (context.FastForward || HudScreen.Instance == null) return;
            var seconds = HudScreen.Instance.TitleCard(Loc.T(_key, _fallback), _hold);
            await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }
    }

    /// <summary>Switches the camera to a story framing of an actor, and back with Return.</summary>
    [Serializable]
    [NodeMenu("Weizza/Camera", "Lean the camera in on a Story Stage Id: close up, medium or wide.")]
    public sealed class FrameShotNode : ActionNode
    {
        [SerializeField] private string _targetStageId = "player";
        [SerializeField] private ShotFraming _framing = ShotFraming.Medium;
        [SerializeField, Min(0f)] private float _blend = 0.8f;
        [SerializeField] private bool _returnToGameCamera;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _returnToGameCamera ? "back to game camera" : $"{_framing} on {_targetStageId}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (!StoryServices.TryGet<ICameraService>(out var cam) || cam == null) return;
            var blend = context.FastForward ? 0f : _blend;
            if (_returnToGameCamera)
            {
                cam.ReturnToDefault(blend);
                return;
            }

            var target = StoryStageId.FindTransform(_targetStageId);
            if (target == null && _targetStageId == "player" && PlayerLocator.Player != null)
                target = PlayerLocator.Player.transform;
            if (target != null) await cam.FrameAsync(target, _framing, blend, ct);
        }
    }

    /// <summary>
    /// The slow pull back that shows how small the player is in the world (Act 0's
    /// reveal). Widens from the game framing and rises over the target. The camera
    /// stays there until a Frame Shot node with Return To Game Camera, or the end.
    /// </summary>
    [Serializable]
    [NodeMenu("Weizza/Camera", "Slowly pull the camera back and up over a Story Stage Id: the reveal shot.")]
    public sealed class PullBackNode : ActionNode
    {
        [SerializeField] private string _targetStageId = "player";
        [SerializeField, Min(1f)] private float _toSize = 18f;
        [SerializeField] private float _rise = 7f;
        [SerializeField, Min(0.1f)] private float _seconds = 7f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"pull back to {_toSize:0} over {_seconds:0.#} s";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var service = OrthoCameraService.Instance;
            if (service == null) return;
            var target = StoryStageId.FindTransform(_targetStageId);
            if (target == null && _targetStageId == "player" && PlayerLocator.Player != null)
                target = PlayerLocator.Player.transform;
            if (target == null) return;
            await service.PullBackAsync(target, _toSize, _rise, context.FastForward ? 0.01f : _seconds, ct);
        }
    }
}
