namespace FlyEditor.Flight
{
    // A per-flight snapshot. Buying research affects the next launch.
    public sealed class FlightEffects
    {
        public static readonly FlightEffects Base = new FlightEffects(null);
        public readonly float boostSpeed, diveDelay, diveCarry, recoveryTime;
        public readonly float durability, collisionSpeedLoss, magnetRadius, magnetPull;
        public readonly int recoveryUses, shieldUses;
        public readonly bool diveEnabled, reinforcedShield;

        public FlightEffects(FlightResearch research)
        {
            boostSpeed = 2f * (1f + .15f * (research?.CountOwned("Boost_1", "Boost_2", "Boost_3") ?? 0));
            diveEnabled = research?.Owns("Dive_1") == true;
            diveDelay = research?.Owns("Dive_2") == true ? .2f : .6f;
            diveCarry = research?.Owns("Dive_3") == true ? 1.25f : 0f;
            recoveryUses = research?.Owns("Recover_1") == true ? (research.Owns("Recover_3") ? 2 : 1) : 0;
            recoveryTime = research?.Owns("Recover_2") == true ? .12f : .25f;
            durability = 100 + 10 * (research?.CountOwned("Durability_1", "Durability_2", "Durability_3") ?? 0);
            collisionSpeedLoss = .28f * (1f - .12f * (research?.CountOwned("Impact_1", "Impact_2", "Impact_3") ?? 0));
            shieldUses = research?.Owns("Shield_1") == true ? (research.Owns("Shield_3") ? 2 : 1) : 0;
            reinforcedShield = research?.Owns("Shield_2") == true;
            magnetRadius = research?.Owns("Magnet_1") == true ? (research.Owns("Magnet_2") ? 5f : 3f) : 0f;
            magnetPull = research?.Owns("Magnet_3") == true ? 18f : 9f;
        }
    }
}
