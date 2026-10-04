using System;
using System.Collections.Generic;
namespace AX.UdonSlot.REV1_0.Editor
{
    // Shared editor approximation; does not call or mutate scene runtime state.
    public class ReelSlipEvaluator
    {
        public ReelOutputController controller;
        public PayoutTable payout;
        public int Pattern, bet = 3, maximumSlip = 4;
        public int[] positions = {-1, -1, -1};
        public ReelControlPriority priority;
        public ReelRolePriority rolePriority;
        public HashSet<string> allowedRoles = new HashSet<string>();
        private int Order(int reel) { return (Pattern >> ((2 - reel) * 2)) & 3; }
        private int TargetReel { get { int r = 0; for (int i = 1; i < 3; i++) if (Order(i) > Order(r)) r = i; return r; } }
        private int Wrap(int p, int r) { int n = controller.reelDatas[r].symbols.Length; return (p % n + n) % n; }
        private string Symbol(int r, int lower, int row) { return controller.reelDatas[r].symbols[Wrap(lower + row - 2, r)]; }
        public sealed class Candidate
        {
            public int forbidden;
            public int preferred;
            public int level;
            public int payout;
            public int combinations;
            public string roles;
        }
        private bool Matches(string actual, string required)
        {
            if (string.IsNullOrEmpty(required)) return true;
            foreach (string option in required.Split(',')) if (option.Trim(' ', '　', '\t') == actual) return true;
            return false;
        }
        private bool StopOrderPossible(int pair)
        {
            if (payout.isReelStopOrderAny[pair]) return true;
            int current = Order(TargetReel);
            for (int r = 0; r < 3; r++)
            {
                int required = payout.GetStopOrder(pair, r);
                if (Order(r) > 0) { if (required != Order(r)) return false; }
                else if (required <= current || required > 3) return false;
            }
            return true;
        }
        // 配当表の図柄条件・押し順・BETを使った編集用評価。未停止リールは配列上の到達可能性を調べる。
        public Candidate Evaluate(int pressed, int slip)
        {
            Candidate result = new Candidate();
            int[] fullCount = new int[2], fullPayout = new int[2], reelCount = new int[2], reelPayout = new int[2];
            List<string> roleNames = new List<string>();
            int lower = Wrap(pressed - slip, TargetReel);
            for (int p = 0; p < payout.GetWinPairCount(); p++)
            {
                if (!payout.IsWinPairValid(p) || !StopOrderPossible(p)) continue;
                bool allowed = allowedRoles.Contains(payout.winPairNames[p]);
                int group = (rolePriority == ReelRolePriority.BonusFirst ? payout.IsBonus(p) : !payout.IsBonus(p)) ? 1 : 0;
                for (int line = 0; line < payout.GetPayLineCount(); line++)
                {
                    if (!payout.IsPayLineValid(line) || !payout.IsPayLineActive(line, bet)) continue;
                    bool possible = true, forced = true, targetMatch = false;
                    for (int r = 0; r < 3; r++)
                    {
                        int row = payout.GetPayLineRow(line, r);
                        if (row < 0 || row > 2) { possible = false; forced = false; continue; }
                        string required = payout.GetSymbol(p, r);
                        if (r == TargetReel)
                        {
                            bool match = Matches(Symbol(r, lower, row), required);
                            targetMatch = !string.IsNullOrEmpty(required) && match;
                            possible &= match; forced &= match;
                        }
                        else if (Order(r) > 0)
                        {
                            bool match = Matches(Symbol(r, positions[r], row), required);
                            possible &= match; forced &= match;
                        }
                        else if (!string.IsNullOrEmpty(required))
                        {
                            forced = false;
                            bool match = false;
                            foreach (string symbol in controller.reelDatas[r].symbols) if (Matches(symbol, required)) { match = true; break; }
                            possible &= match;
                        }
                    }
                    if (!allowed) { if (forced) result.forbidden++; continue; }
                    int coins = payout.GetPayout(p, bet);
                    if (targetMatch) { reelCount[group]++; reelPayout[group] = Math.Max(reelPayout[group], coins); }
                    if (possible)
                    {
                        fullCount[group]++; fullPayout[group] = Math.Max(fullPayout[group], coins);
                        string display = payout.GetWinPairDisplayName(payout.winPairNames[p]);
                        if (!roleNames.Contains(display)) roleNames.Add(display);
                    }
                }
            }
            result.preferred = fullCount[1] > 0 || reelCount[1] > 0 ? 1 : 0;
            int g = result.preferred;
            result.level = fullCount[g] > 0 ? 2 : reelCount[g] > 0 ? 1 : 0;
            result.payout = result.level == 2 ? fullPayout[g] : reelPayout[g];
            result.combinations = result.level == 2 ? fullCount[g] : reelCount[g];
            result.roles = roleNames.Count == 0 ? (result.level == 1 ? "対象リールのみ引込可能" : "対象役なし") : string.Join("、", roleNames);
            return result;
        }
        private bool Better(Candidate a, Candidate b)
        {
            if (a.forbidden != b.forbidden) return a.forbidden < b.forbidden;
            if (a.preferred != b.preferred) return a.preferred > b.preferred;
            if (a.level != b.level) return a.level > b.level;
            if (priority == ReelControlPriority.CombinationCount)
            {
                if (a.combinations != b.combinations) return a.combinations > b.combinations;
                return a.payout > b.payout;
            }
            if (a.payout != b.payout) return a.payout > b.payout;
            return a.combinations > b.combinations;
        }
        public int Recommend(int pressed)
        {
            int best = 0;
            Candidate result = Evaluate(pressed, 0);
            for (int slip = 1; slip <= maximumSlip; slip++)
            {
                Candidate next = Evaluate(pressed, slip);
                if (Better(next, result)) { best = slip; result = next; }
            }
            return best;
        }
    }
}
