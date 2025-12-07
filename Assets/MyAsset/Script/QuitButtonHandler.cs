using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class QuitButtonHandler : MonoBehaviour
{
    [Tooltip("Optional: หากต้องการให้เรียกใช้งานแบบซิงค์/แสดงข้อความก่อน ให้แก้โค้ดนี้")]
    public void QuitGame()
    {
        // บิลด์จริง (Windows/Mac/Linux/Android/iOS) จะปิดแอปเมื่อเรียก Application.Quit()
        Application.Quit();
        // ใน Editor เราจะหยุดโหมด Play แทน เพราะ Application.Quit() จะไม่มีผลใน Editor
        EditorApplication.isPlaying = false;
    }
}