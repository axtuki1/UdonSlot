# Runs the actual lookup implementation with minimal Unity type stubs; no Unity process required.
$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '../Runtime/REV1_0/Slot/ReelControlData.cs'
$stubs = @"
namespace UdonSharp { public class UdonSharpBehaviour {} }
namespace UnityEngine {
    public class HideInInspector : System.Attribute {}
    public static class Mathf { public static int Clamp(int n, int min, int max) { return System.Math.Max(min, System.Math.Min(max, n)); } }
}
namespace VRC.Udon.Serialization.OdinSerializer { public class OdinSerialize : System.Attribute {} }
"@
Add-Type -TypeDefinition ((Get-Content -LiteralPath $sourcePath -Raw) + $stubs)
$data = New-Object AX.UdonSlot.REV1_0.ReelControlData
$script:checks = 0
function Check([string]$name, [int]$actual, [int]$expected) {
    if ($actual -ne $expected) { throw "$name : expected $expected, actual $actual" }
    $script:checks++
}
Check 'Legacy data uses base table' ($data.GetConditionalSlip(27, 5, 8, -1, 0)) -1
$data.conditionPatterns = @(27, 27, 27, 24)
$data.conditionLeftPositions = @(5, 5, 5, 5)
$data.conditionCenterPositions = @(-1, 8, 8, -1)
$data.conditionRightPositions = @(-1, -1, -1, -1)
$data.conditionSlipOffsets = @(0, 2, 4, 6)
$data.conditionSlipLengths = @(2, 2, 2, 2)
$data.conditionSlips = @(1, 2, 3, -1, 4, 4, 0, 1)
Check 'Specific condition beats wildcard' ($data.GetConditionalSlip(27, 5, 8, -1, 0)) 3
Check 'Equal conditions use first row' ($data.GetConditionalSlip(27, 5, 8, -1, 0)) 3
Check 'Wildcard covers other center stops' ($data.GetConditionalSlip(27, 5, 7, -1, 1)) 2
Check 'Unset cell returns to base, not wildcard' ($data.GetConditionalSlip(27, 5, 8, -1, 1)) -1
Check 'Nonmatching left uses base' ($data.GetConditionalSlip(27, 6, 8, -1, 0)) -1
Check 'Different order uses base' ($data.GetConditionalSlip(30, 5, 8, -1, 0)) -1
Check 'Second stop lookup' ($data.GetConditionalSlip(24, 5, -1, -1, 1)) 1
Check 'Unstopped reel cannot match concrete condition' ($data.GetConditionalSlip(27, -1, 8, -1, 0)) -1
Check 'Negative press uses base' ($data.GetConditionalSlip(27, 5, 8, -1, -1)) -1
Check 'Reel length mismatch uses base' ($data.GetConditionalSlip(27, 5, 8, -1, 2)) -1
$data.conditionSlipOffsets[1] = 999
Check 'Broken offset uses base' ($data.GetConditionalSlip(27, 5, 8, -1, 0)) -1
$data.conditionLeftPositions = @()
Check 'Truncated condition arrays use base' ($data.GetConditionalSlip(27, 5, 8, -1, 0)) -1
$data.conditionPatterns = $null
Check 'Null condition arrays use base' ($data.GetConditionalSlip(27, 5, 8, -1, 0)) -1
Check 'Left-center-right encoding' ([AX.UdonSlot.REV1_0.ReelControlData]::GetIndexForReelStopPattern(1, 2, 3)) 27
Check 'Right-center-left encoding' ([AX.UdonSlot.REV1_0.ReelControlData]::GetIndexForReelStopPattern(3, 2, 1)) 57
Write-Output "$script:checks lookup checks passed."
