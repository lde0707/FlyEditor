using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // Calculated when equipping a sheet. Grip and nose are actual points on the folded paper.
    public sealed class AircraftHold
    {
        public Vector3 Grip { get; private set; }
        public Vector3 Nose { get; private set; }
        public Vector3 Forward { get; private set; }
        public Vector3 Up { get; private set; }

        private struct Section
        {
            public Vector3 a,b,normal;
            public int layer;
            public Vector3 Mid => (a+b)*.5f;
        }
        private readonly List<Section> cut=new List<Section>();
        private readonly List<Vector3> normals=new List<Vector3>();
        private Vector3 centre;
        private float mass,start,end;

        public static AircraftHold Measure(PaperSheet sheet)
        {
            var result=new AircraftHold();
            result.FindAxis(sheet);
            result.FindGrip(sheet);
            result.FindNose(sheet);
            return result;
        }
        private static Vector3 MidSurface(PaperSheet.Face face,Vector2 p)
            =>face.World(p,(face.layer+.5f)*PaperSheet.Thickness);

        private void FindAxis(PaperSheet sheet)
        {
            // The longitudinal crease direction lies in both wings and the keel.
            // Weight by material area so outlines and triangle subdivision do not cast extra votes.
            var covariance=new Matrix4x4();
            foreach(var face in sheet.Faces){
                var normal=face.frame.MultiplyVector(Vector3.up).normalized;
                float area=PaperSheet.Area(face.points);
                bool known=false;
                foreach(var n in normals)if(Mathf.Abs(Vector3.Dot(n,normal))>.99999f){known=true;break;}
                if(!known)normals.Add(normal);
                for(int i=0;i<3;i++)for(int j=0;j<3;j++)covariance[i,j]+=area*normal[i]*normal[j];
                var a=MidSurface(face,face.points[0]);
                for(int i=2;i<face.points.Length;i++){
                    var b=MidSurface(face,face.points[i-1]);var c=MidSurface(face,face.points[i]);
                    float weight=Vector3.Cross(b-a,c-a).magnitude*.5f;
                    centre+=weight*((a+b+c)/3f);mass+=weight;
                }
            }
            centre/=Mathf.Max(.0001f,mass);
            Forward=Vector3.ProjectOnPlane(Vector3.right,normals[0]).normalized;
            if(Forward.sqrMagnitude<.5f)Forward=Vector3.ProjectOnPlane(Vector3.forward,normals[0]).normalized;
            if(normals.Count==1)Forward=PlanarLongAxis(sheet,Forward,normals[0]);
            float best=AxisError(Forward,covariance);
            for(int i=0;i<normals.Count;i++)for(int j=0;j<i;j++){
                var axis=Vector3.Cross(normals[i],normals[j]);
                if(axis.sqrMagnitude<.0025f)continue;
                axis.Normalize();float error=AxisError(axis,covariance);
                if(error<best){best=error;Forward=axis;}
            }
            // Resolve a sign tie consistently for blunt or symmetric designs.
            float sign=Mathf.Abs(Forward.x)>.1f?Forward.x:Mathf.Abs(Forward.z)>.1f?Forward.z:Forward.y;
            if(sign<0)Forward=-Forward;
            start=float.PositiveInfinity;end=float.NegativeInfinity;
            foreach(var face in sheet.Faces)foreach(var p in face.points){
                float x=Vector3.Dot(MidSurface(face,p),Forward);
                start=Mathf.Min(start,x);end=Mathf.Max(end,x);
            }
        }
        private Vector3 PlanarLongAxis(PaperSheet sheet,Vector3 x,Vector3 normal)
        {
            // A completely flat stack has no dihedral to tell us its nose direction.
            // Use the in-plane material variance so a long folded strip works in either orientation.
            var y=Vector3.Cross(x,normal).normalized;
            float xx=0,yy=0,xy=0;
            foreach(var f in sheet.Faces){
                var a=MidSurface(f,f.points[0])-centre;
                for(int i=2;i<f.points.Length;i++){
                    var b=MidSurface(f,f.points[i-1])-centre;var c=MidSurface(f,f.points[i])-centre;
                    float area=Vector3.Cross(b-a,c-a).magnitude*.5f;
                    var u=new Vector3(Vector3.Dot(a,x),Vector3.Dot(b,x),Vector3.Dot(c,x));
                    var v=new Vector3(Vector3.Dot(a,y),Vector3.Dot(b,y),Vector3.Dot(c,y));
                    float sx=u.x+u.y+u.z,sy=v.x+v.y+v.z;
                    xx+=area*(sx*sx+u.sqrMagnitude);yy+=area*(sy*sy+v.sqrMagnitude);
                    xy+=area*(sx*sy+Vector3.Dot(u,v));
                }
            }
            if(Mathf.Abs(xx-yy)+2*Mathf.Abs(xy)<.001f*(xx+yy))return x;
            float angle=.5f*Mathf.Atan2(2*xy,xx-yy);
            return x*Mathf.Cos(angle)+y*Mathf.Sin(angle);
        }
        private float AxisError(Vector3 v,Matrix4x4 covariance)
            =>Vector3.Dot(v,covariance.MultiplyVector(v))/mass+.0001f*(1f-Mathf.Abs(v.x));

        private void Slice(PaperSheet sheet,float at)
        {
            cut.Clear();
            foreach(var face in sheet.Faces){
                var normal=face.frame.MultiplyVector(Vector3.up);
                var axis=Vector3.Cross(Forward,normal);
                if(axis.sqrMagnitude<.000001f)continue;
                axis.Normalize();
                float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
                var segment=new Section{normal=normal,layer=face.layer};
                var p=MidSurface(face,face.points[face.points.Length-1]);float d=Vector3.Dot(p,Forward)-at;
                foreach(var v in face.points){
                    var q=MidSurface(face,v);float e=Vector3.Dot(q,Forward)-at;
                    if(Mathf.Abs(e)<.00001f || ((d<0)!=(e<0))){
                        var hit=Mathf.Abs(e)<.00001f?q:Vector3.Lerp(p,q,d/(d-e));
                        float h=Vector3.Dot(hit,axis);
                        if(h<lo){lo=h;segment.a=hit;}
                        if(h>hi){hi=h;segment.b=hit;}
                    }
                    p=q;d=e;
                }
                if(hi-lo>.0001f)cut.Add(segment);
            }
        }

        private void FindGrip(PaperSheet sheet)
        {
            float length=end-start;
            float at=Mathf.Clamp(Vector3.Dot(centre,Forward),start+.25f*length,end-.25f*length);
            float best=0;
            foreach(float shift in new[]{0f,-.085f,.085f}){
                Slice(sheet,at+shift*length);
                foreach(var body in cut){
                    Vector3 up=(body.b-body.a).normalized;
                    Vector3 side=Vector3.Cross(Forward,up);
                    float left=0,right=0,roots=0,weight=0;
                    foreach(var wing in cut){
                        float a=Vector3.Dot(wing.a-body.Mid,side),b=Vector3.Dot(wing.b-body.Mid,side);
                        float reach=Mathf.Abs(b-a);
                        if(reach<(wing.b-wing.a).magnitude*.5f)continue;
                        left=Mathf.Max(left,-Mathf.Min(a,b));right=Mathf.Max(right,Mathf.Max(a,b));
                        // The inboard ends of the two wings indicate which end of the keel is up.
                        var root=Mathf.Abs(a)<Mathf.Abs(b)?wing.a:wing.b;
                        roots+=reach*Vector3.Dot(root-body.Mid,up);weight+=reach;
                    }
                    float balance=Mathf.Min(left,right);
                    if(balance<.12f*(left+right) || weight<=0)continue;
                    float rise=roots/weight;
                    if(Mathf.Abs(rise)<.12f*(body.b-body.a).magnitude)continue;
                    float score=balance*(1f-Mathf.Abs(shift)*1.6f);
                    if(score<=best)continue;
                    best=score;Up=rise<0?-up:up;
                    Grip=body.Mid-Up*((body.b-body.a).magnitude*.22f);
                }
            }
            if(best==0)FindCentralContact(sheet,at);
        }
        private void FindCentralContact(PaperSheet sheet,float at)
        {
            // A V-fold or a flat sheet can have wings without a distinct vertical keel.
            // Cross the centre of the span on actual material, rather than its bounding box.
            Slice(sheet,at);
            if(cut.Count==0){
                var face=sheet.Faces[0];
                Vector2 p=Vector2.zero;foreach(var v in face.points)p+=v;
                Grip=MidSurface(face,p/face.points.Length);
                Up=Vector3.ProjectOnPlane(face.frame.MultiplyVector(Vector3.up),Forward).normalized;
                if(Up.sqrMagnitude<.5f)Up=Vector3.Cross(Forward,Mathf.Abs(Forward.y)<.9f?Vector3.up:Vector3.right).normalized;
                return;
            }
            if(TryWingJunction())return;
            Vector3 a=cut[0].a,b=cut[0].b;
            float longest=(b-a).sqrMagnitude;
            foreach(var u in cut)foreach(var v in cut){
                Wider(u.a,v.a,ref a,ref b,ref longest);Wider(u.a,v.b,ref a,ref b,ref longest);
                Wider(u.b,v.b,ref a,ref b,ref longest);
            }
            var side=(b-a).normalized;
            var mid=(a+b)*.5f;
            float best=float.PositiveInfinity;
            foreach(var seg in cut){
                var delta=seg.b-seg.a;
                float spread=Vector3.Dot(delta,side);
                float t=Mathf.Abs(spread)>.0001f?Mathf.Clamp01(Vector3.Dot(mid-seg.a,side)/spread):.5f;
                var contact=seg.a+t*delta;
                float lateral=Vector3.Dot(contact-mid,side);
                float cost=lateral*lateral+.05f*(contact-centre).sqrMagnitude;
                if(cost<best){best=cost;Grip=contact;}
            }
            Up=Vector3.Cross(side,Forward).normalized;
            float lift=Vector3.Dot(mid-Grip,Up);
            if(Mathf.Abs(lift)<.01f)lift=Mathf.Abs(Up.y)>.001f?Up.y:Up.z;
            if(lift<0)Up=-Up;
        }
        private bool TryWingJunction()
        {
            // Two unequal wings can meet well away from the centre of their bounding box.
            float best=0;
            for(int i=0;i<cut.Count;i++)
            for(int j=0;j<i;j++){
                var a=cut[i];var b=cut[j];
                for(int ends=0;ends<4;ends++){
                    bool reverseA=(ends&1)!=0,reverseB=(ends&2)!=0;
                    var root=reverseA?a.b:a.a;var other=reverseB?b.b:b.a;
                    if((root-other).sqrMagnitude>9*PaperSheet.Thickness*PaperSheet.Thickness)continue;
                    var u=(reverseA?a.a:a.b)-root;var v=(reverseB?b.a:b.b)-other;
                    var du=u.normalized;var dv=v.normalized;
                    float bend=Vector3.Cross(du,dv).magnitude;
                    if(bend<.08f)continue;
                    float score=Mathf.Min(u.magnitude,v.magnitude)*bend;
                    if(score<=best)continue;
                    best=score;Grip=root;Up=(du+dv).normalized;
                }
            }
            return best>0;
        }
        private static void Wider(Vector3 a,Vector3 b,ref Vector3 left,ref Vector3 right,ref float longest)
        {
            float d=(b-a).sqrMagnitude;
            if(d>longest){longest=d;left=a;right=b;}
        }
        private void FindNose(PaperSheet sheet)
        {
            var side=Vector3.Cross(Forward,Up);
            float tail=Breadth(sheet,Mathf.Lerp(start,end,.18f),side);
            float front=Breadth(sheet,Mathf.Lerp(start,end,.82f),side);
            bool reverse=front>tail+Mathf.Max(front,tail)*.12f;
            if(reverse)Forward=-Forward;
            float leading=reverse?-start:end;
            float best=float.PositiveInfinity;
            foreach(var face in sheet.Faces){
                var a=MidSurface(face,face.points[face.points.Length-1]);
                foreach(var p in face.points){
                    var b=MidSurface(face,p);
                    if(Vector3.Dot(b,Forward)>=leading-.0001f){
                        var tip=b;
                        // A blunt leading edge can have its centre between two vertices.
                        if(Vector3.Dot(a,Forward)>=leading-.0001f){
                            float span=Vector3.Dot(a-b,side);
                            if(Mathf.Abs(span)>.0001f)tip+=Mathf.Clamp01(Vector3.Dot(Grip-b,side)/span)*(a-b);
                        }
                        float distance=(tip-Grip).sqrMagnitude;
                        if(distance<best){best=distance;Nose=tip;}
                    }
                    a=b;
                }
            }
        }
        // Slice in the same axes used to equip the mesh. Union the projected strips so
        // overlapping plies add reinforcement but do not count as extra lifting surface.
        internal PaperFlightProfile MeasurePerformance(PaperSheet sheet)
        {
            const int stations=32;
            Vector3 side=Vector3.Cross(Forward,Up);
            float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
            foreach(var f in sheet.Faces)foreach(var v in f.points){
                float x=Vector3.Dot(MidSurface(f,v),Forward);lo=Mathf.Min(lo,x);hi=Mathf.Max(hi,x);
            }
            var strips=new List<Vector2>(sheet.Faces.Count);
            float footprint=0,matched=0,frontOverlap=0,frontLength=0,span=0,noseSpan=0;
            float dx=(hi-lo)/stations;
            for(int i=0;i<stations;i++){
                Slice(sheet,lo+(i+.5f)*dx);strips.Clear();
                if(i>=stations*3/4)MeasureLayers(sheet.Height,ref frontOverlap,ref frontLength);
                foreach(var s in cut){
                    float a=Vector3.Dot(s.a-Grip,side),b=Vector3.Dot(s.b-Grip,side);
                    if(Mathf.Abs(b-a)<.00001f)continue;
                    strips.Add(new Vector2(Mathf.Min(a,b),Mathf.Max(a,b)));
                }
                if(strips.Count==0)continue;
                int used=MergeStrips(strips);
                for(int j=0;j<used;j++)footprint+=(strips[j].y-strips[j].x)*dx;
                matched+=PairedWidth(strips,used)*dx;
                float width=strips[used-1].y-strips[0].x;
                span=Mathf.Max(span,width);
                if(i>=stations*3/4)noseSpan+=width;
            }
            float length=Mathf.Max(.001f,hi-lo);
            float wide=Mathf.InverseLerp(.16f,.84f,footprint/mass);
            float narrow=1-Mathf.InverseLerp(.24f,.93f,span/length);
            float taper=Mathf.Clamp01(1-noseSpan/(stations/4*Mathf.Max(.001f,span)));
            float balance=Mathf.Clamp01(matched/Mathf.Max(.001f,footprint));
            float noseLayers=Mathf.Clamp01(frontOverlap/Mathf.Max(.001f,frontLength));
            float compact=1-wide;
            float streamlined=Mathf.InverseLerp(.15f,.78f,.65f*narrow+.35f*taper);
            float lift=wide*(.62f+.38f*balance);
            float reinforcement=Mathf.Clamp01(.8f*noseLayers+.2f*compact);
            return new PaperFlightProfile(streamlined,.55f*reinforcement+.35f*streamlined+.10f*balance,
                .7f*(1-lift)+.3f*streamlined,lift,reinforcement);
        }
        private static int MergeStrips(List<Vector2> strips)
        {
            strips.Sort(CompareStarts);
            int used=1;
            for(int j=1;j<strips.Count;j++){
                var last=strips[used-1];var next=strips[j];
                if(next.x>last.y){strips[used++]=next;continue;}
                last.y=Mathf.Max(last.y,next.y);strips[used-1]=last;
            }
            return used;
        }
        private static float PairedWidth(List<Vector2> strips,int used)
        {
            // Intersect the covered intervals with their reflection across the fuselage.
            int left=0,right=used-1;
            float width=0;
            while(left<used && right>=0){
                var a=strips[left];var b=strips[right];
                width+=Mathf.Max(0,Mathf.Min(a.y,-b.x)-Mathf.Max(a.x,-b.y));
                if(a.y<=-b.x)left++;else right--;
            }
            return width;
        }
        private void MeasureLayers(float height,ref float overlap,ref float total)
        {
            // Length in the cross section counts layers even on an upright keel,
            // whose top-down projection is nearly zero.
            for(int i=0;i<cut.Count;i++){
                var a=cut[i];var d=a.b-a.a;float length=d.magnitude;
                var axis=d/length;total+=length;
                for(int j=0;j<i;j++){
                    var b=cut[j];
                    if(Mathf.Abs(Vector3.Dot(a.normal,b.normal))<.995f)continue;
                    float gap=Mathf.Min(.015f*height,PaperSheet.Thickness*(1.5f+Mathf.Abs(a.layer-b.layer)));
                    if(Vector3.Cross(b.Mid-a.a,axis).sqrMagnitude>gap*gap)continue;
                    float lo=Vector3.Dot(b.a-a.a,axis),hi=Vector3.Dot(b.b-a.a,axis);
                    overlap+=Mathf.Max(0,Mathf.Min(length,Mathf.Max(lo,hi))-Mathf.Max(0,Mathf.Min(lo,hi)));
                }
            }
        }
        private static int CompareStarts(Vector2 a,Vector2 b)=>a.x.CompareTo(b.x);
        private float Breadth(PaperSheet sheet,float at,Vector3 side)
        {
            Slice(sheet,at);
            float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
            foreach(var seg in cut){
                float a=Vector3.Dot(seg.a,side),b=Vector3.Dot(seg.b,side);
                lo=Mathf.Min(lo,Mathf.Min(a,b));hi=Mathf.Max(hi,Mathf.Max(a,b));
            }
            return Mathf.Max(0,hi-lo);
        }
    }
}
