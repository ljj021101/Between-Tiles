using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BetweenTiles.Core
{
    public sealed class LevelSolveResult
    {
        public readonly bool IsSolvable;
        public readonly int Steps;
        public readonly int VisitedStates;
        public readonly string Message;

        public LevelSolveResult(bool isSolvable, int steps, int visitedStates, string message)
        {
            IsSolvable = isSolvable;
            Steps = steps;
            VisitedStates = visitedStates;
            Message = message;
        }
    }

    public static class LevelSolver
    {
        public const int DefaultMaxVisitedStates = 2000000;

        public static LevelSolveResult Solve(LevelData level, int maxVisitedStates = DefaultMaxVisitedStates)
        {
            if (!TryReadInitialState(level, out var initialState, out var error))
            {
                return new LevelSolveResult(false, 0, 0, error);
            }

            if (!HasGoal(level))
            {
                return new LevelSolveResult(false, 0, 0, "没有终点。");
            }

            var visited = new HashSet<string>();
            var queue = new Queue<SolverNode>();
            visited.Add(EncodeState(initialState));
            queue.Enqueue(new SolverNode(initialState, 0));

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (IsGoal(level, node.State.Player))
                {
                    return new LevelSolveResult(true, node.Steps, visited.Count, $"有解，最短需要 {node.Steps} 步。");
                }

                if (visited.Count >= maxVisitedStates)
                {
                    return new LevelSolveResult(false, node.Steps, visited.Count, $"搜索超过 {maxVisitedStates} 个状态，暂时无法确认。");
                }

                foreach (var direction in Directions)
                {
                    if (!TryMove(level, node.State, direction, out var nextState))
                    {
                        continue;
                    }

                    var key = EncodeState(nextState);
                    if (!visited.Add(key))
                    {
                        continue;
                    }

                    queue.Enqueue(new SolverNode(nextState, node.Steps + 1));
                }
            }

            return new LevelSolveResult(false, 0, visited.Count, $"无解。已搜索 {visited.Count} 个状态。");
        }

        private static readonly Direction[] Directions =
        {
            Direction.Up,
            Direction.Down,
            Direction.Left,
            Direction.Right
        };

        private static bool TryReadInitialState(LevelData level, out SolverState state, out string error)
        {
            var playerCount = 0;
            var player = Vector2Int.zero;
            var boxes = new List<SolverBox>();

            foreach (var entity in level.Entities)
            {
                if (entity.Type == EntityType.Player)
                {
                    playerCount++;
                    player = entity.Position;
                }
                else if (entity.Type == EntityType.Box)
                {
                    boxes.Add(new SolverBox(EntityType.Box, entity.Position, LaserDirections.None));
                }
                else if (entity.Type == EntityType.LaserBox)
                {
                    boxes.Add(new SolverBox(EntityType.LaserBox, entity.Position, entity.LaserDirections));
                }
            }

            if (playerCount == 0)
            {
                state = default;
                error = "没有玩家。";
                return false;
            }

            if (playerCount > 1)
            {
                state = default;
                error = "玩家数量超过 1 个。";
                return false;
            }

            boxes.Sort(CompareBoxes);
            state = new SolverState(player, boxes.ToArray());
            if (IsHitByLaser(level, state))
            {
                error = "玩家初始位置被激光命中。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool HasGoal(LevelData level)
        {
            for (var x = 0; x < level.Width; x++)
            {
                for (var y = 0; y < level.Height; y++)
                {
                    if (level.Cells[x, y] == CellType.Goal)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsGoal(LevelData level, Vector2Int position)
        {
            return level.Contains(position) && level.Cells[position.x, position.y] == CellType.Goal;
        }

        private static bool TryMove(LevelData level, SolverState state, Direction direction, out SolverState nextState)
        {
            var offset = direction.ToOffset();
            var boxes = (SolverBox[])state.Boxes.Clone();
            var player = state.Player;
            var firstBoxIndex = FindBox(boxes, player + offset);

            if (IsSlippery(level, player) && firstBoxIndex >= 0)
            {
                if (!TryStep(level, player, boxes, offset, true, out _))
                {
                    nextState = default;
                    return false;
                }

                SortBoxes(boxes);
                nextState = new SolverState(player, boxes);
                if (IsHitByLaser(level, nextState))
                {
                    nextState = default;
                    return false;
                }

                return true;
            }

            if (!TryStep(level, player, boxes, offset, false, out player))
            {
                nextState = default;
                return false;
            }

            if (IsHitByLaser(level, CreateSortedState(player, boxes)))
            {
                nextState = default;
                return false;
            }

            while (IsSlippery(level, player))
            {
                var beforeSlidePlayer = player;
                if (!TryStep(level, player, boxes, offset, true, out player))
                {
                    player = beforeSlidePlayer;
                    break;
                }

                if (player == beforeSlidePlayer)
                {
                    break;
                }

                if (IsHitByLaser(level, CreateSortedState(player, boxes)))
                {
                    nextState = default;
                    return false;
                }
            }

            SortBoxes(boxes);
            nextState = new SolverState(player, boxes);
            if (IsHitByLaser(level, nextState))
            {
                nextState = default;
                return false;
            }

            return true;
        }

        private static bool TryStep(LevelData level, Vector2Int player, SolverBox[] boxes, Vector2Int offset, bool stopBeforePushedBox, out Vector2Int nextPlayer)
        {
            nextPlayer = player;
            var target = player + offset;

            if (!CanEnter(level, player, target))
            {
                return false;
            }

            var firstBoxIndex = FindBox(boxes, target);
            if (firstBoxIndex >= 0 && !TryPushBoxLine(level, boxes, firstBoxIndex, offset))
            {
                return false;
            }

            if (firstBoxIndex >= 0 && stopBeforePushedBox)
            {
                return true;
            }

            nextPlayer = target;
            return true;
        }

        private static bool IsSlippery(LevelData level, Vector2Int position)
        {
            return level.Contains(position) && RuleDefinitions.Cells[level.Cells[position.x, position.y]].IsSlippery;
        }

        private static bool TryPushBoxLine(LevelData level, SolverBox[] boxes, int firstBoxIndex, Vector2Int offset)
        {
            var pushLine = new List<int> { firstBoxIndex };
            var currentPosition = boxes[firstBoxIndex].Position;

            while (true)
            {
                var nextPosition = currentPosition + offset;
                if (!CanEnter(level, currentPosition, nextPosition))
                {
                    return false;
                }

                var nextBoxIndex = FindBox(boxes, nextPosition);
                if (nextBoxIndex < 0)
                {
                    for (var i = 0; i < pushLine.Count; i++)
                    {
                        boxes[pushLine[i]].Position += offset;
                    }

                    SlidePushedBoxes(level, boxes, pushLine, offset);
                    return true;
                }

                pushLine.Add(nextBoxIndex);
                currentPosition = nextPosition;
            }
        }

        private static void SlidePushedBoxes(LevelData level, SolverBox[] boxes, List<int> pushedBoxes, Vector2Int offset)
        {
            while (true)
            {
                var slidingBoxes = GetSlipperyPushedBoxes(level, boxes, pushedBoxes, offset);
                if (slidingBoxes.Count == 0)
                {
                    return;
                }

                var slidingSet = new HashSet<int>(slidingBoxes);
                var movableBoxes = new HashSet<int>();
                foreach (var boxIndex in slidingBoxes)
                {
                    if (TryResolveSlidingBoxStep(level, boxes, boxIndex, offset, slidingSet, movableBoxes))
                    {
                        movableBoxes.Add(boxIndex);
                    }
                }

                if (movableBoxes.Count == 0)
                {
                    return;
                }

                foreach (var boxIndex in movableBoxes)
                {
                    boxes[boxIndex].Position += offset;
                }
            }
        }

        private static List<int> GetSlipperyPushedBoxes(LevelData level, SolverBox[] boxes, List<int> pushedBoxes, Vector2Int offset)
        {
            var slidingBoxes = new List<int>();
            foreach (var boxIndex in pushedBoxes)
            {
                if (IsSlippery(level, boxes[boxIndex].Position))
                {
                    slidingBoxes.Add(boxIndex);
                }
            }

            slidingBoxes.Sort((left, right) =>
                GetDirectionProjection(boxes[right].Position, offset).CompareTo(GetDirectionProjection(boxes[left].Position, offset)));
            return slidingBoxes;
        }

        private static bool TryResolveSlidingBoxStep(
            LevelData level,
            SolverBox[] boxes,
            int boxIndex,
            Vector2Int offset,
            HashSet<int> slidingBoxes,
            HashSet<int> movableBoxes)
        {
            var from = boxes[boxIndex].Position;
            var to = from + offset;
            if (!CanEnter(level, from, to))
            {
                return false;
            }

            var blockingBoxIndex = FindBox(boxes, to);
            if (blockingBoxIndex < 0)
            {
                return true;
            }

            if (slidingBoxes.Contains(blockingBoxIndex))
            {
                return movableBoxes.Contains(blockingBoxIndex);
            }

            TryPushBoxLine(level, boxes, blockingBoxIndex, offset);
            return false;
        }

        private static bool CanEnter(LevelData level, Vector2Int from, Vector2Int to)
        {
            if (!level.Contains(to))
            {
                return false;
            }

            if (RuleDefinitions.Edges[GetEdge(level, from, to)].BlocksMovement)
            {
                return false;
            }

            return RuleDefinitions.Cells[level.Cells[to.x, to.y]].Walkable;
        }

        private static bool IsHitByLaser(LevelData level, SolverState state)
        {
            foreach (var pair in level.CellLasers)
            {
                if (LaserHitsPlayer(level, state, pair.Key, pair.Value))
                {
                    return true;
                }
            }

            foreach (var box in state.Boxes)
            {
                if (box.Type == EntityType.LaserBox && LaserHitsPlayer(level, state, box.Position, box.LaserDirections))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LaserHitsPlayer(LevelData level, SolverState state, Vector2Int origin, LaserDirections directions)
        {
            if (directions.HasFlag(LaserDirections.Up) && LaserRayHitsPlayer(level, state, origin, Direction.Up))
            {
                return true;
            }

            if (directions.HasFlag(LaserDirections.Down) && LaserRayHitsPlayer(level, state, origin, Direction.Down))
            {
                return true;
            }

            if (directions.HasFlag(LaserDirections.Left) && LaserRayHitsPlayer(level, state, origin, Direction.Left))
            {
                return true;
            }

            return directions.HasFlag(LaserDirections.Right) && LaserRayHitsPlayer(level, state, origin, Direction.Right);
        }

        private static bool LaserRayHitsPlayer(LevelData level, SolverState state, Vector2Int origin, Direction direction)
        {
            var previous = origin;
            var current = origin + direction.ToOffset();
            while (level.Contains(current) && !RuleDefinitions.Edges[GetEdge(level, previous, current)].BlocksMovement)
            {
                if (state.Player == current)
                {
                    return true;
                }

                if (BlocksLaser(level, state.Boxes, current))
                {
                    return false;
                }

                previous = current;
                current += direction.ToOffset();
            }

            return false;
        }

        private static bool BlocksLaser(LevelData level, SolverBox[] boxes, Vector2Int position)
        {
            var cell = level.Cells[position.x, position.y];
            if (!RuleDefinitions.Cells[cell].Walkable || cell == CellType.LaserWall)
            {
                return true;
            }

            return FindBox(boxes, position) >= 0;
        }

        private static EdgeType GetEdge(LevelData level, Vector2Int from, Vector2Int to)
        {
            return level.Edges.TryGetValue(new EdgeKey(from, to), out var edge) ? edge : EdgeType.None;
        }

        private static int FindBox(SolverBox[] boxes, Vector2Int position)
        {
            for (var i = 0; i < boxes.Length; i++)
            {
                if (boxes[i].Position == position)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string EncodeState(SolverState state)
        {
            var builder = new StringBuilder();
            builder.Append(state.Player.x);
            builder.Append(',');
            builder.Append(state.Player.y);
            builder.Append('|');

            for (var i = 0; i < state.Boxes.Length; i++)
            {
                builder.Append((int)state.Boxes[i].Type);
                builder.Append(':');
                builder.Append((int)state.Boxes[i].LaserDirections);
                builder.Append('@');
                builder.Append(state.Boxes[i].Position.x);
                builder.Append(',');
                builder.Append(state.Boxes[i].Position.y);
                builder.Append(';');
            }

            return builder.ToString();
        }

        private static SolverState CreateSortedState(Vector2Int player, SolverBox[] boxes)
        {
            var sortedBoxes = (SolverBox[])boxes.Clone();
            SortBoxes(sortedBoxes);
            return new SolverState(player, sortedBoxes);
        }

        private static void SortBoxes(SolverBox[] boxes)
        {
            System.Array.Sort(boxes, CompareBoxes);
        }

        private static int CompareBoxes(SolverBox a, SolverBox b)
        {
            var yCompare = a.Position.y.CompareTo(b.Position.y);
            if (yCompare != 0)
            {
                return yCompare;
            }

            var xCompare = a.Position.x.CompareTo(b.Position.x);
            if (xCompare != 0)
            {
                return xCompare;
            }

            var typeCompare = a.Type.CompareTo(b.Type);
            return typeCompare != 0 ? typeCompare : a.LaserDirections.CompareTo(b.LaserDirections);
        }

        private static int GetDirectionProjection(Vector2Int position, Vector2Int direction)
        {
            return position.x * direction.x + position.y * direction.y;
        }
    }

    public readonly struct SolverState
    {
        public readonly Vector2Int Player;
        public readonly SolverBox[] Boxes;

        public SolverState(Vector2Int player, SolverBox[] boxes)
        {
            Player = player;
            Boxes = boxes;
        }
    }

    public struct SolverBox
    {
        public readonly EntityType Type;
        public Vector2Int Position;
        public readonly LaserDirections LaserDirections;

        public SolverBox(EntityType type, Vector2Int position, LaserDirections laserDirections)
        {
            Type = type;
            Position = position;
            LaserDirections = laserDirections;
        }
    }

    public readonly struct SolverNode
    {
        public readonly SolverState State;
        public readonly int Steps;

        public SolverNode(SolverState state, int steps)
        {
            State = state;
            Steps = steps;
        }
    }
}
