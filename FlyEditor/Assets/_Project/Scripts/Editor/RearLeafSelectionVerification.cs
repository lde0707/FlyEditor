using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class RearLeafSelectionVerification {
  static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  static void Check(bool ok,string message){if(!ok)throw new Exception("[RearLeaf] "+message);}
  static Vector3 Material(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
  static Vector2 Local(PaperSheet.Face f,Vector3 p){var q=f.material.inverse.MultiplyPoint3x4(p);return new Vector2(q.x,q.z);}
  static bool Owned(PaperSheet.Face f,List<PaperSheet.Face> pieces){Vector2 c=Vector2.zero;foreach(var q in f.points)c+=q;c/=f.points.Length;var m=Material(f,c);foreach(var g in pieces)if(g.Contains(Local(g,m)))return true;return false;}
  static int Fixed(List<PaperSheet.Face> protectedFaces,IReadOnlyList<PaperSheet.Face> after){int count=0;foreach(var f in protectedFaces)foreach(var p in f.points){var m=Material(f,p);bool found=false;foreach(var g in after){var q=Local(g,m);if(!g.Contains(q))continue;Check(Vector3.Distance(f.World(p,0),g.World(q,0))<.002f,"front leaf moved");found=true;}Check(found,"front leaf lost");count++;}return count;}
  public static void Run(){try{
   var root=Environment.GetEnvironmentVariable("FLYEDITOR_REAR_ROOT");var baseLine=JsonUtility.FromJson<FoldStep>(File.ReadAllText(root+"/repro-line.json"));int cases=0,poses=0,fallbacks=0,protectedChecks=0;
   foreach(string name in new[]{"repro-eight.json","front-eight.json"})foreach(float tilt in new[]{-3f,0f,3f})foreach(bool reverse in new[]{false,true}){
    var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/"+name)).paperFolds;var sheet=new PaperSheet();var front=new List<PaperSheet.Face>();
    for(int i=0;i<recipe.Length;i++){
     if(i>=6){PaperSheet.Turn tr;if(recipe[i].cornerPull){var p=PaperCornerPull.Create(sheet,recipe[i]);Check(p!=null,"front four-point plan");tr=(PaperSheet.Turn)typeof(PaperCornerPull).GetProperty("EdgeTurn",Hidden).GetValue(p);}else tr=sheet.Plan(recipe[i]);Check(tr!=null,"front plan");front.AddRange(tr.movingFaces);}
     Check(sheet.ApplyStep(recipe[i]),"seed "+i);
    }
    var before=sheet.Faces;var protectedFaces=new List<PaperSheet.Face>();foreach(var f in before)if(Owned(f,front))protectedFaces.Add(f);Check(protectedFaces.Count>0,"no front coverage");
    var line=baseLine;int anchor=-1;for(int i=0;i<before.Count;i++)if(before[i].Contains(line.contact)&&(anchor<0||before[i].layer<before[anchor].layer))anchor=i;Check(anchor>=0,"rear visible contact");line.anchor=anchor;line.flap=anchor+1;
    float angle=tilt*Mathf.Deg2Rad;var n=line.Normal;line.creaseNormal=new Vector2(n.x*Mathf.Cos(angle)-n.y*Mathf.Sin(angle),n.x*Mathf.Sin(angle)+n.y*Mathf.Cos(angle));line.creaseOffset=Vector2.Dot(line.creaseNormal,new Vector2(0,11));
    if(reverse){line.creaseNormal=-line.creaseNormal;line.creaseOffset=-line.creaseOffset;line.side=-line.side;}
    var legacy=(PaperSheet.Turn)typeof(PaperSheet).GetMethod("Plan",Hidden,null,new[]{typeof(FoldStep),typeof(bool)},null).Invoke(sheet,new object[]{line,false});if(legacy==null)fallbacks++;
    var turn=sheet.Plan(line);Check(turn!=null,"rear plan "+name+"/"+tilt+"/"+reverse);Check(!turn.MovesOtherPlyAt(line.contact),"wrong ply guard");
    foreach(var f in turn.movingFaces)Check(!Owned(f,front),"front flap in moving set");
    // The generated highlight must represent these exact moving pieces.
    typeof(SelectionRegionVerification).GetMethod("MeshMatches",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{sheet,turn,anchor,null});
    var preview=new List<PaperSheet.Face>();var method=typeof(PaperSheet).GetMethod("TryPreviewTurn",Hidden);
    Check(turn.Constrain(30)==0,"opposite direction passed through stack");Check(turn.Constrain(-180)==-180,"rear not fully foldable");
    for(int a=0;a<=180;a+=3){Check((bool)method.Invoke(sheet,new object[]{turn,-(float)a,preview}),"pose "+a);ReverseDirectionVerification.Geometry(preview);protectedChecks+=Fixed(protectedFaces,preview);poses++;}
    Check(sheet.Finish(turn,-180),"commit");Fixed(protectedFaces,sheet.Faces);PetalFoldingVerification.Replay(sheet);Check(sheet.Undo()&&ReferenceEquals(before,sheet.Faces),"undo");Check(sheet.Finish(sheet.Plan(line),-180),"redo");cases++;
   }
   Check(fallbacks>0,"failure not reproduced");Debug.Log($"[RearLeaf] PASS cases={cases}, poses={poses}, fallback cases={fallbacks}, fixed-front vertex checks={protectedChecks}; area, seams, collisions, green region, full fold, replay, undo/redo.");
   WingSideVerification.Run();WingSideVerification.RunFlat();JetEdgeVerification.Run();
   EditorApplication.Exit(0);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
 }
}
