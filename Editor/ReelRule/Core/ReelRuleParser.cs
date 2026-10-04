using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>
    /// 制御ルールファイル（行単位のテキスト）を読む。書式は Docs/ReelRule.md を参照。
    /// </summary>
    public static class ReelRuleParser
    {
        private static readonly char[] Spaces = { ' ', '\t', '　' };

        public static RuleSet Parse(string text)
        {
            var errors = new List<RuleError>();
            var rules = new RuleSet();
            ControlRule control = null;
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string[] tokens = Tokenize(lines[i]);
                if (tokens.Length == 0) continue;
                string head = tokens[0];

                if (Is(head, "停止形", "shape"))
                {
                    control = null;
                    if (tokens.Length < 2) { errors.Add(new RuleError(lineNo, "停止形の名前がありません")); continue; }
                    var shape = new Shape { Name = tokens[1], Line = lineNo };
                    for (int t = 2; t < tokens.Length; t++)
                    {
                        if (Is(tokens[t], "専用", "exclusive")) shape.Exclusive = true;
                        else errors.Add(new RuleError(lineNo, "不明な指定: " + tokens[t]));
                    }
                    if (rules.FindShape(shape.Name) != null) errors.Add(new RuleError(lineNo, "停止形の名前が重複しています: " + shape.Name));
                    int row = 0;
                    while (row < 3 && i + 1 < lines.Length)
                    {
                        string[] cells = Tokenize(lines[i + 1]);
                        i++;
                        if (cells.Length == 0) continue;
                        if (cells.Length != 3)
                        {
                            errors.Add(new RuleError(i + 1, "停止形の行は「左 中 右」の3マスで書いてください"));
                        }
                        else
                        {
                            for (int reel = 0; reel < 3; reel++) shape.Cells[row * 3 + reel] = ParseCell(cells[reel]);
                        }
                        row++;
                    }
                    if (row < 3) errors.Add(new RuleError(lineNo, "停止形は上段・中段・下段の3行が必要です"));
                    rules.Shapes.Add(shape);
                    continue;
                }

                if (Is(head, "フラグ", "制御", "flag", "control"))
                {
                    if (tokens.Length != 2) { errors.Add(new RuleError(lineNo, "フラグ名を1つ書いてください")); control = null; continue; }
                    if (rules.FindControl(tokens[1]) != null) errors.Add(new RuleError(lineNo, "フラグが重複しています: " + tokens[1]));
                    control = new ControlRule { Name = tokens[1], Explicit = true, Line = lineNo };
                    rules.Controls.Add(control);
                    continue;
                }

                if (Is(head, "最大滑り", "maxslip"))
                {
                    int value;
                    if (tokens.Length != 2 || !int.TryParse(tokens[1], out value) || value < 0 || value > 4)
                        errors.Add(new RuleError(lineNo, "最大滑りは0〜4で指定してください"));
                    else rules.MaxSlip = value;
                    continue;
                }

                if (Is(head, "BET", "bet", "ベット"))
                {
                    int value;
                    if (tokens.Length != 2 || !int.TryParse(tokens[1], out value) || value < 1 || value > 3)
                        errors.Add(new RuleError(lineNo, "BETは1〜3で指定してください"));
                    else rules.Bet = value;
                    continue;
                }

                if (control == null)
                {
                    errors.Add(new RuleError(lineNo, "「" + head + "」はフラグの中で書いてください"));
                    continue;
                }

                if (Is(head, "抽選", "lottery"))
                {
                    if (tokens.Length != 2) errors.Add(new RuleError(lineNo, "抽選フラグ名を1つ書いてください"));
                    else control.LotteryFlag = tokens[1];
                }
                else if (Is(head, "成立", "won"))
                {
                    if (tokens.Length < 2) errors.Add(new RuleError(lineNo, "役名がありません"));
                    for (int t = 1; t < tokens.Length; t++) control.ExtraRoles.Add(tokens[t]);
                }
                else if (Is(head, "引込", "pull"))
                {
                    if (tokens.Length == 2 && Is(tokens[1], "なし", "none")) { control.NoImplicitPull = true; continue; }
                    AddGoals(control, GoalKind.Pull, tokens, lineNo, errors);
                }
                else if (Is(head, "狙う", "prefer")) AddGoals(control, GoalKind.Prefer, tokens, lineNo, errors);
                else if (Is(head, "避ける", "avoid")) AddGoals(control, GoalKind.Avoid, tokens, lineNo, errors);
                else if (Is(head, "禁止", "deny"))
                {
                    if (tokens.Length < 2) errors.Add(new RuleError(lineNo, "停止形の名前がありません"));
                    for (int t = 1; t < tokens.Length; t++) control.Denied.Add(tokens[t]);
                }
                else if (Is(head, "許可", "allow"))
                {
                    if (tokens.Length < 2) errors.Add(new RuleError(lineNo, "停止形の名前がありません"));
                    for (int t = 1; t < tokens.Length; t++) control.Allowed.Add(tokens[t]);
                }
                else errors.Add(new RuleError(lineNo, "不明な命令: " + head));
            }

            if (errors.Count > 0) throw new RuleException(errors);
            return rules;
        }

        private static void AddGoals(ControlRule control, GoalKind kind, string[] tokens, int lineNo, List<RuleError> errors)
        {
            if (tokens.Length < 2) errors.Add(new RuleError(lineNo, "対象の名前がありません"));
            for (int t = 1; t < tokens.Length; t++)
                control.Goals.Add(new Goal { Kind = kind, Target = tokens[t], Line = lineNo });
        }

        private static ShapeCell ParseCell(string token)
        {
            var cell = new ShapeCell();
            if (token == "*" || token == "＊") return cell;
            if (token.StartsWith("!") || token.StartsWith("！"))
            {
                cell.Negate = true;
                token = token.Substring(1);
            }
            foreach (string part in token.Split('|', '｜'))
                if (part.Length > 0 && !cell.Options.Contains(part)) cell.Options.Add(part);
            if (cell.Options.Count == 0) cell.Negate = false;
            return cell;
        }

        private static string[] Tokenize(string line)
        {
            int comment = line.IndexOf('#');
            if (comment < 0) comment = line.IndexOf('＃');
            if (comment >= 0) line = line.Substring(0, comment);
            return line.Split(Spaces, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool Is(string token, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(token, names[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
