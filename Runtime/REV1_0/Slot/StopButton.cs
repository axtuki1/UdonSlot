
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// Interactまたはプレイヤー追従Colliderから停止入力を受け、指定リールの押下と解放をMainBoardへ渡す。
    /// </summary>
    public class StopButton : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;
        
        [SerializeField]
        private int stopReelIndex = 0;
        
        private bool isPressed = false;
        
        public override void Interact()
        {
            PushStopButton();
            SendCustomEventDelayedSeconds(nameof(ReleaseStopButton), 0.05f);
        }
        
        public void OnTriggerEnter(Collider other)
        {
            if (isPressed) return;
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            PushStopButton();
            isPressed = true;
        }
        
        public void OnTriggerExit(Collider other)
        {
            if (!isPressed) return;
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            ReleaseStopButton();
            isPressed = false;
        }

        public void PushStopButton()
        {
            mainBoard.PushStopButton(stopReelIndex);
        }

        public void ReleaseStopButton()
        {
            mainBoard.ReleaseStopButton(stopReelIndex);
        }
    }
}
