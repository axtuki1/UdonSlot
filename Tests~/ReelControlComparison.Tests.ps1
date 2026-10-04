# Load the shipped design/output implementations; no Unity native APIs are needed for these checks.
$ErrorActionPreference = 'Stop'
$design = Get-Content (Join-Path $PSScriptRoot '../Editor/Slot/ReelControlDesign.cs') -Raw
$design += [Environment]::NewLine + (Get-Content (Join-Path $PSScriptRoot '../Editor/Slot/ReelControlDesign.Common.cs') -Raw)
$runtime = Get-Content (Join-Path $PSScriptRoot '../Runtime/REV1_0/Slot/ReelControlData.cs') -Raw
$stubs = @'
namespace UnityEngine {
    public class ScriptableObject {}
    public class HideInInspector : System.Attribute {}
    public static class JsonUtility { public static string ToJson(object value) { return ""; } }
    public static class Mathf { public static int Clamp(int v, int min, int max) { return System.Math.Max(min, System.Math.Min(max, v)); } }
}
namespace UdonSharp { public class UdonSharpBehaviour {} }
namespace VRC.Udon.Serialization.OdinSerializer { public class OdinSerialize : System.Attribute {} }
namespace AX.UdonSlot.REV1_0 {
    public class ReelData { public string[] symbols; }
    public class ReelOutputController { public ReelData[] reelDatas; }
}
'@
$comparison = Get-Content (Join-Path $PSScriptRoot '../Editor/Slot/ReelControlComparison.cs') -Raw
$test = @"
namespace AX.UdonSlot.REV1_0.Editor {
public static class ComparisonChecks {
    static int count;
    static void Check(string name, bool value) { if (!value) throw new System.Exception(name); count++; }
    public static string Run() {
        var machine = new ReelOutputController { reelDatas = new ReelData[3] };
        for (int r = 0; r < 3; r++) machine.reelDatas[r] = new ReelData { symbols = new[] { "A", "B", "C", "D", "E" } };
        var a = new ReelControlDesign(); a.Initialize(machine, null);
        var b = new ReelControlDesign(); b.Initialize(machine, null);
        var none = new[] {-1, -1, -1};
        var unset = ReelControlComparison.Evaluate(a,b,0,0,none,0);
        Check("Unset distinct and provisional destination", unset.state == ReelControlComparison.State.Unset && unset.stop == 0);
        b.reels[0].slips[0] = 0;
        var zero = ReelControlComparison.Evaluate(a,b,0,0,none,0);
        Check("Explicit zero differs from unset", !ReelControlComparison.Same(unset,zero));
        b.reels[0].slips[0] = 2;
        var wrap = ReelControlComparison.Evaluate(a,b,0,0,none,0);
        Check("Slip wraps", wrap.stop == 3 && wrap.slip == 2);
        for (int order = 0; order < 6; order++) {
            var result = ReelControlComparison.Evaluate(a,b,order,0,none,1);
            Check("All orders read correct reel", result.state == ReelControlComparison.State.Unset);
        }
        for (int p=0;p<5;p++) b.reels[0].slips[p] = p;
        var unreachable = ReelControlComparison.Evaluate(a,b,0,1,new[] {1,-1,-1},0);
        Check("Unreachable is not unset", unreachable.state == ReelControlComparison.State.Unreachable && !unreachable.HasStop);
        var reachable = ReelControlComparison.Evaluate(a,b,0,1,new[] {0,-1,-1},0);
        Check("Unset at reachable context", reachable.state == ReelControlComparison.State.Unset && !reachable.provisional);
        b.SetSlip(0,1,new[] {0,-1,-1},0,3);
        Check("Conditional slip selected", ReelControlComparison.Evaluate(a,b,0,1,new[] {0,-1,-1},0).slip == 3);
        b.reels[0].slips[1] = -1;
        var provisional = ReelControlComparison.Evaluate(a,b,0,1,new[] {1,-1,-1},0);
        Check("Provisional predecessor reachability", provisional.provisional && provisional.HasStop);
        b.reels[0].slips[2] = 1;
        Check("Definite alternative removes provisional label", !ReelControlComparison.Evaluate(a,b,0,1,new[] {1,-1,-1},0).provisional);
        b.reels[1].symbols[0] = "Changed";
        Check("Same length different symbols rejected", ReelControlComparison.Evaluate(a,b,0,0,none,0).state == ReelControlComparison.State.Incompatible);
        b.reels[1].symbols = new[] {"A"};
        Check("Different lengths rejected", ReelControlComparison.Evaluate(a,b,0,0,none,0).state == ReelControlComparison.State.Incompatible);
        b.Initialize(machine,null);
        for (int order = 0; order < 6; order++) {
            int first = ReelControlDesign.StopOrders[order][0], second = ReelControlDesign.StopOrders[order][1];
            var context = new[] {-1,-1,-1}; context[first] = 1; context[second] = 2;
            b.SetSlip(order,2,context,0,4);
            var third = ReelControlComparison.Evaluate(a,b,order,2,context,0);
            Check("Third stop exact condition in each order", third.slip == 4 && third.stop == 1 && third.provisional);
        }
        b.reels[0].slips[0] = 5;
        Check("Invalid slips rejected", ReelControlComparison.Evaluate(a,b,0,0,none,0).state == ReelControlComparison.State.Invalid);
        Check("Invalid data safe", ReelControlComparison.Evaluate(a,null,0,0,none,0).state == ReelControlComparison.State.Invalid);
        Check("Comparison never changes basis", a.reels[0].slips[0] == -1 && a.laterStops[0].conditions.Count == 0);
        return count + " comparison checks passed";
    }
}}
"@
Add-Type -TypeDefinition ('using System; using System.Collections.Generic; using UnityEngine; using UdonSharp;' + (($stubs, $runtime, $design, $comparison, $test | ForEach-Object { [regex]::Replace($_, '(?m)^using [^;]+;\r?$', '') }) -join "`n"))
[AX.UdonSlot.REV1_0.Editor.ComparisonChecks]::Run()
