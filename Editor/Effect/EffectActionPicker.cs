using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>パターンのセルへ置くアクションを検索して選ぶ窓。</summary>
    public sealed class EffectActionPicker : EditorWindow
    {
        private EffectEditorWindow owner;
        private Action<EffectAction> onPick;
        private string search = "";
        private Vector2 scroll;
        private bool focused;

        public static void Show(EffectEditorWindow owner, Action<EffectAction> onPick)
        {
            var window = CreateInstance<EffectActionPicker>();
            window.titleContent = new GUIContent("アクションを選択");
            window.owner = owner;
            window.onPick = onPick;
            window.minSize = new Vector2(320, 360);
            window.wantsMouseMove = true;
            window.ShowUtility();
        }

        private void OnGUI()
        {
            var model = owner != null ? owner.Model : null;
            if (model == null)
            {
                Close();
                return;
            }

            // Enter で先頭を選ぶ / Esc で閉じる
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Close();
                return;
            }

            GUI.SetNextControlName("search");
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (!focused)
            {
                EditorGUI.FocusTextInControl("search");
                focused = true;
            }

            var hits = new List<EffectAction>();
            foreach (var a in model.Actions)
            {
                if (search.Length == 0 || a.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(a);
            }

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && hits.Count > 0)
            {
                Pick(hits[0]);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var a in hits)
            {
                var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
                if (rect.Contains(e.mousePosition)) EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f));
                GUI.Label(new Rect(rect.x + 8, rect.y, rect.width - 8, rect.height), a.Name);
                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    Pick(a);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(hits.Count + " / " + model.Actions.Count, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("＋ 新しいアクション"))
                {
                    var name = EffectModel.UniqueName(search.Length > 0 ? search : "アクション", model.ActionNames());
                    var created = new EffectAction { Name = name };
                    owner.Edit("アクションの追加", () => model.Actions.Add(created));
                    Pick(created);
                    GUIUtility.ExitGUI();
                }
            }

            if (e.type == EventType.MouseMove) Repaint();
        }

        private void Pick(EffectAction action)
        {
            var callback = onPick;
            Close();
            if (callback != null) callback(action);
        }

        private void OnLostFocus()
        {
            Close();
        }
    }
}
