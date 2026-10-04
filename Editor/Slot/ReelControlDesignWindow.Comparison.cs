using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    public partial class ReelControlDesignWindow
    {
        [SerializeField] private bool showComparison, comparisonDifferencesOnly;
        [SerializeField] private Rect comparisonRect = new Rect(40, 160, 820, 450);
        private Vector2 comparisonScroll;
        private string comparisonSearch = "";
        private string comparisonNotice;
        private ReelControlDesign comparisonBasis;
        private ReelOutputController comparisonController;
        private ReelControlData comparisonOutput;
        private readonly List<ComparisonEntry> comparisonEntries = new List<ComparisonEntry>();
        private sealed class ComparisonEntry
        {
            public string label, source, error;
            public ReelControlDesign data;
            public ReelControlData runtime;
            public bool isRuntime;
            public bool[] tableEnabled;
        }

        private void ClearComparison()
        {
            foreach (var row in comparisonEntries)
                if (row.isRuntime && row.data != null) DestroyImmediate(row.data);
            comparisonEntries.Clear();
            comparisonBasis = null;
        }

        private void RefreshComparison()
        {
            ClearComparison();
            comparisonBasis = design;
            comparisonController = controller;
            comparisonOutput = output;
            comparisonNotice = null;
            if (design == null || !ValidController()) return;
            string machineId = Id(controller);
            foreach (string guid in AssetDatabase.FindAssets("t:ReelControlDesign"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ReelControlDesign>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || asset == design || asset.controllerId != machineId) continue;
                AddComparisonAsset(asset);
            }
            comparisonEntries.Sort((a, b) => StringComparer.Ordinal.Compare(a.label, b.label));
            var property = new SerializedObject(controller).FindProperty("reelControl");
            var mapping = property != null ? property.objectReferenceValue as ReelControl : null;
            bool outputListed = false;
            if (mapping == null || mapping.flagNames == null || mapping.reelControlDataList == null)
                comparisonNotice = "機種の検索名一覧が未設定です。編集中の出力先は別途表示します。";
            else for (int i = 0; i < mapping.flagNames.Length; i++)
            {
                var data = i < mapping.reelControlDataList.Length ? mapping.reelControlDataList[i] : null;
                AddRuntimeComparison(mapping.flagNames[i], data);
                if (data == output) outputListed = true;
            }
            if (output != null && !outputListed) AddRuntimeComparison(output.name + "（編集中の出力先）", output);
            comparisonScroll = Vector2.zero;
        }

        private void AddRuntimeComparison(string label, ReelControlData data)
        {
            var entry = new ComparisonEntry { label = label, runtime = data, isRuntime = true,
                tableEnabled = data != null && data.IsReelStopTableStyle != null ? (bool[])data.IsReelStopTableStyle.Clone() : null,
                source = "出力先（更新時点）: " + (data != null ? data.name : "なし") };
            if (data == null) entry.error = "出力先なし";
            else
            {
                entry.data = CreateInstance<ReelControlDesign>();
                entry.data.hideFlags = HideFlags.HideAndDontSave;
                try { entry.data.Initialize(controller, data); }
                catch (Exception ex) { entry.error = "読み込み不可: " + ex.Message; }
            }
            comparisonEntries.Add(entry);
        }

        private void AddComparisonAsset(ReelControlDesign asset)
        {
            foreach (var entry in comparisonEntries) if (entry.data == asset) return;
            comparisonEntries.Add(new ComparisonEntry { data = asset, label = asset.definitionName,
                source = "編集データ: " + AssetDatabase.GetAssetPath(asset) });
        }

        private void FitComparison()
        {
            comparisonRect.width = Mathf.Min(820, position.width - 20);
            comparisonRect.height = Mathf.Min(500, position.height - 40);
            comparisonRect.x = Mathf.Clamp(comparisonRect.x, 0, position.width - comparisonRect.width);
            comparisonRect.y = Mathf.Clamp(comparisonRect.y, 20, position.height - comparisonRect.height);
        }

        private void DrawComparison(int id)
        {
            EditorGUI.DrawRect(new Rect(1, 20, comparisonRect.width - 2, comparisonRect.height - 21),
                EditorGUIUtility.isProSkin ? new Color(.20f, .20f, .20f, 1) : new Color(.78f, .78f, .78f, 1));
            if (comparisonBasis != design || comparisonController != controller || comparisonOutput != output) RefreshComparison();
            if (GUI.Button(new Rect(comparisonRect.width - 28, 2, 24, 18), "×")) showComparison = false;
            GUILayout.BeginArea(new Rect(8, 24, comparisonRect.width - 16, comparisonRect.height - 32));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(OrderNames[stopOrder] + " / " + StageLabel, EditorStyles.boldLabel);
                if (GUILayout.Button("一覧・出力値を更新", GUILayout.Width(140))) RefreshComparison();
            }
            string prior = "前段条件: ";
            for (int s = 0; s < stopStage; s++)
            { int r = ReelControlDesign.StopOrders[stopOrder][s]; prior += ReelNames[r] + " " + Number(priorPositions[r], r) + "番  "; }
            GUILayout.Label(stopStage == 0 ? "前段条件なし" : prior, EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("← 押下位置", GUILayout.Width(95))) pressedPositions[activeReel] = ReelControlDesign.Normalize(pressedPositions[activeReel] + 1, Count);
                GUILayout.Label(ReelNames[activeReel] + " " + Number(pressedPositions[activeReel], activeReel) + "番押下", GUILayout.Width(110));
                if (GUILayout.Button("押下位置 →", GUILayout.Width(95))) pressedPositions[activeReel] = ReelControlDesign.Normalize(pressedPositions[activeReel] - 1, Count);
                comparisonDifferencesOnly = GUILayout.Toggle(comparisonDifferencesOnly, "差がある定義のみ");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                comparisonSearch = EditorGUILayout.TextField("名前・パスで絞り込み", comparisonSearch);
                var added = (ReelControlDesign)EditorGUILayout.ObjectField(null, typeof(ReelControlDesign), false, GUILayout.Width(170));
                if (added != null && added != design) AddComparisonAsset(added);
            }
            GUILayout.Label("右のアセット欄で比較対象を追加できます。出力先の自動制御は比較対象外。未設定は暫定0です。", EditorStyles.wordWrappedMiniLabel);
            if (comparisonNotice != null) GUILayout.Label(comparisonNotice, EditorStyles.wordWrappedMiniLabel);
            var basis = ReelControlComparison.Evaluate(design, design, stopOrder, stopStage, priorPositions, pressedPositions[activeReel]);
            DrawComparisonRow("編集中: " + design.definitionName, "編集データ（未出力の変更を含む）", design, basis, true);
            comparisonScroll = EditorGUILayout.BeginScrollView(comparisonScroll);
            int shown = 0;
            foreach (var entry in comparisonEntries)
            {
                if (!string.IsNullOrEmpty(comparisonSearch) && (entry.label + " " + entry.source).IndexOf(comparisonSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string error = entry.error;
                if (error == null && entry.isRuntime)
                    for (int s = 0; s <= stopStage; s++)
                    {
                        int pattern = ReelControlDesign.StagePattern(stopOrder, s);
                        var enabled = entry.tableEnabled;
                        if (enabled == null || pattern >= enabled.Length || !enabled[pattern])
                        { error = s == stopStage ? "自動制御（滑り比較不可）" : "前段が自動制御（到達判定不可）"; break; }
                    }
                var result = error != null ? new ReelControlComparison.Result { state = ReelControlComparison.State.Invalid, message = error }
                    : ReelControlComparison.Evaluate(design, entry.data, stopOrder, stopStage, priorPositions, pressedPositions[activeReel]);
                bool same = ReelControlComparison.Same(basis, result);
                if (comparisonDifferencesOnly && same) continue;
                DrawComparisonRow(entry.label, entry.source, entry.data, result, same);
                shown++;
            }
            if (shown == 0) GUILayout.Label("該当する比較対象はありません。", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, comparisonRect.width - 32, 22));
        }

        private void DrawComparisonRow(string label, string source, ReelControlDesign data, ReelControlComparison.Result result, bool same)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox, GUILayout.Height(76)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(300)))
                {
                    GUILayout.Label(new GUIContent(label, label), EditorStyles.boldLabel);
                    GUILayout.Label(new GUIContent(source, source), EditorStyles.miniLabel);
                    GUILayout.Label(result.message + (result.provisional ? " / 前段が暫定値で到達" : ""), EditorStyles.wordWrappedMiniLabel);
                }
                GUILayout.Label(!result.HasStop && result.state != ReelControlComparison.State.Unreachable ? "比較不可" : same ? "同じ" : "差あり", GUILayout.Width(60));
                if (result.HasStop)
                {
                    GUILayout.Label(Number(result.stop, activeReel) + "番停止", GUILayout.Width(72));
                    // Top / center / bottom are horizontal here to keep many definitions visible.
                    var symbols = data.reels[activeReel].symbols;
                    for (int row = 0; row < 3; row++)
                    {
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(75)))
                        {
                            GUILayout.Label(new[] {"上段", "中段", "下段"}[row], EditorStyles.centeredGreyMiniLabel);
                            Rect rect = GUILayoutUtility.GetRect(70, 42);
                            DrawPicture(rect, symbols[ReelControlDesign.Normalize(result.stop - 2 + row, symbols.Length)]);
                        }
                    }
                }
            }
        }
    }
}
