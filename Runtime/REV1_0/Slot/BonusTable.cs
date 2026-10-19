using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>役物の種別。数値は保存データに使うので並びを変えない。</summary>
    public enum BonusKind
    {
        [InspectorName("SB（普通役物）")]
        SB,
        [InspectorName("RB（第一種特別役物）")]
        RB,
        [InspectorName("CB（第二種特別役物）")]
        CB,
        [InspectorName("BB（第一種特別役物に係る役物連続作動装置）")]
        BB,
        [InspectorName("MB（第二種特別役物に係る役物連続作動装置）")]
        MB
    }

    /// <summary>
    /// 役物（ボーナス）とRT状態の定義表。状態の保持と遷移はReelOutputControllerが行う。
    /// 役物・状態・移行はそれぞれ同じインデックスの平坦な配列で1件になる。
    /// 状態0は基底の状態（通常）。
    /// </summary>
    public class BonusTable : UdonSharpBehaviour
    {
        // ---- 役物 ----

        /// <summary>役物の内部名。抽選表のボーナス名、制御名の {bonus} に使う。</summary>
        [HideInInspector] public string[] bonusNames = new string[0];
        [HideInInspector] public string[] bonusDisplayNames = new string[0];
        /// <summary>BonusKindの数値。</summary>
        [HideInInspector] public int[] bonusKinds = new int[0];

        /// <summary>役物を作動させる配当表の役名。BBから連続作動する役物は空。</summary>
        [HideInInspector] public string[] bonusStartRoles = new string[0];

        /// <summary>作動中に使う抽選表。</summary>
        [HideInInspector] public LotteryTable[] bonusLotteryTables = new LotteryTable[0];

        /// <summary>連続作動させる役物のインデックス（BB→RB、MB→CB）。なしは-1。</summary>
        [HideInInspector] public int[] bonusChildIndexes = new int[0];

        /// <summary>払出がこの枚数を超えたら終了。0は使わない。</summary>
        [HideInInspector] public int[] bonusEndPayouts = new int[0];

        /// <summary>この回数の遊技で終了。0は使わない。</summary>
        [HideInInspector] public int[] bonusEndGames = new int[0];

        /// <summary>この回数の入賞で終了。0は使わない。</summary>
        [HideInInspector] public int[] bonusEndWins = new int[0];

        /// <summary>作動中の規定BET。0は通常の最大BET。</summary>
        [HideInInspector] public int[] bonusRequiredBets = new int[0];

        /// <summary>成立時に移る状態。-1は移らない。</summary>
        [HideInInspector] public int[] bonusWonStates = new int[0];

        /// <summary>終了後に移る状態。-1は状態0。</summary>
        [HideInInspector] public int[] bonusEndStates = new int[0];

        /// <summary>CB作動中に75ms以内（滑り1コマ以内）で止まるリール。1=左、2=中、4=右のビット。</summary>
        [HideInInspector] public int[] bonusQuickReels = new int[0];

        /// <summary>連続作動の役物を図柄で作動させる（手動JACイン）なら1、自動なら0。</summary>
        [HideInInspector] public int[] bonusManualChild = new int[0];

        // ---- 状態（RT） ----

        [HideInInspector] public string[] stateNames = new string[0];
        [HideInInspector] public string[] stateDisplayNames = new string[0];

        /// <summary>状態ごとの抽選表。未設定ならReelOutputControllerの抽選表。</summary>
        [HideInInspector] public LotteryTable[] stateLotteryTables = new LotteryTable[0];

        /// <summary>この回数の遊技で状態0へ戻る。0は無制限。</summary>
        [HideInInspector] public int[] stateGameLimits = new int[0];

        // ---- 図柄表示による移行 ----

        /// <summary>移行元の状態。-1はすべての状態。</summary>
        [HideInInspector] public int[] moveFromStates = new int[0];

        /// <summary>契機になる配当表の役名（移行図柄を含む）。</summary>
        [HideInInspector] public string[] moveRoles = new string[0];

        /// <summary>このフラグのときだけ移行する。空はいつでも。</summary>
        [HideInInspector] public string[] moveOnlyFlags = new string[0];

        [HideInInspector] public int[] moveToStates = new int[0];

        public int GetBonusCount()
        {
            return bonusNames == null ? 0 : bonusNames.Length;
        }

        public int GetStateCount()
        {
            return stateNames == null ? 0 : stateNames.Length;
        }

        public int GetMoveCount()
        {
            return moveRoles == null ? 0 : moveRoles.Length;
        }

        public int FindBonus(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return -1;
            for (int i = 0; i < GetBonusCount(); i++)
            {
                if (bonusNames[i] == internalName) return i;
            }

            return -1;
        }

        public string GetBonusName(int index)
        {
            return index >= 0 && index < GetBonusCount() ? bonusNames[index] : "";
        }

        public string GetBonusDisplayName(int index)
        {
            if (index < 0 || index >= GetBonusCount()) return "";
            string name = GetString(bonusDisplayNames, index);
            return string.IsNullOrEmpty(name) ? bonusNames[index] : name;
        }

        public BonusKind GetBonusKind(int index)
        {
            if (bonusKinds == null || index < 0 || index >= bonusKinds.Length) return BonusKind.BB;
            return (BonusKind)bonusKinds[index];
        }

        public string GetBonusStartRole(int index)
        {
            return GetString(bonusStartRoles, index);
        }

        public LotteryTable GetBonusLotteryTable(int index)
        {
            if (bonusLotteryTables == null || index < 0 || index >= bonusLotteryTables.Length) return null;
            return bonusLotteryTables[index];
        }

        public int GetBonusChild(int index)
        {
            int child = GetInt(bonusChildIndexes, index, -1);
            return child >= 0 && child < GetBonusCount() && child != index ? child : -1;
        }

        public int GetBonusEndPayout(int index) { return GetInt(bonusEndPayouts, index, 0); }
        public int GetBonusEndGames(int index) { return GetInt(bonusEndGames, index, 0); }
        public int GetBonusEndWins(int index) { return GetInt(bonusEndWins, index, 0); }
        public int GetBonusRequiredBet(int index) { return GetInt(bonusRequiredBets, index, 0); }
        public int GetBonusWonState(int index) { return ClampState(GetInt(bonusWonStates, index, -1)); }
        public int GetBonusEndState(int index) { return ClampState(GetInt(bonusEndStates, index, -1)); }

        public int GetBonusQuickReels(int index) { return GetInt(bonusQuickReels, index, 0); }
        public bool IsBonusManualChild(int index) { return GetInt(bonusManualChild, index, 0) != 0; }

        /// <summary>SB・CBは1遊技で終わる。</summary>
        public bool IsSingleGameBonus(int index)
        {
            BonusKind kind = GetBonusKind(index);
            return kind == BonusKind.SB || kind == BonusKind.CB;
        }

        public string GetStateName(int index)
        {
            return index >= 0 && index < GetStateCount() ? stateNames[index] : "";
        }

        public string GetStateDisplayName(int index)
        {
            if (index < 0 || index >= GetStateCount()) return "";
            string name = GetString(stateDisplayNames, index);
            return string.IsNullOrEmpty(name) ? stateNames[index] : name;
        }

        public LotteryTable GetStateLotteryTable(int index)
        {
            if (stateLotteryTables == null || index < 0 || index >= stateLotteryTables.Length) return null;
            return stateLotteryTables[index];
        }

        public int GetStateGameLimit(int index) { return GetInt(stateGameLimits, index, 0); }

        public int GetMoveFrom(int index) { return GetInt(moveFromStates, index, -1); }
        public string GetMoveRole(int index) { return GetString(moveRoles, index); }
        public string GetMoveOnlyFlag(int index) { return GetString(moveOnlyFlags, index); }
        public int GetMoveTo(int index) { return ClampState(GetInt(moveToStates, index, -1)); }

        private int ClampState(int index)
        {
            return index >= 0 && index < GetStateCount() ? index : -1;
        }

        private int GetInt(int[] values, int index, int fallback)
        {
            if (values == null || index < 0 || index >= values.Length) return fallback;
            return values[index];
        }

        private string GetString(string[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length || values[index] == null) return "";
            return values[index];
        }
    }
}
