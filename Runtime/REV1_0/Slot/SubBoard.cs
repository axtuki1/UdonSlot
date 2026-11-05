
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

        /// <summary>演出エディタのパターンを再生する。無くてもよい。</summary>
        public EffectPlayer effectPlayer;

        [Header("払出")]
        [SerializeField, Tooltip("1枚あたりの秒数。0で一度に表示")]
        protected float payoutCountInterval = 0.08f;

        /// <summary>払出セグに出している枚数（ローカルで数える）。</summary>
        protected int payoutCount = 0;
        /// <summary>今回の払出の合計枚数。</summary>
        protected int payoutTotal = 0;
        /// <summary>払出を数えている途中か。</summary>
        protected bool isPayoutCounting = false;
        private float payoutStartTime = 0f;

        /// <summary>このゲームで止まったリールの数（ローカルで数える）。</summary>
        private int effectStopCount = 0;
        /// <summary>最後に見たゲーム開始時刻。変わったらリール回転開始（ネットワークを増やさず同期変数で知る）。</summary>
        private float effectSeenGameStart = 0f;
        private bool effectGameStartSeen = false;
        
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

        /// <summary>指定リール（左から0～2）の上下照明色を設定する。HDR値対応。ローカル。</summary>
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

        // ---- 点灯・照明のシナリオ ----

        private int[] symbolSequencePatterns;
        private int[] symbolSequenceDurations;
        private int symbolSequenceStep = -1;
        private float symbolSequenceStepEnd;

        private Color[] colorSequenceUpper;
        private Color[] colorSequenceLower;
        private int[] colorSequenceDurations;
        private int colorSequenceStep = -1;
        private float colorSequenceStepEnd;

        /// <summary>
        /// 表示窓9コマの点灯状態を、時間つきで順に切り替える。最後の状態のまま終わる。ローカル。
        /// 再生中にもう一度呼ぶと、新しいほうに差し替わる。
        /// </summary>
        /// <param name="patterns">各段階の点灯状態（SetReelSymbolLightPattern と同じ9ビット）</param>
        /// <param name="durations">各段階を続ける長さ(ms)。patterns と同じ長さ</param>
        /// <example>
        /// <code>
        /// // 左から右へ消灯 → 右から左へ点灯（各100ms）
        /// PlayReelSymbolLightSequence(
        ///     new int[] { 0x1F8, 0x1C0, 0x000, 0x1C0, 0x1F8, 0x1FF },
        ///     new int[] { 100, 100, 100, 100, 100, 100 });
        /// </code>
        /// </example>
        public void PlayReelSymbolLightSequence(int[] patterns, int[] durations)
        {
            if (patterns == null || durations == null || patterns.Length == 0 || patterns.Length != durations.Length) return;
            symbolSequencePatterns = patterns;
            symbolSequenceDurations = durations;
            symbolSequenceStep = 0;
            SetReelSymbolLightPattern(patterns[0]);
            symbolSequenceStepEnd = Time.time + Mathf.Max(0, durations[0]) / 1000f;
        }

        /// <summary>表示窓の点灯シナリオを止める。点灯状態はその時点のまま。</summary>
        public void StopReelSymbolLightSequence()
        {
            symbolSequenceStep = -1;
        }

        /// <summary>表示窓の点灯シナリオを再生中か。</summary>
        public bool IsReelSymbolLightSequencePlaying()
        {
            return symbolSequenceStep >= 0;
        }

        /// <summary>
        /// リールの上下照明の色を、時間つきで順に切り替える。最後の色のまま終わる。ローカル。
        /// 再生中にもう一度呼ぶと、新しいほうに差し替わる。
        /// </summary>
        /// <param name="upperColors">各段階の上側の色。durations と同じ長さなら3リール共通、3倍の長さなら [段階0左, 中, 右, 段階1左, ...]</param>
        /// <param name="lowerColors">各段階の下側の色。並びは upperColors と同じ。null なら upperColors と同じ色</param>
        /// <param name="durations">各段階を続ける長さ(ms)</param>
        public void PlayReelLightColorSequence(Color[] upperColors, Color[] lowerColors, int[] durations)
        {
            if (upperColors == null || durations == null || durations.Length == 0) return;
            if (lowerColors == null) lowerColors = upperColors;
            if (upperColors.Length != lowerColors.Length) return;
            if (upperColors.Length != durations.Length && upperColors.Length != durations.Length * 3) return;
            colorSequenceUpper = upperColors;
            colorSequenceLower = lowerColors;
            colorSequenceDurations = durations;
            colorSequenceStep = 0;
            ApplyColorSequenceStep();
            colorSequenceStepEnd = Time.time + Mathf.Max(0, durations[0]) / 1000f;
        }

        /// <summary>リール別版。色を [リール][段階] の配列で渡す。</summary>
        /// <param name="upperColors">[リール(左0～右2)][段階] の上側の色。各リールの段階数は durations と同じ</param>
        /// <param name="lowerColors">下側の色。並びは upperColors と同じ。null なら upperColors と同じ色</param>
        /// <param name="durations">各段階を続ける長さ(ms)</param>
        /// <example>
        /// <code>
        /// // 左から順に赤くして、最後に全部白へ
        /// PlayReelLightColorSequence(new Color[][] {
        ///     new Color[] { Color.red,   Color.red,   Color.red,   Color.white },
        ///     new Color[] { Color.white, Color.red,   Color.red,   Color.white },
        ///     new Color[] { Color.white, Color.white, Color.red,   Color.white },
        /// }, null, new int[] { 100, 100, 300, 100 });
        /// </code>
        /// </example>
        public void PlayReelLightColorSequence(Color[][] upperColors, Color[][] lowerColors, int[] durations)
        {
            if (upperColors == null || upperColors.Length != 3 || durations == null) return;
            if (lowerColors == null) lowerColors = upperColors;
            if (lowerColors.Length != 3) return;
            int steps = durations.Length;
            Color[] upper = new Color[steps * 3];
            Color[] lower = new Color[steps * 3];
            for (int reel = 0; reel < 3; reel++)
            {
                if (upperColors[reel] == null || lowerColors[reel] == null) return;
                if (upperColors[reel].Length != steps || lowerColors[reel].Length != steps) return;
                for (int step = 0; step < steps; step++)
                {
                    upper[step * 3 + reel] = upperColors[reel][step];
                    lower[step * 3 + reel] = lowerColors[reel][step];
                }
            }
            PlayReelLightColorSequence(upper, lower, durations);
        }

        /// <summary>上下照明の色のシナリオを止める。色はその時点のまま。</summary>
        public void StopReelLightColorSequence()
        {
            colorSequenceStep = -1;
        }

        /// <summary>上下照明の色のシナリオを再生中か。</summary>
        public bool IsReelLightColorSequencePlaying()
        {
            return colorSequenceStep >= 0;
        }

        private void ApplyColorSequenceStep()
        {
            bool perReel = colorSequenceUpper.Length != colorSequenceDurations.Length;
            for (int i = 0; i < 3; i++)
            {
                int index = perReel ? colorSequenceStep * 3 + i : colorSequenceStep;
                SetReelLightColors(i, colorSequenceUpper[index], colorSequenceLower[index]);
            }
        }

        private void UpdateLightSequences()
        {
            float now = Time.time;
            while (symbolSequenceStep >= 0 && now >= symbolSequenceStepEnd)
            {
                symbolSequenceStep++;
                if (symbolSequenceStep >= symbolSequencePatterns.Length)
                {
                    symbolSequenceStep = -1;
                    break;
                }
                SetReelSymbolLightPattern(symbolSequencePatterns[symbolSequenceStep]);
                symbolSequenceStepEnd += Mathf.Max(0, symbolSequenceDurations[symbolSequenceStep]) / 1000f;
            }
            while (colorSequenceStep >= 0 && now >= colorSequenceStepEnd)
            {
                colorSequenceStep++;
                if (colorSequenceStep >= colorSequenceDurations.Length)
                {
                    colorSequenceStep = -1;
                    break;
                }
                ApplyColorSequenceStep();
                colorSequenceStepEnd += Mathf.Max(0, colorSequenceDurations[colorSequenceStep]) / 1000f;
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
                // 止まっているリールはマテリアルに書き込まない。
                if (deltaAngle == 0f) continue;
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
        
        /// <summary>機種固有の処理を実装する拡張ポイント。ローカルで毎フレーム、リール照明の更新後に呼ばれる。</summary>
        public virtual void LateUpdateLogic(){}
        
        /// <summary>機種固有の初期化を実装する拡張ポイント。ローカルで開始時に1回呼ばれる。</summary>
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
            RestoreStateOnJoin();
            NotifyEffectReelStart();
            UpdateLightSequences();
            UpdatePayoutCount();
            UpdateLogic();
        }

        /// <summary>
        /// 途中参加して状態を復元している最中か。BonusStartLogicなどの中で、ファンファーレのような一度きりの音を
        /// 鳴らさないために使う。
        /// </summary>
        protected bool isRestoring = false;
        private bool isStateRestored = false;

        /// <summary>
        /// 途中参加したとき、最初に同期された状態に合わせて演出の状態を戻す。
        /// 作動中のボーナス・JACゲーム・RTの開始フックを、自分の画面だけで呼び直す。
        /// 成立中（未作動）のボーナスは告知の有無が分からないので戻さない。
        /// </summary>
        private void RestoreStateOnJoin()
        {
            if (isStateRestored || !Utilities.IsValid(mainBoard)) return;
            ReelOutputController controller = mainBoard.reelOutputController;
            if (!Utilities.IsValid(controller)) { isStateRestored = true; return; }
            if (controller.IsOwner()) { isStateRestored = true; return; }
            if (!controller.HasReceivedState()) return;
            isStateRestored = true;
            isRestoring = true;
            if (controller.GetStateIndex() != 0) StateChangeLogic(controller.GetStateName());
            if (controller.IsBonusActive())
            {
                BonusStartLogic(controller.GetActiveBonusName());
                string child = controller.GetActiveChildBonusName();
                if (child.Length > 0) JACGameStartLogic(child);
            }
            isRestoring = false;
        }

        /// <summary>ウェイト中は残り時間のバーを表示する。ウェイト明けの表示はゲーム開始まで維持する。</summary>
        private void UpdateWaitView()
        {
            if (!Utilities.IsValid(waitWrapper)) return;
            if (!Utilities.IsValid(waitValue) || !mainBoard.requestGameStart)
            {
                if (waitWrapper.activeSelf) waitWrapper.SetActive(false);
                return;
            }

            float wait = mainBoard.GetLastGameStartElapsed() / (4.1f * 1000f);
            if (wait > 1f) return;
            waitValue.localScale = new Vector3(Mathf.Clamp01(wait), 1f, 1f);
            if (!waitWrapper.activeSelf) waitWrapper.SetActive(true);
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
            SetEmissionColor(mat, emissionColor);

            StopButtonEffectLogic(reelIndex, stopButton, mat, isRolling);
        }
        
        /// <summary>色が変わるときだけ書き込む（マテリアルの更新を毎フレーム起こさない）。</summary>
        private void SetEmissionColor(Material mat, Color color)
        {
            if (mat.GetColor("_EmissionColor") != color) mat.SetColor("_EmissionColor", color);
        }

        public virtual void StopButtonEffectLogic(int reelIndex, Renderer stopButton, Material mat, bool isRolling){}
        
        public void BetButtonEffect()
        {
            Material mat = betButton.material;
            int maxBet = mainBoard.GetMaxBet();
            bool isCanBetting = maxBet > 0 && mainBoard.bet < maxBet && mainBoard.isControllable;
            SetEmissionColor(mat, isCanBetting ? betButtonActiveColor : betButtonInactiveColor);
            BetButtonEffectLogic(betButton, mat, isCanBetting);
        }
        
        /// <summary>機種固有のMaxBetボタン表示を実装する拡張ポイント。ローカルで毎フレーム呼ばれる。</summary>
        /// <param name="betButton">MaxBetボタンのレンダラー</param>
        /// <param name="mat">マテリアル</param>
        /// <param name="isCanBetting">ベット可能かどうか</param>
        public virtual void BetButtonEffectLogic(Renderer betButton, Material mat, bool isCanBetting)
        { }
        
        /// <summary>機種固有の処理を実装する拡張ポイント。ローカルで毎フレーム呼ばれる。</summary>
        public virtual void UpdateLogic(){}

        /// <summary>メイン基板のゲーム開始時刻が変わったら、演出へリール回転開始を伝える。ローカル。</summary>
        private void NotifyEffectReelStart()
        {
            if (!Utilities.IsValid(mainBoard)) return;
            float start = mainBoard.gameStartTime;
            if (!effectGameStartSeen)
            {
                // 参加した時点のゲームは数えない。
                effectGameStartSeen = true;
                effectSeenGameStart = start;
                return;
            }
            if (start == effectSeenGameStart) return;
            effectSeenGameStart = start;
            // 同期が遅れて先に停止が届いていたら、このゲームの回転開始は流さない。
            if (!mainBoard.isGaming || effectStopCount > 0) return;
            if (Utilities.IsValid(effectPlayer)) effectPlayer.OnStep(EffectPlayer.StepReelStart, false);
        }

        public virtual void FixedUpdate()
        {
            FixedUpdateLogic();
        }
        
        /// <summary>機種固有の処理を実装する拡張ポイント。ローカルで物理更新ごとに呼ばれる。</summary>
        public virtual void FixedUpdateLogic(){}
        
        /// <summary>レバーオンを全員へ通知する。</summary>
        /// <param name="flag">今回の成立フラグ（ボーナスを除いたもの）</param>
        [NetworkCallable]
        public void LeverOn(string flag)
        {
            gameFlag = flag != null ? flag : "";
            gameWinningNames = new string[0];
            gamePayout = 0;
            effectStopCount = 0;
            EndPayoutCount();
            if (Utilities.IsValid(effectPlayer)) effectPlayer.OnStep(EffectPlayer.StepLeverOn, false);
            LeverOnLogic(flag);
        }

        /// <summary>機種固有のレバーオン演出を実装する拡張ポイント。全員の画面で呼ばれる。gameFlagでも今回のフラグを読める。</summary>
        /// <param name="flag">今回の成立フラグ（ボーナスを除いたもの）</param>
        public virtual void LeverOnLogic(string flag) { }

        [NetworkCallable]
        public void GameStart()
        {
            GameStartLogic();
        }

        /// <summary>機種固有のゲーム開始（リール回転開始）の音・演出を実装する拡張ポイント。全員の画面で呼ばれる。基底の音はメイン基板の Owner だけが鳴らす（全員に聞こえる）。</summary>
        public virtual void GameStartLogic()
        {
            if (!CanPlaySound()) return;
            soundBoard.StopSound(SoundType.WAIT);
            soundBoard.PlaySound(SoundType.SE1, (int)ClipType.START);
        }

        /// <summary>今回（GameEnd後は直前）のゲームの成立フラグ（ボーナスを除いたもの）。LeverOnLogic・GameEndLogicの中で使う。</summary>
        protected string gameFlag = "";

        /// <summary>直前のゲームの入賞役の内部名。</summary>
        protected string[] gameWinningNames = new string[0];

        /// <summary>直前のゲームの払出枚数。</summary>
        protected int gamePayout = 0;

        [NetworkCallable]
        public void GameEnd(string flag, string[] winningNames, int payout)
        {
            gameFlag = flag != null ? flag : "";
            gameWinningNames = winningNames != null ? winningNames : new string[0];
            gamePayout = payout;
            StartPayoutCount(payout);
            if (Utilities.IsValid(effectPlayer)) effectPlayer.OnStep(EffectPlayer.StepGameEnd, gameWinningNames.Length > 0);
            GameEndLogic();
        }

        /// <summary>払出セグに出す枚数。数えている途中ならその枚数、それ以外は payout をそのまま返す。</summary>
        /// <param name="payout">メイン基板の直近の払出</param>
        public int GetViewingPayout(int payout)
        {
            return isPayoutCounting ? payoutCount : payout;
        }

        /// <summary>払出を1枚ずつ数え始める。持ち玉はメイン基板ですでに増えている。</summary>
        private void StartPayoutCount(int payout)
        {
            EndPayoutCount();
            if (payout <= 0) return;
            payoutTotal = payout;
            if (payoutCountInterval <= 0f)
            {
                payoutCount = payout;
                PayoutLogic(payoutCount, payoutTotal);
                PayoutEndLogic();
                return;
            }
            payoutCount = 0;
            payoutStartTime = Time.time;
            isPayoutCounting = true;
            UpdatePayoutCount();
        }

        private void UpdatePayoutCount()
        {
            if (!isPayoutCounting) return;
            // 1枚目はすぐ、以降は payoutCountInterval ごとに1枚。
            int target = Mathf.Min(payoutTotal, Mathf.FloorToInt((Time.time - payoutStartTime) / payoutCountInterval) + 1);
            while (payoutCount < target)
            {
                payoutCount++;
                PayoutLogic(payoutCount, payoutTotal);
            }
            if (payoutCount >= payoutTotal)
            {
                isPayoutCounting = false;
                PayoutEndLogic();
            }
        }

        /// <summary>数えている途中なら残りを飛ばして終える（BET・レバーオン・リセット時）。</summary>
        private void EndPayoutCount()
        {
            if (!isPayoutCounting) return;
            isPayoutCounting = false;
            payoutCount = payoutTotal;
            PayoutEndLogic();
        }

        /// <summary>機種固有の払出音・表示を実装する拡張ポイント。1枚数えるごとに全員の画面で呼ばれる。基底は払い出し音をローカルで鳴らす。</summary>
        /// <param name="count">数え終わった枚数</param>
        /// <param name="total">今回の払出の合計枚数</param>
        public virtual void PayoutLogic(int count, int total)
        {
            if (!Utilities.IsValid(soundBoard)) return;
            soundBoard.PlayLocalSound(SoundType.SE1, (int)ClipType.PAYOUT);
        }

        /// <summary>機種固有の払出終了時の処理を実装する拡張ポイント。数え終わったとき、または途中で打ち切られたときに全員の画面で呼ばれる。</summary>
        public virtual void PayoutEndLogic() { }

        public virtual void GameEndLogic()
        {
            
        }

        [NetworkCallable]
        public void ReelStop(int reelIndex, int stopIndex, float accuracy, string[] possibleRoles)
        {
            if (possibleRoles == null) possibleRoles = new string[0];
            if (Utilities.IsValid(effectPlayer) && effectStopCount < 3)
            {
                effectStopCount++;
                // 第三停止では possibleRoles が揃った役になる。
                effectPlayer.OnReelStep(EffectPlayer.StepFirstStop + effectStopCount - 1, effectStopCount == 3 && possibleRoles.Length > 0, reelIndex);
            }
            ReelStopLogic(reelIndex, stopIndex, accuracy, possibleRoles);
        }

        /// <summary>リール停止時の演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="reelIndex">止まったリール（左0～右2）</param>
        /// <param name="stopIndex">押した位置（滑る前の下段の図柄、0始まり）</param>
        /// <param name="accuracy">押したタイミングの正確さ。図柄ちょうどで1、半コマずれで0.5</param>
        /// <param name="possibleRoles">ここまでに止まったリールの図柄で、まだ揃う可能性がある役の内部名（テンパイ音などに使う）。全リール停止時は実際に揃った役</param>
        public virtual void ReelStopLogic(int reelIndex, int stopIndex, float accuracy, string[] possibleRoles)
        {
            if (!CanPlaySound()) return;
            soundBoard.StopSound(SoundType.WAIT);
            // 演出パターンが自分の音を聞かせたいときは鳴らさない。
            if (Utilities.IsValid(effectPlayer) && effectPlayer.IsStopSoundMuted()) return;
            soundBoard.PlaySound(SoundType.SE1, (int)ClipType.STOP);
        }

        [NetworkCallable]
        public void Bet(int betCount)
        {
            EndPayoutCount();
            BetLogic(betCount);
        }

        /// <summary>機種固有のBET音・表示を実装する拡張ポイント。全員の画面で呼ばれる。基底の音はメイン基板の Owner だけが鳴らす（全員に聞こえる）。</summary>
        /// <param name="betCount">今回BETした枚数</param>
        public virtual void BetLogic(int betCount)
        {
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

        /// <summary>機種固有の再遊技音やBET表示演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="betCount">再装填された枚数</param>
        public virtual void ReplayBetLogic(int betCount) { }

        /// <summary>停止ボタンが押されたことを全員へ通知する。ゲーム中でなくても届く。</summary>
        [NetworkCallable]
        public void PushStopButton(int reelIndex)
        {
            PushStopButtonLogic(reelIndex);
        }

        /// <summary>
        /// 機種固有の停止ボタンを押したときの処理を実装する拡張ポイント。全員の画面で呼ばれる。
        /// ゲーム中かどうかは mainBoard.isGaming で分かる。リールが止まるときは、この後に ReelStopLogic が呼ばれる。
        /// </summary>
        /// <param name="reelIndex">押したボタンのリール（左0～右2）</param>
        public virtual void PushStopButtonLogic(int reelIndex) { }

        /// <summary>停止ボタンが離されたことを全員へ通知する。</summary>
        [NetworkCallable]
        public void ReleaseStopButton(int reelIndex)
        {
            ReleaseStopButtonLogic(reelIndex);
        }

        /// <summary>機種固有の停止ボタンを離したときの演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="reelIndex">離したボタンのリール（左0～右2）</param>
        public virtual void ReleaseStopButtonLogic(int reelIndex) { }

        /// <summary>センターボタンが押されたことを全員へ通知する。</summary>
        [NetworkCallable]
        public void PushCenterButton()
        {
            PushCenterButtonLogic();
        }

        /// <summary>機種固有のセンターボタン演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        public virtual void PushCenterButtonLogic() { }

        /// <summary>センターボタンが離されたことを全員へ通知する。</summary>
        [NetworkCallable]
        public void ReleaseCenterButton()
        {
            ReleaseCenterButtonLogic();
        }

        /// <summary>機種固有のセンターボタンを離したときの演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        public virtual void ReleaseCenterButtonLogic() { }
        
        [NetworkCallable]
        public void Credit()
        {
            CreditLogic();
        }
        
        /// <summary>機種固有のクレジット音を実装する拡張ポイント。全員の画面で呼ばれる。基底の音はメイン基板の Owner だけが鳴らす（全員に聞こえる）。</summary>
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
        
        /// <summary>機種固有のウェイト音を実装する拡張ポイント。全員の画面で呼ばれる。基底の音はメイン基板の Owner だけが鳴らす（全員に聞こえる）。</summary>
        public virtual void WaitLogic()
        {
            if (!CanPlaySound()) return;
            soundBoard.PlaySound(SoundType.WAIT, (int)ClipType.WAIT);
        }
        
        /// <summary>効果音は全員へ送信されるため、メイン基板の Owner だけが鳴らす。</summary>
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

        /// <summary>ボーナスが成立したことを通知する（レバーオン時）。</summary>
        [NetworkCallable]
        public void BonusWon(string bonusName)
        {
            BonusWonLogic(bonusName);
        }

        /// <summary>機種固有のボーナス成立演出（告知ランプなど）を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="bonusName">役物の内部名</param>
        public virtual void BonusWonLogic(string bonusName) { }

        /// <summary>BB・MB中に連続作動の役物（JACゲームなど）が作動したことを通知する。</summary>
        [NetworkCallable]
        public void JACGameStart(string bonusName)
        {
            JACGameStartLogic(bonusName);
        }

        /// <summary>BB・MB中に連続作動の役物（JACゲームなど）が作動したときの演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="bonusName">役物の内部名</param>
        public virtual void JACGameStartLogic(string bonusName) { }

        /// <summary>連続作動の役物が終わったことを通知する。</summary>
        [NetworkCallable]
        public void JACGameEnd(string bonusName)
        {
            JACGameEndLogic(bonusName);
        }

        /// <summary>連続作動の役物が終わったときの演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="bonusName">役物の内部名</param>
        public virtual void JACGameEndLogic(string bonusName) { }

        /// <summary>ボーナスが作動したことを通知する。</summary>
        [NetworkCallable]
        public void BonusStart(string bonusName)
        {
            BonusStartLogic(bonusName);
        }

        /// <summary>機種固有のボーナス開始演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="bonusName">役物の内部名</param>
        public virtual void BonusStartLogic(string bonusName) { }

        /// <summary>ボーナスが終了したことを通知する。</summary>
        [NetworkCallable]
        public void BonusEnd(string bonusName)
        {
            BonusEndLogic(bonusName);
        }

        /// <summary>機種固有のボーナス終了演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="bonusName">役物の内部名</param>
        public virtual void BonusEndLogic(string bonusName) { }

        /// <summary>状態（RT）が変わったことを通知する。</summary>
        [NetworkCallable]
        public void StateChange(string stateName)
        {
            StateChangeLogic(stateName);
        }

        /// <summary>機種固有の状態移行演出を実装する拡張ポイント。全員の画面で呼ばれる。</summary>
        /// <param name="stateName">移行先の状態の内部名</param>
        public virtual void StateChangeLogic(string stateName) { }

        /// <summary>機種のリセット（MainBoard.ResetMachine）を全員の画面へ伝える。演出を止めて表示を戻す。</summary>
        [NetworkCallable]
        public void ResetMachine()
        {
            if (Utilities.IsValid(effectPlayer)) effectPlayer.ResetAll();
            EndPayoutCount();
            StopReelSymbolLightSequence();
            StopReelLightColorSequence();
            ResetMachineLogic();
        }

        /// <summary>機種固有の表示（告知ランプ、液晶など）を初期状態に戻す拡張ポイント。全員の画面で呼ばれる。</summary>
        public virtual void ResetMachineLogic() { }

        public void NetworkStandby()
        {
            NetworkStandbyLogic();
        }

        public virtual void NetworkStandbyLogic() { }
    }
}
