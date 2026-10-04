
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0.Common.Mobile
{
    /// <summary>
    /// モバイル環境のFocus View状態に合わせて、表示領域と案内オブジェクトを切り替える。
    /// </summary>
    public class FocusViewSupport : UdonSharpBehaviour
    {
        [SerializeField]
        private GameObject focusViewport;
        [SerializeField]
        private GameObject focusViewGuide;
        private void FixedUpdate()
        {
            #if UNITY_IOS || UNITY_ANDROID
                if(Networking.LocalPlayer.IsUserInVR()){
                    gameObject.SetActive(false);
                    if(Utilities.IsValid(focusViewGuide)) focusViewGuide.SetActive(false);
                    if(Utilities.IsValid(focusViewport)) focusViewport.SetActive(false);
                } else if(VRCCameraSettings.ScreenCamera.CameraMode == VRCCameraMode.FocusView)
                {
                    if(Utilities.IsValid(focusViewport)) focusViewport.SetActive(true);
                    if(Utilities.IsValid(focusViewGuide)) focusViewGuide.SetActive(false);
                }
                else
                {
                    if(Utilities.IsValid(focusViewport)) focusViewport.SetActive(false);
                    if(Utilities.IsValid(focusViewGuide)) focusViewGuide.SetActive(true);
                }
            #else
                gameObject.SetActive(false);
                if(Utilities.IsValid(focusViewGuide)) focusViewGuide.SetActive(false);
                if(Utilities.IsValid(focusViewport)) focusViewport.SetActive(false);
            #endif
        }
    }
}
