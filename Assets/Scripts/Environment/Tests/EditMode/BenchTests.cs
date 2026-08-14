using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for Bench (Docs/Plans/008_bench-sit-mechanic.md, Task 6.3;
/// Docs/Plans/009_bench-visual-fixes.md, Task 3.2 - SitAnchor/SetVisible/NearBench wiring).
/// </summary>
public class BenchTests
{
    private GameObject _benchObject;
    private GameObject _secondBenchObject;
    private GameObject _playerObject;
    private GameObject _strayObject;
    private GameObject _anchorObject;

    [TearDown]
    public void TearDown()
    {
        if (_benchObject != null) Object.DestroyImmediate(_benchObject);
        if (_secondBenchObject != null) Object.DestroyImmediate(_secondBenchObject);
        if (_playerObject != null) Object.DestroyImmediate(_playerObject);
        if (_strayObject != null) Object.DestroyImmediate(_strayObject);
        if (_anchorObject != null) Object.DestroyImmediate(_anchorObject);
    }

    private Bench CreateBench()
    {
        _benchObject = new GameObject("TestBench");
        return _benchObject.AddComponent<Bench>();
    }

    private Bench CreateSecondBench()
    {
        _secondBenchObject = new GameObject("TestBench2");
        return _secondBenchObject.AddComponent<Bench>();
    }

    private Collider2D CreatePlayerCollider(out PlayerController playerController)
    {
        _playerObject = new GameObject("TestPlayer");
        var collider = _playerObject.AddComponent<BoxCollider2D>();
        _playerObject.AddComponent<Rigidbody2D>();
        playerController = _playerObject.AddComponent<PlayerController>();
        return collider;
    }

    private Collider2D CreateStrayCollider()
    {
        _strayObject = new GameObject("TestStray");
        return _strayObject.AddComponent<BoxCollider2D>();
    }

    /// <summary>
    /// Test-only helper that force-sets the private serialized field _sitAnchor via reflection - same
    /// rationale/convention as PlayerControllerTests.cs's ForceSetDefendHeld/ForceSetHorizontalInput
    /// (no public setter exists by design; Bench.SitAnchor is a read-only property that falls back to
    /// the Bench's own transform, per Decision 6).
    /// </summary>
    private static void ForceSetSitAnchor(Bench bench, Transform anchor)
    {
        FieldInfo field = typeof(Bench).GetField(
            "_sitAnchor", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(bench, anchor);
    }

    [Test]
    public void HandleTriggerEnter_WithPlayerController_SetsIsNearBenchTrue()
    {
        Bench bench = CreateBench();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerController playerController);

        bench.HandleTriggerEnter(playerCollider);

        Assert.IsTrue(playerController.IsNearBench);
    }

    [Test]
    public void HandleTriggerExit_WithPlayerController_SetsIsNearBenchFalse()
    {
        Bench bench = CreateBench();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerController playerController);
        playerController.IsNearBench = true;

        bench.HandleTriggerExit(playerCollider);

        Assert.IsFalse(playerController.IsNearBench);
    }

    [Test]
    public void HandleTriggerEnter_WithoutPlayerController_DoesNotThrow()
    {
        Bench bench = CreateBench();
        Collider2D strayCollider = CreateStrayCollider();

        Assert.DoesNotThrow(() => bench.HandleTriggerEnter(strayCollider));
    }

    [Test]
    public void HandleTriggerExit_WithoutPlayerController_DoesNotThrow()
    {
        Bench bench = CreateBench();
        Collider2D strayCollider = CreateStrayCollider();

        Assert.DoesNotThrow(() => bench.HandleTriggerExit(strayCollider));
    }

    // -------------------------------------------------------------------
    // Docs/Plans/009_bench-visual-fixes.md, Task 3.2 - SitAnchor/SetVisible/NearBench coverage
    // -------------------------------------------------------------------

    [Test]
    public void SitAnchor_WhenAssigned_ReturnsTheAssignedTransform()
    {
        Bench bench = CreateBench();
        _anchorObject = new GameObject("TestAnchor");
        _anchorObject.transform.SetParent(bench.transform);
        ForceSetSitAnchor(bench, _anchorObject.transform);

        Assert.AreSame(_anchorObject.transform, bench.SitAnchor);
    }

    [Test]
    public void SitAnchor_WhenUnassigned_FallsBackToOwnTransform()
    {
        Bench bench = CreateBench();

        Assert.AreSame(bench.transform, bench.SitAnchor);
    }

    [Test]
    public void SetVisible_False_DisablesSpriteRenderer()
    {
        Bench bench = CreateBench();
        var spriteRenderer = bench.GetComponent<SpriteRenderer>();

        bench.SetVisible(false);

        Assert.IsFalse(spriteRenderer.enabled);
    }

    [Test]
    public void SetVisible_True_EnablesSpriteRenderer()
    {
        Bench bench = CreateBench();
        var spriteRenderer = bench.GetComponent<SpriteRenderer>();
        bench.SetVisible(false);

        bench.SetVisible(true);

        Assert.IsTrue(spriteRenderer.enabled);
    }

    [Test]
    public void HandleTriggerEnter_WithPlayerController_SetsNearBenchReference()
    {
        Bench bench = CreateBench();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerController playerController);

        bench.HandleTriggerEnter(playerCollider);

        Assert.AreEqual(bench, playerController.NearBench);
    }

    [Test]
    public void HandleTriggerExit_WithPlayerController_ClearsNearBenchReference_WhenItWasThisBench()
    {
        Bench bench = CreateBench();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerController playerController);
        playerController.NearBench = bench;

        bench.HandleTriggerExit(playerCollider);

        Assert.IsNull(playerController.NearBench);
    }

    [Test]
    public void HandleTriggerExit_DoesNotClearNearBenchReference_WhenItPointsAtADifferentBench()
    {
        Bench bench = CreateBench();
        Bench otherBench = CreateSecondBench();
        Collider2D playerCollider = CreatePlayerCollider(out PlayerController playerController);
        playerController.NearBench = otherBench;

        bench.HandleTriggerExit(playerCollider);

        Assert.AreEqual(otherBench, playerController.NearBench);
    }
}
