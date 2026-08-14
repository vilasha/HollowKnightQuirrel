using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for PlayerHealth's real-physics hazard/invulnerability/rest coverage
/// (Docs/Plans/010_health-mask-system.md, Task 5.4).
///
/// RIG CHOICE: mirrors PlayerControllerPlayModeTests.cs's own established convention exactly - a
/// purpose-built minimal rig (TestGround + TestPlayer + TestHazard) built fresh in [UnitySetUp],
/// rather than loading Assets/Scenes/SampleScene.unity, for the same three reasons that file's class
/// doc comment already gives: isolation from unrelated future scene edits, avoiding an async
/// SceneManager.LoadScene dependency on exact in-scene GameObject names/hierarchy, and because the
/// rig's geometry is deliberately copied from the same live-verified SampleScene Player/Ground values
/// (gravityScale 3.823, BoxCollider2D size 1x1.31/offset 0,0.655 for the Player, Ground top surface at
/// world y=0) that file already uses - this suite exercises the exact same physics contract without a
/// scene dependency. TestPlayer deliberately has no Animator attached (same reasoning as
/// PlayerControllerPlayModeTests's rig) - PlayerHealth calls into PlayerController.Hurt()/Die(), both
/// of which already null-check their cached Animator reference, so this is safe.
///
/// MOVEMENT SIMULATION: the player is moved into/out of the hazard's trigger volume by writing
/// Rigidbody2D.position directly (matching PlayerController.TrySit's own existing production-code
/// precedent for teleporting a seated character to a bench's anchor, and this plan's own Task 3.2
/// description of how the hazard is verified) - not simulated held input. A direct rb.position write
/// updates the Rigidbody2D's own internal physics-world position immediately (unlike
/// transform.position, which is a dynamic Rigidbody2D's read-only mirror that only re-syncs after the
/// next FixedUpdate step - see this project's own rb.position/transform-sync Agent Learning), so the
/// very next physics step correctly evaluates the new overlap state against TestHazard's trigger
/// collider and fires OnTriggerEnter2D for real, exactly as it would for continuous player-driven
/// movement. Every teleport below is followed by one or more real `yield return new
/// WaitForFixedUpdate()` steps before any assertion, so the physics engine has actually had a chance to
/// process the new overlap - reading state immediately after a same-frame rb.position write, with zero
/// physics steps elapsed, would race Unity's own trigger-callback timing.
/// </summary>
public class PlayerHealthPlayModeTests
{
    private const int PlayerLayer = 8; // Task 3.1, matches TagManager.asset / SampleScene.unity, same convention as PlayerControllerPlayModeTests
    private const int GroundLayer = 9;

    // Player spawns here, well clear of TestHazard's collider bounds below - used both as the initial
    // spawn position and as the "outside the hazard" position for every move-out step.
    private const float OutsideX = 0f;

    // TestHazard's collider spans world x [4, 6] (see SetUp) - centered well inside that range so the
    // Player's own 1-unit-wide BoxCollider2D (half-width 0.5) is always fully overlapped regardless of
    // minor physics settling jitter.
    private const float InsideHazardX = 5f;

    // Mirrors PlayerHealth's own default serialized _damageInvulnerabilityDuration (1.0f) - not read
    // via reflection/a shared constant (that field is a per-instance serialized tunable, not a `const`
    // like PlayerHealth.QuartersPerMask/MaxMasksCap), so this is hardcoded here the same way
    // PlayerControllerPlayModeTests hardcodes PlayerController's own 0.3s hit-stun duration as a local
    // "0.35f" (duration + margin) constant rather than reflecting it.
    private const float InvulnerabilityWindowSeconds = 1.0f;

    private GameObject _groundObject;
    private GameObject _playerObject;
    private GameObject _hazardObject;
    private PlayerController _controller;
    private PlayerHealth _health;
    private Rigidbody2D _rigidbody;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // Ground: BoxCollider2D on the Ground layer, top surface at world y=0, no Rigidbody2D (a
        // static collider) - identical geometry/precedent to PlayerControllerPlayModeTests.cs's rig.
        _groundObject = new GameObject("TestGround");
        _groundObject.layer = GroundLayer;
        _groundObject.transform.position = new Vector3(0f, -0.5f, 0f);
        BoxCollider2D groundCollider = _groundObject.AddComponent<BoxCollider2D>();
        groundCollider.size = new Vector2(30f, 1f);

        // Player: mirrors SampleScene's/PlayerControllerPlayModeTests's Player geometry exactly.
        _playerObject = new GameObject("TestPlayer");
        _playerObject.layer = PlayerLayer;
        _playerObject.transform.position = new Vector3(OutsideX, 0f, 0f);
        _playerObject.AddComponent<SpriteRenderer>();

        _rigidbody = _playerObject.AddComponent<Rigidbody2D>();
        _rigidbody.gravityScale = 3.823f;
        _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;

        BoxCollider2D playerCollider = _playerObject.AddComponent<BoxCollider2D>();
        playerCollider.size = new Vector2(1f, 1.31f);
        playerCollider.offset = new Vector2(0f, 0.655f);

        _controller = _playerObject.AddComponent<PlayerController>();
        _health = _playerObject.AddComponent<PlayerHealth>(); // [RequireComponent(typeof(PlayerController))] already satisfied above

        // Hazard: a persistent trigger wall, sized generously around the Player's own collider height
        // range (world y [0, 1.31] once grounded at transform.position.y == 0) with margin, centered at
        // InsideHazardX - mirrors this plan's Task 3.2 real scene placement, at unit-test scale. No
        // Rigidbody2D needed (Physics2D only requires ONE Rigidbody2D among the two overlapping
        // colliders for trigger events to fire, satisfied by the Player's own Rigidbody2D above) - same
        // precedent as Bench's own trigger zone and TestGround's static collider above.
        _hazardObject = new GameObject("TestHazard");
        _hazardObject.transform.position = new Vector3(InsideHazardX, 0.65f, 0f);
        BoxCollider2D hazardCollider = _hazardObject.AddComponent<BoxCollider2D>();
        hazardCollider.isTrigger = true;
        hazardCollider.size = new Vector2(2f, 3f); // world bounds: x [4,6], y [-0.85, 2.15]
        _hazardObject.AddComponent<TestDamageHazard>();

        // Let Awake()/OnEnable() (PlayerController resolves its Ground layer mask; PlayerHealth
        // subscribes Rested += FullHeal) and one physics step settle before any test starts driving
        // movement - same margin as PlayerControllerPlayModeTests.SetUp().
        yield return null;
        yield return new WaitForFixedUpdate();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_playerObject != null)
        {
            Object.Destroy(_playerObject);
        }

        if (_hazardObject != null)
        {
            Object.Destroy(_hazardObject);
        }

        if (_groundObject != null)
        {
            Object.Destroy(_groundObject);
        }

        yield return null;
    }

    /// <summary>
    /// Teleports the Player's Rigidbody2D to the given world x (y fixed at 0, the grounded resting
    /// height for this rig's geometry - see class doc comment). Writes .position, not
    /// transform.position, for the reason given in the class doc comment's MOVEMENT SIMULATION section.
    /// </summary>
    private void MovePlayerToX(float x)
    {
        _rigidbody.position = new Vector2(x, 0f);
    }

    /// <summary>
    /// Advances a bounded number of real FixedUpdate physics steps - enough for Physics2D to evaluate
    /// a just-written rb.position against every trigger collider in the scene and fire any resulting
    /// OnTriggerEnter2D/OnTriggerExit2D callbacks. Not a poll-until-condition loop (there is no single
    /// boolean this suite can safely condition on before the very state the test is trying to observe),
    /// but a small fixed count is more than sufficient at the default 0.02s fixedDeltaTime - well under
    /// the 1.0s invulnerability window this suite deliberately keeps its rapid-re-entry case inside.
    /// </summary>
    private static IEnumerator SettlePhysics(int fixedFrames = 5)
    {
        for (int i = 0; i < fixedFrames; i++)
        {
            yield return new WaitForFixedUpdate();
        }
    }

    // ---------------------------------------------------------------------
    // 1. Single hazard entry deals exactly 1 quarter-mask
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator HazardEntry_UnderRealPhysics_ReducesCurrentQuarterMasksByExactlyOne()
    {
        int startingQuarterMasks = _health.CurrentQuarterMasks;
        Assert.AreEqual(16, startingQuarterMasks, "Sanity check: fresh PlayerHealth should start at 4 masks (16 quarter-masks).");

        MovePlayerToX(InsideHazardX);
        yield return SettlePhysics();

        Assert.AreEqual(startingQuarterMasks - 1, _health.CurrentQuarterMasks,
            "Entering TestDamageHazard's trigger volume once under real, stepped physics should reduce " +
            "CurrentQuarterMasks by exactly 1 quarter-mask (Docs/Plans/010_health-mask-system.md, Task 2.3).");
    }

    // ---------------------------------------------------------------------
    // 2. Standing still inside the hazard does not drain beyond the single entry hit
    //
    // Named regression case (explicitly called out in Task 5.4's acceptance criteria): TestDamageHazard
    // fires on OnTriggerEnter2D only, never OnTriggerStay2D (Decision 11), so this should hold true
    // regardless of whether PlayerHealth's invulnerability window is even working - it is still worth
    // asserting explicitly, both as a real regression check on Enter-only firing and as a baseline this
    // suite's own Test 3 below can be read against.
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator HazardEntry_HoldingStationaryInsideForSeveralSeconds_DoesNotDrainBeyondSingleEntryHit()
    {
        MovePlayerToX(InsideHazardX);
        yield return SettlePhysics();

        int quarterMasksAfterEntry = _health.CurrentQuarterMasks;
        Assert.AreEqual(15, quarterMasksAfterEntry, "Sanity check: the single entry hit should already have applied before the hold begins.");

        // Several real seconds of accumulated Time.deltaTime, deliberately spanning well past the 1.0s
        // invulnerability window - Enter-only firing (Decision 11) must prevent further drain here
        // independent of that window ever expiring.
        float elapsed = 0f;
        const float holdDuration = 3f;
        while (elapsed < holdDuration)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }

        Assert.AreEqual(quarterMasksAfterEntry, _health.CurrentQuarterMasks,
            "Standing stationary inside a persistent hazard's trigger volume must not drain health beyond " +
            "the single entry hit - OnTriggerEnter2D does not re-fire for a stationary overlapping collider " +
            "(Docs/Plans/010_health-mask-system.md, Decision 11).");
    }

    // ---------------------------------------------------------------------
    // 3. The actual proof of the invulnerability window
    //
    // Distinct from Test 2 above by design: a stationary hold can never fail this suite whether or not
    // PlayerHealth's invulnerability window is implemented correctly, because Enter-only firing alone
    // already prevents in-place drain (Decision 11). This test isolates the window's own effect by
    // exiting and re-entering the trigger - a scenario Enter-only firing does NOT protect against on its
    // own - within, then after, the 1.0s window.
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator HazardReentry_WithinInvulnerabilityWindow_DealsNoAdditionalDamage_ButAfterWindowElapses_DealsAnotherChip()
    {
        // First entry - the baseline hit, also starts the invulnerability window.
        MovePlayerToX(InsideHazardX);
        yield return SettlePhysics();

        int quarterMasksAfterFirstEntry = _health.CurrentQuarterMasks;
        Assert.AreEqual(15, quarterMasksAfterFirstEntry, "Sanity check: first entry should deal exactly 1 quarter-mask.");
        Assert.IsTrue(_health.IsInvulnerable, "The invulnerability window should be active immediately after a non-fatal hit.");

        // Step back out, then immediately back in - well under the 1.0s window (SettlePhysics's default
        // 5 fixed frames is ~0.1s at the default 0.02s fixedDeltaTime, comfortably inside the window even
        // doubled across the out-then-in round trip below).
        MovePlayerToX(OutsideX);
        yield return SettlePhysics();

        MovePlayerToX(InsideHazardX);
        yield return SettlePhysics();

        Assert.IsTrue(_health.IsInvulnerable, "Sanity check: the window should still be active this soon after the first hit.");
        Assert.AreEqual(quarterMasksAfterFirstEntry, _health.CurrentQuarterMasks,
            "Re-entering the hazard's trigger volume within the 1.0s invulnerability window must deal ZERO " +
            "additional damage - this is the actual proof the window works, not merely that Enter-only " +
            "firing happens to prevent drain (Docs/Plans/010_health-mask-system.md, Task 5.4).");

        // Step back out and wait past the window - same real-time-wait convention
        // PlayerControllerPlayModeTests.Hurt_ReturnsToNormal_AfterStunWindowElapses_NotFrozen already
        // uses for its own FixedUpdate-driven timer (duration + margin via WaitForSeconds), applied here
        // to PlayerHealth.AdvanceInvulnerabilityTimer's own FixedUpdate-driven window instead.
        MovePlayerToX(OutsideX);
        yield return new WaitForSeconds(InvulnerabilityWindowSeconds + 0.1f);

        Assert.IsFalse(_health.IsInvulnerable, "The invulnerability window should have naturally elapsed via real FixedUpdate ticks by now.");

        // Re-enter once the window has genuinely expired - a further chip of damage should be taken,
        // confirming the window correctly expires rather than latching permanently.
        MovePlayerToX(InsideHazardX);
        yield return SettlePhysics();

        Assert.AreEqual(quarterMasksAfterFirstEntry - 1, _health.CurrentQuarterMasks,
            "Re-entering the hazard's trigger volume once the invulnerability window has elapsed should " +
            "deal a further chip of damage, confirming the window expires rather than latching permanently.");
    }

    // ---------------------------------------------------------------------
    // 4. Resting on a bench (TrySit success) fully heals via the live Rested event
    //
    // Deliberately drives this through PlayerController.TrySit(true) itself - not a direct
    // PlayerHealth.FullHeal() call - so this test actually exercises the live Rested event
    // subscription (PlayerHealth.OnEnable: `_playerController.Rested += FullHeal`) under real Play
    // Mode timing, rather than merely proving FullHeal() works in isolation (already covered by the
    // EditMode suite).
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator TrySit_WhileDamaged_FullyHealsViaLiveRestedEvent()
    {
        bool damaged = _health.ApplyDamage(9); // arbitrary non-fatal, non-full amount - just needs to be less than max
        Assert.IsTrue(damaged, "Sanity check: the setup damage call should have succeeded.");
        Assert.Less(_health.CurrentQuarterMasks, _health.MaxQuarterMasks, "Sanity check: player should be damaged before sitting.");

        // ApplyDamage's non-fatal path also calls PlayerController.Hurt(), which starts a 0.3s hit-stun
        // window during which IsFullyCommitted is true - and TrySit requires !IsFullyCommitted (via
        // IsIdleAndGrounded()). Wait past that window first (same duration+margin convention as
        // PlayerControllerPlayModeTests.Hurt_ReturnsToNormal_AfterStunWindowElapses_NotFrozen), or
        // TrySit below would spuriously fail for a reason unrelated to what this test is proving.
        yield return new WaitForSeconds(0.35f);
        Assert.IsFalse(_controller.IsHurtStunned, "Sanity check: hit-stun should have elapsed before attempting to sit.");

        bool sat = _controller.TrySit(true); // isNearBench = true, matching Bench.HandleTriggerEnter's real call shape
        Assert.IsTrue(sat, "TrySit should succeed while idle, grounded, and reported near a bench.");

        // Give the live Rested event / PlayerHealth.FullHeal a real frame to be observed, matching this
        // suite's real-Play-Mode-timing convention rather than asserting in the exact same synchronous
        // call stack as TrySit itself.
        yield return null;
        yield return new WaitForFixedUpdate();

        Assert.AreEqual(_health.MaxQuarterMasks, _health.CurrentQuarterMasks,
            "Successfully sitting while damaged should trigger a full heal via the live PlayerController.Rested " +
            "-> PlayerHealth.FullHeal event path (Docs/Plans/010_health-mask-system.md, Decision 9).");
        Assert.IsTrue(_controller.IsSitting, "Sanity check: the character should actually be seated after a successful TrySit.");
    }
}
