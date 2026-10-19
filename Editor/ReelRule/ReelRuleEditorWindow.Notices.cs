using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ⚠ 警告・通知の一覧
    public sealed partial class ReelRuleEditorWindow
    {
        private static readonly Color WarningBack = new Color(0.35f, 0.23f, 0.08f);
        private static readonly Color WarningText = new Color(1f, 0.81f, 0.48f);

        internal sealed class Notice
        {
            public bool Warning;
            public string Text;
            public int Screen;
            public string Control;
        }

        private List<Notice> CollectNotices()
        {
            var list = new List<Notice>();
            if (machineObject == null) return list;
            var bonusTable = MachineBonusTable;
            var regulation = asset != null ? Design.regulation : Regulation.Gen6;

            if (bonusTable != null)
            {
                var model = BonusModel.Load(bonusTable);
                foreach (var b in model.Bonuses)
                {
                    if (b.StartRole.Length > 0 && asset != null && machine != null && !HasControlFor(b.StartRole))
                        list.Add(new Notice { Warning = true, Text = b.Label + " を揃える制御がありません", Screen = 4 });
                    string over = RegulationProblem(b, regulation);
                    if (over != null) list.Add(new Notice { Warning = true, Text = b.Label + ": " + over, Screen = 4 });
                    if ((b.Kind == BonusKind.MB || b.Kind == BonusKind.CB) && b.Lottery != null && b.Lottery != NormalLottery(model))
                        list.Add(new Notice { Warning = true, Text = b.Label + ": 作動中の抽選表が通常時と違います", Screen = 4 });
                    if (b.ManualChild && b.Child >= 0 && b.Child < model.Bonuses.Count)
                    {
                        var child = model.Bonuses[b.Child];
                        if (child.StartRole.Length == 0)
                            list.Add(new Notice { Warning = true, Text = child.Label + ": 作動役がありません（" + b.Label + "の手動JACイン）", Screen = 4 });
                        if (!LotteryHasBonus(b.Lottery, child.Name))
                            list.Add(new Notice { Warning = true, Text = b.Label + ": 作動中の抽選表に" + child.Label + "の行がありません", Screen = 4 });
                    }
                }
            }
            if (results != null)
                foreach (var r in results)
                    if (r.Stats != null && r.Stats.ViolationPaths > 0)
                        list.Add(new Notice { Warning = true, Text = r.Name + ": 非成立役の入賞 " + r.Stats.ViolationPaths, Screen = 0, Control = r.Name });
            if (asset != null && machine != null) CollectMapNotices(list);
            if (asset != null && registeredNames != null)
                foreach (var c in Design.controls)
                    if (!IsRegistered(c.name) && !(machine != null && Design.IsUnusedCell(c, machine)))
                        list.Add(new Notice { Text = c.name + " はベイク時に作成されます", Screen = 0, Control = c.name });
            return list;
        }

        /// <summary>割り当て表に入っていない制御。</summary>
        private void CollectMapNotices(List<Notice> list)
        {
            foreach (var c in Design.controls)
                if (!c.IsCell)
                    list.Add(new Notice { Warning = true, Text = c.name + " は割り当て表に入っていません", Screen = 0, Control = c.name });
        }

        private static bool LotteryHasBonus(LotteryTable table, string bonus)
        {
            if (table == null || table.entryBonusNames == null) return false;
            foreach (string name in table.entryBonusNames) if (name == bonus) return true;
            return false;
        }

        /// <summary>通常時の抽選表。</summary>
        private LotteryTable NormalLottery(BonusModel model)
        {
            if (model.States.Count > 0 && model.States[0].Lottery != null) return model.States[0].Lottery;
            return ReelMachineReader.LotteryTableOf(machineObject);
        }

        /// <summary>この役を当選として扱う制御があるか。</summary>
        private bool HasControlFor(string role)
        {
            foreach (var c in Design.controls)
                if (!Design.IsUnusedCell(c, machine) && WonRolesOf(c).Contains(role)) return true;
            return false;
        }

        private static string RegulationProblem(BonusRow b, Regulation regulation)
        {
            if (b.Kind == BonusKind.CB && b.QuickReels == 0) return "すぐ止まるリールがありません";
            switch (b.Kind)
            {
                case BonusKind.BB:
                case BonusKind.MB:
                    int limit = b.Kind == BonusKind.BB ? RegulationLimits.BigPayout(regulation) : RegulationLimits.MiddlePayout(regulation);
                    if (b.EndPayout <= 0 && b.EndGames <= 0) return "終了条件がありません";
                    if (b.EndPayout > limit) return "払出 " + b.EndPayout + "枚が規格（" + limit + "以下）を超えています";
                    break;
                case BonusKind.RB:
                    if (b.EndWins <= 0 && b.EndGames <= 0) return "終了条件がありません";
                    if (b.EndWins > RegulationLimits.RegularWins) return "入賞 " + b.EndWins + "回が規格（" + RegulationLimits.RegularWins + "以下）を超えています";
                    if (b.EndGames > RegulationLimits.RegularGames) return "遊技 " + b.EndGames + "Gが規格（" + RegulationLimits.RegularGames + "以下）を超えています";
                    break;
            }
            return null;
        }

        private void DrawNoticeButton()
        {
            var notices = CollectNotices();
            int warnings = 0;
            foreach (var n in notices) if (n.Warning) warnings++;
            var content = new GUIContent(warnings > 0 ? "⚠ " + warnings : "⚠", notices.Count + " 件");
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.toolbarButton, GUILayout.Width(40));
            var color = GUI.contentColor;
            if (warnings > 0) GUI.contentColor = WarningText;
            if (GUI.Button(rect, content, EditorStyles.toolbarButton))
                PopupWindow.Show(rect, new NoticePopup(notices, Go));
            GUI.contentColor = color;
        }

        private void Go(Notice notice)
        {
            screen = notice.Screen;
            if (notice.Control != null && asset != null)
            {
                int index = Design.controls.FindIndex(c => c.name == notice.Control);
                if (index >= 0) SelectControl(index);
            }
            GUI.FocusControl(null);
            Repaint();
        }

        private sealed class NoticePopup : PopupWindowContent
        {
            private readonly List<Notice> notices;
            private readonly Action<Notice> go;
            private Vector2 scroll;

            public NoticePopup(List<Notice> notices, Action<Notice> go)
            {
                this.notices = notices;
                this.go = go;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(420, Mathf.Clamp(notices.Count * 24 + 12, 40, 400));
            }

            public override void OnGUI(Rect rect)
            {
                scroll = GUILayout.BeginScrollView(scroll);
                if (notices.Count == 0) GUILayout.Label("—", MutedLabel);
                foreach (var n in notices)
                {
                    using (new EditorGUILayout.HorizontalScope(GUILayout.Height(22)))
                    {
                        if (n.Warning) Chip("警告", WarningBack, WarningText);
                        else Chip("通知", MutedBack, MutedText);
                        GUILayout.Label(n.Text, EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(ScreenNames[n.Screen], EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            go(n);
                            editorWindow.Close();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                GUILayout.EndScrollView();
            }
        }
    }
}
