using TMPro;
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 指定Transformのオブジェクト名をTextMeshProへ表示する簡易デバッグビュー。
    /// </summary>
    public class DebugTextView : UdonSharpBehaviour
    {
        [SerializeField]
        private Transform target;

        private TextMeshProUGUI text;
        
        private void Start()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        private void Update()
        {
            text.text = target.name;
        }
    }
}

