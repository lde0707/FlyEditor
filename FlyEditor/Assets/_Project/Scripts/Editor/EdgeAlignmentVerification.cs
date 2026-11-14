using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class EdgeAlignmentVerification {
  static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  static object desk;static Type type;static object Get(string n)=>type.GetField(n,F).GetValue(desk);
  static object Call(string n,params object[] a)=>type.GetMethod(n,F).Invoke(desk,a);
  static void Check(bool ok,string message){if(!ok)throw new Exception("[EdgeAlignment] "+message);}
  static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
  static Vector2 R(Vector2 p,int turn){while(turn-->0)p=new Vector2(-p.y,p.x);return p;}
  static void AxisTests(){var helper=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperEdgeAlignment");var method=helper.GetMethod("TryAxis",BindingFlags.Static|BindingFlags.NonPublic);int cases=0;
   foreach(bool parallel in new[]{false,true})for(int r=0;r<4;r++)for(int order=0;order<4;order++){
    Vector2 a=parallel?new Vector2(11,0):new Vector2(0,0),b=new Vector2(11,11),c=Vector2.zero,d=parallel?new Vector2(0,11):new Vector2(0,8);
    a=R(a,r);b=R(b,r);c=R(c,r);d=R(d,r);if((order&1)!=0){var t=a;a=b;b=t;}if((order&2)!=0){var t=c;c=d;d=t;}
    object[] args={a,b,c,d,Vector2.zero,0f};Check((bool)method.Invoke(null,args),"axis");var n=(Vector2)args[4];float offset=(float)args[5];
    foreach(var p in new[]{a,b,(a+b)*.5f}){var q=p-2*n*(Vector2.Dot(n,p)-offset)/n.sqrMagnitude;Check(Mathf.Abs(Cross((d-c).normalized,q-c))<.0001f,"reflection did not match target");}cases++;
   }
   object[] same={Vector2.zero,Vector2.up,Vector2.up,Vector2.up*3,Vector2.zero,0f};Check(!(bool)method.Invoke(null,same),"coincident target");
   object[] invalid={new Vector2(float.NaN,0),Vector2.up,Vector2.zero,Vector2.right,Vector2.zero,0f};Check(!(bool)method.Invoke(null,invalid),"nonfinite target");Debug.Log("[EdgeAlignment] PASS "+cases+" parallel/intersecting, rotated/reversed axes; <0.0001 edge-to-target error.");
  }
  static Vector2 Screen(Vector2 p){var rect=(Rect)Get("viewport");return rect.position+Vector2.Scale(p,new Vector2(rect.width/1280,rect.height/900));}
  static object Edge(Vector2 p){object[] args={Screen(p),null};Check((bool)type.GetMethod("NearestAlignmentEdge",F).Invoke(desk,args),"visible edge "+p);return args[1];}
  static T Field<T>(object value,string n)=>(T)value.GetType().GetField(n).GetValue(value);
  static void Aligned(PaperSheet.Face basis,Vector2 p,IReadOnlyList<PaperSheet.Face> after,Vector3 c,Vector3 d){var m=basis.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));bool found=false;
   foreach(var f in after){var q=f.material.inverse.MultiplyPoint3x4(m);var uv=new Vector2(q.x,q.z);if(!f.Contains(uv))continue;Check(Vector3.Cross((d-c).normalized,f.World(uv,0)-c).magnitude<.002f,"committed edge did not land on line");found=true;}Check(found,"matched edge lost");
  }
  public static void Run(){try{AxisTests();var seed=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;
   var go=new GameObject("edge-alignment-check");var owner=go.AddComponent<PaperFlightController>();var material=new Material(Shader.Find("Unlit/Color"));type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");desk=Activator.CreateInstance(type,F,null,new object[]{owner,material},null);
   Call("Open",seed);Call("Layout");Call("SnapView",7,false);
   var sources=new[]{new Vector2(879.3f,549.1f),new Vector2(739.1f,689.3f),new Vector2(400.65f,549.1f),new Vector2(660.5f,639.8f)};
   var targets=new[]{new Vector2(809.25f,619.25f),new Vector2(470.8f,280.8f),new Vector2(809.2f,280.8f),new Vector2(893.9f,196.1f)};
   int poses=0;
   for(int k=0;k<4;k++){
    if(k==2)Call("SnapView",7,true);Call("BeginEdgeAlignment");var sheet=(PaperSheet)Get("sheet");var before=sheet.Faces;
    object source=Edge(sources[k]),target=Edge(targets[k]);var basis=before[Field<int>(source,"face")];var refFace=before[Field<int>(target,"face")];
    var c=refFace.World(Field<Vector2>(target,"a"),0);var d=refFace.World(Field<Vector2>(target,"b"),0);
    Check((bool)Call("TryAlignEdges",source,target),"two-edge plan "+k);var turn=(PaperSheet.Turn)Get("turn");int sign=(int)Get("edgeMotionSign");
    var fixedFaces=new List<PaperSheet.Face>(turn.fixedFaces);var preview=new List<PaperSheet.Face>();
    for(int a=0;a<=180;a+=6){Check((bool)typeof(PaperSheet).GetMethod("TryPreviewTurn",F).Invoke(sheet,new object[]{turn,sign*(float)a,preview}),"pose");ReverseDirectionVerification.Geometry(preview);foreach(var f in fixedFaces){Vector2 mid=Vector2.zero;foreach(var p in f.points)mid+=p;mid/=f.points.Length;var m=f.material.MultiplyPoint3x4(new Vector3(mid.x,0,mid.y));bool found=false;foreach(var g in preview){var q=g.material.inverse.MultiplyPoint3x4(m);var v=new Vector2(q.x,q.z);if(!g.Contains(v))continue;Check(Vector3.Distance(f.World(mid,0),g.World(v,0))<.002f,"stationary layer moved");found=true;}Check(found,"fixed material lost");}poses++;}
    Call("CloseToContact");Check(sheet.Count==seed.Length+k+1,"close committed count");Aligned(basis,Field<Vector2>(source,"a"),sheet.Faces,c,d);Aligned(basis,Field<Vector2>(source,"b"),sheet.Faces,c,d);ReverseDirectionVerification.Geometry(sheet.Faces);PetalFoldingVerification.Replay(sheet);
    Check(Get("edgeAlignState").ToString()=="Off","mode did not reset");
   }
   Call("BeginEdgeAlignment");Call("ToggleEdgeAlignment");Check(Get("edgeAlignState").ToString()=="Off","M cancel");Call("BeginEdgeAlignment");Call("CancelAngle");Check(Get("edgeAlignState").ToString()=="Off","Escape cancel");
   Call("Open",seed);Call("Layout");Call("SnapView",7,false);Call("BeginEdgeAlignment");var chosen=Edge(sources[0]);type.GetField("alignSource",F).SetValue(desk,chosen);type.GetField("edgeAlignState",F).SetValue(desk,Enum.Parse(type.GetField("edgeAlignState",F).FieldType,"Target"));Call("SnapView",7,true);Check(Get("edgeAlignState").ToString()=="Off","hidden source retained after flip");
   Debug.Log("[EdgeAlignment] PASS four sequential front/back edge matches, "+poses+" poses, committed edge <0.002 from reference, fixed layers, area/seams, serialized replay, mode reset/cancel/hidden selection.");
   Call("Dispose");UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);
   WingSideVerification.Run();WingSideVerification.RunFlat();JetEdgeVerification.Run();EditorApplication.Exit(0);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
 }
}
