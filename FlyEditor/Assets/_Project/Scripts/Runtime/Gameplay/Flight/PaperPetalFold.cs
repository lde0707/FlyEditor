using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // A flat, doubled kite can lift its free corner while both side corners
    // tuck inward. Bisectors and the transverse hinge are derived from four
    // handles, never from a model name, paper size or authored crease template.
    internal sealed class PaperPetalFold
    {
        private enum Motion { Fixed, Centre, LeftUpper, LeftLower, RightUpper, RightLower }
        private struct Piece { public PaperSheet.Face face; public Motion motion; }
        private readonly PaperSheet sheet;
        private readonly IReadOnlyList<PaperSheet.Face> source;
        private readonly PaperSheet.Face basis;
        private readonly HashSet<PaperSheet.Face> upper=new HashSet<PaperSheet.Face>(),active=new HashSet<PaperSheet.Face>();
        private readonly List<Piece> pieces=new List<Piece>();
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>(),trial=new List<PaperSheet.Face>();
        private Vector2 tip,head,left,right,p,q,backLeft,backRight;
        private readonly Matrix4x4[] maps=new Matrix4x4[6];
        private int lowest=int.MaxValue,highest=int.MinValue;
        private float lastAngle=float.NaN;
        private bool continuous;
        private bool coordinated;
        private readonly Dictionary<PaperSheet.Face,int> supportSides=new Dictionary<PaperSheet.Face,int>();
        private readonly List<PaperSheet.Face> neutral=new List<PaperSheet.Face>();
        private readonly List<PaperSheet.Face> rollback=new List<PaperSheet.Face>();
        internal string Failure;
        private bool? positiveClearance,negativeClearance;
        public IReadOnlyList<PaperSheet.Face> Preview=>preview;
        internal bool Coordinated=>coordinated;
        internal bool HingeHints;
        internal bool MatchesHingeHints(Vector3 first,Vector3 second,float tolerance)
        {
            var a=Locate(first);var b=Locate(second);float squared=tolerance*tolerance;
            return Finite(a)&&Finite(b)&&(((a-p).sqrMagnitude<=squared&&(b-q).sqrMagnitude<=squared)||((a-q).sqrMagnitude<=squared&&(b-p).sqrMagnitude<=squared));
        }
        internal int MotionRole(PaperSheet.Face face)
        {
            if(!coordinated)return 0;
            foreach(var piece in pieces)if(ReferenceEquals(piece.face.region,face.region)){
                if(piece.motion==Motion.Fixed)return 0;
                return piece.motion==Motion.Centre||piece.motion==Motion.LeftUpper||piece.motion==Motion.RightUpper?1:2;
            }return 0;
        }
        public Vector3 Tip {get;private set;}
        public Vector3 Stop=>basis.World((p+q)*.5f,(basis.layer+.5f)*PaperSheet.Thickness);
        private PaperPetalFold(PaperSheet sheet,PaperSheet.Face basis){this.sheet=sheet;source=sheet.Faces;this.basis=basis;}
        private static Vector3 V(Vector2 p)=>new Vector3(p.x,0,p.y);
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private Vector2 Locate(Vector3 material)
        {
            foreach(var f in source){var local=f.material.inverse.MultiplyPoint3x4(material);var point=new Vector2(local.x,local.z);
                if(!f.Contains(point))continue;
                var world=basis.frame.inverse.MultiplyPoint3x4(f.World(point,0));return new Vector2(world.x,world.z);}
            return new Vector2(float.NaN,float.NaN);
        }
        public static PaperPetalFold Create(PaperSheet sheet,FoldStep step)
        {
            if(!step.adaptiveCreases || !step.customFlanks || !step.regionalPull)return null;
            var basis=sheet.Faces[step.anchor];var plan=new PaperPetalFold(sheet,basis){continuous=step.partialPetalBacking||step.connectedPetalBacking,coordinated=step.coordinatedSurface};
            var mt=basis.Original(step.pullTip);var mh=basis.Original(step.pullStop);
            var ml=basis.Original(step.pullLeft);var mr=basis.Original(step.pullRight);
            plan.tip=plan.Locate(mt);plan.head=plan.Locate(mh);plan.left=plan.Locate(ml);plan.right=plan.Locate(mr);
            var axis=plan.head-plan.tip;
            if(!Finite(plan.tip)||!Finite(plan.head)||!Finite(plan.left)||!Finite(plan.right)||axis.sqrMagnitude<.01f)return null;
            float l=Cross(axis,plan.left-plan.tip),r=Cross(axis,plan.right-plan.tip);
            if(l*r>=-.0001f)return null;
            if(l<0){var t=plan.left;plan.left=plan.right;plan.right=t;var m=ml;ml=mr;mr=m;}
            if(!Bisector(plan.tip,plan.head,plan.left,out plan.p)||!Bisector(plan.tip,plan.head,plan.right,out plan.q))return null;
            if((plan.p-plan.q).sqrMagnitude<.01f)return null;
            // Identify the picked material leaf, not every coincident screen face.
            var hull=new List<Vector2>{new Vector2(mt.x,mt.z),new Vector2(ml.x,ml.z),new Vector2(mh.x,mh.z),new Vector2(mr.x,mr.z)};
            float winding=0;for(int i=0;i<4;i++)winding+=Cross(hull[i],hull[(i+1)%4]);if(winding<0)hull.Reverse();
            foreach(var f in plan.source){
                var part=f;
                for(int i=0;i<4 && part!=null;i++){
                    var a=hull[i];var edge=hull[(i+1)%4]-a;var n=new Vector2(-edge.y,edge.x);var m=f.material;
                    var line=new FoldStep{creaseNormal=new Vector2(n.x*m.m00+n.y*m.m20,n.x*m.m02+n.y*m.m22),creaseOffset=Vector2.Dot(n,a)-n.x*m.m03-n.y*m.m23};
                    part=PaperSheet.Clip(part,line,1);
                }
                if(part!=null && PaperSheet.Area(part.points)>.0001f)plan.upper.Add(f);
            }
            if(!plan.upper.Contains(basis))return null;
            bool leftBuddy=false,rightBuddy=false;float leftRoot=0,rightRoot=0;
            if(step.connectedPetalBacking){
                if(!plan.CollectBacking(mh,ml,mr,out leftRoot,out rightRoot))return null;
                leftBuddy=rightBuddy=true;
            }else{
            foreach(var f in plan.source){
                if(plan.upper.Contains(f)){plan.active.Add(f);continue;}
                bool a=SharedSegment(f,mh,ml),b=SharedSegment(f,mh,mr);
                if(!a&&!b)continue;
                if(!PaperSheet.SameFrame(f,basis))return null;
                // A folded return leaf must occupy the same side of its hinge.
                bool containsTip=false;foreach(var v in f.points)containsTip|=(v-plan.tip).sqrMagnitude<.00001f;
                if(!containsTip){
                    if(!step.partialPetalBacking)continue;
                    // A return flap may end on the spine before the free tip.
                    // It still follows the same side hinge if its whole area is
                    // inside that hinge's tip-side half-plane.
                    bool onTipSide=true;
                    foreach(var v in f.points){
                        if(a && Cross(plan.left-plan.head,v-plan.head)*Cross(plan.left-plan.head,plan.tip-plan.head)<-.0001f)onTipSide=false;
                        if(b && Cross(plan.right-plan.head,v-plan.head)*Cross(plan.right-plan.head,plan.tip-plan.head)<-.0001f)onTipSide=false;
                    }
                    if(!onTipSide)continue;
                }
                plan.active.Add(f);leftBuddy|=a;rightBuddy|=b;
                var spine=plan.tip-plan.head;
                foreach(var v in f.points)if(Mathf.Abs(Cross(spine,v-plan.head))<.001f){
                    float along=Vector2.Dot(v-plan.head,spine)/spine.sqrMagnitude;
                    if(a)leftRoot=Mathf.Max(leftRoot,along);if(b)rightRoot=Mathf.Max(rightRoot,along);
                }
            }
            }
            if(!leftBuddy||!rightBuddy)return null;
            plan.backLeft=plan.continuous?(plan.coordinated?Vector2.LerpUnclamped(plan.head,plan.tip,leftRoot):Vector2.Lerp(plan.head,plan.tip,leftRoot)):plan.tip;
            plan.backRight=plan.continuous?(plan.coordinated?Vector2.LerpUnclamped(plan.head,plan.tip,rightRoot):Vector2.Lerp(plan.head,plan.tip,rightRoot)):plan.tip;
            if(plan.coordinated && !PaperHingeClosure.TrySolve(plan.tip,plan.head,plan.left,plan.right,plan.backLeft,plan.backRight,ref plan.p,ref plan.q))return null;
            foreach(var f in plan.active){
                if(!PaperSheet.SameFrame(f,basis))return null;
                plan.lowest=Mathf.Min(plan.lowest,f.layer);plan.highest=Mathf.Max(plan.highest,f.layer);
                foreach(var v in f.points){
                    bool inside=InTriangle(v,plan.tip,plan.left,plan.head)||InTriangle(v,plan.tip,plan.head,plan.right);
                    if(plan.coordinated && !plan.upper.Contains(f) && plan.supportSides.TryGetValue(f,out int side))
                        inside=((side&1)!=0 && InTriangle(v,plan.backLeft,plan.left,plan.head))||((side&2)!=0 && InTriangle(v,plan.backRight,plan.head,plan.right));
                    if(!inside)return null;
                }
            }
            return plan.Build()?plan:null;
        }
        // A historical crease may split one rigid return panel into several
        // polygons. Trace that panel in original-paper space rather than requiring
        // every polygon to touch the selected hinge itself. Screen overlap does
        // not create an edge, and a different material transform is a real fold.
        private bool CollectBacking(Vector3 h,Vector3 l,Vector3 r,out float leftRoot,out float rightRoot)
        {
            leftRoot=rightRoot=0;
            var sides=new Dictionary<PaperSheet.Face,int>();
            var queue=new Queue<PaperSheet.Face>();
            foreach(var f in source){
                if(upper.Contains(f)){active.Add(f);continue;}
                int mask=(SharedSegment(f,h,l)?1:0)|(SharedSegment(f,h,r)?2:0);
                if(mask==0)continue;
                if(!PaperSheet.SameFrame(f,basis))return false;
                if(!BackingSide(f,mask))continue;
                sides.Add(f,mask);queue.Enqueue(f);
            }
            while(queue.Count>0){
                var f=queue.Dequeue();int mask=sides[f];
                foreach(var g in source){
                    if(upper.Contains(g)||f==g||!PaperSheet.SamePanel(f,g))continue;
                    int known=sides.TryGetValue(g,out int value)?value:0;
                    int extra=mask&~known;if(extra==0||!BackingSide(g,extra))continue;
                    if(!PaperFoldTopology.SharedEdge(f,g,out _,out _))continue;
                    sides[g]=known|extra;queue.Enqueue(g);
                }
            }
            int found=0;var spine=tip-head;
            foreach(var entry in sides){
                var f=entry.Key;int mask=entry.Value;active.Add(f);found|=mask;supportSides[f]=mask;
                foreach(var v in f.points)if(Mathf.Abs(Cross(spine,v-head))<.001f){
                    float along=Vector2.Dot(v-head,spine)/spine.sqrMagnitude;
                    if((mask&1)!=0)leftRoot=Mathf.Max(leftRoot,along);
                    if((mask&2)!=0)rightRoot=Mathf.Max(rightRoot,along);
                }
            }
            return found==3&&leftRoot>0&&rightRoot>0;
        }
        private bool BackingSide(PaperSheet.Face face,int mask)
        {
            bool containsTip=false;
            foreach(var v in face.points){
                if(!coordinated && !InTriangle(v,tip,left,head)&&!InTriangle(v,tip,head,right))return false;
                containsTip|=(v-tip).sqrMagnitude<.00001f;
            }
            if(containsTip)return true;
            foreach(var v in face.points){
                if((mask&1)!=0&&Cross(left-head,v-head)*Mathf.Sign(Cross(left-head,tip-head))/(left-head).magnitude<-.0001f)return false;
                if((mask&2)!=0&&Cross(right-head,v-head)*Mathf.Sign(Cross(right-head,tip-head))/(right-head).magnitude<-.0001f)return false;
            }
            return true;
        }
        private static bool Finite(Vector2 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y);
        private static bool InTriangle(Vector2 p,Vector2 a,Vector2 b,Vector2 c)
        {
            float x=Cross(b-a,p-a),y=Cross(c-b,p-b),z=Cross(a-c,p-c);
            return (x>=-.0001f&&y>=-.0001f&&z>=-.0001f)||(x<=.0001f&&y<=.0001f&&z<=.0001f);
        }
        private static bool SharedSegment(PaperSheet.Face f,Vector3 a,Vector3 b)
        {
            var axis=b-a;float length=axis.magnitude;if(length<.001f)return false;axis/=length;
            for(int i=0;i<f.points.Length;i++){
                var x=f.Original(f.points[i])-a;var y=f.Original(f.points[(i+1)%f.points.Length])-a;
                if(Vector3.Cross(x,axis).sqrMagnitude>.000001f||Vector3.Cross(y,axis).sqrMagnitude>.000001f)continue;
                float u=Vector3.Dot(x,axis),v=Vector3.Dot(y,axis);
                if(Mathf.Min(length,Mathf.Max(u,v))-Mathf.Max(0,Mathf.Min(u,v))>.001f)return true;
            }return false;
        }
        private static bool Bisector(Vector2 t,Vector2 h,Vector2 side,out Vector2 result)
        {
            result=default;var direction=(h-t).normalized+(side-t).normalized;var edge=h-side;
            float determinant=Cross(direction,edge);if(Mathf.Abs(determinant)<.00001f)return false;
            float u=Cross(side-t,edge)/determinant,v=Cross(side-t,direction)/determinant;
            if(u<=0||v<=.0001f||v>=.9999f)return false;result=t+u*direction;return true;
        }
        private static FoldStep Line(Vector2 a,Vector2 b){var n=new Vector2(-(b-a).y,(b-a).x);return new FoldStep{creaseNormal=n,creaseOffset=Vector2.Dot(n,a)};}
        private bool Build()
        {
            var upperLines=new[]{Line(tip,p),Line(tip,q),Line(p,q)};
            var lowerLines=new[]{Line(backLeft,p),Line(backRight,q),Line(p,q)};
            foreach(var f in source){
                if(!active.Contains(f)){pieces.Add(new Piece{face=f});continue;}
                var lines=continuous && !upper.Contains(f)?lowerLines:upperLines;
                var parts=new List<PaperSheet.Face>{f};
                foreach(var line in lines){var next=new List<PaperSheet.Face>();foreach(var part in parts){
                    var a=PaperSheet.Clip(part,line,1);var b=PaperSheet.Clip(part,line,-1);
                    if(a!=null)next.Add(a);if(b!=null)next.Add(b);
                }parts=next;}
                float area=0;
                foreach(var part in parts){
                    area+=PaperSheet.Area(part.points);Vector2 centre=Vector2.zero;foreach(var v in part.points)centre+=v;centre/=part.points.Length;
                    Motion mode=Motion.Fixed;
                    if(lines[0].Distance(centre)*lines[0].Distance(left)>.00001f)mode=upper.Contains(f)?Motion.LeftUpper:Motion.LeftLower;
                    else if(lines[1].Distance(centre)*lines[1].Distance(right)>.00001f)mode=upper.Contains(f)?Motion.RightUpper:Motion.RightLower;
                    else if(upper.Contains(f)&&lines[2].Distance(centre)*lines[2].Distance(tip)>.00001f)mode=Motion.Centre;
                    pieces.Add(new Piece{face=part,motion=mode});
                }
                if(Mathf.Abs(area-PaperSheet.Area(f.points))>.001f)return false;
            }
            return pieces.Count<=256;
        }
        public void MarkGuides(List<PaperSheet.Guide> guides)
        {
            guides.Clear();foreach(var f in upper)foreach(var ends in new[]{new[]{tip,p},new[]{tip,q},new[]{p,q}}){
                var a=ends[0];var b=ends[1];if(PaperSheet.Trim(f,ref a,ref b))guides.Add(new PaperSheet.Guide{face=f,layer=f.layer,a=a,b=b});
            }
            if(coordinated)foreach(var entry in supportSides){
                var f=entry.Key;
                if((entry.Value&1)!=0){var a=backLeft;var b=p;if(PaperSheet.Trim(f,ref a,ref b))guides.Add(new PaperSheet.Guide{face=f,layer=f.layer,a=a,b=b});}
                if((entry.Value&2)!=0){var a=backRight;var b=q;if(PaperSheet.Trim(f,ref a,ref b))guides.Add(new PaperSheet.Guide{face=f,layer=f.layer,a=a,b=b});}
            }
        }
        private static Quaternion Basis(Vector3 u,Vector3 v)=>Quaternion.LookRotation(u,Vector3.Cross(u,v));
        private static Matrix4x4 Map(Vector2 a,Vector2 b,Vector2 c,Vector3 x,Vector3 y,Vector3 z)
        {
            var rotation=Basis(y-x,z-x)*Quaternion.Inverse(Basis(V(b-a),V(c-a)));
            return Matrix4x4.TRS(x-rotation*V(a),rotation,Vector3.one);
        }
        private bool SidePose(Vector2 side,Vector2 fixedCorner,Vector2 returnTip,Vector3 movingTip,out Vector3 result)
        {
            var a=V(returnTip);var axis=(V(fixedCorner)-a).normalized;var rest=V(side);
            var centre=a+axis*Vector3.Dot(rest-a,axis);float radius=(rest-centre).magnitude;
            var delta=movingTip-centre;var projected=Vector3.ProjectOnPlane(delta,axis);
            float rhs=(radius*radius+delta.sqrMagnitude-(rest-V(tip)).sqrMagnitude)*.5f;
            if(projected.sqrMagnitude<.00000001f){result=rest;return Mathf.Abs(rhs)<.0001f;}
            var mid=projected*(rhs/projected.sqrMagnitude);float height=radius*radius-mid.sqrMagnitude;
            if(height<-.00005f){result=default;return false;}
            var lift=Vector3.Cross(axis,projected).normalized*Mathf.Sqrt(Mathf.Max(0,height));
            var x=centre+mid+lift;var y=centre+mid-lift;result=(x-rest).sqrMagnitude<(y-rest).sqrMagnitude?x:y;return true;
        }
        private static Vector2 Reflect(Vector2 point,Vector2 a,Vector2 b)
        {var axis=(b-a).normalized;var delta=point-a;return a+2*axis*Vector2.Dot(axis,delta)-delta;}
        private static bool Fits(Vector2 a,Vector2 b,Vector2 c,Vector3 x,Vector3 y,Vector3 z)=>
            Mathf.Abs(Vector2.Distance(a,b)-Vector3.Distance(x,y))<.0005f &&
            Mathf.Abs(Vector2.Distance(a,c)-Vector3.Distance(x,z))<.0005f &&
            Mathf.Abs(Vector2.Distance(b,c)-Vector3.Distance(y,z))<.0005f;
        private bool CanOpen(int sign)
        {
            var cached=sign>0?positiveClearance:negativeClearance;if(cached.HasValue)return cached.Value;
            var hinge=(V(q)-V(p)).normalized;float direction=Mathf.Sign(Vector3.Dot(Vector3.Cross(hinge,V(tip-p)),Vector3.up));
            var movedTip=V(p)+Quaternion.AngleAxis(sign*direction,hinge)*V(tip-p);
            bool allowed=SidePose(left,p,backLeft,movedTip,out var movedLeft)&&SidePose(right,q,backRight,movedTip,out _);
            SidePose(right,q,backRight,movedTip,out var movedRight);
            var seed=new[]{Matrix4x4.identity,Map(tip,p,q,movedTip,V(p),V(q)),Map(tip,p,left,movedTip,V(p),movedLeft),Map(backLeft,p,left,V(backLeft),V(p),movedLeft),Map(tip,q,right,movedTip,V(q),movedRight),Map(backRight,q,right,V(backRight),V(q),movedRight)};
            foreach(var a in pieces)if(a.motion!=Motion.Fixed){Vector2 c=Vector2.zero;foreach(var v in a.face.points)c+=v;c/=a.face.points.Length;float dy=seed[(int)a.motion].MultiplyPoint3x4(V(c)).y;
                foreach(var b in pieces)if(b.motion==Motion.Fixed && (b.face.layer-a.face.layer)*dy>.0000001f && PaperSheet.SameFrame(a.face,b.face) && PaperSheet.Turn.Overlap(a.face,b.face)){allowed=false;Failure="A stationary layer blocks the opening direction.";}
            }
            if(sign>0)positiveClearance=allowed;else negativeClearance=allowed;return allowed;
        }
        public bool Pose(float angle)
        {
            if(!coordinated)return PoseCore(angle);
            if(sheet.Faces!=source||!float.IsFinite(angle)||Mathf.Abs(angle)>180)return false;
            if(angle==lastAngle)return true;
            float previousAngle=lastAngle;var previousTip=Tip;rollback.Clear();rollback.AddRange(preview);
            float from=float.IsNaN(lastAngle)?0:lastAngle;
            if(float.IsNaN(lastAngle)&&!PoseCore(0))return false;
            while(from!=angle){
                from=Mathf.MoveTowards(from,angle,2f);
                if(PoseCore(from))continue;
                preview.Clear();preview.AddRange(rollback);lastAngle=previousAngle;Tip=previousTip;return false;
            }
            return PoseCore(angle);
        }
        private bool PoseCore(float angle)
        {
            if(sheet.Faces!=source||!float.IsFinite(angle)||Mathf.Abs(angle)>180)return false;
            if(angle==lastAngle)return true;
            if(angle==0){lastAngle=angle;preview.Clear();if(coordinated)foreach(var piece in pieces)preview.Add(piece.face);else preview.AddRange(source);Tip=basis.World(tip,(basis.layer+.5f)*PaperSheet.Thickness);return true;}
            if(continuous && !CanOpen(angle>0?1:-1)){return false;}
            var axis=(V(q)-V(p)).normalized;float direction=Mathf.Sign(Vector3.Dot(Vector3.Cross(axis,V(tip-p)),Vector3.up));
            var rotation=Quaternion.AngleAxis(angle*direction,axis);var movedTip=V(p)+rotation*V(tip-p);
            if(!SidePose(left,p,backLeft,movedTip,out var movedLeft)||!SidePose(right,q,backRight,movedTip,out var movedRight))return false;
            bool atFlat=Mathf.Abs(angle)>=179.999f;
            if(atFlat){movedTip=V(Reflect(tip,p,q));movedLeft=V(Reflect(left,backLeft,p));movedRight=V(Reflect(right,backRight,q));}
            if(!Fits(tip,p,left,movedTip,V(p),movedLeft)||!Fits(tip,q,right,movedTip,V(q),movedRight)||
               !Fits(backLeft,p,left,V(backLeft),V(p),movedLeft)||!Fits(backRight,q,right,V(backRight),V(q),movedRight)){Failure="rigid metric closure";return false;}
            maps[0]=Matrix4x4.identity;maps[1]=Map(tip,p,q,movedTip,V(p),V(q));
            maps[2]=Map(tip,p,left,movedTip,V(p),movedLeft);maps[3]=Map(backLeft,p,left,V(backLeft),V(p),movedLeft);
            maps[4]=Map(tip,q,right,movedTip,V(q),movedRight);maps[5]=Map(backRight,q,right,V(backRight),V(q),movedRight);
            bool closed=Mathf.Abs(angle)>=179.999f;float press=Mathf.SmoothStep(0,1,Mathf.Abs(angle)/180);trial.Clear();
            foreach(var piece in pieces){
                var f=piece.face;if(piece.motion==Motion.Fixed){trial.Add(f);continue;}
                int targetLayer=piece.motion==Motion.Centre?(angle>0?highest+1:lowest-1):
                    piece.motion==Motion.LeftUpper||piece.motion==Motion.RightUpper?(angle>0?highest+2:lowest-2):f.layer+(angle>0?1:-1);
                float height=(Mathf.Lerp(f.layer,targetLayer,press)+.5f)*PaperSheet.Thickness;
                var map=maps[(int)piece.motion];var frame=basis.frame*Matrix4x4.Translate(Vector3.up*height)*map*Matrix4x4.Translate(-Vector3.up*((f.layer+.5f)*PaperSheet.Thickness));
                if(continuous)frame=basis.frame*map;
                // Check every polygon corner against its rigid triangle frame.
                foreach(var v in f.points){var world=frame.MultiplyPoint3x4(V(v));if(!float.IsFinite(world.x)||!float.IsFinite(world.y)||!float.IsFinite(world.z))return false;}
                if(closed){
                    var points=new Vector2[f.points.Length];var chart=map;
                    for(int i=0;i<points.Length;i++){var v=map.MultiplyPoint3x4(V(f.points[i]));if(Mathf.Abs(v.y)>.0005f)return false;points[i]=new Vector2(v.x,v.z);}
                    if(Mathf.Abs(PaperSheet.Area(points)-PaperSheet.Area(f.points))>.0005f)return false;
                    chart.m01=chart.m10=chart.m12=chart.m21=chart.m13=0;chart.m11=1;
                    bool back=f.back^(chart.m00*chart.m22-chart.m02*chart.m20<0);
                    trial.Add(new PaperSheet.Face(points,targetLayer,back,basis.frame,f.material*chart.inverse,f));
                }else trial.Add(new PaperSheet.Face(f.points,f.layer,f.back,frame,f.material,f));
            }
            neutral.Clear();
            if(continuous)foreach(var f in trial)neutral.Add(new PaperSheet.Face(f.points,0,f.back,f.frame*Matrix4x4.Translate(-Vector3.up*(PaperSheet.Thickness*.5f)),f.material,f));
            var contact=continuous?neutral:trial;
            for(int i=0;i<trial.Count;i++)for(int j=0;j<i;j++){
                var a=trial[i];var b=trial[j];
                if(continuous && PaperFoldTopology.SharedEdge(a,b,out var u,out var v) &&
                   (Vector3.Distance(PaperFoldTopology.Position(a,u),PaperFoldTopology.Position(b,u))>.002f || Vector3.Distance(PaperFoldTopology.Position(a,v),PaperFoldTopology.Position(b,v))>.002f)){Failure="seam "+i+"/"+j;return false;}
                a=contact[i];b=contact[j];if(PaperSheet.Turn.Intersects(a,b,b.frame.inverse*a.frame)||PaperSheet.Turn.Intersects(b,a,a.frame.inverse*b.frame)){Failure="contact "+i+"/"+j;return false;}
            }
            if(continuous && closed){
                var flatFaces=new List<PaperSheet.Face>(trial);
                if(!PoseCore(Mathf.Sign(angle)*179.9f))return false;
                var preceding=new List<PaperSheet.Face>(preview);
                if(!PaperFlatStackOrder.Apply(flatFaces,preceding)){Failure="flat contact ordering";return false;}
                trial.Clear();trial.AddRange(flatFaces);
            }
            lastAngle=angle;preview.Clear();preview.AddRange(trial);Tip=basis.frame.MultiplyPoint3x4(movedTip+Vector3.up*((basis.layer+.5f)*PaperSheet.Thickness));return true;
        }
    }
}

