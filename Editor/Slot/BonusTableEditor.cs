using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>BonusTable は機種エディタの「ボーナス・RT」で編集する。</summary>
    [CustomEditor(typeof(BonusTable))]
    public class BonusTableEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            if (GUILayout.Button("機種エディタで開く")) AX.UdonSlot.REV1_0.Editor.ReelRule.ReelRuleEditorWindow.OpenFor(target);
        }
    }
}
