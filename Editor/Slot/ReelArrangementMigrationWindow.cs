using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    public class ReelArrangementMigrationWindow : EditorWindow
    {
        private ReelControlDesign source;
        private ReelOutputController controller;
        private string[][] symbols;
        private int[][] maps;
        private Vector2 scroll;
        private string error, snapshot;
        private static readonly string[] Names = {"左", "中", "右"};
        public static void Open(ReelControlDesign design, ReelOutputController machine)
        {
            var w = GetWindow<ReelArrangementMigrationWindow>("配列変更の移行");
            w.source = design; w.controller = machine;
            w.symbols = new string[3][]; w.maps = new int[3][];
            for (int r = 0; r < 3; r++)
            {
                w.symbols[r] = (string[])machine.reelDatas[r].symbols.Clone();
                w.maps[r] = ReelArrangementMigration.DefaultMap(design.reels[r].symbols, w.symbols[r]);
            }
            w.snapshot = EditorJsonUtility.ToJson(design); w.error = null;
            w.minSize = new Vector2(650,500); w.Show();
        }
        private bool Fresh()
        {
            if (source == null || controller == null || snapshot != EditorJsonUtility.ToJson(source)) return false;
            for (int r = 0; r < 3; r++)
            {
                var current = controller.reelDatas[r].symbols;
                if (current.Length != symbols[r].Length) return false;
                for (int p = 0; p < current.Length; p++) if (current[p] != symbols[r][p]) return false;
            }
            return true;
        }
        private void Apply(ReelArrangementMigration.Result migration)
        {
            if (!Fresh()) { error = "元データが変わりました。移行画面を開き直してください。"; return; }
            string path = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path)) { error = "先に編集データをアセットとして保存してください。"; return; }
            string backupPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path, null) + "_BeforeArrangement.asset");
            var backup = Instantiate(source);
            AssetDatabase.CreateAsset(backup, backupPath);
            AssetDatabase.SaveAssetIfDirty(backup);
            Undo.RecordObject(source, "リール配列を移行");
            source.reels = migration.reels; source.laterStops = migration.tables;
            source.exportedSignatures = new string[3];
            source.reviews = new List<ReelControlDesign.PendingReview>();
            for (int r = 0; r < 3; r++)
                source.reviews.Add(new ReelControlDesign.PendingReview { order = r*2, stage = 0,
                    positions = new[] {-1,-1,-1}, reason = "配列移行後の第1停止を確認してください。旧データ: " + backupPath });
            if (source.laterStopsInitialized)
                for (int o = 0; o < 6; o++) for (int s = 1; s <= 2; s++)
                {
                    var contexts = source.ReachableContexts(o,s);
                    if (contexts.Count > 0) source.reviews.Add(new ReelControlDesign.PendingReview { order=o, stage=s,
                        positions=contexts[0], reason="配列移行後のこの停止パターン全条件を確認してください。表示先は代表条件です。旧データ: " + backupPath });
                }
            source.generationReport = "配列移行: 未設定 " + migration.unresolved + "セル / 移行できない条件 " +
                migration.removedConditions + "件。旧値・旧確認記録は " + backupPath + " に保存。入賞検証を再実行してください。";
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            Selection.activeObject = source;
            Close();
        }
        private void OnGUI()
        {
            if (source == null) { EditorGUILayout.HelpBox("設計画面から開いてください。",MessageType.Info); return; }
            EditorGUILayout.HelpBox("旧位置 → 新位置を指定します。同じコマ数では番号を維持する初期値です。図柄の移動を追いたい箇所は対応先を変更してください。押下位置と停止先の対応から滑り数を再計算します。",MessageType.Info);
            EditorGUILayout.HelpBox("対応先なし・4コマを超える滑りは未設定へ。対応先のない後段条件は旧データに保存します。移行後は要確認になり、ランタイムへの反映は通常のデータ出力で行います。",MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int r=0;r<3;r++)
            {
                EditorGUILayout.LabelField(Names[r]+"リール",EditorStyles.boldLabel);
                var labels = new string[symbols[r].Length+1]; labels[0]="対応先なし";
                for(int n=0;n<symbols[r].Length;n++) labels[n+1]=(symbols[r].Length-n)+"番 "+symbols[r][n];
                for(int p=0;p<maps[r].Length;p++)
                    maps[r][p]=EditorGUILayout.Popup((maps[r].Length-p)+"番 "+source.reels[r].symbols[p],maps[r][p]+1,labels)-1;
            }
            EditorGUILayout.EndScrollView();
            ReelArrangementMigration.Result result = null;
            string buildError = null;
            try { result=ReelArrangementMigration.Build(source,symbols,maps); }
            catch(Exception ex) { buildError=ex.Message; }
            if(buildError!=null) EditorGUILayout.HelpBox(buildError,MessageType.Warning);
            if(error!=null) EditorGUILayout.HelpBox(error,MessageType.Warning);
            if(result!=null) EditorGUILayout.LabelField("移行後の未設定: "+result.unresolved+"セル / 移行できない条件: "+result.removedConditions+"件");
            if(!Fresh()) EditorGUILayout.HelpBox("元データが変わりました。開き直してください。",MessageType.Warning);
            using(new EditorGUI.DisabledScope(result==null || !Fresh() || Application.isPlaying))
                if(GUILayout.Button("この対応で移行・保存（旧データを別アセットに保管）"))
                    try { Apply(result); } catch(Exception ex) { error=ex.Message; }
        }
    }
}
