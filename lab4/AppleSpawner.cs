using UnityEngine;
using System.Collections.Generic;

public class AppleSpawner : MonoBehaviour
{
    [Header("What to spawn")]
    public GameObject applePrefab;          // assign Apple.prefab
    public Transform parentForApples;       // assign GroundApples

    [Header("How many")]
    public int targetCount = 12;            // total active apples to keep

    [Header("Where (rect area in world)")]
    public Vector3 areaCenter = new Vector3(0, 0, 0);
    public Vector2 areaSizeXZ = new Vector2(20f, 20f);   // width x depth
    public float spawnY = 0;                          // a bit above ground

    [Header("Spacing / tries")]
    public float minSeparation = 1.0f;      // don’t stack apples
    public int maxTriesPerApple = 20;

    readonly List<Vector3> _spawned = new List<Vector3>();

    void Start()
    {
        if (applePrefab == null) { Debug.LogError("[AppleSpawner] applePrefab missing."); enabled = false; return; }
        if (parentForApples == null) { parentForApples = transform; Debug.LogWarning("[AppleSpawner] parentForApples not set — using self."); }
        Debug.Log("[AppleSpawner] Start(): Beginning spawn test...");
        TopUp();
    }

    void Update()
    {
        // Keep topping up if apples get collected/destroyed
        TopUp();
    }

    void TopUp()
    {
        // Count current active apples under parent
        int active = 0;
        for (int i = 0; i < parentForApples.childCount; i++)
        {
            var c = parentForApples.GetChild(i).gameObject;
            if (c.activeInHierarchy) active++;
        }

        int need = targetCount - active;
        for (int i = 0; i < need; i++)
        {
            Vector3 pos;
            if (!FindFreeSpot(out pos)) break;

            var go = Instantiate(applePrefab, pos, Quaternion.Euler(0, Random.Range(0, 360f), 0), parentForApples);

            // make sure runtime spawns keep your intended layer
            go.layer = LayerMask.NameToLayer("Apples");
            Debug.Log($"[AppleSpawner] Spawned apple #{i + 1} at {pos}");

        }
    }

    bool FindFreeSpot(out Vector3 pos)
    {
        for (int t = 0; t < maxTriesPerApple; t++)
        {
            float rx = Random.Range(-areaSizeXZ.x * 0.5f, areaSizeXZ.x * 0.5f);
            float rz = Random.Range(-areaSizeXZ.y * 0.5f, areaSizeXZ.y * 0.5f);
            Vector3 p = new Vector3(areaCenter.x + rx, spawnY, areaCenter.z + rz);

            // simple distance check against existing active apples under parent
            if (IsFarEnough(p))
            {
                pos = p;
                _spawned.Add(p);
                return true;
            }
        }
        pos = default;
        return false;
    }

    bool IsFarEnough(Vector3 p)
    {
        // check live scene children
        for (int i = 0; i < parentForApples.childCount; i++)
        {
            var c = parentForApples.GetChild(i);
            if (!c.gameObject.activeInHierarchy) continue;
            if (Vector3.SqrMagnitude(c.position - p) < minSeparation * minSeparation) return false;
        }
        // also check against newly planned spots this frame
        foreach (var s in _spawned)
            if ((s - p).sqrMagnitude < minSeparation * minSeparation) return false;

        return true;
    }

    // visualize spawn area in editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        var c = areaCenter;
        Gizmos.DrawWireCube(new Vector3(c.x, spawnY, c.z), new Vector3(areaSizeXZ.x, 0.1f, areaSizeXZ.y));
    }
}
