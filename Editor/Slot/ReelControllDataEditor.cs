using System;
using System.Collections.Generic;
using AX.UdonSlot.REV1_0.Serialize;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>停止出目ごとのテーブル編集と、配当表に基づく滑り候補の作成。</summary>
    [CustomEditor(typeof(ReelControlData))]
    public class ReelControllDataEditor : UnityEditor.Editor
    {
        private static readonly int[] Patterns = {16, 4, 1, 24, 18, 36, 6, 33, 9, 27, 30, 39, 54, 45, 57};
        private static readonly string[] PatternNames = {
            "[左]", "[中]", "[右]", "左 → [中]", "左 → [右]", "中 → [左]", "中 → [右]",
            "右 → [左]", "右 → [中]", "左 → 中 → [右]", "左 → 右 → [中]", "中 → 左 → [右]",
            "中 → 右 → [左]", "右 → 左 → [中]", "右 → 中 → [左]" };
        private static readonly string[] ReelNames = { "左", "中", "右" };
        private readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();
        private int patternSelection;
        private int selectedRule = -1;
        private int[] positions = { -1, -1, -1 };
        private int selectedPress;
        private int bet = 3;
        private int maximumSlip = 4;
        private ReelControlPriority priority;
        private ReelRolePriority rolePriority;
        private PayoutTable payout;
        private LotteryTable lottery;
        private string previewFlag = "";
        private readonly HashSet<string> allowedRoles = new HashSet<string>();
        private bool showAssistance = true;
        private ReelOutputController controller;

        private int Pattern { get { return Patterns[patternSelection]; } }
        private int Order(int reel) { return (Pattern >> ((2 - reel) * 2)) & 3; }
        private int TargetReel
        {
            get { int result = 0; for (int r = 1; r < 3; r++) if (Order(r) > Order(result)) result = r; return result; }
        }
        private int Wrap(int position, int reel)
        {
            int count = controller.reelDatas[reel].symbols.Length;
            return (position % count + count) % count;
        }
        private string Symbol(int reel, int lower, int row)
        {
            return controller.reelDatas[reel].symbols[Wrap(lower + row - 2, reel)];
        }
        private string PositionLabel(int reel, int lower)
        {
            return lower < 0 ? "任意" : "#" + lower + "  " + Symbol(reel, lower, 0) + " / " + Symbol(reel, lower, 1) + " / " + Symbol(reel, lower, 2);
        }
        private string[] PositionLabels(int reel)
        {
            int count = controller.reelDatas[reel].symbols.Length;
            string[] labels = new string[count + 1];
            labels[0] = "任意";
            for (int i = 0; i < count; i++) labels[i + 1] = PositionLabel(reel, i);
            return labels;
        }
        private void OnEnable()
        {
            pictures.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:ReelPicture", new[] { "Assets", "Packages/com.axtuki1.udonslot" }))
            {
                ReelPicture pic = AssetDatabase.LoadAssetAtPath<ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                if (pic != null && !string.IsNullOrEmpty(pic.pictureName)) pictures[pic.pictureName] = pic.picture;
            }
        }
        private void Save(ReelControlData data)
        {
            EditorUtility.SetDirty(data);
            PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            if (data.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(data.gameObject.scene);
        }
        private int[] ReadSlips(ReelControlData data, int count)
        {
            int[] values = new int[count];
            if (selectedRule < 0)
            {
                if (data.reelStopSlipCount != null && Pattern < data.reelStopSlipCount.Length && data.reelStopSlipCount[Pattern] != null)
                    Array.Copy(data.reelStopSlipCount[Pattern], values, Math.Min(count, data.reelStopSlipCount[Pattern].Length));
            }
            else
            {
                for (int i = 0; i < count; i++) values[i] = -1;
                int offset = data.conditionSlipOffsets[selectedRule];
                int length = data.conditionSlipLengths[selectedRule];
                if (offset >= 0 && length >= 0 && offset + length <= data.conditionSlips.Length)
                    Array.Copy(data.conditionSlips, offset, values, 0, Math.Min(count, length));
            }
            return values;
        }
        private void WriteSlips(ReelControlData data, int[] values)
        {
            Undo.RecordObject(data, "リール制御テーブルを編集");
            if (selectedRule < 0)
            {
                int[][] tables = data.reelStopSlipCount == null ? new int[58][] : (int[][])data.reelStopSlipCount.Clone();
                if (tables.Length < 58) Array.Resize(ref tables, 58);
                tables[Pattern] = (int[])values.Clone();
                data.reelStopSlipCount = tables;
            }
            else
            {
                List<int> all = new List<int>();
                int[] offsets = (int[])data.conditionSlipOffsets.Clone();
                int[] lengths = (int[])data.conditionSlipLengths.Clone();
                for (int i = 0; i < data.conditionPatterns.Length; i++)
                {
                    offsets[i] = all.Count;
                    if (i == selectedRule) { all.AddRange(values); lengths[i] = values.Length; }
                    else for (int j = 0; j < lengths[i]; j++) all.Add(data.conditionSlips[data.conditionSlipOffsets[i] + j]);
                }
                data.conditionSlipOffsets = offsets;
                data.conditionSlipLengths = lengths;
                data.conditionSlips = all.ToArray();
            }
            Save(data);
        }
        private static int[] Append(int[] array, int value)
        {
            int[] result = new int[(array == null ? 0 : array.Length) + 1];
            if (array != null) Array.Copy(array, result, array.Length);
            result[result.Length - 1] = value;
            return result;
        }
        private void AddRule(ReelControlData data, int[] values)
        {
            Undo.RecordObject(data, "出目条件付きテーブルを追加");
            data.conditionPatterns = Append(data.conditionPatterns, Pattern);
            data.conditionLeftPositions = Append(data.conditionLeftPositions, positions[0]);
            data.conditionCenterPositions = Append(data.conditionCenterPositions, positions[1]);
            data.conditionRightPositions = Append(data.conditionRightPositions, positions[2]);
            data.conditionSlipOffsets = Append(data.conditionSlipOffsets, data.conditionSlips.Length);
            data.conditionSlipLengths = Append(data.conditionSlipLengths, values.Length);
            List<int> all = new List<int>(data.conditionSlips);
            all.AddRange(values);
            data.conditionSlips = all.ToArray();
            selectedRule = data.conditionPatterns.Length - 1;
            Save(data);
        }
        private static int[] Remove(int[] values, int index)
        {
            List<int> result = new List<int>(values);
            result.RemoveAt(index);
            return result.ToArray();
        }
        private void RemoveRule(ReelControlData data)
        {
            Undo.RecordObject(data, "出目条件付きテーブルを削除");
            List<int> all = new List<int>();
            List<int> offsets = new List<int>();
            for (int i = 0; i < data.conditionPatterns.Length; i++)
            {
                if (i == selectedRule) continue;
                offsets.Add(all.Count);
                for (int j = 0; j < data.conditionSlipLengths[i]; j++) all.Add(data.conditionSlips[data.conditionSlipOffsets[i] + j]);
            }
            data.conditionPatterns = Remove(data.conditionPatterns, selectedRule);
            data.conditionLeftPositions = Remove(data.conditionLeftPositions, selectedRule);
            data.conditionCenterPositions = Remove(data.conditionCenterPositions, selectedRule);
            data.conditionRightPositions = Remove(data.conditionRightPositions, selectedRule);
            data.conditionSlipLengths = Remove(data.conditionSlipLengths, selectedRule);
            data.conditionSlipOffsets = offsets.ToArray();
            data.conditionSlips = all.ToArray();
            selectedRule = -1;
            Save(data);
        }
        private bool ConditionsMatch(ReelControlData data, int i)
        {
            return data.conditionPatterns[i] == Pattern && data.conditionLeftPositions[i] == positions[0] &&
                data.conditionCenterPositions[i] == positions[1] && data.conditionRightPositions[i] == positions[2];
        }
        private bool ConditionsValid(ReelControlData data)
        {
            if (data.conditionPatterns == null || data.conditionLeftPositions == null ||
                data.conditionCenterPositions == null || data.conditionRightPositions == null ||
                data.conditionSlipOffsets == null || data.conditionSlipLengths == null || data.conditionSlips == null) return false;
            int count = data.conditionPatterns.Length;
            if (data.conditionLeftPositions.Length != count || data.conditionCenterPositions.Length != count ||
                data.conditionRightPositions.Length != count || data.conditionSlipOffsets.Length != count ||
                data.conditionSlipLengths.Length != count) return false;
            for (int i = 0; i < count; i++)
                if (data.conditionSlipOffsets[i] < 0 || data.conditionSlipLengths[i] < 0 ||
                    (long)data.conditionSlipOffsets[i] + data.conditionSlipLengths[i] > data.conditionSlips.Length) return false;
            return true;
        }
        // Enumerate outputs of each earlier stop, preserving their correlation and runtime fallback rules.
        private List<int[]> GetReachableStates(ReelControlData data, bool allDynamic)
        {
            List<int[]> states = new List<int[]> { new[] { -1, -1, -1 } };
            for (int order = 1; order < Order(TargetReel); order++)
            {
                int reel = 0, prefix = 0;
                for (int r = 0; r < 3; r++)
                {
                    if (Order(r) == order) reel = r;
                    if (Order(r) <= order) prefix |= Order(r) << ((2 - r) * 2);
                }
                bool table = !allDynamic && data.IsReelStopTableStyle != null &&
                    prefix < data.IsReelStopTableStyle.Length && data.IsReelStopTableStyle[prefix];
                List<int[]> next = new List<int[]>();
                foreach (int[] state in states)
                {
                    HashSet<int> outputs = new HashSet<int>();
                    for (int press = 0; press < controller.reelDatas[reel].symbols.Length; press++)
                    {
                        int slip = 0;
                        if (table)
                        {
                            slip = data.GetConditionalSlip(prefix, state[0], state[1], state[2], press);
                            if (slip < 0)
                                slip = data.reelStopSlipCount != null && prefix < data.reelStopSlipCount.Length &&
                                    data.reelStopSlipCount[prefix] != null && press < data.reelStopSlipCount[prefix].Length
                                    ? data.reelStopSlipCount[prefix][press] : 0;
                        }
                        // Dynamic control is extensible: all final positions remain possible until a table is fixed.
                        outputs.Add(Wrap(press - slip, reel));
                    }
                    List<int> sorted = new List<int>(outputs);
                    sorted.Sort();
                    foreach (int output in sorted)
                    {
                        int[] result = (int[])state.Clone();
                        result[reel] = output;
                        next.Add(result);
                    }
                }
                states = next;
            }
            return states;
        }
        private bool IsReachable(List<int[]> states, int[] condition)
        {
            foreach (int[] state in states)
                if ((condition[0] < 0 || condition[0] == state[0]) &&
                    (condition[1] < 0 || condition[1] == state[1]) &&
                    (condition[2] < 0 || condition[2] == state[2])) return true;
            return false;
        }
        private string StateLabel(int[] state)
        {
            List<string> parts = new List<string>();
            for (int order = 1; order < Order(TargetReel); order++)
                for (int r = 0; r < 3; r++)
                    if (Order(r) == order) parts.Add(ReelNames[r] + " " + PositionLabel(r, state[r]));
            return string.Join(" ｜ ", parts);
        }
        private void DrawConditions(ReelControlData data, int count, List<int[]> reachable)
        {
            List<string> labels = new List<string> { "基本テーブル（出目条件なし）" };
            List<int> indices = new List<int> { -1 };
            for (int i = 0; i < data.conditionPatterns.Length; i++)
            {
                if (data.conditionPatterns[i] != Pattern) continue;
                indices.Add(i);
                bool valid = IsReachable(reachable, new[] { data.conditionLeftPositions[i], data.conditionCenterPositions[i], data.conditionRightPositions[i] });
                labels.Add((valid ? "" : "[到達不能] ") + "条件 " + (i + 1) + ": 左 " + PositionLabel(0, data.conditionLeftPositions[i]) +
                    " ｜ 中 " + PositionLabel(1, data.conditionCenterPositions[i]) + " ｜ 右 " + PositionLabel(2, data.conditionRightPositions[i]));
            }
            int selection = Math.Max(0, indices.IndexOf(selectedRule));
            int next = EditorGUILayout.Popup("編集テーブル", selection, labels.ToArray());
            if (next != selection || !indices.Contains(selectedRule))
            {
                selectedRule = indices[next];
                if (selectedRule >= 0) positions = new[] { data.conditionLeftPositions[selectedRule], data.conditionCenterPositions[selectedRule], data.conditionRightPositions[selectedRule] };
            }
            EditorGUILayout.LabelField("停止済みの出目（上段 / 中段 / 下段）", EditorStyles.boldLabel);
            if (Order(TargetReel) > 1)
            {
                List<string> choices = new List<string> { "任意（基本テーブル）" };
                int chosen = -1;
                if (Array.TrueForAll(positions, p => p < 0)) chosen = 0;
                for (int i = 0; i < reachable.Count; i++)
                {
                    choices.Add(StateLabel(reachable[i]));
                    if (positions[0] == reachable[i][0] && positions[1] == reachable[i][1] && positions[2] == reachable[i][2]) chosen = i + 1;
                }
                if (chosen < 0) { chosen = choices.Count; choices.Add("現在の条件: " + StateLabel(positions)); }
                int picked = EditorGUILayout.Popup("到達可能な停止形", chosen, choices.ToArray());
                if (picked != chosen)
                {
                    positions = picked == 0 ? new[] { -1, -1, -1 } : (int[])reachable[picked - 1].Clone();
                    selectedRule = -1;
                    for (int i = 0; i < data.conditionPatterns.Length; i++)
                        if (ConditionsMatch(data, i)) { selectedRule = i; break; }
                }
                EditorGUILayout.LabelField("到達可能な組み合わせ: " + reachable.Count);
            }
            int existing = -1;
            for (int i = 0; i < data.conditionPatterns.Length; i++) if (ConditionsMatch(data, i)) { existing = i; break; }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Order(TargetReel) == 1 || Array.TrueForAll(positions, p => p < 0) || !IsReachable(reachable, positions)))
                {
                    if (GUILayout.Button(existing >= 0 ? "この出目条件を開く" : "この出目条件を作成（表示中をコピー）"))
                    {
                        if (existing >= 0) selectedRule = existing;
                        else AddRule(data, ReadSlips(data, count));
                    }
                }
                using (new EditorGUI.DisabledScope(selectedRule < 0))
                    if (GUILayout.Button("条件を削除")) RemoveRule(data);
            }
            if (selectedRule >= 0 && !ConditionsMatch(data, selectedRule))
                EditorGUILayout.HelpBox("選択中テーブルの条件と出目が異なります。該当条件を開くか作成してから編集してください。", MessageType.Warning);
        }
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            ReelControlData data = (ReelControlData)target;
            if (GUILayout.Button("専用ウィンドウで停止制御を設計", GUILayout.Height(28)))
                ReelControlDesignWindow.OpenFor(data);
            if (!ConditionsValid(data))
            {
                EditorGUILayout.HelpBox("条件テーブルの配列が不整合です。直前の操作をUndoするか、保存済みデータを復元してください。", MessageType.Error);
                return;
            }
            if (controller == null) controller = data.GetComponentInParent<ReelOutputController>();
            ReelOutputController nextController = (ReelOutputController)EditorGUILayout.ObjectField("参照する機種", controller, typeof(ReelOutputController), true);
            if (nextController != controller) { controller = nextController; payout = null; lottery = null; allowedRoles.Clear(); positions = new[] { -1, -1, -1 }; }
            if (controller == null || controller.reelDatas == null || controller.reelDatas.Length < 3)
            { EditorGUILayout.HelpBox("ReelOutputControllerと3本のリール配列を設定してください。", MessageType.Info); return; }
            for (int r = 0; r < 3; r++)
                if (controller.reelDatas[r] == null || controller.reelDatas[r].symbols == null || controller.reelDatas[r].symbols.Length == 0)
                { EditorGUILayout.HelpBox(ReelNames[r] + "リールの図柄配列がありません。", MessageType.Warning); return; }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                int nextPattern = EditorGUILayout.Popup("停止順・今回の停止", patternSelection, PatternNames);
                if (nextPattern != patternSelection) { patternSelection = nextPattern; selectedRule = -1; positions = new[] { -1, -1, -1 }; }
                int count = controller.reelDatas[TargetReel].symbols.Length;
                bool enabled = data.IsReelStopTableStyle != null && Pattern < data.IsReelStopTableStyle.Length && data.IsReelStopTableStyle[Pattern];
                bool nextEnabled = EditorGUILayout.Toggle("この停止順でテーブル制御", enabled);
                if (enabled != nextEnabled)
                {
                    Undo.RecordObject(data, "テーブル制御を切り替え");
                    bool[] flags = data.IsReelStopTableStyle == null ? new bool[58] : (bool[])data.IsReelStopTableStyle.Clone();
                    if (flags.Length < 58) Array.Resize(ref flags, 58);
                    flags[Pattern] = nextEnabled;
                    data.IsReelStopTableStyle = flags;
                    Save(data);
                }
                if (!nextEnabled) EditorGUILayout.HelpBox("現在は自動制御です。テーブルは編集できますが、実行時に使用するには上のチェックを有効にしてください。", MessageType.Info);
                bool allDynamic = (bool)typeof(ReelOutputController).GetField("isAllDynamicControl",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                List<int[]> reachable = GetReachableStates(data, allDynamic);
                DrawConditions(data, count, reachable);
                DrawAssistance();
                if (Order(TargetReel) > 1)
                {
                    EditorGUILayout.HelpBox("前段の有効なテーブルから停止形を絞り込みます。前段が自動制御の場合は到達先を限定せず扱います。今回のリールはどの位置でも押せるため、押下位置の行はすべて編集対象です。", MessageType.Info);
                    using (new EditorGUI.DisabledScope(payout == null))
                        if (GUILayout.Button("到達可能な未作成条件だけ推奨値で作成"))
                        {
                            Undo.IncrementCurrentGroup();
                            int group = Undo.GetCurrentGroup();
                            int[] savedPositions = positions;
                            int savedRule = selectedRule;
                            foreach (int[] state in reachable)
                            {
                                positions = state;
                                bool exists = false;
                                for (int i = 0; i < data.conditionPatterns.Length; i++) if (ConditionsMatch(data, i)) { exists = true; break; }
                                if (exists) continue;
                                int[] generated = new int[count];
                                for (int press = 0; press < count; press++) generated[press] = Recommend(press);
                                AddRule(data, generated);
                            }
                            positions = savedPositions;
                            selectedRule = savedRule;
                            Undo.SetCurrentGroupName("到達可能な条件を生成");
                            Undo.CollapseUndoOperations(group);
                        }
                }
                bool reachableCondition = IsReachable(reachable, positions);
                bool editable = reachableCondition && (selectedRule < 0 || ConditionsMatch(data, selectedRule));
                if (!reachableCondition) EditorGUILayout.HelpBox("前段の制御では到達できない条件です。値の入力・推奨値の反映を無効にしています。", MessageType.Warning);
                bool concrete = true;
                for (int r = 0; r < 3; r++) if (Order(r) > 0 && Order(r) < Order(TargetReel) && positions[r] < 0) concrete = false;
                bool canSuggest = payout != null && concrete && reachableCondition;
                if (!concrete) EditorGUILayout.HelpBox("推奨候補の計算には、停止済みリールの出目をすべて選んでください。任意条件のテーブルは手動で編集できます。", MessageType.Info);
                int[] values = ReadSlips(data, count);
                int[] recommended = new int[count];
                if (canSuggest) for (int i = 0; i < count; i++) recommended[i] = Recommend(i);
                using (new EditorGUI.DisabledScope(!canSuggest || !editable || (selectedRule < 0 && Order(TargetReel) > 1)))
                    if (GUILayout.Button("推奨値をこのテーブル全体に反映")) { values = (int[])recommended.Clone(); WriteSlips(data, values); }
                if (selectedRule < 0 && Order(TargetReel) > 1)
                    EditorGUILayout.HelpBox("特定の出目で計算した候補は条件テーブルに反映してください。基本テーブルへの候補反映は第1停止で使用できます。", MessageType.Info);
                EditorGUILayout.LabelField("押下位置 → 滑り → 停止出目", EditorStyles.boldLabel);
                if (selectedRule >= 0) EditorGUILayout.LabelField("滑り -1 は基本テーブルを使用します。", EditorStyles.miniLabel);
                for (int i = 0; i < count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Toggle(selectedPress == i, "#" + i, "Button", GUILayout.Width(46))) selectedPress = i;
                        EditorGUILayout.LabelField(controller.reelDatas[TargetReel].symbols[i], GUILayout.Width(65));
                        using (new EditorGUI.DisabledScope(!editable))
                        {
                            int value = EditorGUILayout.IntField(values[i], GUILayout.Width(40));
                            if (value != values[i]) { values[i] = Mathf.Clamp(value, selectedRule < 0 ? 0 : -1, 4); WriteSlips(data, values); }
                        }
                        int effective = values[i] < 0 ? BaseSlip(data, i) : values[i];
                        EditorGUILayout.LabelField(PositionLabel(TargetReel, Wrap(i - effective, TargetReel)), GUILayout.MinWidth(110));
                        using (new EditorGUI.DisabledScope(!canSuggest || !editable || (selectedRule < 0 && Order(TargetReel) > 1)))
                        {
                            if (GUILayout.Button(canSuggest ? "推奨 " + recommended[i] : "推奨 —", GUILayout.Width(62)))
                            { values[i] = recommended[i]; WriteSlips(data, values); }
                        }
                    }
                }
                selectedPress = Mathf.Clamp(selectedPress, 0, count - 1);
                DrawCandidates(canSuggest, recommended[selectedPress]);
            }
        }
        private int BaseSlip(ReelControlData data, int position)
        {
            return data.reelStopSlipCount != null && Pattern < data.reelStopSlipCount.Length && data.reelStopSlipCount[Pattern] != null && position < data.reelStopSlipCount[Pattern].Length ? data.reelStopSlipCount[Pattern][position] : 0;
        }
        private void DrawAssistance()
        {
            showAssistance = EditorGUILayout.Foldout(showAssistance, "候補計算の設定", true);
            if (payout == null)
            {
                SerializedObject source = new SerializedObject(controller);
                payout = source.FindProperty("payoutTable").objectReferenceValue as PayoutTable;
                lottery = source.FindProperty("lotteryTable").objectReferenceValue as LotteryTable;
            }
            if (!showAssistance) return;
            payout = (PayoutTable)EditorGUILayout.ObjectField("配当表", payout, typeof(PayoutTable), true);
            lottery = (LotteryTable)EditorGUILayout.ObjectField("抽選表（役の選択補助）", lottery, typeof(LotteryTable), true);
            previewFlag = EditorGUILayout.TextField("成立フラグ", previewFlag);
            using (new EditorGUI.DisabledScope(lottery == null || payout == null))
                if (GUILayout.Button("成立フラグから対象役を選択"))
                {
                    allowedRoles.Clear();
                    for (int p = 0; p < payout.GetWinPairCount(); p++)
                        if (lottery.IsRoleWon(previewFlag, payout.winPairNames[p])) allowedRoles.Add(payout.winPairNames[p]);
                }
            bet = EditorGUILayout.IntSlider("計算用BET", bet, 1, 3);
            maximumSlip = EditorGUILayout.IntSlider("最大滑り", maximumSlip, 0, 4);
            priority = (ReelControlPriority)EditorGUILayout.EnumPopup("配当・組合せの優先", priority);
            rolePriority = (ReelRolePriority)EditorGUILayout.EnumPopup("役種別の優先", rolePriority);
            EditorGUILayout.HelpBox("引き込む役を選択します（複数可、未選択はハズレ）。ボーナスもここで追加できます。計算設定は編集補助用で、実行時は保存した滑り値を使用します。", MessageType.Info);
            if (payout != null) for (int p = 0; p < payout.GetWinPairCount(); p++)
            {
                string name = payout.winPairNames[p];
                bool selected = allowedRoles.Contains(name);
                bool next = EditorGUILayout.ToggleLeft(payout.GetWinPairDisplayName(name) + " (" + name + ")", selected);
                if (next) allowedRoles.Add(name); else allowedRoles.Remove(name);
            }
        }
        private ReelSlipEvaluator Evaluator()
        {
            return new ReelSlipEvaluator { controller = controller, payout = payout, Pattern = Pattern,
                positions = positions, bet = bet, maximumSlip = maximumSlip, priority = priority,
                rolePriority = rolePriority, allowedRoles = allowedRoles };
        }
        private ReelSlipEvaluator.Candidate Evaluate(int pressed, int slip) { return Evaluator().Evaluate(pressed, slip); }
        private int Recommend(int pressed) { return Evaluator().Recommend(pressed); }
        private void DrawCandidates(bool canSuggest, int recommended)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("押下 #" + selectedPress + " の滑り候補", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("未停止リールを含む役は成立可能性の表示です。後続テーブルによる入賞保証ではありません。対象外役が確定する候補は警告します。", MessageType.Info);
            for (int slip = 0; slip <= maximumSlip; slip++)
            {
                int lower = Wrap(selectedPress - slip, TargetReel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(slip + "コマ" + (canSuggest && slip == recommended ? "  ★ 推奨" : "") + "  → " + PositionLabel(TargetReel, lower));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int r = 0; r < 3; r++)
                        {
                            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(65)))
                            {
                                EditorGUILayout.LabelField(ReelNames[r], GUILayout.Width(65));
                                int stop = r == TargetReel ? lower : positions[r];
                                for (int row = 0; row < 3; row++)
                                {
                                    string symbol = stop < 0 ? "—" : Symbol(r, stop, row);
                                    Texture2D texture;
                                    if (pictures.TryGetValue(symbol, out texture) && texture != null)
                                    {
                                        Rect rect = GUILayoutUtility.GetRect(60f, 32f, GUILayout.Width(60f), GUILayout.Height(32f));
                                        ReelPicturePreview.Draw(rect, texture);
                                    }
                                    else GUILayout.Label(symbol, GUILayout.Width(65), GUILayout.Height(24));
                                }
                            }
                        }
                    }
                    if (canSuggest)
                    {
                        ReelSlipEvaluator.Candidate candidate = Evaluate(selectedPress, slip);
                        EditorGUILayout.LabelField(candidate.roles + " / 最大配当 " + candidate.payout, EditorStyles.wordWrappedLabel);
                        if (candidate.forbidden > 0) EditorGUILayout.HelpBox("対象外役が確定するライン: " + candidate.forbidden, MessageType.Warning);
                    }
                }
            }
        }
    }
}


