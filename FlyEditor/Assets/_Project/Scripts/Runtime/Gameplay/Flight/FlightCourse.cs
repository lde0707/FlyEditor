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
        public int LongestChain { get; private set; }
        private int pickupChain;
        public FlightCourse() { Reset(); }
        internal void Reset()
        {
            pickupIndex = obstacleIndex = pickupChain = LongestChain = Collisions = PickupsCollected = 0;
            PlacePickup(); PlaceObstacle();
        }
        public void Step(FlightModel plane, float dt)
        {
            if (plane.State != FlightModel.Phase.Flying) return;
            var from = new Vector2(plane.Distance, plane.Height);
            plane.FixedTick(dt);
            Tick(plane, from, dt);
        }
        // The opening stretch stays low. Beyond 100 m, alternate ground, middle
        // and high lanes so both gliding and climbing continue to offer targets.
        private static float LaneHeight(float distance, int index)
            => Mathf.Clamp((distance - 100f) * .50f, 0f, 1200f) * (index % 3) * .5f;
        private void PlacePickup()
        {
            float x = 16 + pickupIndex * 32;
            Pickup = new Vector2(x, 3.5f + (pickupIndex % 3) * 1.2f + LaneHeight(x,pickupIndex));
        }
        private void PlaceObstacle()
        {
            float x = 29 + obstacleIndex * 38;
            Obstacle = new Vector2(x, 2.5f + (obstacleIndex % 3) * 1.4f + LaneHeight(x,obstacleIndex));
        }
        public void Tick(FlightModel plane, Vector2 from, float dt)
        {
            if (plane.State != FlightModel.Phase.Flying || dt <= 0) return;
            var to = new Vector2(plane.Distance, plane.Height);
            var effects = plane.Effects;
            if (effects.magnetRadius > 0 && DistanceSquared(Pickup, from, to) <= effects.magnetRadius * effects.magnetRadius)
                Pickup = Vector2.MoveTowards(Pickup, to, effects.magnetPull * dt);
            float radius = 1.1f + effects.extra.pickupRadius;
            bool collected = DistanceSquared(Pickup, from, to) <= radius * radius;
            if (collected)
            {
                plane.CollectBoost(); PickupsCollected++;
                LongestChain = Mathf.Max(LongestChain, ++pickupChain);
            }
            float pickupEnd = 16 + pickupIndex * 32 + Mathf.Max(2f, effects.magnetRadius);
            if (collected || to.x > pickupEnd)
            {
                if (!collected) pickupChain = 0;
                pickupIndex++; PlacePickup();
            }

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
