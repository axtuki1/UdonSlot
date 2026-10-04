using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// Interact時に指定UdonBehaviourのイベントをローカルまたは全クライアントで実行する。
    /// </summary>
    public class CallUdon : UdonSharpBehaviour
    {
        [SerializeField]
        private bool isNetworkAll = false;
        
        [SerializeField]
        private UdonBehaviour udon;
        
        [SerializeField]
        private string methodName;
        
        public override void Interact()
        {
            if (!Utilities.IsValid(udon)) return;
            if (isNetworkAll)
            {
                udon.SendCustomNetworkEvent(NetworkEventTarget.All, methodName);
            }
            else
            {
                udon.SendCustomEvent(methodName);
            }
        }
    }
}

