using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// メイン基板のBET数・リプレイ・INSERT・STARTに合わせて、ランプの画像表示を更新する。
    /// </summary>
    public class BetView : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;

        [SerializeField]
        private Image[] bet1Images, bet2Images, bet3Images,
                        replayImages, insertImages, startImages;

        /// <summary>表示に反映した点灯状態（ビットごと）。-1は未反映。</summary>
        private int shownState = -1;

        private void FixedUpdate()
        {
            if (!Utilities.IsValid(mainBoard)) return;
            int bet = mainBoard.bet;
            bool replay = mainBoard.isReplay;
            // INSERT: まだ掛けられる（リプレイ中は自動BETなので消す）。最大BETはボーナス作動中に変わる。
            bool insert = !replay && bet < mainBoard.GetMaxBet();
            bool start = !mainBoard.IsGameInProgress() && mainBoard.CanStartGame();
            int state = (bet >= 1 ? 1 : 0) | (bet >= 2 ? 2 : 0) | (bet >= 3 ? 4 : 0)
                        | (replay ? 8 : 0) | (insert ? 16 : 0) | (start ? 32 : 0);
            if (state == shownState) return;
            shownState = state;
            SetImageAlpha(bet1Images, bet >= 1);
            SetImageAlpha(bet2Images, bet >= 2);
            SetImageAlpha(bet3Images, bet >= 3);
            SetImageAlpha(replayImages, replay);
            SetImageAlpha(insertImages, insert);
            SetImageAlpha(startImages, start);
        }

        private void SetImageAlpha(Image[] images, bool isVisible)
        {
            if (images == null) return;
            for (int i = 0; i < images.Length; i++)
            {
                Color color = images[i].color;
                color.a = isVisible ? 1f : 0f;
                images[i].color = color;
            }
        }
    }
}
