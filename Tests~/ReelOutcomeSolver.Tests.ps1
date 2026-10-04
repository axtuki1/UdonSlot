$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$base = Get-Content (Join-Path $PSScriptRoot 'ReelControlDesign.Tests.ps1') -Raw
$a = $base.IndexOf('namespace UnityEngine {')
$b = $base.IndexOf("'@", $a)
$stubs = $base.Substring($a, $b-$a)
$stubs += 'namespace UnityEngine { public class InspectorName : System.Attribute { public InspectorName(string text) {} } } namespace AX.UdonSlot.REV1_0 { public enum ReelControlPriority { Payout, CombinationCount } public enum ReelRolePriority { SmallRoleFirst, BonusFirst } }'
$code = 'using System; using System.Collections.Generic; using UnityEngine; using UdonSharp;'
foreach($file in @('Editor/Slot/ReelControlDesign.cs','Editor/Slot/ReelControlDesign.Common.cs','Editor/Slot/ReelOutcomeRules.cs','Editor/Slot/ReelOutcomeSolver.cs','Editor/Slot/ReelSlipEvaluator.cs','Editor/Slot/ReelControlValidator.cs','Runtime/REV1_0/Slot/PayoutTable.cs','Runtime/REV1_0/Slot/ReelControlData.cs')) {
 $s = Get-Content (Join-Path $root $file) -Raw
 $code += [regex]::Replace($s,'(?m)^using [^\r\n]+;\r?$','')
}
$tests = @'
namespace AX.UdonSlot.REV1_0.Editor {
public static class OutcomeChecks {
 static int checks;
 static void Check(string label, bool value) { if(!value) throw new Exception(label); checks++; }
 static ReelOutcomeRules Book(int n=5) {
  var b=new ReelOutcomeRules {reels=new ReelControlDesign.FirstStop[3], maximumSlip=2};
  for(int r=0;r<3;r++) { var s=new string[n]; for(int p=0;p<n;p++) s[p]="S"+p; b.reels[r]=new ReelControlDesign.FirstStop {symbols=s}; }
  b.targets.Add(new ReelOutcomeRules.Target {id="yes"}); b.targets.Add(new ReelOutcomeRules.Target {id="no"}); return b;
 }
 static ReelControlDesign Design(ReelOutcomeRules b,ReelOutcomeSolver.Result result) {
  var d=new ReelControlDesign(); var m=new ReelOutputController {reelDatas=new ReelData[3]};
  for(int r=0;r<3;r++) m.reelDatas[r]=new ReelData {symbols=b.reels[r].symbols};
  d.Initialize(m,null); result.WriteTo(d); return d;
 }
 static int[] Sim(ReelControlDesign d,int o,int[] press,ReelControlData output=null) {
  var stops=new[]{-1,-1,-1};
  for(int stage=0;stage<3;stage++) {
   int r=ReelControlDesign.StopOrders[o][stage]; int pattern=ReelControlDesign.StagePattern(o,stage);
   int slip=output==null?d.SlipsFor(o,stage,stops)[press[r]]:output.GetConditionalSlip(pattern,stops[0],stops[1],stops[2],press[r]);
   if(slip<0 && output!=null) slip=ReelControlDesign.BaseSlip(output,pattern,press[r]);
   Check("Every reachable value assigned",slip>=0 && slip<=4);
   stops[r]=ReelControlDesign.Normalize(press[r]-slip,d.reels[r].symbols.Length);
  } return stops;
 }
 public static string Run() {
  var b=Book(); var shape=new ReelOutcomeRules.Shape {name="desired",lower=new[]{1,2,3}};
  shape.permissions.Add(new ReelOutcomeRules.Permission {targetId="yes",mode=ReelOutcomeRules.Mode.Preferred}); b.shapes.Add(shape);
  ReelOutcomeSolver.Evaluate neutral=(pattern,prior,press,slip)=>new ReelOutcomeSolver.Rating();
  var yes=new ReelOutcomeSolver().Run(b,"yes",neutral); var no=new ReelOutcomeSolver().Run(b,"no",neutral);
  Check("Both target conditions solved",yes.success && no.success);
  var dy=Design(b,yes); var dn=Design(b,no); var runtime=new ReelControlData(); dn.ApplyAllOutput(runtime);
  int hits=0;
  for(int l=0;l<5;l++) for(int c=0;c<5;c++) for(int r=0;r<5;r++) for(int o=0;o<6;o++) {
   var press=new[]{l,c,r}; var y=Sim(dy,o,press); var n=Sim(dn,o,press); var actual=Sim(dn,o,press,runtime);
   if(shape.Matches(y)) hits++;
   Check("Unspecified target never shows reserved shape",!shape.Matches(n));
   Check("Exported runtime exactly matches",n[0]==actual[0] && n[1]==actual[1] && n[2]==actual[2]);
  }
  Check("Preferred shape appears",hits>0);
  var repeat=new ReelOutcomeSolver().Run(b,"yes",neutral);
  Check("Deterministic decision count",repeat.decisions.Count==yes.decisions.Count);
  for(int i=0;i<repeat.decisions.Count;i++) for(int p=0;p<repeat.decisions[i].slips.Length;p++)
   Check("Deterministic values",repeat.decisions[i].slips[p]==yes.decisions[i].slips[p]);
  // A partial forbidden shape must be avoided even when its reel stops first.
  var partial=Book(); partial.shapes.Add(new ReelOutcomeRules.Shape {lower=new[]{0,-1,-1}});
  var pd=Design(partial,new ReelOutcomeSolver().Run(partial,"no",neutral));
  for(int p=0;p<5;p++) Check("Backward pruning avoids forbidden first stop",ReelControlDesign.Normalize(p-pd.reels[0].slips[p],5)!=0);
  // All physical outcomes forbidden: fail transaction, never silently pick a violation.
  var impossible=Book(3);
  for(int p=0;p<3;p++) impossible.shapes.Add(new ReelOutcomeRules.Shape {lower=new[]{p,-1,-1}});
  var failed=new ReelOutcomeSolver().Run(impossible,"no",neutral);
  Check("Impossible request reported",!failed.success && failed.issues.Count>0 && failed.decisions.Count==0);
  int[] before=dn.reels[0].slips; bool thrown=false;
  try { failed.WriteTo(dn); } catch(InvalidOperationException) { thrown=true; }
  Check("Failure cannot mutate output",thrown && object.ReferenceEquals(before,dn.reels[0].slips));
  thrown=false; try { new ReelOutcomeSolver().Run(b,"yes",neutral,()=>true); } catch(OperationCanceledException) {thrown=true;}
  Check("Cancellation propagates",thrown);
  // Contradictory preferred+forbidden rules cannot allow the forbidden one through.
  b.shapes.Add(new ReelOutcomeRules.Shape {lower=new[]{1,2,3}});
  var overlap=Design(b,new ReelOutcomeSolver().Run(b,"yes",neutral));
  Check("Forbidden overrides preferred",!shape.Matches(Sim(overlap,0,new[]{1,2,3})));
  var ranking=Book(); ranking.shapes.Add(new ReelOutcomeRules.Shape {lower=new[]{4,-1,-1},permissions=new List<ReelOutcomeRules.Permission>{new ReelOutcomeRules.Permission {targetId="yes",mode=ReelOutcomeRules.Mode.Preferred}}});
  var rank=Design(ranking,new ReelOutcomeSolver().Run(ranking,"yes",(pattern,prior,press,slip)=>new ReelOutcomeSolver.Rating {primary=slip==0?1:0}));
  Check("Role rank precedes cosmetic preference",rank.reels[0].slips[0]==0);
  var zero=Book(); zero.maximumSlip=0; zero.shapes.Add(new ReelOutcomeRules.Shape {lower=new[]{0,-1,-1}});
  Check("No-slip forced forbidden is impossible",!new ReelOutcomeSolver().Run(zero,"no",neutral).success);
  var invalid=Book(); invalid.shapes.Add(new ReelOutcomeRules.Shape()); thrown=false;
  try {new ReelOutcomeSolver().Run(invalid,"no",neutral);} catch(InvalidOperationException) {thrown=true;}
  Check("All ANY rejected",thrown);
  Check("Rules not mutated by generation",shape.lower[0]==1 && shape.permissions.Count==1);
  // Integrate the real candidate evaluator and independently verify using the existing validator.
  var integration=Book(3); var machine=new ReelOutputController {reelDatas=new ReelData[3]};
  for(int r=0;r<3;r++) machine.reelDatas[r]=new ReelData {symbols=integration.reels[r].symbols};
  var payout=new PayoutTable {
   winPairNames=new[]{"R"},leftSymbols=new[]{"S0"},centerSymbols=new[]{"S0"},rightSymbols=new[]{"S0"},
   payouts=new[]{5},isReelStopOrderAny=new[]{true},leftStopOrders=new[]{1},centerStopOrders=new[]{2},rightStopOrders=new[]{3},
   isBonuses=new[]{false},payLineBetMasks=new[]{4},payLineLeftRows=new[]{2},payLineCenterRows=new[]{2},payLineRightRows=new[]{2}
  };
  var evaluator=new ReelSlipEvaluator {controller=machine,payout=payout,bet=3,maximumSlip=2};
  ReelOutcomeSolver.Evaluate adapter=(pattern,prior,press,slip)=>{
   evaluator.Pattern=pattern; evaluator.positions=prior; var c=evaluator.Evaluate(press,slip);
   return new ReelOutcomeSolver.Rating {forbidden=c.forbidden>0,role=c.preferred,level=c.level,primary=c.payout,secondary=c.combinations};
  };
  var lose=Design(integration,new ReelOutcomeSolver().Run(integration,"no",adapter));
  var verified=new ReelControlValidator().Run(lose,payout,new string[0],3,2,0,true);
  Check("Independent validator confirms all losing paths",verified.paths==162 && verified.wins==0 && verified.issues.Count==0 && !verified.provisional);
  evaluator.allowedRoles.Add("R");
  var win=Design(integration,new ReelOutcomeSolver().Run(integration,"yes",adapter));
  verified=new ReelControlValidator().Run(win,payout,new[]{"R"},3,2,0,true);
  Check("Independent validator confirms all winning paths",verified.paths==162 && verified.wins==162 && verified.issues.Count==0 && !verified.provisional);
  // Symbol conditions: OR within a row, AND between rows/reels, repeated symbols and wraparound.
  var symbols=Book(); symbols.reels[0].symbols=new[]{"S0","S1","S2","S3","S0"};
  var ss=new ReelOutcomeRules.Shape(); ss.useSymbols[0]=true;
  ss.cells[1].any=false; ss.cells[1].symbols.AddRange(new[]{"S0","S2"}); symbols.shapes.Add(ss);
  ss.permissions.Add(new ReelOutcomeRules.Permission {targetId="yes",mode=ReelOutcomeRules.Mode.Preferred});
  symbols.Validate();
  var candidates=ss.Candidates(0,symbols.reels[0].symbols);
  Check("Middle OR includes every occurrence and cyclic last symbol",candidates.Count==3 && candidates.Contains(0) && candidates.Contains(1) && candidates.Contains(3));
  Check("Symbols match without exact lower index",ss.Matches(new[]{0,2,4},symbols.reels));
  Check("Nonmatching middle rejected",!ss.Matches(new[]{2,2,4},symbols.reels));
  ss.cells[0].any=false; ss.cells[0].symbols.Add("S3");
  Check("Rows are AND, cyclic top is matched",ss.Candidates(0,symbols.reels[0].symbols).Count==1 && ss.Matches(new[]{0,2,4},symbols.reels));
  ss.lower[1]=2;
  Check("Symbol reel combines with legacy exact reel",ss.Matches(new[]{0,2,4},symbols.reels) && !ss.Matches(new[]{0,3,4},symbols.reels));
  ss.lower[1]=-1; ss.cells[0].any=true;
  var sy=Design(symbols,new ReelOutcomeSolver().Run(symbols,"yes",neutral));
  var sn=Design(symbols,new ReelOutcomeSolver().Run(symbols,"no",neutral));
  var exported=new ReelControlData(); sn.ApplyAllOutput(exported); int symbolHits=0;
  for(int l=0;l<5;l++) for(int c=0;c<5;c++) for(int r=0;r<5;r++) for(int o=0;o<6;o++) {
   var input=new[]{l,c,r}; var stops=Sim(sn,o,input,exported);
   string middle=symbols.reels[0].symbols[ReelControlDesign.Normalize(stops[0]-1,5)];
   Check("Export avoids every forbidden OR symbol in every order",middle!="S0" && middle!="S2");
   var preferred=Sim(sy,o,input); string actual=symbols.reels[0].symbols[ReelControlDesign.Normalize(preferred[0]-1,5)];
   if(actual=="S0" || actual=="S2") symbolHits++;
  }
  Check("OR preferred shape actually appears",symbolHits>0);
  ss.cells[1].symbols.Clear(); thrown=false;
  try { symbols.Validate(); } catch(InvalidOperationException) {thrown=true;}
  Check("Empty selection is not ANY",thrown);
  ss.cells[1].symbols.Add("missing"); thrown=false;
  try { symbols.Validate(); } catch(InvalidOperationException) {thrown=true;}
  Check("Unknown symbol rejected",thrown);
  ss.cells[1].any=true; ss.lower[0]=0; thrown=false;
  try {symbols.Validate();} catch(InvalidOperationException) {thrown=true;}
  Check("Inactive exact index does not constrain symbol ANY",thrown);
  ss.useSymbols=null; ss.cells=null; symbols.Validate();
  Check("Legacy missing symbol fields preserve exact match",ss.Matches(new[]{0,2,4}) && !ss.Matches(new[]{1,2,4}));
  return "Outcome solver checks passed: "+checks;
 }
}}
'@
Add-Type -TypeDefinition ($code + $stubs + $tests)
[AX.UdonSlot.REV1_0.Editor.OutcomeChecks]::Run()
