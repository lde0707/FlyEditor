using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // Inside reverse of a free tip on an existing folded edge. The stop is the
    // common vertex of the old spine and the two new, matching flank creases.
    public sealed class PaperInsideReverse
    {
        private const float FlankAngle=65f;
        private PaperPacketReverse packet;
        private sealed class Leaf
        {
            public PaperSheet.Face source,body,tip;
            public Vector2 stop,handle;
            public Vector3 pivot,radial,flank;
        }
        private readonly Leaf[] leaves=new Leaf[2];
        private readonly List<PaperSheet.Face> trial=new List<PaperSheet.Face>(),preview=new List<PaperSheet.Face>();
        private readonly IReadOnlyList<PaperSheet.Face> initial;
        private readonly Vector3 spine,inward,lateral;
        private readonly float halfOpening;
        private readonly List<PaperSheet.Guide> creases=new List<PaperSheet.Guide>();
        public readonly FoldStep Step;
        public readonly PaperSheet Source;
        public IReadOnlyList<PaperSheet.Face> Preview=>packet!=null?packet.Preview:preview;
        public IReadOnlyList<PaperSheet.Guide> Creases=>packet!=null?packet.Creases:creases;
        public bool UsesPacket=>packet!=null;
        public int PacketPanels=>packet!=null?packet.ActivePanels:0;
        public bool Highlights(int index)=>packet!=null&&packet.Highlights(index);
        public Vector3 TipWorld=>packet!=null?packet.TipWorld:leaves[0].source.World(leaves[0].handle,0);
        public Vector3 StopWorld=>packet!=null?packet.StopWorld:leaves[0].pivot;
        public bool IsCurrent=>initial==Source.Faces;
        private float last=float.NaN;

        private PaperInsideReverse(PaperSheet sheet,FoldStep step,PaperPacketReverse packet){Source=sheet;initial=sheet.Faces;Step=step;this.packet=packet;}
        private PaperInsideReverse(PaperSheet sheet,FoldStep step,Leaf a,Leaf b)
        {
            Source=sheet;initial=sheet.Faces;Step=step;leaves[0]=a;leaves[1]=b;
            spine=(a.source.World(a.handle,0)-a.source.World(a.stop,0)).normalized;
            inward=(a.radial+b.radial).normalized;
            halfOpening=.5f*Vector3.Angle(a.radial,b.radial);
            var across=a.radial-b.radial;
            lateral=across.sqrMagnitude>.00001f?across.normalized:
                a.source.frame.MultiplyVector(Vector3.up)*(a.source.layer>=b.source.layer?1f:-1f);
            foreach(var leaf in leaves){
                var guide=MakeFlank(leaf);
                if(PaperSheet.Intersection(leaf.source,guide,out var p,out var q))
                    creases.Add(new PaperSheet.Guide{face=leaf.source,line=guide,a=p,b=q,layer=leaf.source.layer});
            }
        }
        public static PaperInsideReverse Create(PaperSheet sheet,FoldStep step)
        {
            if(!step.insideReverse || sheet.Count>=sheet.FoldLimit || step.anchor<0 || step.anchor>=sheet.Faces.Count)return null;
            if(step.packetReverse){
                var packetFace=sheet.Faces[step.anchor];var packetTip=packetFace.Original(step.pullTip);var packetStop=packetFace.Original(step.pullStop);
                if(!step.packetMaterialHandles){step.packetMaterialHandles=true;step.packetTipMaterial=new Vector2(packetTip.x,packetTip.z);step.packetStopMaterial=new Vector2(packetStop.x,packetStop.z);}
                var packet=PaperPacketReverse.Create(sheet,step);return packet!=null?new PaperInsideReverse(sheet,step,packet):null;
            }
            var face=sheet.Faces[step.anchor];
            var tip=face.Original(step.pullTip);var stop=face.Original(step.pullStop);
            if(Mathf.Min(Mathf.Min(tip.x,sheet.Width-tip.x),Mathf.Min(tip.z,sheet.Height-tip.z))>.001f ||
               (tip-stop).sqrMagnitude<.012f)return null;
            var a=MakeLeaf(face,tip,stop);
            if(a==null)return null;
            foreach(var f in sheet.Faces){
                if(f==face)continue;
                var b=MakeLeaf(f,tip,stop);
                if(b==null || Vector3.Dot(a.radial,b.radial)<-.998f)continue;
                var aTip=a.source.World(a.handle,(a.source.layer+.5f)*PaperSheet.Thickness);
                var bTip=b.source.World(b.handle,(b.source.layer+.5f)*PaperSheet.Thickness);
                if((a.pivot-b.pivot).magnitude>.04f || (aTip-bTip).magnitude>.04f)continue;
                return new PaperInsideReverse(sheet,step,a,b);
            }
            return null;
        }
        private static Leaf MakeLeaf(PaperSheet.Face f,Vector3 tip,Vector3 stop)
        {
            var inv=f.material.inverse;
            var t=inv.MultiplyPoint3x4(tip);var s=inv.MultiplyPoint3x4(stop);
            var h=new Vector2(t.x,t.z);var p=new Vector2(s.x,s.z);
            if(!f.Contains(h) || !f.Contains(p))return null;
            var u=(h-p).normalized;var v=new Vector2(-u.y,u.x);
            float lo=0,hi=0;
            foreach(var point in f.points){
                float d=Vector2.Dot(point-p,v);lo=Mathf.Min(lo,d);hi=Mathf.Max(hi,d);
            }
            // Both endpoints must lie on the same material boundary of this panel.
            if(lo<-.001f && hi>.001f)return null;
            if(hi<.001f)v=-v;
            var leaf=new Leaf{source=f,stop=p,handle=h,pivot=f.World(p,(f.layer+.5f)*PaperSheet.Thickness),
                radial=f.frame.MultiplyVector(new Vector3(v.x,0,v.y)).normalized};
            var line=MakeFlank(leaf);
            leaf.tip=PaperSheet.Clip(f,line,1);leaf.body=PaperSheet.Clip(f,line,-1);
            if(leaf.tip==null || leaf.body==null)return null;
            var axis=f.frame.MultiplyVector(new Vector3(u.x,0,u.y)).normalized;
            float rad=FlankAngle*Mathf.Deg2Rad;
            leaf.flank=Mathf.Cos(rad)*axis+Mathf.Sin(rad)*leaf.radial;
            return leaf;
        }
        private static FoldStep MakeFlank(Leaf leaf)
        {
            var u=(leaf.handle-leaf.stop).normalized;
            var v=leaf.source.frame.inverse.MultiplyVector(leaf.radial);
            float rad=FlankAngle*Mathf.Deg2Rad;
            var n=Mathf.Sin(rad)*u-Mathf.Cos(rad)*new Vector2(v.x,v.z);
            return new FoldStep(CreaseFamily.Rising,0,1){creaseNormal=n,creaseOffset=Vector2.Dot(n,leaf.stop)};
        }
        private static Quaternion Basis(Vector3 u,Vector3 v)=>Quaternion.LookRotation(u,Vector3.Cross(u,v));
        private static PaperSheet.Face Rotate(PaperSheet.Face f,Vector3 pivot,Quaternion q)=>
            new PaperSheet.Face(f.points,f.layer,f.back,Matrix4x4.TRS(pivot-q*pivot,q,Vector3.one)*f.frame,f.material,f);

        public bool Pose(float progressDegrees)
        {
            if(packet!=null)return packet.Pose(progressDegrees);
            if(!IsCurrent || !(progressDegrees>=0 && progressDegrees<=180))return false;
            if(last==progressDegrees)return true;
            trial.Clear();
            if(progressDegrees==0)trial.AddRange(initial);
            else{
                float t=progressDegrees/180f;
                // Open the old V to its flat transition, then close it on the inside branch.
                const float transition=.25f;
                float opening=t<transition?Mathf.Lerp(halfOpening,90,t/transition):
                    Mathf.Lerp(90,halfOpening,(t-transition)/(1-transition));
                float delta=opening*Mathf.Deg2Rad,alpha=FlankAngle*Mathf.Deg2Rad;
                float theta=t<=transition?0:2*Mathf.Atan(Mathf.Tan(alpha)*Mathf.Cos(delta));
                var newSpine=spine*Mathf.Cos(theta)+inward*Mathf.Sin(theta);
                foreach(var f in initial){
                    int i=f==leaves[0].source?0:f==leaves[1].source?1:-1;
                    if(i<0){trial.Add(f);continue;}
                    var leaf=leaves[i];
                    bool stacked=halfOpening<.001f && PaperSheet.SameFrame(leaves[0].source,leaves[1].source);
                    int outward=leaf.source.layer>leaves[1-i].source.layer?1:-1;
                    if(stacked && progressDegrees==180f){
                        // Put both returned caps between the two exterior plies.
                        trial.Add(new PaperSheet.Face(leaf.body.points,f.layer+outward,f.back,f.frame,f.material,leaf.body));
                        trial.Add(PaperSheet.ReflectFlat(leaf.tip,MakeFlank(leaf),f.layer));
                        continue;
                    }
                    var radial=Mathf.Cos(delta)*inward+(i==0?1:-1)*Mathf.Sin(delta)*lateral;
                    var flank=Mathf.Cos(alpha)*spine+Mathf.Sin(alpha)*radial;
                    var body=Basis(spine,radial)*Quaternion.Inverse(Basis(spine,leaf.radial));
                    var cap=Basis(newSpine,flank)*Quaternion.Inverse(Basis(spine,leaf.flank));
                    var exterior=Rotate(leaf.body,leaf.pivot,body);
                    if(stacked && t>transition){
                        var offset=f.frame.MultiplyVector(Vector3.up)*(outward*PaperSheet.Thickness*(t-transition)/(1-transition));
                        exterior=new PaperSheet.Face(exterior.points,f.layer,f.back,Matrix4x4.Translate(offset)*exterior.frame,f.material,exterior);
                    }
                    trial.Add(exterior);
                    trial.Add(Rotate(leaf.tip,leaf.pivot,cap));
                }
                if(TearsOrCrosses())return false;
            }
            preview.Clear();preview.AddRange(trial);last=progressDegrees;return true;
        }
        private bool TearsOrCrosses()
        {
            for(int i=0;i<trial.Count;i++)for(int j=0;j<i;j++){
                var a=trial[i];var b=trial[j];
                if(PaperSheet.Turn.Intersects(a,b,b.frame.inverse*a.frame) ||
                   PaperSheet.Turn.Intersects(b,a,a.frame.inverse*b.frame))return true;
                // Extra panels are stationary. A pre-existing material joint to one of
                // them must remain joined along its whole overlapping edge.
                if(initial.Count>2 && !SeamsMeet(a,b))return true;
            }
            return false;
        }
        private static bool SeamsMeet(PaperSheet.Face a,PaperSheet.Face b)
        {
            for(int i=0;i<a.points.Length;i++){
                var p=a.points[i];var q=a.points[(i+1)%a.points.Length];
                var origin=a.Original(p);var d=a.Original(q)-origin;float len=d.magnitude;
                if(len<.0001f)continue;
                var axis=d/len;
                for(int j=0;j<b.points.Length;j++){
                    var u=b.points[j];var v=b.points[(j+1)%b.points.Length];
                    var bu=b.Original(u)-origin;var bv=b.Original(v)-origin;
                    if(Vector3.Cross(bu,axis).sqrMagnitude>.000001f || Vector3.Cross(bv,axis).sqrMagnitude>.000001f)continue;
                    float x=Vector3.Dot(bu,axis),y=Vector3.Dot(bv,axis);
                    float lo=Mathf.Max(0,Mathf.Min(x,y)),hi=Mathf.Min(len,Mathf.Max(x,y));
                    if(hi-lo<.0001f)continue;
                    if(!Near(a,Vector2.Lerp(p,q,lo/len),b,Vector2.Lerp(u,v,(lo-x)/(y-x))) ||
                       !Near(a,Vector2.Lerp(p,q,hi/len),b,Vector2.Lerp(u,v,(hi-x)/(y-x))))return false;
                }
            }
            return true;
        }
        private static bool Near(PaperSheet.Face a,Vector2 u,PaperSheet.Face b,Vector2 v)=>
            (a.World(u,(a.layer+.5f)*PaperSheet.Thickness)-b.World(v,(b.layer+.5f)*PaperSheet.Thickness)).sqrMagnitude<.0016f;
    }
}
