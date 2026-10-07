
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
            activeFlagText.text = reelOutputController.GetActiveFlagDisplayName();
            string activeLineText = "";
            string[] activePayLineSymbols = reelOutputController.mainBoard.lastPayLineSymbols;
            int lineCount = 0;
            for (int i = 0; i < activePayLineSymbols.Length; i += 3)
            {
                lineCount++;
                activeLineText += $"LINE{lineCount}: {activePayLineSymbols[i]}, {activePayLineSymbols[i + 1]}, {activePayLineSymbols[i + 2]}\n";
            }
            this.activeLineText.text = activeLineText;
            int[] throughSymbolCount = reelOutputController.mainBoard.stoppedReelsThroughSymbolCount;
            string[] throughSymbolCountText = new string[3];
            for (int i = 0; i < throughSymbolCount.Length; i++)
            {
                if (reelOutputController.mainBoard.isRollingReel1 && i == 0)
                {
                    throughSymbolCountText[i] = "-";
                }
                else if (reelOutputController.mainBoard.isRollingReel2 && i == 1)
                {
                    throughSymbolCountText[i] = "-";
                }
                else if (reelOutputController.mainBoard.isRollingReel3 && i == 2)
                {
                    throughSymbolCountText[i] = "-";
                }
                else if (throughSymbolCount[i] < 0)
                {
                    throughSymbolCountText[i] = throughSymbolCount[i].ToString();
                }
                else
                {
                    throughSymbolCountText[i] = throughSymbolCount[i].ToString();
                }
            }
            slipCountText.text = $"{throughSymbolCountText[0]} / {throughSymbolCountText[1]} / {throughSymbolCountText[2]}";
            WriteDebugText();
        }
        
        public virtual void WriteDebugText()
        { }
    }
}