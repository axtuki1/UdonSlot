using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ② 出目: 基本目・リーチ目・禁止目の形、押し順、特定停止位置、出てよい／出さない制御
    public sealed partial class ReelRuleEditorWindow
    {
        private static readonly string[] FirstReelNames = { "すべて", "左第一停止のみ", "中第一停止のみ", "右第一停止のみ" };
        private static readonly string[] RowNames = { "上段", "中段", "下段" };
        private static readonly string[] KindNames = { "基本目", "リーチ目", "禁止目" };

        private static int KindIndex(DesignShape shape) { return shape.forbidden ? 2 : shape.reach ? 1 : 0; }

        private static string KindName(DesignShape shape) { return KindNames[KindIndex(shape)]; }
        private static readonly string[] HowNames = { "通常", "特定停止位置のみ" };

        [SerializeField] private int shapeIndex;
        [SerializeField] private int shapeCell = 4;
        // 出目一覧の絞り込み（種類: -1=すべて、フラグ: 空=すべて）
        [SerializeField] private int shapeKindFilter = -1;
        [SerializeField] private string shapeFlagFilter = "";
        private Vector2 shapeListScroll, shapeCenterScroll, shapeSideScroll;
        private readonly Vector2[] positionScroll = new Vector2[3];

        private DesignShape SelectedShape
        {
            get { return Design.shapes.Count == 0 ? null : Design.shapes[shapeIndex]; }
        }

        private void DrawShapeScreen()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(290))) DrawShapeList();
                var shape = SelectedShape;
                if (shape == null) return;
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    shapeCenterScroll = EditorGUILayout.BeginScrollView(shapeCenterScroll);
                    DrawShapeEditor(shape);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(320)))
                {
                    shapeSideScroll = EditorGUILayout.BeginScrollView(shapeSideScroll);
                    DrawShapeSettings(shape);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        // ------------------------------------------------------------ 左: 出目一覧

        private void DrawShapeList()
        {
            PanelHeader("出目");
            DrawShapeFilter();
            shapeListScroll = EditorGUILayout.BeginScrollView(shapeListScroll);
            for (int i = 0; i < Design.shapes.Count; i++)
            {
                var shape = Design.shapes[i];
                if (!ShapeVisible(shape)) continue;
                var rect = GUILayoutUtility.GetRect(10, 66, GUILayout.ExpandWidth(true));
                if (i == shapeIndex) EditorGUI.DrawRect(rect, SelectedColor);
                DrawMiniShape(new Rect(rect.x + 8, rect.y + 6, 96, 54), shape);
                GUI.Label(new Rect(rect.x + 114, rect.y + 10, rect.width - 120, 18), shape.name);
                Color back, fore;
                if (shape.forbidden) { back = new Color(0.33f, 0.14f, 0.14f); fore = new Color(1f, 0.56f, 0.56f); }
                else if (shape.reach) { back = new Color(0.29f, 0.23f, 0.11f); fore = new Color(0.94f, 0.77f, 0.42f); }
                else { back = new Color(0.23f, 0.23f, 0.23f); fore = new Color(0.66f, 0.66f, 0.66f); }
                string kind = KindName(shape);
                var size = ChipSize(kind);
                DrawChip(new Rect(rect.x + 114, rect.y + 32, size.x, size.y), kind, back, fore);
                int index = i;
                if (Clicked(rect, 0)) SelectShape(index);
                else if (Clicked(rect, 1))
                {
                    SelectShape(index);
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("複製"), false, delegate { DuplicateShape(index); });
                    menu.AddItem(new GUIContent("削除"), false, delegate { DeleteShape(index); });
                    menu.ShowAsContext();
                }
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 出目を追加")) { AddShape(); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(SelectedShape == null))
                    if (GUILayout.Button("複製")) DuplicateShape(shapeIndex);
            }
            GUILayout.Space(4);
        }

        private void DrawShapeFilter()
        {
            var flags = new List<string>();
            foreach (var control in Design.controls)
                foreach (string flag in FlagsOf(control))
                    if (!flags.Contains(flag)) flags.Add(flag);
            using (new EditorGUILayout.HorizontalScope())
            {
                var kinds = new List<string> { "すべての種類" };
                kinds.AddRange(KindNames);
                shapeKindFilter = EditorGUILayout.Popup(shapeKindFilter + 1, kinds.ToArray()) - 1;
                var flagNames = new List<string> { "すべてのフラグ" };
                flagNames.AddRange(flags);
                int current = Mathf.Max(0, flags.IndexOf(shapeFlagFilter) + 1);
                int picked = EditorGUILayout.Popup(current, flagNames.ToArray());
                shapeFlagFilter = picked == 0 ? "" : flags[picked - 1];
            }
            GUILayout.Space(2);
        }

        private static List<string> FlagsOf(DesignControl control)
        {
            return control.IsCell ? new List<string> { control.flag } : control.lotteryFlags;
        }

        /// <summary>種類とフラグの絞り込みに合うか。フラグは、そのフラグを持つ制御で狙う／出てよい／出さない出目。</summary>
        private bool ShapeVisible(DesignShape shape)
        {
            if (shapeKindFilter >= 0 && KindIndex(shape) != shapeKindFilter) return false;
            if (string.IsNullOrEmpty(shapeFlagFilter)) return true;
            foreach (var control in Design.controls)
            {
                if (!FlagsOf(control).Contains(shapeFlagFilter)) continue;
                if (shape.forbidden ? shape.bannedControls.Contains(control.name)
                    : shape.reach ? shape.allowedControls.Contains(control.name) || control.targets.Contains(shape.name)
                    : control.targets.Contains(shape.name)) return true;
            }
            return false;
        }

        private void SelectShape(int index)
        {
            if (index == shapeIndex) return;
            shapeIndex = index;
            GUI.FocusControl(null);
            Repaint();
        }

        private bool ShapeNameTaken(string name)
        {
            return Design.FindShape(name) != null;
        }

        private void AddShape()
        {
            Edit("出目の追加", delegate
            {
                Design.shapes.Add(new DesignShape { name = ReelDesign.NumberedName("出目", ShapeNameTaken) });
                shapeIndex = Design.shapes.Count - 1;
            });
        }

        private void DuplicateShape(int index)
        {
            Edit("出目の複製", delegate
            {
                var copy = Design.shapes[index].Copy();
                copy.name = ReelDesign.NumberedName(Design.shapes[index].name, ShapeNameTaken);
                Design.shapes.Insert(index + 1, copy);
                shapeIndex = index + 1;
            });
        }

        private void DeleteShape(int index)
        {
            Edit("出目の削除", delegate
            {
                string name = Design.shapes[index].name;
                Design.shapes.RemoveAt(index);
                if (Design.FindShape(name) == null)
                    foreach (var control in Design.controls) control.targets.Remove(name);
                shapeIndex = Mathf.Max(0, Mathf.Min(shapeIndex, Design.shapes.Count - 1));
            });
        }

        private void RenameShape(DesignShape shape, string renamed)
        {
            renamed = renamed.Trim();
            if (renamed.Length == 0 || renamed == shape.name || Design.FindShape(renamed) != null) return;
            string old = shape.name;
            Edit("出目名の変更", delegate
            {
                shape.name = renamed;
                foreach (var control in Design.controls)
                {
                    int i = control.targets.IndexOf(old);
                    if (i >= 0) control.targets[i] = renamed;
                }
            });
        }

        // ------------------------------------------------------------ 中央: 形

        private void DrawShapeEditor(DesignShape shape)
        {
            PanelHeader(shape.name + " の形");
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("名前", GUILayout.Width(48));
                string renamed = EditorGUILayout.DelayedTextField(shape.name, GUILayout.Width(220));
                if (renamed != shape.name) RenameShape(shape, renamed);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("押し順", GUILayout.Width(48));
                int first = GUILayout.Toolbar(shape.firstReel + 1, FirstReelNames, GUILayout.Width(440));
                if (first != shape.firstReel + 1) Edit("押し順の変更", delegate { shape.firstReel = first - 1; });
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(12);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawShapeGrid(shape);
                GUILayout.Space(28);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(270))) DrawPalette(shape);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(14);
            if (shape.specificPositions) DrawPositions(shape);
            DrawAvailability(shape);
        }

        private void DrawShapeGrid(DesignShape shape)
        {
            const float labelW = 44, cellW = 172, cellH = 98, gap = 6;
            var area = GUILayoutUtility.GetRect(labelW + 3 * (cellW + gap), 20 + 3 * (cellH + gap), GUILayout.ExpandWidth(false));
            for (int r = 0; r < 3; r++)
                GUI.Label(new Rect(area.x + labelW + r * (cellW + gap), area.y, cellW, 18), ReelRuleEngine.ReelNames[r], CenteredStyle);
            for (int row = 0; row < 3; row++)
            {
                float y = area.y + 20 + row * (cellH + gap);
                GUI.Label(new Rect(area.x, y, labelW, cellH), RowNames[row], EditorStyles.miniLabel);
                for (int r = 0; r < 3; r++)
                {
                    int c = row * 3 + r;
                    var rect = new Rect(area.x + labelW + r * (cellW + gap), y, cellW, cellH);
                    DrawShapeCell(rect, shape.cells[c]);
                    Outline(rect, c == shapeCell ? AccentColor : new Color(0.12f, 0.12f, 0.12f), 2);
                    if (Clicked(rect, 0)) { shapeCell = c; Repaint(); }
                }
            }
        }

        private void DrawShapeCell(Rect rect, DesignCell cell)
        {
            EditorGUI.DrawRect(rect, AnyColor);
            if (cell.IsAny)
            {
                GUI.Label(rect, "ANY", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 15 });
                return;
            }
            var items = new List<KeyValuePair<string, bool>>();
            foreach (string s in cell.include) items.Add(new KeyValuePair<string, bool>(s, false));
            foreach (string s in cell.exclude) items.Add(new KeyValuePair<string, bool>(s, true));
            var inner = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8);
            if (items.Count == 1)
            {
                DrawCellItem(inner, items[0].Key, items[0].Value);
                return;
            }
            const float w = 52, h = 30, gap = 3;
            int perRow = Mathf.Max(1, (int)((inner.width + gap) / (w + gap)));
            for (int i = 0; i < items.Count; i++)
            {
                var item = new Rect(inner.x + (i % perRow) * (w + gap), inner.y + (i / perRow) * (h + gap), w, h);
                if (item.yMax > rect.yMax) break;
                DrawCellItem(item, items[i].Key, items[i].Value);
            }
        }

        private void DrawCellItem(Rect rect, string symbol, bool negated)
        {
            DrawSymbol(rect, symbol);
            if (!negated) return;
            EditorGUI.DrawRect(rect, new Color(0.6f, 0.08f, 0.1f, 0.55f));
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            GUI.Label(rect, "NOT", style);
        }

        private void DrawPalette(DesignShape shape)
        {
            GUILayout.Label("パレット", EditorStyles.miniLabel);
            var cell = shape.cells[shapeCell];
            const float w = 92, h = 55, gap = 6;
            int count = symbols.Count + 1;
            int rows = (count + 2) / 3;
            var area = GUILayoutUtility.GetRect(3 * (w + gap), rows * (h + gap), GUILayout.ExpandWidth(false));
            for (int i = 0; i < count; i++)
            {
                var rect = new Rect(area.x + (i % 3) * (w + gap), area.y + (i / 3) * (h + gap), w, h);
                if (i == symbols.Count)
                {
                    EditorGUI.DrawRect(rect, AnyColor);
                    GUI.Label(rect, "ANY", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
                    Outline(rect, new Color(0.12f, 0.12f, 0.12f), 1);
                    if (Clicked(rect, 0) && !cell.IsAny)
                        Edit("マスを ANY にする", delegate { cell.include.Clear(); cell.exclude.Clear(); });
                    continue;
                }
                string symbol = symbols[i];
                DrawSymbol(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), symbol);
                string state = cell.include.Contains(symbol) ? "含む" : cell.exclude.Contains(symbol) ? "否定" : null;
                if (state != null)
                {
                    var color = state == "含む" ? AccentColor : DangerColor;
                    Outline(rect, color, 2);
                    var size = ChipSize(state);
                    DrawChip(new Rect(rect.xMax - size.x - 2, rect.yMax - size.y - 2, size.x, size.y), state, color, Color.white);
                }
                if (Clicked(rect, 0)) Edit("マスの図柄を変更", delegate { cell.Cycle(symbol); });
            }
        }

        private void DrawPositions(DesignShape shape)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("特定停止位置", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                var style = new GUIStyle(EditorStyles.miniLabel);
                style.normal.textColor = new Color(0.94f, 0.77f, 0.42f);
                GUILayout.Label("リーチ目ミッションに便利", style);
                GUILayout.FlexibleSpace();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int r = 0; r < 3; r++)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(180)))
                    {
                        var positions = shape.Positions(r);
                        var sorted = new List<int>(positions);
                        sorted.Sort();
                        var labels = new List<string>();
                        for (int q = sorted.Count - 1; q >= 0; q--) labels.Add((machine.Strips[r].Length - sorted[q]).ToString());
                        GUILayout.Label(ReelRuleEngine.ReelNames[r] + "リール  " + (labels.Count == 0 ? "指定なし" : string.Join(", ", labels.ToArray()) + " 番"), EditorStyles.miniLabel);
                        positionScroll[r] = EditorGUILayout.BeginScrollView(positionScroll[r], GUILayout.Height(240));
                        string[] strip = machine.Strips[r];
                        for (int i = 0; i < strip.Length; i++)
                        {
                            var row = GUILayoutUtility.GetRect(10, 28, GUILayout.ExpandWidth(true));
                            bool on = positions.Contains(i);
                            if (on) EditorGUI.DrawRect(row, SelectedColor);
                            GUI.Label(new Rect(row.x + 4, row.y, 26, row.height), (strip.Length - i).ToString(), EditorStyles.miniLabel);
                            DrawStripSymbol(new Rect(row.x + 32, row.y + 2, 46, row.height - 4), r, i);
                            if (on) Outline(row, AccentColor, 1);
                            int reel = r, index = i;
                            if (Clicked(row, 0))
                                Edit("特定停止位置の変更", delegate
                                {
                                    var list = shape.Positions(reel);
                                    if (!list.Remove(index)) list.Add(index);
                                });
                        }
                        EditorGUILayout.EndScrollView();
                    }
                    GUILayout.Space(14);
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(10);
        }

        private static bool CellMatches(DesignCell cell, string symbol)
        {
            if (cell == null) return true;
            if (cell.include.Count > 0 && !cell.include.Contains(symbol)) return false;
            return !cell.exclude.Contains(symbol);
        }

        /// <summary>どの制御でも出せない形なら、その理由。</summary>
        private string ShapeImpossibleReason(DesignShape shape)
        {
            for (int r = 0; r < 3; r++)
            {
                var positions = shape.specificPositions ? shape.Positions(r) : null;
                bool any = false;
                for (int pos = 0; pos < machine.Strips[r].Length && !any; pos++)
                {
                    if (positions != null && positions.Count > 0 && !positions.Contains(pos)) continue;
                    bool ok = true;
                    for (int row = 0; row < 3 && ok; row++)
                        ok = CellMatches(shape.cells[row * 3 + r], machine.SymbolAt(r, pos, row));
                    any = ok;
                }
                if (!any) return ReelRuleEngine.ReelNames[r] + "リールに合う停止位置がない";
            }
            return null;
        }

        private void DrawAvailability(DesignShape shape)
        {
            GUILayout.Label("表示可否", EditorStyles.boldLabel);
            var names = new List<string>();
            if (shape.forbidden) names.AddRange(shape.bannedControls);
            else if (shape.reach) names.AddRange(shape.allowedControls);
            else foreach (var control in Design.controls) if (control.targets.Contains(shape.name)) names.Add(control.name);
            if (names.Count == 0) { GUILayout.Label("-", EditorStyles.miniLabel); return; }
            string impossible = ShapeImpossibleReason(shape);
            var okStyle = new GUIStyle(EditorStyles.boldLabel);
            okStyle.normal.textColor = new Color(0.5f, 0.81f, 0.56f);
            var ngStyle = new GUIStyle(EditorStyles.boldLabel);
            ngStyle.normal.textColor = new Color(1f, 0.56f, 0.56f);
            foreach (string name in names)
            {
                var control = Design.FindControl(name);
                var result = ResultOf(name);
                long paths = -1;
                if (result != null)
                {
                    int k = result.Stats.ShapeNames.IndexOf(shape.name);
                    if (k >= 0) paths = result.Stats.ShapeMatched[k];
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    string message;
                    bool ok = shape.forbidden ? paths == 0 : paths > 0;
                    if (shape.forbidden) message = paths < 0 ? "" : ok ? "出ない" : paths + " 経路で出る";
                    else if (ok) message = paths + " 経路";
                    else if (impossible != null) message = impossible;
                    else if (control == null) message = "制御がない";
                    else if (!control.targets.Contains(shape.name)) message = "狙う出目に入っていない";
                    else if (paths < 0) message = "";
                    else message = "役・上位の出目が優先されて出ない";
                    GUILayout.Label(paths < 0 && (impossible == null || shape.forbidden) ? "…" : ok ? "✓" : "✗", ok ? okStyle : ngStyle, GUILayout.Width(16));
                    GUILayout.Label(name, GUILayout.Width(160));
                    GUILayout.Label(message, EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        // ------------------------------------------------------------ 右: 種類・出てよい制御・出し方

        private void DrawShapeSettings(DesignShape shape)
        {
            PanelHeader("種類");
            int kind = GUILayout.SelectionGrid(KindIndex(shape), KindNames, 1, EditorStyles.radioButton);
            if (kind != KindIndex(shape))
            {
                Edit("種類の変更", delegate
                {
                    shape.reach = kind == 1;
                    shape.forbidden = kind == 2;
                    if (shape.forbidden)
                        foreach (var control in Design.controls) control.targets.Remove(shape.name);
                });
                GUIUtility.ExitGUI();
            }
            GUILayout.Space(8);

            if (shape.Exclusive)
            {
                var list = shape.forbidden ? shape.bannedControls : shape.allowedControls;
                PanelHeader(shape.forbidden ? "出さない制御（内部名）" : "出てよい制御（内部名）");
                int columns = 2;
                var shown = Design.controls.FindAll(c => machine == null || !Design.IsUnusedCell(c, machine));
                for (int i = 0; i < shown.Count; i += columns)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int j = i; j < i + columns && j < shown.Count; j++)
                        {
                            string name = shown[j].name;
                            bool on = list.Contains(name);
                            bool next = EditorGUILayout.ToggleLeft(name, on, GUILayout.Width(150));
                            if (next != on)
                                Edit(shape.forbidden ? "出さない制御の変更" : "出てよい制御の変更", delegate
                                {
                                    if (next) list.Add(name);
                                    else list.Remove(name);
                                });
                        }
                    }
                }
                GUILayout.Space(8);
            }

            PanelHeader("出し方");
            int how = GUILayout.SelectionGrid(shape.specificPositions ? 1 : 0, HowNames, 1, EditorStyles.radioButton);
            if (how != (shape.specificPositions ? 1 : 0)) Edit("出し方の変更", delegate { shape.specificPositions = how == 1; });
        }
    }
}
