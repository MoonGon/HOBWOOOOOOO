using System;
using UnityEngine;

/// <summary>
/// PlayerLevel: source of truth for player level & exp.
/// - Exposes Level property and OnLevelChanged event.
/// - Call AddExp(exp) to grant EXP; it will calc level-ups and raise events.
/// </summary>
public class PlayerLevel : MonoBehaviour
{
    [Header("Leveling Settings")]
    [SerializeField] int level = 1;
    public int Level
    {
        get => level;
        set
        {
            int newVal = Mathf.Max(1, value);
            if (newVal == level) return;
            level = newVal;
            OnLevelChanged?.Invoke(level);
            Debug.Log($"[PlayerLevel] Level changed -> {level}");
        }
    }

    public int maxLevel = 20;
    public int currentExp = 0;
    public int baseExpToNext = 100;
    [Tooltip("Multiplier applied to next-level requirement each level")]
    public float expGrowth = 1.15f;
    public int pointsPerLevel = 1;

    [Header("Auto allocation settings")]
    public bool autoAllocate = false;
    // optional priorities etc - keep generic
    public int availableStatPoints = 0;

    // Event raised when level changes
    public event Action<int> OnLevelChanged;

    // Compute required exp for next level (simple formula)
    public int ExpToNext
    {
        get
        {
            // geometric growth
            float val = baseExpToNext * Mathf.Pow(expGrowth, level - 1);
            return Mathf.Max(1, Mathf.RoundToInt(val));
        }
    }

    // Give EXP; perform level ups if threshold reached
    public void AddExp(int exp)
    {
        if (exp <= 0) return;
        currentExp += exp;

        // loop in case exp big
        while (currentExp >= ExpToNext && Level < maxLevel)
        {
            currentExp -= ExpToNext;
            LevelUp();
        }
    }

    // Level up (increments Level property -> raises event)
    public void LevelUp()
    {
        if (Level >= maxLevel) { currentExp = Mathf.Min(currentExp, ExpToNext - 1); return; }
        Level = Level + 1;
        availableStatPoints += pointsPerLevel;
        // optional: auto allocate if flagged
        if (autoAllocate)
        {
            // example: do nothing here (project-specific)
        }
        Debug.Log($"[PlayerLevel] Leveled up to {Level}. Available stat points: {availableStatPoints}");
    }

    void OnValidate()
    {
        if (level < 1) level = 1;
        if (maxLevel < 1) maxLevel = 1;
        baseExpToNext = Mathf.Max(1, baseExpToNext);
        expGrowth = Mathf.Max(1f, expGrowth);
    }
}