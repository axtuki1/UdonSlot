
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 成立役の名前、配当、図柄組み合わせ、停止順条件、タグを定義する。
    /// </summary>
    public class WinPair : UdonSharpBehaviour
    {
        /**
         * 役の名前
         */
        [SerializeField]
        public string winPairName;
        /**
         * 配当
         */
        [SerializeField]
        public int payout;
        /**
         * 有効ライン上の役の図柄
         * -1はany
         */
        [SerializeField]
        public string[] symbols = new string[3];
        /**
         * リール停止順序を使用せず
         */
        [SerializeField]
        public bool isReelStopOrderAny = true;
        /**
         * リール停止順序
         */
        [SerializeField]
        public int[] reelStopOrder;
        [SerializeField]
        public string[] tags;
    }
}
