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
        ("LookingUpHash", "LookingUp", AnimatorControllerParameterType.Bool),
        ("LookingDownHash", "LookingDown", AnimatorControllerParameterType.Bool),
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

    // -------------------------------------------------------------------
    // Task 3.2 (Docs/Plans/005_attack-while-jumping.md): contract test locking in Attack's rerouted
    // exit transitions (Task 2.1). Same failure-mode reasoning as the tests above: an Animator
    // transition edit compiles fine and fails silently at runtime if wrong.
    // -------------------------------------------------------------------

    private static AnimatorState FindStateByName(AnimatorController controller, string stateName)
    {
        foreach (ChildAnimatorState childState in controller.layers[0].stateMachine.states)
        {
            if (childState.state.name == stateName)
            {
                return childState.state;
            }
        }

        return null;
    }

    private static void AssertTransition(
        AnimatorStateTransition transition,
        string expectedDestinationStateName,
        (AnimatorConditionMode Mode, string Parameter, float Threshold)[] expectedConditions,
        string transitionLabel)
    {
        Assert.IsNotNull(transition.destinationState,
            $"{transitionLabel}: expected a destinationState (not a sub-state-machine or exit) named " +
            $"'{expectedDestinationStateName}'.");
        Assert.AreEqual(expectedDestinationStateName, transition.destinationState.name,
            $"{transitionLabel}: destination state name mismatch.");

        AnimatorCondition[] actualConditions = transition.conditions;
        Assert.AreEqual(expectedConditions.Length, actualConditions.Length,
            $"{transitionLabel}: expected exactly {expectedConditions.Length} condition(s), found {actualConditions.Length}.");

        for (int i = 0; i < expectedConditions.Length; i++)
        {
            Assert.AreEqual(expectedConditions[i].Mode, actualConditions[i].mode,
                $"{transitionLabel}: condition {i} mode mismatch.");
            Assert.AreEqual(expectedConditions[i].Parameter, actualConditions[i].parameter,
                $"{transitionLabel}: condition {i} parameter mismatch.");
            Assert.AreEqual(expectedConditions[i].Threshold, actualConditions[i].threshold, 0.0001f,
                $"{transitionLabel}: condition {i} threshold mismatch.");
        }
    }

    /// <summary>
    /// Locks in plan 005 Task 2.1's exact transition topology: Attack must have exactly 3 outgoing
    /// transitions, in list order Idle/JumpRise/JumpFall (order is load-bearing - Mecanim evaluates a
    /// state's outgoing transitions in list order and takes the first whose conditions are all
    /// satisfied once hasExitTime is reached), with the exact conditions the plan specifies.
    /// </summary>
    [Test]
    public void AttackState_HasExactlyThreeOutgoingTransitions_ToIdleJumpRiseJumpFall_InOrder()
    {
        AnimatorController controller = LoadController();

        AnimatorState attackState = FindStateByName(controller, "Attack");
        Assert.IsNotNull(attackState,
            $"Expected an 'Attack' state on '{ControllerAssetPath}''s base layer (layer 0) - plan 005 Task 2.1 " +
            "reroutes this state's outgoing transitions.");

        AnimatorStateTransition[] transitions = attackState.transitions;
        Assert.AreEqual(3, transitions.Length,
            "Attack should have exactly 3 outgoing transitions (plan 005 Task 2.1: Idle/JumpRise/JumpFall) - " +
            $"found {transitions.Length}. A transition was added or removed without updating this contract test.");

        AssertTransition(transitions[0], "Idle", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "IsGrounded", 0f),
        }, "Attack -> Idle");

        AssertTransition(transitions[1], "JumpRise", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.IfNot, "IsGrounded", 0f),
            (AnimatorConditionMode.Greater, "VerticalVelocity", 0.1f),
        }, "Attack -> JumpRise");

        AssertTransition(transitions[2], "JumpFall", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.IfNot, "IsGrounded", 0f),
        }, "Attack -> JumpFall");
    }

    // -------------------------------------------------------------------
    // Task 6.2 (Docs/Plans/007_look-up-down-idle-animation-and-pan-tuning.md): contract tests locking
    // in Task 3.1's Idle/LookUp/LookDown transition topology. Same failure-mode reasoning as the tests
    // above: an Animator transition edit compiles fine and fails silently at runtime if wrong.
    // -------------------------------------------------------------------

    /// <summary>
    /// Locks in plan 007 Task 3.1's exact transition topology: Idle must have exactly 5 outgoing
    /// transitions, in list order Walk/JumpAnticipation/DefendRaise/LookUp/LookDown (order is
    /// load-bearing - Mecanim evaluates a state's outgoing transitions in list order and takes the
    /// first whose conditions are all satisfied), with the exact conditions the plan specifies. The
    /// existing 3 transitions (Walk/JumpAnticipation/DefendRaise) must remain byte-identical in order,
    /// condition, and destination to their pre-plan-007 state - the 2 new ones are appended last.
    /// </summary>
    [Test]
    public void IdleState_HasExactlyFiveOutgoingTransitions_InOrder_IncludingLookUpAndLookDown()
    {
        AnimatorController controller = LoadController();

        AnimatorState idleState = FindStateByName(controller, "Idle");
        Assert.IsNotNull(idleState,
            $"Expected an 'Idle' state on '{ControllerAssetPath}''s base layer (layer 0) - plan 007 Task 3.1 " +
            "appends 2 new outgoing transitions to it.");

        AnimatorStateTransition[] transitions = idleState.transitions;
        Assert.AreEqual(5, transitions.Length,
            "Idle should have exactly 5 outgoing transitions (plan 007 Task 3.1: Walk/JumpAnticipation/" +
            $"DefendRaise/LookUp/LookDown) - found {transitions.Length}. A transition was added or removed " +
            "without updating this contract test.");

        AssertTransition(transitions[0], "Walk", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "IsWalking", 0f),
        }, "Idle -> Walk");

        AssertTransition(transitions[1], "JumpAnticipation", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "JumpTrigger", 0f),
        }, "Idle -> JumpAnticipation");

        AssertTransition(transitions[2], "DefendRaise", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "DefendHeld", 0f),
        }, "Idle -> DefendRaise");

        AssertTransition(transitions[3], "LookUp", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "LookingUp", 0f),
        }, "Idle -> LookUp");

        AssertTransition(transitions[4], "LookDown", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "LookingDown", 0f),
        }, "Idle -> LookDown");
    }

    /// <summary>
    /// Locks in plan 007 Task 3.1's exact transition topology for the new LookUp state: 4 outgoing
    /// transitions in list order Walk/JumpAnticipation/DefendRaise/Idle, mirroring Idle's first 3
    /// transitions exactly (by destination/condition/timing) before the release edge back to Idle.
    /// </summary>
    [Test]
    public void LookUpState_HasExactlyFourOutgoingTransitions_InOrder()
    {
        AnimatorController controller = LoadController();

        AnimatorState lookUpState = FindStateByName(controller, "LookUp");
        Assert.IsNotNull(lookUpState,
            $"Expected a 'LookUp' state on '{ControllerAssetPath}''s base layer (layer 0) - plan 007 Task 3.1 " +
            "adds this state.");

        AnimatorStateTransition[] transitions = lookUpState.transitions;
        Assert.AreEqual(4, transitions.Length,
            "LookUp should have exactly 4 outgoing transitions (plan 007 Task 3.1: Walk/JumpAnticipation/" +
            $"DefendRaise/Idle) - found {transitions.Length}. A transition was added or removed without " +
            "updating this contract test.");

        AssertTransition(transitions[0], "Walk", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "IsWalking", 0f),
        }, "LookUp -> Walk");

        AssertTransition(transitions[1], "JumpAnticipation", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "JumpTrigger", 0f),
        }, "LookUp -> JumpAnticipation");

        AssertTransition(transitions[2], "DefendRaise", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "DefendHeld", 0f),
        }, "LookUp -> DefendRaise");

        AssertTransition(transitions[3], "Idle", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.IfNot, "LookingUp", 0f),
        }, "LookUp -> Idle");
    }

    /// <summary>
    /// Locks in plan 007 Task 3.1's exact transition topology for the new LookDown state: 4 outgoing
    /// transitions in list order Walk/JumpAnticipation/DefendRaise/Idle, symmetric to LookUp's using
    /// LookingDown instead of LookingUp.
    /// </summary>
    [Test]
    public void LookDownState_HasExactlyFourOutgoingTransitions_InOrder()
    {
        AnimatorController controller = LoadController();

        AnimatorState lookDownState = FindStateByName(controller, "LookDown");
        Assert.IsNotNull(lookDownState,
            $"Expected a 'LookDown' state on '{ControllerAssetPath}''s base layer (layer 0) - plan 007 Task 3.1 " +
            "adds this state.");

        AnimatorStateTransition[] transitions = lookDownState.transitions;
        Assert.AreEqual(4, transitions.Length,
            "LookDown should have exactly 4 outgoing transitions (plan 007 Task 3.1: Walk/JumpAnticipation/" +
            $"DefendRaise/Idle) - found {transitions.Length}. A transition was added or removed without " +
            "updating this contract test.");

        AssertTransition(transitions[0], "Walk", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "IsWalking", 0f),
        }, "LookDown -> Walk");

        AssertTransition(transitions[1], "JumpAnticipation", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "JumpTrigger", 0f),
        }, "LookDown -> JumpAnticipation");

        AssertTransition(transitions[2], "DefendRaise", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.If, "DefendHeld", 0f),
        }, "LookDown -> DefendRaise");

        AssertTransition(transitions[3], "Idle", new (AnimatorConditionMode, string, float)[]
        {
            (AnimatorConditionMode.IfNot, "LookingDown", 0f),
        }, "LookDown -> Idle");
    }
}
