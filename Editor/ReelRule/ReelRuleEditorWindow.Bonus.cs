using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ⑤ ボーナス・RT: 役物の定義、状態（RT）と移行
    public sealed partial class ReelRuleEditorWindow
    {
        private static readonly string[] BonusKindNames =
        {
            "SB（普通役物）", "RB（第一種特別役物）", "CB（第二種特別役物）",
            "BB（第一種特別役物に係る役物連続作動装置）", "MB（第二種特別役物に係る役物連続作動装置）"
        };
        private static readonly Color AutoRowText = new Color(0.55f, 0.55f, 0.55f);

        [SerializeField] private int bonusIndex;
        private Vector2 bonusListScroll, bonusDetailScroll, rtScroll;

        private static readonly string[] JacInNames = { "自動", "手動" };

        private void EditBonus(string label, Action<BonusModel> change)
        {
            var table = MachineBonusTable;
            if (table == null) return;
            var model = BonusModel.Load(table);
            change(model);
            model.Save(label);
            Changed();
            PrepareCells();
            Repaint();
        }

        private void DrawBonusScreen()
        {
            var table = MachineBonusTable;
            if (table == null)
            {
                if (GUILayout.Button("ボーナス・RT表を作成", GUILayout.Width(160))) { CreateBonusTable(); GUIUtility.ExitGUI(); }
                return;
            }
            var model = BonusModel.Load(table);
            string saveLabel = null;
            bonusIndex = Mathf.Clamp(bonusIndex, 0, Mathf.Max(0, model.Bonuses.Count - 1));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
                {
                    if (DrawBonusList(model)) saveLabel = "役物の追加";
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    bonusDetailScroll = EditorGUILayout.BeginScrollView(bonusDetailScroll);
                    if (model.Bonuses.Count > 0 && DrawBonusDetail(model, model.Bonuses[bonusIndex])) saveLabel = "役物の変更";
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(500)))
                {
                    rtScroll = EditorGUILayout.BeginScrollView(rtScroll);
                    if (DrawRtPanel(model)) saveLabel = "RTの変更";
                    EditorGUILayout.EndScrollView();
                }
            }
            if (saveLabel != null)
            {
                model.Save(saveLabel);
                Changed();
            }
        }

        // ------------------------------------------------------------ 左: 役物の一覧

        private bool DrawBonusList(BonusModel model)
        {
            PanelHeader("役物");
            bonusListScroll = EditorGUILayout.BeginScrollView(bonusListScroll);
            for (int i = 0; i < model.Bonuses.Count; i++)
            {
                var b = model.Bonuses[i];
                var rect = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
                if (i == bonusIndex) EditorGUI.DrawRect(rect, SelectedColor);
                GUI.Label(new Rect(rect.x + 8, rect.y + 2, rect.width - 60, 16), b.Label, EditorStyles.boldLabel);
                GUI.Label(new Rect(rect.x + 8, rect.y + 16, rect.width - 60, 14), b.Name, EditorStyles.miniLabel);
                Color back, text;
                BonusKindColors(b.Kind, out back, out text);
                string kind = b.Kind.ToString();
                var size = ChipSize(kind);
                DrawChip(new Rect(rect.xMax - size.x - 8, rect.y + 7, size.x, 16), kind, back, text);
                int index = i;
                if (Clicked(rect, 0)) { bonusIndex = index; GUI.FocusControl(null); }
                else if (RightClicked(rect))
                {
                    bonusIndex = index;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("複製"), false, delegate { DuplicateBonus(index); });
                    menu.AddItem(new GUIContent("削除"), false, delegate { EditBonus("役物の削除", m => m.RemoveBonus(index)); });
                    menu.ShowAsContext();
                }
            }
            EditorGUILayout.EndScrollView();
            bool added = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 追加"))
                {
                    var b = new BonusRow { Name = ReelDesign.NumberedName("Bonus", n => model.FindBonus(n) >= 0) };
                    ApplyKindDefaults(b, true);
                    model.Bonuses.Add(b);
                    bonusIndex = model.Bonuses.Count - 1;
                    added = true;
                }
                using (new EditorGUI.DisabledScope(model.Bonuses.Count == 0))
                    if (GUILayout.Button("複製")) DuplicateBonus(bonusIndex);
            }
            GUILayout.Space(4);
            return added;
        }

        private void DuplicateBonus(int index)
        {
            EditBonus("役物の複製", m =>
            {
                var copy = m.Bonuses[index].Copy();
                copy.Name = ReelDesign.NumberedName(copy.Name, n => m.FindBonus(n) >= 0);
                copy.Display = "";
                m.Bonuses.Insert(index + 1, copy);
                foreach (var b in m.Bonuses) if (b.Child > index) b.Child++;
                bonusIndex = index + 1;
            });
        }

        /// <summary>種別に合わせた終了条件の初期値。</summary>
        private void ApplyKindDefaults(BonusRow b, bool always)
        {
            var regulation = asset != null ? Design.regulation : Regulation.Gen6;
            switch (b.Kind)
            {
                case BonusKind.BB:
                    if (always || b.EndPayout == 0) b.EndPayout = RegulationLimits.BigPayout(regulation);
                    b.EndWins = 0;
                    b.RequiredBet = 3;
                    break;
                case BonusKind.MB:
                    if (always || b.EndPayout == 0) b.EndPayout = RegulationLimits.MiddlePayout(regulation);
                    b.EndWins = 0;
                    break;
                case BonusKind.RB:
                    b.EndPayout = 0;
                    if (always || b.EndWins == 0) b.EndWins = RegulationLimits.RegularWins;
                    if (always || b.EndGames == 0) b.EndGames = RegulationLimits.RegularGames;
                    break;
                default:
                    b.EndPayout = 0;
                    b.EndGames = 0;
                    b.EndWins = 0;
                    break;
            }
            if (b.Kind == BonusKind.CB && b.QuickReels == 0) b.QuickReels = 1;
        }

        // ------------------------------------------------------------ 中央: 役物の設定

        private bool DrawBonusDetail(BonusModel model, BonusRow b)
        {
            bool changed = false;
            var payout = MachinePayoutTable;
            PanelHeader(b.Label);
            GUILayout.Space(4);
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 110;

            string display = EditorGUILayout.DelayedTextField("表示名", b.Display);
            if (display != b.Display) { b.Display = display; changed = true; }
            string renamed = EditorGUILayout.DelayedTextField("内部名", b.Name);
            if (renamed != b.Name && renamed.Length > 0 && model.FindBonus(renamed) < 0)
            {
                RenameBonus(b.Name, renamed);
                b.Name = renamed;
                changed = true;
            }
            int kind;
            using (new EditorGUILayout.HorizontalScope())
            {
                kind = EditorGUILayout.Popup("種別", (int)b.Kind, BonusKindNames);
                var help = GUILayoutUtility.GetRect(new GUIContent("?"), EditorStyles.miniButton, GUILayout.Width(20));
                if (GUI.Button(help, "?", EditorStyles.miniButton))
                    PopupWindow.Show(help, new BonusKindHelpPopup(asset != null ? Design.regulation : Regulation.Gen6));
            }
            if (kind != (int)b.Kind)
            {
                b.Kind = (BonusKind)kind;
                ApplyKindDefaults(b, false);
                b.Child = -1;
                changed = true;
            }

            int index = model.Bonuses.IndexOf(b);
            bool fromParent = model.Bonuses.Exists(p => p.Child == index && !p.ManualChild);
            if (fromParent && b.StartRole.Length == 0)
            {
                EditorGUILayout.LabelField("作動役", "（" + model.Bonuses.Find(p => p.Child == index).Label + "から作動）");
            }
            else
            {
                var names = new List<string> { "" };
                var labels = new List<string> { "—" };
                if (payout != null)
                    for (int i = 0; i < payout.GetWinPairCount(); i++)
                    {
                        names.Add(payout.winPairNames[i]);
                        labels.Add(RoleLabel(payout, payout.winPairNames[i]));
                    }
                int at = Math.Max(0, names.IndexOf(b.StartRole));
                int next = EditorGUILayout.Popup("作動役", at, labels.ToArray());
                if (next != at) { b.StartRole = names[next]; changed = true; }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var lottery = (LotteryTable)EditorGUILayout.ObjectField("作動中の抽選表", b.Lottery, typeof(LotteryTable), true);
                if (lottery != b.Lottery) { b.Lottery = lottery; changed = true; }
                if (b.Lottery == null)
                {
                    if (GUILayout.Button("作成", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        b.Lottery = CreateLotteryTable(b.Label + "中");
                        changed = true;
                    }
                }
                else if (GUILayout.Button("開く", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    OpenLotteryTable(b.Lottery);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("規定BET");
                int bet = GUILayout.Toolbar(b.RequiredBet - 1, BetNames, GUILayout.Width(96));
                if (bet != b.RequiredBet - 1) { b.RequiredBet = bet + 1; changed = true; }
                GUILayout.FlexibleSpace();
            }

            if (b.Kind == BonusKind.BB || b.Kind == BonusKind.MB)
            {
                // BBはRB、MBはCBを連続作動させる（規則）。
                var childKind = b.Kind == BonusKind.BB ? BonusKind.RB : BonusKind.CB;
                var labels = new List<string>();
                var values = new List<int>();
                if (b.Child < 0) { labels.Add("—"); values.Add(-1); }
                for (int i = 0; i < model.Bonuses.Count; i++)
                    if (i != index && (model.Bonuses[i].Kind == childKind || i == b.Child)) { labels.Add(model.Bonuses[i].Label); values.Add(i); }
                int at = Math.Max(0, values.IndexOf(b.Child));
                int next = EditorGUILayout.Popup("連続作動", at, labels.ToArray());
                if (next != at) { b.Child = values[next]; changed = true; }
                using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel("JACイン");
                        int manual = GUILayout.Toolbar(b.ManualChild ? 1 : 0, JacInNames, GUILayout.Width(96));
                        if (manual != (b.ManualChild ? 1 : 0)) { b.ManualChild = manual == 1; changed = true; }
                        GUILayout.FlexibleSpace();
                    }
            }

            if (b.Kind == BonusKind.CB)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("すぐ止まるリール");
                    for (int reel = 0; reel < 3; reel++)
                    {
                        bool on = (b.QuickReels & (1 << reel)) != 0;
                        bool next = GUILayout.Toggle(on, ReelRuleEngine.ReelNames[reel], EditorStyles.miniButton, GUILayout.Width(32));
                        if (next != on) { b.QuickReels ^= 1 << reel; changed = true; }
                    }
                    GUILayout.FlexibleSpace();
                }
            }

            changed |= StatePopup("成立時のRT", ref b.WonState, model, "なし");
            changed |= StatePopup("終了後のRT", ref b.EndState, model, null);

            GUILayout.Space(8);
            GUILayout.Label("終了条件", EditorStyles.boldLabel);
            changed |= DrawEndConditions(b);

            GUILayout.Space(8);
            GUILayout.Label("当選フラグ", EditorStyles.boldLabel);
            DrawBonusFlags(b);
            EditorGUIUtility.labelWidth = labelWidth;
            return changed;
        }

        /// <summary>状態を選ぶポップアップ。none が null なら「なし」を出さず、-1 を状態0として表示する。</summary>
        private static bool StatePopup(string label, ref int value, BonusModel model, string none)
        {
            var labels = new List<string>();
            var values = new List<int>();
            if (none != null) { labels.Add(none); values.Add(-1); }
            for (int i = 0; i < model.States.Count; i++) { labels.Add(model.States[i].Label); values.Add(i); }
            if (labels.Count == 0) return false;
            int shown = none == null && value < 0 ? 0 : value;
            int at = Math.Max(0, values.IndexOf(shown));
            int next = EditorGUILayout.Popup(label, at, labels.ToArray());
            if (next == at) return false;
            value = values[next];
            return true;
        }

        private bool DrawEndConditions(BonusRow b)
        {
            var regulation = asset != null ? Design.regulation : Regulation.Gen6;
            bool changed = false;
            switch (b.Kind)
            {
                case BonusKind.BB:
                case BonusKind.MB:
                    int limit = b.Kind == BonusKind.BB ? RegulationLimits.BigPayout(regulation) : RegulationLimits.MiddlePayout(regulation);
                    changed |= EndRow("払出", ref b.EndPayout, limit, "枚を超えたら", "規格 " + limit + "以下", limit);
                    changed |= EndRow("遊技", ref b.EndGames, 30, "G", "", 0);
                    break;
                case BonusKind.RB:
                    changed |= EndRow("入賞", ref b.EndWins, RegulationLimits.RegularWins, "回", "規格 " + RegulationLimits.RegularWins + "以下", RegulationLimits.RegularWins);
                    changed |= EndRow("遊技", ref b.EndGames, RegulationLimits.RegularGames, "G", "規格 " + RegulationLimits.RegularGames + "以下", RegulationLimits.RegularGames);
                    break;
                default:
                    GUILayout.Label("1遊技", EditorStyles.miniLabel);
                    break;
            }
            return changed;
        }

        /// <summary>終了条件1行。0 は使わない。</summary>
        private static bool EndRow(string label, ref int value, int initial, string unit, string note, int limit)
        {
            bool changed = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool on = value > 0;
                bool nextOn = GUILayout.Toggle(on, label, GUILayout.Width(60));
                if (nextOn != on) { value = nextOn ? Math.Max(1, initial) : 0; changed = true; }
                using (new EditorGUI.DisabledScope(!nextOn))
                {
                    int next = EditorGUILayout.DelayedIntField(nextOn ? value : 0, GUILayout.Width(60));
                    if (nextOn && next != value && next > 0) { value = next; changed = true; }
                }
                GUILayout.Label(unit, GUILayout.ExpandWidth(false));
                if (note.Length > 0)
                {
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    style.normal.textColor = limit > 0 && value > limit ? DangerColor : AutoRowText;
                    GUILayout.Label("（" + note + "）", style);
                }
                GUILayout.FlexibleSpace();
            }
            return changed;
        }

        private void DrawBonusFlags(BonusRow b)
        {
            LotteryTable first = null;
            var names = new List<string>();
            foreach (var table in ReelMachineReader.LotteryTablesOf(machineObject))
                foreach (var e in LotteryModel.Load(table).Entries)
                    if (e.Bonus == b.Name)
                    {
                        names.Add(e.Name);
                        if (first == null) first = table;
                    }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (names.Count == 0) GUILayout.Label("—", MutedLabel);
                foreach (string name in names) Chip(name, SmallRoleBack, SmallRoleText);
                GUILayout.FlexibleSpace();
            }
            if (GUILayout.Button("抽選表で編集", GUILayout.Width(110)))
                OpenLotteryTable(first ?? ReelMachineReader.LotteryTableOf(machineObject));
        }

        /// <summary>役物の内部名の変更を抽選表と制御のマスへ反映する。</summary>
        private void RenameBonus(string old, string renamed)
        {
            RenameCells(null, null, old, renamed);
            foreach (var table in ReelMachineReader.LotteryTablesOf(machineObject))
            {
                var m = LotteryModel.Load(table);
                bool hit = false;
                foreach (var e in m.Entries)
                {
                    if (e.Bonus != old) continue;
                    e.Bonus = renamed;
                    string name = BonusEntryName(e.Base, renamed);
                    if (m.Find(name) == null) e.Name = name;
                    hit = true;
                }
                if (hit) m.Save("役物の内部名の変更");
            }
        }

        // ------------------------------------------------------------ 右: RT

        private bool DrawRtPanel(BonusModel model)
        {
            bool changed = false;
            var header = PanelHeaderRect("RT");
            if (GUI.Button(new Rect(header.xMax - 150, header.y + 3, 70, header.height - 6), "フロー図", EditorStyles.miniButton))
                RtFlowWindow.Open(this);
            if (GUI.Button(new Rect(header.xMax - 76, header.y + 3, 70, header.height - 6), "＋ 状態", EditorStyles.miniButton))
            {
                model.States.Add(new StateRow { Name = ReelDesign.NumberedName("RT", n => model.States.Exists(s => s.Name == n)) });
                changed = true;
            }

            // 状態
            var head = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(head, HeaderColor);
            GUI.Label(new Rect(head.x + 4, head.y + 2, 150, 16), "状態", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(head.x + 154, head.y + 2, 100, 16), "内部名", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(head.x + 258, head.y + 2, 170, 16), "抽選表", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(head.x + 432, head.y + 2, 60, 16), "終了G", EditorStyles.miniBoldLabel);
            for (int i = 0; i < model.States.Count; i++)
            {
                var s = model.States[i];
                var rect = GUILayoutUtility.GetRect(10, 24, GUILayout.ExpandWidth(true));
                HLine(rect.x, rect.yMax - 1, rect.width);
                float y = rect.y + 3;
                string display = EditorGUI.DelayedTextField(new Rect(rect.x + 4, y, 146, 18), s.Label);
                if (display != s.Label) { s.Display = display == s.Name ? "" : display; changed = true; }
                string renamed = EditorGUI.DelayedTextField(new Rect(rect.x + 154, y, 100, 18), s.Name);
                if (renamed != s.Name && renamed.Length > 0 && !model.States.Exists(o => o.Name == renamed)) { s.Name = renamed; changed = true; }
                var lotteryRect = new Rect(rect.x + 258, y, s.Lottery == null ? 126 : 170, 18);
                var lottery = (LotteryTable)EditorGUI.ObjectField(lotteryRect, s.Lottery, typeof(LotteryTable), true);
                if (lottery != s.Lottery) { s.Lottery = lottery; changed = true; }
                if (s.Lottery == null && GUI.Button(new Rect(rect.x + 386, y, 42, 18), "作成", EditorStyles.miniButton))
                {
                    s.Lottery = CreateLotteryTable(s.Label);
                    changed = true;
                }
                if (i == 0) GUI.Label(new Rect(rect.x + 432, y, 60, 18), "—", MutedLabel);
                else
                {
                    int limit = EditorGUI.DelayedIntField(new Rect(rect.x + 432, y, 56, 18), s.GameLimit);
                    if (limit != s.GameLimit && limit >= 0) { s.GameLimit = limit; changed = true; }
                }
                int index = i;
                if (i > 0 && RightClicked(rect))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("削除"), false, delegate { EditBonus("状態の削除", m => m.RemoveState(index)); });
                    menu.ShowAsContext();
                }
            }

            // 移行
            GUILayout.Space(10);
            var moveHead = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(moveHead, HeaderColor);
            GUI.Label(new Rect(moveHead.x + 4, moveHead.y + 2, 110, 16), "移行元", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(moveHead.x + 118, moveHead.y + 2, 250, 16), "契機", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(moveHead.x + 372, moveHead.y + 2, 120, 16), "移行先", EditorStyles.miniBoldLabel);
            foreach (var auto in AutoMoves(model)) DrawAutoMove(auto);
            changed |= DrawSymbolMoves(model);
            if (GUILayout.Button("＋ 図柄表示の移行", GUILayout.Width(140)))
            {
                model.Moves.Add(new MoveRow { From = 0, To = Math.Min(1, model.States.Count - 1) });
                changed = true;
            }
            return changed;
        }

        /// <summary>ボーナスの成立・作動・終了とG数消化による移行（設定から決まるので編集しない）。</summary>
        internal struct AutoMove
        {
            public string From, Kind, What, To;
            public int FromState, ToState;
            public int Bonus;
        }

        internal static List<AutoMove> AutoMoves(BonusModel model)
        {
            var list = new List<AutoMove>();
            for (int b = 0; b < model.Bonuses.Count; b++)
            {
                var bonus = model.Bonuses[b];
                if (bonus.WonState >= 0 && bonus.WonState < model.States.Count)
                    list.Add(new AutoMove { From = "すべて", Kind = "ボーナス成立", What = bonus.Label, To = model.States[bonus.WonState].Label, FromState = -1, ToState = bonus.WonState, Bonus = b });
            }
            var starts = new List<string>();
            foreach (var bonus in model.Bonuses) if (bonus.StartRole.Length > 0) starts.Add(bonus.Label);
            if (starts.Count > 0)
                list.Add(new AutoMove { From = "すべて", Kind = "ボーナス作動", What = string.Join("・", starts.ToArray()), To = "各ボーナス作動中", FromState = -1, ToState = -1, Bonus = -1 });
            for (int b = 0; b < model.Bonuses.Count; b++)
            {
                var bonus = model.Bonuses[b];
                if (bonus.StartRole.Length == 0) continue;
                int to = bonus.EndState >= 0 && bonus.EndState < model.States.Count ? bonus.EndState : 0;
                if (model.States.Count == 0) continue;
                list.Add(new AutoMove { From = bonus.Label + "作動中", Kind = "ボーナス終了", What = bonus.Label, To = model.States[to].Label, FromState = -1, ToState = to, Bonus = b });
            }
            for (int s = 1; s < model.States.Count; s++)
                if (model.States[s].GameLimit > 0)
                    list.Add(new AutoMove { From = model.States[s].Label, Kind = "G数消化", What = model.States[s].GameLimit + "G", To = model.States[0].Label, FromState = s, ToState = 0, Bonus = -1 });
            return list;
        }

        private static void TriggerColors(string kind, out Color back, out Color text)
        {
            switch (kind)
            {
                case "ボーナス成立":
                case "ボーナス作動":
                    back = BonusRoleBack; text = BonusRoleText; break;
                case "図柄表示":
                    back = SmallRoleBack; text = SmallRoleText; break;
                case "G数消化":
                    back = TransitionBack; text = TransitionText; break;
                default:
                    back = MutedBack; text = MutedText; break;
            }
        }

        private void DrawAutoMove(AutoMove move)
        {
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            HLine(rect.x, rect.yMax - 1, rect.width);
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = AutoRowText;
            GUI.Label(new Rect(rect.x + 4, rect.y + 3, 110, 16), move.From, style);
            Color back, text;
            TriggerColors(move.Kind, out back, out text);
            var size = ChipSize(move.Kind);
            DrawChip(new Rect(rect.x + 118, rect.y + 3, size.x, 16), move.Kind, back, text);
            GUI.Label(new Rect(rect.x + 122 + size.x, rect.y + 3, 246 - size.x, 16), move.What, style);
            GUI.Label(new Rect(rect.x + 372, rect.y + 3, 120, 16), move.To, style);
        }

        private bool DrawSymbolMoves(BonusModel model)
        {
            bool changed = false;
            var payout = MachinePayoutTable;
            var fromLabels = new List<string> { "すべて" };
            foreach (var s in model.States) fromLabels.Add(s.Label);
            var stateLabels = new List<string>();
            foreach (var s in model.States) stateLabels.Add(s.Label);
            var roleNames = new List<string>();
            var roleLabels = new List<string>();
            if (payout != null)
                for (int i = 0; i < payout.GetWinPairCount(); i++)
                {
                    if (payout.IsBonus(i)) continue;
                    roleNames.Add(payout.winPairNames[i]);
                    roleLabels.Add(RoleLabel(payout, payout.winPairNames[i]));
                }
            var flagNames = new List<string> { "" };
            var flagLabels = new List<string> { "いつでも" };
            if (machine != null)
                foreach (var flag in machine.Flags)
                    if (!flagNames.Contains(flag.BaseName)) { flagNames.Add(flag.BaseName); flagLabels.Add(flag.BaseName + " のときだけ"); }

            for (int i = 0; i < model.Moves.Count; i++)
            {
                var m = model.Moves[i];
                var rect = GUILayoutUtility.GetRect(10, 24, GUILayout.ExpandWidth(true));
                HLine(rect.x, rect.yMax - 1, rect.width);
                float y = rect.y + 3;
                int from = EditorGUI.Popup(new Rect(rect.x + 4, y, 106, 18), m.From + 1, fromLabels.ToArray()) - 1;
                if (from != m.From) { m.From = from; changed = true; }
                Color back, text;
                TriggerColors("図柄表示", out back, out text);
                var size = ChipSize("図柄表示");
                DrawChip(new Rect(rect.x + 118, y + 1, size.x, 16), "図柄表示", back, text);
                float x = rect.x + 122 + size.x;
                int roleAt = roleNames.IndexOf(m.Role);
                var roleList = new List<string>(roleLabels);
                if (roleAt < 0) { roleList.Insert(0, m.Role.Length > 0 ? m.Role : "—"); }
                int shown = roleAt < 0 ? 0 : roleAt;
                int role = EditorGUI.Popup(new Rect(x, y, 96, 18), shown, roleList.ToArray());
                if (role != shown) { m.Role = roleNames[roleAt < 0 ? role - 1 : role]; changed = true; }
                int flagAt = Math.Max(0, flagNames.IndexOf(m.OnlyFlag));
                int flag = EditorGUI.Popup(new Rect(x + 100, y, rect.x + 368 - (x + 100) - 4, 18), flagAt, flagLabels.ToArray());
                if (flag != flagAt) { m.OnlyFlag = flagNames[flag]; changed = true; }
                int to = EditorGUI.Popup(new Rect(rect.x + 372, y, 116, 18), Math.Max(0, m.To), stateLabels.ToArray());
                if (to != m.To) { m.To = to; changed = true; }
                int index = i;
                if (RightClicked(rect))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("削除"), false, delegate { EditBonus("移行の削除", mm => mm.Moves.RemoveAt(index)); });
                    menu.ShowAsContext();
                }
            }
            return changed;
        }
    }
}
