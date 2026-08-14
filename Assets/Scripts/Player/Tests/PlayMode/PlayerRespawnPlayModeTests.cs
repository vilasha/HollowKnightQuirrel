using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for death and respawn (Docs/Plans/011_death-and-respawn.md, Task 4.4) - the real-
/// physics/real-timing end-to-end proof that a full Die() -> respawn-delay -> Respawn() cycle actually
/// works together: real Rigidbody2D teleport, the live PlayerController.Respawned -> PlayerHealth.FullHeal
/// event subscription (Task 3.1), and the real Assets/Animations/Quirrel.controller's RespawnTrigger-driven
/// Die -> Idle transition (Task 2.1) - not merely each piece's own isolated EditMode coverage (Tasks
/// 4.1-4.3).
///
/// RIG CHOICE: mirrors PlayerHealthPlayModeTests.cs's own established rig-in-[UnitySetUp] convention
/// (TestGround + TestPlayer, no Assets/Scenes/SampleScene.unity dependency - same three reasons that
/// file's own class doc comment gives). UNLIKE that suite's rig, which deliberately has no Animator
/// attached, this suite's TestPlayer also gets a real Animator with the project's actual Quirrel.controller
/// assigned (same AttachRealAnimator()-style pattern PlayerControllerPlayModeTests.cs uses for its own
/// real-Animator tests) - this is a genuinely new rig requirement for this suite, not redundant with
/// PlayerHealthPlayModeTests's coverage, since only a real Animator can prove RespawnTrigger drives the
/// real asset end-to-end (Task 4.4's own stated purpose).
///
/// BENCH STUB: a minimal TestBenchSeat (below) - a bare GameObject + child "SitAnchor" Transform,
/// positioned away from the Player's spawn point (Vector3.zero, matching this file family's own spawn
/// convention) - implementing IBenchSeat directly. Confirmed sufficient by this plan's own review:
/// PlayerController.Respawn() only ever reads LastRestedBenchSeat.SitAnchor, never calls TrySit or
/// SetVisible on the bench it respawns to (Decision 4), so SetVisible is a safe no-op here.
///
/// FATAL-HIT AMOUNT: PlayerHealth's default starting health is 4 masks x 4 quarter-masks/mask = 16
/// quarter-masks (QuartersPerMask, confirmed against PlayerHealth.cs and independently sanity-checked by
/// PlayerHealthPlayModeTests's own "Assert.AreEqual(16, startingQuarterMasks, ...)" assertion) - so
/// ApplyDamage(16) reduces CurrentQuarterMasks to exactly 0 in a single call, which is PlayerHealth's own
/// documented Die() trigger condition ("_currentQuarterMasks == 0"), guaranteeing an instant fatal hit
/// without needing a multi-hit sequence or relying on the (separate, non-fatal-path-only) invulnerability
/// window.
///
/// RESPAWN DELAY: PlayerController's default serialized _respawnDelay is 1.5f (confirmed directly against
/// PlayerController.cs's field initializer and doc comment). Hardcoded here as a local constant rather than
/// reflected, matching PlayerHealthPlayModeTests's own established precedent for a per-instance serialized
/// tunable (that file's InvulnerabilityWindowSeconds comment explains the same reasoning).
/// </summary>
public class PlayerRespawnPlayModeTests
{
    private const int PlayerLayer = 8; // Task 3.1, matches TagManager.asset / SampleScene.unity, same convention as the other PlayMode suites in this folder
    private const int GroundLayer = 9;

    private const string QuirrelControllerAssetPath = "Assets/Animations/Quirrel.controller";

    // See class doc comment's RESPAWN DELAY section - matches PlayerController's own default _respawnDelay.
    private const float RespawnDelaySeconds = 1.5f;

    // See class doc comment's FATAL-HIT AMOUNT section - matches PlayerHealth's own default starting health exactly.
    private const int FatalHitQuarterMasks = 16;

    // The Player always spawns at Vector3.zero in this rig (matching PlayerControllerPlayModeTests's/
    // PlayerHealthPlayModeTests's own established spawn convention).
    private const float SpawnX = 0f;

    // TestBenchSeat's world position - deliberately away from SpawnX (Decision-confirmed requirement:
    // "positioned away from the Player's spawn point"), so a successful respawn teleport is a real,
    // observable position change rather than a no-op that happens to already be in the right place.
    private const float BenchX = -6f;

    // Where the Player is moved to before dying - distinct from both SpawnX and BenchX, so the eventual
    // respawn assertion proves an actual teleport back to the bench, not merely "the player never moved
    // from wherever it started."
    private const float AwayFromBenchX = 4f;

    private GameObject _groundObject;
    private GameObject _playerObject;
    private GameObject _benchObject;
    private PlayerController _controller;
    private PlayerHealth _health;
    private Rigidbody2D _rigidbody;
    private Animator _animator;
    private Transform _benchAnchor;
    private TestBenchSeat _benchSeat;

    /// <summary>
    /// Minimal IBenchSeat stub (Docs/Plans/011_death-and-respawn.md, Task 4.4) - confirmed sufficient by
    /// this plan's own review, since PlayerController.Respawn() only ever reads
    /// <see cref="IBenchSeat.SitAnchor"/> and never calls TrySit/SetVisible on the bench it respawns to
    /// (Decision 4). <see cref="SetVisible"/> is a no-op purely to satisfy the interface's second member.
    /// </summary>
    private sealed class TestBenchSeat : IBenchSeat
    {
        public Transform SitAnchor { get; }

        public TestBenchSeat(Transform sitAnchor)
        {
            SitAnchor = sitAnchor;
        }

        public void SetVisible(bool visible)
        {
            // No-op - Respawn() never calls this (Decision 4).
        }
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // Ground: BoxCollider2D on the Ground layer, top surface at world y=0 - identical geometry to
        // every other PlayMode rig in this folder.
        _groundObject = new GameObject("TestGround");
        _groundObject.layer = GroundLayer;
        _groundObject.transform.position = new Vector3(0f, -0.5f, 0f);
        BoxCollider2D groundCollider = _groundObject.AddComponent<BoxCollider2D>();
        groundCollider.size = new Vector2(30f, 1f); // world x range [-15, 15] - comfortably covers both BenchX (-6) and AwayFromBenchX (4)

        // Player: mirrors SampleScene's/the other PlayMode rigs' Player geometry exactly.
        _playerObject = new GameObject("TestPlayer");
        _playerObject.layer = PlayerLayer;
        _playerObject.transform.position = new Vector3(SpawnX, 0f, 0f);
        _playerObject.AddComponent<SpriteRenderer>();

        _rigidbody = _playerObject.AddComponent<Rigidbody2D>();
        _rigidbody.gravityScale = 3.823f;
        _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;

        BoxCollider2D playerCollider = _playerObject.AddComponent<BoxCollider2D>();
        playerCollider.size = new Vector2(1f, 1.31f);
        playerCollider.offset = new Vector2(0f, 0.655f);

        _controller = _playerObject.AddComponent<PlayerController>();
        _health = _playerObject.AddComponent<PlayerHealth>(); // [RequireComponent(typeof(PlayerController))] already satisfied above

        // Real Animator + the project's actual Quirrel.controller - UNLIKE PlayerHealthPlayModeTests's
        // deliberately Animator-less rig, this suite specifically needs to prove RespawnTrigger drives the
        // real asset end-to-end (class doc comment's RIG CHOICE section).
        _animator = _playerObject.AddComponent<Animator>();
        AnimatorController controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(QuirrelControllerAssetPath);
        Assert.IsNotNull(controllerAsset,
            $"Could not load an AnimatorController at '{QuirrelControllerAssetPath}' - has it moved or been renamed?");
        _animator.runtimeAnimatorController = controllerAsset;

        // Minimal bench stub (see class doc comment's BENCH STUB section) - a bare GameObject + child
        // anchor Transform, positioned away from the Player's spawn point.
        _benchObject = new GameObject("TestBenchSeat");
        _benchObject.transform.position = new Vector3(BenchX, 0f, 0f);
        GameObject anchorObject = new GameObject("SitAnchor");
        anchorObject.transform.SetParent(_benchObject.transform);
        anchorObject.transform.localPosition = Vector3.zero;
        _benchAnchor = anchorObject.transform;
        _benchSeat = new TestBenchSeat(_benchAnchor);

        // Let Awake()/OnEnable() (PlayerController resolves its Ground layer mask; PlayerHealth subscribes
        // Rested/Respawned += FullHeal) settle, and give the real Animator a few frames to settle into its
        // default (Idle) state past any zero-duration Entry->Idle transition - same margin convention as
        // PlayerControllerPlayModeTests's AttachRealAnimator() call sites - plus one physics step.
        for (int i = 0; i < 5; i++)
        {
            yield return null;
        }

        yield return new WaitForFixedUpdate();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_playerObject != null)
        {
            Object.Destroy(_playerObject);
        }

        if (_benchObject != null)
        {
            Object.Destroy(_benchObject);
        }

        if (_groundObject != null)
        {
            Object.Destroy(_groundObject);
        }

        yield return null;
    }

    /// <summary>
    /// Sits on the stub bench (latching LastRestedBenchSeat), then teleports away to
    /// <see cref="AwayFromBenchX"/> - the shared setup steps every test below needs before inflicting a
    /// fatal hit. Direct rb.position writes for the "move away" step, matching this file family's own
    /// established teleport convention (PlayerHealthPlayModeTests.MovePlayerToX / TrySit's own production
    /// snap-to-bench precedent) rather than simulated held input.
    /// </summary>
    private IEnumerator SitOnBenchThenWalkAway()
    {
        _controller.NearBench = _benchSeat;
        bool sat = _controller.TrySit(true); // isNearBench = true, matching Bench.HandleTriggerEnter's real call shape
        Assert.IsTrue(sat, "Sanity check: TrySit should succeed while idle, grounded, and reported near the stub bench.");
        Assert.AreSame(_benchSeat, _controller.LastRestedBenchSeat,
            "Sanity check: a successful TrySit with NearBench set should latch LastRestedBenchSeat to the stub bench.");

        _rigidbody.position = new Vector2(AwayFromBenchX, 0f);
        yield return new WaitForFixedUpdate();
    }

    // ---------------------------------------------------------------------
    // 1. Full cycle under real Play Mode timing
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator FullDeathRespawnCycle_UnderRealTiming_TeleportsHealsAndReturnsToIdle()
    {
        yield return SitOnBenchThenWalkAway();

        bool damaged = _health.ApplyDamage(FatalHitQuarterMasks);
        Assert.IsTrue(damaged, "Sanity check: a 16-quarter-mask hit against the default 16-quarter-mask starting health should be accepted.");
        Assert.IsTrue(_controller.IsDead, "Sanity check: the player should be dead immediately after the fatal hit.");

        yield return new WaitForSeconds(RespawnDelaySeconds + 0.1f);

        float distanceFromBenchAnchor = Vector2.Distance(_rigidbody.position, _benchAnchor.position);
        Assert.Less(distanceFromBenchAnchor, 0.05f,
            "After the respawn delay elapses, the player should be teleported to LastRestedBenchSeat's SitAnchor position " +
            $"(distance was {distanceFromBenchAnchor}).");
        Assert.IsFalse(_controller.IsDead, "IsDead should be false again once Respawn() has resolved.");
        Assert.AreEqual(_health.MaxQuarterMasks, _health.CurrentQuarterMasks,
            "CurrentQuarterMasks should equal MaxQuarterMasks after respawn - proving the live " +
            "PlayerController.Respawned -> PlayerHealth.FullHeal event subscription (Task 3.1) actually fired, " +
            "not merely that Respawn() itself ran.");

        _animator.Update(0f); // defensive sync per this project's documented Animator/execute_code timing gotcha
        Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "The live Animator should have transitioned Die -> Idle via RespawnTrigger (Task 2.1).");
    }

    // ---------------------------------------------------------------------
    // 2. Waiting less than the respawn delay: still dead, position/Animator unchanged
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator RespawnTimer_WhileWaitingLessThanDelay_StaysDeadAndPositionUnchanged()
    {
        yield return SitOnBenchThenWalkAway();

        Vector2 positionAtDeath = _rigidbody.position;

        bool damaged = _health.ApplyDamage(FatalHitQuarterMasks);
        Assert.IsTrue(damaged, "Sanity check: the fatal hit should have been accepted.");

        // Comfortably before RespawnDelaySeconds elapses.
        yield return new WaitForSeconds(RespawnDelaySeconds - 0.5f);

        Assert.IsTrue(_controller.IsDead, "The player should still be dead before the respawn delay elapses.");

        float distanceFromDeathPosition = Vector2.Distance(_rigidbody.position, positionAtDeath);
        Assert.Less(distanceFromDeathPosition, 0.05f,
            $"Position should not have moved while still awaiting respawn (distance was {distanceFromDeathPosition}).");

        _animator.Update(0f);
        Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Die"),
            "The Animator should still be in the Die state before the respawn delay elapses - RespawnTrigger must not fire early.");
    }

    // ---------------------------------------------------------------------
    // 3. Post-respawn TryJump succeeds - no residual guard-flag/physics state survived the cycle
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator PostRespawn_TryJump_ReturnsTrueUnderRealPhysics()
    {
        yield return SitOnBenchThenWalkAway();

        _health.ApplyDamage(FatalHitQuarterMasks);
        yield return new WaitForSeconds(RespawnDelaySeconds + 0.1f);

        Assert.IsFalse(_controller.IsDead, "Sanity check: the player should have respawned by now.");

        bool jumped = _controller.TryJump(_controller.IsGrounded);
        Assert.IsTrue(jumped,
            "TryJump should succeed post-respawn under real Play Mode physics - confirms no residual " +
            "guard-flag (_jumpInProgress/_jumpImpulseCancelled/IsFullyCommitted) or physics state survived " +
            "the death/respawn cycle.");
    }

    // ---------------------------------------------------------------------
    // 4. A second full death->respawn cycle succeeds identically - not one-shot
    // ---------------------------------------------------------------------

    [UnityTest]
    public IEnumerator SecondDeathRespawnCycle_InSameSession_SucceedsIdenticallyToFirst()
    {
        yield return SitOnBenchThenWalkAway();

        _health.ApplyDamage(FatalHitQuarterMasks);
        yield return new WaitForSeconds(RespawnDelaySeconds + 0.1f);

        Assert.IsFalse(_controller.IsDead, "Sanity check: the first cycle should have resolved.");
        Assert.AreEqual(_health.MaxQuarterMasks, _health.CurrentQuarterMasks, "Sanity check: the first respawn should have fully healed.");

        // Move away again and die a second time - LastRestedBenchSeat is untouched by Respawn() itself
        // (only ever overwritten by a fresh successful TrySit), so it still points at the same stub bench.
        _rigidbody.position = new Vector2(AwayFromBenchX, 0f);
        yield return new WaitForFixedUpdate();

        bool damagedAgain = _health.ApplyDamage(FatalHitQuarterMasks);
        Assert.IsTrue(damagedAgain, "Sanity check: the second fatal hit should be accepted (health was fully restored by the first respawn).");
        Assert.IsTrue(_controller.IsDead, "The player should be dead again after the second fatal hit.");

        yield return new WaitForSeconds(RespawnDelaySeconds + 0.1f);

        float distanceFromBenchAnchor = Vector2.Distance(_rigidbody.position, _benchAnchor.position);
        Assert.Less(distanceFromBenchAnchor, 0.05f,
            "The second death/respawn cycle should teleport back to the bench identically to the first " +
            $"(distance was {distanceFromBenchAnchor}) - proves the respawn timer is safely re-triggerable, not one-shot.");
        Assert.IsFalse(_controller.IsDead, "IsDead should be false again once the second Respawn() has resolved.");
        Assert.AreEqual(_health.MaxQuarterMasks, _health.CurrentQuarterMasks,
            "The second respawn should also fully heal via the live Respawned -> FullHeal event.");

        _animator.Update(0f);
        Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "The Animator should reach Idle again after the second respawn - RespawnTrigger (a Trigger " +
            "parameter, which Mecanim auto-resets after consumption) must fire correctly a second time in " +
            "the same session, not only once.");
    }
}
