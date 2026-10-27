using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace AX.UdonSlot.REV1_0.Editor.Build
{
    /// <summary>
    /// ビルド前に、裏の UdonBehaviour にプログラムが入っていない UdonSharp スクリプトへプログラムを入れる。
    /// プログラムが無いと、ビルド時の設定コピーが失敗し、つながっているほかのスクリプトの設定まで VRChat に届かない。
    /// </summary>
    internal sealed class ProgramSourceRepair : IVRCSDKBuildRequestedCallback
    {
        // UdonSharp のビルド前コンパイル（100）の後。
        public int callbackOrder => 101;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType == VRCSDKRequestedBuildType.Avatar) return true;
            Repair();
            return true;
        }

        private static int Repair()
        {
            int count = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
                    {
                        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
                        if (backing == null || backing.programSource != null) continue;
                        var program = UdonSharpEditorUtility.GetUdonSharpProgramAsset(behaviour);
                        if (program == null)
                        {
                            Debug.LogWarning("[UdonSlot] プログラムアセットがありません: " + behaviour.GetType().Name, behaviour);
                            continue;
                        }
                        Undo.RecordObject(backing, "プログラム設定の修復");
                        backing.programSource = program;
                        EditorUtility.SetDirty(backing);
                        EditorSceneManager.MarkSceneDirty(scene);
                        Debug.Log("[UdonSlot] プログラムを設定しました: " + behaviour.name + " (" + behaviour.GetType().Name + ")", behaviour);
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
