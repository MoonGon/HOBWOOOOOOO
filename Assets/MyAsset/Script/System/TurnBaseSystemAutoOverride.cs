using System;
using UnityEngine;

/// <summary>
/// Safe override helper: watches TurnBaseSystem.CurrentBattlerObject and if the current battler
/// has AutoAttackMarker it will invoke PartyAutoAttack.OnBattlerTurnStart regardless of whether
/// TurnBaseSystem considers it player-controlled.
/// - Drop-in: attach to any GameObject in scene (eg. same object as TurnBaseSystem).
/// - Non-invasive: does not modify TurnBaseSystem source.
/// </summary>
[DefaultExecutionOrder(200)]
public class TurnBaseSystemAutoOverride : MonoBehaviour
{
    GameObject _lastHandledBattler = null;
    float _lastHandledTime = 0f;
    // Optional cooldown to avoid double-invoking same battler within short time
    public float minReinvokeSeconds = 0.5f;

    void Update()
    {
        var tbs = TurnBaseSystem.Instance;
        if (tbs == null) return;

        // Try to read CurrentBattlerObject property / field via common names
        GameObject current = null;
        try
        {
            // Prefer property CurrentBattlerObject if present
            var t = tbs.GetType();
            var prop = t.GetProperty("CurrentBattlerObject");
            if (prop != null) current = prop.GetValue(tbs) as GameObject;
            else
            {
                var field = t.GetField("currentBattlerObject") ?? t.GetField("CurrentBattlerObject");
                if (field != null) current = field.GetValue(tbs) as GameObject;
            }
        }
        catch { /* ignore reflection issues */ }

        if (current == null) { _lastHandledBattler = null; return; }

        if (current == _lastHandledBattler && (Time.time - _lastHandledTime) < minReinvokeSeconds) return;

        // If battler has AutoAttackMarker, ensure PartyAutoAttack is called for it.
        if (current.GetComponent<AutoAttackMarker>() != null)
        {
            try
            {
                var pa = FindObjectOfType<PartyAutoAttack>();
                if (pa != null)
                {
                    // If TurnBaseSystem already invoked PartyAutoAttack, this may double-invoke.
                    // We rely on minimalReinvokeSeconds and _lastHandledBattler guard to avoid duplicates.
                    pa.OnBattlerTurnStart(current);
                    Debug.Log($"[TurnBaseSystemAutoOverride] Invoked PartyAutoAttack for {current.name} (AutoAttackMarker present).");
                    _lastHandledBattler = current;
                    _lastHandledTime = Time.time;
                    return;
                }
                else
                {
                    Debug.LogWarning("[TurnBaseSystemAutoOverride] No PartyAutoAttack instance found in scene.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TurnBaseSystemAutoOverride] Exception invoking PartyAutoAttack: " + ex);
            }
        }

        // Otherwise, no action here. Reset last if different object.
        _lastHandledBattler = current;
    }
}