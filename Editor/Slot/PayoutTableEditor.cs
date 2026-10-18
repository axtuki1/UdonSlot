using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>
    /// PayoutTableの平坦配列を、成立役と有効ラインの行単位で編集するInspector。
    /// </summary>
    [CustomEditor(typeof(PayoutTable))]
    public class PayoutTableEditor : UnityEditor.Editor
    {
        private bool[] winPairFoldouts = new bool[0];
        private bool showWinPairs = true;
        private bool showPayLines = true;

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            if (GUILayout.Button("機種エディタで開く")) AX.UdonSlot.REV1_0.Editor.ReelRule.ReelRuleEditorWindow.OpenFor(target);
            // 移行図柄は機種エディタでだけ編集する（配列の並びが崩れないように）
            var payoutTable = (PayoutTable)target;
            if (payoutTable.isTransitions != null)
                foreach (bool transition in payoutTable.isTransitions) if (transition) return;

            serializedObject.Update();

            SerializedProperty names = serializedObject.FindProperty("winPairNames");
            SerializedProperty displayNames = serializedObject.FindProperty("winPairDisplayNames");
            SerializedProperty payoutsBet1 = serializedObject.FindProperty("payoutsBet1");
            SerializedProperty payoutsBet2 = serializedObject.FindProperty("payoutsBet2");
            SerializedProperty payoutsBet3 = serializedObject.FindProperty("payouts");
            SerializedProperty replays = serializedObject.FindProperty("isReplays");
            SerializedProperty bonuses = serializedObject.FindProperty("isBonuses");
            SerializedProperty leftSymbols = serializedObject.FindProperty("leftSymbols");
            SerializedProperty centerSymbols = serializedObject.FindProperty("centerSymbols");
            SerializedProperty rightSymbols = serializedObject.FindProperty("rightSymbols");
            SerializedProperty controlFlags = serializedObject.FindProperty("controlFlags");
            SerializedProperty alwaysCandidates = serializedObject.FindProperty("isAlwaysControlCandidate");
            SerializedProperty anyStopOrders = serializedObject.FindProperty("isReelStopOrderAny");
            SerializedProperty leftOrders = serializedObject.FindProperty("leftStopOrders");
            SerializedProperty centerOrders = serializedObject.FindProperty("centerStopOrders");
            SerializedProperty rightOrders = serializedObject.FindProperty("rightStopOrders");

            NormalizeWinPairArrays(
                names,
                displayNames,
                payoutsBet1,
                payoutsBet2,
                payoutsBet3,
                replays,
                bonuses,
                leftSymbols,
                centerSymbols,
                rightSymbols,
                controlFlags,
                alwaysCandidates,
                anyStopOrders,
                leftOrders,
                centerOrders,
                rightOrders
            );

            showWinPairs = EditorGUILayout.Foldout(showWinPairs, "成立役", true);
            if (showWinPairs)
            {
                DrawWinPairs(
                    names,
                    displayNames,
                    payoutsBet1,
                    payoutsBet2,
                    payoutsBet3,
                    replays,
                    bonuses,
                    leftSymbols,
                    centerSymbols,
                    rightSymbols,
                    controlFlags,
                    alwaysCandidates,
                    anyStopOrders,
                    leftOrders,
                    centerOrders,
                    rightOrders
                );
            }

            EditorGUILayout.Space();

            SerializedProperty betMasks = serializedObject.FindProperty("payLineBetMasks");
            SerializedProperty leftRows = serializedObject.FindProperty("payLineLeftRows");
            SerializedProperty centerRows = serializedObject.FindProperty("payLineCenterRows");
            SerializedProperty rightRows = serializedObject.FindProperty("payLineRightRows");
            NormalizePayLineArrays(betMasks, leftRows, centerRows, rightRows);

            showPayLines = EditorGUILayout.Foldout(showPayLines, "有効ライン", true);
            if (showPayLines)
            {
                DrawPayLines(betMasks, leftRows, centerRows, rightRows);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawWinPairs(
            SerializedProperty names,
            SerializedProperty displayNames,
            SerializedProperty payoutsBet1,
            SerializedProperty payoutsBet2,
            SerializedProperty payoutsBet3,
            SerializedProperty replays,
            SerializedProperty bonuses,
            SerializedProperty leftSymbols,
            SerializedProperty centerSymbols,
            SerializedProperty rightSymbols,
            SerializedProperty controlFlags,
            SerializedProperty alwaysCandidates,
            SerializedProperty anyStopOrders,
            SerializedProperty leftOrders,
            SerializedProperty centerOrders,
            SerializedProperty rightOrders)
        {
            ResizeFoldouts(names.arraySize);

            for (int i = 0; i < names.arraySize; i++)
            {
                string internalName = names.GetArrayElementAtIndex(i).stringValue;
                string displayName = displayNames.GetArrayElementAtIndex(i).stringValue;
                string foldoutTitle = GetFoldoutTitle(displayName, internalName, i);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                winPairFoldouts[i] = EditorGUILayout.Foldout(winPairFoldouts[i], foldoutTitle, true);
                if (GUILayout.Button("削除", GUILayout.Width(48f)))
                {
                    DeleteWinPairAt(
                        i,
                        names,
                        displayNames,
                        payoutsBet1,
                        payoutsBet2,
                        payoutsBet3,
                        replays,
                        bonuses,
                        leftSymbols,
                        centerSymbols,
                        rightSymbols,
                        controlFlags,
                        alwaysCandidates,
                        anyStopOrders,
                        leftOrders,
                        centerOrders,
                        rightOrders
                    );
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                if (winPairFoldouts[i])
                {
                    EditorGUILayout.PropertyField(
                        displayNames.GetArrayElementAtIndex(i),
                        new GUIContent("表示名")
                    );
                    EditorGUILayout.PropertyField(
                        names.GetArrayElementAtIndex(i),
                        new GUIContent("内部使用名")
                    );
                    if (string.IsNullOrEmpty(names.GetArrayElementAtIndex(i).stringValue))
                    {
                        EditorGUILayout.HelpBox("内部使用名を入力してください。", MessageType.Error);
                    }

                    SerializedProperty replay = replays.GetArrayElementAtIndex(i);
                    SerializedProperty bonus = bonuses.GetArrayElementAtIndex(i);
                    PayoutRoleType currentRoleType = replay.boolValue
                        ? PayoutRoleType.Replay
                        : bonus.boolValue ? PayoutRoleType.Bonus : PayoutRoleType.SmallRole;
                    PayoutRoleType roleType = (PayoutRoleType)EditorGUILayout.Popup(
                        "役種別",
                        (int)currentRoleType,
                        new string[] { "小役", "リプレイ", "ボーナス" }
                    );
                    replay.boolValue = roleType == PayoutRoleType.Replay;
                    bonus.boolValue = roleType == PayoutRoleType.Bonus;
                    if (replay.boolValue)
                    {
                        EditorGUILayout.HelpBox(
                            "リプレイ役のメダル払出は0枚です。再遊技への遷移は機種固有処理で行います。",
                            MessageType.Info
                        );
                    }
                    else
                    {
                        EditorGUILayout.LabelField("BET別払出枚数", EditorStyles.boldLabel);
                        DrawPayoutField("1 BET 払出枚数", payoutsBet1, i);
                        DrawPayoutField("2 BET 払出枚数", payoutsBet2, i);
                        DrawPayoutField("3 BET 払出枚数", payoutsBet3, i);
                    }

                    EditorGUILayout.LabelField("図柄組み合わせ", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(leftSymbols.GetArrayElementAtIndex(i), new GUIContent("左"));
                    EditorGUILayout.PropertyField(centerSymbols.GetArrayElementAtIndex(i), new GUIContent("中"));
                    EditorGUILayout.PropertyField(rightSymbols.GetArrayElementAtIndex(i), new GUIContent("右"));
                    EditorGUILayout.HelpBox(
                        "空文字はANY、半角カンマ区切りはORとして扱います。例: Bell,RedBell,BlueBell",
                        MessageType.None
                    );

                    if (HasAmbiguousRoleDefinition(
                        i,
                        names,
                        displayNames,
                        payoutsBet1,
                        payoutsBet2,
                        payoutsBet3,
                        replays,
                        bonuses))
                    {
                        EditorGUILayout.HelpBox(
                            "同じ内部使用名で表示名、リプレイ設定、払出枚数が異なります。役の設定を統一してください。",
                            MessageType.Error
                        );
                    }

                    EditorGUILayout.PropertyField(
                        anyStopOrders.GetArrayElementAtIndex(i),
                        new GUIContent("停止順不問")
                    );
                    if (!anyStopOrders.GetArrayElementAtIndex(i).boolValue)
                    {
                        leftOrders.GetArrayElementAtIndex(i).intValue = EditorGUILayout.IntSlider(
                            "左停止順",
                            leftOrders.GetArrayElementAtIndex(i).intValue,
                            1,
                            3
                        );
                        centerOrders.GetArrayElementAtIndex(i).intValue = EditorGUILayout.IntSlider(
                            "中停止順",
                            centerOrders.GetArrayElementAtIndex(i).intValue,
                            1,
                            3
                        );
                        rightOrders.GetArrayElementAtIndex(i).intValue = EditorGUILayout.IntSlider(
                            "右停止順",
                            rightOrders.GetArrayElementAtIndex(i).intValue,
                            1,
                            3
                        );

                        int leftOrder = leftOrders.GetArrayElementAtIndex(i).intValue;
                        int centerOrder = centerOrders.GetArrayElementAtIndex(i).intValue;
                        int rightOrder = rightOrders.GetArrayElementAtIndex(i).intValue;
                        if (leftOrder == centerOrder || leftOrder == rightOrder || centerOrder == rightOrder)
                        {
                            EditorGUILayout.HelpBox("停止順は左・中・右で重複しないように設定してください。", MessageType.Error);
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("成立役を追加"))
            {
                int index = names.arraySize;
                ResizeWinPairArrays(
                    index + 1,
                    names,
                    displayNames,
                    payoutsBet1,
                    payoutsBet2,
                    payoutsBet3,
                    replays,
                    bonuses,
                    leftSymbols,
                    centerSymbols,
                    rightSymbols,
                    controlFlags,
                    alwaysCandidates,
                    anyStopOrders,
                    leftOrders,
                    centerOrders,
                    rightOrders
                );
                names.GetArrayElementAtIndex(index).stringValue = "New Win Pair";
                displayNames.GetArrayElementAtIndex(index).stringValue = "新しい成立役";
                payoutsBet1.GetArrayElementAtIndex(index).intValue = 0;
                payoutsBet2.GetArrayElementAtIndex(index).intValue = 0;
                payoutsBet3.GetArrayElementAtIndex(index).intValue = 0;
                replays.GetArrayElementAtIndex(index).boolValue = false;
                bonuses.GetArrayElementAtIndex(index).boolValue = false;
                leftSymbols.GetArrayElementAtIndex(index).stringValue = "";
                centerSymbols.GetArrayElementAtIndex(index).stringValue = "";
                rightSymbols.GetArrayElementAtIndex(index).stringValue = "";
                controlFlags.GetArrayElementAtIndex(index).stringValue = "";
                alwaysCandidates.GetArrayElementAtIndex(index).boolValue = false;
                anyStopOrders.GetArrayElementAtIndex(index).boolValue = true;
                leftOrders.GetArrayElementAtIndex(index).intValue = 1;
                centerOrders.GetArrayElementAtIndex(index).intValue = 2;
                rightOrders.GetArrayElementAtIndex(index).intValue = 3;
                ResizeFoldouts(index + 1);
                winPairFoldouts[index] = true;
            }
        }

        private void DrawPayLines(
            SerializedProperty betMasks,
            SerializedProperty leftRows,
            SerializedProperty centerRows,
            SerializedProperty rightRows)
        {
            string[] rowNames = { "上段", "中段", "下段" };

            for (int i = 0; i < betMasks.arraySize; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("ライン " + (i + 1), EditorStyles.boldLabel);
                if (GUILayout.Button("削除", GUILayout.Width(48f)))
                {
                    DeleteArrayElementAt(i, betMasks, leftRows, centerRows, rightRows);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                int mask = betMasks.GetArrayElementAtIndex(i).intValue;
                EditorGUILayout.LabelField("有効BET", EditorStyles.boldLabel);
                bool bet1 = EditorGUILayout.ToggleLeft("1 BET", (mask & 1) != 0);
                bool bet2 = EditorGUILayout.ToggleLeft("2 BET", (mask & 2) != 0);
                bool bet3 = EditorGUILayout.ToggleLeft("3 BET", (mask & 4) != 0);
                betMasks.GetArrayElementAtIndex(i).intValue =
                    (bet1 ? 1 : 0) |
                    (bet2 ? 2 : 0) |
                    (bet3 ? 4 : 0);

                leftRows.GetArrayElementAtIndex(i).intValue = EditorGUILayout.Popup(
                    "左",
                    Mathf.Clamp(leftRows.GetArrayElementAtIndex(i).intValue, 0, 2),
                    rowNames
                );
                centerRows.GetArrayElementAtIndex(i).intValue = EditorGUILayout.Popup(
                    "中",
                    Mathf.Clamp(centerRows.GetArrayElementAtIndex(i).intValue, 0, 2),
                    rowNames
                );
                rightRows.GetArrayElementAtIndex(i).intValue = EditorGUILayout.Popup(
                    "右",
                    Mathf.Clamp(rightRows.GetArrayElementAtIndex(i).intValue, 0, 2),
                    rowNames
                );

                if (betMasks.GetArrayElementAtIndex(i).intValue == 0)
                {
                    EditorGUILayout.HelpBox("有効なBET数が選択されていません。", MessageType.Warning);
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("有効ラインを追加"))
            {
                int index = betMasks.arraySize;
                ResizePayLineArrays(index + 1, betMasks, leftRows, centerRows, rightRows);
                betMasks.GetArrayElementAtIndex(index).intValue = 7;
                leftRows.GetArrayElementAtIndex(index).intValue = 1;
                centerRows.GetArrayElementAtIndex(index).intValue = 1;
                rightRows.GetArrayElementAtIndex(index).intValue = 1;
            }
        }

        private void NormalizeWinPairArrays(
            SerializedProperty names,
            SerializedProperty displayNames,
            SerializedProperty payoutsBet1,
            SerializedProperty payoutsBet2,
            SerializedProperty payoutsBet3,
            SerializedProperty replays,
            SerializedProperty bonuses,
            SerializedProperty leftSymbols,
            SerializedProperty centerSymbols,
            SerializedProperty rightSymbols,
            SerializedProperty controlFlags,
            SerializedProperty alwaysCandidates,
            SerializedProperty anyStopOrders,
            SerializedProperty leftOrders,
            SerializedProperty centerOrders,
            SerializedProperty rightOrders)
        {
            ResizeWinPairArrays(
                names.arraySize,
                names,
                displayNames,
                payoutsBet1,
                payoutsBet2,
                payoutsBet3,
                replays,
                bonuses,
                leftSymbols,
                centerSymbols,
                rightSymbols,
                controlFlags,
                alwaysCandidates,
                anyStopOrders,
                leftOrders,
                centerOrders,
                rightOrders
            );
        }

        private void ResizeWinPairArrays(
            int size,
            SerializedProperty names,
            SerializedProperty displayNames,
            SerializedProperty payoutsBet1,
            SerializedProperty payoutsBet2,
            SerializedProperty payoutsBet3,
            SerializedProperty replays,
            SerializedProperty bonuses,
            SerializedProperty leftSymbols,
            SerializedProperty centerSymbols,
            SerializedProperty rightSymbols,
            SerializedProperty controlFlags,
            SerializedProperty alwaysCandidates,
            SerializedProperty anyStopOrders,
            SerializedProperty leftOrders,
            SerializedProperty centerOrders,
            SerializedProperty rightOrders)
        {
            names.arraySize = size;
            ResizeStringArray(displayNames, size);
            ResizePayoutArray(payoutsBet1, payoutsBet3, size);
            ResizePayoutArray(payoutsBet2, payoutsBet3, size);
            payoutsBet3.arraySize = size;
            ResizeBoolArray(replays, size);
            ResizeBoolArray(bonuses, size);
            leftSymbols.arraySize = size;
            centerSymbols.arraySize = size;
            rightSymbols.arraySize = size;
            controlFlags.arraySize = size;
            alwaysCandidates.arraySize = size;
            anyStopOrders.arraySize = size;
            leftOrders.arraySize = size;
            centerOrders.arraySize = size;
            rightOrders.arraySize = size;
        }

        private void DeleteWinPairAt(
            int index,
            SerializedProperty names,
            SerializedProperty displayNames,
            SerializedProperty payoutsBet1,
            SerializedProperty payoutsBet2,
            SerializedProperty payoutsBet3,
            SerializedProperty replays,
            SerializedProperty bonuses,
            SerializedProperty leftSymbols,
            SerializedProperty centerSymbols,
            SerializedProperty rightSymbols,
            SerializedProperty controlFlags,
            SerializedProperty alwaysCandidates,
            SerializedProperty anyStopOrders,
            SerializedProperty leftOrders,
            SerializedProperty centerOrders,
            SerializedProperty rightOrders)
        {
            DeleteArrayElementAt(
                index,
                names,
                displayNames,
                payoutsBet1,
                payoutsBet2,
                payoutsBet3,
                replays,
                bonuses,
                leftSymbols,
                centerSymbols,
                rightSymbols,
                controlFlags,
                alwaysCandidates,
                anyStopOrders,
                leftOrders,
                centerOrders,
                rightOrders
            );
            ResizeFoldouts(names.arraySize);
        }

        private void NormalizePayLineArrays(
            SerializedProperty betMasks,
            SerializedProperty leftRows,
            SerializedProperty centerRows,
            SerializedProperty rightRows)
        {
            ResizePayLineArrays(betMasks.arraySize, betMasks, leftRows, centerRows, rightRows);
        }

        private void ResizePayLineArrays(
            int size,
            SerializedProperty betMasks,
            SerializedProperty leftRows,
            SerializedProperty centerRows,
            SerializedProperty rightRows)
        {
            betMasks.arraySize = size;
            leftRows.arraySize = size;
            centerRows.arraySize = size;
            rightRows.arraySize = size;
        }

        private void DeleteArrayElementAt(int index, params SerializedProperty[] arrays)
        {
            for (int i = 0; i < arrays.Length; i++)
            {
                arrays[i].DeleteArrayElementAtIndex(index);
            }
        }

        private void ResizeFoldouts(int size)
        {
            if (winPairFoldouts.Length == size) return;

            bool[] resized = new bool[size];
            int copyLength = Mathf.Min(winPairFoldouts.Length, size);
            for (int i = 0; i < copyLength; i++) resized[i] = winPairFoldouts[i];
            for (int i = copyLength; i < size; i++) resized[i] = true;
            winPairFoldouts = resized;
        }

        private string GetFoldoutTitle(
            string displayName,
            string internalName,
            int index)
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
            return "成立役 " + (index + 1);
        }

        private bool HasAmbiguousRoleDefinition(
            int targetIndex,
            SerializedProperty names,
            SerializedProperty displayNames,
            SerializedProperty payoutsBet1,
            SerializedProperty payoutsBet2,
            SerializedProperty payoutsBet3,
            SerializedProperty replays,
            SerializedProperty bonuses)
        {
            string targetName = names.GetArrayElementAtIndex(targetIndex).stringValue;
            if (string.IsNullOrEmpty(targetName)) return false;

            for (int i = 0; i < names.arraySize; i++)
            {
                if (i == targetIndex) continue;
                if (names.GetArrayElementAtIndex(i).stringValue != targetName) continue;
                if (GetEffectiveDisplayName(targetIndex, names, displayNames) !=
                    GetEffectiveDisplayName(i, names, displayNames)) return true;
                if (replays.GetArrayElementAtIndex(i).boolValue !=
                    replays.GetArrayElementAtIndex(targetIndex).boolValue) return true;
                if (bonuses.GetArrayElementAtIndex(i).boolValue !=
                    bonuses.GetArrayElementAtIndex(targetIndex).boolValue) return true;
                if (HasDifferentPayout(targetIndex, i, payoutsBet1)) return true;
                if (HasDifferentPayout(targetIndex, i, payoutsBet2)) return true;
                if (HasDifferentPayout(targetIndex, i, payoutsBet3)) return true;
            }

            return false;
        }

        private string GetEffectiveDisplayName(
            int index,
            SerializedProperty names,
            SerializedProperty displayNames)
        {
            string displayName = displayNames.GetArrayElementAtIndex(index).stringValue;
            return string.IsNullOrEmpty(displayName)
                ? names.GetArrayElementAtIndex(index).stringValue
                : displayName;
        }

        /// <summary>払出枚数を0以上に制限して表示する。</summary>
        private void DrawPayoutField(string label, SerializedProperty payouts, int index)
        {
            SerializedProperty payout = payouts.GetArrayElementAtIndex(index);
            payout.intValue = Mathf.Max(0, EditorGUILayout.IntField(label, payout.intValue));
        }

        /// <summary>追加されたBET別配当を、既存のBET 3配当で初期化する。</summary>
        private void ResizePayoutArray(
            SerializedProperty payouts,
            SerializedProperty fallbackPayouts,
            int size)
        {
            int previousSize = payouts.arraySize;
            payouts.arraySize = size;
            for (int i = previousSize; i < size; i++)
            {
                int fallbackPayout = i < fallbackPayouts.arraySize
                    ? fallbackPayouts.GetArrayElementAtIndex(i).intValue
                    : 0;
                payouts.GetArrayElementAtIndex(i).intValue = fallbackPayout;
            }
        }

        private void ResizeStringArray(SerializedProperty values, int size)
        {
            int previousSize = values.arraySize;
            values.arraySize = size;
            for (int i = previousSize; i < size; i++)
            {
                values.GetArrayElementAtIndex(i).stringValue = "";
            }
        }

        private void ResizeBoolArray(SerializedProperty values, int size)
        {
            int previousSize = values.arraySize;
            values.arraySize = size;
            for (int i = previousSize; i < size; i++)
            {
                values.GetArrayElementAtIndex(i).boolValue = false;
            }
        }

        private bool HasDifferentPayout(
            int targetIndex,
            int otherIndex,
            SerializedProperty payouts)
        {
            return payouts.GetArrayElementAtIndex(targetIndex).intValue !=
                payouts.GetArrayElementAtIndex(otherIndex).intValue;
        }
    }
}
