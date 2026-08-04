# Implementation Plan — Look up/down (Camera Pan)

**Status:** ✅ APPROVED — implementation-plan-reviewer, round 4
**Author:** implementation-plan-architect
**Date:** 2026-08-04
**Feature:** `Docs/Backlog.md` → "Look up/down (camera pan)". Hold `W`: camera
slides up by half a screen height; release: slides back to normal. Hold `S`:
camera slides down by half a screen height; release: slides back to normal.
Confirmed design decision: blocked while the player is dead
(`PlayerController.IsDead`) or in any of the existing full-commit states
(`PlayerController.IsFullyCommitted` — Attack/Defend/Hurt), mirroring how
those two conditions already gate other input in that file.

---

## 0. Summary and verified blast radius

**Confirmed from the actual source:**

- `Assets/Scripts/Camera/CameraFollow.cs` is the entire camera module. Its
  class doc comment (lines 3–13) states: *"Deliberately decoupled from
  PlayerController: this component only knows about a generic Transform...
  never about the player specifically."* This plan is the first thing to put
  a real, if narrow, coupling in that file — that sentence must be
  corrected, not left to silently lie about the architecture.
- `Tick(float deltaTime)` (lines 46–58) is public specifically so EditMode
  tests can drive it without Play Mode — reused as-is, not reinvented.
- `EnsureInitialized()` (lines 60–71) captures `_fixedY`/`_fixedZ` once, on
  first call, and is idempotent (guarded by `_initialized`). Y has been
  eternally frozen since this file's creation — this is the first feature to
  ever move it.
- `Assets/Scripts/Camera/Quirrel.Camera.asmdef` — `"references": []`.
  **CameraFollow's assembly currently cannot see `PlayerController` at
  all.** Any direct-reference coupling requires an asmdef edit, not just a
  C# change.
- `Assets/Scripts/Player/Quirrel.Player.asmdef` — `"references": []`.
  Confirmed no reference back to `Quirrel.Camera`, so adding a
  `Quirrel.Camera → Quirrel.Player` edge is one-directional and does not
  create a circular assembly reference (compile-time-checked, not silent).
- `Assets/Scripts/Camera/Tests/EditMode/Quirrel.Camera.EditModeTests.asmdef`
  — `"references": ["Quirrel.Camera", "UnityEngine.TestRunner",
  "UnityEditor.TestRunner"]`. Asmdef references are **not transitive** —
  this test assembly needs its own added reference to `Quirrel.Player`.
- `Assets/Scripts/Camera/Tests/EditMode/CameraFollowTests.cs`, test
  `YAndZPosition_StayFixed_WhileFollowingOnX` (lines 52–72): sets the
  target's Y to `12f` (deliberately different from camera start Y `3f`) and
  asserts camera Y stays exactly `3f` forever. This conflates two claims:
  (a) the target's own Y is ignored by the follow logic, and (b) the
  camera's Y never moves for any reason. **(a) remains true after this
  feature — pan is driven by W/S, never by the target's Y.** Only **(b)** is
  what this feature breaks on purpose. The rename narrows the test's stated
  scope to "while no pan input is held," not touching its assertions — the
  exact numbers still hold (see Task 2.1a).
- `PlayerController.IsFullyCommitted` (verbatim): `(_isAttacking &&
  IsGrounded) || DefendHeld || _isHurtStunned` — already a public property
  (`Assets/Scripts/Player/PlayerController.cs` line 118).
- **`PlayerController.Die()` clears `IsFullyCommitted` to `false`,
  permanently — confirmed by direct read (lines 602-625).** `Die()` sets
  `_isAttacking = false`, `DefendHeld = false`, and `_isHurtStunned = false`
  unconditionally (with its own comment: *"Die outranks Hurt - no point
  leaving a stun window 'active' under a permanent death lock"*), then sets
  `IsDead = true`. After `Die()`, `IsFullyCommitted`'s formula evaluates
  `false` and stays `false` for the rest of the scene's lifetime. **This
  means a gate that reads only `IsFullyCommitted` would let camera pan work
  normally over a corpse — a real gap, not a hypothetical one.** Every other
  gate in this file treats `IsDead` as a separate, additional condition
  checked alongside `IsFullyCommitted`, never assuming `IsFullyCommitted`
  implies death-blocking: `Update()`'s horizontal-input gate (line 298: `if
  (!IsDead && !IsFullyCommitted)`), `ApplyHorizontalMovement` (line 383:
  `bool locked = IsDead || IsFullyCommitted;`), `TryJump` (line 408: `if
  (!isGrounded || _jumpInProgress || IsDead || IsFullyCommitted)`),
  `TryAttack` (line 472: `if (IsDead || DefendHeld || _isAttacking ||
  _isHurtStunned)`), `UpdateAnimatorParameters`'s `IsWalking` computation
  (line 735: `&& !IsDead ... && !IsFullyCommitted`). This plan's pan gate
  follows the same convention.
- `IsDead` is `public bool IsDead { get; set; }` (line 221) — a real public
  setter, not a read-only computed property, so tests can force it directly
  with no reflection needed (unlike `DefendHeld`, which has a private setter
  and does require reflection).
- `PlayerController`'s own A/D accumulation (`Update()`, lines 300–308):
  `_horizontalInput -= 1f` for A, `+= 1f` for D — pressing both nets to `0`.
  This exact accumulation shape is reusable verbatim for W/S (`+1` for W,
  `-1` for S), giving "both held" a free, already-proven cancel-to-neutral
  behavior with no new logic invented.
- Scene (`Assets/Scenes/SampleScene.unity`, the project's only scene): the
  `Main Camera` GameObject (`fileID 519420028`) carries both the `Camera`
  component (`fileID 519420031`, `orthographic: 1`, `orthographic size: 5`)
  **and** the `CameraFollow` MonoBehaviour (`fileID 519420033`, currently
  serializing only `target: {fileID: 563516963}`) on the same object.
  `PlayerController` lives at `fileID 563516958` on the `Player` GameObject
  (`fileID 563516957`). No prefab instances anywhere in this chain — a scene
  edit here has no fan-out risk.
- Fresh grep of `Assets/Scripts` for `KeyCode.W`/`KeyCode.S`/any
  `Input.Get*`: only hits are the existing `A`/`D`/`J`/`K`/`Space` reads in
  `PlayerController.cs`. W/S are free.
- **Hard testing constraint, same root cause `Docs/Plans/003` already
  documented for A/D/J/K/Space:** this project has no Input System package;
  legacy `UnityEngine.Input.GetKey` cannot be driven programmatically in
  EditMode or PlayMode tests. Whatever method ends up calling
  `Input.GetKey(KeyCode.W/S)` directly is **structurally untestable for the
  literal key-read** — only for what happens downstream of it. This
  constrains Task 1.2's design: the actual pan-gating logic must live in a
  place a test *can* call directly (`Tick()`), not solely inside the
  untestable key-reading method.
- **Sequence-break / early-area-access assessment (explicit):** assessed and
  dismissed. Camera pan moves only the rendered viewport's Y position; it
  never writes to the player's `Transform`, never touches any `Collider2D`,
  `Rigidbody2D`, or `PlayerController` state that affects
  movement/traversal, and grants no new reachable area — the player's
  actual position and physical capabilities are completely unaffected
  regardless of how far the camera is panned. It therefore carries none of
  the ability-gating regression risk a genuine movement ability would. The
  only interaction this plan has with `PlayerController` at all is a
  read-only gate on the *pan itself* (`IsDead || IsFullyCommitted`) — not a
  change to what the player can do or reach.

**Complete write list for this plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Scripts/Camera/CameraFollow.cs` | Task 1.1, Task 1.2 | Optional `PlayerController` coupling; W/S read + gated additive Y-pan (gate reads `IsDead \|\| IsFullyCommitted`); `_fixedY` → `_baseY` rename; class doc-comment correction |
| `Assets/Scripts/Camera/Quirrel.Camera.asmdef` | Task 1.1 | Add `Quirrel.Player` reference |
| `Assets/Scripts/Camera/Tests/EditMode/Quirrel.Camera.EditModeTests.asmdef` | Task 2.1a | Add `Quirrel.Player` reference |
| `Assets/Scripts/Camera/Tests/EditMode/CameraFollowTests.cs` | Task 2.1a, Task 2.1b | Rename/narrow existing test; add pan/gating/fallback coverage, split across two tasks |
| `Assets/Scenes/SampleScene.unity` | Task 1.3 | Wire the new optional `_playerController` field on the CameraFollow component to the real `PlayerController` |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 2.2 | New section for live pan verification (mirrors `5a`'s precedent), including a post-Die() row |
| `Docs/Backlog.md` | Task 2.2 | Remove the "Look up/down (camera pan)" entry now that it has a committed plan |

**Confirmed out of scope:** no save/ScriptableObject schema; no
tags/layers/physics-matrix change; no Animator parameter (never touches
`Quirrel.controller`); no Input Actions (still legacy `Input`, no package);
no change to X-axis follow behavior or its `0.15s` SmoothTime value; no
zoom/FOV feature; no new prefab; no sequence-break/ability-gating risk
(assessed above). **Pan-blocking scope is explicitly `IsDead OR
IsFullyCommitted`, not `IsFullyCommitted` alone** — `Die()` clears every
flag `IsFullyCommitted` reads, so `IsDead` must be checked as its own,
separate condition, matching every other gate in `PlayerController.cs`.

---

## 1. Design decisions

**1. Coupling shape:** a direct, optional, null-safe `[SerializeField]
private PlayerController _playerController` field on `CameraFollow`, read
only for its public `IsDead` and `IsFullyCommitted` members. Requires adding
`Quirrel.Player` to `Quirrel.Camera.asmdef`'s references (non-circular).
**Tradeoff:** ends `CameraFollow`'s previously-total decoupling from
`PlayerController` — narrow, one-directional (Camera → Player), read-only,
inert when unassigned. The X/Y-follow-target logic itself remains fully
decoupled (still just a generic `Transform`) — only the pan-gating check is
coupled. Rejected alternative: a shared interface in a new third asmdef —
over-engineered for a two-script camera module (YAGNI).

**2. Where W/S is read, and where the gate lives:** a new private
`Update()` method on `CameraFollow` reads raw W/S every frame into a
private `_panDirection` field (`+1`/`-1`/`0`, same accumulation shape as
`PlayerController`'s A/D). **The block-gate lives inside `Tick()`, not
`Update()`, and reads `_playerController.IsDead ||
_playerController.IsFullyCommitted` — not `IsFullyCommitted` alone** (since
`Die()` clears every flag `IsFullyCommitted` reads, checking it alone would
silently let pan work over a corpse; this matches every other gate in
`PlayerController.cs`). `Tick()` computes `panBlocked = _playerController !=
null && (_playerController.IsDead || _playerController.IsFullyCommitted)`
and uses `0` in place of `_panDirection` whenever blocked. Putting this in
`Tick()` rather than `Update()` is deliberate: `Update()` never runs in
EditMode tests, so a gate that lived only inside `Update()` would be
unverifiable without Play Mode; putting it in `Tick()` makes it directly
testable via reflection-set `_panDirection` and directly-set `IsDead`.

**3. Slide speed/easing:** reuse the existing `SmoothDamp`/`0.15s`
`SmoothTime` pattern, applied to a second axis (Y) with its own `_velocityY`
ref, for consistency with the X-axis follow — no new tunable, no new
pattern.

**4. Additive vs. replace:** purely additive. `Tick()`'s Y target becomes
`_baseY + panDirection * panDistance` (renaming `_fixedY` → `_baseY` since
it's no longer literally "fixed" — a private, non-serialized field, zero
effect on the scene's serialized YAML), fed through the *same* `SmoothDamp`
call already used for X, just on the Y axis. If `IsDead` or
`IsFullyCommitted` becomes true *while* the camera is mid-pan, `Tick()`
recomputes the blocked target as `_baseY` on the very next call — the
camera glides back over the same 0.15s time constant, not a snap-cut. No
extra code needed; it falls out of recomputing the target every frame.

**Pan distance sourcing:** cache a `Camera` component reference once in
`EnsureInitialized()` (idempotent `GetComponent<Camera>()`), and read
`.orthographicSize` **fresh inside `Tick()` every call** (not cached) — so a
future retune of orthographic size is picked up automatically, with zero
code change. A `[SerializeField] private float _panDistanceFallback = 5f`
is used **only** when no `Camera` component is present (true today only for
the bare EditMode test GameObjects). **Tradeoff:** slightly more moving
parts than a single hardcoded `5f`, but removes the exact desync risk of
`5f` silently going stale if orthographic size is ever retuned.

**Metroidvania-specific check:** see Section 0's sequence-break
dismissal — this feature moves only the camera, not the player, and adds no
persisted unlock, so it is outside the ability-gating/progression
regression category entirely.

---

## Phase 1 — Gameplay code

#### Task 1.1: [GAMEPLAY] Wire an optional `PlayerController` reference into `CameraFollow` (asmdef edge)
**Depends on:** none
**Parallel:** N/A (first task in phase)
**Touches:** `Assets/Scripts/Camera/Quirrel.Camera.asmdef`,
`Assets/Scripts/Camera/CameraFollow.cs` (field + class doc comment only —
no method bodies yet)

**Regression risk:** Adding an assembly reference (`Quirrel.Camera →
Quirrel.Player`) is a compile-time-checked change, not a silent one —
confirmed non-circular in Section 0. The one real risk is the stale class
doc comment: it currently makes an absolute architectural claim ("never
about the player specifically") that becomes false the moment this field
exists — left uncorrected, a future reader would trust a doc comment that
actively misdescribes the file.

Add `[SerializeField] private PlayerController _playerController;` —
optional, Inspector-assignable, null-safe (no `[RequireComponent]`, no
null-forgiving assumption anywhere it's read). Update the class doc comment
to state: the X/Y-follow-target logic remains decoupled (still a generic
`Transform`); this one field is a narrow, read-only, optional exception
added specifically for pan-gating (reading both `IsDead` and
`IsFullyCommitted`), and an unassigned reference means panning is simply
never blocked — a documented, deliberate fallback, not an oversight.

**Acceptance criteria:**
- [ ] `Quirrel.Camera.asmdef`'s `"references"` array contains
      `"Quirrel.Player"` and nothing else changes in that file
- [ ] `CameraFollow.cs` compiles with the new field, default `null`, with no
      `[RequireComponent]` or other hard dependency on it existing
- [ ] Class doc comment no longer claims total decoupling from
      `PlayerController`; it names the one field, that it's read for both
      `IsDead` and `IsFullyCommitted`, and its null-safe fallback behavior
      explicitly
- [ ] **Regression check:** existing `CameraFollowTests.cs` suite (2 tests)
      still compiles and passes unmodified — this task adds no new behavior
      yet, only the field and the doc correction

---

#### Task 1.2: [GAMEPLAY] Implement W/S pan input and the gated, additive Y-pan in `Tick()`
**Depends on:** Task 1.1
**Parallel:** no — same file as Task 1.1, direct dependency
**Touches:** `Assets/Scripts/Camera/CameraFollow.cs` (`EnsureInitialized()`,
`Tick()`, new `Update()`, new fields)

**Regression risk:** This is the task that actually breaks
`CameraFollowTests.cs`'s current universal "Y never moves" invariant — on
purpose (paired test update is Task 2.1a, immediately downstream). The
rename `_fixedY` → `_baseY` is a **private, non-serialized** field —
confirmed zero effect on the scene's serialized YAML. Suite-health note:
this task alone does not turn the existing suite red, because
`_panDirection` defaults to `0` and `SmoothDamp(3f, 3f, ...)` is a true
no-op — the existing (not-yet-renamed) test's numeric assertion still
passes exactly as before, even before Task 2.1a renames it. The rename is
about correcting the test's *stated scope*, not fixing a broken assertion.
The one place this task must not regress: the existing X-axis convergence
test and the existing "no-op if `target` is unassigned" contract — Y-pan
math must sit inside that same early-return guard. **The highest-risk item
in this task: the gate must read `IsDead || IsFullyCommitted`, not
`IsFullyCommitted` alone** — `PlayerController.Die()` clears every flag
`IsFullyCommitted`'s formula reads, so a gate checking only
`IsFullyCommitted` would let pan work normally over a corpse, contradicting
this feature's own stated design and silently diverging from every other
gate in `PlayerController.cs`.

Implementation, in words:
- `EnsureInitialized()`: additionally cache `_camera =
  GetComponent<Camera>()` (idempotent, may legitimately stay `null` — every
  use below is null-checked). Rename `_fixedY` → `_baseY` (identical
  capture logic, name only).
- New fields: `private float _velocityY;` (SmoothDamp ref, mirrors
  `_velocityX`), `private float _panDirection;` (`-1`/`0`/`+1`, written by
  `Update()`, consumed by `Tick()`), `[SerializeField] private float
  _panDistanceFallback = 5f;`.
- New `private void Update()`: `_panDirection = 0f; if
  (Input.GetKey(KeyCode.W)) { _panDirection += 1f; } if
  (Input.GetKey(KeyCode.S)) { _panDirection -= 1f; }` — identical
  accumulation shape to `PlayerController`'s A/D handling.
- `Tick(float deltaTime)`: after the existing `target == null` early
  return, compute `panBlocked = _playerController != null &&
  (_playerController.IsDead || _playerController.IsFullyCommitted)`, then
  `panDirection = panBlocked ? 0f : _panDirection`, then `panDistance =
  _camera != null ? _camera.orthographicSize : _panDistanceFallback`, then
  `targetY = _baseY + panDirection * panDistance`, then `newY =
  Mathf.SmoothDamp(position.y, targetY, ref _velocityY, SmoothTime,
  Mathf.Infinity, deltaTime)`. Final position becomes `(newX, newY,
  _fixedZ)` — `_fixedZ` and its own capture logic are completely untouched.

**Acceptance criteria:**
- [ ] With `_panDirection` forced to `0` and no `_playerController`
      assigned, camera Y converges to and stays at `_baseY` — byte-for-byte
      the pre-feature behavior
- [ ] With `_panDirection` forced to `+1`, camera Y converges to `_baseY +
      panDistance` (using the live `Camera.orthographicSize` if present,
      else `_panDistanceFallback`)
- [ ] With `_panDirection` forced to `-1`, camera Y converges to `_baseY -
      panDistance`
- [ ] Setting `_panDirection` back to `0` after convergence causes Y to
      converge back to `_baseY` over the same `SmoothTime`
- [ ] With a `_playerController` assigned whose `IsFullyCommitted` is true
      (`IsDead` false), `_panDirection` forced to `+1` has **no effect** —
      Y stays at `_baseY`
- [ ] **With a `_playerController` assigned whose `IsDead` is true (all
      `IsFullyCommitted`-contributing flags false, matching real
      post-`Die()` state), `_panDirection` forced to `+1` has no effect** —
      Y stays at `_baseY`
- [ ] With no `_playerController` assigned, `_panDirection` forced to `+1`
      still pans normally (confirms the "unassigned = never blocked"
      fallback is real, not just documented)
- [ ] `target == null` still causes `Tick()` to no-op entirely, including
      skipping the Y-pan math
- [ ] X-axis convergence behavior and the `0.15s` `SmoothTime` constant are
      completely unchanged for the X axis
- [ ] **Regression check:** the pre-existing (not-yet-renamed)
      `CameraFollowTests.cs` suite still compiles and passes after this
      task alone

---

#### Task 1.3: [GAMEPLAY] Wire the real `PlayerController` reference in the scene
**Depends on:** Task 1.2
**Parallel:** yes — with Task 2.1a and Task 2.1b (different files: scene vs.
test scripts)
**Touches:** `Assets/Scenes/SampleScene.unity` (the `CameraFollow`
MonoBehaviour block, `fileID 519420033`, only)

**Regression risk:** This is a shared scene file — the project's only
scene — but the specific edit is additive (one new serialized reference
value on one existing component instance) and does not touch the `Camera`
component, the `Player` GameObject, or `target`'s existing wiring.
Mitigated by the fact that this is the only place `CameraFollow` is
instantiated in the project — exactly one instance to update.

Set `_playerController` on the `CameraFollow` component (`fileID
519420033`) to reference the `PlayerController` component on the `Player`
GameObject (`fileID 563516958`) — the exact analog of how `target` already
points at the Player's `Transform` (`fileID 563516963`). Leave
`_panDistanceFallback` at its default (`5f`) — inert in this scene since a
real `Camera` component is always present alongside `CameraFollow` here.

**Acceptance criteria:**
- [ ] `CameraFollow`'s `_playerController` field in the scene resolves to
      the real `PlayerController` component on the `Player` GameObject
      (verify via Unity Editor Inspector or Unity MCP, not just by
      eyeballing the YAML)
- [ ] `target`'s existing wiring is unchanged
- [ ] The `Camera` component's `orthographic size: 5` value is unchanged
- [ ] Opening the scene produces zero console errors/warnings related to
      `CameraFollow`
- [ ] **Regression check:** existing X-axis camera-follow behavior in Play
      Mode is visually unchanged

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] asmdef + field ──→ 1.2 [GAMEPLAY] pan mechanics ──→ 1.3 [GAMEPLAY] scene wiring
                                                                        (parallel with 2.1a/2.1b)
```

---

## Phase 2 — QA

#### Task 2.1a: [QA] Rename/narrow the existing Y-invariant test; add asmdef edge, reflection helpers, and direction/return-to-baseline coverage
**Depends on:** Task 1.2
**Parallel:** yes — with Task 1.3 (different files)
**Touches:** `Assets/Scripts/Camera/Tests/EditMode/Quirrel.Camera.EditModeTests.asmdef`,
`Assets/Scripts/Camera/Tests/EditMode/CameraFollowTests.cs`

**Regression risk:** The core risk this task exists to close:
`YAndZPosition_StayFixed_WhileFollowingOnX` currently asserts an
unconditional invariant that Task 1.2 deliberately breaks. The test's
actual numeric assertions do not need to change — only its scope
statement — because with `_panDirection` defaulting to `0`,
`SmoothDamp(3f, 3f, ...)` is a true no-op. The asmdef edit is required
before any of the new tests in this task or Task 2.1b can even compile.

**Work (scoped to this task — direction/baseline behavior only):**
1. Add `"Quirrel.Player"` to `Quirrel.Camera.EditModeTests.asmdef`'s
   `"references"`.
2. Rename `YAndZPosition_StayFixed_WhileFollowingOnX` →
   `YAndZPosition_StayFixed_WhileFollowingOnX_AndNoPanInputHeld`. Update its
   doc comment to state explicitly that this invariant is now scoped to "no
   pan input held" (default `_panDirection == 0`) — do not touch its
   assertions or setup.
3. Add a reflection helper local to this test file to set `CameraFollow`'s
   private `_panDirection` field directly, bypassing `Update()`/`Input`
   entirely. Also add a reflection helper to set `CameraFollow`'s private
   `_playerController` field directly (private `[SerializeField]`, no
   public setter) — implemented here as a file-scoped utility both this
   task's and Task 2.1b's tests share.
4. New tests (direction/baseline only):
   - `YPosition_PansUpByFallbackDistance_WhileWDirectionForced` — no
     `Camera` component on the test GameObject; force `_panDirection =
     1f`; converge; assert Y == start Y + `5f`.
   - `YPosition_PansDownByFallbackDistance_WhileSDirectionForced` — same,
     `_panDirection = -1f`, assert Y == start Y − `5f`.
   - `YPosition_ReturnsToBaseline_AfterPanDirectionReleased` — converge
     panned up, then set `_panDirection = 0f`, run further steps, assert Y
     returns to start Y.

**Acceptance criteria:**
- [ ] `Quirrel.Camera.EditModeTests.asmdef`'s `"references"` array contains
      `"Quirrel.Player"` and nothing else changes in that file
- [ ] Renamed test's assertions/tolerances are byte-identical to today's;
      only its name and doc comment change
- [ ] All 3 new direction/baseline tests pass
- [ ] Existing `XPosition_ConvergesToStationaryTarget_AfterFixedStepSimulation`
      test passes unmodified
- [ ] No test calls `Input.GetKey`/`GetKeyDown` directly — all pan-input
      simulation goes through the reflection-set `_panDirection` field
- [ ] **Regression check:** `Quirrel.Camera.EditModeTests` assembly
      compiles and passes with a strictly greater test count than the
      pre-plan baseline

---

#### Task 2.1b: [QA] Live-orthographic-size and full-commit/dead gating test coverage
**Depends on:** Task 2.1a (needs the asmdef edge and shared reflection
helpers; same file, cannot run parallel with it)
**Parallel:** yes — with Task 1.3; no — with Task 2.1a (same file)
**Touches:** `Assets/Scripts/Camera/Tests/EditMode/CameraFollowTests.cs`
only

**Regression risk:** Additive only. Confirmed the 4th test (`IsDead`) does
not push this task over its 4h budget: `IsDead` is `public bool IsDead {
get; set; }` — a real public setter, so this test needs **no reflection
plumbing at all** (unlike the `DefendHeld` case), keeping this task's total
scope comfortably inside budget.

**Work:**
- `YPosition_UsesLiveCameraOrthographicSize_WhenCameraComponentPresent` —
  `AddComponent<Camera>()` on the test GameObject, set `orthographicSize =
  7f`, force `_panDirection = 1f`, converge, assert Y == start Y + `7f`
  (not the `5f` fallback).
- `PanInput_IsBlocked_WhilePlayerControllerIsFullyCommitted` — attach a
  real `PlayerController` to a separate `GameObject`, force `DefendHeld =
  true` via the same `PropertyInfo`-based reflection technique
  `PlayerControllerTests.cs` already uses, assign it into
  `CameraFollow._playerController` via the reflection helper added in Task
  2.1a, force `_panDirection = 1f`, converge, assert Y stays at start Y
  (blocked).
- **`PanInput_IsBlocked_WhilePlayerControllerIsDead`** — attach a real
  `PlayerController` to a separate `GameObject`, set `controller.IsDead =
  true` directly (its public setter — no reflection needed), leave
  `DefendHeld`/other full-commit flags at their real post-`Die()` values
  (do not also force `DefendHeld` true — the point is to prove `IsDead`
  alone blocks pan even when `IsFullyCommitted` is false, matching real
  `Die()` behavior exactly), assign the controller into
  `CameraFollow._playerController`, force `_panDirection = 1f`, converge,
  assert Y stays at start Y (blocked).
- `PanInput_IsNeverBlocked_WhenPlayerControllerUnassigned` — leave
  `_playerController` at its default `null`, force `_panDirection = 1f`,
  converge, assert Y reaches start Y + `5f` normally.

**Acceptance criteria:**
- [ ] All 4 tests pass, each isolating exactly one of: live-orthographic-size
      sourcing, full-commit blocking (`IsFullyCommitted` true, `IsDead`
      false), dead blocking (`IsDead` true, `IsFullyCommitted` false),
      unassigned-never-blocks
- [ ] The dead-blocking test specifically asserts blocking occurs with
      `IsFullyCommitted` false and no reflection used — confirming the
      gate's `IsDead` branch is independently load-bearing, not merely
      redundant with the `IsFullyCommitted` branch
- [ ] Both `PlayerController`-attached tests' GameObjects are torn down in
      `[TearDown]` alongside the existing camera/target objects — no leaked
      GameObject between tests
- [ ] No test calls `Input.GetKey`/`GetKeyDown` directly
- [ ] **Regression check:** full `Quirrel.Camera.EditModeTests` assembly
      (Task 2.1a's tests + this task's 4) passes together, zero unexpected
      failures, no test-order dependency

---

#### Task 2.2: [QA] Manual playtest addendum, live verification, and backlog cleanup
**Depends on:** Task 1.3, Task 2.1b
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md`,
`Docs/Backlog.md`

**Regression risk:** None to code — documentation/backlog only.
`Input.GetKey(KeyCode.W/S)` and the "both held" cancellation are exactly
the class of behavior the automated suite structurally cannot verify — a
live pass is the only real proof, and this is also the row set that must
include the post-`Die()` case live, not just in EditMode.

Add a new section, following the existing `5a` precedent exactly — call it
**`5b. Look up/down camera pan (Docs/Plans/006_look-up-down-camera-pan.md)`**
— with checklist rows for: W held pans up smoothly and by a plausible
half-screen amount; release returns smoothly to normal; S held pans down,
release returns to normal; both W and S held simultaneously produce no pan
(regression pin for the accumulation-cancel design); pan is fully blocked
during an Attack, a Defend hold, and a Hurt stun window (three rows, one
per `IsFullyCommitted`-contributing state); **pan is fully blocked after
`Die()` specifically** (a fourth, separate row — do not fold this into the
`IsFullyCommitted` rows, since it is the one case where `IsFullyCommitted`
itself is `false` and only `IsDead` is doing the blocking); starting a
full-commit state (or dying) while mid-pan glides the camera back rather
than snapping.

Also remove the "Look up/down (camera pan)" entry from `Docs/Backlog.md`
now that it has a committed plan.

**Acceptance criteria:**
- [ ] New `5b` section added with all rows listed above, following the
      `5a` section's exact format, with the post-`Die()` row explicitly
      separate from the three `IsFullyCommitted` rows
- [ ] Live verification performed and recorded PASS/FAIL for every new row,
      including the both-keys-cancel case, all three `IsFullyCommitted`
      cases, and the separate post-`Die()` case
- [ ] Any FAIL is logged as a bug per this doc's existing bug-report format
      and routed back through the pipeline, not silently patched inside
      this task
- [ ] `Docs/Backlog.md`'s "Look up/down (camera pan)" entry is removed; no
      other backlog entries altered
- [ ] **Regression check:** re-confirm the existing X-axis follow and the
      existing `5a` (air-attack) manual checks still read correctly

---

**Phase 2 dependency graph:**
```
2.1a [QA] rename + direction/baseline tests ──→ 2.1b [QA] live-ortho-size + IsFullyCommitted/IsDead gating tests ──┐
                                                                                                                    ├──→ 2.2 [QA] manual playtest + backlog cleanup
1.3 [GAMEPLAY] scene wiring ────────────────────────────────────────────────────────────────────────────────────────┘
(1.3 parallel with both 2.1a and 2.1b — disjoint files; 2.1a/2.1b sequential — same file)
```

---

## Explicitly out of scope for this plan
Any Input System migration (still legacy `KeyCode`/`Input.GetKey`, matching
`Docs/Plans/003`'s established scope boundary); any change to the X-axis
follow's `SmoothTime` value or behavior; camera zoom/FOV; any new Animator
parameter; any save/ScriptableObject schema; rebindable pan keys; any
sequence-break/ability-gating concern (assessed and dismissed in Section 0).
**Pan-blocking is explicitly `IsDead OR IsFullyCommitted`** — never
`IsFullyCommitted` alone, since `Die()` clears every flag that formula
reads.
