
using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// Interact時に指定したUdonBehaviourのカスタムイベントをローカル実行する汎用ボタン。
    /// </summary>
    public class ButtonPush : UdonSharpBehaviour
    {
        [SerializeField]
        private UdonBehaviour udon;
        [SerializeField]
        private string methodName;

        public override void Interact()
        {
            udon.SendCustomEvent(methodName);
        }
    }
}
