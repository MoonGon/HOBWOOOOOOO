using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Hunterstat with optional level-scaling that follows the Main Character level.
/// - Assign mainCharacter explicitly if you want deterministic binding.
/// - Otherwise the script will try to find a main character heuristically.
/// - Scaling is linear: newStat = baseStat + perLevel * (targetLevel - 1)
/// </summary>
public class Hunterstat : MonoBehaviour, ICharacterStat, IHpProvider
{
    [Header("Hunter Stats (base)")]
    public string hunterName = "Hunter";

    [Tooltip("Current HP (will be clamped to maxHp on Start)")]
    public int hunterhp = 30;

    [Tooltip("Maximum HP (capacity). Keep this > 0.")]
    public int hunterMaxHp = 30;

    public int hunteratk = 10;
    public int hunterdef = 5;
    public int hunterspeed = 8;
    public int hunterlevel = 1;

    // store base values so scaling uses them as level-1 reference
    [Header("Scaling base values (auto-captured from the inspector values above)")]
    [SerializeField] int baseHp = 30;
    [SerializeField] int baseAtk = 10;
    [SerializeField] int baseDef = 5;
    [SerializeField] int baseSpeed = 8;
    [SerializeField] int baseLevel = 1;

    [Header("Level scaling settings")]
    [Tooltip("If true, this hunter will use the Main Character level to set its own level")]
    public bool followMainLevel = true;
    [Tooltip("If assigned, the mainCharacter GameObject will be used to read level; otherwise we try to find heuristically.")]
    public GameObject mainCharacter;
    [Tooltip("Add this offset to the main character level to compute hunter level: hunterLevel = mainLevel + levelOffset")]
    public int levelOffset = 0;

    [Tooltip("Linear increase per (main) level: maxHp = baseHp + hpPerLevel * (level-1)")]
    public int hpPerLevel = 5;
    [Tooltip("Linear increase per level for attack")]
    public int atkPerLevel = 2;
    [Tooltip("Linear increase per level for defense")]
    public int defPerLevel = 1;
    [Tooltip("Linear increase per level for speed")]
    public int speedPerLevel = 0;

    [Tooltip("Keep current HP as percentage when max HP changes due to scaling")]
    public bool keepHpPercentOnScale = true;

    [Tooltip("If true, update scaling continuously (polling). If false, scale once at Start.")]
    public bool scaleLive = false;
    [Tooltip("Polling interval (seconds) when scaleLive=true")]
    public float scalePollInterval = 0.5f;

    // Optional event for UI subscription (if you use event-driven health UI)
    public event Action<int, int> OnHpChanged; // (current, max)

    // IHpProvider properties (for HealthBarManager)
    public int CurrentHp => hunterhp;
    public int MaxHp => hunterMaxHp;

    // ICharacterStat implementation (include maxHp)
    public string Name => hunterName;
    public int hp => hunterhp;
    public int maxHp => hunterMaxHp;
    public int atk => hunteratk;
    public int def => hunterdef;
    public int speed => hunterspeed;
    public int level => hunterlevel;

    [Header("HealthBar (screen-space via HealthBarManager)")]
    [Tooltip("If true, this hunter will request a healthbar from HealthBarManager at Start.")]
    public bool createManagerUI = true;
    [Tooltip("Optional transform to follow (e.g. head). If null, uses this.transform.")]
    public Transform headTransform;
    [Tooltip("World offset applied to the follower when creating manager UI.")]
    public Vector3 hpUiOffset = new Vector3(0f, 1.6f, 0f);

    // internal tracking for live scaling
    int _lastObservedMainLevel = -1;
    float _lastScaleTime = 0f;

    void Reset()
    {
        // initialize base values from current inspector values
        baseHp = hunterMaxHp > 0 ? hunterMaxHp : hunterhp;
        baseAtk = hunteratk;
        baseDef = hunterdef;
        baseSpeed = hunterspeed;
        baseLevel = hunterlevel;
    }

    void Awake()
    {
        // capture base if not set (useful when script added at runtime)
        if (baseHp <= 0) baseHp = Mathf.Max(1, hunterMaxHp);
        if (baseAtk <= 0) baseAtk = Mathf.Max(1, hunteratk);
        if (baseDef < 0) baseDef = Mathf.Max(0, hunterdef);
        if (baseSpeed < 0) baseSpeed = Mathf.Max(0, hunterspeed);
        if (baseLevel <= 0) baseLevel = Mathf.Max(1, hunterlevel);
    }

    void Start()
    {
        // Ensure maxHp is valid and clamp current hp to max
        if (hunterMaxHp <= 0) hunterMaxHp = Mathf.Max(1, hunterhp);
        hunterhp = Mathf.Clamp(hunterhp, 0, hunterMaxHp);

        // initial scaling
        if (followMainLevel)
        {
            UpdateLevelFromMain(/*force=*/true);
        }

        // Optionally register healthbar with manager (safe-wait to avoid ordering/race)
        if (createManagerUI)
            StartCoroutine(RegisterHealthBarSafe());

        Debug.Log($"[{name}] Level: {hunterlevel} HP: {hunterhp}/{hunterMaxHp}, ATK: {hunteratk}, DEF: {hunterdef}, SPD: {hunterspeed}");

        if (scaleLive)
        {
            // start a lightweight poll coroutine
            StartCoroutine(LiveScalePoll());
        }
    }

    IEnumerator LiveScalePoll()
    {
        while (scaleLive)
        {
            yield return new WaitForSeconds(scalePollInterval);
            UpdateLevelFromMain();
        }
    }

    IEnumerator RegisterHealthBarSafe()
    {
        float t = 0f;
        float timeout = 2f;
        while (HealthBarManager.Instance == null && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (HealthBarManager.Instance == null)
        {
            Debug.LogWarning($"{name}: HealthBarManager not found. Manager UI not created for Hunter.");
            yield break;
        }

        Transform target = headTransform != null ? headTransform : transform;
        var hb = HealthBarManager.Instance.CreateFor(gameObject, target);
        if (hb != null)
        {
            var follower = hb.GetComponent<HealthBarFollower>();
            if (follower != null)
            {
                follower.worldOffset = hpUiOffset;
                if (follower.uiCamera == null && Camera.main != null) follower.uiCamera = Camera.main;
            }
            Debug.Log($"{name}: Registered manager healthbar.");
        }
    }

    // public helper to force re-evaluate scaling from main
    public void UpdateLevelFromMain(bool force = false)
    {
        var mainStat = FindMainCharacterStat();
        if (mainStat == null)
        {
            if (force)
                Debug.LogWarning($"[{name}] Main character stat not found for level-scaling.");
            return;
        }

        int mainLevel = Mathf.Max(1, mainStat.level);
        int targetLevel = Mathf.Max(1, mainLevel + levelOffset);

        if (!force && _lastObservedMainLevel == targetLevel) return; // nothing changed

        _lastObservedMainLevel = targetLevel;

        // preserve current hp percentage if requested
        float curPct = keepHpPercentOnScale ? (hunterMaxHp > 0 ? (float)hunterhp / hunterMaxHp : 1f) : -1f;

        // compute scaled stats (linear)
        int newMaxHp = Mathf.Max(1, baseHp + hpPerLevel * (targetLevel - 1));
        int newAtk = Mathf.Max(0, baseAtk + atkPerLevel * (targetLevel - 1));
        int newDef = Mathf.Max(0, baseDef + defPerLevel * (targetLevel - 1));
        int newSpd = Mathf.Max(0, baseSpeed + speedPerLevel * (targetLevel - 1));

        hunterlevel = targetLevel;
        hunterMaxHp = newMaxHp;
        hunteratk = newAtk;
        hunterdef = newDef;
        hunterspeed = newSpd;

        // restore hp preserving percentage if requested
        if (curPct >= 0f)
        {
            hunterhp = Mathf.Clamp(Mathf.RoundToInt(curPct * hunterMaxHp), 0, hunterMaxHp);
        }
        else
        {
            hunterhp = Mathf.Clamp(hunterhp, 0, hunterMaxHp);
        }

        OnHpChanged?.Invoke(hunterhp, hunterMaxHp);
        Debug.Log($"[{name}] Scaled to level {hunterlevel}: HP {hunterhp}/{hunterMaxHp} ATK {hunteratk} DEF {hunterdef} SPD {hunterspeed}");
    }

    // Heuristic search for main character stat (tries explicit assignment first)
    ICharacterStat FindMainCharacterStat()
    {
        // 1) explicit GameObject assigned
        if (mainCharacter != null)
        {
            // check components on that object implementing ICharacterStat
            foreach (var mb in mainCharacter.GetComponents<MonoBehaviour>())
            {
                if (mb is ICharacterStat ics) return ics;
            }
        }

        // 2) search by likely name hints (Main, Player, Knight, Hero)
        var all = FindObjectsOfType<MonoBehaviour>();
        foreach (var mb in all)
        {
            if (mb is ICharacterStat ics)
            {
                var go = mb.gameObject;
                string n = go.name.ToLower();
                if (n.Contains("main") || n.Contains("player") || n.Contains("knight") || n.Contains("hero"))
                {
                    return ics;
                }
            }
        }

        // 3) fallback: first ICharacterStat that does not look like a monster (try to avoid gameObjects with IMonsterStat)
        foreach (var mb in all)
        {
            if (mb is ICharacterStat ics)
            {
                var go = mb.gameObject;
                if (go.GetComponent<IMonsterStat>() == null) return ics;
            }
        }

        // not found
        return null;
    }

    // รับ Damage
    public void TakeDamage(int damage)
    {
        Debug.Log($"TakeDamage called on {gameObject.name} dmg={damage} time={Time.time}");
        int dmg = Mathf.Max(damage - hunterdef, 1);
        hunterhp -= dmg;
        hunterhp = Mathf.Max(0, hunterhp);

        Debug.Log($"Hunter got hit {dmg} HP left: {hunterhp}/{hunterMaxHp}");

        // notify UI
        OnHpChanged?.Invoke(hunterhp, hunterMaxHp);

        if (hunterhp <= 0)
        {
            hunterhp = 0;
            Die();
        }
    }

    // Heal (utility)
    public void Heal(int amount)
    {
        if (amount <= 0) return;
        hunterhp = Mathf.Min(hunterhp + amount, hunterMaxHp);
        OnHpChanged?.Invoke(hunterhp, hunterMaxHp);
    }

    // Change max HP safely. keepPercent=true preserves current hp percentage.
    public void SetMaxHp(int newMaxHp, bool keepPercent = true)
    {
        if (newMaxHp <= 0) newMaxHp = 1;
        if (keepPercent)
        {
            float pct = (hunterMaxHp > 0) ? (float)hunterhp / hunterMaxHp : 1f;
            hunterMaxHp = newMaxHp;
            hunterhp = Mathf.Clamp(Mathf.RoundToInt(pct * hunterMaxHp), 0, hunterMaxHp);
        }
        else
        {
            hunterMaxHp = newMaxHp;
            hunterhp = Mathf.Clamp(hunterhp, 0, hunterMaxHp);
        }
        OnHpChanged?.Invoke(hunterhp, hunterMaxHp);
    }

    // Set current HP as percentage (0..1)
    public void SetHpPercent(float pct)
    {
        pct = Mathf.Clamp01(pct);
        hunterhp = Mathf.RoundToInt(pct * hunterMaxHp);
        OnHpChanged?.Invoke(hunterhp, hunterMaxHp);
    }

    // ตาย
    void Die()
    {
        Debug.Log($"{Name} died");
        // remove manager UI if any (HealthBarManager.RemoveFor is safe no-op if not registered)
        if (HealthBarManager.Instance != null)
            HealthBarManager.Instance.RemoveFor(gameObject);
        Destroy(gameObject);
    }

    // โจมตี Monster
    public void AttackMonster(IMonsterStat monster)
    {
        if (monster == null) return;
        monster.TakeDamage(hunteratk);
        // Animation / Sound เพิ่มตรงนี้
    }

    public void StrongAttackMonster(IMonsterStat monster)
    {
        if (monster == null) return;
        int strongAtk = atk * 2; // แรงขึ้น 2 เท่า
        monster.TakeDamage(strongAtk);
        // Animation / Sound เพิ่มตรงนี้
    }

    void OnDestroy()
    {
        // ensure manager cleanup
        if (HealthBarManager.Instance != null)
            HealthBarManager.Instance.RemoveFor(gameObject);
    }
}