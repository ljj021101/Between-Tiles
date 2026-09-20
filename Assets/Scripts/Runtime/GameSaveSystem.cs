using BetweenTiles.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetweenTiles.Runtime
{
    public static class GameSaveSystem
    {
        private const string SaveKey = "BetweenTiles.Save.V1";

        public static GameSaveData Load()
        {
            var json = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new GameSaveData();
            }

            try
            {
                var data = JsonUtility.FromJson<GameSaveData>(json) ?? new GameSaveData();
                data.Normalize();
                return data;
            }
            catch
            {
                return new GameSaveData();
            }
        }

        public static void Save(GameSaveData data)
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }

        public static string GetLevelKey(LevelAsset levelAsset)
        {
            return levelAsset != null ? levelAsset.SaveKey : string.Empty;
        }
    }

    [Serializable]
    public sealed class GameSaveData
    {
        public List<string> CompletedLevels = new();
        public List<SavedLevelLocks> ClearedLocks = new();

        public bool IsLevelCompleted(LevelAsset levelAsset)
        {
            var key = GameSaveSystem.GetLevelKey(levelAsset);
            return !string.IsNullOrEmpty(key) && CompletedLevels.Contains(key);
        }

        public bool MarkLevelCompleted(LevelAsset levelAsset)
        {
            var key = GameSaveSystem.GetLevelKey(levelAsset);
            if (string.IsNullOrEmpty(key) || CompletedLevels.Contains(key))
            {
                return false;
            }

            CompletedLevels.Add(key);
            return true;
        }

        public int CompletedLevelCount => CompletedLevels.Count;

        public void Normalize()
        {
            CompletedLevels ??= new List<string>();
            ClearedLocks ??= new List<SavedLevelLocks>();

            ClearedLocks.RemoveAll(record => record == null);
            foreach (var record in ClearedLocks)
            {
                record.Positions ??= new List<Vector2Int>();
            }
        }

        public IReadOnlyList<Vector2Int> GetClearedLocks(LevelAsset levelAsset)
        {
            var record = FindLockRecord(GameSaveSystem.GetLevelKey(levelAsset));
            return record != null ? record.Positions : Array.Empty<Vector2Int>();
        }

        public bool MarkLockCleared(LevelAsset levelAsset, Vector2Int position)
        {
            var key = GameSaveSystem.GetLevelKey(levelAsset);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            var record = FindLockRecord(key);
            if (record == null)
            {
                record = new SavedLevelLocks { LevelKey = key };
                ClearedLocks.Add(record);
            }

            if (record.Positions.Contains(position))
            {
                return false;
            }

            record.Positions.Add(position);
            return true;
        }

        private SavedLevelLocks FindLockRecord(string levelKey)
        {
            foreach (var record in ClearedLocks)
            {
                if (record.LevelKey == levelKey)
                {
                    return record;
                }
            }

            return null;
        }
    }

    [Serializable]
    public sealed class SavedLevelLocks
    {
        public string LevelKey;
        public List<Vector2Int> Positions = new();
    }
}
