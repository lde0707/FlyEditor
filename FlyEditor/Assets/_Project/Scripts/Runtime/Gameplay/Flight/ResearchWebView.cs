using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace FlyEditor.Flight
{
    public sealed class ResearchWebView
    {
        private enum Phase { Browsing, Purchasing, PurchasedFeedback }
        private readonly PaperFlightController owner;
        private readonly ResearchWebAssets assets;
        public ResearchWebNavigation Navigation { get; } = new ResearchWebNavigation();
        private Phase phase;
        private int selectedIndex = -1, feedbackIndex = -1, panControl;
        private float feedbackStarted;
        private GUIStyle heading, body, small, value, button;
        private string title, stateText, description, currentValue, costText, buttonText, pointsText, growthText;
        private string zoomText = "100%";
        private bool previewAll;
        private readonly Texture2D[] edgeTextures;
        private readonly Rect[] edgeBounds;
        private readonly int[] edgeParents, edgeChildren;
        private Texture2D focusMask;
        private Color32[] focusPixels;
        private Vector2[] focusOutline;
        private static readonly Color Ink = new Color(.20f, .23f, .20f);
        private const float CandidateOpacity = .5f;
        private static readonly Rect Viewport = new Rect(32, 164, 1408, 790);
        public bool PreviewAll => previewAll;
        public string SelectedId => assets != null && assets.nodes != null && selectedIndex >= 0
            && selectedIndex < assets.nodes.Length ? assets.nodes[selectedIndex].id : "";

        public ResearchWebView(PaperFlightController controller, ResearchWebAssets skin)
        {
            owner = controller; assets = skin;
            int count = assets != null && assets.nodes != null ? assets.nodes.Length : 0;
            var parents = new List<int>(); var children = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var prerequisites = assets.nodes[i].prerequisiteIndices;
                if (prerequisites == null || prerequisites.Length == 0)
                { parents.Add(-1); children.Add(i); }
                else foreach (int parent in prerequisites)
                { parents.Add(parent); children.Add(i); }
            }
            edgeParents = parents.ToArray(); edgeChildren = children.ToArray();
            edgeTextures = new Texture2D[children.Count]; edgeBounds = new Rect[children.Count];
            for (int i = 0; i < children.Count; i++) BuildEdge(i);
            Refresh(); FitVisible();
        }
        // Axis-aligned textures remain inside GUI.BeginGroup clipping at every pan/zoom.
        // Build once instead of rotating GUI.matrix inside a clipped group.
        private void BuildEdge(int index)
        {
            var n = assets.nodes[edgeChildren[index]];
            int parentIndex = edgeParents[index];
            var parent = parentIndex < 0 ? null : assets.nodes[parentIndex];
            Vector2 parentCenter = parent == null ? Vector2.zero : parent.position;
            float parentSize = parent == null ? assets.rootFrameSize : parent.frameSize;
            var parentOutline = parent == null ? assets.rootOutline : parent.outline;
            Vector2 from = ResearchWebGeometry.Attachment(parentCenter, n.position, parentSize, parentOutline);
            Vector2 to = ResearchWebGeometry.Attachment(n.position, parentCenter, n.frameSize, n.outline);
            bool dashed = parent != null && parent.family != n.family;
            int dashCount = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(from, to) / 22f));
            float dashUnits = 2 * dashCount - 1;
            Vector2 delta = to - from;
            float left = Mathf.Min(from.x, to.x) - 4, top = Mathf.Min(from.y, to.y) - 4;
            int width = Mathf.CeilToInt(Mathf.Abs(delta.x)) + 8, height = Mathf.CeilToInt(Mathf.Abs(delta.y)) + 8;
            edgeBounds[index] = new Rect(left, top, width, height);
            var pixels = new Color32[width * height]; float lengthSquared = delta.sqrMagnitude;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 point = new Vector2(left + x + .5f, top + y + .5f);
                    float t = lengthSquared > 0 ? Mathf.Clamp01(Vector2.Dot(point - from, delta) / lengthSquared) : 0;
                    float distance = Vector2.Distance(point, from + delta * t);
                    float coverage = Mathf.Clamp01(2f - distance);
                    if (coverage > 0 && (ResearchWebGeometry.Contains(parentOutline,
                        (point - parentCenter) / ResearchWebGeometry.TextureSize(parentSize))
                        || ResearchWebGeometry.Contains(n.outline,
                            (point - n.position) / ResearchWebGeometry.TextureSize(n.frameSize))))
                        coverage = 0;
                    if (dashed && Mathf.FloorToInt(t * dashUnits) % 2 != 0) coverage = 0;
                    byte alpha = (byte)(coverage * 255);
                    pixels[(height - 1 - y) * width + x] = new Color32(255, 255, 255, alpha);
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Research connection", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true); edgeTextures[index] = texture;
        }
        public void Dispose()
        {
            CancelPan();
            if (focusMask != null) Object.Destroy(focusMask);
            for (int i = 0; i < edgeTextures.Length; i++) if (edgeTextures[i] != null) Object.Destroy(edgeTextures[i]);
        }
        public bool IsVisible(int index) => assets != null && assets.nodes != null
            && index >= 0 && index < assets.nodes.Length
            && (previewAll || assets.IsVisible(index, owner.Session?.Research));
        public int VisibleCount
        {
            get
            {
                int count = 0;
                if (assets != null && assets.nodes != null)
                    for (int i = 0; i < assets.nodes.Length; i++) if (IsVisible(i)) count++;
                return count;
            }
        }
        public bool Select(string id)
        {
            if (assets == null || assets.nodes == null) return false;
            for (int i = 0; i < assets.nodes.Length; i++)
                if (assets.nodes[i].id == id && IsVisible(i))
                { SelectIndex(i); return true; }
            return false;
        }
        private void SelectIndex(int index)
        {
            selectedIndex = index;
            ChangePhase(Phase.Browsing);
            Refresh();
        }
        internal void HandleKeyboard(Keyboard keyboard)
        {
            if (keyboard == null || assets == null || assets.nodes == null) return;
            Vector2 direction = Vector2.zero;
            if (keyboard.upArrowKey.wasPressedThisFrame) direction = Vector2.down; // GUI y grows downward.
            else if (keyboard.downArrowKey.wasPressedThisFrame) direction = Vector2.up;
            else if (keyboard.leftArrowKey.wasPressedThisFrame) direction = Vector2.left;
            else if (keyboard.rightArrowKey.wasPressedThisFrame) direction = Vector2.right;
            if (direction != Vector2.zero) MoveSelection(direction);
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                && phase == Phase.Browsing && IsVisible(selectedIndex))
                TryPurchase(assets.nodes[selectedIndex].id);
        }
        private void MoveSelection(Vector2 direction)
        {
            Vector2 origin = IsVisible(selectedIndex) ? assets.nodes[selectedIndex].position : Vector2.zero;
            int best = -1;
            float bestScore = float.PositiveInfinity;
            for (int i=0;i<assets.nodes.Length;i++)
            {
                if (i == selectedIndex || !IsVisible(i)) continue;
                Vector2 delta = assets.nodes[i].position - origin;
                float forward = Vector2.Dot(delta,direction);
                if (forward <= 0f) continue;
                // Prefer nearby nodes along the requested heading.
                float score = delta.sqrMagnitude / forward;
                if (score < bestScore) { best = i; bestScore = score; }
            }
            if (best < 0) return;
            SelectIndex(best);
            var node = assets.nodes[best];
            Navigation.EnsureVisible(node.position,ResearchWebGeometry.TextureSize(node.frameSize)*.5f,Viewport.size);
        }
        public bool TryPurchase(string id)
        {
            if (!owner.ResearchOpen || previewAll || phase == Phase.Purchasing || assets == null) return false;
            for (int i = 0; i < assets.nodes.Length; i++)
            {
                if (assets.nodes[i].id != id || !assets.CanBuy(i, owner.Session.Research)) continue;
                selectedIndex = i; ChangePhase(Phase.Purchasing);
                bool bought = owner.CommitResearchPurchase(i);
                if (bought)
                {
                    feedbackIndex = i;
                    RevealChildren(i);
                }
                ChangePhase(bought ? Phase.PurchasedFeedback : Phase.Browsing); Refresh();
                return bought;
            }
            return false;
        }
        private void RevealChildren(int parent)
        {
            for (int i = 0; i < assets.nodes.Length; i++)
            {
                var child = assets.nodes[i];
                if (System.Array.IndexOf(child.prerequisiteIndices, parent) < 0 || !IsVisible(i) || ResearchWebAssets.IsOwned(child, owner.Session.Research)) continue;
                Vector2 point = Navigation.WorldToView(child.position, Viewport.size);
                float margin = child.frameSize * (128f / 192f) * Navigation.Zoom;
                if (point.x - margin < 0 || point.y - margin < 0
                    || point.x + margin > Viewport.width || point.y + margin > Viewport.height)
                {
                    FitVisible();
                    return;
                }
            }
        }
        // Compatibility entry point still selects a distinct, unowned one-time node.
        public bool TryPurchaseNext(ResearchNode runtime)
        {
            if (assets == null) return false;
            for (int i = 0; i < assets.nodes.Length; i++)
                if (assets.nodes[i].runtimeIndex == (int)runtime && assets.CanBuy(i, owner.Session.Research))
                    return TryPurchase(assets.nodes[i].id);
            return false;
        }
        public void SetPreview(bool enabled)
        {
            CancelPan(); previewAll = enabled; ChangePhase(Phase.Browsing); Refresh();
            FitVisible();
        }
        private void ChangePhase(Phase next)
        {
            if (phase == next) return;
            ExitPhase(); phase = next; EnterPhase();
        }
        private void ExitPhase() { if (phase == Phase.PurchasedFeedback) feedbackIndex = -1; }
        private void EnterPhase() { if (phase == Phase.PurchasedFeedback) feedbackStarted = Time.unscaledTime; }
        public void Tick(float now)
        {
            if (phase == Phase.PurchasedFeedback && now - feedbackStarted >= .35f)
            { ChangePhase(Phase.Browsing); Refresh(); }
        }
        public void Refresh()
        {
            if (assets == null || assets.nodes == null || assets.nodes.Length == 0 || owner.Session == null) return;
            pointsText = "연구점  " + owner.Session.Research.Points;
            var research = owner.Session.Research;
            growthText = "기체 " + (research.GrowthTier + 1) + "단계   ·   "
                + (research.NextGrowthCount > 0 ? "다음 성장 " + research.OwnedCount + " / " + research.NextGrowthCount
                    + "개   ·   다음 발사 배율 ×" + research.NextGrowthSpeed.ToString("0.##") : "최종 단계");
            if (!IsVisible(selectedIndex)) { selectedIndex = -1; return; }
            var n = assets.nodes[selectedIndex];
            bool owned = ResearchWebAssets.IsOwned(n, owner.Session.Research);
            title = n.title; description = n.summary;
            stateText = previewAll ? "전체 설계 미리보기 · 각 노드 1회 구매"
                : owned ? "구매 완료" : "구매 후보 · 1회 구매";
            currentValue = n.benefit;
            costText = owned && !previewAll ? "구매 완료" : n.costs[0] + " 연구점";
            buttonText = previewAll ? "미리보기 · 구매 불가" : owned ? "구매 완료"
                : assets.CanBuy(selectedIndex, owner.Session.Research) ? "해금하기" : "연구점 부족";
        }
        private void EnsureStyles(Font font)
        {
            if (heading != null) return;
            heading = MakeStyle(font, 32, FontStyle.Bold); heading.wordWrap = true;
            body = MakeStyle(font, 22); body.wordWrap = true;
            small = MakeStyle(font, 18); small.wordWrap = true;
            value = MakeStyle(font, 24, FontStyle.Bold); value.wordWrap = true;
            button = MakeStyle(font, 23, FontStyle.Bold); button.alignment = TextAnchor.MiddleCenter;
        }
        private static GUIStyle MakeStyle(Font font, int size, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight };
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = Ink;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = Ink;
            return style;
        }
        public void FitVisible()
        {
            if (assets == null) return;
            float rootHalf = ResearchWebGeometry.TextureSize(assets.rootFrameSize) * .5f;
            Rect bounds = new Rect(-rootHalf, -rootHalf, rootHalf * 2, rootHalf * 2);
            if (assets != null && assets.nodes != null)
                for (int i = 0; i < assets.nodes.Length; i++)
                {
                    if (!IsVisible(i)) continue;
                    var n = assets.nodes[i]; float half = n.frameSize * (256f / 192f) * .5f;
                    bounds.xMin = Mathf.Min(bounds.xMin, n.position.x - half);
                    bounds.yMin = Mathf.Min(bounds.yMin, n.position.y - half);
                    bounds.xMax = Mathf.Max(bounds.xMax, n.position.x + half);
                    bounds.yMax = Mathf.Max(bounds.yMax, n.position.y + half);
                }
            Navigation.Fit(bounds, Viewport.size); CacheZoom();
        }
        private void CacheZoom() { zoomText = Mathf.RoundToInt(Navigation.Zoom * 100) + "%"; }
        private void CancelPan()
        {
            Navigation.Cancel();
            if (panControl != 0 && GUIUtility.hotControl == panControl) GUIUtility.hotControl = 0;
            panControl = 0;
        }
        private void HandleNavigation()
        {
            Event e = Event.current;
            int control = GUIUtility.GetControlID(753104, FocusType.Passive);
            Rect area = new Rect(Vector2.zero, Viewport.size);
            if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition))
            { Navigation.ZoomAt(Navigation.Zoom * Mathf.Pow(.9f, e.delta.y), e.mousePosition, Viewport.size); CacheZoom(); e.Use(); }
            if (e.type == EventType.MouseDown && (e.button == 1 || e.button == 2) && area.Contains(e.mousePosition))
            { panControl = control; GUIUtility.hotControl = control; Navigation.BeginPan(); e.Use(); }
            if (panControl == control && GUIUtility.hotControl == control)
            {
                if (e.type == EventType.MouseDrag) { Navigation.Drag(e.delta); e.Use(); }
                if (e.rawType == EventType.MouseUp) { CancelPan(); e.Use(); }
            }
            if (Navigation.State == ResearchWebNavigation.Phase.Panning && GUIUtility.hotControl != panControl) CancelPan();
        }
        public void Draw(Font font)
        {
            EnsureStyles(font);
            if (assets == null || assets.nodes == null || assets.nodes.Length == 0) return;
            // Update handles these keys. Keep IMGUI buttons from activating a second action.
            var e = Event.current;
            if (e.isKey && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow ||
                e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)) e.Use();
            Matrix4x4 previous = GUI.matrix; Color previousColor = GUI.color;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1920f * scale) * .5f,
                (Screen.height - 1080f * scale) * .5f, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.color = Color.white;
            Texture(new Rect(0, 0, 1920, 1080), assets.background);
            heading.normal.textColor = Ink;
            GUI.Label(new Rect(48, 32, 700, 56), "FlyEditor / 비행 연구", heading);
            GUI.Label(new Rect(48, 100, 1300, 38), previewAll ? "전체 98개 노드 · 설계 미리보기 · 구매 불가"
                : "연결된 후보 중 원하는 갈래를 선택하세요.", small);
            if (previewAll && GUI.Button(new Rect(1090, 94, 326, 44), "실제 구매 후보만 보기", button)) SetPreview(false);
            Texture(new Rect(1500, 42, 28, 28), assets.currency);
            GUI.Label(new Rect(1544, 35, 320, 40), pointsText, body);
            GUI.BeginGroup(Viewport);
            HandleNavigation(); DrawGraph();
            GUI.EndGroup();
            DrawPanel(); DrawToolbar();
            GUI.matrix = previous; GUI.color = previousColor;
        }
        private void DrawGraph()
        {
            float zoom = Navigation.Zoom;
            for (int i = 0; i < edgeTextures.Length; i++)
            {
                int child = edgeChildren[i], parent = edgeParents[i];
                if (!IsVisible(child)) continue;
                if (parent >= 0 && (!IsVisible(parent) || (!previewAll &&
                    !ResearchWebAssets.IsOwned(assets.nodes[parent], owner.Session.Research)))) continue;
                var n = assets.nodes[child];
                Color color = Color.Lerp(assets.families[n.family].accent, Color.white, child == selectedIndex ? .05f : .25f);
                color.a = ResearchWebAssets.IsOwned(n, owner.Session.Research) ? 1f : CandidateOpacity;
                Rect bounds = edgeBounds[i]; Vector2 topLeft = Navigation.WorldToView(bounds.position, Viewport.size);
                GUI.color = color;
                GUI.DrawTexture(new Rect(topLeft, bounds.size * zoom), edgeTextures[i], ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }
            string hoverTitle = null;
            DrawTile(Vector2.zero, assets.rootFrameSize, assets.root);
            for (int i = 0; i < assets.nodes.Length; i++)
            {
                if (!IsVisible(i)) continue;
                var n = assets.nodes[i];
                int state = ResearchWebAssets.IsOwned(n, owner.Session.Research) ? 1 : 0;
                // Opacity communicates ownership; it never disables the node's hit target.
                GUI.color = new Color(1, 1, 1, state == 1 ? 1f : CandidateOpacity);
                Rect tile = DrawTile(n.position, n.frameSize, n.normal[state]);
                GUI.color = Color.white;
                if (phase == Phase.PurchasedFeedback && feedbackIndex == i && Time.unscaledTime - feedbackStarted < .21f)
                    Texture(tile, n.selection);
                Event e = Event.current;
                Vector2 local = (e.mousePosition - tile.center) / tile.width;
                if (Navigation.State != ResearchWebNavigation.Phase.Idle || !tile.Contains(e.mousePosition)
                    || !ResearchWebGeometry.Contains(n.outline, local)) continue;
                hoverTitle = n.title;
                if (e.type == EventType.MouseDown && e.button == 0)
                { SelectIndex(i); e.Use(); }
            }
            if (Event.current.type == EventType.Repaint && IsVisible(selectedIndex))
                DrawFocus(assets.nodes[selectedIndex]);
            if (hoverTitle != null && Event.current.type == EventType.Repaint)
            {
                Vector2 mouse = Event.current.mousePosition;
                Rect label = new Rect(Mathf.Clamp(mouse.x + 16f, 0, Viewport.width - 310),
                    Mathf.Clamp(mouse.y + 16f, 0, Viewport.height - 36), 310, 36);
                GUI.DrawTexture(label, Texture2D.whiteTexture);
                GUI.Label(new Rect(label.x + 8,label.y + 4,294,28), hoverTitle, small);
            }
        }
        private void DrawFocus(ResearchWebAssets.Node node)
        {
            int size = Mathf.RoundToInt(ResearchWebGeometry.TextureSize(node.frameSize)*Navigation.Zoom);
            UpdateFocusMask(node,size);
            // Keep line weights and corner spacing independent of web zoom.
            Vector2 center=Navigation.WorldToView(node.position,Viewport.size);
            float width=focusMask.width;
            GUI.color=assets.families[node.family].dark;
            GUI.DrawTexture(new Rect(center.x-width*.5f,center.y-width*.5f,width,width),focusMask);
            GUI.color=Color.white;
        }
        private void UpdateFocusMask(ResearchWebAssets.Node node,int size)
        {
            int width = size + 22;
            var outline = node.outline != null && node.outline.Length >= 3 ? node.outline : ResearchWebGeometry.Square;
            if (focusMask == null || focusMask.width != width)
            {
                if (focusMask != null) Object.Destroy(focusMask);
                focusMask = new Texture2D(width,width,TextureFormat.RGBA32,false)
                    { name = "Research focus", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                focusPixels = new Color32[width*width];
                focusOutline = null;
            }
            if (focusOutline != outline)
            {
                focusOutline = outline;
                System.Array.Clear(focusPixels,0,focusPixels.Length);
                Vector2 middle = Vector2.one*(width*.5f);
                for (int i=0;i<outline.Length;i++)
                {
                    Vector2 a=outline[i], b=outline[(i+1)%outline.Length];
                    FocusStroke(middle+a*(size+4),middle+b*(size+4),1.25f);
                    Vector2 start=middle+a*(size+17), end=middle+b*(size+17);
                    Vector2 leg=Vector2.ClampMagnitude((end-start)*.32f,14f);
                    FocusStroke(start,start+leg,2.2f);
                    FocusStroke(end-leg,end,2.2f);
                }
                focusMask.SetPixels32(focusPixels);
                focusMask.Apply(false);
            }
        }
        private void FocusStroke(Vector2 from,Vector2 to,float radius)
        {
            int width=focusMask.width;
            Vector2 delta=to-from;
            float lengthSquared=delta.sqrMagnitude;
            if (lengthSquared<=0f) return;
            float pad=radius+1f;
            int x0=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(from.x,to.x)-pad));
            int x1=Mathf.Min(width-1,Mathf.CeilToInt(Mathf.Max(from.x,to.x)+pad));
            int y0=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(from.y,to.y)-pad));
            int y1=Mathf.Min(width-1,Mathf.CeilToInt(Mathf.Max(from.y,to.y)+pad));
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
            {
                Vector2 point=new Vector2(x+.5f,y+.5f);
                float t=Mathf.Clamp01(Vector2.Dot(point-from,delta)/lengthSquared);
                float distance=Vector2.Distance(point,from+t*delta);
                byte alpha=(byte)(255*Mathf.Clamp01(radius+.5f-distance));
                int index=(width-1-y)*width+x;
                if (alpha>focusPixels[index].a) focusPixels[index]=new Color32(255,255,255,alpha);
            }
        }
        private Rect DrawTile(Vector2 world, float frameSize, Texture2D texture)
        {
            Vector2 center = Navigation.WorldToView(world, Viewport.size);
            float size = frameSize * (256f / 192f) * Navigation.Zoom;
            Rect box = new Rect(center.x - size * .5f, center.y - size * .5f, size, size);
            Texture(box, texture); return box;
        }
        private void DrawToolbar()
        {
            button.normal.textColor = Ink;
            if (GUI.Button(new Rect(48, 980, 52, 44), "-", button))
            { Navigation.ZoomAt(Navigation.Zoom / 1.2f, Viewport.size * .5f, Viewport.size); CacheZoom(); }
            GUI.Label(new Rect(104, 980, 85, 44), zoomText, button);
            if (GUI.Button(new Rect(192, 980, 52, 44), "+", button))
            { Navigation.ZoomAt(Navigation.Zoom * 1.2f, Viewport.size * .5f, Viewport.size); CacheZoom(); }
            if (GUI.Button(new Rect(264, 980, 130, 44), "전체 보기", button)) FitVisible();
            if (GUI.Button(new Rect(408, 980, 110, 44), "100%", button)) { Navigation.Reset(); CacheZoom(); }
            GUI.Label(new Rect(48, 1032, 1360, 35), previewAll
                ? "방향키로 선택   ·   휠로 확대/축소   ·   오른쪽/가운데 드래그로 이동"
                : "방향키로 선택   ·   Enter로 해금   ·   휠로 확대/축소   ·   오른쪽/가운데 드래그로 이동", small);
            GUI.Label(new Rect(556,986,570,40),growthText,small);
            var back = new Rect(1150, 980, 266, 50);
            Texture(back, assets.backButton);
            if (GUI.Button(back, "비행으로 [Esc]", button)) owner.CloseResearch();
        }
        private void DrawPanel()
        {
            if (selectedIndex < 0)
            {
                Texture(new Rect(1472, 132, 400, 900), assets.panel);
                GUI.Label(new Rect(1500, 420, 344, 90), "노드를 선택하세요.", body);
                return;
            }
            var n = assets.nodes[selectedIndex]; var family = assets.families[n.family];
            const float x = 1472, y = 132;
            Texture(new Rect(x, y, 400, 900), family.panel != null ? family.panel : assets.panel);
            Texture(new Rect(x + 128, y + 58, 144, 144), n.icon);
            heading.normal.textColor = family.dark;
            GUI.Label(new Rect(x + 28, y + 240, 344, 94), title, heading);
            GUI.Label(new Rect(x + 28, y + 344, 344, 64), stateText, small);
            Segment(new Vector2(x + 28, y + 418), new Vector2(x + 372, y + 418), 1, family.accent);
            value.normal.textColor = family.dark;
            GUI.Label(new Rect(x + 28, y + 442, 344, 70), currentValue, value);
            GUI.Label(new Rect(x + 28, y + 525, 344, 180), description, body);
            GUI.Label(new Rect(x + 28, y + 720, 344, 45), costText, body);
            bool owned = ResearchWebAssets.IsOwned(n, owner.Session.Research);
            bool canBuy = !previewAll && phase == Phase.Browsing && assets.CanBuy(selectedIndex, owner.Session.Research);
            var texture = !previewAll && owned ? family.maximum : family.ready;
            if (phase == Phase.PurchasedFeedback) texture = family.success;
            var rect = new Rect(x + 28, y + 780, 344, 64.5f);
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (canBuy && mouse != null && mouse.leftButton.isPressed && rect.Contains(Event.current.mousePosition)) texture = family.pressed;
            GUI.color = canBuy || owned ? Color.white : new Color(1, 1, 1, .6f);
            Texture(rect, texture); GUI.color = Color.white;
            button.normal.textColor = family.dark; GUI.Label(rect, buttonText, button);
            bool enabled = GUI.enabled; GUI.enabled = canBuy;
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) TryPurchase(n.id);
            GUI.enabled = enabled;
            if (!previewAll && !string.IsNullOrEmpty(owner.Session.Research.LastError))
                GUI.Label(new Rect(x + 28, y + 850, 344, 45), owner.Session.Research.LastError, small);
        }
        private static void Texture(Rect rect, Texture2D texture)
        { if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true); }
        private static void Segment(Vector2 from, Vector2 to, float thickness, Color color)
        {
            float length = Vector2.Distance(from, to); if (length < .01f) return;
            var previous = GUI.matrix; GUI.color = color;
            GUI.matrix = previous * Matrix4x4.TRS(new Vector3(from.x, from.y, 0),
                Quaternion.Euler(0, 0, Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg), Vector3.one);
            GUI.DrawTexture(new Rect(0, -thickness * .5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = previous; GUI.color = Color.white;
        }
    }
}
