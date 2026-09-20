using System.Collections.Generic;
using UnityEngine;

namespace BetweenTiles.Core
{
    public enum MoveResult
    {
        Moved,
        PushedEntityOnly,
        OpenedLock,
        BlockedByBounds,
        BlockedByCell,
        BlockedByEdge,
        BlockedByEntity,
        Completed
    }

    public sealed class BoardRuntime
    {
        private readonly LevelData level;
        private readonly Dictionary<Vector2Int, EntityInstance> entitiesByPosition = new();
        private readonly List<ImpactPushEvent> lastImpactPushes = new();
        private readonly List<Vector2Int> lastOpenedLocks = new();
        private readonly int completedLevelCount;

        public int Width => level.Width;
        public int Height => level.Height;
        public EntityInstance Player { get; private set; }
        public IReadOnlyList<EntityInstance> Entities => level.Entities;
        public IReadOnlyDictionary<Vector2Int, LevelAsset> Entrances => level.Entrances;
        public IReadOnlyDictionary<Vector2Int, LaserDirections> CellLasers => level.CellLasers;
        public IReadOnlyList<ImpactPushEvent> LastImpactPushes => lastImpactPushes;
        public IReadOnlyList<Vector2Int> LastOpenedLocks => lastOpenedLocks;
        public bool OpenedLockDuringLastMove { get; private set; }

        public BoardRuntime(LevelData levelData, int completedLevelCount = 0)
        {
            level = levelData;
            this.completedLevelCount = completedLevelCount;

            foreach (var entity in level.Entities)
            {
                entitiesByPosition[entity.Position] = entity;

                if (entity.Type == EntityType.Player)
                {
                    Player = entity;
                }
            }
        }

        public CellType GetCell(Vector2Int position)
        {
            return level.Cells[position.x, position.y];
        }

        public EdgeType GetEdge(Vector2Int first, Vector2Int second)
        {
            return level.Edges.TryGetValue(new EdgeKey(first, second), out var edgeType)
                ? edgeType
                : EdgeType.None;
        }

        public LevelAsset GetEntranceTarget(Vector2Int position)
        {
            return level.Entrances.TryGetValue(position, out var targetLevel) ? targetLevel : null;
        }

        public int GetLockRequirement(Vector2Int position)
        {
            return level.Locks.TryGetValue(position, out var requiredCompletions) ? requiredCompletions : 0;
        }

        public bool IsLockOpen(Vector2Int position)
        {
            return GetCell(position) == CellType.Lock && completedLevelCount >= GetLockRequirement(position);
        }

        public bool HasBlockingEntity(Vector2Int position)
        {
            return TryGetBlockingEntity(position, out _);
        }

        public MoveResult TryMovePlayer(Direction direction)
        {
            return TryMovePlayer(direction, false);
        }

        public MoveResult TrySlidePlayer(Direction direction)
        {
            return TryMovePlayer(direction, true);
        }

        private MoveResult TryMovePlayer(Direction direction, bool stopBeforePushedEntity)
        {
            lastImpactPushes.Clear();
            lastOpenedLocks.Clear();
            OpenedLockDuringLastMove = false;
            var from = Player.Position;
            var to = from + direction.ToOffset();

            if (!level.Contains(to))
            {
                return MoveResult.BlockedByBounds;
            }

            if (RuleDefinitions.Edges[GetEdge(from, to)].BlocksMovement)
            {
                return MoveResult.BlockedByEdge;
            }

            var targetCell = GetCell(to);
            if (targetCell == CellType.Lock)
            {
                if (!IsLockOpen(to))
                {
                    return MoveResult.BlockedByCell;
                }
            }
            else if (!RuleDefinitions.Cells[targetCell].Walkable)
            {
                return MoveResult.BlockedByCell;
            }

            if (TryGetBlockingEntity(to, out var blockingEntity))
            {
                if (!TryPushEntityLine(blockingEntity, direction, out _))
                {
                    return MoveResult.BlockedByEntity;
                }

                if (stopBeforePushedEntity)
                {
                    return MoveResult.PushedEntityOnly;
                }
            }

            entitiesByPosition.Remove(from);
            Player.Position = to;
            entitiesByPosition[to] = Player;

            if (ClearOpenLockIfOccupied(to))
            {
                return MoveResult.OpenedLock;
            }

            return RuleDefinitions.Cells[targetCell].IsGoal ? MoveResult.Completed : MoveResult.Moved;
        }

        public bool IsSlippery(Vector2Int position)
        {
            return level.Contains(position) && RuleDefinitions.Cells[GetCell(position)].IsSlippery;
        }

        public void MovePlayerTo(Vector2Int position)
        {
            entitiesByPosition.Remove(Player.Position);
            Player.Position = position;
            entitiesByPosition[position] = Player;
        }

        public bool IsPlayerHitByLaser()
        {
            return Player != null && GetLaserCells().Contains(Player.Position);
        }

        public void ApplyClearedLocks(IEnumerable<Vector2Int> positions)
        {
            foreach (var position in positions)
            {
                if (level.Contains(position) && GetCell(position) == CellType.Lock)
                {
                    level.SetCell(position, CellType.Ground);
                }
            }
        }

        public BoardSnapshot CreateSnapshot()
        {
            var entities = new List<EntitySnapshot>(level.Entities.Count);
            foreach (var entity in level.Entities)
            {
                entities.Add(new EntitySnapshot(entity.Type, entity.Position, entity.LaserDirections));
            }

            var cells = new CellType[level.Width, level.Height];
            for (var x = 0; x < level.Width; x++)
            {
                for (var y = 0; y < level.Height; y++)
                {
                    cells[x, y] = level.Cells[x, y];
                }
            }

            return new BoardSnapshot(entities, cells);
        }

        public void RestoreSnapshot(BoardSnapshot snapshot)
        {
            if (snapshot.Cells != null)
            {
                for (var x = 0; x < level.Width && x < snapshot.Cells.GetLength(0); x++)
                {
                    for (var y = 0; y < level.Height && y < snapshot.Cells.GetLength(1); y++)
                    {
                        level.Cells[x, y] = snapshot.Cells[x, y];
                    }
                }
            }

            entitiesByPosition.Clear();
            Player = null;

            for (var i = 0; i < level.Entities.Count && i < snapshot.Entities.Count; i++)
            {
                var entity = level.Entities[i];
                entity.Type = snapshot.Entities[i].Type;
                entity.Position = snapshot.Entities[i].Position;
                entity.LaserDirections = snapshot.Entities[i].LaserDirections;
                entitiesByPosition[entity.Position] = entity;

                if (entity.Type == EntityType.Player)
                {
                    Player = entity;
                }
            }
        }

        private bool TryGetBlockingEntity(Vector2Int position, out EntityInstance blockingEntity)
        {
            if (entitiesByPosition.TryGetValue(position, out var entity) &&
                RuleDefinitions.Entities[entity.Type].BlocksMovement)
            {
                blockingEntity = entity;
                return true;
            }

            blockingEntity = null;
            return false;
        }

        public HashSet<Vector2Int> GetLaserCells()
        {
            var laserCells = new HashSet<Vector2Int>();
            foreach (var pair in level.CellLasers)
            {
                AddLaserCells(pair.Key, pair.Value, laserCells);
            }

            foreach (var entity in level.Entities)
            {
                if (entity.Type == EntityType.LaserBox)
                {
                    AddLaserCells(entity.Position, entity.LaserDirections, laserCells);
                }
            }

            return laserCells;
        }

        public List<LaserBeam> GetLaserBeams()
        {
            var beams = new List<LaserBeam>();
            foreach (var pair in level.CellLasers)
            {
                AddLaserBeams(pair.Key, pair.Value, beams);
            }

            foreach (var entity in level.Entities)
            {
                if (entity.Type == EntityType.LaserBox)
                {
                    AddLaserBeams(entity.Position, entity.LaserDirections, beams);
                }
            }

            return beams;
        }

        private void AddLaserCells(Vector2Int origin, LaserDirections directions, HashSet<Vector2Int> laserCells)
        {
            if (directions.HasFlag(LaserDirections.Up))
            {
                AddLaserRay(origin, Direction.Up, laserCells);
            }

            if (directions.HasFlag(LaserDirections.Down))
            {
                AddLaserRay(origin, Direction.Down, laserCells);
            }

            if (directions.HasFlag(LaserDirections.Left))
            {
                AddLaserRay(origin, Direction.Left, laserCells);
            }

            if (directions.HasFlag(LaserDirections.Right))
            {
                AddLaserRay(origin, Direction.Right, laserCells);
            }
        }

        private void AddLaserRay(Vector2Int origin, Direction direction, HashSet<Vector2Int> laserCells)
        {
            var offset = direction.ToOffset();
            var previous = origin;
            var current = origin + offset;
            while (level.Contains(current) && !RuleDefinitions.Edges[GetEdge(previous, current)].BlocksMovement)
            {
                if (BlocksLaser(current))
                {
                    break;
                }

                laserCells.Add(current);
                previous = current;
                current += offset;
            }
        }

        private void AddLaserBeams(Vector2Int origin, LaserDirections directions, List<LaserBeam> beams)
        {
            if (directions.HasFlag(LaserDirections.Up))
            {
                AddLaserBeam(origin, Direction.Up, beams);
            }

            if (directions.HasFlag(LaserDirections.Down))
            {
                AddLaserBeam(origin, Direction.Down, beams);
            }

            if (directions.HasFlag(LaserDirections.Left))
            {
                AddLaserBeam(origin, Direction.Left, beams);
            }

            if (directions.HasFlag(LaserDirections.Right))
            {
                AddLaserBeam(origin, Direction.Right, beams);
            }
        }

        private void AddLaserBeam(Vector2Int origin, Direction direction, List<LaserBeam> beams)
        {
            var offset = direction.ToOffset();
            var previous = origin;
            var current = origin + offset;
            var distance = 1f;
            const float startDistance = 0.5f;
            while (level.Contains(current) && !RuleDefinitions.Edges[GetEdge(previous, current)].BlocksMovement)
            {
                if (BlocksLaser(current))
                {
                    var endDistance = distance - GetLaserBlockInset(current);
                    if (endDistance > startDistance)
                    {
                        beams.Add(new LaserBeam(origin, direction, startDistance, endDistance));
                    }

                    break;
                }

                previous = current;
                current += offset;
                distance += 1f;
            }

            if (!level.Contains(current))
            {
                var endDistance = distance - 0.5f;
                if (endDistance > startDistance)
                {
                    beams.Add(new LaserBeam(origin, direction, startDistance, endDistance));
                }
            }
            else if (RuleDefinitions.Edges[GetEdge(previous, current)].BlocksMovement)
            {
                var endDistance = distance - 0.5f;
                if (endDistance > startDistance)
                {
                    beams.Add(new LaserBeam(origin, direction, startDistance, endDistance));
                }
            }
        }

        private float GetLaserBlockInset(Vector2Int position)
        {
            if (TryGetBlockingEntity(position, out var entity) && entity.Type != EntityType.Player)
            {
                return 0.36f;
            }

            return 0.5f;
        }

        private bool BlocksLaser(Vector2Int position)
        {
            var cell = GetCell(position);
            if (!RuleDefinitions.Cells[cell].Walkable || cell == CellType.LaserWall)
            {
                return true;
            }

            return TryGetBlockingEntity(position, out var entity) && entity.Type != EntityType.Player;
        }

        private bool TryPushEntityLine(EntityInstance firstEntity, Direction direction, out List<EntityInstance> pushedEntities)
        {
            pushedEntities = null;
            var pushLine = new List<EntityInstance>();
            var current = firstEntity;

            while (true)
            {
                if (!RuleDefinitions.Entities[current.Type].Pushable)
                {
                    return false;
                }

                pushLine.Add(current);

                var nextPosition = current.Position + direction.ToOffset();
                if (!level.Contains(nextPosition) || RuleDefinitions.Edges[GetEdge(current.Position, nextPosition)].BlocksMovement)
                {
                    return false;
                }

                if (!TryGetBlockingEntity(nextPosition, out var nextEntity))
                {
                    if (!CanEntityEnterCell(nextPosition))
                    {
                        return false;
                    }

                    MovePushLine(pushLine, direction);
                    pushedEntities = new List<EntityInstance>(pushLine);
                    SlidePushedEntities(pushLine, direction);
                    return true;
                }

                current = nextEntity;
            }
        }

        private bool CanEntityEnterCell(Vector2Int position)
        {
            var targetCell = GetCell(position);
            if (targetCell == CellType.Lock)
            {
                return IsLockOpen(position);
            }

            return RuleDefinitions.Cells[targetCell].Walkable;
        }

        private void MovePushLine(List<EntityInstance> pushLine, Direction direction)
        {
            foreach (var entity in pushLine)
            {
                entitiesByPosition.Remove(entity.Position);
            }

            for (var i = pushLine.Count - 1; i >= 0; i--)
            {
                var entity = pushLine[i];
                entity.Position += direction.ToOffset();
                entitiesByPosition[entity.Position] = entity;
                ClearOpenLockIfOccupied(entity.Position);
            }
        }

        private void SlidePushedEntities(List<EntityInstance> pushedEntities, Direction direction)
        {
            while (true)
            {
                var slidingEntities = GetSlipperyPushedEntities(pushedEntities, direction);
                if (slidingEntities.Count == 0)
                {
                    return;
                }

                var slidingSet = new HashSet<EntityInstance>(slidingEntities);
                var movableEntities = new HashSet<EntityInstance>();
                foreach (var entity in slidingEntities)
                {
                    if (TryResolveSlidingEntityStep(entity, direction, slidingSet, movableEntities))
                    {
                        movableEntities.Add(entity);
                    }
                }

                if (movableEntities.Count == 0)
                {
                    return;
                }

                MoveEntities(movableEntities, direction);
            }
        }

        private List<EntityInstance> GetSlipperyPushedEntities(List<EntityInstance> pushedEntities, Direction direction)
        {
            var offset = direction.ToOffset();
            var slidingEntities = new List<EntityInstance>();
            foreach (var entity in pushedEntities)
            {
                if (IsSlippery(entity.Position))
                {
                    slidingEntities.Add(entity);
                }
            }

            slidingEntities.Sort((left, right) =>
                GetDirectionProjection(right.Position, offset).CompareTo(GetDirectionProjection(left.Position, offset)));
            return slidingEntities;
        }

        private bool TryResolveSlidingEntityStep(
            EntityInstance entity,
            Direction direction,
            HashSet<EntityInstance> slidingEntities,
            HashSet<EntityInstance> movableEntities)
        {
            var nextPosition = entity.Position + direction.ToOffset();
            if (!level.Contains(nextPosition) ||
                RuleDefinitions.Edges[GetEdge(entity.Position, nextPosition)].BlocksMovement ||
                !CanEntityEnterCell(nextPosition))
            {
                return false;
            }

            if (!TryGetBlockingEntity(nextPosition, out var blockingEntity))
            {
                return true;
            }

            if (slidingEntities.Contains(blockingEntity))
            {
                return movableEntities.Contains(blockingEntity);
            }

            if (TryPushEntityLine(blockingEntity, direction, out var pushedEntities))
            {
                lastImpactPushes.Add(new ImpactPushEvent(entity, pushedEntities));
            }

            return false;
        }

        private void MoveEntities(HashSet<EntityInstance> entities, Direction direction)
        {
            foreach (var entity in entities)
            {
                entitiesByPosition.Remove(entity.Position);
            }

            foreach (var entity in entities)
            {
                entity.Position += direction.ToOffset();
                entitiesByPosition[entity.Position] = entity;
                ClearOpenLockIfOccupied(entity.Position);
            }
        }

        private bool ClearOpenLockIfOccupied(Vector2Int position)
        {
            if (!IsLockOpen(position))
            {
                return false;
            }

            level.SetCell(position, CellType.Ground);
            OpenedLockDuringLastMove = true;
            lastOpenedLocks.Add(position);
            return true;
        }

        private static int GetDirectionProjection(Vector2Int position, Vector2Int direction)
        {
            return position.x * direction.x + position.y * direction.y;
        }
    }

    public sealed class BoardSnapshot
    {
        public readonly List<EntitySnapshot> Entities;
        public readonly CellType[,] Cells;

        public BoardSnapshot(List<EntitySnapshot> entities, CellType[,] cells)
        {
            Entities = entities;
            Cells = cells;
        }
    }

    public readonly struct EntitySnapshot
    {
        public readonly EntityType Type;
        public readonly Vector2Int Position;
        public readonly LaserDirections LaserDirections;

        public EntitySnapshot(EntityType type, Vector2Int position, LaserDirections laserDirections = LaserDirections.None)
        {
            Type = type;
            Position = position;
            LaserDirections = laserDirections;
        }
    }

    public sealed class ImpactPushEvent
    {
        public readonly EntityInstance Source;
        public readonly IReadOnlyList<EntityInstance> PushedEntities;

        public ImpactPushEvent(EntityInstance source, IReadOnlyList<EntityInstance> pushedEntities)
        {
            Source = source;
            PushedEntities = pushedEntities;
        }
    }

    public readonly struct LaserBeam
    {
        public readonly Vector2Int Origin;
        public readonly Direction Direction;
        public readonly float StartDistance;
        public readonly float EndDistance;

        public LaserBeam(Vector2Int origin, Direction direction, float startDistance, float endDistance)
        {
            Origin = origin;
            Direction = direction;
            StartDistance = startDistance;
            EndDistance = endDistance;
        }
    }
}
