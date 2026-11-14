using System.Collections.Generic;
using UnityEngine;
namespace FlyEditor.Flight
{
    // Recover the layer order at flat contact from the preceding rigid pose.
    // Ordering constraints are local to overlapping polygons, not face indices.
    internal static class PaperFlatStackOrder
    {
        public static bool Apply(List<PaperSheet.Face> flat,IReadOnlyList<PaperSheet.Face> before)
        {
            if(flat.Count!=before.Count)return false;
            var assigned=new bool[flat.Count];
            for(int i=0;i<flat.Count;i++){if(assigned[i])continue;var a=new List<PaperSheet.Face>();var b=new List<PaperSheet.Face>();var ids=new List<int>();for(int j=i;j<flat.Count;j++)if(!assigned[j]&&PaperSheet.SameFrame(flat[i],flat[j])){assigned[j]=true;ids.Add(j);a.Add(flat[j]);b.Add(before[j]);}if(!ApplyGroup(a,b))return false;for(int j=0;j<ids.Count;j++)flat[ids[j]]=a[j];}return true;
        }
        private static bool ApplyGroup(List<PaperSheet.Face> flat,IReadOnlyList<PaperSheet.Face> before)
        {
            if(flat.Count!=before.Count||flat.Count==0)return false;
            var frame=flat[0].frame;foreach(var f in flat)if(!PaperSheet.SameFrame(f,flat[0]))return false;
            int count=flat.Count;var edges=new List<int>[count];var incoming=new int[count];var level=new int[count];
            for(int i=0;i<count;i++)edges[i]=new List<int>();
            for(int i=0;i<count;i++)for(int j=0;j<i;j++){
                var part=flat[i];var b=flat[j];
                for(int k=0;k<b.points.Length&&part!=null;k++){
                    var a=b.points[k];var d=b.points[(k+1)%b.points.Length]-a;
                    var n=new Vector2(-d.y,d.x)*(b.back?-1:1);
                    part=PaperSheet.Clip(part,new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,a)},1);
                }
                if(part==null||PaperSheet.Area(part.points)<.0001f)continue;
                Vector2 sample=Vector2.zero;foreach(var v in part.points)sample+=v;sample/=part.points.Length;
                float difference=Height(flat[i],before[i],sample,frame)-Height(b,before[j],sample,frame);
                if(Mathf.Abs(difference)<.00000001f){if(before[i].layer==before[j].layer)continue;difference=before[i].layer-before[j].layer;}
                int lower=difference>0?j:i,higher=difference>0?i:j;
                edges[lower].Add(higher);incoming[higher]++;
            }
            var queue=new Queue<int>();for(int i=0;i<count;i++)if(incoming[i]==0)queue.Enqueue(i);
            int visited=0;while(queue.Count>0){int a=queue.Dequeue();visited++;foreach(int b in edges[a]){level[b]=Mathf.Max(level[b],level[a]+1);if(--incoming[b]==0)queue.Enqueue(b);}}
            if(visited!=count)return false;
            for(int i=0;i<count;i++){var f=flat[i];flat[i]=new PaperSheet.Face(f.points,level[i],f.back,f.frame,f.material,f);}return true;
        }
        private static float Height(PaperSheet.Face flat,PaperSheet.Face near,Vector2 point,Matrix4x4 frame)
        {
            var local=near.material.inverse.MultiplyPoint3x4(flat.Original(point));
            // A tiny shell bias breaks coincident rigid-group ties without
            // overpowering the geometric order of the closing panels.
            local.y=(near.layer+.5f)*.0000001f;
            return frame.inverse.MultiplyPoint3x4(near.frame.MultiplyPoint3x4(local)).y;
        }
    }
}
