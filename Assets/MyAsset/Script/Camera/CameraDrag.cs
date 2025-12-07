using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// CameraDrag (extended, LateUpdate-safe)
/// - Click & hold (or single touch) + drag to pan camera.
/// - Options:
///     requireClickOnLayer: only start dragging when initial press hits an object in clickableLayers (Physics2D/3D)
///     allowDragWhenOverUI: if false, UI will block drag start
///     supportTouch: enable one-finger touch panning
///     useInertia: camera continues moving briefly after release
///     applyInLateUpdate: apply computed camera position in LateUpdate (avoid being overridden)
///     targetTransform: if set, move this transform instead of cam.transform (useful for VirtualCamera)
/// - Attach to Camera (uses assigned cam or Camera.main)
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraDrag : MonoBehaviour
{
    [Header("Camera")]
    public Camera cam;
    [Tooltip("If set, move this transform instead of cam.transform (useful for Cinemachine VirtualCamera or camera parent).")]
    public Transform targetTransform;

    [Header("Movement")]
    [Tooltip("Drag speed multiplier")]
    public float dragSpeed = 1f;
    public bool verticalOnly = true;

    [Header("Limits")]
    public bool clampY = true;
    public float minY = -5f;
    public float maxY = 5f;
    public bool clampX = false;
    public float minX = -10f;
    public float maxX = 10f;

    [Header("Start Conditions")]
    [Tooltip("If true, start drag only when initial press hits an object in clickableLayers (2D/3D).")]
    public bool requireClickOnLayer = false;
    public LayerMask clickableLayers = ~0; // default All

    [Tooltip("If false, pointer over UI will block starting a drag.")]
    public bool allowDragWhenOverUI = false;

    [Header("Touch / Input")]
    [Tooltip("Support single-finger touch panning")]
    public bool supportTouch = true;

    [Header("Inertia")]
    public bool useInertia = false;
    [Tooltip("How quickly inertia damps (larger = faster stop)")]
    public float inertiaDamping = 5f;
    [Tooltip("Max velocity allowed from drag (world units/sec)")]
    public float maxInertiaVelocity = 20f;

    [Header("Apply / Debug")]
    [Tooltip("If true, apply computed camera position in LateUpdate (safer when other systems adjust camera).")]
    public bool applyInLateUpdate = true;
    [Tooltip("Enable debug logs for troubleshooting")]
    public bool debugLogs = false;

    // internal
    bool dragging = false;
    Vector3 lastPointerScreenPos;
    int activePointerId = -999;

    Vector3 inertiaVelocity = Vector3.zero;

    // pending pos (for LateUpdate application)
    Vector3 pendingPos;
    bool hasPendingPos = false;

    // UI raycast helpers
    GraphicRaycaster uiRaycaster;
    PointerEventData pEventData;
    List<RaycastResult> uiRaycastResults = new List<RaycastResult>();

    void Awake()
    {
        // prefer explicitly assigned cam, then camera on same GameObject, then Camera.main
        if (cam == null) cam = GetComponent<Camera>() ?? Camera.main;
        if (cam == null) Debug.LogWarning("[CameraDrag] No camera assigned and Camera.main is null.");

        // if targetTransform not set, default to cam.transform (will be resolved in Start)
        if (targetTransform == null && cam != null) targetTransform = cam.transform;

        // try to get a GraphicRaycaster in scene (for more precise UI hit tests)
        var canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            uiRaycaster = canvas.GetComponent<GraphicRaycaster>();
        }
    }

    void Start()
    {
        // ensure we have a transform to move
        if (targetTransform == null && cam != null)
            targetTransform = cam.transform;

        if (debugLogs)
        {
            Debug.Log($"[CameraDrag] Awake done. cam={(cam != null ? cam.name : "null")} targetTransform={(targetTransform != null ? targetTransform.name : "null")}");
        }
    }

    void Update()
    {
        if (cam == null) return;

        // Touch handling (priority)
        if (supportTouch && Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                if (CanStartDragAtScreenPos(t.position))
                {
                    StartDrag(t.position, pointerId: t.fingerId);
                    if (debugLogs) Debug.Log($"[CameraDrag] Touch began at {t.position}");
                }
            }
            else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
            {
                if (dragging && activePointerId == t.fingerId)
                    ContinueDrag(t.position);
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (dragging && activePointerId == t.fingerId)
                    EndDrag();
            }

            // do not process mouse when touch active
            return;
        }

        // Mouse handling
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mouse = Input.mousePosition;
            if (CanStartDragAtScreenPos(mouse))
            {
                StartDrag(mouse);
                if (debugLogs) Debug.Log($"[CameraDrag] MouseDown at {mouse}");
            }
            else
            {
                if (debugLogs) Debug.Log($"[CameraDrag] MouseDown blocked at {mouse} (UI or layer)");
            }
        }

        if (dragging && Input.GetMouseButton(0))
        {
            ContinueDrag(Input.mousePosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            if (dragging) EndDrag();
        }

        // inertia update (when not dragging)
        if (!dragging && useInertia && inertiaVelocity.sqrMagnitude > 0.0001f)
        {
            // apply inertia immediately or queue for LateUpdate
            Vector3 delta = inertiaVelocity * Time.deltaTime;
            Vector3 newPos = GetCurrentTransformPosition() + delta;

            if (verticalOnly)
            {
                newPos.x = GetCurrentTransformPosition().x;
                newPos.z = GetCurrentTransformPosition().z;
            }
            else
            {
                newPos.z = GetCurrentTransformPosition().z;
            }

            if (clampX) newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
            if (clampY) newPos.y = Mathf.Clamp(newPos.y, minY, maxY);

            if (applyInLateUpdate)
            {
                pendingPos = newPos;
                hasPendingPos = true;
            }
            else
            {
                ApplyPositionImmediate(newPos);
            }

            // damp velocity
            inertiaVelocity = Vector3.Lerp(inertiaVelocity, Vector3.zero, Mathf.Clamp01(inertiaDamping * Time.deltaTime));
            if (inertiaVelocity.magnitude < 0.01f) inertiaVelocity = Vector3.zero;
        }
    }

    void LateUpdate()
    {
        if (applyInLateUpdate && hasPendingPos)
        {
            ApplyPositionImmediate(pendingPos);
            hasPendingPos = false;
            if (debugLogs) Debug.Log($"[CameraDrag] Applied pendingPos in LateUpdate: {pendingPos}");
        }
    }

    Vector3 GetCurrentTransformPosition()
    {
        if (targetTransform != null) return targetTransform.position;
        if (cam != null) return cam.transform.position;
        return Vector3.zero;
    }

    void ApplyPositionImmediate(Vector3 pos)
    {
        if (targetTransform != null)
        {
            targetTransform.position = pos;
        }
        else if (cam != null)
        {
            cam.transform.position = pos;
        }
    }

    bool CanStartDragAtScreenPos(Vector2 screenPos)
    {
        // UI blocking
        if (!allowDragWhenOverUI && IsPointerOverUI(screenPos))
            return false;

        if (!requireClickOnLayer) return true;

        // require click on layer: check 2D then 3D
        // Choose an appropriate Z for ScreenToWorldPoint:
        float depth = cam.orthographic ? Mathf.Abs(cam.transform.position.z) : (cam.nearClipPlane + 1f);
        Vector3 worldPoint = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
        // 2D check
        Collider2D hit2d = Physics2D.OverlapPoint(worldPoint, clickableLayers);
        if (hit2d != null) return true;

        // 3D check
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit3d, Mathf.Infinity, clickableLayers))
            return true;

        return false;
    }

    void StartDrag(Vector2 screenPos, int pointerId = -999)
    {
        dragging = true;
        activePointerId = pointerId;
        lastPointerScreenPos = screenPos;
        // stop inertia when starting drag
        inertiaVelocity = Vector3.zero;
        // clear pending
        hasPendingPos = false;

        if (debugLogs) Debug.Log($"[CameraDrag] StartDrag lastPointer={lastPointerScreenPos}");
    }

    void ContinueDrag(Vector2 screenPos)
    {
        Vector3 cur = new Vector3(screenPos.x, screenPos.y, 0f);
        float z = cam.orthographic ? Mathf.Abs(cam.transform.position.z) : Mathf.Abs(cam.transform.position.z);
        Vector3 lastWorld = cam.ScreenToWorldPoint(new Vector3(lastPointerScreenPos.x, lastPointerScreenPos.y, z));
        Vector3 curWorld = cam.ScreenToWorldPoint(new Vector3(cur.x, cur.y, z));
        Vector3 worldDelta = curWorld - lastWorld;

        // move opposite to pointer drag
        Vector3 move = -worldDelta * dragSpeed;

        if (verticalOnly)
        {
            move.x = 0f;
            move.z = 0f;
        }
        else
        {
            move.z = 0f;
        }

        Vector3 newPos = GetCurrentTransformPosition() + move;

        if (clampX) newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
        if (clampY) newPos.y = Mathf.Clamp(newPos.y, minY, maxY);

        if (applyInLateUpdate)
        {
            pendingPos = newPos;
            hasPendingPos = true;
        }
        else
        {
            ApplyPositionImmediate(newPos);
        }

        // compute velocity for inertia (world units per second)
        if (useInertia)
        {
            Vector3 vel = move / Mathf.Max(0.0001f, Time.deltaTime);
            if (vel.magnitude > maxInertiaVelocity) vel = vel.normalized * maxInertiaVelocity;
            inertiaVelocity = vel;
        }

        lastPointerScreenPos = screenPos;

        if (debugLogs) Debug.Log($"[CameraDrag] ContinueDrag move={move} newPos={newPos}");
    }

    void EndDrag()
    {
        dragging = false;
        activePointerId = -999;
        if (debugLogs) Debug.Log($"[CameraDrag] EndDrag inertiaVelocity={inertiaVelocity}");
        // inertiaVelocity remains (if useInertia true) so camera will continue moving and damp
    }

    bool IsPointerOverUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;

        // If no GraphicRaycaster found, fallback to simple EventSystem check
        if (uiRaycaster == null)
        {
            return EventSystem.current.IsPointerOverGameObject();
        }

        pEventData = new PointerEventData(EventSystem.current) { position = screenPos };
        uiRaycastResults.Clear();
        uiRaycaster.Raycast(pEventData, uiRaycastResults);
        return uiRaycastResults.Count > 0;
    }
}