using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyEditor.Flight
{
    internal sealed class FoldingDesk
    {
        private enum Phase { FirstPoint, SecondPoint, LeftFlank, RightFlank, ReviewFourPoints, PickSide, AdjustAngle, PullCorner, Opening, PreparingClearance }
        private enum PickMode { Points, Face }
        private PickMode pickMode;
        private enum EdgeAlignState { Off, Source, Target, Preview }
        private EdgeAlignState edgeAlignState;
        private struct AlignmentEdge { public int face,index;public Vector2 a,b,contact; }
        private AlignmentEdge alignSource,alignTarget,alignHover;
        private bool alignHovered;
        private bool PickingAlignment=>edgeAlignState==EdgeAlignState.Source || edgeAlignState==EdgeAlignState.Target;
        private bool CanStartAlignment=>phase!=Phase.PreparingClearance && gesture==PointerGesture.Idle && (!Adjusting || (Mathf.Abs(angle)<.01f && !editing.HasValue));
        private enum Ply { Top, Bottom, Stack, Packet }
        private enum FoldDirection { Normal, Opposite }
        private FoldDirection foldDirection;
        private int InputSign{
            get{
                if(edgeMotionSign!=0)return edgeMotionSign;
                if(pull!=null && pull.UsesClearanceSequence)return pull.Step.clearanceDirection;
                if(selected.HasValue && selected.Value.clearance!=null && selected.Value.clearance.Length>0)return selected.Value.clearanceDirection;
                int anchor=selected.HasValue?selected.Value.anchor:pinnedFace;
                Vector3 normal=anchor>=0 && anchor<sheet.Faces.Count?sheet.Faces[anchor].frame.MultiplyVector(Vector3.up):Vector3.up;
                return (Vector3.Dot(camera.transform.forward,normal)<0?1:-1)*(foldDirection==FoldDirection.Opposite?-1:1);
            }
        }
        private bool AssistedClearance=>(pull!=null && pull.UsesClearanceSequence) || (selected.HasValue && selected.Value.clearance!=null && selected.Value.clearance.Length>0);
        private bool CanReverseDirection=>edgeMotionSign==0 && !AssistedClearance && phase!=Phase.Opening && phase!=Phase.ReviewFourPoints && ((!Reversing && !(selected.HasValue && selected.Value.insideReverse)) || (reverse!=null && reverse.UsesPacket && gesture==PointerGesture.Idle));
        private struct LayerHit { public int index;public Vector2 point;public float depth; }
        private readonly List<LayerHit> layerHits=new List<LayerHit>();
        private int pinnedFace=-1,layerRank,layerCount;
        private bool explicitLayer;
        private bool edgeMatchSelected;
        private PaperCornerPull edgeSelection;
        private enum SideChoiceState { None, Ready, NeedsHinges, DifferentPlanes, AxisOutside, ContactBlocked }
        private SideChoiceState sideChoiceState;
        private PaperSurfaceChoice surfaceChoice;
        private enum SelectionOrigin { Side, Individual, Box }
        private SelectionOrigin selectionOrigin;
        private int viewRevision,hoverViewRevision=-1;
        private readonly List<int> firstPointPanels=new List<int>();
        private bool ResolvingFirstPoint=>phase==Phase.SecondPoint && firstPointPanels.Count>1;
        private Vector3 layerProbe;
        private bool CanSwapPullEnds=>!Adjusting && !edgeMatchSelected && first>=0 && second>=0 && leftFlank>=0 && rightFlank>=0;
        private bool CanFlipPaper=>!Adjusting && gesture==PointerGesture.Idle;
        private bool CanChangeLayer=>gesture!=PointerGesture.BoxSelect && gesture!=PointerGesture.LayerPress && (!Adjusting || (gesture==PointerGesture.Idle && Mathf.Abs(angle)<.01f && !editing.HasValue && pull==null && pair==null && reverse==null));
        private static readonly int[] AnglePresets={-180,-90,90,180};
        private static readonly string[] PresetLabels={"−180°","−90°","+90°","+180°"};
        private static readonly int[] ClearancePresets={45,90,135,180};
        private static readonly string[] ClearanceLabels={"25%","50%","75%","100%"};
        private static readonly string[] PocketPresetLabels={"−밀착","−벌림","+벌림","+밀착"};
        private static readonly Color Ink=new Color(.19f,.26f,.26f), Muted=new Color(.39f,.44f,.43f);
        private readonly PaperFlightController owner;
        private readonly Camera camera;
        private readonly Transform table;
        private readonly Mesh mesh;
        private readonly RenderTexture target;
        private readonly FoldMesh builder=new FoldMesh();
        private readonly List<PaperSheet.FoldVertex> vertices=new List<PaperSheet.FoldVertex>();
        private int builtInVertexCount;
        private readonly List<List<PaperSheet.FoldVertex>> referenceMembers=new List<List<PaperSheet.FoldVertex>>();
        private readonly List<PaperSheet.FoldVertex> displayVertices=new List<PaperSheet.FoldVertex>();
        private readonly List<bool> visiblePoints=new List<bool>();
        private readonly List<bool> interiorReferences=new List<bool>();
        private readonly List<bool> centreReferences=new List<bool>();
        private readonly List<float> interiorOpacityByFace=new List<float>(),interiorTargetByFace=new List<float>();
        private readonly List<float> pointOpacities=new List<float>();
        private enum PointFadePhase { Settled, Transitioning }
        private PointFadePhase pointFadePhase;
        private readonly List<PaperSheet.Guide> hinge=new List<PaperSheet.Guide>();
        private PaperSheet.Turn candidate;
        private PaperCornerPull pull;
        private CancellationTokenSource clearanceCancel;
        private Task<PaperFoldClearance.Result> clearanceTask;
        private IReadOnlyList<PaperSheet.Face> clearanceSource;
        private FoldStep? clearanceFallback;
        private PaperInsideReverse reverse;
        private bool keyAdjustment,sliderEditing;
        private bool Reversing=>reverse!=null || (pair!=null && pair.Step.insideReverse);
        private PaperMirrorFold pair;
        private Vector2 pullStart,pullVector;
        private bool Adjusting=>phase==Phase.AdjustAngle || phase==Phase.PullCorner || phase==Phase.Opening;
        private readonly List<PaperSheet.Turn> openingTurns=new List<PaperSheet.Turn>();
        private Vector3 openingHandle;
        private Vector2 openingOffset;
        private float openingMin,openingMax;
        private int first=-1,second=-1,hoverPoint=-1;
        private bool SelectingAxis=>phase==Phase.FirstPoint || phase==Phase.SecondPoint;
        private bool PickingPoint=>phase==Phase.FirstPoint || phase==Phase.SecondPoint || phase==Phase.LeftFlank || phase==Phase.RightFlank || phase==Phase.ReviewFourPoints;
        private enum PointerGesture { Idle, EdgePress, Press, LayerPress, BoxSelect, FacePress, PointPress, SecondPointPress, OpenPress, Opening, Pan, Orbit, Angle, Pull }
        private PointerGesture gesture;
        private int edgeFace=-1,edgeIndex=-1,edgeSign,edgeMotionSign;
        private PaperCornerPull edgeCandidate;
        private bool pressEmpty;
        private Vector2 pressPos,lastPan;
        private int pressedPoint=-1;
        private bool FourPointMode=>leftFlank>=0;
        private int leftFlank=-1,rightFlank=-1;
        private Vector3 panOffset;
        private bool middlePan;
        private PaperSheet sheet;
        private PaperSheet.Turn turn;
        private Phase phase;
        private Vector2 boxEnd;
        private readonly List<int> boxFaces=new List<int>();
        private FoldStep? selected;
        private int hoverSide, hoverFlap;
        private Ply scope;
        private FoldStep? editing;
        private float dragY, dragStart, angle;
        private float pitch,yaw,roll,zoom,scale;
        private enum AxisView { User, Front, Back, Right, Left, Top, Bottom }
        private AxisView axisView;
        private static readonly string[] AxisLabels={"자유 시점","정면","뒷면","오른쪽","왼쪽","위","아래"};
        private enum ViewRepeat { Idle, Waiting, Repeating }
        private ViewRepeat viewRepeat;
        private Key heldViewKey;
        private bool heldViewCtrl,heldViewShift;
        private float nextViewRepeat;
        private static readonly Key[] ViewKeys={Key.Numpad1,Key.Numpad3,Key.Numpad7,Key.Numpad9,Key.Numpad5,Key.NumpadPeriod,Key.Home,Key.NumpadPlus,Key.NumpadMinus,Key.Numpad4,Key.Numpad6,Key.Numpad2,Key.Numpad8};
        private Rect viewport;
        private bool dirty=true;
        private static readonly string[] StatNames={"출발 속도","힘 · 속도 유지","하강 속도","상승력","충돌 저항"};
        private readonly string[] statValues=new string[5];
        private IReadOnlyList<PaperSheet.Face> measuredFaces;
        private GUIStyle statValue;
        private string notice,layerLabel;
        private GUIStyle title,body,caption,action,pointLabel;
        public FoldingDesk(PaperFlightController owner,Material material)
        {
            this.owner=owner;
            table=new GameObject("종이접기 작업대").transform;
            table.SetParent(owner.transform,false);table.position=new Vector3(0,2000,0);
            mesh=new Mesh{name="겹친 종이"};mesh.MarkDynamic();
            table.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            table.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
            camera=new GameObject("종이 작업대 카메라").AddComponent<Camera>();
            camera.transform.SetParent(table,false);camera.enabled=false;camera.orthographic=true;
            camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.fieldOfView=20;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.92f,.94f,.92f);
            target=new RenderTexture(1280,900,24){name="접기 작업대",antiAliasing=4,filterMode=FilterMode.Bilinear};
            camera.targetTexture=target;
        }
        public void Open(FoldStep[] applied)
        {
            sheet=applied==null || applied.Length==0 ? new PaperSheet() : PaperSheet.Replay(applied);pitch=57f;yaw=-18f;roll=0;zoom=1f;axisView=AxisView.User;camera.orthographic=true;viewRepeat=ViewRepeat.Idle;panOffset=Vector3.zero;gesture=PointerGesture.Idle;
            editing=null;scope=Ply.Top;foldDirection=FoldDirection.Normal;
            table.gameObject.SetActive(true);Browse();
        }
        public void Close(){CancelClearance();table.gameObject.SetActive(false);}
        public void Dispose()
        {
            CancelClearance();
            camera.targetTexture=null;target.Release();Object.Destroy(target);Object.Destroy(mesh);Object.Destroy(table.gameObject);
        }
        private void Browse()
        {
            edgeAlignState=EdgeAlignState.Off;alignHovered=false;
            edgeMatchSelected=false;
            edgeSelection=null;
            surfaceChoice=null;sideChoiceState=SideChoiceState.None;selectionOrigin=SelectionOrigin.Side;hoverViewRevision=-1;
            CancelClearance();
            edgeFace=edgeIndex=-1;edgeSign=edgeMotionSign=0;edgeCandidate=null;
            pickMode=PickMode.Points;scope=Ply.Top;foldDirection=FoldDirection.Normal;
            pinnedFace=-1;layerRank=layerCount=0;layerHits.Clear();
            explicitLayer=false;firstPointPanels.Clear();
            phase=Phase.FirstPoint;first=second=hoverPoint=pressedPoint=leftFlank=rightFlank=-1;selected=null;hoverSide=hoverFlap=0;
            gesture=PointerGesture.Idle;candidate=turn=null;pull=null;pair=null;reverse=null;keyAdjustment=sliderEditing=false;hinge.Clear();openingTurns.Clear();notice=null;
            CollectReferencePoints();builtInVertexCount=vertices.Count;
            if(measuredFaces!=sheet.Faces){
                var profile=PaperFlightProfile.Measure(sheet);measuredFaces=sheet.Faces;
                statValues[0]=Rating(profile.launch);statValues[1]=Rating(profile.momentum);statValues[2]=Rating(profile.descent);
                statValues[3]=Rating(profile.climb);statValues[4]=Rating(profile.impact);
            }
            layerLabel="접기  "+sheet.Count+" / "+sheet.FoldLimit+"    ·    최대 "+sheet.PeakPlies+"겹";
            PositionCamera();
        }
        private void CollectReferencePoints()
        {
            vertices.Clear();referenceMembers.Clear();interiorReferences.Clear();centreReferences.Clear();
            if(measuredFaces!=sheet.Faces){interiorOpacityByFace.Clear();interiorTargetByFace.Clear();pointFadePhase=PointFadePhase.Settled;}
            // A common length threshold keeps an edge's density identical on
            // both adjacent faces. Endpoints are included and merged below.
            float shortEdge=Mathf.Min(sheet.Width,sheet.Height)*.25f;
            for(int i=0;i<sheet.Faces.Count;i++){
                var face=sheet.Faces[i];
                for(int edge=0;edge<face.points.Length;edge++){
                    var a=face.points[edge];var b=face.points[(edge+1)%face.points.Length];
                    float length=(b-a).sqrMagnitude;
                    if(length<.00000001f)continue;
                    int intervals=length<=shortEdge*shortEdge?2:4;
                    for(int point=0;point<=intervals;point++)
                        AddReferencePoint(i,Vector2.Lerp(a,b,point/(float)intervals));
                }
            }
            for(int i=0;i<sheet.Faces.Count;i++){
                if(interiorOpacityByFace.Count<=i){interiorOpacityByFace.Add(0);interiorTargetByFace.Add(0);}
                AddInteriorReferences(i);
            }
        }
        private void AddInteriorReferences(int anchor)
        {
            var face=sheet.Faces[anchor];float bottom=float.PositiveInfinity,top=float.NegativeInfinity;
            foreach(var p in face.points){bottom=Mathf.Min(bottom,p.y);top=Mathf.Max(top,p.y);}
            if(top-bottom<.0001f)return;
            float area=0;Vector2 weighted=Vector2.zero;
            for(int i=0;i<face.points.Length;i++){
                var a=face.points[i];var b=face.points[(i+1)%face.points.Length];float cross=a.x*b.y-b.x*a.y;
                area+=cross;weighted+=(a+b)*cross;
            }
            if(Mathf.Abs(area)>.000001f)AddReferencePoint(anchor,weighted/(3*area),true,true);
            // Three inset rows follow the convex face outline. Endpoints are
            // excluded, so these never duplicate its edge reference points.
            for(int row=1;row<=3;row++){
                float y=Mathf.Lerp(bottom,top,row*.25f),left=float.PositiveInfinity,right=float.NegativeInfinity;
                for(int edge=0;edge<face.points.Length;edge++){
                    var a=face.points[edge];var b=face.points[(edge+1)%face.points.Length];
                    if(y<Mathf.Min(a.y,b.y) || y>Mathf.Max(a.y,b.y) || Mathf.Abs(b.y-a.y)<.000001f)continue;
                    float x=Mathf.Lerp(a.x,b.x,(y-a.y)/(b.y-a.y));left=Mathf.Min(left,x);right=Mathf.Max(right,x);
                }
                if(right-left<.0001f)continue;
                for(int column=1;column<=3;column++)if(row!=2 || column!=2)AddReferencePoint(anchor,new Vector2(Mathf.Lerp(left,right,column*.25f),y),true);
            }
        }
        private void AddReferencePoint(int anchor,Vector2 point,bool interior=false,bool centre=false)
        {
            var member=new PaperSheet.FoldVertex(anchor,point);
            for(int i=0;i<vertices.Count;i++){
                // Material identity distinguishes a true joint from two unrelated
                // corners that merely overlap after folding.
                if(!SharedVertex(vertices[i],anchor,out var mapped) || (mapped.point-point).sqrMagnitude>.00000001f)continue;
                interiorReferences[i]&=interior;centreReferences[i]|=centre;
                foreach(var old in referenceMembers[i])if(old.anchor==anchor && (old.point-point).sqrMagnitude<.00000001f)return;
                referenceMembers[i].Add(member);return;
            }
            vertices.Add(member);referenceMembers.Add(new List<PaperSheet.FoldVertex>{member});interiorReferences.Add(interior);centreReferences.Add(centre);
        }
        private static string Rating(float value)=>(value*100f).ToString("0.0")+"%";
        private void PositionCamera()
        {
            viewRevision++;
            var b=sheet.SpatialBounds;
            Vector3 pivot=table.position+b.center+panOffset;
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,roll);
            camera.orthographicSize=Mathf.Max(3f,b.size.magnitude*.47f)/zoom;
            float distance=camera.orthographic?30f:camera.orthographicSize/Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad);
            camera.transform.position=pivot-camera.transform.forward*distance;
            camera.farClipPlane=Mathf.Max(100,distance+b.extents.magnitude+20);
            if(pinnedFace>=0)RefreshLayerStack();
            // Rotating must not keep an invisible material leaf selected or silently
            // move the authored line onto the new front leaf.
            if((!Adjusting || (CanChangeLayer && Mathf.Abs(angle)<.01f)) && first>=0 && !SelectionExposed()){
                Browse();notice="가려진 면의 선택을 해제했습니다. 보이는 면에서 점을 고르세요.";return;
            }
            if(edgeAlignState==EdgeAlignState.Target || (edgeAlignState==EdgeAlignState.Preview && Mathf.Abs(angle)<.01f)){
                if(!PointVisible(new PaperSheet.FoldVertex(alignSource.face,alignSource.contact),out _)){
                    Browse();notice="가려진 변의 선택을 해제했습니다. 보이는 변을 고르세요.";return;
                }
            }
            Rebuild();dirty=true;
        }
        private void Rebuild()
        {
            ResolveDisplayPoints();
            bool adjust=phase==Phase.AdjustAngle || phase==Phase.Opening;
            var shownTurn=phase==Phase.PickSide && edgeMatchSelected?edgeSelection?.EdgeTurn:turn;
            builder.Build(mesh,sheet,shownTurn,adjust?angle:0f,displayVertices,first,second,hoverPoint,phase==Phase.PickSide,hinge,pull,pair,MirrorAxis.Off,true,reverse,leftFlank,rightFlank,PickingPoint || ResolvingFirstPoint || scope==Ply.Packet || scope==Ply.Stack?-1:pinnedFace,camera.transform.forward,ResolvingFirstPoint?firstPointPanels:null,visiblePoints,pointOpacities,shownTurn==null?surfaceChoice:null,camera.orthographic?(Vector3?)null:camera.transform.position-table.position);
            dirty=true;
        }
        private bool PointVisible(PaperSheet.FoldVertex vertex,out float depth)
        {
            var face=sheet.Faces[vertex.anchor];
            depth=float.PositiveInfinity;
            if(Mathf.Abs(ViewFacing(face))<.00001f)return false;
            float h=(face.layer+(ViewFacing(face)<0?1:0))*PaperSheet.Thickness;
            var view=camera.WorldToViewportPoint(table.position+face.World(vertex.point,h));depth=view.z;
            if(depth<=camera.nearClipPlane)return false;
            sheet.PlyRange(face,vertex.point,out int bottom,out int top);
            bool front=ViewFacing(face)<0;
            if(face.layer!=(front?top:bottom))return false;
            var cover=RayHit(new Vector2(view.x,view.y),out var at);
            if(cover!=null && !PaperSheet.SamePanel(face,cover)){
                float ch=(cover.layer+(ViewFacing(cover)<0?1:0))*PaperSheet.Thickness;
                if(camera.WorldToViewportPoint(table.position+cover.World(at,ch)).z<depth-.001f)return false;
            }
            // A lower corner may project just outside the upper silhouette due
            // to thickness. Resolve its adjacent surface as well as the exact
            // zero-area vertex, so a hidden ply does not win that numerical gap.
            bool boundary=false;Vector2 centre=Vector2.zero;
            for(int i=0;i<face.points.Length;i++){
                var a=face.points[i];var edge=face.points[(i+1)%face.points.Length]-a;centre+=a;
                if(edge.sqrMagnitude>.000001f && Mathf.Abs(edge.x*(vertex.point.y-a.y)-edge.y*(vertex.point.x-a.x))<edge.magnitude*.0005f)boundary=true;
            }
            if(!boundary)return true;
            var inset=Vector2.Lerp(vertex.point,centre/face.points.Length,.002f);
            var inner=camera.WorldToViewportPoint(table.position+face.World(inset,h));
            cover=RayHit(new Vector2(inner.x,inner.y),out at);
            if(cover==null || PaperSheet.SamePanel(face,cover))return true;
            float ih=(cover.layer+(ViewFacing(cover)<0?1:0))*PaperSheet.Thickness;
            return camera.WorldToViewportPoint(table.position+cover.World(at,ih)).z>=inner.z-.001f;
        }
        private void UpdateInteriorVisibility()
        {
            // Continuous zoom response around the previous 80% fit threshold.
            // Time smoothing below also handles large mouse-wheel steps.
            for(int i=0;i<sheet.Faces.Count;i++){
                var face=sheet.Faces[i];Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
                Vector2 max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
                foreach(var p in face.points){
                    var view=camera.WorldToViewportPoint(table.position+face.World(p,(face.layer+.5f)*PaperSheet.Thickness));
                    var xy=new Vector2(view.x,view.y);min=Vector2.Min(min,xy);max=Vector2.Max(max,xy);
                }
                float fill=Mathf.Max(max.x-min.x,max.y-min.y);
                interiorTargetByFace[i]=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.72f,.88f,fill));
                if(Mathf.Abs(interiorTargetByFace[i]-interiorOpacityByFace[i])>.0001f)pointFadePhase=PointFadePhase.Transitioning;
            }
        }
        private void TickInteriorFade(float dt)
        {
            if(pointFadePhase==PointFadePhase.Settled || dt<=0)return;
            float blend=1-Mathf.Exp(-dt/.08f);bool changing=false;
            for(int i=0;i<interiorOpacityByFace.Count;i++){
                float target=interiorTargetByFace[i];float value=Mathf.Lerp(interiorOpacityByFace[i],target,blend);
                if(Mathf.Abs(target-value)<.001f)value=target;else changing=true;
                interiorOpacityByFace[i]=value;
            }
            pointFadePhase=changing?PointFadePhase.Transitioning:PointFadePhase.Settled;Rebuild();
        }
        private void ResolveDisplayPoints()
        {
            UpdateInteriorVisibility();
            displayVertices.Clear();visiblePoints.Clear();pointOpacities.Clear();
            bool locked=!PickingPoint && explicitLayer && scope==Ply.Top && pinnedFace>=0;
            for(int i=0;i<vertices.Count;i++){
                var representative=vertices[i];bool visible=false;float nearest=float.PositiveInfinity,opacity=1;
                if(i<referenceMembers.Count){
                    foreach(var member in referenceMembers[i]){
                        float memberOpacity=interiorReferences[i] && !centreReferences[i]?interiorOpacityByFace[member.anchor]:1;
                        if(memberOpacity<=.0001f)continue;
                        if(locked && !PaperSheet.SamePanel(sheet.Faces[member.anchor],sheet.Faces[pinnedFace]))continue;
                        bool exposed=PointVisible(member,out float depth);
                        if(exposed && depth<nearest){nearest=depth;representative=member;visible=true;opacity=memberOpacity;}
                    }
                }else{
                    bool exposed=PointVisible(representative,out _);
                    visible=exposed && (!locked || PaperSheet.SamePanel(sheet.Faces[representative.anchor],sheet.Faces[pinnedFace]));
                }
                bool chosen=i==first || i==second || i==leftFlank || i==rightFlank;
                if(chosen){representative=vertices[i];visible=PointVisible(representative,out _);opacity=visible?1:0;}
                displayVertices.Add(representative);visiblePoints.Add(visible);pointOpacities.Add(visible?opacity:0);
            }
        }
        private PaperSheet.Face RayHit(Vector2 uv,out Vector2 point)
        {
            Ray ray=camera.ViewportPointToRay(uv);
            float nearest=float.PositiveInfinity;point=Vector2.zero;
            PaperSheet.Face hit=null;
            foreach(var f in sheet.Faces){
                Matrix4x4 inv=f.frame.inverse;
                Vector3 origin=inv.MultiplyPoint3x4(ray.origin-table.position),dir=inv.MultiplyVector(ray.direction);
                if(Mathf.Abs(dir.y)<.00001f)continue;
                float t=((f.layer+(dir.y<0?1:0))*PaperSheet.Thickness-origin.y)/dir.y;
                if(t<0 || t>=nearest)continue;
                var p=new Vector2(origin.x+t*dir.x,origin.z+t*dir.z);
                if(TryHitPoint(f,p,out var snapped)){nearest=t;point=snapped;hit=f;}
            }
            return hit;
        }
        private bool TryHitPoint(PaperSheet.Face face,Vector2 point,out Vector2 resolved)
        {
            resolved=point;if(face.Contains(point))return true;
            // A projected silhouette can miss its own polygon by float error.
            // Accept at most two display pixels, snapping to a real boundary.
            float best=float.PositiveInfinity;
            for(int i=0;i<face.points.Length;i++){
                var a=face.points[i];var edge=face.points[(i+1)%face.points.Length]-a;
                if(edge.sqrMagnitude<.00000001f)continue;
                var q=a+edge*Mathf.Clamp01(Vector2.Dot(point-a,edge)/edge.sqrMagnitude);
                float distance=(q-point).sqrMagnitude;if(distance<best){best=distance;resolved=q;}
            }
            float facing=Mathf.Abs(ViewFacing(face));
            float tolerance=4*camera.orthographicSize/Mathf.Max(1,viewport.height)/Mathf.Max(.05f,facing);
            return best<=tolerance*tolerance && (SurfaceScreen(face,point)-SurfaceScreen(face,resolved)).sqrMagnitude<=4;
        }
        private bool TrySelectionAxis(int index,out FoldStep line)=>TrySelectionAxis(index,false,out line);
        private bool TrySelectionAxis(int index,bool project,out FoldStep line)
        {
            line=default;if(first<0 || second<0 || index<0 || index>=sheet.Faces.Count)return false;
            var hit=sheet.Faces[index];var va=vertices[first];var vb=vertices[second];
            var fa=sheet.Faces[va.anchor];var fb=sheet.Faces[vb.anchor];
            var inverse=hit.frame.inverse;
            var a3=inverse.MultiplyPoint3x4(fa.World(va.point,0));
            var b3=inverse.MultiplyPoint3x4(fb.World(vb.point,0));
            if(project){
                var direction=inverse.MultiplyVector(camera.transform.forward);
                if(Mathf.Abs(direction.y)<.00001f)return false;
                a3-=direction*(a3.y/direction.y);b3-=direction*(b3.y/direction.y);
            }else if(Mathf.Abs(a3.y)>.04f || Mathf.Abs(b3.y)>.04f)return false;
            var a=new Vector2(a3.x,a3.z);var b=new Vector2(b3.x,b3.z);var delta=b-a;
            if(delta.sqrMagnitude<.000001f)return false;
            var normal=new Vector2(-delta.y,delta.x).normalized;
            line=new FoldStep(CreaseFamily.Rising,0,1){anchor=index,flap=scope==Ply.Stack?0:index+1,
                singlePanel=scope!=Ply.Stack,connectedPanel=scope!=Ply.Stack,separateFoldSides=scope!=Ply.Stack,materialPartition=true,vertexJoints=true,preserveSlivers=true,continuousHinge=true,neutralContacts=true,compactLayers=true,creaseNormal=normal,creaseOffset=Vector2.Dot(normal,a)};
            return true;
        }
        private bool TryFaceAxis(Vector2 uv,out FoldStep line,out Vector2 point,out int flap)
        {
            line=default;flap=0;var hit=SelectedFace(uv,out point,out int index);
            if(hit==null || !TrySelectionAxis(index,out line))return false;
            line.contact=point;flap=index+1;return true;
        }
        private bool TrySideRegion(Vector2 pos,out FoldStep line,out bool coplanar,out int hitIndex)
        {
            line=default;coplanar=false;hitIndex=-1;
            var face=SelectedFace(ViewUV(pos),out var point,out hitIndex);
            if(face==null || first<0 || second<0)return false;
            int basis=vertices[first].anchor;
            coplanar=TrySelectionAxis(basis,out line);
            if(!coplanar && !TrySelectionAxis(basis,true,out line))return false;
            var local=sheet.Faces[basis].frame.inverse.MultiplyPoint3x4(face.World(point,0));
            line.contact=new Vector2(local.x,local.z);float distance=line.Distance(line.contact);
            if(Mathf.Abs(distance)<.0001f)return false;
            line.side=distance>0?1:-1;return true;
        }
        private void CollectSideRegion(FoldStep line)
        {
            if(scope!=Ply.Stack){PaperBoxSelection.Collect(sheet,camera,table.position,viewport,viewport,line,false,boxFaces);return;}
            // Whole-side selection includes off-screen and edge-on plies too.
            // The rectangle tool remains explicitly restricted to its rectangle.
            boxFaces.Clear();var basis=sheet.Faces[line.anchor];
            var normal=basis.frame.MultiplyVector(new Vector3(line.Normal.x,0,line.Normal.y));
            var at=basis.World(line.Normal*(line.Position/line.Normal.sqrMagnitude),0);
            for(int i=0;i<sheet.Faces.Count;i++){
                var face=sheet.Faces[i];var local=PaperFoldTopology.WorldLine(face,at,normal);local.preserveSlivers=true;
                bool positive=false;foreach(var p in face.points)if(local.Distance(p)*line.side>.0001f){positive=true;break;}
                if(positive && PaperSheet.Clip(face,local,line.side)!=null)boxFaces.Add(i);
            }
        }
        private bool ChooseSide(Vector2 pos,bool press)
        {
            if(!TrySideRegion(pos,out var line,out bool coplanar,out _))return false;
            CollectSideRegion(line);ResolveRegion(line,coplanar,SelectionOrigin.Side,pos,press);return true;
        }
        private void ResolveRegion(FoldStep line,bool coplanar,SelectionOrigin origin,Vector2 pos,bool press)
        {
            candidate=turn=null;surfaceChoice=null;hinge.Clear();angle=0;selectionOrigin=origin;phase=Phase.PickSide;
            if(boxFaces.Count==0){sideChoiceState=SideChoiceState.AxisOutside;notice="이쪽에서 선택할 면이 없습니다.";Rebuild();return;}
            line.selectedFaces=boxFaces.ToArray();line.flap=0;line.singlePanel=line.connectedPanel=line.separateFoldSides=false;
            selected=line;candidate=coplanar?sheet.Plan(line):null;turn=candidate;
            bool blocked=turn!=null && Mathf.Abs(turn.Constrain(1))<.001f && Mathf.Abs(turn.Constrain(-1))<.001f;
            if(blocked)turn=null;
            sideChoiceState=!coplanar?SideChoiceState.DifferentPlanes:blocked?SideChoiceState.ContactBlocked:turn==null?SideChoiceState.NeedsHinges:SideChoiceState.Ready;
            if(turn==null){
                surfaceChoice=new PaperSurfaceChoice(sheet.Faces,line,boxFaces);
                notice=!coplanar?"노란색 영역 선택됨 · 면 각도가 다릅니다.\n점 추가 [F]로 보조 접힘을 정하세요.":blocked?"노란색 영역 선택됨 · 다른 겹에 막혔습니다.\n반대 방향이나 전체 겹 [2]을 확인하세요.":"노란색 영역 선택됨 · 연결된 부분에 보조 접힘이 필요합니다.";
            }else{
                selected=turn.step;phase=Phase.AdjustAngle;pickMode=PickMode.Points;pinnedFace=-1;
                foreach(var face in turn.movingFaces)if(PaperSheet.SameFrame(face,sheet.Faces[line.anchor]) && PaperSheet.Intersection(face,line,out var a,out var b))
                    hinge.Add(new PaperSheet.Guide{line=line,a=a,b=b,layer=face.layer,face=face});
                notice=origin==SelectionOrigin.Box?"초록색: 실제 접기 영역 · V: 다시 선택":"초록색: 선 한쪽의 선택 영역 전체\n드래그: 접기 · V: 개별/박스 선택";
                if(press)StartFacePress(pos);
            }
            Rebuild();
        }
        private bool ChooseIndividualSide(Vector2 pos,bool press)
        {
            selectionOrigin=SelectionOrigin.Individual;
            var face=SelectedFace(ViewUV(pos),out var point,out int index);
            if(face==null)return false;
            bool coplanar=TrySelectionAxis(index,out var line);
            if(!coplanar && !TrySelectionAxis(index,true,out line)){
                notice="두 점이 같은 위치로 보입니다. 시점을 조금 돌려 선의 양쪽을 선택하세요.";return false;
            }
            float distance=line.Distance(point);
            if(Mathf.Abs(distance)<.0001f){notice="선 위가 아니라 접을 쪽의 면을 클릭하세요.";return false;}
            line.side=distance>0?1:-1;line.contact=point;
            selected=line;PinLayer(index,point);surfaceChoice=new PaperSurfaceChoice(face,line);
            // A visible surface can contain several pieces from earlier folds.
            // The crease may cross a connected neighbour of the clicked piece.
            // Let the material planner validate that connection and moving side;
            // a per-polygon intersection is not the player's selection boundary.
            candidate=coplanar?sheet.Plan(line):null;
            bool crosses=candidate!=null || PaperSheet.Intersection(face,line,out _,out _);
            turn=candidate!=null && !candidate.MovesOtherPlyAt(point)?candidate:null;
            bool contactBlocked=turn!=null && Mathf.Abs(turn.Constrain(1))<.001f && Mathf.Abs(turn.Constrain(-1))<.001f;
            if(contactBlocked){
                var packet=sheet.PlanContactPacket(line);
                if(packet!=null&&packet.step.contactPacket){candidate=turn=packet;selected=packet.step;contactBlocked=false;}
            }
            if(contactBlocked)turn=null;
            sideChoiceState=!coplanar?SideChoiceState.DifferentPlanes:!crosses?SideChoiceState.AxisOutside:contactBlocked?SideChoiceState.ContactBlocked:turn==null?SideChoiceState.NeedsHinges:SideChoiceState.Ready;
            if(turn!=null){
                surfaceChoice=null;phase=Phase.AdjustAngle;angle=0;sheet.MarkCrease(turn.step,hinge);
                notice=turn.step.flap==0?"전체 겹: 선 한쪽의 모든 겹을 함께 접습니다.":turn.step.contactPacket?"초록: 선택한 면 · 파란 사선: 함께 접어야 하는 접촉 겹. 드래그로 접으세요.":"선택한 쪽을 드래그해 접으세요. 겹친 뒤쪽 면은 선택하지 않습니다.";
                if(press)StartFacePress(pos);
            }else{
                phase=Phase.PickSide;gesture=PointerGesture.Idle;
                notice=sideChoiceState==SideChoiceState.DifferentPlanes?"면 선택됨 · 두 점의 면 각도가 다릅니다. F로 점 2개를 추가해 함께 접으세요.":
                    sideChoiceState==SideChoiceState.AxisOutside?"면 선택됨 · 선이 이 면을 가로지르지 않습니다. 점 위치를 바꿔주세요.":
                    sideChoiceState==SideChoiceState.ContactBlocked?"면 선택됨 · 겹친 종이가 양쪽 방향을 막습니다. 겹을 먼저 벌리거나 선을 바꿔주세요.":
                    "면 선택됨 · 이 연결은 한 축 접기로 계산되지 않습니다. F로 점 2개를 추가하세요.";
            }
            Rebuild();return true;
        }
        private PaperSheet.Face SelectedFace(Vector2 uv,out Vector2 point,out int index)
        {
            CollectLayerHits(uv);point=default;index=-1;if(layerHits.Count==0)return null;
            // Every new surface click resolves the nearest visible material.
            // A previous face selection scopes its fold, not future clicks.
            var hit=layerHits[0];point=hit.point;index=hit.index;return sheet.Faces[index];
        }
        private void CollectLayerHits(Vector2 uv)
        {
            layerHits.Clear();var ray=camera.ViewportPointToRay(uv);
            for(int i=0;i<sheet.Faces.Count;i++){
                var face=sheet.Faces[i];var inverse=face.frame.inverse;
                var origin=inverse.MultiplyPoint3x4(ray.origin-table.position);var direction=inverse.MultiplyVector(ray.direction);
                if(Mathf.Abs(direction.y)<.00001f)continue;
                float t=((face.layer+(direction.y<0?1:0))*PaperSheet.Thickness-origin.y)/direction.y;
                if(t<0)continue;
                var p=new Vector2(origin.x+t*direction.x,origin.z+t*direction.z);if(!TryHitPoint(face,p,out p))continue;
                bool duplicate=false;foreach(var old in layerHits)if(PaperSheet.SamePanel(face,sheet.Faces[old.index])){duplicate=true;break;}
                if(duplicate)continue;
                int at=layerHits.Count;while(at>0 && layerHits[at-1].depth>t)at--;
                layerHits.Insert(at,new LayerHit{index=i,point=p,depth=t});
            }
        }
        private void PinLayer(int index,Vector2 point)
        {
            if(index<0 || index>=sheet.Faces.Count)return;
            pinnedFace=index;var face=sheet.Faces[index];Vector2 centre=Vector2.zero;
            foreach(var p in face.points)centre+=p;centre/=face.points.Length;
            // Nudge off a shared edge so neighbouring pieces are not counted as plies.
            point=Vector2.Lerp(point,centre,.01f);
            layerProbe=face.World(point,(face.layer+.5f)*PaperSheet.Thickness);
            RefreshLayerStack();
        }
        private void RefreshLayerStack()
        {
            var view=camera.WorldToViewportPoint(table.position+layerProbe);CollectLayerHits(new Vector2(view.x,view.y));
            layerCount=layerHits.Count;layerRank=0;
            for(int i=0;i<layerHits.Count;i++)if(layerHits[i].index==pinnedFace){layerRank=i;break;}
        }
        private int NearestVertex(Vector2 mouse,float radius=13f)
        {
            int hit=-1;
            float best=radius*radius,depth=float.PositiveInfinity;Vector2 bestScreen=Vector2.zero;
            for(int i=0;i<vertices.Count;i++){
                if(i<visiblePoints.Count && (!visiblePoints[i] || pointOpacities[i]<.2f))continue;
                var vertex=i<displayVertices.Count?displayVertices[i]:vertices[i];var face=sheet.Faces[vertex.anchor];
                bool chosen=i==first || i==second || i==leftFlank || i==rightFlank;
                if(ResolvingFirstPoint){if(!FirstPanelContains(face))continue;}
                else if(!chosen && !PickingPoint && pinnedFace>=0 && scope!=Ply.Packet && scope!=Ply.Stack && !PaperSheet.SamePanel(face,sheet.Faces[pinnedFace]))continue;
                float h=(face.layer+(ViewFacing(face)<0?1:0))*PaperSheet.Thickness;
                var world=table.position+face.World(vertex.point,h);
                var v=camera.WorldToViewportPoint(world);
                var screen=new Vector2(viewport.x+v.x*viewport.width,viewport.y+(1-v.y)*viewport.height);
                float sq=(mouse-screen).sqrMagnitude;
                if(sq>radius*radius)continue;
                bool coincident=hit>=0 && (screen-bestScreen).sqrMagnitude<4f;
                if(coincident ? v.z>=depth-.00001f : sq>best+.001f)continue;
                // Test occlusion at the marker, so a click just outside a corner can still hit it.
                if(!chosen && (PickingPoint || !explicitLayer || pinnedFace<0 || ResolvingFirstPoint)){
                    if(scope==Ply.Bottom){
                        var cover=SelectedFace(new Vector2(v.x,v.y),out _,out _);
                        if(cover!=null && !PaperSheet.SamePanel(cover,face))continue;
                    }else{
                        // At a silhouette vertex the ray can miss its own face by
                        // float precision. Only reject an actually nearer cover.
                        var cover=RayHit(new Vector2(v.x,v.y),out var at);
                        if(cover!=null && !PaperSheet.SamePanel(cover,face)){
                            float ch=(cover.layer+(ViewFacing(cover)<0?1:0))*PaperSheet.Thickness;
                            float cd=camera.WorldToViewportPoint(table.position+cover.World(at,ch)).z;
                            if(cd<v.z-.001f)continue;
                        }
                    }
                }
                hit=i;best=sq;depth=v.z;bestScreen=screen;
            }
            return hit;
        }
        private void SetAngle(float requested)
        {
            if(Reversing){
                float next=Mathf.Clamp(Mathf.Round(requested),0,180);
                while(angle!=next){
                    float sample=Mathf.MoveTowards(angle,next,4f);
                    if(!(pair!=null?pair.Pose(sample):reverse.Pose(sample))){
                        notice="다른 면에 닿아 여기까지 들어갑니다.";Rebuild();return;
                    }
                    angle=sample;
                }
                notice=null;Rebuild();return;
            }
            if(pair!=null){
                float max=pair.Step.cornerPull && !Reversing?PaperCornerPull.LimitFor(pair.Step):180f;
                float next=Mathf.Clamp(Mathf.Round(requested),-max,max);
                while(angle!=next){
                    float sample=Mathf.MoveTowards(angle,next,3f);
                    if(!pair.Pose(sample)){notice="반대편이 걸리거나 가운데가 벌어져 여기까지 접을 수 있습니다.";Rebuild();return;}
                    angle=sample;
                }
                notice=null;Rebuild();return;
            }
            if(pull!=null){
                float next=Mathf.Clamp(Mathf.Round(requested),-pull.AngleLimit,pull.AngleLimit);
                if(next==angle)return;
                // Walk from the last valid pose so a large input cannot jump through a blocked fold.
                while(angle!=next){
                    var sample=Mathf.MoveTowards(angle,next,5f);
                    if(!pull.Pose(sample)){
                        var step=pull.Step;
                        if(!step.flexible){
                            step.flexible=true;
                            step=PullScope(step);
                            var connected=PaperCornerPull.Create(sheet,step);
                            if(connected!=null && connected.Pose(sample)){pull=connected;angle=sample;continue;}
                        }
                        if(pull.TryMove(angle,sample,out float contactAngle))angle=contactAngle;
                        notice="종이가 맞닿는 지점입니다. 양옆 접힘 위치를 바꿔보세요.";Rebuild();return;
                    }
                    angle=sample;
                }
            notice=pull.UsesCoordinatedSurface?"초록: 직접 접기 · 파란 사선: 함께 움직이는 지지면":pull.UsesClearanceSequence?"주변 겹을 정리하며 꽃잎을 접습니다.":pull.UsesRadialGather?"연결된 면을 함께 모아 접습니다.":pull.UsesPetalFold?"꼭짓점을 들어 올리며 양옆을 안으로 접습니다.":pull.UsesEdgeMatch?"가장자리를 선택한 기준선에 맞춥니다.":(pull.UsesCoupledHinges||pull.UsesExactSquash)?(Mathf.Abs(angle)<=90?"주머니를 벌리는 중입니다.":"양옆 면을 함께 눌러 펴는 중입니다."):pull.Step.flexible?"주변 면이 함께 움직입니다.":null;Rebuild();return;
            }
            requested=Mathf.Clamp(Mathf.Round(requested),-180f,180f);
            float allowed=turn.Constrain(requested);
            notice=Mathf.Abs(allowed-requested)>.01f?"다른 면에 닿았습니다. 반대 방향으로 접거나 다른 겹을 선택하세요.":null;
            angle=allowed;Rebuild();
        }
        private void CloseToContact()
        {
            if(!Adjusting || Reversing)return;
            SetAngle((angle==0?InputSign:Mathf.Sign(angle))*180);FinishInteraction();
        }
        private void FinishInteraction()
        {
            if(!Adjusting)return;
            if(Mathf.Abs(angle)<1){CancelAngle();return;}
            bool applied=pair!=null?sheet.FinishPair(pair,angle):
                reverse!=null?sheet.FinishReverse(reverse,angle):
                pull!=null?sheet.FinishPull(pull,angle):turn!=null && sheet.Finish(turn,angle);
            if(!applied){CancelAngle();notice="여기서는 접을 수 없습니다.";return;}
            editing=null;Browse();
        }
        private void CancelAngle()
        {
            if(editing.HasValue)sheet.ApplyStep(editing.Value);
            editing=null;Browse();
        }
        private void EditLast()
        {
            var recipe=sheet.Recipe();
            if(recipe.Length==0)return;
            var last=recipe[recipe.Length-1];sheet.Undo();editing=last;
            PrepareAngle(last,last.degrees==0?180f:last.degrees);
        }
        private void PrepareAngle(FoldStep line,float suggested)
        {
            if(line.cornerPull && !line.insideReverse)line.closeFlat=true;
            Browse();
            if(line.mirror!=MirrorAxis.Off){
                pair=PaperMirrorFold.Create(sheet,line);
                if(pair==null || !pair.Pose(suggested)){CancelAngle();return;}
                phase=line.cornerPull?Phase.PullCorner:Phase.AdjustAngle;angle=suggested;Rebuild();return;
            }
            if(line.insideReverse){
                reverse=PaperInsideReverse.Create(sheet,line);
                if(reverse==null || !reverse.Pose(suggested)){CancelAngle();return;}
                phase=Phase.PullCorner;angle=suggested;Rebuild();return;
            }
            if(line.cornerPull){
                pull=PaperCornerPull.Create(sheet,line);
                if(pull==null || !pull.Pose(suggested)){CancelAngle();return;}
                phase=Phase.PullCorner;angle=suggested;Rebuild();return;
            }
            SelectAxis(line);turn=sheet.Plan(line);
            if(turn==null){CancelAngle();notice="이 선에서는 종이를 나누지 않고 접을 수 없습니다.";return;}
            phase=Phase.AdjustAngle;SetAngle(suggested);
        }
        private void NewPaper()
        {
            editing=null;sheet=new PaperSheet();scope=Ply.Top;foldDirection=FoldDirection.Normal;Browse();
        }
        public void Tick(float dt)
        {
            Layout();
            var keys=Keyboard.current;
            if(keys!=null && keys.escapeKey.wasPressedThisFrame){
                if(phase!=Phase.FirstPoint || edgeAlignState!=EdgeAlignState.Off)CancelAngle();else owner.CloseFolding();return;
            }
            if(gesture==PointerGesture.BoxSelect || gesture==PointerGesture.LayerPress){
                var heldMouse=Mouse.current;
                if(heldMouse!=null){var raw=heldMouse.position.ReadValue();TickPointer(heldMouse,new Vector2(raw.x,Screen.height-raw.y)/scale);}
                if(dirty){camera.Render();dirty=false;}return;
            }
            if(keys!=null && keys.mKey.wasPressedThisFrame && CanStartAlignment){ToggleEdgeAlignment();return;}
            if(keys!=null && TickViewKeys(keys))return;
            if(keys!=null && keys.bKey.wasPressedThisFrame && CanFlipPaper){FlipPaper();return;}
            if(keys!=null && keys.tKey.wasPressedThisFrame && CanSwapPullEnds){SwapPullEnds();return;}
            if(phase==Phase.PreparingClearance){
                TickClearance();if(dirty){camera.Render();dirty=false;}return;
            }
            if(keys!=null){
                if(keys.fKey.wasPressedThisFrame && edgeAlignState==EdgeAlignState.Off){AddMorePoints();return;}
                if(keys.vKey.wasPressedThisFrame && CanChangeLayer && edgeAlignState==EdgeAlignState.Off){ToggleFacePicking();return;}
                if(keys.rKey.wasPressedThisFrame && CanReverseDirection)ReverseFoldDirection();
                if(CanChangeLayer && edgeAlignState==EdgeAlignState.Off){
                    if(keys.digit1Key.wasPressedThisFrame){ChoosePly(Ply.Top);return;}
                    if(keys.digit2Key.wasPressedThisFrame && second>=0 && !FourPointMode){ChoosePly(Ply.Stack);return;}
                }
                if(Adjusting){
                    if(keys.cKey.wasPressedThisFrame && !Reversing){CloseToContact();return;}
                    float step=keys.shiftKey.isPressed?5f:1f;
                    if(gesture==PointerGesture.Idle && keys.upArrowKey.wasPressedThisFrame){SetAngle(angle+step*(AssistedClearance?InputSign:1));keyAdjustment=true;}
                    if(gesture==PointerGesture.Idle && keys.downArrowKey.wasPressedThisFrame){SetAngle(angle-step*(AssistedClearance?InputSign:1));keyAdjustment=true;}
                    if(keyAdjustment && !keys.upArrowKey.isPressed && !keys.downArrowKey.isPressed)FinishInteraction();
                }else{
                    if(keys.eKey.wasPressedThisFrame && phase==Phase.FirstPoint && edgeAlignState==EdgeAlignState.Off && sheet.Count>0){EditLast();return;}
                    if(keys.zKey.wasPressedThisFrame && sheet.Undo())Browse();
                    if(keys.backspaceKey.wasPressedThisFrame){if(PickingAlignment)BeginEdgeAlignment();else RemoveLastPoint();}
                }
                if(keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame){
                    if(phase==Phase.FirstPoint && edgeAlignState==EdgeAlignState.Off)Apply();
                    return;
                }
            }
            var mouse=Mouse.current;
            if(mouse!=null && viewport.width>0){
                Vector2 raw=mouse.position.ReadValue();
                Vector2 pos=new Vector2(raw.x,Screen.height-raw.y)/scale;
                TickPointer(mouse,pos);
            }
            TickInteriorFade(dt);
            if(dirty){camera.Render();dirty=false;}
        }

        private Vector2 ViewUV(Vector2 pos)=>new Vector2((pos.x-viewport.x)/viewport.width,1f-(pos.y-viewport.y)/viewport.height);
        private bool TickViewKeys(Keyboard keys)
        {
            if(gesture!=PointerGesture.Idle || phase==Phase.PreparingClearance){viewRepeat=ViewRepeat.Idle;return false;}
            bool ctrl=keys.ctrlKey.isPressed,shift=keys.shiftKey.isPressed;
            foreach(var key in ViewKeys)if(keys[key].wasPressedThisFrame){
                NavigateView(key,ctrl,shift);
                bool repeat=key==Key.Numpad4||key==Key.Numpad6||key==Key.Numpad2||key==Key.Numpad8||key==Key.NumpadPlus||key==Key.NumpadMinus;
                viewRepeat=repeat?ViewRepeat.Waiting:ViewRepeat.Idle;heldViewKey=key;heldViewCtrl=ctrl;heldViewShift=shift;nextViewRepeat=Time.unscaledTime+.35f;return true;
            }
            if(viewRepeat==ViewRepeat.Idle)return false;
            if(!keys[heldViewKey].isPressed||ctrl!=heldViewCtrl||shift!=heldViewShift){viewRepeat=ViewRepeat.Idle;return false;}
            if(Time.unscaledTime<nextViewRepeat)return false;
            viewRepeat=ViewRepeat.Repeating;nextViewRepeat=Time.unscaledTime+.09f;
            NavigateView(heldViewKey,ctrl,shift);return true;
        }
        private void NavigateView(Key key,bool ctrl,bool shift)
        {
            if(key==Key.Numpad1||key==Key.Numpad3||key==Key.Numpad7){SnapView(key==Key.Numpad1?1:key==Key.Numpad3?3:7,ctrl);return;}
            if(key==Key.Numpad5){camera.orthographic=!camera.orthographic;PositionCamera();return;}
            if(key==Key.Home){FrameView(false);return;}
            if(key==Key.NumpadPeriod){FrameView(true);return;}
            if(key==Key.NumpadPlus||key==Key.NumpadMinus){SetZoom(zoom*(key==Key.NumpadPlus?1.2f:1f/1.2f),new Vector2(.5f,.5f));return;}
            if(key==Key.Numpad9){
                bool ortho=camera.orthographic;
                if(axisView==AxisView.Front||axisView==AxisView.Back)SnapView(1,axisView==AxisView.Front);
                else if(axisView==AxisView.Right||axisView==AxisView.Left)SnapView(3,axisView==AxisView.Right);
                else if(axisView==AxisView.Top||axisView==AxisView.Bottom)SnapView(7,axisView==AxisView.Top);
                else{SetViewRotation(Quaternion.AngleAxis(180,Vector3.up)*camera.transform.rotation);PositionCamera();}
                camera.orthographic=ortho;PositionCamera();return;
            }
            float horizontal=key==Key.Numpad6?1:key==Key.Numpad4?-1:0;
            float vertical=key==Key.Numpad8?1:key==Key.Numpad2?-1:0;
            if(ctrl){
                panOffset+=camera.orthographicSize*.16f*(horizontal*camera.transform.right+vertical*camera.transform.up);PositionCamera();return;
            }
            if(shift){
                if(horizontal!=0){SetViewRotation(camera.transform.rotation*Quaternion.AngleAxis(-horizontal*15,Vector3.forward));axisView=AxisView.User;PositionCamera();}
                return;
            }
            OrbitView(horizontal*15,vertical*15);
        }
        private void SetViewRotation(Quaternion rotation)
        {
            var euler=rotation.eulerAngles;pitch=euler.x;yaw=euler.y;roll=euler.z;
        }
        private void OrbitView(float horizontal,float vertical)
        {
            if(axisView!=AxisView.User)camera.orthographic=false; // Blender default Auto Perspective on leaving an axis view.
            var rotation=Quaternion.AngleAxis(-horizontal,Vector3.up)*camera.transform.rotation;
            rotation*=Quaternion.AngleAxis(vertical,Vector3.right);
            SetViewRotation(rotation);axisView=AxisView.User;PositionCamera();
        }
        private void SnapView(int axis,bool opposite)
        {
            pitch=axis==7?(opposite?270:90):0;
            yaw=axis==3?(opposite?90:270):axis==1 && opposite?180:0;roll=0;camera.orthographic=true;
            axisView=axis==1?(opposite?AxisView.Back:AxisView.Front):axis==3?(opposite?AxisView.Left:AxisView.Right):(opposite?AxisView.Bottom:AxisView.Top);
            PositionCamera();
        }
        private void FrameView(bool selection)
        {
            var all=sheet.SpatialBounds;var bounds=new Bounds();bool found=false;
            void Include(Vector3 p){if(found)bounds.Encapsulate(p);else{bounds=new Bounds(p,Vector3.zero);found=true;}}
            if(selection){
                if(turn!=null){var rotation=turn.Rotation(Adjusting?angle:0);foreach(var face in turn.movingFaces)foreach(var p in face.points)Include(rotation.MultiplyPoint3x4(face.World(p,0)));}
                else{
                    void Point(int index){if(index>=0){var v=vertices[index];Include(sheet.Faces[v.anchor].World(v.point,0));}}
                    Point(first);Point(second);Point(leftFlank);Point(rightFlank);
                }
            }
            if(!found){zoom=1;panOffset=Vector3.zero;}
            else{panOffset=bounds.center-all.center;zoom=Mathf.Clamp(Mathf.Max(3f,all.size.magnitude*.47f)/Mathf.Max(.35f,bounds.extents.magnitude*1.12f),.4f,4f);}
            PositionCamera();
        }
        private void SetZoom(float value,Vector2 uv)
        {
            float old=camera.orthographicSize;
            zoom=Mathf.Clamp(value,.4f,4f);PositionCamera();
            float difference=2*(old-camera.orthographicSize);
            panOffset+=difference*((uv.x-.5f)*camera.aspect*camera.transform.right+(uv.y-.5f)*camera.transform.up);
            PositionCamera();
        }
        private void PanTo(Vector2 pos)
        {
            var delta=pos-lastPan;lastPan=pos;
            panOffset+=2f*camera.orthographicSize/viewport.height*
                (-delta.x*camera.transform.right+delta.y*camera.transform.up);
            PositionCamera();
        }
        private void Orbit(Vector2 delta)
        {
            OrbitView(-delta.x*.34f,-delta.y*.24f);
        }
        private void ToggleEdgeAlignment()
        {
            bool enable=edgeAlignState==EdgeAlignState.Off;
            if(!enable){Browse();return;}
            BeginEdgeAlignment();
        }
        private void BeginEdgeAlignment()
        {
            Browse();edgeAlignState=EdgeAlignState.Source;
            notice="옮길 변 → 맞출 선 순서로 클릭하세요.";Rebuild();
        }
        private bool NearestAlignmentEdge(Vector2 pos,out AlignmentEdge result)
        {
            result=default;bool found=false;float best=9f*9f,bestDepth=float.PositiveInfinity;
            // Coincident edges belong to both adjacent panels. Prefer the visible
            // panel under the pointer, rather than whichever was stored first.
            SelectedFace(ViewUV(pos),out _,out int visibleIndex);
            for(int index=0;index<sheet.Faces.Count;index++){
                var face=sheet.Faces[index];Vector2 centre=Vector2.zero;
                foreach(var p in face.points)centre+=p;centre/=face.points.Length;
                for(int i=0;i<face.points.Length;i++){
                    var a=face.points[i];var b=face.points[(i+1)%face.points.Length];
                    var start=EdgeScreen(face,a);var end=EdgeScreen(face,b);var delta=end-start;
                    if(delta.sqrMagnitude<16)continue;
                    float t=Mathf.Clamp01(Vector2.Dot(pos-start,delta)/delta.sqrMagnitude);
                    float distance=(pos-(start+delta*t)).sqrMagnitude;
                    if(distance>best+.01f)continue;
                    var point=Vector2.Lerp(a,b,t);var contact=Vector2.Lerp(point,centre,.025f);
                    if(!PointVisible(new PaperSheet.FoldVertex(index,point),out float depth) ||
                        !PointVisible(new PaperSheet.FoldVertex(index,contact),out _))continue;
                    if(found && Mathf.Abs(distance-best)<=.01f){
                        bool nextUnderPointer=index==visibleIndex,previousUnderPointer=result.face==visibleIndex;
                        if(previousUnderPointer && !nextUnderPointer)continue;
                        if(nextUnderPointer==previousUnderPointer && depth>=bestDepth-.00001f)continue;
                    }
                    best=distance;bestDepth=depth;found=true;
                    result=new AlignmentEdge{face=index,index=i,a=a,b=b,contact=contact};
                }
            }
            return found;
        }
        private void TickEdgeAlignment(Mouse mouse,Vector2 pos,bool inside)
        {
            bool found=false;AlignmentEdge next=default;
            if(inside)found=NearestAlignmentEdge(pos,out next);
            if(alignHovered!=found || (found && (next.face!=alignHover.face || next.index!=alignHover.index))){
                alignHovered=found;alignHover=next;Rebuild();
            }
            if(!mouse.leftButton.wasPressedThisFrame || !inside)return;
            if(!found){
                if(RayHit(ViewUV(pos),out _)==null)Browse();
                else notice="점 사이의 변이나 중앙선을 클릭하세요.";
                return;
            }
            // Keep the exact closest point from this click, not a stale hover sample.
            if(edgeAlignState==EdgeAlignState.Source){
                alignSource=next;edgeAlignState=EdgeAlignState.Target;alignHovered=false;
                notice="이제 맞출 중앙선이나 변을 클릭하세요.";Rebuild();return;
            }
            if(TryAlignEdges(alignSource,next))alignHovered=false;
        }
        private bool TryAlignEdges(AlignmentEdge source,AlignmentEdge targetEdge)
        {
            var planned=PaperEdgeAlignment.Plan(sheet,source.face,source.a,source.b,targetEdge.face,targetEdge.a,targetEdge.b,source.contact);
            if(planned==null){notice="이 두 변은 현재 연결 상태에서 맞출 수 없습니다. 다른 기준선을 고르세요.";return false;}
            var normal=sheet.Faces[source.face].frame.MultiplyVector(Vector3.up);
            int sign=Vector3.Dot(camera.transform.forward,normal)<0?1:-1;
            var trial=new List<PaperSheet.Face>();
            bool allowed=Mathf.Abs(planned.Constrain(sign*180)-sign*180)<.001f && sheet.TryPreviewTurn(planned,sign*180,trial);
            if(!allowed){sign=-sign;allowed=Mathf.Abs(planned.Constrain(sign*180)-sign*180)<.001f && sheet.TryPreviewTurn(planned,sign*180,trial);}
            if(!allowed){notice="다른 겹에 막혀 기준선까지 맞출 수 없습니다. 겹을 먼저 벌려주세요.";return false;}
            alignSource=source;alignTarget=targetEdge;edgeAlignState=EdgeAlignState.Preview;
            selected=planned.step;turn=planned;candidate=null;surfaceChoice=null;sideChoiceState=SideChoiceState.Ready;
            pull=null;pair=null;reverse=null;first=second=leftFlank=rightFlank=-1;
            edgeMotionSign=sign;angle=0;phase=Phase.AdjustAngle;gesture=PointerGesture.Idle;
            PinLayer(source.face,source.contact);hinge.Clear();sheet.MarkCrease(planned.step,hinge);
            notice="접는 선을 계산했습니다. C를 누르면 선택한 변이 기준선에 맞춰집니다.";Rebuild();return true;
        }
        private void DrawAlignmentGuides()
        {
            if(edgeAlignState==EdgeAlignState.Off)return;
            if(edgeAlignState==EdgeAlignState.Target || edgeAlignState==EdgeAlignState.Preview)
                DrawAlignmentEdge(alignSource,new Color(.69f,.48f,.23f),"옮길 변",true);
            if(edgeAlignState==EdgeAlignState.Preview)
                DrawAlignmentEdge(alignTarget,new Color(.33f,.51f,.66f),"맞출 선",false);
            else if(alignHovered)
                DrawAlignmentEdge(alignHover,edgeAlignState==EdgeAlignState.Source?new Color(.69f,.48f,.23f):new Color(.33f,.51f,.66f),edgeAlignState==EdgeAlignState.Source?"옮길 변":"맞출 선",false);
        }
        private void DrawAlignmentEdge(AlignmentEdge edge,Color color,string label,bool moving)
        {
            var face=sheet.Faces[edge.face];Vector2 a,b;
            if(moving && edgeAlignState==EdgeAlignState.Preview && turn!=null && Mathf.Abs(angle)>.01f){
                var rotation=turn.Rotation(angle);
                float h=(face.layer+(ViewFacing(face)<0?1:0))*PaperSheet.Thickness;
                var u=camera.WorldToViewportPoint(table.position+rotation.MultiplyPoint3x4(face.World(edge.a,h)));
                var v=camera.WorldToViewportPoint(table.position+rotation.MultiplyPoint3x4(face.World(edge.b,h)));
                a=new Vector2(viewport.x+u.x*viewport.width,viewport.y+(1-u.y)*viewport.height);
                b=new Vector2(viewport.x+v.x*viewport.width,viewport.y+(1-v.y)*viewport.height);
            }else{a=EdgeScreen(face,edge.a);b=EdgeScreen(face,edge.b);}
            a-=viewport.position;b-=viewport.position;
            GUI.BeginGroup(viewport);
            var previous=GUI.matrix;var delta=b-a;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,a);
            Fill(new Rect(a.x,a.y-1.5f,delta.magnitude,3),color);GUI.matrix=previous;
            var mid=(a+b)*.5f;var box=new Rect(Mathf.Clamp(mid.x+8,0,Mathf.Max(0,viewport.width-86)),Mathf.Clamp(mid.y-26,0,Mathf.Max(0,viewport.height-25)),86,25);
            Fill(box,new Color(.98f,.98f,.96f,.95f));var old=caption.normal.textColor;caption.normal.textColor=color;GUI.Label(box,label,caption);caption.normal.textColor=old;
            GUI.EndGroup();
        }

        private Vector2 EdgeScreen(PaperSheet.Face face,Vector2 point)
        {
            bool front=ViewFacing(face)<0;
            var v=camera.WorldToViewportPoint(table.position+face.World(point,(face.layer+(front?1:0))*PaperSheet.Thickness));
            return new Vector2(viewport.x+v.x*viewport.width,viewport.y+(1-v.y)*viewport.height);
        }
        private void HoverPocketEdge(Vector2 pos,bool inside)
        {
            int faceIndex=-1,edge=-1;
            if(inside && NearestVertex(pos,8)<0){
                var face=SelectedFace(ViewUV(pos),out _,out int index);
                if(face!=null){float best=7f*7f;
                    for(int i=0;i<face.points.Length;i++){
                        var a=EdgeScreen(face,face.points[i]);var b=EdgeScreen(face,face.points[(i+1)%face.points.Length]);
                        var d=b-a;if(d.sqrMagnitude<16)continue;float t=Vector2.Dot(pos-a,d)/d.sqrMagnitude;
                        if(t<.04f||t>.96f)continue;float distance=(pos-(a+d*t)).sqrMagnitude;
                        if(distance<best){best=distance;faceIndex=index;edge=i;}
                    }
                }
            }
            if(edgeFace==faceIndex && edgeIndex==edge)return;
            edgeFace=faceIndex;edgeIndex=edge;edgeCandidate=null;edgeSign=0;hinge.Clear();
            if(edge>=0 && PaperFoldAssist.TryEdgePocket(sheet,faceIndex,edge,out edgeCandidate,out edgeSign)){
                var f=sheet.Faces[faceIndex];hinge.Add(new PaperSheet.Guide{face=f,layer=f.layer,a=f.points[edge],b=f.points[(edge+1)%f.points.Length]});
            }
            Rebuild();
        }
        private void SelectPocketEdge(Vector2 pos)
        {
            if(edgeCandidate==null)return;
            pull=edgeCandidate;selected=pull.Step;edgeMotionSign=edgeSign;scope=Ply.Top;
            var step=pull.Step;
            first=AssistPoint(new PaperSheet.FoldVertex(step.anchor,step.pullTip));
            second=AssistPoint(new PaperSheet.FoldVertex(step.anchor,step.pullStop));
            // Flanks may belong to the other leaf; the solver retains their
            // material coordinates. They do not require extra player clicks.
            leftFlank=rightFlank=-1;PinLayer(step.anchor,step.pullTip);
            candidate=turn=null;pair=null;reverse=null;angle=0;pull.Pose(0);pull.MarkGuides(hinge);
            phase=Phase.PullCorner;pressPos=pos;gesture=PointerGesture.EdgePress;
            notice="변을 가운데로 끌면 주머니를 벌린 뒤 눌러 폅니다. 반대로 끌면 되돌아갑니다.";
            Rebuild();
        }
        private void TickPointer(Mouse mouse,Vector2 pos)
        {
            bool inside=viewport.Contains(pos);
            if(gesture==PointerGesture.BoxSelect){TickBoxSelection(mouse,pos);return;}
            // Capture the gesture until release, even outside the paper viewport.
            if(gesture==PointerGesture.Orbit){
                if(mouse.rightButton.isPressed)Orbit(mouse.delta.ReadValue());
                else gesture=PointerGesture.Idle;
                return;
            }
            if(inside && gesture==PointerGesture.Idle && mouse.rightButton.wasPressedThisFrame){
                gesture=PointerGesture.Orbit;Orbit(mouse.delta.ReadValue());return;
            }
            if(inside && mouse.scroll.ReadValue().y!=0)
                SetZoom(zoom*(mouse.scroll.ReadValue().y>0?1.16f:1f/1.16f),ViewUV(pos));
            if(inside && mouse.middleButton.wasPressedThisFrame){
                gesture=PointerGesture.Pan;middlePan=true;lastPan=pos;
            }
            if(gesture==PointerGesture.Pan){
                PanTo(pos);
                if(!(middlePan?mouse.middleButton.isPressed:mouse.leftButton.isPressed))gesture=PointerGesture.Idle;
                return;
            }
            if(PickingAlignment){TickEdgeAlignment(mouse,pos,inside);return;}
            if(gesture==PointerGesture.EdgePress){
                if((pos-pressPos).sqrMagnitude>25){StartPullDrag(pressPos);TickPull(mouse,pos);}
                else if(!mouse.leftButton.isPressed)gesture=PointerGesture.Idle;
                return;
            }
            if(gesture==PointerGesture.BoxSelect){TickBoxSelection(mouse,pos);return;}
            if(gesture==PointerGesture.LayerPress){
                if((pos-pressPos).sqrMagnitude>25){
                    if(first>=0 && second>=0 && leftFlank<0){gesture=PointerGesture.BoxSelect;TickBoxSelection(mouse,pos);}
                    else {gesture=PointerGesture.Idle;PickFaceAt(pressPos);}
                    return;
                }
                if(!mouse.leftButton.isPressed){gesture=PointerGesture.Idle;if(inside)PickFaceAt(pos);}
                return;
            }
            if(pickMode==PickMode.Face && CanChangeLayer){
                if(hoverPoint>=0){hoverPoint=-1;Rebuild();}
                if(inside && mouse.leftButton.wasPressedThisFrame){pressPos=pos;gesture=PointerGesture.LayerPress;}
                return;
            }
            if(gesture==PointerGesture.OpenPress){
                if((pos-pressPos).sqrMagnitude>25){
                    gesture=PointerGesture.Idle;
                    if(BeginOpening(pressedPoint,pressPos,pos))TickOpening(mouse,pos);
                }else if(!mouse.leftButton.isPressed){
                    gesture=PointerGesture.Idle;
                    if(inside)PickVertex(pressedPoint);
                }
                return;
            }
            if(gesture==PointerGesture.Opening){TickOpening(mouse,pos);return;}
            if(gesture==PointerGesture.SecondPointPress){
                if((pos-pressPos).sqrMagnitude>25){
                    gesture=PointerGesture.Idle;
                    PickVertex(pressedPoint);
                    BeginVertexDrag(pressedPoint,pressPos,pos);
                    TickVertexDrag(mouse,pos);
                }else if(!mouse.leftButton.isPressed){
                    gesture=PointerGesture.Idle;
                    if(inside)PickVertex(pressedPoint);
                }
                return;
            }
            if(gesture==PointerGesture.PointPress){TickPointPress(mouse,pos,inside);return;}
            if(gesture==PointerGesture.Pull){TickPull(mouse,pos);return;}
            if(gesture==PointerGesture.FacePress){
                if((pos-pressPos).sqrMagnitude>25){BeginAngleDrag(pressPos);TickAngle(mouse,pos);}
                else if(!mouse.leftButton.isPressed)gesture=PointerGesture.Idle;
                return;
            }
            if(gesture==PointerGesture.Angle){TickAngle(mouse,pos);return;}
            if(gesture==PointerGesture.Press){
                if((pos-pressPos).sqrMagnitude>25){
                    gesture=PointerGesture.Pan;middlePan=false;lastPan=pressPos;PanTo(pos);return;
                }
                if(!mouse.leftButton.isPressed){
                    gesture=PointerGesture.Idle;
                    if(inside){
                        if(pressEmpty)CancelAngle();
                        else if(PickingPoint)PickSurfacePoint(pos);
                    }
                }
                return;
            }
            if(phase==Phase.FirstPoint && gesture==PointerGesture.Idle){
                HoverPocketEdge(pos,inside);
                if(inside && edgeCandidate!=null && mouse.leftButton.wasPressedThisFrame){SelectPocketEdge(pos);return;}
            }
            if(!inside){
                bool changed=hoverPoint>=0;hoverPoint=-1;
                if(!Adjusting && hoverSide!=0){
                    hoverSide=hoverFlap=0;candidate=turn=null;changed=true;
                }
                if(changed)Rebuild();
                return;
            }
            // Hover uses the same hit radius as clicking in each selection phase.
            // Include the two/four-point and idle angle states, not just the first pair.
            if(!Adjusting || CanChangeLayer){
                int near=NearestVertex(pos,PickingPoint?13f:6f);
                if(phase==Phase.PickSide && near!=first && near!=second && near!=leftFlank && near!=rightFlank)near=-1;
                if(near!=hoverPoint){hoverPoint=near;Rebuild();}
            }
            bool click=mouse.leftButton.wasPressedThisFrame;
            // Defer toggling until release so dragging a selected endpoint still folds.
            if(click && (!Adjusting || CanChangeLayer)){
                int hit=NearestVertex(pos,6f);
                if(SelectedPointSlot(hit)>=0){
                    pressedPoint=hit;pressPos=pos;gesture=PointerGesture.PointPress;return;
                }
            }
            if(click && phase==Phase.FirstPoint){
                int hit=NearestVertex(pos);
                if(hit>=0){pressedPoint=hit;pressPos=pos;gesture=PointerGesture.OpenPress;return;}
            }
            // A press on an existing second vertex belongs to the paper, not the camera.
            // A short click still selects the axis; dragging directly pulls that endpoint.
            if(click && phase==Phase.SecondPoint){
                int hit=NearestVertex(pos);
                if(hit>=0 && hit!=first){
                    pressedPoint=hit;pressPos=pos;gesture=PointerGesture.SecondPointPress;return;
                }
            }
            // After two points, unselected markers belong to the surface click.
            // F explicitly enters the third/fourth-point states.
            if(click && (PickingPoint || RayHit(ViewUV(pos),out _)==null)){
                pressEmpty=RayHit(ViewUV(pos),out _)==null && NearestVertex(pos)<0;
                gesture=PointerGesture.Press;pressPos=pos;return;
            }
            if(Adjusting){
                if(click && phase==Phase.AdjustAngle && CanChangeLayer && turn!=null && first>=0 && second>=0){
                    if(selectionOrigin==SelectionOrigin.Box)StartFacePress(pos);else if(selectionOrigin==SelectionOrigin.Individual)ChooseIndividualSide(pos,true);else ChooseSide(pos,true);return;
                }
                if(click && (phase==Phase.AdjustAngle || turn!=null))StartFacePress(pos);else if(click)StartPullDrag(pos);return;
            }
            if(!(click || dirty || mouse.delta.ReadValue()!=Vector2.zero) || sheet.Count>=sheet.FoldLimit)return;
            if(!PickingPoint){
                if(scope==Ply.Packet && !FourPointMode){
                    if(click && reverse!=null){phase=Phase.PullCorner;angle=0;StartPullDrag(pos);Rebuild();}
                    return;
                }
                if(hoverPoint>=0){
                    if(hoverSide!=0 || candidate!=null || turn!=null){
                        hoverSide=hoverFlap=0;candidate=turn=null;Rebuild();
                    }
                    return;
                }
                if(selected.HasValue && selected.Value.customFlanks){if(click)BeginFourDrag(pos);return;}
                if(click){ChooseSide(pos,true);return;}
                if(surfaceChoice!=null)return;
                // Resolve a straight crease from the two visible points on the
                // clicked ply; pocket hints and old material frames are not axes.
                bool hit=TrySideRegion(pos,out var line,out bool coplanar,out int flap);
                int sign=hit?line.side:0;
                bool changed=sign!=hoverSide || flap!=hoverFlap || hoverViewRevision!=viewRevision;
                if(changed){
                    hoverSide=sign;hoverFlap=flap;hoverViewRevision=viewRevision;candidate=null;
                    if(sign!=0 && coplanar){CollectSideRegion(line);if(boxFaces.Count>0){line.selectedFaces=boxFaces.ToArray();line.flap=0;line.singlePanel=line.connectedPanel=line.separateFoldSides=false;candidate=sheet.Plan(line);}}
                }
                if(changed || turn!=candidate){turn=candidate;Rebuild();}
            }
        }
        private void PickSurfacePoint(Vector2 pos)
        {
            if(sheet.Count>=sheet.FoldLimit)return;
            // Match the hover radius: a highlighted silhouette corner remains clickable
            // even when the pointer is just outside the paper polygon.
            int hit=NearestVertex(pos,PickingPoint?13f:8f);bool added=false;
            if(hit<0){
                var face=SelectedFace(ViewUV(pos),out var point,out int index);
                if(face==null){if(RayHit(ViewUV(pos),out _)==null)CancelAngle();else notice="선택한 겹 위의 점을 고르세요.";return;}
                // Four-point controls can lie on adjacent visible panels. The fold
                // scope stays pinned; selecting a handle does not select the entire stack.
                bool crossFace=PickingPoint;
                if(ResolvingFirstPoint && !FirstPanelContains(face) ||
                   !ResolvingFirstPoint && pinnedFace>=0 && scope!=Ply.Packet && scope!=Ply.Stack && !crossFace && !PaperSheet.SamePanel(face,sheet.Faces[pinnedFace])){
                    notice="선택한 겹 위에 점을 찍으세요. 반대쪽: 종이 뒤집기 [B]";return;
                }
                point=SnapSurfacePoint(face,index,point);
                if(!float.IsFinite(point.x)||!float.IsFinite(point.y)||!face.Contains(point))return;
                for(int i=0;i<vertices.Count;i++)if(PaperSheet.SamePanel(sheet.Faces[vertices[i].anchor],face) && (vertices[i].point-point).sqrMagnitude<.000001f){hit=i;break;}
                if(hit<0){hit=vertices.Count;vertices.Add(new PaperSheet.FoldVertex(index,point));added=true;}
            }
            if(DeselectPoint(hit))return;
            bool accepted;
            if(phase==Phase.PickSide){
                accepted=!FourPointMode;
                if(accepted){leftFlank=hit;ClearSelectionPreview();ShowThreePoints();}
                else notice="선택한 점을 해제한 뒤 새 점을 찍으세요.";
            }else accepted=PickVertex(hit);
            if(!accepted && added)vertices.RemoveAt(vertices.Count-1);
            Rebuild();
        }
        // Snap only within eight display pixels on the selected paper surface.
        // No grid is involved; alignment follows that surface when the camera rotates.
        private float ViewFacing(PaperSheet.Face face)
        {
            if(camera.orthographic)return Vector3.Dot(camera.transform.forward,face.frame.MultiplyVector(Vector3.up));
            Vector2 centre=Vector2.zero;foreach(var p in face.points)centre+=p;centre/=face.points.Length;
            var ray=table.position+face.World(centre,0)-camera.transform.position;
            return Vector3.Dot(ray.normalized,face.frame.MultiplyVector(Vector3.up));
        }
        private Vector2 SurfaceScreen(PaperSheet.Face face,Vector2 point)
        {
            float h=(face.layer+(ViewFacing(face)<0?1:0))*PaperSheet.Thickness;
            var v=camera.WorldToViewportPoint(table.position+face.World(point,h));
            return new Vector2(viewport.x+v.x*viewport.width,viewport.y+(1-v.y)*viewport.height);
        }
        private Vector2 SnapSurfacePoint(PaperSheet.Face face,int index,Vector2 point)
        {
            Vector2 screen=SurfaceScreen(face,point),bestPoint=point;float best=64f;
            // Corners and the silhouette remain reachable without permanent markers.
            foreach(var corner in face.points){float d=(SurfaceScreen(face,corner)-screen).sqrMagnitude;if(d<best){best=d;bestPoint=corner;}}
            if(best<64f)return bestPoint;
            if(phase==Phase.SecondPoint && first>=0 && SharedVertex(vertices[first],index,out var origin)){
                var delta=point-origin.point;
                foreach(var candidate in new[]{new Vector2(point.x,origin.point.y),new Vector2(origin.point.x,point.y)}){
                    if(!face.Contains(candidate)||(candidate-origin.point).sqrMagnitude<.0001f)continue;
                    if((candidate-point).magnitude>delta.magnitude*.07f)continue;
                    float d=(SurfaceScreen(face,candidate)-screen).sqrMagnitude;
                    if(d<best){best=d;bestPoint=candidate;}
                }
                if(best<64f)return bestPoint;
            }
            // Snap to a polygon boundary only at a short distance; never to empty air.
            for(int i=0;i<face.points.Length;i++){
                var a=face.points[i];var b=face.points[(i+1)%face.points.Length];var e=b-a;
                if(e.sqrMagnitude<.000001f)continue;
                var q=a+e*Mathf.Clamp01(Vector2.Dot(point-a,e)/e.sqrMagnitude);
                float d=(SurfaceScreen(face,q)-screen).sqrMagnitude;if(d<best){best=d;bestPoint=q;}
            }
            return bestPoint;
        }
        private int AssistPoint(PaperSheet.FoldVertex point)
        {
            var material=sheet.Faces[point.anchor].Original(point.point);
            for(int i=0;i<vertices.Count;i++){
                var v=vertices[i];
                if((sheet.Faces[v.anchor].Original(v.point)-material).sqrMagnitude>.000001f)continue;
                if(i!=first && i!=second)vertices[i]=point;
                return i;
            }
            vertices.Add(point);return vertices.Count-1;
        }
        private void SuggestFlanks()
        {
            if(phase!=Phase.PickSide||first<0||second<0||leftFlank>=0||scope==Ply.Stack||scope==Ply.Packet)return;
            if(!PaperFoldAssist.TryFlanks(sheet,vertices[first],vertices[second],out var a,out var b)){
                var request=PullScope(new FoldStep{anchor=vertices[first].anchor,cornerPull=true,customFlanks=true,
                    flexible=true,closeFlat=true,exactSquash=true,pullTip=HandlePoint(first),pullStop=HandlePoint(second)});
                request.customFlanks=false;
                if(PaperFoldClearance.HasSpine(sheet,request)){BeginClearance(request);Rebuild();return;}
                notice="이 모양은 양옆 점을 직접 골라주세요. 점 추가 [F]를 누르면 현재 두 점은 유지됩니다.";return;
            }
            ClearSelectionPreview();
            leftFlank=AssistPoint(a);rightFlank=AssistPoint(b);
            if(CompleteFourPoints()){
                if(phase!=Phase.PreparingClearance)notice="양옆 점을 찾았습니다. 면이나 끝점을 드래그하세요. 점을 다시 클릭하면 바꿀 수 있습니다.";return;
            }
            leftFlank=rightFlank=-1;PruneSurfacePoints();SelectPair();
            notice="양옆 점을 직접 고르면 더 정확히 접을 수 있습니다. 점 추가 [F]";
        }
        private void AddMorePoints()
        {
            if(phase==Phase.AdjustAngle && turn!=null && first>=0 && second>=0 && leftFlank<0 && !editing.HasValue){
                if(gesture!=PointerGesture.Idle || Mathf.Abs(angle)>.01f){notice="각도를 0도로 돌리거나 접기를 마친 뒤 점을 추가하세요.";return;}
                phase=Phase.PickSide;
            }
            if(Adjusting || phase!=Phase.PickSide || second<0 || leftFlank>=0)return;
            ClearSelectionPreview();phase=Phase.LeftFlank;AddHandleGuide(first,second);
            notice="양옆 접힘을 정할 점을 하나씩 찍으세요.";Rebuild();
        }
        private void PruneSurfacePoints()
        {
            for(int i=vertices.Count-1;i>=builtInVertexCount;i--){
                if(i==first || i==second || i==leftFlank || i==rightFlank)continue;
                vertices.RemoveAt(i);ShiftIndex(ref first,i);ShiftIndex(ref second,i);ShiftIndex(ref leftFlank,i);ShiftIndex(ref rightFlank,i);
                ShiftIndex(ref pressedPoint,i);ShiftIndex(ref hoverPoint,i);
            }
        }
        private static void ShiftIndex(ref int index,int removed){if(index==removed)index=-1;else if(index>removed)index--;}
        private void StartFacePress(Vector2 pos){pressPos=pos;gesture=PointerGesture.FacePress;}
        private void TickAngle(Mouse mouse,Vector2 pos)
        {
            float value=dragStart+(dragY-pos.y)*.65f*(Reversing?1:InputSign);
            if(Mathf.Abs(value-angle)>.5f)SetAngle(value);
            if(!mouse.leftButton.isPressed)FinishInteraction();
        }
        private void SelectFaceHinge(Vector2 pos)
        {
            var face=SelectedFace(ViewUV(pos),out var point,out int index);
            if(face==null || index<0)return;
            PinLayer(index,point);
            explicitLayer=true;firstPointPanels.Clear();
            sheet.OpeningTurns(new PaperSheet.FoldVertex(index,point),openingTurns,scope==Ply.Stack,true,true);
            PaperSheet.Turn best=null;float distance=float.PositiveInfinity;
            foreach(var plan in openingTurns){
                float d=Mathf.Abs(plan.step.Distance(point))/plan.step.Normal.magnitude;
                if(d>=distance)continue;distance=d;best=plan;
            }
            if(best==null){notice="겹을 선택했습니다. 접을 선의 두 점을 고르세요.";Rebuild();return;}
            turn=best;candidate=null;selected=best.step;pull=null;pair=null;reverse=null;
            hinge.Clear();sheet.MarkCrease(best.step,hinge);angle=0;
            phase=Phase.AdjustAngle;gesture=PointerGesture.Idle;notice="선택한 면의 각도를 조절하세요.";Rebuild();
        }
        private void TickPull(Mouse mouse,Vector2 pos)
        {
            float distance=Vector2.Dot(pos-pullStart,pullVector)/pullVector.sqrMagnitude;
            float inward=Mathf.Clamp(distance,Reversing||edgeMotionSign!=0?0f:-2f,Reversing?1f:2f);
            if(Reversing){
                if(inward>.5f && (1-inward)*pullVector.magnitude<5f)inward=1;
                SetAngle(180*inward);
            }else{
                float amount=Mathf.Abs(inward);
                if(amount>1.999f || (edgeMotionSign!=0 && amount>1.9f))amount=2f;
                float tilt=Mathf.Acos(1-amount)*Mathf.Rad2Deg;
                SetAngle(InputSign*Mathf.Sign(inward)*tilt);
            }
            if(!mouse.leftButton.isPressed)FinishInteraction();
        }
        private void TickPointPress(Mouse mouse,Vector2 pos,bool inside)
        {
            if((pos-pressPos).sqrMagnitude>25){
                // Invalid four-point constraints must not fall back to opening an old hinge.
                if(phase==Phase.ReviewFourPoints){
                    gesture=PointerGesture.Idle;
                    if(NearEnd(pressedPoint,second)){
                        SwapPullEnds();
                        if(phase==Phase.PickSide && selected.HasValue){BeginPull(first,pressPos);TickVertexDrag(mouse,pos);}
                    }
                    return;
                }
                if(NearEnd(pressedPoint,first) || NearEnd(pressedPoint,second)){
                    gesture=PointerGesture.Idle;
                    BeginVertexDrag(pressedPoint,pressPos,pos);
                    TickVertexDrag(mouse,pos);
                }else{
                    if(!BeginOpening(pressedPoint,pressPos,pos)){
                        gesture=PointerGesture.Pan;middlePan=false;lastPan=pressPos;PanTo(pos);
                    }else TickOpening(mouse,pos);
                }
                return;
            }
            if(mouse.leftButton.isPressed)return;
            gesture=PointerGesture.Idle;
            if(!inside)return;
            if(DeselectPoint(pressedPoint))return;
            if(!FourPointMode){
                leftFlank=pressedPoint;ClearSelectionPreview();ShowThreePoints();return;
            }
            notice="선택한 점을 다시 클릭해 해제한 뒤 새 점을 고르세요.";Rebuild();
        }
        private int SelectedPointSlot(int hit)
        {
            if(hit<0)return -1;
            if(NearEnd(hit,first))return 0;
            if(NearEnd(hit,second))return 1;
            if(NearEnd(hit,leftFlank))return 2;
            return NearEnd(hit,rightFlank)?3:-1;
        }
        private bool DeselectPoint(int hit)
        {
            int slot=SelectedPointSlot(hit);
            if(slot<0)return false;
            ClearSelectionPreview();
            // Keep the other points in selection order; their count determines the mode.
            if(slot==0)first=second;
            if(slot<=1)second=leftFlank;
            if(slot<=2)leftFlank=rightFlank;
            rightFlank=-1;
            firstPointPanels.Clear();
            if(first<0){Browse();return true;}
            PruneSurfacePoints();
            PinLayer(vertices[first].anchor,vertices[first].point);
            if(second<0){phase=Phase.SecondPoint;PrepareFirstPanels();notice=null;Rebuild();}
            else if(leftFlank<0)SelectPair();
            else ShowThreePoints();
            return true;
        }
        private void BeginAngleDrag(Vector2 pos)
        {gesture=PointerGesture.Angle;dragY=pos.y;dragStart=angle;}
        private bool PickVertex(Vector2 pos)=>PickVertex(NearestVertex(pos));
        private bool PickVertex(int hit)
        {
            if(sheet.Count>=sheet.FoldLimit)return false;
            if(hit>=0 && hit<displayVertices.Count && (hit!=first && hit!=second))vertices[hit]=displayVertices[hit];
            if(phase==Phase.FirstPoint){
                if(hit<0){notice="종이 위의 점을 클릭하세요.";return false;}
                first=hit;PinLayer(vertices[hit].anchor,vertices[hit].point);hoverPoint=-1;phase=Phase.SecondPoint;
                PrepareFirstPanels();notice=ResolvingFirstPoint?"여러 면이 만나는 점입니다. 두 번째 점을 고르면 접을 면이 정해집니다.":null;Rebuild();return true;
            }
            if(hit<0){notice="표시된 점을 클릭하세요.";return false;}
            if(hit==first){notice="첫 점과 다른 점을 고르세요.";return false;}
            if(phase==Phase.LeftFlank){leftFlank=hit;ClearSelectionPreview();ShowThreePoints();return true;}
            if(phase==Phase.RightFlank || phase==Phase.ReviewFourPoints)return PickFlank(hit);
            if(ResolvingFirstPoint){
                var b=vertices[hit];
                if(!SharedVertex(vertices[first],b.anchor,out var a) || (a.point-b.point).sqrMagnitude<.000001f){
                    notice="첫 점과 함께 있는 면의 다른 점을 고르세요.";return false;
                }
                vertices[first]=a;PinLayer(b.anchor,(a.point+b.point)*.5f);
            }
            firstPointPanels.Clear();
            second=hit;SelectPair();return true;
        }
        private void PrepareFirstPanels()
        {
            // Endpoints belong to the visible surface that was clicked. Choosing
            // the first endpoint must not restrict the second to that panel.
            firstPointPanels.Clear();
        }
        private bool FirstPanelContains(PaperSheet.Face face)
        {
            foreach(int i in firstPointPanels)if(PaperSheet.SamePanel(face,sheet.Faces[i]))return true;
            return false;
        }
        private void SelectPair()
        {
            ClearSelectionPreview();
            var a=vertices[first];var b=vertices[second];
            if(scope==Ply.Packet){
                var materialStop=sheet.Faces[b.anchor].Original(b.point);var localStop=sheet.Faces[a.anchor].material.inverse.MultiplyPoint3x4(materialStop);
                reverse=PocketAt(a,new Vector2(localStop.x,localStop.z),out var packetStep);
                if(reverse!=null&&reverse.Pose(0)){
                    selected=reverse.Step;phase=Phase.PickSide;hinge.AddRange(reverse.Creases);
                    notice="끝부분 "+reverse.PacketPanels+"개 면 연결 · 기준 몸통을 잡고 다른 면을 벌립니다. 끝점을 끌거나 면을 클릭하세요.";
                }else{reverse=null;phase=Phase.PickSide;AddHandleGuide(first,second);notice="이 묶음은 아직 뒤집을 수 없습니다. 끝점과 같은 접힘축 위의 기준점을 고르세요.";}
                Rebuild();return;
            }
            if(SharedVertex(a,b.anchor,out var sharedA) && PointVisible(sharedA,out _)){
                vertices[first]=a=sharedA;
            }else if(!explicitLayer && RebindVisibleHandle(first,b.anchor)){
                a=vertices[first];
            }else if(SharedVertex(b,a.anchor,out var sharedB) && PointVisible(sharedB,out _))vertices[second]=b=sharedB;
            int basis=scope==Ply.Stack || !explicitLayer || pinnedFace<0?a.anchor:pinnedFace;
            if(TrySelectionAxis(basis,out var line)){
                if(scope!=Ply.Stack)PinLayer(basis,(a.point+b.point)*.5f);
                SelectAxis(line);
            }
            else notice="선은 연결됐습니다. 기울어진 면을 함께 변형하려면 점 추가 [F]로 양옆 접힘을 정하세요.";
            hoverPoint=-1;phase=Phase.PickSide;Rebuild();
        }
        private void ClearSelectionPreview()
        {
            CancelClearance();
            edgeMatchSelected=false;
            edgeSelection=null;
            surfaceChoice=null;sideChoiceState=SideChoiceState.None;selectionOrigin=SelectionOrigin.Side;hoverViewRevision=-1;
            selected=null;candidate=turn=null;pull=null;pair=null;reverse=null;hinge.Clear();
            hoverPoint=-1;hoverSide=hoverFlap=0;angle=0;gesture=PointerGesture.Idle;
        }
        private void ShowThreePoints()
        {
            phase=Phase.RightFlank;AddHandleGuide(first,second);AddHandleGuide(second,leftFlank);
            notice=null;Rebuild();
        }
        private void RemoveLastPoint()
        {
            if(Adjusting || first<0)return;
            ClearSelectionPreview();
            if(rightFlank>=0){rightFlank=-1;ShowThreePoints();}
            else if(leftFlank>=0){leftFlank=-1;SelectPair();}
            else if(second>=0){second=-1;phase=Phase.SecondPoint;PrepareFirstPanels();notice=null;Rebuild();}
            else Browse();
            PruneSurfacePoints();Rebuild();
        }
        private Vector2 HandlePoint(int index)=>HandlePoint(index,first);
        private Vector2 HandlePoint(int index,int origin)
        {
            var vertex=vertices[index];var basis=sheet.Faces[vertices[origin].anchor];
            var p=basis.material.inverse.MultiplyPoint3x4(sheet.Faces[vertex.anchor].Original(vertex.point));
            return new Vector2(p.x,p.z);
        }
        private bool PickFlank(int hit)
        {
            if(hit==first || hit==second || hit==leftFlank){notice="서로 다른 네 점을 선택하세요.";return false;}
            ClearSelectionPreview();rightFlank=hit;
            if(!CompleteFourPoints())ReviewFourPoints();
            // A valid click remains selected even when its fold cannot be planned.
            return true;
        }
        private void SwapPullEnds()
        {
            if(!CanSwapPullEnds)return;
            ClearSelectionPreview();
            int previous=first;first=second;second=previous;
            if(!ResolveFourPoints(false))ReviewFourPoints();
        }
        private void ReviewFourPoints()
        {
            ClearSelectionPreview();phase=Phase.ReviewFourPoints;
            AddHandleGuide(first,second);AddHandleGuide(second,leftFlank);AddHandleGuide(second,rightFlank);
            notice="네 점은 선택됐지만 이 조합으로 접을 수 없습니다. 1 이동점과 2 기준점을 바꾸거나 선택한 겹을 확인하세요.";
            Rebuild();
        }
        private FoldStep PullScope(FoldStep step)
        {
            step.materialPartition=true;
            step.vertexJoints=true;
            step.preserveSlivers=true;
            step.coordinatedSurface=true;
            step.mirror=MirrorAxis.Off;step.compactLayers=true;
            step.flap=scope==Ply.Stack?0:step.anchor+1;
            step.regionalPull=step.customFlanks && scope!=Ply.Stack;
            step.coupledHinges=step.customFlanks;
            step.coordinatedCreases=step.customFlanks;
            step.adaptiveCreases=step.customFlanks;
            step.continuousHinge=true;step.neutralContacts=true;
            step.separateFoldSides=step.regionalPull;
            step.connectedPetalBacking=step.regionalPull;
            step.partialPetalBacking=step.regionalPull;
            step.singlePanel=scope!=Ply.Stack && !step.regionalPull;
            return step;
        }
        private FoldStep FourPointStep(int start,int end)
        {
            var tip=HandlePoint(start,start);var stop=HandlePoint(end,start);var n=new Vector2(stop.y-tip.y,tip.x-stop.x).normalized;
            return PullScope(new FoldStep(CreaseFamily.Rising,0,1){anchor=vertices[start].anchor,exactSquash=true,cornerPull=true,flexible=true,customFlanks=true,closeFlat=true,
                pullTip=tip,pullStop=stop,pullLeft=HandlePoint(leftFlank,start),pullRight=HandlePoint(rightFlank,start),
                creaseNormal=n,creaseOffset=Vector2.Dot(n,stop)});
        }
        private bool OpensTowardViewer(PaperCornerPull plan,FoldStep step)
        {
            if(plan==null || !plan.UsesPetalFold)return false;
            var normal=sheet.Faces[step.anchor].frame.MultiplyVector(Vector3.up);
            return plan.Pose(Vector3.Dot(camera.transform.forward,normal)<0?5:-5);
        }
        private bool CompleteFourPoints()=>ResolveFourPoints(true);
        private bool ResolveFourPoints(bool detectMovingEnd)
        {
            var line=FourPointStep(first,second);
            var selectedPlan=PaperCornerPull.Create(sheet,line);
            // A doubled leaf has one free end. Resolve that physical end before
            // an expensive clearance search; never select a different hidden ply.
            if(detectMovingEnd && !OpensTowardViewer(selectedPlan,line)){
                var alternative=FourPointStep(second,first);
                var alternatePlan=PaperCornerPull.Create(sheet,alternative);
                if(OpensTowardViewer(alternatePlan,alternative)){
                    int old=first;first=second;second=old;
                    line=alternative;selectedPlan=alternatePlan;
                }
            }
            var tip=line.pullTip;
            if(selectedPlan==null){if(PaperFoldClearance.HasSpine(sheet,line)){BeginClearance(line);Rebuild();return true;}notice="중심에서 봤을 때 선의 양옆에 있는 점을 선택하세요.";return false;}
            line=selectedPlan.Step;selected=line;edgeMatchSelected=selectedPlan.UsesEdgeMatch;
            edgeSelection=selectedPlan.UsesEdgeMatch?selectedPlan:null;
            if(line.regionalPull)PinLayer(line.anchor,tip);
            hinge.Clear();if(!selectedPlan.MarkGuides(hinge)){AddHandleGuide(first,second);AddHandleGuide(second,leftFlank);AddHandleGuide(second,rightFlank);}
            phase=Phase.PickSide;notice=selectedPlan.UsesCoordinatedSurface?"초록: 직접 접기 · 파란 사선: 함께 움직이는 지지면. 면을 드래그하세요.":selectedPlan.UsesRadialGather?"양쪽 면을 함께 모읍니다. 면을 드래그하세요.":selectedPlan.UsesEdgeMatch?"선택한 변을 기준선에 맞춥니다. 면을 드래그하세요.":selectedPlan.UsesPetalFold?"끝점을 들어 올리면 양옆이 안으로 접힙니다.":"움직일 꼭짓점을 끌거나 면을 드래그하세요.";
            if(selectedPlan.UsesPetalFold && !selectedPlan.Pose(5) && !selectedPlan.Pose(-5))BeginClearance(line);
            Rebuild();return true;
        }
        private void CancelClearance()
        {
            if(clearanceCancel!=null){clearanceCancel.Cancel();clearanceCancel.Dispose();clearanceCancel=null;}
            clearanceTask=null;clearanceSource=null;clearanceFallback=null;
        }
        private void BeginClearance(FoldStep request)
        {
            CancelClearance();clearanceFallback=request.customFlanks?request:selected;selected=request;phase=Phase.PreparingClearance;gesture=PointerGesture.Idle;
            clearanceSource=sheet.Faces;var snapshot=sheet.PlanningCopy();
            clearanceCancel=new CancellationTokenSource();var token=clearanceCancel.Token;
            clearanceTask=Task.Run(()=>PaperFoldClearance.Search(snapshot,request,token),token);
            notice="주변 겹이 걸리지 않는 접힘 경로를 찾는 중입니다. Esc: 취소";
        }
        private void TickClearance()
        {
            if(clearanceTask==null||!clearanceTask.IsCompleted)return;
            var task=clearanceTask;var fallback=clearanceFallback;bool current=sheet.Faces==clearanceSource;
            var result=task.Status==TaskStatus.RanToCompletion?task.Result:null;
            if(task.IsFaulted)Debug.LogException(task.Exception);
            CancelClearance();phase=Phase.PickSide;
            if(current && result!=null){
                selected=result.step;hinge.Clear();
                if(leftFlank<0)leftFlank=MaterialHandle(result.step.pullLeft);
                if(rightFlank<0)rightFlank=MaterialHandle(result.step.pullRight);
                notice="면을 드래그하면 주변 겹을 먼저 정리한 뒤 함께 접습니다. 놓으면 적용됩니다.";
            }else {selected=fallback;notice="선택한 점에서 실행 가능한 접힘 경로를 찾지 못했습니다. 같은 주머니의 시작점과 양옆 점을 확인하세요.";}
            Rebuild();
        }
        private int MaterialHandle(Vector2 local)
        {
            var material=sheet.Faces[selected.Value.anchor].Original(local);
            for(int i=0;i<sheet.Faces.Count;i++){
                var point=PaperFoldTopology.Local(sheet.Faces[i],material);
                if(sheet.Faces[i].Contains(point))return AssistPoint(new PaperSheet.FoldVertex(i,point));
            }return -1;
        }
        private void AddHandleGuide(int from,int to)
        {
            var a=vertices[from];var b=vertices[to];
            var start=sheet.Faces[a.anchor].Original(a.point);var end=sheet.Faces[b.anchor].Original(b.point);
            foreach(var face in sheet.Faces){
                if(leftFlank<0 && scope!=Ply.Stack && scope!=Ply.Packet && !PaperSheet.SamePanel(face,sheet.Faces[a.anchor]))continue;
                var inverse=face.material.inverse;var u=inverse.MultiplyPoint3x4(start);var v=inverse.MultiplyPoint3x4(end);
                var p=new Vector2(u.x,u.z);var q=new Vector2(v.x,v.z);
                if(PaperSheet.Trim(face,ref p,ref q))hinge.Add(new PaperSheet.Guide{face=face,layer=face.layer,a=p,b=q});
            }
        }
        private void BeginFourDrag(Vector2 pos)
        {
            var step=PullScope(selected.Value);
            selected=step;pull=edgeSelection!=null && edgeSelection.IsCurrent?edgeSelection:PaperCornerPull.Create(sheet,step);
            if(pair==null && pull==null){notice="네 점을 다시 선택하세요.";return;}
            if(!(pair!=null?pair.Pose(0):pull.Pose(0))){pair=null;pull=null;return;}
            candidate=turn=null;phase=Phase.AdjustAngle;angle=0;StartFacePress(pos);Rebuild();
        }
        private bool SharedVertex(PaperSheet.FoldVertex vertex,int target,out PaperSheet.FoldVertex mapped)
        {
            mapped=default;
            var source=sheet.Faces[vertex.anchor];var face=sheet.Faces[target];
            var local=face.material.inverse.MultiplyPoint3x4(source.Original(vertex.point));
            var point=new Vector2(local.x,local.z);
            if(!face.Contains(point))return false;
            var from=source.World(vertex.point,(source.layer+.5f)*PaperSheet.Thickness);
            var to=face.World(point,(face.layer+.5f)*PaperSheet.Thickness);
            if((from-to).sqrMagnitude>16*PaperSheet.Thickness*PaperSheet.Thickness)return false;
            mapped=new PaperSheet.FoldVertex(target,point);return true;
        }
        private PaperInsideReverse PocketAt(PaperSheet.FoldVertex firstVertex,Vector2 other,out FoldStep line,bool packet=false)
        {
            var delta=other-firstVertex.point;var n=new Vector2(-delta.y,delta.x).normalized;
            line=new FoldStep(CreaseFamily.Rising,0,1){anchor=firstVertex.anchor,flap=firstVertex.anchor+1,singlePanel=true,
                creaseNormal=n,creaseOffset=Vector2.Dot(n,firstVertex.point),cornerPull=true,insideReverse=true,packetReverse=scope==Ply.Packet || packet,compactLayers=true,
                pullTip=firstVertex.point,pullStop=other,reverseCreaseAngle=foldDirection==FoldDirection.Opposite?115:65};
            var result=ResolvePocket(line);
            if(result!=null){line=result.Step;return result;}
            line.pullTip=other;line.pullStop=firstVertex.point;
            result=ResolvePocket(line);if(result!=null)line=result.Step;return result;
        }
        private PaperInsideReverse ResolvePocket(FoldStep line)
        {
            var direct=PaperInsideReverse.Create(sheet,line);
            if(direct!=null && direct.Pose(0))return direct;
            if(!line.packetReverse)return null;
            // A visible point on a wrapped packet can belong to several material
            // charts. Resolve the second handle on its actual incident panels;
            // extrapolating the first panel's chart invents a different spine.
            var basis=sheet.Faces[line.anchor];var tip=basis.Original(line.pullTip);
            var world=basis.World(line.pullStop,0);
            foreach(var face in sheet.Faces){
                var local=face.frame.inverse.MultiplyPoint3x4(world);
                var point=new Vector2(local.x,local.z);
                if(Mathf.Abs(local.y)>.002f || !face.Contains(point))continue;
                var stop=face.Original(point);var candidate=line;
                candidate.packetMaterialHandles=true;
                candidate.packetTipMaterial=new Vector2(tip.x,tip.z);
                candidate.packetStopMaterial=new Vector2(stop.x,stop.z);
                var plan=PaperInsideReverse.Create(sheet,candidate);
                if(plan!=null && plan.Pose(0))return plan;
            }
            return null;
        }
        private void SelectAxis(FoldStep line)
        {
            selected=line;sheet.MarkCrease(line,hinge);
            if(first<0 || second<0)return;
            for(int i=hinge.Count-1;i>=0;i--){
                var g=hinge[i];var inverse=g.face.frame.inverse;
                var va=vertices[first];var vb=vertices[second];
                var pa=inverse.MultiplyPoint3x4(sheet.Faces[va.anchor].World(va.point,0));
                var pb=inverse.MultiplyPoint3x4(sheet.Faces[vb.anchor].World(vb.point,0));
                var a=new Vector2(pa.x,pa.z);var b=new Vector2(pb.x,pb.z);
                var direction=(b-a).normalized;float length=Vector2.Distance(a,b);float u=Vector2.Dot(g.a-a,direction),v=Vector2.Dot(g.b-a,direction);
                float lo=Mathf.Max(0,Mathf.Min(u,v)),hi=Mathf.Min(length,Mathf.Max(u,v));
                if(hi<=lo){hinge.RemoveAt(i);continue;}
                g.a=a+lo*direction;g.b=a+hi*direction;hinge[i]=g;
            }
        }
        private bool NearEnd(int hit,int end)
        {
            if(end<0)return false;
            var a=vertices[hit];var b=vertices[end];
            if(FourPointMode || (selected.HasValue && selected.Value.insideReverse))
                return (sheet.Faces[a.anchor].Original(a.point)-sheet.Faces[b.anchor].Original(b.point)).sqrMagnitude<.000001f;
            return PaperSheet.SameFrame(sheet.Faces[a.anchor],sheet.Faces[b.anchor]) && (a.point-b.point).sqrMagnitude<.000001f;
        }
        private void BeginPull(int end,Vector2 pos)
        {
            if(selected.HasValue && selected.Value.customFlanks && end==second){
                SwapPullEnds();end=first;
                if(phase!=Phase.PickSide || !selected.HasValue)return;
            }
            var tip=vertices[end].point;var stop=vertices[end==first?second:first].point;
            var step=selected.Value;
            if(step.customFlanks){tip=step.pullTip;stop=step.pullStop;}
            if(scope==Ply.Stack && !step.customFlanks && !step.insideReverse){
                var handle=vertices[end];var other=vertices[end==first?second:first];var basis=sheet.Faces[handle.anchor];
                var local=basis.frame.inverse.MultiplyPoint3x4(sheet.Faces[other.anchor].World(other.point,0));
                if(Mathf.Abs(local.y)<.04f){
                    var pocket=PocketAt(handle,new Vector2(local.x,local.z),out var autoStep,true);
                    if(pocket==null)pocket=PocketAt(handle,new Vector2(local.x,local.z),out autoStep);
                    if(pocket!=null){step=autoStep;selected=step;tip=handle.point;}
                }
            }
            if(step.insideReverse){
                if((tip-step.pullTip).sqrMagnitude>.0001f){notice="종이 가장자리의 꼭짓점을 잡으세요.";return;}
                step.mirror=MirrorAxis.Off;step.compactLayers=true;
                reverse=PaperInsideReverse.Create(sheet,step);
                if(!(reverse!=null?reverse.Pose(0):pair!=null && pair.Pose(0))){
                    reverse=null;pair=null;notice="이 모서리에서 안쪽 접기를 시작할 수 없습니다.";return;
                }
                candidate=turn=null;phase=Phase.PullCorner;angle=0;notice=null;Rebuild();StartPullDrag(pos);return;
            }
            int target=step.anchor;
            var face=sheet.Faces[target];
            if(!face.Contains(tip) || (!step.customFlanks && !sheet.ContainsPanelPoint(face,stop))){
                notice="선택한 면의 가장자리 끝점을 잡으세요.";return;
            }
            step=PullScope(step);
            if(scope==Ply.Stack)step.flexible=true;
            step.cornerPull=true;step.closeFlat=true;
            if(!step.customFlanks){step.pullTip=tip;step.pullStop=stop;}
            {
                pull=PaperCornerPull.Create(sheet,step);
                if(pull==null){
                    step.flexible=true;
                    step=PullScope(step);
                    pull=PaperCornerPull.Create(sheet,step);
                }
            }
            if(pull==null && pair==null){notice="종이 가장자리의 끝점을 잡고 면 안쪽 점을 향해 당기세요.";return;}
            if(pair!=null)pair.Pose(0);else pull.Pose(0);candidate=turn=null;phase=Phase.PullCorner;angle=0;notice=null;Rebuild();StartPullDrag(pos);
        }
        private void TickVertexDrag(Mouse mouse,Vector2 pos)
        {
            if(gesture==PointerGesture.Opening)TickOpening(mouse,pos);
            else if(gesture==PointerGesture.Pull)TickPull(mouse,pos);
        }
        private void BeginVertexDrag(int index,Vector2 pressed,Vector2 pointer)
        {
            if(phase==Phase.ReviewFourPoints)return;
            int end=NearEnd(index,first)?first:NearEnd(index,second)?second:-1;
            bool hasFold=selected.HasValue && end>=0;
            if(hasFold && selected.Value.packetReverse){BeginPull(end,pressed);return;}
            float foldScore=float.PositiveInfinity;
            if(hasFold){
                var step=selected.Value;var face=sheet.Faces[step.anchor];
                var tip=vertices[end].point;var stop=vertices[end==first?second:first].point;
                var direction=ProjectPaperPoint(face.World(stop,(face.layer+.5f)*PaperSheet.Thickness))-
                    ProjectPaperPoint(face.World(tip,(face.layer+.5f)*PaperSheet.Thickness));
                var delta=pointer-pressed;
                if(direction.sqrMagnitude>4){
                    direction.Normalize();float along=Vector2.Dot(delta,direction);
                    // Pulling toward the other selected point describes a new tip fold.
                    // A better matching existing hinge can instead follow the hand.
                    if(along>0)foldScore=(delta-direction*along).sqrMagnitude;
                }
            }
            // Explicit four-point folds keep their authored constraints.
            if((!hasFold || !selected.Value.customFlanks) &&
               BeginOpening(index,pressed,pointer,foldScore))return;
            if(hasFold)BeginPull(end,pressed);
        }
        private Vector2 ProjectPaperPoint(Vector3 world)
        {
            var p=camera.WorldToViewportPoint(table.position+world);
            return new Vector2(viewport.x+p.x*viewport.width,viewport.y+(1-p.y)*viewport.height);
        }
        private Vector2 OpeningScreenPoint(PaperSheet.Turn plan,float degrees)
        {
            var world=plan.Rotation(degrees).MultiplyPoint3x4(openingHandle);
            return ProjectPaperPoint(world);
        }
        private bool BeginOpening(int index,Vector2 pressed,Vector2 pointer,float foldScore=float.PositiveInfinity)
        {
            if(index<0 || index>=vertices.Count)return false;
            var handle=vertices[index];
            sheet.OpeningTurns(handle,openingTurns,scope==Ply.Stack,true,true);
            if(openingTurns.Count==0){
                notice="접힘선 밖의 꼭짓점을 잡으세요. 반대쪽을 접으려면 종이를 뒤집어 보이는 면을 잡으세요.";return false;
            }
            var face=sheet.Faces[handle.anchor];
            openingHandle=face.World(handle.point,(face.layer+.5f)*PaperSheet.Thickness);
            PaperSheet.Turn best=null;float bestScore=float.PositiveInfinity;
            foreach(var plan in openingTurns){
                var offset=OpeningScreenPoint(plan,0)-pressed;var target=pointer+offset;
                // Choose from nearby motion, not the opposite end of the projected arc.
                float min=plan.Constrain(-45),max=plan.Constrain(45);
                for(float degrees=min;degrees<=max;degrees+=1f){
                    float score=(OpeningScreenPoint(plan,degrees)-target).sqrMagnitude+Mathf.Abs(degrees)*.02f;
                    if(score>=bestScore)continue;bestScore=score;best=plan;
                }
            }
            if(best==null || bestScore+4f>=foldScore)return false;
            candidate=null;pull=null;pair=null;reverse=null;turn=best;selected=best.step;
            openingMin=best.Constrain(-180);openingMax=best.Constrain(180);
            openingOffset=OpeningScreenPoint(best,0)-pressed;
            hinge.Clear();sheet.MarkCrease(best.step,hinge);
            phase=Phase.Opening;gesture=PointerGesture.Opening;angle=0;
            notice=scope==Ply.Stack?"연결된 겹이 손을 따라 움직입니다.":"선택한 면이 손을 따라 움직입니다.";
            Rebuild();return true;
        }
        private void TickOpening(Mouse mouse,Vector2 pointer)
        {
            var target=pointer+openingOffset;float best=angle;
            float score=(OpeningScreenPoint(turn,angle)-target).sqrMagnitude;
            // Prefer the nearby branch of the projected arc to avoid a 180-degree jump.
            float min=Mathf.Max(openingMin,angle-25),max=Mathf.Min(openingMax,angle+25);
            for(float sample=min;sample<=max;sample+=1f){
                float error=(OpeningScreenPoint(turn,sample)-target).sqrMagnitude+Mathf.Abs(sample-angle)*.02f;
                if(error>=score)continue;score=error;best=sample;
            }
            SetAngle(best);
            if(!mouse.leftButton.isPressed)FinishInteraction();
        }
        private void StartPullDrag(Vector2 pos)
        {
            var step=pair!=null?pair.Step:reverse!=null?reverse.Step:pull.Step;
            var f=sheet.Faces[step.anchor];float h=(f.layer+.5f)*PaperSheet.Thickness;
            var a=camera.WorldToViewportPoint(table.position+(reverse!=null && reverse.UsesPacket?reverse.TipWorld:pull!=null && step.flexible?pull.Tip:f.World(step.pullTip,h)));
            var b=camera.WorldToViewportPoint(table.position+(reverse!=null && reverse.UsesPacket?reverse.StopWorld:pull!=null && step.flexible?pull.Stop:f.World(step.pullStop,h)));
            pullVector=new Vector2((b.x-a.x)*viewport.width,(a.y-b.y)*viewport.height);
            if(pullVector.sqrMagnitude<4){notice="종이를 조금 회전해 두 점이 떨어져 보이게 하세요.";return;}
            float progress=Reversing?angle/180f:Mathf.Sign(angle)*InputSign*(1-Mathf.Cos(Mathf.Abs(angle)*Mathf.Deg2Rad));
            pullStart=pos-pullVector*progress;
            gesture=PointerGesture.Pull;
        }
        private void ReverseFoldDirection()
        {
            if(!CanReverseDirection)return;
            if(reverse!=null && reverse.UsesPacket){
                var changed=reverse.Step;
                changed.reverseCreaseAngle=180-(changed.reverseCreaseAngle==0?65:changed.reverseCreaseAngle);
                var alternative=PaperInsideReverse.Create(sheet,changed);
                if(alternative==null || !alternative.Pose(0)){
                    notice="이 방향은 연결된 면과 함께 접을 수 없습니다.";return;
                }
                // A changed crease is a new preview, never a jump across the paper.
                reverse=alternative;selected=alternative.Step;angle=0;keyAdjustment=false;sliderEditing=false;
                foldDirection=changed.reverseCreaseAngle>90?FoldDirection.Opposite:FoldDirection.Normal;
                hinge.Clear();hinge.AddRange(alternative.Creases);
                notice="끝부분을 반대 방향으로 꺾습니다. 드래그하거나 슬라이더를 움직이세요.";
                Rebuild();return;
            }
            foldDirection=foldDirection==FoldDirection.Normal?FoldDirection.Opposite:FoldDirection.Normal;
            keyAdjustment=false;
            if(Adjusting){
                // SetAngle sweeps through zero and retains its existing contact checks.
                // Reanchor an active drag so the next pointer sample cannot undo the switch.
                SetAngle(-angle);
                var mouse=Mouse.current;
                if(mouse!=null){
                    var raw=mouse.position.ReadValue();var pos=new Vector2(raw.x,Screen.height-raw.y)/scale;
                    if(gesture==PointerGesture.Angle){dragY=pos.y;dragStart=angle;}
                    else if(gesture==PointerGesture.Pull)StartPullDrag(pos);
                }
            }else notice=foldDirection==FoldDirection.Opposite?"선택한 점은 유지됩니다. 반대 방향으로 접으세요.":null;
            Rebuild();
        }
        private FoldStep SelectedPly(FoldStep line)
        {
            line.materialPartition=true;
            // The point picker already owns a concrete ply; never promote it by height.
            line.compactLayers=true;line.flap=scope==Ply.Stack?0:line.anchor+1;line.singlePanel=scope!=Ply.Stack;line.connectedPanel=scope!=Ply.Stack;line.separateFoldSides=scope!=Ply.Stack;line.continuousHinge=true;line.neutralContacts=true;return line;
        }
        private void TickBoxSelection(Mouse mouse,Vector2 pos)
        {
            boxEnd=new Vector2(Mathf.Clamp(pos.x,viewport.xMin,viewport.xMax),Mathf.Clamp(pos.y,viewport.yMin,viewport.yMax));
            if(mouse.leftButton.isPressed)return;
            gesture=PointerGesture.Idle;SelectBox(pressPos,boxEnd);
        }
        private static Rect BoxRect(Vector2 a,Vector2 b)=>Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        private void DrawBoxSelection()
        {
            if(gesture!=PointerGesture.BoxSelect)return;
            var r=BoxRect(pressPos,boxEnd);Fill(r,new Color(.39f,.65f,.58f,.10f));var ink=new Color(.25f,.48f,.43f,.8f);
            Fill(new Rect(r.x,r.y,r.width,1.5f),ink);Fill(new Rect(r.x,r.yMax-1.5f,r.width,1.5f),ink);
            Fill(new Rect(r.x,r.y,1.5f,r.height),ink);Fill(new Rect(r.xMax-1.5f,r.y,1.5f,r.height),ink);
        }
        private void SelectBox(Vector2 start,Vector2 end)
        {
            if(first<0 || second<0 || FourPointMode)return;
            if(!TrySelectionAxis(vertices[first].anchor,out var line)){
                notice="두 점이 서로 다른 각도의 면에 있습니다. 같은 접는 축을 먼저 정하세요.";return;
            }
            var basis=sheet.Faces[line.anchor];var a=SurfaceScreen(sheet.Faces[vertices[first].anchor],vertices[first].point);
            var b=SurfaceScreen(sheet.Faces[vertices[second].anchor],vertices[second].point);var delta=b-a;
            float Cross(Vector2 p)=>delta.x*(p.y-a.y)-delta.y*(p.x-a.x);
            float sign=Cross(start);if(Mathf.Abs(sign)<.01f)sign=Cross(end);
            var point=line.Normal*(line.Position/line.Normal.sqrMagnitude);
            float positive=Cross(SurfaceScreen(basis,point+line.Normal));
            if(Mathf.Abs(sign)<.01f || Mathf.Abs(positive)<.01f){notice="선 한쪽에서 박스를 시작하세요.";return;}
            line.side=sign*positive>0?1:-1;
            PaperBoxSelection.Collect(sheet,camera,table.position,viewport,BoxRect(start,end),line,scope==Ply.Stack,boxFaces);
            ResolveRegion(line,true,SelectionOrigin.Box,end,false);
        }

        private void ToggleFacePicking()
        {
            if(!CanChangeLayer || first<0 || second<0)return;
            pickMode=pickMode==PickMode.Face?PickMode.Points:PickMode.Face;Rebuild();
        }
        private void PickFaceAt(Vector2 pos)
        {
            if(first>=0 && second>=0 && leftFlank<0){pickMode=PickMode.Points;ChooseIndividualSide(pos,false);return;}
            CollectLayerHits(ViewUV(pos));
            if(layerHits.Count==0){Browse();pickMode=PickMode.Face;return;}
            var hit=layerHits[0];scope=Ply.Top;SelectLayer(hit);
            pickMode=PickMode.Points;
            notice="보이는 면과 연결된 면을 접습니다. 반대쪽은 종이 뒤집기 [B].";
            Rebuild();
        }
        private bool SelectionExposed()
        {
            return HandleExposed(first) && HandleExposed(second) && HandleExposed(leftFlank) && HandleExposed(rightFlank);
        }
        private bool HandleExposed(int index)=>index<0 || PointVisible(vertices[index],out _);
        private void FlipPaper()
        {
            if(!CanFlipPaper)return;
            SetViewRotation(camera.transform.rotation*Quaternion.AngleAxis(180,Vector3.right));axisView=AxisView.User;
            Browse();notice="종이를 뒤집었습니다. 보이는 면에서 같은 순서로 점을 고르세요.";
        }
        // A deliberate layer selection owns the screen-space handle, even when
        // a different material corner occupies the same position in the stack.
        // Rebind selection only; never weld paper geometry or cross tilted leaves.
        private bool RebindVisibleHandle(int index,int target)
        {
            if(index<0 || target<0 || target>=sheet.Faces.Count)return false;
            var vertex=vertices[index];var face=sheet.Faces[target];
            var local=face.frame.inverse.MultiplyPoint3x4(sheet.Faces[vertex.anchor].World(vertex.point,0));
            var point=new Vector2(local.x,local.z);
            if(Mathf.Abs(local.y)>.04f || !face.Contains(point))return false;
            var mapped=new PaperSheet.FoldVertex(target,point);
            if(!PointVisible(mapped,out _))return false;
            vertices[index]=mapped;return true;
        }
        private Vector2 MapLayerPoint(int source,Vector2 p,int target)
        {
            var from=sheet.Faces[source];var to=sheet.Faces[target];
            var world=from.World(p,(from.layer+.5f)*PaperSheet.Thickness);var local=to.frame.inverse.MultiplyPoint3x4(world);
            return new Vector2(local.x,local.z);
        }
        private void SelectLayer(LayerHit hit)
        {
            edgeSelection=null;edgeMatchSelected=false;
            explicitLayer=true;firstPointPanels.Clear();
            bool adjusting=Adjusting;var previous=selected;if(scope!=Ply.Packet)scope=Ply.Top;
            candidate=turn=null;pull=null;pair=null;reverse=null;hoverSide=hoverFlap=0;angle=0;gesture=PointerGesture.Idle;
            if(!adjusting && rightFlank>=0 && phase==Phase.ReviewFourPoints){
                // Retry on an explicitly chosen layer without discarding the four clicks.
                bool allOnLayer=true;
                foreach(int index in new[]{first,second,leftFlank,rightFlank}){
                    var vertex=vertices[index];var face=sheet.Faces[hit.index];
                    var local=face.frame.inverse.MultiplyPoint3x4(sheet.Faces[vertex.anchor].World(vertex.point,0));
                    if(Mathf.Abs(local.y)>.04f || !face.Contains(new Vector2(local.x,local.z))){allOnLayer=false;break;}
                }
                if(allOnLayer)foreach(int index in new[]{first,second,leftFlank,rightFlank})RebindVisibleHandle(index,hit.index);
                PinLayer(hit.index,hit.point);
                if(!allOnLayer || !CompleteFourPoints())ReviewFourPoints();
                Rebuild();return;
            }
            if(!adjusting && leftFlank>=0 && rightFlank<0){
                // The fourth handle may be on another folded leaf. Keep the
                // three authored material points instead of reassigning them
                // to the newly visible layer at the same screen positions.
                PinLayer(hit.index,hit.point);hinge.Clear();selected=null;
                ShowThreePoints();
                notice="선택한 세 점은 유지됩니다. 이 겹에서 네 번째 점을 고르세요.";
                Rebuild();return;
            }
            if(first>=0 && second>=0 && leftFlank<0 && scope!=Ply.Packet){
                PinLayer(hit.index,hit.point);hinge.Clear();selected=null;
                if(TrySelectionAxis(hit.index,out var axis)){
                    RebindVisibleHandle(first,hit.index);RebindVisibleHandle(second,hit.index);
                    axis.contact=hit.point;axis.side=axis.Distance(hit.point)>=0?1:-1;
                    SelectAxis(axis);phase=Phase.PickSide;
                }else{phase=Phase.PickSide;notice="선택한 선은 이 면과 다른 높이에 있습니다. 점 추가 [F]로 양옆 접힘을 정하세요.";}
                Rebuild();return;
            }
            bool mapped=true;
            foreach(int index in new[]{first,second,leftFlank,rightFlank}){
                if(index<0)continue;var vertex=vertices[index];var p=MapLayerPoint(vertex.anchor,vertex.point,hit.index);
                if(!sheet.Faces[hit.index].Contains(p)){mapped=false;break;}
                vertices[index]=new PaperSheet.FoldVertex(hit.index,p);
            }
            PinLayer(hit.index,hit.point);hinge.Clear();selected=null;
            if(scope==Ply.Packet && mapped && !adjusting && first>=0 && second>=0 && !FourPointMode){SelectPair();return;}
            if(!mapped || (previous.HasValue && previous.Value.customFlanks)){
                first=second=leftFlank=rightFlank=-1;phase=Phase.FirstPoint;
            }else if(previous.HasValue){
                var line=previous.Value;var n=line.Normal;var p=n*(line.Position/n.sqrMagnitude);
                var a=MapLayerPoint(line.anchor,p,hit.index);var b=MapLayerPoint(line.anchor,p+new Vector2(-n.y,n.x).normalized,hit.index);
                var delta=b-a;
                if(delta.sqrMagnitude>.0001f){
                    var normal=new Vector2(delta.y,-delta.x).normalized;
                    line.anchor=hit.index;line.flap=hit.index+1;line.singlePanel=true;line.connectedPanel=true;line.separateFoldSides=true;line.mirror=MirrorAxis.Off;
                    line.cornerPull=line.insideReverse=line.customFlanks=line.packetReverse=line.packetMaterialHandles=false;line.creaseNormal=normal;line.creaseOffset=Vector2.Dot(normal,a);
                    line.side=line.Distance(hit.point)>=0?1:-1;line.contact=hit.point;
                    SelectAxis(line);phase=Phase.PickSide;
                    if(adjusting){turn=sheet.Plan(line);if(turn!=null && !turn.MovesOtherPlyAt(hit.point))phase=Phase.AdjustAngle;else turn=null;}
                }else{first=second=leftFlank=rightFlank=-1;phase=Phase.FirstPoint;}
            }else phase=first>=0?Phase.SecondPoint:Phase.FirstPoint;
            PruneSurfacePoints();
            notice="클릭 위치의 "+(layerRank+1)+" / "+layerCount+"번째 겹을 선택했습니다. 반대쪽: 종이 뒤집기 [B]";Rebuild();
        }
        private void ChoosePly(Ply value)
        {
            if(!CanChangeLayer)return;
            if(value==Ply.Bottom){FlipPaper();return;}
            if(value==Ply.Top && second<0){scope=Ply.Top;return;}
            bool faceMode=pickMode==PickMode.Face;
            scope=value;explicitLayer=false;
            hoverSide=hoverFlap=0;candidate=turn=null;notice=null;
            if(Adjusting)phase=Phase.PickSide;
            if(value==Ply.Stack){pinnedFace=-1;pickMode=PickMode.Points;}
            else pickMode=PickMode.Points;
            if(first>=0 && second>=0 && !FourPointMode){
                if(value==Ply.Top)pinnedFace=vertices[first].anchor;
                SelectPair();
            }else if(phase==Phase.ReviewFourPoints){if(!CompleteFourPoints())ReviewFourPoints();}
            else if(selected.HasValue && !selected.Value.customFlanks)SelectAxis(SelectedPly(selected.Value));
            if(faceMode)pickMode=PickMode.Face;
            Rebuild();
        }
        private void Apply(){if(!owner.ApplyFold(sheet))notice=owner.Session.Research.LastError;}
        private void Layout()
        {
            scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            Rect area=new Rect(36,105,Screen.width/scale-414,Screen.height/scale-245);
            float aspect=(float)target.width/target.height;
            float w=Mathf.Min(area.width,area.height*aspect);
            Vector2 size=new Vector2(w,w/aspect);
            viewport=new Rect(area.center-size*.5f,size);
        }

        public void Draw(float width,float height,Font font)
        {
            if(title==null){
                title=new GUIStyle(GUI.skin.label){font=font,fontSize=32,fontStyle=FontStyle.Bold};
                body=new GUIStyle(GUI.skin.label){font=font,fontSize=21,wordWrap=true};
                caption=new GUIStyle(body){fontSize=16};action=new GUIStyle(body){alignment=TextAnchor.MiddleCenter};
                title.normal.textColor=body.normal.textColor=action.normal.textColor=Ink;caption.normal.textColor=Muted;
            }
            if(statValue==null)statValue=new GUIStyle(caption){alignment=TextAnchor.MiddleRight};
            Layout();
            Fill(new Rect(0,0,width,height),new Color(.975f,.975f,.955f));
            GUI.Label(new Rect(42,32,800,54),"종이접기 작업대",title);
            GUI.Label(new Rect(42,79,290,28),"용지  ·  "+(sheet.Format==PaperSize.Square?"정사각형":sheet.Format.ToString()),caption);
            if(Button(new Rect(845,37,48,40),"−",false,zoom>.401f))SetZoom(zoom/1.25f,new Vector2(.5f,.5f));
            if(Button(new Rect(899,37,90,40),"맞춤",false,true))FrameView(false);
            if(Button(new Rect(995,37,48,40),"+",false,zoom<3.999f))SetZoom(zoom*1.25f,new Vector2(.5f,.5f));
            GUI.DrawTexture(viewport,target,ScaleMode.StretchToFill);
            GUI.Label(new Rect(viewport.x+12,viewport.y+8,100,26),AxisLabels[(int)axisView],caption);
            GUI.Label(new Rect(viewport.x+114,viewport.y+8,100,26),camera.orthographic?"직교":"원근",caption);
            DrawAlignmentGuides();
            DrawBoxSelection();
            if(leftFlank>=0 || phase==Phase.LeftFlank)DrawHandleNumbers();
            float x=width-354f,w=width-x-36;
            if(phase==Phase.PreparingClearance){
                GUI.Label(new Rect(x,286,w,90),notice,body);
                if(Button(new Rect(x,386,w,42),"1·2 바꾸기 [T]",false,CanSwapPullEnds))SwapPullEnds();
                if(Button(new Rect(width-184,35,148,42),"닫기  [Esc]",false,true))owner.CloseFolding();
                return;
            }
            float sw=(w-6)/2;
            bool canScope=CanChangeLayer && !FourPointMode && edgeAlignState==EdgeAlignState.Off;
            if(Button(new Rect(x,105,sw,40),"보이는 면 [1]",scope==Ply.Top,canScope))ChoosePly(Ply.Top);
            if(Button(new Rect(x+sw+6,105,sw,40),"전체 겹 [2]",scope==Ply.Stack,canScope && second>=0))ChoosePly(Ply.Stack);
            if(Button(new Rect(430,37,205,40),"변 맞추기 [M]",edgeAlignState!=EdgeAlignState.Off,CanStartAlignment))ToggleEdgeAlignment();
            GUI.Label(new Rect(650,37,185,40),edgeAlignState!=EdgeAlignState.Off?"변 → 기준선 맞춤":rightFlank>=0?"4점 접기":leftFlank>=0?"3점 · 한 점 더":second>=0?"2점 접기":"꼭짓점 드래그",caption);
            if(Button(new Rect(x,156,w,40),"종이 뒤집기 [B]",false,CanFlipPaper))FlipPaper();
            if(Button(new Rect(x,205,sw,36),"면 선택 [V]",pickMode==PickMode.Face,CanChangeLayer && second>=0 && edgeAlignState==EdgeAlignState.Off))ToggleFacePicking();
            if(Button(new Rect(x+sw+6,205,sw,36),"방향 반전 [R]",foldDirection==FoldDirection.Opposite,CanReverseDirection))ReverseFoldDirection();
            GUI.Label(new Rect(x,246,w,28),scope==Ply.Stack?"가려진 아랫겹도 선택에 포함합니다.":pickMode==PickMode.Face?"개별 면 / 박스 선택 모드":selectionOrigin==SelectionOrigin.Individual?"개별 면을 선택했습니다.":selectionOrigin==SelectionOrigin.Box?"박스로 고른 면을 함께 접습니다.":"보이는 면 전체를 함께 고릅니다.",caption);
            string help=edgeAlignState==EdgeAlignState.Source?"1. 옮길 바깥 변을 클릭하세요.\n점 사이의 선 전체를 선택합니다.":
                edgeAlignState==EdgeAlignState.Target?"2. 맞출 중앙선이나 변을 클릭하세요.\nEsc: 취소 · Backspace: 변 다시 선택":
                edgeAlignState==EdgeAlignState.Preview?"초록색 면이 접힙니다.\nC: 기준선에 밀착 맞추기\n드래그: 각도 조절 · Esc: 취소":pickMode==PickMode.Face?"클릭: 면 하나 · 드래그: 박스 선택\n선을 넘으면 드래그 시작 쪽 선택\n놓고 다시 드래그: 선택 영역 접기":sheet.Count==sheet.FoldLimit?"기체를 적용하거나 한 단계 되돌리세요.":
                surfaceChoice!=null?"노란색: 면 선택 완료\n접기 제한 이유는 아래에 표시됩니다.\n다른 쪽 클릭: 선택 변경 · F: 점 추가":
                phase==Phase.Opening?"움직이는 방향으로 접거나 벌립니다. 놓으면 적용, Esc: 취소.":
                phase==Phase.FirstPoint?(edgeCandidate!=null?"강조된 변을 잡아 가운데로 끌면 주머니가 벌어지고 눌러 펴집니다.":"점을 찍거나 변에 마우스를 올리세요. 주머니를 펼칠 수 있는 변은 강조됩니다."):
                phase==Phase.SecondPoint?"다른 면의 점도 선택할 수 있습니다. 두 번째 점을 찍어 선을 만드세요.":
                phase==Phase.LeftFlank?"꽃잎: 양옆 점을 추가합니다.\n변 맞추기: 목표 선 위의 두 점을 추가합니다.":
                phase==Phase.RightFlank?"꽃잎: 반대쪽 점을 선택하세요.\n변 맞추기: 목표 선 위의 다른 점을 선택하세요.":
                phase==Phase.ReviewFourPoints?"네 점 선택됨 · 접기 조합 확인\n1 이동 · 2 기준 · 3·4 양옆\n새 점 클릭: 4번 교체":
                phase==Phase.PickSide?(rightFlank>=0?(edgeMatchSelected?"변 1–2 → 목표 선 3–4\n면 클릭 → C: 밀착 맞추기\n면 드래그: 각도 조절":AssistedClearance?"면을 드래그하면 겹 정리와 접기가 이어집니다. 반대로 움직이면 되돌아갑니다.":"1 또는 2 끝점을 잡아 다른 끝점 쪽으로 드래그하세요. 잡은 쪽이 이동점이 됩니다."):"선 한쪽 클릭: 보이는 면 전체 선택\n드래그: 접기 · V: 개별/박스 선택") :
                phase==Phase.PullCorner?"끝점을 끌고 놓으면 그 모양으로 접힙니다." :
                turn!=null && turn.step.selectedFaces!=null && turn.step.selectedFaces.Length>0?"초록색: 실제 접기 영역\n드래그: 각도 · V: 다시 선택\n연결된 면도 함께 움직입니다.":
                "드래그하거나 슬라이더로 각도를 맞추고 놓으세요.";
            GUI.Label(new Rect(x,286,w,90),help,body);
            if(Adjusting){
                bool pocket=pull!=null && (pull.UsesCoupledHinges||pull.UsesExactSquash);
                string value=pull!=null && pull.UsesClearanceSequence?"겹 정리 · 접기   "+Mathf.RoundToInt(Mathf.Abs(angle)/1.8f)+"%":pull!=null && pull.UsesRadialGather?"모아 접기   "+Mathf.RoundToInt(Mathf.Abs(angle)/1.8f)+"%":pull!=null && pull.UsesPetalFold?"들어 올려 접기   "+Mathf.RoundToInt(Mathf.Abs(angle)/1.8f)+"%":pull!=null && pull.UsesEdgeMatch?"가장자리 맞추기   "+Mathf.RoundToInt(angle)+"°":pocket?(Mathf.Abs(angle)<=90?"주머니 벌리기   "+Mathf.RoundToInt(Mathf.Abs(angle)/.9f):"눌러 펴기   "+Mathf.RoundToInt((Mathf.Abs(angle)-90)/.9f))+"%":Reversing?"끝부분 되접기   "+Mathf.RoundToInt(angle/1.8f)+"%":
                    (phase==Phase.Opening?"움직인 각도   ":"접는 각도   ")+Mathf.RoundToInt(angle)+"°";
                GUI.Label(new Rect(x,426,w,32),value,body);
                float limit=pull!=null?pull.AngleLimit:pair!=null && pair.Step.cornerPull && !Reversing?
                    PaperCornerPull.LimitFor(pair.Step):180f;
                var sliderRect=new Rect(x,466,w,24);var ev=Event.current;
                if(ev.rawType==EventType.MouseDown && sliderRect.Contains(ev.mousePosition))sliderEditing=true;
                float slider=AssistedClearance?InputSign*GUI.HorizontalSlider(sliderRect,Mathf.Abs(angle),0,180):GUI.HorizontalSlider(sliderRect,angle,Reversing?0:-limit,limit);
                if(Mathf.Abs(slider-angle)>.1f)SetAngle(slider);
                if(sliderEditing && ev.rawType==EventType.MouseUp){sliderEditing=false;FinishInteraction();}
                if(!Reversing)for(int i=0;i<AnglePresets.Length;i++)
                    if(Button(new Rect(x+i*w/4,497,w/4-3,36),AssistedClearance?ClearanceLabels[i]:pocket?PocketPresetLabels[i]:PresetLabels[i],Mathf.Abs(angle-(AssistedClearance?InputSign*ClearancePresets[i]:AnglePresets[i]))<.5f,true)){
                        SetAngle(AssistedClearance?InputSign*ClearancePresets[i]:AnglePresets[i]);FinishInteraction();break;
                    }
                if(!Reversing && Button(new Rect(x,544,w,38),edgeAlignState==EdgeAlignState.Preview?"기준선에 맞추기  [C]":"밀착 접기  [C]",false,true))CloseToContact();
            }else if(first>=0){
                if(phase==Phase.PickSide && leftFlank<0){
                    if(Button(new Rect(x,386,w*.54f,42),"양옆 점 찾기",false,scope!=Ply.Stack && scope!=Ply.Packet))SuggestFlanks();
                    if(Button(new Rect(x+w*.56f,386,w*.44f,42),"점 추가 [F]",false,true))AddMorePoints();
                }else if(CanSwapPullEnds){
                    if(Button(new Rect(x,386,w*.52f,42),"1·2 바꾸기 [T]",false,true))SwapPullEnds();
                    if(Button(new Rect(x+w*.54f,386,w*.46f,42),"점 취소",false,true))RemoveLastPoint();
                }else if(Button(new Rect(x,386,w,42),"마지막 점 취소  [Backspace]",false,true))RemoveLastPoint();
            }else if(!PickingAlignment && sheet.Count>0 && Button(new Rect(x,386,w,42),"마지막 접기 각도 수정  [E]",false,true))EditLast();
            if(!Adjusting){
                GUI.Label(new Rect(x,448,w,28),"기체 성능 · 기본 기체 100%",caption);
                for(int i=0;i<StatNames.Length;i++){
                    float y=482+i*28;
                    GUI.Label(new Rect(x,y,w*.66f,26),StatNames[i],caption);
                    GUI.Label(new Rect(x+w*.66f,y,w*.34f,26),statValues[i],statValue);
                }
            }
            if(Button(new Rect(x,height-248,w,42),"한 단계 되돌리기  [Z]",false,!Adjusting && sheet.Count>0)){sheet.Undo();Browse();}
            if(Button(new Rect(x,height-195,w,42),"새 종이",false,!Adjusting)){NewPaper();}
            if(Button(new Rect(x,height-124,w,52),sheet.Count==0?"기본 기체 사용  [Enter]":"접은 기체 적용  [Enter]",true,phase==Phase.FirstPoint && edgeAlignState==EdgeAlignState.Off))Apply();
            if(Button(new Rect(width-184,35,148,42),"닫기  [Esc]",false,true))owner.CloseFolding();
            GUI.Label(new Rect(40,height-154,width-430,154),"클릭: 점 선택/해제 · 선 한쪽 클릭: 보이는 면 전체 · 놓기: 접기 적용\n우클릭 드래그: 회전 · B: 뒤집기 · 휠: 확대/축소 · 휠 버튼 드래그: 이동\n키패드 1/3/7: 정면/오른쪽/위 · Ctrl: 반대편 · 9: 반대 보기\n2/4/6/8: 회전 · Ctrl: 이동 · Shift+4/6: 화면 기울이기\n5: 원근/직교 · +/−: 확대/축소 · .: 선택 맞춤 · Home: 전체 맞춤\nM: 변 맞추기 · F: 점 추가 · V: 박스/면 선택 · 1/2: 겹 범위\n접기 중 C: 밀착 · R: 방향 반전 · ↑↓: 1° (Shift: 5°)\nBackspace: 점 취소 · Z: 되돌리기 · Enter: 기체 적용 · Esc: 취소/닫기",caption);
            if(notice!=null)GUI.Label(new Rect(x,height-69,w,63),notice,caption);
        }
        private void DrawHandleNumbers()
        {
            if(pointLabel==null)pointLabel=new GUIStyle(caption){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
            for(int slot=0;slot<4;slot++){
                int index=slot==0?first:slot==1?second:slot==2?leftFlank:rightFlank;
                if(index<0 || index>=vertices.Count)continue;
                var vertex=vertices[index];var face=sheet.Faces[vertex.anchor];
                var at=SurfaceScreen(face,vertex.point);if(!viewport.Contains(at))continue;
                float labelWidth=edgeMatchSelected?104:slot<2?76:24;
                var box=new Rect(Mathf.Clamp(at.x+8,viewport.x,viewport.xMax-labelWidth),Mathf.Clamp(at.y-28,viewport.y,viewport.yMax-24),labelWidth,24);
                string label=edgeMatchSelected?(slot==0?"1 공통점":slot==1?"2 가장자리":slot==2?"3 목표선":"4 목표선"):
                    slot==0?"1 이동":slot==1?"2 기준":slot==2?"3":"4";
                Fill(box,new Color(.96f,.98f,.96f,.95f));GUI.Label(box,label,pointLabel);
            }
        }
        private bool Button(Rect rect,string label,bool selectedButton,bool enabled)
        {
            Fill(rect,enabled?(selectedButton?new Color(.58f,.75f,.68f):new Color(.71f,.75f,.71f)):Color.gray);
            Fill(new Rect(rect.x+1,rect.y+1,rect.width-2,rect.height-2),
                selectedButton?new Color(.83f,.91f,.85f):new Color(.97f,.97f,.95f));
            bool previous=GUI.enabled;GUI.enabled=enabled;
            bool pressed=GUI.Button(rect,label,action);GUI.enabled=previous;return pressed;
        }
        private static void Fill(Rect r,Color color)
        {var old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
    }
}
