
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 成立役の名前、配当、図柄組み合わせ、停止順条件、タグを定義する。
    /// </summary>
    public class WinPair : UdonSharpBehaviour
    {
        /// <summary>
        /// 役の名前
        /// </summary>
        [SerializeField]
        public string winPairName;
        /// <summary>
        /// 配当
        /// </summary>
        [SerializeField]
        public int payout;
        /// <summary>
        /// 有効ライン上の役の図柄
        /// 空文字は任意
        /// </summary>
        [SerializeField]
        public string[] symbols = new string[3];
        /// <summary>
        /// リール停止順序を使用せず
        /// </summary>
        [SerializeField]
        public bool isReelStopOrderAny = true;
        /// <summary>
        /// リール停止順序
        /// </summary>
        [SerializeField]
        public int[] reelStopOrder;
        [SerializeField]
        public string[] tags;
    }
}
