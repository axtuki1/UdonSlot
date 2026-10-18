using System.Collections.Generic;
using AX.UdonSlot.REV1_0.Editor.ReelRule;

// 割り当て表（フラグ × 成立中のボーナス）の制御。
public static partial class Program
{
    private static void CellChecks()
    {
        var m = SampleMachine();
        var cherryBB = new LotteryFlag { Name = "チェリー_BB", BaseFlag = "チェリー", Bonus = "BB" };
        cherryBB.Roles.Add("チェリー");
        cherryBB.Roles.Add("BB");
        m.Flags.Add(cherryBB);
        m.BonusStartRoles["BB"] = "BB";

        var pull = new DesignControl { flag = "チェリー", bonus = "BB", pullBonus = true };
        var keep = new DesignControl { flag = "ハズレ", bonus = "BB", pullBonus = false };
        var normal = new DesignControl { flag = "チェリー" };
        Check("cell: pull flags", string.Join(",", ReelDesign.LotteryFlagsOf(pull, m).ToArray()) == "チェリー,チェリー_BB");
        Check("cell: no-pull flags", string.Join(",", ReelDesign.LotteryFlagsOf(keep, m).ToArray()) == "ハズレ");
        Check("cell: normal flags", string.Join(",", ReelDesign.LotteryFlagsOf(normal, m).ToArray()) == "チェリー");
        Check("cell: won bonus", ReelDesign.WonBonusOf(pull) == "BB" && ReelDesign.WonBonusOf(keep) == "");
        Check("cell: name", DesignControl.CellName("ハズレ", "BB") == "ハズレ_BB" && DesignControl.CellName("ハズレ", "") == "ハズレ");

        var d = new ReelDesign();
        foreach (var flag in new[] { "ハズレ", "リプレイ", "ベル", "スイカ", "チェリー", "BB" })
            d.controls.Add(new DesignControl { flag = flag, name = flag });
        d.controls.Add(new DesignControl { flag = "ハズレ", bonus = "BB", pullBonus = true, name = "ハズレ_BB" });
        d.controls.Add(new DesignControl { flag = "ベル", bonus = "BB", pullBonus = false, name = "ベル_BB" });
        var results = Generate(m, d);
        var nowinBB = Find(results, "ハズレ_BB");
        var bellBB = Find(results, "ベル_BB");
        Check("cell: nowin+BB lines up BB", Rate(nowinBB, "BB") > 0.1);
        Check("cell: bell+BB (no pull) never lines up BB", Rate(bellBB, "BB") == 0);
        Check("cell: bell+BB no violations", bellBB.Stats.ViolationPaths == 0 && nowinBB.Stats.ViolationPaths == 0);

        // 小役優先では小役のあるフラグの成立中マスを使わない（生成しない）。ハズレのマスは使う。
        d.priority = ControlPriority.SmallFirst;
        Check("cell: small-first unused", d.IsUnusedCell(d.FindCell("ベル", "BB"), m) && !d.IsUnusedCell(d.FindCell("ハズレ", "BB"), m) && !d.IsUnusedCell(d.FindCell("ベル", ""), m));
        var small = Generate(m, d);
        Check("cell: small-first skips unused", !small.Exists(r => r.Name == "ベル_BB") && small.Exists(r => r.Name == "ハズレ_BB"));
    }
}
