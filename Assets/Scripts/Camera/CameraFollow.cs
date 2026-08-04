using UnityEngine;

/// <summary>
/// X-axis-only camera follow (Docs/Plans/002_quirrel-sprite-animation-player-control.md,
/// section 1.15 / Task 3.7).
///
/// Deliberately decoupled from PlayerController: this component only knows about a
/// generic <see cref="Transform"/> assigned via the Inspector (or set at runtime), never
/// about the player specifically. Y and Z are frozen at whatever this GameObject's
/// position is the first time it runs (Awake, or the first manual <see cref="Tick"/>
/// call for tests) - the scene wiring task (3.8) is responsible for placing the camera
/// at the desired Z (-10 for a standard 2D orthographic setup), not this script.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    /// <summary>
    /// Transform this camera follows on the X axis. Must be assigned in the Inspector
    /// (or set at runtime) - intentionally no hardcoded scene reference.
    /// </summary>
    public Transform target;

    /// <summary>SmoothDamp smoothing time, in seconds, for the X-axis follow (fixed at 0.15s per plan section 1.15).</summary>
    private const float SmoothTime = 0.15f;

    private float _velocityX;
    private float _fixedY;
    private float _fixedZ;
    private bool _initialized;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances the X-axis SmoothDamp by one step of <paramref name="deltaTime"/> seconds.
    /// Exposed as a public method (rather than folded only into LateUpdate) specifically so
    /// EditMode tests can simulate repeated fixed-step calls without needing Play Mode.
    /// No-ops if <see cref="target"/> is unassigned.
    /// </summary>
    public void Tick(float deltaTime)
    {
        EnsureInitialized();

        if (target == null)
        {
            return;
        }

        Vector3 position = transform.position;
        float newX = Mathf.SmoothDamp(position.x, target.position.x, ref _velocityX, SmoothTime, Mathf.Infinity, deltaTime);
        transform.position = new Vector3(newX, _fixedY, _fixedZ);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Vector3 position = transform.position;
        _fixedY = position.y;
        _fixedZ = position.z;
        _initialized = true;
    }
}
