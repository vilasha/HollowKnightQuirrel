# Backlog

Feature ideas not yet drafted into a plan. When picked up, each item goes
through the normal pipeline (`implementation-plan-architect` →
`implementation-plan-reviewer` → committed plan file in `Docs/Plans/`)
before implementation starts.

## Attack while jumping

Currently the player cannot attack mid-jump (`TryAttack` requires
`isGrounded`, per `Assets/Scripts/Player/PlayerController.cs`). Requested
behavior: while airborne (jump in progress), pressing the attack button
should:
- Let the jump's physics trajectory continue uninterrupted (position/velocity
  unaffected).
- Switch the visible animation to the Attack animation.
- Once the Attack animation finishes, resume the jump animation
  (JumpRise/JumpFall, whichever matches current vertical velocity) rather
  than snapping back to Idle/Walk.

Touches likely: `PlayerController.cs`'s full-commit gating (`IsFullyCommitted`
currently freezes movement during Attack, which conflicts with "trajectory
continues" — needs a jump-specific carve-out), `Assets/Animations/Quirrel.controller`
(new transitions so Attack can be entered from JumpRise/JumpFall and exit back
to the correct one, not just Idle), and regression coverage in the existing
Player test suites.

## Look up/down (camera pan)

Requested behavior:
- Press and hold `S`: camera slides down by half a screen height; release `S`:
  camera slides back to its normal position.
- Press and hold `W`: camera slides up by half a screen height; release `W`:
  camera slides back to its normal position.

Touches likely: a new or existing camera-follow script (check
`Assets/Scripts/Camera/`), plus deciding on slide speed/easing and whether
W/S should be blocked during any of the existing full-commit states
(Attack/Defend/Hurt/Die) or during a jump.
