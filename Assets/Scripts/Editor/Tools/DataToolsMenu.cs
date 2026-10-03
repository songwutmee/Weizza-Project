using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Small data chores that would otherwise be done by hand and done wrong:
    /// fixing duplicated persistent ids, exporting frame data, opening or
    /// wiping save files, and filling a sound bank from a folder of clips.
    /// </summary>
    public static class DataToolsMenu
    {
        // ---- Persistent ids -----------------------------------------------------------

        [MenuItem("Weizza/Validate Persistent Ids", priority = 200)]
        private static void ValidateIds()
        {
            var seen = new Dictionary<string, PersistentId>();
            var fixedCount = 0;

            foreach (var pid in Object.FindObjectsByType<PersistentId>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (!seen.ContainsKey(pid.Id))
                {
                    seen[pid.Id] = pid;
                    continue;
                }

                // The later object in instance order is the copy. The first one
                // keeps its id, so its saved state survives.
                Undo.RecordObject(pid, "Fix duplicate persistent id");
                pid.Regenerate();
                EditorUtility.SetDirty(pid);
                fixedCount++;
                Debug.Log($"[Weizza] '{pid.name}' shared an id with '{seen[pid.Id]?.name}'. Gave it a new one.", pid);
            }

            var missing = 0;
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour is ISaveable && behaviour.GetComponent<PersistentId>() == null)
                {
                    Undo.AddComponent<PersistentId>(behaviour.gameObject);
                    missing++;
                }
            }

            Debug.Log($"[Weizza] Persistent ids checked. {fixedCount} duplicates fixed, {missing} saveable objects given an id.");
        }

        // ---- Frame data ------------------------------------------------------------------

        [MenuItem("Weizza/Export Frame Data CSV", priority = 201)]
        private static void ExportFrameData()
        {
            var csv = new StringBuilder("Asset,Name,Startup,Active,Recovery,Total,Damage,Poise,HitStop,HitStun,KnockX,KnockY,DPS,SuperArmor,Launcher\n");
            foreach (var guid in AssetDatabase.FindAssets("t:AttackDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<AttackDefinition>(path);
                if (a == null) continue;
                var dps = a.TotalSeconds > 0f ? a.damage / a.TotalSeconds : 0f;
                csv.AppendLine(string.Join(",", Path.GetFileNameWithoutExtension(path), a.displayName,
                    a.startupFrames, a.activeFrames, a.recoveryFrames, a.TotalFrames, a.damage, a.poiseDamage,
                    a.hitStopFrames, a.hitStunFrames, a.knockbackX, a.knockbackY, dps.ToString("0.0"),
                    a.superArmor, a.launcher));
            }

            var file = EditorUtility.SaveFilePanel("Export frame data", "", "weizza_frame_data.csv", "csv");
            if (string.IsNullOrEmpty(file)) return;
            File.WriteAllText(file, csv.ToString());
            EditorUtility.RevealInFinder(file);
        }

        // ---- Saves --------------------------------------------------------------------------

        [MenuItem("Weizza/Saves/Open Save Folder", priority = 210)]
        private static void OpenSaves() => EditorUtility.RevealInFinder(Application.persistentDataPath + "/");

        [MenuItem("Weizza/Saves/Print Slot 1", priority = 211)]
        private static void PrintSlot()
        {
            var path = SaveSystem.PathFor(0);
            Debug.Log(File.Exists(path) ? File.ReadAllText(path) : "[Weizza] Slot 1 is empty.");
        }

        [MenuItem("Weizza/Saves/Delete All Slots", priority = 212)]
        private static void DeleteSaves()
        {
            if (!EditorUtility.DisplayDialog("Delete saves", "Delete all three save slots?", "Delete", "Cancel")) return;
            for (var i = 0; i < 3; i++)
            {
                var path = SaveSystem.PathFor(i);
                if (File.Exists(path)) File.Delete(path);
            }
            Debug.Log("[Weizza] Save slots deleted.");
        }

        // ---- Sound banks ------------------------------------------------------------------

        /// <summary>
        /// Adds every audio clip in the selected folder to the selected bank,
        /// one entry per clip named after the file. Clips named step_1, step_2
        /// become one entry "step" with both, so variation needs no extra work.
        /// </summary>
        [MenuItem("Weizza/Audio/Fill Selected Bank From Folder", priority = 220)]
        private static void FillBank()
        {
            var bank = Selection.activeObject as SoundBank;
            if (bank == null)
            {
                Debug.LogWarning("[Weizza] Select a Sound Bank asset first.");
                return;
            }

            var folder = EditorUtility.OpenFolderPanel("Folder of clips", "Assets", "");
            if (string.IsNullOrEmpty(folder)) return;
            var relative = "Assets" + folder.Substring(Application.dataPath.Length);

            var grouped = new Dictionary<string, List<AudioClip>>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { relative }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                var id = StripVariant(clip.name).ToLowerInvariant().Replace('_', '.').Replace(' ', '.');
                if (!grouped.TryGetValue(id, out var list)) grouped[id] = list = new List<AudioClip>();
                list.Add(clip);
            }

            Undo.RecordObject(bank, "Fill sound bank");
            var added = 0;
            foreach (var pair in grouped)
            {
                var entry = bank.Find(pair.Key);
                if (entry == null)
                {
                    entry = new SoundEntry { id = pair.Key };
                    bank.entries.Add(entry);
                    added++;
                }
                entry.clips = pair.Value.ToArray();
            }

            EditorUtility.SetDirty(bank);
            Debug.Log($"[Weizza] {bank.name}: {added} new entries, {grouped.Count} filled from {relative}.", bank);
        }

        [MenuItem("Weizza/Audio/Create Starter Bank", priority = 221)]
        private static void StarterBank()
        {
            const string folder = "Assets/Resources/Audio";
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources", "Audio");

            var path = folder + "/SFX_Weizza.asset";
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(path);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, path);
            }

            // Every id the code already asks for, empty, so the list of sounds
            // still to make is the bank itself.
            var ids = new[]
            {
                "hit.light", "hit.heavy", "hit.armored", "hit.ignored", "player.hurt", "shot.hit", "enemy.kill",
                "move.dash", "move.walljump", "shot.fire", "aim.slow.in", "aim.slow.out", "stance.switch",
                "ui.denied", "stance.learned", "player.death", "player.revive", "player.heal", "world.checkpoint",
                "ui.save", "boss.roar", "boss.phase", "world.shrine", "step", "land",
                "bgm.forest", "bgm.combat", "bgm.boss", "amb.forest",
            };

            foreach (var id in ids)
            {
                if (bank.Find(id) != null) continue;
                var entry = new SoundEntry { id = id };
                if (id.StartsWith("bgm.")) { entry.category = SoundCategory.Music; entry.loop = true; entry.cooldown = 0f; }
                else if (id.StartsWith("amb.")) { entry.category = SoundCategory.Ambience; entry.loop = true; entry.cooldown = 0f; }
                else if (id.StartsWith("ui.")) entry.category = SoundCategory.UI;
                bank.entries.Add(entry);
            }

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Selection.activeObject = bank;
            Debug.Log($"[Weizza] Starter bank at {path} with every id the game asks for. Drop clips into it.", bank);
        }

        private static string StripVariant(string name)
        {
            var end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1])) end--;
            while (end > 0 && (name[end - 1] == '_' || name[end - 1] == '-' || name[end - 1] == ' ')) end--;
            return end > 0 ? name.Substring(0, end) : name;
        }
    }
}
