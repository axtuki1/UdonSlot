using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AX.UdonSlot.REV1_0.Serialize;

namespace AX.UdonSlot.REV1_0.Editor
{
    public sealed class ReelControlProjectWindow : EditorWindow
    {
        [SerializeField] private ReelControlProject project;
        [SerializeField] private ReelOutputController machine;
        [SerializeField] private int flagIndex, tab, order, stage, press, issuePage;
        [SerializeField] private int[] prior = {-1,-1,-1};
        private ReelControlData bakeTarget;
        private PayoutTable payout;
        private List<ReelControlProjectEngine.Issue> issues;
        private string verificationSignature, message;
        private Vector2 leftScroll, mainScroll, shapeScroll;
        private double nextProgress;
        private readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();
        private static readonly string[] Reels = {"左", "中", "右"};
        private static readonly string[] Orders = {"左→中→右", "左→右→中", "中→左→右", "中→右→左", "右→左→中", "右→中→左"};
        private ReelControlProject.Flag Flag { get { return project != null && project.flags.Count > 0 ? project.flags[Mathf.Clamp(flagIndex,0,project.flags.Count-1)] : null; } }

        [MenuItem("UdonSlot/制御プロジェクト")]
        public static void Open() { GetWindow<ReelControlProjectWindow>("制御プロジェクト").Show(); }
        [UnityEditor.Callbacks.OnOpenAsset]
        public static bool OpenAsset(int id, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(id) as ReelControlProject;
            if (asset == null) return false;
            var w = GetWindow<ReelControlProjectWindow>("制御プロジェクト"); w.Load(asset); w.Show(); return true;
        }
        private void OnEnable()
        {
            minSize = new Vector2(960,640); Undo.undoRedoPerformed += OnUndo;
            ReloadPictures(); if (project != null) Load(project);
        }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndo; }
        private void OnUndo() { issues = null; Repaint(); }
        private void ReloadPictures()
        {
            pictures.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:ReelPicture", new[] {"Assets", "Packages/com.axtuki1.udonslot"}))
            {
                var p = AssetDatabase.LoadAssetAtPath<ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && !string.IsNullOrEmpty(p.pictureName)) pictures[p.pictureName] = p.picture;
            }
        }
        private void Load(ReelControlProject value)
        {
            project = value; flagIndex = 0; issues = null; message = null; bakeTarget = null; machine = null;
            GlobalObjectId id;
            if (project != null && GlobalObjectId.TryParse(project.machineId,out id))
                machine = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as ReelOutputController;
            ResolvePayout();
        }
        private void ResolvePayout()
        { payout = machine == null ? null : new SerializedObject(machine).FindProperty("payoutTable").objectReferenceValue as PayoutTable; }
        private bool MachineValid()
        { return machine != null && machine.reelDatas != null && machine.reelDatas.Length == 3 && machine.reelDatas.All(r => r != null && r.symbols != null && r.symbols.Length > 0); }
        private ReelControlProject.Strip[] CurrentStrips()
        { return machine.reelDatas.Select(r => new ReelControlProject.Strip {symbols=(string[])r.symbols.Clone()}).ToArray(); }
        private bool ArrangementMatches()
        {
            return MachineValid() && project.strips.Length == 3 && Enumerable.Range(0,3).All(r => project.strips[r].symbols.SequenceEqual(machine.reelDatas[r].symbols));
        }
        private void Edit(string action)
        { Undo.RecordObject(project,action); issues = null; message = null; }
        private void Dirty() { EditorUtility.SetDirty(project); }
        private void Try(Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { message = "キャンセル"; }
            catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
            finally { EditorUtility.ClearProgressBar(); }
        }
        private bool Cancel()
        {
            if (EditorApplication.timeSinceStartup < nextProgress) return false;
            nextProgress = EditorApplication.timeSinceStartup + 0.15;
            return EditorUtility.DisplayCancelableProgressBar("制御プロジェクト",Flag.name,0.5f);
        }
        private string InputSignature()
        {
            ResolvePayout();
            return JsonUtility.ToJson(new Input {strips=CurrentStrips(), bet=project.bet, maximumSlip=project.maximumSlip,
                roles=Flag.roles, shapes=Flag.shapes}) + "|" + (payout == null ? "" : EditorJsonUtility.ToJson(payout));
        }
        [Serializable] private sealed class Input
        {
            public ReelControlProject.Strip[] strips;
            public int bet, maximumSlip;
            public List<string> roles;
            public List<ReelControlProject.Shape> shapes;
        }
        private string EffectiveSignature() { return InputSignature() + "|" + JsonUtility.ToJson(Flag); }
        private ReelControlProjectEngine.Forbidden Evaluator()
        {
            ResolvePayout();
            if (!ArrangementMatches()) throw new InvalidOperationException("配列変更／機種参照切れ");
            project.Validate(Flag);
            if (payout == null) throw new InvalidOperationException("配当表参照切れ");
            for (int p = 0; p < payout.GetWinPairCount(); p++) if (!payout.IsWinPairValid(p)) throw new InvalidOperationException("配当表: " + p);
            int active = 0;
            for (int l = 0; l < payout.GetPayLineCount(); l++)
            {
                if (!payout.IsPayLineValid(l)) throw new InvalidOperationException("有効ライン: " + l);
                for (int r = 0; r < 3; r++) if (payout.GetPayLineRow(l,r) < 0 || payout.GetPayLineRow(l,r) > 2) throw new InvalidOperationException("ライン段範囲外");
                if (payout.IsPayLineActive(l,project.bet)) active++;
            }
            if (active == 0) throw new InvalidOperationException("有効ラインなし");
            foreach (var role in Flag.roles) if (Array.IndexOf(payout.winPairNames,role) < 0) throw new InvalidOperationException("成立役参照切れ: " + role);
            var e = new ReelSlipEvaluator {controller=machine,payout=payout,bet=project.bet,allowedRoles=new HashSet<string>(Flag.roles)};
            return (pattern,positions,p,slip) => { e.Pattern=pattern; e.positions=positions; return e.Evaluate(p,slip).forbidden > 0; };
        }
        private void NewProject()
        {
            if (machine.gameObject.scene.IsValid() && string.IsNullOrEmpty(machine.gameObject.scene.path))
                throw new InvalidOperationException("機種のシーンを保存してください。");
            string path = EditorUtility.SaveFilePanelInProject("制御プロジェクト", "ReelControlProject", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            var p = CreateInstance<ReelControlProject>();
            p.machineId = GlobalObjectId.GetGlobalObjectIdSlow(machine).ToString(); p.strips=CurrentStrips();
            ImportFlags(p);
            if (p.flags.Count == 0) p.flags.Add(new ReelControlProject.Flag {name="ハズレ"});
            AssetDatabase.CreateAsset(p,AssetDatabase.GenerateUniqueAssetPath(path));
            Load(p); Selection.activeObject=p;
        }
        private void ImportFlags(ReelControlProject destination)
        {
            var lottery = new SerializedObject(machine).FindProperty("lotteryTable").objectReferenceValue;
            if (lottery == null) return;
            var so = new SerializedObject(lottery); var names=so.FindProperty("winpairNames");
            var starts=so.FindProperty("winningRoleStartIndexes"); var counts=so.FindProperty("winningRoleCounts"); var roles=so.FindProperty("winningRoleNames");
            for (int i = 0; i < names.arraySize; i++)
            {
                string name=names.GetArrayElementAtIndex(i).stringValue;
                if (destination.flags.Exists(f=>f.name==name)) continue;
                var f=new ReelControlProject.Flag {name=name,lotterySourceName=name};
                if (i < starts.arraySize && i < counts.arraySize)
                {
                    int start=starts.GetArrayElementAtIndex(i).intValue, count=counts.GetArrayElementAtIndex(i).intValue;
                    if (start < 0 || count < 0 || start > roles.arraySize-count) throw new InvalidOperationException("抽選表の役範囲が不正です。");
                    for (int j=0;j<count;j++) f.roles.Add(roles.GetArrayElementAtIndex(start+j).stringValue);
                }
                destination.flags.Add(f);
            }
        }
        private static string StringAt(SerializedProperty p,int i) { return i<p.arraySize?p.GetArrayElementAtIndex(i).stringValue:""; }
        private static float FloatAt(SerializedProperty p,int i) { return i<p.arraySize?p.GetArrayElementAtIndex(i).floatValue:0; }
        private void ApplyFlag()
        {
            ResolvePayout();
            if(machine==null || payout==null) throw new InvalidOperationException("機種／配当表参照切れ");
            if(string.IsNullOrWhiteSpace(Flag.lotterySourceName)) throw new InvalidOperationException("小役フラグが未指定です。");
            var lottery=new SerializedObject(machine).FindProperty("lotteryTable").objectReferenceValue;
            if(lottery==null) throw new InvalidOperationException("抽選表参照切れ");
            var so=new SerializedObject(lottery);
            var names=so.FindProperty("winpairNames"); var display=so.FindProperty("flagDisplayNames");
            var b1=so.FindProperty("winpairProbabilitiesBet1"); var b2=so.FindProperty("winpairProbabilitiesBet2"); var b3=so.FindProperty("winpairProbabilities");
            var remainder=so.FindProperty("isRemainderFlags"); var starts=so.FindProperty("winningRoleStartIndexes");
            var counts=so.FindProperty("winningRoleCounts"); var roles=so.FindProperty("winningRoleNames");
            var entries=new List<ReelProjectFlagSync.Entry>();
            for(int i=0;i<names.arraySize;i++)
            {
                var entry=new ReelProjectFlagSync.Entry {name=StringAt(names,i),display=StringAt(display,i),
                    bet1=FloatAt(b1,i),bet2=FloatAt(b2,i),bet3=FloatAt(b3,i),remainder=i<remainder.arraySize && remainder.GetArrayElementAtIndex(i).boolValue};
                if(i<starts.arraySize && i<counts.arraySize)
                {
                    int start=starts.GetArrayElementAtIndex(i).intValue,count=counts.GetArrayElementAtIndex(i).intValue;
                    if(start<0 || count<0 || start>roles.arraySize-count) throw new InvalidOperationException("抽選表の役範囲が不正です。");
                    for(int j=0;j<count;j++) entry.roles.Add(StringAt(roles,start+j));
                }
                else if(roles.arraySize>0) throw new InvalidOperationException("抽選表の役範囲がありません。");
                entries.Add(entry);
            }
            var sourceFlag=new ReelControlProject.Flag {name=Flag.lotterySourceName,roles=new List<string>(Flag.roles)};
            var plan=ReelProjectFlagSync.Plan(entries,sourceFlag,payout.winPairNames);
            string old=sourceFlag.name;
            bool added=!entries.Exists(e=>e.name==old);
            string details="抽選表: "+lottery.name+"\n小役フラグ: "+sourceFlag.name+
                "\n成立役: "+(Flag.roles.Count==0?"なし":string.Join(" / ",Flag.roles.ToArray()))+
                (added?"\n新規追加／抽選比率: 0":"\n既存の抽選比率・ハズレ設定を保持");
            if(!EditorUtility.DisplayDialog("フラグを反映",details,"反映","キャンセル")) return;
            Undo.RecordObjects(new UnityEngine.Object[] {lottery,project},"フラグ・成立役を反映");
            names.arraySize=display.arraySize=b1.arraySize=b2.arraySize=b3.arraySize=remainder.arraySize=starts.arraySize=counts.arraySize=plan.Count;
            roles.arraySize=plan.Sum(e=>e.roles.Count); int offset=0;
            for(int i=0;i<plan.Count;i++)
            {
                var e=plan[i]; names.GetArrayElementAtIndex(i).stringValue=e.name; display.GetArrayElementAtIndex(i).stringValue=e.display;
                b1.GetArrayElementAtIndex(i).floatValue=e.bet1; b2.GetArrayElementAtIndex(i).floatValue=e.bet2; b3.GetArrayElementAtIndex(i).floatValue=e.bet3;
                remainder.GetArrayElementAtIndex(i).boolValue=e.remainder; starts.GetArrayElementAtIndex(i).intValue=offset; counts.GetArrayElementAtIndex(i).intValue=e.roles.Count;
                foreach(string role in e.roles) roles.GetArrayElementAtIndex(offset++).stringValue=role;
            }
            so.ApplyModifiedProperties(); Dirty();
            EditorUtility.SetDirty(lottery); PrefabUtility.RecordPrefabInstancePropertyModifications(lottery);
            var component=lottery as Component;
            if(component!=null && component.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            message="フラグ・成立役を反映済み";
        }
        private void Generate(int fixedThrough)
        {
            var evaluate=Evaluator(); string signature=InputSignature();
            var next=new ReelControlProjectEngine().Generate(project,Flag,evaluate,Cancel,fixedThrough);
            int corrections=Flag.tables.Sum(t=>t.manual.Count(x=>x>=0));
            if (corrections > 0 && !EditorUtility.DisplayDialog("生成結果を適用", "対象: " + Flag.name + (fixedThrough<0?"／全段階":"／第"+(fixedThrough+2)+"停止以降") +
                "\n手動補正: " + corrections + "件\n変更箇所は補正競合として保持", "適用", "キャンセル")) return;
            var preview=new ReelControlProject.Flag {name=Flag.name,roles=Flag.roles,shapes=Flag.shapes,tables=Flag.tables,detached=Flag.detached};
            ReelControlProjectEngine.Merge(preview,next,fixedThrough);
            var checkedIssues=ReelControlProjectEngine.Verify(project,preview,evaluate,Cancel);
            Edit("制御生成"); Flag.tables=preview.tables; Flag.detached=preview.detached; Flag.generationSignature=signature;
            Dirty(); issues=checkedIssues; verificationSignature=EffectiveSignature();
            message="生成済み";
        }
        private void ResetArrangement()
        {
            if (!EditorUtility.DisplayDialog("配列更新", "対象: 全制御\n生成結果・手動補正をリセット", "リセット", "キャンセル")) return;
            Edit("配列更新"); project.ResetArrangement(CurrentStrips()); Dirty();
        }
        private void Bake()
        {
            var evaluate=Evaluator();
            if (Flag.generationSignature != InputSignature()) throw new InvalidOperationException("生成設定変更／再生成が必要");
            var checkedIssues=ReelControlProjectEngine.Verify(project,Flag,evaluate,Cancel);
            issues=checkedIssues; verificationSignature=EffectiveSignature();
            if (issues.Any(i=>i.kind!="取りこぼし")) throw new InvalidOperationException("制御不整合／補正競合");
            if (!EditorUtility.DisplayDialog("ベイク",Flag.name+"\n出力先: "+bakeTarget.name+"\n出力先の全制御テーブルを置換", "ベイク", "キャンセル")) return;
            Undo.RecordObject(bakeTarget,"制御ベイク"); ReelControlProjectEngine.Bake(project,Flag,bakeTarget);
            EditorUtility.SetDirty(bakeTarget); PrefabUtility.RecordPrefabInstancePropertyModifications(bakeTarget);
            if (bakeTarget.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bakeTarget.gameObject.scene);
            message="ベイク済み";
        }
        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var value=(ReelControlProject)EditorGUILayout.ObjectField(project,typeof(ReelControlProject),false,GUILayout.Width(270));
                if (value!=project) Load(value);
                if (project==null)
                {
                    machine=(ReelOutputController)EditorGUILayout.ObjectField(machine,typeof(ReelOutputController),true);
                    using (new EditorGUI.DisabledScope(!MachineValid())) if (GUILayout.Button("新規",EditorStyles.toolbarButton)) Try(NewProject);
                }
                else
                {
                    if (GUILayout.Button("保存",EditorStyles.toolbarButton,GUILayout.Width(50))) AssetDatabase.SaveAssetIfDirty(project);
                    tab=GUILayout.Toolbar(tab,new[] {"基本停止形","滑り表","検証","ベイク","関連"},EditorStyles.toolbarButton);
                }
            }
            if (project==null) return;
            if (machine==null) EditorGUILayout.HelpBox("機種参照切れ",MessageType.Error);
            else if (!ArrangementMatches())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("配列変更",EditorStyles.boldLabel);
                    if (GUILayout.Button("配列更新",GUILayout.Width(100))) Try(ResetArrangement);
                }
            }
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(190))) DrawFlags();
                using (new EditorGUILayout.VerticalScope())
                {
                    bool scrollMain=tab!=0;
                    if(scrollMain) mainScroll=EditorGUILayout.BeginScrollView(mainScroll);
                    if (tab==4) DrawLinks();
                    else if (Flag!=null)
                    {
                        GUILayout.Label(Flag.name,EditorStyles.boldLabel);
                        if (Flag.tables.Count>0 && MachineValid() && Flag.generationSignature!=InputSignature()) GUILayout.Label("生成設定変更／再生成が必要");
                        if (tab==0) DrawShapes();
                        else if (tab==1) DrawTables();
                        else if (tab==2) DrawVerification();
                        else DrawBake();
                    }
                    if(scrollMain) EditorGUILayout.EndScrollView();
                }
            }
        }
        private void DrawFlags()
        {
            leftScroll=EditorGUILayout.BeginScrollView(leftScroll);
            for (int i=0;i<project.flags.Count;i++) if (GUILayout.Toggle(flagIndex==i,project.flags[i].name,"Button") && flagIndex!=i)
            { flagIndex=i; issues=null; mainScroll=shapeScroll=Vector2.zero; }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("＋ 制御追加")) { Edit("制御追加"); project.flags.Add(new ReelControlProject.Flag()); flagIndex=project.flags.Count-1; Dirty(); }
            using(new EditorGUI.DisabledScope(Flag==null)) if(GUILayout.Button("制御を複製"))
            {
                Edit("制御複製"); flagIndex=project.DuplicateControl(flagIndex);
                mainScroll=shapeScroll=Vector2.zero; bakeTarget=null; Dirty();
            }
            if (Flag!=null && GUILayout.Button("制御削除") && EditorUtility.DisplayDialog("制御削除",Flag.name+"\n停止形・生成結果・手動補正を削除","削除","キャンセル"))
            { Edit("制御削除"); project.flags.Remove(Flag); flagIndex=Mathf.Max(0,flagIndex-1); Dirty(); }
            using (new EditorGUI.DisabledScope(!MachineValid())) if (GUILayout.Button("小役フラグから追加")) Try(()=> {
                var temp=CreateInstance<ReelControlProject>();
                try
                {
                    ImportFlags(temp); var menu=new GenericMenu(); var destination=project;
                    foreach(var f in temp.flags)
                    {
                        var source=f;
                        menu.AddItem(new GUIContent(source.name.Replace('/','／')),false,()=> {
                            if(project!=destination) return;
                            string name=source.name; int suffix=2;
                            while(project.flags.Exists(x=>x.name==name)) name=source.name+"_"+(suffix++);
                            Edit("小役フラグから制御追加");
                            project.flags.Add(new ReelControlProject.Flag {name=name,lotterySourceName=source.lotterySourceName,roles=new List<string>(source.roles)});
                            flagIndex=project.flags.Count-1; Dirty(); Repaint();
                        });
                    }
                    if(temp.flags.Count==0) menu.AddDisabledItem(new GUIContent("小役フラグなし"));
                    menu.ShowAsContext();
                }
                finally { DestroyImmediate(temp); }
            });
        }
        private void DrawShapes()
        {
            EditorGUI.BeginChangeCheck();
            string name=EditorGUILayout.TextField("制御名",Flag.name);
            int bet=EditorGUILayout.IntSlider("BET",project.bet,1,3), maximum=EditorGUILayout.IntSlider("最大滑り",project.maximumSlip,0,4);
            if (EditorGUI.EndChangeCheck())
            {
                Edit("基本設定");
                if(name!=Flag.name && string.IsNullOrEmpty(Flag.lotterySourceName) && machine!=null)
                {
                    var lottery=new SerializedObject(machine).FindProperty("lotteryTable").objectReferenceValue;
                    if(lottery!=null)
                    {
                        var names=new SerializedObject(lottery).FindProperty("winpairNames");
                        for(int i=0;i<names.arraySize;i++) if(StringAt(names,i)==Flag.name) { Flag.lotterySourceName=Flag.name; break; }
                    }
                }
                Flag.name=name; project.bet=bet; project.maximumSlip=maximum; Dirty();
            }
            if(!string.IsNullOrEmpty(Flag.lotterySourceName)) GUILayout.Label(Flag.lotterySourceName+"から",EditorStyles.miniLabel);
            GUILayout.Label("メモ",EditorStyles.miniLabel);
            string notes=EditorGUILayout.TextArea(Flag.notes,GUILayout.Height(36));
            if(notes!=Flag.notes) { Undo.RecordObject(project,"制御メモ"); Flag.notes=notes; Dirty(); }
            GUILayout.Label("成立役",EditorStyles.boldLabel); ResolvePayout();
            if (payout!=null) foreach(string role in (payout.winPairNames??new string[0]).Distinct())
            {
                bool has=Flag.roles.Contains(role), set=EditorGUILayout.ToggleLeft(payout.GetWinPairDisplayName(role),has);
                if(set!=has) { Edit("成立役"); if(set) Flag.roles.Add(role); else Flag.roles.Remove(role); Dirty(); }
            }
            using(var listScroll=new EditorGUILayout.ScrollViewScope(shapeScroll,GUILayout.ExpandHeight(true)))
            {
            shapeScroll=listScroll.scrollPosition;
            for(int i=0;i<Flag.shapes.Count;i++)
            {
                var shape=Flag.shapes[i];
                using(new EditorGUILayout.VerticalScope("box"))
                {
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label((i+1).ToString(),GUILayout.Width(24));
                        string shapeName=EditorGUILayout.TextField(shape.name);
                        if(shapeName!=shape.name) { Edit("停止形名"); shape.name=shapeName; Dirty(); }
                        using(new EditorGUI.DisabledScope(i==0)) if(GUILayout.Button("↑",GUILayout.Width(28))) { Edit("停止形順序"); Flag.shapes.RemoveAt(i); Flag.shapes.Insert(i-1,shape); Dirty(); GUIUtility.ExitGUI(); }
                        using(new EditorGUI.DisabledScope(i==Flag.shapes.Count-1)) if(GUILayout.Button("↓",GUILayout.Width(28))) { Edit("停止形順序"); Flag.shapes.RemoveAt(i); Flag.shapes.Insert(i+1,shape); Dirty(); GUIUtility.ExitGUI(); }
                        using(new EditorGUI.DisabledScope(Flag.shapes.Count<=1)) if(GUILayout.Button("×",GUILayout.Width(28)) && EditorUtility.DisplayDialog("停止形削除",shape.name,"削除","キャンセル"))
                        { Edit("停止形削除"); Flag.shapes.RemoveAt(i); Dirty(); GUIUtility.ExitGUI(); }
                    }
                    for(int row=0;row<3;row++) using(new EditorGUILayout.HorizontalScope())
                        for(int r=0;r<3;r++) DrawCell(shape,r,row);
                }
            }
            }
            if(GUILayout.Button("＋ 停止形")) { Edit("停止形追加"); Flag.shapes.Add(new ReelControlProject.Shape()); Dirty(); }
            using(new EditorGUI.DisabledScope(!MachineValid() || !ArrangementMatches())) if(GUILayout.Button("生成")) Try(()=>Generate(-1));
        }
        private void DrawCell(ReelControlProject.Shape shape,int reel,int row)
        {
            var cell=shape.cells[reel*3+row];
            var options=cell.symbols;
            string label=options.Count==0?"ANY":string.Join(" / ",options.ToArray());
            if(options.Count>0 && cell.negate) label="NOT " + label;
            Rect rect=GUILayoutUtility.GetRect(100,80,GUILayout.MinWidth(100),GUILayout.ExpandWidth(true));
            if(GUI.Button(rect,new GUIContent("",label)))
                PopupWindow.Show(rect,new SymbolPicker(this,project,cell,reel));
            if(options.Count==0) GUI.Label(rect,"ANY",new GUIStyle(EditorStyles.label) {alignment=TextAnchor.MiddleCenter});
            else
            {
                int count=Mathf.Min(4,options.Count), columns=count==1?1:2, rows=count<=2?1:2;
                Rect images=new Rect(rect.x+6,rect.y+4,rect.width-12,rect.height-26);
                for(int i=0;i<count;i++)
                {
                    var imageRect=new Rect(images.x+(i%columns)*images.width/columns,images.y+(i/columns)*images.height/rows,
                        images.width/columns,images.height/rows);
                    DrawSymbolInRect(imageRect,options[i]);
                }
                GUI.Label(new Rect(rect.x+4,rect.yMax-21,rect.width-8,18),options.Count>4?(cell.negate?"NOT ":"")+options.Count+"図柄":label,
                    new GUIStyle(EditorStyles.label) {alignment=TextAnchor.MiddleCenter,clipping=TextClipping.Clip});
            }
        }
        private sealed class SymbolPicker : PopupWindowContent
        {
            private readonly ReelControlProjectWindow owner;
            private readonly ReelControlProject asset;
            private readonly ReelControlProject.Cell cell;
            private readonly string[] symbols;
            private readonly HashSet<string> available;
            private Vector2 scroll;
            public SymbolPicker(ReelControlProjectWindow owner,ReelControlProject asset,ReelControlProject.Cell cell,int reel)
            {
                this.owner=owner; this.asset=asset; this.cell=cell;
                symbols=asset.strips.SelectMany(s=>s.symbols).Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
                available=new HashSet<string>(asset.strips.Length==3?asset.strips[reel].symbols:new string[0]);
            }
            public override Vector2 GetWindowSize() { return new Vector2(260,Mathf.Min(420,76+symbols.Length*36)); }
            private void Change(Action action)
            {
                Undo.RecordObject(asset,"停止形の図柄選択"); action(); EditorUtility.SetDirty(asset);
                owner.issues=null; owner.message=null; owner.Repaint();
            }
            public override void OnGUI(Rect rect)
            {
                if(owner==null || asset==null || owner.project!=asset) { editorWindow.Close(); return; }
                if(GUILayout.Toggle(cell.symbols.Count==0,"ANY","Button") && cell.symbols.Count>0) Change(()=> {cell.symbols.Clear(); cell.negate=false;});
                using(new EditorGUI.DisabledScope(cell.symbols.Count==0))
                {
                    bool negate=EditorGUILayout.ToggleLeft("否定",cell.symbols.Count>0 && cell.negate);
                    if(negate!=cell.negate && cell.symbols.Count>0) Change(()=>cell.negate=negate);
                }
                scroll=EditorGUILayout.BeginScrollView(scroll);
                foreach(string symbol in symbols) using(new EditorGUI.DisabledScope(!available.Contains(symbol))) using(new EditorGUILayout.HorizontalScope())
                {
                    Rect image=GUILayoutUtility.GetRect(44,32,GUILayout.Width(44)); owner.DrawSymbolInRect(image,symbol);
                    bool selected=cell.symbols.Contains(symbol);
                    bool value=GUILayout.Toggle(selected,symbol,GUILayout.Height(32));
                    if(value!=selected) Change(()=> { if(value) cell.symbols.Add(symbol); else cell.symbols.Remove(symbol); if(cell.symbols.Count==0) cell.negate=false; });
                }
                EditorGUILayout.EndScrollView();
            }
        }
        private void DrawSymbolInRect(Rect rect,string symbol)
        {
            Texture2D texture;
            if(pictures.TryGetValue(symbol,out texture) && texture!=null) ReelPicturePreview.Draw(rect,texture);
            else GUI.Label(rect,symbol,EditorStyles.centeredGreyMiniLabel);
        }
        private void DrawSymbol(string symbol,float height)
        {
            var rect=GUILayoutUtility.GetRect(50,height,GUILayout.ExpandWidth(true)); DrawSymbolInRect(rect,symbol);
        }
        private void DrawTables()
        {
            int nextOrder=EditorGUILayout.Popup("押し順",order,Orders), nextStage=GUILayout.Toolbar(stage,new[] {"第1停止","第2停止","第3停止"});
            if(nextOrder!=order || nextStage!=stage) { order=nextOrder; stage=nextStage; prior=new[] {-1,-1,-1}; }
            var contexts=Flag.tables.Where(t=>t.pattern==ReelControlProject.Pattern(order,stage)).ToList();
            for(int s=0;s<stage;s++)
            {
                int r=ReelControlProject.Orders[order][s];
                int n=project.strips[r].symbols.Length;
                int[] candidates=contexts.Select(t=>t.prior[r]).Distinct().OrderBy(p=>p).ToArray();
                if(candidates.Length==0) { GUILayout.Label("未生成／到達範囲外"); return; }
                string[] labels=candidates.Select(p=>(n-p)+"  "+project.strips[r].symbols[p]).ToArray();
                int selected=Array.IndexOf(candidates,prior[r]);
                prior[r]=candidates[EditorGUILayout.Popup(Reels[r],Mathf.Max(0,selected),labels)];
                contexts=contexts.Where(t=>t.prior[r]==prior[r]).ToList();
            }
            int pattern=ReelControlProject.Pattern(order,stage);
            var table=Flag.tables.Find(t=>t.Key==ReelControlProject.KeyFor(pattern,prior));
            if(table==null) { GUILayout.Label("未生成／到達範囲外"); return; }
            int reel=table.reel;
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("コマ数をコピー")) { EditorGUIUtility.systemCopyBuffer=ReelControlProject.CopySlips(table); message="コマ数をコピー済み"; }
                using(new EditorGUI.DisabledScope(!ArrangementMatches())) if(GUILayout.Button("コマ数を貼り付け")) Try(()=> {
                    var values=ReelControlProject.ParseSlips(EditorGUIUtility.systemCopyBuffer,table.generated.Length,project.maximumSlip);
                    if(!EditorUtility.DisplayDialog("コマ数を貼り付け",Flag.name+"／"+Orders[order]+"／第"+(stage+1)+"停止\n条件: "+string.Join(", ",table.prior.Select((p,r)=>p<0?"ANY":Reels[r]+(project.strips[r].symbols.Length-p)).ToArray())+
                        "\n表示順で"+values.Length+"件の滑り値・手動補正を置換", "貼り付け", "キャンセル")) return;
                    Edit("コマ数貼り付け"); table.manual=values; table.conflict=new bool[values.Length]; Dirty(); message="コマ数を貼り付け済み";
                });
            }
            using(new EditorGUI.DisabledScope(stage==2 || !ArrangementMatches()))
                if(GUILayout.Button("後段を生成")) Try(()=>Generate(stage));
            if(table.unsafePath) GUILayout.Label("制御不整合",EditorStyles.boldLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("番号",GUILayout.Width(45)); GUILayout.Label(Reels[reel],GUILayout.Width(90));
                GUILayout.Label("生成",GUILayout.Width(40)); GUILayout.Label("滑り",GUILayout.Width(70)); GUILayout.Label("停止先",GUILayout.Width(110)); GUILayout.Label("状態");
            }
            for(int p=0;p<table.generated.Length;p++) using(new EditorGUILayout.HorizontalScope())
            {
                int n=table.generated.Length; Color before=GUI.backgroundColor;
                bool unreachable=table.impossible[p] && Flag.shapes.TrueForAll(s=>s.ConstrainsReel(reel));
                if(table.conflict[p]) GUI.backgroundColor=new Color(1,0.45f,0.45f);
                else if(unreachable) GUI.backgroundColor=new Color(1,0.8f,0.4f);
                if(GUILayout.Button((n-p).ToString(),GUILayout.Width(45),GUILayout.Height(30))) press=p;
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(90))) DrawSymbol(project.strips[reel].symbols[p],30);
                GUILayout.Label(table.generated[p].ToString(),GUILayout.Width(40));
                int value=EditorGUILayout.IntField(table.Effective(p),GUILayout.Width(70));
                if(value!=table.Effective(p)) { Edit("滑り補正"); table.manual[p]=Mathf.Clamp(value,0,project.maximumSlip); table.conflict[p]=false; press=p; Dirty(); }
                int stop=ReelControlProject.Wrap(p-table.Effective(p),n);
                GUILayout.Label((n-stop)+"  "+project.strips[reel].symbols[stop],GUILayout.Width(110));
                GUILayout.Label(table.conflict[p]?"競合":table.manual[p]>=0?"補正":unreachable?"到達不可":"",GUILayout.Width(80));
                if(table.manual[p]>=0)
                {
                    if(GUILayout.Button("生成値",GUILayout.Width(60))) { Edit("補正解除"); table.manual[p]=-1; table.conflict[p]=false; Dirty(); }
                    if(table.conflict[p] && GUILayout.Button("補正採用",GUILayout.Width(70))) { Edit("競合確認"); table.conflict[p]=false; Dirty(); }
                }
                GUI.backgroundColor=before;
            }
            press=Mathf.Clamp(press,0,table.generated.Length-1);
            using(new EditorGUILayout.HorizontalScope("box"))
            {
                for(int r=0;r<3;r++) using(new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(Reels[r],EditorStyles.boldLabel);
                    int lower=r==reel?ReelControlProject.Wrap(press-table.Effective(press),project.strips[r].symbols.Length):prior[r];
                    if(lower<0) GUILayout.Label("ANY");
                    else for(int row=0;row<3;row++) DrawSymbol(project.strips[r].symbols[ReelControlProject.Wrap(lower+row-2,project.strips[r].symbols.Length)],45);
                }
            }
        }
        private void DrawVerification()
        {
            if(GUILayout.Button("検証")) Try(()=> { var e=Evaluator(); issues=ReelControlProjectEngine.Verify(project,Flag,e,Cancel); verificationSignature=EffectiveSignature(); });
            bool stale=issues!=null && (!MachineValid() || verificationSignature!=EffectiveSignature());
            if(stale) GUILayout.Label("検証結果: 更新が必要");
            GUILayout.Label("旧条件の補正: "+Flag.detached.Count);
            if(issues==null) return;
            var filtered=issues.Where(i=>i.kind!="取りこぼし").ToList();
            GUILayout.Label("不整合: "+filtered.Count+"件");
            int pages=Mathf.Max(1,(filtered.Count+99)/100); issuePage=Mathf.Clamp(issuePage,0,pages-1);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(issuePage==0)) if(GUILayout.Button("前",GUILayout.Width(50))) issuePage--;
                GUILayout.Label((issuePage+1)+" / "+pages+"  "+filtered.Count+"件");
                using(new EditorGUI.DisabledScope(issuePage==pages-1)) if(GUILayout.Button("次",GUILayout.Width(50))) issuePage++;
            }
            foreach(var issue in filtered.Skip(issuePage*100).Take(100)) using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(issue.kind+"  "+Orders[issue.order]+"  第"+(issue.stage+1)+"停止  "+
                    (project.strips[ReelControlProject.Orders[issue.order][issue.stage]].symbols.Length-issue.press)+"番  "+issue.paths+"経路");
                using(new EditorGUI.DisabledScope(stale)) if(GUILayout.Button("開く",GUILayout.Width(50)))
                { order=issue.order; stage=issue.stage; prior=(int[])issue.prior.Clone(); press=issue.press; tab=1; mainScroll=Vector2.zero; }
            }
        }
        private void DrawBake()
        {
            bakeTarget=(ReelControlData)EditorGUILayout.ObjectField("出力先",bakeTarget,typeof(ReelControlData),true);
            using(new EditorGUI.DisabledScope(bakeTarget==null || !ArrangementMatches())) if(GUILayout.Button("ベイク")) Try(Bake);
        }
        private void DrawLinks()
        {
            var value=(ReelOutputController)EditorGUILayout.ObjectField("機種",machine,typeof(ReelOutputController),true);
            if(value!=machine)
            {
                if(value!=null && value.gameObject.scene.IsValid() && string.IsNullOrEmpty(value.gameObject.scene.path))
                { message="機種のシーンを保存してください。"; return; }
                if(EditorUtility.DisplayDialog("機種変更",value==null?"参照解除":value.name,"変更","キャンセル"))
                { Edit("機種関連"); machine=value; project.machineId=machine==null?"":GlobalObjectId.GetGlobalObjectIdSlow(machine).ToString(); ResolvePayout(); Dirty(); }
            }
            using(new EditorGUI.DisabledScope(true))
            {
                if(MachineValid()) for(int r=0;r<3;r++) EditorGUILayout.ObjectField(Reels[r]+"配列",machine.reelDatas[r],typeof(ReelData),true);
                ResolvePayout(); EditorGUILayout.ObjectField("配当・ライン",payout,typeof(PayoutTable),true);
                if(machine!=null) EditorGUILayout.ObjectField("抽選表",new SerializedObject(machine).FindProperty("lotteryTable").objectReferenceValue,typeof(UnityEngine.Object),true);
            }
            if(GUILayout.Button("関連を再読込")) { Load(project); ReloadPictures(); }
            if(Flag!=null)
            {
                GUILayout.Label(Flag.name,EditorStyles.boldLabel);
                if(!string.IsNullOrEmpty(Flag.lotterySourceName)) GUILayout.Label(Flag.lotterySourceName+"から",EditorStyles.miniLabel);
                using(new EditorGUI.DisabledScope(machine==null || payout==null || string.IsNullOrWhiteSpace(Flag.lotterySourceName)))
                    if(GUILayout.Button("小役フラグ・成立役を反映")) Try(ApplyFlag);
            }
            using(new EditorGUI.DisabledScope(machine==null)) if(GUILayout.Button("抽選表を開く"))
            {
                var lottery=new SerializedObject(machine).FindProperty("lotteryTable").objectReferenceValue;
                if(lottery!=null) { Selection.activeObject=lottery; EditorGUIUtility.PingObject(lottery); }
            }
        }
    }
}
