using System.Collections.Generic;
using UnityEngine;
namespace FlyEditor.Flight
{
    // Coordinated radial collapse. Four handles define a centre and one square
    // edge; no model identity or stored crease diagram is involved.
    internal sealed class PaperRadialGather
    {
        private struct Seam { public int a,b;public Vector3 start,end; }
        private readonly List<Seam> seams=new List<Seam>();
        private struct Piece { public PaperSheet.Face face;public int sector; }
        private readonly PaperSheet sheet;
        private readonly IReadOnlyList<PaperSheet.Face> source;
        private readonly PaperSheet.Face basis;
        private readonly List<Piece> pieces=new List<Piece>();
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>(),trial=new List<PaperSheet.Face>();
        private readonly Vector3[] rest=new Vector3[8],posed=new Vector3[8];
        private readonly List<PaperSheet.Face> middle=new List<PaperSheet.Face>();
        private readonly List<int> layers=new List<int>();
        private readonly Matrix4x4[] maps=new Matrix4x4[8];
        private static readonly int[] Depth={1,0,0,1,2,3,3,2};
        private Vector2 centre,u,v;
        private float radius,last=float.NaN;
        public IReadOnlyList<PaperSheet.Face> Preview=>preview;
        public Vector3 Tip {get;private set;}
        public Vector3 Stop=>basis.World(centre,0);
        private PaperRadialGather(PaperSheet sheet,PaperSheet.Face basis){this.sheet=sheet;source=sheet.Faces;this.basis=basis;}
        private Vector2 Locate(Vector2 point)
        {
            var original=basis.Original(point);foreach(var f in source){var local=PaperFoldTopology.Local(f,original);if(f.Contains(local))return local;}return new Vector2(float.NaN,float.NaN);
        }
        public static PaperRadialGather Create(PaperSheet sheet,FoldStep step)
        {
            if(!step.coordinatedCreases||!step.customFlanks||sheet.Faces.Count>1&&step.flap!=0)return null;
            var basis=sheet.Faces[step.anchor];foreach(var f in sheet.Faces)if(!PaperSheet.SameFrame(f,basis))return null;
            var plan=new PaperRadialGather(sheet,basis);foreach(var f in sheet.Faces)if(!plan.layers.Contains(f.layer))plan.layers.Add(f.layer);plan.layers.Sort();var c=plan.Locate(step.pullStop);var t=plan.Locate(step.pullTip);var l=plan.Locate(step.pullLeft);var r=plan.Locate(step.pullRight);
            if(!float.IsFinite(c.x)||!float.IsFinite(t.x)||!float.IsFinite(l.x)||!float.IsFinite(r.x))return null;
            plan.radius=(r-l).magnitude*.5f;if(plan.radius<.1f)return null;
            plan.v=(r-l).normalized;plan.u=new Vector2(plan.v.y,-plan.v.x);
            if(Vector2.Dot(plan.u,t-c)<0)plan.u=-plan.u;
            var edgeMid=(l+r)*.5f;var fittedCentre=edgeMid-plan.u*plan.radius;
            // Free clicks are not exact coordinates. Fit a compatible square only
            // within 2% of its radius; broader layouts belong to other solvers.
            float tolerance=Mathf.Max(.002f,plan.radius*.02f);
            if((t-edgeMid).magnitude>tolerance||(c-fittedCentre).magnitude>tolerance)return null;
            c=fittedCentre;plan.centre=c;
            // All radial wedges have to exist; a boundary fan uses a different solver.
            foreach(var a in new[]{plan.u,plan.v,-plan.u,-plan.v}){bool contains=false;foreach(var f in sheet.Faces)contains|=f.Contains(c+a*plan.radius);if(!contains)return null;}
            for(int i=0;i<8;i++){float a=i*45*Mathf.Deg2Rad;float length=plan.radius*(i%2==0?1:Mathf.Sqrt(2));plan.rest[i]=new Vector3(Mathf.Cos(a)*length,0,Mathf.Sin(a)*length);}
            var directions=new[]{plan.u,plan.v,plan.u+plan.v,plan.u-plan.v};
            foreach(var f in sheet.Faces){
                var parts=new List<PaperSheet.Face>{f};
                foreach(var axis in directions){var n=new Vector2(-axis.y,axis.x);var line=new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,c)};var next=new List<PaperSheet.Face>();
                    foreach(var part in parts){var a=PaperSheet.Clip(part,line,1);var b=PaperSheet.Clip(part,line,-1);if(a!=null)next.Add(a);if(b!=null)next.Add(b);}parts=next;}
                foreach(var part in parts){Vector2 mid=Vector2.zero;foreach(var p in part.points)mid+=p;mid=mid/part.points.Length-c;
                    float angle=Mathf.Atan2(Vector2.Dot(mid,plan.v),Vector2.Dot(mid,plan.u))*Mathf.Rad2Deg;if(angle<0)angle+=360;
                    plan.pieces.Add(new Piece{face=part,sector=Mathf.Min(7,(int)(angle/45))});}
            }
            if(plan.pieces.Count>256)return null;
            for(int i=0;i<plan.pieces.Count;i++)for(int j=0;j<i;j++)
                if(PaperFoldTopology.SharedEdge(plan.pieces[i].face,plan.pieces[j].face,out var start,out var end))
                    plan.seams.Add(new Seam{a=i,b=j,start=start,end=end});
            return plan;
        }
        public void MarkGuides(List<PaperSheet.Guide> guides)
        {
            guides.Clear();foreach(var axis in new[]{u,v,u+v,u-v}){var n=new Vector2(-axis.y,axis.x);var line=new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,centre)};
                foreach(var f in source)if(PaperSheet.Intersection(f,line,out var a,out var b))guides.Add(new PaperSheet.Guide{face=f,layer=f.layer,line=line,a=a,b=b});}
        }
        private static Quaternion Basis(Vector3 a,Vector3 b)=>Quaternion.LookRotation(a,Vector3.Cross(a,b));
        private Vector3 Point(Vector3 p)=>new Vector3(centre.x+u.x*p.x+v.x*p.z,p.y,centre.y+u.y*p.x+v.y*p.z);
        public bool Pose(float angle)
        {
            if(sheet.Faces!=source||!float.IsFinite(angle)||Mathf.Abs(angle)>180)return false;
            if(angle==last)return true;if(angle==0){preview.Clear();preview.AddRange(source);Tip=basis.World(centre+u*radius,0);last=angle;return true;}
            float a=Mathf.Min(Mathf.Abs(angle),90)*Mathf.Deg2Rad,b=Mathf.Max(0,Mathf.Abs(angle)-90)*Mathf.Deg2Rad,sign=Mathf.Sign(angle);
            float x=radius*Mathf.Cos(a),y=radius*Mathf.Sin(a),z=radius;
            if(b>0){z=radius*Mathf.Cos(b)/(1+Mathf.Sin(b));x=radius*Mathf.Sqrt(Mathf.Max(0,1-z*z/(radius*radius)));}
            posed[0]=new Vector3(b>0?0:x,sign*y,0);posed[4]=new Vector3(b>0?0:-x,sign*y,0);
            posed[2]=new Vector3(0,sign*radius*Mathf.Sin(b),radius*Mathf.Cos(b));posed[6]=new Vector3(0,sign*radius*Mathf.Sin(b),-radius*Mathf.Cos(b));
            posed[1]=new Vector3(x,sign*y,z);posed[3]=new Vector3(-x,sign*y,z);posed[5]=new Vector3(-x,sign*y,-z);posed[7]=new Vector3(x,sign*y,-z);
            var settle=Quaternion.AngleAxis(sign*b*Mathf.Rad2Deg,Vector3.right);for(int i=0;i<8;i++)posed[i]=settle*posed[i];
            var origin=Point(Vector3.zero);
            for(int i=0;i<8;i++){int j=(i+1)%8;var ra=Point(rest[i])-origin;var rb=Point(rest[j])-origin;var pa=Point(posed[i])-origin;var pb=Point(posed[j])-origin;
                if(Mathf.Abs((ra-rb).magnitude-(pa-pb).magnitude)>.0005f)return false;
                var q=Basis(pa,pb)*Quaternion.Inverse(Basis(ra,rb));maps[i]=Matrix4x4.TRS(origin-q*origin,q,Vector3.one);}
            trial.Clear();bool closed=Mathf.Abs(angle)==180;
            foreach(var piece in pieces){var f=piece.face;var map=maps[piece.sector];int rank=layers.BinarySearch(f.layer);bool up=map.MultiplyVector(Vector3.up).y>=0;
                int group=angle>0?Depth[piece.sector]:3-Depth[piece.sector];
                int layer=group*layers.Count+(up?rank:layers.Count-1-rank);
                if(closed){var points=new Vector2[f.points.Length];for(int i=0;i<points.Length;i++){var p=map.MultiplyPoint3x4(new Vector3(f.points[i].x,0,f.points[i].y));if(Mathf.Abs(p.y)>.001f)return false;points[i]=new Vector2(p.x,p.z);}
                    var chart=map;chart.m01=chart.m10=chart.m12=chart.m21=chart.m13=0;chart.m11=1;
                    bool back=f.back^(chart.m00*chart.m22-chart.m02*chart.m20<0);trial.Add(new PaperSheet.Face(points,layer,back,basis.frame,f.material*chart.inverse));
                }else trial.Add(new PaperSheet.Face(f.points,f.layer,f.back,basis.frame*map,f.material));
            }
            // All plies are explicitly selected and follow the same crease field.
            // Solve the paper midsurfaces; shell thickness at a mathematical
            // zero-radius hinge otherwise reports a false crossing of its plies.
            // Finished shell order is assigned per wedge, reversing the packet
            // whenever that wedge turns over. This does not affect unselected plies.
            middle.Clear();foreach(var f in trial)middle.Add(new PaperSheet.Face(f.points,0,f.back,f.frame*Matrix4x4.Translate(-Vector3.up*(PaperSheet.Thickness*.5f)),f.material));
            for(int i=0;i<middle.Count;i++)for(int j=0;j<i;j++){var p=middle[i];var q=middle[j];if(PaperSheet.Turn.Intersects(p,q,q.frame.inverse*p.frame)||PaperSheet.Turn.Intersects(q,p,p.frame.inverse*q.frame))return false;}
            foreach(var seam in seams){
                var p=trial[seam.a];var q=trial[seam.b];
                if(Vector3.Distance(PaperFoldTopology.Position(p,seam.start),PaperFoldTopology.Position(q,seam.start))>.002f ||
                   Vector3.Distance(PaperFoldTopology.Position(p,seam.end),PaperFoldTopology.Position(q,seam.end))>.002f)return false;
            }
            preview.Clear();preview.AddRange(trial);Tip=basis.frame.MultiplyPoint3x4(Point(posed[0]));last=angle;return true;
        }
    }
}
