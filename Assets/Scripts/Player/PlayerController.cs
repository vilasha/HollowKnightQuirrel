using UnityEngine;

/// <summary>
/// Drives player movement and Animator parameters
/// (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Tasks 3.2, 3.3, 3.4a).
///
/// SCOPE SO FAR: Task 3.2 added the asmdef scaffold + horizontal movement. Task 3.3
/// added jump physics, the real grounded-check, jump-related Animator parameters,
/// and the delayed-impulse interrupt-safety plumbing (plan sections 1.4/1.5/1.9).
/// Task 3.4a (this pass) adds the combat/reaction layer: Attack (J), Defend (K),
/// the full-commit gating that freezes movement/jump while Attack/Defend/hit-stun
/// are active (plan section 1.8), and the public Hurt()/Die() trigger API with the
/// guard-flag interrupt resets required by plan section 1.9. Task 3.4b (a separate
/// follow-on) owns the EditMode/PlayMode test suite for this layer - do not add
/// test files here.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    /// <summary>Horizontal move speed in units/sec (plan section 1.5). Same value used for air control by Task 3.3.</summary>
    [SerializeField]
    private float _walkSpeed = 4.5f;

    private Rigidbody2D _rigidbody;
    private SpriteRenderer _spriteRenderer;
    private Animator _animator;

    /// <summary>+1 = last-pressed direction was Right, -1 = Left. Starts facing right per plan section 1.14 (source art only faces screen-right).</summary>
    private float _facingDirection = 1f;

    /// <summary>Raw horizontal input axis for the current frame, read in Update and consumed in FixedUpdate.</summary>
    private float _horizontalInput;

    // ---------------------------------------------------------------------
    // Combat/reaction layer - Attack/Defend/Hurt/Die gating and guard-flag
    // resets (Task 3.4a, plan sections 1.7/1.8/1.9/1.10).
    // ---------------------------------------------------------------------

    /// <summary>
    /// True from the moment AttackTrigger fires until the Quirrel_Attack clip's final-frame Animation
    /// Event calls <see cref="OnAttackAnimationComplete"/> on the normal-completion path, or until
    /// Hurt()/Die() force-clears it on the interrupt path (plan section 1.9 - an Any-State interrupt
    /// skips the Animation Event entirely, so relying on it alone to clear this flag would leave
    /// Attack permanently disabled after a single mid-swing hit). Blocks re-firing AttackTrigger
    /// (plan section 1.8's no-stack guard, defense-in-depth alongside the Animator's own
    /// Can Transition To Self = false) and, via <see cref="IsFullyCommitted"/>, freezes movement/jump
    /// while true.
    /// </summary>
    private bool _isAttacking;

    /// <summary>Read-only view of _isAttacking, for tests and for Task 3.4b's assertions.</summary>
    public bool IsAttacking => _isAttacking;

    /// <summary>
    /// Code-tracked mirror of the Animator's DefendHeld bool parameter (plan section 1.7) - true while
    /// K is held and none of IsDead/hit-stun/!IsGrounded gate it off. Named to match the Animator
    /// parameter exactly, same convention as <see cref="IsGrounded"/>/<see cref="IsDead"/> above.
    /// Recomputed every Update() from live input; also explicitly force-set false by Hurt()/Die() on
    /// interrupt (plan section 1.9 - belt-and-suspenders visual consistency, not load-bearing for
    /// correctness the way _jumpInProgress/_isAttacking's resets are, since this is a continuous read
    /// rather than a one-way latch).
    /// </summary>
    public bool DefendHeld { get; private set; }

    /// <summary>
    /// True while the 0.3s post-Hurt() hit-stun window is active (plan sections 1.9/1.10). Joins
    /// _isAttacking/DefendHeld in the <see cref="IsFullyCommitted"/> family that freezes movement/jump
    /// input. Re-entrant Hurt() calls while this is already true RESTART _hurtStunTimeRemaining
    /// (extending the stun) rather than being ignored or queued - plan section 1.10, a deliberate
    /// design decision. There is only ever one active stun timer.
    /// </summary>
    private bool _isHurtStunned;

    /// <summary>Read-only view of _isHurtStunned, for tests and for Task 3.4b's assertions.</summary>
    public bool IsHurtStunned => _isHurtStunned;

    /// <summary>Seconds remaining on the current hit-stun window while _isHurtStunned is true.</summary>
    private float _hurtStunTimeRemaining;

    /// <summary>Hit-stun duration in seconds (plan section 1.9/1.10: fixed 0.3s). Serialized so design/QA can retune without recompiling.</summary>
    [SerializeField]
    private float _hurtStunDuration = 0.3f;

    /// <summary>
    /// True while the character is fully committed to Attack, Defend, or hit-stun recovery (plan
    /// section 1.8's "commit in place" rule, extended to Hurt's stun window per plan section 1.9's
    /// "same mechanism/flag family" note). ApplyHorizontalMovement and TryJump both check this and
    /// freeze while it's true; UpdateAnimatorParameters' IsWalking computation reads this same state
    /// too - replaces Task 3.3's IsCommittedToAttackOrDefendPlaceholder field entirely (not merely
    /// renamed - this is real, derived logic now).
    /// </summary>
    public bool IsFullyCommitted => _isAttacking || DefendHeld || _isHurtStunned;

    // ---------------------------------------------------------------------
    // Jump physics, grounded-check, interrupt safety (Task 3.3, plan sections
    // 1.4 / 1.5 / 1.7 / 1.9).
    // ---------------------------------------------------------------------

    /// <summary>Vertical impulse applied to Rigidbody2D.velocity.y when the jump-anticipation timer elapses (plan section 1.5, v0 = 15 u/s exactly).</summary>
    [SerializeField]
    private float _jumpVerticalVelocity = 15f;

    /// <summary>
    /// Seconds between JumpTrigger firing and the vertical impulse applying (plan section 1.4).
    /// Must match the Quirrel_JumpAnticipation clip's length exactly (plan section 1.6) - do not
    /// change one without the other.
    /// </summary>
    [SerializeField]
    private float _jumpAnticipationDuration = 0.08f;

    /// <summary>Defensive terminal fall-speed clamp on Rigidbody2D.velocity.y (plan section 1.5). Not reached within a normal symmetric jump arc.</summary>
    [SerializeField]
    private float _maxFallSpeed = -20f;

    /// <summary>Radius of the OverlapCircle grounded-check query (plan sections 1.5/3.3), centered at transform.position + _groundCheckOffset.</summary>
    [SerializeField]
    private float _groundCheckRadius = 0.1f;

    /// <summary>
    /// Local offset of the grounded-check circle from transform.position. Defaults to zero because the
    /// character's sprites use a Bottom pivot (plan section 1.1) - the transform origin already sits at
    /// the character's feet, so no downward offset is needed by default.
    /// </summary>
    [SerializeField]
    private Vector2 _groundCheckOffset = Vector2.zero;

    /// <summary>
    /// Physics layer the grounded-check queries against. Left unset (0) in the Inspector, this falls
    /// back to LayerMask.GetMask("Ground") in Awake (Task 3.1's layer 9) - still overridable per-prefab
    /// if ever needed.
    /// </summary>
    [SerializeField]
    private LayerMask _groundLayerMask;

    /// <summary>
    /// True while airborne-from-a-jump, from the moment JumpTrigger fires until landing. Guards against
    /// re-triggering Space mid-jump (plan section 1.4), independent of IsGrounded - IsGrounded alone
    /// stays true throughout the 0.08s anticipation window and would not otherwise block a second press
    /// from re-entering TryJump.
    ///
    /// Clearing: Task 3.3 (this file, right now) clears it ONLY on the normal landing edge (IsGrounded
    /// false -> true, see FixedUpdate). Task 3.4a's Hurt()/Die() additionally clear it on interrupt
    /// (plan section 1.9) by writing this field directly (same class) - do NOT duplicate that reset
    /// here. Plan section 1.9's race-safety note: both clear-paths only ever write false; the only
    /// writer of true is TryJump, which itself requires the flag to currently be false - this makes the
    /// two clear-paths idempotent and commutative regardless of execution order. Preserve this property
    /// if either clear-path is ever touched again.
    /// </summary>
    private bool _jumpInProgress;

    /// <summary>Read-only view of _jumpInProgress, for tests and for Task 3.4a's gating logic.</summary>
    public bool IsJumpInProgress => _jumpInProgress;

    /// <summary>
    /// True from JumpTrigger firing until the 0.08s anticipation window resolves one way or another
    /// (impulse applied, or cancelled by AdvanceJumpTimer's checks). Distinct from _jumpInProgress,
    /// which persists for the entire airborne phase, not just the anticipation beat.
    /// </summary>
    private bool _jumpImpulseTimerActive;

    /// <summary>Seconds remaining on the jump-anticipation timer while _jumpImpulseTimerActive is true.</summary>
    private float _jumpTimeRemaining;

    /// <summary>
    /// Real grounded-check result (OverlapCircle against the Ground layer, computed in FixedUpdate -
    /// plan section 1.5: "grounded-check belongs alongside the physics step it gates"). Replaces Task
    /// 3.2's IsGroundedPlaceholder. Defaults true to match the character's grounded spawn state (plan
    /// section 1.15) for the brief window before the first FixedUpdate runs.
    /// </summary>
    public bool IsGrounded { get; private set; } = true;

    /// <summary>
    /// True once the character has died. Defaults false. Public get/set (not internal - this project's
    /// asmdefs have no InternalsVisibleTo, so an internal member would be invisible to the EditMode test
    /// assembly) so that Task 3.4a's Die() can set it directly once it exists, and so this task's own
    /// EditMode test can exercise the delayed-impulse cancellation path before Die() exists. Gates the
    /// delayed jump-impulse callback (plan sections 1.4/1.9) together with the JumpAnticipation
    /// Animator-state check - either failing no-ops the impulse, covering both the Die case and a
    /// Hurt-interrupt case with one check, by design.
    /// </summary>
    public bool IsDead { get; set; }

    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private static readonly int JumpTriggerHash = Animator.StringToHash("JumpTrigger");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");
    private static readonly int AttackTriggerHash = Animator.StringToHash("AttackTrigger");
    private static readonly int DefendHeldHash = Animator.StringToHash("DefendHeld");
    private static readonly int HurtTriggerHash = Animator.StringToHash("HurtTrigger");
    private static readonly int HurtRecoveryTriggerHash = Animator.StringToHash("HurtRecoveryTrigger");
    private static readonly int DieTriggerHash = Animator.StringToHash("DieTrigger");
    private static readonly int IsDeadHash = Animator.StringToHash("IsDead");

    /// <summary>
    /// The Animator state name the delayed jump-impulse cancellation check looks for (plan sections
    /// 1.4/1.9). Exact string match required - a real coupling risk the plan flags for Task 4.1's
    /// Animator-state contract test. Do not rename without updating both the Animator (Task 3.5/3.6)
    /// and that contract test.
    /// </summary>
    private const string JumpAnticipationStateName = "JumpAnticipation";

    private void Awake()
    {
        EnsureCachedComponents();

        if (_groundLayerMask.value == 0)
        {
            // Not overridden in the Inspector - resolve by name (Task 3.1's layer 9) rather than
            // leaving the grounded-check permanently querying an empty mask.
            _groundLayerMask = LayerMask.GetMask("Ground");
        }
    }

    /// <summary>
    /// Caches Rigidbody2D/SpriteRenderer/Animator references. Called from Awake() for the normal
    /// runtime/Play-Mode path, and defensively re-called at the top of every public method that's
    /// directly testable without a full lifecycle (ApplyHorizontalMovement, TryJump, AdvanceJumpTimer)
    /// - Unity does NOT invoke Awake() on a plain MonoBehaviour (no [ExecuteAlways]) outside Play Mode,
    /// so EditMode tests that AddComponent this class and call those methods directly would otherwise
    /// NullReferenceException on _rigidbody. Idempotent (no-ops once cached) so this costs nothing at
    /// runtime beyond the first call.
    /// </summary>
    private void EnsureCachedComponents()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (_animator == null)
        {
            _animator = GetComponent<Animator>(); // may legitimately stay null until Task 3.8 attaches one - all uses below null-check
        }
    }

    private void Update()
    {
        EnsureCachedComponents();

        // Full-commit gating (plan section 1.8, extended to hit-stun per section 1.9's "same
        // mechanism/flag family" note): while attacking, defending, or hurt-stunned, horizontal
        // input is not read at all - the character does not move and facing simply holds at
        // whatever it last was (plan section 1.14: facing persists through Attack/Defend/Hurt).
        // IsDead permanently blocks it too (plan section 1.7/"Die" acceptance criteria).
        _horizontalInput = 0f;
        if (!IsDead && !IsFullyCommitted)
        {
            if (Input.GetKey(KeyCode.A))
            {
                _horizontalInput -= 1f;
            }

            if (Input.GetKey(KeyCode.D))
            {
                _horizontalInput += 1f;
            }

            if (_horizontalInput != 0f)
            {
                _facingDirection = Mathf.Sign(_horizontalInput);
            }
        }

        // GetKeyDown (edge-triggered) must be read in Update, not FixedUpdate, or presses can be
        // missed between fixed steps (plan section 1.5). IsGrounded here is the value most recently
        // computed by FixedUpdate - a one-frame lag against a physics value read from Update is the
        // standard, acceptable pattern. TryAttack/TryJump are called unconditionally on their key's
        // edge and self-guard internally (same convention already used for TryJump in Task 3.3) -
        // reading GetKeyDown every frame is safe regardless of gating state, it does not "consume"
        // the edge.
        if (Input.GetKeyDown(KeyCode.J))
        {
            TryAttack(IsGrounded);
        }

        // DefendHeld is a continuous read of live input, not an edge (plan section 1.7) - gated off
        // while IsDead or !IsGrounded (this task's literal acceptance criteria) and also while
        // hurt-stunned (plan section 1.9's Hurt() spec: "movement/attack/defend/jump input is
        // ignored" during the stun window).
        DefendHeld = !IsDead && !_isHurtStunned && IsGrounded && Input.GetKey(KeyCode.K);

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryJump(IsGrounded);
        }

        UpdateFacing();
        UpdateAnimatorParameters();
    }

    private void FixedUpdate()
    {
        ApplyHorizontalMovement(_horizontalInput);

        bool previouslyGrounded = IsGrounded;
        IsGrounded = CheckGrounded();
        if (IsGrounded && !previouslyGrounded)
        {
            // Normal landing edge only (plan section 1.4, this task's own acceptance criteria).
            // The interrupt-path clear belongs to Task 3.4a's Hurt()/Die() - see _jumpInProgress's
            // doc comment and plan section 1.9. Do not also clear it here on any other condition.
            _jumpInProgress = false;
        }

        AdvanceJumpTimer(Time.fixedDeltaTime);
        AdvanceHurtStunTimer(Time.fixedDeltaTime);
        ClampFallVelocity();
        UpdatePhysicsAnimatorParameters();
    }

    /// <summary>
    /// Applies horizontal velocity for the given input axis (-1..1) to the Rigidbody2D.
    /// Public so EditMode tests can drive it directly without needing a full FixedUpdate/Play Mode cycle.
    ///
    /// CRITICAL: writes ONLY the .x component and preserves whatever .y currently is.
    /// Task 3.3 adds vertical jump velocity to this same Rigidbody2D on the same field;
    /// stomping .y here (e.g. `new Vector2(desiredX, 0)`) would silently break jumping.
    ///
    /// Full-commit gating (Task 3.4a, plan section 1.8): while IsDead or IsFullyCommitted
    /// (attacking/defending/hurt-stunned), the desired X velocity is forced to zero rather than
    /// merely left unwritten - a residual nonzero velocity.x carried over from the frame before
    /// the character committed would otherwise keep the Rigidbody2D drifting via physics
    /// integration even though no new input is being applied, letting the character walk off the
    /// ground's edge mid-attack/mid-block. Called every FixedUpdate regardless of gating state
    /// (matching the existing call site in FixedUpdate()), so this method must self-guard.
    /// </summary>
    public void ApplyHorizontalMovement(float horizontalInput)
    {
        EnsureCachedComponents();

        bool locked = IsDead || IsFullyCommitted;
        float desiredX = locked ? 0f : horizontalInput * _walkSpeed;
        Vector2 velocity = _rigidbody.velocity;
        _rigidbody.velocity = new Vector2(desiredX, velocity.y);
    }

    /// <summary>
    /// Attempts to start a jump. Takes isGrounded as a parameter (rather than reading the IsGrounded
    /// property internally) so EditMode tests can drive it directly without a live physics scene -
    /// the same convention as ApplyHorizontalMovement above. The real call site is Update(), passing
    /// the cached IsGrounded property on a Space GetKeyDown edge.
    ///
    /// On success: fires JumpTrigger immediately and starts the 0.08s anticipation timer. Returns
    /// whether the jump actually started, so tests (and callers) can assert on it.
    ///
    /// Guard: ignored whenever _jumpInProgress is already true, independent of isGrounded (plan
    /// section 1.4) - IsGrounded alone stays true through the whole anticipation window and would
    /// not otherwise block a second press from re-entering this method. Also ignored while IsDead
    /// or IsFullyCommitted (attacking/defending/hurt-stunned) - plan section 1.8's full-commit
    /// gating and the Die() input-lock requirement.
    /// </summary>
    public bool TryJump(bool isGrounded)
    {
        EnsureCachedComponents();

        if (!isGrounded || _jumpInProgress || IsDead || IsFullyCommitted)
        {
            return false;
        }

        _jumpInProgress = true;
        _jumpImpulseTimerActive = true;
        _jumpTimeRemaining = _jumpAnticipationDuration;

        if (_animator != null)
        {
            _animator.SetTrigger(JumpTriggerHash);
        }

        return true;
    }

    /// <summary>
    /// Ticks the jump-anticipation timer down by deltaTime; when it elapses, resolves the delayed
    /// impulse (applies it or cancels it - see ApplyJumpImpulseIfValid). Takes deltaTime as a
    /// parameter (rather than reading Time.fixedDeltaTime internally) for the same EditMode-
    /// testability reason as TryJump/ApplyHorizontalMovement. The real call site is FixedUpdate(),
    /// passing Time.fixedDeltaTime.
    /// </summary>
    public void AdvanceJumpTimer(float deltaTime)
    {
        EnsureCachedComponents();

        if (!_jumpImpulseTimerActive)
        {
            return;
        }

        _jumpTimeRemaining -= deltaTime;
        if (_jumpTimeRemaining > 0f)
        {
            return;
        }

        _jumpImpulseTimerActive = false; // window resolved (applied or cancelled) exactly once - never fires twice
        ApplyJumpImpulseIfValid();
    }

    // ---------------------------------------------------------------------
    // Combat/reaction layer methods (Task 3.4a, plan sections 1.7/1.8/1.9/1.10).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Attempts to start an Attack. Takes isGrounded as a parameter (rather than reading IsGrounded
    /// internally) for the same EditMode-testability reason as TryJump. Returns whether the attack
    /// actually started.
    ///
    /// Gated off (no-op) while DefendHeld, IsDead, !isGrounded, _isAttacking (no-stack guard, plan
    /// section 1.8 - defense-in-depth alongside the Animator's own Can Transition To Self = false),
    /// or hurt-stunned (plan section 1.9's Hurt() spec: attack input is ignored during the stun
    /// window).
    /// </summary>
    public bool TryAttack(bool isGrounded)
    {
        EnsureCachedComponents();

        if (IsDead || DefendHeld || !isGrounded || _isAttacking || _isHurtStunned)
        {
            return false;
        }

        _isAttacking = true;

        if (_animator != null)
        {
            _animator.SetTrigger(AttackTriggerHash);
        }

        return true;
    }

    /// <summary>
    /// Called via a Unity Animation Event on the Quirrel_Attack clip's final frame (t=0.1875s,
    /// Task 3.4a) on the NORMAL COMPLETION path only. Clears _isAttacking so a new AttackTrigger can
    /// fire. If Hurt()/Die() interrupts mid-swing, this event is never reached (an Any-State
    /// transition skips straight past it) - that interrupt path clears _isAttacking itself instead
    /// (see Hurt()/Die() below and plan section 1.9). Do not add a competing reset here for the
    /// interrupt case; the two clear-paths are already race-free by construction (plan section
    /// 1.9's race-safety note) as long as neither ever writes `true`.
    /// </summary>
    public void OnAttackAnimationComplete()
    {
        EnsureCachedComponents();
        _isAttacking = false;
    }

    /// <summary>
    /// Ticks the hit-stun timer down by deltaTime; when it elapses uninterrupted, fires
    /// HurtRecoveryTrigger exactly once for the completed stun EPISODE (plan section 1.10) - not
    /// once per individual Hurt() call, since a re-entrant Hurt() call restarts
    /// _hurtStunTimeRemaining (see Hurt() below) without touching _isHurtStunned itself, so this
    /// method's "elapsed" branch is only reached once the LAST restart's window has actually run
    /// out. Takes deltaTime as a parameter for the same EditMode-testability reason as
    /// AdvanceJumpTimer/TryJump/ApplyHorizontalMovement. Real call site is FixedUpdate(), passing
    /// Time.fixedDeltaTime.
    /// </summary>
    public void AdvanceHurtStunTimer(float deltaTime)
    {
        EnsureCachedComponents();

        if (!_isHurtStunned)
        {
            return;
        }

        _hurtStunTimeRemaining -= deltaTime;
        if (_hurtStunTimeRemaining > 0f)
        {
            return;
        }

        _isHurtStunned = false; // episode resolved exactly once - restores normal input handling
        if (_animator != null)
        {
            _animator.SetTrigger(HurtRecoveryTriggerHash);
        }
    }

    /// <summary>
    /// Public trigger API for an external combat/health system to signal the character was hit. No
    /// system like that exists yet in this project (plan section "Explicitly Out of Scope") - this
    /// is intentionally just a trigger API for a future system to call, per the plan.
    ///
    /// No-ops entirely if IsDead (plan section 1.7's HurtTrigger gating - a dead character cannot be
    /// hurt again). Otherwise: fires HurtTrigger, and starts/restarts a fixed
    /// <see cref="_hurtStunDuration"/> (0.3s) hit-stun window during which movement/attack/defend/
    /// jump input is ignored. Re-entrant calls while a stun window is ALREADY active RESTART the
    /// timer (extending the stun) rather than being ignored or queued - plan section 1.10, a
    /// deliberate design decision, not a bug to "fix" toward ignoring re-entrant calls.
    ///
    /// CRITICAL (plan section 1.9, found via two full review rounds): unconditionally resets
    /// _jumpInProgress and _isAttacking to false the moment this interrupt is accepted (i.e.
    /// whenever this method isn't a no-op due to IsDead) - NOT only when a jump/attack happened to
    /// be in progress. A guard flag that's only ever cleared by "its owning state finishing
    /// naturally" gets stuck permanently true if Hurt() interrupts that state first, since Hurt is
    /// an Any-State transition that can cut in before the owning state's normal completion signal
    /// fires - silently disabling jump or attack for the rest of the session after a single hit.
    /// See _jumpInProgress's doc comment for the full reasoning. Also explicitly force-sets
    /// DefendHeld false (belt-and-suspenders visual consistency - not required for correctness,
    /// since DefendHeld is a continuous read, not a one-way latch).
    /// </summary>
    public void Hurt()
    {
        EnsureCachedComponents();

        if (IsDead)
        {
            return;
        }

        // Unconditional guard-flag resets at the moment the interrupt is accepted (plan section
        // 1.9) - harmless when a flag is already false, and avoids needing to track which state was
        // actually interrupted.
        _jumpInProgress = false;
        _isAttacking = false;
        DefendHeld = false;
        if (_animator != null)
        {
            _animator.SetBool(DefendHeldHash, false);
        }

        // Re-entrant calls restart the window rather than stacking a second timer (plan section
        // 1.10) - there is only ever one active _hurtStunTimeRemaining.
        _isHurtStunned = true;
        _hurtStunTimeRemaining = _hurtStunDuration;

        if (_animator != null)
        {
            _animator.SetTrigger(HurtTriggerHash);
        }
    }

    /// <summary>
    /// Public trigger API for an external combat/health system to signal the character died. Sets
    /// IsDead permanently true (plan section 1.7) - from this point on ALL movement/jump/attack/
    /// defend/hurt input is ignored for the remainder of the scene's lifetime (enforced in Update()
    /// and inside each Try*/ApplyHorizontalMovement/Hurt() method, which all check IsDead directly).
    ///
    /// Applies the exact same explicit guard-flag resets as Hurt() (plan section 1.9) for symmetry
    /// between the two interrupt paths - moot for future input once IsDead blocks everything, but
    /// keeps both interrupt paths structurally identical rather than silently diverging.
    /// </summary>
    public void Die()
    {
        EnsureCachedComponents();

        _jumpInProgress = false;
        _isAttacking = false;
        DefendHeld = false;
        _isHurtStunned = false; // Die outranks Hurt - no point leaving a stun window "active" under a permanent death lock.

        if (_animator != null)
        {
            _animator.SetBool(DefendHeldHash, false);
            _animator.SetTrigger(DieTriggerHash);
        }

        IsDead = true;

        if (_animator != null)
        {
            _animator.SetBool(IsDeadHash, true);
        }
    }

    /// <summary>
    /// Applies the vertical jump impulse, unless cancelled (plan sections 1.4/1.9). Cancellation
    /// covers both the Die case (IsDead) and a Hurt-interrupt case (Animator moved off
    /// JumpAnticipation via an Any-State transition) with a single check by design - do not
    /// special-case Die vs Hurt separately.
    ///
    /// CRITICAL: writes ONLY the .y component and preserves whatever .x currently is - the mirror of
    /// ApplyHorizontalMovement's .y-preservation requirement, applied to the other axis. Horizontal
    /// movement is not frozen during the anticipation window, so .x may legitimately be nonzero here.
    /// </summary>
    private void ApplyJumpImpulseIfValid()
    {
        if (IsDead)
        {
            return;
        }

        if (!IsJumpAnticipationStillActive())
        {
            return;
        }

        Vector2 velocity = _rigidbody.velocity;
        _rigidbody.velocity = new Vector2(velocity.x, _jumpVerticalVelocity);
    }

    /// <summary>
    /// True if the Animator's current state (layer 0) is still JumpAnticipation, OR if there's no
    /// Animator/AnimatorController wired yet to check (e.g. EditMode tests, or before Task 3.8
    /// attaches one) - in that case nothing could have interrupted the jump, so don't block it.
    /// </summary>
    private bool IsJumpAnticipationStillActive()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null)
        {
            return true;
        }

        return _animator.GetCurrentAnimatorStateInfo(0).IsName(JumpAnticipationStateName);
    }

    /// <summary>
    /// Real grounded-check query: OverlapCircle against the Ground layer (Task 3.1, layer 9),
    /// centered at transform.position + _groundCheckOffset. Called from FixedUpdate only (plan
    /// section 1.5 - grounded-check belongs alongside the physics step it gates).
    /// </summary>
    private bool CheckGrounded()
    {
        Vector2 checkPosition = (Vector2)transform.position + _groundCheckOffset;
        return Physics2D.OverlapCircle(checkPosition, _groundCheckRadius, _groundLayerMask) != null;
    }

    /// <summary>Defensive terminal-fall clamp (plan section 1.5). Preserves .x, same discipline as the other velocity writes in this file.</summary>
    private void ClampFallVelocity()
    {
        Vector2 velocity = _rigidbody.velocity;
        if (velocity.y < _maxFallSpeed)
        {
            _rigidbody.velocity = new Vector2(velocity.x, _maxFallSpeed);
        }
    }

    /// <summary>Sets the physics-driven Animator parameters (plan section 1.7) each FixedUpdate, alongside the physics step that computes them.</summary>
    private void UpdatePhysicsAnimatorParameters()
    {
        if (_animator == null)
        {
            return; // No Animator/Controller attached yet - no-op rather than throw.
        }

        _animator.SetBool(IsGroundedHash, IsGrounded);
        _animator.SetFloat(VerticalVelocityHash, _rigidbody.velocity.y);
    }

    private void UpdateFacing()
    {
        if (_spriteRenderer == null)
        {
            return;
        }

        _spriteRenderer.flipX = _facingDirection < 0f;
    }

    private void UpdateAnimatorParameters()
    {
        if (_animator == null)
        {
            return; // No Animator/Controller attached yet (Task 3.8 attaches one) - no-op rather than throw.
        }

        bool isWalking = IsGrounded
            && !IsDead
            && _horizontalInput != 0f
            && !IsFullyCommitted;

        _animator.SetBool(IsWalkingHash, isWalking);
        _animator.SetBool(DefendHeldHash, DefendHeld);
    }
}
