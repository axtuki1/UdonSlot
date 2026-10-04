using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 所有中のカードが一定時間Trigger内に留まったとき、Sandへカード情報を挿入する。
    /// </summary>
    public class CardInsertPoint : UdonSharpBehaviour
    {
        [SerializeField]
        private float insertDuration = 1.0f;

        [SerializeField]
        private Sand sand;

        private bool isCardInserting = false;
        private float insertStartTime;

        public void OnTriggerEnter(Collider other)
        {
            isCardInserting = true;
            Card enteredCard = other.GetComponentInChildren<Card>();
            if (!Utilities.IsValid(enteredCard) || !Networking.IsOwner(enteredCard.gameObject)) return;
            insertStartTime = Networking.GetServerTimeInMilliseconds();
        }

        public void OnTriggerStay(Collider other)
        {
            if (isCardInserting)
            {
                Card card = other.GetComponentInChildren<Card>();
                if (!Utilities.IsValid(card) || !Networking.IsOwner(card.gameObject)) return;
                float elapsedTime = (Networking.GetServerTimeInMilliseconds() - insertStartTime) / 1000.0f;
                if (elapsedTime >= insertDuration)
                {
                    isCardInserting = false;
                    sand.InsertCard(card);
                }
            }
        }
    }
}
