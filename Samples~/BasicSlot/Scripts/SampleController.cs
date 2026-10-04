using System;
using UdonSharp;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AX.UdonSlot.REV1_0
{
    /**
     * 小役の種類
     */
    public enum WinType
    {
        None,
        Blueberry,
        Cherry,
        CenterCherry,
        Bell,
        Replay,
    }

    /**
     * 抽選の種類
     */
    public enum LotteryType
    {
        None,
        Blueberry,
        Cherry,
        CenterCherryBigBonus,
        CenterCherryRegularBonus,
        DuplicateCherryBigBonus,
        DuplicateCherryRegularBonus,
        Bell,
        Replay,
        BigBonus,
        RegularBonus,
    }

    /// <summary>
    /// BasicSlotサンプルの抽選、ボーナス状態、動的リール制御、成立役判定、配当を実装する。
    /// </summary>
    public class SampleController : ReelOutputController
    {
        /**
         * 使用中のSlotSettingのインデックス
         */
        [SerializeField]
        private int setting = 0;

        [SerializeField]
        private SlotSetting[] slotSettingsFor1Bet, slotSettingsFor2Bet, slotSettingsFor3Bet;

        /**
         * 1枚がけ時の有効ライン
         * 枠内上から0オリジンでカウント。
         */
        private int[][] activePaylineBET1 = new int[][]
        {
            new int[] { 1, 1, 1 }, // 2ライン目
        };

        /**
         * 2枚がけ時の有効ライン
         * 枠内上から0オリジンでカウント。
         */
        private int[][] activePaylineBET2 = new int[][]
        {
            new int[] { 1, 1, 1 }, // 2ライン目
            new int[] { 0, 0, 0 }, // 1ライン目
        };

        /**
         * 3枚がけ時の有効ライン
         * 枠内上から0オリジンでカウント。
         */
        private int[][] activePaylineBET3 = new int[][]
        {
            new int[] { 1, 1, 1 }, // 2ライン目
            new int[] { 0, 0, 0 }, // 1ライン目
            new int[] { 2, 2, 2 }, // 3ライン目
            new int[] { 0, 1, 2 }, // 斜め下がり
            new int[] { 2, 1, 0 }, // 斜め上がり
        };

        // フラグ変数
        [UdonSynced]
        public bool isBonusGaming;

        [UdonSynced]
        public bool isBigBonus;

        [UdonSynced]
        public bool isRegularBonus;

        [UdonSynced]
        public bool isReplay;

        [UdonSynced]
        public WinType activeWin; // 成立役

        [UdonSynced]
        public LotteryType activeLot;

        [UdonSynced]
        public int lotteryResult;

        public override void GenerateFlag(int bet)
        {
            SetOwner();
            if (isBonusGaming)
            {
                // ボーナスゲーム中は抽選を行わない
                return;
            }

            activeWin = WinType.None;
            activeLot = LotteryType.None;
            SlotSetting currentSetting;
            switch (bet)
            {
                case 3:
                    currentSetting = slotSettingsFor3Bet[setting];
                    break;
                case 2:
                    currentSetting = slotSettingsFor2Bet[setting];
                    break;
                default:
                    currentSetting = slotSettingsFor1Bet[setting];
                    break;
            }

            // SlotSettingの範囲を並び替える
            LotteryType[] lotteryTypes = new LotteryType[]
            {
                LotteryType.None,
                LotteryType.Blueberry,
                LotteryType.Cherry,
                LotteryType.CenterCherryBigBonus,
                LotteryType.CenterCherryRegularBonus,
                LotteryType.DuplicateCherryBigBonus,
                LotteryType.DuplicateCherryRegularBonus,
                LotteryType.Bell,
                LotteryType.Replay,
                LotteryType.BigBonus,
                LotteryType.RegularBonus,
            };
            int[] ranges = new int[]
            {
                currentSetting.noneRange,
                currentSetting.blueberryRange,
                currentSetting.cherryRange,
                currentSetting.centerCherryBigBounsRange,
                currentSetting.centerCherryRegularBounsRange,
                currentSetting.duplicateCherryBigBounsRange,
                currentSetting.duplicateCherryRegularBounsRange,
                currentSetting.bellRange,
                currentSetting.replayRange,
                currentSetting.bigBonusRange,
                currentSetting.regularBonusRange,
            };

            // rangesを並び替える
            for (int i = 0; i < ranges.Length - 1; i++)
            {
                for (int j = 0; j < ranges.Length - i - 1; j++)
                {
                    if (ranges[j] > ranges[j + 1])
                    {
                        // swap
                        int tempRange = ranges[j];
                        ranges[j] = ranges[j + 1];
                        ranges[j + 1] = tempRange;

                        LotteryType tempType = lotteryTypes[j];
                        lotteryTypes[j] = lotteryTypes[j + 1];
                        lotteryTypes[j + 1] = tempType;
                    }
                }
            }

            lotteryResult = Random.Range(0, currentSetting.lotteryMaxRange);
            for (int i = 0; i < ranges.Length; i++)
            {
                if (lotteryResult < ranges[i])
                {
                    // lotteryResultがranges[i]未満ならlotteryTypes[i-1]が当選
                    // range[0]は必ず0以下なのでi-1しても問題ない
                    // 当選したフラグに応じてフラグ変数を更新
                    int selectedLotteryTypeIndex = i - 1;
                    activeLot = lotteryTypes[selectedLotteryTypeIndex];
                    switch (lotteryTypes[selectedLotteryTypeIndex])
                    {
                        case LotteryType.None:
                            activeWin = WinType.None;
                            break;
                        case LotteryType.Blueberry:
                            activeWin = WinType.Blueberry;
                            break;
                        case LotteryType.Cherry:
                            activeWin = WinType.Cherry;
                            break;
                        case LotteryType.Bell:
                            activeWin = WinType.Bell;
                            break;
                        case LotteryType.Replay:
                            activeWin = WinType.Replay;
                            isReplay = true;
                            break;
                        case LotteryType.CenterCherryBigBonus:
                            activeWin = WinType.CenterCherry;
                            if (!isBigBonus && !isRegularBonus) isBigBonus = true;
                            break;
                        case LotteryType.CenterCherryRegularBonus:
                            activeWin = WinType.CenterCherry;
                            if (!isBigBonus && !isRegularBonus) isRegularBonus = true;
                            break;
                        case LotteryType.DuplicateCherryBigBonus:
                            activeWin = WinType.Cherry;
                            if (!isBigBonus && !isRegularBonus) isBigBonus = true;
                            break;
                        case LotteryType.BigBonus:
                            if (!isBigBonus && !isRegularBonus) isBigBonus = true;
                            break;
                        case LotteryType.DuplicateCherryRegularBonus:
                            activeWin = WinType.Cherry;
                            if (!isBigBonus && !isRegularBonus) isRegularBonus = true;
                            break;
                        case LotteryType.RegularBonus:
                            if (!isBigBonus && !isRegularBonus) isRegularBonus = true;
                            break;
                    }

                    break;
                }
            }

            RequestSerialization();
        }
        
        public override int DynamicControl(string[] symbols, int currentSymbol, int reelIndex, int stopReelCount)
        {
            // コントロール制御
            // 目的: activeWin に応じて、有効ライン上へ当たりシンボルをできるだけ引き込む。

            // 有効ラインを選択
            int[][] activeLines;
            int currentBet = 1;
            if (mainBoard != null) currentBet = Mathf.Clamp(mainBoard.bet, 1, 3);
            switch (currentBet)
            {
                case 3:
                    activeLines = activePaylineBET3;
                    break;
                case 2:
                    activeLines = activePaylineBET2;
                    break;
                default:
                    activeLines = activePaylineBET1;
                    break;
            }

            // 目標となる図柄名の候補を準備（大文字比較）
            string[] candidates = new string[0];
            if (activeWin == WinType.Blueberry)
            {
                candidates = new string[] { "BLUEBERRY", "BLUE" };
            }
            else if (activeWin == WinType.CenterCherry)
            {
                candidates = new string[] { "CENTER", "CENTERCHERRY", "CENTRE" };
            }
            else if (activeWin == WinType.Cherry)
            {
                candidates = new string[] { "CHERRY" };
            }
            else if (activeWin == WinType.Bell)
            {
                candidates = new string[] { "BELL" };
            }
            else if (activeWin == WinType.Replay)
            {
                candidates = new string[] { "REPLAY", "REP" };
            }
            else
            {
                if (!string.IsNullOrEmpty(activeLot.ToString()))
                    candidates = new string[] { activeLot.ToString().ToUpper() };
            }

            if (candidates == null || candidates.Length == 0)
            {
                Debug.Log("コントロール対象の図柄候補が見つからないため0を返す");
                return 0;
            }

            int symbolCount = reelDatas[reelIndex].symbols.Length;

            // 最小滑り量を見つける
            int bestSlip = -1;
            int bestLineIdx = -1;
            int bestRequiredRow = -1;
            int bestAbsIndex = -1;
            string bestSymbol = null;
            string bestCandidate = null;

            // 各有効ラインについてチェック
            for (int lineIdx = 0; lineIdx < activeLines.Length; lineIdx++)
            {
                int[] line = activeLines[lineIdx];
                if (line == null || line.Length <= reelIndex) continue;
                int requiredRow = line[reelIndex]; // 0=top,1=middle,2=bottom

                // 最大探索幅を決める
                int maxSearch;
                if (stopReelCount >= 1)
                {
                    // 第二停止／第三停止時は表示範囲内のみを探索（枠外を越えて過去に行ってしまった図柄は不可）
                    int visibleWindow = symbols.Length; // 例: 8
                    // offset = s + (2 - r) が visibleWindow - 1 まで許容される -> s <= visibleWindow - 1 - (2 - r)
                    int maxAllowedS = visibleWindow - 1 - (2 - requiredRow);
                    if (maxAllowedS < 0) maxAllowedS = 0;
                    maxSearch = Mathf.Min(symbolCount, maxAllowedS + 1); // inclusive
                }
                else
                {
                    // 第1停止ではラップを許容して広く探索
                    maxSearch = symbolCount;
                }

                // コントロール制御の滑り上限は4コマ（s=0..4）
                maxSearch = Mathf.Min(maxSearch, 4 + 1);

                // search slips s = 0..maxSearch-1
                for (int s = 0; s < maxSearch; s++)
                {
                    int offset = s + (2 - requiredRow);
                    int absIndex = (currentSymbol - offset) % symbolCount;
                    if (absIndex < 0) absIndex += symbolCount;
                    string symbolName = reelDatas[reelIndex].symbols[absIndex];
                    if (string.IsNullOrEmpty(symbolName)) continue;
                    string up = symbolName.ToUpper();
                    bool match = false;
                    for (int ci = 0; ci < candidates.Length; ci++)
                    {
                        if (up.Contains(candidates[ci]))
                        {
                            match = true;
                            bestCandidate = candidates[ci];
                            break;
                        }
                    }

                    if (match)
                    {
                        if (bestSlip == -1 || s < bestSlip)
                        {
                            bestSlip = s;
                            bestLineIdx = lineIdx;
                            bestRequiredRow = requiredRow;
                            bestAbsIndex = absIndex;
                            bestSymbol = symbolName;
                        }
                        else
                        {
                            Debug.Log(
                                $"コントロール制御(補足): リール{reelIndex} ライン{lineIdx} で図柄 {symbolName} が s={s} で一致したが、既により小さい滑り s={bestSlip} があるため採用せず");
                        }

                        break; // このラインについてはこれ以上大きい s を調べる必要はない
                    }
                }
            }

            if (bestSlip >= 0)
            {
                int clampedSlip = Mathf.Clamp(bestSlip, 0, 4);
                string lineDesc = "";
                if (bestLineIdx >= 0)
                {
                    int[] chosenLine = activeLines[bestLineIdx];
                    string lineStr = "";
                    for (int i = 0; i < chosenLine.Length; i++)
                    {
                        if (i > 0) lineStr += ",";
                        if (i == reelIndex) lineStr += $"[{chosenLine[i]}]";
                        else lineStr += chosenLine[i].ToString();
                    }

                    lineDesc = $"有効ライン#{bestLineIdx} (" + lineStr + ")";
                }

                Debug.Log(
                    $"コントロール制御: リール{reelIndex} -> 滑り {clampedSlip} を選択しました (成立役: {activeWin})\n理由: {lineDesc}、要求行={bestRequiredRow}、現在下段={currentSymbol}、対象図柄インデックス={bestAbsIndex}、図柄='{bestSymbol}'、一致した候補='{bestCandidate}'");
                return clampedSlip;
            }
            else
            {
                Debug.Log(
                    $"コントロール制御: リール{reelIndex} で成立図柄が見つからなかったため0を返す (成立役: {activeWin})\n検索範囲のシンボル: {string.Join(",", symbols)}");
                return 0;
            }
        }

        public override string[] JudgePayout(int[] stopSymbolIndex)
        {
            // リールの状況をもとに、成立役を判定して返す関数。
            string[] output = new string[0];
            int[][] activeLines;
            int currentBet = 1;
            if (mainBoard != null) currentBet = Mathf.Clamp(mainBoard.bet, 1, 3);
            switch (currentBet)
            {
                case 3:
                    activeLines = activePaylineBET3;
                    break;
                case 2:
                    activeLines = activePaylineBET2;
                    break;
                default:
                    activeLines = activePaylineBET1;
                    break;
            }

            for (int i = 0; i < activeLines.Length; i++)
            {
                int[] line = activeLines[i];
                string[] symbols = new string[3];
                for (int r = 0; r < 3; r++)
                {
                    int reelIndex = r;
                    int symbolIndex = stopSymbolIndex[reelIndex] + (line[reelIndex] - 2); // lineの値は0=上、1=中、2=下なので-2して調整
                    if (symbolIndex < 0) symbolIndex += reelDatas[reelIndex].symbols.Length;
                    else if (symbolIndex >= reelDatas[reelIndex].symbols.Length)
                        symbolIndex -= reelDatas[reelIndex].symbols.Length;
                    symbols[r] = reelDatas[reelIndex].symbols[symbolIndex];
                }

                Debug.Log($"有効ライン#{i}のシンボル: {string.Join(", ", symbols)}");
                // symbolsをもとに成立役を判定するロジックを実装
                if (symbols[0].Contains("Cherry"))
                {
                    var tmp = new string[output.Length + 1];
                    Array.Copy(output, tmp, output.Length);
                    tmp[tmp.Length - 1] = "Cherry";
                    output = tmp;
                    break; // 複数ラインで同時に成立することはない想定で最初に見つかった役を返す
                }

                if (symbols[0].Contains("Bell") && symbols[1].Contains("Bell") && symbols[2].Contains("Bell"))
                {
                    var tmp = new string[output.Length + 1];
                    Array.Copy(output, tmp, output.Length);
                    tmp[tmp.Length - 1] = "Bell";
                    output = tmp;
                    break;
                }

                if (symbols[0].Contains("Blueberry") && symbols[1].Contains("Blueberry") &&
                    symbols[2].Contains("Blueberry"))
                {
                    var tmp = new string[output.Length + 1];
                    Array.Copy(output, tmp, output.Length);
                    tmp[tmp.Length - 1] = "Blueberry";
                    output = tmp;
                    break;
                }
            }

            return output;
        }

        public override int GetPayout(string[] winningSymbols)
        {
            // 表示している役が渡ってくるので、その役に応じた配当を返す関数。
            int payout = 0;
            for (int i = 0; i < winningSymbols.Length; i++)
            {
                if (winningSymbols[i].Contains("Cherry"))
                {
                    payout += 2;
                }
                else if (winningSymbols[i].Contains("Bell"))
                {
                    payout += 14;
                }
                else if (winningSymbols[i].Contains("Blueberry"))
                {
                    payout += 8;
                }
            }

            return payout;
        }

        /**
         * 入賞している役の図柄を返す関数。コントロール制御向け。
         * 空白はANYを意味する。例えば、チェリーが成立している役の図柄が["CHERRY", "", ""]であれば、左リールにチェリーがあればその役は成立する。
         * 中段チェリーとかはテーブル制御推奨。
         */
        public string[] GetSymbolsForLotteryType(LotteryType lotteryType)
        {
            switch (lotteryType)
            {
                case LotteryType.Blueberry:
                    return new string[] { "Blueberry", "Blueberry", "Blueberry" };
                case LotteryType.Cherry:
                    return new string[] { "Cherry", "Cherry", "" };
                case LotteryType.CenterCherryBigBonus:
                case LotteryType.CenterCherryRegularBonus:
                    return new string[] { "Cherry", "", "" };
                case LotteryType.DuplicateCherryBigBonus:
                case LotteryType.DuplicateCherryRegularBonus:
                    return new string[] { "Cherry", "", "" };
                case LotteryType.Bell:
                    return new string[] { "Bell", "Bell", "Bell" };
                case LotteryType.Replay:
                    return new string[] { "Replay", "Replay", "Replay" };
                default:
                    return new string[] { "", "", "" };
            }
        }

        public string GetLotteryTypeName(LotteryType lotteryType)
        {
            switch (lotteryType)
            {
                case LotteryType.Blueberry:
                    return "Blueberry";
                case LotteryType.Cherry:
                    return "Cherry";
                case LotteryType.CenterCherryBigBonus:
                    return "CenterCherryBigBonus";
                case LotteryType.CenterCherryRegularBonus:
                    return "CenterCherryRegularBonus";
                case LotteryType.DuplicateCherryBigBonus:
                    return "DuplicateCherryBigBonus";
                case LotteryType.DuplicateCherryRegularBonus:
                    return "DuplicateCherryRegularBonus";
                case LotteryType.Bell:
                    return "Bell";
                case LotteryType.Replay:
                    return "Replay";
                case LotteryType.BigBonus:
                    return "BigBonus";
                case LotteryType.RegularBonus:
                    return "RegularBonus";
                default:
                    return "None";
            }
        }
    }
}
