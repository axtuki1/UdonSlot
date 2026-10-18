using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>
    /// 機種エディタ。①制御（滑り表・出目マップ）②出目（基本目・リーチ目）③抽選表 ④配当表 ⑤ボーナス・RT を編集する。
    /// リール制御は変更のたびに裏で全経路を再計算し、ReelControlData へベイクする。
    /// </summary>
    public sealed partial class ReelRuleEditorWindow : EditorWindow
    {
        private const string MachinePrefKey = "AX.UdonSlot.ReelRuleEditor.Machine";
        private const string DesignPrefKey = "AX.UdonSlot.ReelRuleEditor.Design";
        private static readonly string[] ScreenNames = { "制御", "出目", "抽選表", "配当表", "ボーナス・RT" };
        private static readonly string[] PriorityNames = { "ボーナス優先", "小役優先" };

        private ReelOutputController machineObject;
        [SerializeField] private ReelDesignAsset asset;
        [SerializeField] private int screen;

        // 機種と生成結果
        private Machine machine;
        private string machineError;
        private string[] registeredNames;
        private List<string> symbols = new List<string>();
        private Job job;
        private string requested;
        private double nextPoll;
        private ReelRuleEngine engine;
        private List<ControlResult> results;
        private readonly Dictionary<string, ControlResult> resultByName = new Dictionary<string, ControlResult>();
        private readonly List<string> errors = new List<string>();
        private string generatedFrom;
        private long elapsedMs;

        [MenuItem("UdonSlot/機種エディタ")]
        public static void Open()
        {
            GetWindow<ReelRuleEditorWindow>("機種エディタ");
        }

        private void OnEnable()
        {
            minSize = new Vector2(1280, 720);
            GlobalObjectId id;
            if (machineObject == null && GlobalObjectId.TryParse(EditorPrefs.GetString(MachinePrefKey, ""), out id))
                machineObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as ReelOutputController;
            if (asset == null)
            {
                string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(DesignPrefKey, ""));
                if (!string.IsNullOrEmpty(path)) asset = AssetDatabase.LoadAssetAtPath<ReelDesignAsset>(path);
            }
            Undo.undoRedoPerformed += OnUndoRedo;
            requested = null;
            nextPoll = 0;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (job != null) job.Cancel = true;
            job = null;
        }

        private void OnUndoRedo()
        {
            Changed();
            Repaint();
        }

        private ReelDesign Design { get { return asset.design; } }

        // ------------------------------------------------------------ 再計算

        private sealed class Job
        {
            public string Signature;
            public Machine Machine;
            public ReelDesign Design;
            public volatile bool Cancel;
            public volatile bool Done;
            public ReelRuleEngine Engine;
            public List<ControlResult> Results;
            public readonly List<string> Errors = new List<string>();
            public Exception Failure;
            public long Ms;

            public void Run()
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    var rules = ReelDesignConverter.ToRuleSet(Design, Machine);
                    var e = new ReelRuleEngine(Machine, rules);
                    Results = e.GenerateAll(() => Cancel);
                    Engine = e;
                }
                catch (RuleException e)
                {
                    foreach (var error in e.Errors) Errors.Add(error.ToString());
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    Errors.Add(e.Message);
                    Failure = e;
                }
                Ms = watch.ElapsedMilliseconds;
                Done = true;
            }
        }

        /// <summary>設計を変えたらすぐ再計算を始める。</summary>
        private void Changed()
        {
            nextPoll = 0;
            mapCache = null;
        }

        private void Update()
        {
            if (job != null && job.Done)
            {
                var finished = job;
                job = null;
                if (!finished.Cancel) Finish(finished);
                Repaint();
            }
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            Poll();
        }

        private void Poll()
        {
            machine = null;
            machineError = null;
            registeredNames = null;
            if (machineObject == null || asset == null)
            {
                Restart(null, null);
                return;
            }
            try
            {
                machine = ReelMachineReader.Read(machineObject);
                var control = ReelMachineReader.ReelControlOf(machineObject);
                registeredNames = control != null ? control.flagNames : null;
            }
            catch (Exception e)
            {
                machineError = e.Message;
                machine = null;
            }
            CollectSymbols();
            if (machine == null)
            {
                Restart(null, null);
                return;
            }
            string json = JsonUtility.ToJson(Design);
            string signature = ReelMachineReader.Signature(machine) + "\n" + json;
            if (signature == requested) return;
            var design = JsonUtility.FromJson<ReelDesign>(json);
            Restart(signature, design);
        }

        private void Restart(string signature, ReelDesign design)
        {
            if (signature == requested && signature != null) return;
            requested = signature;
            if (job != null) job.Cancel = true;
            job = null;
            if (signature == null)
            {
                SetResults(null, null, null);
                return;
            }
            var next = new Job { Signature = signature, Machine = machine, Design = design };
            job = next;
            ThreadPool.QueueUserWorkItem(delegate { next.Run(); });
            Repaint();
        }

        private void Finish(Job finished)
        {
            if (finished.Failure != null) UnityEngine.Debug.LogException(finished.Failure);
            SetResults(finished.Errors.Count == 0 ? finished.Results : null, finished.Errors.Count == 0 ? finished.Engine : null, finished.Errors);
            generatedFrom = finished.Signature;
            elapsedMs = finished.Ms;
        }

        private void SetResults(List<ControlResult> list, ReelRuleEngine e, List<string> newErrors)
        {
            results = list;
            engine = e;
            resultByName.Clear();
            if (list != null) foreach (var r in list) resultByName[r.Name] = r;
            errors.Clear();
            if (newErrors != null) errors.AddRange(newErrors);
            generatedFrom = null;
            mapCache = null;
        }

        private bool Ready { get { return results != null && errors.Count == 0 && job == null && generatedFrom == requested; } }

        private ControlResult ResultOf(string name)
        {
            ControlResult r;
            return name != null && resultByName.TryGetValue(name, out r) ? r : null;
        }

        private void CollectSymbols()
        {
            symbols.Clear();
            if (machine == null) return;
            for (int r = 0; r < 3; r++)
                foreach (string s in machine.Strips[r])
                    if (!symbols.Contains(s)) symbols.Add(s);
        }

        // ------------------------------------------------------------ 画面

        private void OnGUI()
        {
            DrawTopBar();
            if (machineError != null) EditorGUILayout.HelpBox(machineError, MessageType.Error);
            foreach (string error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (machineObject == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(6);
                int next = GUILayout.Toolbar(screen, ScreenNames, GUILayout.Width(460), GUILayout.Height(22));
                if (next != screen) { screen = next; GUI.FocusControl(null); }
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(4);
            switch (screen)
            {
                case 2: DrawLotteryScreen(); return;
                case 3: DrawPayoutScreen(); return;
                case 4: DrawBonusScreen(); return;
            }
            if (asset == null || machine == null) return;
            ClampSelection();
            if (screen == 0) DrawControlScreen();
            else DrawShapeScreen();
        }

        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24)))
            {
                GUILayout.Label("機種", GUILayout.Width(28));
                EditorGUI.BeginChangeCheck();
                machineObject = (ReelOutputController)EditorGUILayout.ObjectField(machineObject, typeof(ReelOutputController), true, GUILayout.Width(200));
                GUILayout.Space(8);
                GUILayout.Label("設計", GUILayout.Width(28));
                asset = (ReelDesignAsset)EditorGUILayout.ObjectField(asset, typeof(ReelDesignAsset), false, GUILayout.Width(200));
                if (EditorGUI.EndChangeCheck())
                {
                    SavePrefs();
                    requested = null;
                    Changed();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("新規", EditorStyles.toolbarButton, GUILayout.Width(40))) { CreateAsset(); GUIUtility.ExitGUI(); }

                if (asset != null)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("リール画像", GUILayout.Width(56));
                    var texture = (Texture2D)EditorGUILayout.ObjectField(asset.reelTexture, typeof(Texture2D), false, GUILayout.Width(140));
                    if (texture != asset.reelTexture) Edit("リール画像の変更", delegate { asset.reelTexture = texture; });

                    GUILayout.Space(8);
                    GUILayout.Label("制御方式", GUILayout.Width(48));
                    int priority = EditorGUILayout.Popup((int)Design.priority, PriorityNames, EditorStyles.toolbarPopup, GUILayout.Width(96));
                    if (priority != (int)Design.priority) Edit("制御方式の変更", delegate { Design.priority = (ControlPriority)priority; });

                    GUILayout.Space(8);
                    GUILayout.Label("最大滑り", GUILayout.Width(48));
                    int shown = Design.maxSlip >= 0 ? Design.maxSlip : machine != null ? machine.MaxSlip : 4;
                    int slip = EditorGUILayout.IntPopup(shown, new[] { "0", "1", "2", "3", "4" }, new[] { 0, 1, 2, 3, 4 }, EditorStyles.toolbarPopup, GUILayout.Width(40));
                    if (slip != shown) Edit("最大滑りの変更", delegate { Design.maxSlip = slip; });

                    GUILayout.Space(8);
                    GUILayout.Label("規格", GUILayout.Width(28));
                    int regulation = GUILayout.Toolbar((int)Design.regulation, RegulationLimits.Names, EditorStyles.toolbarButton, GUILayout.Width(110));
                    if (regulation != (int)Design.regulation) Edit("規格の変更", delegate { Design.regulation = (Regulation)regulation; });
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("リール配列", EditorStyles.toolbarButton, GUILayout.Width(64))) { ReelMaker.ReelMaker.Open(); GUIUtility.ExitGUI(); }
                DrawNoticeButton();
                if (asset != null && machine != null)
                {
                    if (job != null) GUILayout.Label("計算中…", EditorStyles.miniLabel);
                    else if (Ready) GUILayout.Label("✓ 計算済み " + elapsedMs + "ms", EditorStyles.miniLabel);
                }
                using (new EditorGUI.DisabledScope(!Ready))
                {
                    if (GUILayout.Button("レポート保存", EditorStyles.toolbarButton, GUILayout.Width(80))) { SaveReport(); GUIUtility.ExitGUI(); }
                    if (GUILayout.Button("ベイク", EditorStyles.toolbarButton, GUILayout.Width(60))) { Bake(); GUIUtility.ExitGUI(); }
                }
            }
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(MachinePrefKey, machineObject == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(machineObject).ToString());
            EditorPrefs.SetString(DesignPrefKey, asset == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
        }

        /// <summary>設計アセットへの変更（Undo 対応）。</summary>
        private void Edit(string label, Action change)
        {
            if (asset == null) return;
            Undo.RecordObject(asset, label);
            change();
            EditorUtility.SetDirty(asset);
            Changed();
            Repaint();
        }

        // ------------------------------------------------------------ 操作

        private void CreateAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("リール制御設計", "ReelDesign", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            var created = CreateInstance<ReelDesignAsset>();
            if (machineObject != null)
            {
                try
                {
                    var m = ReelMachineReader.Read(machineObject);
                    created.design.priority = m.BonusFirst ? ControlPriority.BonusFirst : ControlPriority.SmallFirst;
                    created.design.AddControlsFromLottery(m);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning(e.Message);
                }
            }
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            asset = created;
            SavePrefs();
            requested = null;
            Changed();
        }

        private void SaveReport()
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string path = EditorUtility.SaveFilePanel("レポート保存", Path.GetDirectoryName(assetPath), Path.GetFileNameWithoutExtension(assetPath) + ".report.md", "md");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, ReelRuleReport.Markdown(results, 20));
            AssetDatabase.Refresh();
        }

        private void Bake()
        {
            Poll();
            if (!Ready) return;
            var control = ReelMachineReader.ReelControlOf(machineObject);
            var targets = new List<KeyValuePair<ControlResult, ReelControlData>>();
            var created = new List<string>();
            foreach (var result in results)
            {
                ReelControlData data = FindControlData(control, result.Name);
                if (data == null) created.Add(result.Name);
                targets.Add(new KeyValuePair<ControlResult, ReelControlData>(result, data));
            }
            if (targets.Count == 0) return;
            string message = targets.Count + "件の ReelControlData を上書きします。";
            if (created.Count > 0) message += "\n\n新規作成:\n" + string.Join("、", created.ToArray());
            long violations = 0;
            foreach (var pair in targets) violations += pair.Key.Stats.ViolationPaths;
            if (violations > 0) message += "\n\n非成立役の入賞: " + violations;
            if (!EditorUtility.DisplayDialog("ベイク", message, "ベイク", "キャンセル")) return;

            Undo.SetCurrentGroupName("リール制御のベイク");
            int undoGroup = Undo.GetCurrentGroup();
            if (created.Count > 0)
            {
                control = EnsureReelControl(control);
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Value != null) continue;
                    var data = CreateControlData(control, targets[i].Key.Name);
                    targets[i] = new KeyValuePair<ControlResult, ReelControlData>(targets[i].Key, data);
                }
            }

            var lengths = new[] { machine.Strips[0].Length, machine.Strips[1].Length, machine.Strips[2].Length };
            var objects = new List<UnityEngine.Object>();
            foreach (var pair in targets) objects.Add(pair.Value);
            Undo.RecordObjects(objects.ToArray(), "リール制御のベイク");
            foreach (var pair in targets)
            {
                var baked = ReelRuleBaker.Bake(pair.Key, lengths);
                var data = pair.Value;
                data.reelStopSlipCount = baked.ReelStopSlipCount;
                data.IsReelStopTableStyle = baked.IsReelStopTableStyle;
                data.conditionPatterns = baked.ConditionPatterns;
                data.conditionLeftPositions = baked.ConditionLeftPositions;
                data.conditionCenterPositions = baked.ConditionCenterPositions;
                data.conditionRightPositions = baked.ConditionRightPositions;
                data.conditionSlipOffsets = baked.ConditionSlipOffsets;
                data.conditionSlipLengths = baked.ConditionSlipLengths;
                data.conditionSlips = baked.ConditionSlips;
                EditorUtility.SetDirty(data);
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                if (data.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(data.gameObject.scene);
            }
            WriteControlMap(EnsureReelControl(control));
            Undo.CollapseUndoOperations(undoGroup);
            Poll();
            EditorUtility.DisplayDialog("ベイク", targets.Count + "件をベイクしました。", "OK");
        }

        /// <summary>抽選フラグ（＋成立中のボーナス・RT）→ 制御名 の対応表を ReelControl に書き出す。</summary>
        private void WriteControlMap(ReelControl control)
        {
            var flags = new List<string>();
            var bonuses = new List<string>();
            var states = new List<string>();
            var names = new List<string>();
            foreach (var c in Design.controls)
                foreach (string flag in c.lotteryFlags)
                {
                    flags.Add(flag);
                    bonuses.Add(c.bonus ?? "");
                    states.Add(c.state ?? "");
                    names.Add(c.name);
                }
            Undo.RecordObject(control, "リール制御のベイク");
            control.mapFlags = flags.ToArray();
            control.mapBonuses = bonuses.ToArray();
            control.mapStates = states.ToArray();
            control.mapControls = names.ToArray();
            EditorUtility.SetDirty(control);
            PrefabUtility.RecordPrefabInstancePropertyModifications(control);
        }

        private static ReelControlData FindControlData(ReelControl control, string name)
        {
            if (control == null || control.flagNames == null || control.reelControlDataList == null) return null;
            int index = Array.IndexOf(control.flagNames, name);
            return index >= 0 && index < control.reelControlDataList.Length ? control.reelControlDataList[index] : null;
        }

        /// <summary>機種に ReelControl が無ければ、機種と同じ GameObject に追加して参照を設定する。</summary>
        private ReelControl EnsureReelControl(ReelControl control)
        {
            if (control != null) return control;
            control = UdonSharpEditor.UdonSharpUndo.AddComponent<ReelControl>(machineObject.gameObject);
            control.flagNames = new string[0];
            control.reelControlDataList = new ReelControlData[0];
            var so = new SerializedObject(machineObject);
            so.FindProperty("reelControl").objectReferenceValue = control;
            so.ApplyModifiedProperties();
            return control;
        }

        /// <summary>機種の子に内部名の GameObject を作って ReelControlData を付け、ReelControl に登録する。</summary>
        private ReelControlData CreateControlData(ReelControl control, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(machineObject.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "リール制御のベイク");
            var data = UdonSharpEditor.UdonSharpUndo.AddComponent<ReelControlData>(go);

            Undo.RecordObject(control, "リール制御のベイク");
            var names = new List<string>(control.flagNames ?? new string[0]);
            var datas = new List<ReelControlData>(control.reelControlDataList ?? new ReelControlData[0]);
            while (datas.Count < names.Count) datas.Add(null);
            int index = names.IndexOf(name);
            if (index >= 0) datas[index] = data;
            else
            {
                names.Add(name);
                datas.Add(data);
            }
            control.flagNames = names.ToArray();
            control.reelControlDataList = datas.ToArray();
            EditorUtility.SetDirty(control);
            PrefabUtility.RecordPrefabInstancePropertyModifications(control);
            return data;
        }

        // ------------------------------------------------------------ 描画の部品

        private static readonly Color PanelColor = new Color(0.2f, 0.2f, 0.2f);
        private static readonly Color HeaderColor = new Color(0.17f, 0.17f, 0.17f);
        private static readonly Color SelectedColor = new Color(0.18f, 0.35f, 0.54f);
        private static readonly Color AccentColor = new Color(0.30f, 0.56f, 0.84f);
        private static readonly Color DangerColor = new Color(0.90f, 0.28f, 0.30f);
        private static readonly Color ManualColor = new Color(0.79f, 0.54f, 0.17f);
        private static readonly Color AnyColor = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color HazureColor = new Color(0.36f, 0.42f, 0.52f);

        private static GUIStyle chipStyle;
        private static GUIStyle headStyle;
        private static GUIStyle centeredStyle;
        private static GUIStyle linkStyle;

        private static GUIStyle ChipStyle
        {
            get
            {
                if (chipStyle == null)
                {
                    chipStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(7, 7, 1, 1) };
                }
                return chipStyle;
            }
        }

        private static GUIStyle HeadStyle
        {
            get
            {
                if (headStyle == null) headStyle = new GUIStyle(EditorStyles.boldLabel) { padding = new RectOffset(8, 8, 4, 4) };
                return headStyle;
            }
        }

        private static GUIStyle CenteredStyle
        {
            get
            {
                if (centeredStyle == null) centeredStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                return centeredStyle;
            }
        }

        private static GUIStyle LinkStyle
        {
            get
            {
                if (linkStyle == null)
                {
                    linkStyle = new GUIStyle(EditorStyles.miniLabel);
                    linkStyle.normal.textColor = new Color(0.5f, 0.7f, 0.92f);
                    linkStyle.hover.textColor = new Color(0.66f, 0.8f, 0.95f);
                }
                return linkStyle;
            }
        }

        private static bool Link(string text)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(text), LinkStyle, GUILayout.ExpandWidth(false));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, text, LinkStyle);
        }

        /// <summary>パネルの見出し。</summary>
        private static void PanelHeader(string title)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(title), HeadStyle, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);
            GUI.Label(rect, title, HeadStyle);
        }

        private static Rect PanelHeaderRect(string title)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(title), HeadStyle, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);
            GUI.Label(rect, title, HeadStyle);
            return rect;
        }

        private static Vector2 ChipSize(string text)
        {
            var size = ChipStyle.CalcSize(new GUIContent(text));
            return new Vector2(size.x, 16);
        }

        private static void DrawChip(Rect rect, string text, Color background, Color foreground)
        {
            EditorGUI.DrawRect(rect, background);
            var color = GUI.contentColor;
            ChipStyle.normal.textColor = foreground;
            GUI.Label(rect, text, ChipStyle);
            GUI.contentColor = color;
        }

        /// <summary>レイアウト上にチップを1つ置く。クリックされたら true。</summary>
        private static bool Chip(string text, Color background, Color foreground)
        {
            var size = ChipSize(text);
            var rect = GUILayoutUtility.GetRect(size.x, size.y, GUILayout.Width(size.x), GUILayout.Height(size.y));
            rect.y += 1;
            DrawChip(rect, text, background, foreground);
            return Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition) && Use();
        }

        private static bool Use()
        {
            Event.current.Use();
            return true;
        }

        private static void Outline(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private static bool Clicked(Rect rect, int button)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != button || !rect.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        /// <summary>決め手ラベルの配色。</summary>
        private static void ReasonColors(string reason, out Color background, out Color foreground)
        {
            if (reason == "手動") { background = new Color(0.35f, 0.23f, 0.08f); foreground = new Color(1f, 0.81f, 0.48f); }
            else if (reason == "回避不能") { background = new Color(0.45f, 0.14f, 0.15f); foreground = new Color(1f, 0.78f, 0.78f); }
            else if (reason.EndsWith("引込")) { background = new Color(0.14f, 0.29f, 0.19f); foreground = new Color(0.62f, 0.88f, 0.69f); }
            else if (reason.EndsWith("狙い")) { background = new Color(0.12f, 0.23f, 0.36f); foreground = new Color(0.65f, 0.80f, 0.96f); }
            else if (reason.EndsWith("回避")) { background = new Color(0.29f, 0.23f, 0.11f); foreground = new Color(0.94f, 0.77f, 0.42f); }
            else { background = new Color(0.23f, 0.23f, 0.23f); foreground = new Color(0.72f, 0.72f, 0.72f); }
        }

        // ------------------------------------------------------------ 図柄の画像

        private readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();
        private bool picturesLoaded;
        private readonly Dictionary<Texture, float> sourceAspects = new Dictionary<Texture, float>();

        /// <summary>インポート設定で縮められる前の、元画像ファイルの縦横比。</summary>
        private float SourceAspect(Texture texture)
        {
            float aspect;
            if (sourceAspects.TryGetValue(texture, out aspect)) return aspect;
            aspect = (float)texture.width / texture.height;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null)
            {
                int width, height;
                importer.GetSourceTextureWidthAndHeight(out width, out height);
                if (width > 0 && height > 0) aspect = (float)width / height;
            }
            sourceAspects[texture] = aspect;
            return aspect;
        }

        private static Rect Fit(Rect rect, float aspect)
        {
            if (aspect <= 0) return rect;
            if (rect.width / rect.height > aspect)
            {
                float w = rect.height * aspect;
                return new Rect(rect.x + (rect.width - w) / 2, rect.y, w, rect.height);
            }
            float h = rect.width / aspect;
            return new Rect(rect.x, rect.y + (rect.height - h) / 2, rect.width, h);
        }

        /// <summary>リール r の index 番のコマを描く。</summary>
        private void DrawStripSymbol(Rect rect, int reel, int index)
        {
            string[] strip = machine.Strips[reel];
            var texture = asset.reelTexture;
            if (texture != null)
            {
                int n = strip.Length;
                float aspect = SourceAspect(texture) * n / 3f;
                var uv = new Rect(reel / 3f, 1f - (index + 1f) / n, 1f / 3f, 1f / n);
                EditorGUI.DrawRect(rect, Color.black);
                GUI.DrawTextureWithTexCoords(Fit(rect, aspect), texture, uv);
                return;
            }
            DrawSymbolPicture(rect, strip[index]);
        }

        /// <summary>図柄名で描く（リールテクスチャ上の最初の出現位置を使う）。</summary>
        private void DrawSymbol(Rect rect, string symbol)
        {
            if (asset != null && asset.reelTexture != null && machine != null)
                for (int r = 0; r < 3; r++)
                {
                    int index = Array.IndexOf(machine.Strips[r], symbol);
                    if (index >= 0) { DrawStripSymbol(rect, r, index); return; }
                }
            DrawSymbolPicture(rect, symbol);
        }

        private void DrawSymbolPicture(Rect rect, string symbol)
        {
            if (!picturesLoaded)
            {
                picturesLoaded = true;
                foreach (string guid in AssetDatabase.FindAssets("t:ReelPicture"))
                {
                    var p = AssetDatabase.LoadAssetAtPath<AX.UdonSlot.REV1_0.Serialize.ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                    if (p != null && !string.IsNullOrEmpty(p.pictureName)) pictures[p.pictureName] = p.picture;
                }
            }
            EditorGUI.DrawRect(rect, Color.black);
            Texture2D picture;
            if (pictures.TryGetValue(symbol, out picture) && picture != null)
                GUI.DrawTexture(Fit(rect, SourceAspect(picture)), picture, ScaleMode.StretchToFill);
            else
                GUI.Label(rect, symbol, CenteredStyle);
        }

        /// <summary>停止位置（下段インデックス）で見える3コマを描く。行は 0=上段。</summary>
        private void DrawWindow(Rect rect, int[] stops)
        {
            float w = rect.width / 3, h = rect.height / 3;
            EditorGUI.DrawRect(rect, Color.black);
            for (int row = 0; row < 3; row++)
                for (int r = 0; r < 3; r++)
                {
                    int index = Machine.Wrap(stops[r] + row - 2, machine.Strips[r].Length);
                    DrawStripSymbol(new Rect(rect.x + r * w + 1, rect.y + row * h + 1, w - 2, h - 2), r, index);
                }
        }

        /// <summary>出目の小さなプレビュー（含む図柄の先頭を表示）。</summary>
        private void DrawMiniShape(Rect rect, DesignShape shape)
        {
            float w = rect.width / 3, h = rect.height / 3;
            EditorGUI.DrawRect(rect, Color.black);
            for (int c = 0; c < 9; c++)
            {
                var cell = c < shape.cells.Count ? shape.cells[c] : null;
                var cr = new Rect(rect.x + (c % 3) * w + 1, rect.y + (c / 3) * h + 1, w - 2, h - 2);
                if (cell == null || cell.IsAny) { EditorGUI.DrawRect(cr, AnyColor); continue; }
                if (cell.include.Count > 0) DrawSymbol(cr, cell.include[0]);
                else { EditorGUI.DrawRect(cr, AnyColor); GUI.Label(cr, "NOT", CenteredStyle); }
            }
        }

        private void ClampSelection()
        {
            controlIndex = Mathf.Clamp(controlIndex, 0, Mathf.Max(0, Design.controls.Count - 1));
            shapeIndex = Mathf.Clamp(shapeIndex, 0, Mathf.Max(0, Design.shapes.Count - 1));
            shapeCell = Mathf.Clamp(shapeCell, 0, 8);
            bool repaired = false;
            foreach (var shape in Design.shapes)
            {
                if (shape.cells == null) { shape.cells = DesignShape.NewCells(); repaired = true; }
                while (shape.cells.Count < 9) { shape.cells.Add(new DesignCell()); repaired = true; }
            }
            if (repaired) EditorUtility.SetDirty(asset);
        }

        private static string OrderTag(int firstReel)
        {
            return firstReel < 0 ? "押し順: すべて" : ReelRuleEngine.ReelNames[firstReel] + "第一停止のみ";
        }
    }
}
