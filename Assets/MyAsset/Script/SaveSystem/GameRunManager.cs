using System;
using UnityEngine;

/// <summary>
/// GameRunManager: convenience singleton to manage run lifecycle and hook common events.
/// - Call StartRun() when a fresh run begins (new campaign)
/// - Call SaveProgress() when important changes happen (allocation, levelup, manual save)
/// - Call EndRunOnDeath() or EndRunOnVictory() to terminate the run (clears active save)
/// - On startup you can call LoadExistingRun() to restore if a run is active.
/// </summary>
public class GameRunManager : MonoBehaviour
{
    public static GameRunManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        // optional: load if there's an active run
        if (SaveSystem.HasActiveRun())
        {
            Debug.Log("[GameRunManager] Active run save found — loading.");
            SaveSystem.LoadProgress();
            // raise event or update UI if needed
        }
    }

    public void StartRun()
    {
        SaveSystem.StartNewRun();
        // initial save right away
        SaveSystem.SaveProgress();
        Debug.Log("[GameRunManager] Run started.");
    }

    public void SaveProgress()
    {
        SaveSystem.SaveProgress();
    }

    public void EndRunOnDeath()
    {
        SaveSystem.EndRun(false);
        Debug.Log("[GameRunManager] Run ended (player death).");
    }

    public void EndRunOnVictory()
    {
        SaveSystem.EndRun(true);
        Debug.Log("[GameRunManager] Run ended (victory completed).");
    }
}