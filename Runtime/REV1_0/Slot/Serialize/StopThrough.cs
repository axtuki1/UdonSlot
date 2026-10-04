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
        /**
         * この滑りコマ定義の名前
         * 管理用に接頭辞とかにLeftとかCenterとかRightとかつけるとわかりやすいかも
         */
        public string flagName;
        /**
         * ビタ止めしたコマからどれだけ滑るかを定義する配列
         * 長さは紐づけるリールのコマ数と同じにする必要がある
         */
        public int[] throughLength;
    }
}

