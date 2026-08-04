using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Task 4.1 (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Phase 4 - QA): contract tests
/// between PlayerController.cs's Animator parameter/state-name string constants and the actual
/// Assets/Animations/Quirrel.controller asset (plan section 1.7). These exist to fail loudly the
/// moment either side desyncs from a typo or a rename - Animator.SetBool/SetTrigger/etc. silently
/// no-op against an unknown parameter hash at runtime, and Animator.GetCurrentAnimatorStateInfo(0).
/// IsName(...) silently returns false against a renamed state, so neither failure mode throws or logs
/// anything on its own.
///
/// Approach for the parameter test: rather than parsing PlayerController.cs's IL to recover the
/// literal strings passed to Animator.StringToHash (fragile, compiler-dependent), this test uses
/// reflection to read the actual computed hash stored in each of PlayerController's private static
/// readonly *Hash fields, and compares it against Animator.StringToHash(expectedName) computed here
/// from a test-side list of the 10 expected names (plan section 1.7's contract table). Because
/// StringToHash is a deterministic hash of the string content, this closes the loop on both sides: a
/// typo in PlayerController.cs's Animator.StringToHash("...") call would produce a hash that does NOT
/// match this test's independently-computed expected hash, and a missing/renamed/mistyped parameter on
/// the AnimatorController asset is caught by the second half of the same test.
/// </summary>
public class AnimatorContractTests
{
    private const string ControllerAssetPath = "Assets/Animations/Quirrel.controller";

    /// <summary>
    /// The 10-parameter contract from plan section 1.7. FieldName is the private static readonly int
    /// field on PlayerController that stores Animator.StringToHash(ParameterName) - kept in sync with
    /// PlayerController.cs's field list by name; if PlayerController.cs ever renames one of these
    /// fields, FindHashField below will fail loudly rather than silently skipping the check.
    /// </summary>
    private static readonly (string FieldName, string ParameterName, AnimatorControllerParameterType Type)[] ExpectedParameters =
    {
        ("IsWalkingHash", "IsWalking", AnimatorControllerParameterType.Bool),
        ("IsGroundedHash", "IsGrounded", AnimatorControllerParameterType.Bool),
        ("VerticalVelocityHash", "VerticalVelocity", AnimatorControllerParameterType.Float),
        ("JumpTriggerHash", "JumpTrigger", AnimatorControllerParameterType.Trigger),
        ("AttackTriggerHash", "AttackTrigger", AnimatorControllerParameterType.Trigger),
        ("DefendHeldHash", "DefendHeld", AnimatorControllerParameterType.Bool),
        ("HurtTriggerHash", "HurtTrigger", AnimatorControllerParameterType.Trigger),
        ("HurtRecoveryTriggerHash", "HurtRecoveryTrigger", AnimatorControllerParameterType.Trigger),
        ("DieTriggerHash", "DieTrigger", AnimatorControllerParameterType.Trigger),
        ("IsDeadHash", "IsDead", AnimatorControllerParameterType.Bool),
    };

    private static AnimatorController LoadController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerAssetPath);
        Assert.IsNotNull(controller,
            $"Could not load an AnimatorController at '{ControllerAssetPath}' - has it moved or been renamed? " +
            "Update ControllerAssetPath in this test if the asset's path legitimately changed.");
        return controller;
    }

    private static int GetPlayerControllerHashFieldValue(string fieldName)
    {
        FieldInfo field = typeof(PlayerController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field,
            $"PlayerController no longer declares a private static field named '{fieldName}'. Either it was " +
            "renamed (update this test's ExpectedParameters list to match) or removed (the Animator parameter " +
            "it backed may no longer be set by code at all - a genuine regression worth investigating).");
        return (int)field.GetValue(null);
    }

    [Test]
    public void Controller_HasExactly10Parameters_MatchingPlayerControllerContract()
    {
        AnimatorController controller = LoadController();
        AnimatorControllerParameter[] actualParameters = controller.parameters;

        Assert.AreEqual(ExpectedParameters.Length, actualParameters.Length,
            $"Expected exactly {ExpectedParameters.Length} Animator parameters (plan section 1.7's contract) " +
            $"on '{ControllerAssetPath}', found {actualParameters.Length}. A parameter was added or removed on " +
            "only one side of the PlayerController/AnimatorController contract.");

        foreach ((string fieldName, string parameterName, AnimatorControllerParameterType type) in ExpectedParameters)
        {
            // Side 1: PlayerController.cs's own hash field must actually encode this exact parameter
            // name - catches a typo inside PlayerController.cs's Animator.StringToHash("...") call
            // that the controller-side check below cannot see (the controller can be perfectly correct
            // while PlayerController.cs is silently hashing a misspelled string).
            int actualHash = GetPlayerControllerHashFieldValue(fieldName);
            int expectedHash = Animator.StringToHash(parameterName);
            Assert.AreEqual(expectedHash, actualHash,
                $"PlayerController.{fieldName} does not equal Animator.StringToHash(\"{parameterName}\") - " +
                $"the string literal passed to StringToHash for this field has drifted from the plan section " +
                "1.7 contract name, and Animator.Set* calls using this hash will silently no-op at runtime.");

            // Side 2: the AnimatorController asset must actually declare this parameter, with the
            // expected type.
            AnimatorControllerParameter match = System.Array.Find(actualParameters, p => p.name == parameterName);
            Assert.IsNotNull(match,
                $"Expected Animator parameter '{parameterName}' not found on '{ControllerAssetPath}'. " +
                "PlayerController.cs references this parameter by name - a rename on the controller side " +
                "will silently no-op the corresponding Animator.Set* calls at runtime.");
            Assert.AreEqual(type, match.type,
                $"Animator parameter '{parameterName}' has type {match.type} on the controller, but plan " +
                $"section 1.7's contract (and PlayerController.cs's usage) expects {type}.");
        }
    }

    [Test]
    public void Controller_ContainsJumpAnticipationState_MatchingPlayerControllerConstant()
    {
        FieldInfo field = typeof(PlayerController).GetField(
            "JumpAnticipationStateName", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field,
            "PlayerController no longer declares a private const 'JumpAnticipationStateName' field - " +
            "update this test if it was renamed or removed.");
        string expectedStateName = (string)field.GetValue(null);

        AnimatorController controller = LoadController();

        bool found = false;
        foreach (ChildAnimatorState childState in controller.layers[0].stateMachine.states)
        {
            if (childState.state.name == expectedStateName)
            {
                found = true;
                break;
            }
        }

        Assert.IsTrue(found,
            $"Expected a state named '{expectedStateName}' in '{ControllerAssetPath}''s base layer (layer 0) - " +
            "PlayerController's delayed jump-impulse cancellation logic (plan sections 1.4/1.9) checks " +
            $"Animator.GetCurrentAnimatorStateInfo(0).IsName(\"{expectedStateName}\") and will silently never " +
            "cancel the impulse (or worse, always cancel it) if this state is renamed without updating that check.");
    }
}
