using BetweenTiles.Core;
using BetweenTiles.Runtime;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BetweenTiles.Editor
{
    public static class LevelAssetOpenHandler
    {
        [OnOpenAsset]
        public static bool OpenLevelInScene(int instanceId, int line)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            var levelAsset = EditorUtility.InstanceIDToObject(instanceId) as LevelAsset;
            if (levelAsset == null)
            {
                return false;
            }

            var controller = Object.FindFirstObjectByType<LevelEditorController>();
            if (controller == null)
            {
                Debug.LogWarning("场景里没有找到 LevelEditorController，无法自动加载关卡。");
                return true;
            }

            SaveCurrentLevelIfNeeded(controller);

            Undo.RecordObject(controller, "Open Level");
            controller.CurrentLevel = levelAsset;
            controller.LoadFromCurrentLevel();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);

            Selection.activeGameObject = controller.gameObject;
            SceneView.RepaintAll();
            Debug.Log($"已加载关卡到绘制器：{levelAsset.name}");
            return true;
        }

        private static void SaveCurrentLevelIfNeeded(LevelEditorController controller)
        {
            if (!controller.AutoSave || controller.CurrentLevel == null)
            {
                return;
            }

            Undo.RecordObject(controller.CurrentLevel, "Auto Save Level");
            if (!controller.SaveToCurrentLevel(out _))
            {
                return;
            }

            EditorUtility.SetDirty(controller.CurrentLevel);
            AssetDatabase.SaveAssets();
        }
    }
}
