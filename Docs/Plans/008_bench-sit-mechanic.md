# Implementation Plan — Bench Sit Mechanic

**Status:** ✅ APPROVED (round 2, accepted by implementation-plan-reviewer)
**Author:** implementation-plan-architect
**Date:** 2026-08-05
**Feature:** A single, static `Bench` prop the player can sit on. Standing in
front of it (any horizontal/X-axis overlap between Quirrel's footprint and
the bench's footprint — no centering required) and pressing `W` (the same
key that already drives the cosmetic look-up pose) sits Quirrel down. While
seated, Attack (`J`)/Jump (`Space`)/Defend (`K`) are fully blocked at the
code level — a deliberate "rest state," mirroring a Hollow Knight bench.
Walking (`A`/`D`) is the only stated exit — pressing either stands Quirrel
up and lets him walk immediately. The game starts with Quirrel already
seated (bench placed at the spawn point). **Health restoration on sitting is
explicitly out of scope** — no Health/HP field exists anywhere in this
codebase yet (confirmed by direct read of `PlayerController.cs`), and this
plan adds no speculative hook, interface, or event for a system that doesn't
exist. A later plan wires that up once a health system is designed.

---

## 0. Summary and verified blast radius

**Confirmed from the actual source (read directly, not assumed):**

- `Assets/Scripts/Player/PlayerController.cs` (807 lines as of this plan,
  confirmed by direct read — grown since plan 007's 741-line snapshot).
  `Update()`'s current call order (verified, lines 304–359): full-commit
  gate → A/D read into `_horizontalInput` → `TryAttack` (J edge) →
  `DefendHeld` (K held) → `TryJump` (Space edge) → `UpdateLookState` (W/S
  held) → `UpdateFacing()` → `UpdateAnimatorParameters()`. This plan inserts
  **new logic at three points**: (1) immediately after the A/D block, a
  same-frame "stand up if walking away while seated" check; (2) a new
  `TrySit` call on the `W` `GetKeyDown` edge, placed **before**
  `UpdateLookState`'s call (Decision 1 — routing); (3) a one-shot
  spawn-time auto-sit check at the very top of `Update()`.
- `IsFullyCommitted => (_isAttacking && IsGrounded) || DefendHeld ||
  _isHurtStunned` (line 132) **must not be touched** — folding sitting into
  it would also gate the existing top-of-`Update()` A/D read
  (`if (!IsDead && !IsFullyCommitted) { ... }`, lines 313–330), silently
  making it impossible to ever detect "the player wants to walk away," the
  one stated exit condition. Confirmed by direct read: this is a real,
  non-hypothetical landmine, not a theoretical one — see Decision 2.
- `TryAttack` (line 486, guard at line 490:
  `if (IsDead || DefendHeld || _isAttacking || _isHurtStunned) return
  false;`), `TryJump` (line 422, guard at line 426: `if (!isGrounded ||
  _jumpInProgress || IsDead || IsFullyCommitted) return false;`),
  `DefendHeld`'s per-frame computation (line 348: `DefendHeld = !IsDead &&
  !_isHurtStunned && IsGrounded && Input.GetKey(KeyCode.K);`), and
  `UpdateLookState`'s `isIdleAndGrounded` gate (line 759: `IsGrounded &&
  !IsDead && !IsFullyCommitted && _horizontalInput == 0f`) each need an
  individual, explicit `&& !_isSitting`-style addition (Decision 2) — none
  of these currently reference sitting because sitting doesn't exist yet.
- `ApplyHorizontalMovement` (line 397), its own `locked = IsDead ||
  IsFullyCommitted` gate, and `CameraFollow.Tick`'s `panBlocked =
  _playerController.IsDead || _playerController.IsFullyCommitted` (confirmed
  by direct read of `CameraFollow.cs` line 100) are **left completely
  untouched** — A/D keeps moving the character normally while seated (no
  freeze), and the camera can still pan on W/S while seated. This is an
  explicit, named judgment call (Decision 2), not an oversight.
- `Hurt()` (line 577) and `Die()` (line 624) already have an established,
  load-bearing precedent (plan 007) for force-clearing derived-input state
  the moment an interrupt is *accepted*: both explicitly set `DefendHeld =
  false`, `LookingUp = false`, `LookingDown = false` and their mirrored
  Animator bools, even though those are otherwise continuous/per-frame
  reads. This plan's `_isSitting` must follow the same pattern for the same
  reason — an externally-triggered `Hurt()`/`Die()` (e.g., from a future
  combat system) must not leave a seated character stuck mid-interrupt-
  transition — see Decision 6.
- `Assets/Animations/Quirrel.controller`: 12 parameters, 12 states, per
  `AnimatorContractTests.cs`'s current `ExpectedParameters` (12 entries) and
  plan 007's own confirmed topology (`Idle` has 5 outgoing transitions:
  Walk/JumpAnticipation/DefendRaise/LookUp/LookDown, in that exact order).
  This plan **appends** a 13th parameter (`IsSitting`, Bool) and a new
  `Sitting` state, and appends `Idle`'s 6th outgoing transition
  (`IsSitting==true`→`Sitting`) **after** the existing 5 — none of the
  existing 5 are reordered, retimed, or reconditioned. The 3 existing
  Any-State transitions (`DieTrigger`/`HurtTrigger`/`AttackTrigger`) apply to
  the new `Sitting` state automatically, zero additional wiring, same
  free-inheritance finding plan 007 already documented.
- **`Sitting` needs a much simpler transition topology than `LookUp`/
  `LookDown` did** (the design question the user flagged explicitly). Because
  Attack/Jump/Defend are blocked at the **code** level while `_isSitting` is
  true (`AttackTrigger`/`JumpTrigger` never fire, `DefendHeld` bool never
  goes true), `Sitting` structurally can never need an escape transition
  toward `Attack`/`JumpAnticipation`/`DefendRaise` the way `LookUp`/`LookDown`
  needed mirrored pass-throughs (those cosmetic states did **not**
  code-block the underlying actions, only visually overlaid them — see plan
  007 Decision 1/topology). `Sitting` needs exactly **one** outgoing
  transition of its own: `IsWalking==true`→`Walk` (the "stand up by walking"
  exit), timed identically to `Idle`'s own `[1]` `Walk` transition
  (`TransitionDuration 0.1`, `ExitTime 0.75`, `HasExitTime 0`) for a
  consistent locomotion-start feel.
- `AnimatorContractTests.cs` (360 lines): `ExpectedParameters` is a
  12-entry array; `Controller_HasExactly10Parameters...`'s length assertion
  (its name is now stale — it already asserts 12, not 10, per plan 007's
  own note that the assertion self-adjusts without a hardcoded number) will
  self-adjust to 13 with no further change needed. The established
  `AssertTransition`/`FindStateByName` helper style (used for `Attack`'s and
  `Idle`'s outgoing-transition contract tests) is this plan's QA template
  for `Idle`'s 6th transition and `Sitting`'s 1 transition.
- `PlayerControllerTests.cs` (881 lines): existing reflection-helper
  precedent (`ForceSetDefendHeld`, `ForceSetIsGrounded`,
  `ForceSetHorizontalInput`) is reused as-is. This plan's new `IsNearBench`
  property is designed with a **public setter** (mirroring `IsDead`'s exact
  shape — `public bool IsDead { get; set; }` — rather than `IsGrounded`'s
  private-set-plus-reflection shape) since `IsNearBench` is legitimately,
  normally externally driven by the new `Bench` component in real gameplay,
  not merely test-only — see Decision 4. No new reflection helper is needed
  for it.
- `Assets/Scenes/SampleScene.unity` (confirmed by direct read): `Player`
  spawns at `{x: 0, y: 0, z: 0}` (fileID 563516963), `BoxCollider2D` offset
  `{x: 0, y: 0.655}` / size `{x: 1, y: 1.31}` (so the collider's bottom edge
  sits exactly at world y=0 — feet-on-ground). `Ground` sits at `{x: 0, y:
  -0.5, z: 0}` with `localScale {x: 30, y: 1}` (top surface at world y=0,
  confirming the shared ground line). `Player` is on layer 8
  (`m_TagString: Untagged` — **no `Player` tag exists**), `Ground` is on
  layer 9. `CameraFollow`'s `_panDistanceFallback: 5` is the only other
  serialized value of note, untouched by this plan.
- No `OnTrigger2D` convention exists anywhere in this codebase yet — the
  only existing physics-query precedent is `PlayerController.CheckGrounded()`
  (`Physics2D.OverlapCircle` against a named layer, computed in
  `FixedUpdate`). This plan's bench-proximity detection deliberately does
  **not** copy that pattern (which would require a new physics layer/layer
  mask, a global regression vector per this project's own conventions) —
  see Decision 3 for the chosen alternative (a `Bench`-owned trigger
  collider using `GetComponent<PlayerController>()`, zero new layers, zero
  `ProjectSettings`/collision-matrix changes).
- `Assets/Sprites/Reference/Bench.png` and `Quirrel_On_Bench.png` (both
  896×1174px, confirmed by direct image read): the same wrought-iron bench
  photo, the second composited with Quirrel sitting on it (legs bent, feet
  near the ground, back against the backrest, pin resting across his lap) —
  visually consistent with the mood-board reference
  `Docs/UserRequirements/Quirrel_resting_on_bench.png`. Both images share
  what appears to be the identical camera framing/photo — this plan's scale-
  derivation method (Decision 5) is built on that assumption, with an
  explicit cross-check acceptance criterion rather than taking it on faith.
- `Assets/Editor/Tools/QuirrelReferenceSpriteImporter.cs` (386 lines,
  confirmed read in full, unmodified since plan 007): its pure-logic
  pipeline is already decomposed into small, independently-callable public
  functions — critically, `CropPixels(pixels, width, height, xMin, yMin,
  xMax, yMax, out croppedWidth, out croppedHeight)` **already takes an
  explicit, caller-supplied rectangle** (it's the existing bbox-autodetect
  step's own cropping primitive, already exposed publicly) and
  `BuildOutputPixels` already does bbox-detect→crop→resize→knockout on any
  raw buffer it's given. **This means the "manual rectangle pre-crop" the
  user's own note anticipated needing does not require any new cropping
  algorithm at all** — only a new, tiny overload that calls the existing
  `CropPixels` once (with a human-supplied region isolating Quirrel from the
  bench in the composite) and then delegates to the existing, unmodified
  `BuildOutputPixels` on that sub-buffer. See Decision 5/Task 1.1 — this is
  the smallest possible glue addition, not a new algorithm.
- `QuirrelSpriteKnockout.KnockoutAndFeather`'s only removal mechanism is a
  **border-connected flood fill of near-white pixels** (confirmed by
  reading `FloodFillBackground`) — it has no shape/connectivity awareness of
  "character vs. prop." Any non-near-white pixel inside the crop (including
  a stray bit of dark bench ironwork) remains fully opaque in the final
  output. This is exactly why a *loose* manual pre-crop (including margin
  bench pixels) is unsafe and why Task 1.2's accept/reject pass needs a new,
  explicit "no residual bench-fragment contamination" check beyond the
  existing halo/fringe check.
- `Assets/Sprites/Quirrel/` currently has 9 per-state folders (`Attack`,
  `Defend`, `Die`, `Hurt`, `Idle`, `Jump`, `LookDown`, `LookUp`, `Walk`) —
  confirmed by directory listing. This plan adds a 10th,
  `Assets/Sprites/Quirrel/Sitting/`, matching the established
  one-folder-per-state convention exactly. `Assets/Sprites/` has no existing
  environment/prop folder — this plan creates
  `Assets/Sprites/Environment/Bench/` as a new sibling category (room for
  future props — doors, other benches — without restructuring).
- `Assets/Scripts/` currently has exactly two per-system folders, each with
  its own asmdef: `Player/` (`Quirrel.Player`, no references) and `Camera/`
  (`Quirrel.Camera`, references `Quirrel.Player`), each with an
  `EditMode`/`PlayMode` `Tests/` subfolder with its own test asmdef
  referencing `UnityEngine.TestRunner`/`UnityEditor.TestRunner` plus
  `nunit.framework.dll`. This plan adds a **third** such pair:
  `Assets/Scripts/Environment/` (`Quirrel.Environment`, references
  `Quirrel.Player`) for the new `Bench` component, with its own
  `Tests/EditMode/` + `Quirrel.Environment.EditModeTests` asmdef, mirroring
  the existing pattern exactly rather than bolting `Bench` onto the
  `Quirrel.Player` assembly.
- `ART.md` §2.4 already reserves `#F6E7C7` for "Save bench / safe-point
  glow" and §2.5/§3's contrast hierarchy already lists benches under
  "interactive foreground objects." **This plan adds no glow VFX** (no
  `Light2D`/URP available per §7; any future glow would need sprite-gradient
  faking, explicitly out of scope here) — the reserved color stays reserved,
  unused, for a later task. §7's Animation Timing table gets exactly one new
  row (`Sitting`), mirroring the `Look Up/Down` row's format.

**Complete write list for this plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Editor/Tools/QuirrelReferenceSpriteImporter.cs` | Task 1.1 | New overload: region-based pre-crop + existing pipeline |
| `Assets/Editor/Tools/QuirrelReferenceSpriteImporterSelfTest.cs` | Task 1.1 | New self-test case for the region overload |
| `Assets/Sprites/Environment/Bench/Bench_01.png` (+ `.meta`) | Task 1.2, 1.3 | New build-ready prop sprite |
| `Assets/Sprites/Quirrel/Sitting/Quirrel_Sitting_01.png` (+ `.meta`) | Task 1.2, 1.3 | New build-ready character sprite |
| `Assets/Animations/Clips/Quirrel_Sitting.anim` (new) | Task 2.1 | New AnimationClip |
| `Assets/Animations/Quirrel.controller` | Task 3.1, 3.2 | +1 parameter, +1 state, +2 transitions, then Motion assignment |
| `Assets/Scripts/Player/PlayerController.cs` | Task 4.1, 4.2 | +2 properties, +1 hash constant, new methods, guards, call sites, Hurt/Die clears |
| `Assets/Scripts/Environment/Quirrel.Environment.asmdef` (new) | Task 5.1 | New assembly |
| `Assets/Scripts/Environment/Bench.cs` (new) | Task 5.1 | New component |
| `Assets/Scenes/SampleScene.unity` | Task 5.2 | +1 GameObject (Bench) |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 6.1 | New tests |
| `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs` | Task 6.2 | +1 parameter row, +2 transition-order tests |
| `Assets/Scripts/Environment/Tests/EditMode/Quirrel.Environment.EditModeTests.asmdef` (new) | Task 6.3 | New test assembly |
| `Assets/Scripts/Environment/Tests/EditMode/BenchTests.cs` (new) | Task 6.3 | New tests |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 6.4 | New `5d` section |
| `ART.md` §7 | Task 6.4 | +1 row |

**Confirmed out of scope:** any Health/HP field, save state, or health-
restore hook of any kind (no such system exists anywhere in this codebase —
confirmed by direct read; a future plan adds this once health is designed);
any glow/VFX on the bench; any save/ScriptableObject schema; any tags,
physics layers, or collision-matrix change (Bench's collider is a trigger,
needs none); any Input Actions migration (still legacy `KeyCode`/
`Input.GetKey`); any multi-bench save-point network (Decision 7 — one bench,
not over-engineered, but not hard-coded to make a second one impossible
either); camera gating changes (Decision 2 — sitting doesn't join
`IsFullyCommitted`, so `CameraFollow`'s pan gate is untouched by design).

**Metroidvania-specific check:** no new traversal ability is granted (sitting
doesn't move the `Transform` anywhere, grants no new reach, no jump/dash/
etc.); no new reachable area; no save/persisted-unlock state exists yet to
worry about (no save system exists in this codebase — confirmed); no
sequence-break risk (a bench that only sits/stands the character in place is
not a gating mechanism). Checkpoint integrity and backtracking concerns are
not yet applicable — there is exactly one bench, positioned at the only
spawn point, with no persisted state. This plan is outside the ability-
gating/progression regression category.

---

## 1. Design decisions

**1. `W`-handling routing: `TrySit` (edge-triggered) is checked before
`UpdateLookState` (continuous), so a same-frame "start sitting" always wins
over "start looking up," never both.** `TrySit` is called on
`Input.GetKeyDown(KeyCode.W)` — an edge — and, on success, immediately sets
`_isSitting = true`. `UpdateLookState` is called immediately after (same as
today), continuously reading `Input.GetKey(KeyCode.W)`, but its
`isIdleAndGrounded` gate now includes `&& !_isSitting` (Decision 2). Because
`TrySit`'s call site precedes `UpdateLookState`'s in `Update()`'s existing
order, the moment sitting starts, that **same frame's** `UpdateLookState`
call already sees `_isSitting == true` and evaluates `LookingUp` false —
this is not merely defensive, it prevents a real, concrete bug: without the
`!_isSitting` guard, `LookingUp` would independently evaluate `true` that
same frame (every other condition in `isIdleAndGrounded` — grounded, not
dead, not fully committed, zero horizontal input — is still satisfied while
seated), and because `Idle`'s transition list evaluates `LookingUp==true`
(priority `[4]`) **before** the new `IsSitting==true` (priority `[6]`),
Mecanim would take the first satisfied transition in list order and send the
character into `LookUp` instead of `Sitting` — a real, silent, order-
dependent bug, not a hypothetical one. Rejected alternative: reorder
`Idle`'s transitions so `IsSitting` comes first — rejected because it would
require touching (and re-testing) the existing, contract-pinned
`Walk`/`JumpAnticipation`/`DefendRaise`/`LookUp`/`LookDown` order for no
benefit, when a code-side mutual-exclusion guard achieves the same
correctness more cheaply and matches this file's existing "code gate does
the real work, Animator side stays simple" convention (the same convention
plan 007 already established for `DefendHeld`'s single-condition
transition).

**2. Sitting is deliberately NOT folded into `IsFullyCommitted`.** Doing so
would also gate `Update()`'s top-of-method A/D read (`if (!IsDead &&
!IsFullyCommitted) { ... }`), silently making it impossible to ever detect
"the player wants to walk away" — the one stated exit condition — a real
landmine, not a hypothetical one (confirmed by direct read of the current
gate). Instead, four call sites get an individual, explicit `&&
!_isSitting` addition: `TryAttack`'s guard, `TryJump`'s guard,
`DefendHeld`'s per-frame computation, and `UpdateLookState`'s
`isIdleAndGrounded` gate. `ApplyHorizontalMovement`'s own gate, `Update()`'s
A/D read, and `CameraFollow`'s pan-blocking condition are left **completely
untouched** — A/D keeps working to stand up (by design), and the camera can
still pan on W/S while seated. This last point is an explicit judgment
call, not a hard requirement the brief stated either way: sitting freezes
Attack/Jump/Defend (an explicit ask) but does not freeze the camera's own
independent W/S read (an unrelated system, per plan 007 Decision 3 — the
two were already decoupled before this plan existed). If this reads as
wrong in the manual playtest pass (Task 6.4), it is a one-line fix
(`CameraFollow`'s gate would need a way to read `IsSitting`, e.g. via a new
public property) — not implemented here since it isn't a named requirement.

**3. Bench proximity detection: a `Bench`-owned trigger collider using
`GetComponent<PlayerController>()`, not a new physics layer.** The only
existing physics-query precedent in this codebase
(`PlayerController.CheckGrounded()`) uses `Physics2D.OverlapCircle` against
a named layer mask, which would require adding a new physics layer for
`Bench` — a global, silent, project-wide `ProjectSettings`/collision-matrix
change per this project's own regression-vector list, for a check that
doesn't need one. Instead: `Bench` owns a single `BoxCollider2D`
(`isTrigger = true` — **never a solid collider**, so Quirrel walks freely
across/through its footprint, satisfying "the Bench must not be a physical
obstacle") sized to the bench sprite's rendered width and a generous height
(3 units — comfortably exceeding the Player's own 1.31-unit collider
height, so Y is never the limiting factor and ordinary 2D trigger overlap
behaves equivalently to "any X-axis overlap" for every realistic
ground-level player position, satisfying the "even 1 pixel counts, no
centering" spec without a bespoke X-only overlap query). `Bench`'s
`OnTriggerEnter2D`/`OnTriggerExit2D` do `other.GetComponent<PlayerController>
()` and, if non-null, set `PlayerController.IsNearBench` — no `Player` tag
exists in this scene (confirmed — `m_TagString: Untagged`) and none is
added by this plan; component-presence detection works regardless of
layer/tag and needs zero `ProjectSettings` changes. Rejected alternative:
`PlayerController`-owned `OverlapBox`-against-a-layer, mirroring
`CheckGrounded` exactly — rejected for the new-layer regression cost named
above, for a feature (proximity to one static prop) that doesn't need
`CheckGrounded`'s per-`FixedUpdate` polling cadence.

**4. `IsNearBench` is `{ get; set; }` (public setter), not `{ get; private
set; }` plus a reflection helper.** Unlike `IsGrounded` (only ever
legitimately set by this class's own `CheckGrounded()`), `IsNearBench` is
**legitimately, normally** driven by an external component (`Bench`) during
real gameplay — the exact same shape as the existing `IsDead` property
(`public bool IsDead { get; set; }`, chosen for the identical reason per its
own doc comment). Mirroring `IsDead`'s precedent exactly means `Bench` calls
`playerController.IsNearBench = true;` directly (no new method needed), and
EditMode tests set it directly too, with zero new reflection helper
required — simpler than the user's own suggested `ForceSetIsNearBench`
convention, and more consistent with this file's own two existing property
shapes (there are exactly two: `{ get; private set; }` for values only this
class computes, `{ get; set; }` for values a legitimate external caller
must drive — `IsNearBench` is unambiguously the second kind).

**5. Scale derivation measures both quantities from the SAME composite
image, with a cross-check against the standalone `Bench.png` before
trusting it.** Per the wrinkle already identified: `Bench.png` alone has no
absolute scale reference. `Quirrel_On_Bench.png` shows both subjects at
their true relative scale. The method (Task 1.2): (a) using an image
viewer/Sprite-Editor pixel readout, record the topmost and bottommost bench
pixel rows *within the composite* (top of the backrest's finial; bottom of
a front leg's foot — both visible/unoccluded by Quirrel in the reference
image, confirmed by direct visual inspection) → `benchHeightInComposite`;
(b) record Quirrel's own topmost (hat) and bottommost (feet) pixel rows in
the same composite → `quirrelSeatedHeightInComposite`; (c) `ratio =
benchHeightInComposite / quirrelSeatedHeightInComposite`; (d) choose
`targetSittingHeightPx` the same way plan 007 chose its own target heights
— verified visually against the existing `Idle` roster (~131px baseline,
natural per-pose variance accepted); (e) `targetBenchHeightPx = round(ratio
* targetSittingHeightPx)`. **Cross-check, new acceptance criterion**: before
trusting this, confirm 2–3 unoccluded bench landmark pixel coordinates (top-
right backrest corner, bottom-right leg foot) land within a few px of the
same coordinates in the standalone `Bench.png` — if they diverge
meaningfully, the "same photo/framing" assumption this method depends on is
wrong and must be re-investigated (e.g., re-measuring bench height directly
in the composite alone, using only its own unoccluded top/bottom rows)
before promoting anything. This keeps the derivation auditable rather than
a one-shot eyeball guess.

**6. `Hurt()`/`Die()` explicitly force-clear `IsSitting` (both the C# flag
and the Animator bool), exactly mirroring the existing `DefendHeld`/
`LookingUp`/`LookingDown` precedent.** Even though no combat system calls
`Hurt()`/`Die()` yet, both are already real, callable public APIs with an
established "force-clear every derived-input flag at the moment the
interrupt is accepted" contract (plan 007) — extending that existing
contract to also cover `IsSitting` is maintaining an existing guarantee,
not adding a speculative health-system hook. Without this, a future call to
`Hurt()`/`Die()` while seated would leave `_isSitting` stale-true (blocking
Attack/Jump forever afterward, per Decision 2's guards) even though the
Animator has already moved to `Hurt`/`Die` via their Any-State transitions
— the identical stuck-flag bug class plan 002/004 already found and fixed
for `_jumpInProgress`/`_isAttacking`.

**7. One `Bench` component, extensible, not multi-bench-proofed.** A single
`Bench` MonoBehaviour + GameObject is sufficient for the one bench this plan
needs (matching the one spawn point) — no save-point registry, no
per-bench IDs, no persistence. It is not, however, hard-coded in a way that
would make a *second* bench instance break things later: `Bench` detects the
player via `GetComponent<PlayerController>()` on whatever collider enters
its own trigger (no serialized single-Player reference), so a second `Bench`
GameObject could be dropped into the scene later with zero code changes.
**Named, accepted limitation**: if two benches' proximity zones ever
overlap simultaneously and the player exits only one, that bench's
`OnTriggerExit2D` would incorrectly clear `IsNearBench` to `false` even
while still inside the other bench's zone (a plain bool, not a reference-
counted overlap tracker). Not fixed here — flagged for whoever adds a
second bench, per this plan's explicitly single-bench scope.

**8. `TrySit`/the "stand up" check reuse a single, shared
`IsIdleAndGrounded()` gate, rather than duplicating the same four-condition
expression in two places.** `UpdateLookState`'s `isIdleAndGrounded`
computation (`IsGrounded && !IsDead && !IsFullyCommitted && _horizontalInput
== 0f`) and `TrySit`'s own entry gate are the *same* condition. Rather than
copy the expression into `TrySit` (a classic two-copies-silently-drift
risk), this plan factors it into one private helper both methods call —
explicitly named here so a reviewer knows this refactor is in scope and
deliberate, not a byproduct.

**9. `Sitting`'s only outgoing transition is `IsWalking==true`→`Walk`.** See
Section 0's topology finding above — Attack/Jump/Defend are code-blocked
while `_isSitting` is true, so unlike `LookUp`/`LookDown`, `Sitting` needs
no mirrored pass-through transitions toward `Attack`/`JumpAnticipation`/
`DefendRaise`; those triggers/bools structurally cannot fire while seated.

**10. Spawn-time auto-sit is a one-shot check in `Update()`, not a new
Inspector flag, and its narrow frame-0 timing risk is named explicitly
rather than hidden.** A private `_hasCheckedInitialSpawnSit` bool (default
`false`) is consumed exactly once, at the very top of `Update()`: if unset,
mark it set, then — if `IsNearBench` is already `true` and the character
isn't dead — call the same `TrySit` used for the real W-press path. This
deliberately does **not** live in `Awake()`/`Start()`: per Unity's documented
per-frame execution order, `Start()` runs once, before the very first
`FixedUpdate`, and `Bench`'s `OnTriggerEnter2D` (which sets `IsNearBench`)
fires during the physics step — so `IsNearBench` would still read its
default (`false`) if checked from `Start()`. Checking from `Update()`
instead (which runs after at least one `FixedUpdate` in the overwhelming
majority of real frame timings) gives `IsNearBench` its correct spawn-time
value in practice. **Named, accepted edge case**: in the narrow scenario
where the very first frame's `Update()` genuinely runs before any
`FixedUpdate` has processed (a real, if rare, Unity timing possibility),
the one-shot check would consume itself against a stale `false` and the
player would spawn standing, requiring one manual W-press — this is the
same class of frame-0 uncertainty this codebase already accepts for
`IsGrounded`'s own defaulted-true value (its own doc comment: "for the brief
window before the first `FixedUpdate` runs"). Not solved preemptively
(YAGNI) — if a live playtest (Task 6.4) actually observes a standing spawn,
the fix is to retry the check across the first few frames instead of
exactly once, at that point.

---

## Phase 1 — Reference art → build-ready sprites

#### Task 1.1: [GAMEPLAY] Extend the reference-sprite importer with a region-based pre-crop overload
**Depends on:** none
**Parallel:** yes — with Task 3.1, Task 4.1, Task 4.2, Task 5.1 (each is
independent of this task, directly or transitively). **Not** parallel with
Task 1.2, Task 1.3, Task 2.1, Task 3.2, or Task 5.2 — each of those depends
on this task, directly (1.2) or transitively (1.3 ← 1.2; 2.1 ← 1.3; 3.2 ←
2.1; 5.2 ← 1.3), so "Phases 2–5" as a blanket claim was wrong.
**Touches:** `Assets/Editor/Tools/QuirrelReferenceSpriteImporter.cs`,
`Assets/Editor/Tools/QuirrelReferenceSpriteImporterSelfTest.cs`
**Regression risk:** additive only — `CropPixels`, `BuildOutputPixels`,
`TryComputeContentBoundingBox`, `ResizeBilinear`, and
`QuirrelSpriteKnockout.KnockoutAndFeather` are all reused **unmodified**;
this task must not edit any of their existing bodies or the existing
self-test's existing cases.

Implementation, in words: a new public overload,
`BuildOutputPixelsFromRegion(sourcePixels, sourceWidth, sourceHeight,
regionXMin, regionYMin, regionXMax, regionYMax, targetContentHeight,
whiteThreshold, out outputWidth, out outputHeight)`, that calls the
**existing, unmodified** `CropPixels` once with the caller-supplied region
(isolating Quirrel from most/all of the visible bench in the composite),
then delegates entirely to the **existing, unmodified** `BuildOutputPixels`
on that sub-buffer (which itself still auto-tightens the bbox, resizes, and
knocks out — no duplicated logic). A matching file-based entry point
(`CropRegionResizeAndKnockout`, mirroring the existing
`CropResizeAndKnockout`'s signature plus the four region ints) and a sibling
menu command (`Tools/Quirrel/Crop Region, Resize And Knockout Reference
Sprite...`) that prompts for the source/output/target-height (reusing the
existing `TargetHeightPromptWindow` UX) plus four additional int fields for
the region rectangle.

**Acceptance criteria:**
- [ ] `BuildOutputPixelsFromRegion` produces byte-identical output to
      manually calling `CropPixels` then `BuildOutputPixels` in sequence —
      confirmed by a new self-test case
- [ ] New self-test case: a synthetic canvas with Quirrel's synthetic
      "island" shape plus a second, disconnected dark "contaminant" shape
      well outside the supplied region — the region overload's output
      contains no trace of the contaminant, proving the pre-crop actually
      excludes out-of-region content before the existing bbox-autodetect
      step ever runs
- [ ] `CropPixels`, `BuildOutputPixels`, `TryComputeContentBoundingBox`,
      `ResizeBilinear`, and `QuirrelSpriteKnockout.KnockoutAndFeather` are
      byte-for-byte unmodified — **regression check**
- [ ] The existing `QuirrelReferenceSpriteImporterSelfTest` cases (from plan
      007) still pass unmodified — **regression check**
- [ ] New menu command prompts for and correctly threads through all four
      region ints plus the existing source/output/target-height fields

---

#### Task 1.2: [ART] Measure the scale ratio; run the tool against both images; accept/reject; promote
**Depends on:** Task 1.1
**Parallel:** no
**Touches:** `Assets/Sprites/Reference/Bench.png`,
`Assets/Sprites/Reference/Quirrel_On_Bench.png` (read-only sources, not
modified); creates `Assets/Sprites/Environment/Bench/**`,
`Assets/Sprites/Quirrel/Sitting/**` (new)
**Regression risk:** none (new system) — future regression surface: any
later re-generation of either sprite must go back through this same
tool/process, same caveat every prior sprite-pipeline plan carries.

Follow Decision 5's method exactly: measure `benchHeightInComposite` and
`quirrelSeatedHeightInComposite` within `Quirrel_On_Bench.png`, cross-check
against `Bench.png`'s own landmark coordinates, choose
`targetSittingHeightPx` against the existing `Idle` roster, derive
`targetBenchHeightPx`. Run the **existing, unmodified** tool against
`Bench.png` alone (single subject, no wrinkle) at `targetBenchHeightPx`.
Run Task 1.1's new region overload against `Quirrel_On_Bench.png` at
`targetSittingHeightPx`, with a manually-chosen pre-crop rectangle bounding
Quirrel's own silhouette **and the full extent of his held pin** (the pin
resting across his lap is part of the intended output, per this plan's own
description of the seated pose — it is not stray background) — tightened to
exclude as much *unrelated* visible bench material (armrests, backrest,
seat slats not directly behind/under the pin) as the pin's own footprint
allows. Per the finding above (`KnockoutAndFeather`'s flood fill has no
shape/connectivity awareness), any bench material that falls directly
behind or around the pin (inside the pin's own bounding footprint), or in
the negative space between parts of Quirrel's own silhouette that one
rectangle cannot separate him from (e.g. the bench seat slats visible
between his two dangling legs), cannot be removed by this tool and is an
accepted exception — see the acceptance criteria below for the exact two
named zones. Output to the scratchpad first (off-tree iteration, matching
plan 002/007's discipline).

**Acceptance criteria:**
- [ ] Bench/Quirrel landmark cross-check (Decision 5) lands within a few px
      between the two source files — if not, re-measure directly from the
      composite alone before proceeding, and note which method was used
- [ ] Both outputs visually inspected at ≥400% zoom against a contrasting
      checkerboard: no white halo/fringe at edges (same discipline as plan
      002/007)
- [ ] **New criterion, specific to this composite-sourced sprite**: the
      `Quirrel_Sitting` output contains no residual bench-fragment
      contamination (stray dark scrollwork/slat lines baked in as opaque
      pixels) **outside the two named exception zones below** — if found,
      tighten Task 1.1's pre-crop rectangle and rerun; do not hand-patch the
      output outside the tool.

      **Named, accepted exception — two zones, same underlying reason:**
      - **Zone 1 (pin-coincident):** opaque bench material directly
        coincident with/behind the pin's own extent (e.g. seat-slat lines
        the blade rests across).
      - **Zone 2 (body-gap-coincident):** opaque bench material visible in
        the negative space between parts of Quirrel's own silhouette that
        one rectangle cannot separate him from — concretely, the bench seat
        slats visible between his two dangling legs (confirmed present in
        `Quirrel_On_Bench.png` by direct inspection), and any equivalent gap
        between hair-strand tips.

      Both zones share the same root cause: `KnockoutAndFeather`'s
      border-connected, near-white-only flood fill (`FloodFillBackground`)
      cannot reach non-white pixels that aren't connected to the canvas
      border, and no rectangle edit can exclude a gap sitting between two
      things that must both stay in the crop. Any stray bench fragment
      **outside both zones** (e.g., scrollwork beside Quirrel's shoulder
      once the rectangle is tightened) still fails this criterion and still
      triggers a tighten-and-rerun cycle
- [ ] Both outputs' color values spot-checked against `ART.md` §2.2's
      character base-tone table and §2.3's pin-head table (Verdant
      `#6FA25C`) — any deviation corrected or flagged before promotion
- [ ] Side-by-side scale check in Unity's Scene/Sprite Editor view: Quirrel-
      sitting reads at a proportionally correct seated scale relative to
      `Bench_01`, consistent with `Quirrel_On_Bench.png` and the mood-board
      reference (`Docs/UserRequirements/Quirrel_resting_on_bench.png`) —
      legs bent, feet near ground, back against the backrest
- [ ] Only the 2 final accepted PNGs are promoted, to
      `Assets/Sprites/Environment/Bench/Bench_01.png` and
      `Assets/Sprites/Quirrel/Sitting/Quirrel_Sitting_01.png` — no throwaway
      PNGs committed to `Assets/`
- [ ] The two source reference files are left completely unmodified —
      **regression check**

---

#### Task 1.3: [ART] Unity sprite import configuration for both new sprites
**Depends on:** Task 1.2
**Parallel:** no
**Touches:** none (new system) — new `.meta` files only

Mirrors the existing per-frame convention exactly (verified directly
against `Assets/Sprites/Quirrel/Idle/Quirrel_Idle_01.png.meta`): Sprite Mode
Single, PPU 100, Pivot Bottom (0.5, 0), Max Size 256, Filter Bilinear, sRGB
on, Alpha Is Transparency on — for **both** `Quirrel_Sitting_01.png` and
`Bench_01.png` (the prop sprite uses the identical baseline; no
per-object-type deviation is warranted for a single static sprite).

**Acceptance criteria:**
- [ ] Both new sprites: Sprite Mode Single, PPU 100, Pivot Bottom (0.5, 0),
      Max Size 256, Filter Bilinear, sRGB on, Alpha Is Transparency on
- [ ] Visual spot-check: `Quirrel_Sitting_01`'s lowest visible pixel (feet)
      sits at the sprite's local y=0; `Bench_01`'s lowest visible pixel
      (leg feet) sits at its own local y=0
- [ ] Both sprites live in their own folders
      (`Assets/Sprites/Quirrel/Sitting/`,
      `Assets/Sprites/Environment/Bench/`), matching the established
      per-state/per-object folder convention

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] tool overload ──→ 1.2 [ART] measure + run + accept/promote ──→ 1.3 [ART] import config
```

---

## Phase 2 — Animation Clip [ART]

#### Task 2.1: [ART] `Quirrel_Sitting` AnimationClip
**Depends on:** Task 1.3
**Parallel:** yes — with Phase 3 (topology half only), Phase 4, Phase 5
**Touches:** none (new system)

Single-frame, duplicate-keyframed, Loop Time **on** — identical technique
to `Quirrel_DefendHold`/`Quirrel_Hurt`/`Quirrel_LookUp` (all "held while a
condition is true" poses).

**Acceptance criteria:**
- [ ] `Quirrel_Sitting`: `Quirrel_Sitting_01` only, duplicate-keyframed to a
      short non-zero length (e.g. 0.1s, matching the `DefendHold`/`LookUp`
      convention), Loop Time on
- [ ] Clip name exactly matches what Task 3.2 assigns by name
      (`Quirrel_Sitting`) — no typo/casing drift
- [ ] Plays back cleanly in the Animation window preview (no missing-sprite
      warnings)

---

**Phase 2 dependency graph:**
```
1.3 [ART] import config ──→ 2.1 [ART] Quirrel_Sitting clip
```

---

## Phase 3 — Animator Controller [GAMEPLAY]

#### Task 3.1: [GAMEPLAY] Add `IsSitting` parameter, `Sitting` state, and its 2 transitions
**Depends on:** none — states/parameters/transitions don't require the
AnimationClip asset to exist, only the final Motion assignment does (same
reasoning plan 002/007 used)
**Parallel:** yes — with Phases 1, 2, 4, 5
**Touches:** `Assets/Animations/Quirrel.controller` (adds 1 parameter, 1
state, 2 transitions; **does not modify** any existing state's Motion
field, any existing parameter, or any of the 3 existing Any-State
transitions, or `Idle`'s existing 5 outgoing transitions)
**Regression risk:** shared, already-load-bearing asset. The one place this
task can genuinely regress existing behavior: `Idle`'s **existing** 5
outgoing transitions (`IsWalking`→Walk, `JumpTrigger`→JumpAnticipation,
`DefendHeld`→DefendRaise, `LookingUp`→LookUp, `LookingDown`→LookDown) must
keep their exact current order, conditions, and timing — the 1 new
transition is **appended after** them, never inserted before or between.

Add 1 Bool parameter: `IsSitting` (default `false`). Add 1 state: `Sitting`
(Motion left empty — Task 3.2's job). `Idle`'s transition list becomes
(existing `[1]`–`[5]` byte-identical to today, `[6]` new, appended last):
`[6]` `IsSitting==true`→`Sitting` (duration 0, no exit time — matching the
`DefendHeld`/`LookingUp`/`LookingDown` transitions' own instant-swap timing
profile, per Decision 9's finding that this is also an instant cosmetic
state change).

`Sitting`'s own transition list (1 total, per Decision 9 — no mirrored
pass-throughs needed since Attack/Jump/Defend are code-blocked while
seated): `[1]` `IsWalking==true`→`Walk`, timed identically to `Idle`'s own
`[1]` `Walk` transition (`TransitionDuration 0.1`, `ExitTime 0.75`,
`HasExitTime 0`).

No changes to `Walk`, `JumpAnticipation`, `JumpRise`, `JumpFall`, `Attack`,
`DefendRaise`, `DefendHold`, `Hurt`, `Die`, `LookUp`, or `LookDown` — and no
new transition *into* `Sitting` from any state other than `Idle`.

**Acceptance criteria:**
- [ ] `IsSitting` Bool parameter added, default `false`
- [ ] `Sitting` state added (Motion left empty, assigned in Task 3.2)
- [ ] `Idle`'s 5 existing outgoing transitions are byte-identical in order,
      condition, and timing to their pre-plan state — **regression check**
- [ ] `Idle` gains exactly 1 new outgoing transition, appended after the
      existing 5: `IsSitting==true`→`Sitting`, duration 0, no exit time
- [ ] `Sitting` has exactly 1 outgoing transition: `IsWalking==true`→`Walk`,
      timing matching `Idle`'s own `Walk` transition exactly
- [ ] No new transition exists from `Walk`, `JumpAnticipation`, `JumpRise`,
      `JumpFall`, `Attack`, `DefendRaise`, `DefendHold`, `Hurt`, `Die`,
      `LookUp`, or `LookDown` into `Sitting`, and none of those states'
      existing transitions were altered — **regression check**
- [ ] The 3 existing Any-State transitions are unmodified — confirmed they
      apply to `Sitting` automatically with zero additional wiring

---

#### Task 3.2: [GAMEPLAY] Assign `Quirrel_Sitting` clip to the new state's Motion field
**Depends on:** Task 3.1, Task 2.1
**Parallel:** no
**Touches:** `Assets/Animations/Quirrel.controller` (Motion field on the
new state only)

**Acceptance criteria:**
- [ ] `Sitting` state's Motion = `Quirrel_Sitting` clip, by exact name
      cross-check
- [ ] Controller preview: scrubbing into `Sitting` in the Animator window
      shows the correct sprite, no missing-Motion warning
- [ ] No other state's Motion field touched — **regression check**

---

**Phase 3 dependency graph:**
```
3.1 [GAMEPLAY] topology (independent, parallel with Phases 1/2/4/5) ──┐
                                                                        ├──→ 3.2 [GAMEPLAY] motion assignment
2.1 [ART] clip ─────────────────────────────────────────────────────────┘
```

---

## Phase 4 — PlayerController wiring [GAMEPLAY]

#### Task 4.1: [GAMEPLAY] Core sit/stand mechanic — properties, `TrySit`, stand-up-on-walk, guards
**Depends on:** none for compiling (Animator `SetBool` on an unknown hash
silently no-ops at runtime — the same established pattern every other
parameter write in this file already relies on — so this task does not
functionally require Task 3.1 to have landed first, though the feature
isn't observable end-to-end until it has)
**Parallel:** yes — with Phases 1, 2, 3 in full (none of those tasks depend
on this one). **Not** parallel with Phase 5 (Task 5.1 explicitly depends on
this task, and Task 5.2 is downstream of Task 5.1) or with Task 4.2 (same
file, sequential regardless of dependency direction).
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (new properties,
new hash constant, new methods, 3 new call sites in `Update()`, additions
to the existing `TryAttack`/`TryJump`/`DefendHeld`/`UpdateLookState` guard
expressions)
**Regression risk:** the new call sites sit inside `Update()`, the single
highest-traffic method in this file — must not change the *order* or
*behavior* of any existing line, only add to it. The `IsIdleAndGrounded()`
refactor (Decision 8) touches `UpdateLookState`'s existing gate expression —
its resulting boolean value must be provably unchanged for every existing
`UpdateLookState` test.

Implementation, in words:
- One new hash constant: `IsSittingHash = Animator.StringToHash
  ("IsSitting")`.
- Two new properties: `public bool IsSitting { get; private set; }` (mirrors
  `IsAttacking`'s shape — code-owned latch); `public bool IsNearBench { get;
  set; }` (public setter, per Decision 4 — externally driven by `Bench`).
- New private helper `IsIdleAndGrounded()`: extracts the exact expression
  `UpdateLookState` already computes (`IsGrounded && !IsDead &&
  !IsFullyCommitted && _horizontalInput == 0f`) into one place, called by
  both `UpdateLookState` and the new `TrySit` (Decision 8).
- New method `public bool TrySit(bool isNearBench)`: mirrors
  `TryJump`/`TryAttack`'s exact shape (raw environmental input as a
  parameter, for EditMode testability without a live physics scene).
  No-ops (returns `false`) if `_isSitting` is already true, or
  `!IsIdleAndGrounded()`, or `!isNearBench`. On success: `_isSitting =
  true`, `_animator?.SetBool(IsSittingHash, true)`, returns `true`.
- New method `public void StandUpIfWalking(float horizontalInput)`: if
  `_isSitting && horizontalInput != 0f`, sets `_isSitting = false` and
  `_animator?.SetBool(IsSittingHash, false)`.
- Guard additions (Decision 2): `TryAttack`'s guard becomes `if (IsDead ||
  DefendHeld || _isAttacking || _isHurtStunned || _isSitting) return
  false;`. `TryJump`'s guard becomes `if (!isGrounded || _jumpInProgress ||
  IsDead || IsFullyCommitted || _isSitting) return false;`. `DefendHeld`'s
  computation becomes `DefendHeld = !IsDead && !_isHurtStunned && !_isSitting
  && IsGrounded && Input.GetKey(KeyCode.K);`. `UpdateLookState` now calls
  `IsIdleAndGrounded()` (which itself now includes `&& !_isSitting`) instead
  of recomputing the expression inline.
- Call sites in `Update()`: immediately after the existing A/D block,
  `StandUpIfWalking(_horizontalInput);` (Decision 2 — same-frame clear so
  later-evaluated guards this same frame already see `_isSitting == false`).
  After the existing `TryJump` block and before `UpdateLookState`
  (Decision 1): `if (Input.GetKeyDown(KeyCode.W)) { TrySit(IsNearBench); }`,
  followed by the existing (now `IsIdleAndGrounded()`-backed)
  `UpdateLookState(Input.GetKey(KeyCode.W), Input.GetKey(KeyCode.S));` call,
  unchanged in shape.

**Acceptance criteria:**
- [ ] With `IsGrounded` true, `IsDead`/`IsFullyCommitted` false,
      `_horizontalInput` at `0f`: `TrySit(true)` returns `true` and sets
      `IsSitting` true; `TrySit(false)` (not near a bench) returns `false`
      and leaves `IsSitting` false
- [ ] `TrySit(true)` called a second time while already sitting returns
      `false` (no-stack guard, mirroring `TryAttack`'s own no-stack
      precedent)
- [ ] With `IsFullyCommitted` true (via the existing `ForceSetDefendHeld`
      helper) or `IsGrounded` false (via `ForceSetIsGrounded`) or
      `_horizontalInput` nonzero (via `ForceSetHorizontalInput`): `TrySit
      (true)` returns `false`
- [ ] `StandUpIfWalking` clears `IsSitting` immediately when called with a
      nonzero input while sitting; is a no-op (does not throw, does not
      flip anything) when called while not sitting, or with zero input
      while sitting
- [ ] `TryAttack(true)` returns `false` while `IsSitting` is true (new
      guard); `TryJump(true)` returns `false` while `IsSitting` is true (new
      guard); `DefendHeld` reads `false` while `IsSitting` is true even with
      K forced held (via the existing `ForceSetDefendHeld`-style pattern
      applied to simulate the K read, or by directly asserting the computed
      value) — **new coverage, Task 6.1**
- [ ] `UpdateLookState(true, false)` returns both `LookingUp`/`LookingDown`
      false while `IsSitting` is true — **new coverage, Task 6.1**, and the
      concrete regression this guard prevents (Decision 1)
- [ ] All existing `PlayerControllerTests` (pre-this-plan) still pass
      unmodified, including every existing `UpdateLookState` test — proves
      the `IsIdleAndGrounded()` extraction is behavior-preserving —
      **regression check**
- [ ] Existing `Update()` behavior (A/D, J, K, Space, W/S handling, facing,
      existing Animator parameter writes) is otherwise byte-identical aside
      from the 3 new call sites — **regression check**

---

#### Task 4.2: [GAMEPLAY] Spawn-time auto-sit + `Hurt()`/`Die()` force-clears
**Depends on:** Task 4.1
**Parallel:** no — same file as 4.1
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (one new field,
one new method, one new call site at the top of `Update()`, small additions
inside the existing `Hurt()`/`Die()` bodies)
**Regression risk:** `Hurt()`/`Die()` already have several sequential,
order-sensitive resets (plan 002 §1.9) — the new `IsSitting` clear must be
added alongside the existing `DefendHeld = false;`/`LookingUp = false;`
lines, not interleaved in a way that could shadow an existing reset.

Implementation, in words: new private field `_hasCheckedInitialSpawnSit`
(default `false`). New method `public void CheckInitialSpawnSit()`
(Decision 10): idempotent past its first call — if
`_hasCheckedInitialSpawnSit` is already true, no-ops; otherwise sets it
true, and if `IsNearBench && !IsDead`, calls `TrySit(true)`. Called once, at
the very top of `Update()`, before the existing A/D block. `Hurt()`:
alongside the existing `DefendHeld = false;` / `LookingUp = false;` /
`LookingDown = false;` lines, add `_isSitting = false;` and (inside the
existing `if (_animator != null)` block) `_animator.SetBool(IsSittingHash,
false);`. `Die()`: identical additions, mirroring `Hurt()`'s for symmetry.

**Acceptance criteria:**
- [ ] `CheckInitialSpawnSit()` called once with `IsNearBench` true and
      `IsDead` false: `IsSitting` becomes true
- [ ] `CheckInitialSpawnSit()` called a second time (regardless of
      `IsNearBench`'s value at that point) is a no-op — proves the one-shot
      consumption, not a re-checked condition
- [ ] `CheckInitialSpawnSit()` called once with `IsNearBench` false: leaves
      `IsSitting` false, and a *second* call (even with `IsNearBench` now
      forced true) still leaves it false — proves the check is genuinely
      one-shot, not "the first time the condition holds"
- [ ] `Hurt()` called while `IsSitting` is true immediately clears
      `IsSitting` — not merely on the next `Update()` tick
- [ ] `Die()` does the same
- [ ] `Hurt()`/`Die()`'s existing reset ordering and all their existing
      acceptance criteria from plan 002 §1.9 and plan 007 remain true — no
      existing reset was reordered or removed — **regression check**

---

**Phase 4 dependency graph:**
```
4.1 [GAMEPLAY] core mechanic (independent, parallel with Phases 1/2/3 — NOT Phase 5, which depends on it) ──→ 4.2 [GAMEPLAY] spawn + interrupts
```

---

## Phase 5 — Bench component + scene setup [GAMEPLAY]

#### Task 5.1: [GAMEPLAY] `Bench` MonoBehaviour + new `Quirrel.Environment` assembly
**Depends on:** Task 4.1 (needs `PlayerController.IsNearBench` to exist)
**Parallel:** yes — with Phases 1, 2, 3 in full; with Task 4.2 (different
concern, though both eventually land in the same scene)
**Touches:** none existing — creates
`Assets/Scripts/Environment/Quirrel.Environment.asmdef` (new, references
`Quirrel.Player`), `Assets/Scripts/Environment/Bench.cs` (new)
**Regression risk:** none (new assembly, new file) — the only shared-asset
risk is asmdef reference correctness (must reference `Quirrel.Player` for
`GetComponent<PlayerController>()` to resolve).

Implementation, in words: `Bench` requires a `BoxCollider2D` component
(`[RequireComponent(typeof(BoxCollider2D))]`, mirroring
`PlayerController`'s own `[RequireComponent(typeof(Rigidbody2D))]`
convention) and, in `Awake()`, asserts/ensures that collider's
`isTrigger` is `true` (defensive — the scene-setup task, 5.2, is
responsible for the actual Inspector value, but this catches a misconfigured
prefab/instance loudly rather than silently allowing a solid Bench). Thin
Unity-message wrappers forward to testable public methods, matching this
codebase's established pattern (`Update()`/`FixedUpdate()` calling
public, parameter-driven methods elsewhere):
`OnTriggerEnter2D(Collider2D other) => HandleTriggerEnter(other);`,
`OnTriggerExit2D(Collider2D other) => HandleTriggerExit(other);`, where
`public void HandleTriggerEnter(Collider2D other)` does
`other.GetComponent<PlayerController>()?.IsNearBench = true` (no serialized
Player reference — Decision 7) and `HandleTriggerExit` the mirror,
setting `false`.

**Acceptance criteria:**
- [ ] `Bench` requires a `BoxCollider2D` component
- [ ] `HandleTriggerEnter`/`HandleTriggerExit` are public and take a
      `Collider2D` parameter (EditMode-testable without a live physics
      scene, same convention as every other testable method in this
      codebase)
- [ ] Given a fake `GameObject` with a `PlayerController` component,
      `HandleTriggerEnter` sets that controller's `IsNearBench` true;
      `HandleTriggerExit` sets it false
- [ ] Given a `Collider2D` with **no** `PlayerController` component (e.g., a
      stray non-player trigger), both methods no-op without throwing
- [ ] `Quirrel.Environment.asmdef` references `Quirrel.Player`; no other
      existing asmdef is modified — **regression check**

---

#### Task 5.2: [GAMEPLAY] Scene setup — add the `Bench` GameObject to `SampleScene.unity`
**Depends on:** Task 1.3 (needs the real sprite dimensions to size the
trigger collider), Task 5.1
**Parallel:** no — edits the shared scene file; must not run alongside any
other task touching `SampleScene.unity` (none currently do in this plan,
but flagged per this project's "same scene/prefab → never parallel" rule)
**Touches:** `Assets/Scenes/SampleScene.unity` (adds 1 new GameObject; does
not modify `Player`, `Ground`, `Main Camera`, or any existing GameObject's
Transform/component values)
**Regression risk:** shared scene file — the one thing this task must not
do is move `Player`'s spawn Transform (`{0,0,0}`) or touch any existing
GameObject.

Add a new `Bench` GameObject: `SpriteRenderer` (sprite = `Bench_01`,
`sortingOrder = -1` — explicit, so it reliably renders behind `Player`
regardless of any ambiguity in the project's Transparency Sort Mode
settings, since neither `Player` nor `Ground` currently rely on anything
but the implicit default), `BoxCollider2D` (`isTrigger = true` — **never**
add a second, non-trigger collider), and the new `Bench` component.
Position at world `{0, 0, 0}` (Bottom pivot places it exactly on the shared
ground line, and colocates it with `Player`'s own spawn point, satisfying
"the game starts seated"). Size the `BoxCollider2D` to the actual imported
`Bench_01` sprite's rendered width (read from the Sprite Editor/Inspector
once Task 1.3 lands), height 3 units (Decision 3), Y-offset centered on
that 3-unit span above the ground line.

**Acceptance criteria:**
- [ ] `Bench` GameObject exists in `SampleScene.unity`, positioned at
      `{0, 0, 0}`
- [ ] `SpriteRenderer` shows `Bench_01`, `sortingOrder = -1`
- [ ] `BoxCollider2D.isTrigger == true` — **regression check** (this must
      never flip to a solid collider, or Quirrel would be physically
      blocked from walking through the bench's footprint)
- [ ] Trigger collider's width covers the bench sprite's full rendered
      footprint; height (3 units) comfortably exceeds the Player's own
      1.31-unit collider height
- [ ] `Player`'s spawn Transform, `Ground`'s Transform, and `Main Camera`'s
      Transform are byte-identical to their pre-plan values — **regression
      check**
- [ ] In the Editor (or via the manual playtest pass, Task 6.4): standing
      anywhere along the bench's horizontal footprint (not centered) and
      walking through it produces no physical blocking/snagging

---

**Phase 5 dependency graph:**
```
4.1 [GAMEPLAY] ──→ 5.1 [GAMEPLAY] Bench component ──┐
1.3 [ART] sprites ─────────────────────────────────┼──→ 5.2 [GAMEPLAY] scene setup
                                                     ┘
```

---

## Phase 6 — QA

#### Task 6.1: [QA] `PlayerControllerTests` coverage for the sit/stand mechanic
**Depends on:** Task 4.1, Task 4.2
**Parallel:** yes — with Task 6.2 (different file), Task 6.3 (different
assembly)
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`
**Regression risk:** additive only.

**Work:**
- `TrySit_WhileIdleGroundedAndNearBench_Succeeds`
- `TrySit_WhileNotNearBench_Fails`
- `TrySit_WhileAlreadySitting_DoesNotRefire` (no-stack guard, mirroring
  `TryAttack_WhileAlreadyAttacking_DoesNotRefire`)
- `TrySit_WhileFullyCommitted_Fails` (via `ForceSetDefendHeld`)
- `TrySit_WhileNotGrounded_Fails` (via `ForceSetIsGrounded`)
- `TrySit_WhileHorizontalInputNonzero_Fails` (via `ForceSetHorizontalInput`)
- `StandUpIfWalking_ClearsSittingImmediately_WhenHorizontalInputNonzero`
- `StandUpIfWalking_NoOp_WhenNotSitting`
- `StandUpIfWalking_NoOp_WhenSittingButNoHorizontalInput`
- `TryAttack_WhileSitting_IsIgnored` (new guard)
- `TryJump_WhileSitting_IsIgnored` (new guard)
- `DefendHeld_WhileSitting_ReadsFalse_EvenWithKHeld`
- `UpdateLookState_WhileSitting_BothLookFlagsFalse` (the concrete Decision 1
  regression pin)
- `CheckInitialSpawnSit_WhenNearBenchAtFirstCall_EntersSitting`
- `CheckInitialSpawnSit_SecondCall_IsNoOp_RegardlessOfIsNearBench`
- `CheckInitialSpawnSit_WhenNotNearBenchAtFirstCall_NeverEntersSittingEvenIfNearBenchLater`
- `Hurt_WhileSitting_ImmediatelyClearsIsSitting`
- `Die_WhileSitting_ImmediatelyClearsIsSitting`

**Acceptance criteria:**
- [ ] All new tests pass
- [ ] No test calls `Input.GetKey`/`GetKeyDown` directly — all input
      simulation goes through `TrySit`/`StandUpIfWalking`/
      `CheckInitialSpawnSit`'s own parameters, or the existing
      `ForceSetDefendHeld`/`ForceSetIsGrounded`/`ForceSetHorizontalInput`
      helpers
- [ ] Full existing `PlayerControllerTests` suite (all pre-existing tests)
      still passes unmodified — **regression check**

---

#### Task 6.2: [QA] Extend `AnimatorContractTests` for `IsSitting` and its transitions
**Depends on:** Task 3.1, Task 3.2, Task 4.1
**Parallel:** yes — with Task 6.1 (different file), Task 6.3 (different
assembly)
**Touches:** `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs`
**Regression risk:** the existing `ExpectedParameters` array (12 rows) must
be **appended to, not replaced**.

**Work:**
- Append 1 row to `ExpectedParameters`: `("IsSittingHash", "IsSitting",
  AnimatorControllerParameterType.Bool)` — the existing
  `Controller_HasExactly10Parameters...` test's length assertion
  self-adjusts to 13 with no further change needed (same self-adjusting
  property plan 007 already documented for this test).
- Update `IdleState_HasExactlyFiveOutgoingTransitions_InOrder_
  IncludingLookUpAndLookDown` (renamed, or a new test alongside it — see
  acceptance criteria) to assert `Idle`'s 6 transitions in order, with the
  6th being `[Sitting, IsSitting==true]`.
- New test `SittingState_HasExactlyOneOutgoingTransition_ToWalk` —
  asserting `[Walk, IsWalking==true]`.

**Acceptance criteria:**
- [ ] All 12 pre-existing `ExpectedParameters` rows are byte-identical to
      today — **regression check**
- [ ] `Idle`'s transition-order test now asserts 6 transitions, with `[1]`–
      `[5]` byte-identical to today's assertions (order, condition,
      destination) and `[6]` the new `IsSitting==true`→`Sitting` — whether
      by editing the existing test in place (updating its name/count) or
      adding a new test that supersedes it is a call the implementing agent
      can make, but not both left asserting conflicting counts
- [ ] The new `Sitting` transition test passes
- [ ] The existing `AttackState_...`, `LookUpState_...`, `LookDownState_...`
      transition-order tests still pass unmodified — **regression check**
- [ ] The existing `Controller_ContainsJumpAnticipationState...` test still
      passes unmodified — **regression check**

---

#### Task 6.3: [QA] `Bench` EditMode tests (new `Quirrel.Environment.EditModeTests` assembly)
**Depends on:** Task 5.1
**Parallel:** yes — with Task 6.1, Task 6.2 (different assembly)
**Touches:** none existing — creates
`Assets/Scripts/Environment/Tests/EditMode/Quirrel.Environment.EditModeTests.asmdef`
(new, mirroring `Quirrel.Player.EditModeTests`'s/`Quirrel.Camera.
EditModeTests`'s exact shape: references `Quirrel.Environment`,
`Quirrel.Player`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`;
`nunit.framework.dll` precompiled reference; `Editor`-only platform;
`UNITY_INCLUDE_TESTS` define constraint), `Assets/Scripts/Environment/
Tests/EditMode/BenchTests.cs` (new)

**Work:**
- `HandleTriggerEnter_WithPlayerController_SetsIsNearBenchTrue`
- `HandleTriggerExit_WithPlayerController_SetsIsNearBenchFalse`
- `HandleTriggerEnter_WithoutPlayerController_DoesNotThrow`
- `HandleTriggerExit_WithoutPlayerController_DoesNotThrow`

**Acceptance criteria:**
- [ ] All 4 tests pass
- [ ] New asmdef mirrors the existing `Quirrel.Player.EditModeTests`/
      `Quirrel.Camera.EditModeTests` shape exactly (same reference list
      pattern, same platform/define-constraint settings)
- [ ] No existing asmdef is modified — **regression check**

---

#### Task 6.4: [QA] Manual playtest protocol addendum + `ART.md` doc sync
**Depends on:** Tasks 1.3, 2.1, 3.2, 4.2, 5.2, 6.1, 6.2, 6.3 (needs the
whole feature working end-to-end, plus the automated suites green, before a
live pass is meaningful)
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md`, `ART.md` §7

**Regression risk:** none to code — documentation only. Real `Bench`
trigger-callback timing (including Decision 10's named frame-0 edge case)
is exactly the class of behavior the automated suite structurally cannot
verify — a live pass is the only real proof.

Add a new section, following the existing `5a`/`5b`/`5c` precedent exactly
— call it **`5d. Bench sit mechanic (Docs/Plans/008_bench-sit-mechanic.
md)`** — with checklist rows for: the game starts with Quirrel already
seated on the bench (Decision 10 — note if this ever fails, i.e. the
character spawns standing, per the named frame-0 edge case, and whether a
single manual W-press then correctly sits him); standing anywhere along the
bench's footprint (not centered) and pressing W sits Quirrel; pressing W
while idle but **not** near the bench still shows the ordinary look-up pose
(regression check against plan 007's existing feature); pressing A or D
while seated stands Quirrel up and he walks immediately, with no lingering
seated frame; Attack (J), Jump (Space), and Defend (K) all do nothing while
seated (three separate rows); `Hurt()`/`Die()` (via the existing Section 0
trigger method) interrupt sitting immediately, with no lingering seated
frame; walking across the bench's horizontal footprint without stopping
produces no physical snagging/blocking (regression check against Task
5.2's collider-is-a-trigger requirement); the camera's W/S pan (`5b`) still
works normally while seated (Decision 2 — confirms this deliberate,
non-obvious choice reads correctly, not as a bug).

Update `ART.md` §7's Animation Timing table: add one row — `Sitting | 1 |
Cosmetic idle-only overlay; entered via W near the bench prop while idle
and grounded, or automatically at spawn if already seated; blocks
Attack/Jump/Defend at the code level until the player walks away`.

**Acceptance criteria:**
- [ ] New `5d` section added with all rows listed above, following the
      `5a`/`5b`/`5c` format exactly
- [ ] Live verification performed and recorded PASS/FAIL for every new
      `5d` row
- [ ] Any FAIL is logged as a bug per this doc's existing bug-report format
      and routed back through the pipeline, not silently patched inside
      this task
- [ ] `ART.md` §7's Animation Timing table has exactly one new row added;
      no other `ART.md` content altered — **regression check**
- [ ] Re-confirm the existing `5a`/`5b`/`5c` sections still read correctly —
      **regression check**

---

**Phase 6 dependency graph:**
```
4.1, 4.2 ──→ 6.1 [QA] PlayerController tests ─────┐
3.1, 3.2, 4.1 ──→ 6.2 [QA] AnimatorContractTests ─┼──→ 6.4 [QA] manual playtest + ART.md sync
5.1 ──→ 6.3 [QA] Bench tests ──────────────────────┘
(6.1/6.2/6.3 mutually parallel — three different files/assemblies)
```

---

## Explicitly out of scope for this plan

Any Health/HP field, save state, or "sitting restores health" hook of any
kind (no health system exists anywhere in this codebase yet — confirmed by
direct read; this is deliberately deferred to a future plan once that
system is designed, per the user's own instruction not to add speculative
hooks); any glow/VFX on the bench (§2.4's reserved `#F6E7C7` stays reserved,
unused); a multi-bench save-point registry (Decision 7); any change to
`CameraFollow`'s gating condition (Decision 2 — a named judgment call, not
a hard requirement either way); any Input System migration (still legacy
`KeyCode`/`Input.GetKey`); any tags/physics-layers/collision-matrix change
(Decision 3 avoids needing one); any save/ScriptableObject schema; sprite
atlasing/packing; any change to the existing 24-frame sheet or its
existing clips/states/transitions beyond the additive edges named in Phase
3.

---

## Judgment calls made explicit

1. Sitting is not folded into `IsFullyCommitted` — four call sites get an
   individual, explicit `!_isSitting` addition instead, so A/D keeps
   working to stand up (Decisions 2).
2. The camera's W/S pan is **not** newly blocked while seated —
   `CameraFollow`'s gate is untouched by design, an explicit, named,
   reversible-if-wrong choice, not a hard requirement (Decision 2).
3. Bench proximity is a `Bench`-owned trigger collider using
   `GetComponent<PlayerController>()`, not a new physics layer + `Player`-
   owned `OverlapBox` polling — avoids a `ProjectSettings`/collision-matrix
   change entirely (Decision 3).
4. `IsNearBench` is `{ get; set; }`, mirroring `IsDead`'s exact shape,
   rather than `{ get; private set; }` plus a reflection helper (Decision
   4).
5. Scale derivation measures both quantities from the same composite image,
   with an explicit cross-check against the standalone `Bench.png` before
   trusting the "same photo/framing" assumption (Decision 5).
6. `Hurt()`/`Die()` force-clear `IsSitting`, extending an existing
   interrupt-safety contract (plan 007) rather than adding new architecture
   (Decision 6).
7. One `Bench` component, not multi-bench-proofed — a named, accepted
   limitation for whoever adds a second bench later (Decision 7).
8. `TrySit` and `UpdateLookState` share one `IsIdleAndGrounded()` helper
   rather than duplicating the same four-condition expression (Decision 8).
9. `Sitting`'s Animator topology is deliberately minimal (1 outgoing
   transition) because Attack/Jump/Defend are code-blocked while seated,
   unlike `LookUp`/`LookDown` (Decision 9).
10. Spawn-time auto-sit is a one-shot `Update()`-time check, with a named,
    accepted, narrow frame-0 timing edge case rather than a bespoke
    zero-frame guarantee — mirrors the same class of risk this codebase
    already accepts for `IsGrounded`'s own defaulted-true value (Decision
    10).

---

## Reference file paths consulted while drafting this plan

- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\CLAUDE.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\ART.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\002_quirrel-sprite-animation-player-control.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\002_manual-playtest-protocol.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\007_look-up-down-idle-animation-and-pan-tuning.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\PlayerController.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Tests\EditMode\PlayerControllerTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Tests\EditMode\AnimatorContractTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\CameraFollow.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Quirrel.Player.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\Quirrel.Camera.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Tests\EditMode\Quirrel.Player.EditModeTests.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\Tests\EditMode\Quirrel.Camera.EditModeTests.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Editor\Tools\QuirrelReferenceSpriteImporter.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Editor\Tools\QuirrelReferenceSpriteImporterSelfTest.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Editor\Tools\QuirrelSpriteKnockout.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Quirrel\Idle\Quirrel_Idle_01.png.meta`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Reference\Bench.png` (896×1174px, read directly)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Reference\Quirrel_On_Bench.png` (896×1174px, read directly)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\UserRequirements\Quirrel_resting_on_bench.png` (read directly, mood-board only)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scenes\SampleScene.unity` (Player/Ground/Main Camera Transform + component blocks, confirmed by direct read)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\` (directory listing — existing folder conventions)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\` (directory listing — existing per-system folder/asmdef conventions)
