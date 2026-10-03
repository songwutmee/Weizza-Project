using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine.Rendering;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Keeps the Area Moods table in step with the map: the first chunk of a new area
    /// gets a row with its own grade, copied from the scene's grade so it starts
    /// looking like the rest of the game and is tuned from there. Called by
    /// WorldMapWindow.AppendChunk, so Map Painter's Add chunk and Make chunk from
    /// selection both cover it.
    /// </summary>
    public static class AreaMoodSetup
    {
        private const string TablePath = "Assets/Resources/World/AreaMoods.asset";
        private const string SceneGrade = "Assets/GameData/Atmosphere/SceneGrade.asset";
        private const string ZoneFolder = "Assets/GameData/Atmosphere/Zones";

        public static AreaMoodTable Table()
        {
            var table = AssetDatabase.LoadAssetAtPath<AreaMoodTable>(TablePath);
            if (table != null) return table;

            Directory.CreateDirectory(Path.GetDirectoryName(TablePath)!);
            table = UnityEngine.ScriptableObject.CreateInstance<AreaMoodTable>();
            // The first area keeps the scene's own grade and its own title cutscene.
            table.moods.Add(new AreaMoodTable.Mood { areaId = "forest" });
            AssetDatabase.CreateAsset(table, TablePath);
            return table;
        }

        public static void EnsureArea(string areaId)
        {
            if (string.IsNullOrWhiteSpace(areaId)) return;
            var table = Table();
            if (table.Find(areaId) != null) return;

            Directory.CreateDirectory(ZoneFolder);
            var gradePath = $"{ZoneFolder}/{areaId}_Grade.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(gradePath) == null && AssetDatabase.LoadAssetAtPath<VolumeProfile>(SceneGrade) != null)
                AssetDatabase.CopyAsset(SceneGrade, gradePath);

            var nice = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(areaId.Replace('_', ' '));
            Undo.RecordObject(table, "Add area mood");
            table.moods.Add(new AreaMoodTable.Mood
            {
                areaId = areaId,
                grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>(gradePath),
                titleKey = "AREA_" + areaId.ToUpperInvariant(),
                titleFallback = "The " + nice,
            });
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log($"[Area Moods] New area '{areaId}': grade at {gradePath}, title key AREA_{areaId.ToUpperInvariant()}. " +
                                  "Tune the grade so the area reads different from the last one.");
        }
    }
}
