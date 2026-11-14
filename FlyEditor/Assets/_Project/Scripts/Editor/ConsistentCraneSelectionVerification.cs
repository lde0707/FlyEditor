using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;
using FlyEditor.Editor;
public static class ConsistentCraneSelectionVerification {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static object desk;static Type type;
 static object Get(string n)=>type.GetField(n,Flags).GetValue(desk);
 static void Set(string n,object v)=>type.GetField(n,Flags).SetValue(desk,v);
 static object Call(string n,params object[] a){foreach(var m in type.GetMethods(Flags)){
  if(m.Name!=n||m.GetParameters().Length!=a.Length)continue;var ps=m.GetParameters();bool ok=true;
  for(int i=0;i<a.Length;i++)if(a[i]!=null&&!ps[i].ParameterType.IsInstanceOfType(a[i]))ok=false;
  if(ok)return m.Invoke(desk,a);
 }throw new Exception("method "+n);}
 static void Check(bool ok,string text){if(!ok)throw new Exception("[CraneSelection] "+text);}
 static Vector2 Screen(PaperSheet.Face f,Vector2 p)=>(Vector2)Call("SurfaceScreen",f,p);
 static bool Exposed(int index,Vector2 p){object[] args={new PaperSheet.FoldVertex(index,p),0f};return (bool)type.GetMethod("PointVisible",Flags).Invoke(desk,args);}
 static void View(int view){if(view<2)Call("SnapView",7,view==1);else{Set("pitch",view==2?57f:303f);Set("yaw",view==2?342f:162f);Call("PositionCamera");}}
 static bool Intersect(PaperSheet.Face face,FoldStep line,out Vector2 a,out Vector2 b){object[] args={face,line,Vector2.zero,Vector2.zero};bool ok=(bool)typeof(PaperSheet).GetMethod("Intersection",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);a=(Vector2)args[2];b=(Vector2)args[3];return ok;}
 public static void Run(){try{
  var go=new GameObject("crane-selection-verification");var owner=go.AddComponent<PaperFlightController>();
  type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");var mat=new Material(Shader.Find("Unlit/Color"));desk=Activator.CreateInstance(type,Flags,null,new object[]{owner,mat},null);
  int folds=0,picks=0,corners=0,skipped=0;var outcomes=new Dictionary<string,int>();var report=new List<string>{"recipe,stage,view,reversed,axis,side,state"};
  foreach(var path in Environment.GetEnvironmentVariable("FLYEDITOR_CRANE_RECIPES").Split('|')){
   var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(path)).paperFolds;var replay=new PaperSheet();
   for(int i=0;i<recipe.Length;i++){Check(replay.ApplyStep(recipe[i]),"full recipe "+path+" step "+i);ReverseDirectionVerification.Geometry(replay.Faces);folds++;}
   PetalFoldingVerification.Replay(replay);
   foreach(int stage in new[]{0,2,4,6,8,10,13,16}){
    if(stage>recipe.Length)continue;var prefix=new FoldStep[stage];Array.Copy(recipe,prefix,stage);
    for(int view=0;view<4;view++)foreach(bool reversed in new[]{false,true})for(int axis=0;axis<2;axis++){
     Call("Open",prefix);Call("Layout");View(view);var sheet=(PaperSheet)Get("sheet");int best=-1;float bestArea=0;Vector2 pa=default,pb=default;
     // Choose an actually visible, sufficiently large panel. Samples are on
     // the current mesh; no diagram-specific vertex indices enter the runtime.
     for(int i=0;i<sheet.Faces.Count;i++){
      var f=sheet.Faces[i];Vector2 centre=Vector2.zero;foreach(var p in f.points)centre+=p;centre/=f.points.Length;
      if(!Exposed(i,centre))continue;
      var line=new FoldStep{creaseNormal=axis==0?Vector2.right:Vector2.up,creaseOffset=centre[axis]};
      if(!Intersect(f,line,out var a,out var b)||!Exposed(i,a)||!Exposed(i,b))continue;
      float span=(Screen(f,a)-Screen(f,b)).sqrMagnitude;float area=PaperSheet.Area(f.points);
      if(span<400||area<=bestArea)continue;best=i;bestArea=area;pa=a;pb=b;
     }
     if(best<0){skipped++;continue;}
     var face=sheet.Faces[best];var aScreen=Screen(face,pa);var bScreen=Screen(face,pb);
     Call("PickSurfacePoint",reversed?bScreen:aScreen);Check((int)Get("first")>=0,"first point "+stage+"/"+view);
     Check((bool)Call("SelectionExposed"),"first point selected hidden face");corners++;
     Call("PickSurfacePoint",reversed?aScreen:bScreen);Check((int)Get("second")>=0,"second point "+stage+"/"+view+" reverse="+reversed+" axis="+axis+" a="+aScreen+" b="+bScreen+" phase="+Get("phase")+" first="+Get("first")+" notice="+Get("notice"));
     Check((bool)Call("SelectionExposed"),"pair rebound to hidden face");corners++;
     object[] lineArgs={best,default(FoldStep)};bool valid=(bool)type.GetMethod("TrySelectionAxis",Flags,null,new[]{typeof(int),typeof(FoldStep).MakeByRefType()},null).Invoke(desk,lineArgs);
     Check(valid,"coplanar selected points rejected");var chosenLine=(FoldStep)lineArgs[1];
     foreach(int side in new[]{-1,1}){
      var line=chosenLine;line.side=side;
      var part=(PaperSheet.Face)typeof(PaperSheet).GetMethod("Clip",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{face,line,side});
      if(part==null){skipped++;continue;}Vector2 centre=Vector2.zero;foreach(var p in part.points)centre+=p;centre/=part.points.Length;
      if(!Exposed(best,centre)){skipped++;continue;}
      Check((bool)Call("ChooseSide",Screen(face,centre),false),"visible side not selectable "+stage+"/"+view+"/"+side);
      string state=Get("sideChoiceState").ToString();Check(state!="None","no side choice state");
      if(state=="Ready")Check(Get("turn")!=null,"ready without physical plan");else Check(Get("surfaceChoice")!=null&&Get("turn")==null,"blocked selection must remain explicit without motion");
      if(!outcomes.ContainsKey(state))outcomes[state]=0;outcomes[state]++;picks++;
      report.Add(Path.GetFileName(Path.GetDirectoryName(path))+","+stage+","+view+","+reversed+","+axis+","+side+","+state);
     }
    }
   }
  }
  Call("Open",Array.Empty<FoldStep>());foreach(int axis in new[]{1,3,7})foreach(bool opposite in new[]{false,true}){
   Call("SnapView",axis,opposite);var forward=((Camera)Get("camera")).transform.forward;
   var expected=axis==1?Vector3.forward:axis==3?Vector3.left:Vector3.down;if(opposite)expected=-expected;
   Check(Vector3.Dot(forward,expected)>.99999f,"fixed view direction "+axis+"/"+opposite);
  }
  var folder=Environment.GetEnvironmentVariable("FLYEDITOR_SELECTION_OUT");Directory.CreateDirectory(folder);File.WriteAllLines(Path.Combine(folder,"crane-selection.csv"),report);
  foreach(var outcome in outcomes)Debug.Log("[CraneSelection] "+outcome.Key+"="+outcome.Value);
  Debug.Log("[CraneSelection] PASS full recipe folds="+folds+", visible side picks="+picks+", exposed endpoint checks="+corners+", six exact camera views; skipped edge-on/occluded/degenerate samples="+skipped);
  Call("Dispose");UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mat);EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}

