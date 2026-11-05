using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace AX.UdonSlot.REV1_0
{
    
    public enum ClipType : int
    {
        BET, // ベット
        START, // レバーオン
        STOP, // リール停止
        STOP_REACH, // テンパイ
        PAYOUT, // 払い出し
        CREDIT, // クレジット入転送
        WAIT, // 待機音
    }

    public enum SoundType : int
    {
        SE1, // 効果音1
        SE2, // 効果音2
        BET, // ベット音
        VFX1, // 演出音
        VFX2, // 演出音2
        ALWAYS_BGM, // 常に流れるBGM
        WAIT, // 待機音
    }
    /// <summary>
    /// スロットで使用する効果音とBGMをAudioSource種別ごとに再生する。
    /// ネットワーク再生、停止、遅延再生キューも管理する。
    /// </summary>
    public class SoundBoard : UdonSharpBehaviour
    {
        public AudioClip[] clip;
        
        public AudioSource[] source;
        
        /// <summary>
        /// 再生キュー
        /// Sound: 再生種別
        /// Clip: 再生する音
        /// Time: 再生するまでの時間
        /// </summary>
        private SoundType[] queuedSound = new SoundType[0];
        private int[] queuedClip = new int[0];
        private float[] queuedTime = new float[0];
        private bool[] queuedIsOneShot = new bool[0];
        private bool[] queuedIsReplay = new bool[0];
        
        private void Update()
        {
            for (int i = 0; i < queuedSound.Length; i++)
            {
                UpdateQueuedSound(i);
            }
        }

        private void UpdateQueuedSound(int index)
        {
            if (queuedTime[index] <= 0f) return;

            queuedTime[index] -= Time.deltaTime;
            if (queuedTime[index] > 0f) return;

            // ベット音は重ねて鳴らないように、再生前に一度止める。
            if (queuedSound[index] == SoundType.BET) StopSound(queuedSound[index]);
            PlaySound(queuedSound[index], queuedClip[index], queuedIsOneShot[index], queuedIsReplay[index]);
            queuedTime[index] = -9999999f;
        }

        /// <summary>
        /// キューのクリアをやっておく
        /// </summary>
        private void FixedUpdate()
        {
            for (int i = 0; i < queuedSound.Length; i++)
            {
                // 再生済み（時間が大きく負になったもの）をキューから消す
                if (queuedTime[i] <= -9000000) RemoveQueuedSound(i);
            }
        }

        private void RemoveQueuedSound(int index)
        {
            int newLength = queuedSound.Length - 1;
            int tailLength = newLength - index;
            SoundType[] newQueuedSound = new SoundType[newLength];
            int[] newQueuedClip = new int[newLength];
            float[] newQueuedTime = new float[newLength];
            bool[] newQueuedIsOneShot = new bool[newLength];
            bool[] newQueuedIsReplay = new bool[newLength];
            Array.Copy(queuedSound, 0, newQueuedSound, 0, index);
            Array.Copy(queuedClip, 0, newQueuedClip, 0, index);
            Array.Copy(queuedTime, 0, newQueuedTime, 0, index);
            Array.Copy(queuedIsOneShot, 0, newQueuedIsOneShot, 0, index);
            Array.Copy(queuedIsReplay, 0, newQueuedIsReplay, 0, index);
            Array.Copy(queuedSound, index + 1, newQueuedSound, index, tailLength);
            Array.Copy(queuedClip, index + 1, newQueuedClip, index, tailLength);
            Array.Copy(queuedTime, index + 1, newQueuedTime, index, tailLength);
            Array.Copy(queuedIsOneShot, index + 1, newQueuedIsOneShot, index, tailLength);
            Array.Copy(queuedIsReplay, index + 1, newQueuedIsReplay, index, tailLength);
            queuedSound = newQueuedSound;
            queuedClip = newQueuedClip;
            queuedTime = newQueuedTime;
            queuedIsOneShot = newQueuedIsOneShot;
            queuedIsReplay = newQueuedIsReplay;
        }
        
        [NetworkCallable]
        public void RemotePlaySound(SoundType type, int clip, bool isOneShot, bool isReplay)
        {
            AudioSource targetSource = source[(int)type];
            AudioClip targetClip = this.clip[clip];
            if (isOneShot)
            {
                targetSource.PlayOneShot(targetClip);
                return;
            }

            // isReplayなら同じクリップでも頭から再生し直す。
            if (!isReplay && targetSource.clip == targetClip) return;
            targetSource.clip = targetClip;
            targetSource.Play();
        }
        
        [NetworkCallable]
        public void RemoteStopSound(SoundType type)
        {
            source[(int)type].Stop();
        }

        public void PlaySound(SoundType type, int clip)
        {
            PlaySound(type, clip, true, false);
        }

        public void PlaySound(SoundType type, int clip, bool isOneShot, bool isReplay)
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RemotePlaySound), type, clip, isOneShot, isReplay);
        }

        /// <summary>ローカルだけで鳴らす（全員へは送らない）。OneShot固定。クリップが無ければ何もしない。</summary>
        public void PlayLocalSound(SoundType type, int clip)
        {
            if (clip < 0 || clip >= this.clip.Length || this.clip[clip] == null) return;
            RemotePlaySound(type, clip, true, false);
        }

        /// <summary>
        /// 途中参加者（Late Joiner）向け。指定SoundTypeで再生していなければ再生する。ローカル。
        /// この関数はOneShotがfalse固定。
        /// </summary>
        public void TryPlaySound(SoundType type, int clip, bool isReplay)
        {
            if (IsPlayingSound(type)) return;
            source[(int)type].clip = this.clip[clip];
            source[(int)type].Play();
        }
        
        /// <summary>
        /// 途中参加者（Late Joiner）向け。指定SoundTypeで再生しているが、再生しているクリップが違う場合は変更する。ローカル。
        /// </summary>
        public void TryMusicChange(SoundType type, int clip)
        {
            if (!IsPlayingSound(type) || source[(int)type].clip == this.clip[clip]) return;
            source[(int)type].clip = this.clip[clip];
            source[(int)type].Play();
        }
        
        public void StopSound(SoundType type)
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RemoteStopSound), type);
        }

        /// <summary>
        /// 指定SoundTypeで再生しているか
        /// なおOneShotは判定できない
        /// </summary>
        public bool IsPlayingSound(SoundType type)
        {
            return source[(int)type].isPlaying;
        }

        public void ScheduledPlaySound(SoundType type, int clip, float delay)
        {
            ScheduledPlaySound(type, clip, delay, true, false);
        }
        
        public void ScheduledPlaySound(SoundType type, int clip, float delay, bool isOneShot)
        {
            ScheduledPlaySound(type, clip, delay, isOneShot, false);
        }

        public void ScheduledPlaySound(SoundType type, int clip, float delay, bool isOneShot, bool isReplay)
        {
            // キューの末尾に追加
            int length = queuedSound.Length;
            SoundType[] newQueuedSound = new SoundType[length + 1];
            int[] newQueuedClip = new int[length + 1];
            float[] newQueuedTime = new float[length + 1];
            bool[] newQueuedIsOneShot = new bool[length + 1];
            bool[] newQueuedIsReplay = new bool[length + 1];
            Array.Copy(queuedSound, newQueuedSound, length);
            Array.Copy(queuedClip, newQueuedClip, length);
            Array.Copy(queuedTime, newQueuedTime, length);
            Array.Copy(queuedIsOneShot, newQueuedIsOneShot, length);
            Array.Copy(queuedIsReplay, newQueuedIsReplay, length);
            newQueuedSound[length] = type;
            newQueuedClip[length] = clip;
            newQueuedTime[length] = delay;
            newQueuedIsOneShot[length] = isOneShot;
            newQueuedIsReplay[length] = isReplay;
            queuedSound = newQueuedSound;
            queuedClip = newQueuedClip;
            queuedTime = newQueuedTime;
            queuedIsOneShot = newQueuedIsOneShot;
            queuedIsReplay = newQueuedIsReplay;
        }
    }
}
