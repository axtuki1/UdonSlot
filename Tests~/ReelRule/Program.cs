using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AX.UdonSlot.REV1_0;
using AX.UdonSlot.REV1_0.Editor.ReelRule;

// 生成エンジンと設計データ変換の検証。終了コード 0 なら全項目通過。
public static partial class Program
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
        ModelChecks();
        var machine = SampleMachine();
        var design = SampleDesign(machine);
        var engine = new ReelRuleEngine(machine, ReelDesignConverter.ToRuleSet(design, machine));
        var results = engine.GenerateAll();
        GenerationChecks(machine, design, engine, results);
        ConditionChecks(machine);
        OverrideChecks(machine);
        RuntimeLookupChecks(results);
        ErrorChecks(machine);
        UnavoidableChecks();
        RealMachineChecks();
        if (args.Length > 0) File.WriteAllText(args[0], ReelRuleReport.Markdown(results, 15));
        Console.WriteLine(checks + " checks, " + failures + " failures, " + watch.ElapsedMilliseconds + " ms");
        return failures == 0 ? 0 : 1;
    }

    private static List<ControlResult> Generate(Machine machine, ReelDesign design)
    {
        return new ReelRuleEngine(machine, ReelDesignConverter.ToRuleSet(design, machine)).GenerateAll();
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

    private static double ShapeRateByOrder(ControlResult r, string shape, int order)
    {
        int i = r.Stats.ShapeNames.IndexOf(shape);
        return (double)r.Stats.ShapeMatchedByOrder[i, order] / r.Stats.PathsByOrder[order];
    }

    // ------------------------------------------------------------ 機種データと設計

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

    private static bool Throws(Machine machine, ReelDesign design)
    {
        try { Generate(machine, design); return false; }
        catch (RuleException) { return true; }
    }

    private static DesignShape ShapeOf(string name, bool reach, params string[] cells)
    {
        var shape = new DesignShape { name = name, reach = reach };
        for (int i = 0; i < 9; i++)
        {
            if (cells[i] == "*") continue;
            foreach (string part in cells[i].Split('|'))
            {
                if (part.StartsWith("!")) shape.cells[i].exclude.Add(part.Substring(1));
                else shape.cells[i].include.Add(part);
            }
        }
        return shape;
    }

    public static ReelDesign SampleDesign(Machine machine)
    {
        var d = new ReelDesign();
        d.AddControlsFromLottery(machine);
        d.shapes.Add(ShapeOf("右下がりスイカ", false, "スイカ", "*", "*", "*", "スイカ", "*", "*", "*", "スイカ"));
        var reach = ShapeOf("リーチ目A", true, "*", "*", "*", "7", "BAR", "*", "*", "*", "*");
        reach.allowedControls.Add("BB");
        reach.allowedControls.Add("REPLAY_BB");
        d.shapes.Add(reach);
        d.FindControl("スイカ").targets.Add("右下がりスイカ");
        d.FindControl("BB").targets.Add("リーチ目A");
        var replayBB = new DesignControl { name = "REPLAY_BB" };
        replayBB.lotteryFlags.Add("リプレイ");
        replayBB.extraRoles.Add("BB");
        d.controls.Add(replayBB);
        return d;
    }

    // ------------------------------------------------------------ 検証

    private static void ModelChecks()
    {
        var cell = new ShapeCell();
        Check("empty cell is any", cell.IsAny && cell.Matches("ベル"));
        cell.Include.Add("7");
        cell.Include.Add("BAR");
        Check("include matches any of them", cell.Matches("7") && cell.Matches("BAR") && !cell.Matches("ベル"));
        cell.Include.Clear();
        cell.Exclude.Add("BAR");
        cell.Exclude.Add("チェリー");
        Check("exclude only matches the rest", cell.Matches("7") && !cell.Matches("BAR") && !cell.Matches("チェリー"));
        cell.Include.Add("7");
        cell.Include.Add("BAR");
        Check("include and exclude combine", cell.Matches("7") && !cell.Matches("BAR") && !cell.Matches("ベル"));

        var d = new DesignCell();
        d.Cycle("7");
        Check("cycle to include", d.include.Contains("7") && d.exclude.Count == 0);
        d.Cycle("7");
        Check("cycle to exclude", d.exclude.Contains("7") && d.include.Count == 0);
        d.Cycle("7");
        Check("cycle to none", d.IsAny);

        var names = new HashSet<string> { "Replay", "Replay (1)" };
        Check("numbered name skips taken", ReelDesign.NumberedName("Replay", names.Contains) == "Replay (2)");

        var control = new DesignControl { name = "A" };
        control.targets.Add("S");
        control.overrides.Add(new DesignOverride { pattern = 32, press = 3, slip = 2 });
        var copy = control.Copy();
        copy.targets.Add("T");
        copy.overrides[0].slip = 4;
        Check("copy is deep", control.targets.Count == 1 && control.overrides[0].slip == 2);
    }

    private static void GenerationChecks(Machine machine, ReelDesign design, ReelRuleEngine engine, List<ControlResult> results)
    {
        Check("one result per control", results.Count == design.controls.Count);
        foreach (var r in results)
        {
            Check(r.Name + ": no violation", r.Stats.ViolationPaths == 0);
            Check(r.Name + ": no unavoidable", r.Stats.UnavoidableCells == 0);
            Check(r.Name + ": all paths", r.Stats.Paths == 6L * 21 * 21 * 21);
            foreach (var t in r.Tables)
                for (int p = 0; p < t.Slips.Length; p++)
                {
                    Check(r.Name + ": slip range", t.Slips[p] >= 0 && t.Slips[p] <= 4);
                    Check(r.Name + ": reason set", !string.IsNullOrEmpty(t.Reasons[p]));
                }
        }
        // 役だけの制御は、総当たりで求めた最大の入賞率と一致する
        Check("bell rate is optimal", Near(Rate(Find(results, "ベル"), "ベル"), Optimum(machine, "ベル", new[] { "ベル" })));
        Check("replay rate is optimal", Near(Rate(Find(results, "リプレイ"), "リプレイ"), Optimum(machine, "リプレイ", new[] { "リプレイ" })));
        Check("cherry rate is optimal", Near(Rate(Find(results, "チェリー"), "チェリー"), Optimum(machine, "チェリー", new[] { "チェリー" })));

        // 狙う出目は小役の引込より先
        var suika = Find(results, "スイカ");
        Check("target comes before small pull", suika.GoalLines[0] == "狙う 右下がりスイカ" && suika.GoalLines[1].StartsWith("避ける スイカ"));
        var plain = SampleDesign(machine);
        plain.FindControl("スイカ").targets.Clear();
        var untargeted = Find(Generate(machine, plain), "スイカ");
        Check("target raises shape rate", ShapeRate(suika, "右下がりスイカ") > ShapeRate(untargeted, "右下がりスイカ"));

        // 押し順限定の基本目を狙う制御でも、その押し順以外では小役を通常どおり引き込む
        var leftOnly = SampleDesign(machine);
        var middleBell = ShapeOf("中段ベル", false, "*", "*", "*", "ベル", "ベル", "ベル", "*", "*", "*");
        middleBell.firstReel = 0;
        leftOnly.shapes.Add(middleBell);
        leftOnly.FindControl("ベル").targets.Add("中段ベル");
        var bellLeft = Find(Generate(machine, leftOnly), "ベル");
        var bellPlain = Find(results, "ベル");
        int bellIndex = Array.IndexOf(bellLeft.Stats.RoleNames, "ベル");
        for (int o = 2; o < 6; o++)
            Check("other orders keep pulling bell " + o, bellLeft.Stats.RoleFormedByOrder[bellIndex, o] == bellPlain.Stats.RoleFormedByOrder[bellIndex, o]);

        // 押し順ベル: 停止順つきの15枚役は正解押し順で、それ以外は1枚役（抽選表の並び順によらない）
        foreach (bool highFirst in new[] { true, false })
        {
            var pm = SampleMachine();
            var b15 = RoleOf("ベル15", "リプ", "ベル", "リプ", 15);
            b15.StopOrder = new[] { 1, 0, 0 };
            pm.Roles.Add(b15);
            pm.Roles.Add(RoleOf("ベル1", "ベル", "リプ", "ベル", 1));
            if (highFirst) AddFlag(pm, "押し順ベル", "ベル15", "ベル1"); else AddFlag(pm, "押し順ベル", "ベル1", "ベル15");
            var pr = Find(Generate(pm, SampleDesign(pm)), "押し順ベル");
            int i15 = Array.IndexOf(pr.Stats.RoleNames, "ベル15"), i1 = Array.IndexOf(pr.Stats.RoleNames, "ベル1");
            for (int o = 0; o < 6; o++)
            {
                bool correct = o < 2;
                Check("push-order bell " + highFirst + " " + o, correct
                    ? pr.Stats.RoleFormedByOrder[i15, o] == pr.Stats.PathsByOrder[o]
                    : pr.Stats.RoleFormedByOrder[i15, o] == 0 && pr.Stats.RoleFormedByOrder[i1, o] > 0);
            }
        }

        // 範囲外の手動指定・出目設定はエラーになる
        var badOverride = SampleDesign(machine);
        badOverride.FindControl("ベル").overrides.Add(new DesignOverride { pattern = 0, press = 5, slip = 9 });
        Check("out-of-range override is an error", Throws(machine, badOverride));
        var badShape = SampleDesign(machine);
        badShape.shapes[0].firstReel = 5;
        Check("bad first reel is an error", Throws(machine, badShape));

        // リーチ目は出てよい制御以外で出ない
        foreach (var r in results)
            if (r.Name != "BB" && r.Name != "REPLAY_BB")
                Check(r.Name + ": reach shape never appears", ShapeRate(r, "リーチ目A") == 0);
        Check("BB shows reach shape", ShapeRate(Find(results, "BB"), "リーチ目A") > 0);

        var replayBB = Find(results, "REPLAY_BB");
        Check("extra roles are won", replayBB.WonRoles.Contains("リプレイ") && replayBB.WonRoles.Contains("BB"));
        Check("bonus first pulls BB first", replayBB.GoalLines[0] == "引込 BB");
        Check("bonus first pulls BB optimally", Near(Rate(replayBB, "BB"), Optimum(machine, "BB", new[] { "リプレイ", "BB" })));
        var small = SampleDesign(machine);
        small.priority = ControlPriority.SmallFirst;
        var replayBBSmall = Find(Generate(machine, small), "REPLAY_BB");
        Check("small first pulls replay first", replayBBSmall.GoalLines[0] == "引込 リプレイ");
        Check("small first pulls replay optimally", Near(Rate(replayBBSmall, "リプレイ"), Optimum(machine, "リプレイ", new[] { "リプレイ", "BB" })));

        // 決め手
        bool bellReason = false;
        foreach (var t in Find(results, "ベル").Tables) foreach (string reason in t.Reasons) if (reason == "ベル引込") bellReason = true;
        Check("bell reason is pull", bellReason);
        bool hazureClean = true;
        foreach (var t in Find(results, "ハズレ").Tables) foreach (string reason in t.Reasons) if (reason != "最短" && reason != "回避") hazureClean = false;
        Check("hazure reasons are shortest or avoid", hazureClean);

        // 出目マップのたどり方は全経路シミュレーションと一致する
        var bbResult = Find(results, "BB");
        long matched = 0, paths = 0;
        for (int o = 0; o < 6; o++)
        {
            var firstTable = engine.FindTable(bbResult, ReelRuleEngine.PatternFor(o, 0), new[] { -1, -1, -1 });
            int first = ReelRuleEngine.Orders[o][0];
            for (int p1 = 0; p1 < 21; p1++)
            {
                int stop = Machine.Wrap(p1 - firstTable.Slips[p1], 21);
                for (int p2 = 0; p2 < 21; p2++)
                    for (int p3 = 0; p3 < 21; p3++)
                    {
                        int[] orders;
                        var stops = engine.Resolve(bbResult, o, stop, p2, p3, out orders);
                        paths++;
                        if (stops != null && engine.MatchedShapes(stops, orders).Contains("リーチ目A")) matched++;
                    }
            }
            Check("first stops reachable", engine.FirstStops(bbResult, o)[Machine.Wrap(0 - firstTable.Slips[0], 21)]);
        }
        Check("resolve matches simulation", paths == bbResult.Stats.Paths && matched == bbResult.Stats.ShapeMatched[bbResult.Stats.ShapeNames.IndexOf("リーチ目A")]);

        var again = Generate(machine, SampleDesign(machine));
        bool same = again.Count == results.Count;
        for (int i = 0; same && i < results.Count; i++)
        {
            same = again[i].Tables.Count == results[i].Tables.Count;
            for (int t = 0; same && t < results[i].Tables.Count; t++)
                same = string.Join(",", again[i].Tables[t].Slips) == string.Join(",", results[i].Tables[t].Slips);
        }
        Check("deterministic", same);
    }

    private static void ConditionChecks(Machine machine)
    {
        // 押し順: 左第1停止のときだけ有効な出目は、他の押し順では数えない（狙いもしない）
        var d = SampleDesign(machine);
        var leftOnly = ShapeOf("左だけスイカ", false, "スイカ", "*", "*", "*", "スイカ", "*", "*", "*", "スイカ");
        leftOnly.firstReel = 0;
        d.shapes.Add(leftOnly);
        d.FindControl("スイカ").targets.Insert(0, "左だけスイカ");
        var r = Find(Generate(machine, d), "スイカ");
        Check("first reel shape appears in left orders", ShapeRateByOrder(r, "左だけスイカ", 0) > 0);
        for (int o = 2; o < 6; o++) Check("first reel shape absent in order " + o, ShapeRateByOrder(r, "左だけスイカ", o) == 0);

        // 停止位置: 指定した位置で止まったときだけ合う
        d = SampleDesign(machine);
        var mission = ShapeOf("ミッション", true, "*", "*", "*", "*", "*", "*", "*", "*", "*");
        mission.specificPositions = true;
        mission.leftPositions.Add(3);
        mission.centerPositions.Add(5);
        mission.centerPositions.Add(6);
        mission.allowedControls.Add("BB");
        d.shapes.Add(mission);
        d.FindControl("BB").targets.Insert(0, "ミッション");
        var engine = new ReelRuleEngine(machine, ReelDesignConverter.ToRuleSet(d, machine));
        var results = engine.GenerateAll();
        var bb = Find(results, "BB");
        Check("mission shape reachable", ShapeRate(bb, "ミッション") > 0);
        foreach (var other in results)
            if (other.Name != "BB") Check(other.Name + ": mission never appears", ShapeRate(other, "ミッション") == 0);
        bool positionsOk = true;
        for (int stop = 0; stop < 21; stop++)
            for (int p2 = 0; p2 < 21; p2++)
                for (int p3 = 0; p3 < 21; p3++)
                {
                    int[] orders;
                    var stops = engine.Resolve(bb, 0, stop, p2, p3, out orders);
                    if (stops == null || !engine.MatchedShapes(stops, orders).Contains("ミッション")) continue;
                    if (stops[0] != 3 || (stops[1] != 5 && stops[1] != 6)) positionsOk = false;
                }
        Check("mission only at given positions", positionsOk);
    }

    private static void OverrideChecks(Machine machine)
    {
        var d = SampleDesign(machine);
        int pattern = ReelRuleEngine.PatternFor(0, 0);
        d.FindControl("スイカ").overrides.Add(new DesignOverride { pattern = pattern, press = 5, slip = 3 });
        var engine = new ReelRuleEngine(machine, ReelDesignConverter.ToRuleSet(d, machine));
        var r = Find(engine.GenerateAll(), "スイカ");
        var table = engine.FindTable(r, pattern, new[] { -1, -1, -1 });
        Check("override applied", table.Slips[5] == 3 && table.Manual[5] && table.Reasons[5] == "手動");
        Check("other cells not manual", !table.Manual[4] && table.Reasons[4] != "手動");
        Check("override keeps all paths", r.Stats.Paths == 6L * 21 * 21 * 21);
    }

    // ベイクした配列を実機と同じ検索（条件付き→基本テーブル）でたどり、生成結果と一致するか
    private static void RuntimeLookupChecks(List<ControlResult> results)
    {
        foreach (var r in results)
        {
            var baked = ReelRuleBaker.Bake(r, new[] { 21, 21, 21 });
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

    private static void ErrorChecks(Machine machine)
    {
        var cases = new List<KeyValuePair<string, ReelDesign>>();
        var d = SampleDesign(machine);
        d.controls.Add(new DesignControl { name = "ベル" });
        cases.Add(new KeyValuePair<string, ReelDesign>("duplicate control", d));
        d = SampleDesign(machine);
        d.FindControl("ベル").targets.Add("無い出目");
        cases.Add(new KeyValuePair<string, ReelDesign>("unknown target", d));
        d = SampleDesign(machine);
        d.FindControl("ベル").targets.Add("リーチ目A");
        cases.Add(new KeyValuePair<string, ReelDesign>("reach target not allowed", d));
        d = SampleDesign(machine);
        d.FindControl("ベル").lotteryFlags.Add("謎");
        cases.Add(new KeyValuePair<string, ReelDesign>("unknown lottery flag", d));
        d = SampleDesign(machine);
        d.shapes.Add(ShapeOf("金", false, "金", "*", "*", "*", "*", "*", "*", "*", "*"));
        cases.Add(new KeyValuePair<string, ReelDesign>("symbol not on strip", d));
        d = SampleDesign(machine);
        d.FindControl("ベル").extraRoles.Add("無い役");
        cases.Add(new KeyValuePair<string, ReelDesign>("unknown extra role", d));
        foreach (var c in cases)
        {
            bool thrown = false;
            try { Generate(machine, c.Value); }
            catch (RuleException e) { thrown = e.Errors.Count > 0; }
            Check("error detected: " + c.Key, thrown);
        }
    }

    private static void UnavoidableChecks()
    {
        // 全コマ同じ図柄なら、その役のハズレは避けられない
        var m = new Machine();
        for (int r = 0; r < 3; r++) m.Strips[r] = new[] { "A", "A", "A", "A", "A", "A", "A", "A" };
        m.Lines.Add(new PayLine { Rows = new[] { 1, 1, 1 }, BetMask = 7 });
        m.Roles.Add(RoleOf("AAA", "A", "A", "A", 1));
        AddFlag(m, "ハズレ");
        var d = new ReelDesign();
        d.AddControlsFromLottery(m);
        var r0 = Generate(m, d)[0];
        Check("unavoidable reported", r0.Stats.UnavoidableCells > 0 && r0.Stats.ViolationPaths == r0.Stats.Paths);
    }

    private static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-9; }

    // ---- エンジンとは独立した総当たり（役、または右下がりスイカの最大入賞率）
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

    private static bool OFormed(Role role, int[] st)
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

    private static bool OMet(int[] st)
    {
        return OFormed(om.FindRole(oTarget), st);
    }

    private static bool OBad(int[] st)
    {
        foreach (var role in om.Roles) if (!oWon.Contains(role.Name) && OFormed(role, st)) return true;
        // リーチ目A（中段 7 BAR *）は BB 当選中以外で禁止
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
        if (stage == 3) result = !OBad(st);
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
        if (stage == 3) result = OMet(st) && !OBad(st) ? 1 : 0;
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
}
