using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ChildCollisionResponder : MonoBehaviour
{
    [Header("Who can collect me?")]
    public LayerMask collectFromLayers;     // set to "Cart" in Inspector

    [Header("What happens when collected?")]
    public bool destroyOnCollect = false;   // false -> SetActive(false)
    public float respawnAfter = -1f;        // <0 = never respawn

    bool collected;
    float respawnAt;

    void Reset()
    {
        // Make sure apples are triggers and default to Cart as collector
        var col = GetComponent<Collider>();
        col.isTrigger = true;
        collectFromLayers = LayerMask.GetMask("Cart");
    }

    void OnTriggerEnter(Collider other)  => TryCollect(other.gameObject);
    void OnCollisionEnter(Collision c)   => TryCollect(c.gameObject);

    void Update()
    {
        if (!collected || respawnAfter < 0f) return;
        if (Time.time >= respawnAt)
        {
            gameObject.SetActive(true);
            collected = false;
        }
    }

    void TryCollect(GameObject other)
    {
        if (collected) return;

        // Layer filter
        if ((collectFromLayers.value & (1 << other.layer)) == 0) return;

        collected = true;

        if (destroyOnCollect)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
            if (respawnAfter >= 0f)
                respawnAt = Time.time + respawnAfter;
        }

        // TODO (optional): send score/event here
        // Example: FindObjectOfType<GameManager>()?.OnAppleCollected();
    }
}
