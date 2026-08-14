using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// EditMode tests for MaskPickup (Docs/Plans/010_health-mask-system.md, Tasks 2.2/5.2). Identical shape
/// to MaskShardPickupTests.cs (Decision 15's generalized isTrigger check included) - see that file for
/// the shared rationale.
/// </summary>
public class MaskPickupTests
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

    private MaskPickup CreatePickup(bool isTrigger = true)
    {
        _pickupObject = new GameObject("TestMaskPickup");
        var collider = _pickupObject.AddComponent<BoxCollider2D>();
        collider.isTrigger = isTrigger;
        return _pickupObject.AddComponent<MaskPickup>();
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
                                    // in this project's EditMode tests (see MaskShardPickupTests.cs).
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

    private static void InvokeAwake(MaskPickup pickup)
    {
        MethodInfo method = typeof(MaskPickup).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(pickup, null);
    }

    [Test]
    public void HandleTriggerEnter_WithPlayerHealth_CallsFullHealOnce_DestroysPickup()
    {
        MaskPickup pickup = CreatePickup();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerHealth playerHealth);
        playerHealth.ApplyDamage(15); // 16 -> 1, non-fatal, so the pickup's FullHeal() effect is observable
        Assert.AreEqual(1, playerHealth.CurrentQuarterMasks, "Precondition: the player should be damaged before pickup.");

        bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true; // see MaskShardPickupTests.cs's identical note on
                                                 // Object.Destroy() outside Play Mode.
        try
        {
            pickup.HandleTriggerEnter(playerCollider);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnoreSetting;
        }

        Assert.AreEqual(playerHealth.MaxQuarterMasks, playerHealth.CurrentQuarterMasks,
            "HandleTriggerEnter must call FullHeal() - health should be fully restored.");
        Assert.IsTrue(pickup == null, "The pickup GameObject must be destroyed on a successful pickup.");
    }

    [Test]
    public void HandleTriggerEnter_WithoutPlayerHealth_DoesNotThrow_DoesNotDestroy()
    {
        MaskPickup pickup = CreatePickup();
        Collider2D strayCollider = CreateStrayCollider();

        Assert.DoesNotThrow(() => pickup.HandleTriggerEnter(strayCollider));
        Assert.IsFalse(pickup == null, "The pickup must not be destroyed when the entering collider has no PlayerHealth.");
    }

    [Test]
    public void Awake_WithNonTriggerCollider_LogsError()
    {
        MaskPickup pickup = CreatePickup(isTrigger: false);

        LogAssert.Expect(LogType.Error, new Regex("non-trigger Collider2D"));
        InvokeAwake(pickup);
    }
}
