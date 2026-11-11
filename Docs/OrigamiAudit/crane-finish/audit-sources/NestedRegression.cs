using System;using System.Collections.Generic;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class NestedRegression {
 static void Check(bool v,string m){if(!v)throw new Exception("[NestedRegression] "+m);}
 static Vector2 R(Vector2 p,int n){while(n-->0)p=new Vector2(11-p.y,p.x);return p;}
 static Vector3 At(IReadOnlyList<PaperSheet.Face> faces,Vector2 m){foreach(var f in faces){var p=f.material.inverse.MultiplyPoint3x4(new Vector3(m.x,0,m.y));if(f.Contains(new Vector2(p.x,p.z)))return f.World(new Vector2(p.x,p.z),0);}throw new Exception("missing material");}
 static void Geometry(IReadOnlyList<PaperSheet.Face> faces){float area=0;var points=new Dictionary<Vector2Int,Vector3>();foreach(var f in faces){area+=PaperSheet.Area(f.points);foreach(var p in f.points){var m=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));var key=new Vector2Int(Mathf.RoundToInt(m.x*10000),Mathf.RoundToInt(m.z*10000));var w=f.World(p,0);Check(float.IsFinite(w.x)&&float.IsFinite(w.y)&&float.IsFinite(w.z),"nonfinite");if(points.TryGetValue(key,out var prior))Check(Vector3.Distance(w,prior)<.002f,"seam separated");else points.Add(key,w);}}Check(Mathf.Abs(area-121)<.01f,"area changed");}
 public static void Run(){int cases=0;for(int rotation=0;rotation<4;rotation++)foreach(bool reversed in new[]{false,true}){
 var s=new PaperSheet();Vector2 n=R(new Vector2(6.5f,5.5f),rotation)-Vector2.one*5.5f;Check(s.ApplyStep(new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,Vector2.one*5.5f),side=-1,compactLayers=true,degrees=180}),"half");
 var tip=R(new Vector2(5.5f,0),rotation);var tail=R(new Vector2(5.5f,11),rotation);var stop=R(new Vector2(5.5f,2.75f),rotation);
 Check(s.FinishReverse(PacketFoldingVerification.Plan(s,tip,stop,reversed),180),"parent");Check(s.FinishReverse(PacketFoldingVerification.Plan(s,tail,R(new Vector2(5.5f,8.25f),rotation),reversed),180),"tail");
 var before=s.Faces;var fixedTip=At(before,tail);var parentStop=At(before,stop);var oldTip=At(before,tip);var plan=PacketFoldingVerification.Plan(s,tip,R(new Vector2(5.5f,.75f),rotation),reversed);Check(plan!=null,"nested plan");
 for(int a=0;a<=180;a+=3){Check(plan.Pose(a),"sweep "+a);Geometry(plan.Preview);}foreach(int a in new[]{90,12,150,0,180})Check(plan.Pose(a),"scrub");Check(!plan.Pose(float.NaN)&&!plan.Pose(-1)&&!plan.Pose(181),"invalid angle");
 Check(s.FinishReverse(plan,90),"partial commit");PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"partial undo");
 plan=PacketFoldingVerification.Plan(s,tip,R(new Vector2(5.5f,.75f),rotation),reversed);Check(s.FinishReverse(plan,180),"full");Geometry(s.Faces);PetalFoldingVerification.Replay(s);
 Check(Vector3.Distance(At(s.Faces,tail),fixedTip)<.002f,"tail moved");Check(Vector3.Distance(At(s.Faces,stop),parentStop)<.002f,"parent hinge moved");Check(Vector3.Distance(At(s.Faces,tip),oldTip)>.1f,"head did not move");
 var child=s.Faces;var grand=PacketFoldingVerification.Plan(s,tip,R(new Vector2(5.5f,.25f),rotation),reversed);Check(grand!=null,"third level plan");for(int a=0;a<=180;a+=6)Check(grand.Pose(a),"third level sweep");Check(s.FinishReverse(grand,180),"third level commit");Geometry(s.Faces);PetalFoldingVerification.Replay(s);Check(Vector3.Distance(At(s.Faces,tail),fixedTip)<.002f,"third level moved tail");Check(s.Undo()&&ReferenceEquals(child,s.Faces)&&s.Undo()&&ReferenceEquals(before,s.Faces),"nested undo");cases++;
 }
 Debug.Log("[NestedRegression] PASS "+cases+" rotated/reversed cases, depth 2/3, continuous sweep and scrub, area/seams, independent tail, parent hinge, partial/full replay and exact undo.");
 PacketFoldingVerification.Regression();
 }
}
