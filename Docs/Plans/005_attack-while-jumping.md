# Implementation Plan — Attack While Jumping

**Status:** ✅ APPROVED — implementation-plan-reviewer, round 2
**Author:** implementation-plan-architect
**Date:** 2026-08-04
**Feature:** `Docs/Backlog.md` → "Attack while jumping". Currently `TryAttack()`
requires `isGrounded`. New behavior: pressing Attack (J) while airborne lets
the jump's physics trajectory continue completely uninterrupted (no freezing
of horizontal or vertical velocity), switches the visible animation to
Attack, and once the Attack clip finishes, resumes the correct jump animation
(JumpRise or JumpFall, matching current vertical velocity) instead of
snapping to Idle/Walk. Confirmed design decision: air-attacks keep live
horizontal air control (same as a normal jump), not frozen movement.

## 0. Summary and verified blast radius

**Confirmed from the actual source:**

- `TryAttack(bool isGrounded)` gate is exactly
  `if (IsDead || DefendHeld || !isGrounded || _isAttacking || _isHurtStunned) return false;`
  — `!isGrounded` is the literal line to remove.
- `IsFullyCommitted => _isAttacking || DefendHeld || _isHurtStunned` freezes
  horizontal velocity in `ApplyHorizontalMovement` (forces `desiredX = 0`),
  blocks `TryJump`, gates horizontal-input reading in `Update()`, and gates
  `IsWalking` in `UpdateAnimatorParameters`. Today `_isAttacking` always
  implies grounded (since `TryAttack` currently requires `isGrounded`) —
  this feature breaks that implication for the first time, so any
  redefinition must key off **live** grounded state, not a flag latched at
  attack-start, or a player who lands mid-air-attack (plausible near the end
  of a fall, well before the Attack clip's exit time) will stay un-frozen on
  the ground for the remainder of the swing — a case that literally could
  not happen before this feature. Confirmed via direct read of
  `FixedUpdate()`: `ApplyHorizontalMovement` is called **before**
  `IsGrounded` is refreshed for the current frame (`IsGrounded =
  CheckGrounded()` runs immediately after), so re-engaging the freeze off
  live `IsGrounded` carries one `FixedUpdate` frame of lag on the landing
  edge — an already-accepted pattern in this file (see its own comment on
  `Update()`'s "one-frame lag against a physics value... is the standard,
  acceptable pattern").
- `DefendHeld` is already computed as `... && IsGrounded && ...`, so it is
  always `false` while airborne — no interaction with air-attack to worry
  about.
- `TryJump`'s own guard already includes `!isGrounded` as its first
  condition, checked independently of `IsFullyCommitted`. This means an
  air-attack can never open a double-jump hole through `IsFullyCommitted`
  alone — `!isGrounded` already blocks a second jump attempt regardless of
  how `IsFullyCommitted` is redefined. Confirmed by reading `TryJump` directly.
- `UpdatePhysicsAnimatorParameters()` (called every `FixedUpdate`,
  unconditionally, regardless of Animator state) already writes `IsGrounded`
  (Bool) and `VerticalVelocity` (Float) onto the Animator every frame,
  **including while the Attack state is playing**. This means the Attack
  clip's exit transitions can route to Idle/JumpRise/JumpFall using
  **parameters that already exist and are already live** — no new Animator
  parameter is needed for this feature.
- `Quirrel.controller`'s `AnyState -> Attack` transition is already
  unconditional (Attack state, fileID `1102006`, is targeted by the existing
  `AttackTrigger` any-state transition `1101015`, `CanTransitionToSelf: 0`,
  no state restriction). So entering Attack from JumpRise/JumpFall already
  works today with zero Animator change — the only Animator-side gap is
  Attack's **exit**: today it has exactly one outgoing transition (fileID
  `1101012`), unconditional, `hasExitTime=1`, straight to Idle. That is the
  one transition this plan touches, replacing it with a small guarded set.
- `JumpAnticipation -> JumpRise` fires on `VerticalVelocity Greater 0.1`;
  `JumpRise -> JumpFall` fires on `VerticalVelocity Less 0.1`. This plan
  reuses those exact same numeric thresholds for Attack's exit so a
  post-attack jump animation always matches what a fresh jump would show at
  the same velocity — no new tunable introduced.
- Existing `AnimatorContractTests.cs`'s 10-parameter contract test needs
  **zero changes** — no parameter is added or removed.
- Existing PlayMode test `FullCommit_WhileAttacking_XPositionUnchangedAcrossRealFrames`
  (`PlayerControllerPlayModeTests.cs`) calls `TryAttack(_controller.IsGrounded)`
  while the rig starts grounded — this is a **ground**-attack under this
  plan's redefinition, so it must keep passing byte-for-byte unmodified.

**Complete write list for this plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Scripts/Player/PlayerController.cs` | Task 1.1, Task 1.2 | Air-attack gating + freeze-semantics carve-out |
| `Assets/Animations/Quirrel.controller` | Task 2.1 | Attack's exit transitions rerouted to Idle/JumpRise/JumpFall |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 3.1 | New EditMode coverage for the C# gating change |
| `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs` | Task 3.2 | New contract test locking in Attack's transition topology |
| `Assets/Scripts/Player/Tests/PlayMode/PlayerControllerPlayModeTests.cs` | Task 3.3 | New PlayMode test proving trajectory continuity + animation resume under real physics |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 3.4 | New checklist rows for air-attack |

**Confirmed out of scope / not touched:** no new Animator parameter; no
save/ScriptableObject schema (none exists yet for combat); no tags/layers/
physics-matrix changes; no Input Actions changes (still legacy `Input`,
still `KeyCode.J`, no new key); `TryJump`'s own gating logic is unchanged
(it already independently blocks a second jump via `!isGrounded`); the
pre-existing edge case of pressing Attack during the 0.08s jump-anticipation
window is not solved by this plan (pre-existing, unaffected behavior either
way); `Look up/down (camera pan)` backlog item is a separate, untouched
feature.

---

## Phase 1 — Gameplay code (`PlayerController.cs`)

#### Task 1.1: [GAMEPLAY] Allow `TryAttack` to succeed while airborne, with a tracked air-attack flag
**Depends on:** none
**Parallel:** yes — with Task 2.1 (different files, no shared state)
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (`TryAttack`,
`OnAttackAnimationComplete`, `Hurt()`, `Die()`)

**Regression risk:** `_isAirAttack` is attack-start metadata only (not read
by `IsFullyCommitted` — see Task 1.2) exposed via `IsAirAttack` for
tests/future systems (e.g. a future air-attack-specific SFX/damage rule).
It is still reset alongside `_isAttacking` in `OnAttackAnimationComplete()`/
`Hurt()`/`Die()` for hygiene — a stale `true` reading between attacks would
be a confusing, if not currently load-bearing, `IsAirAttack` read.

Add a new private `bool _isAirAttack`, set at the moment an attack starts
(`_isAirAttack = !isGrounded` inside `TryAttack`, using the same `isGrounded`
parameter already passed in — no new physics read). Add a public
`IsAirAttack` read-only property (matching the existing convention of
`IsAttacking`/`IsHurtStunned`/`IsJumpInProgress`). Remove `!isGrounded` from
`TryAttack`'s gate — the remaining gate becomes
`IsDead || DefendHeld || _isAttacking || _isHurtStunned`. Clear
`_isAirAttack = false` in **all three** of `_isAttacking`'s existing clear
sites: `OnAttackAnimationComplete()` (normal completion) and both `Hurt()`
and `Die()` (interrupt paths), directly alongside the existing
`_isAttacking = false` line in each.

**Acceptance criteria:**
- [ ] `TryAttack(false)` (airborne) now succeeds when not already attacking/
      defending/hurt-stunned/dead, fires `AttackTrigger`, sets
      `IsAttacking` and `IsAirAttack` both `true`
- [ ] `TryAttack(true)` (grounded) behavior is byte-for-byte unchanged:
      succeeds under the same conditions as before, sets `IsAttacking` true
      and `IsAirAttack` **false**
- [ ] `OnAttackAnimationComplete()` clears both `_isAttacking` and
      `_isAirAttack`
- [ ] `Hurt()` and `Die()` each clear both `_isAttacking` and `_isAirAttack`
      unconditionally at the moment the interrupt is accepted (mirroring the
      existing `_isAttacking`/`_jumpInProgress` reset pattern exactly)
- [ ] A new attack (ground or air) succeeds normally after an
      interrupted air-attack recovers — proves `_isAirAttack` did not leak
      `true` into a later ground-attack
- [ ] No-stack guard is unaffected: a second J press while `_isAttacking` is
      already true (whether the first attack was ground or air) is still
      ignored
- [ ] **Regression check:** existing EditMode attack tests and the
      `Hurt()`-interrupt attack tests in `PlayerControllerTests.cs` still
      pass unmodified

#### Task 1.2: [GAMEPLAY] Redefine `IsFullyCommitted` to freeze on live grounded state, not latched attack-start metadata
**Depends on:** Task 1.1 (needs `_isAttacking`/`_isAirAttack` to exist first)
**Parallel:** no (same file, same property, direct dependency)
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (`IsFullyCommitted`
property only — no other method body changes)

**Regression risk (highest-risk item in this plan):** a naive
`(_isAttacking && !_isAirAttack)` formula — latching the ground/air
distinction once at attack-start — breaks the ground-attack "freeze in
place" invariant for the first time in this codebase's history: a player who
starts an air-attack and lands before the Attack clip's exit time (fully
plausible near the end of a fall) would have `_isAttacking` still true and
`_isAirAttack` still true, so `IsFullyCommitted` would stay false even
though the character is now standing on the ground — un-frozen,
mid-"Attack" animation, walkable. This could not happen before this
feature. **Fix:** key the attack contribution off **live** `IsGrounded`,
not `_isAirAttack`:

`IsFullyCommitted => (_isAttacking && IsGrounded) || DefendHeld || _isHurtStunned;`

This naturally re-engages the freeze the instant `IsGrounded` flips true
mid-air-attack, with no extra latched state to keep in sync. `_isAirAttack`
(Task 1.1) remains as pure metadata — not read here at all. As established
in Section 0, `ApplyHorizontalMovement` runs before `IsGrounded` is
refreshed each `FixedUpdate`, so the freeze re-engages with exactly one
`FixedUpdate` frame of lag after the physical landing — matching this
file's own already-accepted one-frame-lag convention elsewhere, not a new
looseness introduced by this task.

Re-verify all four `IsFullyCommitted` call sites against the new formula:
  - `ApplyHorizontalMovement`: ground-attack (`IsGrounded == true`) still
    freezes; air-attack (`IsGrounded == false`) does not — and freezing
    re-engages automatically, with the one-frame lag noted above, the
    moment `IsGrounded` flips true while `_isAttacking` is still true.
  - `TryJump`: unaffected — already independently blocked by `!isGrounded`
    regardless of this property.
  - `Update()`'s input-gate: air-attack reads live A/D input (confirmed
    design decision — matches normal jump air control; character should
    behave physically identical to an ordinary jump with a different
    sprite on top).
  - `UpdateAnimatorParameters`'s `IsWalking`: already gated by
    `IsGrounded &&`, so air-attack (never grounded while airborne) cannot
    set `IsWalking` true regardless of this property — unaffected.

Update the property's XML doc comment to state explicitly: the attack
freeze is keyed off live `IsGrounded`, not a latched per-attack flag, and
why (the landing-mid-air-attack case above) — so a future edit doesn't
"simplify" this back to the broken latched form.

**Acceptance criteria:**
- [ ] While air-attacking (started airborne, still airborne),
      `ApplyHorizontalMovement` applies real horizontal velocity from input,
      not forced to zero
- [ ] While ground-attacking, `ApplyHorizontalMovement` still forces `.x` to
      zero — regression check, unchanged from today
- [ ] **Landing mid-air-attack re-freezes movement.** Start an air-attack,
      advance frames/physics until the character lands while `_isAttacking`
      is still true (Attack clip not yet finished), and assert horizontal
      velocity is forced back to zero within one `FixedUpdate` frame of
      `IsGrounded` becoming true — this is the dedicated test for this
      task's named risk
- [ ] A jump cannot be re-triggered mid-air-attack (guaranteed by
      `!isGrounded` in `TryJump`, independent of this property — assert
      explicitly so the guarantee is pinned to its real source)
- [ ] `DefendHeld`/hit-stun behavior is completely unchanged (both remain
      unconditional in the new formula, same as before)
- [ ] **Regression check:** full existing EditMode suite passes, in
      particular `FullCommit_While*_HorizontalVelocityStaysFrozenAtZero`
      (Defend/Hurt variants) and the PlayMode test
      `FullCommit_WhileAttacking_XPositionUnchangedAcrossRealFrames` (this
      one specifically exercises a **ground**-attack under this plan's
      split and must keep passing unmodified)

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] TryAttack + _isAirAttack lifecycle ──→ 1.2 [GAMEPLAY] IsFullyCommitted redefinition
(both parallel with Phase 2's 2.1, different file)
```

---

## Phase 2 — Animator Controller (`Quirrel.controller`)

#### Task 2.1: [GAMEPLAY] Reroute Attack's exit transitions to Idle/JumpRise/JumpFall
**Depends on:** none
**Parallel:** yes — with Task 1.1/1.2 (different asset, no code dependency;
the exit conditions reuse `IsGrounded`/`VerticalVelocity`, which are already
written every `FixedUpdate` regardless of this feature)
**Touches:** `Assets/Animations/Quirrel.controller` (Attack state's outgoing
transitions only — no parameters added, no other state touched)

**Regression risk:** This is a shared asset — every place Attack can be
entered from (currently: Idle, Walk, JumpRise, JumpFall, DefendRaise,
DefendHold, Hurt, via the existing unconditional `AnyState -> Attack`) is
affected by a change to how it *exits*, since today's ground-attack exit
(`Attack -> Idle`, unconditional, `hasExitTime=1`) is the one path every one
of those entries currently shares. Replacing one unconditional transition
with three guarded ones must remain **exhaustive** (some transition must
always eventually become true) or Attack can get stuck playing its
last frame forever once `hasExitTime` is reached. Modifies Animator
transitions on a shared asset with no compile-time check — this is exactly
the kind of change that "breaks at runtime with no compile error," which is
why Task 3.2 adds a dedicated contract test.

Replace the single existing `Attack -> Idle` transition with three, in this
exact order (Animator evaluates a state's outgoing transitions in list
order and takes the first whose conditions are all satisfied once
`hasExitTime` is reached — order is load-bearing here):

1. `Attack -> Idle`: `hasExitTime=true, exitTime=1` (unchanged timing),
   condition `IsGrounded == true` (Bool). Reproduces today's ground-attack
   behavior exactly, now explicit rather than implicit.
2. `Attack -> JumpRise`: `hasExitTime=true, exitTime=1`, conditions (AND)
   `IsGrounded == false` AND `VerticalVelocity Greater 0.1` — same threshold
   `JumpAnticipation -> JumpRise` already uses.
3. `Attack -> JumpFall`: `hasExitTime=true, exitTime=1`, condition
   `IsGrounded == false` only (deliberately no velocity condition — this is
   the exhaustive fallback: if transition 2's stricter velocity condition
   fails, this one catches every remaining airborne case, avoiding any dead
   zone around the exact `VerticalVelocity == 0.1` boundary, the same
   boundary-handling approach already implicitly accepted by
   `JumpRise -> JumpFall`'s own `Less 0.1` condition).

No new Animator parameter, no change to any other state or transition, no
change to `AnyState -> Attack` (already unconditional).

**Acceptance criteria:**
- [ ] Attack state has exactly three outgoing transitions, in the order and
      with the exact conditions listed above
- [ ] No other state's transitions are modified (diff confirms this)
- [ ] No new Animator parameter is added (parameter count remains 10)
- [ ] Manual check in the Animator window (or via Unity MCP if connected):
      forcing `IsGrounded = true` while in Attack routes to Idle at clip
      end; forcing `IsGrounded = false, VerticalVelocity = 5` routes to
      JumpRise; forcing `IsGrounded = false, VerticalVelocity = -5` routes
      to JumpFall
- [ ] **Regression check:** `AnimatorContractTests.cs`'s existing
      10-parameter test and `JumpAnticipation`-state test both still pass
      unmodified (expected zero-diff on that file until Task 3.2 adds to it)

---

**Phase 2 dependency graph:**
```
2.1 [GAMEPLAY] (independent, parallel with 1.1/1.2) ──→ 3.2 [QA] contract test, 3.3 [QA] PlayMode test
```

---

## Phase 3 — QA regression and new coverage

#### Task 3.1: [QA] EditMode tests for the air-attack gating change
**Depends on:** Task 1.1, Task 1.2
**Parallel:** yes — with Task 3.2 (different test file)
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`
(additive only)

**Regression risk:** Additive only — verify no existing `[Test]` signature
or assertion is altered.

**Acceptance criteria:**
- [ ] New test: `TryAttack(false)` succeeds, sets `IsAttacking` and
      `IsAirAttack` both true, fires no exception with no Animator attached
- [ ] New test: `TryAttack(true)` still sets `IsAirAttack` false
      (ground-attack regression pin)
- [ ] New test: while air-attacking, `ApplyHorizontalMovement(1f)` sets a
      nonzero `.x` velocity matching `_walkSpeed` and leaves `.y` untouched,
      proving no freeze
- [ ] New test: while ground-attacking, `ApplyHorizontalMovement(1f)` still
      forces `.x` to zero (regression pin)
- [ ] New test implementing Task 1.2's landing-mid-air-attack case: start an
      air-attack, drive the rig (or force `IsGrounded` via the existing
      reflection convention, if a physics-free EditMode repro is cleaner)
      until grounded while `_isAttacking` is still true, assert
      `ApplyHorizontalMovement` freezes `.x` to zero on the very next call
      after `IsGrounded` becomes true
- [ ] New test: `Hurt()` called mid-air-attack resets `IsAirAttack` to
      `false` alongside `IsAttacking` — then a new ground-attack started
      after recovery correctly re-freezes horizontal movement (guards
      Task 1.1's named risk of `_isAirAttack` leaking `true` into a later
      ground-attack)
- [ ] New test: same as above but via `Die()` instead of `Hurt()`
- [ ] Full existing EditMode suite passes with the same or greater pass
      count than before this plan started

#### Task 3.2: [QA] Animator contract test for Attack's new exit transitions
**Depends on:** Task 2.1
**Parallel:** yes — with Task 3.1
**Touches:** `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs`
(additive only)

**Regression risk:** Additive only. This test exists specifically to close
the gap Task 2.1 opens: an Animator transition edit compiles fine and fails
silently at runtime if wrong (no exception, the character just never
animates correctly) — same reasoning this file already documents for its
existing parameter/state-name checks.

**Acceptance criteria:**
- [ ] New test loads `Quirrel.controller`, finds the `Attack` state, and
      asserts it has exactly 3 outgoing transitions
- [ ] Asserts the destination state names are `Idle`, `JumpRise`,
      `JumpFall` in that order
- [ ] Asserts each transition's condition list matches Task 2.1's spec
      (`IsGrounded`/`VerticalVelocity` parameter names and comparison
      thresholds)
- [ ] Existing 10-parameter test and `JumpAnticipation`-state test in this
      same file still pass unmodified

#### Task 3.3: [QA] PlayMode test — trajectory continuity under real physics (deterministic, single-run)
**Depends on:** Task 1.1, Task 1.2, Task 2.1
**Parallel:** no (depends on all three preceding tasks)
**Touches:** `Assets/Scripts/Player/Tests/PlayMode/PlayerControllerPlayModeTests.cs`
(additive only; this is the one test in this plan that needs the real
`Quirrel.controller` attached, following the same rig-extension precedent
plan 004's Task 3.1b already established for this file)

**Regression risk:** Additive only. Asserts against deterministic known
constants (matching this file's existing convention — every current test
asserts against known constants like `_walkSpeed`/`_jumpVerticalVelocity`/
fixed thresholds; none diffs two separately-executed real-time runs, which
would be flaky by construction).

**Acceptance criteria:**
- [ ] Test attaches the real `Quirrel.controller` to the existing minimal
      physics rig (matching the established rig-building convention in this
      file)
- [ ] Starts a jump (`TryJump(true)`), advances frames past the anticipation
      window so the vertical impulse applies, then calls
      `TryAttack(_controller.IsGrounded)` while airborne and confirms it
      succeeds (`IsAttacking && IsAirAttack` both true)
- [ ] While air-attacking and holding a horizontal input direction via this
      file's existing `SetHorizontalInput` reflection helper, asserts
      `Rigidbody2D.velocity.x` equals `horizontalInput * _walkSpeed` (a
      known constant, matching this file's existing convention) — not
      forced to zero
- [ ] Over the same frames, asserts `Rigidbody2D.velocity.y` is never
      written by anything other than gravity/the existing
      `ClampFallVelocity` clamp during the air-attack (e.g. asserting `.y`
      only ever decreases monotonically frame-to-frame while airborne and
      pre-apex, matching an un-attacked jump's known physics — no separate
      control run required, since the expected curve is derived from
      constants already known to the test)
- [ ] Advances frames until the Attack clip's exit time is reached and
      asserts the Animator's current state (layer 0) is `JumpRise` or
      `JumpFall` (matching the rig's actual `VerticalVelocity` sign at that
      moment), never `Idle` or `Walk`
- [ ] Continues advancing frames until the character lands and asserts the
      Animator correctly reaches `Idle` via the existing
      `JumpFall -> Idle [IsGrounded If]` transition (untouched by this plan)
      — proving the landing path still works after an air-attack
- [ ] Test is re-run a few times locally to confirm it is not flaky
- [ ] **Regression check:** full existing PlayMode suite passes, in
      particular `FullCommit_WhileAttacking_XPositionUnchangedAcrossRealFrames`
      (ground-attack, must still pass unmodified)

#### Task 3.4: [QA] Update the manual playtest protocol
**Depends on:** Task 3.1, Task 3.2, Task 3.3
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md`

**Regression risk:** None — documentation only.

**Acceptance criteria:**
- [ ] New checklist row: jump, attack mid-air, confirm the character keeps
      moving/falling exactly as it would without the attack (no visible
      pause/freeze in the jump arc)
- [ ] New checklist row: attack immediately at the jump's apex, confirm the
      animation resumes into a falling pose, not idle/walk
- [ ] New checklist row: ground-attack still visibly freezes the character
      in place (regression spot-check against pre-existing behavior)
- [ ] No existing row altered except where this plan's change affects
      expected behavior (none anticipated)

---

**Phase 3 dependency graph:**
- Task 3.1 depends on: Task 1.1, Task 1.2
- Task 3.2 depends on: Task 2.1
- Task 3.3 depends on: Task 1.1, Task 1.2, Task 2.1
- Task 3.4 depends on: Task 3.1, Task 3.2, Task 3.3

---

**Explicitly out of scope for this plan:** any new Animator parameter; any
change to `TryJump`'s own gating (already correctly independent of
`IsFullyCommitted` via `!isGrounded`); resolving the pre-existing
attack-during-jump-anticipation corner case (unaffected either way, per
Section 0); air-control tuning during a normal (non-attack) jump (unchanged,
pre-existing); any combat/health/hitbox system (still out of scope per plan
002); the `Look up/down (camera pan)` backlog item (separate feature).
