# Implementation Plan — Walk-Animation Freeze and Jump-Stall Fix

**Status:** ✅ APPROVED — implementation-plan-reviewer, round 4
**Author:** implementation-plan-architect
**Date:** 2026-08-04
**Feature:** Fix two bugs found during post-003 playtesting: (1) the walking animation
freezes on Idle after some time while the character keeps physically moving, fixed
by attacking; (2) pressing Space (Jump) stops doing anything at all after some
time, though it works fine at the start of a session.

## 0. Summary and verified blast radius

Two bugs surfaced during post-003 playtesting, both pre-existing (unrelated to the
KeyCode rebind):

- **Bug 2 (Jump silently stops working)** — root cause confirmed from committed
  asset data: `TryJump()`'s delayed-impulse cancellation check
  (`IsJumpAnticipationStillActive()`, inspecting `GetCurrentAnimatorStateInfo(0)`)
  can read false not only on a genuine Hurt/Die interrupt (its intended purpose,
  per `Docs/Plans/002...md` §1.9) but also whenever a Space press coincides with
  an in-flight Idle↔Walk transition — because that transition's
  `InterruptionSource: None` + `0.1s` duration can outlast the code's `0.08s`
  jump-anticipation timer, so the queued `JumpTrigger` hasn't been consumed yet
  when the timer checks. The impulse is cancelled, the character never leaves the
  ground, and `_jumpInProgress`'s only clear-path (the landing edge) never fires
  — it is stuck `true` for the rest of the session.
- **Bug 1 (Walk animation freezes on Idle while translation continues)** —
  code-side `IsWalking` is confirmed (by elimination) to be set correctly every
  frame at the moment of the freeze; this is a Mecanim-layer problem, not a C#
  logic bug. Root mechanism not yet confirmed — requires the Phase 0 diagnostic.

**Why Bug 2 shipped past a fully-green test suite:** both `PlayerControllerTests.cs`
(EditMode) and `PlayerControllerPlayModeTests.cs` (PlayMode) deliberately never
attach a real `Animator`/`Quirrel.controller`. Confirmed directly:
`PlayerControllerTests.cs`'s `CreatePlayer()` (lines 23-29) adds only
`Rigidbody2D`, `SpriteRenderer`, and `PlayerController` — no `Animator`. As a
result, every existing test exercising `TryJump`/`ApplyJumpImpulseIfValid` hits
`IsJumpAnticipationStillActive()`'s `_animator == null → return true` fallback
unconditionally. Confirmed further: the one existing interrupt test,
`Jump_InterruptedByHurtMidAnticipation_ThenRecovers_NewJumpSucceeds` (lines
380-399), never calls `AdvanceJumpTimer` after `Hurt()` and never asserts on
`velocity.y` — it only checks that `IsJumpInProgress` clears and a second jump
can start. **No existing test actually proves the impulse itself gets cancelled
by `Hurt()`.** This plan adds that missing coverage (Task 3.1a).

**Complete write list for this whole plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Scripts/Player/PlayerDiagnosticLogger.cs` (new, temporary) | Task 0.1 | Read-only instrumentation; removed by Task 0.2 |
| `Assets/Scenes/SampleScene.unity` | Task 0.1 (add), Task 0.2 (remove) | Adds/removes the logger component on the existing Player GameObject only — no existing component's fields touched |
| `Assets/Scripts/Player/PlayerController.cs` | Task 1.1, Task 2.2 (pending Phase 0/2.1) | Bug 2 fix (confirmed); Bug 1 fix (provisional, see Phase 2) |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 3.1a | New EditMode regression tests: `Hurt()`-cancellation proof, no-animator-attached impulse test, and the Branch B/C repro (EditMode-only, per review — reproducible at the flag/reflection level, same pattern as the file's existing `ForceSetDefendHeld`-style tests) |
| `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs` | Task 3.1a (verification only — see Task 1.1's explicit const-retention decision; expected zero-diff) | Confirm the `JumpAnticipation` state-name contract assertion still passes unmodified |
| `Assets/Scripts/Player/Tests/PlayMode/PlayerControllerPlayModeTests.cs` | Task 3.1b | New PlayMode test attaching the real `Quirrel.controller` and exercising the actual crossfade race — the only PlayMode addition in this plan, no overlap with Task 3.1a |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 3.2 | New checklist rows: rapid direction-tap-then-jump, extended-session walk/jump checks |

**Confirmed out of scope / not touched:** Animator Controller topology
(`Assets/Animations/Quirrel.controller`) — Phase 1's fix is deliberately code-side
only; no ScriptableObject/save schema exists to migrate; no tags/layers/physics-layer
changes; `Docs/Plans/003_control-rebind-adjk.md`'s work is untouched.

---

## Phase 0 — Diagnostic (human-playtest-required, gates Phase 2 only)

#### Task 0.1: [GAMEPLAY] Add a temporary read-only diagnostic logger
**Depends on:** none
**Parallel:** yes — with Task 1.1
**Touches:** `Assets/Scripts/Player/PlayerDiagnosticLogger.cs` (new),
`Assets/Scenes/SampleScene.unity` (adds one new component to the existing Player
GameObject — additive only)

**Regression risk:** None in the strict "existing content changed" sense — read-only
instrumentation, zero new Animator parameters/states/transitions, zero edits to any
existing component's serialized fields. The only real risk is a naive per-frame
logger perturbing frame timing enough to mask or distort the very race conditions
it's meant to observe. Mitigated by the throttling + allocation criterion below.

**Acceptance criteria:**
- [ ] Logger compiles, attaches as a new component on Player, does not alter any
      existing component's Inspector values
- [ ] Confirmed via a short manual smoke check that log lines appear during
      ordinary walk/jump/attack and correctly name each known state
      (Idle/Walk/JumpAnticipation/JumpRise/JumpFall/Attack/DefendRaise/DefendHold/Hurt/Die)
- [ ] Logging is throttled/change-triggered (not unconditional per-frame string
      building) and avoids per-frame heap allocation where reasonably avoidable,
      confirmed by spot-checking the Profiler or frame-time counter shows no
      perceptible added GC/frame-time cost during a short test session
- [ ] Regression check: `SampleScene.unity` still opens with zero console
      errors/warnings beyond the new logger's own intentional log lines

#### Task 0.2: [QA] Human-playtest capture session and bug confirmation
**Depends on:** Task 0.1
**Parallel:** no
**Touches:** `Assets/Scenes/SampleScene.unity` (removes the Task 0.1 logger
component at the end of this task, restoring the scene to its pre-0.1 committed
state)

**Regression risk:** None — this task's only scene write is removing what 0.1
added, verified as a clean revert.

**This task cannot be performed by an implementation agent or via Unity MCP
`execute_code`.** Both bugs depend on real, sustained keyboard input that legacy
`Input.GetKey`/`GetKeyDown` cannot be driven through programmatically (same
constraint plan 003 documented for its own live-verification step) —
synthetically poking Animator parameters via `execute_code` would just be
overwritten by `PlayerController.Update()`'s real per-frame input read the same
frame, so it can't produce a faithful repro, especially for Bug 1, which appears
to depend on incidental timing from genuine human play patterns rather than a
scriptable single input sequence. This requires a human at the keyboard for
several minutes of real, varied play: walking back and forth repeatedly, jumping
frequently including right at the start/stop of a walk, until each bug visibly
reproduces (or a reasonable extended session — e.g. 10-15 minutes of active play
— elapses without reproducing one or both, which is itself a valid, reportable
outcome).

**Acceptance criteria:**
- [ ] A real human play session (not agent- or MCP-driven) of sufficient length
      is conducted, actively trying to reproduce both bugs (frequent walk
      start/stop, frequent jumping, sustained play)
- [ ] For Bug 2 (if reproduced): log captured showing the Animator
      state/transition info at the moment of the Space press whose jump silently
      fails, confirming (or refuting) the Idle↔Walk-transition-blocks-queued-
      JumpTrigger mechanism
- [ ] For Bug 1 (if reproduced): log captured showing the exact Animator
      current-state name and the Animator's own `IsGrounded`/`IsWalking`
      parameter values at the moment the walk animation visibly freezes,
      resolving which of Phase 2's candidate mechanisms actually applies
- [ ] Findings (logs + your read of them) reported back before Task 2.1 begins
- [ ] Logger component removed from the Player GameObject in `SampleScene.unity`;
      scene diff shows only that removal, restoring it to Task 0.1's pre-state

---

**Phase 0 dependency graph:**
```
0.1 [GAMEPLAY] add logger ──→ 0.2 [QA] human playtest + capture + logger removal ──→ (feeds Task 2.1 only)
0.1 is parallel with 1.1; 0.2 does not gate Phase 1 or Phase 3's Bug-2-specific work
```

---

## Phase 1 — Bug 2 fix (Jump stall), proceeds independently of Phase 0

#### Task 1.1: [GAMEPLAY] Decouple jump-impulse cancellation from Animator state inspection
**Depends on:** none
**Parallel:** yes — with Task 0.1
**Touches:** `Assets/Scripts/Player/PlayerController.cs` only

**Regression risk:** Modifies the gating logic behind `ApplyJumpImpulseIfValid()`.
The existing test `Jump_InterruptedByHurtMidAnticipation_ThenRecovers_NewJumpSucceeds`
(lines 380-399) does **not** actually prove `Hurt()` cancels the impulse — it
never calls `AdvanceJumpTimer` after `Hurt()` and never asserts `velocity.y`.
Since `CreatePlayer()` (lines 23-29) never attaches an Animator,
`IsJumpAnticipationStillActive()`'s fallback returns true in that fixture
regardless of `Hurt()` — the only reason this hasn't caused a visible test
failure is that the test never checks `velocity.y`. **This plan adds that
guarantee for the first time**, via a new deterministic, animator-independent
mechanism. **Explicit decision on `JumpAnticipationStateName`/
`IsJumpAnticipationStillActive()`:** both are kept, unmodified, and no longer
called from `ApplyJumpImpulseIfValid()` — their sole remaining purpose is
serving as the reflection target `AnimatorContractTests.cs` (~lines 106-133)
asserts against. A doc comment on both must state this explicitly.

Add a dedicated code-side flag (`_jumpImpulseCancelled`):
- Reset to `false` at the moment a new jump starts (inside `TryJump()`,
  alongside `_jumpInProgress = true`).
- Set to `true` only by `Hurt()` and `Die()`, at the same point they already
  reset `_jumpInProgress`/`_isAttacking`.
- `ApplyJumpImpulseIfValid()` checks `IsDead || _jumpImpulseCancelled` instead
  of calling `IsJumpAnticipationStillActive()`.

**Acceptance criteria:**
- [ ] A jump started while the Animator has not yet reached `JumpAnticipation`
      (e.g., no Animator/Controller attached) still applies its vertical impulse
- [ ] **New test:** `TryJump(true)` → `Hurt()` → `AdvanceJumpTimer(0.08f)` →
      assert `velocity.y` shows no jump impulse applied
- [ ] `Die()` before the 0.08s timer elapses still cancels the impulse (existing
      EditMode test from Task 3.3 continues to pass unmodified — this one
      genuinely does assert the outcome today)
- [ ] A second, later jump attempt after a cancelled one correctly applies its
      impulse (`_jumpImpulseCancelled` does not leak `true` into a subsequent
      jump) — new test
- [ ] `JumpAnticipationStateName` and `IsJumpAnticipationStillActive()` remain
      present, compiling, and documented as intentionally retained-but-unused-
      for-gating
- [ ] `AnimatorContractTests.cs` diff is confirmed **empty** and its
      `JumpAnticipation` state-name assertion still passes
- [ ] **Regression check:** full existing EditMode suite
      (`PlayerControllerTests.cs`, `JumpPhysicsMathTests`, `AnimatorContractTests.cs`)
      and PlayMode suite (`PlayerControllerPlayModeTests.cs`) pass with no new
      failures

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] (independent) ──→ 3.1a [QA] EditMode regression tests
                              └──→ 3.1b [QA] real-Animator race test
```

---

## Phase 2 — Bug 1 fix (Walk animation freeze), provisional, blocked on Phase 0

#### Task 2.1: [GAMEPLAY] Diagnose which fix branch applies from Task 0.2's log data
**Depends on:** Task 0.2 (hard gate)
**Parallel:** no
**Touches:** none (produces a decision/finding only — no code or asset changes)

**Regression risk:** None — this task writes no code.

**Acceptance criteria:**
- [ ] Task 0.2's captured Animator current-state name at Bug-1 freeze time is
      read against the three branches below and exactly one is selected, with
      the log excerpt quoted as evidence
- [ ] If Branch A applies, this is stated explicitly and Task 2.2 is **not**
      created under this plan
- [ ] If Branch B or C applies, the specific desynced parameter/state pair is
      named precisely

**Branch definitions:**
- **Branch A — log shows current state is genuinely `Idle` or `Walk`:** not a
  state-machine desync. **Exits this plan back to implementation-plan-architect
  for a new Phase 2 draft — do not proceed to Task 2.2 under this plan.**
- **Branch B — log shows current state is `JumpFall`, `JumpRise`, or
  `JumpAnticipation`:** candidate fix ensures a stray/late `JumpTrigger` cannot
  arrive after its originating `TryJump()` call is no longer meaningful (e.g.
  `Animator.ResetTrigger`).
- **Branch C — log shows current state is `DefendHold` or `Hurt`:** candidate
  fix targets whichever `DefendHeld`/hit-stun parameter is desynced.

#### Task 2.2: [GAMEPLAY] Implement the Branch B or C fix
**Depends on:** Task 2.1 resolving to Branch B or C (does not exist as a task if
Task 2.1 resolves to Branch A)
**Parallel:** no
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (both branches);
`Assets/Animations/Quirrel.controller` **only if** the selected branch's fix
requires an Animator-side change (confirmed against Task 2.1's finding, not
assumed up front)

**Regression risk:** Branch B interacts with the same `JumpTrigger`/
`_jumpInProgress` machinery Task 1.1 just changed — must not reintroduce Bug 2.
Branch C interacts with `DefendHeld`/hit-stun's existing full-commit gating
(§1.8) and guard-flag reset tests (Task 3.4b) — must not interfere with
`IsFullyCommitted`. If an Animator Controller edit is required: shared-asset
touch requiring verification that no other state/transition is affected beyond
the one being fixed.

**Acceptance criteria (branch-specific, finalized once Task 2.1 selects the branch):**
- [ ] Selected branch's fix applied; character resumes walking animation without
      requiring an Attack press to "unstick" it, verified across at least 3
      repeated attempts to reproduce the original trigger conditions
- [ ] Movement (translation) behavior is unchanged
- [ ] If Branch B: regression check confirms Task 1.1's `_jumpImpulseCancelled`
      change and this fix do not reintroduce each other's bug
- [ ] If Branch C: regression check confirms no interference with Defend/Hurt's
      existing full-commit gating (§1.8) or Task 3.4b's guard-flag reset tests
- [ ] If an Animator Controller edit was needed: diff confirms only the specific
      transition/condition named in Task 2.1's finding changed
- [ ] **Regression check (either branch):** full existing EditMode + PlayMode
      suites pass with no new failures

---

**Phase 2 dependency graph:**
```
0.2 [QA] diagnostic capture ──→ 2.1 [GAMEPLAY] branch diagnosis ──┬──→ Branch A: EXITS this plan,
                                                                    │      returns to architect for a
                                                                    │      fresh Phase 2 draft (no 2.2)
                                                                    │
                                                                    └──→ Branch B or C: 2.2 [GAMEPLAY]
                                                                          fix ──→ 3.1a [QA] Bug-1
                                                                          regression test (EditMode)
```

---

## Phase 3 — QA regression and documentation

#### Task 3.1a: [QA] EditMode regression test additions
**Depends on:** Task 1.1 (Bug 2 portion); Task 2.2 (Bug-1 portion, does not exist
under this plan if Branch A applies)
**Parallel:** yes — with Task 3.1b (disjoint files: this task touches only
`PlayerControllerTests.cs`/`AnimatorContractTests.cs`, never
`PlayerControllerPlayModeTests.cs`)
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`;
`Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs` (read/verify
only — expected zero-diff)

**Regression risk:** Adds tests only. Verify no existing `[Test]` signature or
assertion is altered, only added to.

**Acceptance criteria:**
- [ ] New EditMode test: `TryJump(true)` → `Hurt()` → `AdvanceJumpTimer(0.08f)`
      → assert no vertical impulse applied
- [ ] New EditMode test: jump started with no Animator attached (matching this
      fixture's existing convention) still applies the impulse deterministically
      via `_jumpImpulseCancelled == false`
- [ ] **New EditMode test reproducing whichever Branch (B or C) Task 2.2
      resolved to, committed to EditMode only** (both branches are reproducible
      at the code/flag level using this file's existing reflection pattern —
      e.g. the same style as `ForceSetDefendHeld` — with no live Animator
      required; if Branch B, drive the reproduction via the equivalent
      private-field/flag state directly rather than a real Animator
      transition). Skipped, with a note why, if Branch A applied and no
      Task 2.2 exists.
- [ ] Full existing EditMode suite passes with the same or greater pass count
      than before this plan started

#### Task 3.1b: [QA] Real-Animator crossfade-race regression test (PlayMode)
**Depends on:** Task 1.1
**Parallel:** yes — with Task 3.1a
**Touches:** `Assets/Scripts/Player/Tests/PlayMode/PlayerControllerPlayModeTests.cs`

**Regression risk:** None to existing tests (additive), but this task carries
the plan's highest inherent flake/iteration risk — it's the first (and, per
Task 3.1a's revision above, the *only*) test in the suite to attach a real
Animator + `Quirrel.controller`, and depends on genuinely landing a Space press
mid-crossfade.

**Timing mechanism:** the Player GameObject's Animator uses `m_UpdateMode: 0`
(Normal — evaluated once per rendered frame tied to `Time.deltaTime`), and
`IsWalking`/Space are both read/set in `PlayerController.Update()`, not
`FixedUpdate()`. This file's own class doc comment (lines 27-44) already
establishes that `yield return null` (not `yield return new
WaitForFixedUpdate()`) is required to land input/state changes correctly
relative to `Update()`. This test must follow that same established convention.

**Acceptance criteria:**
- [ ] Test attaches the real `Assets/Animations/Quirrel.controller` to a
      PlayMode rig (the only test in the suite to do so)
- [ ] Drives held-movement input via this file's existing reflection-based
      `_horizontalInput` override technique, one `yield return null` per frame
      (matching this file's established convention — not `WaitForFixedUpdate`),
      to produce a genuine, repeated Idle↔Walk oscillation in the real Animator
- [ ] Detects "currently mid-crossfade" by polling `Animator.IsInTransition(0)`
      each `yield return null` iteration — not by assuming a fixed frame count
      reliably lands inside the 0.1s window
- [ ] At the moment `Animator.IsInTransition(0)` confirms a live Idle↔Walk
      transition, calls `TryJump(true)` directly (edge-triggered action, per
      this file's existing input-simulation convention) and continues advancing
      `yield return null` frames until the 0.08s anticipation window has elapsed
- [ ] Asserts the vertical impulse was applied (`velocity.y` reflects
      `_jumpVerticalVelocity`, not zero) despite the transition having been in
      flight — this is the one test in the whole suite that actually exercises
      the real race Task 1.1 fixes
- [ ] Test is retried/re-run at least a few times locally to confirm it isn't
      itself flaky before being considered done
- [ ] **Regression check:** full existing PlayMode suite passes with no new
      failures

#### Task 3.2: [QA] Update the manual playtest protocol
**Depends on:** Task 3.1a, Task 3.1b
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md`

**Regression risk:** None — documentation only.

**Acceptance criteria:**
- [ ] New checklist row: rapid direction-key tap immediately followed by a
      Space press, repeated several times, confirming jump still fires every
      time
- [ ] New checklist row: extended-session check (walk/jump repeatedly for
      several minutes) confirming neither bug recurs
- [ ] No existing row altered except where this plan's fixes change expected
      behavior (none anticipated)

---

**Phase 3 dependency graph:**
```
1.1 ──┬──→ 3.1a [QA] EditMode regression tests ──┐
       │                                           ├──→ 3.2 [QA] playtest protocol update
       └──→ 3.1b [QA] real-Animator race test ─────┘
2.2 ──→ 3.1a's Bug-1 portion (skipped/noted if Branch A applied)
```

---

**Explicitly out of scope for this plan:** any change to
`Assets/Animations/Quirrel.controller`'s existing transition topology unless
Task 2.2 (Branch B/C) specifically requires it; any combat/health system (still
out of scope per plan 002); a full Input System migration (still out of scope
per plan 002 §1.12/003); a fresh Phase 2 fix design if Branch A applies
(explicitly handed back to plan authoring, not absorbed into this plan).
