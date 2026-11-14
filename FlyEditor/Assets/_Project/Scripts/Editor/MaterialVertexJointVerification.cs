using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class MaterialVertexJointVerification
{
    public static void Verify(){
        const BindingFlags H=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var a=new PaperSheet.Face(new[]{new Vector2(1,1),new Vector2(2,1),new Vector2(1,2)},0,false);
        var b=new PaperSheet.Face(new[]{new Vector2(1,1),new Vector2(0,1),new Vector2(1,0)},0,false);
        var joined=typeof(PaperSheet).GetMethod("Joined",H);var line=new FoldStep{creaseNormal=Vector2.up,creaseOffset=.5f,materialPartition=true,vertexJoints=true};
        if(!(bool)joined.Invoke(null,new object[]{a,b,line,a}))throw new Exception("[MaterialVertexJoint] an off-axis original material corner was allowed to separate");
        line.creaseOffset=1;if((bool)joined.Invoke(null,new object[]{a,b,line,a}))throw new Exception("[MaterialVertexJoint] a corner on the hinge incorrectly carried its neighbour");
        Debug.Log("[MaterialVertexJoint] PASS shared corner off-axis retained; on-axis corner remains free to hinge.");
    }
    public static void VerifyContinuation(){
        const BindingFlags H=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_NEXT_ROOT");var all=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/checkpoint-eighteen.json")).paperFolds;
        var prefix=new FoldStep[16];Array.Copy(all,prefix,16);var sheet=PaperSheet.Replay(prefix);if(sheet.Count!=16)throw new Exception("[MaterialVertexJoint] source reset");ReverseDirectionVerification.Geometry(sheet.Faces);
        var request=all[16];request.vertexJoints=request.preserveSlivers=true;var single=sheet.Plan(request);var packet=sheet.PlanContactPacket(request);var plan=packet??single;
        int poses=0;var preview=typeof(PaperSheet).GetMethod("TryPreviewTurn",H);var trial=new List<PaperSheet.Face>();
        if(plan!=null)foreach(float sign in new[]{1f,-1f})for(float a=0;a<=180;a+=6){
            float next=sign*a;if(Mathf.Abs(plan.Constrain(next)-next)>.001f)break;
            if(!(bool)preview.Invoke(sheet,new object[]{plan,next,trial}))break;try{ReverseDirectionVerification.Geometry(trial);}catch(Exception e){
                File.WriteAllText(root+"/vertex-debug.json",JsonUtility.ToJson(new ThreePlayerFlowVerification.Geometry{sheet=Dump(sheet.Faces),moving=Dump(plan.movingFaces),stationary=Dump(plan.fixedFaces),rotation=plan.Rotation(next)},true));
                File.WriteAllText(root+"/vertex-preview.json",JsonUtility.ToJson(new ThreePlayerFlowVerification.Geometry{sheet=Dump(trial)},true));File.WriteAllText(root+"/vertex-axis.json",JsonUtility.ToJson(plan.step,true));throw new Exception("[MaterialVertexJoint] geometry at "+next,e);
            }poses++;
        }
        Debug.Log("[MaterialVertexJoint] historical invalid crossing: new plan="+(plan!=null)+" safe poses="+poses+"; a rejected multi-hinge motion is BLOCKED, never model completion.");
    }
    static ThreePlayerFlowVerification.Face[] Dump(IReadOnlyList<PaperSheet.Face> faces){var list=new List<ThreePlayerFlowVerification.Face>();foreach(var f in faces)list.Add(new ThreePlayerFlowVerification.Face{points=f.points,layer=f.layer,back=f.back,frame=f.frame,material=f.material});return list.ToArray();}
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
