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

        _playerController.ApplyHorizontalMovement(1f); // simulate one frame of Right-arrow input

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
    /// Input.GetKey(KeyCode.X) reads (plan section 1.7), and EditMode tests cannot simulate real
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

        _playerController.ApplyHorizontalMovement(1f); // simulate held Right-arrow

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

        _playerController.ApplyHorizontalMovement(1f); // simulate held Right-arrow

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
        Assert.IsTrue(firstAttackFired, "First Z press while grounded and not already attacking should fire the attack.");
        Assert.IsTrue(_playerController.IsAttacking);

        bool secondAttackFired = _playerController.TryAttack(true);
        Assert.IsFalse(secondAttackFired,
            "A second Z press while _isAttacking is already true must be ignored (plan section 1.8's no-stack guard).");
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
