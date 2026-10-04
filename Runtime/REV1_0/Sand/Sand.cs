using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// カード、残高、貯メダル、持ちメダルを管理し、スロット本体へクレジットを供給するサンド。
    /// カードの挿入、排出、ネットワーク同期も担当する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class Sand : UdonSharpBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI balanceText, storedMedalText, inventoryMedalText;

        [SerializeField]
        private GameObject standbyPanel, usingPanel, exitingPanel, normalCardPanel, memberCardPanel,
            CanInsertPanel, CannotInsertPanel;

        [SerializeField]
        private VRCObjectPool normalCardPool, memberCardPool;

        /**
         * cardSpawnPointはカードの出現位置
         * cardExitPointはスポーン後移動させる位置
         */
        [SerializeField]
        private Transform cardSpawnPoint, cardExitPoint;

        [SerializeField]
        private MainBoard mainBoard;
        
        [SerializeField]
        private int moneyToCreditRate = 46;

        // ------------
        public bool isUsing
        {
            get => balance > 0 || storedMedal > 0 || inventoryMedal > 0;
        }

        [UdonSynced]
        public bool isMember = false;

        [UdonSynced]
        public int balance = 1000;

        [UdonSynced]
        public int storedMedal = 0;

        [UdonSynced]
        public int inventoryMedal = 0;

        private bool isCardOuting = false;
        private GameObject outingCardObj = null;
        private bool isExiting = false;

        private void FixedUpdate()
        {
            bool currentlyUsing = isUsing;
            standbyPanel.SetActive(!currentlyUsing);
            usingPanel.SetActive(currentlyUsing);
            exitingPanel.SetActive(isExiting);
            normalCardPanel.SetActive(!isMember);
            memberCardPanel.SetActive(isMember);
            CanInsertPanel.SetActive(balance < 10000);
            CannotInsertPanel.SetActive(balance >= 10000);
            balanceText.text = balance.ToString();
            storedMedalText.text = storedMedal.ToString();
            inventoryMedalText.text = inventoryMedal.ToString();
        }

        private void Update()
        {
            if (Utilities.IsValid(outingCardObj) && isCardOuting)
            {
                outingCardObj.transform.position =
                    Vector3.Lerp(outingCardObj.transform.position, cardExitPoint.position, 0.1f);
                outingCardObj.transform.rotation =
                    Quaternion.Lerp(outingCardObj.transform.rotation, cardExitPoint.rotation, 0.1f);
                if (Vector3.Distance(outingCardObj.transform.position, cardExitPoint.position) < 0.01f)
                {
                    outingCardObj.transform.position = cardExitPoint.position;
                    outingCardObj.transform.rotation = cardExitPoint.rotation;
                    isCardOuting = false;
                }
            }
        }

        public void AddBalance(int amount)
        {
            SetOwner();
            balance += amount;
            RequestSerialization();
        }

        public void AddStoredMedal(int amount)
        {
            SetOwner();
            storedMedal += amount;
            RequestSerialization();
        }

        public void AddInventoryMedal(int amount)
        {
            SetOwner();
            inventoryMedal += amount;
            RequestSerialization();
        }

        public void InsertCard(Card card)
        {
            SetOwner();
            if(balance > 0 && storedMedal > 0 && inventoryMedal > 0) return;
            GameObject cardObj = card.transform.parent.gameObject;
            balance = card.balance;
            storedMedal = card.storedMedal;
            inventoryMedal = card.inventoryMedal;
            isMember = card.isMember;
            RequestSerialization();
            VRCObjectPool cardPool = card.isMember ? memberCardPool : normalCardPool;
            cardPool.Return(cardObj);
        }

        public void MoneyUse()
        {
            SetOwner();
            if (balance < 1000) return;
            balance -= 1000;
            mainBoard.AddCredit(moneyToCreditRate);
            RequestSerialization();
        }
        
        public void StoredMedalUse()
        {
            SetOwner();
            int medal;
            if (inventoryMedal > 0)
            {
                medal = inventoryMedal > moneyToCreditRate ? moneyToCreditRate : inventoryMedal;
                inventoryMedal -= medal;
                mainBoard.AddCredit(medal);
            } else if (storedMedal > 0)
            { 
                medal = storedMedal > moneyToCreditRate ? moneyToCreditRate : storedMedal;
                storedMedal -= medal;
                mainBoard.AddCredit(medal);
            }
            RequestSerialization();
        }

        public void ExitCard()
        {
            if(balance <= 0 && storedMedal <= 0 && inventoryMedal <= 0) return;
            SetOwner();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ExitFlagOn));
            SendCustomEventDelayedSeconds(nameof(CardOutput), 1.75f);
        }

        public void ExitFlagOn()
        {
            isExiting = true;
        }
        
        public void ExitFlagOff()
        {
            isExiting = false;
        }

        public void CardOutput()
        {
            SetOwner();
            VRCObjectPool cardPool = isMember ? memberCardPool : normalCardPool;
            Networking.SetOwner(Networking.LocalPlayer, cardPool.gameObject);
            GameObject cardObj = cardPool.TryToSpawn();

            // 払出可能なカードがない場合はエラーにしたいけどとりあえず置き
            if (!Utilities.IsValid(cardObj))
            {
                // TODO
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ExitFlagOff));
                return;
            }
            Networking.SetOwner(Networking.LocalPlayer, cardObj);
            Card card = cardObj.GetComponentInChildren<Card>();
            card.balance = balance;
            card.storedMedal = storedMedal;
            card.inventoryMedal = inventoryMedal;
            card.isMember = isMember;
            balance = 0;
            storedMedal = 0;
            inventoryMedal = 0;
            isMember = false;
            cardObj.transform.position = cardSpawnPoint.position;
            cardObj.transform.rotation = cardSpawnPoint.rotation;
            outingCardObj = cardObj;
            isCardOuting = true;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ExitFlagOff));
        }

        public bool IsOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        public void SetOwner()
        {
            if (IsOwner()) return;
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }
}
