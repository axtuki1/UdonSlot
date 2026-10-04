using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    public enum SEGMENGCOUNT
    {
        DSEG7,
        DSEG14
    }
    
    /// <summary>
    /// MainBoardのクレジットを通常表示、即時表示、セグメント表示へ反映する。
    /// 無操作時のデモ表示と、増減時の追従アニメーションも管理する。
    /// </summary>
    public class CreditView : UdonSharpBehaviour
    {
        /**
         * カウントアップ/ダウンして反映するテキスト
         */
        [SerializeField]
        private TextMeshProUGUI creditText;
        
        /**
         * 即時反映するテキスト
         */
        [SerializeField]
        private TextMeshProUGUI instantApplyText;

        /**
         * クレジットのセグメント表示テキスト
         */
        [SerializeField]
        private TextMeshProUGUI creditSegText;
        
        [SerializeField]
        private SEGMENGCOUNT segmentCount = SEGMENGCOUNT.DSEG7;

        [SerializeField]
        private int demoView;

        /**
         * デモ表示の内容2
         */
        [SerializeField]
        private string[] demoView1 = {""};

        /**
         * デモ表示のフレームレート
         */
        [SerializeField]
        private float frameRateView1 = 0.25f;

        /**
         * デモ表示の内容2
         */
        [SerializeField]
        private string[] demoView2 = {""};

        /**
         * デモ表示のフレームレート
         */
        [SerializeField]
        private float frameRateView2 = 0.25f;
        
        /**
         * デモ表示の内容2
         */
        [SerializeField]
        private string[] demoView3 = {""};

        /**
         * デモ表示のフレームレート
         */
        [SerializeField]
        private float frameRateView3 = 0.25f;

        [SerializeField]
        private int currentFrame;
        [SerializeField]
        private float lastDemoViewSeconds;

        [SerializeField]
        private float nonActionTime = 5f;
        
        /**
         * クレジット更新の際に1フレームに付きどれだけ増減させるか
         */
        public float frameChangeingRate = 5f;

        private MainBoard mainBoard;

        private int viewingCredit = 0;

        private float changingTime = 0f;
        
        private float currentNonActionTime = 0f;

        private void Start()
        {
            mainBoard = GetComponentInParent<MainBoard>();
        }

        private void Update()
        {
            if (!Utilities.IsValid(mainBoard)) return;

            UpdateViewingCredit();
            if (Utilities.IsValid(instantApplyText)) instantApplyText.text = mainBoard.credit.ToString();
            if (Utilities.IsValid(creditText)) creditText.text = viewingCredit.ToString();
            if (Utilities.IsValid(creditSegText)) UpdateSegmentDisplay();
        }

        private void UpdateViewingCredit()
        {
            if (mainBoard.credit != viewingCredit)
            {
                // クレジットが変わっている場合、1フレームに付きframeChangeingRateだけ増減させる
                // changingTimeが0.5秒以上経過している場合は、frameChangeingRateを2倍にする
                float changeRate = frameChangeingRate;
                if (changingTime > 10f)
                {
                    changeRate *= 8f;
                }
                else if (changingTime > 5f)
                {
                    changeRate *= 2f;
                }

                if (mainBoard.credit > viewingCredit)
                {
                    viewingCredit += (int)changeRate;
                    if (viewingCredit > mainBoard.credit) viewingCredit = mainBoard.credit;
                }
                else
                {
                    viewingCredit -= (int)changeRate;
                    if (viewingCredit < mainBoard.credit) viewingCredit = mainBoard.credit;
                }

                changingTime += Time.deltaTime;
            }
            else
            {
                changingTime = 0f;
            }
        }

        private void UpdateSegmentDisplay()
        {
            if (mainBoard.credit == 0)
            {
                currentNonActionTime += Time.deltaTime;
            }
            else
            {
                currentNonActionTime = 0f;
                lastDemoViewSeconds = 0f;
                currentFrame = 0;
            }

            if (currentNonActionTime < nonActionTime || demoView < 1 || demoView > 3) return;

            float frameRate = frameRateView1;
            string[] view = demoView1;
            switch (demoView)
            {
                case 2:
                    frameRate = frameRateView2;
                    view = demoView2;
                    break;
                case 3:
                    frameRate = frameRateView3;
                    view = demoView3;
                    break;
            }

            lastDemoViewSeconds += Time.deltaTime;
            if (lastDemoViewSeconds >= frameRate)
            {
                lastDemoViewSeconds = 0f;
                currentFrame++;
                if (currentFrame >= view.Length) currentFrame = 0;
            }

            creditSegText.text = view[currentFrame];
        }
    }
}
