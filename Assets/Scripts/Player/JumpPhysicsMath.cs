/// <summary>
/// Pure jump-physics derivation formulas (Docs/Plans/002_quirrel-sprite-animation-player-control.md,
/// plan section 1.5, Task 3.3). Deliberately a plain static class with no UnityEngine/MonoBehaviour
/// dependency so it's EditMode-testable without a scene, per this project's "pure logic separated
/// from MonoBehaviour" convention.
///
/// This does NOT drive runtime behavior - PlayerController's serialized fields (_jumpVerticalVelocity,
/// etc.) and the Rigidbody2D.gravityScale set on the Player prefab (Task 3.8) are the actual runtime
/// values. This class exists solely to verify, by test, that those hand-picked numbers are internally
/// consistent with the plan's design targets (apex height 3.0 units, time-to-apex 0.4s) - see plan
/// section 1.5's worked-by-hand verification, preserved here as executable proof rather than just a
/// comment.
/// </summary>
public static class JumpPhysicsMath
{
    /// <summary>Unity's default Physics2D.gravity magnitude, used to derive Rigidbody2D.gravityScale from a target gravity magnitude.</summary>
    public const float UnityDefaultGravityMagnitude = 9.81f;

    /// <summary>v0 = 2h/t - initial vertical velocity needed to reach apexHeight in timeToApex seconds.</summary>
    public static float DeriveInitialVelocity(float apexHeight, float timeToApex)
    {
        return 2f * apexHeight / timeToApex;
    }

    /// <summary>g = 2h/t^2 - constant gravity magnitude needed to reach apexHeight in timeToApex seconds.</summary>
    public static float DeriveGravityMagnitude(float apexHeight, float timeToApex)
    {
        return 2f * apexHeight / (timeToApex * timeToApex);
    }

    /// <summary>Rigidbody2D.gravityScale required to realize gravityMagnitude given Unity's default Physics2D.gravity.</summary>
    public static float DeriveGravityScale(float gravityMagnitude, float unityDefaultGravityMagnitude = UnityDefaultGravityMagnitude)
    {
        return gravityMagnitude / unityDefaultGravityMagnitude;
    }
}
