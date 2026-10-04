using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    [CustomEditor(typeof(ReelControlProject))]
    public sealed class ReelControlProjectInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var project = (ReelControlProject)target;
            EditorGUILayout.LabelField("制御",project.flags.Count.ToString());
            if (GUILayout.Button("開く")) ReelControlProjectWindow.OpenAsset(project.GetInstanceID(),0);
        }
    }
}
