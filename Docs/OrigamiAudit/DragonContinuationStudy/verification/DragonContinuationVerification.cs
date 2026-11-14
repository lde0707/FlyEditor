using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class DragonContinuationVerification
{
    public static void Run(){try{
        var root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_CONTINUE_ROOT");var s=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/checkpoint-ten.json")).paperFolds);if(s.Count!=10)throw new Exception("checkpoint reset");
        var step=JsonUtility.FromJson<FoldStep>(File.ReadAllText(root+"/opposite-petal.json"));
        if(Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_BOUNDARY")=="1"){step.pullLeft=new Vector2(7.7781744f,0);step.pullRight=new Vector2(11,3.2218256f);}
        var direct=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperPetalFold").GetMethod("Create").Invoke(null,new object[]{s,step});
        Debug.Log("[DragonContinuation] direct support="+(direct!=null));
        var plan=PaperCornerPull.Create(s,step);Debug.Log("[DragonContinuation] plan="+(plan!=null)+" coordinated="+plan?.UsesCoordinatedSurface+" hints="+plan?.ResolvedHingeHints);
        if(plan==null||!plan.UsesCoordinatedSurface||!plan.Pose(3))throw new Exception("[DragonContinuation] opposite supported pocket cannot lift 3 degrees");
        foreach(float a in new[]{0f,3f,30f,60f,90f,120f,150f,179.9f,180f,45f,0f,180f}){if(!plan.Pose(a))throw new Exception("[DragonContinuation] blocked "+a+" "+plan.LastFailure);ReverseDirectionVerification.Geometry(plan.Preview);}
        if(!s.FinishPull(plan,180))throw new Exception("[DragonContinuation] commit failed");PetalFoldingVerification.Replay(s);Debug.Log("[DragonContinuation] PASS opposite supported pocket, full closure, seams and replay.");EditorApplication.Exit(0);
    }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
