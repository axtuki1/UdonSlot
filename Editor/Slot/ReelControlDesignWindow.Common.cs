using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    public partial class ReelControlDesignWindow
    {
        private void DrawCommonSettingsControls()
        {
            if (!design.laterStopsInitialized) return;
            if (!design.commonSettingsEnabled)
            {
                EditorGUILayout.HelpBox("リールごとの第1停止を共通設定にすると、全押し順・全段階で使えます。既存の異なる設定は例外として保持します。", MessageType.Info);
                if (GUILayout.Button("共通設定を使う（既存の差分は保持）"))
                {
                    Undo.RecordObject(design, "共通設定へ移行");
                    design.EnableCommonSettings(); editCommon = true;
                    SelectStage(activeReel * 2, 0);
                    EditorUtility.SetDirty(design);
                }
                return;
            }
            bool common = GUILayout.Toolbar(editCommon ? 0 : 1, new[] { "共通設定（押し順に共通）", "条件別設定（例外）" }) == 0;
            if (common != editCommon)
            {
                editCommon = common; lastPress = -1;
                if (common) SelectStage(activeReel * 2, 0);
            }
            if (!editCommon) return;
            int exceptions = design.ExceptionCount(activeReel);
            EditorGUILayout.LabelField("共通設定は役を問わず使用できます。条件別の上書きは、条件ごとの滑り表全体を保持します。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(ReelNames[activeReel] + "リール: 例外 " + exceptions + "件（共通値の変更は例外に反映されません）", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(exceptions == 0))
                    if (GUILayout.Button("このリールの例外を共通に戻す", GUILayout.Width(215)))
                    {
                        Undo.RecordObject(design, "リールの例外を共通に戻す");
                        design.UseCommonForReel(activeReel);
                        EditorUtility.SetDirty(design); lastPress = -1;
                        message = ReelNames[activeReel] + "リールを全押し順で共通にしました。Undoで戻せます。";
                    }
            }
        }
        private bool CommonOutputMatches()
        {
            if (output == null) return false;
            for (int r = 0; r < 3; r++) if (!design.OutputMatches(output, r)) return false;
            foreach (var table in design.laterStops)
                if (table.exportedDesign != design.LaterSignature(table.pattern) ||
                    table.exportedOutput != ReelControlDesign.OutputSignature(output, table.pattern)) return false;
            return true;
        }
    }
}
