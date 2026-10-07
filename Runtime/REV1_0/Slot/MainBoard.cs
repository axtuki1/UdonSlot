using System;
using AX.UdonSlot.REV1_0.Serialize;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// スロットの遊技状態とリール動作を管理するメイン基板。
    /// BET、ゲーム開始、停止判定、配当、同期処理を一元管理する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class MainBoard : UdonSharpBehaviour
    {
        protected virtual string _MAINBOARD_VERSION() => "UNVERSIONED";
        private string _UDONSLOT_MAINBOARD_VERSION = "1.0.0";
        
        /// <summary>
        /// メイン基板のバージョン情報。
        /// </summary>
        public string MAINBOARD_VERSION => _MAINBOARD_VERSION();
        
        /// <summary>
        /// UdonSlotのバージョン情報。
        /// </summary>
        public string UDONSLOT_MAINBOARD_VERSION => _UDONSLOT_MAINBOARD_VERSION;
        
        private float REEL_ANGLEPOS_BASE = 58f;

        public bool isDebug = false;

        public int pictureCount = 21; // シンボルの数

        /// <summary>
        /// シンボル1つ分の角度
        /// </summary>
        protected float anglePerSymbol;

        public bool isControllable
        {
            get
            {
                // 次に操作可能になる時間を過ぎているかどうかで、プレイヤーが操作可能かどうかを判断する
                return Networking.GetServerTimeInMilliseconds() >= nextControllableTime;
            }
        } // プレイヤーが操作可能かどうか

        [SerializeField]
        protected bool isWaitMode = true;

        [SerializeField]
        protected int requiredBet = 1;

        /// <summary>
        /// 持ち玉
        /// </summary>
        [UdonSynced]
        public int credit;

        /// <summary>
        /// 直近の払出
        /// </summary>
        [UdonSynced]
        public int lastPayout;

        /// <summary>
        /// 直近の遊技終了時に、有効ライン上へ停止した図柄名。
        /// PayoutTableの有効ライン順に、左、中、右の3要素単位で格納する。
        /// </summary>
        [UdonSynced]
        public string[] lastPayLineSymbols = new string[0];

        /// <summary>
        /// ベット数
        /// </summary>
        [UdonSynced]
        public int bet;

        /// <summary>
        /// リプレイ成立によって前回BETが再装填され、クレジットへ戻せない状態か。
        /// 次のリール回転開始までtrueを維持する。
        /// </summary>
        [UdonSynced]
        public bool isReplay = false;

        /// <summary>
        /// 最大ベット数
        /// </summary>
        public int maxBet = 3;

        /// <summary>
        /// ゲーム中か
        /// </summary>
        [UdonSynced]
        public bool isGaming = false;

        /// <summary>
        /// 疑似遊技モード
        /// <ul>
        ///   <li>リールがぷるぷるする</li>
        ///   <li>BETが0にならない</li>
        ///   <li>払出が発生しない</li>
        /// </ul>
        /// </summary>
        public bool isMockPlay = false;

        [SerializeField, Tooltip("疑似遊技中の震え幅（度）。正の値で上→下の順に動く。")]
        private float mockPlayShakeAngle = .4f;

        private float _mockPlayShakeElapsed;
        private bool _mockPlayShakeActive;
        private bool[] _mockPlayShakeApplied = new bool[3];
        private Quaternion[] _mockPlayShakeBaseRotation = new Quaternion[3];
        private bool[] _scheduledRollHolding = new bool[3];

        /// <summary>
        /// リール回転中
        /// </summary>
        [UdonSynced]
        public bool isRollingReel1 = false, isRollingReel2 = false, isRollingReel3 = false;

        [UdonSynced]
        public Quaternion reelRot_1 = Quaternion.identity,
            reelRot_2 = Quaternion.identity,
            reelRot_3 = Quaternion.identity; // 停止時のリールの回転角度

        // 各リールの reelRot_ が更新されたサーバ時刻（ミリ秒）。reelRot_ を基準として現在の回転を計算するために使う
        [UdonSynced]
        public double reelRotTime_1 = 0.0, reelRotTime_2 = 0.0, reelRotTime_3 = 0.0;

        [SerializeField]
        private Quaternion defaultReelRot;

        [UdonSynced]
        public float nextControllableTime = 0f; // 次に操作可能になる時間（ミリ秒）

        public bool requestGameStart = false;

        [UdonSynced]
        public float firstLeverOnTime = 0f;

        [UdonSynced]
        public float gameStartTime = 0f; // 前回ゲームの開始時間（秒）

        [SerializeField]
        protected float maxReelSpeed = 0.4615f;

        protected float reel1Speed = 0f, reel2Speed = 0f, reel3Speed = 0f; // リールの回転速度

        [SerializeField]
        private float reelAcceleration = 1.75f; // リールの加速度

        [SerializeField]
        protected Transform reel1Transform, reel2Transform, reel3Transform;

        public bool[] pushingButton = new bool[3];
        public bool isPushingMaxBet = false;

        protected bool isInit;
        
        [FormerlySerializedAs("reelController")]
        [SerializeField]
        public ReelOutputController reelOutputController;
        
        [SerializeField]
        public SubBoard subBoard;

        [SerializeField]
        protected GameObject controllableStateObject; // 操作可能状態のオブジェクト
        
        [SerializeField]
        protected SegView segView;
        
        /// <summary>
        /// 各リールの停止ボタン押下時のシンボルのインデックス。0からpictureCount-1までの値。0ならシンボル0が下段に、1ならシンボル1が下段に停止する。<br />
        /// 滑り前のシンボルを示すので注意。
        /// 回転中のリールは-1。
        /// </summary>
        [UdonSynced]
        public int[] stoppedReelsSymbolIndex = new int[3];

        /// <summary>
        /// 各リールの滑りコマ数。
        /// </summary>
        [UdonSynced]
        public int[] stoppedReelsThroughSymbolCount = new int[3];
        
        /// <summary>
        /// 停止したリールのビタ押し精度（0～1に正規化）。<br />
        /// 1=完全ビタ、0.5=半コマずれ、0=1コマ以上のずれ。
        /// </summary>
        [UdonSynced]
        public float[] stoppedReelsPerfectAccuracy = new float[3];

        /// <summary>
        /// 停止済みリール数。
        /// </summary>
        public int stoppedReelCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < stoppedReelsSymbolIndex.Length; i++)
                {
                    if (stoppedReelsSymbolIndex[i] != -1) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 各リールの停止順序。
        /// </summary>
        [UdonSynced]
        public int[] stoppedReelsStopOrder = new int[] { -1, -1, -1 };
        
        /// <summary>
        /// 押し順要求時の正解順序
        /// </summary>
        [UdonSynced]
        public int[] pushOrder = new int[] { -1, -1, -1 };

        /// <summary>
        /// 押し順指定があるか
        /// </summary>
        public bool isPushOrder
        {
            get
            {
                // すべて-1だったらfalse
                return pushOrder[0] != -1 || pushOrder[1] != -1 || pushOrder[2] != -1;
            }
        }

        /// <summary>
        /// 押し順ミスしたか
        /// </summary>
        public bool isPushOrderMiss
        {
            get
            {
                for (int i = 0; i < stoppedReelsStopOrder.Length; i++)
                {
                    // 未指定と未停止は判定しない
                    if (pushOrder[i] == -1 || stoppedReelsStopOrder[i] == -1) continue;
                    if (stoppedReelsStopOrder[i] != pushOrder[i]) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// 押し順時に次に押すべきリールを返す
        /// </summary>
        public int nextOrderReelIndex
        {
            get
            {
                int nextOrder = int.MaxValue;
                int nextOrderReel = -1;
                for (int i = 0; i < stoppedReelsStopOrder.Length; i++)
                {
                    // 未指定と停止済みは対象外。同じ順番なら後のリールを返す。
                    if (pushOrder[i] == -1 || stoppedReelsStopOrder[i] != -1) continue;
                    if (pushOrder[i] > nextOrder) continue;
                    nextOrder = pushOrder[i];
                    nextOrderReel = i;
                }

                return nextOrderReel;
            }
        }
        
        private int[] scheduledRollType = new int[0];
        private int[][] scheduledRollStartTime = new int[0][];
        private int[][] scheduledRollEndTime = new int[0][];
        private float[][] scheduledRollStartAngle = new float[0][];
        private float[][] scheduledRollGoalAngle = new float[0][];
        private float[][] scheduledRollSpeed = new float[0][];
        private int[][] scheduledRollDirection = new int[0][];

        private Quaternion _reel1AccumRot = Quaternion.identity,
            _reel2AccumRot = Quaternion.identity,
            _reel3AccumRot = Quaternion.identity;

        private float[] _scheduledRollBaseAngles = new float[3];
        private bool[] _scheduledRollBaseAngleInitialized = new bool[3];
        private bool[] _scheduledRollCompleted = new bool[3];
        
        public ReelData[] reelDatas;

        private void Start()
        {
            anglePerSymbol = 360f / pictureCount;
            reelRot_1 = defaultReelRot;
            reelRot_2 = defaultReelRot;
            reelRot_3 = defaultReelRot;
            if (IsOwner())
            {
                gameStartTime = Networking.GetServerTimeInMilliseconds();
                nextControllableTime = Networking.GetServerTimeInMilliseconds();
                RequestSerialization();
            }

            CaptureReelAccumulatedRotations();

            reelDatas = new ReelData[3];
            reelDatas[0] = reel1Transform.GetComponent<ReelData>();
            reelDatas[1] = reel2Transform.GetComponent<ReelData>();
            reelDatas[2] = reel3Transform.GetComponent<ReelData>();
            
            Init();
            isInit = true;
        }

        public virtual void Init() { }
        public virtual void UpdateLogic() { }

        private void Update()
        {
            if (!isInit) return;
            RemoveMockPlayShake();
            bool isLocalOwner = IsOwner();
            if (isLocalOwner && requestGameStart && (!isWaitMode || GetLastGameStartElapsed() > 4.1f * 1000f))
            {
                Debug.Log("Game Start");
                StartReel();
            }

            double currentServerMs = Networking.GetServerTimeInMilliseconds();
            float maxStopStepDeg = maxReelSpeed * Time.deltaTime * 1000f;

            bool[] scheduledRollActive = UpdateScheduledRoll(currentServerMs);

            if (Utilities.IsValid(segView))
            {
                segView.SetNumberSeg(0, lastPayout);
            }
            
            if (!scheduledRollActive[0]) UpdateReel(0, reel1Transform, isRollingReel1, currentServerMs, isLocalOwner, maxStopStepDeg);
            if (!scheduledRollActive[1]) UpdateReel(1, reel2Transform, isRollingReel2, currentServerMs, isLocalOwner, maxStopStepDeg);
            if (!scheduledRollActive[2]) UpdateReel(2, reel3Transform, isRollingReel3, currentServerMs, isLocalOwner, maxStopStepDeg);

            if (Utilities.IsValid(controllableStateObject)) controllableStateObject.SetActive(isControllable);
            UpdateLogic();
            bool shakeReel1 = CanMockPlayShake(0, isRollingReel1, scheduledRollActive[0], _reel1AccumRot);
            bool shakeReel2 = CanMockPlayShake(1, isRollingReel2, scheduledRollActive[1], _reel2AccumRot);
            bool shakeReel3 = CanMockPlayShake(2, isRollingReel3, scheduledRollActive[2], _reel3AccumRot);

            // 最初のリールが停止したら共通周期を開始し、後から停止したリールも合流する。
            bool shakeActive = shakeReel1 || shakeReel2 || shakeReel3;
            _mockPlayShakeElapsed = shakeActive && _mockPlayShakeActive
                ? (_mockPlayShakeElapsed + Time.deltaTime) % 0.7f
                : 0f;
            _mockPlayShakeActive = shakeActive;
            if (shakeReel1) UpdateMockPlayShake(0);
            if (shakeReel2) UpdateMockPlayShake(1);
            if (shakeReel3) UpdateMockPlayShake(2);
        }

        private bool CanMockPlayShake(int reelIndex, bool isRolling, bool scheduledRollActive, Quaternion remainingRotation)
        {
            if (!isMockPlay || !Utilities.IsValid(GetReelTransform(reelIndex))) return false;
            // 予約中は通常の回転フラグにかかわらず、角度保持の予約だけを対象にする。
            if (scheduledRollActive) return _scheduledRollHolding[reelIndex];
            // 各リールの滑りが完了するまでは震えを適用しない。
            return !isRolling && Quaternion.Angle(remainingRotation, Quaternion.identity) <= 0.01f;
        }

        private void UpdateMockPlayShake(int reelIndex)
        {
            Transform reel = GetReelTransform(reelIndex);

            // 0.5秒静止 → 0.1秒上で保持 → 0.1秒下で保持 → 元の位置。
            float elapsed = _mockPlayShakeElapsed;
            float offset = elapsed < 0.5f ? 0f : (elapsed < 0.6f ? -mockPlayShakeAngle : mockPlayShakeAngle);
            if (offset == 0f) return;
            _mockPlayShakeBaseRotation[reelIndex] = reel.localRotation;
            _mockPlayShakeApplied[reelIndex] = true;
            reel.localRotation = Quaternion.Euler(offset, 0f, 0f) * reel.localRotation;
        }

        private void RemoveMockPlayShake()
        {
            for (int i = 0; i < 3; i++)
            {
                if (!_mockPlayShakeApplied[i]) continue;
                Transform reel = GetReelTransform(i);
                if (Utilities.IsValid(reel)) reel.localRotation = _mockPlayShakeBaseRotation[i];
                _mockPlayShakeApplied[i] = false;
            }
        }

        private void OnDisable()
        {
            RemoveMockPlayShake();
            _mockPlayShakeActive = false;
            _mockPlayShakeElapsed = 0f;
        }

        private void UpdateReel(int reelIndex, Transform reelTransform, bool isRolling, double currentServerMs, bool isLocalOwner, float maxStopStepDeg)
        {
            if (!Utilities.IsValid(reelTransform)) return;

            Quaternion accumulatedRotation = GetReelAccumulatedRotation(reelIndex);
            Quaternion targetRotation = GetReelRotation(reelIndex);

            if (!isRolling)
            {
                if (Quaternion.Dot(accumulatedRotation, Quaternion.identity) < 0f)
                {
                    accumulatedRotation = new Quaternion(
                        -accumulatedRotation.x,
                        -accumulatedRotation.y,
                        -accumulatedRotation.z,
                        -accumulatedRotation.w
                    );
                }

                accumulatedRotation = isLocalOwner
                    ? Quaternion.RotateTowards(accumulatedRotation, Quaternion.identity, maxStopStepDeg)
                    : Quaternion.Lerp(accumulatedRotation, Quaternion.identity, Time.deltaTime * 25f);
                SetReelAccumulatedRotation(reelIndex, accumulatedRotation);
                reelTransform.localRotation = accumulatedRotation * targetRotation;
                return;
            }

            float speed = Mathf.MoveTowards(GetReelSpeed(reelIndex), maxReelSpeed, reelAcceleration * Time.deltaTime);
            SetReelSpeed(reelIndex, speed);
            double elapsed = currentServerMs - GetReelRotationTime(reelIndex);
            if (elapsed < 0d) elapsed = 0d;
            float spinDegrees = (float)(elapsed * speed);
            Quaternion currentAccumulatedRotation = Quaternion.Euler(spinDegrees, 0f, 0f) * accumulatedRotation;
            reelTransform.localRotation = currentAccumulatedRotation * targetRotation;
        }

        /**
         * 最後のゲーム開始からの経過時間
         * ※ゲーム開始時、この数値が4100ms以上である必要がある
         */
        public float GetLastGameStartElapsed()
        {
            return Networking.GetServerTimeInMilliseconds() - gameStartTime;
        }
        
        public float GetCurrentReelAngle(int reelIndex)
        {
            Transform reel = GetReelTransform(reelIndex);
            if (!Utilities.IsValid(reel)) return 0f;
            Quaternion rotation = _mockPlayShakeApplied[reelIndex]
                ? _mockPlayShakeBaseRotation[reelIndex] : reel.localRotation;
            // EulerのX成分は90度を越えると折り返すため、Rx(angle) * Rz(90)の向きから復元する。
            Vector3 direction = rotation * Vector3.right;
            return Mathf.Repeat(Mathf.Atan2(direction.z, direction.y) * Mathf.Rad2Deg, 360f);
        }

        public float GetOneSymbolAngle()
        {
            return pictureCount > 0 ? 360f / pictureCount : 0f;
        }

        /// <summary>
        /// 指定秒数、操作を受け付けない状態にする
        /// </summary>
        /// <param name="duration">秒数</param>
        public void OffControllable(float duration = 0.25f)
        {
            if (!IsOwner()) return;
            nextControllableTime = Networking.GetServerTimeInMilliseconds() + duration * 1000f;
            RequestSerialization();
        }

        public void AddCredit(int amount)
        {
            if (!isControllable) return;
            SetOwner();
            OffControllable();
            credit += amount;
            subBoard.Credit();
        }

        public virtual void Bet()
        {
            if (!CanBet()) return;
            SetOwner();
            OffControllable();
            lastPayout = 0;
            bet++;
            credit--;
            BetLogic(1);
            subBoard.Bet(1);
            RequestSerialization();
        }
        
        public virtual void MaxBet()
        {
            if (!CanBet()) return;
            SetOwner();
            OffControllable();
            int betAmount = Mathf.Min(maxBet - bet, credit);
            lastPayout = 0;
            bet += betAmount;
            credit -= betAmount;
            BetLogic(betAmount);
            subBoard.Bet(betAmount);
            RequestSerialization();
        }

        private bool CanBet()
        {
            return isControllable && !isReplay && credit != 0 && bet < maxBet;
        }

        public virtual void PushMaxBet()
        {
            MaxBet();
            isPushingMaxBet = true;
        }

        public virtual void ReleaseMaxBetButton()
        {
            isPushingMaxBet = false;
        }
        
        public virtual void BetLogic(int bet)
        {
            
        }

        public virtual void LeverOn()
        {
            if (!isControllable || requestGameStart) return;
            if (bet == 0 || bet < requiredBet) return;
            if (isRollingReel1 || isRollingReel2 || isRollingReel3) return;
            SetOwner();
            OffControllable(0.75f);
            firstLeverOnTime = Networking.GetServerTimeInMilliseconds();
            // 疑似遊技中は再抽選せず、現在のフラグを維持する。
            if (!isMockPlay) reelOutputController.GenerateFlag(bet);
            if (!LeverOnLogic()) return;
            subBoard.Wait();
            RequestGameStart();
        }

        /// <summary>
        /// レバーオン時の処理。
        /// </summary>
        /// <returns>
        /// ゲーム開始予約を基底クラスで行うか
        /// LeverOnLogic内で遅延して予約する等、通常のタイミングと異なる場合はfalseを返す。
        /// </returns>
        public virtual bool LeverOnLogic()
        {
            return true;
        }

        public bool isPushingCenterButton = false;
        public virtual void PushCenterButton()
        {
            isPushingCenterButton = true;
            PushCenterButtonLogic();
        }
        
        public virtual void PushCenterButtonLogic()
        {
            // ここにセンターボタンを押したときの処理を実装する
        }

        public virtual void ReleaseCenterButton()
        {
            isPushingCenterButton = false;
            ReleaseCenterButtonLogic();
        }
        
        public virtual void ReleaseCenterButtonLogic()
        {
            // ここにセンターボタンを離したときの処理を実装する
        }

        public void RequestGameStart() { requestGameStart = true; }

        public int GetReelStopIndex(int reelIndex)
        {
            return pictureCount - stoppedReelsSymbolIndex[reelIndex] - 1;
        }

        /**
         * リール回転開始時の基本処理
         */
        public void StartReel()
        {
            if (!requestGameStart) return;
            int now = Networking.GetServerTimeInMilliseconds();
            isReplay = false;
            isGaming = true;
            gameStartTime = now;
            requestGameStart = false;
            isRollingReel1 = true;
            isRollingReel2 = true;
            isRollingReel3 = true;
            reel1Speed = 0f;
            reel2Speed = 0f;
            reel3Speed = 0f;

            CaptureReelAccumulatedRotations();

            reelRotTime_1 = now;
            reelRotTime_2 = now;
            reelRotTime_3 = now;

            stoppedReelsSymbolIndex = new int[] { -1, -1, -1 };
            stoppedReelsStopOrder = new int[] { -1, -1, -1 };
            stoppedReelsPerfectAccuracy = new float[] { -1, -1, -1 };

            OffControllable();
            RequestSerialization();
            StartReelLogic();
            subBoard.GameStart();
        }
        
        /// <summary>
        /// リール回転開始時の追加処理
        /// </summary>
        public virtual void StartReelLogic() { }

        /// <summary>
        /// 停止ボタン押下時の基本処理
        /// </summary>
        /// <param name="reelIndex">停止リール</param>
        public virtual void PushStopButton(int reelIndex)
        {
            if (!isControllable) return;
            if (PushStopButtonLogic(reelIndex)) StopReel(reelIndex);
        }

        /// <summary>
        /// 停止ボタン押下時の追加処理
        /// </summary>
        /// <param name="reelIndex">停止リール</param>
        /// <returns>リール停止処理を実行するか否か</returns>
        public virtual bool PushStopButtonLogic(int reelIndex)
        {
            return true;
        }
        
        /// <summary>
        /// リール停止処理
        /// </summary>
        /// <param name="reelIndex">停止リール</param>
        public void StopReel(int reelIndex)
        {
            if (!isControllable || !IsReelRolling(reelIndex)) return;

            stoppedReelsStopOrder[reelIndex] = stoppedReelCount + 1;

            Transform reelTransform = GetReelTransform(reelIndex);
            SetOwner();
            SetReelRolling(reelIndex, false);

            // 押下時の角度から、滑り前の図柄位置を求める。
            Vector3 defaultForward = defaultReelRot * Vector3.right;
            Vector3 currentForward = reelTransform.localRotation * Vector3.right;
            float pressedAngle = Vector3.SignedAngle(defaultForward, currentForward, defaultReelRot * Vector3.down);
            int symbolIndex = Mathf.RoundToInt((pressedAngle + anglePerSymbol / 2f) / anglePerSymbol % pictureCount);
            if (symbolIndex < 0) symbolIndex += pictureCount;
            int viewLowerReelIndex = (pictureCount - symbolIndex + 2) % pictureCount;

            int throughSymbolCount = ControllReel(reelIndex, viewLowerReelIndex);
            Quaternion targetRotation = Quaternion.Euler(
                REEL_ANGLEPOS_BASE + ((symbolIndex + throughSymbolCount) % pictureCount) * anglePerSymbol,
                0f,
                90f
            );
            SetReelRotation(reelIndex, targetRotation, Networking.GetServerTimeInMilliseconds());
            RequestSerialization();
            SetReelAccumulatedRotation(reelIndex, reelTransform.localRotation * Quaternion.Inverse(targetRotation));

            OffControllable(0.001f);
            stoppedReelsSymbolIndex[reelIndex] = viewLowerReelIndex;
            stoppedReelsThroughSymbolCount[reelIndex] = throughSymbolCount;
            float delta = Mathf.Abs(Mathf.DeltaAngle(pressedAngle, symbolIndex * anglePerSymbol));
            stoppedReelsPerfectAccuracy[reelIndex] = Mathf.Clamp01(1f - delta / anglePerSymbol);

            pushingButton[reelIndex] = true;
            StopReelLogic(reelIndex, GetReelStopIndex(reelIndex), stoppedReelsPerfectAccuracy[reelIndex]);

            // StopReelLogicで書き換えられた値も通知するため、ここで読み直す。
            if (IsOwner())
            {
                subBoard.SendCustomNetworkEvent(NetworkEventTarget.All,
                    nameof(subBoard.ReelStop),
                    reelIndex,
                    GetReelStopIndex(reelIndex),
                    stoppedReelsPerfectAccuracy[reelIndex]
                );
            }
            Debug.Log("滑りコマ数: "+throughSymbolCount);
            
            RequestSerialization();
        }
        
        /// <summary>
        /// リール停止時の追加処理
        /// </summary>
        /// <param name="reelIndex">停止リール</param>
        /// <param name="stopIndex">停止インデックス(下段のシンボルのインデックス)</param>
        /// <param name="accuracy">停止精度</param>
        public virtual void StopReelLogic(int reelIndex, int stopIndex, float accuracy) { }

        /// <summary>
        /// 停止ボタンを離した時の追加処理
        /// </summary>
        /// <param name="reelIndex">停止リール</param>
        public virtual void ReleaseStopButtonLogic(int reelIndex)
        { }
        
        /// <summary>
        /// 停止ボタンを離した時の基本処理
        /// </summary>
        /// <param name="reelIndex">停止リール</param>
        public void ReleaseStopButton(int reelIndex)
        {
            if (!isControllable) return;
            ReleaseStopButtonLogic(reelIndex);
            if (!isGaming) return;
            SetOwner();
            pushingButton[reelIndex] = false;
            if (!isRollingReel1 && !isRollingReel2 && !isRollingReel3) GameEnd();
        }

        public int ControllReel(int targetReelIndex, int stopSymbolIndex)
        {
            return Utilities.IsValid(reelOutputController) ? reelOutputController.ControllReel(targetReelIndex, stopSymbolIndex) : 0;
        }

        public virtual void GameEndLogic() { }

        /// <summary>前回BETがリプレイとして再装填された直後の追加処理。</summary>
        public virtual void ReplayBetLogic(int replayBet) { }

        /// <summary>現在のBETをクレジットへ戻せる状態か確認する。</summary>
        public bool CanReturnBetToCredit()
        {
            return !isReplay && !isGaming && !requestGameStart;
        }

        public void GameEnd()
        {
            GameEndLogic();
            if (isMockPlay)
            {
                isReplay = false;
            }
            else
            {
                int previousBet = bet;
                bool shouldReplay = false;
                lastPayLineSymbols = new string[0];
                if (Utilities.IsValid(reelOutputController))
                {
                    lastPayLineSymbols = reelOutputController.GetActivePayLineSymbols();
                    string[] payoutNames = reelOutputController.JudgePayout(stoppedReelsSymbolIndex);
                    int payoutAmount = reelOutputController.GetPayout(payoutNames);
                    shouldReplay = reelOutputController.IsReplay(payoutNames);
                    credit += payoutAmount;
                    lastPayout = payoutAmount;
                }

                // リプレイ時は今回のBETを次ゲームへ持ち越す。
                isReplay = shouldReplay;
                bet = shouldReplay ? previousBet : 0;
                if (shouldReplay)
                {
                    ReplayBetLogic(previousBet);
                    subBoard.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(subBoard.ReplayBet), previousBet);
                }
                reelOutputController.FlagPostProcess();
                RequestSerialization();
            }
            isGaming = false;
            subBoard.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(subBoard.GameEnd));
        }

        public bool IsOwner() { return Networking.IsOwner(gameObject); }

        public void SetOwner()
        {
            if (!IsOwner()) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            if (!reelOutputController.IsOwner()) reelOutputController.SetOwner();
        }

        public override void OnDeserialization()
        {
            CaptureReelAccumulatedRotations();
            if (!isNetworkStandby) NetworkStandby();
        }

        private bool isNetworkStandby = false;
        public void NetworkStandby()
        {
            isNetworkStandby = true;
            subBoard.NetworkStandby();
            NetworkStandbyLogic();
        }
        
        public virtual void NetworkStandbyLogic(){}
        
        public void SetMockPlay(bool value)
        {
            if (IsOwner())
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RemoteSetMockPlay), value);
            }
        }

        [NetworkCallable]
        public void RemoteSetMockPlay(bool value)
        {
            isMockPlay = value;
        }

        #region SendCustomEvent用の関数
        public void StopReel1() { StopReel(0); }
        public void StopReel2() { StopReel(1); }
        public void StopReel3() { StopReel(2); }

        public void SetOnMockPlay()
        {
            SetOwner();
            SetMockPlay(true);
        }
        public void SetOffMockPlay()
        {
            SetOwner();
            SetMockPlay(false);
        }
        
        #endregion

        // ===========================================
        // 以下、schedule用の関数
        // ===========================================

        private bool[] UpdateScheduledRoll(double currentServerMs)
        {
            bool[] activeReels = new bool[3];
            for (int i = 0; i < 3; i++) _scheduledRollHolding[i] = false;
            if (scheduledRollType.Length == 0) return activeReels;
            if (scheduledRollStartTime.Length == 0 || scheduledRollEndTime.Length == 0)
            {
                RemoveScheduledVariableArray();
                return activeReels;
            }

            int[] rollStartTime = scheduledRollStartTime[0];
            int[] rollEndTime = scheduledRollEndTime[0];
            if (rollStartTime == null || rollEndTime == null)
            {
                RemoveScheduledVariableArray();
                return activeReels;
            }

            bool hasPendingRoll = false;
            for (int reelIndex = 0; reelIndex < 3; reelIndex++)
            {
                // 他リールの予約が残っていても、完了済みリールは通常回転へ戻す。
                if (_scheduledRollCompleted[reelIndex]) continue;
                Transform reelTransform = GetReelTransform(reelIndex);
                if (!Utilities.IsValid(reelTransform)) continue;
                if (rollStartTime.Length <= reelIndex || rollEndTime.Length <= reelIndex) continue;

                int startTime = rollStartTime[reelIndex];
                int endTime = rollEndTime[reelIndex];

                if (currentServerMs < startTime)
                {
                    hasPendingRoll = true;
                    continue;
                }

                float scheduledAngle;
                bool isHoldingGoal = false;
                switch (scheduledRollType[0])
                {
                    case 0:
                        if (!TryGetScheduledGoalAngle(reelIndex, currentServerMs, startTime, endTime, out scheduledAngle)) continue;
                        // 生の角度差で比較し、0度→360度などの追加周回を保持と誤判定しない。
                        isHoldingGoal = Mathf.Abs(scheduledRollGoalAngle[0][reelIndex] - scheduledRollStartAngle[0][reelIndex]) <= 0.1f;
                        _scheduledRollHolding[reelIndex] = currentServerMs < endTime && isHoldingGoal;
                        break;
                    case 1:
                        if (!TryGetScheduledSpeedAngle(reelIndex, reelTransform, currentServerMs, startTime, endTime, out scheduledAngle)) continue;
                        break;
                    default:
                        RemoveScheduledVariableArray();
                        return activeReels;
                }

                ApplyScheduledRoll(reelIndex, reelTransform, scheduledAngle, currentServerMs, isHoldingGoal);

                if (currentServerMs < endTime)
                {
                    hasPendingRoll = true;
                    activeReels[reelIndex] = true;
                }
                else
                {
                    // 最終角度の適用は一度だけ。以降は回転基準時刻も上書きしない。
                    _scheduledRollCompleted[reelIndex] = true;
                }
            }

            if (!hasPendingRoll) RemoveScheduledVariableArray();
            return activeReels;
        }

        private bool TryGetScheduledGoalAngle(int reelIndex, double currentServerMs, int startTime, int endTime, out float angle)
        {
            angle = 0f;
            if (scheduledRollStartAngle.Length == 0 || scheduledRollGoalAngle.Length == 0) return false;

            float[] startAngles = scheduledRollStartAngle[0];
            float[] goalAngles = scheduledRollGoalAngle[0];
            if (startAngles == null || goalAngles == null) return false;
            if (startAngles.Length <= reelIndex || goalAngles.Length <= reelIndex) return false;

            if (endTime <= startTime)
            {
                angle = goalAngles[reelIndex];
                return true;
            }

            float start = startAngles[reelIndex];
            float goal = goalAngles[reelIndex];
            float globalProgress = Mathf.Clamp01((float)((currentServerMs - startTime) / (double)(endTime - startTime)));

            int direction = GetScheduledDirection(reelIndex);
            float directedGoal = GetDirectedGoal(start, goal, direction);
            float harmonizedProgress = GetGoalHarmonizedProgress(reelIndex, globalProgress);

            angle = Mathf.Lerp(start, directedGoal, harmonizedProgress);
            return true;
        }

        private float GetGoalHarmonizedProgress(int reelIndex, float globalProgress)
        {
            if (scheduledRollStartAngle.Length == 0 || scheduledRollGoalAngle.Length == 0) return globalProgress;

            float[] starts = scheduledRollStartAngle[0];
            float[] goals = scheduledRollGoalAngle[0];
            if (starts == null || goals == null) return globalProgress;

            int reelCount = 3;
            float[] distances = new float[reelCount];
            float minDistance = float.MaxValue;

            for (int i = 0; i < reelCount; i++)
            {
                if (starts.Length <= i || goals.Length <= i)
                {
                    distances[i] = 0f;
                    continue;
                }

                float directedGoal = GetDirectedGoal(starts[i], goals[i], GetScheduledDirection(i));
                float distance = Mathf.Abs(directedGoal - starts[i]);
                distances[i] = distance;

                if (distance > 0f && distance < minDistance)
                {
                    minDistance = distance;
                }
            }

            if (minDistance == float.MaxValue) return globalProgress;
            if (reelIndex < 0 || reelIndex >= reelCount) return globalProgress;

            float totalDistance = distances[reelIndex];
            if (totalDistance <= 0.0001f) return 1f;

            // 進行の半分時点で、全リールの残差が同じになるようにする
            float targetRemainingAtHalf = minDistance * 0.5f;
            float midProgress = 1f - (targetRemainingAtHalf / totalDistance);
            midProgress = Mathf.Clamp01(midProgress);

            if (globalProgress <= 0.5f)
            {
                return Mathf.Lerp(0f, midProgress, globalProgress / 0.5f);
            }

            return Mathf.Lerp(midProgress, 1f, (globalProgress - 0.5f) / 0.5f);
        }

        private float GetDirectedGoal(float start, float goal, int direction)
        {
            if (direction == 0)
            {
                // 方向省略時は最短経路。絶対角ではなく差分で追加周回を判定する。
                float delta = goal - start;
                if (Mathf.Abs(delta) >= 360f) return goal;
                return start + Mathf.DeltaAngle(start, goal);
            }
            float directedGoal = goal;
            if (direction >= 0)
            {
                while (directedGoal < start)
                {
                    directedGoal += 360f;
                }
            }
            else
            {
                while (directedGoal > start)
                {
                    directedGoal -= 360f;
                }
            }

            return directedGoal;
        }

        private bool TryGetScheduledSpeedAngle(int reelIndex, Transform reelTransform, double currentServerMs, int startTime, int endTime, out float angle)
        {
            angle = 0f;
            if (scheduledRollSpeed.Length == 0) return false;

            float[] rollSpeeds = scheduledRollSpeed[0];
            if (rollSpeeds == null) return false;
            if (rollSpeeds.Length <= reelIndex) return false;

            if (!_scheduledRollBaseAngleInitialized[reelIndex])
            {
                _scheduledRollBaseAngles[reelIndex] = GetCurrentReelAngle(reelIndex);
                _scheduledRollBaseAngleInitialized[reelIndex] = true;
            }

            double elapsedMs = currentServerMs - startTime;
            if (elapsedMs < 0d) elapsedMs = 0d;
            double durationMs = endTime - startTime;
            if (durationMs < 0d) durationMs = 0d;
            if (elapsedMs > durationMs) elapsedMs = durationMs;

            int direction = GetScheduledDirection(reelIndex);
            float speed = Mathf.Abs(rollSpeeds[reelIndex]);

            // +指定で正方向、-指定で逆方向
            angle = _scheduledRollBaseAngles[reelIndex] + (float)(elapsedMs * speed * direction);
            return true;
        }

        private void ApplyScheduledRoll(int reelIndex, Transform reelTransform, float scheduledAngle, double currentServerMs, bool isHoldingGoal)
        {
            Quaternion rotation = Quaternion.Euler(scheduledAngle, 0f, 90f);
            reelTransform.localRotation = rotation;

            if (isHoldingGoal && !IsReelRolling(reelIndex))
            {
                // 一時保持で確定停止角を上書きしない。滑り途中なら演出終了後に残りを進める。
                SetReelAccumulatedRotation(reelIndex, rotation * Quaternion.Inverse(GetReelRotation(reelIndex)));
                return;
            }

            SetReelRotation(reelIndex, rotation, currentServerMs);
            SetReelAccumulatedRotation(reelIndex, Quaternion.identity);
        }

        public Transform GetReelTransform(int reelIndex)
        {
            switch (reelIndex)
            {
                case 0: return reel1Transform;
                case 1: return reel2Transform;
                case 2: return reel3Transform;
                default: return null;
            }
        }

        private void CaptureReelAccumulatedRotations()
        {
            RemoveMockPlayShake();
            for (int i = 0; i < 3; i++)
            {
                Transform reel = GetReelTransform(i);
                if (!Utilities.IsValid(reel)) continue;
                SetReelAccumulatedRotation(i, reel.localRotation * Quaternion.Inverse(GetReelRotation(i)));
            }
        }

        private float GetReelSpeed(int reelIndex)
        {
            switch (reelIndex)
            {
                case 0: return reel1Speed;
                case 1: return reel2Speed;
                case 2: return reel3Speed;
                default: return 0f;
            }
        }

        private void SetReelSpeed(int reelIndex, float speed)
        {
            switch (reelIndex)
            {
                case 0:
                    reel1Speed = speed;
                    break;
                case 1:
                    reel2Speed = speed;
                    break;
                case 2:
                    reel3Speed = speed;
                    break;
            }
        }

        private Quaternion GetReelRotation(int reelIndex)
        {
            switch (reelIndex)
            {
                case 0: return reelRot_1;
                case 1: return reelRot_2;
                case 2: return reelRot_3;
                default: return Quaternion.identity;
            }
        }

        private double GetReelRotationTime(int reelIndex)
        {
            switch (reelIndex)
            {
                case 0: return reelRotTime_1;
                case 1: return reelRotTime_2;
                case 2: return reelRotTime_3;
                default: return 0d;
            }
        }

        private Quaternion GetReelAccumulatedRotation(int reelIndex)
        {
            switch (reelIndex)
            {
                case 0: return _reel1AccumRot;
                case 1: return _reel2AccumRot;
                case 2: return _reel3AccumRot;
                default: return Quaternion.identity;
            }
        }

        private bool IsReelRolling(int reelIndex)
        {
            switch (reelIndex)
            {
                case 0: return isRollingReel1;
                case 1: return isRollingReel2;
                case 2: return isRollingReel3;
                default: return false;
            }
        }

        private void SetReelRolling(int reelIndex, bool isRolling)
        {
            switch (reelIndex)
            {
                case 0:
                    isRollingReel1 = isRolling;
                    break;
                case 1:
                    isRollingReel2 = isRolling;
                    break;
                case 2:
                    isRollingReel3 = isRolling;
                    break;
            }
        }

        private void SetReelRotation(int reelIndex, Quaternion rotation, double serverTime)
        {
            switch (reelIndex)
            {
                case 0:
                    reelRot_1 = rotation;
                    reelRotTime_1 = serverTime;
                    break;
                case 1:
                    reelRot_2 = rotation;
                    reelRotTime_2 = serverTime;
                    break;
                case 2:
                    reelRot_3 = rotation;
                    reelRotTime_3 = serverTime;
                    break;
            }
        }

        private void SetReelAccumulatedRotation(int reelIndex, Quaternion rotation)
        {
            switch (reelIndex)
            {
                case 0:
                    _reel1AccumRot = rotation;
                    break;
                case 1:
                    _reel2AccumRot = rotation;
                    break;
                case 2:
                    _reel3AccumRot = rotation;
                    break;
            }
        }

        private void ResetScheduledRollRuntimeState()
        {
            _scheduledRollBaseAngles = new float[3];
            _scheduledRollBaseAngleInitialized = new bool[3];
            _scheduledRollCompleted = new bool[3];
        }

        private int[] NormalizeIntArray3(int[] values, int defaultValue)
        {
            int[] result = new int[] { defaultValue, defaultValue, defaultValue };
            if (values == null) return result;
            int copyLength = values.Length;
            if (copyLength > 3) copyLength = 3;
            for (int i = 0; i < copyLength; i++) result[i] = values[i];
            return result;
        }

        private float[] NormalizeFloatArray3(float[] values, float defaultValue)
        {
            float[] result = new float[] { defaultValue, defaultValue, defaultValue };
            if (values == null) return result;
            int copyLength = values.Length;
            if (copyLength > 3) copyLength = 3;
            for (int i = 0; i < copyLength; i++) result[i] = values[i];
            return result;
        }

        private int[] NormalizeDirectionArray3(int[] values)
        {
            int[] result = new int[] { 1, 1, 1 };
            if (values == null) return result;

            int copyLength = values.Length;
            if (copyLength > 3) copyLength = 3;
            for (int i = 0; i < copyLength; i++)
            {
                result[i] = values[i] < 0 ? -1 : 1;
            }

            return result;
        }

        private int[] CreateDefaultEndTimes(int[] startTimes, int durationMs = 1000)
        {
            int[] safeStartTimes = NormalizeIntArray3(startTimes, Networking.GetServerTimeInMilliseconds());
            return new int[]
            {
                safeStartTimes[0] + durationMs,
                safeStartTimes[1] + durationMs,
                safeStartTimes[2] + durationMs
            };
        }

        private float[] GetCurrentReelAngles()
        {
            return new float[] { GetCurrentReelAngle(0), GetCurrentReelAngle(1), GetCurrentReelAngle(2) };
        }

        public int AddScheduledVariableArray()
        {
            return AddScheduledVariableArrays(1);
        }

        private int AddScheduledVariableArrays(int count)
        {
            int newIndex = scheduledRollType.Length;
            int[] newScheduledRollType = new int[scheduledRollType.Length + count];
            int[][] newScheduledRollStartTime = new int[scheduledRollStartTime.Length + count][];
            int[][] newScheduledRollEndTime =  new int[scheduledRollEndTime.Length + count][];
            float[][] newScheduledRollStartAngle = new float[scheduledRollStartAngle.Length + count][];
            float[][] newScheduledRollGoalAngle = new float[scheduledRollGoalAngle.Length + count][];
            float[][] newScheduledRollSpeed = new float[scheduledRollSpeed.Length + count][];
            int[][] newScheduledRollDirection = new int[scheduledRollDirection.Length + count][];

            Array.Copy(scheduledRollType, 0, newScheduledRollType, 0, scheduledRollType.Length);
            Array.Copy(scheduledRollStartTime, 0, newScheduledRollStartTime, 0, scheduledRollStartTime.Length);
            Array.Copy(scheduledRollEndTime, 0, newScheduledRollEndTime, 0, scheduledRollEndTime.Length);
            Array.Copy(scheduledRollStartAngle, 0, newScheduledRollStartAngle, 0, scheduledRollStartAngle.Length);
            Array.Copy(scheduledRollGoalAngle, 0, newScheduledRollGoalAngle, 0, scheduledRollGoalAngle.Length);
            Array.Copy(scheduledRollSpeed, 0, newScheduledRollSpeed, 0, scheduledRollSpeed.Length);
            Array.Copy(scheduledRollDirection, 0, newScheduledRollDirection, 0, scheduledRollDirection.Length);

            scheduledRollType = newScheduledRollType;
            scheduledRollStartTime = newScheduledRollStartTime;
            scheduledRollEndTime = newScheduledRollEndTime;
            scheduledRollStartAngle = newScheduledRollStartAngle;
            scheduledRollGoalAngle = newScheduledRollGoalAngle;
            scheduledRollSpeed = newScheduledRollSpeed;
            scheduledRollDirection = newScheduledRollDirection;

            return newIndex;
        }

        public void RemoveScheduledVariableArray()
        {
            if (scheduledRollType.Length == 0) return;

            int[] newScheduledRollType = new int[scheduledRollType.Length - 1];
            int[][] newScheduledRollStartTime = new int[scheduledRollStartTime.Length - 1][];
            int[][] newScheduledRollEndTime =  new int[scheduledRollEndTime.Length - 1][];
            float[][] newScheduledRollStartAngle = new float[scheduledRollStartAngle.Length - 1][];
            float[][] newScheduledRollGoalAngle = new float[scheduledRollGoalAngle.Length - 1][];
            float[][] newScheduledRollSpeed = new float[scheduledRollSpeed.Length - 1][];
            int[][] newScheduledRollDirection = new int[scheduledRollDirection.Length - 1][];
            int newLength = newScheduledRollType.Length;

            Array.Copy(scheduledRollType, 1, newScheduledRollType, 0, newLength);
            Array.Copy(scheduledRollStartTime, 1, newScheduledRollStartTime, 0, newLength);
            Array.Copy(scheduledRollEndTime, 1, newScheduledRollEndTime, 0, newLength);
            Array.Copy(scheduledRollStartAngle, 1, newScheduledRollStartAngle, 0, newLength);
            Array.Copy(scheduledRollGoalAngle, 1, newScheduledRollGoalAngle, 0, newLength);
            Array.Copy(scheduledRollSpeed, 1, newScheduledRollSpeed, 0, newLength);
            Array.Copy(scheduledRollDirection, 1, newScheduledRollDirection, 0, newLength);

            scheduledRollType = newScheduledRollType;
            scheduledRollStartTime = newScheduledRollStartTime;
            scheduledRollEndTime = newScheduledRollEndTime;
            scheduledRollStartAngle = newScheduledRollStartAngle;
            scheduledRollGoalAngle = newScheduledRollGoalAngle;
            scheduledRollSpeed = newScheduledRollSpeed;
            scheduledRollDirection = newScheduledRollDirection;
            ResetScheduledRollRuntimeState();
        }

        /// <summary>
        /// 複数区間の角度指定予約を一括追加する。方向はscheduleRollWithGoalと同じ最短経路。
        /// ローカル動作のため、全員へ反映する場合はSendCustomNetworkEvent(NetworkEventTarget.All,
        /// nameof(scheduleRollSequence), baseTime, startOffsets, endOffsets, startAngles, goalAngles)で一度だけ呼ぶ。
        /// </summary>
        /// <param name="baseTime">全区間共通の基準絶対時刻(ms)。送信側でNetworking.GetServerTimeInMilliseconds()を取得する。</param>
        /// <param name="startOffsets">各区間の開始時刻。baseTimeからの経過ms。[区間0左, 中, 右, 区間1左, 中, 右, ...]。</param>
        /// <param name="endOffsets">各区間の終了時刻。baseTimeからの経過ms。配列順はstartOffsetsと同じ。</param>
        /// <param name="startAngles">各区間の開始絶対角度(度)。配列順はstartOffsetsと同じ。</param>
        /// <param name="goalAngles">各区間の目標絶対角度(度)。配列順はstartOffsetsと同じ。</param>
        /// <remarks>
        /// 4配列はすべて必須で同じ長さ、要素数は3の倍数。空配列は何もしない。
        /// 時刻は非負かつ終了が開始以降、角度は有限値。不正な入力は全体を追加せずに終了する。
        /// 区間は配列順で既存キューの末尾へ追加する。後続区間は前区間の全リール終了後に処理されるため、
        /// 通常は前区間の最大終了時刻以降に次区間を開始する。終了時間はリールごとに変えられる。
        /// 既存予約の進行はリセットしない。保持・震え・複数周の仕様はscheduleRollWithGoalと同じ。
        /// ネットワークイベントの容量上限を超える長大な演出は分割する。
        /// </remarks>
        /// <example>
        /// <code>
        /// // 1秒保持した後、0.2秒で全リールを上へ1図柄移動。
        /// float l = GetCurrentReelAngle(0), c = GetCurrentReelAngle(1), r = GetCurrentReelAngle(2);
        /// float step = GetOneSymbolAngle();
        /// SendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollSequence),
        ///     Networking.GetServerTimeInMilliseconds(),
        ///     new int[] { 0, 0, 0, 1000, 1000, 1000 },
        ///     new int[] { 1000, 1000, 1000, 1200, 1200, 1200 },
        ///     new float[] { l, c, r, l, c, r },
        ///     new float[] { l, c, r, l - step, c - step, r - step });
        /// </code>
        /// </example>
        [NetworkCallable]
        public void scheduleRollSequence(int baseTime, int[] startOffsets, int[] endOffsets, float[] startAngles, float[] goalAngles)
        {
            if (startOffsets == null || endOffsets == null || startAngles == null || goalAngles == null)
            {
                Debug.LogWarning("scheduleRollSequence: 配列にnullは指定できません。");
                return;
            }
            int length = startOffsets.Length;
            if (length % 3 != 0 || endOffsets.Length != length || startAngles.Length != length || goalAngles.Length != length)
            {
                Debug.LogWarning("scheduleRollSequence: 配列は同じ長さかつ3の倍数にしてください。");
                return;
            }
            if (length == 0) return;
            for (int i = 0; i < length; i++)
            {
                if (startOffsets[i] < 0 || endOffsets[i] < startOffsets[i] ||
                    float.IsNaN(startAngles[i]) || float.IsInfinity(startAngles[i]) ||
                    float.IsNaN(goalAngles[i]) || float.IsInfinity(goalAngles[i]))
                {
                    Debug.LogWarning("scheduleRollSequence: 時刻または角度が不正です。");
                    return;
                }
            }

            int count = length / 3;
            int firstIndex = AddScheduledVariableArrays(count);
            for (int section = 0; section < count; section++)
            {
                int offset = section * 3;
                SetScheduledRoll(
                    firstIndex + section,
                    0,
                    new int[] { baseTime + startOffsets[offset], baseTime + startOffsets[offset + 1], baseTime + startOffsets[offset + 2] },
                    new int[] { baseTime + endOffsets[offset], baseTime + endOffsets[offset + 1], baseTime + endOffsets[offset + 2] },
                    new float[] { startAngles[offset], startAngles[offset + 1], startAngles[offset + 2] },
                    new float[] { goalAngles[offset], goalAngles[offset + 1], goalAngles[offset + 2] },
                    new float[3],
                    new int[3]);
            }
        }

        // scheduleRollWith系 共通ルール:
        // ・配列はすべて [左, 中, 右] の3要素。nullは各引数の既定値を使う。
        // ・時刻はNetworking.GetServerTimeInMilliseconds()と同じ絶対時刻(ms)。
        //   「1秒後」は1000ではなく now + 1000 を渡す。Time.timeとは単位も基準も異なる。
        // ・予約は呼び出した順にキューへ追加され、先頭の全リールが終了すると次の予約へ進む。
        //   後続予約の時刻は自動で後ろにずれないため、連続演出では開始・終了時刻を明示する。
        // ・各リールは自分の終了時刻で通常制御へ戻る。isRollingReelがtrueなら通常回転を再開する。
        //   他のリールに予約が残っていても、その終了を待たない。
        // ・StopReelの代わりに見た目を動かす処理であり、停止順・停止図柄の確定は行わない。
        //   isMockPlayや操作禁止も自動では設定しない。必要ならSetMockPlay/OffControllableを併用する。
        // ・直接呼ぶとこのクライアントの予約だけが追加される。[NetworkCallable]だけでは自動同期しない。
        //   全員に同じ演出を見せる場合はSendCustomNetworkEvent(NetworkEventTarget.All, nameof(対象メソッド), ...)で呼ぶ。
        //   発行元(通常はOwner)から一度だけ送る。Allには送信者自身も含まれるため、直接呼び出しを併用すると二重予約になる。
        //   以下の例はMainBoard内・派生クラス内からの呼び出し。別BehaviourからはmainBoard.SendCustomNetworkEvent(...)とする。
        //   nullの現在時刻・現在角度は、実行した各クライアントで取得される。

        /// <summary>
        /// 指定時刻までに目標角度へ動かす予約を追加する。方向指定なしでは最短経路を使う。
        /// ローカル動作のため、全員に反映する場合はSendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollWithGoal), ...)で呼ぶ。
        /// </summary>
        /// <param name="rollStartTime">[左, 中, 右]の開始絶対時刻(ms)。nullなら呼び出し時点。</param>
        /// <param name="rollStartAngle">[左, 中, 右]の開始角度(度)。nullなら呼び出し時点の角度。予約開始時点で再取得はしない。</param>
        /// <param name="rollEndTime">[左, 中, 右]の終了絶対時刻(ms)。nullなら各開始時刻の1000ms後。別々の時刻も指定可能。</param>
        /// <param name="rollGoalAngle">[左, 中, 右]の目標絶対角度(度)。移動量ではない。nullなら開始角度と同じ位置を保持。</param>
        /// <remarks>
        /// 角度はGetCurrentReelAngleで取得する。上へ1図柄なら「開始角度 - GetOneSymbolAngle()」。
        /// 開始角と目標角の差が360度未満なら境界をまたいでも最短経路、360度以上なら差分どおりに回す。
        /// 例: 350→10は+20度、10→350は-20度、350→710は+360度。
        /// 移動量が異なるリール間で進行を調整するため、一定速度とは限らない。
        /// isMockPlayがtrueで開始角と目標角の生の差が0.1度以内なら、保持中も共通周期でぷるぷるする。
        /// </remarks>
        /// <example>
        /// <code>
        /// // 0.2秒かけて全リールを上へ1図柄動かす。
        /// int now = Networking.GetServerTimeInMilliseconds();
        /// float step = GetOneSymbolAngle();
        /// float[] start = new float[] { GetCurrentReelAngle(0), GetCurrentReelAngle(1), GetCurrentReelAngle(2) };
        /// SendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollWithGoal),
        ///     new int[] { now, now, now }, start,
        ///     new int[] { now + 200, now + 200, now + 200 },
        ///     new float[] { start[0] - step, start[1] - step, start[2] - step });
        ///
        /// // 別の使用例(上の予約と続けて実行しない): 左1秒後→右2秒後→中3秒後に保持を解除する。
        /// // 通常回転の再開には各isRollingReelがtrueであることが必要。
        /// SendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollWithGoal),
        ///     new int[] { now, now, now }, start,
        ///     new int[] { now + 1000, now + 3000, now + 2000 }, start);
        /// </code>
        /// </example>
        [NetworkCallable]
        public void scheduleRollWithGoal(int[] rollStartTime, float[] rollStartAngle, int[] rollEndTime, float[] rollGoalAngle)
        {
            int index = AddScheduledVariableArray();
            int[] startTimes = ResolveStartTimes(rollStartTime);
            float[] startAngles = ResolveStartAngles(rollStartAngle);
            // 方向0は最短経路
            SetScheduledRoll(index, 0, startTimes, ResolveEndTimes(rollEndTime, startTimes),
                startAngles, ResolveGoalAngles(rollGoalAngle, startAngles), new float[3], new int[3]);
        }

        /// <summary>
        /// 回転方向をリールごとに指定して、目標角度へ動かす予約を追加する。
        /// 時刻・キュー・保持中の震えの扱いはscheduleRollWithGoalと同じ。
        /// ローカル動作のため、全員に反映する場合はSendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollWithGoalDirection), ...)で呼ぶ。
        /// </summary>
        /// <param name="rollStartTime">[左, 中, 右]の開始絶対時刻(ms)。nullなら呼び出し時点。</param>
        /// <param name="rollStartAngle">[左, 中, 右]の開始角度(度)。nullなら呼び出し時点の角度。</param>
        /// <param name="rollEndTime">[左, 中, 右]の終了絶対時刻(ms)。nullなら各開始時刻の1000ms後。</param>
        /// <param name="rollGoalAngle">[左, 中, 右]の目標絶対角度(度)。nullなら開始角度。</param>
        /// <param name="rollDirection">[左, 中, 右]の方向。-1は角度減少(上)、+1は角度増加(下)。null・0・正の値は+1扱い。</param>
        /// <remarks>
        /// 最短経路より指定方向を優先する。350→10で+1なら+20度、-1なら-340度回転する。
        /// 同じ角度を渡すと保持になる。1周させる場合は開始角度に方向に応じて360度を加減して渡す。
        /// 複数周も同様に、指定方向と一致する符号で目標角度を作る。
        /// </remarks>
        /// <example>
        /// <code>
        /// // 1秒かけて全リールを上方向へ1周させる。
        /// int now = Networking.GetServerTimeInMilliseconds();
        /// float[] start = new float[] { GetCurrentReelAngle(0), GetCurrentReelAngle(1), GetCurrentReelAngle(2) };
        /// SendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollWithGoalDirection),
        ///     new int[] { now, now, now }, start,
        ///     new int[] { now + 1000, now + 1000, now + 1000 },
        ///     new float[] { start[0] - 360f, start[1] - 360f, start[2] - 360f },
        ///     new int[] { -1, -1, -1 });
        /// </code>
        /// </example>
        [NetworkCallable]
        public void scheduleRollWithGoalDirection(int[] rollStartTime, float[] rollStartAngle, int[] rollEndTime, float[] rollGoalAngle, int[] rollDirection)
        {
            int index = AddScheduledVariableArray();
            int[] startTimes = ResolveStartTimes(rollStartTime);
            float[] startAngles = ResolveStartAngles(rollStartAngle);
            SetScheduledRoll(index, 0, startTimes, ResolveEndTimes(rollEndTime, startTimes),
                startAngles, ResolveGoalAngles(rollGoalAngle, startAngles), new float[3], NormalizeDirectionArray3(rollDirection));
        }

        /// <summary>
        /// 指定期間、リールごとに一定速度で回す予約を追加する。目標角度の指定は不要。
        /// 時刻・キュー・終了後の通常制御への復帰はscheduleRollWithGoalと同じ。
        /// ローカル動作のため、全員に反映する場合はSendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollWithSpeed), ...)で呼ぶ。
        /// </summary>
        /// <param name="rollStartTime">[左, 中, 右]の開始絶対時刻(ms)。nullなら呼び出し時点。</param>
        /// <param name="rollEndTime">[左, 中, 右]の終了絶対時刻(ms)。nullなら各開始時刻の1000ms後。</param>
        /// <param name="rollSpeed">[左, 中, 右]の速度(度/ms)。正は下方向、負は上方向。nullなら全リール0。度/秒を使う場合は1000で割る。</param>
        /// <remarks>
        /// 0.36なら1秒で+360度、-0.36なら1秒で-360度回る。
        /// 基準角度はその予約を最初に適用するフレームで取得し、開始時刻からの経過時間×速度を加える。
        /// 速度0でも保持できるが、この予約方式では疑似遊技中のぷるぷる判定は行わない。
        /// 震える保持演出にはscheduleRollWithGoalで開始角度と目標角度を同じにする。
        /// </remarks>
        /// <example>
        /// <code>
        /// // 2秒間、左は下方向に毎秒1周、中は保持、右は上方向に毎秒1周。
        /// int now = Networking.GetServerTimeInMilliseconds();
        /// SendCustomNetworkEvent(NetworkEventTarget.All, nameof(scheduleRollWithSpeed),
        ///     new int[] { now, now, now },
        ///     new int[] { now + 2000, now + 2000, now + 2000 },
        ///     new float[] { 0.36f, 0f, -0.36f });
        /// </code>
        /// </example>
        [NetworkCallable]
        public void scheduleRollWithSpeed(int[] rollStartTime, int[] rollEndTime, float[] rollSpeed)
        {
            int index = AddScheduledVariableArray();
            int[] startTimes = ResolveStartTimes(rollStartTime);
            float[] speeds = NormalizeFloatArray3(rollSpeed, 0f);
            int[] directions = new int[]
            {
                speeds[0] < 0f ? -1 : 1,
                speeds[1] < 0f ? -1 : 1,
                speeds[2] < 0f ? -1 : 1
            };
            SetScheduledRoll(index, 1, startTimes, ResolveEndTimes(rollEndTime, startTimes),
                GetCurrentReelAngles(), GetCurrentReelAngles(), speeds, directions);
        }

        private void SetScheduledRoll(int index, int type, int[] startTimes, int[] endTimes, float[] startAngles, float[] goalAngles, float[] speeds, int[] directions)
        {
            scheduledRollType[index] = type;
            scheduledRollStartTime[index] = startTimes;
            scheduledRollEndTime[index] = endTimes;
            scheduledRollStartAngle[index] = startAngles;
            scheduledRollGoalAngle[index] = goalAngles;
            scheduledRollSpeed[index] = speeds;
            scheduledRollDirection[index] = directions;
        }

        /// <summary>nullや不足分は呼び出し時点の時刻で補う。</summary>
        private int[] ResolveStartTimes(int[] rollStartTime)
        {
            return NormalizeIntArray3(rollStartTime, Networking.GetServerTimeInMilliseconds());
        }

        /// <summary>nullなら各開始時刻の1000ms後、不足分は左リール開始の1000ms後で補う。</summary>
        private int[] ResolveEndTimes(int[] rollEndTime, int[] startTimes)
        {
            return rollEndTime != null ? NormalizeIntArray3(rollEndTime, startTimes[0] + 1000) : CreateDefaultEndTimes(startTimes);
        }

        private float[] ResolveStartAngles(float[] rollStartAngle)
        {
            return rollStartAngle != null ? NormalizeFloatArray3(rollStartAngle, 0f) : GetCurrentReelAngles();
        }

        private float[] ResolveGoalAngles(float[] rollGoalAngle, float[] startAngles)
        {
            return rollGoalAngle != null ? NormalizeFloatArray3(rollGoalAngle, 0f) : startAngles;
        }

        private int GetScheduledDirection(int reelIndex)
        {
            if (scheduledRollDirection.Length == 0) return 1;

            int[] directions = scheduledRollDirection[0];
            if (directions == null || directions.Length <= reelIndex) return 1;

            if (directions[reelIndex] == 0) return 0;
            return directions[reelIndex] < 0 ? -1 : 1;
        }
        
    }
}
