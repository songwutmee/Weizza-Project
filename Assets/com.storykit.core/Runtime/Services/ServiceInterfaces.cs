using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// Game-facing service contracts. A game implements the ones it has;
    /// unregistered services make related nodes no-op with a single warning.
    /// Keep these small: they are the entire coupling surface between
    /// StoryKit and any game.
    /// </summary>

    public interface IDialogueUI : IStoryService
    {
        /// <summary>Show one line and complete when the player advances (or voice ends).
        /// emotion selects the portrait variant (may be null/empty).</summary>
        UniTask SayAsync(string speakerId, string emotion, string text, CancellationToken ct);

        /// <summary>Show choices, return the picked index.</summary>
        UniTask<int> ChoiceAsync(IReadOnlyList<string> options, CancellationToken ct);

        UniTask OpenAsync(CancellationToken ct);
        UniTask CloseAsync(CancellationToken ct);
    }

    public interface ILocalizationService : IStoryService
    {
        /// <summary>Resolve a line key to text in the active language. Missing keys return the key itself.</summary>
        string Resolve(string key);
        string ActiveLanguage { get; set; }
    }

    public interface IScreenUI : IStoryService
    {
        UniTask FadeAsync(float toAlpha, float duration, CancellationToken ct);
        UniTask ShowBannerAsync(string text, float duration, CancellationToken ct);
        void SetHudVisible(bool visible);
    }

    public interface IActorService : IStoryService
    {
        UniTask<IActor> SpawnAsync(string actorId, Vector3 position, CancellationToken ct);
        IActor Find(string actorId);
        void Despawn(string actorId);
    }

    public interface IActor
    {
        string Id { get; }
        Transform Transform { get; }
        UniTask MoveToAsync(Vector3 position, float speed, CancellationToken ct);
        UniTask PlayAnimationAsync(string animationName, bool waitForEnd, CancellationToken ct);
        void LookAt(Vector3 worldPosition);
    }

    /// <summary>How a shot should frame its subject.</summary>
    public enum ShotFraming
    {
        /// <summary>Whatever the camera was already set up to do.</summary>
        Default,
        /// <summary>Close on the face. The one for a serious line.</summary>
        CloseUp,
        /// <summary>Head and shoulders. The default conversation shot.</summary>
        Medium,
        /// <summary>Whole body plus room. Good for showing where you are.</summary>
        Wide,
        /// <summary>Behind and above the shoulder, looking at the target.</summary>
        OverShoulder,
    }

    public interface ICameraService : IStoryService
    {
        UniTask FocusAsync(Transform target, float blendTime, CancellationToken ct);
        UniTask ShakeAsync(float strength, float duration, CancellationToken ct);
        void ReturnToDefault(float blendTime);

        /// <summary>
        /// Frames a target for a conversation. Separate from FocusAsync because
        /// a talking shot wants a distance and an angle, not just a look-at.
        /// </summary>
        UniTask FrameAsync(Transform target, ShotFraming framing, float blendTime,
            CancellationToken ct);

        /// <summary>
        /// Cuts to a camera the scene already has, found by its Story Stage Id.
        /// This is how a game with hand-placed shots drives them from a graph.
        /// </summary>
        bool Activate(string cameraId, float blendTime);

        /// <summary>Orbits the current shot around its target. Returns when done.</summary>
        UniTask OrbitAsync(float degrees, float duration, CancellationToken ct);
    }

    /// <summary>One camera move, in the numbers a camera actually needs.</summary>
    public struct CameraMove
    {
        /// <summary>Seconds. Zero snaps straight to the end.</summary>
        public float Duration;

        /// <summary>Metres closer (negative) or further away (positive).</summary>
        public float DistanceChange;

        /// <summary>Degrees to swing around the subject.</summary>
        public float Degrees;

        /// <summary>Metres to rise (positive) or drop (negative).</summary>
        public float HeightChange;

        /// <summary>Constant wobble, 0 to 1. Held for the length of the move.</summary>
        public float Handheld;

        /// <summary>Ease in and out instead of a constant rate.</summary>
        public bool Smooth;
    }

    /// <summary>
    /// Cameras that can run a described move. Kept apart from ICameraService so
    /// a game can supply a simple camera without having to implement moves it
    /// does not want.
    /// </summary>
    public interface ICameraMoves : IStoryService
    {
        UniTask MoveAsync(CameraMove move, CancellationToken ct);

        /// <summary>Ends any wobble a move left running.</summary>
        void StopHandheld();
    }

    public interface IAudioService : IStoryService
    {
        void PlayBgm(string bgmId, float crossfade);
        void StopBgm(float fadeOut);
        void PlaySfx(string sfxId);
        UniTask PlayVoiceAsync(string lineKey, CancellationToken ct);
    }

    public interface IQuestService : IStoryService
    {
        void StartQuest(string questId);
        void AdvanceStep(string questId, string stepId);
        void CompleteQuest(string questId);
        void FailQuest(string questId);
        int GetQuestState(string questId);
    }

    public interface IInventoryService : IStoryService
    {
        void GiveItem(string itemId, int amount);
        bool TakeItem(string itemId, int amount);
        int CountItem(string itemId);
        void GiveCurrency(int amount);

        /// <summary>
        /// Every item the player is currently carrying. Needed by any item
        /// screen: without it the UI can only ask "do I have X" one id at a
        /// time, which means it can never list anything.
        /// </summary>
        void CollectOwnedItems(List<(string itemId, int count)> into);
    }

    public interface IInputService : IStoryService
    {
        /// <summary>Lock player control groups. Returns a handle; disposing it unlocks.
        /// Handle-based so overlapping locks can never leave input stuck.</summary>
        System.IDisposable LockPlayerControl(string reason);
    }

    public interface ISaveService : IStoryService
    {
        void RequestAutoSave();
        void SetCheckpoint(string checkpointId);
    }

    public interface ICombatService : IStoryService
    {
        void StartEncounter(string encounterId);
        void EndEncounter(bool playerWon);
        bool IsEncounterActive { get; }
        void SetInvincible(string actorId, bool invincible);
        void DealDamage(string actorId, float amount);
    }
}
