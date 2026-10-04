using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using AX.UdonSlot.REV1_0.Serialize;

namespace AX.UdonSlot.REV1_0.Editor
{
    public sealed partial class ReelOutcomeRulesWindow : EditorWindow
    {
        [SerializeField] private ReelOutcomeRules book;
        [SerializeField] private ReelOutputController controller;
        [SerializeField] private PayoutTable payout;
        [SerializeField] private int selectedShape, selectedTarget, order;
        [SerializeField] private int[] presses = {0,0,0};
        private Vector2 scroll, resultScroll;
        private string message;
        private readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();
        private List<Trial> trials = new List<Trial>(), previous = new List<Trial>();
        private static readonly string[] Names = {"左", "中", "右"};
        private static readonly string[] Orders = {"左→中→右", "左→右→中", "中→左→右", "中→右→左", "右→左→中", "右→中→左"};
        private static readonly string[] Modes = {"禁止", "許可", "優先して狙う"};
        private sealed class Trial
        {
            public string id, name, signature, rulesJson, summary;
            public ReelControlDesign design;
            public readonly List<string> issues = new List<string>();
            public readonly List<Sample> samples = new List<Sample>();
        }
        private sealed class Sample { public string label; public int order; public int[] presses; }
        [MenuItem("UdonSlot/出目ルールからテーブル生成")]
        public static void Open() { GetWindow<ReelOutcomeRulesWindow>("出目ルール生成").Show(); }
        [UnityEditor.Callbacks.OnOpenAsset]
        public static bool OpenAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId) as ReelOutcomeRules;
            if (asset == null) return false;
            var window = GetWindow<ReelOutcomeRulesWindow>("出目ルール生成");
            window.LoadBook(asset); window.Show(); return true;
        }
        private void LoadBook(ReelOutcomeRules value)
        {
            book = value; selectedShape = selectedTarget = 0;
            if (book == null) return;
            GlobalObjectId id;
            if (GlobalObjectId.TryParse(book.controllerId,out id))
            {
                var machine = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as ReelOutputController;
                if (machine != null) SetController(machine);
            }
        }
        public static void OpenFor(ReelOutputController machine)
        {
            var w = GetWindow<ReelOutcomeRulesWindow>("出目ルール生成");
            w.SetController(machine); w.Show();
        }
        private void OnEnable()
        {
            minSize = new Vector2(900,650);
            Undo.undoRedoPerformed += Repaint;
            foreach (string guid in AssetDatabase.FindAssets("t:ReelPicture", new[] {"Assets", "Packages/com.axtuki1.udonslot"}))
            {
                var p = AssetDatabase.LoadAssetAtPath<ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && !string.IsNullOrEmpty(p.pictureName)) pictures[p.pictureName] = p.picture;
            }
        }
        private static void Dispose(List<Trial> list)
        { foreach (var t in list) if (t.design != null) DestroyImmediate(t.design); list.Clear(); }
        private void OnDisable() { Undo.undoRedoPerformed -= Repaint; Dispose(trials); Dispose(previous); }
        private void SetController(ReelOutputController value)
        {
            controller = value;
            payout = value == null ? null : new SerializedObject(value).FindProperty("payoutTable").objectReferenceValue as PayoutTable;
        }
        private bool ValidMachine()
        {
            if (controller == null || controller.reelDatas == null || controller.reelDatas.Length != 3) return false;
            for (int r = 0; r < 3; r++) if (controller.reelDatas[r] == null || controller.reelDatas[r].symbols == null || controller.reelDatas[r].symbols.Length == 0) return false;
            return true;
        }
        private bool ArrangementMatches()
        {
            if (!ValidMachine() || book == null || book.reels.Length != 3) return false;
            for (int r = 0; r < 3; r++)
            {
                var a = book.reels[r].symbols; var b = controller.reelDatas[r].symbols;
                if (a.Length != b.Length) return false;
                for (int p = 0; p < a.Length; p++) if (a[p] != b[p]) return false;
            }
            return true;
        }
        private void NewBook()
        {
            string path = EditorUtility.SaveFilePanelInProject("出目ルール集を作成", "ReelOutcomeRules", "asset", "Editorフォルダー内に保存してください。");
            if (string.IsNullOrEmpty(path)) return;
            var next = CreateInstance<ReelOutcomeRules>();
            next.controllerId = GlobalObjectId.GetGlobalObjectIdSlow(controller).ToString();
            next.reels = new ReelControlDesign.FirstStop[3];
            for (int r = 0; r < 3; r++) next.reels[r] = new ReelControlDesign.FirstStop { symbols = (string[])controller.reelDatas[r].symbols.Clone() };
            next.targets.Add(new ReelOutcomeRules.Target { name = "ハズレ_ボーナス非成立" });
            AssetDatabase.CreateAsset(next, AssetDatabase.GenerateUniqueAssetPath(path));
            book = next; selectedShape = selectedTarget = 0;
        }
        private string Signature()
        {
            if (!ValidMachine() || book == null || payout == null) return "";
            var s = new StringBuilder(EditorJsonUtility.ToJson(book));
            s.Append('|').Append(controller.GetInstanceID()).Append('|').Append(EditorJsonUtility.ToJson(payout));
            for (int r = 0; r < 3; r++) s.Append('|').Append(EditorJsonUtility.ToJson(controller.reelDatas[r]));
            var settings = new SerializedObject(controller);
            s.Append('|').Append(settings.FindProperty("priority").intValue).Append('|').Append(settings.FindProperty("rolePriority").intValue);
            return s.ToString();
        }
        private void ValidatePayout()
        {
            if (payout == null) throw new InvalidOperationException("配当表を指定してください。");
            for (int p = 0; p < payout.GetWinPairCount(); p++)
                if (!payout.IsWinPairValid(p)) throw new InvalidOperationException("配当表の役定義が不正: " + p);
            int active = 0;
            for (int l = 0; l < payout.GetPayLineCount(); l++)
            {
                if (!payout.IsPayLineValid(l)) throw new InvalidOperationException("配当表のライン定義が不正: " + l);
                for (int r = 0; r < 3; r++) if (payout.GetPayLineRow(l,r) < 0 || payout.GetPayLineRow(l,r) > 2)
                    throw new InvalidOperationException("配当表の段が範囲外です。");
                if (payout.IsPayLineActive(l, book.bet)) active++;
            }
            if (active == 0) throw new InvalidOperationException("指定BETに有効ラインがありません。");
            foreach (var t in book.targets) foreach (var role in t.roles)
                if (Array.IndexOf(payout.winPairNames, role) < 0) throw new InvalidOperationException(t.name + ": 配当表にない役 " + role);
        }
        private void Generate()
        {
            var batch = new List<Trial>(); message = null;
            try
            {
                book.Validate(); ValidatePayout();
                if (!ArrangementMatches()) throw new InvalidOperationException("ルール作成時と配列が違います。元配列のルール集を残し、新しいルール集で位置を指定してください。");
                if (book.targets.Count == 0) throw new InvalidOperationException("制御条件を追加してください。");
                var settings = new SerializedObject(controller);
                string signature = Signature(), json = EditorJsonUtility.ToJson(book);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double nextProgress = 0;
                for (int index = 0; index < book.targets.Count; index++)
                {
                    var target = book.targets[index];
                    var evaluator = new ReelSlipEvaluator { controller = controller, payout = payout, bet = book.bet,
                        maximumSlip = book.maximumSlip, allowedRoles = new HashSet<string>(target.roles),
                        priority = (ReelControlPriority)settings.FindProperty("priority").intValue,
                        rolePriority = (ReelRolePriority)settings.FindProperty("rolePriority").intValue };
                    Func<bool> cancel = () => {
                        if (EditorApplication.timeSinceStartup < nextProgress) return false;
                        nextProgress = EditorApplication.timeSinceStartup + 0.1;
                        return EditorUtility.DisplayCancelableProgressBar("出目ルール生成", target.name + "：後続の停止を確認中", (float)index/book.targets.Count);
                    };
                    var solved = new ReelOutcomeSolver().Run(book, target.id, (pattern, prior, press, slip) => {
                        evaluator.Pattern = pattern; evaluator.positions = prior;
                        var c = evaluator.Evaluate(press, slip);
                        bool count = evaluator.priority == ReelControlPriority.CombinationCount;
                        return new ReelOutcomeSolver.Rating { forbidden = c.forbidden > 0, role = c.preferred, level = c.level,
                            primary = count ? c.combinations : c.payout, secondary = count ? c.payout : c.combinations };
                    }, cancel);
                    var trial = new Trial { id = target.id, name = target.name, signature = signature, rulesJson = json };
                    batch.Add(trial);
                    if (!solved.success)
                    {
                        trial.issues.AddRange(solved.issues);
                        trial.issues.Add("有効な禁止出目: " + string.Join("、",book.shapes.FindAll(s => s.enabled && s.For(target.id) == ReelOutcomeRules.Mode.Forbidden).ConvertAll(s => s.name)));
                        trial.summary = "生成不可（保存データは変更していません）"; continue;
                    }
                    trial.design = CreateInstance<ReelControlDesign>(); trial.design.hideFlags = HideFlags.HideAndDontSave;
                    trial.design.Initialize(controller, null); solved.WriteTo(trial.design);
                    trial.design.definitionName = target.name;
                    trial.design.controllerId = GlobalObjectId.GetGlobalObjectIdSlow(controller).ToString();
                    Verify(trial, target, evaluator, cancel);
                    trial.summary += " / 探索 " + solved.states + "状態";
                }
                Dispose(previous); previous = trials; trials = batch;
                message = "全条件を再生成しました（" + clock.Elapsed.TotalSeconds.ToString("F1") + "秒）。前回の結果と比較できます。";
                resultScroll = Vector2.zero;
            }
            catch (Exception ex) { Dispose(batch); message = ex.Message + " 前回の試行結果は保持しています。"; }
            finally { EditorUtility.ClearProgressBar(); }
        }
        private static int[] Simulate(ReelControlDesign d, int order, int[] pressed)
        {
            var stops = new[] {-1,-1,-1};
            for (int s = 0; s < 3; s++)
            {
                int r = ReelControlDesign.StopOrders[order][s];
                int slip = d.SlipsFor(order,s,stops)[pressed[r]];
                if (slip < 0 || slip > 4) throw new InvalidOperationException("生成結果の到達経路に未設定または不正な滑りがあります。");
                stops[r] = ReelControlDesign.Normalize(pressed[r]-slip, d.reels[r].symbols.Length);
            }
            return stops;
        }
        private void Verify(Trial t, ReelOutcomeRules.Target target, ReelSlipEvaluator evaluator, Func<bool> cancel)
        {
            int paths = 0, changed = 0; int[] hits = new int[book.shapes.Count];
            var old = trials.Find(x => x.id == t.id && x.design != null);
            bool comparable = old != null;
            if (comparable) for (int r = 0; r < 3; r++)
            {
                var a = old.design.reels[r].symbols; var b = t.design.reels[r].symbols;
                if (a.Length != b.Length) { comparable = false; break; }
                for (int p = 0; p < a.Length; p++) if (a[p] != b[p]) comparable = false;
            }
            for (int o = 0; o < 6; o++)
                for (int l = 0; l < book.reels[0].symbols.Length; l++)
                    for (int c = 0; c < book.reels[1].symbols.Length; c++)
                        for (int r = 0; r < book.reels[2].symbols.Length; r++)
                        {
                            if ((paths & 127) == 0 && cancel()) throw new OperationCanceledException("検証を中止しました。");
                            var input = new[] {l,c,r}; var stops = Simulate(t.design,o,input); paths++;
                            int last = ReelControlDesign.StopOrders[o][2];
                            var prior = (int[])stops.Clone(); prior[last] = -1;
                            evaluator.Pattern = ReelControlDesign.StagePattern(o,2); evaluator.positions = prior;
                            int slip = t.design.SlipsFor(o,2,prior)[input[last]];
                            if (evaluator.Evaluate(input[last],slip).forbidden > 0) throw new InvalidOperationException("生成後検証で対象外入賞を検出しました。");
                            for (int i = 0; i < book.shapes.Count; i++) if (book.shapes[i].enabled && book.shapes[i].Matches(stops,book.reels))
                            {
                                if (book.shapes[i].For(target.id) == ReelOutcomeRules.Mode.Forbidden)
                                    throw new InvalidOperationException("生成後検証で禁止出目を検出しました: " + book.shapes[i].name);
                                if (hits[i]++ == 0) t.samples.Add(new Sample {label=book.shapes[i].name+" の出現例",order=o,presses=(int[])input.Clone()});
                            }
                            if (comparable)
                            {
                                var before = Simulate(old.design,o,input);
                                if (before[0] != stops[0] || before[1] != stops[1] || before[2] != stops[2])
                                {
                                    if (changed++ == 0) t.samples.Insert(0,new Sample {label="前回から変わった出目の例",order=o,presses=(int[])input.Clone()});
                                }
                            }
                        }
            t.summary = "全6押し順 " + paths + "経路：禁止出目・対象外入賞なし" +
                (comparable ? " / 前回から停止形変更 " + changed + "経路" : " / 前回比較なし");
            for (int i = 0; i < hits.Length; i++) if (book.shapes[i].enabled)
            {
                var s = book.shapes[i];
                t.issues.Add(s.name + ": " + hits[i] + "経路" + (s.For(target.id) == ReelOutcomeRules.Mode.Preferred && hits[i] == 0 ? "【希望未達】" : ""));
                for (int r = 0; r < 3; r++) if (s.Candidates(r,book.reels[r].symbols).Count == 0)
                    t.issues.Add(s.name + ": " + Names[r] + "リールの配列に条件を満たす停止位置がありません。");
            }
            t.design.generationReport = t.summary + "\n" + string.Join("\n", t.issues) +
                "\n配当表ベースの近似評価。引込最適性・独自DynamicControl一致は未保証。\nルールのスナップショット:\n" + t.rulesJson;
        }
        private void SaveTrial(Trial t)
        {
            string path = EditorUtility.SaveFilePanelInProject("試行結果を別アセットへ保存", "ReelControlTrial", "asset", "既存データを上書きせず保存します。ランタイム出力は設計画面から行います。");
            if (string.IsNullOrEmpty(path)) return;
            var saved = Instantiate(t.design); saved.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(saved, AssetDatabase.GenerateUniqueAssetPath(path));
            Selection.activeObject = saved;
            ReelControlDesignWindow.OpenDesignAsset(saved.GetInstanceID(),0);
        }
        private void DrawSymbol(string symbol, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(60,height,GUILayout.ExpandWidth(true));
            Texture2D texture;
            if (pictures.TryGetValue(symbol, out texture) && texture != null) ReelPicturePreview.Draw(rect,texture);
            else GUI.Label(rect,symbol,EditorStyles.centeredGreyMiniLabel);
        }
        private void DrawShape(ReelOutcomeRules.Shape shape)
        {
            float columnWidth = (Mathf.Max(420,position.width*0.52f)-38)/3;
            shape.name = EditorGUILayout.TextField("出目名",shape.name);
            shape.enabled = EditorGUILayout.Toggle("このルールを使う",shape.enabled);
            using (new EditorGUILayout.HorizontalScope())
                for (int r = 0; r < 3; r++)
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,GUILayout.Width(columnWidth)))
                    {
                        int n = book.reels[r].symbols.Length;
                        GUILayout.Label(Names[r],EditorStyles.boldLabel);
                        int before = shape.UsesSymbols(r) ? 2 : shape.lower[r] < 0 ? 0 : 1;
                        int mode = EditorGUILayout.Popup(before,new[] {"任意（ANY）","特定コマ","図柄条件"},GUILayout.Width(columnWidth-12));
                        if (mode != before)
                        {
                            if (shape.useSymbols == null) shape.useSymbols = new bool[3];
                            shape.useSymbols[r] = mode == 2;
                            if (mode == 0) shape.lower[r] = -1;
                            if (mode == 1 && shape.lower[r] < 0) shape.lower[r] = 0;
                        }
                        if (mode == 2) { DrawSymbolConditions(shape,r,columnWidth-12); continue; }
                        if (shape.lower[r] < 0) { GUILayout.Label("ANY",GUILayout.Height(140)); continue; }
                        GUILayout.Label("下段番号",EditorStyles.miniLabel);
                        int number = EditorGUILayout.IntSlider(n-shape.lower[r], 1,n,GUILayout.Width(columnWidth-12));
                        shape.lower[r] = n-number;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("↑")) shape.lower[r] = ReelControlDesign.Normalize(shape.lower[r]-1,n);
                            if (GUILayout.Button("↓")) shape.lower[r] = ReelControlDesign.Normalize(shape.lower[r]+1,n);
                        }
                        for (int row = 0; row < 3; row++) DrawSymbol(book.reels[r].symbols[ReelControlDesign.Normalize(shape.lower[r]+row-2,n)],45);
                    }
            EditorGUILayout.LabelField("この出目を出せる条件（未指定は禁止）",EditorStyles.boldLabel);
            foreach (var target in book.targets)
            {
                var mode = (ReelOutcomeRules.Mode)EditorGUILayout.Popup(target.name,(int)shape.For(target.id),Modes);
                var permission = shape.permissions.Find(p => p.targetId == target.id);
                if (permission == null && mode != ReelOutcomeRules.Mode.Forbidden)
                    shape.permissions.Add(new ReelOutcomeRules.Permission { targetId = target.id, mode = mode });
                else if (permission != null) permission.mode = mode;
            }
        }
        private void DrawTrial(Trial t)
        {
            bool fresh = t.signature == Signature();
            EditorGUILayout.LabelField(t.name,EditorStyles.boldLabel);
            if (!fresh) EditorGUILayout.HelpBox("設定変更前の結果です。再生成してください。",MessageType.Warning);
            EditorGUILayout.LabelField(t.summary,EditorStyles.wordWrappedLabel);
            foreach (string issue in t.issues) EditorGUILayout.LabelField(issue,EditorStyles.wordWrappedMiniLabel);
            var old = previous.Find(x => x.id == t.id && x.design != null);
            if (old != null && GUILayout.Button("前回のルール設定に戻す（Undo可能）"))
            {
                Undo.RecordObject(book,"前回の出目ルールへ戻す");
                EditorJsonUtility.FromJsonOverwrite(old.rulesJson,book);
                EditorUtility.SetDirty(book);
                message = "前回のルールへ戻しました。再生成すると結果を確認できます。";
            }
            if (t.design == null) return;
            foreach (var sample in t.samples)
                if (GUILayout.Button(sample.label)) { order = sample.order; presses = (int[])sample.presses.Clone(); }
            order = GUILayout.Toolbar(order,Orders);
            for (int r = 0; r < 3; r++)
                presses[r] = t.design.reels[r].symbols.Length - EditorGUILayout.IntSlider(Names[r]+" 押下番号",t.design.reels[r].symbols.Length-Mathf.Clamp(presses[r],0,t.design.reels[r].symbols.Length-1),1,t.design.reels[r].symbols.Length);
            int[] stops = Simulate(t.design,order,presses);
            using (new EditorGUILayout.HorizontalScope())
                for (int r = 0; r < 3; r++) using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    int n = t.design.reels[r].symbols.Length;
                    GUILayout.Label(Names[r]+" "+(n-stops[r])+"番 / "+ReelControlDesign.Slip(presses[r],stops[r],n)+"コマ");
                    for (int row = 0; row < 3; row++) DrawSymbol(t.design.reels[r].symbols[ReelControlDesign.Normalize(stops[r]+row-2,n)],40);
                }
            if (old != null)
            {
                bool same = true;
                for (int r = 0; r < 3; r++)
                {
                    var a = old.design.reels[r].symbols; var b = t.design.reels[r].symbols;
                    if (a.Length != b.Length) { same = false; break; }
                    for (int p = 0; p < a.Length; p++) if (a[p] != b[p]) same = false;
                }
                if (same)
                {
                    var p = Simulate(old.design,order,presses);
                    EditorGUILayout.LabelField("前回の停止番号：左 "+(old.design.reels[0].symbols.Length-p[0])+" / 中 "+(old.design.reels[1].symbols.Length-p[1])+" / 右 "+(old.design.reels[2].symbols.Length-p[2]));
                }
            }
            using (new EditorGUI.DisabledScope(!fresh || Application.isPlaying))
                if (GUILayout.Button("この試行を別アセットへ保存して詳細画面で開く")) SaveTrial(t);
        }
        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var next = (ReelOutcomeRules)EditorGUILayout.ObjectField(book,typeof(ReelOutcomeRules),false);
                if (next != book) LoadBook(next);
                using (new EditorGUI.DisabledScope(!ValidMachine() || Application.isPlaying)) if (GUILayout.Button("新規ルール集",EditorStyles.toolbarButton)) NewBook();
                using (new EditorGUI.DisabledScope(book == null || Application.isPlaying)) if (GUILayout.Button("ルールを保存",EditorStyles.toolbarButton)) AssetDatabase.SaveAssetIfDirty(book);
            }
            var machine = (ReelOutputController)EditorGUILayout.ObjectField("参照する機種",controller,typeof(ReelOutputController),true);
            if (machine != controller) SetController(machine);
            payout = (PayoutTable)EditorGUILayout.ObjectField("配当表",payout,typeof(PayoutTable),true);
            EditorGUILayout.HelpBox("出目を指定 → 全条件を再生成 → 押下位置を動かして比較 → 気に入った試行を別保存。生成結果はこのウィンドウを閉じると破棄します。\n特定コマ・段ごとの複数図柄・ANYで最終停止形を指定できます。配当表ベースの近似評価を使い、既存DynamicControlとの一致・引込最適性は未保証です。",MessageType.Info);
            if (book == null) return;
            if (!ArrangementMatches()) EditorGUILayout.HelpBox("機種を選ぶか、ルール集と配列を一致させてください。配列が変わったルールを無条件には流用しません。",MessageType.Warning);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(420,position.width*0.52f))))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    Undo.RecordObject(book,"出目ルールを編集");
                    EditorGUI.BeginChangeCheck();
                    book.bet = EditorGUILayout.IntSlider("BET",book.bet,1,3);
                    book.maximumSlip = EditorGUILayout.IntSlider("最大滑り",book.maximumSlip,0,4);
                    EditorGUILayout.LabelField("制御条件（検索名と入賞可能役）",EditorStyles.boldLabel);
                    if (GUILayout.Button("条件を追加")) { book.targets.Add(new ReelOutcomeRules.Target()); selectedTarget = book.targets.Count-1; GUI.changed = true; }
                    if (book.targets.Count > 0)
                    {
                        selectedTarget = EditorGUILayout.Popup("編集中の条件",Mathf.Clamp(selectedTarget,0,book.targets.Count-1),book.targets.ConvertAll(t => t.name).ToArray());
                        var target = book.targets[selectedTarget];
                        target.name = EditorGUILayout.TextField("検索名 / 状態名",target.name);
                        EditorGUILayout.LabelField("許可する入賞役（未選択はハズレ）",EditorStyles.miniLabel);
                        if (payout != null) for (int p = 0; p < payout.GetWinPairCount(); p++)
                        {
                            string role = payout.winPairNames[p]; bool before = target.roles.Contains(role);
                            bool after = EditorGUILayout.ToggleLeft(payout.GetWinPairDisplayName(role),before);
                            if (before != after) { if (after) target.roles.Add(role); else target.roles.Remove(role); }
                        }
                        if (GUILayout.Button("この条件を削除"))
                        { foreach (var s in book.shapes) s.permissions.RemoveAll(p => p.targetId == target.id); book.targets.RemoveAt(selectedTarget); GUI.changed = true; }
                    }
                    GUILayout.Space(10);
                    if (GUILayout.Button("出目を追加"))
                    { book.shapes.Add(new ReelOutcomeRules.Shape { lower = new[] {0,-1,-1} }); selectedShape = book.shapes.Count-1; GUI.changed = true; }
                    if (book.shapes.Count > 0)
                    {
                        selectedShape = EditorGUILayout.Popup("編集中の出目",Mathf.Clamp(selectedShape,0,book.shapes.Count-1),book.shapes.ConvertAll(s => s.name).ToArray());
                        DrawShape(book.shapes[selectedShape]);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("この出目を複製"))
                            {
                                var copy = JsonUtility.FromJson<ReelOutcomeRules.Shape>(JsonUtility.ToJson(book.shapes[selectedShape]));
                                copy.id = Guid.NewGuid().ToString("N"); copy.name += " コピー"; book.shapes.Add(copy); selectedShape = book.shapes.Count-1; GUI.changed = true;
                            }
                            if (GUILayout.Button("この出目を削除")) { book.shapes.RemoveAt(selectedShape); GUI.changed = true; }
                        }
                    }
                    book.notes = EditorGUILayout.TextField("メモ",book.notes);
                    if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(book);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUI.DisabledScope(!ArrangementMatches() || payout == null))
                        if (GUILayout.Button("全条件を再生成・検証",GUILayout.Height(32))) Generate();
                    if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message,MessageType.Info);
                    resultScroll = EditorGUILayout.BeginScrollView(resultScroll);
                    var trial = book.targets.Count == 0 ? null : trials.Find(t => t.id == book.targets[Mathf.Clamp(selectedTarget,0,book.targets.Count-1)].id);
                    if (trial != null) DrawTrial(trial);
                    else EditorGUILayout.LabelField("左の条件を選ぶと、その生成結果を表示します。",EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndScrollView();
                }
            }
        }
    }
}
