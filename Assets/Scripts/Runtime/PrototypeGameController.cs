using BetweenTiles.Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetweenTiles.Runtime
{
    public sealed class PrototypeGameController : MonoBehaviour
    {
        private const float CellSize = 1f;
        private BoardRuntime board;
        private LevelAsset currentLevelAsset;
        private Camera mainCamera;
        private GameObject playerView;
        private readonly Dictionary<EntityInstance, GameObject> entityViews = new();
        private readonly Dictionary<Vector2Int, EntranceNameLabel> entranceNameLabels = new();
        private readonly List<GameObject> laserViews = new();
        private bool completed;
        private bool isDead;
        private bool isMoving;
        private bool isResolvingAction;
        private bool cameraFollowsPlayer;
        private Vector3 fixedCameraPosition;
        private Direction? preferredHeldDirection;
        private ReturnContext pendingEntrance;
        private bool pendingCompletion;
        private readonly Stack<BoardSnapshot> undoStack = new();
        private readonly Stack<ReturnContext> returnStack = new();
        private readonly HashSet<LevelAsset> completedLevels = new();
        private GameSaveData saveData;

        [SerializeField]
        private LevelAsset startingLevel;

        [SerializeField]
        private float moveDuration = 0.12f;

        [SerializeField]
        private float iceMoveDuration = 0.07f;

        [SerializeField]
        private float repeatMoveDelay = 0.06f;

        [SerializeField]
        private float entranceNameFadeSpeed = 8f;

        [SerializeField]
        private float cameraOrthographicSize = 6.5f;

        [SerializeField]
        private float cameraPadding = 1f;

        [Header("View Prefabs")]
        [SerializeField]
        private GameObject groundPrefab;

        [SerializeField]
        private GameObject icePrefab;

        [SerializeField]
        private GameObject goalPrefab;

        [SerializeField]
        private GameObject cellWallPrefab;

        [SerializeField]
        private GameObject levelEntrancePrefab;

        [SerializeField]
        private GameObject edgeWallPrefab;

        [SerializeField]
        private GameObject playerPrefab;

        [SerializeField]
        private GameObject boxPrefab;

        [SerializeField]
        private GameObject laserBoxPrefab;

        [SerializeField]
        private GameObject laserWallPrefab;

        private void Start()
        {
            saveData = GameSaveSystem.Load();
            SetupCamera();

            var editorSelectedLevel = GetEditorSelectedLevel();
            if (editorSelectedLevel != null)
            {
                LoadLevel(editorSelectedLevel);
            }
            else if (startingLevel != null)
            {
                LoadLevel(startingLevel);
            }
            else
            {
                LoadDemoLevel();
            }
        }

        private LevelAsset GetEditorSelectedLevel()
        {
            var levelEditor = FindFirstObjectByType<LevelEditorController>();
            return levelEditor != null ? levelEditor.CurrentLevel : null;
        }

        private void OnValidate()
        {
            moveDuration = Mathf.Max(0.01f, moveDuration);
            iceMoveDuration = Mathf.Max(0.01f, iceMoveDuration);
            repeatMoveDelay = Mathf.Max(0f, repeatMoveDelay);
            entranceNameFadeSpeed = Mathf.Max(0.1f, entranceNameFadeSpeed);
            cameraOrthographicSize = Mathf.Max(0.5f, cameraOrthographicSize);
            cameraPadding = Mathf.Max(0f, cameraPadding);
        }

        private void Update()
        {
            UpdateEntranceNameLabels();

            if (Keyboard.current == null)
            {
                return;
            }

            RefreshPreferredHeldDirection();

            if (Keyboard.current.escapeKey.wasPressedThisFrame && !isMoving && !isResolvingAction)
            {
                HandleEscapePressed();
                return;
            }

            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                ReloadCurrentLevel();
                return;
            }

            if (Keyboard.current.zKey.wasPressedThisFrame && !isMoving && !isResolvingAction)
            {
                UndoMove();
                return;
            }

            if (TryGetPressedDirection(out var direction))
            {
                if (isMoving || isResolvingAction)
                {
                    return;
                }

                if (!completed && !isDead)
                {
                    TryMove(direction);
                }

                return;
            }

            if (completed || isMoving || isResolvingAction)
            {
                return;
            }
        }

        private void LoadDemoLevel()
        {
            StopAllCoroutines();
            isMoving = false;
            isResolvingAction = false;
            isDead = false;
            preferredHeldDirection = null;

            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            completed = false;
            undoStack.Clear();
            entityViews.Clear();
            entranceNameLabels.Clear();
            laserViews.Clear();
            board = new BoardRuntime(DemoLevelFactory.CreateFirstPrototype(), GetCompletedLevelCount());
            currentLevelAsset = null;
            DrawBoard();
            ConfigureCameraForLevel();
        }

        private void LoadLevel(LevelAsset levelAsset, Vector2Int? playerOverridePosition = null, BoardSnapshot restoreSnapshot = null)
        {
            StopAllCoroutines();
            isMoving = false;
            isResolvingAction = false;
            completed = false;
            isDead = false;
            pendingEntrance = null;
            pendingCompletion = false;
            preferredHeldDirection = null;
            undoStack.Clear();
            entityViews.Clear();
            entranceNameLabels.Clear();
            laserViews.Clear();

            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            currentLevelAsset = levelAsset;
            var levelData = levelAsset.ToLevelData();
            ApplySavedClearedLocks(levelAsset, levelData);
            board = new BoardRuntime(levelData, GetCompletedLevelCount());
            if (restoreSnapshot != null)
            {
                board.RestoreSnapshot(restoreSnapshot);
                ApplySavedClearedLocksToBoard();
            }

            if (playerOverridePosition.HasValue)
            {
                board.MovePlayerTo(playerOverridePosition.Value);
            }

            DrawBoard();
            ConfigureCameraForLevel();
        }

        private void ReloadCurrentLevel()
        {
            if (currentLevelAsset != null)
            {
                LoadLevel(currentLevelAsset);
            }
            else
            {
                LoadDemoLevel();
            }
        }

        private void HandleEscapePressed()
        {
            if (returnStack.Count > 0)
            {
                ReturnToPreviousLevel();
                return;
            }

            QuitGame();
        }

        private void ReturnToPreviousLevel()
        {
            pendingEntrance = null;
            pendingCompletion = false;
            completed = false;
            isDead = false;

            var context = returnStack.Pop();
            LoadLevel(context.SourceLevel, context.ReturnPosition, context.SourceSnapshot);
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void TryMove(Direction direction)
        {
            if (TryEnterFacingEntrance(direction))
            {
                return;
            }

            var snapshot = board.CreateSnapshot();
            StartCoroutine(ResolvePlayerAction(direction, snapshot, ShouldPushOnlyFromIce(direction)));
        }

        private bool TryEnterFacingEntrance(Direction direction)
        {
            if (board?.Player == null)
            {
                return false;
            }

            var from = board.Player.Position;
            var targetPosition = from + direction.ToOffset();
            if (!IsInsideBoard(targetPosition))
            {
                return false;
            }

            if (RuleDefinitions.Edges[board.GetEdge(from, targetPosition)].BlocksMovement)
            {
                return false;
            }

            if (board.GetCell(targetPosition) != CellType.LevelEntrance)
            {
                return false;
            }

            var targetLevel = board.GetEntranceTarget(targetPosition);
            if (targetLevel == null)
            {
                return false;
            }

            pendingEntrance = new ReturnContext(
                currentLevelAsset,
                from,
                targetPosition,
                targetLevel,
                board.CreateSnapshot());
            EnterPendingLevel();
            return true;
        }

        private void UndoMove()
        {
            if (undoStack.Count == 0)
            {
                return;
            }

            completed = false;
            isDead = false;
            playerView.GetComponent<SpriteRenderer>().color = new Color(0.22f, 0.42f, 0.95f);

            var previousSnapshot = undoStack.Pop();
            board.RestoreSnapshot(previousSnapshot);
            ApplySavedClearedLocksToBoard();
            RedrawBoardViews();
            UpdateCameraPosition(true);
        }

        private IEnumerator ResolvePlayerAction(Direction direction, BoardSnapshot snapshot, bool pushOnlyFromIce)
        {
            isResolvingAction = true;

            var firstResult = pushOnlyFromIce
                ? board.TrySlidePlayer(direction)
                : board.TryMovePlayer(direction);
            if (!IsSuccessfulMove(firstResult))
            {
                isResolvingAction = false;
                yield break;
            }

            SaveOpenedLocks();
            undoStack.Push(snapshot);
            var openedLockDuringAction = firstResult == MoveResult.OpenedLock || board.OpenedLockDuringLastMove;
            var firstStepImpactPushes = new List<ImpactPushEvent>(board.LastImpactPushes);
            var firstStepIsSliding = board.IsSlippery(board.Player.Position);
            var firstStepFromPositions = CaptureEntityViewPositions(snapshot);
            var firstStepMaxDistance = GetMaxGridMoveDistance(firstStepFromPositions);
            yield return AnimateEntityMoves(
                firstStepFromPositions,
                firstStepIsSliding ? iceMoveDuration : moveDuration,
                !firstStepIsSliding && firstStepMaxDistance <= 1f,
                firstStepImpactPushes);

            if (isDead || RedrawLaserViews())
            {
                SetLaserFailure();
                yield break;
            }

            HandlePostMoveTriggers(firstResult);
            if (pendingCompletion || pendingEntrance != null)
            {
                FinishResolvedAction();
                yield break;
            }

            if (firstResult == MoveResult.PushedEntityOnly)
            {
                yield return FinishActionAfterLaserCheck(openedLockDuringAction);
                yield break;
            }

            while (board.IsSlippery(board.Player.Position))
            {
                var slideSnapshot = board.CreateSnapshot();
                var slideResult = board.TrySlidePlayer(direction);
                if (!IsSuccessfulMove(slideResult))
                {
                    break;
                }

                SaveOpenedLocks();
                var slideImpactPushes = new List<ImpactPushEvent>(board.LastImpactPushes);
                var slideFromPositions = CaptureEntityViewPositions(slideSnapshot);
                yield return AnimateEntityMoves(
                    slideFromPositions,
                    iceMoveDuration,
                    false,
                    slideImpactPushes);

                if (isDead || RedrawLaserViews())
                {
                    SetLaserFailure();
                    yield break;
                }

                if (slideResult == MoveResult.Moved || slideResult == MoveResult.Completed || slideResult == MoveResult.OpenedLock)
                {
                    openedLockDuringAction |= slideResult == MoveResult.OpenedLock || board.OpenedLockDuringLastMove;
                    HandlePostMoveTriggers(slideResult);
                    if (pendingCompletion || pendingEntrance != null)
                    {
                        break;
                    }
                }

                if (slideResult == MoveResult.PushedEntityOnly)
                {
                    break;
                }

                if (!board.IsSlippery(board.Player.Position))
                {
                    break;
                }
            }

            if (pendingCompletion || pendingEntrance != null)
            {
                if (openedLockDuringAction)
                {
                    RedrawBoardViews();
                }

                FinishResolvedAction();
                yield break;
            }

            yield return FinishActionAfterLaserCheck(openedLockDuringAction);
        }

        private IEnumerator FinishActionAfterLaserCheck(bool redrawOpenedLocks)
        {
            if (RedrawLaserViews())
            {
                SetLaserFailure();
                yield break;
            }

            if (redrawOpenedLocks)
            {
                RedrawBoardViews();
            }

            FinishResolvedAction();
        }

        private void SetLaserFailure()
        {
            isResolvingAction = false;
            isMoving = false;
            isDead = true;
            preferredHeldDirection = null;
            if (playerView != null && playerView.TryGetComponent<SpriteRenderer>(out var renderer))
            {
                renderer.color = new Color(1f, 0.18f, 0.18f);
            }
        }

        private IEnumerator AnimateEntityMoves(
            Dictionary<EntityInstance, Vector3> fromPositions,
            float baseDuration,
            bool useSmoothStep,
            IReadOnlyList<ImpactPushEvent> impactPushes)
        {
            isMoving = true;
            var animations = BuildEntityMoveAnimations(fromPositions, baseDuration, useSmoothStep, impactPushes);
            var duration = GetTotalAnimationDuration(animations, baseDuration);
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                foreach (var animation in animations)
                {
                    if (!entityViews.TryGetValue(animation.Entity, out var view))
                    {
                        continue;
                    }

                    var t = animation.Duration <= 0f
                        ? 1f
                        : Mathf.Clamp01((elapsed - animation.Delay) / animation.Duration);
                    if (animation.UseSmoothStep)
                    {
                        t = Mathf.SmoothStep(0f, 1f, t);
                    }

                    view.transform.position = Vector3.Lerp(animation.FromPosition, animation.ToPosition, t);
                }

                UpdateCameraPosition(false);
                if (RedrawLaserViews())
                {
                    SetLaserFailure();
                    yield break;
                }

                yield return null;
            }

            SyncEntityViews();
            UpdateCameraPosition(false);
        }

        private List<EntityMoveAnimation> BuildEntityMoveAnimations(
            Dictionary<EntityInstance, Vector3> fromPositions,
            float baseDuration,
            bool useSmoothStep,
            IReadOnlyList<ImpactPushEvent> impactPushes)
        {
            var animations = new List<EntityMoveAnimation>();
            foreach (var pair in fromPositions)
            {
                var toPosition = GridToWorld(pair.Key.Position);
                var distance = Vector3.Distance(pair.Value, toPosition) / CellSize;
                if (distance <= 0.001f)
                {
                    continue;
                }

                animations.Add(new EntityMoveAnimation(
                    pair.Key,
                    pair.Value,
                    toPosition,
                    GetEntityAnimationDuration(baseDuration, distance),
                    useSmoothStep && distance <= 1f,
                    distance));
            }

            ApplyImpactDelays(animations, impactPushes);
            return animations;
        }

        private void ApplyImpactDelays(List<EntityMoveAnimation> animations, IReadOnlyList<ImpactPushEvent> impactPushes)
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var impactPush in impactPushes)
                {
                    var sourceAnimation = FindEntityAnimation(animations, impactPush.Source);
                    if (sourceAnimation == null)
                    {
                        continue;
                    }

                    var delay = sourceAnimation.Delay + sourceAnimation.Duration;
                    foreach (var pushedEntity in impactPush.PushedEntities)
                    {
                        var pushedAnimation = FindEntityAnimation(animations, pushedEntity);
                        if (pushedAnimation == null || delay <= pushedAnimation.Delay + 0.001f)
                        {
                            continue;
                        }

                        pushedAnimation.Delay = delay;
                        pushedAnimation.UseSmoothStep = false;
                        changed = true;
                    }
                }
            }
        }

        private float GetEntityAnimationDuration(float baseDuration, float gridDistance)
        {
            return gridDistance <= 1f ? baseDuration : iceMoveDuration * gridDistance;
        }

        private static float GetTotalAnimationDuration(List<EntityMoveAnimation> animations, float fallbackDuration)
        {
            var duration = fallbackDuration;
            foreach (var animation in animations)
            {
                duration = Mathf.Max(duration, animation.Delay + animation.Duration);
            }

            return duration;
        }

        private static EntityMoveAnimation FindEntityAnimation(List<EntityMoveAnimation> animations, EntityInstance entity)
        {
            foreach (var animation in animations)
            {
                if (animation.Entity == entity)
                {
                    return animation;
                }
            }

            return null;
        }

        private void FinishResolvedAction()
        {
            isResolvingAction = false;

            if (pendingCompletion)
            {
                CompleteCurrentLevel();
                return;
            }

            if (pendingEntrance != null)
            {
                EnterPendingLevel();
                return;
            }

            StartCoroutine(FinishActionAfterRepeatDelay());
        }

        private IEnumerator FinishActionAfterRepeatDelay()
        {
            if (!completed && !isDead && repeatMoveDelay > 0f)
            {
                yield return new WaitForSeconds(repeatMoveDelay);
            }

            isMoving = false;

            if (completed || isDead)
            {
                yield break;
            }

            if (TryGetPreferredHeldDirection(out var heldDirection))
            {
                TryMove(heldDirection);
            }
        }

        private void HandlePostMoveTriggers(MoveResult result)
        {
            if (result == MoveResult.Completed)
            {
                completed = true;
                pendingCompletion = true;
                playerView.GetComponent<SpriteRenderer>().color = new Color(0.28f, 0.95f, 0.64f);
                Debug.Log("Level complete.");
            }
        }

        private bool IsSuccessfulMove(MoveResult result)
        {
            return result is MoveResult.Moved or MoveResult.Completed or MoveResult.PushedEntityOnly or MoveResult.OpenedLock;
        }

        private bool ShouldPushOnlyFromIce(Direction direction)
        {
            if (board?.Player == null || !board.IsSlippery(board.Player.Position))
            {
                return false;
            }

            var targetPosition = board.Player.Position + direction.ToOffset();
            return IsInsideBoard(targetPosition) && board.HasBlockingEntity(targetPosition);
        }

        private void SaveOpenedLocks()
        {
            if (currentLevelAsset == null || board.LastOpenedLocks.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var position in board.LastOpenedLocks)
            {
                changed |= saveData.MarkLockCleared(currentLevelAsset, position);
            }

            if (changed)
            {
                GameSaveSystem.Save(saveData);
            }
        }

        private void ApplySavedClearedLocks(LevelAsset levelAsset, LevelData levelData)
        {
            foreach (var position in saveData.GetClearedLocks(levelAsset))
            {
                if (levelData.Contains(position) && levelData.Cells[position.x, position.y] == CellType.Lock)
                {
                    levelData.SetCell(position, CellType.Ground);
                }
            }
        }

        private void ApplySavedClearedLocksToBoard()
        {
            if (currentLevelAsset != null)
            {
                board.ApplyClearedLocks(saveData.GetClearedLocks(currentLevelAsset));
            }
        }

        private int GetCompletedLevelCount()
        {
            return saveData != null ? saveData.CompletedLevelCount : completedLevels.Count;
        }

        private bool IsLevelCompleted(LevelAsset levelAsset)
        {
            return completedLevels.Contains(levelAsset) || saveData != null && saveData.IsLevelCompleted(levelAsset);
        }

        private void EnterPendingLevel()
        {
            if (pendingEntrance.SourceLevel != null)
            {
                returnStack.Push(pendingEntrance);
            }

            var targetLevel = pendingEntrance.EnteredLevel;
            pendingEntrance = null;
            LoadLevel(targetLevel);
        }

        private void CompleteCurrentLevel()
        {
            pendingCompletion = false;

            if (currentLevelAsset != null)
            {
                completedLevels.Add(currentLevelAsset);
                if (saveData.MarkLevelCompleted(currentLevelAsset))
                {
                    GameSaveSystem.Save(saveData);
                }
            }

            if (returnStack.Count == 0)
            {
                isMoving = false;
                Debug.Log("Top level complete. Press R to restart.");
                return;
            }

            ReturnToPreviousLevel();
        }

        private void RefreshPreferredHeldDirection()
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame)
            {
                preferredHeldDirection = Direction.Up;
            }

            if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame)
            {
                preferredHeldDirection = Direction.Down;
            }

            if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame)
            {
                preferredHeldDirection = Direction.Left;
            }

            if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame)
            {
                preferredHeldDirection = Direction.Right;
            }

            if (preferredHeldDirection.HasValue && !IsDirectionHeld(preferredHeldDirection.Value))
            {
                preferredHeldDirection = null;
            }
        }

        private bool TryGetPressedDirection(out Direction direction)
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame)
            {
                direction = Direction.Up;
                return true;
            }

            if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame)
            {
                direction = Direction.Down;
                return true;
            }

            if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame)
            {
                direction = Direction.Left;
                return true;
            }

            if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame)
            {
                direction = Direction.Right;
                return true;
            }

            direction = Direction.Up;
            return false;
        }

        private bool TryGetPreferredHeldDirection(out Direction direction)
        {
            RefreshPreferredHeldDirection();

            if (preferredHeldDirection.HasValue)
            {
                direction = preferredHeldDirection.Value;
                return true;
            }

            if (Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed)
            {
                direction = Direction.Up;
                return true;
            }

            if (Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed)
            {
                direction = Direction.Down;
                return true;
            }

            if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed)
            {
                direction = Direction.Left;
                return true;
            }

            if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed)
            {
                direction = Direction.Right;
                return true;
            }

            direction = Direction.Up;
            return false;
        }

        private bool IsDirectionHeld(Direction direction)
        {
            return direction switch
            {
                Direction.Up => Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed,
                Direction.Down => Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed,
                Direction.Left => Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed,
                Direction.Right => Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed,
                _ => false
            };
        }

        private void DrawBoard()
        {
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var position = new Vector2Int(x, y);
                    var cell = board.GetCell(position);
                    CreateCellView(position, cell);

                    if (x < board.Width - 1 && board.GetEdge(position, position + Vector2Int.right) == EdgeType.Wall)
                    {
                        CreateEdgeWallView(position, Direction.Right);
                    }

                    if (y < board.Height - 1 && board.GetEdge(position, position + Vector2Int.up) == EdgeType.Wall)
                    {
                        CreateEdgeWallView(position, Direction.Up);
                    }
                }
            }

            foreach (var entity in board.Entities)
            {
                var view = entity.Type switch
                {
                    EntityType.Player => CreateEntityView("Player", EntityType.Player, GridToWorld(entity.Position), new Vector2(0.62f, 0.62f), new Color(0.22f, 0.42f, 0.95f), 3),
                    EntityType.Box => CreateEntityView("Box", EntityType.Box, GridToWorld(entity.Position), new Vector2(0.72f, 0.72f), new Color(0.64f, 0.42f, 0.20f), 2),
                    EntityType.LaserBox => CreateLaserBoxView(GridToWorld(entity.Position), entity.LaserDirections, 2),
                    _ => null
                };

                if (view == null)
                {
                    continue;
                }

                entityViews[entity] = view;
                if (entity.Type == EntityType.Player)
                {
                    playerView = view;
                }
            }

            RedrawLaserViews();
        }

        private void RedrawBoardViews()
        {
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            entityViews.Clear();
            entranceNameLabels.Clear();
            laserViews.Clear();
            playerView = null;
            DrawBoard();
        }

        private bool RedrawLaserViews()
        {
            for (var i = laserViews.Count - 1; i >= 0; i--)
            {
                if (laserViews[i] != null)
                {
                    Destroy(laserViews[i]);
                }
            }

            laserViews.Clear();
            if (board == null)
            {
                return false;
            }

            var hitPlayer = false;
            foreach (var pair in board.CellLasers)
            {
                hitPlayer |= DrawLaserEmitter(GridToWorld(pair.Key), pair.Value, 0.5f, null);
            }

            foreach (var entity in board.Entities)
            {
                if (entity.Type != EntityType.LaserBox || !entityViews.TryGetValue(entity, out var view))
                {
                    continue;
                }

                hitPlayer |= DrawLaserEmitter(view.transform.position, entity.LaserDirections, 0.36f, entity);
            }

            return hitPlayer;
        }

        private bool DrawLaserEmitter(Vector3 origin, LaserDirections directions, float startInset, EntityInstance sourceEntity)
        {
            var hitPlayer = false;
            if (directions.HasFlag(LaserDirections.Up))
            {
                hitPlayer |= DrawLaserBeam(origin, Direction.Up, startInset, sourceEntity);
            }

            if (directions.HasFlag(LaserDirections.Down))
            {
                hitPlayer |= DrawLaserBeam(origin, Direction.Down, startInset, sourceEntity);
            }

            if (directions.HasFlag(LaserDirections.Left))
            {
                hitPlayer |= DrawLaserBeam(origin, Direction.Left, startInset, sourceEntity);
            }

            if (directions.HasFlag(LaserDirections.Right))
            {
                hitPlayer |= DrawLaserBeam(origin, Direction.Right, startInset, sourceEntity);
            }

            return hitPlayer;
        }

        private bool DrawLaserBeam(Vector3 origin, Direction direction, float startInset, EntityInstance sourceEntity)
        {
            var directionVector = GetDirectionVector(direction);
            var endDistance = FindLaserEndDistance(origin, direction, startInset, sourceEntity);
            var length = endDistance - startInset;
            if (length <= 0.001f)
            {
                return false;
            }

            var center = origin + directionVector * ((startInset + endDistance) * 0.5f);
            var size = direction is Direction.Up or Direction.Down
                ? new Vector2(0.18f, length)
                : new Vector2(length, 0.18f);
            var view = CreateSpriteObject("Laser", center, size, new Color(1f, 0.05f, 0.08f, 0.9f), 4);
            laserViews.Add(view);
            return IsPlayerInsideLaser(center, size);
        }

        private float FindLaserEndDistance(Vector3 origin, Direction direction, float startInset, EntityInstance sourceEntity)
        {
            var endDistance = GetBoardEdgeDistance(origin, direction);

            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var position = new Vector2Int(x, y);
                    if (IsLaserBlockingCell(position))
                    {
                        TryShortenLaser(origin, direction, GridToWorld(position), 0.5f, startInset, ref endDistance);
                    }

                    if (x < board.Width - 1 && board.GetEdge(position, position + Vector2Int.right) == EdgeType.Wall)
                    {
                        TryShortenLaserByEdge(origin, direction, GridToWorld(position) + new Vector3(CellSize * 0.5f, 0f, 0f), true, startInset, ref endDistance);
                    }

                    if (y < board.Height - 1 && board.GetEdge(position, position + Vector2Int.up) == EdgeType.Wall)
                    {
                        TryShortenLaserByEdge(origin, direction, GridToWorld(position) + new Vector3(0f, CellSize * 0.5f, 0f), false, startInset, ref endDistance);
                    }
                }
            }

            foreach (var entity in board.Entities)
            {
                if (entity == sourceEntity || entity.Type == EntityType.Player || !entityViews.TryGetValue(entity, out var view))
                {
                    continue;
                }

                if (RuleDefinitions.Entities[entity.Type].BlocksMovement)
                {
                    TryShortenLaser(origin, direction, view.transform.position, 0.36f, startInset, ref endDistance);
                }
            }

            return Mathf.Max(startInset, endDistance);
        }

        private bool IsLaserBlockingCell(Vector2Int position)
        {
            var cell = board.GetCell(position);
            if (cell == CellType.Lock && board.IsLockOpen(position))
            {
                return false;
            }

            return !RuleDefinitions.Cells[cell].Walkable || cell == CellType.LaserWall;
        }

        private void TryShortenLaser(
            Vector3 origin,
            Direction direction,
            Vector3 blockCenter,
            float blockHalfSize,
            float startInset,
            ref float endDistance)
        {
            TryShortenLaser(origin, direction, blockCenter, blockHalfSize, blockHalfSize, startInset, ref endDistance);
        }

        private void TryShortenLaserByEdge(
            Vector3 origin,
            Direction direction,
            Vector3 edgeCenter,
            bool verticalEdge,
            float startInset,
            ref float endDistance)
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

        private float GetBoardEdgeDistance(Vector3 origin, Direction direction)
        {
            var minCell = GridToWorld(Vector2Int.zero);
            var maxCell = GridToWorld(new Vector2Int(board.Width - 1, board.Height - 1));
            return direction switch
            {
                Direction.Up => maxCell.y + CellSize * 0.5f - origin.y,
                Direction.Down => origin.y - (minCell.y - CellSize * 0.5f),
                Direction.Right => maxCell.x + CellSize * 0.5f - origin.x,
                Direction.Left => origin.x - (minCell.x - CellSize * 0.5f),
                _ => 0f
            };
        }

        private bool IsPlayerInsideLaser(Vector3 center, Vector2 size)
        {
            if (playerView == null)
            {
                return false;
            }

            var playerPosition = playerView.transform.position;
            return Mathf.Abs(playerPosition.x - center.x) <= size.x * 0.5f + 0.31f &&
                Mathf.Abs(playerPosition.y - center.y) <= size.y * 0.5f + 0.31f;
        }

        private static Vector3 GetDirectionVector(Direction direction)
        {
            var offset = direction.ToOffset();
            return new Vector3(offset.x, offset.y, 0f);
        }

        private Dictionary<EntityInstance, Vector3> CaptureEntityViewPositions(BoardSnapshot snapshot)
        {
            var positions = new Dictionary<EntityInstance, Vector3>();

            for (var i = 0; i < board.Entities.Count && i < snapshot.Entities.Count; i++)
            {
                var entity = board.Entities[i];
                if (entityViews.ContainsKey(entity))
                {
                    positions[entity] = GridToWorld(snapshot.Entities[i].Position);
                }
            }

            return positions;
        }

        private float GetMaxGridMoveDistance(Dictionary<EntityInstance, Vector3> fromPositions)
        {
            var maxDistance = 0f;
            foreach (var pair in fromPositions)
            {
                var toPosition = GridToWorld(pair.Key.Position);
                maxDistance = Mathf.Max(maxDistance, Vector3.Distance(pair.Value, toPosition) / CellSize);
            }

            return maxDistance;
        }

        private void SyncEntityViews()
        {
            foreach (var pair in entityViews)
            {
                pair.Value.transform.position = GridToWorld(pair.Key.Position);
            }

            if (board.Player != null && entityViews.TryGetValue(board.Player, out var view))
            {
                playerView = view;
            }

            RedrawLaserViews();
        }

        private void CreateCellView(Vector2Int gridPosition, CellType cellType)
        {
            if (cellType == CellType.Empty)
            {
                return;
            }

            var color = cellType switch
            {
                CellType.Goal => new Color(0.96f, 0.74f, 0.25f),
                CellType.Ice => new Color(0.54f, 0.82f, 0.96f),
                CellType.Wall => new Color(0.16f, 0.17f, 0.2f),
                CellType.Lock => board.IsLockOpen(gridPosition) ? new Color(0.28f, 0.78f, 0.34f) : new Color(0.84f, 0.18f, 0.2f),
                CellType.LaserWall => new Color(0.16f, 0.17f, 0.2f),
                CellType.LevelEntrance => GetEntranceColor(gridPosition),
                _ => new Color(0.72f, 0.75f, 0.74f)
            };

            var view = CreateCellViewObject(cellType.ToString(), cellType, GridToWorld(gridPosition), new Vector2(0.94f, 0.94f), color, 0);
            if (cellType == CellType.LaserWall)
            {
                AddLaserEmitterDetails(view, board.CellLasers.TryGetValue(gridPosition, out var directions) ? directions : LaserDirections.None, 1);
            }

            if (cellType == CellType.Lock)
            {
                AddCellLabel(view, $"{GetCompletedLevelCount()}/{board.GetLockRequirement(gridPosition)}");
            }
            else if (cellType == CellType.LevelEntrance)
            {
                AddEntranceNameLabel(view, gridPosition);
            }
        }

        private Color GetEntranceColor(Vector2Int gridPosition)
        {
            var targetLevel = board.GetEntranceTarget(gridPosition);
            return targetLevel != null && IsLevelCompleted(targetLevel)
                ? new Color(0.34f, 0.82f, 0.52f)
                : new Color(0.46f, 0.33f, 0.88f);
        }

        private void CreateEdgeWallView(Vector2Int gridPosition, Direction direction)
        {
            var center = GridToWorld(gridPosition);
            var size = direction == Direction.Right
                ? new Vector2(0.12f, 1.05f)
                : new Vector2(1.05f, 0.12f);

            var offset = direction == Direction.Right
                ? new Vector3(CellSize * 0.5f, 0f, 0f)
                : new Vector3(0f, CellSize * 0.5f, 0f);

            CreateView(
                "Edge Wall",
                edgeWallPrefab,
                center + offset,
                size,
                new Color(0.92f, 0.28f, 0.32f),
                1);
        }

        private GameObject CreateEntityView(string objectName, EntityType entityType, Vector3 position, Vector2 size, Color color, int sortingOrder)
        {
            var prefab = entityType switch
            {
                EntityType.Player => playerPrefab,
                EntityType.Box => boxPrefab,
                _ => null
            };

            var created = CreateView(objectName, prefab, position, size, color, sortingOrder);
            return created;
        }

        private GameObject CreateLaserBoxView(Vector3 position, LaserDirections directions, int sortingOrder)
        {
            var root = laserBoxPrefab != null
                ? CreateView("Laser Box", laserBoxPrefab, position, new Vector2(0.72f, 0.72f), new Color(0.64f, 0.42f, 0.20f), sortingOrder)
                : CreateSpriteObject("Laser Box", position, new Vector2(0.72f, 0.72f), new Color(0.64f, 0.42f, 0.20f), sortingOrder);
            AddLaserEmitterDetails(root, directions, 6);
            return root;
        }

        private GameObject CreateCellViewObject(string objectName, CellType cellType, Vector3 position, Vector2 size, Color color, int sortingOrder)
        {
            var prefab = cellType switch
            {
                CellType.Ground => groundPrefab,
                CellType.Ice => icePrefab,
                CellType.Goal => goalPrefab,
                CellType.Wall => cellWallPrefab,
                CellType.LaserWall => laserWallPrefab != null ? laserWallPrefab : cellWallPrefab,
                CellType.LevelEntrance => levelEntrancePrefab,
                _ => null
            };

            var view = CreateView(objectName, prefab, position, size, color, sortingOrder);
            if (cellType == CellType.LaserWall && prefab == null)
            {
                // Details are attached from CreateCellView because the directions live in level data.
            }

            return view;
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
            triangle.hideFlags = HideFlags.HideInHierarchy;
            triangle.transform.SetParent(parent);
            triangle.transform.localPosition = GetDirectionVector(direction) * 0.39f + new Vector3(0f, 0f, -0.03f);
            var meshFilter = triangle.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateTriangleMesh(direction);
            var meshRenderer = triangle.AddComponent<MeshRenderer>();
            meshRenderer.material = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.material.color = Color.white;
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
            if (parent == null)
            {
                return;
            }

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(parent.transform);
            labelObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = GetCellLabelCharacterSize(text);
            textMesh.fontSize = 64;
            textMesh.color = Color.white;
            var renderer = labelObject.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 5;
        }

        private void AddEntranceNameLabel(GameObject parent, Vector2Int gridPosition)
        {
            var targetLevel = board.GetEntranceTarget(gridPosition);
            if (parent == null || targetLevel == null)
            {
                return;
            }

            var labelObject = new GameObject("Level Name");
            labelObject.transform.SetParent(parent.transform);
            labelObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = targetLevel.name;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = GetEntranceNameCharacterSize(targetLevel.name);
            textMesh.fontSize = 64;
            textMesh.color = new Color(1f, 1f, 1f, 0f);
            var renderer = labelObject.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 5;
            entranceNameLabels[gridPosition] = new EntranceNameLabel(textMesh);
        }

        private void UpdateEntranceNameLabels()
        {
            if (board?.Player == null || entranceNameLabels.Count == 0)
            {
                return;
            }

            foreach (var pair in entranceNameLabels)
            {
                var targetAlpha = IsAdjacentToPlayer(pair.Key) ? 1f : 0f;
                pair.Value.Alpha = Mathf.MoveTowards(pair.Value.Alpha, targetAlpha, entranceNameFadeSpeed * Time.deltaTime);
                var color = pair.Value.Text.color;
                color.a = pair.Value.Alpha;
                pair.Value.Text.color = color;
            }
        }

        private bool IsAdjacentToPlayer(Vector2Int position)
        {
            var delta = board.Player.Position - position;
            return Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1;
        }

        private bool IsInsideBoard(Vector2Int position)
        {
            return position.x >= 0 && position.x < board.Width && position.y >= 0 && position.y < board.Height;
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

        private GameObject CreateView(string objectName, GameObject prefab, Vector3 position, Vector2 fallbackSize, Color fallbackColor, int sortingOrder)
        {
            if (prefab != null)
            {
                var instance = Instantiate(prefab, transform);
                instance.name = objectName;
                instance.transform.position = position;
                return instance;
            }

            return CreateSpriteObject(objectName, position, fallbackSize, fallbackColor, sortingOrder);
        }

        private GameObject CreateSpriteObject(string objectName, Vector3 position, Vector2 size, Color color, int sortingOrder)
        {
            var view = new GameObject(objectName);
            view.transform.SetParent(transform);
            view.transform.position = position;
            view.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = view.AddComponent<SpriteRenderer>();
            renderer.sprite = SpriteFactory.WhiteSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;

            return view;
        }

        private Vector3 GridToWorld(Vector2Int gridPosition)
        {
            var origin = new Vector2(-(board.Width - 1) * 0.5f, -(board.Height - 1) * 0.5f);
            return new Vector3(origin.x + gridPosition.x * CellSize, origin.y + gridPosition.y * CellSize, 0f);
        }

        private void SetupCamera()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                mainCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }

            mainCamera.orthographic = true;
            mainCamera.orthographicSize = cameraOrthographicSize;
            mainCamera.backgroundColor = new Color(0.08f, 0.09f, 0.1f);
        }

        private void ConfigureCameraForLevel()
        {
            if (mainCamera == null || playerView == null)
            {
                return;
            }

            mainCamera.orthographic = true;
            mainCamera.orthographicSize = cameraOrthographicSize;

            var visibleHeight = mainCamera.orthographicSize * 2f;
            var visibleWidth = visibleHeight * mainCamera.aspect;
            var levelWidth = board.Width * CellSize + cameraPadding * 2f;
            var levelHeight = board.Height * CellSize + cameraPadding * 2f;
            cameraFollowsPlayer = levelWidth > visibleWidth || levelHeight > visibleHeight;

            var minCell = GridToWorld(Vector2Int.zero);
            var maxCell = GridToWorld(new Vector2Int(board.Width - 1, board.Height - 1));
            var center = (minCell + maxCell) * 0.5f;
            fixedCameraPosition = new Vector3(center.x, center.y, -10f);
            UpdateCameraPosition(true);
        }

        private void UpdateCameraPosition(bool instant)
        {
            if (mainCamera == null)
            {
                return;
            }

            var target = cameraFollowsPlayer && playerView != null
                ? GetFollowCameraPosition(playerView.transform.position)
                : fixedCameraPosition;

            mainCamera.transform.position = instant
                ? target
                : Vector3.Lerp(mainCamera.transform.position, target, 0.35f);
        }

        private Vector3 GetFollowCameraPosition(Vector3 playerPosition)
        {
            var visibleHeight = mainCamera.orthographicSize * 2f;
            var visibleWidth = visibleHeight * mainCamera.aspect;

            var minCell = GridToWorld(Vector2Int.zero);
            var maxCell = GridToWorld(new Vector2Int(board.Width - 1, board.Height - 1));
            var minX = minCell.x - CellSize * 0.5f - cameraPadding;
            var maxX = maxCell.x + CellSize * 0.5f + cameraPadding;
            var minY = minCell.y - CellSize * 0.5f - cameraPadding;
            var maxY = maxCell.y + CellSize * 0.5f + cameraPadding;

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            var targetX = maxX - minX <= visibleWidth
                ? centerX
                : Mathf.Clamp(playerPosition.x, minX + visibleWidth * 0.5f, maxX - visibleWidth * 0.5f);
            var targetY = maxY - minY <= visibleHeight
                ? centerY
                : Mathf.Clamp(playerPosition.y, minY + visibleHeight * 0.5f, maxY - visibleHeight * 0.5f);

            return new Vector3(targetX, targetY, -10f);
        }
    }

    public static class SpriteFactory
    {
        private static Sprite whiteSprite;

        public static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite != null)
                {
                    return whiteSprite;
                }

                var texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                texture.filterMode = FilterMode.Point;
                whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                return whiteSprite;
            }
        }
    }

    public sealed class ReturnContext
    {
        public readonly LevelAsset SourceLevel;
        public readonly Vector2Int ReturnPosition;
        public readonly Vector2Int EntrancePosition;
        public readonly LevelAsset EnteredLevel;
        public readonly BoardSnapshot SourceSnapshot;

        public ReturnContext(
            LevelAsset sourceLevel,
            Vector2Int returnPosition,
            Vector2Int entrancePosition,
            LevelAsset enteredLevel,
            BoardSnapshot sourceSnapshot)
        {
            SourceLevel = sourceLevel;
            ReturnPosition = returnPosition;
            EntrancePosition = entrancePosition;
            EnteredLevel = enteredLevel;
            SourceSnapshot = sourceSnapshot;
        }
    }

    public sealed class EntranceNameLabel
    {
        public readonly TextMesh Text;
        public float Alpha;

        public EntranceNameLabel(TextMesh text)
        {
            Text = text;
        }
    }

    public sealed class EntityMoveAnimation
    {
        public readonly EntityInstance Entity;
        public readonly Vector3 FromPosition;
        public readonly Vector3 ToPosition;
        public readonly float Duration;
        public readonly float GridDistance;
        public float Delay;
        public bool UseSmoothStep;

        public EntityMoveAnimation(
            EntityInstance entity,
            Vector3 fromPosition,
            Vector3 toPosition,
            float duration,
            bool useSmoothStep,
            float gridDistance)
        {
            Entity = entity;
            FromPosition = fromPosition;
            ToPosition = toPosition;
            Duration = duration;
            GridDistance = gridDistance;
            UseSmoothStep = useSmoothStep;
        }
    }

}
