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
$test = @'
public static class DesignChecks {
    static int count;
    static void Check(string name, bool result) { if (!result) throw new System.Exception(name); count++; }
    public static string Run() {
        var controller = new AX.UdonSlot.REV1_0.ReelOutputController();
        controller.reelDatas = new AX.UdonSlot.REV1_0.ReelData[3];
        for (int r = 0; r < 3; r++) {
            var symbols = new string[21];
            for (int p = 0; p < 21; p++) symbols[p] = "Symbol" + p;
            controller.reelDatas[r] = new AX.UdonSlot.REV1_0.ReelData { symbols = symbols };
        }
        var draft = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        draft.Initialize(controller, null);
        Check("New cells are unassigned", draft.reels[0].slips[0] == -1);
        Check("Snapshot matches source", draft.Matches(controller, 0));
        Check("Snapshot is independent", !object.ReferenceEquals(draft.reels[0].symbols, controller.reelDatas[0].symbols));
        Check("Display 12", AX.UdonSlot.REV1_0.Editor.ReelControlDesign.DisplayNumber(9, 21) == 12);
        draft.reels[0].slips[9] = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.Slip(9, 8, 21);
        draft.reels[0].slips[8] = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.Slip(8, 6, 21);
        Check("12 goes to 13 with one slip", draft.reels[0].slips[9] == 1);
        Check("13 goes to 15, not a zero-slip stop", draft.reels[0].slips[8] == 2);
        Check("Wrap calculation", AX.UdonSlot.REV1_0.Editor.ReelControlDesign.Slip(0, 20, 21) == 1);
        var values = draft.BuildOutput(0);
        Check("Unassigned exports temporary zero", values[0] == 0);
        Check("Temporary output retains unassigned intent", draft.reels[0].slips[0] == -1);
        var target = new AX.UdonSlot.REV1_0.ReelControlData();
        target.reelStopSlipCount = new int[58][];
        target.reelStopSlipCount[24] = new[] {3, 4};
        target.IsReelStopTableStyle = new bool[58];
        target.IsReelStopTableStyle[24] = true;
        target.conditionPatterns = new[] {16, 24};
        target.conditionLeftPositions = new[] {-1, 8};
        target.conditionCenterPositions = new[] {-1, -1};
        target.conditionRightPositions = new[] {-1, -1};
        target.conditionSlipOffsets = new[] {0, 2}; target.conditionSlipLengths = new[] {2, 3};
        target.conditionSlips = new[] {4, 4, 1, 2, 3};
        draft.ApplyOutput(target, 0);
        Check("First stop enabled", target.IsReelStopTableStyle[16]);
        Check("Other stop flags preserved", target.IsReelStopTableStyle[24] && !target.IsReelStopTableStyle[4]);
        Check("Other base tables preserved", target.reelStopSlipCount[24][1] == 4);
        Check("First-stop condition cannot shadow export", target.GetConditionalSlip(16, -1, -1, -1, 9) == 1);
        Check("Other conditions preserved after repack", target.GetConditionalSlip(24, 8, -1, -1, 2) == 3);
        Check("Output matches design", draft.OutputMatches(target, 0));
        target.conditionSlips[9] = 3;
        Check("External output change detected", !draft.OutputMatches(target, 0));
        var imported = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        imported.Initialize(controller, target);
        Check("Import uses effective conditional result", imported.reels[0].slips[9] == 3);
        Check("Disabled first stop imports unassigned", imported.reels[1].slips[0] == -1);
        controller.reelDatas[0].symbols[0] = "Changed";
        Check("Reel change detected", !draft.Matches(controller, 0));
        Check("Original snapshot retained", draft.reels[0].symbols[0] == "Symbol0");
        var before = target.reelStopSlipCount;
        target.conditionSlipOffsets[1] = 100000;
        bool failed = false;
        try { draft.ApplyOutput(target, 0); } catch (System.InvalidOperationException) { failed = true; }
        Check("Malformed output rejected", failed);
        Check("Failed export does not partially modify base tables", object.ReferenceEquals(before, target.reelStopSlipCount));
        draft.reels[0].slips[0] = 9;
        failed = false;
        try { draft.BuildOutput(0); } catch (System.InvalidOperationException) { failed = true; }
        Check("Invalid slip rejected", failed);
        var smallController = new AX.UdonSlot.REV1_0.ReelOutputController();
        smallController.reelDatas = new AX.UdonSlot.REV1_0.ReelData[3];
        for (int r = 0; r < 3; r++) smallController.reelDatas[r] = new AX.UdonSlot.REV1_0.ReelData { symbols = new[] {"A", "B", "C", "D", "E"} };
        var later = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        later.Initialize(smallController, null);
        Check("All later patterns initialized", later.laterStopsInitialized && later.laterStops.Count == 12);
        int[] empty = {-1, -1, -1};
        Check("First stage has one empty context", later.ReachableContexts(0, 0).Count == 1);
        Check("Unset first-stop provisional values retain every stop", later.ReachableContexts(0, 1).Count == 5);
        for (int p = 0; p < 5; p++) later.reels[0].slips[p] = p;
        var contexts = later.ReachableContexts(0, 1);
        Check("Converging presses deduplicated", contexts.Count == 1 && contexts[0][0] == 0 && contexts[0][1] == -1);
        int[] left0 = {0, -1, -1};
        var second = later.LaterTable(24);
        int rulesBefore = second.conditions.Count;
        var inherited = later.SlipsFor(0, 1, left0);
        Check("Reading a context does not create data", second.conditions.Count == rulesBefore && inherited[0] == -1);
        for (int p = 0; p < 5; p++) later.SetSlip(0, 1, left0, p, (p + 4) % 5);
        Check("Writes share one exact context", second.conditions.Count == 1);
        Check("Editing context preserves default", second.slips[0] == -1);
        contexts = later.ReachableContexts(0, 2);
        Check("Third stage follows effective second stop", contexts.Count == 1 && contexts[0][0] == 0 && contexts[0][1] == 1);
        later.reels[0].slips[1] = 0;
        int[] left1 = {1, -1, -1};
        for (int p = 0; p < 5; p++) later.SetSlip(0, 1, left1, p, (p + 2) % 5);
        contexts = later.ReachableContexts(0, 2);
        Check("Third stage keeps conditional pairs", contexts.Count == 2);
        Check("Impossible Cartesian pair excluded", !later.IsReachable(0, 2, new[] {0, 3, -1}));
        int[] pair01 = {0, 1, -1}, pair13 = {1, 3, -1};
        later.SetSlip(0, 2, pair01, 2, 2);
        later.SetSlip(0, 2, pair13, 2, 1);
        Check("Third-stop conditions isolated", later.SlipsFor(0, 2, pair01)[2] == 2 && later.SlipsFor(0, 2, pair13)[2] == 1);
        Check("Other stop order isolated", later.SlipsFor(2, 2, new[] {0, 1, -1})[2] == -1);
        later.reels[0].slips[1] = 1;
        Check("Predecessor edit makes old pair unreachable", !later.IsReachable(0, 2, pair13));
        Check("Unreachable manual data retained", later.SlipsFor(0, 2, pair13)[2] == 1);
        failed = false;
        try { later.SetSlip(0, 2, pair13, 2, 0); } catch (System.InvalidOperationException) { failed = true; }
        Check("Unreachable context cannot be edited", failed && later.SlipsFor(0, 2, pair13)[2] == 1);
        var laterTarget = new AX.UdonSlot.REV1_0.ReelControlData();
        later.ApplyOutput(laterTarget, 0);
        later.ApplyLaterOutput(laterTarget, 0, 1);
        later.ApplyLaterOutput(laterTarget, 0, 2);
        Check("Later export enables selected stages", laterTarget.IsReelStopTableStyle[24] && laterTarget.IsReelStopTableStyle[27]);
        Check("Other stage stays disabled", !laterTarget.IsReelStopTableStyle[18]);
        Check("First-stop export preserved", laterTarget.reelStopSlipCount[16][4] == 4);
        Check("Second-stop condition matches runtime", laterTarget.GetConditionalSlip(24, 0, -1, -1, 3) == 2);
        Check("Third-stop condition matches runtime", laterTarget.GetConditionalSlip(27, 0, 1, -1, 2) == 2);
        Check("Unassigned later cells output provisional zero", laterTarget.GetConditionalSlip(27, 0, 1, -1, 1) == 0 && later.SlipsFor(0, 2, pair01)[1] == -1);
        Check("Unreachable settings survive output", laterTarget.GetConditionalSlip(27, 1, 3, -1, 2) == 1);
        var reimported = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        reimported.Initialize(smallController, laterTarget);
        Check("Later data reimports effective value", reimported.SlipsFor(0, 2, pair01)[2] == 2);
        int firstSaved = reimported.reels[0].slips[4];
        reimported.InitializeLaterStops(null);
        Check("Repeat import preserves edits", reimported.reels[0].slips[4] == firstSaved && reimported.SlipsFor(0, 2, pair01)[2] == 2);
        string fingerprint = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.OutputSignature(laterTarget, 27);
        laterTarget.conditionSlips[laterTarget.conditionSlipOffsets[2]] = 4;
        Check("Output fingerprint detects external edits", fingerprint != AX.UdonSlot.REV1_0.Editor.ReelControlDesign.OutputSignature(laterTarget, 27));
        var basesBeforeFailure = laterTarget.reelStopSlipCount;
        var conditionsBeforeFailure = laterTarget.conditionPatterns;
        laterTarget.conditionSlipOffsets[0] = 99999;
        failed = false;
        try { later.ApplyLaterOutput(laterTarget, 0, 2); } catch (System.InvalidOperationException) { failed = true; }
        Check("Later export failure is transactional", failed && object.ReferenceEquals(basesBeforeFailure, laterTarget.reelStopSlipCount) && object.ReferenceEquals(conditionsBeforeFailure, laterTarget.conditionPatterns));
        var patternsSeen = new System.Collections.Generic.HashSet<int>();
        for (int order = 0; order < 6; order++)
            for (int stage = 1; stage < 3; stage++) {
                int pattern = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StagePattern(order, stage);
                Check("Stop-order encoding unique " + order + "/" + stage, patternsSeen.Add(pattern));
                int reel = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopOrders[order][stage];
                Check("Encoded reel stage " + pattern, ((pattern >> ((2 - reel) * 2)) & 3) == stage + 1);
            }
        var wildcardSource = new AX.UdonSlot.REV1_0.ReelControlData();
        wildcardSource.reelStopSlipCount = new int[58][];
        wildcardSource.reelStopSlipCount[27] = new[] {2, 2, 2, 2, 2};
        wildcardSource.IsReelStopTableStyle = new bool[58]; wildcardSource.IsReelStopTableStyle[27] = true;
        wildcardSource.conditionPatterns = new[] {27, 27, 27, 27};
        wildcardSource.conditionLeftPositions = new[] {-1, 0, 0, 0};
        wildcardSource.conditionCenterPositions = new[] {-1, -1, 1, 1};
        wildcardSource.conditionRightPositions = new[] {-1, -1, -1, -1};
        wildcardSource.conditionSlipOffsets = new[] {0, 5, 10, 15};
        wildcardSource.conditionSlipLengths = new[] {5, 5, 5, 5};
        wildcardSource.conditionSlips = new[] {4,4,4,4,4, 3,3,3,3,3, -1,1,0,2,4, 0,0,0,0,0};
        var wildcardDraft = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        wildcardDraft.Initialize(smallController, wildcardSource);
        Check("Most-specific minus one falls directly to base", wildcardDraft.SlipsFor(0, 2, pair01)[0] == 2);
        Check("Equal specificity keeps first rule", wildcardDraft.SlipsFor(0, 2, pair01)[1] == 1);
        var wildcardOutput = new AX.UdonSlot.REV1_0.ReelControlData();
        wildcardDraft.ApplyLaterOutput(wildcardOutput, 0, 2);
        bool roundtripMatches = true;
        for (int l = 0; l < 5; l++) for (int c = 0; c < 5; c++) for (int p = 0; p < 5; p++) {
            int expected = wildcardSource.GetConditionalSlip(27, l, c, -1, p);
            if (expected < 0) expected = wildcardSource.reelStopSlipCount[27][p];
            int actual = wildcardOutput.GetConditionalSlip(27, l, c, -1, p);
            if (actual < 0) actual = wildcardOutput.reelStopSlipCount[27][p];
            roundtripMatches &= actual == expected && wildcardDraft.SlipsFor(0, 2, new[] {l, c, -1})[p] == expected;
        }
        Check("Wildcard import/export matches runtime across 125 inputs", roundtripMatches);
        var allOrders = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        allOrders.Initialize(smallController, null);
        var allOutput = new AX.UdonSlot.REV1_0.ReelControlData();
        for (int r = 0; r < 3; r++) allOrders.ApplyOutput(allOutput, r);
        for (int order = 0; order < 6; order++) {
            var prior = new[] {-1, -1, -1};
            int first = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopOrders[order][0];
            int secondReel = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopOrders[order][1];
            prior[first] = 0;
            allOrders.SetSlip(order, 1, prior, 2, 1);
            allOrders.ApplyLaterOutput(allOutput, order, 1);
            prior[secondReel] = 1;
            allOrders.SetSlip(order, 2, prior, 3, 2);
            allOrders.ApplyLaterOutput(allOutput, order, 2);
        }
        for (int order = 0; order < 6; order++) {
            var prior = new[] {-1, -1, -1};
            prior[AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopOrders[order][0]] = 0;
            int pattern = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StagePattern(order, 1);
            Check("Second stop runtime order " + order, allOutput.GetConditionalSlip(pattern, prior[0], prior[1], prior[2], 2) == 1);
            prior[AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopOrders[order][1]] = 1;
            pattern = AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StagePattern(order, 2);
            Check("Third stop runtime order " + order, allOutput.GetConditionalSlip(pattern, prior[0], prior[1], prior[2], 3) == 2);
        }
        var generated = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        generated.Initialize(smallController, null);
        generated.reels[0].slips[0] = 0;
        int calls = 0;
        int added = generated.FillUnset(0, 0, 2, null, (s, ctx, press) => { calls++; return press; });
        Check("Generate only unset cells", added == 14 && calls == 14);
        Check("Deliberate zero retained", generated.reels[0].slips[0] == 0);
        Check("First generated stop prunes second", generated.ReachableContexts(0, 1).Count == 1);
        Check("Second generated stop prunes third", generated.ReachableContexts(0, 2).Count == 1);
        Check("Only reachable second condition created", generated.LaterTable(24).conditions.Count == 1);
        Check("Only reachable third condition created", generated.LaterTable(27).conditions.Count == 1);
        Check("Unselected reel first stop preserved", generated.reels[1].slips[0] == -1);
        Check("Other stop order preserved", generated.LaterTable(18).conditions.Count == 0);
        Check("Repeat preserves existing generated values", generated.FillUnset(0, 0, 2, null, (s, ctx, press) => 4) == 0);
        var scoped = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        scoped.Initialize(smallController, null);
        Check("Selected condition scope", scoped.FillUnset(0, 1, 1, new[] {2,-1,-1}, (s, ctx, press) => 1) == 5);
        Check("Other condition remains unset", scoped.SlipsFor(0, 1, new[] {1,-1,-1})[0] == -1);
        Check("Scoped generation preserves predecessor", scoped.reels[0].slips[0] == -1);
        bool invalidGenerated = false;
        try { scoped.FillUnset(0, 1, 1, new[] {1,-1,-1}, (s, ctx, press) => 5); }
        catch (InvalidOperationException) { invalidGenerated = true; }
        Check("Invalid generated values rejected", invalidGenerated);
        var migration = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        migration.Initialize(smallController, null);
        migration.reels[0].slips = new[] {0,1,2,3,4};
        for (int p = 0; p < 5; p++) {
            migration.SetSlip(0, 1, new[] {0,-1,-1}, p, p);
            migration.SetSlip(0, 2, new[] {0,0,-1}, p, 2);
        }
        migration.SetSlipWithReview(0, 0, new[] {-1,-1,-1}, 0, 1);
        Check("Wrapped nearby second context inherited", migration.SlipsFor(0, 1, new[] {4,-1,-1})[3] == 3);
        Check("Third context follows inherited second", migration.SlipsFor(0, 2, new[] {4,0,-1})[3] == 2);
        Check("Both inherited stages need review", migration.PendingReviewCount == 2);
        Check("Original condition kept", migration.SlipsFor(0, 1, new[] {0,-1,-1})[3] == 3);
        Check("Review stores original values", migration.reviews[0].previousSlips[3] == 3);
        var provisional = new AX.UdonSlot.REV1_0.ReelControlData();
        migration.ApplyLaterOutput(provisional, 0, 1);
        Check("Unconfirmed values can be exported", provisional.GetConditionalSlip(24,4,-1,-1,3) == 3);
        migration.reviews[0].confirmed = true;
        Check("Confirmation is independent of output", migration.PendingReviewCount == 1);
        migration.SetSlipWithReview(0, 0, new[] {-1,-1,-1}, 0, 1);
        Check("No-op does not reopen review", migration.PendingReviewCount == 1);
        var ambiguous = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        ambiguous.Initialize(smallController, null);
        ambiguous.reels[0].slips = new[] {0,1,0,1,2};
        ambiguous.SetSlip(0,1,new[] {0,-1,-1},0,1);
        ambiguous.SetSlip(0,1,new[] {2,-1,-1},0,2);
        ambiguous.SetSlipWithReview(0,0,new[] {-1,-1,-1},1,0);
        Check("Equal distance does not pick arbitrary source", ambiguous.SlipsFor(0,1,new[] {1,-1,-1})[0] == -1);
        Check("Ambiguous context needs review", ambiguous.PendingReviewCount == 1 && ambiguous.reviews[0].fromPositions == null);
        var retained = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        retained.Initialize(smallController, null);
        retained.reels[0].slips = new[] {0,1,2,3,4};
        retained.SetSlip(0,1,new[] {0,-1,-1},0,2);
        retained.LaterTable(24).conditions.Add(new AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopCondition {
            left=4, slips=new[] {0,-1,-1,-1,-1} });
        retained.SetSlipWithReview(0,0,new[] {-1,-1,-1},0,1);
        Check("Existing unreachable target value wins on reactivation", retained.SlipsFor(0,1,new[] {4,-1,-1})[0] == 0);
        Check("Reactivated context still asks review", retained.PendingReviewCount == 1);
        var emptyMigration = new AX.UdonSlot.REV1_0.Editor.ReelControlDesign();
        emptyMigration.Initialize(smallController, null);
        emptyMigration.SetSlipWithReview(0,0,new[] {-1,-1,-1},0,1);
        Check("No previous configured intent means no review flood", emptyMigration.PendingReviewCount == 0);
        return count + " design/output checks passed.";
    }
}
'@
foreach ($using in @('using System;', 'using System.Collections.Generic;', 'using UnityEngine;', 'using UdonSharp;')) {
    $design = $design.Replace($using, ''); $runtime = $runtime.Replace($using, '')
}
Add-Type -TypeDefinition ('using System; using System.Collections.Generic; using UnityEngine; using UdonSharp;' + $design + $runtime + $stubs + $test)
[DesignChecks]::Run()



