$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$base = Get-Content (Join-Path $PSScriptRoot 'ReelControlDesign.Tests.ps1') -Raw
$a = $base.IndexOf('namespace UnityEngine {')
$b = $base.IndexOf("'@", $a)
$stubs = $base.Substring($a, $b-$a)
$code = 'using System; using System.Collections.Generic; using UnityEngine; using UdonSharp;'
foreach($file in @('Editor/Slot/ReelControlDesign.cs','Editor/Slot/ReelControlDesign.Common.cs','Editor/Slot/ReelArrangementMigration.cs','Runtime/REV1_0/Slot/ReelControlData.cs')) {
 $s = Get-Content (Join-Path $root $file) -Raw
 $code += [regex]::Replace($s,'(?m)^using [^\r\n]+;\r?$','')
}
$tests = @"
namespace AX.UdonSlot.REV1_0.Editor {
public static class CommonChecks {
 static int count;
 static void Check(string label,bool value) { if(!value) throw new Exception(label); count++; }
 static bool Equal(int[] a,int[] b) { for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false; return true; }
 public static string Run() {
  var machine = new ReelOutputController {reelDatas=new ReelData[3]};
  for(int r=0;r<3;r++) machine.reelDatas[r]=new ReelData {symbols=new[]{"A","B","C","D","E"}};
  var d = new ReelControlDesign(); d.Initialize(machine,null);
  Check("Legacy stays independent",!d.commonSettingsEnabled);
  d.EnableCommonSettings();
  for(int r=0;r<3;r++) for(int p=0;p<5;p++) d.SetCommonSlip(r,p,(r+p)%3);
  Check("No exception or review flood",d.ExceptionCount(0)==0 && d.ExceptionCount(1)==0 && d.ExceptionCount(2)==0 && d.PendingReviewCount==0);
  var target = new ReelControlData(); d.ApplyAllOutput(target);
  for(int l=0;l<5;l++) for(int m=0;m<5;m++) for(int r=0;r<5;r++) {
   var presses=new[]{l,m,r}; var expected=new int[3];
   for(int reel=0;reel<3;reel++) expected[reel]=ReelControlDesign.Normalize(presses[reel]-d.reels[reel].slips[presses[reel]],5);
   for(int order=0;order<6;order++) {
    var stops=new[]{-1,-1,-1}; var runtime=new[]{-1,-1,-1};
    for(int stage=0;stage<3;stage++) {
     int reel=ReelControlDesign.StopOrders[order][stage],press=presses[reel];
     int slip=d.SlipsFor(order,stage,stops)[press]; stops[reel]=ReelControlDesign.Normalize(press-slip,5);
     int pattern=ReelControlDesign.StagePattern(order,stage);
     int actual=target.GetConditionalSlip(pattern,runtime[0],runtime[1],runtime[2],press);
     if(actual<0) actual=ReelControlDesign.BaseSlip(target,pattern,press);
     runtime[reel]=ReelControlDesign.Normalize(press-actual,5);
    }
    Check("Every order yields same final shape",Equal(stops,expected));
    Check("Exported runtime agrees",Equal(runtime,expected));
   }
  }
  var context=d.ReachableContexts(0,1)[0];
  d.SetSlip(0,1,context,0,4);
  Check("Conditional edit creates exception",!d.UsesCommon(0,1,context));
  d.SetCommonSlip(1,0,2);
  Check("Exception survives common change",d.SlipsFor(0,1,context)[0]==4);
  d.UseCommonForContext(0,1,context);
  d.SetCommonSlip(1,0,1);
  Check("Reset resumes live inheritance",d.SlipsFor(0,1,context)[0]==1 && d.UsesCommon(0,1,context));
  var old=new ReelControlDesign(); old.Initialize(machine,null);
  old.reels[0].slips[0]=2;
  old.LaterTable(ReelControlDesign.StagePattern(2,1)).slips[0]=3;
  var before=old.CaptureContexts(); old.EnableCommonSettings();
  foreach(var row in before) Check("Legacy conversion preserves every reachable value",Equal(row.slips,old.SlipsFor(row.order,row.stage,row.positions)));
  old.UseCommonForReel(0);
  Check("Reel reset removes exceptions",old.ExceptionCount(0)==0);
  old.SetCommonSlip(0,0,1);
  Check("Reset bases follow common",old.BaseValues(old.LaterTable(ReelControlDesign.StagePattern(2,1)))[0]==1);
  var symbols=new[]{machine.reelDatas[0].symbols,machine.reelDatas[1].symbols,machine.reelDatas[2].symbols};
  var maps=new[]{new[]{0,1,2,3,4},new[]{0,1,2,3,4},new[]{0,1,2,3,4}};
  var migrated=ReelArrangementMigration.Build(d,symbols,maps);
  Check("Migration retains common base",migrated.tables[0].useCommon);
  Check("Migration retains inherited condition",migrated.tables[0].conditions[0].useCommon);
  var refs=target.reelStopSlipCount;
  d.LaterTable(ReelControlDesign.StagePattern(0,1)).useCommon=false;
  d.LaterTable(ReelControlDesign.StagePattern(0,1)).slips=new[]{9};
  bool rejected=false; try{d.ApplyAllOutput(target);}catch(InvalidOperationException){rejected=true;}
  Check("Failed all-export leaves runtime unchanged",rejected && object.ReferenceEquals(refs,target.reelStopSlipCount));
  return count+" common settings checks passed";
 }
}}
"@
Add-Type -TypeDefinition ($code+$stubs+$tests)
[AX.UdonSlot.REV1_0.Editor.CommonChecks]::Run()