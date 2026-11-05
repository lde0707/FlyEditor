using UnityEngine;

namespace FlyEditor.Flight
{
    // Reuse one pickup and one obstacle. Advance each immediately after a hit or a miss.
    public sealed class FlightCourse
    {
        private int pickupIndex, obstacleIndex;
        public Vector2 Pickup { get; private set; }
        public Vector2 Obstacle { get; private set; }
        public int PickupsCollected { get; private set; }
        public int Collisions { get; private set; }
        public FlightCourse() { PlacePickup(); PlaceObstacle(); }
        private void PlacePickup() => Pickup = new Vector2(16 + pickupIndex * 32, 3.5f + (pickupIndex % 3) * 1.2f);
        private void PlaceObstacle() => Obstacle = new Vector2(29 + obstacleIndex * 38, 2.5f + (obstacleIndex % 3) * 1.4f);
        public void Tick(FlightModel plane, Vector2 from, float dt)
        {
            if (plane.State != FlightModel.Phase.Flying || dt <= 0) return;
            var to = new Vector2(plane.Distance, plane.Height);
            var effects = plane.Effects;
            if (effects.magnetRadius > 0 && DistanceSquared(Pickup, from, to) <= effects.magnetRadius * effects.magnetRadius)
                Pickup = Vector2.MoveTowards(Pickup, to, effects.magnetPull * dt);
            bool collected = DistanceSquared(Pickup, from, to) <= 1.1f * 1.1f;
            if (collected) { plane.CollectBoost(); PickupsCollected++; }
            float pickupEnd = 16 + pickupIndex * 32 + Mathf.Max(2f, effects.magnetRadius);
            if (collected || to.x > pickupEnd) { pickupIndex++; PlacePickup(); }

            bool struck = DistanceSquared(Obstacle, from, to) <= 1.3f * 1.3f;
            if (struck) { plane.StrikeObstacle(50f + 5f * (obstacleIndex % 3)); Collisions++; }
            if (struck || to.x > Obstacle.x + 2f) { obstacleIndex++; PlaceObstacle(); }
        }
        private static float DistanceSquared(Vector2 target, Vector2 from, Vector2 to)
        {
            Vector2 travel = to - from;
            float t = travel.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(target - from, travel) / travel.sqrMagnitude) : 0;
            return (target - from - travel * t).sqrMagnitude;
        }
    }
}
