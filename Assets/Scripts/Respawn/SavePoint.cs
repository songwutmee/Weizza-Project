using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A place of light the player can rest at. Press E beside it to save the game and make it
    /// the place to wake up at. Sits on a Checkpoint and stops that checkpoint from claiming
    /// itself when walked through, so a save is always the player's choice.
    ///
    /// The light is drawn in code: a slow breathing halo, a bright heart that blooms, and a
    /// few motes drifting up. All additive, so the scene's bloom turns it into real glow.
    /// </summary>
    [AddComponentMenu("Weizza/Save Point")]
    [RequireComponent(typeof(Checkpoint))]
    public sealed class SavePoint : MonoBehaviour
    {
        [BoxGroup("Interaction")]
        [PropertyRange(1f, 5f), Tooltip("How close the player must stand for E to work.")]
        [SerializeField] private float range = 2.4f;

        [BoxGroup("Look")]
        [SerializeField] private Color tint = new(1f, 0.86f, 0.5f, 1f);

        [BoxGroup("Look")]
        [PropertyRange(2, 10)]
        [SerializeField] private int motes = 5;

        private Checkpoint _checkpoint;
        private Narrative.InteractPrompt _prompt;
        private SpriteRenderer _halo, _core;
        private Transform[] _moteTransforms;
        private SpriteRenderer[] _moteRenderers;
        private float[] _moteSeed;
        private float _breath, _flash;
        private Transform _root;

        private void Awake()
        {
            _checkpoint = GetComponent<Checkpoint>();
            _checkpoint.ClaimOnTouch = false;
            if (!Application.isPlaying) return;
            BuildLight();
        }

        private void BuildLight()
        {
            // Its own copy at a lower strength: the shared glow is so bright that its small alpha steps
            // show as rings, so this one is dimmer per unit and gets larger alpha values instead.
            var material = GlowFx.Material != null ? new Material(GlowFx.Material) : null;
            if (material != null) material.SetFloat("_Intensity", 3f);
            _root = new GameObject("Save Light").transform;
            _root.SetParent(transform, false);
            _root.localPosition = new Vector3(0f, 1.1f, 0f);

            _halo = Make("Halo", Soft(), material, 6);
            _halo.transform.localScale = new Vector3(3.2f, 4.2f, 1f);
            _core = Make("Heart", Soft(), material, 7);
            _core.transform.localScale = new Vector3(0.8f, 1.1f, 1f);

            _moteTransforms = new Transform[motes];
            _moteRenderers = new SpriteRenderer[motes];
            _moteSeed = new float[motes];
            for (var i = 0; i < motes; i++)
            {
                _moteRenderers[i] = Make("Mote", Soft(), material, 8);
                _moteTransforms[i] = _moteRenderers[i].transform;
                _moteTransforms[i].localScale = Vector3.one * Random.Range(0.1f, 0.2f);
                _moteSeed[i] = Random.value * 10f;
            }

            var prompt = new GameObject("Prompt");
            prompt.transform.SetParent(transform, false);
            prompt.transform.localPosition = Vector3.up * 3f;
            _prompt = prompt.AddComponent<Narrative.InteractPrompt>();
            _prompt.SetVisible(false);
        }

        // A round falloff that reaches exactly zero at its edge. The shared glow sprite stops at a
        // small value, which an additive material this bright turns into a visible square.
        private static Sprite _soft;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSoft() => _soft = null;

        private static Sprite Soft()
        {
            if (_soft != null) return _soft;
            const int size = 128;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.4f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SaveSoft" };
            texture.SetPixels32(pixels);
            texture.Apply();
            _soft = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _soft;
        }

        private SpriteRenderer Make(string objectName, Sprite sprite, Material material, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (material != null) sr.sharedMaterial = material;
            sr.sortingLayerName = "Default";
            sr.sortingOrder = order;
            return sr;
        }

        private void Update()
        {
            if (_halo == null) return;

            // Phase is accumulated, so the breathing never jumps.
            _breath += Time.deltaTime * 0.9f;
            var swell = 0.5f + 0.5f * Mathf.Sin(_breath);
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 1.2f);

            // The additive material is very bright, so alpha stays small: it is the glow's strength.
            var haloA = Mathf.Lerp(0.2f, 0.32f, swell) + _flash * 0.4f;
            var coreA = Mathf.Clamp01(Mathf.Lerp(0.6f, 0.85f, swell) + _flash * 0.15f);
            _halo.color = new Color(tint.r, tint.g, tint.b, haloA);
            _core.color = new Color(tint.r, tint.g, tint.b, coreA);
            _halo.transform.localScale = new Vector3(3.2f, 4.2f, 1f) * (1f + swell * 0.08f + _flash * 0.5f);

            for (var i = 0; i < _moteTransforms.Length; i++)
            {
                var t = Mathf.Repeat(Time.time * 0.18f + _moteSeed[i], 1f);
                var x = Mathf.Sin(Time.time * 0.7f + _moteSeed[i] * 3f) * 0.5f;
                _moteTransforms[i].localPosition = new Vector3(x, -0.8f + t * 3.2f, 0f);
                var fade = Mathf.Clamp01(t * 5f) * Mathf.Clamp01((1f - t) * 3f);
                _moteRenderers[i].color = new Color(tint.r, tint.g, tint.b, fade * 0.7f);
            }

            var player = PlayerLocator.Player;
            var near = player != null
                       && Mathf.Abs(player.transform.position.x - transform.position.x) <= range
                       && Mathf.Abs(player.transform.position.y - transform.position.y) <= 3f;
            var free = !StoryKit.StoryControl.MovementLocked && !Narrative.DialogueDirector.Talking;
            _prompt.SetVisible(near && free);

            if (near && free && InputReader.Instance != null && InputReader.Instance.InteractPressed) Rest();
        }

        private void Rest()
        {
            _flash = 1f;
            // Resting at the light mends you, the way a bench does: players expect a
            // save spot to be safe in every sense. Through Heal, so the wings visibly regrow.
            var vitals = PlayerLocator.Player != null ? PlayerLocator.Player.Vitals : null;
            if (vitals != null && vitals.IsAlive) vitals.Heal(vitals.Max);
            _checkpoint.ClaimNow();
            SaveSystem.Instance?.Save();
            HudScreen.Instance?.Banner("Saved");
            CameraFeel.Instance?.Shake(0.05f);
        }
    }
}
