using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // An explicit, bounded suggestion for the two flank handles. It only uses
    // real corners of the picked panel and its material hinge neighbours.
    // It never commits a fold.
    internal static class PaperFoldAssist
    {
        private struct Corner { public PaperSheet.FoldVertex vertex; public Vector2 local; public float reach; }
        private const int CandidateLimit=3;
        // Pick a mouth edge of a closed pocket. Its neighbouring material seam
        // supplies the spine; flank inference supplies the two leaves.
        // Coincident screen positions never establish material connectivity.
        public static bool TryEdgePocket(PaperSheet sheet,int anchor,int edge,out PaperCornerPull plan,out int direction)
        {
            plan=null;direction=0;
            if(anchor<0||anchor>=sheet.Faces.Count||sheet.Count>=sheet.FoldLimit)return false;
            var face=sheet.Faces[anchor];if(edge<0||edge>=face.points.Length)return false;
            var a=face.Original(face.points[edge]);var b=face.Original(face.points[(edge+1)%face.points.Length]);
            foreach(var other in sheet.Faces){
                if(PaperSheet.SamePanel(face,other)||!PaperSheet.SameFrame(face,other)||
                    !PaperFoldTopology.SharedEdge(face,other,out var u,out var v))continue;
                bool atMouth=OnSegment(u,a,b),endMouth=OnSegment(v,a,b);
                if(atMouth==endMouth)continue;
                var tip=PaperFoldTopology.Local(face,atMouth?u:v);var head=PaperFoldTopology.Local(face,atMouth?v:u);
                if(!TryFlanks(sheet,new PaperSheet.FoldVertex(anchor,tip),new PaperSheet.FoldVertex(anchor,head),out var left,out var right))continue;
                var l=PaperFoldTopology.Local(face,sheet.Faces[left.anchor].Original(left.point));
                var r=PaperFoldTopology.Local(face,sheet.Faces[right.anchor].Original(right.point));
                var step=new FoldStep{anchor=anchor,flap=anchor+1,cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,
                    exactSquash=true,regionalPull=true,coupledHinges=true,coordinatedCreases=true,adaptiveCreases=true,
                    connectedPetalBacking=true,partialPetalBacking=true,compactLayers=true,localLayerPacking=true,continuousHinge=true,neutralContacts=true,
                    pullTip=tip,pullStop=head,pullLeft=l,pullRight=r};
                var candidate=PaperCornerPull.Create(sheet,step);
                if(candidate==null||(!candidate.UsesCoupledHinges&&!candidate.UsesExactSquash))continue;
                int sign=candidate.Pose(5)?1:candidate.Pose(-5)?-1:0;
                if(sign==0)continue;
                candidate.Pose(0);plan=candidate;direction=sign;return true;
            }
            return false;
        }
        private static bool OnSegment(Vector3 p,Vector3 a,Vector3 b)
        {
            var d=b-a;if(d.sqrMagnitude<=.000001f)return false;
            float t=Vector3.Dot(p-a,d)/d.sqrMagnitude;
            return t>=-.0001f&&t<=1.0001f&&Vector3.Distance(p,a+d*t)<.001f;
        }
        public static bool TryFlanks(PaperSheet sheet,PaperSheet.FoldVertex first,PaperSheet.FoldVertex second,
            out PaperSheet.FoldVertex left,out PaperSheet.FoldVertex right)
        {
            left=right=default;
            if(first.anchor<0||second.anchor<0||first.anchor>=sheet.Faces.Count||second.anchor>=sheet.Faces.Count||sheet.Count>=sheet.FoldLimit)return false;
            var basis=sheet.Faces[first.anchor];var other=sheet.Faces[second.anchor];
            if(!basis.Contains(first.point)||!other.Contains(second.point))return false;
            var mapped=basis.material.inverse.MultiplyPoint3x4(other.Original(second.point));
            var head=new Vector2(mapped.x,mapped.z);var tip=first.point;var axis=head-tip;float length=axis.magnitude;
            if(!float.IsFinite(length)||length<.05f)return false;
            // A point from an unrelated stacked leaf is not a shared handle.
            if(!sheet.ContainsPanelPoint(basis,head))return false;
            var positive=new List<Corner>(CandidateLimit);var negative=new List<Corner>(CandidateLimit);
            for(int i=0;i<sheet.Faces.Count;i++){
                var face=sheet.Faces[i];if(!PaperSheet.SamePanel(face,basis) && !AxisNeighbour(basis,face,tip,head))continue;
                foreach(var corner in face.points){
                    var local=basis.material.inverse.MultiplyPoint3x4(face.Original(corner));var point=new Vector2(local.x,local.z);
                    float along=Vector2.Dot(point-tip,axis)/(length*length);
                    float across=(axis.x*(point.y-tip.y)-axis.y*(point.x-tip.x))/length;
                    if(along<-.001f||along>1.001f||Mathf.Abs(across)<.02f)continue;
                    Add(across>0?positive:negative,new Corner{vertex=new PaperSheet.FoldVertex(i,corner),local=point,reach=Mathf.Abs(across)});
                }
            }
            bool blockedPetal=false;PaperSheet.FoldVertex blockedLeft=default,blockedRight=default;
            foreach(var a in positive)foreach(var b in negative){
                var step=new FoldStep{anchor=first.anchor,flap=first.anchor+1,cornerPull=true,customFlanks=true,flexible=true,closeFlat=true,
                    continuousHinge=true,neutralContacts=true,exactSquash=true,regionalPull=true,coupledHinges=true,coordinatedCreases=true,adaptiveCreases=true,connectedPetalBacking=true,partialPetalBacking=true,compactLayers=true,
                    pullTip=tip,pullStop=head,pullLeft=a.local,pullRight=b.local};
                var plan=PaperCornerPull.Create(sheet,step);
                if(plan==null)continue;
                if(!plan.Pose(5)&&!plan.Pose(-5)){
                    if(!blockedPetal&&plan.UsesPetalFold){blockedPetal=true;blockedLeft=a.vertex;blockedRight=b.vertex;}
                    continue;
                }
                left=a.vertex;right=b.vertex;return true;
            }
            if(blockedPetal){left=blockedLeft;right=blockedRight;return true;}
            return false;
        }
        private static bool AxisNeighbour(PaperSheet.Face basis,PaperSheet.Face other,Vector2 tip,Vector2 head)
        {
            var a=basis.Original(tip);var b=basis.Original(head);
            if(!other.Contains(PaperFoldTopology.Local(other,a))||!other.Contains(PaperFoldTopology.Local(other,b)))return false;
            if(!PaperFoldTopology.SharedEdge(basis,other,out var u,out var v))return false;
            var axis=(b-a).normalized;
            return Vector3.Cross(u-a,axis).sqrMagnitude<.000001f && Vector3.Cross(v-a,axis).sqrMagnitude<.000001f;
        }
        private static void Add(List<Corner> list,Corner candidate)
        {
            foreach(var old in list)if((old.local-candidate.local).sqrMagnitude<.000001f)return;
            int at=0;while(at<list.Count&&list[at].reach>=candidate.reach)at++;
            if(at>=CandidateLimit)return;list.Insert(at,candidate);if(list.Count>CandidateLimit)list.RemoveAt(CandidateLimit);
        }
    }
}
