using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AX.UdonSlot.REV1_0.Serialize;

namespace AX.UdonSlot.REV1_0.Editor
{
    public partial class ReelControlDesignWindow : EditorWindow
    {
        [SerializeField] private ReelControlDesign design;
        [SerializeField] private ReelOutputController controller;
        [SerializeField] private ReelControlData output;
        [SerializeField] private int activeReel;
        [SerializeField] private int stopOrder, stopStage;
        [SerializeField] private bool editCommon;
        [SerializeField] private int[] priorPositions = {-1, -1, -1};
        private List<int[]> reachable = new List<int[]>();
        private string contextError;
        [SerializeField] private int[] pressedPositions = { 0, 0, 0 };
        [SerializeField] private bool showTable;
        [SerializeField] private bool showIssues;
        [SerializeField] private bool compareSideBySide;
        [SerializeField] private Rect tableRect = new Rect(30, 100, 350, 400);
        [SerializeField] private Rect issueRect = new Rect(400, 100, 400, 240);
        private Vector2 issueScroll, assignmentScroll;
        private readonly HashSet<ReelControlDesign.PendingReview> expandedReviews = new HashSet<ReelControlDesign.PendingReview>();
        private int tableRows;
        private float tableRowHeight;
        private Vector2[] stripScroll = new Vector2[3];
        private readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();
        private int lastPress = -1, lastOld = -1, lastNew = -1, lastReel = -1;
        private string message;
        private static readonly string[] ReelNames = { "左", "中", "右" };
        private static readonly string[] OrderNames = { "左 → 中 → 右", "左 → 右 → 中", "中 → 左 → 右", "中 → 右 → 左", "右 → 左 → 中", "右 → 中 → 左" };
        private int CurrentPattern { get { return ReelControlDesign.StagePattern(stopOrder, stopStage); } }
        private int[] CurrentSlips { get { return design.SlipsFor(stopOrder, stopStage, priorPositions); } }
        private bool ContextReachable
        {
            get
            {
                foreach (var row in reachable) if (row[0] == priorPositions[0] && row[1] == priorPositions[1] && row[2] == priorPositions[2]) return true;
                return false;
            }
        }
        private string StageLabel { get { return editCommon ? "共通設定" : "第" + (stopStage + 1) + "停止"; } }
        private bool StageOutputMatches(int stage)
        {
            if (stage == 0) return design.OutputMatches(output, ReelControlDesign.StopOrders[stopOrder][0]);
            int pattern = ReelControlDesign.StagePattern(stopOrder, stage);
            var table = design.LaterTable(pattern);
            try { return table.exportedDesign == design.LaterSignature(pattern) && table.exportedOutput == ReelControlDesign.OutputSignature(output, pattern); }
            catch (Exception) { return false; }
        }
        private int StageOf(int reel) { return Array.IndexOf(ReelControlDesign.StopOrders[stopOrder], reel); }
        private void RefreshContexts(bool selectFirst)
        {
            activeReel = ReelControlDesign.StopOrders[stopOrder][stopStage];
            contextError = null;
            try { reachable = design.ReachableContexts(stopOrder, stopStage); }
            catch (Exception ex) { reachable = new List<int[]>(); contextError = ex.Message; }
            if (selectFirst && reachable.Count > 0) priorPositions = (int[])reachable[0].Clone();
        }
        private bool CanSelectPrior(int reel, int position)
        {
            int stage = StageOf(reel);
            foreach (var context in reachable)
            {
                if (context[reel] != position) continue;
                bool matches = true;
                for (int s = 0; s < stage; s++)
                { int earlier = ReelControlDesign.StopOrders[stopOrder][s]; if (context[earlier] != priorPositions[earlier]) matches = false; }
                if (matches) return true;
            }
            return false;
        }
        private void SelectPrior(int reel, int position)
        {
            var preferred = (int[])priorPositions.Clone(); preferred[reel] = position;
            foreach (var context in reachable)
                if (context[0] == preferred[0] && context[1] == preferred[1] && context[2] == preferred[2])
                { priorPositions = preferred; lastPress = -1; return; }
            foreach (var context in reachable)
            {
                if (context[reel] != position) continue;
                bool matches = true;
                for (int s = 0; s < StageOf(reel); s++)
                { int earlier = ReelControlDesign.StopOrders[stopOrder][s]; if (context[earlier] != priorPositions[earlier]) matches = false; }
                if (!matches) continue;
                priorPositions = (int[])context.Clone(); lastPress = -1; return;
            }
        }
        private void SelectStage(int order, int stage)
        {
            int[] preferred = {-1, -1, -1};
            try
            {
                for (int s = 0; s < stage; s++)
                {
                    int reel = ReelControlDesign.StopOrders[order][s];
                    if (order == stopOrder && s < stopStage) preferred[reel] = priorPositions[reel];
                    else
                    {
                        int press = ReelControlDesign.Normalize(pressedPositions[reel], design.reels[reel].symbols.Length);
                        int slip = design.SlipsFor(order, s, preferred)[press];
                        preferred[reel] = ReelControlDesign.Normalize(press - Math.Max(0, slip), design.reels[reel].symbols.Length);
                    }
                }
            }
            catch (Exception) { preferred = new[] {-1, -1, -1}; }
            stopOrder = order; stopStage = stage; priorPositions = preferred; lastPress = -1;
            RefreshContexts(false);
            if (!ContextReachable && reachable.Count > 0) priorPositions = (int[])reachable[0].Clone();
        }

        [MenuItem("UdonSlot/リール制御設計")]
        public static void Open() { GetWindow<ReelControlDesignWindow>("リール制御設計"); }
        public static void ShowValidationIssue(ReelControlDesign source, ReelOutputController machine, ReelControlValidator.Issue issue)
        {
            var window = GetWindow<ReelControlDesignWindow>("リール制御設計");
            window.LoadDesign(source); window.controller = machine;
            window.editCommon = false;
            window.stopOrder = issue.order; window.stopStage = issue.stage;
            window.priorPositions = (int[])issue.context.Clone();
            window.pressedPositions = (int[])issue.presses.Clone();
            window.RefreshContexts(false); window.showTable = true;
            window.Show(); window.Repaint();
        }
        [UnityEditor.Callbacks.OnOpenAsset]
        public static bool OpenDesignAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId) as ReelControlDesign;
            if (asset == null) return false;
            GetWindow<ReelControlDesignWindow>("リール制御設計").LoadDesign(asset);
            return true;
        }
        public static void OpenFor(ReelControlData data)
        {
            var window = GetWindow<ReelControlDesignWindow>("リール制御設計");
            window.output = data;
            window.controller = data.GetComponentInParent<ReelOutputController>();
        }
        private void OnEnable()
        {
            minSize = new Vector2(950, 620);
            Undo.undoRedoPerformed += OnUndo;
            foreach (string guid in AssetDatabase.FindAssets("t:ReelPicture", new[] { "Assets", "Packages/com.axtuki1.udonslot" }))
            {
                var pic = AssetDatabase.LoadAssetAtPath<ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                if (pic != null && !string.IsNullOrEmpty(pic.pictureName)) pictures[pic.pictureName] = pic.picture;
            }
        }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndo; ClearComparison(); }
        private void OnUndo() { lastPress = -1; Repaint(); }
        private static string Id(UnityEngine.Object value)
        {
            return value == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        }
        private static T Resolve<T>(string id) where T : UnityEngine.Object
        {
            GlobalObjectId value;
            return GlobalObjectId.TryParse(id, out value) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(value) as T : null;
        }
        private bool ValidController()
        {
            if (controller == null || controller.reelDatas == null || controller.reelDatas.Length < 3) return false;
            foreach (var reel in controller.reelDatas)
                if (reel == null || reel.symbols == null || reel.symbols.Length == 0) return false;
            return true;
        }
        private void LoadDesign(ReelControlDesign next)
        {
            design = next;
            if (design != null)
            {
                controller = Resolve<ReelOutputController>(design.controllerId);
                output = Resolve<ReelControlData>(design.outputId);
            }
            lastPress = -1;
            pressedPositions = new[] {0, 0, 0};
            stopStage = 0; priorPositions = new[] {-1, -1, -1};
            editCommon = design != null && design.commonSettingsEnabled;
        }
        private void CreateDesign()
        {
            string path = EditorUtility.SaveFilePanelInProject("編集データを作成", "ReelControlDesign", "asset", "Editorフォルダーへの保存を推奨します。");
            if (string.IsNullOrEmpty(path)) return;
            var draft = CreateInstance<ReelControlDesign>();
            draft.Initialize(controller, output);
            draft.EnableCommonSettings();
            draft.definitionName = output != null ? output.name : "新しい停止定義";
            draft.controllerId = Id(controller);
            draft.outputId = Id(output);
            AssetDatabase.CreateAsset(draft, path);
            AssetDatabase.SaveAssetIfDirty(draft);
            design = draft;
            editCommon = true;
            pressedPositions = new[] {0, 0, 0};
            lastPress = -1;
            stopStage = 0; priorPositions = new[] {-1, -1, -1};
            message = "編集データを作成しました。ランタイムはデータ出力時に更新します。";
        }
        private int Count { get { return design.reels[activeReel].symbols.Length; } }
        private string Number(int index, int reel)
        {
            return ReelControlDesign.DisplayNumber(index, design.reels[reel].symbols.Length).ToString("00");
        }
        private string StopLabel(int press, int slip, int reel)
        {
            if (slip < 0) return "未設定";
            int target = ReelControlDesign.Normalize(press - slip, design.reels[reel].symbols.Length);
            return Number(target, reel) + "番 / " + slip + "コマ";
        }
        private void Assign(int press, int slip)
        {
            pressedPositions[activeReel] = press;
            if (!ContextReachable) return;
            int old = CurrentSlips[press];
            if (old == slip) return;
            Undo.RecordObject(design, "押下位置の停止先を変更");
            if (design.commonSettingsEnabled && stopStage == 0) design.SetCommonSlip(activeReel, press, slip);
            else design.SetSlipWithReview(stopOrder, stopStage, priorPositions, press, slip);
            EditorUtility.SetDirty(design);
            lastPress = press; lastOld = old; lastNew = slip; lastReel = activeReel;
        }
        private bool HasConcreteSelection
        {
            get
            {
                if (design == null || design.reels == null || design.reels.Length != 3) return false;
                foreach (var reel in design.reels)
                    if (reel == null || reel.symbols == null || reel.symbols.Length == 0 ||
                        reel.slips == null || reel.slips.Length != reel.symbols.Length) return false;
                return true;
            }
        }
        private void OnGUI()
        {
            if (design == null || !design.commonSettingsEnabled) editCommon = false;
            if (HasConcreteSelection && !design.laterStopsInitialized && stopStage > 0)
            { stopStage = 0; priorPositions = new[] {-1, -1, -1}; }
            if (HasConcreteSelection) RefreshContexts(false);
            if (showTable && HasConcreteSelection) FitTable();
            if (showComparison) FitComparison();
            // Floating windows must receive pointer input before controls underneath them.
            bool overOverlay = HasConcreteSelection && ValidController() &&
                ((showTable && tableRect.Contains(Event.current.mousePosition)) ||
                 (showIssues && issueRect.Contains(Event.current.mousePosition)) ||
                 (showComparison && comparisonRect.Contains(Event.current.mousePosition)));
            using (new EditorGUI.DisabledScope(overOverlay)) DrawMain();
            if (!HasConcreteSelection || !ValidController()) return;
            BeginWindows();
            if (showTable)
            {
                FitTable();
                tableRect = GUI.Window(101, tableRect, DrawTable, "滑り表 — " + ReelNames[activeReel] + " " + StageLabel);
            }
            if (showIssues) issueRect = GUILayout.Window(102, issueRect, DrawIssues, "検証一覧（編集状態）");
            if (showComparison) comparisonRect = GUI.Window(103, comparisonRect, DrawComparison, "他の停止定義との比較");
            EndWindows();
        }
        private void DrawMain()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!ValidController() || Application.isPlaying))
                    if (GUILayout.Button("出目ルール生成", EditorStyles.toolbarButton)) ReelOutcomeRulesWindow.OpenFor(controller);
                using (new EditorGUI.DisabledScope(!HasConcreteSelection || !ValidController() || Application.isPlaying))
                    if (GUILayout.Button("たたき台生成", EditorStyles.toolbarButton))
                        ReelControlGenerateWindow.Open(design, controller, stopOrder, stopStage, priorPositions, editCommon);
                using (new EditorGUI.DisabledScope(!HasConcreteSelection || !ValidController() || Application.isPlaying))
                    if (GUILayout.Button("入賞検証", EditorStyles.toolbarButton))
                        ReelControlGenerateWindow.OpenValidation(design, controller, stopOrder, stopStage, priorPositions);
                var next = (ReelControlDesign)EditorGUILayout.ObjectField(design, typeof(ReelControlDesign), false, GUILayout.Width(220));
                if (next != design) LoadDesign(next);
                using (new EditorGUI.DisabledScope(!ValidController() || Application.isPlaying))
                    if (GUILayout.Button("新規編集データ", EditorStyles.toolbarButton)) CreateDesign();
                using (new EditorGUI.DisabledScope(design == null))
                    if (GUILayout.Button("保存", EditorStyles.toolbarButton))
                    {
                        string controllerKey = Id(controller), outputKey = Id(output);
                        if (design.controllerId != controllerKey || design.outputId != outputKey)
                        {
                            Undo.RecordObject(design, "編集データの参照先を保存");
                            design.controllerId = controllerKey; design.outputId = outputKey;
                            EditorUtility.SetDirty(design);
                        }
                        AssetDatabase.SaveAssetIfDirty(design);
                    }
                GUILayout.FlexibleSpace();
                showTable = GUILayout.Toggle(showTable, "滑り表", EditorStyles.toolbarButton);
                showIssues = GUILayout.Toggle(showIssues, "検証一覧", EditorStyles.toolbarButton);
                bool compare = GUILayout.Toggle(showComparison, "他の定義", EditorStyles.toolbarButton);
                if (compare != showComparison)
                {
                    showComparison = compare;
                    if (compare) RefreshComparison();
                    else ClearComparison();
                }
            }
            controller = (ReelOutputController)EditorGUILayout.ObjectField("参照する機種", controller, typeof(ReelOutputController), true);
            output = (ReelControlData)EditorGUILayout.ObjectField("出力先", output, typeof(ReelControlData), true);
            if (design == null)
            {
                EditorGUILayout.HelpBox("機種と出力先を選び「新規編集データ」で開始します。有効な第1停止テーブルを取り込み、それ以外は未設定で開始します。既存の編集アセットを上の欄で開くこともできます。", MessageType.Info);
                return;
            }
            if (!ValidController() || !HasConcreteSelection)
            { EditorGUILayout.HelpBox("3本のリール配列を持つ機種を指定してください。", MessageType.Warning); return; }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.TextField("停止定義", design.definitionName);
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(design, "停止定義名を変更"); design.definitionName = name; EditorUtility.SetDirty(design); }
                }
                if (!design.laterStopsInitialized)
                {
                    EditorGUILayout.HelpBox("第2・第3停止は出力先から取り込んで開始します。編集中の第1停止は保持します。", MessageType.Info);
                    if (GUILayout.Button("第2・第3停止を取り込む"))
                    {
                        try { Undo.RecordObject(design, "後段の制御を取り込む"); design.InitializeLaterStops(output); EditorUtility.SetDirty(design); }
                        catch (Exception ex) { message = ex.Message; }
                    }
                }
                DrawCommonSettingsControls();
                using (new EditorGUILayout.HorizontalScope())
                {
                    int order = stopOrder;
                    if (editCommon)
                    {
                        int reel = GUILayout.Toolbar(activeReel, new[] { "左リール共通", "中リール共通", "右リール共通" });
                        order = reel * 2;
                    }
                    else order = EditorGUILayout.Popup("停止順", stopOrder, OrderNames);
                    int stage;
                    using (new EditorGUI.DisabledScope(!design.laterStopsInitialized || editCommon))
                        stage = GUILayout.Toolbar(stopStage, new[] { "第1停止", "第2停止", "第3停止" }, GUILayout.Width(280));
                    if (editCommon) stage = 0;
                    if (order != stopOrder || stage != stopStage)
                        SelectStage(order, stage);
                }
                bool matches = true;
                for (int r = 0; r < 3; r++) matches &= design.Matches(controller, r);
                if (stopStage > 0)
                {
                    string condition = "停止済み: ";
                    for (int s = 0; s < stopStage; s++)
                    { int r = ReelControlDesign.StopOrders[stopOrder][s]; condition += ReelNames[r] + " " + (priorPositions[r] < 0 ? "未選択" : Number(priorPositions[r], r) + "番") + "  "; }
                    GUILayout.Label(condition + "（リール上で選択）", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("到達可能な条件 " + reachable.Count + "件。前段の未設定セルは暫定0コマとして判定します。", EditorStyles.wordWrappedMiniLabel);
                    bool priorOutputMatches = true;
                    for (int s = 0; s < stopStage; s++) priorOutputMatches &= StageOutputMatches(s);
                    if (!priorOutputMatches) EditorGUILayout.HelpBox("前段の編集内容が出力先と一致していることを確認できません。実機で同じ停止条件を試す際は、前段も順にデータ出力してください。", MessageType.Info);
                    var selectedTable = design.LaterTable(CurrentPattern);
                    bool individual = false;
                    foreach (var conditionRow in selectedTable.conditions) if (conditionRow.Exact(priorPositions)) { individual = true; break; }
                    EditorGUILayout.LabelField(individual ? "この停止条件の個別設定を編集中" : "初期テーブルを参照中。滑り値を変更すると、この条件の個別設定を作成します。", EditorStyles.wordWrappedMiniLabel);
                }
                if (!ContextReachable)
                    EditorGUILayout.HelpBox(contextError ?? "前段の変更でこの条件は到達不能になりました。設定は保持しています。停止済みリールの選択可能な位置を選び直してください。", MessageType.Warning);
                if (design.PendingReviewCount > 0)
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        GUILayout.Label("要確認 " + design.PendingReviewCount + "件（未確認でも暫定出力できます）");
                        if (GUILayout.Button("確認一覧", GUILayout.Width(80))) showIssues = true;
                    }
                }
                int unset = 0;
                foreach (int slip in CurrentSlips) if (slip < 0) unset++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool exported = stopStage == 0 && design.exportedSignatures != null && design.exportedSignatures.Length > activeReel &&
                        design.exportedSignatures[activeReel] == design.Signature(activeReel) && design.OutputMatches(output, activeReel);
                    if (stopStage > 0)
                    {
                        var table = design.LaterTable(CurrentPattern);
                        try { exported = table.exportedDesign == design.LaterSignature(CurrentPattern) && table.exportedOutput == ReelControlDesign.OutputSignature(output, CurrentPattern); }
                        catch (Exception) { exported = false; }
                    }
                    if (editCommon) exported = CommonOutputMatches();
                    GUILayout.Label((EditorUtility.IsDirty(design) ? "未保存" : "保存済み") + " ｜ " + (exported ? "出力済み" : "未出力／出力先に差分") + " ｜ 未設定 " + unset);
                    using (new EditorGUI.DisabledScope(!matches || output == null))
                        if (GUILayout.Button(editCommon ? "全押し順・全段階を出力" : "この停止順・段階を出力", GUILayout.Width(190))) Export(editCommon);
                }
                if (!matches)
                {
                    EditorGUILayout.HelpBox("参照リールの配列が変わっています。旧位置と新位置の対応を指定して、編集データを移行できます。", MessageType.Warning);
                    if (GUILayout.Button("配列変更の後処理（対応付け・移行）"))
                        ReelArrangementMigrationWindow.Open(design, controller);
                }
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.LabelField(message, EditorStyles.wordWrappedMiniLabel);
                DrawSharedWarning();
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int r = 0; r < 3; r++) DrawStrip(r);
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(280)))
                    {
                        assignmentScroll = EditorGUILayout.BeginScrollView(assignmentScroll);
                        using (new EditorGUI.DisabledScope(!ContextReachable)) DrawAssignment();
                        EditorGUILayout.EndScrollView();
                    }
                }
            }
        }
        private void DrawSharedWarning()
        {
            if (output == null || controller == null) return;
            var property = new SerializedObject(controller).FindProperty("reelControl");
            var mapping = property != null ? property.objectReferenceValue as ReelControl : null;
            if (mapping == null || mapping.flagNames == null || mapping.reelControlDataList == null) return;
            var names = new List<string>();
            for (int i = 0; i < Math.Min(mapping.flagNames.Length, mapping.reelControlDataList.Length); i++)
                if (mapping.reelControlDataList[i] == output) names.Add(mapping.flagNames[i]);
            if (names.Count > 1) EditorGUILayout.HelpBox("共有出力先: " + string.Join("、", names) + "。データ出力はこれらすべてに反映されます。", MessageType.Info);
        }
        private void DrawPicture(Rect rect, string symbol)
        {
            Texture2D texture;
            if (pictures.TryGetValue(symbol, out texture) && texture != null) ReelPicturePreview.Draw(rect, texture);
            else GUI.Label(rect, symbol, EditorStyles.centeredGreyMiniLabel);
        }
        private void DrawStrip(int reel)
        {
            var row = design.reels[reel];
            pressedPositions[reel] = ReelControlDesign.Normalize(pressedPositions[reel], row.symbols.Length);
            int selectedSlip = reel == activeReel ? CurrentSlips[pressedPositions[reel]] : -1;
            int result = selectedSlip < 0 ? -1 : ReelControlDesign.Normalize(pressedPositions[reel] - selectedSlip, row.symbols.Length);
            bool prior = StageOf(reel) < stopStage;
            bool[] destinations = null;
            if (reel == activeReel)
            {
                destinations = new bool[row.symbols.Length];
                int[] slips = CurrentSlips;
                for (int p = 0; p < slips.Length; p++)
                {
                    // Use the same provisional zero as reachability and runtime export.
                    if (slips[p] < -1 || slips[p] > 4) continue;
                    destinations[ReelControlDesign.Normalize(p - Math.Max(0, slips[p]), row.symbols.Length)] = true;
                }
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
            {
                GUILayout.Label(ReelNames[reel] + (reel == activeReel ? "  ● 編集中" : prior ? "  停止済み" : "  未停止"), EditorStyles.boldLabel);
                if (reel == activeReel) GUILayout.Label("クリックで押下時の下段位置を選択", EditorStyles.miniLabel);
                else GUILayout.Label(editCommon ? "上のリール切替で共通設定を編集" : prior ? "到達可能な下段停止位置を選択" : "停止順・段階で編集対象を切替", EditorStyles.miniLabel);
                stripScroll[reel] = EditorGUILayout.BeginScrollView(stripScroll[reel]);
                for (int i = 0; i < row.symbols.Length; i++)
                {
                    Rect rect = GUILayoutUtility.GetRect(100, 44, GUILayout.ExpandWidth(true));
                    int delta = ReelControlDesign.Normalize(pressedPositions[reel] - i, row.symbols.Length);
                    if (reel == activeReel && delta <= 2) EditorGUI.DrawRect(rect, new Color(.1f, .6f, .7f, .22f));
                    if (prior && priorPositions[reel] >= 0 && ReelControlDesign.Normalize(priorPositions[reel] - i, row.symbols.Length) <= 2)
                        EditorGUI.DrawRect(rect, new Color(1, .65f, .15f, .22f));
                    bool selectable = prior ? CanSelectPrior(reel, i) : reel == activeReel;
                    using (new EditorGUI.DisabledScope(!selectable))
                        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                        { if (prior) SelectPrior(reel, i); else pressedPositions[reel] = i; }
                    GUI.Label(new Rect(rect.x, rect.y + 12, 30, 22), Number(i, reel));
                    DrawPicture(new Rect(rect.x + 32, rect.y + 2, Math.Max(30, rect.width - 62), 40), row.symbols[i]);
                    if ((prior && !selectable) || (destinations != null && !destinations[i]))
                        EditorGUI.DrawRect(rect, new Color(0, 0, 0, .5f));
                    // Keep the pressed-position and result markers visible even on a dimmed row.
                    if (reel == activeReel && i == pressedPositions[reel])
                        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 3, rect.width, 3), Color.cyan);
                    if (reel == activeReel && result >= 0 && ReelControlDesign.Normalize(result - i, row.symbols.Length) <= 2)
                        EditorGUI.DrawRect(new Rect(rect.xMax - 4, rect.y, 4, rect.height), new Color(1, .65f, .15f));
                    if (prior && i == priorPositions[reel])
                    { EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 3, rect.width, 3), new Color(1, .65f, .15f)); GUI.Label(new Rect(rect.xMax - 32, rect.y, 32, 18), "条件", EditorStyles.miniLabel); }
                    if (reel == activeReel && i == pressedPositions[reel])
                        GUI.Label(new Rect(rect.xMax - 32, rect.y, 32, 18), "押下", EditorStyles.miniLabel);
                    if (reel == activeReel && i == result)
                        GUI.Label(new Rect(rect.xMax - 32, rect.y + 22, 32, 18), "停止", EditorStyles.miniLabel);
                    if (!compareSideBySide && reel == lastReel && lastPress >= 0)
                    {
                        if (i == lastPress) GUI.Label(new Rect(rect.x + 28, rect.y, 65, 18), "押下", EditorStyles.miniLabel);
                        int oldStop = lastOld < 0 ? -1 : ReelControlDesign.Normalize(lastPress - lastOld, row.symbols.Length);
                        int newStop = lastNew < 0 ? -1 : ReelControlDesign.Normalize(lastPress - lastNew, row.symbols.Length);
                        if (i == oldStop) GUI.Label(new Rect(rect.x + 28, rect.y + 25, 65, 18), "旧 →", EditorStyles.miniLabel);
                        if (i == newStop) GUI.Label(new Rect(rect.xMax - 55, rect.y + 25, 55, 18), "→ 新", EditorStyles.miniBoldLabel);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }
        private void DrawAssignment()
        {
            if (design.commonSettingsEnabled)
            {
                bool common = design.UsesCommon(stopOrder, stopStage, priorPositions);
                GUILayout.Label(editCommon || stopStage == 0 ? "全押し順の共通設定を編集" : common ? "共通設定を使用中（変更すると個別設定）" : "この条件の個別設定を編集中", EditorStyles.wordWrappedMiniLabel);
                if (!editCommon && stopStage > 0 && !common && GUILayout.Button("この条件を共通設定に戻す"))
                {
                    Undo.RecordObject(design, "共通設定を使用");
                    design.UseCommonForContext(stopOrder, stopStage, priorPositions);
                    EditorUtility.SetDirty(design); lastPress = -1;
                }
            }
            GUILayout.Label("押下時の下段位置を選択", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("↑ 1コマ")) pressedPositions[activeReel] = ReelControlDesign.Normalize(pressedPositions[activeReel] - 1, Count);
                if (GUILayout.Button("↓ 1コマ")) pressedPositions[activeReel] = ReelControlDesign.Normalize(pressedPositions[activeReel] + 1, Count);
            }
            int press = pressedPositions[activeReel];
            int current = CurrentSlips[press];
            GUILayout.Label(Number(press, activeReel) + "番押下 → 滑りコマ数", EditorStyles.boldLabel);
            int choice = GUILayout.Toolbar(current + 1, new[] { "未設定", "0", "1", "2", "3", "4" }, EditorStyles.miniButton, GUILayout.Height(28)) - 1;
            if (choice != current) { Assign(press, choice); current = choice; }
            GUILayout.Label(current < 0 ? "未設定（出力時は暫定0コマ）" : StopLabel(press, current, activeReel) + (current == 0 ? "（ビタ）" : ""));
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawShapePreview("押下時", press);
                DrawShapePreview("停止後", current < 0 ? -1 : ReelControlDesign.Normalize(press - current, Count));
            }
            GUILayout.Label("水色: 押下時の3コマ\n橙: 停止後の3コマ\n暗い行: 下段停止先にならない位置\n（押下位置としては編集可／未設定は暫定0）", EditorStyles.miniLabel);
            GUILayout.Space(8);
            compareSideBySide = GUILayout.Toolbar(compareSideBySide ? 1 : 0, new[] { "差分A：重ね表示", "差分B：前後表示" }) == 1;
            if (lastPress >= 0 && lastReel == activeReel)
            {
                GUILayout.Label(Number(lastPress, activeReel) + "番押下の変更", EditorStyles.boldLabel);
                if (compareSideBySide)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Box("変更前\n" + StopLabel(lastPress, lastOld, activeReel), GUILayout.ExpandWidth(true));
                        GUILayout.Box("変更後\n" + StopLabel(lastPress, lastNew, activeReel), GUILayout.ExpandWidth(true));
                    }
                }
                else GUILayout.Label(StopLabel(lastPress, lastOld, activeReel) + " → " + StopLabel(lastPress, lastNew, activeReel));
            }
            GUILayout.Space(8);
            GUILayout.Label("メモ", EditorStyles.boldLabel);
            string notes = EditorGUILayout.TextArea(design.notes, GUILayout.Height(60));
            if (notes != design.notes) { Undo.RecordObject(design, "設計メモを変更"); design.notes = notes; EditorUtility.SetDirty(design); }
            DrawStopWindowPreview();
        }
        private void DrawStopWindowPreview()
        {
            GUILayout.Space(8);
            GUILayout.Label(editCommon ? "共通設定の停止形（例外適用前）" : "停止形プレビュー", EditorStyles.boldLabel);
            const float rowHeight = 44f, peek = .35f, gap = 4f;
            Rect panel = GUILayoutUtility.GetRect(100, rowHeight * (3 + 2 * peek) + 30,
                GUILayout.ExpandWidth(true));
            float width = (panel.width - 2 * gap) / 3;
            var orange = new Color(1, .65f, .15f);
            var anyStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            anyStyle.normal.textColor = new Color(.8f, .12f, .1f);
            for (int reel = 0; reel < 3; reel++)
            {
                int lower = -1;
                bool editing = reel == activeReel;
                bool prior = StageOf(reel) < stopStage;
                if (prior) lower = priorPositions[reel];
                else if (editing || editCommon)
                {
                    int press = ReelControlDesign.Normalize(pressedPositions[reel], design.reels[reel].symbols.Length);
                    int slip = editCommon ? design.reels[reel].slips[press] : CurrentSlips[press];
                    if (slip >= 0 && slip <= 4) lower = ReelControlDesign.Normalize(press - slip, design.reels[reel].symbols.Length);
                }
                Rect window = new Rect(panel.x + reel * (width + gap), panel.y, width, rowHeight * (3 + 2 * peek));
                EditorGUI.DrawRect(window, new Color(.12f, .15f, .17f));
                Rect inside = new Rect(window.x + 2, window.y + 2, window.width - 4, window.height - 4);
                GUI.BeginGroup(inside);
                EditorGUI.DrawRect(new Rect(0, 0, inside.width, inside.height), new Color(.94f, .94f, .91f));
                if (lower >= 0)
                {
                    var symbols = design.reels[reel].symbols;
                    // Draw complete symbol cells, then clip at the aperture edges: never squash the fringe symbols.
                    for (int row = -1; row <= 3; row++)
                    {
                        int index = ReelControlDesign.Normalize(lower + row - 2, symbols.Length);
                        DrawPicture(new Rect(3, (peek + row) * rowHeight, inside.width - 6, rowHeight), symbols[index]);
                    }
                    float top = peek * rowHeight, bottom = (peek + 3) * rowHeight;
                    EditorGUI.DrawRect(new Rect(0, 0, inside.width, top), new Color(0, 0, 0, .16f));
                    EditorGUI.DrawRect(new Rect(0, bottom, inside.width, inside.height - bottom), new Color(0, 0, 0, .16f));
                    EditorGUI.DrawRect(new Rect(0, top, inside.width, 1), orange);
                    EditorGUI.DrawRect(new Rect(0, bottom - 1, inside.width, 1), orange);
                }
                else if (!editing && !editCommon)
                {
                    for (int row = 0; row < 3; row++)
                        GUI.Label(new Rect(0, (peek + row) * rowHeight, inside.width, rowHeight), new[] {"A", "N", "Y"}[row], anyStyle);
                }
                else GUI.Label(new Rect(0, 0, inside.width, inside.height), "未設定", EditorStyles.centeredGreyMiniLabel);
                GUI.EndGroup();
                string label = ReelNames[reel] + (lower >= 0 ? " " + Number(lower, reel) + "番" : editing || editCommon ? " 未設定" : " 任意");
                GUI.Label(new Rect(window.x, window.yMax + 2, width, 24), label, EditorStyles.centeredGreyMiniLabel);
            }
        }
        private void DrawShapePreview(string title, int lower)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(122)))
            {
                GUILayout.Label(title, EditorStyles.boldLabel);
                for (int row = 0; row < 3; row++)
                {
                    Rect rect = GUILayoutUtility.GetRect(90, 44, GUILayout.ExpandWidth(true));
                    if (lower < 0) { GUI.Label(rect, "未設定", EditorStyles.centeredGreyMiniLabel); continue; }
                    int index = ReelControlDesign.Normalize(lower + row - 2, Count);
                    GUI.Label(new Rect(rect.x, rect.y + 10, 28, 22), Number(index, activeReel));
                    DrawPicture(new Rect(rect.x + 30, rect.y, rect.width - 30, 44), design.reels[activeReel].symbols[index]);
                }
            }
        }
        private void FitTable()
        {
            // Keep every symbol visible. Long strips continue in the next column
            // instead of shrinking the text or hiding rows behind a scrollbar.
            float availableHeight = position.height - 46;
            int capacity = Math.Max(1, Mathf.FloorToInt((availableHeight - 78) / 22));
            int columns = Mathf.CeilToInt((float)Count / capacity);
            tableRows = Mathf.CeilToInt((float)Count / columns);
            tableRowHeight = Mathf.Min(32, (availableHeight - 78) / tableRows);
            tableRect.width = columns * 330 + 16;
            tableRect.height = tableRows * tableRowHeight + 78;
            tableRect.x = Mathf.Clamp(tableRect.x, 0, Mathf.Max(0, position.width - tableRect.width));
            tableRect.y = Mathf.Clamp(tableRect.y, 24, Mathf.Max(24, position.height - tableRect.height));
        }
        private void DrawTable(int id)
        {
            if (GUI.Button(new Rect(tableRect.width - 65, 21, 57, 20), "閉じる")) showTable = false;
            int columns = Mathf.CeilToInt((float)Count / tableRows);
            for (int column = 0; column < columns; column++)
            {
                float x = 8 + column * 330;
                GUI.Label(new Rect(x, 34, 32, 20), "番号", EditorStyles.miniLabel);
                GUI.Label(new Rect(x + 36, 34, 64, 20), "リール配列", EditorStyles.miniLabel);
                GUI.Label(new Rect(x + 108, 34, 85, 20), "滑りコマ数", EditorStyles.miniLabel);
                GUI.Label(new Rect(x + 204, 34, 110, 20), "停止先", EditorStyles.miniLabel);
            }
            for (int p = 0; p < Count; p++)
            {
                float x = 8 + (p / tableRows) * 330;
                float y = 56 + (p % tableRows) * tableRowHeight;
                int current = CurrentSlips[p];
                Color tint = p == pressedPositions[activeReel] ? new Color(.1f, .6f, .7f, .25f) :
                    (p % 2 == 0 ? new Color(1, 1, 1, .045f) : Color.clear);
                EditorGUI.DrawRect(new Rect(x, y, 320, tableRowHeight), tint);
                EditorGUI.DrawRect(new Rect(x, y + tableRowHeight - 1, 320, 1), new Color(1, 1, 1, .12f));
                float controlY = y + (tableRowHeight - 18) / 2;
                GUI.Label(new Rect(x, controlY, 32, 18), Number(p, activeReel));
                var pictureRect = new Rect(x + 34, y + 1, 66, tableRowHeight - 2);
                if (GUI.Button(pictureRect, new GUIContent("", "押下時にこの図柄が下段にある位置を選択"), GUIStyle.none)) pressedPositions[activeReel] = p;
                DrawPicture(pictureRect, design.reels[activeReel].symbols[p]);
                if (p == pressedPositions[activeReel]) EditorGUI.DrawRect(new Rect(x + 34, y + tableRowHeight - 2, 66, 2), Color.cyan);
                using (new EditorGUI.DisabledScope(Application.isPlaying || !ContextReachable))
                {
                    int slip = EditorGUI.IntField(new Rect(x + 108, controlY, 38, 18), current);
                    if (slip != current) Assign(p, Mathf.Clamp(slip, -1, 4));
                    if (GUI.Button(new Rect(x + 150, controlY, 20, 18), "−")) Assign(p, Math.Max(-1, CurrentSlips[p] - 1));
                    if (GUI.Button(new Rect(x + 174, controlY, 20, 18), "+")) Assign(p, Math.Min(4, CurrentSlips[p] + 1));
                }
                current = CurrentSlips[p];
                string destination = current < 0 ? "未設定" : (current == 0 ? "● " : "↑ ") + Number(ReelControlDesign.Normalize(p - current, Count), activeReel) + "番";
                if (GUI.Button(new Rect(x + 204, controlY, 110, 18), new GUIContent(destination, "この押下位置の停止結果を確認")))
                    pressedPositions[activeReel] = p;
            }
            GUI.Label(new Rect(8, tableRect.height - 20, tableRect.width - 16, 18), "● ビタ  ↑ 滑り  ｜  -1: 未設定（出力時は暫定0コマ）", EditorStyles.miniLabel);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
        private void DrawIssues(int id)
        {
            if (GUILayout.Button("閉じる")) showIssues = false;
            GUILayout.Label("入賞・配当の検証は後続段階で追加します。", EditorStyles.wordWrappedMiniLabel);
            issueScroll = EditorGUILayout.BeginScrollView(issueScroll, GUILayout.Height(170));
            if (design.reviews != null) foreach (var review in design.reviews)
            {
                if (review.confirmed) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string label = OrderNames[review.order] + " 第" + (review.stage + 1) + "停止 / ";
                    for (int r = 0; r < 3; r++) if (review.positions[r] >= 0) label += ReelNames[r] + Number(review.positions[r], r) + "番 ";
                    GUILayout.Label(label, EditorStyles.wordWrappedLabel);
                    GUILayout.Label(review.reason, EditorStyles.wordWrappedLabel);
                    if (review.fromPositions != null)
                    {
                        string origin = "引き継ぎ元: ";
                        for (int r = 0; r < 3; r++) if (review.fromPositions[r] >= 0) origin += ReelNames[r] + Number(review.fromPositions[r], r) + "番 ";
                        GUILayout.Label(origin, EditorStyles.wordWrappedMiniLabel);
                        bool expanded = expandedReviews.Contains(review);
                        bool next = EditorGUILayout.Foldout(expanded, "補完元と現在の滑り値（押下位置順）", true);
                        if (next) expandedReviews.Add(review); else expandedReviews.Remove(review);
                        if (next && review.previousSlips != null)
                        {
                            int reel = ReelControlDesign.StopOrders[review.order][review.stage];
                            var currentValues = design.SlipsFor(review.order, review.stage, review.positions);
                            for (int p = 0; p < review.previousSlips.Length && p < currentValues.Length; p++)
                                GUILayout.Label(Number(p, reel) + "番: 元 " + review.previousSlips[p] + " → 現在 " + currentValues[p] + " コマ", EditorStyles.miniLabel);
                        }
                    }
                    bool canReach = design.IsReachable(review.order, review.stage, review.positions);
                    if (!canReach) GUILayout.Label("現在は到達不能。記録と設定を保持しています。", EditorStyles.wordWrappedMiniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("この条件を表示"))
                        {
                            editCommon = false;
                            stopOrder = review.order; stopStage = review.stage;
                            priorPositions = (int[])review.positions.Clone(); RefreshContexts(false);
                            lastPress = -1; showTable = true;
                        }
                        if (GUILayout.Button("確認済みにする"))
                        {
                            Undo.RecordObject(design, "補完した停止条件を確認");
                            review.confirmed = true; EditorUtility.SetDirty(design);
                        }
                    }
                }
            }
            for (int r = 0; r < 3; r++)
            {
                if (!design.Matches(controller, r)) GUILayout.Label(ReelNames[r] + ": 配列変更・出力前に対応付けが必要", EditorStyles.wordWrappedLabel);
                for (int p = 0; p < design.reels[r].slips.Length; p++)
                {
                    int slip = design.reels[r].slips[p];
                    if (slip >= 0 && slip <= 4) continue;
                    if (GUILayout.Button(ReelNames[r] + " " + Number(p, r) + "番: " + (slip == -1 ? "未設定／暫定0コマ" : "滑り値が範囲外")))
                    { stopOrder = r * 2; stopStage = 0; RefreshContexts(true); pressedPositions[r] = p; lastPress = -1; showTable = true; }
                }
            }
            if (stopStage > 0)
            {
                GUILayout.Label("現在の停止順・段階", EditorStyles.boldLabel);
                var table = design.LaterTable(CurrentPattern);
                foreach (var condition in table.conditions)
                {
                    int[] positions = {condition.left, condition.center, condition.right};
                    bool exact = true;
                    for (int s = 0; s < stopStage; s++) if (positions[ReelControlDesign.StopOrders[stopOrder][s]] < 0) exact = false;
                    if (!exact) continue;
                    bool reachableCondition = false;
                    foreach (var context in reachable) if (condition.Exact(context)) { reachableCondition = true; break; }
                    int unset = 0;
                    foreach (int slip in design.ConditionValues(design.LaterTable(CurrentPattern), condition)) if (slip < 0 || slip > 4) unset++;
                    if (reachableCondition && unset == 0) continue;
                    string label = "";
                    for (int s = 0; s < stopStage; s++) { int r = ReelControlDesign.StopOrders[stopOrder][s]; label += ReelNames[r] + Number(positions[r], r) + "番 "; }
                    if (GUILayout.Button(label + (reachableCondition ? "未設定／範囲外 " + unset : "到達不能・設定保持")))
                    { priorPositions = positions; lastPress = -1; showTable = true; }
                }
                for (int p = 0; p < CurrentSlips.Length; p++)
                    if (CurrentSlips[p] < 0 || CurrentSlips[p] > 4)
                        if (GUILayout.Button("選択条件 " + Number(p, activeReel) + "番: 未設定／範囲外")) { pressedPositions[activeReel] = p; showTable = true; }
            }
            EditorGUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
        private void Export(bool all = false)
        {
            try
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.RecordObjects(new UnityEngine.Object[] { output, design }, "停止データを出力");
                if (all) design.ApplyAllOutput(output);
                else if (stopStage == 0) design.ApplyOutput(output, activeReel);
                else design.ApplyLaterOutput(output, stopOrder, stopStage);
                design.controllerId = Id(controller); design.outputId = Id(output);
                if (design.exportedSignatures == null || design.exportedSignatures.Length != 3) design.exportedSignatures = new string[3];
                if (all)
                {
                    for (int r = 0; r < 3; r++) design.exportedSignatures[r] = design.Signature(r);
                    foreach (var table in design.laterStops)
                    {
                        table.exportedDesign = design.LaterSignature(table.pattern);
                        table.exportedOutput = ReelControlDesign.OutputSignature(output, table.pattern);
                    }
                }
                else if (stopStage == 0) design.exportedSignatures[activeReel] = design.Signature(activeReel);
                else
                {
                    var table = design.LaterTable(CurrentPattern);
                    table.exportedDesign = design.LaterSignature(CurrentPattern);
                    table.exportedOutput = ReelControlDesign.OutputSignature(output, CurrentPattern);
                }
                EditorUtility.SetDirty(output); EditorUtility.SetDirty(design);
                PrefabUtility.RecordPrefabInstancePropertyModifications(output);
                if (output.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(output.gameObject.scene);
                AssetDatabase.SaveAssetIfDirty(design);
                Undo.CollapseUndoOperations(group);
                message = (all ? "全押し順・全段階" : OrderNames[stopOrder] + "の" + StageLabel + "（全条件）") + "を出力しました。未設定は暫定0コマです。出力先のシーン／Prefabも保存してください。";
            }
            catch (Exception ex) { message = "出力できませんでした: " + ex.Message; }
        }
    }
}


