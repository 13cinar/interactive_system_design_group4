using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ChildCollisionResponder : MonoBehaviour
{
    private DisappearManager manager;
    private bool hasDisappeared = false;

    void Awake()
    {
        manager = GetComponentInParent<DisappearManager>();
        if (manager == null)
        {
            Debug.LogError("[ChildCollisionResponder] No DisappearManager found in parent hierarchy.");
        }
    }

    // For trigger-based setups
    void OnTriggerEnter(Collider other)
    {
        TryDisappear(other.gameObject);
    }

    // For non-trigger physics collisions
    void OnCollisionEnter(Collision collision)
    {
        TryDisappear(collision.gameObject);
    }

    private void TryDisappear(GameObject other)
    {
        if (manager == null || hasDisappeared) return;
        if (!manager.IsInCollisionMask(other)) return;

        hasDisappeared = true;
        manager.ChildHit(gameObject);
    }

    // Called by manager when reactivating this child
    public void ResetDisappearState()
    {
        hasDisappeared = false;
    }
}
