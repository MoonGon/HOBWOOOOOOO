using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// SaveSystem for a single "run".
/// - Saves MCstat1-derived character stats + PlayerLevel available points.
/// - Also stores completedEncounters (list of encounter ids).
/// - Save file persists until EndRun() is called.
/// </summary>
public static class SaveSystem
{
    [Serializable]
    public class CharacterData
    {
        public string persistentId;
        public string name;
        public int hp;
        public int maxHp;
        public int atk;
        public int def;
        public int speed;
        public int level;
    }

    [Serializable]
    public class GameRunSave
    {
        public int version = 1;
        public long savedAtTicks;
        public List<CharacterData> characters = new List<CharacterData>();
        public int availableStatPoints = 0;
        public bool isCompleted = false;

        // NEW: completed encounter ids
        public List<string> completedEncounters = new List<string>();
    }

    static string SaveFilePath => Path.Combine(Application.persistentDataPath, "current_run_save.json");
    static string CompletedFilePath => Path.Combine(Application.persistentDataPath, "last_completed_run.json");

    // in-memory cache of completed encounters so other code can query without loading file
    static HashSet<string> _completedEncounters = new HashSet<string>(StringComparer.Ordinal);

    public static bool HasActiveRun() => File.Exists(SaveFilePath);

    public static void StartNewRun()
    {
        _completedEncounters.Clear();
        var s = new GameRunSave { savedAtTicks = DateTime.UtcNow.Ticks };
        s.completedEncounters = new List<string>(_completedEncounters);
        var json = JsonUtility.ToJson(s, true);
        File.WriteAllText(SaveFilePath, json);
        Debug.Log($"[SaveSystem] Started new run : {SaveFilePath}");
    }

    public static void SaveProgress()
    {
        try
        {
            var save = new GameRunSave { savedAtTicks = DateTime.UtcNow.Ticks };

            var all = UnityEngine.Object.FindObjectsOfType<MCstat1>();
            foreach (var mc in all)
            {
                string id = EnsurePersistentId(mc);

                var data = new CharacterData
                {
                    persistentId = id,
                    name = mc.gameObject.name,
                    hp = mc.hp,
                    maxHp = mc.maxHp,
                    atk = mc.atk,
                    def = mc.def,
                    speed = mc.speed,
                    level = mc.level
                };
                save.characters.Add(data);
            }

            var pl = UnityEngine.Object.FindObjectOfType<PlayerLevel>();
            if (pl != null)
            {
                save.availableStatPoints = pl.availableStatPoints;
            }

            // include completed encounters
            save.completedEncounters = new List<string>(_completedEncounters);

            var json = JsonUtility.ToJson(save, true);
            File.WriteAllText(SaveFilePath, json);
            Debug.Log($"[SaveSystem] Saved run state ({save.characters.Count} characters, {_completedEncounters.Count} completed encounters) to {SaveFilePath}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SaveSystem] SaveProgress failed: " + ex);
        }
    }

    public static GameRunSave LoadProgress()
    {
        if (!File.Exists(SaveFilePath))
        {
            Debug.Log("[SaveSystem] No active run save found.");
            return null;
        }

        try
        {
            string json = File.ReadAllText(SaveFilePath);
            var save = JsonUtility.FromJson<GameRunSave>(json);
            if (save == null) { Debug.LogWarning("[SaveSystem] Save file parse returned null."); return null; }

            // restore character stats
            foreach (var character in save.characters)
            {
                MCstat1 mc = FindCharacterByPersistentIdOrName(character.persistentId, character.name);
                if (mc != null)
                {
                    mc.hp = character.hp;
                    mc.maxHp = character.maxHp;
                    mc.atk = character.atk;
                    mc.def = character.def;
                    mc.speed = character.speed;
                    mc.level = character.level;
                    Debug.Log($"[SaveSystem] Restored {character.name} ({character.persistentId})");
                }
                else
                {
                    Debug.LogWarning($"[SaveSystem] Could not find character to restore: {character.name}/{character.persistentId}");
                }
            }

            var pl = UnityEngine.Object.FindObjectOfType<PlayerLevel>();
            if (pl != null)
            {
                pl.availableStatPoints = save.availableStatPoints;
            }

            // restore completed encounters into memory
            _completedEncounters.Clear();
            if (save.completedEncounters != null)
            {
                foreach (var e in save.completedEncounters)
                    _completedEncounters.Add(e);
            }

            Debug.Log("[SaveSystem] LoadProgress complete.");
            return save;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SaveSystem] LoadProgress failed: " + ex);
            return null;
        }
    }

    public static void EndRun(bool completed)
    {
        try
        {
            if (!File.Exists(SaveFilePath))
            {
                Debug.Log("[SaveSystem] EndRun: no active save to end.");
                return;
            }

            if (completed)
            {
                var json = File.ReadAllText(SaveFilePath);
                File.WriteAllText(CompletedFilePath, json);
                File.Delete(SaveFilePath);
                _completedEncounters.Clear();
                Debug.Log($"[SaveSystem] Run completed — archived to {CompletedFilePath}");
            }
            else
            {
                File.Delete(SaveFilePath);
                _completedEncounters.Clear();
                Debug.Log("[SaveSystem] Run ended (player death) — active save deleted.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SaveSystem] EndRun failed: " + ex);
        }
    }

    // --------- Completed encounters API ----------
    public static IReadOnlyCollection<string> GetCompletedEncounters() => _completedEncounters;
    public static bool IsEncounterCompleted(string id) => !string.IsNullOrEmpty(id) && _completedEncounters.Contains(id);

    public static void AddCompletedEncounter(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_completedEncounters.Add(id))
        {
            Debug.Log($"[SaveSystem] Marked encounter completed: {id}");
            // persist immediately
            SaveProgress();
        }
    }

    // --- Helpers (same as before) ---
    static MCstat1 FindCharacterByPersistentIdOrName(string persistentId, string name)
    {
        if (!string.IsNullOrEmpty(persistentId))
        {
            var all = UnityEngine.Object.FindObjectsOfType<MCstat1>();
            foreach (var mc in all)
            {
                string id = GetPersistentId(mc);
                if (!string.IsNullOrEmpty(id) && id == persistentId) return mc;
            }
        }

        var byName = UnityEngine.Object.FindObjectsOfType<MCstat1>();
        foreach (var mc in byName)
        {
            if (mc.gameObject.name == name) return mc;
        }

        return null;
    }

    static string EnsurePersistentId(MCstat1 mc)
    {
        if (mc == null) return null;
        var t = mc.GetType();
        var prop = t.GetProperty("persistentId", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.PropertyType == typeof(string))
        {
            try
            {
                var val = (string)prop.GetValue(mc);
                if (string.IsNullOrEmpty(val) && prop.CanWrite)
                {
                    var newId = Guid.NewGuid().ToString();
                    prop.SetValue(mc, newId);
                    return newId;
                }
                return val;
            }
            catch { }
        }

        var field = t.GetField("persistentId", BindingFlags.Public | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(string))
        {
            try
            {
                var val = (string)field.GetValue(mc);
                if (string.IsNullOrEmpty(val))
                {
                    var newId = Guid.NewGuid().ToString();
                    field.SetValue(mc, newId);
                    return newId;
                }
                return val;
            }
            catch { }
        }

        return mc.gameObject.name;
    }

    static string GetPersistentId(MCstat1 mc)
    {
        if (mc == null) return null;
        var t = mc.GetType();
        var prop = t.GetProperty("persistentId", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.PropertyType == typeof(string))
        {
            try { return (string)prop.GetValue(mc); } catch { }
        }
        var field = t.GetField("persistentId", BindingFlags.Public | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(string))
        {
            try { return (string)field.GetValue(mc); } catch { }
        }
        return null;
    }
}