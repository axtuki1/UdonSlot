
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// "セグ"の表示制御を行う。
    /// </summary>
    public class SegView : UdonSharpBehaviour
    {
        /// <summary>
        /// セグユニット配列
        /// </summary>
        [SerializeField]
        private SegUnit[] segObjs;
        
        /// <summary>
        /// セグ点灯フラグ。
        /// byte型なので8bit。点灯パターンは各セグユニットに依存する。
        /// </summary>
        private byte[] activeSegFlgs;
        
        private void Start()
        {
            activeSegFlgs = new byte[segObjs.Length];
        }

        private void FixedUpdate()
        {
            for (int i = 0; i < segObjs.Length; i++)
            {
                segObjs[i].ApplySegPattern(activeSegFlgs[i]);
            }
        }

        public void SetActiveSeg(int index)
        {
            activeSegFlgs[index] = 0;
        }
        
        public byte GetActiveSeg(int index)
        {
            return activeSegFlgs[index];
        }
        
        /// <summary>
        /// 数値を表示する時の汎用セグパターンを返す。 <br />
        /// 当然、セグユニット側の定義が異なる場合は想定通りの点灯にならない。 <br />
        /// 定義済みのパターンは以下の通り。 <br />
        /// ___*A*___<br />
        /// *F*___*B*<br />
        /// ___*G*___<br />
        /// *E*___*C* <br />
        /// ___*D*___*H*(Dot) のセグパターンで、、 <br />
        /// フラグ配列は次のようになっていた場合の配列 <br />
        /// 0xHGFEDCBA
        /// </summary>
        /// <param name="index">セグユニット番号</param>
        /// <param name="number">表示する数値</param>
        /// <param name="dot">ドット</param>
        public void SetNumberSeg(int index, int number, bool dot = false)
        {
            number = number % 10;
            activeSegFlgs[index] = (byte)(numberSegPatterns[number] | (dot ? 0b10000000 : 0));
        }

        /// <summary>
        /// セグの点灯パターンを数値に変換するための配列。<br />
        /// ___*A*___<br />
        /// *F*___*B*<br />
        /// ___*G*___<br />
        /// *E*___*C* <br />
        /// ___*D*___*H*(Dot) のセグパターンで、、 <br />
        /// フラグ配列は次のようになっていた場合の配列 <br />
        /// 0xHGFEDCBA
        /// </summary>
        private byte[] numberSegPatterns =
        {
            0b00111111, // 0
            0b00000110, // 1
            0b01110011, // 2
            0b01001111, // 3
            0b01100111, // 4
            0b01101101, // 5
            0b01111101, // 6
            0b01100110, // 7
            0b01111111, // 8
            0b01101111, // 9
        };
    }
}