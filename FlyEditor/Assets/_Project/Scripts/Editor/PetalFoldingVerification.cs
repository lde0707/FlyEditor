using System;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class PetalFoldingVerification {
  static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("[Petal] "+message);}
  static Vector2 R(Vector2 p,int n){while(n-->0)p=new Vector2(11-p.y,p.x);return p;}
  static Vector2 N(Vector2 p,int n){while(n-->0)p=new Vector2(-p.y,p.x);return p;}
  static Vector2 Local(PaperSheet.Face f,Vector2 p){var v=f.material.inverse.MultiplyPoint3x4(new Vector3(p.x,0,p.y));return new Vector2(v.x,v.z);}
  static FoldStep Axis(Vector2 n,Vector2 p,int side,int rotation)=>new FoldStep{creaseNormal=N(n,rotation),creaseOffset=Vector2.Dot(N(n,rotation),R(p,rotation)),side=side};
  public static PaperCornerPull Plan(PaperSheet s,Vector2 t,Vector2 h,Vector2 l,Vector2 r,int sign,bool petal){
   for(int i=0;i<s.Faces.Count;i++){
    var f=s.Faces[i];var a=Local(f,t);if(!f.Contains(a))continue;
    var step=new FoldStep{cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,regionalPull=true,coupledHinges=true,adaptiveCreases=petal,anchor=i,flap=i+1,pullTip=a,pullStop=Local(f,h),pullLeft=Local(f,l),pullRight=Local(f,r)};
    var plan=PaperCornerPull.Create(s,step);if(plan==null||plan.UsesPetalFold!=petal)continue;
    plan.TryMove(0,sign*180,out var reached);if(Mathf.Abs(reached-sign*180)<.001f)return plan;
   }return null;
  }
  public static PaperSheet Square(int rotation=0){
   var s=new PaperSheet();Check(s.ApplyStep(Axis(Vector2.one,new Vector2(0,11),1,rotation)),"diagonal");Check(s.ApplyStep(Axis(new Vector2(1,-1),Vector2.zero,-1,rotation)),"triangle");
   var h=R(new Vector2(5.5f,5.5f),rotation);
   Check(s.FinishPull(Plan(s,R(new Vector2(0,11),rotation),h,R(new Vector2(0,5.5f),rotation),R(new Vector2(5.5f,11),rotation),1,false),180),"front squash");
   Check(s.FinishPull(Plan(s,R(new Vector2(11,0),rotation),h,R(new Vector2(5.5f,0),rotation),R(new Vector2(11,5.5f),rotation),-1,false),-180),"back squash");return s;
  }
  public static void Replay(PaperSheet s){
   var r=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(JsonUtility.ToJson(new ResearchSave{paperFolds=s.Recipe()})).paperFolds);
   Check(r.Count==s.Count&&r.Faces.Count==s.Faces.Count,"replay count");
   for(int i=0;i<s.Faces.Count;i++){var a=s.Faces[i];var b=r.Faces[i];Check(a.points.Length==b.points.Length&&a.layer==b.layer,"replay topology");for(int k=0;k<a.points.Length;k++)Check(Vector3.Distance(a.World(a.points[k],0),b.World(b.points[k],0))<.0001f,"replay position");}
  }
  static void Tip(PaperSheet s,Vector2 original,Vector2 expected){bool found=false;foreach(var f in s.Faces)foreach(var p in f.points){var m=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));if((new Vector2(m.x,m.z)-original).sqrMagnitude>.000001f)continue;var w=f.World(p,0);Check((new Vector2(w.x,w.z)-expected).magnitude<.002f,"wrong target shape");found=true;}Check(found,"missing corner");}
  [MenuItem("Tools/FlyEditor/Validation/Petal folding")]
  public static void Run(){
   int cases=0;
   for(int rotation=0;rotation<4;rotation++)foreach(bool swapped in new[]{false,true}){
    var s=Square(rotation);var before=s.Faces;var h=R(new Vector2(5.5f,5.5f),rotation);var l=R(new Vector2(0,5.5f),rotation);var r=R(new Vector2(5.5f,11),rotation);var t=R(new Vector2(0,11),rotation);
    var p=Plan(s,t,h,swapped?r:l,swapped?l:r,1,true);Check(p!=null,"front petal rotation="+rotation+" swapped="+swapped);
    Check(p.Pose(33.3f)&&p.Pose(150)&&p.Pose(12.5f)&&p.Pose(90),"backtracking preview");Check(s.FinishPull(p,90),"half commit");Replay(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"half undo");
    p=Plan(s,t,h,l,r,1,true);Check(s.FinishPull(p,180),"front flatten");FourPointFoldingVerification.Metrics(s);Replay(s);Tip(s,t,R(Vector2.one*(5.5f*Mathf.Sqrt(2)),rotation));
    var front=s.Faces;var b=Plan(s,R(new Vector2(11,0),rotation),h,R(new Vector2(5.5f,0),rotation),R(new Vector2(11,5.5f),rotation),-1,true);
    Check(b!=null&&s.FinishPull(b,-180),"back petal");FourPointFoldingVerification.Metrics(s);Replay(s);
    Tip(s,R(new Vector2(11,0),rotation),R(Vector2.one*(5.5f*Mathf.Sqrt(2)),rotation));Tip(s,R(Vector2.zero,rotation),R(Vector2.zero,rotation));
    Check(s.Undo()&&ReferenceEquals(front,s.Faces)&&s.Undo()&&ReferenceEquals(before,s.Faces),"two undo");cases++;
   }
   Debug.Log("[Petal] PASS "+cases+" rotated/swapped cases: front/back, area and seams, exact tip shape, partial replay, backward scrub, undo.");
  }
  public static void Regression(){Run();PocketFoldingVerification.Run();FourPointFoldingVerification.Run();PaperFoldingVerification.Run();}
 }
}
