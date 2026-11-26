using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>
    /// SoundBoard のクリップを検索して選ぶドロップダウン。番号か名前で絞り込み、▶ で試聴できる。
    /// ↑↓ で移動、Enter で決定、Esc で閉じる。
    /// </summary>
    internal sealed class EffectSoundPicker : PopupWindowContent
    {
        /// <summary>試聴の目印（EffectBgmPreview の Owner）。</summary>
        internal const string PreviewOwner = "SoundBoard";

        private readonly AudioClip[] clips;
        private readonly int current;
        private readonly Action<int> onPick;
        private readonly float width;
        private string search = "";
        private Vector2 scroll;
        private bool focused;
        private int highlight = -1;
        private bool scrollToHighlight = true;

        private EffectSoundPicker(AudioClip[] clips, int current, Action<int> onPick, float width)
        {
            this.width = Mathf.Max(320, width);
            this.clips = clips;
            this.current = current;
            this.onPick = onPick;
            highlight = current;
        }

        internal static void Show(Rect activator, AudioClip[] clips, int current, Action<int> onPick)
        {
            PopupWindow.Show(activator, new EffectSoundPicker(clips, current, onPick, activator.width));
        }

        internal static string Label(AudioClip[] clips, int index)
        {
            if (index < 0 || index >= clips.Length) return index.ToString();
            return index + "  " + (clips[index] != null ? clips[index].name : "");
        }

        /// <summary>クリップを試聴する。同じクリップを鳴らしていれば止める。</summary>
        internal static void TogglePreview(AudioClip clip, int index)
        {
            if (EffectBgmPreview.IsPlaying(PreviewOwner, index))
            {
                EffectBgmPreview.Stop();
                return;
            }
            if (clip == null) return;
            var segment = new EffectBgmPreview.Segment { Clip = clip, From = 0, To = clip.samples, Row = -1 };
            EffectBgmPreview.Play(new List<EffectBgmPreview.Segment> { segment }, -1, PreviewOwner, index);
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(width, 380);
        }

        public override void OnGUI(Rect rect)
        {
            var e = Event.current;
            var hits = new List<int>();
            for (int i = 0; i < clips.Length; i++)
            {
                string name = clips[i] != null ? clips[i].name : "";
                if (search.Length == 0 || i.ToString() == search.Trim() || name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(i);
            }
            if (!hits.Contains(highlight)) highlight = hits.Count > 0 ? hits[0] : -1;

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape) { editorWindow.Close(); return; }
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && highlight >= 0) { Pick(highlight); return; }
                if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.UpArrow)
                {
                    int at = hits.IndexOf(highlight) + (e.keyCode == KeyCode.DownArrow ? 1 : -1);
                    if (hits.Count > 0) highlight = hits[Mathf.Clamp(at, 0, hits.Count - 1)];
                    scrollToHighlight = true;
                    e.Use();
                }
            }

            GUI.SetNextControlName("search");
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (!focused)
            {
                EditorGUI.FocusTextInControl("search");
                focused = true;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (int i in hits)
            {
                var row = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
                if (i == highlight) EditorGUI.DrawRect(row, new Color(0.17f, 0.36f, 0.53f));
                else if (i == current) EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.06f));
                else if (row.Contains(e.mousePosition)) EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.1f));
                var play = new Rect(row.xMax - 28, row.y + 1, 24, row.height - 2);
                using (new EditorGUI.DisabledScope(clips[i] == null))
                {
                    if (GUI.Button(play, EffectBgmPreview.IsPlaying(PreviewOwner, i) ? "■" : "▶")) TogglePreview(clips[i], i);
                }
                GUI.Label(new Rect(row.x + 8, row.y, row.width - 40, row.height), Label(clips, i));
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition) && !play.Contains(e.mousePosition))
                {
                    Pick(i);
                    GUIUtility.ExitGUI();
                }
                if (scrollToHighlight && i == highlight && e.type == EventType.Repaint)
                {
                    scrollToHighlight = false;
                    float view = rect.height - 50;
                    if (row.y < scroll.y) scroll.y = row.y;
                    else if (row.yMax > scroll.y + view) scroll.y = row.yMax - view;
                    editorWindow.Repaint();
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(hits.Count + " / " + clips.Length, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }

            if (e.type == EventType.MouseMove || EffectBgmPreview.Playing) editorWindow.Repaint();
        }

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;
        }

        public override void OnClose()
        {
            if (EffectBgmPreview.Owner == PreviewOwner) EffectBgmPreview.Stop();
        }

        private void Pick(int index)
        {
            var callback = onPick;
            editorWindow.Close();
            if (callback != null) callback(index);
        }
    }
}
