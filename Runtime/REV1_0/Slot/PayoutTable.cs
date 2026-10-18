using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>成立役を小役、リプレイ、ボーナスへ分類する。</summary>
    public enum PayoutRoleType
    {
        [InspectorName("小役")]
        SmallRole,
        [InspectorName("リプレイ")]
        Replay,
        [InspectorName("ボーナス")]
        Bonus,
        [InspectorName("移行図柄")]
        Transition
    }

    /// <summary>
    /// 成立役ごとの図柄組み合わせと払出条件を定義する配当表。
    /// 成立役の表示名と内部使用名、図柄、BET別配当、リプレイ属性、停止順、有効ラインを提供する。
    /// どのフラグでどの役が当選するかはLotteryTable側で定義する。
    /// 告知やボーナス状態遷移は扱わない。
    ///
    /// Udonで扱いやすくするため、各成立役と各有効ラインは複数の平坦な配列で保持する。
    /// 成立役用配列は同じインデックス同士、有効ライン用配列も同じインデックス同士が1件の定義になる。
    /// 通常は配列を直接編集せず、専用Inspectorを使用する。
    /// </summary>
    public class PayoutTable : UdonSharpBehaviour
    {
        /// <summary>
        /// 成立判定やLotteryTableとの照合に使用する成立役の内部使用名。
        /// 既存のInspectorデータとの互換性を保つため、変数名はそのままにしている。
        /// </summary>
        [HideInInspector]
        public string[] winPairNames = new string[0];

        /// <summary>Inspectorや画面表示に使用する成立役の表示名。</summary>
        [HideInInspector]
        public string[] winPairDisplayNames = new string[0];

        /// <summary>
        /// 各成立役がBET 3で1ライン成立したときの払出枚数。
        /// 既存のInspectorデータとの互換性を保つため、変数名はそのままにしている。
        /// </summary>
        [HideInInspector]
        public int[] payouts = new int[0];

        /// <summary>各成立役がBET 1で1ライン成立したときの払出枚数。</summary>
        [HideInInspector]
        public int[] payoutsBet1 = new int[0];

        /// <summary>各成立役がBET 2で1ライン成立したときの払出枚数。</summary>
        [HideInInspector]
        public int[] payoutsBet2 = new int[0];

        /// <summary>trueの場合はリプレイ役として扱い、メダルの払出枚数を0にする。</summary>
        [HideInInspector]
        public bool[] isReplays = new bool[0];

        /// <summary>trueの場合はボーナス役として扱う。</summary>
        [HideInInspector]
        public bool[] isBonuses = new bool[0];

        /// <summary>
        /// trueの場合は移行図柄（RTの契機にする払出なしの組合せ）として扱う。
        /// リール制御では引き込みも回避もしない。
        /// </summary>
        [HideInInspector]
        public bool[] isTransitions = new bool[0];

        /// <summary>左リールに必要な図柄名。空文字は任意、半角カンマ区切りはOR条件として扱う。</summary>
        [HideInInspector]
        public string[] leftSymbols = new string[0];

        /// <summary>中リールに必要な図柄名。空文字は任意、半角カンマ区切りはOR条件として扱う。</summary>
        [HideInInspector]
        public string[] centerSymbols = new string[0];

        /// <summary>右リールに必要な図柄名。空文字は任意、半角カンマ区切りはOR条件として扱う。</summary>
        [HideInInspector]
        public string[] rightSymbols = new string[0];

        /// <summary>旧方式の対応フラグ。既存データの移行互換にのみ使用する。</summary>
        [HideInInspector]
        public string[] controlFlags = new string[0];

        /// <summary>旧方式の常時候補設定。既存データの移行互換にのみ使用する。</summary>
        [HideInInspector]
        public bool[] isAlwaysControlCandidate = new bool[0];

        /// <summary>trueの場合、リールの停止順を成立条件に含めない。</summary>
        [HideInInspector]
        public bool[] isReelStopOrderAny = new bool[0];

        /// <summary>左リールに要求する停止順。1が第1停止、3が第3停止。</summary>
        [HideInInspector]
        public int[] leftStopOrders = new int[0];

        /// <summary>中リールに要求する停止順。1が第1停止、3が第3停止。</summary>
        [HideInInspector]
        public int[] centerStopOrders = new int[0];

        /// <summary>右リールに要求する停止順。1が第1停止、3が第3停止。</summary>
        [HideInInspector]
        public int[] rightStopOrders = new int[0];

        /// <summary>
        /// 各有効ラインを使用するBET数のビットマスク。1BET=1、2BET=2、3BET=4。
        /// </summary>
        [HideInInspector]
        public int[] payLineBetMasks = new int[0];

        /// <summary>有効ラインが通る左リールの段。0=上段、1=中段、2=下段。</summary>
        [HideInInspector]
        public int[] payLineLeftRows = new int[0];

        /// <summary>有効ラインが通る中リールの段。0=上段、1=中段、2=下段。</summary>
        [HideInInspector]
        public int[] payLineCenterRows = new int[0];

        /// <summary>有効ラインが通る右リールの段。0=上段、1=中段、2=下段。</summary>
        [HideInInspector]
        public int[] payLineRightRows = new int[0];

        /// <summary>登録されている成立役定義の件数を返す。</summary>
        public int GetWinPairCount()
        {
            return winPairNames == null ? 0 : winPairNames.Length;
        }

        /// <summary>登録されている有効ライン定義の件数を返す。</summary>
        public int GetPayLineCount()
        {
            return payLineBetMasks == null ? 0 : payLineBetMasks.Length;
        }

        /// <summary>指定した成立役の平坦配列がすべて揃っているか確認する。</summary>
        public bool IsWinPairValid(int pairIndex)
        {
            int count = GetWinPairCount();
            if (pairIndex < 0 || pairIndex >= count) return false;
            if (payouts == null || payouts.Length <= pairIndex) return false;
            if (leftSymbols == null || leftSymbols.Length <= pairIndex) return false;
            if (centerSymbols == null || centerSymbols.Length <= pairIndex) return false;
            if (rightSymbols == null || rightSymbols.Length <= pairIndex) return false;
            if (isReelStopOrderAny == null || isReelStopOrderAny.Length <= pairIndex) return false;
            if (leftStopOrders == null || leftStopOrders.Length <= pairIndex) return false;
            if (centerStopOrders == null || centerStopOrders.Length <= pairIndex) return false;
            if (rightStopOrders == null || rightStopOrders.Length <= pairIndex) return false;
            return true;
        }

        /// <summary>指定した有効ラインの平坦配列がすべて揃っているか確認する。</summary>
        public bool IsPayLineValid(int lineIndex)
        {
            int count = GetPayLineCount();
            if (lineIndex < 0 || lineIndex >= count) return false;
            if (payLineLeftRows == null || payLineLeftRows.Length <= lineIndex) return false;
            if (payLineCenterRows == null || payLineCenterRows.Length <= lineIndex) return false;
            if (payLineRightRows == null || payLineRightRows.Length <= lineIndex) return false;
            return true;
        }

        /// <summary>旧方式の対応フラグから、成立役を候補として使用できるか確認する。</summary>
        public bool IsControlCandidate(int pairIndex, string activeFlag)
        {
            if (!IsWinPairValid(pairIndex)) return false;
            if (controlFlags == null || controlFlags.Length <= pairIndex) return false;
            if (isAlwaysControlCandidate == null || isAlwaysControlCandidate.Length <= pairIndex) return false;
            if (isAlwaysControlCandidate[pairIndex]) return true;
            if (string.IsNullOrEmpty(activeFlag)) return false;
            return controlFlags[pairIndex] == activeFlag;
        }

        /// <summary>指定した有効ラインが現在のBET数で有効か確認する。</summary>
        public bool IsPayLineActive(int lineIndex, int bet)
        {
            if (!IsPayLineValid(lineIndex)) return false;
            if (bet < 1 || bet > 3) return false;

            // 1BET=1、2BET=2、3BET=4
            int betMask = 1 << (bet - 1);
            return (payLineBetMasks[lineIndex] & betMask) != 0;
        }

        /// <summary>指定した成立役で、対象リールに要求される図柄名を返す。</summary>
        public string GetSymbol(int pairIndex, int reelIndex)
        {
            if (!IsWinPairValid(pairIndex)) return "";
            switch (reelIndex)
            {
                case 0: return leftSymbols[pairIndex];
                case 1: return centerSymbols[pairIndex];
                case 2: return rightSymbols[pairIndex];
                default: return "";
            }
        }

        /// <summary>指定した成立役で、対象リールに要求される停止順を返す。</summary>
        public int GetStopOrder(int pairIndex, int reelIndex)
        {
            if (!IsWinPairValid(pairIndex)) return -1;
            switch (reelIndex)
            {
                case 0: return leftStopOrders[pairIndex];
                case 1: return centerStopOrders[pairIndex];
                case 2: return rightStopOrders[pairIndex];
                default: return -1;
            }
        }

        /// <summary>指定した有効ラインが、対象リールのどの表示段を通るか返す。</summary>
        public int GetPayLineRow(int lineIndex, int reelIndex)
        {
            if (!IsPayLineValid(lineIndex)) return -1;
            switch (reelIndex)
            {
                case 0: return payLineLeftRows[lineIndex];
                case 1: return payLineCenterRows[lineIndex];
                case 2: return payLineRightRows[lineIndex];
                default: return -1;
            }
        }

        /// <summary>指定した成立役のBET別払出枚数を返す。</summary>
        public int GetPayout(int pairIndex, int bet)
        {
            if (!IsWinPairValid(pairIndex)) return 0;
            if (IsReplay(pairIndex) || IsTransition(pairIndex)) return 0;

            // BET 1・2の配当が未設定ならBET 3の配当を使う。
            switch (bet)
            {
                case 1: return payoutsBet1 != null && payoutsBet1.Length > pairIndex ? payoutsBet1[pairIndex] : payouts[pairIndex];
                case 2: return payoutsBet2 != null && payoutsBet2.Length > pairIndex ? payoutsBet2[pairIndex] : payouts[pairIndex];
                case 3: return payouts[pairIndex];
                default: return 0;
            }
        }

        /// <summary>指定した成立役がリプレイとして設定されているか確認する。</summary>
        public bool IsReplay(int pairIndex)
        {
            if (pairIndex < 0 || pairIndex >= GetWinPairCount()) return false;
            if (isReplays == null || isReplays.Length <= pairIndex) return false;
            return isReplays[pairIndex];
        }

        /// <summary>内部使用名に対応する成立役がリプレイか確認する。</summary>
        public bool IsReplay(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return false;
            for (int i = 0; i < GetWinPairCount(); i++)
            {
                if (winPairNames[i] == internalName) return IsReplay(i);
            }

            return false;
        }

        /// <summary>指定した成立役がボーナスとして設定されているか確認する。</summary>
        public bool IsBonus(int pairIndex)
        {
            if (pairIndex < 0 || pairIndex >= GetWinPairCount()) return false;
            if (isBonuses == null || isBonuses.Length <= pairIndex) return false;
            return isBonuses[pairIndex];
        }

        /// <summary>指定した成立役が移行図柄として設定されているか確認する。</summary>
        public bool IsTransition(int pairIndex)
        {
            if (pairIndex < 0 || pairIndex >= GetWinPairCount()) return false;
            if (isTransitions == null || isTransitions.Length <= pairIndex) return false;
            return isTransitions[pairIndex];
        }

        /// <summary>内部使用名に対応する成立役が移行図柄か確認する。</summary>
        public bool IsTransition(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return false;
            for (int i = 0; i < GetWinPairCount(); i++)
            {
                if (winPairNames[i] == internalName) return IsTransition(i);
            }

            return false;
        }

        /// <summary>内部使用名に対応する成立役がボーナスか確認する。</summary>
        public bool IsBonus(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return false;
            for (int i = 0; i < GetWinPairCount(); i++)
            {
                if (winPairNames[i] == internalName) return IsBonus(i);
            }

            return false;
        }

        /// <summary>成立役一覧にリプレイ役が含まれているか確認する。</summary>
        public bool ContainsReplay(string[] internalNames)
        {
            if (internalNames == null) return false;
            for (int i = 0; i < internalNames.Length; i++)
            {
                if (IsReplay(internalNames[i])) return true;
            }

            return false;
        }

        /// <summary>内部使用名に対応する表示名を返す。</summary>
        public string GetWinPairDisplayName(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return "";
            for (int i = 0; i < GetWinPairCount(); i++)
            {
                if (winPairNames[i] != internalName) continue;
                if (winPairDisplayNames == null || winPairDisplayNames.Length <= i ||
                    string.IsNullOrEmpty(winPairDisplayNames[i])) return internalName;
                return winPairDisplayNames[i];
            }

            return internalName;
        }

        /// <summary>
        /// 現在の制御フラグで有効な成立役から、指定された役名のBET 3払出枚数を返す。
        /// </summary>
        public int FindPayout(string winPairName, string activeFlag)
        {
            return FindPayout(winPairName, activeFlag, 3);
        }

        /// <summary>現在の制御フラグで有効な成立役から、指定BETの払出枚数を返す。</summary>
        public int FindPayout(string winPairName, string activeFlag, int bet)
        {
            for (int i = 0; i < GetWinPairCount(); i++)
            {
                if (!IsControlCandidate(i, activeFlag)) continue;
                if (winPairNames[i] == winPairName) return GetPayout(i, bet);
            }

            return 0;
        }

        /// <summary>成立役名に対応するBET 3払出枚数を返す。</summary>
        public int FindPayout(string winPairName)
        {
            return FindPayout(winPairName, 3);
        }

        /// <summary>成立役名に対応する指定BETの払出枚数を返す。</summary>
        public int FindPayout(string winPairName, int bet)
        {
            for (int i = 0; i < GetWinPairCount(); i++)
            {
                if (!IsWinPairValid(i)) continue;
                if (winPairNames[i] == winPairName) return GetPayout(i, bet);
            }

            return 0;
        }
    }
}
