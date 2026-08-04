using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for CameraFollow (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Task 3.7;
/// Docs/Plans/006_look-up-down-camera-pan.md, Tasks 2.1a/2.1b for the W/S look-up/down pan).
/// </summary>
public class CameraFollowTests
{
    private const float FixedDeltaTime = 0.02f;
    private const int StepCount = 300; // 6 simulated seconds - far beyond SmoothDamp's 0.15s time constant, comfortable convergence margin

    private GameObject _cameraObject;
    private GameObject _targetObject;
    private GameObject _playerControllerObject;
    private CameraFollow _cameraFollow;

    [TearDown]
    public void TearDown()
    {
        if (_cameraObject != null)
        {
            Object.DestroyImmediate(_cameraObject);
        }

        if (_targetObject != null)
        {
            Object.DestroyImmediate(_targetObject);
        }

        if (_playerControllerObject != null)
        {
            Object.DestroyImmediate(_playerControllerObject);
        }
    }

    /// <summary>
    /// Test-only helper that sets CameraFollow's private <c>_panDirection</c> field directly via
    /// reflection, bypassing Update()/Input.GetKey entirely - Update() never runs in EditMode tests,
    /// and legacy Input cannot be driven programmatically (Docs/Plans/006, Section 0).
    /// </summary>
    private static void ForceSetPanDirection(CameraFollow cameraFollow, float value)
    {
        FieldInfo field = typeof(CameraFollow).GetField(
            "_panDirection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(cameraFollow, value);
    }

    /// <summary>
    /// Test-only helper that assigns CameraFollow's private <c>_playerController</c>
    /// [SerializeField] directly via reflection - it has no public setter (Docs/Plans/006, Task 2.1a).
    /// Shared by this task's and Task 2.1b's tests.
    /// </summary>
    private static void ForceSetPlayerController(CameraFollow cameraFollow, PlayerController playerController)
    {
        FieldInfo field = typeof(CameraFollow).GetField(
            "_playerController",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(cameraFollow, playerController);
    }

    [Test]
    public void XPosition_ConvergesToStationaryTarget_AfterFixedStepSimulation()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        float finalX = _cameraObject.transform.position.x;
        Assert.AreEqual(_targetObject.transform.position.x, finalX, 0.01f,
            "Camera X should converge to within 0.01 units of the stationary target's X after enough fixed-step SmoothDamp calls.");
    }

    /// <summary>
    /// Narrowed per Docs/Plans/006 Task 2.1a: this invariant ("Y/Z never move") now holds only while
    /// no pan input is held. With <c>_panDirection</c> defaulting to 0, SmoothDamp(3f, 3f, ...) is a
    /// true no-op, so the assertions/setup below are unchanged from before this feature existed.
    /// </summary>
    [Test]
    public void YAndZPosition_StayFixed_WhileFollowingOnX_AndNoPanInputHeld()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(8f, 12f, 42f); // deliberately different Y/Z to prove they're ignored

        _cameraFollow.target = _targetObject.transform;

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Vector3 finalPosition = _cameraObject.transform.position;
        Assert.AreEqual(3f, finalPosition.y, 0.0001f, "Camera Y must stay fixed at its starting value while no pan input is held.");
        Assert.AreEqual(-10f, finalPosition.z, 0.0001f, "Camera Z must stay fixed at its starting value.");
    }

    [Test]
    public void YPosition_PansUpByFallbackDistance_WhileWDirectionForced()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        ForceSetPanDirection(_cameraFollow, 1f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f + 5f, _cameraObject.transform.position.y, 0.01f,
            "With W held (_panDirection = 1) and no Camera component present, Y should converge to baseline + the 5f fallback pan distance.");
    }

    [Test]
    public void YPosition_PansDownByFallbackDistance_WhileSDirectionForced()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        ForceSetPanDirection(_cameraFollow, -1f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f - 5f, _cameraObject.transform.position.y, 0.01f,
            "With S held (_panDirection = -1) and no Camera component present, Y should converge to baseline - the 5f fallback pan distance.");
    }

    [Test]
    public void YPosition_ReturnsToBaseline_AfterPanDirectionReleased()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        ForceSetPanDirection(_cameraFollow, 1f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f + 5f, _cameraObject.transform.position.y, 0.01f,
            "Precondition: camera should have converged to the panned-up position before release.");

        ForceSetPanDirection(_cameraFollow, 0f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f, _cameraObject.transform.position.y, 0.01f,
            "Releasing pan input (_panDirection back to 0) should let Y converge back to baseline over the same SmoothTime.");
    }

    [Test]
    public void YPosition_UsesLiveCameraOrthographicSize_WhenCameraComponentPresent()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        Camera camera = _cameraObject.AddComponent<Camera>();
        camera.orthographicSize = 7f;

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        ForceSetPanDirection(_cameraFollow, 1f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f + 7f, _cameraObject.transform.position.y, 0.01f,
            "With a live Camera component present, pan distance should come from its orthographicSize (7f), not the 5f fallback.");
    }

    [Test]
    public void PanInput_IsBlocked_WhilePlayerControllerIsFullyCommitted()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        _playerControllerObject = new GameObject("TestPlayerController");
        _playerControllerObject.AddComponent<Rigidbody2D>();
        PlayerController playerController = _playerControllerObject.AddComponent<PlayerController>();

        PropertyInfo defendHeldProperty = typeof(PlayerController).GetProperty(
            nameof(PlayerController.DefendHeld),
            BindingFlags.Public | BindingFlags.Instance);
        defendHeldProperty.SetValue(playerController, true);

        ForceSetPlayerController(_cameraFollow, playerController);
        ForceSetPanDirection(_cameraFollow, 1f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f, _cameraObject.transform.position.y, 0.01f,
            "Pan must be fully blocked while the assigned PlayerController's IsFullyCommitted (via DefendHeld) is true.");
    }

    /// <summary>
    /// Isolates the IsDead branch of the pan gate specifically: DefendHeld/other IsFullyCommitted
    /// flags are left at their real post-Die() values (false), so this proves IsDead alone blocks
    /// pan even when IsFullyCommitted is false - the exact gap the plan called out (Section 0:
    /// Die() clears every flag IsFullyCommitted's formula reads).
    /// </summary>
    [Test]
    public void PanInput_IsBlocked_WhilePlayerControllerIsDead()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        _playerControllerObject = new GameObject("TestPlayerController");
        _playerControllerObject.AddComponent<Rigidbody2D>();
        PlayerController playerController = _playerControllerObject.AddComponent<PlayerController>();
        playerController.IsDead = true;

        Assert.IsFalse(playerController.IsFullyCommitted,
            "Precondition: IsFullyCommitted must be false so this test isolates the IsDead branch of the gate.");

        ForceSetPlayerController(_cameraFollow, playerController);
        ForceSetPanDirection(_cameraFollow, 1f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f, _cameraObject.transform.position.y, 0.01f,
            "Pan must be fully blocked while the assigned PlayerController's IsDead is true, independent of IsFullyCommitted.");
    }

    [Test]
    public void PanInput_IsNeverBlocked_WhenPlayerControllerUnassigned()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        _cameraFollow = _cameraObject.AddComponent<CameraFollow>();

        _targetObject = new GameObject("TestTarget");
        _targetObject.transform.position = new Vector3(5f, 0f, 0f);

        _cameraFollow.target = _targetObject.transform;

        // _playerController deliberately left at its default null - no ForceSetPlayerController call.
        ForceSetPanDirection(_cameraFollow, 1f);

        for (int i = 0; i < StepCount; i++)
        {
            _cameraFollow.Tick(FixedDeltaTime);
        }

        Assert.AreEqual(3f + 5f, _cameraObject.transform.position.y, 0.01f,
            "With no PlayerController assigned, pan must never be blocked - Y should converge normally to baseline + fallback distance.");
    }
}
