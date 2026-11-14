using UnityEngine;
namespace FlyEditor.Flight
{
    // Reflect a chosen edge onto a reference line. No diagram, stage, camera
    // direction or face index participates in the crease construction.
    internal static class PaperEdgeAlignment
    {
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        internal static bool TryAxis(Vector2 a,Vector2 b,Vector2 c,Vector2 d,out Vector2 normal,out float offset)
        {
            normal=default;offset=0;
            if(!float.IsFinite(a.x)||!float.IsFinite(a.y)||!float.IsFinite(b.x)||!float.IsFinite(b.y)||
                !float.IsFinite(c.x)||!float.IsFinite(c.y)||!float.IsFinite(d.x)||!float.IsFinite(d.y))return false;
            var u=b-a;var v=d-c;
            if(u.sqrMagnitude<.000001f || v.sqrMagnitude<.000001f)return false;
            u.Normalize();v.Normalize();float cross=Cross(u,v);
            if(Mathf.Abs(cross)<.0001f){
                normal=new Vector2(-u.y,u.x);
                float source=Vector2.Dot(normal,a),target=Vector2.Dot(normal,c);
                if(Mathf.Abs(source-target)<.0001f)return false;
                offset=(source+target)*.5f;return true;
            }
            var intersection=a+u*(Cross(c-a,v)/cross);
            // Use the clicked segments' rays, so reversing either edge's endpoint
            // order does not choose the other angle bisector.
            var sourceRay=(a+b)*.5f-intersection;
            var targetRay=(c+d)*.5f-intersection;
            if(sourceRay.sqrMagnitude<.000001f || targetRay.sqrMagnitude<.000001f)return false;
            var axis=sourceRay.normalized+targetRay.normalized;
            if(axis.sqrMagnitude<.000001f)return false;
            axis.Normalize();normal=new Vector2(-axis.y,axis.x);offset=Vector2.Dot(normal,intersection);
            return true;
        }
        internal static PaperSheet.Turn Plan(PaperSheet sheet,int source,Vector2 a,Vector2 b,int target,Vector2 c,Vector2 d,Vector2 contact)
        {
            if(source<0 || target<0 || source>=sheet.Faces.Count || target>=sheet.Faces.Count)return null;
            var face=sheet.Faces[source];var reference=sheet.Faces[target];
            // Different physical planes need a coordinated fold, not projection
            // of a screen line onto the wrong layer.
            if(!PaperSheet.SameFrame(face,reference) || !TryAxis(a,b,c,d,out var n,out float offset))return null;
            float distance=Vector2.Dot(n,(a+b)*.5f)-offset;
            if(Mathf.Abs(distance)<.0001f)return null;
            var step=new FoldStep{anchor=source,flap=source+1,creaseNormal=n,creaseOffset=offset,side=distance>0?1:-1,
                singlePanel=true,connectedPanel=true,separateFoldSides=true,materialPartition=true,vertexJoints=true,preserveSlivers=true,continuousHinge=true,neutralContacts=true,compactLayers=true,contact=contact};
            if(step.Distance(contact)*step.side<=0)return null;
            return sheet.Plan(step);
        }
    }
}
