using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>
    /// 演出エディタの BGM のプレビュー。隠した AudioSource 2つで、区間を本番と同じように予約してつなぐ。
    /// 音量はエディタに保存する（シーンには何も残さない）。
    /// </summary>
    internal static class EffectBgmPreview
    {
        internal struct Segment
        {
            public AudioClip Clip;
            public int From;
            public int To;
            /// <summary>どの行の区間か（0 ファンファーレ 1 イントロ 2 本体 3 アウトロ）。</summary>
            public int Row;
        }

        private const string VolumeKey = "AX.UdonSlot.EffectBgmPreviewVolume";
        private const double Lead = 0.05;
        private const double Lookahead = 1.0;

        private static GameObject host;
        private static AudioSource[] sources;
        private static List<Segment> plan;
        /// <summary>最後まで来たら繰り返す区間（-1 は繰り返さない）。</summary>
        private static int repeatIndex = -1;
        private static int nextIndex;
        private static int tailSource = 1;
        private static double tailStart, tailEnd;
        private static readonly double[] sourceStart = new double[2];
        private static readonly Segment[] sourceSegment = new Segment[2];

        static EffectBgmPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.playModeStateChanged += _ => Dispose();
        }

        internal static float Volume
        {
            get { return EditorPrefs.GetFloat(VolumeKey, 0.3f); }
            set
            {
                EditorPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                if (sources == null) return;
                foreach (var s in sources) if (s != null) s.volume = Mathf.Clamp01(value);
            }
        }

        internal static bool Playing { get { return plan != null; } }

        /// <summary>鳴らし始めたもの（演出エディタが付ける目印：曲名と、行・つなぎ目などの番号）。</summary>
        internal static string Owner { get; private set; }
        internal static int Tag { get; private set; }

        internal static bool IsPlaying(string owner, int tag)
        {
            return plan != null && Owner == owner && Tag == tag;
        }

        /// <summary>区間を順に鳴らす。最後まで来たら、repeatRow の行の区間から繰り返す（-1 は繰り返さない）。</summary>
        internal static void Play(List<Segment> segments, int repeatRow, string owner, int tag)
        {
            Stop();
            segments.RemoveAll(s => s.Clip == null || s.Clip.frequency <= 0 || s.From >= s.To);
            if (segments.Count == 0) return;
            EnsureSources();
            plan = segments;
            Owner = owner;
            Tag = tag;
            repeatIndex = repeatRow < 0 ? -1 : segments.FindIndex(s => s.Row == repeatRow);
            nextIndex = 0;
            tailSource = 1;
            double now = AudioSettings.dspTime;
            tailStart = now;
            tailEnd = now + Lead;
            EditorApplication.update += Update;
            Update();
        }

        internal static void Stop()
        {
            plan = null;
            EditorApplication.update -= Update;
            for (int i = 0; i < 2; i++) sourceSegment[i] = default(Segment);
            if (sources == null) return;
            foreach (var s in sources) if (s != null) s.Stop();
        }

        /// <summary>今鳴っている区間と、その中の位置（サンプル数）。</summary>
        internal static bool Current(out Segment segment, out int sample)
        {
            segment = default(Segment);
            sample = 0;
            if (plan == null) return false;
            double now = AudioSettings.dspTime;
            int index = -1;
            for (int i = 0; i < 2; i++)
            {
                if (sourceSegment[i].Clip == null || sourceStart[i] > now) continue;
                if (index < 0 || sourceStart[i] > sourceStart[index]) index = i;
            }
            if (index < 0) return false;
            segment = sourceSegment[index];
            sample = segment.From + (int)((now - sourceStart[index]) * segment.Clip.frequency);
            return sample < segment.To;
        }

        private static void Update()
        {
            if (plan == null) return;
            double now = AudioSettings.dspTime;
            if (nextIndex >= plan.Count)
            {
                if (repeatIndex < 0)
                {
                    if (now >= tailEnd) Stop();
                    return;
                }
                nextIndex = repeatIndex;
            }
            if (now < tailStart || now < tailEnd - Lookahead) return;
            double at = tailEnd < now + Lead ? now + Lead : tailEnd;
            Schedule(plan[nextIndex], at);
            nextIndex++;
        }

        private static void Schedule(Segment segment, double at)
        {
            int index = 1 - tailSource;
            var source = sources[index];
            source.Stop();
            source.clip = segment.Clip;
            source.timeSamples = segment.From;
            source.volume = Volume;
            source.PlayScheduled(at);
            double length = (segment.To - segment.From) / (double)segment.Clip.frequency;
            if (segment.To < segment.Clip.samples) source.SetScheduledEndTime(at + length);
            sourceStart[index] = at;
            sourceSegment[index] = segment;
            tailSource = index;
            tailStart = at;
            tailEnd = at + length;
        }

        private static void EnsureSources()
        {
            if (host != null && sources != null && sources[0] != null && sources[1] != null) return;
            if (host != null) Object.DestroyImmediate(host);
            host = new GameObject("EffectBgmPreview") { hideFlags = HideFlags.HideAndDontSave };
            sources = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.volume = Volume;
                sources[i] = s;
            }
        }

        private static void Dispose()
        {
            Stop();
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            sources = null;
        }
    }
}
