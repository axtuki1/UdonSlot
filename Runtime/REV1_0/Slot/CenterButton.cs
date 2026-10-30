
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0.Common
{
    /// <summary>
    /// Interactまたはプレイヤー追従Colliderから中央ボタンの押下と解放をメイン基板へ渡す。
    /// </summary>
    public class CenterButton : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;

        public override void Interact()
        {
            mainBoard.PushCenterButton();
        }

        public void OnTriggerEnter(Collider other)
        {
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            mainBoard.PushCenterButton();
        }

        public void OnTriggerExit(Collider other)
        {
            if (!Networking.IsOwner(other.gameObject)) return;
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            mainBoard.ReleaseCenterButton();
        }
    }
}
