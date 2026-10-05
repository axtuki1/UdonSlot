using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ① 制御: 制御一覧・設定・滑り表／出目マップ・狙う出目・結果
    public sealed partial class ReelRuleEditorWindow
    {
        private static readonly string[] ViewNames = { "滑り表", "出目マップ" };
        private static readonly string[] StageNames = { "第一停止", "第二停止", "第三停止" };
        private static readonly string[] ReelLetters = { "L", "C", "R" };

        [SerializeField] private int controlIndex;
        [SerializeField] private int view;
        [SerializeField] private int order;
        [SerializeField] private bool manualEditing;
        private int firstStop = -1;
        private int secondStop = -1;
        private Vector2 controlListScroll, tableScroll, mapScroll, controlSideScroll;
        private int mapRow = -1, mapColumn = -1;

        private DesignControl SelectedControl
        {
            get { return Design.controls.Count == 0 ? null : Design.controls[controlIndex]; }
        }

        private void DrawControlScreen()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230))) DrawControlList();
                var control = SelectedControl;
                if (control == null) return;
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) DrawControlCenter(control);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
                {
                    controlSideScroll = EditorGUILayout.BeginScrollView(controlSideScroll);
                    DrawTargets(control);
                    DrawAllowedReach(control);
                    DrawManualPanel(control);
                    DrawResultPanel(control);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        // ------------------------------------------------------------ 左: 制御一覧

        private List<string> WonRolesOf(DesignControl control)
        {
            var won = new List<string>();
            foreach (string flagName in control.lotteryFlags)
            {
                var flag = machine.FindFlag(flagName);
                if (flag != null) foreach (string role in flag.Roles) if (!won.Contains(role)) won.Add(role);
            }
            foreach (string role in control.extraRoles) if (!won.Contains(role)) won.Add(role);
            return won;
        }

        private bool IsRegistered(string name)
        {
            return registeredNames == null || Array.IndexOf(registeredNames, name) >= 0;
        }

        private void DrawControlList()
        {
            PanelHeader("制御");
            controlListScroll = EditorGUILayout.BeginScrollView(controlListScroll);
            for (int i = 0; i < Design.controls.Count; i++)
            {
                var control = Design.controls[i];
                var rect = GUILayoutUtility.GetRect(10, 34, GUILayout.ExpandWidth(true));
                if (i == controlIndex) EditorGUI.DrawRect(rect, SelectedColor);
                string name = (IsRegistered(control.name) ? "" : "⚠ ") + control.name;
                GUI.Label(new Rect(rect.x + 8, rect.y + 2, rect.width - 12, 16), name, EditorStyles.boldLabel);
                var won = WonRolesOf(control);
                GUI.Label(new Rect(rect.x + 8, rect.y + 17, rect.width - 12, 14), won.Count == 0 ? "当選なし" : "当選: " + string.Join(", ", won.ToArray()), EditorStyles.miniLabel);
                int index = i;
                if (Clicked(rect, 0)) { SelectControl(index); }
                else if (Clicked(rect, 1))
                {
                    SelectControl(index);
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("複製"), false, delegate { DuplicateControl(index); });
                    menu.AddItem(new GUIContent("削除"), false, delegate { DeleteControl(index); });
                    menu.ShowAsContext();
                }
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 追加")) { AddControl(); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(SelectedControl == null))
                    if (GUILayout.Button("複製")) DuplicateControl(controlIndex);
            }
            if (GUILayout.Button("抽選表から一括作成"))
            {
                Edit("抽選表から一括作成", delegate { Design.AddControlsFromLottery(machine); });
                GUIUtility.ExitGUI();
            }
            GUILayout.Space(4);
        }

        private void SelectControl(int index)
        {
            if (index == controlIndex) return;
            controlIndex = index;
            mapCache = null;
            GUI.FocusControl(null);
            Repaint();
        }

        private bool ControlNameTaken(string name)
        {
            return Design.FindControl(name) != null;
        }

        private void AddControl()
        {
            Edit("制御の追加", delegate
            {
                Design.controls.Add(new DesignControl { name = ReelDesign.NumberedName("Control", ControlNameTaken) });
                controlIndex = Design.controls.Count - 1;
            });
        }

        private void DuplicateControl(int index)
        {
            Edit("制御の複製", delegate
            {
                var copy = Design.controls[index].Copy();
                copy.name = ReelDesign.NumberedName(Design.controls[index].name, ControlNameTaken);
                Design.controls.Insert(index + 1, copy);
                foreach (var shape in Design.shapes)
                    if (shape.allowedControls.Contains(Design.controls[index].name)) shape.allowedControls.Add(copy.name);
                controlIndex = index + 1;
            });
        }

        private void DeleteControl(int index)
        {
            Edit("制御の削除", delegate
            {
                string name = Design.controls[index].name;
                Design.controls.RemoveAt(index);
                if (Design.FindControl(name) == null)
                    foreach (var shape in Design.shapes) shape.allowedControls.Remove(name);
                controlIndex = Mathf.Max(0, Mathf.Min(controlIndex, Design.controls.Count - 1));
            });
        }

        // ------------------------------------------------------------ 中央: 設定と表

        private void DrawControlCenter(DesignControl control)
        {
            PanelHeader("制御：" + control.name);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("内部名", GUILayout.Width(40));
                string renamed = EditorGUILayout.DelayedTextField(control.name, GUILayout.Width(180));
                if (renamed != control.name) RenameControl(control, renamed);
                GUILayout.Space(16);
                DrawLotteryFlags(control);
                GUILayout.Space(16);
                GUILayout.Label("当選役", GUILayout.Width(40));
                foreach (string role in WonRolesOf(control)) Chip(role, new Color(0.14f, 0.29f, 0.19f), new Color(0.62f, 0.88f, 0.69f));
                GUILayout.Space(4);
                var lottery = ReelMachineReader.LotteryTableOf(machineObject);
                if (lottery != null && Link("抽選表で編集"))
                {
                    Selection.activeObject = lottery;
                    EditorGUIUtility.PingObject(lottery);
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                int nextView = GUILayout.Toolbar(view, ViewNames, GUILayout.Width(170));
                if (nextView != view) view = nextView;
                GUILayout.Space(24);
                int nextOrder = GUILayout.Toolbar(order, ReelRuleEngine.OrderNames);
                if (nextOrder != order) { order = nextOrder; firstStop = secondStop = -1; mapCache = null; mapRow = mapColumn = -1; }
            }
            var result = ResultOf(control.name);
            if (result == null || engine == null) return;
            if (view == 0) DrawSlipTables(control, result);
            else DrawMap(result);
        }

        private void RenameControl(DesignControl control, string renamed)
        {
            renamed = renamed.Trim();
            if (renamed.Length == 0 || renamed == control.name || ControlNameTaken(renamed)) return;
            string old = control.name;
            Edit("内部名の変更", delegate
            {
                control.name = renamed;
                foreach (var shape in Design.shapes)
                {
                    int i = shape.allowedControls.IndexOf(old);
                    if (i >= 0) shape.allowedControls[i] = renamed;
                }
            });
        }

        private void DrawLotteryFlags(DesignControl control)
        {
            GUILayout.Label("抽選フラグ", GUILayout.Width(56));
            var flagBack = new Color(0.12f, 0.23f, 0.36f);
            var flagFore = new Color(0.65f, 0.80f, 0.96f);
            foreach (string flag in control.lotteryFlags.ToArray())
            {
                string name = flag;
                if (Chip(machine.FindFlag(name) == null ? "⚠ " + name : name, flagBack, flagFore))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("外す"), false, delegate { Edit("抽選フラグを外す", delegate { control.lotteryFlags.Remove(name); }); });
                    menu.ShowAsContext();
                }
                GUILayout.Space(3);
            }
            foreach (string role in control.extraRoles.ToArray())
            {
                string name = role;
                if (Chip(machine.FindRole(name) == null ? "⚠ " + name : name, flagBack, flagFore))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("外す"), false, delegate { Edit("抽選フラグを外す", delegate { control.extraRoles.Remove(name); }); });
                    menu.ShowAsContext();
                }
                GUILayout.Space(3);
            }
            if (GUILayout.Button("＋", EditorStyles.miniButton, GUILayout.Width(24)))
            {
                var menu = new GenericMenu();
                foreach (var flag in machine.Flags)
                {
                    string name = flag.Name;
                    if (control.lotteryFlags.Contains(name)) continue;
                    menu.AddItem(new GUIContent(name), false, delegate { Edit("抽選フラグを追加", delegate { control.lotteryFlags.Add(name); }); });
                }
                bool separated = false;
                foreach (var role in machine.Roles)
                {
                    string name = role.Name;
                    if (!role.IsBonus || control.extraRoles.Contains(name) || machine.FindFlag(name) != null) continue;
                    if (!separated) { menu.AddSeparator(""); separated = true; }
                    menu.AddItem(new GUIContent(name), false, delegate { Edit("抽選フラグを追加", delegate { control.extraRoles.Add(name); }); });
                }
                menu.ShowAsContext();
            }
        }

        // ------------------------------------------------------------ 滑り表

        /// <summary>第一停止で止まりうる位置の一覧。</summary>
        private List<int> FirstStopChoices(ControlResult result)
        {
            var list = new List<int>();
            var reached = engine.FirstStops(result, order);
            for (int i = 0; i < reached.Length; i++) if (reached[i]) list.Add(i);
            return list;
        }

        private List<int> SecondStopChoices(ControlResult result, int first)
        {
            var list = new List<int>();
            var prior = new[] { -1, -1, -1 };
            prior[ReelRuleEngine.Orders[order][0]] = first;
            var table = engine.FindTable(result, ReelRuleEngine.PatternFor(order, 1), prior);
            if (table == null) return list;
            int n = table.Slips.Length;
            var reached = new bool[n];
            for (int press = 0; press < n; press++) reached[Machine.Wrap(press - table.Slips[press], n)] = true;
            for (int i = 0; i < n; i++) if (reached[i]) list.Add(i);
            return list;
        }

        private void DrawSlipTables(DesignControl control, ControlResult result)
        {
            int[] reels = ReelRuleEngine.Orders[order];
            var firstChoices = FirstStopChoices(result);
            if (!firstChoices.Contains(firstStop)) firstStop = firstChoices.Count > 0 ? firstChoices[0] : -1;
            var secondChoices = firstStop >= 0 ? SecondStopChoices(result, firstStop) : new List<int>();
            if (!secondChoices.Contains(secondStop)) secondStop = secondChoices.Count > 0 ? secondChoices[0] : -1;

            tableScroll = EditorGUILayout.BeginScrollView(tableScroll);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(200)))
                    {
                        int reel = reels[stage];
                        GUILayout.Label(StageNames[stage] + "：" + ReelRuleEngine.ReelNames[reel] + "リール", EditorStyles.boldLabel);
                        var prior = new[] { -1, -1, -1 };
                        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(20)))
                        {
                            if (stage >= 1) firstStop = StopPopup(reels[0], firstStop, firstChoices);
                            if (stage >= 2) secondStop = StopPopup(reels[1], secondStop, secondChoices);
                            GUILayout.FlexibleSpace();
                        }
                        if (stage >= 1) prior[reels[0]] = firstStop;
                        if (stage >= 2) prior[reels[1]] = secondStop;
                        var table = engine.FindTable(result, ReelRuleEngine.PatternFor(order, stage), prior);
                        if (table == null) { GUILayout.Label("-"); continue; }
                        DrawSlipTable(control, table);
                    }
                    if (stage < 2) GUILayout.Space(8);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private int StopPopup(int reel, int current, List<int> choices)
        {
            if (choices.Count == 0) return current;
            var labels = new string[choices.Count];
            for (int i = 0; i < choices.Count; i++) labels[i] = ReelRuleEngine.ReelNames[reel] + " " + (choices[i] + 1);
            int selected = Mathf.Max(0, choices.IndexOf(current));
            return choices[EditorGUILayout.Popup(selected, labels, GUILayout.Width(70))];
        }

        private void DrawSlipTable(DesignControl control, SlipTable table)
        {
            const float rowHeight = 29;
            var head = GUILayoutUtility.GetRect(10, 16, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(head.x, head.y, 26, 16), "コマ", EditorStyles.miniLabel);
            GUI.Label(new Rect(head.x + 28, head.y, 50, 16), "押し位置", EditorStyles.miniLabel);
            GUI.Label(new Rect(head.x + 87, head.y, 30, 16), "滑り", EditorStyles.miniLabel);
            GUI.Label(new Rect(head.x + 117, head.y, 60, 16), "決め手", EditorStyles.miniLabel);
            for (int press = 0; press < table.Slips.Length; press++)
            {
                var row = GUILayoutUtility.GetRect(10, rowHeight, GUILayout.ExpandWidth(true));
                if (table.Manual[press])
                {
                    EditorGUI.DrawRect(row, new Color(0.23f, 0.18f, 0.12f));
                    Outline(row, ManualColor, 1);
                }
                else if (press % 2 == 1) EditorGUI.DrawRect(row, new Color(0.18f, 0.18f, 0.18f));
                GUI.Label(new Rect(row.x + 2, row.y, 24, row.height), (press + 1).ToString(), EditorStyles.miniLabel);
                DrawStripSymbol(new Rect(row.x + 28, row.y + 2, 51, row.height - 4), table.Reel, press);
                GUI.Label(new Rect(row.x + 87, row.y, 24, row.height), table.Slips[press].ToString(), EditorStyles.boldLabel);
                string reason = table.Unavoidable[press] && !table.Manual[press] ? "回避不能" : table.Reasons[press];
                Color back, fore;
                ReasonColors(reason, out back, out fore);
                var size = ChipSize(reason);
                DrawChip(new Rect(row.x + 117, row.y + (row.height - size.y) / 2, size.x, size.y), reason, back, fore);
                if (table.Manual[press] && table.Unavoidable[press])
                    GUI.Label(new Rect(row.x + 121 + size.x, row.y, 20, row.height), "⚠", EditorStyles.boldLabel);
                if (manualEditing)
                {
                    EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
                    int p = press;
                    if (Clicked(row, 0)) ShowSlipMenu(control, table, p);
                }
            }
        }

        private void ShowSlipMenu(DesignControl control, SlipTable table, int press)
        {
            var prior = table.Prior;
            var current = control.FindOverride(table.Pattern, prior, press);
            int maxSlip = engine.MaxSlip;
            var menu = new GenericMenu();
            for (int s = 0; s <= maxSlip; s++)
            {
                int slip = s;
                menu.AddItem(new GUIContent(s.ToString()), current != null && current.slip == s, delegate
                {
                    Edit("滑り数の手動指定", delegate
                    {
                        var o = control.FindOverride(table.Pattern, prior, press);
                        if (o == null)
                        {
                            o = new DesignOverride { pattern = table.Pattern, left = prior[0], center = prior[1], right = prior[2], press = press };
                            control.overrides.Add(o);
                        }
                        o.slip = slip;
                    });
                });
            }
            menu.AddSeparator("");
            if (current != null)
                menu.AddItem(new GUIContent("自動"), false, delegate
                {
                    Edit("滑り数の手動指定を解除", delegate { control.overrides.Remove(control.FindOverride(table.Pattern, prior, press)); });
                });
            else
                menu.AddDisabledItem(new GUIContent("自動"), true);
            menu.ShowAsContext();
        }

        // ------------------------------------------------------------ 出目マップ

        private sealed class MapCache
        {
            public ControlResult Result;
            public int Order;
            public int FirstStop;
            public int[,] Cells;
            public List<string> Labels = new List<string>();
            public List<int> Counts = new List<int>();
            public List<Color> Colors = new List<Color>();
        }

        private MapCache mapCache;

        private static readonly Color[] MapPalette =
        {
            new Color(0.23f, 0.62f, 0.33f), new Color(0.30f, 0.56f, 0.84f), new Color(0.80f, 0.55f, 0.20f),
            new Color(0.62f, 0.40f, 0.80f), new Color(0.20f, 0.65f, 0.65f), new Color(0.75f, 0.70f, 0.25f),
            new Color(0.80f, 0.42f, 0.60f), new Color(0.45f, 0.55f, 0.30f)
        };

        private string Outcome(ControlResult result, int[] stops, int[] orders, bool smallWon)
        {
            var parts = new List<string>();
            foreach (string role in engine.FormedRoles(stops, orders))
                parts.Add(result.WonRoles.Contains(role) ? role : "非成立 " + role);
            parts.AddRange(engine.MatchedShapes(stops, orders));
            if (parts.Count > 0) return string.Join("・", parts.ToArray());
            return smallWon ? "取りこぼし" : "ハズレ目";
        }

        private MapCache BuildMap(ControlResult result)
        {
            int[] reels = ReelRuleEngine.Orders[order];
            int n2 = machine.Strips[reels[1]].Length, n3 = machine.Strips[reels[2]].Length;
            var map = new MapCache { Result = result, Order = order, FirstStop = firstStop, Cells = new int[n2, n3] };
            bool smallWon = false;
            foreach (string role in result.WonRoles)
            {
                var r = machine.FindRole(role);
                if (r != null && !r.IsBonus) smallWon = true;
            }
            var index = new Dictionary<string, int>();
            for (int p2 = 0; p2 < n2; p2++)
                for (int p3 = 0; p3 < n3; p3++)
                {
                    int[] orders;
                    var stops = engine.Resolve(result, order, firstStop, p2, p3, out orders);
                    string label = stops == null ? "-" : Outcome(result, stops, orders, smallWon);
                    int k;
                    if (!index.TryGetValue(label, out k))
                    {
                        k = map.Labels.Count;
                        index[label] = k;
                        map.Labels.Add(label);
                        map.Counts.Add(0);
                    }
                    map.Counts[k]++;
                    map.Cells[p2, p3] = k;
                }
            // 多い順に色を割り当てる。非成立は赤、取りこぼしは灰色、ハズレ目は青灰色。
            var rank = new List<int>();
            for (int k = 0; k < map.Labels.Count; k++) rank.Add(k);
            rank.Sort(delegate (int a, int b) { return map.Counts[b].CompareTo(map.Counts[a]); });
            var colors = new Color[map.Labels.Count];
            int next = 0;
            foreach (int k in rank)
            {
                string label = map.Labels[k];
                if (label.Contains("非成立")) colors[k] = DangerColor;
                else if (label == "取りこぼし") colors[k] = new Color(0.36f, 0.36f, 0.36f);
                else if (label == "ハズレ目" || label == "-") colors[k] = HazureColor;
                else colors[k] = MapPalette[next++ % MapPalette.Length];
            }
            map.Colors.AddRange(colors);
            return map;
        }

        private void DrawMap(ControlResult result)
        {
            int[] reels = ReelRuleEngine.Orders[order];
            var firstChoices = FirstStopChoices(result);
            if (!firstChoices.Contains(firstStop)) { firstStop = firstChoices.Count > 0 ? firstChoices[0] : -1; mapCache = null; }
            if (firstStop < 0) return;
            if (mapCache == null || mapCache.Result != result || mapCache.Order != order || mapCache.FirstStop != firstStop)
            {
                mapCache = BuildMap(result);
            }
            var map = mapCache;
            int r1 = reels[0], r2 = reels[1], r3 = reels[2];
            int n1 = machine.Strips[r1].Length, n2 = machine.Strips[r2].Length, n3 = machine.Strips[r3].Length;

            mapScroll = EditorGUILayout.BeginScrollView(mapScroll);
            using (new EditorGUILayout.HorizontalScope())
            {
                // 第一停止の止まり位置
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(70)))
                {
                    GUILayout.Label("第一停止 " + ReelRuleEngine.ReelNames[r1], EditorStyles.miniLabel);
                    for (int i = 0; i < n1; i++)
                    {
                        var rect = GUILayoutUtility.GetRect(62, 24, GUILayout.Width(62), GUILayout.Height(24));
                        bool reachable = firstChoices.Contains(i);
                        var color = GUI.color;
                        if (!reachable) GUI.color = new Color(1, 1, 1, 0.35f);
                        DrawStripSymbol(rect, r1, i);
                        GUI.color = color;
                        if (!reachable) EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.55f));
                        if (i == firstStop) Outline(rect, AccentColor, 2);
                        int stop = i;
                        if (reachable && Clicked(rect, 0)) { firstStop = stop; mapCache = null; mapRow = mapColumn = -1; Repaint(); }
                    }
                    GUILayout.Label(ReelLetters[r1] + (firstStop + 1), EditorStyles.boldLabel);
                }
                GUILayout.Space(8);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawLegend(map);
                    DrawMatrix(map, r2, r3, n2, n3);
                    DrawMapSelection(result, map, r2, r3);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawLegend(MapCache map)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var ranked = new List<int>();
                for (int k = 0; k < map.Labels.Count; k++) ranked.Add(k);
                ranked.Sort(delegate (int a, int b) { return map.Counts[b].CompareTo(map.Counts[a]); });
                foreach (int k in ranked)
                {
                    var swatch = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
                    swatch.y += 2;
                    EditorGUI.DrawRect(swatch, map.Colors[k]);
                    GUILayout.Label(map.Labels[k] + " " + map.Counts[k], EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    GUILayout.Space(8);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawMatrix(MapCache map, int r2, int r3, int n2, int n3)
        {
            const float cw = 40, ch = 26, numW = 24, imgW = 52;
            var area = GUILayoutUtility.GetRect(numW + imgW + n3 * cw + 2, ch * 2 + n2 * ch + 2, GUILayout.ExpandWidth(false));
            float x0 = area.x + numW + imgW, y0 = area.y + ch * 2;
            GUI.Label(new Rect(area.x, area.y, numW + imgW, ch), ReelRuleEngine.ReelNames[r2] + "＼" + ReelRuleEngine.ReelNames[r3], CenteredStyle);
            for (int c = 0; c < n3; c++)
            {
                GUI.Label(new Rect(x0 + c * cw, area.y, cw, ch), (c + 1).ToString(), CenteredStyle);
                DrawStripSymbol(new Rect(x0 + c * cw + 1, area.y + ch + 1, cw - 2, ch - 2), r3, c);
            }
            for (int r = 0; r < n2; r++)
            {
                float y = y0 + r * ch;
                GUI.Label(new Rect(area.x, y, numW, ch), (r + 1).ToString(), CenteredStyle);
                DrawStripSymbol(new Rect(area.x + numW + 1, y + 1, imgW - 2, ch - 2), r2, r);
                for (int c = 0; c < n3; c++)
                {
                    var cell = new Rect(x0 + c * cw, y, cw - 1, ch - 1);
                    EditorGUI.DrawRect(cell, map.Colors[map.Cells[r, c]]);
                    if (r == mapRow && c == mapColumn) Outline(cell, Color.white, 2);
                    int row = r, column = c;
                    if (Clicked(cell, 0)) { mapRow = row; mapColumn = column; Repaint(); }
                }
            }
        }

        private void DrawMapSelection(ControlResult result, MapCache map, int r2, int r3)
        {
            if (mapRow < 0 || mapColumn < 0 || mapRow >= map.Cells.GetLength(0) || mapColumn >= map.Cells.GetLength(1)) return;
            int[] orders;
            var stops = engine.Resolve(result, order, firstStop, mapRow, mapColumn, out orders);
            if (stops == null) return;
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                var preview = GUILayoutUtility.GetRect(139, 79, GUILayout.Width(139), GUILayout.Height(79));
                DrawWindow(preview, stops);
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(ReelRuleEngine.ReelNames[r2] + " " + (mapRow + 1) + "押し × " + ReelRuleEngine.ReelNames[r3] + " " + (mapColumn + 1) + "押し", EditorStyles.miniLabel);
                    int k = map.Cells[mapRow, mapColumn];
                    Chip(map.Labels[k], map.Colors[k], Color.white);
                    GUILayout.Space(4);
                    if (Link("滑り表"))
                    {
                        secondStop = stops[r2];
                        view = 0;
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }

        // ------------------------------------------------------------ 右: 狙う出目・リーチ目・手動指定・結果

        private void DrawShapeTags(DesignShape shape)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var gray = new Color(0.23f, 0.23f, 0.23f);
                var text = new Color(0.66f, 0.66f, 0.66f);
                Chip(shape.reach ? "リーチ目" : "基本目", gray, text);
                GUILayout.Space(3);
                Chip(OrderTag(shape.firstReel), gray, text);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawTargets(DesignControl control)
        {
            PanelHeader("狙う出目（上から優先）");
            for (int i = 0; i < control.targets.Count; i++)
            {
                string name = control.targets[i];
                var shape = Design.FindShape(name);
                var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(58));
                GUILayout.Label((i + 1).ToString(), EditorStyles.miniLabel, GUILayout.Width(14));
                var mini = GUILayoutUtility.GetRect(96, 54, GUILayout.Width(96), GUILayout.Height(54));
                if (shape != null) DrawMiniShape(mini, shape);
                GUILayout.Space(8);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(shape == null ? "⚠ " + name : name);
                    if (shape != null) DrawShapeTags(shape);
                }
                EditorGUILayout.EndHorizontal();
                int index = i;
                if (Clicked(rect, 1))
                {
                    var menu = new GenericMenu();
                    if (index > 0) menu.AddItem(new GUIContent("上へ"), false, delegate { Edit("狙う出目の並べ替え", delegate { Swap(control.targets, index, index - 1); }); });
                    else menu.AddDisabledItem(new GUIContent("上へ"));
                    if (index < control.targets.Count - 1) menu.AddItem(new GUIContent("下へ"), false, delegate { Edit("狙う出目の並べ替え", delegate { Swap(control.targets, index, index + 1); }); });
                    else menu.AddDisabledItem(new GUIContent("下へ"));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("外す"), false, delegate { Edit("狙う出目を外す", delegate { control.targets.RemoveAt(index); }); });
                    menu.ShowAsContext();
                }
                GUILayout.Space(4);
            }
            if (GUILayout.Button("＋ 出目を追加", GUILayout.Width(110)))
            {
                var menu = new GenericMenu();
                foreach (var shape in Design.shapes)
                {
                    string name = shape.name;
                    if (control.targets.Contains(name)) continue;
                    if (shape.reach && !shape.allowedControls.Contains(control.name)) menu.AddDisabledItem(new GUIContent(name));
                    else menu.AddItem(new GUIContent(name), false, delegate { Edit("狙う出目を追加", delegate { control.targets.Add(name); }); });
                }
                if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("-"));
                menu.ShowAsContext();
            }
            GUILayout.Space(8);
        }

        private static void Swap(List<string> list, int a, int b)
        {
            string t = list[a];
            list[a] = list[b];
            list[b] = t;
        }

        private void DrawAllowedReach(DesignControl control)
        {
            var header = PanelHeaderRect("この制御で出てよいリーチ目");
            var link = new Rect(header.xMax - 60, header.y + 3, 56, header.height - 6);
            EditorGUIUtility.AddCursorRect(link, MouseCursor.Link);
            if (GUI.Button(link, "②で編集", LinkStyle)) { screen = 1; GUI.FocusControl(null); }
            var names = new List<string>();
            foreach (var shape in Design.shapes)
                if (shape.reach && shape.allowedControls.Contains(control.name)) names.Add(shape.name);
            GUILayout.Label(names.Count == 0 ? "なし" : string.Join("、", names.ToArray()), EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);
        }

        private void DrawManualPanel(DesignControl control)
        {
            var header = PanelHeaderRect("Advanced：滑り数の手動指定");
            if (control.overrides.Count > 0)
            {
                var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                style.normal.textColor = ManualColor;
                GUI.Label(new Rect(header.xMax - 100, header.y, 94, header.height), control.overrides.Count + "コマ指定中", style);
            }
            manualEditing = EditorGUILayout.ToggleLeft("有効", manualEditing);
            GUILayout.Space(8);
        }

        private void DrawResultPanel(DesignControl control)
        {
            PanelHeader("結果");
            var result = ResultOf(control.name);
            if (result == null) return;
            var s = result.Stats;
            for (int i = 0; i < s.RoleNames.Length; i++)
                if (s.RoleWon[i]) RateBar(s.RoleNames[i], s.RoleFormed[i], s.Paths, new Color(0.23f, 0.62f, 0.33f));
            for (int k = 0; k < s.ShapeNames.Count; k++)
                if (s.ShapeMatched[k] > 0) RateBar(s.ShapeNames[k], s.ShapeMatched[k], s.Paths, AccentColor);
            GUILayout.Space(6);

            long leaks = 0;
            for (int k = 0; k < s.ShapeNames.Count; k++)
            {
                var shape = Design.FindShape(s.ShapeNames[k]);
                if (shape != null && shape.reach && !shape.allowedControls.Contains(control.name)) leaks += s.ShapeMatched[k];
            }
            int manualWarnings = 0, unavoidable = 0;
            foreach (var t in result.Tables)
                for (int p = 0; p < t.Slips.Length; p++)
                {
                    if (!t.Unavoidable[p]) continue;
                    if (t.Manual[p]) manualWarnings++; else unavoidable++;
                }
            CheckRow("非成立役の入賞", s.ViolationPaths);
            CheckRow("リーチ目の漏れ", leaks);
            CheckRow("手動指定による警告", manualWarnings);
            if (unavoidable > 0) CheckRow("回避不能", unavoidable);
        }

        private static void RateBar(string label, long part, long whole, Color color)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(ReelRuleReport.Percent(part, whole), EditorStyles.miniBoldLabel);
            }
            var bar = GUILayoutUtility.GetRect(10, 6, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, new Color(0.12f, 0.12f, 0.12f));
            if (whole > 0) EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * part / whole, bar.height), color);
            GUILayout.Space(4);
        }

        private static void CheckRow(string label, long value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                var style = new GUIStyle(EditorStyles.miniBoldLabel);
                style.normal.textColor = value == 0 ? new Color(0.5f, 0.81f, 0.56f) : new Color(1f, 0.56f, 0.56f);
                GUILayout.Label(value.ToString(), style);
            }
        }
    }
}
