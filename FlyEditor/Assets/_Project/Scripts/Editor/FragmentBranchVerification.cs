using System;using System.IO;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using FlyEditor.Flight;using FlyEditor.Editor;
public static class FragmentBranchVerification
{
    const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool ok,string message){if(!ok)throw new Exception("[FragmentBranch] "+message);}
    static void Fixed(IReadOnlyList<PaperSheet.Face> expected,IReadOnlyList<PaperSheet.Face> actual){foreach(var f in expected)foreach(var p in f.points){
        var m=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));bool found=false;foreach(var g in actual){var q=g.material.inverse.MultiplyPoint3x4(m);var v=new Vector2(q.x,q.z);if(!g.Contains(v))continue;found=true;Check(Vector3.Distance(f.World(p,0),g.World(v,0))<.002f,"stationary material moved");}Check(found,"stationary material lost");}}
    static void TopologyGuards(){
        var sheet=new PaperSheet();var original=sheet.Faces[0];var faces=(List<PaperSheet.Face>)sheet.Faces;
        var clip=typeof(PaperSheet).GetMethod("Clip",H);var crosses=typeof(PaperSheet).GetMethod("CrossesRigidMaterialPanel",H);
        PaperSheet.Face Part(PaperSheet.Face f,float x,int side)=>(PaperSheet.Face)clip.Invoke(null,new object[]{f,new FoldStep{creaseNormal=Vector2.right,creaseOffset=x},side});
        var left=Part(original,4,-1);var middle=Part(Part(original,4,1),8,-1);var remote=Part(original,8,1);
        var line=new FoldStep{creaseNormal=Vector2.right,creaseOffset=7};
        faces.Clear();faces.Add(left);faces.Add(remote);Check(!(bool)crosses.Invoke(sheet,new object[]{left,line}),"disconnected remote material accepted");
        faces.Add(middle);Check((bool)crosses.Invoke(sheet,new object[]{left,line}),"connected fragments rejected");
        var ctor=typeof(PaperSheet.Face).GetConstructor(H,null,new[]{typeof(Vector2[]),typeof(int),typeof(bool),typeof(Matrix4x4),typeof(Matrix4x4),typeof(PaperSheet.Face),typeof(bool)},null);
        foreach(var chart in new[]{Matrix4x4.Translate(new Vector3(.02f,0,0))*middle.material,middle.material*Matrix4x4.Scale(new Vector3(-1,1,1))}){
            var unrelated=(PaperSheet.Face)ctor.Invoke(new object[]{middle.points,middle.layer,middle.back,middle.frame,chart,middle,false});faces[2]=unrelated;Check(!(bool)crosses.Invoke(sheet,new object[]{left,line}),"coincident different ply accepted");
        }
    }
    public static void Verify(){
        var root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_NEXT_ROOT");
        var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/checkpoint-eighteen.json")).paperFolds;
        var request=JsonUtility.FromJson<FoldStep>(File.ReadAllText(root+"/fragment-axis.json"));int cases=0,poses=0;
        var preview=typeof(PaperSheet).GetMethod("TryPreviewTurn",H);
        foreach(bool reverse in new[]{false,true})foreach(int order in new[]{0,1,2}){
            var sheet=PaperSheet.Replay(recipe);Check(sheet.Count==recipe.Length,"source replay reset");ReverseDirectionVerification.Geometry(sheet.Faces);var step=request;var faces=(List<PaperSheet.Face>)sheet.Faces;var picked=faces[step.anchor];
            if(order==1)faces.Reverse();if(order==2){var first=faces[0];faces.RemoveAt(0);faces.Add(first);}step.anchor=faces.IndexOf(picked);step.flap=step.anchor+1;
            if(reverse){step.creaseNormal=-step.creaseNormal;step.creaseOffset=-step.creaseOffset;step.side=-step.side;}
            var plan=sheet.Plan(step);Check(plan!=null,"unfolded fragment rejected its connected axis");
            Check(plan.Constrain(3)==0&&plan.Constrain(-3)==0,"physical packet precondition changed");
            var packet=sheet.PlanContactPacket(step);Check(packet!=null&&packet.step.contactPacket,"required contact packet absent");
            var trial=new List<PaperSheet.Face>();foreach(float angle in new[]{0f,3f,30f,60f,90f,150f,179.9f,180f,-3f,-90f,-180f,0f}){
                Check((bool)preview.Invoke(sheet,new object[]{packet,angle,trial}),"contact or seam failure at "+angle);Fixed(packet.fixedFaces,trial);try{ReverseDirectionVerification.Geometry(trial);}catch(Exception e){File.WriteAllText(root+"/bad-preview.json",JsonUtility.ToJson(new PaperGeometryDump{faces=Dump(trial)},true));throw new Exception("[FragmentBranch] geometry reverse="+reverse+" order="+order+" angle="+angle,e);}poses++;
            }
            Check(sheet.Finish(packet,90),"partial commit");if(order==0)PetalFoldingVerification.Replay(sheet);Check(sheet.Undo(),"partial undo");
            Check(sheet.Finish(sheet.PlanContactPacket(step),180),"flat commit");if(order==0)PetalFoldingVerification.Replay(sheet);Check(sheet.Undo(),"flat undo");cases++;
        }
        TopologyGuards();Debug.Log("[FragmentBranch] PASS "+cases+" actual-source axis/order variants, "+poses+" collision/geometry poses; fixed material, replay, undo; remote, shifted and reflected plies excluded.");
    }
    [Serializable]class PaperGeometryDump{public ThreePlayerFlowVerification.Face[] faces;}
    static ThreePlayerFlowVerification.Face[] Dump(IReadOnlyList<PaperSheet.Face> faces){var list=new List<ThreePlayerFlowVerification.Face>();foreach(var f in faces)list.Add(new ThreePlayerFlowVerification.Face{points=f.points,layer=f.layer,back=f.back,frame=f.frame,material=f.material});return list.ToArray();}
    public static void VerifyPlayer(){
        var root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_NEXT_PLAYER");
        PlayerTransition(root,"d",2,3);
        int states=0;foreach(var session in new[]{new[]{"b","13"},new[]{"d","3"}})for(int i=1;i<=int.Parse(session[1]);i++){
            // Historical b/10 is the independently discovered point-junction
            // failure, retained as a negative fixture, not counted as a pass.
            if(session[0]=="b"&&i==10)continue;
            var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(root,"session-"+session[0],"recipe-"+i.ToString("000")+".json"))).paperFolds;
            var sheet=PaperSheet.Replay(recipe);Check(sheet.Count==recipe.Length,"player state silently reset");try{ReverseDirectionVerification.Geometry(sheet.Faces);}catch(Exception e){File.WriteAllText(Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_NEXT_ROOT")+"/bad-player-replay.json",JsonUtility.ToJson(new PaperGeometryDump{faces=Dump(sheet.Faces)},true));throw new Exception("[FragmentBranch] player replay geometry "+session[0]+"/"+i,e);}states++;
        }
        Debug.Log("[FragmentBranchPlayer] PASS actual same mouse input rejected before and folded after; independently checked moving and stationary material; "+states+" observed states replayed. Diagram 17 remains incomplete.");
    }
    static void PlayerTransition(string root,string session,int start,int end){
        var before=JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(Path.Combine(root,"session-"+session,"geometry-"+start.ToString("000")+".json")));
        var after=JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(Path.Combine(root,"session-"+session,"geometry-"+end.ToString("000")+".json")));
        var executed=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(root,"session-"+session,"recipe-"+end.ToString("000")+".json"))).paperFolds;var step=executed[executed.Length-1];
        var basis=before.sheet[step.anchor];var n=step.Normal;var at=n*(step.Position/n.sqrMagnitude);var pivot=basis.frame.MultiplyPoint3x4(new Vector3(at.x,0,at.y));var axis=basis.frame.MultiplyVector(new Vector3(-n.y,0,n.x)*step.side).normalized;
        var rotate=Matrix4x4.Translate(pivot)*Matrix4x4.Rotate(Quaternion.AngleAxis(step.degrees,axis))*Matrix4x4.Translate(-pivot);
        Equal(before.moving,after.sheet,rotate);Equal(before.stationary,after.sheet,Matrix4x4.identity);
    }
    public static void VerifyCorrectedPlayer(){
        var root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_NEXT_PLAYER");PlayerTransition(root,"e",2,3);PlayerTransition(root,"e",4,5);
        var before=JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(Path.Combine(root,"session-e/geometry-001.json")));
        var after=JsonUtility.FromJson<ThreePlayerFlowVerification.Geometry>(File.ReadAllText(Path.Combine(root,"session-e/geometry-005.json")));Equal(before.sheet,after.sheet,Matrix4x4.identity);
        var saved=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(root,"session-e/progress-006.json"))).paperFolds;
        Check(saved.Length==18&&saved[16].preserveSlivers&&saved[16].vertexJoints&&saved[17].preserveSlivers,"corrected input/save flags absent");
        for(int i=1;i<=7;i++){var recipe=JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path.Combine(root,"session-e/recipe-"+i.ToString("000")+".json"))).paperFolds;var sheet=PaperSheet.Replay(recipe);Check(sheet.Count==recipe.Length,"corrected input replay reset");ReverseDirectionVerification.Geometry(sheet.Faces);}
        Debug.Log("[CorrectedCrossingPlayer] PASS actual old failing mouse trace, flat fold and reopening; moving/fixed material, recovered original geometry, 7 observed recipe states, isolated save. Diagram 16 reached; diagram 17 not attempted further.");
    }
    // Independent winding plus point-to-segment distance oracle. Do not use
    // the old area epsilon here: it was the cause of the phantom seam finding.
    static bool Inside(Vector2[] points,Vector2 p){bool positive=false,negative=false;double nearest=double.PositiveInfinity;
        for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];double dx=b.x-a.x,dy=b.y-a.y,x=p.x-a.x,y=p.y-a.y,c=dx*y-dy*x;positive|=c>0;negative|=c<0;double length=dx*dx+dy*dy,t=length==0?0:Math.Max(0,Math.Min(1,(x*dx+y*dy)/length));nearest=Math.Min(nearest,(x-t*dx)*(x-t*dx)+(y-t*dy)*(y-t*dy));}
        return !positive||!negative||nearest<=4e-10;
    }
    static void Equal(ThreePlayerFlowVerification.Face[] before,ThreePlayerFlowVerification.Face[] after,Matrix4x4 transform){Check(before.Length>0,"player material regions absent");foreach(var f in before){var samples=new List<Vector2>(f.points);Vector2 centre=Vector2.zero;foreach(var p in f.points)centre+=p;centre/=f.points.Length;samples.Add(centre);foreach(var p in f.points)samples.Add(Vector2.Lerp(p,centre,.01f));
        foreach(var p in samples){var material=f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));var expected=transform.MultiplyPoint3x4(f.frame.MultiplyPoint3x4(new Vector3(p.x,0,p.y)));bool found=false;foreach(var g in after){var q=g.material.inverse.MultiplyPoint3x4(material);var local=new Vector2(q.x,q.z);if(!Inside(g.points,local))continue;found=true;Check(Vector3.Distance(expected,g.frame.MultiplyPoint3x4(new Vector3(local.x,0,local.y)))<.003f,"player moving/fixed material diverged at "+material);}Check(found,"player material lost");}
    }}
    public static void Run(){try{PaperContainmentVerification.Verify();SliverClippingVerification.Verify();MaterialVertexJointVerification.Verify();MaterialVertexJointVerification.VerifyContinuation();Verify();VerifyPlayer();VerifyCorrectedPlayer();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
