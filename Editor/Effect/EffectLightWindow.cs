using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>
    /// アクションのリール照明をキーフレームで編集する窓（Animation ウィンドウと同じ操作）。
    /// 行がトラック（表示窓のコマの明るさ・入賞図柄・入賞図柄以外・上下照明の色）、横軸が時間。
    /// 計算は実行時（EffectPlayer.TrackValue）と同じ：キーの間はリニアか切り替え、最初のキーより前は最初のキーの値。
    /// </summary>
    public sealed class EffectLightWindow : EditorWindow
    {
        private static readonly Color GraphColor = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color RowColor = new Color(0.19f, 0.19f, 0.19f);
        private static readonly Color RowSelectedColor = new Color(0.17f, 0.36f, 0.53f);
        private static readonly Color RowLineColor = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color SnapLineColor = new Color(0.19f, 0.19f, 0.19f);
        private static readonly Color TickColor = new Color(0.33f, 0.33f, 0.33f);
        private static readonly Color HeaderColor = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color PlayheadColor = new Color(0.88f, 0.35f, 0.35f);
        private static readonly Color CurveColor = new Color(0.95f, 0.8f, 0.4f);
        private static readonly Color KeyColor = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color WinOutlineColor = new Color(0.95f, 0.75f, 0.3f);
        private static readonly string[] LoopNames = { "1回", "ループ" };
        private static readonly string[] EndNames = { "戻す", "そのまま" };
        private static readonly string[] HoldNames = { "リニア", "切り替え" };
        private static readonly string[] RowNames = { "上", "中", "下" };

        private const float NameW = 150f;
        private const float RulerH = 20f;
        private const float TrackH = 22f;
        private const float KeySize = 10f;
        private const float CellW = 54f;
        private const float CellH = 34f;
        private const float BandH = 8f;
        private static readonly int[] SnapSteps = { 1, 10, 50, 100, 500, 1000 };
        private static readonly string[] SnapNames = { "1 ms", "10 ms", "50 ms", "100 ms", "500 ms", "1000 ms" };
        private const string SnapPref = "AX.UdonSlot.EffectLight.Snap";

        private static int TimeSnap
        {
            get { return Mathf.Max(1, EditorPrefs.GetInt(SnapPref, 50)); }
            set { EditorPrefs.SetInt(SnapPref, value); }
        }

        private static float Snap(float t)
        {
            return Mathf.Max(0, Mathf.RoundToInt(t / TimeSnap) * TimeSnap);
        }

        [SerializeField] private string actionName;
        /// <summary>キーはまだ無いが行を出しているトラック。</summary>
        [SerializeField] private List<int> extraTracks = new List<int>();
        /// <summary>プレビューで入賞図柄として光らせるライン（配当表の有効ラインの番号）。</summary>
        [SerializeField] private int previewLine = -1;

        private EffectEditorWindow owner;
        private EffectAction action;
        private float time;
        private bool playing;
        private double lastTick;

        private enum DragMode { None, Scrub, Move, Box, Pan }

        private readonly List<EffectLightKey> selection = new List<EffectLightKey>();
        private readonly List<Vector2Int> selectionIds = new List<Vector2Int>();
        private DragMode mode;
        private bool dragMoved;
        private Vector2 dragStart, boxStart, boxEnd;
        private List<int> dragTimes = new List<int>();

        private bool viewSet;
        private float viewStart, viewEnd, panStart, panEnd;
        private Vector2 trackScroll;
        private static GUIStyle tickLabel, cellLabel;

        public static void Open(EffectEditorWindow owner, EffectAction action)
        {
            var window = GetWindow<EffectLightWindow>();
            window.titleContent = new GUIContent("リール照明");
            window.minSize = new Vector2(900, 560);
            window.owner = owner;
            window.action = action;
            window.actionName = action.Name;
            window.extraTracks.Clear();
            window.selection.Clear();
            window.selectionIds.Clear();
            window.viewSet = false;
            window.time = 0f;
            window.playing = false;
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= Repaint;
        }

        private void Tick()
        {
            if (!playing || action == null) return;
            double now = EditorApplication.timeSinceStartup;
            time += (float)((now - lastTick) * 1000.0);
            lastTick = now;
            int length = action.LightEnd;
            // ループは長さの終わりで再生ヘッドを最初に戻す。
            if (action.LightLoop && length > 0) time = Mathf.Repeat(time, length);
            else if (time >= length + MaxDelay())
            {
                time = length + MaxDelay();
                playing = false;
            }
            Repaint();
        }

        // ------------------------------------------------------------ 参照

        private EffectModel Model { get { return owner != null ? owner.Model : null; } }

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
            if (action.LightDelay == null || action.LightDelay.Length != 3) action.LightDelay = new int[3];
            if (selection.Exists(k => !action.LightKeys.Contains(k)))
            {
                selection.Clear();
                foreach (var id in selectionIds)
                {
                    var key = action.LightKeys.Find(k => k.Track == id.x && k.Time == id.y);
                    if (key != null && !selection.Contains(key)) selection.Add(key);
                }
            }
            return true;
        }

        private PayoutTable Table
        {
            get
            {
                var model = Model;
                var mainBoard = model != null && model.Player != null ? model.Player.mainBoard : null;
                if (mainBoard == null || mainBoard.reelOutputController == null) return null;
                var p = new SerializedObject(mainBoard.reelOutputController).FindProperty("payoutTable");
                return p != null ? p.objectReferenceValue as PayoutTable : null;
            }
        }

        // ------------------------------------------------------------ トラック

        private static bool IsColorTrack(int track) { return track >= EffectPlayer.LightTrackColor; }

        private static string TrackName(int track)
        {
            if (track == EffectPlayer.LightTrackWin) return "入賞図柄";
            if (track == EffectPlayer.LightTrackNotWin) return "入賞図柄以外";
            if (IsColorTrack(track))
            {
                int k = track - EffectPlayer.LightTrackColor;
                return EffectModel.ReelNames[k / 2] + "リール " + (k % 2 == 0 ? "上照明" : "下照明");
            }
            return EffectModel.ReelNames[track / 3] + RowNames[track % 3];
        }

        /// <summary>表示する行。入賞図柄・入賞図柄以外、9コマ、照明の色の順。</summary>
        private List<int> Tracks()
        {
            var used = new HashSet<int>(extraTracks);
            foreach (var k in action.LightKeys) used.Add(k.Track);
            var list = new List<int>();
            int[] order = { EffectPlayer.LightTrackWin, EffectPlayer.LightTrackNotWin, 0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16 };
            foreach (int t in order) if (used.Contains(t)) list.Add(t);
            return list;
        }

        // ------------------------------------------------------------ 値の計算（実行時と同じ）

        /// <summary>トラックの時刻 t の値。キーが無ければ false。</summary>
        private bool TrackValue(int track, float t, out Color value)
        {
            EffectLightKey before = null, after = null;
            foreach (var k in action.LightKeys)
            {
                if (k.Track != track) continue;
                if (k.Time <= t) { if (before == null || k.Time >= before.Time) before = k; }
                else if (after == null || k.Time < after.Time) after = k;
            }
            value = Color.black;
            if (before == null && after == null) return false;
            if (before == null) value = after.Value;
            else if (after == null || before.Hold) value = before.Value;
            else value = Color.Lerp(before.Value, after.Value, (t - before.Time) / Mathf.Max(1f, after.Time - before.Time));
            return true;
        }

        private int MaxDelay()
        {
            return Mathf.Max(action.LightDelay[0], Mathf.Max(action.LightDelay[1], action.LightDelay[2]));
        }

        private float LocalTime(float elapsed)
        {
            int length = action.LightEnd;
            if (action.LightLoop && length > 0) return Mathf.Repeat(elapsed, length);
            return Mathf.Min(elapsed, length);
        }

        /// <summary>プレビューのコマの明るさ。トラックが無いコマは null。</summary>
        private float? CellBrightness(int cell, int winMask)
        {
            Color v;
            if (TrackValue(cell, LocalTime(time), out v)) return v.r;
            int track = ((winMask >> cell) & 1) != 0 ? EffectPlayer.LightTrackWin : EffectPlayer.LightTrackNotWin;
            if (TrackValue(track, LocalTime(time - action.LightDelay[cell / 3]), out v)) return v.r;
            return null;
        }

        private int PreviewMask()
        {
            var table = Table;
            if (table == null || previewLine < 0 || previewLine >= table.GetPayLineCount()) return 0;
            int mask = 0;
            for (int reel = 0; reel < 3; reel++)
            {
                int row = table.GetPayLineRow(previewLine, reel);
                if (row >= 0 && row < 3) mask |= 1 << (reel * 3 + row);
            }
            return mask;
        }

        private static string LineName(PayoutTable table, int line)
        {
            int l = table.GetPayLineRow(line, 0), c = table.GetPayLineRow(line, 1), r = table.GetPayLineRow(line, 2);
            if (l == c && c == r && l >= 0 && l < 3) return RowNames[l] + "段";
            if (l == 0 && c == 1 && r == 2) return "右下がり";
            if (l == 2 && c == 1 && r == 0) return "右上がり";
            return "ライン" + (line + 1);
        }

        // ------------------------------------------------------------ 表示範囲

        private float DefaultEnd { get { return Mathf.Max(2000f, Mathf.Ceil((action.LightEnd + MaxDelay() + 500) / 1000f) * 1000f); } }
        private float ViewStart { get { return viewSet ? viewStart : 0f; } }
        private float ViewEnd { get { return viewSet ? viewEnd : DefaultEnd; } }

        private float X(Rect rect, float t) { return (t - ViewStart) / (ViewEnd - ViewStart) * rect.width; }

        private float TimeAt(Rect rect, float windowX)
        {
            return Mathf.Max(0f, ViewStart + (windowX - rect.x) / rect.width * (ViewEnd - ViewStart));
        }

        private void SetView(float start, float end)
        {
            float span = Mathf.Clamp(end - start, 50f, 600000f);
            if (start < 0f) start = 0f;
            viewStart = start;
            viewEnd = start + span;
            viewSet = true;
        }

        private void Zoom(Rect rect, float windowX, float delta)
        {
            float t = ViewStart + (windowX - rect.x) / rect.width * (ViewEnd - ViewStart);
            float f = delta > 0 ? 1.15f : 1f / 1.15f;
            SetView(t - (t - ViewStart) * f, t + (ViewEnd - t) * f);
        }

        private void Frame(bool selectionOnly)
        {
            var keys = selectionOnly && selection.Count > 0 ? selection : action.LightKeys;
            if (keys.Count == 0) { viewSet = false; return; }
            float min = float.MaxValue, max = 0f;
            foreach (var k in keys) { min = Mathf.Min(min, k.Time); max = Mathf.Max(max, k.Time); }
            if (!selectionOnly || selection.Count == 0) { min = 0f; max = Mathf.Max(max, action.LightEnd); }
            float span = Mathf.Max(200f, max - min);
            SetView(min - span * 0.05f, max + span * 0.05f);
        }

        private static float TickStep(float span, float width)
        {
            float[] steps = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 60000 };
            foreach (float s in steps) if (s / span * width >= 60f) return s;
            return 60000f;
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
            HandleKeyboard();
            DrawToolbar();
            DrawTop();
            DrawEditArea();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(action.Name, EditorStyles.boldLabel);
                GUILayout.Space(16);
                GUILayout.Label("長さ", GUILayout.Width(30));
                int length;
                if (EffectFields.EditInt(action.LightEnd, out length, GUILayout.Width(70)))
                    Edit("長さの変更", () => action.LightLength = Mathf.Max(0, length));
                GUILayout.Label("ms", GUILayout.Width(24));
                GUILayout.Space(8);
                int loop = GUILayout.Toolbar(action.LightLoop ? 1 : 0, LoopNames, EditorStyles.toolbarButton, GUILayout.Width(110));
                if (loop != (action.LightLoop ? 1 : 0))
                {
                    Edit("ループの変更", () => action.LightLoop = loop == 1);
                    time = 0f;
                }
                GUILayout.Space(8);
                GUILayout.Label("終了時", GUILayout.Width(40));
                int end = GUILayout.Toolbar(action.LightRestore ? 0 : 1, EndNames, EditorStyles.toolbarButton, GUILayout.Width(120));
                if (end != (action.LightRestore ? 0 : 1)) Edit("終了時の変更", () => action.LightRestore = end == 0);
                GUILayout.FlexibleSpace();
                GUILayout.Label("スナップ", GUILayout.Width(50));
                int snapIndex = System.Array.IndexOf(SnapSteps, TimeSnap);
                int pickedSnap = EditorGUILayout.Popup(snapIndex, SnapNames, EditorStyles.toolbarPopup, GUILayout.Width(80));
                if (pickedSnap != snapIndex && pickedSnap >= 0) TimeSnap = SnapSteps[pickedSnap];
            }
        }

        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            switch (e.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (selection.Count == 0) return;
                    DeleteSelected();
                    break;
                case KeyCode.Space:
                    TogglePlay();
                    break;
                case KeyCode.F:
                    Frame(true);
                    break;
                case KeyCode.A:
                    Frame(false);
                    break;
                case KeyCode.K:
                    AddKeysAtPlayhead();
                    break;
                default:
                    return;
            }
            e.Use();
            Repaint();
        }

        private void TogglePlay()
        {
            if (playing) { playing = false; return; }
            if (action.LightKeys.Count == 0) return;
            if (!action.LightLoop && time >= action.LightEnd + MaxDelay()) time = 0f;
            playing = true;
            lastTick = EditorApplication.timeSinceStartup;
        }

        /// <summary>上段：プレビュー（表示窓と上下照明）、入賞ラインと遅れ、選んだキーの欄、再生ボタン。</summary>
        private void DrawTop()
        {
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(CellW * 3 + 16)))
                {
                    GUILayout.Label("プレビュー", EditorStyles.miniBoldLabel);
                    var face = GUILayoutUtility.GetRect(CellW * 3 + 12, CellH * 3 + BandH * 2, GUILayout.Width(CellW * 3 + 12));
                    DrawPreview(face);
                }
                GUILayout.Space(16);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
                {
                    GUILayout.Space(16);
                    var table = Table;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("入賞ライン", GUILayout.Width(64));
                        var names = new List<string> { "なし" };
                        int lines = table != null ? table.GetPayLineCount() : 0;
                        for (int i = 0; i < lines; i++) names.Add(LineName(table, i));
                        previewLine = EditorGUILayout.Popup(Mathf.Clamp(previewLine + 1, 0, lines), names.ToArray(), GUILayout.Width(120)) - 1;
                    }
                    GUILayout.Space(4);
                    GUILayout.Label("入賞図柄・入賞図柄以外の遅れ (ms)", EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int r = 0; r < 3; r++)
                        {
                            GUILayout.Label(EffectModel.ReelNames[r], GUILayout.Width(16));
                            int reel = r;
                            int delay;
                            if (EffectFields.EditInt(action.LightDelay[r], out delay, GUILayout.Width(44)))
                                Edit("遅れの変更", () => action.LightDelay[reel] = Mathf.Max(0, delay));
                        }
                    }
                }
                GUILayout.Space(16);
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(320)))
                {
                    GUILayout.Space(16);
                    DrawInspector();
                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("|◀", GUILayout.Width(32))) { time = 0f; playing = false; }
                        using (new EditorGUI.DisabledScope(action.LightKeys.Count == 0))
                        {
                            if (GUILayout.Button(playing ? "❚❚" : "▶", GUILayout.Width(32))) TogglePlay();
                        }
                        GUILayout.Label(Mathf.RoundToInt(time) + " / " + action.LightEnd + " ms");
                        GUILayout.FlexibleSpace();
                    }
                }
            }
            GUILayout.Space(8);
        }

        private void DrawPreview(Rect rect)
        {
            int mask = PreviewMask();
            for (int reel = 0; reel < 3; reel++)
            {
                float x = rect.x + reel * (CellW + 6);
                Color upper, lower;
                bool hasUpper = TrackValue(EffectPlayer.LightTrackColor + reel * 2, LocalTime(time), out upper);
                bool hasLower = TrackValue(EffectPlayer.LightTrackColor + reel * 2 + 1, LocalTime(time), out lower);
                EditorGUI.DrawRect(new Rect(x, rect.y, CellW, BandH), hasUpper ? Display(upper) : HeaderColor);
                EditorGUI.DrawRect(new Rect(x, rect.y + BandH + CellH * 3, CellW, BandH), hasLower ? Display(lower) : HeaderColor);
                for (int row = 0; row < 3; row++)
                {
                    int cell = reel * 3 + row;
                    var r = new Rect(x, rect.y + BandH + row * CellH, CellW, CellH - 1);
                    float? b = CellBrightness(cell, mask);
                    float v = b.HasValue ? b.Value : 1f;
                    EditorGUI.DrawRect(r, new Color(0.25f + 0.65f * v, 0.25f + 0.63f * v, 0.25f + 0.58f * v));
                    if (((mask >> cell) & 1) != 0)
                    {
                        Handles.color = WinOutlineColor;
                        Handles.DrawSolidRectangleWithOutline(r, Color.clear, WinOutlineColor);
                    }
                    if (b.HasValue) GUI.Label(r, Mathf.RoundToInt(v * 100f) + "%", CellLabel);
                }
            }
        }

        /// <summary>HDR の色を画面に出せる明るさに収める。</summary>
        private static Color Display(Color c)
        {
            float max = Mathf.Max(1f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            return new Color(c.r / max, c.g / max, c.b / max, 1f);
        }

        private void DrawEditArea()
        {
            var tracks = Tracks();
            float height = RulerH + Mathf.Max(1, tracks.Count) * TrackH + 28;
            var area = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            area.xMin += 8;
            area.xMax -= 8;

            var names = new Rect(area.x, area.y + RulerH, NameW, tracks.Count * TrackH);
            var ruler = new Rect(area.x + NameW + 4, area.y, area.width - NameW - 4, RulerH);
            var graph = new Rect(ruler.x, ruler.yMax, ruler.width, Mathf.Max(TrackH, tracks.Count * TrackH));

            DrawTrackNames(names, tracks);
            if (GUI.Button(new Rect(area.x, names.yMax + 4, NameW, 20), "＋ トラック")) ShowAddTrackMenu(tracks);
            DrawRuler(ruler);
            DrawGraph(graph, tracks);
        }

        private void DrawTrackNames(Rect rect, List<int> tracks)
        {
            var e = Event.current;
            for (int i = 0; i < tracks.Count; i++)
            {
                int track = tracks[i];
                var r = new Rect(rect.x, rect.y + i * TrackH, rect.width, TrackH - 1);
                bool selected = selection.Exists(k => k.Track == track);
                EditorGUI.DrawRect(r, selected ? RowSelectedColor : RowColor);
                GUI.Label(new Rect(r.x + 6, r.y, r.width - 6, r.height), TrackName(track));
                if (e.type == EventType.ContextClick && r.Contains(e.mousePosition))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("トラックを削除"), false, () =>
                    {
                        extraTracks.Remove(track);
                        Edit("トラックの削除", () => action.LightKeys.RemoveAll(k => k.Track == track));
                    });
                    menu.ShowAsContext();
                    e.Use();
                }
            }
        }

        private void ShowAddTrackMenu(List<int> shown)
        {
            var menu = new GenericMenu();
            int[] order = { EffectPlayer.LightTrackWin, EffectPlayer.LightTrackNotWin, 0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16 };
            foreach (int t in order)
            {
                int track = t;
                string path = track <= 8 ? "コマ/" + TrackName(track) : IsColorTrack(track) ? "照明の色/" + TrackName(track) : TrackName(track);
                if (shown.Contains(track)) menu.AddDisabledItem(new GUIContent(path));
                else menu.AddItem(new GUIContent(path), false, () => { extraTracks.Add(track); Repaint(); });
            }
            menu.ShowAsContext();
        }

        private void DrawRuler(Rect rect)
        {
            var e = Event.current;
            float vs = ViewStart, ve = ViewEnd;
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            GUI.BeginClip(rect);
            float step = TickStep(ve - vs, rect.width);
            for (float t = Mathf.Ceil(vs / step) * step; t <= ve; t += step)
            {
                float x = X(rect, t);
                EditorGUI.DrawRect(new Rect(x, 6, 1, rect.height - 6), TickColor);
                GUI.Label(new Rect(x + 3, 2, 60, rect.height - 2), Mathf.RoundToInt(t).ToString(), TickLabel);
            }
            // 1回の長さの終わり
            float endX = X(rect, action.LightEnd);
            EditorGUI.DrawRect(new Rect(endX, 0, 1, rect.height), new Color(0.6f, 0.6f, 0.6f));
            EditorGUI.DrawRect(new Rect(X(rect, time) - 1, 0, 2, rect.height), PlayheadColor);
            GUI.EndClip();

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.SlideArrow);
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                mode = DragMode.Scrub;
                playing = false;
                time = Snap(TimeAt(rect, e.mousePosition.x));
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                Zoom(rect, e.mousePosition.x, e.delta.y);
                e.Use();
                Repaint();
            }
        }

        private void DrawGraph(Rect rect, List<int> tracks)
        {
            var e = Event.current;
            EditorGUI.DrawRect(rect, GraphColor);
            GUI.BeginClip(rect);
            for (int i = 1; i <= tracks.Count; i++) EditorGUI.DrawRect(new Rect(0, i * TrackH - 1, rect.width, 1), RowLineColor);
            float snap = TimeSnap;
            if (snap / (ViewEnd - ViewStart) * rect.width >= 8f)
            {
                for (float t = Mathf.Ceil(ViewStart / snap) * snap; t <= ViewEnd; t += snap)
                    EditorGUI.DrawRect(new Rect(X(rect, t), 0, 1, rect.height), SnapLineColor);
            }
            float tick = TickStep(ViewEnd - ViewStart, rect.width);
            for (float t = Mathf.Ceil(ViewStart / tick) * tick; t <= ViewEnd; t += tick)
                EditorGUI.DrawRect(new Rect(X(rect, t), 0, 1, rect.height), RowLineColor);

            if (e.type == EventType.Repaint)
            {
                for (int i = 0; i < tracks.Count; i++) DrawTrackValues(rect, tracks[i], i);
                foreach (var key in action.LightKeys)
                {
                    int row = tracks.IndexOf(key.Track);
                    if (row < 0) continue;
                    DrawKey(KeyPosition(rect, key, row), key);
                }
                if (mode == DragMode.Box)
                {
                    var box = BoxRect();
                    box.position -= rect.position;
                    EditorGUI.DrawRect(box, new Color(0.5f, 0.7f, 0.9f, 0.15f));
                    Handles.DrawSolidRectangleWithOutline(box, Color.clear, new Color(0.5f, 0.7f, 0.9f, 0.8f));
                }
            }
            EditorGUI.DrawRect(new Rect(X(rect, action.LightEnd), 0, 1, rect.height), new Color(0.6f, 0.6f, 0.6f, 0.6f));
            EditorGUI.DrawRect(new Rect(X(rect, time) - 1, 0, 2, rect.height), PlayheadColor);
            GUI.EndClip();

            HandleGraphMouse(rect, tracks);
        }

        /// <summary>明るさは行の中の折れ線、色は色の帯で値の変化を見せる。</summary>
        private void DrawTrackValues(Rect rect, int track, int row)
        {
            float top = row * TrackH + 3, h = TrackH - 7;
            float x0 = Mathf.Max(0f, X(rect, 0)), x1 = rect.width;
            const float stepPx = 4f;
            if (IsColorTrack(track))
            {
                for (float x = x0; x < x1; x += stepPx)
                {
                    Color c;
                    if (!TrackValue(track, ViewStart + x / rect.width * (ViewEnd - ViewStart), out c)) return;
                    EditorGUI.DrawRect(new Rect(x, top + h * 0.3f, stepPx, h * 0.4f), Display(c));
                }
                return;
            }
            Handles.color = CurveColor;
            Vector3 previous = Vector3.zero;
            bool first = true;
            for (float x = x0; x <= x1; x += stepPx)
            {
                Color c;
                if (!TrackValue(track, ViewStart + x / rect.width * (ViewEnd - ViewStart), out c)) return;
                var point = new Vector3(x, top + h * (1f - Mathf.Clamp01(c.r)));
                if (!first) Handles.DrawAAPolyLine(2f, previous, point);
                previous = point;
                first = false;
            }
        }

        private Vector2 KeyPosition(Rect rect, EffectLightKey key, int row)
        {
            return new Vector2(X(rect, key.Time), row * TrackH + TrackH / 2f);
        }

        /// <summary>◇ はリニア、□ は切り替え。</summary>
        private void DrawKey(Vector2 c, EffectLightKey key)
        {
            bool on = selection.Contains(key);
            float size = on ? KeySize + 2 : KeySize;
            Shape(c, size + 2, key.Hold, on ? Color.white : new Color(0.1f, 0.1f, 0.1f));
            Shape(c, size - 1, key.Hold, on ? new Color(0.5f, 0.7f, 0.9f) : KeyColor);
        }

        private static void Shape(Vector2 c, float size, bool square, Color color)
        {
            float h = size / 2f;
            if (square)
            {
                EditorGUI.DrawRect(new Rect(c.x - h, c.y - h, size, size), color);
                return;
            }
            h *= 1.3f;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(new Vector3(c.x, c.y - h), new Vector3(c.x + h, c.y), new Vector3(c.x, c.y + h), new Vector3(c.x - h, c.y));
        }

        private EffectLightKey HitKey(Vector2 mouse, Rect rect, List<int> tracks)
        {
            EffectLightKey hit = null;
            float best = KeySize;
            foreach (var key in action.LightKeys)
            {
                int row = tracks.IndexOf(key.Track);
                if (row < 0) continue;
                float d = Vector2.Distance(KeyPosition(rect, key, row) + rect.position, mouse);
                if (d <= best) { best = d; hit = key; }
            }
            return hit;
        }

        private Rect BoxRect()
        {
            return Rect.MinMaxRect(Mathf.Min(boxStart.x, boxEnd.x), Mathf.Min(boxStart.y, boxEnd.y),
                Mathf.Max(boxStart.x, boxEnd.x), Mathf.Max(boxStart.y, boxEnd.y));
        }

        /// <summary>
        /// Animation ウィンドウと同じ操作：
        /// キーをクリックで選択（Shift／Ctrl で追加・解除）、ドラッグで選んだキーをまとめて横へ移動、
        /// 空いた所をドラッグで範囲選択、ダブルクリックでその行にキーを追加、
        /// ホイールで拡大縮小、中ボタン（または Alt＋左）ドラッグで横へ移動。
        /// </summary>
        private void HandleGraphMouse(Rect rect, List<int> tracks)
        {
            var e = Event.current;
            bool additive = e.shift || e.control || e.command;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (!rect.Contains(e.mousePosition)) break;
                    Zoom(rect, e.mousePosition.x, e.delta.y);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDown:
                    if (!rect.Contains(e.mousePosition)) break;
                    if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        mode = DragMode.Pan;
                        dragStart = e.mousePosition;
                        panStart = ViewStart;
                        panEnd = ViewEnd;
                        e.Use();
                        break;
                    }
                    if (e.button != 0) break;
                    {
                        var hit = HitKey(e.mousePosition, rect, tracks);
                        if (hit != null)
                        {
                            if (additive && selection.Contains(hit))
                            {
                                selection.Remove(hit);
                                RememberSelection();
                                e.Use();
                                break;
                            }
                            if (additive) selection.Add(hit);
                            else if (!selection.Contains(hit)) SetSelection(hit);
                            RememberSelection();
                            GUI.FocusControl(null);
                            mode = DragMode.Move;
                            dragMoved = false;
                            dragStart = e.mousePosition;
                            dragTimes = selection.ConvertAll(k => k.Time);
                        }
                        else if (e.clickCount == 2)
                        {
                            int row = Mathf.FloorToInt((e.mousePosition.y - rect.y) / TrackH);
                            if (row >= 0 && row < tracks.Count) AddKey(tracks[row], (int)Snap(TimeAt(rect, e.mousePosition.x)));
                        }
                        else
                        {
                            if (!additive) SetSelection(null);
                            mode = DragMode.Box;
                            boxStart = boxEnd = e.mousePosition;
                        }
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    switch (mode)
                    {
                        case DragMode.Scrub:
                            time = Snap(TimeAt(rect, e.mousePosition.x));
                            break;
                        case DragMode.Pan:
                        {
                            float dt = (e.mousePosition.x - dragStart.x) / rect.width * (panEnd - panStart);
                            float s0 = Mathf.Max(0f, panStart - dt);
                            SetView(s0, s0 + (panEnd - panStart));
                            break;
                        }
                        case DragMode.Box:
                            boxEnd = e.mousePosition;
                            break;
                        case DragMode.Move:
                        {
                            float dt = (e.mousePosition.x - dragStart.x) / rect.width * (ViewEnd - ViewStart);
                            for (int i = 0; i < selection.Count; i++)
                            {
                                int newTime = (int)Snap(dragTimes[i] + dt);
                                if (newTime != selection[i].Time) dragMoved = true;
                                selection[i].Time = newTime;
                            }
                            RememberSelection();
                            break;
                        }
                        default:
                            return;
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    if (mode == DragMode.None) break;
                    if (mode == DragMode.Move && dragMoved) Edit("照明キーの移動", delegate { });
                    if (mode == DragMode.Box)
                    {
                        var box = BoxRect();
                        foreach (var key in action.LightKeys)
                        {
                            int row = tracks.IndexOf(key.Track);
                            if (row < 0 || selection.Contains(key)) continue;
                            if (box.Contains(KeyPosition(rect, key, row) + rect.position)) selection.Add(key);
                        }
                        RememberSelection();
                    }
                    mode = DragMode.None;
                    e.Use();
                    Repaint();
                    break;

                case EventType.ContextClick:
                    if (!rect.Contains(e.mousePosition)) break;
                    {
                        var hit = HitKey(e.mousePosition, rect, tracks);
                        var menu = new GenericMenu();
                        if (hit != null)
                        {
                            if (!selection.Contains(hit)) SetSelection(hit);
                            menu.AddItem(new GUIContent("削除"), false, DeleteSelected);
                        }
                        else
                        {
                            int row = Mathf.FloorToInt((e.mousePosition.y - rect.y) / TrackH);
                            int at = (int)Snap(TimeAt(rect, e.mousePosition.x));
                            if (row >= 0 && row < tracks.Count)
                            {
                                int track = tracks[row];
                                menu.AddItem(new GUIContent("キーを追加"), false, () => AddKey(track, at));
                            }
                        }
                        menu.ShowAsContext();
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>選んだキーの欄。</summary>
        private void DrawInspector()
        {
            const float labelW = 72f;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("キー＋", GUILayout.Width(80))) AddKeysAtPlayhead();
                GUILayout.FlexibleSpace();
            }
            if (selection.Count == 0) return;
            var first = selection[0];
            bool sameTrack = selection.TrueForAll(k => k.Track == first.Track);
            bool allColor = selection.TrueForAll(k => IsColorTrack(k.Track));
            bool allBrightness = selection.TrueForAll(k => !IsColorTrack(k.Track));

            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("トラック", GUILayout.Width(labelW));
                GUILayout.Label(sameTrack ? TrackName(first.Track) : "—", EditorStyles.boldLabel, GUILayout.Width(140));
                if (selection.Count > 1) GUILayout.Label(selection.Count + " キー");
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("時間", GUILayout.Width(labelW));
                EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Time == first.Time);
                int t;
                if (EffectFields.EditInt(first.Time, out t, GUILayout.Width(90)))
                    Edit("照明キーの変更", () => { foreach (var k in selection) k.Time = Mathf.Max(0, t); });
                EditorGUI.showMixedValue = false;
                GUILayout.Label("ms", GUILayout.Width(30));
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(allColor ? "色" : "明るさ", GUILayout.Width(labelW));
                EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Value == first.Value);
                if (allBrightness)
                {
                    EditorGUI.BeginChangeCheck();
                    float v = EditorGUILayout.Slider(first.Value.r, 0f, 1f, GUILayout.Width(200));
                    if (EditorGUI.EndChangeCheck()) Edit("照明キーの変更", () => { foreach (var k in selection) k.Value = new Color(v, v, v, 1f); });
                }
                else if (allColor)
                {
                    EditorGUI.BeginChangeCheck();
                    var c = EditorGUILayout.ColorField(GUIContent.none, first.Value, true, false, true, GUILayout.Width(200));
                    if (EditorGUI.EndChangeCheck()) Edit("照明キーの変更", () => { foreach (var k in selection) k.Value = c; });
                }
                else GUILayout.Label("—");
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("次のキーまで", GUILayout.Width(labelW));
                bool same = selection.TrueForAll(k => k.Hold == first.Hold);
                int current = same ? (first.Hold ? 1 : 0) : -1;
                int picked = GUILayout.Toolbar(current, HoldNames, GUILayout.Width(160));
                if (picked != current && picked >= 0) Edit("照明キーの変更", () => { foreach (var k in selection) k.Hold = picked == 1; });
                GUILayout.FlexibleSpace();
            }
        }

        // ------------------------------------------------------------ 編集

        private void Edit(string label, System.Action change)
        {
            if (owner == null) return;
            owner.Edit(label, change);
            RememberSelection();
            Repaint();
        }

        private void SetSelection(EffectLightKey key)
        {
            selection.Clear();
            if (key != null) selection.Add(key);
            RememberSelection();
            GUI.FocusControl(null);
        }

        private void RememberSelection()
        {
            selectionIds.Clear();
            foreach (var k in selection) selectionIds.Add(new Vector2Int(k.Track, k.Time));
        }

        /// <summary>その時間の今の値でキーを置く（見た目は変わらない）。キーが無いトラックは明るさ 100%・白。</summary>
        private void AddKey(int track, int at)
        {
            if (action.LightKeys.Exists(k => k.Track == track && k.Time == at)) return;
            Color value;
            if (!TrackValue(track, at, out value)) value = Color.white;
            var key = new EffectLightKey { Track = track, Time = at, Value = value };
            Edit("照明キーの追加", () => action.LightKeys.Add(key));
            extraTracks.Remove(track);
            SetSelection(key);
        }

        private void AddKeysAtPlayhead()
        {
            int at = (int)Snap(time);
            var added = new List<EffectLightKey>();
            foreach (int track in Tracks())
            {
                if (action.LightKeys.Exists(k => k.Track == track && k.Time == at)) continue;
                Color value;
                if (!TrackValue(track, at, out value)) value = Color.white;
                added.Add(new EffectLightKey { Track = track, Time = at, Value = value });
            }
            if (added.Count == 0) return;
            Edit("照明キーの追加", () => action.LightKeys.AddRange(added));
            foreach (var k in added) extraTracks.Remove(k.Track);
            selection.Clear();
            selection.AddRange(added);
            RememberSelection();
        }

        private void DeleteSelected()
        {
            if (selection.Count == 0) return;
            var removed = new List<EffectLightKey>(selection);
            // キーを全部消したトラックも行は残す
            foreach (var k in removed) if (!extraTracks.Contains(k.Track)) extraTracks.Add(k.Track);
            Edit("照明キーの削除", () => action.LightKeys.RemoveAll(removed.Contains));
            SetSelection(null);
        }

        // ------------------------------------------------------------ 見た目

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

        private static GUIStyle CellLabel
        {
            get
            {
                if (cellLabel == null)
                {
                    cellLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                    cellLabel.normal.textColor = new Color(0.1f, 0.1f, 0.1f);
                }
                return cellLabel;
            }
        }
    }
}
