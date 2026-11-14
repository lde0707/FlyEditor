using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // Four handles may name mouth corners or the crease junctions inside that
    // same material leaf. Resolve junction hints only against a real boundary
    // and an independently valid metric support solve; never guess from overlap.
    internal static class PaperPocketIntent
    {
        private readonly struct Segment
        {
            internal readonly Vector2 a,b;
            internal Segment(Vector2 a,Vector2 b){this.a=a;this.b=b;}
        }
        private static Vector2 Material(PaperSheet.Face face,Vector2 p){var m=face.Original(p);return new Vector2(m.x,m.z);}
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static bool OnLine(Vector2 p,Vector2 origin,Vector2 axis,float tolerance)=>Mathf.Abs(Cross(axis,p-origin))<=tolerance;
        private static List<Segment> Boundary(List<PaperSheet.Face> group,float tolerance)
        {
            var edges=new List<Segment>();foreach(var f in group)for(int i=0;i<f.points.Length;i++)edges.Add(new Segment(Material(f,f.points[i]),Material(f,f.points[(i+1)%f.points.Length])));
            var result=new List<Segment>();var cuts=new List<float>();
            for(int i=0;i<edges.Count;i++){
                var edge=edges[i];var delta=edge.b-edge.a;float length=delta.magnitude;if(length<tolerance)continue;var axis=delta/length;
                cuts.Clear();cuts.Add(0);cuts.Add(length);
                for(int j=0;j<edges.Count;j++)if(i!=j){var other=edges[j];if(!OnLine(other.a,edge.a,axis,tolerance)||!OnLine(other.b,edge.a,axis,tolerance))continue;
                    float lo=Mathf.Max(0,Mathf.Min(Vector2.Dot(other.a-edge.a,axis),Vector2.Dot(other.b-edge.a,axis))),hi=Mathf.Min(length,Mathf.Max(Vector2.Dot(other.a-edge.a,axis),Vector2.Dot(other.b-edge.a,axis)));
                    if(hi-lo>tolerance){cuts.Add(lo);cuts.Add(hi);}}
                cuts.Sort();for(int k=1;k<cuts.Count;k++){
                    float lo=cuts[k-1],hi=cuts[k];if(hi-lo<=tolerance)continue;var mid=edge.a+axis*((lo+hi)*.5f);bool shared=false;
                    for(int j=0;j<edges.Count;j++)if(i!=j){var other=edges[j];if(!OnLine(other.a,edge.a,axis,tolerance)||!OnLine(other.b,edge.a,axis,tolerance))continue;
                        if(Vector2.Dot(mid-other.a,mid-other.b)<-tolerance*tolerance){shared=true;break;}}
                    if(!shared)result.Add(new Segment(edge.a+axis*lo,edge.a+axis*hi));
                }
            }return result;
        }
        internal static PaperPetalFold Resolve(PaperSheet sheet,FoldStep request)
        {
            if(!request.coordinatedSurface||!request.customFlanks||!request.regionalPull)return null;
            var basis=sheet.Faces[request.anchor];var group=new List<PaperSheet.Face>{basis};var seen=new HashSet<PaperSheet.Face>{basis};
            for(int i=0;i<group.Count;i++)foreach(var face in sheet.Faces){
                if(seen.Contains(face)||!PaperSheet.SameFrame(face,basis)||!PaperFoldTopology.MaterialChartsMatch(face,basis))continue;
                if(PaperFoldTopology.SharedEdge(group[i],face,out _,out _)){seen.Add(face);group.Add(face);}
            }
            var leftHint=basis.Original(request.pullLeft);var rightHint=basis.Original(request.pullRight);bool hasLeft=false,hasRight=false;
            foreach(var face in group){hasLeft|=face.Contains(PaperFoldTopology.Local(face,leftHint));hasRight|=face.Contains(PaperFoldTopology.Local(face,rightHint));}
            if(!hasLeft||!hasRight)return null;
            var tip=Material(basis,request.pullTip);var head=Material(basis,request.pullStop);float scale=(head-tip).magnitude;
            if(scale<.01f)return null;float tolerance=Mathf.Max(.0001f,scale*.00001f);var boundary=Boundary(group,tolerance);
            foreach(var edge in boundary){
                var delta=edge.b-edge.a;float length=delta.magnitude;if(length<tolerance)continue;var axis=delta/length;
                if(!OnLine(tip,edge.a,axis,tolerance)||Vector2.Dot(tip-edge.a,tip-edge.b)>tolerance*tolerance||Mathf.Abs(Cross(axis,head-tip))<scale*.01f)continue;
                float lo=0,hi=0;Vector2 a=tip,b=tip;
                bool changed;do{changed=false;foreach(var candidate in boundary){if(!OnLine(candidate.a,tip,axis,tolerance)||!OnLine(candidate.b,tip,axis,tolerance))continue;
                    float x=Vector2.Dot(candidate.a-tip,axis),y=Vector2.Dot(candidate.b-tip,axis);if(Mathf.Min(x,y)>hi+tolerance||Mathf.Max(x,y)<lo-tolerance)continue;
                    if(x<lo-tolerance){lo=x;a=candidate.a;changed=true;}if(y<lo-tolerance){lo=y;a=candidate.b;changed=true;}if(x>hi+tolerance){hi=x;b=candidate.a;changed=true;}if(y>hi+tolerance){hi=y;b=candidate.b;changed=true;}
                }}while(changed);
                if(lo>=-tolerance||hi<=tolerance)continue;
                var derived=request;derived.pullLeft=PaperFoldTopology.Local(basis,new Vector3(a.x,0,a.y));derived.pullRight=PaperFoldTopology.Local(basis,new Vector3(b.x,0,b.y));
                var plan=PaperPetalFold.Create(sheet,derived);
                if(plan!=null&&plan.MatchesHingeHints(leftHint,rightHint,scale*.005f)){plan.HingeHints=true;return plan;}
            }return null;
        }
    }
}
