
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// ReelOutputController.GetReelControlFlagName()が返す検索用フラグ名と、ReelControlDataの対応を保持する。
    /// </summary>
    public class ReelControl : UdonSharpBehaviour
    {
        [SerializeField]
        public string[] flagNames;
        
        [SerializeField]
        public ReelControlData[] reelControlDataList;
    }
}
