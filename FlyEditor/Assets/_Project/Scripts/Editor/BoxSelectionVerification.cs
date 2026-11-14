using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class BoxSelectionVerification {
 const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static Type type;static object desk;static GameObject go;static Material material;static int cases,poses;
 static void Check(bool value,string message){if(!value)throw new Exception("[BoxSelection] "+message);}
 static object Get(string name)=>type.GetField(name,H).GetValue(desk);
 static object Call(string name,params object[] args){foreach(var m in type.GetMethods(H)){if(m.Name!=name||m.GetParameters().Length!=args.Length)continue;var p=m.GetParameters();bool match=true;for(int i=0;i<args.Length;i++)if(args[i]!=null&&!p[i].ParameterType.IsInstanceOfType(args[i]))match=false;if(match)return m.Invoke(desk,args);}throw new Exception(name);}
 static void Set(string name,object value)=>type.GetField(name,H).SetValue(desk,value);
 static float Area(IReadOnlyList<PaperSheet.Face> faces){float result=0;foreach(var f in faces)result+=PaperSheet.Area(f.points);return result;}
 static Vector2 R(Vector2 p,int r){while(r-->0)p=new Vector2(11-p.y,p.x);return p;}
 static Vector2 N(Vector2 p,int r){while(r-->0)p=new Vector2(-p.y,p.x);return p;}
 static FoldStep Line(Vector2 n,float d,int side,int r=0){var nr=N(n,r);return new FoldStep{creaseNormal=nr,creaseOffset=d+Vector2.Dot(nr-n,Vector2.one*5.5f),side=side,continuousHinge=true,neutralContacts=true,compactLayers=true,materialPartition=true,vertexJoints=true,preserveSlivers=true};}
 static Vector2 Screen(PaperSheet s,Vector2 p,bool back=false){PaperSheet.Face visible=null;foreach(var f in s.Faces)if(f.Contains(p)&&(visible==null||(back?f.layer<visible.layer:f.layer>visible.layer)))visible=f;Check(visible!=null,"screen point outside paper");return (Vector2)Call("SurfaceScreen",visible,p);}
 static void Open(PaperSheet s,bool back=false){Call("Open",s.Recipe());Call("Layout");Call("SnapView",7,back);}
 static PaperSheet Sheet=>(PaperSheet)Get("sheet");
 static void Axis(Vector2 a,Vector2 b,bool back=false){Call("PickSurfacePoint",Screen(Sheet,a,back));Call("PickSurfacePoint",Screen(Sheet,b,back));Check((int)Get("second")>=0,"axis absent");}
 static void Mode(bool stack){Call("ChoosePly",Enum.Parse(type.GetNestedType("Ply",BindingFlags.NonPublic),stack?"Stack":"Top"));if(Get("pickMode").ToString()=="Face")Call("ToggleFacePicking");Call("ToggleFacePicking");Check(Get("pickMode").ToString()=="Face","V did not enable face mode");Check(Get("scope").ToString()==(stack?"Stack":"Top"),"V lost requested scope");}
 static void Mesh(PaperSheet.Turn t){typeof(SelectionRegionVerification).GetMethod("MeshMatches",H).Invoke(null,new object[]{Sheet,t,-1,null});}
 static void Fixed(PaperSheet.Face source,IReadOnlyList<PaperSheet.Face> after){
  for(int x=1;x<5;x++)for(int y=1;y<5;y++){Vector2 centre=Vector2.zero;foreach(var q in source.points)centre+=q;centre/=source.points.Length;var p=Vector2.Lerp(centre,source.points[(x+y)%source.points.Length],.1f*x);var m=source.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));bool found=false;
   foreach(var f in after){var q=f.material.inverse.MultiplyPoint3x4(m);var uv=new Vector2(q.x,q.z);if(!f.Contains(uv))continue;Check(Vector3.Distance(source.World(p,0),f.World(uv,0))<.002f,"hidden unselected ply moved");found=true;}Check(found,"stationary material lost");}
 }
 static void Book(){foreach(bool back in new[]{false,true})foreach(bool whole in new[]{false,true})for(int r=0;r<4;r++)foreach(bool reverse in new[]{false,true}){
  var seed=new PaperSheet();Check(seed.Finish(seed.Plan(Line(Vector2.up,5.5f,1,r)),180),"book seed");Open(seed,back);var source=Sheet.Faces;
  Axis(R(new Vector2(0,2.75f),r),R(new Vector2(11,2.75f),r),back);Mode(whole);
  // Both drag directions remain on the same crease side.
  var start=Screen(Sheet,R(new Vector2(.01f,.01f),r),back);var end=Screen(Sheet,R(new Vector2(10.99f,2.65f),r),back);
  Call("SelectBox",reverse?end:start,reverse?start:end);var t=Get("turn") as PaperSheet.Turn;
  Check(t!=null,"book box rejected "+back+"/"+whole+"/"+r+": "+Get("notice"));Check(t.step.selectedFaces.Length==(whole?2:1),"occluded face inclusion");
  Check(Mathf.Abs(Area(t.movingFaces)-(whole?60.5f:30.25f))<.002f,"box moved incorrect crease-side area");Mesh(t);
  int sign=whole?1:back?-1:1;var preview=new List<PaperSheet.Face>();var previewMethod=typeof(PaperSheet).GetMethod("TryPreviewTurn",H);
  PaperSheet.Face hidden=null;if(!whole)foreach(var f in source)if(!Array.Exists(t.step.selectedFaces,index=>ReferenceEquals(source[index],f)))hidden=f;
  foreach(float angle in new[]{0f,3f,30f,90f,150f,180f}){Check((bool)previewMethod.Invoke(Sheet,new object[]{t,sign*angle,preview}),"book pose "+angle);ReverseDirectionVerification.Geometry(preview);if(hidden!=null)Fixed(hidden,preview);poses++;}
  if(!whole)Check(t.Constrain(-sign*30)==0,"hidden stack collision bypassed");
  Check(Sheet.Finish(t,sign*90),"box commit");if(hidden!=null)Fixed(hidden,Sheet.Faces);
  var json=JsonUtility.ToJson(new ResearchSave{paperFolds=Sheet.Recipe()});var replay=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(json).paperFolds);Check(replay.Count==2,"selected owners lost on JSON/replay");PetalFoldingVerification.Replay(Sheet);ReverseDirectionVerification.Geometry(replay.Faces);
  Check(Sheet.Undo()&&ReferenceEquals(source,Sheet.Faces),"box undo");cases++;
 }}
 static void Multiple(){foreach(bool whole in new[]{false,true}){
  var s=new PaperSheet();Check(s.Finish(s.Plan(Line(Vector2.right,2,-1)),180),"left flap");Check(s.Finish(s.Plan(Line(Vector2.right,9,1)),180),"right flap");Open(s);Axis(new Vector2(2,5.5f),new Vector2(9,5.5f));Mode(whole);
  Call("SelectBox",Screen(Sheet,new Vector2(2.01f,.01f)),Screen(Sheet,new Vector2(8.99f,5.4f)));var t=Get("turn") as PaperSheet.Turn;
  Check(t!=null&&t.step.selectedFaces.Length==3,"three visible connected faces not selected: "+Get("notice"));Check(Mathf.Abs(Area(t.movingFaces)-60.5f)<.01f,"multiple face motion");Mesh(t);Check(Sheet.Finish(t,90),"three faces could not fold");ReverseDirectionVerification.Geometry(Sheet.Faces);PetalFoldingVerification.Replay(Sheet);cases++;
 }}
 static void BoundsAndRejection(){
  var s=new PaperSheet();Check(s.Finish(s.Plan(Line(Vector2.up,5.5f,1)),180),"rejection seed");Open(s);Axis(new Vector2(5.5f,0),new Vector2(5.5f,5.5f));Mode(false);
  Call("SelectBox",Screen(Sheet,new Vector2(5.6f,.1f)),Screen(Sheet,new Vector2(10.9f,5.4f)));Check(Get("turn")==null,"unselected lower joint was silently carried");Check(Sheet.Count==1,"invalid selection changed paper");
  Mode(true);Call("SelectBox",Screen(Sheet,new Vector2(5.6f,.1f)),Screen(Sheet,new Vector2(10.9f,5.4f)));Check(Get("turn")!=null,"whole joint closure rejected");
  Call("Browse");Check(Get("scope").ToString()=="Top"&&Get("turn")==null,"cancel left stale selection");
  Axis(new Vector2(0,2.75f),new Vector2(11,2.75f));Mode(false);var viewport=(Rect)Get("viewport");Call("SelectBox",viewport.min+Vector2.one,viewport.min+Vector2.one*8);Check(Get("turn")==null&&Sheet.Count==1,"empty box retained previous movement");cases+=3;
 }
 static void TiltedAssembly(){
  var s=new PaperSheet();Check(s.Finish(s.Plan(Line(Vector2.up,2,-1)),60),"tilted seed");Open(s);Axis(new Vector2(0,5.5f),new Vector2(11,5.5f));Mode(false);
  var basis=Sheet.Faces[0];var start=(Vector2)Call("SurfaceScreen",basis,new Vector2(.01f,3));var end=(Vector2)Call("SurfaceScreen",basis,new Vector2(10.99f,-2));
  Call("SelectBox",start,end);var turn=Get("turn") as PaperSheet.Turn;
  Check(turn!=null&&turn.step.selectedFaces.Length==2,"connected visible tilted assembly could not be box-selected: "+Get("notice"));
  Check(Mathf.Abs(Area(turn.movingFaces)-60.5f)<.01f,"tilted selection omitted or enlarged moving material");
  var next=new List<PaperSheet.Face>();foreach(float angle in new[]{0f,3f,30f,60f}){Check((bool)typeof(PaperSheet).GetMethod("TryPreviewTurn",H).Invoke(Sheet,new object[]{turn,angle,next}),"tilted assembly sweep");ReverseDirectionVerification.Geometry(next);poses++;}
  Check(Sheet.Finish(turn,60),"tilted assembly commit");PetalFoldingVerification.Replay(Sheet);cases++;
 }
 static void Visibility(){
  var s=new PaperSheet();Check(s.Finish(s.Plan(Line(Vector2.right,1,-1)),180),"occlusion seed");Open(s);
  var select=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperBoxSelection").GetMethod("Collect",H);
  var result=new List<int>();var line=Line(Vector2.up,5.5f,-1);var camera=(Camera)Get("camera");var table=(Transform)Get("table");var viewport=(Rect)Get("viewport");
  void Collect(float left,float right,bool whole){var a=Screen(Sheet,new Vector2(left,1));var b=Screen(Sheet,new Vector2(right,4));var box=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));select.Invoke(null,new object[]{Sheet,camera,table.position,viewport,box,line,whole,result});}
  Collect(1.1f,1.9f,false);Check(result.Count==1&&Sheet.Faces[result[0]].layer>0,"fully hidden source picked");
  Collect(1.1f,1.9f,true);Check(result.Count==2,"whole scope omitted occluded source");
  Collect(1.999f,2.001f,false);Check(result.Count==2,"thin exposed strip was missed by visibility selection");
  // A scope toggle inside V must keep the box tool active.
  Call("Browse");Axis(new Vector2(1,5.5f),new Vector2(11,5.5f));Call("ToggleFacePicking");Call("ChoosePly",Enum.Parse(type.GetNestedType("Ply",BindingFlags.NonPublic),"Stack"));
  Check(Get("pickMode").ToString()=="Face","switching to whole scope left V box-selection mode");cases+=4;
 }
 public static void Prepare(){try{Directory.CreateDirectory(Environment.GetEnvironmentVariable("FLYEDITOR_BOX_ROOT"));var s=new PaperSheet();Check(s.Finish(s.Plan(Line(Vector2.up,5.5f,1)),180),"player seed");File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("FLYEDITOR_BOX_ROOT"),"book-seed.json"),JsonUtility.ToJson(new ResearchSave{paperFolds=s.Recipe()},true));EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
 public static void Verify(){try{
  type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldingDesk");Check(type.GetMethod("SelectBox",H)!=null,"V drag must select multiple crease-side faces instead of rotating the first clicked face");
  go=new GameObject("box-selection-check");material=new Material(Shader.Find("Unlit/Color"));desk=Activator.CreateInstance(type,H,null,new object[]{go.AddComponent<PaperFlightController>(),material},null);
  TiltedAssembly();Visibility();Book();Multiple();BoundsAndRejection();
  WingSideVerification.Run();WingSideVerification.RunFlat();
  var root=Environment.GetEnvironmentVariable("FLYEDITOR_BOX_ROOT");if(!string.IsNullOrEmpty(root)){Directory.CreateDirectory(root);var seed=new PaperSheet();Check(seed.Finish(seed.Plan(Line(Vector2.up,5.5f,1)),180),"player seed");File.WriteAllText(Path.Combine(root,"book-seed.json"),JsonUtility.ToJson(new ResearchSave{paperFolds=seed.Recipe()},true));}
  Debug.Log("[BoxSelection] PASS "+cases+" cases; "+poses+" poses; visible/whole, both views, 4 rotations, reverse drag, multiple faces, exact green area, hidden material fixed, contacts, seams, JSON/replay/undo, cancel and empty.");
 }finally{if(desk!=null)Call("Dispose");if(material!=null)UnityEngine.Object.DestroyImmediate(material);if(go!=null)UnityEngine.Object.DestroyImmediate(go);}}
 public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}

