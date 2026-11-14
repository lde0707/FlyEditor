using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;using FlyEditor.Flight;
public static class ContactPacketVerification
{
    const BindingFlags H=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool ok,string m){if(!ok)throw new Exception("[ContactPacket] "+m);}
    static Vector3 Material(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
    static void Fixed(System.Collections.Generic.IReadOnlyList<PaperSheet.Face> expected,System.Collections.Generic.IReadOnlyList<PaperSheet.Face> actual){foreach(var f in expected)foreach(var p in f.points){var m=Material(f,p);bool found=false;foreach(var g in actual){var v=g.material.inverse.MultiplyPoint3x4(m);var q=new Vector2(v.x,v.z);if(!g.Contains(q))continue;found=true;Check(Vector3.Distance(f.World(p,0),g.World(q,0))<.002f,"fixed material moved");}Check(found,"fixed material missing");}}
    public static void Verify(){
        var root=Environment.GetEnvironmentVariable("FLYEDITOR_DRAGON_CONTINUE_ROOT");var s=PaperSheet.Replay(JsonUtility.FromJson<ResearchSave>(File.ReadAllText(root+"/checkpoint-twelve.json")).paperFolds);if(s.Count!=12)throw new Exception("source recipe reset");
        var step=JsonUtility.FromJson<FoldStep>(File.ReadAllText(root+"/contact-tip.json"));var single=s.Plan(step);Debug.Log("[ContactPacket] single limits="+single?.Constrain(180)+"/"+single?.Constrain(-180));
        var all=step;all.flap=0;all.singlePanel=all.connectedPanel=all.separateFoldSides=false;var packet=s.Plan(all);Debug.Log("[ContactPacket] whole limits="+packet?.Constrain(180)+"/"+packet?.Constrain(-180));
        var method=typeof(PaperSheet).GetMethod("PlanContactPacket",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);if(method==null)throw new Exception("[ContactPacket] no player-reachable minimal contact packet plan");
        var result=(PaperSheet.Turn)method.Invoke(s,new object[]{step});if(result==null||Mathf.Abs(result.Constrain(180))<30)throw new Exception("[ContactPacket] minimal closed packet cannot fold");
        var preview=typeof(PaperSheet).GetMethod("TryPreviewTurn",H);int cases=0,poses=0;
        foreach(bool reverse in new[]{false,true})foreach(float shift in new[]{-.05f,0,.05f}){
            var request=step;request.creaseOffset+=shift;
            if(reverse){request.creaseNormal=-request.creaseNormal;request.creaseOffset=-request.creaseOffset;request.side=-request.side;}
            var plan=s.PlanContactPacket(request);Check(plan!=null&&plan.step.contactPacket,"packet selection variant");
            var original=s.Faces;var trial=new System.Collections.Generic.List<PaperSheet.Face>();
            foreach(float a in new[]{0f,3f,30f,60f,90f,120f,150f,179.9f,180f,-3f,-45f,-90f,-180f,0f}){
                Check((bool)preview.Invoke(s,new object[]{plan,a,trial}),"packet preview "+a);Fixed(plan.fixedFaces,trial);FlyEditor.Editor.ReverseDirectionVerification.Geometry(trial);poses++;
            }
            Check(s.Finish(plan,90),"partial packet commit");FlyEditor.Editor.PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(original,s.Faces),"packet undo");
            Check(s.Finish(s.Plan(plan.step),180),"flat packet commit");FlyEditor.Editor.PetalFoldingVerification.Replay(s);Check(s.Undo()&&ReferenceEquals(original,s.Faces),"flat packet undo");cases++;
        }
        // Ordinary independently movable wings must retain their original scope.
        var pick=typeof(FlyEditor.Editor.WingSideVerification).GetMethod("Pick",BindingFlags.NonPublic|BindingFlags.Static);int wings=0;
        foreach(bool narrow in new[]{false,true})for(int r=0;r<4;r++)foreach(bool top in new[]{false,true}){
            var paper=FlyEditor.Editor.WingSideVerification.Seed(narrow,r,out var owner);var line=(FoldStep)pick.Invoke(null,new object[]{paper,owner,r,top,3.5f});line.materialPartition=true;
            var independent=paper.Plan(line);var unchanged=paper.PlanContactPacket(line);Check(independent!=null&&unchanged!=null&&!unchanged.step.contactPacket,"independent leaf silently enlarged");
            float a=0,b=0;foreach(var f in independent.movingFaces)a+=PaperSheet.Area(f.points);foreach(var f in unchanged.movingFaces)b+=PaperSheet.Area(f.points);Check(Mathf.Abs(a-b)<.001f,"independent moving area changed");wings++;
        }
        Debug.Log("[ContactPacket] PASS "+cases+" crease/order variants, "+poses+" poses; fixed material, collisions, partial/flat replay and undo; "+wings+" independent wings retain scope.");
    }
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
