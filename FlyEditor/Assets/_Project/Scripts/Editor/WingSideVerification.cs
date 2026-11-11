using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;
namespace FlyEditor.Editor {
 public static class WingSideVerification {
  static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
  static void Check(bool ok,string text){if(!ok)throw new Exception("[WingSide] "+text);}
  static Vector2 R(Vector2 p,int r){while(r-->0)p=new Vector2(11-p.y,p.x);return p;}
  static Vector2 N(Vector2 n,int r){while(r-->0)n=new Vector2(-n.y,n.x);return n;}
  static FoldStep Line(Vector2 n,float d,int side,int r){var nr=N(n,r);return new FoldStep{creaseNormal=nr,creaseOffset=d+Vector2.Dot(nr-n,Vector2.one*5.5f),side=side,continuousHinge=true,neutralContacts=true,compactLayers=true};}
  static Vector2 Local(PaperSheet.Face f,Vector3 m){var q=f.material.inverse.MultiplyPoint3x4(m);return new Vector2(q.x,q.z);}
  static Vector3 Material(PaperSheet.Face f,Vector2 q)=>f.material.MultiplyPoint3x4(new Vector3(q.x,0,q.y));
  static bool In(IReadOnlyList<PaperSheet.Face> pieces,Vector3 m){foreach(var f in pieces)if(f.Contains(Local(f,m)))return true;return false;}
  static bool Owner(PaperSheet.Face f,IReadOnlyList<PaperSheet.Face> pieces){Vector2 p=Vector2.zero;foreach(var q in f.points)p+=q;return In(pieces,Material(f,p/f.points.Length));}
  public static PaperSheet Seed(bool narrow,int rotation,out List<PaperSheet.Face> upper){
   var s=new PaperSheet();Check(s.Finish(s.Plan(Line(Vector2.one,16.5f,1,rotation)),180),"nose A");Check(s.Finish(s.Plan(Line(new Vector2(1,-1),5.5f,1,rotation)),180),"nose B");
   if(narrow){var n=new Vector2(Mathf.Sin(22.5f*Mathf.Deg2Rad),Mathf.Cos(22.5f*Mathf.Deg2Rad));Check(s.Finish(s.Plan(Line(n,Vector2.Dot(n,new Vector2(11,5.5f)),1,rotation)),180),"narrow A");n.y=-n.y;Check(s.Finish(s.Plan(Line(n,Vector2.Dot(n,new Vector2(11,5.5f)),1,rotation)),180),"narrow B");}
   var turn=s.Plan(Line(Vector2.up,5.5f,1,rotation));Check(turn!=null,"body plan");upper=new List<PaperSheet.Face>(turn.movingFaces);Check(s.Finish(turn,180),"body fold");return s;
  }
  static void Fixed(IReadOnlyList<PaperSheet.Face> before,IReadOnlyList<PaperSheet.Face> after,IReadOnlyList<PaperSheet.Face> upper,bool top){
   int checkedPoints=0;foreach(var f in before)if(Owner(f,upper)!=top)foreach(var p in f.points){var m=Material(f,p);bool found=false;foreach(var g in after){var q=Local(g,m);if(!g.Contains(q))continue;Check(Vector3.Distance(f.World(p,0),g.World(q,0))<.002f,"opposite wing moved");found=true;}Check(found,"opposite wing material lost");checkedPoints++;}Check(checkedPoints>0,"opposite wing coverage");
  }
  static FoldStep Pick(PaperSheet s,List<PaperSheet.Face> upper,int r,bool top,float tail=-1){var p=R(new Vector2(2.5f,tail<0?3.5f:2f),r);int anchor=-1;
   for(int i=0;i<s.Faces.Count;i++){var f=s.Faces[i];if(f.Contains(p)&&Owner(f,upper)==top&&(anchor<0||(top?f.layer>s.Faces[anchor].layer:f.layer<s.Faces[anchor].layer)))anchor=i;}
   Check(anchor>=0,"wing pick");var normal=tail<0?Vector2.up:new Vector2(-(5.5f-tail),11).normalized;var step=Line(normal,tail<0?4.5f:normal.y*tail,-1,r);step.anchor=anchor;step.flap=anchor+1;step.singlePanel=step.connectedPanel=step.separateFoldSides=true;step.contact=p;return step;
  }
  public static void RunFlat(){int cases=0;
   for(int r=0;r<4;r++)foreach(bool topFirst in new[]{false,true})foreach(float tail in new[]{3.5f,2.5f}){
    var s=Seed(true,r,out var upper);int k=0;
    foreach(bool top in new[]{topFirst,!topFirst}){
     var before=s.Faces;var step=Pick(s,upper,r,top,tail);var turn=s.Plan(step);
     Check(turn!=null,"flat wing plan "+r+"/"+top+"/"+tail);
     foreach(var f in turn.movingFaces)Check(Owner(f,upper)==top,"flat refold selected opposite wing");
     Check(s.Finish(turn,(top?1:-1)*(k++==0?180:82)),"flat wing commit "+r+"/"+top+"/"+tail);
     Fixed(before,s.Faces,upper,top);ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);
    }cases++;
   }
   Debug.Log("[WingSide] PASS "+cases+" full-180 tapered wing sequences followed by opposite wing; fixed material, seams and replay.");
  }
  [MenuItem("Tools/FlyEditor/Validation/Independent folded wings")]
  public static void Run(){int cases=0,poses=0;
   foreach(bool narrow in new[]{false,true})for(int r=0;r<4;r++)foreach(bool topFirst in new[]{false,true})foreach(float tail in new[]{-1f,4.5f,3.5f,2.5f}){
    var s=Seed(narrow,r,out var upper);
    foreach(bool top in new[]{topFirst,!topFirst}){
     var step=Pick(s,upper,r,top,tail);var source=s.Faces;var turn=s.Plan(step);Check(turn!=null,"individual wing plan");
     Check(!turn.MovesOtherPlyAt(step.contact),"wrong side guard");foreach(var f in turn.movingFaces)Check(Owner(f,upper)==top,"opposite wing entered movement set");
     if(s.Count==(narrow?5:3))Check(turn.Constrain(top?-30:30)==0,"stationary packet collision bypassed");
     var preview=new List<PaperSheet.Face>();var method=typeof(PaperSheet).GetMethod("TryPreviewTurn",Hidden);
     for(int a=0;a<=90;a+=3){Check((bool)method.Invoke(s,new object[]{turn,top?(float)a:-a,preview}),"sweep "+narrow+"/"+r+"/"+top+"/tail="+tail+"/"+a);ReverseDirectionVerification.Geometry(preview);Fixed(source,preview,upper,top);poses++;}
     Check(s.Finish(turn,top?76:-82),"commit");PetalFoldingVerification.Replay(s);ReverseDirectionVerification.Geometry(s.Faces);Fixed(source,s.Faces,upper,top);
     Check(s.Undo()&&ReferenceEquals(source,s.Faces),"undo");Check(s.Finish(s.Plan(step),top?76:-82),"redo");
    }cases++;
   }
   // Planning workers must retain fold ownership without sharing mutable history.
   var packet=Seed(true,0,out var owner);var copy=(PaperSheet)typeof(PaperSheet).GetMethod("PlanningCopy",Hidden).Invoke(packet,null);var picked=Pick(copy,owner,0,true);Check(copy.Finish(copy.Plan(picked),70),"planning copy ownership");Check(packet.Count==5&&copy.Count==6,"copy changed source");PetalFoldingVerification.Replay(copy);
   // Reproduce the actual leak: a slightly oblique line crosses the central
   // joining seam. Legacy adjacency follows it into the opposite wing, and
   // its connected-panel guard used to explicitly skip that check.
   var crossed=Seed(true,0,out var split);var invalid=Pick(crossed,split,0,true);
   invalid.creaseNormal=new Vector2(-Mathf.Sin(4*Mathf.Deg2Rad),Mathf.Cos(4*Mathf.Deg2Rad));invalid.creaseOffset=5.3f;
   var legacy=invalid;legacy.separateFoldSides=false;var leaked=crossed.Plan(legacy);Check(leaked!=null,"leak reproduction plan");bool other=false;foreach(var f in leaked.movingFaces)other|=!Owner(f,split);Check(other&&!leaked.MovesOtherPlyAt(legacy.contact),"legacy opposite-wing leak not reproduced");
   Check(Mathf.Max(Mathf.Abs(leaked.Constrain(90)),Mathf.Abs(leaked.Constrain(-90)))>=30,"legacy leak could not actually move");
   Check(crossed.Plan(invalid)==null,"crossing crease silently carried opposite wing");
   // Earlier saves may have made the central fold using connected individual mode.
   var individual=new PaperSheet();var book=Line(Vector2.up,5.5f,1,0);book.anchor=0;book.flap=1;book.singlePanel=book.connectedPanel=true;
   var beforeBook=individual.Plan(book);var foldedSide=new List<PaperSheet.Face>(beforeBook.movingFaces);Check(individual.Finish(beforeBook,180),"individual body seed");var wing=Pick(individual,foldedSide,0,true);Check(individual.Finish(individual.Plan(wing),65),"individual body ownership");PetalFoldingVerification.Replay(individual);ReverseDirectionVerification.Geometry(individual.Faces);
   Debug.Log("[WingSide] PASS "+cases+" rotated basic/narrow parallel and tapered wing sequences, "+poses+" poses; either wing first, opposite material unchanged, contacts, area, all seams, replay, undo, planning-copy history.");
   Debug.Log("[WingSide] PASS reproduced legacy opposite-wing leak; crossing scope rejected; legacy individual body fold supported.");
  }
 }
}
