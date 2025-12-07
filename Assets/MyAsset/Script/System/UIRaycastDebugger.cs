using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIRaycastDebugger : MonoBehaviour
{
    public GraphicRaycaster[] raycasters; // leave empty to auto-find
    void Start()
    {
        if (raycasters == null || raycasters.Length == 0)
            raycasters = FindObjectsOfType<GraphicRaycaster>();
        Debug.Log($"UIRaycastDebugger: found {raycasters.Length} GraphicRaycasters. EventSystem present: {EventSystem.current != null}");
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            var pos = Input.mousePosition;
            if (EventSystem.current == null) { Debug.Log("No EventSystem in scene"); return; }
            var ped = new PointerEventData(EventSystem.current) { position = pos };
            foreach (var gr in raycasters)
            {
                var results = new List<RaycastResult>();
                gr.Raycast(ped, results);
                Debug.Log($"[UIRaycastDebugger] Raycaster:{gr.gameObject.name} results:{results.Count}");
                foreach (var r in results)
                {
                    var go = r.gameObject;
                    var cg = go.GetComponent<CanvasGroup>();
                    var canvas = go.GetComponentInParent<Canvas>();
                    Debug.Log($"  hit: {go.name} active={go.activeInHierarchy} depth={r.depth} sortOrder={(canvas != null ? canvas.sortingOrder : -999)} blocksRaycasts={(cg != null ? cg.blocksRaycasts : false)} interactable={(cg != null ? cg.interactable : false)}");
                }
            }
        }
    }
}