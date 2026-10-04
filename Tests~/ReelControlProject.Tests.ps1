$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$base = Get-Content (Join-Path $PSScriptRoot 'ReelControlDesign.Tests.ps1') -Raw -Encoding UTF8
$a = $base.IndexOf('namespace UnityEngine {')
$b = $base.IndexOf("'@", $a)
$stubs = $base.Substring($a, $b-$a)
$stubs += 'namespace UnityEngine { public class InspectorName : System.Attribute { public InspectorName(string text) {} } } namespace AX.UdonSlot.REV1_0 { public enum ReelControlPriority { Payout, CombinationCount } public enum ReelRolePriority { SmallRoleFirst, BonusFirst } }'
$code = 'using System; using System.Collections.Generic; using UnityEngine; using UdonSharp;'
foreach ($file in @('Editor/Slot/ReelControlProject.cs','Editor/Slot/ReelControlProjectEngine.cs','Editor/Slot/ReelProjectFlagSync.cs','Editor/Slot/ReelSlipEvaluator.cs','Runtime/REV1_0/Slot/PayoutTable.cs','Runtime/REV1_0/Slot/ReelControlData.cs')) {
    $code += [regex]::Replace((Get-Content (Join-Path $root $file) -Raw -Encoding UTF8),'(?m)^using [^\r\n]+;\r?$','')
}
$tests = @'
namespace AX.UdonSlot.REV1_0.Editor {
public static class ProjectChecks {
 static int checks;
 static void Check(string name,bool ok) { if(!ok) throw new Exception(name); checks++; }
 static ReelControlProject Project() {
  var p=new ReelControlProject {strips=new ReelControlProject.Strip[3],maximumSlip=2};
  for(int r=0;r<3;r++) p.strips[r]=new ReelControlProject.Strip {symbols=new[]{"A","B","C","D","E"}};
  p.flags.Add(new ReelControlProject.Flag());
  for(int r=0;r<3;r++) p.flags[0].shapes[0].cells[r*3+2].symbols.Add("A");
  return p;
 }
 static int[] Sim(ReelControlProject p,ReelControlProject.Flag f,int order,int[] presses,ReelControlData baked=null) {
  var stops=new[]{-1,-1,-1}; var tables=ReelControlProjectEngine.Index(f.tables);
  for(int s=0;s<3;s++) {
   int r=ReelControlProject.Orders[order][s],pattern=ReelControlProject.Pattern(order,s);
   var t=tables[ReelControlProject.KeyFor(pattern,stops)]; int slip=t.Effective(presses[r]);
   if(baked!=null) {
    int actual=baked.GetConditionalSlip(pattern,stops[0],stops[1],stops[2],presses[r]);
    if(actual<0) actual=baked.reelStopSlipCount[pattern][presses[r]];
    Check("Baked lookup matches editor",actual==slip);
   }
   stops[r]=ReelControlProject.Wrap(presses[r]-slip,p.strips[r].symbols.Length);
  } return stops;
 }
 public static string Run() {
  var p=Project(); var f=p.flags[0]; var engine=new ReelControlProjectEngine();
  ReelControlProjectEngine.Forbidden none=(pattern,prior,press,slip)=>false;
  f.tables=engine.Generate(p,f,none);
  var baked=new ReelControlData(); ReelControlProjectEngine.Bake(p,f,baked);
  int misses=0;
  for(int o=0;o<6;o++) for(int l=0;l<5;l++) for(int c=0;c<5;c++) for(int r=0;r<5;r++) {
   var stop=Sim(p,f,o,new[]{l,c,r},baked);
   if(!f.shapes[0].Matches(stop,p.strips)) misses++;
  }
  var issues=ReelControlProjectEngine.Verify(p,f,none); int reported=0;
  foreach(var i in issues) {Check("Misses distinguished from errors",i.kind=="取りこぼし"); reported+=i.paths;}
  Check("All missed paths reported",reported==misses && misses>0);
  Check("Unreachable cells marked",f.tables.Exists(t=>Array.Exists(t.impossible,x=>x)));
  var first=f.tables.Find(t=>t.stage==0 && t.reel==0); first.manual[1]=first.generated[1];
  ReelControlProjectEngine.Merge(f,engine.Generate(p,f,none));
  first=f.tables.Find(t=>t.stage==0 && t.reel==0);
  Check("Unchanged correction preserved",first.manual[1]>=0 && !first.conflict[1]);
  first.manual[1]=0;
  var before=first.Effective(1);
  ReelControlProjectEngine.Merge(f,engine.Generate(p,f,none,null,0),0);
  first=f.tables.Find(t=>t.stage==0 && t.reel==0);
  Check("Downstream generation fixes first stop",first.Effective(1)==before && first.manual[1]==0);
  Check("Downstream rows include manual first stop",ReelControlProjectEngine.Verify(p,f,none).TrueForAll(i=>i.kind!="未生成"));
  f.shapes[0].cells[2].symbols.Clear(); f.shapes[0].cells[2].symbols.Add("B");
  ReelControlProjectEngine.Merge(f,engine.Generate(p,f,none));
  first=f.tables.Find(t=>t.stage==0 && t.reel==0);
  Check("Changed generator conflicts with correction",first.manual[1]==0 && first.conflict[1]);
  Check("Verification exposes conflict",ReelControlProjectEngine.Verify(p,f,none).Exists(i=>i.kind=="補正競合"));
  var basis=baked.reelStopSlipCount; bool threw=false;
  try {ReelControlProjectEngine.Bake(p,f,baked);} catch(InvalidOperationException) {threw=true;}
  Check("Rejected bake leaves runtime untouched",threw && object.ReferenceEquals(basis,baked.reelStopSlipCount));
  var saved=f.tables; threw=false;
  try {engine.Generate(p,f,none,()=>true);} catch(OperationCanceledException) {threw=true;}
  Check("Cancellation leaves project untouched",threw && object.ReferenceEquals(saved,f.tables));
  var ranked=Project(); ranked.maximumSlip=4; var rf=ranked.flags[0];
  var second=new ReelControlProject.Shape(); second.cells[2].symbols.Add("B"); rf.shapes.Add(second);
  rf.tables=engine.Generate(ranked,rf,none);
  Check("Registered shape wins over lower slip",Sim(ranked,rf,0,new[]{1,1,1})[0]==0);
  var unsafeProject=Project(); var uf=unsafeProject.flags[0];
  uf.tables=engine.Generate(unsafeProject,uf,(pattern,prior,press,slip)=>true);
  Check("Impossible safety still produces inspectable tables",uf.tables.Count>0 && uf.tables.Exists(t=>t.unsafePath));
  Check("Unsafe paths reported",ReelControlProjectEngine.Verify(unsafeProject,uf,(pattern,prior,press,slip)=>true).Exists(i=>i.kind=="非成立役入賞"));
  var invalid=Project(); invalid.flags[0].shapes.Clear(); threw=false;
  try {engine.Generate(invalid,invalid.flags[0],none);} catch(InvalidOperationException) {threw=true;}
  Check("At least one shape required",threw);
  var strips=p.strips; p.ResetArrangement(strips);
  Check("Explicit reset clears generated and manual data",f.tables.Count==0 && f.detached.Count==0);
  Check("Reset retains basic shapes",f.shapes.Count==1 && f.shapes[0].cells[2].symbols[0]=="B");
  Check("Arrangement snapshot cloned",!object.ReferenceEquals(strips[0].symbols,p.strips[0].symbols));
  // Use the shipped payout evaluator, with a one-line middle-row winning role.
  var integration=Project(); integration.maximumSlip=4; var inf=integration.flags[0];
  var payout=new PayoutTable {winPairNames=new[]{"R"},payouts=new[]{15},payoutsBet1=new[]{15},payoutsBet2=new[]{15},
   leftSymbols=new[]{"A"},centerSymbols=new[]{"A"},rightSymbols=new[]{"A"},isReplays=new[]{false},isBonuses=new[]{false},
   isReelStopOrderAny=new[]{true},leftStopOrders=new[]{1},centerStopOrders=new[]{2},rightStopOrders=new[]{3},
   payLineBetMasks=new[]{7},payLineLeftRows=new[]{1},payLineCenterRows=new[]{1},payLineRightRows=new[]{1}};
  var machine=new ReelOutputController {reelDatas=new ReelData[3]};
  for(int r=0;r<3;r++) {
   machine.reelDatas[r]=new ReelData {symbols=integration.strips[r].symbols};
   inf.shapes[0].cells[r*3+2].symbols.Clear(); inf.shapes[0].cells[r*3+1].symbols.Add("A");
  }
  var eval=new ReelSlipEvaluator {controller=machine,payout=payout,bet=3};
  ReelControlProjectEngine.Forbidden adapter=(pattern,prior,press,slip)=> {eval.Pattern=pattern;eval.positions=prior;return eval.Evaluate(press,slip).forbidden>0;};
  inf.tables=engine.Generate(integration,inf,adapter);
  Check("Nonwinning flag avoids non-established middle role",ReelControlProjectEngine.Verify(integration,inf,adapter).TrueForAll(i=>i.kind=="取りこぼし"));
  var anyMiss=Project(); anyMiss.maximumSlip=4; var anyFlag=anyMiss.flags[0];
  anyFlag.shapes[0]=new ReelControlProject.Shape();
  anyMiss.Validate(anyFlag);
  anyFlag.tables=engine.Generate(anyMiss,anyFlag,adapter);
  Check("All-ANY miss generates with non-established role avoidance",ReelControlProjectEngine.Verify(anyMiss,anyFlag,adapter).Count==0);
  Check("All-ANY safe cells are not marked impossible",anyFlag.tables.TrueForAll(t=>!Array.Exists(t.impossible,x=>x)));
  var partial=Project(); partial.maximumSlip=0; var pf=partial.flags[0];
  pf.shapes[0]=new ReelControlProject.Shape(); pf.shapes[0].cells[2].symbols.Add("A");
  pf.tables=engine.Generate(partial,pf,none);
  Check("Constrained reel shows true unreachable presses",pf.tables.Exists(t=>t.reel==0 && Array.Exists(t.impossible,x=>x)));
  Check("Later ANY reels remain unrestricted after first-stop miss",pf.tables.TrueForAll(t=>t.reel==0 || !Array.Exists(t.impossible,x=>x)));
  Check("Whole-shape miss remains visible in verification",ReelControlProjectEngine.Verify(partial,pf,none).Exists(i=>i.kind=="取りこぼし"));
  var anyBake=new ReelControlData(); ReelControlProjectEngine.Bake(anyMiss,anyFlag,anyBake);
  for(int o=0;o<6;o++) for(int l=0;l<5;l++) for(int c=0;c<5;c++) for(int r=0;r<5;r++) {
   var stops=Sim(anyMiss,anyFlag,o,new[]{l,c,r},anyBake);
   Check("All-ANY baked miss never awards middle role",!(stops[0]==1 && stops[1]==1 && stops[2]==1));
  }
  eval.allowedRoles.Add("R"); inf.roles.Add("R"); inf.tables=engine.Generate(integration,inf,adapter);
  Check("Established middle role pulled on every path",ReelControlProjectEngine.Verify(integration,inf,adapter).Count==0);
  var middleBake=new ReelControlData(); ReelControlProjectEngine.Bake(integration,inf,middleBake);
  for(int o=0;o<6;o++) for(int l=0;l<5;l++) for(int c=0;c<5;c++) for(int r=0;r<5;r++)
   Check("Middle role baked exactly",inf.shapes[0].Matches(Sim(integration,inf,o,new[]{l,c,r},middleBake),integration.strips));
  // A correction on a condition which disappears must return as an explicit conflict.
  var history=Project(); history.maximumSlip=4; var hf=history.flags[0]; hf.tables=engine.Generate(history,hf,none);
  var secondTable=hf.tables.Find(t=>t.stage==1 && t.reel==1 && t.prior[0]==0); secondTable.manual[0]=secondTable.generated[0];
  var left=hf.tables.Find(t=>t.stage==0 && t.reel==0);
  for(int p0=0;p0<5;p0++) left.manual[p0]=ReelControlProject.Wrap(p0-1,5);
  ReelControlProjectEngine.Merge(hf,engine.Generate(history,hf,none,null,0),0);
  Check("Unreachable manual condition archived",hf.detached.Exists(t=>t.Key==secondTable.Key));
  left=hf.tables.Find(t=>t.stage==0 && t.reel==0);
  for(int p0=0;p0<5;p0++) left.manual[p0]=p0;
  ReelControlProjectEngine.Merge(hf,engine.Generate(history,hf,none,null,0),0);
  var restored=hf.tables.Find(t=>t.Key==secondTable.Key);
  Check("Restored condition retains manual correction with review",restored!=null && restored.manual[0]>=0 && restored.conflict[0]);
  var missing=Project(); var mf=missing.flags[0]; mf.tables=engine.Generate(missing,mf,none); mf.tables.RemoveAll(t=>t.stage==2);
  threw=false; basis=baked.reelStopSlipCount;
  try {ReelControlProjectEngine.Bake(missing,mf,baked);} catch(InvalidOperationException) {threw=true;}
  Check("Incomplete bake rejected atomically",threw && object.ReferenceEquals(basis,baked.reelStopSlipCount));
  var unrelated=Project(); unrelated.maximumSlip=4; var un=unrelated.flags[0]; un.tables=engine.Generate(unrelated,un,none);
  var middle=un.tables.Find(t=>t.stage==1 && t.reel==1 && t.prior[0]==0); middle.manual[0]=middle.generated[0];
  var rightRoot=un.tables.Find(t=>t.stage==0 && t.reel==2); rightRoot.manual[1]=0;
  ReelControlProjectEngine.Merge(un,engine.Generate(unrelated,un,none));
  middle=un.tables.Find(t=>t.Key==middle.Key);
  Check("Unrelated first-stop correction does not conflict with left-middle context",middle.manual[0]>=0 && !middle.conflict[0]);
  var multiple=Project(); multiple.maximumSlip=4; var multi=multiple.flags[0];
  multi.shapes[0].cells[2].symbols.Add("B");
  Check("Multiple symbols accept first cherry",multi.shapes[0].Matches(new[]{0,0,0},multiple.strips));
  Check("Multiple symbols accept second cherry",multi.shapes[0].Matches(new[]{1,0,0},multiple.strips));
  Check("Multiple symbols reject unselected symbol",!multi.shapes[0].Matches(new[]{2,0,0},multiple.strips));
  multi.tables=engine.Generate(multiple,multi,none);
  Check("Multiple-symbol shape generates across all orders and presses",ReelControlProjectEngine.Verify(multiple,multi,none).Count==0);
  var full=Project(); full.maximumSlip=4; var ff=full.flags[0];
  for(int r=0;r<3;r++) { var symbols=new string[21]; for(int j=0;j<21;j++) symbols[j]=j==0?"A":"S"+j; full.strips[r].symbols=symbols; }
  var timer=System.Diagnostics.Stopwatch.StartNew(); ff.tables=engine.Generate(full,ff,none);
  var fullIssues=ReelControlProjectEngine.Verify(full,ff,none); int fullMisses=0;
  foreach(var i in fullIssues) {Check("21-symbol paths complete",i.kind=="取りこぼし");fullMisses+=i.paths;}
  Check("21-symbol full-path miss accounting",fullMisses==6*(21*21*21-5*5*5));
  var fullBake=new ReelControlData(); ReelControlProjectEngine.Bake(full,ff,fullBake);
  for(int o=0;o<6;o++) for(int j=0;j<21;j++) Sim(full,ff,o,new[]{j,(j+7)%21,(j+13)%21},fullBake);
  timer.Stop();
  var negated=Project(); negated.maximumSlip=4; var nf=negated.flags[0]; nf.shapes[0]=new ReelControlProject.Shape();
  var excluded=nf.shapes[0].cells[2]; excluded.symbols.AddRange(new[]{"A","B"}); excluded.negate=true;
  Check("Negated list rejects first selected symbol",!nf.shapes[0].Matches(new[]{0,0,0},negated.strips));
  Check("Negated list rejects second selected symbol",!nf.shapes[0].Matches(new[]{1,0,0},negated.strips));
  Check("Negated list accepts unselected symbol",nf.shapes[0].Matches(new[]{2,0,0},negated.strips));
  nf.tables=engine.Generate(negated,nf,none);
  Check("Negated shape generates and verifies",ReelControlProjectEngine.Verify(negated,nf,none).Count==0);
  var negatedBake=new ReelControlData(); ReelControlProjectEngine.Bake(negated,nf,negatedBake);
  for(int o=0;o<6;o++) for(int l=0;l<5;l++) for(int c=0;c<5;c++) for(int r=0;r<5;r++)
   Check("Baked negated shape avoids excluded symbols",Sim(negated,nf,o,new[]{l,c,r},negatedBake)[0]>=2);
  int ni=negated.DuplicateControl(0);
  Check("Duplication preserves negation",negated.flags[ni].shapes[0].cells[2].negate);
  negated.flags[ni].shapes[0].cells[2].negate=false;
  Check("Duplicated negation independent",excluded.negate);
  excluded.symbols.Clear();
  Check("Empty selection remains ANY even with negation metadata",nf.shapes[0].Matches(new[]{0,0,0},negated.strips) && !nf.shapes[0].ConstrainsReel(0));
  var clipboardTable=new ReelControlProject.Table {generated=new[]{0,1,2,3,4},manual=new[]{-1,4,-1,-1,0}};
  string clip=ReelControlProject.CopySlips(clipboardTable);
  Check("Copy uses effective slips in displayed row order",clip=="0\n4\n2\n3\n0");
  var paste=ReelControlProject.ParseSlips(clip,5,4);
  Check("Copy paste round trip",paste[0]==0 && paste[1]==4 && paste[4]==0);
  paste=ReelControlProject.ParseSlips("0\t1,2\r\n3 4\n",5,4);
  Check("Spreadsheet and text separators supported",paste[3]==3 && paste[4]==4);
  foreach(string invalidText in new[]{"0 1","0 1 2 3 4 0","0 1 2 3 5","0 1 -1 3 4","0 1 A 3 4","0 1 2.5 3 4"}) {
   threw=false; try {ReelControlProject.ParseSlips(invalidText,5,4);} catch(InvalidOperationException) {threw=true;}
   Check("Invalid paste rejected without mutating correction",threw && clipboardTable.manual[1]==4);
  }
  var duplication=Project(); var original=duplication.flags[0]; original.name="通常"; original.lotterySourceName="W"; original.roles.Add("W1");
  original.tables=engine.Generate(duplication,original,none); original.tables[0].manual[0]=2; original.tables[0].conflict[0]=true;
  original.detached.Add(original.tables[0]); original.generationSignature="saved";
  int copyIndex=duplication.DuplicateControl(0); var duplicate=duplication.flags[copyIndex];
  Check("Duplicate inserted and uniquely named",copyIndex==1 && duplicate.name=="通常_コピー" && duplication.flags.Count==2);
  Check("Duplicate keeps lottery source and generation state",duplicate.lotterySourceName=="W" && duplicate.generationSignature=="saved");
  Check("Duplicate keeps corrections and conflicts",duplicate.tables[0].manual[0]==2 && duplicate.tables[0].conflict[0]);
  duplicate.roles.Add("bonus"); duplicate.shapes[0].cells[2].symbols.Add("B");
  duplicate.tables[0].manual[0]=3; duplicate.tables[0].generated[0]=4; duplicate.tables[0].prior[0]=4;
  duplicate.tables[0].conflict[0]=false; duplicate.tables[0].impossible[0]=!original.tables[0].impossible[0];
  duplicate.detached[0].manual[0]=4;
  Check("Duplicate role list independent",original.roles.Count==1);
  Check("Duplicate shape selections independent",original.shapes[0].cells[2].symbols.Count==1);
  Check("Duplicate table arrays independent",original.tables[0].manual[0]==2 && original.tables[0].prior[0]==-1 && original.tables[0].conflict[0]);
  Check("Duplicate archived corrections independent",original.detached[0].manual[0]==2);
  duplication.DuplicateControl(0);
  Check("Repeated duplicate names distinct",duplication.flags[1].name=="通常_コピー_2");
  var entries=new List<ReelProjectFlagSync.Entry> {
   new ReelProjectFlagSync.Entry {name="watermelon",display="スイカ",bet1=1,bet2=2,bet3=3,roles=new List<string>{"W1"}},
   new ReelProjectFlagSync.Entry {name="miss",display="ハズレ",remainder=true,roles=new List<string>()}
  };
  var sf=new ReelControlProject.Flag {name="watermelonB",lotterySourceName="watermelon",roles=new List<string>{"W1","W2"}};
  var plan=ReelProjectFlagSync.Plan(entries,sf,new[]{"W1","W2"});
  Check("Flag rename retains lottery slot and ratios",plan.Count==2 && plan[0].name=="watermelonB" && plan[0].bet1==1 && plan[0].bet2==2 && plan[0].bet3==3);
  Check("All established roles exported",plan[0].roles.Count==2 && plan[0].roles[1]=="W2");
  Check("Unrelated remainder retained",plan[1].name=="miss" && plan[1].remainder);
  Check("Planning leaves source unchanged",entries[0].name=="watermelon" && entries[0].roles.Count==1);
  sf=new ReelControlProject.Flag {name="new",roles=new List<string>{"W2"}};
  plan=ReelProjectFlagSync.Plan(entries,sf,new[]{"W1","W2"});
  Check("New flag includes roles with zero ratios",plan.Count==3 && plan[2].name=="new" && plan[2].roles[0]=="W2" && plan[2].bet1==0 && plan[2].bet2==0 && plan[2].bet3==0 && !plan[2].remainder);
  sf=new ReelControlProject.Flag {name="watermelon",roles=new List<string>{"W2"}};
  plan=ReelProjectFlagSync.Plan(entries,sf,new[]{"W1","W2"});
  Check("Existing name updates rather than duplicates",plan.Count==2 && plan[0].roles[0]=="W2");
  sf.lotterySourceName="missing"; threw=false;
  try {ReelProjectFlagSync.Plan(entries,sf,new[]{"W1","W2"});} catch(InvalidOperationException) {threw=true;}
  Check("Deleted source cannot silently create renamed flag",threw);
  sf.lotterySourceName="watermelon"; sf.name="miss"; threw=false;
  try {ReelProjectFlagSync.Plan(entries,sf,new[]{"W1","W2"});} catch(InvalidOperationException) {threw=true;}
  Check("Rename collision rejected",threw);
  sf.name="watermelon"; sf.roles.Add("missing"); threw=false;
  try {ReelProjectFlagSync.Plan(entries,sf,new[]{"W1","W2"});} catch(InvalidOperationException) {threw=true;}
  Check("Unknown role cannot be applied",threw);
  return checks+" checks passed; 21-symbol generation/verification/bake: "+timer.Elapsed.TotalSeconds.ToString("F2")+"s";
 }
}}
'@
Add-Type -TypeDefinition ($code + $stubs + $tests)
[AX.UdonSlot.REV1_0.Editor.ProjectChecks]::Run()









