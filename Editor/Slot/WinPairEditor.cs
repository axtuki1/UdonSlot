using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>
    /// WinPairの成立図柄を、親のReelOutputControllerが持つ図柄名から選択できるInspector。
    /// </summary>
    [CustomEditor(typeof(WinPair))]
    public class WinPairEditor : UnityEditor.Editor
    {
        private string[] symbolNames;

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("winPairName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("payout"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isReelStopOrderAny"));
            if (!serializedObject.FindProperty("isReelStopOrderAny").boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("reelStopOrder"), true);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("tags"), true);
            // シンボルはリールデータのシンボル名から取得し選択させる
            WinPair winPair = (WinPair)target;
            ReelOutputController reelOutputController = winPair.transform.parent.GetComponentInParent<ReelOutputController>();
            if (reelOutputController != null)
            {
                if (symbolNames == null)
                {
                    List<string> symbolNameList = new List<string>();
                    symbolNameList.Add("-- any --");
                    foreach (ReelData reelData in reelOutputController.reelDatas)
                    {
                        symbolNameList.AddRange(reelData.symbols);
                    }
                    symbolNames = symbolNameList.ToArray();
                }

                SerializedProperty symbolsProperty = serializedObject.FindProperty("symbols");
                for (int i = 0; i < symbolsProperty.arraySize; i++)
                {
                    SerializedProperty symbolProperty = symbolsProperty.GetArrayElementAtIndex(i);
                    int currentValue = 0;
                    string currentSymbol = symbolProperty.stringValue;
                    if (!string.IsNullOrEmpty(currentSymbol))
                    {
                        int index = System.Array.IndexOf(symbolNames, currentSymbol);
                        if (index >= 0) currentValue = index;
                    }

                    int newValue = EditorGUILayout.Popup("Symbol " + i, currentValue, symbolNames);
                    if (newValue != currentValue)
                    {
                        symbolProperty.stringValue = newValue == 0 ? "" : symbolNames[newValue];
                    }
                }
            }


            serializedObject.ApplyModifiedProperties();
        }
    }
}
