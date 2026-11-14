using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class VisiblePetalInteractionVerification {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static object desk;static Type t;static object Get(string n)=>t.GetField(n,flags).GetValue(desk);
 static void Set(string n,object v)=>t.GetField(n,flags).SetValue(desk,v);
 static object Call(string n,params object[] a){foreach(var m in t.GetMethods(flags))if(m.Name==n&&m.GetParameters().Length==a.Length){bool ok=true;var ps=m.GetParameters();for(int i=0;i<a.Length;i++)if(a[i]!=null&&!ps[i].ParameterType.IsInstanceOfType(a[i]))ok=false;if(ok)return m.Invoke(desk,a);}throw new Exception(n);}


 static void Check(bool ok,string text){if(!ok)throw new Exception("[VisiblePetal] "+text);}
 static Vector2 Screen(PaperSheet.Face f,Vector2 p){return (Vector2)Call("SurfaceScreen",f,p);}
 static void WaitPlan(){for(int i=0;i<300&&Get("phase").ToString()=="PreparingClearance";i++){System.Threading.Thread.Sleep(10);Call("TickClearance");}Check(Get("phase").ToString()!="PreparingClearance","planner timeout");}
 public static void Run(){try{
 var all=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;Check(all.Length==6,"six-step saved recipe");
 var go=new GameObject("visible-petal-test");var owner=go.AddComponent<PaperFlightController>();t=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");var mat=new Material(Shader.Find("Unlit/Color"));desk=Activator.CreateInstance(t,flags,null,new object[]{owner,mat},null);
 int cases=0,visibility=0;
 foreach(bool reversed in new[]{false,true}){
 var initial=new FoldStep[4];Array.Copy(all,initial,4);Call("Open",initial);Call("Layout");Call("PositionCamera");
 for(int stage=4;stage<=5;stage++){
 if(stage==5){Call("FlipPaper");Check(Get("phase").ToString()=="FirstPoint","flip clears selection");}
 var sheet=(PaperSheet)Get("sheet");Check(sheet.Count==stage,"flip keeps recipe");Check(Get("scope").ToString()=="Top","visible default");
 var step=all[stage];var f=sheet.Faces[step.anchor];
 var a=Screen(f,step.pullTip);var b=Screen(f,step.pullStop);var l=Screen(f,step.pullLeft);var r=Screen(f,step.pullRight);
 Debug.Log($"[VisiblePetal] stage={stage} reversed={reversed} a={a} b={b} l={l} r={r}");
 foreach(var hit in new[]{reversed?b:a,reversed?a:b}){Call("PickSurfacePoint",hit);Debug.Log($"[Pick] phase={Get("phase")} first={Get("first")} second={Get("second")} notice={Get("notice")}");}
 Check(!(bool)Get("explicitLayer"),"line selection must not silently pin a hidden layer");Call("AddMorePoints");foreach(var hit in new[]{l,r}){Call("PickSurfacePoint",hit);Debug.Log($"[Pick] phase={Get("phase")} first={Get("first")} second={Get("second")} left={Get("leftFlank")} right={Get("rightFlank")} notice={Get("notice")}");}WaitPlan();
 Check((int)Get("rightFlank")>=0,"four handles retained stage="+stage);
 // Player may grab either selected endpoint, with no T/R/layer switch.
 int end=(int)Call("NearestVertex",a,13f);Check(end==(int)Get("first") || end==(int)Get("second"),"free handle identity");Call("BeginPull",end,a);
 Check(Get("phase").ToString()=="PullCorner","begin drag stage="+stage+" reverse="+reversed+" notice="+Get("notice"));
 var plan=(PaperCornerPull)Get("pull");Check(plan!=null&&plan.UsesPetalFold,"recognize petal");int sign=stage==4?1:-1;
 Check((int)t.GetProperty("InputSign",flags).GetValue(desk)==sign,"same drag maps outward on both faces");
 for(int angle=0;angle<=180;angle+=5){Call("SetAngle",(float)(sign*angle));Check(Mathf.Abs((float)Get("angle")-sign*angle)<.01f,"sweep "+angle);ReverseDirectionVerification.Geometry(plan.Preview);}
 Check(!plan.Pose(-sign*5),"opposite fixed layer must block");Call("SetAngle",(float)(sign*175));Call("SetAngle",(float)(sign*180));Call("FinishInteraction");
 Check(sheet.Count==stage+1,"commit");PetalFoldingVerification.Replay(sheet);cases++;
 }
 }
 // Occluded panes cannot be selected even if a stale explicit pin exists.
 Call("Open",new[]{all[0],all[1],all[2],all[3]});Call("Layout");Call("PositionCamera");
 var source=(PaperSheet)Get("sheet");
 for(int i=0;i<source.Faces.Count;i++){
 var f=source.Faces[i];Vector2 c=Vector2.zero;foreach(var p in f.points)c+=p;c/=f.points.Length;
 var screen=Screen(f,c);var uv=(Vector2)Call("ViewUV",screen);Call("CollectLayerHits",uv);
 var hits=(System.Collections.IList)Get("layerHits");if(hits.Count<2)continue;
 object front=hits[0],back=hits[hits.Count-1];int fi=(int)front.GetType().GetField("index").GetValue(front),bi=(int)back.GetType().GetField("index").GetValue(back);
 var method=t.GetMethod("SelectedFace",flags);Set("phase",Enum.Parse(t.GetField("phase",flags).FieldType,"PickSide"));Set("explicitLayer",true);Set("pinnedFace",bi);
 object[] args={uv,Vector2.zero,0};Check(method.Invoke(desk,args)!=null && (int)args[2]==fi,"new click resolves visible face rather than stale hidden layer");
 Call("Browse");Call("PickFaceAt",screen);Check((int)Get("pinnedFace")==fi,"front click");Call("PickFaceAt",screen);Check((int)Get("pinnedFace")==fi,"repeat click must not cycle behind");
 Call("Browse");visibility++;
 }
 Check(visibility>0,"overlap cases exercised");
 Call("CancelClearance");var rt=(RenderTexture)Get("target");((Camera)Get("camera")).targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate((Mesh)Get("mesh"));UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mat);
 Debug.Log("[VisiblePetal] PASS "+cases+" sequential front/back folds with either endpoint order; 148 collision/geometry poses; flip/default/drag direction; "+visibility+" occluded/repeated face picks; replay.");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}

