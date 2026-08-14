using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for PlayerHealth (Docs/Plans/010_health-mask-system.md, Tasks 1.2/1.3/5.1).
///
/// IMPORTANT - the project's own confirmed EditMode-Awake gotcha (see PlayerController's own
/// EnsureCachedComponents doc comment, and the agent-learnings entry it cites): Unity does not invoke
/// Awake()/Start()/Update()/FixedUpdate() for a plain MonoBehaviour added via AddComponent() outside
/// Play Mode. PlayerHealth's Awake() is the ONLY place that initializes _maxMasks/_currentQuarterMasks/
/// _maskShardProgress/_isInvulnerable - unlike _playerController (re-cached defensively by
/// EnsureCachedComponents() at the top of every public method), these value fields have no such
/// idempotent safety net in the current PlayerHealth.cs. <see cref="FreshInstance_HasExpectedDefaults"/>
/// below is the one deliberate exception that reads state immediately after CreatePlayer() with no
/// further setup, to directly exercise Task 1.2's literal "fresh instance" acceptance criterion as
/// written - if PlayerHealth.cs's Awake()-only initialization is truly unreachable from this project's
/// EditMode tests, this specific test will fail, which is the correct, intended way to surface that gap
/// (see this file's own InitializeHealth() doc comment for the workaround every other test below uses).
/// </summary>
public class PlayerHealthTests
{
    private GameObject _playerObject;
    private PlayerController _playerController;
    private PlayerHealth _playerHealth;

    [TearDown]
    public void TearDown()
    {
        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }
    }

    /// <summary>Mirrors PlayerControllerTests.cs's own CreatePlayer() convention, extended with PlayerHealth.</summary>
    private void CreatePlayer()
    {
        _playerObject = new GameObject("TestPlayer");
        _playerObject.AddComponent<Rigidbody2D>();
        _playerObject.AddComponent<SpriteRenderer>();
        _playerController = _playerObject.AddComponent<PlayerController>();
        _playerHealth = _playerObject.AddComponent<PlayerHealth>();
    }

    /// <summary>
    /// Test-only helper that invokes PlayerHealth's private Awake() directly via reflection - this runs
    /// the actual production Awake() code (not a hand-duplicated reimplementation of it), purely to work
    /// around Awake() not firing automatically for AddComponent'd instances in this Editor test context
    /// (see this class's own doc comment).
    /// </summary>
    private static void InvokeAwake(PlayerHealth health)
    {
        MethodInfo method = typeof(PlayerHealth).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(health, null);
    }

    /// <summary>
    /// Same reflection-invocation technique as <see cref="InvokeAwake"/>, applied to OnEnable - toggling
    /// the public <c>enabled</c> property was tried first but does not reliably fire OnEnable
    /// synchronously in this project's EditMode test context (confirmed empirically: tests relying on it
    /// alone failed with the Rested subscription never having registered), the same underlying class of
    /// gap as Awake() never firing for a bare AddComponent instance outside Play Mode. Invoking the real
    /// production OnEnable() directly sidesteps that uncertainty entirely.
    /// </summary>
    private static void InvokeOnEnable(PlayerHealth health)
    {
        MethodInfo method = typeof(PlayerHealth).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(health, null);
    }

    /// <summary>Same reflection-invocation technique as <see cref="InvokeOnEnable"/>, for OnDisable.</summary>
    private static void InvokeOnDisable(PlayerHealth health)
    {
        MethodInfo method = typeof(PlayerHealth).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(health, null);
    }

    /// <summary>
    /// Test-only helper used by every test below EXCEPT <see cref="FreshInstance_HasExpectedDefaults"/>:
    /// forces PlayerHealth into a fully-initialized, subscribed state before exercising its logic, via
    /// direct reflection invocation of the real Awake()/OnEnable() methods (not a hand-duplicated
    /// reimplementation of either).
    /// </summary>
    private void InitializeHealth()
    {
        InvokeAwake(_playerHealth);
        InvokeOnEnable(_playerHealth);
    }

    // ---------------------------------------------------------------------
    // Task 1.2 - data model, damage/death path, invulnerability
    // ---------------------------------------------------------------------

    [Test]
    public void FreshInstance_HasExpectedDefaults()
    {
        CreatePlayer();

        Assert.AreEqual(4, _playerHealth.MaxMasks);
        Assert.AreEqual(16, _playerHealth.CurrentQuarterMasks);
        Assert.AreEqual(0, _playerHealth.MaskShardProgress);
        Assert.IsFalse(_playerHealth.IsInvulnerable);
    }

    [Test]
    public void ApplyDamage_NormalHit_ReducesHealthByAmount_SetsInvulnerable_CallsHurtNotDie()
    {
        CreatePlayer();
        InitializeHealth();

        bool result = _playerHealth.ApplyDamage(4);

        Assert.IsTrue(result);
        Assert.AreEqual(12, _playerHealth.CurrentQuarterMasks);
        Assert.IsTrue(_playerHealth.IsInvulnerable);
        Assert.IsFalse(_playerController.IsDead, "A non-fatal hit must not call Die().");
        Assert.IsTrue(_playerController.IsHurtStunned,
            "A non-fatal hit must call PlayerController.Hurt() exactly once (observed via IsHurtStunned becoming true).");
    }

    [Test]
    public void ApplyDamage_OneQuarterMask_QAHazardAmount_ReducesHealthByOne()
    {
        CreatePlayer();
        InitializeHealth();

        bool result = _playerHealth.ApplyDamage(1);

        Assert.IsTrue(result);
        Assert.AreEqual(15, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void ApplyDamage_ReducingToExactlyZero_CallsDie_IsInvulnerableStaysFalse()
    {
        CreatePlayer();
        InitializeHealth();

        bool result = _playerHealth.ApplyDamage(16); // exactly MaxQuarterMasks

        Assert.IsTrue(result);
        Assert.AreEqual(0, _playerHealth.CurrentQuarterMasks);
        Assert.IsTrue(_playerController.IsDead);
        Assert.IsFalse(_playerHealth.IsInvulnerable,
            "A fatal hit must not start the invulnerability window (Decision 6 - the window starts only on a non-fatal hit).");
    }

    [Test]
    public void ApplyDamage_WhileInvulnerable_ReturnsFalse_HealthUnchanged()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(4);
        Assert.IsTrue(_playerHealth.IsInvulnerable, "Precondition: the first hit should have started the invulnerability window.");
        int healthAfterFirstHit = _playerHealth.CurrentQuarterMasks;

        bool result = _playerHealth.ApplyDamage(4);

        Assert.IsFalse(result);
        Assert.AreEqual(healthAfterFirstHit, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void AdvanceInvulnerabilityTimer_ClearsInvulnerability_OnceWindowElapses_AndSubsequentDamageSucceeds()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(4); // starts the default 1.0s invulnerability window
        Assert.IsTrue(_playerHealth.IsInvulnerable);

        _playerHealth.AdvanceInvulnerabilityTimer(0.5f);
        Assert.IsTrue(_playerHealth.IsInvulnerable, "The window should still be active at 0.5s into the default 1.0s duration.");

        _playerHealth.AdvanceInvulnerabilityTimer(0.51f); // elapses the remaining ~0.5s
        Assert.IsFalse(_playerHealth.IsInvulnerable, "The window should have cleared once fully elapsed.");

        bool secondHitResult = _playerHealth.ApplyDamage(4);
        Assert.IsTrue(secondHitResult, "A subsequent ApplyDamage call must succeed once invulnerability clears.");
        Assert.AreEqual(8, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void ApplyDamage_WhileAlreadyDead_ReturnsFalse_NoThrow()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(16); // fatal
        Assert.IsTrue(_playerController.IsDead, "Precondition: the character should already be dead.");

        bool result = true;
        Assert.DoesNotThrow(() => result = _playerHealth.ApplyDamage(4));
        Assert.IsFalse(result);
    }

    [Test]
    public void ApplyDamage_ZeroOrNegative_IsANoOp()
    {
        CreatePlayer();
        InitializeHealth();

        Assert.IsFalse(_playerHealth.ApplyDamage(0));
        Assert.AreEqual(16, _playerHealth.CurrentQuarterMasks);

        Assert.IsFalse(_playerHealth.ApplyDamage(-1));
        Assert.AreEqual(16, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void OnHealthChanged_FiresExactlyOnce_OnSuccessfulApplyDamage()
    {
        CreatePlayer();
        InitializeHealth();
        int fireCount = 0;
        _playerHealth.OnHealthChanged += () => fireCount++;

        _playerHealth.ApplyDamage(4);

        Assert.AreEqual(1, fireCount);
    }

    [Test]
    public void OnHealthChanged_DoesNotFire_OnZeroAmountApplyDamage()
    {
        CreatePlayer();
        InitializeHealth();
        int fireCount = 0;
        _playerHealth.OnHealthChanged += () => fireCount++;

        _playerHealth.ApplyDamage(0);

        Assert.AreEqual(0, fireCount);
    }

    [Test]
    public void OnHealthChanged_DoesNotFire_OnApplyDamageWhileInvulnerable()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(4); // starts invulnerability, fires once
        int fireCount = 0;
        _playerHealth.OnHealthChanged += () => fireCount++;

        _playerHealth.ApplyDamage(4); // no-op while invulnerable

        Assert.AreEqual(0, fireCount);
    }

    [Test]
    public void OnHealthChanged_DoesNotFire_OnApplyDamageWhileDead()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(16); // fatal
        int fireCount = 0;
        _playerHealth.OnHealthChanged += () => fireCount++;

        _playerHealth.ApplyDamage(4); // no-op while dead

        Assert.AreEqual(0, fireCount);
    }

    [Test]
    public void MaxMasksCap_IsAConstant_NotASerializedTunableField()
    {
        FieldInfo field = typeof(PlayerHealth).GetField(nameof(PlayerHealth.MaxMasksCap), BindingFlags.Public | BindingFlags.Static);

        Assert.IsNotNull(field, "MaxMasksCap must be a public field.");
        Assert.IsTrue(field.IsLiteral,
            "MaxMasksCap must be a compile-time const (Decision 7), not a serialized/tunable Inspector " +
            "field - a serialized tunable here, paired with MaskHUD's hardcoded 9-slot Image[] array, " +
            "would let a designer silently break the HUD by raising the cap above 9 with no error.");
        Assert.AreEqual(9, PlayerHealth.MaxMasksCap);
    }

    // ---------------------------------------------------------------------
    // Task 1.3 - heal API, shard-to-mask conversion, Rested subscription
    // ---------------------------------------------------------------------

    [Test]
    public void Heal_NormalAmount_IncreasesHealth()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(8); // 16 -> 8
        Assert.AreEqual(8, _playerHealth.CurrentQuarterMasks);

        bool result = _playerHealth.Heal(4);

        Assert.IsTrue(result);
        Assert.AreEqual(12, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void Heal_Overheal_ClampsToMaxQuarterMasks()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(8); // 16 -> 8

        bool result = _playerHealth.Heal(100);

        Assert.IsTrue(result);
        Assert.AreEqual(_playerHealth.MaxQuarterMasks, _playerHealth.CurrentQuarterMasks);
        Assert.AreEqual(16, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void Heal_WhenAlreadyFull_ReturnsFalse_OnHealthChangedDoesNotFire()
    {
        CreatePlayer();
        InitializeHealth();
        Assert.AreEqual(_playerHealth.MaxQuarterMasks, _playerHealth.CurrentQuarterMasks, "Precondition: a fresh instance starts full.");
        int fireCount = 0;
        _playerHealth.OnHealthChanged += () => fireCount++;

        bool result = _playerHealth.Heal(4);

        Assert.IsFalse(result);
        Assert.AreEqual(0, fireCount);
    }

    [Test]
    public void FullHeal_RestoresExactlyToMaxQuarterMasks()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(15); // 16 -> 1, non-fatal
        Assert.AreEqual(1, _playerHealth.CurrentQuarterMasks);

        _playerHealth.FullHeal();

        Assert.AreEqual(_playerHealth.MaxQuarterMasks, _playerHealth.CurrentQuarterMasks);
        Assert.AreEqual(16, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void AddMaskShard_ThreeCalls_OnlyIncrementsProgress()
    {
        CreatePlayer();
        InitializeHealth();
        int maxMasksBefore = _playerHealth.MaxMasks;
        int healthBefore = _playerHealth.CurrentQuarterMasks;

        _playerHealth.AddMaskShard();
        _playerHealth.AddMaskShard();
        _playerHealth.AddMaskShard();

        Assert.AreEqual(3, _playerHealth.MaskShardProgress);
        Assert.AreEqual(maxMasksBefore, _playerHealth.MaxMasks);
        Assert.AreEqual(healthBefore, _playerHealth.CurrentQuarterMasks);
    }

    [Test]
    public void AddMaskShard_FourthCall_ResetsProgress_RaisesMaxMasks_HealthUnchanged()
    {
        CreatePlayer();
        InitializeHealth();
        int healthBeforeAnyShards = _playerHealth.CurrentQuarterMasks;

        _playerHealth.AddMaskShard();
        _playerHealth.AddMaskShard();
        _playerHealth.AddMaskShard();
        bool fourthResult = _playerHealth.AddMaskShard();

        Assert.IsTrue(fourthResult);
        Assert.AreEqual(0, _playerHealth.MaskShardProgress);
        Assert.AreEqual(5, _playerHealth.MaxMasks);
        Assert.AreEqual(healthBeforeAnyShards, _playerHealth.CurrentQuarterMasks,
            "Mask Shards must raise the health ceiling only, never CurrentQuarterMasks directly (Decision 3) - " +
            "explicit assertion against the value captured before any of the 4 calls.");
    }

    [Test]
    public void AddMaskShard_RepeatedUntilCap_FurtherCallsReturnFalse_Unchanged()
    {
        CreatePlayer();
        InitializeHealth();
        int callsNeeded = (PlayerHealth.MaxMasksCap - _playerHealth.MaxMasks) * _playerHealth.ShardsPerMask;
        for (int i = 0; i < callsNeeded; i++)
        {
            _playerHealth.AddMaskShard();
        }
        Assert.AreEqual(PlayerHealth.MaxMasksCap, _playerHealth.MaxMasks, "Precondition: MaxMasks should have reached the cap.");
        Assert.AreEqual(0, _playerHealth.MaskShardProgress, "Precondition: progress resets to 0 on the threshold-crossing call.");

        bool resultAtCap = _playerHealth.AddMaskShard();

        Assert.IsFalse(resultAtCap);
        Assert.AreEqual(PlayerHealth.MaxMasksCap, _playerHealth.MaxMasks);
        Assert.AreEqual(0, _playerHealth.MaskShardProgress);
    }

    [Test]
    public void AddMaskShard_SuccessfulCall_RaisesOnHealthChanged()
    {
        CreatePlayer();
        InitializeHealth();
        int fireCount = 0;
        _playerHealth.OnHealthChanged += () => fireCount++;

        _playerHealth.AddMaskShard();

        Assert.AreEqual(1, fireCount);
    }

    [Test]
    public void PlayerControllerRested_TriggersFullHealEndToEnd()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(15); // 16 -> 1, non-fatal
        Assert.AreEqual(1, _playerHealth.CurrentQuarterMasks);

        // ApplyDamage's non-fatal path also calls PlayerController.Hurt(), which starts a 0.3s
        // hit-stun window during which IsFullyCommitted is true - and TrySit requires
        // !IsFullyCommitted (via IsIdleAndGrounded()). Clear it first via the same production method
        // PlayerController's own FixedUpdate uses, or TrySit below would spuriously fail for a reason
        // unrelated to what this test is proving.
        _playerController.AdvanceHurtStunTimer(1f);
        Assert.IsFalse(_playerController.IsHurtStunned, "Precondition: hit-stun must have elapsed before attempting to sit.");

        bool sat = _playerController.TrySit(true); // TrySit's success path invokes Rested (Decision 9)

        Assert.IsTrue(sat, "Precondition: TrySit must succeed (idle, grounded, near bench) to invoke Rested.");
        Assert.AreEqual(_playerHealth.MaxQuarterMasks, _playerHealth.CurrentQuarterMasks,
            "PlayerHealth's OnEnable-time Rested += FullHeal subscription must fully heal the character end-to-end when TrySit succeeds.");
    }

    [Test]
    public void OnDisable_Unsubscribes_DisablingThenInvokingRestedAgain_DoesNotThrow()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(15); // 16 -> 1, non-fatal
        Assert.AreEqual(1, _playerHealth.CurrentQuarterMasks);
        _playerController.AdvanceHurtStunTimer(1f); // clear hit-stun so TrySit below isn't blocked by it

        // Direct reflection invocation, not toggling the public `enabled` property - the latter does
        // not reliably fire OnDisable/OnEnable synchronously in this project's EditMode test context
        // (see InvokeOnEnable's own doc comment for the empirical finding this mirrors).
        InvokeOnDisable(_playerHealth); // unsubscribes Rested -= FullHeal

        Assert.DoesNotThrow(() => _playerController.TrySit(true));
        Assert.AreEqual(1, _playerHealth.CurrentQuarterMasks,
            "After OnDisable unsubscribes Rested += FullHeal, a subsequent Rested invocation must not heal the character.");
    }

    // ---------------------------------------------------------------------
    // Docs/Plans/011_death-and-respawn.md, Task 3.1 - coverage for the new Respawned += FullHeal
    // subscription (OnEnable/OnDisable), mirroring this file's own existing Rested-subscription tests
    // above (PlayerControllerRested_TriggersFullHealEndToEnd / OnDisable_Unsubscribes...) exactly.
    // ---------------------------------------------------------------------

    /// <summary>Same reflection-access convention as this file's other private-field readers - reads the serialized <c>_respawnDelay</c> tunable rather than hardcoding it, so this test stays correct if the Inspector default is ever retuned.</summary>
    private static float GetRespawnDelay(PlayerController controller)
    {
        FieldInfo field = typeof(PlayerController).GetField(
            "_respawnDelay", BindingFlags.NonPublic | BindingFlags.Instance);
        return (float)field.GetValue(controller);
    }

    [Test]
    public void PlayerControllerRespawned_TriggersFullHealEndToEnd()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(16); // fatal - reduces to 0 and calls PlayerController.Die()
        Assert.IsTrue(_playerController.IsDead, "Precondition: the character must be dead before it can respawn.");
        Assert.AreEqual(0, _playerHealth.CurrentQuarterMasks, "Precondition: health should be 0 after the fatal hit.");

        // Drives the real respawn timer past its delay (mirrors PlayerControllerTests.cs's own
        // Die()/AdvanceRespawnTimer convention) - Respawn()'s success path invokes Respawned
        // (Docs/Plans/011_death-and-respawn.md, Decision 7), which this component's OnEnable-time
        // subscription consumes, exactly like TrySit's success path already does for Rested above.
        float respawnDelay = GetRespawnDelay(_playerController);
        _playerController.AdvanceRespawnTimer(respawnDelay + 0.01f);

        Assert.IsFalse(_playerController.IsDead, "Precondition: the character must have respawned.");
        Assert.AreEqual(_playerHealth.MaxQuarterMasks, _playerHealth.CurrentQuarterMasks,
            "PlayerHealth's OnEnable-time Respawned += FullHeal subscription must fully heal the character end-to-end on respawn.");
    }

    [Test]
    public void OnDisable_UnsubscribesRespawned_DisablingThenRespawning_DoesNotThrow_DoesNotHeal()
    {
        CreatePlayer();
        InitializeHealth();
        _playerHealth.ApplyDamage(16); // fatal
        Assert.IsTrue(_playerController.IsDead, "Precondition: the character must be dead.");
        Assert.AreEqual(0, _playerHealth.CurrentQuarterMasks);

        // Direct reflection invocation, not toggling the public `enabled` property - same rationale as
        // InvokeOnEnable's own doc comment above.
        InvokeOnDisable(_playerHealth); // unsubscribes Respawned -= FullHeal (alongside Rested -= FullHeal)

        float respawnDelay = GetRespawnDelay(_playerController);

        Assert.DoesNotThrow(() => _playerController.AdvanceRespawnTimer(respawnDelay + 0.01f));

        Assert.IsFalse(_playerController.IsDead,
            "PlayerController's own Respawn() logic is entirely independent of PlayerHealth's subscription and must still resolve.");
        Assert.AreEqual(0, _playerHealth.CurrentQuarterMasks,
            "After OnDisable unsubscribes Respawned += FullHeal, a subsequent Respawned invocation must not heal the character.");
    }
}
