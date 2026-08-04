# Backlog

Feature ideas not yet drafted into a plan. When picked up, each item goes
through the normal pipeline (`implementation-plan-architect` →
`implementation-plan-reviewer` → committed plan file in `Docs/Plans/`)
before implementation starts.

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
