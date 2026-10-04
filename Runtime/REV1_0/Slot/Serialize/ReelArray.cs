using System;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Serialize
{
    /// <summary>
    /// リールの図柄配列と、通常時・フラグ重複時の滑り定義をまとめるScriptableObject。
    /// </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "UdonSlot/ReelArray")]
    public class ReelArray : ScriptableObject
    {
        /**
         * リールに並ぶ図柄
         */
        public ReelPicture[] reel;

        /**
         * 通常時滑り定義
         */
        public StopThrough standardStop;
        
        /**
         * フラグ重複滑り定義
         */
        public StopThrough[] flagDuplicateStop;

    }
}
