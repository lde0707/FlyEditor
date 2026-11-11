using System;using System.Collections.Generic;using System.IO;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class PacketFoldingVerification {
  static void Check(bool ok,string text){if(!ok)throw new Exception("[Packet] "+text);}
  static Vector2 R(Vector2 p,int n){while(n-->0)p=new Vector2(11-p.y,p.x);return p;}
  static Vector2 Local(PaperSheet.Face f,Vector2 p){var v=f.material.inverse.MultiplyPoint3x4(new Vector3(p.x,0,p.y));return new Vector2(v.x,v.z);}
  public static PaperInsideReverse Plan(PaperSheet s,Vector2 tip,Vector2 stop,bool reverseOrder=false){
   for(int k=0;k<s.Faces.Count;k++){int i=reverseOrder?s.Faces.Count-1-k:k;var f=s.Faces[i];var a=Local(f,tip);if(!f.Contains(a))continue;
    var p=PaperInsideReverse.Create(s,new FoldStep{insideReverse=true,cornerPull=true,packetReverse=true,compactLayers=true,anchor=i,flap=i+1,pullTip=a,pullStop=Local(f,stop)});
    if(p!=null&&p.Pose(0)&&p.Pose(180))return p;
   }return null;
  }
  public static void Metrics(PaperSheet s){Metrics(s.Faces,s.Width*s.Height);}
  static void Metrics(IReadOnlyList<PaperSheet.Face> faces,float expectedArea){
   float area=0;var seams=new Dictionary<Vector2Int,Vector3>();
   foreach(var f in faces){float part=0;for(int i=0;i<f.points.Length;i++){
    var p=f.points[i];var q=f.points[(i+1)%f.points.Length];part+=p.x*q.y-q.x*p.y;var m=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));var key=new Vector2Int(Mathf.RoundToInt(m.x*10000),Mathf.RoundToInt(m.z*10000));var w=f.World(p,0);
    Check(float.IsFinite(w.x)&&float.IsFinite(w.y)&&float.IsFinite(w.z),"finite coordinates");if(seams.TryGetValue(key,out var other))Check(Vector3.Distance(w,other)<.002f,"shared seam");else seams.Add(key,w);
   }area+=Mathf.Abs(part)*.5f;}Check(Mathf.Abs(area-expectedArea)<.01f,"paper area");
  }
  static int LayerAt(PaperSheet s,Vector2 original){foreach(var f in s.Faces)if(f.Contains(Local(f,original)))return f.layer;throw new Exception("missing material layer");}
  static Vector3 At(IReadOnlyList<PaperSheet.Face> faces,Vector2 material){foreach(var f in faces){var p=Local(f,material);if(f.Contains(p))return f.World(p,0);}throw new Exception("missing material point");}
  static Vector2 FixedSample(PaperInsideReverse p){
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
   var packet=typeof(PaperInsideReverse).GetField("packet",flags).GetValue(p);var pieces=(System.Collections.IEnumerable)packet.GetType().GetField("pieces",flags).GetValue(packet);
   foreach(var piece in pieces){var type=piece.GetType();if((bool)type.GetField("cap",flags).GetValue(piece)||(int)type.GetField("side",flags).GetValue(piece)!=0)continue;
    var f=(PaperSheet.Face)type.GetField("face",flags).GetValue(piece);Vector2 c=Vector2.zero;foreach(var q in f.points)c+=q;c/=f.points.Length;
    var m=f.material.MultiplyPoint3x4(new Vector3(c.x,0,c.y));return new Vector2(m.x,m.z);
   }throw new Exception("No fixed body");
  }
  static void Sweep(PaperSheet s,PaperInsideReverse p){Check(p!=null&&p.UsesPacket,"packet plan");Check(p.PacketPanels>=2,"preview membership");
   var anchor=FixedSample(p);var fixedWorld=At(s.Faces,anchor);
   for(int a=0;a<=180;a+=3){Check(p.Pose(a),"motion at "+a);Metrics(p.Preview,s.Width*s.Height);Check(Vector3.Distance(At(p.Preview,anchor),fixedWorld)<.002f,"body anchor moved");}foreach(float a in new[]{90,140,20,179,0})Check(p.Pose(a),"reverse scrub");
   Check(!p.Pose(float.NaN)&&!p.Pose(-1)&&!p.Pose(181),"invalid angle accepted");
  }
  [MenuItem("Tools/FlyEditor/Validation/Connected packet reverse")]
  public static void Run(){int cases=0;
   var invalid=new PaperSheet();Check(PaperInsideReverse.Create(invalid,new FoldStep{insideReverse=true,packetReverse=true,pullTip=new Vector2(float.NaN,0),pullStop=Vector2.one})==null&&invalid.Count==0,"invalid control accepted");
   for(int rot=0;rot<4;rot++)foreach(bool order in new[]{false,true}){
    var s=new PaperSheet();var normal=R(new Vector2(6.5f,5.5f),rot)-Vector2.one*5.5f;
    Check(s.Finish(s.Plan(new FoldStep{creaseNormal=normal,creaseOffset=Vector2.Dot(normal,Vector2.one*5.5f),side=-1,compactLayers=true}),180),"half fold");
    var tip=R(new Vector2(5.5f,0),rot);var stop=R(new Vector2(5.5f,2.75f),rot);var p=Plan(s,tip,stop,order);Sweep(s,p);var before=s.Faces;
    Check(s.FinishReverse(p,90),"partial save");PetalFoldingVerification.Replay(s);Metrics(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"partial undo");
    p=Plan(s,tip,stop,order);Check(s.FinishReverse(p,180),"complete return");PetalFoldingVerification.Replay(s);Metrics(s);Check((At(s.Faces,tip)-At(before,tip)).magnitude>1,"tip unchanged");
    Check(LayerAt(s,R(new Vector2(3,5.5f),rot))>LayerAt(s,R(new Vector2(8,5.5f),rot)),"outer body layer order inverted");
    var first=s.Faces;var oldTip=At(first,tip);
    var q=Plan(s,R(new Vector2(5.5f,11),rot),R(new Vector2(5.5f,8.25f),rot),order);Sweep(s,q);Check(s.FinishReverse(q,180),"opposite end");Metrics(s);PetalFoldingVerification.Replay(s);
    Check(Vector3.Distance(oldTip,At(s.Faces,tip))<.002f,"previous end not restored");Check(s.Undo()&&ReferenceEquals(s.Faces,first)&&s.Undo()&&ReferenceEquals(s.Faces,before),"two undos");cases++;
   }
   Debug.Log("[Packet] PASS "+cases+" rotated/anchor cases: full sweep, backward scrub, invalid angles, partial/full replay, independent ends, first tip preserved, original area, seams, undo.");
   var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/crane-current/results/10-narrow.json"));if(!File.Exists(path))path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.crane-current/results/10-narrow.json"));var crane=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(path)).paperFolds);
   Check(crane.Finish(crane.Plan(new FoldStep{creaseNormal=new Vector2(1,-1),side=1,compactLayers=true}),180),"crane book");
   var neck=Plan(crane,Vector2.zero,Vector2.one*2.75f);Sweep(crane,neck);Check(neck.PacketPanels>2,"crane must use multiple pieces");Check(crane.FinishReverse(neck,180),"crane neck");Metrics(crane);PetalFoldingVerification.Replay(crane);
   var tail=Plan(crane,Vector2.one*11,Vector2.one*8.25f);Sweep(crane,tail);Check(crane.FinishReverse(tail,180),"crane tail");Metrics(crane);PetalFoldingVerification.Replay(crane);
   var count=crane.Count;var head=Plan(crane,Vector2.zero,Vector2.one*.75f);
   Check(head!=null && crane.Count==count,"nested planning changed source");Sweep(crane,head);Check(crane.FinishReverse(head,180),"nested head");Metrics(crane);PetalFoldingVerification.Replay(crane);
   Debug.Log("[Packet] PASS nested head reverse after independent neck and tail.");
   Debug.Log("[Packet] PASS crane sequential returns; neck panels="+neck.PacketPanels+", tail panels="+tail.PacketPanels+", folds="+crane.Count+". Wings are verified separately.");
  }
  public static void Regression(){Run();RadialGatherVerification.Regression();}
 }
}
