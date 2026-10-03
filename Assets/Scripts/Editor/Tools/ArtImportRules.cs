using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Sets the right import settings the first time an artist drops a picture into
    /// the Art folders, so nobody has to remember them (M6.4). Every number is one
    /// this project already learned the hard way (the project notes, Mossy import settings):
    ///
    /// - World art: Sprite, 150 pixels per unit, Bilinear, up to 4096 px. A big sheet
    ///   squeezed to 2048 makes sprite.rect and sprite.vertices disagree by the ratio.
    /// - Characters: Sprite, 250 pixels per unit, up to 8192 px.
    /// - UI: Sprite, 100 pixels per unit.
    ///
    /// Only on first import. Anything changed by hand afterwards is left alone.
    /// </summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        private const string World = "Assets/Art/World/";
        private const string Characters = "Assets/Art/Characters/";
        private const string Ui = "Assets/Art/UI/";

        private void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;
            if (!importer.importSettingsMissing) return;

            var path = assetPath.Replace('\\', '/');
            float ppu;
            int maxSize;
            if (path.StartsWith(World)) { ppu = 150f; maxSize = 4096; }
            else if (path.StartsWith(Characters)) { ppu = 250f; maxSize = 8192; }
            else if (path.StartsWith(Ui)) { ppu = 100f; maxSize = 2048; }
            else return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            Debug.Log($"[Art Import] {path}: Sprite, {ppu} px per unit, Bilinear, max {maxSize}.");
        }
    }
}
