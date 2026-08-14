using UnityEngine;

/// <summary>
/// Static bench prop the player can sit on (Docs/Plans/008_bench-sit-mechanic.md, Task 5.1).
///
/// Proximity detection is a Bench-owned trigger collider using
/// <see cref="Collider2D.GetComponent{T}"/> against the entering collider (Decision 3) - not a new
/// physics layer + PlayerController-owned OverlapBox polling, which would require a
/// ProjectSettings/collision-matrix change for a check that doesn't need one. No serialized Player
/// reference (Decision 7): detecting via GetComponent&lt;PlayerController&gt;() on whatever collider
/// enters means a second Bench instance could be dropped into the scene later with zero code changes.
///
/// Named, accepted limitation (Decision 7): if two benches' proximity zones ever overlap
/// simultaneously and the player exits only one, that bench's OnTriggerExit2D would incorrectly clear
/// IsNearBench to false even while still inside the other bench's zone (a plain bool, not a
/// reference-counted overlap tracker). Not fixed here - out of scope for this plan's single-bench
/// use case.
///
/// Implements <see cref="IBenchSeat"/> (Docs/Plans/009_bench-visual-fixes.md, Decision 1) so
/// PlayerController can snap to this bench's sit anchor and hide/show its sprite for the duration of
/// a sit, without Quirrel.Player needing a reference to the concrete Bench type (which would create a
/// circular assembly reference, since Quirrel.Environment already references Quirrel.Player).
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Bench : MonoBehaviour, IBenchSeat
{
    [SerializeField] private Transform _sitAnchor;

    private SpriteRenderer _spriteRenderer;

    /// <summary>
    /// The world-space Transform a sitting character should snap to. Falls back to this Bench's own
    /// transform when no dedicated child anchor is assigned in the Inspector (Decision 6) - a future
    /// second Bench instance that forgets to wire an anchor still functions rather than throwing.
    /// </summary>
    public Transform SitAnchor => _sitAnchor != null ? _sitAnchor : transform;

    private void Awake()
    {
        EnsureCachedComponents();

        // Defensive - scene setup (Task 5.2) is responsible for the actual Inspector value, but this
        // catches a misconfigured prefab/instance loudly (a solid Bench would physically block the
        // player) rather than silently allowing it.
        var boxCollider = GetComponent<BoxCollider2D>();
        if (!boxCollider.isTrigger)
        {
            Debug.LogError(
                $"Bench '{name}' has a non-trigger BoxCollider2D - this would physically block the " +
                "player. Bench's collider must always be a trigger (Docs/Plans/008_bench-sit-mechanic.md, Decision 3).",
                this);
        }
    }

    /// <summary>
    /// Caches the SpriteRenderer reference. Called from Awake() for the normal runtime/Play-Mode path,
    /// and defensively re-called at the top of SetVisible() - Unity does NOT invoke Awake() on a plain
    /// MonoBehaviour (no [ExecuteAlways]) outside Play Mode, so EditMode tests that AddComponent this
    /// class and call SetVisible() directly would otherwise no-op silently on a null
    /// _spriteRenderer. Idempotent (no-ops once cached) so this costs nothing at runtime beyond the
    /// first call.
    /// </summary>
    private void EnsureCachedComponents()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }
    }

    /// <summary>Toggles this Bench's SpriteRenderer - hidden while a character is seated on it.</summary>
    public void SetVisible(bool visible)
    {
        EnsureCachedComponents();

        if (_spriteRenderer != null)
        {
            _spriteRenderer.enabled = visible;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleTriggerEnter(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        HandleTriggerExit(other);
    }

    /// <summary>
    /// Public, parameter-driven, EditMode-testable without a live physics scene - matches this
    /// codebase's established convention of thin Unity-message wrappers forwarding to testable public
    /// methods (e.g. PlayerController.ApplyHorizontalMovement/TryJump). No-ops without throwing if
    /// <paramref name="other"/> has no PlayerController component (e.g. a stray non-player trigger).
    /// </summary>
    public void HandleTriggerEnter(Collider2D other)
    {
        var playerController = other.GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.IsNearBench = true;
            playerController.NearBench = this;
        }
    }

    /// <summary>Mirror of <see cref="HandleTriggerEnter"/>, clearing IsNearBench instead.</summary>
    public void HandleTriggerExit(Collider2D other)
    {
        var playerController = other.GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.IsNearBench = false;

            // Reference-equality guarded (Decision 7): only clear NearBench when it currently points
            // at THIS bench - a reference already reassigned to a different, overlapping bench must
            // not be silently wiped by this bench's own exit.
            if (playerController.NearBench == (IBenchSeat)this)
            {
                playerController.NearBench = null;
            }
        }
    }
}
