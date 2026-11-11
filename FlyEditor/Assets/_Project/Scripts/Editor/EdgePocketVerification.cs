using System;using System.Reflection;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class EdgePocketVerification {
 static readonly MethodInfo Find=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperFoldAssist").GetMethod("TryEdgePocket");
 public static bool Try(PaperSheet s,int anchor,int edge,out PaperCornerPull p,out int sign){var args=new object[]{s,anchor,edge,null,0};bool ok=(bool)Find.Invoke(null,args);p=(PaperCornerPull)args[3];sign=(int)args[4];return ok;}
 static void Check(bool value,string message){if(!value)throw new Exception("[EdgePocket] "+message);}
 static Vector2 Rotate(Vector2 p,int r){while(r-->0)p=new Vector2(11-p.y,p.x);return p;}
 public static void Run(){try{int cases=0,poses=0;
  Check(!Try(new PaperSheet(),0,0,out _,out _),"flat sheet mistaken for a pocket");
  Check(!Try(new PaperSheet(),-1,0,out _,out _),"invalid face");
  for(int rotation=0;rotation<4;rotation++)foreach(bool frontFirst in new[]{true,false}){
   var recipe=PetalFoldingVerification.Square(rotation).Recipe();var s=new PaperSheet();Check(s.ApplyStep(recipe[0])&&s.ApplyStep(recipe[1]),"seed");
   foreach(bool front in new[]{frontFirst,!frontFirst}){
    var tip=Rotate(front?new Vector2(0,11):new Vector2(11,0),rotation);int direction=front?1:-1;PaperCornerPull fold=null;
    for(int i=0;i<s.Faces.Count&&fold==null;i++)for(int e=0;e<s.Faces[i].points.Length;e++)if(Try(s,i,e,out var p,out var sign)&&sign==direction){
     var m=s.Faces[i].material.MultiplyPoint3x4(new Vector3(p.Step.pullTip.x,0,p.Step.pullTip.y));
     if(Vector2.Distance(new Vector2(m.x,m.z),tip)<.01f){fold=p;break;}
    }
    Check(fold!=null,"missing mouth "+rotation+"/"+front);var before=s.Faces;
    for(int a=0;a<=180;a+=5){Check(fold.Pose(direction*a),"sweep "+a);ReverseDirectionVerification.Geometry(fold.Preview);poses++;}
    foreach(float a in new[]{160f,50,0,95}){Check(fold.Pose(direction*a),"scrub");ReverseDirectionVerification.Geometry(fold.Preview);}
    Check(!fold.Pose(-direction*10),"opposite blocked side crossed");
    Check(s.FinishPull(fold,direction*60),"partial");PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(s.Faces,before),"partial undo");
    Check(s.FinishPull(fold,direction*180),"flat commit");PetalFoldingVerification.Replay(s);ReverseDirectionVerification.Geometry(s.Faces);
   }
   foreach(var f in s.Faces)foreach(var p in f.points){var w=f.World(p,0);var q=Rotate(new Vector2(w.x,w.z),(4-rotation)%4);Check(q.x>=-.002f&&q.y>=-.002f&&q.x<=5.502f&&q.y<=5.502f,"wrong square base");}cases++;
  }
  Debug.Log("[EdgePocket] PASS "+cases+" rotated/front-first/back-first sequences; "+poses+" sweep poses; exact square, collision, scrub, partial/full replay and undo, invalid/flat input.");
 }catch(Exception e){Debug.LogException(e);UnityEditor.EditorApplication.Exit(1);}}
}
