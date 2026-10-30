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
    /// メイン基板のクレジットを通常表示、即時表示、セグメント表示へ反映する。
    /// 無操作時のデモ表示と、増減時の追従アニメーションも管理する。
    /// </summary>
    public class CreditView : UdonSharpBehaviour
    {
        /// <summary>
        /// カウントアップ/ダウンして反映するテキスト
        /// </summary>
        [SerializeField]
        private TextMeshProUGUI creditText;
        
        /// <summary>
        /// 即時反映するテキスト
        /// </summary>
        [SerializeField]
        private TextMeshProUGUI instantApplyText;

        /// <summary>
        /// クレジットのセグメント表示テキスト
        /// </summary>
        [SerializeField]
        private TextMeshProUGUI creditSegText;
        
        [SerializeField]
        private SEGMENGCOUNT segmentCount = SEGMENGCOUNT.DSEG7;

        [SerializeField]
        private int demoView;

        /// <summary>
        /// デモ表示の内容2
        /// </summary>
        [SerializeField]
        private string[] demoView1 = {""};

        /// <summary>
        /// デモ表示のフレームレート
        /// </summary>
        [SerializeField]
        private float frameRateView1 = 0.25f;

        /// <summary>
        /// デモ表示の内容2
        /// </summary>
        [SerializeField]
        private string[] demoView2 = {""};

        /// <summary>
        /// デモ表示のフレームレート
        /// </summary>
        [SerializeField]
        private float frameRateView2 = 0.25f;
        
        /// <summary>
        /// デモ表示の内容2
        /// </summary>
        [SerializeField]
        private string[] demoView3 = {""};

        /// <summary>
        /// デモ表示のフレームレート
        /// </summary>
        [SerializeField]
        private float frameRateView3 = 0.25f;

        [SerializeField]
        private int currentFrame;
        [SerializeField]
        private float lastDemoViewSeconds;

        [SerializeField]
        private float nonActionTime = 5f;
        
        /// <summary>
        /// クレジット更新の際に1フレームに付きどれだけ増減させるか
        /// </summary>
        public float frameChangeingRate = 5f;

        private MainBoard mainBoard;

        private int viewingCredit = 0;

        private float changingTime = 0f;
        
        private float currentNonActionTime = 0f;

        /// <summary>テキストに書き込んだ値。int.MinValueは未表示。</summary>
        private int shownInstantCredit = int.MinValue, shownViewingCredit = int.MinValue;

        private void Start()
        {
            mainBoard = GetComponentInParent<MainBoard>();
        }

        private void Update()
        {
            if (!Utilities.IsValid(mainBoard)) return;

            UpdateViewingCredit();
            // 値が変わったときだけ文字列を作って書き込む。
            int credit = mainBoard.credit;
            if (Utilities.IsValid(instantApplyText) && credit != shownInstantCredit)
            {
                shownInstantCredit = credit;
                instantApplyText.text = credit.ToString();
            }
            if (Utilities.IsValid(creditText) && viewingCredit != shownViewingCredit)
            {
                shownViewingCredit = viewingCredit;
                creditText.text = viewingCredit.ToString();
            }
            if (Utilities.IsValid(creditSegText)) UpdateSegmentDisplay();
        }

        private void UpdateViewingCredit()
        {
            int credit = mainBoard.credit;
            if (credit == viewingCredit)
            {
                changingTime = 0f;
                return;
            }

            // 1フレームごとにframeChangeingRateずつ近づける。変化が5秒を超えたら2倍、10秒を超えたら8倍。
            float changeRate = frameChangeingRate;
            if (changingTime > 10f) changeRate *= 8f;
            else if (changingTime > 5f) changeRate *= 2f;

            viewingCredit = credit > viewingCredit
                ? Mathf.Min(viewingCredit + (int)changeRate, credit)
                : Mathf.Max(viewingCredit - (int)changeRate, credit);
            changingTime += Time.deltaTime;
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
