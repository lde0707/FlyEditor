using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FlyEditor.Flight;
using FlyEditor.Editor;
public static class ThreeFoldsAudit {
 [Serializable] public class Model {public string name,status,blockedAt;public bool complete;public int folds;public List<string> actions=new List<string>();public List<string> attempts=new List<string>();}
 [Serializable] public class Report {public string unity,execution="Current production engine API, no injected final mesh, no runtime solver changes. Rendered by Unity; not an end-to-end mouse walkthrough.";public List<Model> models=new List<Model>();}
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
  int plans=0,full=0;float maxAngle=0;var before=paper.Faces;
  foreach(float offset in new[]{.35f,.5f,.75f,1f,1.25f,1.5f,2f})foreach(bool packet in new[]{true,false}){
   int candidates=0;float best=0;
   for(int i=0;i<paper.Faces.Count;i++){var f=paper.Faces[i];var inv=f.material.inverse;var t=inv.MultiplyPoint3x4(Vector3.zero);var h=inv.MultiplyPoint3x4(new Vector3(offset,0,offset));var tip=new Vector2(t.x,t.z);if(!f.Contains(tip))continue;
    var p=PaperInsideReverse.Create(paper,new FoldStep{insideReverse=true,cornerPull=true,packetReverse=packet,compactLayers=true,anchor=i,flap=i+1,pullTip=tip,pullStop=new Vector2(h.x,h.z)});if(p==null)continue;plans++;candidates++;float reach=0;for(int a=0;a<=180;a+=3){if(!p.Pose(a))break;reach=a;}best=Mathf.Max(best,reach);maxAngle=Mathf.Max(maxAngle,reach);if(reach>=180)full++;
   }record.attempts.Add("Head stop="+offset+", packet="+packet+", plans="+candidates+", reached="+best+"/180");
  }
  Require(ReferenceEquals(before,paper.Faces),"Head probes mutated the paper");
  record.complete=false;record.status="blocked";record.blockedAt="Nested head reverse after neck and tail: "+plans+" plans, "+full+" full-angle candidates, maximum "+maxAngle+" degrees. Wings and final crane silhouette not certified.";
 }
}
