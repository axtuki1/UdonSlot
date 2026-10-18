using UdonSharp;
using UnityEngine;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// 制御名と ReelControlData の対応、および抽選フラグから制御を選ぶ対応表を保持する。
    /// 対応表は機種エディタのベイクで書き出す。
    /// </summary>
    public class ReelControl : UdonSharpBehaviour
    {
        [SerializeField]
        public string[] flagNames;
        
        [SerializeField]
        public ReelControlData[] reelControlDataList;

        /// <summary>対応表: 抽選フラグの内部名。</summary>
        [SerializeField]
        public string[] mapFlags = new string[0];

        /// <summary>対応表: 成立中のボーナスの内部名。空ならボーナスの有無を問わない。</summary>
        [SerializeField]
        public string[] mapBonuses = new string[0];

        /// <summary>対応表: RT 状態の内部名。空ならどの状態でも使う。</summary>
        [SerializeField]
        public string[] mapStates = new string[0];

        /// <summary>対応表: 使う制御名（flagNames の名前）。</summary>
        [SerializeField]
        public string[] mapControls = new string[0];

        /// <summary>対応表を持っているか。</summary>
        public bool HasMap()
        {
            return mapFlags != null && mapFlags.Length > 0;
        }

        /// <summary>
        /// 抽選フラグ・成立中のボーナス・RT 状態に合う制御名を返す。条件の多い行を優先する。なければ空文字。
        /// </summary>
        public string FindControlName(string flag, string bonus, string state)
        {
            if (!HasMap() || mapBonuses == null || mapStates == null || mapControls == null) return "";
            int count = mapFlags.Length;
            if (mapBonuses.Length < count || mapStates.Length < count || mapControls.Length < count) return "";
            string found = "";
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (mapFlags[i] != flag) continue;
                string mapBonus = mapBonuses[i];
                string mapState = mapStates[i];
                bool hasBonus = !string.IsNullOrEmpty(mapBonus);
                bool hasState = !string.IsNullOrEmpty(mapState);
                if (hasBonus && mapBonus != bonus) continue;
                if (hasState && mapState != state) continue;
                int score = (hasBonus ? 2 : 0) + (hasState ? 1 : 0);
                if (score <= best) continue;
                best = score;
                found = mapControls[i];
            }
            return found;
        }
    }
}
