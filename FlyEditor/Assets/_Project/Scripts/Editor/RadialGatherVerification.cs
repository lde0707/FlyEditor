using System;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class RadialGatherVerification {
  static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("[RadialGather] "+message);}
  static Vector2 R(Vector2 p,int n){while(n-->0)p=new Vector2(11-p.y,p.x);return p;}
  static FoldStep Step(int rotation,bool swapped){var l=R(new Vector2(11,0),rotation);var r=R(new Vector2(11,11),rotation);return new FoldStep{cornerPull=true,flexible=true,customFlanks=true,closeFlat=true,regionalPull=true,adaptiveCreases=true,coordinatedCreases=true,anchor=0,flap=1,pullTip=R(new Vector2(11,5.5f),rotation),pullStop=Vector2.one*5.5f,pullLeft=swapped?r:l,pullRight=swapped?l:r};}
  [MenuItem("Tools/FlyEditor/Validation/Radial gather")]
  public static void Run(){int count=0;
   for(int rotation=0;rotation<4;rotation++)foreach(bool swapped in new[]{false,true})foreach(int sign in new[]{1,-1}){
    var s=new PaperSheet();var before=s.Faces;var step=Step(rotation,swapped);var p=PaperCornerPull.Create(s,step);Check(p!=null&&p.UsesRadialGather,"create");
    Check(p.TryMove(0,sign*180,out var reached)&&Mathf.Abs(reached-sign*180)<.001f,"full motion");
    foreach(float a in new[]{30f,90,140,175,40})Check(p.Pose(sign*a),"scrub");
    Check(s.FinishPull(p,sign*90),"partial commit");FourPointFoldingVerification.Metrics(s);PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(s.Faces,before),"partial undo");
    p=PaperCornerPull.Create(s,step);Check(s.FinishPull(p,sign*180),"flat commit");FourPointFoldingVerification.Metrics(s);PetalFoldingVerification.Replay(s);
    var layers=new System.Collections.Generic.HashSet<int>();foreach(var f in s.Faces)layers.Add(f.layer);Check(layers.Count==4,"four distinct plies");
    Check(s.Undo()&&ReferenceEquals(s.Faces,before),"undo");count++;
   }
   var packet=new PaperSheet();Check(packet.Finish(packet.Plan(new FoldStep{creaseNormal=Vector2.right,creaseOffset=5.5f,side=-1,compactLayers=true}),180),"packet initial fold");
   var gather=new FoldStep{cornerPull=true,flexible=true,customFlanks=true,closeFlat=true,adaptiveCreases=true,coordinatedCreases=true,flap=0,anchor=0,pullTip=new Vector2(11,8.25f),pullStop=new Vector2(8.25f,8.25f),pullLeft=new Vector2(11,11),pullRight=new Vector2(11,5.5f)};
   var source=packet.Faces;var plan=PaperCornerPull.Create(packet,gather);Check(plan!=null&&plan.UsesRadialGather,"whole packet create");Check(plan.TryMove(0,180,out var end)&&end>179.99f,"whole packet motion");Check(packet.FinishPull(plan,180),"whole packet commit");PacketMetrics(packet);PetalFoldingVerification.Replay(packet);
   // Material points in the lower rectangle must not move when the upper patch collapses.
   int checkedPoints=0;foreach(var f in packet.Faces)foreach(var q in f.points){var original=f.material.MultiplyPoint3x4(new Vector3(q.x,0,q.y));foreach(var old in source){var v=old.material.inverse.MultiplyPoint3x4(original);var local=new Vector2(v.x,v.z);if(local.y>5.5f-.001f||!old.Contains(local))continue;Check(Vector3.Distance(f.World(q,0),old.World(local,0))<.002f,"attached body moved");checkedPoints++;break;}}
   Check(checkedPoints>0,"body coverage");Check(packet.Undo()&&ReferenceEquals(source,packet.Faces),"packet undo");
   gather.flap=1;Check((PaperCornerPull.Create(packet,gather)?.UsesRadialGather!=true),"single ply must not move whole packet");
   var legacy=Step(0,false);legacy.coordinatedCreases=false;Check((PaperCornerPull.Create(new PaperSheet(),legacy)?.UsesRadialGather!=true),"legacy opt in");
   Debug.Log("[RadialGather] PASS "+count+" rotations/signs/swaps; scrub, partial/full replay, undo, four plies, two-ply packet, unchanged attached body, layer scope, legacy opt-in.");
  }
  static void PacketMetrics(PaperSheet s){
   float area=0,maxGap=0;var seams=new System.Collections.Generic.Dictionary<Vector2Int,Vector3>();var shells=new System.Collections.Generic.Dictionary<Vector2Int,Vector3>();
   foreach(var f in s.Faces){float a=0;for(int i=0;i<f.points.Length;i++){
    var p=f.points[i];var q=f.points[(i+1)%f.points.Length];a+=p.x*q.y-q.x*p.y;
    var original=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));var key=new Vector2Int(Mathf.RoundToInt(original.x*10000),Mathf.RoundToInt(original.z*10000));
    var middle=f.World(p,0);var shell=f.World(p,(f.layer+.5f)*PaperSheet.Thickness);
    Check(float.IsFinite(middle.x)&&float.IsFinite(middle.y)&&float.IsFinite(middle.z),"finite packet");
    if(seams.TryGetValue(key,out var other)){Check(Vector3.Distance(middle,other)<.002f,"packet midsurface seam");maxGap=Mathf.Max(maxGap,Vector3.Distance(shell,shells[key]));}else{seams.Add(key,middle);shells.Add(key,shell);}
   }area+=Mathf.Abs(a)*.5f;}
   Check(Mathf.Abs(area-s.Width*s.Height)<.01f,"packet area");
   Check(maxGap<=8*PaperSheet.Thickness+.002f,"shell separation exceeds eight-ply thickness");
   Debug.Log("[RadialGather] packet midsurface seam < .002; maximum shell gap="+maxGap+" (eight-ply shell allowance="+(8*PaperSheet.Thickness)+")");
  }
  public static void Regression(){Run();PetalFoldingVerification.Regression();}
 }
}
