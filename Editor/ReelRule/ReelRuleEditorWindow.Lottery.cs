using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ③ 抽選表: フラグ・ボーナス付きの行・設定ごとの比率・合算
    public sealed partial class ReelRuleEditorWindow
    {
        private const float LotHeadH = 22, LotRowH = 36, LotSumH = 22;
        private const float LotCheckW = 24, LotNameW = 150, LotIdW = 150, LotRolesW = 180, LotControlW = 140, LotSettingW = 96;
        private static readonly string[] BetNames = { "1", "2", "3" };

        [SerializeField] private int lotteryTableIndex;
        [SerializeField] private int lotteryBet = 2;
        [SerializeField] private bool bonusSumsOpen;
        private Vector2 lotteryScroll, lotteryHScroll, lotteryListScroll;
        private readonly HashSet<string> lotteryPicks = new HashSet<string>();

        /// <summary>抽選表の行（親フラグの下にボーナス付きの行が並ぶ）。</summary>
        private struct LotteryRowRef
        {
            public LotteryEntry Entry;
            public bool Sub;
        }

        private void DrawLotteryScreen()
        {
            var tables = ReelMachineReader.LotteryTablesOf(machineObject);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(180)))
                {
                    PanelHeader("抽選表");
                    lotteryListScroll = EditorGUILayout.BeginScrollView(lotteryListScroll);
                    for (int i = 0; i < tables.Count; i++)
                    {
                        var rect = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
                        if (i == lotteryTableIndex) EditorGUI.DrawRect(rect, SelectedColor);
                        GUI.Label(new Rect(rect.x + 8, rect.y + 4, rect.width - 12, 18), TableLabel(tables[i]));
                        int index = i;
                        if (Clicked(rect, 0)) { lotteryTableIndex = index; GUI.FocusControl(null); }
                    }
                    EditorGUILayout.EndScrollView();
                }
                if (tables.Count == 0) return;
                lotteryTableIndex = Mathf.Clamp(lotteryTableIndex, 0, tables.Count - 1);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) DrawLotteryTable(tables[lotteryTableIndex]);
            }
        }

        /// <summary>指定した抽選表を開く（⑤ の「抽選表で編集」から）。</summary>
        private void OpenLotteryTable(LotteryTable table)
        {
            var tables = ReelMachineReader.LotteryTablesOf(machineObject);
            int index = tables.IndexOf(table);
            if (index >= 0) lotteryTableIndex = index;
            screen = 2;
            GUI.FocusControl(null);
        }

        private void EditLottery(LotteryTable table, string label, Action<LotteryModel> change)
        {
            var model = LotteryModel.Load(table);
            change(model);
            model.Save(label);
            Changed();
            Repaint();
        }

        private void DrawLotteryTable(LotteryTable table)
        {
            var model = LotteryModel.Load(table);
            var payout = MachinePayoutTable;
            var bonusTable = MachineBonusTable;
            string saveLabel = null;
            int settingCount = model.SettingCount;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("BET", GUILayout.Width(28));
                lotteryBet = GUILayout.Toolbar(lotteryBet, BetNames, GUILayout.Width(96));
                GUILayout.Space(16);
                GUILayout.Label("設定", GUILayout.Width(28));
                if (GUILayout.Button("−", EditorStyles.miniButtonLeft, GUILayout.Width(22)) && settingCount > 1)
                {
                    model.SetSettingCount(settingCount - 1);
                    saveLabel = "設定の数の変更";
                }
                GUILayout.Label(settingCount.ToString(), CenteredStyle, GUILayout.Width(22));
                if (GUILayout.Button("＋", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                {
                    model.SetSettingCount(settingCount + 1);
                    saveLabel = "設定の数の変更";
                }
                GUILayout.Space(16);
                GUILayout.Label("最大確率分母", GUILayout.Width(72));
                int denominator = EditorGUILayout.DelayedIntField(model.Denominator, GUILayout.Width(70));
                if (denominator != model.Denominator && denominator > 0)
                {
                    model.Denominator = denominator;
                    saveLabel = "最大確率分母の変更";
                }
                GUILayout.FlexibleSpace();
            }
            if (saveLabel != null)
            {
                model.Save(saveLabel);
                Changed();
                GUIUtility.ExitGUI();
            }
            GUILayout.Space(4);

            settingCount = model.SettingCount;
            int bet = lotteryBet;
            var rows = LotteryRows(model);
            var bonusNames = new List<string>();
            foreach (var e in model.Entries) if (e.IsBonusRow && !bonusNames.Contains(e.Bonus)) bonusNames.Add(e.Bonus);
            if (bonusTable != null)
                for (int b = 0; b < bonusTable.GetBonusCount(); b++)
                    if (!bonusNames.Contains(bonusTable.bonusNames[b])) bonusNames.Add(bonusTable.bonusNames[b]);
            int sumRows = 2 + (bonusSumsOpen ? bonusNames.Count : 0);
            float leftW = LotCheckW + LotNameW + LotIdW + LotRolesW + LotControlW;
            float gridH = LotHeadH + rows.Count * LotRowH + sumRows * LotSumH + 2;
            string pickPrefix = table.GetInstanceID() + "/";

            lotteryScroll = EditorGUILayout.BeginScrollView(lotteryScroll);
            var area = GUILayoutUtility.GetRect(leftW + 100, gridH + 16, GUILayout.ExpandWidth(true));

            // ---- 左（固定列）
            var left = new Rect(area.x, area.y, leftW, gridH);
            EditorGUI.DrawRect(new Rect(left.x, left.y, left.width, LotHeadH), HeaderColor);
            float cx = left.x + LotCheckW;
            GUI.Label(new Rect(cx + 4, left.y + 3, LotNameW, 16), "名前", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(cx + LotNameW + 4, left.y + 3, LotIdW, 16), "内部名", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(cx + LotNameW + LotIdW + 4, left.y + 3, LotRolesW, 16), "当選役", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(cx + LotNameW + LotIdW + LotRolesW + 4, left.y + 3, LotControlW, 16), "制御", EditorStyles.miniBoldLabel);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var e = row.Entry;
                float y = left.y + LotHeadH + i * LotRowH;
                var rowRect = new Rect(left.x, y, left.width, LotRowH);
                if (row.Sub) EditorGUI.DrawRect(rowRect, SubRowColor);
                HLine(left.x, y + LotRowH - 1, left.width);

                string key = pickPrefix + e.Name;
                bool picked = lotteryPicks.Contains(key);
                bool nextPicked = EditorGUI.Toggle(new Rect(left.x + 5, y + 10, 16, 16), picked);
                if (nextPicked != picked) { if (nextPicked) lotteryPicks.Add(key); else lotteryPicks.Remove(key); }

                var nameRect = new Rect(cx + 4, y + 9, LotNameW - 8, 18);
                var idRect = new Rect(cx + LotNameW + 4, y + 9, LotIdW - 8, 18);
                var rolesRect = new Rect(cx + LotNameW + LotIdW + 4, y + 4, LotRolesW - 8, LotRowH - 8);
                if (row.Sub)
                {
                    string bonusLabel = bonusTable != null ? bonusTable.GetBonusDisplayName(bonusTable.FindBonus(e.Bonus)) : "";
                    GUI.Label(new Rect(nameRect.x + 14, nameRect.y, nameRect.width - 14, 18), "＋" + (bonusLabel.Length > 0 ? bonusLabel : e.Bonus));
                    GUI.Label(idRect, e.Name, MutedLabel);
                }
                else
                {
                    bool canAdd = bonusTable != null && bonusTable.GetBonusCount() > 0;
                    var field = canAdd ? new Rect(nameRect.x, nameRect.y, nameRect.width - 24, 18) : nameRect;
                    string display = EditorGUI.DelayedTextField(field, e.Label);
                    if (display != e.Label) { e.Display = display == e.Name ? "" : display; saveLabel = "フラグ名の変更"; }
                    if (canAdd && GUI.Button(new Rect(nameRect.xMax - 22, nameRect.y, 22, 18), new GUIContent("＋", "ボーナス付きの行を追加"), EditorStyles.miniButton))
                        ShowAddBonusRowMenu(table, e.Name, bonusTable);
                    string renamed = EditorGUI.DelayedTextField(idRect, e.Name);
                    if (renamed != e.Name && renamed.Length > 0 && model.Find(renamed) == null)
                    {
                        RenameLotteryEntry(model, e, renamed);
                        saveLabel = "内部名の変更";
                    }
                }
                if (!e.Remainder) DrawLotteryRoles(rolesRect, table, e, payout);
                DrawLotteryControl(new Rect(cx + LotNameW + LotIdW + LotRolesW + 4, y + 9, LotControlW - 8, 18), e.Name);

                if (RightClicked(rowRect)) ShowLotteryRowMenu(table, e.Name, row.Sub);
            }
            float sumY = left.y + LotHeadH + rows.Count * LotRowH;
            var foldRect = new Rect(left.x + 6, sumY + 3, left.width - 12, 16);
            GUI.Label(foldRect, (bonusSumsOpen ? "▾ " : "▸ ") + "ボーナス合算", EditorStyles.miniBoldLabel);
            if (Clicked(new Rect(left.x, sumY, left.width, LotSumH), 0)) bonusSumsOpen = !bonusSumsOpen;
            int sumIndex = 1;
            if (bonusSumsOpen)
                foreach (string b in bonusNames)
                {
                    string label = bonusTable != null && bonusTable.FindBonus(b) >= 0 ? bonusTable.GetBonusDisplayName(bonusTable.FindBonus(b)) : b;
                    GUI.Label(new Rect(left.x + 24, sumY + sumIndex * LotSumH + 3, left.width - 30, 16), label + " 合算", EditorStyles.miniLabel);
                    sumIndex++;
                }
            int pickCount = 0;
            foreach (var row in rows) if (lotteryPicks.Contains(pickPrefix + row.Entry.Name)) pickCount++;
            GUI.Label(new Rect(left.x + 6, sumY + sumIndex * LotSumH + 3, left.width - 12, 16), "選択した " + pickCount + " 行の合算", EditorStyles.miniBoldLabel);

            // ---- 右（設定ごと。横にスクロール）
            var right = new Rect(area.x + leftW, area.y, Mathf.Max(60, area.width - leftW), gridH + 16);
            float contentW = settingCount * LotSettingW;
            lotteryHScroll = GUI.BeginScrollView(right, lotteryHScroll, new Rect(0, 0, contentW, gridH), false, false, GUI.skin.horizontalScrollbar, GUIStyle.none);
            EditorGUI.DrawRect(new Rect(0, 0, Mathf.Max(contentW, right.width), LotHeadH), HeaderColor);
            for (int s = 0; s < settingCount; s++)
            {
                float x = s * LotSettingW;
                string name = s < model.Settings.Count ? model.Settings[s] : (s + 1).ToString();
                string next = EditorGUI.DelayedTextField(new Rect(x + 6, 2, LotSettingW - 12, 18), name);
                if (next != name)
                {
                    model.SetSettingCount(settingCount);
                    model.Settings[s] = next;
                    saveLabel = "設定名の変更";
                }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i].Entry;
                float y = LotHeadH + i * LotRowH;
                if (rows[i].Sub) EditorGUI.DrawRect(new Rect(0, y, Mathf.Max(contentW, right.width), LotRowH), SubRowColor);
                HLine(0, y + LotRowH - 1, Mathf.Max(contentW, right.width));
                for (int s = 0; s < settingCount; s++)
                {
                    float x = s * LotSettingW;
                    if (e.Remainder)
                    {
                        float rest = model.Remainder(bet, s);
                        GUI.Label(new Rect(x + 6, y + 3, LotSettingW - 12, 16), rest.ToString("0.##"), MutedLabel);
                        GUI.Label(new Rect(x + 6, y + 18, LotSettingW - 12, 14), Probability(model.Denominator, rest), MutedLabel);
                        continue;
                    }
                    float value = LotteryModel.Ratio(e, bet, s);
                    float next = EditorGUI.DelayedFloatField(new Rect(x + 6, y + 3, LotSettingW - 12, 16), value);
                    if (next != value && next >= 0)
                    {
                        LotteryModel.SetRatio(e, bet, s, next);
                        saveLabel = "抽選比率の変更";
                    }
                    GUI.Label(new Rect(x + 6, y + 19, LotSettingW - 12, 14), Probability(model.Denominator, value), EditorStyles.miniLabel);
                }
            }
            // 合算
            float sy = LotHeadH + rows.Count * LotRowH;
            DrawSumRow(sy, settingCount, model, bet, e => e.IsBonusRow, new Color(1f, 0.82f, 0.85f));
            int k = 1;
            if (bonusSumsOpen)
                foreach (string b in bonusNames)
                {
                    string bonusName = b;
                    DrawSumRow(sy + k * LotSumH, settingCount, model, bet, e => e.Bonus == bonusName, BonusRoleText);
                    k++;
                }
            DrawSumRow(sy + k * LotSumH, settingCount, model, bet, e => lotteryPicks.Contains(pickPrefix + e.Name), RegularText);
            GUI.EndScrollView();

            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ フラグを追加", GUILayout.Width(120)))
                {
                    model.Entries.Add(NewLotteryEntry(model, ReelDesign.NumberedName("Flag", n => model.Find(n) != null)));
                    saveLabel = "フラグの追加";
                }
                GUILayout.FlexibleSpace();
            }

            if (saveLabel != null)
            {
                model.Save(saveLabel);
                Changed();
            }
        }

        private void DrawSumRow(float y, int settingCount, LotteryModel model, int bet, Predicate<LotteryEntry> include, Color color)
        {
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = color;
            for (int s = 0; s < settingCount; s++)
            {
                float sum = 0;
                foreach (var e in model.Entries)
                    if (include(e)) sum += e.Remainder ? model.Remainder(bet, s) : LotteryModel.Ratio(e, bet, s);
                GUI.Label(new Rect(s * LotSettingW + 6, y + 3, LotSettingW - 12, 16), Probability(model.Denominator, sum), style);
            }
        }

        /// <summary>表示順: ハズレ → 親フラグとその下のボーナス付きの行 → 親のないボーナス付きの行。</summary>
        private static List<LotteryRowRef> LotteryRows(LotteryModel model)
        {
            var rows = new List<LotteryRowRef>();
            var placed = new HashSet<LotteryEntry>();
            foreach (var e in model.Entries)
                if (e.Remainder && !e.IsBonusRow) AddWithChildren(model, e, rows, placed);
            foreach (var e in model.Entries)
                if (!placed.Contains(e) && !e.IsBonusRow) AddWithChildren(model, e, rows, placed);
            foreach (var e in model.Entries)
                if (placed.Add(e)) rows.Add(new LotteryRowRef { Entry = e, Sub = e.IsBonusRow });
            return rows;
        }

        private static void AddWithChildren(LotteryModel model, LotteryEntry e, List<LotteryRowRef> rows, HashSet<LotteryEntry> placed)
        {
            rows.Add(new LotteryRowRef { Entry = e });
            placed.Add(e);
            foreach (var child in model.Entries)
                if (child.IsBonusRow && child.Base == e.Name && placed.Add(child)) rows.Add(new LotteryRowRef { Entry = child, Sub = true });
        }

        private static LotteryEntry NewLotteryEntry(LotteryModel model, string name)
        {
            var e = new LotteryEntry { Name = name };
            for (int b = 0; b < 3; b++)
                for (int s = 0; s < model.SettingCount; s++) e.Ratios[b].Add(0f);
            return e;
        }

        private void RenameLotteryEntry(LotteryModel model, LotteryEntry e, string renamed)
        {
            string old = e.Name;
            e.Name = renamed;
            foreach (var child in model.Entries)
            {
                if (!child.IsBonusRow || child.BaseFlag != old) continue;
                child.BaseFlag = renamed;
                string name = BonusEntryName(renamed, child.Bonus);
                if (model.Find(name) == null) child.Name = name;
            }
        }

        /// <summary>このフラグで使う制御（ボーナス成立中・RT の条件なし）。クリックで選ぶ。</summary>
        private void DrawLotteryControl(Rect rect, string flag)
        {
            if (asset == null) { GUI.Label(rect, "—", MutedLabel); return; }
            var used = new List<string>();
            foreach (var c in Design.controls)
                if (IsPlainControl(c) && c.lotteryFlags.Contains(flag)) used.Add(c.name);
            string label = used.Count == 0 ? "—" : (used.Count > 1 ? "⚠ " : "") + string.Join(", ", used.ToArray());
            if (!GUI.Button(rect, label, EditorStyles.popup)) return;
            var menu = new GenericMenu();
            foreach (var c in Design.controls)
            {
                if (!IsPlainControl(c)) continue;
                string name = c.name;
                menu.AddItem(new GUIContent(name), used.Contains(name), delegate { AssignControl(flag, name); });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("なし"), used.Count == 0, delegate { AssignControl(flag, null); });
            menu.AddItem(new GUIContent("新しい制御を作る"), false, delegate
            {
                string name = Design.FindControl(flag) == null ? flag : ReelDesign.NumberedName(flag, n => Design.FindControl(n) != null);
                Edit("制御の追加", delegate { Design.controls.Add(new DesignControl { name = name }); });
                AssignControl(flag, name);
            });
            menu.ShowAsContext();
        }

        private static bool IsPlainControl(DesignControl c)
        {
            return string.IsNullOrEmpty(c.bonus) && string.IsNullOrEmpty(c.state);
        }

        /// <summary>フラグで使う制御を付け替える。null なら外すだけ。</summary>
        private void AssignControl(string flag, string control)
        {
            Edit("使う制御の変更", delegate
            {
                foreach (var c in Design.controls)
                    if (IsPlainControl(c)) c.lotteryFlags.Remove(flag);
                var target = control != null ? Design.FindControl(control) : null;
                if (target != null && !target.lotteryFlags.Contains(flag)) target.lotteryFlags.Add(flag);
            });
        }

        private void DrawLotteryRoles(Rect rect, LotteryTable table, LotteryEntry e, PayoutTable payout)
        {
            var texts = new List<string>();
            var backs = new List<Color>();
            var fronts = new List<Color>();
            foreach (string role in e.Roles)
            {
                Color back, text;
                RoleColors(payout, role, out back, out text);
                texts.Add(RoleLabel(payout, role));
                backs.Add(back);
                fronts.Add(text);
            }
            if (texts.Count == 0) GUI.Label(new Rect(rect.x, rect.y + 7, rect.width, 16), "—", MutedLabel);
            else DrawChips(rect, texts, backs, fronts, 2);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (!Clicked(rect, 0) || payout == null) return;

            var menu = new GenericMenu();
            string flagName = e.Name;
            for (int i = 0; i < payout.GetWinPairCount(); i++)
            {
                string role = payout.winPairNames[i];
                if (string.IsNullOrEmpty(role)) continue;
                bool on = e.Roles.Contains(role);
                string group = payout.IsTransition(i) ? "移行図柄/" : "";
                menu.AddItem(new GUIContent(group + RoleLabel(payout, role)), on, delegate
                {
                    EditLottery(table, "当選役の変更", m =>
                    {
                        var target = m.Find(flagName);
                        if (target == null) return;
                        if (!target.Roles.Remove(role)) target.Roles.Add(role);
                    });
                });
            }
            menu.ShowAsContext();
        }

        private void ShowAddBonusRowMenu(LotteryTable table, string parent, BonusTable bonusTable)
        {
            var menu = new GenericMenu();
            for (int b = 0; b < bonusTable.GetBonusCount(); b++)
            {
                if (bonusTable.GetBonusStartRole(b).Length == 0) continue;
                int index = b;
                string name = BonusEntryName(parent, bonusTable.bonusNames[b]);
                var existing = LotteryModel.Load(table).Find(name);
                if (existing != null) { menu.AddDisabledItem(new GUIContent(bonusTable.GetBonusDisplayName(b))); continue; }
                menu.AddItem(new GUIContent(bonusTable.GetBonusDisplayName(b)), false, delegate
                {
                    EditLottery(table, "ボーナス付きの行の追加", m =>
                    {
                        var source = m.Find(parent);
                        if (source == null) return;
                        var row = NewLotteryEntry(m, name);
                        row.BaseFlag = parent;
                        row.Bonus = bonusTable.bonusNames[index];
                        row.Roles.AddRange(source.Roles);
                        string start = bonusTable.GetBonusStartRole(index);
                        if (start.Length > 0 && !row.Roles.Contains(start)) row.Roles.Add(start);
                        int at = m.Entries.IndexOf(source);
                        for (int i = at + 1; i < m.Entries.Count; i++)
                            if (m.Entries[i].IsBonusRow && m.Entries[i].Base == parent) at = i;
                        m.Entries.Insert(at + 1, row);
                    });
                });
            }
            menu.ShowAsContext();
        }

        private void ShowLotteryRowMenu(LotteryTable table, string name, bool sub)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("上へ"), false, delegate { EditLottery(table, "フラグの移動", m => MoveLotteryEntry(m, name, -1)); });
            menu.AddItem(new GUIContent("下へ"), false, delegate { EditLottery(table, "フラグの移動", m => MoveLotteryEntry(m, name, 1)); });
            if (!sub)
                menu.AddItem(new GUIContent("ハズレ（余り）にする"), false, delegate
                {
                    EditLottery(table, "ハズレの変更", m => { foreach (var e in m.Entries) e.Remainder = e.Name == name; });
                });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("削除"), false, delegate
            {
                EditLottery(table, "フラグの削除", m => m.Entries.RemoveAll(e => e.Name == name || (e.IsBonusRow && e.BaseFlag == name)));
            });
            menu.ShowAsContext();
        }

        /// <summary>同じ種類の行（親どうし、同じ親のボーナス付きどうし）と入れ替える。</summary>
        private static void MoveLotteryEntry(LotteryModel m, string name, int direction)
        {
            var e = m.Find(name);
            if (e == null) return;
            int at = m.Entries.IndexOf(e);
            for (int i = at + direction; i >= 0 && i < m.Entries.Count; i += direction)
            {
                var other = m.Entries[i];
                bool same = e.IsBonusRow ? other.IsBonusRow && other.Base == e.Base : !other.IsBonusRow && !other.Remainder;
                if (!same) continue;
                m.Entries[i] = e;
                m.Entries[at] = other;
                return;
            }
        }
    }
}
