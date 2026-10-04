# Test shipped UI-independent evaluator methods and the actual PayoutTable with synthetic reels.
$ErrorActionPreference = 'Stop'
$editor = Get-Content (Join-Path $PSScriptRoot '../Editor/Slot/ReelControllDataEditor.cs') -Raw
$payout = Get-Content (Join-Path $PSScriptRoot '../Runtime/REV1_0/Slot/PayoutTable.cs') -Raw
$controlData = Get-Content (Join-Path $PSScriptRoot '../Runtime/REV1_0/Slot/ReelControlData.cs') -Raw
$reachability = $editor.Substring($editor.IndexOf('        private List<int[]> GetReachableStates'), $editor.IndexOf('        private string StateLabel') - $editor.IndexOf('        private List<int[]> GetReachableStates'))
$helpers = $editor.Substring($editor.IndexOf('        private int Pattern'), $editor.IndexOf('        private string PositionLabel') - $editor.IndexOf('        private int Pattern'))
$evaluator = Get-Content (Join-Path $PSScriptRoot '../Editor/Slot/ReelSlipEvaluator.cs') -Raw
$evaluation = $evaluator.Substring($evaluator.IndexOf('        public sealed class Candidate'))
$evaluation = $evaluation.Substring(0, $evaluation.LastIndexOf('    }'))
$stubs = @'
namespace UdonSharp { public class UdonSharpBehaviour {} }
namespace UnityEngine {
    public static class Mathf { public static int Clamp(int v, int min, int max) { return System.Math.Max(min, System.Math.Min(max, v)); } }
    public class HideInInspector : System.Attribute {}
    public class SerializeField : System.Attribute {}
    public class InspectorName : System.Attribute { public InspectorName(string name) {} }
}
namespace VRC.Udon.Serialization.OdinSerializer { public class OdinSerialize : System.Attribute {} }
namespace AX.UdonSlot.REV1_0 {
    public enum ReelControlPriority { Payout, CombinationCount }
    public enum ReelRolePriority { SmallRoleFirst, BonusFirst }
    public class ReelData { public string[] symbols; }
    public class ReelOutputController { public ReelData[] reelDatas; }
}
'@
$prefix = @'
using System;
using System.Collections.Generic;
namespace AX.UdonSlot.REV1_0 {
public class CandidateChecks {
    private static readonly int[] Patterns = {16, 27};
    private int patternSelection;
    private int[] positions = {-1, -1, -1};
    private ReelOutputController controller;
    private PayoutTable payout;
    private int bet = 3, maximumSlip = 4;
    private ReelControlPriority priority = ReelControlPriority.Payout;
    private ReelRolePriority rolePriority;
    private HashSet<string> allowedRoles = new HashSet<string>();
'@
$suffix = @'
    static int checks;
    static void Check(string name, int actual, int expected) {
        if (actual != expected) throw new Exception(name + ": expected " + expected + ", actual " + actual);
        checks++;
    }
    static CandidateChecks Setup(params string[] symbols) {
        var c = new CandidateChecks();
        c.controller = new ReelOutputController { reelDatas = new[] {
            new ReelData { symbols = new[] {"A", "B", "C", "D", "E"} },
            new ReelData { symbols = new[] {"A", "B", "C", "D", "E"} },
            new ReelData { symbols = new[] {"A", "B", "C", "D", "E"} } } };
        int n = symbols.Length;
        c.payout = new PayoutTable {
            winPairNames = new string[n], leftSymbols = symbols, centerSymbols = symbols, rightSymbols = symbols,
            payouts = new int[n], isReelStopOrderAny = new bool[n], leftStopOrders = new int[n],
            centerStopOrders = new int[n], rightStopOrders = new int[n], isBonuses = new bool[n],
            payLineBetMasks = new[] {4}, payLineLeftRows = new[] {2}, payLineCenterRows = new[] {2}, payLineRightRows = new[] {2} };
        for (int i = 0; i < n; i++) { c.payout.winPairNames[i] = "R" + i; c.payout.payouts[i] = i + 1; c.payout.isReelStopOrderAny[i] = true; }
        return c;
    }
    public static string Run() {
        var c = Setup("A"); c.allowedRoles.Add("R0");
        Check("First stop pulls target", c.Recommend(1), 1);
        Check("No unnecessary slip", c.Recommend(0), 0);
        Check("Possible full role", c.Evaluate(1, 1).level, 2);
        c.maximumSlip = 0;
        Check("Maximum slip respected", c.Recommend(1), 0);
        c = Setup("E"); c.allowedRoles.Add("R0");
        Check("Wrap at reel boundary", c.Recommend(0), 1);
        c = Setup("A"); c.allowedRoles.Add("R0"); c.patternSelection = 1; c.positions = new[] {0, 0, -1};
        Check("Third stop uses previous output", c.Recommend(1), 1);
        c.positions[0] = 1;
        Check("Missed full role retains target reel match", c.Evaluate(1, 1).level, 1);
        c = Setup("A"); c.patternSelection = 1; c.positions = new[] {0, 0, -1};
        Check("Avoid forbidden completed role", c.Recommend(0), 1);
        Check("Warn forbidden role", c.Evaluate(0, 0).forbidden, 1);
        c = Setup("A", "B"); c.allowedRoles.UnionWith(new[] {"R0", "R1"}); c.payout.isBonuses[1] = true;
        Check("Small role first", c.Recommend(2), 2);
        c.rolePriority = ReelRolePriority.BonusFirst;
        Check("Bonus first", c.Recommend(2), 1);
        c = Setup("A"); c.allowedRoles.Add("R0"); c.bet = 1;
        Check("Inactive BET line ignored", c.Recommend(1), 0);
        c = Setup(" A, E "); c.allowedRoles.Add("R0");
        Check("OR symbols and whitespace", c.Recommend(0), 0);
        c = Setup("A"); c.allowedRoles.Add("R0"); c.payout.isReelStopOrderAny[0] = false;
        c.payout.leftStopOrders[0] = 2; c.payout.centerStopOrders[0] = 1; c.payout.rightStopOrders[0] = 3;
        Check("Wrong order ignored", c.Recommend(1), 0);
        c = Setup("A"); c.patternSelection = 1;
        var data = new ReelControlData { IsReelStopTableStyle = new bool[58], reelStopSlipCount = new int[58][] };
        data.IsReelStopTableStyle[16] = true; data.IsReelStopTableStyle[24] = true;
        data.reelStopSlipCount[16] = new[] {0, 1, 2, 3, 4};
        data.reelStopSlipCount[24] = new[] {4, 0, 1, 2, 3};
        var states = c.GetReachableStates(data, false);
        Check("Previous tables reduce 25 combinations to one", states.Count, 1);
        Check("First final position", states[0][0], 0);
        Check("Second final position including wrap", states[0][1], 1);
        Check("Unreachable state rejected", c.IsReachable(states, new[] {1, 1, -1}) ? 1 : 0, 0);
        Check("Wildcard covers reachable state", c.IsReachable(states, new[] {-1, 1, -1}) ? 1 : 0, 1);
        data.reelStopSlipCount[16] = new[] {0, 0, 2, 2, 4};
        data.conditionPatterns = new[] {24}; data.conditionLeftPositions = new[] {1};
        data.conditionCenterPositions = new[] {-1}; data.conditionRightPositions = new[] {-1};
        data.conditionSlipOffsets = new[] {0}; data.conditionSlipLengths = new[] {5};
        data.conditionSlips = new[] {3, 4, 0, 1, 2};
        states = c.GetReachableStates(data, false);
        Check("Correlated outputs are not Cartesian product", states.Count, 2);
        Check("Conditional second stop used", c.IsReachable(states, new[] {1, 2, -1}) ? 1 : 0, 1);
        Check("Impossible cross-pair excluded", c.IsReachable(states, new[] {0, 2, -1}) ? 1 : 0, 0);
        data.conditionSlips[0] = -1;
        Check("Conditional unset cell falls back to base", c.GetReachableStates(data, false).Count, 3);
        data.IsReelStopTableStyle[24] = false;
        Check("Dynamic previous stop conservatively keeps all positions", c.GetReachableStates(data, false).Count, 10);
        Check("All-dynamic override keeps all combinations", c.GetReachableStates(data, true).Count, 25);
        return checks + " candidate checks passed.";
    }
}}
'@
$payoutBody = $payout.Replace('using UdonSharp;', '').Replace('using UnityEngine;', '')
$controlBody = $controlData.Replace('using UdonSharp;', '').Replace('using UnityEngine;', '')
Add-Type -TypeDefinition ('using UdonSharp; using UnityEngine;' + $prefix + $helpers + $reachability + $evaluation + $suffix + $payoutBody + $controlBody + $stubs)
[AX.UdonSlot.REV1_0.CandidateChecks]::Run()

