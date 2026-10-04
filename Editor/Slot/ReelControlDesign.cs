using System;
using System.Collections.Generic;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    /// <summary>編集専用。ランタイムから参照せず、明示的に停止テーブルへ出力する。</summary>
    public partial class ReelControlDesign : ScriptableObject
    {
        [Serializable]
        public class FirstStop
        {
            public string[] symbols = new string[0];
            // -1 means unassigned, unlike a deliberate zero-slip stop.
            public int[] slips = new int[0];
        }

        public string definitionName = "";
        public string notes = "";
        public string generationReport = "";
        public string controllerId = "";
        public string outputId = "";
        public FirstStop[] reels = new FirstStop[0];
        public string[] exportedSignatures = new string[3];
        [Serializable]
        public class StopCondition
        {
            public bool useCommon;
            public int left = -1, center = -1, right = -1;
            public int[] slips = new int[0];
            public int Position(int reel) { return reel == 0 ? left : reel == 1 ? center : right; }
            public bool Exact(int[] positions) { return left == positions[0] && center == positions[1] && right == positions[2]; }
        }
        [Serializable]
        public class LaterStop
        {
            public bool useCommon;
            public int pattern;
            public int[] slips = new int[0];
            public List<StopCondition> conditions = new List<StopCondition>();
            public string exportedDesign = "", exportedOutput = "";
        }
        public bool laterStopsInitialized;
        public List<LaterStop> laterStops = new List<LaterStop>();
        public static readonly int[][] StopOrders = {
            new[] {0, 1, 2}, new[] {0, 2, 1}, new[] {1, 0, 2},
            new[] {1, 2, 0}, new[] {2, 0, 1}, new[] {2, 1, 0}
        };
        public static int StagePattern(int order, int stage)
        {
            int pattern = 0;
            for (int s = 0; s <= stage; s++) pattern |= (s + 1) << ((2 - StopOrders[order][s]) * 2);
            return pattern;
        }

        public static int Pattern(int reel) { return 1 << ((2 - reel) * 2); }
        public static int Normalize(int index, int count) { return (index % count + count) % count; }
        public static int Slip(int press, int stop, int count) { return Normalize(press - stop, count); }
        public static int DisplayNumber(int index, int count) { return count - index; }

        public void Initialize(ReelOutputController controller, ReelControlData source)
        {
            reels = new FirstStop[3];
            exportedSignatures = new string[3];
            for (int r = 0; r < 3; r++)
            {
                string[] symbols = (string[])controller.reelDatas[r].symbols.Clone();
                FirstStop row = new FirstStop { symbols = symbols, slips = new int[symbols.Length] };
                int pattern = Pattern(r);
                bool enabled = source != null && source.IsReelStopTableStyle != null &&
                    pattern < source.IsReelStopTableStyle.Length && source.IsReelStopTableStyle[pattern];
                for (int p = 0; p < symbols.Length; p++)
                {
                    row.slips[p] = -1;
                    if (!enabled) continue;
                    int slip = source.GetConditionalSlip(pattern, -1, -1, -1, p);
                    if (slip < 0) slip = BaseSlip(source, pattern, p);
                    row.slips[p] = slip;
                }
                reels[r] = row;
            }
            InitializeLaterStops(source);
        }

        // Explicitly called for older editor assets. First-stop edits are never reimported.
        public void InitializeLaterStops(ReelControlData source)
        {
            if (laterStopsInitialized) return;
            var imported = new List<LaterStop>();
            for (int order = 0; order < StopOrders.Length; order++)
                for (int stage = 1; stage <= 2; stage++)
                {
                    int pattern = StagePattern(order, stage), count = reels[StopOrders[order][stage]].symbols.Length;
                    var table = new LaterStop { pattern = pattern, slips = new int[count] };
                    bool enabled = source != null && source.IsReelStopTableStyle != null &&
                        pattern < source.IsReelStopTableStyle.Length && source.IsReelStopTableStyle[pattern];
                    for (int p = 0; p < count; p++) table.slips[p] = enabled ? BaseSlip(source, pattern, p) : -1;
                    if (enabled && source.conditionPatterns != null)
                        for (int i = 0; i < source.conditionPatterns.Length; i++)
                        {
                            if (source.conditionPatterns[i] != pattern) continue;
                            ValidateCondition(source, i);
                            var condition = new StopCondition { left = source.conditionLeftPositions[i], center = source.conditionCenterPositions[i],
                                right = source.conditionRightPositions[i], slips = new int[count] };
                            for (int p = 0; p < count; p++)
                            {
                                int slip = p < source.conditionSlipLengths[i] ? source.conditionSlips[source.conditionSlipOffsets[i] + p] : -1;
                                // Runtime -1 falls directly back to the base, not a less-specific condition.
                                condition.slips[p] = slip < 0 ? table.slips[p] : slip;
                            }
                            table.conditions.Add(condition);
                        }
                    imported.Add(table);
                }
            laterStops = imported;
            laterStopsInitialized = true;
        }
        public LaterStop LaterTable(int pattern)
        {
            if (laterStops != null) foreach (var table in laterStops) if (table.pattern == pattern) return table;
            throw new InvalidOperationException("第2・第3停止の編集データを取り込んでください。");
        }
        public int[] SlipsFor(int order, int stage, int[] positions)
        {
            if (stage == 0) return reels[StopOrders[order][0]].slips;
            var table = LaterTable(StagePattern(order, stage));
            StopCondition best = null;
            int specificity = -1;
            foreach (var condition in table.conditions)
            {
                int score = 0;
                bool matches = true;
                for (int r = 0; r < 3; r++)
                    if (condition.Position(r) >= 0) { score++; if (condition.Position(r) != positions[r]) matches = false; }
                if (matches && score > specificity) { best = condition; specificity = score; }
            }
            return best == null ? BaseValues(table) : ConditionValues(table, best);
        }
        public void SetSlip(int order, int stage, int[] positions, int press, int slip)
        {
            if (slip < -1 || slip > 4) throw new ArgumentOutOfRangeException("slip");
            if (!IsReachable(order, stage, positions)) throw new InvalidOperationException("到達不能な停止条件は編集できません。");
            if (stage == 0) { reels[StopOrders[order][0]].slips[press] = slip; return; }
            var table = LaterTable(StagePattern(order, stage));
            StopCondition exact = null;
            foreach (var condition in table.conditions) if (condition.Exact(positions)) { exact = condition; break; }
            if (exact == null)
            {
                exact = new StopCondition { left = positions[0], center = positions[1], right = positions[2],
                    slips = (int[])SlipsFor(order, stage, positions).Clone() };
                table.conditions.Insert(0, exact);
            }
            if (exact.useCommon) { exact.slips = (int[])SlipsFor(order, stage, positions).Clone(); exact.useCommon = false; }
            exact.slips[press] = slip;
        }
        public List<int[]> ReachableContexts(int order, int stage)
        {
            var contexts = new List<int[]> { new[] {-1, -1, -1} };
            for (int s = 0; s < stage; s++)
            {
                int reel = StopOrders[order][s], count = reels[reel].symbols.Length;
                var next = new List<int[]>();
                foreach (var context in contexts)
                {
                    int[] values = SlipsFor(order, s, context);
                    var seen = new HashSet<int>();
                    for (int p = 0; p < count; p++)
                    {
                        if (values[p] < -1 || values[p] > 4) throw new InvalidOperationException("前段の滑り値が範囲外です。");
                        int destination = Normalize(p - Math.Max(0, values[p]), count);
                        if (!seen.Add(destination)) continue;
                        int[] result = (int[])context.Clone(); result[reel] = destination; next.Add(result);
                    }
                }
                contexts = next;
            }
            return contexts;
        }
        [Serializable]
        public class PendingReview
        {
            public int order, stage;
            public int[] positions, fromPositions;
            public int[] previousSlips;
            public string reason;
            public bool confirmed;
        }
        public List<PendingReview> reviews = new List<PendingReview>();
        public int PendingReviewCount { get { return reviews == null ? 0 : reviews.FindAll(r => !r.confirmed).Count; } }
        public class ContextSnapshot
        {
            public int order, stage;
            public int[] positions, slips;
        }
        private static bool SamePositions(int[] a, int[] b)
        {
            return a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
        }
        public List<ContextSnapshot> CaptureContexts()
        {
            var result = new List<ContextSnapshot>();
            if (!laterStopsInitialized) return result;
            for (int order = 0; order < 6; order++)
                for (int stage = 1; stage <= 2; stage++)
                    foreach (var context in ReachableContexts(order, stage))
                        result.Add(new ContextSnapshot { order = order, stage = stage,
                            positions = (int[])context.Clone(), slips = (int[])SlipsFor(order, stage, context).Clone() });
            return result;
        }
        // New reachable contexts may inherit a unique nearby source. Existing values always win.
        // Source snapshots remain intact even when their old conditions become unreachable.
        public void ReconcileContexts(List<ContextSnapshot> before)
        {
            if (!laterStopsInitialized) return;
            if (reviews == null) reviews = new List<PendingReview>();
            for (int stage = 1; stage <= 2; stage++)
                for (int order = 0; order < 6; order++)
                {
                    var old = before.FindAll(c => c.order == order && c.stage == stage);
                    if (!old.Exists(c => Array.Exists(c.slips, v => v >= 0))) continue;
                    foreach (var context in ReachableContexts(order, stage))
                    {
                        if (old.Exists(c => SamePositions(c.positions, context))) continue;
                        if (commonSettingsEnabled && UsesCommon(order, stage, context)) continue;
                        ContextSnapshot best = null;
                        int distance = int.MaxValue, ties = 0;
                        foreach (var candidate in old)
                        {
                            if (!Array.Exists(candidate.slips, v => v >= 0)) continue;
                            int d = 0;
                            for (int r = 0; r < 3; r++)
                            {
                                if (context[r] < 0) continue;
                                int n = reels[r].symbols.Length;
                                int delta = Math.Abs(context[r] - candidate.positions[r]);
                                d += Math.Min(delta, n - delta);
                            }
                            if (d < distance) { best = candidate; distance = d; ties = 1; }
                            else if (d == distance) ties++;
                        }
                        var values = (int[])SlipsFor(order, stage, context).Clone();
                        int copied = 0;
                        bool unique = best != null && distance <= 2 && ties == 1;
                        if (unique)
                            for (int p = 0; p < values.Length; p++)
                                if (values[p] == -1 && best.slips[p] >= 0 && best.slips[p] <= 4)
                                { values[p] = best.slips[p]; copied++; }
                        if (copied > 0)
                        {
                            var table = LaterTable(StagePattern(order, stage));
                            var exact = table.conditions.Find(c => c.Exact(context));
                            if (exact != null) { exact.slips = values; exact.useCommon = false; }
                            else table.conditions.Insert(0, new StopCondition { left = context[0], center = context[1],
                                right = context[2], slips = values });
                        }
                        var review = reviews.Find(r => r.order == order && r.stage == stage && SamePositions(r.positions, context));
                        if (review == null) { review = new PendingReview { order = order, stage = stage, positions = (int[])context.Clone() }; reviews.Add(review); }
                        review.confirmed = false;
                        review.fromPositions = unique ? (int[])best.positions.Clone() : null;
                        review.previousSlips = unique ? (int[])best.slips.Clone() : null;
                        review.reason = unique ? distance + "コマ差の条件から未設定 " + copied + "マスを補完。設定済みの値は保持。" :
                            ties > 1 && distance <= 2 ? "同じ距離の候補が複数あり、補完先を決められません。既存値を保持し、未設定は暫定0コマ。" :
                            "2コマ以内に引き継げる条件がありません。既存値を保持し、未設定は暫定0コマ。";
                    }
                }
        }
        public void SetSlipWithReview(int order, int stage, int[] positions, int press, int slip)
        {
            if (SlipsFor(order, stage, positions)[press] == slip) return;
            var before = CaptureContexts();
            SetSlip(order, stage, positions, press, slip);
            ReconcileContexts(before);
        }
        public bool IsReachable(int order, int stage, int[] positions)
        {
            foreach (var context in ReachableContexts(order, stage))
                if (context[0] == positions[0] && context[1] == positions[1] && context[2] == positions[2]) return true;
            return false;
        }
        // Called on an isolated preview. Earlier generated stages determine later reachability.
        public int FillUnset(int order, int firstStage, int lastStage, int[] selectedContext,
            Func<int, int[], int, int> recommend)
        {
            int changed = 0;
            for (int stage = firstStage; stage <= lastStage; stage++)
            {
                var contexts = ReachableContexts(order, stage);
                foreach (var context in contexts)
                {
                    if (selectedContext != null && (context[0] != selectedContext[0] ||
                        context[1] != selectedContext[1] || context[2] != selectedContext[2])) continue;
                    int[] original = SlipsFor(order, stage, context);
                    int[] values = (int[])original.Clone();
                    bool any = false;
                    for (int press = 0; press < values.Length; press++)
                    {
                        if (values[press] < -1 || values[press] > 4) throw new InvalidOperationException("滑り値が範囲外です。");
                        if (values[press] != -1) continue;
                        int value = recommend(stage, context, press);
                        if (value < 0 || value > 4) throw new InvalidOperationException("推奨値が範囲外です。");
                        values[press] = value; changed++; any = true;
                    }
                    if (!any) continue;
                    if (stage == 0) reels[StopOrders[order][0]].slips = values;
                    else
                    {
                        var table = LaterTable(StagePattern(order, stage));
                        var exact = table.conditions.Find(c => c.Exact(context));
                        if (exact != null) { exact.slips = values; exact.useCommon = false; }
                        else table.conditions.Insert(0, new StopCondition { left = context[0], center = context[1],
                            right = context[2], slips = values });
                    }
                }
            }
            return changed;
        }
        public static int[] ProvisionalValues(int[] source)
        {
            var result = (int[])source.Clone();
            for (int p = 0; p < result.Length; p++)
            {
                if (result[p] < -1 || result[p] > 4) throw new InvalidOperationException("滑り値は未設定(-1)または0〜4コマで指定してください。");
                if (result[p] == -1) result[p] = 0;
            }
            return result;
        }
        private static void ValidateCondition(ReelControlData target, int i)
        {
            if (target.conditionLeftPositions == null || target.conditionCenterPositions == null || target.conditionRightPositions == null ||
                target.conditionSlipOffsets == null || target.conditionSlipLengths == null || target.conditionSlips == null ||
                i >= target.conditionLeftPositions.Length || i >= target.conditionCenterPositions.Length || i >= target.conditionRightPositions.Length ||
                i >= target.conditionSlipOffsets.Length || i >= target.conditionSlipLengths.Length)
                throw new InvalidOperationException("出力先の条件テーブルの配列が不整合です。");
            int start = target.conditionSlipOffsets[i], length = target.conditionSlipLengths[i];
            if (start < 0 || length < 0 || (long)start + length > target.conditionSlips.Length)
                throw new InvalidOperationException("出力先の条件テーブルの範囲が不正です。");
        }
        public void ApplyLaterOutput(ReelControlData target, int order, int stage)
        {
            if (stage < 1 || stage > 2) throw new ArgumentOutOfRangeException("stage");
            var table = LaterTable(StagePattern(order, stage));
            int pattern = table.pattern;
            int[] values = ProvisionalValues(BaseValues(table));
            int[][] bases = target.reelStopSlipCount == null ? new int[58][] : (int[][])target.reelStopSlipCount.Clone();
            if (bases.Length < 58) Array.Resize(ref bases, 58);
            bases[pattern] = values;
            bool[] enabled = target.IsReelStopTableStyle == null ? new bool[58] : (bool[])target.IsReelStopTableStyle.Clone();
            if (enabled.Length < 58) Array.Resize(ref enabled, 58);
            enabled[pattern] = true;
            var patterns = new List<int>(); var left = new List<int>(); var center = new List<int>(); var right = new List<int>();
            var offsets = new List<int>(); var lengths = new List<int>(); var flat = new List<int>();
            if (target.conditionPatterns != null)
                for (int i = 0; i < target.conditionPatterns.Length; i++)
                {
                    ValidateCondition(target, i);
                    if (target.conditionPatterns[i] == pattern) continue;
                    patterns.Add(target.conditionPatterns[i]); left.Add(target.conditionLeftPositions[i]);
                    center.Add(target.conditionCenterPositions[i]); right.Add(target.conditionRightPositions[i]);
                    offsets.Add(flat.Count); lengths.Add(target.conditionSlipLengths[i]);
                    for (int j = 0; j < target.conditionSlipLengths[i]; j++) flat.Add(target.conditionSlips[target.conditionSlipOffsets[i] + j]);
                }
            foreach (var condition in table.conditions)
            {
                var resolved = ConditionValues(table, condition);
                if (resolved.Length != values.Length) throw new InvalidOperationException("条件のコマ数がリール配列と一致しません。");
                int[] slips = ProvisionalValues(resolved);
                patterns.Add(pattern); left.Add(condition.left); center.Add(condition.center); right.Add(condition.right);
                offsets.Add(flat.Count); lengths.Add(slips.Length); flat.AddRange(slips);
            }
            target.reelStopSlipCount = bases; target.IsReelStopTableStyle = enabled;
            target.conditionPatterns = patterns.ToArray(); target.conditionLeftPositions = left.ToArray();
            target.conditionCenterPositions = center.ToArray(); target.conditionRightPositions = right.ToArray();
            target.conditionSlipOffsets = offsets.ToArray(); target.conditionSlipLengths = lengths.ToArray(); target.conditionSlips = flat.ToArray();
        }
        public string LaterSignature(int pattern)
        {
            var table = LaterTable(pattern);
            var resolved = new LaterStop { pattern = pattern, slips = BaseValues(table) };
            foreach (var c in table.conditions) resolved.conditions.Add(new StopCondition { left = c.left, center = c.center,
                right = c.right, slips = ConditionValues(table, c) });
            return JsonUtility.ToJson(resolved);
        }
        public static string OutputSignature(ReelControlData target, int pattern)
        {
            if (target == null) return "";
            var text = new System.Text.StringBuilder();
            text.Append(target.IsReelStopTableStyle != null && pattern < target.IsReelStopTableStyle.Length && target.IsReelStopTableStyle[pattern]);
            if (target.reelStopSlipCount != null && pattern < target.reelStopSlipCount.Length && target.reelStopSlipCount[pattern] != null)
                foreach (int slip in target.reelStopSlipCount[pattern]) text.Append(',').Append(slip);
            if (target.conditionPatterns != null)
                for (int i = 0; i < target.conditionPatterns.Length; i++)
                {
                    if (target.conditionPatterns[i] != pattern) continue;
                    ValidateCondition(target, i);
                    text.Append('|').Append(target.conditionLeftPositions[i]).Append(',').Append(target.conditionCenterPositions[i]).Append(',').Append(target.conditionRightPositions[i]);
                    for (int j = 0; j < target.conditionSlipLengths[i]; j++) text.Append(',').Append(target.conditionSlips[target.conditionSlipOffsets[i] + j]);
                }
            return text.ToString();
        }

        public bool Matches(ReelOutputController controller, int reel)
        {
            if (controller == null || controller.reelDatas == null || controller.reelDatas.Length <= reel ||
                controller.reelDatas[reel] == null || controller.reelDatas[reel].symbols == null ||
                reels == null || reels.Length <= reel || reels[reel] == null) return false;
            string[] a = reels[reel].symbols, b = controller.reelDatas[reel].symbols;
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length ||
                reels[reel].slips == null || reels[reel].slips.Length != a.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public string Signature(int reel)
        {
            return JsonUtility.ToJson(reels[reel]);
        }

        public int[] BuildOutput(int reel)
        {
            int[] result = (int[])reels[reel].slips.Clone();
            for (int p = 0; p < result.Length; p++)
            {
                if (result[p] < -1 || result[p] > 4) throw new InvalidOperationException("滑り値は未設定(-1)または0〜4コマで指定してください。");
                if (result[p] == -1) result[p] = 0;
            }
            return result;
        }

        public static int BaseSlip(ReelControlData data, int pattern, int press)
        {
            return data.reelStopSlipCount != null && pattern < data.reelStopSlipCount.Length &&
                data.reelStopSlipCount[pattern] != null && press < data.reelStopSlipCount[pattern].Length
                ? data.reelStopSlipCount[pattern][press] : 0;
        }

        public bool OutputMatches(ReelControlData target, int reel)
        {
            int pattern = Pattern(reel);
            if (target == null || target.IsReelStopTableStyle == null || pattern >= target.IsReelStopTableStyle.Length ||
                !target.IsReelStopTableStyle[pattern]) return false;
            foreach (int slip in reels[reel].slips) if (slip < -1 || slip > 4) return false;
            int[] expected = BuildOutput(reel);
            for (int p = 0; p < expected.Length; p++)
            {
                int slip = target.GetConditionalSlip(pattern, -1, -1, -1, p);
                if (slip < 0) slip = BaseSlip(target, pattern, p);
                if (slip != expected[p]) return false;
            }
            return true;
        }

        /// <summary>選択中の第1停止だけを書き出す。検証・配列構築を終えてから出力先を変更する。</summary>
        public void ApplyOutput(ReelControlData target, int reel)
        {
            int[] values = BuildOutput(reel);
            int pattern = Pattern(reel);
            int[][] tables = target.reelStopSlipCount == null ? new int[58][] : (int[][])target.reelStopSlipCount.Clone();
            if (tables.Length < 58) Array.Resize(ref tables, 58);
            tables[pattern] = values;
            bool[] enabled = target.IsReelStopTableStyle == null ? new bool[58] : (bool[])target.IsReelStopTableStyle.Clone();
            if (enabled.Length < 58) Array.Resize(ref enabled, 58);
            enabled[pattern] = true;
            int[] patterns = target.conditionPatterns ?? new int[0];
            int[] offsets = new int[patterns.Length], lengths = new int[patterns.Length];
            var flat = new List<int>();
            for (int i = 0; i < patterns.Length; i++)
            {
                if (target.conditionSlipOffsets == null || target.conditionSlipLengths == null || target.conditionSlips == null ||
                    i >= target.conditionSlipOffsets.Length || i >= target.conditionSlipLengths.Length)
                    throw new InvalidOperationException("出力先の条件テーブルの配列が不整合です。");
                int start = target.conditionSlipOffsets[i], length = target.conditionSlipLengths[i];
                if (start < 0 || length < 0 || (long)start + length > target.conditionSlips.Length)
                    throw new InvalidOperationException("出力先の条件テーブルの範囲が不正です。");
                offsets[i] = flat.Count;
                if (patterns[i] == pattern) { flat.AddRange(values); lengths[i] = values.Length; }
                else { for (int j = 0; j < length; j++) flat.Add(target.conditionSlips[start + j]); lengths[i] = length; }
            }
            target.reelStopSlipCount = tables;
            target.IsReelStopTableStyle = enabled;
            target.conditionSlipOffsets = offsets;
            target.conditionSlipLengths = lengths;
            target.conditionSlips = flat.ToArray();
        }
    }
}

