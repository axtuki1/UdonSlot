using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// LotteryTableの各フラグ、抽選比率、複数の当選役をまとめて編集するInspector。
    /// </summary>
    [CustomEditor(typeof(LotteryTable))]
    public class LotteryTableEditor : UnityEditor.Editor
    {
        private bool[] flagFoldouts = new bool[0];

        private static bool HasBonusEntries(LotteryTable table)
        {
            if (table.entryBonusNames == null) return false;
            foreach (string name in table.entryBonusNames) if (!string.IsNullOrEmpty(name)) return true;
            return false;
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            if (GUILayout.Button("機種エディタで開く")) AX.UdonSlot.REV1_0.Editor.ReelRule.ReelRuleEditorWindow.OpenFor(target);
            var table = (LotteryTable)target;
            // 設定やボーナス付きの行は機種エディタでだけ編集する（配列の並びが崩れないように）
            if (table.GetSettingCount() > 1 || HasBonusEntries(table)) return;

            serializedObject.Update();

            SerializedProperty flagNames = serializedObject.FindProperty("winpairNames");
            SerializedProperty displayNames = serializedObject.FindProperty("flagDisplayNames");
            SerializedProperty ratiosBet1 = serializedObject.FindProperty("winpairProbabilitiesBet1");
            SerializedProperty ratiosBet2 = serializedObject.FindProperty("winpairProbabilitiesBet2");
            SerializedProperty ratiosBet3 = serializedObject.FindProperty("winpairProbabilities");
            SerializedProperty denominator = serializedObject.FindProperty("maximumProbabilityDenominator");
            SerializedProperty remainderFlags = serializedObject.FindProperty("isRemainderFlags");
            SerializedProperty roleStarts = serializedObject.FindProperty("winningRoleStartIndexes");
            SerializedProperty roleCounts = serializedObject.FindProperty("winningRoleCounts");
            SerializedProperty roleNames = serializedObject.FindProperty("winningRoleNames");

            NormalizeArrays(
                flagNames,
                displayNames,
                ratiosBet1,
                ratiosBet2,
                ratiosBet3,
                remainderFlags,
                roleStarts,
                roleCounts,
                roleNames
            );
            ResizeFoldouts(flagNames.arraySize);

            denominator.intValue = Mathf.Max(1, EditorGUILayout.IntField(
                new GUIContent("最大確率分母"),
                denominator.intValue
            ));
            EditorGUILayout.Space();

            for (int flagIndex = 0; flagIndex < flagNames.arraySize; flagIndex++)
            {
                string internalName = flagNames.GetArrayElementAtIndex(flagIndex).stringValue;
                string displayName = displayNames.GetArrayElementAtIndex(flagIndex).stringValue;
                string foldoutTitle = GetFoldoutTitle(displayName, internalName, flagIndex);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                flagFoldouts[flagIndex] = EditorGUILayout.Foldout(flagFoldouts[flagIndex], foldoutTitle, true);
                if (GUILayout.Button("削除", GUILayout.Width(48f)))
                {
                    DeleteFlag(
                        flagIndex,
                        flagNames,
                        displayNames,
                        ratiosBet1,
                        ratiosBet2,
                        ratiosBet3,
                        remainderFlags,
                        roleStarts,
                        roleCounts,
                        roleNames
                    );
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                if (flagFoldouts[flagIndex])
                {
                    EditorGUILayout.PropertyField(
                        displayNames.GetArrayElementAtIndex(flagIndex),
                        new GUIContent("表示名")
                    );
                    EditorGUILayout.PropertyField(
                        flagNames.GetArrayElementAtIndex(flagIndex),
                        new GUIContent("内部使用名")
                    );
                    if (string.IsNullOrEmpty(flagNames.GetArrayElementAtIndex(flagIndex).stringValue))
                    {
                        EditorGUILayout.HelpBox("内部使用名を入力してください。", MessageType.Error);
                    }
                    SerializedProperty remainderProperty = remainderFlags.GetArrayElementAtIndex(flagIndex);
                    bool wasRemainder = remainderProperty.boolValue;
                    bool isRemainder = EditorGUILayout.Toggle(
                        new GUIContent("ハズレ（余り）"),
                        wasRemainder
                    );
                    if (isRemainder != wasRemainder)
                    {
                        SetRemainderFlag(flagIndex, isRemainder, remainderFlags);
                    }

                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField("BET別抽選", EditorStyles.boldLabel);
                    DrawBetProbabilityRow(
                        "BET 1",
                        flagIndex,
                        isRemainder,
                        denominator.intValue,
                        ratiosBet1,
                        remainderFlags
                    );
                    DrawBetProbabilityRow(
                        "BET 2",
                        flagIndex,
                        isRemainder,
                        denominator.intValue,
                        ratiosBet2,
                        remainderFlags
                    );
                    DrawBetProbabilityRow(
                        "BET 3",
                        flagIndex,
                        isRemainder,
                        denominator.intValue,
                        ratiosBet3,
                        remainderFlags
                    );

                    if (HasDuplicateFlagName(flagIndex, flagNames))
                    {
                        EditorGUILayout.HelpBox("同じ内部使用名が複数登録されています。", MessageType.Error);
                    }

                    EditorGUILayout.LabelField("当選役", EditorStyles.boldLabel);
                    int roleStart = roleStarts.GetArrayElementAtIndex(flagIndex).intValue;
                    int roleCount = roleCounts.GetArrayElementAtIndex(flagIndex).intValue;
                    if (isRemainder && roleCount > 0)
                    {
                        EditorGUILayout.HelpBox("ハズレフラグには通常、当選役を設定しません。", MessageType.Warning);
                    }
                    bool roleDeleted = false;

                    for (int localRoleIndex = 0; localRoleIndex < roleCount; localRoleIndex++)
                    {
                        int roleIndex = roleStart + localRoleIndex;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PropertyField(
                            roleNames.GetArrayElementAtIndex(roleIndex),
                            GUIContent.none
                        );
                        if (GUILayout.Button("削除", GUILayout.Width(48f)))
                        {
                            DeleteRole(flagIndex, roleIndex, roleStarts, roleCounts, roleNames);
                            roleDeleted = true;
                        }
                        EditorGUILayout.EndHorizontal();

                        if (roleDeleted) break;

                        string roleName = roleNames.GetArrayElementAtIndex(roleIndex).stringValue;
                        if (string.IsNullOrEmpty(roleName))
                        {
                            EditorGUILayout.HelpBox("PayoutTableの成立役名を入力してください。", MessageType.Warning);
                        }
                        else if (HasDuplicateRole(roleStart, roleCount, roleIndex, roleNames))
                        {
                            EditorGUILayout.HelpBox("同じ当選役がこのフラグ内に重複しています。", MessageType.Warning);
                        }
                    }

                    if (GUILayout.Button("当選役を追加"))
                    {
                        InsertRole(flagIndex, roleStarts, roleCounts, roleNames);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("フラグを追加"))
            {
                AddFlag(
                    flagNames,
                    displayNames,
                    ratiosBet1,
                    ratiosBet2,
                    ratiosBet3,
                    remainderFlags,
                    roleStarts,
                    roleCounts,
                    roleNames
                );
            }

            if (HasRemainderFlag(remainderFlags))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("BET別割当", EditorStyles.boldLabel);
                DrawAllocationSummary("BET 1", denominator.intValue, ratiosBet1, remainderFlags);
                DrawAllocationSummary("BET 2", denominator.intValue, ratiosBet2, remainderFlags);
                DrawAllocationSummary("BET 3", denominator.intValue, ratiosBet3, remainderFlags);
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "ハズレ（余り）が未指定のため、既存互換の相対比率抽選として動作します。",
                    MessageType.Info
                );
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void NormalizeArrays(
            SerializedProperty flagNames,
            SerializedProperty displayNames,
            SerializedProperty ratiosBet1,
            SerializedProperty ratiosBet2,
            SerializedProperty ratiosBet3,
            SerializedProperty remainderFlags,
            SerializedProperty roleStarts,
            SerializedProperty roleCounts,
            SerializedProperty roleNames)
        {
            int flagCount = flagNames.arraySize;
            displayNames.arraySize = flagCount;
            ResizeRatioArray(ratiosBet1, flagCount);
            ResizeRatioArray(ratiosBet2, flagCount);
            ResizeRatioArray(ratiosBet3, flagCount);
            remainderFlags.arraySize = flagCount;
            roleStarts.arraySize = flagCount;
            roleCounts.arraySize = flagCount;

            bool foundRemainder = false;
            for (int flagIndex = 0; flagIndex < flagCount; flagIndex++)
            {
                SerializedProperty remainderProperty = remainderFlags.GetArrayElementAtIndex(flagIndex);
                if (!remainderProperty.boolValue) continue;
                if (foundRemainder) remainderProperty.boolValue = false;
                else foundRemainder = true;
            }

            int nextRoleStart = 0;
            for (int flagIndex = 0; flagIndex < flagCount; flagIndex++)
            {
                SerializedProperty countProperty = roleCounts.GetArrayElementAtIndex(flagIndex);
                int roleCount = Mathf.Max(0, countProperty.intValue);
                int remainingRoles = roleNames.arraySize - nextRoleStart;
                if (roleCount > remainingRoles) roleCount = Mathf.Max(0, remainingRoles);

                roleStarts.GetArrayElementAtIndex(flagIndex).intValue = nextRoleStart;
                countProperty.intValue = roleCount;
                nextRoleStart += roleCount;
            }

            roleNames.arraySize = nextRoleStart;
        }

        private void AddFlag(
            SerializedProperty flagNames,
            SerializedProperty displayNames,
            SerializedProperty ratiosBet1,
            SerializedProperty ratiosBet2,
            SerializedProperty ratiosBet3,
            SerializedProperty remainderFlags,
            SerializedProperty roleStarts,
            SerializedProperty roleCounts,
            SerializedProperty roleNames)
        {
            int flagIndex = flagNames.arraySize;
            flagNames.arraySize++;
            displayNames.arraySize++;
            ratiosBet1.arraySize++;
            ratiosBet2.arraySize++;
            ratiosBet3.arraySize++;
            remainderFlags.arraySize++;
            roleStarts.arraySize++;
            roleCounts.arraySize++;

            flagNames.GetArrayElementAtIndex(flagIndex).stringValue = "NewFlag";
            displayNames.GetArrayElementAtIndex(flagIndex).stringValue = "新しいフラグ";
            ratiosBet1.GetArrayElementAtIndex(flagIndex).floatValue = 0f;
            ratiosBet2.GetArrayElementAtIndex(flagIndex).floatValue = 0f;
            ratiosBet3.GetArrayElementAtIndex(flagIndex).floatValue = 0f;
            remainderFlags.GetArrayElementAtIndex(flagIndex).boolValue = false;
            roleStarts.GetArrayElementAtIndex(flagIndex).intValue = roleNames.arraySize;
            roleCounts.GetArrayElementAtIndex(flagIndex).intValue = 0;

            ResizeFoldouts(flagNames.arraySize);
            flagFoldouts[flagIndex] = true;
        }

        private void DeleteFlag(
            int flagIndex,
            SerializedProperty flagNames,
            SerializedProperty displayNames,
            SerializedProperty ratiosBet1,
            SerializedProperty ratiosBet2,
            SerializedProperty ratiosBet3,
            SerializedProperty remainderFlags,
            SerializedProperty roleStarts,
            SerializedProperty roleCounts,
            SerializedProperty roleNames)
        {
            int roleStart = roleStarts.GetArrayElementAtIndex(flagIndex).intValue;
            int roleCount = roleCounts.GetArrayElementAtIndex(flagIndex).intValue;
            for (int i = 0; i < roleCount; i++)
            {
                roleNames.DeleteArrayElementAtIndex(roleStart);
            }

            flagNames.DeleteArrayElementAtIndex(flagIndex);
            displayNames.DeleteArrayElementAtIndex(flagIndex);
            ratiosBet1.DeleteArrayElementAtIndex(flagIndex);
            ratiosBet2.DeleteArrayElementAtIndex(flagIndex);
            ratiosBet3.DeleteArrayElementAtIndex(flagIndex);
            remainderFlags.DeleteArrayElementAtIndex(flagIndex);
            roleStarts.DeleteArrayElementAtIndex(flagIndex);
            roleCounts.DeleteArrayElementAtIndex(flagIndex);
            ResizeFoldouts(flagNames.arraySize);
        }

        private void InsertRole(
            int flagIndex,
            SerializedProperty roleStarts,
            SerializedProperty roleCounts,
            SerializedProperty roleNames)
        {
            int roleStart = roleStarts.GetArrayElementAtIndex(flagIndex).intValue;
            int roleCount = roleCounts.GetArrayElementAtIndex(flagIndex).intValue;
            int insertIndex = roleStart + roleCount;

            roleNames.arraySize++;
            for (int i = roleNames.arraySize - 1; i > insertIndex; i--)
            {
                roleNames.GetArrayElementAtIndex(i).stringValue =
                    roleNames.GetArrayElementAtIndex(i - 1).stringValue;
            }
            roleNames.GetArrayElementAtIndex(insertIndex).stringValue = "";
            roleCounts.GetArrayElementAtIndex(flagIndex).intValue = roleCount + 1;

            for (int i = flagIndex + 1; i < roleStarts.arraySize; i++)
            {
                roleStarts.GetArrayElementAtIndex(i).intValue++;
            }
        }

        private void DeleteRole(
            int flagIndex,
            int roleIndex,
            SerializedProperty roleStarts,
            SerializedProperty roleCounts,
            SerializedProperty roleNames)
        {
            roleNames.DeleteArrayElementAtIndex(roleIndex);
            roleCounts.GetArrayElementAtIndex(flagIndex).intValue--;

            for (int i = flagIndex + 1; i < roleStarts.arraySize; i++)
            {
                roleStarts.GetArrayElementAtIndex(i).intValue--;
            }
        }

        private string GetFoldoutTitle(
            string displayName,
            string internalName,
            int flagIndex)
        {
            if (!string.IsNullOrEmpty(displayName))
            {
                if (!string.IsNullOrEmpty(internalName))
                {
                    return displayName + " (" + internalName + ")";
                }

                return displayName;
            }

            if (!string.IsNullOrEmpty(internalName)) return internalName;
            return "フラグ " + (flagIndex + 1);
        }

        private bool HasDuplicateFlagName(int targetIndex, SerializedProperty flagNames)
        {
            string targetName = flagNames.GetArrayElementAtIndex(targetIndex).stringValue;
            if (string.IsNullOrEmpty(targetName)) return false;

            for (int i = 0; i < flagNames.arraySize; i++)
            {
                if (i == targetIndex) continue;
                if (flagNames.GetArrayElementAtIndex(i).stringValue == targetName) return true;
            }

            return false;
        }

        private bool HasDuplicateRole(
            int roleStart,
            int roleCount,
            int targetRoleIndex,
            SerializedProperty roleNames)
        {
            string targetName = roleNames.GetArrayElementAtIndex(targetRoleIndex).stringValue;
            for (int i = roleStart; i < roleStart + roleCount; i++)
            {
                if (i == targetRoleIndex) continue;
                if (roleNames.GetArrayElementAtIndex(i).stringValue == targetName) return true;
            }

            return false;
        }

        private void SetRemainderFlag(
            int targetIndex,
            bool enabled,
            SerializedProperty remainderFlags)
        {
            for (int i = 0; i < remainderFlags.arraySize; i++)
            {
                remainderFlags.GetArrayElementAtIndex(i).boolValue = enabled && i == targetIndex;
            }
        }

        private bool HasRemainderFlag(SerializedProperty remainderFlags)
        {
            for (int i = 0; i < remainderFlags.arraySize; i++)
            {
                if (remainderFlags.GetArrayElementAtIndex(i).boolValue) return true;
            }

            return false;
        }

        /// <summary>1つのフラグについて、指定BETの比率と実確率を表示する。</summary>
        private void DrawBetProbabilityRow(
            string betLabel,
            int flagIndex,
            bool isRemainder,
            int denominator,
            SerializedProperty ratios,
            SerializedProperty remainderFlags)
        {
            float assignedRatio;
            if (isRemainder)
            {
                assignedRatio = GetRemainderRatio(denominator, ratios, remainderFlags);
                EditorGUILayout.LabelField(
                    betLabel + " 自動割当",
                    assignedRatio.ToString("0.######")
                );
            }
            else
            {
                SerializedProperty ratioProperty = ratios.GetArrayElementAtIndex(flagIndex);
                ratioProperty.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent(betLabel + " 抽選比率"),
                    ratioProperty.floatValue
                ));
                assignedRatio = HasRemainderFlag(remainderFlags)
                    ? GetEffectiveAssignedRatio(flagIndex, denominator, ratios, remainderFlags)
                    : ratioProperty.floatValue;
            }

            float probabilityBase = HasRemainderFlag(remainderFlags)
                ? denominator
                : GetPositiveRatioTotal(ratios, null);
            EditorGUILayout.LabelField(
                betLabel + " 実確率",
                FormatProbability(assignedRatio, probabilityBase)
            );
        }

        /// <summary>指定BETの通常フラグ合計と、ハズレへ回る余りを表示する。</summary>
        private void DrawAllocationSummary(
            string betLabel,
            int denominator,
            SerializedProperty ratios,
            SerializedProperty remainderFlags)
        {
            float configuredTotal = GetPositiveRatioTotal(ratios, remainderFlags);
            float remainderRatio = Mathf.Max(0f, denominator - configuredTotal);
            EditorGUILayout.LabelField(
                betLabel + " 割当済み / 最大分母",
                configuredTotal.ToString("0.######") + " / " + denominator
            );
            EditorGUILayout.LabelField(
                betLabel + " ハズレ割当",
                remainderRatio.ToString("0.######")
            );
            if (configuredTotal > denominator)
            {
                EditorGUILayout.HelpBox(
                    betLabel + "の通常フラグ抽選比率が最大確率分母を超えています。後ろのフラグから抽選領域が失われます。",
                    MessageType.Error
                );
            }
        }

        /// <summary>追加された抽選比率を0で初期化しながら、フラグ数へ揃える。</summary>
        private void ResizeRatioArray(SerializedProperty ratios, int size)
        {
            int previousSize = ratios.arraySize;
            ratios.arraySize = size;
            for (int i = previousSize; i < size; i++)
            {
                ratios.GetArrayElementAtIndex(i).floatValue = 0f;
            }
        }

        private float GetRemainderRatio(
            int denominator,
            SerializedProperty ratios,
            SerializedProperty remainderFlags)
        {
            return Mathf.Max(0f, denominator - GetPositiveRatioTotal(ratios, remainderFlags));
        }

        private float GetEffectiveAssignedRatio(
            int targetIndex,
            int denominator,
            SerializedProperty ratios,
            SerializedProperty remainderFlags)
        {
            float assignedBeforeTarget = 0f;
            for (int i = 0; i < targetIndex; i++)
            {
                if (remainderFlags.GetArrayElementAtIndex(i).boolValue) continue;
                assignedBeforeTarget += Mathf.Max(0f, ratios.GetArrayElementAtIndex(i).floatValue);
                if (assignedBeforeTarget >= denominator) return 0f;
            }

            float availableRatio = Mathf.Max(0f, denominator - assignedBeforeTarget);
            float configuredRatio = Mathf.Max(0f, ratios.GetArrayElementAtIndex(targetIndex).floatValue);
            return Mathf.Min(configuredRatio, availableRatio);
        }

        private float GetPositiveRatioTotal(
            SerializedProperty ratios,
            SerializedProperty remainderFlags)
        {
            float total = 0f;
            for (int i = 0; i < ratios.arraySize; i++)
            {
                if (remainderFlags != null &&
                    remainderFlags.GetArrayElementAtIndex(i).boolValue) continue;

                total += Mathf.Max(0f, ratios.GetArrayElementAtIndex(i).floatValue);
            }

            return total;
        }

        private string FormatProbability(float assignedRatio, float denominator)
        {
            if (assignedRatio <= 0f || denominator <= 0f) return "0%";

            float percentage = assignedRatio / denominator * 100f;
            float inverse = denominator / assignedRatio;
            return percentage.ToString("0.######") + "%  (1/" + inverse.ToString("0.###") + ")";
        }

        private void ResizeFoldouts(int size)
        {
            if (flagFoldouts.Length == size) return;

            bool[] resized = new bool[size];
            int copyLength = Mathf.Min(flagFoldouts.Length, size);
            for (int i = 0; i < copyLength; i++) resized[i] = flagFoldouts[i];
            flagFoldouts = resized;
        }
    }
}
