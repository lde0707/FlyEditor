using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEngine;using UnityEditor;using FlyEditor.Flight;
public static class FourHandleSelectionProbe {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static object desk;static Type t;static object Get(string n)=>t.GetField(n,flags).GetValue(desk);
 static void Set(string n,object v)=>t.GetField(n,flags).SetValue(desk,v);
 static object Call(string n,params object[] a){foreach(var m in t.GetMethods(flags))if(m.Name==n&&m.GetParameters().Length==a.Length){bool ok=true;var ps=m.GetParameters();for(int i=0;i<a.Length;i++)if(a[i]!=null&&!ps[i].ParameterType.IsInstanceOfType(a[i]))ok=false;if(ok)return m.Invoke(desk,a);}throw new Exception(n);}
 public static void Run(){try{
 var all=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;var recipe=new FoldStep[4];Array.Copy(all,recipe,4);
 var go=new GameObject("handle-test");var owner=go.AddComponent<PaperFlightController>();t=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");var mat=new Material(Shader.Find("Unlit/Color"));desk=Activator.CreateInstance(t,flags,null,new object[]{owner,mat},null);
 foreach(float pitch in new[]{57f,123f,90f})foreach(float yaw in new[]{-18f,0f,90f,180f})foreach(int turn in new[]{0,1,2,3}){
 Call("Open",recipe);Set("pitch",pitch);Set("yaw",yaw);Call("Layout");Call("PositionCamera");Call("Rebuild");
 var sheet=(PaperSheet)Get("sheet");var vs=(List<PaperSheet.FoldVertex>)Get("displayVertices");var visible=(List<bool>)Get("visiblePoints");var cam=(Camera)Get("camera");var table=(Transform)Get("table");var rect=(Rect)Get("viewport");var list=new List<Vector2>();
 for(int i=0;i<vs.Count;i++)if(visible[i]){var f=sheet.Faces[vs[i].anchor];var q=cam.WorldToViewportPoint(table.position+f.World(vs[i].point,(f.layer+(Vector3.Dot(cam.transform.forward,f.frame.MultiplyVector(Vector3.up))<0?1:0))*PaperSheet.Thickness));list.Add(new Vector2(rect.x+q.x*rect.width,rect.y+(1-q.y)*rect.height));}
 // Choose four silhouette corners by diagonally extreme projections; this matches a rotated square.
 var corners=new Vector2[4];var dirs=new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
 for(int j=0;j<4;j++){float best=float.NegativeInfinity;foreach(var p in list){float d=Vector2.Dot(p,dirs[j]);if(d>best){best=d;corners[j]=p;}}}
 Call("PickSurfacePoint",corners[turn]);Call("PickSurfacePoint",corners[(turn+2)%4]);Call("AddMorePoints");Call("PickSurfacePoint",corners[(turn+1)%4]);Call("PickSurfacePoint",corners[(turn+3)%4]);
 if((int)Get("rightFlank")<0)throw new Exception("Fourth click disappeared");
 if(Get("phase").ToString()=="ReviewFourPoints"){
  if(Get("selected")!=null)throw new Exception("Invalid fold retained executable preview");
  int last=(int)Get("rightFlank");var before=sheet.Faces;
  Call("BeginVertexDrag",(int)Get("first"),corners[turn],corners[(turn+2)%4]);
  if(Get("phase").ToString()!="ReviewFourPoints" || !ReferenceEquals(sheet.Faces,before))throw new Exception("Invalid selection opened existing fold");
  Call("DeselectPoint",last);if((int)Get("rightFlank")>=0 || Get("phase").ToString()!="RightFlank")throw new Exception("Cannot deselect fourth");
  Call("PickSurfacePoint",corners[(turn+3)%4]);if((int)Get("rightFlank")<0)throw new Exception("Cannot reselect fourth");
  Call("CycleLayer");if((int)Get("rightFlank")<0 || !ReferenceEquals(sheet.Faces,before))throw new Exception("Layer retry discarded handles or changed geometry");
 }
 Debug.Log($"[FourHandle] pitch={pitch} yaw={yaw} corner={turn} phase={Get("phase")} first={Get("first")} second={Get("second")} left={Get("leftFlank")} right={Get("rightFlank")} notice={Get("notice")}");
 }
 Debug.Log("[FourHandle] PASS 48 view/order cases; retention, deselection, reselection and blocked drag safety, explicit layer retry.");
 Call("CancelClearance");var rt=(RenderTexture)Get("target");((Camera)Get("camera")).targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate((Mesh)Get("mesh"));UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mat);EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
