
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0.Common
{
    /// <summary>
    /// Interactまたはプレイヤー追従Colliderから計数ボタンの押下と解放をメイン基板へ渡す。
    /// </summary>
    public class MedalCountButton : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;

        public override void Interact()
        {
            mainBoard.PushCountingButton();
        }

        public void OnTriggerEnter(Collider other)
        {
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            mainBoard.PushCountingButton();
        }

        public void OnTriggerExit(Collider other)
        {
            if (!Networking.IsOwner(other.gameObject)) return;
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            mainBoard.ReleaseCountingButton();
        }
    }

}