# Tests the shipped validator with synthetic reels and the real payout table.
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$designTest = Get-Content (Join-Path $PSScriptRoot 'ReelControlDesign.Tests.ps1') -Raw
$a = $designTest.IndexOf("namespace UnityEngine {")
$b = $designTest.IndexOf("'@", $a)
$stubs = $designTest.Substring($a, $b-$a)
$stubs += 'namespace UnityEngine { public class InspectorName : System.Attribute { public InspectorName(string text) {} } }'
$code = 'using System; using System.Collections.Generic; using UnityEngine; using UdonSharp;'
foreach ($file in @('Editor/Slot/ReelControlDesign.cs','Editor/Slot/ReelControlDesign.Common.cs','Editor/Slot/ReelControlValidator.cs','Runtime/REV1_0/Slot/ReelControlData.cs','Runtime/REV1_0/Slot/PayoutTable.cs')) {
    $text = Get-Content (Join-Path $root $file) -Raw
    $text = [regex]::Replace($text, '(?m)^using [^\r\n]+;\r?$', '')
    $code += $text
}
$tests = @'
public static class ValidationChecks {
    static int count;
    static void Check(string label, bool value) { if (!value) throw new Exception(label); count++; }
    static AX.UdonSlot.REV1_0.Editor.ReelControlDesign Setup() {
        var controller = new AX.UdonSlot.REV1_0.ReelOutputController { reelDatas = new[] {
            new AX.UdonSlot.REV1_0.ReelData { symbols = new[] {"A","B","C"} },
            new AX.UdonSlot.REV1_0.ReelData { symbols = new[] {"A","B","C"} },
            new AX.UdonSlot.REV1_0.ReelData { symbols = new[] {"A","B","C"} } } };
        var d = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        d.Initialize(controller,null);
        return d;
    }
    static AX.UdonSlot.REV1_0.PayoutTable Table() {
        return new AX.UdonSlot.REV1_0.PayoutTable {
            winPairNames = new[] {"R"}, leftSymbols = new[] {"A"}, centerSymbols = new[] {"A"}, rightSymbols = new[] {"A"},
            payouts = new[] {5}, isReelStopOrderAny = new[] {true},
            leftStopOrders = new[] {1}, centerStopOrders = new[] {2}, rightStopOrders = new[] {3},
            isBonuses = new[] {false}, payLineBetMasks = new[] {4},
            payLineLeftRows = new[] {2}, payLineCenterRows = new[] {2}, payLineRightRows = new[] {2} };
    }
    static void PullAll(AX.UdonSlot.REV1_0.Editor.ReelControlDesign d) {
        foreach(var r in d.reels) r.slips = new[] {0,1,2};
        foreach(var t in d.laterStops) t.slips = new[] {0,1,2};
    }
    public static string Run() {
        var d = Setup(); var p = Table();
        var v = new AX.UdonSlot.REV1_0.Editor.ReelControlValidator();
        var a = v.Run(d,p,new[] {"R"},3,4,0,false);
        Check("Every pressed triple traversed", a.paths == 27);
        Check("Provisional zero not hidden", a.provisional);
        Check("Only one actual win with zero slips", a.wins == 1);
        Check("Input not changed", d.reels[0].slips[0] == -1);
        PullAll(d);
        a = v.Run(d,p,new[] {"R"},3,4,0,true);
        Check("All six orders traversed", a.paths == 162);
        Check("Every path pulls target", a.wins == 162);
        Check("Complete tables have no findings", a.issues.Count == 0 && !a.provisional);
        a = v.Run(d,p,new string[0],3,4,0,false);
        Check("Forbidden wins flagged with payout", a.issues.Exists(i => i.kind == "対象外入賞" && i.message.Contains("5枚")));
        Check("Forbidden findings dedup by editing cell", a.issues.Count == 3);
        d.LaterTable(27).slips[0] = 1;
        a = v.Run(d,p,new[] {"R"},3,4,0,false);
        Check("Final stop recoverable miss", a.issues.Exists(i => i.kind == "引込経路あり" && i.stage == 2 && i.alternative == 0));
        Check("Final stop miss actual path count", a.wins == 18);
        PullAll(d); d.reels[0].slips[0] = 1;
        a = v.Run(d,p,new[] {"R"},3,4,0,false);
        var early = a.issues.Find(i => i.kind == "引込経路あり" && i.stage == 0);
        Check("Earlier stop miss detected through later tables", early != null && early.alternative == 0 && early.alternativeStops[0] == 0);
        Check("Witness contains original presses and context", early.presses[0] == 0 && early.context[0] == -1 && early.stops[0] == 2);
        PullAll(d);
        d.LaterTable(27).conditions.Add(new AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopCondition {left=0,center=0,slips=new[]{1,1,2}});
        a = v.Run(d,p,new[] {"R"},3,4,0,false);
        Check("Specific conditional table used", a.wins == 18);
        d.LaterTable(27).conditions.Clear();
        p.isReelStopOrderAny[0] = false;
        a = v.Run(d,p,new[] {"R"},3,4,0,true);
        Check("Payout stop order respected", a.wins == 27);
        p = Table(); p.leftSymbols[0] = " B, A "; p.centerSymbols[0] = "";
        a = v.Run(d,p,new[] {"R"},3,4,0,false);
        Check("OR and wildcard matching", a.wins == 27);
        bool bad = false;
        try { v.Run(d,p,new[] {"R"},1,4,0,false); } catch(InvalidOperationException) { bad=true; }
        Check("No active BET line does not falsely pass", bad);
        bad=false; try { v.Run(d,p,new[] {"unknown"},3,4,0,false); } catch(InvalidOperationException) { bad=true; }
        Check("Unknown target rejected", bad);
        bad=false; try { v.Run(d,p,new[] {"R"},3,4,0,false,()=>true); } catch(OperationCanceledException) { bad=true; }
        Check("Cancellation discards report", bad);
        d.reels[0].slips[0]=5;
        bad=false; try { v.Run(d,p,new[] {"R"},3,4,0,false); } catch(InvalidOperationException) { bad=true; }
        Check("Invalid slip blocks validation", bad);
        return count + " validation checks passed.";
    }
}
'@
Add-Type -TypeDefinition ($code + $stubs + $tests)
[ValidationChecks]::Run()

