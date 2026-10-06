using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>生成した滑りテーブル1枚。ReelControlData の基本テーブル／条件付きテーブル1件に対応する。</summary>
    public sealed class SlipTable
    {
        /// <summary>ReelControlData.GetIndexForReelStopPattern と同じ停止パターン値。</summary>
        public int Pattern;
        public int Reel;
        /// <summary>0=第1停止、1=第2停止、2=第3停止。</summary>
        public int Stage;
        /// <summary>停止済みリールの最終下段インデックス。未停止は -1。</summary>
        public int[] Prior = { -1, -1, -1 };
        /// <summary>押下位置（滑り前の下段インデックス）ごとの滑りコマ数。</summary>
        public int[] Slips;
        /// <summary>非成立役・禁止停止形を避けられなかった押下位置。</summary>
        public bool[] Unavoidable;
        /// <summary>押下位置ごとの決め手（「スイカ引込」「回避」「最短」「手動」など）。</summary>
        public string[] Reasons;
        /// <summary>手動指定で決めた押下位置。</summary>
        public bool[] Manual;
    }

    /// <summary>1つの検索名（フラグ）に対する生成結果。</summary>
    public sealed class ControlResult
    {
        public string Name = "";
        public string LotteryFlag = "";
        public List<string> WonRoles = new List<string>();
        public List<string> GoalLines = new List<string>();
        public List<SlipTable> Tables = new List<SlipTable>();
        public ControlStats Stats;
    }

    /// <summary>
    /// 優先順位方式の生成エンジン。各押下位置で 0〜最大滑りの候補を比べ、
    /// ①非成立役・禁止停止形を最後まで避けられる候補 ②ルールの優先順位（引込・狙う・避ける）③滑りが少ない
    /// の順で1つに決める。
    /// </summary>
    public sealed class ReelRuleEngine
    {
        public static readonly int[][] Orders =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };
        public static readonly string[] OrderNames = { "左中右", "左右中", "中左右", "中右左", "右左中", "右中左" };
        public static readonly string[] ReelNames = { "左", "中", "右" };

        private sealed class Plan
        {
            public ControlRule Rule;
            public bool[] Won;
            public List<int> Denied = new List<int>();
            public List<GoalKind> Kinds = new List<GoalKind>();
            public List<int> Targets = new List<int>();
            public List<string> Labels = new List<string>();
            public List<string> Reasons = new List<string>();
        }

        private readonly Machine machine;
        private readonly RuleSet rules;
        private readonly int maxSlip;
        private readonly int[] lengths = new int[3];
        private readonly List<PayLine> activeLines = new List<PayLine>();
        // roleCell[role][reel][pos*3+row]: その位置で役の図柄条件を満たすか
        private bool[][][] roleCell;
        private bool[][] roleReelAny;
        // roleRowExists[role][reel][row]: そのリールのどこかで条件を満たせるか
        private bool[][][] roleRowExists;
        // shapeReel[shape][reel][pos]: そのリールの3コマが停止形に合うか
        private bool[][][] shapeReel;
        private bool[][] shapeReelPossible;

        private Plan plan;
        private readonly Dictionary<long, bool> safeMemo = new Dictionary<long, bool>();
        private readonly Dictionary<long, double> chanceMemo = new Dictionary<long, double>();
        private readonly Dictionary<long, SlipTable> tables = new Dictionary<long, SlipTable>();
        private Func<bool> cancelled;
        private readonly Dictionary<ControlResult, Dictionary<long, SlipTable>> resultIndex = new Dictionary<ControlResult, Dictionary<long, SlipTable>>();

        public ReelRuleEngine(Machine machine, RuleSet rules)
        {
            this.machine = machine;
            this.rules = rules;
            maxSlip = rules.MaxSlip >= 0 ? rules.MaxSlip : machine.MaxSlip;
            if (maxSlip < 0 || maxSlip > 4) maxSlip = 4;
        }

        public int MaxSlip { get { return maxSlip; } }

        /// <summary>全フラグを生成する。ルールや機種データの誤りは RuleException で返す。</summary>
        public List<ControlResult> GenerateAll(Func<bool> cancel = null)
        {
            cancelled = cancel;
            var errors = new List<RuleError>();
            ValidateMachine(errors);
            if (errors.Count > 0) throw new RuleException(errors);
            Precompute();
            var plans = BuildPlans(errors);
            if (errors.Count > 0) throw new RuleException(errors);

            var results = new List<ControlResult>();
            foreach (var p in plans)
            {
                plan = p;
                safeMemo.Clear();
                chanceMemo.Clear();
                tables.Clear();
                Expand(new[] { -1, -1, -1 }, new[] { 0, 0, 0 }, 0, 0);
                var result = new ControlResult { Name = p.Rule.Name, LotteryFlag = p.Rule.LotteryFlags != null && p.Rule.LotteryFlags.Count > 0 ? string.Join(",", p.Rule.LotteryFlags.ToArray()) : p.Rule.SourceFlag };
                for (int i = 0; i < machine.Roles.Count; i++) if (p.Won[i]) result.WonRoles.Add(machine.Roles[i].Name);
                result.GoalLines.AddRange(p.Labels);
                result.Tables.AddRange(tables.Values);
                result.Tables.Sort(CompareTables);
                result.Stats = Simulate(p, result);
                results.Add(result);
            }
            return results;
        }

        public static long Key(int pattern, int[] prior)
        {
            return ((long)pattern << 36) | ((long)(prior[0] + 1) << 24) | ((long)(prior[1] + 1) << 12) | (long)(prior[2] + 1);
        }

        public static int PatternFor(int order, int stage)
        {
            int pattern = 0;
            for (int s = 0; s <= stage; s++) pattern |= (s + 1) << ((2 - Orders[order][s]) * 2);
            return pattern;
        }

        private static int CompareTables(SlipTable a, SlipTable b)
        {
            int c = a.Stage.CompareTo(b.Stage);
            if (c != 0) return c;
            c = a.Pattern.CompareTo(b.Pattern);
            if (c != 0) return c;
            for (int r = 0; r < 3; r++)
            {
                c = a.Prior[r].CompareTo(b.Prior[r]);
                if (c != 0) return c;
            }
            return 0;
        }

        // ---------------------------------------------------------------- 準備

        private void ValidateMachine(List<RuleError> errors)
        {
            if (machine.Strips == null || machine.Strips.Length != 3)
            {
                errors.Add(new RuleError(0, "リールが3本ではありません"));
                return;
            }
            for (int r = 0; r < 3; r++)
            {
                if (machine.Strips[r] == null || machine.Strips[r].Length < 3 || machine.Strips[r].Length > 1000)
                    errors.Add(new RuleError(0, ReelNames[r] + "リールの配列が不正です"));
                else lengths[r] = machine.Strips[r].Length;
            }
            int mask = 1 << (rules.Bet - 1);
            foreach (var line in machine.Lines)
            {
                bool ok = line.Rows != null && line.Rows.Length == 3;
                for (int r = 0; ok && r < 3; r++) ok = line.Rows[r] >= 0 && line.Rows[r] <= 2;
                if (!ok) errors.Add(new RuleError(0, "有効ラインの段指定が不正です"));
                else if ((line.BetMask & mask) != 0) activeLines.Add(line);
            }
            if (activeLines.Count == 0) errors.Add(new RuleError(0, rules.Bet + "BETで有効なラインがありません"));
            foreach (var role in machine.Roles)
            {
                bool ok = role.StopOrder == null || role.StopOrder.Length == 3;
                for (int r = 0; ok && role.StopOrder != null && r < 3; r++) ok = role.StopOrder[r] >= 0 && role.StopOrder[r] <= 3;
                if (!ok) errors.Add(new RuleError(0, "役「" + role.Name + "」の停止順指定が不正です"));
            }
        }

        private void Precompute()
        {
            int roleCount = machine.Roles.Count;
            roleCell = new bool[roleCount][][];
            roleReelAny = new bool[roleCount][];
            roleRowExists = new bool[roleCount][][];
            for (int i = 0; i < roleCount; i++)
            {
                var role = machine.Roles[i];
                roleCell[i] = new bool[3][];
                roleReelAny[i] = new bool[3];
                roleRowExists[i] = new bool[3][];
                for (int r = 0; r < 3; r++)
                {
                    string[] options = role.Symbols != null && role.Symbols.Length > r && role.Symbols[r] != null ? role.Symbols[r] : new string[0];
                    roleReelAny[i][r] = options.Length == 0;
                    roleCell[i][r] = new bool[lengths[r] * 3];
                    roleRowExists[i][r] = new bool[3];
                    for (int pos = 0; pos < lengths[r]; pos++)
                        for (int row = 0; row < 3; row++)
                        {
                            bool match = options.Length == 0 || Array.IndexOf(options, machine.SymbolAt(r, pos, row)) >= 0;
                            roleCell[i][r][pos * 3 + row] = match;
                            if (match) roleRowExists[i][r][row] = true;
                        }
                }
            }

            int shapeCount = rules.Shapes.Count;
            shapeReel = new bool[shapeCount][][];
            shapeReelPossible = new bool[shapeCount][];
            for (int s = 0; s < shapeCount; s++)
            {
                var shape = rules.Shapes[s];
                shapeReel[s] = new bool[3][];
                shapeReelPossible[s] = new bool[3];
                for (int r = 0; r < 3; r++)
                {
                    shapeReel[s][r] = new bool[lengths[r]];
                    int[] allowed = shape.Positions != null && shape.Positions.Length > r ? shape.Positions[r] : null;
                    for (int pos = 0; pos < lengths[r]; pos++)
                    {
                        bool match = allowed == null || Array.IndexOf(allowed, pos) >= 0;
                        for (int row = 0; row < 3 && match; row++) match = shape.Cell(row, r).Matches(machine.SymbolAt(r, pos, row));
                        shapeReel[s][r][pos] = match;
                        if (match) shapeReelPossible[s][r] = true;
                    }
                }
            }
        }

        private List<Plan> BuildPlans(List<RuleError> errors)
        {
            foreach (var shape in rules.Shapes)
                for (int c = 0; c < 9; c++)
                {
                    var symbols = new List<string>(shape.Cells[c].Include);
                    symbols.AddRange(shape.Cells[c].Exclude);
                    foreach (string symbol in symbols)
                        if (!machine.HasSymbol(symbol))
                            errors.Add(new RuleError(shape.Line, "停止形「" + shape.Name + "」の図柄がリール配列にありません: " + symbol));
                }

            // 抽選表の全フラグ＋ルールファイルで追加した検索名
            var controls = new List<ControlRule>();
            if (!rules.OnlyListedControls)
                foreach (var flag in machine.Flags)
                {
                    var rule = rules.FindControl(flag.Name);
                    controls.Add(rule ?? new ControlRule { Name = flag.Name });
                }
            foreach (var rule in rules.Controls)
                if (!controls.Contains(rule)) controls.Add(rule);

            var plans = new List<Plan>();
            foreach (var rule in controls)
            {
                var p = new Plan { Rule = rule, Won = new bool[machine.Roles.Count] };
                var names = new List<string>();
                bool missingFlag = false;
                foreach (string flagName in rule.LotteryFlags ?? new List<string> { rule.SourceFlag })
                {
                    var lottery = machine.FindFlag(flagName);
                    if (lottery == null)
                    {
                        errors.Add(new RuleError(rule.Line, "抽選表にフラグ「" + flagName + "」がありません（制御 " + rule.Name + "）"));
                        missingFlag = true;
                        continue;
                    }
                    names.AddRange(lottery.Roles);
                }
                if (missingFlag) continue;
                var wonOrder = new List<int>();
                names.AddRange(rule.ExtraRoles);
                foreach (string name in names)
                {
                    int index = RoleIndex(name);
                    if (index < 0) { errors.Add(new RuleError(rule.Line, "配当表に役「" + name + "」がありません（フラグ " + rule.Name + "）")); continue; }
                    if (!p.Won[index]) wonOrder.Add(index);
                    p.Won[index] = true;
                }

                foreach (var goal in rule.Goals)
                {
                    if (goal.Kind == GoalKind.Pull)
                    {
                        int index = RoleIndex(goal.Target);
                        if (index < 0) errors.Add(new RuleError(goal.Line, "配当表に役「" + goal.Target + "」がありません"));
                        else if (!p.Won[index]) errors.Add(new RuleError(goal.Line, "「" + goal.Target + "」はフラグ " + rule.Name + " の当選役ではないため引き込めません"));
                        else AddGoal(p, GoalKind.Pull, index, "引込 " + goal.Target, goal.Target + "引込");
                    }
                    else if (goal.Kind == GoalKind.AvoidRole)
                    {
                        int index = RoleIndex(goal.Target);
                        if (index < 0) errors.Add(new RuleError(goal.Line, "配当表に役「" + goal.Target + "」がありません"));
                        else AddGoal(p, GoalKind.AvoidRole, index, "避ける " + goal.Target, goal.Target + "回避");
                    }
                    else
                    {
                        int index = ShapeIndex(goal.Target);
                        if (index < 0) errors.Add(new RuleError(goal.Line, "停止形「" + goal.Target + "」が定義されていません"));
                        else AddGoal(p, goal.Kind, index, (goal.Kind == GoalKind.Prefer ? "狙う " : "避ける ") + goal.Target, goal.Target + (goal.Kind == GoalKind.Prefer ? "狙い" : "回避"));
                    }
                }

                // 狙う出目が複数あれば、まず「どれかになる」割合を最大にし、上下の順はその中で比べる
                int firstPrefer = p.Kinds.IndexOf(GoalKind.Prefer);
                if (firstPrefer >= 0 && p.Kinds.LastIndexOf(GoalKind.Prefer) != firstPrefer)
                {
                    p.Kinds.Insert(firstPrefer, GoalKind.PreferAny);
                    p.Targets.Insert(firstPrefer, rules.Shapes.Count);
                    p.Labels.Insert(firstPrefer, "狙う 出目のどれか");
                    p.Reasons.Insert(firstPrefer, "出目狙い");
                }

                // 暗黙の引込：ルールに書かれていない当選役を最後に足す（書いた行が常に優先）
                if (!rule.NoImplicitPull)
                {
                    var implicitRoles = new List<int>();
                    foreach (int index in wonOrder)
                    {
                        bool mentioned = false;
                        foreach (var goal in rule.Goals)
                            if (goal.Kind == GoalKind.Pull && goal.Target == machine.Roles[index].Name) mentioned = true;
                        if (!mentioned) implicitRoles.Add(index);
                    }
                    // 安定ソート：小役優先なら小役→ボーナス、ボーナス優先なら逆
                    var first = new List<int>();
                    var second = new List<int>();
                    foreach (int index in implicitRoles)
                    {
                        bool bonus = machine.Roles[index].IsBonus;
                        if (bonus == machine.BonusFirst) first.Add(index); else second.Add(index);
                    }
                    first.AddRange(second);
                    foreach (int index in first) AddGoal(p, GoalKind.Pull, index, "引込 " + machine.Roles[index].Name + "（自動）", machine.Roles[index].Name + "引込");
                }

                foreach (string name in rule.Allowed)
                    if (ShapeIndex(name) < 0) errors.Add(new RuleError(rule.Line, "停止形「" + name + "」が定義されていません"));
                foreach (string name in rule.Denied)
                {
                    int index = ShapeIndex(name);
                    if (index < 0) errors.Add(new RuleError(rule.Line, "停止形「" + name + "」が定義されていません"));
                    else if (!p.Denied.Contains(index)) p.Denied.Add(index);
                }
                // 専用の停止形は、狙う／許可したフラグ以外では出さない
                for (int s = 0; s < rules.Shapes.Count; s++)
                {
                    if (!rules.Shapes[s].Exclusive || p.Denied.Contains(s)) continue;
                    bool permitted = rule.Allowed.Contains(rules.Shapes[s].Name);
                    for (int g = 0; g < p.Kinds.Count; g++)
                        if (p.Kinds[g] == GoalKind.Prefer && p.Targets[g] == s) permitted = true;
                    if (!permitted) p.Denied.Add(s);
                }
                foreach (int s in p.Denied) p.Labels.Add("禁止 " + rules.Shapes[s].Name);
                plans.Add(p);
            }
            return plans;
        }

        private static void AddGoal(Plan p, GoalKind kind, int target, string label, string reason)
        {
            p.Kinds.Add(kind);
            p.Targets.Add(target);
            p.Labels.Add(label);
            p.Reasons.Add(reason);
        }

        private int RoleIndex(string name)
        {
            for (int i = 0; i < machine.Roles.Count; i++) if (machine.Roles[i].Name == name) return i;
            return -1;
        }

        private int ShapeIndex(string name)
        {
            for (int i = 0; i < rules.Shapes.Count; i++) if (rules.Shapes[i].Name == name) return i;
            return -1;
        }

        // ---------------------------------------------------------------- 生成

        private void Expand(int[] stops, int[] orders, int stage, int pattern)
        {
            if (stage == 3) return;
            for (int r = 0; r < 3; r++)
            {
                if (stops[r] >= 0) continue;
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                int nextPattern = pattern | ((stage + 1) << ((2 - r) * 2));
                long key = Key(nextPattern, stops);
                if (tables.ContainsKey(key)) continue;
                var table = new SlipTable
                {
                    Pattern = nextPattern, Reel = r, Stage = stage, Prior = (int[])stops.Clone(),
                    Slips = new int[lengths[r]], Unavoidable = new bool[lengths[r]],
                    Reasons = new string[lengths[r]], Manual = new bool[lengths[r]]
                };
                tables[key] = table;
                Dictionary<int, int> manual;
                plan.Rule.Overrides.TryGetValue(key, out manual);
                var reached = new bool[lengths[r]];
                for (int press = 0; press < lengths[r]; press++)
                {
                    bool unavoidable;
                    string reason;
                    int slip = Choose(stops, orders, r, stage, press, out unavoidable, out reason);
                    int forced;
                    if (manual != null && manual.TryGetValue(press, out forced) && forced >= 0 && forced <= maxSlip)
                    {
                        slip = forced;
                        reason = "手動";
                        table.Manual[press] = true;
                        stops[r] = Machine.Wrap(press - slip, lengths[r]);
                        orders[r] = stage + 1;
                        unavoidable = !IsSafe(stops, orders);
                        stops[r] = -1;
                        orders[r] = 0;
                    }
                    table.Slips[press] = slip;
                    table.Unavoidable[press] = unavoidable;
                    table.Reasons[press] = reason;
                    reached[Machine.Wrap(press - slip, lengths[r])] = true;
                }
                for (int pos = 0; pos < lengths[r]; pos++)
                {
                    if (!reached[pos]) continue;
                    stops[r] = pos;
                    orders[r] = stage + 1;
                    Expand(stops, orders, stage + 1, nextPattern);
                    stops[r] = -1;
                    orders[r] = 0;
                }
            }
        }

        private int Choose(int[] stops, int[] orders, int reel, int stage, int press, out bool unavoidable, out string reason)
        {
            int goalCount = plan.Kinds.Count;
            var safe = new bool[maxSlip + 1];
            bool anySafe = false;
            for (int s = 0; s <= maxSlip; s++)
            {
                stops[reel] = Machine.Wrap(press - s, lengths[reel]);
                orders[reel] = stage + 1;
                safe[s] = IsSafe(stops, orders);
                if (safe[s]) anySafe = true;
            }
            unavoidable = !anySafe;

            int best = -1;
            int[] bestScore = null;
            var candidates = new List<int[]>();
            for (int s = 0; s <= maxSlip; s++)
            {
                if (anySafe && !safe[s]) continue;
                stops[reel] = Machine.Wrap(press - s, lengths[reel]);
                orders[reel] = stage + 1;
                var score = new int[goalCount];
                for (int g = 0; g < goalCount; g++) score[g] = GoalScore(g, stops, orders, reel);
                candidates.Add(score);
                if (best < 0 || Better(score, bestScore))
                {
                    best = s;
                    bestScore = score;
                }
            }
            stops[reel] = -1;
            orders[reel] = 0;

            // 決め手: 候補の間で最初に差がついた優先行。差がなければ、避けたか最短か。
            reason = null;
            for (int g = 0; g < goalCount && reason == null; g++)
                foreach (var score in candidates)
                    if (score[g] != bestScore[g]) { reason = plan.Reasons[g]; break; }
            if (reason == null) reason = unavoidable ? "回避不能" : best > 0 && !safe[0] ? "回避" : "最短";
            return best;
        }

        private static bool Better(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] > b[i]) return true;
                if (a[i] < b[i]) return false;
            }
            return false;
        }

        private int GoalScore(int g, int[] stops, int[] orders, int reel)
        {
            int target = plan.Targets[g];
            switch (plan.Kinds[g])
            {
                case GoalKind.Pull: return PullLevel(target, stops, orders, reel);
                case GoalKind.Prefer:
                case GoalKind.PreferAny: return Score(Chance(machine.Roles.Count + target, stops, orders));
                case GoalKind.AvoidRole:
                    // この押し順で狙える基本目がなければ、通常どおり引き込む
                    if (!BasicTargetPossible(orders)) return PullLevel(target, stops, orders, reel);
                    return -Score(Chance(target, stops, orders));
                default: return -Score(Chance(machine.Roles.Count + target, stops, orders));
            }
        }

        /// <summary>
        /// 引込の評価値。主に「この後どの順・どの位置で押されても、非成立役を避けつつ最善に滑らせたときに役が揃う割合」
        /// （1なら必ず揃う）で比べる。揃う見込みがなくなった後は、今回のリール単体で役図柄を有効ラインに出す候補を優先する。
        /// </summary>
        private int PullLevel(int role, int[] stops, int[] orders, int reel)
        {
            int chance = Score(Chance(role, stops, orders));
            bool single = false;
            if (!roleReelAny[role][reel])
                foreach (var line in activeLines)
                    if (roleCell[role][reel][stops[reel] * 3 + line.Rows[reel]]) single = true;
            return chance * 2 + (single ? 1 : 0);
        }

        private static int Score(double chance) { return (int)Math.Round(chance * 1000000.0); }

        /// <summary>
        /// 残りのリールを等確率の順・位置で押されたとき、安全な範囲で最善に滑らせて目標を満たす割合。
        /// target は役のインデックス、または 役数＋停止形のインデックス。
        /// </summary>
        private double Chance(int target, int[] stops, int[] orders)
        {
            long key = (long)target << 48;
            for (int r = 0; r < 3; r++) key |= (long)((stops[r] + 1) << 2 | orders[r]) << (r * 14);
            double cached;
            if (chanceMemo.TryGetValue(key, out cached)) return cached;

            double result;
            int stage = 0;
            for (int r = 0; r < 3; r++) if (stops[r] >= 0) stage++;
            if (stage == 3) result = TargetMet(target, stops, orders) && !FinalViolation(plan, stops, orders) ? 1.0 : 0.0;
            else
            {
                double sum = 0;
                int count = 0;
                for (int r = 0; r < 3; r++)
                {
                    if (stops[r] >= 0) continue;
                    for (int press = 0; press < lengths[r]; press++)
                    {
                        double best = 0;
                        bool anySafe = false;
                        for (int s = 0; s <= maxSlip; s++)
                        {
                            stops[r] = Machine.Wrap(press - s, lengths[r]);
                            orders[r] = stage + 1;
                            if (IsSafe(stops, orders)) anySafe = true;
                        }
                        for (int s = 0; s <= maxSlip; s++)
                        {
                            stops[r] = Machine.Wrap(press - s, lengths[r]);
                            orders[r] = stage + 1;
                            if (anySafe && !IsSafe(stops, orders)) continue;
                            double value = Chance(target, stops, orders);
                            if (value > best) best = value;
                        }
                        stops[r] = -1;
                        orders[r] = 0;
                        sum += best;
                        count++;
                    }
                }
                result = count == 0 ? 0 : sum / count;
            }
            chanceMemo[key] = result;
            return result;
        }

        private bool TargetMet(int target, int[] stops, int[] orders)
        {
            int roles = machine.Roles.Count;
            if (target < roles) return RoleFormed(target, stops, orders);
            if (target - roles < rules.Shapes.Count) return ShapePossible(target - roles, stops, orders);
            for (int g = 0; g < plan.Kinds.Count; g++)
                if (plan.Kinds[g] == GoalKind.Prefer && ShapePossible(plan.Targets[g], stops, orders)) return true;
            return false;
        }

        private bool BasicTargetPossible(int[] orders)
        {
            var none = new[] { -1, -1, -1 };
            for (int g = 0; g < plan.Kinds.Count; g++)
            {
                if (plan.Kinds[g] != GoalKind.Prefer || rules.Shapes[plan.Targets[g]].Exclusive) continue;
                if (ShapePossible(plan.Targets[g], none, orders)) return true;
            }
            return false;
        }

        private bool ShapePossible(int shape, int[] stops, int[] orders)
        {
            int first = rules.Shapes[shape].FirstReel;
            if (first >= 0)
                for (int r = 0; r < 3; r++)
                    if (orders[r] == 1 ? r != first : r == first && orders[r] > 1) return false;
            for (int r = 0; r < 3; r++)
            {
                if (stops[r] >= 0 ? !shapeReel[shape][r][stops[r]] : !shapeReelPossible[shape][r]) return false;
            }
            return true;
        }

        private bool IsSafe(int[] stops, int[] orders)
        {
            long key = 0;
            for (int r = 0; r < 3; r++) key = (key << 14) | (long)((stops[r] + 1) << 2 | orders[r]);
            bool cached;
            if (safeMemo.TryGetValue(key, out cached)) return cached;

            bool result;
            int stage = 0;
            for (int r = 0; r < 3; r++) if (stops[r] >= 0) stage++;
            if (stage == 3) result = !FinalViolation(plan, stops, orders);
            else
            {
                result = true;
                for (int r = 0; r < 3 && result; r++)
                {
                    if (stops[r] >= 0) continue;
                    for (int press = 0; press < lengths[r] && result; press++)
                    {
                        bool ok = false;
                        for (int s = 0; s <= maxSlip && !ok; s++)
                        {
                            stops[r] = Machine.Wrap(press - s, lengths[r]);
                            orders[r] = stage + 1;
                            ok = IsSafe(stops, orders);
                        }
                        stops[r] = -1;
                        orders[r] = 0;
                        if (!ok) result = false;
                    }
                }
            }
            safeMemo[key] = result;
            return result;
        }

        private bool FinalViolation(Plan p, int[] stops, int[] orders)
        {
            for (int i = 0; i < machine.Roles.Count; i++)
                if (!p.Won[i] && RoleFormed(i, stops, orders)) return true;
            foreach (int s in p.Denied)
                if (ShapePossible(s, stops, orders)) return true;
            return false;
        }

        private bool RoleFormed(int role, int[] stops, int[] orders)
        {
            var stopOrder = machine.Roles[role].StopOrder;
            if (stopOrder != null)
                for (int r = 0; r < 3; r++)
                    if (stopOrder[r] > 0 && stopOrder[r] != orders[r]) return false;
            foreach (var line in activeLines)
            {
                bool all = true;
                for (int r = 0; r < 3 && all; r++) all = roleCell[role][r][stops[r] * 3 + line.Rows[r]];
                if (all) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- 全経路シミュレーション

        private ControlStats Simulate(Plan p, ControlResult result)
        {
            var stats = new ControlStats();
            var index = new Dictionary<long, SlipTable>();
            foreach (var t in result.Tables) index[Key(t.Pattern, t.Prior)] = t;
            foreach (var t in result.Tables)
                foreach (bool u in t.Unavoidable) if (u) stats.UnavoidableCells++;

            int roleCount = machine.Roles.Count;
            stats.RoleNames = new string[roleCount];
            stats.RoleWon = new bool[roleCount];
            stats.RoleFormed = new long[roleCount];
            stats.RoleFormedByOrder = new long[roleCount, 6];
            for (int i = 0; i < roleCount; i++) { stats.RoleNames[i] = machine.Roles[i].Name; stats.RoleWon[i] = p.Won[i]; }
            stats.ShapeNames = new List<string>();
            var shapeIds = new List<int>();
            for (int s = 0; s < rules.Shapes.Count; s++) shapeIds.Add(s);
            foreach (int s in shapeIds) stats.ShapeNames.Add(rules.Shapes[s].Name);
            stats.ShapeMatched = new long[shapeIds.Count];
            stats.ShapeMatchedByOrder = new long[shapeIds.Count, 6];

            var stops = new int[3];
            var orders = new int[3];
            var presses = new int[3];
            for (int o = 0; o < 6; o++)
            {
                for (presses[0] = 0; presses[0] < lengths[0]; presses[0]++)
                for (presses[1] = 0; presses[1] < lengths[1]; presses[1]++)
                for (presses[2] = 0; presses[2] < lengths[2]; presses[2]++)
                {
                    stops[0] = stops[1] = stops[2] = -1;
                    orders[0] = orders[1] = orders[2] = 0;
                    int pattern = 0;
                    for (int s = 0; s < 3; s++)
                    {
                        int r = Orders[o][s];
                        pattern |= (s + 1) << ((2 - r) * 2);
                        SlipTable t;
                        if (!index.TryGetValue(Key(pattern, stops), out t)) throw new InvalidOperationException("生成されていない停止条件があります");
                        stops[r] = Machine.Wrap(presses[r] - t.Slips[presses[r]], lengths[r]);
                        orders[r] = s + 1;
                    }
                    stats.Paths++;
                    stats.PathsByOrder[o]++;
                    bool violation = false;
                    for (int i = 0; i < roleCount; i++)
                    {
                        if (!RoleFormed(i, stops, orders)) continue;
                        stats.RoleFormed[i]++;
                        stats.RoleFormedByOrder[i, o]++;
                        if (!p.Won[i]) violation = true;
                    }
                    for (int k = 0; k < shapeIds.Count; k++)
                    {
                        if (!ShapePossible(shapeIds[k], stops, orders)) continue;
                        stats.ShapeMatched[k]++;
                        stats.ShapeMatchedByOrder[k, o]++;
                        if (p.Denied.Contains(shapeIds[k])) violation = true;
                    }
                    if (violation) stats.ViolationPaths++;
                    stats.AddWindow(WindowKey(stops));
                }
            }
            return stats;
        }

        // ---------------------------------------------------------------- 結果の参照（出目マップなど）

        /// <summary>生成済みの滑りテーブルを1枚取り出す。無ければ null。</summary>
        public SlipTable FindTable(ControlResult result, int pattern, int[] prior)
        {
            Dictionary<long, SlipTable> index;
            if (!resultIndex.TryGetValue(result, out index))
            {
                index = new Dictionary<long, SlipTable>();
                foreach (var t in result.Tables) index[Key(t.Pattern, t.Prior)] = t;
                resultIndex[result] = index;
            }
            SlipTable table;
            return index.TryGetValue(Key(pattern, prior), out table) ? table : null;
        }

        /// <summary>
        /// 押し順 order で、第1停止リールが firstStop（下段インデックス）に止まった後、
        /// 第2・第3停止を press2・press3 で押したときの最終停止位置。たどれなければ null。
        /// </summary>
        public int[] Resolve(ControlResult result, int order, int firstStop, int press2, int press3, out int[] orders)
        {
            var stops = new[] { -1, -1, -1 };
            orders = new int[3];
            int first = Orders[order][0];
            stops[first] = firstStop;
            orders[first] = 1;
            int[] presses = { 0, press2, press3 };
            for (int s = 1; s < 3; s++)
            {
                int r = Orders[order][s];
                var table = FindTable(result, PatternFor(order, s), stops);
                if (table == null) return null;
                stops[r] = Machine.Wrap(presses[s] - table.Slips[presses[s]], lengths[r]);
                orders[r] = s + 1;
            }
            return stops;
        }

        /// <summary>第1停止で止まりうる位置（下段インデックス）。</summary>
        public bool[] FirstStops(ControlResult result, int order)
        {
            int r = Orders[order][0];
            var reached = new bool[lengths[r]];
            var table = FindTable(result, PatternFor(order, 0), new[] { -1, -1, -1 });
            if (table == null) return reached;
            for (int press = 0; press < lengths[r]; press++) reached[Machine.Wrap(press - table.Slips[press], lengths[r])] = true;
            return reached;
        }

        /// <summary>停止位置で揃っている役の名前。</summary>
        public List<string> FormedRoles(int[] stops, int[] orders)
        {
            var list = new List<string>();
            for (int i = 0; i < machine.Roles.Count; i++) if (RoleFormed(i, stops, orders)) list.Add(machine.Roles[i].Name);
            return list;
        }

        /// <summary>停止位置で合っている停止形の名前（定義順）。</summary>
        public List<string> MatchedShapes(int[] stops, int[] orders)
        {
            var list = new List<string>();
            for (int s = 0; s < rules.Shapes.Count; s++) if (ShapePossible(s, stops, orders)) list.Add(rules.Shapes[s].Name);
            return list;
        }

        private string WindowKey(int[] stops)
        {
            var parts = new string[9];
            for (int row = 0; row < 3; row++)
                for (int r = 0; r < 3; r++) parts[row * 3 + r] = machine.SymbolAt(r, stops[r], row);
            return string.Join("\t", parts);
        }
    }

    /// <summary>全押し順・全押下位置を等確率で押したときの集計。</summary>
    public sealed class ControlStats
    {
        public long Paths;
        public long[] PathsByOrder = new long[6];
        public long ViolationPaths;
        public int UnavoidableCells;
        public string[] RoleNames;
        public bool[] RoleWon;
        public long[] RoleFormed;
        public long[,] RoleFormedByOrder;
        public List<string> ShapeNames;
        public long[] ShapeMatched;
        public long[,] ShapeMatchedByOrder;
        /// <summary>出目（上中下×左中右の図柄をタブ区切り）ごとの経路数。</summary>
        public Dictionary<string, long> Windows = new Dictionary<string, long>();

        public void AddWindow(string key)
        {
            long count;
            Windows.TryGetValue(key, out count);
            Windows[key] = count + 1;
        }

        public List<KeyValuePair<string, long>> TopWindows(int limit)
        {
            var list = new List<KeyValuePair<string, long>>(Windows);
            list.Sort(delegate (KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                int c = b.Value.CompareTo(a.Value);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }
    }
}
