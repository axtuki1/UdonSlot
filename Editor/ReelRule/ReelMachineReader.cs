using System;
using System.Collections.Generic;
using UnityEditor;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>シーン上の機種（ReelOutputController）から生成エンジン用の Machine を組み立てる。</summary>
    public static class ReelMachineReader
    {
        public static Machine Read(ReelOutputController machine)
        {
            var m = new Machine();
            if (machine.reelDatas == null || machine.reelDatas.Length != 3) throw new InvalidOperationException("機種の reelDatas が3本ではありません");
            for (int r = 0; r < 3; r++)
            {
                if (machine.reelDatas[r] == null || machine.reelDatas[r].symbols == null) throw new InvalidOperationException("リール配列が未設定です");
                m.Strips[r] = (string[])machine.reelDatas[r].symbols.Clone();
            }
            var so = new SerializedObject(machine);
            m.MaxSlip = so.FindProperty("maxSlipCount").intValue;
            m.BonusFirst = so.FindProperty("rolePriority").enumValueIndex == (int)ReelRolePriority.BonusFirst;
            var payout = so.FindProperty("payoutTable").objectReferenceValue as PayoutTable;
            var lottery = so.FindProperty("lotteryTable").objectReferenceValue as LotteryTable;
            if (payout == null) throw new InvalidOperationException("機種に配当表（PayoutTable）が設定されていません");
            if (lottery == null) throw new InvalidOperationException("機種に抽選表（LotteryTable）が設定されていません");

            for (int i = 0; i < payout.GetPayLineCount(); i++)
            {
                if (!payout.IsPayLineValid(i)) continue;
                m.Lines.Add(new PayLine
                {
                    Rows = new[] { payout.GetPayLineRow(i, 0), payout.GetPayLineRow(i, 1), payout.GetPayLineRow(i, 2) },
                    BetMask = payout.payLineBetMasks[i]
                });
            }
            for (int i = 0; i < payout.GetWinPairCount(); i++)
            {
                if (!payout.IsWinPairValid(i)) continue;
                var role = new Role
                {
                    Name = payout.winPairNames[i],
                    IsBonus = payout.IsBonus(i),
                    IsReplay = payout.IsReplay(i),
                    IsTransition = payout.IsTransition(i),
                    Payout = payout.payouts[i]
                };
                for (int r = 0; r < 3; r++) role.Symbols[r] = SplitSymbols(payout.GetSymbol(i, r));
                if (!payout.isReelStopOrderAny[i])
                    role.StopOrder = new[] { payout.GetStopOrder(i, 0), payout.GetStopOrder(i, 1), payout.GetStopOrder(i, 2) };
                m.Roles.Add(role);
            }

            foreach (var table in LotteryTablesOf(machine)) ReadFlags(m, table, payout);
            var bonusTable = BonusTableOf(machine);
            if (bonusTable != null)
                for (int b = 0; b < bonusTable.GetBonusCount(); b++)
                {
                    string name = bonusTable.GetBonusName(b);
                    if (name.Length > 0 && !m.BonusStartRoles.ContainsKey(name)) m.BonusStartRoles[name] = bonusTable.GetBonusStartRole(b);
                }
            return m;
        }

        /// <summary>抽選表のフラグを Machine に足す。先に読んだ抽選表に同じ名前があれば足さない。</summary>
        private static void ReadFlags(Machine m, LotteryTable lottery, PayoutTable payout)
        {
            var lso = new SerializedObject(lottery);
            var names = lso.FindProperty("winpairNames");
            bool hasRoles = lottery.HasWinningRoleDefinitions();
            for (int f = 0; f < names.arraySize; f++)
            {
                var flag = new LotteryFlag
                {
                    Name = names.GetArrayElementAtIndex(f).stringValue,
                    BaseFlag = Element(lottery.entryBaseFlags, f),
                    Bonus = Element(lottery.entryBonusNames, f)
                };
                if (hasRoles)
                {
                    if (lottery.winningRoleStartIndexes != null && lottery.winningRoleCounts != null &&
                        f < lottery.winningRoleStartIndexes.Length && f < lottery.winningRoleCounts.Length)
                    {
                        int start = lottery.winningRoleStartIndexes[f];
                        int end = Math.Min(start + lottery.winningRoleCounts[f], lottery.winningRoleNames.Length);
                        for (int k = Math.Max(0, start); k < end; k++) flag.Roles.Add(lottery.winningRoleNames[k]);
                    }
                }
                else
                {
                    // 旧方式: 配当表側の対応フラグで当選役を決める
                    for (int i = 0; i < payout.GetWinPairCount(); i++)
                        if (payout.IsControlCandidate(i, flag.Name) && !flag.Roles.Contains(payout.winPairNames[i]))
                            flag.Roles.Add(payout.winPairNames[i]);
                }
                if (m.FindFlag(flag.Name) == null) m.Flags.Add(flag);
            }
        }

        private static string Element(string[] array, int index)
        {
            return array != null && index < array.Length && array[index] != null ? array[index] : "";
        }

        /// <summary>機種で使う抽選表。通常の抽選表、状態ごと、役物ごとの順で重複なし。</summary>
        public static List<LotteryTable> LotteryTablesOf(ReelOutputController machine)
        {
            var list = new List<LotteryTable>();
            var main = LotteryTableOf(machine);
            if (main != null) list.Add(main);
            var bonus = BonusTableOf(machine);
            if (bonus != null)
            {
                if (bonus.stateLotteryTables != null)
                    foreach (var t in bonus.stateLotteryTables) if (t != null && !list.Contains(t)) list.Add(t);
                if (bonus.bonusLotteryTables != null)
                    foreach (var t in bonus.bonusLotteryTables) if (t != null && !list.Contains(t)) list.Add(t);
            }
            return list;
        }

        public static PayoutTable PayoutTableOf(ReelOutputController machine)
        {
            return new SerializedObject(machine).FindProperty("payoutTable").objectReferenceValue as PayoutTable;
        }

        public static BonusTable BonusTableOf(ReelOutputController machine)
        {
            return new SerializedObject(machine).FindProperty("bonusTable").objectReferenceValue as BonusTable;
        }

        private static string[] SplitSymbols(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            var list = new List<string>();
            foreach (string part in text.Split(','))
            {
                string symbol = part.Trim();
                if (symbol.Length > 0) list.Add(symbol);
            }
            return list.ToArray();
        }

        public static string Signature(Machine m)
        {
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < 3; r++) sb.Append(string.Join(",", m.Strips[r])).Append('|');
            foreach (var line in m.Lines) sb.Append(string.Join(",", line.Rows)).Append(':').Append(line.BetMask).Append('|');
            foreach (var role in m.Roles)
            {
                sb.Append(role.Name).Append('=');
                foreach (var symbols in role.Symbols) sb.Append(string.Join("/", symbols)).Append(',');
                if (role.StopOrder != null) sb.Append(string.Join("", role.StopOrder));
                sb.Append(role.IsBonus ? 'B' : '-').Append(role.IsReplay ? 'R' : '-').Append(role.IsTransition ? 'T' : '-').Append(role.Payout).Append('|');
            }
            foreach (var flag in m.Flags) sb.Append(flag.Name).Append('/').Append(flag.BaseFlag).Append('/').Append(flag.Bonus).Append('=').Append(string.Join(",", flag.Roles)).Append('|');
            foreach (var pair in m.BonusStartRoles) sb.Append(pair.Key).Append('>').Append(pair.Value).Append('|');
            sb.Append(m.MaxSlip).Append(m.BonusFirst);
            return sb.ToString();
        }

        public static ReelControl ReelControlOf(ReelOutputController machine)
        {
            return new SerializedObject(machine).FindProperty("reelControl").objectReferenceValue as ReelControl;
        }

        public static LotteryTable LotteryTableOf(ReelOutputController machine)
        {
            return new SerializedObject(machine).FindProperty("lotteryTable").objectReferenceValue as LotteryTable;
        }
    }
}
