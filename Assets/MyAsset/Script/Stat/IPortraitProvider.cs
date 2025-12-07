// Assets/MyAsset/Script/System/IPortraitProvider.cs
using System;
using UnityEngine;

public interface IPortraitProvider
{
    // คืน Sprite (อาจเป็น null)
    Sprite GetPortrait();

    // Event ถ้ารูปเปลี่ยน (optional)
    event Action<Sprite> OnPortraitChanged;
}