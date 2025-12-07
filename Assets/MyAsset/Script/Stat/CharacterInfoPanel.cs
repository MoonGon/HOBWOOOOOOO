// (วางทับไฟล์เดิม — นี่คือไฟล์เดียวกับที่คุณส่งมาแต่เพิ่มพารามิเตอร์ extraSlideDistance)
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// CharacterInfoPanel (TextMeshPro) - shows portrait, HP, level, attributes, with slide/open/close.
/// Now supports defaulting to Main Character on start and portrait lookup via IPortraitProvider / SpriteRenderer / UI.Image / reflection.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CharacterInfoPanel : MonoBehaviour
{
    [Header("Sliding")]
    public RectTransform panelRect;
    public Button handleButton;
    public RectTransform handleRect;
    public float handleVisibleWidth = 28f;
    public float slideDuration = 0.25f;
    public bool startClosed = true;
    public bool respectEditorPosition = true;
    public bool bringToFrontOnOpen = true;
    public bool slideRight = false;

    [Header("Sliding: extra distance")]
    [Tooltip("เพิ่มระยะที่ panel จะเลื่อนออกมานอกเหนือจากขนาดของ panel (พิกเซล). ใส่ค่าบวกเพื่อเลื่อนไกลขึ้น.")]
    public float extraSlideDistance = 0f;

    [Header("Overlay (optional)")]
    public Button overlayButton;

    [Header("Portrait / HP")]
    public Image portraitImage;
    public Image portraitFrameImage;
    public Slider hpSlider;
    public TextMeshProUGUI hpText;

    [Header("Level (progress)")]
    public TextMeshProUGUI levelLabelText;
    public Slider levelSlider;
    public TextMeshProUGUI levelValueText;

    [Header("Attributes (read-only)")]
    public TextMeshProUGUI atkText;
    public TextMeshProUGUI defText;
    public TextMeshProUGUI speedText;

    [Header("General Info")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI descriptionText;

    [Header("Canvas override (to keep panel on top)")]
    public bool ensureOverrideCanvas = true;
    public int overrideSortingOrder = 200;

    [Header("Default / Startup")]
    [Tooltip("If true, the panel will try to set its initial target to the Main Character (MCstat1) on Start.")]
    public bool showMainByDefault = true;
    [Tooltip("Optional: explicitly assign the main character GameObject here to avoid automatic search.")]
    public GameObject mainCharacterOverride;
    [Tooltip("If true, open the panel after setting the default target.")]
    public bool openOnDefault = true;

    [Header("HP Animation")]
    public float hpAnimateDuration = 0.35f;

    // runtime
    RectTransform rt;
    Vector2 baselineAnchoredPos;
    Vector2 closedPos;
    Vector2 openedPos;
    Coroutine slideCoroutine;
    bool isOpen = false;

    // target bindings
    GameObject target;
    IHpProvider subscribedHpSource;
    PlayerStat subscribedPlayerStat;

    // portrait provider
    IPortraitProvider _portraitProvider;

    // cached canvas reference if we create one
    Canvas panelCanvas;

    // coroutine handle for HP animation
    private Coroutine hpAnimCoroutine = null;

    // last known HP to avoid re-animating each frame
    int lastHp = -1;
    int lastMax = -1;

    void Reset()
    {
        if (panelRect == null) panelRect = GetComponent<RectTransform>();
        if (handleButton == null)
        {
            var b = transform.Find("Handle")?.GetComponent<Button>();
            if (b != null) handleButton = b;
        }
        if (handleRect == null && handleButton != null) handleRect = handleButton.GetComponent<RectTransform>();
    }

    void Awake()
    {
        rt = panelRect != null ? panelRect : GetComponent<RectTransform>();
        if (handleButton != null) handleButton.onClick.AddListener(Toggle);
        if (overlayButton != null) overlayButton.onClick.AddListener(Close);
    }

    IEnumerator Start()
    {
        if (rt == null) rt = GetComponent<RectTransform>();

        yield return new WaitForEndOfFrame();

        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        baselineAnchoredPos = rt.anchoredPosition;
        RecalculatePositions();

        isOpen = !startClosed;

        if (!respectEditorPosition)
        {
            rt.anchoredPosition = isOpen ? openedPos : closedPos;
        }

        UpdateHandleAnchor();
        EnsureOverrideCanvas();

        if (overlayButton != null) overlayButton.gameObject.SetActive(isOpen);

        Debug.Log($"[CharacterInfoPanel] Start: baseline={baselineAnchoredPos} opened={openedPos.x} closed={closedPos.x} slideRight={slideRight} overrideCanvas={(panelCanvas != null ? "yes" : "no")}");

        // --- default target logic ---
        if (showMainByDefault && target == null)
        {
            GameObject main = mainCharacterOverride;
            if (main == null)
            {
                var mainStat = FindObjectOfType<MCstat1>();
                if (mainStat != null) main = mainStat.gameObject;
            }

            if (main != null)
            {
                SetTarget(main);
                if (openOnDefault) Open();
            }
            else
            {
                Debug.LogWarning("[CharacterInfoPanel] showMainByDefault enabled but no MCstat1/mainCharacter found.");
            }
        }
    }

    void EnsureOverrideCanvas()
    {
        if (!ensureOverrideCanvas) return;

        panelCanvas = GetComponent<Canvas>();
        bool added = false;
        if (panelCanvas == null)
        {
            panelCanvas = gameObject.AddComponent<Canvas>();
            added = true;
        }

        panelCanvas.overrideSorting = true;
        panelCanvas.sortingOrder = overrideSortingOrder;

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        if (added)
            Debug.Log($"[CharacterInfoPanel] Added Canvas component and set overrideSorting={panelCanvas.overrideSorting} order={panelCanvas.sortingOrder}");
        else
            Debug.Log($"[CharacterInfoPanel] Using existing Canvas and set overrideSorting={panelCanvas.overrideSorting} order={panelCanvas.sortingOrder}");
    }

    void UpdateHandleAnchor()
    {
        if (handleRect == null) return;

        handleRect.pivot = new Vector2(0.5f, 0.5f);
        if (slideRight)
        {
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.anchoredPosition = new Vector2(-handleVisibleWidth * 0.5f, 0f);
        }
        else
        {
            handleRect.anchorMin = new Vector2(1f, 0.5f);
            handleRect.anchorMax = new Vector2(1f, 0.5f);
            handleRect.anchoredPosition = new Vector2(handleVisibleWidth * 0.5f, 0f);
        }
    }

    public void RecalculatePositions(bool forceBaselineFromCurrent = false)
    {
        if (rt == null) rt = GetComponent<RectTransform>();

        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        float w = Mathf.Max(0.0001f, rt.rect.width);

        if (forceBaselineFromCurrent)
        {
            baselineAnchoredPos = rt.anchoredPosition;
        }
        openedPos = new Vector2(baselineAnchoredPos.x, baselineAnchoredPos.y);

        // NOTE: use extraSlideDistance to extend how far the panel 'slides out'
        float effectiveDistance = (w - handleVisibleWidth) + extraSlideDistance;

        if (slideRight)
            closedPos = new Vector2(baselineAnchoredPos.x + effectiveDistance, baselineAnchoredPos.y);
        else
            closedPos = new Vector2(baselineAnchoredPos.x - effectiveDistance, baselineAnchoredPos.y);
    }

    void Update()
    {
        if (target != null) RefreshFromTarget();
    }

    public void Toggle()
    {
        RecalculatePositions(false);

        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        slideCoroutine = StartCoroutine(SlideTo(!isOpen));
    }

    public void Open()
    {
        if (isOpen) return;
        if (bringToFrontOnOpen && rt != null) rt.SetAsLastSibling();

        EnsureOverrideCanvas();

        if (overlayButton != null) overlayButton.gameObject.SetActive(true);
        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        RecalculatePositions(false);
        slideCoroutine = StartCoroutine(SlideTo(true));
    }

    public void Close()
    {
        if (!isOpen) return;
        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        RecalculatePositions(false);
        slideCoroutine = StartCoroutine(SlideTo(false));
    }

    IEnumerator SlideTo(bool open)
    {
        RecalculatePositions(false);

        isOpen = open;
        Vector2 from = rt.anchoredPosition;
        Vector2 to = open ? openedPos : closedPos;

        float t = 0f;
        while (t < slideDuration)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / slideDuration));
            rt.anchoredPosition = Vector2.Lerp(from, to, f);
            yield return null;
        }

        rt.anchoredPosition = to;
        slideCoroutine = null;

        if (!isOpen && overlayButton != null) overlayButton.gameObject.SetActive(false);

        Debug.Log($"[CharacterInfoPanel] Slide finished. isOpen={isOpen} anchoredPos={rt.anchoredPosition} openedX={openedPos.x} closedX={closedPos.x}");
    }

    // --- HP display helper (animated) ---
    public void ShowHp(int current, int max, bool animate = true)
    {
        max = Mathf.Max(1, max);
        current = Mathf.Clamp(current, 0, max);

        if (hpSlider != null)
        {
            if (!hpSlider.gameObject.activeSelf) hpSlider.gameObject.SetActive(true);
            hpSlider.maxValue = max;

            if (!animate)
            {
                hpSlider.value = max;
                UpdateHpText(max, max);
                lastHp = max;
                lastMax = max;
                EnsureFillColorRed();
                return;
            }

            if (lastHp == current && lastMax == max)
            {
                return;
            }

            if (hpAnimCoroutine != null) StopCoroutine(hpAnimCoroutine);
            hpAnimCoroutine = StartCoroutine(AnimateHp(hpSlider.value, current, hpAnimateDuration));
            lastHp = current;
            lastMax = max;
        }
        else
        {
            UpdateHpText(current, max);
            lastHp = current;
            lastMax = max;
        }

        if (hpText != null)
        {
            if (!hpText.gameObject.activeSelf) hpText.gameObject.SetActive(true);
        }

        EnsureFillColorRed();
    }

    IEnumerator AnimateHp(float fromValue, float toValue, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
            float value = Mathf.Lerp(fromValue, toValue, Mathf.SmoothStep(0f, 1f, t));
            if (hpSlider != null) hpSlider.value = value;
            UpdateHpText(Mathf.RoundToInt(value), (hpSlider != null ? Mathf.RoundToInt(hpSlider.maxValue) : Mathf.Max(1, Mathf.RoundToInt(value))));
            yield return null;
        }

        if (hpSlider != null) hpSlider.value = toValue;
        UpdateHpText(Mathf.RoundToInt(toValue), (hpSlider != null ? Mathf.RoundToInt(hpSlider.maxValue) : Mathf.Max(1, Mathf.RoundToInt(toValue))));
        hpAnimCoroutine = null;
    }

    void UpdateHpText(int cur, int max)
    {
        if (hpText == null) return;
        hpText.text = $"{cur}/{max}";
        if (cur < max) hpText.color = Color.red;
        else hpText.color = Color.white;
    }

    void EnsureFillColorRed()
    {
        if (hpSlider != null && hpSlider.fillRect != null)
        {
            var fillImage = hpSlider.fillRect.GetComponent<Image>();
            if (fillImage != null)
            {
                fillImage.color = Color.red;
            }
        }
    }

    // Set the panel to show info for this GameObject
    public void SetTarget(GameObject go)
    {
        UnsubscribeFromTarget();
        UnsubscribePortraitProvider();

        target = go;
        Debug.Log($"[CharacterInfoPanel] SetTarget called for: {(go != null ? go.name : "null")}");

        if (target == null)
        {
            ClearUI();
            return;
        }

        // portrait first (so UI shows immediately)
        SetPortraitFromTarget(target);

        // read static attributes (one-time) first
        var cs2 = target.GetComponent<ICharacterStat>();
        if (cs2 != null)
        {
            if (nameText != null) nameText.text = cs2.Name ?? target.name;
            if (atkText != null) atkText.text = $"ATK: {cs2.atk}";
            if (defText != null) defText.text = $"DEF: {cs2.def}";
            if (speedText != null) speedText.text = $"SPD: {cs2.speed}";
        }

        // Try to initialize HP display as FULL immediately (do not animate)
        var hpProv = target.GetComponent<IHpProvider>();
        if (hpProv != null)
        {
            subscribedHpSource = hpProv;
            try { hpProv.OnHpChanged += OnTargetHpChanged; } catch { }
            try
            {
                int cur = hpProv.CurrentHp;
                int max = hpProv.MaxHp;
                ShowHp(max, max, animate: false);
                if (cur < max)
                {
                    ShowHp(cur, max, animate: true);
                    Debug.Log($"[CharacterInfoPanel] Initial HP from IHpProvider (damaged): {cur}/{max}");
                }
                else
                {
                    Debug.Log($"[CharacterInfoPanel] Initial HP from IHpProvider (full): {cur}/{max}");
                }
            }
            catch
            {
                // fallthrough - will try other fallbacks below
            }
        }
        else
        {
            var cs = target.GetComponent<ICharacterStat>();
            if (cs != null && hpSlider != null)
            {
                int cur = cs.hp;
                int max = TryGetMaxHpFromCharacter(cs, cur);
                ShowHp(max, max, animate: false);
                if (cur < max)
                {
                    ShowHp(cur, max, animate: true);
                    if (hpText != null) Debug.Log($"[CharacterInfoPanel] Initial HP from ICharacterStat (damaged): {cur}/{max}");
                }
                else
                {
                }
            }
            else
            {
                if (hpSlider != null) hpSlider.gameObject.SetActive(false);
                if (hpText != null) hpText.gameObject.SetActive(false);
            }
        }

        // subscribe to exp/level events if available
        var ps = target.GetComponent<PlayerStat>();
        if (ps != null)
        {
            subscribedPlayerStat = ps;
            try { ps.OnExpChanged += OnTargetExpChanged; } catch { }
            try { ps.OnLevelUp += OnTargetLevelUp; } catch { }
            OnTargetExpChanged(ps.currentExp, ps.ExpToNext);
        }
    }

    int TryGetMaxHpFromCharacter(object csObj, int fallback)
    {
        if (csObj == null) return Mathf.Max(1, fallback);
        var t = csObj.GetType();

        string[] propNames = { "maxHp", "MaxHp", "hpMax", "HpMax", "maxHP", "MaxHP", "max_health", "maxHealth" };
        foreach (var n in propNames)
        {
            var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
            if (p != null)
            {
                try { return Convert.ToInt32(p.GetValue(csObj)); } catch { }
            }
            var f = t.GetField(n, BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                try { return Convert.ToInt32(f.GetValue(csObj)); } catch { }
            }
        }

        return Mathf.Max(1, fallback);
    }

    void UnsubscribeFromTarget()
    {
        if (subscribedHpSource != null)
        {
            try { subscribedHpSource.OnHpChanged -= OnTargetHpChanged; } catch { }
            subscribedHpSource = null;
        }
        if (subscribedPlayerStat != null)
        {
            try { subscribedPlayerStat.OnExpChanged -= OnTargetExpChanged; } catch { }
            try { subscribedPlayerStat.OnLevelUp -= OnTargetLevelUp; } catch { }
            subscribedPlayerStat = null;
        }
    }

    // portrait helper: unsubscribe provider
    void UnsubscribePortraitProvider()
    {
        if (_portraitProvider != null)
        {
            try { _portraitProvider.OnPortraitChanged -= OnPortraitProviderChanged; } catch { }
            _portraitProvider = null;
        }
    }

    void OnTargetHpChanged(int current, int uiMax)
    {
        if (current != lastHp || uiMax != lastMax)
        {
            ShowHp(current, uiMax, animate: true);
            Debug.Log($"[CharacterInfoPanel] OnTargetHpChanged -> {current}/{uiMax}");
        }
    }

    void OnTargetExpChanged(int currentExp, int reqExp)
    {
        if (levelSlider != null)
        {
            levelSlider.gameObject.SetActive(true);
            levelSlider.maxValue = Mathf.Max(1, reqExp);
            levelSlider.value = Mathf.Clamp(currentExp, 0, reqExp);
        }
        if (levelValueText != null && subscribedPlayerStat != null)
        {
            float pct = reqExp > 0 ? (100f * currentExp / reqExp) : 0f;
            levelValueText.text = $"Lv {subscribedPlayerStat.level} — {Mathf.RoundToInt(pct)}% ({currentExp}/{reqExp})";
        }
    }

    void OnTargetLevelUp(int oldLevel, int newLevel)
    {
        if (levelValueText != null)
        {
            levelValueText.text = $"Lv {newLevel}";
        }
    }

    void ClearUI()
    {
        if (portraitImage != null) portraitImage.sprite = null;
        if (hpSlider != null) { hpSlider.maxValue = 1; hpSlider.value = 0; hpSlider.gameObject.SetActive(false); }
        if (hpText != null) { hpText.text = ""; hpText.gameObject.SetActive(false); }
        if (levelSlider != null) { levelSlider.maxValue = 1; levelSlider.value = 0; levelSlider.gameObject.SetActive(false); }
        if (levelValueText != null) levelValueText.text = "";
        if (nameText != null) nameText.text = "";
        if (descriptionText != null) descriptionText.text = "";
        if (atkText != null) atkText.text = "";
        if (defText != null) defText.text = "";
        if (speedText != null) speedText.text = "";
    }

    void RefreshFromTarget()
    {
        if (target == null) return;

        var hpProv = target.GetComponent<IHpProvider>();
        if (hpProv != null)
        {
            if (hpProv.CurrentHp != lastHp || hpProv.MaxHp != lastMax)
                ShowHp(hpProv.CurrentHp, hpProv.MaxHp, animate: true);
        }
        else
        {
            var cs = target.GetComponent<ICharacterStat>();
            if (cs != null && hpSlider != null)
            {
                int cur = cs.hp;
                int max = TryGetMaxHpFromCharacter(cs, cur);
                if (cur != lastHp || max != lastMax)
                    ShowHp(cur, max, animate: true);
            }
        }

        var ps = target.GetComponent<PlayerStat>();
        if (ps != null)
        {
            OnTargetExpChanged(ps.currentExp, ps.ExpToNext);
        }
    }

    void OnDestroy()
    {
        UnsubscribeFromTarget();
        UnsubscribePortraitProvider();
        if (handleButton != null) handleButton.onClick.RemoveListener(Toggle);
        if (overlayButton != null) overlayButton.onClick.RemoveListener(Close);
    }

    public void SetAnchoredPosition(Vector2 anchoredPos)
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        baselineAnchoredPos = rt.anchoredPosition;
        RecalculatePositions(false);
    }

    public void MoveBy(float dx)
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        SetAnchoredPosition(rt.anchoredPosition + new Vector2(dx, 0f));
    }

    public void DebugShowBringToFront()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        rt.SetAsLastSibling();
        rt.localScale = Vector3.one;
        if (overlayButton != null) overlayButton.gameObject.SetActive(isOpen);
        Debug.Log($"CharacterInfoPanel DebugShowBringToFront anchoredPos={rt.anchoredPosition} size={rt.sizeDelta} openedX={openedPos.x} closedX={closedPos.x}");
    }

    // ---------- Portrait helpers ----------
    void SetPortraitFromTarget(GameObject go)
    {
        UnsubscribePortraitProvider();

        if (portraitImage == null)
        {
            Debug.LogWarning("[CharacterInfoPanel] portraitImage not assigned in inspector.");
            return;
        }

        if (go == null)
        {
            ApplyPortraitSprite(null);
            return;
        }

        // 1) IPortraitProvider on target or children
        var provider = go.GetComponent<IPortraitProvider>() ?? go.GetComponentInChildren<IPortraitProvider>();
        if (provider != null)
        {
            _portraitProvider = provider;
            try { _portraitProvider.OnPortraitChanged += OnPortraitProviderChanged; } catch { }
            var s = provider.GetPortrait();
            ApplyPortraitSprite(s);
            return;
        }

        // 2) SpriteRenderer on target or child
        var sr = go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            ApplyPortraitSprite(sr.sprite);
            return;
        }

        // 3) UI Image on target or child
        var uiImage = go.GetComponentInChildren<UnityEngine.UI.Image>();
        if (uiImage != null && uiImage.sprite != null)
        {
            ApplyPortraitSprite(uiImage.sprite);
            return;
        }

        // 4) Reflection: look for public field/property named portrait / portraitSprite
        Sprite reflected = TryGetPortraitViaReflection(go);
        if (reflected != null)
        {
            ApplyPortraitSprite(reflected);
            return;
        }

        // nothing found -> hide portrait
        ApplyPortraitSprite(null);
    }

    void OnPortraitProviderChanged(Sprite newSprite)
    {
        ApplyPortraitSprite(newSprite);
    }

    void ApplyPortraitSprite(Sprite s)
    {
        if (portraitImage == null) return;
        if (s == null)
        {
            portraitImage.sprite = null;
            portraitImage.gameObject.SetActive(false);
        }
        else
        {
            portraitImage.sprite = s;
            portraitImage.gameObject.SetActive(true);
        }
    }

    Sprite TryGetPortraitViaReflection(GameObject go)
    {
        if (go == null) return null;
        var comps = go.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            var t = c.GetType();
            var prop = t.GetProperty("portrait", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && typeof(Sprite).IsAssignableFrom(prop.PropertyType))
            {
                try { return (Sprite)prop.GetValue(c); } catch { }
            }
            var field = t.GetField("portrait", BindingFlags.Public | BindingFlags.Instance);
            if (field != null && typeof(Sprite).IsAssignableFrom(field.FieldType))
            {
                try { return (Sprite)field.GetValue(c); } catch { }
            }

            var prop2 = t.GetProperty("portraitSprite", BindingFlags.Public | BindingFlags.Instance);
            if (prop2 != null && typeof(Sprite).IsAssignableFrom(prop2.PropertyType))
            {
                try { return (Sprite)prop2.GetValue(c); } catch { }
            }
            var field2 = t.GetField("portraitSprite", BindingFlags.Public | BindingFlags.Instance);
            if (field2 != null && typeof(Sprite).IsAssignableFrom(field2.FieldType))
            {
                try { return (Sprite)field2.GetValue(c); } catch { }
            }
        }
        return null;
    }
}