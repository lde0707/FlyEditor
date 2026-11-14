using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class BoxSelectionPlayerVerification
{
 const BindingFlags H=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static string root;
 static void Check(bool ok,string message){if(!ok)throw new Exception("[BoxSelectionPlayer] "+message);}
 static string At(string s,string file)=>Path.Combine(root,"session-"+s,file);
 static string Status(string s,int id)=>File.ReadAllText(At(s,"status-"+id.ToString("000")+".txt"));
 static FoldStep[] Recipe(string s,int id)=>JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At(s,"recipe-"+id.ToString("000")+".json"))).paperFolds;
 static ThreePlayerFlowVerification.Geometry Geometry(string s,int id)=>JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(At(s,"geometry-"+id.ToString("000")+".json")));
 static float Area(ThreePlayerFlowVerification.Face[] faces){float sum=0;foreach(var f in faces){double a=0;for(int i=0;i<f.points.Length;i++){var p=f.points[i];var q=f.points[(i+1)%f.points.Length];a+=(double)p.x*q.y-(double)p.y*q.x;}sum+=(float)(Math.Abs(a)*.5);}return sum;}
 static void Green(string s,int id,float expected){var g=Geometry(s,id);float green=0;
  for(int i=0;i<g.meshIndices.Length;i+=3){int a=g.meshIndices[i],b=g.meshIndices[i+1],c=g.meshIndices[i+2];var ink=g.meshColors[a];if(Mathf.Abs(ink.r-.62f)>.001f||Mathf.Abs(ink.g-.83f)>.001f||Mathf.Abs(ink.b-.73f)>.001f)continue;
   var cross=Vector3.Cross(g.meshVertices[b]-g.meshVertices[a],g.meshVertices[c]-g.meshVertices[a]);if(cross.sqrMagnitude<1e-10f||Mathf.Abs(cross.normalized.y)<.99f)continue;green+=cross.magnitude*.5f;}
  Check(Mathf.Abs(Area(g.moving)-expected)<.002f,"actual planned moving area "+s+"/"+id);Check(Mathf.Abs(green-2*expected)<.01f,"actual rendered green area differs from moving material "+s+"/"+id);
 }
 static void Transition(string s,int start,int end)=>typeof(FragmentBranchVerification).GetMethod("PlayerTransition",H).Invoke(null,new object[]{root,s,start,end});
 public static void Verify(){root=Environment.GetEnvironmentVariable("FLYEDITOR_BOX_PLAYER_ROOT");Check(!string.IsNullOrEmpty(root),"evidence root missing");
  Check(Status("a",2).Contains("gesture=BoxSelect")&&Status("a",7).Contains("gesture=BoxSelect"),"V drags did not enter box FSM");
  Check(Status("a",3).Contains("scope=Top")&&Status("a",8).Contains("scope=Stack"),"scope switch failed through actual keys");
  Green("a",3,30.25f);Green("a",8,60.5f);Green("b",3,60.5f);
  Check(Recipe("a",5).Length==2&&Recipe("a",5)[1].selectedFaces.Length==1,"visible box owner recipe");
  Check(Recipe("a",10).Length==2&&Recipe("a",10)[1].selectedFaces.Length==2,"whole box owner recipe");
  Check(Recipe("b",5).Length==3&&Recipe("b",5)[2].selectedFaces.Length==3,"three visible faces recipe");
  Check(Recipe("a",5)[1].degrees==26&&Recipe("a",10)[1].degrees==26&&Recipe("b",5)[2].degrees==42,"drag did not adjust the selected box turn");
  Transition("a",3,5);Transition("a",8,10);Transition("b",3,5);
  Check(Status("a",10).Contains("scope=Top")&&Status("a",12).Contains("phase=FirstPoint")&&Recipe("a",12).Length==1,"finish/cancel did not reset safely");
  var equal=typeof(FragmentBranchVerification).GetMethod("Equal",H);equal.Invoke(null,new object[]{Geometry("a",6).sheet,Geometry("a",12).sheet,Matrix4x4.identity});
  int states=0;foreach(var pair in new[]{("a",13),("b",7)})for(int i=1;i<=pair.Item2;i++){var recipe=Recipe(pair.Item1,i);var paper=PaperSheet.Replay(recipe);Check(paper.Count==recipe.Length,"actual-input replay reset "+pair.Item1+"/"+i);FlyEditor.Editor.ReverseDirectionVerification.Geometry(paper.Faces);states++;}
  var savedA=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("a","progress-011.json"))).paperFolds;
  var savedB=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(At("b","progress-006.json"))).paperFolds;
  Check(savedA.Length==2&&savedA[1].selectedFaces.Length==2&&savedB.Length==3&&savedB[2].selectedFaces.Length==3,"disk save lost box scope");
  Debug.Log("[BoxSelectionPlayer] PASS 3 real mouse/key folds; visible top only, whole hidden ply, three visible faces; exact rendered green/moving material, independent fixed/moving geometry, drag preserved selection, Esc, Z, isolated disk saves, "+states+" replays. Initial states are generated seeds, not full models folded from blank by input.");
 }
 public static void Run(){try{BoxSelectionVerification.Verify();Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
