
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// サンドへ受け渡す会員種別、残高、貯メダル、持ちメダルを保持するカード。
    /// </summary>
    public class Card : UdonSharpBehaviour
    {
        /**
         * メンバーズカードかどうか
         */
        [SerializeField]
        public bool isMember = false;
        /**
         * 残高
         */
        [SerializeField]
        public int balance = 0;
        /**
         * 貯メダル
         */
        [SerializeField]
        public int storedMedal = 0;
        /**
         * 持メダル
         */
        [SerializeField]
        public int inventoryMedal = 0;

        private void FixedUpdate()
        {
            if (Networking.IsOwner(transform.parent.gameObject) && !Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }
    }
}
