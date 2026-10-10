using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Synthesises a placeholder for every sound id the game already asks for, and
    /// builds the Sound Bank that AudioDirector loads from Resources/Audio.
    ///
    /// The technique is sfxr's (a wave, a pitch slide, an envelope, a filter, some
    /// noise), researched from jsfxr and usfxr, but voiced soft on purpose: sine and
    /// triangle, low passed, never the harsh square wave sfxr is known for. The game
    /// is wholesome and its placeholders should not teach anyone it sounds like a
    /// retro arcade.
    ///
    /// Nothing is downloaded. Rebuilding only fills entries that have no clip yet, so a
    /// real sound dropped into the bank by hand is never replaced, unless "Overwrite"
    /// is ticked. Every recipe below is a few numbers: tune one and press the button.
    /// </summary>
    public sealed class SoundForgeWindow : EditorWindow
    {
        private const string ClipFolder = "Assets/Audio/Placeholder";
        private const string BankPath = "Assets/Resources/Audio/SFX_Placeholder.asset";
        private const int Rate = 44100;

        private bool _overwrite;
        private Vector2 _scroll;

        [MenuItem("Weizza/Audio/Placeholder Sound Forge", priority = 20)]
        private static void Open() => GetWindow<SoundForgeWindow>("Sound Forge");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Makes a soft placeholder for every sound id the code asks for (hits, steps, dash, stance, " +
                "checkpoint...), a forest ambience loop and three music loops, then fills the Sound Bank in " +
                "Resources/Audio. Real sounds already in the bank are kept.", MessageType.Info);
            _overwrite = EditorGUILayout.ToggleLeft("Overwrite entries that already have clips", _overwrite);
            GUI.backgroundColor = new Color(0.5f, 0.85f, 0.55f);
            if (GUILayout.Button("Build every placeholder sound", GUILayout.Height(34))) BuildAll(_overwrite);
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recipes", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in Recipes()) EditorGUILayout.LabelField(r.id, $"{r.category}, {r.variations} take(s)");
            EditorGUILayout.EndScrollView();
        }

        // ---- Recipes ---------------------------------------------------------------

        private sealed class Recipe
        {
            public string id;
            public SoundCategory category = SoundCategory.Sfx;
            public int variations = 3;
            public float volume = 0.8f;
            public Vector2 pitch = new(0.95f, 1.05f);
            public float cooldown = 0.04f;
            public bool loop;
            public Func<int, float[]> make;
        }

        private static IEnumerable<Recipe> Recipes()
        {
            // Movement
            yield return Sfx("step", 4, 0.45f, s => Mix(1.0f,
                Noise(0.07f, 0.002f, 700f, 250f, 0.8f, s),
                Tone(Wave.Triangle, 0.06f, 0.002f, 140f, 90f, 0.4f, s)));
            yield return Sfx("land", 3, 0.6f, s => Mix(1.0f,
                Noise(0.14f, 0.003f, 500f, 150f, 0.9f, s),
                Tone(Wave.Sine, 0.16f, 0.002f, 95f, 55f, 0.7f, s)));
            yield return Sfx("move.dash", 3, 0.6f, s => Mix(1.0f,
                Swell(Noise(0.24f, 0.08f, 400f, 3200f, 0.9f, s)),
                Tone(Wave.Triangle, 0.18f, 0.01f, 300f, 520f, 0.2f, s)));
            yield return Sfx("move.walljump", 3, 0.6f, s => Mix(1.0f,
                Tone(Wave.Triangle, 0.11f, 0.003f, 300f, 520f, 0.6f, s),
                Noise(0.06f, 0.001f, 2000f, 600f, 0.4f, s)));

            // Combat. Light and heavy differ in weight, armoured rings, ignored is a dull tick.
            yield return Sfx("hit.light", 4, 0.75f, s => Mix(1.0f,
                Noise(0.06f, 0.001f, 3500f, 900f, 0.7f, s),
                Tone(Wave.Sine, 0.09f, 0.001f, 320f, 150f, 0.8f, s)));
            yield return Sfx("hit.heavy", 3, 0.9f, s => Soften(Mix(1.0f,
                Noise(0.18f, 0.001f, 1800f, 250f, 0.9f, s),
                Tone(Wave.Sine, 0.26f, 0.001f, 170f, 55f, 1.0f, s)), 1.6f));
            yield return Sfx("hit.armored", 3, 0.7f, s => Mix(1.0f,
                Bell(0.35f, 880f + 30f * Jit(s), 0.6f),
                Bell(0.3f, 1370f + 40f * Jit(s), 0.35f),
                Noise(0.03f, 0.001f, 5000f, 2000f, 0.4f, s)));
            yield return Sfx("hit.ignored", 2, 0.4f, s => Tone(Wave.Triangle, 0.05f, 0.001f, 230f, 200f, 0.7f, s));
            yield return Sfx("enemy.kill", 3, 0.7f, s => Mix(1.0f,
                Noise(0.32f, 0.004f, 2200f, 200f, 0.6f, s),
                Delay(Bell(0.5f, 1046f, 0.35f), 0.05f)));
            yield return Sfx("player.hurt", 3, 0.8f, s => Mix(1.0f,
                Vibrato(Tone(Wave.Sine, 0.22f, 0.002f, 560f, 260f, 0.8f, s), 22f, 0.04f),
                Noise(0.08f, 0.001f, 2500f, 500f, 0.5f, s)));
            yield return Sfx("shot.fire", 3, 0.6f, s => Mix(1.0f,
                Swell(Noise(0.14f, 0.01f, 900f, 3800f, 0.7f, s)),
                Tone(Wave.Triangle, 0.12f, 0.003f, 620f, 950f, 0.35f, s)));
            yield return Sfx("shot.hit", 3, 0.6f, s => Mix(1.0f,
                Tone(Wave.Sine, 0.07f, 0.001f, 1250f, 620f, 0.7f, s),
                Noise(0.04f, 0.001f, 4000f, 1200f, 0.4f, s)));
            yield return Sfx("aim.slow.in", 1, 0.5f, s => Lowpass(Tone(Wave.Sine, 0.45f, 0.05f, 620f, 280f, 0.8f, s), 1800f));
            yield return Sfx("aim.slow.out", 1, 0.5f, s => Lowpass(Tone(Wave.Sine, 0.32f, 0.03f, 280f, 620f, 0.8f, s), 1800f));

            // Stances
            yield return Sfx("stance.switch", 3, 0.6f, s => Mix(1.0f,
                Bell(0.3f, 660f * Mathf.Pow(2f, (s % 3) / 12f), 0.6f),
                Bell(0.25f, 990f * Mathf.Pow(2f, (s % 3) / 12f), 0.3f)));
            yield return Sfx("stance.learned", 1, 0.7f, s => Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.12f, 1.4f));
            yield return Sfx("ui.denied", 1, 0.5f, s => Mix(1.0f,
                Tone(Wave.Triangle, 0.07f, 0.002f, 190f, 185f, 0.6f, s),
                Delay(Tone(Wave.Triangle, 0.09f, 0.002f, 160f, 150f, 0.6f, s), 0.09f)));

            // Life and death. Death is a sigh, not a failure sting; revive rises.
            yield return Sfx("player.death", 1, 0.75f, s => Lowpass(Mix(1.0f,
                Tone(Wave.Sine, 1.3f, 0.02f, 440f, 110f, 0.8f, s),
                Noise(1.3f, 0.3f, 900f, 200f, 0.15f, s)), 2200f), 0.5f);
            yield return Sfx("player.revive", 1, 0.7f, s => Mix(1.0f,
                Arpeggio(new[] { 392f, 523.25f, 659.25f, 783.99f, 1046.5f }, 0.1f, 1.6f),
                Swell(Noise(1.2f, 0.6f, 3000f, 6000f, 0.06f, s))), 0.5f);
            yield return Sfx("player.heal", 2, 0.6f, s => Mix(1.0f, Bell(0.5f, 784f, 0.5f), Delay(Bell(0.5f, 1046.5f, 0.45f), 0.08f)));
            yield return Sfx("world.checkpoint", 1, 0.6f, s => Mix(1.0f, Bell(1.0f, 880f, 0.5f), Delay(Bell(1.0f, 1318.5f, 0.35f), 0.12f)), 0.5f);
            yield return Sfx("ui.save", 1, 0.5f, s => Mix(1.0f, Bell(0.4f, 659.25f, 0.5f), Delay(Bell(0.5f, 987.77f, 0.5f), 0.1f)), 0.5f);
            yield return Sfx("world.shrine", 1, 0.6f, s => Mix(1.0f,
                Bell(2.2f, 523.25f, 0.4f), Delay(Bell(2.0f, 659.25f, 0.3f), 0.15f), Delay(Bell(1.8f, 783.99f, 0.3f), 0.3f)), 1f);

            // Boss
            yield return Sfx("boss.roar", 1, 0.8f, s => Soften(Mix(1.0f,
                Tremolo(Tone(Wave.Saw, 1.4f, 0.12f, 72f, 52f, 0.6f, s), 9f, 0.5f),
                Noise(1.4f, 0.1f, 600f, 250f, 0.5f, s)), 1.4f), 1f);
            // An insect roar: a low growl under a chittering rasp and a thin screech on top,
            // so it reads as a beetle and not as a lion.
            yield return Sfx("boss.beetle.roar", 1, 0.9f, s => Soften(Mix(1.0f,
                Tremolo(Tone(Wave.Saw, 1.8f, 0.08f, 64f, 44f, 0.55f, s), 14f, 0.6f),
                Tremolo(Noise(1.8f, 0.05f, 1400f, 500f, 0.45f, s), 23f, 0.8f),
                Lowpass(Vibrato(Tone(Wave.Saw, 1.5f, 0.12f, 880f, 600f, 0.22f, s), 7f, 0.05f), 2600f)), 1.6f), 1f);
            yield return Sfx("boss.phase", 1, 0.8f, s => Mix(1.0f,
                Tone(Wave.Sine, 0.9f, 0.002f, 70f, 40f, 1.0f, s),
                Noise(0.6f, 0.002f, 1200f, 120f, 0.6f, s)), 0.5f);

            // Ambience and music, long seamless loops.
            yield return new Recipe
            {
                id = "amb.forest", category = SoundCategory.Ambience, variations = 1, volume = 0.55f,
                pitch = Vector2.one, cooldown = 0f, loop = true, make = _ => ForestAmbience(24f),
            };
            yield return new Recipe
            {
                id = "bgm.forest", category = SoundCategory.Music, variations = 1, volume = 0.45f,
                pitch = Vector2.one, cooldown = 0f, loop = true, make = _ => Music(false, false),
            };
            yield return new Recipe
            {
                id = "bgm.combat", category = SoundCategory.Music, variations = 1, volume = 0.5f,
                pitch = Vector2.one, cooldown = 0f, loop = true, make = _ => Music(true, false),
            };
            yield return new Recipe
            {
                id = "bgm.boss", category = SoundCategory.Music, variations = 1, volume = 0.55f,
                pitch = Vector2.one, cooldown = 0f, loop = true, make = _ => Music(true, true),
            };
        }

        private static Recipe Sfx(string id, int takes, float volume, Func<int, float[]> make, float cooldown = 0.04f) =>
            new() { id = id, variations = takes, volume = volume, make = make, cooldown = cooldown };

        // ---- Build -----------------------------------------------------------------

        public static void BuildAll(bool overwrite)
        {
            Directory.CreateDirectory(ClipFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(BankPath)!);

            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
            if (bank == null)
            {
                bank = CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }

            var written = new List<(Recipe recipe, List<string> paths)>();
            var made = 0;
            try
            {
                var recipes = new List<Recipe>(Recipes());
                for (var i = 0; i < recipes.Count; i++)
                {
                    var r = recipes[i];
                    EditorUtility.DisplayProgressBar("Sound Forge", r.id, i / (float)recipes.Count);
                    var existing = bank.entries.Find(e => e.id == r.id);
                    if (existing != null && existing.clips != null && existing.clips.Length > 0 && !overwrite) continue;

                    var paths = new List<string>();
                    for (var v = 0; v < r.variations; v++)
                    {
                        var samples = r.make(v * 7919 + r.id.GetHashCode());
                        Normalise(samples, r.category == SoundCategory.Sfx ? 0.9f : 0.8f);
                        var path = $"{ClipFolder}/{r.id}{(r.variations > 1 ? "_" + (v + 1) : "")}.wav";
                        WriteWav(path, samples);
                        paths.Add(path);
                        made++;
                    }
                    written.Add((r, paths));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            foreach (var (r, paths) in written)
            {
                var clips = new List<AudioClip>();
                foreach (var p in paths)
                {
                    SetImport(p, r.category);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                    if (clip != null) clips.Add(clip);
                }

                var entry = bank.entries.Find(e => e.id == r.id);
                if (entry == null)
                {
                    entry = new SoundEntry { id = r.id };
                    bank.entries.Add(entry);
                }
                entry.category = r.category;
                entry.clips = clips.ToArray();
                entry.volume = r.volume;
                entry.pitch = r.pitch;
                entry.cooldown = r.cooldown;
                entry.loop = r.loop;
            }

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Sound Forge] Wrote {made} clip(s) for {written.Count} sound(s) into {BankPath}.");
        }

        // Effects decompress on load (short, played often), music streams, ambience
        // stays compressed in memory: the standard split for each kind of clip.
        private static void SetImport(string path, SoundCategory category)
        {
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer) return;
            importer.forceToMono = true;
            var s = importer.defaultSampleSettings;
            s.loadType = category switch
            {
                SoundCategory.Music => AudioClipLoadType.Streaming,
                SoundCategory.Ambience => AudioClipLoadType.CompressedInMemory,
                _ => AudioClipLoadType.DecompressOnLoad,
            };
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = category == SoundCategory.Sfx ? 0.7f : 0.5f;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }

        // ---- Synthesis primitives -----------------------------------------------------

        private enum Wave { Sine, Triangle, Saw }

        private static float Jit(int seed) => (float)new System.Random(seed).NextDouble() * 2f - 1f;

        private static float Osc(Wave wave, double phase)
        {
            var p = phase - Math.Floor(phase);
            return wave switch
            {
                Wave.Triangle => (float)(p < 0.5 ? 4 * p - 1 : 3 - 4 * p),
                Wave.Saw => (float)(2 * p - 1),
                _ => (float)Math.Sin(p * 2 * Math.PI),
            };
        }

        /// <summary>A tone sliding exponentially from one pitch to another, with an attack and an exponential decay.</summary>
        private static float[] Tone(Wave wave, float seconds, float attack, float from, float to, float amp, int seed)
        {
            var n = Mathf.CeilToInt(seconds * Rate);
            var buf = new float[n];
            var rand = new System.Random(seed);
            var detune = 1f + ((float)rand.NextDouble() - 0.5f) * 0.04f;
            double phase = 0;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                var f = from * Mathf.Pow(to / from, t) * detune;
                phase += f / Rate;
                buf[i] = Osc(wave, phase) * amp * Env(i, n, attack);
            }
            return buf;
        }

        /// <summary>Noise through a low pass filter whose cutoff slides, the core of every hit, step and whoosh.</summary>
        private static float[] Noise(float seconds, float attack, float cutFrom, float cutTo, float amp, int seed)
        {
            var n = Mathf.CeilToInt(seconds * Rate);
            var buf = new float[n];
            var rand = new System.Random(seed ^ 0x5bd1e995);
            float y = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                var cut = cutFrom * Mathf.Pow(cutTo / cutFrom, t);
                var a = 1f - Mathf.Exp(-2f * Mathf.PI * cut / Rate);
                var white = (float)rand.NextDouble() * 2f - 1f;
                y += a * (white - y);
                buf[i] = y * amp * Env(i, n, attack) * 2.2f;
            }
            return buf;
        }

        /// <summary>A soft bell: a sine plus a quiet inharmonic partial, long exponential ring.</summary>
        private static float[] Bell(float seconds, float freq, float amp)
        {
            var n = Mathf.CeilToInt(seconds * Rate);
            var buf = new float[n];
            for (var i = 0; i < n; i++)
            {
                var time = i / (float)Rate;
                var env = Mathf.Exp(-time * 5f / seconds) * Mathf.Min(1f, time / 0.004f);
                var s = Mathf.Sin(2 * Mathf.PI * freq * time) + 0.25f * Mathf.Sin(2 * Mathf.PI * freq * 2.76f * time) * Mathf.Exp(-time * 9f);
                buf[i] = s * amp * env;
            }
            return buf;
        }

        private static float[] Arpeggio(float[] notes, float gap, float ring)
        {
            var parts = new List<float[]>();
            for (var i = 0; i < notes.Length; i++) parts.Add(Delay(Bell(ring, notes[i], 0.45f), gap * i));
            return Mix(1f, parts.ToArray());
        }

        // Attack then an exponential tail that reaches silence at the end, so nothing clicks.
        private static float Env(int i, int n, float attack)
        {
            var time = i / (float)Rate;
            var a = attack <= 0f ? 1f : Mathf.Min(1f, time / attack);
            var t = i / (float)n;
            return a * Mathf.Exp(-t * 4.5f) * (1f - t);
        }

        private static float[] Mix(float gain, params float[][] parts)
        {
            var n = 0;
            foreach (var p in parts) n = Math.Max(n, p.Length);
            var buf = new float[n];
            foreach (var p in parts)
                for (var i = 0; i < p.Length; i++) buf[i] += p[i] * gain;
            return buf;
        }

        private static float[] Delay(float[] src, float seconds)
        {
            var offset = Mathf.CeilToInt(seconds * Rate);
            var buf = new float[src.Length + offset];
            Array.Copy(src, 0, buf, offset, src.Length);
            return buf;
        }

        // Fades in over the whole length instead of decaying, for whooshes.
        private static float[] Swell(float[] src)
        {
            var n = src.Length;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                src[i] *= Mathf.Sin(t * Mathf.PI) / Mathf.Max(0.05f, Mathf.Exp(-t * 4.5f) * (1f - t)) * 0.5f;
            }
            return src;
        }

        private static float[] Vibrato(float[] src, float rate, float depth)
        {
            var buf = new float[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                var shift = Mathf.Sin(2 * Mathf.PI * rate * i / Rate) * depth * Rate * 0.002f;
                var j = Mathf.Clamp(i + Mathf.RoundToInt(shift), 0, src.Length - 1);
                buf[i] = src[j];
            }
            return buf;
        }

        private static float[] Tremolo(float[] src, float rate, float depth)
        {
            for (var i = 0; i < src.Length; i++)
                src[i] *= 1f - depth * 0.5f * (1f + Mathf.Sin(2 * Mathf.PI * rate * i / Rate));
            return src;
        }

        private static float[] Lowpass(float[] src, float cutoff)
        {
            var a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
            float y = 0f;
            for (var i = 0; i < src.Length; i++)
            {
                y += a * (src[i] - y);
                src[i] = y;
            }
            return src;
        }

        // Gentle saturation, for weight on heavy hits without clipping.
        private static float[] Soften(float[] src, float drive)
        {
            for (var i = 0; i < src.Length; i++) src[i] = (float)Math.Tanh(src[i] * drive) / (float)Math.Tanh(drive);
            return src;
        }

        private static void Normalise(float[] buf, float peak)
        {
            var max = 0f;
            foreach (var s in buf) max = Mathf.Max(max, Mathf.Abs(s));
            if (max < 1e-5f) return;
            var g = peak / max;
            for (var i = 0; i < buf.Length; i++) buf[i] *= g;
        }

        // ---- Long loops ----------------------------------------------------------------

        /// <summary>
        /// Wind through leaves plus sparse bird calls. Every slow movement completes a
        /// whole number of cycles over the loop and the tail is crossfaded into the
        /// head, so the loop point cannot be heard.
        /// </summary>
        private static float[] ForestAmbience(float seconds)
        {
            var n = Mathf.CeilToInt(seconds * Rate);
            var fade = Rate * 2;
            var total = n + fade;
            var buf = new float[total];
            var rand = new System.Random(1234);

            float lowY = 0f, highY = 0f;
            for (var i = 0; i < total; i++)
            {
                var t = i / (float)n;
                var gust = 0.55f + 0.3f * Mathf.Sin(2 * Mathf.PI * 2 * t) + 0.15f * Mathf.Sin(2 * Mathf.PI * 5 * t + 1.3f);
                var cut = 250f + 650f * gust;
                var a = 1f - Mathf.Exp(-2f * Mathf.PI * cut / Rate);
                var white = (float)rand.NextDouble() * 2f - 1f;
                lowY += a * (white - lowY);
                var aHigh = 1f - Mathf.Exp(-2f * Mathf.PI * 3500f / Rate);
                highY += aHigh * (white - highY);
                // A bed of low wind, plus leaf rustle that rises with each gust.
                buf[i] = lowY * 1.6f * gust + (highY - lowY) * 0.05f * gust * gust;
            }

            // Birds: short rising and falling sine chirps, a few per loop.
            for (var b = 0; b < 9; b++)
            {
                var start = (float)rand.NextDouble() * (seconds - 1.5f);
                var baseF = 2200f + (float)rand.NextDouble() * 1800f;
                var chirps = 2 + rand.Next(3);
                for (var c = 0; c < chirps; c++)
                {
                    var at = Mathf.CeilToInt((start + c * 0.13f) * Rate);
                    var len = Mathf.CeilToInt(0.09f * Rate);
                    double phase = 0;
                    for (var i = 0; i < len && at + i < total; i++)
                    {
                        var tt = i / (float)len;
                        var f = baseF * (1f + 0.35f * Mathf.Sin(tt * Mathf.PI)) * (c % 2 == 0 ? 1f : 1.12f);
                        phase += f / Rate;
                        buf[at + i] += (float)Math.Sin(phase * 2 * Math.PI) * 0.08f * Mathf.Sin(tt * Mathf.PI);
                    }
                }
            }

            return Seam(buf, n, fade);
        }

        /// <summary>
        /// Slow pentatonic pad music in A minor (Am F C G, eight seconds a chord), with a
        /// few plucked notes. combat adds a soft pulse and ticks; boss drops to a darker
        /// progression. Same loop length and seam method as the ambience.
        /// </summary>
        private static float[] Music(bool pulse, bool boss)
        {
            var chordSeconds = boss ? 6f : 8f;
            float[][] chords = boss
                ? new[] { new[] { 146.83f, 174.61f, 220f }, new[] { 116.54f, 146.83f, 174.61f }, new[] { 130.81f, 164.81f, 196f }, new[] { 110f, 138.59f, 164.81f } }
                : new[] { new[] { 220f, 261.63f, 329.63f }, new[] { 174.61f, 220f, 261.63f }, new[] { 130.81f, 164.81f, 196f }, new[] { 196f, 246.94f, 293.66f } };
            var seconds = chordSeconds * chords.Length;
            var n = Mathf.CeilToInt(seconds * Rate);
            var fade = Rate * 2;
            var total = n + fade;
            var buf = new float[total];
            var rand = new System.Random(boss ? 77 : 42);

            for (var i = 0; i < total; i++)
            {
                var time = i / (float)Rate;
                var loopTime = time % seconds;
                var index = Mathf.FloorToInt(loopTime / chordSeconds) % chords.Length;
                var within = loopTime - index * chordSeconds;
                var env = Mathf.Min(1f, within / 1.5f) * Mathf.Min(1f, (chordSeconds - within) / 1.2f + 0.25f);
                var s = 0f;
                foreach (var f in chords[index])
                {
                    s += Mathf.Sin(2 * Mathf.PI * f * time) * 0.18f;
                    s += Osc(Wave.Triangle, f * 0.5 * time) * 0.06f;
                }
                buf[i] = s * env;

                if (pulse)
                {
                    var bpm = boss ? 112f : 96f;
                    var beat = time * bpm / 60f;
                    var inBeat = beat - Mathf.Floor(beat);
                    var root = chords[index][0] * 0.5f;
                    buf[i] += Mathf.Sin(2 * Mathf.PI * root * time) * Mathf.Exp(-inBeat * 6f) * 0.22f;
                }
            }

            // Plucked notes from the pentatonic scale, sparse.
            var scale = boss ? new[] { 293.66f, 349.23f, 392f, 440f, 523.25f } : new[] { 440f, 523.25f, 587.33f, 659.25f, 783.99f };
            var plucks = boss ? 10 : 14;
            for (var p = 0; p < plucks; p++)
            {
                var at = (float)rand.NextDouble() * (seconds - 2f);
                var note = Bell(1.8f, scale[rand.Next(scale.Length)], 0.16f);
                var offset = Mathf.CeilToInt(at * Rate);
                for (var i = 0; i < note.Length && offset + i < total; i++) buf[offset + i] += note[i];
            }

            if (pulse)
            {
                var bpm = boss ? 112f : 96f;
                var beatLen = 60f / bpm;
                for (var b = 0f; b < seconds + 2f; b += beatLen * 0.5f)
                {
                    var tick = Noise(0.05f, 0.001f, 6000f, 3000f, 0.06f, Mathf.RoundToInt(b * 1000));
                    var offset = Mathf.CeilToInt(b * Rate);
                    for (var i = 0; i < tick.Length && offset + i < total; i++) buf[offset + i] += tick[i];
                }
            }

            return Seam(Lowpass(buf, 5000f), n, fade);
        }

        // Crossfades the extra tail into the head and trims to the loop length.
        private static float[] Seam(float[] buf, int n, int fade)
        {
            var output = new float[n];
            Array.Copy(buf, output, n);
            for (var i = 0; i < fade && n + i < buf.Length; i++)
            {
                var t = i / (float)fade;
                output[i] = output[i] * t + buf[n + i] * (1f - t);
            }
            return output;
        }

        // ---- WAV ---------------------------------------------------------------------

        private static void WriteWav(string path, float[] samples)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(stream);
            var dataBytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        }
    }
}
