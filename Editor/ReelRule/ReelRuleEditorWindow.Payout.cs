using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ④ 配当表: 図柄の組合せ・種別・BET別の払出・停止順・有効ライン、移行図柄の出し方
    public sealed partial class ReelRuleEditorWindow
    {
        private static readonly string[] RoleTypeNames = { "小役", "リプレイ", "ボーナス", "移行図柄" };
        private static readonly string[] OrderNames = { "ANY", "左中右", "左右中", "中左右", "中右左", "右左中", "右中左" };
        /// <summary>押す順（OrderNames の 1〜6）。値はリールのインデックス。</summary>
        private static readonly int[][] PressOrders =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };
        private static readonly string[] LineRowNames = { "上段", "中段", "下段" };
        private static readonly string[] TransitionHowNames = { "いつでも", "押し順不正解のときだけ" };

        [SerializeField] private string payoutSelected = "";
        private Vector2 payoutScroll, payoutSideScroll;

        private void EditPayout(string label, Action<PayoutModel> change)
        {
            var table = MachinePayoutTable;
            if (table == null) return;
            var model = PayoutModel.Load(table);
            change(model);
            model.Save(label);
            Changed();
            Repaint();
        }

        private void DrawPayoutScreen()
        {
            var table = MachinePayoutTable;
            if (table == null) return;
            var model = PayoutModel.Load(table);
            string saveLabel = null;
            var selected = model.Find(payoutSelected);
            if (selected == null && model.Rows.Count > 0) { selected = model.Rows[0]; payoutSelected = selected.Name; }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    var header = PanelHeaderRect("図柄の組合せ");
                    if (GUI.Button(new Rect(header.xMax - 70, header.y + 3, 64, header.height - 6), "＋ 追加", EditorStyles.miniButton))
                    {
                        string name = ReelDesign.NumberedName("Role", n => model.Find(n) != null);
                        model.Rows.Add(new PayoutRow { Name = name });
                        payoutSelected = name;
                        saveLabel = "役の追加";
                    }
                    payoutScroll = EditorGUILayout.BeginScrollView(payoutScroll);
                    if (DrawPayoutGrid(model)) saveLabel = "配当表の変更";
                    GUILayout.Space(12);
                    if (DrawPayLines(model)) saveLabel = "有効ラインの変更";
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(300)))
                {
                    payoutSideScroll = EditorGUILayout.BeginScrollView(payoutSideScroll);
                    if (selected != null) DrawPayoutSide(selected);
                    EditorGUILayout.EndScrollView();
                }
            }
            if (saveLabel != null)
            {
                model.Save(saveLabel);
                Changed();
            }
        }

        private static readonly float[] PayoutColumns = { 130, 110, 86, 128, 128, 128, 50, 50, 50, 70, 110 };
        private static readonly string[] PayoutHeads = { "名前", "内部名", "種別", "左", "中", "右", "BET1", "BET2", "BET3", "停止順", "RT" };

        private bool DrawPayoutGrid(PayoutModel model)
        {
            bool changed = false;
            float width = 0;
            foreach (float w in PayoutColumns) width += w;
            var head = GUILayoutUtility.GetRect(width, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(head, HeaderColor);
            float x = head.x;
            for (int c = 0; c < PayoutColumns.Length; c++)
            {
                GUI.Label(new Rect(x + 4, head.y + 2, PayoutColumns[c] - 8, 16), PayoutHeads[c], EditorStyles.miniBoldLabel);
                x += PayoutColumns[c];
            }
            var bonus = MachineBonusTable;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                var r = model.Rows[i];
                var rect = GUILayoutUtility.GetRect(width, 34, GUILayout.ExpandWidth(true));
                if (r.Name == payoutSelected) EditorGUI.DrawRect(rect, new Color(0.18f, 0.26f, 0.35f));
                else if (i % 2 == 1) EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
                x = rect.x;
                float y = rect.y + 8;
                Func<int, Rect> cell = c => { float left = rect.x; for (int k = 0; k < c; k++) left += PayoutColumns[k]; return new Rect(left + 3, y, PayoutColumns[c] - 6, 18); };

                string display = EditorGUI.DelayedTextField(cell(0), r.Label);
                if (display != r.Label) { r.Display = display == r.Name ? "" : display; changed = true; }
                string renamed = EditorGUI.DelayedTextField(cell(1), r.Name);
                if (renamed != r.Name && renamed.Length > 0 && model.Find(renamed) == null)
                {
                    RenamePayoutRole(r.Name, renamed);
                    if (payoutSelected == r.Name) payoutSelected = renamed;
                    r.Name = renamed;
                    changed = true;
                }
                int type = EditorGUI.Popup(cell(2), (int)r.Type, RoleTypeNames);
                if (type != (int)r.Type) { r.Type = (PayoutRoleType)type; changed = true; }
                for (int reel = 0; reel < 3; reel++)
                {
                    var c = cell(3 + reel);
                    var symbolRect = new Rect(c.x, rect.y + 3, c.width, rect.height - 6);
                    DrawPayoutSymbols(symbolRect, r.Symbols[reel]);
                    if (Clicked(symbolRect, 0))
                    {
                        payoutSelected = r.Name;
                        PopupWindow.Show(symbolRect, new SymbolPickerPopup(this, r.Name, reel));
                    }
                }
                bool paid = r.Type == PayoutRoleType.SmallRole || r.Type == PayoutRoleType.Bonus;
                for (int b = 0; b < 3; b++)
                {
                    if (!paid) { GUI.Label(cell(6 + b), "—", MutedLabel); continue; }
                    int value = EditorGUI.DelayedIntField(cell(6 + b), r.Payouts[b]);
                    if (value != r.Payouts[b] && value >= 0) { r.Payouts[b] = value; changed = true; }
                }
                int order = r.OrderAny ? 0 : OrderIndex(r.Order);
                int nextOrder = EditorGUI.Popup(cell(9), order, OrderNames);
                if (nextOrder != order)
                {
                    r.OrderAny = nextOrder == 0;
                    if (nextOrder > 0) r.Order = StopOrderOf(PressOrders[nextOrder - 1]);
                    changed = true;
                }
                GUI.Label(cell(10), PayoutRtLabel(bonus, r.Name), MutedLabel);

                if (Clicked(rect, 0)) { payoutSelected = r.Name; GUI.FocusControl(null); Repaint(); }
                if (RightClicked(rect))
                {
                    string name = r.Name;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("上へ"), false, delegate { EditPayout("役の移動", m => Swap(m.Rows, m.Rows.FindIndex(p => p.Name == name), -1)); });
                    menu.AddItem(new GUIContent("下へ"), false, delegate { EditPayout("役の移動", m => Swap(m.Rows, m.Rows.FindIndex(p => p.Name == name), 1)); });
                    menu.AddItem(new GUIContent("複製"), false, delegate
                    {
                        EditPayout("役の複製", m =>
                        {
                            int at = m.Rows.FindIndex(p => p.Name == name);
                            var copy = m.Rows[at].Copy();
                            copy.Name = ReelDesign.NumberedName(name, n => m.Find(n) != null);
                            copy.Display = "";
                            m.Rows.Insert(at + 1, copy);
                        });
                    });
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("削除"), false, delegate { EditPayout("役の削除", m => m.Rows.RemoveAll(p => p.Name == name)); });
                    menu.ShowAsContext();
                }
            }
            return changed;
        }

        private static List<string> SplitSymbols(string value)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(value)) return list;
            foreach (string part in value.Split(','))
            {
                string symbol = part.Trim();
                if (symbol.Length > 0 && !list.Contains(symbol)) list.Add(symbol);
            }
            return list;
        }

        /// <summary>配当表のセル。図柄を画像で並べ、入りきらない分は「+N」。空は ANY。</summary>
        private void DrawPayoutSymbols(Rect rect, string value)
        {
            var list = SplitSymbols(value);
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
            if (list.Count == 0)
            {
                GUI.Label(rect, "ANY", CenteredStyle);
                return;
            }
            const float gap = 2;
            float h = rect.height - 4, w = Mathf.Round(h * 1.5f);
            int fit = Mathf.Max(1, (int)((rect.width - 4 + gap) / (w + gap)));
            int shown = list.Count > fit ? fit - 1 : list.Count;
            float x = rect.x + 2;
            for (int i = 0; i < shown; i++, x += w + gap) DrawSymbol(new Rect(x, rect.y + 2, w, h), list[i]);
            if (list.Count > shown)
            {
                string more = "+" + (list.Count - shown);
                var size = ChipSize(more);
                DrawChip(new Rect(x, rect.y + (rect.height - 16) / 2, size.x, 16), more, MutedBack, MutedText);
            }
        }

        /// <summary>図柄をクリックで選ぶ（複数可）。何も選ばなければ ANY。</summary>
        private sealed class SymbolPickerPopup : PopupWindowContent
        {
            private const float W = 72, H = 46, Gap = 4;
            private const int PerRow = 5;
            private readonly ReelRuleEditorWindow window;
            private readonly string role;
            private readonly int reel;

            public SymbolPickerPopup(ReelRuleEditorWindow window, string role, int reel)
            {
                this.window = window;
                this.role = role;
                this.reel = reel;
            }

            private List<string> Palette()
            {
                var list = new List<string>(window.PayoutPalette());
                var row = window.CurrentPayoutRow(role);
                if (row != null) foreach (string s in SplitSymbols(row.Symbols[reel])) if (!list.Contains(s)) list.Add(s);
                return list;
            }

            public override Vector2 GetWindowSize()
            {
                int count = Palette().Count + 1;
                int rows = (count + PerRow - 1) / PerRow;
                return new Vector2(PerRow * (W + Gap) + Gap, rows * (H + Gap) + Gap);
            }

            public override void OnGUI(Rect rect)
            {
                var row = window.CurrentPayoutRow(role);
                if (row == null) { editorWindow.Close(); return; }
                var selected = SplitSymbols(row.Symbols[reel]);
                var palette = Palette();
                for (int i = 0; i <= palette.Count; i++)
                {
                    var cell = new Rect(Gap + (i % PerRow) * (W + Gap), Gap + (i / PerRow) * (H + Gap), W, H);
                    if (i == palette.Count)
                    {
                        EditorGUI.DrawRect(cell, AnyColor);
                        GUI.Label(cell, "ANY", CenteredStyle);
                        if (selected.Count == 0) Outline(cell, AccentColor, 2);
                        if (Clicked(cell, 0) && selected.Count > 0) Set(new List<string>());
                        continue;
                    }
                    string symbol = palette[i];
                    window.DrawSymbol(new Rect(cell.x + 2, cell.y + 2, cell.width - 4, cell.height - 4), symbol);
                    if (selected.Contains(symbol)) Outline(cell, AccentColor, 2);
                    if (Clicked(cell, 0))
                    {
                        if (!selected.Remove(symbol)) selected.Add(symbol);
                        Set(selected);
                    }
                }
                if (Event.current.type == EventType.MouseMove) editorWindow.Repaint();
            }

            private void Set(List<string> list)
            {
                // 並びは図柄一覧の順にそろえる
                var palette = Palette();
                list.Sort((a, b) => palette.IndexOf(a).CompareTo(palette.IndexOf(b)));
                string value = string.Join(",", list.ToArray());
                window.EditPayout("図柄の変更", m =>
                {
                    var target = m.Find(role);
                    if (target != null) target.Symbols[reel] = value;
                });
                editorWindow.Repaint();
            }
        }

        private PayoutRow CurrentPayoutRow(string role)
        {
            var table = MachinePayoutTable;
            return table != null ? PayoutModel.Load(table).Find(role) : null;
        }

        /// <summary>選べる図柄（リール配列に出てくる順）。</summary>
        private List<string> PayoutPalette()
        {
            if (symbols.Count > 0) return symbols;
            if (paletteFallback == null)
            {
                paletteFallback = new List<string>();
                try
                {
                    var read = ReelMachineReader.Read(machineObject);
                    for (int r = 0; r < 3; r++)
                        foreach (string s in read.Strips[r])
                            if (!paletteFallback.Contains(s)) paletteFallback.Add(s);
                }
                catch (Exception) { }
            }
            return paletteFallback;
        }

        [NonSerialized] private List<string> paletteFallback;

        private static void Swap<T>(List<T> list, int index, int direction)
        {
            int other = index + direction;
            if (index < 0 || other < 0 || other >= list.Count) return;
            var item = list[index];
            list[index] = list[other];
            list[other] = item;
        }

        /// <summary>押す順（リールのインデックスの並び）から、リールごとの停止順（1〜3）にする。</summary>
        private static int[] StopOrderOf(int[] press)
        {
            var order = new int[3];
            for (int k = 0; k < 3; k++) order[press[k]] = k + 1;
            return order;
        }

        private static int OrderIndex(int[] order)
        {
            for (int i = 0; i < PressOrders.Length; i++)
            {
                var o = StopOrderOf(PressOrders[i]);
                if (o[0] == order[0] && o[1] == order[1] && o[2] == order[2]) return i + 1;
            }
            return 0;
        }

        /// <summary>この役が契機になる移行やボーナスの作動。</summary>
        private static string PayoutRtLabel(BonusTable bonus, string role)
        {
            if (bonus == null) return "";
            var parts = new List<string>();
            for (int b = 0; b < bonus.GetBonusCount(); b++)
                if (bonus.GetBonusStartRole(b) == role) parts.Add(bonus.GetBonusDisplayName(b) + " 作動");
            for (int m = 0; m < bonus.GetMoveCount(); m++)
                if (bonus.GetMoveRole(m) == role) parts.Add("→" + bonus.GetStateDisplayName(bonus.GetMoveTo(m)));
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>役名の変更を、抽選表・ボーナス表・移行図柄の設定へ反映する。</summary>
        private void RenamePayoutRole(string old, string renamed)
        {
            foreach (var table in ReelMachineReader.LotteryTablesOf(machineObject))
            {
                var m = LotteryModel.Load(table);
                bool hit = false;
                foreach (var e in m.Entries)
                    for (int i = 0; i < e.Roles.Count; i++)
                        if (e.Roles[i] == old) { e.Roles[i] = renamed; hit = true; }
                if (hit) m.Save("役名の変更");
            }
            var bonusTable = MachineBonusTable;
            if (bonusTable != null)
            {
                var b = BonusModel.Load(bonusTable);
                bool hit = false;
                foreach (var row in b.Bonuses) if (row.StartRole == old) { row.StartRole = renamed; hit = true; }
                foreach (var move in b.Moves) if (move.Role == old) { move.Role = renamed; hit = true; }
                if (hit) b.Save("役名の変更");
            }
            if (asset != null)
            {
                var t = Design.FindTransition(old);
                if (t != null) Edit("役名の変更", delegate { t.role = renamed; });
            }
        }

        private bool DrawPayLines(PayoutModel model)
        {
            bool changed = false;
            var header = PanelHeaderRect("有効ライン");
            if (GUI.Button(new Rect(header.xMax - 70, header.y + 3, 64, header.height - 6), "＋ 追加", EditorStyles.miniButton))
            {
                model.Lines.Add(new PayLineRow());
                changed = true;
            }
            for (int i = 0; i < model.Lines.Count; i++)
            {
                var line = model.Lines[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label((i + 1).ToString(), GUILayout.Width(20));
                    for (int reel = 0; reel < 3; reel++)
                    {
                        GUILayout.Label(ReelLetters[reel], GUILayout.Width(12));
                        int row = EditorGUILayout.Popup(line.Rows[reel], LineRowNames, GUILayout.Width(56));
                        if (row != line.Rows[reel]) { line.Rows[reel] = row; changed = true; }
                    }
                    GUILayout.Space(12);
                    GUILayout.Label("BET", GUILayout.Width(26));
                    for (int b = 0; b < 3; b++)
                    {
                        bool on = (line.BetMask & (1 << b)) != 0;
                        bool next = GUILayout.Toggle(on, BetNames[b], EditorStyles.miniButton, GUILayout.Width(24));
                        if (next != on) { line.BetMask ^= 1 << b; changed = true; }
                    }
                    GUILayout.Space(12);
                    if (GUILayout.Button("削除", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        model.Lines.RemoveAt(i);
                        changed = true;
                        break;
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            return changed;
        }

        // ------------------------------------------------------------ 右: 選んだ役

        private void DrawPayoutSide(PayoutRow row)
        {
            PanelHeader(row.Label);
            GUILayout.Label("種別  " + RoleTypeNames[(int)row.Type], EditorStyles.miniLabel);
            if (row.Type != PayoutRoleType.Transition || asset == null) return;

            var t = Design.FindTransition(row.Name);
            GUILayout.Space(6);
            GUILayout.Label("出してよいフラグ", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (t != null)
                    foreach (string flag in t.flags.ToArray())
                    {
                        string target = flag;
                        if (Chip(flag + " ×", SmallRoleBack, SmallRoleText))
                            Edit("出してよいフラグの変更", delegate { t.flags.Remove(target); });
                    }
                if (GUILayout.Button("＋", EditorStyles.miniButton, GUILayout.Width(24)) && machine != null)
                {
                    var menu = new GenericMenu();
                    var names = new List<string>();
                    foreach (var flag in machine.Flags) if (!names.Contains(flag.BaseName)) names.Add(flag.BaseName);
                    string role = row.Name;
                    foreach (string name in names)
                    {
                        string flagName = name;
                        bool on = t != null && t.flags.Contains(name);
                        menu.AddItem(new GUIContent(name), on, delegate
                        {
                            Edit("出してよいフラグの変更", delegate
                            {
                                var target = Design.FindTransition(role);
                                if (target == null) Design.transitions.Add(target = new DesignTransition { role = role });
                                if (!target.flags.Remove(flagName)) target.flags.Add(flagName);
                            });
                        });
                    }
                    menu.ShowAsContext();
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("出し方", EditorStyles.miniBoldLabel, GUILayout.Width(50));
                int how = t != null && t.wrongOrderOnly ? 1 : 0;
                int next = EditorGUILayout.Popup(how, TransitionHowNames);
                if (next != how)
                {
                    string role = row.Name;
                    Edit("出し方の変更", delegate
                    {
                        var target = Design.FindTransition(role);
                        if (target == null) Design.transitions.Add(target = new DesignTransition { role = role });
                        target.wrongOrderOnly = next == 1;
                    });
                }
            }
            GUILayout.Space(8);
            GUILayout.Label("自動で入る出目", EditorStyles.miniBoldLabel);
            if (t == null || t.flags.Count == 0)
            {
                GUILayout.Label("—", MutedLabel);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Chip("狙う", RegularBack, RegularText);
                GUILayout.Label(string.Join(", ", t.flags.ToArray()) + (t.wrongOrderOnly ? "（押し順不正解）" : ""), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Chip("禁止", BonusRoleBack, BonusRoleText);
                GUILayout.Label("それ以外のすべての制御", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }
        }
    }
}
