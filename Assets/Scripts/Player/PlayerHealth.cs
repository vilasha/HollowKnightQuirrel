using UnityEngine;

/// <summary>
/// Quirrel's Mask-based health data model (Docs/Plans/010_health-mask-system.md, Tasks 1.2/1.3).
///
/// A new sibling MonoBehaviour on the Player GameObject, inside the existing Quirrel.Player assembly
/// (Decision 1) - not merged into <see cref="PlayerController"/>, which is already large and
/// regression-sensitive. Internal health storage is <c>int</c> quarter-masks, not <c>float</c> masks
/// (Decision 2): a standard hit is <see cref="QuartersPerMask"/> (4) quarter-masks; the plan's QA test
/// hazard deals exactly 1.
///
/// <see cref="ApplyDamage"/> never reimplements <see cref="PlayerController.Hurt"/>/
/// <see cref="PlayerController.Die"/>'s existing guard-flag-reset/hit-stun contracts - this component
/// calls into them instead. Mask Shards (<see cref="AddMaskShard"/>) raise <see cref="MaxMasks"/> only,
/// never <see cref="CurrentQuarterMasks"/> directly (Decision 3) - the user's explicit instruction.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerHealth : MonoBehaviour
{
    /// <summary>
    /// Hard design maximum for <see cref="MaxMasks"/> (Decision 7) - a compile-time constant, not a
    /// serialized/tunable field. A serialized tunable here, paired with MaskHUD's hardcoded 9-slot
    /// Image[] array, would let a designer silently break the HUD by raising the cap above 9 in the
    /// Inspector with no error. Read by both PlayerHealth and the future MaskHUD.
    /// </summary>
    public const int MaxMasksCap = 9;

    /// <summary>
    /// Quarter-masks per whole Mask (Decision 2) - kept as a separately-named unit constant from
    /// <see cref="_shardsPerMask"/> even though both happen to be 4, so a future design change to one
    /// never silently drags the other along.
    /// </summary>
    public const int QuartersPerMask = 4;

    /// <summary>Starting whole Masks at spawn. Serialized so design/QA can retune without recompiling.</summary>
    [SerializeField]
    private int _startingMasks = 4;

    /// <summary>Mask Shards required to raise <see cref="MaxMasks"/> by one (Decision 3).</summary>
    [SerializeField]
    private int _shardsPerMask = 4;

    /// <summary>
    /// Seconds of damage-invulnerability after a non-fatal hit (Decision 6) - separate, forward-looking
    /// i-frame infrastructure distinct from <see cref="PlayerController"/>'s existing 0.3s hit-stun
    /// window. Serialized so design/QA can retune without recompiling.
    /// </summary>
    [SerializeField]
    private float _damageInvulnerabilityDuration = 1.0f;

    private PlayerController _playerController;

    private int _maxMasks;
    private int _currentQuarterMasks;
    private int _maskShardProgress;
    private bool _isInvulnerable;
    private float _invulnerabilityTimeRemaining;

    /// <summary>
    /// Guards the one-time value-field init block in <see cref="EnsureCachedComponents"/> - a separate
    /// bool rather than checking a field like <c>_maxMasks == 0</c>, because 0 is also a legitimate,
    /// reachable runtime value for e.g. <see cref="_currentQuarterMasks"/> (a dead character) that must
    /// never be silently reset back to full health by a later defensive re-init call.
    /// </summary>
    private bool _isValueStateInitialized;

    /// <summary>
    /// Current maximum whole Masks, raised only by <see cref="AddMaskShard"/> (capped at
    /// <see cref="MaxMasksCap"/>). Calls <see cref="EnsureCachedComponents"/> before reading the backing
    /// field - unlike <see cref="PlayerController"/>'s own simple auto-properties (which have correct
    /// field-initializer defaults that hold even before Awake), this component's starting value depends
    /// on the serialized <see cref="_startingMasks"/> tunable and can't be a static field initializer, so
    /// every public read - not just the mutating methods - must be able to trigger the same one-time
    /// lazy init a bare AddComponent (no Awake) would otherwise skip entirely.
    /// </summary>
    public int MaxMasks
    {
        get
        {
            EnsureCachedComponents();
            return _maxMasks;
        }
    }

    /// <summary>Current health in quarter-masks. See <see cref="MaxMasks"/>'s doc comment for why this reads through <see cref="EnsureCachedComponents"/>.</summary>
    public int CurrentQuarterMasks
    {
        get
        {
            EnsureCachedComponents();
            return _currentQuarterMasks;
        }
    }

    /// <summary><see cref="MaxMasks"/> expressed in quarter-masks - the current full-health ceiling.</summary>
    public int MaxQuarterMasks => MaxMasks * QuartersPerMask; // routes through the MaxMasks property (not _maxMasks directly) so this also self-initializes

    /// <summary>Mask Shards collected toward the next <see cref="MaxMasks"/> increase, in [0, <see cref="ShardsPerMask"/>). See <see cref="MaxMasks"/>'s doc comment for why this reads through <see cref="EnsureCachedComponents"/>.</summary>
    public int MaskShardProgress
    {
        get
        {
            EnsureCachedComponents();
            return _maskShardProgress;
        }
    }

    /// <summary>Mask Shards required to raise <see cref="MaxMasks"/> by one.</summary>
    public int ShardsPerMask => _shardsPerMask;

    /// <summary>True while the post-damage invulnerability window (Decision 6) is active. See <see cref="MaxMasks"/>'s doc comment for why this reads through <see cref="EnsureCachedComponents"/>.</summary>
    public bool IsInvulnerable
    {
        get
        {
            EnsureCachedComponents();
            return _isInvulnerable;
        }
    }

    /// <summary>
    /// Fired once per successful <see cref="ApplyDamage"/>/<see cref="Heal"/>/<see cref="FullHeal"/>/
    /// <see cref="AddMaskShard"/> call that actually changes state - zero times on any no-op call.
    /// Consumed by MaskHUD to refresh its pip display - AddMaskShard must raise this too, not just the
    /// two health-changing paths, because Task 4.4's acceptance criteria require the HUD's pip row to
    /// grow live the instant a 4th shard raises MaxMasks, and MaskHUD's Refresh() is driven entirely by
    /// this single event (there is no separate "shard collected" notification).
    /// </summary>
    public event System.Action OnHealthChanged;

    private void Awake()
    {
        EnsureCachedComponents();
    }

    private void OnEnable()
    {
        EnsureCachedComponents();

        if (_playerController != null)
        {
            _playerController.Rested += FullHeal;
            // Docs/Plans/011_death-and-respawn.md, Task 3.1: Respawned fires only after
            // PlayerController.IsDead has already been set back to false (that ordering is
            // load-bearing - see Respawn()'s own doc comment), so FullHeal here is never blocked
            // by Heal()'s own IsDead gate.
            _playerController.Respawned += FullHeal;
        }
    }

    private void OnDisable()
    {
        if (_playerController != null)
        {
            _playerController.Rested -= FullHeal;
            _playerController.Respawned -= FullHeal;
        }
    }

    private void FixedUpdate()
    {
        AdvanceInvulnerabilityTimer(Time.fixedDeltaTime);
    }

    /// <summary>
    /// Caches the PlayerController reference AND performs the one-time value-field init (formerly
    /// Awake()-only) that establishes starting health state. Called from Awake() for the normal
    /// runtime/Play-Mode path, and defensively re-called at the top of every publicly-testable method -
    /// Unity does NOT invoke Awake() on a plain MonoBehaviour (no [ExecuteAlways]) outside Play Mode, so
    /// EditMode tests that AddComponent this class and call those methods directly would otherwise both
    /// NullReferenceException on _playerController AND silently observe zeroed-out health state
    /// (MaxMasks/CurrentQuarterMasks all 0) instead of the real starting values - the same class of gap
    /// PlayerController's own EnsureCachedComponents exists to close for its own cached component
    /// references. Both halves here are independently idempotent (no-ops once each has already run),
    /// matching that same defensive-re-init idiom.
    /// </summary>
    private void EnsureCachedComponents()
    {
        if (_playerController == null)
        {
            _playerController = GetComponent<PlayerController>();
        }

        if (!_isValueStateInitialized)
        {
            _isValueStateInitialized = true;
            _maxMasks = _startingMasks;
            _currentQuarterMasks = MaxQuarterMasks;
            _maskShardProgress = 0;
            _isInvulnerable = false;
            _invulnerabilityTimeRemaining = 0f;
        }
    }

    /// <summary>
    /// Generic public damage API (Decision 5) - not a fixed "standard hit" method, so a future
    /// enemy/hazard system can call it unmodified with any quarter-mask amount.
    ///
    /// No-ops (returns false) if <see cref="PlayerController.IsDead"/>, <see cref="IsInvulnerable"/>,
    /// or <paramref name="quarterMasks"/> is zero/negative. Otherwise clamps
    /// <see cref="CurrentQuarterMasks"/> down (floor 0), raises <see cref="OnHealthChanged"/>, and
    /// either calls <see cref="PlayerController.Die"/> (health reaches exactly 0) or
    /// <see cref="PlayerController.Hurt"/> plus starts the invulnerability window (non-fatal hit) -
    /// mirrors PlayerController.Hurt()'s own no-op-while-IsDead convention. Never reimplements
    /// Hurt()/Die()'s guard-flag-reset/hit-stun logic itself.
    /// </summary>
    public bool ApplyDamage(int quarterMasks)
    {
        EnsureCachedComponents();

        if (_playerController.IsDead || _isInvulnerable || quarterMasks <= 0)
        {
            return false;
        }

        _currentQuarterMasks = Mathf.Max(0, _currentQuarterMasks - quarterMasks);
        OnHealthChanged?.Invoke();

        if (_currentQuarterMasks == 0)
        {
            _playerController.Die();
        }
        else
        {
            _playerController.Hurt();
            _isInvulnerable = true;
            _invulnerabilityTimeRemaining = _damageInvulnerabilityDuration;
        }

        return true;
    }

    /// <summary>
    /// Ticks the damage-invulnerability window down by deltaTime; when it elapses, clears
    /// <see cref="IsInvulnerable"/> exactly once. Takes deltaTime as a parameter (rather than reading
    /// Time.fixedDeltaTime internally) for the same EditMode-testability reason as
    /// PlayerController.AdvanceHurtStunTimer - the real call site is FixedUpdate(), passing
    /// Time.fixedDeltaTime.
    /// </summary>
    public void AdvanceInvulnerabilityTimer(float deltaTime)
    {
        EnsureCachedComponents();

        if (!_isInvulnerable)
        {
            return;
        }

        _invulnerabilityTimeRemaining -= deltaTime;
        if (_invulnerabilityTimeRemaining > 0f)
        {
            return;
        }

        _isInvulnerable = false; // window resolved exactly once - a subsequent ApplyDamage can succeed again
    }

    /// <summary>
    /// Generic public heal API (Decision 5), mirroring <see cref="ApplyDamage"/> on the regen side.
    /// No-ops (returns false) if <see cref="PlayerController.IsDead"/>, <paramref name="quarterMasks"/>
    /// is zero/negative, or already at <see cref="MaxQuarterMasks"/> (Decision 10). Otherwise clamps
    /// <see cref="CurrentQuarterMasks"/> up (ceiling at <see cref="MaxQuarterMasks"/>) and raises
    /// <see cref="OnHealthChanged"/>.
    /// </summary>
    public bool Heal(int quarterMasks)
    {
        EnsureCachedComponents();

        if (_playerController.IsDead || quarterMasks <= 0 || _currentQuarterMasks >= MaxQuarterMasks)
        {
            return false;
        }

        _currentQuarterMasks = Mathf.Min(MaxQuarterMasks, _currentQuarterMasks + quarterMasks);
        OnHealthChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Restores <see cref="CurrentQuarterMasks"/> to exactly <see cref="MaxQuarterMasks"/> (Decision 4)
    /// - not a duplicated code path, just <see cref="Heal"/> with the full-heal amount (Decision 10).
    /// Called by <see cref="MaskPickup"/> and subscribed directly to <see cref="PlayerController.Rested"/>
    /// (Decision 9). Idempotent no-op on an already-full character, by construction of <see cref="Heal"/>.
    ///
    /// Deliberately <c>void</c>, not <c>bool</c> (a judgment call - the plan's prose describes this as
    /// "<c>FullHeal() = Heal(MaxQuarterMasks)</c>" without pinning a return type, while separately
    /// requiring <c>_playerController.Rested += FullHeal</c> against <c>Rested</c>'s fixed
    /// <c>System.Action</c> shape from Task 1.1 - a <c>bool</c>-returning method cannot be assigned to
    /// an <c>Action</c> delegate in C#, so <c>void</c> is the only signature that satisfies both).
    /// <see cref="Heal"/>'s own return value is intentionally discarded here.
    /// </summary>
    public void FullHeal()
    {
        Heal(MaxQuarterMasks);
    }

    /// <summary>
    /// Mask Shard progress toward the next <see cref="MaxMasks"/> increase (Decision 3) - raises the
    /// health ceiling only, never <see cref="CurrentQuarterMasks"/>, a deliberate, explicit design
    /// decision, not an oversight. No-ops (returns false) if <see cref="PlayerController.IsDead"/> or
    /// <see cref="MaxMasks"/> is already at <see cref="MaxMasksCap"/>. Otherwise increments
    /// <see cref="MaskShardProgress"/>; on reaching <see cref="ShardsPerMask"/>, resets progress to 0
    /// and increments <see cref="MaxMasks"/> by one (capped at <see cref="MaxMasksCap"/>). Raises
    /// <see cref="OnHealthChanged"/> on every successful call (not just the threshold-crossing one) so
    /// a future shard-progress HUD indicator, if ever added, can also react to partial progress -
    /// MaskHUD itself only cares about the <see cref="MaxMasks"/>-changing case, but this event is the
    /// single change-notification mechanism for this whole component (see its own doc comment).
    /// </summary>
    public bool AddMaskShard()
    {
        EnsureCachedComponents();

        if (_playerController.IsDead || _maxMasks >= MaxMasksCap)
        {
            return false;
        }

        _maskShardProgress++;
        if (_maskShardProgress >= _shardsPerMask)
        {
            _maskShardProgress = 0;
            _maxMasks = Mathf.Min(MaxMasksCap, _maxMasks + 1);
        }

        OnHealthChanged?.Invoke();

        return true;
    }
}
