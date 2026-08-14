using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// EditMode tests for MaskShardPickup (Docs/Plans/010_health-mask-system.md, Tasks 2.1/5.2). Follows
/// BenchTests.cs's established fixture style (CreatePlayerCollider/CreateStrayCollider), extended to
/// also AddComponent&lt;PlayerHealth&gt;() per Task 5.2's own instruction.
/// </summary>
public class MaskShardPickupTests
{
    private GameObject _pickupObject;
    private GameObject _playerObject;
    private GameObject _strayObject;

    [TearDown]
    public void TearDown()
    {
        if (_pickupObject != null) Object.DestroyImmediate(_pickupObject);
        if (_playerObject != null) Object.DestroyImmediate(_playerObject);
        if (_strayObject != null) Object.DestroyImmediate(_strayObject);
    }

    private MaskShardPickup CreatePickup(bool isTrigger = true)
    {
        _pickupObject = new GameObject("TestMaskShardPickup");
        var collider = _pickupObject.AddComponent<BoxCollider2D>();
        collider.isTrigger = isTrigger;
        return _pickupObject.AddComponent<MaskShardPickup>();
    }

    private Collider2D CreatePlayerCollider(out PlayerHealth playerHealth)
    {
        _playerObject = new GameObject("TestPlayer");
        var collider = _playerObject.AddComponent<BoxCollider2D>();
        _playerObject.AddComponent<Rigidbody2D>();
        _playerObject.AddComponent<SpriteRenderer>();
        _playerObject.AddComponent<PlayerController>();
        playerHealth = _playerObject.AddComponent<PlayerHealth>();
        InvokeAwake(playerHealth); // PlayerHealth's Awake()-only init never fires via AddComponent alone
                                    // in this project's EditMode tests (confirmed project gotcha - see
                                    // PlayerHealthTests.cs's own doc comment) - invoked directly here so
                                    // AddMaskShard()'s cap check runs against a real baseline.
        return collider;
    }

    private Collider2D CreateStrayCollider()
    {
        _strayObject = new GameObject("TestStray");
        return _strayObject.AddComponent<BoxCollider2D>();
    }

    private static void InvokeAwake(PlayerHealth health)
    {
        MethodInfo method = typeof(PlayerHealth).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(health, null);
    }

    private static void InvokeAwake(MaskShardPickup pickup)
    {
        MethodInfo method = typeof(MaskShardPickup).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(pickup, null);
    }

    [Test]
    public void HandleTriggerEnter_WithPlayerHealth_CallsAddMaskShardOnce_DestroysPickup()
    {
        MaskShardPickup pickup = CreatePickup();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerHealth playerHealth);
        int progressBefore = playerHealth.MaskShardProgress;

        bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true; // Object.Destroy() called outside Play Mode triggers
                                                 // Unity's own Editor console warning/error (it falls back
                                                 // to DestroyImmediate internally) - expected Editor
                                                 // behavior from this pickup's own Destroy(gameObject)
                                                 // call, not a bug under test.
        try
        {
            pickup.HandleTriggerEnter(playerCollider);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnoreSetting;
        }

        Assert.AreEqual(progressBefore + 1, playerHealth.MaskShardProgress,
            "HandleTriggerEnter must call AddMaskShard() exactly once.");
        Assert.IsTrue(pickup == null, "The pickup GameObject must be destroyed on a successful pickup.");
    }

    [Test]
    public void HandleTriggerEnter_WithoutPlayerHealth_DoesNotThrow_DoesNotDestroy()
    {
        MaskShardPickup pickup = CreatePickup();
        Collider2D strayCollider = CreateStrayCollider();

        Assert.DoesNotThrow(() => pickup.HandleTriggerEnter(strayCollider));
        Assert.IsFalse(pickup == null, "The pickup must not be destroyed when the entering collider has no PlayerHealth.");
    }

    [Test]
    public void Awake_WithNonTriggerCollider_LogsError()
    {
        MaskShardPickup pickup = CreatePickup(isTrigger: false);

        LogAssert.Expect(LogType.Error, new Regex("non-trigger Collider2D"));
        InvokeAwake(pickup);
    }
}
