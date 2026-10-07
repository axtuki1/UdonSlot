
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// Interactまたはプレイヤー追従Colliderからレバー入力を受け、MainBoardへゲーム開始を要求する。
    /// </summary>
    public class Lever : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;
        
        [SerializeField]
        private float leverPushAngle = 30f;

        private Quaternion initialRotation;
        private bool isPushed = false;
        
        private void Start()
        {
            initialRotation = transform.localRotation;
        }

        private void Update()
        {
            Quaternion targetRotation = isPushed ? Quaternion.Euler(leverPushAngle, 0, 0) : initialRotation;
            transform.localRotation = Quaternion.Lerp(transform.localRotation, targetRotation, Time.deltaTime * 10f);
        }

        public override void Interact()
        {
            mainBoard.LeverOn();
        }

        public void OnTriggerStay(Collider other)
        {
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            isPushed = true;
            if (!tracking.IsOwner()) return;
            mainBoard.LeverOn();
        }
        
        public void OnTriggerExit(Collider other)
        {
            PlayerTracking tracking = other.GetComponent<PlayerTracking>();
            if (!Utilities.IsValid(tracking)) return;
            isPushed = false;
        }
    }
}
