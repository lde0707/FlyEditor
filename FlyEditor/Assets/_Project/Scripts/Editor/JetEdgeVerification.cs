using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class JetEdgeVerification {
  static void Check(bool v,string m){if(!v)throw new Exception("[JetEdge] "+m);}
  static FoldStep Edge(PaperSheet s,int anchor,bool modern){
   var f=s.Faces[anchor];
   // Two selected edges on a folded wing. Coordinates here are test geometry,
   // never a model-specific instruction in the runtime solver.
   return new FoldStep {anchor=anchor,flap=anchor+1,cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,
    regionalPull=true,adaptiveCreases=true,compactLayers=true,continuousHinge=modern,neutralContacts=modern,
    pullTip=new Vector2(5.5f,0),pullStop=Vector2.zero,pullLeft=new Vector2(5.5f,2.75f),pullRight=new Vector2(5.5f,5.5f)};
  }
  static PaperSheet Packet(){var s=new PaperSheet();Check(s.ApplyStep(new FoldStep{creaseNormal=Vector2.up,creaseOffset=5.5f,side=1,degrees=180,continuousHinge=true,neutralContacts=true,compactLayers=true}),"half fold");return s;}
  static string Out {get {var p=Environment.GetEnvironmentVariable("FLYEDITOR_JET");if(string.IsNullOrEmpty(p))p=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/jet-study"));Directory.CreateDirectory(p);return p;}}
  static void Capture(PaperSheet s,string name,PaperCornerPull p=null){JetStudyAudit.Render(s,Out,name,p);if(p==null)File.WriteAllText(Path.Combine(Out,name+".json"),JsonUtility.ToJson(new ResearchSave{paperFolds=s.Recipe()},true));}
  [MenuItem("Tools/FlyEditor/Validation/Jet edge alignment")]
  public static void Run(){
   var legacy=new PaperSheet();var lp=PaperCornerPull.Create(legacy,Edge(legacy,0,false));Check(lp!=null&&lp.UsesEdgeMatch&&lp.Pose(90),"legacy baseline");bool gap=false;
   try{ReverseDirectionVerification.Geometry(lp.Preview);}catch{gap=true;}
   Check(gap,"expected baseline mid-fold seam defect");
   int cases=0,poses=0;
   foreach(bool packet in new[]{false,true})foreach(bool upper in new[]{false,true})foreach(int sign in new[]{-1,1}){
    if(!packet&&upper)continue;var s=packet?Packet():new PaperSheet();int anchor=0;
    if(packet){int desired=upper?int.MinValue:int.MaxValue;for(int i=0;i<s.Faces.Count;i++){var f=s.Faces[i];if(upper?f.layer>desired:f.layer<desired){desired=f.layer;anchor=i;}}}
    var before=s.Faces;var st=Edge(s,anchor,true);var p=PaperCornerPull.Create(s,st);Check(p!=null&&p.UsesEdgeMatch,"create "+packet+"/"+upper);
    bool away=!packet||(upper?sign>0:sign<0);
    if(!away){Check(!p.Pose(sign*30),"blocked ply must remain blocking");Check(ReferenceEquals(before,s.Faces),"rejected mutation");continue;}
    for(int a=0;a<=180;a+=3){Check(p.Pose(sign*a),"pose "+packet+"/"+upper+"/"+sign+"/"+a);ReverseDirectionVerification.Geometry(p.Preview);poses++;}
    foreach(float a in new[]{179f,35,0,90,10}){Check(p.Pose(sign*a),"scrub");ReverseDirectionVerification.Geometry(p.Preview);}
    if(packet){foreach(var f in before)if(!ReferenceEquals(f,before[anchor]))foreach(var q in f.points){var m=f.material.MultiplyPoint3x4(new Vector3(q.x,0,q.y));bool found=false;foreach(var g in p.Preview){var local=g.material.inverse.MultiplyPoint3x4(m);var v=new Vector2(local.x,local.z);if(g.Contains(v)&&Vector3.Distance(g.World(v,0),f.World(q,0))<.002f){found=true;break;}}Check(found,"unselected wing changed");}}
    Check(s.FinishPull(p,sign*90),"partial commit");ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"partial undo");
    p=PaperCornerPull.Create(s,st);Check(s.FinishPull(p,sign*180),"full commit");ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);
    if(packet&&upper){Capture(s,"wing-edge-aligned");File.WriteAllText(Path.Combine(Out,"wing-edge-step.json"),JsonUtility.ToJson(st,true));}
    Check(s.Undo()&&ReferenceEquals(before,s.Faces),"full undo");cases++;
   }
   // A shared corner belongs to the fixed and already tilted wing. Looking up
   // only its first face used to abort before reaching the selected wing chart.
   foreach(float tilt in new[]{-120f,-45f,45f,120f}){
    var s=new PaperSheet();Check(s.ApplyStep(new FoldStep{creaseNormal=Vector2.right,creaseOffset=5.5f,side=1,degrees=tilt,continuousHinge=true,neutralContacts=true,compactLayers=true}),"tilted wing");int anchor=1;
    var st=Edge(s,anchor,true);st.pullStop=new Vector2(11,0);var old=st;old.continuousHinge=false;old.neutralContacts=false;
    Check(PaperCornerPull.Create(s,old)?.UsesEdgeMatch!=true,"baseline shared-owner rejection");
    var p=PaperCornerPull.Create(s,st);Check(p!=null&&p.UsesEdgeMatch,"shared corner on tilted wing "+tilt);
    int direction=p.Pose(90)?1:-1;for(int a=0;a<=90;a+=3){Check(p.Pose(direction*a),"tilted sweep");ReverseDirectionVerification.Geometry(p.Preview);poses++;}
    var before=s.Faces;Check(s.FinishPull(p,direction*90),"tilted commit");PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"tilted undo");cases++;
   }
   Debug.Log("[JetEdge] PASS "+cases+" cases / "+poses+" sweep poses, baseline defects reproduced, contact blocking, stationary other wing, partial/full replay and undo.");
  }
 }
}

