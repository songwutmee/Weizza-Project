using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// One page that answers "what was that key again" and "is there a tool for this",
    /// for anyone on the team, inside Unity. The keys work in the editor and in
    /// development builds; the tool buttons open the window they name.
    /// </summary>
    public sealed class HelpWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Help: Keys and Tools", priority = 0)]
        private static void Open() => GetWindow<HelpWindow>("Weizza Help").Show();

        [Title("Keys while playing", "Editor and development builds")]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string Keys =>
            "F1     Debug panel: health, stance, state, numbers\n" +
            "F2     Hit and hurt boxes over the game\n" +
            "F3 or `  Developer console (type help). Useful: god, boss, bosshp 55, tp x y, stance Than\n" +
            "F9     Photo mode: freeze, hide the HUD. F12 then saves a still at 2x into PhotoCaptures\n" +
            "F10    Settings (also from the pause menu)\n" +
            "Hold up or down while standing still: look above or below";

        [Title("Settings players can change", "Pause menu > Settings")]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string Settings =>
            "Comfort: screen shake, controller rumble, softer flashes, damage numbers\n" +
            "Assist: game speed (0.5 to 1), longer enemy warnings, hold attack to keep swinging, invincible\n" +
            "Each assist is its own switch on purpose (the Celeste assist mode lesson):\n" +
            "a player who only needs more time keeps the challenge of being hit.";

        [Title("Before a build")]
        [ButtonGroup("Build"), Button("Validate World", ButtonSizes.Large), GUIColor(0.6f, 0.85f, 0.6f)]
        private void Validate() => EditorApplication.ExecuteMenuItem("Weizza/Validate World");

        [ButtonGroup("Build"), Button("Fix What It Can", ButtonSizes.Large)]
        private void Fix() => EditorApplication.ExecuteMenuItem("Weizza/Validate World (Fix What It Can)");

        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string BuildNote =>
            "Checks chunk scenes are in the build, shaders looked up by name ship, swaying art has its helper,\n" +
            "enemies are on the Enemy layer, no missing scripts. Errors also stop a build with the same report.";

        [Title("Feel and playtest tools")]
        [ButtonGroup("Feel"), Button("Feel Lab", ButtonSizes.Large)]
        private void FeelLab() => EditorApplication.ExecuteMenuItem("Weizza/Feel Lab");

        [ButtonGroup("Feel"), Button("Pacing Graph", ButtonSizes.Large)]
        private void Pacing() => EditorApplication.ExecuteMenuItem("Weizza/Playtest/Pacing Graph");

        [ButtonGroup("Feel"), Button("Death Map", ButtonSizes.Large)]
        private void Deaths() => EditorApplication.ExecuteMenuItem("Weizza/Playtest/Death Map");

        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string FeelNote =>
            "Feel Lab: every feel dial in one window (wind, plants, mist, camera lead, dust, lean, rim light).\n" +
            "Pacing Graph: how intense the level is along x, with rests marked. Click it to jump there.\n" +
            "Death Map: where players died (your own play, or a playtester's weizza_deaths.jsonl).\n" +
            "Colour blind preview: Weizza > Playtest > Colour Blind Preview. Switch it Off after.";

        [Title("Building the map")]
        [ButtonGroup("Map"), Button("Map Painter", ButtonSizes.Large)]
        private void Painter() => EditorApplication.ExecuteMenuItem("Weizza/Map Painter");

        [ButtonGroup("Map"), Button("World Map", ButtonSizes.Large)]
        private void WorldMap() => EditorApplication.ExecuteMenuItem("Weizza/World/World Map");

        [ButtonGroup("Map"), Button("Fit Rock Colliders", ButtonSizes.Large)]
        private void Outlines() => EditorApplication.ExecuteMenuItem("Weizza/Art/Fit Colliders To Outline (All Open Scenes)");

        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string MapNote =>
            "Rocks and stumps painted with Map Painter get a collider that follows their outline by themselves.\n" +
            "Flat strips stay boxes. Fit Rock Colliders redoes every piece already placed in the open scenes.";
    }
}
