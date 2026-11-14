using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class MaterialChartContinuationVerification
{
    const BindingFlags H=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool ok,string m){if(!ok)throw new Exception("[MaterialChart] "+m);}
    public static void Verify(){
        string root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_CONTINUE_ROOT");var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/checkpoint-ten.json")).paperFolds;var request=JsonUtility.FromJson<FoldStep>(File.ReadAllText(root+"/opposite-petal.json"));
        var topology=typeof(PaperSheet).Assembly.GetType("FlyEditor.Flight.PaperFoldTopology");var match=topology.GetMethod("MaterialChartsMatch",H);
        var ctor=typeof(PaperSheet.Face).GetConstructor(H,null,new[]{typeof(Vector2[]),typeof(int),typeof(bool),typeof(Matrix4x4),typeof(Matrix4x4),typeof(PaperSheet.Face),typeof(bool)},null);
        var clip=typeof(PaperSheet).GetMethod("Clip",H);int cases=0,poses=0;
        foreach(float jitter in new[]{-.00002f,0,.00002f})foreach(bool split in new[]{false,true})foreach(bool swap in new[]{false,true}){
            var sheet=PaperSheet.Replay(recipe);Check(sheet.Count==10,"source recipe");var faces=(List<PaperSheet.Face>)sheet.Faces;var basis=faces[request.anchor];int neighbour=-1;
            for(int i=0;i<faces.Count;i++){var f=faces[i];if(i!=request.anchor&&f.frame==basis.frame&&f.material!=basis.material&&(bool)match.Invoke(null,new object[]{f,basis})){neighbour=i;break;}}
            Check(neighbour>=0,"rounded neighbouring chart missing");var old=faces[neighbour];
            PaperSheet.Face Copy(Matrix4x4 chart)=>(PaperSheet.Face)ctor.Invoke(new object[]{old.points,old.layer,old.back,old.frame,chart,old,false});
            var shifted=Copy(Matrix4x4.Translate(new Vector3(.02f,0,0))*old.material);Check(!(bool)match.Invoke(null,new object[]{shifted,basis}),"different material placement treated as same chart");
            var reflected=Copy(old.material*Matrix4x4.Scale(new Vector3(-1,1,1)));Check(!(bool)match.Invoke(null,new object[]{reflected,basis}),"opposite ply treated as same chart");
            var changed=Copy(Matrix4x4.Translate(new Vector3(jitter,0,-jitter))*old.material);faces[neighbour]=changed;
            if(split){var centre=Vector2.zero;foreach(var p in changed.points)centre+=p;centre/=changed.points.Length;var n=new Vector2(1,.37f);var line=new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,centre)};
                var a=(PaperSheet.Face)clip.Invoke(null,new object[]{changed,line,1});var b=(PaperSheet.Face)clip.Invoke(null,new object[]{changed,line,-1});Check(a!=null&&b!=null,"fragment setup");faces[neighbour]=a;faces.Add(b);}
            var step=request;if(swap){var t=step.pullLeft;step.pullLeft=step.pullRight;step.pullRight=t;}
            var plan=PaperCornerPull.Create(sheet,step);Check(plan!=null&&plan.UsesCoordinatedSurface&&plan.ResolvedHingeHints,"rounded/fragmented chart lost supported selection");
            foreach(float angle in new[]{0f,3f,30f,60f,90f,120f,150f,179.9f,180f,45f,0f,180f}){Check(plan.Pose(angle),"support motion blocked at "+angle+" "+plan.LastFailure);ReverseDirectionVerification.Geometry(plan.Preview);poses++;}
            cases++;
        }
        var original=PaperSheet.Replay(recipe);var fold=PaperCornerPull.Create(original,request);Check(original.FinishPull(fold,180),"actual request commit");PetalFoldingVerification.Replay(original);
        Debug.Log("[MaterialChart] PASS "+cases+" roundoff/fragment/order conditions, "+poses+" poses; different material placement and reflected plies rejected; original saved replay.");
    }
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
