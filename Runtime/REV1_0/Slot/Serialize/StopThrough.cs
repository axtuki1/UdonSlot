using UnityEngine;
using System;

namespace AX.UdonSlot.REV1_0.Serialize
{
    /// <summary>
    /// 1本のリールについて、成立フラグと図柄位置ごとの滑りコマ数を定義するScriptableObject。
    /// </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "UdonSlot/StopThrough")]
    public class StopThrough : ScriptableObject
    {
        /// <summary>
        /// この滑りコマ定義の名前
        /// 管理用に接頭辞とかにLeftとかCenterとかRightとかつけるとわかりやすいかも
        /// </summary>
        public string flagName;
        /// <summary>
        /// ビタ止めしたコマからどれだけ滑るかを定義する配列
        /// 長さは紐づけるリールのコマ数と同じにする必要がある
        /// </summary>
        public int[] throughLength;
    }
}

