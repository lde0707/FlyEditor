using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEngine;using UnityEditor;using FlyEditor.Flight;using FlyEditor.Editor;
public static class SavedPetalInteractionVerification {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static object desk;static Type t;static object Get(string n)=>t.GetField(n,flags).GetValue(desk);
 static void Set(string n,object v)=>t.GetField(n,flags).SetValue(desk,v);
 static object Call(string n,params object[] a){foreach(var m in t.GetMethods(flags))if(m.Name==n&&m.GetParameters().Length==a.Length){bool ok=true;var ps=m.GetParameters();for(int i=0;i<a.Length;i++)if(a[i]!=null&&!ps[i].ParameterType.IsInstanceOfType(a[i]))ok=false;if(ok)return m.Invoke(desk,a);}throw new Exception(n);}

 static void Check(bool ok,string text){if(!ok)throw new Exception("[SavedPetal] "+text);}
 public static void Run(){try{
 var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;
 Check(recipe.Length==4,"expected saved four-fold sheet");
 var go=new GameObject("saved-petal-test");var owner=go.AddComponent<PaperFlightController>();t=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");var mat=new Material(Shader.Find("Unlit/Color"));desk=Activator.CreateInstance(t,flags,null,new object[]{owner,mat},null);
 int cases=0;
 foreach(bool back in new[]{false,true})foreach(bool swap in new[]{false,true}){
 Call("Open",recipe);Set("pitch",back?237f:57f);Set("yaw",back?-18f:135f);Call("Layout");Call("PositionCamera");Call("Rebuild");
 var sheet=(PaperSheet)Get("sheet");var vs=(List<PaperSheet.FoldVertex>)Get("displayVertices");var visible=(List<bool>)Get("visiblePoints");var cam=(Camera)Get("camera");var table=(Transform)Get("table");var rect=(Rect)Get("viewport");var list=new List<Vector2>();
 for(int i=0;i<vs.Count;i++)if(visible[i]){var f=sheet.Faces[vs[i].anchor];var q=cam.WorldToViewportPoint(table.position+f.World(vs[i].point,(f.layer+(Vector3.Dot(cam.transform.forward,f.frame.MultiplyVector(Vector3.up))<0?1:0))*PaperSheet.Thickness));list.Add(new Vector2(rect.x+q.x*rect.width,rect.y+(1-q.y)*rect.height));}
 var corners=new Vector2[4];var dirs=new[]{Vector2.up,Vector2.right,Vector2.down,Vector2.left};
 for(int j=0;j<4;j++){float best=float.NegativeInfinity;foreach(var p in list){float d=Vector2.Dot(p,dirs[j]);if(d>best){best=d;corners[j]=p;}}}
 // GUI coordinates have downward-positive Y: corner 0 is the bottom tip.
 Call("PickSurfacePoint",corners[swap?2:0]);Call("PickSurfacePoint",corners[swap?0:2]);Call("AddMorePoints");Call("PickSurfacePoint",corners[3]);
 // Highlighted corner: click 10 display pixels outside the silhouette.
 Call("PickSurfacePoint",corners[1]+Vector2.right*10);
 Check((int)Get("rightFlank")>=0,"highlighted fourth corner must remain selected");
 if(swap){Call("SwapPullEnds");Check(sheet.Count==4,"swap must not commit or mutate paper");}
 Check(Get("phase").ToString()=="PickSide","valid four-point state: "+Get("phase"));
 var step=(FoldStep)Get("selected");var plan=PaperCornerPull.Create(sheet,step);Check(plan!=null&&plan.UsesPetalFold,"petal recognition");int sign=back?-1:1;
 Check(plan.TryMove(0,sign*180,out var reached)&&Mathf.Abs(reached)==180,"full opening");
 for(int angle=0;angle<=180;angle+=5){Check(plan.Pose(sign*angle),"collision sweep "+angle);ReverseDirectionVerification.Geometry(plan.Preview);}
 Check(!plan.Pose(-sign*5),"opposite stationary layer must block penetration");
 Check(sheet.FinishPull(plan,sign*180),"commit");PetalFoldingVerification.Replay(sheet);Check(sheet.Undo()&&sheet.Count==4,"undo");cases++;
 }
 Call("CancelClearance");var rt=(RenderTexture)Get("target");((Camera)Get("camera")).targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate((Mesh)Get("mesh"));UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mat);
 Debug.Log("[SavedPetal] PASS "+cases+" front/back and swapped-end cases; 10px silhouette clicks; 148 collision/geometry poses; blocked inward motion; replay and undo.");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
