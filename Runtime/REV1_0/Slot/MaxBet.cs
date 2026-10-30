
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// Interactまたはプレイヤー追従ColliderからMaxBET入力を受け、メイン基板へ渡す。
    /// </summary>
    public class MaxBet : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;

        public override void Interact()
        {
            mainBoard.MaxBet();
        }

        public void OnTriggerEnter(Collider other)
        {
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            mainBoard.PushMaxBet();
        }
        
        public void OnTriggerExit(Collider other)
        {
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            if (!tracking.IsOwner()) return;
            mainBoard.ReleaseMaxBetButton();
        }
    }
}
