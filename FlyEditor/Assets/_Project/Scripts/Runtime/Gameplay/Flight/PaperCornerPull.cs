using System.Collections.Generic;
using UnityEngine;

namespace FlyEditor.Flight
{
    // A corner driven toward an interior stop needs several hinges meeting there.
    // Five convex sectors meet at the stop. Their rigid poses give a continuous
    // seed on flat paper; shared material vertices reconcile existing angled seams.
    public sealed class PaperCornerPull
    {
        public const float Limit=75f, FlexibleLimit=170f;
        public static float LimitFor(FoldStep step)=>step.closeFlat?180f:step.flexible?FlexibleLimit:Limit;
        public float AngleLimit=>closingFan?rays[4]:LimitFor(Step);
        private readonly float[] rays={-145,-65,0,65,145,215};
        private sealed class Node
        {
            public Vector3 material,rest,position;
            public int samples;
            public bool pinned;
            public float mobility=1f;
        }
        private sealed class Panel
        {
            public PaperSheet.Face face;
            public int sector,ib,ic;
            public readonly List<int> nodes=new List<int>();
        }
        private struct Joint { public int a,b,c,d; public float rest,target; public int kind; }
        private readonly List<Joint> joints=new List<Joint>();
        private bool coupled;
        private PaperPetalFold petal;
        private PaperClearanceSequence clearance;
        private PaperPocketSquash squash;
        private PaperRadialGather gather;
        private PaperEdgeMatchFold match;
        private float coupledAngle;
        private bool hasSolved;
        private string jointFailure;
        private Vector3[] solved;
        private struct Edge { public int a,b; public float length; }
        private struct Contact
        {
            public int point,a,b,c;
            public Vector3 weights;
            public float direction,gap;
        }
        private readonly List<Contact> contacts=new List<Contact>();
        private readonly List<Node> nodes=new List<Node>();
        private readonly List<Panel> panels=new List<Panel>();
        private readonly List<Edge> edges=new List<Edge>();
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>(),trial=new List<PaperSheet.Face>();
        private readonly Quaternion[] rotations=new Quaternion[5];
        private readonly Vector3[] flatRays=new Vector3[6],foldedRays=new Vector3[6];
        private readonly Vector2 forward;
        private readonly Vector3 pivot,along,up;
        private readonly float reach;
        private readonly PaperSheet.Face basis;
        private readonly MirrorAxis pinnedAxis;
        private readonly IReadOnlyList<PaperSheet.Face> original;
        private bool refine,blockedAbove,blockedBelow,closingFan;
        private readonly List<Vector2> region=new List<Vector2>();
        private readonly HashSet<PaperSheet.Face> selectedFaces=new HashSet<PaperSheet.Face>();
        private int topLayer,bottomLayer;
        private readonly bool flatStack,stackedInterior;
        private int tipNode=-1,stopNode=-1;
        private float previewAngle=float.NaN;
        public readonly PaperSheet Source;
        public readonly FoldStep Step;
        public bool IsCurrent=>original==Source.Faces;
        public bool UsesCoupledHinges=>coupled;
        public bool UsesEdgeMatch=>match!=null;
        public bool UsesRadialGather=>gather!=null;
        public bool UsesExactSquash=>squash!=null;
        public bool UsesPetalFold=>petal!=null || clearance!=null;
        public bool UsesClearanceSequence=>clearance!=null;
        public bool MarkGuides(List<PaperSheet.Guide> guides){if(clearance!=null){guides.Clear();return true;}if(squash!=null){squash.MarkGuides(guides);return true;}if(gather!=null){gather.MarkGuides(guides);return true;}if(match!=null){match.MarkGuides(guides);return true;}if(petal==null)return false;petal.MarkGuides(guides);return true;}
        public IReadOnlyList<PaperSheet.Face> Preview=>clearance!=null?clearance.Preview:squash!=null?squash.Preview:gather!=null?gather.Preview:match!=null?match.Preview:petal!=null?petal.Preview:preview;
        public Vector3 Tip { get; private set; }

        public Vector3 Stop=>clearance!=null?clearance.Stop:squash!=null?squash.Stop:gather!=null?gather.Stop:match!=null?match.Stop:petal!=null?petal.Stop:pivot;

        private PaperCornerPull(PaperSheet sheet,FoldStep step,MirrorAxis pinnedAxis)
        {
            Source=sheet;Step=step;this.pinnedAxis=pinnedAxis;original=sheet.Faces;basis=original[step.anchor];
            flatStack=original.Count>1;
            foreach(var face in original)flatStack &= PaperSheet.SameFrame(face,basis);
            forward=(step.pullTip-step.pullStop).normalized;
            bool left=false,right=false;
            var cross=new Vector2(-forward.y,forward.x);
            foreach(var face in original)if(PaperSheet.SameFrame(face,basis))foreach(var p in face.points){
                float distance=Vector2.Dot(p-step.pullStop,cross);left|=distance<-.001f;right|=distance>.001f;
            }
            stackedInterior=left && right;
            along=basis.frame.MultiplyVector(new Vector3(forward.x,0,forward.y)).normalized;
            up=basis.frame.MultiplyVector(Vector3.up).normalized;
            pivot=basis.World(step.pullStop,(basis.layer+.5f)*PaperSheet.Thickness);
            reach=(step.pullTip-step.pullStop).magnitude;
            if(step.flexible){
                pivot=MaterialPosition(basis.Original(step.pullStop));
                var tip=MaterialPosition(basis.Original(step.pullTip));
                reach=Vector3.Distance(pivot,tip);along=(tip-pivot).normalized;
                up=Vector3.ProjectOnPlane(up,along).normalized;
                if(up.sqrMagnitude<.5f)up=Vector3.Cross(along,basis.frame.MultiplyVector(Vector3.right)).normalized;
            }
        }
        private Vector3 MaterialPosition(Vector3 point)
        {
            foreach(var face in original){
                var local=face.material.inverse.MultiplyPoint3x4(point);var p=new Vector2(local.x,local.z);
                if(face.Contains(p))return face.World(p,(face.layer+.5f)*PaperSheet.Thickness);
            }
            return basis.World(Step.pullStop,(basis.layer+.5f)*PaperSheet.Thickness);
        }
        private bool SetFlanks()
        {
            if(Step.regionalPull && !BuildRegion())return false;
            if(!Step.customFlanks){
                ConfigureClosure();return true;
            }
            var left=Step.pullLeft-Step.pullStop;var right=Step.pullRight-Step.pullStop;
            if(left.sqrMagnitude<.01f || right.sqrMagnitude<.01f)return false;
            float a=Vector2.SignedAngle(forward,left),b=Vector2.SignedAngle(forward,right);
            if(a*b>=0 || Mathf.Min(Mathf.Abs(a),Mathf.Abs(b))<15 || Mathf.Max(Mathf.Abs(a),Mathf.Abs(b))>150)return false;
            rays[1]=Mathf.Min(a,b);rays[3]=Mathf.Max(a,b);
            rays[0]=Mathf.Max(-179,rays[1]-90);rays[4]=Mathf.Min(179,rays[3]+90);rays[5]=360+rays[0];
            ConfigureClosure();return true;
        }
        // The convex material footprint of the four handles identifies the
        // connected part of the paper even when its leaves coincide in space.
        // Unselected plies stay pinned. Nothing is inferred from screen depth.
        private bool BuildRegion()
        {
            if(!Step.customFlanks)return false;
            var points=new List<Vector2>{Step.pullTip,Step.pullStop,Step.pullLeft,Step.pullRight};
            points.Sort((a,b)=>a.x==b.x?a.y.CompareTo(b.y):a.x.CompareTo(b.x));
            foreach(var point in points){
                while(region.Count>=2 && Cross(region[region.Count-1]-region[region.Count-2],point-region[region.Count-1])<=.00001f)region.RemoveAt(region.Count-1);
                region.Add(point);
            }
            int lower=region.Count;
            for(int i=points.Count-2;i>=0;i--){
                var point=points[i];
                while(region.Count>lower && Cross(region[region.Count-1]-region[region.Count-2],point-region[region.Count-1])<=.00001f)region.RemoveAt(region.Count-1);
                region.Add(point);
            }
            if(region.Count>1)region.RemoveAt(region.Count-1);
            if(region.Count<3)return false;
            foreach(var face in original){
                var chart=basis.material.inverse*face.material;var part=face;
                for(int i=0;i<region.Count && part!=null;i++){
                    var a=region[i];var edge=region[(i+1)%region.Count]-a;var n=new Vector2(-edge.y,edge.x);
                    var line=new FoldStep(CreaseFamily.Vertical,0,1){
                        creaseNormal=new Vector2(n.x*chart.m00+n.y*chart.m20,n.x*chart.m02+n.y*chart.m22),
                        creaseOffset=Vector2.Dot(n,a)-n.x*chart.m03-n.y*chart.m23};
                    part=PaperSheet.Clip(part,line,1);
                }
                if(part!=null && PaperSheet.Area(part.points)>.0001f)selectedFaces.Add(face);
            }
            return selectedFaces.Contains(basis);
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private void ConfigureClosure()
        {
            closingFan=Step.closeFlat && (original.Count==1 || flatStack) && pinnedAxis==MirrorAxis.Off &&
                Mathf.Abs(rays[1]+rays[3])<.01f && rays[3]<=90.01f;
            if(Step.regionalPull){
                // Folded leaves can share a spatial frame but have different
                // material charts. They require the connected constraint solver.
                foreach(var face in selectedFaces)if(face.material!=basis.material)closingFan=false;
            }
            if(!closingFan)return;
            rays[0]=rays[1]-90;rays[4]=rays[3]+90;rays[5]=360+rays[0];
            topLayer=bottomLayer=basis.layer;
            foreach(var face in original){topLayer=Mathf.Max(topLayer,face.layer);bottomLayer=Mathf.Min(bottomLayer,face.layer);}
        }
        public static PaperCornerPull Create(PaperSheet sheet,FoldStep step,MirrorAxis pinnedAxis=MirrorAxis.Off)
        {
            if(!step.cornerPull || sheet.Count>=sheet.FoldLimit || step.anchor<0 || step.anchor>=sheet.Faces.Count ||
                (step.flap!=0 && step.flap!=step.anchor+1) || !Finite(step.pullTip) || !Finite(step.pullStop))return null;
            var face=sheet.Faces[step.anchor];
            if(!face.Contains(step.pullTip) || (!step.flexible && !sheet.ContainsPanelPoint(face,step.pullStop)) ||
               (step.pullTip-step.pullStop).sqrMagnitude<.01f)return null;
            var p=face.Original(step.pullTip);
            if(!step.flexible && Mathf.Min(p.x,p.z,sheet.Width-p.x,sheet.Height-p.z)>.001f)return null;
            var plan=new PaperCornerPull(sheet,step,pinnedAxis);
            if(step.clearance!=null && step.clearance.Length>0){plan.clearance=PaperClearanceSequence.Create(sheet,step);return plan.clearance!=null?plan:null;}
            if(pinnedAxis==MirrorAxis.Off && (plan.gather=PaperRadialGather.Create(sheet,step))!=null)return plan;
            if(pinnedAxis==MirrorAxis.Off && (plan.squash=PaperPocketSquash.Create(sheet,step))!=null)return plan;
            if(pinnedAxis==MirrorAxis.Off && (plan.match=PaperEdgeMatchFold.Create(sheet,step))!=null)return plan;
            if(pinnedAxis==MirrorAxis.Off && (plan.petal=PaperPetalFold.Create(sheet,step))!=null)return plan;
            // A surface click can lie just beside a real material junction. Resolve
            // a nearby junction before falling back to the generic fan solver;
            // that solver cannot reproduce the return hinges of a closed petal.
            if(pinnedAxis==MirrorAxis.Off && step.adaptiveCreases && step.customFlanks && step.regionalPull){
                var snapped=step;float tolerance=Mathf.Min(sheet.Width,sheet.Height)*.005f;
                snapped.pullTip=SnapMaterialCorner(sheet,face,step.pullTip,tolerance);
                snapped.pullStop=SnapMaterialCorner(sheet,face,step.pullStop,tolerance);
                snapped.pullLeft=SnapMaterialCorner(sheet,face,step.pullLeft,tolerance);
                snapped.pullRight=SnapMaterialCorner(sheet,face,step.pullRight,tolerance);
                bool changed=(snapped.pullTip-step.pullTip).sqrMagnitude+(snapped.pullStop-step.pullStop).sqrMagnitude+
                    (snapped.pullLeft-step.pullLeft).sqrMagnitude+(snapped.pullRight-step.pullRight).sqrMagnitude>.000000001f;
                if(changed && face.Contains(snapped.pullTip)){
                    var petal=PaperPetalFold.Create(sheet,snapped);
                    if(petal!=null)return new PaperCornerPull(sheet,snapped,pinnedAxis){petal=petal};
                }
            }
            return plan.reach>.01f && plan.SetFlanks() && plan.Build() ? plan : null;
        }
        // Material coordinates distinguish genuinely connected corners from
        // unrelated plies occupying the same screen position. Only existing
        // polygon vertices within half a percent of the sheet size are eligible.
        private static Vector2 SnapMaterialCorner(PaperSheet sheet,PaperSheet.Face basis,Vector2 point,float tolerance)
        {
            var material=basis.Original(point);float best=tolerance*tolerance;var result=point;
            var inverse=basis.material.inverse;
            foreach(var face in sheet.Faces)foreach(var corner in face.points){
                var candidate=face.Original(corner);float distance=(candidate-material).sqrMagnitude;
                if(distance>=best)continue;
                best=distance;var local=inverse.MultiplyPoint3x4(candidate);result=new Vector2(local.x,local.z);
            }
            return result;
        }
        private static bool Finite(Vector2 p)=>!float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);
        private Vector2 Ray(float degrees)
        {
            float angle=degrees*Mathf.Deg2Rad;
            return forward*Mathf.Cos(angle)+new Vector2(-forward.y,forward.x)*Mathf.Sin(angle);
        }
        private PaperSheet.Face Cut(PaperSheet.Face face,Matrix4x4 chart,Vector2 ray,int side)
        {
            var n=new Vector2(-ray.y,ray.x);
            var line=new FoldStep(CreaseFamily.Vertical,0,side){
                creaseNormal=new Vector2(n.x*chart.m00+n.y*chart.m20,n.x*chart.m02+n.y*chart.m22),
                creaseOffset=Vector2.Dot(n,Step.pullStop)-n.x*chart.m03-n.y*chart.m23};
            return PaperSheet.Clip(face,line,side);
        }
        private bool Build()
        {
            for(int i=0;i<rays.Length;i++){
                var ray=Ray(rays[i]);
                flatRays[i]=Step.flexible ? along*Vector2.Dot(ray,forward)+Vector3.Cross(along,up)*Vector2.Dot(ray,new Vector2(-forward.y,forward.x)) :
                    basis.frame.MultiplyVector(new Vector3(ray.x,0,ray.y)).normalized;
            }
            refine=Step.flexible || original.Count>1 || pinnedAxis!=MirrorAxis.Off;
            bool left=false,right=false;
            foreach(var face in original){
                if(panels.Count>(Step.flexible?512:128))return false;
                // The other ply of a flat stack stays where it is. Never silently pull it through the selected one.
                bool overlaps=!Step.regionalPull && Step.flap!=0 && PaperSheet.SameFrame(face,basis) && face.layer!=basis.layer && PaperSheet.Turn.Overlap(face,basis);
                if((Step.regionalPull && !selectedFaces.Contains(face)) || overlaps || (Step.singlePanel && !PaperSheet.SamePanel(face,basis))){
                    if(PaperSheet.SameFrame(face,basis) && face.Contains((Step.pullStop+Step.pullTip)*.5f)){
                        blockedAbove|=face.layer>basis.layer;blockedBelow|=face.layer<basis.layer;
                    }
                    AddPanel(face,-1);continue;
                }
                var chart=Step.regionalPull?basis.material.inverse*face.material:
                    closingFan || (Step.flexible && flatStack && stackedInterior)?Matrix4x4.identity:basis.material.inverse*face.material;
                float area=0;
                for(int i=0;i<5;i++){
                    if(rays[i+1]-rays[i]<.001f)continue;
                    var part=Cut(face,chart,Ray(rays[i]),1);
                    if(part==null)continue;
                    part=Cut(part,chart,Ray(rays[i+1]),-1);
                    if(part==null)continue;
                    if(refine && part.points.Length>3){
                        Vector2 centre=Vector2.zero;
                        foreach(var v in part.points)centre+=v;
                        centre/=part.points.Length;
                        for(int j=0;j<part.points.Length;j++){
                            var poly=new[]{centre,part.points[j],part.points[(j+1)%part.points.Length]};
                            if(PaperSheet.Area(poly)>.0001f)
                                AddPanel(new PaperSheet.Face(poly,part.layer,part.back,part.frame,part.material),i);
                        }
                    }else AddPanel(part,i);
                    area+=PaperSheet.Area(part.points);
                    if(face==basis || Step.flexible){left|=i==1;right|=i==2;}
                }
                if(Mathf.Abs(area-PaperSheet.Area(face.points))>.003f)return false;
            }
            if(!left || !right || panels.Count>(Step.flexible?512:128))return false;
            tipNode=Find(basis.Original(Step.pullTip));stopNode=Find(basis.Original(Step.pullStop));
            if(tipNode<0 || stopNode<0 || nodes[tipNode].pinned)return false;
            nodes[tipNode].pinned=nodes[stopNode].pinned=true;
            var seen=new HashSet<long>();
            foreach(var panel in panels){
                // A neighbouring panel may end halfway along this polygon's edge.
                // Include that material point in both panels' distance constraints.
                var inv=panel.face.material.inverse;
                for(int i=0;i<nodes.Count;i++){
                    var v=inv.MultiplyPoint3x4(nodes[i].material);
                    if(!panel.face.Contains(new Vector2(v.x,v.z)))continue;
                    if(!panel.nodes.Contains(i))panel.nodes.Add(i);
                    if(Step.singlePanel && panel.sector<0)nodes[i].pinned=true;
                }
                for(int i=0;i<panel.nodes.Count;i++)
                for(int j=0;j<i;j++){
                    int a=panel.nodes[i],b=panel.nodes[j];
                    long key=((long)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
                    if(seen.Add(key))edges.Add(new Edge{a=a,b=b,length=Vector3.Distance(nodes[a].material,nodes[b].material)});
                }
            }
            // A target on a frozen seam cannot be lifted independently.
            if(Step.singlePanel){
                foreach(var panel in panels)if(panel.sector<0 && panel.nodes.Contains(tipNode))return false;
            }
            if(Step.flexible && flatStack)BuildContacts();
            if(Step.coupledHinges && Step.customFlanks)BuildJoints();
            return true;
        }
        private float JointAngle(Joint j)
        {
            var a=nodes[j.a].position;var axis=(nodes[j.b].position-a).normalized;
            var c=Vector3.ProjectOnPlane(nodes[j.c].position-a,axis);
            var d=Vector3.ProjectOnPlane(nodes[j.d].position-a,axis);
            return Mathf.Atan2(Vector3.Dot(axis,Vector3.Cross(c,d)),Vector3.Dot(c,d));
        }
        private static float Wrapped(float value)=>Mathf.DeltaAngle(0,value*Mathf.Rad2Deg)*Mathf.Deg2Rad;
        private void BuildJoints()
        {
            foreach(var node in nodes)node.position=node.rest;
            var origin=basis.Original(Step.pullStop);var dir=(basis.Original(Step.pullTip)-origin).normalized;
            var left=(basis.Original(Step.pullLeft)-origin).normalized;var right=(basis.Original(Step.pullRight)-origin).normalized;
            int central=0,sideMask=0;
            for(int i=0;i<panels.Count;i++)for(int k=0;k<i;k++){
                var p=panels[i];var q=panels[k];if(p.sector<0 && q.sector<0)continue;
                int a=-1,b=-1;float longest=.00001f;
                foreach(int x in p.nodes)if(q.nodes.Contains(x))foreach(int y in p.nodes)if(y!=x && q.nodes.Contains(y)){
                    float length=(nodes[x].material-nodes[y].material).sqrMagnitude;
                    if(length>longest){a=x;b=y;longest=length;}
                }
                if(a<0)continue;
                if((nodes[a].material-origin).sqrMagnitude>(nodes[b].material-origin).sqrMagnitude){int t=a;a=b;b=t;}
                var edge=(nodes[b].material-nodes[a].material).normalized;
                int c=Opposite(p,a,b),d=Opposite(q,a,b);if(c<0 || d<0 || c==d)continue;
                if(Vector3.Cross(edge,nodes[c].material-nodes[a].material).y<0){int t=c;c=d;d=t;}
                var j=new Joint{a=a,b=b,c=c,d=d};j.rest=JointAngle(j);j.target=j.rest;
                bool radial=Vector3.Cross(nodes[a].material-origin,edge).sqrMagnitude<.000001f;
                if(radial && Vector3.Dot(edge,dir)>.9999f && Mathf.Abs(j.rest)<Mathf.PI*.65f){
                    j.kind=1;j.target=(j.rest<0?-1:1)*Mathf.PI;central++;
                }else if(radial && (Vector3.Dot(edge,left)>.9999f || Vector3.Dot(edge,right)>.9999f)){
                    j.kind=2;j.target=0;
                    if(p.sector>=0 && q.sector>=0)sideMask|=Vector3.Dot(edge,left)>.9999f?1:2;
                }
                if(j.kind==0 && (p.sector!=q.sector || p.face.material!=q.face.material))j.kind=-1;
                joints.Add(j);
            }
            coupled=central>0 && sideMask==3;
            if(coupled){
                foreach(var panel in panels)if(panel.sector==4){panel.sector=-1;foreach(int id in panel.nodes)nodes[id].pinned=true;}
                nodes[tipNode].pinned=false;
                // A material seam has a small offset on each physical layer.
                // Keep the selected side of that seam on its original surface.
                if(flatStack)foreach(var node in nodes){
                    var local=basis.frame.inverse.MultiplyPoint3x4(node.rest);local.y=0;node.rest=basis.frame.MultiplyPoint3x4(local);node.position=node.rest;
                }
                var initialized=new HashSet<int>();
                foreach(var panel in panels)if(panel.sector>=0){var f=panel.face;var inverse=f.material.inverse;
                    foreach(int id in panel.nodes)if(initialized.Add(id)){
                        var local=inverse.MultiplyPoint3x4(nodes[id].material);
                        nodes[id].rest=f.World(new Vector2(local.x,local.z),flatStack?0:(f.layer+.5f)*PaperSheet.Thickness);
                        nodes[id].position=nodes[id].rest;
                    }
                }
                for(int i=0;i<joints.Count;i++){var j=joints[i];j.rest=JointAngle(j);if(j.kind==0 || j.kind<0)j.target=j.rest;joints[i]=j;}
            }
        }
        private int Opposite(Panel panel,int a,int b)
        {
            int best=-1;float area=.000001f;var edge=nodes[b].material-nodes[a].material;
            foreach(int id in panel.nodes){float value=Vector3.Cross(edge,nodes[id].material-nodes[a].material).sqrMagnitude;
                if(value>area){area=value;best=id;}}
            return best;
        }
        private float JointWeight(int id)=>id==tipNode?1:nodes[id].pinned?0:1;
        private float SolveJoint(Joint j,float t,float sign)
        {
            if(j.kind<0 || j.kind==2)return 0;
            var a=nodes[j.a].position;var edge=nodes[j.b].position-a;float length=edge.sqrMagnitude;
            if(length<.000001f)return 0;
            var axis=edge/Mathf.Sqrt(length);var c=nodes[j.c].position-a;var d=nodes[j.d].position-a;
            float tc=Vector3.Dot(c,edge)/length,td=Vector3.Dot(d,edge)/length;
            var pc=c-tc*edge;var pd=d-td*edge;
            if(pc.sqrMagnitude<.000001f || pd.sqrMagnitude<.000001f)return 0;
            float target=j.kind==0?j.rest:j.kind==1?j.rest+sign*t*Wrapped(j.target-j.rest):j.rest*(1-t);
            float error=Wrapped(JointAngle(j)-target);
            var gc=-Vector3.Cross(axis,pc)/pc.sqrMagnitude;var gd=Vector3.Cross(axis,pd)/pd.sqrMagnitude;
            var ga=-(1-tc)*gc-(1-td)*gd;var gb=-tc*gc-td*gd;
            float wa=JointWeight(j.a),wb=JointWeight(j.b),wc=JointWeight(j.c),wd=JointWeight(j.d);
            float denominator=wa*ga.sqrMagnitude+wb*gb.sqrMagnitude+wc*gc.sqrMagnitude+wd*gd.sqrMagnitude;
            if(denominator<.000001f)return Mathf.Abs(error);
            float lambda=-error*.35f/denominator;
            nodes[j.a].position+=wa*lambda*ga;nodes[j.b].position+=wb*lambda*gb;
            nodes[j.c].position+=wc*lambda*gc;nodes[j.d].position+=wd*lambda*gd;
            return Mathf.Abs(error);
        }
        private bool PoseCoupled(float angle)
        {
            float requested=angle;
            if(flatStack)angle=Mathf.Sign(angle)*Mathf.Min(180,Mathf.Abs(angle)*2);
            if(!hasSolved || angle*coupledAngle<0 || Mathf.Abs(angle)<Mathf.Abs(coupledAngle)){
                coupledAngle=0;hasSolved=false;foreach(var node in nodes)node.position=node.rest;
            }else for(int i=0;i<nodes.Count;i++)nodes[i].position=solved[i];
            float progress=coupledAngle;bool firstPose=progress==0;
            while(progress!=angle){
                progress=Mathf.Sign(angle)*Mathf.Min(Mathf.Abs(angle),(Mathf.Floor(Mathf.Abs(progress)/2)+1)*2);float t=Mathf.Abs(progress)/180f;
                if(firstPose && flatStack){SeedPocket(Mathf.Min(15,Mathf.Abs(progress)*4));firstPose=false;}
                for(int iteration=0;iteration<(t>.8f?12000:2400);iteration++){
                    float error=0;
                    foreach(var j in joints)error=Mathf.Max(error,SolveJoint(j,t,Mathf.Sign(angle)));
                    foreach(var e in edges){
                        var a=nodes[e.a];var b=nodes[e.b];var delta=b.position-a.position;float length=delta.magnitude;
                        float wa=JointWeight(e.a),wb=JointWeight(e.b);if(length<.000001f || wa+wb==0)continue;
                        float difference=length-e.length;var fix=delta*(difference/(length*(wa+wb)));
                        a.position+=wa*fix;b.position-=wb*fix;error=Mathf.Max(error,Mathf.Abs(difference));
                    }
                    // Contact remains active; opening must not pass through a fixed ply.
                    error=Mathf.Max(error,flatStack?SeparateFixedSurfaces():SeparateLayers(-1));
                    if(error<.0004f)break;
                }
                if(!RecoverPanels(flatStack?progress*.5f:progress))return false;
                if(Mathf.Abs(progress/2-Mathf.Round(progress/2))<.00001f){
                    if(solved==null)solved=new Vector3[nodes.Count];
                    for(int i=0;i<nodes.Count;i++)solved[i]=nodes[i].position;
                    coupledAngle=progress;hasSolved=true;
                }
            }
            if(flatStack && Mathf.Abs(requested)>90 && !PressPocket((Mathf.Abs(requested)-90)/90))return false;
            if(flatStack && Mathf.Abs(requested)>=179.999f){
                var inverse=basis.frame.inverse;
                foreach(var node in nodes)if(Mathf.Abs(inverse.MultiplyPoint3x4(node.position).y)>.003f){jointFailure="not planar";return false;}
            }
            if(!RecoverPanels(requested))return false;
            preview.Clear();preview.AddRange(trial);previewAngle=requested;Tip=nodes[tipNode].position;return true;
        }
        private void SeedPocket(float degrees)
        {
            int a=-1,b=-1;float distance=0;
            for(int i=0;i<nodes.Count;i++)if(IsPocketAnchor(i))for(int j=0;j<i;j++)if(IsPocketAnchor(j)){
                float d=(nodes[i].rest-nodes[j].rest).sqrMagnitude;if(d>distance){a=i;b=j;distance=d;}}
            if(a<0)return;var origin=nodes[a].rest;var axis=(nodes[b].rest-origin).normalized;
            float away=1;foreach(var panel in panels)if(panel.sector<0 && panel.face.layer>basis.layer){away=-1;break;}
            var normal=basis.frame.MultiplyVector(Vector3.up)*away;
            float sign=Mathf.Sign(Vector3.Dot(Vector3.Cross(axis,nodes[tipNode].rest-origin),normal));
            var rotation=Quaternion.AngleAxis(degrees*sign,axis);
            foreach(var node in nodes)if(!node.pinned)node.position=origin+rotation*(node.position-origin);
        }
        private bool IsPocketAnchor(int id)
        {
            if(!nodes[id].pinned)return false;
            foreach(var panel in panels)if(panel.sector>=0 && panel.nodes.Contains(id))return true;
            return false;
        }
        private bool PressPocket(float amount)
        {
            int a=-1,b=-1;float distance=0;
            for(int i=0;i<nodes.Count;i++)if(IsPocketAnchor(i))for(int j=0;j<i;j++)if(IsPocketAnchor(j)){
                float d=(nodes[i].rest-nodes[j].rest).sqrMagnitude;if(d>distance){a=i;b=j;distance=d;}
            }
            if(a<0 || distance<.00001f)return false;
            var origin=nodes[a].rest;var axis=(nodes[b].rest-origin).normalized;
            for(int i=0;i<nodes.Count;i++)if(IsPocketAnchor(i) && Vector3.Cross(nodes[i].rest-origin,axis).sqrMagnitude>.00001f)return false;
            var ray=Vector3.zero;
            foreach(var node in nodes)if(!node.pinned){var r=Vector3.ProjectOnPlane(node.position-origin,axis);if(r.sqrMagnitude>ray.sqrMagnitude)ray=r;}
            var normal=basis.frame.MultiplyVector(Vector3.up);
            var target=Vector3.ProjectOnPlane(ray,normal);
            if(target.sqrMagnitude<.000001f)target=Vector3.Cross(axis,normal);
            var rotation=Quaternion.AngleAxis(Vector3.SignedAngle(ray,target,axis)*amount,axis);
            foreach(var node in nodes)if(!node.pinned)node.position=origin+rotation*(node.position-origin);
            if(amount>=.99999f){
                // At flat contact the angular constraint is singular. Solve its
                // coplanar distance constraints, then retain all rigidity checks.
                foreach(var node in nodes)if(!node.pinned && Mathf.Abs(Vector3.Dot(node.position-origin,normal))>reach*.025f)return false;
                for(int iter=0;iter<600;iter++){
                    foreach(var node in nodes)if(!node.pinned)node.position-=normal*Vector3.Dot(node.position-origin,normal);
                    float error=0;
                    foreach(var e in edges){var aNode=nodes[e.a];var bNode=nodes[e.b];
                        var delta=Vector3.ProjectOnPlane(bNode.position-aNode.position,normal);float length=delta.magnitude;
                        float wa=JointWeight(e.a),wb=JointWeight(e.b);if(length<.000001f || wa+wb==0)continue;
                        float diff=length-e.length;var fix=delta*(diff/(length*(wa+wb)));aNode.position+=wa*fix;bNode.position-=wb*fix;error=Mathf.Max(error,Mathf.Abs(diff));
                    }
                    if(error<.000001f)break;
                }
            }
            return true;
        }
        private float SeparateFixedSurfaces()
        {
            float error=0;
            for(int id=0;id<nodes.Count;id++){
                var node=nodes[id];if(node.pinned)continue;
                int layer=basis.layer;foreach(var part in panels)if(part.sector>=0 && part.nodes.Contains(id))layer=Mathf.Min(layer,part.face.layer);
                foreach(var part in panels)if(part.sector<0 && !part.nodes.Contains(id)){
                    var f=part.face;var local=f.frame.inverse.MultiplyPoint3x4(node.position);
                    if(!f.Contains(new Vector2(local.x,local.z)))continue;
                    float side=layer>f.layer?1:-1;
                    float clearance=(f.layer-layer+side)*PaperSheet.Thickness;
                    float penetration=(clearance-local.y)*side;
                    if(penetration<=0)continue;
                    node.position+=f.frame.MultiplyVector(Vector3.up)*(penetration*side);error=Mathf.Max(error,penetration);
                }
            }
            return error;
        }
        private bool RecoverPanels(float angle)
        {
            jointFailure=null;trial.Clear();
            foreach(var panel in panels){
                var f=panel.face;if(panel.sector<0){trial.Add(f);continue;}
                int b=panel.ib,c=panel.ic;var target=nodes[panel.nodes[0]].position;
                Vector2 u=f.points[b]-f.points[0],v=f.points[c]-f.points[0];
                var rotation=Basis(nodes[panel.nodes[b]].position-target,nodes[panel.nodes[c]].position-target)*Quaternion.Inverse(Basis(new Vector3(u.x,0,u.y),new Vector3(v.x,0,v.y)));
                int layer=f.layer;
                int highest=int.MinValue,lowest=int.MaxValue,fixedLowest=int.MaxValue;
                foreach(var sourcePanel in panels){if(sourcePanel.sector<0)fixedLowest=Mathf.Min(fixedLowest,sourcePanel.face.layer);
                    else {highest=Mathf.Max(highest,sourcePanel.face.layer);lowest=Mathf.Min(lowest,sourcePanel.face.layer);}}
                bool below=fixedLowest!=int.MaxValue && highest<fixedLowest;
                bool closed=flatStack && Mathf.Abs(angle)>=179.999f;
                if(closed)layer=panel.sector==1 || panel.sector==2?(below?lowest:highest):(below?lowest+1:highest-1);
                float h=(layer+.5f)*PaperSheet.Thickness;
                var separation=flatStack?basis.frame.MultiplyVector(Vector3.up)*h:Vector3.zero;
                var offset=target+separation-rotation*new Vector3(f.points[0].x,h,f.points[0].y);
                var moved=new PaperSheet.Face(f.points,layer,f.back,Matrix4x4.TRS(offset,rotation,Vector3.one),f.material);
                var inv=f.material.inverse;
                foreach(int id in panel.nodes){var p=inv.MultiplyPoint3x4(nodes[id].material);
                    float residual=(moved.World(new Vector2(p.x,p.z),h)-separation-nodes[id].position).magnitude;
                    if(residual>.012f){jointFailure="rigid panel residual="+residual;return false;}}
                if(closed){
                    var flat=Rebase(moved,moved.frame,basis.frame,layer);
                    if(Mathf.Abs(PaperSheet.Area(flat.points)-PaperSheet.Area(f.points))>.0005f){jointFailure="flattening changes material area";return false;}
                    for(int j=0;j<f.points.Length;j++){
                        int k=(j+1)%f.points.Length;
                        if(Mathf.Abs(Vector2.Distance(flat.points[j],flat.points[k])-Vector2.Distance(f.points[j],f.points[k]))>.0005f){jointFailure="flattening changes edge length";return false;}
                    }
                    trial.Add(flat);
                }else trial.Add(moved);
            }
            for(int i=0;i<trial.Count;i++)for(int j=0;j<i;j++){
                var a=trial[i];var b=trial[j];
                if(PaperSheet.Turn.Intersects(a,b,b.frame.inverse*a.frame) || PaperSheet.Turn.Intersects(b,a,a.frame.inverse*b.frame)){jointFailure="collision panels="+i+","+j;return false;}
            }
            return true;
        }

        private void BuildContacts()
        {
            var paired=new HashSet<long>();
            for(int i=0;i<nodes.Count;i++)foreach(var panel in panels){
                var face=panel.face;var key=((long)i<<32)|(uint)face.layer;
                if(paired.Contains(key) || panel.nodes.Contains(i))continue;
                var local=face.frame.inverse.MultiplyPoint3x4(nodes[i].rest);
                var p=new Vector2(local.x,local.z);
                if(!face.Contains(p) || Mathf.Abs(local.y-(face.layer+.5f)*PaperSheet.Thickness)<PaperSheet.Thickness*.5f)continue;
                var a=face.points[0];var u=face.points[panel.ib]-a;var v=face.points[panel.ic]-a;var delta=p-a;
                float determinant=u.x*v.y-u.y*v.x;
                if(Mathf.Abs(determinant)<.00001f)continue;
                float b=(delta.x*v.y-delta.y*v.x)/determinant,c=(u.x*delta.y-u.y*delta.x)/determinant;
                int ia=panel.nodes[0],ib=panel.nodes[panel.ib],ic=panel.nodes[panel.ic];
                var normal=Vector3.Cross(nodes[ib].rest-nodes[ia].rest,nodes[ic].rest-nodes[ia].rest).normalized;
                float separation=Vector3.Dot(nodes[i].rest-nodes[ia].rest,normal);
                contacts.Add(new Contact{point=i,a=ia,b=ib,c=ic,weights=new Vector3(1-b-c,b,c),
                    direction=Mathf.Sign(separation),gap=Mathf.Abs(separation)});
                paired.Add(key);
            }
        }
        private float FreeWeight(int id,int rear)=>nodes[id].pinned || id==rear?0:nodes[id].mobility;
        private float SeparateLayers(int rear)
        {
            float error=0;
            foreach(var contact in contacts){
                var p=nodes[contact.point];var a=nodes[contact.a];var b=nodes[contact.b];var c=nodes[contact.c];
                var normal=Vector3.Cross(b.position-a.position,c.position-a.position).normalized*contact.direction;
                var w=contact.weights;var surface=w.x*a.position+w.y*b.position+w.z*c.position;
                float penetration=contact.gap-Vector3.Dot(p.position-surface,normal);
                if(penetration<=0)continue;
                error=Mathf.Max(error,penetration);
                float wp=FreeWeight(contact.point,rear),wa=FreeWeight(contact.a,rear),wb=FreeWeight(contact.b,rear),wc=FreeWeight(contact.c,rear);
                float total=wp+wa*w.x*w.x+wb*w.y*w.y+wc*w.z*w.z;
                if(total<.00001f)continue;
                var correction=normal*(penetration/total);
                p.position+=wp*correction;a.position-=wa*w.x*correction;b.position-=wb*w.y*correction;c.position-=wc*w.z*correction;
            }
            return error;
        }
        private int Find(Vector3 material)
        {
            for(int i=0;i<nodes.Count;i++)if((nodes[i].material-material).sqrMagnitude<.000001f)return i;
            return -1;
        }
        private void AddPanel(PaperSheet.Face face,int sector)
        {
            var panel=new Panel{face=face,sector=sector};
            foreach(var p in face.points){
                var material=face.Original(p);int id=Find(material);
                if(id<0){
                    id=nodes.Count;
                    bool sideX=Mathf.Min(Mathf.Abs(material.x),Mathf.Abs(material.x-Source.Width))<.001f;
                    bool sideY=Mathf.Min(Mathf.Abs(material.z),Mathf.Abs(material.z-Source.Height))<.001f;
                    nodes.Add(new Node{material=material,rest=face.World(p,(face.layer+.5f)*PaperSheet.Thickness),
                        mobility=!Step.flexible && sideX && sideY?.025f:1f});
                }
                panel.nodes.Add(id);
                bool seam=pinnedAxis==MirrorAxis.Horizontal?Mathf.Abs(material.z-Source.Height*.5f)<.0001f:
                    pinnedAxis==MirrorAxis.Vertical && Mathf.Abs(material.x-Source.Width*.5f)<.0001f;
                if(sector<0 || seam)nodes[id].pinned=true;
            }
            // Choose a well-conditioned triangle to recover this rigid panel's frame.
            float largest=0;
            for(int i=1;i<face.points.Length;i++)
            for(int j=1;j<i;j++){
                var u=face.points[i]-face.points[0];var v=face.points[j]-face.points[0];
                float area=Mathf.Abs(u.x*v.y-u.y*v.x);
                if(area>largest){largest=area;panel.ib=i;panel.ic=j;}
            }
            panels.Add(panel);
        }

        private void SetRotations(float angle)
        {
            float rad=angle*Mathf.Deg2Rad;
            var tip=along*Mathf.Cos(rad)+up*Mathf.Sin(rad);
            foldedRays[2]=tip;foldedRays[0]=flatRays[0];foldedRays[4]=flatRays[4];foldedRays[5]=flatRays[5];
            for(int side=0;side<=4;side+=4){
                var fixedRay=flatRays[side];
                if(closingFan && Mathf.Abs(rays[3]-90)<.01f){foldedRays[side==0?1:3]=flatRays[side==0?1:3];continue;}
                // Each ray must keep its original angles to the two adjacent rays.
                int flank=side==0?1:3;
                float dot=Vector3.Dot(tip,fixedRay),u=Vector3.Dot(flatRays[flank],flatRays[2]),v=Vector3.Dot(flatRays[flank],fixedRay);
                var mid=((u-dot*v)*tip+(v-dot*u)*fixedRay)/(1-dot*dot);
                var lift=Vector3.Cross(tip,fixedRay).normalized*Mathf.Sqrt(Mathf.Max(0,1f-mid.sqrMagnitude));
                if((closingFan?Vector3.Dot(Vector3.Cross(along,fixedRay),up):Vector3.Dot(lift,up))*angle>0)lift=-lift;
                foldedRays[side==0?1:3]=mid+lift;
            }
            for(int i=0;i<4;i++)
                rotations[i]=Basis(foldedRays[i],foldedRays[i+1])*Quaternion.Inverse(Basis(flatRays[i],flatRays[i+1]));
            rotations[4]=Quaternion.identity;
        }
        private int ClosingLayer(Panel panel,float angle)
        {
            int layer=panel.face.layer;
            if(panel.sector<0 || panel.sector==4)return layer;
            // A perpendicular pair is one ordinary half-fold. In a fan the
            // two side pleats occupy the layer beneath the central flap.
            bool straight=Mathf.Abs(rays[3]-90)<.01f;
            if(straight && (panel.sector==0 || panel.sector==3))return layer;
            if(straight || panel.sector==0 || panel.sector==3)
                return angle>0?2*topLayer+1-layer:2*bottomLayer-1-layer;
            int span=topLayer-bottomLayer+1;
            return layer+(angle>0?2*span:-2*span);
        }
        private PaperSheet.Face Rebase(PaperSheet.Face face,Matrix4x4 frame,Matrix4x4 target,int layer)
        {
            var chart=target.inverse*frame;
            float h=(face.layer+.5f)*PaperSheet.Thickness;
            var points=new Vector2[face.points.Length];
            for(int i=0;i<points.Length;i++){
                var p=chart.MultiplyPoint3x4(new Vector3(face.points[i].x,h,face.points[i].y));points[i]=new Vector2(p.x,p.z);
            }
            chart.m01=chart.m10=chart.m12=chart.m21=chart.m13=0;chart.m11=1;
            bool back=face.back^(chart.m00*chart.m22-chart.m02*chart.m20<0);
            return new PaperSheet.Face(points,layer,back,target,face.material*chart.inverse);
        }
        private bool PoseClosingFan(float angle)
        {
            trial.Clear();
            bool straight=Mathf.Abs(rays[3]-90)<.01f,closed=Mathf.Abs(angle)>=AngleLimit-.0001f;
            float press=Mathf.SmoothStep(0,1,Mathf.InverseLerp(AngleLimit-10,AngleLimit,Mathf.Abs(angle)));
            var contactNormal=rotations[1]*up;
            var contactFrame=Matrix4x4.TRS(pivot-contactNormal*PaperSheet.Thickness,
                rotations[1]*basis.frame.rotation,Vector3.one);
            foreach(var panel in panels){
                var f=panel.face;
                if(panel.sector<0){trial.Add(f);continue;}
                var q=rotations[panel.sector];
                var frame=Matrix4x4.TRS(pivot-q*pivot,q,Vector3.one)*f.frame;
                if(straight){
                    int layer=ClosingLayer(panel,angle);
                    float h=(f.layer+.5f)*PaperSheet.Thickness;
                    float oldHeight=(basis.layer+.5f)*PaperSheet.Thickness;
                    float flipped=Vector3.Dot(q*up,up)>=0?1:-1;
                    float idealHeight=oldHeight+flipped*(h-oldHeight);
                    frame=Matrix4x4.Translate(up*(((layer+.5f)*PaperSheet.Thickness-idealHeight)*press))*frame;
                    trial.Add(closed?Rebase(f,frame,basis.frame,layer):new PaperSheet.Face(f.points,f.layer,f.back,frame,f.material));
                }else{
                    // Stop at the first meeting of the two central faces. Continuing
                    // to 180 here would push the left and right pleats through each other.
                    float side=panel.sector<2?1:panel.sector<4?-1:0;
                    frame=Matrix4x4.Translate(contactNormal*(side*PaperSheet.Thickness*.5f*press))*frame;
                    if(closed && (panel.sector==1 || panel.sector==2) && (original.Count==1 || (Step.regionalPull && selectedFaces.Count==1)))
                        trial.Add(Rebase(f,frame,contactFrame,2-panel.sector));
                    else trial.Add(new PaperSheet.Face(f.points,f.layer,f.back,frame,f.material));
                }
            }
            for(int i=0;i<trial.Count;i++)for(int j=0;j<i;j++){
                var a=trial[i];var b=trial[j];
                if(PaperSheet.Turn.Intersects(a,b,b.frame.inverse*a.frame) || PaperSheet.Turn.Intersects(b,a,a.frame.inverse*b.frame))return false;
            }
            if(Step.regionalPull){
                // The closed-form fan assumes its outer rays can move freely.
                // A selected flap may instead share a fixed material seam with
                // an unselected ply. Never accept a pose that tears that seam.
                for(int i=0;i<panels.Count;i++){
                    var panel=panels[i];if(panel.sector<0)continue;
                    var face=trial[i];var inverse=face.material.inverse;
                    foreach(int id in panel.nodes){
                        if(id==tipNode || !nodes[id].pinned)continue;
                        var p=inverse.MultiplyPoint3x4(nodes[id].material);
                        var rest=panel.face.material.inverse.MultiplyPoint3x4(nodes[id].material);
                        float originalGap=Vector3.Distance(panel.face.World(new Vector2(rest.x,rest.z),(panel.face.layer+.5f)*PaperSheet.Thickness),nodes[id].rest);
                        if(Vector3.Distance(face.World(new Vector2(p.x,p.z),(face.layer+.5f)*PaperSheet.Thickness),nodes[id].rest)>originalGap+.012f)return false;
                    }
                }
            }
            preview.Clear();preview.AddRange(trial);previewAngle=angle;Tip=pivot+foldedRays[2]*reach;
            if(straight)Tip+=up*(topLayer-bottomLayer+1)*PaperSheet.Thickness*press*Mathf.Sign(angle);
            return true;
        }
        private static Quaternion Basis(Vector3 u,Vector3 v)=>Quaternion.LookRotation(u,Vector3.Cross(u,v));
        // Preserve the same incremental path as dragging, then refine the last
        // blocked interval instead of leaving a visible five-degree gap.
        public bool TryMove(float from,float requested,out float reached)
        {
            reached=from;
            if(float.IsNaN(from) || float.IsInfinity(from) || float.IsNaN(requested) || float.IsInfinity(requested) || !Pose(from))return false;
            float goal=Mathf.Clamp(requested,-AngleLimit,AngleLimit);
            while(reached!=goal){
                float next=Mathf.MoveTowards(reached,goal,5f);
                if(Pose(next)){reached=next;continue;}
                float blocked=next;
                for(int i=0;i<9;i++){
                    float mid=(reached+blocked)*.5f;
                    if(Pose(mid))reached=mid;else blocked=mid;
                }
                return Pose(reached);
            }
            return true;
        }
        public bool Pose(float angle)
        {
            if(clearance!=null){if(!clearance.Pose(angle))return false;Tip=clearance.Tip;return true;}
            if(squash!=null){if(!squash.Pose(angle))return false;Tip=squash.Tip;return true;}
            if(gather!=null){if(!gather.Pose(angle))return false;Tip=gather.Tip;return true;}
            if(match!=null){if(!match.Pose(angle))return false;Tip=match.Tip;return true;}
            if(petal!=null){bool ok=petal.Pose(angle);if(ok)Tip=petal.Tip;return ok;}
            if(!IsCurrent || float.IsNaN(angle) || Mathf.Abs(angle)>AngleLimit ||
                (angle>0 && blockedAbove) || (angle<0 && blockedBelow))return false;
            if(previewAngle==angle)return true;
            if(angle==0){
                coupledAngle=0;hasSolved=false;
                preview.Clear();preview.AddRange(original);previewAngle=0;Tip=pivot+along*reach;return true;
            }
            if(coupled)return PoseCoupled(angle);
            SetRotations(angle);
            if(closingFan){
                if(PoseClosingFan(angle))return true;
                if(!Step.regionalPull)return false;
                // Solve against the existing fixed seams when the free fan's
                // analytic pose is not compatible with the selected region.
            }
            if(!refine){
                preview.Clear();
                foreach(var panel in panels){
                    var q=rotations[panel.sector];var f=panel.face;
                    var pose=Matrix4x4.TRS(pivot-q*pivot,q,Vector3.one)*f.frame;
                    preview.Add(new PaperSheet.Face(f.points,f.layer,f.back,pose,f.material));
                }
                Tip=pivot+foldedRays[2]*reach;
                previewAngle=angle;return true;
            }
            foreach(var node in nodes){node.position=Vector3.zero;node.samples=0;}
            foreach(var panel in panels){
                var q=panel.sector<0?Quaternion.identity:rotations[panel.sector];
                foreach(int i in panel.nodes){
                    var node=nodes[i];node.position+=pivot+q*(node.rest-pivot);node.samples++;
                }
            }
            foreach(var node in nodes)node.position=node.pinned?node.rest:node.position/node.samples;
            var targetTip=pivot+foldedRays[2]*reach;
            if(Step.flexible && flatStack){
                var displacement=targetTip-nodes[tipNode].rest;
                foreach(var node in nodes){
                    float weight=Mathf.Clamp01(Vector3.Dot(node.rest-pivot,along)/reach);
                    node.position=node.pinned?node.rest:node.rest+weight*displacement;
                }
            }
            nodes[stopNode].position=pivot;
            nodes[tipNode].position=targetTip;
            // Keep the far side anchored while allowing corners of previously tilted panels to follow.
            int rear=-1;float distance=0;
            foreach(var panel in panels)if(panel.sector==4)foreach(int i in panel.nodes){
                float back=Vector3.Dot(pivot-nodes[i].rest,along);
                if(i!=tipNode && i!=stopNode && back>distance){rear=i;distance=back;}
            }
            if(rear>=0)nodes[rear].position=nodes[rear].rest;
            for(int iter=0;iter<(Step.flexible?1800:700);iter++){
                float error=0;
                foreach(var edge in edges){
                    var a=nodes[edge.a];var b=nodes[edge.b];var d=b.position-a.position;
                    float len=d.magnitude,diff=len-edge.length;error=Mathf.Max(error,Mathf.Abs(diff));
                    bool freeA=!a.pinned && edge.a!=rear,freeB=!b.pinned && edge.b!=rear;
                    if(len<.00001f || (!freeA && !freeB))continue;
                    float wa=freeA?a.mobility:0,wb=freeB?b.mobility:0;
                    var correction=d*(diff/(len*(wa+wb)));
                    if(freeA)a.position+=wa*correction;if(freeB)b.position-=wb*correction;
                }
                error=Mathf.Max(error,SeparateLayers(rear));
                if(error<(Step.flexible?.0003f:.001f))break;
            }
            trial.Clear();
            foreach(var panel in panels){
                var f=panel.face;
                if(panel.sector<0){trial.Add(f);continue;}
                int b=panel.ib,c=panel.ic;
                var target=nodes[panel.nodes[0]].position;
                Vector2 u=f.points[b]-f.points[0],v=f.points[c]-f.points[0];
                var rotation=Basis(nodes[panel.nodes[b]].position-target,nodes[panel.nodes[c]].position-target)*
                    Quaternion.Inverse(Basis(new Vector3(u.x,0,u.y),new Vector3(v.x,0,v.y)));
                float h=(f.layer+.5f)*PaperSheet.Thickness;
                var offset=target-rotation*new Vector3(f.points[0].x,h,f.points[0].y);
                var moved=new PaperSheet.Face(f.points,f.layer,f.back,Matrix4x4.TRS(offset,rotation,Vector3.one),f.material);
                var inv=f.material.inverse;
                foreach(int id in panel.nodes){
                    var p=inv.MultiplyPoint3x4(nodes[id].material);
                    if((moved.World(new Vector2(p.x,p.z),h)-nodes[id].position).sqrMagnitude>.012f*.012f)return false;
                }
                trial.Add(moved);
            }
            for(int i=0;i<trial.Count;i++)
            for(int j=0;j<i;j++){
                var a=trial[i];var b=trial[j];
                if(PaperSheet.Turn.Intersects(a,b,b.frame.inverse*a.frame) || PaperSheet.Turn.Intersects(b,a,a.frame.inverse*b.frame))return false;
            }
            preview.Clear();preview.AddRange(trial);previewAngle=angle;Tip=nodes[tipNode].position;return true;
        }
    }
}
