using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor
{
    public partial class ReelControlDesign
    {
        // Existing assets default to the legacy independent tables until explicitly converted.
        public bool commonSettingsEnabled;
        public static int ReelForPattern(int pattern)
        {
            int reel = 0;
            for (int r = 1; r < 3; r++)
                if (((pattern >> ((2-r)*2)) & 3) > ((pattern >> ((2-reel)*2)) & 3)) reel = r;
            return reel;
        }
        public int[] BaseValues(LaterStop table)
        {
            return commonSettingsEnabled && table.useCommon ? reels[ReelForPattern(table.pattern)].slips : table.slips;
        }
        public int[] ConditionValues(LaterStop table, StopCondition condition)
        {
            return commonSettingsEnabled && condition.useCommon ? reels[ReelForPattern(table.pattern)].slips : condition.slips;
        }
        private static bool SameValues(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        public void EnableCommonSettings()
        {
            if (commonSettingsEnabled) return;
            if (!laterStopsInitialized) throw new InvalidOperationException("先に後段の編集データを取り込んでください。");
            // Preserve every effective value, including wildcard precedence and explicit unset cells.
            foreach (var table in laterStops)
            {
                var common = reels[ReelForPattern(table.pattern)].slips;
                table.useCommon = SameValues(table.slips, common);
                foreach (var c in table.conditions) c.useCommon = SameValues(c.slips, common);
            }
            commonSettingsEnabled = true;
        }
        public bool UsesCommon(int order, int stage, int[] context)
        {
            if (!commonSettingsEnabled) return false;
            return Object.ReferenceEquals(SlipsFor(order, stage, context), reels[StopOrders[order][stage]].slips);
        }
        public void SetCommonSlip(int reel, int press, int slip)
        {
            if (!commonSettingsEnabled) throw new InvalidOperationException("共通設定を有効にしてください。");
            if (slip < -1 || slip > 4) throw new ArgumentOutOfRangeException("slip");
            if (reels[reel].slips[press] == slip) return;
            var before = CaptureContexts();
            reels[reel].slips[press] = slip;
            ReconcileContexts(before);
        }
        public void UseCommonForContext(int order, int stage, int[] context)
        {
            if (!commonSettingsEnabled || stage == 0) return;
            var before = CaptureContexts();
            var table = LaterTable(StagePattern(order, stage));
            var exact = table.conditions.Find(c => c.Exact(context));
            if (exact == null)
            {
                exact = new StopCondition { left = context[0], center = context[1], right = context[2],
                    slips = (int[])reels[StopOrders[order][stage]].slips.Clone() };
                table.conditions.Insert(0, exact);
            }
            exact.useCommon = true;
            ReconcileContexts(before);
        }
        public void UseCommonForReel(int reel)
        {
            if (!commonSettingsEnabled) throw new InvalidOperationException("共通設定を有効にしてください。");
            var before = CaptureContexts();
            foreach (var table in laterStops)
            {
                if (ReelForPattern(table.pattern) != reel) continue;
                table.useCommon = true;
                foreach (var c in table.conditions) c.useCommon = true;
            }
            ReconcileContexts(before);
        }
        public int ExceptionCount(int reel)
        {
            int count = 0;
            foreach (var table in laterStops)
            {
                if (ReelForPattern(table.pattern) != reel) continue;
                if (!table.useCommon) count++;
                foreach (var c in table.conditions) if (!c.useCommon) count++;
            }
            return count;
        }
        public void ApplyAllOutput(ReelControlData target)
        {
            // Preflight every table and the existing output before any output mutation.
            for (int r = 0; r < 3; r++) BuildOutput(r);
            foreach (var table in laterStops)
            {
                int n = reels[ReelForPattern(table.pattern)].symbols.Length;
                if (BaseValues(table).Length != n) throw new InvalidOperationException("基本テーブルの長さが不正です。");
                ProvisionalValues(BaseValues(table));
                foreach (var c in table.conditions)
                {
                    if (ConditionValues(table, c).Length != n) throw new InvalidOperationException("条件テーブルの長さが不正です。");
                    ProvisionalValues(ConditionValues(table, c));
                }
            }
            for (int order = 0; order < 6; order++)
                for (int stage = 1; stage <= 2; stage++) LaterTable(StagePattern(order, stage));
            if (target.conditionPatterns != null)
                for (int i = 0; i < target.conditionPatterns.Length; i++) ValidateCondition(target, i);
            for (int r = 0; r < 3; r++) ApplyOutput(target, r);
            for (int order = 0; order < 6; order++)
                for (int stage = 1; stage <= 2; stage++) ApplyLaterOutput(target, order, stage);
        }
    }
}
