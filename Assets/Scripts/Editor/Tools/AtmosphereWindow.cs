using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds the part of a scene that makes it read as a place.
    ///
    /// The ground is not what gives a hand drawn side scroller its depth. Neva's
    /// floor is a flat green band with no detail in it at all. What carries the
    /// frame is light shafts crossing it, a black foreground with no detail,
    /// motes drifting past the camera, and one hue laid over everything so the
    /// character's colour is the only thing that reads as separate.
    ///
    /// None of that needs art, which is why it is worth doing first. It works
    /// against blockout geometry and keeps working once the real paintings
    /// arrive.
    /// </summary>
    public sealed class AtmosphereWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Atmosphere", priority = 5)]
        private static void Open()
        {
            var window = GetWindow<AtmosphereWindow>();
            window.titleContent = new GUIContent("Atmosphere");
            window.minSize = new Vector2(460f, 700f);
            window.Show();
        }

        private const string RootName = "Atmosphere";
        private const string TextureFolder = "Assets/GameData/Atmosphere";

        // ---- Light shafts ---------------------------------------------------

        [TitleGroup("Light shafts", "The diagonal beams", TitleAlignments.Centered)]
        [InfoBox("Beams are the single strongest depth cue in the reference shots " +
                 "and the cheapest to add. They are soft additive quads, so they " +
                 "brighten whatever is behind them without hiding it.")]
        [PropertyRange(0, 12)]
        [SerializeField] private int shafts = 5;

        [TitleGroup("Light shafts")]
        [PropertyRange(-80f, 80f)]
        [Tooltip("Degrees off vertical. All beams lean the same way because they " +
                 "come from one sun; beams that fan out read as spotlights.")]
        [SerializeField] private float shaftLean = 28f;

        [TitleGroup("Light shafts")]
        [PropertyRange(0.2f, 6f)]
        [SerializeField] private float shaftWidth = 1.6f;

        [TitleGroup("Light shafts")]
        [PropertyRange(6f, 40f)]
        [SerializeField] private float shaftLength = 20f;

        [TitleGroup("Light shafts")]
        [ColorUsage(true, true)]
        [Tooltip("HDR on purpose. Above one it blooms, which is what makes a beam " +
                 "look like light rather than like a grey triangle.")]
        [SerializeField] private Color shaftColour = new(1.5f, 1.35f, 0.7f, 0.12f);

        [TitleGroup("Light shafts")]
        [PropertyRange(0f, 60f)]
        [Tooltip("How far along x the beams are spread.")]
        [SerializeField] private float shaftSpread = 30f;

        [TitleGroup("Light shafts")]
        [PropertyRange(0.1f, 6f)]
        [Tooltip("Metres a beam takes to fade out once something solid stands " +
                 "between it and the camera. Small values look like a hard cut, " +
                 "large values make the beam look like it is dimming through fog.")]
        [SerializeField] private float shaftOcclusionFade = 1.2f;

        // ---- Motes ----------------------------------------------------------

        [TitleGroup("Motes", "Dust in the air", TitleAlignments.Centered)]
        [InfoBox("Placed in front of the player, not behind. Dust between the " +
                 "camera and the character is what tells the eye there is air in " +
                 "the scene; dust behind them just looks like stars.")]
        [ToggleLeft]
        [SerializeField] private bool motes = true;

        [TitleGroup("Motes")]
        [ShowIf(nameof(motes))]
        [PropertyRange(10, 300)]
        [SerializeField] private int moteCount = 90;

        [TitleGroup("Motes")]
        [ShowIf(nameof(motes))]
        [PropertyRange(4f, 60f)]
        [SerializeField] private float moteArea = 26f;

        [TitleGroup("Motes")]
        [ShowIf(nameof(motes))]
        [ColorUsage(true, true)]
        [SerializeField] private Color moteColour = new(1.4f, 1.4f, 1.1f, 0.5f);

        // ---- Grade ----------------------------------------------------------

        [TitleGroup("Grade", "One hue over everything", TitleAlignments.Centered)]
        [InfoBox("This is the step that makes a screenshot look composed rather " +
                 "than assembled. Art from different packs disagrees about colour; " +
                 "a filter over the whole frame forces agreement.")]
        [ToggleLeft]
        [SerializeField] private bool grade = true;

        [TitleGroup("Grade")]
        [ShowIf(nameof(grade))]
        [Tooltip("The colour the whole scene is pulled towards.")]
        [SerializeField] private Color wash = new(0.78f, 0.88f, 0.72f, 1f);

        [TitleGroup("Grade")]
        [ShowIf(nameof(grade))]
        [PropertyRange(-40f, 40f)]
        [SerializeField] private float saturation = -12f;

        [TitleGroup("Grade")]
        [ShowIf(nameof(grade))]
        [PropertyRange(-40f, 40f)]
        [SerializeField] private float contrast = 14f;

        [TitleGroup("Grade")]
        [ShowIf(nameof(grade))]
        [PropertyRange(0f, 1.5f)]
        [Tooltip("Vignette. Darkening the corners is how the eye is pushed to the " +
                 "middle of the frame where the player is.")]
        [SerializeField] private float corners = 0.42f;

        [TitleGroup("Grade")]
        [ShowIf(nameof(grade))]
        [PropertyRange(0f, 3f)]
        [SerializeField] private float bloom = 1.1f;

        // ---- Build ----------------------------------------------------------

        [Button("Build the atmosphere", ButtonSizes.Gigantic), GUIColor(0.45f, 0.85f, 0.6f)]
        private void Build()
        {
            var root = Root();

            BuildShafts(root);
            BuildMotes(root);
            if (grade) BuildGrade(root);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log($"[Weizza] Atmosphere built under '{RootName}'. " +
                      "Post processing has to be on for the grade to show: " +
                      "every camera's Rendering section, Post Processing ticked.");
        }

        [Button("Turn post processing on for every camera", ButtonSizes.Large)]
        [InfoBox("Off by default on a camera, with no warning. The volume sits " +
                 "there with its weight animating and nothing happens.")]
        private void EnablePostProcessing()
        {
            var touched = 0;

            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var extra = camera.GetComponent<UniversalAdditionalCameraData>();
                if (extra == null) extra = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
                if (extra.renderPostProcessing) continue;

                Undo.RecordObject(extra, "Enable post processing");
                extra.renderPostProcessing = true;
                touched++;
            }

            Debug.Log($"[Weizza] Post processing turned on for {touched} camera(s).");
        }

        private GameObject Root()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) return existing;

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create atmosphere");
            return root;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var existing = parent.transform.Find(name);
            if (existing != null) return existing.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create atmosphere part");
            return go;
        }

        // ---- Shafts ---------------------------------------------------------

        private void BuildShafts(GameObject root)
        {
            var holder = Child(root, "Light Shafts");

            for (var i = holder.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(holder.transform.GetChild(i).gameObject);
            }

            if (shafts <= 0) return;

            var sprite = ShaftSprite();
            var material = GodRayMaterial();

            for (var i = 0; i < shafts; i++)
            {
                var beam = new GameObject($"Shaft {i + 1}");
                beam.transform.SetParent(holder.transform, false);

                // Spread is uneven on purpose. Evenly spaced beams read as a
                // pattern, and a pattern reads as a texture rather than as light.
                var along = shafts == 1 ? 0.5f : i / (float)(shafts - 1);
                var jitter = Mathf.PerlinNoise(i * 3.7f, 0.5f) - 0.5f;
                var x = (along - 0.5f) * shaftSpread + jitter * shaftSpread * 0.18f;

                beam.transform.localPosition = new Vector3(x, shaftLength * 0.35f, 6f);
                beam.transform.localRotation = Quaternion.Euler(0f, 0f, shaftLean);

                var width = shaftWidth * Mathf.Lerp(0.6f, 1.4f, Mathf.PerlinNoise(i * 1.3f, 2f));
                beam.transform.localScale = new Vector3(width, shaftLength, 1f);

                var renderer = beam.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sharedMaterial = material;
                renderer.color = shaftColour;

                // In front of the level, behind the black foreground. A beam
                // drawn over the silhouette lights up the thing that is meant to
                // be the darkest object on screen.
                renderer.sortingOrder = 30;
            }
        }

        /// <summary>
        /// A one pixel wide beam, soft at both edges and fading out along its
        /// length. Drawn here rather than imported so a scene can be lit before
        /// any art exists.
        /// </summary>
        private static Sprite ShaftSprite()
        {
            var path = $"{TextureFolder}/Shaft.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            const int w = 64;
            const int h = 256;

            var pixels = new Color[w * h];

            for (var y = 0; y < h; y++)
            {
                var down = y / (float)(h - 1);

                // Brightest where it enters, gone before it lands. A beam of even
                // brightness looks like a solid object.
                var along = Mathf.Pow(1f - down, 1.6f);

                for (var x = 0; x < w; x++)
                {
                    var across = Mathf.Abs(x / (float)(w - 1) * 2f - 1f);
                    var edge = Mathf.Pow(1f - across, 2.2f);
                    pixels[y * w + x] = new Color(1f, 1f, 1f, along * edge);
                }
            }

            return Write(path, w, h, pixels);
        }

        private static Material Additive()
        {
            var path = $"{TextureFolder}/Shaft.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Folder(TextureFolder);

            // Additive rather than alpha blended. Light adds to what is behind
            // it; alpha blending replaces it, which dims the scene wherever a
            // beam crosses and reads as fog instead of as light.
            var material = new Material(Shader.Find("Sprites/Default")) { name = "Shaft" };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// The shafts' own material, on `Weizza/GodRay`. A plain additive quad
        /// (what `Additive()` above still gives the motes) draws at full
        /// brightness through solid ground and foliage, which reads as a beam
        /// painted on top of the level rather than light actually crossing it.
        /// This one samples the depth texture and fades near whatever is
        /// standing in front of it.
        /// </summary>
        private Material GodRayMaterial()
        {
            var path = $"{TextureFolder}/GodRay.mat";
            var shader = Shader.Find("Weizza/GodRay");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Folder(TextureFolder);
                material = new Material(shader) { name = "GodRay" };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetFloat("_OcclusionFade", shaftOcclusionFade);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- Motes ----------------------------------------------------------

        private void BuildMotes(GameObject root)
        {
            var holder = Child(root, "Motes");
            if (!motes)
            {
                holder.SetActive(false);
                return;
            }

            holder.SetActive(true);
            holder.transform.localPosition = new Vector3(0f, moteArea * 0.3f, -4f);

            var system = holder.GetComponent<ParticleSystem>();
            if (system == null) system = Undo.AddComponent<ParticleSystem>(holder);

            var main = system.main;
            main.loop = true;
            main.duration = 8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.13f);
            main.startColor = moteColour;
            main.maxParticles = moteCount;
            main.gravityModifier = -0.01f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = moteCount / 10f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(moteArea, moteArea * 0.7f, 0.1f);

            // Motes that drift in a straight line read as rain. Noise is what
            // makes them read as floating in air.
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.15f;
            noise.scrollSpeed = 0.12f;

            var fade = system.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(Ramp());

            // Every mote its own little turn and size, so the air does not read as a grid.
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = MoteMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            // In front of the player. This is the whole point of them.
            renderer.sortingOrder = 40;

            system.Stop();
            system.Clear();
        }

        /// <summary>
        /// Additive like the old shaft material, but with a soft round dot. With no
        /// texture a particle draws its whole quad, and the motes showed as hard squares.
        /// </summary>
        internal static Material MoteMaterial()
        {
            var path = $"{TextureFolder}/Mote.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Folder(TextureFolder);
                material = new Material(Shader.Find("Sprites/Default")) { name = "Mote" };
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = MoteTexture();
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D MoteTexture()
        {
            var path = $"{TextureFolder}/MoteDot.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            Folder(TextureFolder);
            var dot = SoftDust.Texture(64, keepReadable: true);
            System.IO.File.WriteAllBytes(path, dot.EncodeToPNG());
            Object.DestroyImmediate(dot);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Gradient Ramp() => new()
        {
            colorKeys = new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f),
            },
        };

        // ---- Grade ----------------------------------------------------------

        private void BuildGrade(GameObject root)
        {
            var holder = Child(root, "Grade Volume");

            var volume = holder.GetComponent<Volume>();
            if (volume == null) volume = Undo.AddComponent<Volume>(holder);

            volume.isGlobal = true;
            volume.priority = 5f;
            volume.weight = 1f;

            var path = $"{TextureFolder}/SceneGrade.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

            if (profile == null)
            {
                Folder(TextureFolder);
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            volume.sharedProfile = profile;

            var colours = Override<ColorAdjustments>(profile);
            colours.colorFilter.overrideState = true;
            colours.colorFilter.value = wash;
            colours.saturation.overrideState = true;
            colours.saturation.value = saturation;
            colours.contrast.overrideState = true;
            colours.contrast.value = contrast;

            var glow = Override<Bloom>(profile);
            glow.intensity.overrideState = true;
            glow.intensity.value = bloom;
            glow.threshold.overrideState = true;
            glow.threshold.value = 0.9f;
            glow.scatter.overrideState = true;
            glow.scatter.value = 0.75f;

            var edges = Override<Vignette>(profile);
            edges.intensity.overrideState = true;
            edges.intensity.value = corners;
            edges.smoothness.overrideState = true;
            edges.smoothness.value = 0.55f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Adds an override and makes it stick.
        ///
        /// VolumeProfile.Add builds the override in memory and never files it
        /// into the asset, so the profile loads back empty on the next reload
        /// with no warning anywhere. AddObjectToAsset is the missing call, and
        /// this cost a long time to find once already.
        /// </summary>
        private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing) && existing != null) return existing;

            var added = profile.Add<T>();
            added.active = true;

            if (!AssetDatabase.Contains(added))
            {
                added.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(added, profile);
            }

            return added;
        }

        // ---- Assets ---------------------------------------------------------

        private static Sprite Write(string path, int width, int height, Color[] pixels)
        {
            Folder(TextureFolder);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.Refresh();

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)!.Replace(Path.DirectorySeparatorChar, '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
