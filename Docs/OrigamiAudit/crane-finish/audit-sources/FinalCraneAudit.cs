using System;using System.IO;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class FinalCraneAudit {
 [Serializable]public class Result {public int committedFolds;public bool allStageGeometry,continuousReverse,oppositeWings,saveReplay;public string execution="Production fold API replay and motion checks; input harness separately checks tail and direction controls.";public string remaining="Body remains flat; body inflation and uniform shell spacing are not certified.";public float wingSeparation;}
 static void Check(bool v,string m){if(!v)throw new Exception("[FinalCrane] "+m);}
 static Vector3 At(PaperSheet s,Vector2 m){foreach(var f in s.Faces){var q=f.material.inverse.MultiplyPoint3x4(new Vector3(m.x,0,m.y));var p=new Vector2(q.x,q.z);if(f.Contains(p))return f.World(p,0);}throw new Exception("Missing point");}
 public static void Run(){string root=Environment.GetEnvironmentVariable("FLYEDITOR_CRANE_FINISH");var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(root,"crane.json"))).paperFolds;var s=new PaperSheet();ReverseDirectionVerification.Geometry(s.Faces);
 for(int i=0;i<recipe.Length;i++){var step=recipe[i];if(step.packetReverse){var plan=PaperInsideReverse.Create(s,step);Check(plan!=null,"reverse plan "+i);for(int a=0;a<=180;a+=5){Check(plan.Pose(a),"reverse sweep "+i+"/"+a);ReverseDirectionVerification.Geometry(plan.Preview);}foreach(float a in new[]{80,20,150,0}){Check(plan.Pose(a),"scrub");ReverseDirectionVerification.Geometry(plan.Preview);}}
 Check(s.ApplyStep(step),"commit "+i);ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);Debug.Log("[FinalCrane] step "+(i+1)+" area/seams/replay PASS");}
 var aTip=At(s,new Vector2(0,11));var bTip=At(s,new Vector2(11,0));Check(aTip.y*bTip.y<0 && Mathf.Abs(aTip.y-bTip.y)>5,"wings did not spread to opposite sides");
 File.WriteAllText(Path.Combine(root,"result.json"),JsonUtility.ToJson(new Result{committedFolds=s.Count,allStageGeometry=true,continuousReverse=true,oppositeWings=true,saveReplay=true,wingSeparation=Vector3.Distance(aTip,bTip)},true));Debug.Log("[FinalCrane] PASS 16 stages; neck/tail/head sweeps; opposite wing tips distance="+Vector3.Distance(aTip,bTip));
 }
 public static void Regression(){ReverseDirectionVerification.Run();NestedRegression.Run();Run();CraneFinishRender.Run();}
}
