using System.Collections.Generic;
using UnityEngine;

public class DisappearManager : MonoBehaviour
{
    [Header("Which layers should trigger disappearance?")]
    public LayerMask collisionMask;

    // Queue of children in the order they disappeared (FIFO)
    private readonly Queue<GameObject> disappearedQueue = new Queue<GameObject>();

    // Called by children when they collide with a valid layer
    public void ChildHit(GameObject child)
    {
        //Debug.LogWarning("[DisappearManager] ChildHit.");
        if (!child.activeSelf) return;

        // Hide child and remember order
        child.SetActive(false);
        disappearedQueue.Enqueue(child);

        if (disappearedQueue.Count > 2)
        {
            var toReappear = disappearedQueue.Dequeue(); // earliest disappeared
            // Safety: if it somehow was destroyed, skip
            if (toReappear != null)
            {
                toReappear.SetActive(true);

                // Reset the child's "hasDisappeared" so it can disappear again later
                var responder = toReappear.GetComponent<ChildCollisionResponder>();
                if (responder != null) responder.ResetDisappearState();
            }
        }
    }

    // Utility used by children to check if the other collider is in allowed layers
    public bool IsInCollisionMask(GameObject other)
    {
        int mask = 1 << other.layer;
        return (collisionMask.value & mask) != 0;
    }
}
