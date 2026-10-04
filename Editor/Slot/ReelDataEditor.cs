
using AX.UdonSlot.REV1_0.Serialize;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>
    /// ReelDataの図柄配列を編集し、ReelArrayから図柄名を取り込むInspector。
    /// </summary>
    [CustomEditor(typeof(ReelData))]
    public class ReelDataEditor : UnityEditor.Editor
    {
        private ReelArray reelArray;
        
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("symbols"));
            
            ReelData reelData = (ReelData)target;
            
            reelArray = EditorGUILayout.ObjectField("Reel Array", reelArray, typeof(ReelArray), false) as ReelArray;

            if (GUILayout.Button("ReelArrayからデータを生成"))
            {
                Undo.RecordObject(reelData, "Generate ReelData from ReelArray");
                if (reelArray != null)
                {
                    int reelCount = reelArray.reel.Length;
                    string[] symbols = new string[reelCount];
                    for (int i = 0; i < reelCount; i++)
                    {
                        symbols[i] = reelArray.reel[i].pictureName;
                    }
                    serializedObject.FindProperty("symbols").ClearArray();
                    serializedObject.FindProperty("symbols").arraySize = reelCount;
                    for (int i = 0; i < reelCount; i++)                    {
                        serializedObject.FindProperty("symbols").GetArrayElementAtIndex(i).stringValue = symbols[i];
                    }
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
    }

}

