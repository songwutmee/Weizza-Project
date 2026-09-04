using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>Right-click > Create > StoryKit > Story Script: new .story
    /// file pre-filled with a template so nobody starts from a blank page.</summary>
    public static class CreateStoryFileMenu
    {
        private const string Template =
@"// StoryScript: save this file and Unity compiles it into a Flow Graph.
// Drag the imported asset into a FlowRunner or a Story Graph Trigger.
//
// [speaker]: text            dialogue line
// * choice text {guard}      choice (indent the lines under it)
// -> section  /  -> END      jump
// << command args >>         var.set / var.add / event / quest.start /
//                            give / wait / bgm / fade / save / call / bark

=== start ===
[npc]: Hello traveler!
[npc]: Nice weather today, isn't it?

* Sure is!
    [player]: Sure is!
    << var.add npc_mood 1 >>
    -> friendly
* Leave silently
    -> END

=== friendly ===
[npc]: You seem like good folk. Take this.
<< give apple 1 >>
-> END
";

        [MenuItem("Assets/Create/StoryKit/Story Script (.story)", priority = 82)]
        public static void CreateStoryFile()
        {
            ProjectWindowUtil.CreateAssetWithContent(
                "NewStory.story", Template,
                EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D);
        }
    }
}
