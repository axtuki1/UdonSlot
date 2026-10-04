using UnityEngine;
using System;

namespace AX.UdonSlot.REV1_0.Serialize
{
    /// <summary>
    /// リール図柄の識別名と表示画像を対応付けるScriptableObject。
    /// </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "UdonSlot/ReelPicture")]
    public class ReelPicture : ScriptableObject
    {
        /**
         * 図柄の名前
         */
        public string pictureName;
        /**
         * 図柄の画像
         */
        public Texture2D picture;
    }
}
