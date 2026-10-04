using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// MainBoardのBET数に合わせて、各BET段階の画像表示を更新する。
    /// </summary>
    public class BetView : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;

        [SerializeField]
        private Image[] bet1Images, bet2Images, bet3Images;

        private void FixedUpdate()
        {
            if (!Utilities.IsValid(mainBoard)) return;
            SetImageAlpha(bet1Images, mainBoard.bet >= 1);
            SetImageAlpha(bet2Images, mainBoard.bet >= 2);
            SetImageAlpha(bet3Images, mainBoard.bet >= 3);
        }

        private void SetImageAlpha(Image[] images, bool isVisible)
        {
            for (int i = 0; i < images.Length; i++)
            {
                Color color = images[i].color;
                color.a = isVisible ? 1f : 0f;
                images[i].color = color;
            }
        }
    }
}
