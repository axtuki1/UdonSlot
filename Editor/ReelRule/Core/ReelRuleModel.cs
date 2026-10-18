using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // Unityに依存しない純C#のモデル。Unity側のアダプターが機種データからMachineを組み立てる。

    /// <summary>生成に必要な機種データ（配列・有効ライン・成立役・抽選フラグ）。</summary>
    public sealed class Machine
    {
        /// <summary>リールごとの図柄名。インデックスは ReelData.symbols と同じ（下段インデックス）。</summary>
        public string[][] Strips = new string[3][];
        public List<PayLine> Lines = new List<PayLine>();
        public List<Role> Roles = new List<Role>();
        public List<LotteryFlag> Flags = new List<LotteryFlag>();
        public int MaxSlip = 4;
        /// <summary>trueならボーナス役を小役より先に引き込む（暗黙の引込の並び順）。</summary>
        public bool BonusFirst;
        /// <summary>ボーナスの内部名 → 作動役。成立中の制御で当選として扱う。</summary>
        public Dictionary<string, string> BonusStartRoles = new Dictionary<string, string>();

        public Role FindRole(string name)
        {
            for (int i = 0; i < Roles.Count; i++) if (Roles[i].Name == name) return Roles[i];
            return null;
        }

        public LotteryFlag FindFlag(string name)
        {
            for (int i = 0; i < Flags.Count; i++) if (Flags[i].Name == name) return Flags[i];
            return null;
        }

        public bool HasSymbol(string symbol)
        {
            for (int r = 0; r < 3; r++)
                if (Strips[r] != null && Array.IndexOf(Strips[r], symbol) >= 0) return true;
            return false;
        }

        public string SymbolAt(int reel, int lower, int row)
        {
            string[] strip = Strips[reel];
            return strip[Wrap(lower + row - 2, strip.Length)];
        }

        public static int Wrap(int value, int length)
        {
            int m = value % length;
            return m < 0 ? m + length : m;
        }
    }

    /// <summary>有効ライン。Rows[reel] は 0=上段、1=中段、2=下段。</summary>
    public sealed class PayLine
    {
        public int[] Rows = new int[3];
        /// <summary>1BET=1、2BET=2、3BET=4 のビットマスク。</summary>
        public int BetMask = 7;
    }

    /// <summary>配当表の成立役1件。</summary>
    public sealed class Role
    {
        public string Name = "";
        /// <summary>リールごとの許可図柄。空配列はANY。</summary>
        public string[][] Symbols = { new string[0], new string[0], new string[0] };
        /// <summary>リールごとに要求する停止順（1〜3）。null なら停止順を問わない。</summary>
        public int[] StopOrder;
        public bool IsBonus;
        public bool IsReplay;
        /// <summary>移行図柄（払出なし。RTの契機）。</summary>
        public bool IsTransition;
        public int Payout;
    }

    /// <summary>抽選表のフラグ1件と、その当選役。</summary>
    public sealed class LotteryFlag
    {
        public string Name = "";
        /// <summary>ボーナス付きフラグの元のフラグ名。空は Name。</summary>
        public string BaseFlag = "";
        /// <summary>同時に成立するボーナスの内部名。</summary>
        public string Bonus = "";
        public List<string> Roles = new List<string>();

        public string BaseName { get { return string.IsNullOrEmpty(BaseFlag) ? Name : BaseFlag; } }
    }

    /// <summary>3×3の停止形。セルは [段*3+リール]（段は 0=上段）。</summary>
    public sealed class Shape
    {
        public string Name = "";
        /// <summary>true なら許可した制御以外では出さない（リーチ目）。</summary>
        public bool Exclusive;
        public ShapeCell[] Cells = NewCells();
        /// <summary>このリールを第1停止したときだけ有効。-1 なら押し順を問わない。</summary>
        public int FirstReel = -1;
        /// <summary>リールごとに有効な停止位置（下段インデックス）。null ならどこでもよい。</summary>
        public int[][] Positions = new int[3][];
        public int Line;

        public static ShapeCell[] NewCells()
        {
            var cells = new ShapeCell[9];
            for (int i = 0; i < 9; i++) cells[i] = new ShapeCell();
            return cells;
        }

        public ShapeCell Cell(int row, int reel) { return Cells[row * 3 + reel]; }
    }

    /// <summary>停止形の1マス。含む図柄のどれかで、否定した図柄ではないとき合う。どちらも空ならANY。</summary>
    public sealed class ShapeCell
    {
        public List<string> Include = new List<string>();
        public List<string> Exclude = new List<string>();

        public bool IsAny { get { return Include.Count == 0 && Exclude.Count == 0; } }

        public bool Matches(string symbol)
        {
            if (Include.Count > 0 && !Include.Contains(symbol)) return false;
            return !Exclude.Contains(symbol);
        }
    }

    public enum GoalKind { Pull, Prefer, Avoid, AvoidRole, PreferAny }

    /// <summary>制御の優先順位1行。上にある行ほど優先する。</summary>
    public sealed class Goal
    {
        public GoalKind Kind;
        public string Target = "";
        public int Line;
        /// <summary>ルールファイルに書かれていない暗黙の引込なら true。</summary>
        public bool Implicit;
    }

    /// <summary>ReelControl の検索名1件ぶんのルール。</summary>
    public sealed class ControlRule
    {
        public string Name = "";
        /// <summary>当選役を取り出す抽選フラグ名。空なら Name と同じ。LotteryFlags を指定したときは使わない。</summary>
        public string LotteryFlag = "";
        /// <summary>当選役を取り出す抽選フラグ（複数）。null なら LotteryFlag を使う。</summary>
        public List<string> LotteryFlags;
        public List<string> ExtraRoles = new List<string>();
        public List<Goal> Goals = new List<Goal>();
        public List<string> Denied = new List<string>();
        public List<string> Allowed = new List<string>();
        public bool NoImplicitPull;
        public bool Explicit;
        public int Line;
        /// <summary>手動指定の滑り。キーは ReelRuleEngine.Key(停止パターン, 停止済み位置)、値は 押下位置→滑り。</summary>
        public Dictionary<long, Dictionary<int, int>> Overrides = new Dictionary<long, Dictionary<int, int>>();

        public string SourceFlag { get { return string.IsNullOrEmpty(LotteryFlag) ? Name : LotteryFlag; } }
    }

    /// <summary>生成の入力一式。</summary>
    public sealed class RuleSet
    {
        public int MaxSlip = -1;
        public int Bet = 3;
        public List<Shape> Shapes = new List<Shape>();
        public List<ControlRule> Controls = new List<ControlRule>();
        /// <summary>true なら Controls に書いた制御だけを生成する（抽選表の全フラグを自動で足さない）。</summary>
        public bool OnlyListedControls;

        public Shape FindShape(string name)
        {
            for (int i = 0; i < Shapes.Count; i++) if (Shapes[i].Name == name) return Shapes[i];
            return null;
        }

        public ControlRule FindControl(string name)
        {
            for (int i = 0; i < Controls.Count; i++) if (Controls[i].Name == name) return Controls[i];
            return null;
        }
    }

    public sealed class RuleError
    {
        public int Line;
        public string Message;
        public RuleError(int line, string message) { Line = line; Message = message; }
        public override string ToString() { return Line > 0 ? Line + "行目: " + Message : Message; }
    }

    public sealed class RuleException : Exception
    {
        public readonly List<RuleError> Errors;
        public RuleException(List<RuleError> errors) : base(Join(errors)) { Errors = errors; }

        private static string Join(List<RuleError> errors)
        {
            var parts = new string[errors.Count];
            for (int i = 0; i < errors.Count; i++) parts[i] = errors[i].ToString();
            return string.Join("\n", parts);
        }
    }
}
