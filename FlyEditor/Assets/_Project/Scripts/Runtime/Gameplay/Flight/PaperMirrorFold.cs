using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // Reflect the gesture on the original paper, not in camera or table coordinates.
    // Each half is evaluated against the same starting pose. The pair is committed as one fold.
    public sealed class PaperMirrorFold
    {
        private sealed class Half
        {
            private readonly PaperSheet sheet;
            private readonly PaperSheet.Turn turn;
            private PaperCornerPull pull;
            private readonly MirrorAxis seam;
            private readonly PaperInsideReverse reverse;
            private readonly float sign;
            internal IReadOnlyList<PaperSheet.Face> result;
            internal bool Valid=>turn!=null || pull!=null || reverse!=null;
            internal bool Flexible=>pull!=null && pull.Step.flexible;
            internal bool UseFlexible()
            {
                if(pull==null)return false;
                if(pull.Step.flexible)return true;
                var step=pull.Step;step.flexible=true;step.singlePanel=false;step.flap=0;
                var connected=PaperCornerPull.Create(sheet,step,seam);
                if(connected==null)return false;
                pull=connected;return true;
            }
            internal Half(PaperSheet sheet,FoldStep step,float sign,MirrorAxis seam=MirrorAxis.Off)
            {
                this.sheet=sheet;this.sign=sign;this.seam=seam;step.mirror=MirrorAxis.Off;
                if(step.insideReverse)reverse=PaperInsideReverse.Create(sheet,step);
                else if(step.cornerPull)pull=PaperCornerPull.Create(sheet,step,seam);
                else turn=sheet.Plan(step);
            }
            internal bool PressesInto(Half other,float angle)
            {
                float direction=angle*sign;
                foreach(var cover in other.sheet.Faces){
                    if(turn!=null){
                        foreach(var moving in turn.movingFaces)
                            if(Presses(moving,cover,direction))return true;
                    }else if(pull!=null && Presses(sheet.Faces[pull.Step.anchor],cover,direction))return true;
                }
                return false;
            }
            private static bool Presses(PaperSheet.Face moving,PaperSheet.Face cover,float direction)=>
                PaperSheet.SameFrame(moving,cover) && (cover.layer-moving.layer)*direction>0 &&
                PaperSheet.Turn.Overlap(moving,cover);
            internal bool Pose(float angle)
            {
                if(angle==0){result=sheet.Faces;return true;}
                if(reverse!=null){
                    if(!reverse.Pose(angle))return false;
                    result=reverse.Preview;return true;
                }
                if(pull!=null){
                    if(!pull.Pose(angle*sign)){
                        var step=pull.Step;
                        if(step.flexible)return false;
                        step.flexible=true;step.singlePanel=false;step.flap=0;
                        var connected=PaperCornerPull.Create(sheet,step,seam);
                        if(connected==null || !connected.Pose(angle*sign))return false;
                        pull=connected;
                    }
                    result=pull.Preview;return true;
                }
                if(!sheet.Finish(turn,angle*sign))return false;
                result=sheet.Faces;sheet.Undo();return true;
            }
        }
        private readonly PaperSheet source;
        private readonly IReadOnlyList<PaperSheet.Face> initial;
        private Half primary,partner;
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>();
        private float shown=float.NaN;
        public FoldStep Step { get; private set; }
        public IReadOnlyList<PaperSheet.Face> Preview=>preview;
        private PaperMirrorFold(PaperSheet sheet,FoldStep step)
        {source=sheet;initial=sheet.Faces;Step=step;}
        public bool IsCurrent(PaperSheet sheet)=>source==sheet && initial==sheet.Faces;
        public static PaperMirrorFold Create(PaperSheet sheet,FoldStep step)
        {
            if(sheet.Count>=sheet.FoldLimit || step.anchor<0 || step.anchor>=sheet.Faces.Count ||
                (step.mirror!=MirrorAxis.Horizontal && step.mirror!=MirrorAxis.Vertical))return null;
            var pair=new PaperMirrorFold(sheet,step);
            return pair.Prepare()?pair:null;
        }
        internal Vector3 PartnerPoint(Vector3 material)=>Reflect(material);
        internal static FoldStep CentreLine(PaperSheet sheet,PaperSheet.Face f,MirrorAxis axis)
        {
            var m=f.material;
            bool horizontal=axis==MirrorAxis.Horizontal;
            return new FoldStep(horizontal?CreaseFamily.Horizontal:CreaseFamily.Vertical,0,1){
                creaseNormal=horizontal?new Vector2(m.m20,m.m22):new Vector2(m.m00,m.m02),
                creaseOffset=horizontal?sheet.Height*.5f-m.m23:sheet.Width*.5f-m.m03};
        }
        private Vector3 Reflect(Vector3 p)
        {
            if(Step.mirror==MirrorAxis.Horizontal)p.z=source.Height-p.z;else p.x=source.Width-p.x;
            return p;
        }
        private static Vector2 Chart(Matrix4x4 inverse,Vector3 original)
        {var p=inverse.MultiplyPoint3x4(original);return new Vector2(p.x,p.z);}
        private bool Prepare()
        {
            var basis=initial[Step.anchor];
            var centre=CentreLine(source,basis,Step.mirror);
            var normal=Step.Normal;
            var origin=normal*(Step.Position/normal.sqrMagnitude);
            if((!Step.cornerPull || Step.insideReverse) && Mathf.Abs(centre.Distance(origin))<.0001f &&
                Mathf.Abs(Vector2.Dot(centre.Normal.normalized,normal.normalized))>.99999f){
                // There is only one physical crease on the symmetry axis itself.
                primary=new Half(new PaperSheet(source.Format,source.Width,new List<PaperSheet.Face>(initial)),Step,1);
                return primary.Valid;
            }
            var probe=Step.cornerPull?(Step.pullTip+Step.pullStop)*.5f:Step.contact;
            float which=centre.Distance(probe);
            if(Mathf.Abs(which)<.0001f)return false;
            var a=new List<PaperSheet.Face>();var b=new List<PaperSheet.Face>();
            int index=-1;
            for(int i=0;i<initial.Count;i++){
                var f=initial[i];var cut=CentreLine(source,f,Step.mirror);
                int side=which>0?1:-1;
                var left=PaperSheet.Clip(f,cut,side);
                var right=PaperSheet.Clip(f,cut,-side);
                if(left!=null){if(i==Step.anchor)index=a.Count;a.Add(left);}
                if(right!=null)b.Add(right);
            }
            if(index<0 || b.Count==0)return false;
            var one=Step;one.anchor=index;one.flap=Step.flap==0?0:index+1;
            var sub=new PaperSheet(source.Format,source.Width,a);
            primary=new Half(sub,one,1,Step.mirror);
            if(!primary.Valid)return false;
            var target=Reflect(basis.Original(probe));
            var targetTip=Reflect(basis.Original(Step.pullTip));var targetStop=Reflect(basis.Original(Step.pullStop));
            var newOrigin=Reflect(basis.Original(origin));var positive=Reflect(basis.Original(origin+normal.normalized));
            sub=new PaperSheet(source.Format,source.Width,b);
            for(int i=0;i<b.Count;i++){
                var f=b[i];var inv=f.material.inverse;var p=Chart(inv,target);
                if(!f.Contains(p))continue;
                var two=Step;two.anchor=i;two.flap=Step.flap==0?0:i+1;two.contact=p;
                two.pullTip=Chart(inv,targetTip);two.pullStop=Chart(inv,targetStop);
                two.pullLeft=Chart(inv,Reflect(basis.Original(Step.pullLeft)));
                two.pullRight=Chart(inv,Reflect(basis.Original(Step.pullRight)));
                var linePoint=Chart(inv,newOrigin);
                two.creaseNormal=(Chart(inv,positive)-linePoint).normalized;
                two.creaseOffset=Vector2.Dot(two.creaseNormal,linePoint);
                if(two.cornerPull && (!f.Contains(two.pullTip) || !f.Contains(two.pullStop)))continue;
                partner=new Half(sub,two,basis.back==f.back?1:-1,Step.mirror);
                if(partner.Valid)return true;
            }
            return false;
        }
        public bool Pose(float degrees)
        {
            if(initial!=source.Faces || float.IsNaN(degrees) || Mathf.Abs(degrees)>180)return false;
            if(shown==degrees)return true;
            if(partner!=null && degrees!=0 && (primary.PressesInto(partner,degrees) || partner.PressesInto(primary,degrees)))return false;
            if(!primary.Pose(degrees) || (partner!=null && !partner.Pose(degrees)))return false;
            bool useFlexible=primary.Flexible || (partner!=null && partner.Flexible);
            if(useFlexible && !Step.flexible){
                if(!primary.UseFlexible() || (partner!=null && !partner.UseFlexible()))return false;
                if(!primary.Pose(degrees) || (partner!=null && !partner.Pose(degrees)))return false;
            }
            if(partner!=null && degrees!=0 && !CanJoin(primary.result,partner.result))return false;
            preview.Clear();preview.AddRange(primary.result);
            if(partner!=null)preview.AddRange(partner.result);
            if(useFlexible){var step=Step;step.flexible=true;step.flap=0;step.singlePanel=false;Step=step;}
            shown=degrees;return true;
        }
        private bool CanJoin(IReadOnlyList<PaperSheet.Face> left,IReadOnlyList<PaperSheet.Face> right)
        {
            foreach(var a in left){
                var axisA=CentreLine(source,a,Step.mirror);
                bool onSeam=PaperSheet.Intersection(a,axisA,out var p,out var q);
                foreach(var b in right){
                    if(PaperSheet.Turn.Intersects(a,b,b.frame.inverse*a.frame) ||
                       PaperSheet.Turn.Intersects(b,a,a.frame.inverse*b.frame))return false;
                    if(!onSeam || !PaperSheet.Intersection(b,CentreLine(source,b,Step.mirror),out var u,out var v))continue;
                    var m=a.Original(p);var n=a.Original(q);var s=b.Original(u);var t=b.Original(v);
                    float x=Along(m),y=Along(n),z=Along(s),w=Along(t);
                    float lo=Mathf.Max(Mathf.Min(x,y),Mathf.Min(z,w)),hi=Mathf.Min(Mathf.Max(x,y),Mathf.Max(z,w));
                    if(hi-lo<.0001f)continue;
                    if(!Coincide(a,b,lo) || !Coincide(a,b,hi))return false;
                }
            }
            return true;
        }
        private float Along(Vector3 p)=>Step.mirror==MirrorAxis.Horizontal?p.x:p.z;
        private bool Coincide(PaperSheet.Face a,PaperSheet.Face b,float along)
        {
            var material=Step.mirror==MirrorAxis.Horizontal?new Vector3(along,0,source.Height*.5f):new Vector3(source.Width*.5f,0,along);
            var u=a.World(Chart(a.material.inverse,material),(a.layer+.5f)*PaperSheet.Thickness);
            var v=b.World(Chart(b.material.inverse,material),(b.layer+.5f)*PaperSheet.Thickness);
            return (u-v).sqrMagnitude<.03f*.03f;
        }
    }
}
