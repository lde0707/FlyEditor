using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using FlyEditor.Flight;
using FlyEditor.Editor;
public static class SelectionRegionVerification {
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool ok,string message){if(!ok)throw new Exception("[SelectionRegion] "+message);}
 static Vector3 Material(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
 static void Fixed(PaperSheet.Face source,IReadOnlyList<PaperSheet.Face> after){
  foreach(var p in source.points){var m=Material(source,p);bool found=false;
   foreach(var f in after){var q=f.material.inverse.MultiplyPoint3x4(m);var uv=new Vector2(q.x,q.z);if(!f.Contains(uv))continue;
    Check(Vector3.Distance(f.World(uv,0),source.World(p,0))<.002f,"unselected layer moved");found=true;}
   Check(found,"unselected material lost");
  }
 }
 static float Area(IReadOnlyList<PaperSheet.Face> faces){float a=0;foreach(var f in faces)a+=PaperSheet.Area(f.points);return a;}
 static void MeshMatches(PaperSheet s,PaperSheet.Turn turn,int panel,PaperCornerPull pull=null){
  var type=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.FoldMesh");var builder=Activator.CreateInstance(type,true);
  var build=type.GetMethod("Build");var parameters=build.GetParameters();var args=new object[parameters.Length];
  for(int i=0;i<args.Length;i++)args[i]=parameters[i].DefaultValue;
  var mesh=new Mesh();args[0]=mesh;args[1]=s;args[2]=turn;args[3]=0f;
  args[4]=new List<PaperSheet.FoldVertex>{new PaperSheet.FoldVertex(0,Vector2.zero),new PaperSheet.FoldVertex(0,Vector2.one)};
  for(int i=0;i<args.Length;i++){
   switch(parameters[i].Name){case "first":args[i]=0;break;case "second":args[i]=1;break;case "selectedPanel":args[i]=panel;break;
    case "viewDirection":args[i]=Vector3.down;break;case "visiblePoints":args[i]=new[]{false,false};break;case "pull":args[i]=pull;break;}
  }
  build.Invoke(builder,args);float green=0;var v=mesh.vertices;var c=mesh.colors;var ids=mesh.triangles;
  for(int i=0;i<ids.Length;i+=3){var color=c[ids[i]];if(Mathf.Abs(color.r-.62f)>.001f||Mathf.Abs(color.g-.83f)>.001f||Mathf.Abs(color.b-.73f)>.001f)continue;
   var a=v[ids[i]];var b=v[ids[i+1]];var d=v[ids[i+2]];var cross=Vector3.Cross(b-a,d-a);
   if(cross.sqrMagnitude<1e-10f||Mathf.Abs(cross.normalized.y)<.99f)continue;
   green+=cross.magnitude*.5f;
   if(turn!=null){var centre=(a+b+d)/3;bool inside=false;
    foreach(var f in turn.movingFaces){var local=f.frame.inverse.MultiplyPoint3x4(centre);
     if(local.y<f.layer*PaperSheet.Thickness-.001f||local.y>(f.layer+1)*PaperSheet.Thickness+.001f)continue;
     inside|=f.Contains(new Vector2(local.x,local.z));}
    Check(inside,"green surface outside actual moving region");}
  }
  Check(Mathf.Abs(green-(turn==null?0:2*Area(turn.movingFaces)))<.01f,"green area does not equal moving surfaces: "+green);
  UnityEngine.Object.DestroyImmediate(mesh);
 }
 public static void Run(){try{
  int cases=0,poses=0;
  // A line on one panel: pinned ownership must not tint its stationary half.
  foreach(int side in new[]{-1,1}){
   var s=new PaperSheet();var line=new FoldStep{anchor=0,flap=1,singlePanel=true,connectedPanel=true,separateFoldSides=true,
    continuousHinge=true,neutralContacts=true,creaseNormal=Vector2.right,creaseOffset=5.5f,side=side,contact=new Vector2(side>0?8:3,5)};
   var t=s.Plan(line);Check(t!=null,"flat split");foreach(int pinned in new[]{-1,0})MeshMatches(s,t,pinned);MeshMatches(s,null,0);cases++;
  }
  foreach(bool upper in new[]{false,true}){
   var s=new PaperSheet();Check(s.ApplyStep(new FoldStep{creaseNormal=Vector2.up,creaseOffset=5.5f,side=1,degrees=180,continuousHinge=true,neutralContacts=true,compactLayers=true}),"book");
   int selected=0;for(int i=1;i<s.Faces.Count;i++)if(upper?s.Faces[i].layer>s.Faces[selected].layer:s.Faces[i].layer<s.Faces[selected].layer)selected=i;
   var lower=s.Faces[1-selected];var before=s.Faces;
   var line=new FoldStep{anchor=selected,flap=selected+1,singlePanel=true,connectedPanel=true,separateFoldSides=true,continuousHinge=true,neutralContacts=true,
    creaseNormal=Vector2.up,creaseOffset=4.5f,side=-1,contact=new Vector2(5,2)};
   var t=s.Plan(line);Check(t!=null,"stack selection");MeshMatches(s,t,selected);
   var preview=new List<PaperSheet.Face>();var method=typeof(PaperSheet).GetMethod("TryPreviewTurn",Hidden);int sign=upper?1:-1;
   for(int a=0;a<=180;a+=3){Check((bool)method.Invoke(s,new object[]{t,sign*(float)a,preview}),"stack sweep "+upper+"/"+a);Fixed(lower,preview);ReverseDirectionVerification.Geometry(preview);poses++;}
   Check(t.Constrain(-sign*30)==0,"contact guard bypassed");Check(s.Finish(t,sign*180),"stack commit");Fixed(lower,s.Faces);
   PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(before,s.Faces),"undo");cases++;
  }
  // The actual four-click user-derived state, before/after entering angle mode.
  var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;
  var paper=new PaperSheet();for(int i=0;i<recipe.Length-1;i++)Check(paper.ApplyStep(recipe[i]),"recipe "+i);
  var step=recipe[recipe.Length-1];step.separateFoldSides=true;var plan=PaperCornerPull.Create(paper,step);Check(plan!=null&&plan.UsesEdgeMatch&&plan.Pose(0),"four-point plan");
  var edge=(PaperSheet.Turn)typeof(PaperCornerPull).GetProperty("EdgeTurn",Hidden).GetValue(plan);
  MeshMatches(paper,edge,step.anchor);MeshMatches(paper,edge,step.anchor,plan);cases++;
  WingSideVerification.Run();WingSideVerification.RunFlat();JetEdgeVerification.Run();
  Debug.Log("[SelectionRegion] PASS "+cases+" region cases, "+poses+" stack poses: exact green moving area, fixed opposite layer, seams/area, collision, replay, undo, four-point preview/drag consistency.");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
