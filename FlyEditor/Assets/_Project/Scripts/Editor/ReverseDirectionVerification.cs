using System;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class ReverseDirectionVerification {
  static void Check(bool value,string text){if(!value)throw new Exception("[ReverseDirection] "+text);}
  static Vector2 Rotate(Vector2 p,int n){while(n-->0)p=new Vector2(11-p.y,p.x);return p;}
  static Vector2 Local(PaperSheet.Face f,Vector2 p){var v=f.material.inverse.MultiplyPoint3x4(new Vector3(p.x,0,p.y));return new Vector2(v.x,v.z);}
  public static PaperInsideReverse Plan(PaperSheet s,Vector2 tip,Vector2 stop,float crease){for(int i=0;i<s.Faces.Count;i++){var f=s.Faces[i];var a=Local(f,tip);if(!f.Contains(a))continue;var p=PaperInsideReverse.Create(s,new FoldStep{insideReverse=true,cornerPull=true,packetReverse=true,compactLayers=true,anchor=i,flap=i+1,pullTip=a,pullStop=Local(f,stop),reverseCreaseAngle=crease});if(p!=null&&p.Pose(0)&&p.Pose(180))return p;}return null;}
  public static void Geometry(IReadOnlyList<PaperSheet.Face> faces){float area=0;foreach(var f in faces){float twice=0;for(int i=0;i<f.points.Length;i++){var a=f.points[i];var b=f.points[(i+1)%f.points.Length];twice+=a.x*b.y-b.x*a.y;var m=f.material.MultiplyPoint3x4(new Vector3(a.x,0,a.y));var w=f.World(a,0);Check(float.IsFinite(w.x)&&float.IsFinite(w.y)&&float.IsFinite(w.z),"nonfinite");foreach(var g in faces){var q=Local(g,new Vector2(m.x,m.z));if(g.Contains(q))Check(Vector3.Distance(w,g.World(q,0))<.002f,"material seam including T-junction");}}area+=Mathf.Abs(twice)*.5f;}Check(Mathf.Abs(area-121)<.01f,"area");}
  [MenuItem("Tools/FlyEditor/Validation/Reverse direction and continuous hinges")]
  public static void Run(){int cases=0;for(int r=0;r<4;r++)foreach(float crease in new[]{35f,65f,115f,145f}){
   var s=new PaperSheet();var normal=Rotate(new Vector2(6.5f,5.5f),r)-Vector2.one*5.5f;Check(s.ApplyStep(new FoldStep{creaseNormal=normal,creaseOffset=Vector2.Dot(normal,Vector2.one*5.5f),side=-1,degrees=180,compactLayers=true}),"book");
   var tip=Rotate(new Vector2(5.5f,0),r);var stop=Rotate(new Vector2(5.5f,2.75f),r);var p=Plan(s,tip,stop,crease);Check(p!=null,"plan "+crease);var before=s.Faces;
   for(int a=0;a<=180;a+=5){Check(p.Pose(a),"sweep "+crease+"/"+a);Geometry(p.Preview);}foreach(float a in new[]{130,20,0,179,60}){Check(p.Pose(a),"backward scrub");Geometry(p.Preview);}
   Check(s.FinishReverse(p,90),"partial commit");PetalFoldingVerification.Replay(s);Geometry(s.Faces);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"partial undo");
   p=Plan(s,tip,stop,crease);Check(s.FinishReverse(p,180),"full commit");PetalFoldingVerification.Replay(s);Geometry(s.Faces);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"full undo");
   foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,1,90,179})Check(Plan(s,tip,stop,invalid)==null,"invalid crease accepted");cases++;
  }
  for(int r=0;r<4;r++)foreach(float a in new[]{-160f,-90f,-30f,30f,90f,160f}){var s=new PaperSheet();var n=Rotate(new Vector2(6.5f,5.5f),r)-Vector2.one*5.5f;var step=new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,Vector2.one*5.5f),side=1,continuousHinge=true,compactLayers=true,degrees=a};var before=s.Faces;Check(s.ApplyStep(step),"continuous hinge");Geometry(s.Faces);PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"hinge undo");}
  Debug.Log("[ReverseDirection] PASS "+cases+" reverse cases, 24 hinge cases: continuous motion, all material seams, area, partial/full replay and undo, invalid input.");
  }
 }
}
