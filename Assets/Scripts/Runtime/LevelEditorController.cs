using BetweenTiles.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetweenTiles.Runtime
{
    public enum LevelBrushKind
    {
        Goal = 0,
        CellWall = 1,
        LevelEntrance = 2,
        Player = 3,
        Box = 4,
        EdgeWall = 5,
        Ground = 6,
        Erase = 7,
        EraseEdge = 8,
        Ice = 9,
        Lock = 10,
        LaserWall = 11,
        LaserBox = 12
    }

    [ExecuteAlways]
    public sealed class LevelEditorController : MonoBehaviour
    {
        private const float CellSize = 1f;

        public LevelAsset CurrentLevel;
        public LevelBrushKind Brush;
        public LevelAsset EntranceTargetLevel;
        public int LockRequiredCompletions = 1;
        public LaserDirections LaserDirections = LaserDirections.None;
        public bool PaintingEnabled = true;
        public bool SelectionMode;
        public bool AutoSave = true;
        public float AutoSaveDelay = 0.6f;
        public int SolverMaxVisitedStates = LevelSolver.DefaultMaxVisitedStates;
        public bool ShowPreview = true;
        public int EmptyPreviewWidth = 8;
        public int EmptyPreviewHeight = 6;
        [HideInInspector]
        public int PreviewPadding = 0;

        [Header("Preview Prefabs")]
        public GameObject GroundPrefab;
        public GameObject IcePrefab;
        public GameObject GoalPrefab;
        public GameObject CellWallPrefab;
        public GameObject LevelEntrancePrefab;
        public GameObject EdgeWallPrefab;
        public GameObject PlayerPrefab;
        public GameObject BoxPrefab;
        public GameObject LaserBoxPrefab;
        public GameObject LaserWallPrefab;

        [SerializeField, HideInInspector]
        private List<CellEditData> editedCells = new();

        [SerializeField, HideInInspector]
        private List<EntityData> editedEntities = new();

        [SerializeField, HideInInspector]
        private List<EdgeData> editedEdges = new();

        [SerializeField, HideInInspector]
        private List<LevelEntranceData> editedEntrances = new();

        [SerializeField, HideInInspector]
        private List<LevelLockData> editedLocks = new();

        [SerializeField, HideInInspector]
        private List<LevelLaserData> editedLasers = new();

        public void LoadFromCurrentLevel()
        {
            editedCells.Clear();
            editedEntities.Clear();
            editedEdges.Clear();
            editedEntrances.Clear();
            editedLocks.Clear();
            editedLasers.Clear();

            if (CurrentLevel == null)
            {
                RebuildPreview();
                return;
            }

            CurrentLevel.EnsureCellCount();
            for (var y = 0; y < CurrentLevel.Height; y++)
            {
                for (var x = 0; x < CurrentLevel.Width; x++)
                {
                    var cell = CurrentLevel.Cells[CurrentLevel.ToIndex(x, y)];
                    if (cell != CellType.Ground && cell != CellType.Empty)
                    {
                        editedCells.Add(new CellEditData { Position = new Vector2Int(x, y), Type = cell });
                    }
                }
            }

            foreach (var entity in CurrentLevel.Entities)
            {
                editedEntities.Add(new EntityData { Type = entity.Type, Position = entity.Position, LaserDirections = entity.LaserDirections });
            }

            foreach (var edge in CurrentLevel.Edges)
            {
                editedEdges.Add(new EdgeData { Type = edge.Type, A = edge.A, B = edge.B });
            }

            foreach (var entrance in CurrentLevel.Entrances)
            {
                editedEntrances.Add(new LevelEntranceData { Position = entrance.Position, TargetLevel = entrance.TargetLevel });
            }

            CurrentLevel.Locks ??= new List<LevelLockData>();
            foreach (var levelLock in CurrentLevel.Locks)
            {
                editedLocks.Add(new LevelLockData { Position = levelLock.Position, RequiredCompletions = Mathf.Max(0, levelLock.RequiredCompletions) });
            }

            CurrentLevel.Lasers ??= new List<LevelLaserData>();
            foreach (var laser in CurrentLevel.Lasers)
            {
                editedLasers.Add(new LevelLaserData { Position = laser.Position, Directions = laser.Directions });
            }

            RebuildPreview();
        }

        public bool SaveToCurrentLevel(out string message)
        {
            if (CurrentLevel == null)
            {
                message = "No Current Level assigned.";
                return false;
            }

            if (!TryGetContentBounds(out var min, out var max))
            {
                message = "Nothing to save.";
                return false;
            }

            SaveToLevel(CurrentLevel, min, max);
            message = $"Saved {CurrentLevel.name} as {CurrentLevel.Width}x{CurrentLevel.Height}.";
            return true;
        }

        public bool TryBuildEditedLevelData(out LevelData level, out string message)
        {
            if (!TryGetContentBounds(out var min, out var max))
            {
                level = null;
                message = "没有可验证的内容。";
                return false;
            }

            level = BuildLevelData(min, max);
            message = string.Empty;
            return true;
        }

        public void SaveToLevel(LevelAsset levelAsset, Vector2Int min, Vector2Int max)
        {
            var editedLevel = BuildLevelData(min, max);

            levelAsset.Width = editedLevel.Width;
            levelAsset.Height = editedLevel.Height;
            levelAsset.Cells.Clear();
            levelAsset.Entities.Clear();
            levelAsset.Edges.Clear();
            levelAsset.Entrances.Clear();
            levelAsset.Locks ??= new List<LevelLockData>();
            levelAsset.Locks.Clear();
            levelAsset.Lasers ??= new List<LevelLaserData>();
            levelAsset.Lasers.Clear();

            for (var y = 0; y < editedLevel.Height; y++)
            {
                for (var x = 0; x < editedLevel.Width; x++)
                {
                    levelAsset.Cells.Add(editedLevel.Cells[x, y]);
                }
            }

            foreach (var entity in editedLevel.Entities)
            {
                levelAsset.Entities.Add(new EntityData { Type = entity.Type, Position = entity.Position, LaserDirections = entity.LaserDirections });
            }

            foreach (var edge in editedLevel.Edges)
            {
                levelAsset.Edges.Add(new EdgeData { Type = edge.Value, A = edge.Key.A, B = edge.Key.B });
            }

            foreach (var entrance in editedLevel.Entrances)
            {
                levelAsset.Entrances.Add(new LevelEntranceData { Position = entrance.Key, TargetLevel = entrance.Value });
            }

            foreach (var levelLock in editedLevel.Locks)
            {
                levelAsset.Locks.Add(new LevelLockData { Position = levelLock.Key, RequiredCompletions = levelLock.Value });
            }

            foreach (var laser in editedLevel.CellLasers)
            {
                levelAsset.Lasers.Add(new LevelLaserData { Position = laser.Key, Directions = laser.Value });
            }

            RebuildPreview();
        }

        private LevelData BuildLevelData(Vector2Int min, Vector2Int max)
        {
            var width = Mathf.Max(1, max.x - min.x + 1);
            var height = Mathf.Max(1, max.y - min.y + 1);
            var level = new LevelData(width, height, CellType.Empty);
            var cells = BuildCellTypes(min, max);

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    level.SetCell(new Vector2Int(x, y), cells[x, y]);
                }
            }

            foreach (var entity in editedEntities)
            {
                var position = entity.Position - min;
                if (level.Contains(position))
                {
                    var laserDirections = entity.Type == EntityType.LaserBox ? entity.LaserDirections : LaserDirections.None;
                    level.AddEntity(entity.Type, position, laserDirections);
                }
            }

            foreach (var edge in editedEdges)
            {
                var a = edge.A - min;
                var b = edge.B - min;
                if (level.Contains(a) && level.Contains(b))
                {
                    level.SetEdge(a, b, edge.Type);
                }
            }

            foreach (var entrance in editedEntrances)
            {
                var position = entrance.Position - min;
                if (level.Contains(position))
                {
                    level.Entrances[position] = entrance.TargetLevel;
                }
            }

            foreach (var levelLock in editedLocks)
            {
                var position = levelLock.Position - min;
                if (level.Contains(position))
                {
                    level.Locks[position] = Mathf.Max(0, levelLock.RequiredCompletions);
                }
            }

            foreach (var laser in editedLasers)
            {
                var position = laser.Position - min;
                if (level.Contains(position))
                {
                    level.CellLasers[position] = laser.Directions;
                }
            }

            return level;
        }

        private CellType[,] BuildCellTypes(Vector2Int min, Vector2Int max)
        {
            var width = Mathf.Max(1, max.x - min.x + 1);
            var height = Mathf.Max(1, max.y - min.y + 1);
            var cells = new CellType[width, height];

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    cells[x, y] = CellType.Empty;
                }
            }

            foreach (var cell in editedCells)
            {
                var position = cell.Position - min;
                if (ContainsLocal(position, width, height))
                {
                    cells[position.x, position.y] = cell.Type;
                }
            }

            FillClosedGround(cells, min, width, height);
            EnsureEntityFloors(cells, min, width, height);
            return cells;
        }

        private void FillClosedGround(CellType[,] cells, Vector2Int min, int width, int height)
        {
            var exterior = new bool[width + 2, height + 2];
            var queue = new Queue<Vector2Int>();
            var start = new Vector2Int(-1, -1);
            queue.Enqueue(start);
            exterior[0, 0] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                TryVisitExterior(current, current + Vector2Int.up, cells, min, width, height, exterior, queue);
                TryVisitExterior(current, current + Vector2Int.down, cells, min, width, height, exterior, queue);
                TryVisitExterior(current, current + Vector2Int.left, cells, min, width, height, exterior, queue);
                TryVisitExterior(current, current + Vector2Int.right, cells, min, width, height, exterior, queue);
            }

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    if (cells[x, y] == CellType.Empty && !exterior[x + 1, y + 1])
                    {
                        cells[x, y] = CellType.Ground;
                    }
                }
            }
        }

        private void EnsureEntityFloors(CellType[,] cells, Vector2Int min, int width, int height)
        {
            foreach (var entity in editedEntities)
            {
                var position = entity.Position - min;
                if (ContainsLocal(position, width, height) && cells[position.x, position.y] == CellType.Empty)
                {
                    cells[position.x, position.y] = CellType.Ground;
                }
            }
        }

        private void TryVisitExterior(
            Vector2Int from,
            Vector2Int to,
            CellType[,] cells,
            Vector2Int min,
            int width,
            int height,
            bool[,] exterior,
            Queue<Vector2Int> queue)
        {
            if (to.x < -1 || to.x > width || to.y < -1 || to.y > height)
            {
                return;
            }

            if (exterior[to.x + 1, to.y + 1] || BlocksExteriorFlow(from, to, cells, min, width, height))
            {
                return;
            }

            exterior[to.x + 1, to.y + 1] = true;
            queue.Enqueue(to);
        }

        private bool BlocksExteriorFlow(Vector2Int from, Vector2Int to, CellType[,] cells, Vector2Int min, int width, int height)
        {
            var fromInside = ContainsLocal(from, width, height);
            var toInside = ContainsLocal(to, width, height);

            if (toInside && IsSolidCellForAutoGround(cells[to.x, to.y]))
            {
                return true;
            }

            if (fromInside && IsSolidCellForAutoGround(cells[from.x, from.y]))
            {
                return true;
            }

            if (!fromInside || !toInside)
            {
                return false;
            }

            return HasEditedEdgeWall(from + min, to + min);
        }

        public bool TryGetContentBounds(out Vector2Int min, out Vector2Int max)
        {
            var hasContent = false;
            min = Vector2Int.zero;
            max = Vector2Int.zero;

            foreach (var cell in editedCells)
            {
                Include(cell.Position, ref min, ref max, ref hasContent);
            }

            foreach (var entity in editedEntities)
            {
                Include(entity.Position, ref min, ref max, ref hasContent);
            }

            foreach (var entrance in editedEntrances)
            {
                Include(entrance.Position, ref min, ref max, ref hasContent);
            }

            foreach (var levelLock in editedLocks)
            {
                Include(levelLock.Position, ref min, ref max, ref hasContent);
            }

            foreach (var laser in editedLasers)
            {
                Include(laser.Position, ref min, ref max, ref hasContent);
            }

            foreach (var edge in editedEdges)
            {
                Include(edge.A, ref min, ref max, ref hasContent);
                Include(edge.B, ref min, ref max, ref hasContent);
            }

            return hasContent;
        }

        public void PaintAtWorld(Vector3 worldPosition, bool erase)
        {
            if (erase)
            {
                EraseAtWorld(worldPosition);
                RebuildPreview();
                return;
            }

            if (Brush == LevelBrushKind.EdgeWall || Brush == LevelBrushKind.EraseEdge)
            {
                GetNearestEdge(worldPosition, out var a, out var b);
                SetEdge(a, b, Brush == LevelBrushKind.EdgeWall ? EdgeType.Wall : EdgeType.None);
                RebuildPreview();
                return;
            }

            var cellPosition = WorldToCell(worldPosition);
            switch (Brush)
            {
                case LevelBrushKind.Goal:
                    SetCell(cellPosition, CellType.Goal);
                    break;
                case LevelBrushKind.Ice:
                    SetCell(cellPosition, CellType.Ice);
                    break;
                case LevelBrushKind.CellWall:
                    SetCell(cellPosition, CellType.Wall);
                    break;
                case LevelBrushKind.Lock:
                    SetCell(cellPosition, CellType.Lock);
                    SetLock(cellPosition, LockRequiredCompletions);
                    break;
                case LevelBrushKind.LaserWall:
                    SetCell(cellPosition, CellType.LaserWall);
                    SetLaser(cellPosition, LaserDirections);
                    break;
                case LevelBrushKind.LevelEntrance:
                    SetCell(cellPosition, CellType.LevelEntrance);
                    SetEntrance(cellPosition, EntranceTargetLevel);
                    break;
                case LevelBrushKind.Player:
                    SetPlayer(cellPosition);
                    break;
                case LevelBrushKind.Box:
                    SetEntity(cellPosition, EntityType.Box);
                    break;
                case LevelBrushKind.LaserBox:
                    SetEntity(cellPosition, EntityType.LaserBox);
                    break;
                case LevelBrushKind.Ground:
                case LevelBrushKind.Erase:
                    SetCell(cellPosition, CellType.Ground);
                    RemoveEntity(cellPosition);
                    break;
            }

            RebuildPreview();
        }

        public Vector2Int CellFromWorld(Vector3 worldPosition)
        {
            return WorldToCell(worldPosition);
        }

        public void TranslateSelection(Vector2Int min, Vector2Int max, Vector2Int offset)
        {
            if (offset == Vector2Int.zero)
            {
                return;
            }

            NormalizeBounds(ref min, ref max);

            var movedCells = new List<CellEditData>();
            editedCells.RemoveAll(cell =>
            {
                if (!ContainsInclusive(cell.Position, min, max))
                {
                    return false;
                }

                movedCells.Add(new CellEditData { Position = cell.Position + offset, Type = cell.Type });
                return true;
            });

            var movedEntities = new List<EntityData>();
            editedEntities.RemoveAll(entity =>
            {
                if (!ContainsInclusive(entity.Position, min, max))
                {
                    return false;
                }

                movedEntities.Add(new EntityData { Type = entity.Type, Position = entity.Position + offset, LaserDirections = entity.LaserDirections });
                return true;
            });

            var movedEntrances = new List<LevelEntranceData>();
            editedEntrances.RemoveAll(entrance =>
            {
                if (!ContainsInclusive(entrance.Position, min, max))
                {
                    return false;
                }

                movedEntrances.Add(new LevelEntranceData { Position = entrance.Position + offset, TargetLevel = entrance.TargetLevel });
                return true;
            });

            var movedLocks = new List<LevelLockData>();
            editedLocks.RemoveAll(levelLock =>
            {
                if (!ContainsInclusive(levelLock.Position, min, max))
                {
                    return false;
                }

                movedLocks.Add(new LevelLockData { Position = levelLock.Position + offset, RequiredCompletions = levelLock.RequiredCompletions });
                return true;
            });

            var movedLasers = new List<LevelLaserData>();
            editedLasers.RemoveAll(laser =>
            {
                if (!ContainsInclusive(laser.Position, min, max))
                {
                    return false;
                }

                movedLasers.Add(new LevelLaserData { Position = laser.Position + offset, Directions = laser.Directions });
                return true;
            });

            var movedEdges = new List<EdgeData>();
            editedEdges.RemoveAll(edge =>
            {
                if (!ContainsInclusive(edge.A, min, max) && !ContainsInclusive(edge.B, min, max))
                {
                    return false;
                }

                movedEdges.Add(new EdgeData { Type = edge.Type, A = edge.A + offset, B = edge.B + offset });
                return true;
            });

            foreach (var cell in movedCells)
            {
                editedCells.RemoveAll(existing => existing.Position == cell.Position);
                if (IsSolidCellForEntities(cell.Type))
                {
                    editedEntities.RemoveAll(entity => entity.Position == cell.Position);
                }

                if (cell.Type != CellType.LevelEntrance)
                {
                    editedEntrances.RemoveAll(entrance => entrance.Position == cell.Position);
                }

                if (cell.Type != CellType.Lock)
                {
                    editedLocks.RemoveAll(levelLock => levelLock.Position == cell.Position);
                }

                if (cell.Type != CellType.LaserWall)
                {
                    editedLasers.RemoveAll(laser => laser.Position == cell.Position);
                }

                editedCells.Add(cell);
            }

            foreach (var entity in movedEntities)
            {
                editedEntities.RemoveAll(existing => existing.Position == entity.Position || existing.Type == EntityType.Player && entity.Type == EntityType.Player);
                editedEntities.Add(entity);
            }

            foreach (var entrance in movedEntrances)
            {
                editedEntrances.RemoveAll(existing => existing.Position == entrance.Position);
                editedEntrances.Add(entrance);
            }

            foreach (var levelLock in movedLocks)
            {
                editedLocks.RemoveAll(existing => existing.Position == levelLock.Position);
                editedLocks.Add(levelLock);
            }

            foreach (var laser in movedLasers)
            {
                editedLasers.RemoveAll(existing => existing.Position == laser.Position);
                editedLasers.Add(laser);
            }

            foreach (var edge in movedEdges)
            {
                var key = new EdgeKey(edge.A, edge.B);
                editedEdges.RemoveAll(existing => new EdgeKey(existing.A, existing.B).Equals(key));
                editedEdges.Add(new EdgeData { Type = edge.Type, A = key.A, B = key.B });
            }

            RebuildPreview();
        }

        public void RebuildPreview()
        {
            ClearPreview();

            if (!ShowPreview || Application.isPlaying)
            {
                return;
            }

            GetPreviewBounds(out var min, out var max);
            var cells = BuildCellTypes(min, max);
            for (var x = min.x; x <= max.x; x++)
            {
                for (var y = min.y; y <= max.y; y++)
                {
                    var position = new Vector2Int(x, y);
                    var localPosition = position - min;
                    var cell = cells[localPosition.x, localPosition.y];
                    if (cell == CellType.Empty)
                    {
                        continue;
                    }

                    var view = CreateView(cell.ToString(), GetCellPrefab(cell), CellToWorld(position), new Vector2(0.94f, 0.94f), GetCellColor(cell), 0);
                    if (cell == CellType.Lock)
                    {
                        AddCellLabel(view, $"0/{GetLockRequirement(position)}");
                    }
                    else if (cell == CellType.LevelEntrance)
                    {
                        var entranceName = GetEntranceName(position);
                        if (!string.IsNullOrEmpty(entranceName))
                        {
                            AddCellLabel(view, entranceName, GetEntranceNameCharacterSize(entranceName));
                        }
                    }
                    else if (cell == CellType.LaserWall)
                    {
                        AddLaserEmitterDetails(view, GetLaserDirections(position), 1);
                    }
                }
            }

            foreach (var edge in editedEdges)
            {
                if (edge.Type == EdgeType.Wall)
                {
                    CreateEdgeWallView(edge.A, edge.B);
                }
            }

            foreach (var entity in editedEntities)
            {
                if (entity.Type == EntityType.Player)
                {
                    CreateView("Player", PlayerPrefab, CellToWorld(entity.Position), new Vector2(0.62f, 0.62f), new Color(0.22f, 0.42f, 0.95f), 2);
                }
                else if (entity.Type == EntityType.Box)
                {
                    CreateView("Box", BoxPrefab, CellToWorld(entity.Position), new Vector2(0.72f, 0.72f), new Color(0.64f, 0.42f, 0.20f), 2);
                }
                else if (entity.Type == EntityType.LaserBox)
                {
                    CreateLaserBoxView(CellToWorld(entity.Position), entity.LaserDirections, 2);
                }
            }

            DrawLaserPreview();
        }

        private void DrawLaserPreview()
        {
            foreach (var laser in editedLasers)
            {
                DrawLaserEmitter(CellToWorld(laser.Position), laser.Directions, 0.5f, null);
            }

            foreach (var entity in editedEntities)
            {
                if (entity.Type == EntityType.LaserBox)
                {
                    DrawLaserEmitter(CellToWorld(entity.Position), entity.LaserDirections, 0.36f, entity);
                }
            }
        }

        private void DrawLaserEmitter(Vector3 origin, LaserDirections directions, float startInset, EntityData sourceEntity)
        {
            if (directions.HasFlag(LaserDirections.Up))
            {
                DrawLaserBeam(origin, Direction.Up, startInset, sourceEntity);
            }

            if (directions.HasFlag(LaserDirections.Down))
            {
                DrawLaserBeam(origin, Direction.Down, startInset, sourceEntity);
            }

            if (directions.HasFlag(LaserDirections.Left))
            {
                DrawLaserBeam(origin, Direction.Left, startInset, sourceEntity);
            }

            if (directions.HasFlag(LaserDirections.Right))
            {
                DrawLaserBeam(origin, Direction.Right, startInset, sourceEntity);
            }
        }

        private void DrawLaserBeam(Vector3 origin, Direction direction, float startInset, EntityData sourceEntity)
        {
            var endDistance = FindLaserEndDistance(origin, direction, startInset, sourceEntity);
            var length = endDistance - startInset;
            if (length <= 0.001f)
            {
                return;
            }

            var directionVector = GetDirectionVector(direction);
            var center = origin + directionVector * ((startInset + endDistance) * 0.5f);
            var size = direction is Direction.Up or Direction.Down
                ? new Vector2(0.18f, length)
                : new Vector2(length, 0.18f);
            CreateSpriteObject("Laser Preview", center, size, new Color(1f, 0.05f, 0.08f, 0.9f), 4);
        }

        private float FindLaserEndDistance(Vector3 origin, Direction direction, float startInset, EntityData sourceEntity)
        {
            var endDistance = GetPreviewEdgeDistance(origin, direction);
            if (!TryGetContentBounds(out var min, out var max))
            {
                return endDistance;
            }

            for (var x = min.x; x <= max.x; x++)
            {
                for (var y = min.y; y <= max.y; y++)
                {
                    var position = new Vector2Int(x, y);
                    if (IsLaserBlockingCell(position))
                    {
                        TryShortenLaser(origin, direction, CellToWorld(position), 0.5f, startInset, ref endDistance);
                    }

                    if (HasEditedEdgeWall(position, position + Vector2Int.right))
                    {
                        TryShortenLaserByEdge(origin, direction, CellToWorld(position) + new Vector3(CellSize * 0.5f, 0f, 0f), true, startInset, ref endDistance);
                    }

                    if (HasEditedEdgeWall(position, position + Vector2Int.up))
                    {
                        TryShortenLaserByEdge(origin, direction, CellToWorld(position) + new Vector3(0f, CellSize * 0.5f, 0f), false, startInset, ref endDistance);
                    }
                }
            }

            foreach (var entity in editedEntities)
            {
                if (entity == sourceEntity || entity.Type == EntityType.Player)
                {
                    continue;
                }

                if (RuleDefinitions.Entities[entity.Type].BlocksMovement)
                {
                    TryShortenLaser(origin, direction, CellToWorld(entity.Position), 0.36f, startInset, ref endDistance);
                }
            }

            return Mathf.Max(startInset, endDistance);
        }

        private bool IsLaserBlockingCell(Vector2Int position)
        {
            var cell = GetCell(position);
            return !RuleDefinitions.Cells[cell].Walkable || cell == CellType.LaserWall;
        }

        private void TryShortenLaser(Vector3 origin, Direction direction, Vector3 blockCenter, float blockHalfSize, float startInset, ref float endDistance)
        {
            TryShortenLaser(origin, direction, blockCenter, blockHalfSize, blockHalfSize, startInset, ref endDistance);
        }

        private void TryShortenLaserByEdge(Vector3 origin, Direction direction, Vector3 edgeCenter, bool verticalEdge, float startInset, ref float endDistance)
        {
            if (verticalEdge && direction is not (Direction.Left or Direction.Right))
            {
                return;
            }

            if (!verticalEdge && direction is not (Direction.Up or Direction.Down))
            {
                return;
            }

            TryShortenLaser(origin, direction, edgeCenter, 0.5f, 0.03f, startInset, ref endDistance);
        }

        private void TryShortenLaser(
            Vector3 origin,
            Direction direction,
            Vector3 blockCenter,
            float perpendicularHalfSize,
            float forwardHalfSize,
            float startInset,
            ref float endDistance)
        {
            var vertical = direction is Direction.Up or Direction.Down;
            var perpendicularDelta = vertical
                ? Mathf.Abs(blockCenter.x - origin.x)
                : Mathf.Abs(blockCenter.y - origin.y);
            if (perpendicularDelta > perpendicularHalfSize + 0.001f)
            {
                return;
            }

            var signedDistance = vertical
                ? blockCenter.y - origin.y
                : blockCenter.x - origin.x;
            if (direction is Direction.Down or Direction.Left)
            {
                signedDistance = -signedDistance;
            }

            var nearFaceDistance = signedDistance - forwardHalfSize;
            if (nearFaceDistance <= startInset + 0.001f || nearFaceDistance >= endDistance)
            {
                return;
            }

            endDistance = nearFaceDistance;
        }

        private float GetPreviewEdgeDistance(Vector3 origin, Direction direction)
        {
            if (!TryGetContentBounds(out var min, out var max))
            {
                return 0f;
            }

            var minWorld = CellToWorld(min);
            var maxWorld = CellToWorld(max);
            return direction switch
            {
                Direction.Up => maxWorld.y + CellSize * 0.5f - origin.y,
                Direction.Down => origin.y - (minWorld.y - CellSize * 0.5f),
                Direction.Right => maxWorld.x + CellSize * 0.5f - origin.x,
                Direction.Left => origin.x - (minWorld.x - CellSize * 0.5f),
                _ => 0f
            };
        }

        private void OnValidate()
        {
            EmptyPreviewWidth = Mathf.Max(1, EmptyPreviewWidth);
            EmptyPreviewHeight = Mathf.Max(1, EmptyPreviewHeight);
            PreviewPadding = 0;
            AutoSaveDelay = Mathf.Max(0.1f, AutoSaveDelay);
            SolverMaxVisitedStates = Mathf.Max(1000, SolverMaxVisitedStates);
            LockRequiredCompletions = Mathf.Max(0, LockRequiredCompletions);
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                ClearPreview();
            }
        }

        private void EraseAtWorld(Vector3 worldPosition)
        {
            if (Brush == LevelBrushKind.EdgeWall || Brush == LevelBrushKind.EraseEdge)
            {
                GetNearestEdge(worldPosition, out var a, out var b);
                SetEdge(a, b, EdgeType.None);
                return;
            }

            var cellPosition = WorldToCell(worldPosition);
            if (Brush == LevelBrushKind.Player || Brush == LevelBrushKind.Box || Brush == LevelBrushKind.LaserBox)
            {
                RemoveEntity(cellPosition);
                return;
            }

            SetCell(cellPosition, CellType.Ground);
            RemoveEntity(cellPosition);
        }

        private void SetCell(Vector2Int position, CellType type)
        {
            editedCells.RemoveAll(cell => cell.Position == position);
            if (type != CellType.Ground)
            {
                editedCells.Add(new CellEditData { Position = position, Type = type });
            }

            if (IsSolidCellForEntities(type))
            {
                RemoveEntity(position);
            }

            if (type != CellType.LevelEntrance)
            {
                editedEntrances.RemoveAll(entrance => entrance.Position == position);
            }

            if (type != CellType.Lock)
            {
                editedLocks.RemoveAll(levelLock => levelLock.Position == position);
            }

            if (type != CellType.LaserWall)
            {
                editedLasers.RemoveAll(laser => laser.Position == position);
            }
        }

        private CellType GetCell(Vector2Int position)
        {
            foreach (var cell in editedCells)
            {
                if (cell.Position == position)
                {
                    return cell.Type;
                }
            }

            return CellType.Ground;
        }

        private void SetPlayer(Vector2Int position)
        {
            if (IsSolidCellForEntities(GetCell(position)))
            {
                SetCell(position, CellType.Ground);
            }

            editedEntities.RemoveAll(entity => entity.Type == EntityType.Player);
            RemoveEntity(position);
            editedEntities.Add(new EntityData { Type = EntityType.Player, Position = position });
        }

        private void SetEntity(Vector2Int position, EntityType type)
        {
            if (IsSolidCellForEntities(GetCell(position)))
            {
                SetCell(position, CellType.Ground);
            }

            editedEntities.RemoveAll(entity => entity.Position == position);
            editedEntities.Add(new EntityData
            {
                Type = type,
                Position = position,
                LaserDirections = type == EntityType.LaserBox ? LaserDirections : LaserDirections.None
            });
        }

        private void RemoveEntity(Vector2Int position)
        {
            editedEntities.RemoveAll(entity => entity.Position == position);
        }

        private void SetEntrance(Vector2Int position, LevelAsset targetLevel)
        {
            editedEntrances.RemoveAll(entrance => entrance.Position == position);
            editedEntrances.Add(new LevelEntranceData { Position = position, TargetLevel = targetLevel });
        }

        private void SetLock(Vector2Int position, int requiredCompletions)
        {
            editedLocks.RemoveAll(levelLock => levelLock.Position == position);
            editedLocks.Add(new LevelLockData { Position = position, RequiredCompletions = Mathf.Max(0, requiredCompletions) });
        }

        private void SetLaser(Vector2Int position, LaserDirections directions)
        {
            editedLasers.RemoveAll(laser => laser.Position == position);
            editedLasers.Add(new LevelLaserData { Position = position, Directions = directions });
        }

        private int GetLockRequirement(Vector2Int position)
        {
            foreach (var levelLock in editedLocks)
            {
                if (levelLock.Position == position)
                {
                    return Mathf.Max(0, levelLock.RequiredCompletions);
                }
            }

            return Mathf.Max(0, LockRequiredCompletions);
        }

        private LaserDirections GetLaserDirections(Vector2Int position)
        {
            foreach (var laser in editedLasers)
            {
                if (laser.Position == position)
                {
                    return laser.Directions;
                }
            }

            return LaserDirections;
        }

        private string GetEntranceName(Vector2Int position)
        {
            foreach (var entrance in editedEntrances)
            {
                if (entrance.Position == position && entrance.TargetLevel != null)
                {
                    return entrance.TargetLevel.name;
                }
            }

            return string.Empty;
        }

        private void SetEdge(Vector2Int a, Vector2Int b, EdgeType type)
        {
            if (!AreAdjacent(a, b))
            {
                return;
            }

            var key = new EdgeKey(a, b);
            editedEdges.RemoveAll(edge => new EdgeKey(edge.A, edge.B).Equals(key));

            if (type != EdgeType.None)
            {
                editedEdges.Add(new EdgeData { Type = type, A = key.A, B = key.B });
            }
        }

        private bool HasEditedEdgeWall(Vector2Int a, Vector2Int b)
        {
            var key = new EdgeKey(a, b);
            foreach (var edge in editedEdges)
            {
                if (edge.Type == EdgeType.Wall && new EdgeKey(edge.A, edge.B).Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        private void GetNearestEdge(Vector3 worldPosition, out Vector2Int a, out Vector2Int b)
        {
            a = WorldToCell(worldPosition);
            var cellCenter = CellToWorld(a);
            var local = worldPosition - cellCenter;

            if (Mathf.Abs(local.x) > Mathf.Abs(local.y))
            {
                b = a + (local.x >= 0f ? Vector2Int.right : Vector2Int.left);
            }
            else
            {
                b = a + (local.y >= 0f ? Vector2Int.up : Vector2Int.down);
            }
        }

        private void GetPreviewBounds(out Vector2Int min, out Vector2Int max)
        {
            if (TryGetContentBounds(out min, out max))
            {
                min -= new Vector2Int(PreviewPadding, PreviewPadding);
                max += new Vector2Int(PreviewPadding, PreviewPadding);
                return;
            }

            min = Vector2Int.zero;
            max = new Vector2Int(EmptyPreviewWidth - 1, EmptyPreviewHeight - 1);
        }

        private Color GetCellColor(CellType cellType)
        {
            return cellType switch
            {
                CellType.Goal => new Color(0.96f, 0.74f, 0.25f),
                CellType.Ice => new Color(0.54f, 0.82f, 0.96f),
                CellType.Wall => new Color(0.16f, 0.17f, 0.2f),
                CellType.Lock => new Color(0.84f, 0.18f, 0.2f),
                CellType.LaserWall => new Color(0.16f, 0.17f, 0.2f),
                CellType.LevelEntrance => new Color(0.46f, 0.33f, 0.88f),
                _ => new Color(0.72f, 0.75f, 0.74f)
            };
        }

        private void CreateEdgeWallView(Vector2Int a, Vector2Int b)
        {
            var center = (CellToWorld(a) + CellToWorld(b)) * 0.5f;
            var vertical = a.x != b.x;
            var size = vertical ? new Vector2(0.12f, 1.05f) : new Vector2(1.05f, 0.12f);
            CreateView("Edge Wall", EdgeWallPrefab, center, size, new Color(0.92f, 0.28f, 0.32f), 1);
        }

        private GameObject GetCellPrefab(CellType cellType)
        {
            return cellType switch
            {
                CellType.Ground => GroundPrefab,
                CellType.Ice => IcePrefab,
                CellType.Goal => GoalPrefab,
                CellType.Wall => CellWallPrefab,
                CellType.LaserWall => LaserWallPrefab != null ? LaserWallPrefab : CellWallPrefab,
                CellType.LevelEntrance => LevelEntrancePrefab,
                _ => null
            };
        }

        private GameObject CreateView(string objectName, GameObject prefab, Vector3 position, Vector2 fallbackSize, Color fallbackColor, int sortingOrder)
        {
            if (prefab != null)
            {
                var instance = Instantiate(prefab, transform);
                instance.name = objectName;
                instance.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
                instance.transform.position = position;
                return instance;
            }

            return CreateSpriteObject(objectName, position, fallbackSize, fallbackColor, sortingOrder);
        }

        private GameObject CreateLaserBoxView(Vector3 position, LaserDirections directions, int sortingOrder)
        {
            var root = LaserBoxPrefab != null
                ? CreateView("Laser Box", LaserBoxPrefab, position, new Vector2(0.72f, 0.72f), new Color(0.64f, 0.42f, 0.20f), sortingOrder)
                : CreateSpriteObject("Laser Box", position, new Vector2(0.72f, 0.72f), new Color(0.64f, 0.42f, 0.20f), sortingOrder);
            AddLaserEmitterDetails(root, directions, 6);
            return root;
        }

        private void AddLaserEmitterDetails(GameObject parent, LaserDirections directions, int sortingOrder)
        {
            if (parent == null)
            {
                return;
            }

            sortingOrder = Mathf.Max(sortingOrder, 6);
            var core = CreateSpriteObject("Laser Core", parent.transform.position, new Vector2(0.22f, 0.22f), new Color(0.92f, 0.08f, 0.1f), sortingOrder);
            core.transform.SetParent(parent.transform);
            core.transform.localPosition = Vector3.zero;
            AddLaserDirectionTriangles(parent, directions, sortingOrder + 1);
        }

        private void AddLaserDirectionTriangles(GameObject parent, LaserDirections directions, int sortingOrder)
        {
            if (directions.HasFlag(LaserDirections.Up))
            {
                CreateTriangleObject("Laser Direction Up", parent.transform, Direction.Up, sortingOrder);
            }

            if (directions.HasFlag(LaserDirections.Down))
            {
                CreateTriangleObject("Laser Direction Down", parent.transform, Direction.Down, sortingOrder);
            }

            if (directions.HasFlag(LaserDirections.Left))
            {
                CreateTriangleObject("Laser Direction Left", parent.transform, Direction.Left, sortingOrder);
            }

            if (directions.HasFlag(LaserDirections.Right))
            {
                CreateTriangleObject("Laser Direction Right", parent.transform, Direction.Right, sortingOrder);
            }
        }

        private GameObject CreateTriangleObject(string objectName, Transform parent, Direction direction, int sortingOrder)
        {
            var triangle = new GameObject(objectName);
            triangle.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
            triangle.transform.SetParent(parent);
            triangle.transform.localPosition = GetDirectionVector(direction) * 0.39f + new Vector3(0f, 0f, -0.03f);
            var meshFilter = triangle.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateTriangleMesh(direction);
            var meshRenderer = triangle.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = Color.white
            };
            meshRenderer.sortingOrder = sortingOrder;
            return triangle;
        }

        private static Mesh CreateTriangleMesh(Direction direction)
        {
            const float halfWidth = 0.075f;
            const float height = 0.13f;
            Vector3 tip;
            Vector3 left;
            Vector3 right;
            switch (direction)
            {
                case Direction.Up:
                    tip = new Vector3(0f, height * 0.5f, 0f);
                    left = new Vector3(-halfWidth, -height * 0.5f, 0f);
                    right = new Vector3(halfWidth, -height * 0.5f, 0f);
                    break;
                case Direction.Down:
                    tip = new Vector3(0f, -height * 0.5f, 0f);
                    left = new Vector3(halfWidth, height * 0.5f, 0f);
                    right = new Vector3(-halfWidth, height * 0.5f, 0f);
                    break;
                case Direction.Left:
                    tip = new Vector3(-height * 0.5f, 0f, 0f);
                    left = new Vector3(height * 0.5f, -halfWidth, 0f);
                    right = new Vector3(height * 0.5f, halfWidth, 0f);
                    break;
                default:
                    tip = new Vector3(height * 0.5f, 0f, 0f);
                    left = new Vector3(-height * 0.5f, halfWidth, 0f);
                    right = new Vector3(-height * 0.5f, -halfWidth, 0f);
                    break;
            }

            var mesh = new Mesh();
            mesh.vertices = new[] { tip, left, right };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddCellLabel(GameObject parent, string text)
        {
            AddCellLabel(parent, text, GetCellLabelCharacterSize(text));
        }

        private void AddCellLabel(GameObject parent, string text, float characterSize)
        {
            if (parent == null)
            {
                return;
            }

            var labelObject = new GameObject("Label");
            labelObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
            labelObject.transform.SetParent(parent.transform);
            labelObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = characterSize;
            textMesh.fontSize = 64;
            textMesh.color = Color.white;
            var renderer = labelObject.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 5;
        }

        private static float GetCellLabelCharacterSize(string text)
        {
            var length = Mathf.Max(1, text.Length);
            return Mathf.Min(0.07f, 0.26f / length);
        }

        private static float GetEntranceNameCharacterSize(string text)
        {
            var length = Mathf.Max(1, text.Length);
            return Mathf.Min(0.075f, 0.30f / length);
        }

        private GameObject CreateSpriteObject(string objectName, Vector3 position, Vector2 size, Color color, int sortingOrder)
        {
            var view = new GameObject(objectName);
            view.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
            view.transform.SetParent(transform);
            view.transform.position = position;
            view.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = view.AddComponent<SpriteRenderer>();
            renderer.sprite = SpriteFactory.WhiteSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;

            return view;
        }

        public void ClearPreview()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private static Vector2Int WorldToCell(Vector3 worldPosition)
        {
            return new Vector2Int(Mathf.RoundToInt(worldPosition.x / CellSize), Mathf.RoundToInt(worldPosition.y / CellSize));
        }

        private static Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(cell.x * CellSize, cell.y * CellSize, 0f);
        }

        private static Vector3 GetDirectionVector(Direction direction)
        {
            var offset = direction.ToOffset();
            return new Vector3(offset.x, offset.y, 0f);
        }

        private static bool Contains(LevelAsset levelAsset, Vector2Int position)
        {
            return position.x >= 0 && position.x < levelAsset.Width && position.y >= 0 && position.y < levelAsset.Height;
        }

        private static bool ContainsLocal(Vector2Int position, int width, int height)
        {
            return position.x >= 0 && position.x < width && position.y >= 0 && position.y < height;
        }

        private static bool IsSolidCellForAutoGround(CellType cellType)
        {
            return cellType is CellType.Wall or CellType.Lock or CellType.LevelEntrance or CellType.LaserWall;
        }

        private static bool IsSolidCellForEntities(CellType cellType)
        {
            return cellType is CellType.Wall or CellType.Lock or CellType.LevelEntrance or CellType.LaserWall;
        }

        private static bool AreAdjacent(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
        }

        private static bool ContainsInclusive(Vector2Int position, Vector2Int min, Vector2Int max)
        {
            return position.x >= min.x && position.x <= max.x && position.y >= min.y && position.y <= max.y;
        }

        private static void NormalizeBounds(ref Vector2Int min, ref Vector2Int max)
        {
            var normalizedMin = new Vector2Int(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y));
            var normalizedMax = new Vector2Int(Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            min = normalizedMin;
            max = normalizedMax;
        }

        private static void Include(Vector2Int position, ref Vector2Int min, ref Vector2Int max, ref bool hasContent)
        {
            if (!hasContent)
            {
                min = position;
                max = position;
                hasContent = true;
                return;
            }

            min = Vector2Int.Min(min, position);
            max = Vector2Int.Max(max, position);
        }

        private static string GetLaserDirectionText(LaserDirections directions)
        {
            var text = string.Empty;
            if (directions.HasFlag(LaserDirections.Up))
            {
                text += "U";
            }

            if (directions.HasFlag(LaserDirections.Down))
            {
                text += "D";
            }

            if (directions.HasFlag(LaserDirections.Left))
            {
                text += "L";
            }

            if (directions.HasFlag(LaserDirections.Right))
            {
                text += "R";
            }

            return text;
        }
    }

    [Serializable]
    public sealed class CellEditData
    {
        public Vector2Int Position;
        public CellType Type;
    }
}
