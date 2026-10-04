
using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0.WatchDog
{
    /// <summary>
    /// Udonがエラーによって停止していないか監視する
    /// </summary>
    public class SlotWatchDog : UdonSharpBehaviour
    {
        [SerializeField]
        private UdonBehaviour[] targetUdonBehaviours;
        
        [SerializeField]
        private GameObject[] onErrorActiveObjects;

        [SerializeField]
        private GameObject[] onErrorInActiveObjects;
        
        [SerializeField]
        private TextMeshProUGUI errorText, errorTimeText;
        
        [SerializeField]
        private float checkInterval = 2f;
        
        public void Start()
        {
            CheckUdonTooSoft();
        }
        
        public void CheckUdonTooSoft()
        {
            foreach (var udon in targetUdonBehaviours)
            {
                if (!Utilities.IsValid(udon))
                {
                    // オブジェクトがNullだったら何もしない
                    continue;
                }
                
                if (!udon.enabled)
                {
                    // UdonBehaviourが無効化されていたらエラー表示して動作を止める
                    errorText.text = $"{udon.gameObject.name}";
                    errorTimeText.text = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss}";
                    Debug.Log($"============================================================");
                    Debug.Log($"    SlotWatchDog Detected!: {udon.gameObject.name}");
                    Debug.Log($"============================================================");
                    ErrorDisplay();
                    return;
                }
            }
            SendCustomEventDelayedSeconds(nameof(CheckUdonTooSoft), checkInterval);
        }
        
        public void ErrorDisplay()
        {
            foreach (var obj in onErrorActiveObjects)
            {
                if (Utilities.IsValid(obj))
                {
                    obj.SetActive(true);
                }
            }
            foreach (var obj in onErrorInActiveObjects)
            {
                if (Utilities.IsValid(obj))
                {
                    obj.SetActive(false);
                }
            }
        }
    }
}
