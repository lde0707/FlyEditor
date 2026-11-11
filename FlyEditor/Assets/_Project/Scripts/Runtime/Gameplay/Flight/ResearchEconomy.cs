using System;

namespace FlyEditor.Flight
{
    // Pure calculations; invoked when buying or settling, never per simulation tick.
    public static class ResearchEconomy
    {
        public static int NodePrice(int depth, bool unlocksAbility)
        {
            if (depth < 1 || depth > 5) throw new ArgumentOutOfRangeException(nameof(depth));
            int price = 30 * (1 << (2 * (depth - 1)));
            return unlocksAbility && depth > 1 ? price * 3 / 2 : price;
        }

        public static int BaseReward(float distance)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0f) return 0;
            return (int)Math.Min(int.MaxValue, Math.Floor(30d * Math.Pow(distance / 50d, 1.65d)));
        }

        // Each existing reward unit grants 5 percentage points of the actual run reward.
        // Conditions stack additively, up to +50%; distance and bonus are rounded separately.
        public static int BonusReward(float distance, float units)
        {
            if (float.IsNaN(units) || float.IsInfinity(units) || units <= 0f) return 0;
            return (int)Math.Floor(BaseReward(distance) * Math.Min(.5d, units * .05d));
        }
    }
}
