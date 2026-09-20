using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetweenTiles.Core
{
    public enum CellType
    {
        Ground = 0,
        Goal = 1,
        Wall = 2,
        LevelEntrance = 3,
        Ice = 4,
        Empty = 5,
        Lock = 6,
        LaserWall = 7
    }

    public enum EntityType
    {
        Player,
        Box,
        LaserBox
    }

    [Flags]
    public enum LaserDirections
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 4,
        Right = 8
    }

    public enum EdgeType
    {
        None,
        Wall
    }

    public enum Direction
    {
        Up,
        Down,
        Left,
        Right
    }

    public sealed class CellDefinition
    {
        public bool Walkable;
        public bool IsGoal;
        public bool IsLevelEntrance;
        public bool IsSlippery;
    }

    public sealed class EntityDefinition
    {
        public bool BlocksMovement;
        public bool Controllable;
        public bool Pushable;
    }

    public sealed class EdgeDefinition
    {
        public bool BlocksMovement;
    }

    public static class RuleDefinitions
    {
        public static readonly Dictionary<CellType, CellDefinition> Cells = new()
        {
            { CellType.Ground, new CellDefinition { Walkable = true, IsGoal = false, IsLevelEntrance = false, IsSlippery = false } },
            { CellType.Ice, new CellDefinition { Walkable = true, IsGoal = false, IsLevelEntrance = false, IsSlippery = true } },
            { CellType.Goal, new CellDefinition { Walkable = true, IsGoal = true, IsLevelEntrance = false, IsSlippery = false } },
            { CellType.Wall, new CellDefinition { Walkable = false, IsGoal = false, IsLevelEntrance = false, IsSlippery = false } },
            { CellType.LevelEntrance, new CellDefinition { Walkable = false, IsGoal = false, IsLevelEntrance = true, IsSlippery = false } },
            { CellType.Empty, new CellDefinition { Walkable = false, IsGoal = false, IsLevelEntrance = false, IsSlippery = false } },
            { CellType.Lock, new CellDefinition { Walkable = false, IsGoal = false, IsLevelEntrance = false, IsSlippery = false } },
            { CellType.LaserWall, new CellDefinition { Walkable = false, IsGoal = false, IsLevelEntrance = false, IsSlippery = false } }
        };

        public static readonly Dictionary<EntityType, EntityDefinition> Entities = new()
        {
            { EntityType.Player, new EntityDefinition { BlocksMovement = true, Controllable = true, Pushable = false } },
            { EntityType.Box, new EntityDefinition { BlocksMovement = true, Controllable = false, Pushable = true } },
            { EntityType.LaserBox, new EntityDefinition { BlocksMovement = true, Controllable = false, Pushable = true } }
        };

        public static readonly Dictionary<EdgeType, EdgeDefinition> Edges = new()
        {
            { EdgeType.None, new EdgeDefinition { BlocksMovement = false } },
            { EdgeType.Wall, new EdgeDefinition { BlocksMovement = true } }
        };
    }

    public static class DirectionExtensions
    {
        public static Vector2Int ToOffset(this Direction direction)
        {
            return direction switch
            {
                Direction.Up => Vector2Int.up,
                Direction.Down => Vector2Int.down,
                Direction.Left => Vector2Int.left,
                Direction.Right => Vector2Int.right,
                _ => Vector2Int.zero
            };
        }
    }

    public readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly Vector2Int A;
        public readonly Vector2Int B;

        public EdgeKey(Vector2Int first, Vector2Int second)
        {
            if (ComesBefore(first, second))
            {
                A = first;
                B = second;
                return;
            }

            A = second;
            B = first;
        }

        public bool Equals(EdgeKey other)
        {
            return A == other.A && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(A, B);
        }

        private static bool ComesBefore(Vector2Int first, Vector2Int second)
        {
            return first.x < second.x || first.x == second.x && first.y <= second.y;
        }
    }
}
