using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>Runs editor tables, including conditional later stops, for every pressed-position triple.</summary>
    public sealed class ReelControlValidator
    {
        public sealed class Issue
        {
            public string kind, message;
            public int order, stage, press, slip, alternative = -1;
            public int[] context, presses, stops, alternativeStops;
        }
        public sealed class Report
        {
            public readonly List<Issue> issues = new List<Issue>();
            public int paths, wins;
            public bool provisional;
        }
        private sealed class Outcome
        {
            public bool allowed;
            public string forbidden = "";
        }
        private ReelControlDesign design;
        private PayoutTable payout;
        private HashSet<string> roles;
        private int bet, maxSlip;
        private readonly Dictionary<string, Outcome> outcomes = new Dictionary<string, Outcome>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private Report report;
        private bool lastSimulationProvisional;

        public Report Run(ReelControlDesign source, PayoutTable table, IEnumerable<string> targets,
            int betValue, int maximumSlip, int selectedOrder, bool allOrders, Func<bool> cancel = null)
        {
            design = source; payout = table; roles = new HashSet<string>(targets);
            bet = betValue; maxSlip = maximumSlip;
            report = new Report(); seen.Clear();
            if (design == null || payout == null || !design.laterStopsInitialized)
                throw new InvalidOperationException("編集データ・配当表・後段テーブルが必要です。");
            if (bet < 1 || bet > 3 || maxSlip < 0 || maxSlip > 4 || selectedOrder < 0 || selectedOrder > 5)
                throw new InvalidOperationException("BET・最大滑り・押し順が範囲外です。");
            foreach (string role in roles) if (Array.IndexOf(payout.winPairNames, role) < 0)
                throw new InvalidOperationException("配当表にない対象役: " + role);
            for (int p = 0; p < payout.GetWinPairCount(); p++)
                if (!payout.IsWinPairValid(p)) throw new InvalidOperationException("配当表の役定義が不正: " + p);
            int activeLines = 0;
            for (int l = 0; l < payout.GetPayLineCount(); l++)
            {
                if (!payout.IsPayLineValid(l)) throw new InvalidOperationException("配当表のライン定義が不正: " + l);
                for (int r = 0; r < 3; r++)
                    if (payout.GetPayLineRow(l, r) < 0 || payout.GetPayLineRow(l, r) > 2)
                        throw new InvalidOperationException("配当表の段が範囲外: " + l);
                if (payout.IsPayLineActive(l, bet)) activeLines++;
            }
            if (activeLines == 0) throw new InvalidOperationException("指定BETで有効なラインがありません。");
            for (int r = 0; r < 3; r++) CheckValues(design.reels[r].slips, design.reels[r].symbols.Length);
            foreach (var t in design.laterStops)
            {
                int reel = 0;
                for (int r = 1; r < 3; r++)
                    if (((t.pattern >> ((2-r)*2)) & 3) > ((t.pattern >> ((2-reel)*2)) & 3)) reel = r;
                CheckValues(design.BaseValues(t), design.reels[reel].symbols.Length);
                foreach (var c in t.conditions) CheckValues(design.ConditionValues(t, c), design.reels[reel].symbols.Length);
            }
            for (int order = allOrders ? 0 : selectedOrder; order <= (allOrders ? 5 : selectedOrder); order++)
            {
                outcomes.Clear();
                for (int l = 0; l < design.reels[0].symbols.Length; l++)
                    for (int c = 0; c < design.reels[1].symbols.Length; c++)
                        for (int r = 0; r < design.reels[2].symbols.Length; r++)
                        {
                            if ((report.paths & 63) == 0 && cancel != null && cancel())
                                throw new OperationCanceledException("検証を中止しました。結果は破棄しました。");
                            int[] presses = {l,c,r};
                            var stops = Simulate(order, presses, -1, 0);
                            var actual = Evaluate(order, stops);
                            report.paths++;
                            if (actual.allowed) report.wins++;
                            var context = new[] {-1,-1,-1};
                            for (int stage = 0; stage < 3; stage++)
                            {
                                int reel = ReelControlDesign.StopOrders[order][stage];
                                int slip = design.SlipsFor(order, stage, context)[presses[reel]];
                                if (slip == -1) Add("未設定", "暫定0コマで検証しています。", order, stage, context, presses, stops, slip);
                                else if (slip > maxSlip) Add("最大滑り超過", "設定した検証用最大滑りを超えています。", order, stage, context, presses, stops, slip);
                                if (stage == 2 && actual.forbidden.Length > 0)
                                    Add("対象外入賞", actual.forbidden, order, stage, context, presses, stops, slip);
                                if (!actual.allowed && roles.Count > 0)
                                    for (int alternate = 0; alternate <= maxSlip; alternate++)
                                    {
                                        if (alternate == Math.Max(0, slip)) continue;
                                        int[] alternativeStops = Simulate(order, presses, stage, alternate);
                                        bool provisionalAlternative = lastSimulationProvisional;
                                        var possible = Evaluate(order, alternativeStops);
                                        if (!possible.allowed || possible.forbidden.Length > 0) continue;
                                        if (provisionalAlternative) report.provisional = true;
                                        Add("引込経路あり", "同じ3本の押下位置で、この停止の滑りだけを変更すると対象役が成立します。後段は条件付きテーブルで再計算。" +
                                            (provisionalAlternative ? " 代替経路は未設定の暫定0コマを含みます。" : ""), order,
                                            stage, context, presses, stops, slip, alternate, alternativeStops);
                                        break;
                                    }
                                context[reel] = stops[reel];
                            }
                        }
            }
            return report;
        }
        private void CheckValues(int[] values, int count)
        {
            if (count == 0 || values == null || values.Length != count)
                throw new InvalidOperationException("滑りテーブルとリール配列の長さが一致しません。");
            foreach (int v in values) if (v < -1 || v > 4) throw new InvalidOperationException("滑り値が範囲外です。");
        }
        private int[] Simulate(int order, int[] presses, int changeStage, int changeSlip)
        {
            var stops = new[] {-1,-1,-1};
            lastSimulationProvisional = false;
            for (int s = 0; s < 3; s++)
            {
                int reel = ReelControlDesign.StopOrders[order][s];
                int slip = s == changeStage ? changeSlip : design.SlipsFor(order, s, stops)[presses[reel]];
                if (slip < 0) lastSimulationProvisional = true;
                stops[reel] = ReelControlDesign.Normalize(presses[reel] - Math.Max(0, slip), design.reels[reel].symbols.Length);
            }
            return stops;
        }
        private bool Matches(string actual, string required)
        {
            if (string.IsNullOrEmpty(required)) return true;
            foreach (var option in required.Split(',')) if (actual == option.Trim(' ', '　', '\t')) return true;
            return false;
        }
        private Outcome Evaluate(int order, int[] stops)
        {
            string key = string.Join(",", stops);
            Outcome result;
            if (outcomes.TryGetValue(key, out result)) return result;
            result = new Outcome();
            for (int p = 0; p < payout.GetWinPairCount(); p++)
            {
                bool orderMatches = true;
                if (!payout.isReelStopOrderAny[p])
                    for (int s = 0; s < 3; s++)
                        if (payout.GetStopOrder(p, ReelControlDesign.StopOrders[order][s]) != s + 1) orderMatches = false;
                if (!orderMatches) continue;
                for (int line = 0; line < payout.GetPayLineCount(); line++)
                {
                    if (!payout.IsPayLineActive(line, bet)) continue;
                    bool match = true;
                    for (int r = 0; r < 3; r++)
                    {
                        var symbols = design.reels[r].symbols;
                        string actual = symbols[ReelControlDesign.Normalize(stops[r] + payout.GetPayLineRow(line, r) - 2, symbols.Length)];
                        if (!Matches(actual, payout.GetSymbol(p,r))) match = false;
                    }
                    if (!match) continue;
                    if (roles.Contains(payout.winPairNames[p])) result.allowed = true;
                    else result.forbidden += payout.GetWinPairDisplayName(payout.winPairNames[p]) + " / ライン" + (line+1) +
                        " / " + payout.GetPayout(p,bet) + "枚; ";
                }
            }
            outcomes.Add(key, result);
            return result;
        }
        private void Add(string kind, string message, int order, int stage, int[] context, int[] presses,
            int[] stops, int slip, int alternative = -1, int[] alternativeStops = null)
        {
            int reel = ReelControlDesign.StopOrders[order][stage];
            string key = order + "/" + stage + "/" + string.Join(",",context) + "/" + presses[reel] + "/" + kind;
            if (kind == "未設定") report.provisional = true;
            if (!seen.Add(key)) return;
            report.issues.Add(new Issue { kind = kind, message = message, order = order, stage = stage,
                context = (int[])context.Clone(), presses = (int[])presses.Clone(), stops = (int[])stops.Clone(),
                press = presses[reel], slip = slip, alternative = alternative, alternativeStops = alternativeStops });
        }
    }
}
