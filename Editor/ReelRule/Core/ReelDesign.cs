using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // エディタで編集する設計データ。Unity のシリアライズに乗るよう、公開フィールドと List だけで組む。

    public enum ControlPriority { BonusFirst, SmallFirst }

    /// <summary>規格（号機）。役物の上限値の確認と初期値に使う。</summary>
    public enum Regulation { Gen6, Gen5 }

    /// <summary>規格ごとの役物の上限値。</summary>
    public static class RegulationLimits
    {
        public static readonly string[] Names = { "6号機", "5号機" };

        /// <summary>BBの終了枚数の上限（この枚数以下で定めた枚数を超えたら終了）。</summary>
        public static int BigPayout(Regulation r) { return r == Regulation.Gen5 ? 465 : 285; }

        /// <summary>MBの終了枚数の上限。</summary>
        public static int MiddlePayout(Regulation r) { return r == Regulation.Gen5 ? 253 : 153; }

        public const int RegularWins = 8;
        public const int RegularGames = 12;
    }

    /// <summary>移行図柄の出し方。</summary>
    [Serializable]
    public sealed class DesignTransition
    {
        /// <summary>配当表の役名（種別が移行図柄のもの）。</summary>
        public string role = "";
        /// <summary>出してよいフラグ。空ならどの制御でも引き込みも回避もしない。</summary>
        public List<string> flags = new List<string>();
        /// <summary>true なら押し順不正解のときだけ（記録用。制御では小役の後に狙う）。</summary>
        public bool wrongOrderOnly;
    }

    /// <summary>フロー図のノード位置。</summary>
    [Serializable]
    public sealed class FlowNodePosition
    {
        public string key = "";
        public float x;
        public float y;
    }

    /// <summary>リール制御の設計一式（出目と制御）。</summary>
    [Serializable]
    public sealed class ReelDesign
    {
        public ControlPriority priority = ControlPriority.BonusFirst;
        /// <summary>最大滑り。-1 なら機種の maxSlipCount。</summary>
        public int maxSlip = -1;
        public List<DesignShape> shapes = new List<DesignShape>();
        public List<DesignControl> controls = new List<DesignControl>();
        public Regulation regulation = Regulation.Gen6;
        public List<DesignTransition> transitions = new List<DesignTransition>();
        public List<FlowNodePosition> flowNodes = new List<FlowNodePosition>();

        public DesignTransition FindTransition(string role)
        {
            foreach (var t in transitions) if (t.role == role) return t;
            return null;
        }

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
        /// <summary>true なら禁止目（bannedControls の制御では出さない）。</summary>
        public bool forbidden;
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
        public List<string> bannedControls = new List<string>();

        /// <summary>許可した制御以外では出さない出目（リーチ目・禁止目）。</summary>
        public bool Exclusive { get { return reach || forbidden; } }

        /// <summary>この制御で出してよいか。</summary>
        public bool AllowedIn(string control)
        {
            if (forbidden) return !bannedControls.Contains(control);
            if (reach) return allowedControls.Contains(control);
            return true;
        }

        /// <summary>制御名の変更・複製・削除を出目の制御リストに反映する。</summary>
        public void RenameControl(string old, string renamed)
        {
            foreach (var list in new[] { allowedControls, bannedControls })
            {
                int i = list.IndexOf(old);
                if (i >= 0) list[i] = renamed;
            }
        }

        public void CopyControl(string source, string copy)
        {
            foreach (var list in new[] { allowedControls, bannedControls })
                if (list.Contains(source) && !list.Contains(copy)) list.Add(copy);
        }

        public void RemoveControl(string name)
        {
            allowedControls.Remove(name);
            bannedControls.Remove(name);
        }

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
            copy.bannedControls = new List<string>(bannedControls);
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

    /// <summary>制御1件。抽選フラグ（＋成立中のボーナス・RT）からこの制御が選ばれる。対応はベイクで ReelControl に書き出す。</summary>
    [Serializable]
    public sealed class DesignControl
    {
        public string name = "";
        public List<string> lotteryFlags = new List<string>();
        /// <summary>抽選表のフラグ以外に当選として扱う役（成立中のボーナスなど）。</summary>
        public List<string> extraRoles = new List<string>();
        /// <summary>成立中のボーナスの内部名。空ならボーナスの有無を問わない。</summary>
        public string bonus = "";
        /// <summary>RT 状態の内部名。空ならどの状態でも使う。</summary>
        public string state = "";
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
                bonus = bonus,
                state = state,
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
        private static List<Goal> StableSortByPayout(List<Goal> pulls, Machine machine)
        {
            var sorted = new List<Goal>();
            foreach (var pull in pulls)
            {
                var role = machine.FindRole(pull.Target);
                int payout = role != null ? role.Payout : 0;
                int at = sorted.Count;
                while (at > 0)
                {
                    var prev = machine.FindRole(sorted[at - 1].Target);
                    if ((prev != null ? prev.Payout : 0) >= payout) break;
                    at--;
                }
                sorted.Insert(at, pull);
            }
            return sorted;
        }

        public static RuleSet ToRuleSet(ReelDesign design, Machine machine)
        {
            var errors = new List<RuleError>();
            var rules = new RuleSet { MaxSlip = design.maxSlip, OnlyListedControls = true };
            int maxSlip = design.maxSlip >= 0 ? design.maxSlip : machine.MaxSlip;
            if (maxSlip > 4) errors.Add(new RuleError(0, "最大滑りは4までです（" + maxSlip + "）"));
            int maxLength = 0;
            if (machine.Strips != null) foreach (var strip in machine.Strips) if (strip != null) maxLength = Math.Max(maxLength, strip.Length);

            var shapeNames = new HashSet<string>();
            foreach (var d in design.shapes)
            {
                if (string.IsNullOrEmpty(d.name)) { errors.Add(new RuleError(0, "名前のない出目があります")); continue; }
                if (!shapeNames.Add(d.name)) { errors.Add(new RuleError(0, "出目の名前が重複しています: " + d.name)); continue; }
                if (d.firstReel < -1 || d.firstReel > 2) errors.Add(new RuleError(0, "出目「" + d.name + "」の押し順が不正です"));
                var shape = new Shape { Name = d.name, Exclusive = d.Exclusive, FirstReel = d.firstReel };
                for (int c = 0; c < 9; c++)
                {
                    if (d.cells == null || c >= d.cells.Count || d.cells[c] == null) continue;
                    shape.Cells[c].Include.AddRange(d.cells[c].include);
                    shape.Cells[c].Exclude.AddRange(d.cells[c].exclude);
                }
                if (d.specificPositions)
                    for (int r = 0; r < 3; r++)
                        if (d.Positions(r).Count > 0)
                        {
                            int length = machine.Strips != null && r < machine.Strips.Length && machine.Strips[r] != null ? machine.Strips[r].Length : 0;
                            foreach (int position in d.Positions(r))
                                if (position < 0 || position >= length)
                                {
                                    errors.Add(new RuleError(0, "出目「" + d.name + "」の停止位置 " + (position + 1) + " がリールにありません"));
                                    break;
                                }
                            shape.Positions[r] = d.Positions(r).ToArray();
                        }
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
                if (!string.IsNullOrEmpty(d.bonus))
                {
                    string startRole;
                    if (!machine.BonusStartRoles.TryGetValue(d.bonus, out startRole))
                        errors.Add(new RuleError(0, "制御 " + d.name + " の成立中のボーナス「" + d.bonus + "」がありません"));
                    else if (startRole.Length > 0 && !rule.ExtraRoles.Contains(startRole)) rule.ExtraRoles.Add(startRole);
                }

                // 優先順: [ボーナス（ボーナス優先時）] → 狙う出目 → 狙わない基本目を避ける → 小役 → [ボーナス（小役優先時）]
                var won = new List<string>();
                foreach (string flagName in d.lotteryFlags)
                {
                    var flag = machine.FindFlag(flagName);
                    if (flag != null) foreach (string role in flag.Roles) if (!won.Contains(role)) won.Add(role);
                }
                foreach (string role in rule.ExtraRoles) if (!won.Contains(role)) won.Add(role);

                // 移行図柄: 出してよいフラグの制御では小役の後に狙い、ほかの制御では出さない。
                // フラグの指定がなければ、どの制御でも引き込みも回避もしない。
                var transitionPulls = new List<Goal>();
                foreach (var role in machine.Roles)
                {
                    if (!role.IsTransition || won.Contains(role.Name)) continue;
                    var t = design.FindTransition(role.Name);
                    if (t == null || t.flags.Count == 0)
                    {
                        rule.ExtraRoles.Add(role.Name);
                        continue;
                    }
                    bool allowed = false;
                    foreach (string flagName in d.lotteryFlags)
                    {
                        var flag = machine.FindFlag(flagName);
                        if (t.flags.Contains(flagName) || (flag != null && t.flags.Contains(flag.BaseName))) allowed = true;
                    }
                    if (!allowed) continue;
                    rule.ExtraRoles.Add(role.Name);
                    transitionPulls.Add(new Goal { Kind = GoalKind.Pull, Target = role.Name });
                }
                bool bonusFirst = design.priority == ControlPriority.BonusFirst;
                var bonusPulls = new List<Goal>();
                var smallPulls = new List<Goal>();
                foreach (string name in won)
                {
                    var role = machine.FindRole(name);
                    var pull = new Goal { Kind = GoalKind.Pull, Target = name };
                    if (role != null && role.IsBonus) bonusPulls.Add(pull); else smallPulls.Add(pull);
                }
                // 小役は配当の多い順（押し順ベルの15枚役を1枚役より先に引き込む）
                smallPulls = StableSortByPayout(smallPulls, machine);
                var targets = new List<Goal>();
                foreach (string target in d.targets)
                {
                    var shape = design.FindShape(target);
                    if (shape == null) { errors.Add(new RuleError(0, "制御 " + d.name + " の狙う出目「" + target + "」がありません")); continue; }
                    if (shape.forbidden)
                    {
                        errors.Add(new RuleError(0, "禁止目「" + target + "」は狙えません（制御 " + d.name + "）"));
                        continue;
                    }
                    if (shape.reach && !shape.allowedControls.Contains(d.name))
                    {
                        errors.Add(new RuleError(0, "リーチ目「" + target + "」は制御 " + d.name + " で出てよい設定になっていません"));
                        continue;
                    }
                    targets.Add(new Goal { Kind = GoalKind.Prefer, Target = target });
                }
                // 基本目を狙う制御では、狙っていない基本目は出さず、小役も狙う出目でだけ揃える（リーチ目だけなら小役は通常どおり）
                bool basicTarget = false;
                foreach (var goal in targets) if (!design.FindShape(goal.Target).Exclusive) basicTarget = true;
                if (bonusFirst) rule.Goals.AddRange(bonusPulls);
                rule.Goals.AddRange(targets);
                if (basicTarget)
                    foreach (var shape in design.shapes)
                        if (!shape.Exclusive && !string.IsNullOrEmpty(shape.name) && !d.targets.Contains(shape.name))
                            rule.Goals.Add(new Goal { Kind = GoalKind.Avoid, Target = shape.name });
                foreach (var pull in smallPulls)
                    rule.Goals.Add(basicTarget ? new Goal { Kind = GoalKind.AvoidRole, Target = pull.Target } : pull);
                if (!bonusFirst) rule.Goals.AddRange(bonusPulls);
                rule.Goals.AddRange(transitionPulls);
                foreach (var shape in design.shapes)
                    if (shape.Exclusive && shape.AllowedIn(d.name)) rule.Allowed.Add(shape.name);

                foreach (var o in d.overrides)
                {
                    if (o.slip < 0 || o.slip > Math.Min(maxSlip, 4) || o.press < 0 || o.press >= maxLength
                        || o.left < -1 || o.left >= maxLength || o.center < -1 || o.center >= maxLength || o.right < -1 || o.right >= maxLength)
                    {
                        errors.Add(new RuleError(0, "制御 " + d.name + " の手動指定（" + (o.press + 1) + "押し → " + o.slip + "コマ）が範囲外です"));
                        continue;
                    }
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
