using UnityEngine;

/// <summary>
/// TEMPORARY QA/DEV-ONLY AID - NOT SHIPPABLE CONTENT.
///
/// A persistent, re-triggerable chip-damage hazard wall (Docs/Plans/010_health-mask-system.md,
/// Task 2.3), added solely to prove Quirrel's Mask health model supports sub-Mask granularity
/// damage. This component must be removed (along with its scene placement, Task 3.2) before any
/// real level content ships - see Docs/Plans/010_health-mask-system.md for full context and
/// removal reference.
///
/// Fires on OnTriggerEnter2D ONLY, never OnTriggerStay2D (Decision 11) - this is load-bearing:
/// Unity does not re-fire OnTriggerEnter2D for a stationary overlapping collider, so Enter-only
/// firing alone already prevents a player standing inside this wall from draining health far
/// faster than one chip per touch, independent of PlayerHealth's own invulnerability window.
///
/// Deals exactly <see cref="DamageAmountQuarterMasks"/> (1) quarter-mask of damage - deliberately
/// NOT <see cref="PlayerHealth.QuartersPerMask"/> (4), which would be a full-mask hit and defeat
/// this hazard's stated chip-damage purpose.
///
/// Does not destroy or disable itself after firing - it is a persistent wall, re-triggerable once
/// the player exits and re-enters (subject to PlayerHealth's invulnerability window).
///
/// Deliberately invisible in Play Mode - no SpriteRenderer, no [ART] task. OnDrawGizmos gives
/// Editor-only placement convenience with zero runtime visual footprint.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TestDamageHazard : MonoBehaviour
{
    /// <summary>
    /// Chip-damage amount in quarter-masks for this QA hazard - deliberately not
    /// <see cref="PlayerHealth.QuartersPerMask"/>, which would deal a full-mask hit instead of the
    /// sub-Mask chip damage this hazard exists to prove.
    /// </summary>
    private const int DamageAmountQuarterMasks = 1;

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleTriggerEnter(other);
    }

    /// <summary>
    /// Public, parameter-driven, EditMode-testable without a live physics scene - matches
    /// Bench.HandleTriggerEnter's established convention. No-ops (no throw) if
    /// <paramref name="other"/> has no PlayerHealth component.
    /// </summary>
    public void HandleTriggerEnter(Collider2D other)
    {
        var playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.ApplyDamage(DamageAmountQuarterMasks);
        }
    }

    /// <summary>
    /// Editor-only placement convenience - draws a wire cube matching the attached collider's
    /// bounds. Zero runtime visual footprint (Gizmos never render in a built Play Mode view).
    /// </summary>
    private void OnDrawGizmos()
    {
        var collider = GetComponent<Collider2D>();
        if (collider == null)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
    }
}
