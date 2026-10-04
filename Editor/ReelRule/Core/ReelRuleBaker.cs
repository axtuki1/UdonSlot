using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>ReelControlData へそのまま代入できる配列一式。</summary>
    public sealed class BakedControl
    {
        public int[][] ReelStopSlipCount;
        public bool[] IsReelStopTableStyle;
        public int[] ConditionPatterns;
        public int[] ConditionLeftPositions;
        public int[] ConditionCenterPositions;
        public int[] ConditionRightPositions;
        public int[] ConditionSlipOffsets;
        public int[] ConditionSlipLengths;
        public int[] ConditionSlips;
    }

    /// <summary>生成結果を既存の Runtime 形式（基本テーブル＋停止位置の条件付きテーブル）へ変換する。</summary>
    public static class ReelRuleBaker
    {
        public static BakedControl Bake(ControlResult result, int[] stripLengths)
        {
            var basis = new int[64][];
            var styles = new bool[64];
            var patterns = new List<int>();
            var left = new List<int>();
            var center = new List<int>();
            var right = new List<int>();
            var offsets = new List<int>();
            var lengths = new List<int>();
            var slips = new List<int>();
            foreach (var t in result.Tables)
            {
                styles[t.Pattern] = true;
                if (t.Stage == 0)
                {
                    basis[t.Pattern] = (int[])t.Slips.Clone();
                    continue;
                }
                if (basis[t.Pattern] == null) basis[t.Pattern] = new int[stripLengths[t.Reel]];
                patterns.Add(t.Pattern);
                left.Add(t.Prior[0]);
                center.Add(t.Prior[1]);
                right.Add(t.Prior[2]);
                offsets.Add(slips.Count);
                lengths.Add(t.Slips.Length);
                slips.AddRange(t.Slips);
            }
            for (int i = 0; i < basis.Length; i++) if (basis[i] == null) basis[i] = new int[0];
            return new BakedControl
            {
                ReelStopSlipCount = basis,
                IsReelStopTableStyle = styles,
                ConditionPatterns = patterns.ToArray(),
                ConditionLeftPositions = left.ToArray(),
                ConditionCenterPositions = center.ToArray(),
                ConditionRightPositions = right.ToArray(),
                ConditionSlipOffsets = offsets.ToArray(),
                ConditionSlipLengths = lengths.ToArray(),
                ConditionSlips = slips.ToArray()
            };
        }
    }
}
