using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

/// <summary>
/// ImageGalleryController
/// - กำหนด list ของ Sprite (images) ที่จะแสดง
/// - ระบุ UI Image (displayImage) เป็น target ที่จะแสดงรูป
/// - เมื่อคลิกขวา NextImage() จะถูกเรียก
/// - เมื่อถึงรูปสุดท้าย nextMapButton จะเปิดใช้งาน (visible & interactable)
/// - ผูกปุ่ม nextMapButton -> NextMap() หรือเรียก UnityEvent onComplete
/// </summary>
public class ImageGalleryController : MonoBehaviour
{
    [Header("Image list (set in Inspector)")]
    public List<Sprite> images = new List<Sprite>();

    [Header("UI References")]
    public Image displayImage;             // UI Image ที่จะแสดงรูป
    public Button nextMapButton;           // ปุ่มที่จะโชว์เมื่อถึงรูปสุดท้าย (ตั้ง inactive ใน Inspector)

    [Header("Scene / callback")]
    [Tooltip("ชื่อ Scene ที่จะโหลดเมื่อกดปุ่ม NextMap (ถ้าว่างจะใช้ onComplete event แทน)")]
    public string nextSceneName = "";

    [Tooltip("เรียกใช้งานเมื่อจบ gallery ถ้าไม่อยากโหลด scene โดยตรง")]
    public UnityEvent onComplete;

    [Header("Options")]
    [Tooltip("ถ้า true, จะให้ปุ่ม nextMapButton ปรากฏเมื่อถึงรูปสุดท้าย")]
    public bool showButtonOnLast = true;

    [Tooltip("ถ้า true, จะไม่ให้คลิกขวาจากรูปสุดท้าย (ปิดการทำงาน)")]
    public bool stopAtLast = true;

    private int currentIndex = 0;

    void Start()
    {
        if (displayImage == null)
        {
            Debug.LogWarning("ImageGalleryController: displayImage not assigned.");
        }

        if (nextMapButton != null)
        {
            // ตั้งค่าเริ่มต้น ปิดปุ่มถ้ากำหนดให้
            nextMapButton.gameObject.SetActive(false);
        }

        // แสดงรูปแรก (ถ้ามี)
        currentIndex = 0;
        UpdateDisplayedImage();
    }

    void Update()
    {
        // เช็คคลิกขวา (Right Mouse Button)
        if (Input.GetMouseButtonDown(0))
        {
            OnLeftClick();
        }

        // (Optional) keyboard shortcuts: ขวา -> next, ซ้าย -> previous
        // if (Input.GetKeyDown(KeyCode.RightArrow)) NextImage();
        // if (Input.GetKeyDown(KeyCode.LeftArrow)) PrevImage();
    }

    // ถูกเรียกเมื่อคลิกขวา
    public void OnLeftClick()
    {
        // ถ้าปุ่มโชว์และครอบ UI อยู่ และต้องการป้องกัน ให้ตรวจ logic เพิ่มได้ที่นี่
        NextImage();
    }

    // ไปรูปถัดไป
    public void NextImage()
    {
        if (images == null || images.Count == 0) return;

        // ถ้าถึงสุดท้ายแล้ว
        if (currentIndex >= images.Count - 1)
        {
            // ถ้าหยุดที่สุดท้าย ให้โชว์ปุ่มหรือเรียก onComplete
            if (showButtonOnLast && nextMapButton != null)
            {
                nextMapButton.gameObject.SetActive(true);
            }

            // เรียก onComplete ถ้าตั้งไว้
            if (onComplete != null) onComplete.Invoke();

            if (stopAtLast) return; // หยุดที่สุดท้าย
            // ถ้าไม่หยุด ให้วนกลับไปเริ่มต้น (ถ้าต้องการ) uncomment
            // currentIndex = 0;
            return;
        }

        currentIndex++;
        UpdateDisplayedImage();

        // ถ้าเพิ่งมาถึงสุดท้าย ให้เปิดปุ่มถ้าตั้งไว้
        if (currentIndex >= images.Count - 1)
        {
            if (showButtonOnLast && nextMapButton != null)
            {
                nextMapButton.gameObject.SetActive(true);
            }
        }
    }

    // (optional) ย้อนกลับไปรูปก่อนหน้า
    public void PrevImage()
    {
        if (images == null || images.Count == 0) return;
        if (currentIndex <= 0) return;
        currentIndex--;
        UpdateDisplayedImage();
        // ถ้าย้อนกลับมาก่อนสุดท้ายให้ซ่อนปุ่ม
        if (nextMapButton != null && nextMapButton.gameObject.activeSelf)
        {
            nextMapButton.gameObject.SetActive(false);
        }
    }

    // อัพเดต UI Image
    void UpdateDisplayedImage()
    {
        if (displayImage == null) return;
        if (images == null || images.Count == 0)
        {
            displayImage.sprite = null;
            displayImage.enabled = false;
            return;
        }
        var s = images[Mathf.Clamp(currentIndex, 0, images.Count - 1)];
        displayImage.enabled = true;
        displayImage.sprite = s;
        // ถ้าต้องการ fit แบบ Native size: uncomment ด้านล่าง
        // displayImage.SetNativeSize();
    }

    // เมธอดที่ผูกกับ nextMapButton
    public void NextMap()
    {
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            // โหลด scene แบบ synchronous
            SceneManager.LoadScene(nextSceneName);
            return;
        }

        // ถ้า nextSceneName ว่าง ให้เรียก onComplete (อีกครั้ง) หรือทำ logic อื่น
        if (onComplete != null) onComplete.Invoke();
    }
}