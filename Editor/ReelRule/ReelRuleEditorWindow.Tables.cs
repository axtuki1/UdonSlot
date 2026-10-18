using System;
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ③〜⑤ で共有する、機種の表の取得・作成と配色。
    public sealed partial class ReelRuleEditorWindow
    {
        private static readonly Color SmallRoleBack = new Color(0.14f, 0.29f, 0.19f);
        private static readonly Color SmallRoleText = new Color(0.62f, 0.88f, 0.69f);
        private static readonly Color BonusRoleBack = new Color(0.36f, 0.12f, 0.16f);
        private static readonly Color BonusRoleText = new Color(0.96f, 0.65f, 0.71f);
        private static readonly Color RegularBack = new Color(0.12f, 0.23f, 0.36f);
        private static readonly Color RegularText = new Color(0.65f, 0.80f, 0.96f);
        private static readonly Color TransitionBack = new Color(0.29f, 0.23f, 0.11f);
        private static readonly Color TransitionText = new Color(0.94f, 0.77f, 0.42f);
        private static readonly Color MutedBack = new Color(0.23f, 0.23f, 0.23f);
        private static readonly Color MutedText = new Color(0.72f, 0.72f, 0.72f);
        private static readonly Color SubRowColor = new Color(0.17f, 0.17f, 0.17f);
        private static readonly Color GridLineColor = new Color(0.13f, 0.13f, 0.13f);

        private PayoutTable MachinePayoutTable { get { return ReelMachineReader.PayoutTableOf(machineObject); } }
        private BonusTable MachineBonusTable { get { return ReelMachineReader.BonusTableOf(machineObject); } }

        /// <summary>表（抽選表・配当表・ボーナス表）を使っている機種を探して、該当するタブを開く。</summary>
        public static void OpenFor(UnityEngine.Object table)
        {
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ReelOutputController>(true))
            {
                int tab = -1, lotteryIndex = -1;
                var lotteries = ReelMachineReader.LotteryTablesOf(candidate);
                if (table is LotteryTable && lotteries.Contains((LotteryTable)table)) { tab = 2; lotteryIndex = lotteries.IndexOf((LotteryTable)table); }
                else if (table is PayoutTable && ReelMachineReader.PayoutTableOf(candidate) == table) tab = 3;
                else if (table is BonusTable && ReelMachineReader.BonusTableOf(candidate) == table) tab = 4;
                if (tab < 0) continue;
                var window = GetWindow<ReelRuleEditorWindow>("機種エディタ");
                window.machineObject = candidate;
                window.SavePrefs();
                window.screen = tab;
                if (lotteryIndex >= 0) window.lotteryTableIndex = lotteryIndex;
                window.requested = null;
                window.Changed();
                window.Repaint();
                return;
            }
            GetWindow<ReelRuleEditorWindow>("機種エディタ");
        }

        internal BonusTable FlowBonusTable { get { return machineObject != null ? MachineBonusTable : null; } }
        internal PayoutTable FlowPayoutTable { get { return machineObject != null ? MachinePayoutTable : null; } }
        internal List<FlowNodePosition> FlowPositions { get { return asset != null ? Design.flowNodes : null; } }

        /// <summary>フロー図のノード位置を設計に保存する（Undo には積まない）。</summary>
        internal void SetFlowPositions(List<FlowNodePosition> list)
        {
            if (asset == null) return;
            Design.flowNodes = list;
            EditorUtility.SetDirty(asset);
        }

        private static string TableLabel(UnityEngine.Object table)
        {
            return table == null ? "—" : table.name;
        }

        /// <summary>役の種別に合わせたチップの色。</summary>
        private static void RoleColors(PayoutTable payout, string role, out Color back, out Color text)
        {
            if (payout != null && payout.IsTransition(role)) { back = TransitionBack; text = TransitionText; }
            else if (payout != null && payout.IsBonus(role)) { back = BonusRoleBack; text = BonusRoleText; }
            else { back = SmallRoleBack; text = SmallRoleText; }
        }

        private static void BonusKindColors(BonusKind kind, out Color back, out Color text)
        {
            if (kind == BonusKind.BB || kind == BonusKind.MB) { back = BonusRoleBack; text = BonusRoleText; }
            else { back = RegularBack; text = RegularText; }
        }

        private static string RoleLabel(PayoutTable payout, string role)
        {
            return payout != null ? payout.GetWinPairDisplayName(role) : role;
        }

        /// <summary>ボーナス付きフラグの内部名。制御名の形式から作る（例 Cherry_RedBB）。</summary>
        private string BonusEntryName(string baseFlag, string bonus)
        {
            var property = new SerializedObject(machineObject).FindProperty("controlNameFormat");
            string format = property != null ? property.stringValue : "";
            if (string.IsNullOrEmpty(format) || format.IndexOf("{bonus}", StringComparison.Ordinal) < 0) format = "{flag}_{bonus}";
            string name = format.Replace("{flag}", baseFlag).Replace("{bonus}", bonus).Replace("{rt}", "");
            while (name.Contains("__")) name = name.Replace("__", "_");
            return name.Trim('_');
        }

        /// <summary>機種の子に抽選表を作る。</summary>
        private LotteryTable CreateLotteryTable(string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "抽選表の作成");
            Undo.SetTransformParent(go.transform, machineObject.transform, "抽選表の作成");
            go.transform.localPosition = Vector3.zero;
            var table = UdonSharpUndo.AddComponent<LotteryTable>(go);
            var so = new SerializedObject(table);
            so.FindProperty("winpairNames").arraySize = 1;
            so.FindProperty("winpairNames").GetArrayElementAtIndex(0).stringValue = "NoWin";
            so.FindProperty("winpairProbabilities").arraySize = 1;
            so.FindProperty("isRemainderFlags").arraySize = 1;
            so.FindProperty("isRemainderFlags").GetArrayElementAtIndex(0).boolValue = true;
            so.ApplyModifiedProperties();
            var main = ReelMachineReader.LotteryTableOf(machineObject);
            if (main != null)
            {
                // 設定の数と名前は通常の抽選表に合わせる
                var source = LotteryModel.Load(main);
                var created = LotteryModel.Load(table);
                created.Settings = new List<string>(source.Settings);
                created.Denominator = source.Denominator;
                created.SetSettingCount(source.SettingCount);
                created.Save("抽選表の作成");
            }
            return table;
        }

        /// <summary>機種の子にボーナス・RT表を作り、機種に設定する。状態0は通常の抽選表を使う。</summary>
        private BonusTable CreateBonusTable()
        {
            var go = new GameObject("BonusTable");
            Undo.RegisterCreatedObjectUndo(go, "ボーナス・RT表の作成");
            Undo.SetTransformParent(go.transform, machineObject.transform, "ボーナス・RT表の作成");
            go.transform.localPosition = Vector3.zero;
            var table = UdonSharpUndo.AddComponent<BonusTable>(go);
            var model = BonusModel.Load(table);
            model.States.Add(new StateRow { Name = "Normal", Display = "通常", Lottery = ReelMachineReader.LotteryTableOf(machineObject) });
            model.Save("ボーナス・RT表の作成");
            var so = new SerializedObject(machineObject);
            so.FindProperty("bonusTable").objectReferenceValue = table;
            so.ApplyModifiedProperties();
            return table;
        }

        /// <summary>行の右クリック。</summary>
        private static bool RightClicked(Rect rect)
        {
            return Clicked(rect, 1);
        }

        private static void HLine(float x, float y, float width)
        {
            EditorGUI.DrawRect(new Rect(x, y, width, 1), GridLineColor);
        }

        private static GUIStyle mutedLabel;
        private static GUIStyle MutedLabel
        {
            get
            {
                if (mutedLabel == null)
                {
                    mutedLabel = new GUIStyle(EditorStyles.miniLabel);
                    mutedLabel.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
                }
                return mutedLabel;
            }
        }

        private static GUIStyle rightMini;
        private static GUIStyle RightMini
        {
            get
            {
                if (rightMini == null) rightMini = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                return rightMini;
            }
        }

        /// <summary>チップを並べる。入りきらない分は「+N」。</summary>
        private static void DrawChips(Rect rect, List<string> texts, List<Color> backs, List<Color> fronts, int max)
        {
            float x = rect.x;
            int shown = Math.Min(texts.Count, max);
            for (int i = 0; i < shown; i++)
            {
                var size = ChipSize(texts[i]);
                float width = Math.Min(size.x, rect.xMax - x - 28);
                if (width < 24) { shown = i; break; }
                DrawChip(new Rect(x, rect.y + (rect.height - 16) / 2, width, 16), texts[i], backs[i], fronts[i]);
                x += width + 4;
            }
            if (texts.Count > shown)
            {
                string more = "+" + (texts.Count - shown);
                var size = ChipSize(more);
                DrawChip(new Rect(x, rect.y + (rect.height - 16) / 2, size.x, 16), more, MutedBack, MutedText);
            }
        }

        private static string Probability(float denominator, float value)
        {
            return value > 0 ? "1/" + (denominator / value).ToString("0.0") : "—";
        }
    }
}
