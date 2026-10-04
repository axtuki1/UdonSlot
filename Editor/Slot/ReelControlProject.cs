using System;
using System.Collections.Generic;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor
{
    // Editing source only. Baked runtime data never refers to this asset.
    public sealed class ReelControlProject : ScriptableObject
    {
        [Serializable] public sealed class Strip { public string[] symbols = new string[0]; }
        [Serializable] public sealed class Cell
        {
            public List<string> symbols = new List<string>(); // Empty = ANY.
            public bool negate;
        }
        [Serializable] public sealed class Shape
        {
            public string name = "停止形";
            public Cell[] cells = NewCells();
            public bool ConstrainsReel(int reel)
            {
                for (int row = 0; row < 3; row++) if (cells[reel*3+row].symbols.Count > 0) return true;
                return false;
            }
            private static Cell[] NewCells()
            {
                var a = new Cell[9]; for (int i = 0; i < 9; i++) a[i] = new Cell(); return a;
            }
            public bool Matches(int[] stops, Strip[] strips)
            {
                for (int r = 0; r < 3; r++) for (int row = 0; row < 3; row++)
                {
                    var cell = cells[r * 3 + row];
                    var options = cell.symbols;
                    if (options.Count > 0)
                    {
                        bool selected = options.Contains(strips[r].symbols[Wrap(stops[r] + row - 2, strips[r].symbols.Length)]);
                        if (cell.negate ? selected : !selected) return false;
                    }
                }
                return true;
            }
        }
        [Serializable] public sealed class Table
        {
            public int pattern, reel, stage;
            public int[] prior = {-1, -1, -1};
            public int[] generated = new int[0], manual = new int[0];
            public bool[] conflict = new bool[0], impossible = new bool[0];
            public bool unsafePath;
            public int Effective(int press) { return manual[press] >= 0 ? manual[press] : generated[press]; }
            public string Key { get { return KeyFor(pattern, prior); } }
        }
        [Serializable] public sealed class Flag
        {
            public string name = "新しい制御";
            public string lotterySourceName = "";
            public string notes = "";
            public List<string> roles = new List<string>();
            public List<Shape> shapes = new List<Shape> {new Shape()};
            public List<Table> tables = new List<Table>();
            public List<Table> detached = new List<Table>();
            public string generationSignature = "";
        }
        public string machineId = "";
        public Strip[] strips = new Strip[0];
        public int bet = 3, maximumSlip = 4;
        public List<Flag> flags = new List<Flag>();
        public int DuplicateControl(int index)
        {
            var source = flags[index];
            string baseName = source.name + "_コピー", name = baseName;
            int suffix = 2;
            while (flags.Exists(f => f.name == name)) name = baseName + "_" + suffix++;
            var copy = new Flag {name=name,lotterySourceName=source.lotterySourceName,notes=source.notes,
                roles=new List<string>(source.roles),generationSignature=source.generationSignature,
                shapes=source.shapes.ConvertAll(s => {
                    var cells = new Cell[s.cells.Length];
                    for (int i = 0; i < cells.Length; i++) cells[i] = new Cell {symbols=new List<string>(s.cells[i].symbols),negate=s.cells[i].negate};
                    return new Shape {name=s.name,cells=cells};
                }),tables=source.tables.ConvertAll(CopyTable),detached=source.detached.ConvertAll(CopyTable)};
            flags.Insert(index+1,copy);
            return index+1;
        }
        private static Table CopyTable(Table source)
        {
            return new Table {pattern=source.pattern,reel=source.reel,stage=source.stage,unsafePath=source.unsafePath,
                prior=(int[])source.prior.Clone(),generated=(int[])source.generated.Clone(),manual=(int[])source.manual.Clone(),
                conflict=(bool[])source.conflict.Clone(),impossible=(bool[])source.impossible.Clone()};
        }
        public static string CopySlips(Table table)
        {
            var values = new string[table.generated.Length];
            for (int i = 0; i < values.Length; i++) values[i] = table.Effective(i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Join("\n",values);
        }
        public static int[] ParseSlips(string text,int count,int maximumSlip)
        {
            var tokens = (text ?? "").Split(new[] {' ', '\t', '\r', '\n', ',', '、', ';'},StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != count) throw new InvalidOperationException("コマ数: " + count + "件必要（" + tokens.Length + "件）");
            var values = new int[count];
            for (int i = 0; i < count; i++)
                if (!int.TryParse(tokens[i],System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out values[i]) || values[i] < 0 || values[i] > maximumSlip)
                    throw new InvalidOperationException((i+1) + "行目: 0〜" + maximumSlip + "の整数が必要です。");
            return values;
        }
        public static readonly int[][] Orders = {
            new[] {0,1,2}, new[] {0,2,1}, new[] {1,0,2}, new[] {1,2,0}, new[] {2,0,1}, new[] {2,1,0}
        };
        public static int Wrap(int p, int n) { return (p % n + n) % n; }
        public static string KeyFor(int pattern, int[] prior) { return pattern + ":" + prior[0] + "," + prior[1] + "," + prior[2]; }
        public static int Pattern(int order, int stage)
        {
            int p = 0; for (int s = 0; s <= stage; s++) p |= (s + 1) << ((2 - Orders[order][s]) * 2); return p;
        }
        public void Validate(Flag flag)
        {
            if (strips == null || strips.Length != 3 || bet < 1 || bet > 3 || maximumSlip < 0 || maximumSlip > 4)
                throw new InvalidOperationException("配列・BET・最大滑りが不正です。");
            foreach (var strip in strips) if (strip == null || strip.symbols == null || strip.symbols.Length == 0)
                throw new InvalidOperationException("リール配列がありません。");
            if (flag.shapes.Count == 0) throw new InvalidOperationException("基本停止形がありません。");
            foreach (var shape in flag.shapes)
            {
                if (shape.cells == null || shape.cells.Length != 9) throw new InvalidOperationException("停止形が不正です。");
                for (int i = 0; i < 9; i++)
                {
                    if (shape.cells[i] == null || shape.cells[i].symbols == null) throw new InvalidOperationException("停止形が不正です。");
                    foreach (var symbol in shape.cells[i].symbols)
                    {
                        if (Array.IndexOf(strips[i / 3].symbols, symbol) < 0) throw new InvalidOperationException("配列にない図柄: " + symbol);
                    }
                }
            }
        }
        public void ResetArrangement(Strip[] current)
        {
            var copy = new Strip[3];
            for (int r = 0; r < 3; r++) copy[r] = new Strip {symbols = (string[])current[r].symbols.Clone()};
            strips = copy;
            foreach (var flag in flags) { flag.tables.Clear(); flag.detached.Clear(); flag.generationSignature = ""; }
        }
    }
}
