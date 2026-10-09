namespace FusionKingdoms.Sim;

// The contract every brain shares. Changing anything here invalidates all trained brains.
public static class BrainSpec
{
    public const int Version = 1;

    public static class In
    {
        public const int Food = 0;            // vec2, proximity-encoded
        public const int Timber = 2;
        public const int Stone = 4;
        public const int Lumen = 6;
        public const int Ally = 8;
        public const int Enemy = 10;
        public const int Wild = 12;
        public const int SubduedAlly = 14;
        public const int SubduedHostile = 16;
        public const int EnemyBuilding = 18;
        public const int AllyCount = 20;
        public const int EnemyCount = 21;
        public const int WildCount = 22;
        public const int HomeDir = 23;        // unit vec2
        public const int HomeDist = 25;
        public const int RulerDir = 26;       // unit vec2
        public const int RulerDist = 28;
        public const int CommandDir = 29;     // unit vec2
        public const int CommandDist = 31;
        public const int Will = 32;
        public const int CarryLoad = 33;
        public const int CarryingUnit = 34;
        public const int InHome = 35;
        public const int InTussle = 36;
        public const int TimeSin = 37;
        public const int TimeCos = 38;
        public const int Stance = 39;
        public const int CmdGuide = 40;       // work zone or rally banner active
        public const int CmdRaid = 41;
        public const int CarryFood = 42;
        public const int CarryMaterial = 43;
        public const int CarryLumen = 44;
        public const int BuildSite = 45;      // vec2, proximity-encoded over OwnBuildingRange
        public const int Count = 47;
    }

    public const int NumMoves = 9;            // 0 stay, 1..8 = N, NE, E, SE, S, SW, W, NW
    public const int NumActions = 9;
    public const int NumOutputs = NumMoves + NumActions;

    public const int Hidden1 = 64;
    public const int Hidden2 = 64;

    public const int W1 = 0;
    public const int B1 = W1 + Hidden1 * In.Count;
    public const int W2 = B1 + Hidden1;
    public const int B2 = W2 + Hidden2 * Hidden1;
    public const int W3 = B2 + Hidden2;
    public const int B3 = W3 + NumOutputs * Hidden2;
    public const int WeightCount = B3 + NumOutputs;
}

public enum Act
{
    Idle, Gather, Deposit, Build, Tussle, Carry, Steal, Sabotage, Rescue
}
