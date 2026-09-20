using System.Collections.Generic;
using UnityEngine;

namespace BetweenTiles.Core
{
    public sealed class EntityInstance
    {
        public EntityType Type;
        public Vector2Int Position;
        public LaserDirections LaserDirections;

        public EntityInstance(EntityType type, Vector2Int position, LaserDirections laserDirections = LaserDirections.None)
        {
            Type = type;
            Position = position;
            LaserDirections = laserDirections;
        }
    }

    public sealed class LevelData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly CellType[,] Cells;
        public readonly List<EntityInstance> Entities = new();
        public readonly Dictionary<EdgeKey, EdgeType> Edges = new();
        public readonly Dictionary<Vector2Int, LevelAsset> Entrances = new();
        public readonly Dictionary<Vector2Int, int> Locks = new();
        public readonly Dictionary<Vector2Int, LaserDirections> CellLasers = new();

        public LevelData(int width, int height, CellType defaultCell = CellType.Ground)
        {
            Width = width;
            Height = height;
            Cells = new CellType[width, height];

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    Cells[x, y] = defaultCell;
                }
            }
        }

        public bool Contains(Vector2Int position)
        {
            return position.x >= 0 && position.x < Width && position.y >= 0 && position.y < Height;
        }

        public void SetCell(Vector2Int position, CellType type)
        {
            if (!Contains(position))
            {
                return;
            }

            Cells[position.x, position.y] = type;
        }

        public void AddEntity(EntityType type, Vector2Int position, LaserDirections laserDirections = LaserDirections.None)
        {
            Entities.Add(new EntityInstance(type, position, laserDirections));
        }

        public void SetEdgeWall(Vector2Int first, Vector2Int second)
        {
            SetEdge(first, second, EdgeType.Wall);
        }

        public void SetEdge(Vector2Int first, Vector2Int second, EdgeType type)
        {
            Edges[new EdgeKey(first, second)] = type;
        }
    }

    public static class DemoLevelFactory
    {
        public static LevelData CreateFirstPrototype()
        {
            var level = new LevelData(8, 6);

            for (var x = 0; x < level.Width; x++)
            {
                level.SetCell(new Vector2Int(x, 0), CellType.Wall);
                level.SetCell(new Vector2Int(x, level.Height - 1), CellType.Wall);
            }

            for (var y = 0; y < level.Height; y++)
            {
                level.SetCell(new Vector2Int(0, y), CellType.Wall);
                level.SetCell(new Vector2Int(level.Width - 1, y), CellType.Wall);
            }

            level.SetCell(new Vector2Int(3, 2), CellType.Wall);
            level.SetCell(new Vector2Int(3, 3), CellType.Wall);
            level.SetCell(new Vector2Int(6, 4), CellType.Goal);

            level.SetEdgeWall(new Vector2Int(2, 1), new Vector2Int(2, 2));
            level.SetEdgeWall(new Vector2Int(4, 3), new Vector2Int(5, 3));
            level.SetEdgeWall(new Vector2Int(5, 3), new Vector2Int(5, 4));

            level.AddEntity(EntityType.Player, new Vector2Int(1, 1));
            level.AddEntity(EntityType.Box, new Vector2Int(2, 1));
            return level;
        }
    }
}
