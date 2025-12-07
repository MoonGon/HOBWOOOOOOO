using System;
using UnityEngine;

/// <summary>
/// EncounterManager: convenience singleton to check/mark completed encounters.
/// - Use EncounterIdentity on maps/encounters to provide a unique id.
/// - Call CanEnter(encounterId) before allowing the player to enter the battle.
/// - Call MarkEncounterCompleted(encounterId) when player wins the battle.
/// </summary>
public class EncounterManager : MonoBehaviour
{
    public static EncounterManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
    }

    public bool CanEnter(string encounterId)
    {
        if (string.IsNullOrEmpty(encounterId)) return true; // no id -> allow
        return !SaveSystem.IsEncounterCompleted(encounterId);
    }

    public void MarkEncounterCompleted(string encounterId)
    {
        if (string.IsNullOrEmpty(encounterId)) return;
        if (SaveSystem.IsEncounterCompleted(encounterId)) return;
        SaveSystem.AddCompletedEncounter(encounterId);
        Debug.Log($"[EncounterManager] Encounter marked completed: {encounterId}");
    }
}