using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for TestDamageHazard (Docs/Plans/010_health-mask-system.md, Tasks 2.3/5.2). Follows
/// BenchTests.cs's established fixture style, extended to also AddComponent&lt;PlayerHealth&gt;().
///
/// Not automated here (code-review/manual-verification items instead, per this task's own scope):
/// the class doc comment's "temporary QA/dev aid, not shippable content" wording (XML doc comments are
/// not reflectable at runtime - confirmed present by direct source read, see TestDamageHazard.cs), and
/// OnDrawGizmos' wire-cube rendering (Gizmos calls require a live Scene-view rendering context that
/// cannot be exercised meaningfully from NUnit).
/// </summary>
public class TestDamageHazardTests
{
    private GameObject _hazardObject;
    private GameObject _playerObject;
    private GameObject _strayObject;

    [TearDown]
    public void TearDown()
    {
        if (_hazardObject != null) Object.DestroyImmediate(_hazardObject);
        if (_playerObject != null) Object.DestroyImmediate(_playerObject);
        if (_strayObject != null) Object.DestroyImmediate(_strayObject);
    }

    private TestDamageHazard CreateHazard()
    {
        _hazardObject = new GameObject("TestHazard");
        var collider = _hazardObject.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        return _hazardObject.AddComponent<TestDamageHazard>();
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
                                    // in this project's EditMode tests (see MaskShardPickupTests.cs) -
                                    // invoked directly here so the damage arithmetic below is exercised
                                    // against a real, initialized baseline rather than an uninitialized zero.
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

    [Test]
    public void HandleTriggerEnter_WithPlayerHealth_DealsExactlyOneQuarterMaskOfDamage()
    {
        TestDamageHazard hazard = CreateHazard();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerHealth playerHealth);
        int healthBefore = playerHealth.CurrentQuarterMasks;

        hazard.HandleTriggerEnter(playerCollider);

        Assert.AreEqual(healthBefore - 1, playerHealth.CurrentQuarterMasks,
            "TestDamageHazard must deal exactly 1 quarter-mask of chip damage per entry, deliberately not " +
            "a full PlayerHealth.QuartersPerMask hit (Decision 11).");
    }

    [Test]
    public void HandleTriggerEnter_WithoutPlayerHealth_DoesNotThrow()
    {
        TestDamageHazard hazard = CreateHazard();
        Collider2D strayCollider = CreateStrayCollider();

        Assert.DoesNotThrow(() => hazard.HandleTriggerEnter(strayCollider));
    }

    [Test]
    public void Hazard_HasNoSpriteRenderer()
    {
        TestDamageHazard hazard = CreateHazard();

        Assert.IsNull(hazard.GetComponent<SpriteRenderer>(),
            "TestDamageHazard must remain invisible in Play Mode - no SpriteRenderer (Decision 12).");
    }
}
