using System;using System.IO;using System.Collections.Generic;using System.Reflection;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using FlyEditor.Flight;using FlyEditor.Editor;
public static class CraneEdgeAudit {
 static string folder;static readonly string[] Names={"square","diagonal","half-triangle","front-pocket","back-pocket","front-petal","back-petal","narrow-front-left","narrow-front-right","narrow-back-left","narrow-back-right","body-book","neck","tail","beak","wing-front","wing-back"};
 static void Check(bool v,string m){if(!v)throw new Exception("[CraneEdge] "+m);}
 static Vector2 Local(PaperSheet.Face f,Vector3 p){var v=f.material.inverse.MultiplyPoint3x4(p);return new Vector2(v.x,v.z);}
 static Vector3 Original(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
 static FoldStep Map(FoldStep st,PaperSheet.Face from,PaperSheet.Face to,int index){
  st.anchor=index;if(st.flap>0)st.flap=index+1;
  st.pullTip=Local(to,Original(from,st.pullTip));st.pullStop=Local(to,Original(from,st.pullStop));
  st.pullLeft=Local(to,Original(from,st.pullLeft));st.pullRight=Local(to,Original(from,st.pullRight));
  if(!st.cornerPull){var n=st.Normal;var q=n*(st.Position/n.sqrMagnitude);var u=to.frame.inverse.MultiplyPoint3x4(from.World(q,0));var v=to.frame.inverse.MultiplyPoint3x4(from.World(q+new Vector2(-n.y,n.x),0));var d=new Vector2(v.x-u.x,v.z-u.z);st.creaseNormal=new Vector2(d.y,-d.x).normalized;st.creaseOffset=Vector2.Dot(st.creaseNormal,new Vector2(u.x,u.z));}
  return st;
 }
 static bool ApplyMapped(PaperSheet paper,PaperSheet before,FoldStep old){
  var source=before.Faces[old.anchor];var material=Original(source,old.pullTip);
  Vector2 centre=Vector2.zero;foreach(var q in source.points)centre+=q;centre/=source.points.Length;
  var pickedMaterial=Original(source,centre);
  for(int i=0;i<paper.Faces.Count;i++){
   var f=paper.Faces[i];if(old.cornerPull&&!f.Contains(Local(f,material)))continue;
   if(!old.cornerPull&&old.flap>0&&!f.Contains(Local(f,pickedMaterial)))continue;
   var step=Map(old,source,f,i);if(paper.ApplyStep(step))return true;
  }return false;
 }
 static bool Edge(PaperSheet s,Vector2 tip,int direction){
  var material=new Vector3(tip.x,0,tip.y);
  for(int i=0;i<s.Faces.Count;i++){var f=s.Faces[i];if(!f.Contains(Local(f,material)))continue;
   for(int e=0;e<f.points.Length;e++)if(EdgePocketVerification.Try(s,i,e,out var p,out int sign)&&sign==direction){
    if(Vector3.Distance(Original(f,p.Step.pullTip),material)>.01f)continue;
    for(int a=0;a<=180;a+=5){Check(p.Pose(sign*a),"edge sweep "+a);ReverseDirectionVerification.Geometry(p.Preview);}
    return s.FinishPull(p,sign*180);
   }
  }return false;
 }
 [MenuItem("Tools/FlyEditor/Validation/Capture crane diagram steps")]
 public static void Run(){try{
  folder=Environment.GetEnvironmentVariable("FLYEDITOR_CRANE_STEPS")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/crane-edge/steps"));Directory.CreateDirectory(folder);
  string path=Environment.GetEnvironmentVariable("FLYEDITOR_CRANE_RECIPE")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/OrigamiAudit/crane-finish/crane.json"));
  var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(path)).paperFolds;var s=new PaperSheet();var reference=new PaperSheet();Capture(s,0);
  for(int i=0;i<recipe.Length;i++){
   bool ok=i==2?Edge(s,new Vector2(0,11),1):i==3?Edge(s,new Vector2(11,0),-1):ApplyMapped(s,reference,recipe[i]);
   Check(ok,"step "+(i+1));ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);Check(reference.ApplyStep(recipe[i]),"reference");Capture(s,i+1);
   Debug.Log("[CraneEdge] step "+(i+1)+" "+Names[i+1]+" geometry/replay PASS faces="+s.Faces.Count);
  }
  Debug.Log("[CraneEdge] PASS folded crane silhouette; body inflation remains a separate operation.");if(Application.isBatchMode)EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);else throw;}}
 static void Capture(PaperSheet s,int index){string name=index.ToString("00")+"-"+Names[index];File.WriteAllText(Path.Combine(folder,name+".json"),JsonUtility.ToJson(new ResearchSave{paperFolds=s.Recipe()},true));Render(s,Path.Combine(folder,name+".png"),index<12?new Vector3(0,18,1):new Vector3(12,8,8));}
 public static void Render(PaperSheet s,string path,Vector3 direction){
  var scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Actual folded paper");var cameraGo=new GameObject("Fold capture");
  UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
  var mesh=new Mesh();var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldMesh");var method=type.GetMethod("Build");var info=method.GetParameters();var args=new object[info.Length];
  for(int i=0;i<args.Length;i++)args[i]=info[i].HasDefaultValue?info[i].DefaultValue:null;
  args[0]=mesh;args[1]=s;args[3]=0f;args[4]=new List<PaperSheet.FoldVertex>();args[18]=direction.normalized;method.Invoke(Activator.CreateInstance(type,true),args);mesh.RecalculateBounds();
  go.AddComponent<MeshFilter>().sharedMesh=mesh;var mat=new Material(Shader.Find("FlyEditor/PaperFlight"));go.AddComponent<MeshRenderer>().sharedMaterial=mat;
  var cam=cameraGo.AddComponent<Camera>();cam.cameraType=CameraType.Preview;cam.orthographic=true;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.92f,.94f,.92f);
  var center=mesh.bounds.center;cam.transform.position=center+direction;cam.transform.LookAt(center,new Vector3(1,0,1));cam.orthographicSize=Mathf.Max(4,mesh.bounds.size.magnitude*.58f);cam.nearClipPlane=.1f;cam.farClipPlane=100;cam.scene=scene;
  var rt=new RenderTexture(1200,1000,24);var tex=new Texture2D(1200,1000,TextureFormat.RGB24,false);var previous=RenderTexture.active;
  try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1200,1000),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
  finally{cam.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(mat);EditorSceneManager.ClosePreviewScene(scene);}
 }
}
