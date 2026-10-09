using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Shows the monsters in the Scene view while editing.
    ///
    /// Two reasons they were invisible: a character's sprite is set by its
    /// SpriteAnimator when Play starts, so a placed enemy has an empty renderer
    /// in Edit mode, and an Encounter Room's enemies are pooled and only exist in
    /// Play. Both are drawn here as a faded first idle frame with a name tag.
    /// Nothing is written into the scene, it is only a drawing on top of the
    /// Scene view, so it can never end up in a build.
    /// </summary>
    [InitializeOnLoad]
    public static class EncounterPreview
    {
        private const float PlacedAlpha = 0.9f;
        private const float WaveAlpha = 0.5f;

        static EncounterPreview()
        {
            SceneView.duringSceneGui += Draw;
        }

        private static void Draw(SceneView view)
        {
            if (Application.isPlaying || Event.current.type != EventType.Repaint) return;
            var svm = SceneVisibilityManager.instance;

            Handles.BeginGUI();

            foreach (var animator in Object.FindObjectsByType<SpriteAnimator>(FindObjectsSortMode.None))
            {
                var sr = animator.Target;
                if (sr == null || sr.sprite != null || !sr.enabled || svm.IsHidden(animator.gameObject)) continue;
                var frame = animator.FirstIdleFrame;
                if (frame == null) continue;
                var owner = animator.GetComponentInParent<EnemyController>();
                var name = owner != null ? owner.name : animator.name;
                DrawSprite(frame, sr.transform.position, sr.transform.lossyScale, sr.flipX, PlacedAlpha);
                DrawTag(owner != null ? owner.transform.position : sr.transform.position, name);
            }

            foreach (var room in Object.FindObjectsByType<EncounterRoom>(FindObjectsSortMode.None))
            {
                if (svm.IsHidden(room.gameObject)) continue;
                for (var w = 0; w < room.Waves.Count; w++)
                {
                    var wave = room.Waves[w];
                    if (wave == null) continue;
                    foreach (var spawn in wave.spawns)
                    {
                        if (spawn?.prefab == null) continue;
                        var at = spawn.point != null ? spawn.point.position : room.transform.position;
                        DrawPrefab(spawn.prefab, at);
                        DrawTag(at, $"{spawn.prefab.name}  wave {w + 1}");
                    }
                }
            }

            Handles.EndGUI();
        }

        private static void DrawPrefab(GameObject prefab, Vector3 at)
        {
            var animator = prefab.GetComponentInChildren<SpriteAnimator>(true);
            var sr = animator != null ? animator.Target : prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null) return;
            var sprite = sr.sprite != null ? sr.sprite : animator != null ? animator.FirstIdleFrame : null;
            if (sprite == null) return;
            var offset = sr.transform.position - prefab.transform.position;
            DrawSprite(sprite, at + offset, sr.transform.lossyScale, sr.flipX, WaveAlpha);
        }

        private static void DrawSprite(Sprite sprite, Vector3 pivot, Vector3 scale, bool flip, float alpha)
        {
            var b = sprite.bounds;
            var min = pivot + Vector3.Scale(b.min, scale);
            var max = pivot + Vector3.Scale(b.max, scale);
            var a = HandleUtility.WorldToGUIPoint(new Vector3(min.x, max.y, pivot.z));
            var c = HandleUtility.WorldToGUIPoint(new Vector3(max.x, min.y, pivot.z));
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            var tex = sprite.texture;
            var tr = sprite.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            if (flip || scale.x < 0f) { uv.x += uv.width; uv.width = -uv.width; }

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(rect, tex, uv, true);
            GUI.color = old;
        }

        private static void DrawTag(Vector3 at, string label)
        {
            var foot = HandleUtility.WorldToGUIPoint(at);
            var style = EditorStyles.miniBoldLabel;
            var size = style.CalcSize(new GUIContent(label));
            var box = new Rect(foot.x - size.x * 0.5f - 3f, foot.y + 4f, size.x + 6f, size.y);
            EditorGUI.DrawRect(box, new Color(0.15f, 0.08f, 0.05f, 0.75f));
            var old = GUI.contentColor;
            GUI.contentColor = new Color(1f, 0.7f, 0.5f);
            GUI.Label(box, label, style);
            GUI.contentColor = old;
        }
    }
}
