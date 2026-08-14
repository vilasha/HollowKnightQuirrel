using UnityEngine;

/// <summary>
/// Minimal contract a seat prop (e.g. a bench) implements so <see cref="PlayerController"/>
/// can know which specific seat it's near/seated on without holding a concrete reference to
/// the seat's own type (Docs/Plans/009_bench-visual-fixes.md, Decision 1).
///
/// Defined in Quirrel.Player (not Quirrel.Environment) specifically to avoid a circular
/// assembly reference: Quirrel.Environment already references Quirrel.Player, so a concrete
/// PlayerController field typed as a Quirrel.Environment class would require the reverse
/// reference too, which Unity cannot compile. Quirrel.Environment's Bench class implements
/// this interface instead - a pure addition to the existing, correct dependency direction.
///
/// Exactly two members by design (YAGNI) - no speculative multi-seat-type hierarchy.
/// </summary>
public interface IBenchSeat
{
    /// <summary>The world-space Transform a sitting character should snap to.</summary>
    Transform SitAnchor { get; }

    /// <summary>Toggles whatever visual represents the seat prop (e.g. its SpriteRenderer).</summary>
    void SetVisible(bool visible);
}
