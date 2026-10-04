using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor
{
    public static class ReelArrangementMigration
    {
        public sealed class Result
        {
            public ReelControlDesign.FirstStop[] reels;
            public List<ReelControlDesign.LaterStop> tables = new List<ReelControlDesign.LaterStop>();
            public int unresolved, removedConditions;
        }
        public static int[] DefaultMap(string[] oldSymbols, string[] newSymbols)
        {
            var map = new int[oldSymbols.Length];
            var used = new HashSet<int>();
            for (int p = 0; p < map.Length; p++)
            {
                map[p] = -1;
                // Equal lengths default to preserving physical numbered positions, including symbol replacements.
                if (oldSymbols.Length == newSymbols.Length) { map[p] = p; continue; }
                int candidate = -1, count = 0;
                for (int n = 0; n < newSymbols.Length; n++)
                    if (!used.Contains(n) && oldSymbols[p] == newSymbols[n]) { candidate = n; count++; }
                if (count == 1) { map[p] = candidate; used.Add(candidate); }
            }
            return map;
        }
        public static Result Build(ReelControlDesign source, string[][] symbols, int[][] maps)
        {
            var result = new Result { reels = new ReelControlDesign.FirstStop[3] };
            for (int r = 0; r < 3; r++)
            {
                if (symbols[r] == null || symbols[r].Length == 0 || maps[r].Length != source.reels[r].symbols.Length)
                    throw new InvalidOperationException("配列または対応付けの長さが不正です。");
                var used = new HashSet<int>();
                foreach (int value in maps[r])
                    if (value < -1 || value >= symbols[r].Length || (value >= 0 && !used.Add(value)))
                        throw new InvalidOperationException("対応先が重複しているか範囲外です。同じ位置への統合はできません。");
                result.reels[r] = new ReelControlDesign.FirstStop { symbols = (string[])symbols[r].Clone(),
                    slips = ConvertSlips(source.reels[r].slips, maps[r], symbols[r].Length, result) };
            }
            foreach (var table in source.laterStops)
            {
                int reel = 0;
                for (int r = 1; r < 3; r++)
                    if (((table.pattern >> ((2-r)*2)) & 3) > ((table.pattern >> ((2-reel)*2)) & 3)) reel = r;
                var next = new ReelControlDesign.LaterStop { pattern = table.pattern, useCommon = table.useCommon,
                    slips = ConvertSlips(table.slips, maps[reel], symbols[reel].Length, result) };
                foreach (var condition in table.conditions)
                {
                    int[] positions = {condition.left, condition.center, condition.right};
                    bool removed = false;
                    for (int r = 0; r < 3; r++)
                    {
                        if (positions[r] < 0) continue;
                        if (positions[r] >= maps[r].Length) throw new InvalidOperationException("旧条件の位置が範囲外です。");
                        positions[r] = maps[r][positions[r]];
                        if (positions[r] < 0) removed = true;
                    }
                    if (removed) { result.removedConditions++; continue; }
                    next.conditions.Add(new ReelControlDesign.StopCondition { left = positions[0], center = positions[1], useCommon = condition.useCommon,
                        right = positions[2], slips = ConvertSlips(condition.slips, maps[reel], symbols[reel].Length, result) });
                }
                result.tables.Add(next);
            }
            return result;
        }
        private static int[] ConvertSlips(int[] old, int[] map, int length, Result result)
        {
            if (old == null || old.Length != map.Length) throw new InvalidOperationException("旧滑りテーブルの長さが不正です。");
            var values = new int[length];
            for (int n = 0; n < length; n++) values[n] = -1;
            for (int p = 0; p < old.Length; p++)
            {
                if (old[p] < -1 || old[p] > 4) throw new InvalidOperationException("旧滑り値が範囲外です。");
                if (map[p] < 0 || old[p] < 0) continue;
                int oldStop = ReelControlDesign.Normalize(p - old[p], old.Length);
                if (map[oldStop] < 0) continue;
                int slip = ReelControlDesign.Slip(map[p], map[oldStop], length);
                if (slip <= 4) values[map[p]] = slip;
            }
            foreach (int v in values) if (v < 0) result.unresolved++;
            return values;
        }
    }
}
