using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza
{
    /// <summary>
    /// Makes any scene playable on its own, the way Hollow Knight's team worked:
    /// open one room, press Play, and the core scene (player, camera, HUD) loads
    /// in around it and the player appears at that room's spawn.
    ///
    /// Also covers the other direction: pressing Play in the core scene alone
    /// loads the atlas start room.
    ///
    /// Does nothing at all without a WorldAtlas, so SampleScene and the
    /// playground, which carry their own player, are untouched.
    /// </summary>
    public static class WorldBoot
    {
        private static bool _booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _booted = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_booted) return;
            _booted = true;
            var atlas = WorldAtlas.Load();
            if (atlas == null) return;
            BootAsync(atlas, Application.exitCancellationToken).Forget();
        }

        private static async UniTaskVoid BootAsync(WorldAtlas atlas, CancellationToken ct)
        {
            try
            {
                var core = SceneManager.GetSceneByName(atlas.coreScene);
                var coreOpen = core.IsValid() && core.isLoaded;
                var hasPlayer = Object.FindAnyObjectByType<PlayerController>() != null;

                // Only take over when the world is actually in use: Core is open, or
                // a scene the atlas knows as a room is. SampleScene and the
                // playground are neither, and must keep working exactly as before.
                var roomOpen = false;
                for (var i = 0; i < SceneManager.sceneCount; i++)
                    if (atlas.FindByScene(SceneManager.GetSceneAt(i).name) != null) roomOpen = true;
                if (!coreOpen && !roomOpen) return;

                if (!coreOpen && !hasPlayer)
                {
                    if (!Application.CanStreamedLevelBeLoaded(atlas.coreScene))
                    {
                        Debug.LogWarning($"[WorldBoot] Core scene '{atlas.coreScene}' is not in Build Profiles, cannot boot this room on its own.");
                        return;
                    }
                    var op = SceneManager.LoadSceneAsync(atlas.coreScene, LoadSceneMode.Additive);
                    while (op != null && !op.isDone) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    core = SceneManager.GetSceneByName(atlas.coreScene);
                }

                // New objects (projectiles, damage numbers, pooled effects) go into
                // the active scene. It must be the core one, or they vanish when the
                // room they happened to spawn in unloads.
                if (core.IsValid() && core.isLoaded) SceneManager.SetActiveScene(core);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                // Which room is open? Any loaded scene the atlas knows. None means the
                // core scene was played alone, so load the start room.
                RoomRoot room = null;
                foreach (var r in RoomRoot.All) { room = room != null ? room : r; r.ActivateAllNow(); }

                if (room == null)
                {
                    var start = atlas.Find(atlas.startRoom);
                    if (start == null || WorldStreamer.Instance == null) return;
                    await WorldStreamer.Instance.EnsureLoadedAsync(start.id, ct);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    room = RoomRoot.Find(start.id);
                    if (room == null) return;
                }

                // In linear mode the room the player starts in is not enough: the
                // look ahead must be in before the first frame is shown.
                var spawnAt = coreOpen && PlayerLocator.Player != null ? PlayerLocator.Player.transform.position : room.SpawnPoint;
                if (WorldStreamer.Instance != null)
                {
                    await WorldStreamer.Instance.EnsureLoadedAtAsync(spawnAt, ct);
                    WorldStreamer.Instance.DropUnwantedNow();
                }

                // Only move the player when the core scene was brought in by us.
                // A core scene opened by hand already has the player where the
                // designer put it.
                var player = PlayerLocator.Player;
                if (player != null && !coreOpen)
                {
                    player.Motor.Teleport(room.SpawnPoint);
                    Physics.SyncTransforms();
                    CameraAnchor.CutToPlayer();
                }

                WorldStreamer.Instance?.Enter(room.Definition);
                if (atlas.linear) LinearCameraBounds.Apply(atlas);
                else room.ApplyCameraBounds();
            }
            catch (System.OperationCanceledException) { }
        }
    }
}
