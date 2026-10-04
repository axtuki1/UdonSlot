using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using AX.UdonSlot.REV1_0.Serialize;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>Explicit preview/apply transaction. Runtime output is a separate operation.</summary>
    public class ReelControlGenerateWindow : EditorWindow
    {
        [SerializeField] private ReelControlDesign source;
        [SerializeField] private ReelOutputController controller;
        [SerializeField] private PayoutTable payout;
        [SerializeField] private LotteryTable lottery;
        [SerializeField] private string flag = "";
        [SerializeField] private List<string> roles = new List<string>();
        [SerializeField] private int order, stage, scope, bet = 3, maxSlip = 4;
        [SerializeField] private int[] context;
        [SerializeField] private ReelControlPriority priority;
        [SerializeField] private ReelRolePriority rolePriority;
        private ReelControlDesign preview;
        [SerializeField] private bool validationMode;
        [SerializeField] private bool commonOnly;
        private ReelControlValidator.Report validation;
        private int validationFilter;
        private string sourceSnapshot, settingsSnapshot, error;
        private readonly List<Row> rows = new List<Row>();
        private readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();
        private Vector2 settingsScroll, rowsScroll;
        private int page;
        private bool warningsOnly;
        private const int PageSize = 80;
        private static readonly string[] Names = {"左", "中", "右"};
        private class Row
        {
            public string location, result, warning;
            public string[] symbols;
        }
        private void OnEnable()
        {
            Undo.undoRedoPerformed += Repaint;
            pictures.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:ReelPicture", new[] {"Assets", "Packages/com.axtuki1.udonslot"}))
            {
                var picture = AssetDatabase.LoadAssetAtPath<ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                if (picture != null && !string.IsNullOrEmpty(picture.pictureName)) pictures[picture.pictureName] = picture.picture;
            }
        }

        public static void Open(ReelControlDesign design, ReelOutputController machine, int order, int stage, int[] positions, bool commonOnly = false)
        {
            var window = GetWindow<ReelControlGenerateWindow>("たたき台生成");
            window.ClearPreview();
            window.commonOnly = commonOnly;
            if (commonOnly) window.scope = 1;
            window.validation = null; window.validationMode = false; window.error = null;
            window.settingsScroll = Vector2.zero; window.rowsScroll = Vector2.zero;
            window.titleContent = new GUIContent("たたき台生成");
            window.source = design; window.controller = machine;
            window.order = order; window.stage = stage; window.context = (int[])positions.Clone();
            var serialized = new SerializedObject(machine);
            window.payout = serialized.FindProperty("payoutTable").objectReferenceValue as PayoutTable;
            window.lottery = serialized.FindProperty("lotteryTable").objectReferenceValue as LotteryTable;
            window.priority = (ReelControlPriority)serialized.FindProperty("priority").enumValueIndex;
            window.rolePriority = (ReelRolePriority)serialized.FindProperty("rolePriority").enumValueIndex;
            window.maxSlip = Mathf.Clamp(serialized.FindProperty("maxSlipCount").intValue, 0, 4);
            window.roles.Clear(); window.flag = "";
            window.minSize = new Vector2(680, 560);
            window.Show();
        }
        public static void OpenValidation(ReelControlDesign design, ReelOutputController machine, int order, int stage, int[] positions)
        {
            Open(design, machine, order, stage, positions);
            var window = GetWindow<ReelControlGenerateWindow>();
            window.validationMode = true; window.scope = 0;
            window.titleContent = new GUIContent("入賞検証");
        }

        private void ClearPreview()
        {
            if (preview != null) DestroyImmediate(preview);
            preview = null; rows.Clear(); page = 0;
        }
        private void OnDisable() { Undo.undoRedoPerformed -= Repaint; ClearPreview(); }
        private string SettingsSignature()
        {
            return (controller == null ? 0 : controller.GetInstanceID()) + "|" +
                (payout == null ? "" : payout.GetInstanceID() + ":" + EditorJsonUtility.ToJson(payout)) + "|" +
                order + "|" + stage + "|" + scope + "|" + bet + "|" + maxSlip + "|" +
                priority + "|" + rolePriority + "|" + flag + "|" + string.Join(",", roles);
        }
        private bool Fresh()
        {
            if (source == null || controller == null || (preview == null && validation == null)) return false;
            for (int r = 0; r < 3; r++) if (!source.Matches(controller, r)) return false;
            return sourceSnapshot == EditorJsonUtility.ToJson(source) && settingsSnapshot == SettingsSignature();
        }
        private string Position(int reel, int lower)
        {
            return Names[reel] + ReelControlDesign.DisplayNumber(lower, source.reels[reel].symbols.Length) + "番";
        }
        private void Generate()
        {
            ClearPreview(); error = null;
            try
            {
                if (payout == null) throw new InvalidOperationException("配当表を指定してください。");
                foreach (string role in roles)
                    if (Array.IndexOf(payout.winPairNames, role) < 0)
                        throw new InvalidOperationException("配当表にない対象役があります。成立フラグから対象役を選び直してください: " + role);
                for (int r = 0; r < 3; r++)
                    if (!source.Matches(controller, r)) throw new InvalidOperationException("リール配列が編集データと一致しません。");
                if (!source.laterStopsInitialized) throw new InvalidOperationException("設計ウィンドウで第2・第3停止を取り込んでください。");
                sourceSnapshot = EditorJsonUtility.ToJson(source);
                settingsSnapshot = SettingsSignature();
                preview = CreateInstance<ReelControlDesign>();
                EditorJsonUtility.FromJsonOverwrite(sourceSnapshot, preview);
                preview.hideFlags = HideFlags.HideAndDontSave;
                var evaluator = new ReelSlipEvaluator { controller = controller, payout = payout,
                    bet = bet, maximumSlip = maxSlip, priority = priority, rolePriority = rolePriority,
                    allowedRoles = new HashSet<string>(roles) };
                double nextProgress = 0;
                preview.FillUnset(order, scope == 0 ? 0 : stage, scope == 0 ? 2 : stage,
                    scope == 0 ? null : context, (s, positions, press) =>
                {
                    if (EditorApplication.timeSinceStartup >= nextProgress)
                    {
                        nextProgress = EditorApplication.timeSinceStartup + 0.1;
                        if (EditorUtility.DisplayCancelableProgressBar("たたき台生成", "第" + (s + 1) + "停止を計算中", s / 3f))
                            throw new OperationCanceledException("生成を中止しました。編集データは変更していません。");
                    }
                    evaluator.Pattern = ReelControlDesign.StagePattern(order, s);
                    evaluator.positions = positions;
                    int slip = evaluator.Recommend(press);
                    var candidate = evaluator.Evaluate(press, slip);
                    int reel = ReelControlDesign.StopOrders[order][s];
                    string previous = "";
                    for (int i = 0; i < s; i++)
                    {
                        int prior = ReelControlDesign.StopOrders[order][i];
                        previous += Position(prior, positions[prior]) + " ";
                    }
                    int lower = ReelControlDesign.Normalize(press - slip, source.reels[reel].symbols.Length);
                    string shape = "";
                    var symbols = new string[3];
                    for (int row = 0; row < 3; row++)
                    {
                        if (row > 0) shape += " / ";
                        symbols[row] = source.reels[reel].symbols[ReelControlDesign.Normalize(lower + row - 2, source.reels[reel].symbols.Length)];
                        shape += symbols[row];
                    }
                    string warning = candidate.forbidden > 0 ? "対象外役が確定するライン: " + candidate.forbidden :
                        roles.Count > 0 && candidate.level < 2 ? "対象役の成立候補なし（取りこぼしを含む）" : "";
                    rows.Add(new Row { location = "第" + (s + 1) + "停止 [" + previous.Trim() + "] 押下 " + Position(reel, press),
                        result = slip + "コマ → " + Position(reel, lower) + "  " + shape, warning = warning, symbols = symbols });
                    return slip;
                });
                page = 0;
            }
            catch (Exception ex) { ClearPreview(); error = ex.Message; }
            finally { EditorUtility.ClearProgressBar(); }
        }
        private void Apply()
        {
            if (!Fresh()) { error = "編集データまたは計算設定が変わりました。プレビューを再生成してください。"; return; }
            Undo.RecordObject(source, "コントロール制御の推奨値を反映");
            source.reels = preview.reels;
            source.laterStops = preview.laterStops;
            var report = new StringBuilder();
            report.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " 配当表ベースの近似生成: " + rows.Count + "マス");
            report.AppendLine("フラグ: " + flag + " / 対象役: " + string.Join(", ", roles) + " / BET: " + bet +
                " / 最大滑り: " + maxSlip + " / " + priority + " / " + rolePriority);
            foreach (var row in rows) if (row.warning.Length > 0) report.AppendLine(row.location + " / " + row.result + " / " + row.warning);
            source.generationReport = report.ToString();
            EditorUtility.SetDirty(source);
            ClearPreview();
            error = null;
            Repaint();
        }
        private void DrawValidation()
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying || payout == null))
                if (GUILayout.Button("全押下位置を検証"))
                {
                    validation = null; error = null;
                    try
                    {
                        for (int r = 0; r < 3; r++) if (!source.Matches(controller, r))
                            throw new InvalidOperationException("機種と編集データのリール配列が一致しません。");
                        sourceSnapshot = EditorJsonUtility.ToJson(source);
                        settingsSnapshot = SettingsSignature();
                        double nextProgress = 0;
                        validation = new ReelControlValidator().Run(source, payout, roles, bet, maxSlip, order, scope == 1, () =>
                        {
                            if (EditorApplication.timeSinceStartup < nextProgress) return false;
                            nextProgress = EditorApplication.timeSinceStartup + 0.1;
                            return EditorUtility.DisplayCancelableProgressBar("入賞検証", "全押下位置と代替滑りの経路を検証中", 0.5f);
                        });
                        page = 0;
                        rowsScroll = Vector2.zero;
                    }
                    catch (Exception ex) { error = ex.Message; validation = null; }
                    finally { EditorUtility.ClearProgressBar(); }
                }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (validation == null) return;
            bool fresh = Fresh();
            if (!fresh) EditorGUILayout.HelpBox("編集データまたは検証条件が変わりました。再検証してください。以下は変更前の結果です。", MessageType.Warning);
            EditorGUILayout.LabelField("検証経路 " + validation.paths + " / 対象役成立 " + validation.wins + " / 指摘箇所 " + validation.issues.Count);
            if (validation.provisional) EditorGUILayout.HelpBox("未設定を含む暫定検証です。指摘がない経路も完成済みとは限りません。", MessageType.Warning);
            EditorGUILayout.LabelField("同じ編集箇所の指摘は代表例1件にまとめています。引込経路は一か所の滑り変更だけを調べます。", EditorStyles.wordWrappedMiniLabel);
            string[] filters = {"すべて", "対象外入賞", "引込経路あり", "未設定", "最大滑り超過"};
            validationFilter = EditorGUILayout.Popup("表示", validationFilter, filters);
            var visible = validationFilter == 0 ? validation.issues : validation.issues.FindAll(i => i.kind == filters[validationFilter]);
            int pages = Math.Max(1, (visible.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(page == 0)) if (GUILayout.Button("前へ")) page--;
                GUILayout.Label((page + 1) + " / " + pages);
                using (new EditorGUI.DisabledScope(page == pages - 1)) if (GUILayout.Button("次へ")) page++;
            }
            rowsScroll = EditorGUILayout.BeginScrollView(rowsScroll);
            if (visible.Count == 0) EditorGUILayout.LabelField("この検証条件・表示条件での指摘はありません。");
            for (int i = page * PageSize; i < Math.Min(visible.Count, (page + 1) * PageSize); i++)
            {
                var issue = visible[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(issue.kind + " / " +
                        string.Join("→", Array.ConvertAll(ReelControlDesign.StopOrders[issue.order], r => Names[r])) +
                        " 第" + (issue.stage+1) + "停止", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(issue.message, EditorStyles.wordWrappedLabel);
                    string pressed = "押下: ", stopped = "停止: ";
                    for (int r = 0; r < 3; r++) { pressed += Position(r, issue.presses[r]) + " "; stopped += Position(r, issue.stops[r]) + " "; }
                    EditorGUILayout.LabelField(pressed, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(stopped, EditorStyles.wordWrappedLabel);
                    if (issue.alternative >= 0)
                    {
                        string alternate = "代替: " + issue.slip + " → " + issue.alternative + "コマ / ";
                        for (int r = 0; r < 3; r++) alternate += Position(r, issue.alternativeStops[r]) + " ";
                        EditorGUILayout.LabelField(alternate, EditorStyles.wordWrappedLabel);
                    }
                    using (new EditorGUI.DisabledScope(!fresh))
                        if (GUILayout.Button("この条件・押下位置を設計画面で表示"))
                            ReelControlDesignWindow.ShowValidationIssue(source, controller, issue);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        private void OnGUI()
        {
            if (source == null || controller == null) { EditorGUILayout.HelpBox("設計ウィンドウの「たたき台生成」から開いてください。", MessageType.Info); return; }
            EditorGUILayout.LabelField(source.definitionName + " — " +
                string.Join(" → ", Array.ConvertAll(ReelControlDesign.StopOrders[order], r => Names[r])), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(validationMode ? "編集テーブルを全押下位置で通して検証します。未設定は暫定0コマ。対象役は下で指定してください。実行時の独自制御・役の優先順位・意図したリーチ目の正否は判定しません。" : "配当表ベースの推奨値（コントロール制御の近似）。独自のDynamicControlは再現しません。未設定だけを埋め、入力済みの0コマも保持します。後続での入賞を保証するものではありません。", MessageType.Info);
            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll, GUILayout.Height(210));
            if (commonOnly && !validationMode)
            {
                scope = 1;
                EditorGUILayout.HelpBox("選択リールの共通設定だけを生成します。全押し順で共通値を使用する条件に反映されます。対象役は下で指定してください。", MessageType.Info);
            }
            else scope = EditorGUILayout.Popup(validationMode ? "検証範囲" : "生成範囲", scope, validationMode ? new[] {"この押し順の全押下位置", "全6押し順の全押下位置"} : new[] {"この押し順の第1 → 第3停止", "現在選択中の停止条件だけ"});
            EditorGUILayout.LabelField("第1停止は同じ最初のリールを使う押し順と共有されます。", EditorStyles.wordWrappedMiniLabel);
            payout = (PayoutTable)EditorGUILayout.ObjectField("配当表", payout, typeof(PayoutTable), true);
            lottery = (LotteryTable)EditorGUILayout.ObjectField("抽選表（役選択の補助）", lottery, typeof(LotteryTable), true);
            flag = EditorGUILayout.TextField("成立フラグ", flag);
            using (new EditorGUI.DisabledScope(payout == null || lottery == null))
                if (GUILayout.Button("成立フラグから対象役を選択"))
                {
                    roles.Clear();
                    for (int p = 0; p < payout.GetWinPairCount(); p++)
                        if (lottery.IsRoleWon(flag, payout.winPairNames[p])) roles.Add(payout.winPairNames[p]);
                }
            bet = EditorGUILayout.IntSlider("BET", bet, 1, 3);
            maxSlip = EditorGUILayout.IntSlider("最大滑り", maxSlip, 0, 4);
            if (!validationMode)
            {
                priority = (ReelControlPriority)EditorGUILayout.EnumPopup("配当・組合せの優先", priority);
                rolePriority = (ReelRolePriority)EditorGUILayout.EnumPopup("役種別の優先", rolePriority);
            }
            EditorGUILayout.LabelField("対象役（未選択はハズレ。ボーナスを手動追加できます）");
            if (payout != null) for (int p = 0; p < payout.GetWinPairCount(); p++)
            {
                string role = payout.winPairNames[p];
                bool before = roles.Contains(role);
                bool after = EditorGUILayout.ToggleLeft(payout.GetWinPairDisplayName(role) + " (" + role + ")", before);
                if (after && !before) roles.Add(role);
                if (!after && before) roles.Remove(role);
            }
            EditorGUILayout.EndScrollView();
            if (validationMode) { DrawValidation(); return; }
            using (new EditorGUI.DisabledScope(Application.isPlaying || payout == null))
                if (GUILayout.Button("プレビューを生成")) Generate();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (preview == null)
            {
                if (!string.IsNullOrEmpty(source.generationReport))
                {
                    EditorGUILayout.LabelField("前回の生成記録（反映時点）");
                    rowsScroll = EditorGUILayout.BeginScrollView(rowsScroll);
                    EditorGUILayout.SelectableLabel(source.generationReport, EditorStyles.wordWrappedLabel,
                        GUILayout.Height(Math.Max(120, EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(source.generationReport), position.width - 40))));
                    EditorGUILayout.EndScrollView();
                }
                return;
            }
            EditorGUILayout.LabelField("変更予定: " + rows.Count + "マス / 注意: " + rows.FindAll(r => r.warning.Length > 0).Count + "マス");
            bool fresh = Fresh();
            if (!fresh) EditorGUILayout.HelpBox("元データまたは設定が変わりました。再生成が必要です。", MessageType.Warning);
            if (rows.Count == 0) EditorGUILayout.HelpBox("この範囲に未設定のマスはありません。変更はありません。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!fresh || rows.Count == 0 || Application.isPlaying))
                if (GUILayout.Button("確認して編集データへ反映（Undo可能）")) { Apply(); return; }
            EditorGUILayout.LabelField("反映後に設計画面で保存・データ出力してください。", EditorStyles.wordWrappedMiniLabel);
            warningsOnly = EditorGUILayout.ToggleLeft("注意のある候補だけ表示", warningsOnly);
            var visible = warningsOnly ? rows.FindAll(r => r.warning.Length > 0) : rows;
            int pages = Math.Max(1, (visible.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(page == 0)) if (GUILayout.Button("前へ")) page--;
                GUILayout.Label((page + 1) + " / " + pages);
                using (new EditorGUI.DisabledScope(page == pages - 1)) if (GUILayout.Button("次へ")) page++;
            }
            rowsScroll = EditorGUILayout.BeginScrollView(rowsScroll);
            for (int i = page * PageSize; i < Math.Min(visible.Count, (page + 1) * PageSize); i++)
            {
                var row = visible[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(64)))
                        foreach (string symbol in row.symbols)
                        {
                            Texture2D texture;
                            Rect rect = GUILayoutUtility.GetRect(64, 22, GUILayout.Width(64), GUILayout.Height(22));
                            if (pictures.TryGetValue(symbol, out texture) && texture != null)
                                ReelPicturePreview.Draw(rect, texture);
                            else GUI.Label(rect, symbol, EditorStyles.miniLabel);
                        }
                    using (new EditorGUILayout.VerticalScope())
                    {
                    EditorGUILayout.LabelField(row.location, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(row.result, EditorStyles.wordWrappedLabel);
                    if (row.warning.Length > 0) EditorGUILayout.LabelField("注意: " + row.warning, EditorStyles.wordWrappedLabel);
                    }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}


