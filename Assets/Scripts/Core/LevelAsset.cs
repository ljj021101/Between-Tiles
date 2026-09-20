using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetweenTiles.Core
{
    public enum LevelKind
    {
        PuzzleLevel,
        WorldMap
    }

    [CreateAssetMenu(menuName = "Between Tiles/Level", fileName = "New Level")]
    public sealed class LevelAsset : ScriptableObject
    {
        public string SaveId;
        public LevelKind Kind = LevelKind.PuzzleLevel;
        public int Width = 1;
        public int Height = 1;
        public List<CellType> Cells = new() { CellType.Ground };
        public List<EntityData> Entities = new();
        public List<EdgeData> Edges = new();
        public List<LevelEntranceData> Entrances = new();
        public List<LevelLockData> Locks = new();
        public List<LevelLaserData> Lasers = new();

        public string SaveKey => string.IsNullOrWhiteSpace(SaveId) ? name : SaveId;

        public LevelData ToLevelData(Vector2Int? playerOverridePosition = null)
        {
            EnsureCellCount();

            var level = new LevelData(Width, Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    level.SetCell(new Vector2Int(x, y), Cells[ToIndex(x, y)]);
                }
            }

            foreach (var entity in Entities)
            {
                var position = entity.Position;
                if (entity.Type == EntityType.Player && playerOverridePosition.HasValue)
                {
                    position = playerOverridePosition.Value;
                }

                if (level.Contains(position))
                {
                    level.AddEntity(entity.Type, position, entity.LaserDirections);
                }
            }

            foreach (var edge in Edges)
            {
                if (edge.Type == EdgeType.None || !level.Contains(edge.A) || !level.Contains(edge.B))
                {
                    continue;
                }

                level.SetEdge(edge.A, edge.B, edge.Type);
            }

            foreach (var entrance in Entrances)
            {
                if (level.Contains(entrance.Position))
                {
                    level.Entrances[entrance.Position] = entrance.TargetLevel;
                }
            }

            if (Locks == null)
            {
                Locks = new List<LevelLockData>();
            }

            foreach (var levelLock in Locks)
            {
                if (level.Contains(levelLock.Position))
                {
                    level.Locks[levelLock.Position] = Mathf.Max(0, levelLock.RequiredCompletions);
                }
            }

            Lasers ??= new List<LevelLaserData>();
            foreach (var laser in Lasers)
            {
                if (level.Contains(laser.Position))
                {
                    level.CellLasers[laser.Position] = laser.Directions;
                }
            }

            return level;
        }

        public int ToIndex(int x, int y)
        {
            return y * Width + x;
        }

        public void EnsureCellCount()
        {
            Width = Mathf.Max(1, Width);
            Height = Mathf.Max(1, Height);
            var expectedCount = Width * Height;

            while (Cells.Count < expectedCount)
            {
                Cells.Add(CellType.Ground);
            }

            if (Cells.Count > expectedCount)
            {
                Cells.RemoveRange(expectedCount, Cells.Count - expectedCount);
            }
        }
    }

    [Serializable]
    public sealed class EntityData
    {
        public EntityType Type;
        public Vector2Int Position;
        public LaserDirections LaserDirections = LaserDirections.None;
    }

    [Serializable]
    public sealed class EdgeData
    {
        public EdgeType Type;
        public Vector2Int A;
        public Vector2Int B;
    }

    [Serializable]
    public sealed class LevelEntranceData
    {
        public Vector2Int Position;
        public LevelAsset TargetLevel;
    }

    [Serializable]
    public sealed class LevelLockData
    {
        public Vector2Int Position;
        public int RequiredCompletions = 1;
    }

    [Serializable]
    public sealed class LevelLaserData
    {
        public Vector2Int Position;
        public LaserDirections Directions = LaserDirections.None;
    }
}
