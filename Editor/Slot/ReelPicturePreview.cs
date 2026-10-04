using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>Use original image proportions even when texture import resizes NPOT images.</summary>
    [InitializeOnLoad]
    internal static class ReelPicturePreview
    {
        private static readonly Dictionary<Texture2D, float> Aspects = new Dictionary<Texture2D, float>();

        static ReelPicturePreview() { EditorApplication.projectChanged += Aspects.Clear; }

        internal static void Draw(Rect rect, Texture2D texture)
        {
            if (texture == null) return;
            float aspect;
            if (!Aspects.TryGetValue(texture, out aspect))
            {
                aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                if (importer != null)
                {
                    int width, height;
                    importer.GetSourceTextureWidthAndHeight(out width, out height);
                    if (width > 0 && height > 0) aspect = (float)width / height;
                }
                Aspects[texture] = aspect;
            }
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true, aspect);
        }
    }
}
