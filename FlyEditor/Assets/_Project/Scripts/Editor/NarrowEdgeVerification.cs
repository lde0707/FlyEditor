using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class NarrowEdgeVerification {
  static void Check(bool ok,string message){if(!ok)throw new Exception("[NarrowEdge] "+message);}
  static void Fixed(IReadOnlyList<PaperSheet.Face> source,IReadOnlyList<PaperSheet.Face> after){
   int checkedPoints=0;
   foreach(var f in source)if(f.layer<2)foreach(var p in f.points){
    var material=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));bool found=false;
    foreach(var g in after){var q=g.material.inverse.MultiplyPoint3x4(material);var uv=new Vector2(q.x,q.z);if(!g.Contains(uv))continue;
     Check(Vector3.Distance(f.World(p,0),g.World(uv,0))<.002f,"opposite leaf moved layer="+f.layer+" local="+p+" material="+material+" before="+f.World(p,0)+" after="+g.World(uv,0));found=true;}
    Check(found,"opposite material lost");checkedPoints++;
   }
   Check(checkedPoints>0,"no lower leaf coverage");
  }
  public static void Run(){try{
   var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;
   var s=new PaperSheet();for(int i=0;i<recipe.Length-1;i++)Check(s.ApplyStep(recipe[i]),"seed "+i);
   var step=recipe[recipe.Length-1];step.separateFoldSides=true;var before=s.Faces;
   var p=PaperCornerPull.Create(s,step);Check(p!=null&&p.UsesEdgeMatch,"edge recognition");
   var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
   var match=typeof(PaperCornerPull).GetField("match",hidden).GetValue(p);
   var turn=(PaperSheet.Turn)match.GetType().GetField("turn",hidden).GetValue(match);
   Check(turn.step.separateFoldSides&&turn.step.singlePanel&&turn.step.flap==step.flap,"selected leaf scope lost");
   int poses=0;for(int a=0;a<=180;a+=3){Check(p.Pose(a),"pose "+a);ReverseDirectionVerification.Geometry(p.Preview);Fixed(before,p.Preview);poses++;}
   Check(s.FinishPull(p,180),"commit");Fixed(before,s.Faces);PetalFoldingVerification.Replay(s);
   Check(s.Undo()&&ReferenceEquals(before,s.Faces),"undo");
   // Existing two-point folded-wing isolation and edge-contact checks remain valid.
   WingSideVerification.Run();JetEdgeVerification.Run();
   Debug.Log("[NarrowEdge] PASS actual four-click recipe, "+poses+" poses, opposite leaf fixed, seams, area, scope propagation, replay and undo.");
   EditorApplication.Exit(0);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
 }
}


