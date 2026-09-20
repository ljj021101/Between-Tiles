using BetweenTiles.Runtime;
using UnityEditor;
using UnityEngine;

namespace BetweenTiles.Editor
{
    public static class SaveDataEditorTools
    {
        [MenuItem("Between Tiles/Clear Save Data")]
        public static void ClearSaveData()
        {
            if (!EditorUtility.DisplayDialog("清除存档", "确定要清除已通关关卡和已清除锁格记录吗？", "清除", "取消"))
            {
                return;
            }

            GameSaveSystem.Clear();
            Debug.Log("Between Tiles 存档已清除。");
        }
    }
}
