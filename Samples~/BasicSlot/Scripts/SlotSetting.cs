using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// BasicSlotサンプルで、設定段階ごとの抽選範囲を保持する。
    /// </summary>
    public class SlotSetting : UdonSharpBehaviour
    {
        [SerializeField]
        public string settingName;

        [SerializeField]
        public int lotteryMaxRange = 65535;

        /**
         * ハズレが選ばれる範囲。
         */
        [SerializeField]
        public int noneRange;

        /*
         * リプレイが選ばれる範囲。
         */
        [SerializeField]
        public int replayRange;

        /**
         * ぶどうが選ばれる範囲。
         */
        [SerializeField]
        public int blueberryRange;
        
        /**
         * ベルが選ばれる範囲。
         */
        [SerializeField]
        public int bellRange;

        /**
         * チェリーが選ばれる範囲。
         */
        [SerializeField]
        public int cherryRange;
        
        /**
         * 中段チェリー重複ビッグボーナスが選ばれる範囲。
         */
        [SerializeField]
        public int centerCherryBigBounsRange;
        
        /**
         * 中段チェリー重複レギュラーボーナスが選ばれる範囲。
         */
        [SerializeField]
        public int centerCherryRegularBounsRange;

        /**
         * チェリー重複ビッグボーナスが選ばれる範囲。
         */
        [SerializeField]
        public int duplicateCherryBigBounsRange;

        /**
         * チェリー重複レギュラーボーナスが選ばれる範囲。
         */
        [SerializeField]
        public int duplicateCherryRegularBounsRange;

        /**
         * 単独ビッグボーナスが選ばれる範囲。
         */
        [SerializeField]
        public int bigBonusRange;

        /**
         * 単独レギュラーボーナスが選ばれる範囲。
         */
        [SerializeField]
        public int regularBonusRange;
    }
}
