using UnityEngine;

/// <summary>
/// A Mask Shard pickup (Docs/Plans/010_health-mask-system.md, Task 2.1) - 4 shards raise
/// <see cref="PlayerHealth.MaxMasks"/> by one via <see cref="PlayerHealth.AddMaskShard"/>
/// (Decision 3). Never heals current health directly.
///
/// Follows <see cref="Bench"/>'s established trigger-zone convention: OnTriggerEnter2D forwards
/// to a public, parameter-driven HandleTriggerEnter(Collider2D) for EditMode testability, and
/// GetComponent&lt;T&gt;() on the entering collider locates the relevant player-side component
/// rather than holding a serialized Player reference.
///
/// The defensive Awake() isTrigger check generalizes Bench.Awake()'s BoxCollider2D-specific
/// pattern to any Collider2D (Decision 15) - this plan doesn't fix the pickup's final collider
/// shape, and a misconfigured non-trigger collider would physically block the player exactly
/// like a misconfigured Bench would.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class MaskShardPickup : MonoBehaviour
{
    private void Awake()
    {
        var collider = GetComponent<Collider2D>();
        if (!collider.isTrigger)
        {
            Debug.LogError(
                $"MaskShardPickup '{name}' has a non-trigger Collider2D - this would physically " +
                "block the player. MaskShardPickup's collider must always be a trigger " +
                "(Docs/Plans/010_health-mask-system.md, Decision 15).",
                this);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleTriggerEnter(other);
    }

    /// <summary>
    /// Public, parameter-driven, EditMode-testable without a live physics scene - matches
    /// Bench.HandleTriggerEnter's established convention. No-ops (no throw, no destroy) if
    /// <paramref name="other"/> has no PlayerHealth component. Destroys this pickup unconditionally
    /// once a PlayerHealth is found, regardless of whether AddMaskShard() actually changed anything
    /// (e.g. already at the mask cap) - a shard is still "collected" from the player's perspective.
    /// </summary>
    public void HandleTriggerEnter(Collider2D other)
    {
        var playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.AddMaskShard();

            // Destroy() defers to end-of-frame in Play Mode (harmless there - real gameplay never
            // calls HandleTriggerEnter outside Play Mode) but never resolves synchronously when
            // called from an EditMode test with no running player loop to process the deferred
            // queue. DestroyImmediate outside Play Mode is the direct equivalent with zero behavior
            // difference for real gameplay, since Application.isPlaying is always true there.
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }
    }
}
