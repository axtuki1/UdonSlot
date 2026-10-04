using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>Finite backward search; decisions depend only on exported runtime keys.</summary>
    public sealed class ReelOutcomeSolver
    {
        public sealed class Rating
        {
            public bool forbidden;
            public int role, level, primary, secondary;
        }
        public delegate Rating Evaluate(int pattern, int[] priorStops, int press, int slip);
        public sealed class Decision
        {
            public int pattern, reel;
            public int[] prior, slips;
            public bool safe;
            public double desirability;
        }
        public sealed class Result
        {
            public bool success;
            public readonly List<Decision> decisions = new List<Decision>();
            public readonly List<string> issues = new List<string>();
            public int states;
            public void WriteTo(ReelControlDesign design)
            {
                if (!success) throw new InvalidOperationException("制約を満たす制御がありません。反映しません。");
                design.commonSettingsEnabled = false;
                design.laterStopsInitialized = false;
                design.laterStops.Clear();
                design.InitializeLaterStops(null);
                foreach (var d in decisions)
                {
                    if (Array.TrueForAll(d.prior, p => p < 0)) design.reels[d.reel].slips = (int[])d.slips.Clone();
                    else design.LaterTable(d.pattern).conditions.Add(new ReelControlDesign.StopCondition {
                        left = d.prior[0], center = d.prior[1], right = d.prior[2], slips = (int[])d.slips.Clone() });
                }
            }
        }
        private ReelOutcomeRules book;
        private string target;
        private Evaluate evaluate;
        private Func<bool> cancel;
        private Result result;
        private readonly Dictionary<string, Decision> memo = new Dictionary<string, Decision>();
        private int visits;
        private static string Key(int pattern, int[] p) { return pattern + ":" + p[0] + "," + p[1] + "," + p[2]; }
        private int N(int r) { return book.reels[r].symbols.Length; }
        private static int Rank(Rating a, Rating b)
        {
            int c = a.role.CompareTo(b.role); if (c != 0) return c;
            c = a.level.CompareTo(b.level); if (c != 0) return c;
            c = a.primary.CompareTo(b.primary); return c != 0 ? c : a.secondary.CompareTo(b.secondary);
        }
        public Result Run(ReelOutcomeRules rules, string targetId, Evaluate rating, Func<bool> cancelled = null)
        {
            rules.Validate();
            if (!rules.targets.Exists(t => t.id == targetId) || rating == null) throw new ArgumentException("生成対象が不正です。");
            book = rules; target = targetId; evaluate = rating; cancel = cancelled;
            result = new Result { success = true }; memo.Clear(); visits = 0;
            var roots = new List<Decision>();
            for (int r = 0; r < 3; r++)
            {
                var d = Solve(0, 0, new[] {-1,-1,-1}, r);
                roots.Add(d);
                if (!d.safe)
                {
                    result.success = false;
                    for (int p = 0; p < d.slips.Length; p++) if (d.slips[p] < 0)
                        result.issues.Add(new[] {"左", "中", "右"}[r] + "を第1停止・" + (N(r)-p) +
                            "番押下: 全ての後続押し順・押下位置で禁止を回避できる滑りがありません。");
                }
            }
            result.states = memo.Count;
            if (result.success)
            {
                var seen = new HashSet<string>();
                foreach (var root in roots) Collect(root, 0, seen);
            }
            return result;
        }
        private Decision Solve(int previousPattern, int stage, int[] prior, int reel)
        {
            int pattern = previousPattern | ((stage + 1) << ((2-reel)*2));
            string key = Key(pattern, prior);
            Decision cached;
            if (memo.TryGetValue(key, out cached)) return cached;
            var d = new Decision { pattern = pattern, reel = reel, prior = (int[])prior.Clone(), slips = new int[N(reel)], safe = true };
            memo.Add(key, d);
            for (int press = 0; press < N(reel); press++)
            {
                if ((visits++ & 127) == 0 && cancel != null && cancel()) throw new OperationCanceledException("生成を中止しました。保存データは変更していません。");
                Rating best = null; double bestWant = -1; int bestSlip = -1;
                for (int slip = 0; slip <= book.maximumSlip; slip++)
                {
                    int[] next = (int[])prior.Clone(); next[reel] = ReelControlDesign.Normalize(press-slip, N(reel));
                    var rating = evaluate(pattern, prior, press, slip);
                    if (rating.forbidden) continue;
                    bool safe = true; double want = 0;
                    if (stage == 2)
                    {
                        foreach (var shape in book.shapes)
                        {
                            if (!shape.enabled || !shape.Matches(next,book.reels)) continue;
                            var mode = shape.For(target);
                            if (mode == ReelOutcomeRules.Mode.Forbidden) { safe = false; break; }
                            if (mode == ReelOutcomeRules.Mode.Preferred) want += 1;
                        }
                    }
                    else
                    {
                        int remaining = 0;
                        for (int r = 0; r < 3; r++) if (next[r] < 0)
                        {
                            var child = Solve(pattern, stage+1, next, r);
                            if (!child.safe) { safe = false; break; }
                            want += child.desirability; remaining++;
                        }
                        if (remaining > 0) want /= remaining;
                    }
                    if (!safe) continue;
                    int rank = best == null ? 1 : Rank(rating, best);
                    if (rank > 0 || (rank == 0 && want > bestWant))
                    { best = rating; bestWant = want; bestSlip = slip; }
                }
                d.slips[press] = bestSlip;
                if (bestSlip < 0) d.safe = false;
                else d.desirability += bestWant / N(reel);
            }
            return d;
        }
        private void Collect(Decision d, int stage, HashSet<string> seen)
        {
            if (!seen.Add(Key(d.pattern, d.prior))) return;
            result.decisions.Add(d);
            if (stage == 2) return;
            for (int p = 0; p < d.slips.Length; p++)
            {
                var next = (int[])d.prior.Clone(); next[d.reel] = ReelControlDesign.Normalize(p-d.slips[p], N(d.reel));
                for (int r = 0; r < 3; r++) if (next[r] < 0)
                    Collect(Solve(d.pattern, stage+1, next, r), stage+1, seen);
            }
        }
    }
}
