
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace AX.UdonSlot.REV1_0.WatchDog
{
    /// <summary>
    /// WatchDogのテスト用。null参照を発生させる。
    /// </summary>
    public class WatchDogTest : UdonSharpBehaviour
    {
        private GameObject alwaysNullVariable;
        public void InvokeError()
        {
            // nullからメソッドを呼ぼうとして死ぬ。
            alwaysNullVariable.SetActive(true);
        }
    }
}