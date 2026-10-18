
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// メイン基板の状態を受けて、ランプ、待機表示、効果音などの演出を制御するサブ基板。
    /// 機種固有の演出は各Logicメソッドをオーバーライドして実装する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SubBoard : UdonSharpBehaviour
    {
        protected virtual string _SUBBOARD_VERSION() => "UNVERSIONED";
        private string _UDONSLOT_SUBBOARD_VERSION = "1.0.0";
        
        /// <summary>
        /// サブ基板のバージョン情報。
        /// </summary>
        public string SUBBOARD_VERSION => _SUBBOARD_VERSION();
        
        /// <summary>
        /// UdonSlotのバージョン情報。
        /// </summary>
        public string UDONSLOT_SUBBOARD_VERSION => _UDONSLOT_SUBBOARD_VERSION;
        
        [SerializeField]
        protected MainBoard mainBoard;
        
        [SerializeField]
        protected Renderer leftStopButton, centerStopButton, rightStopButton;
        
        [SerializeField]
        [ColorUsage(true, true)]
        protected Color stopButtonInactiveColor = Color.red * 1.5f,
            stopButtonActiveColor = new Color(0, 255f/31,1) * 1.5f;
        
        [SerializeField]
        protected Renderer betButton;
        
        [SerializeField]
        [ColorUsage(true, true)]
        protected Color betButtonInactiveColor = new Color(0.5f, 0.5f, 0.5f),
            betButtonActiveColor = Color.red * 1.5f;
        
        [SerializeField]
        private GameObject waitWrapper;
        
        [SerializeField]
        private RectTransform waitValue;
        
        public SoundBoard soundBoard;
        
        protected VRCPlayerApi localPlayer;

        [Header("リール照明 (lilToon 発光2ndマスク)")]
        [SerializeField, Tooltip("リールの回転に合わせて発光2ndマスクのOffsetを補正します。")]
        private bool followReelEmissionMask = true;

        [SerializeField, Tooltip("UVの上下が逆のモデルでは -1 に変更してください。")]
        private float reelEmissionMaskDirection = 1f;

        private const string ReelEmissionMaskProperty = "_Emission2ndBlendMask";
        private const string ReelUpperLightColorProperty = "_ReelUpperLightColor";
        private const string ReelLowerLightColorProperty = "_ReelLowerLightColor";
        private const string ReelSymbolLightsProperty = "_ReelSymbolLights";
        private const string ReelSymbolWindowProperty = "_ReelSymbolWindow";
        private bool emissionMaterialsInitialized;
        private bool[] emissionMaskEnabled = new bool[3];
        private bool[] reelLightColorsSupported = new bool[3];
        private Transform[] emissionReels = new Transform[3];
        private Material[] emissionMaterials = new Material[3];
        private Vector2[] emissionMaskOffsets = new Vector2[3];
        private float[] emissionMaskScalesY = new float[3];
        private float[] emissionPreviousAngles = new float[3];
        private bool[] reelSymbolLightsSupported = new bool[3];
        private Vector4[] reelSymbolLights = new Vector4[3];
        private Vector4[] reelSymbolWindows = new Vector4[3];

        public void Start()
        {
            localPlayer = Networking.LocalPlayer;
            StartLogic();
            InitializeReelEmissionMasks();
        }

        private void InitializeReelEmissionMasks()
        {
            if (emissionMaterialsInitialized || !Utilities.IsValid(mainBoard)) return;
            emissionMaterialsInitialized = true;

            for (int i = 0; i < 3; i++)
            {
                Transform reel = mainBoard.GetReelTransform(i);
                if (!Utilities.IsValid(reel)) continue;
                Renderer reelRenderer = reel.GetComponent<Renderer>();
                if (!Utilities.IsValid(reelRenderer)) continue;
                Material shared = reelRenderer.sharedMaterial;
                if (!Utilities.IsValid(shared) || !shared.HasProperty(ReelEmissionMaskProperty)) continue;

                // 同じマテリアルを共有していても、各リールのOffsetは独立させる。
                Material material = reelRenderer.material;
                emissionReels[i] = reel;
                emissionMaterials[i] = material;
                emissionMaskEnabled[i] = material.GetTexture(ReelEmissionMaskProperty) != null;
                reelLightColorsSupported[i] = material.HasProperty(ReelUpperLightColorProperty)
                    && material.HasProperty(ReelLowerLightColorProperty);
                reelSymbolLightsSupported[i] = material.HasProperty(ReelSymbolLightsProperty)
                    && material.HasProperty(ReelSymbolWindowProperty);
                if (reelSymbolLightsSupported[i])
                {
                    reelSymbolLights[i] = material.GetVector(ReelSymbolLightsProperty);
                    Vector4 window = material.GetVector(ReelSymbolWindowProperty);
                    window.x = Mathf.Max(3, mainBoard.pictureCount);
                    reelSymbolWindows[i] = window;
                    material.SetVector(ReelSymbolWindowProperty, window);
                }
                emissionMaskOffsets[i] = material.GetTextureOffset(ReelEmissionMaskProperty);
                emissionMaskScalesY[i] = material.GetTextureScale(ReelEmissionMaskProperty).y;
                Vector3 direction = reel.localRotation * Vector3.right;
                emissionPreviousAngles[i] = Mathf.Atan2(direction.z, direction.y) * Mathf.Rad2Deg;
            }
        }

        /// <summary>指定リール（左から0～2）の上下照明色を設定する。HDR値対応、ローカル演出。</summary>
        public void SetReelLightColors(int reelIndex, Color upperColor, Color lowerColor)
        {
            if (!CanSetReelLightColor(reelIndex)) return;
            emissionMaterials[reelIndex].SetColor(ReelUpperLightColorProperty, upperColor);
            emissionMaterials[reelIndex].SetColor(ReelLowerLightColorProperty, lowerColor);
        }

        /// <summary>指定リールの上側の照明色のみ変更する。</summary>
        public void SetReelUpperLightColor(int reelIndex, Color color)
        {
            if (!CanSetReelLightColor(reelIndex)) return;
            emissionMaterials[reelIndex].SetColor(ReelUpperLightColorProperty, color);
        }

        /// <summary>指定リールの下側の照明色のみ変更する。</summary>
        public void SetReelLowerLightColor(int reelIndex, Color color)
        {
            if (!CanSetReelLightColor(reelIndex)) return;
            emissionMaterials[reelIndex].SetColor(ReelLowerLightColorProperty, color);
        }

        private bool CanSetReelLightColor(int reelIndex)
        {
            if (reelIndex < 0 || reelIndex >= 3) return false;
            InitializeReelEmissionMasks();
            return reelLightColorsSupported[reelIndex] && Utilities.IsValid(emissionMaterials[reelIndex]);
        }

        private bool CanSetReelSymbolLight(int reelIndex)
        {
            if (reelIndex < 0 || reelIndex >= 3) return false;
            InitializeReelEmissionMasks();
            return reelSymbolLightsSupported[reelIndex] && Utilities.IsValid(emissionMaterials[reelIndex]);
        }

        /// <summary>3リールすべての上下照明色を設定する。</summary>
        public void SetAllReelLightColors(Color upperColor, Color lowerColor)
        {
            for (int i = 0; i < 3; i++) SetReelLightColors(i, upperColor, lowerColor);
        }

        /// <summary>表示窓の1コマを点灯/消灯する。リール:左0～右2、段:上0・中1・下2。</summary>
        public void SetReelSymbolLight(int reelIndex, int rowIndex, bool enabled)
        {
            SetReelSymbolBrightness(reelIndex, rowIndex, enabled ? 1f : 0f);
        }

        /// <summary>表示窓の1コマのバックライト（発光1st）を0～1で指定する。通常の陰影・上下照明（発光2nd）は維持する。</summary>
        public void SetReelSymbolBrightness(int reelIndex, int rowIndex, float brightness)
        {
            if (rowIndex < 0 || rowIndex >= 3) return;
            if (!CanSetReelSymbolLight(reelIndex)) return;
            Vector4 lights = reelSymbolLights[reelIndex];
            brightness = Mathf.Clamp01(brightness);
            if (rowIndex == 0) lights.x = brightness;
            else if (rowIndex == 1) lights.y = brightness;
            else lights.z = brightness;
            reelSymbolLights[reelIndex] = lights;
            emissionMaterials[reelIndex].SetVector(ReelSymbolLightsProperty, lights);
        }

        /// <summary>指定リールの上・中・下の点灯状態をまとめて設定する。</summary>
        public void SetReelSymbolLights(int reelIndex, bool top, bool middle, bool bottom)
        {
            if (!CanSetReelSymbolLight(reelIndex)) return;
            Vector4 lights = new Vector4(top ? 1f : 0f, middle ? 1f : 0f, bottom ? 1f : 0f, 1f);
            reelSymbolLights[reelIndex] = lights;
            emissionMaterials[reelIndex].SetVector(ReelSymbolLightsProperty, lights);
        }

        /// <summary>表示窓9コマすべてを点灯/消灯する。</summary>
        public void SetAllReelSymbolLights(bool enabled)
        {
            for (int i = 0; i < 3; i++) SetReelSymbolLights(i, enabled, enabled, enabled);
        }

        /// <summary>9ビットの点灯パターン。bit0～2=左上中下、bit3～5=中央上中下、bit6～8=右上中下。</summary>
        public void SetReelSymbolLightPattern(int pattern)
        {
            for (int i = 0; i < 3; i++)
            {
                int bits = pattern >> (i * 3);
                SetReelSymbolLights(i, (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
            }
        }

        public virtual void LateUpdate()
        {
            // MainBoard.Update後の実回転を使い、停止時の揺れや逆回転にも追従する。
            for (int i = 0; i < 3; i++)
            {
                if (!Utilities.IsValid(emissionReels[i]) || !Utilities.IsValid(emissionMaterials[i])) continue;
                // Rx(angle) * Rz(90) の向きから角度を復元。Euler Xの折り返しを避ける。
                Vector3 direction = emissionReels[i].localRotation * Vector3.right;
                float angle = Mathf.Atan2(direction.z, direction.y) * Mathf.Rad2Deg;
                float deltaAngle = Mathf.DeltaAngle(emissionPreviousAngles[i], angle);
                emissionPreviousAngles[i] = angle;
                if (followReelEmissionMask && emissionMaskEnabled[i])
                {
                    Vector2 offset = emissionMaskOffsets[i];
                    // 1周分のUV移動 × マスクのTilingを打ち消す。
                    offset.y = Mathf.Repeat(offset.y - deltaAngle / 360f * emissionMaskScalesY[i] * reelEmissionMaskDirection, 1f);
                    emissionMaskOffsets[i] = offset;
                    emissionMaterials[i].SetTextureOffset(ReelEmissionMaskProperty, offset);
                }
                if (reelSymbolLightsSupported[i])
                {
                    // 1周期=リール1周。照明マスクのTilingや有効/無効とは独立して表示窓に固定する。
                    Vector4 window = reelSymbolWindows[i];
                    window.y = Mathf.Repeat(window.y - deltaAngle / 360f * reelEmissionMaskDirection, 1f);
                    reelSymbolWindows[i] = window;
                    emissionMaterials[i].SetVector(ReelSymbolWindowProperty, window);
                }
            }
            LateUpdateLogic();
        }
        
        public virtual void LateUpdateLogic(){}
        
        public virtual void StartLogic(){}
        
        public virtual void Update()
        {
            // 停止ボタン色制御
            StopButtonEffect(0, leftStopButton, mainBoard.isRollingReel1);
            StopButtonEffect(1, centerStopButton, mainBoard.isRollingReel2);
            StopButtonEffect(2, rightStopButton, mainBoard.isRollingReel3);
            // MaxBetボタン制御
            BetButtonEffect();
            UpdateWaitView();
            UpdateLogic();
        }

        /// <summary>ウェイト中は残り時間のバーを表示する。ウェイト明けの表示はゲーム開始まで維持する。</summary>
        private void UpdateWaitView()
        {
            if (!Utilities.IsValid(waitWrapper)) return;
            if (!Utilities.IsValid(waitValue) || !mainBoard.requestGameStart)
            {
                waitWrapper.SetActive(false);
                return;
            }

            float wait = mainBoard.GetLastGameStartElapsed() / (4.1f * 1000f);
            if (wait > 1f) return;
            waitValue.localScale = new Vector3(Mathf.Clamp01(wait), 1f, 1f);
            waitWrapper.SetActive(true);
        }

        public void StopButtonEffect(int reelIndex)
        {
            Renderer stopButton = null;
            bool isRolling = false;
            switch (reelIndex)
            {
                case 0:
                    stopButton = leftStopButton;
                    isRolling = mainBoard.isRollingReel1;
                    break;
                case 1:
                    stopButton = centerStopButton;
                    isRolling = mainBoard.isRollingReel2;
                    break;
                case 2:
                    stopButton = rightStopButton;
                    isRolling = mainBoard.isRollingReel3;
                    break;
            }
            StopButtonEffect(reelIndex, stopButton, isRolling);
        }

        public void StopButtonEffect(int reelIndex, Renderer stopButton, bool isRolling)
        {
            Material mat = stopButton.material;
            Color emissionColor;
            if (mainBoard.isGaming && mainBoard.isPushOrder && mainBoard.isControllable && !mainBoard.isPushOrderMiss)
            {
                // 押し順ナビ: 次に押すリールだけ点滅させる。
                emissionColor = reelIndex == mainBoard.nextOrderReelIndex
                    ? Color.white * 1.5f * Mathf.PingPong(Time.time * 4f, 0.8f)
                    : Color.black * 1.5f;
            }
            else
            {
                emissionColor = isRolling && mainBoard.isControllable ? stopButtonActiveColor : stopButtonInactiveColor;
            }
            mat.SetColor("_EmissionColor", emissionColor);

            StopButtonEffectLogic(reelIndex, stopButton, mat, isRolling);
        }
        
        public virtual void StopButtonEffectLogic(int reelIndex, Renderer stopButton, Material mat, bool isRolling){}
        
        public void BetButtonEffect()
        {
            Material mat = betButton.material;
            int maxBet = mainBoard.GetMaxBet();
            bool isCanBetting = maxBet > 0 && mainBoard.bet < maxBet && mainBoard.isControllable;
            mat.SetColor("_EmissionColor", isCanBetting ? betButtonActiveColor : betButtonInactiveColor);
            BetButtonEffectLogic(betButton, mat, isCanBetting);
        }
        
        /// <summary>
        /// MaxBetボタンの表示を定義する。
        /// </summary>
        /// <param name="betButton">MaxBetボタンのレンダラー</param>
        /// <param name="mat">マテリアル</param>
        /// <param name="isCanBetting">ベット可能かどうか</param>
        public virtual void BetButtonEffectLogic(Renderer betButton, Material mat, bool isCanBetting)
        { }
        
        public virtual void UpdateLogic(){}

        public virtual void FixedUpdate()
        {
            FixedUpdateLogic();
        }
        
        public virtual void FixedUpdateLogic(){}
        
        [NetworkCallable]
        public void LeverOn()
        {
            LeverOnLogic();
        }
        
        public virtual void LeverOnLogic()
        {
            if (!CanPlaySound()) return;
            soundBoard.StopSound(SoundType.WAIT);
            soundBoard.PlaySound(SoundType.SE1, (int)ClipType.START);
        }

        [NetworkCallable]
        public void GameStart()
        {
            GameStartLogic();
        }

        public virtual void GameStartLogic()
        {
            if (!CanPlaySound()) return;
            soundBoard.StopSound(SoundType.WAIT);
            soundBoard.PlaySound(SoundType.SE1, (int)ClipType.START);
        }

        [NetworkCallable]
        public void GameEnd()
        {
            GameEndLogic();
        }

        public virtual void GameEndLogic()
        {
            
        }

        [NetworkCallable]
        public void ReelStop(int reelIndex, int stopIndex, float accuracy)
        {
            ReelStopLogic(reelIndex, stopIndex, accuracy);
        }

        public virtual void ReelStopLogic(int reelIndex, int stopIndex, float accuracy)
        {
            if (!CanPlaySound()) return;
            soundBoard.StopSound(SoundType.WAIT);
            soundBoard.PlaySound(SoundType.SE1, (int)ClipType.STOP);
        }

        [NetworkCallable]
        public void Bet(int betCount)
        {
            BetLogic(betCount);
        }

        public virtual void BetLogic(int betCount)
        {
            Debug.Log($"SubBoard: Bet {betCount}");
            if (!CanPlaySound() || betCount <= 0) return;
            // 1枚目は即時、2枚目以降は0.13秒間隔でベット音を鳴らす。
            soundBoard.PlaySound(SoundType.SE1, (int)ClipType.BET);
            for (int i = 1; i < betCount; i++)
            {
                soundBoard.ScheduledPlaySound(SoundType.SE1, (int)ClipType.BET, 0.13f * i);
            }
        }

        /// <summary>リプレイ成立によって前回BETが再装填されたことを通知する。</summary>
        [NetworkCallable]
        public void ReplayBet(int betCount)
        {
            ReplayBetLogic(betCount);
        }

        /// <summary>機種固有の再遊技音やBET表示演出を実装する拡張ポイント。</summary>
        public virtual void ReplayBetLogic(int betCount) { }
        
        [NetworkCallable]
        public void Credit()
        {
            CreditLogic();
        }
        
        public virtual void CreditLogic()
        {
            if (!CanPlaySound()) return;
            soundBoard.PlaySound(SoundType.SE1, (int)ClipType.CREDIT);
        }

        [NetworkCallable]
        public void Wait()
        {
            WaitLogic();
        }
        
        public virtual void WaitLogic()
        {
            Debug.Log("SubBoard: Wait");
            if (!CanPlaySound()) return;
            soundBoard.PlaySound(SoundType.WAIT, (int)ClipType.WAIT);
        }
        
        /// <summary>効果音は全員へ送信されるため、メイン基板のオーナーだけが鳴らす。</summary>
        private bool CanPlaySound()
        {
            return Utilities.IsValid(soundBoard) && IsMainBoardOwner();
        }

        public bool IsMainBoardOwner()
        {
            return Networking.IsOwner(mainBoard.gameObject);
        }
        
        public bool IsSubBoardOwner()
        {
            return Networking.IsOwner(gameObject);
        }
        
        public void SetOwner()
        {
            if (!IsSubBoardOwner()) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        /// <summary>ボーナスが作動したことを通知する。</summary>
        [NetworkCallable]
        public void BonusStart(string bonusName)
        {
            BonusStartLogic(bonusName);
        }

        /// <summary>機種固有のボーナス開始演出を実装する拡張ポイント。引数は役物の内部名。</summary>
        public virtual void BonusStartLogic(string bonusName) { }

        /// <summary>ボーナスが終了したことを通知する。</summary>
        [NetworkCallable]
        public void BonusEnd(string bonusName)
        {
            BonusEndLogic(bonusName);
        }

        /// <summary>機種固有のボーナス終了演出を実装する拡張ポイント。引数は役物の内部名。</summary>
        public virtual void BonusEndLogic(string bonusName) { }

        /// <summary>状態（RT）が変わったことを通知する。</summary>
        [NetworkCallable]
        public void StateChange(string stateName)
        {
            StateChangeLogic(stateName);
        }

        /// <summary>機種固有の状態移行演出を実装する拡張ポイント。引数は移行先の状態の内部名。</summary>
        public virtual void StateChangeLogic(string stateName) { }

        public void NetworkStandby()
        {
            NetworkStandbyLogic();
        }

        public virtual void NetworkStandbyLogic() { }
    }
}
