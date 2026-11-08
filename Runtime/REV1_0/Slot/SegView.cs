
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
        
        /// <summary>各ユニットに反映したパターン。-1は未反映。</summary>
        private int[] appliedSegFlgs;

        private void Start()
        {
            activeSegFlgs = new byte[segObjs.Length];
            appliedSegFlgs = new int[segObjs.Length];
            for (int i = 0; i < appliedSegFlgs.Length; i++) appliedSegFlgs[i] = -1;
        }

        private void FixedUpdate()
        {
            if (activeSegFlgs == null || appliedSegFlgs == null) return;
            // 変わったユニットだけ点灯を切り替える。
            for (int i = 0; i < segObjs.Length; i++)
            {
                if (appliedSegFlgs[i] == activeSegFlgs[i]) continue;
                appliedSegFlgs[i] = activeSegFlgs[i];
                segObjs[i].ApplySegPattern(activeSegFlgs[i]);
            }
        }

        public void SetActiveSeg(int index)
        {
            if (!HasSeg(index)) return;
            activeSegFlgs[index] = 0;
        }
        
        public byte GetActiveSeg(int index)
        {
            return HasSeg(index) ? activeSegFlgs[index] : (byte)0;
        }

        /// <summary>そのセグユニットがあるか。無い番号への書き込みは無視する（桁数の少ない機種向け）。</summary>
        public bool HasSeg(int index)
        {
            return activeSegFlgs != null && index >= 0 && index < activeSegFlgs.Length;
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
            if (!HasSeg(index)) return;
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