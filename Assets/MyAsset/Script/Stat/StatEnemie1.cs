using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatEnemie1Monster (updated)
/// - Monster level can be set from main character at spawn (optional)
/// - Stats randomized from configurable base ranges (level 1) and stepPerLevel added per level
/// - Gameplay max HP set to randomized HP on spawn (bar full)
/// - Implements IMonsterStat / IHpProvider as before
/// </summary>
public class StatEnemie1Monster : MonoBehaviour, IMonsterStat, IHpProvider
{
    [Header("Monster Stat")]
    public string _monsterName = "Chess";
    [Tooltip("Monster level (will be overwritten at spawn if useMainLevelOnSpawn=true)")]
    public int _monsterLevel = 1;

    // current / base stats
    public int _monsterHp;
    public int _monsterAtk;
    public int _monsterDef;
    public int _monsterSpeed;

    // gameplay max hp (used by game logic)
    [Header("Runtime (gameplay)")]
    [Tooltip("Gameplay max HP used for clamps and combat calculations.")]
    public int _monsterMaxHpGame = 30;

    // UI-only max HP (optional). If > 0, UI will use this value as the max for the health bar.
    [Header("UI-only (optional)")]
    [Tooltip("If > 0, UI will use this value as its max. If 0, UI falls back to gameplay max.")]
    public int uiMaxHp = 0;

    [Header("EXP (designer configurable)")]
    [Tooltip("Base EXP for level 1. Final exp = Round(baseExp * expMultiplier^(level-1))")]
    public int baseExp = 10;
    [Tooltip("Multiplier per level (e.g. 1.2 => each level gives 20% more EXP)")]
    public float expMultiplier = 1.2f;

    // --- New: base ranges for level 1 and scaling step ---
    [Header("Base Ranges (level 1)")]
    public int baseHpMin = 10;
    public int baseHpMax = 30;
    public int baseAtkMin = 10;
    public int baseAtkMax = 15;
    public int baseDefMin = 5;
    public int baseDefMax = 12;
    public int baseSpeedMin = 5;
    public int baseSpeedMax = 10;

    [Header("Scaling")]
    [Tooltip("Amount to add to both min and max for each additional level (step per level).")]
    public int stepPerLevel = 3;

    [Header("Level behavior")]
    [Tooltip("If true, on spawn this monster will copy Main Character level (PlayerStat.level or MCstat1.level)")]
    public bool useMainLevelOnSpawn = true;

    // Implement IMonsterStat properties (names must match interface)
    public string monsterName => _monsterName;
    public int monsterLevel => _monsterLevel;
    public int monsterHp => _monsterHp;
    public int monsterMaxHp => _monsterMaxHpGame; // expose gameplay max
    public int monsterAtk => _monsterAtk;
    public int monsterDef => _monsterDef;
    public int monsterSpeed => _monsterSpeed;

    // EXP value awarded when this monster is defeated — scales with level
    public int expValue
    {
        get
        {
            int lvl = Math.Max(1, _monsterLevel);
            double val = baseExp * Math.Pow((double)expMultiplier, lvl - 1);
            int outVal = Math.Max(1, (int)Math.Round(val));
            return outVal;
        }
    }

    // IHpProvider implementation (event + props) for HealthBarManager subscribe
    public event Action<int, int> OnHpChanged; // (current, maxForUI)
    public int CurrentHp => _monsterHp;
    public int MaxHp => _monsterMaxHpGame; // other systems see gameplay max

    [Header("World-space UI (optional)")]
    [Tooltip("Assign a small World-Space Canvas prefab that contains an Image for HP fill and a Text for name.")]
    public GameObject uiPrefab; // prefab structure described below
    GameObject uiInstance;
    Image hpFillImage;
    Text nameText;
    CanvasGroup uiCanvasGroup;

    [Header("UI Options")]
    public Vector3 uiWorldOffset = new Vector3(0, 2.0f, 0);
    public bool faceCamera = true;
    public float uiSmoothing = 8f; // lerp smoothing for hp bar

    [Header("HealthBarManager (screen-space UI)")]
    [Tooltip("If enabled, this monster will request a healthbar from HealthBarManager at runtime.")]
    public bool createManagerUI = true;

    // internal displayed fill for smoothing
    float displayedFill = 1f;

    void Start()
    {
        // optionally copy main char level (only at spawn)
        if (useMainLevelOnSpawn)
        {
            // try PlayerStat first, then MCstat1
            var ps = FindObjectOfType<PlayerStat>();
            if (ps != null)
            {
                _monsterLevel = Math.Max(1, ps.level);
            }
            else
            {
                var mc = FindObjectOfType<MCstat1>();
                if (mc != null)
                {
                    // assume MCstat1 has a 'level' field/property; fallback if not present
                    try
                    {
                        var t = mc.GetType();
                        var prop = t.GetProperty("level");
                        if (prop != null) _monsterLevel = Math.Max(1, (int)prop.GetValue(mc));
                        else
                        {
                            var field = t.GetField("level");
                            if (field != null) _monsterLevel = Math.Max(1, (int)field.GetValue(mc));
                        }
                    }
                    catch { /* ignore reflection errors */ }
                }
            }
        }

        // set stats based on level (randomized once at spawn)
        SetStatsByLevel();

        // use the randomized _monsterHp as the gameplay max so the bar is full on spawn
        _monsterMaxHpGame = Mathf.Max(1, _monsterHp);
        _monsterHp = Mathf.Clamp(_monsterHp, 0, _monsterMaxHpGame);

        // initial displayed fill uses uiMax if set, otherwise gameplay max
        displayedFill = GetFillFraction();

        SetupUI();
        UpdateUIImmediate();

        // fire initial event with UI max (so manager/UI draws correctly)
        OnHpChanged?.Invoke(_monsterHp, GetMaxForUI());

        // optionally register with HealthBarManager (safe wait)
        if (createManagerUI)
            StartCoroutine(RegisterHealthBarSafe());

        Debug.Log($"{_monsterName} Level {_monsterLevel} HP: {_monsterHp}/{_monsterMaxHpGame}, ATK: {_monsterAtk}, Def: {_monsterDef}, Speed: {_monsterSpeed}, EXP: {expValue}");
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
            Debug.LogWarning($"{name}: HealthBarManager not found, manager UI not created.");
            yield break;
        }

        var hb = HealthBarManager.Instance.CreateFor(gameObject, transform);
        if (hb != null)
        {
            var follower = hb.GetComponent<HealthBarFollower>();
            if (follower != null)
            {
                follower.worldOffset = uiWorldOffset;
                if (follower.uiCamera == null && Camera.main != null)
                    follower.uiCamera = Camera.main;
            }
            Debug.Log($"{name}: Registered manager healthbar.");
        }
    }

    void Update()
    {
        // rotate/face camera if required
        if (uiInstance != null)
        {
            uiInstance.transform.position = transform.position + uiWorldOffset;

            if (faceCamera && Camera.main != null)
            {
                Vector3 camPos = Camera.main.transform.position;
                uiInstance.transform.LookAt(uiInstance.transform.position + (uiInstance.transform.position - camPos));
            }

            // smooth HP fill
            if (hpFillImage != null)
            {
                float targetFill = GetFillFraction();
                displayedFill = Mathf.Lerp(displayedFill, targetFill, Time.deltaTime * uiSmoothing);
                hpFillImage.fillAmount = displayedFill;
            }
        }
    }

    // กำหนดค่าสถานะตามเลเวล (randomized once at spawn)
    void SetStatsByLevel()
    {
        int lvl = Math.Max(1, _monsterLevel);

        // compute ranges by adding stepPerLevel*(lvl-1) to base mins/maxs
        int hpMin = baseHpMin + (lvl - 1) * stepPerLevel;
        int hpMax = baseHpMax + (lvl - 1) * stepPerLevel;
        int atkMin = baseAtkMin + (lvl - 1) * stepPerLevel;
        int atkMax = baseAtkMax + (lvl - 1) * stepPerLevel;
        int defMin = baseDefMin + (lvl - 1) * stepPerLevel;
        int defMax = baseDefMax + (lvl - 1) * stepPerLevel;
        int spdMin = baseSpeedMin + (lvl - 1) * stepPerLevel;
        int spdMax = baseSpeedMax + (lvl - 1) * stepPerLevel;

        // clamp sensible ranges (min <= max)
        hpMin = Mathf.Min(hpMin, hpMax);
        atkMin = Mathf.Min(atkMin, atkMax);
        defMin = Mathf.Min(defMin, defMax);
        spdMin = Mathf.Min(spdMin, spdMax);

        // Random.Range with ints is exclusive on max, so use max+1 for inclusive behavior
        _monsterHp = UnityEngine.Random.Range(hpMin, hpMax + 1);
        _monsterAtk = UnityEngine.Random.Range(atkMin, atkMax + 1);
        _monsterDef = UnityEngine.Random.Range(defMin, defMax + 1);
        _monsterSpeed = UnityEngine.Random.Range(spdMin, spdMax + 1);
    }

    // รับ Damage
    public void TakeDamage(int damage)
    {
        Debug.Log($"TakeDamage called on {gameObject.name} dmg={damage} time={Time.time}");
        Debug.Log(System.Environment.StackTrace);
        int dmg = Mathf.Max(damage - _monsterDef, 1);
        _monsterHp -= dmg;
        _monsterHp = Mathf.Max(0, _monsterHp);

        Debug.Log($"{monsterName} got hit {dmg} HP left: {_monsterHp}/{_monsterMaxHpGame}");

        // update UI and event (send UI max)
        UpdateUIImmediate();
        OnHpChanged?.Invoke(_monsterHp, GetMaxForUI());

        if (_monsterHp <= 0)
        {
            Die();
        }
    }

    // ตาย
    void Die()
    {
        // record defeat so TurnManager can assemble rewards/exp
        if (TurnManager.Instance != null)
            TurnManager.Instance.RecordEnemyDefeated(gameObject);

        // hide UI gracefully if exists
        if (uiCanvasGroup != null)
        {
            uiCanvasGroup.alpha = 0f;
            uiCanvasGroup.interactable = false;
            uiCanvasGroup.blocksRaycasts = false;
        }
        else if (uiInstance != null)
        {
            uiInstance.SetActive(false);
        }

        // if registered with manager, manager will remove when we call RemoveFor in OnDestroy
        Destroy(gameObject);
    }

    // โจมตี Player ผ่าน interface
    public void AttackPlayer(ICharacterStat player)
    {
        if (player != null)
        {
            player.TakeDamage(_monsterAtk);
            Debug.Log($"{monsterName} attacks {player.Name} with {_monsterAtk} damage");
        }
    }

    // --- UI helper methods ---
    void SetupUI()
    {
        if (uiPrefab == null)
        {
            Debug.LogWarning($"StatEnemie1Monster ({name}): uiPrefab not assigned. To show HP above monster, assign a world-space UI prefab.");
            return;
        }

        uiInstance = Instantiate(uiPrefab, transform.position + uiWorldOffset, Quaternion.identity, null);
        uiInstance.transform.position = transform.position + uiWorldOffset;

        Transform nameTf = uiInstance.transform.Find("NameText");
        if (nameTf != null) nameText = nameTf.GetComponent<Text>();
        Transform fillTf = uiInstance.transform.Find("HPBackground/HPFill");
        if (fillTf != null) hpFillImage = fillTf.GetComponent<Image>();

        if (nameText == null) nameText = uiInstance.GetComponentInChildren<Text>();
        if (hpFillImage == null)
        {
            var imgs = uiInstance.GetComponentsInChildren<Image>(true);
            foreach (var img in imgs)
            {
                string iname = img.gameObject.name.ToLower();
                if (iname.Contains("fill") || iname.Contains("hp"))
                {
                    hpFillImage = img;
                    break;
                }
            }
        }

        uiCanvasGroup = uiInstance.GetComponent<CanvasGroup>();
        if (uiCanvasGroup == null) uiCanvasGroup = uiInstance.AddComponent<CanvasGroup>();

        if (nameText != null) nameText.text = _monsterName;
    }

    void UpdateUIImmediate()
    {
        if (hpFillImage != null)
        {
            float fill = GetFillFraction();
            hpFillImage.fillAmount = fill;
            displayedFill = fill;
        }
        if (nameText != null)
        {
            nameText.text = _monsterName;
        }
        if (uiInstance != null)
        {
            uiInstance.transform.position = transform.position + uiWorldOffset;
        }
    }

    void OnDestroy()
    {
        if (uiInstance != null)
        {
            Destroy(uiInstance);
        }

        if (HealthBarManager.Instance != null)
        {
            HealthBarManager.Instance.RemoveFor(gameObject);
        }
    }

    // Optional debuff APIs (stubs)
    public void ApplyBleed(int damagePerTurn, int duration)
    {
        Debug.Log($"{monsterName} would receive Bleed {damagePerTurn} for {duration} turns (Not implemented here).");
    }

    public void ApplyStun(int duration)
    {
        Debug.Log($"{monsterName} would be Stunned for {duration} turns (Not implemented here).");
    }

    // --- Helpers for UI vs gameplay max ---
    int GetMaxForUI()
    {
        return (uiMaxHp > 0) ? uiMaxHp : _monsterMaxHpGame;
    }

    float GetFillFraction()
    {
        int maxForUI = GetMaxForUI();
        return (maxForUI > 0) ? (float)_monsterHp / maxForUI : 0f;
    }
}