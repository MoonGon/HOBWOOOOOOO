using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queue-based PartyAutoAttack (improved duplicate protection)
/// - Enqueues battlers and processes them one at a time
/// - Prevents duplicate processing with s_processingBattlers
/// - Ensures each battler only performs one attack per processing
/// - Returns attacker to start position, then ends turn once
/// </summary>
public class PartyAutoAttack : MonoBehaviour
{
    public bool allowAnyWithEquipment = false;
    public float minReinvokeSeconds = 0.5f;
    public float returnDuration = 0.25f;
    public bool teleportIfBlocked = true;

    // shared state
    static readonly Dictionary<int, int> s_lastHandledTurnId = new Dictionary<int, int>();
    static readonly Dictionary<int, float> s_lastHandledTime = new Dictionary<int, float>();
    static readonly HashSet<int> s_processingBattlers = new HashSet<int>(); // currently being processed or locked
    static Queue<GameObject> s_queue = new Queue<GameObject>();
    static bool s_processing = false;
    static PartyAutoAttack s_singletonInstance = null;

    void Awake()
    {
        if (s_singletonInstance == null) s_singletonInstance = this;
        else
        {
            enabled = false;
            return;
        }
    }

    void OnDestroy()
    {
        if (s_singletonInstance == this) s_singletonInstance = null;
    }

    // Called by Turn system -- enqueue quick and return
    public void OnBattlerTurnStart(GameObject battler)
    {
        if (battler == null) return;
        int id = battler.GetInstanceID();

        // If currently processing this battler, ignore
        if (s_processingBattlers.Contains(id))
        {
            return;
        }

        int turnId = GetTurnIdFromTurnBaseSystem();
        if (turnId != -1)
        {
            if (s_lastHandledTurnId.TryGetValue(id, out int prev) && prev == turnId)
            {
                return;
            }
            s_lastHandledTurnId[id] = turnId; // reserve for this turn
        }
        else
        {
            if (s_lastHandledTime.TryGetValue(id, out float lastT) && (Time.time - lastT) < minReinvokeSeconds)
            {
                return;
            }
            s_lastHandledTime[id] = Time.time;
        }

        bool hasMarker = battler.GetComponent<AutoAttackMarker>() != null;
        bool isMarkedAllyAuto = false;
        try { isMarkedAllyAuto = battler.CompareTag("AllyAuto"); } catch { isMarkedAllyAuto = false; }

        if (!hasMarker && !isMarkedAllyAuto && !allowAnyWithEquipment)
        {
            return;
        }

        // enqueue and start processor if needed
        s_queue.Enqueue(battler);
        if (!s_processing && s_singletonInstance != null)
        {
            s_singletonInstance.StartCoroutine(s_singletonInstance.ProcessQueue());
        }
    }

    IEnumerator ProcessQueue()
    {
        s_processing = true;
        while (s_queue.Count > 0)
        {
            var battler = s_queue.Dequeue();
            if (battler == null) continue;
            if (!IsValidGameObject(battler)) continue;

            int id = battler.GetInstanceID();
            if (s_processingBattlers.Contains(id))
            {
                continue;
            }

            // lock this battler to prevent duplicates
            s_processingBattlers.Add(id);
            yield return StartCoroutine(HandleOneBattlerCoroutine(battler));
            // unlock
            s_processingBattlers.Remove(id);
            yield return null;
        }
        s_processing = false;
    }

    bool IsValidGameObject(GameObject g)
    {
        try { return g != null; }
        catch { return false; }
    }

    IEnumerator HandleOneBattlerCoroutine(GameObject battler)
    {
        if (battler == null) yield break;

        // eligibility re-check
        bool hasMarker = battler.GetComponent<AutoAttackMarker>() != null;
        bool isMarkedAllyAuto = false;
        try { isMarkedAllyAuto = battler.CompareTag("AllyAuto"); } catch { isMarkedAllyAuto = false; }
        if (!hasMarker && !isMarkedAllyAuto && !allowAnyWithEquipment)
        {
            yield break;
        }

        var equip = battler.GetComponent<CharacterEquipment>();
        GameObject target = FindMonsterTarget();
        if (target == null)
        {
            yield break;
        }

        Vector3 startPos = Vector3.zero;
        Transform tr = battler.transform;
        try { startPos = tr.position; } catch { startPos = Vector3.zero; }

        bool attackInvoked = false;
        bool attackCompletedFlag = false;

        // Helper to mark attack invoked safely
        Action markInvoked = () => { attackInvoked = true; };

        // 1) Try GoAttck animated approach first (if exists)
        var goAI = battler.GetComponent<GoAttck>();
        if (goAI != null)
        {
            try
            {
                // Provide callback that will attempt to perform the actual attack (callback runs on main thread)
                goAI.AttackMonster(target, () =>
                {
                    // if attack already invoked by fallback, skip
                    if (attackInvoked)
                    {
                        attackCompletedFlag = true;
                        return;
                    }

                    // Try async-capable attack (with callback)
                    bool asyncStarted = TryDoNormalAttackWithCallback(equip, battler, target, () =>
                    {
                        // async callback invoked by attack implementation
                        attackCompletedFlag = true;
                    });

                    if (asyncStarted)
                    {
                        markInvoked();
                        // Wait for async callback in coroutine below
                        return;
                    }

                    // If no async found, try immediate once
                    bool ok = TryDoNormalAttackWithImmediate(equip, battler, target);
                    if (ok) markInvoked();
                    attackCompletedFlag = true;
                });
            }
            catch { }
            // Wait for completion flag (set by callback or async callback) or timeout
            float waitStart = Time.time;
            float maxWait = 10f; // safety timeout
            while (!attackCompletedFlag && (Time.time - waitStart) < maxWait)
            {
                yield return null;
            }
            // if still not completed, attempt direct attack
            if (!completedCheck(ref attackInvoked, ref attackCompletedFlag, equip, battler, target))
            {
                // nothing more to do
            }
        }
        else
        {
            // no goAI: try immediate attack paths
            attackInvoked = TryDoNormalAttackWithImmediate(equip, battler, target);
            // if reflection attempted an async with callback, it should signal EndTurn itself; but here we wait a frame to allow callback to run
            yield return null;
        }

        // Ensure attacker returns to start position smoothly (or instantly)
        yield return StartCoroutine(ReturnToPositionCoroutine(tr, startPos));

        // Clear highlight selection (so the target highlight disappears after attack)
        try { HighlightOnSelect.ClearSelection(); } catch { }

        // End turn once
        EndTurnSafely();

        yield break;
    }

    // small helper extracted to keep flow clear
    bool completedCheck(ref bool attackInvoked, ref bool attackCompletedFlag, CharacterEquipment equip, GameObject battler, GameObject target)
    {
        if (!attackCompletedFlag)
        {
            bool ok = TryDoNormalAttackWithImmediate(equip, battler, target);
            if (ok) attackInvoked = true;
            return ok;
        }
        return true;
    }

    // Immediate (sync) attack attempts (DO NOT call EndTurn here)
    bool TryDoNormalAttackWithImmediate(CharacterEquipment equip, GameObject battler, GameObject target)
    {
        if (equip != null)
        {
            try
            {
                equip.DoNormalAttack(target);
                return true;
            }
            catch { }
        }

        // reflection fallback (1-param methods)
        var comps = battler.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            var methods = c.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            foreach (var m in methods)
            {
                if (m.Name != "DoNormalAttack" && m.Name != "AttackMonster" && m.Name != "Attack") continue;
                var ps = m.GetParameters();
                try
                {
                    if (ps.Length == 1 && typeof(GameObject).IsAssignableFrom(ps[0].ParameterType))
                    {
                        m.Invoke(c, new object[] { target });
                        return true;
                    }
                }
                catch { }
            }
        }

        var wc = battler.GetComponent<WeaponController>();
        if (wc != null)
        {
            try
            {
                var mi = wc.GetType().GetMethod("Attack");
                if (mi != null) { mi.Invoke(wc, new object[] { target }); return true; }
            }
            catch { }
        }

        return false;
    }

    // Try attack methods that accept a callback; returns true if started async attack (onComplete will be called)
    bool TryDoNormalAttackWithCallback(CharacterEquipment equip, GameObject battler, GameObject target, Action onComplete)
    {
        if (equip != null)
        {
            var t = equip.GetType();
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (m.Name != "DoNormalAttack") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && typeof(GameObject).IsAssignableFrom(ps[0].ParameterType) &&
                    (ps[1].ParameterType == typeof(Action) || ps[1].ParameterType == typeof(System.Action)))
                {
                    try
                    {
                        m.Invoke(equip, new object[] { target, onComplete ?? (Action)(() => { }) });
                        return true;
                    }
                    catch { }
                }
            }
        }

        var comps = battler.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            var methods = c.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            foreach (var m in methods)
            {
                if (m.Name != "DoNormalAttack" && m.Name != "AttackMonster" && m.Name != "Attack") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && typeof(GameObject).IsAssignableFrom(ps[0].ParameterType))
                {
                    try
                    {
                        var dummy = onComplete ?? (Action)(() => { });
                        m.Invoke(c, new object[] { target, dummy });
                        return true;
                    }
                    catch { }
                }
            }
        }

        return false;
    }

    IEnumerator ReturnToPositionCoroutine(Transform tr, Vector3 startPos)
    {
        yield return null;
        if (tr == null) yield break;

        if (returnDuration <= 0f)
        {
            try { tr.position = startPos; } catch { }
            yield break;
        }

        Vector3 from = tr.position;
        if (Vector3.Distance(from, startPos) <= 0.01f) yield break;

        float t = 0f;
        while (t < returnDuration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(t / returnDuration);
            try { tr.position = Vector3.Lerp(from, startPos, alpha); } catch { break; }
            yield return null;
        }

        try { tr.position = startPos; } catch { }
    }

    void EndTurnSafely()
    {
        try { TurnBaseSystem.Instance?.EndTurn(); return; } catch { }
        try { TurnManager.Instance?.EndTurn(); return; } catch { }
    }

    bool SafeCompareTag(GameObject go, string tag)
    {
        try { return go.CompareTag(tag); } catch { return false; }
    }

    GameObject FindMonsterTarget()
    {
        var tbs = TurnBaseSystem.Instance;
        if (tbs != null && tbs.selectedMonster != null) return tbs.selectedMonster;

        var tm = TurnManager.Instance;
        if (tm != null && tm.selectedMonster != null) return tm.selectedMonster;

        var all = FindObjectsOfType<GameObject>();
        foreach (var g in all)
        {
            if (g == null) continue;
            if (g.GetComponent<IMonsterStat>() != null) return g;
        }
        foreach (var g in all)
        {
            if (g == null) continue;
            if (SafeCompareTag(g, "Enemy") || SafeCompareTag(g, "Monster")) return g;
        }
        return null;
    }

    int GetTurnIdFromTurnBaseSystem()
    {
        var tbs = TurnBaseSystem.Instance;
        if (tbs == null) return -1;
        var t = tbs.GetType();

        string[] propNames = new string[] {
            "turnNumber", "TurnNumber", "roundNumber", "RoundNumber",
            "currentTurn", "CurrentTurn", "currentRound", "CurrentRound",
            "turnIndex", "TurnIndex", "turnCounter", "TurnCounter"
        };

        foreach (var pn in propNames)
        {
            try
            {
                var prop = t.GetProperty(pn, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop != null && prop.PropertyType == typeof(int))
                {
                    var v = prop.GetValue(tbs);
                    return v != null ? (int)v : -1;
                }
            }
            catch { }
            try
            {
                var field = t.GetField(pn, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(int))
                {
                    var v = field.GetValue(tbs);
                    return v != null ? (int)v : -1;
                }
            }
            catch { }
        }
        return -1;
    }
}