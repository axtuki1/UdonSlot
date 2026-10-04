using System;

namespace AX.UdonSlot.REV1_0.Editor
{
    internal static class ReelControlComparison
    {
        internal enum State { Ready, Unset, Unreachable, Incompatible, Invalid }
        internal sealed class Result
        {
            public State state;
            public int slip = -1, stop = -1;
            public string message;
            public bool provisional;
            public bool HasStop { get { return state == State.Ready || state == State.Unset; } }
        }

        // Compare at the same physical positions, never silently remap different arrangements.
        internal static Result Evaluate(ReelControlDesign basis, ReelControlDesign candidate,
            int order, int stage, int[] context, int press)
        {
            try
            {
                if (candidate == null || basis == null) throw new InvalidOperationException("編集データなし");
                for (int r = 0; r < 3; r++)
                {
                    var a = basis.reels[r].symbols;
                    var b = candidate.reels[r].symbols;
                    if (a.Length != b.Length) return new Result { state = State.Incompatible, message = "配列不一致" };
                    for (int p = 0; p < a.Length; p++)
                        if (a[p] != b[p]) return new Result { state = State.Incompatible, message = "配列不一致" };
                }
                bool reachable = false;
                foreach (var row in candidate.ReachableContexts(order, stage))
                    if (row[0] == context[0] && row[1] == context[1] && row[2] == context[2]) { reachable = true; break; }
                if (!reachable) return new Result { state = State.Unreachable, message = "到達不能" };
                bool provisional = false;
                var prefix = new[] {-1, -1, -1};
                for (int s = 0; s < stage; s++)
                {
                    int r = ReelControlDesign.StopOrders[order][s];
                    var values = candidate.SlipsFor(order, s, prefix);
                    bool definite = false;
                    for (int p = 0; p < values.Length; p++)
                        if (values[p] >= 0 && ReelControlDesign.Normalize(p - values[p], values.Length) == context[r])
                            definite = true;
                    if (!definite) provisional = true;
                    prefix[r] = context[r];
                }
                int reel = ReelControlDesign.StopOrders[order][stage];
                int slip = candidate.SlipsFor(order, stage, context)[press];
                if (slip < -1 || slip > 4) throw new InvalidOperationException("滑り値が範囲外");
                return new Result { state = slip < 0 ? State.Unset : State.Ready, slip = slip,
                    stop = ReelControlDesign.Normalize(press - Math.Max(0, slip), candidate.reels[reel].symbols.Length),
                    message = slip < 0 ? "未設定（暫定0）" : slip + "コマ", provisional = provisional };
            }
            catch (Exception ex) { return new Result { state = State.Invalid, message = "比較不可: " + ex.Message }; }
        }

        internal static bool Same(Result a, Result b)
        {
            return a.state == b.state && a.slip == b.slip && a.stop == b.stop && a.provisional == b.provisional;
        }
    }
}
