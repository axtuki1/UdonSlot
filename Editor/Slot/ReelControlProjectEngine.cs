using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor
{
    public sealed class ReelControlProjectEngine
    {
        public delegate bool Forbidden(int pattern, int[] prior, int press, int slip);
        private sealed class Node
        {
            public ReelControlProject.Table table;
            public double[] probabilities;
            public bool safe = true;
        }
        public sealed class Issue
        {
            public string kind, key;
            public int order, stage, press;
            public int[] prior;
            public int paths;
        }
        private ReelControlProject project;
        private ReelControlProject.Flag flag;
        private Forbidden forbidden;
        private Func<bool> cancel;
        private readonly Dictionary<string, Node> memo = new Dictionary<string, Node>();
        private Dictionary<string, ReelControlProject.Table> fixedTables;
        private int fixedThrough, visits;
        public List<ReelControlProject.Table> Generate(ReelControlProject source, ReelControlProject.Flag target,
            Forbidden invalid, Func<bool> cancelled = null, int preserveThrough = -1)
        {
            source.Validate(target);
            project = source; flag = target; forbidden = invalid; cancel = cancelled; fixedThrough = preserveThrough;
            memo.Clear(); visits = 0;
            fixedTables = Index(target.tables);
            var roots = new List<Node>();
            for (int r = 0; r < 3; r++) roots.Add(Solve(0, 0, new[] {-1,-1,-1}, r));
            var result = new List<ReelControlProject.Table>(); var seen = new HashSet<string>();
            foreach (var root in roots) Collect(root, seen, result);
            return result;
        }
        private void CheckCancel()
        { if ((visits++ & 127) == 0 && cancel != null && cancel()) throw new OperationCanceledException(); }
        public static Dictionary<string, ReelControlProject.Table> Index(List<ReelControlProject.Table> tables)
        {
            var result = new Dictionary<string, ReelControlProject.Table>();
            foreach (var table in tables) result.Add(table.Key, table); return result;
        }
        private Node Solve(int previousPattern, int stage, int[] prior, int reel)
        {
            int pattern = previousPattern | ((stage + 1) << ((2 - reel) * 2));
            string key = ReelControlProject.KeyFor(pattern, prior); Node cached;
            if (memo.TryGetValue(key, out cached)) return cached;
            int n = project.strips[reel].symbols.Length;
            var table = new ReelControlProject.Table {pattern = pattern, stage = stage, reel = reel, prior = (int[])prior.Clone(),
                generated = new int[n], manual = new int[n], conflict = new bool[n], impossible = new bool[n]};
            for (int p = 0; p < n; p++) table.manual[p] = -1;
            var node = new Node {table = table, probabilities = new double[flag.shapes.Count]}; memo.Add(key, node);
            ReelControlProject.Table fixedTable;
            bool locked = stage <= fixedThrough;
            if (locked && !fixedTables.TryGetValue(key, out fixedTable)) throw new InvalidOperationException("前段のテーブルがありません。");
            fixedTables.TryGetValue(key, out fixedTable);
            for (int press = 0; press < n; press++)
            {
                CheckCancel();
                int bestSlip = -1; double[] best = null;
                int start = locked ? fixedTable.Effective(press) : 0;
                int end = locked ? start : project.maximumSlip;
                if (start < 0 || end > project.maximumSlip) throw new InvalidOperationException("前段の滑りが不正です。");
                for (int slip = start; slip <= end; slip++)
                {
                    if (forbidden != null && forbidden(pattern, prior, press, slip)) continue;
                    var next = (int[])prior.Clone(); next[reel] = ReelControlProject.Wrap(press - slip, n);
                    var scores = new double[flag.shapes.Count]; bool safe = true;
                    if (stage == 2)
                    {
                        for (int i = 0; i < scores.Length; i++) if (flag.shapes[i].Matches(next, project.strips)) { scores[i] = 1; break; }
                    }
                    else
                    {
                        int remaining = 0;
                        for (int r = 0; r < 3; r++) if (next[r] < 0)
                        {
                            var child = Solve(pattern, stage + 1, next, r); safe &= child.safe; remaining++;
                            for (int i = 0; i < scores.Length; i++) scores[i] += child.probabilities[i];
                        }
                        for (int i = 0; i < scores.Length; i++) scores[i] /= remaining;
                    }
                    if (!safe) continue;
                    bool better = best == null;
                    if (best != null) for (int i = 0; i < scores.Length; i++)
                    {
                        if (Math.Abs(scores[i] - best[i]) < 1e-12) continue;
                        better = scores[i] > best[i]; break;
                    }
                    if (better) { best = scores; bestSlip = slip; }
                }
                table.generated[press] = bestSlip < 0 ? (locked ? start : 0) : bestSlip;
                table.impossible[press] = best != null && Array.TrueForAll(best, x => x == 0) && flag.shapes.TrueForAll(s => s.ConstrainsReel(reel));
                if (best == null) { node.safe = false; table.unsafePath = true; }
                else for (int i = 0; i < best.Length; i++) node.probabilities[i] += best[i] / n;
            }
            if (locked)
            {
                table.generated = (int[])fixedTable.generated.Clone(); table.manual = (int[])fixedTable.manual.Clone();
                table.conflict = (bool[])fixedTable.conflict.Clone();
            }
            return node;
        }
        private void Collect(Node node, HashSet<string> seen, List<ReelControlProject.Table> result)
        {
            var t = node.table; if (!seen.Add(t.Key)) return; result.Add(t);
            if (t.stage == 2) return;
            for (int p = 0; p < t.generated.Length; p++)
            {
                var next = (int[])t.prior.Clone(); next[t.reel] = ReelControlProject.Wrap(p - t.Effective(p), t.generated.Length);
                for (int r = 0; r < 3; r++) if (next[r] < 0) Collect(Solve(t.pattern, t.stage + 1, next, r), seen, result);
            }
        }
        public static void Merge(ReelControlProject.Flag flag, List<ReelControlProject.Table> next, int preserveThrough = -1)
        {
            var previous = Index(flag.tables); var nextIndex = Index(next);
            var detachedKeys = new HashSet<string>();
            foreach (var t in flag.detached) if (!previous.ContainsKey(t.Key)) { previous.Add(t.Key,t); detachedKeys.Add(t.Key); }
            foreach (var t in next)
            {
                ReelControlProject.Table old;
                if (t.stage <= preserveThrough || !previous.TryGetValue(t.Key,out old)) continue;
                for (int p = 0; p < t.generated.Length; p++) if (old.manual[p] >= 0)
                {
                    t.manual[p] = old.manual[p];
                    t.conflict[p] = old.conflict[p] || old.generated[p] != t.generated[p] || detachedKeys.Contains(t.Key);
                }
            }
            foreach (var t in next)
            {
                ReelControlProject.Table old;
                if (t.stage <= preserveThrough || !previous.TryGetValue(t.Key, out old)) continue;
                bool ancestryChanged = false;
                foreach (var ancestor in flag.tables)
                {
                    if (ancestor.stage >= t.stage) continue;
                    // Conservative context invalidation when any preceding policy changes.
                    bool applies = t.prior[ancestor.reel] >= 0;
                    for (int r = 0; r < 3; r++)
                    {
                        if (ancestor.prior[r] >= 0 && ancestor.prior[r] != t.prior[r]) applies = false;
                        int ancestorOrder = (ancestor.pattern >> ((2-r)*2)) & 3;
                        if (ancestorOrder > 0 && ancestorOrder != ((t.pattern >> ((2-r)*2)) & 3)) applies = false;
                    }
                    if (!applies) continue;
                    ReelControlProject.Table updated;
                    if (!nextIndex.TryGetValue(ancestor.Key, out updated)) { ancestryChanged = true; break; }
                    for (int p = 0; p < ancestor.generated.Length; p++)
                        if (ancestor.Effective(p) != updated.Effective(p)) { ancestryChanged = true; break; }
                    if (ancestryChanged) break;
                }
                for (int p = 0; p < t.generated.Length; p++) if (old.manual[p] >= 0)
                {
                    t.conflict[p] |= ancestryChanged;
                }
            }
            var detached = new List<ReelControlProject.Table>();
            foreach (var t in flag.detached) if (!nextIndex.ContainsKey(t.Key)) detached.Add(t);
            foreach (var t in flag.tables) if (!nextIndex.ContainsKey(t.Key) && Array.Exists(t.manual, x => x >= 0)) detached.Add(t);
            flag.tables = next; flag.detached = detached;
        }
        public static List<Issue> Verify(ReelControlProject project, ReelControlProject.Flag flag, Forbidden forbidden, Func<bool> cancelled = null)
        {
            project.Validate(flag); var tables = Index(flag.tables); var issues = new Dictionary<string, Issue>(); int visits = 0;
            for (int o = 0; o < 6; o++)
            for (int l = 0; l < project.strips[0].symbols.Length; l++)
            for (int c = 0; c < project.strips[1].symbols.Length; c++)
            for (int r = 0; r < project.strips[2].symbols.Length; r++)
            {
                if ((visits++ & 127) == 0 && cancelled != null && cancelled()) throw new OperationCanceledException();
                var presses = new[] {l,c,r}; var stops = new[] {-1,-1,-1};
                for (int s = 0; s < 3; s++)
                {
                    int reel = ReelControlProject.Orders[o][s], pattern = ReelControlProject.Pattern(o,s);
                    string key = ReelControlProject.KeyFor(pattern,stops); ReelControlProject.Table t;
                    string kind = null; int slip = 0;
                    if (!tables.TryGetValue(key, out t)) kind = "未生成";
                    else
                    {
                        slip = t.Effective(presses[reel]);
                        if (slip < 0 || slip > project.maximumSlip) kind = "滑り範囲外";
                        else if (t.conflict[presses[reel]]) kind = "補正競合";
                        else if (forbidden != null && forbidden(pattern,stops,presses[reel],slip)) kind = "非成立役入賞";
                    }
                    if (kind != null) { AddIssue(issues, kind,key,o,s,presses[reel],stops); break; }
                    var prior = (int[])stops.Clone(); stops[reel] = ReelControlProject.Wrap(presses[reel]-slip, project.strips[reel].symbols.Length);
                    if (s == 2 && !flag.shapes.Exists(shape => shape.Matches(stops,project.strips))) AddIssue(issues,"取りこぼし",key,o,s,presses[reel],prior);
                }
            }
            return new List<Issue>(issues.Values);
        }
        private static void AddIssue(Dictionary<string, Issue> issues, string kind, string key, int o, int s, int press, int[] prior)
        {
            string id = kind + ":" + key + ":" + press; Issue issue;
            if (!issues.TryGetValue(id,out issue)) issues.Add(id, issue = new Issue {kind=kind,key=key,order=o,stage=s,press=press,prior=(int[])prior.Clone()});
            issue.paths++;
        }
        public static void Bake(ReelControlProject project, ReelControlProject.Flag flag, ReelControlData target)
        {
            project.Validate(flag);
            if (flag.tables.Count == 0) throw new InvalidOperationException("未生成");
            if (Verify(project,flag,null).Exists(i => i.kind != "取りこぼし")) throw new InvalidOperationException("未生成・補正競合・滑り範囲外");
            var basis = new int[64][]; var styles = new bool[64];
            var patterns = new List<int>(); var left = new List<int>(); var center = new List<int>(); var right = new List<int>();
            var offsets = new List<int>(); var lengths = new List<int>(); var slips = new List<int>();
            foreach (var t in flag.tables)
            {
                var values = new int[t.generated.Length];
                for (int p = 0; p < values.Length; p++)
                {
                    values[p] = t.Effective(p);
                    if (t.conflict[p] || values[p] < 0 || values[p] > project.maximumSlip) throw new InvalidOperationException("補正競合・滑り範囲外");
                }
                styles[t.pattern] = true;
                if (basis[t.pattern] == null) basis[t.pattern] = new int[values.Length];
                if (t.stage == 0) basis[t.pattern] = values;
                else
                {
                    patterns.Add(t.pattern); left.Add(t.prior[0]); center.Add(t.prior[1]); right.Add(t.prior[2]);
                    offsets.Add(slips.Count); lengths.Add(values.Length); slips.AddRange(values);
                }
            }
            for (int i = 0; i < basis.Length; i++) if (basis[i] == null) basis[i] = new int[0];
            target.reelStopSlipCount = basis; target.IsReelStopTableStyle = styles;
            target.conditionPatterns = patterns.ToArray(); target.conditionLeftPositions = left.ToArray();
            target.conditionCenterPositions = center.ToArray(); target.conditionRightPositions = right.ToArray();
            target.conditionSlipOffsets = offsets.ToArray(); target.conditionSlipLengths = lengths.ToArray(); target.conditionSlips = slips.ToArray();
        }
    }
}
