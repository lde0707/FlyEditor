using UnityEngine;

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
        private string title, stateText, description, currentValue, costText, buttonText, pointsText;
        private string zoomText = "100%";
        private bool previewAll;
        private readonly Texture2D[] edgeTextures;
        private readonly Rect[] edgeBounds;
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
            edgeTextures = new Texture2D[count]; edgeBounds = new Rect[count];
            for (int i = 0; i < count; i++) BuildEdge(i);
            Refresh(); FitVisible();
        }
        // Axis-aligned textures remain inside GUI.BeginGroup clipping at every pan/zoom.
        // Build once instead of rotating GUI.matrix inside a clipped group.
        private void BuildEdge(int index)
        {
            var n = assets.nodes[index];
            Vector2 from = n.parent < 0 ? Vector2.zero : assets.nodes[n.parent].position, to = n.position;
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
                    byte alpha = (byte)(Mathf.Clamp01(2f - distance) * 255);
                    pixels[(height - 1 - y) * width + x] = new Color32(255, 255, 255, alpha);
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Research connection", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true); edgeTextures[index] = texture;
        }
        public void Dispose()
        {
            CancelPan();
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
                { selectedIndex = i; ChangePhase(Phase.Browsing); Refresh(); return true; }
            return false;
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
                if (child.parent != parent || !IsVisible(i) || ResearchWebAssets.IsOwned(child, owner.Session.Research)) continue;
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
            if (!IsVisible(selectedIndex)) { selectedIndex = -1; return; }
            var n = assets.nodes[selectedIndex];
            bool owned = ResearchWebAssets.IsOwned(n, owner.Session.Research);
            title = n.title; description = n.summary;
            stateText = previewAll ? "전체 설계 미리보기 · 각 노드 1회 구매"
                : owned ? "구매 완료" : "구매 후보 · 1회 구매";
            currentValue = n.unlocksAbility ? "새 능력 해금" : "수치 강화";
            if (!previewAll && n.runtimeIndex >= 0)
            {
                var effective = owner.GetResearchTuning();
                switch (n.runtimeIndex)
                {
                    case 0: currentValue = "현재 발사 속도  " + effective.launchSpeed.ToString("F2") + " m/s"; break;
                    case 1: currentValue = "현재 침하  " + effective.sink.ToString("F4") + " m/s"; break;
                    case 3: currentValue = "현재 공기 저항  " + effective.drag.ToString("F5"); break;
                    case 4: currentValue = "현재 상승 저항  " + effective.climbDrag.ToString("F5"); break;
                    case 5: currentValue = "아이템 가속  +" + effective.effects.boostSpeed.ToString("F1") + " m/s"; break;
                    case 6: currentValue = effective.effects.diveEnabled ? "하강 가속  " + effective.effects.diveDelay.ToString("F2") + "초 후" : "하강 가속 해금"; break;
                    case 7: currentValue = "자세 회복  " + effective.effects.recoveryUses + "회 / 비행"; break;
                    case 8: currentValue = "최대 내구  " + effective.effects.durability; break;
                    case 9: currentValue = "충돌 감속  " + effective.effects.collisionSpeedLoss.ToString("P0"); break;
                    case 10: currentValue = "종이 방패  " + effective.effects.shieldUses + "회 / 비행"; break;
                    case 11: currentValue = "수집 반경  " + effective.effects.magnetRadius.ToString("F1") + " m"; break;
                }
            }
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
            value = MakeStyle(font, 24, FontStyle.Bold);
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
            Rect bounds = new Rect(-112, -112, 224, 224);
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
            Matrix4x4 previous = GUI.matrix; Color previousColor = GUI.color;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1920f * scale) * .5f,
                (Screen.height - 1080f * scale) * .5f, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.color = Color.white;
            Texture(new Rect(0, 0, 1920, 1080), assets.background);
            heading.normal.textColor = Ink;
            GUI.Label(new Rect(48, 32, 700, 56), "FlyEditor / 비행 연구", heading);
            GUI.Label(new Rect(48, 100, 1300, 38), previewAll ? "전체 33개 노드 · 설계 미리보기 · 구매 불가"
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
            for (int i = 0; i < assets.nodes.Length; i++)
            {
                if (!IsVisible(i)) continue;
                var n = assets.nodes[i];
                if (n.parent >= 0 && !IsVisible(n.parent)) continue;
                Color color = Color.Lerp(assets.families[n.family].accent, Color.white, i == selectedIndex ? .05f : .40f);
                color.a = ResearchWebAssets.IsOwned(n, owner.Session.Research) ? 1f : CandidateOpacity;
                Rect bounds = edgeBounds[i]; Vector2 topLeft = Navigation.WorldToView(bounds.position, Viewport.size);
                GUI.color = color;
                GUI.DrawTexture(new Rect(topLeft, bounds.size * zoom), edgeTextures[i], ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }
            DrawTile(Vector2.zero, 160, assets.root);
            for (int i = 0; i < assets.nodes.Length; i++)
            {
                if (!IsVisible(i)) continue;
                var n = assets.nodes[i];
                int state = ResearchWebAssets.IsOwned(n, owner.Session.Research) ? 1 : 0;
                var sprite = i == selectedIndex ? n.selected[state] : n.normal[state];
                // Opacity communicates ownership; it never disables the node's hit target.
                GUI.color = new Color(1, 1, 1, state == 1 ? 1f : CandidateOpacity);
                Rect tile = DrawTile(n.position, n.frameSize, sprite);
                GUI.color = Color.white;
                if (state == 0 && i == selectedIndex) Texture(tile, assets.families[n.family].feedback[0]);
                if (phase == Phase.PurchasedFeedback && feedbackIndex == i && Time.unscaledTime - feedbackStarted < .21f)
                    Texture(tile, assets.families[n.family].feedback[Mathf.Clamp((int)((Time.unscaledTime - feedbackStarted) / .07f), 0, 2)]);
                Vector2 center = Navigation.WorldToView(n.position, Viewport.size);
                float size = n.frameSize * zoom;
                if (Navigation.State == ResearchWebNavigation.Phase.Idle
                    && GUI.Button(new Rect(center.x - size * .5f, center.y - size * .5f, size, size), GUIContent.none, GUIStyle.none))
                { selectedIndex = i; ChangePhase(Phase.Browsing); Refresh(); }
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
            GUI.Label(new Rect(48, 1032, 1050, 35), "휠: 확대/축소   ·   오른쪽/가운데 버튼 드래그: 이동", small);
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
