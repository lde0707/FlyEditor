using System;using System.IO;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class PetalCornerToleranceVerification {
 static void Check(bool yes,string message){if(!yes)throw new Exception("[PetalTolerance] "+message);}
 static PaperSheet Base(FoldStep[] a,int count){var s=new PaperSheet();for(int i=0;i<count;i++)Check(s.ApplyStep(a[i]),"base "+i);return s;}
 public static void Run(){try{
 var all=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Environment.GetEnvironmentVariable("FLYEDITOR_PLAYER_RECIPE"))).paperFolds;int cases=0;
 foreach(int stage in new[]{4,5})foreach(int handle in new[]{0,1,2})foreach(var d in new[]{new Vector2(.01f,.01f),new Vector2(-.01f,.01f),new Vector2(.01f,-.01f),new Vector2(-.01f,-.01f),new Vector2(.035f,0),new Vector2(-.035f,0),new Vector2(0,.035f),new Vector2(0,-.035f)}){
 var s=Base(all,stage);var st=all[stage];if(handle==0)st.pullStop+=d;else if(handle==1)st.pullLeft+=d;else st.pullRight+=d;
 var p=PaperCornerPull.Create(s,st);Check(p!=null&&p.UsesPetalFold,"petal recognition stage="+stage+" handle="+handle+" delta="+d);
 int sign=all[stage].degrees>0?1:-1;Check(p.TryMove(0,sign*180,out var reached)&&Mathf.Abs(reached-sign*180)<.001f,"full movement");
 for(int deg=0;deg<=180;deg+=5){Check(p.Pose(sign*deg),"sweep "+deg);ReverseDirectionVerification.Geometry(p.Preview);}
 foreach(int deg in new[]{90,5,140,0}){Check(p.Pose(sign*deg),"reverse scrub");ReverseDirectionVerification.Geometry(p.Preview);}
 Check(!p.Pose(-sign*5),"must not penetrate fixed opposite ply");
 Check(s.FinishPull(p,sign*180),"commit");PetalFoldingVerification.Replay(s);Check(s.Undo()&&s.Count==stage,"undo");cases++;
 }
 {var s=Base(all,4);var st=all[4];st.pullRight+=new Vector2(.2f,.2f);var p=PaperCornerPull.Create(s,st);Check(p==null||(p.Step.pullRight-st.pullRight).sqrMagnitude<.000001f,"distant custom point silently snapped");}
 PetalFoldingVerification.Run();PocketFoldingVerification.Run();FourPointFoldingVerification.Run();
 Debug.Log("[PetalTolerance] PASS "+cases+" near-corner front/back cases, full 180-degree sweeps, reverse scrub, blocked reverse direction, seams/area, replay/undo, distant-point preservation and petal/pocket/four-point regressions.");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
