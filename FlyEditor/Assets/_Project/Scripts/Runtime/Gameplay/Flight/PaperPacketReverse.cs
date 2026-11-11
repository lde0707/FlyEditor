using System.Collections.Generic;
using UnityEngine;
namespace FlyEditor.Flight
{
    // Reverse a connected multi-panel tip. The material spine partitions the
    // book; a flood fill finds the tip packet instead of assuming two faces.
    internal sealed class PaperPacketReverse
    {
        private sealed class Piece { public PaperSheet.Face face;public int side;public bool cap;public int region=-1;public int startLayer; }
        private readonly PaperSheet sheet;
        private readonly IReadOnlyList<PaperSheet.Face> source;
        private readonly PaperSheet.Face basis;
        private sealed class Region {public Vector3 tip,stop,pivot,spine;public readonly Vector3[] flank=new Vector3[2];public bool previous;public int parent=-1,depth=1;public float alpha;}
        private readonly List<Region> regions=new List<Region>();
        private readonly IReadOnlyList<PaperSheet.Face> geometry;
        private readonly List<Piece> pieces=new List<Piece>();
        private readonly List<PaperSheet.Face> preview=new List<PaperSheet.Face>(),trial=new List<PaperSheet.Face>();
        private readonly List<PaperSheet.Guide> guides=new List<PaperSheet.Guide>();
        private Vector3 pivot,spine,inward,lateral,materialTip,materialStop;
        private readonly Vector3[] radial=new Vector3[2];
        private float halfOpening,last=float.NaN;
        private bool side0Above=true,hierarchical;
        private Matrix4x4[,] regionMaps;
        private readonly Matrix4x4[] bodyMaps=new Matrix4x4[2];
        private readonly List<int> regionOrder=new List<int>();
        private int maxDepth=1;
        private readonly List<PaperSheet.Face> middle=new List<PaperSheet.Face>();
        private readonly List<int> layers=new List<int>();
        private struct Seam {public int a,b;public Vector3 start,end;}
        private readonly List<Seam> seams=new List<Seam>();
        private static float CreaseAngle(float value)=> (value==0?65:value)*Mathf.Deg2Rad;
        public IReadOnlyList<PaperSheet.Face> Preview=>preview;
        public int ActivePanels {get;private set;}
        public bool Highlights(int index)=>index>=0&&index<pieces.Count&&pieces[index].region==0;
        public Vector3 TipWorld {get;private set;}
        public Vector3 StopWorld {get;private set;}
        private static bool Locate(IReadOnlyList<PaperSheet.Face> faces,Vector3 material,out Vector3 world,out PaperSheet.Face owner){
            if(!float.IsFinite(material.x)||!float.IsFinite(material.y)||!float.IsFinite(material.z)){world=default;owner=null;return false;}
            foreach(var f in faces){var p=PaperFoldTopology.Local(f,material);if(f.Contains(p)){world=f.World(p,0);owner=f;return true;}}world=default;owner=null;return false;
        }
        public IReadOnlyList<PaperSheet.Guide> Creases=>guides;
        private PaperPacketReverse(PaperSheet sheet,PaperSheet.Face basis,IReadOnlyList<PaperSheet.Face> geometry){this.sheet=sheet;source=sheet.Faces;this.basis=basis;this.geometry=geometry;}
        public static PaperPacketReverse Create(PaperSheet sheet,FoldStep step)=>Create(sheet,step,false);
        private static PaperPacketReverse Create(PaperSheet sheet,FoldStep step,bool hierarchical)
        {
            if(!float.IsFinite(step.reverseCreaseAngle) || step.reverseCreaseAngle!=0 && (step.reverseCreaseAngle<5 || step.reverseCreaseAngle>175 || Mathf.Abs(step.reverseCreaseAngle-90)<1))return null;
            if(!step.packetReverse||step.anchor<0||step.anchor>=sheet.Faces.Count||!float.IsFinite(step.pullTip.x)||!float.IsFinite(step.pullTip.y)||!float.IsFinite(step.pullStop.x)||!float.IsFinite(step.pullStop.y))return null;
            var selected=sheet.Faces[step.anchor];
            var materialTip=step.packetMaterialHandles?new Vector3(step.packetTipMaterial.x,0,step.packetTipMaterial.y):selected.Original(step.pullTip);
            var materialStop=step.packetMaterialHandles?new Vector3(step.packetStopMaterial.x,0,step.packetStopMaterial.y):selected.Original(step.pullStop);
            if(!selected.Contains(step.pullTip) || (materialTip-selected.Original(step.pullTip)).sqrMagnitude>.000001f || (materialTip-materialStop).sqrMagnitude<.01f)return null;
            var recipe=sheet.Recipe();int first=recipe.Length;while(first>0&&recipe[first-1].insideReverse&&recipe[first-1].packetReverse&&recipe[first-1].degrees==180)first--;
            var baseSheet=sheet;var previous=new List<Region>();
            if(first<recipe.Length){
                var prefix=new FoldStep[first];System.Array.Copy(recipe,prefix,first);baseSheet=PaperSheet.Replay(prefix);
                for(int i=first;i<recipe.Length;i++){
                    var old=recipe[i];if(!old.packetMaterialHandles)return null;
                    previous.Add(new Region{tip=new Vector3(old.packetTipMaterial.x,0,old.packetTipMaterial.y),stop=new Vector3(old.packetStopMaterial.x,0,old.packetStopMaterial.y),previous=true,alpha=CreaseAngle(old.reverseCreaseAngle)});
                }
            }
            if(!Locate(baseSheet.Faces,materialTip,out var tipWorld,out var basis)||!Locate(baseSheet.Faces,materialStop,out var stopWorld,out _))return null;
            var plan=new PaperPacketReverse(sheet,basis,baseSheet.Faces);plan.materialTip=materialTip;plan.materialStop=materialStop;
            plan.hierarchical=hierarchical;plan.pivot=stopWorld;plan.spine=(tipWorld-stopWorld).normalized;
            if(!Locate(sheet.Faces,materialTip,out var currentTip,out _)||!Locate(sheet.Faces,materialStop,out var currentStop,out _))return null;
            plan.TipWorld=currentTip;plan.StopWorld=currentStop;
            plan.regions.Add(new Region{tip=materialTip,stop=materialStop,alpha=CreaseAngle(step.reverseCreaseAngle)});plan.regions.AddRange(previous);
            foreach(var region in plan.regions){
                if(!Locate(baseSheet.Faces,region.stop,out region.pivot,out _)||!Locate(baseSheet.Faces,region.tip,out var t,out _))return null;
                region.spine=(t-region.pivot).normalized;
                if(Mathf.Abs(Vector3.Dot(region.spine,plan.spine))<.9999f||Vector3.Cross(region.pivot-plan.pivot,plan.spine).magnitude>.002f)return null;
            }
            var materialAxis=(plan.materialTip-plan.materialStop).normalized;var materialNormal=new Vector3(-materialAxis.z,0,materialAxis.x);
            var halves=new List<Piece>();
            foreach(var f in plan.geometry){
                var line=PaperFoldTopology.MaterialLine(f,plan.materialStop,materialNormal);
                foreach(int sign in new[]{1,-1}){var part=PaperSheet.Clip(f,line,sign);if(part==null)continue;halves.Add(new Piece{face=part,side=sign>0?0:1});}
                if(!plan.layers.Contains(f.layer))plan.layers.Add(f.layer);
            }
            bool[] found={false,false};
            foreach(var piece in halves){var f=piece.face;var t=PaperFoldTopology.Local(f,plan.materialTip);var s=PaperFoldTopology.Local(f,plan.materialStop);
                if(!f.Contains(t))continue;
                if(Vector3.Distance(f.World(t,0),tipWorld)>.002f)continue;
                Vector3 centre=Vector3.zero;foreach(var p in f.points)centre+=f.World(p,0);centre/=f.points.Length;
                var r=Vector3.ProjectOnPlane(centre-plan.pivot,plan.spine);if(r.sqrMagnitude<.00001f)continue;
                plan.radial[piece.side]=r.normalized;found[piece.side]=true;
            }
            if(!found[0]||!found[1]||Vector3.Dot(plan.radial[0],plan.radial[1])<-.998f)return null;
            plan.halfOpening=.5f*Vector3.Angle(plan.radial[0],plan.radial[1]);plan.inward=(plan.radial[0]+plan.radial[1]).normalized;
            var across=plan.radial[0]-plan.radial[1];plan.lateral=across.sqrMagnitude>1e-6f?across.normalized:basis.frame.MultiplyVector(Vector3.up);
            // Keep each side on the exterior corresponding to its current layer.
            if(plan.halfOpening<.01f){
                float[] layers={0,0};int[] samples={0,0};foreach(var p in halves){layers[p.side]+=p.face.layer;samples[p.side]++;}
                plan.side0Above=layers[0]/samples[0]>=layers[1]/samples[1];
                if(!plan.side0Above)plan.lateral=-plan.lateral;
            }
            foreach(var region in plan.regions)for(int i=0;i<2;i++)region.flank[i]=Mathf.Cos(region.alpha)*region.spine+Mathf.Sin(region.alpha)*plan.radial[i];
            plan.pieces.AddRange(halves);
            if(hierarchical){for(int i=1;i<plan.regions.Count;i++)plan.regionOrder.Add(i);plan.regionOrder.Add(0);}
            else for(int i=0;i<plan.regions.Count;i++)plan.regionOrder.Add(i);
            foreach(int regionIndex in plan.regionOrder){
                var region=plan.regions[regionIndex];var next=new List<Piece>();
                if(hierarchical){
                    int parent=-1;
                    foreach(var part in plan.pieces)if(part.cap && part.face.Contains(PaperFoldTopology.Local(part.face,region.tip))){
                        if(parent>=0 && parent!=part.region)return null;
                        parent=part.region;
                    }
                    region.parent=parent;region.depth=parent<0?1:plan.regions[parent].depth+1;
                    plan.maxDepth=Mathf.Max(plan.maxDepth,region.depth);
                }
                foreach(var part in plan.pieces){
                    if(!hierarchical && part.cap){
                        if(part.face.Contains(PaperFoldTopology.Local(part.face,region.tip)))return Create(sheet,step,true);
                        next.Add(part);continue;
                    }
                    if(hierarchical && part.region!=region.parent){next.Add(part);continue;}
                    var n=Mathf.Sin(region.alpha)*region.spine-Mathf.Cos(region.alpha)*plan.radial[part.side];var line=PaperFoldTopology.WorldLine(part.face,region.pivot,n);
                    var body=PaperSheet.Clip(part.face,line,-1);var cap=PaperSheet.Clip(part.face,line,1);
                    if(body!=null)next.Add(new Piece{face=body,side=part.side,cap=part.cap,region=part.region});
                    if(cap!=null)next.Add(new Piece{face=cap,side=part.side,cap=true,region=regionIndex});
                }
                plan.pieces.Clear();plan.pieces.AddRange(next);if(plan.pieces.Count>512)return null;
                var moving=new HashSet<Piece>();var queue=new Queue<Piece>();
                foreach(var part in plan.pieces)if(part.region==regionIndex&&part.face.Contains(PaperFoldTopology.Local(part.face,region.tip)))queue.Enqueue(part);
                while(queue.Count>0){var part=queue.Dequeue();if(!moving.Add(part))continue;
                    foreach(var q in plan.pieces)if(q.region==regionIndex&&q.side==part.side&&!moving.Contains(q)&&PaperFoldTopology.SharedEdge(part.face,q.face,out _,out _))queue.Enqueue(q);
                }
                int caps=0;foreach(var part in plan.pieces)if(part.region==regionIndex){if(moving.Contains(part))caps++;else{part.region=hierarchical?region.parent:-1;part.cap=part.region>=0;}}
                if(caps<2)return null;
            }
            // Every shared edge must lie on the axis of the two proposed motions.
            for(int i=0;i<plan.pieces.Count;i++)for(int j=0;j<i;j++){
                var a=plan.pieces[i];var b=plan.pieces[j];if(a.side==b.side&&a.region==b.region)continue;
                if(!PaperFoldTopology.SharedEdge(a.face,b.face,out var u,out var v))continue;
                int ri=a.cap?a.region:b.region;
                if(a.cap&&b.cap&&a.region!=b.region){
                    if(!hierarchical || a.side!=b.side)return null;
                    if(plan.regions[a.region].parent==b.region)ri=a.region;
                    else if(plan.regions[b.region].parent==a.region)ri=b.region;
                    else return null;
                }
                Vector3 axis=a.side!=b.side?plan.spine:plan.regions[ri].flank[a.side];
                var hingePivot=a.side!=b.side?plan.pivot:plan.regions[ri].pivot;
                if(Vector3.Cross(PaperFoldTopology.Position(a.face,u)-hingePivot,axis).magnitude>.002f||Vector3.Cross(PaperFoldTopology.Position(a.face,v)-hingePivot,axis).magnitude>.002f)return null;
                if(a.side!=b.side&&a.cap!=b.cap)return null;
            }
            foreach(var p in plan.pieces)if(p.region==0){var region=plan.regions[0];var n=Mathf.Sin(region.alpha)*plan.spine-Mathf.Cos(region.alpha)*plan.radial[p.side];var line=PaperFoldTopology.WorldLine(p.face,plan.pivot,n);
                if(PaperSheet.Intersection(p.face,line,out var a,out var b))plan.guides.Add(new PaperSheet.Guide{face=p.face,layer=p.face.layer,line=line,a=a,b=b});}
            foreach(var piece in plan.pieces){
                Vector2 c=Vector2.zero;foreach(var point in piece.face.points)c+=point;c/=piece.face.points.Length;
                if(!Locate(sheet.Faces,piece.face.Original(c),out _,out var owner))return null;
                piece.startLayer=owner.layer;if(piece.region==0)plan.ActivePanels++;
            }
            plan.layers.Sort();
            for(int i=0;i<plan.pieces.Count;i++)for(int j=0;j<i;j++)if(PaperFoldTopology.SharedEdge(plan.pieces[i].face,plan.pieces[j].face,out var a,out var b))plan.seams.Add(new Seam{a=i,b=j,start=a,end=b});
            if(hierarchical)plan.regionMaps=new Matrix4x4[plan.regions.Count,2];
            return plan;
        }
        private static Quaternion Basis(Vector3 a,Vector3 b)=>Quaternion.LookRotation(a,Vector3.Cross(a,b));
        // Each child is solved in its already posed parent's frame. Its two
        // flap hinges stay fixed in that frame while its shared tip turns.
        private void PoseRegions(float delta,float progress,Matrix4x4 anchorMap)
        {
            for(int side=0;side<2;side++){
                var r=Mathf.Cos(delta)*inward+(side==0?1:-1)*Mathf.Sin(delta)*lateral;
                var q=Basis(spine,r)*Quaternion.Inverse(Basis(spine,radial[side]));
                bodyMaps[side]=anchorMap*Matrix4x4.TRS(pivot-q*pivot,q,Vector3.one);
            }
            foreach(int index in regionOrder){
                var region=regions[index];
                var a=region.parent<0?bodyMaps[0]:regionMaps[region.parent,0];
                var b=region.parent<0?bodyMaps[1]:regionMaps[region.parent,1];
                var axis=a.MultiplyVector(region.spine).normalized;
                var r0=a.MultiplyVector(radial[0]).normalized;var r1=b.MultiplyVector(radial[1]).normalized;
                var toward=(r0+r1).normalized;
                float cosine=Mathf.Sqrt(Mathf.Clamp01((1+Vector3.Dot(r0,r1))*.5f));
                float theta=(!region.previous && progress<=.25f)?0:2*Mathf.Atan(Mathf.Tan(region.alpha)*cosine);
                var direction=axis*Mathf.Cos(theta)+toward*Mathf.Sin(theta);
                for(int side=0;side<2;side++){
                    var parent=side==0?a:b;var centre=parent.MultiplyPoint3x4(region.pivot);
                    var flank=parent.MultiplyVector(region.flank[side]).normalized;
                    var q=Basis(direction,flank)*Quaternion.Inverse(Basis(axis,flank));
                    regionMaps[index,side]=Matrix4x4.TRS(centre-q*centre,q,Vector3.one)*parent;
                }
            }
        }
        public bool Pose(float angle)
        {
            if(sheet.Faces!=source||!float.IsFinite(angle)||angle<0||angle>180)return false;
            if(last==angle)return true;
            float t=angle/180f;float opening=t<.25f?Mathf.Lerp(halfOpening,90,t/.25f):Mathf.Lerp(90,halfOpening,(t-.25f)/.75f);
            float delta=opening*Mathf.Deg2Rad;var activeRegion=regions[0];float theta=t<=.25f?0:2*Mathf.Atan(Mathf.Tan(activeRegion.alpha)*Mathf.Cos(delta));
            trial.Clear();
            // Hold the first body panel in place; the other half opens about the spine.
            var bodyRadial=Mathf.Cos(delta)*inward+Mathf.Sin(delta)*lateral;
            var anchorRotation=Basis(spine,bodyRadial)*Quaternion.Inverse(Basis(spine,radial[0]));
            var anchorMap=Matrix4x4.TRS(pivot-anchorRotation*pivot,anchorRotation,Vector3.one).inverse;
            if(hierarchical)PoseRegions(delta,t,anchorMap);
            foreach(var p in pieces){
                var r=Mathf.Cos(delta)*inward+(p.side==0?1:-1)*Mathf.Sin(delta)*lateral;
                Quaternion q;Vector3 centre=pivot;
                if(p.cap){
                    var region=regions[p.region];float thetaCap=region.previous?2*Mathf.Atan(Mathf.Tan(region.alpha)*Mathf.Cos(delta)):theta;
                    var direction=region.spine*Mathf.Cos(thetaCap)+inward*Mathf.Sin(thetaCap);
                    var targetFlank=Mathf.Cos(region.alpha)*region.spine+Mathf.Sin(region.alpha)*r;
                    q=Basis(direction,targetFlank)*Quaternion.Inverse(Basis(region.spine,region.flank[p.side]));centre=region.pivot;
                }else q=Basis(spine,r)*Quaternion.Inverse(Basis(spine,radial[p.side]));
                var map=Matrix4x4.TRS(centre-q*centre,q,Vector3.one);var face=p.face;
                var frame=(hierarchical?(p.cap?regionMaps[p.region,p.side]:bodyMaps[p.side]):anchorMap*map)*face.frame;
                if(angle==180 && halfOpening<.001f && PaperSheet.SameFrame(face,basis)){
                    var chart=basis.frame.inverse*frame;var points=new Vector2[face.points.Length];
                    foreach(var point in face.points)if(Mathf.Abs(chart.MultiplyPoint3x4(new Vector3(point.x,0,point.y)).y)>.002f)return false;
                    for(int k=0;k<points.Length;k++){var point=chart.MultiplyPoint3x4(new Vector3(face.points[k].x,0,face.points[k].y));points[k]=new Vector2(point.x,point.z);}
                    chart.m01=chart.m10=chart.m12=chart.m21=chart.m13=0;chart.m11=1;
                    int rank=layers.BinarySearch(face.layer);int group=p.side==0?(p.cap?2:3):(p.cap?1:0);
                    int depth=p.cap?regions[p.region].depth:0;
                    if(hierarchical)group=p.side==0?2*maxDepth+1-depth:depth;
                    if(!side0Above)group=(hierarchical?2*maxDepth+1:3)-group;
                    int layer=group*layers.Count+((hierarchical?depth%2==1:p.cap)?layers.Count-1-rank:rank);
                    trial.Add(new PaperSheet.Face(points,layer,face.back^(chart.m00*chart.m22-chart.m02*chart.m20<0),basis.frame,face.material*chart.inverse));
                }else trial.Add(new PaperSheet.Face(face.points,p.startLayer,face.back,frame,face.material));
            }
            if(angle==0)foreach(var face in trial)foreach(var point in face.points){
                if(!Locate(source,face.Original(point),out var old,out _)||Vector3.Distance(old,face.World(point,0))>.002f)return false;
            }
            // Neutral-surface contact, consistent with coordinated multi-ply folds.
            // Shell layer order is separate from rigid zero-radius hinge geometry.
            middle.Clear();foreach(var f in trial)middle.Add(new PaperSheet.Face(f.points,0,f.back,f.frame*Matrix4x4.Translate(-Vector3.up*(PaperSheet.Thickness*.5f)),f.material));
            for(int i=0;i<middle.Count;i++)for(int j=0;j<i;j++){
                var a=middle[i];var b=middle[j];if(PaperSheet.Turn.Intersects(a,b,b.frame.inverse*a.frame)||PaperSheet.Turn.Intersects(b,a,a.frame.inverse*b.frame))return false;
            }
            foreach(var seam in seams){var a=trial[seam.a];var b=trial[seam.b];
                if(Vector3.Distance(PaperFoldTopology.Position(a,seam.start),PaperFoldTopology.Position(b,seam.start))>.002f || Vector3.Distance(PaperFoldTopology.Position(a,seam.end),PaperFoldTopology.Position(b,seam.end))>.002f)return false;
            }
            preview.Clear();preview.AddRange(trial);last=angle;return true;
        }
    }
}
