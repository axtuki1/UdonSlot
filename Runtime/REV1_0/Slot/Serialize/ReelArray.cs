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
        /// <summary>
        /// リールに並ぶ図柄
        /// </summary>
        public ReelPicture[] reel;

        /// <summary>
        /// 通常時滑り定義
        /// </summary>
        public StopThrough standardStop;
        
        /// <summary>
        /// フラグ重複滑り定義
        /// </summary>
        public StopThrough[] flagDuplicateStop;

    }
}
