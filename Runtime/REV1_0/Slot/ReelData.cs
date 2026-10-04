
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 1本のリールに並ぶ図柄名を、物理的な並び順で保持する。
    /// </summary>
    public class ReelData : UdonSharpBehaviour
    {
        [SerializeField]
        public string[] symbols;
    }
}
