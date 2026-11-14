using System;using System.Reflection;using UnityEditor;using UnityEngine;using UnityEngine.InputSystem;using FlyEditor.Flight;
public static class BlenderFoldViewVerification {
 const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static object desk;static Type type;static GameObject go;static Material material;
 static void Check(bool ok,string text){if(!ok)throw new Exception("[BlenderFoldView] "+text);}
 static object Get(string name)=>type.GetField(name,H).GetValue(desk);
 static object Call(string name,params object[] args){foreach(var m in type.GetMethods(H)){if(m.Name!=name||m.GetParameters().Length!=args.Length)continue;return m.Invoke(desk,args);}throw new Exception("Missing navigation command: "+name);}
 static Camera Cam=>(Camera)Get("camera");
 static void Press(Key k,bool ctrl=false,bool shift=false)=>Call("NavigateView",k,ctrl,shift);
 static void Direction(Vector3 forward,Vector3 up){Check(Vector3.Dot(Cam.transform.forward,forward)>.99998f,"wrong forward "+Cam.transform.forward+" expected "+forward);Check(Vector3.Dot(Cam.transform.up,up)>.99998f,"wrong screen up "+Cam.transform.up+" expected "+up);}
 static void PerspectiveSelection(){
  var book=new PaperSheet();Check(book.ApplyStep(new FoldStep{creaseNormal=Vector2.up,creaseOffset=5.5f,side=1,degrees=180,continuousHinge=true,neutralContacts=true,compactLayers=true,materialPartition=true,vertexJoints=true,preserveSlivers=true}),"perspective book seed");
  foreach(bool oblique in new[]{false,true})foreach(bool stack in new[]{false,true}){
   Call("Open",book.Recipe());Call("Layout");Press(Key.Numpad7);Press(Key.Numpad5);if(oblique)Press(Key.Numpad2);
   var sheet=(PaperSheet)Get("sheet");var top=sheet.Faces[1];
   Vector2 Screen(Vector2 p)=>(Vector2)Call("SurfaceScreen",top,p);
   Call("PickSurfacePoint",Screen(new Vector2(0,2.75f)));Call("PickSurfacePoint",Screen(new Vector2(11,2.75f)));
   if(stack)Call("ChoosePly",Enum.Parse(type.GetNestedType("Ply",BindingFlags.NonPublic),"Stack"));
   Call("ToggleFacePicking");Call("SelectBox",Screen(new Vector2(1,.4f)),Screen(new Vector2(10,2.4f)));
   var turn=Get("turn") as PaperSheet.Turn;Check(turn!=null,"perspective box rejected: "+Get("notice"));Check(turn.step.selectedFaces.Length==(stack?2:1),"perspective selected a hidden ply");
   float area=0;foreach(var f in turn.movingFaces)area+=PaperSheet.Area(f.points);Check(Mathf.Abs(area-(stack?60.5f:30.25f))<.01f,"perspective moving area mismatch");
   Check(sheet.Finish(turn,45),"perspective selected fold blocked");FlyEditor.Editor.ReverseDirectionVerification.Geometry(sheet.Faces);FlyEditor.Editor.PetalFoldingVerification.Replay(sheet);
  }
  // A near-plane cut must not project the portion behind the eye across the screen.
  Call("Open",book.Recipe());Call("Layout");Press(Key.Numpad7);Press(Key.Numpad5);
  var paper=(PaperSheet)Get("sheet");var table=(Transform)Get("table");Cam.transform.SetPositionAndRotation(table.position+new Vector3(5.5f,.08f,3.5f),Quaternion.Euler(10,0,0));
  var viewport=(Rect)Get("viewport");var box=new Rect(viewport.center-viewport.size*.1f,viewport.size*.2f);var selected=new System.Collections.Generic.List<int>();
  var collect=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperBoxSelection").GetMethod("Collect",H);
  var line=new FoldStep{creaseNormal=Vector2.up,creaseOffset=2.75f,side=1};
  collect.Invoke(null,new object[]{paper,Cam,table.position,viewport,box,line,false,selected});Check(selected.Count==1&&selected[0]==1,"near-plane occlusion selected hidden/behind-eye material: "+string.Join(",",selected));
  collect.Invoke(null,new object[]{paper,Cam,table.position,viewport,box,line,true,selected});Check(selected.Count==2,"near-plane whole selection lost a visible projected ply");
  Debug.Log("[BlenderFoldView] PASS 4 perspective selection/fold cases and near-plane clipping; visible/whole, angled camera, hidden material, seams and replay.");
 }
 public static void Verify(){
  type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");go=new GameObject("blender-view-check");material=new Material(Shader.Find("Unlit/Color"));desk=Activator.CreateInstance(type,H,null,new object[]{go.AddComponent<PaperFlightController>(),material},null);
  try{Call("Open",Array.Empty<FoldStep>());Call("Layout");
   // This behavior is absent in the old always-orthographic keypad path.
   Press(Key.Numpad5);Check(!Cam.orthographic,"Numpad5 did not enter perspective");Press(Key.Numpad5);Check(Cam.orthographic,"Numpad5 did not restore ortho");
   foreach(int axis in new[]{1,3,7})foreach(bool opposite in new[]{false,true}){
    Press(Key.Numpad5);Press(axis==1?Key.Numpad1:axis==3?Key.Numpad3:Key.Numpad7,opposite);Check(Cam.orthographic,"axis view did not enforce orthographic");
    var forward=axis==1?Vector3.forward:axis==3?Vector3.left:Vector3.down;if(opposite)forward=-forward;
    var up=axis==7?(opposite?Vector3.back:Vector3.forward):Vector3.up;Direction(forward,up);
    Press(Key.Numpad9);Direction(-forward,axis==7?-up:up);Press(Key.Numpad9);Direction(forward,up);
   }
   Press(Key.Numpad7);Press(Key.Numpad5);Press(Key.Numpad9);Check(!Cam.orthographic,"opposite axis view lost perspective");Direction(Vector3.up,Vector3.back);
   Press(Key.Numpad1);Press(Key.Numpad6);Check(!Cam.orthographic,"axis orbit did not use Blender auto-perspective");Direction(new Vector3(-.258819f,0,.965926f),Vector3.up);
   Press(Key.Numpad4);Direction(Vector3.forward,Vector3.up);Press(Key.Numpad8);Direction(new Vector3(0,-.258819f,.965926f),new Vector3(0,.965926f,.258819f));
   float height=Cam.transform.forward.y;Press(Key.Numpad9);Check(Mathf.Abs(Cam.transform.forward.y-height)<.00001f,"user opposite view inverted elevation");Press(Key.Numpad2);Direction(Vector3.back,Vector3.up);
   Press(Key.Numpad1);var before=Cam.transform.rotation;var pivot=(Vector3)Get("panOffset");Press(Key.Numpad6,true);Check(Quaternion.Angle(before,Cam.transform.rotation)<.001f&&Vector3.Dot((Vector3)Get("panOffset")-pivot,Cam.transform.right)>0,"Ctrl+6 did not pan right");Press(Key.Numpad4,true);Check(((Vector3)Get("panOffset")-pivot).magnitude<.0001f,"opposite pans did not cancel");
   Press(Key.Numpad6,false,true);Check(Vector3.Dot(Cam.transform.forward,Vector3.forward)>.99999f&&Quaternion.Angle(before,Cam.transform.rotation)>14.99f,"Shift+6 did not roll");Press(Key.Numpad4,false,true);Direction(Vector3.forward,Vector3.up);
   float zoom=(float)Get("zoom");Press(Key.NumpadPlus);Check((float)Get("zoom")>zoom,"plus did not zoom in");Press(Key.NumpadMinus);Check(Mathf.Abs((float)Get("zoom")-zoom)<.00001f,"zoom inverse");
   Press(Key.Numpad7);var sheet=(PaperSheet)Get("sheet");var point=new Vector2(8.25f,8.25f);var screen=(Vector2)Call("SurfaceScreen",sheet.Faces[0],point);Call("PickSurfacePoint",screen);Press(Key.NumpadPeriod);
   var view=Cam.WorldToViewportPoint(((Transform)Get("table")).position+sheet.Faces[0].World(point,0));Check(Mathf.Abs(view.x-.5f)<.001f&&Mathf.Abs(view.y-.5f)<.001f,"period did not frame selected point");Check((float)Get("zoom")>1,"frame selected did not zoom");Press(Key.Home);Check(((Vector3)Get("panOffset")).sqrMagnitude<.00001f&&Mathf.Abs((float)Get("zoom")-1)<.0001f,"Home did not frame all");
   PerspectiveSelection();
   Debug.Log("[BlenderFoldView] PASS projection, six canonical views, axis/user opposites, correct orbit directions, auto-perspective, Ctrl pan, Shift roll, zoom and selected/all framing.");
  }finally{Call("Dispose");UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(go);}
 }
 public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}


