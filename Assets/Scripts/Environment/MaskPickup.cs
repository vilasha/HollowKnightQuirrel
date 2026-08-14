using UnityEngine;

/// <summary>
/// A full-heal Mask pickup (Docs/Plans/010_health-mask-system.md, Task 2.2) - restores
/// <see cref="PlayerHealth.CurrentQuarterMasks"/> to <see cref="PlayerHealth.MaxQuarterMasks"/> via
/// <see cref="PlayerHealth.FullHeal"/> (Decision 4). Entirely distinct from a Mask Shard, which
/// only raises the health ceiling (see <see cref="MaskShardPickup"/>).
///
/// Identical shape to <see cref="MaskShardPickup"/> (Decision 15's generalized isTrigger check
/// included) - see that class's doc comment for the shared rationale.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class MaskPickup : MonoBehaviour
{
    private void Awake()
    {
        var collider = GetComponent<Collider2D>();
        if (!collider.isTrigger)
        {
            Debug.LogError(
                $"MaskPickup '{name}' has a non-trigger Collider2D - this would physically block " +
                "the player. MaskPickup's collider must always be a trigger " +
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
    /// once a PlayerHealth is found, regardless of whether FullHeal() actually changed anything
    /// (e.g. already at full health) - the pickup is still "collected" from the player's perspective.
    /// </summary>
    public void HandleTriggerEnter(Collider2D other)
    {
        var playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.FullHeal();

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
