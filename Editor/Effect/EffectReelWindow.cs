using System.Collections.Generic;
using System.Linq;
using AX.UdonSlot.REV1_0.Serialize;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>
    /// アクションのリールをキーフレームで編集する窓。
    /// 縦軸は左に並べたリール配列（上が21、下が1）、横軸は時間。値は中段に来るコマ。
    /// 相対キー（◇）は開始位置から上方向へのコマ数、絶対キー（□）は中段に来るコマ番号。
    /// 計算は実行時（EffectPlayer.ValueAt）と同じ：キーの間はキーのイージングに従う、最初のキーより前はそのキーの値。
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
        private static readonly Color SnapLineColor = new Color(0.19f, 0.19f, 0.19f);
        private static readonly Color TickColor = new Color(0.33f, 0.33f, 0.33f);
        private static readonly Color StripColor = new Color(0.2f, 0.2f, 0.2f);
        private static readonly Color StripEditableColor = new Color(0.26f, 0.26f, 0.26f);
        private static readonly Color ReelFaceColor = new Color(0.9f, 0.88f, 0.83f);
        private static readonly Color ReelLineColor = new Color(0.78f, 0.75f, 0.68f);
        private static readonly Color PlayheadColor = new Color(0.88f, 0.35f, 0.35f);
        private static readonly string[] ModeNames = { "相対", "絶対" };
        private static readonly string[] DirectionNames = { "最短", "順回転", "逆回転" };
        private static readonly string[] EaseNames = { "直線", "加速", "減速", "加減速" };

        private const float RowH = 18f;
        private const float StripW = 64f;
        private const float StripGap = 4f;
        private const float RulerH = 20f;
        private const float CellH = 40f;
        private const float FaceW = 90f;
        private const float KeySize = 10f;
        private const float DimAlpha = 0.25f;
        private static readonly int[] SnapSteps = { 1, 10, 50, 100, 500, 1000 };
        private static readonly string[] SnapNames = { "1 ms", "10 ms", "50 ms", "100 ms", "500 ms", "1000 ms" };
        private const string SnapPref = "AX.UdonSlot.EffectReel.Snap";

        /// <summary>時間のスナップ（グリッド）間隔。個人設定として EditorPrefs に残す。</summary>
        private static int TimeSnap
        {
            get { return Mathf.Max(1, EditorPrefs.GetInt(SnapPref, 10)); }
            set { EditorPrefs.SetInt(SnapPref, value); }
        }

        private static float Snap(float t)
        {
            return Mathf.Max(0, Mathf.RoundToInt(t / TimeSnap) * TimeSnap);
        }

        [SerializeField] private string actionName;
        [SerializeField] private bool[] shown = { true, true, true };
        [SerializeField] private int[] startFrame = { 1, 1, 1 };

        private EffectEditorWindow owner;
        private EffectAction action;
        private float time;
        private bool playing;
        private double lastTick;

        private enum DragMode { None, Scrub, Move, Box, Pan }

        private readonly List<EffectReelKey> selection = new List<EffectReelKey>();
        private readonly List<Vector2Int> selectionIds = new List<Vector2Int>();

        private DragMode mode;
        /// <summary>ドラッグ中にマウスをつかんでおくための ID。ウィンドウの外に出ても操作を続けられる。</summary>
        private int dragId;
        private bool dragMoved;
        private Vector2 dragStart, boxStart, boxEnd;
        private List<int> dragTimes = new List<int>();
        private List<float> dragValues = new List<float>();
        private int faceDragReel = -1;
        private float faceDragY;

        /// <summary>横の表示範囲（ms）。拡大・移動するまでは全体を自動で表示する。</summary>
        private bool viewSet;
        private float viewStart, viewEnd, panStart, panEnd;

        private Dictionary<string, Texture2D> pictures;
        private static GUIStyle stripLabel, faceLabel, tickLabel;

        public static void Open(EffectEditorWindow owner, EffectAction action)
        {
            var window = GetWindow<EffectReelWindow>();
            window.titleContent = new GUIContent("リール");
            window.minSize = new Vector2(1000, 640);
            window.owner = owner;
            window.action = action;
            window.actionName = action.Name;
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
            if (selection.Exists(k => !action.ReelKeys.Contains(k)))
            {
                selection.Clear();
                foreach (var id in selectionIds)
                {
                    var key = action.ReelKeys.Find(k => k.Reel == id.x && k.Time == id.y);
                    if (key != null && !selection.Contains(key)) selection.Add(key);
                }
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

        private List<EffectReelKey> Ordered(int reel)
        {
            return action.ReelKeys.Where(x => x.Reel == reel).OrderBy(x => x.Time).ToList();
        }

        /// <summary>
        /// キーの位置を、前のキーの位置 previous から求める（実行時の EffectPlayer.ResolveReelKeys と同じ）。
        /// 相対キーは前のキーから順回転の向きに Value コマ（−は逆回転）、絶対キーは向きに従ってそのコマまで。
        /// 位置は「開始位置から上方向へのコマ数」（順回転すると減る）。
        /// </summary>
        private float ResolveKey(EffectReelKey k, float previous, int n)
        {
            return k.Absolute ? ResolveAbsolute(previous, startFrame[k.Reel], k.Value, k.Direction, k.Turns, n) : previous - k.Value;
        }

        /// <summary>キーの位置（開始位置から上方向へのコマ数）。</summary>
        private float Relative(EffectReelKey key, int n)
        {
            float previous = 0f;
            foreach (var k in Ordered(key.Reel))
            {
                float value = ResolveKey(k, previous, n);
                if (k == key) return value;
                previous = value;
            }
            return 0f;
        }

        /// <summary>key の一つ前のキーの位置（なければ開始位置 0）。</summary>
        private float Previous(EffectReelKey key, int n)
        {
            float previous = 0f;
            foreach (var k in Ordered(key.Reel))
            {
                if (k == key) return previous;
                previous = ResolveKey(k, previous, n);
            }
            return previous;
        }

        /// <summary>今の全キーの位置。キーの増減の後、相対キーを付け直して他のキーの位置を保つのに使う。</summary>
        private Dictionary<EffectReelKey, float> Positions(int n)
        {
            var map = new Dictionary<EffectReelKey, float>();
            foreach (var k in action.ReelKeys) map[k] = Relative(k, n);
            return map;
        }

        /// <summary>相対キーの値を、positions の位置になるように前のキーから付け直す。</summary>
        private void Rebase(Dictionary<EffectReelKey, float> positions, int n)
        {
            for (int r = 0; r < 3; r++)
            {
                float previous = 0f;
                foreach (var k in Ordered(r))
                {
                    float target;
                    if (!k.Absolute && positions.TryGetValue(k, out target)) k.Value = Mathf.Round((previous - target) * 100f) / 100f;
                    previous = ResolveKey(k, previous, n);
                }
            }
        }

        private static float ResolveAbsolute(float previous, int start, float frame, int direction, int turns, int n)
        {
            float delta = (start - frame) - previous;
            delta -= Mathf.Floor(delta / n) * n;
            if (direction == EffectPlayer.ReelForward) { if (delta > 0f) delta -= n; delta -= turns * n; }
            else if (direction == EffectPlayer.ReelBackward) delta += turns * n;
            else if (delta > n / 2f) delta -= n;
            return previous + delta;
        }

        private float RelativeAt(int reel, float t, int n)
        {
            // 0ms は開始位置（移動 0）。0ms にキーがあればそちらを使う。
            float beforeTime = 0f, afterTime = float.MaxValue;
            float beforeValue = 0f, afterValue = 0f;
            bool hasBefore = true, hasAfter = false;
            int afterEase = EffectPlayer.EaseLinear;
            foreach (var k in action.ReelKeys)
            {
                if (k.Reel != reel) continue;
                float value = Relative(k, n);
                if (k.Time <= t && k.Time >= beforeTime) { beforeTime = k.Time; beforeValue = value; hasBefore = true; }
                if (k.Time >= t && k.Time < afterTime) { afterTime = k.Time; afterValue = value; hasAfter = true; afterEase = k.Ease; }
            }
            if (!hasBefore && !hasAfter) return 0f;
            if (!hasBefore) return afterValue;
            if (!hasAfter || afterTime == beforeTime) return beforeValue;
            return beforeValue + (afterValue - beforeValue) * EaseValue(afterEase, (t - beforeTime) / (afterTime - beforeTime));
        }

        /// <summary>MainBoard.EaseValue と同じ曲線。</summary>
        private static float EaseValue(int ease, float p)
        {
            p = Mathf.Clamp01(p);
            if (ease == EffectPlayer.EaseIn) return p * p * p;
            if (ease == EffectPlayer.EaseOut) { float q = 1f - p; return 1f - q * q * q; }
            if (ease == EffectPlayer.EaseInOut)
            {
                if (p < 0.5f) return 4f * p * p * p;
                float q = 2f - 2f * p;
                return 1f - q * q * q / 2f;
            }
            return p;
        }

        /// <summary>中段に来るコマ（小数あり）。上へ1コマ動くと1つ下がる。</summary>
        private float CenterAt(int reel, float t, int n)
        {
            return startFrame[reel] - RelativeAt(reel, t, n);
        }


        // ------------------------------------------------------------ 表示範囲

        private float DefaultEnd
        {
            get { return Mathf.Max(2000f, Mathf.Ceil((action.ReelLength + 500) / 1000f) * 1000f); }
        }

        private float ViewStart { get { return viewSet ? viewStart : 0f; } }
        private float ViewEnd { get { return viewSet ? viewEnd : DefaultEnd; } }

        /// <summary>グラフ内の x（左端 0）。</summary>
        private float X(Rect rect, float t)
        {
            return (t - ViewStart) / (ViewEnd - ViewStart) * rect.width;
        }

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

        /// <summary>F：選んだキー（なければ全体）、A：全体を表示する。</summary>
        private void Frame(bool selectionOnly)
        {
            var keys = selectionOnly && selection.Count > 0 ? selection : action.ReelKeys;
            if (keys.Count == 0)
            {
                viewSet = false;
                return;
            }
            float min = float.MaxValue, max = 0f;
            foreach (var k in keys) { min = Mathf.Min(min, k.Time); max = Mathf.Max(max, k.Time); }
            if (!selectionOnly || selection.Count == 0) min = 0f;
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
            if (shown == null || shown.Length != 3) shown = new[] { true, true, true };
            if (startFrame == null || startFrame.Length != 3) startFrame = new[] { 1, 1, 1 };

            dragId = GUIUtility.GetControlID(FocusType.Passive);
            int n = SymbolCount;
            for (int r = 0; r < 3; r++) startFrame[r] = WrapFrame(startFrame[r], n);
            HandleKeyboard();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(action.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("スナップ", GUILayout.Width(50));
                int snapIndex = System.Array.IndexOf(SnapSteps, TimeSnap);
                int pickedSnap = EditorGUILayout.Popup(snapIndex, SnapNames, EditorStyles.toolbarPopup, GUILayout.Width(80));
                if (pickedSnap != snapIndex && pickedSnap >= 0) TimeSnap = SnapSteps[pickedSnap];
            }

            DrawPreview(n);
            DrawEditArea(n);
            CaptureMouse(mode != DragMode.None || faceDragReel >= 0);
        }

        /// <summary>ドラッグが始まったらマウスをつかみ、終わったら離す。</summary>
        private void CaptureMouse(bool dragging)
        {
            if (dragging && GUIUtility.hotControl == 0) GUIUtility.hotControl = dragId;
            else if (!dragging && GUIUtility.hotControl == dragId) GUIUtility.hotControl = 0;
        }

        private void HandleKeyboard()
        {
            var e = Event.current;
            if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) && !EditorGUIUtility.editingTextField)
            {
                bool run = e.type == EventType.ExecuteCommand;
                switch (e.commandName)
                {
                    case "Copy":
                        if (selection.Count == 0) return;
                        if (run) CopySelected();
                        break;
                    case "Cut":
                        if (selection.Count == 0) return;
                        if (run) { CopySelected(); DeleteSelected(); }
                        break;
                    case "Paste":
                        if (clipboard.Count == 0) return;
                        if (run) Paste(Mathf.RoundToInt(time / TimeSnap) * TimeSnap, -1);
                        break;
                    case "SelectAll":
                        if (run) { selection.Clear(); selection.AddRange(action.ReelKeys.Where(k => shown[k.Reel])); RememberSelection(); }
                        break;
                    default:
                        return;
                }
                e.Use();
                if (run) Repaint();
                return;
            }
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField || e.control || e.command) return;
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

        private void DrawPreview(int n)
        {
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);

                // 開始位置（再生前の停止出目）
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(FaceW * 3 + 12)))
                {
                    GUILayout.Label("開始時点の停止系", EditorStyles.miniBoldLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int r = 0; r < 3; r++)
                        {
                            using (new EditorGUILayout.VerticalScope(GUILayout.Width(FaceW)))
                            {
                                var face = GUILayoutUtility.GetRect(FaceW, CellH * 3, GUILayout.Width(FaceW));
                                DrawReelFace(face, r, startFrame[r], n);
                                int rows = FaceInput(face, r, false);
                                if (rows != 0) SetStart(r, startFrame[r] + rows, n);
                                int step = StepButtons(startFrame[r].ToString());
                                if (step != 0) SetStart(r, startFrame[r] + step, n);
                            }
                            if (r < 2) GUILayout.Space(6);
                        }
                    }
                }

                GUILayout.Space(24);

                // 再生ヘッドの位置。動かすとその時間にキーを打つ。
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(FaceW * 3 + 12)))
                {
                    GUILayout.Label("プレビュー", EditorStyles.miniBoldLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int r = 0; r < 3; r++)
                        {
                            using (new EditorGUILayout.VerticalScope(GUILayout.Width(FaceW)))
                            {
                                float center = CenterAt(r, time, n);
                                var face = GUILayoutUtility.GetRect(FaceW, CellH * 3, GUILayout.Width(FaceW));
                                DrawReelFace(face, r, center, n);
                                int rows = FaceInput(face, r, true);
                                if (rows != 0) KeyAtPlayhead(r, rows, n);
                                int step = StepButtons(WrapFrame(Mathf.RoundToInt(center), n).ToString());
                                if (step != 0) KeyAtPlayhead(r, step, n);
                            }
                            if (r < 2) GUILayout.Space(6);
                        }
                    }
                }

                GUILayout.Space(16);
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(340)))
                {
                    GUILayout.Space(16);
                    DrawInspector(n);
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

        /// <summary>−／＋。押した向き（−1／＋1）を返す。＋は中段のコマ番号が1つ上がる。</summary>
        private static int StepButtons(string label)
        {
            int step = 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("−", EditorStyles.miniButtonLeft, GUILayout.Width(20))) step = -1;
                GUILayout.Label(label, StripLabel, GUILayout.Width(FaceW - 40));
                if (GUILayout.Button("＋", EditorStyles.miniButtonRight, GUILayout.Width(20))) step = 1;
            }
            return step;
        }

        private void SetStart(int reel, int frame, int n)
        {
            startFrame[reel] = WrapFrame(frame, n);
            playing = false;
            time = 0f;
        }

        /// <summary>
        /// リール窓の上下ドラッグとホイール。動いたコマ数を返す（下へ＝図柄が下へ流れる＝中段のコマ番号が上がる）。
        /// </summary>
        private int FaceInput(Rect face, int reel, bool preview)
        {
            var e = Event.current;
            int id = preview ? reel + 3 : reel;
            EditorGUIUtility.AddCursorRect(face, MouseCursor.ResizeVertical);
            if (e.type == EventType.MouseDown && e.button == 0 && face.Contains(e.mousePosition))
            {
                faceDragReel = id;
                faceDragY = e.mousePosition.y;
                playing = false;
                if (preview) PickKeyAtPlayhead(reel, e.clickCount >= 2, e.shift || EditorGUI.actionKey);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && faceDragReel == id)
            {
                int rows = Mathf.RoundToInt((e.mousePosition.y - faceDragY) / CellH);
                faceDragY += rows * CellH;
                e.Use();
                Repaint();
                return rows;
            }
            else if (e.rawType == EventType.MouseUp && faceDragReel == id)
            {
                faceDragReel = -1;
            }
            else if (e.type == EventType.ScrollWheel && face.Contains(e.mousePosition))
            {
                playing = false;
                e.Use();
                Repaint();
                return e.delta.y > 0 ? 1 : -1;
            }
            return 0;
        }

        /// <summary>
        /// 再生ヘッドの時間で、中段のコマ番号を frames だけずらすキーを打つ。
        /// その時間にキーがあれば直し、なければ今の線の位置から相対キーを足す。
        /// </summary>
        private void KeyAtPlayhead(int reel, int frames, int n)
        {
            playing = false;
            int at = Mathf.RoundToInt(time / TimeSnap) * TimeSnap;
            time = at;
            var key = action.ReelKeys.Find(k => k.Reel == reel && k.Time == at);
            if (key != null)
            {
                Edit("リールキーの変更", delegate
                {
                    if (key.Absolute) key.Value = WrapFrame(Mathf.RoundToInt(key.Value) + frames, n);
                    else key.Value += frames;
                });
                SetSelection(key);
                return;
            }
            var added = new EffectReelKey { Reel = reel, Time = at };
            var positions = Positions(n);
            positions[added] = Mathf.Round(RelativeAt(reel, at, n)) - frames;
            Edit("リールキーの追加", delegate
            {
                action.ReelKeys.Add(added);
                Rebase(positions, n);
            });
            SetSelection(added);
        }

        /// <summary>プレビューのクリック。再生ヘッドの時間にあるキーを選ぶ。なければ、ダブルクリックで今の線の上に作る。</summary>
        private void PickKeyAtPlayhead(int reel, bool create, bool add)
        {
            int at = Mathf.RoundToInt(time / TimeSnap) * TimeSnap;
            var key = action.ReelKeys.Find(k => k.Reel == reel && k.Time == at);
            if (key == null && create)
            {
                time = at;
                AddKey(reel, at, SymbolCount);
                return;
            }
            if (add)
            {
                if (key != null && !selection.Contains(key)) selection.Add(key);
                RememberSelection();
            }
            else SetSelection(key);
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

        private void DrawReelFace(Rect rect, int reel, float center, int n)
        {
            EditorGUI.DrawRect(rect, ReelFaceColor);
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
            DrawRuler(ruler, graph);
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

                int reel = r;
                var targets = shown[r] ? selection.FindAll(k => k.Reel == reel) : new List<EffectReelKey>();
                bool editable = targets.Count > 0;
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
                            Edit("リールキーの変更", delegate
                            {
                                foreach (var key in targets)
                                {
                                    key.Absolute = true;
                                    key.Value = frame;
                                }
                            });
                            e.Use();
                        }
                    }
                }
                GUI.color = old;
            }
        }

        /// <summary>目盛り。クリック・ドラッグで再生ヘッドを動かす。</summary>
        private void DrawRuler(Rect rect, Rect graph)
        {
            var e = Event.current;
            float vs = ViewStart, ve = ViewEnd, span = ve - vs;
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            GUI.BeginClip(rect);
            float step = TickStep(span, rect.width);
            for (float t = Mathf.Ceil(vs / step) * step; t <= ve; t += step)
            {
                float x = X(rect, t);
                EditorGUI.DrawRect(new Rect(x, 6, 1, rect.height - 6), TickColor);
                GUI.Label(new Rect(x + 3, 2, 60, rect.height - 2), Mathf.RoundToInt(t).ToString(), TickLabel);
            }
            float head = X(rect, time);
            EditorGUI.DrawRect(new Rect(head - 1, 0, 2, rect.height), PlayheadColor);
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

        private void DrawGraph(Rect rect, int n)
        {
            var e = Event.current;
            EditorGUI.DrawRect(rect, GraphColor);
            GUI.BeginClip(rect);
            for (int i = 1; i < n; i++) EditorGUI.DrawRect(new Rect(0, i * RowH, rect.width, 1), RowLineColor);
            float snap = TimeSnap;
            if (snap / (ViewEnd - ViewStart) * rect.width >= 8f)
            {
                for (float t = Mathf.Ceil(ViewStart / snap) * snap; t <= ViewEnd; t += snap)
                    EditorGUI.DrawRect(new Rect(X(rect, t), 0, 1, rect.height), SnapLineColor);
            }
            float step = TickStep(ViewEnd - ViewStart, rect.width);
            for (float t = Mathf.Ceil(ViewStart / step) * step; t <= ViewEnd; t += step)
                EditorGUI.DrawRect(new Rect(X(rect, t), 0, 1, rect.height), RowLineColor);

            if (e.type == EventType.Repaint)
            {
                for (int r = 0; r < 3; r++) DrawCurve(r, rect, n);
                for (int r = 0; r < 3; r++) DrawKeys(r, rect, n);
                if (mode == DragMode.Box)
                {
                    var box = BoxRect();
                    box.position -= rect.position;
                    EditorGUI.DrawRect(box, new Color(0.5f, 0.7f, 0.9f, 0.15f));
                    Handles.color = new Color(0.5f, 0.7f, 0.9f, 0.8f);
                    Handles.DrawSolidRectangleWithOutline(box, Color.clear, Handles.color);
                }
            }
            EditorGUI.DrawRect(new Rect(X(rect, time) - 1, 0, 2, rect.height), PlayheadColor);
            GUI.EndClip();

            HandleGraphMouse(rect, n);
        }

        private static float Y(float p, int n) { return (n + 0.5f - p) * RowH; }

        private Color ReelColor(int reel)
        {
            var c = ReelColors[reel];
            if (!shown[reel]) c.a = DimAlpha;
            return c;
        }

        private void DrawCurve(int reel, Rect rect, int n)
        {
            var keys = action.ReelKeys.FindAll(k => k.Reel == reel);
            keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            Handles.color = ReelColor(reel);
            float start = startFrame[reel];
            float end = Mathf.Max(ViewEnd, action.ReelLength);
            if (keys.Count == 0)
            {
                Segment(rect, 0, start, end, start, n);
                return;
            }
            // 0ms は開始位置から
            if (keys[0].Time > 0) EasedSegment(rect, 0, start, keys[0].Time, start - Relative(keys[0], n), keys[0].Ease, n);
            for (int i = 1; i < keys.Count; i++)
                EasedSegment(rect, keys[i - 1].Time, start - Relative(keys[i - 1], n), keys[i].Time, start - Relative(keys[i], n), keys[i].Ease, n);
            float last = start - Relative(keys[keys.Count - 1], n);
            Segment(rect, keys[keys.Count - 1].Time, last, end, last, n);
        }

        /// <summary>イージングのある区間は細かく折れ線にして描く。</summary>
        private void EasedSegment(Rect rect, float ta, float pa, float tb, float pb, int ease, int n)
        {
            if (ease == EffectPlayer.EaseLinear || pa == pb)
            {
                Segment(rect, ta, pa, tb, pb, n);
                return;
            }
            const int pieces = 24;
            float t0 = ta, p0 = pa;
            for (int i = 1; i <= pieces; i++)
            {
                float u = i / (float)pieces;
                float t1 = Mathf.Lerp(ta, tb, u), p1 = Mathf.Lerp(pa, pb, EaseValue(ease, u));
                Segment(rect, t0, p0, t1, p1, n);
                t0 = t1;
                p0 = p1;
            }
        }

        /// <summary>配列の上下端をまたぐ所で線を切り、反対側から続ける。</summary>
        private void Segment(Rect rect, float ta, float pa, float tb, float pb, int n)
        {
            float top = n + 0.5f, bottom = 0.5f;
            float current = WrapPosition(pa, n);
            float target = current + (pb - pa);
            float xa = X(rect, ta), xb = X(rect, tb);
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

        private Vector2 KeyPosition(EffectReelKey key, Rect rect, int n)
        {
            float p = WrapPosition(startFrame[key.Reel] - Relative(key, n), n);
            return new Vector2(X(rect, key.Time), Y(p, n));
        }

        private void DrawKeys(int reel, Rect rect, int n)
        {
            var color = ReelColor(reel);
            var outline = new Color(0.1f, 0.1f, 0.1f, color.a);
            foreach (var key in action.ReelKeys)
            {
                if (key.Reel != reel) continue;
                var c = KeyPosition(key, rect, n);
                bool on = selection.Contains(key);
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

        private EffectReelKey HitKey(Vector2 mouse, Rect rect, int n)
        {
            EffectReelKey hit = null;
            float best = KeySize;
            foreach (var key in action.ReelKeys)
            {
                if (!shown[key.Reel]) continue;
                var c = KeyPosition(key, rect, n) + rect.position;
                float d = Vector2.Distance(c, mouse);
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
        /// キーをクリックで選択（Shift／Ctrl で追加・解除）、ドラッグで選んだキーをまとめて移動、
        /// 空いた所をドラッグで範囲選択、ダブルクリックで線の上にキーを追加、
        /// ホイールで拡大縮小、中ボタン（または Alt＋左）ドラッグで横へ移動。
        /// </summary>
        private void HandleGraphMouse(Rect rect, int n)
        {
            var e = Event.current;
            bool additive = e.shift || e.control || e.command;
            switch (e.GetTypeForControl(dragId))
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
                        var hit = HitKey(e.mousePosition, rect, n);
                        if (hit != null)
                        {
                            if (additive && selection.Contains(hit))
                            {
                                selection.Remove(hit);
                                e.Use();
                                break;
                            }
                            if (additive) selection.Add(hit);
                            else if (!selection.Contains(hit)) SetSelection(hit);
                            GUI.FocusControl(null);
                            // 再生ヘッドをキーに合わせ、プレビューがそのキーの位置を見せるようにする
                            time = hit.Time;
                            playing = false;
                            mode = DragMode.Move;
                            dragMoved = false;
                            dragStart = e.mousePosition;
                            dragTimes = selection.ConvertAll(k => k.Time);
                            dragValues = selection.ConvertAll(k => k.Value);
                        }
                        else if (e.clickCount == 2)
                        {
                            AddKeyOnCurve(rect, e.mousePosition, n);
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
                            float s0 = panStart - dt;
                            if (s0 < 0f) s0 = 0f;
                            SetView(s0, s0 + (panEnd - panStart));
                            break;
                        }
                        case DragMode.Box:
                            boxEnd = e.mousePosition;
                            break;
                        case DragMode.Move:
                        {
                            float dt = (e.mousePosition.x - dragStart.x) / rect.width * (ViewEnd - ViewStart);
                            // 下へ動かすとコマ番号は下がる（逆回転の向き）
                            int rows = Mathf.RoundToInt((e.mousePosition.y - dragStart.y) / RowH);
                            for (int i = 0; i < selection.Count; i++)
                            {
                                var key = selection[i];
                                int newTime = (int)Snap(dragTimes[i] + dt);
                                float value = key.Absolute ? WrapFrame(Mathf.RoundToInt(dragValues[i]) - rows, n) : dragValues[i] - rows;
                                if (newTime != key.Time || value != key.Value) dragMoved = true;
                                key.Time = newTime;
                                key.Value = value;
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
                    if (mode == DragMode.Move && dragMoved) Edit("リールキーの移動", delegate { });
                    if (mode == DragMode.Box)
                    {
                        var box = BoxRect();
                        foreach (var key in action.ReelKeys)
                        {
                            if (!shown[key.Reel] || selection.Contains(key)) continue;
                            if (box.Contains(KeyPosition(key, rect, n) + rect.position)) selection.Add(key);
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
                        var hit = HitKey(e.mousePosition, rect, n);
                        int at = Mathf.RoundToInt(TimeAt(rect, e.mousePosition.x) / TimeSnap) * TimeSnap;
                        var menu = new GenericMenu();
                        if (hit != null)
                        {
                            if (!selection.Contains(hit)) SetSelection(hit);
                            menu.AddItem(new GUIContent("コピー"), false, CopySelected);
                            menu.AddItem(new GUIContent("切り取り"), false, () => { CopySelected(); DeleteSelected(); });
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
                            if (clipboard.Count == 0) menu.AddDisabledItem(new GUIContent("貼り付け"));
                            else if (clipboard.TrueForAll(k => k.Reel == clipboard[0].Reel))
                            {
                                // 1リール分なら貼り付け先のリールを選べる
                                for (int r = 0; r < 3; r++)
                                {
                                    int reel = r;
                                    menu.AddItem(new GUIContent("貼り付け/" + EffectModel.ReelNames[r]), false, () => Paste(at, reel));
                                }
                            }
                            else menu.AddItem(new GUIContent("貼り付け"), false, () => Paste(at, -1));
                        }
                        menu.ShowAsContext();
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>ダブルクリック：その時間で一番近い線（表示中のリール）の上にキーを置く。</summary>
        private void AddKeyOnCurve(Rect rect, Vector2 mouse, int n)
        {
            int at = Mathf.RoundToInt(TimeAt(rect, mouse.x) / TimeSnap) * TimeSnap;
            float localY = mouse.y - rect.y;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int r = 0; r < 3; r++)
            {
                if (!shown[r]) continue;
                float y = Y(WrapPosition(CenterAt(r, at, n), n), n);
                float d = Mathf.Abs(y - localY);
                if (d < bestDistance) { bestDistance = d; best = r; }
            }
            if (best >= 0) AddKey(best, at, n);
        }

        /// <summary>選んだキーの欄。プレビューの右、再生ボタンの上に縦に並べる。</summary>
        private void DrawInspector(int n)
        {
            const float labelW = 72f;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("キー＋", GUILayout.Width(80))) AddKeysAtPlayhead();
                GUILayout.FlexibleSpace();
            }
            if (selection.Count == 0) return;

            var first = selection[0];
            bool sameReel = selection.TrueForAll(k => k.Reel == first.Reel);
            bool allAbsolute = selection.TrueForAll(k => k.Absolute);
            bool allRelative = selection.TrueForAll(k => !k.Absolute);

            // リール
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("リール", GUILayout.Width(labelW));
                var label = new GUIStyle(EditorStyles.boldLabel);
                if (sameReel) label.normal.textColor = ReelColors[first.Reel];
                GUILayout.Label(sameReel ? EffectModel.ReelNames[first.Reel] : "—", label, GUILayout.Width(40));
                if (selection.Count > 1) GUILayout.Label(selection.Count + " キー");
                GUILayout.FlexibleSpace();
            }

            // 時間
            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("時間", GUILayout.Width(labelW));
                EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Time == first.Time);
                int t;
                if (EffectFields.EditInt(first.Time, out t, GUILayout.Width(90)))
                    Edit("リールキーの変更", () => { foreach (var k in selection) k.Time = Mathf.Max(0, t); });
                EditorGUI.showMixedValue = false;
                GUILayout.Label("ms", GUILayout.Width(30));
                GUILayout.FlexibleSpace();
            }

            // 相対／絶対（揃っていなければどちらも選ばない）
            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("指定", GUILayout.Width(labelW));
                int current = allAbsolute ? 1 : allRelative ? 0 : -1;
                int modeIndex = GUILayout.Toolbar(current, ModeNames, GUILayout.Width(140));
                if (modeIndex != current && modeIndex >= 0)
                {
                    bool absolute = modeIndex == 1;
                    var values = selection.ConvertAll(k => k.Absolute == absolute ? k.Value
                        : absolute ? WrapFrame(Mathf.RoundToInt(startFrame[k.Reel] - Relative(k, n)), n) : Previous(k, n) - Relative(k, n));
                    Edit("リールキーの変更", delegate
                    {
                        for (int i = 0; i < selection.Count; i++)
                        {
                            selection[i].Absolute = absolute;
                            selection[i].Value = values[i];
                        }
                    });
                }
                GUILayout.FlexibleSpace();
            }

            // コマ数
            GUILayout.Space(2);
            if (allAbsolute)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("コマ番号", GUILayout.Width(labelW));
                    EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Value == first.Value);
                    int frame;
                    if (EffectFields.EditInt(Mathf.RoundToInt(first.Value), out frame, GUILayout.Width(60)))
                        Edit("リールキーの変更", () => { foreach (var k in selection) k.Value = WrapFrame(frame, n); });
                    EditorGUI.showMixedValue = false;
                    if (sameReel) GUILayout.Label(SymbolAt(first.Reel, Mathf.RoundToInt(first.Value), n), GUILayout.MinWidth(120));
                    GUILayout.FlexibleSpace();
                }
                GUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("向き", GUILayout.Width(labelW));
                    EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Direction == first.Direction);
                    EditorGUI.BeginChangeCheck();
                    int direction = EditorGUILayout.Popup(first.Direction, DirectionNames, GUILayout.Width(100));
                    if (EditorGUI.EndChangeCheck()) Edit("リールキーの変更", () => { foreach (var k in selection) k.Direction = direction; });
                    EditorGUI.showMixedValue = false;
                    if (selection.TrueForAll(k => k.Direction != EffectPlayer.ReelShortest))
                    {
                        GUILayout.Space(8);
                        GUILayout.Label("＋", GUILayout.Width(18));
                        EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Turns == first.Turns);
                        int turns;
                        if (EffectFields.EditInt(first.Turns, out turns, GUILayout.Width(44)))
                            Edit("リールキーの変更", () => { foreach (var k in selection) k.Turns = Mathf.Max(0, turns); });
                        EditorGUI.showMixedValue = false;
                        GUILayout.Label("周", GUILayout.Width(24));
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            else if (allRelative)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("移動", GUILayout.Width(labelW));
                    EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Value == first.Value);
                    float value;
                    if (EffectFields.EditFloat(first.Value, out value, GUILayout.Width(60)))
                        Edit("リールキーの変更", () => { foreach (var k in selection) k.Value = value; });
                    EditorGUI.showMixedValue = false;
                    GUILayout.Label("コマ", GUILayout.Width(40));
                    GUILayout.FlexibleSpace();
                }
            }

            // 前のキーからこのキーまでの動き方
            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("イージング", GUILayout.Width(labelW));
                EditorGUI.showMixedValue = !selection.TrueForAll(k => k.Ease == first.Ease);
                EditorGUI.BeginChangeCheck();
                int ease = EditorGUILayout.Popup(first.Ease, EaseNames, GUILayout.Width(100));
                if (EditorGUI.EndChangeCheck()) Edit("リールキーの変更", () => { foreach (var k in selection) k.Ease = ease; });
                EditorGUI.showMixedValue = false;
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

        private void SetSelection(EffectReelKey key)
        {
            selection.Clear();
            if (key != null) selection.Add(key);
            RememberSelection();
            GUI.FocusControl(null);
        }

        private void RememberSelection()
        {
            selectionIds.Clear();
            foreach (var k in selection) selectionIds.Add(new Vector2Int(k.Reel, k.Time));
        }

        private void AddKeysAtPlayhead()
        {
            int n = SymbolCount;
            int at = Mathf.RoundToInt(time / TimeSnap) * TimeSnap;
            var added = new List<EffectReelKey>();
            for (int r = 0; r < 3; r++)
            {
                if (!shown[r]) continue;
                int reel = r;
                if (action.ReelKeys.Exists(k => k.Reel == reel && k.Time == at)) continue;
                added.Add(new EffectReelKey { Reel = r, Time = at });
            }
            if (added.Count == 0) return;
            var positions = Positions(n);
            foreach (var k in added) positions[k] = Mathf.Round(RelativeAt(k.Reel, at, n) * 100f) / 100f;
            Edit("リールキーの追加", delegate
            {
                action.ReelKeys.AddRange(added);
                Rebase(positions, n);
            });
            selection.Clear();
            selection.AddRange(added);
            RememberSelection();
        }

        /// <summary>今の線の上にキーを置く（見た目は変わらない）。同じ時間のキーがあれば何もしない。</summary>
        private void AddKey(int reel, int at, int n)
        {
            if (action.ReelKeys.Exists(k => k.Reel == reel && k.Time == at)) return;
            var key = new EffectReelKey { Reel = reel, Time = at };
            var positions = Positions(n);
            positions[key] = Mathf.Round(RelativeAt(reel, at, n) * 100f) / 100f;
            Edit("リールキーの追加", delegate
            {
                action.ReelKeys.Add(key);
                Rebase(positions, n);
            });
            SetSelection(key);
        }

        // ------------------------------------------------------------ コピー・貼り付け

        /// <summary>コピーしたキー。Time は一番早いキーからの差。ウィンドウを閉じても残す。</summary>
        private static readonly List<EffectReelKey> clipboard = new List<EffectReelKey>();

        private void CopySelected()
        {
            if (selection.Count == 0) return;
            int start = selection.Min(k => k.Time);
            clipboard.Clear();
            foreach (var k in selection)
            {
                var c = k.Copy();
                c.Time -= start;
                clipboard.Add(c);
            }
        }

        /// <summary>
        /// 時間 at から貼り付ける。reel が 0～2 ならそのリールへ（1リール分のとき）、-1 なら元のリールへ。
        /// 相対キーは前のキーからのコマ数のまま貼り、後ろにあるキーは今の位置を保つ。同じ時間のキーは上書きする。
        /// </summary>
        private void Paste(int at, int reel)
        {
            if (clipboard.Count == 0) return;
            int n = SymbolCount;
            var pasted = new List<EffectReelKey>();
            foreach (var c in clipboard)
            {
                var k = c.Copy();
                k.Time += at;
                if (reel >= 0) k.Reel = reel;
                pasted.Add(k);
            }
            var positions = Positions(n);
            Edit("リールキーの貼り付け", delegate
            {
                action.ReelKeys.RemoveAll(k => pasted.Exists(p => p.Reel == k.Reel && p.Time == k.Time));
                action.ReelKeys.AddRange(pasted);
                Rebase(positions, n);
            });
            for (int r = 0; r < 3; r++) if (pasted.Exists(k => k.Reel == r)) shown[r] = true;
            selection.Clear();
            selection.AddRange(pasted);
            RememberSelection();
        }

        private void DeleteSelected()
        {
            if (selection.Count == 0) return;
            var removed = new List<EffectReelKey>(selection);
            var positions = Positions(SymbolCount);
            Edit("リールキーの削除", delegate
            {
                action.ReelKeys.RemoveAll(removed.Contains);
                Rebase(positions, SymbolCount);
            });
            SetSelection(null);
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
