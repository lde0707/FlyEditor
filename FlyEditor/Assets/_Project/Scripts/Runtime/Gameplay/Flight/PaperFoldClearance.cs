using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FlyEditor.Flight
{
    [Serializable]
    public struct FoldPreparation
    {
        public int anchor,side;
        public Vector2 normal;
        public float offset,degrees;
        internal FoldStep Step=>new FoldStep{anchor=anchor,flap=anchor+1,side=side,creaseNormal=normal,
            creaseOffset=offset,degrees=degrees,singlePanel=true,connectedPanel=true,continuousHinge=true,
            neutralContacts=true,compactLayers=true,opening=true};
        internal FoldPreparation(FoldStep step){anchor=step.anchor;side=step.side;normal=step.Normal;offset=step.Position;degrees=step.degrees;}
    }

    // Bounded material-hinge search, called only when a four-point petal is
    // obstructed. No model names, authored coordinates, mesh replacement or
    // changes to the contact solver are involved.
    internal static class PaperFoldClearance
    {
        private const int StateLimit=192,DepthLimit=3;
        private sealed class Node
        {
            internal PaperSheet sheet;
            internal FoldPreparation[] path;
        }
        internal sealed class Result
        {
            internal FoldStep step;
            internal int visited;
        }
        internal static Vector3 Material(PaperSheet.Face face,Vector2 p)=>face.Original(p);
        private static Vector2 Centre(PaperSheet.Face f){var c=Vector2.zero;foreach(var p in f.points)c+=p;return c/f.points.Length;}
        private static string StateKey(PaperSheet s)
        {
            var keys=new List<string>(s.Faces.Count);
            foreach(var f in s.Faces){var c=Centre(f);keys.Add(f.Original(c).ToString("F3")+f.World(c,0).ToString("F3")+
                f.frame.MultiplyVector(Vector3.up).ToString("F3")+"/"+f.layer);}
            keys.Sort(StringComparer.Ordinal);return string.Join(";",keys);
        }
        internal static bool Resolve(PaperSheet s,FoldStep request,Vector3[] handles,int direction,out PaperCornerPull fold)
        {
            fold=null;request.clearance=null;request.clearanceDirection=0;
            for(int i=0;i<s.Faces.Count;i++){
                var f=s.Faces[i];var t=PaperFoldTopology.Local(f,handles[0]);if(!f.Contains(t))continue;
                request.anchor=i;request.flap=i+1;request.pullTip=t;
                request.pullStop=PaperFoldTopology.Local(f,handles[1]);
                if(request.customFlanks){request.pullLeft=PaperFoldTopology.Local(f,handles[2]);request.pullRight=PaperFoldTopology.Local(f,handles[3]);}
                else {
                    if(!PaperFoldAssist.TryFlanks(s,new PaperSheet.FoldVertex(i,t),new PaperSheet.FoldVertex(i,request.pullStop),out var left,out var right))continue;
                    request.pullLeft=PaperFoldTopology.Local(f,s.Faces[left.anchor].Original(left.point));
                    request.pullRight=PaperFoldTopology.Local(f,s.Faces[right.anchor].Original(right.point));
                }
                bool automatic=!request.customFlanks;request.customFlanks=true;
                var p=PaperCornerPull.Create(s,request);request.customFlanks=!automatic;if(p==null||!p.UsesPetalFold)continue;
                if(!p.Pose(direction*5)||!p.TryMove(0,direction*180,out var reached)||Mathf.Abs(reached-direction*180)>.001f)continue;
                fold=p;return true;
            }return false;
        }
        internal static Vector3[] Handles(PaperSheet s,FoldStep step)
        {
            var f=s.Faces[step.anchor];return new[]{f.Original(step.pullTip),f.Original(step.pullStop),f.Original(step.pullLeft),f.Original(step.pullRight)};
        }
        internal static bool HasSpine(PaperSheet sheet,FoldStep request)
        {
            if(request.anchor<0||request.anchor>=sheet.Faces.Count||sheet.Faces.Count<2)return false;
            var basis=sheet.Faces[request.anchor];var origin=basis.World(request.pullTip,0);
            var axis=basis.World(request.pullStop,0)-origin;if(axis.sqrMagnitude<.01f)return false;axis.Normalize();
            for(int i=0;i<sheet.Faces.Count;i++)for(int j=0;j<i;j++){
                var a=sheet.Faces[i];var b=sheet.Faces[j];
                if(PaperSheet.SamePanel(a,b)||!PaperFoldTopology.SharedEdge(a,b,out var u,out var v))continue;
                var x=PaperFoldTopology.Position(a,u);var y=PaperFoldTopology.Position(a,v);
                if(Vector3.Cross(x-origin,axis).sqrMagnitude<.00001f&&Vector3.Cross(y-origin,axis).sqrMagnitude<.00001f)return true;
            }return false;
        }
        internal static Result Search(PaperSheet snapshot,FoldStep request,CancellationToken cancel)
        {
            if(request.anchor<0||request.anchor>=snapshot.Faces.Count||request.clearance!=null && request.clearance.Length>0)return null;
            // Solve in a rigid local frame. World translations/rotations must not
            // perturb the sub-thickness ordering of coincident paper layers.
            snapshot=snapshot.LocalPlanningCopy(snapshot.Faces[request.anchor].frame);
            var handles=Handles(snapshot,request);var basis=snapshot.Faces[request.anchor];
            var origin=basis.World(request.pullTip,0);var axis=basis.World(request.pullStop,0)-origin;
            if(axis.sqrMagnitude<.01f)return null;axis.Normalize();
            var queue=new Queue<Node>();var seen=new HashSet<string>();
            queue.Enqueue(new Node{sheet=snapshot,path=Array.Empty<FoldPreparation>()});seen.Add(StateKey(snapshot));int visits=0;
            while(queue.Count>0&&visits++<StateLimit&&!cancel.IsCancellationRequested){
                var node=queue.Dequeue();var s=node.sheet;
                if(node.path.Length>0)foreach(int direction in new[]{1,-1}){
                    if(!Resolve(s,request,handles,direction,out var final))continue;
                    if(!request.customFlanks){
                        var f=s.Faces[final.Step.anchor];
                        request.pullLeft=PaperFoldTopology.Local(basis,f.Original(final.Step.pullLeft));
                        request.pullRight=PaperFoldTopology.Local(basis,f.Original(final.Step.pullRight));
                        request.customFlanks=true;
                    }
                    request.clearance=node.path;request.clearanceDirection=direction;
                    return new Result{step=request,visited=visits};
                }
                if(node.path.Length>=DepthLimit||s.Count>=s.FoldLimit-1)continue;
                var groups=new HashSet<string>();
                for(int i=0;i<s.Faces.Count&&!cancel.IsCancellationRequested;i++){
                    var f=s.Faces[i];for(int e=0;e<f.points.Length;e++){
                        var a=f.points[e];var b=f.points[(e+1)%f.points.Length];var d=b-a;if(d.sqrMagnitude<.001f)continue;
                        // Only already present edges on the selected pocket's spine.
                        // Turning other creases would change the user's chosen shape.
                        if(Vector3.Cross(f.World(a,0)-origin,axis).sqrMagnitude>.00001f||Vector3.Cross(f.World(b,0)-origin,axis).sqrMagnitude>.00001f)continue;
                        var n=new Vector2(-d.y,d.x).normalized;
                        var st=new FoldPreparation{anchor=i,side=Vector2.Dot(n,Centre(f)-a)>0?1:-1,normal=n,offset=Vector2.Dot(n,a)}.Step;
                        var turn=s.Plan(st);if(turn==null||turn.movingFaces.Count>=s.Faces.Count||turn.movingFaces.Count+turn.fixedFaces.Count!=s.Faces.Count)continue;
                        var ids=new List<string>();foreach(var moved in turn.movingFaces)ids.Add(moved.Original(Centre(moved)).ToString("F3"));ids.Sort(StringComparer.Ordinal);
                        if(!groups.Add(string.Join(";",ids)))continue;
                        foreach(float angle in new[]{90f,-90f,180f,-180f}){
                            if(cancel.IsCancellationRequested)return null;
                            if(Mathf.Abs(turn.Constrain(angle)-angle)>.001f)continue;
                            st.degrees=angle;var next=s.PlanningCopy();if(!next.ApplyStep(st)||next.Faces.Count!=s.Faces.Count)continue;
                            if(!seen.Add(StateKey(next)))continue;
                            var path=new FoldPreparation[node.path.Length+1];Array.Copy(node.path,path,node.path.Length);path[path.Length-1]=new FoldPreparation(st);
                            queue.Enqueue(new Node{sheet=next,path=path});
                        }
                    }
                }
            }return null;
        }
    }
}
