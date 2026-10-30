
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// Interact時に設定量のクレジットをメイン基板へ加算する補助コンポーネント。
    /// </summary>
    public class MakeCredit : UdonSharpBehaviour
    {
        [SerializeField]
        private MainBoard mainBoard;
        
        [SerializeField]
        private int creditAmount = 1;
        
        public override void Interact()
        {
            mainBoard.AddCredit(creditAmount);
        }
    }
}
