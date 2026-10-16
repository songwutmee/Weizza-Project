using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Shows the game the way a colour blind player sees it, through the Scene Look pass.
    ///
    /// The whole combat language rests on one colour, one meaning: white for a stagger,
    /// amber for armour that held, red for the player losing health, a warm yellow pulse
    /// for a wind up. Red against amber is exactly the pair protan and deutan players
    /// confuse, so this is the check that the pulse (not the hue alone) carries the
    /// warning. Editor only; a build always draws normal colour.
    ///
    /// Weizza > Playtest > Colour Blind Preview. Needs Scene Look in the scene.
    /// </summary>
    [InitializeOnLoad]
    public static class ColourBlindPreview
    {
        private const string Pref = "Weizza.ColourBlindPreview";
        private static readonly int Cvd = Shader.PropertyToID("_LookCvd");
        private const string Menu = "Weizza/Playtest/Colour Blind Preview/";

        static ColourBlindPreview()
        {
            // Globals survive play mode but not a domain reload, so put the choice back.
            EditorApplication.delayCall += () => Shader.SetGlobalFloat(Cvd, EditorPrefs.GetInt(Pref, 0));
        }

        [MenuItem(Menu + "Off", priority = 1)] private static void Off() => Set(0);
        [MenuItem(Menu + "Protanopia (no red cones)", priority = 2)] private static void Protan() => Set(1);
        [MenuItem(Menu + "Deuteranopia (no green cones)", priority = 3)] private static void Deutan() => Set(2);
        [MenuItem(Menu + "Tritanopia (no blue cones)", priority = 4)] private static void Tritan() => Set(3);

        [MenuItem(Menu + "Off", true)] private static bool OffCheck() => Check(0);
        [MenuItem(Menu + "Protanopia (no red cones)", true)] private static bool ProtanCheck() => Check(1);
        [MenuItem(Menu + "Deuteranopia (no green cones)", true)] private static bool DeutanCheck() => Check(2);
        [MenuItem(Menu + "Tritanopia (no blue cones)", true)] private static bool TritanCheck() => Check(3);

        private static bool Check(int mode)
        {
            var names = new[] { "Off", "Protanopia (no red cones)", "Deuteranopia (no green cones)", "Tritanopia (no blue cones)" };
            UnityEditor.Menu.SetChecked(Menu + names[mode], EditorPrefs.GetInt(Pref, 0) == mode);
            return true;
        }

        private static void Set(int mode)
        {
            EditorPrefs.SetInt(Pref, mode);
            Shader.SetGlobalFloat(Cvd, mode);
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            if (mode != 0) Debug.Log("[Colour Blind Preview] On. Remember to switch it Off; it only affects this editor.");
        }
    }
}
