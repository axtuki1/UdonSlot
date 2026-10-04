using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AX.UdonSlot.REV1_0;
using AX.UdonSlot.REV1_0.Editor.ReelRule;

// 生成エンジンの検証。終了コード 0 なら全項目通過。
public static class Program
{
    private static int checks;
    private static int failures;

    private static void Check(string name, bool ok)
    {
        checks++;
        if (ok) return;
        failures++;
        Console.WriteLine("NG: " + name);
    }

    public static int Main(string[] args)
    {
        var watch = Stopwatch.StartNew();
        ParserChecks();
        var machine = SampleMachine();
        var results = Generate(machine, File.ReadAllText(Path.Combine(Dir(), "sample.rules.txt")));
        GenerationChecks(machine, results);
        RuntimeLookupChecks(machine, results);
        DefaultRuleChecks(machine);
        ErrorChecks(machine);
        UnavoidableChecks();
        if (args.Length > 0) File.WriteAllText(args[0], ReelRuleReport.Markdown(results, 15));
        Console.WriteLine(checks + " checks, " + failures + " failures, " + watch.ElapsedMilliseconds + " ms");
        return failures == 0 ? 0 : 1;
    }

    private static string Dir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "sample.rules.txt"))) dir = Path.GetDirectoryName(dir);
        return dir;
    }

    private static List<ControlResult> Generate(Machine machine, string text)
    {
        return new ReelRuleEngine(machine, ReelRuleParser.Parse(text)).GenerateAll();
    }

    private static ControlResult Find(List<ControlResult> results, string name)
    {
        foreach (var r in results) if (r.Name == name) return r;
        throw new Exception("result not found: " + name);
    }

    private static double Rate(ControlResult r, string role)
    {
        int i = Array.IndexOf(r.Stats.RoleNames, role);
        return (double)r.Stats.RoleFormed[i] / r.Stats.Paths;
    }

    private static double ShapeRate(ControlResult r, string shape)
    {
        int i = r.Stats.ShapeNames.IndexOf(shape);
        return i < 0 ? -1 : (double)r.Stats.ShapeMatched[i] / r.Stats.Paths;
    }

    // ------------------------------------------------------------ 機種データ

    public static Machine SampleMachine()
    {
        var m = new Machine();
        m.Strips[0] = "7 ベル リプ スイカ チェリー ベル リプ BAR ベル リプ スイカ ベル リプ 7 ベル リプ チェリー スイカ ベル リプ BAR".Split(' ');
        m.Strips[1] = "7 ベル リプ スイカ ベル リプ チェリー ベル リプ BAR ベル リプ スイカ ベル リプ 7 ベル リプ スイカ ベル リプ".Split(' ');
        m.Strips[2] = "7 ベル リプ BAR ベル リプ スイカ ベル リプ チェリー ベル リプ 7 ベル リプ スイカ ベル リプ BAR ベル リプ".Split(' ');
        int[][] lines = { new[] { 0, 0, 0 }, new[] { 1, 1, 1 }, new[] { 2, 2, 2 }, new[] { 0, 1, 2 }, new[] { 2, 1, 0 } };
        foreach (var rows in lines) m.Lines.Add(new PayLine { Rows = rows, BetMask = 4 });
        m.Lines[1].BetMask = 7;
        m.Roles.Add(RoleOf("リプレイ", "リプ", "リプ", "リプ", 0));
        m.Roles.Add(RoleOf("ベル", "ベル", "ベル", "ベル", 8));
        m.Roles.Add(RoleOf("スイカ", "スイカ", "スイカ", "スイカ", 5));
        m.Roles.Add(RoleOf("チェリー", "チェリー", "", "", 2));
        var bb = RoleOf("BB", "7", "7", "7", 0);
        bb.IsBonus = true;
        m.Roles.Add(bb);
        m.Roles[0].IsReplay = true;
        AddFlag(m, "ハズレ");
        AddFlag(m, "リプレイ", "リプレイ");
        AddFlag(m, "ベル", "ベル");
        AddFlag(m, "スイカ", "スイカ");
        AddFlag(m, "チェリー", "チェリー");
        AddFlag(m, "BB", "BB");
        return m;
    }

    private static Role RoleOf(string name, string left, string center, string right, int payout)
    {
        var role = new Role { Name = name, Payout = payout };
        string[] symbols = { left, center, right };
        for (int r = 0; r < 3; r++) role.Symbols[r] = symbols[r].Length == 0 ? new string[0] : symbols[r].Split(',');
        return role;
    }

    private static void AddFlag(Machine m, string name, params string[] roles)
    {
        var flag = new LotteryFlag { Name = name };
        flag.Roles.AddRange(roles);
        m.Flags.Add(flag);
    }

    // ------------------------------------------------------------ 検証

    private static void ParserChecks()
    {
        var rules = ReelRuleParser.Parse("最大滑り 3\n停止形 A 専用\n  スイカ * !ベル|リプ\n  * * *\n  * * *\nフラグ X # comment\n  抽選 Y\n  成立 BB\n  狙う A\n  引込 なし\n");
        Check("parse maxslip", rules.MaxSlip == 3);
        var a = rules.FindShape("A");
        Check("parse shape", a != null && a.Exclusive);
        Check("parse any cell", a.Cell(0, 1).IsAny && a.Cell(2, 2).IsAny);
        Check("parse negate cell", a.Cell(0, 2).Negate && a.Cell(0, 2).Options.Count == 2);
        Check("negate matches others", a.Cell(0, 2).Matches("7") && !a.Cell(0, 2).Matches("ベル"));
        var x = rules.FindControl("X");
        Check("parse control", x != null && x.SourceFlag == "Y" && x.ExtraRoles.Contains("BB") && x.NoImplicitPull && x.Goals.Count == 1);
        try
        {
            ReelRuleParser.Parse("狙う A\nフラグ B\n  謎 C\n停止形 S\n * *\n");
            Check("parse errors thrown", false);
        }
        catch (RuleException e)
        {
            Check("parse error count", e.Errors.Count == 4);
            Check("parse error line", e.Errors[0].Line == 1 && e.Errors[1].Line == 3);
        }
    }

    private static void GenerationChecks(Machine machine, List<ControlResult> results)
    {
        Check("all lottery flags + composite", results.Count == machine.Flags.Count + 1);
        foreach (var r in results)
        {
            Check(r.Name + ": no violation", r.Stats.ViolationPaths == 0);
            Check(r.Name + ": no unavoidable", r.Stats.UnavoidableCells == 0);
            Check(r.Name + ": all paths", r.Stats.Paths == 6L * 21 * 21 * 21);
            foreach (var t in r.Tables)
                foreach (int s in t.Slips) Check(r.Name + ": slip range", s >= 0 && s <= 4);
        }
        // 引込だけのフラグは、全押下位置の総当たりで求めた最大の入賞率と一致する
        Check("bell rate is optimal", Near(Rate(Find(results, "ベル"), "ベル"), Optimum(machine, "ベル", new[] { "ベル" })));
        Check("replay rate is optimal", Near(Rate(Find(results, "リプレイ"), "リプレイ"), Optimum(machine, "リプレイ", new[] { "リプレイ" })));
        Check("cherry rate is optimal", Near(Rate(Find(results, "チェリー"), "チェリー"), Optimum(machine, "チェリー", new[] { "チェリー" })));
        var suika = Find(results, "スイカ");
        Check("prefer shape first is optimal for the shape", Near(ShapeRate(suika, "右下がりスイカ"), Optimum(machine, "右下がりスイカ", new[] { "スイカ" })));
        Check("upper suika preferred appears", ShapeRate(suika, "右下がりスイカ") > 0);
        Check("suika still pulled after shape", Rate(suika, "スイカ") >= ShapeRate(suika, "右下がりスイカ"));
        Check("BB allows exclusive shape", !Find(results, "BB").GoalLines.Contains("禁止 リーチ目A"));
        Check("other flags deny exclusive shape", Find(results, "ベル").GoalLines.Contains("禁止 リーチ目A"));
        foreach (var r in results)
            if (r.Name != "BB")
                Check(r.Name + ": exclusive shape never appears", ShapeRate(r, "リーチ目A") <= 0);
        var replayBB = Find(results, "REPLAY_BB");
        Check("composite uses lottery roles", replayBB.WonRoles.Contains("リプレイ") && replayBB.WonRoles.Contains("BB"));
        Check("composite pulls replay optimally", Near(Rate(replayBB, "リプレイ"), Optimum(machine, "リプレイ", new[] { "リプレイ", "BB" })));

        // 狙う の効果: 指定しない場合より右下がりスイカが増える
        string plain = "停止形 右下がりスイカ\n スイカ * *\n * スイカ *\n * * スイカ\nフラグ スイカ\n  避ける 右下がりスイカ\n";
        var avoided = Find(Generate(machine, plain), "スイカ");
        Check("avoid shape keeps pull", Rate(avoided, "スイカ") > 0);
        Check("prefer raises shape rate", ShapeRate(suika, "右下がりスイカ") > ShapeRate(avoided, "右下がりスイカ"));

        // 同じ入力なら同じ結果
        var again = Generate(machine, File.ReadAllText(Path.Combine(Dir(), "sample.rules.txt")));
        bool same = again.Count == results.Count;
        for (int i = 0; same && i < results.Count; i++)
        {
            same = again[i].Tables.Count == results[i].Tables.Count;
            for (int t = 0; same && t < results[i].Tables.Count; t++)
                same = string.Join(",", again[i].Tables[t].Slips) == string.Join(",", results[i].Tables[t].Slips);
        }
        Check("deterministic", same);
    }

    // ベイクした配列を実機と同じ検索（条件付き→基本テーブル）でたどり、生成結果と一致するか
    private static void RuntimeLookupChecks(Machine machine, List<ControlResult> results)
    {
        int[] lengths = { 21, 21, 21 };
        foreach (var r in results)
        {
            var baked = ReelRuleBaker.Bake(r, lengths);
            var data = new ReelControlData
            {
                reelStopSlipCount = baked.ReelStopSlipCount,
                IsReelStopTableStyle = baked.IsReelStopTableStyle,
                conditionPatterns = baked.ConditionPatterns,
                conditionLeftPositions = baked.ConditionLeftPositions,
                conditionCenterPositions = baked.ConditionCenterPositions,
                conditionRightPositions = baked.ConditionRightPositions,
                conditionSlipOffsets = baked.ConditionSlipOffsets,
                conditionSlipLengths = baked.ConditionSlipLengths,
                conditionSlips = baked.ConditionSlips
            };
            var index = new Dictionary<long, SlipTable>();
            foreach (var t in r.Tables) index[ReelRuleEngine.Key(t.Pattern, t.Prior)] = t;
            bool ok = true;
            var presses = new int[3];
            for (int o = 0; o < 6 && ok; o++)
            for (presses[0] = 0; presses[0] < 21 && ok; presses[0]++)
            for (presses[1] = 0; presses[1] < 21 && ok; presses[1]++)
            for (presses[2] = 0; presses[2] < 21 && ok; presses[2]++)
            {
                var stops = new[] { -1, -1, -1 };
                var order = new int[3];
                for (int s = 0; s < 3; s++)
                {
                    int reel = ReelRuleEngine.Orders[o][s];
                    order[reel] = s + 1;
                    int pattern = ReelControlData.GetIndexForReelStopPattern(order[0], order[1], order[2]);
                    ok &= pattern == ReelRuleEngine.PatternFor(o, s) && data.IsReelStopTableStyle[pattern];
                    int slip = data.GetConditionalSlip(pattern, stops[0], stops[1], stops[2], presses[reel]);
                    if (slip < 0) slip = data.reelStopSlipCount[pattern][presses[reel]];
                    ok &= slip == index[ReelRuleEngine.Key(pattern, stops)].Slips[presses[reel]];
                    stops[reel] = Machine.Wrap(presses[reel] - slip, 21);
                }
            }
            Check(r.Name + ": baked runtime lookup matches", ok);
        }
    }

    private static void DefaultRuleChecks(Machine machine)
    {
        // ルールが空でも全フラグが生成され、当選役は自動で引き込まれる
        var results = Generate(machine, "");
        Check("empty rules generate all flags", results.Count == machine.Flags.Count);
        Check("empty rules: bell pulled", Near(Rate(Find(results, "ベル"), "ベル"), Optimum(machine, "ベル", new[] { "ベル" })));
        Check("empty rules: hazure clean", Find(results, "ハズレ").Stats.ViolationPaths == 0);
        Check("implicit pull label", Find(results, "ベル").GoalLines[0].Contains("自動"));
        var order = Find(Generate(machine, "停止形 S\n スイカ * *\n * * *\n * * *\nフラグ スイカ\n 狙う S\n"), "スイカ");
        Check("written lines come before implicit pull", order.GoalLines[0] == "狙う S" && order.GoalLines[1].Contains("自動"));
        // 引込なし なら役を狙わない
        var none = Find(Generate(machine, "フラグ ベル\n 引込 なし\n"), "ベル");
        Check("no implicit pull lowers bell", Rate(none, "ベル") < Rate(Find(results, "ベル"), "ベル"));
    }

    private static void ErrorChecks(Machine machine)
    {
        string[] bad =
        {
            "フラグ ベル\n 引込 スイカ\n",          // 当選役でない役
            "フラグ ベル\n 狙う 無い停止形\n",      // 未定義の停止形
            "フラグ 謎\n",                          // 抽選表にないフラグ
            "停止形 A\n 金 * *\n * * *\n * * *\n",   // 配列にない図柄
            "フラグ ベル\n 成立 無い役\n"           // 配当表にない役
        };
        foreach (string text in bad)
        {
            bool thrown = false;
            try { Generate(machine, text); }
            catch (RuleException e) { thrown = e.Errors.Count > 0; }
            Check("rule error detected: " + text.Split('\n')[1].Trim(), thrown);
        }
    }

    private static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-9; }

    // ---- エンジンとは独立した総当たり（停止形は上段の3マスだけを見る簡易版）
    private static Machine om;
    private static string oTarget;
    private static HashSet<string> oWon;
    private static Dictionary<string, bool> oSafe;
    private static Dictionary<string, double> oValue;

    private static double Optimum(Machine machine, string target, string[] won)
    {
        om = machine; oTarget = target; oWon = new HashSet<string>(won);
        oSafe = new Dictionary<string, bool>(); oValue = new Dictionary<string, double>();
        return OValue(new[] { -1, -1, -1 }, new int[3]);
    }

    private static bool OFormed(Role role, int[] st, int[] ord)
    {
        foreach (var line in om.Lines)
        {
            if ((line.BetMask & 4) == 0) continue;
            bool all = true;
            for (int k = 0; k < 3; k++)
                if (role.Symbols[k].Length > 0 && Array.IndexOf(role.Symbols[k], om.SymbolAt(k, st[k], line.Rows[k])) < 0) all = false;
            if (all) return true;
        }
        return false;
    }

    private static bool OMet(int[] st, int[] ord)
    {
        var role = om.FindRole(oTarget);
        if (role != null) return OFormed(role, st, ord);
        for (int k = 0; k < 3; k++) if (om.SymbolAt(k, st[k], k) != "スイカ") return false; // 右下がりスイカ
        return true;
    }

    private static bool OBad(int[] st, int[] ord)
    {
        foreach (var role in om.Roles) if (!oWon.Contains(role.Name) && OFormed(role, st, ord)) return true;
        // 専用停止形 リーチ目A（中段 7 BAR *）は BB 以外で禁止
        if (!oWon.Contains("BB") && om.SymbolAt(0, st[0], 1) == "7" && om.SymbolAt(1, st[1], 1) == "BAR") return true;
        return false;
    }

    private static string OKey(int[] st, int[] ord) { return string.Join(",", st) + "/" + string.Join(",", ord); }

    private static bool OIsSafe(int[] st, int[] ord)
    {
        string key = OKey(st, ord);
        bool cached;
        if (oSafe.TryGetValue(key, out cached)) return cached;
        int stage = 0;
        foreach (int x in st) if (x >= 0) stage++;
        bool result = true;
        if (stage == 3) result = !OBad(st, ord);
        else
            for (int r = 0; r < 3 && result; r++)
            {
                if (st[r] >= 0) continue;
                for (int p = 0; p < 21 && result; p++)
                {
                    bool ok = false;
                    for (int s = 0; s <= 4 && !ok; s++) { st[r] = Machine.Wrap(p - s, 21); ord[r] = stage + 1; ok = OIsSafe(st, ord); }
                    st[r] = -1; ord[r] = 0;
                    result = ok;
                }
            }
        oSafe[key] = result;
        return result;
    }

    private static double OValue(int[] st, int[] ord)
    {
        string key = OKey(st, ord);
        double cached;
        if (oValue.TryGetValue(key, out cached)) return cached;
        int stage = 0;
        foreach (int x in st) if (x >= 0) stage++;
        double result;
        if (stage == 3) result = OMet(st, ord) && !OBad(st, ord) ? 1 : 0;
        else
        {
            double sum = 0; int n = 0;
            for (int r = 0; r < 3; r++)
            {
                if (st[r] >= 0) continue;
                for (int p = 0; p < 21; p++)
                {
                    var safe = new bool[5]; bool any = false;
                    for (int s = 0; s <= 4; s++) { st[r] = Machine.Wrap(p - s, 21); ord[r] = stage + 1; safe[s] = OIsSafe(st, ord); any |= safe[s]; }
                    double best = 0;
                    for (int s = 0; s <= 4; s++)
                    {
                        if (any && !safe[s]) continue;
                        st[r] = Machine.Wrap(p - s, 21); ord[r] = stage + 1;
                        best = Math.Max(best, OValue(st, ord));
                    }
                    st[r] = -1; ord[r] = 0;
                    sum += best; n++;
                }
            }
            result = sum / n;
        }
        oValue[key] = result;
        return result;
    }

    private static void UnavoidableChecks()
    {
        // 全コマ同じ図柄なら、その役のハズレは避けられない
        var m = new Machine();
        for (int r = 0; r < 3; r++) m.Strips[r] = new[] { "A", "A", "A", "A", "A", "A", "A", "A" };
        m.Lines.Add(new PayLine { Rows = new[] { 1, 1, 1 }, BetMask = 7 });
        m.Roles.Add(RoleOf("AAA", "A", "A", "A", 1));
        AddFlag(m, "ハズレ");
        var r0 = Generate(m, "")[0];
        Check("unavoidable reported", r0.Stats.UnavoidableCells > 0 && r0.Stats.ViolationPaths == r0.Stats.Paths);
    }
}
