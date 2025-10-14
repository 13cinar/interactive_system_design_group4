using System;
using System.Collections.Generic;
using UnityEngine;
using static OVRInput;
using TMPro;

// --------- payloads for sending anchors to Python ----------
[Serializable] public class AnchorVec3 { public float x, y, z; }
[Serializable] public class AnchorQuat { public float x, y, z, w; }
[Serializable]
public class AnchorMsg
{
    public int id;
    public AnchorVec3 position;
    public AnchorQuat rotation;
}
[Serializable]
public class AnchorMsgRoot
{
    public string type = "anchors";
    public List<AnchorMsg> anchors = new List<AnchorMsg>();
}

public class AnchorMeta : MonoBehaviour
{
    public int markerId;   // which ArUco/marker id this anchor represents
}

public static class SpatialAnchorRegistry
{
    // Other scripts (like TCPCompleted) will look anchors up here by id
    public static readonly Dictionary<int, Transform> anchorsById = new Dictionary<int, Transform>();
}

// -----------------------------------------------------------

public class SpatialAnchorsCompleted : MonoBehaviour
{
    // Which controller places anchors (set in Inspector)
    [SerializeField] private Controller controller = Controller.RTouch;

    // Prefab with a small Canvas under it that shows ID/position (your existing prefab)
    public GameObject anchorPrefab;

    // OPTIONAL: parent all anchors here so you can nudge the whole group
    public Transform anchorsParent;

    // The ID that will be assigned to the NEXT anchor you place
    [Header("Placement")]
    public int anchorArucoId = 0;

    // Auto-increment ID after each placement (0,1,2,3...)
    public bool autoIncrementIds = true;

    // OPTIONAL HUD label to show the current id (drag a TextMeshProUGUI here)
    public TextMeshProUGUI currentIdLabel;

    // Track exactly one anchor Transform per ArUco id
    private readonly Dictionary<int, Transform> anchorsById = new Dictionary<int, Transform>();

    void Start()
    {
        RefreshIdLabel();
    }

    void Update()
    {
        // Place or update anchor for current id with Index Trigger
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller))
            CreateOrUpdateSpatialAnchor();

        // OPTIONAL: A/X button sends all anchors to Python (or hook a UI Button to SendAllAnchorsToPython)
        if (OVRInput.GetDown(OVRInput.Button.One, controller))
            SendAllAnchorsToPython();

        // OPTIONAL: change current id with thumbstick left/right
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickLeft, controller)) { anchorArucoId--; RefreshIdLabel(); }
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickRight, controller)) { anchorArucoId++; RefreshIdLabel(); }
    }

    /// <summary>
    /// Creates a new anchor for the current id, or moves the existing one if that id already exists.
    /// </summary>
    public void CreateOrUpdateSpatialAnchor()
    {
        int id = anchorArucoId;

        Vector3 placePos = OVRInput.GetLocalControllerPosition(controller);
        Quaternion placeRot = OVRInput.GetLocalControllerRotation(controller);

        if (anchorsById.TryGetValue(id, out Transform existing))
        {
            existing.SetPositionAndRotation(placePos, placeRot);
            SpatialAnchorRegistry.anchorsById[id] = existing;
            UpdateAnchorLabels(existing, id);

            Debug.Log($"[Anchors] Registered id {id} -> {existing.name} @ {existing.position}");
            Debug.Log($"[Anchors] Known IDs: {string.Join(",", SpatialAnchorRegistry.anchorsById.Keys)}");

            Debug.Log($"Updated anchor for id {id}");
        }
        else
        {
            GameObject anchor = Instantiate(anchorPrefab, placePos, placeRot);

            // tag the instance with its marker id
            var meta = anchor.GetComponent<AnchorMeta>();
            if (meta == null) meta = anchor.AddComponent<AnchorMeta>();
            meta.markerId = id;

            // register in the global map (create or update)
            SpatialAnchorRegistry.anchorsById[id] = anchor.transform;

            Debug.Log($"[Anchors] Registered id {id} -> {anchor.name} @ {anchor.transform.position}");
            Debug.Log($"[Anchors] Known IDs: {string.Join(",", SpatialAnchorRegistry.anchorsById.Keys)}");

            if (anchorsParent) anchor.transform.SetParent(anchorsParent, true);

            // Make it a Quest spatial anchor so it can persist
            if (anchor.GetComponent<OVRSpatialAnchor>() == null)
                anchor.AddComponent<OVRSpatialAnchor>();

            // Show ID/position on the prefab canvas (if present)
            UpdateAnchorLabels(anchor.transform, id);

            anchorsById[id] = anchor.transform;
            Debug.Log($"Created anchor for id {id}");
        }

        if (autoIncrementIds) { anchorArucoId++; RefreshIdLabel(); }
    }

    /// <summary>
    /// Sends ALL currently placed anchors (id + world pose) to Python via TCPCompleted.
    /// Hook this to a UI Button OnClick for an easy Quest workflow.
    /// </summary>
    public void SendAllAnchorsToPython()
    {
        if (anchorsById.Count == 0)
        {
            Debug.Log("No anchors to send.");
            return;
        }

        var root = new AnchorMsgRoot();
        foreach (var kv in anchorsById)
        {
            int id = kv.Key;
            Transform t = kv.Value;
            Vector3 p = t.position;
            Quaternion r = t.rotation;

            root.anchors.Add(new AnchorMsg
            {
                id = id,
                position = new AnchorVec3 { x = p.x, y = p.y, z = p.z },
                rotation = new AnchorQuat { x = r.x, y = r.y, z = r.z, w = r.w }
            });
        }

        string json = JsonUtility.ToJson(root, false);
        TCPCompleted.Instance?.SendJson(json);
        Debug.Log($"Sent {anchorsById.Count} anchors to Python.");
    }

    // ---------------- helpers ----------------

    private void UpdateAnchorLabels(Transform anchorT, int id)
    {
        var canvas = anchorT.GetComponentInChildren<Canvas>();
        if (canvas != null)
        {
            try
            {
                var idText = canvas.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
                if (idText) idText.text = "ID: " + id.ToString();

                Vector3 labelPos = anchorT.GetChild(0).GetChild(0).position;
                var positionText = canvas.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
                if (positionText) positionText.text = labelPos.ToString();
            }
            catch { /* ignore label errors if prefab structure differs */ }
        }
    }

    private void RefreshIdLabel()
    {
        if (currentIdLabel != null)
            currentIdLabel.text = $"Current ID: {anchorArucoId}";
    }
}
