using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    // 演出エディタの「抽選」タブ。成立フラグ（＋成立中のボーナス）ごとに、パターンの振り分けを設定別に決める。
    public sealed partial class EffectEditorWindow
    {
        private int lotIndex;
        /// <summary>選んでいる振り分け表（フラグ＋ボーナスのキー）。最後の1つが右に出る。</summary>
        private readonly List<string> lotSelection = new List<string>();
        private readonly HashSet<string> lotCollapsed = new HashSet<string>();
        /// <summary>前の描画で並んでいた行（Shift での範囲選択用）。</summary>
        private List<string> lotVisible = new List<string>();
        private Vector2 lotFlagScroll, lotMatrixScroll;
        private static EffectLotCell lotClipboard;

        private static readonly Color WarnColor = new Color(0.95f, 0.75f, 0.3f);

        private EffectLotTable LotTable { get { return model != null && lotIndex >= 0 && lotIndex < model.LotTables.Count ? model.LotTables[lotIndex] : null; } }

        private static string CellKey(string flag, string bonus) { return flag + "\n" + bonus; }
        private static string KeyFlag(string key) { return key.Substring(0, key.IndexOf('\n')); }
        private static string KeyBonus(string key) { return key.Substring(key.IndexOf('\n') + 1); }

        // ------------------------------------------------------------ 機種の情報

        private sealed class LotSituation
        {
            public string Key;
            public string Label;
            public LotteryTable Table;
        }

        private sealed class LotMachine
        {
            public readonly List<LotSituation> Situations = new List<LotSituation>();
            /// <summary>成立中になりうるボーナス（内部名, 表示名）。</summary>
            public readonly List<KeyValuePair<string, string>> Bonuses = new List<KeyValuePair<string, string>>();
            public string[] SettingNames = { "設定1" };
        }

        private LotMachine lotMachine;
        private EffectModel lotMachineModel;

        /// <summary>機種の状態・ボーナス・設定。モデルを読み直したときに作り直す。</summary>
        private LotMachine Machine()
        {
            if (lotMachine != null && lotMachineModel == model) return lotMachine;
            lotMachineModel = model;
            lotMachine = new LotMachine();
            var controller = player != null && player.mainBoard != null ? player.mainBoard.reelOutputController : null;
            if (controller == null) return lotMachine;
            var so = new SerializedObject(controller);
            var basic = so.FindProperty("lotteryTable").objectReferenceValue as LotteryTable;
            var bonusTable = so.FindProperty("bonusTable").objectReferenceValue as BonusTable;

            if (bonusTable != null && bonusTable.GetStateCount() > 0)
            {
                for (int i = 0; i < bonusTable.GetStateCount(); i++)
                {
                    string name = bonusTable.stateNames[i] ?? "";
                    string display = bonusTable.stateDisplayNames != null && i < bonusTable.stateDisplayNames.Length ? bonusTable.stateDisplayNames[i] : "";
                    if (string.IsNullOrEmpty(display)) display = name.Length > 0 ? name : "通常時";
                    var table = bonusTable.GetStateLotteryTable(i);
                    lotMachine.Situations.Add(new LotSituation { Key = "S:" + name, Label = display, Table = table != null ? table : basic });
                }
            }
            else lotMachine.Situations.Add(new LotSituation { Key = "S:", Label = "通常時", Table = basic });

            var bonusDisplay = new Dictionary<string, string>();
            if (bonusTable != null)
            {
                for (int i = 0; i < bonusTable.GetBonusCount(); i++)
                {
                    string name = bonusTable.bonusNames[i] ?? "";
                    string display = bonusTable.bonusDisplayNames != null && i < bonusTable.bonusDisplayNames.Length ? bonusTable.bonusDisplayNames[i] : "";
                    if (string.IsNullOrEmpty(display)) display = name;
                    bonusDisplay[name] = display;
                    var table = bonusTable.GetBonusLotteryTable(i);
                    lotMachine.Situations.Add(new LotSituation { Key = "B:" + name, Label = display + "作動中", Table = table != null ? table : basic });
                }
            }

            // 抽選表の行に出てくるボーナスだけが成立中になる。
            foreach (var situation in lotMachine.Situations)
            {
                if (situation.Table == null || situation.Table.entryBonusNames == null) continue;
                foreach (string bonus in situation.Table.entryBonusNames)
                {
                    if (string.IsNullOrEmpty(bonus) || lotMachine.Bonuses.Exists(b => b.Key == bonus)) continue;
                    string display;
                    lotMachine.Bonuses.Add(new KeyValuePair<string, string>(bonus, bonusDisplay.TryGetValue(bonus, out display) ? display : bonus));
                }
            }

            if (basic != null)
            {
                int count = basic.GetSettingCount();
                lotMachine.SettingNames = new string[count];
                for (int i = 0; i < count; i++)
                {
                    bool named = basic.settingNames != null && i < basic.settingNames.Length && !string.IsNullOrEmpty(basic.settingNames[i]);
                    lotMachine.SettingNames[i] = named ? basic.settingNames[i] : "設定" + (i + 1);
                }
            }
            return lotMachine;
        }

        /// <summary>テーブルが対象にする状態の抽選表から、成立フラグ（内部名, 表示名）を集める。ボーナス付きの行は元のフラグにまとめる。</summary>
        private List<KeyValuePair<string, string>> LotFlags(EffectLotTable table)
        {
            var list = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<LotteryTable>();
            foreach (var situation in Machine().Situations)
            {
                if (table.Auto && table.Situations.Count > 0 && !table.Situations.Contains(situation.Key)) continue;
                var lottery = situation.Table;
                if (lottery == null || !seen.Add(lottery)) continue;
                var names = new SerializedObject(lottery).FindProperty("winpairNames");
                if (names == null) continue;
                for (int i = 0; i < names.arraySize; i++)
                {
                    string flag = lottery.GetEntryBaseFlag(names.GetArrayElementAtIndex(i).stringValue);
                    if (string.IsNullOrEmpty(flag) || list.Exists(f => f.Key == flag)) continue;
                    list.Add(new KeyValuePair<string, string>(flag, lottery.GetFlagDisplayName(flag)));
                }
            }
            // 抽選表から消えたフラグの振り分け表も出す（消せるように）
            foreach (var cell in table.Cells)
                if (cell.Flag.Length > 0 && !list.Exists(f => f.Key == cell.Flag)) list.Add(new KeyValuePair<string, string>(cell.Flag, cell.Flag));
            return list;
        }

        private List<KeyValuePair<string, string>> LotBonuses(EffectLotTable table)
        {
            var list = new List<KeyValuePair<string, string>>(Machine().Bonuses);
            foreach (var cell in table.Cells)
                if (cell.Bonus.Length > 0 && !list.Exists(b => b.Key == cell.Bonus)) list.Add(new KeyValuePair<string, string>(cell.Bonus, cell.Bonus));
            return list;
        }

        // ------------------------------------------------------------ 一覧（テーブル・演出モード）

        private void AddLotTable()
        {
            Edit("抽選テーブルの追加", delegate
            {
                model.LotTables.Add(new EffectLotTable
                {
                    Name = EffectModel.UniqueName("抽選テーブル", model.LotTableNames()),
                    Id = EffectModel.UniqueId("Table1", model.LotTableIds())
                });
                lotIndex = model.LotTables.Count - 1;
                lotSelection.Clear();
            });
        }

        private void AddLotTableMenu(GenericMenu menu, int index)
        {
            menu.AddItem(new GUIContent("複製"), false, () => Edit("抽選テーブルの複製", delegate
            {
                var src = model.LotTables[index];
                model.LotTables.Insert(index + 1, src.Copy(EffectModel.UniqueName(src.Name, model.LotTableNames()), EffectModel.UniqueId(src.Id, model.LotTableIds())));
                lotIndex = index + 1;
            }));
            menu.AddItem(new GUIContent("削除"), false, () => Edit("抽選テーブルの削除", delegate
            {
                model.RemoveLotTable(model.LotTables[index]);
                lotIndex = Mathf.Clamp(lotIndex, 0, model.LotTables.Count - 1);
                lotSelection.Clear();
            }));
        }

        /// <summary>同じチャンネル・演出モードで、状態が重なる自動抽選のテーブルがほかにある。</summary>
        private bool LotTableConflicts(EffectLotTable table)
        {
            if (!table.Auto) return false;
            foreach (var other in model.LotTables)
            {
                if (other == table || !other.Auto || other.Channel != table.Channel || other.Mode != table.Mode) continue;
                if (table.Situations.Count == 0 && other.Situations.Count == 0) return true;
                foreach (string key in table.Situations) if (other.Situations.Contains(key)) return true;
            }
            return false;
        }

        private void DrawModeList()
        {
            if (SectionHeader("演出モード"))
                Edit("演出モードの追加", () => model.Modes.Add(EffectModel.UniqueName("モード", model.Modes)));
            for (int i = 0; i < model.Modes.Count; i++)
            {
                string mode = model.Modes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.DelayedTextField(mode);
                    if (EditorGUI.EndChangeCheck() && name.Trim().Length > 0 && name != mode)
                    {
                        var used = new List<string>(model.Modes);
                        used.Remove(mode);
                        Edit("演出モード名の変更", () => model.RenameMode(mode, EffectModel.UniqueName(name.Trim(), used)));
                    }
                    if (GUILayout.Button("×", GUILayout.Width(22))) Edit("演出モードの削除", () => model.RemoveMode(mode));
                }
            }
            GUILayout.Space(4);
        }

        // ------------------------------------------------------------ 次ゲームの予定（パターンのタブ）

        /// <summary>次ゲームの予定の選択。パターンか抽選テーブルを選ぶ。bonus は「ボーナス成立中」の欄。</summary>
        private void DrawNextPopup(EffectPattern pattern, bool bonus)
        {
            var options = new List<string> { bonus ? "通常と同じ" : "なし" };
            foreach (string name in model.PatternNames()) options.Add(name.Replace("/", "／"));
            foreach (string name in model.LotTableNames()) options.Add("抽選：" + name.Replace("/", "／"));
            var nextPattern = bonus ? pattern.NextBonus : pattern.Next;
            var nextTable = bonus ? pattern.NextBonusTable : pattern.NextTable;
            int current = nextPattern != null ? model.Patterns.IndexOf(nextPattern) + 1
                : nextTable != null ? model.LotTables.IndexOf(nextTable) + 1 + model.Patterns.Count : 0;
            int picked = EditorGUILayout.Popup(current, options.ToArray(), GUILayout.Width(200));
            if (picked == current) return;
            Edit("次ゲームの予定の変更", delegate
            {
                var p = picked >= 1 && picked <= model.Patterns.Count ? model.Patterns[picked - 1] : null;
                var t = picked > model.Patterns.Count ? model.LotTables[picked - 1 - model.Patterns.Count] : null;
                if (bonus) { pattern.NextBonus = p; pattern.NextBonusTable = t; }
                else { pattern.Next = p; pattern.NextTable = t; }
            });
        }

        // ------------------------------------------------------------ 抽選

        private void DrawLotteryView()
        {
            var table = LotTable;
            using (new EditorGUILayout.VerticalScope())
            {
                if (table == null) { GUILayout.FlexibleSpace(); return; }
                DrawLotHeader(table);
                GUILayout.Space(4);
                HandleLotCommands(table);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawLotFlags(table);
                    var line = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
                    EditorGUI.DrawRect(line, HeaderColor);
                    DrawLotMatrix(table);
                }
            }
        }

        private void DrawLotHeader(EffectLotTable table)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.DelayedTextField(table.Name, EditorStyles.boldLabel, GUILayout.MinWidth(160));
                if (EditorGUI.EndChangeCheck() && name.Trim().Length > 0 && name != table.Name)
                {
                    var used = model.LotTableNames();
                    used.Remove(table.Name);
                    Edit("抽選テーブル名の変更", () => table.Name = EffectModel.UniqueName(name.Trim(), used));
                }
                GUILayout.Space(16);
                GUILayout.Label("内部名", GUILayout.Width(44));
                EditorGUI.BeginChangeCheck();
                string id = EditorGUILayout.DelayedTextField(table.Id, GUILayout.Width(160));
                if (EditorGUI.EndChangeCheck() && id.Trim().Length > 0 && id != table.Id)
                {
                    var used = model.LotTableIds();
                    used.Remove(table.Id);
                    Edit("内部名の変更", () => table.Id = EffectModel.UniqueId(id, used));
                }
                GUILayout.FlexibleSpace();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("チャンネル", GUILayout.Width(62));
                var channels = new string[EffectPlayer.ChannelCount];
                for (int c = 0; c < channels.Length; c++) channels[c] = c + (player.IsResidentChannel(c) ? "  常駐" : "");
                int channel = EditorGUILayout.Popup(Mathf.Clamp(table.Channel, 0, channels.Length - 1), channels, GUILayout.Width(80));
                if (channel != table.Channel) Edit("チャンネルの変更", () => table.Channel = channel);
                GUILayout.Space(16);
                EditorGUI.BeginChangeCheck();
                bool auto = GUILayout.Toggle(table.Auto, "自動抽選", GUILayout.Width(76));
                if (EditorGUI.EndChangeCheck()) Edit("自動抽選の変更", () => table.Auto = auto);
                using (new EditorGUI.DisabledScope(!table.Auto))
                {
                    GUILayout.Space(16);
                    GUILayout.Label("状態", GUILayout.Width(30));
                    if (GUILayout.Button(SituationSummary(table), EditorStyles.popup, GUILayout.Width(180))) ShowSituationMenu(table);
                    GUILayout.Space(16);
                    GUILayout.Label("演出モード", GUILayout.Width(62));
                    var modes = new List<string> { "指定なし" };
                    modes.AddRange(model.Modes);
                    int current = Mathf.Max(0, model.Modes.IndexOf(table.Mode) + 1);
                    int mode = EditorGUILayout.Popup(current, modes.ToArray(), GUILayout.Width(140));
                    if (mode != current) Edit("演出モードの変更", () => table.Mode = mode == 0 ? "" : model.Modes[mode - 1]);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private string SituationSummary(EffectLotTable table)
        {
            if (table.Situations.Count == 0) return "すべて";
            var labels = new List<string>();
            foreach (string key in table.Situations)
            {
                var situation = Machine().Situations.Find(s => s.Key == key);
                labels.Add(situation != null ? situation.Label : key);
            }
            return string.Join("、", labels.ToArray());
        }

        private void ShowSituationMenu(EffectLotTable table)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("すべて"), table.Situations.Count == 0, () => Edit("状態の変更", () => table.Situations.Clear()));
            menu.AddSeparator("");
            foreach (var situation in Machine().Situations)
            {
                string key = situation.Key;
                bool on = table.Situations.Contains(key);
                menu.AddItem(new GUIContent(situation.Label.Replace("/", "／")), on, () => Edit("状態の変更", delegate
                {
                    if (on) table.Situations.Remove(key);
                    else table.Situations.Add(key);
                }));
            }
            // 機種から消えた状態も外せるように出す
            foreach (string key in table.Situations)
            {
                if (Machine().Situations.Exists(s => s.Key == key)) continue;
                string stale = key;
                menu.AddItem(new GUIContent(stale), true, () => Edit("状態の変更", () => table.Situations.Remove(stale)));
            }
            menu.ShowAsContext();
        }

        // ------------------------------------------------------------ 成立フラグの一覧

        private void DrawLotFlags(EffectLotTable table)
        {
            var flags = LotFlags(table);
            var bonuses = LotBonuses(table);
            var visible = new List<string>();
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(250)))
            {
                var head = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(head, HeaderColor);
                GUI.Label(new Rect(head.x + 6, head.y, head.width, head.height), "成立フラグ", EditorStyles.boldLabel);
                lotFlagScroll = EditorGUILayout.BeginScrollView(lotFlagScroll);
                DrawLotFlagGroup(table, "", "共通", bonuses, visible);
                foreach (var flag in flags) DrawLotFlagGroup(table, flag.Key, flag.Value, bonuses, visible);
                EditorGUILayout.EndScrollView();
            }
            lotVisible = visible;
        }

        private void DrawLotFlagGroup(EffectLotTable table, string flag, string label, List<KeyValuePair<string, string>> bonuses, List<string> visible)
        {
            bool open = !lotCollapsed.Contains(flag);
            DrawLotFlagRow(table, flag, "", label, 0, bonuses.Count > 0, open, visible);
            if (!open) return;
            foreach (var bonus in bonuses) DrawLotFlagRow(table, flag, bonus.Key, bonus.Value + "成立中", 1, false, false, visible);
        }

        private void DrawLotFlagRow(EffectLotTable table, string flag, string bonus, string label, int indent, bool foldable, bool open, List<string> visible)
        {
            string key = CellKey(flag, bonus);
            visible.Add(key);
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            bool selected = lotSelection.Contains(key);
            if (selected) EditorGUI.DrawRect(rect, CellSelectedColor);
            float x = rect.x + 4 + indent * 18;
            var e = Event.current;
            if (foldable)
            {
                var fold = new Rect(x, rect.y, 16, rect.height);
                GUI.Label(fold, open ? "▾" : "▸");
                if (e.type == EventType.MouseDown && e.button == 0 && fold.Contains(e.mousePosition))
                {
                    if (open) lotCollapsed.Add(flag);
                    else lotCollapsed.Remove(flag);
                    e.Use();
                    Repaint();
                    return;
                }
            }
            GUI.Label(new Rect(x + 16, rect.y, rect.width - 80 - indent * 18, rect.height), label);

            var cell = table.Cell(flag, bonus);
            string mark = cell != null ? (cell.Rows.Count > 0 ? "●" : "なし") : (flag.Length > 0 && table.Cell("", bonus) != null ? "共通" : "");
            if (cell != null && cell.Rows.Count > 0 && LotCellOver(cell)) mark = "⚠";
            if (mark.Length > 0) GUI.Label(new Rect(rect.xMax - 44, rect.y, 40, rect.height), mark, mark == "●" || mark == "⚠" ? CenterLabel : CenterMuted);

            if (e.type != EventType.MouseDown || !rect.Contains(e.mousePosition)) return;
            bool menu = e.button == 1, toggle = e.control || e.command, range = e.shift;
            Switch(delegate
            {
                bool on = lotSelection.Contains(key);
                if (menu)
                {
                    if (!on) { lotSelection.Clear(); lotSelection.Add(key); }
                    ShowLotCellMenu(table);
                }
                else if (toggle)
                {
                    if (on) lotSelection.Remove(key);
                    else lotSelection.Add(key);
                }
                else if (range && lotSelection.Count > 0)
                {
                    int from = lotVisible.IndexOf(lotSelection[lotSelection.Count - 1]), to = lotVisible.IndexOf(key);
                    if (from >= 0 && to >= 0)
                    {
                        for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
                            if (!lotSelection.Contains(lotVisible[i])) lotSelection.Add(lotVisible[i]);
                        lotSelection.Remove(key);
                    }
                    lotSelection.Add(key);
                }
                else
                {
                    lotSelection.Clear();
                    lotSelection.Add(key);
                }
            });
            e.Use();
            Repaint();
        }

        private bool LotCellOver(EffectLotCell cell)
        {
            for (int s = 0; s < (cell.PerSetting ? model.SettingCount : 1); s++)
                if (cell.Sum(s) > EffectPlayer.LotteryDenominator) return true;
            return false;
        }

        private void ShowLotCellMenu(EffectLotTable table)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("コピー"), false, () => CopyLotCell(table));
            if (lotClipboard != null) menu.AddItem(new GUIContent("貼り付け"), false, () => PasteLotCell(table));
            else menu.AddDisabledItem(new GUIContent("貼り付け"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("抽選しない"), false, () => Edit("抽選しないにする", delegate
            {
                foreach (string key in lotSelection) SetLotCell(table, key, new EffectLotCell());
            }));
            menu.AddItem(new GUIContent("クリア"), false, () => ClearLotCells(table));
            menu.ShowAsContext();
        }

        private void CopyLotCell(EffectLotTable table)
        {
            if (lotSelection.Count == 0) return;
            string key = lotSelection[lotSelection.Count - 1];
            var cell = table.Cell(KeyFlag(key), KeyBonus(key));
            lotClipboard = cell != null ? cell.Copy("", "") : null;
        }

        private void PasteLotCell(EffectLotTable table)
        {
            if (lotClipboard == null || lotSelection.Count == 0) return;
            // ほかの EffectPlayer でコピーしたものは、パターンが違うので貼らない。
            foreach (var row in lotClipboard.Rows) if (row.Pattern != null && !model.Patterns.Contains(row.Pattern)) return;
            Edit("振り分けの貼り付け", delegate
            {
                foreach (string key in lotSelection) SetLotCell(table, key, lotClipboard);
            });
        }

        private void ClearLotCells(EffectLotTable table)
        {
            Edit("振り分けのクリア", delegate
            {
                foreach (string key in lotSelection) table.Cells.RemoveAll(c => c.Flag == KeyFlag(key) && c.Bonus == KeyBonus(key));
            });
        }

        private void SetLotCell(EffectLotTable table, string key, EffectLotCell source)
        {
            string flag = KeyFlag(key), bonus = KeyBonus(key);
            var copy = source.Copy(flag, bonus);
            foreach (var row in copy.Rows) Array.Resize(ref row.Values, model.SettingCount);
            int index = table.Cells.FindIndex(c => c.Flag == flag && c.Bonus == bonus);
            if (index >= 0) table.Cells[index] = copy;
            else table.Cells.Add(copy);
        }

        /// <summary>Ctrl+C / Ctrl+V / Delete（文字の入力中は入力欄に任せる）。</summary>
        private void HandleLotCommands(EffectLotTable table)
        {
            var e = Event.current;
            if (EditorGUIUtility.editingTextField || lotSelection.Count == 0) return;
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) return;
            bool known = e.commandName == "Copy" || e.commandName == "Paste" || e.commandName == "Delete" || e.commandName == "SoftDelete";
            if (!known) return;
            if (e.type == EventType.ExecuteCommand)
            {
                if (e.commandName == "Copy") CopyLotCell(table);
                else if (e.commandName == "Paste") PasteLotCell(table);
                else ClearLotCells(table);
            }
            e.Use();
        }

        // ------------------------------------------------------------ 振り分け表（パターン × 設定）

        private void DrawLotMatrix(EffectLotTable table)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (lotSelection.Count == 0) { GUILayout.FlexibleSpace(); return; }
                string key = lotSelection[lotSelection.Count - 1];
                string flag = KeyFlag(key), bonus = KeyBonus(key);
                var cell = table.Cell(flag, bonus);
                bool perSetting = cell != null && cell.PerSetting;
                int columns = perSetting ? model.SettingCount : 1;
                var settingNames = Machine().SettingNames;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(LotCellTitle(table, flag, bonus), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(cell == null || model.SettingCount <= 1))
                    {
                        EditorGUI.BeginChangeCheck();
                        bool common = GUILayout.Toggle(!perSetting, "全設定共通", GUILayout.Width(90));
                        if (EditorGUI.EndChangeCheck() && cell != null)
                            Switch(() => Edit("全設定共通の変更", delegate
                            {
                                // どちらへ切り替えても、設定1の値を全設定へ広げる。
                                foreach (var row in cell.Rows)
                                    for (int s = 1; s < row.Values.Length; s++) row.Values[s] = row.Values[0];
                                cell.PerSetting = !common;
                            }));
                    }
                }
                GUILayout.Space(2);

                const float nameWidth = 200, paramWidth = 70, valueWidth = 62, percentWidth = 54;
                lotMatrixScroll = EditorGUILayout.BeginScrollView(lotMatrixScroll);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("パターン", EditorStyles.miniLabel, GUILayout.Width(nameWidth));
                    GUILayout.Label("パラメータ", EditorStyles.miniLabel, GUILayout.Width(paramWidth));
                    for (int s = 0; s < columns; s++)
                    {
                        string head = perSetting ? (s < settingNames.Length ? settingNames[s] : "設定" + (s + 1)) : "全設定";
                        GUILayout.Label(head, EditorStyles.miniLabel, GUILayout.Width(valueWidth + percentWidth + 4));
                    }
                    GUILayout.FlexibleSpace();
                }

                if (cell != null)
                {
                    for (int r = 0; r < cell.Rows.Count; r++)
                    {
                        var row = cell.Rows[r];
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(row.Pattern != null ? row.Pattern.Name : "停止", EditorStyles.popup, GUILayout.Width(nameWidth)))
                                EffectPatternPicker.Show(this, picked => Edit("パターンの変更", () => row.Pattern = picked));
                            EditorGUI.BeginChangeCheck();
                            int parameter = EffectFields.Int(row.Parameter, GUILayout.Width(paramWidth));
                            if (EditorGUI.EndChangeCheck()) Edit("パラメータの変更", () => row.Parameter = parameter);
                            for (int s = 0; s < columns; s++)
                            {
                                int column = s;
                                int value;
                                if (EffectFields.EditInt(row.Values[s], out value, GUILayout.Width(valueWidth)))
                                    Edit("振り分けの変更", delegate
                                    {
                                        row.Values[column] = Mathf.Clamp(value, 0, EffectPlayer.LotteryDenominator);
                                        if (!cell.PerSetting) for (int i = 1; i < row.Values.Length; i++) row.Values[i] = row.Values[0];
                                    });
                                GUILayout.Label(Percent(row.Values[s]), MutedLabel, GUILayout.Width(percentWidth));
                                GUILayout.Space(4);
                            }
                            if (GUILayout.Button("×", GUILayout.Width(22)))
                                Switch(() => Edit("振り分けの削除", delegate
                                {
                                    cell.Rows.Remove(row);
                                    if (cell.Rows.Count == 0) table.Cells.Remove(cell);
                                }));
                            GUILayout.FlexibleSpace();
                        }
                    }
                    if (cell.Rows.Count > 0)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label("なし", GUILayout.Width(nameWidth));
                            GUILayout.Space(paramWidth + 4);
                            for (int s = 0; s < columns; s++)
                            {
                                int rest = EffectPlayer.LotteryDenominator - cell.Sum(s);
                                var style = rest < 0 ? WarnLabel : EditorStyles.label;
                                GUILayout.Label(rest < 0 ? "⚠ " + rest : rest.ToString(), style, GUILayout.Width(valueWidth));
                                GUILayout.Label(Percent(Mathf.Max(0, rest)), MutedLabel, GUILayout.Width(percentWidth));
                                GUILayout.Space(4);
                            }
                            GUILayout.FlexibleSpace();
                        }
                    }
                }
                GUILayout.Space(4);
                if (GUILayout.Button("＋ パターン", GUILayout.Width(110)))
                {
                    EffectPatternPicker.Show(this, picked => Edit("振り分けの追加", delegate
                    {
                        var target = table.Cell(flag, bonus);
                        if (target == null)
                        {
                            target = new EffectLotCell { Flag = flag, Bonus = bonus };
                            table.Cells.Add(target);
                        }
                        target.Rows.Add(new EffectLotRow { Pattern = picked, Values = new int[model.SettingCount] });
                    }));
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private string LotCellTitle(EffectLotTable table, string flag, string bonus)
        {
            string title = "共通";
            if (flag.Length > 0)
            {
                var found = LotFlags(table).Find(f => f.Key == flag);
                title = found.Value ?? flag;
            }
            if (bonus.Length == 0) return title;
            var b = LotBonuses(table).Find(x => x.Key == bonus);
            return title + " × " + (b.Value ?? bonus) + "成立中";
        }

        private static string Percent(int value)
        {
            return (value * 100.0 / EffectPlayer.LotteryDenominator).ToString("0.##") + "%";
        }

        private static GUIStyle mutedLabel, warnLabel, centerLabel;

        private static GUIStyle MutedLabel
        {
            get
            {
                if (mutedLabel == null)
                {
                    mutedLabel = new GUIStyle(EditorStyles.label);
                    mutedLabel.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
                }
                return mutedLabel;
            }
        }

        private static GUIStyle WarnLabel
        {
            get
            {
                if (warnLabel == null)
                {
                    warnLabel = new GUIStyle(EditorStyles.label);
                    warnLabel.normal.textColor = WarnColor;
                }
                return warnLabel;
            }
        }

        private static GUIStyle CenterLabel
        {
            get { return centerLabel ?? (centerLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter }); }
        }
    }

    /// <summary>振り分けに置くパターンを検索して選ぶ窓。「停止」も選べる（null を返す）。</summary>
    public sealed class EffectPatternPicker : EditorWindow
    {
        private EffectEditorWindow owner;
        private Action<EffectPattern> onPick;
        private string search = "";
        private Vector2 scroll;
        private bool focused;

        public static void Show(EffectEditorWindow owner, Action<EffectPattern> onPick)
        {
            var window = CreateInstance<EffectPatternPicker>();
            window.titleContent = new GUIContent("パターンを選択");
            window.owner = owner;
            window.onPick = onPick;
            window.minSize = new Vector2(320, 360);
            window.wantsMouseMove = true;
            window.ShowUtility();
        }

        private void OnGUI()
        {
            var model = owner != null ? owner.Model : null;
            if (model == null)
            {
                Close();
                return;
            }
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Close();
                return;
            }

            GUI.SetNextControlName("search");
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (!focused)
            {
                EditorGUI.FocusTextInControl("search");
                focused = true;
            }

            var hits = new List<EffectPattern>();
            foreach (var p in model.Patterns)
            {
                bool hit = search.Length == 0 || p.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           p.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                if (hit) hits.Add(p);
            }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && hits.Count > 0)
            {
                Pick(hits[0]);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var p in hits)
            {
                if (!Row(p.Name, e)) continue;
                Pick(p);
                GUIUtility.ExitGUI();
            }
            if (search.Length == 0 && Row("停止", e))
            {
                Pick(null);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndScrollView();
            GUILayout.Label(hits.Count + " / " + model.Patterns.Count, EditorStyles.miniLabel);
            if (e.type == EventType.MouseMove) Repaint();
        }

        private static bool Row(string label, Event e)
        {
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            if (rect.Contains(e.mousePosition)) EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f));
            GUI.Label(new Rect(rect.x + 8, rect.y, rect.width - 8, rect.height), label);
            return e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition);
        }

        private void Pick(EffectPattern pattern)
        {
            var callback = onPick;
            Close();
            if (callback != null) callback(pattern);
        }

        private void OnLostFocus()
        {
            Close();
        }
    }
}
