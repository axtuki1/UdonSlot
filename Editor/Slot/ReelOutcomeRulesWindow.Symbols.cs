using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    public sealed partial class ReelOutcomeRulesWindow
    {
        private readonly int[] symbolPreview = new int[3];
        private static readonly string[] Rows = {"上段", "中段", "下段"};

        private void DrawSymbolConditions(ReelOutcomeRules.Shape shape, int reel, float width)
        {
            for (int row = 0; row < 3; row++)
            {
                var cell = shape.cells[reel*3+row];
                Rect rect = GUILayoutUtility.GetRect(width,70,GUILayout.Width(width));
                string caption = cell.any ? "ANY" : cell.symbols.Count == 0 ? "未選択" : cell.symbols.Count+"図柄（OR）";
                if (GUI.Button(rect,GUIContent.none))
                    PopupWindow.Show(rect,new SymbolPicker(this,book,shape.id,reel,row));
                GUI.Label(new Rect(rect.x+4,rect.y+2,width-8,20),Rows[row]+" : "+caption,EditorStyles.miniLabel);
                if (cell.any || cell.symbols.Count == 0)
                    GUI.Label(new Rect(rect.x+4,rect.y+26,width-8,35),cell.any ? "ANY" : "クリックして選択",EditorStyles.centeredGreyMiniLabel);
                else
                {
                    int shown = Mathf.Min(3,cell.symbols.Count);
                    for (int i = 0; i < shown; i++)
                    {
                        var imageRect = new Rect(rect.x+4+i*(width-8)/shown,rect.y+24,(width-8)/shown,38);
                        Texture2D texture;
                        if (pictures.TryGetValue(cell.symbols[i],out texture) && texture != null) ReelPicturePreview.Draw(imageRect,texture);
                        else GUI.Label(imageRect,cell.symbols[i],EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            var strip = book.reels[reel].symbols;
            var candidates = shape.Candidates(reel,strip);
            GUILayout.Label("該当停止位置: "+candidates.Count+"か所",EditorStyles.miniLabel);
            if (candidates.Count == 0)
            {
                GUILayout.Label("配列に該当なし",EditorStyles.wordWrappedMiniLabel);
                return;
            }
            if (!candidates.Contains(symbolPreview[reel])) symbolPreview[reel] = candidates[0];
            // Candidate buttons are explicit physical lower positions; selecting one only changes the preview.
            for (int i = 0; i < candidates.Count; i += 3)
                using (new EditorGUILayout.HorizontalScope())
                    for (int j = i; j < Mathf.Min(i+3,candidates.Count); j++)
                    {
                        int candidate = candidates[j];
                        Color old = GUI.backgroundColor;
                        if (candidate == symbolPreview[reel]) GUI.backgroundColor = new Color(1f,0.7f,0.3f);
                        if (GUILayout.Button((strip.Length-candidate).ToString(),GUILayout.Width((width-8)/3))) symbolPreview[reel] = candidate;
                        GUI.backgroundColor = old;
                    }
            GUILayout.Label("例: 下段 "+(strip.Length-symbolPreview[reel])+"番",EditorStyles.miniLabel);
            for (int row = 0; row < 3; row++)
                DrawSymbol(strip[ReelControlDesign.Normalize(symbolPreview[reel]+row-2,strip.Length)],30);
        }

        private sealed class SymbolPicker : PopupWindowContent
        {
            private readonly ReelOutcomeRulesWindow owner;
            private readonly ReelOutcomeRules source;
            private readonly string shapeId;
            private readonly int reel, row;
            private Vector2 scroll;
            public SymbolPicker(ReelOutcomeRulesWindow window, ReelOutcomeRules rules, string id, int r, int line)
            { owner = window; source = rules; shapeId = id; reel = r; row = line; }
            public override Vector2 GetWindowSize() { return new Vector2(350,360); }
            public override void OnGUI(Rect rect)
            {
                if (source == null || owner == null) { editorWindow.Close(); return; }
                var shape = source.shapes.Find(s => s.id == shapeId);
                if (shape == null) { editorWindow.Close(); return; }
                var cell = shape.cells[reel*3+row];
                GUILayout.Label(Names[reel]+"リール・"+Rows[row],EditorStyles.boldLabel);
                GUILayout.Label("選んだ図柄のどれかに一致。別の段の条件も同時に満たします。",EditorStyles.wordWrappedMiniLabel);
                bool any = EditorGUILayout.ToggleLeft("ANY（この段を問わない）",cell.any);
                if (any != cell.any)
                {
                    Undo.RecordObject(source,"段のANYを変更"); cell.any = any;
                    EditorUtility.SetDirty(source); owner.Repaint();
                }
                if (!cell.any && cell.symbols.Count == 0) EditorGUILayout.HelpBox("図柄を選択してください。未選択はANYとは異なります。",MessageType.Warning);
                var names = new List<string>();
                foreach (var symbol in source.reels[reel].symbols) if (!names.Contains(symbol)) names.Add(symbol);
                foreach (var symbol in cell.symbols) if (!names.Contains(symbol)) names.Add(symbol);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (string symbol in names)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool before = !cell.any && cell.symbols.Contains(symbol);
                        bool after = EditorGUILayout.Toggle(before,GUILayout.Width(20),GUILayout.Height(30));
                        if (before != after)
                        {
                            Undo.RecordObject(source,"段の図柄を選択");
                            if (cell.any) cell.symbols.Clear();
                            cell.any = false;
                            if (after && !cell.symbols.Contains(symbol)) cell.symbols.Add(symbol);
                            if (!after) cell.symbols.Remove(symbol);
                            EditorUtility.SetDirty(source); owner.Repaint();
                        }
                        Rect icon = GUILayoutUtility.GetRect(68,30,GUILayout.Width(68));
                        Texture2D texture;
                        if (owner.pictures.TryGetValue(symbol,out texture) && texture != null) ReelPicturePreview.Draw(icon,texture);
                        GUILayout.Label(symbol,GUILayout.Height(30));
                    }
                }
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("閉じる")) editorWindow.Close();
            }
        }
    }
}
