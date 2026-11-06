using UnityEngine;

namespace FlyEditor.Flight
{
    public static class ResearchWebGeometry
    {
        public static float TextureSize(float frameSize) => frameSize * (256f / 192f);
        public static readonly Vector2[] Square =
        {
            new Vector2(-.375f, -.375f), new Vector2(.375f, -.375f),
            new Vector2(.375f, .375f), new Vector2(-.375f, .375f)
        };
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        // Convex silhouettes use the same normalized vertices as the drawn PNGs.
        public static bool Contains(Vector2[] outline, Vector2 local)
        {
            var points = outline != null && outline.Length >= 3 ? outline : Square;
            float sign = 0;
            for (int i = 0; i < points.Length; i++)
            {
                float side = Cross(points[(i + 1) % points.Length] - points[i], local - points[i]);
                if (Mathf.Abs(side) < .000001f) continue;
                if (sign != 0 && sign * side < 0) return false;
                sign = side;
            }
            return true;
        }
        public static Vector2 Attachment(Vector2 center, Vector2 toward, float frameSize, Vector2[] outline)
        {
            var points = outline != null && outline.Length >= 3 ? outline : Square;
            Vector2 ray = (toward - center).normalized;
            float hit = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i], edge = points[(i + 1) % points.Length] - a;
                float denominator = Cross(ray, edge);
                if (Mathf.Abs(denominator) < .000001f) continue;
                float t = Cross(a, edge) / denominator, u = Cross(a, ray) / denominator;
                if (t >= 0 && u >= -.00001f && u <= 1.00001f) hit = Mathf.Min(hit, t);
            }
            return float.IsInfinity(hit) ? center : center + ray * (hit * TextureSize(frameSize));
        }
    }
}
