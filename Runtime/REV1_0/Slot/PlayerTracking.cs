
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// オブジェクトをローカルプレイヤーの指定ボーンへ追従させる。
    /// 物理ボタン操作時のローカル Owner 判定にも使用する。
    /// </summary>
    public class PlayerTracking : UdonSharpBehaviour
    {
        [SerializeField]
        private bool isTracking = true;
        [SerializeField]
        private HumanBodyBones trackingBone = HumanBodyBones.RightIndexDistal;
        [SerializeField]
        private GameObject referenceObject;
        
        private void Update()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer) || !isTracking) return;
            transform.position = Networking.LocalPlayer.GetBonePosition(trackingBone);
            transform.rotation = Networking.LocalPlayer.GetBoneRotation(trackingBone);
        }

        public bool IsOwner()
        {
            if (Utilities.IsValid(referenceObject))
            {
                return Networking.IsOwner(referenceObject);
            }
            return Networking.IsOwner(gameObject);
        }
    }
}
