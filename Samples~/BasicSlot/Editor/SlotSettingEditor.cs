using System.Collections;
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>
    /// SlotSettingの抽選範囲を編集し、各役のおおよその確率をInspectorへ表示する。
    /// </summary>
    [CustomEditor(typeof(SlotSetting))]
    public class SlotSettingEditor : UnityEditor.Editor
    {
        List<string> lotteryPropertyNames = new List<string>();
        List<int> lotteryProbabilities = new List<int>();

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            lotteryPropertyNames.Clear();
            lotteryProbabilities.Clear();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("settingName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lotteryMaxRange"));
            ViewLotteryRow(serializedObject.FindProperty("replayRange"));
            ViewLotteryRow(serializedObject.FindProperty("blueberryRange"));
            ViewLotteryRow(serializedObject.FindProperty("bellRange"));
            ViewLotteryRow(serializedObject.FindProperty("cherryRange"));
            ViewLotteryRow(serializedObject.FindProperty("centerCherryBigBounsRange"));
            ViewLotteryRow(serializedObject.FindProperty("centerCherryRegularBounsRange"));
            ViewLotteryRow(serializedObject.FindProperty("duplicateCherryBigBounsRange"));
            ViewLotteryRow(serializedObject.FindProperty("duplicateCherryRegularBounsRange"));
            ViewLotteryRow(serializedObject.FindProperty("bigBonusRange"));
            ViewLotteryRow(serializedObject.FindProperty("regularBonusRange"));
            ViewLotteryRow(serializedObject.FindProperty("noneRange"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();

            // 小さい順にソートする
            for (int i = 0; i < lotteryProbabilities.Count - 1; i++)
            {
                for (int j = 0; j < lotteryProbabilities.Count - i - 1; j++)
                {
                    if (lotteryProbabilities[j] > lotteryProbabilities[j + 1])
                    {
                        // swap probabilities
                        (lotteryProbabilities[j], lotteryProbabilities[j + 1]) =
                            (lotteryProbabilities[j + 1], lotteryProbabilities[j]);

                        // swap names
                        (lotteryPropertyNames[j], lotteryPropertyNames[j + 1]) =
                            (lotteryPropertyNames[j + 1], lotteryPropertyNames[j]);
                    }
                }
            }

            // ソートした結果を表示する
            int totalRange = serializedObject.FindProperty("lotteryMaxRange").intValue;
            EditorGUILayout.LabelField("Lottery Probabilities (Sorted)");
            for (int i = 0; i < lotteryPropertyNames.Count; i++)
            {
                if(lotteryProbabilities[i] < 0) continue; // 負の値は無効とみなす
                int nextPoint = i < lotteryProbabilities.Count - 1 ? lotteryProbabilities[i + 1] : totalRange;
                int range = nextPoint - lotteryProbabilities[i] + 1;
                EditorGUILayout.LabelField($"{lotteryPropertyNames[i]}: {range} : 1/{1 / ((float)range / totalRange)}");
            }
        }

        public void ViewLotteryRow(SerializedProperty targetProperty)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(targetProperty);
            }

            if (lotteryPropertyNames.IndexOf(targetProperty.displayName) == -1)
            {
                lotteryPropertyNames.Add(targetProperty.displayName);
                lotteryProbabilities.Add(targetProperty.intValue);
            }
        }
    }
}
