using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TurnOrderIcon
/// - auto-assign iconImage ถ้ายังว่าง
/// - ตั้งค่า sprite, ชื่อ และ highlight ตามที่เรียก SetData
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TurnOrderIcon : MonoBehaviour
{
    [Tooltip("Image component used as the icon sprite")]
    public Image iconImage;

    [Tooltip("Optional Text component to show name/HP")]
    public Text nameText;

    [Tooltip("Optional highlight GameObject (e.g. border) that shows when this is the current turn")]
    public GameObject highlight;

    [Tooltip("Scale applied to current icon")]
    public float currentScale = 1.15f;

    Vector3 defaultScale;

    void Awake()
    {
        // try to auto-assign the iconImage: first search assigned, then child Image, then own Image component
        if (iconImage == null)
            iconImage = GetComponentInChildren<Image>();

        if (iconImage == null)
            iconImage = GetComponent<Image>();

        defaultScale = transform.localScale;
    }

    public void SetData(string displayName, Sprite sprite, bool isCurrent)
    {
        if (iconImage != null)
        {
            iconImage.sprite = sprite;
            iconImage.enabled = sprite != null;
        }

        if (nameText != null) nameText.text = displayName;

        if (highlight != null) highlight.SetActive(isCurrent);

        // simple scale highlight
        transform.localScale = isCurrent ? defaultScale * currentScale : defaultScale;
    }
}