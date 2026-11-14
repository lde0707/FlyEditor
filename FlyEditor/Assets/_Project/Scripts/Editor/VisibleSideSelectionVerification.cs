using System;using System.Collections.Generic;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class VisibleSideSelectionVerification {
 const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static Type type;static object desk;static GameObject go;static Material mat;
 static object Get(string n)=>type.GetField(n,H).GetValue(desk);
 static object Call(string n,params object[] args){foreach(var m in type.GetMethods(H)){if(m.Name!=n||m.GetParameters().Length!=args.Length)continue;var p=m.GetParameters();bool match=true;for(int i=0;i<args.Length;i++)if(args[i]!=null&&!p[i].ParameterType.IsInstanceOfType(args[i]))match=false;if(match)return m.Invoke(desk,args);}throw new Exception(n);}
 static void Check(bool ok,string text){if(!ok)throw new Exception("[VisibleSide] "+text);}
 static FoldStep Line(Vector2 normal,float at,int side)=>new FoldStep{creaseNormal=normal,creaseOffset=at,side=side,continuousHinge=true,neutralContacts=true,compactLayers=true,materialPartition=true,vertexJoints=true,preserveSlivers=true};
 static void Init(){go=new GameObject("visible-side-check");mat=new Material(Shader.Find("FlyEditor/PaperFlight"));type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");desk=Activator.CreateInstance(type,H,null,new object[]{go.AddComponent<PaperFlightController>(),mat},null);}
 static void Dispose(){Call("Dispose");UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mat);}
 static void Open(PaperSheet s){Call("Open",s.Recipe());Call("Layout");Call("SnapView",7,false);}
 static PaperSheet Sheet=>(PaperSheet)Get("sheet");
 static Vector2 Screen(Vector2 p){PaperSheet.Face top=null;foreach(var f in Sheet.Faces)if(f.Contains(p)&&(top==null||f.layer>top.layer))top=f;Check(top!=null,"point outside paper");return (Vector2)Call("SurfaceScreen",top,p);}
 static PaperSheet ThreeFaces(){var s=new PaperSheet();Check(s.Finish(s.Plan(Line(Vector2.right,2,-1)),180),"left flap seed");Check(s.Finish(s.Plan(Line(Vector2.right,9,1)),180),"right flap seed");return s;}
 static Vector2 R(Vector2 p,int r){while(r-->0)p=new Vector2(11-p.y,p.x);return p;}
 static Vector2 N(Vector2 n,int r){while(r-->0)n=new Vector2(-n.y,n.x);return n;}
 static PaperSheet RotatedSeed(int r){var s=new PaperSheet();var n=N(Vector2.right,r);float d=UnityEngine.Vector2.Dot(n-Vector2.right,Vector2.one*5.5f);Check(s.ApplyStep(new FoldStep{creaseNormal=n,creaseOffset=2+d,side=-1,degrees=180,continuousHinge=true,neutralContacts=true,compactLayers=true,materialPartition=true,vertexJoints=true,preserveSlivers=true}),"rotated left seed");Check(s.ApplyStep(new FoldStep{creaseNormal=n,creaseOffset=9+d,side=1,degrees=180,continuousHinge=true,neutralContacts=true,compactLayers=true,materialPartition=true,vertexJoints=true,preserveSlivers=true}),"rotated right seed");return s;}
 static void Geometry(PaperSheet.Turn turn,PaperSheet sheet,float angle){var target=new List<PaperSheet.Face>();Check((bool)typeof(PaperSheet).GetMethod("TryPreviewTurn",H).Invoke(sheet,new object[]{turn,angle,target}),"preview rejected "+angle);FlyEditor.Editor.ReverseDirectionVerification.Geometry(target);}
 static void Test(){int cases=0,poses=0;
  foreach(bool oblique in new[]{false,true})for(int r=0;r<4;r++)foreach(bool perspective in new[]{false,true})foreach(bool reverse in new[]{false,true})foreach(bool blue in new[]{false,true}){
   Open(RotatedSeed(r));if(perspective)Call("NavigateView",UnityEngine.InputSystem.Key.Numpad5,false,false);
   var a=Screen(R(oblique?new Vector2(3.5f,11):new Vector2(5.5f,0),r));var b=Screen(R(oblique?new Vector2(5.5f,0):new Vector2(5.5f,11),r));Call("PickSurfacePoint",reverse?b:a);Call("PickSurfacePoint",reverse?a:b);Check((int)Get("second")>=0,"line missing");
   Check((bool)Call("ChooseSide",Screen(R(new Vector2(blue?3:oblique?4.25f:4.5f,5.5f),r)),false),"side click rejected");var turn=Get("turn") as PaperSheet.Turn;float area=0;if(turn!=null)foreach(var f in turn.movingFaces)area+=PaperSheet.Area(f.points);
   Check(turn!=null&&Mathf.Abs(area-(oblique?48.8125f:60.5f))<(oblique?.03f:.01f),"one click must select both visible crease-side panels; actual area="+area+" oblique="+oblique+" r="+r+" perspective="+perspective+" reversed="+reverse+" notice="+Get("notice"));
   Check(turn.step.selectedFaces!=null&&turn.step.selectedFaces.Length==2,"must retain both independently visible source panels");
   typeof(SelectionRegionVerification).GetMethod("MeshMatches",H).Invoke(null,new object[]{Sheet,turn,-1,null});
   var source=Sheet.Faces;foreach(float angle in new[]{0f,3f,30f,90f,180f}){Geometry(turn,Sheet,angle);poses++;}
   Check(Sheet.Finish(turn,90),"visible region fold commit");FlyEditor.Editor.PetalFoldingVerification.Replay(Sheet);Check(Sheet.Undo()&&ReferenceEquals(source,Sheet.Faces),"visible region undo");cases++;
  }
  foreach(bool back in new[]{false,true})foreach(bool stack in new[]{false,true})foreach(bool perspective in new[]{false,true}){
   var seed=new PaperSheet();Check(seed.ApplyStep(new FoldStep{creaseNormal=Vector2.up,creaseOffset=5.5f,side=1,degrees=180,continuousHinge=true,neutralContacts=true,compactLayers=true,materialPartition=true,vertexJoints=true,preserveSlivers=true}),"book seed");Open(seed);Call("SnapView",7,back);if(perspective)Call("NavigateView",UnityEngine.InputSystem.Key.Numpad5,false,false);
   PaperSheet.Face visible=Sheet.Faces[back?0:1];Vector2 At(Vector2 p)=>(Vector2)Call("SurfaceScreen",visible,p);
   Call("PickSurfacePoint",At(new Vector2(0,2.75f)));Call("PickSurfacePoint",At(new Vector2(11,2.75f)));if(stack)Call("ChoosePly",Enum.Parse(type.GetNestedType("Ply",BindingFlags.NonPublic),"Stack"));
   Check((bool)Call("ChooseSide",At(new Vector2(5,1)),false),"book click");var turn=Get("turn") as PaperSheet.Turn;
   Check(turn!=null&&turn.step.selectedFaces.Length==(stack?2:1),"visible side silently selected buried ply");float area=0;foreach(var f in turn.movingFaces)area+=PaperSheet.Area(f.points);Check(Mathf.Abs(area-(stack?60.5f:30.25f))<.01f,"book moving area");
   var hidden=Sheet.Faces[back?1:0];var source=Sheet.Faces;Check(Sheet.Finish(turn,back?-60:60),"book commit");if(!stack)typeof(BoxSelectionVerification).GetMethod("Fixed",H).Invoke(null,new object[]{hidden,Sheet.Faces});
   FlyEditor.Editor.ReverseDirectionVerification.Geometry(Sheet.Faces);FlyEditor.Editor.PetalFoldingVerification.Replay(Sheet);Check(Sheet.Undo()&&ReferenceEquals(source,Sheet.Faces),"book undo");cases++;
  }
  Open(ThreeFaces());Call("PickSurfacePoint",Screen(new Vector2(5.5f,0)));Call("PickSurfacePoint",Screen(new Vector2(5.5f,11)));Call("ToggleFacePicking");Call("PickFaceAt",Screen(new Vector2(3,5.5f)));Check(Get("turn")==null&&Get("surfaceChoice")!=null,"explicit single-face selection silently expanded to all visible faces");cases++;
  var root=Environment.GetEnvironmentVariable("FLYEDITOR_VISIBLE_SIDE_ROOT");if(!string.IsNullOrEmpty(root)){Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"three-face-seed.json"),JsonUtility.ToJson(new ResearchSave{paperFolds=ThreeFaces().Recipe()},true));}
  Debug.Log("[VisibleSide] PASS "+cases+" cases / "+poses+" poses: blue/white click equivalence, 4 rotations, reversed endpoints, ortho/perspective; visible/whole and both book views, exact moving highlight, hidden ply fixed, contacts/seams/JSON/replay/undo; V individual retained.");
 }
 static void Save(string path){var camera=(Camera)Get("camera");camera.Render();var rt=(RenderTexture)Get("target");var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=old;}
 static void Probe(){var root=Environment.GetEnvironmentVariable("FLYEDITOR_VISIBLE_SIDE_ROOT");if(string.IsNullOrEmpty(root)||string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLYEDITOR_VISIBLE_SIDE_RECIPE")))return;Directory.CreateDirectory(root);var all=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_VISIBLE_SIDE_RECIPE"))).paperFolds;
  for(int stage=4;stage<=all.Length;stage++)foreach(float yaw in new[]{0f,45f,90f,135f}){var recipe=new FoldStep[stage];Array.Copy(all,recipe,stage);Call("Open",recipe);Call("Layout");Call("SnapView",7,false);type.GetField("yaw",H).SetValue(desk,yaw);Call("PositionCamera");Save(Path.Combine(root,"seed-"+stage+"-"+yaw+".png"));File.WriteAllText(Path.Combine(root,"seed-"+stage+".json"),JsonUtility.ToJson(new ResearchSave{paperFolds=recipe},true));}
 }
 public static void ProbeRun(){try{Init();try{Probe();}finally{Dispose();}EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
 public static void Verify(){Init();try{Probe();Test();}finally{Dispose();}}
 public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}



