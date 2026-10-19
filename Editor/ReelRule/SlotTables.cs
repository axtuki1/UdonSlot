using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // 抽選表・配当表・ボーナス表の平坦な配列を、編集しやすい行のリストに読み書きする。
    // 書き込みは SerializedObject 経由（Undo とプレハブの上書きに対応）。

    /// <summary>抽選表のフラグ1行。</summary>
    public sealed class LotteryEntry
    {
        public string Name = "";
        public string Display = "";
        /// <summary>同時に成立するボーナスの内部名。空はなし。</summary>
        public string Bonus = "";
        /// <summary>ボーナス付きフラグの元のフラグ名。空は Name。</summary>
        public string BaseFlag = "";
        public bool Remainder;
        public List<string> Roles = new List<string>();
        /// <summary>[BET 0〜2][設定]。</summary>
        public List<float>[] Ratios = { new List<float>(), new List<float>(), new List<float>() };

        public bool IsBonusRow { get { return Bonus.Length > 0; } }
        public string Base { get { return BaseFlag.Length > 0 ? BaseFlag : Name; } }
        public string Label { get { return Display.Length > 0 ? Display : Name; } }

        public LotteryEntry Copy()
        {
            var copy = (LotteryEntry)MemberwiseClone();
            copy.Roles = new List<string>(Roles);
            copy.Ratios = new[] { new List<float>(Ratios[0]), new List<float>(Ratios[1]), new List<float>(Ratios[2]) };
            return copy;
        }
    }

    public sealed class LotteryModel
    {
        public LotteryTable Table;
        public int Denominator = 65536;
        public List<string> Settings = new List<string>();
        public List<LotteryEntry> Entries = new List<LotteryEntry>();

        public int SettingCount { get { return Math.Max(1, Settings.Count); } }

        public static LotteryModel Load(LotteryTable table)
        {
            var m = new LotteryModel { Table = table };
            var so = new SerializedObject(table);
            m.Denominator = so.FindProperty("maximumProbabilityDenominator").intValue;
            var names = so.FindProperty("winpairNames");
            var displays = so.FindProperty("flagDisplayNames");
            var r1 = so.FindProperty("winpairProbabilitiesBet1");
            var r2 = so.FindProperty("winpairProbabilitiesBet2");
            var r3 = so.FindProperty("winpairProbabilities");
            var rem = so.FindProperty("isRemainderFlags");
            var starts = so.FindProperty("winningRoleStartIndexes");
            var counts = so.FindProperty("winningRoleCounts");
            var roles = so.FindProperty("winningRoleNames");
            var settings = so.FindProperty("settingNames");
            var s1 = so.FindProperty("settingRatiosBet1");
            var s2 = so.FindProperty("settingRatiosBet2");
            var s3 = so.FindProperty("settingRatiosBet3");
            var bonuses = so.FindProperty("entryBonusNames");
            var bases = so.FindProperty("entryBaseFlags");

            for (int i = 0; i < settings.arraySize; i++) m.Settings.Add(settings.GetArrayElementAtIndex(i).stringValue);
            int n = names.arraySize;
            int settingCount = m.SettingCount;
            for (int f = 0; f < n; f++)
            {
                var e = new LotteryEntry
                {
                    Name = names.GetArrayElementAtIndex(f).stringValue ?? "",
                    Display = Str(displays, f),
                    Remainder = f < rem.arraySize && rem.GetArrayElementAtIndex(f).boolValue,
                    Bonus = Str(bonuses, f),
                    BaseFlag = Str(bases, f)
                };
                if (f < starts.arraySize && f < counts.arraySize)
                {
                    int start = starts.GetArrayElementAtIndex(f).intValue;
                    int end = Math.Min(start + counts.GetArrayElementAtIndex(f).intValue, roles.arraySize);
                    for (int k = Math.Max(0, start); k < end; k++) e.Roles.Add(roles.GetArrayElementAtIndex(k).stringValue);
                }
                var first = new[] { r1, r2, r3 };
                var rest = new[] { s1, s2, s3 };
                for (int b = 0; b < 3; b++)
                {
                    e.Ratios[b].Add(Float(first[b], f));
                    for (int s = 1; s < settingCount; s++) e.Ratios[b].Add(Float(rest[b], (s - 1) * n + f));
                }
                m.Entries.Add(e);
            }
            return m;
        }

        public void Save(string label)
        {
            Undo.RecordObject(Table, label);
            var so = new SerializedObject(Table);
            int n = Entries.Count;
            int settingCount = SettingCount;
            so.FindProperty("maximumProbabilityDenominator").intValue = Math.Max(1, Denominator);
            var names = Resize(so, "winpairNames", n);
            var displays = Resize(so, "flagDisplayNames", n);
            var first = new[] { Resize(so, "winpairProbabilitiesBet1", n), Resize(so, "winpairProbabilitiesBet2", n), Resize(so, "winpairProbabilities", n) };
            var rest = new[] { Resize(so, "settingRatiosBet1", (settingCount - 1) * n), Resize(so, "settingRatiosBet2", (settingCount - 1) * n), Resize(so, "settingRatiosBet3", (settingCount - 1) * n) };
            var rem = Resize(so, "isRemainderFlags", n);
            var starts = Resize(so, "winningRoleStartIndexes", n);
            var counts = Resize(so, "winningRoleCounts", n);
            var bonuses = Resize(so, "entryBonusNames", n);
            var bases = Resize(so, "entryBaseFlags", n);
            int roleTotal = 0;
            foreach (var e in Entries) roleTotal += e.Roles.Count;
            var roles = Resize(so, "winningRoleNames", roleTotal);
            var settings = Resize(so, "settingNames", Settings.Count);
            for (int s = 0; s < Settings.Count; s++) settings.GetArrayElementAtIndex(s).stringValue = Settings[s];

            int roleIndex = 0;
            for (int f = 0; f < n; f++)
            {
                var e = Entries[f];
                names.GetArrayElementAtIndex(f).stringValue = e.Name;
                displays.GetArrayElementAtIndex(f).stringValue = e.Display;
                rem.GetArrayElementAtIndex(f).boolValue = e.Remainder;
                bonuses.GetArrayElementAtIndex(f).stringValue = e.Bonus;
                bases.GetArrayElementAtIndex(f).stringValue = e.BaseFlag;
                starts.GetArrayElementAtIndex(f).intValue = roleIndex;
                counts.GetArrayElementAtIndex(f).intValue = e.Roles.Count;
                foreach (string role in e.Roles) roles.GetArrayElementAtIndex(roleIndex++).stringValue = role;
                for (int b = 0; b < 3; b++)
                {
                    first[b].GetArrayElementAtIndex(f).floatValue = Ratio(e, b, 0);
                    for (int s = 1; s < settingCount; s++) rest[b].GetArrayElementAtIndex((s - 1) * n + f).floatValue = Ratio(e, b, s);
                }
            }
            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(Table);
        }

        public static float Ratio(LotteryEntry e, int bet, int setting)
        {
            var list = e.Ratios[bet];
            return setting < list.Count ? list[setting] : 0f;
        }

        public static void SetRatio(LotteryEntry e, int bet, int setting, float value)
        {
            var list = e.Ratios[bet];
            while (list.Count <= setting) list.Add(0f);
            list[setting] = value;
        }

        public void SetSettingCount(int count)
        {
            count = Math.Max(1, count);
            while (Settings.Count < count) Settings.Add((Settings.Count + 1).ToString());
            if (Settings.Count > count) Settings.RemoveRange(count, Settings.Count - count);
            foreach (var e in Entries)
                for (int b = 0; b < 3; b++)
                {
                    var list = e.Ratios[b];
                    // 新しい設定は直前の設定の値で始める
                    while (list.Count < count) list.Add(list.Count > 0 ? list[list.Count - 1] : 0f);
                    if (list.Count > count) list.RemoveRange(count, list.Count - count);
                }
        }

        /// <summary>ハズレ（余り）の値。最大確率分母から他のフラグを引いた残り。</summary>
        public float Remainder(int bet, int setting)
        {
            float used = 0;
            foreach (var e in Entries) if (!e.Remainder) used += Math.Max(0f, Ratio(e, bet, setting));
            return Denominator - used;
        }

        public LotteryEntry Find(string name)
        {
            foreach (var e in Entries) if (e.Name == name) return e;
            return null;
        }

        private static string Str(SerializedProperty array, int index)
        {
            return index < array.arraySize ? array.GetArrayElementAtIndex(index).stringValue ?? "" : "";
        }

        private static float Float(SerializedProperty array, int index)
        {
            return index < array.arraySize ? array.GetArrayElementAtIndex(index).floatValue : 0f;
        }

        internal static SerializedProperty Resize(SerializedObject so, string name, int size)
        {
            var p = so.FindProperty(name);
            p.arraySize = size;
            return p;
        }
    }

    /// <summary>配当表の役1行。</summary>
    public sealed class PayoutRow
    {
        public string Name = "";
        public string Display = "";
        public PayoutRoleType Type;
        public string[] Symbols = { "", "", "" };
        public int[] Payouts = new int[3];
        public bool OrderAny = true;
        public int[] Order = { 1, 2, 3 };

        public string Label { get { return Display.Length > 0 ? Display : Name; } }

        public PayoutRow Copy()
        {
            var copy = (PayoutRow)MemberwiseClone();
            copy.Symbols = (string[])Symbols.Clone();
            copy.Payouts = (int[])Payouts.Clone();
            copy.Order = (int[])Order.Clone();
            return copy;
        }
    }

    /// <summary>有効ライン1本。</summary>
    public sealed class PayLineRow
    {
        public int[] Rows = { 1, 1, 1 };
        public int BetMask = 7;
    }

    public sealed class PayoutModel
    {
        public PayoutTable Table;
        public List<PayoutRow> Rows = new List<PayoutRow>();
        public List<PayLineRow> Lines = new List<PayLineRow>();

        public static PayoutModel Load(PayoutTable t)
        {
            var m = new PayoutModel { Table = t };
            for (int i = 0; i < t.GetWinPairCount(); i++)
            {
                var row = new PayoutRow
                {
                    Name = t.winPairNames[i] ?? "",
                    Display = Get(t.winPairDisplayNames, i, ""),
                    Type = t.IsTransition(i) ? PayoutRoleType.Transition : t.IsReplay(i) ? PayoutRoleType.Replay : t.IsBonus(i) ? PayoutRoleType.Bonus : PayoutRoleType.SmallRole,
                    Symbols = new[] { Get(t.leftSymbols, i, ""), Get(t.centerSymbols, i, ""), Get(t.rightSymbols, i, "") },
                    OrderAny = Get(t.isReelStopOrderAny, i, true),
                    Order = new[] { Get(t.leftStopOrders, i, 1), Get(t.centerStopOrders, i, 2), Get(t.rightStopOrders, i, 3) }
                };
                int bet3 = Get(t.payouts, i, 0);
                row.Payouts = new[] { Get(t.payoutsBet1, i, bet3), Get(t.payoutsBet2, i, bet3), bet3 };
                m.Rows.Add(row);
            }
            for (int i = 0; i < t.GetPayLineCount(); i++)
            {
                m.Lines.Add(new PayLineRow
                {
                    Rows = new[] { Get(t.payLineLeftRows, i, 1), Get(t.payLineCenterRows, i, 1), Get(t.payLineRightRows, i, 1) },
                    BetMask = t.payLineBetMasks[i]
                });
            }
            return m;
        }

        public void Save(string label)
        {
            Undo.RecordObject(Table, label);
            int n = Rows.Count;
            var t = Table;
            t.winPairNames = new string[n];
            t.winPairDisplayNames = new string[n];
            t.payouts = new int[n];
            t.payoutsBet1 = new int[n];
            t.payoutsBet2 = new int[n];
            t.isReplays = new bool[n];
            t.isBonuses = new bool[n];
            t.isTransitions = new bool[n];
            t.leftSymbols = new string[n];
            t.centerSymbols = new string[n];
            t.rightSymbols = new string[n];
            t.isReelStopOrderAny = new bool[n];
            t.leftStopOrders = new int[n];
            t.centerStopOrders = new int[n];
            t.rightStopOrders = new int[n];
            // 旧方式の対応フラグは長さだけ合わせて残す
            t.controlFlags = Fit(t.controlFlags, n, "");
            t.isAlwaysControlCandidate = Fit(t.isAlwaysControlCandidate, n, false);
            for (int i = 0; i < n; i++)
            {
                var r = Rows[i];
                t.winPairNames[i] = r.Name;
                t.winPairDisplayNames[i] = r.Display;
                t.payoutsBet1[i] = r.Payouts[0];
                t.payoutsBet2[i] = r.Payouts[1];
                t.payouts[i] = r.Payouts[2];
                t.isReplays[i] = r.Type == PayoutRoleType.Replay;
                t.isBonuses[i] = r.Type == PayoutRoleType.Bonus;
                t.isTransitions[i] = r.Type == PayoutRoleType.Transition;
                t.leftSymbols[i] = r.Symbols[0];
                t.centerSymbols[i] = r.Symbols[1];
                t.rightSymbols[i] = r.Symbols[2];
                t.isReelStopOrderAny[i] = r.OrderAny;
                t.leftStopOrders[i] = r.Order[0];
                t.centerStopOrders[i] = r.Order[1];
                t.rightStopOrders[i] = r.Order[2];
            }
            int lines = Lines.Count;
            t.payLineBetMasks = new int[lines];
            t.payLineLeftRows = new int[lines];
            t.payLineCenterRows = new int[lines];
            t.payLineRightRows = new int[lines];
            for (int i = 0; i < lines; i++)
            {
                t.payLineBetMasks[i] = Lines[i].BetMask;
                t.payLineLeftRows[i] = Lines[i].Rows[0];
                t.payLineCenterRows[i] = Lines[i].Rows[1];
                t.payLineRightRows[i] = Lines[i].Rows[2];
            }
            EditorUtility.SetDirty(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        public PayoutRow Find(string name)
        {
            foreach (var r in Rows) if (r.Name == name) return r;
            return null;
        }

        private static T Get<T>(T[] array, int index, T fallback)
        {
            return array != null && index < array.Length ? array[index] : fallback;
        }

        private static T[] Fit<T>(T[] array, int size, T fill)
        {
            var result = new T[size];
            for (int i = 0; i < size; i++) result[i] = array != null && i < array.Length ? array[i] : fill;
            return result;
        }
    }

    /// <summary>役物1件。</summary>
    public sealed class BonusRow
    {
        public string Name = "";
        public string Display = "";
        public BonusKind Kind = BonusKind.BB;
        public string StartRole = "";
        public LotteryTable Lottery;
        public int Child = -1;
        public int EndPayout;
        public int EndGames;
        public int EndWins;
        public int RequiredBet;
        public int WonState = -1;
        public int EndState = -1;
        /// <summary>CB作動中にすぐ止まるリール（1=左、2=中、4=右）。</summary>
        public int QuickReels;
        /// <summary>連続作動の役物を作動役で作動させる（手動JACイン）。</summary>
        public bool ManualChild;

        public string Label { get { return Display.Length > 0 ? Display : Name; } }
        public BonusRow Copy() { return (BonusRow)MemberwiseClone(); }
    }

    /// <summary>状態（RT）1件。</summary>
    public sealed class StateRow
    {
        public string Name = "";
        public string Display = "";
        public LotteryTable Lottery;
        public int GameLimit;

        public string Label { get { return Display.Length > 0 ? Display : Name; } }
    }

    /// <summary>図柄表示による移行1件。</summary>
    public sealed class MoveRow
    {
        public int From = -1;
        public string Role = "";
        public string OnlyFlag = "";
        public int To;
    }

    public sealed class BonusModel
    {
        public BonusTable Table;
        public List<BonusRow> Bonuses = new List<BonusRow>();
        public List<StateRow> States = new List<StateRow>();
        public List<MoveRow> Moves = new List<MoveRow>();

        public static BonusModel Load(BonusTable t)
        {
            var m = new BonusModel { Table = t };
            for (int i = 0; i < t.GetBonusCount(); i++)
            {
                m.Bonuses.Add(new BonusRow
                {
                    Name = t.bonusNames[i] ?? "",
                    Display = Get(t.bonusDisplayNames, i, ""),
                    Kind = t.GetBonusKind(i),
                    StartRole = t.GetBonusStartRole(i),
                    Lottery = t.GetBonusLotteryTable(i),
                    Child = Get(t.bonusChildIndexes, i, -1),
                    EndPayout = t.GetBonusEndPayout(i),
                    EndGames = t.GetBonusEndGames(i),
                    EndWins = t.GetBonusEndWins(i),
                    RequiredBet = t.GetBonusRequiredBet(i),
                    WonState = Get(t.bonusWonStates, i, -1),
                    EndState = Get(t.bonusEndStates, i, -1),
                    QuickReels = t.GetBonusQuickReels(i),
                    ManualChild = t.IsBonusManualChild(i)
                });
            }
            for (int i = 0; i < t.GetStateCount(); i++)
            {
                m.States.Add(new StateRow
                {
                    Name = t.stateNames[i] ?? "",
                    Display = Get(t.stateDisplayNames, i, ""),
                    Lottery = t.GetStateLotteryTable(i),
                    GameLimit = t.GetStateGameLimit(i)
                });
            }
            for (int i = 0; i < t.GetMoveCount(); i++)
            {
                m.Moves.Add(new MoveRow
                {
                    From = t.GetMoveFrom(i),
                    Role = t.GetMoveRole(i),
                    OnlyFlag = t.GetMoveOnlyFlag(i),
                    To = Get(t.moveToStates, i, 0)
                });
            }
            return m;
        }

        public void Save(string label)
        {
            Undo.RecordObject(Table, label);
            var t = Table;
            int n = Bonuses.Count;
            t.bonusNames = new string[n];
            t.bonusDisplayNames = new string[n];
            t.bonusKinds = new int[n];
            t.bonusStartRoles = new string[n];
            t.bonusLotteryTables = new LotteryTable[n];
            t.bonusChildIndexes = new int[n];
            t.bonusEndPayouts = new int[n];
            t.bonusEndGames = new int[n];
            t.bonusEndWins = new int[n];
            t.bonusRequiredBets = new int[n];
            t.bonusWonStates = new int[n];
            t.bonusEndStates = new int[n];
            t.bonusQuickReels = new int[n];
            t.bonusManualChild = new int[n];
            for (int i = 0; i < n; i++)
            {
                var b = Bonuses[i];
                t.bonusNames[i] = b.Name;
                t.bonusDisplayNames[i] = b.Display;
                t.bonusKinds[i] = (int)b.Kind;
                t.bonusStartRoles[i] = b.StartRole;
                t.bonusLotteryTables[i] = b.Lottery;
                t.bonusChildIndexes[i] = b.Child;
                t.bonusEndPayouts[i] = b.EndPayout;
                t.bonusEndGames[i] = b.EndGames;
                t.bonusEndWins[i] = b.EndWins;
                t.bonusRequiredBets[i] = b.RequiredBet;
                t.bonusWonStates[i] = b.WonState;
                t.bonusEndStates[i] = b.EndState;
                t.bonusQuickReels[i] = b.QuickReels;
                t.bonusManualChild[i] = b.ManualChild ? 1 : 0;
            }
            int s = States.Count;
            t.stateNames = new string[s];
            t.stateDisplayNames = new string[s];
            t.stateLotteryTables = new LotteryTable[s];
            t.stateGameLimits = new int[s];
            for (int i = 0; i < s; i++)
            {
                t.stateNames[i] = States[i].Name;
                t.stateDisplayNames[i] = States[i].Display;
                t.stateLotteryTables[i] = States[i].Lottery;
                t.stateGameLimits[i] = States[i].GameLimit;
            }
            int mv = Moves.Count;
            t.moveFromStates = new int[mv];
            t.moveRoles = new string[mv];
            t.moveOnlyFlags = new string[mv];
            t.moveToStates = new int[mv];
            for (int i = 0; i < mv; i++)
            {
                t.moveFromStates[i] = Moves[i].From;
                t.moveRoles[i] = Moves[i].Role;
                t.moveOnlyFlags[i] = Moves[i].OnlyFlag;
                t.moveToStates[i] = Moves[i].To;
            }
            EditorUtility.SetDirty(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        public int FindBonus(string name)
        {
            for (int i = 0; i < Bonuses.Count; i++) if (Bonuses[i].Name == name) return i;
            return -1;
        }

        /// <summary>状態を消したとき、参照しているインデックスを詰める。</summary>
        public void RemoveState(int index)
        {
            States.RemoveAt(index);
            foreach (var b in Bonuses)
            {
                b.WonState = Shift(b.WonState, index);
                b.EndState = Shift(b.EndState, index);
            }
            Moves.RemoveAll(m => m.From == index || m.To == index);
            foreach (var m in Moves)
            {
                m.From = Shift(m.From, index);
                m.To = Shift(m.To, index);
            }
        }

        /// <summary>役物を消したとき、連続作動の参照を詰める。</summary>
        public void RemoveBonus(int index)
        {
            Bonuses.RemoveAt(index);
            foreach (var b in Bonuses) b.Child = Shift(b.Child, index);
        }

        private static int Shift(int value, int removed)
        {
            if (value == removed) return -1;
            return value > removed ? value - 1 : value;
        }

        private static T Get<T>(T[] array, int index, T fallback)
        {
            return array != null && index < array.Length ? array[index] : fallback;
        }
    }
}
