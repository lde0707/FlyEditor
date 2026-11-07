using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyEditor.Flight
{
    internal sealed class FoldingDesk
    {
        private enum Phase { FirstPoint, SecondPoint, RightFlank, PickSide, AdjustAngle, PullCorner, Opening }
        private enum Ply { Top, Bottom, Stack }
        private enum FoldDirection { Normal, Opposite }
        private FoldDirection foldDirection;
        private int InputSign=>(scope==Ply.Bottom?-1:1)*(foldDirection==FoldDirection.Opposite?-1:1);
        private bool CanReverseDirection=>phase!=Phase.Opening && !Reversing && !(selected.HasValue && selected.Value.insideReverse);
        private static readonly string[] ScopeLabels={"윗면","아랫면","전체 겹"};
        private static readonly int[] AnglePresets={-180,-90,90,180};
        private static readonly string[] PresetLabels={"−180°","−90°","+90°","+180°"};
        private static readonly Color Ink=new Color(.19f,.26f,.26f), Muted=new Color(.39f,.44f,.43f);
        private readonly PaperFlightController owner;
        private readonly Camera camera;
        private readonly Transform table;
        private readonly Mesh mesh;
        private readonly RenderTexture target;
        private readonly FoldMesh builder=new FoldMesh();
        private readonly List<PaperSheet.FoldVertex> vertices=new List<PaperSheet.FoldVertex>();
        private readonly List<PaperSheet.Guide> hinge=new List<PaperSheet.Guide>();
        private PaperSheet.Turn candidate;
        private PaperCornerPull pull;
        private PaperInsideReverse reverse;
        private bool keyAdjustment,sliderEditing;
        private bool Reversing=>reverse!=null || (pair!=null && pair.Step.insideReverse);
        private PaperMirrorFold pair;
        private MirrorAxis mirror=MirrorAxis.Off,lastAxis=MirrorAxis.Horizontal;
        private Vector2 pullStart,pullVector;
        private bool Adjusting=>phase==Phase.AdjustAngle || phase==Phase.PullCorner || phase==Phase.Opening;
        private readonly List<PaperSheet.Turn> openingTurns=new List<PaperSheet.Turn>();
        private Vector3 openingHandle;
        private Vector2 openingOffset;
        private float openingMin,openingMax;
        private int first=-1,second=-1,hoverPoint=-1;
        private bool PickingPoint=>phase==Phase.FirstPoint || phase==Phase.SecondPoint || phase==Phase.RightFlank;
        private enum PointerGesture { Idle, Press, PointPress, SecondPointPress, OpenPress, Opening, Pan, Angle, Pull }
        private PointerGesture gesture;
        private Vector2 pressPos,lastPan;
        private int pressedPoint=-1;
        private bool replaceFirst;
        private int replaceFlank;
        private bool FourPointMode=>leftFlank>=0;
        private bool replaceArmed;
        private int leftFlank=-1,rightFlank=-1;
        private Vector3 panOffset;
        private bool middlePan;
        private PaperSheet sheet;
        private PaperSheet.Turn turn;
        private Phase phase;
        private FoldStep? selected;
        private int hoverSide, hoverFlap;
        private Ply scope;
        private FoldStep? editing;
        private float dragY, dragStart, angle;
        private float pitch,yaw,zoom,scale;
        private Rect viewport;
        private bool dirty=true;
        private static readonly string[] StatNames={"출발 속도","힘 · 속도 유지","하강 속도","상승력","충돌 저항"};
        private readonly string[] statValues=new string[5];
        private IReadOnlyList<PaperSheet.Face> measuredFaces;
        private GUIStyle statValue;
        private string notice,layerLabel;
        private GUIStyle title,body,caption,action;
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
            camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.92f,.94f,.92f);
            target=new RenderTexture(1280,900,24){name="접기 작업대",antiAliasing=4,filterMode=FilterMode.Bilinear};
            camera.targetTexture=target;
        }
        public void Open(FoldStep[] applied)
        {
            sheet=applied==null || applied.Length==0 ? new PaperSheet() : PaperSheet.Replay(applied);pitch=57f;yaw=-18f;zoom=1f;panOffset=Vector3.zero;gesture=PointerGesture.Idle;
            editing=null;scope=Ply.Top;foldDirection=FoldDirection.Normal;
            table.gameObject.SetActive(true);Browse();
        }
        public void Close(){table.gameObject.SetActive(false);}
        public void Dispose()
        {
            camera.targetTexture=null;target.Release();Object.Destroy(target);Object.Destroy(mesh);Object.Destroy(table.gameObject);
        }
        private void Browse()
        {
            phase=Phase.FirstPoint;first=second=hoverPoint=pressedPoint=leftFlank=rightFlank=-1;replaceFirst=false;replaceFlank=0;replaceArmed=false;selected=null;hoverSide=hoverFlap=0;
            gesture=PointerGesture.Idle;candidate=turn=null;pull=null;pair=null;reverse=null;keyAdjustment=sliderEditing=false;hinge.Clear();openingTurns.Clear();notice=null;
            sheet.CollectVertices(vertices);
            if(measuredFaces!=sheet.Faces){
                var profile=PaperFlightProfile.Measure(sheet);measuredFaces=sheet.Faces;
                statValues[0]=Rating(profile.launch);statValues[1]=Rating(profile.momentum);statValues[2]=Rating(profile.descent);
                statValues[3]=Rating(profile.climb);statValues[4]=Rating(profile.impact);
            }
            layerLabel="접기  "+sheet.Count+" / "+sheet.FoldLimit+"    ·    최대 "+sheet.PeakPlies+"겹";
            Rebuild();PositionCamera();
        }
        private static string Rating(float value)=>(value*100f).ToString("0.0")+"%";
        private void PositionCamera()
        {
            var b=sheet.SpatialBounds;
            Vector3 pivot=table.position+b.center+panOffset;
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,0);
            camera.transform.position=pivot-camera.transform.forward*30f;
            camera.orthographicSize=Mathf.Max(3f,b.size.magnitude*.47f)/zoom;
            dirty=true;
        }
        private void Rebuild()
        {
            bool adjust=phase==Phase.AdjustAngle || phase==Phase.Opening;
            builder.Build(mesh,sheet,turn,adjust?angle:0f,vertices,first,second,hoverPoint,phase==Phase.PickSide,hinge,pull,pair,pair!=null?pair.Step.mirror:mirror,scope!=Ply.Bottom,reverse,leftFlank,rightFlank);
            dirty=true;
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
                if(f.Contains(p)){nearest=t;point=p;hit=f;}
            }
            return hit;
        }
        private bool MousePoint(Vector2 uv,FoldStep line,out Vector2 point,out int flap)
        {
            flap=0;var hit=RayHit(uv,out point);
            if(hit==null || !PaperSheet.SameFrame(hit,sheet.Faces[line.anchor]))return false;
            var basis=sheet.Faces[line.anchor];
            for(int i=0;i<sheet.Faces.Count;i++){
                var face=sheet.Faces[i];
                if(!PaperSheet.SamePanel(face,basis) || !face.Contains(point))continue;
                flap=i+1;return true;
            }
            return false;
        }
        private int NearestVertex(Vector2 mouse)
        {
            int hit=-1;
            float best=13f*13f,depth=float.PositiveInfinity;
            for(int i=0;i<vertices.Count;i++){
                var vertex=vertices[i];var face=sheet.Faces[vertex.anchor];
                sheet.PlyRange(face,vertex.point,out int bottom,out int top);
                if(face.layer!=(scope!=Ply.Bottom?top:bottom))continue;
                float h=(Vector3.Dot(camera.transform.forward,face.frame.MultiplyVector(Vector3.up))<0?top+1:bottom)*PaperSheet.Thickness;
                var world=table.position+face.World(vertex.point,h);
                var v=camera.WorldToViewportPoint(world);
                var screen=new Vector2(viewport.x+v.x*viewport.width,viewport.y+(1-v.y)*viewport.height);
                float sq=(mouse-screen).sqrMagnitude;
                if(sq>best+.001f || (Mathf.Abs(sq-best)<.001f && v.z>=depth))continue;
                // Test occlusion at the marker, so a click just outside a corner can still hit it.
                var cover=RayHit(new Vector2(v.x,v.y),out var at);
                if(cover!=null && cover!=face){
                    float coverHeight=(cover.layer+(Vector3.Dot(camera.transform.forward,cover.frame.MultiplyVector(Vector3.up))<0?1:0))*PaperSheet.Thickness;
                    float coverDepth=camera.WorldToViewportPoint(table.position+cover.World(at,coverHeight)).z;
                    if(v.z>coverDepth+.001f)continue;
                }
                hit=i;best=sq;depth=v.z;
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
                            if(scope==Ply.Stack || step.customFlanks){step.singlePanel=false;step.flap=0;}
                            var connected=PaperCornerPull.Create(sheet,step);
                            if(connected!=null && connected.Pose(sample)){pull=connected;angle=sample;continue;}
                        }
                        notice="종이가 맞닿는 지점입니다. 양옆 접힘 위치를 바꿔보세요.";Rebuild();return;
                    }
                    angle=sample;
                }
                notice=pull.Step.flexible?"주변 면이 함께 움직입니다.":null;Rebuild();return;
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
            editing=null;sheet=new PaperSheet();foldDirection=FoldDirection.Normal;Browse();
        }
        public void Tick(float dt)
        {
            Layout();
            var keys=Keyboard.current;
            if(keys!=null && keys.escapeKey.wasPressedThisFrame){
                if(phase!=Phase.FirstPoint)CancelAngle();else owner.CloseFolding();return;
            }
            if(keys!=null){
                if(keys.rKey.wasPressedThisFrame && CanReverseDirection)ReverseFoldDirection();
                if(Adjusting){
                    if(keys.cKey.wasPressedThisFrame && !Reversing){CloseToContact();return;}
                    float step=keys.shiftKey.isPressed?5f:1f;
                    if(gesture==PointerGesture.Idle && keys.upArrowKey.wasPressedThisFrame){SetAngle(angle+step);keyAdjustment=true;}
                    if(gesture==PointerGesture.Idle && keys.downArrowKey.wasPressedThisFrame){SetAngle(angle-step);keyAdjustment=true;}
                    if(keyAdjustment && !keys.upArrowKey.isPressed && !keys.downArrowKey.isPressed)FinishInteraction();
                }else{
                    if(keys.eKey.wasPressedThisFrame && phase==Phase.FirstPoint && sheet.Count>0){EditLast();return;}
                    if(keys.zKey.wasPressedThisFrame && sheet.Undo())Browse();
                    if(keys.tabKey.wasPressedThisFrame)ChoosePly((Ply)(((int)scope+1)%ScopeLabels.Length));
                    if(keys.digit1Key.wasPressedThisFrame)ChoosePly(Ply.Top);
                    if(keys.digit2Key.wasPressedThisFrame)ChoosePly(Ply.Bottom);
                    if(keys.digit3Key.wasPressedThisFrame)ChoosePly(Ply.Stack);
                    if(keys.backspaceKey.wasPressedThisFrame)RemoveLastPoint();
                    if(keys.mKey.wasPressedThisFrame)SetMirror(mirror==MirrorAxis.Off?lastAxis:MirrorAxis.Off);
                }
                if(keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame){
                    if(phase==Phase.FirstPoint)Apply();
                    return;
                }
            }
            var mouse=Mouse.current;
            if(mouse!=null && viewport.width>0){
                Vector2 raw=mouse.position.ReadValue();
                Vector2 pos=new Vector2(raw.x,Screen.height-raw.y)/scale;
                TickPointer(mouse,pos);
            }
            if(dirty){camera.Render();dirty=false;}
        }

        private Vector2 ViewUV(Vector2 pos)=>new Vector2((pos.x-viewport.x)/viewport.width,1f-(pos.y-viewport.y)/viewport.height);
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
        private void TickPointer(Mouse mouse,Vector2 pos)
        {
            bool inside=viewport.Contains(pos);
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
            if(gesture==PointerGesture.Angle){
                float value=dragStart+(dragY-pos.y)*.65f*(Reversing?1:InputSign);
                if(Mathf.Abs(value-angle)>.5f)SetAngle(value);
                if(!mouse.leftButton.isPressed)FinishInteraction();
                return;
            }
            if(gesture==PointerGesture.Press){
                if((pos-pressPos).sqrMagnitude>25){
                    gesture=PointerGesture.Pan;middlePan=false;lastPan=pressPos;PanTo(pos);return;
                }
                if(!mouse.leftButton.isPressed){
                    gesture=PointerGesture.Idle;
                    if(inside && PickingPoint)PickVertex(pos);
                }
                return;
            }
            if(!inside){
                if(!Adjusting && (hoverPoint>=0 || hoverSide!=0)){
                    hoverPoint=-1;hoverSide=hoverFlap=0;candidate=turn=null;Rebuild();
                }
                return;
            }
            if(mouse.rightButton.isPressed){
                var delta=mouse.delta.ReadValue();
                yaw+=delta.x*.34f;pitch=Mathf.Clamp(pitch-delta.y*.24f,-85f,86f);PositionCamera();return;
            }
            bool click=mouse.leftButton.wasPressedThisFrame;
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
            if(click && phase==Phase.PickSide){
                int hit=NearestVertex(pos);
                if(hit>=0){
                    pressedPoint=hit;pressPos=pos;gesture=PointerGesture.PointPress;return;
                }
            }
            if(click && (PickingPoint || RayHit(ViewUV(pos),out _)==null)){
                gesture=PointerGesture.Press;pressPos=pos;return;
            }
            if(Adjusting){if(click && phase==Phase.AdjustAngle)BeginAngleDrag(pos);else if(click)StartPullDrag(pos);return;}
            if(!(click || dirty || mouse.delta.ReadValue()!=Vector2.zero) || sheet.Count>=sheet.FoldLimit)return;
            if(PickingPoint){
                int near=NearestVertex(pos);
                if(near!=hoverPoint){hoverPoint=near;Rebuild();}
            }else{
                if(!selected.HasValue){if(click)notice="점을 두 개 더 선택하면 4점으로 접을 수 있습니다.";return;}
                var line=selected.Value;
                if(line.customFlanks){if(click)BeginFourDrag(pos);return;}
                if(line.insideReverse){
                    if(click)notice="초록 선의 바깥 꼭짓점을 잡고 안쪽 점으로 당기세요.";
                    return;
                }
                float side=MousePoint(ViewUV(pos),line,out var point,out int flap)?line.Distance(point):0f;
                int sign=side>.035f?1:side<-.035f?-1:0;
                bool changed=sign!=hoverSide || flap!=hoverFlap;
                if(changed){
                    hoverSide=sign;hoverFlap=flap;line.side=sign;line.flap=scope==Ply.Stack?0:flap;
                    line.singlePanel=scope!=Ply.Stack;if(flap>0)line.anchor=flap-1;
                    candidate=sign==0?null:sheet.Plan(line);
                }
                var allowed=candidate!=null && !candidate.MovesOtherPlyAt(point)?candidate:null;
                if(changed || turn!=allowed){turn=allowed;Rebuild();}
                if(click && mirror!=MirrorAxis.Off && sign!=0 && flap>0){
                    line.side=sign;line.anchor=flap-1;line.flap=scope==Ply.Stack?0:flap;line.contact=point;line.singlePanel=scope!=Ply.Stack;line.mirror=mirror;
                    pair=PaperMirrorFold.Create(sheet,line);
                    if(pair==null || !pair.Pose(0)){pair=null;notice="이 접힘의 반대쪽에 대응하는 면이 없습니다.";return;}
                    candidate=turn=null;
                    phase=Phase.AdjustAngle;angle=0;notice=null;BeginAngleDrag(pos);Rebuild();return;
                }
                if(click && turn!=null){
                    phase=Phase.AdjustAngle;angle=0;notice=null;BeginAngleDrag(pos);Rebuild();
                }else if(click)notice=candidate!=null?"이쪽은 다른 면과 이어져 있습니다. 자유롭게 움직일 쪽을 고르세요.":
                    "전체 겹 또는 4점 접기를 사용하세요.";
            }
        }
        private void TickPull(Mouse mouse,Vector2 pos)
        {
            float distance=Vector2.Dot(pos-pullStart,pullVector)/pullVector.sqrMagnitude;
            float inward=Mathf.Clamp(distance,Reversing?0f:-2f,Reversing?1f:2f);
            if(Reversing){
                if(inward>.5f && (1-inward)*pullVector.magnitude<5f)inward=1;
                SetAngle(180*inward);
            }else{
                float amount=Mathf.Abs(inward);
                if(amount>1.999f)amount=2f;
                float tilt=Mathf.Acos(1-amount)*Mathf.Rad2Deg;
                SetAngle(InputSign*Mathf.Sign(inward)*tilt);
            }
            if(!mouse.leftButton.isPressed)FinishInteraction();
        }
        private void TickPointPress(Mouse mouse,Vector2 pos,bool inside)
        {
            if((pos-pressPos).sqrMagnitude>25){
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
            if(NearEnd(pressedPoint,first) || NearEnd(pressedPoint,second)){
                replaceFirst=NearEnd(pressedPoint,first);replaceFlank=0;replaceArmed=true;
                notice=replaceFirst?"첫 번째 점을 바꿀 위치를 클릭하세요.":"두 번째 점을 바꿀 위치를 클릭하세요.";
                Rebuild();return;
            }
            if(FourPointMode && (NearEnd(pressedPoint,leftFlank) || NearEnd(pressedPoint,rightFlank))){
                replaceFlank=NearEnd(pressedPoint,leftFlank)?1:2;replaceArmed=true;
                notice="접힘 방향을 바꿀 점을 클릭하세요.";Rebuild();return;
            }
            if(replaceArmed){ReplacePoint(pos);replaceArmed=false;return;}
            if(!FourPointMode){
                leftFlank=pressedPoint;ClearSelectionPreview();ShowThreePoints();return;
            }
            notice="바꿀 점을 먼저 클릭하세요.";Rebuild();
        }
        private void ReplacePoint(Vector2 pos)
        {
            if(FourPointMode && leftFlank>=0 && rightFlank>=0){ReplaceFourPoint(pos);return;}
            int previousFirst=first,previousSecond=second;
            var a=vertices[first];var b=vertices[second];
            if(replaceFirst){first=previousSecond;second=previousFirst;}
            if(!PickVertex(pos)){
                first=previousFirst;second=previousSecond;
                vertices[first]=a;vertices[second]=b;Rebuild();return;
            }
            if(replaceFirst){int replacement=second;second=first;first=replacement;}
            hoverSide=hoverFlap=0;candidate=turn=null;pull=null;pair=null;reverse=null;angle=0;
            Rebuild();
        }
        private void ReplaceFourPoint(Vector2 pos)
        {
            int hit=NearestVertex(pos);
            if(hit<0)return;
            int a=first,b=second,c=leftFlank,d=rightFlank;
            if(replaceFlank==1)leftFlank=hit;
            else if(replaceFlank==2)rightFlank=hit;
            else if(replaceFirst)first=hit;
            else second=hit;
            bool distinct=first!=second && first!=leftFlank && first!=rightFlank &&
                second!=leftFlank && second!=rightFlank && leftFlank!=rightFlank;
            if(distinct && CompleteFourPoints()){candidate=turn=null;pull=null;pair=null;reverse=null;Rebuild();return;}
            first=a;second=b;leftFlank=c;rightFlank=d;
            if(!distinct)notice="서로 다른 네 점을 선택하세요.";
            Rebuild();
        }
        private void BeginAngleDrag(Vector2 pos)
        {gesture=PointerGesture.Angle;dragY=pos.y;dragStart=angle;}
        private bool PickVertex(Vector2 pos)=>PickVertex(NearestVertex(pos));
        private bool PickVertex(int hit)
        {
            if(sheet.Count>=sheet.FoldLimit)return false;
            if(phase==Phase.FirstPoint){
                if(hit<0){notice="종이 위의 점을 클릭하세요.";return false;}
                first=hit;hoverPoint=-1;phase=Phase.SecondPoint;notice=null;Rebuild();return true;
            }
            if(hit<0){notice="표시된 점을 클릭하세요.";return false;}
            if(hit==first){notice="첫 점과 다른 점을 고르세요.";return false;}
            if(phase==Phase.RightFlank)return PickFlank(hit);
            second=hit;SelectPair();return true;
        }
        private void SelectPair()
        {
            ClearSelectionPreview();
            var a=vertices[first];var b=vertices[second];
            if(!PaperSheet.SameFrame(sheet.Faces[a.anchor],sheet.Faces[b.anchor])){
                if(SharedVertex(b,a.anchor,out var mapped))b=mapped;
                else if(SharedVertex(a,b.anchor,out mapped))a=mapped;
            }
            var basis=sheet.Faces[a.anchor];
            var neighbour=sheet.Faces[b.anchor];
            var material=neighbour.Original(b.point);
            var local=basis.material.inverse.MultiplyPoint3x4(material);
            var probe=new Vector2(local.x,local.z);
            var pocket=PocketAt(a,probe,out var line);
            if(pocket!=null){
                if(b.anchor!=a.anchor || (b.point-probe).sqrMagnitude>.000001f){
                    b=new PaperSheet.FoldVertex(a.anchor,probe);
                }
            }else{
                if(!PaperSheet.SameFrame(basis,neighbour) || !sheet.TryAxis(a,b,out line)){
                    phase=Phase.PickSide;AddHandleGuide(first,second);
                    notice="점을 두 개 더 선택하면 4점으로 접을 수 있습니다.";Rebuild();return;
                }
            }
            vertices[first]=a;vertices[second]=b;hoverPoint=-1;
            if(!line.insideReverse)line=SelectedPly(line);
            SelectAxis(line);
            if(pocket!=null)hinge.AddRange(pocket.Creases);
            phase=Phase.PickSide;notice=null;Rebuild();
        }
        private void ClearSelectionPreview()
        {
            selected=null;candidate=turn=null;pull=null;pair=null;reverse=null;hinge.Clear();
            hoverPoint=-1;hoverSide=hoverFlap=0;angle=0;gesture=PointerGesture.Idle;replaceArmed=false;
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
            else if(second>=0){second=-1;phase=Phase.SecondPoint;notice=null;Rebuild();}
            else Browse();
        }
        private Vector2 HandlePoint(int index)
        {
            var vertex=vertices[index];var basis=sheet.Faces[vertices[first].anchor];
            var p=basis.material.inverse.MultiplyPoint3x4(sheet.Faces[vertex.anchor].Original(vertex.point));
            return new Vector2(p.x,p.z);
        }
        private bool PickFlank(int hit)
        {
            if(hit==first || hit==second || hit==leftFlank){notice="서로 다른 네 점을 선택하세요.";return false;}
            rightFlank=hit;
            if(CompleteFourPoints())return true;
            rightFlank=-1;return false;
        }
        private bool CompleteFourPoints()
        {
            var tip=HandlePoint(first);var stop=HandlePoint(second);var n=new Vector2(stop.y-tip.y,tip.x-stop.x).normalized;
            var line=new FoldStep(CreaseFamily.Rising,0,1){anchor=vertices[first].anchor,cornerPull=true,flexible=true,customFlanks=true,closeFlat=true,
                pullTip=tip,pullStop=stop,pullLeft=HandlePoint(leftFlank),pullRight=HandlePoint(rightFlank),
                creaseNormal=n,creaseOffset=Vector2.Dot(n,stop),mirror=mirror};
            var check=line;check.mirror=MirrorAxis.Off;
            if(PaperCornerPull.Create(sheet,check)==null){notice="중심에서 봤을 때 선의 양옆에 있는 점을 선택하세요.";return false;}
            selected=line;hinge.Clear();AddHandleGuide(first,second);AddHandleGuide(second,leftFlank);AddHandleGuide(second,rightFlank);
            phase=Phase.PickSide;notice="움직일 꼭짓점을 끌거나 면을 드래그하세요.";Rebuild();return true;
        }
        private void AddHandleGuide(int from,int to)
        {
            var a=vertices[from];var b=vertices[to];
            var start=sheet.Faces[a.anchor].Original(a.point);var end=sheet.Faces[b.anchor].Original(b.point);
            foreach(var face in sheet.Faces){
                var inverse=face.material.inverse;var u=inverse.MultiplyPoint3x4(start);var v=inverse.MultiplyPoint3x4(end);
                var p=new Vector2(u.x,u.z);var q=new Vector2(v.x,v.z);
                if(PaperSheet.Trim(face,ref p,ref q))hinge.Add(new PaperSheet.Guide{face=face,layer=face.layer,a=p,b=q});
            }
        }
        private void BeginFourDrag(Vector2 pos)
        {
            var step=selected.Value;
            if(mirror!=MirrorAxis.Off)pair=PaperMirrorFold.Create(sheet,step);
            else pull=PaperCornerPull.Create(sheet,step);
            if(pair==null && pull==null){notice="네 점을 다시 선택하세요.";return;}
            if(!(pair!=null?pair.Pose(0):pull.Pose(0))){pair=null;pull=null;return;}
            candidate=turn=null;phase=Phase.AdjustAngle;angle=0;BeginAngleDrag(pos);Rebuild();
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
        private PaperInsideReverse PocketAt(PaperSheet.FoldVertex firstVertex,Vector2 other,out FoldStep line)
        {
            var delta=other-firstVertex.point;var n=new Vector2(-delta.y,delta.x).normalized;
            line=new FoldStep(CreaseFamily.Rising,0,1){anchor=firstVertex.anchor,flap=firstVertex.anchor+1,singlePanel=true,
                creaseNormal=n,creaseOffset=Vector2.Dot(n,firstVertex.point),cornerPull=true,insideReverse=true,
                pullTip=firstVertex.point,pullStop=other};
            var result=PaperInsideReverse.Create(sheet,line);
            if(result!=null)return result;
            line.pullTip=other;line.pullStop=firstVertex.point;
            return PaperInsideReverse.Create(sheet,line);
        }
        private void SelectAxis(FoldStep line)
        {
            selected=line;sheet.MarkCrease(line,hinge);
            if(first<0 || second<0)return;
            var a=vertices[first].point;var b=vertices[second].point;
            var direction=(b-a).normalized;float length=Vector2.Distance(a,b);
            for(int i=hinge.Count-1;i>=0;i--){
                var g=hinge[i];float u=Vector2.Dot(g.a-a,direction),v=Vector2.Dot(g.b-a,direction);
                float lo=Mathf.Max(0,Mathf.Min(u,v)),hi=Mathf.Min(length,Mathf.Max(u,v));
                if(hi<=lo){hinge.RemoveAt(i);continue;}
                g.a=a+lo*direction;g.b=a+hi*direction;hinge[i]=g;
            }
        }
        private bool NearEnd(int hit,int end)
        {
            if(end<0)return false;
            var a=vertices[hit];var b=vertices[end];
            if(selected.HasValue && selected.Value.insideReverse)
                return (sheet.Faces[a.anchor].Original(a.point)-sheet.Faces[b.anchor].Original(b.point)).sqrMagnitude<.000001f;
            return PaperSheet.SameFrame(sheet.Faces[a.anchor],sheet.Faces[b.anchor]) && (a.point-b.point).sqrMagnitude<.000001f;
        }
        private void BeginPull(int end,Vector2 pos)
        {
            var tip=vertices[end].point;var stop=vertices[end==first?second:first].point;
            var step=selected.Value;
            if(step.customFlanks && end!=first){notice="첫 번째로 선택한 꼭짓점을 당기세요.";return;}
            if(step.insideReverse){
                if((tip-step.pullTip).sqrMagnitude>.0001f){notice="종이 가장자리의 꼭짓점을 잡으세요.";return;}
                step.mirror=mirror;
                if(mirror==MirrorAxis.Off)reverse=PaperInsideReverse.Create(sheet,step);
                else pair=PaperMirrorFold.Create(sheet,step);
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
            step.flap=scope==Ply.Stack?0:target+1;step.singlePanel=scope!=Ply.Stack;step.mirror=mirror;
            if(scope==Ply.Stack || step.customFlanks){step.flexible=true;step.singlePanel=false;step.flap=0;}
            step.cornerPull=true;step.closeFlat=true;
            if(!step.customFlanks){step.pullTip=tip;step.pullStop=stop;}
            if(mirror!=MirrorAxis.Off)pair=PaperMirrorFold.Create(sheet,step);
            else{
                pull=PaperCornerPull.Create(sheet,step);
                if(pull==null){
                    step.flexible=true;
                    if(scope==Ply.Stack || step.customFlanks){step.singlePanel=false;step.flap=0;}
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
            int end=NearEnd(index,first)?first:NearEnd(index,second)?second:-1;
            bool hasFold=selected.HasValue && end>=0;
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
            // Explicit four-point and mirrored folds keep their authored constraints.
            if((!hasFold || (!selected.Value.customFlanks && mirror==MirrorAxis.Off)) &&
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
            sheet.OpeningTurns(handle,openingTurns,scope==Ply.Stack);
            if(openingTurns.Count==0){
                notice="접힘선 밖의 꼭짓점을 잡으세요. 겹들이 함께 연결된 경우 전체 겹으로 벌릴 수 있습니다.";return false;
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
            var a=camera.WorldToViewportPoint(table.position+(pull!=null && step.flexible?pull.Tip:f.World(step.pullTip,h)));
            var b=camera.WorldToViewportPoint(table.position+(pull!=null && step.flexible?pull.Stop:f.World(step.pullStop,h)));
            pullVector=new Vector2((b.x-a.x)*viewport.width,(a.y-b.y)*viewport.height);
            if(pullVector.sqrMagnitude<4){notice="종이를 조금 회전해 두 점이 떨어져 보이게 하세요.";return;}
            float progress=Reversing?angle/180f:Mathf.Sign(angle)*InputSign*(1-Mathf.Cos(Mathf.Abs(angle)*Mathf.Deg2Rad));
            pullStart=pos-pullVector*progress;
            gesture=PointerGesture.Pull;
        }
        private void ReverseFoldDirection()
        {
            if(!CanReverseDirection)return;
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
        private void SetMirror(MirrorAxis value)
        {
            if(Adjusting)return;
            mirror=value;if(value!=MirrorAxis.Off)lastAxis=value;
            hoverSide=hoverFlap=0;candidate=turn=null;notice=null;Rebuild();
        }
        private FoldStep SelectedPly(FoldStep line)
        {
            if(first<0 || second<0)return line;
            var basis=sheet.Faces[line.anchor];var a=vertices[first].point;var b=vertices[second].point;
            int target=line.anchor;
            for(int i=0;i<sheet.Faces.Count;i++){
                var f=sheet.Faces[i];
                if(!PaperSheet.SameFrame(f,basis) || !sheet.ContainsPanelPoint(f,a) || !sheet.ContainsPanelPoint(f,b))continue;
                if(scope!=Ply.Bottom?f.layer>sheet.Faces[target].layer:f.layer<sheet.Faces[target].layer)target=i;
            }
            line.anchor=target;line.flap=scope==Ply.Stack?0:target+1;line.singlePanel=scope!=Ply.Stack;return line;
        }
        private void ChoosePly(Ply value)
        {
            if(scope==value)return;
            scope=value;hoverSide=hoverFlap=0;candidate=turn=null;notice=null;
            if(selected.HasValue && !selected.Value.insideReverse && !selected.Value.customFlanks)SelectAxis(SelectedPly(selected.Value));
            Rebuild();
        }
        private void Apply(){if(!owner.ApplyFold(sheet))notice=owner.Session.Research.LastError;}
        private void Layout()
        {
            scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            Rect area=new Rect(36,105,Screen.width/scale-414,Screen.height/scale-185);
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
            if(Button(new Rect(899,37,90,40),"맞춤",false,true)){zoom=1;panOffset=Vector3.zero;PositionCamera();}
            if(Button(new Rect(995,37,48,40),"+",false,zoom<3.999f))SetZoom(zoom*1.25f,new Vector2(.5f,.5f));
            GUI.DrawTexture(viewport,target,ScaleMode.StretchToFill);
            float x=width-354f,w=width-x-36;
            GUI.Label(new Rect(x,105,w,42),"접을 종이",body);
            if(Button(new Rect(310,37,185,40),mirror==MirrorAxis.Off?"대칭 꺼짐  [M]":"대칭 켜짐  [M]",mirror!=MirrorAxis.Off,!Adjusting))
                SetMirror(mirror==MirrorAxis.Off?lastAxis:MirrorAxis.Off);
            if(mirror!=MirrorAxis.Off && Button(new Rect(502,37,142,40),mirror==MirrorAxis.Horizontal?"가로 중심선":"세로 중심선",false,!Adjusting))
                SetMirror(mirror==MirrorAxis.Horizontal?MirrorAxis.Vertical:MirrorAxis.Horizontal);
            GUI.Label(new Rect(650,37,185,40),rightFlank>=0?"4점 접기":leftFlank>=0?"3점 · 한 점 더":second>=0?"2점 접기":"꼭짓점 드래그",caption);
            float sw=(w-8)/3;
            for(int i=0;i<ScopeLabels.Length;i++)
                if(Button(new Rect(x+i*(sw+4),156,sw,38),ScopeLabels[i],(int)scope==i,!Adjusting)){
                    ChoosePly((Ply)i);
                }
            if(Button(new Rect(x,205,w,36),foldDirection==FoldDirection.Opposite?"반대 방향  [R]":"방향 반전  [R]",foldDirection==FoldDirection.Opposite,CanReverseDirection))ReverseFoldDirection();
            GUI.Label(new Rect(x,246,w,28),layerLabel,caption);
            string help=sheet.Count==sheet.FoldLimit?"기체를 적용하거나 한 단계 되돌리세요.":
                phase==Phase.Opening?"움직이는 방향으로 접거나 벌립니다. 놓으면 적용, Esc: 취소.":
                phase==Phase.FirstPoint?"꼭짓점을 끌어 접거나 벌리세요. 점을 클릭하면 새 접힘선을 선택합니다.":
                phase==Phase.SecondPoint?"두 번째 점을 클릭하거나, 누른 채 끌어오세요.":
                phase==Phase.RightFlank?"반대쪽 접힘 방향의 점을 선택하세요.":
                phase==Phase.PickSide?(rightFlank>=0?"꼭짓점이나 면을 드래그하세요. R: 방향 반전.":"끝점을 잡아 끌거나, 면을 접으세요. 양옆 점 추가: 4점 접기.") :
                phase==Phase.PullCorner?"끝점을 끌고 놓으면 그 모양으로 접힙니다." :
                "드래그하거나 슬라이더로 각도를 맞추고 놓으세요.";
            GUI.Label(new Rect(x,286,w,90),help,body);
            if(Adjusting){
                string value=Reversing?"안쪽으로 넣기   "+Mathf.RoundToInt(angle/1.8f)+"%":
                    (phase==Phase.Opening?"움직인 각도   ":"접는 각도   ")+Mathf.RoundToInt(angle)+"°";
                GUI.Label(new Rect(x,426,w,32),value,body);
                float limit=pull!=null?pull.AngleLimit:pair!=null && pair.Step.cornerPull && !Reversing?
                    PaperCornerPull.LimitFor(pair.Step):180f;
                var sliderRect=new Rect(x,466,w,24);var ev=Event.current;
                if(ev.rawType==EventType.MouseDown && sliderRect.Contains(ev.mousePosition))sliderEditing=true;
                float slider=GUI.HorizontalSlider(sliderRect,angle,Reversing?0:-limit,limit);
                if(Mathf.Abs(slider-angle)>.1f)SetAngle(slider);
                if(sliderEditing && ev.rawType==EventType.MouseUp){sliderEditing=false;FinishInteraction();}
                if(!Reversing)for(int i=0;i<AnglePresets.Length;i++)
                    if(Button(new Rect(x+i*w/4,497,w/4-3,36),PresetLabels[i],Mathf.Abs(angle-AnglePresets[i])<.5f,true)){
                        SetAngle(AnglePresets[i]);FinishInteraction();break;
                    }
                if(!Reversing && Button(new Rect(x,544,w,38),"밀착 접기  [C]",false,true))CloseToContact();
            }else if(first>=0){
                if(Button(new Rect(x,386,w,42),"마지막 점 취소  [Backspace]",false,true))RemoveLastPoint();
            }else if(sheet.Count>0 && Button(new Rect(x,386,w,42),"마지막 접기 각도 수정  [E]",false,true))EditLast();
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
            if(Button(new Rect(x,height-124,w,52),sheet.Count==0?"기본 기체 사용  [Enter]":"접은 기체 적용  [Enter]",true,phase==Phase.FirstPoint))Apply();
            if(Button(new Rect(width-184,35,148,42),"닫기  [Esc]",false,true))owner.CloseFolding();
            GUI.Label(new Rect(40,height-61,width-430,44),"드래그: 이동   ·   휠: 확대/축소   ·   우클릭: 회전   ·   각도 조절 중 이동: 휠 버튼 드래그",caption);
            if(notice!=null)GUI.Label(new Rect(x,height-69,w,63),notice,caption);
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
