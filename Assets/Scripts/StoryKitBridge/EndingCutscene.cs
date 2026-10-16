using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.SceneManagement;
using Weizza.Narrative;

namespace Weizza
{
    /// <summary>
    /// The end of Act 0. Put it on the last person the player talks to. When that conversation
    /// closes, the player is held, the camera rises and pulls back over the forest, the game's
    /// name stands alone on screen, the picture goes dark and the game starts again from the
    /// beginning with nothing remembered.
    /// </summary>
    [AddComponentMenu("Weizza/Story/Ending Cutscene")]
    public sealed class EndingCutscene : MonoBehaviour
    {
        [BoxGroup("Title")]
        [SerializeField] private string title = "Weizza";

        [BoxGroup("Title")]
        [PropertyRange(2f, 12f), Tooltip("Seconds the title stays fully visible.")]
        [SerializeField] private float titleSeconds = 6f;

        [BoxGroup("Camera")]
        [PropertyRange(0.1f, 1.2f), Tooltip("How far the framing rises, as a share of the screen.")]
        [SerializeField] private float panUp = 0.55f;

        [BoxGroup("Camera")]
        [PropertyRange(6f, 20f), Tooltip("Orthographic size the camera ends on.")]
        [SerializeField] private float endZoom = 13f;

        [BoxGroup("Camera")]
        [PropertyRange(4f, 20f), Tooltip("Seconds the camera takes to rise and pull back.")]
        [SerializeField] private float riseSeconds = 11f;

        [BoxGroup("Light")]
        [Tooltip("The pack's light shaft drawings. They fade in over the picture while the camera rises, like sun falling through the trees.")]
        [SerializeField] private Sprite[] lightShafts = Array.Empty<Sprite>();

        [BoxGroup("Light")]
        [SerializeField] private Color shaftTint = new(1f, 0.92f, 0.62f, 1f);

        [BoxGroup("Light")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float shaftStrength = 0.5f;

        [BoxGroup("Restart")]
        [Tooltip("Scene loaded after the ending. Everything remembered is wiped first.")]
        [SerializeField] private string restartScene = "Act0_Prototype";

        [BoxGroup("Restart")]
        [PropertyRange(1f, 12f), Tooltip("How close the player must be for this conversation to count as the ending.")]
        [SerializeField] private float nearRange = 6f;

        private bool _wasTalking;
        private bool _running;

        private void Update()
        {
            var talking = DialogueDirector.Talking;
            if (talking) _wasTalking = true;
            if (talking || !_wasTalking || _running) return;
            _wasTalking = false;

            var player = PlayerLocator.Player;
            if (player == null || Mathf.Abs(player.transform.position.x - transform.position.x) > nearRange) return;
            RunAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            _running = true;
            var hold = StoryControl.Lock("Ending");
            var hud = HudScreen.Instance;
            var zone = FindFirstObjectByType<CameraZoomZone>();
            try
            {
                hud?.SetVisible(false);
                if (Letterbox.Instance != null) await Letterbox.Instance.SetAsync(true, 1.2f, ct);
                await Wait(0.6f, ct);

                var rig = ViewRig.Instance;
                if (zone != null) zone.Paused = true;
                var startZoom = zone != null && zone.CurrentSize > 0f ? zone.CurrentSize : rig != null ? rig.OrthographicSize : 6f;

                var shafts = SpawnShafts();
                var titled = false;
                var t = 0f;
                while (t < riseSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    var k = Mathf.Clamp01(t / riseSeconds);
                    var eased = k * k * (3f - 2f * k);
                    rig?.SetPanUp(Mathf.Lerp(0f, panUp, eased));
                    rig?.SetZoom(Mathf.Lerp(startZoom, endZoom, eased));
                    TickShafts(shafts, t, k);
                    if (!titled && k > 0.4f && hud != null)
                    {
                        titled = true;
                        // The name alone, none of the HUD around it.
                        hud.SetVisible(true);
                        hud.ShowOnlyTitle(true);
                        hud.TitleCard(title, titleSeconds);
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                await Wait(Mathf.Max(0f, titleSeconds - riseSeconds * 0.6f) + 1.5f, ct);
                var screen = StoryServices.Get<IScreenUI>();
                if (screen != null) await screen.FadeAsync(1f, 2f, ct);
                await Wait(0.5f, ct);

                Restart();
            }
            catch (OperationCanceledException) { }
            finally
            {
                hold?.Dispose();
                _running = false;
            }
        }

        private SpriteRenderer[] SpawnShafts()
        {
            var cam = Camera.main;
            if (cam == null || lightShafts == null || lightShafts.Length == 0) return Array.Empty<SpriteRenderer>();
            var list = new SpriteRenderer[lightShafts.Length];
            for (var i = 0; i < list.Length; i++)
            {
                var go = new GameObject("Ending Shaft");
                go.transform.SetParent(cam.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = lightShafts[i];
                sr.color = new Color(shaftTint.r, shaftTint.g, shaftTint.b, 0f);
                sr.sortingLayerName = "Foreground";
                sr.sortingOrder = 4 + i;
                list[i] = sr;
            }
            return list;
        }

        // The drawings were made for the normal view, so they grow with the zoom to keep covering the picture,
        // and each sways on its own slow beat so the light is never still.
        private void TickShafts(SpriteRenderer[] shafts, float t, float k)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var scale = cam.orthographicSize / 6f;
            var fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 2.2f));
            for (var i = 0; i < shafts.Length; i++)
            {
                if (shafts[i] == null) continue;
                var sway = Mathf.Sin(t * (0.25f + i * 0.07f) + i * 2.1f);
                shafts[i].transform.localScale = Vector3.one * scale * (1.05f + 0.04f * sway);
                shafts[i].transform.localPosition = new Vector3(sway * 0.8f * scale, 0f, 5f);
                var a = shaftStrength * fade * (0.75f + 0.25f * Mathf.Sin(t * 0.4f + i * 1.7f));
                shafts[i].color = new Color(shaftTint.r, shaftTint.g, shaftTint.b, a);
            }
        }

        private void Restart()
        {
            StoryTimeScale.SetBase(AssistOptions.Normal);
            StoryState.Global.Clear();
            SaveSystem.Instance?.DeleteSlot();
            SceneManager.LoadScene(restartScene);
        }

        private static UniTask Wait(float seconds, CancellationToken ct) =>
            UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
    }
}
