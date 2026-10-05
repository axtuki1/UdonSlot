using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // エディタで編集する設計データ。Unity のシリアライズに乗るよう、公開フィールドと List だけで組む。

    public enum ControlPriority { BonusFirst, SmallFirst }

    /// <summary>リール制御の設計一式（出目と制御）。</summary>
    [Serializable]
    public sealed class ReelDesign
    {
        public ControlPriority priority = ControlPriority.BonusFirst;
        /// <summary>最大滑り。-1 なら機種の maxSlipCount。</summary>
        public int maxSlip = -1;
        public List<DesignShape> shapes = new List<DesignShape>();
        public List<DesignControl> controls = new List<DesignControl>();

        /// <summary>抽選表の全フラグから、同名の制御を作る。既にある名前は作らない。</summary>
        public int AddControlsFromLottery(Machine machine)
        {
            int added = 0;
            foreach (var flag in machine.Flags)
            {
                if (FindControl(flag.Name) != null) continue;
                var control = new DesignControl { name = flag.Name };
                control.lotteryFlags.Add(flag.Name);
                controls.Add(control);
                added++;
            }
            return added;
        }

        public DesignControl FindControl(string name)
        {
            foreach (var c in controls) if (c.name == name) return c;
            return null;
        }

        public DesignShape FindShape(string name)
        {
            foreach (var s in shapes) if (s.name == name) return s;
            return null;
        }

        /// <summary>「名前 (1)」「名前 (2)」… のうち空いている名前。</summary>
        public static string NumberedName(string name, Predicate<string> taken)
        {
            for (int i = 1; ; i++)
            {
                string candidate = name + " (" + i + ")";
                if (!taken(candidate)) return candidate;
            }
        }
    }

    /// <summary>出目（基本目・リーチ目）1件。</summary>
    [Serializable]
    public sealed class DesignShape
    {
        public string name = "";
        /// <summary>true ならリーチ目（allowedControls の制御でだけ出る）。</summary>
        public bool reach;
        /// <summary>このリールを第1停止したときだけ使う。-1 ならすべての押し順。</summary>
        public int firstReel = -1;
        /// <summary>true なら停止位置を指定する（リーチ目ミッション向け）。</summary>
        public bool specificPositions;
        /// <summary>停止位置（下段インデックス）。空のリールはどこでもよい。</summary>
        public List<int> leftPositions = new List<int>();
        public List<int> centerPositions = new List<int>();
        public List<int> rightPositions = new List<int>();
        /// <summary>[段*3+リール]、段は 0=上段。</summary>
        public List<DesignCell> cells = NewCells();
        public List<string> allowedControls = new List<string>();

        public static List<DesignCell> NewCells()
        {
            var list = new List<DesignCell>();
            for (int i = 0; i < 9; i++) list.Add(new DesignCell());
            return list;
        }

        public List<int> Positions(int reel)
        {
            return reel == 0 ? leftPositions : reel == 1 ? centerPositions : rightPositions;
        }

        public DesignShape Copy()
        {
            var copy = (DesignShape)MemberwiseClone();
            copy.leftPositions = new List<int>(leftPositions);
            copy.centerPositions = new List<int>(centerPositions);
            copy.rightPositions = new List<int>(rightPositions);
            copy.cells = new List<DesignCell>();
            foreach (var c in cells) copy.cells.Add(c.Copy());
            copy.allowedControls = new List<string>(allowedControls);
            return copy;
        }
    }

    [Serializable]
    public sealed class DesignCell
    {
        public List<string> include = new List<string>();
        public List<string> exclude = new List<string>();

        public bool IsAny { get { return include.Count == 0 && exclude.Count == 0; } }

        /// <summary>パレットでのクリック: 指定なし → 含む → 否定 → 指定なし。</summary>
        public void Cycle(string symbol)
        {
            if (include.Remove(symbol)) exclude.Add(symbol);
            else if (!exclude.Remove(symbol)) include.Add(symbol);
        }

        public DesignCell Copy()
        {
            return new DesignCell { include = new List<string>(include), exclude = new List<string>(exclude) };
        }
    }

    /// <summary>制御1件。name は ReelOutputController.GetReelControlFlagName() が返す検索名。</summary>
    [Serializable]
    public sealed class DesignControl
    {
        public string name = "";
        public List<string> lotteryFlags = new List<string>();
        /// <summary>抽選表のフラグ以外に当選として扱う役（持越し中のボーナスなど）。</summary>
        public List<string> extraRoles = new List<string>();
        /// <summary>狙う出目の名前（上ほど優先）。</summary>
        public List<string> targets = new List<string>();
        public List<DesignOverride> overrides = new List<DesignOverride>();

        public DesignControl Copy()
        {
            var copy = new DesignControl
            {
                name = name,
                lotteryFlags = new List<string>(lotteryFlags),
                extraRoles = new List<string>(extraRoles),
                targets = new List<string>(targets)
            };
            foreach (var o in overrides) copy.overrides.Add(o.Copy());
            return copy;
        }

        public DesignOverride FindOverride(int pattern, int[] prior, int press)
        {
            foreach (var o in overrides)
                if (o.pattern == pattern && o.press == press && o.left == prior[0] && o.center == prior[1] && o.right == prior[2]) return o;
            return null;
        }
    }

    /// <summary>滑り数の手動指定1マス。</summary>
    [Serializable]
    public sealed class DesignOverride
    {
        /// <summary>ReelControlData.GetIndexForReelStopPattern と同じ停止パターン値。</summary>
        public int pattern;
        public int left = -1;
        public int center = -1;
        public int right = -1;
        public int press;
        public int slip;

        public DesignOverride Copy() { return (DesignOverride)MemberwiseClone(); }
    }

    /// <summary>設計データを生成エンジンの入力へ変換する。</summary>
    public static class ReelDesignConverter
    {
        public static RuleSet ToRuleSet(ReelDesign design, Machine machine)
        {
            var errors = new List<RuleError>();
            var rules = new RuleSet { MaxSlip = design.maxSlip, OnlyListedControls = true };

            var shapeNames = new HashSet<string>();
            foreach (var d in design.shapes)
            {
                if (string.IsNullOrEmpty(d.name)) { errors.Add(new RuleError(0, "名前のない出目があります")); continue; }
                if (!shapeNames.Add(d.name)) { errors.Add(new RuleError(0, "出目の名前が重複しています: " + d.name)); continue; }
                var shape = new Shape { Name = d.name, Exclusive = d.reach, FirstReel = d.firstReel };
                for (int c = 0; c < 9; c++)
                {
                    if (d.cells == null || c >= d.cells.Count || d.cells[c] == null) continue;
                    shape.Cells[c].Include.AddRange(d.cells[c].include);
                    shape.Cells[c].Exclude.AddRange(d.cells[c].exclude);
                }
                if (d.specificPositions)
                    for (int r = 0; r < 3; r++)
                        if (d.Positions(r).Count > 0) shape.Positions[r] = d.Positions(r).ToArray();
                rules.Shapes.Add(shape);
            }

            var controlNames = new HashSet<string>();
            foreach (var d in design.controls)
            {
                if (string.IsNullOrEmpty(d.name)) { errors.Add(new RuleError(0, "内部名のない制御があります")); continue; }
                if (!controlNames.Add(d.name)) { errors.Add(new RuleError(0, "内部名が重複しています: " + d.name)); continue; }
                var rule = new ControlRule
                {
                    Name = d.name,
                    LotteryFlags = new List<string>(d.lotteryFlags),
                    NoImplicitPull = true,
                    Explicit = true
                };
                rule.ExtraRoles.AddRange(d.extraRoles);

                // 役を揃えるのが先（役優先）。ボーナスと小役の順は制御方式に従う。
                var won = new List<string>();
                foreach (string flagName in d.lotteryFlags)
                {
                    var flag = machine.FindFlag(flagName);
                    if (flag != null) foreach (string role in flag.Roles) if (!won.Contains(role)) won.Add(role);
                }
                foreach (string role in d.extraRoles) if (!won.Contains(role)) won.Add(role);
                bool bonusFirst = design.priority == ControlPriority.BonusFirst;
                foreach (bool pass in new[] { true, false })
                    foreach (string name in won)
                    {
                        var role = machine.FindRole(name);
                        bool bonus = role != null && role.IsBonus;
                        if ((bonus == bonusFirst) == pass) rule.Goals.Add(new Goal { Kind = GoalKind.Pull, Target = name });
                    }

                foreach (string target in d.targets)
                {
                    var shape = design.FindShape(target);
                    if (shape == null) { errors.Add(new RuleError(0, "制御 " + d.name + " の狙う出目「" + target + "」がありません")); continue; }
                    if (shape.reach && !shape.allowedControls.Contains(d.name))
                    {
                        errors.Add(new RuleError(0, "リーチ目「" + target + "」は制御 " + d.name + " で出てよい設定になっていません"));
                        continue;
                    }
                    rule.Goals.Add(new Goal { Kind = GoalKind.Prefer, Target = target });
                }
                foreach (var shape in design.shapes)
                    if (shape.reach && shape.allowedControls.Contains(d.name)) rule.Allowed.Add(shape.name);

                foreach (var o in d.overrides)
                {
                    long key = ReelRuleEngine.Key(o.pattern, new[] { o.left, o.center, o.right });
                    Dictionary<int, int> map;
                    if (!rule.Overrides.TryGetValue(key, out map)) rule.Overrides[key] = map = new Dictionary<int, int>();
                    map[o.press] = o.slip;
                }
                rules.Controls.Add(rule);
            }
            if (errors.Count > 0) throw new RuleException(errors);
            return rules;
        }
    }
}
