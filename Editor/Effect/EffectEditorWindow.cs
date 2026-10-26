using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>
    /// 演出エディタ。パターン（1ゲームの流れ）とアクション（映像・表示・音・リール・コードの箱）を編集し、
    /// シーンの EffectPlayer に直接書き込む。
    /// </summary>
    public sealed class EffectEditorWindow : EditorWindow
    {
        [MenuItem("UdonSlot/演出エディタ")]
        public static void Open()
        {
            var window = GetWindow<EffectEditorWindow>();
            window.titleContent = new GUIContent("演出エディタ");
            window.minSize = new Vector2(900, 480);
            window.Show();
        }

        private static readonly Color PanelColor = new Color(0.19f, 0.19f, 0.19f);
        private static readonly Color CellColor = new Color(0.2f, 0.2f, 0.2f);
        private static readonly Color CellSelectedColor = new Color(0.17f, 0.36f, 0.53f);
        private static readonly Color ChipColor = new Color(0.32f, 0.32f, 0.32f);
        private static readonly Color HeaderColor = new Color(0.16f, 0.16f, 0.16f);
        private static readonly string[] Tabs = { "パターン", "アクション" };
        private static readonly string[] BranchNames = { "共通", "入賞", "非入賞" };
        private static readonly string[] SoundTypeNames = Enum.GetNames(typeof(SoundType));

        [SerializeField] private EffectPlayer player;
        private EffectModel model;
        private int tab;
        private int patternIndex;
        private int actionIndex;
        private int selectedLayer = -1;
        private int selectedStep = -1;
        private string search = "";
        private Vector2 listScroll, mainScroll, sideScroll;

        private EffectPattern Pattern { get { return model != null && patternIndex >= 0 && patternIndex < model.Patterns.Count ? model.Patterns[patternIndex] : null; } }
        private EffectAction Action { get { return model != null && actionIndex >= 0 && actionIndex < model.Actions.Count ? model.Actions[actionIndex] : null; } }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += Reload;
            if (player == null) player = FindObjectOfType<EffectPlayer>();
            Reload();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Reload;
        }

        private void OnFocus()
        {
            Reload();
        }

        private void OnHierarchyChange()
        {
            if (player == null) Reload();
        }

        internal void Reload()
        {
            model = player != null ? EffectModel.Load(player) : null;
            Repaint();
        }

        /// <summary>変更してシーンへ書き込む。</summary>
        internal void Edit(string label, Action change)
        {
            if (model == null) return;
            change();
            model.Save(label);
            Repaint();
        }

        internal EffectModel Model { get { return model; } }

        private void OnGUI()
        {
            DrawToolbar();
            if (player == null)
            {
                DrawMissingPlayer();
                return;
            }
            if (model == null) Reload();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                if (tab == 0) DrawPatternView();
                else DrawActionView();
            }
        }

        // ------------------------------------------------------------ ツールバー

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                var picked = (EffectPlayer)EditorGUILayout.ObjectField(player, typeof(EffectPlayer), true, GUILayout.Width(260));
                if (EditorGUI.EndChangeCheck())
                {
                    player = picked;
                    Reload();
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawMissingPlayer()
        {
            var sub = FindObjectOfType<SubBoard>();
            GUILayout.Space(20);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(20);
                using (new EditorGUI.DisabledScope(sub == null))
                {
                    if (GUILayout.Button("EffectPlayer を作成", GUILayout.Width(180), GUILayout.Height(28))) CreatePlayer(sub);
                }
            }
        }

        private void CreatePlayer(SubBoard sub)
        {
            var go = new GameObject("EffectPlayer");
            Undo.RegisterCreatedObjectUndo(go, "EffectPlayer の作成");
            go.transform.SetParent(sub.transform, false);
            var created = UdonSharpEditor.UdonSharpUndo.AddComponent<EffectPlayer>(go);
            var subSo = new SerializedObject(sub);
            var mainBoard = subSo.FindProperty("mainBoard").objectReferenceValue as MainBoard;
            var so = new SerializedObject(created);
            so.FindProperty("mainBoard").objectReferenceValue = mainBoard;
            so.ApplyModifiedProperties();
            subSo.FindProperty("effectPlayer").objectReferenceValue = created;
            subSo.ApplyModifiedProperties();
            player = created;
            Reload();
        }

        // ------------------------------------------------------------ 一覧

        private void DrawList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
            {
                int newTab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(24));
                if (newTab != tab) { tab = newTab; GUI.FocusControl(null); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                    if (GUILayout.Button("＋", GUILayout.Width(28))) AddItem();
                }
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                var names = tab == 0 ? model.PatternNames() : model.ActionNames();
                int selected = tab == 0 ? patternIndex : actionIndex;
                for (int i = 0; i < names.Count; i++)
                {
                    if (search.Length > 0 && names[i].IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
                    if (i == selected) EditorGUI.DrawRect(rect, CellSelectedColor);
                    GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 6, rect.height), names[i]);
                    var e = Event.current;
                    if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
                    {
                        int index = i;
                        if (tab == 0) { patternIndex = index; selectedLayer = selectedStep = -1; }
                        else actionIndex = index;
                        GUI.FocusControl(null);
                        if (e.button == 1) ShowItemMenu(index);
                        e.Use();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            var line = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(line, HeaderColor);
        }

        private void AddItem()
        {
            if (tab == 0)
                Edit("パターンの追加", delegate
                {
                    var p = new EffectPattern { Name = EffectModel.UniqueName("パターン", model.PatternNames()) };
                    p.Layers.Add("レイヤー");
                    model.Patterns.Add(p);
                    patternIndex = model.Patterns.Count - 1;
                });
            else
                Edit("アクションの追加", delegate
                {
                    model.Actions.Add(new EffectAction { Name = EffectModel.UniqueName("アクション", model.ActionNames()) });
                    actionIndex = model.Actions.Count - 1;
                });
        }

        private void ShowItemMenu(int index)
        {
            var menu = new GenericMenu();
            if (tab == 0)
            {
                menu.AddItem(new GUIContent("複製"), false, () => Edit("パターンの複製", delegate
                {
                    var src = model.Patterns[index];
                    var p = new EffectPattern { Name = EffectModel.UniqueName(src.Name, model.PatternNames()), Next = src.Next, Layers = new List<string>(src.Layers) };
                    foreach (var e in src.Entries) p.Entries.Add(new EffectEntry { Layer = e.Layer, Step = e.Step, Branch = e.Branch, Action = e.Action });
                    model.Patterns.Insert(index + 1, p);
                    patternIndex = index + 1;
                }));
                menu.AddItem(new GUIContent("削除"), false, () => Edit("パターンの削除", delegate
                {
                    model.RemovePattern(model.Patterns[index]);
                    patternIndex = Mathf.Clamp(patternIndex, 0, model.Patterns.Count - 1);
                }));
            }
            else
            {
                menu.AddItem(new GUIContent("複製"), false, () => Edit("アクションの複製", delegate
                {
                    var src = model.Actions[index];
                    model.Actions.Insert(index + 1, src.Copy(EffectModel.UniqueName(src.Name, model.ActionNames())));
                    actionIndex = index + 1;
                }));
                menu.AddItem(new GUIContent("削除"), false, () => Edit("アクションの削除", delegate
                {
                    model.RemoveAction(model.Actions[index]);
                    actionIndex = Mathf.Clamp(actionIndex, 0, model.Actions.Count - 1);
                }));
            }
            menu.ShowAsContext();
        }

        // ------------------------------------------------------------ パターン

        private void DrawPatternView()
        {
            var pattern = Pattern;
            using (new EditorGUILayout.VerticalScope())
            {
                if (pattern == null) { GUILayout.FlexibleSpace(); return; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.DelayedTextField(pattern.Name, EditorStyles.boldLabel, GUILayout.MinWidth(160));
                    if (EditorGUI.EndChangeCheck() && name.Trim().Length > 0 && name != pattern.Name)
                    {
                        var used = model.PatternNames();
                        used.Remove(pattern.Name);
                        Edit("パターン名の変更", () => pattern.Name = EffectModel.UniqueName(name.Trim(), used));
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("次", GUILayout.Width(16));
                    var options = new List<string> { "なし" };
                    options.AddRange(model.PatternNames());
                    int current = pattern.Next != null ? model.Patterns.IndexOf(pattern.Next) + 1 : 0;
                    int next = EditorGUILayout.Popup(current, options.ToArray(), GUILayout.Width(180));
                    if (next != current) Edit("次のパターンの変更", () => pattern.Next = next == 0 ? null : model.Patterns[next - 1]);
                }
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (var scroll = new EditorGUILayout.ScrollViewScope(mainScroll))
                    {
                        mainScroll = scroll.scrollPosition;
                        DrawGrid(pattern);
                    }
                    var line = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
                    EditorGUI.DrawRect(line, HeaderColor);
                    DrawCellPanel(pattern);
                }
            }
        }

        private void DrawGrid(EffectPattern pattern)
        {
            const float layerWidth = 120;
            float width = Mathf.Max(600, position.width - 240 - 280 - 30);
            float cellWidth = (width - layerWidth) / EffectPlayer.StepCount;

            var head = GUILayoutUtility.GetRect(width, 24, GUILayout.Width(width));
            for (int s = 0; s < EffectPlayer.StepCount; s++)
            {
                var r = new Rect(head.x + layerWidth + s * cellWidth + 2, head.y, cellWidth - 4, head.height);
                EditorGUI.DrawRect(r, HeaderColor);
                GUI.Label(r, EffectModel.StepNames[s], CenterBold);
            }

            for (int layer = 0; layer < pattern.Layers.Count; layer++)
            {
                int max = 1;
                for (int s = 0; s < EffectPlayer.StepCount; s++)
                {
                    int count = pattern.Cell(layer, s).Count + (pattern.IsSplit(layer, s) ? 1 : 0);
                    max = Mathf.Max(max, count);
                }
                float height = Mathf.Max(52, max * 22 + 10);
                var row = GUILayoutUtility.GetRect(width, height + 4, GUILayout.Width(width));
                var nameRect = new Rect(row.x, row.y + 2, layerWidth - 4, height);
                EditorGUI.DrawRect(nameRect, HeaderColor);
                int l = layer;
                EditorGUI.BeginChangeCheck();
                string layerName = EditorGUI.DelayedTextField(new Rect(nameRect.x + 4, nameRect.y + (height - 18) / 2, nameRect.width - 26, 18), pattern.Layers[layer]);
                if (EditorGUI.EndChangeCheck()) Edit("レイヤー名の変更", () => pattern.Layers[l] = layerName);
                if (GUI.Button(new Rect(nameRect.xMax - 20, nameRect.y + (height - 18) / 2, 18, 18), "⋮", EditorStyles.label)) ShowLayerMenu(pattern, l);

                for (int s = 0; s < EffectPlayer.StepCount; s++)
                {
                    var cell = new Rect(row.x + layerWidth + s * cellWidth + 2, row.y + 2, cellWidth - 4, height);
                    bool selected = selectedLayer == layer && selectedStep == s;
                    EditorGUI.DrawRect(cell, selected ? CellSelectedColor : CellColor);
                    DrawCellContents(pattern, layer, s, cell);
                    var e = Event.current;
                    if (e.type == EventType.MouseDown && cell.Contains(e.mousePosition))
                    {
                        selectedLayer = layer;
                        selectedStep = s;
                        GUI.FocusControl(null);
                        e.Use();
                        Repaint();
                    }
                }
            }
            GUILayout.Space(4);
            if (GUILayout.Button("＋ レイヤー", GUILayout.Width(110)))
                Edit("レイヤーの追加", () => pattern.Layers.Add(EffectModel.UniqueName("レイヤー", pattern.Layers)));
        }

        private void DrawCellContents(EffectPattern pattern, int layer, int step, Rect cell)
        {
            var entries = pattern.Cell(layer, step);
            float y = cell.y + 5;
            if (pattern.IsSplit(layer, step))
            {
                for (int b = EffectPlayer.BranchWin; b <= EffectPlayer.BranchLose; b++)
                {
                    GUI.Label(new Rect(cell.x + 4, y, 44, 20), BranchNames[b], EditorStyles.miniLabel);
                    bool any = false;
                    foreach (var e in entries)
                    {
                        if (e.Branch != b) continue;
                        DrawChip(new Rect(cell.x + 48, y, cell.width - 52, 20), e.Action.Name);
                        y += 22;
                        any = true;
                    }
                    if (!any) y += 22;
                }
                return;
            }
            if (entries.Count == 0)
            {
                GUI.Label(cell, "—", CenterMuted);
                return;
            }
            foreach (var e in entries)
            {
                DrawChip(new Rect(cell.x + 4, y, cell.width - 8, 20), e.Action.Name);
                y += 22;
            }
        }

        private void ShowLayerMenu(EffectPattern pattern, int layer)
        {
            var menu = new GenericMenu();
            if (layer > 0) menu.AddItem(new GUIContent("上へ"), false, () => Edit("レイヤーの移動", () => SwapLayers(pattern, layer, layer - 1)));
            else menu.AddDisabledItem(new GUIContent("上へ"));
            if (layer < pattern.Layers.Count - 1) menu.AddItem(new GUIContent("下へ"), false, () => Edit("レイヤーの移動", () => SwapLayers(pattern, layer, layer + 1)));
            else menu.AddDisabledItem(new GUIContent("下へ"));
            menu.AddItem(new GUIContent("削除"), false, () => Edit("レイヤーの削除", delegate
            {
                pattern.Entries.RemoveAll(e => e.Layer == layer);
                foreach (var e in pattern.Entries) if (e.Layer > layer) e.Layer--;
                pattern.Layers.RemoveAt(layer);
                selectedLayer = selectedStep = -1;
            }));
            menu.ShowAsContext();
        }

        private static void SwapLayers(EffectPattern pattern, int a, int b)
        {
            string name = pattern.Layers[a];
            pattern.Layers[a] = pattern.Layers[b];
            pattern.Layers[b] = name;
            foreach (var e in pattern.Entries)
            {
                if (e.Layer == a) e.Layer = b;
                else if (e.Layer == b) e.Layer = a;
            }
        }

        private void DrawCellPanel(EffectPattern pattern)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(270)))
            {
                if (selectedLayer < 0 || selectedLayer >= pattern.Layers.Count || selectedStep < 0)
                {
                    GUILayout.FlexibleSpace();
                    return;
                }
                int layer = selectedLayer, step = selectedStep;
                GUILayout.Label(pattern.Layers[layer] + " × " + EffectModel.StepNames[step], EditorStyles.boldLabel);
                bool split = pattern.IsSplit(layer, step);
                if (EffectModel.CanSplit(step))
                {
                    int mode = GUILayout.Toolbar(split ? 1 : 0, new[] { "共通", "入賞／非入賞" });
                    if (mode != (split ? 1 : 0)) Edit("分岐の切り替え", () => SetSplit(pattern, layer, step, mode == 1));
                    split = pattern.IsSplit(layer, step);
                }
                GUILayout.Space(6);
                sideScroll = EditorGUILayout.BeginScrollView(sideScroll);
                if (split)
                {
                    DrawBranchList(pattern, layer, step, EffectPlayer.BranchWin);
                    GUILayout.Space(8);
                    DrawBranchList(pattern, layer, step, EffectPlayer.BranchLose);
                }
                else DrawBranchList(pattern, layer, step, EffectPlayer.BranchCommon);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawBranchList(EffectPattern pattern, int layer, int step, int branch)
        {
            if (branch != EffectPlayer.BranchCommon) GUILayout.Label(BranchNames[branch], EditorStyles.miniBoldLabel);
            foreach (var e in pattern.Cell(layer, step))
            {
                if (e.Branch != branch) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var entry = e;
                    var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
                    DrawChip(rect, e.Action.Name);
                    EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        // アクションの編集へ
                        tab = 1;
                        actionIndex = model.Actions.IndexOf(e.Action);
                        Event.current.Use();
                    }
                    if (GUILayout.Button("×", GUILayout.Width(22))) Edit("アクションを外す", () => pattern.Entries.Remove(entry));
                }
            }
            if (GUILayout.Button("＋ アクション"))
            {
                EffectActionPicker.Show(this, picked => Edit("アクションの配置", delegate
                {
                    pattern.Entries.Add(new EffectEntry { Layer = layer, Step = step, Branch = branch, Action = picked });
                }));
            }
        }

        private static void SetSplit(EffectPattern pattern, int layer, int step, bool split)
        {
            var seen = new HashSet<EffectAction>();
            var remove = new List<EffectEntry>();
            foreach (var e in pattern.Cell(layer, step))
            {
                if (split) e.Branch = EffectPlayer.BranchWin;
                else
                {
                    e.Branch = EffectPlayer.BranchCommon;
                    if (!seen.Add(e.Action)) remove.Add(e);
                }
            }
            foreach (var e in remove) pattern.Entries.Remove(e);
            // 空のセルを分けたときも、分けた状態が残るように入賞側の目印は要らない（表示は IsSplit で判断）。
        }

        // ------------------------------------------------------------ アクション

        private void DrawActionView()
        {
            var action = Action;
            using (new EditorGUILayout.VerticalScope())
            {
                if (action == null) { GUILayout.FlexibleSpace(); return; }
                mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
                using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(640)))
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.DelayedTextField("名前", action.Name);
                    if (EditorGUI.EndChangeCheck() && name.Trim().Length > 0 && name != action.Name)
                    {
                        var used = model.ActionNames();
                        used.Remove(action.Name);
                        Edit("アクション名の変更", () => action.Name = EffectModel.UniqueName(name.Trim(), used));
                    }
                    EditorGUI.BeginChangeCheck();
                    int delay = EditorGUILayout.DelayedIntField("遅延 (ms)", action.DelayMs);
                    if (EditorGUI.EndChangeCheck()) Edit("遅延の変更", () => action.DelayMs = Mathf.Max(0, delay));

                    GUILayout.Space(8);
                    DrawAnimSection(action);
                    GUILayout.Space(8);
                    DrawActiveSection(action);
                    GUILayout.Space(8);
                    DrawSoundSection(action);
                    GUILayout.Space(8);
                    DrawReelSection(action);
                    GUILayout.Space(8);
                    DrawCodeSection(action);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private static bool SectionHeader(string title)
        {
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);
            GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 40, rect.height), title, EditorStyles.boldLabel);
            return GUI.Button(new Rect(rect.xMax - 28, rect.y + 2, 26, 18), "＋");
        }

        private static readonly string[] KindNames = { "トリガー", "Bool", "Int", "Float" };
        private static readonly string[] BoolNames = { "OFF", "ON" };
        private static readonly string[] IntSourceNames = { "固定", "呼び出し時の値" };

        private void DrawAnimSection(EffectAction action)
        {
            if (SectionHeader("映像")) Edit("映像の追加", () => action.Anims.Add(new EffectAnim()));
            if (action.Anims.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Animator", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                    GUILayout.Label("パラメータ名", EditorStyles.miniLabel, GUILayout.Width(150));
                    GUILayout.Label("設定値", EditorStyles.miniLabel, GUILayout.Width(180));
                    GUILayout.Space(26);
                }
            }
            for (int i = 0; i < action.Anims.Count; i++)
            {
                var anim = action.Anims[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var target = (Animator)EditorGUILayout.ObjectField(anim.Target, typeof(Animator), true);
                    if (EditorGUI.EndChangeCheck()) Edit("Animator の変更", () => anim.Target = target);

                    DrawAnimName(anim);
                    DrawAnimValue(anim);

                    int index = i;
                    if (GUILayout.Button("×", GUILayout.Width(22))) Edit("映像の削除", () => action.Anims.RemoveAt(index));
                }
            }
        }

        /// <summary>パラメータ名。コントローラがあれば一覧から選び、種類も合わせる。無ければ名前と種類を手で入れる。</summary>
        private void DrawAnimName(EffectAnim anim)
        {
            var controller = ControllerOf(anim.Target);
            if (controller == null)
            {
                EditorGUI.BeginChangeCheck();
                string typed = EditorGUILayout.DelayedTextField(anim.Name, GUILayout.Width(90));
                int kind = EditorGUILayout.Popup(BaseKind(anim.Kind), KindNames, GUILayout.Width(56));
                if (EditorGUI.EndChangeCheck())
                {
                    bool fromCall = anim.Kind == EffectPlayer.AnimIntFromCall && kind == EffectPlayer.AnimInt;
                    Edit("パラメータの変更", delegate
                    {
                        anim.Name = typed;
                        anim.Kind = fromCall ? EffectPlayer.AnimIntFromCall : kind;
                    });
                }
                return;
            }

            var parameters = controller.parameters;
            var names = parameters.Select(p => p.name).ToList();
            if (anim.Name.Length > 0 && !names.Contains(anim.Name)) names.Add(anim.Name);
            int index = names.IndexOf(anim.Name);
            int picked = EditorGUILayout.Popup(index, names.ToArray(), GUILayout.Width(150));
            if (picked == index || picked < 0 || picked >= parameters.Length) return;
            var param = parameters[picked];
            int newKind = KindOf(param.type);
            if (newKind < 0) return;
            float value = newKind == EffectPlayer.AnimBool ? (param.defaultBool ? 1f : 0f)
                : newKind == EffectPlayer.AnimInt ? param.defaultInt
                : newKind == EffectPlayer.AnimFloat ? param.defaultFloat : 0f;
            Edit("パラメータの変更", delegate
            {
                anim.Name = param.name;
                anim.Kind = newKind;
                anim.Value = value;
            });
        }

        private void DrawAnimValue(EffectAnim anim)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(180)))
            {
                switch (anim.Kind)
                {
                    case EffectPlayer.AnimTrigger:
                        using (new EditorGUI.DisabledScope(true)) GUILayout.Label("トリガー", GUILayout.Width(176));
                        break;
                    case EffectPlayer.AnimBool:
                    {
                        int v = EditorGUILayout.Popup(anim.Value != 0f ? 1 : 0, BoolNames, GUILayout.Width(176));
                        if (v != (anim.Value != 0f ? 1 : 0)) Edit("設定値の変更", () => anim.Value = v);
                        break;
                    }
                    case EffectPlayer.AnimInt:
                    case EffectPlayer.AnimIntFromCall:
                    {
                        bool fromCall = anim.Kind == EffectPlayer.AnimIntFromCall;
                        int source = EditorGUILayout.Popup(fromCall ? 1 : 0, IntSourceNames, GUILayout.Width(fromCall ? 176 : 110));
                        if (source != (fromCall ? 1 : 0))
                            Edit("設定値の変更", () => anim.Kind = source == 1 ? EffectPlayer.AnimIntFromCall : EffectPlayer.AnimInt);
                        if (!fromCall)
                        {
                            EditorGUI.BeginChangeCheck();
                            int v = EditorGUILayout.DelayedIntField(Mathf.RoundToInt(anim.Value), GUILayout.Width(62));
                            if (EditorGUI.EndChangeCheck()) Edit("設定値の変更", () => anim.Value = v);
                        }
                        break;
                    }
                    case EffectPlayer.AnimFloat:
                    {
                        EditorGUI.BeginChangeCheck();
                        float v = EditorGUILayout.DelayedFloatField(anim.Value, GUILayout.Width(176));
                        if (EditorGUI.EndChangeCheck()) Edit("設定値の変更", () => anim.Value = v);
                        break;
                    }
                }
            }
        }

        private static int BaseKind(int kind)
        {
            return kind == EffectPlayer.AnimIntFromCall ? EffectPlayer.AnimInt : kind;
        }

        private static int KindOf(AnimatorControllerParameterType type)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Trigger: return EffectPlayer.AnimTrigger;
                case AnimatorControllerParameterType.Bool: return EffectPlayer.AnimBool;
                case AnimatorControllerParameterType.Int: return EffectPlayer.AnimInt;
                case AnimatorControllerParameterType.Float: return EffectPlayer.AnimFloat;
            }
            return -1;
        }

        private static AnimatorController ControllerOf(Animator animator)
        {
            if (animator == null) return null;
            var runtime = animator.runtimeAnimatorController;
            var overrideController = runtime as AnimatorOverrideController;
            if (overrideController != null) runtime = overrideController.runtimeAnimatorController;
            return runtime as AnimatorController;
        }

        private void DrawActiveSection(EffectAction action)
        {
            if (SectionHeader("表示")) Edit("表示の追加", () => action.Actives.Add(new EffectActive()));
            for (int i = 0; i < action.Actives.Count; i++)
            {
                var active = action.Actives[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var target = (GameObject)EditorGUILayout.ObjectField(active.Target, typeof(GameObject), true);
                    if (EditorGUI.EndChangeCheck()) Edit("表示の変更", () => active.Target = target);
                    int value = GUILayout.Toolbar(active.Value ? 0 : 1, new[] { "ON", "OFF" }, GUILayout.Width(100));
                    if (value != (active.Value ? 0 : 1)) Edit("表示の変更", () => active.Value = value == 0);
                    int index = i;
                    if (GUILayout.Button("×", GUILayout.Width(22))) Edit("表示の削除", () => action.Actives.RemoveAt(index));
                }
            }
        }

        private void DrawSoundSection(EffectAction action)
        {
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);
            GUI.Label(new Rect(rect.x + 6, rect.y, rect.width, rect.height), "サウンド", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var typeNames = new List<string> { "なし" };
                typeNames.AddRange(SoundTypeNames);
                int type = EditorGUILayout.Popup(action.SoundType + 1, typeNames.ToArray(), GUILayout.Width(130)) - 1;
                if (type != action.SoundType) Edit("サウンドの変更", () => action.SoundType = type);
                using (new EditorGUI.DisabledScope(action.SoundType < 0))
                {
                    var clips = SoundClips();
                    if (clips.Length > 0)
                    {
                        var labels = new string[clips.Length];
                        for (int i = 0; i < clips.Length; i++) labels[i] = i + "  " + (clips[i] != null ? clips[i].name : "");
                        int clip = EditorGUILayout.Popup(Mathf.Clamp(action.SoundClip, 0, clips.Length - 1), labels);
                        if (clip != action.SoundClip) Edit("サウンドの変更", () => action.SoundClip = clip);
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        int clip = EditorGUILayout.DelayedIntField(action.SoundClip);
                        if (EditorGUI.EndChangeCheck()) Edit("サウンドの変更", () => action.SoundClip = Mathf.Max(0, clip));
                    }
                }
            }
        }

        private AudioClip[] SoundClips()
        {
            var mainBoard = player.mainBoard;
            if (mainBoard == null || mainBoard.subBoard == null || mainBoard.subBoard.soundBoard == null || mainBoard.subBoard.soundBoard.clip == null)
                return new AudioClip[0];
            return mainBoard.subBoard.soundBoard.clip;
        }

        private void DrawReelSection(EffectAction action)
        {
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);
            GUI.Label(new Rect(rect.x + 6, rect.y, rect.width, rect.height), "リール", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(action.ReelKeys.Count == 0 ? "なし" : action.ReelLength + " ms", GUILayout.Width(120));
                if (GUILayout.Button("編集", GUILayout.Width(80))) EffectReelWindow.Open(this, action);
                using (new EditorGUI.DisabledScope(action.ReelKeys.Count == 0))
                {
                    if (GUILayout.Button("クリア", GUILayout.Width(80))) Edit("リールのクリア", () => action.ReelKeys.Clear());
                }
            }
        }

        private void DrawCodeSection(EffectAction action)
        {
            if (SectionHeader("コード")) Edit("コードの追加", () => action.Codes.Add(new EffectCode()));
            for (int i = 0; i < action.Codes.Count; i++)
            {
                var code = action.Codes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var target = (UdonSharpBehaviour)EditorGUILayout.ObjectField(code.Target, typeof(UdonSharpBehaviour), true);
                    if (EditorGUI.EndChangeCheck()) Edit("コードの変更", () => code.Target = target);
                    var methods = PublicMethods(code.Target);
                    if (code.Method.Length > 0 && !methods.Contains(code.Method)) methods.Add(code.Method);
                    int index = methods.IndexOf(code.Method);
                    int picked = EditorGUILayout.Popup(index, methods.ToArray(), GUILayout.Width(200));
                    if (picked != index && picked >= 0) Edit("メソッドの変更", () => code.Method = methods[picked]);
                    int row = i;
                    if (GUILayout.Button("×", GUILayout.Width(22))) Edit("コードの削除", () => action.Codes.RemoveAt(row));
                }
            }
        }

        /// <summary>SendCustomEvent で呼べる public メソッド（引数なし）。</summary>
        private static List<string> PublicMethods(UdonSharpBehaviour target)
        {
            var list = new List<string>();
            if (target == null) return list;
            for (var type = target.GetType(); type != null && type != typeof(UdonSharpBehaviour); type = type.BaseType)
            {
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName || m.GetParameters().Length > 0 || m.IsGenericMethod) continue;
                    if (!list.Contains(m.Name)) list.Add(m.Name);
                }
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // ------------------------------------------------------------ 見た目

        private static GUIStyle centerBold, centerMuted, chipLabel;

        private static GUIStyle CenterBold
        {
            get { return centerBold ?? (centerBold = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter }); }
        }

        private static GUIStyle CenterMuted
        {
            get
            {
                if (centerMuted == null)
                {
                    centerMuted = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
                    centerMuted.normal.textColor = new Color(0.4f, 0.4f, 0.4f);
                }
                return centerMuted;
            }
        }

        private static GUIStyle ChipLabel
        {
            get { return chipLabel ?? (chipLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(6, 4, 0, 0) }); }
        }

        private static void DrawChip(Rect rect, string text)
        {
            EditorGUI.DrawRect(rect, ChipColor);
            GUI.Label(rect, text, ChipLabel);
        }
    }
}
