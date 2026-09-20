using BetweenTiles.Core;
using BetweenTiles.Runtime;
using UnityEditor;
using UnityEngine;

namespace BetweenTiles.Editor
{
    [CustomEditor(typeof(LevelEditorController))]
    public sealed class LevelEditorControllerEditor : UnityEditor.Editor
    {
        private static readonly Rect ToolbarRect = new(12, 12, 360, 500);
        private LevelEditorController controller;
        private int sceneControlId;
        private bool restoreSelectionAfterPaint;
        private bool pendingAutoSave;
        private double nextAutoSaveTime;
        private bool hasSelection;
        private bool isSelecting;
        private Vector2Int selectionStart;
        private Vector2Int selectionEnd;

        private void OnEnable()
        {
            controller = (LevelEditorController)target;
            controller.RebuildPreview();
            SceneView.duringSceneGui += DuringSceneGui;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("从当前关卡读取"))
            {
                Undo.RecordObject(controller, "Load Level");
                controller.LoadFromCurrentLevel();
                EditorUtility.SetDirty(controller);
            }

            if (GUILayout.Button("保存到当前关卡"))
            {
                SaveCurrentLevel(false);
            }

            if (GUILayout.Button("另存为新关卡"))
            {
                SaveAsNewLevel();
            }

            if (GUILayout.Button("验证是否有解"))
            {
                ValidateSolvable();
            }

            if (GUILayout.Button("清除存档"))
            {
                SaveDataEditorTools.ClearSaveData();
            }

            EditorGUILayout.HelpBox("Scene 视图：左键点击/拖拽绘制，右键点击/拖拽擦除当前画笔对应的层。", MessageType.Info);
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            if (controller == null || !IsControllerSelectionActive())
            {
                return;
            }

            var currentEvent = Event.current;
            if (currentEvent.alt)
            {
                return;
            }

            Handles.BeginGUI();
            DrawSceneToolbar();
            Handles.EndGUI();

            if (!controller.PaintingEnabled)
            {
                return;
            }

            sceneControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (ToolbarRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (controller.SelectionMode)
            {
                HandleSelectionMode(currentEvent);
                return;
            }

            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(sceneControlId);
                return;
            }

            if (currentEvent.type == EventType.MouseUp && GUIUtility.hotControl == sceneControlId)
            {
                GUIUtility.hotControl = 0;
                ScheduleAutoSave();
                RestoreSelectionSoon();
                currentEvent.Use();
                return;
            }

            if ((currentEvent.type != EventType.MouseDown && currentEvent.type != EventType.MouseDrag) ||
                (currentEvent.button != 0 && currentEvent.button != 1))
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown)
            {
                GUIUtility.hotControl = sceneControlId;
                Selection.activeGameObject = controller.gameObject;
                restoreSelectionAfterPaint = true;
            }

            var ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(ray, out var distance))
            {
                return;
            }

            Undo.RecordObject(controller, currentEvent.button == 0 ? "Paint Level" : "Erase Level");
            controller.PaintAtWorld(ray.GetPoint(distance), currentEvent.button == 1);
            EditorUtility.SetDirty(controller);
            Selection.activeGameObject = controller.gameObject;
            currentEvent.Use();
        }

        private void DrawSceneToolbar()
        {
            GUILayout.BeginArea(ToolbarRect, EditorStyles.helpBox);
            EditorGUILayout.LabelField("关卡绘制器", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var paintingEnabled = EditorGUILayout.Toggle("启用绘制", controller.PaintingEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Toggle Painting");
                controller.PaintingEnabled = paintingEnabled;
                EditorUtility.SetDirty(controller);
            }

            EditorGUI.BeginChangeCheck();
            var selectionMode = EditorGUILayout.Toggle("框选模式", controller.SelectionMode);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Toggle Selection Mode");
                controller.SelectionMode = selectionMode;
                if (!selectionMode)
                {
                    hasSelection = false;
                    isSelecting = false;
                }
                EditorUtility.SetDirty(controller);
                SceneView.RepaintAll();
            }

            EditorGUI.BeginChangeCheck();
            var currentLevel = (LevelAsset)EditorGUILayout.ObjectField("当前关卡", controller.CurrentLevel, typeof(LevelAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Change Level");
                controller.CurrentLevel = currentLevel;
                controller.LoadFromCurrentLevel();
                EditorUtility.SetDirty(controller);
            }

            if (GUILayout.Button("清除存档"))
            {
                SaveDataEditorTools.ClearSaveData();
            }

            EditorGUILayout.Space(4);
            DrawBrushButtonRow(
                ("终点", LevelBrushKind.Goal),
                ("冰面", LevelBrushKind.Ice),
                ("网格墙", LevelBrushKind.CellWall));

            DrawBrushButtonRow(
                ("关卡入口", LevelBrushKind.LevelEntrance),
                ("玩家", LevelBrushKind.Player),
                ("箱子", LevelBrushKind.Box));

            DrawBrushButtonRow(
                ("激光墙", LevelBrushKind.LaserWall),
                ("激光箱", LevelBrushKind.LaserBox));

            DrawBrushButtonRow(
                ("边墙", LevelBrushKind.EdgeWall),
                ("锁格子", LevelBrushKind.Lock),
                ("擦除", LevelBrushKind.Erase),
                ("擦边界", LevelBrushKind.EraseEdge));

            if (controller.Brush == LevelBrushKind.LevelEntrance)
            {
                EditorGUI.BeginChangeCheck();
                var entranceTarget = (LevelAsset)EditorGUILayout.ObjectField("入口目标", controller.EntranceTargetLevel, typeof(LevelAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(controller, "Change Entrance Target");
                    controller.EntranceTargetLevel = entranceTarget;
                    EditorUtility.SetDirty(controller);
                }
            }

            if (controller.Brush == LevelBrushKind.Lock)
            {
                EditorGUI.BeginChangeCheck();
                var requiredCompletions = EditorGUILayout.IntField("需要通关数", controller.LockRequiredCompletions);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(controller, "Change Lock Requirement");
                    controller.LockRequiredCompletions = Mathf.Max(0, requiredCompletions);
                    EditorUtility.SetDirty(controller);
                }
            }

            if (controller.Brush == LevelBrushKind.LaserWall || controller.Brush == LevelBrushKind.LaserBox)
            {
                DrawLaserDirectionControls();
            }

            EditorGUI.BeginChangeCheck();
            var autoSave = EditorGUILayout.Toggle("自动保存", controller.AutoSave);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Toggle Auto Save");
                controller.AutoSave = autoSave;
                EditorUtility.SetDirty(controller);
            }

            EditorGUI.BeginChangeCheck();
            var solverMaxStates = EditorGUILayout.IntField("验证状态上限", controller.SolverMaxVisitedStates);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Change Solver Limit");
                controller.SolverMaxVisitedStates = Mathf.Max(1000, solverMaxStates);
                EditorUtility.SetDirty(controller);
            }

            EditorGUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("读取"))
            {
                Undo.RecordObject(controller, "Load Level");
                controller.LoadFromCurrentLevel();
                EditorUtility.SetDirty(controller);
            }

            if (GUILayout.Button("保存"))
            {
                SaveCurrentLevel(false);
            }

            if (GUILayout.Button("另存"))
            {
                SaveAsNewLevel();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("验证是否有解"))
            {
                ValidateSolvable();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(controller.SelectionMode ? "左键拖拽框选，方向键平移" : "左键绘制/拖拽，右键擦除");
            GUILayout.EndArea();
        }

        private void HandleSelectionMode(Event currentEvent)
        {
            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(sceneControlId);
                return;
            }

            DrawSelectionRect();

            if (currentEvent.type == EventType.KeyDown && TryGetSelectionMoveOffset(currentEvent.keyCode, out var offset))
            {
                if (hasSelection)
                {
                    GetSelectionBounds(out var min, out var max);
                    Undo.RecordObject(controller, "Move Selection");
                    controller.TranslateSelection(min, max, offset);
                    selectionStart += offset;
                    selectionEnd += offset;
                    EditorUtility.SetDirty(controller);
                    ScheduleAutoSave();
                    SceneView.RepaintAll();
                }

                currentEvent.Use();
                return;
            }

            if (currentEvent.button != 0)
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown)
            {
                if (!TryGetMouseCell(currentEvent, out var cell))
                {
                    return;
                }

                GUIUtility.hotControl = sceneControlId;
                Selection.activeGameObject = controller.gameObject;
                selectionStart = cell;
                selectionEnd = cell;
                hasSelection = true;
                isSelecting = true;
                currentEvent.Use();
                SceneView.RepaintAll();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && isSelecting)
            {
                if (TryGetMouseCell(currentEvent, out var cell))
                {
                    selectionEnd = cell;
                    currentEvent.Use();
                    SceneView.RepaintAll();
                }
                return;
            }

            if (currentEvent.type == EventType.MouseUp && GUIUtility.hotControl == sceneControlId)
            {
                GUIUtility.hotControl = 0;
                isSelecting = false;
                RestoreSelectionSoon();
                currentEvent.Use();
                SceneView.RepaintAll();
            }
        }

        private void DrawSelectionRect()
        {
            if (!hasSelection)
            {
                return;
            }

            GetSelectionBounds(out var min, out var max);
            var minWorld = new Vector3(min.x - 0.5f, min.y - 0.5f, 0f);
            var maxWorld = new Vector3(max.x + 0.5f, max.y + 0.5f, 0f);
            var corners = new[]
            {
                new Vector3(minWorld.x, minWorld.y, 0f),
                new Vector3(maxWorld.x, minWorld.y, 0f),
                new Vector3(maxWorld.x, maxWorld.y, 0f),
                new Vector3(minWorld.x, maxWorld.y, 0f)
            };

            Handles.DrawSolidRectangleWithOutline(corners, new Color(0.28f, 0.55f, 1f, 0.12f), new Color(0.28f, 0.55f, 1f, 0.95f));
        }

        private void GetSelectionBounds(out Vector2Int min, out Vector2Int max)
        {
            min = new Vector2Int(Mathf.Min(selectionStart.x, selectionEnd.x), Mathf.Min(selectionStart.y, selectionEnd.y));
            max = new Vector2Int(Mathf.Max(selectionStart.x, selectionEnd.x), Mathf.Max(selectionStart.y, selectionEnd.y));
        }

        private bool TryGetMouseCell(Event currentEvent, out Vector2Int cell)
        {
            var ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (plane.Raycast(ray, out var distance))
            {
                cell = controller.CellFromWorld(ray.GetPoint(distance));
                return true;
            }

            cell = Vector2Int.zero;
            return false;
        }

        private static bool TryGetSelectionMoveOffset(KeyCode keyCode, out Vector2Int offset)
        {
            offset = keyCode switch
            {
                KeyCode.UpArrow => Vector2Int.up,
                KeyCode.DownArrow => Vector2Int.down,
                KeyCode.LeftArrow => Vector2Int.left,
                KeyCode.RightArrow => Vector2Int.right,
                _ => Vector2Int.zero
            };

            return offset != Vector2Int.zero;
        }

        private void DrawBrushButtonRow(params (string Label, LevelBrushKind Brush)[] buttons)
        {
            GUILayout.BeginHorizontal();
            foreach (var button in buttons)
            {
                var selected = controller.Brush == button.Brush;
                var previousColor = GUI.backgroundColor;
                GUI.backgroundColor = selected ? new Color(0.52f, 0.72f, 1f) : previousColor;

                if (GUILayout.Button(button.Label, GUILayout.Height(28)))
                {
                    Undo.RecordObject(controller, "Change Brush");
                    controller.Brush = button.Brush;
                    EditorUtility.SetDirty(controller);
                }

                GUI.backgroundColor = previousColor;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLaserDirectionControls()
        {
            EditorGUI.BeginChangeCheck();
            var directions = controller.LaserDirections;
            const float buttonSize = 30f;
            const float gap = 8f;
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fixedWidth = buttonSize,
                fixedHeight = buttonSize
            };

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var up = DrawDirectionToggle(directions.HasFlag(LaserDirections.Up), "▲", buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var left = DrawDirectionToggle(directions.HasFlag(LaserDirections.Left), "◀", buttonStyle);
            GUILayout.Space(gap);
            var right = DrawDirectionToggle(directions.HasFlag(LaserDirections.Right), "▶", buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var down = DrawDirectionToggle(directions.HasFlag(LaserDirections.Down), "▼", buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            var newDirections = LaserDirections.None;
            if (up)
            {
                newDirections |= LaserDirections.Up;
            }

            if (down)
            {
                newDirections |= LaserDirections.Down;
            }

            if (left)
            {
                newDirections |= LaserDirections.Left;
            }

            if (right)
            {
                newDirections |= LaserDirections.Right;
            }

            Undo.RecordObject(controller, "Change Laser Directions");
            controller.LaserDirections = newDirections;
            EditorUtility.SetDirty(controller);
        }

        private static bool DrawDirectionToggle(bool selected, string label, GUIStyle style)
        {
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = selected ? new Color(1f, 0.34f, 0.34f) : previousColor;
            var result = GUILayout.Toggle(selected, label, style);
            GUI.backgroundColor = previousColor;
            return result;
        }

        private void SaveCurrentLevel(bool silent)
        {
            if (controller.CurrentLevel != null)
            {
                Undo.RecordObject(controller.CurrentLevel, "Save Level");
            }

            if (controller.SaveToCurrentLevel(out var message))
            {
                EditorUtility.SetDirty(controller.CurrentLevel);
                AssetDatabase.SaveAssets();
                if (!silent)
                {
                    Debug.Log(message);
                }
            }
            else
            {
                if (!silent)
                {
                    Debug.LogWarning(message);
                }
            }
        }

        private void ScheduleAutoSave()
        {
            if (!controller.AutoSave || controller.CurrentLevel == null)
            {
                return;
            }

            pendingAutoSave = true;
            nextAutoSaveTime = EditorApplication.timeSinceStartup + controller.AutoSaveDelay;
        }

        private void OnEditorUpdate()
        {
            if (!pendingAutoSave || controller == null || EditorApplication.timeSinceStartup < nextAutoSaveTime)
            {
                return;
            }

            pendingAutoSave = false;
            SaveCurrentLevel(true);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || controller == null)
            {
                return;
            }

            if (pendingAutoSave)
            {
                pendingAutoSave = false;
                SaveCurrentLevel(true);
            }

            controller.ClearPreview();
        }

        private void SaveAsNewLevel()
        {
            if (!controller.TryGetContentBounds(out var min, out var max))
            {
                Debug.LogWarning("Nothing to save.");
                return;
            }

            var path = EditorUtility.SaveFilePanelInProject(
                "Save Level",
                "New Level",
                "asset",
                "Choose where to save this level asset.",
                "Assets/Levels");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var levelAsset = ScriptableObject.CreateInstance<LevelAsset>();
            AssetDatabase.CreateAsset(levelAsset, path);
            controller.CurrentLevel = levelAsset;
            controller.SaveToLevel(levelAsset, min, max);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(levelAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"Saved new level: {path}");
        }

        private void ValidateSolvable()
        {
            if (!controller.TryBuildEditedLevelData(out var level, out var message))
            {
                Debug.LogWarning(message);
                return;
            }

            var result = LevelSolver.Solve(level, controller.SolverMaxVisitedStates);
            if (result.IsSolvable)
            {
                Debug.Log($"[关卡验证] {result.Message} 搜索状态数：{result.VisitedStates}");
            }
            else
            {
                Debug.LogWarning($"[关卡验证] {result.Message} 搜索状态数：{result.VisitedStates}");
            }
        }

        private bool IsControllerSelectionActive()
        {
            if (Selection.activeGameObject == controller.gameObject)
            {
                return true;
            }

            return Selection.activeGameObject != null &&
                Selection.activeGameObject.transform.IsChildOf(controller.transform);
        }

        private void RestoreSelectionSoon()
        {
            if (!restoreSelectionAfterPaint || controller == null || !controller.PaintingEnabled)
            {
                restoreSelectionAfterPaint = false;
                return;
            }

            restoreSelectionAfterPaint = false;
            EditorApplication.delayCall += () =>
            {
                if (controller != null && controller.PaintingEnabled)
                {
                    Selection.activeGameObject = controller.gameObject;
                }
            };
        }
    }
}
