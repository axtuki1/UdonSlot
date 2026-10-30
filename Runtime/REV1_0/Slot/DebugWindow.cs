
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
        private TextMeshProUGUI pendingBonusText;
        
        [SerializeField]
        private TextMeshProUGUI activeBonusText;
        
        [SerializeField]
        private TextMeshProUGUI activeBonusCountText;

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
            
            BonusTable bonusTable = reelOutputController.GetBonusTable();
            SetText(pendingBonusText, BonusLabel(bonusTable, reelOutputController.GetPendingBonusName()));
            SetText(activeBonusText, BonusLabel(bonusTable, reelOutputController.GetActiveBonusName()));
            SetText(activeBonusCountText, BonusRemaining(bonusTable));
            WriteDebugText();
        }
        
        /// <summary>ボーナスの表示名。なければ "-"。</summary>
        private string BonusLabel(BonusTable table, string name)
        {
            if (!Utilities.IsValid(table)) return "-";
            int index = table.FindBonus(name);
            return index < 0 ? "-" : table.GetBonusDisplayName(index);
        }

        /// <summary>作動中のボーナスの終了条件まで残り（枚数・入賞回数・G数のうち条件にあるものだけ）。なければ "-"。</summary>
        private string BonusRemaining(BonusTable table)
        {
            if (!Utilities.IsValid(table) || !reelOutputController.IsBonusActive()) return "-";
            int index = table.FindBonus(reelOutputController.GetActiveBonusName());
            if (index < 0) return "-";
            string text = "";
            int payout = table.GetBonusEndPayout(index);
            int wins = table.GetBonusEndWins(index);
            int games = table.GetBonusEndGames(index);
            if (payout > 0) text = Mathf.Max(0, payout - reelOutputController.GetBonusPayoutCount()) + "枚";
            if (wins > 0) text += (text.Length > 0 ? " / " : "") + Mathf.Max(0, wins - reelOutputController.GetBonusWinCount()) + "回";
            if (games > 0) text += (text.Length > 0 ? " / " : "") + Mathf.Max(0, games - reelOutputController.GetBonusGameCount()) + "G";
            return text.Length > 0 ? text : "-";
        }

        private void SetText(TextMeshProUGUI text, string value)
        {
            if (Utilities.IsValid(text)) text.text = value;
        }

        public virtual void WriteDebugText()
        { }
    }
}