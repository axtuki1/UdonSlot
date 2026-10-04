using System;
using System.Collections.Generic;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    // Editor-only source of intent. Generated tables are separate assets.
    public class ReelOutcomeRules : ScriptableObject
    {
        public enum Mode { Forbidden, Allowed, Preferred }
        [Serializable] public class Target
        {
            public string id = Guid.NewGuid().ToString("N"), name = "新しい制御条件";
            public List<string> roles = new List<string>();
        }
        [Serializable] public class Permission
        {
            public string targetId;
            public Mode mode;
        }
        [Serializable] public class SymbolCell
        {
            public bool any = true;
            public List<string> symbols = new List<string>();
        }
        [Serializable] public class Shape
        {
            public string id = Guid.NewGuid().ToString("N"), name = "新しい出目";
            public bool enabled = true;
            // Final lower indices. -1 is ANY, not a symbol wildcard.
            public int[] lower = {-1, -1, -1};
            // Defaults preserve the meaning of assets made before symbol conditions existed.
            public bool[] useSymbols = new bool[3];
            public SymbolCell[] cells = NewCells();
            private static SymbolCell[] NewCells()
            {
                var result = new SymbolCell[9];
                for (int i = 0; i < result.Length; i++) result[i] = new SymbolCell();
                return result;
            }
            public bool UsesSymbols(int reel) { return useSymbols != null && useSymbols.Length == 3 && useSymbols[reel]; }
            public List<Permission> permissions = new List<Permission>();
            public Mode For(string targetId)
            {
                var p = permissions.Find(x => x.targetId == targetId);
                return p == null ? Mode.Forbidden : p.mode;
            }
            public bool MatchesReel(int reel, int stop, string[] strip)
            {
                if (!UsesSymbols(reel)) return lower[reel] < 0 || lower[reel] == stop;
                if (strip == null || strip.Length == 0) throw new InvalidOperationException("図柄条件の判定には配列が必要です。");
                for (int row = 0; row < 3; row++)
                {
                    var cell = cells[reel*3+row];
                    if (!cell.any && !cell.symbols.Contains(strip[ReelControlDesign.Normalize(stop+row-2,strip.Length)])) return false;
                }
                return true;
            }
            public bool Matches(int[] stops, ReelControlDesign.FirstStop[] reels = null)
            {
                for (int r = 0; r < 3; r++) if (!MatchesReel(r,stops[r],reels == null ? null : reels[r].symbols)) return false;
                return true;
            }
            public List<int> Candidates(int reel, string[] strip)
            {
                var result = new List<int>();
                for (int p = 0; p < strip.Length; p++) if (MatchesReel(reel,p,strip)) result.Add(p);
                return result;
            }
        }
        public string controllerId, notes;
        public ReelControlDesign.FirstStop[] reels = new ReelControlDesign.FirstStop[0];
        public int bet = 3, maximumSlip = 4;
        public List<Target> targets = new List<Target>();
        public List<Shape> shapes = new List<Shape>();

        public void Validate()
        {
            if (reels == null || reels.Length != 3 || bet < 1 || bet > 3 || maximumSlip < 0 || maximumSlip > 4)
                throw new InvalidOperationException("配列・BET・最大滑りを確認してください。");
            for (int r = 0; r < 3; r++)
                if (reels[r] == null || reels[r].symbols == null || reels[r].symbols.Length == 0)
                    throw new InvalidOperationException("3本のリール配列が必要です。");
            var ids = new HashSet<string>();
            foreach (var t in targets)
                if (t == null || string.IsNullOrEmpty(t.id) || !ids.Add(t.id))
                    throw new InvalidOperationException("制御条件のIDが不正です。");
            foreach (var s in shapes)
            {
                if (!s.enabled) continue;
                if (s.lower == null || s.lower.Length != 3 || (s.useSymbols != null && s.useSymbols.Length != 3))
                    throw new InvalidOperationException(s.name + ": リール条件のデータが不正です。");
                bool constrained = false;
                for (int r = 0; r < 3; r++)
                {
                    if (s.UsesSymbols(r))
                    {
                        if (s.cells == null || s.cells.Length != 9) throw new InvalidOperationException(s.name + ": 図柄条件のデータが不正です。");
                        for (int row = 0; row < 3; row++)
                        {
                            var cell = s.cells[r*3+row];
                            if (cell == null) throw new InvalidOperationException(s.name + ": 段の条件がありません。");
                            if (cell.any) continue;
                            constrained = true;
                            if (cell.symbols == null || cell.symbols.Count == 0)
                                throw new InvalidOperationException(s.name + ": 図柄が未選択です。任意ならANYを選んでください。");
                            foreach (string symbol in cell.symbols)
                                if (string.IsNullOrEmpty(symbol) || Array.IndexOf(reels[r].symbols,symbol) < 0)
                                    throw new InvalidOperationException(s.name + ": 配列にない図柄 " + symbol);
                        }
                    }
                    else if (s.lower[r] < -1 || s.lower[r] >= reels[r].symbols.Length)
                        throw new InvalidOperationException(s.name + ": 停止位置が配列の範囲外です。");
                    else constrained |= s.lower[r] >= 0;
                }
                if (!constrained) throw new InvalidOperationException(s.name + ": 少なくとも1か所の条件を指定してください。");
                var seen = new HashSet<string>();
                foreach (var p in s.permissions)
                    if (!ids.Contains(p.targetId) || !seen.Add(p.targetId) || (int)p.mode < 0 || (int)p.mode > 2)
                        throw new InvalidOperationException(s.name + ": 出現条件が不正です。");
            }
        }
    }
}
