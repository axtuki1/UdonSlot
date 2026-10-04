
using TMPro;
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// BasicSlotサンプルの抽選値、抽選結果、成立役、ボーナス状態を画面へ表示するデバッグビュー。
    /// </summary>
    public class DebugWindow : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;
        
        [SerializeField]
        private SampleController sampleController;

        [SerializeField]
        private TextMeshProUGUI rndText, selectRndText, activeWinText, bigFlgText, regFlgText;
        
        private string[] activeWinNames = new string[]
        {
            "None",
            "Blueberry",
            "Cherry",
            "CenterCherry",
            "Bell",
            "Replay"
        };
        
        private string[] lotteryResultNames = new string[]
        {
            "None",
            "Blueberry",
            "Cherry",
            "CenterCherryBigBonus",
            "CenterCherryRegularBonus",
            "DuplicateCherryBigBonus",
            "DuplicateCherryRegularBonus",
            "Bell",
            "Replay",
            "BigBonus",
            "RegularBonus"
        };

        private void FixedUpdate()
        {
            rndText.text = sampleController.lotteryResult.ToString();
            selectRndText.text = lotteryResultNames[(int)sampleController.activeLot];
            activeWinText.text = activeWinNames[(int)sampleController.activeWin];
            bigFlgText.text = sampleController.isBigBonus.ToString();
            regFlgText.text = sampleController.isRegularBonus.ToString();
        }
    }
}
