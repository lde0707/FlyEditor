using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FlyEditor.Flight;
using FlyEditor.Editor;
public static class ThreeFoldsImprovedAudit {
 [Serializable] public class Model {public string name,status,blockedAt;public bool complete;public int folds;public List<string> actions=new List<string>();public List<string> attempts=new List<string>();}
 [Serializable] public class Report {public string unity,execution="Current production engine API, no injected final mesh, general nested reverse solver. Rendered by Unity; not an end-to-end mouse walkthrough.";public List<Model> models=new List<Model>();}
 static string root; static Model record;static Report report;static PaperSheet paper;
 const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
 static object Call(string method,params object[] args)=>typeof(CraneCurrentAudit).GetMethod(method,Hidden).Invoke(null,args);
 static void Require(bool value,string message){if(!value)throw new Exception(message);}
 static void Record(string action){record.actions.Add(paper.Count+": "+action);Debug.Log("[ThreeFolds] "+record.name+" "+paper.Count+" "+action);}
 static void Capture(string name,PaperCornerPull pull=null){
  string dir=Path.Combine(root,record.name);Directory.CreateDirectory(dir);
  typeof(CraneCurrentAudit).GetField("folder",Hidden).SetValue(null,dir);
  Call("Render",paper,name,pull);
  if(pull==null)File.WriteAllText(Path.Combine(dir,name+".json"),JsonUtility.ToJson(new ResearchSave{paperFolds=paper.Recipe()},true));
 }
 static void CheckReplay(){PetalFoldingVerification.Replay(paper);}
 static void Case(string name,Action action){record=new Model{name=name};report.models.Add(record);try{action();}catch(Exception e){record.status="blocked";record.blockedAt=e.GetBaseException().Message;Debug.LogWarning("[ThreeFolds] "+name+" blocked: "+record.blockedAt);}record.folds=paper?.Count??0;if(paper!=null){CheckReplay();Capture("final");}File.WriteAllText(Path.Combine(root,"result.json"),JsonUtility.ToJson(report,true));}
 public static void Run(){root=Environment.GetEnvironmentVariable("FLYEDITOR_THREE_FOLDS");Directory.CreateDirectory(root);report=new Report{unity=Application.unityVersion};Case("airplane",Airplane);Case("petal",Petal);Case("crane",Crane);Debug.Log("[ThreeFolds] FINISHED "+root);}
 static void Airplane(){paper=new PaperSheet();Capture("00-square");while(PaperPatterns.Next(paper,PlanePattern.Basic,out var step,out string text)){
  step.compactLayers=true;if(step.flap>0){step.singlePanel=true;step.connectedPanel=true;}
  var turn=paper.Plan(step);Require(turn!=null,"No plan: "+text);Require(paper.Finish(turn),"Blocked: "+text);Record(text);CheckReplay();Capture(paper.Count.ToString("00"));
 }Require(paper.Count==5,"Incomplete airplane sequence");FourPointFoldingVerification.Metrics(paper);record.complete=true;record.status="complete";}
 static PaperCornerPull PetalPlan(bool back){var h=Vector2.one*5.5f;return (PaperCornerPull)Call("CurrentPlan",paper,back?new Vector2(11,0):new Vector2(0,11),h,back?new Vector2(5.5f,0):new Vector2(0,5.5f),back?new Vector2(11,5.5f):new Vector2(5.5f,11),back?-1:1,true);}
 static void Base(){paper=(PaperSheet)Call("CurrentSquare");Record("Two shape folds and two four-point pocket squashes; no marking-only precreases.");Capture("04-square-base");}
 static void Petals(){for(int i=0;i<2;i++){bool back=i==1;var p=PetalPlan(back);Require(p!=null,"Petal plan "+i);int sign=back?-1:1;Require(p.Pose(sign*90),"Petal preview");Capture(back?"06-back-midway":"05-front-midway",p);for(int a=0;a<=180;a+=3)Require(p.Pose(sign*a),"Petal movement at "+a);Require(paper.FinishPull(p,sign*180),"Petal commit");FourPointFoldingVerification.Metrics(paper);CheckReplay();Record(back?"Back petal flattened.":"Front petal flattened.");Capture(back?"06-bird-base":"05-front-petal");}}
 static void Petal(){Base();Petals();record.complete=true;record.status="complete";}
 static void Crane(){Base();Petals();typeof(CraneCurrentAudit).GetField("paper",Hidden).SetValue(null,paper);
  for(int i=0;i<4;i++){Require((bool)Call("Narrow",i%2==0),"Narrow edge "+i);CheckReplay();Record("Narrow edge "+(i+1)+" with four surface handles.");Capture(paper.Count.ToString("00")+"-narrow");}
  Require(paper.ApplyStep(new FoldStep{creaseNormal=new Vector2(1,-1),side=1,degrees=180,compactLayers=true}),"Book fold");Record("Book fold for neck and tail.");Capture("11-book");
  foreach(bool tail in new[]{false,true}){var tip=tail?Vector2.one*11:Vector2.zero;var stop=tail?Vector2.one*8.25f:Vector2.one*2.75f;var plan=PacketFoldingVerification.Plan(paper,tip,stop);Require(plan!=null,"Neck/tail plan");for(int a=0;a<=180;a+=3)Require(plan.Pose(a),"Neck/tail motion "+a);Require(paper.FinishReverse(plan,180),"Neck/tail commit");PacketFoldingVerification.Metrics(paper);CheckReplay();Record(tail?"Tail reverse completed.":"Neck reverse completed.");Capture(tail?"13-tail":"12-neck");}
  var head=PacketFoldingVerification.Plan(paper,Vector2.zero,Vector2.one*.75f);Require(head!=null,"Nested head plan");for(int a=0;a<=180;a+=3)Require(head.Pose(a),"Head motion "+a);Require(paper.FinishReverse(head,180),"Head commit");PacketFoldingVerification.Metrics(paper);CheckReplay();Record("Nested head reverse completed.");Capture("14-head");
  foreach(var tipMaterial in new[]{new Vector2(0,11),new Vector2(11,0)}){
   bool done=false;var other=tipMaterial.x==0?new Vector2(11,0):new Vector2(0,11);var beforeOther=At(paper,other);var beforeHead=At(paper,Vector2.zero);var beforeTail=At(paper,Vector2.one*11);
   for(int i=0;i<paper.Faces.Count&&!done;i++){
    var f=paper.Faces[i];var t=f.material.inverse.MultiplyPoint3x4(new Vector3(tipMaterial.x,0,tipMaterial.y));var tip=new Vector2(t.x,t.z);if(!f.Contains(tip))continue;
    var h=f.frame.inverse.MultiplyPoint3x4(At(paper,Vector2.one*5.5f));var hinge=new Vector2(h.x,h.z);var normal=(tip-hinge).normalized;
    var step=new FoldStep{singlePanel=true,connectedPanel=true,compactLayers=true,flap=i+1,anchor=i,creaseNormal=normal,creaseOffset=Vector2.Dot(normal,hinge),side=1};
    foreach(float angle in new[]{90f,-90f}){var trial=PaperSheet.Replay(paper.Recipe());step.degrees=angle;if(!trial.ApplyStep(step))continue;
     if(Vector3.Distance(At(trial,other),beforeOther)>.002f || Vector3.Distance(At(trial,Vector2.zero),beforeHead)>.002f || Vector3.Distance(At(trial,Vector2.one*11),beforeTail)>.002f)continue;
     paper=trial;done=true;break;
    }
   }Require(done,"Wing blocked");CheckReplay();Record("Independent wing opened; head and tail retained.");Capture(paper.Count.ToString("00")+"-wing");
  }
  record.complete=false;record.status="head-fixed; wing motion passed; standard shape pending";record.blockedAt="Head passes; upper wing hinges pass. Separate lower-waist hinge probe was rejected; final standard crane shape is not certified.";
 }
 static Vector3 At(PaperSheet s,Vector2 m){foreach(var f in s.Faces){var p=f.material.inverse.MultiplyPoint3x4(new Vector3(m.x,0,m.y));if(f.Contains(new Vector2(p.x,p.z)))return f.World(new Vector2(p.x,p.z),0);}throw new Exception("Material point missing");}
}
