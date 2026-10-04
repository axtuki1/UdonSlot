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
$tests = @'
public static class MigrationChecks {
 static int count;
 static void Check(string label,bool value) { if(!value) throw new Exception(label); count++; }
 public static string Run() {
  var machine=new AX.UdonSlot.REV1_0.ReelOutputController {reelDatas=new[] {
   new AX.UdonSlot.REV1_0.ReelData {symbols=new[]{"A","B","A"}},
   new AX.UdonSlot.REV1_0.ReelData {symbols=new[]{"C","D","E"}},
   new AX.UdonSlot.REV1_0.ReelData {symbols=new[]{"F","G","H"}} }};
  var d=new AX.UdonSlot.REV1_0.Editor.ReelControlDesign(); d.Initialize(machine,null);
  d.reels[0].slips=new[]{0,1,2};
  var symbols=new[]{new[]{"B","A","A"},new[]{"C","D","E"},new[]{"F","G","H"}};
  var maps=new[]{new[]{0,1,2},new[]{0,1,2},new[]{0,1,2}};
  var result=AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.Build(d,symbols,maps);
  Check("Identity preserves slips",result.reels[0].slips[2]==2);
  Check("Source remains old",d.reels[0].symbols[0]=="A");
  Check("New symbols independent",!object.ReferenceEquals(symbols[0],result.reels[0].symbols));
  maps[0]=new[]{1,0,2};
  result=AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.Build(d,symbols,maps);
  Check("Mapped pressed position and target determine slip",result.reels[0].slips[0]==2);
  Check("Mapped zero remains zero",result.reels[0].slips[1]==0);
  d.LaterTable(24).conditions.Add(new AX.UdonSlot.REV1_0.Editor.ReelControlDesign.StopCondition {left=0,slips=new[]{0,1,2}});
  result=AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.Build(d,symbols,maps);
  Check("Later context remaps",result.tables.Find(t=>t.pattern==24).conditions[0].left==1);
  Check("Later slip preserved for unchanged reel",result.tables.Find(t=>t.pattern==24).conditions[0].slips[2]==2);
  maps[0]=new[]{-1,0,2};
  result=AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.Build(d,symbols,maps);
  Check("Removed target unset",result.reels[0].slips[0]==-1);
  Check("Removed condition archived rather than wildcard",result.removedConditions==1 && result.tables.Find(t=>t.pattern==24).conditions.Count==0);
  Check("Original condition retained in source",d.LaterTable(24).conditions.Count==1);
  maps[0]=new[]{0,0,2};
  bool failed=false;try{AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.Build(d,symbols,maps);}catch(InvalidOperationException){failed=true;}
  Check("Duplicate mapping rejected",failed);
  symbols[0]=new[]{"A","B","A","X","Y","Z","Q"};
  maps[0]=new[]{0,6,2};
  result=AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.Build(d,symbols,maps);
  Check("Over four slip becomes unset",result.reels[0].slips[6]==-1);
  Check("Inserted position unset",result.reels[0].slips[3]==-1);
  var auto=AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.DefaultMap(new[]{"A","A","B"},new[]{"A","A","B","C"});
  Check("Repeated symbols not arbitrarily matched",auto[0]==-1 && auto[1]==-1 && auto[2]==2);
  auto=AX.UdonSlot.REV1_0.Editor.ReelArrangementMigration.DefaultMap(new[]{"A","B"},new[]{"B","A"});
  Check("Equal-length default is physical position",auto[0]==0 && auto[1]==1);
  return count+" arrangement migration checks passed.";
 }
}
'@
Add-Type -TypeDefinition ($code+$stubs+$tests)
[MigrationChecks]::Run()

