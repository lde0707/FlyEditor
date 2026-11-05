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
        private enum View { Flight, Research }
        private View view;
        public bool ResearchOpen => view == View.Research;
        private string progressError = "";
        private bool waitForMouseRelease;
        private Texture2D circleTexture;
        private Texture2D paperTexture;
        private Font uiFont;
        public Camera FlightCamera => flightCamera;
        private Transform plane;
        private Transform terrain;
        private Transform scenery;
        private Transform boostPickup, obstacle;
        private string equipmentText;
        private Transform[] markers;
        private Material material;
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
            BuildWorld();
            if (Model != null) { UpdateVisuals(true); RefreshHud(); }
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
            if (Session == null || !Session.Research.Unlocked || Model.State == FlightModel.Phase.Flying
                || Session.SettlementPending) return false;
            SetView(View.Research); RefreshResearch(); return true;
        }
        public void CloseResearch() { SetView(View.Flight); }
        private void SetView(View next)
        {
            if (view == next) return;
            view = next;
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
            CloseResearch();
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
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool pressed = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool held = mouse != null && mouse.leftButton.isPressed;
            bool restart = keyboard != null && keyboard.rKey.wasPressedThisFrame;
            if (ResearchOpen)
            {
                researchWeb?.Tick(Time.unscaledTime);
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) CloseResearch();
                return;
            }
            if (keyboard != null && keyboard.uKey.wasPressedThisFrame && OpenResearch()) return;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) Model.TryRecoverPosture();
            bool onUi = mouse != null && IsOverFlightUI(mouse.position.ReadValue());
            if (waitForMouseRelease)
            {
                if (!held) waitForMouseRelease = false;
                pressed = held = false;
            }
            Session.Tick(pressed && !onUi, held && !onUi, restart);
            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer <= 0f) { RefreshHud(); hudTimer = 0.1f; }
        }

        private void FixedUpdate()
        {
            if (hasFocus && Model != null && !ResearchOpen) Session.FixedTick(Time.fixedDeltaTime);
        }

        private void LateUpdate() { if (Model != null) UpdateVisuals(false); }

        private bool IsOverFlightUI(Vector2 mousePosition)
        {
            if (Model.State == FlightModel.Phase.Flying) return false;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            float width = Screen.width / scale;
            Vector2 point = new Vector2(mousePosition.x, Screen.height - mousePosition.y) / scale;
            return new Rect(width - 242, 295, 202, 84).Contains(point);
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused || Application.isBatchMode;
            if (!focused && Model != null) Model.SuspendInput();
        }

        private void OnDisable() { if (Model != null) Model.SuspendInput(); }

        private void OnDestroy()
        {
            researchWeb?.Dispose();
            if (material != null) Destroy(material);
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
            speedText = Model.Speed.ToString("0.0");
            altitudeText = Model.Clearance.ToString("0.0");
            bestText = "BEST FLIGHT   " + Model.BestDistance.ToString("0.0") + " m";
        }

        private void UpdateVisuals(bool snap)
        {
            plane.position = new Vector3(Model.Distance, Model.Height, 0f);
            plane.rotation = Quaternion.Euler(0f, 0f, Model.Pitch);
            boostPickup.position = Session.Course.Pickup;
            obstacle.position = Session.Course.Obstacle;
            Vector3 cameraPosition = new Vector3(Model.Distance + 7f, Mathf.Max(7.5f, Model.Height + 2f), -24f);
            flightCamera.transform.position = snap || Model.State == FlightModel.Phase.Ready
                ? cameraPosition : Vector3.Lerp(flightCamera.transform.position, cameraPosition, 1f - Mathf.Exp(-cameraFollowSharpness * Time.deltaTime));
            terrain.position = new Vector3(Model.Distance, 0f, 0f);
            scenery.position = new Vector3(Mathf.Floor(Model.Distance / 120f) * 120f, 0f, 0f);
            int first = Mathf.FloorToInt((Model.Distance - 35f) / 5f);
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
            if (ResearchOpen)
            {
                DrawResearch(width, height);
                GUI.matrix = previous; return;
            }
            Fill(new Rect(36, 32, 6, 58), Mint);
            GUI.Label(new Rect(58, 29, 360, 42), "PAPER / FLIGHT", titleStyle);
            GUI.Label(new Rect(60, 69, 360, 25), "01   /   THE FIRST GLIDE", smallStyle);
            GUI.Label(new Rect(width - 245, 36, 210, 23), bestText, smallStyle);
            Fill(new Rect(40, 111, width - 80, 1), new Color(0.82f, 0.85f, 0.84f));

            GUI.Label(new Rect(42, 135, 250, 24), "DISTANCE / m", smallStyle);
            GUI.Label(new Rect(40, 159, 280, 58), distanceText, valueStyle);
            GUI.Label(new Rect(290, 135, 180, 24), "AIR SPEED / m/s", smallStyle);
            GUI.Label(new Rect(288, 169, 180, 38), speedText, titleStyle);
            GUI.Label(new Rect(480, 135, 180, 24), "ALTITUDE / m", smallStyle);
            GUI.Label(new Rect(478, 169, 180, 38), altitudeText, titleStyle);
            string badge = Model.State == FlightModel.Phase.Ready ? "READY TO FLY" : Model.State == FlightModel.Phase.Landed
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

            if (Model.State != FlightModel.Phase.Flying)
            {
                float left = width * 0.5f - 240f;
                Fill(new Rect(left, height * 0.48f - 1, 480, 130), Ink);
                Fill(new Rect(left + 1, height * 0.48f, 478, 128), new Color(0.98f, 0.99f, 0.98f));
                GUI.Label(new Rect(left, height * 0.48f + 14, 480, 45),
                    Model.State == FlightModel.Phase.Ready ? "A little paper. A long way." : "Every flight starts again.", centerStyle);
                GUI.Label(new Rect(left, height * 0.48f + 71, 480, 30),
                    Model.State == FlightModel.Phase.Ready ? "LEFT CLICK  /  LAUNCH" : "LEFT CLICK OR R  /  RESET", badgeStyle);
            }
            Fill(new Rect(40, height - 96, width - 80, 1), new Color(0.82f, 0.85f, 0.84f));
            GUI.Label(new Rect(42, height - 74, width - 84, 30),
                Model.State == FlightModel.Phase.Flying && !Model.ControlArmed
                    ? "Release the launch click, then hold LEFT MOUSE to climb."
                    : "HOLD LEFT MOUSE  /  PITCH UP       RELEASE  /  PITCH DOWN       R  /  RESET", bodyStyle);
            GUI.Label(new Rect(42, height - 40, width - 84, 24), "노란 마름모는 가속 아이템입니다. 삼각 장애물을 피해 날아가세요.", smallStyle);
            GUI.matrix = previous;
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
}
