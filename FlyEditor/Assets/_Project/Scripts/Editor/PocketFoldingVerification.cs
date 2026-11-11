using System;using UnityEngine;using UnityEditor;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class PocketFoldingVerification {
  static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("[Pocket] "+message);}
  static Vector2 R(Vector2 p,int turns){while(turns-->0)p=new Vector2(11-p.y,p.x);return p;}
  static Vector2 N(Vector2 p,int turns){while(turns-->0)p=new Vector2(-p.y,p.x);return p;}
  static Vector2 Local(PaperSheet.Face f,Vector2 p){var v=f.material.inverse.MultiplyPoint3x4(new Vector3(p.x,0,p.y));return new Vector2(v.x,v.z);}
  static FoldStep Axis(Vector2 normal,Vector2 point,int side,int turns)=>new FoldStep(CreaseFamily.Vertical,0,side){creaseNormal=N(normal,turns),creaseOffset=Vector2.Dot(N(normal,turns),R(point,turns))};
  static PaperCornerPull Plan(PaperSheet paper,Vector2 tip,Vector2 stop,Vector2 l,Vector2 r,int sign){
   for(int i=0;i<paper.Faces.Count;i++){
    var f=paper.Faces[i];if(!f.Contains(Local(f,tip)))continue;
    var step=new FoldStep{cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,regionalPull=true,coupledHinges=true,anchor=i,flap=i+1,pullTip=Local(f,tip),pullStop=Local(f,stop),pullLeft=Local(f,l),pullRight=Local(f,r)};
    var plan=PaperCornerPull.Create(paper,step);if(plan==null || !plan.UsesCoupledHinges)continue;
    if(plan.TryMove(0,sign*180,out float reached) && Mathf.Abs(reached-sign*180)<.001f)return plan;
   }return null;
  }
  static void Replay(PaperSheet sheet){
   var save=JsonUtility.FromJson<ResearchSave>(JsonUtility.ToJson(new ResearchSave{paperFolds=sheet.Recipe()}));var replay=PaperSheet.Replay(save.paperFolds);
   Check(replay.Count==sheet.Count && replay.Faces.Count==sheet.Faces.Count,"replay topology");
   for(int i=0;i<sheet.Faces.Count;i++){var a=sheet.Faces[i];var b=replay.Faces[i];Check(a.points.Length==b.points.Length && a.layer==b.layer,"replay layers");
    for(int j=0;j<a.points.Length;j++)Check(Vector3.Distance(a.World(a.points[j],0),b.World(b.points[j],0))<.0001f,"input sampling changed replay");}
  }
  static void Vertex(PaperSheet sheet,Vector2 material,Vector2 expected){
   bool found=false;foreach(var f in sheet.Faces)foreach(var p in f.points){var original=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
    if((new Vector2(original.x,original.z)-material).sqrMagnitude>.000001f)continue;
    var w=f.World(p,0);Check((new Vector2(w.x,w.z)-expected).magnitude<.002f,"wrong squash shape");found=true;}
   Check(found,"target material vertex missing");
  }
  [MenuItem("Tools/FlyEditor/Validation/Pocket folding")]
  public static void Run(){
   int count=0;
   for(int rotation=0;rotation<4;rotation++)foreach(bool swapped in new[]{false,true}){
    var paper=new PaperSheet();Check(paper.ApplyStep(Axis(Vector2.one,new Vector2(0,11),1,rotation)),"setup diagonal");
    Check(paper.ApplyStep(Axis(new Vector2(1,-1),Vector2.zero,-1,rotation)),"setup half triangle");
    var fixed0=paper.Faces[0];var fixed1=paper.Faces[1];var before=paper.Faces;
    var tip=R(new Vector2(0,11),rotation);var stop=R(new Vector2(5.5f,5.5f),rotation);
    var l=R(new Vector2(0,5.5f),rotation);var r=R(new Vector2(5.5f,11),rotation);
    var front=Plan(paper,tip,stop,swapped?r:l,swapped?l:r,1);Check(front!=null,"front pocket "+rotation+" swapped="+swapped);
    Check(front.Pose(33.3f) && front.Pose(12.25f) && front.Pose(90) && front.Pose(180),"fractional and reversed scrubbing");
    Check(paper.FinishPull(front,180),"front commit");Check(ReferenceEquals(paper.Faces[0],fixed0)&&ReferenceEquals(paper.Faces[1],fixed1),"unselected sheets moved");
    FourPointFoldingVerification.Metrics(paper);Replay(paper);Vertex(paper,tip,R(Vector2.zero,rotation));
    Vertex(paper,l,R(new Vector2(0,5.5f),rotation));Vertex(paper,r,R(new Vector2(5.5f,0),rotation));
    var frontPose=paper.Faces;
    var back=Plan(paper,R(new Vector2(11,0),rotation),stop,R(new Vector2(5.5f,0),rotation),R(new Vector2(11,5.5f),rotation),-1);
    Check(back!=null,"back pocket "+rotation);Check(paper.FinishPull(back,-180),"back commit");FourPointFoldingVerification.Metrics(paper);Replay(paper);
    foreach(var f in paper.Faces)foreach(var p in f.points){var w=f.World(p,0);var q=R(new Vector2(w.x,w.z),(4-rotation)%4);Check(q.x>=-.002f&&q.y>=-.002f&&q.x<=5.502f&&q.y<=5.502f,"base is not the target square");}
    Check(paper.Undo()&&ReferenceEquals(paper.Faces,frontPose),"back undo");Check(paper.Undo()&&ReferenceEquals(paper.Faces,before),"front undo");count++;
   }
   Debug.Log("[Pocket] PASS "+count+" rotated/swapped cases, front/back squashes, exact square-base vertices, fixed plies, fractional/reversed scrubbing, JSON replay and undo.");
  }
 }
}
