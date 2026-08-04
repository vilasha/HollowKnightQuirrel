using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for PlayerController (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Tasks 3.2, 3.3, and 3.4b).
/// </summary>
public class PlayerControllerTests
{
    private GameObject _playerObject;
    private PlayerController _playerController;
    private Rigidbody2D _rigidbody;

    [TearDown]
    public void TearDown()
    {
        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }
    }

    private void CreatePlayer()
    {
        _playerObject = new GameObject("TestPlayer");
        _rigidbody = _playerObject.AddComponent<Rigidbody2D>();
        _playerObject.AddComponent<SpriteRenderer>();
        _playerController = _playerObject.AddComponent<PlayerController>();
    }

    /// <summary>
    /// Load-bearing test per plan section 1.5 ("Velocity-write discipline"): a regression here
    /// would silently break jumping once Task 3.3 adds vertical velocity to this same Rigidbody2D.
    /// </summary>
    [Test]
    public void ApplyHorizontalMovement_PreservesExistingVerticalVelocity()
    {
        CreatePlayer();

        const float preExistingVerticalVelocity = 15f; // simulates a mid-air jump velocity already present on .y
        _rigidbody.velocity = new Vector2(0f, preExistingVerticalVelocity);

        _playerController.ApplyHorizontalMovement(1f); // simulate one frame of D input

        Assert.AreEqual(preExistingVerticalVelocity, _rigidbody.velocity.y, 0.0001f,
            "Horizontal movement must not stomp the existing vertical (.y) velocity component.");
    }

    [Test]
    public void ApplyHorizontalMovement_SetsExpectedHorizontalSpeed()
    {
        CreatePlayer();

        _playerController.ApplyHorizontalMovement(1f);
        Assert.AreEqual(4.5f, _rigidbody.velocity.x, 0.0001f, "Rightward input should apply +4.5 units/sec (plan section 1.5).");

        _playerController.ApplyHorizontalMovement(-1f);
        Assert.AreEqual(-4.5f, _rigidbody.velocity.x, 0.0001f, "Leftward input should apply -4.5 units/sec (plan section 1.5).");

        _playerController.ApplyHorizontalMovement(0f);
        Assert.AreEqual(0f, _rigidbody.velocity.x, 0.0001f, "No input should apply zero horizontal velocity.");
    }

    [Test]
    public void ApplyHorizontalMovement_DoesNotAffectXWhenNoInput_ButLeavesYAlone()
    {
        CreatePlayer();
        _rigidbody.velocity = new Vector2(2f, -3f);

        _playerController.ApplyHorizontalMovement(0f);

        Assert.AreEqual(0f, _rigidbody.velocity.x, 0.0001f);
        Assert.AreEqual(-3f, _rigidbody.velocity.y, 0.0001f);
    }

    // -------------------------------------------------------------------
    // Task 3.3: jump physics, re-trigger guard, delayed-impulse cancellation
    // (Docs/Plans/002_quirrel-sprite-animation-player-control.md, plan sections 1.4/1.5/1.9)
    // -------------------------------------------------------------------

    /// <summary>
    /// Load-bearing test per plan section 1.5's velocity-write discipline, applied to the vertical
    /// impulse (the mirror of ApplyHorizontalMovement_PreservesExistingVerticalVelocity above): the
    /// impulse write must never stomp a nonzero .x set by concurrent horizontal input/movement.
    /// </summary>
    [Test]
    public void JumpImpulse_PreservesExistingHorizontalVelocity()
    {
        CreatePlayer();

        const float preExistingHorizontalVelocity = 3f; // simulates mid-air horizontal control already present on .x
        _rigidbody.velocity = new Vector2(preExistingHorizontalVelocity, 0f);

        _playerController.TryJump(true);
        _playerController.AdvanceJumpTimer(0.08f); // elapse exactly the anticipation window

        Assert.AreEqual(preExistingHorizontalVelocity, _rigidbody.velocity.x, 0.0001f,
            "The vertical jump impulse must not stomp the existing horizontal (.x) velocity component.");
        Assert.AreEqual(15f, _rigidbody.velocity.y, 0.0001f,
            "The vertical impulse should apply v0 = 15 u/s (plan section 1.5) once the anticipation window elapses.");
    }

    /// <summary>
    /// Plan section 1.4's jump re-trigger guard: a second Space press within the 0.08s anticipation
    /// window must be ignored (independent of IsGrounded), resulting in exactly one JumpTrigger fire
    /// and exactly one vertical impulse application, not two.
    /// </summary>
    [Test]
    public void TryJump_DoublePressWithinAnticipationWindow_FiresOnlyOnce()
    {
        CreatePlayer();

        bool firstPressFired = _playerController.TryJump(true);
        Assert.IsTrue(firstPressFired, "First Space press while grounded and not already mid-jump should fire the jump.");
        Assert.IsTrue(_playerController.IsJumpInProgress);

        // Simulate a second Space press arriving before the 0.08s anticipation timer has elapsed.
        // Grounded is still true here (the character hasn't left the ground yet), which is exactly
        // why _jumpInProgress - not IsGrounded - must be the guard.
        bool secondPressFired = _playerController.TryJump(true);
        Assert.IsFalse(secondPressFired, "A second press within the anticipation window must be ignored, independent of IsGrounded.");

        _playerController.AdvanceJumpTimer(0.08f); // elapse exactly the anticipation window
        Assert.AreEqual(15f, _rigidbody.velocity.y, 0.0001f, "Exactly one vertical impulse should have applied.");

        // Perturb velocity.y and advance the timer again: since the window already resolved, this
        // must be a no-op - proof the impulse cannot be re-applied/stacked from the ignored second press.
        _rigidbody.velocity = new Vector2(_rigidbody.velocity.x, 5f);
        _playerController.AdvanceJumpTimer(0.08f);
        Assert.AreEqual(5f, _rigidbody.velocity.y, 0.0001f, "The already-resolved timer must not re-apply the impulse.");
    }

    /// <summary>
    /// Plan sections 1.4/1.9's delayed-impulse cancellation: if IsDead becomes true before the 0.08s
    /// timer elapses (simulating Die() interrupting JumpAnticipation - Task 3.4a wires the real Die()),
    /// no vertical impulse may be applied.
    /// </summary>
    [Test]
    public void AdvanceJumpTimer_CancelsImpulse_WhenDeadBeforeTimerElapses()
    {
        CreatePlayer();

        // A distinct, non-default pre-existing .y so the assertion below proves "unchanged from
        // whatever gravity had already produced", not a coincidental match against Rigidbody2D's
        // zero default.
        const float preExistingVerticalVelocity = -2f;
        _rigidbody.velocity = new Vector2(0f, preExistingVerticalVelocity);

        _playerController.TryJump(true);
        _playerController.IsDead = true; // simulates Die() interrupting the anticipation window before Task 3.4a exists

        _playerController.AdvanceJumpTimer(0.08f); // elapse exactly the anticipation window

        Assert.AreEqual(preExistingVerticalVelocity, _rigidbody.velocity.y, 0.0001f,
            "No vertical impulse should be applied once IsDead is true when the anticipation timer elapses - " +
            "velocity.y must reflect only gravity's prior effect, not the 15 u/s jump impulse.");
    }

    // -------------------------------------------------------------------
    // Task 3.4b: combat/reaction layer test suite for Task 3.4a
    // (Docs/Plans/002_quirrel-sprite-animation-player-control.md, plan sections 1.8/1.9/1.10)
    // -------------------------------------------------------------------

    /// <summary>
    /// Test-only helper that force-sets the private-set DefendHeld property via reflection.
    /// DefendHeld has no public setter - it's recomputed every Update() from live
    /// Input.GetKey(KeyCode.K) reads (plan section 1.7), and EditMode tests cannot simulate real
    /// keyboard input - so this is the only way to exercise the DefendHeld branch of full-commit
    /// gating (Task 3.4b's acceptance criterion) without adding a test-only public setter to
    /// PlayerController.cs, which is out of scope for this task (owned by Task 3.4a).
    /// </summary>
    private static void ForceSetDefendHeld(PlayerController controller, bool value)
    {
        PropertyInfo property = typeof(PlayerController).GetProperty(
            nameof(PlayerController.DefendHeld),
            BindingFlags.Public | BindingFlags.Instance);
        property.SetValue(controller, value);
    }

    [Test]
    public void Die_CalledTwice_DoesNotDoubleFireOrThrow()
    {
        CreatePlayer();

        Assert.DoesNotThrow(() => _playerController.Die());
        Assert.IsTrue(_playerController.IsDead);

        Assert.DoesNotThrow(() => _playerController.Die());
        Assert.IsTrue(_playerController.IsDead, "IsDead must remain true after a second Die() call.");
    }

    [Test]
    public void Hurt_AfterDie_IsANoOp()
    {
        CreatePlayer();

        _playerController.Die();
        Assert.IsTrue(_playerController.IsDead);

        _playerController.Hurt();

        Assert.IsFalse(_playerController.IsHurtStunned,
            "Hurt() must no-op once IsDead is true (plan section 1.7's HurtTrigger gating) - a dead character cannot enter hit-stun.");
    }

    /// <summary>
    /// Plan section 1.10: a re-entrant Hurt() call mid-stun RESTARTS the 0.3s window rather than
    /// being ignored. Proven here via elapsed-time math, not just a boolean snapshot: at 0.4s total
    /// elapsed since the FIRST Hurt() call (which would have exceeded the original 0.3s window on its
    /// own), stun is still active because only 0.2s has passed since the RESTART.
    /// </summary>
    [Test]
    public void Hurt_SecondCallMidStun_RestartsWindow()
    {
        CreatePlayer();

        _playerController.Hurt();
        Assert.IsTrue(_playerController.IsHurtStunned);

        _playerController.AdvanceHurtStunTimer(0.2f); // 0.2s elapsed since first Hurt() - within the original window
        Assert.IsTrue(_playerController.IsHurtStunned, "Should still be stunned at 0.2s into the original 0.3s window.");

        _playerController.Hurt(); // restart - a fresh 0.3s window starts now

        _playerController.AdvanceHurtStunTimer(0.2f); // 0.4s total since the FIRST Hurt(), but only 0.2s since the restart
        Assert.IsTrue(_playerController.IsHurtStunned,
            "0.4s total elapsed exceeds the ORIGINAL window's 0.3s duration - if this is still true, it proves the " +
            "second Hurt() call actually restarted the timer rather than being ignored or merely resetting a display value.");

        _playerController.AdvanceHurtStunTimer(0.11f); // exhausts the remaining ~0.1s of the restarted window
        Assert.IsFalse(_playerController.IsHurtStunned, "The restarted window should have fully elapsed by now.");
    }

    /// <summary>
    /// Plan section 1.10: HurtRecoveryTrigger fires exactly once per completed stun EPISODE, not once
    /// per Hurt() call. No live Animator is attached in this test fixture (CreatePlayer() does not add
    /// one), so the trigger itself cannot be observed directly - _animator is null and
    /// AdvanceHurtStunTimer's SetTrigger call safely no-ops. Tested behaviorally instead via the
    /// IsHurtStunned false-&gt;true-&gt;(stays true through a restart)-&gt;false transition: exactly one
    /// false-&gt;true-&gt;false cycle across two Hurt() calls proves the recovery-resolution branch inside
    /// AdvanceHurtStunTimer executed exactly once for the whole episode, not once per Hurt() call.
    /// </summary>
    [Test]
    public void HurtRecoveryTrigger_FiresOncePerEpisode_EvenWithMultipleHurtCallsMidStun()
    {
        CreatePlayer();

        Assert.IsFalse(_playerController.IsHurtStunned, "Should start un-stunned.");

        _playerController.Hurt(); // episode starts
        Assert.IsTrue(_playerController.IsHurtStunned);

        _playerController.AdvanceHurtStunTimer(0.1f);
        Assert.IsTrue(_playerController.IsHurtStunned, "Mid-window - the episode has not resolved yet.");

        _playerController.Hurt(); // re-entrant call restarts the SAME episode - not a second episode
        Assert.IsTrue(_playerController.IsHurtStunned);

        _playerController.AdvanceHurtStunTimer(0.31f); // exhaust the restarted window
        Assert.IsFalse(_playerController.IsHurtStunned, "Exactly one false transition - the episode resolved once.");

        // Calling again post-resolution must be a safe no-op (early-return branch), not a second
        // "recovery" firing.
        Assert.DoesNotThrow(() => _playerController.AdvanceHurtStunTimer(0.1f));
        Assert.IsFalse(_playerController.IsHurtStunned);
    }

    [Test]
    public void FullCommit_WhileAttacking_HorizontalVelocityStaysFrozenAtZero()
    {
        CreatePlayer();

        bool attackStarted = _playerController.TryAttack(true);
        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAttacking);

        _playerController.ApplyHorizontalMovement(1f); // simulate held D

        Assert.AreEqual(0f, _rigidbody.velocity.x, 0.0001f,
            "Movement must be fully frozen while IsAttacking is true (plan section 1.8's full-commit gating).");
    }

    /// <summary>
    /// Same full-commit requirement as above, exercised for DefendHeld via <see cref="ForceSetDefendHeld"/>
    /// (see that helper's doc comment for why reflection is used here instead of driving it through
    /// Update()/real keyboard input).
    /// </summary>
    [Test]
    public void FullCommit_WhileDefendHeld_HorizontalVelocityStaysFrozenAtZero()
    {
        CreatePlayer();

        ForceSetDefendHeld(_playerController, true);
        Assert.IsTrue(_playerController.DefendHeld);

        _playerController.ApplyHorizontalMovement(1f); // simulate held D

        Assert.AreEqual(0f, _rigidbody.velocity.x, 0.0001f,
            "Movement must be fully frozen while DefendHeld is true (plan section 1.8's full-commit gating).");
    }

    /// <summary>
    /// Plan section 1.8's Attack no-stack guard, code-side half (the other half is the Animator's own
    /// Can Transition To Self = false on the Attack Any-State transition, set in Task 3.5 - not
    /// testable from EditMode since it lives in the AnimatorController asset, not PlayerController.cs).
    /// Mirrors TryJump_DoublePressWithinAnticipationWindow_FiresOnlyOnce's structure for the analogous
    /// Attack case. Gap identified during Task 4.1's QA pass: no existing test previously exercised a
    /// same-attack-still-in-progress re-press directly.
    /// </summary>
    [Test]
    public void TryAttack_WhileAlreadyAttacking_DoesNotRefire()
    {
        CreatePlayer();

        bool firstAttackFired = _playerController.TryAttack(true);
        Assert.IsTrue(firstAttackFired, "First J press while grounded and not already attacking should fire the attack.");
        Assert.IsTrue(_playerController.IsAttacking);

        bool secondAttackFired = _playerController.TryAttack(true);
        Assert.IsFalse(secondAttackFired,
            "A second J press while _isAttacking is already true must be ignored (plan section 1.8's no-stack guard).");
        Assert.IsTrue(_playerController.IsAttacking,
            "The original attack should still be in progress, undisturbed by the ignored second press.");

        // Simulate the Attack clip's final-frame Animation Event firing on the normal-completion path.
        _playerController.OnAttackAnimationComplete();
        Assert.IsFalse(_playerController.IsAttacking);

        bool thirdAttackFired = _playerController.TryAttack(true);
        Assert.IsTrue(thirdAttackFired, "A genuinely new attack must succeed once the prior one completed normally.");
    }

    /// <summary>
    /// Plan section 1.8's full-commit gating, the Jump/Space half (the horizontal-movement half is
    /// covered by FullCommit_WhileAttacking_HorizontalVelocityStaysFrozenAtZero and
    /// FullCommit_WhileDefendHeld_HorizontalVelocityStaysFrozenAtZero above) - "while _isAttacking or
    /// DefendHeld is true, both Jump/Space AND Left/Right horizontal input are ignored" (Task 3.4a's
    /// literal acceptance criterion). Gap identified during Task 4.1's QA pass: nothing previously
    /// exercised TryJump's IsFullyCommitted branch directly.
    /// </summary>
    [Test]
    public void TryJump_WhileAttacking_IsIgnored()
    {
        CreatePlayer();

        bool attackStarted = _playerController.TryAttack(true);
        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAttacking);

        bool jumpStarted = _playerController.TryJump(true);
        Assert.IsFalse(jumpStarted,
            "Space presses must be ignored while fully committed to Attack (plan section 1.8) - " +
            "the character cannot both swing and jump simultaneously.");
        Assert.IsFalse(_playerController.IsJumpInProgress);
    }

    /// <summary>Same requirement as above, exercised for DefendHeld via <see cref="ForceSetDefendHeld"/>.</summary>
    [Test]
    public void TryJump_WhileDefendHeld_IsIgnored()
    {
        CreatePlayer();

        ForceSetDefendHeld(_playerController, true);
        Assert.IsTrue(_playerController.DefendHeld);

        bool jumpStarted = _playerController.TryJump(true);
        Assert.IsFalse(jumpStarted,
            "Space presses must be ignored while DefendHeld is true (plan section 1.8's full-commit gating).");
        Assert.IsFalse(_playerController.IsJumpInProgress);
    }

    /// <summary>
    /// The specific regression this plan's review process found and required a fix for (plan section
    /// 1.9): if Hurt() interrupts JumpAnticipation before the 0.08s timer elapses, _jumpInProgress must
    /// be explicitly reset to false by Hurt() itself - since the character never left the ground in
    /// this scenario, there is no landing edge for the NORMAL clear-path to fire on, and a
    /// clear-only-on-landing implementation would leave jump permanently disabled after this exact
    /// sequence.
    /// </summary>
    [Test]
    public void Jump_InterruptedByHurtMidAnticipation_ThenRecovers_NewJumpSucceeds()
    {
        CreatePlayer();

        bool jumpStarted = _playerController.TryJump(true);
        Assert.IsTrue(jumpStarted);
        Assert.IsTrue(_playerController.IsJumpInProgress);

        _playerController.Hurt(); // interrupts before AdvanceJumpTimer ever resolves the 0.08s window

        Assert.IsFalse(_playerController.IsJumpInProgress,
            "Hurt() must explicitly reset _jumpInProgress at the moment the interrupt is accepted (plan section 1.9).");

        _playerController.AdvanceHurtStunTimer(0.31f); // wait out the stun window, unconditionally

        bool secondJumpStarted = _playerController.TryJump(true);
        Assert.IsTrue(secondJumpStarted,
            "A NEW jump must succeed after recovering from the interrupt - proves _jumpInProgress didn't get stuck permanently true.");
    }

    /// <summary>
    /// Mirror of the jump test above, for Attack (plan section 1.9): if Hurt() interrupts mid-swing
    /// before the Animation Event (OnAttackAnimationComplete) is ever reached, _isAttacking must be
    /// explicitly reset to false by Hurt() itself, or Attack would be permanently disabled after any
    /// mid-swing hit.
    /// </summary>
    [Test]
    public void Attack_InterruptedByHurtMidSwing_ThenRecovers_NewAttackSucceeds()
    {
        CreatePlayer();

        bool attackStarted = _playerController.TryAttack(true);
        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAttacking);

        _playerController.Hurt(); // interrupts before OnAttackAnimationComplete() is ever called

        Assert.IsFalse(_playerController.IsAttacking,
            "Hurt() must explicitly reset _isAttacking at the moment the interrupt is accepted (plan section 1.9).");

        _playerController.AdvanceHurtStunTimer(0.31f); // wait out the stun window, unconditionally

        bool secondAttackStarted = _playerController.TryAttack(true);
        Assert.IsTrue(secondAttackStarted,
            "A NEW attack must succeed after recovering from the interrupt - proves _isAttacking didn't get stuck permanently true.");
    }

    // -------------------------------------------------------------------
    // Task 3.1a (Docs/Plans/004_walk-anim-freeze-and-jump-stall-fix.md): coverage for the
    // Task 1.1 _jumpImpulseCancelled mechanism. As that plan's Task 1.1 write-up documents, no
    // existing test above actually proved Hurt() cancels the delayed jump impulse itself -
    // Jump_InterruptedByHurtMidAnticipation_ThenRecovers_NewJumpSucceeds only asserts on
    // IsJumpInProgress/a second jump starting, never on velocity.y. These three tests close that
    // gap. (Task 3.1a's Branch B/C repro test is intentionally NOT included here - it depends on
    // Task 2.2, which has not happened yet: Phase 2 is still blocked on the Task 0.2 human-playtest
    // diagnostic session. Add it once Task 2.2 lands.)
    // -------------------------------------------------------------------

    /// <summary>
    /// Test-only helper that reads the private <c>_jumpImpulseCancelled</c> field via reflection -
    /// same rationale/convention as <see cref="ForceSetDefendHeld"/> above: the field has no public
    /// accessor (by design, per plan 004 Task 1.1's doc comment), and adding a test-only public
    /// getter is out of scope for this QA-only task.
    /// </summary>
    private static bool GetJumpImpulseCancelled(PlayerController controller)
    {
        FieldInfo field = typeof(PlayerController).GetField(
            "_jumpImpulseCancelled",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)field.GetValue(controller);
    }

    /// <summary>
    /// The specific missing guarantee plan 004's Task 1.1 write-up calls out: no prior test actually
    /// asserted on velocity.y after a Hurt()-interrupted jump. This proves Hurt() cancellation is
    /// real and deterministic via the dedicated <c>_jumpImpulseCancelled</c> flag, independent of any
    /// Animator/JumpAnticipation state inspection (no Animator is attached in this fixture at all).
    /// </summary>
    [Test]
    public void Jump_CancelledByHurt_NoVerticalImpulseApplied()
    {
        CreatePlayer();

        bool jumpStarted = _playerController.TryJump(true);
        Assert.IsTrue(jumpStarted);

        // A distinct, non-default pre-existing .y so the assertion below proves "unchanged from
        // whatever gravity had already produced", not a coincidental match against Rigidbody2D's
        // zero default (same discipline as AdvanceJumpTimer_CancelsImpulse_WhenDeadBeforeTimerElapses).
        const float preExistingVerticalVelocity = -1f;
        _rigidbody.velocity = new Vector2(0f, preExistingVerticalVelocity);

        _playerController.Hurt(); // cancels this jump's impulse via _jumpImpulseCancelled (plan 004 Task 1.1)

        _playerController.AdvanceJumpTimer(0.08f); // elapse exactly the anticipation window

        Assert.AreEqual(preExistingVerticalVelocity, _rigidbody.velocity.y, 0.0001f,
            "Hurt() must cancel the delayed jump impulse - velocity.y must not reflect the 15 u/s jump " +
            "impulse once the anticipation window elapses (plan 004 Task 1.1's _jumpImpulseCancelled).");
    }

    /// <summary>
    /// Proves the new mechanism directly: an uninterrupted jump leaves <c>_jumpImpulseCancelled</c>
    /// false and its vertical impulse applies deterministically - independent of the old
    /// Animator-null fallback in <see cref="PlayerController"/>'s now-unused
    /// IsJumpAnticipationStillActive (this fixture's CreatePlayer() never attaches an Animator either
    /// way, but this test asserts on the actual flag rather than relying on that fallback coincidence).
    /// </summary>
    [Test]
    public void Jump_NoInterrupt_ImpulseCancelledFlagStaysFalse_AndImpulseApplies()
    {
        CreatePlayer();

        bool jumpStarted = _playerController.TryJump(true);
        Assert.IsTrue(jumpStarted);

        _playerController.AdvanceJumpTimer(0.08f); // elapse exactly the anticipation window

        Assert.IsFalse(GetJumpImpulseCancelled(_playerController),
            "_jumpImpulseCancelled must remain false for an uninterrupted jump (plan 004 Task 1.1).");
        Assert.AreEqual(15f, _rigidbody.velocity.y, 0.0001f,
            "The vertical impulse must apply deterministically once _jumpImpulseCancelled is confirmed " +
            "false, with no Animator attached at all in this fixture.");
    }

    /// <summary>
    /// Proves <c>_jumpImpulseCancelled</c> does not leak true from a cancelled jump into a later,
    /// independent one - it must be reset to false by the second <see cref="PlayerController.TryJump"/>
    /// call, not left stuck true from the first Hurt() interrupt (plan 004 Task 1.1's explicit
    /// "reset to false at the moment a new jump starts" requirement).
    /// </summary>
    [Test]
    public void Jump_ImpulseCancelledFlag_DoesNotLeakIntoLaterJump()
    {
        CreatePlayer();

        bool firstJumpStarted = _playerController.TryJump(true);
        Assert.IsTrue(firstJumpStarted);

        _playerController.Hurt(); // cancels the first jump's impulse and resets _jumpInProgress

        _playerController.AdvanceHurtStunTimer(0.31f); // wait out the stun window so a new jump is possible

        bool secondJumpStarted = _playerController.TryJump(true);
        Assert.IsTrue(secondJumpStarted,
            "A new jump must be able to start after recovering from the Hurt() interrupt.");

        _playerController.AdvanceJumpTimer(0.08f); // elapse exactly the anticipation window for the SECOND jump

        Assert.AreEqual(15f, _rigidbody.velocity.y, 0.0001f,
            "The second jump's impulse must apply - proves _jumpImpulseCancelled was reset to false by " +
            "the second TryJump() call rather than staying stuck true from the first cancellation.");
    }

    // -------------------------------------------------------------------
    // Task 3.1 (Docs/Plans/005_attack-while-jumping.md): coverage for air-attack gating
    // (Task 1.1's TryAttack/_isAirAttack lifecycle and Task 1.2's live-IsGrounded IsFullyCommitted
    // redefinition).
    // -------------------------------------------------------------------

    /// <summary>
    /// Test-only helper that force-sets the private-set IsGrounded property via reflection - same
    /// rationale/convention as <see cref="ForceSetDefendHeld"/> above. IsGrounded is normally computed
    /// by FixedUpdate's real OverlapCircle query (Task 3.3), which this EditMode fixture never runs
    /// (no ground collider exists), so this is the only physics-free way to simulate "airborne" /
    /// "just landed" for the air-attack tests below.
    /// </summary>
    private static void ForceSetIsGrounded(PlayerController controller, bool value)
    {
        PropertyInfo property = typeof(PlayerController).GetProperty(
            nameof(PlayerController.IsGrounded), BindingFlags.Public | BindingFlags.Instance);
        property.SetValue(controller, value);
    }

    [Test]
    public void TryAttack_WhileAirborne_SucceedsAndSetsIsAttackingAndIsAirAttack()
    {
        CreatePlayer();

        bool attackStarted = _playerController.TryAttack(false);

        Assert.IsTrue(attackStarted,
            "TryAttack(false) (airborne) should succeed now that the !isGrounded gate is removed (plan 005 Task 1.1).");
        Assert.IsTrue(_playerController.IsAttacking);
        Assert.IsTrue(_playerController.IsAirAttack);
    }

    [Test]
    public void TryAttack_WhileGrounded_SetsIsAirAttackFalse()
    {
        CreatePlayer();

        bool attackStarted = _playerController.TryAttack(true);

        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAttacking);
        Assert.IsFalse(_playerController.IsAirAttack,
            "Ground-attack must set IsAirAttack false (plan 005 Task 1.1, regression pin).");
    }

    [Test]
    public void ApplyHorizontalMovement_WhileAirAttacking_AppliesRealHorizontalVelocity_NotFrozen()
    {
        CreatePlayer();
        ForceSetIsGrounded(_playerController, false);

        bool attackStarted = _playerController.TryAttack(false); // air-attack: starts while airborne
        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAirAttack);

        const float preExistingVerticalVelocity = -5f; // simulates mid-fall .y that must be left alone
        _rigidbody.velocity = new Vector2(0f, preExistingVerticalVelocity);

        _playerController.ApplyHorizontalMovement(1f);

        Assert.AreEqual(4.5f, _rigidbody.velocity.x, 0.0001f,
            "Air-attack must not freeze horizontal movement (plan 005 Task 1.2) - IsFullyCommitted's " +
            "attack contribution keys off live IsGrounded, which is false here.");
        Assert.AreEqual(preExistingVerticalVelocity, _rigidbody.velocity.y, 0.0001f,
            "ApplyHorizontalMovement must never touch .y.");
    }

    [Test]
    public void ApplyHorizontalMovement_WhileGroundAttacking_StillForcesXToZero()
    {
        CreatePlayer();
        Assert.IsTrue(_playerController.IsGrounded, "Rig defaults grounded true.");

        bool attackStarted = _playerController.TryAttack(true); // ground-attack
        Assert.IsTrue(attackStarted);
        Assert.IsFalse(_playerController.IsAirAttack);

        _playerController.ApplyHorizontalMovement(1f);

        Assert.AreEqual(0f, _rigidbody.velocity.x, 0.0001f,
            "Ground-attack must still freeze horizontal movement (plan 005 Task 1.2, regression pin - " +
            "unchanged from pre-plan behavior).");
    }

    /// <summary>
    /// Plan 005 Task 1.2's named highest-risk case: a player who starts an air-attack and lands before
    /// the Attack clip's exit time must have the freeze re-engage on the very next
    /// ApplyHorizontalMovement call once IsGrounded flips true - proving IsFullyCommitted's attack
    /// contribution is keyed off LIVE IsGrounded, not a latched _isAirAttack flag (which would leave
    /// the character un-frozen and walkable on the ground for the rest of the swing).
    /// </summary>
    [Test]
    public void ApplyHorizontalMovement_LandingMidAirAttack_ReFreezesOnVeryNextCall()
    {
        CreatePlayer();
        ForceSetIsGrounded(_playerController, false);

        bool attackStarted = _playerController.TryAttack(false); // air-attack, starts airborne
        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAttacking);
        Assert.IsTrue(_playerController.IsAirAttack);

        // Sanity baseline: still un-frozen immediately before landing.
        _playerController.ApplyHorizontalMovement(1f);
        Assert.AreEqual(4.5f, _rigidbody.velocity.x, 0.0001f, "Should still be un-frozen immediately before landing.");

        // Simulate landing while _isAttacking is still true (Attack clip not yet finished) - the
        // exact case Task 1.2 names as its highest risk.
        ForceSetIsGrounded(_playerController, true);

        _playerController.ApplyHorizontalMovement(1f);

        Assert.AreEqual(0f, _rigidbody.velocity.x, 0.0001f,
            "Landing mid-air-attack must re-freeze horizontal movement on the very next " +
            "ApplyHorizontalMovement call (plan 005 Task 1.2's named risk).");
        Assert.IsTrue(_playerController.IsAttacking, "Still mid-swing - only IsGrounded flipped, not _isAttacking.");
    }

    /// <summary>
    /// Plan 005 Task 1.1's named risk: _isAirAttack must not leak `true` into a later ground-attack
    /// after a Hurt() interrupt mid-air-attack.
    /// </summary>
    [Test]
    public void Hurt_MidAirAttack_ResetsIsAirAttack_AndSubsequentGroundAttackReFreezesMovement()
    {
        CreatePlayer();
        ForceSetIsGrounded(_playerController, false);

        bool attackStarted = _playerController.TryAttack(false); // air-attack
        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAirAttack);

        _playerController.Hurt(); // interrupts mid-air-attack

        Assert.IsFalse(_playerController.IsAttacking);
        Assert.IsFalse(_playerController.IsAirAttack,
            "Hurt() must reset IsAirAttack alongside IsAttacking (plan 005 Task 1.1) - a stale true would " +
            "leak into the next attack.");

        _playerController.AdvanceHurtStunTimer(0.31f); // wait out the stun window
        ForceSetIsGrounded(_playerController, true); // character is grounded again for the new attack

        bool secondAttackStarted = _playerController.TryAttack(true); // NEW ground-attack
        Assert.IsTrue(secondAttackStarted);
        Assert.IsFalse(_playerController.IsAirAttack,
            "The new ground-attack must not inherit a leaked true from the interrupted air-attack.");

        _playerController.ApplyHorizontalMovement(1f);
        Assert.AreEqual(0f, _rigidbody.velocity.x, 0.0001f,
            "The new ground-attack must correctly re-freeze movement - no leak from the interrupted air-attack.");
    }

    /// <summary>
    /// Mirror of the Hurt() test above, for Die() (plan 005 Task 1.1). Unlike Hurt(), Die() is a
    /// permanent lock (IsDead stays true forever), so there is no "recovers, then a new attack
    /// succeeds" step here - instead this proves the flag reset itself, and that any subsequent
    /// TryAttack call is blocked by IsDead specifically, not by a leaked _isAirAttack/_isAttacking flag.
    /// </summary>
    [Test]
    public void Die_MidAirAttack_ResetsIsAttackingAndIsAirAttack()
    {
        CreatePlayer();
        ForceSetIsGrounded(_playerController, false);

        bool attackStarted = _playerController.TryAttack(false); // air-attack
        Assert.IsTrue(attackStarted);
        Assert.IsTrue(_playerController.IsAirAttack);

        _playerController.Die(); // interrupts mid-air-attack, permanently

        Assert.IsFalse(_playerController.IsAttacking);
        Assert.IsFalse(_playerController.IsAirAttack,
            "Die() must reset IsAirAttack alongside IsAttacking (plan 005 Task 1.1) - mirrors Hurt()'s reset.");
        Assert.IsTrue(_playerController.IsDead);

        bool attackAfterDeath = _playerController.TryAttack(true);
        Assert.IsFalse(attackAfterDeath,
            "TryAttack must remain permanently blocked by IsDead after Die() - unrelated to this plan, " +
            "confirms the block is IsDead, not a leaked attack flag.");
    }

    // -------------------------------------------------------------------
    // Task 6.1 (Docs/Plans/007_look-up-down-idle-animation-and-pan-tuning.md): coverage for
    // UpdateLookState's idle-AND-grounded gating and Hurt()/Die()'s LookingUp/LookingDown force-clears.
    // -------------------------------------------------------------------

    /// <summary>
    /// Test-only helper that force-sets the private field _horizontalInput via reflection - same
    /// rationale/convention as <see cref="ForceSetDefendHeld"/>/<see cref="ForceSetIsGrounded"/> above.
    /// _horizontalInput is otherwise only ever written inside the untestable Update() (from real
    /// Input.GetKey(KeyCode.A/D) reads), so EditMode tests need a way to force it nonzero to exercise
    /// UpdateLookState's "not walking" gate (plan section, Decision 6).
    /// </summary>
    private static void ForceSetHorizontalInput(PlayerController controller, float value)
    {
        FieldInfo field = typeof(PlayerController).GetField(
            "_horizontalInput", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(controller, value);
    }

    [Test]
    public void UpdateLookState_WHeldWhileIdleAndGrounded_SetsLookingUpOnly()
    {
        CreatePlayer();
        Assert.IsTrue(_playerController.IsGrounded, "Rig defaults grounded true.");

        _playerController.UpdateLookState(true, false);

        Assert.IsTrue(_playerController.LookingUp, "Holding W while idle and grounded should set LookingUp.");
        Assert.IsFalse(_playerController.LookingDown, "LookingDown must stay false when only W is held.");
    }

    [Test]
    public void UpdateLookState_SHeldWhileIdleAndGrounded_SetsLookingDownOnly()
    {
        CreatePlayer();
        Assert.IsTrue(_playerController.IsGrounded, "Rig defaults grounded true.");

        _playerController.UpdateLookState(false, true);

        Assert.IsTrue(_playerController.LookingDown, "Holding S while idle and grounded should set LookingDown.");
        Assert.IsFalse(_playerController.LookingUp, "LookingUp must stay false when only S is held.");
    }

    /// <summary>
    /// The accumulation-cancel behavior (plan Decision 3/6): mirrors CameraFollow's own W/S cancel and
    /// this file's own A/D cancel - both keys held simultaneously must net to neither direction.
    /// </summary>
    [Test]
    public void UpdateLookState_BothHeld_SetsNeitherTrue_MirrorsCameraCancelBehavior()
    {
        CreatePlayer();

        _playerController.UpdateLookState(true, true);

        Assert.IsFalse(_playerController.LookingUp, "Both keys held must cancel to neither direction.");
        Assert.IsFalse(_playerController.LookingDown, "Both keys held must cancel to neither direction.");
    }

    [Test]
    public void UpdateLookState_WhileDead_BothFalse_RegardlessOfKeysHeld()
    {
        CreatePlayer();
        _playerController.IsDead = true;

        _playerController.UpdateLookState(true, false);

        Assert.IsFalse(_playerController.LookingUp, "Dead characters must never show a look-pose.");
        Assert.IsFalse(_playerController.LookingDown, "Dead characters must never show a look-pose.");
    }

    /// <summary>
    /// IsFullyCommitted is exercised here via DefendHeld (see <see cref="ForceSetDefendHeld"/>'s doc
    /// comment for why reflection is used) - one of IsFullyCommitted's three inputs.
    /// </summary>
    [Test]
    public void UpdateLookState_WhileFullyCommitted_BothFalse()
    {
        CreatePlayer();
        ForceSetDefendHeld(_playerController, true);
        Assert.IsTrue(_playerController.IsFullyCommitted);

        _playerController.UpdateLookState(true, false);

        Assert.IsFalse(_playerController.LookingUp, "Fully-committed characters must never show a look-pose.");
        Assert.IsFalse(_playerController.LookingDown, "Fully-committed characters must never show a look-pose.");
    }

    [Test]
    public void UpdateLookState_WhileNotGrounded_BothFalse()
    {
        CreatePlayer();
        ForceSetIsGrounded(_playerController, false);

        _playerController.UpdateLookState(true, false);

        Assert.IsFalse(_playerController.LookingUp, "Airborne characters must never show a look-pose.");
        Assert.IsFalse(_playerController.LookingDown, "Airborne characters must never show a look-pose.");
    }

    [Test]
    public void UpdateLookState_WhileHorizontalInputNonzero_BothFalse()
    {
        CreatePlayer();
        ForceSetHorizontalInput(_playerController, 1f);

        _playerController.UpdateLookState(true, false);

        Assert.IsFalse(_playerController.LookingUp, "Walking characters must never show a look-pose.");
        Assert.IsFalse(_playerController.LookingDown, "Walking characters must never show a look-pose.");
    }

    /// <summary>
    /// Mirrors the existing DefendHeld force-clear precedent in Hurt()/Die() (plan Decision 5): the
    /// clear must be immediate, in the same call, not merely visible on a subsequent Update() tick.
    /// </summary>
    [Test]
    public void Hurt_WhileLookingUp_ImmediatelyClearsLookingUp()
    {
        CreatePlayer();
        _playerController.UpdateLookState(true, false);
        Assert.IsTrue(_playerController.LookingUp, "Precondition: LookingUp should be true before Hurt().");

        _playerController.Hurt();

        Assert.IsFalse(_playerController.LookingUp,
            "Hurt() must immediately force-clear LookingUp, not merely on the next Update() tick.");
    }

    /// <summary>Mirror of the test above, for Die() (plan Decision 5).</summary>
    [Test]
    public void Die_WhileLookingDown_ImmediatelyClearsLookingDown()
    {
        CreatePlayer();
        _playerController.UpdateLookState(false, true);
        Assert.IsTrue(_playerController.LookingDown, "Precondition: LookingDown should be true before Die().");

        _playerController.Die();

        Assert.IsFalse(_playerController.LookingDown,
            "Die() must immediately force-clear LookingDown, not merely on the next Update() tick.");
    }
}

/// <summary>
/// EditMode tests for JumpPhysicsMath (Task 3.3, plan section 1.5). Confirms the plan's hand-verified
/// numbers (v0 = 15 u/s, gravity = 37.5 u/s^2, gravityScale ~= 3.823) are reproduced by the derivation
/// formulas from the design targets (apex height 3.0 units, time-to-apex 0.4s).
/// </summary>
public class JumpPhysicsMathTests
{
    [Test]
    public void DerivesV0AndGravityAndGravityScale_FromApexHeightAndTimeToApex()
    {
        const float apexHeight = 3.0f;
        const float timeToApex = 0.4f;

        float v0 = JumpPhysicsMath.DeriveInitialVelocity(apexHeight, timeToApex);
        float gravity = JumpPhysicsMath.DeriveGravityMagnitude(apexHeight, timeToApex);
        float gravityScale = JumpPhysicsMath.DeriveGravityScale(gravity);

        Assert.AreEqual(15f, v0, 0.001f, "v0 = 2h/t per plan section 1.5.");
        Assert.AreEqual(37.5f, gravity, 0.001f, "g = 2h/t^2 per plan section 1.5.");
        Assert.AreEqual(3.823f, gravityScale, 0.001f, "gravityScale = g / 9.81 per plan section 1.5.");
    }
}
