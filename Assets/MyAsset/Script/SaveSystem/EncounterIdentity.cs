using UnityEngine;

/// <summary>
/// Simple component to give a unique id to an encounter/map.
/// - Set the id in the Inspector (e.g. "map_forest_01", "boss_final").
/// - Use this id when checking entry or marking completion.
/// </summary>
public class EncounterIdentity : MonoBehaviour
{
    [Tooltip("Unique id for this encounter/map. Must be consistent across runs.")]
    public string encounterId;
}