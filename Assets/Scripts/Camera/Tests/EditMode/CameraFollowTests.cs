using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for CameraFollow (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Task 3.7).
/// </summary>
public class CameraFollowTests
{
    private const float FixedDeltaTime = 0.02f;
    private const int StepCount = 300; // 6 simulated seconds - far beyond SmoothDamp's 0.15s time constant, comfortable convergence margin

    private GameObject _cameraObject;
    private GameObject _targetObject;
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

    [Test]
    public void YAndZPosition_StayFixed_WhileFollowingOnX()
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
        Assert.AreEqual(3f, finalPosition.y, 0.0001f, "Camera Y must stay fixed at its starting value.");
        Assert.AreEqual(-10f, finalPosition.z, 0.0001f, "Camera Z must stay fixed at its starting value.");
    }
}
