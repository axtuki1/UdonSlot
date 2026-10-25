
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 演出エディタで作ったパターンを再生する。
    /// パターンは「レバーオン／第一停止／第二停止／第三停止／ゲーム終了」の5列に、アクション（映像・表示・音・リール・コード）を並べたもの。
    /// どのパターンを流すかは機種コードが決め、メイン基板の所有者が CallEffectPattern で呼ぶ。
    /// 呼ぶと全員へ届き、次のステップから新しいパターンに差し替わる。差し替えられない限り、毎ゲーム同じパターンを繰り返す。
    /// データは演出エディタ（UdonSlot/演出エディタ）が書き込む。手で編集しない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EffectPlayer : UdonSharpBehaviour
    {
        public const int StepLeverOn = 0;
        public const int StepFirstStop = 1;
        public const int StepSecondStop = 2;
        public const int StepThirdStop = 3;
        public const int StepGameEnd = 4;
        public const int StepCount = 5;

        /// <summary>セルの分岐。共通・入賞時・非入賞時。</summary>
        public const int BranchCommon = 0;
        public const int BranchWin = 1;
        public const int BranchLose = 2;

        public MainBoard mainBoard;

        // ---------------------------------------------------------------- アクション

        public string[] actionNames = new string[0];
        public int[] actionDelayMs = new int[0];

        public int[] actionAnimStart = new int[0];
        public int[] actionAnimCount = new int[0];
        public Animator[] animTargets = new Animator[0];
        public string[] animTriggers = new string[0];
        /// <summary>パターンのパラメータを渡す Animator の int パラメータ名。空なら渡さない。</summary>
        public string[] animParameters = new string[0];

        public int[] actionActiveStart = new int[0];
        public int[] actionActiveCount = new int[0];
        public GameObject[] activeTargets = new GameObject[0];
        public bool[] activeValues = new bool[0];

        /// <summary>SoundType。-1 は音なし。</summary>
        public int[] actionSoundType = new int[0];
        public int[] actionSoundClip = new int[0];

        public int[] actionCodeStart = new int[0];
        public int[] actionCodeCount = new int[0];
        public UdonSharpBehaviour[] codeTargets = new UdonSharpBehaviour[0];
        public string[] codeMethods = new string[0];

        /// <summary>リールのキー。アクションごとに連続して並ぶ。</summary>
        public int[] actionReelStart = new int[0];
        public int[] actionReelCount = new int[0];
        /// <summary>0=左 1=中 2=右。</summary>
        public int[] reelKeyReel = new int[0];
        public int[] reelKeyTime = new int[0];
        /// <summary>相対なら開始位置から上方向へ何コマ（PlayReelSequenceと同じ）。絶対なら中段に来るコマ番号（下が1）。</summary>
        public float[] reelKeyValue = new float[0];
        public bool[] reelKeyAbsolute = new bool[0];

        // ---------------------------------------------------------------- パターン

        public string[] patternNames = new string[0];
        /// <summary>ゲーム終了時に差し替わる次のパターン。-1 はなし（同じパターンを繰り返す）。</summary>
        public int[] patternNext = new int[0];
        public int[] patternLayerStart = new int[0];
        public int[] patternLayerCount = new int[0];
        public string[] layerNames = new string[0];

        /// <summary>セルに置いたアクション。1件 = (パターン, レイヤー, 列, 分岐, アクション)。</summary>
        public int[] entryPattern = new int[0];
        public int[] entryLayer = new int[0];
        public int[] entryStep = new int[0];
        public int[] entryBranch = new int[0];
        public int[] entryAction = new int[0];

        // ---------------------------------------------------------------- 再生状態

        /// <summary>今のパターン。途中参加者向けに同期する。</summary>
        [UdonSynced] private int syncedPattern = -1;
        [UdonSynced] private int syncedParameter = 0;

        private int currentPattern = -1;
        private int currentParameter = 0;
        private int pendingPattern = -1;
        private int pendingParameter = 0;
        private int pendingFromStep = -1;
        /// <summary>最後に処理したステップ。ゲームの外（終了〜次のレバー）は StepGameEnd。</summary>
        private int lastStep = StepGameEnd;
        private bool lastWon = false;

        private int[] queueAction = new int[0];
        private float[] queueTime = new float[0];

        public void Start()
        {
            currentPattern = syncedPattern;
            currentParameter = syncedParameter;
        }

        public override void OnDeserialization()
        {
            // 途中参加者：次のレバーから今のパターンで再生する。
            if (currentPattern < 0 && pendingPattern < 0)
            {
                currentPattern = syncedPattern;
                currentParameter = syncedParameter;
            }
        }

        /// <summary>パターンを名前で呼ぶ。パラメータは 0。</summary>
        public void CallEffectPattern(string patternName)
        {
            CallEffectPatternWithParameter(patternName, 0);
        }

        /// <summary>
        /// パターンを名前で呼ぶ。メイン基板の所有者だけが呼べる（ほかの人が呼んでも何もしない）。
        /// 全員へ届き、次のステップから差し替わる。ゲームの外で呼んだら次のレバーオンから。
        /// parameter は Animator の int パラメータとして各アクションへ渡す（キャラ違いなどの差し替え用）。
        /// </summary>
        public void CallEffectPatternWithParameter(string patternName, int parameter)
        {
            if (!IsMainBoardOwner()) return;
            int pattern = FindPattern(patternName);
            if (pattern < 0)
            {
                Debug.LogWarning("[EffectPlayer] パターンがありません: " + patternName);
                return;
            }
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            syncedPattern = pattern;
            syncedParameter = parameter;
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePattern), pattern, parameter, NextStep(lastStep));
        }

        /// <summary>パターンの番号。ないときは -1。</summary>
        public int FindPattern(string patternName)
        {
            for (int i = 0; i < patternNames.Length; i++) if (patternNames[i] == patternName) return i;
            return -1;
        }

        /// <summary>今のパターンの名前。ないときは空。</summary>
        public string GetCurrentPatternName()
        {
            return currentPattern >= 0 && currentPattern < patternNames.Length ? patternNames[currentPattern] : "";
        }

        [NetworkCallable]
        public void ReceivePattern(int pattern, int parameter, int fromStep)
        {
            if (pattern < 0 || pattern >= patternNames.Length) return;
            if (HasPassed(fromStep))
            {
                // 呼び出しより先にそのステップが届いていた：差し替えて、今のステップの列を1回だけ流して追いつく。
                currentPattern = pattern;
                currentParameter = parameter;
                pendingPattern = -1;
                if (lastStep != StepGameEnd || fromStep == StepGameEnd) PlayStep(lastStep, lastWon);
                return;
            }
            pendingPattern = pattern;
            pendingParameter = parameter;
            pendingFromStep = fromStep;
        }

        /// <summary>ステップが来たことを伝える。SubBoard が全員の画面で呼ぶ。won は第三停止・ゲーム終了で入賞したか。</summary>
        public void OnStep(int step, bool won)
        {
            if (step < 0 || step >= StepCount) return;
            if (pendingPattern >= 0 && pendingFromStep == step)
            {
                currentPattern = pendingPattern;
                currentParameter = pendingParameter;
                pendingPattern = -1;
            }
            lastStep = step;
            lastWon = won;
            PlayStep(step, won);
            if (step == StepGameEnd && pendingPattern < 0 && currentPattern >= 0 && currentPattern < patternNext.Length)
            {
                int next = patternNext[currentPattern];
                if (next >= 0 && next < patternNames.Length) currentPattern = next;
            }
        }

        private bool HasPassed(int step)
        {
            // ゲームの外で「次のレバーから」の呼び出しは、まだ来ていない。
            if (step == StepLeverOn && lastStep == StepGameEnd) return false;
            return lastStep >= step;
        }

        private static int NextStep(int step)
        {
            return step >= StepGameEnd ? StepLeverOn : step + 1;
        }

        private void PlayStep(int step, bool won)
        {
            if (currentPattern < 0) return;
            for (int i = 0; i < entryAction.Length; i++)
            {
                if (entryPattern[i] != currentPattern || entryStep[i] != step) continue;
                int branch = entryBranch[i];
                if (branch == BranchWin && !won) continue;
                if (branch == BranchLose && won) continue;
                StartAction(entryAction[i], step);
            }
        }

        private void StartAction(int action, int step)
        {
            if (action < 0 || action >= actionNames.Length) return;
            int delay = actionDelayMs[action];
            if (delay <= 0)
            {
                RunAction(action, step);
                return;
            }
            int n = queueAction.Length;
            int[] actions = new int[n + 1];
            float[] times = new float[n + 1];
            System.Array.Copy(queueAction, actions, n);
            System.Array.Copy(queueTime, times, n);
            actions[n] = action;
            times[n] = Time.time + delay / 1000f;
            queueAction = actions;
            queueTime = times;
        }

        public void Update()
        {
            if (queueAction.Length == 0) return;
            float now = Time.time;
            int kept = 0;
            for (int i = 0; i < queueAction.Length; i++)
            {
                if (queueTime[i] <= now) RunAction(queueAction[i], lastStep);
                else kept++;
            }
            if (kept == queueAction.Length) return;
            int[] actions = new int[kept];
            float[] times = new float[kept];
            int k = 0;
            for (int i = 0; i < queueAction.Length; i++)
            {
                if (queueTime[i] <= now) continue;
                actions[k] = queueAction[i];
                times[k] = queueTime[i];
                k++;
            }
            queueAction = actions;
            queueTime = times;
        }

        private void RunAction(int action, int step)
        {
            int start = actionAnimStart[action], end = start + actionAnimCount[action];
            for (int i = start; i < end; i++)
            {
                Animator animator = animTargets[i];
                if (!Utilities.IsValid(animator)) continue;
                if (animParameters[i].Length > 0) animator.SetInteger(animParameters[i], currentParameter);
                if (animTriggers[i].Length > 0) animator.SetTrigger(animTriggers[i]);
            }

            start = actionActiveStart[action];
            end = start + actionActiveCount[action];
            for (int i = start; i < end; i++)
            {
                if (Utilities.IsValid(activeTargets[i])) activeTargets[i].SetActive(activeValues[i]);
            }

            // 効果音は各自の画面で鳴らす（SoundBoard の全員送信は使わない）。
            int soundType = actionSoundType[action];
            if (soundType >= 0 && Utilities.IsValid(mainBoard) && Utilities.IsValid(mainBoard.subBoard)
                && Utilities.IsValid(mainBoard.subBoard.soundBoard))
                mainBoard.subBoard.soundBoard.RemotePlaySound((SoundType)soundType, actionSoundClip[action], true, false);

            if (actionReelCount[action] > 0) RunReel(action, step);

            start = actionCodeStart[action];
            end = start + actionCodeCount[action];
            for (int i = start; i < end; i++)
            {
                if (Utilities.IsValid(codeTargets[i]) && codeMethods[i].Length > 0) codeTargets[i].SendCustomEvent(codeMethods[i]);
            }
        }

        /// <summary>
        /// リールのキーを区間に直して、メイン基板の所有者が全員へ配信する（ほかの人は配信を受けて動く）。
        /// 絶対指定のキーは、所有者の今のリール位置からのコマ数に直す。
        /// </summary>
        private void RunReel(int action, int step)
        {
            if (!IsMainBoardOwner()) return;
            int start = actionReelStart[action], count = actionReelCount[action];
            int symbolCount = Mathf.Max(1, mainBoard.pictureCount);

            // 全リールのキーの時刻をまとめて区間の境目にする。
            int[] times = new int[count + 1];
            int timeCount = 0;
            times[timeCount++] = 0;
            for (int i = start; i < start + count; i++)
            {
                int t = Mathf.Max(0, reelKeyTime[i]);
                bool exists = false;
                for (int j = 0; j < timeCount; j++) if (times[j] == t) { exists = true; break; }
                if (!exists) times[timeCount++] = t;
            }
            // 挿入ソート
            for (int i = 1; i < timeCount; i++)
            {
                int v = times[i], j = i - 1;
                while (j >= 0 && times[j] > v) { times[j + 1] = times[j]; j--; }
                times[j + 1] = v;
            }

            int sections = timeCount;
            int[] durations = new int[sections];
            float[] left = new float[sections];
            float[] center = new float[sections];
            float[] right = new float[sections];
            int previous = 0;
            for (int s = 0; s < sections; s++)
            {
                durations[s] = times[s] - previous;
                previous = times[s];
                left[s] = ValueAt(start, count, 0, times[s], symbolCount);
                center[s] = ValueAt(start, count, 1, times[s], symbolCount);
                right[s] = ValueAt(start, count, 2, times[s], symbolCount);
            }
            mainBoard.PlayReelEffect(durations, left, center, right, step == StepLeverOn);
        }

        /// <summary>リール reel の時刻 t での位置（開始位置から上方向へのコマ数）。キーの間は等速。</summary>
        private float ValueAt(int start, int count, int reel, int t, int symbolCount)
        {
            int beforeTime = int.MinValue, afterTime = int.MaxValue;
            float beforeValue = 0f, afterValue = 0f;
            bool hasBefore = false, hasAfter = false;
            for (int i = start; i < start + count; i++)
            {
                if (reelKeyReel[i] != reel) continue;
                int time = reelKeyTime[i];
                float value = RelativeValue(i, reel, symbolCount);
                if (time <= t && time >= beforeTime) { beforeTime = time; beforeValue = value; hasBefore = true; }
                if (time >= t && time < afterTime) { afterTime = time; afterValue = value; hasAfter = true; }
            }
            if (!hasBefore && !hasAfter) return 0f;
            if (!hasBefore) return afterValue;
            if (!hasAfter || afterTime == beforeTime) return beforeValue;
            return beforeValue + (afterValue - beforeValue) * (t - beforeTime) / (float)(afterTime - beforeTime);
        }

        private float RelativeValue(int key, int reel, int symbolCount)
        {
            if (!reelKeyAbsolute[key]) return reelKeyValue[key];
            // 絶対：中段に来るコマ番号（下が1）。上へ1コマ動くと中段のコマ番号は1つ下がる。
            int current = mainBoard.GetReelCenterFrame(reel);
            float delta = current - reelKeyValue[key];
            float half = symbolCount / 2f;
            while (delta > half) delta -= symbolCount;
            while (delta < -half) delta += symbolCount;
            return delta;
        }

        private bool IsMainBoardOwner()
        {
            return Utilities.IsValid(mainBoard) && Networking.IsOwner(mainBoard.gameObject);
        }
    }
}
