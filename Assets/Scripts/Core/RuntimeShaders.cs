using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Every shader the game looks up by name at runtime, in one list.
    ///
    /// A build only contains shaders that something references: a material in a scene,
    /// a material in Resources, or the Always Included list in Graphics settings. A shader
    /// reached only through Shader.Find is stripped, Find returns null, and the effect
    /// silently vanishes or throws, but only in a build. The revive dissolve and the
    /// pickup outline were both in that state. Weizza > Validate World checks this list
    /// and can add any missing one to Always Included.
    /// </summary>
    public static class RuntimeShaders
    {
        public static readonly string[] Names =
        {
            "Weizza/Dissolve",
            "Weizza/SpriteOutline",
            "Weizza/SpriteRim",
            "Sprites/Default",
        };

        private static readonly HashSet<string> Warned = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Warned.Clear();

        /// <summary>Shader.Find that says once, loudly, when the shader did not make it into the build.</summary>
        public static Shader Find(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null && Warned.Add(name))
                Debug.LogError($"[RuntimeShaders] '{name}' is not in this build. Run Weizza > Validate World and let it fix Always Included Shaders.");
            return shader;
        }
    }
}
