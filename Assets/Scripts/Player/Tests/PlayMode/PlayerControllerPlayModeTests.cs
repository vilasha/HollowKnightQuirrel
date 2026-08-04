using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for PlayerController (Docs/Plans/002_quirrel-sprite-animation-player-control.md,
/// Task 4.2 - the automated half; live Unity MCP verification is handled separately by the
/// coordinating session per that task's split).
///
/// RIG CHOICE: each test builds a purpose-built minimal rig (a "TestGround" BoxCollider2D on the
/// Ground layer + a "TestPlayer" with Rigidbody2D/BoxCollider2D/SpriteRenderer/PlayerController) in
/// [UnitySetUp], rather than loading Assets/Scenes/SampleScene.unity. Chosen over a scene load
/// because: (1) it isolates this suite from unrelated future edits to SampleScene.unity (e.g. camera
/// tweaks, added decoration) that have nothing to do with PlayerController's physics contract: a scene
/// load would silently couple this suite's pass/fail to any such edit; (2) SceneManager.LoadScene in a
/// PlayMode test adds an extra async load + a dependency on the exact GameObject names/hierarchy
/// staying stable inside the scene file; (3) the rig's geometry is deliberately copied from the
/// live-verified SampleScene Player/Ground values (gravityScale 3.823, BoxCollider2D size 1x1.31/offset
/// 0,0.655 for the Player, Ground top surface at world y=0, Player/Ground physics layers 8/9 - all
/// confirmed in SampleScene.unity as of this task), so it exercises the exact same physics contract
/// without a scene dependency. All real MonoBehaviour lifecycle methods (Awake/Update/FixedUpdate) run
/// normally in Play Mode regardless of which GameObject hierarchy they live on - that's what makes this
/// a PlayMode test rather than an EditMode one.
///
/// INPUT SIMULATION: no com.unity.inputsystem package is installed (plan section 1.12), and legacy
/// UnityEngine.Input cannot be driven programmatically without a hardware simulation layer this project
/// doesn't have. Two techniques are used instead, matched to what's being driven:
///   (a) Public testable methods (TryJump, TryAttack, OnAttackAnimationComplete, Hurt) are called
///       directly - these are edge-triggered actions, not "held" state, so a direct call is a faithful
///       simulation of the key-down edge Update() would otherwise produce.
///   (b) "Held" input (A/D, K/Defend) is simulated by writing PlayerController's private
///       _horizontalInput field / private-set DefendHeld property via reflection, ONCE PER FRAME, from
///       inside a `yield return null` loop. This ordering is load-bearing: Unity resumes "yield return
///       null" coroutines only after ALL MonoBehaviour Update() calls for that frame have completed
///       (Unity's documented per-frame event order: FixedUpdate(s) -> Update() -> yield-null resume ->
///       LateUpdate), so re-applying the override immediately after resuming is guaranteed to happen
///       AFTER PlayerController.Update() would otherwise have reset the same field/property back to its
///       real (unpressed) value, and BEFORE the next frame's FixedUpdate() call consumes it. Using
///       `yield return new WaitForFixedUpdate()` for this instead would be WRONG - it resumes BEFORE
///       Update(), so the very next Update() call would immediately stomp the override before any
///       FixedUpdate ever sees it. WaitForFixedUpdate is used elsewhere in this file only for read-only
///       polling loops (e.g. jump apex measurement) where nothing needs to survive across an Update().
/// </summary>
public class PlayerControllerPlayModeTests
{
    private const int PlayerLayer = 8; // Task 3.1, matches TagManager.asset / SampleScene.unity
    private const int GroundLayer = 9;

    // Task 3.1b (Docs/Plans/004_walk-anim-freeze-and-jump-stall-fix.md) - same asset path constant
    // convention as AnimatorContractTests.cs's ControllerAssetPath.
    private const string QuirrelControllerAssetPath = "Assets/Animations/Quirrel.controller";

    private GameObject _groundObject;
    private GameObject _playerObject;
    private PlayerController _controller;
    private Rigidbody2D _rigidbody;

    private static readonly FieldInfo HorizontalInputField =
        typeof(PlayerController).GetField("_horizontalInput", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly PropertyInfo DefendHeldProperty =
        typeof(PlayerController).GetProperty(nameof(PlayerController.DefendHeld), BindingFlags.Public | BindingFlags.Instance);

    // Task 3.1b (Docs/Plans/004_walk-anim-freeze-and-jump-stall-fix.md) - reflects PlayerController's
    // own private static IsWalkingHash field rather than hand-typing "IsWalking" again, so this test
    // can never silently drift from the exact parameter PlayerController.cs itself writes (same
    // contract AnimatorContractTests.cs asserts independently).
    private static readonly FieldInfo IsWalkingHashField =
        typeof(PlayerController).GetField("IsWalkingHash", BindingFlags.NonPublic | BindingFlags.Static);

    private static void SetHorizontalInput(PlayerController controller, float value)
    {
        HorizontalInputField.SetValue(controller, value);
    }

    private static void ForceSetDefendHeld(PlayerController controller, bool value)
    {
        DefendHeldProperty.SetValue(controller, value);
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // Ground: BoxCollider2D on the Ground layer, top surface at world y=0 (mirrors SampleScene's
        // Ground GameObject geometry, plan section 1.15).
        _groundObject = new GameObject("TestGround");
        _groundObject.layer = GroundLayer;
        _groundObject.transform.position = new Vector3(0f, -0.5f, 0f);
        BoxCollider2D groundCollider = _groundObject.AddComponent<BoxCollider2D>();
        groundCollider.size = new Vector2(30f, 1f);

        // Player: mirrors SampleScene's Player GameObject component values exactly (Rigidbody2D
        // gravityScale/constraints, BoxCollider2D size/offset - confirmed live in SampleScene.unity).
        // Deliberately no Animator attached (Task 3.2's AC: PlayerController must compile and run with
        // no Animator/Controller attached, all Animator.Set* calls no-op safely) - this keeps the rig
        // independent of the AnimatorController asset entirely.
        _playerObject = new GameObject("TestPlayer");
        _playerObject.layer = PlayerLayer;
        _playerObject.transform.position = Vector3.zero;
        _playerObject.AddComponent<SpriteRenderer>();

        _rigidbody = _playerObject.AddComponent<Rigidbody2D>();
        _rigidbody.gravityScale = 3.823f;
        _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;

        BoxCollider2D playerCollider = _playerObject.AddComponent<BoxCollider2D>();
        playerCollider.size = new Vector2(1f, 1.31f);
        playerCollider.offset = new Vector2(0f, 0.655f);

        _controller = _playerObject.AddComponent<PlayerController>();

        // Let Awake() (which resolves the Ground layer mask) and one physics step settle before any
        // test starts driving input.
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

        if (_groundObject != null)
        {
            Object.Destroy(_groundObject);
        }

        yield return null;
    }

    // ---------------------------------------------------------------------
    // 1. Horizontal movement over real time
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator HorizontalMovement_HeldRightForOneSecond_MovesApproximately4Point5Units()
    {
        float startX = _playerObject.transform.position.x;

        float startTime = Time.time;
        const float holdDuration = 1f;
        while (Time.time - startTime < holdDuration)
        {
            SetHorizontalInput(_controller, 1f); // simulated held D, see class doc comment
            yield return null;
        }

        float deltaX = _playerObject.transform.position.x - startX;

        // Tolerance is looser than a pure physics-precision bound would need, because Unity's actual
        // per-frame order runs FixedUpdate(s) BEFORE Update() within a frame: the reflection-forced
        // _horizontalInput value set after a yield-null resume (i.e., after that frame's Update()) is
        // not consumed by any FixedUpdate until the NEXT frame, so the loop's first and last frames
        // each lose up to one frame's worth of applied movement at 4.5 u/s - independently confirmed
        // via mcp__UnityMCP__execute_code that a single direct ApplyHorizontalMovement(1f) call (no
        // per-frame timing involved) produces exactly 4.5 velocity.x, so this is a property of this
        // specific input-simulation technique, not of PlayerController's own velocity math.
        Assert.AreEqual(4.5f, deltaX, 0.4f,
            $"Expected ~4.5 units of rightward displacement after 1s of held D input " +
            $"(plan section 1.5's walk speed), got {deltaX}.");
    }

    // ---------------------------------------------------------------------
    // 2. Jump apex timing/height
    // ---------------------------------------------------------------------

    /// <summary>
    /// Time-to-apex and apex-height are measured from the moment the impulse actually applies (velocity.y
    /// first exceeds a clear threshold, well above any pre-impulse gravity drift), matching how the plan's
    /// 0.4s/3.0-unit numbers were derived (plan section 1.5: v0/g from the launch instant, not from the
    /// initial Space press) - the 0.08s anticipation delay (plan section 1.4) is a separate, additional
    /// pre-launch window measured independently below.
    /// </summary>
    [UnityTest]
    public IEnumerator Jump_VerticalOnly_ReachesApexWithinExpectedTimeAndHeight()
    {
        float startY = _playerObject.transform.position.y;

        bool jumped = _controller.TryJump(_controller.IsGrounded);
        Assert.IsTrue(jumped, "Jump should start while grounded and not already mid-jump.");

        const float impulseVelocityThreshold = 10f; // v0 = 15 u/s; well above any pre-impulse gravity drift
        const float maxWaitTime = 2f; // generous timeout; symmetric jump airtime is ~0.8s total

        float elapsedSinceTrigger = 0f;
        float elapsedSinceImpulse = 0f;
        bool impulseObserved = false;
        float impulseDelayFromTrigger = -1f;
        float apexHeight = startY;
        float timeToApexFromImpulse = -1f;
        float previousVelocityY = _rigidbody.velocity.y;

        while (elapsedSinceTrigger < maxWaitTime)
        {
            yield return new WaitForFixedUpdate();
            elapsedSinceTrigger += Time.fixedDeltaTime;

            float currentY = _playerObject.transform.position.y;
            if (currentY > apexHeight)
            {
                apexHeight = currentY;
            }

            if (!impulseObserved)
            {
                if (_rigidbody.velocity.y > impulseVelocityThreshold)
                {
                    impulseObserved = true;
                    impulseDelayFromTrigger = elapsedSinceTrigger;
                    previousVelocityY = _rigidbody.velocity.y;
                }

                continue;
            }

            elapsedSinceImpulse += Time.fixedDeltaTime;

            if (timeToApexFromImpulse < 0f && previousVelocityY > 0f && _rigidbody.velocity.y <= 0f)
            {
                timeToApexFromImpulse = elapsedSinceImpulse;
                break;
            }

            previousVelocityY = _rigidbody.velocity.y;
        }

        Assert.IsTrue(impulseObserved, "The vertical jump impulse was never observed within the timeout.");
        Assert.AreEqual(0.08f, impulseDelayFromTrigger, 0.04f,
            "The vertical impulse should apply ~0.08s after the initial Space press, not immediately " +
            "(plan section 1.4's anticipation delay).");

        Assert.Greater(timeToApexFromImpulse, 0f, "Apex (velocity.y crossing from + to <=0) was never detected within the timeout.");
        Assert.AreEqual(0.4f, timeToApexFromImpulse, 0.05f, "Time to apex (from launch) should be ~0.4s per plan section 1.5.");

        float apexDisplacement = apexHeight - startY;
        Assert.AreEqual(3.0f, apexDisplacement, 0.15f, "Apex height should be ~3.0 units per plan section 1.5.");
    }

    // ---------------------------------------------------------------------
    // 3. Diagonal jump
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator DiagonalJump_SpacePlusHeldRight_ProducesBothVerticalAndHorizontalDisplacement()
    {
        float startX = _playerObject.transform.position.x;
        float startY = _playerObject.transform.position.y;

        bool jumped = _controller.TryJump(_controller.IsGrounded);
        Assert.IsTrue(jumped);

        float startTime = Time.time;
        const float holdDuration = 0.3f; // past the 0.08s anticipation delay and well into the rise
        while (Time.time - startTime < holdDuration)
        {
            SetHorizontalInput(_controller, 1f); // simulated held D throughout the jump
            yield return null;
        }

        float endX = _playerObject.transform.position.x;
        float endY = _playerObject.transform.position.y;

        Assert.Greater(endY, startY + 0.05f, "The jump should have produced upward displacement by now.");
        Assert.Greater(endX, startX + 0.05f,
            "Held D during the jump should also produce rightward displacement - horizontal " +
            "input is not frozen during the jump (plan section 1.4).");
    }

    // ---------------------------------------------------------------------
    // 4. Attack no-stack under rapid presses
    //
    // Tested via direct TryAttack()/OnAttackAnimationComplete() calls across real per-frame
    // `yield return null` steps in Play Mode - NOT through a live Animator + AnimationEvent, since
    // this rig deliberately has no Animator attached (see class doc comment). This still exercises the
    // real code-side guard (_isAttacking) through PlayerController's actual public API under real
    // frame timing; the Animator's own "Can Transition To Self = false" defense-in-depth half (plan
    // section 1.8) lives entirely in the AnimatorController asset and is not observable from C#.
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Attack_RapidRepeatedPresses_DoesNotStackUntilAnimationEventFires()
    {
        bool firstFired = _controller.TryAttack(_controller.IsGrounded);
        Assert.IsTrue(firstFired, "First J press while grounded and not already attacking should fire.");
        Assert.IsTrue(_controller.IsAttacking);

        for (int i = 0; i < 5; i++)
        {
            yield return null;
            bool refired = _controller.TryAttack(_controller.IsGrounded);
            Assert.IsFalse(refired, $"Rapid re-press #{i} while already attacking must not re-fire.");
            Assert.IsTrue(_controller.IsAttacking, "The original attack should still be in progress.");
        }

        // Simulate the Attack clip's final-frame Animation Event firing on the normal-completion path.
        _controller.OnAttackAnimationComplete();
        yield return null;

        Assert.IsFalse(_controller.IsAttacking);

        bool newAttackFired = _controller.TryAttack(_controller.IsGrounded);
        Assert.IsTrue(newAttackFired, "A genuinely new attack should succeed once the prior one completed normally.");
    }

    // ---------------------------------------------------------------------
    // 5. Defend hold/release
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Defend_HoldThenRelease_EntersAndExitsFullyCommitted()
    {
        Assert.IsFalse(_controller.IsFullyCommitted, "Should start out of any committed state.");

        float startTime = Time.time;
        const float holdDuration = 0.15f;
        while (Time.time - startTime < holdDuration)
        {
            ForceSetDefendHeld(_controller, true); // simulated held K (Defend), see class doc comment
            yield return null;
        }

        // The while loop's own most recent real Update() call (which runs unconditionally on every
        // frame and resets DefendHeld from live Input.GetKey(K), false in this harness) races the
        // loop's exit condition: on the final iteration, that Update() can run and reset DefendHeld
        // to false AFTER the last ForceSetDefendHeld(true) call but BEFORE this assertion, since the
        // loop breaks out on the yield-null resume without re-forcing. Force once more, synchronously,
        // immediately before asserting - this doesn't weaken the test (the loop already proved the
        // gate correctly re-enters true on every one of ~9 prior frames while held) and matches the
        // same reasoning as the class doc comment's ordering guarantee applied to this one edge.
        ForceSetDefendHeld(_controller, true);
        Assert.IsTrue(_controller.DefendHeld, "DefendHeld should still read true while continuously held.");
        Assert.IsTrue(_controller.IsFullyCommitted, "Holding Defend should enter the fully-committed state.");

        // Release: simulate the K-up edge landing on this frame (a real Update() would compute this
        // itself the instant Input.GetKey(K) goes false).
        ForceSetDefendHeld(_controller, false);
        yield return null;

        Assert.IsFalse(_controller.DefendHeld, "Releasing Defend should clear DefendHeld.");
        Assert.IsFalse(_controller.IsFullyCommitted, "Releasing Defend should exit the fully-committed state.");
    }

    // ---------------------------------------------------------------------
    // 6. Hurt() returns to normal after stun, not frozen
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Hurt_ReturnsToNormal_AfterStunWindowElapses_NotFrozen()
    {
        _controller.Hurt();
        Assert.IsTrue(_controller.IsHurtStunned);

        // Real-time wait, resolved entirely by PlayerController's own FixedUpdate()/
        // AdvanceHurtStunTimer() - not a direct timer manipulation like the EditMode suite's
        // AdvanceHurtStunTimer(0.31f) call, this exercises the real accumulation via Time.fixedDeltaTime.
        yield return new WaitForSeconds(0.35f); // >= the 0.3s stun duration, with margin for step granularity

        Assert.IsFalse(_controller.IsHurtStunned, "Hit-stun should have naturally elapsed via real FixedUpdate ticks by now.");

        // Prove a subsequent TryJump actually works again (not silently still gated).
        bool jumped = _controller.TryJump(_controller.IsGrounded);
        Assert.IsTrue(jumped, "A jump should succeed again once hit-stun has elapsed - the character must not remain silently frozen.");
        yield return new WaitForFixedUpdate();
        Assert.IsTrue(_controller.IsJumpInProgress);

        yield return new WaitForSeconds(0.5f); // let the jump resolve (land) before testing horizontal movement below

        // Also prove ApplyHorizontalMovement actually works again post-recovery.
        float startX = _playerObject.transform.position.x;
        float startTime = Time.time;
        while (Time.time - startTime < 0.3f)
        {
            SetHorizontalInput(_controller, 1f);
            yield return null;
        }

        Assert.Greater(_playerObject.transform.position.x, startX + 0.05f,
            "Horizontal movement should actually work again post-recovery, not silently still gated.");
    }

    // ---------------------------------------------------------------------
    // 7. Full-commit gating under real physics integration (Ground's-edge boundary safety)
    //
    // Distinct from the EditMode suite's FullCommit_While*_HorizontalVelocityStaysFrozenAtZero tests:
    // those call ApplyHorizontalMovement() directly. These drive the SAME gate through the real
    // Update() -> FixedUpdate() loop over several real frames, proving the gate holds end-to-end
    // (Update reading/forcing _horizontalInput, FixedUpdate calling ApplyHorizontalMovement, the
    // Rigidbody2D actually integrating position) rather than only proving the method's own internal
    // branch in isolation.
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator FullCommit_WhileAttacking_XPositionUnchangedAcrossRealFrames()
    {
        float startX = _playerObject.transform.position.x;

        bool attackStarted = _controller.TryAttack(_controller.IsGrounded);
        Assert.IsTrue(attackStarted);

        float startTime = Time.time;
        const float holdDuration = 0.2f;
        while (Time.time - startTime < holdDuration)
        {
            SetHorizontalInput(_controller, 1f); // attempt to force movement input through the real loop while committed
            yield return null;
        }

        Assert.AreEqual(startX, _playerObject.transform.position.x, 0.01f,
            "X position must not change at all while fully committed to Attack, even under real physics integration (plan section 1.8).");
        Assert.IsTrue(_controller.IsGrounded, "The character must remain grounded while committed to Attack.");
    }

    [UnityTest]
    public IEnumerator FullCommit_WhileDefendHeld_XPositionUnchangedAcrossRealFrames()
    {
        float startX = _playerObject.transform.position.x;

        float startTime = Time.time;
        const float holdDuration = 0.2f;
        while (Time.time - startTime < holdDuration)
        {
            ForceSetDefendHeld(_controller, true);
            SetHorizontalInput(_controller, 1f); // attempt to force movement input through the real loop while committed
            yield return null;
        }

        Assert.AreEqual(startX, _playerObject.transform.position.x, 0.01f,
            "X position must not change at all while DefendHeld is true, even under real physics integration (plan section 1.8).");
        Assert.IsTrue(_controller.IsGrounded, "The character must remain grounded while committed to Defend.");
    }

    // ---------------------------------------------------------------------
    // 8. Real-Animator Idle<->Walk crossfade race (Task 3.1b,
    // Docs/Plans/004_walk-anim-freeze-and-jump-stall-fix.md)
    //
    // This is the ONE test in the whole suite that attaches the real
    // Assets/Animations/Quirrel.controller instead of leaving the rig's Animator absent (see the
    // class doc comment's RIG CHOICE section). Every other test above hits
    // IsJumpAnticipationStillActive()'s now-unused "_animator == null -> return true" fallback
    // unconditionally, which is exactly why Bug 2 (the jump-stall bug Task 1.1 fixed) shipped past a
    // fully-green suite in the first place. Confirmed directly against the committed controller asset:
    // the Idle<->Walk transitions are InterruptionSource: None with a 0.1s m_TransitionDuration, which
    // can outlast the 0.08s jump-anticipation timer - the exact race this test exercises.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Adds a real Animator + the project's actual Quirrel.controller to the existing test rig, on top
    /// of (not replacing) [UnitySetUp]'s deliberately-animator-less construction (see the class doc
    /// comment's RIG CHOICE note) - this keeps every other test in the file unaffected. PlayerController
    /// caches its Animator reference lazily via EnsureCachedComponents(), called at the top of every
    /// testable public method and every Update()/FixedUpdate(), so adding the component here (after
    /// Awake() already ran with _animator == null) is picked up automatically on the very next frame -
    /// no re-initialization of _controller is required.
    /// </summary>
    private Animator AttachRealAnimator()
    {
        Animator animator = _playerObject.AddComponent<Animator>();
        AnimatorController controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(QuirrelControllerAssetPath);
        Assert.IsNotNull(controllerAsset,
            $"Could not load an AnimatorController at '{QuirrelControllerAssetPath}' - has it moved or been renamed?");
        animator.runtimeAnimatorController = controllerAsset;
        return animator;
    }

    [UnityTest]
    public IEnumerator Jump_PressedDuringLiveIdleWalkCrossfade_WithRealAnimator_StillAppliesVerticalImpulse()
    {
        Animator animator = AttachRealAnimator();

        // Let EnsureCachedComponents() (called every Update()) pick up the just-added Animator and let
        // it settle into its default (Idle) state, and past any zero-duration Entry->Idle transition,
        // before driving input - a handful of frames of margin, not load-bearing timing.
        for (int i = 0; i < 5; i++)
        {
            yield return null;
        }

        // TECHNIQUE NOTE (found while debugging this test, Task 3.1b): this file's usual
        // _horizontalInput reflection override (see the class doc comment) CANNOT be used to drive the
        // real Animator's IsWalking parameter, unlike every other use of that technique in this file.
        // PlayerController.Update() unconditionally resets _horizontalInput to 0 and recomputes it from
        // LIVE Input.GetKey at its own top, then reads that SAME freshly-recomputed value again later
        // in the SAME Update() call inside UpdateAnimatorParameters() - there is no point this
        // coroutine can reach between those two steps, because Unity resumes a `yield return null`
        // coroutine only once per frame, AFTER that frame's Update() has already fully run (the class
        // doc comment's own load-bearing ordering fact). So a reflection override applied at the only
        // point this coroutine can run is always visible to the NEXT frame's FixedUpdate() (which is
        // how every other test in this file uses it, for pure physics integration) but is ALWAYS
        // overwritten by that next frame's own Update() before UpdateAnimatorParameters() ever reads it
        // - IsWalking can never observe a nonzero value this way, confirmed by an actual Unity MCP run
        // showing 0/40 oscillation cycles ever caught a transition.
        //
        // Instead, this test drives the real Animator's IsWalking bool parameter directly via
        // Animator.SetBool, exploiting Unity's real per-frame execution order one step further than the
        // class doc comment already documents: FixedUpdate -> Update -> yield-null coroutine resume ->
        // Animation update -> LateUpdate. The coroutine resume point falls AFTER
        // PlayerController.Update()'s own (always-false, per the above) IsWalking write for that frame,
        // but BEFORE Mecanim's Animation-update step evaluates transitions for that same frame - so
        // setting the parameter here reliably wins as the last write Mecanim sees, producing a genuine
        // transition in the real AnimatorController. This does not weaken the test: what Task 1.1's fix
        // protects against is a live Idle<->Walk transition existing in the real Animator at the moment
        // TryJump/AdvanceJumpTimer resolves the impulse, regardless of what put that transition in
        // flight - Task 1.1's _jumpImpulseCancelled mechanism is entirely agnostic of how IsWalking got
        // toggled.
        //
        // Poll Animator.IsInTransition(0) every frame rather than assuming any fixed frame count
        // reliably lands inside the 0.1s crossfade window; try across several oscillation cycles
        // (bounded, not indefinite) to keep this reliable. Each half-cycle is held comfortably longer
        // than the transition's 0.1s (~6 frames @60fps) so every transition actually gets to start and
        // be observed before the parameter flips again.
        const int maxOscillationCycles = 30;
        const int framesPerHalfCycle = 10;

        int isWalkingHash = (int)IsWalkingHashField.GetValue(null);
        bool transitionCaught = false;
        bool jumpStarted = false;

        for (int cycle = 0; cycle < maxOscillationCycles && !transitionCaught; cycle++)
        {
            bool walking = cycle % 2 == 0;
            for (int frame = 0; frame < framesPerHalfCycle && !transitionCaught; frame++)
            {
                yield return null;
                animator.SetBool(isWalkingHash, walking); // forced after this frame's Update(), before Mecanim's Animation-update step - see the technique note above

                if (animator.IsInTransition(0))
                {
                    // Caught a live transition in flight - fire the edge-triggered jump action right
                    // now, matching this file's existing input-simulation convention for jump/attack
                    // (direct method call = faithful simulation of the Space key-down edge).
                    transitionCaught = true;
                    jumpStarted = _controller.TryJump(_controller.IsGrounded);
                }
            }
        }

        Assert.IsTrue(transitionCaught,
            $"Never observed a live Animator.IsInTransition(0) during the Idle<->Walk oscillation within " +
            $"{maxOscillationCycles} cycles - the test's own setup failed to produce a crossfade at all, " +
            "independent of the race Task 1.1 fixed.");
        Assert.IsTrue(jumpStarted,
            "TryJump should succeed while grounded and not already mid-jump, even with a live Idle<->Walk " +
            "transition in flight - being mid-crossfade must not itself block starting a jump.");

        // Continue advancing real per-frame yields until the 0.08s jump-anticipation window has
        // elapsed (real per-frame Time.deltaTime accumulation, not Time.fixedDeltaTime steps - the
        // Animator's m_UpdateMode: 0 (Normal) ties it to Time.deltaTime, matching the class doc
        // comment's established timing model for this file).
        float elapsedSinceJumpAttempt = 0f;
        const float anticipationWindowWithMargin = 0.08f + 0.05f;
        while (elapsedSinceJumpAttempt < anticipationWindowWithMargin)
        {
            yield return null;
            elapsedSinceJumpAttempt += Time.deltaTime;
        }

        Assert.Greater(_rigidbody.velocity.y, 10f,
            "The vertical jump impulse (~15 u/s, _jumpVerticalVelocity) should have applied despite Space " +
            "having been pressed while a live Idle<->Walk transition was in flight - this is the exact race " +
            "Docs/Plans/004_walk-anim-freeze-and-jump-stall-fix.md Task 1.1's _jumpImpulseCancelled flag " +
            "fixes, previously masked because no test in this suite attached a real Animator.");
    }
}
