using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    public enum CreaseFamily { Horizontal, Vertical, Rising, Falling }

    public enum MirrorAxis { Off, Horizontal, Vertical }

    public enum PaperSize { Square, A4, Letter }

    [Serializable]
    public struct FoldStep
    {
        public CreaseFamily family;
        public int offset, side;
        // Zero is the legacy save value: those folds were always 180 degrees.
        public float degrees;
        public bool cornerPull,insideReverse;
        public bool flexible,customFlanks;
        public bool closeFlat; // Opt-in closure geometry; old recipes retain their original creases.
        public Vector2 pullLeft,pullRight;
        // Defaults preserve the motion and panel ordering in earlier saved recipes.
        public bool singlePanel;
        public bool opening; // Existing-seam rotation; sweep contact while unstacking.
        public MirrorAxis mirror;
        public Vector2 contact;
        public Vector2 pullTip,pullStop;
        public PaperSize paper;
        public PlanePattern pattern;
        public Vector2 creaseNormal;
        public float creaseOffset;
        public float Position => creaseNormal == Vector2.zero ? offset : creaseOffset;
        public int anchor, flap; // flap is one-based; zero folds the entire stack.
        public FoldStep(CreaseFamily family,int offset,int side)
        { this=default; this.family=family; this.offset=offset; this.side=side; degrees=180;anchor=flap=0; }
        public Vector2 Normal => creaseNormal!=Vector2.zero ? creaseNormal : family==CreaseFamily.Horizontal ? Vector2.up :
            family==CreaseFamily.Vertical ? Vector2.right :
            family==CreaseFamily.Rising ? new Vector2(1,-1) : Vector2.one;
        public float Distance(Vector2 p) => Vector2.Dot(Normal,p)-Position;
    }

    // Convex pieces in flat sheet coordinates. Layers count from the table upward.
    public sealed class PaperSheet
    {
        public const int Span=11, MaxFolds=10, GridIntervals=6;
        public const float Thickness=.009f;
        const float Epsilon=.0001f;

        public sealed class Face
        {
            public readonly Vector2[] points;
            public readonly int layer;
            public readonly bool back;
            public readonly Matrix4x4 frame, material;
            public Face(Vector2[] points,int layer,bool back) : this(points,layer,back,Matrix4x4.identity,Matrix4x4.identity){}
            internal Face(Vector2[] points,int layer,bool back,Matrix4x4 frame,Matrix4x4 material)
            { this.points=points;this.layer=layer;this.back=back;this.frame=frame;this.material=material; }
            public Vector3 World(Vector2 p,float h)=>frame.MultiplyPoint3x4(new Vector3(p.x,h,p.y));
            internal Vector3 Original(Vector2 p)=>material.MultiplyPoint3x4(new Vector3(p.x,0,p.y));
            public bool Contains(Vector2 p)
            {
                float sign=0;
                for(int i=0;i<points.Length;i++)
                {
                    Vector2 a=points[i], edge=points[(i+1)%points.Length]-a;
                    float cross=edge.x*(p.y-a.y)-edge.y*(p.x-a.x);
                    if(Mathf.Abs(cross)<=Epsilon) continue;
                    if(sign*cross<0) return false;
                    sign=cross;
                }
                return true;
            }
        }
        public readonly struct FoldVertex
        {
            public readonly int anchor;
            public readonly Vector2 point;
            public FoldVertex(int anchor,Vector2 point){this.anchor=anchor;this.point=point;}
        }
        public void CollectVertices(List<FoldVertex> result)
        {
            result.Clear();
            for(int i=0;i<faces.Count;i++){
                var face=faces[i];var inverse=face.material.inverse;
                int start=result.Count;
                for(int x=0;x<=GridIntervals;x++)
                for(int y=0;y<=GridIntervals;y++){
                    var p=inverse.MultiplyPoint3x4(GridPoint(x,y));
                    var local=new Vector2(p.x,p.z);
                    if(face.Contains(local))result.Add(new FoldVertex(i,local));
                }
                foreach(var corner in face.points){
                    bool listed=false;
                    for(int j=start;j<result.Count;j++)
                        if((corner-result[j].point).sqrMagnitude<Epsilon*Epsilon){listed=true;break;}
                    if(!listed)result.Add(new FoldVertex(i,corner));
                }
            }
        }
        internal void PlyRange(Face basis,Vector2 point,out int bottom,out int top)
        {
            bottom=top=basis.layer;
            foreach(var f in faces)if(SameFrame(f,basis) && f.Contains(point)){
                bottom=Mathf.Min(bottom,f.layer);top=Mathf.Max(top,f.layer);
            }
        }
        internal bool ContainsPanelPoint(Face basis,Vector2 point)
        {
            foreach(var face in faces)if(SamePanel(basis,face) && face.Contains(point))return true;
            return false;
        }
        public bool TryAxis(FoldVertex first,FoldVertex second,out FoldStep step)
        {
            step=default;
            if(first.anchor<0 || second.anchor<0 || first.anchor>=faces.Count || second.anchor>=faces.Count)return false;
            var face=faces[first.anchor];
            if(!SameFrame(face,faces[second.anchor]))return false;
            var delta=second.point-first.point;
            if(delta.sqrMagnitude<Epsilon*Epsilon)return false;
            var normal=new Vector2(-delta.y,delta.x).normalized;
            var family=Mathf.Abs(normal.x)<Epsilon?CreaseFamily.Horizontal:Mathf.Abs(normal.y)<Epsilon?CreaseFamily.Vertical:
                normal.x*normal.y<0?CreaseFamily.Rising:CreaseFamily.Falling;
            var axis=new FoldStep(family,0,1){creaseNormal=normal,creaseOffset=Vector2.Dot(normal,first.point),singlePanel=true};
            // A corner on a shared edge can belong to either panel. Resolve the
            // panel containing both selected points before drawing or folding it.
            for(int pass=0;pass<2;pass++){
                for(int i=0;i<faces.Count;i++){
                    var panel=faces[i];
                    if((pass==0)!=(i==first.anchor) || !SameFrame(face,panel) ||
                        !ContainsPanelPoint(panel,first.point) || !ContainsPanelPoint(panel,second.point))continue;
                    bool left=false,right=false;
                    foreach(var piece in faces){
                        if(!SamePanel(panel,piece))continue;
                        foreach(var point in piece.points){
                            float distance=axis.Distance(point);
                            left|=distance< -Epsilon;right|=distance>Epsilon;
                        }
                    }
                    if(!left || !right)continue;
                    axis.anchor=i;axis.flap=i+1;step=axis;return true;
                }
            }
            return false;
        }
        public struct Guide
        {
            public FoldStep line;
            public Vector2 a,b;
            public int layer;
            public bool blueprint;
            public Face face;
        }
        public sealed class Turn
        {
            public readonly FoldStep step;
            public readonly int top,bottom;
            public readonly Matrix4x4 frame;
            internal readonly List<Face> source;
            public readonly List<Face> fixedFaces=new List<Face>(), movingFaces=new List<Face>();
            private float upLimit=-1,downLimit=-1;
            internal Turn(FoldStep step,int top,int bottom,List<Face> source)
            {this.step=step;this.top=top;this.bottom=bottom;this.source=source;frame=source[step.anchor].frame;}
            // A connected return fold may bring the opposite ply with it. Do not
            // accept that as a single-ply selection at the player's contact point.
            public bool MovesOtherPlyAt(Vector2 point)
            {
                if(step.flap==0)return false;
                var picked=source[step.flap-1];
                foreach(var f in movingFaces)
                    if(SameFrame(f,picked) && f.layer!=picked.layer && f.Contains(point))return true;
                return false;
            }
            public Matrix4x4 Rotation(float degrees)
            {
                Vector2 n=step.Normal;
                Vector2 p=n*(step.Position/n.sqrMagnitude);
                Vector3 pivot=frame.MultiplyPoint3x4(new Vector3(p.x,(degrees<0?bottom:top+1)*Thickness,p.y));
                Vector3 axis=frame.MultiplyVector(new Vector3(-n.y,0,n.x)*step.side).normalized;
                return Matrix4x4.Translate(pivot)*Matrix4x4.Rotate(Quaternion.AngleAxis(degrees,axis))*Matrix4x4.Translate(-pivot);
            }
            public float Constrain(float degrees)
            {
                if(degrees==0f)return 0f;
                if(degrees>0f){
                    if(upLimit<0)upLimit=Clearance(1);
                    return Mathf.Min(degrees,upLimit);
                }
                if(downLimit<0)downLimit=Clearance(-1);
                return Mathf.Max(degrees,-downLimit);
            }
            private float Clearance(int sign)
            {
                bool flat=true;
                foreach(var a in movingFaces)foreach(var b in fixedFaces)
                {
                    flat &= a.frame==frame && b.frame==frame;
                    if(SameFrame(a,b) && (b.layer-a.layer)*sign>0 && Overlap(a,b))return 0f;
                }
                if(flat)return 180f;
                // Sweep from the starting pose, so jumping the slider cannot pass
                // through a wing. Refine the first contact to sub-degree precision.
                for(int angle=2;angle<=180;angle+=2){
                    var previous=Rotation(sign*(angle-2));
                    if(!Crosses(Rotation(sign*angle)) && !OpeningCrosses(previous,Rotation(sign*angle)))continue;
                    float lo=angle-2,hi=angle;
                    for(int i=0;i<8;i++){
                        float mid=(lo+hi)*.5f;
                        if(Crosses(Rotation(sign*mid)) || OpeningCrosses(previous,Rotation(sign*mid)))hi=mid;else lo=mid;
                    }
                    return lo;
                }
                return 180f;
            }
            internal static bool Overlap(Face a,Face b)=>!Separated(a,b) && !Separated(b,a);
            private static bool Separated(Face a,Face b)
            {
                for(int i=0;i<a.points.Length;i++){
                    Vector2 edge=a.points[(i+1)%a.points.Length]-a.points[i];
                    if(edge.sqrMagnitude<Epsilon*Epsilon)continue;
                    Vector2 inward=new Vector2(-edge.y,edge.x)*(a.back?-1:1);
                    float far=float.NegativeInfinity;
                    foreach(var p in b.points)far=Mathf.Max(far,Vector2.Dot(inward,p-a.points[i]));
                    if(far<=Epsilon)return true;
                }
                return false;
            }
            private bool Crosses(Matrix4x4 rotation)
            {
                foreach(var a in movingFaces)foreach(var b in fixedFaces){
                    Matrix4x4 relative=b.frame.inverse*rotation*a.frame;
                    // A flap pivoting away from a stack must not count its own
                    // narrow thickness at the hinge as a crossing.
                    if(Intersects(a,b,relative))return true;
                }
                return false;
            }
            private bool OpeningCrosses(Matrix4x4 before,Matrix4x4 after)
            {
                if(!step.opening)return false;
                foreach(var a in movingFaces)foreach(var b in fixedFaces){
                    var from=b.frame.inverse*before*a.frame;var to=b.frame.inverse*after*a.frame;
                    Vector2 centre=Vector2.zero;foreach(var p in a.points)centre+=p;centre/=a.points.Length;
                    if(CrossesSurface(centre,a,b,from,to))return true;
                    for(int i=0;i<a.points.Length;i++){
                        if(CrossesSurface(a.points[i],a,b,from,to) ||
                           CrossesSurface((a.points[i]+a.points[(i+1)%a.points.Length])*.5f,a,b,from,to))return true;
                    }
                }
                return false;
            }
            private static bool CrossesSurface(Vector2 sample,Face a,Face b,Matrix4x4 from,Matrix4x4 to)
            {
                var local=new Vector3(sample.x,(a.layer+.5f)*Thickness,sample.y);
                var p=from.MultiplyPoint3x4(local);var q=to.MultiplyPoint3x4(local);
                float h=(b.layer+.5f)*Thickness,d=p.y-h,e=q.y-h;
                if(d*e>=0 || Mathf.Abs(d-e)<Epsilon)return false;
                var at=Vector3.Lerp(p,q,d/(d-e));var point=new Vector2(at.x,at.z);
                if(!b.Contains(point))return false;
                // Contact on the hinge boundary is allowed; crossing the interior is not.
                for(int i=0;i<b.points.Length;i++){
                    var u=b.points[i];var edge=b.points[(i+1)%b.points.Length]-u;
                    if(Mathf.Abs(edge.x*(point.y-u.y)-edge.y*(point.x-u.x))<=Thickness*edge.magnitude)return false;
                }
                return true;
            }
            internal static bool Intersects(Face a,Face b,Matrix4x4 relative)
            {
                float ha=(a.layer+.5f)*Thickness,hb=(b.layer+.5f)*Thickness;
                Vector2 first=default,last=default;int count=0;
                float min=float.PositiveInfinity,max=float.NegativeInfinity;
                Vector2 prev=a.points[a.points.Length-1];
                Vector3 p=relative.MultiplyPoint3x4(new Vector3(prev.x,ha,prev.y));
                foreach(var v in a.points){
                    Vector3 q=relative.MultiplyPoint3x4(new Vector3(v.x,ha,v.y));
                    float d=p.y-hb,e=q.y-hb;min=Mathf.Min(min,e);max=Mathf.Max(max,e);
                    if((d<0)!=(e<0)){
                        var hit=Vector3.Lerp(p,q,d/(d-e));
                        last=new Vector2(hit.x,hit.z);if(count++==0)first=last;
                    }
                    p=q;
                }
                // Rounded transforms at contact must not turn a thickness-wide hinge into a crossing.
                float contactTolerance=Thickness+Epsilon;
                if(count<2 || min>=-contactTolerance || max<=contactTolerance)return false;
                float lo=0,hi=1;
                for(int i=0;i<b.points.Length;i++){
                    Vector2 u=b.points[i],edge=b.points[(i+1)%b.points.Length]-u;
                    Vector2 inward=new Vector2(-edge.y,edge.x).normalized*(b.back?-1:1);
                    float x=Vector2.Dot(first-u,inward)-Thickness,y=Vector2.Dot(last-u,inward)-Thickness;
                    if(x<0 && y<0)return false;
                    if(x<0)lo=Mathf.Max(lo,x/(x-y));else if(y<0)hi=Mathf.Min(hi,x/(x-y));
                }
                return hi-lo>Epsilon;
            }
        }
        public PaperSize Format { get; }
        public float Width { get; }
        public float Height => Span;
        public int FoldLimit => MaxFolds;
        private List<Face> faces;
        public PaperSheet(PaperSize format=PaperSize.Square)
        {
            Format=format;
            Width=Span*(format==PaperSize.A4 ? 297f/210f : format==PaperSize.Letter ? 11f/8.5f : 1f);
            faces=new List<Face>{new Face(new[]{Vector2.zero,new Vector2(Width,0),
                new Vector2(Width,Height),new Vector2(0,Height)},0,false)};
        }
        internal PaperSheet(PaperSize format,float width,List<Face> panels)
        {Format=format;Width=width;faces=panels;}
        internal static bool SamePanel(Face a,Face b)=>SameFrame(a,b) && a.layer==b.layer && a.material==b.material;
        private readonly Stack<List<Face>> history=new Stack<List<Face>>();
        private readonly List<FoldStep> steps=new List<FoldStep>();
        private int peakPlies=1;
        public int PeakPlies
        {
            get
            {
                if(peakPlies>0)return peakPlies;
                // Every positive-area overlap has an edge. Sweep just inside each
                // edge, counting intervals of coverage between distinct crossings.
                var crossings=new List<float>();
                foreach(var f in faces)
                    for(int i=0;i<f.points.Length;i++)
                        peakPlies=Mathf.Max(peakPlies,EdgePlies(f,i,crossings));
                return peakPlies;
            }
        }
        private int EdgePlies(Face face,int index,List<float> crossings)
        {
            var a=face.points[index];
            var delta=face.points[(index+1)%face.points.Length]-a;
            if(delta.sqrMagnitude<Epsilon*Epsilon)return 0;
            var inward=new Vector2(-delta.y,delta.x).normalized*(face.back?-1:1);
            a+=inward*(Epsilon*2);
            crossings.Clear();
            foreach(var other in faces){
                if(!SameFrame(face,other))continue;
                float lo=0,hi=1;
                for(int j=0;j<other.points.Length && hi>lo;j++){
                    var u=other.points[j];
                    var edge=other.points[(j+1)%other.points.Length]-u;
                    if(edge.sqrMagnitude<Epsilon*Epsilon)continue;
                    var n=new Vector2(-edge.y,edge.x).normalized*(other.back?-1:1);
                    float start=Vector2.Dot(a-u,n), change=Vector2.Dot(delta,n);
                    if(Mathf.Abs(change)<Epsilon){if(start<=0)hi=lo;}
                    else if(change>0)lo=Mathf.Max(lo,-start/change);
                    else hi=Mathf.Min(hi,-start/change);
                }
                if(hi-lo>Epsilon){
                    // Store starts and ends separately using the fractional sign.
                    crossings.Add(lo+1);crossings.Add(-(hi+1));
                }
            }
            crossings.Sort(CompareCrossings);
            int live=0,peak=0;
            foreach(float t in crossings){live+=t>0?1:-1;peak=Mathf.Max(peak,live);}
            return peak;
        }
        private static int CompareCrossings(float a,float b)
        {
            int order=Mathf.Abs(a).CompareTo(Mathf.Abs(b));
            return order!=0?order:a.CompareTo(b);
        }
        public IReadOnlyList<Face> Faces=>faces;
        public FoldStep[] Recipe()=>steps.ToArray();
        public int Count=>steps.Count;
        internal FoldStep StepAt(int index)=>steps[index];
        public Bounds SpatialBounds
        {
            get
            {
                var bounds=new Bounds(faces[0].World(faces[0].points[0],faces[0].layer*Thickness),Vector3.zero);
                foreach(var f in faces)foreach(var p in f.points){
                    bounds.Encapsulate(f.World(p,f.layer*Thickness));
                    bounds.Encapsulate(f.World(p,(f.layer+1)*Thickness));
                }
                return bounds;
            }
        }
        internal static bool SameFrame(Face a,Face b)=>a.frame==b.frame;
        private struct GridLine
        {
            public Vector2Int start,end,normal;
            public int offset;
        }
        private static readonly List<GridLine>[] gridLines=BuildGridLines();
        private static List<GridLine>[] BuildGridLines()
        {
            var result=new List<GridLine>[4];
            for(int i=0;i<result.Length;i++)result[i]=new List<GridLine>();
            for(int dx=0;dx<=GridIntervals;dx++)
            for(int dy=-GridIntervals;dy<=GridIntervals;dy++){
                if((dx==0 && dy!=1) || CommonDivisor(dx,Math.Abs(dy))!=1)continue;
                var direction=new Vector2Int(dx,dy);
                var family=dx==0?CreaseFamily.Vertical:dy==0?CreaseFamily.Horizontal:
                           dy>0?CreaseFamily.Rising:CreaseFamily.Falling;
                var normal=new Vector2Int(Math.Abs(dy),dy>0?-dx:dx);
                for(int x=0;x<=GridIntervals;x++)
                for(int y=0;y<=GridIntervals;y++){
                    var start=new Vector2Int(x,y);
                    // Only the first point on this straight line emits its segment.
                    if(InsideGrid(start-direction) || !InsideGrid(start+direction))continue;
                    if((dx==0 && (x==0 || x==GridIntervals)) ||
                       (dy==0 && (y==0 || y==GridIntervals)))continue; // Paper edges already connect these points.
                    var end=start+direction;
                    while(InsideGrid(end+direction))end+=direction;
                    result[(int)family].Add(new GridLine{start=start,end=end,normal=normal,offset=normal.x*x+normal.y*y});
                }
            }
            return result;
        }
        private static int CommonDivisor(int x,int y)
        {
            while(y!=0){int next=x%y;x=y;y=next;}
            return x;
        }
        private static bool InsideGrid(Vector2Int p)=>p.x>=0 && p.x<=GridIntervals && p.y>=0 && p.y<=GridIntervals;
        internal Vector3 GridPoint(int x,int y)=>new Vector3(x*Width/GridIntervals,0,y*Height/GridIntervals);
        public void Guides(CreaseFamily family,List<Guide> result)
        {
            result.Clear();
            for(int i=0;i<faces.Count;i++){
                var f=faces[i];
                var inverse=f.material.inverse;
                foreach(var grid in gridLines[(int)family]){
                    var start=inverse.MultiplyPoint3x4(GridPoint(grid.start.x,grid.start.y));
                    var end=inverse.MultiplyPoint3x4(GridPoint(grid.end.x,grid.end.y));
                    var a=new Vector2(start.x,start.z);var b=new Vector2(end.x,end.z);
                    if(!Trim(f,ref a,ref b))continue;
                    var printed=new Vector2(grid.normal.x*GridIntervals/Width,grid.normal.y*GridIntervals/Height);
                    var n=new Vector2(printed.x*f.material.m00+printed.y*f.material.m20,
                                      printed.x*f.material.m02+printed.y*f.material.m22).normalized;
                    var line=new FoldStep(family,grid.offset,1){anchor=i,creaseNormal=n,creaseOffset=Vector2.Dot(n,a)};
                    result.Add(new Guide{line=line,a=a,b=b,layer=f.layer,face=f});
                }
            }
        }
        // Clip a finite, printed line to a convex panel. Winding reverses on full folds.
        internal static bool Trim(Face face,ref Vector2 a,ref Vector2 b)
        {
            var delta=b-a;
            float lo=0,hi=1;
            for(int i=0;i<face.points.Length;i++){
                var u=face.points[i];var edge=face.points[(i+1)%face.points.Length]-u;
                var normal=new Vector2(-edge.y,edge.x).normalized*(face.back?-1:1);
                float start=Vector2.Dot(a-u,normal), change=Vector2.Dot(delta,normal);
                if(Mathf.Abs(change)<Epsilon){if(start< -Epsilon)return false;}
                else if(change>0)lo=Mathf.Max(lo,-start/change);
                else hi=Mathf.Min(hi,-start/change);
                if(hi<=lo)return false;
            }
            b=a+delta*hi;a+=delta*lo;
            return (a-b).sqrMagnitude>Epsilon*Epsilon;
        }
        internal void MarkCrease(FoldStep line,List<Guide> result)
        {
            result.Clear();
            foreach(var f in faces)
                if((line.singlePanel?SamePanel(f,faces[line.anchor]):SameFrame(f,faces[line.anchor])) && Intersection(f,line,out var a,out var b))
                    result.Add(new Guide{line=line,a=a,b=b,layer=f.layer,face=f});
        }
        internal static bool Intersection(Face face,FoldStep line,out Vector2 a,out Vector2 b)
        {
            a=b=Vector2.zero;bool found=false;
            Vector2 axis=new Vector2(-line.Normal.y,line.Normal.x);
            for(int i=0;i<face.points.Length;i++)
            {
                Vector2 p=face.points[i],q=face.points[(i+1)%face.points.Length];
                float d=line.Distance(p),e=line.Distance(q);
                if(Mathf.Abs(d)>Epsilon && d*e>=0) continue;
                Vector2 hit=Mathf.Abs(d)<=Epsilon ? p : Vector2.Lerp(p,q,d/(d-e));
                if(!found){a=b=hit;found=true;}
                else{if(Vector2.Dot(hit-a,axis)<0)a=hit;if(Vector2.Dot(hit-b,axis)>0)b=hit;}
            }
            return found && (a-b).sqrMagnitude>Epsilon*Epsilon;
        }
        internal static Face Clip(Face face,FoldStep step,int side)
        {
            var polygon=new List<Vector2>();
            for(int i=0;i<face.points.Length;i++)
            {
                Vector2 p=face.points[i],q=face.points[(i+1)%face.points.Length];
                float d=step.Distance(p)*side,e=step.Distance(q)*side;
                if(d>=-Epsilon)polygon.Add(p);
                if((d>Epsilon && e< -Epsilon)||(d< -Epsilon && e>Epsilon))polygon.Add(Vector2.Lerp(p,q,d/(d-e)));
            }
            return polygon.Count>=3 && Area(polygon)>Epsilon ? new Face(polygon.ToArray(),face.layer,face.back,face.frame,face.material) : null;
        }
        public static float Area(IReadOnlyList<Vector2> polygon)
        {
            float twice=0;
            for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];twice+=a.x*b.y-b.x*a.y;}
            return Mathf.Abs(twice)*.5f;
        }
        // Determine which pieces remain connected through the original sheet, not
        // merely which polygons happen to touch after stacking.
        private static bool Joined(Face a,Face b,FoldStep line,Face basis)
        {
            for(int i=0;i<a.points.Length;i++)
            {
                Vector2 p=a.points[i],q=a.points[(i+1)%a.points.Length];
                if(IsHingeEdge(a,basis,line,p,q))continue;
                Vector3 u=a.Original(p), edge=a.Original(q)-u;float length=edge.magnitude;
                if(length<Epsilon)continue;
                Vector3 axis=edge/length;
                for(int j=0;j<b.points.Length;j++)
                {
                    Vector2 r=b.points[j],s=b.points[(j+1)%b.points.Length];
                    if(IsHingeEdge(b,basis,line,r,s))continue;
                    Vector3 v=b.Original(r)-u,w=b.Original(s)-u;
                    if(Vector3.Cross(axis,v).sqrMagnitude>Epsilon*Epsilon ||
                        Vector3.Cross(axis,w).sqrMagnitude>Epsilon*Epsilon)continue;
                    float t=Vector3.Dot(v,axis), end=Vector3.Dot(w,axis);
                    if(Mathf.Min(length,Mathf.Max(t,end))-Mathf.Max(0f,Mathf.Min(t,end))>Epsilon)return true;
                }
            }
            return false;
        }
        private static bool IsHingeEdge(Face f,Face basis,FoldStep line,Vector2 a,Vector2 b)=>
            SameFrame(f,basis) && Mathf.Abs(line.Distance(a))<Epsilon && Mathf.Abs(line.Distance(b))<Epsilon;

        // Opening uses an existing material seam. It does not invent a new
        // crease between the grabbed corner and another selected point.
        public void OpeningTurns(FoldVertex handle,List<Turn> result,bool wholeStack=false)
        {
            result.Clear();
            if(Count>=FoldLimit || handle.anchor<0 || handle.anchor>=faces.Count)return;
            var face=faces[handle.anchor];
            for(int edge=0;edge<face.points.Length;edge++){
                var a=face.points[edge];var b=face.points[(edge+1)%face.points.Length];
                var delta=b-a;if(delta.sqrMagnitude<Epsilon*Epsilon)continue;
                var n=new Vector2(-delta.y,delta.x).normalized;
                float distance=Vector2.Dot(n,handle.point-a);
                if(Mathf.Abs(distance)<.01f || !HasMaterialJoint(face,a,b))continue;
                var step=new FoldStep(CreaseFamily.Rising,0,distance>0?1:-1){
                    anchor=handle.anchor,flap=wholeStack?0:handle.anchor+1,singlePanel=!wholeStack,opening=true,
                    creaseNormal=n,creaseOffset=Vector2.Dot(n,a),contact=handle.point};
                var planned=Plan(step);
                if(planned==null){step.singlePanel=false;planned=Plan(step);}
                if(planned==null || (!wholeStack && planned.MovesOtherPlyAt(handle.point)))continue;
                if(Mathf.Abs(planned.Constrain(180))<2 && Mathf.Abs(planned.Constrain(-180))<2)continue;
                bool duplicate=false;
                foreach(var existing in result)
                    if(Mathf.Abs(Mathf.Abs(Vector2.Dot(existing.step.Normal,n))-1)<.0001f &&
                       Mathf.Abs(existing.step.Distance(a))<.0001f){duplicate=true;break;}
                if(!duplicate)result.Add(planned);
            }
        }
        private bool HasMaterialJoint(Face face,Vector2 a,Vector2 b)
        {
            var origin=face.Original(a);var edge=face.Original(b)-origin;
            float length=edge.magnitude;if(length<Epsilon)return false;
            var axis=edge/length;
            foreach(var other in faces){
                if(SamePanel(face,other))continue;
                for(int j=0;j<other.points.Length;j++){
                    var p=other.Original(other.points[j])-origin;
                    var q=other.Original(other.points[(j+1)%other.points.Length])-origin;
                    if(Vector3.Cross(axis,p).sqrMagnitude>Epsilon*Epsilon ||
                       Vector3.Cross(axis,q).sqrMagnitude>Epsilon*Epsilon)continue;
                    float x=Vector3.Dot(axis,p),y=Vector3.Dot(axis,q);
                    if(Mathf.Min(length,Mathf.Max(x,y))-Mathf.Max(0,Mathf.Min(x,y))>Epsilon)return true;
                }
            }
            return false;
        }
        public Turn Plan(FoldStep step)
        {
            if(step.cornerPull || step.mirror!=MirrorAxis.Off || (step.singlePanel && step.flap==0) || Count>=FoldLimit || (int)step.family<0 || (int)step.family>3 || (step.side!=1 && step.side!=-1)
                || step.anchor<0 || step.anchor>=faces.Count || step.flap<0 || step.flap>faces.Count)return null;
            var basis=faces[step.anchor];
            int top=basis.layer,bottom=top;
            var parts=new List<Face>();var sides=new List<int>();var seeds=new Queue<int>();
            bool hinge=false;
            for(int i=0;i<faces.Count;i++){
                var f=faces[i];
                if(step.singlePanel && !SamePanel(f,faces[step.flap-1])){
                    parts.Add(f);sides.Add(-1);continue;
                }
                if(!SameFrame(f,basis)){parts.Add(f);sides.Add(0);continue;}
                top=Mathf.Max(top,f.layer);bottom=Mathf.Min(bottom,f.layer);
                hinge|=Intersection(f,step,out _,out _);
                var stay=Clip(f,step,-step.side);if(stay!=null){parts.Add(stay);sides.Add(-1);}
                var move=Clip(f,step,step.side);if(move==null)continue;
                if(step.flap==0 || step.flap==i+1)seeds.Enqueue(parts.Count);
                parts.Add(move);sides.Add(1);
            }
            if(!hinge || seeds.Count==0)return null;
            var moving=new bool[parts.Count];
            while(seeds.Count>0){
                int i=seeds.Dequeue();if(moving[i])continue;moving[i]=true;
                if(sides[i]<0)return null; // A second, non-collinear joint would have to tear.
                for(int j=0;j<parts.Count;j++)
                    if(!moving[j] && i!=j && Joined(parts[i],parts[j],step,basis))seeds.Enqueue(j);
            }
            var turn=new Turn(step,top,bottom,faces);
            for(int i=0;i<parts.Count;i++)(moving[i]?turn.movingFaces:turn.fixedFaces).Add(parts[i]);
            return turn.fixedFaces.Count>0?turn:null;
        }
        public bool Finish(Turn turn)=>Finish(turn,turn==null?0f:(turn.step.degrees==0?180f:turn.step.degrees));
        public bool Finish(Turn turn,float degrees)
        {
            if(turn==null || turn.source!=faces || Count>=FoldLimit || !(Mathf.Abs(degrees)>=1f && Mathf.Abs(degrees)<=180f)
                || Mathf.Abs(turn.Constrain(degrees)-degrees)>.001f)return false;
            var next=new List<Face>(turn.fixedFaces);
            var rotation=turn.Rotation(degrees);
            var basis=faces[turn.step.anchor];
            foreach(var f in turn.movingFaces){
                if(Mathf.Abs(degrees)==180f && SameFrame(f,basis)){
                    int layer=degrees>0 ? 2*turn.top+1-f.layer : 2*turn.bottom-1-f.layer;
                    next.Add(ReflectFlat(f,turn.step,layer));
                }else next.Add(new Face(f.points,f.layer,f.back,rotation*f.frame,f.material));
            }
            var saved=turn.step;saved.degrees=degrees;saved.paper=Format;
            history.Push(faces);faces=next;steps.Add(saved);peakPlies=0;return true;
        }
        internal static Face ReflectFlat(Face f,FoldStep line,int layer)
        {
            var n=line.Normal;float k=2f/n.sqrMagnitude;
            var mirror=Matrix4x4.identity;
            mirror.m00-=k*n.x*n.x;mirror.m02=mirror.m20=-k*n.x*n.y;mirror.m22-=k*n.y*n.y;
            mirror.m03=k*line.Position*n.x;mirror.m23=k*line.Position*n.y;
            var points=new Vector2[f.points.Length];
            for(int i=0;i<points.Length;i++)points[i]=f.points[i]-k*line.Distance(f.points[i])*n;
            return new Face(points,layer,!f.back,f.frame,f.material*mirror);
        }
        public bool FinishPull(PaperCornerPull pull,float degrees)
        {
            if(pull==null || pull.Source!=this || !pull.IsCurrent || Count>=FoldLimit ||
                Mathf.Abs(degrees)<1f || !pull.Pose(degrees))return false;
            var saved=pull.Step;saved.paper=Format;saved.degrees=degrees;
            history.Push(faces);faces=new List<Face>(pull.Preview);steps.Add(saved);peakPlies=0;return true;
        }
        public bool FinishReverse(PaperInsideReverse fold,float degrees)
        {
            if(fold==null || fold.Source!=this || !fold.IsCurrent || Count>=FoldLimit || degrees<1f || !fold.Pose(degrees))return false;
            var saved=fold.Step;saved.paper=Format;saved.degrees=degrees;
            history.Push(faces);faces=new List<Face>(fold.Preview);steps.Add(saved);peakPlies=0;return true;
        }
        public bool FinishPair(PaperMirrorFold pair,float degrees)
        {
            if(pair==null || !pair.IsCurrent(this) || Count>=FoldLimit || Mathf.Abs(degrees)<1 || !pair.Pose(degrees))return false;
            var saved=pair.Step;saved.paper=Format;saved.degrees=degrees;
            history.Push(faces);faces=new List<Face>(pair.Preview);steps.Add(saved);peakPlies=0;return true;
        }
        public bool ApplyStep(FoldStep step)
        {
            if(step.mirror!=MirrorAxis.Off)return FinishPair(PaperMirrorFold.Create(this,step),step.degrees);
            if(step.insideReverse)return FinishReverse(PaperInsideReverse.Create(this,step),step.degrees);
            if(step.cornerPull)return FinishPull(PaperCornerPull.Create(this,step),step.degrees);
            return Finish(Plan(step));
        }
        public bool Undo()
        {
            if(history.Count==0)return false;
            faces=history.Pop();steps.RemoveAt(steps.Count-1);peakPlies=0;return true;
        }
        public static PaperSheet Replay(FoldStep[] recipe)
        {
            if(recipe==null || recipe.Length==0)return new PaperSheet();
            var format=recipe[0].paper;
            if((int)format<0 || (int)format>2)return new PaperSheet();
            var sheet=new PaperSheet(format);
            if(recipe.Length>sheet.FoldLimit)return new PaperSheet();
            foreach(var step in recipe)
            {
                if(step.paper!=format)return new PaperSheet();
                if(!sheet.ApplyStep(step))return new PaperSheet();
            }
            return sheet;
        }
    }
}
