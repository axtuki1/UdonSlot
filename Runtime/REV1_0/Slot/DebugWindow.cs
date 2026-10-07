
using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0 {
    public class DebugWindow : UdonSharpBehaviour
    {
        [SerializeField]
        protected ReelOutputController reelOutputController;

        [SerializeField]
        private TextMeshProUGUI activeFlagText;
        
        [SerializeField]
        private TextMeshProUGUI activeLineText;
        
        [SerializeField]
        private TextMeshProUGUI slipCountText;
        
        private void FixedUpdate()
        {
            MainBoard mainBoard = reelOutputController.mainBoard;
            activeFlagText.text = reelOutputController.GetActiveFlagDisplayName();

            string lineText = "";
            string[] payLineSymbols = mainBoard.lastPayLineSymbols;
            for (int i = 0; i < payLineSymbols.Length; i += 3)
            {
                lineText += $"LINE{i / 3 + 1}: {payLineSymbols[i]}, {payLineSymbols[i + 1]}, {payLineSymbols[i + 2]}\n";
            }
            activeLineText.text = lineText;

            // 回転中のリールは滑りコマ数を「-」で表示する。
            int[] throughSymbolCount = mainBoard.stoppedReelsThroughSymbolCount;
            string left = mainBoard.isRollingReel1 ? "-" : throughSymbolCount[0].ToString();
            string center = mainBoard.isRollingReel2 ? "-" : throughSymbolCount[1].ToString();
            string right = mainBoard.isRollingReel3 ? "-" : throughSymbolCount[2].ToString();
            slipCountText.text = $"{left} / {center} / {right}";
            WriteDebugText();
        }
        
        public virtual void WriteDebugText()
        { }
    }
}