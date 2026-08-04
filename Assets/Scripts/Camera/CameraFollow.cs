using UnityEngine;

/// <summary>
/// X-axis-only camera follow (Docs/Plans/002_quirrel-sprite-animation-player-control.md,
/// section 1.15 / Task 3.7), plus a gated W/S look-up/down pan on the Y axis
/// (Docs/Plans/006_look-up-down-camera-pan.md).
///
/// The X/Y-follow-target logic itself remains decoupled: this component only knows about
/// a generic <see cref="Transform"/> assigned via the Inspector (or set at runtime), never
/// about the player specifically. The one exception is <see cref="_playerController"/>: a
/// narrow, read-only, optional field used solely to gate camera panning, reading only its
/// public <c>IsDead</c> and <c>IsFullyCommitted</c> members. It is never required - if left
/// unassigned in the Inspector, panning is simply never blocked (a documented, deliberate
/// fallback, not an oversight). Z is frozen at whatever this GameObject's position is the
/// first time it runs (Awake, or the first manual <see cref="Tick"/> call for tests) - the
/// scene wiring task (3.8) is responsible for placing the camera at the desired Z (-10 for
/// a standard 2D orthographic setup), not this script.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    /// <summary>
    /// Transform this camera follows on the X axis. Must be assigned in the Inspector
    /// (or set at runtime) - intentionally no hardcoded scene reference.
    /// </summary>
    public Transform target;

    /// <summary>
    /// Optional, read-only source for pan-gating. Read only for its public <c>IsDead</c>
    /// and <c>IsFullyCommitted</c> members - never required, and every read of it is
    /// null-checked. If left unassigned, panning is never blocked.
    /// </summary>
    [SerializeField] private PlayerController _playerController;

    /// <summary>SmoothDamp smoothing time, in seconds, for the X-axis follow (fixed at 0.15s per plan section 1.15).</summary>
    private const float SmoothTime = 0.15f;

    /// <summary>
    /// Scales the pan distance to 40% of full screen height (0.8 * orthographicSize), since
    /// orthographicSize is half the vertical viewport in world units (Docs/Plans/007, Task 5.1).
    /// </summary>
    private const float PanDistanceScale = 0.8f;

    /// <summary>Fallback pan distance used only when no <see cref="Camera"/> component is present on this GameObject.</summary>
    [SerializeField] private float _panDistanceFallback = 5f;

    private float _velocityX;
    private float _velocityY;
    private float _panDirection;
    private float _baseY;
    private float _fixedZ;
    private Camera _camera;
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
    /// Reads raw W/S pan input every frame into <see cref="_panDirection"/>. Kept separate
    /// from <see cref="Tick"/> because <c>Update</c> never runs in EditMode tests - the
    /// actual pan-gating logic lives in <see cref="Tick"/> so it remains directly testable.
    /// </summary>
    private void Update()
    {
        _panDirection = 0f;

        if (Input.GetKey(KeyCode.W))
        {
            _panDirection += 1f;
        }

        if (Input.GetKey(KeyCode.S))
        {
            _panDirection -= 1f;
        }
    }

    /// <summary>
    /// Advances the X-axis SmoothDamp by one step of <paramref name="deltaTime"/> seconds,
    /// and the gated Y-axis pan by the same step. Exposed as a public method (rather than
    /// folded only into LateUpdate) specifically so EditMode tests can simulate repeated
    /// fixed-step calls without needing Play Mode. No-ops if <see cref="target"/> is
    /// unassigned.
    /// </summary>
    public void Tick(float deltaTime)
    {
        EnsureInitialized();

        if (target == null)
        {
            return;
        }

        bool panBlocked = _playerController != null && (_playerController.IsDead || _playerController.IsFullyCommitted);
        float panDirection = panBlocked ? 0f : _panDirection;
        float panDistance = (_camera != null ? _camera.orthographicSize : _panDistanceFallback) * PanDistanceScale;
        float targetY = _baseY + panDirection * panDistance;

        Vector3 position = transform.position;
        float newX = Mathf.SmoothDamp(position.x, target.position.x, ref _velocityX, SmoothTime, Mathf.Infinity, deltaTime);
        float newY = Mathf.SmoothDamp(position.y, targetY, ref _velocityY, SmoothTime, Mathf.Infinity, deltaTime);
        transform.position = new Vector3(newX, newY, _fixedZ);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Vector3 position = transform.position;
        _baseY = position.y;
        _fixedZ = position.z;
        _camera = GetComponent<Camera>();
        _initialized = true;
    }
}
