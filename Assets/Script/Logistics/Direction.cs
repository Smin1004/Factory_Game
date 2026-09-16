using UnityEngine;

namespace FactoryGame.Logistics
{
    public enum Direction : byte
    {
        North = 0, // +Y
        East = 1,  // +X
        South = 2, // -Y
        West = 3,  // -X
    }

    public static class DirectionExtensions
    {
        private static readonly Vector2Int[] Vectors =
        {
            new(0, 1), new(1, 0), new(0, -1), new(-1, 0),
        };

        public static Vector2Int ToVector(this Direction d) => Vectors[(int)d];
        public static Direction Opposite(this Direction d) => (Direction)(((int)d + 2) & 3);
        public static Direction RotateCW(this Direction d) => (Direction)(((int)d + 1) & 3);
        public static Direction RotateCCW(this Direction d) => (Direction)(((int)d + 3) & 3);
        public static float ToAngleDeg(this Direction d) => d switch
        {
            Direction.North => 90f,
            Direction.East => 0f,
            Direction.South => -90f,
            _ => 180f,
        };
    }
}
