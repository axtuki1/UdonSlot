#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.Editor
{
    public class ReelLightInspector : lilToon.lilToonInspector
    {
        // Custom properties
        private MaterialProperty upperColor, lowerColor, split, swap;
        private MaterialProperty symbolLights, symbolWindow;

        private static bool isShowCustomProperties;
        private const string shaderName = "UdonSlot/Reel";

        protected override void LoadCustomProperties(MaterialProperty[] props, Material material)
        {
            isCustomShader = true;

            // This reel shader supports opaque rendering without outlines only.
            ReplaceToCustomShaders();
            isShowRenderMode = false;
            upperColor = FindProperty("_ReelUpperLightColor", props);
            lowerColor = FindProperty("_ReelLowerLightColor", props);
            split = FindProperty("_ReelLightSplit", props);
            swap = FindProperty("_ReelLightSwap", props);
            symbolLights = FindProperty("_ReelSymbolLights", props);
            symbolWindow = FindProperty("_ReelSymbolWindow", props);
        }

        protected override void DrawCustomProperties(Material material)
        {
            // GUIStyles Name   Description
            // ---------------- ------------------------------------
            // boxOuter         outer box
            // boxInnerHalf     inner box
            // boxInner         inner box without label
            // customBox        box (similar to unity default box)
            // customToggleFont label for box

            isShowCustomProperties = Foldout("リール上下照明", "リール上下照明", isShowCustomProperties);
            if(isShowCustomProperties)
            {
                EditorGUILayout.BeginVertical(boxOuter);
                EditorGUILayout.LabelField(GetLoc("リール上下照明"), customToggleFont);
                EditorGUILayout.BeginVertical(boxInnerHalf);

                m_MaterialEditor.ShaderProperty(upperColor, "上側の照明色 (HDR)");
                m_MaterialEditor.ShaderProperty(lowerColor, "下側の照明色 (HDR)");
                m_MaterialEditor.ShaderProperty(split, "マスク内の上下境界");
                m_MaterialEditor.ShaderProperty(swap, "上下を入れ替える");
                EditorGUILayout.HelpBox("発光2ndのマスクを上下に分けて着色します。両方白で元の発光です。OffsetはSubBoardが補正します。", MessageType.Info);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("図柄バックライト（発光1st）", EditorStyles.boldLabel);
                EditorGUI.showMixedValue = symbolLights.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                Vector4 lights = symbolLights.vectorValue;
                lights.x = EditorGUILayout.Slider("上段の発光量", lights.x, 0f, 1f);
                lights.y = EditorGUILayout.Slider("中段の発光量", lights.y, 0f, 1f);
                lights.z = EditorGUILayout.Slider("下段の発光量", lights.z, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    m_MaterialEditor.RegisterPropertyChangeUndo("図柄の発光量");
                    symbolLights.vectorValue = lights;
                }
                EditorGUI.showMixedValue = symbolWindow.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                Vector4 window = symbolWindow.vectorValue;
                window.x = Mathf.Max(3, EditorGUILayout.IntField("1周の図柄数", Mathf.RoundToInt(window.x)));
                window.y = EditorGUILayout.FloatField("表示窓の位置（UV）", window.y);
                if (EditorGUI.EndChangeCheck())
                {
                    m_MaterialEditor.RegisterPropertyChangeUndo("図柄照明の表示窓");
                    symbolWindow.vectorValue = window;
                }
                EditorGUI.showMixedValue = false;
                EditorGUILayout.HelpBox("0で消灯、1で通常点灯。図柄バックライト（発光1st）のみに作用し、リール内の上下照明（発光2nd）は別制御です。図柄数は実行時にMainBoardから取得し、表示窓の位置はリールの回転に合わせて補正します。", MessageType.Info);

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndVertical();
            }
        }

        protected override void ReplaceToCustomShaders()
        {
            lts = Shader.Find(shaderName + "/lilToon");
        }
        [MenuItem("Assets/UdonSlot/Convert to Reel Light Shader", false, 1100)]
        private static void ConvertMaterialToCustomShaderMenu()
        {
            if(Selection.objects.Length == 0) return;
            Shader targetShader = Shader.Find(shaderName + "/lilToon");
            if (targetShader == null)
            {
                Debug.LogError("UdonSlot/Reel/lilToon が見つかりません。Shaderフォルダを再インポートしてください。");
                return;
            }
            for(int i = 0; i < Selection.objects.Length; i++)
            {
                if(Selection.objects[i] is Material)
                {
                    Material material = (Material)Selection.objects[i];
                    if (material.shader == targetShader) continue;
                    if (material.shader == null || material.shader.name != "lilToon")
                    {
                        Debug.LogWarning("リール照明への変換対象は通常の lilToon（不透明・輪郭線なし）のみです。", material);
                        continue;
                    }
                    Undo.RecordObject(material, "Convert to UdonSlot Reel Light Shader");
                    material.shader = targetShader;
                    EditorUtility.SetDirty(material);
                }
            }
        }
    }
}
#endif
