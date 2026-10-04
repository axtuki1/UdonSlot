using System.Collections.Generic;
using System.Text;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>生成結果を Markdown の文章にする。Codex 等へそのまま渡せる形にする。</summary>
    public static class ReelRuleReport
    {
        public static string Percent(long part, long whole)
        {
            if (whole <= 0) return "-";
            return (100.0 * part / whole).ToString("0.0") + "%";
        }

        public static string Markdown(List<ControlResult> results, int windowLimit)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# リール制御 生成レポート");
            sb.AppendLine();
            sb.AppendLine("全押し順・全押下位置を等確率で押したときの割合です。");
            sb.AppendLine();
            sb.AppendLine("| フラグ | 当選役の入賞率 | 非成立入賞 | 回避不能 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var r in results)
            {
                sb.Append("| ").Append(r.Name).Append(" | ").Append(WonSummary(r)).Append(" | ")
                  .Append(r.Stats.ViolationPaths).Append(" | ").Append(r.Stats.UnavoidableCells).AppendLine(" |");
            }
            foreach (var r in results)
            {
                sb.AppendLine();
                sb.Append("## ").AppendLine(r.Name);
                if (r.LotteryFlag != r.Name) sb.Append("抽選フラグ: ").AppendLine(r.LotteryFlag).AppendLine();
                sb.Append("優先順位: ").AppendLine(r.GoalLines.Count == 0 ? "（なし）" : string.Join(" → ", r.GoalLines.ToArray()));
                sb.AppendLine();
                sb.Append("| 項目 | 全体 |");
                foreach (string order in ReelRuleEngine.OrderNames) sb.Append(' ').Append(order).Append(" |");
                sb.AppendLine();
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                var s = r.Stats;
                for (int i = 0; i < s.RoleNames.Length; i++)
                {
                    if (s.RoleFormed[i] == 0 && !s.RoleWon[i]) continue;
                    sb.Append("| ").Append(s.RoleWon[i] ? "入賞 " : "**非成立入賞** ").Append(s.RoleNames[i]).Append(" | ").Append(Percent(s.RoleFormed[i], s.Paths)).Append(" |");
                    for (int o = 0; o < 6; o++) sb.Append(' ').Append(Percent(s.RoleFormedByOrder[i, o], s.PathsByOrder[o])).Append(" |");
                    sb.AppendLine();
                }
                for (int k = 0; k < s.ShapeNames.Count; k++)
                {
                    sb.Append("| 停止形 ").Append(s.ShapeNames[k]).Append(" | ").Append(Percent(s.ShapeMatched[k], s.Paths)).Append(" |");
                    for (int o = 0; o < 6; o++) sb.Append(' ').Append(Percent(s.ShapeMatchedByOrder[k, o], s.PathsByOrder[o])).Append(" |");
                    sb.AppendLine();
                }
                if (s.UnavoidableCells > 0)
                    sb.AppendLine().Append("回避不能な押下位置: ").Append(s.UnavoidableCells).AppendLine("件（最大滑りでは非成立役・禁止停止形を避けきれない）");
                sb.AppendLine();
                sb.Append("出目（多い順 ").Append(windowLimit).Append("件、全").Append(s.Windows.Count).AppendLine("種類）");
                sb.AppendLine();
                sb.AppendLine("| 割合 | 上段 | 中段 | 下段 |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var w in s.TopWindows(windowLimit))
                {
                    string[] cells = w.Key.Split('\t');
                    sb.Append("| ").Append(Percent(w.Value, s.Paths));
                    for (int row = 0; row < 3; row++)
                        sb.Append(" | ").Append(cells[row * 3]).Append(' ').Append(cells[row * 3 + 1]).Append(' ').Append(cells[row * 3 + 2]);
                    sb.AppendLine(" |");
                }
            }
            return sb.ToString();
        }

        public static string WonSummary(ControlResult r)
        {
            var parts = new List<string>();
            var s = r.Stats;
            for (int i = 0; i < s.RoleNames.Length; i++)
                if (s.RoleWon[i]) parts.Add(s.RoleNames[i] + " " + Percent(s.RoleFormed[i], s.Paths));
            return parts.Count == 0 ? "（当選役なし）" : string.Join(" / ", parts.ToArray());
        }
    }
}
