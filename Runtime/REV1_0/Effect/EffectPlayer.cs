
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
    /// チャンネル0（CallEffectPattern）はそのゲームだけ。常駐チャンネル（1〜residentChannelCount）は差し替えられない限り毎ゲーム繰り返し、それより後はそのゲームで止まる。
    /// データは演出エディタ（UdonSlot/演出エディタ）が書き込む。手で編集しない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EffectPlayer : UdonSharpBehaviour
    {
        public const int StepLeverOn = 0;
        public const int StepReelStart = 1;
        public const int StepFirstStop = 2;
        public const int StepSecondStop = 3;
        public const int StepThirdStop = 4;
        public const int StepGameEnd = 5;
        public const int StepCount = 6;
        /// <summary>今の列の並び。1 未満は「リール回転開始」の列が無かったころのデータ。</summary>
        public const int CurrentStepVersion = 1;

        /// <summary>セルの分岐。共通・入賞時・非入賞時・押したリール（左・中・右）。</summary>
        public const int BranchCommon = 0;
        public const int BranchWin = 1;
        public const int BranchLose = 2;
        public const int BranchLeft = 3;
        public const int BranchCenter = 4;
        public const int BranchRight = 5;

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

        /// <summary>コンポーネントの有効／無効。1件ごとに Behaviour・Renderer・Collider のどれか1つが入る。</summary>
        public int[] actionEnableStart = new int[0];
        public int[] actionEnableCount = new int[0];
        public Behaviour[] enableBehaviours = new Behaviour[0];
        public Renderer[] enableRenderers = new Renderer[0];
        public Collider[] enableColliders = new Collider[0];
        public bool[] enableValues = new bool[0];

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
        /// <summary>前のキーからこのキーまでの動き方。0=直線 1=加速 2=減速 3=加減速。</summary>
        public int[] reelKeyEase = new int[0];

        public const int ReelShortest = 0;
        public const int ReelForward = 1;
        public const int ReelBackward = 2;

        public const int EaseLinear = 0;
        public const int EaseIn = 1;
        public const int EaseOut = 2;
        public const int EaseInOut = 3;

        // ---------------------------------------------------------------- パターン

        /// <summary>エディタに出す名前。</summary>
        public string[] patternNames = new string[0];
        /// <summary>コードから呼ぶときの名前（英数字）。</summary>
        public string[] patternIds = new string[0];
        /// <summary>ゲーム終了時に差し替わる次のパターン。-1 はなし（同じパターンを繰り返す）。</summary>
        public int[] patternNext = new int[0];
        /// <summary>true のパターンは、チャンネルから外れても表示（オブジェクトの ON/OFF）を戻さない。既定は戻す。</summary>
        public bool[] patternKeepActive = new bool[0];
        /// <summary>パターン名の定数ファイルの書き出し先（エディタ用）。</summary>
        public string constantsPath = "";
        public int[] patternLayerStart = new int[0];
        public int[] patternLayerCount = new int[0];
        public string[] layerNames = new string[0];

        /// <summary>セルに置いたアクション。1件 = (パターン, レイヤー, 列, 分岐, アクション)。</summary>
        public int[] entryPattern = new int[0];
        public int[] entryLayer = new int[0];
        public int[] entryStep = new int[0];
        /// <summary>entryStep の列の並びの版。古いデータは Start で読み替える。</summary>
        public int stepVersion = 0;
        public int[] entryBranch = new int[0];
        public int[] entryAction = new int[0];

        // ---------------------------------------------------------------- 再生状態

        /// <summary>チャンネルの数。チャンネルごとに今のパターンを1つ持ち、同時に重ねて流す。</summary>
        public const int ChannelCount = 16;

        /// <summary>
        /// チャンネル1からこの数だけが常駐（差し替えるまで毎ゲーム繰り返す）。
        /// チャンネル0とそれより後のチャンネルは、そのゲームで流し終えたら止まる（押し順ナビ、ペナルティ画面など）。
        /// </summary>
        public int residentChannelCount = 6;

        /// <summary>常駐チャンネル（1〜residentChannelCount）か。</summary>
        public bool IsResidentChannel(int channel)
        {
            return channel >= 1 && channel <= residentChannelCount;
        }

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
        /// <summary>最後の停止で押したリール。停止以外のステップでは -1。</summary>
        private int lastReel = -1;
        private bool joined = false;

        private int[] queueAction = new int[0];
        private int[] queueParameter = new int[0];
        private float[] queueTime = new float[0];
        /// <summary>遅延待ちのアクションを流したチャンネル。パターンが外れたら取り消す。</summary>
        private int[] queueChannel = new int[0];

        private bool[] received = new bool[ChannelCount];
        /// <summary>activeTargets ごとの、ワールド開始時の ON/OFF。</summary>
        private bool[] activeInitial = new bool[0];
        /// <summary>enable の各件の、ワールド開始時の有効／無効。</summary>
        private bool[] enableInitial = new bool[0];
        private bool initialCaptured = false;

        public void Start()
        {
            MigrateSteps();
            CaptureInitial();
            if (joined) return;
            for (int c = 0; c < ChannelCount; c++) currentPattern[c] = -1;
        }

        /// <summary>「リール回転開始」の列が無かったころのデータは、レバーオンより後の列を1つずらす。</summary>
        private void MigrateSteps()
        {
            if (stepVersion >= CurrentStepVersion) return;
            for (int i = 0; i < entryStep.Length; i++) if (entryStep[i] >= StepReelStart) entryStep[i]++;
            stepVersion = CurrentStepVersion;
        }

        private void CaptureInitial()
        {
            if (initialCaptured) return;
            initialCaptured = true;
            activeInitial = new bool[activeTargets.Length];
            for (int i = 0; i < activeTargets.Length; i++)
                activeInitial[i] = Utilities.IsValid(activeTargets[i]) && activeTargets[i].activeSelf;
            enableInitial = new bool[enableValues.Length];
            for (int i = 0; i < enableValues.Length; i++) enableInitial[i] = GetEnabled(i);
        }

        private bool GetEnabled(int i)
        {
            if (i < enableBehaviours.Length && Utilities.IsValid(enableBehaviours[i])) return enableBehaviours[i].enabled;
            if (i < enableRenderers.Length && Utilities.IsValid(enableRenderers[i])) return enableRenderers[i].enabled;
            if (i < enableColliders.Length && Utilities.IsValid(enableColliders[i])) return enableColliders[i].enabled;
            return false;
        }

        private void SetEnabled(int i, bool value)
        {
            if (i < enableBehaviours.Length && Utilities.IsValid(enableBehaviours[i])) enableBehaviours[i].enabled = value;
            else if (i < enableRenderers.Length && Utilities.IsValid(enableRenderers[i])) enableRenderers[i].enabled = value;
            else if (i < enableColliders.Length && Utilities.IsValid(enableColliders[i])) enableColliders[i].enabled = value;
        }

        /// <summary>チャンネルのパターンを差し替える。外れたパターンが触ったオブジェクトは開始時の ON/OFF に戻す。</summary>
        private void SetChannelPattern(int channel, int pattern)
        {
            int old = currentPattern[channel];
            currentPattern[channel] = pattern;
            if (old < 0 || old == pattern || old >= patternNames.Length) return;
            if (old < patternKeepActive.Length && patternKeepActive[old]) return;
            // 戻したあとで点かないよう、外れたパターンのまだ始まっていないアクションは流さない。
            for (int i = 0; i < queueChannel.Length; i++) if (queueChannel[i] == channel) queueAction[i] = -1;
            ResetPatternActives(old);
        }

        private void ResetPatternActives(int pattern)
        {
            CaptureInitial();
            for (int e = 0; e < entryAction.Length; e++)
            {
                if (entryPattern[e] != pattern) continue;
                int action = entryAction[e];
                if (action < 0 || action >= actionNames.Length) continue;
                int start = actionActiveStart[action], end = start + actionActiveCount[action];
                for (int i = start; i < end && i < activeTargets.Length; i++)
                {
                    if (Utilities.IsValid(activeTargets[i]) && i < activeInitial.Length) activeTargets[i].SetActive(activeInitial[i]);
                }
                if (action >= actionEnableStart.Length) continue;
                start = actionEnableStart[action];
                end = start + actionEnableCount[action];
                for (int i = start; i < end && i < enableInitial.Length; i++) SetEnabled(i, enableInitial[i]);
            }
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
            for (int c = 0; c < ChannelCount; c++)
            {
                if (received[c] || !IsResidentChannel(c)) continue;
                currentPattern[c] = syncedPatterns[c] - 1;
                currentParameter[c] = syncedParameters[c];
            }
        }

        /// <summary>チャンネル0のパターンを内部名で呼ぶ（そのゲームだけ）。パラメータは 0。null や空なら止める。</summary>
        public void CallEffectPattern(string patternName)
        {
            if (patternName == null || patternName.Length == 0) StopEffectPattern(0);
            else CallEffectPatternOnChannel(0, patternName, 0);
        }

        /// <summary>チャンネル0のパターンを、パラメータ付きで呼ぶ（そのゲームだけ）。</summary>
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
            if (IsResidentChannel(channel))
            {
                if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
                syncedPatterns[channel] = pattern + 1;
                syncedParameters[channel] = parameter;
                RequestSerialization();
            }
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePattern), channel, pattern, parameter, NextStep(lastStep));
        }

        /// <summary>内部名からパターンの番号を探す。ないときは -1。</summary>
        public int FindPattern(string patternName)
        {
            for (int i = 0; i < patternIds.Length; i++) if (patternIds[i] == patternName) return i;
            // 内部名をまだ持たない古いデータ（演出エディタで開けば付く）
            for (int i = 0; i < patternNames.Length; i++)
                if ((i >= patternIds.Length || patternIds[i].Length == 0) && patternNames[i] == patternName) return i;
            return -1;
        }

        /// <summary>チャンネル0の今のパターンの内部名。ないときは空。</summary>
        public string GetCurrentPatternName()
        {
            return GetChannelPatternName(0);
        }

        /// <summary>チャンネルの今のパターンの内部名。ないときは空。</summary>
        public string GetChannelPatternName(int channel)
        {
            if (channel < 0 || channel >= ChannelCount) return "";
            int pattern = currentPattern[channel];
            return pattern >= 0 && pattern < patternIds.Length ? patternIds[pattern] : "";
        }

        [NetworkCallable]
        public void ReceivePattern(int channel, int pattern, int parameter, int fromStep)
        {
            if (channel < 0 || channel >= ChannelCount || pattern >= patternNames.Length) return;
            received[channel] = true;
            if (HasPassed(fromStep))
            {
                // 呼び出しより先にそのステップが届いていた：差し替えて、今のステップの列を1回だけ流して追いつく。
                SetChannelPattern(channel, pattern);
                currentParameter[channel] = parameter;
                hasPending[channel] = false;
                if (lastStep != StepGameEnd || fromStep == StepGameEnd) PlayStep(channel, lastStep, lastWon, lastReel);
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
            OnReelStep(step, won, -1);
        }

        /// <summary>OnStep の停止版。reelIndex は押したリール（0=左 1=中 2=右）。</summary>
        public void OnReelStep(int step, bool won, int reelIndex)
        {
            if (step < 0 || step >= StepCount) return;
            lastStep = step;
            lastWon = won;
            lastReel = reelIndex;
            for (int c = 0; c < ChannelCount; c++)
            {
                // 途中のステップが届かなかった（回転開始より先に停止が届いたなど）ときも、追い越したら差し替える。
                if (hasPending[c] && pendingFromStep[c] <= step)
                {
                    SetChannelPattern(c, pendingPattern[c]);
                    currentParameter[c] = pendingParameter[c];
                    hasPending[c] = false;
                }
                PlayStep(c, step, won, reelIndex);
                if (step == StepGameEnd) EndGame(c);
            }
        }

        /// <summary>ゲーム終了の後：次のパターンへ進む。常駐でないチャンネルは、次がなければ止まる。</summary>
        private void EndGame(int channel)
        {
            int pattern = currentPattern[channel];
            if (hasPending[channel] || pattern < 0) return;
            int next = pattern < patternNext.Length ? patternNext[pattern] : -1;
            if (next >= 0 && next < patternNames.Length) SetChannelPattern(channel, next);
            else if (!IsResidentChannel(channel)) SetChannelPattern(channel, -1);
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

        private void PlayStep(int channel, int step, bool won, int reel)
        {
            int pattern = currentPattern[channel];
            if (pattern < 0) return;
            for (int i = 0; i < entryAction.Length; i++)
            {
                if (entryPattern[i] != pattern || entryStep[i] != step) continue;
                int branch = entryBranch[i];
                if (branch == BranchWin && !won) continue;
                if (branch == BranchLose && won) continue;
                if (branch >= BranchLeft && branch - BranchLeft != reel) continue;
                StartAction(channel, entryAction[i], step, currentParameter[channel]);
            }
        }

        private void StartAction(int channel, int action, int step, int parameter)
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
            int[] channels = new int[n + 1];
            System.Array.Copy(queueChannel, channels, n);
            channels[n] = channel;
            actions[n] = action;
            parameters[n] = parameter;
            times[n] = Time.time + delay / 1000f;
            queueAction = actions;
            queueParameter = parameters;
            queueTime = times;
            queueChannel = channels;
        }

        public void Update()
        {
            if (queueAction.Length == 0) return;
            float now = Time.time;
            int kept = 0;
            for (int i = 0; i < queueAction.Length; i++)
            {
                if (queueTime[i] <= now) { if (queueAction[i] >= 0) RunAction(queueAction[i], lastStep, queueParameter[i]); }
                else kept++;
            }
            if (kept == queueAction.Length) return;
            int[] actions = new int[kept];
            int[] parameters = new int[kept];
            float[] times = new float[kept];
            int[] keptChannels = new int[kept];
            int k = 0;
            for (int i = 0; i < queueAction.Length; i++)
            {
                if (queueTime[i] <= now) continue;
                actions[k] = queueAction[i];
                parameters[k] = queueParameter[i];
                times[k] = queueTime[i];
                keptChannels[k] = queueChannel[i];
                k++;
            }
            queueAction = actions;
            queueParameter = parameters;
            queueTime = times;
            queueChannel = keptChannels;
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
            if (action < actionEnableStart.Length)
            {
                start = actionEnableStart[action];
                end = start + actionEnableCount[action];
                for (int i = start; i < end && i < enableValues.Length; i++) SetEnabled(i, enableValues[i]);
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

            // キーのないリールには触らない。最後のキーを過ぎたリールは、そこで元の動きに戻す。
            int[] lastTime = new int[] { -1, -1, -1 };
            for (int i = start; i < start + count; i++)
            {
                int reel = reelKeyReel[i];
                if (reel >= 0 && reel < 3) lastTime[reel] = Mathf.Max(lastTime[reel], Mathf.Max(0, reelKeyTime[i]));
            }

            int sections = timeCount;
            int[] durations = new int[sections];
            float[] left = new float[sections];
            float[] center = new float[sections];
            float[] right = new float[sections];
            // 区間ごと・リールごとの動き方と、キー間のどこからどこまでか（0〜1）。
            int[] eases = new int[sections * 3];
            float[] easeRanges = new float[sections * 6];
            int previous = 0;
            for (int s = 0; s < sections; s++)
            {
                durations[s] = times[s] - previous;
                left[s] = SectionValue(start, count, 0, s, previous, times[s], lastTime[0], resolved);
                center[s] = SectionValue(start, count, 1, s, previous, times[s], lastTime[1], resolved);
                right[s] = SectionValue(start, count, 2, s, previous, times[s], lastTime[2], resolved);
                for (int reel = 0; reel < 3; reel++) SectionEase(start, count, reel, previous, times[s], s * 3 + reel, eases, easeRanges);
                previous = times[s];
            }
            mainBoard.PlayReelEffectEased(durations, left, center, right, step == StepLeverOn, eases, easeRanges);
        }

        /// <summary>区間 from〜to を含むキーの間を探し、その動き方と、キー間のうち区間が占める範囲（0〜1）を入れる。</summary>
        private void SectionEase(int start, int count, int reel, int from, int to, int index, int[] eases, float[] ranges)
        {
            int beforeTime = 0, afterTime = int.MaxValue, afterIndex = -1;
            for (int i = start; i < start + count; i++)
            {
                if (reelKeyReel[i] != reel) continue;
                int time = Mathf.Max(0, reelKeyTime[i]);
                if (time <= from && time >= beforeTime) beforeTime = time;
                if (time >= to && time < afterTime) { afterTime = time; afterIndex = i; }
            }
            ranges[index * 2] = 0f;
            ranges[index * 2 + 1] = 1f;
            if (afterIndex < 0 || afterTime <= beforeTime) return;
            eases[index] = KeyEase(afterIndex);
            ranges[index * 2] = (from - beforeTime) / (float)(afterTime - beforeTime);
            ranges[index * 2 + 1] = (to - beforeTime) / (float)(afterTime - beforeTime);
        }

        private int KeyEase(int key)
        {
            return reelKeyEase.Length > key ? reelKeyEase[key] : EaseLinear;
        }

        /// <summary>区間の終わりの位置。キーがない、または最後のキーより後の区間は NaN（触らない）。</summary>
        private float SectionValue(int start, int count, int reel, int section, int from, int to, int lastTime, float[] resolved)
        {
            if (lastTime < 0) return float.NaN;
            if (section > 0 && from >= lastTime) return float.NaN;
            return ValueAt(start, count, reel, to, resolved);
        }

        /// <summary>リール reel の時刻 t での位置（開始位置から上方向へのコマ数）。0ms は開始位置で、キーの間はキーの動き方（イージング）に従う。</summary>
        private float ValueAt(int start, int count, int reel, int t, float[] resolved)
        {
            // 0ms は開始位置（移動 0）。0ms にキーがあればそちらを使う。
            int beforeTime = 0, afterTime = int.MaxValue;
            float beforeValue = 0f, afterValue = 0f;
            bool hasBefore = true, hasAfter = false;
            int afterIndex = -1;
            for (int i = start; i < start + count; i++)
            {
                if (reelKeyReel[i] != reel) continue;
                int time = reelKeyTime[i];
                float value = resolved[i - start];
                if (time <= t && time >= beforeTime) { beforeTime = time; beforeValue = value; hasBefore = true; }
                if (time >= t && time < afterTime) { afterTime = time; afterValue = value; hasAfter = true; afterIndex = i; }
            }
            if (!hasBefore && !hasAfter) return 0f;
            if (!hasBefore) return afterValue;
            if (!hasAfter || afterTime == beforeTime) return beforeValue;
            float p = (t - beforeTime) / (float)(afterTime - beforeTime);
            return beforeValue + (afterValue - beforeValue) * mainBoard.EaseValue(KeyEase(afterIndex), p);
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
