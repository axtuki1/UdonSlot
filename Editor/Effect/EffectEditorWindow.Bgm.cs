using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    // 演出エディタの「BGM」タブと、アクションの BGM 欄。
    public sealed partial class EffectEditorWindow
    {
        private static readonly string[] BgmCommandNames = { "なし", "流す", "アウトロ", "止める" };
        private static readonly string[] BgmPartModeNames = { "なし", "別クリップ", "本体から" };
        private static readonly Color FanfareColor = new Color(0.95f, 0.65f, 0.25f);
        private static readonly Color IntroColor = new Color(0.35f, 0.7f, 0.95f);
        private static readonly Color MainColor = new Color(0.4f, 0.85f, 0.45f);
        private static readonly Color OutroColor = new Color(0.75f, 0.5f, 0.95f);
        private const float BgmControlWidth = 270;
        private const float BgmWaveHeight = 58;
        /// <summary>本体の波形の上に重ねる、本体から使う区間の帯の太さ。</summary>
        private const float BgmLaneHeight = 5;

        private int bgmIndex;

        private EffectBgm Bgm { get { return model != null && bgmIndex >= 0 && bgmIndex < model.Bgms.Count ? model.Bgms[bgmIndex] : null; } }

        private void AddBgm()
        {
            Edit("BGM の追加", delegate
            {
                model.Bgms.Add(new EffectBgm { Name = EffectModel.UniqueName("BGM", model.BgmNames()) });
                bgmIndex = model.Bgms.Count - 1;
            });
        }

        private void AddBgmMenu(GenericMenu menu, int index)
        {
            menu.AddItem(new GUIContent("複製"), false, () => Edit("BGM の複製", delegate
            {
                var src = model.Bgms[index];
                model.Bgms.Insert(index + 1, src.Copy(EffectModel.UniqueName(src.Name, model.BgmNames())));
                bgmIndex = index + 1;
            }));
            menu.AddItem(new GUIContent("削除"), false, () => Edit("BGM の削除", delegate
            {
                model.RemoveBgm(model.Bgms[index]);
                bgmIndex = Mathf.Clamp(bgmIndex, 0, model.Bgms.Count - 1);
            }));
        }

        private void DrawBgmView()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawBgmSources();
                var bgm = Bgm;
                if (bgm == null) { GUILayout.FlexibleSpace(); return; }
                HandleBgmSpace(bgm);
                GUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.DelayedTextField(bgm.Name, EditorStyles.boldLabel, GUILayout.Width(BgmControlWidth - 4));
                    if (EditorGUI.EndChangeCheck() && name.Trim().Length > 0 && name != bgm.Name)
                    {
                        var used = model.BgmNames();
                        used.Remove(bgm.Name);
                        Edit("BGM 名の変更", () => bgm.Name = EffectModel.UniqueName(name.Trim(), used));
                    }
                    GUILayout.Space(8);
                    PreviewButton(bgm, TagSong, "最初から", 28, () => PreviewSong(bgm));
                    GUILayout.FlexibleSpace();
                }
                GUILayout.Space(4);
                mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
                DrawBgmPartRow(bgm, bgm.Fanfare, "ファンファーレ", FanfareColor, RowFanfare);
                DrawBgmPartRow(bgm, bgm.Intro, "イントロ", IntroColor, RowIntro);
                DrawBgmMainRow(bgm);
                DrawBgmPartRow(bgm, bgm.Outro, "アウトロ", OutroColor, RowOutro);
                EditorGUILayout.EndScrollView();
                if (EffectBgmPreview.Playing) Repaint();
            }
        }

        // ------------------------------------------------------------ プレビュー

        /// <summary>プレビューの音量（ツールバー）。Game ビューの Mute Audio 中はプレビューも鳴らないので知らせる。</summary>
        private static void DrawPreviewVolume()
        {
            if (EditorUtility.audioMasterMute)
            {
                var muted = new GUIContent(" ミュート中", EditorGUIUtility.IconContent("console.warnicon.sml").image, "Game ビューの Mute Audio がオンです。クリックで解除");
                if (GUILayout.Button(muted, EditorStyles.toolbarButton)) EditorUtility.audioMasterMute = false;
            }
            GUILayout.Label(new GUIContent(EditorGUIUtility.IconContent("AudioSource Icon").image, "プレビューの音量"), GUILayout.Width(20), GUILayout.Height(18));
            EditorGUI.BeginChangeCheck();
            float volume = GUILayout.HorizontalSlider(EffectBgmPreview.Volume, 0f, 1f, GUILayout.Width(100));
            if (EditorGUI.EndChangeCheck()) EffectBgmPreview.Volume = volume;
            GUILayout.Space(8);
        }

        private const int RowFanfare = 0;
        private const int RowIntro = 1;
        private const int RowMain = 2;
        private const int RowOutro = 3;
        // プレビューの目印（行は RowFanfare～RowOutro のまま）
        private const int TagSong = 10;
        private const int TagSeam = 11;

        /// <summary>最後に触った行（▶ を押した・波形をクリックした）。スペースで鳴らす／止める。</summary>
        private int lastPreviewTag = RowMain;

        private static EffectBgmPart PartOfRow(EffectBgm bgm, int row)
        {
            return row == RowFanfare ? bgm.Fanfare : row == RowIntro ? bgm.Intro : bgm.Outro;
        }

        private static EffectBgmPreview.Segment SegmentOf(EffectBgm bgm, int row)
        {
            var s = new EffectBgmPreview.Segment { Row = row };
            if (row == RowMain)
            {
                s.Clip = bgm.Main;
                s.From = bgm.LoopStart;
                s.To = bgm.LoopEnd;
            }
            else
            {
                var part = PartOfRow(bgm, row);
                if (part.Mode != EffectBgmPart.ModeNone)
                {
                    s.Clip = part.Mode == EffectBgmPart.ModeClip ? part.Clip : bgm.Main;
                    s.From = part.Start;
                    s.To = part.End;
                }
            }
            if (s.Clip == null) return s;
            int total = s.Clip.samples;
            if (s.To <= 0 || s.To > total) s.To = total;
            s.From = Mathf.Clamp(s.From, 0, total);
            return s;
        }

        private void StartPreview(EffectBgm bgm, int tag)
        {
            if (tag == TagSong)
            {
                // ファンファーレ → イントロ → 本体のループ（レバーオン待ちはしない）
                var list = new List<EffectBgmPreview.Segment> { SegmentOf(bgm, RowFanfare), SegmentOf(bgm, RowIntro), SegmentOf(bgm, RowMain) };
                EffectBgmPreview.Play(list, RowMain, bgm.Name, tag);
            }
            else if (tag == TagSeam)
            {
                // ループ終了の少し前から鳴らし、ループ開始へ戻ってそのまま繰り返す
                var loop = SegmentOf(bgm, RowMain);
                if (loop.Clip == null) return;
                var preRoll = loop;
                preRoll.Row = -1;
                preRoll.From = Mathf.Max(loop.From, loop.To - Mathf.RoundToInt(SeamPreRoll * loop.Clip.frequency));
                EffectBgmPreview.Play(new List<EffectBgmPreview.Segment> { preRoll, loop }, RowMain, bgm.Name, tag);
            }
            else
            {
                // その行だけ。本体はループを繰り返す。クリックした位置があればそこから
                var segment = SegmentOf(bgm, tag);
                var list = new List<EffectBgmPreview.Segment> { segment };
                int cursor;
                if (segment.Clip != null && waveCursors.TryGetValue(tag, out cursor))
                {
                    if (tag != RowMain) segment.From = Mathf.Clamp(cursor, segment.From, segment.To);
                    else
                    {
                        // 本体：ループ終了より前ならそこからループへ、後ろならクリップの最後まで
                        var lead = segment;
                        lead.Row = -1;
                        lead.From = cursor;
                        lead.To = cursor < segment.To ? segment.To : segment.Clip.samples;
                        list.Insert(0, lead);
                        if (cursor >= segment.To) list.RemoveAt(1);
                    }
                    list[0] = tag != RowMain ? segment : list[0];
                }
                EffectBgmPreview.Play(list, tag == RowMain ? RowMain : -1, bgm.Name, tag);
            }
        }

        private void PreviewSong(EffectBgm bgm) { StartPreview(bgm, TagSong); }

        /// <summary>つなぎ目の確認で、ループ終了の何秒前から鳴らすか。</summary>
        private const float SeamPreRoll = 2f;

        /// <summary>▶。鳴らしている間は ■ になり、押すと止める。</summary>
        private void PreviewButton(EffectBgm bgm, int tag, string tooltip, float width, Action play)
        {
            bool playing = EffectBgmPreview.IsPlaying(bgm.Name, tag);
            string icon = playing ? "■" : tag == TagSeam ? "⇥" : "▶";
            if (!GUILayout.Button(new GUIContent(icon, tooltip), GUILayout.Width(width))) return;
            lastPreviewTag = tag;
            if (playing) EffectBgmPreview.Stop();
            else play();
        }

        /// <summary>スペース：鳴っていれば止め、止まっていれば最後に触った行を鳴らす。</summary>
        private void HandleBgmSpace(EffectBgm bgm)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode != KeyCode.Space || EditorGUIUtility.editingTextField) return;
            if (EffectBgmPreview.Playing) EffectBgmPreview.Stop();
            else StartPreview(bgm, lastPreviewTag);
            e.Use();
            Repaint();
        }

        /// <summary>再生位置の線。from〜to を rect に描いているとき。</summary>
        private static void DrawPlayhead(Rect rect, AudioClip clip, double from, double to, int row)
        {
            EffectBgmPreview.Segment segment;
            int sample;
            if (clip == null || !EffectBgmPreview.Current(out segment, out sample)) return;
            if (segment.Clip != clip || (row >= 0 && segment.Row != row)) return;
            if (to <= from || sample < from || sample > to) return;
            float x = rect.x + (float)(rect.width * (sample - from) / (to - from));
            EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), Color.white);
        }

        // ------------------------------------------------------------ 波形のズーム

        /// <summary>行ごとの表示範囲（サンプル数）。曲を切り替えたら戻す。</summary>
        private readonly Dictionary<int, double[]> waveViews = new Dictionary<int, double[]>();
        private string waveViewSong;
        /// <summary>行ごとの、クリックした再生開始位置（サンプル数）。ダブルクリックで消える。</summary>
        private readonly Dictionary<int, int> waveCursors = new Dictionary<int, int>();
        private static readonly Color CursorColor = new Color(1f, 0.85f, 0.2f);

        private void DrawCursor(Rect rect, int row, double from, double to)
        {
            int cursor;
            if (!waveCursors.TryGetValue(row, out cursor) || cursor < from || cursor > to || to <= from) return;
            float x = rect.x + (float)(rect.width * (cursor - from) / (to - from));
            EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), CursorColor);
        }

        /// <summary>中ボタンでのスクロール中の行。</summary>
        private int panRow = -1;
        private float panLastX;
        /// <summary>ズームの最小幅（サンプル数）。</summary>
        private const double MinViewSamples = 64;

        /// <summary>行の表示範囲。全体は baseFrom〜baseTo。</summary>
        private void ViewOf(EffectBgm bgm, int row, int baseFrom, int baseTo, out double from, out double to)
        {
            ViewOf(bgm, row, baseFrom, baseTo, out from, out to, baseFrom, baseTo);
        }

        /// <summary>行の表示範囲。ズームしていなければ defaultFrom〜defaultTo。</summary>
        private void ViewOf(EffectBgm bgm, int row, int baseFrom, int baseTo, out double from, out double to, double defaultFrom, double defaultTo)
        {
            if (waveViewSong != bgm.Name)
            {
                waveViews.Clear();
                waveCursors.Clear();
                waveViewSong = bgm.Name;
            }
            from = defaultFrom;
            to = defaultTo;
            double[] v;
            if (!waveViews.TryGetValue(row, out v)) return;
            // 区間が変わって外れたら全体へ戻す
            if (v[0] < baseFrom || v[1] > baseTo || v[1] - v[0] < MinViewSamples)
            {
                waveViews.Remove(row);
                return;
            }
            from = v[0];
            to = v[1];
        }

        /// <summary>
        /// 波形の上の操作：Ctrl（Mac は Cmd）＋ホイールでズーム、Shift＋ホイール・中ボタンのドラッグで左右に動かす、ダブルクリックで全体に戻す。
        /// クリックした行をスペースの対象にする。
        /// </summary>
        private void HandleWaveInput(Rect rect, int row, int baseFrom, int baseTo, double from, double to)
        {
            var e = Event.current;
            if (baseTo - baseFrom <= 0) return;
            bool inside = rect.Contains(e.mousePosition);
            switch (e.type)
            {
                case EventType.ScrollWheel:
                {
                    if (!inside || !(e.control || e.command || e.shift)) return;
                    double length = to - from;
                    float delta = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
                    if (e.shift && !(e.control || e.command))
                    {
                        double shift = length * 0.1 * Mathf.Sign(delta);
                        SetView(row, baseFrom, baseTo, from + shift, to + shift);
                    }
                    else
                    {
                        double anchor = from + length * (e.mousePosition.x - rect.x) / rect.width;
                        double scale = delta > 0 ? 1.25 : 0.8;
                        double newLength = Math.Max(MinViewSamples, Math.Min(baseTo - baseFrom, length * scale));
                        double ratio = (anchor - from) / length;
                        SetView(row, baseFrom, baseTo, anchor - newLength * ratio, anchor - newLength * ratio + newLength);
                    }
                    e.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseDown:
                {
                    if (!inside) return;
                    lastPreviewTag = row;
                    if (e.button == 2)
                    {
                        panRow = row;
                        panLastX = e.mousePosition.x;
                        e.Use();
                    }
                    else if (e.button == 0 && e.clickCount == 2)
                    {
                        waveViews.Remove(row);
                        waveCursors.Remove(row);
                        e.Use();
                        Repaint();
                    }
                    else if (e.button == 0)
                    {
                        // 再生を始める位置。ドラッグ中はついてくる
                        SetCursor(rect, row, baseFrom, baseTo, from, to);
                        cursorDragRow = row;
                        e.Use();
                        Repaint();
                    }
                    break;
                }
                case EventType.MouseDrag:
                {
                    if (cursorDragRow == row)
                    {
                        SetCursor(rect, row, baseFrom, baseTo, from, to);
                        e.Use();
                        Repaint();
                        return;
                    }
                    if (panRow != row) return;
                    double shift = -(e.mousePosition.x - panLastX) / rect.width * (to - from);
                    panLastX = e.mousePosition.x;
                    SetView(row, baseFrom, baseTo, from + shift, to + shift);
                    e.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseUp:
                {
                    if (cursorDragRow == row)
                    {
                        // 離したところから。この行を鳴らしていたら鳴らし直す
                        cursorDragRow = -1;
                        if (EffectBgmPreview.Playing && EffectBgmPreview.Owner == waveViewSong && EffectBgmPreview.Tag == row) StartPreview(Bgm, row);
                        e.Use();
                        return;
                    }
                    if (panRow != row) return;
                    panRow = -1;
                    e.Use();
                    break;
                }
            }
        }

        private static readonly Color OutsideWaveColor = new Color(0.38f, 0.38f, 0.38f);
        /// <summary>端を詰めている行と、どちらの端か（0 始点 1 終点）。</summary>
        private int trimRow = -1;
        private int trimEdge;
        /// <summary>詰めたあとに残す最小の長さ（サンプル数）。</summary>
        private const int MinPartSamples = 64;

        /// <summary>ファンファーレ・イントロ・アウトロの行の波形で、使う区間の端をドラッグして詰める。</summary>
        private void HandleTrim(Rect rect, int row, EffectBgmPart part, int total, double from, double to)
        {
            var e = Event.current;
            int start = Mathf.Clamp(part.Start, 0, total);
            int end = part.End > 0 && part.End < total ? part.End : total;
            float xa = rect.x + (float)(rect.width * (start - from) / (to - from));
            float xb = rect.x + (float)(rect.width * (end - from) / (to - from));
            var edgeA = new Rect(xa - 5, rect.y, 10, rect.height);
            var edgeB = new Rect(xb - 5, rect.y, 10, rect.height);
            bool showA = xa >= rect.x - 1 && xa <= rect.xMax + 1, showB = xb >= rect.x - 1 && xb <= rect.xMax + 1;
            if (showA) EditorGUIUtility.AddCursorRect(edgeA, MouseCursor.ResizeHorizontal);
            if (showB) EditorGUIUtility.AddCursorRect(edgeB, MouseCursor.ResizeHorizontal);
            switch (e.type)
            {
                case EventType.MouseDown:
                {
                    if (e.button != 0 || e.clickCount > 1) return;
                    int edge = showA && edgeA.Contains(e.mousePosition) ? 0 : showB && edgeB.Contains(e.mousePosition) ? 1 : -1;
                    if (edge < 0) return;
                    trimRow = row;
                    trimEdge = edge;
                    lastPreviewTag = row;
                    // ドラッグ中に表示が動かないよう、今の表示範囲で止めておく
                    waveViews[row] = new[] { from, to };
                    e.Use();
                    break;
                }
                case EventType.MouseDrag:
                {
                    if (trimRow != row) return;
                    int at = (int)Math.Round(Math.Max(0, Math.Min(total, from + (to - from) * (e.mousePosition.x - rect.x) / rect.width)));
                    if (trimEdge == 0) part.Start = Mathf.Clamp(at, 0, Mathf.Max(0, end - MinPartSamples));
                    else
                    {
                        int value = Mathf.Clamp(at, Mathf.Min(total, start + MinPartSamples), total);
                        part.End = value >= total ? 0 : value;
                    }
                    e.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseUp:
                {
                    if (trimRow != row) return;
                    trimRow = -1;
                    e.Use();
                    Edit("区間の変更", () => { });
                    break;
                }
            }
        }

        /// <summary>黄色い線をドラッグしている行。</summary>
        private int cursorDragRow = -1;

        private void SetCursor(Rect rect, int row, int baseFrom, int baseTo, double from, double to)
        {
            double at = from + (to - from) * (Event.current.mousePosition.x - rect.x) / rect.width;
            waveCursors[row] = (int)Math.Round(Math.Max(baseFrom, Math.Min(baseTo, at)));
        }

        private void SetView(int row, int baseFrom, int baseTo, double from, double to)
        {
            double length = to - from;
            if (from < baseFrom) { from = baseFrom; to = from + length; }
            if (to > baseTo) { to = baseTo; from = Math.Max(baseFrom, to - length); }
            if (from <= baseFrom && to >= baseTo) waveViews.Remove(row);
            else waveViews[row] = new[] { from, to };
        }

        // ------------------------------------------------------------ 行

        private void DrawBgmPartRow(EffectBgm bgm, EffectBgmPart part, string label, Color color, int row)
        {
            bool isIntro = row == RowIntro;
            var shown = SegmentOf(bgm, row);
            RowRule(color);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(BgmControlWidth)))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        PartLabel(label, color);
                        int mode = EditorGUILayout.Popup(part.Mode, BgmPartModeNames, GUILayout.Width(100));
                        if (mode != part.Mode) Edit(label + "の変更", () => SetPartMode(bgm, part, mode, isIntro));
                        using (new EditorGUI.DisabledScope(shown.Clip == null))
                        {
                            PreviewButton(bgm, row, "", 24, () => StartPreview(bgm, row));
                        }
                    }
                    if (part.Mode == EffectBgmPart.ModeClip)
                    {
                        EditorGUI.BeginChangeCheck();
                        var clip = (AudioClip)EditorGUILayout.ObjectField(part.Clip, typeof(AudioClip), false);
                        if (EditorGUI.EndChangeCheck()) Edit(label + "の変更", delegate
                        {
                            part.Clip = clip;
                            part.Start = part.End = 0;
                        });
                    }
                    if (part.Mode != EffectBgmPart.ModeNone)
                    {
                        var source = part.Mode == EffectBgmPart.ModeClip ? part.Clip : bgm.Main;
                        SampleField("開始", part.Start, source, false, v => part.Start = v);
                        SampleField("終了", part.End, source, true, v => part.End = v);
                    }
                    if (isIntro)
                    {
                        EditorGUI.BeginChangeCheck();
                        bool wait = GUILayout.Toggle(bgm.WaitLever, "レバーオンで開始");
                        if (EditorGUI.EndChangeCheck()) Edit("レバーオンで開始の変更", () => bgm.WaitLever = wait);
                    }
                }
                Rect outer, bar;
                WaveRects(part.Mode == EffectBgmPart.ModeNone ? 22 : BgmWaveHeight, out outer, out bar);
                var rect = Inset(outer);
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(outer, CellColor);
                if (shown.Clip != null)
                {
                    // 表示はクリップ全体を範囲にし、ズームしていなければ使う区間の前後を少し広げて見せる
                    int total = shown.Clip.samples;
                    double pad = Math.Max(256, (shown.To - shown.From) * 0.05);
                    double from, to;
                    ViewOf(bgm, row, 0, total, out from, out to, Math.Max(0, shown.From - pad), Math.Min(total, shown.To + pad));
                    WaveScrollbar(bar, row, 0, total, from, to);
                    HandleTrim(rect, row, part, total, from, to);
                    HandleWaveInput(rect, row, 0, total, from, to);
                    if (Event.current.type == EventType.Repaint)
                    {
                        DrawWave(rect, shown.Clip, from, to, OutsideWaveColor);
                        double a = Math.Max(from, shown.From), b = Math.Min(to, shown.To);
                        if (b > a)
                        {
                            float xa = rect.x + (float)(rect.width * (a - from) / (to - from));
                            float xb = rect.x + (float)(rect.width * (b - from) / (to - from));
                            EditorGUI.DrawRect(new Rect(xa, rect.y, xb - xa, rect.height), CellColor);
                            DrawWave(new Rect(xa, rect.y, Mathf.Max(1, xb - xa), rect.height), shown.Clip, a, b, color);
                            if (shown.From >= from) EditorGUI.DrawRect(new Rect(xa, rect.y, 1, rect.height), color);
                            if (shown.To <= to) EditorGUI.DrawRect(new Rect(xb - 1, rect.y, 1, rect.height), color);
                        }
                        DrawCursor(rect, row, from, to);
                        DrawPlayhead(rect, shown.Clip, from, to, row);
                    }
                }
            }
            GUILayout.Space(6);
        }

        /// <summary>「本体から」に切り替えたとき、区間が空ならループの前（イントロ）・後ろ（アウトロ）を入れておく。</summary>
        private static void SetPartMode(EffectBgm bgm, EffectBgmPart part, int mode, bool isIntro)
        {
            part.Mode = mode;
            part.Start = part.End = 0;
            if (mode != EffectBgmPart.ModeMain) return;
            if (isIntro) part.End = bgm.LoopStart;
            else if (part == bgm.Outro) part.Start = bgm.LoopEnd;
        }

        private void DrawBgmMainRow(EffectBgm bgm)
        {
            RowRule(MainColor);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(BgmControlWidth)))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        PartLabel("本体", MainColor);
                        EditorGUI.BeginChangeCheck();
                        var clip = (AudioClip)EditorGUILayout.ObjectField(bgm.Main, typeof(AudioClip), false);
                        if (EditorGUI.EndChangeCheck()) Edit("本体の変更", () => bgm.Main = clip);
                        using (new EditorGUI.DisabledScope(bgm.Main == null))
                        {
                            PreviewButton(bgm, RowMain, "ループ", 24, () => StartPreview(bgm, RowMain));
                            PreviewButton(bgm, TagSeam, "つなぎ目：ループ終了の少し前から", 24, () => StartPreview(bgm, TagSeam));
                        }
                        if (bgm.Main != null && UsesMainOffsets(bgm) && !DecompressOnLoad(bgm.Main))
                            GUILayout.Label(new GUIContent("⚠", "Load Type を Decompress On Load にすると、区間の位置が正確になり、拡大した波形も細かく出ます"), GUILayout.Width(18));
                    }
                    SampleField("ループ開始", bgm.LoopStart, bgm.Main, false, v => bgm.LoopStart = v);
                    SampleField("ループ終了", bgm.LoopEnd, bgm.Main, true, v => bgm.LoopEnd = v);
                }
                Rect outer, bar;
                WaveRects(BgmWaveHeight + UsedLanes(bgm) * LaneStep, out outer, out bar);
                DrawMainWave(outer, bar, bgm);
            }
            GUILayout.Space(6);
        }

        private static bool UsesMainOffsets(EffectBgm bgm)
        {
            if (bgm.LoopStart > 0 || bgm.LoopEnd > 0) return true;
            foreach (var p in new[] { bgm.Fanfare, bgm.Intro, bgm.Outro })
                if (p.Mode == EffectBgmPart.ModeMain && (p.Start > 0 || p.End > 0)) return true;
            return false;
        }

        /// <summary>行の上の色付きの線（その行の設定がどこまでかを分かりやすくする）。</summary>
        private static void RowRule(Color color)
        {
            var rect = GUILayoutUtility.GetRect(10, 2, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width - 12, rect.height), color);
            GUILayout.Space(3);
        }

        private static void PartLabel(string label, Color color)
        {
            var rect = GUILayoutUtility.GetRect(10, 18, GUILayout.Width(96));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 3, 4, rect.height - 6), color);
            GUI.Label(new Rect(rect.x + 8, rect.y, rect.width - 8, rect.height), label, EditorStyles.boldLabel);
        }

        /// <summary>サンプル数の欄。横に秒数を出す。</summary>
        private void SampleField(string label, int value, AudioClip clip, bool zeroIsEnd, Action<int> set)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(64));
                EditorGUI.BeginChangeCheck();
                int v = EffectFields.Int(value, GUILayout.Width(100));
                if (EditorGUI.EndChangeCheck()) Edit(label + "の変更", () => set(Mathf.Max(0, v)));
                GUILayout.Label(SampleText(value, clip, zeroIsEnd), EditorStyles.miniLabel);
            }
        }

        private static string SampleText(int value, AudioClip clip, bool zeroIsEnd)
        {
            if (zeroIsEnd && value <= 0) return "最後";
            return clip != null && clip.frequency > 0 ? (value / (double)clip.frequency).ToString("0.000") + " s" : "";
        }

        private static bool DecompressOnLoad(AudioClip clip)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            return importer == null || importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad;
        }

        // ------------------------------------------------------------ 本体の波形と区間

        private static readonly Color LoopBandColor = new Color(0.4f, 0.85f, 0.45f, 0.18f);
        private static readonly Color WaveColor = new Color(0.72f, 0.72f, 0.72f);

        /// <summary>ドラッグ中の区間の端。</summary>
        private string dragSong;
        private int dragHandle = -1;
        /// <summary>区間ごと動かすときの dragHandle（＋区間の番号）と、つかんだ位置の始点からのずれ。</summary>
        private const int MoveHandle = 100;
        private int dragOffset;

        private struct BgmHandle
        {
            public float X;
            public float YMin, YMax;
        }

        // 区間の端の番号：0,1 ループ開始・終了、2+2p, 3+2p 本体から使う区間（p=0 ファンファーレ 1 イントロ 2 アウトロ）の開始・終了。
        private static int GetHandleValue(EffectBgm bgm, int handle, int total)
        {
            if (handle == 0) return bgm.LoopStart;
            if (handle == 1) return bgm.LoopEnd > 0 ? bgm.LoopEnd : total;
            var part = BgmParts(bgm)[(handle - 2) / 2];
            if (handle % 2 == 0) return part.Start;
            return part.End > 0 ? part.End : total;
        }

        private static void SetHandleValue(EffectBgm bgm, int handle, int value, int total)
        {
            // 最後まで引いたら「最後」（0）にする
            int end = value >= total ? 0 : value;
            if (handle == 0) bgm.LoopStart = value;
            else if (handle == 1) bgm.LoopEnd = end;
            else
            {
                var part = BgmParts(bgm)[(handle - 2) / 2];
                if (handle % 2 == 0) part.Start = value;
                else part.End = end;
            }
        }

        private static EffectBgmPart[] BgmParts(EffectBgm bgm)
        {
            return new[] { bgm.Fanfare, bgm.Intro, bgm.Outro };
        }

        private static readonly Color[] PartColors = { FanfareColor, IntroColor, OutroColor };
        private static readonly string[] PartNames = { "ファンファーレ", "イントロ", "アウトロ" };
        private const float LaneTagHeight = 14;
        /// <summary>区間の段1つ分の高さ（段の間に1px空ける）。</summary>
        private const float LaneStep = LaneTagHeight + 1;

        private static int UsedLanes(EffectBgm bgm)
        {
            int n = 0;
            foreach (var p in BgmParts(bgm)) if (p.Mode == EffectBgmPart.ModeMain) n++;
            return n;
        }
        private static GUIStyle laneTagStyle;

        private static GUIStyle LaneTagStyle
        {
            get
            {
                if (laneTagStyle != null) return laneTagStyle;
                laneTagStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(3, 3, 0, 0), clipping = TextClipping.Clip };
                laneTagStyle.normal.textColor = new Color(0.1f, 0.1f, 0.1f);
                return laneTagStyle;
            }
        }

        private void DrawMainWave(Rect outer, Rect bar, EffectBgm bgm)
        {
            var clip = bgm.Main;
            var e = Event.current;
            var rect = Inset(outer);
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Repaint) EditorGUI.DrawRect(outer, CellColor);
            if (clip == null || clip.samples <= 0) return;
            int total = clip.samples;
            double from, to;
            ViewOf(bgm, RowMain, 0, total, out from, out to);
            WaveScrollbar(bar, RowMain, 0, total, from, to);
            var parts = BgmParts(bgm);
            float lanes = UsedLanes(bgm) * LaneStep;
            var wave = new Rect(rect.x, rect.y + lanes, rect.width, rect.height - lanes);
            double viewFrom = from, viewLength = to - from;
            Func<int, float> toX = s => rect.x + (float)(rect.width * (s - viewFrom) / viewLength);

            var handles = new List<BgmHandle>();
            handles.Add(new BgmHandle { X = toX(GetHandleValue(bgm, 0, total)), YMin = wave.y, YMax = wave.yMax });
            handles.Add(new BgmHandle { X = toX(GetHandleValue(bgm, 1, total)), YMin = wave.y, YMax = wave.yMax });
            // 本体から使う区間は、波形の上に1段ずつ（使っている区間だけ）
            var laneY = new float[parts.Length];
            for (int p = 0, lane = 0; p < parts.Length; p++)
            {
                bool used = parts[p].Mode == EffectBgmPart.ModeMain;
                laneY[p] = used ? rect.y + (lane++) * LaneStep : -1;
                // 使っていない区間は掴めないよう、高さ 0 にしておく
                float yMin = used ? laneY[p] : 0, yMax = used ? laneY[p] + LaneTagHeight : 0;
                handles.Add(new BgmHandle { X = toX(GetHandleValue(bgm, 2 + 2 * p, total)), YMin = yMin, YMax = yMax });
                handles.Add(new BgmHandle { X = toX(GetHandleValue(bgm, 3 + 2 * p, total)), YMin = yMin, YMax = yMax });
            }

            for (int i = 0; i < handles.Count; i++)
            {
                var h = handles[i];
                if (h.YMax <= h.YMin || h.X < rect.x - 4 || h.X > rect.xMax + 4) continue;
                EditorGUIUtility.AddCursorRect(new Rect(h.X - 4, h.YMin, 8, h.YMax - h.YMin), MouseCursor.ResizeHorizontal);
            }
            // 区間の始点に付ける名札。始点が表示範囲より左なら左端に寄せる
            var tags = new Rect[parts.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                if (laneY[p] < 0) continue;
                float a = handles[2 + 2 * p].X, b = handles[3 + 2 * p].X;
                if (b < rect.x || a > rect.xMax) continue;
                float x = Mathf.Max(rect.x, a);
                float width = LaneTagStyle.CalcSize(new GUIContent(PartNames[p])).x + 6;
                // 区間より長い名前は区間の中で切る（終点の線を隠さない）
                width = Mathf.Min(width, Mathf.Min(rect.xMax, b) - 1 - x);
                if (width < 4) continue;
                tags[p] = new Rect(x, laneY[p], width, LaneTagHeight);
            }
            // 段の中ほど（端以外）をつかむと、長さを保ったまま区間ごと動かせる
            var bodies = new Rect[parts.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                if (laneY[p] < 0) continue;
                float a = Mathf.Max(rect.x, handles[2 + 2 * p].X + 5), b = Mathf.Min(rect.xMax, handles[3 + 2 * p].X - 5);
                if (b <= a) continue;
                bodies[p] = new Rect(a, laneY[p], b - a, LaneTagHeight);
                EditorGUIUtility.AddCursorRect(bodies[p], MouseCursor.Pan);
            }

            switch (e.type)
            {
                case EventType.Repaint:
                {
                    float loopA = Mathf.Max(rect.x, handles[0].X), loopB = Mathf.Min(rect.xMax, handles[1].X);
                    if (loopB > loopA) EditorGUI.DrawRect(new Rect(loopA, wave.y, loopB - loopA, wave.height), LoopBandColor);
                    DrawWave(wave, clip, from, to, WaveColor);
                    DrawMarker(rect, wave, handles[0].X, MainColor);
                    DrawMarker(rect, wave, handles[1].X - 1, MainColor);
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (laneY[p] < 0) continue;
                        float a = Mathf.Max(rect.x, handles[2 + 2 * p].X), b = Mathf.Min(rect.xMax, handles[3 + 2 * p].X);
                        if (b < a) continue;
                        var color = PartColors[p];
                        EditorGUI.DrawRect(new Rect(a, laneY[p], Mathf.Max(1, b - a), LaneTagHeight), new Color(color.r, color.g, color.b, 0.3f));
                        if (handles[3 + 2 * p].X <= rect.xMax) EditorGUI.DrawRect(new Rect(b - 1, laneY[p], 1, LaneTagHeight), color);
                    }
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (tags[p].width <= 0) continue;
                        EditorGUI.DrawRect(tags[p], PartColors[p]);
                        GUI.Label(tags[p], PartNames[p], LaneTagStyle);
                    }
                    DrawCursor(wave, RowMain, from, to);
                    DrawPlayhead(wave, clip, from, to, -1);
                    break;
                }
                case EventType.MouseDown:
                {
                    if (e.button != 0 || e.clickCount > 1 || !outer.Contains(e.mousePosition)) break;
                    int best = -1;
                    float bestDistance = 5;
                    for (int i = 0; i < handles.Count; i++)
                    {
                        var h = handles[i];
                        if (e.mousePosition.y < h.YMin || e.mousePosition.y > h.YMax) continue;
                        float d = Mathf.Abs(e.mousePosition.x - h.X);
                        // 区間の帯は細いので、同じくらいの距離ならそちらを優先する
                        if (d < bestDistance || (d <= bestDistance + 2 && i >= 2 && best < 2)) { best = i; bestDistance = d; }
                    }
                    if (best < 0)
                    {
                        // 端でなければ、段の中ほど：区間ごと動かす
                        for (int p = 0; p < parts.Length && best < 0; p++)
                        {
                            if (bodies[p].width <= 0 || !bodies[p].Contains(e.mousePosition)) continue;
                            best = MoveHandle + p;
                            double at = from + (to - from) * (e.mousePosition.x - rect.x) / rect.width;
                            dragOffset = (int)Math.Round(at) - GetHandleValue(bgm, 2 + 2 * p, total);
                        }
                    }
                    if (best < 0) break;
                    lastPreviewTag = RowMain;
                    dragSong = bgm.Name;
                    dragHandle = best;
                    GUIUtility.hotControl = controlId;
                    e.Use();
                    break;
                }
                case EventType.MouseDrag:
                {
                    if (dragSong != bgm.Name || dragHandle < 0) break;
                    double at = from + (to - from) * (e.mousePosition.x - rect.x) / rect.width;
                    if (dragHandle >= MoveHandle)
                    {
                        int p = dragHandle - MoveHandle;
                        int start = GetHandleValue(bgm, 2 + 2 * p, total), length = GetHandleValue(bgm, 3 + 2 * p, total) - start;
                        int newStart = Mathf.Clamp((int)Math.Round(at) - dragOffset, 0, Mathf.Max(0, total - length));
                        SetHandleValue(bgm, 2 + 2 * p, newStart, total);
                        SetHandleValue(bgm, 3 + 2 * p, newStart + length, total);
                    }
                    else SetHandleValue(bgm, dragHandle, (int)Math.Round(Math.Max(0, Math.Min(total, at))), total);
                    e.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseUp:
                {
                    if (dragSong != bgm.Name || dragHandle < 0) break;
                    dragSong = null;
                    dragHandle = -1;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    // 値はドラッグ中に変えてあるので、ここでまとめて書き込む
                    Edit("区間の変更", () => { });
                    break;
                }
            }
            HandleWaveInput(rect, RowMain, 0, total, from, to);
        }

        /// <summary>波形の左右の余白（端の区間をつかみやすくする）。</summary>
        private const float WaveInset = 8;
        /// <summary>ズーム中に出すスクロールバーの高さ（場所は常に空けておく）。</summary>
        private const float WaveBarHeight = 12;

        /// <summary>波形の場所と、その下のスクロールバーの場所。右にも余白を空ける。</summary>
        private static void WaveRects(float height, out Rect wave, out Rect bar)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                wave = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
                bar = GUILayoutUtility.GetRect(10, WaveBarHeight, GUILayout.ExpandWidth(true));
            }
            GUILayout.Space(12);
        }

        private static Rect Inset(Rect rect)
        {
            return new Rect(rect.x + WaveInset, rect.y, Mathf.Max(1, rect.width - 2 * WaveInset), rect.height);
        }

        /// <summary>ズーム中だけ出す、表示範囲のスクロールバー。</summary>
        private void WaveScrollbar(Rect bar, int row, int baseFrom, int baseTo, double from, double to)
        {
            if (from <= baseFrom && to >= baseTo) return;
            var r = Inset(bar);
            double length = to - from;
            EditorGUI.BeginChangeCheck();
            float value = GUI.HorizontalScrollbar(new Rect(r.x, r.y + 2, r.width, r.height - 2), (float)from, (float)length, baseFrom, baseTo);
            if (EditorGUI.EndChangeCheck())
            {
                SetView(row, baseFrom, baseTo, value, value + length);
                Repaint();
            }
        }

        private static void DrawMarker(Rect rect, Rect wave, float x, Color color)
        {
            if (x < rect.x || x > rect.xMax) return;
            EditorGUI.DrawRect(new Rect(x, wave.y, 1, wave.height), color);
        }

        // ------------------------------------------------------------ 波形

        /// <summary>クリップごとの、ブロックごとの振幅（0〜1）。全体を見るときに使う。</summary>
        private static readonly Dictionary<int, float[]> PeakCache = new Dictionary<int, float[]>();
        private static MethodInfo minMaxMethod;
        private static bool minMaxSearched;
        /// <summary>拡大したときにサンプルから直接描く上限（表示範囲のサンプル数）。</summary>
        private const int ExactLimit = 1 << 20;

        /// <summary>クリップの from〜to（サンプル数）を rect に描く。拡大していて読めるクリップなら、サンプルから直接描く。</summary>
        private static void DrawWave(Rect rect, AudioClip clip, double from, double to, Color color)
        {
            int total = clip.samples;
            if (total <= 0 || to <= from) return;
            int columns = Mathf.Max(1, (int)rect.width);
            float half = rect.height / 2 - 2;
            float mid = rect.y + rect.height / 2;
            var peaks = Peaks(clip);
            bool exact = peaks == null || (to - from) / columns < (double)total / Mathf.Max(1, peaks.Length);
            float[] mins, maxs;
            if (exact && to - from <= ExactLimit && ExactColumns(clip, from, to, columns, out mins, out maxs))
            {
                for (int x = 0; x < columns; x++)
                {
                    float top = mid - maxs[x] * half, bottom = mid - mins[x] * half;
                    EditorGUI.DrawRect(new Rect(rect.x + x, top, 1, Mathf.Max(1, bottom - top)), color);
                }
                return;
            }
            if (peaks == null || peaks.Length == 0) return;
            for (int x = 0; x < columns; x++)
            {
                double s0 = from + (to - from) * x / columns;
                double s1 = from + (to - from) * (x + 1) / columns;
                int b0 = (int)(s0 / total * peaks.Length);
                int b1 = Mathf.Max(b0 + 1, (int)(s1 / total * peaks.Length));
                float peak = 0;
                for (int b = b0; b < b1 && b < peaks.Length; b++) peak = Mathf.Max(peak, peaks[b]);
                float h = Mathf.Max(1, peak * half);
                EditorGUI.DrawRect(new Rect(rect.x + x, mid - h, 1, h * 2), color);
            }
        }

        // 直前に読んだ範囲（再生中は毎フレーム描き直すので、同じ範囲なら読み直さない）
        private static readonly Dictionary<string, float[][]> ExactCache = new Dictionary<string, float[][]>();

        /// <summary>表示範囲のサンプルを読んで、列ごとの最小・最大を出す（Decompress On Load のクリップだけ）。</summary>
        private static bool ExactColumns(AudioClip clip, double from, double to, int columns, out float[] mins, out float[] maxs)
        {
            mins = maxs = null;
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad) return false;
            int start = Mathf.Clamp((int)Math.Floor(from), 0, clip.samples - 1);
            int end = Mathf.Clamp((int)Math.Ceiling(to), start + 1, clip.samples);
            string key = clip.GetInstanceID() + ":" + start + ":" + end + ":" + columns;
            float[][] cached;
            if (ExactCache.TryGetValue(key, out cached))
            {
                mins = cached[0];
                maxs = cached[1];
                return true;
            }
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData()) return false;
            int channels = Mathf.Max(1, clip.channels);
            var data = new float[(end - start) * channels];
            if (!clip.GetData(data, start)) return false;
            mins = new float[columns];
            maxs = new float[columns];
            int frames = end - start;
            for (int x = 0; x < columns; x++)
            {
                int f0 = (int)((from - start) + (to - from) * x / columns);
                int f1 = (int)((from - start) + (to - from) * (x + 1) / columns);
                f0 = Mathf.Clamp(f0, 0, frames - 1);
                f1 = Mathf.Clamp(Mathf.Max(f1, f0 + 1), 1, frames);
                float lo = 1f, hi = -1f;
                for (int f = f0; f < f1; f++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        float v = data[f * channels + c];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                }
                mins[x] = Mathf.Clamp(lo, -1f, 1f);
                maxs[x] = Mathf.Clamp(hi, -1f, 1f);
            }
            if (ExactCache.Count > 16) ExactCache.Clear();
            ExactCache[key] = new[] { mins, maxs };
            return true;
        }

        private static float[] Peaks(AudioClip clip)
        {
            int id = clip.GetInstanceID();
            float[] peaks;
            if (PeakCache.TryGetValue(id, out peaks)) return peaks;
            peaks = PeaksFromImporter(clip) ?? PeaksFromData(clip);
            PeakCache[id] = peaks;
            return peaks;
        }

        /// <summary>Inspector のプレビューと同じ、インポーターが持つ波形（Load Type によらず読める）。</summary>
        private static float[] PeaksFromImporter(AudioClip clip)
        {
            if (!minMaxSearched)
            {
                minMaxSearched = true;
                var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.AudioUtil");
                if (type != null) minMaxMethod = type.GetMethod("GetMinMaxData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            if (minMaxMethod == null || importer == null) return null;
            float[] data;
            try { data = minMaxMethod.Invoke(null, new object[] { importer }) as float[]; }
            catch (Exception) { return null; }
            int channels = Mathf.Max(1, clip.channels);
            if (data == null || data.Length < 2 * channels) return null;
            int blocks = data.Length / (2 * channels);
            var peaks = new float[blocks];
            for (int i = 0; i < blocks; i++)
            {
                float peak = 0;
                for (int c = 0; c < channels; c++)
                {
                    int o = (i * channels + c) * 2;
                    peak = Mathf.Max(peak, Mathf.Abs(data[o]), Mathf.Abs(data[o + 1]));
                }
                peaks[i] = Mathf.Clamp01(peak);
            }
            return peaks;
        }

        /// <summary>インポーターから読めないとき：クリップのデータから（Decompress On Load のときだけ読める）。</summary>
        private static float[] PeaksFromData(AudioClip clip)
        {
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad) return null;
            const int blocks = 2048;
            int channels = Mathf.Max(1, clip.channels);
            var samples = new float[clip.samples * channels];
            if (samples.Length == 0 || !clip.GetData(samples, 0)) return null;
            var peaks = new float[blocks];
            for (int i = 0; i < samples.Length; i++)
            {
                int b = (int)((long)(i / channels) * blocks / clip.samples);
                if (b >= blocks) b = blocks - 1;
                float v = Mathf.Abs(samples[i]);
                if (v > peaks[b]) peaks[b] = v;
            }
            return peaks;
        }

        // ------------------------------------------------------------ AudioSource

        /// <summary>BGM を鳴らす AudioSource（2つ）。どの曲でも共通。</summary>
        private void DrawBgmSources()
        {
            var so = new SerializedObject(player);
            var sources = so.FindProperty("bgmSources");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("AudioSource", GUILayout.Width(80));
                for (int i = 0; i < 2; i++)
                {
                    var current = i < sources.arraySize ? sources.GetArrayElementAtIndex(i).objectReferenceValue as AudioSource : null;
                    EditorGUI.BeginChangeCheck();
                    var picked = (AudioSource)EditorGUILayout.ObjectField(current, typeof(AudioSource), true, GUILayout.Width(180));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (sources.arraySize < 2) sources.arraySize = 2;
                        sources.GetArrayElementAtIndex(i).objectReferenceValue = picked;
                        so.ApplyModifiedProperties();
                    }
                }
                bool missing = sources.arraySize < 2 || sources.GetArrayElementAtIndex(0).objectReferenceValue == null || sources.GetArrayElementAtIndex(1).objectReferenceValue == null;
                if (missing && GUILayout.Button("作成", GUILayout.Width(60))) CreateBgmSources();
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>
        /// EffectPlayer の下に「BGM」オブジェクトを作り、AudioSource を2つ付ける。
        /// SoundBoard に ALWAYS_BGM の AudioSource があれば、その設定（音量・3D の設定など）を写す。
        /// </summary>
        private void CreateBgmSources()
        {
            var go = new GameObject("BGM");
            Undo.RegisterCreatedObjectUndo(go, "BGM の AudioSource の作成");
            go.transform.SetParent(player.transform, false);
            var template = BgmTemplateSource();
            var created = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                var source = go.AddComponent<AudioSource>();
                if (template != null) EditorUtility.CopySerialized(template, source);
                source.clip = null;
                source.playOnAwake = false;
                source.loop = false;
                created[i] = source;
            }
            var so = new SerializedObject(player);
            var sources = so.FindProperty("bgmSources");
            sources.arraySize = 2;
            for (int i = 0; i < 2; i++) sources.GetArrayElementAtIndex(i).objectReferenceValue = created[i];
            so.ApplyModifiedProperties();
        }

        private AudioSource BgmTemplateSource()
        {
            var mainBoard = player.mainBoard;
            var soundBoard = mainBoard != null && mainBoard.subBoard != null ? mainBoard.subBoard.soundBoard : null;
            int index = (int)SoundType.ALWAYS_BGM;
            if (soundBoard == null || soundBoard.source == null || index >= soundBoard.source.Length) return null;
            return soundBoard.source[index];
        }

        // ------------------------------------------------------------ アクションの BGM 欄

        private void DrawBgmSection(EffectAction action)
        {
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);
            GUI.Label(new Rect(rect.x + 6, rect.y, rect.width, rect.height), "BGM", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                int command = Mathf.Clamp(action.BgmCommand, 0, BgmCommandNames.Length - 1);
                int pickedCommand = EditorGUILayout.Popup(command, BgmCommandNames, GUILayout.Width(100));
                if (pickedCommand != command) Edit("BGM の変更", () => action.BgmCommand = pickedCommand);
                if (action.BgmCommand == EffectPlayer.BgmPlay)
                {
                    var names = model.BgmNames();
                    int current = action.Bgm != null ? model.Bgms.IndexOf(action.Bgm) : -1;
                    int picked = EditorGUILayout.Popup(current, names.ToArray(), GUILayout.Width(200));
                    if (picked != current && picked >= 0) Edit("BGM の変更", () => action.Bgm = model.Bgms[picked]);
                }
                GUILayout.FlexibleSpace();
            }
        }
    }
}
