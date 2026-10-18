
using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// レバーON時の抽選内容を定義するテーブル。
    /// 各フラグにBET 1〜3ごとの抽選比率と0個以上の当選役を設定する。
    /// 1つのフラグへ複数の役を登録すると、複数役の同時当選を表現できる。
    /// ハズレに指定したフラグには、最大確率分母から他フラグの抽選比率を引いた残りを割り当てる。
    /// 役名はPayoutTableの成立役名と完全に一致させる。
    /// 対象BETの通常フラグ比率がすべて0の場合と、BET 1〜3以外の場合はハズレになる。
    /// </summary>
    public class LotteryTable : UdonSharpBehaviour
    {
        /// <summary>
        /// 抽選候補となるフラグの内部使用名。抽選結果を識別する名前として使用する。
        /// DrawFlagの戻り値、同期値、ReelControlとの照合にはこちらを使用する。
        /// 同じ位置にあるBET別比率配列の値が、そのフラグの抽選比率になる。
        /// 変数名は既存のInspectorデータとの互換性を保つためwinpairNamesのままにしている。
        /// </summary>
        [SerializeField]
        private string[] winpairNames;

        /// <summary>
        /// Inspectorや画面表示に使用するフラグの表示名。
        /// 内部処理では使用しないため、日本語名や後からの表示変更が抽選結果へ影響しない。
        /// 空の場合は内部使用名を表示名として扱う。
        /// </summary>
        [HideInInspector]
        public string[] flagDisplayNames = new string[0];
        
        /// <summary>
        /// BET 3で各フラグへ割り当てる抽選比率。
        /// 既存のInspectorデータとの互換性を保つため、変数名はそのままにしている。
        /// ハズレ指定がある場合は最大確率分母に対する割当値として使用する。
        /// ハズレ指定がない既存データでは、全項目の合計に対する相対比率として使用する。
        /// 0以下の要素は抽選対象から除外する。
        /// </summary>
        [SerializeField]
        private float[] winpairProbabilities;

        /// <summary>BET 1で各フラグへ割り当てる抽選比率。</summary>
        [HideInInspector]
        public float[] winpairProbabilitiesBet1 = new float[0];

        /// <summary>BET 2で各フラグへ割り当てる抽選比率。</summary>
        [HideInInspector]
        public float[] winpairProbabilitiesBet2 = new float[0];

        /// <summary>
        /// 固定抽選領域の最大値。65536なら、抽選比率1は1/65536として扱う。
        /// ハズレ指定がない既存データでは使用せず、従来の相対比率抽選を行う。
        /// </summary>
        [SerializeField]
        private int maximumProbabilityDenominator = 65536;

        /// <summary>
        /// 最大確率分母に対する余りを受け取るハズレフラグ。
        /// 複数指定されている場合は、先頭のフラグだけを使用する。
        /// </summary>
        [HideInInspector]
        public bool[] isRemainderFlags = new bool[0];

        /// <summary>
        /// 各フラグに属する当選役の開始位置。
        /// winningRoleNamesをフラグごとの範囲に分けるために使用する。
        /// </summary>
        [HideInInspector]
        public int[] winningRoleStartIndexes = new int[0];

        /// <summary>各フラグに登録されている当選役数。</summary>
        [HideInInspector]
        public int[] winningRoleCounts = new int[0];

        /// <summary>
        /// 全フラグの当選役名を順番に格納した配列。
        /// 役名はPayoutTableのwinPairNamesと完全一致で比較する。
        /// </summary>
        [HideInInspector]
        public string[] winningRoleNames = new string[0];

        /// <summary>
        /// 設定の名前。要素数が設定の数になる。空は設定1つ。
        /// 1つめの設定の比率は既存のBET別配列を使う。
        /// </summary>
        [HideInInspector]
        public string[] settingNames = new string[0];

        /// <summary>2つめ以降の設定のBET 1比率。[(設定 - 1) × フラグ数 + フラグ]。</summary>
        [HideInInspector]
        public float[] settingRatiosBet1 = new float[0];

        /// <summary>2つめ以降の設定のBET 2比率。</summary>
        [HideInInspector]
        public float[] settingRatiosBet2 = new float[0];

        /// <summary>2つめ以降の設定のBET 3比率。</summary>
        [HideInInspector]
        public float[] settingRatiosBet3 = new float[0];

        /// <summary>
        /// フラグと同時に成立するボーナスの内部名（BonusTable）。空はボーナスなし。
        /// </summary>
        [HideInInspector]
        public string[] entryBonusNames = new string[0];

        /// <summary>
        /// ボーナス付きフラグの元のフラグ名（制御名の {flag}）。空はフラグ名そのもの。
        /// 例: Cherry_RedBB の元は Cherry。
        /// </summary>
        [HideInInspector]
        public string[] entryBaseFlags = new string[0];

        /// <summary>
        /// 設定された抽選方式に従って、抽選結果のフラグを1つ選ぶ。
        /// </summary>
        /// <returns>
        /// 抽選されたフラグの内部使用名。抽選可能な候補がない場合は空文字。
        /// </returns>
        public string DrawFlag()
        {
            return DrawFlag(3);
        }

        /// <summary>
        /// 指定されたBET数の抽選比率を使用して、フラグを1つ選ぶ。
        /// BET 1〜3以外、または対象BETの通常役がすべて未設定の場合は余り指定のハズレになる。
        /// BET数が遊技上有効かどうかの判定は呼び出し側で行う。
        /// </summary>
        public string DrawFlag(int bet)
        {
            return DrawFlag(bet, 0);
        }

        /// <summary>指定されたBET数と設定（0始まり）の抽選比率で、フラグを1つ選ぶ。</summary>
        public string DrawFlag(int bet, int setting)
        {
            if (winpairNames == null) return "";

            int entryCount = winpairNames.Length;
            if (setting < 0 || setting >= GetSettingCount()) setting = 0;
            int offset = setting == 0 ? 0 : (setting - 1) * entryCount;
            float[] probabilityRatios = GetProbabilityRatios(bet, setting);
            if (probabilityRatios == null) return GetRemainderFlagName();

            int remainderFlagIndex = GetRemainderFlagIndex(entryCount);
            if (remainderFlagIndex >= 0)
            {
                return DrawFlagFromFixedRange(entryCount, remainderFlagIndex, probabilityRatios, offset);
            }

            return DrawFlagFromRelativeRatios(entryCount, probabilityRatios, offset);
        }

        /// <summary>ハズレ指定がない既存データを、各値の相対比率で抽選する。</summary>
        private string DrawFlagFromRelativeRatios(int entryCount, float[] probabilityRatios, int offset)
        {
            float totalWeight = 0f;
            for (int i = 0; i < entryCount; i++)
            {
                float ratio = GetProbabilityRatio(probabilityRatios, offset + i);
                if (ratio > 0f) totalWeight += ratio;
            }

            if (totalWeight <= 0f) return "";

            float selectedWeight = Random.value * totalWeight;
            float accumulatedWeight = 0f;
            string lastPositiveFlag = "";
            for (int i = 0; i < entryCount; i++)
            {
                float weight = GetProbabilityRatio(probabilityRatios, offset + i);
                if (weight <= 0f) continue;

                lastPositiveFlag = winpairNames[i];
                accumulatedWeight += weight;
                if (selectedWeight < accumulatedWeight) return winpairNames[i];
            }

            return lastPositiveFlag;
        }

        /// <summary>
        /// 最大確率分母を抽選領域とし、各フラグへ設定値分の領域を割り当てる。
        /// どの通常フラグにも入らなかった領域は、ハズレフラグへ割り当てる。
        /// </summary>
        private string DrawFlagFromFixedRange(
            int entryCount,
            int remainderFlagIndex,
            float[] probabilityRatios,
            int offset)
        {
            float denominator = maximumProbabilityDenominator;
            if (denominator <= 0f) return "";

            float selectedPosition = Random.value * denominator;
            float assignedEnd = 0f;

            for (int i = 0; i < entryCount; i++)
            {
                if (i == remainderFlagIndex) continue;

                float ratio = GetProbabilityRatio(probabilityRatios, offset + i);
                if (ratio <= 0f) continue;

                float remainingRange = denominator - assignedEnd;
                if (remainingRange <= 0f) break;
                if (ratio > remainingRange) ratio = remainingRange;

                assignedEnd += ratio;
                if (selectedPosition < assignedEnd) return winpairNames[i];
            }

            return winpairNames[remainderFlagIndex];
        }

        /// <summary>BET数と設定に対応する抽選比率配列を返す。</summary>
        private float[] GetProbabilityRatios(int bet, int setting)
        {
            if (setting > 0)
            {
                switch (bet)
                {
                    case 1:
                        return settingRatiosBet1;
                    case 2:
                        return settingRatiosBet2;
                    case 3:
                        return settingRatiosBet3;
                    default:
                        return null;
                }
            }

            switch (bet)
            {
                case 1:
                    return winpairProbabilitiesBet1;
                case 2:
                    return winpairProbabilitiesBet2;
                case 3:
                    return winpairProbabilities;
                default:
                    return null;
            }
        }

        /// <summary>配列外の要素を未設定（比率0）として抽選比率を返す。</summary>
        private float GetProbabilityRatio(float[] probabilityRatios, int index)
        {
            if (probabilityRatios == null || index < 0 || index >= probabilityRatios.Length) return 0f;
            return probabilityRatios[index];
        }

        /// <summary>ハズレフラグの内部使用名を返す。未設定の場合は空文字。</summary>
        private string GetRemainderFlagName()
        {
            int entryCount = winpairNames == null ? 0 : winpairNames.Length;
            int remainderFlagIndex = GetRemainderFlagIndex(entryCount);
            if (remainderFlagIndex < 0 || remainderFlagIndex >= entryCount) return "";
            return winpairNames[remainderFlagIndex];
        }

        /// <summary>余り領域を受け取るハズレフラグの位置を返す。</summary>
        private int GetRemainderFlagIndex(int entryCount)
        {
            if (isRemainderFlags == null) return -1;
            if (isRemainderFlags.Length < entryCount) entryCount = isRemainderFlags.Length;

            for (int i = 0; i < entryCount; i++)
            {
                if (isRemainderFlags[i]) return i;
            }

            return -1;
        }

        /// <summary>内部使用名に対応する表示名を返す。</summary>
        public string GetFlagDisplayName(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return "";
            if (winpairNames == null) return internalName;

            for (int i = 0; i < winpairNames.Length; i++)
            {
                if (winpairNames[i] != internalName) continue;
                if (flagDisplayNames == null || flagDisplayNames.Length <= i ||
                    string.IsNullOrEmpty(flagDisplayNames[i])) return internalName;
                return flagDisplayNames[i];
            }

            return internalName;
        }

        /// <summary>設定の数。1以上。</summary>
        public int GetSettingCount()
        {
            return settingNames == null || settingNames.Length == 0 ? 1 : settingNames.Length;
        }

        /// <summary>設定の名前。未設定なら1始まりの番号。</summary>
        public string GetSettingName(int setting)
        {
            if (settingNames == null || setting < 0 || setting >= settingNames.Length ||
                string.IsNullOrEmpty(settingNames[setting])) return (setting + 1).ToString();
            return settingNames[setting];
        }

        /// <summary>フラグと同時に成立するボーナスの内部名。なければ空文字。</summary>
        public string GetEntryBonusName(string flagName)
        {
            int index = FindEntry(flagName);
            if (index < 0 || entryBonusNames == null || index >= entryBonusNames.Length) return "";
            string name = entryBonusNames[index];
            return name == null ? "" : name;
        }

        /// <summary>ボーナス付きフラグの元のフラグ名。なければフラグ名そのもの。</summary>
        public string GetEntryBaseFlag(string flagName)
        {
            int index = FindEntry(flagName);
            if (index < 0 || entryBaseFlags == null || index >= entryBaseFlags.Length ||
                string.IsNullOrEmpty(entryBaseFlags[index])) return flagName;
            return entryBaseFlags[index];
        }

        /// <summary>フラグの内部使用名が登録されているか確認する。</summary>
        public bool HasFlag(string flagName)
        {
            return FindEntry(flagName) >= 0;
        }

        private int FindEntry(string flagName)
        {
            if (string.IsNullOrEmpty(flagName) || winpairNames == null) return -1;
            for (int i = 0; i < winpairNames.Length; i++)
            {
                if (winpairNames[i] == flagName) return i;
            }

            return -1;
        }

        /// <summary>
        /// 新方式の当選役定義が1件以上存在するか確認する。
        /// 未設定の場合は、既存データとの互換処理に切り替えるために使用する。
        /// </summary>
        public bool HasWinningRoleDefinitions()
        {
            return winningRoleNames != null && winningRoleNames.Length > 0;
        }

        /// <summary>指定されたフラグに、対象の役が当選役として登録されているか確認する。</summary>
        public bool IsRoleWon(string flagName, string roleName)
        {
            if (string.IsNullOrEmpty(flagName) || string.IsNullOrEmpty(roleName)) return false;
            if (winpairNames == null || winningRoleStartIndexes == null ||
                winningRoleCounts == null || winningRoleNames == null) return false;

            int flagCount = winpairNames.Length;
            if (winningRoleStartIndexes.Length < flagCount) flagCount = winningRoleStartIndexes.Length;
            if (winningRoleCounts.Length < flagCount) flagCount = winningRoleCounts.Length;

            for (int flagIndex = 0; flagIndex < flagCount; flagIndex++)
            {
                if (winpairNames[flagIndex] != flagName) continue;

                int startIndex = winningRoleStartIndexes[flagIndex];
                int roleCount = winningRoleCounts[flagIndex];
                if (startIndex < 0 || roleCount < 0) return false;

                int endIndex = startIndex + roleCount;
                if (endIndex > winningRoleNames.Length) endIndex = winningRoleNames.Length;
                for (int roleIndex = startIndex; roleIndex < endIndex; roleIndex++)
                {
                    if (winningRoleNames[roleIndex] == roleName) return true;
                }

                return false;
            }

            return false;
        }
    }
}
