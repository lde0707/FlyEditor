using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class VisibleSidePlayerVerification {
 const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 [Serializable]public class Origin {public string selectionOrigin;}
 static string root;static string At(string file)=>Path.Combine(root,"session-a",file);
 static ThreePlayerFlowVerification.Geometry Read(int id)=>JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(At("geometry-"+id.ToString("000")+".json")));
 static string Kind(int id)=>JsonUtility.FromJson<Origin>(File.ReadAllText(At("geometry-"+id.ToString("000")+".json"))).selectionOrigin;
 static void Check(bool ok,string message){if(!ok)throw new Exception("[VisibleSidePlayer] "+message);}
 static float Area(ThreePlayerFlowVerification.Face[] faces){double a=0;foreach(var f in faces){double sum=0;for(int i=0;i<f.points.Length;i++){var p=f.points[i];var q=f.points[(i+1)%f.points.Length];sum+=(double)p.x*q.y-(double)p.y*q.x;}a+=Math.Abs(sum)*.5;}return (float)a;}
 static void Green(int id){var g=Read(id);float actual=Area(g.moving);Check(g.moving.Length==2&&Mathf.Abs(actual-48.8125f)<.04f,"visible left blue + white material not selected "+id+" area="+actual);
  float area=0;for(int i=0;i<g.meshIndices.Length;i+=3){int a=g.meshIndices[i],b=g.meshIndices[i+1],c=g.meshIndices[i+2];var ink=g.meshColors[a];if(Mathf.Abs(ink.r-.62f)>.001f||Mathf.Abs(ink.g-.83f)>.001f||Mathf.Abs(ink.b-.73f)>.001f)continue;var cross=Vector3.Cross(g.meshVertices[b]-g.meshVertices[a],g.meshVertices[c]-g.meshVertices[a]);if(cross.sqrMagnitude<1e-10f||Mathf.Abs(cross.normalized.y)<.99f)continue;area+=cross.magnitude*.5f;}
  Check(Mathf.Abs(area-2*actual)<.01f,"actual green highlight differs from actual moving area "+id);
 }
 public static void Verify(){root=Environment.GetEnvironmentVariable("FLYEDITOR_VISIBLE_SIDE_ROOT");Check(!string.IsNullOrEmpty(root),"input evidence absent");
  foreach(int id in new[]{2,3,5,6,14})Green(id);
  var equal=typeof(FragmentBranchVerification).GetMethod("Equal",H);
  equal.Invoke(null,new object[]{Read(3).moving,Read(6).moving,Matrix4x4.identity});equal.Invoke(null,new object[]{Read(3).stationary,Read(6).stationary,Matrix4x4.identity});
  Check(Kind(3)=="Side"&&Kind(6)=="Side"&&Kind(11)=="Individual"&&Kind(14)=="Box","selection modes became ambiguous");Check(Read(11).moving.Length==0,"explicit individual click silently selected all visible faces");
  typeof(FragmentBranchVerification).GetMethod("PlayerTransition",H).Invoke(null,new object[]{root,"a",6,8});
  var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("recipe-008.json"))).paperFolds;Check(recipe.Length==3&&recipe[2].degrees==26&&recipe[2].selectedFaces.Length==2,"actual all-visible fold not recorded");
  var saved=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("progress-009.json"))).paperFolds;Check(saved.Length==3&&saved[2].selectedFaces.Length==2,"isolated save lost visible region");
  foreach(int id in new[]{4,10,12,15})equal.Invoke(null,new object[]{Read(1).sheet,Read(id).sheet,Matrix4x4.identity});
  for(int id=1;id<=16;id++){var steps=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("recipe-"+id.ToString("000")+".json"))).paperFolds;var paper=PaperSheet.Replay(steps);Check(paper.Count==steps.Length,"recorded state replay reset "+id);FlyEditor.Editor.ReverseDirectionVerification.Geometry(paper.Faces);}
  Debug.Log("[VisibleSidePlayer] PASS 16 actual-input captures: oblique cross-panel line, blue/white hover and click select identical regions, exact rendered green/moving material, 26 degree fold with fixed other side, V individual/box preserved, undo/cancel, isolated save and all replays. Similar multi-panel reproduction, not the exact unsaved attachment state.");
 }
 public static void Run(){try{VisibleSideSelectionVerification.Verify();BoxSelectionVerification.Verify();BlenderFoldViewVerification.Verify();WholeSheetFoldVerification.Verify();Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
