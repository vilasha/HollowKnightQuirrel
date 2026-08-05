# Implementation Plan — Bench Visual Fixes (Plan 008 follow-up)

**Status:** ✅ APPROVED (round 2, accepted by implementation-plan-reviewer)
**Author:** implementation-plan-architect
**Date:** 2026-08-05
**Feature:** Three bugs found during the live/manual Play Mode QA pass of plan
008 (bench sit/stand mechanic, `Docs/Plans/008_bench-sit-mechanic.md`,
already implemented and shipped), fully documented with root cause and the
user's own corrected design direction in
`Docs/Plans/002_manual-playtest-protocol.md`'s "## Bugs Found — 5d bench sit
mechanic (2026-08-05)" section:

1. **Bug 1 (Minor, Art):** `Bench_01.png` has residual near-white background
   leftovers around the backrest that the knockout pass should have removed.
2. **Bug 2 (Minor, Art/Gameplay scene setup):** The `Bench` GameObject reads
   as floating above the ground rather than resting on it.
3. **Bug 3 (Major, Art/Gameplay design):** Sitting currently leaves Quirrel
   wherever he was standing (plan 008 Decision 3's "no centering required,"
   explicitly overridden by the user as wrong). Corrected behavior: on a
   successful sit, Quirrel's transform snaps to the bench's sit-anchor
   position and the standalone `Bench_01` sprite is hidden for the duration
   of the sit (the composite `Quirrel_Sitting_01` art already has bench
   geometry baked in, by design — no re-cropping of that sprite). On
   standing up (walking away, or an interrupting `Hurt()`/`Die()`),
   `Bench_01` is shown again, now visibly empty.

---

## 0. Summary and verified blast radius

**Confirmed from the actual source (read directly, not assumed):**

- `Assets/Scripts/Player/PlayerController.cs` (959 lines as of this plan,
  confirmed by direct read). `IsNearBench` is `public bool IsNearBench {
  get; set; }` (plan 008 Decision 4) — a plain bool with no reference to
  *which* `Bench` set it. `TrySit(bool isNearBench)` takes that bool as a
  parameter (not read internally), exactly mirroring `TryJump`/`TryAttack`'s
  EditMode-testability convention; it currently does nothing to `transform`/
  `_rigidbody.position` on success. `StandUpIfWalking`, `Hurt()`, and
  `Die()` each independently inline their own `_isSitting = false;` +
  `_animator?.SetBool(IsSittingHash, false);` pair — three separate copies
  of the same two lines, not yet factored into a shared helper (unlike
  `IsIdleAndGrounded()`, which plan 008 Decision 8 already did factor out).
  `_rigidbody` is already cached via the existing `EnsureCachedComponents()`
  idiom used throughout this file.
- `Assets/Scripts/Environment/Bench.cs` (71 lines, confirmed by direct
  read): `[RequireComponent(typeof(BoxCollider2D))]` only — no
  `SpriteRenderer` requirement or reference exists yet.
  `HandleTriggerEnter`/`HandleTriggerExit` are public, `Collider2D`-
  parameterized, EditMode-testable methods that today only touch
  `PlayerController.IsNearBench` (bool). No sit-anchor concept, no
  visibility-toggle method exists yet.
- **A real, non-hypothetical asmdef landmine, found during this plan's own
  blast-radius mapping, not assumed from the bug report's own suggested
  fix:** `Assets/Scripts/Environment/Quirrel.Environment.asmdef` references
  `["Quirrel.Player"]`; `Assets/Scripts/Player/Quirrel.Player.asmdef`
  references `[]` (confirmed by direct read of both files). The bug
  report's own suggested fix ("`IsNearBench` becoming a `Bench` reference")
  would require `PlayerController` (in `Quirrel.Player`) to hold a field
  typed as the concrete `Bench` class (defined in `Quirrel.Environment`) —
  which would require `Quirrel.Player` to reference `Quirrel.Environment`,
  creating a circular asmdef reference (`Quirrel.Player` →
  `Quirrel.Environment` → `Quirrel.Player`) that Unity cannot compile. This
  is the single most important finding of this plan's blast-radius pass —
  see Decision 1 for the resolution (a small interface defined in
  `Quirrel.Player`, implemented by `Bench`).
- `Assets/Scripts/Camera/CameraFollow.cs` (confirmed by direct read): the
  X/Y follow uses `Mathf.SmoothDamp(position.x, target.position.x,
  ref _velocityX, SmoothTime, Mathf.Infinity, deltaTime)` — a continuous
  glide toward the target's live position, never an instant snap of its own
  Transform. This means Bug 3's position-snap-on-sit will **not** need any
  `CameraFollow` change to avoid a jarring cut: the camera will glide to the
  new position exactly the way it already glides on any other sudden
  Player-position change, the same precedent plan 007's mid-pan interrupt
  glide-back already established. Confirmed by direct read, not assumed —
  flagged as a blast-radius item precisely because "does the camera need a
  change" was a real open question worth checking, not skipping.
- `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` (1105+
  lines, confirmed by direct read of the Task 6.1 bench section, lines
  860–1105): every existing bench-related test sets `IsNearBench` directly
  as a bool (`_playerController.IsNearBench = true;`) or passes a bool
  literal to `TrySit(true)`/`TrySit(false)` — **none of them ever set or
  reference a `Bench`/seat reference of any kind.** This is the concrete
  proof that keeping `IsNearBench` (bool) completely untouched and adding a
  new, independently-set reference property alongside it (Decision 2) is
  the correct minimal-regression path — every one of these ~19 existing
  tests keeps compiling and passing with zero modification under that
  design.
- `Assets/Scripts/Environment/Tests/EditMode/BenchTests.cs` (82 lines,
  confirmed by direct read): 4 existing tests, all exercising
  `HandleTriggerEnter`/`HandleTriggerExit` against `IsNearBench` only. Test
  fixture builds a bare `PlayerController` (`AddComponent<Rigidbody2D>()` +
  `AddComponent<PlayerController>()`, no `SpriteRenderer`) and a bare
  `Bench` (`AddComponent<Bench>()`, relying on `RequireComponent` to
  auto-add the `BoxCollider2D`).
- `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`'s
  `CreatePlayer()` fixture (line 23–29, confirmed by direct read) already
  adds both a `Rigidbody2D` (cached as `_rigidbody`, exposed to the test
  class) and a `SpriteRenderer` — so `Rigidbody2D.position`-based assertions
  and `SpriteRenderer.enabled`-based assertions are both directly
  exercisable by new EditMode tests with zero fixture changes.
- `Assets/Scenes/SampleScene.unity` (confirmed by direct read of the
  `Bench` GameObject block, fileID `1161090211`): `Bench` sits at world
  `{0, 0, 0}` with **no children** (`m_Children: []`). Its `SpriteRenderer`
  (fileID `1161090214`) references `Bench_01.png` by GUID
  `a843ff31170e0424bb73233ffb27ffcf`, `sortingOrder: -1`, size `{1.37,
  1.05}`. Its `BoxCollider2D` (fileID `1161090213`) has local `m_Offset:
  {x: 0, y: 1.5}`, `m_Size: {x: 1.37, y: 3}`, `m_IsTrigger: 1`. Nothing
  about this block changed since plan 008 shipped.
- `Assets/Editor/Tools/QuirrelReferenceSpriteImporter.cs` and
  `Assets/Editor/Tools/QuirrelSpriteKnockout.cs` (confirmed by direct read,
  unmodified since plan 008): `QuirrelSpriteKnockout.FloodFillBackground`
  is a **border-connected** flood fill only — it has no ability to remove a
  non-white contaminant (e.g. a shadow blob) or a near-white region that
  isn't reachable from the canvas border, and no threshold tuning fixes
  either of those two failure modes. `CropResizeAndKnockout` (standalone,
  single-subject entry point) and `CropRegionResizeAndKnockout` (plan 008
  Task 1.1's region-pre-crop overload, already shipped and unmodified) both
  remain fully reusable as-is — this plan's art fix needs **zero new tool
  code**, only a re-run with different parameters and/or the existing
  region overload, exactly the same "additive tool reuse" precedent plan
  008 Task 1.2 already established for the Quirrel composite sprite.
- `ART.md` (confirmed by grep across the full file): no changes are needed
  by this plan. §7's Animation Timing table's `Sitting` row is untouched
  (this plan changes no Animator topology). §2.4's `#F6E7C7` reserved
  bench-glow color stays reserved, unused. No new token, no new row.
- This project's `CLAUDE.md` (confirmed by direct read, full file): the
  "Prioritize Unity's own tools" and "If Unity becomes unreachable"
  sections explicitly cite `Docs/Plans/008_bench-sit-mechanic.md`'s own
  implementation history as "a real example of this going wrong" — a
  Python reimplementation of Unity's own knockout logic was treated as
  equivalent to running the real tool and had to be redone. This plan's
  Bug 1 task is written to use Python **only** for read-only pixel
  diagnosis (sampling RGB values to characterize the contamination) and
  requires the actual regeneration to run through the live Unity Editor
  (the existing, unmodified C# tool), per that section's explicit rule.

**Complete write list for this plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Scripts/Player/IBenchSeat.cs` (new) | Task 1.1 | New interface — breaks the circular-asmdef problem named above |
| `Assets/Scripts/Environment/Bench.cs` | Task 1.2 | Implements `IBenchSeat`; adds sit-anchor + visibility toggle; extends trigger callbacks |
| `Assets/Scripts/Player/PlayerController.cs` | Task 1.3 | New `NearBench` property, `_seatedBench` field, shared `StandUp()` helper, `TrySit`/`Hurt`/`Die`/`StandUpIfWalking` wiring |
| `Assets/Sprites/Environment/Bench/Bench_01.png` (+`.meta`, same GUID) | Task 2.1 | Regenerated in place — knockout re-tune (Bug 1) |
| `Assets/Scenes/SampleScene.unity` | Task 2.2 | `Bench` Transform.position.y correction (Bug 2) |
| `Assets/Scenes/SampleScene.unity` | Task 2.3 | New `SitAnchor` child GameObject under `Bench`; wires `Bench._sitAnchor` |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 3.1 | New tests for snap/hide/restore |
| `Assets/Scripts/Environment/Tests/EditMode/BenchTests.cs` | Task 3.2 | New tests for `SitAnchor`/`SetVisible`/`NearBench` wiring |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 3.3 | New `5e` section; supersede row 5d.2; annotate "Bugs Found" as resolved |

**Confirmed out of scope:** any change to `Assets/Animations/Quirrel.controller`
(no Animator topology change — confirmed during blast-radius mapping, not
assumed; this is a position/visibility fix only); any change to
`CameraFollow.cs` (its existing `SmoothDamp` glide already covers the new
position snap, confirmed by direct read); any change to `IsNearBench`'s
existing bool shape/semantics (kept byte-identical; `NearBench` is
additive, not a replacement — Decision 2); a multi-bench save-point
registry or persistence of any kind (still deferred, plan 008 Decision 7
unchanged); any `ProjectSettings`/tags/physics-layers/collision-matrix
change; any Health/HP/save system; re-cropping `Quirrel_Sitting_01.png` to
remove its baked-in bench geometry (explicitly rejected per the user's own
corrected spec — the composite art IS the sitting visual, by design); a
generalized multi-seat/`ISeatable` system beyond the 2-member `IBenchSeat`
interface this plan actually needs (YAGNI); any `ART.md` change (confirmed
none needed).

**Metroidvania-specific check:** no new traversal ability is granted or
changed (the position snap re-places Quirrel onto a static prop he was
already standing next to — no new reach, no jump/dash, no sequence-break
risk); no new reachable area; no save/persisted-unlock state exists yet
(unchanged from plan 008); checkpoint integrity and backtracking concerns
remain not-yet-applicable (still one bench, at the one spawn point, no
persisted state). This plan is outside the ability-gating/progression
regression category, same as plan 008.

---

## 1. Design decisions

**1. A new `IBenchSeat` interface, defined in `Quirrel.Player` (not
`Quirrel.Environment`), is the mechanism `PlayerController` uses to know
which specific bench it's near/seated on — not a direct `Bench` reference.**
See Section 0's blast-radius finding: typing `PlayerController.NearBench` as
the concrete `Bench` class would require `Quirrel.Player` to reference
`Quirrel.Environment`, which already references `Quirrel.Player` — a
circular asmdef reference Unity cannot compile. `IBenchSeat` is a 2-member
interface (`Transform SitAnchor { get; }`, `void SetVisible(bool visible)`)
living in `Assets/Scripts/Player/IBenchSeat.cs`, requiring zero new
reference on `Quirrel.Player.asmdef`. `Bench` (in `Quirrel.Environment`,
which already legitimately references `Quirrel.Player`) implements it —
that direction of dependency was already established and load-bearing, so
this is a pure addition to the existing, correct DAG, not a restructuring
of it. Rejected alternative: the bug report's own literal suggestion, a
direct `Bench` reference — rejected once the circular-reference compile
failure was found during this plan's own blast-radius pass, not assumed
from the report. Rejected alternative: invert the asmdef graph (`Quirrel.
Player` → `Quirrel.Environment`, remove the reverse reference) — rejected
as a much larger, genuinely invasive change to a foundational, already-
shipped asmdef graph for no benefit over the 2-member-interface fix.

**2. `IsNearBench` (bool) is left completely untouched. `NearBench`
(`IBenchSeat`, nullable) is a new, independently-set paired property, not a
replacement or a derived value.** Per Section 0's confirmed finding: every
existing bench-related test (~19 across `PlayerControllerTests.cs` and
`BenchTests.cs`) sets/reads `IsNearBench` as a plain bool and never
references any bench object — keeping it byte-identical means every one of
those tests keeps compiling and passing with zero modification. `Bench`'s
trigger callbacks set both properties together (`IsNearBench = true;
NearBench = this;` on enter; the bool `false` and the reference cleared —
guarded, see Decision 7 — on exit). The accepted cost: two properties set
in the same two places instead of one, a minor duplication traded for zero
regression risk to already-shipped, tested code — an explicit,
deliberately-conservative choice given this project's top priority.

**3. `_seatedBench` is a separate, PlayerController-owned field, latched
once at the moment `TrySit` succeeds — not re-read from the live
`NearBench` property later, at stand-up time.** Mirrors this file's own
existing `_isAirAttack`-latched-at-attack-start precedent (plan
`005_attack-while-jumping.md`) — a value computed once at commit-time,
reused later, rather than recomputed from a continuously-updated live
signal. `NearBench` is continuously driven by physical trigger overlap
(matching plan 008 Decision 3's proximity design) and isn't guaranteed to
still reference the same bench by the time a stand-up path runs in every
future (multi-bench) scenario, even though today, with exactly one bench,
the two values would always agree in practice. Rejected alternative: read
`NearBench` directly inside `StandUp()` — rejected as coupling "which bench
am I standing next to right now" to "which bench did I actually sit on,"
two questions that are only accidentally the same question in a
single-bench scene.

**4. The position snap writes `Rigidbody2D.position`, not
`transform.position` directly.** `PlayerController`'s `Rigidbody2D` is a
live, non-kinematic body (per `[RequireComponent(typeof(Rigidbody2D))]`
and the existing `ApplyHorizontalMovement`/jump-impulse code, both of which
already write `_rigidbody.velocity`, never `transform.position`, for the
exact same reason). Writing `transform.position` directly on a dynamic
Rigidbody2D is a documented Unity footgun: the physics engine's own cached
internal position can override a direct transform write on the very next
physics step, producing a one-frame visual snap-back. `Rigidbody2D.
position`'s setter is the physics-correct way to relocate a dynamic body
immediately, and this codebase already exclusively uses `_rigidbody.*`
writes for every other position/velocity mutation — this keeps the new
code consistent with that existing discipline rather than introducing the
one exception. Verified live in Unity (Task 1.3's own acceptance
criteria), not assumed, since EditMode's outside-Play-Mode physics
behavior is a real enough divergence risk to be worth confirming rather
than trusting on paper.

**5. A shared private `StandUp()` helper replaces the three existing,
separately-inlined `_isSitting = false;` / Animator-bool-clear pairs in
`StandUpIfWalking`, `Hurt()`, and `Die()`.** Mirrors plan 008 Decision 8's
own "one shared helper, not N duplicated copies" precedent
(`IsIdleAndGrounded()`). `StandUp()` clears `_isSitting`, clears the
Animator `IsSitting` bool (identical null-check style to today), and adds
the new bench-visibility-restore/`_seatedBench`-clear logic in exactly one
place instead of three. `Hurt()`'s and `Die()`'s existing, already-tested
reset *ordering* is preserved exactly — the new call replaces their
existing two lines at the same position in their existing sequence, it
does not reorder anything around it.

**6. `Bench.SitAnchor` falls back to the Bench's own `transform` when no
dedicated child anchor Transform is assigned, rather than throwing or
requiring one.** A `[SerializeField] private Transform _sitAnchor;`
(Inspector-assignable, defaults `null`) with `public Transform SitAnchor =>
_sitAnchor != null ? _sitAnchor : transform;` degrades gracefully — a
future second `Bench` instance that forgets to wire an anchor still
functions (snaps to the bench's own origin) rather than throwing a
`NullReferenceException` the first time a player sits on it.

**7. `Bench.HandleTriggerExit`'s new `NearBench`-clearing line is guarded
by reference equality against `this`.** `if (playerController.NearBench ==
(IBenchSeat)this) playerController.NearBench = null;` — a strictly safer
version of plan 008 Decision 7's already-named, accepted multi-bench
overlap limitation (which still applies to the plain `IsNearBench` bool,
unchanged by this plan): exiting bench A's zone should never silently wipe
out a `NearBench` reference that's actually already been reassigned to
bench B by an overlapping trigger. This does not attempt to fully solve
multi-bench overlap (still out of scope, still Decision 7's named
limitation for `IsNearBench`) — it only prevents the new reference-typed
property from being strictly *more* fragile than the bool it sits
alongside.

**8. Bug 1 and Bug 2 are diagnosed together but fixed and promoted as two
sequenced tasks, not one.** The bug reports' own suspected-cause notes
name a plausible shared root: the same near-white/threshold contamination
that leaves a halo around the backrest could also be inflating the
sprite's computed content bounding box at the *bottom* edge (a shadow/
margin the auto-bbox-detect step didn't fully exclude), which would make
the sprite's bottom-pivot land below the legs' true visual bottom — i.e.
Bug 1's fix could partially or fully resolve Bug 2 as a side effect. Task
2.1 (Bug 1) is written to investigate both symptoms together (backrest
halo AND bottom-margin inflation) using the same diagnostic pass, but Bug
2's actual Transform correction (Task 2.2) is sequenced strictly after,
because its exact residual offset can only be measured against Task 2.1's
already-regenerated asset, not guessed in advance.

**9. Bug 2's Y-position fix is kept as a small, dedicated task — not
handed to the user as a raw Inspector edit — but only for two structural
reasons, not because the edit itself is complex.** (a) It must be
live-measured against Task 2.1's regenerated sprite, which doesn't exist
until that task lands; (b) it edits the same `Bench` scene GameObject Task
2.3 (`SitAnchor` wiring) also needs to touch, and this project's "same
scene/same GameObject → never parallel" rule means these two edits must be
sequenced regardless. Per `CLAUDE.md`'s "Delegate to the human when that's
cheaper": once Task 2.2 lands with its own live-measured value, any further
aesthetic micro-nudge the user notices afterward is explicitly **not**
routed back through this pipeline — it's a one-line `Transform.position.y`
edit in the Inspector, cheaper for the user to do directly than to spin up
another plan/review/QA cycle over a single float. This is stated explicitly
in Task 2.2 itself so nobody re-opens the pipeline for a trivial follow-up
nudge.

---

## Phase 1 — Bug 3: sit/stand snap-and-hide mechanic [GAMEPLAY]

#### Task 1.1: [GAMEPLAY] New `IBenchSeat` interface
**Depends on:** none
**Parallel:** yes — with Task 2.1 (different files/assemblies entirely).
**Not** parallel with anything in this phase that depends on it (Tasks 1.2,
1.3 both do).
**Touches:** none existing — creates `Assets/Scripts/Player/IBenchSeat.cs`
(new)
**Regression risk:** the placement of this file (`Quirrel.Player`, not
`Quirrel.Environment`) is itself the regression-prevention mechanism named
in Decision 1 — this task must not add any new entry to `Quirrel.
Player.asmdef`'s `references` array; doing so would defeat the entire
point of this task.

Add a minimal interface with exactly two members: `Transform SitAnchor {
get; }` (the world-space Transform a sitting character should snap to) and
`void SetVisible(bool visible)` (toggle whatever visual represents the seat
prop). No other members — YAGNI, this plan needs exactly these two
operations and no speculative "seat type" hierarchy beyond a bench.

**Acceptance criteria:**
- [ ] `IBenchSeat` compiles inside `Quirrel.Player.asmdef`
- [ ] `Quirrel.Player.asmdef`'s `references` array is confirmed unchanged
      (still `[]`) by direct read before and after this task — **the
      regression check this whole task exists to satisfy**
- [ ] Interface has exactly the two members named above, nothing more

---

#### Task 1.2: [GAMEPLAY] `Bench.cs` — implement `IBenchSeat`, add sit anchor + visibility toggle, wire `NearBench`
**Depends on:** Task 1.1
**Parallel:** yes — with Task 1.3 (different file, both depend only on
1.1)
**Touches:** `Assets/Scripts/Environment/Bench.cs` (implements the new
interface; adds a serialized `_sitAnchor` field, a `SitAnchor` property, a
`SetVisible` method, and idempotent `SpriteRenderer` caching; extends the
existing bodies of `HandleTriggerEnter`/`HandleTriggerExit`)
**Regression risk:** `HandleTriggerEnter`/`HandleTriggerExit` are existing,
tested, load-bearing methods (plan 008 Task 6.3) — this task must only
*add* lines to their existing bodies. Their existing `IsNearBench`
bool-setting behavior, their signatures, and their no-`PlayerController`
no-op behavior must not change.

Implementation, in words:
- `Bench : MonoBehaviour, IBenchSeat`, alongside its existing
  `[RequireComponent(typeof(BoxCollider2D))]` add
  `[RequireComponent(typeof(SpriteRenderer))]` (a genuinely required
  dependency for `SetVisible` — Unity auto-adds a default `SpriteRenderer`
  to any `Bench` instance/prefab that doesn't already have one, including
  every existing EditMode test fixture that does `AddComponent<Bench>()`
  with no `SpriteRenderer`, so no test setup needs to change).
- `[SerializeField] private Transform _sitAnchor;` (Inspector-assignable,
  defaults `null`); `public Transform SitAnchor => _sitAnchor != null ?
  _sitAnchor : transform;` (Decision 6's fallback).
- New idempotent `EnsureCachedComponents()` (same name/shape/rationale as
  `PlayerController`'s own — Unity does not call `Awake()` outside Play
  Mode without `[ExecuteAlways]`, so this must be reachable from every
  testable public method, not only `Awake()`, per this project's own
  documented `EditMode Awake()` gotcha) caching a private `_spriteRenderer`
  field. Called from `Awake()` and defensively from `SetVisible`.
- `public void SetVisible(bool visible)`: `EnsureCachedComponents(); if
  (_spriteRenderer != null) _spriteRenderer.enabled = visible;`.
- `HandleTriggerEnter`: after the existing `playerController.IsNearBench =
  true;` line, add `playerController.NearBench = this;`.
- `HandleTriggerExit`: after the existing `playerController.IsNearBench =
  false;` line, add the reference-equality-guarded clear from Decision 7:
  `if (playerController.NearBench == (IBenchSeat)this) playerController.
  NearBench = null;`.

**Acceptance criteria:**
- [ ] `Bench` implements `IBenchSeat`; `SitAnchor` returns the assigned
      `_sitAnchor` when one is set in the Inspector, and falls back to
      `transform` when `_sitAnchor` is left unassigned
- [ ] `SetVisible(false)` disables the `Bench`'s `SpriteRenderer`;
      `SetVisible(true)` re-enables it
- [ ] `HandleTriggerEnter` now also sets `playerController.NearBench` to
      the `Bench` instance, in addition to its existing, unmodified
      `IsNearBench = true;` line
- [ ] `HandleTriggerExit` now also clears `playerController.NearBench` to
      `null`, but **only** when it currently points at this same `Bench`
      instance (Decision 7's guard) — a `NearBench` reference already
      pointing at a different bench is left untouched
- [ ] The 4 pre-existing `BenchTests` (enter/exit sets/clears `IsNearBench`
      bool; both no-throw-without-`PlayerController` cases) pass unmodified
      — **regression check**
- [ ] `Quirrel.Environment.asmdef`'s `references` array is confirmed
      unchanged (still exactly `["Quirrel.Player"]`) — **regression check**

---

#### Task 1.3: [GAMEPLAY] `PlayerController.cs` — `NearBench`, seated-bench snap/hide/restore
**Depends on:** Task 1.1
**Parallel:** yes — with Task 1.2 (different file, both depend only on
1.1)
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (new `NearBench`
property, new `_seatedBench` field, new private `StandUp()` helper,
additions inside `TrySit`; `StandUpIfWalking`/`Hurt()`/`Die()`'s existing
inline `_isSitting`/Animator-bool clears replaced by calls to the new
shared helper, at the same point in their existing sequences)
**Regression risk:** `TrySit`, `StandUpIfWalking`, `Hurt()`, and `Die()`
are existing, heavily-tested (plan 008 Task 6.1), order-sensitive methods.
This task must not reorder any of `Hurt()`'s/`Die()`'s other existing
resets (`_jumpInProgress`, `_isAttacking`, `DefendHeld`, `LookingUp`/
`LookingDown`, etc.) around the new `StandUp()` call — it slots into
exactly the position their existing two `IsSitting`-related lines
currently occupy.

Implementation, in words:
- `public IBenchSeat NearBench { get; set; }` — mirrors `IsNearBench`'s
  existing public-setter shape/rationale (plan 008 Decision 4). `IsNearBench`
  itself is not touched (Decision 2).
- New private field `private IBenchSeat _seatedBench;` (Decision 3).
- `TrySit`: on success, immediately after the existing `_isSitting = true;`
  / Animator-bool-set lines, add: capture `_seatedBench = NearBench;`, and
  if non-null, `_rigidbody.position = _seatedBench.SitAnchor.position;`
  (Decision 4 — `Rigidbody2D.position`, not `transform.position`) followed
  by `_seatedBench.SetVisible(false);`. If `NearBench` is `null` (the case
  for every pre-existing EditMode test, which never sets it), this block is
  fully skipped — no position write, no visibility call, `TrySit`'s
  existing behavior is otherwise completely unchanged.
- New private helper `StandUp()` (Decision 5): clears `_isSitting`, clears
  the Animator `IsSitting` bool (identical null-check style to the
  existing code), and — if `_seatedBench` is non-null — calls its
  `SetVisible(true)` and clears `_seatedBench` to `null`.
- `StandUpIfWalking`, `Hurt()`, `Die()`: each replaces its own existing
  `_isSitting = false;` + `_animator?.SetBool(IsSittingHash, false);` pair
  with a single call to `StandUp()`, at the exact same point in its
  existing sequence — no other line in any of the three methods is
  reordered or removed.

**Acceptance criteria:**
- [ ] `TrySit(true)` with `NearBench` set (to a test double, Task 3.1)
      writes `_rigidbody.position` (and therefore `transform.position`) to
      that seat's `SitAnchor.position`, and calls its `SetVisible(false)`
      exactly once
- [ ] `TrySit(true)` with `NearBench` left at its default `null` — i.e.
      every pre-existing `TrySit` test in this codebase — leaves
      `transform.position` completely unchanged and does not throw —
      **regression check**, the concrete proof of Decision 2's zero-impact
      claim
- [ ] `StandUpIfWalking`, when it stands the character up while a seated
      bench is set, calls that seat's `SetVisible(true)` exactly once and
      clears `_seatedBench` to `null`
- [ ] `Hurt()` while seated with a seated bench set restores that seat's
      visibility and clears `_seatedBench`, in addition to its existing,
      already-tested immediate `IsSitting` clear
- [ ] `Die()` — identical coverage to `Hurt()`'s, mirroring the existing,
      already-tested `Die()` behavior
- [ ] The full pre-existing `PlayerControllerTests` suite (every test from
      plans 002 through 008, not only the bench-related subset) passes
      unmodified — **regression check**
- [ ] Live Unity check (per `CLAUDE.md`'s "Prioritize Unity's own tools"):
      confirm `Rigidbody2D.position`'s setter synchronously updates
      `transform.position` in this project's actual Unity version, both in
      EditMode (no live physics stepping) and in Play Mode — do not assume
      this from documentation alone

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] IBenchSeat interface ──┬──→ 1.2 [GAMEPLAY] Bench.cs
                                       └──→ 1.3 [GAMEPLAY] PlayerController.cs
(1.2 and 1.3 are mutually parallel — different files, both depend only on 1.1)
```

---

## Phase 2 — Bugs 1 & 2: art regeneration + scene positioning

#### Task 2.1: [ART] Diagnose and regenerate `Bench_01.png` (Bug 1)
**Depends on:** none
**Parallel:** yes — with all of Phase 1 (different files entirely)
**Touches:** `Assets/Sprites/Reference/Bench.png` (read-only source, not
modified); `Assets/Sprites/Environment/Bench/Bench_01.png` (+`.meta`) —
regenerated **in place**, same path, same GUID
**Regression risk:** unlike plan 008 Task 1.2/1.3 (which created brand-new
files), this task overwrites an already-promoted, already-referenced
asset. `SampleScene.unity`'s `SpriteRenderer` (fileID `1161090214`)
references `Bench_01.png` by GUID `a843ff31170e0424bb73233ffb27ffcf` — the
regenerated file must be written to the exact same path so the existing
`.meta`/GUID is preserved and that scene reference keeps resolving without
becoming a Missing Sprite. Do not delete-and-recreate through any path that
would mint a new GUID.

Implementation, in words:
- Per `CLAUDE.md`'s "Prioritize Unity's own tools": use Python
  (`py -3.13`, Pillow/numpy) **only** for read-only diagnosis — sample the
  actual RGB values in the currently-promoted `Bench_01.png` at (a) the
  reported contaminated region around the backrest, and (b) the sprite's
  lowest few rows (per Decision 8, checking whether the same contamination
  is inflating the bottom-edge bbox too). Record whether the leftover
  pixels are near-white (just outside the current `whiteThreshold` of 195)
  or a genuinely non-white contaminant (e.g. a mid-gray shadow blob) that
  no threshold value can remove via a border-connected flood fill (per
  `QuirrelSpriteKnockout.FloodFillBackground`'s documented algorithm — see
  Section 0).
- If the contamination is near-white: re-run the **existing, unmodified**
  `QuirrelReferenceSpriteImporter.CropResizeAndKnockout` (the standalone,
  single-subject entry point — `Bench.png` alone has no composite/isolation
  wrinkle, unlike the Quirrel_Sitting sprite) via the live Unity Editor
  (menu command, or Unity MCP `execute_code`) against the original
  `Assets/Sprites/Reference/Bench.png`, at a re-tuned `whiteThreshold`.
  Output to the scratchpad first (off-tree iteration, matching plan
  002/007/008's discipline).
- If the contamination is a genuinely non-white blob distinct from the
  legs/backrest: reuse the **existing, unmodified** region-pre-crop
  overload (`CropRegionResizeAndKnockout`, already shipped by plan 008
  Task 1.1) with a manually chosen rectangle over `Bench.png` that excludes
  the contaminant — the same "additive tool reuse, no new algorithm"
  precedent plan 008 Task 1.2 already established for the Quirrel
  composite sprite.
- Accept/reject pass mirroring plan 008 Task 1.2's own checklist: ≥400%
  zoom against a contrasting checkerboard, no halo/fringe; `ART.md` color
  spot-check (no token changes expected); re-confirm the Bench/Quirrel
  landmark scale cross-check (plan 008 Decision 5) still holds if the crop
  geometry shifted at all.
- Promote by overwriting `Assets/Sprites/Environment/Bench/Bench_01.png`
  at the exact same path, preserving the existing `.meta`'s GUID.

**Acceptance criteria:**
- [ ] Python pixel-sampling diagnosis is performed and its findings
      (near-white vs. genuinely non-white contamination, at both the
      backrest and the bottom rows) are recorded before any regeneration
      is attempted — the diagnosis is not skipped or assumed
- [ ] The actual regeneration runs through the live Unity Editor's
      existing, unmodified tool (menu command or Unity MCP `execute_code`)
      — not a Python reimplementation of the knockout algorithm, per
      `CLAUDE.md`'s explicit rule and its own cited history for this exact
      feature
- [ ] Regenerated `Bench_01.png` shows zero near-white or other
      contaminant pixels around the backrest at ≥400% zoom against a
      contrasting checkerboard
- [ ] The `.meta` file's GUID is byte-identical before and after
      (`a843ff31170e0424bb73233ffb27ffcf`) — **regression check**: confirmed
      by direct read of the `.meta` file, and confirmed live in Unity that
      `SampleScene.unity`'s `SpriteRenderer` still resolves the sprite with
      no Missing Sprite warning
- [ ] Import settings (Sprite Mode Single, PPU 100, Pivot Bottom (0.5, 0),
      Max Size 256, Filter Bilinear, sRGB on, Alpha Is Transparency on)
      unchanged — spot-checked live in the Unity Inspector
- [ ] Bench/Quirrel scale cross-check (plan 008 Decision 5) re-confirmed:
      if the regenerated crop's pixel dimensions changed at all versus the
      previously-promoted asset, the bench still reads at the same
      proportional scale relative to `Quirrel_Sitting_01` in the Scene view
      — **regression check**
- [ ] `Assets/Sprites/Reference/Bench.png` (the read-only source) is
      byte-identical before and after — **regression check**
- [ ] `QuirrelReferenceSpriteImporter.cs`/`QuirrelSpriteKnockout.cs` are
      byte-for-byte unmodified — no new algorithm code, only a re-run with
      different parameters and/or the existing region overload —
      **regression check**
- [ ] Diagnostic findings recorded on whether Bug 1's root cause also
      explains Bug 2's bottom-margin inflation (Decision 8), to inform how
      large a correction Task 2.2 is likely to need

---

#### Task 2.2: [GAMEPLAY] Bench `Transform.position.y` correction (Bug 2)
**Depends on:** Task 2.1
**Parallel:** no — edits `SampleScene.unity`'s `Bench` GameObject; not
parallel with Task 2.3 (same GameObject, same scene file — this project's
"same scene/same GameObject → never parallel" rule)
**Touches:** `Assets/Scenes/SampleScene.unity` (`Bench` GameObject's
Transform only — its `SpriteRenderer` and `BoxCollider2D` values, and every
other GameObject in the scene, are untouched)
**Regression risk:** shared scene file. Must not touch `Player`'s spawn
Transform, `Ground`'s Transform, `Main Camera`'s Transform, or `Bench`'s
own `SpriteRenderer`/`BoxCollider2D` component values (only its parent
Transform's `y` moves — the `BoxCollider2D`'s existing local offset/size
rides along with it automatically, so its footprint coverage of the sprite
is preserved without any separate edit).

Implementation, in words: live-inspect (Sprite Editor / Scene view pixel
readout) Task 2.1's regenerated `Bench_01.png` to see how much of Bug 2 it
already resolved (per Decision 8's shared-root-cause hypothesis) before
deciding on a correction size. If the bench still visibly floats in Play
Mode, measure the residual delta in world units between the sprite's
visually-perceived leg-bottom and the ground line (world y=0, matching
`Ground`'s confirmed top-surface convention) and apply that as a downward
shift to `Bench`'s `Transform.position.y`. The near/far-leg perspective
described in the bug report is inherent to the source photo's own framing
and is not something a Y-translation changes or should attempt to change —
only the sprite's vertical placement relative to the ground line is in
scope here.

Per Decision 9: this task's own live-measured value is the deliverable.
Any further small aesthetic nudge the user notices once this lands is
explicitly **not** routed back through this pipeline — it is a one-line
`Transform.position.y` edit in the Inspector, cheaper for the user to make
directly than to re-open Draft → Review → Implement for a single float.

**Acceptance criteria:**
- [ ] Live Unity measurement (Sprite Editor / Scene view pixel readout)
      records the regenerated sprite's actual lowest-opaque-pixel offset
      from its own local y=0 pivot, before deciding whether/how much to
      move the GameObject
- [ ] `Bench`'s `Transform.position.y` is corrected so the sprite's
      visually-perceived leg-bottoms read at (or effectively at) the
      ground line in Play Mode
- [ ] `Bench`'s `BoxCollider2D` local offset/size (`{0, 1.5}` / `{1.37,
      3}`) is confirmed unchanged by direct read of the scene file after
      this task — it moves rigidly with the corrected Transform, so its
      coverage of the sprite's footprint is preserved automatically —
      **regression check**
- [ ] `Player`'s spawn Transform (`{0, 0, 0}`), `Ground`'s Transform, and
      `Main Camera`'s Transform are byte-identical to their pre-task
      values — **regression check**
- [ ] Live Play Mode check: standing anywhere along the bench's footprint
      and walking through it at speed still produces no snagging (plan 008
      Task 5.2's trigger-only guarantee, re-confirmed) — **regression
      check**
- [ ] Live Play Mode check, recorded via Unity MCP or direct observation:
      the bench now visibly reads as resting on the ground

---

#### Task 2.3: [GAMEPLAY] Scene wiring — `Bench`'s `SitAnchor` child Transform
**Depends on:** Task 2.2 (same GameObject — must land after the Y
correction), Task 1.2 (`Bench.cs`'s `_sitAnchor` field must exist to
assign it), Task 1.3 (the full snap code path must be wired for this
task's own live-verification criterion below)
**Parallel:** no — edits `SampleScene.unity`'s `Bench` GameObject
**Touches:** `Assets/Scenes/SampleScene.unity` (adds one new child
GameObject under `Bench`; assigns `Bench`'s `_sitAnchor` field)
**Regression risk:** same shared-scene-file/same-GameObject risk as Task
2.2 — must not re-touch `Bench`'s own Transform, `SpriteRenderer`, or
`BoxCollider2D` values Task 2.2 just finalized, or any other existing
GameObject.

Add a child GameObject (`SitAnchor`) under `Bench`; position it, by live
iteration in Play Mode (enter Play Mode, sit, adjust the child's local
position, repeat), so Quirrel's seated sprite (`Quirrel_Sitting_01`) reads
correctly placed on the bench — back against the backrest, feet near the
ground, consistent with plan 008 Decision 5's original scale/placement
intent and the mood-board reference. Assign this child Transform to
`Bench`'s `_sitAnchor` serialized field.

**Acceptance criteria:**
- [ ] `SitAnchor` child GameObject exists under `Bench`; its position is
      visually verified live in Play Mode (Quirrel's seated sprite reads
      centered/plausible on the bench, not obviously offset)
- [ ] `Bench`'s `_sitAnchor` field references this new child Transform
      (confirmed non-null — not silently left falling back to `Bench`'s
      own transform)
- [ ] `Bench`'s own Transform (Task 2.2's corrected value), `SpriteRenderer`,
      and `BoxCollider2D` values are byte-identical to their post-Task-2.2
      state — **regression check**
- [ ] `Player`'s spawn Transform, `Ground`'s Transform, and `Main Camera`'s
      Transform are unchanged — **regression check**
- [ ] Live Play Mode check: sitting from either edge of the bench's
      footprint (not centered) produces the identical final seated
      position both times — the direct, concrete test of the corrected
      requirement that supersedes plan 008's old "no centering required"
      acceptance criteria

---

**Phase 2 dependency graph:**
```
2.1 [ART] Bench_01 regen ──→ 2.2 [GAMEPLAY] Y-position fix ──┐
                                                               ├──→ 2.3 [GAMEPLAY] SitAnchor wiring
1.2 [GAMEPLAY] Bench.cs (_sitAnchor field) ───────────────────┤
1.3 [GAMEPLAY] PlayerController.cs (full snap path) ──────────┘
```

---

## Phase 3 — QA

#### Task 3.1: [QA] `PlayerControllerTests` coverage for seated-bench snap/hide/restore
**Depends on:** Task 1.1, Task 1.3
**Parallel:** yes — with Task 3.2 (different file/assembly)
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`
**Regression risk:** additive only — no pre-existing test is modified.

**Work:** add a small, private, test-only fake class implementing
`IBenchSeat` (e.g. `FakeBenchSeat` — a settable `SitAnchor` Transform plus
a `SetVisible` call-count/last-value so tests can assert on it), then:
- `TrySit_WithNearBenchSet_SnapsPositionToSitAnchor`
- `TrySit_WithNearBenchSet_HidesTheBench`
- `TrySit_WithNearBenchNull_LeavesPositionUnchanged` (the explicit
  regression pin for every pre-existing `TrySit` test's implicit
  assumption — proves Decision 2's zero-impact claim directly, not just by
  inference from other tests still passing)
- `StandUpIfWalking_WithSeatedBenchSet_ShowsTheBenchAgainAndClearsSeatedBench`
- `Hurt_WhileSittingWithSeatedBenchSet_ShowsTheBenchAgain`
- `Die_WhileSittingWithSeatedBenchSet_ShowsTheBenchAgain`

**Acceptance criteria:**
- [ ] All new tests pass
- [ ] No test calls `Input.GetKey`/`GetKeyDown` directly, consistent with
      this file's existing convention
- [ ] The full pre-existing `PlayerControllerTests` suite (every test from
      plans 002 through 008) passes unmodified — **regression check**
- [ ] `AnimatorContractTests.cs`'s full suite is re-run and confirmed still
      green, byte-identical to its pre-this-plan state — **regression
      check**, confirming this plan's own "no Animator change needed"
      blast-radius claim rather than merely assuming it
- [ ] `CameraFollowTests.cs`'s full suite is re-run and confirmed still
      green, byte-identical to its pre-this-plan state — **regression
      check**, confirming this plan's own "`CameraFollow` needs no change"
      blast-radius claim (Section 0) rather than merely assuming it
- [ ] `PlayerControllerPlayModeTests.cs`'s full suite is re-run and
      confirmed still green — **regression check**: this is the only
      suite that exercises `PlayerController` under real, stepped Unity
      physics, making it the concrete regression surface for Decision 4's
      new `Rigidbody2D.position` write inside `TrySit`
- [ ] Live Unity check (consolidating Task 1.3's own live-verification
      criterion here, per this project's existing precedent of gathering a
      phase's live-Unity checks at its QA task, e.g. plan 008 Task 6.4):
      re-confirm `Rigidbody2D.position`'s setter synchronously updates
      `transform.position`, both in EditMode and in Play Mode

---

#### Task 3.2: [QA] `BenchTests` coverage for `SitAnchor`/`SetVisible`/`NearBench` wiring
**Depends on:** Task 1.2
**Parallel:** yes — with Task 3.1 (different assembly)
**Touches:** `Assets/Scripts/Environment/Tests/EditMode/BenchTests.cs`
**Regression risk:** additive only.

**Work:**
- `SitAnchor_WhenAssigned_ReturnsTheAssignedTransform`
- `SitAnchor_WhenUnassigned_FallsBackToOwnTransform`
- `SetVisible_False_DisablesSpriteRenderer`
- `SetVisible_True_EnablesSpriteRenderer`
- `HandleTriggerEnter_WithPlayerController_SetsNearBenchReference`
- `HandleTriggerExit_WithPlayerController_ClearsNearBenchReference_WhenItWasThisBench`
- `HandleTriggerExit_DoesNotClearNearBenchReference_WhenItPointsAtADifferentBench`
  (Decision 7's equality-guard case)

**Acceptance criteria:**
- [ ] All new tests pass
- [ ] The 4 pre-existing `BenchTests` (enter/exit sets/clears `IsNearBench`
      bool; both no-throw-without-`PlayerController` cases) pass unmodified
      — **regression check**

---

#### Task 3.3: [QA] Manual playtest protocol addendum — new `5e` section, supersede `5d.2`
**Depends on:** Task 2.1, Task 2.2, Task 2.3, Task 3.1, Task 3.2 (needs the
whole fix working end-to-end, plus green automated suites, before a live
pass is meaningful — mirrors plan 008 Task 6.4's own dependency shape)
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md` (new `5e`
section; rewrites row `5d.2`'s expectation to point at `5e.3` instead of
describing the fix inline; appends a one-line "Resolved" note to each of
the three "Bugs Found" entries)
**Regression risk:** none to code — documentation only. Per `CLAUDE.md`'s
"Prioritize Unity's own tools" section, real sprite/trigger/visibility
runtime behavior (a visual read of "no white halo," "reads as resting on
the ground," "reads as one coherent bench, not two fragments") is exactly
the class of thing the automated suite cannot verify by itself — a live
pass is the only real proof, same discipline as plan 008 Task 6.4.

New `5e` rows to add, following the `5a`–`5d` format exactly:
- **5e.1** — Bench sprite shows no residual white/halo pixels around the
  backrest (Bug 1), verified at ≥400% zoom in a live Play Mode screenshot
  or the Sprite Editor.
- **5e.2** — Bench reads as resting on the ground, not floating (Bug 2),
  live Play Mode visual check.
- **5e.3** — Sitting from either edge of the bench's footprint snaps
  Quirrel to the identical sit-anchor position both times (Bug 3) — this
  row explicitly supersedes `5d.2`.
- **5e.4** — `Bench_01` is hidden the instant Quirrel sits, and visible
  again the instant he stands, in both stand-up directions (walking away,
  and interrupted by `Hurt()`/`Die()`).
- **5e.5** — The bench reappears empty, in the correct place, after
  standing — visual coherence check: one bench, not two disconnected
  fragments (the literal Bug 3 complaint, confirmed fixed).
- **5e.6** — `Hurt()`/`Die()` while seated also correctly restore the
  bench's visibility, not only `StandUpIfWalking`'s path — regression-
  adjacent to `5d.8`/`5d.9`.
- **5e.7** — The camera glides smoothly (no jarring cut) when the position
  snap happens on sit — regression check confirming `CameraFollow`'s
  existing `SmoothDamp` behavior (untouched by this plan) reads smoothly
  against the new instantaneous position snap.
- **5e.8** — Regression re-check of `5d.1`, `5d.3`–`5d.11` (all rows except
  the now-superseded `5d.2`) — still read correctly.

**Acceptance criteria:**
- [ ] New `5e` section added with all rows above, following the `5a`–`5d`
      format exactly
- [ ] `5d.2`'s row is rewritten to point at `5e.3` rather than describing
      the fix inline — no duplicate/conflicting description across the two
      rows
- [ ] The "Bugs Found" section's three entries each receive a one-line
      "Resolved — see `Docs/Plans/009_bench-visual-fixes.md`" addendum, not
      a deletion — preserves the historical record, matching this doc's
      own established discipline of not silently erasing prior findings
- [ ] Live verification performed and recorded PASS/FAIL for every new
      `5e` row
- [ ] Any FAIL is logged as a new bug per this doc's existing bug-report
      format and routed back through the pipeline, not silently patched
      inside this task
- [ ] `5a`–`5c` sections re-confirmed to still read correctly —
      **regression check**
- [ ] Explicit negative-confirmation criterion: `ART.md` is confirmed to
      need zero changes for this plan (no new reserved color, no new
      Animation Timing row — no Animator topology changed) — not silently
      skipped

---

**Phase 3 dependency graph:**
```
1.1, 1.3 ──→ 3.1 [QA] PlayerController tests ──┐
1.2 ──────→ 3.2 [QA] Bench tests ───────────────┼──→ 3.3 [QA] manual playtest + doc sync
2.1, 2.2, 2.3 ───────────────────────────────────┘
(3.1/3.2 are mutually parallel — different files/assemblies)
```

---

## Explicitly out of scope for this plan

Any change to `Assets/Animations/Quirrel.controller` (no Animator topology
change — confirmed during blast-radius mapping); any change to
`CameraFollow.cs` (its existing glide already covers the new position
snap); any change to `IsNearBench`'s existing bool shape/semantics
(Decision 2); a multi-bench save-point registry or any persistence of
bench state (plan 008 Decision 7, still deferred); any
`ProjectSettings`/tags/physics-layers/collision-matrix change; any
Health/HP/save system; re-cropping `Quirrel_Sitting_01.png` to remove its
baked-in bench geometry (explicitly rejected per the user's own corrected
spec); a generalized multi-seat/`ISeatable` interface hierarchy beyond the
2-member `IBenchSeat` this plan needs (YAGNI); any Input System migration
(still legacy `KeyCode`/`Input.GetKey`); any `ART.md` change (confirmed
none needed); fully solving the multi-bench trigger-overlap edge case
named in plan 008 Decision 7 (Task 1.2's reference-equality guard only
prevents this plan's new property from being *more* fragile than the bool
it sits alongside — it does not implement reference counting).

---

## Judgment calls made explicit

1. `IBenchSeat`, a 2-member interface in `Quirrel.Player`, resolves the
   circular-asmdef problem the bug report's own suggested fix (a direct
   `Bench` reference) would have caused — found during this plan's own
   blast-radius mapping, not assumed (Decision 1).
2. `IsNearBench` (bool) stays completely untouched; `NearBench` is a new,
   independently-set paired property — maximizes backward compatibility
   with plan 008's entire existing test suite at the cost of a small,
   accepted duplication (Decision 2).
3. `_seatedBench` is latched once at sit-time, not re-read from the live
   `NearBench` property at stand-up time — mirrors the existing
   `_isAirAttack` precedent (Decision 3).
4. The position snap uses `Rigidbody2D.position`, matching this file's
   existing exclusive-use-of-`_rigidbody`-writes discipline, and is
   verified live in Unity rather than assumed from documentation (Decision
   4).
5. A shared `StandUp()` helper replaces three duplicated inline resets,
   mirroring plan 008 Decision 8's own precedent (Decision 5).
6. `Bench.SitAnchor` falls back to the Bench's own transform rather than
   requiring an assigned anchor — graceful degradation for a future second
   bench (Decision 6).
7. `HandleTriggerExit`'s new `NearBench` clear is reference-equality
   guarded — a strictly safer version of plan 008's already-named,
   still-accepted multi-bench limitation, without attempting to fully
   solve it here (Decision 7).
8. Bug 1 and Bug 2 are diagnosed together (shared suspected root cause)
   but fixed as two sequenced tasks, since Bug 2's exact correction can
   only be measured against Bug 1's regenerated asset (Decision 8).
9. Bug 2's Y-position task is kept small and dedicated only for structural
   sequencing reasons (must follow Bug 1's asset; shares a GameObject with
   Bug 3's scene wiring) — any further micro-nudge after it lands is
   explicitly handed to the user directly, per `CLAUDE.md`'s
   "Delegate to the human when that's cheaper" (Decision 9).

---

## Reference file paths consulted while drafting this plan

- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\CLAUDE.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\ART.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\008_bench-sit-mechanic.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\002_manual-playtest-protocol.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\PlayerController.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Environment\Bench.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Tests\EditMode\PlayerControllerTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Environment\Tests\EditMode\BenchTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\CameraFollow.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\Tests\EditMode\CameraFollowTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Tests\PlayMode\PlayerControllerPlayModeTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Quirrel.Player.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Environment\Quirrel.Environment.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Editor\Tools\QuirrelReferenceSpriteImporter.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Editor\Tools\QuirrelSpriteKnockout.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scenes\SampleScene.unity` (`Bench` GameObject block, fileID `1161090211`, confirmed by direct read)
