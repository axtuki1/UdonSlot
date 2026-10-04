
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0
{
    public class SegUnit : UdonSharpBehaviour
    {
        /// <summary>
        /// セグの点灯パターン配列。
        /// index = フラグ上の桁数。
        /// </summary>
        [SerializeField]
        private GameObject[] segPatterns;
        
        /// <summary>
        /// セグの点灯パターンを適用する。
        /// </summary>
        /// <param name="segFlg">セグの点灯フラグ</param>
        public void ApplySegPattern(byte segFlg)
        {
            for (int i = 0; i < segPatterns.Length; i++)
            {
                bool isActive = (segFlg & (1 << i)) != 0;
                segPatterns[i].SetActive(isActive);
            }
        }
        
    }

}