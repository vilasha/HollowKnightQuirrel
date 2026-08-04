using UnityEngine;

/// <summary>
/// TEMPORARY read-only diagnostic instrumentation for
/// Docs/Plans/004_walk-anim-freeze-and-jump-stall-fix.md, Task 0.1 (Phase 0).
///
/// Purpose: capture Animator state/parameter data and PlayerController's own public
/// state during a human playtest session (Task 0.2), to diagnose two bugs - a walk
/// animation that freezes on Idle, and a jump that permanently stops working. This
/// component is intentionally removed again by Task 0.2 once its data has been
/// captured; it must never ship.
///
/// STRICTLY READ-ONLY: this script never writes to the Animator, never calls any
/// PlayerController method, and never sets any Animator parameter/state/transition.
/// It only reads already-public/already-accessible state via Debug.Log.
///
/// Throttling (plan's explicit acceptance criterion - a naive per-frame logger could
/// itself perturb the frame timing this diagnostic is trying to observe): logs at
/// most once every <see cref="_logIntervalTicks"/> FixedUpdate ticks, PLUS
/// immediately any tick where a tracked value actually changed since the last log.
/// No string is built at all on a tick that is neither due nor changed.
/// </summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(Animator))]
public class PlayerDiagnosticLogger : MonoBehaviour
{
    /// <summary>Log at least this often even if nothing changed, so a long silent stall is still visible in the log (e.g. Bug 1's "stuck on Idle while moving"). Serialized so a human can retune the sampling rate without recompiling, per this project's tunables convention.</summary>
    [SerializeField]
    private int _logIntervalTicks = 30;

    private PlayerController _playerController;
    private Animator _animator;

    private int _ticksSinceLastLog;

    // Last-logged snapshot, used purely to detect "did anything change" - never written
    // back to the Animator or PlayerController.
    private string _lastLoggedStateName = "";
    private bool _lastLoggedIsInTransition;
    private bool _lastLoggedAnimatorIsGrounded;
    private bool _lastLoggedAnimatorIsWalking;
    private bool _lastLoggedControllerIsGrounded;
    private bool _lastLoggedControllerIsJumpInProgress;
    private bool _lastLoggedControllerIsAttacking;
    private bool _hasLoggedOnce;

    // Known Animator state names (Base Layer, layer 0) this logger resolves the
    // current state against - matches Assets/Animations/Quirrel.controller exactly
    // (plan Task 0.1's literal list). Read-only lookup, never used to set a state.
    private static readonly string[] KnownStateNames =
    {
        "Idle",
        "Walk",
        "JumpAnticipation",
        "JumpRise",
        "JumpFall",
        "Attack",
        "DefendRaise",
        "DefendHold",
        "Hurt",
        "Die",
    };

    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    private void Awake()
    {
        EnsureCachedComponents();
    }

    /// <summary>Idempotent cache of sibling components, same defensive pattern PlayerController itself uses (its EnsureCachedComponents doc comment explains why Awake alone isn't sufficient in all test contexts) - kept consistent for this codebase's existing convention.</summary>
    private void EnsureCachedComponents()
    {
        if (_playerController == null)
        {
            _playerController = GetComponent<PlayerController>();
        }

        if (_animator == null)
        {
            _animator = GetComponent<Animator>();
        }
    }

    private void FixedUpdate()
    {
        EnsureCachedComponents();

        if (_animator == null || _playerController == null)
        {
            return; // Defensive only - [RequireComponent] guarantees both exist in practice.
        }

        _ticksSinceLastLog++;

        string currentStateName = ResolveCurrentStateName();
        bool isInTransition = _animator.IsInTransition(0);
        bool animatorIsGrounded = _animator.GetBool(IsGroundedHash);
        bool animatorIsWalking = _animator.GetBool(IsWalkingHash);
        bool controllerIsGrounded = _playerController.IsGrounded;
        bool controllerIsJumpInProgress = _playerController.IsJumpInProgress;
        bool controllerIsAttacking = _playerController.IsAttacking;

        bool changed = !_hasLoggedOnce
            || currentStateName != _lastLoggedStateName
            || isInTransition != _lastLoggedIsInTransition
            || animatorIsGrounded != _lastLoggedAnimatorIsGrounded
            || animatorIsWalking != _lastLoggedAnimatorIsWalking
            || controllerIsGrounded != _lastLoggedControllerIsGrounded
            || controllerIsJumpInProgress != _lastLoggedControllerIsJumpInProgress
            || controllerIsAttacking != _lastLoggedControllerIsAttacking;

        bool due = _ticksSinceLastLog >= _logIntervalTicks;

        if (!changed && !due)
        {
            return; // Nothing to report yet - skip all string building/allocation this tick.
        }

        Debug.Log(
            "[PlayerDiagnosticLogger] state=" + currentStateName
            + " inTransition=" + isInTransition
            + " anim.IsGrounded=" + animatorIsGrounded
            + " anim.IsWalking=" + animatorIsWalking
            + " ctrl.IsGrounded=" + controllerIsGrounded
            + " ctrl.IsJumpInProgress=" + controllerIsJumpInProgress
            + " ctrl.IsAttacking=" + controllerIsAttacking
            + (changed ? " (changed)" : " (periodic)"));

        _lastLoggedStateName = currentStateName;
        _lastLoggedIsInTransition = isInTransition;
        _lastLoggedAnimatorIsGrounded = animatorIsGrounded;
        _lastLoggedAnimatorIsWalking = animatorIsWalking;
        _lastLoggedControllerIsGrounded = controllerIsGrounded;
        _lastLoggedControllerIsJumpInProgress = controllerIsJumpInProgress;
        _lastLoggedControllerIsAttacking = controllerIsAttacking;
        _hasLoggedOnce = true;
        _ticksSinceLastLog = 0;
    }

    /// <summary>
    /// Resolves the Base Layer's current AnimatorStateInfo to a human-readable name by
    /// checking .IsName(...) against each of the plan's known state names (Task 0.1).
    /// Read-only - .IsName merely queries, it never changes state. Returns "Unknown" if
    /// none match (e.g. mid-transition into a state not in this fixed list, which itself
    /// would be diagnostically interesting and is still visible via inTransition=True).
    /// </summary>
    private string ResolveCurrentStateName()
    {
        AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < KnownStateNames.Length; i++)
        {
            if (stateInfo.IsName(KnownStateNames[i]))
            {
                return KnownStateNames[i];
            }
        }

        return "Unknown";
    }
}
