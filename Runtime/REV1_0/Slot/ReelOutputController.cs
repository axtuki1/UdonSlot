
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 配当を優先するか、停止表示できる図柄組み合わせ数を優先するかを指定する。
    /// </summary>
    public enum ReelControlPriority
    {
        [InspectorName("枚数優先")]
        Payout,
        [InspectorName("組み合わせ数優先")]
        CombinationCount
    }

    /// <summary>複数役が同時当選したとき、自動滑りで先に引き込む役種別。</summary>
    public enum ReelRolePriority
    {
        [InspectorName("小役優先")]
        SmallRoleFirst,
        [InspectorName("ボーナス優先")]
        BonusFirst
    }

    /// <summary>
    /// 機種固有の抽選、リール制御、成立役判定、配当計算を実装するための基底クラス。
    /// MainBoardから遊技の各段階で呼び出される。
    /// </summary>
    public class ReelOutputController : UdonSharpBehaviour
    {
        [SerializeField]
        public ReelData[] reelDatas;
        
        [SerializeField]
        public MainBoard mainBoard;

        /// <summary>レバーON時のフラグ抽選に使用する抽選テーブル。</summary>
        [SerializeField]
        protected LotteryTable lotteryTable;

        /// <summary>成立役判定、BET別払出、リプレイ判定に使用する配当表。</summary>
        [SerializeField]
        protected PayoutTable payoutTable;

        /// <summary>自動滑り制御で、払出枚数と成立可能な組み合わせ数のどちらを優先するか。</summary>
        [SerializeField]
        protected ReelControlPriority priority = ReelControlPriority.Payout;

        /// <summary>自動滑り制御で、小役系とボーナスのどちらを優先するか。</summary>
        [SerializeField]
        protected ReelRolePriority rolePriority = ReelRolePriority.SmallRoleFirst;

        /// <summary>自動滑り制御で許可する最大滑りコマ数。</summary>
        [SerializeField]
        protected int maxSlipCount = 4;

        /// <summary>現在成立しているフラグの内部使用名。</summary>
        [UdonSynced]
        protected string activeFlag = "";
        
        /// <summary>
        /// 次回抽選時に使用するフラグの内部使用名。役強制時想定。
        /// レバーオン時に抽選の代わりにactiveFlagへコピーされる。
        /// null時は抽選を行う。
        /// </summary>
        [UdonSynced]
        private string nextFlag = "";
        
        [SerializeField]
        private ReelControl reelControl;
        
        protected bool isAllDynamicControl = false;
        
        /// <summary>
        /// レバーON時にLotteryTableからフラグを抽選し、結果を同期する。
        /// 抽選後、機種固有処理を追加するためのGenerateFlagLogicを呼び出す。
        /// </summary>
        /// <param name="bet">現在のBET数。</param>
        public virtual void GenerateFlag(int bet)
        {
            SetOwner();
            if (Utilities.IsValid(nextFlag) && nextFlag.Length > 0)
            {
                activeFlag = nextFlag;
                nextFlag = "";
            }
            else
            {
                activeFlag = Utilities.IsValid(lotteryTable) ? lotteryTable.DrawFlag(bet) : "";
            }

            GenerateFlagLogic(bet, activeFlag);
            RequestSerialization();
        }

        /// <summary>
        /// フラグ抽選後に機種固有処理を追加するための拡張ポイント。
        /// 既存のGenerateFlagLogic(int)オーバーライドも呼び出す。
        /// </summary>
        public virtual void GenerateFlagLogic(int bet, string activeFlag)
        {
            
        }

        /// <summary>
        /// 成立役に応じた後処理(ボーナス入賞時の状態移行等)を行う。
        /// </summary>
        public virtual void FlagPostProcess()
        {
            
        }

        /// <summary>
        /// 次回レバーオン時に強制的に設定するフラグを設定する。
        /// </summary>
        /// <param name="flag">設定するフラグの内部使用名。</param>
        public void ForceFlag(string flag)
        {
            SetOwner();
            nextFlag = flag;
            RequestSerialization();
        }

        /// <summary>
        /// 成立フラグに応じてリールの挙動を制御する関数。
        /// 例えば、チェリーが成立している場合は左リールにチェリーを引き込むために左リールの滑りコマ数を返す、など。
        /// </summary>
        /// <param name="reelIndex">リールのインデックス。左から0,1,2...</param>
        /// <param name="currentSymbol">現在リールの下段に表示されているシンボルのインデックス。0からpictureCount-1までの値。0ならシンボル0が表示されている。</param>
        /// <returns>リールの滑りコマ数</returns>
        public virtual int ControllReel(int reelIndex, int currentSymbol)
        {
            string flag = GetReelControlFlagName();
            string[] symbols = GetVisibleSymbols(reelIndex, currentSymbol);
            int stopReelCount = GetStoppedReelCount();
            
            Debug.Log($"制御名: {flag}");
            Debug.Log("リール停止数: " + stopReelCount);
            Debug.Log($"停止位置(内部値): {reelDatas[reelIndex].symbols[currentSymbol]}({currentSymbol})");
            Debug.Log($"滑り猶予圏内シンボルs: {string.Join(", ", symbols)}");

            // 該当するテーブル制御がなければコントロール制御で決める。
            int stopPatternIndex = ReelControlData.GetIndexForReelStopPattern(
                mainBoard.stoppedReelsStopOrder[0],
                mainBoard.stoppedReelsStopOrder[1],
                mainBoard.stoppedReelsStopOrder[2]
            );
            ReelControlData reelControlData = FindReelControlData(flag, stopPatternIndex);
            if (isAllDynamicControl || !IsTableControlPattern(reelControlData, stopPatternIndex))
            {
                return DynamicControl(symbols, currentSymbol, reelIndex, stopReelCount);
            }

            int conditionalSlip = reelControlData.GetConditionalSlip(
                stopPatternIndex,
                GetFinalLowerSymbolIndex(0),
                GetFinalLowerSymbolIndex(1),
                GetFinalLowerSymbolIndex(2),
                currentSymbol
            );
            if (conditionalSlip >= 0) return conditionalSlip;

            int[][] slipTable = reelControlData.reelStopSlipCount;
            if (slipTable == null ||
                slipTable.Length <= stopPatternIndex ||
                slipTable[stopPatternIndex] == null ||
                slipTable[stopPatternIndex].Length <= currentSymbol)
            {
                Debug.Log($"テーブル制御: パターン{stopPatternIndex}のリール{reelIndex}の停止位置({currentSymbol})の滑りコマ数の情報が存在しないため0を返す");
                return 0;
            }

            int slipCount = slipTable[stopPatternIndex][currentSymbol];
            Debug.Log($"テーブル制御: パターン{stopPatternIndex}のリール{reelIndex}の停止位置({currentSymbol})の滑りコマ数は{slipCount}");
            return slipCount;
        }

        /// <summary>制御名に対応するReelControlDataを返す。未設定・未登録ならnull。</summary>
        private ReelControlData FindReelControlData(string flag, int stopPatternIndex)
        {
            if (reelControl == null || reelControl.flagNames == null || reelControl.reelControlDataList == null)
            {
                Debug.Log("ReelControl が未設定");
                return null;
            }

            int dataIndex = Array.IndexOf(reelControl.flagNames, flag);
            if (dataIndex == -1 || dataIndex >= reelControl.reelControlDataList.Length)
            {
                Debug.Log("ReelControlData が見つからない");
                return null;
            }

            Debug.Log("ReelControlData が見つかりました: " + flag + "、停止パターンインデックス: " + stopPatternIndex);
            return reelControl.reelControlDataList[dataIndex];
        }

        private bool IsTableControlPattern(ReelControlData reelControlData, int stopPatternIndex)
        {
            if (!Utilities.IsValid(reelControlData)) return false;
            bool[] tableStyles = reelControlData.IsReelStopTableStyle;
            return tableStyles != null && tableStyles.Length > stopPatternIndex && tableStyles[stopPatternIndex];
        }

        /// <summary>停止済みリールの滑り後の下段位置。未停止なら-1。</summary>
        private int GetFinalLowerSymbolIndex(int reelIndex)
        {
            return mainBoard.stoppedReelsSymbolIndex[reelIndex] >= 0 ? GetStoppedLowerSymbolIndex(reelIndex) : -1;
        }

        private string[] GetVisibleSymbols(int reelIndex, int currentSymbol)
        {
            // 下段から上段、さらに枠上5コマまでを取得する。
            string[] symbols = new string[8];
            string[] reelSymbols = reelDatas[reelIndex].symbols;
            for (int i = 0; i < symbols.Length; i++)
            {
                int index = (currentSymbol - i) % reelSymbols.Length;
                if (index < 0) index += reelSymbols.Length;
                symbols[i] = reelSymbols[index];
            }

            return symbols;
        }

        private int GetStoppedReelCount()
        {
            int count = 0;
            for (int i = 0; i < mainBoard.stoppedReelsSymbolIndex.Length; i++)
            {
                if (mainBoard.stoppedReelsSymbolIndex[i] != -1) count++;
            }

            return count;
        }

        /// <summary>
        /// プログラムによって滑りコマ数を制御する関数。
        /// </summary>
        /// <param name="symbols">リールに表示されているシンボルの配列。</param>
        /// <param name="currentSymbol">現在リールの下段に表示されているシンボルのインデックス。0からpictureCount-1までの値。0ならシンボル0が表示されている。</param>
        /// <param name="reelIndex">リールのインデックス</param>
        /// <param name="stopReelCount">停止済リール数(当該リールは含まない)</param>
        /// <returns>リールの滑りコマ数。</returns>
        public virtual int DynamicControl(string[] symbols, int currentSymbol, int reelIndex, int stopReelCount)
        {
            if (!Utilities.IsValid(payoutTable)) return 0;
            return CalculatePrioritySlip(
                reelIndex,
                currentSymbol,
                payoutTable,
                lotteryTable,
                activeFlag,
                priority,
                maxSlipCount
            );
        }

        /// <summary>
        /// リール配列、有効ライン、配当表から、優先方式に従った滑りコマ数を算出する。
        /// </summary>
        /// <param name="reelIndex">今回停止するリール。左から0、1、2。</param>
        /// <param name="currentSymbol">滑り前に下段へ表示されている図柄インデックス。</param>
        /// <param name="activePayLines">有効ライン。各要素は左、中、右の表示段を0=上段、1=中段、2=下段で表す。</param>
        /// <param name="activeWinPairs">現在の抽選結果で入賞を許可するWinPairだけを格納した配当表。</param>
        /// <param name="priority">枚数優先または個数優先。</param>
        /// <returns>0から4までの滑りコマ数。</returns>
        public int CalculatePrioritySlip(
            int reelIndex,
            int currentSymbol,
            int[][] activePayLines,
            WinPair[] activeWinPairs,
            ReelControlPriority priority)
        {
            return CalculatePrioritySlip(
                reelIndex,
                currentSymbol,
                activePayLines,
                activeWinPairs,
                priority,
                4
            );
        }

        /// <summary>
        /// 最大滑りコマ数を指定して、優先方式に従った滑りコマ数を算出する。
        /// </summary>
        public int CalculatePrioritySlip(
            int reelIndex,
            int currentSymbol,
            int[][] activePayLines,
            WinPair[] activeWinPairs,
            ReelControlPriority priority,
            int maxSlipCount)
        {
            if (!Utilities.IsValid(mainBoard)) return 0;
            if (reelDatas == null || reelDatas.Length == 0) return 0;
            if (reelIndex < 0 || reelIndex >= reelDatas.Length) return 0;
            if (!Utilities.IsValid(reelDatas[reelIndex])) return 0;
            if (reelDatas[reelIndex].symbols == null || reelDatas[reelIndex].symbols.Length == 0) return 0;
            if (mainBoard.stoppedReelsSymbolIndex == null ||
                mainBoard.stoppedReelsSymbolIndex.Length < reelDatas.Length) return 0;
            if (mainBoard.stoppedReelsThroughSymbolCount == null ||
                mainBoard.stoppedReelsThroughSymbolCount.Length < reelDatas.Length) return 0;
            if (mainBoard.stoppedReelsStopOrder == null ||
                mainBoard.stoppedReelsStopOrder.Length < reelDatas.Length) return 0;
            if (activePayLines == null || activePayLines.Length == 0) return 0;
            if (activeWinPairs == null || activeWinPairs.Length == 0) return 0;

            int reelSymbolCount = reelDatas[reelIndex].symbols.Length;
            int normalizedCurrentSymbol = NormalizeSymbolIndex(currentSymbol, reelSymbolCount);
            int maximumSlip = Mathf.Clamp(maxSlipCount, 0, 4);
            int currentStopOrder = GetStoppedReelCount() + 1;

            int bestSlip = 0;
            int bestPayout = -1;
            int bestCombinationCount = -1;

            for (int slip = 0; slip <= maximumSlip; slip++)
            {
                int candidateLowerSymbol = NormalizeSymbolIndex(normalizedCurrentSymbol - slip, reelSymbolCount);
                int candidatePayout = 0;
                int candidateCombinationCount = 0;

                for (int pairIndex = 0; pairIndex < activeWinPairs.Length; pairIndex++)
                {
                    WinPair winPair = activeWinPairs[pairIndex];
                    if (!Utilities.IsValid(winPair)) continue;
                    if (!IsStopOrderPossible(winPair, reelIndex, currentStopOrder)) continue;

                    for (int lineIndex = 0; lineIndex < activePayLines.Length; lineIndex++)
                    {
                        int[] payLine = activePayLines[lineIndex];
                        if (!IsWinPairPossible(winPair, payLine, reelIndex, candidateLowerSymbol)) continue;

                        candidateCombinationCount++;
                        if (winPair.payout > candidatePayout) candidatePayout = winPair.payout;
                    }
                }

                if (!IsPriorityCandidateBetter(priority, candidatePayout, candidateCombinationCount, bestPayout, bestCombinationCount)) continue;
                bestSlip = slip;
                bestPayout = candidatePayout;
                bestCombinationCount = candidateCombinationCount;
            }

            return bestSlip;
        }

        private bool IsPriorityCandidateBetter(
            ReelControlPriority priority,
            int candidatePayout,
            int candidateCombinationCount,
            int bestPayout,
            int bestCombinationCount)
        {
            if (priority == ReelControlPriority.CombinationCount)
            {
                if (candidateCombinationCount != bestCombinationCount) return candidateCombinationCount > bestCombinationCount;
                return candidatePayout > bestPayout;
            }

            if (candidatePayout != bestPayout) return candidatePayout > bestPayout;
            return candidateCombinationCount > bestCombinationCount;
        }

        private bool IsStopOrderPossible(WinPair winPair, int targetReelIndex, int currentStopOrder)
        {
            if (winPair.isReelStopOrderAny) return true;
            if (winPair.reelStopOrder == null || winPair.reelStopOrder.Length < reelDatas.Length) return false;

            for (int reelIndex = 0; reelIndex < reelDatas.Length; reelIndex++)
            {
                int requiredOrder = winPair.reelStopOrder[reelIndex];
                if (reelIndex == targetReelIndex)
                {
                    if (requiredOrder != currentStopOrder) return false;
                    continue;
                }

                // 停止済みは実際の停止順、未停止は今後の停止順に収まるか確認する。
                int stoppedOrder = mainBoard.stoppedReelsStopOrder[reelIndex];
                if (stoppedOrder >= 1)
                {
                    if (requiredOrder != stoppedOrder) return false;
                }
                else if (requiredOrder <= currentStopOrder || requiredOrder > reelDatas.Length) return false;
            }

            return true;
        }

        private bool IsWinPairPossible(
            WinPair winPair,
            int[] payLine,
            int targetReelIndex,
            int candidateLowerSymbol)
        {
            if (payLine == null || payLine.Length < reelDatas.Length) return false;
            if (winPair.symbols == null || winPair.symbols.Length < reelDatas.Length) return false;

            for (int reelIndex = 0; reelIndex < reelDatas.Length; reelIndex++)
            {
                if (!Utilities.IsValid(reelDatas[reelIndex])) return false;
                if (reelDatas[reelIndex].symbols == null || reelDatas[reelIndex].symbols.Length == 0) return false;

                int row = payLine[reelIndex];
                if (row < 0 || row > 2) return false;

                string requiredSymbol = winPair.symbols[reelIndex];
                if (string.IsNullOrEmpty(requiredSymbol)) continue;

                int lowerSymbol = reelIndex == targetReelIndex ? candidateLowerSymbol : GetFinalLowerSymbolIndex(reelIndex);
                if (lowerSymbol >= 0)
                {
                    if (GetSymbolAtRow(reelIndex, lowerSymbol, row) != requiredSymbol) return false;
                }
                else if (!ReelContainsSymbol(reelIndex, requiredSymbol)) return false;
            }

            return true;
        }

        private int GetStoppedLowerSymbolIndex(int reelIndex)
        {
            int symbolCount = reelDatas[reelIndex].symbols.Length;
            int pressedLowerSymbol = mainBoard.stoppedReelsSymbolIndex[reelIndex];
            int slipCount = mainBoard.stoppedReelsThroughSymbolCount[reelIndex];
            return NormalizeSymbolIndex(pressedLowerSymbol - slipCount, symbolCount);
        }

        private string GetSymbolAtRow(int reelIndex, int lowerSymbolIndex, int row)
        {
            string[] reelSymbols = reelDatas[reelIndex].symbols;
            int symbolIndex = NormalizeSymbolIndex(lowerSymbolIndex + row - 2, reelSymbols.Length);
            return reelSymbols[symbolIndex];
        }

        private bool ReelContainsSymbol(int reelIndex, string requiredSymbol)
        {
            string[] reelSymbols = reelDatas[reelIndex].symbols;
            for (int i = 0; i < reelSymbols.Length; i++)
            {
                if (reelSymbols[i] == requiredSymbol) return true;
            }

            return false;
        }

        private int NormalizeSymbolIndex(int symbolIndex, int symbolCount)
        {
            if (symbolCount <= 0) return 0;

            // Udon上の負数剰余へ依存せず、0番を越えた図柄を配列末尾へ戻す。
            while (symbolIndex < 0)
            {
                symbolIndex += symbolCount;
            }
            while (symbolIndex >= symbolCount)
            {
                symbolIndex -= symbolCount;
            }

            return symbolIndex;
        }

        /// <summary>
        /// PayoutTableに登録された成立役と有効ラインから、優先方式に従った滑りコマ数を算出する。
        /// </summary>
        public int CalculatePrioritySlip(
            int reelIndex,
            int currentSymbol,
            PayoutTable payoutTable,
            string activeFlag,
            ReelControlPriority priority)
        {
            return CalculatePrioritySlip(
                reelIndex,
                currentSymbol,
                payoutTable,
                null,
                activeFlag,
                priority,
                4
            );
        }

        /// <summary>
        /// 最大滑りコマ数を指定し、PayoutTableから優先方式に従った滑りコマ数を算出する。
        /// </summary>
        public int CalculatePrioritySlip(
            int reelIndex,
            int currentSymbol,
            PayoutTable payoutTable,
            string activeFlag,
            ReelControlPriority priority,
            int maxSlipCount)
        {
            return CalculatePrioritySlip(
                reelIndex,
                currentSymbol,
                payoutTable,
                null,
                activeFlag,
                priority,
                maxSlipCount
            );
        }

        /// <summary>
        /// LotteryTableで現在のフラグに登録された当選役とPayoutTableを使用し、滑りコマ数を算出する。
        /// </summary>
        public int CalculatePrioritySlip(
            int reelIndex,
            int currentSymbol,
            PayoutTable payoutTable,
            LotteryTable lotteryTable,
            string activeFlag,
            ReelControlPriority priority,
            int maxSlipCount)
        {
            if (!IsAutomaticControlStateValid(reelIndex, payoutTable)) return 0;

            int reelSymbolCount = reelDatas[reelIndex].symbols.Length;
            int normalizedCurrentSymbol = NormalizeSymbolIndex(currentSymbol, reelSymbolCount);
            int maximumSlip = Mathf.Clamp(maxSlipCount, 0, 4);
            int currentStopOrder = GetStoppedReelCount() + 1;

            int bestSlip = 0;
            int bestForbiddenCombinationCount = -1;
            int bestRolePriority = -1;
            int bestMatchLevel = -1;
            int bestPayout = -1;
            int bestCombinationCount = -1;

            for (int slip = 0; slip <= maximumSlip; slip++)
            {
                int candidateLowerSymbol = NormalizeSymbolIndex(normalizedCurrentSymbol - slip, reelSymbolCount);
                // 当選役は「全リールで揃い得る」と「今回のリールだけ一致」を、優先側と非優先側に分けて集計する。
                int preferredPayout = 0;
                int preferredCombinationCount = 0;
                int fallbackPayout = 0;
                int fallbackCombinationCount = 0;
                int preferredReelPayout = 0;
                int preferredReelCombinationCount = 0;
                int fallbackReelPayout = 0;
                int fallbackReelCombinationCount = 0;
                // 非当選役は、この位置で止めると成立が確定してしまう組み合わせ数を数える。
                int forbiddenCombinationCount = 0;

                for (int pairIndex = 0; pairIndex < payoutTable.GetWinPairCount(); pairIndex++)
                {
                    bool isWon = IsPayoutTableCandidate(payoutTable, lotteryTable, pairIndex, activeFlag);
                    if (!IsPayoutTableStopOrderPossible(payoutTable, pairIndex, reelIndex, currentStopOrder)) continue;

                    int payout = 0;
                    bool isPreferred = false;
                    if (isWon)
                    {
                        payout = payoutTable.GetPayout(pairIndex, mainBoard.bet);
                        bool isBonus = payoutTable.IsBonus(pairIndex);
                        isPreferred = rolePriority == ReelRolePriority.BonusFirst ? isBonus : !isBonus;
                    }

                    for (int lineIndex = 0; lineIndex < payoutTable.GetPayLineCount(); lineIndex++)
                    {
                        if (!payoutTable.IsPayLineActive(lineIndex, mainBoard.bet)) continue;

                        if (!isWon)
                        {
                            if (IsPayoutTableWinPairForced(payoutTable, pairIndex, lineIndex, reelIndex, candidateLowerSymbol))
                            {
                                forbiddenCombinationCount++;
                            }
                            continue;
                        }

                        if (IsPayoutTableTargetReelMatched(payoutTable, pairIndex, lineIndex, reelIndex, candidateLowerSymbol))
                        {
                            if (isPreferred)
                            {
                                preferredReelCombinationCount++;
                                if (payout > preferredReelPayout) preferredReelPayout = payout;
                            }
                            else
                            {
                                fallbackReelCombinationCount++;
                                if (payout > fallbackReelPayout) fallbackReelPayout = payout;
                            }
                        }

                        if (!IsPayoutTableWinPairPossible(payoutTable, pairIndex, lineIndex, reelIndex, candidateLowerSymbol)) continue;

                        if (isPreferred)
                        {
                            preferredCombinationCount++;
                            if (payout > preferredPayout) preferredPayout = payout;
                        }
                        else
                        {
                            fallbackCombinationCount++;
                            if (payout > fallbackPayout) fallbackPayout = payout;
                        }
                    }
                }

                // 優先側に候補が1つでもあれば優先側だけで評価する。
                bool hasPreferredCandidate = preferredCombinationCount > 0 || preferredReelCombinationCount > 0;
                int fullCombinationCount = hasPreferredCandidate ? preferredCombinationCount : fallbackCombinationCount;
                int fullPayout = hasPreferredCandidate ? preferredPayout : fallbackPayout;
                int reelCombinationCount = hasPreferredCandidate ? preferredReelCombinationCount : fallbackReelCombinationCount;
                int reelPayout = hasPreferredCandidate ? preferredReelPayout : fallbackReelPayout;

                int candidateRolePriority = hasPreferredCandidate ? 1 : 0;
                int candidateMatchLevel = 0;
                int candidatePayout = 0;
                int candidateCombinationCount = 0;
                if (fullCombinationCount > 0)
                {
                    candidateMatchLevel = 2;
                    candidatePayout = fullPayout;
                    candidateCombinationCount = fullCombinationCount;
                }
                else if (reelCombinationCount > 0)
                {
                    candidateMatchLevel = 1;
                    candidatePayout = reelPayout;
                    candidateCombinationCount = reelCombinationCount;
                }

                // 確定してしまう非当選役が少ない → 優先側 → 一致段階 → 枚数/組み合わせ数 の順で比較する。
                bool isBetter;
                if (forbiddenCombinationCount != bestForbiddenCombinationCount)
                {
                    isBetter = bestForbiddenCombinationCount < 0 || forbiddenCombinationCount < bestForbiddenCombinationCount;
                }
                else if (candidateRolePriority != bestRolePriority)
                {
                    isBetter = candidateRolePriority > bestRolePriority;
                }
                else if (candidateMatchLevel != bestMatchLevel)
                {
                    isBetter = candidateMatchLevel > bestMatchLevel;
                }
                else
                {
                    isBetter = IsPriorityCandidateBetter(priority, candidatePayout, candidateCombinationCount, bestPayout, bestCombinationCount);
                }
                if (!isBetter) continue;

                bestSlip = slip;
                bestForbiddenCombinationCount = forbiddenCombinationCount;
                bestRolePriority = candidateRolePriority;
                bestMatchLevel = candidateMatchLevel;
                bestPayout = candidatePayout;
                bestCombinationCount = candidateCombinationCount;
            }

            return bestSlip;
        }

        /// <summary>
        /// 全リール停止後の図柄をPayoutTableと照合し、成立した役名を返す。
        /// </summary>
        public string[] JudgePayoutFromTable(PayoutTable payoutTable)
        {
            string[] winningNames = new string[0];
            if (!IsAutomaticControlStateValid(0, payoutTable)) return winningNames;

            for (int lineIndex = 0; lineIndex < payoutTable.GetPayLineCount(); lineIndex++)
            {
                if (!payoutTable.IsPayLineActive(lineIndex, mainBoard.bet)) continue;

                for (int pairIndex = 0; pairIndex < payoutTable.GetWinPairCount(); pairIndex++)
                {
                    if (!IsCompletedStopOrderMatched(payoutTable, pairIndex)) continue;
                    if (!IsCompletedWinPairMatched(payoutTable, pairIndex, lineIndex)) continue;

                    winningNames = AppendString(winningNames, payoutTable.winPairNames[pairIndex]);
                }
            }

            return winningNames;
        }

        /// <summary>
        /// 現在BETで有効なライン上の停止図柄を、ラインごとに左、中、右の順で返す。
        /// 戻り値は3要素単位の平坦な配列で、ラインの並びはPayoutTableの登録順となる。
        /// </summary>
        public string[] GetActivePayLineSymbols()
        {
            string[] symbols = new string[0];
            if (!IsAutomaticControlStateValid(0, payoutTable)) return symbols;

            for (int reelIndex = 0; reelIndex < 3; reelIndex++)
            {
                if (!IsReelDataValid(reelIndex)) return symbols;
                if (mainBoard.stoppedReelsSymbolIndex[reelIndex] < 0) return symbols;
            }

            int activeLineCount = 0;
            for (int lineIndex = 0; lineIndex < payoutTable.GetPayLineCount(); lineIndex++)
            {
                if (payoutTable.IsPayLineActive(lineIndex, mainBoard.bet)) activeLineCount++;
            }

            symbols = new string[activeLineCount * 3];
            int outputIndex = 0;
            for (int lineIndex = 0; lineIndex < payoutTable.GetPayLineCount(); lineIndex++)
            {
                if (!payoutTable.IsPayLineActive(lineIndex, mainBoard.bet)) continue;

                for (int reelIndex = 0; reelIndex < 3; reelIndex++)
                {
                    int row = payoutTable.GetPayLineRow(lineIndex, reelIndex);
                    symbols[outputIndex] = GetSymbolAtRow(reelIndex, GetStoppedLowerSymbolIndex(reelIndex), row);
                    outputIndex++;
                }
            }

            return symbols;
        }

        /// <summary>
        /// 成立役名をPayoutTableへ照合し、合計払出枚数を返す。
        /// </summary>
        public int GetPayoutFromTable(PayoutTable payoutTable, string[] winningNames)
        {
            if (!Utilities.IsValid(payoutTable)) return 0;
            if (winningNames == null) return 0;

            int payout = 0;
            for (int i = 0; i < winningNames.Length; i++)
            {
                payout += payoutTable.FindPayout(winningNames[i], mainBoard.bet);
            }

            return payout;
        }

        private bool IsPayoutTableCandidate(
            PayoutTable payoutTable,
            LotteryTable lotteryTable,
            int pairIndex,
            string activeFlag)
        {
            if (Utilities.IsValid(lotteryTable) && lotteryTable.HasWinningRoleDefinitions())
            {
                return lotteryTable.IsRoleWon(activeFlag, payoutTable.winPairNames[pairIndex]);
            }

            return payoutTable.IsControlCandidate(pairIndex, activeFlag);
        }

        private bool IsAutomaticControlStateValid(int reelIndex, PayoutTable payoutTable)
        {
            if (!Utilities.IsValid(mainBoard) || !Utilities.IsValid(payoutTable)) return false;
            if (reelDatas == null || reelDatas.Length < 3) return false;
            if (reelIndex < 0 || reelIndex >= reelDatas.Length) return false;
            if (!IsReelDataValid(reelIndex)) return false;
            if (mainBoard.stoppedReelsSymbolIndex == null || mainBoard.stoppedReelsSymbolIndex.Length < 3) return false;
            if (mainBoard.stoppedReelsThroughSymbolCount == null ||
                mainBoard.stoppedReelsThroughSymbolCount.Length < 3) return false;
            if (mainBoard.stoppedReelsStopOrder == null || mainBoard.stoppedReelsStopOrder.Length < 3) return false;
            return true;
        }

        private bool IsPayoutTableStopOrderPossible(
            PayoutTable payoutTable,
            int pairIndex,
            int targetReelIndex,
            int currentStopOrder)
        {
            if (payoutTable.isReelStopOrderAny[pairIndex]) return true;

            for (int reelIndex = 0; reelIndex < 3; reelIndex++)
            {
                int requiredOrder = payoutTable.GetStopOrder(pairIndex, reelIndex);
                if (reelIndex == targetReelIndex)
                {
                    if (requiredOrder != currentStopOrder) return false;
                    continue;
                }

                // 停止済みは実際の停止順、未停止は今後の停止順に収まるか確認する。
                int stoppedOrder = mainBoard.stoppedReelsStopOrder[reelIndex];
                if (stoppedOrder >= 1)
                {
                    if (requiredOrder != stoppedOrder) return false;
                }
                else if (requiredOrder <= currentStopOrder || requiredOrder > 3) return false;
            }

            return true;
        }

        private bool IsPayoutTableWinPairPossible(
            PayoutTable payoutTable,
            int pairIndex,
            int lineIndex,
            int targetReelIndex,
            int candidateLowerSymbol)
        {
            for (int reelIndex = 0; reelIndex < 3; reelIndex++)
            {
                if (!IsReelDataValid(reelIndex)) return false;

                int row = payoutTable.GetPayLineRow(lineIndex, reelIndex);
                if (row < 0 || row > 2) return false;

                string requiredSymbol = payoutTable.GetSymbol(pairIndex, reelIndex);
                if (string.IsNullOrEmpty(requiredSymbol)) continue;

                // 対象リールは候補位置、停止済みは最終位置、未停止はリール上に図柄があるかで判定する。
                int lowerSymbol = reelIndex == targetReelIndex ? candidateLowerSymbol : GetFinalLowerSymbolIndex(reelIndex);
                if (lowerSymbol >= 0)
                {
                    if (!IsPayoutSymbolMatched(GetSymbolAtRow(reelIndex, lowerSymbol, row), requiredSymbol)) return false;
                }
                else if (!ReelContainsPayoutSymbol(reelIndex, requiredSymbol)) return false;
            }

            return true;
        }

        /// <summary>
        /// 今回停止するリールだけを対象に、当選役の要求図柄を表示できるか確認する。
        /// 他リールですでに役全体を取りこぼしている場合の引き込み継続に使用する。
        /// </summary>
        private bool IsPayoutTableTargetReelMatched(
            PayoutTable payoutTable,
            int pairIndex,
            int lineIndex,
            int targetReelIndex,
            int candidateLowerSymbol)
        {
            string requiredSymbol = payoutTable.GetSymbol(pairIndex, targetReelIndex);
            if (string.IsNullOrEmpty(requiredSymbol)) return false;

            int row = payoutTable.GetPayLineRow(lineIndex, targetReelIndex);
            if (row < 0 || row > 2) return false;

            return IsPayoutSymbolMatched(GetSymbolAtRow(targetReelIndex, candidateLowerSymbol, row), requiredSymbol);
        }

        /// <summary>
        /// 今回の停止によって、未停止リールの操作に関係なく対象役が確定するか確認する。
        /// 未停止リールの要求図柄がANYの場合だけ確定済みとして扱う。
        /// </summary>
        private bool IsPayoutTableWinPairForced(
            PayoutTable payoutTable,
            int pairIndex,
            int lineIndex,
            int targetReelIndex,
            int candidateLowerSymbol)
        {
            for (int reelIndex = 0; reelIndex < 3; reelIndex++)
            {
                if (!IsReelDataValid(reelIndex)) return false;

                string requiredSymbol = payoutTable.GetSymbol(pairIndex, reelIndex);
                int row = payoutTable.GetPayLineRow(lineIndex, reelIndex);
                if (row < 0 || row > 2) return false;

                int lowerSymbol = reelIndex == targetReelIndex ? candidateLowerSymbol : GetFinalLowerSymbolIndex(reelIndex);
                if (lowerSymbol >= 0)
                {
                    if (!IsPayoutSymbolMatched(GetSymbolAtRow(reelIndex, lowerSymbol, row), requiredSymbol)) return false;
                }
                else if (!string.IsNullOrEmpty(requiredSymbol)) return false;
            }

            return true;
        }

        private bool IsCompletedStopOrderMatched(PayoutTable payoutTable, int pairIndex)
        {
            if (payoutTable.isReelStopOrderAny[pairIndex]) return true;

            for (int reelIndex = 0; reelIndex < 3; reelIndex++)
            {
                if (payoutTable.GetStopOrder(pairIndex, reelIndex) !=
                    mainBoard.stoppedReelsStopOrder[reelIndex]) return false;
            }

            return true;
        }

        private bool IsCompletedWinPairMatched(PayoutTable payoutTable, int pairIndex, int lineIndex)
        {
            for (int reelIndex = 0; reelIndex < 3; reelIndex++)
            {
                if (!IsReelDataValid(reelIndex)) return false;
                if (mainBoard.stoppedReelsSymbolIndex[reelIndex] < 0) return false;

                string requiredSymbol = payoutTable.GetSymbol(pairIndex, reelIndex);
                if (string.IsNullOrEmpty(requiredSymbol)) continue;

                int row = payoutTable.GetPayLineRow(lineIndex, reelIndex);
                if (!IsPayoutSymbolMatched(GetSymbolAtRow(reelIndex, GetStoppedLowerSymbolIndex(reelIndex), row), requiredSymbol)) return false;
            }

            return true;
        }

        private bool IsReelDataValid(int reelIndex)
        {
            return Utilities.IsValid(reelDatas[reelIndex]) &&
                reelDatas[reelIndex].symbols != null &&
                reelDatas[reelIndex].symbols.Length > 0;
        }

        /// <summary>リール上に、カンマ区切り候補のいずれかが存在するか確認する。</summary>
        private bool ReelContainsPayoutSymbol(int reelIndex, string requiredSymbols)
        {
            string[] reelSymbols = reelDatas[reelIndex].symbols;
            for (int i = 0; i < reelSymbols.Length; i++)
            {
                if (IsPayoutSymbolMatched(reelSymbols[i], requiredSymbols)) return true;
            }

            return false;
        }

        /// <summary>
        /// 実際の図柄名が半角カンマ区切り候補のいずれかと一致するか確認する。
        /// 候補前後の半角・全角スペースは無視する。
        /// </summary>
        private bool IsPayoutSymbolMatched(string actualSymbol, string requiredSymbols)
        {
            if (string.IsNullOrEmpty(requiredSymbols)) return true;

            int candidateStart = 0;
            int textLength = requiredSymbols.Length;
            for (int i = 0; i <= textLength; i++)
            {
                // 区切りのカンマか末尾に来たら、直前の候補を前後の空白を除いて比較する。
                if (i < textLength && requiredSymbols.Substring(i, 1) != ",") continue;

                int trimmedStart = candidateStart;
                int trimmedEnd = i;
                while (trimmedStart < trimmedEnd &&
                    IsPayoutSymbolSpace(requiredSymbols.Substring(trimmedStart, 1)))
                {
                    trimmedStart++;
                }
                while (trimmedEnd > trimmedStart &&
                    IsPayoutSymbolSpace(requiredSymbols.Substring(trimmedEnd - 1, 1)))
                {
                    trimmedEnd--;
                }

                if (trimmedEnd > trimmedStart &&
                    requiredSymbols.Substring(trimmedStart, trimmedEnd - trimmedStart) == actualSymbol)
                {
                    return true;
                }

                candidateStart = i + 1;
            }

            return false;
        }

        /// <summary>図柄候補の前後から除外する空白か確認する。</summary>
        private bool IsPayoutSymbolSpace(string value)
        {
            return value == " " || value == "　" || value == "\t";
        }

        private string[] AppendString(string[] source, string value)
        {
            string[] result = new string[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[result.Length - 1] = value;
            return result;
        }
        
        /// <summary>PayoutTable、現在のBET、有効ライン、停止順から成立役を判定する。</summary>
        /// <param name="stopSymbolIndex">MainBoardから渡される各リールの停止図柄位置。</param>
        /// <returns>成立した役の内部使用名。成立役がない場合は空配列。</returns>
        public virtual string[] JudgePayout(int[] stopSymbolIndex)
        {
            if (!Utilities.IsValid(payoutTable)) return new string[0];
            return JudgePayoutFromTable(payoutTable);
        }

        /// <summary>現在成立しているフラグの内部使用名を返す。</summary>
        public virtual string GetActiveFlag()
        {
            return activeFlag;
        }

        /// <summary>
        /// ReelControl.flagNamesから制御データを検索するためのフラグ名を返す。
        /// 既定では成立フラグの内部使用名を使用する。
        /// 派生クラスでオーバーライドし、GetActiveFlag() + "_" + bonusFlagなどの検索名を指定できる。
        /// </summary>
        public virtual string GetReelControlFlagName()
        {
            return GetActiveFlag();
        }

        /// <summary>現在成立しているフラグの表示名を返す。</summary>
        public string GetActiveFlagDisplayName()
        {
            if (!Utilities.IsValid(lotteryTable)) return activeFlag;
            return lotteryTable.GetFlagDisplayName(activeFlag);
        }
        

        /// <summary>成立役の内部使用名から現在BETの払出枚数合計を返す。</summary>
        public virtual int GetPayout(string[] winningSymbols)
        {
            if (!Utilities.IsValid(payoutTable)) return 0;
            return GetPayoutFromTable(payoutTable, winningSymbols);
        }

        /// <summary>成立役にリプレイが含まれているか返す。</summary>
        public virtual bool IsReplay(string[] winningSymbols)
        {
            if (!Utilities.IsValid(payoutTable)) return false;
            return payoutTable.ContainsReplay(winningSymbols);
        }

        public bool IsOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        public void SetOwner()
        {
            if (IsOwner()) return;
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        
    }

}
