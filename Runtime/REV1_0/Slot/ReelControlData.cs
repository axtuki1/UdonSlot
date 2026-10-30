using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 停止順ごとの基本テーブルと、停止済みリールの最終位置で切り替える条件付きテーブルを保持する。
    /// </summary>
    public class ReelControlData : UdonSharpBehaviour
    {
        /// <summary>
        /// 1次元目のインデックスはリール停止パターンを表す。
        /// 2次元目はリールの図柄番号、値は停止時の滑りコマ数を表す。
        ///
        /// 1次元目は上位ビットを左リール、下位ビットを右リールとし、停止順序を各2ビットで表す。
        /// 一番大きい値を持つリールのパターンが2次元目に格納されている。
        /// 000001: 右のみ停止 / 000100: 中のみ停止 / 010000: 左のみ停止
        /// 011000: 左→中(中リールのパターン) / 000110: 中→右(右リールのパターン) / 011011: 左→中→右(右リールのパターン)
        /// </summary>
        [VRC.Udon.Serialization.OdinSerializer.OdinSerialize]
        public int[][] reelStopSlipCount = new int[0][];
        
        /// <summary>
        /// テーブル制御かコントロール制御かを示す。trueであればテーブル制御、falseであればコントロール制御。
        /// </summary>
        public bool[] IsReelStopTableStyle = new bool[0];

        // 条件付きテーブル。条件位置は滑り後の下段インデックス、-1は任意。
        // 同じ押し順では具体的な条件が多い行を優先し、同数なら先に登録した行を使用する。
        [HideInInspector] public int[] conditionPatterns = new int[0];
        [HideInInspector] public int[] conditionLeftPositions = new int[0];
        [HideInInspector] public int[] conditionCenterPositions = new int[0];
        [HideInInspector] public int[] conditionRightPositions = new int[0];
        [HideInInspector] public int[] conditionSlipOffsets = new int[0];
        [HideInInspector] public int[] conditionSlipLengths = new int[0];
        [HideInInspector] public int[] conditionSlips = new int[0];

        /// <summary>条件付きテーブルを検索する。該当なし、未入力(-1)の場合は基本テーブルへ戻す。</summary>
        public int GetConditionalSlip(int pattern, int left, int center, int right, int pressedPosition)
        {
            if (conditionPatterns == null || conditionLeftPositions == null ||
                conditionCenterPositions == null || conditionRightPositions == null ||
                conditionSlipOffsets == null || conditionSlipLengths == null || conditionSlips == null) return -1;
            int best = -1;
            int bestSpecificity = -1;
            for (int i = 0; i < conditionPatterns.Length; i++)
            {
                if (i >= conditionLeftPositions.Length || i >= conditionCenterPositions.Length ||
                    i >= conditionRightPositions.Length || i >= conditionSlipOffsets.Length ||
                    i >= conditionSlipLengths.Length) continue;
                if (conditionPatterns[i] != pattern) continue;
                int l = conditionLeftPositions[i];
                int c = conditionCenterPositions[i];
                int r = conditionRightPositions[i];
                if ((l >= 0 && l != left) || (c >= 0 && c != center) || (r >= 0 && r != right)) continue;
                int specificity = (l >= 0 ? 1 : 0) + (c >= 0 ? 1 : 0) + (r >= 0 ? 1 : 0);
                if (specificity <= bestSpecificity) continue;
                best = i;
                bestSpecificity = specificity;
            }
            if (best < 0 || pressedPosition < 0 || pressedPosition >= conditionSlipLengths[best]) return -1;
            int index = conditionSlipOffsets[best] + pressedPosition;
            if (conditionSlipOffsets[best] < 0 || index < 0 || index >= conditionSlips.Length) return -1;
            return conditionSlips[index];
        }

        /// <summary>
        /// reelStopSlipCountのインデックスをリール停止順序から計算する。
        /// 各引数は当該リールの状態/停止順序を表す整数で、0は停止なし、1-3は停止順序を表す。
        /// 4以上、0未満の値はエラー。
        /// </summary>
        public static int GetIndexForReelStopPattern(int leftReelStopOrder, int centerReelStopOrder, int rightReelStopOrder)
        {
            leftReelStopOrder = Mathf.Clamp(leftReelStopOrder, 0, 3);
            centerReelStopOrder = Mathf.Clamp(centerReelStopOrder, 0, 3);
            rightReelStopOrder = Mathf.Clamp(rightReelStopOrder, 0, 3);
            int pattern = 0;
            pattern |= leftReelStopOrder << 4;
            pattern |= centerReelStopOrder << 2;
            pattern |= rightReelStopOrder;
            return pattern;
        }
    }
}
