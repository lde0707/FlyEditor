using UnityEngine;

namespace FlyEditor.Flight
{
    // Independent of screen pixels. View coordinates use the 1920x1080 reference canvas.
    public sealed class ResearchWebNavigation
    {
        public enum Phase { Idle, Panning }
        public const float MinimumZoom = .25f, MaximumZoom = 1.5f;
        public float Zoom { get; private set; } = 1f;
        public Vector2 Pan { get; private set; }
        public Phase State { get; private set; }
        public Vector2 WorldToView(Vector2 world, Vector2 size) => size * .5f + Pan + world * Zoom;
        public Vector2 ViewToWorld(Vector2 point, Vector2 size) => (point - size * .5f - Pan) / Zoom;
        public void ZoomAt(float requested, Vector2 anchor, Vector2 size)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested)) return;
            Vector2 world = ViewToWorld(anchor, size);
            Zoom = Mathf.Clamp(requested, MinimumZoom, MaximumZoom);
            Pan = anchor - size * .5f - world * Zoom;
        }
        public void Fit(Rect bounds, Vector2 size)
        {
            Cancel();
            Zoom = Mathf.Clamp(Mathf.Min((size.x - 64) / Mathf.Max(1, bounds.width),
                (size.y - 64) / Mathf.Max(1, bounds.height)), MinimumZoom, 1f);
            Pan = -bounds.center * Zoom;
        }
        public void Reset() { Cancel(); Zoom = 1; Pan = Vector2.zero; }
        public void EnsureVisible(Vector2 world,float halfSize,Vector2 size)
        {
            Vector2 center = WorldToView(world,size);
            float margin = halfSize*Zoom + 8f;
            Pan += new Vector2(Mathf.Clamp(center.x,margin,size.x-margin),
                Mathf.Clamp(center.y,margin,size.y-margin)) - center;
        }
        public void BeginPan() { ChangePhase(Phase.Panning); }
        public void Drag(Vector2 delta) { if (State == Phase.Panning) Pan += delta; }
        public void Cancel() { ChangePhase(Phase.Idle); }
        private void ChangePhase(Phase next) { if (State != next) State = next; }
    }
}
