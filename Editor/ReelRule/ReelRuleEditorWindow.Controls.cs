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
        private static readonly string[] ReelLetters = { "左", "中", "右" };

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
            get { return Design.controls.Count == 0 ? null : Design.controls[Mathf.Clamp(controlIndex, 0, Design.controls.Count - 1)]; }
        }

        private void DrawControlScreen()
        {
            PrepareCells();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(AssignGridWidth()))) DrawAssignGrid();
                var control = SelectedControl;
                if (control != null && machine != null && Design.IsUnusedCell(control, machine))
                {
                    var normal = Design.FindCell(control.flag, "");
                    if (normal != null) { SelectControl(Design.controls.IndexOf(normal)); control = normal; }
                }
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

        // ------------------------------------------------------------ 左: 割り当て表（フラグ × 成立中のボーナス）

        private const float AssignRowH = 26, AssignSubH = 22;

        /// <summary>割り当て表の行。ボーナス付きの行（Cherry_RedBB など）は含めない。</summary>
        private List<string> GridFlags()
        {
            var list = new List<string>();
            if (machine == null) return list;
            foreach (var f in machine.Flags)
                if (string.IsNullOrEmpty(f.Bonus) && !list.Contains(f.Name)) list.Add(f.Name);
            return list;
        }

        /// <summary>割り当て表の列（作動役のあるボーナス）。Key=内部名、Value=表示名。</summary>
        private List<KeyValuePair<string, string>> GridBonuses()
        {
            var list = new List<KeyValuePair<string, string>>();
            var bonusTable = MachineBonusTable;
            if (bonusTable == null) return list;
            for (int b = 0; b < bonusTable.GetBonusCount(); b++)
                if (bonusTable.GetBonusStartRole(b).Length > 0)
                    list.Add(new KeyValuePair<string, string>(bonusTable.GetBonusName(b), bonusTable.GetBonusDisplayName(b)));
            return list;
        }

        private float AssignGridWidth()
        {
            return 230;
        }

        private string BonusLabel(string bonus)
        {
            foreach (var pair in GridBonuses()) if (pair.Key == bonus) return pair.Value;
            return bonus;
        }

        private string ControlLabel(DesignControl c)
        {
            if (!c.IsCell) return c.name;
            return string.IsNullOrEmpty(c.bonus) ? c.flag : c.flag + " × " + BonusLabel(c.bonus) + " 成立中";
        }

        /// <summary>割り当て表のマスがすべてそろうように制御を作る。旧形式の制御はマスへ移す。</summary>
        [NonSerialized] private bool holdCells;

        private void PrepareCells()
        {
            if (machine == null || asset == null || holdCells) return;
            var flags = GridFlags();
            var bonuses = new List<string>();
            foreach (var pair in GridBonuses()) bonuses.Add(pair.Key);
            bool legacy = false;
            foreach (var c in Design.controls)
                if (!c.IsCell && LegacyCellOf(c, flags, bonuses) != null) legacy = true;
            bool missing = false;
            foreach (string flag in flags)
            {
                if (Design.FindCell(flag, "") == null) missing = true;
                foreach (string bonus in bonuses) if (Design.FindCell(flag, bonus) == null && !SkipCell(flag, bonus)) missing = true;
            }
            // 消した（または作動役を外した）ボーナスのマスは表に出ないので消す。
            var orphans = new List<string>();
            if (MachineBonusTable != null)
                foreach (var c in Design.controls)
                    if (c.IsCell && !string.IsNullOrEmpty(c.bonus) && !bonuses.Contains(c.bonus)) orphans.Add(c.name);
            if (!legacy && !missing && orphans.Count == 0) return;
            Edit("割り当て表の作成", delegate
            {
                foreach (string name in orphans)
                {
                    Design.controls.Remove(Design.FindControl(name));
                    foreach (var shape in Design.shapes) shape.RemoveControl(name);
                }
                if (orphans.Count > 0) controlIndex = Mathf.Max(0, Mathf.Min(controlIndex, Design.controls.Count - 1));
                if (legacy) MigrateControls(flags, bonuses);
                foreach (string flag in flags)
                {
                    var normal = Design.FindCell(flag, "");
                    if (normal == null)
                    {
                        normal = new DesignControl { flag = flag, name = FreeCellName(flag, "") };
                        Design.controls.Add(normal);
                    }
                    foreach (string bonus in bonuses)
                    {
                        if (Design.FindCell(flag, bonus) != null || SkipCell(flag, bonus)) continue;
                        var cell = normal.Copy();
                        cell.bonus = bonus;
                        cell.state = "";
                        cell.pullBonus = true;
                        cell.lotteryFlags.Clear();
                        cell.name = FreeCellName(flag, bonus);
                        Design.controls.Add(cell);
                        foreach (var shape in Design.shapes) shape.CopyControl(normal.name, cell.name);
                    }
                }
            });
        }

        /// <summary>
        /// フラグ名・ボーナス名の変更をマスへ反映する。マスの名前と出目の制御リストも付け替える。
        /// 変えないほうは null。
        /// </summary>
        private void RenameCells(string oldFlag, string newFlag, string oldBonus, string newBonus)
        {
            if (asset == null) return;
            // 変更後の名前のマスがもうあれば、付け替えない（重複を作らない）。
            foreach (var c in Design.controls)
                if (c.IsCell && ((newFlag != null && c.flag == newFlag) || (newBonus != null && c.bonus == newBonus))) return;
            Edit("内部名の変更", delegate
            {
                var moved = new List<DesignControl>();
                foreach (var c in Design.controls)
                {
                    if (!c.IsCell) continue;
                    bool flagHit = oldFlag != null && c.flag == oldFlag;
                    bool bonusHit = oldBonus != null && c.bonus == oldBonus;
                    if (!flagHit && !bonusHit) continue;
                    if (flagHit) c.flag = newFlag;
                    if (bonusHit) c.bonus = newBonus;
                    // 名前の入れ替えがぶつからないよう、いったん仮の名前にする
                    string old = c.name;
                    c.name = "\u0001" + old;
                    foreach (var shape in Design.shapes) shape.RenameControl(old, c.name);
                    moved.Add(c);
                }
                foreach (var c in moved)
                {
                    string temp = c.name;
                    c.name = FreeCellName(c.flag, c.bonus);
                    foreach (var shape in Design.shapes) shape.RenameControl(temp, c.name);
                }
                if (oldFlag != null)
                    foreach (var t in Design.transitions)
                    {
                        int i = t.flags.IndexOf(oldFlag);
                        if (i >= 0) t.flags[i] = newFlag;
                    }
            });
        }

        /// <summary>小役優先で、小役のあるフラグのボーナス成立中（使わないマス）。</summary>
        private bool SkipCell(string flag, string bonus)
        {
            return Design.priority == ControlPriority.SmallFirst && ReelDesign.HasSmallRoles(flag, machine);
        }

        /// <summary>マスの制御名。旧形式の制御が同じ名前を使っていれば、旧形式のほうを改名する。</summary>
        private string FreeCellName(string flag, string bonus)
        {
            string name = DesignControl.CellName(flag, bonus);
            var other = Design.FindControl(name);
            if (other != null && !other.IsCell)
            {
                string renamed = ReelDesign.NumberedName(name, n => Design.FindControl(n) != null);
                foreach (var shape in Design.shapes) shape.RenameControl(name, renamed);
                other.name = renamed;
            }
            return name;
        }

        /// <summary>旧形式の制御が入るマス（フラグ, ボーナス）。決められなければ null。</summary>
        private string[] LegacyCellOf(DesignControl c, List<string> flags, List<string> bonuses)
        {
            // RT状態つきの制御はマスに入らない。
            if (!string.IsNullOrEmpty(c.state)) return null;
            string flag = null, bonus = c.bonus ?? "", rowBonus = null;
            bool plain = false;
            foreach (string name in c.lotteryFlags)
            {
                var f = machine.FindFlag(name);
                if (f == null) continue;
                // 元のフラグが違うフラグをまとめた制御は、どのマスとも決められない。
                if (flag != null && flag != f.BaseName) return null;
                flag = f.BaseName;
                if (string.IsNullOrEmpty(f.Bonus)) plain = true;
                else if (rowBonus == null) rowBonus = f.Bonus;
            }
            // ボーナス付きの行（Cherry_RedBB など）だけの制御は、そのボーナス成立中のマス。
            if (!plain && rowBonus != null && bonus.Length == 0) bonus = rowBonus;
            if (flag == null)
                foreach (string f in flags)
                {
                    if (c.name == f) { flag = f; break; }
                    foreach (string b in bonuses)
                        if (c.name == f + "_" + b) { flag = f; bonus = b; }
                }
            if (flag == null || !flags.Contains(flag) || (bonus.Length > 0 && !bonuses.Contains(bonus))) return null;
            if (Design.FindCell(flag, bonus) != null) return null;
            return new[] { flag, bonus };
        }

        /// <summary>旧形式（抽選フラグ・ボーナス成立中の欄）の制御をマスの制御にする。</summary>
        private void MigrateControls(List<string> flags, List<string> bonuses)
        {
            var moved = new List<KeyValuePair<DesignControl, string>>();
            foreach (var c in Design.controls)
            {
                if (c.IsCell) continue;
                var cell = LegacyCellOf(c, flags, bonuses);
                if (cell == null) continue;
                string old = c.name;
                // ボーナス成立中の欄を設定していた制御（NoWin_RedBB など）は揃える。
                // ボーナス付きのフラグだけの制御（Cherry_RedBB など）は、元の小役の制御と同じく揃えない。
                bool explicitBonus = !string.IsNullOrEmpty(c.bonus);
                c.flag = cell[0];
                c.bonus = cell[1];
                c.state = "";
                c.pullBonus = explicitBonus;
                c.lotteryFlags.Clear();
                string start;
                if (c.bonus.Length > 0 && machine.BonusStartRoles.TryGetValue(c.bonus, out start)) c.extraRoles.Remove(start);
                // 名前の入れ替えがぶつからないよう、いったん仮の名前にする
                c.name = "\u0001" + old;
                foreach (var shape in Design.shapes) shape.RenameControl(old, c.name);
                moved.Add(new KeyValuePair<DesignControl, string>(c, old));
            }
            foreach (var pair in moved)
            {
                string temp = pair.Key.name;
                string name = FreeCellName(pair.Key.flag, pair.Key.bonus);
                pair.Key.name = name;
                foreach (var shape in Design.shapes) shape.RenameControl(temp, name);
            }
        }

        [SerializeField] private List<string> openFlags = new List<string>();

        private void DrawAssignGrid()
        {
            PanelHeader("割り当て");
            var flags = GridFlags();
            var bonuses = GridBonuses();
            var selected = SelectedControl;
            controlListScroll = EditorGUILayout.BeginScrollView(controlListScroll);
            foreach (string flag in flags)
            {
                var normal = Design.FindCell(flag, "");
                bool open = openFlags.Contains(flag);
                var row = GUILayoutUtility.GetRect(10, AssignRowH, GUILayout.ExpandWidth(true));
                if (normal != null && normal == selected) EditorGUI.DrawRect(row, SelectedColor);
                HLine(row.x, row.yMax - 1, row.width);
                if (bonuses.Count > 0)
                {
                    var fold = new Rect(row.x + 4, row.y + 5, 16, 16);
                    GUI.Label(fold, open ? "▾" : "▸");
                    if (Clicked(new Rect(row.x, row.y, 22, row.height), 0))
                    {
                        if (open) openFlags.Remove(flag); else openFlags.Add(flag);
                        Repaint();
                    }
                }
                GUI.Label(new Rect(row.x + 22, row.y + 4, row.width - 26, 18), (HasViolation(normal) ? "⚠ " : "") + flag, EditorStyles.boldLabel);
                if (normal != null && Clicked(row, 0)) SelectControl(Design.controls.IndexOf(normal));
                if (!open) continue;
                foreach (var pair in bonuses)
                {
                    if (SkipCell(flag, pair.Key))
                    {
                        var none = GUILayoutUtility.GetRect(10, AssignSubH, GUILayout.ExpandWidth(true));
                        EditorGUI.DrawRect(none, SubRowColor);
                        GUI.Label(new Rect(none.x + 30, none.y + 3, none.width - 110, 16), pair.Value + "中", MutedLabel);
                        GUI.Label(new Rect(none.xMax - 74, none.y + 3, 68, 16), "-", CenteredMini);
                        continue;
                    }
                    var cell = Design.FindCell(flag, pair.Key);
                    if (cell == null) continue;
                    var sub = GUILayoutUtility.GetRect(10, AssignSubH, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(sub, cell == selected ? SelectedColor : SubRowColor);
                    GUI.Label(new Rect(sub.x + 30, sub.y + 3, sub.width - 110, 16), (HasViolation(cell) ? "⚠ " : "") + pair.Value + "中");
                    var state = new Rect(sub.xMax - 74, sub.y + 3, 68, 16);
                    EditorGUI.DrawRect(state, cell.pullBonus ? BonusRoleBack : AnyColor);
                    var style = new GUIStyle(CenteredMini);
                    style.normal.textColor = cell.pullBonus ? BonusRoleText : MutedText;
                    GUI.Label(state, cell.pullBonus ? "揃える" : "揃えない", style);
                    if (Clicked(sub, 0)) SelectControl(Design.controls.IndexOf(cell));
                    else if (Clicked(sub, 1))
                    {
                        var target = cell;
                        var source = normal;
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("通常の内容をコピー"), false, delegate { CopyCellFrom(source, target); });
                        menu.ShowAsContext();
                    }
                }
            }
            // 割り当て表に入らない旧形式の制御
            var others = Design.controls.FindAll(c => !c.IsCell);
            if (others.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("その他の制御", MutedLabel);
                foreach (var c in others)
                {
                    var rect = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true));
                    if (c == selected) EditorGUI.DrawRect(rect, SelectedColor);
                    GUI.Label(new Rect(rect.x + 8, rect.y + 2, rect.width - 12, 16), c.name);
                    int index = Design.controls.IndexOf(c);
                    if (Clicked(rect, 0)) SelectControl(index);
                    else if (Clicked(rect, 1))
                    {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("削除"), false, delegate { DeleteControl(index); });
                        menu.ShowAsContext();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool HasViolation(DesignControl cell)
        {
            if (cell == null) return false;
            var result = ResultOf(cell.name);
            return result != null && result.Stats != null && result.Stats.ViolationPaths > 0;
        }

        private void CopyCellFrom(DesignControl source, DesignControl target)
        {
            if (source == null || target == null) return;
            Edit("通常の内容をコピー", delegate
            {
                target.targets = new List<string>(source.targets);
                target.overrides.Clear();
                foreach (var o in source.overrides) target.overrides.Add(o.Copy());
                foreach (var shape in Design.shapes)
                {
                    shape.RemoveControl(target.name);
                    shape.CopyControl(source.name, target.name);
                }
            });
        }

        private List<string> WonRolesOf(DesignControl control)
        {
            var won = new List<string>();
            if (machine == null) return won;
            foreach (string flagName in ReelDesign.LotteryFlagsOf(control, machine))
            {
                var flag = machine.FindFlag(flagName);
                if (flag != null) foreach (string role in flag.Roles) if (!won.Contains(role)) won.Add(role);
            }
            foreach (string role in control.extraRoles) if (!won.Contains(role)) won.Add(role);
            string start, bonus = ReelDesign.WonBonusOf(control);
            if (bonus.Length > 0 && machine.BonusStartRoles.TryGetValue(bonus, out start) && start.Length > 0 && !won.Contains(start))
                won.Add(start);
            return won;
        }

        private bool IsRegistered(string name)
        {
            return registeredNames == null || Array.IndexOf(registeredNames, name) >= 0;
        }

        private void SelectControl(int index)
        {
            if (index < 0 || index == controlIndex) return;
            controlIndex = index;
            mapCache = null;
            firstStop = secondStop = -1;
            GUI.FocusControl(null);
            Repaint();
        }

        private void DeleteControl(int index)
        {
            Edit("制御の削除", delegate
            {
                string name = Design.controls[index].name;
                Design.controls.RemoveAt(index);
                if (Design.FindControl(name) == null)
                    foreach (var shape in Design.shapes) shape.RemoveControl(name);
                controlIndex = Mathf.Max(0, Mathf.Min(controlIndex, Design.controls.Count - 1));
            });
        }

        // ------------------------------------------------------------ 中央: 設定と表

        private void DrawControlCenter(DesignControl control)
        {
            PanelHeader("制御：" + ControlLabel(control));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("当選役", GUILayout.Width(40));
                foreach (string role in WonRolesOf(control)) Chip(role, new Color(0.14f, 0.29f, 0.19f), new Color(0.62f, 0.88f, 0.69f));
                GUILayout.Space(4);
                if (Link("抽選表で編集")) { screen = 2; GUI.FocusControl(null); }
                GUILayout.FlexibleSpace();
                if (control.IsCell && !string.IsNullOrEmpty(control.bonus))
                {
                    bool pull = GUILayout.Toggle(control.pullBonus, "ボーナスを揃える", GUILayout.ExpandWidth(false));
                    if (pull != control.pullBonus) Edit("ボーナスを揃えるの変更", delegate { control.pullBonus = pull; });
                }
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
            else DrawMap(control, result);
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
            for (int i = 0; i < choices.Count; i++) labels[i] = ReelRuleEngine.ReelNames[reel] + " " + (machine.Strips[reel].Length - choices[i]);
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
            // 図柄はReelMakerと同じ並び、コマ番号は下が1
            for (int i = 0; i < table.Slips.Length; i++)
            {
                int press = i;
                var row = GUILayoutUtility.GetRect(10, rowHeight, GUILayout.ExpandWidth(true));
                if (table.Manual[press])
                {
                    EditorGUI.DrawRect(row, new Color(0.23f, 0.18f, 0.12f));
                    Outline(row, ManualColor, 1);
                }
                else if (i % 2 == 1) EditorGUI.DrawRect(row, new Color(0.18f, 0.18f, 0.18f));
                GUI.Label(new Rect(row.x + 2, row.y, 24, row.height), (table.Slips.Length - press).ToString(), EditorStyles.miniLabel);
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

        private void DrawMap(DesignControl control, ControlResult result)
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
                    DrawMatrix(result, map, r2, r3, n2, n3);
                    DrawMapSelection(control, result, map, r2, r3);
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

        private void DrawMatrix(ControlResult result, MapCache map, int r2, int r3, int n2, int n3)
        {
            const float cw = 40, ch = 26, numW = 24, imgW = 52;
            var area = GUILayoutUtility.GetRect(numW + imgW + n3 * cw + 2, ch * 2 + n2 * ch + 2, GUILayout.ExpandWidth(false));
            float x0 = area.x + numW + imgW, y0 = area.y + ch * 2;
            GUI.Label(new Rect(area.x, area.y, numW + imgW, ch), ReelRuleEngine.ReelNames[r2] + "＼" + ReelRuleEngine.ReelNames[r3], CenteredStyle);
            for (int c = 0; c < n3; c++)
            {
                GUI.Label(new Rect(x0 + c * cw, area.y, cw, ch), (n3 - c).ToString(), CenteredStyle);
                DrawStripSymbol(new Rect(x0 + c * cw + 1, area.y + ch + 1, cw - 2, ch - 2), r3, c);
            }
            for (int r = 0; r < n2; r++)
            {
                float y = y0 + r * ch;
                GUI.Label(new Rect(area.x, y, numW, ch), (n2 - r).ToString(), CenteredStyle);
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
            if (Event.current.type == EventType.Repaint && SelectionValid(map))
            {
                int[] orders;
                var stops = engine.Resolve(result, order, firstStop, mapRow, mapColumn, out orders);
                if (stops != null)
                {
                    float cx = x0 + mapColumn * cw + cw / 2, cy = y0 + mapRow * ch + ch / 2;
                    // 第三停止: 左へ、第二停止: 上へ（滑りで位置が小さくなる方向）
                    SlipArrow(cx, cy, cw, n3, mapColumn, Machine.Wrap(mapColumn - stops[r3], n3), x0, true);
                    SlipArrow(cx, cy, ch, n2, mapRow, Machine.Wrap(mapRow - stops[r2], n2), y0, false);
                }
            }
        }

        private static readonly Color ArrowColor = new Color(0.92f, 0.22f, 0.22f);

        private bool SelectionValid(MapCache map)
        {
            return mapRow >= 0 && mapColumn >= 0 && mapRow < map.Cells.GetLength(0) && mapColumn < map.Cells.GetLength(1);
        }

        /// <summary>選択セルから滑り数ぶんの矢印。端を越えるときは反対側から続ける。</summary>
        private static void SlipArrow(float cx, float cy, float size, int count, int index, int slip, float origin, bool horizontal)
        {
            if (slip <= 0) return;
            float from = horizontal ? cx : cy;
            int target = index - slip;
            if (target >= 0)
            {
                ArrowLine(from, origin + target * size + size / 2, horizontal ? cy : cx, horizontal, true);
                return;
            }
            ArrowLine(from, origin, horizontal ? cy : cx, horizontal, false);
            ArrowLine(origin + count * size, origin + (count + target) * size + size / 2, horizontal ? cy : cx, horizontal, true);
        }

        private static void ArrowLine(float from, float to, float across, bool horizontal, bool head)
        {
            const float thick = 3, headLength = 9, headHalf = 7;
            float end = head ? to + headLength : to;
            float a = Mathf.Min(from, end), b = Mathf.Max(from, end);
            EditorGUI.DrawRect(horizontal ? new Rect(a, across - thick / 2, b - a, thick) : new Rect(across - thick / 2, a, thick, b - a), ArrowColor);
            if (!head) return;
            for (int i = 0; i < headLength; i++)
            {
                float half = headHalf * i / headLength;
                float at = to + i;
                EditorGUI.DrawRect(horizontal ? new Rect(at, across - half, 1, half * 2 + 1) : new Rect(across - half, at, half * 2 + 1, 1), ArrowColor);
            }
        }

        private void DrawMapSelection(DesignControl control, ControlResult result, MapCache map, int r2, int r3)
        {
            if (!SelectionValid(map)) return;
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
                    GUILayout.Label(ReelRuleEngine.ReelNames[r2] + " " + (machine.Strips[r2].Length - mapRow) + "押し × " + ReelRuleEngine.ReelNames[r3] + " " + (machine.Strips[r3].Length - mapColumn) + "押し", EditorStyles.miniLabel);
                    int k = map.Cells[mapRow, mapColumn];
                    Chip(map.Labels[k], map.Colors[k], Color.white);
                    GUILayout.Space(4);
                    var prior = new[] { -1, -1, -1 };
                    prior[ReelRuleEngine.Orders[order][0]] = firstStop;
                    DrawSlipEditor(control, result, 1, mapRow, prior);
                    prior[r2] = stops[r2];
                    DrawSlipEditor(control, result, 2, mapColumn, prior);
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

        private void DrawSlipEditor(DesignControl control, ControlResult result, int stage, int press, int[] prior)
        {
            var table = engine.FindTable(result, ReelRuleEngine.PatternFor(order, stage), prior);
            if (table == null) return;
            int reel = ReelRuleEngine.Orders[order][stage];
            int slip = table.Slips[press];
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(ReelRuleEngine.ReelNames[reel] + " 滑り", EditorStyles.miniLabel, GUILayout.Width(44));
                if (!manualEditing)
                {
                    GUILayout.Label(slip.ToString(), EditorStyles.boldLabel, GUILayout.Width(20));
                    return;
                }
                var tablePrior = (int[])table.Prior.Clone();
                bool manual = control.FindOverride(table.Pattern, tablePrior, press) != null;
                for (int s = 0; s <= engine.MaxSlip; s++)
                {
                    var color = GUI.backgroundColor;
                    if (s == slip) GUI.backgroundColor = manual ? ManualColor : AccentColor;
                    if (GUILayout.Button(s.ToString(), EditorStyles.miniButton, GUILayout.Width(26)) && !(manual && s == slip))
                        SetOverride(control, table.Pattern, tablePrior, press, s);
                    GUI.backgroundColor = color;
                }
                using (new EditorGUI.DisabledScope(!manual))
                    if (GUILayout.Button("自動", EditorStyles.miniButton, GUILayout.Width(36)))
                        Edit("滑り数の手動指定を解除", delegate { control.overrides.Remove(control.FindOverride(table.Pattern, tablePrior, press)); });
            }
        }

        private void SetOverride(DesignControl control, int pattern, int[] prior, int press, int slip)
        {
            Edit("滑り数の手動指定", delegate
            {
                var o = control.FindOverride(pattern, prior, press);
                if (o == null)
                {
                    o = new DesignOverride { pattern = pattern, left = prior[0], center = prior[1], right = prior[2], press = press };
                    control.overrides.Add(o);
                }
                o.slip = slip;
            });
        }

        // ------------------------------------------------------------ 右: 狙う出目・リーチ目・手動指定・結果

        private void DrawShapeTags(DesignShape shape)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var gray = new Color(0.23f, 0.23f, 0.23f);
                var text = new Color(0.66f, 0.66f, 0.66f);
                Chip(KindName(shape), gray, text);
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
                    if (shape.Exclusive && !(shape.reach && shape.AllowedIn(control.name))) menu.AddDisabledItem(new GUIContent(name));
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
            // 出目マップ表示中は、マップと同じ色を名前の左に付ける
            var map = view == 1 && mapCache != null && mapCache.Result == result && mapCache.Order == order && mapCache.FirstStop == firstStop ? mapCache : null;
            for (int i = 0; i < s.RoleNames.Length; i++)
                if (s.RoleWon[i]) RateBar(s.RoleNames[i], s.RoleFormed[i], s.Paths, new Color(0.23f, 0.62f, 0.33f), map);
            for (int k = 0; k < s.ShapeNames.Count; k++)
                if (s.ShapeMatched[k] > 0) RateBar(s.ShapeNames[k], s.ShapeMatched[k], s.Paths, AccentColor, map);
            GUILayout.Space(6);

            long leaks = 0;
            for (int k = 0; k < s.ShapeNames.Count; k++)
            {
                var shape = Design.FindShape(s.ShapeNames[k]);
                if (shape != null && !shape.AllowedIn(control.name)) leaks += s.ShapeMatched[k];
            }
            int manualWarnings = 0, unavoidable = 0;
            foreach (var t in result.Tables)
                for (int p = 0; p < t.Slips.Length; p++)
                {
                    if (!t.Unavoidable[p]) continue;
                    if (t.Manual[p]) manualWarnings++; else unavoidable++;
                }
            CheckRow("非成立役の入賞", s.ViolationPaths);
            CheckRow("リーチ目・禁止目の漏れ", leaks);
            CheckRow("手動指定による警告", manualWarnings);
            if (unavoidable > 0) CheckRow("回避不能", unavoidable);
        }

        private static void RateBar(string label, long part, long whole, Color color, MapCache map)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 出目マップの色（名前とバーの2行ぶん）
                if (map != null)
                {
                    var mark = GUILayoutUtility.GetRect(10, 24, GUILayout.Width(10), GUILayout.Height(24));
                    int k = map.Labels.IndexOf(label);
                    if (k >= 0) EditorGUI.DrawRect(new Rect(mark.x, mark.y + 2, mark.width, mark.height - 2), map.Colors[k]);
                    GUILayout.Space(4);
                }
                using (new EditorGUILayout.VerticalScope())
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
                }
            }
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
