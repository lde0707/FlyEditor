using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FlyEditor.Flight;
using FlyEditor.Editor;

public static class SequenceConsistencyVerification
{
    const BindingFlags H=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly MethodInfo Pick=typeof(WingSideVerification).GetMethod("Pick",H);
    static readonly MethodInfo Preview=typeof(PaperSheet).GetMethod("TryPreviewTurn",H);
    static readonly MethodInfo Fixed=typeof(WingSideVerification).GetMethod("Fixed",H);
    static int operations,poses,historySelections,blockedRequests;
    static void Check(bool ok,string message){if(!ok)throw new Exception("[SequenceConsistency] "+message);}
    static Vector3 Material(PaperSheet.Face f,Vector2 p)=>f.material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
    static Vector2 Local(PaperSheet.Face f,Vector3 p){var q=f.material.inverse.MultiplyPoint3x4(p);return new Vector2(q.x,q.z);}
    static bool Owned(PaperSheet.Face f,IReadOnlyList<PaperSheet.Face> owner){Vector2 c=Vector2.zero;foreach(var p in f.points)c+=p;var m=Material(f,c/f.points.Length);foreach(var g in owner)if(g.Contains(Local(g,m)))return true;return false;}
    static void Equivalent(IReadOnlyList<PaperSheet.Face> expected,IReadOnlyList<PaperSheet.Face> actual,string context){
        foreach(var f in expected){var samples=new List<Vector2>(f.points);Vector2 c=Vector2.zero;foreach(var p in f.points)c+=p;samples.Add(c/f.points.Length);
            foreach(var p in samples){var m=Material(f,p);bool found=false;foreach(var g in actual){var q=Local(g,m);if(!g.Contains(q))continue;found=true;Check(Vector3.Distance(f.World(p,0),g.World(q,0))<.003f,context+" changed material "+m);}
                Check(found,context+" lost material");}}
    }
    static void Geometry(PaperSheet s){ReverseDirectionVerification.Geometry(s.Faces);PetalFoldingVerification.Replay(s);}
    static void Commit(PaperSheet s,PaperSheet.Turn t,float angle,List<PaperSheet.Face> owner,bool top,string context){
        Check(t!=null,context+" no plan");var before=s.Faces;
        foreach(var f in t.movingFaces)Check(Owned(f,owner)==top,context+" selected opposite ply");
        if(context.StartsWith("fold ")){
            float forbidden=-Mathf.Sign(angle)*30;Check(Mathf.Abs(t.Constrain(forbidden))<.001f,context+" stack contact became passable after earlier reopen");
            var valid=new List<PaperSheet.Face>();Check((bool)Preview.Invoke(s,new object[]{t,0f,valid}),context+" initial preview");var old=new List<PaperSheet.Face>(valid);int count=s.Count;
            Check(!(bool)Preview.Invoke(s,new object[]{t,forbidden,valid}),context+" forbidden preview accepted");
            Check(valid.Count==old.Count,context+" failed preview replaced visible region");for(int i=0;i<old.Count;i++)Check(ReferenceEquals(valid[i],old[i]),context+" failed preview changed visible face");
            Check(!s.Finish(t,forbidden)&&ReferenceEquals(s.Faces,before)&&s.Count==count,context+" blocked commit changed source");blockedRequests++;
        }
        var next=new List<PaperSheet.Face>();foreach(float factor in new[]{0f,.25f,.5f,1f}){
            Check((bool)Preview.Invoke(s,new object[]{t,angle*factor,next}),context+" blocked at "+(angle*factor)+" limit="+t.Constrain(angle));
            Fixed.Invoke(null,new object[]{before,next,owner,top});ReverseDirectionVerification.Geometry(next);poses++;
        }
        Check(s.Finish(t,angle),context+" commit");Fixed.Invoke(null,new object[]{before,s.Faces,owner,top});Geometry(s);operations++;
    }
    static PaperSheet.Turn Return(PaperSheet s,PaperSheet.SurfacePoint handle,IReadOnlyList<PaperSheet.Face> target,out float angle){
        angle=0;Check(s.TryResolveSurface(handle,out var vertex),"opening handle lost");var f=s.Faces[vertex.anchor];var material=Material(f,vertex.point);
        Vector3 desired=default;bool found=false;foreach(var g in target){var q=Local(g,material);if(g.Contains(q)){desired=g.World(q,0);found=true;break;}}Check(found,"return target missing");
        var turns=new List<PaperSheet.Turn>();s.OpeningTurns(vertex,turns,false,true,true);PaperSheet.Turn best=null;float score=float.PositiveInfinity;
        foreach(var t in turns)for(int a=-180;a<=180;a++){
            if(Mathf.Abs(t.Constrain(a)-a)>.001f)continue;float error=Vector3.Distance(t.Rotation(a).MultiplyPoint3x4(f.World(vertex.point,0)),desired);
            if(error<score){score=error;best=t;angle=a;}
        }
        Check(best!=null&&score<.003f,"opening cannot return to previous pose: candidates="+turns.Count+" closest="+score+" angle="+angle);return best;
    }
    static void Wings(){
        foreach(bool narrow in new[]{false,true})for(int r=0;r<4;r++)foreach(bool first in new[]{false,true})foreach(float foldAngle in new[]{76f,180f}){
            var sheet=WingSideVerification.Seed(narrow,r,out var owner);var source=sheet.Faces;
            foreach(bool top in new[]{first,!first,first,!first}){
                var step=(FoldStep)Pick.Invoke(null,new object[]{sheet,owner,r,top,3.5f});step.materialPartition=true;
                var turn=sheet.Plan(step);Check(turn!=null,"fold plan "+narrow+"/"+r+"/"+top+"/"+foldAngle);
                var f=turn.movingFaces[0];Vector2 c=Vector2.zero;foreach(var p in f.points)c+=p;c/=f.points.Length;
                int index=-1;var m=Material(f,c);for(int i=0;i<sheet.Faces.Count;i++)if(sheet.Faces[i].Contains(Local(sheet.Faces[i],m))&&Owned(sheet.Faces[i],owner)==top){index=i;break;}
                Check(index>=0,"handle owner missing");var handle=sheet.CaptureSurface(new PaperSheet.FoldVertex(index,Local(sheet.Faces[index],m)));var before=sheet.Faces;
                string context=narrow+"/"+r+"/"+top+"/"+foldAngle+"/count="+sheet.Count;
                Commit(sheet,turn,top?foldAngle:-foldAngle,owner,top,"fold "+context);
                var back=Return(sheet,handle,before,out var degrees);Commit(sheet,back,degrees,owner,top,"open "+context);Equivalent(before,sheet.Faces,"round trip "+context);
                var replay=PaperSheet.Replay(sheet.Recipe());Check(replay.Count==sheet.Count,"silent replay reset");Equivalent(sheet.Faces,replay.Faces,"replay");
            }
            Equivalent(source,sheet.Faces,"sequence return");
        }
    }
    static void OpenBookHistory(){
        var s=new PaperSheet();var source=s.Faces;
        var t=s.Plan(new FoldStep{anchor=0,flap=1,singlePanel=true,connectedPanel=true,separateFoldSides=true,materialPartition=true,continuousHinge=true,neutralContacts=true,compactLayers=true,creaseNormal=Vector2.right,creaseOffset=5.5f,side=1,contact=new Vector2(8,5)});
        var handle=s.CaptureSurface(new PaperSheet.FoldVertex(0,new Vector2(9,5)));
        Check(s.Finish(t,180),"book commit");var opening=Return(s,handle,source,out var angle);Check(s.Finish(opening,angle),"book reopen");Equivalent(source,s.Faces,"unfolded book");
        var folder=Environment.GetEnvironmentVariable("FLYEDITOR_SEQUENCE_OUT");if(!string.IsNullOrEmpty(folder)){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"unfolded-book.json"),JsonUtility.ToJson(new ResearchSave{paperFolds=s.Recipe()},true));}
        foreach(float offset in new[]{3f,5.5f,8f})foreach(int side in new[]{-1,1}){
            var point=new Vector2(offset+side,5);int anchor=-1;for(int i=0;i<s.Faces.Count;i++)if(s.Faces[i].Contains(point)){anchor=i;break;}
            Check(anchor>=0,"unfolded contact");var step=new FoldStep{anchor=anchor,flap=anchor+1,singlePanel=true,connectedPanel=true,separateFoldSides=true,materialPartition=true,continuousHinge=true,neutralContacts=true,compactLayers=true,creaseNormal=Vector2.right,creaseOffset=offset,side=side,contact=point};
            var plan=s.Plan(step);Check(plan!=null,"fully reopened paper retains obsolete leaf boundary at "+offset+"/"+side);
            float area=0;foreach(var f in plan.movingFaces)area+=PaperSheet.Area(f.points);
            Check(Mathf.Abs(area-(side<0?offset:11-offset)*11)<.003f,"unfolded selection region differs from fresh paper "+offset+"/"+side+" area="+area);
        }
    }
    static void ReopenedVariants(){
        foreach(PaperSize size in new[]{PaperSize.Square,PaperSize.A4,PaperSize.Letter})for(int r=0;r<4;r++)foreach(int sign in new[]{-1,1}){
            var s=new PaperSheet(size);var before=s.Faces;var n=r%2==0?Vector2.right:Vector2.up;if(r>=2)n=-n;
            float extent=r%2==0?s.Width:s.Height;var centre=new Vector2(s.Width,s.Height)*.5f;var contact=centre+n*(extent*.3f);
            var step=new FoldStep{anchor=0,flap=1,singlePanel=true,connectedPanel=true,separateFoldSides=true,materialPartition=true,continuousHinge=true,neutralContacts=true,compactLayers=true,creaseNormal=n,creaseOffset=Vector2.Dot(n,centre),side=1,contact=contact};
            var handle=s.CaptureSurface(new PaperSheet.FoldVertex(0,contact));Check(s.Finish(s.Plan(step),sign*180),"variant initial");var folded=s.Faces;
            var partial=Return(s,handle,before,out var fullAngle);
            Check(s.Finish(partial,fullAngle*.5f),"partial reopen");var mid=s.Faces;
            var open=Return(s,handle,before,out var remaining);Check(s.Finish(open,remaining),"final reopen");Equivalent(before,s.Faces,"multi-stage reopening");
            var replay=PaperSheet.Replay(s.Recipe());Check(replay.Count==s.Count,"reopen variant reset");Equivalent(s.Faces,replay.Faces,"reopen variant replay");
            foreach(bool transverse in new[]{false,true})foreach(float fraction in new[]{.27f,.5f,.73f})foreach(int side in new[]{-1,1}){
                var normal=transverse?new Vector2(-n.y,n.x):n;float span=Mathf.Abs(normal.x)*s.Width+Mathf.Abs(normal.y)*s.Height;
                var onLine=centre+normal*(span*(fraction-.5f));var at=onLine+normal*(side*span*.08f);int anchor=-1;Vector2 local=default;
                for(int i=0;i<s.Faces.Count;i++){local=Local(s.Faces[i],new Vector3(at.x,0,at.y));if(s.Faces[i].Contains(local)){anchor=i;break;}}Check(anchor>=0,"variant pick");
                var chart=s.Faces[anchor].material;
                var request=new FoldStep{creaseNormal=new Vector2(normal.x*chart.m00+normal.y*chart.m20,normal.x*chart.m02+normal.y*chart.m22),creaseOffset=Vector2.Dot(normal,onLine)-normal.x*chart.m03-normal.y*chart.m23};
                request.anchor=anchor;request.flap=anchor+1;request.side=side;request.contact=local;request.singlePanel=request.connectedPanel=request.separateFoldSides=request.materialPartition=request.continuousHinge=request.neutralContacts=request.compactLayers=true;
                var plan=s.Plan(request);Check(plan!=null,"variant reopened selection "+size+"/"+r+"/"+sign+"/"+transverse+"/"+fraction+"/"+side);
                float area=0;foreach(var f in plan.movingFaces)area+=PaperSheet.Area(f.points);
                float expected=s.Width*s.Height*(side<0?fraction:1-fraction);
                Check(Mathf.Abs(area-expected)<.005f,"variant wrong selected area");
                foreach(float a in new[]{30f,-30f,90f,-90f}){var next=new List<PaperSheet.Face>();Check((bool)Preview.Invoke(s,new object[]{plan,a,next}),"unfolded phantom contact");Equivalent(plan.fixedFaces,next,"fixed reopened region");poses++;}
                historySelections++;
            }
            // Undo and replay must restore the bent boundary, not retain the
            // newly opened interpretation in a cached selection.
            Check(s.Undo()&&ReferenceEquals(s.Faces,mid),"variant open undo");GeometryIfSquare(s);Check(s.Undo()&&ReferenceEquals(s.Faces,folded),"variant partial undo");
        }
    }
    static void GeometryIfSquare(PaperSheet s){if(s.Format==PaperSize.Square)Geometry(s);}
    public static void Verify(){operations=poses=historySelections=blockedRequests=0;OpenBookHistory();ReopenedVariants();Wings();Debug.Log("[SequenceConsistency] PASS operations="+operations+" poses="+poses+" reopenedSelections="+historySelections+" blockedRequests="+blockedRequests);}
    public static void Run(){try{Verify();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
