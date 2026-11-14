using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // Screen-area picking in orthographic and perspective views. Subtract nearer polygons instead of
    // sampling centroids, which misses a partially exposed ply or a thin window.
    internal static class PaperBoxSelection
    {
        private sealed class Projection
        {
            internal List<Vector3> polygon;
            internal Vector3 depth;
            internal float Depth(Vector3 p)=>depth.x*p.x+depth.y*p.y+depth.z;
        }
        internal static void Collect(PaperSheet sheet,Camera camera,Vector3 origin,Rect viewport,Rect box,
            FoldStep line,bool whole,List<int> result)
        {
            result.Clear();var views=new Projection[sheet.Faces.Count];
            for(int i=0;i<views.Length;i++)views[i]=Project(sheet.Faces[i],camera,origin,viewport);
            var basis=sheet.Faces[line.anchor];var normal=basis.frame.MultiplyVector(new Vector3(line.Normal.x,0,line.Normal.y));
            var at=basis.World(line.Normal*(line.Position/line.Normal.sqrMagnitude),0);
            for(int i=0;i<views.Length;i++){
                if(views[i]==null)continue;var face=sheet.Faces[i];
                var local=PaperFoldTopology.WorldLine(face,at,normal);local.side=line.side;local.preserveSlivers=true;
                var chosen=PaperSheet.Clip(face,local,line.side);if(chosen==null)continue;
                var polygon=Project(chosen,camera,origin,viewport)?.polygon;if(polygon==null)continue;
                polygon=Half(polygon,Vector2.right,box.xMin);polygon=Half(polygon,Vector2.left,-box.xMax);
                polygon=Half(polygon,Vector2.up,box.yMin);polygon=Half(polygon,Vector2.down,-box.yMax);
                if(!HasArea(polygon))continue;
                var fragments=new List<List<Vector3>>{polygon};
                if(!whole)for(int j=0;j<views.Length && fragments.Count>0;j++){
                    if(i==j || views[j]==null)continue;
                    // Keep only the portion of the occluder which is nearer at
                    // this pixel. Depth (or negative reciprocal depth) is affine in screen space.
                    var other=views[j];var delta=views[i].depth-other.depth;
                    // Equal depths use the same source-index tie break as ray picking.
                    float epsilon=camera.orthographic?.000001f:.000000001f;
                    float tolerance=j<i?-epsilon:epsilon;
                    var mask=Half(other.polygon,new Vector2(delta.x,delta.y),tolerance-delta.z);
                    if(!HasArea(mask))continue;
                    var next=new List<List<Vector3>>();
                    foreach(var fragment in fragments)Subtract(fragment,mask,next);
                    fragments=next;
                }
                foreach(var fragment in fragments)if(HasArea(fragment)){result.Add(i);break;}
            }
        }
        private static Projection Project(PaperSheet.Face face,Camera camera,Vector3 origin,Rect viewport)
        {
            Vector2 centre=Vector2.zero;foreach(var p in face.points)centre+=p;centre/=face.points.Length;
            var direction=camera.orthographic?camera.transform.forward:(origin+face.World(centre,0)-camera.transform.position).normalized;
            float facing=Vector3.Dot(direction,face.frame.MultiplyVector(Vector3.up));
            if(Mathf.Abs(facing)<.000001f)return null;
            float h=(face.layer+(facing<0?1:0))*PaperSheet.Thickness;
            // Clip at the camera near plane before dividing by depth. Portions
            // behind the eye must not wrap across the screen and become pickable.
            var world=new List<Vector3>();foreach(var p in face.points)world.Add(origin+face.World(p,h));
            var clipped=new List<Vector3>();var previous=world[world.Count-1];
            float Depth(Vector3 p)=>Vector3.Dot(camera.transform.forward,p-camera.transform.position)-camera.nearClipPlane;
            float d=Depth(previous);
            foreach(var p in world){float e=Depth(p);if((d>=0)!=(e>=0))clipped.Add(Vector3.LerpUnclamped(previous,p,d/(d-e)));if(e>=0)clipped.Add(p);previous=p;d=e;}
            if(clipped.Count<3)return null;
            var polygon=new List<Vector3>();foreach(var p in clipped){var v=camera.WorldToViewportPoint(p);polygon.Add(new Vector3(viewport.x+v.x*viewport.width,viewport.y+(1-v.y)*viewport.height,camera.orthographic?v.z:-1f/Mathf.Max(camera.nearClipPlane,v.z)));}
            // Reciprocal depth is affine in screen space for a perspective
            // plane. Its negative, like ortho depth, grows with distance.
            var a=polygon[0];Vector3 u=default,vv=default;float determinant=0;
            for(int i=1;i+1<polygon.Count;i++){u=polygon[i]-a;vv=polygon[i+1]-a;determinant=u.x*vv.y-u.y*vv.x;if(Mathf.Abs(determinant)>.000001f)break;}
            if(Mathf.Abs(determinant)<.000001f)return null;
            float dx=(u.z*vv.y-vv.z*u.y)/determinant,dy=(u.x*vv.z-vv.x*u.z)/determinant;
            return new Projection{polygon=polygon,depth=new Vector3(dx,dy,a.z-dx*a.x-dy*a.y)};
        }
        private static List<Vector3> Half(List<Vector3> polygon,Vector2 n,float at)
        {
            var result=new List<Vector3>();if(polygon.Count==0)return result;
            var previous=polygon[polygon.Count-1];double d=(double)n.x*previous.x+(double)n.y*previous.y-at;
            foreach(var p in polygon){double e=(double)n.x*p.x+(double)n.y*p.y-at;
                if((d>=0)!=(e>=0))result.Add(Vector3.LerpUnclamped(previous,p,(float)(d/(d-e))));
                if(e>=0)result.Add(p);previous=p;d=e;
            }return result;
        }
        private static bool HasArea(List<Vector3> p)
        {
            if(p.Count<3)return false;double area=0;var a=p[0];
            for(int i=1;i+1<p.Count;i++)area+=((double)p[i].x-a.x)*(p[i+1].y-a.y)-((double)p[i].y-a.y)*(p[i+1].x-a.x);
            return System.Math.Abs(area)>.00002; // numerical screen-area tolerance, not a sample grid
        }
        private static void Subtract(List<Vector3> polygon,List<Vector3> mask,List<List<Vector3>> output)
        {
            double winding=0;for(int i=0;i<mask.Count;i++){var a=mask[i];var b=mask[(i+1)%mask.Count];winding+=(double)a.x*b.y-(double)b.x*a.y;}
            float sign=winding>=0?1:-1;var inside=polygon;
            for(int i=0;i<mask.Count && HasArea(inside);i++){
                var a=mask[i];var b=mask[(i+1)%mask.Count];var n=new Vector2(a.y-b.y,b.x-a.x)*sign;float at=n.x*a.x+n.y*a.y;
                var outside=Half(inside,-n,-at);if(HasArea(outside))output.Add(outside);
                inside=Half(inside,n,at);
            }
        }
    }
}
