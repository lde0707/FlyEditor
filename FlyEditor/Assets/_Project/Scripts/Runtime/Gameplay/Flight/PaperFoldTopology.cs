using System.Collections.Generic;
using UnityEngine;
namespace FlyEditor.Flight
{
    // Material-space adjacency is independent of screen overlap and layer number.
    internal static class PaperFoldTopology
    {
        public static bool SharedEdge(PaperSheet.Face a,PaperSheet.Face b,out Vector3 start,out Vector3 end)
        {
            start=end=default;
            for(int i=0;i<a.points.Length;i++){
                var p=a.Original(a.points[i]);var edge=a.Original(a.points[(i+1)%a.points.Length])-p;float len=edge.magnitude;
                if(len<.0001f)continue;var axis=edge/len;
                for(int j=0;j<b.points.Length;j++){
                    var u=b.Original(b.points[j])-p;var v=b.Original(b.points[(j+1)%b.points.Length])-p;
                    if(Vector3.Cross(axis,u).sqrMagnitude>1e-8f||Vector3.Cross(axis,v).sqrMagnitude>1e-8f)continue;
                    float lo=Mathf.Max(0,Mathf.Min(Vector3.Dot(u,axis),Vector3.Dot(v,axis))),hi=Mathf.Min(len,Mathf.Max(Vector3.Dot(u,axis),Vector3.Dot(v,axis)));
                    if(hi-lo<.0001f)continue;start=p+lo*axis;end=p+hi*axis;return true;
                }
            }return false;
        }
        public static Vector2 Local(PaperSheet.Face face,Vector3 original)
        {var p=face.material.inverse.MultiplyPoint3x4(original);return new Vector2(p.x,p.z);}
        public static Vector3 Position(PaperSheet.Face face,Vector3 original)=>face.World(Local(face,original),0);
        public static FoldStep MaterialLine(PaperSheet.Face f,Vector3 point,Vector3 normal)
        {
            var m=f.material;
            return new FoldStep{creaseNormal=new Vector2(normal.x*m.m00+normal.z*m.m20,normal.x*m.m02+normal.z*m.m22),creaseOffset=Vector3.Dot(normal,point)-normal.x*m.m03-normal.z*m.m23};
        }
        public static FoldStep WorldLine(PaperSheet.Face f,Vector3 point,Vector3 normal)
        {
            var m=f.frame;
            return new FoldStep{creaseNormal=new Vector2(Vector3.Dot(normal,m.MultiplyVector(Vector3.right)),Vector3.Dot(normal,m.MultiplyVector(Vector3.forward))),creaseOffset=Vector3.Dot(normal,point-m.MultiplyPoint3x4(Vector3.zero))};
        }
    }
}
