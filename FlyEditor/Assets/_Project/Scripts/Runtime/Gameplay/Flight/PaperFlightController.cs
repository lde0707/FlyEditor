using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyEditor.Flight
{
    [DisallowMultipleComponent]
    public sealed class PaperFlightController : MonoBehaviour
    {
        [SerializeField] private Camera flightCamera;
        [SerializeField] private Shader paperShader;
        [SerializeField, Min(0.1f)] private float cameraFollowSharpness = 12f;
        [SerializeField] private FlightTuning tuning = new FlightTuning();
        [SerializeField] private ResearchWebAssets researchWebAssets;
        private ResearchWebView researchWeb;
        public ResearchWebAssets ResearchAssets => researchWebAssets;
        public int VisibleResearchCount => researchWeb?.VisibleCount ?? 0;
        public string SelectedResearchId => researchWeb?.SelectedId ?? "";
        public bool ResearchPreviewAll => researchWeb != null && researchWeb.PreviewAll;
        public bool SelectResearchNode(string id) => ResearchOpen && researchWeb != null && researchWeb.Select(id);
        public bool BuyResearchNode(string id) => ResearchOpen && researchWeb != null && researchWeb.TryPurchase(id);
        public ResearchWebNavigation ResearchNavigation => researchWeb?.Navigation;
        public void FitResearchWeb() { if (ResearchOpen) researchWeb?.FitVisible(); }
        public FlightTuning GetResearchTuning() => Session.Research.Apply(tuning);
        public FlightSession Session { get; private set; }
        public FlightModel Model => Session?.Model;
        private enum View { Flight, Research, Folding }
        private FoldingDesk folding;
        private Renderer[] defaultAircraft;
        private Mesh customMesh;
        private Transform customAircraft;
        private Vector3 localNose=PaperPlaneGeometry.Nose;
        public bool FoldingOpen => view==View.Folding;
        private View view;
        public bool ResearchOpen => view == View.Research;
        private string progressError = "";
        private bool waitForMouseRelease;
        private Texture2D circleTexture;
        private Texture2D paperTexture;
        private Font uiFont;
        public Camera FlightCamera => flightCamera;
        private Transform plane;
        private readonly FlightVisualMotion visualMotion = new FlightVisualMotion();
        private LaunchThrower thrower;
        public LaunchThrower Thrower => thrower;
        private LaunchTrajectory trajectory;
        public LaunchTrajectory Trajectory => trajectory;
        private float aimStartY, launchAngle;
        private string aimLabel;
        public float LaunchAngle => launchAngle;
        private Transform terrain;
        private Transform scenery;
        private Transform skyScenery;
        private Transform boostPickup, obstacle;
        private Transform openingSign, nextOpeningPickup, recordFlag;
        private string recordLabel;
        private string equipmentText;
        private Transform[] markers;
        private Material material;
        private Material planeMaterial;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> indices = new List<int>();
        private GUIStyle titleStyle, smallStyle, valueStyle, bodyStyle, centerStyle, badgeStyle;
        private string distanceText = "0.0", speedText = "13.0", altitudeText = "4.0", bestText = "BEST FLIGHT   0.0 m";
        private float hudTimer;
        private bool hasFocus = true;
        private static readonly Color Ink = new Color(0.12f, 0.15f, 0.17f);
        private static readonly Color Muted = new Color(0.40f, 0.46f, 0.48f);
        private static readonly Color Mint = new Color(0.77f, 0.89f, 0.84f);
        private static readonly Color Peach = new Color(0.98f, 0.84f, 0.73f);

        private void Awake()
        {
            if (flightCamera == null || paperShader == null)
            {
                Debug.LogError("Paper Flight: assign the scene camera and PaperFlight shader.", this);
                enabled = false;
                return;
            }
            LoadSession();
            researchWeb = new ResearchWebView(this, researchWebAssets);
            material = new Material(paperShader) { name = "Paper Flight (runtime)" };
            // The thrower uses transparent sprites without depth writes. Draw the
            // paper after those sprites while retaining depth tests against scenery.
            planeMaterial = new Material(material) { name = "Paper plane (foreground)",
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10 };
            trajectory = new LaunchTrajectory(transform,material);
            BuildWorld();
            var prefab = Resources.Load<GameObject>("LaunchThrower");
            if (prefab == null) throw new InvalidOperationException("LaunchThrower prefab is missing.");
            thrower = Instantiate(prefab,transform).GetComponent<LaunchThrower>();
            if (Model != null) { SetAircraft(PaperSheet.Replay(Session.Research.PaperDesign));PrepareThrow(); UpdateVisuals(true); RefreshHud(); }
        }

        private void LoadSession()
        {
            try
            {
                Session = new FlightSession(tuning, new FlightResearch(ResearchFileStore.OpenCurrent()));
                progressError = "";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException)
            { progressError = "연구 저장 파일을 읽지 못했습니다. 기존 파일을 보존했습니다.\n" + exception.Message; }
        }

        public bool OpenResearch()
        {
            if (FoldingOpen || Session == null || !Session.Research.Unlocked || Model.State == FlightModel.Phase.Flying
                || Session.SettlementPending) return false;
            SetView(View.Research); RefreshResearch(); return true;
        }
        public void CloseResearch() { SetView(View.Flight); }
        public bool OpenFolding()
        {
            if(view!=View.Flight || Model==null || Model.State!=FlightModel.Phase.Ready || thrower.Pending)return false;
            if(folding==null)folding=new FoldingDesk(this,material);
            SetView(View.Folding);folding.Open(Session.Research.PaperDesign);return true;
        }
        internal void CloseFolding(){if(FoldingOpen)SetView(View.Flight);}
        internal bool ApplyFold(PaperSheet sheet)
        {
            if(!FoldingOpen || !Session.TryEquipPaper(sheet))return false;
            SetAircraft(sheet);PrepareThrow();RefreshHud();CloseFolding();return true;
        }
        private void SetAircraft(PaperSheet sheet)
        {
            bool custom=sheet.Count>0;
            if(custom){
                if(customAircraft==null){
                    customAircraft=Child("내가 접은 기체",plane);
                    customMesh=new Mesh{name="접은 기체"};meshes.Add(customMesh);
                    customAircraft.gameObject.AddComponent<MeshFilter>().sharedMesh=customMesh;
                    var renderer=customAircraft.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial=planeMaterial;renderer.sortingOrder=100;
                }
                localNose=new FoldMesh().BuildAircraft(customMesh,sheet);
            }
            else localNose=PaperPlaneGeometry.Nose;
            trajectory.SetNose(localNose);
            if(customAircraft!=null)customAircraft.gameObject.SetActive(custom);
            foreach(var renderer in defaultAircraft)renderer.enabled=!custom;
        }
        private void SetView(View next)
        {
            if (view == next) return;
            if (FoldingOpen) folding?.Close();
            view = next;
            if (thrower != null && thrower.Pending) PrepareThrow();
            if (next == View.Flight) researchWeb?.SetPreview(false);
            Model?.SuspendInput();
            waitForMouseRelease = true;
        }
        public bool BuySelectedResearch(ResearchNode node) => ResearchOpen && researchWeb != null && researchWeb.TryPurchaseNext(node);
        internal bool CommitResearchPurchase(int index)
        {
            if (!ResearchOpen || ResearchPreviewAll || !Session.TryBuy(researchWebAssets, index)) return false;
            RefreshHud(); return true;
        }
        private void RefreshResearch() { researchWeb?.Refresh(); }
#if UNITY_EDITOR
        public bool AdjustDeveloperPoints(int delta)
        {
            if (Session == null || !Session.Research.AdjustDeveloperPoints(delta)) return false;
            RefreshResearch();
            RefreshHud();
            return true;
        }
        public bool ResetDeveloperProgress()
        {
            if (Session == null || !Session.ResetDeveloperSession()) return false;
            SetAircraft(new PaperSheet());
            CloseResearch();
            PrepareThrow();
            RefreshResearch();
            waitForMouseRelease = true;
            UpdateVisuals(true);
            RefreshHud();
            return true;
        }
#endif

        public bool SetResearchPreview(bool enabled)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || Session == null || Model.State == FlightModel.Phase.Flying || Session.SettlementPending) return false;
            if (!enabled) { CloseResearch(); return true; }
            SetView(View.Research); researchWeb.SetPreview(true); return true;
#else
            return false;
#endif
        }

        private void OnValidate()
        {
            tuning.Sanitize();
            cameraFollowSharpness = Mathf.Max(0.1f, cameraFollowSharpness);
        }

        private void Update()
        {
            if (!hasFocus || Model == null) return;
            if (FoldingOpen) { folding.Tick(Time.unscaledDeltaTime); return; }
            var mouse = Mouse.current;
            if (mouse == null && thrower.State == LaunchThrower.Phase.Aiming) PrepareThrow();
            var keyboard = Keyboard.current;
            bool pressed = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool held = mouse != null && mouse.leftButton.isPressed;
            bool released = mouse != null && mouse.leftButton.wasReleasedThisFrame;
            bool restart = keyboard != null && keyboard.rKey.wasPressedThisFrame;
            if (view==View.Flight && pressed && !waitForMouseRelease)
            {
                float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
                Vector2 p=mouse.position.ReadValue();
                if (FoldButton(Screen.width/scale).Contains(new Vector2(p.x,Screen.height-p.y)/scale) && OpenFolding())return;
            }
            if (ResearchOpen)
            {
                researchWeb?.Tick(Time.unscaledTime);
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) CloseResearch();
                else researchWeb?.HandleKeyboard(keyboard);
                return;
            }
            if (keyboard != null && keyboard.uKey.wasPressedThisFrame && OpenResearch()) return;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) Model.TryRecoverPosture();
            bool onUi = mouse != null && IsOverFlightUI(mouse.position.ReadValue());
            if (waitForMouseRelease)
            {
                if (!held) waitForMouseRelease = false;
                pressed = held = released = false;
            }
            float mouseY = mouse != null ? mouse.position.ReadValue().y : 0f;
            TickLaunch(pressed && !onUi, held && !onUi, released, mouseY, restart);
            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer <= 0f) { RefreshHud(); hudTimer = 0.1f; }
        }

        private void PrepareThrow()
        {
            trajectory.Hide();
            visualMotion.Reset(Model,Time.timeAsDouble);
            launchAngle = 0;
            thrower.Prepare(new Vector3(0f, Model.Height, 0f));
            // Snapshot the saved best before launch. A new record is marked on the next run.
            float best=Session.Research.BestDistance;
            recordFlag.position=new Vector3(best,0f,.35f);
            recordFlag.gameObject.SetActive(best>0f);
            recordLabel="최고 "+best.ToString("0.0")+" m";
        }
        private void UpdateAim(float mouseY)
        {
            float angle = LaunchThrower.NeutralAim + (mouseY - aimStartY) * 100f / Mathf.Max(1,Screen.height);
            angle = Mathf.Round(Mathf.Clamp(angle,FlightModel.MinLaunchAngle,FlightModel.MaxLaunchAngle)*4f)/4f;
            if (!trajectory.Visible || angle != launchAngle)
                aimLabel = "발사 각도  " + angle.ToString("0.##") + "°";
            launchAngle = angle;
            thrower.SetAim(angle);
            trajectory.Show(Model,angle,Time.fixedDeltaTime,thrower.ReleaseHeight);
        }
        private void TickLaunch(bool pressed,bool held,bool released,float mouseY,bool restart)
        {
            if (restart || Model.State == FlightModel.Phase.Landed)
            {
                Session.Tick(pressed,held,restart);
                if (Model.State == FlightModel.Phase.Ready) PrepareThrow();
                else thrower.Advance(Time.deltaTime);
                return;
            }
            if (Model.State == FlightModel.Phase.Ready)
            {
                if (pressed && thrower.State == LaunchThrower.Phase.Holding)
                {
                    aimStartY = mouseY;
                    thrower.StartAiming();
                }
                if (thrower.State == LaunchThrower.Phase.Aiming)
                {
                    UpdateAim(mouseY);
                    if (released)
                    {
                        // Commit the last aim at the displayed nose before the hand starts moving.
                        trajectory.DrawFromNose(plane.TransformPoint(localNose));
                        thrower.Begin();
                    }
                }
                if (!thrower.Advance(Time.deltaTime)) return;
                // Start the saved flight exactly once, at the authored Spine release event.
                trajectory.Hide();
                if (Session.TryLaunch(launchAngle,thrower.Grip.position.y)) Model.Tick(false,false,false);
                else PrepareThrow();
                return;
            }
            thrower.Advance(Time.deltaTime);
            Session.Tick(pressed,held,false);
        }

        private void FixedUpdate()
        {
            if (hasFocus && Model != null && view==View.Flight)
            {
                var before = new FlightVisualMotion.Pose(Model);
                Session.FixedTick(Time.fixedDeltaTime);
                visualMotion.RecordStep(Model,before,Time.fixedTimeAsDouble);
            }
        }

        private void LateUpdate()
        {
            if (Model == null || FoldingOpen) return;
            UpdateVisuals(false);
            if (hasFocus && !ResearchOpen && thrower.State != LaunchThrower.Phase.Holding)
                thrower.UpdateGaze(plane.TransformPoint(localNose),Time.deltaTime);
        }

        private bool IsOverFlightUI(Vector2 mousePosition)
        {
            if (Model.State == FlightModel.Phase.Flying) return false;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            float width = Screen.width / scale;
            Vector2 point = new Vector2(mousePosition.x, Screen.height - mousePosition.y) / scale;
            return (Model.State==FlightModel.Phase.Ready && FoldButton(width).Contains(point)) ||
                ((Session.Research.Unlocked || Session.SettlementPending) && new Rect(width - 242, 295, 202, 84).Contains(point));
        }
        private static Rect FoldButton(float width)=>new Rect(width-475,34,201,52);

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused || Application.isBatchMode;
            if (!focused && Model != null)
            {
                Model.SuspendInput();
                if (!hasFocus && thrower != null && thrower.Pending) PrepareThrow();
            }
        }

        private void OnDisable()
        {
            if (Model != null) Model.SuspendInput();
            if (thrower != null && thrower.Pending) PrepareThrow();
        }

        private void OnDestroy()
        {
            folding?.Dispose();
            trajectory?.Dispose();
            researchWeb?.Dispose();
            if (material != null) Destroy(material);
            if (planeMaterial != null) Destroy(planeMaterial);
            if (circleTexture != null) Destroy(circleTexture);
            if (paperTexture != null) Destroy(paperTexture);
            if (uiFont != null) Destroy(uiFont);
            foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
        }

        private void RefreshHud()
        {
            equipmentText = "내구 " + Mathf.CeilToInt(Model.Durability) + " / " + Mathf.CeilToInt(Model.Effects.durability);
            if (Model.Effects.shieldUses > 0) equipmentText += "     방패 " + Model.ShieldUses;
            if (Model.Effects.recoveryUses > 0) equipmentText += "     자세 회복 [Space] " + Model.RecoveryUses;
            distanceText = Model.Distance.ToString("0.0");
            speedText = Model.HorizontalSpeed.ToString("0.0");
            altitudeText = Model.Clearance.ToString("0.0");
            bestText = "BEST FLIGHT   " + Model.BestDistance.ToString("0.0") + " m";
        }

        private void UpdateVisuals(bool snap)
        {
            // The aircraft, camera and scrolling world must share the same rendered
            // sample. The simulation remains on its original fixed clock.
            var pose = visualMotion.Sample(Model,Time.timeAsDouble,Time.fixedDeltaTime,snap);
            if (Model.State == FlightModel.Phase.Ready && thrower.HoldingPlane)
                plane.SetPositionAndRotation(thrower.Grip.position, thrower.Pending
                    ? Quaternion.Euler(0,0,launchAngle) : thrower.Grip.rotation);
            else plane.SetPositionAndRotation(pose.position,Quaternion.Euler(0f,0f,pose.pitch));
            if (thrower.State == LaunchThrower.Phase.Aiming && trajectory.Visible)
            {
                Vector3 nose = plane.TransformPoint(localNose);
                trajectory.DrawFromNose(nose);
            }
            boostPickup.position = Session.Course.Pickup;
            bool showNext=Session.Course.TryGetNextOpeningPickup(out var nextPosition);
            if (showNext!=nextOpeningPickup.gameObject.activeSelf) nextOpeningPickup.gameObject.SetActive(showNext);
            if (showNext) nextOpeningPickup.position=nextPosition;
            obstacle.position = Session.Course.Obstacle;
            bool showSign=Session.Course.IsPaperSign;
            if (showSign!=openingSign.gameObject.activeSelf)
            {
                openingSign.gameObject.SetActive(showSign);
                obstacle.gameObject.SetActive(!showSign);
            }
            Vector3 cameraPosition = new Vector3(pose.position.x + 7f, Mathf.Max(7.5f, pose.position.y + 2f), -24f);
            flightCamera.transform.position = snap || Model.State == FlightModel.Phase.Ready
                ? cameraPosition : Vector3.Lerp(flightCamera.transform.position, cameraPosition, 1f - Mathf.Exp(-cameraFollowSharpness * Time.deltaTime));
            float viewSize = 8.5f + Mathf.Clamp((pose.speed - 18f) * .10f, 0f, 12f);
            flightCamera.orthographicSize = snap ? viewSize :
                Mathf.Lerp(flightCamera.orthographicSize, viewSize, 1f - Mathf.Exp(-4f * Time.deltaTime));
            terrain.position = new Vector3(pose.position.x, 0f, 0f);
            scenery.position = new Vector3(Mathf.Floor(pose.position.x / 120f) * 120f, 0f, 0f);
            skyScenery.position = new Vector3(Mathf.Floor(pose.position.x / 96f) * 96f,
                Mathf.Max(0f, Mathf.Floor((pose.position.y - 60f) / 48f) * 48f), 0);
            int first = Mathf.FloorToInt((pose.position.x - 35f) / 5f);
            for (int i = 0; i < markers.Length; i++)
                markers[i].position = new Vector3((first + i) * 5f, 0f, -0.4f);
        }

        private Transform Child(string objectName, Transform parent)
        {
            var child = new GameObject(objectName).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void BuildWorld()
        {
            flightCamera.orthographic = true;
            flightCamera.orthographicSize = 8.5f;
            flightCamera.clearFlags = CameraClearFlags.SolidColor;
            flightCamera.backgroundColor = Color.white;
            flightCamera.transform.rotation = Quaternion.Euler(9f, 0f, 0f);
            plane = Child("Folded paper plane", transform);
            Vector3 nose = PaperPlaneGeometry.Nose;
            Vector3 back = PaperPlaneGeometry.Back;
            Vector3 left = PaperPlaneGeometry.Left;
            Vector3 right = PaperPlaneGeometry.Right;
            Vector3 keel = PaperPlaneGeometry.Keel;
            BeginMesh();
            Triangle(nose, back, left, new Color(0.94f, 0.97f, 0.96f));
            Triangle(nose, right, back, Mint);
            Triangle(nose, keel, back, new Color(0.71f, 0.80f, 0.80f));
            EndMesh("Paper surfaces", plane);
            Line("Near wing outline", plane, new[] { nose, left, back, nose }, Ink, 0.025f);
            Line("Far wing outline", plane, new[] { nose, right, back }, Ink, 0.025f);
            Line("Centre fold", plane, new[] { nose, keel, back }, Ink, 0.025f);
            defaultAircraft=plane.GetComponentsInChildren<Renderer>();
            foreach (var renderer in defaultAircraft)
            {
                renderer.sharedMaterial = planeMaterial;
                renderer.sortingOrder = renderer is LineRenderer ? 101 : 100;
            }

            boostPickup = Child("가속 아이템", transform);
            Vector3 up = Vector3.up * .75f, side = Vector3.right * .75f;
            BeginMesh(); Quad(up, side, -up, -side, new Color(.97f, .88f, .66f));
            EndMesh("Folded pickup", boostPickup);
            Line("Pickup outline", boostPickup, new[] { up, side, -up, -side, up }, Ink, .04f);
            Line("Pickup arrow", boostPickup, new[] { new Vector3(-.22f,.34f,-.03f), new Vector3(.19f,0,-.03f), new Vector3(-.22f,-.34f,-.03f) }, Ink, .07f);
            obstacle = Child("종이 장애물", transform);
            var tip = new Vector3(0, 1.1f, 0); var leftCorner = new Vector3(-1.05f,-.78f,0); var rightCorner = new Vector3(1.05f,-.78f,0);
            BeginMesh(); Triangle(tip, rightCorner, leftCorner, Peach); EndMesh("Obstacle surface", obstacle);
            Line("Obstacle outline", obstacle, new[] { tip, rightCorner, leftCorner, tip }, Ink, .05f);
            Line("Obstacle fold", obstacle, new[] { tip, new Vector3(.32f,-.78f,-.03f) }, Muted, .04f);
            BuildOpeningCourse();
            obstacle.gameObject.SetActive(false);

            terrain = Child("Ground", transform);
            BeginMesh();
            Quad(new Vector3(-70f, -5f, 0.8f), new Vector3(90f, -5f, 0.8f),
                new Vector3(90f, 0f, 0.8f), new Vector3(-70f, 0f, 0.8f), new Color(0.96f, 0.96f, 0.92f));
            EndMesh("Ground wash", terrain);
            Line("Ground contact line", terrain, new[] { new Vector3(-70f, 0f, 0f), new Vector3(90f, 0f, 0f) }, Ink, 0.032f);
            markers = new Transform[25];
            for (int i = 0; i < markers.Length; i++)
            {
                markers[i] = Child("Five metre tick", transform);
                Line("Tick", markers[i], new[] { Vector3.zero, Vector3.down * 0.22f }, Muted, 0.02f);
            }
            scenery = Child("Pastel landscape", transform);
            BeginMesh();
            for (int i = -2; i < 9; i++)
            {
                float x = i * 24f;
                Color color = i % 2 == 0 ? new Color(0.92f, 0.94f, 0.97f) : new Color(0.96f, 0.91f, 0.87f);
                Triangle(new Vector3(x - 12f, -1.8f, 8f), new Vector3(x + 17f, -1.8f, 8f),
                    new Vector3(x + 2f, 3.5f + (i & 1), 8f), color);
                Quad(new Vector3(x + 8f, 7.8f, 10f), new Vector3(x + 11f, 7.8f, 10f),
                    new Vector3(x + 11f, 7.85f, 10f), new Vector3(x + 8f, 7.85f, 10f), new Color(0.85f, 0.9f, 0.91f));
            }
            EndMesh("Distant geometry", scenery);
            skyScenery = Child("높은 하늘 구름층", transform);
            BeginMesh();
            for (int row = 0; row < 6; row++)
                for (int column = -2; column <= 6; column++)
                {
                    float x = column * 24f + (row % 2) * 9f;
                    float y = 24f + row * 24f + ((column & 1) * 2f);
                    Quad(new Vector3(x-5,y,12), new Vector3(x+6,y,12),
                        new Vector3(x+3,y+1.1f,12), new Vector3(x-2,y+1.1f,12),
                        new Color(.90f,.94f,.96f));
                }
            EndMesh("높이 기준 구름", skyScenery);
        }

        private void BuildOpeningCourse()
        {
            var course=Child("첫 비행 · 0–50 m",transform);
            nextOpeningPickup=Instantiate(boostPickup,course);
            nextOpeningPickup.name="다음 저공 가속 아이템";
            // Two pale paper ridges frame the low lane; their folds are scenery.
            var hills=Child("접힌 종이 언덕",course);
            BeginMesh();
            FoldedHill(2f,12f,21f,2.7f);
            FoldedHill(31f,42f,50f,3.2f);
            EndMesh("언덕 종이 면",hills);
            float end=FlightCourse.OpeningEnd;
            Line("50 m 접힘선",course,new[]{new Vector3(end,-.43f,-.35f),new Vector3(end,.35f,-.35f)},Muted,.06f);

            openingSign=Child("종이 표지판",course);
            openingSign.position=new Vector3(FlightCourse.SignCenter.x,0,0);
            float w=FlightCourse.SignHalfSize.x, top=FlightCourse.SignCenter.y+FlightCourse.SignHalfSize.y;
            float bottom=FlightCourse.SignCenter.y-FlightCourse.SignHalfSize.y, post=FlightCourse.PostHalfWidth;
            Vector3 a=new Vector3(-w,bottom,0), b=new Vector3(w,bottom,0);
            Vector3 c=new Vector3(w,top-.34f,0), d=new Vector3(w-.34f,top,0), e=new Vector3(-w,top,0);
            BeginMesh();
            Quad(new Vector3(-post,0,.02f),new Vector3(post,0,.02f),new Vector3(post,bottom,.02f),new Vector3(-post,bottom,.02f),Muted);
            Quad(a,b,c,d,Peach); Triangle(a,d,e,Peach);
            Triangle(c,d,new Vector3(d.x,c.y,-.02f),new Color(.87f,.66f,.56f));
            EndMesh("표지판 접힌 면",openingSign);
            Vector3 inkOffset=Vector3.back*.04f;
            Line("표지판 테두리",openingSign,new[]{a+inkOffset,b+inkOffset,c+inkOffset,d+inkOffset,e+inkOffset,a+inkOffset},Ink,.055f);
            Line("위로 피해가기",openingSign,new[]{new Vector3(-.45f,bottom+.40f,-.04f),
                new Vector3(0,top-.52f,-.04f),new Vector3(.45f,bottom+.40f,-.04f)},Ink,.10f);

            recordFlag=Child("최고 기록 깃발",transform);
            Vector3 flagTop=new Vector3(0,2.7f,0), tail=new Vector3(1.55f,2.43f,0);
            Vector3 fold=new Vector3(.58f,2.09f,-.02f), lower=new Vector3(0,1.76f,0);
            BeginMesh();
            Triangle(flagTop,tail,fold,Mint);Triangle(flagTop,fold,lower,new Color(.62f,.79f,.71f));
            EndMesh("기록 페넌트",recordFlag);
            Line("깃대",recordFlag,new[]{inkOffset,flagTop+inkOffset},Muted,.075f);
            Line("깃발 테두리",recordFlag,new[]{flagTop+inkOffset,tail+inkOffset,fold+inkOffset,lower+inkOffset,flagTop+inkOffset},Muted,.045f);
            Line("깃발 접힌 선",recordFlag,new[]{flagTop+inkOffset,fold+inkOffset},Muted,.03f);
            recordFlag.gameObject.SetActive(false);
        }
        private void FoldedHill(float left,float peak,float right,float height)
        {
            Vector3 a=new Vector3(left,0,4), b=new Vector3(peak,height,4);
            Vector3 crease=new Vector3(peak-2.4f,0,4), c=new Vector3(right,0,4);
            Triangle(a,b,crease,new Color(.93f,.95f,.91f));
            Triangle(crease,b,c,new Color(.87f,.91f,.86f));
        }

        private void BeginMesh() { vertices.Clear(); colors.Clear(); indices.Clear(); }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            colors.Add(color); colors.Add(color); colors.Add(color);
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Triangle(a, b, c, color); Triangle(a, c, d, color);
        }

        private void EndMesh(string meshName, Transform parent)
        {
            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var child = Child(meshName, parent);
            child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void Line(string lineName, Transform parent, Vector3[] points, Color color, float width)
        {
            var line = Child(lineName, parent).gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 24);
            titleStyle = Style(28, Ink, FontStyle.Bold);
            smallStyle = Style(13, Muted);
            valueStyle = Style(42, Ink);
            bodyStyle = Style(17, Ink);
            centerStyle = Style(26, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            badgeStyle = Style(13, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            foreach (var style in new[] { titleStyle, smallStyle, valueStyle, bodyStyle, centerStyle, badgeStyle })
                style.font = uiFont;
            bodyStyle.wordWrap = true;
            circleTexture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            circleTexture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                pixels[y * 128 + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(63.5f - Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f))));
            circleTexture.SetPixels(pixels); circleTexture.Apply();
            paperTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            paperTexture.filterMode = FilterMode.Bilinear;
            var paperPixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                Vector2 p = new Vector2(x - 32, 32 - y);
                if (InTriangle(p, new Vector2(-20, 16), new Vector2(22, -18), Vector2.zero))
                    paperPixels[y * 64 + x] = new Color(0.94f, 0.97f, 0.96f);
                else if (InTriangle(p, new Vector2(22, -18), new Vector2(8, 23), Vector2.zero))
                    paperPixels[y * 64 + x] = Mint;
            }
            paperTexture.SetPixels(paperPixels); paperTexture.Apply();
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }

        private static GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight, alignment = alignment };
            style.normal.textColor = color;
            return style;
        }

        private static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void OnGUI()
        {
            EnsureStyles();
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            if (Model == null)
            {
                Fill(new Rect(0, 0, width, height), Color.white);
                GUI.Label(new Rect(80, 100, width - 160, 180), progressError, bodyStyle);
                if (OutlineButton(new Rect(80, 300, 220, 48), "다시 읽기")) LoadSession();
                GUI.matrix = previous; return;
            }
            if (FoldingOpen)
            {
                folding.Draw(width,height,uiFont);
                GUI.matrix=previous;return;
            }
            if (ResearchOpen)
            {
                DrawResearch(width, height);
                GUI.matrix = previous; return;
            }
            Fill(new Rect(36, 32, 6, 58), Mint);
            GUI.Label(new Rect(58, 29, 360, 42), "PAPER / FLIGHT", titleStyle);
            GUI.Label(new Rect(60, 69, 360, 25), "01   /   THE FIRST GLIDE", smallStyle);
            GUI.Label(new Rect(width - 245, 36, 210, 23), bestText, smallStyle);
            if(Model.State==FlightModel.Phase.Ready && !thrower.Pending &&
                OutlineButton(FoldButton(width),"종이접기"))OpenFolding();
            Fill(new Rect(40, 111, width - 80, 1), new Color(0.82f, 0.85f, 0.84f));

            GUI.Label(new Rect(42, 135, 250, 24), "DISTANCE / m", smallStyle);
            GUI.Label(new Rect(40, 159, 280, 58), distanceText, valueStyle);
            GUI.Label(new Rect(290, 135, 180, 24), "전진 속도 / m/s", smallStyle);
            GUI.Label(new Rect(288, 169, 180, 38), speedText, titleStyle);
            GUI.Label(new Rect(480, 135, 180, 24), "ALTITUDE / m", smallStyle);
            GUI.Label(new Rect(478, 169, 180, 38), altitudeText, titleStyle);
            string badge = Model.State == FlightModel.Phase.Ready ? (trajectory.Visible ? aimLabel : thrower.Pending ? "던지는 중" : "왼쪽 버튼 길게 누르기") : Model.State == FlightModel.Phase.Landed
                ? "FLIGHT COMPLETE" : Model.Recovering ? "RELEASE TO RECOVER" : Model.Climbing ? "CLIMB" : "GLIDE";
            Fill(new Rect(width - 242, 141, 202, 36), Model.Recovering ? Peach : Mint);
            GUI.Label(new Rect(width - 242, 141, 202, 36), badge, badgeStyle);
            GUI.Label(new Rect(width - 242, 190, 202, 24), "CLIMB RESERVE", smallStyle);
            Fill(new Rect(width - 242, 221, 202, 5), new Color(0.91f, 0.93f, 0.92f));
            Fill(new Rect(width - 242, 221, 202 * Model.ClimbReserve, 5), Model.ClimbReserve < 0.25f ? Peach : Ink);
            GUI.Label(new Rect(width - 242, 239, 202, 24), "TOTAL FLIGHT ENERGY", smallStyle);
            Fill(new Rect(width - 242, 270, 202, 3), new Color(0.91f, 0.93f, 0.92f));
            Fill(new Rect(width - 242, 270, 202 * Mathf.Clamp01(Model.Energy / Session.InitialEnergy), 3), Muted);
            if (Model.State == FlightModel.Phase.Flying)
            {
                GUI.Label(new Rect(42, 245, 680, 32), equipmentText, bodyStyle);
                if (Model.Posture == FlightModel.Attitude.Disturbed && Model.RecoveryUses > 0)
                    GUI.Label(new Rect(42, 279, 420, 32), "Space · 자세 회복", bodyStyle);
                else if (Model.DiveBoostActive)
                    GUI.Label(new Rect(42, 279, 420, 32), "하강 가속", bodyStyle);
            }
            if (Model.State != FlightModel.Phase.Flying)
            {
                if (Session.SettlementPending)
                {
                    if (OutlineButton(new Rect(width - 242, 295, 202, 44), "보상 저장 재시도")) Session.TrySettle();
                }
                else if (Session.Research.Unlocked)
                {
                    if (OutlineButton(new Rect(width - 242, 295, 202, 44), "연구 열기  [U]")) OpenResearch();
                    GUI.Label(new Rect(width - 242, 346, 220, 28), "연구점  " + Session.Research.Points, bodyStyle);
                }
                else GUI.Label(new Rect(width - 242, 302, 220, 56), "첫 비행을 마치면\n연구가 열립니다.", bodyStyle);
                if (Model.State == FlightModel.Phase.Landed)
                    GUI.Label(new Rect(42, 235, 480, 30), Session.SettlementPending ? "보상을 아직 저장하지 못했습니다."
                        : "이번 비행  +" + Session.LastReward + " 연구점", bodyStyle);
            }
            if (!string.IsNullOrEmpty(Session.Research.LastError))
                GUI.Label(new Rect(42, 280, width - 330, 80), Session.Research.LastError, bodyStyle);

            if (Model.State == FlightModel.Phase.Landed)
            {
                float left = width * 0.5f - 240f;
                Fill(new Rect(left, height * 0.48f - 1, 480, 130), Ink);
                Fill(new Rect(left + 1, height * 0.48f, 478, 128), new Color(0.98f, 0.99f, 0.98f));
                GUI.Label(new Rect(left, height * 0.48f + 14, 480, 45),
                    Model.State == FlightModel.Phase.Ready ? "A little paper. A long way." : "Every flight starts again.", centerStyle);
                GUI.Label(new Rect(left, height * 0.48f + 71, 480, 30),
                    "LEFT CLICK OR R  /  RESET", badgeStyle);
            }
            Fill(new Rect(40, height - 96, width - 80, 1), new Color(0.82f, 0.85f, 0.84f));
            GUI.Label(new Rect(42, height - 74, width - 84, 30),
                Model.State == FlightModel.Phase.Ready ? "왼쪽 버튼을 누른 채 위아래로 조준하고, 놓아서 던지세요."
                    : "왼쪽 버튼 누르기 · 상승     놓기 · 하강     R · 다시 시작", bodyStyle);
            GUI.Label(new Rect(42, height - 40, width - 84, 24), Model.Distance<FlightCourse.OpeningEnd
                ? "첫 50 m   ·   낮게 날면 가속 아이템   /   높게 날면 표지판 위로 안전 통과"
                : "노란 마름모를 먹으면 2초간 앞으로 더 빠르게 날아갑니다.", smallStyle);
            if (Model.State!=FlightModel.Phase.Landed)
            {
                DrawCourseLabel(new Vector3(FlightCourse.OpeningEnd,-.5f,0),"50 m",scale);
                if (recordFlag.gameObject.activeSelf)
                    DrawCourseLabel(recordFlag.position+Vector3.up*3.35f,recordLabel,scale);
            }
            GUI.matrix = previous;
        }

        private void DrawCourseLabel(Vector3 world,string text,float scale)
        {
            Vector3 p=flightCamera.WorldToScreenPoint(world);
            if (p.z>0 && p.x>=0 && p.x<=Screen.width && p.y>=0 && p.y<=Screen.height)
                GUI.Label(new Rect(p.x/scale,(Screen.height-p.y)/scale,150,22),text,smallStyle);
        }

        private bool OutlineButton(Rect rect, string text)
        {
            Fill(rect, GUI.enabled ? Ink : Muted);
            Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), Color.white);
            GUI.Label(rect, text, badgeStyle);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }
        private void Disc(Vector2 center, float radius, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), circleTexture);
            GUI.color = Color.white;
        }
        private void Segment(Vector2 a, Vector2 b, float thickness, Color color)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - thickness * 0.5f, Vector2.Distance(a, b), thickness), color);
            GUI.matrix = matrix;
        }
        private void Symbol(Vector2 c, ResearchNode node)
        {
            if (node == ResearchNode.Throw)
            {
                GUI.DrawTexture(new Rect(c.x - 32, c.y - 32, 64, 64), paperTexture);
                Segment(c + new Vector2(-20, 16), c + new Vector2(22, -18), 2, Ink);
                Segment(c + new Vector2(22, -18), c + new Vector2(8, 23), 2, Ink);
                Segment(c + new Vector2(8, 23), c, 2, Ink);
                Segment(c, c + new Vector2(-20, 16), 2, Ink);
                Segment(c, c + new Vector2(22, -18), 2, Ink);
            }
            else if (node == ResearchNode.Glide)
            {
                Segment(c + new Vector2(-25, -10), c, 2, Ink);
                Segment(c, c + new Vector2(25, -10), 2, Ink);
                Segment(c + new Vector2(-18, 8), c + new Vector2(18, 8), 2, Ink);
                Segment(c + new Vector2(-10, 20), c + new Vector2(10, 20), 2, Ink);
            }
            else
            {
                Disc(c, 16, Ink); Disc(c, 14, Color.white);
                Segment(c + new Vector2(-26, 0), c + new Vector2(26, 0), 2, Ink);
                Segment(c + new Vector2(0, -26), c + new Vector2(0, 26), 2, Ink);
                Disc(c, 4, Ink);
            }
        }
        private void Square(Vector2 center, float size, Color fill)
        {
            Rect rect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            Fill(rect, Ink);
            Fill(new Rect(rect.x + 1.5f, rect.y + 1.5f, size - 3, size - 3), fill);
        }
        private void DrawResearch(float width, float height)
        {
            Fill(new Rect(0, 0, width, height), Color.white);
            if (researchWeb == null) researchWeb = new ResearchWebView(this, researchWebAssets);
            researchWeb.Draw(uiFont);
        }
    }
    // Fixed simulation snapshots, sampled one physics tick behind for rendering.
    // Model identity and the existing Ready/Flying/Landed FSM bound each sample pair.
    public sealed class FlightVisualMotion
    {
        public readonly struct Pose
        {
            public readonly Vector3 position;
            public readonly float pitch,speed;
            public Pose(FlightModel model)
            {position=new Vector3(model.Distance,model.Height,0);pitch=model.Pitch;speed=model.HorizontalSpeed;}
            private Pose(Vector3 position,float pitch,float speed)
            {this.position=position;this.pitch=pitch;this.speed=speed;}
            internal static Pose Blend(Pose a,Pose b,float t)=>new Pose(Vector3.Lerp(a.position,b.position,t),
                Mathf.LerpAngle(a.pitch,b.pitch,t),Mathf.Lerp(a.speed,b.speed,t));
        }
        private FlightModel source;
        private FlightModel.Phase sampledPhase;
        private Pose previous,current;
        private double fixedAt;
        public void Reset(FlightModel model,double now)
        {
            source=model;sampledPhase=model.State;previous=current=new Pose(model);fixedAt=now;
        }
        public void RecordStep(FlightModel model,Pose before,double fixedTime)
        {
            source=model;sampledPhase=model.State;previous=before;current=new Pose(model);fixedAt=fixedTime;
        }
        public Pose Sample(FlightModel model,double renderTime,float step,bool snap=false)
        {
            // Never blend a retry/equipped design or hand release with the preceding run.
            if(snap || source!=model || model.State==FlightModel.Phase.Ready || sampledPhase==FlightModel.Phase.Ready)
                Reset(model,renderTime);
            float t=step>0?Mathf.Clamp01((float)((renderTime-fixedAt)/step)):1f;
            return Pose.Blend(previous,current,t);
        }
    }

}
