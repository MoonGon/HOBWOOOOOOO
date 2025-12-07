using System;
using UnityEngine;

/// <summary>
/// Main character stat container that implements ICharacterStat and IHpProvider,
/// subscribes to PlayerLevel (if present) and raises events for UI.
/// Reworked to correctly implement the interface members required by the project.
/// </summary>
public class MCstat1 : MonoBehaviour, ICharacterStat, IHpProvider
{
    [Header("Player Base Stats")]
    public string playerName = "Main Knight";
    public int hp = 20;
    public int maxHp = 20;
    public int atk = 15;
    public int def = 6;
    public int speed = 10;

    [Header("Local level (kept in sync with PlayerLevel when available)")]
    [Tooltip("This value will be overwritten when PlayerLevel provider is present.")]
    public int level = 1;

    [Header("Level provider (optional)")]
    public PlayerLevel playerLevel; // optional reference (drag in inspector)

    // events for UI
    public event Action<int, int> OnHpChanged; // (current, max)
    public event Action<int> OnLevelChanged;   // (new level)

    // IHpProvider
    public int CurrentHp => hp;
    public int MaxHp => maxHp;

    // ICharacterStat implementation (names must match the interface)
    public string Name => playerName;
    public int hpStat => hp; // keep legacy accessor if other code uses hpStat; not required by interface
    public int maxHpStat => maxHp;
    public int atkStat => atk;
    public int defStat => def;
    public int speedStat => speed;
    public int levelStat => level;

    // Interface-required properties (explicit implementation to match project expectations)
    int ICharacterStat.hp { get => hp; }
    int ICharacterStat.maxHp { get => maxHp; }
    int ICharacterStat.atk { get => atk; }
    int ICharacterStat.def { get => def; }
    int ICharacterStat.speed { get => speed; }
    int ICharacterStat.level { get => level; }
    string ICharacterStat.Name { get => playerName; }

    void Start()
    {
        // clamp hp
        if (maxHp <= 0) maxHp = Mathf.Max(1, hp);
        hp = Mathf.Clamp(hp, 0, maxHp);

        // try subscribe to PlayerLevel
        EnsurePlayerLevelSubscription();

        // initial UI notification
        OnHpChanged?.Invoke(hp, maxHp);
        OnLevelChanged?.Invoke(level);
    }

    void EnsurePlayerLevelSubscription()
    {
        if (playerLevel == null)
        {
            playerLevel = GetComponent<PlayerLevel>() ?? FindObjectOfType<PlayerLevel>();
        }

        if (playerLevel != null)
        {
            // sync immediately
            SyncLevelFromProvider(playerLevel.Level);

            // subscribe
            playerLevel.OnLevelChanged += OnProviderLevelChanged;
        }
        else
        {
            Debug.LogWarning($"{name}: PlayerLevel provider not found. MCstat1 will use its local level value.");
        }
    }

    void OnDestroy()
    {
        if (playerLevel != null)
            playerLevel.OnLevelChanged -= OnProviderLevelChanged;
    }

    void OnProviderLevelChanged(int newLevel)
    {
        SyncLevelFromProvider(newLevel);
    }

    void SyncLevelFromProvider(int providerLevel)
    {
        level = Mathf.Max(1, providerLevel);
        // optionally recalc stats here if you have level-based scaling
        OnLevelChanged?.Invoke(level);
        Debug.Log($"[MCstat1] Synced level to {level}");
    }

    // public API: take damage / heal
    public void TakeDamage(int damage)
    {
        Debug.Log($"[MCstat1] TakeDamage on {gameObject.name} dmg={damage} beforeHp={hp}");
        int applied = Mathf.Max(1, damage - def);
        hp -= applied;
        hp = Mathf.Clamp(hp, 0, maxHp);
        Debug.Log($"[MCstat1] After applying, hp={hp} (applied={applied})");
        OnHpChanged?.Invoke(hp, maxHp);
        if (hp <= 0) Die();
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        hp = Mathf.Clamp(hp + amount, 0, maxHp);
        OnHpChanged?.Invoke(hp, maxHp);
    }

    // Die behaviour: show Game Over UI, disable player control, then remove the character
    void Die()
    {
        Debug.Log($"{Name} died - showing GameOver and cleaning up.");

        // 1) Show game over UI using BattleEndUIManager if available
        var bem = FindObjectOfType<BattleEndUIManager>();
        if (bem != null)
        {
            // adjust message as needed
            bem.ShowGameOver("You have been defeated!");
        }
        else
        {
            Debug.LogWarning("[MCstat1] BattleEndUIManager not found - cannot show GameOver UI.");
        }

        // 2) Disable player control components so no further input occurs
        var playerController = GetComponent<PlayerController>();
        if (playerController != null) playerController.enabled = false;

        var playerAI = GetComponent<GoAttck>();
        if (playerAI != null) playerAI.enabled = false;

        // 3) Optionally notify TurnBaseSystem (if you want it to handle removal)
        var tbs = FindObjectOfType<TurnBaseSystem>();
        if (tbs != null)
        {
            // Prefer TurnBaseSystem.RemoveBattler so it can handle rewards/turn order cleanup.
            try
            {
                tbs.RemoveBattler(gameObject, true);
            }
            catch
            {
                // fallback if RemoveBattler not present/throws
                Destroy(gameObject);
            }
        }
        else
        {
            // If no TurnBaseSystem, just destroy or deactivate the GameObject
            Destroy(gameObject);
        }
    }

    // Implement interface attack methods required by ICharacterStat
    public void AttackMonster(IMonsterStat monster)
    {
        if (monster == null) return;
        monster.TakeDamage(atk);
        Debug.Log($"{Name} attacked {monster} for {atk}");
    }

    public void StrongAttackMonster(IMonsterStat monster)
    {
        if (monster == null) return;
        int strongAtk = atk * 2;
        monster.TakeDamage(strongAtk);
        Debug.Log($"{Name} strong-attacked {monster} for {strongAtk}");
    }
}