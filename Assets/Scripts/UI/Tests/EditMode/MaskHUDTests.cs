using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// EditMode tests for MaskHUD (Docs/Plans/010_health-mask-system.md, Tasks 4.3/5.5). Constructs a bare
/// GameObject hierarchy directly (9 Image components on child GameObjects, a PlayerHealth) without
/// loading any scene, per Task 5.5's own instruction.
/// </summary>
public class MaskHUDTests
{
    private GameObject _hudObject;
    private GameObject _playerObject;
    private GameObject[] _pipObjects;
    private Sprite _filledSprite;
    private Sprite _emptySprite;

    [TearDown]
    public void TearDown()
    {
        if (_hudObject != null) Object.DestroyImmediate(_hudObject);
        if (_playerObject != null) Object.DestroyImmediate(_playerObject);
        if (_pipObjects != null)
        {
            foreach (GameObject pip in _pipObjects)
            {
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }
        if (_filledSprite != null) Object.DestroyImmediate(_filledSprite);
        if (_emptySprite != null) Object.DestroyImmediate(_emptySprite);
    }

    private static void InvokeAwake(PlayerHealth health)
    {
        MethodInfo method = typeof(PlayerHealth).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(health, null);
    }

    /// <summary>
    /// Builds a bare Player rig and forces PlayerHealth's Awake()-only initialization directly via
    /// reflection - it never fires via AddComponent alone in this project's EditMode tests (the
    /// project's own confirmed EditMode-Awake gotcha; see PlayerHealthTests.cs's own doc comment).
    /// </summary>
    private PlayerHealth CreatePlayerHealth()
    {
        _playerObject = new GameObject("TestPlayer");
        _playerObject.AddComponent<Rigidbody2D>();
        _playerObject.AddComponent<SpriteRenderer>();
        _playerObject.AddComponent<PlayerController>();
        PlayerHealth health = _playerObject.AddComponent<PlayerHealth>();
        InvokeAwake(health);
        return health;
    }

    private Image[] CreatePipSlots(int count)
    {
        _pipObjects = new GameObject[count];
        var images = new Image[count];
        for (int i = 0; i < count; i++)
        {
            _pipObjects[i] = new GameObject($"Pip{i}");
            images[i] = _pipObjects[i].AddComponent<Image>();
        }
        return images;
    }

    private static void ForceSetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(target, value);
    }

    /// <summary>
    /// Directly invokes MaskHUD's real, private OnEnable() via reflection - the same technique used for
    /// PlayerHealth's Awake()/OnEnable() elsewhere in this project's EditMode suites. Toggling the public
    /// <c>enabled</c> property was tried first but does not reliably fire OnEnable synchronously in this
    /// Editor test context (confirmed empirically - tests relying on it alone saw stale/default field
    /// reads), the same underlying class of gap as Awake() never firing for a bare AddComponent instance
    /// outside Play Mode.
    /// </summary>
    private static void InvokeOnEnable(MaskHUD hud)
    {
        MethodInfo method = typeof(MaskHUD).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(hud, null);
    }

    /// <summary>Same reflection-invocation technique as <see cref="InvokeOnEnable"/>, for OnDisable.</summary>
    private static void InvokeOnDisable(MaskHUD hud)
    {
        MethodInfo method = typeof(MaskHUD).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(hud, null);
    }

    /// <summary>
    /// Builds a MaskHUD with the given dependencies force-assigned via reflection (mirroring this
    /// codebase's other "assign in Inspector" serialized-field test conventions, e.g. BenchTests.cs's
    /// ForceSetSitAnchor), then directly invokes the real OnEnable() so the pip-slots-length validation
    /// and OnHealthChanged subscription run against the now fully-populated fields.
    /// </summary>
    private MaskHUD CreateHud(PlayerHealth playerHealth, Image[] pipSlots)
    {
        _hudObject = new GameObject("TestMaskHUD");
        _filledSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        _emptySprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        MaskHUD hud = _hudObject.AddComponent<MaskHUD>();
        ForceSetField(hud, "_playerHealth", playerHealth);
        ForceSetField(hud, "_pipSlots", pipSlots);
        ForceSetField(hud, "_filledSprite", _filledSprite);
        ForceSetField(hud, "_emptySprite", _emptySprite);

        InvokeOnEnable(hud);

        return hud;
    }

    [Test]
    public void Refresh_FreshHealth_Pips0To3ActiveFilled_Pips4To8Inactive()
    {
        PlayerHealth health = CreatePlayerHealth(); // MaxMasks == 4, CurrentQuarterMasks == 16
        Image[] pips = CreatePipSlots(9);
        MaskHUD hud = CreateHud(health, pips);

        hud.Refresh();

        for (int i = 0; i < 4; i++)
        {
            Assert.IsTrue(pips[i].gameObject.activeSelf, $"Pip {i} should be active (within MaxMasks=4).");
            Assert.AreEqual(_filledSprite, pips[i].sprite, $"Pip {i} should show the filled sprite (fully healed).");
        }
        for (int i = 4; i < 9; i++)
        {
            Assert.IsFalse(pips[i].gameObject.activeSelf, $"Pip {i} should be inactive (beyond MaxMasks=4).");
        }
    }

    /// <summary>The rounds-down proof: 9 quarter-masks / 4 QuartersPerMask = 2 filled pips, not 2.25.</summary>
    [Test]
    public void Refresh_PartialHealth_RoundsDownFilledPipCount()
    {
        PlayerHealth health = CreatePlayerHealth();
        health.ApplyDamage(7); // 16 -> 9 quarter-masks
        Assert.AreEqual(9, health.CurrentQuarterMasks, "Precondition.");
        Image[] pips = CreatePipSlots(9);
        MaskHUD hud = CreateHud(health, pips);

        hud.Refresh();

        Assert.AreEqual(_filledSprite, pips[0].sprite);
        Assert.AreEqual(_filledSprite, pips[1].sprite);
        Assert.AreEqual(_emptySprite, pips[2].sprite);
        Assert.AreEqual(_emptySprite, pips[3].sprite);
        for (int i = 0; i < 4; i++)
        {
            Assert.IsTrue(pips[i].gameObject.activeSelf, $"Pip {i} should be active (within MaxMasks=4).");
        }
        for (int i = 4; i < 9; i++)
        {
            Assert.IsFalse(pips[i].gameObject.activeSelf, $"Pip {i} should be inactive (beyond MaxMasks=4).");
        }
    }

    [Test]
    public void Refresh_MaxMasksAtCap_AllNinePipsActive()
    {
        PlayerHealth health = CreatePlayerHealth();
        int callsNeeded = (PlayerHealth.MaxMasksCap - health.MaxMasks) * health.ShardsPerMask;
        for (int i = 0; i < callsNeeded; i++)
        {
            health.AddMaskShard();
        }
        Assert.AreEqual(PlayerHealth.MaxMasksCap, health.MaxMasks, "Precondition: MaxMasks should have reached the cap.");
        Image[] pips = CreatePipSlots(9);
        MaskHUD hud = CreateHud(health, pips);

        hud.Refresh();

        for (int i = 0; i < 9; i++)
        {
            Assert.IsTrue(pips[i].gameObject.activeSelf, $"Pip {i} should be active with MaxMasks==9.");
        }
    }

    [Test]
    public void Refresh_WithNullPlayerHealth_DoesNotThrow()
    {
        Image[] pips = CreatePipSlots(9);
        MaskHUD hud = CreateHud(null, pips);

        LogAssert.Expect(LogType.Error, new Regex("no PlayerHealth assigned"));
        Assert.DoesNotThrow(() => hud.Refresh());
    }

    [Test]
    public void OnEnable_WithWrongLengthPipSlotsArray_LogsError_RefreshSkipsPipLoop()
    {
        PlayerHealth health = CreatePlayerHealth();
        Image[] pips = CreatePipSlots(5); // wrong length - PlayerHealth.MaxMasksCap is 9

        LogAssert.Expect(LogType.Error, new Regex("expected exactly 9"));
        MaskHUD hud = CreateHud(health, pips);

        Assert.DoesNotThrow(() => hud.Refresh(),
            "Refresh() must skip the pip loop entirely (not under/over-index) when _pipSlots was not confirmed to be exactly 9 long.");
    }

    [Test]
    public void OnDisable_Unsubscribes_HealthChangeAfterDisable_DoesNotTriggerRefresh()
    {
        PlayerHealth health = CreatePlayerHealth();
        Image[] pips = CreatePipSlots(9);
        MaskHUD hud = CreateHud(health, pips);
        hud.Refresh(); // baseline: all 4 starting pips show filled
        Assert.AreEqual(_filledSprite, pips[0].sprite, "Precondition: pip 0 should be filled before any damage.");

        InvokeOnDisable(hud); // unsubscribes OnHealthChanged -= Refresh
        health.ApplyDamage(4); // would normally raise OnHealthChanged -> Refresh(), if still subscribed

        Assert.AreEqual(_filledSprite, pips[0].sprite,
            "OnDisable must unsubscribe from OnHealthChanged - a health change afterward must not auto-refresh the HUD.");
    }
}
