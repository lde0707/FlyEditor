using System.Collections.Generic;
using UnityEngine;
namespace FlyEditor.Flight
{
    // Squash a closed triangular pocket. The two flank handles may coincide in
    // space while remaining distinct points of the original sheet.
    internal sealed class PaperPocketSquash
    {
        private struct Piece {public PaperSheet.Face face;public int side;public bool cap,active;}
        private readonly PaperSheet sheet;private readonly IReadOnlyList<PaperSheet.Face> source;
        private readonly PaperSheet.Face basis;
        private readonly List<Piece> pieces=new List<Piece>();
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>(),trial=new List<PaperSheet.Face>(),middle=new List<PaperSheet.Face>();
        private readonly List<int> layers=new List<int>();
        private Vector3 head,tip,foot,axis,radial,crease;private float length,beta,last=float.NaN;
        public IReadOnlyList<PaperSheet.Face> Preview=>preview;public Vector3 Tip{get;private set;}public Vector3 Stop=>head;
        private PaperPocketSquash(PaperSheet s,PaperSheet.Face f){sheet=s;source=s.Faces;basis=f;}
        private static bool Locate(PaperSheet s,Vector3 m,out Vector3 w){foreach(var f in s.Faces){var p=PaperFoldTopology.Local(f,m);if(f.Contains(p)){w=f.World(p,0);return true;}}w=default;return false;}
        private static PaperSheet.Face Triangle(PaperSheet.Face f,Vector3 a,Vector3 b,Vector3 c){
            if(Vector3.Cross(b-a,c-a).y>0){var t=b;b=c;c=t;}
            var points=new[]{a,b,c};var part=f;
            for(int i=0;i<3&&part!=null;i++){var d=points[(i+1)%3]-points[i];var n=new Vector3(-d.z,0,d.x);part=PaperSheet.Clip(part,PaperFoldTopology.MaterialLine(part,points[i],n),1);}return part;
        }
        public static PaperPocketSquash Create(PaperSheet s,FoldStep step){
            if(!step.exactSquash||!step.regionalPull||!step.customFlanks)return null;
            var f=s.Faces[step.anchor];var t=f.Original(step.pullTip);var h=f.Original(step.pullStop);var l=f.Original(step.pullLeft);var r=f.Original(step.pullRight);
            if((l-r).sqrMagnitude<.01f || Vector3.Cross(t-h,l-h).y*Vector3.Cross(t-h,r-h).y>=-.0001f)return null;
            var p=new PaperPocketSquash(s,f);
            if(!Locate(s,t,out p.tip)||!Locate(s,h,out p.head)||!Locate(s,l,out p.foot)||!Locate(s,r,out var other)||Vector3.Distance(p.foot,other)>.002f)return null;
            p.length=Vector3.Distance(p.tip,p.head);float baseLength=Vector3.Distance(p.foot,p.head);
            if(p.length<.01f||baseLength<.01f)return null;
            p.axis=(p.foot-p.head)/baseLength;p.radial=Vector3.ProjectOnPlane(p.tip-p.head,p.axis).normalized;
            p.beta=Vector3.Angle(p.tip-p.head,p.axis)*Mathf.Deg2Rad;
            if(p.radial.sqrMagnitude<.9f||p.beta<.05f||p.beta>Mathf.PI-.05f)return null;
            p.crease=(p.tip*baseLength+p.foot*p.length)/(p.length+baseLength);
            var creaseNormal=Vector3.Cross(p.crease-p.head,f.frame.MultiplyVector(Vector3.up));
            int activeSides=0;
            foreach(var face in s.Faces){
                var a=Triangle(face,h,t,l);var b=Triangle(face,h,r,t);float area=(a==null?0:PaperSheet.Area(a.points))+(b==null?0:PaperSheet.Area(b.points));
                if(area<.0001f){p.pieces.Add(new Piece{face=face});continue;}
                if(!PaperSheet.SameFrame(face,f)||Mathf.Abs(area-PaperSheet.Area(face.points))>.001f)return null;
                if(!p.layers.Contains(face.layer))p.layers.Add(face.layer);
                for(int side=0;side<2;side++){var part=side==0?a:b;if(part==null)continue;activeSides|=1<<side;
                    var line=PaperFoldTopology.WorldLine(part,p.head,creaseNormal);int sign=line.Distance(PaperFoldTopology.Local(part,t))>0?1:-1;
                    var cap=PaperSheet.Clip(part,line,sign);var body=PaperSheet.Clip(part,line,-sign);
                    if(cap!=null)p.pieces.Add(new Piece{face=cap,active=true,cap=true,side=side});
                    if(body!=null)p.pieces.Add(new Piece{face=body,active=true,side=side});
                }
            }
            if(activeSides!=3||p.pieces.Count>512)return null;p.layers.Sort();return p;
        }
        private static Quaternion Basis(Vector3 a,Vector3 b)=>Quaternion.LookRotation(a,Vector3.Cross(a,b));
        private Matrix4x4 Map(int side,bool cap,float angle,out Vector3 movedTip){
            float half=angle*.5f;var anchor=Quaternion.AngleAxis(-half,axis);
            float k=Mathf.Tan(beta*.5f)*Mathf.Cos(half*Mathf.Deg2Rad);float a=2*Mathf.Atan(k);
            movedTip=head+anchor*(axis*(length*Mathf.Cos(a))+radial*(length*Mathf.Sin(a)));
            var body=Quaternion.AngleAxis(side==0?0:-angle,axis);var q=body;
            if(cap){var movedCrease=body*(crease-head);q=Basis(movedTip-head,movedCrease)*Quaternion.Inverse(Basis(tip-head,crease-head));}
            return Matrix4x4.TRS(head-q*head,q,Vector3.one);
        }
        public void MarkGuides(List<PaperSheet.Guide> guides){guides.Clear();foreach(var piece in pieces)if(piece.active&&piece.cap){var n=Vector3.Cross(crease-head,basis.frame.MultiplyVector(Vector3.up));var line=PaperFoldTopology.WorldLine(piece.face,head,n);if(PaperSheet.Intersection(piece.face,line,out var a,out var b))guides.Add(new PaperSheet.Guide{face=piece.face,line=line,layer=piece.face.layer,a=a,b=b});}}
        public bool Pose(float angle){
            if(sheet.Faces!=source||!float.IsFinite(angle)||Mathf.Abs(angle)>180)return false;
            if(angle==last)return true;if(angle==0){preview.Clear();preview.AddRange(source);Tip=tip;last=0;return true;}
            var normal=basis.frame.MultiplyVector(Vector3.up);Map(0,true,Mathf.Sign(angle),out var sample);
            int away=Vector3.Dot(sample-tip,normal)>0?1:-1;
            // Opening an interior pocket may not pass through a stationary ply.
            foreach(var a in pieces)if(a.active){var map=Map(a.side,a.cap,Mathf.Sign(angle),out _);Vector2 c=Vector2.zero;foreach(var v in a.face.points)c+=v;c/=a.face.points.Length;
                if(Mathf.Abs(Vector3.Dot(map.MultiplyPoint3x4(a.face.World(c,0))-a.face.World(c,0),normal))<.00001f)continue;
                foreach(var b in pieces)if(!b.active&&PaperSheet.SameFrame(a.face,b.face)&&(b.face.layer-a.face.layer)*away>0&&PaperSheet.Turn.Overlap(a.face,b.face))return false;
            }
            trial.Clear();bool closed=Mathf.Abs(angle)==180;int count=layers.Count;
            foreach(var piece in pieces){var f=piece.face;if(!piece.active){trial.Add(f);continue;}var map=Map(piece.side,piece.cap,angle,out var movedTip);Tip=movedTip;
                if(closed){var chart=basis.frame.inverse*map*f.frame;var pts=new Vector2[f.points.Length];for(int i=0;i<pts.Length;i++){var v=chart.MultiplyPoint3x4(new Vector3(f.points[i].x,0,f.points[i].y));if(Mathf.Abs(v.y)>.002f)return false;pts[i]=new Vector2(v.x,v.z);}
                    chart.m01=chart.m10=chart.m12=chart.m21=chart.m13=0;chart.m11=1;bool flipped=chart.m00*chart.m22-chart.m02*chart.m20<0;int rank=layers.BinarySearch(f.layer);int group=piece.side==0?(piece.cap?1:0):(piece.cap?2:3);
                    int layer=away>0?layers[0]+group*count+(flipped?count-1-rank:rank):layers[count-1]-(group+1)*count+1+(flipped?count-1-rank:rank);
                    trial.Add(new PaperSheet.Face(pts,layer,f.back^flipped,basis.frame,f.material*chart.inverse,f));
                }else trial.Add(new PaperSheet.Face(f.points,f.layer,f.back,map*f.frame,f.material,f));
            }
            middle.Clear();foreach(var f in trial)middle.Add(new PaperSheet.Face(f.points,0,f.back,f.frame*Matrix4x4.Translate(-Vector3.up*(PaperSheet.Thickness*.5f)),f.material,f));
            for(int i=0;i<trial.Count;i++)for(int j=0;j<i;j++){
                var a=trial[i];var b=trial[j];if(PaperFoldTopology.SharedEdge(a,b,out var u,out var v)&&(Vector3.Distance(PaperFoldTopology.Position(a,u),PaperFoldTopology.Position(b,u))>.002f||Vector3.Distance(PaperFoldTopology.Position(a,v),PaperFoldTopology.Position(b,v))>.002f))return false;
                var x=middle[i];var y=middle[j];if(PaperSheet.Turn.Intersects(x,y,y.frame.inverse*x.frame)||PaperSheet.Turn.Intersects(y,x,x.frame.inverse*y.frame))return false;
            }
            if(closed){middle.Clear();foreach(var piece in pieces){var f=piece.face;var map=piece.active?Map(piece.side,piece.cap,Mathf.Sign(angle)*179.9f,out _):Matrix4x4.identity;middle.Add(new PaperSheet.Face(f.points,f.layer,f.back,map*f.frame,f.material,f));}if(!PaperFlatStackOrder.Apply(trial,middle))return false;}
            preview.Clear();preview.AddRange(trial);last=angle;return true;
        }
    }
}
