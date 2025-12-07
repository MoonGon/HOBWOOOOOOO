using UnityEngine;

/// <summary>
/// Simple highlight helper:
/// - Attach to enemy GameObject (or prefab).
/// - Option A: assign a child GameObject (highlightObject) that contains a sprite/graphic for the yellow frame; it will be enabled/disabled.
/// - Option B: leave highlightObject null and set autoCreateCopy = true to create a copy sprite that follows the character.
/// - Call Select() to highlight this object (it will clear any previous highlight).
/// - Call Deselect() to remove highlight.
/// - Call ClearSelection() to deselect global current selection.
/// </summary>
public class HighlightOnSelect : MonoBehaviour
{
    public GameObject highlightObject; // optional: child object with highlight graphic (disabled by default)
    public bool autoCreateCopy = true;
    public Color highlightColor = new Color(1f, 0.9f, 0.2f, 0.9f);
    public Vector3 offset = Vector3.zero;
    public Vector3 scale = new Vector3(1.15f, 1.15f, 1f);
    public int orderOffset = 1;

    SpriteRenderer _targetSprite;
    SpriteRenderer _autoRenderer;

    static HighlightOnSelect s_current;

    void Awake()
    {
        _targetSprite = GetComponentInChildren<SpriteRenderer>() ?? GetComponent<SpriteRenderer>();

        if (highlightObject != null)
        {
            highlightObject.SetActive(false);
            return;
        }

        if (autoCreateCopy && _targetSprite != null)
        {
            var go = new GameObject("__HighlightCopy_" + gameObject.name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            _autoRenderer = go.AddComponent<SpriteRenderer>();
            _autoRenderer.color = highlightColor;
            _autoRenderer.sortingLayerID = _targetSprite.sortingLayerID;
            _autoRenderer.sortingOrder = _targetSprite.sortingOrder + orderOffset;
            go.SetActive(false);
            highlightObject = go;
        }
    }

    void Update()
    {
        if (_autoRenderer != null && _targetSprite != null && highlightObject != null && highlightObject.activeSelf)
        {
            if (_autoRenderer.sprite != _targetSprite.sprite) _autoRenderer.sprite = _targetSprite.sprite;
            _autoRenderer.flipX = _targetSprite.flipX;
            _autoRenderer.flipY = _targetSprite.flipY;
            _autoRenderer.sortingLayerID = _targetSprite.sortingLayerID;
            _autoRenderer.sortingOrder = _targetSprite.sortingOrder + orderOffset;
        }
    }

    public void Select()
    {
        if (s_current == this) return;
        if (s_current != null) s_current.Deselect();
        s_current = this;
        if (highlightObject != null) highlightObject.SetActive(true);
    }

    public void Deselect()
    {
        if (s_current == this) s_current = null;
        if (highlightObject != null) highlightObject.SetActive(false);
    }

    void OnDisable()
    {
        if (s_current == this) s_current = null;
    }

    // Public helper to clear the current highlight globally
    public static void ClearSelection()
    {
        if (s_current != null) s_current.Deselect();
    }

    // Optional: get currently highlighted object
    public static HighlightOnSelect GetCurrent() => s_current;
}