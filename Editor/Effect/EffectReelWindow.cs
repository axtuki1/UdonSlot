using System.Collections.Generic;
using AX.UdonSlot.REV1_0.Serialize;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>
    /// アクションのリールをキーフレームで編集する窓。
    /// 縦軸は左に並べたリール配列（上が21、下が1）、横軸は時間。値は中段に来るコマ。
    /// 相対キー（◇）は開始位置から上方向へのコマ数、絶対キー（□）は中段に来るコマ番号。
    /// 計算は実行時（EffectPlayer.ValueAt）と同じ：キーの間は等速、最初のキーより前はそのキーの値。
    /// </summary>
    public sealed class EffectReelWindow : EditorWindow
    {
        private static readonly Color[] ReelColors =
        {
            new Color(0.50f, 0.70f, 0.90f),
            new Color(0.88f, 0.66f, 0.35f),
            new Color(0.81f, 0.81f, 0.81f),
        };
        private static readonly Color GraphColor = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color RowLineColor = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color TickColor = new Color(0.33f, 0.33f, 0.33f);
        private static readonly Color StripColor = new Color(0.2f, 0.2f, 0.2f);
        private static readonly Color StripEditableColor = new Color(0.26f, 0.26f, 0.26f);
        private static readonly Color ReelFaceColor = new Color(0.9f, 0.88f, 0.83f);
        private static readonly Color ReelLineColor = new Color(0.78f, 0.75f, 0.68f);
        private static readonly Color PlayheadColor = new Color(0.88f, 0.35f, 0.35f);
        private static readonly string[] ModeNames = { "相対", "絶対" };

        private const float RowH = 18f;
        private const float StripW = 64f;
        private const float StripGap = 4f;
        private const float RulerH = 20f;
        private const float CellH = 40f;
        private const float FaceW = 90f;
        private const float KeySize = 10f;
        private const float DimAlpha = 0.25f;
        private const int TimeSnap = 10;

        [SerializeField] private string actionName;
        [SerializeField] private bool[] shown = { true, true, true };
        [SerializeField] private int[] startFrame = { 1, 1, 1 };

        private EffectEditorWindow owner;
        private EffectAction action;
        private float time;
        private bool playing;
        private double lastTick;

        private EffectReelKey selected;
        private int selectedReel = -1, selectedTime = -1;

        private bool dragging, dragMoved, scrubbing;
        private Vector2 dragStart;
        private int dragTime;
        private float dragValue;
        private float dragTotal;

        private Dictionary<string, Texture2D> pictures;
        private static GUIStyle stripLabel, faceLabel, tickLabel;

        public static void Open(EffectEditorWindow owner, EffectAction action)
        {
            var window = GetWindow<EffectReelWindow>();
            window.titleContent = new GUIContent("リール");
            window.minSize = new Vector2(820, 640);
            window.owner = owner;
            window.action = action;
            window.actionName = action.Name;
            window.selected = null;
            window.time = 0f;
            window.playing = false;
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += Repaint;
            pictures = null;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnFocus()
        {
            pictures = null;
        }

        private void Tick()
        {
            if (!playing) return;
            double now = EditorApplication.timeSinceStartup;
            time += (float)((now - lastTick) * 1000.0);
            lastTick = now;
            int length = action != null ? action.ReelLength : 0;
            if (time >= length)
            {
                time = length;
                playing = false;
            }
            Repaint();
        }

        // ------------------------------------------------------------ 参照

        private EffectModel Model { get { return owner != null ? owner.Model : null; } }

        /// <summary>メイン窓の読み直し（Undo など）でオブジェクトが替わっても、名前で追いかける。</summary>
        private bool Resolve()
        {
            if (owner == null)
            {
                var windows = Resources.FindObjectsOfTypeAll<EffectEditorWindow>();
                if (windows.Length > 0) owner = windows[0];
            }
            var model = Model;
            if (model == null) return false;
            if (action == null || !model.Actions.Contains(action)) action = model.Actions.Find(a => a.Name == actionName);
            if (action == null) return false;
            actionName = action.Name;
            if (selected != null && !action.ReelKeys.Contains(selected))
                selected = action.ReelKeys.Find(k => k.Reel == selectedReel && k.Time == selectedTime);
            if (selected != null)
            {
                selectedReel = selected.Reel;
                selectedTime = selected.Time;
            }
            return true;
        }

        private int SymbolCount
        {
            get
            {
                var model = Model;
                var mainBoard = model != null && model.Player != null ? model.Player.mainBoard : null;
                return mainBoard != null && mainBoard.pictureCount > 0 ? mainBoard.pictureCount : 21;
            }
        }

        private string SymbolAt(int reel, int frame, int n)
        {
            var model = Model;
            var mainBoard = model != null && model.Player != null ? model.Player.mainBoard : null;
            if (mainBoard == null || mainBoard.reelOutputController == null) return "";
            var datas = mainBoard.reelOutputController.reelDatas;
            if (datas == null || reel >= datas.Length || datas[reel] == null || datas[reel].symbols == null) return "";
            int index = n - frame;
            var symbols = datas[reel].symbols;
            return index >= 0 && index < symbols.Length ? symbols[index] : "";
        }

        private Texture2D Picture(string symbol)
        {
            if (pictures == null)
            {
                pictures = new Dictionary<string, Texture2D>();
                foreach (var guid in AssetDatabase.FindAssets("t:ReelPicture"))
                {
                    var p = AssetDatabase.LoadAssetAtPath<ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                    if (p != null && !string.IsNullOrEmpty(p.pictureName) && p.picture != null) pictures[p.pictureName] = p.picture;
                }
            }
            Texture2D texture;
            return symbol != null && pictures.TryGetValue(symbol, out texture) ? texture : null;
        }

        // ------------------------------------------------------------ 位置の計算（実行時と同じ）

        private static int WrapFrame(int frame, int n)
        {
            return ((frame - 1) % n + n) % n + 1;
        }

        /// <summary>0.5〜n+0.5 に折り返す。</summary>
        private static float WrapPosition(float p, int n)
        {
            float m = (p - 0.5f) % n;
            if (m < 0) m += n;
            return m + 0.5f;
        }

        private float Relative(EffectReelKey key, int n)
        {
            if (!key.Absolute) return key.Value;
            float delta = startFrame[key.Reel] - key.Value;
            float half = n / 2f;
            while (delta > half) delta -= n;
            while (delta < -half) delta += n;
            return delta;
        }

        private float RelativeAt(int reel, float t, int n)
        {
            float beforeTime = float.MinValue, afterTime = float.MaxValue;
            float beforeValue = 0f, afterValue = 0f;
            bool hasBefore = false, hasAfter = false;
            foreach (var k in action.ReelKeys)
            {
                if (k.Reel != reel) continue;
                float value = Relative(k, n);
                if (k.Time <= t && k.Time >= beforeTime) { beforeTime = k.Time; beforeValue = value; hasBefore = true; }
                if (k.Time >= t && k.Time < afterTime) { afterTime = k.Time; afterValue = value; hasAfter = true; }
            }
            if (!hasBefore && !hasAfter) return 0f;
            if (!hasBefore) return afterValue;
            if (!hasAfter || afterTime == beforeTime) return beforeValue;
            return beforeValue + (afterValue - beforeValue) * (t - beforeTime) / (afterTime - beforeTime);
        }

        /// <summary>中段に来るコマ（小数あり）。上へ1コマ動くと1つ下がる。</summary>
        private float CenterAt(int reel, float t, int n)
        {
            return startFrame[reel] - RelativeAt(reel, t, n);
        }

        private float Total
        {
            get
            {
                if (dragging) return dragTotal;
                int length = action.ReelLength;
                return Mathf.Max(2000f, Mathf.Ceil((length + 500) / 1000f) * 1000f);
            }
        }

        // ------------------------------------------------------------ 画面

        private void OnGUI()
        {
            if (!Resolve())
            {
                EditorGUILayout.Space();
                GUILayout.Label("演出エディタでアクションを開いてください。", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (shown == null || shown.Length != 3) shown = new[] { true, true, true };
            if (startFrame == null || startFrame.Length != 3) startFrame = new[] { 1, 1, 1 };

            int n = SymbolCount;
            for (int r = 0; r < 3; r++) startFrame[r] = WrapFrame(startFrame[r], n);
            HandleKeyboard();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(action.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
            }

            DrawPreview(n);
            DrawEditArea(n);
            DrawInspector(n);
        }

        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selected != null)
            {
                DeleteSelected();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Space)
            {
                TogglePlay();
                e.Use();
            }
        }

        private void DrawPreview(int n)
        {
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                for (int r = 0; r < 3; r++)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(FaceW)))
                    {
                        var face = GUILayoutUtility.GetRect(FaceW, CellH * 3, GUILayout.Width(FaceW));
                        DrawReelFace(face, r, n);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("−", EditorStyles.miniButtonLeft, GUILayout.Width(20))) startFrame[r] = WrapFrame(startFrame[r] - 1, n);
                            GUILayout.Label(startFrame[r].ToString(), StripLabel, GUILayout.Width(FaceW - 40));
                            if (GUILayout.Button("＋", EditorStyles.miniButtonRight, GUILayout.Width(20))) startFrame[r] = WrapFrame(startFrame[r] + 1, n);
                        }
                    }
                    GUILayout.Space(6);
                }

                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("|◀", GUILayout.Width(32))) { time = 0f; playing = false; }
                        using (new EditorGUI.DisabledScope(action.ReelLength <= 0))
                        {
                            if (GUILayout.Button(playing ? "❚❚" : "▶", GUILayout.Width(32))) TogglePlay();
                        }
                        GUILayout.Label(Mathf.RoundToInt(time) + " / " + action.ReelLength + " ms");
                        GUILayout.FlexibleSpace();
                    }
                }
            }
            GUILayout.Space(8);
        }

        private void TogglePlay()
        {
            if (playing)
            {
                playing = false;
                return;
            }
            if (action.ReelLength <= 0) return;
            if (time >= action.ReelLength) time = 0f;
            playing = true;
            lastTick = EditorApplication.timeSinceStartup;
        }

        private void DrawReelFace(Rect rect, int reel, int n)
        {
            EditorGUI.DrawRect(rect, ReelFaceColor);
            float center = CenterAt(reel, time, n);
            GUI.BeginClip(rect);
            int baseFrame = Mathf.FloorToInt(center);
            for (int k = baseFrame - 2; k <= baseFrame + 3; k++)
            {
                float y = rect.height / 2f - (k - center) * CellH;
                var cell = new Rect(0, y - CellH / 2f, rect.width, CellH);
                DrawSymbol(new Rect(cell.x + 4, cell.y + 3, cell.width - 8, cell.height - 6), SymbolAt(reel, WrapFrame(k, n), n), FaceLabel);
                EditorGUI.DrawRect(new Rect(0, cell.yMax - 1, rect.width, 1), ReelLineColor);
            }
            GUI.EndClip();
        }

        private void DrawSymbol(Rect rect, string symbol, GUIStyle style)
        {
            var texture = Picture(symbol);
            if (texture != null) ReelPicturePreview.Draw(rect, texture);
            else GUI.Label(rect, symbol, style);
        }

        private void DrawEditArea(int n)
        {
            float stripsW = StripW * 3 + StripGap * 2;
            float height = RulerH + RowH * n;
            var area = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
            area.xMin += 8;
            area.xMax -= 8;

            var stripsRect = new Rect(area.x, area.y, stripsW, height);
            var ruler = new Rect(stripsRect.xMax + 8, area.y, area.xMax - stripsRect.xMax - 8, RulerH);
            var graph = new Rect(ruler.x, ruler.yMax, ruler.width, RowH * n);

            DrawStrips(stripsRect, n);
            DrawRuler(ruler);
            DrawGraph(graph, n);
        }

        private void DrawStrips(Rect rect, int n)
        {
            var e = Event.current;
            for (int r = 0; r < 3; r++)
            {
                float x = rect.x + r * (StripW + StripGap);
                var header = new Rect(x, rect.y, StripW, RulerH);
                shown[r] = GUI.Toggle(header, shown[r], EffectModel.ReelNames[r]);

                bool editable = shown[r] && selected != null && selected.Reel == r;
                var old = GUI.color;
                if (!shown[r]) GUI.color = new Color(1f, 1f, 1f, 0.3f);
                for (int i = 0; i < n; i++)
                {
                    int frame = n - i;
                    var cell = new Rect(x, rect.y + RulerH + i * RowH, StripW, RowH);
                    EditorGUI.DrawRect(new Rect(cell.x, cell.y, cell.width, cell.height - 1), editable ? StripEditableColor : StripColor);
                    DrawSymbol(new Rect(cell.x + 2, cell.y + 1, cell.width - 4, cell.height - 3), SymbolAt(r, frame, n), StripLabel);
                    if (editable)
                    {
                        EditorGUIUtility.AddCursorRect(cell, MouseCursor.Link);
                        if (e.type == EventType.MouseDown && e.button == 0 && cell.Contains(e.mousePosition))
                        {
                            var key = selected;
                            Edit("リールキーの変更", delegate
                            {
                                key.Absolute = true;
                                key.Value = frame;
                            });
                            e.Use();
                        }
                    }
                }
                GUI.color = old;
            }
        }

        private void DrawRuler(Rect rect)
        {
            var e = Event.current;
            float total = Total;
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            for (int t = 0; t <= total; t += 1000)
            {
                float x = rect.x + rect.width * t / total;
                EditorGUI.DrawRect(new Rect(x, rect.y + 4, 1, rect.height - 4), TickColor);
                GUI.Label(new Rect(x + 3, rect.y + 2, 60, rect.height - 2), t.ToString(), TickLabel);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                scrubbing = true;
                playing = false;
                time = TimeAt(rect, e.mousePosition.x, total);
                e.Use();
            }
            else if (scrubbing && e.type == EventType.MouseDrag)
            {
                time = TimeAt(rect, e.mousePosition.x, total);
                e.Use();
                Repaint();
            }
            else if (scrubbing && e.rawType == EventType.MouseUp)
            {
                scrubbing = false;
            }
        }

        private static float TimeAt(Rect rect, float x, float total)
        {
            return Mathf.Clamp((x - rect.x) / rect.width, 0f, 1f) * total;
        }

        private void DrawGraph(Rect rect, int n)
        {
            var e = Event.current;
            float total = Total;
            EditorGUI.DrawRect(rect, GraphColor);
            for (int i = 1; i < n; i++) EditorGUI.DrawRect(new Rect(rect.x, rect.y + i * RowH, rect.width, 1), RowLineColor);
            for (int t = 1000; t < total; t += 1000) EditorGUI.DrawRect(new Rect(rect.x + rect.width * t / total, rect.y, 1, rect.height), RowLineColor);

            // 線とキー（グラフ内の座標で描く）
            GUI.BeginClip(rect);
            if (e.type == EventType.Repaint)
            {
                for (int r = 0; r < 3; r++) DrawCurve(r, rect, n, total);
                for (int r = 0; r < 3; r++) DrawKeys(r, rect, n, total);
            }
            EditorGUI.DrawRect(new Rect(rect.width * time / total - 1, 0, 2, rect.height), PlayheadColor);
            GUI.EndClip();

            HandleGraphMouse(rect, n, total);
        }

        private float X(Rect rect, float t, float total) { return rect.width * t / total; }

        private static float Y(float p, int n) { return (n + 0.5f - p) * RowH; }

        private Color ReelColor(int reel)
        {
            var c = ReelColors[reel];
            if (!shown[reel]) c.a = DimAlpha;
            return c;
        }

        private void DrawCurve(int reel, Rect rect, int n, float total)
        {
            var keys = action.ReelKeys.FindAll(k => k.Reel == reel);
            keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            Handles.color = ReelColor(reel);
            float start = startFrame[reel];
            if (keys.Count == 0)
            {
                Segment(rect, 0, start, total, start, n, total);
                return;
            }
            float first = start - Relative(keys[0], n);
            if (keys[0].Time > 0) Segment(rect, 0, first, keys[0].Time, first, n, total);
            for (int i = 1; i < keys.Count; i++)
            {
                Segment(rect, keys[i - 1].Time, start - Relative(keys[i - 1], n), keys[i].Time, start - Relative(keys[i], n), n, total);
            }
            float last = start - Relative(keys[keys.Count - 1], n);
            Segment(rect, keys[keys.Count - 1].Time, last, total, last, n, total);
        }

        /// <summary>配列の上下端をまたぐ所で線を切り、反対側から続ける。</summary>
        private void Segment(Rect rect, float ta, float pa, float tb, float pb, int n, float total)
        {
            float top = n + 0.5f, bottom = 0.5f;
            float current = WrapPosition(pa, n);
            float target = current + (pb - pa);
            float xa = X(rect, ta, total), xb = X(rect, tb, total);
            int guard = 0;
            while ((target > top || target < bottom) && guard++ < 64)
            {
                float edge = target > top ? top : bottom;
                float xm = xa + (xb - xa) * (edge - current) / (target - current);
                Line(xa, Y(current, n), xm, Y(edge, n));
                if (edge == top) { current = bottom; target -= n; }
                else { current = top; target += n; }
                xa = xm;
            }
            Line(xa, Y(current, n), xb, Y(target, n));
        }

        private static void Line(float x1, float y1, float x2, float y2)
        {
            Handles.DrawAAPolyLine(2.5f, new Vector3(x1, y1), new Vector3(x2, y2));
        }

        private Vector2 KeyPosition(EffectReelKey key, Rect rect, int n, float total)
        {
            float p = WrapPosition(startFrame[key.Reel] - Relative(key, n), n);
            return new Vector2(X(rect, key.Time, total), Y(p, n));
        }

        private void DrawKeys(int reel, Rect rect, int n, float total)
        {
            var color = ReelColor(reel);
            var outline = new Color(0.1f, 0.1f, 0.1f, color.a);
            foreach (var key in action.ReelKeys)
            {
                if (key.Reel != reel) continue;
                var c = KeyPosition(key, rect, n, total);
                bool on = key == selected;
                float size = on ? KeySize + 4 : KeySize;
                Shape(c, size + 2, key.Absolute, on ? Color.white : outline);
                Shape(c, on ? size - 2 : size, key.Absolute, color);
            }
        }

        private static void Shape(Vector2 c, float size, bool square, Color color)
        {
            float h = size / 2f;
            if (square)
            {
                EditorGUI.DrawRect(new Rect(c.x - h, c.y - h, size, size), color);
                return;
            }
            // ◇ は少し大きめに（同じ見た目の大きさにする）
            h *= 1.3f;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                new Vector3(c.x, c.y - h), new Vector3(c.x + h, c.y),
                new Vector3(c.x, c.y + h), new Vector3(c.x - h, c.y));
        }

        private EffectReelKey HitKey(Vector2 mouse, Rect rect, int n, float total)
        {
            EffectReelKey hit = null;
            float best = KeySize;
            foreach (var key in action.ReelKeys)
            {
                if (!shown[key.Reel]) continue;
                var c = KeyPosition(key, rect, n, total) + rect.position;
                float d = Vector2.Distance(c, mouse);
                if (d <= best) { best = d; hit = key; }
            }
            return hit;
        }

        private void HandleGraphMouse(Rect rect, int n, float total)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!rect.Contains(e.mousePosition)) break;
                    if (e.button == 0)
                    {
                        var hit = HitKey(e.mousePosition, rect, n, total);
                        playing = false;
                        if (hit != null)
                        {
                            Select(hit);
                            time = hit.Time;
                            dragging = true;
                            dragMoved = false;
                            dragStart = e.mousePosition;
                            dragTime = hit.Time;
                            dragValue = hit.Value;
                            dragTotal = total;
                        }
                        else
                        {
                            Select(null);
                            time = TimeAt(rect, e.mousePosition.x, total);
                        }
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (!dragging || selected == null) break;
                    {
                        float dt = (e.mousePosition.x - dragStart.x) / rect.width * total;
                        int newTime = Mathf.Max(0, Mathf.RoundToInt((dragTime + dt) / TimeSnap) * TimeSnap);
                        int rows = Mathf.RoundToInt((e.mousePosition.y - dragStart.y) / RowH);
                        // 下へ動かすとコマ番号は下がる＝上方向への移動は増える
                        float value = selected.Absolute ? WrapFrame(Mathf.RoundToInt(dragValue) - rows, n) : dragValue + rows;
                        if (newTime != selected.Time || value != selected.Value) dragMoved = true;
                        selected.Time = newTime;
                        selected.Value = value;
                        selectedTime = newTime;
                        time = newTime;
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (!dragging) break;
                    dragging = false;
                    if (dragMoved) Edit("リールキーの移動", delegate { });
                    e.Use();
                    break;

                case EventType.ContextClick:
                    if (!rect.Contains(e.mousePosition)) break;
                    {
                        var hit = HitKey(e.mousePosition, rect, n, total);
                        int at = Mathf.RoundToInt(TimeAt(rect, e.mousePosition.x, total) / TimeSnap) * TimeSnap;
                        var menu = new GenericMenu();
                        if (hit != null)
                        {
                            Select(hit);
                            menu.AddItem(new GUIContent("削除"), false, DeleteSelected);
                        }
                        else
                        {
                            for (int r = 0; r < 3; r++)
                            {
                                if (!shown[r]) continue;
                                int reel = r;
                                menu.AddItem(new GUIContent("キーを追加/" + EffectModel.ReelNames[r]), false, () => AddKey(reel, at, n));
                            }
                        }
                        menu.ShowAsContext();
                        e.Use();
                    }
                    break;
            }
        }

        private void DrawInspector(int n)
        {
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                if (GUILayout.Button("キー＋", GUILayout.Width(70)))
                {
                    int at = Mathf.RoundToInt(time / TimeSnap) * TimeSnap;
                    for (int r = 0; r < 3; r++) if (shown[r]) AddKey(r, at, n);
                }
                GUILayout.Space(16);

                var key = selected;
                if (key != null)
                {
                    var label = new GUIStyle(EditorStyles.boldLabel);
                    label.normal.textColor = ReelColors[key.Reel];
                    GUILayout.Label(EffectModel.ReelNames[key.Reel], label, GUILayout.Width(24));

                    GUILayout.Label("時間", GUILayout.Width(30));
                    EditorGUI.BeginChangeCheck();
                    int t = EditorGUILayout.DelayedIntField(key.Time, GUILayout.Width(70));
                    if (EditorGUI.EndChangeCheck()) Edit("リールキーの変更", () => { key.Time = Mathf.Max(0, t); });
                    GUILayout.Label("ms", GUILayout.Width(24));
                    GUILayout.Space(12);

                    int mode = GUILayout.Toolbar(key.Absolute ? 1 : 0, ModeNames, GUILayout.Width(110));
                    if (mode != (key.Absolute ? 1 : 0))
                    {
                        bool absolute = mode == 1;
                        float value = absolute
                            ? WrapFrame(Mathf.RoundToInt(startFrame[key.Reel] - key.Value), n)
                            : Relative(key, n);
                        Edit("リールキーの変更", delegate
                        {
                            key.Absolute = absolute;
                            key.Value = value;
                        });
                    }
                    GUILayout.Space(12);

                    if (key.Absolute)
                    {
                        GUILayout.Label("コマ番号", GUILayout.Width(52));
                        EditorGUI.BeginChangeCheck();
                        int frame = EditorGUILayout.DelayedIntField(Mathf.RoundToInt(key.Value), GUILayout.Width(50));
                        if (EditorGUI.EndChangeCheck()) Edit("リールキーの変更", () => { key.Value = WrapFrame(frame, n); });
                        GUILayout.Label(SymbolAt(key.Reel, Mathf.RoundToInt(key.Value), n), GUILayout.Width(80));
                    }
                    else
                    {
                        GUILayout.Label("移動", GUILayout.Width(30));
                        EditorGUI.BeginChangeCheck();
                        float value = EditorGUILayout.DelayedFloatField(key.Value, GUILayout.Width(60));
                        if (EditorGUI.EndChangeCheck()) Edit("リールキーの変更", () => { key.Value = value; });
                        GUILayout.Label("コマ", GUILayout.Width(30));
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("削除", GUILayout.Width(60))) DeleteSelected();
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }
                GUILayout.Space(8);
            }
        }

        // ------------------------------------------------------------ 編集

        private void Edit(string label, System.Action change)
        {
            if (owner == null) return;
            owner.Edit(label, change);
            Repaint();
        }

        private void Select(EffectReelKey key)
        {
            selected = key;
            selectedReel = key != null ? key.Reel : -1;
            selectedTime = key != null ? key.Time : -1;
            GUI.FocusControl(null);
        }

        /// <summary>今の線の上にキーを置く（見た目は変わらない）。同じ時間のキーがあれば何もしない。</summary>
        private void AddKey(int reel, int at, int n)
        {
            if (action.ReelKeys.Exists(k => k.Reel == reel && k.Time == at)) return;
            var key = new EffectReelKey { Reel = reel, Time = at, Value = Mathf.Round(RelativeAt(reel, at, n) * 100f) / 100f };
            Edit("リールキーの追加", () => action.ReelKeys.Add(key));
            Select(key);
        }

        private void DeleteSelected()
        {
            var key = selected;
            if (key == null) return;
            Edit("リールキーの削除", () => action.ReelKeys.Remove(key));
            Select(null);
        }

        // ------------------------------------------------------------ 見た目

        private static GUIStyle StripLabel
        {
            get
            {
                if (stripLabel == null)
                {
                    stripLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
                }
                return stripLabel;
            }
        }

        private static GUIStyle FaceLabel
        {
            get
            {
                if (faceLabel == null)
                {
                    faceLabel = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
                    faceLabel.normal.textColor = new Color(0.13f, 0.13f, 0.13f);
                }
                return faceLabel;
            }
        }

        private static GUIStyle TickLabel
        {
            get
            {
                if (tickLabel == null)
                {
                    tickLabel = new GUIStyle(EditorStyles.miniLabel);
                    tickLabel.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                }
                return tickLabel;
            }
        }
    }
}
