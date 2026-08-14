using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Upper-left Mask-pip HUD row (Docs/Plans/010_health-mask-system.md, Task 4.3).
///
/// Renders <see cref="PlayerHealth.MaxMasks"/>/<see cref="PlayerHealth.CurrentQuarterMasks"/> as a row of
/// pre-authored <see cref="Image"/> pip slots (Decision 13) - not runtime-Instantiate'd from a prefab,
/// matching <see cref="Bench"/>'s "assign in Inspector, defensive fallback/assert if misconfigured"
/// convention. Only 2 pip states exist - filled and empty (Decision 8) - no partial/"cracked" pip art.
///
/// Entirely driven by <see cref="PlayerHealth.OnHealthChanged"/> (no per-frame polling), plus one direct
/// <see cref="Refresh"/> call from <see cref="Start"/> to cover the case where the event hasn't fired yet
/// by the time this component first enables (e.g. the very first frame).
/// </summary>
public class MaskHUD : MonoBehaviour
{
    [SerializeField] private PlayerHealth _playerHealth;

    /// <summary>
    /// Expected length is <see cref="PlayerHealth.MaxMasksCap"/> (9) - a hand-authored, serialized
    /// Inspector array (Decision 13), so a scene-editing mistake could still mis-size it independent of
    /// the shared compile-time cap (Decision 7). Validated in <see cref="OnEnable"/>.
    /// </summary>
    [SerializeField] private Image[] _pipSlots;

    [SerializeField] private Sprite _filledSprite;
    [SerializeField] private Sprite _emptySprite;

    /// <summary>
    /// True once <see cref="_pipSlots"/> has been confirmed to be exactly
    /// <see cref="PlayerHealth.MaxMasksCap"/> long. When false, <see cref="Refresh"/> skips pip updates
    /// entirely rather than under/over-indexing - the misconfiguration is already loudly logged once by
    /// <see cref="OnEnable"/>, so this avoids re-logging on every subsequent Refresh() call.
    /// </summary>
    private bool _pipSlotsValid;

    private void OnEnable()
    {
        _pipSlotsValid = _pipSlots != null && _pipSlots.Length == PlayerHealth.MaxMasksCap;
        if (!_pipSlotsValid)
        {
            Debug.LogError(
                $"MaskHUD '{name}' has {(_pipSlots == null ? 0 : _pipSlots.Length)} pip slot(s) assigned, " +
                $"expected exactly {PlayerHealth.MaxMasksCap} (PlayerHealth.MaxMasksCap). Pip updates will " +
                "be skipped until this is fixed (Docs/Plans/010_health-mask-system.md, Decision 7).",
                this);
        }

        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged += Refresh;
        }
    }

    private void OnDisable()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged -= Refresh;
        }
    }

    private void Start()
    {
        // Covers the case where OnHealthChanged hasn't fired yet by the time this component first
        // enables (e.g. the very first frame) - Refresh() is otherwise entirely event-driven.
        Refresh();
    }

    /// <summary>
    /// Re-renders every pip slot from the current <see cref="PlayerHealth"/> state. No-ops (logged, not
    /// thrown) if <see cref="_playerHealth"/> is unset - matches this codebase's established "no-op
    /// rather than throw for an optional/unset dependency" convention (see PlayerController's null-
    /// Animator checks). Skips the pip loop entirely (rather than under/over-indexing) if
    /// <see cref="_pipSlots"/> was not confirmed to be exactly <see cref="PlayerHealth.MaxMasksCap"/>
    /// long in <see cref="OnEnable"/>.
    /// </summary>
    public void Refresh()
    {
        if (_playerHealth == null)
        {
            Debug.LogError($"MaskHUD '{name}' has no PlayerHealth assigned - cannot refresh.", this);
            return;
        }

        if (!_pipSlotsValid)
        {
            return;
        }

        int filledPipCount = _playerHealth.CurrentQuarterMasks / PlayerHealth.QuartersPerMask;

        for (int i = 0; i < _pipSlots.Length; i++)
        {
            bool slotActive = i < _playerHealth.MaxMasks;
            _pipSlots[i].gameObject.SetActive(slotActive);

            if (slotActive)
            {
                _pipSlots[i].sprite = i < filledPipCount ? _filledSprite : _emptySprite;
            }
        }
    }
}
