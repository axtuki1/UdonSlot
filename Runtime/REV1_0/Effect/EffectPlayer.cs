
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
    /// 呼ぶと全員へ届き、次のステップから新しいパターンに差し替わる。チャンネルごとに別のパターンを重ねて流せる。
    /// 常駐チャンネル（residentChannelCount より前）は差し替えられない限り毎ゲーム繰り返し、それ以降はそのゲームで止まる。
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

        /// <summary>Animator への書き込みの種類。</summary>
        public const int AnimTrigger = 0;
        public const int AnimBool = 1;
        public const int AnimInt = 2;
        public const int AnimFloat = 3;
        public const int AnimIntFromCall = 4;

        public MainBoard mainBoard;

        // ---------------------------------------------------------------- アクション

        public string[] actionNames = new string[0];
        public int[] actionDelayMs = new int[0];
        /// <summary>操作（停止ボタン・レバーなど）を受け付けない時間。セルのタイミングから数える（再生開始遅延とは同時に始まる）。リールは回ったまま（フリーズ）。</summary>
        public int[] actionLockMs = new int[0];

        public int[] actionAnimStart = new int[0];
        public int[] actionAnimCount = new int[0];
        public Animator[] animTargets = new Animator[0];
        /// <summary>Animator のパラメータ名。</summary>
        public string[] animNames = new string[0];
        /// <summary>0=トリガー 1=Bool 2=Int 3=Float 4=Int（呼び出し時の値を入れる）。</summary>
        public int[] animKinds = new int[0];
        public float[] animValues = new float[0];

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
        /// <summary>相対なら前のキーから順回転の向きに何コマ（−は逆回転）。絶対なら中段に来るコマ番号（下が1）。</summary>
        public float[] reelKeyValue = new float[0];
        public bool[] reelKeyAbsolute = new bool[0];
        /// <summary>絶対キーの回り方。0=最短 1=順回転（図柄が下へ流れる） 2=逆回転。前のキーの位置から数える。</summary>
        public int[] reelKeyDirection = new int[0];
        /// <summary>絶対キーで、そのコマに着くまでに余分に回る周回数（順回転・逆回転のとき）。</summary>
        public int[] reelKeyTurns = new int[0];

        public const int ReelShortest = 0;
        public const int ReelForward = 1;
        public const int ReelBackward = 2;

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

        /// <summary>チャンネルの数。チャンネルごとに今のパターンを1つ持ち、同時に重ねて流す。</summary>
        public const int ChannelCount = 16;

        /// <summary>
        /// この番号より前のチャンネルは常駐（差し替えるまで毎ゲーム繰り返す）。
        /// これ以降のチャンネルは、そのゲームで流し終えたら止まる（押し順ナビ、ペナルティ画面など）。
        /// </summary>
        public int residentChannelCount = 6;

        /// <summary>チャンネルごとの今のパターン＋1（0 はなし）。途中参加者向けに同期する。</summary>
        [UdonSynced] private int[] syncedPatterns = new int[ChannelCount];
        [UdonSynced] private int[] syncedParameters = new int[ChannelCount];

        private int[] currentPattern = new int[ChannelCount];
        private int[] currentParameter = new int[ChannelCount];
        private bool[] hasPending = new bool[ChannelCount];
        /// <summary>-1 は止める。</summary>
        private int[] pendingPattern = new int[ChannelCount];
        private int[] pendingParameter = new int[ChannelCount];
        private int[] pendingFromStep = new int[ChannelCount];
        /// <summary>最後に処理したステップ。ゲームの外（終了〜次のレバー）は StepGameEnd。</summary>
        private int lastStep = StepGameEnd;
        private bool lastWon = false;
        private bool joined = false;

        private int[] queueAction = new int[0];
        private int[] queueParameter = new int[0];
        private float[] queueTime = new float[0];

        private bool[] received = new bool[ChannelCount];

        public void Start()
        {
            if (joined) return;
            for (int c = 0; c < ChannelCount; c++) currentPattern[c] = -1;
        }

        public override void OnDeserialization()
        {
            ApplySynced();
        }

        /// <summary>途中参加者：常駐チャンネルだけ、次のレバーから今のパターンで再生する。</summary>
        private void ApplySynced()
        {
            if (joined || Networking.IsOwner(gameObject)) return;
            if (syncedPatterns == null || syncedPatterns.Length != ChannelCount) return;
            joined = true;
            for (int c = 0; c < ChannelCount; c++) if (!received[c]) currentPattern[c] = -1;
            for (int c = 0; c < residentChannelCount && c < ChannelCount; c++)
            {
                if (received[c]) continue;
                currentPattern[c] = syncedPatterns[c] - 1;
                currentParameter[c] = syncedParameters[c];
            }
        }

        /// <summary>チャンネル0（通常）のパターンを名前で呼ぶ。パラメータは 0。</summary>
        public void CallEffectPattern(string patternName)
        {
            CallEffectPatternOnChannel(0, patternName, 0);
        }

        /// <summary>チャンネル0（通常）のパターンを、パラメータ付きで呼ぶ。</summary>
        public void CallEffectPatternWithParameter(string patternName, int parameter)
        {
            CallEffectPatternOnChannel(0, patternName, parameter);
        }

        /// <summary>
        /// チャンネルを指定してパターンを呼ぶ。メイン基板の所有者だけが呼べる（ほかの人が呼んでも何もしない）。
        /// 全員へ届き、次のステップから差し替わる。ゲームの外で呼んだら次のレバーオンから。
        /// parameter は Animator の int パラメータとして各アクションへ渡す（キャラ違いなどの差し替え用）。
        /// </summary>
        public void CallEffectPatternOnChannel(int channel, string patternName, int parameter)
        {
            if (!IsMainBoardOwner() || channel < 0 || channel >= ChannelCount) return;
            int pattern = FindPattern(patternName);
            if (pattern < 0)
            {
                Debug.LogWarning("[EffectPlayer] パターンがありません: " + patternName);
                return;
            }
            Send(channel, pattern, parameter);
        }

        /// <summary>チャンネルのパターンを、次のステップから止める。</summary>
        public void StopEffectPattern(int channel)
        {
            if (!IsMainBoardOwner() || channel < 0 || channel >= ChannelCount) return;
            Send(channel, -1, 0);
        }

        private void Send(int channel, int pattern, int parameter)
        {
            if (channel < residentChannelCount)
            {
                if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
                syncedPatterns[channel] = pattern + 1;
                syncedParameters[channel] = parameter;
                RequestSerialization();
            }
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePattern), channel, pattern, parameter, NextStep(lastStep));
        }

        /// <summary>パターンの番号。ないときは -1。</summary>
        public int FindPattern(string patternName)
        {
            for (int i = 0; i < patternNames.Length; i++) if (patternNames[i] == patternName) return i;
            return -1;
        }

        /// <summary>チャンネル0の今のパターンの名前。ないときは空。</summary>
        public string GetCurrentPatternName()
        {
            return GetChannelPatternName(0);
        }

        /// <summary>チャンネルの今のパターンの名前。ないときは空。</summary>
        public string GetChannelPatternName(int channel)
        {
            if (channel < 0 || channel >= ChannelCount) return "";
            int pattern = currentPattern[channel];
            return pattern >= 0 && pattern < patternNames.Length ? patternNames[pattern] : "";
        }

        [NetworkCallable]
        public void ReceivePattern(int channel, int pattern, int parameter, int fromStep)
        {
            if (channel < 0 || channel >= ChannelCount || pattern >= patternNames.Length) return;
            received[channel] = true;
            if (HasPassed(fromStep))
            {
                // 呼び出しより先にそのステップが届いていた：差し替えて、今のステップの列を1回だけ流して追いつく。
                currentPattern[channel] = pattern;
                currentParameter[channel] = parameter;
                hasPending[channel] = false;
                if (lastStep != StepGameEnd || fromStep == StepGameEnd) PlayStep(channel, lastStep, lastWon);
                if (lastStep == StepGameEnd) EndGame(channel);
                return;
            }
            hasPending[channel] = true;
            pendingPattern[channel] = pattern;
            pendingParameter[channel] = parameter;
            pendingFromStep[channel] = fromStep;
        }

        /// <summary>ステップが来たことを伝える。SubBoard が全員の画面で呼ぶ。won は第三停止・ゲーム終了で入賞したか。</summary>
        public void OnStep(int step, bool won)
        {
            if (step < 0 || step >= StepCount) return;
            lastStep = step;
            lastWon = won;
            for (int c = 0; c < ChannelCount; c++)
            {
                if (hasPending[c] && pendingFromStep[c] == step)
                {
                    currentPattern[c] = pendingPattern[c];
                    currentParameter[c] = pendingParameter[c];
                    hasPending[c] = false;
                }
                PlayStep(c, step, won);
                if (step == StepGameEnd) EndGame(c);
            }
        }

        /// <summary>ゲーム終了の後：次のパターンへ進む。常駐でないチャンネルは、次がなければ止まる。</summary>
        private void EndGame(int channel)
        {
            int pattern = currentPattern[channel];
            if (hasPending[channel] || pattern < 0) return;
            int next = pattern < patternNext.Length ? patternNext[pattern] : -1;
            if (next >= 0 && next < patternNames.Length) currentPattern[channel] = next;
            else if (channel >= residentChannelCount) currentPattern[channel] = -1;
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

        private void PlayStep(int channel, int step, bool won)
        {
            int pattern = currentPattern[channel];
            if (pattern < 0) return;
            for (int i = 0; i < entryAction.Length; i++)
            {
                if (entryPattern[i] != pattern || entryStep[i] != step) continue;
                int branch = entryBranch[i];
                if (branch == BranchWin && !won) continue;
                if (branch == BranchLose && won) continue;
                StartAction(entryAction[i], step, currentParameter[channel]);
            }
        }

        private void StartAction(int action, int step, int parameter)
        {
            if (action < 0 || action >= actionNames.Length) return;
            int delay = actionDelayMs[action];
            // 操作禁止は再生開始遅延と関係なく、セルのタイミングから数える。
            if (action < actionLockMs.Length && actionLockMs[action] > 0 && IsMainBoardOwner())
                mainBoard.OffControllableAtLeast(actionLockMs[action] / 1000f);
            if (delay <= 0)
            {
                RunAction(action, step, parameter);
                return;
            }
            int n = queueAction.Length;
            int[] actions = new int[n + 1];
            int[] parameters = new int[n + 1];
            float[] times = new float[n + 1];
            System.Array.Copy(queueAction, actions, n);
            System.Array.Copy(queueParameter, parameters, n);
            System.Array.Copy(queueTime, times, n);
            actions[n] = action;
            parameters[n] = parameter;
            times[n] = Time.time + delay / 1000f;
            queueAction = actions;
            queueParameter = parameters;
            queueTime = times;
        }

        public void Update()
        {
            if (queueAction.Length == 0) return;
            float now = Time.time;
            int kept = 0;
            for (int i = 0; i < queueAction.Length; i++)
            {
                if (queueTime[i] <= now) RunAction(queueAction[i], lastStep, queueParameter[i]);
                else kept++;
            }
            if (kept == queueAction.Length) return;
            int[] actions = new int[kept];
            int[] parameters = new int[kept];
            float[] times = new float[kept];
            int k = 0;
            for (int i = 0; i < queueAction.Length; i++)
            {
                if (queueTime[i] <= now) continue;
                actions[k] = queueAction[i];
                parameters[k] = queueParameter[i];
                times[k] = queueTime[i];
                k++;
            }
            queueAction = actions;
            queueParameter = parameters;
            queueTime = times;
        }

        private void RunAction(int action, int step, int parameter)
        {
            int start = actionAnimStart[action], end = start + actionAnimCount[action];
            for (int i = start; i < end; i++)
            {
                Animator animator = animTargets[i];
                if (!Utilities.IsValid(animator)) continue;
                string name = animNames[i];
                if (name.Length == 0) continue;
                switch (animKinds[i])
                {
                    case AnimTrigger: animator.SetTrigger(name); break;
                    case AnimBool: animator.SetBool(name, animValues[i] != 0f); break;
                    case AnimInt: animator.SetInteger(name, Mathf.RoundToInt(animValues[i])); break;
                    case AnimFloat: animator.SetFloat(name, animValues[i]); break;
                    case AnimIntFromCall: animator.SetInteger(name, parameter); break;
                }
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

            float[] resolved = ResolveReelKeys(start, count, symbolCount);

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
                left[s] = ValueAt(start, count, 0, times[s], resolved);
                center[s] = ValueAt(start, count, 1, times[s], resolved);
                right[s] = ValueAt(start, count, 2, times[s], resolved);
            }
            mainBoard.PlayReelEffect(durations, left, center, right, step == StepLeverOn);
        }

        /// <summary>リール reel の時刻 t での位置（開始位置から上方向へのコマ数）。0ms は開始位置で、キーの間は等速。</summary>
        private float ValueAt(int start, int count, int reel, int t, float[] resolved)
        {
            // 0ms は開始位置（移動 0）。0ms にキーがあればそちらを使う。
            int beforeTime = 0, afterTime = int.MaxValue;
            float beforeValue = 0f, afterValue = 0f;
            bool hasBefore = true, hasAfter = false;
            for (int i = start; i < start + count; i++)
            {
                if (reelKeyReel[i] != reel) continue;
                int time = reelKeyTime[i];
                float value = resolved[i - start];
                if (time <= t && time >= beforeTime) { beforeTime = time; beforeValue = value; hasBefore = true; }
                if (time >= t && time < afterTime) { afterTime = time; afterValue = value; hasAfter = true; }
            }
            if (!hasBefore && !hasAfter) return 0f;
            if (!hasBefore) return afterValue;
            if (!hasAfter || afterTime == beforeTime) return beforeValue;
            return beforeValue + (afterValue - beforeValue) * (t - beforeTime) / (float)(afterTime - beforeTime);
        }

        /// <summary>
        /// 各キーを「開始位置から上方向へのコマ数」（PlayReelSequence の値）に直す。キーは時間順に見ていき、
        /// 相対キーは前のキーから順回転の向きに値の分（−は逆回転）、
        /// 絶対キーは前のキーの位置から、向き（最短／順回転／逆回転）に従ってそのコマまでの移動にする。
        /// </summary>
        private float[] ResolveReelKeys(int start, int count, int symbolCount)
        {
            float[] resolved = new float[count];
            for (int reel = 0; reel < 3; reel++)
            {
                int startFrame = mainBoard.GetReelCenterFrame(reel);
                float previous = 0f;
                int lastTime = -1, lastIndex = -1;
                while (true)
                {
                    // まだ見ていないキーのうち、時間（同じなら並び順）が一番早いもの
                    int next = -1;
                    for (int i = start; i < start + count; i++)
                    {
                        if (reelKeyReel[i] != reel) continue;
                        int time = reelKeyTime[i];
                        if (time < lastTime || (time == lastTime && i <= lastIndex)) continue;
                        if (next < 0 || time < reelKeyTime[next] || (time == reelKeyTime[next] && i < next)) next = i;
                    }
                    if (next < 0) break;
                    float value = reelKeyAbsolute[next]
                        ? ResolveAbsolute(previous, startFrame, reelKeyValue[next], reelKeyDirection.Length > next ? reelKeyDirection[next] : ReelShortest,
                            reelKeyTurns.Length > next ? reelKeyTurns[next] : 0, symbolCount)
                        : previous - reelKeyValue[next];
                    resolved[next - start] = value;
                    previous = value;
                    lastTime = reelKeyTime[next];
                    lastIndex = next;
                }
            }
            return resolved;
        }

        /// <summary>中段に frame が来る位置のうち、previous から direction の向きで一番近いもの。上へ1コマ動くと中段のコマ番号は1つ下がる。</summary>
        private float ResolveAbsolute(float previous, int startFrame, float frame, int direction, int turns, int symbolCount)
        {
            float delta = (startFrame - frame) - previous;
            float n = symbolCount;
            delta = delta - Mathf.Floor(delta / n) * n; // 0〜n
            if (direction == ReelForward) { if (delta > 0f) delta -= n; delta -= turns * n; }
            else if (direction == ReelBackward) delta += turns * n;
            else if (delta > n / 2f) delta -= n;
            return previous + delta;
        }

        private bool IsMainBoardOwner()
        {
            return Utilities.IsValid(mainBoard) && Networking.IsOwner(mainBoard.gameObject);
        }
    }
}
